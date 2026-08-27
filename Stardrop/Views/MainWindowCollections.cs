using Avalonia.Controls;
using SharpCompress.Archives;
using SharpCompress.Common;
using Stardrop.Models;
using Stardrop.Models.Data;
using Stardrop.Models.Data.Enums;
using Stardrop.Models.Nexus;
using Stardrop.Models.Nexus.Web;
using Stardrop.Utilities;
using Stardrop.Utilities.External;
using Stardrop.Utilities.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Stardrop.Views
{
    public partial class MainWindow : Window
    {
        /// <summary>Extensions AddMods is able to open, used to tell an archive apart from a loose file</summary>
        private static readonly string[] _archiveExtensions = new string[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz" };

        /// <summary>How many entries a list in the install summary names before it gives a count instead</summary>
        private const int _maxListedEntries = 5;
        // The lock window is opened from a timer, so a stretch of synchronous work has to hand the thread back for
        // long enough to let that timer run. Sits above the sentinel's own interval
        private const int _lockWindowYieldMilliseconds = 150;

        /// <summary>
        /// Handles an nxm collection link end to end: resolve the revision, pull the archive, read collection.json,
        /// install what can be installed and generate a profile for the result.
        /// </summary>
        private async Task<bool> ProcessCollectionLink(NXM nxmLink)
        {
            if (Nexus.Client is null || String.IsNullOrEmpty(nxmLink.Link))
            {
                await CreateWarningWindow(Program.translation.Get("ui.message.require_nexus_login"), Program.translation.Get("internal.ok"));
                return false;
            }

            if (NexusClient.TryParseCollectionNxmLink(nxmLink.Link, out var domainName, out var slug, out var revisionNumber) is false)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.message.failed_collection_get"), nxmLink.Link), Program.translation.Get("internal.ok"));
                return false;
            }

            Program.helper.Log($"Processing NXM link as a collection: {slug} revision {revisionNumber}");

            var revision = await Nexus.Client.GetCollectionRevision(slug, revisionNumber, domainName);
            if (revision is null || String.IsNullOrEmpty(revision.DownloadLink))
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.message.failed_collection_get"), nxmLink.Link), Program.translation.Get("internal.ok"));
                return false;
            }

            var collectionName = revision.Collection is null || String.IsNullOrEmpty(revision.Collection.Name) ? slug : revision.Collection.Name;
            if (Program.settings.IsAskingBeforeAcceptingNXM)
            {
                var requestWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.confirm_nxm_collection_install"), collectionName));
                if (await requestWindow.ShowDialog<bool>(this) is false)
                {
                    return false;
                }
            }

            // One source covers the archive fetch and the mod downloads, so a single cancel press stops the lot
            using var cancellationSource = new CancellationTokenSource();

            // The lock window only appears once SetLockState has run, as a sentinel timer creates it from that state.
            // UpdateLockWindow on its own does nothing, since it looks for a window that was never opened
            SetLockState(true, String.Format(Program.translation.Get("ui.message.collection_preparing"), collectionName), cancellationSource);

            var (index, extractedArchivePath) = await DownloadAndReadCollectionIndex(revision.DownloadLink, collectionName, cancellationSource.Token);
            if (index is null)
            {
                SetLockState(false);
                return false;
            }

            var resolvedRevision = revision.RevisionNumber is null ? (revisionNumber is null ? 0 : revisionNumber.Value) : revision.RevisionNumber.Value;
            var collection = Nexus.Client.CreateCollectionInstall(index, slug, resolvedRevision, domainName);

            await InstallCollection(collection, extractedArchivePath, cancellationSource);

            return true;
        }

        /// <summary>
        /// Fetches the collection archive and extracts it, returning the parsed collection.json.
        /// </summary>
        private async Task<(CollectionIndex? Index, string ExtractedPath)> DownloadAndReadCollectionIndex(string revisionDownloadLink, string collectionName, CancellationToken cancellationToken = default)
        {
            if (Nexus.Client is null)
            {
                return (null, String.Empty);
            }

            var archiveUri = await Nexus.Client.GetCollectionArchiveLink(revisionDownloadLink, EnumParser.GetDescription(Program.settings.PreferredNexusServer));
            if (String.IsNullOrEmpty(archiveUri))
            {
                SetLockState(false);
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.message.failed_collection_get_archive"), revisionDownloadLink), Program.translation.Get("internal.ok"));
                return (null, String.Empty);
            }

            var safeName = Pathing.GetSafePathSegment(collectionName);
            var archiveFileName = $"{safeName}.7z";
            var downloadResult = await Nexus.Client.DownloadFileAndGetPath(archiveUri, archiveFileName, cancellationToken);
            if (downloadResult.ResultKind is DownloadResultKind.UserCanceled)
            {
                // No warning, as the user triggered this intentionally
                return (null, String.Empty);
            }

            if (String.IsNullOrEmpty(downloadResult.DownloadedModFilePath))
            {
                SetLockState(false);
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.message.failed_collection_download_archive"), archiveUri), Program.translation.Get("internal.ok"));
                return (null, String.Empty);
            }

            Program.helper.Log($"Downloaded the collection archive to {downloadResult.DownloadedModFilePath}");

            var targetFolder = Path.Combine(Pathing.GetNexusPath(), safeName);
            Directory.CreateDirectory(targetFolder);

            try
            {
                using (var archive = ArchiveFactory.OpenArchive(downloadResult.DownloadedModFilePath))
                {
                    foreach (var entry in archive.Entries.Where(e => e.IsDirectory is false))
                    {
                        entry.WriteToDirectory(targetFolder, new ExtractionOptions() { ExtractFullPath = true, Overwrite = true });
                    }
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to extract the collection archive: {ex}", Helper.Status.Alert);
                SetLockState(false);
                await CreateWarningWindow(Program.translation.Get("ui.message.failed_read_collection_index"), Program.translation.Get("internal.ok"));
                return (null, String.Empty);
            }
            finally
            {
                // The archive has served its purpose once extracted and downloads use FileMode.CreateNew, so
                // leaving it here would make a second attempt at the same collection fail outright
                TryDelete(downloadResult.DownloadedModFilePath);
            }

            var indexPath = Path.Combine(targetFolder, "collection.json");
            if (File.Exists(indexPath) is false)
            {
                Program.helper.Log($"The collection archive did not contain a collection.json at {indexPath}", Helper.Status.Alert);
                SetLockState(false);
                await CreateWarningWindow(Program.translation.Get("ui.message.failed_read_collection_index"), Program.translation.Get("internal.ok"));
                return (null, String.Empty);
            }

            try
            {
                var index = JsonSerializer.Deserialize<CollectionIndex>(await File.ReadAllTextAsync(indexPath), new JsonSerializerOptions() { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true });
                if (index is null)
                {
                    SetLockState(false);
                    await CreateWarningWindow(Program.translation.Get("ui.message.failed_read_collection_index"), Program.translation.Get("internal.ok"));
                    return (null, String.Empty);
                }

                return (index, targetFolder);
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to parse the collection.json at {indexPath}: {ex}", Helper.Status.Alert);
                SetLockState(false);
                await CreateWarningWindow(Program.translation.Get("ui.message.failed_read_collection_index"), Program.translation.Get("internal.ok"));
                return (null, String.Empty);
            }
        }

        /// <summary>
        /// Downloads and installs everything the collection pins into its own folder, then generates a profile with
        /// exactly those mods enabled. Failures are gathered up and reported once at the end rather than per mod.
        /// </summary>
        private async Task InstallCollection(CollectionInstall collection, string extractedArchivePath, CancellationTokenSource cancellationSource)
        {
            var installPath = Pathing.GetCollectionInstallPath(collection.SourceId);
            Directory.CreateDirectory(installPath);

            // Every entry is downloaded into the collection's own folder, including mods the user already has
            // elsewhere. A second copy costs disk, while pointing at the user's copy puts that mod outside the only
            // folder the curator's configuration and ordering rules are ever written to
            //
            // Keyed by archive path, so AddMods can hand each installed mod back to the entry that requested it
            var entriesByArchive = new Dictionary<string, CollectionModEntry>(StringComparer.OrdinalIgnoreCase);
            var pendingMods = collection.Mods.Where(m => m.Status is CollectionModStatus.Pending).ToList();

            // Mod sizes in a collection vary by orders of magnitude, so counting mods makes the bar sit still through
            // one large download then jump. Bytes are tracked instead, falling back to counting when sizes are absent
            var totalDownloadSize = collection.GetPendingDownloadSize();
            var bytesByUri = new Dictionary<Uri, long>();
            var currentEntryName = String.Empty;
            int currentIndex = 0;

            void OnDownloadProgress(object? sender, ModDownloadProgressEventArgs e)
            {
                bytesByUri[e.Uri] = e.TotalBytes;
                UpdateCollectionProgress(currentEntryName, currentIndex, pendingMods.Count, bytesByUri.Values.Sum(), totalDownloadSize);
            }

            if (Nexus.Client is not null)
            {
                Nexus.Client.DownloadProgressChanged += OnDownloadProgress;
            }

            SetLockState(true, String.Format(Program.translation.Get("ui.message.collection_preparing"), collection.Name), cancellationSource);

            try
            {
                foreach (var entry in pendingMods)
                {
                    // Entries left Pending are the ones a resume would pick up, so nothing is marked here
                    if (cancellationSource.IsCancellationRequested)
                    {
                        Program.helper.Log($"Collection install for {collection.Name} was cancelled with {pendingMods.Count - currentIndex} mod(s) left to download");
                        break;
                    }

                    currentIndex++;
                    currentEntryName = entry.Name;
                    UpdateCollectionProgress(currentEntryName, currentIndex, pendingMods.Count, bytesByUri.Values.Sum(), totalDownloadSize);

                    var downloadedPath = await DownloadCollectionEntry(entry, extractedArchivePath, cancellationSource.Token);
                    if (String.IsNullOrEmpty(downloadedPath))
                    {
                        continue;
                    }

                    entry.SourceArchivePath = downloadedPath;
                    entriesByArchive[downloadedPath] = entry;
                }
            }
            finally
            {
                if (Nexus.Client is not null)
                {
                    Nexus.Client.DownloadProgressChanged -= OnDownloadProgress;
                }
            }

            // A cancelled install leaves nothing behind. Installing a partial collection would produce a profile that
            // silently does not match what the curator specified, which is worse than having nothing to show for it
            if (cancellationSource.IsCancellationRequested)
            {
                await AbandonCollectionInstall(collection, entriesByArchive.Keys.ToList(), installPath, extractedArchivePath);
                return;
            }

            // AddMods waits for the window to unlock before it starts, then locks it again itself, so the download
            // lock has to be released first or the install never begins
            SetLockState(false);

            // Anything without a manifest is not a mod and AddMods would drop it on the floor. Those are pulled out
            // here and applied further down, as they are usually the configuration a curator's rules point at
            var overlaysByArchive = TakeNonModEntries(entriesByArchive);

            // The rules decide which entry wins where two of them write the same path, which comes down to writing
            // them in the order the rules impose
            var orderedArchives = SortArchivesByInstallOrder(collection, entriesByArchive);
            if (collection.HasRuleCycle())
            {
                Program.helper.Log($"The ordering rules in {collection.Name} form a cycle, so the entries caught in it are written in the order the collection declares them", Helper.Status.Warning);
            }

            var installedModsByArchive = new Dictionary<string, List<Mod>>(StringComparer.OrdinalIgnoreCase);
            if (orderedArchives.Count > 0)
            {
                await AddMods(orderedArchives.ToArray(), installPath, installedModsByArchive);

                // Same reasoning as the collection archive: AddMods has extracted what it needs and leaving dozens
                // of archives behind would make reinstalling this collection fail on the first repeated filename
                foreach (var archivePath in orderedArchives)
                {
                    TryDelete(archivePath);
                }
            }

            // AddMods drops the lock as it finishes, and everything from here to the summary runs with nothing on
            // screen: two passes over the mods folder, the overlays, the extracted archive being cleared out and the
            // profile being built. On a large collection that is long enough to read as finished or as stuck
            SetLockState(true, String.Format(Program.translation.Get("ui.message.collection_finalizing"), collection.Name));
            await YieldToLockWindow();

            _viewModel.DiscoverMods(Pathing.defaultModPath);

            // The profile is built from what actually landed on disk rather than from what was requested, so a
            // partial install still produces a working profile
            var installedMods = _viewModel.Mods.Where(m => String.Equals(m.SourceId, collection.SourceId, StringComparison.OrdinalIgnoreCase)).ToList();
            RecordInstalledMods(entriesByArchive, installedModsByArchive, installedMods);

            UpdateLockWindow(Program.translation.Get("ui.message.collection_finalizing_configuration"));
            await YieldToLockWindow();

            // Last, as an overlay is copied into a mod folder that only exists once the mods above are installed and
            // their folder names have been recorded
            ApplyCollectionOverlays(collection, SortArchivesByInstallOrder(collection, overlaysByArchive), overlaysByArchive, installPath);

            // Configuration only lands at this point, so a second pass is needed for the mods it touched to report
            // that they now have a config
            if (overlaysByArchive.Count > 0)
            {
                _viewModel.DiscoverMods(Pathing.defaultModPath);
            }

            // Bundled entries and overlays are both read out of the extracted archive, so this can only go once
            // installing has finished
            TryDeleteDirectory(extractedArchivePath);

            UpdateLockWindow(Program.translation.Get("ui.message.collection_finalizing_profile"));
            await YieldToLockWindow();

            var profile = CreateProfileForCollection(collection);
            CollectionCache.Save(collection);

            // Handed straight over to the summary, so the two never overlap
            SetLockState(false);

            await ReportCollectionResult(collection);

            // After the summary rather than before it. Switching profiles can raise its own dialog over unsaved
            // configuration on the profile being left, which would collide with the summary still being open
            SelectCollectionProfile(profile);
        }

        /// <summary>
        /// Removes the entries that carry no manifest, handing them back for separate treatment. AddMods has nothing
        /// to install from a file with no manifest and skips it, which is the wrong outcome here: a curator's
        /// configuration arrives this way, whether bundled in the collection archive or downloaded like any other
        /// entry and the mod rules point at it as their source.
        /// </summary>
        /// <summary>
        /// The archives in the order their entries should be written. Anything the rules say nothing about keeps the
        /// position the collection gave it.
        /// </summary>
        private static List<string> SortArchivesByInstallOrder(CollectionInstall collection, Dictionary<string, CollectionModEntry> entriesByArchive)
        {
            var ranks = new Dictionary<CollectionModEntry, int>();
            var order = collection.GetInstallOrder();
            for (int rank = 0; rank < order.Count; rank++)
            {
                ranks[order[rank]] = rank;
            }

            return entriesByArchive.Keys.OrderBy(a => ranks.TryGetValue(entriesByArchive[a], out var rank) ? rank : Int32.MaxValue).ToList();
        }

        private static Dictionary<string, CollectionModEntry> TakeNonModEntries(Dictionary<string, CollectionModEntry> entriesByArchive)
        {
            var nonMods = new Dictionary<string, CollectionModEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var archivePath in entriesByArchive.Keys.ToList())
            {
                // A loose file, such as a config.json on its own, has no manifest to look for and is copied across
                // as it is further down
                if (IsArchiveFile(archivePath) && ArchiveHasManifest(archivePath))
                {
                    continue;
                }

                var entry = entriesByArchive[archivePath];
                Program.helper.Log($"Handling {entry.Name} as configuration rather than as a mod, as it carries no manifest of its own");

                nonMods[archivePath] = entry;
                entriesByArchive.Remove(archivePath);
            }

            return nonMods;
        }

        /// <summary>
        /// Whether a file is meant to be an archive, judged by its extension rather than by opening it. A corrupt
        /// download then still counts as one and goes down the path where it can be reported as a failure.
        /// </summary>
        private static bool IsArchiveFile(string filePath)
        {
            return _archiveExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);
        }

        private static bool ArchiveHasManifest(string archivePath)
        {
            try
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath);
                return archive.Entries.Any(e => e.IsDirectory is false && String.Equals(Path.GetFileName(e.Key), "manifest.json", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                // Assumed to be a mod, so a file that cannot be read here goes down the normal path and fails there
                Program.helper.Log($"Unable to read {archivePath} while checking whether it holds configuration: {ex.Message}", Helper.Status.Warning);
                return true;
            }
        }

        /// <summary>
        /// Writes each manifest-less entry into the collection's mod folder, whole and with its structure intact,
        /// then records which mods it landed on top of. The rules never decide which files an entry places, only
        /// which entry wins where two of them place the same path, so this is a plain copy and the ordering above is
        /// what makes the curator's configuration take precedence.
        /// </summary>
        private static void ApplyCollectionOverlays(CollectionInstall collection, List<string> orderedArchives, Dictionary<string, CollectionModEntry> overlaysByArchive, string installPath)
        {
            foreach (var archivePath in orderedArchives)
            {
                var entry = overlaysByArchive[archivePath];
                var overwritten = GetOverwrittenTargets(collection, entry, archivePath);

                if (TryExtractOverlay(archivePath, installPath) is false)
                {
                    entry.Status = CollectionModStatus.Failed;
                    entry.FailureReason = Program.translation.Get("ui.message.collection_reason_overlay_failed");
                    TryDelete(archivePath);
                    continue;
                }

                TryDelete(archivePath);

                entry.OverlayTargets = overwritten;
                entry.Status = CollectionModStatus.AppliedAsOverlay;
            }
        }

        /// <summary>
        /// The mods an entry's files land on top of, worked out by comparing the folders in its archive against the
        /// folders its targets were installed to. Reporting only: nothing here changes where the files go.
        /// </summary>
        private static List<string> GetOverwrittenTargets(CollectionInstall collection, CollectionModEntry entry, string archivePath)
        {
            var archiveFolders = GetArchiveRootFolders(archivePath);
            if (archiveFolders.Count == 0)
            {
                return new List<string>();
            }

            var overwritten = new List<string>();
            foreach (var target in collection.GetOverlayTargets(entry))
            {
                if (target.InstalledMods.Any(m => String.IsNullOrEmpty(m.FolderName) is false && archiveFolders.Contains(m.FolderName!, StringComparer.OrdinalIgnoreCase)))
                {
                    overwritten.Add(target.Name);
                }
            }

            return overwritten.Distinct().ToList();
        }

        private static List<string> GetArchiveRootFolders(string archivePath)
        {
            var folders = new List<string>();
            if (IsArchiveFile(archivePath) is false)
            {
                return folders;
            }

            try
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath);
                foreach (var archiveEntry in archive.Entries.Where(e => e.IsDirectory is false && String.IsNullOrEmpty(e.Key) is false))
                {
                    var segments = archiveEntry.Key!.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length < 2 || folders.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    folders.Add(segments[0]);
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to read the folder structure of {archivePath}: {ex.Message}", Helper.Status.Warning);
            }

            return folders;
        }

        private static bool TryExtractOverlay(string archivePath, string destinationPath)
        {
            try
            {
                Directory.CreateDirectory(destinationPath);

                if (IsArchiveFile(archivePath) is false)
                {
                    File.Copy(archivePath, Path.Combine(destinationPath, Path.GetFileName(archivePath)), true);
                    Program.helper.Log($"Copied the configuration file {Path.GetFileName(archivePath)} into {destinationPath}");

                    return true;
                }

                var written = 0;
                var destinationRoot = Path.GetFullPath(destinationPath) + Path.DirectorySeparatorChar;

                using var archive = ArchiveFactory.OpenArchive(archivePath);
                foreach (var archiveEntry in archive.Entries.Where(e => e.IsDirectory is false && String.IsNullOrEmpty(e.Key) is false))
                {
                    var segments = archiveEntry.Key!.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length == 0)
                    {
                        continue;
                    }

                    // The archive comes from a third party, so an entry that would climb out of the destination is
                    // dropped rather than written where it asks to go
                    var filePath = Path.GetFullPath(Path.Combine(destinationPath, Path.Combine(segments)));
                    if (filePath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase) is false)
                    {
                        Program.helper.Log($"Skipping {archiveEntry.Key} from {Path.GetFileName(archivePath)}, as it points outside {destinationPath}", Helper.Status.Warning);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                    using (var fileStream = File.Open(filePath, FileMode.Create, FileAccess.Write))
                    {
                        archiveEntry.WriteTo(fileStream);
                    }

                    written += 1;
                }

                Program.helper.Log($"Placed {written} file(s) from {Path.GetFileName(archivePath)} into {destinationPath}");

                return written > 0;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to place the contents of {archivePath} into {destinationPath}: {ex}", Helper.Status.Alert);

                return false;
            }
        }

        /// <summary>
        /// Undoes a cancelled install. Nothing was written to the mod folder yet, as installing only happens after
        /// the download loop, so this comes down to clearing the archives that had already been fetched.
        /// </summary>
        private async Task AbandonCollectionInstall(CollectionInstall collection, List<string> downloadedArchives, string installPath, string extractedArchivePath)
        {
            Program.helper.Log($"Discarding the cancelled install of the collection {collection.Name}, removing {downloadedArchives.Count} downloaded archive(s)");

            foreach (var archivePath in downloadedArchives)
            {
                TryDelete(archivePath);
            }

            // The collection's own archive and the folder it was extracted into
            TryDeleteDirectory(extractedArchivePath);

            // Created before the loop, so it exists even when nothing was ever placed in it. Skipped when a record
            // already exists, as that means an earlier install of this revision owns the folder and its contents
            if (CollectionCache.Load(collection.SourceId) is null)
            {
                TryDeleteDirectory(installPath);
            }

            SetLockState(false);

            await CreateWarningWindow(String.Format(Program.translation.Get("ui.message.collection_install_cancelled"), collection.Name), Program.translation.Get("internal.ok"));
        }

        private static void TryDelete(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to delete the file {filePath} while discarding a cancelled collection install: {ex.Message}", Helper.Status.Warning);
            }
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            try
            {
                if (String.IsNullOrEmpty(directoryPath) is false && Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, true);
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to delete the folder {directoryPath} while discarding a cancelled collection install: {ex.Message}", Helper.Status.Warning);
            }
        }

        /// <summary>
        /// Writes the aggregate download progress into the lock window. Reports bytes when the collection declares
        /// sizes and falls back to a mod count when it does not.
        /// </summary>
        private void UpdateCollectionProgress(string currentModName, int currentIndex, int totalMods, long downloadedBytes, long totalBytes)
        {
            var heading = String.Format(Program.translation.Get("ui.message.collection_downloading"), currentModName, currentIndex, totalMods);

            if (totalBytes <= 0)
            {
                UpdateLockWindow(heading, currentIndex, totalMods);
                return;
            }

            // Reported in megabytes, as a large collection would overflow the int the lock window takes
            var megabyte = 1024L * 1024L;
            var cappedBytes = downloadedBytes > totalBytes ? totalBytes : downloadedBytes;
            var sizeText = String.Format(Program.translation.Get("ui.message.collection_download_size"), Toolkit.ToHumanReadableSize(cappedBytes), Toolkit.ToHumanReadableSize(totalBytes));

            UpdateLockWindow(String.Concat(heading, Environment.NewLine, sizeText), (int)(cappedBytes / megabyte), (int)(totalBytes / megabyte));
        }

        /// <summary>
        /// Writes each archive's results back onto the entry that produced it. The unique IDs come straight from
        /// AddMods, so nothing is matched by name here. Folder names are then filled in from the discovered mods,
        /// which is an exact lookup now that the unique IDs are known.
        /// </summary>
        private static void RecordInstalledMods(Dictionary<string, CollectionModEntry> entriesByArchive, Dictionary<string, List<Mod>> installedModsByArchive, List<Mod> discoveredMods)
        {
            foreach (var archivePath in entriesByArchive.Keys)
            {
                var entry = entriesByArchive[archivePath];
                if (installedModsByArchive.TryGetValue(archivePath, out var producedMods) is false || producedMods.Count == 0)
                {
                    entry.Status = CollectionModStatus.Failed;
                    entry.FailureReason = Program.translation.Get("ui.message.collection_reason_no_manifest");
                    continue;
                }

                entry.InstalledMods.Clear();
                foreach (var producedMod in producedMods)
                {
                    var discovered = discoveredMods.FirstOrDefault(m => m.UniqueId.Equals(producedMod.UniqueId, StringComparison.OrdinalIgnoreCase));
                    var folderName = discovered is null || discovered.ModFileInfo.Directory is null ? null : discovered.ModFileInfo.Directory.Name;
                    entry.InstalledMods.Add(new InstalledModRecord(producedMod.UniqueId, folderName));
                }

                entry.Status = CollectionModStatus.Installed;
            }
        }

        private async Task<string?> DownloadCollectionEntry(CollectionModEntry entry, string extractedArchivePath, CancellationToken cancellationToken)
        {
            if (Nexus.Client is null)
            {
                return null;
            }

            // Bundled files are already on disk inside the extracted archive
            if (entry.SourceType is CollectionModSourceType.Bundle)
            {
                return FindBundledFile(entry, extractedArchivePath);
            }

            if (entry.IsFromNexus() is false)
            {
                entry.Status = CollectionModStatus.AwaitingManualDownload;
                return null;
            }

            entry.Status = CollectionModStatus.Downloading;

            // Asked for by file ID rather than by version. A mod can publish several files under one version number
            // and a collection routinely pins more than one of them, so matching on version hands every entry that
            // shares a mod ID the same file and only its name is used, which then collides on the way to disk
            var modFile = await Nexus.Client.GetFile(entry.NexusModId!.Value, entry.NexusFileId!.Value);
            if (modFile is null || String.IsNullOrEmpty(modFile.Name))
            {
                entry.Status = CollectionModStatus.Failed;
                entry.FailureReason = Program.translation.Get("ui.message.collection_reason_no_file");
                return null;
            }

            var downloadLink = await Nexus.Client.GetFileDownloadLink(entry.NexusModId.Value, entry.NexusFileId.Value, serverName: EnumParser.GetDescription(Program.settings.PreferredNexusServer));
            if (String.IsNullOrEmpty(downloadLink))
            {
                entry.Status = CollectionModStatus.AwaitingManualDownload;
                return null;
            }

            var downloadResult = await Nexus.Client.DownloadFileAndGetPath(downloadLink, GetAvailableDownloadName(modFile.Name, entry.NexusFileId.Value), cancellationToken);
            if (downloadResult.ResultKind is DownloadResultKind.UserCanceled)
            {
                entry.Status = CollectionModStatus.Skipped;
                return null;
            }

            if (String.IsNullOrEmpty(downloadResult.DownloadedModFilePath))
            {
                entry.Status = CollectionModStatus.Failed;
                entry.FailureReason = Program.translation.Get("ui.message.collection_reason_download_failed");
                return null;
            }

            return downloadResult.DownloadedModFilePath;
        }

        /// <summary>
        /// A download name nothing in the Nexus folder is already using. Two entries in one collection can point at
        /// files that were uploaded under the same name and a download cannot write over a name that is taken, so
        /// the file ID is folded in where that happens. A file the user supplied carries no ID, and falls back to a
        /// plain number.
        /// </summary>
        private static string GetAvailableDownloadName(string fileName, int? fileId = null)
        {
            // Taken care of before anything is looked for, as a name the filesystem would alter on the way in gets
            // written under one name and checked for under another
            fileName = Pathing.GetSafePathSegment(fileName);

            var downloadPath = Pathing.GetNexusPath();
            if (File.Exists(Path.Combine(downloadPath, fileName)) is false)
            {
                return fileName;
            }

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var qualifier = fileId is null ? String.Empty : $" [{fileId}]";

            var candidate = $"{baseName}{qualifier}{extension}";
            int suffix = 2;
            while (File.Exists(Path.Combine(downloadPath, candidate)))
            {
                candidate = $"{baseName}{qualifier} ({suffix}){extension}";
                suffix++;
            }

            Program.helper.Log($"Writing {fileName} as {candidate}, as the original name is already taken in the Nexus folder");

            return candidate;
        }

        /// <summary>
        /// Locates a bundled entry inside the extracted collection archive. A curator can bundle a folder just as
        /// readily as an archive and collection.json records the name with its extension stripped either way, so an
        /// exact filename is only one of the three shapes a bundle arrives in. A folder is packed into an archive of
        /// its own, as everything downstream reads an entry's contents out of one.
        /// </summary>
        private static string? FindBundledFile(CollectionModEntry entry, string extractedArchivePath)
        {
            if (String.IsNullOrEmpty(extractedArchivePath) || Directory.Exists(extractedArchivePath) is false)
            {
                return FailBundledEntry(entry, "ui.message.collection_reason_missing_bundle");
            }

            // Both names describe the same file and either can be absent, so whichever is present is tried in turn
            var searchNames = new[] { entry.FileExpression, entry.LogicalFilename }.Where(n => String.IsNullOrEmpty(n) is false).Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (searchNames.Count == 0)
            {
                return FailBundledEntry(entry, "ui.message.collection_reason_missing_bundle");
            }

            var root = new DirectoryInfo(extractedArchivePath);
            foreach (var searchName in searchNames)
            {
                if (MatchBundledFile(root, searchName) is FileInfo file)
                {
                    return file.FullName;
                }

                if (MatchBundledFolder(root, searchName) is not DirectoryInfo folder)
                {
                    continue;
                }

                var packedPath = PackBundledFolder(folder);
                if (String.IsNullOrEmpty(packedPath))
                {
                    return FailBundledEntry(entry, "ui.message.collection_reason_bundle_pack_failed");
                }

                Program.helper.Log($"Packed the bundled folder {folder.Name} for {entry.Name}, so it installs by the same route as every other entry");

                return packedPath;
            }

            return FailBundledEntry(entry, "ui.message.collection_reason_missing_bundle");
        }

        /// <summary>
        /// The file a bundled entry names, preferring an exact match and falling back to the same name under any
        /// extension. An archive wins that fallback, as a curator bundling one alongside a loose file of the same
        /// name means the archive.
        /// </summary>
        private static FileInfo? MatchBundledFile(DirectoryInfo root, string searchName)
        {
            try
            {
                if (root.GetFiles(searchName, SearchOption.AllDirectories).FirstOrDefault() is FileInfo exact)
                {
                    return exact;
                }

                var candidates = root.GetFiles($"{searchName}.*", SearchOption.AllDirectories);

                return candidates.FirstOrDefault(f => IsArchiveFile(f.FullName)) ?? candidates.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to search {root.FullName} for the bundled file {searchName}: {ex.Message}", Helper.Status.Warning);

                return null;
            }
        }

        private static DirectoryInfo? MatchBundledFolder(DirectoryInfo root, string searchName)
        {
            try
            {
                return root.GetDirectories(searchName, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to search {root.FullName} for the bundled folder {searchName}: {ex.Message}", Helper.Status.Warning);

                return null;
            }
        }

        /// <summary>
        /// Packs a bundled folder into an archive of its own, written alongside the collection's other downloads so
        /// that the cleanup after installing removes it along with the rest. The folder is kept as the archive's
        /// root rather than being flattened into it, since its name is what the mod ends up installed as.
        /// </summary>
        private static string? PackBundledFolder(DirectoryInfo folder)
        {
            try
            {
                var downloadPath = Pathing.GetNexusPath();
                Directory.CreateDirectory(downloadPath);

                // Removed first, as CreateFromDirectory refuses to write over a file that already exists
                var archivePath = Path.Combine(downloadPath, $"{folder.Name}.zip");
                TryDelete(archivePath);

                // Uncompressed, as this is read back and deleted within the same install
                ZipFile.CreateFromDirectory(folder.FullName, archivePath, CompressionLevel.NoCompression, includeBaseDirectory: true);

                return archivePath;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to pack the bundled folder {folder.FullName}: {ex}", Helper.Status.Alert);

                return null;
            }
        }

        private static string? FailBundledEntry(CollectionModEntry entry, string reasonKey)
        {
            entry.Status = CollectionModStatus.Failed;
            entry.FailureReason = Program.translation.Get(reasonKey);

            return null;
        }

        /// <summary>
        /// Generates the profile that represents this collection. The profile is protected, as editing it directly
        /// would drift it away from the curator's pins. Users wanting changes clone it into a plain profile.
        /// </summary>
        private Profile CreateProfileForCollection(CollectionInstall collection)
        {
            var enabledMods = collection.GetEnabledModReferences();

            var profileName = GetAvailableProfileName(collection);
            collection.ProfileName = profileName;

            var profile = new Profile(profileName, isProtected: true, enabledMods: enabledMods, sourceId: collection.SourceId);
            _editorView.AddProfile(profile);

            return profile;
        }

        /// <summary>
        /// Switches the grid over to the collection's profile, so what was just installed is what the user is left
        /// looking at. A profile with nothing enabled is left alone, as selecting it would empty the grid and give
        /// the user nothing to look at after a report explaining what went wrong.
        /// </summary>
        private void SelectCollectionProfile(Profile profile)
        {
            if (profile.EnabledModIds.Count == 0)
            {
                Program.helper.Log($"Leaving the current profile selected, as {profile.Name} has nothing enabled to show");
                return;
            }

            var profileComboBox = this.FindControl<ComboBox>("profileComboBox");
            if (profileComboBox is null)
            {
                return;
            }

            // The profile was built moments ago and GetAvailableProfileName gives it a name nothing else holds, so
            // this is always a change of selection and always raises the handler that enables its mods
            profileComboBox.SelectedItem = profile;
        }

        private string GetAvailableProfileName(CollectionInstall collection)
        {
            var baseName = String.IsNullOrEmpty(collection.Name) ? collection.Slug : collection.Name;
            var candidate = $"{baseName} (r{collection.RevisionNumber})";

            int suffix = 2;
            while (_editorView.Profiles.Any(p => p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{baseName} (r{collection.RevisionNumber}) [{suffix}]";
                suffix++;
            }

            return candidate;
        }

        /// <summary>
        /// Handles an nxm mod link that matches an entry a collection is still waiting on. A Mod Manager Download
        /// link carries the key and expiry pair that authorises the download, so this is the one route a non-premium
        /// account has into a collection Stardrop could not fetch on its behalf.
        /// </summary>
        /// <returns>True when the link was handled here, false when it should install as an ordinary mod</returns>
        private async Task<bool> TryProcessCollectionEntryLink(NXM nxmLink)
        {
            if (Nexus.Client is null || String.IsNullOrEmpty(nxmLink.Link))
            {
                return false;
            }

            if (NexusClient.TryParseModNxmLink(nxmLink.Link, out _, out var modId, out var fileId) is false)
            {
                return false;
            }

            var matches = FindUnsatisfiedCollectionEntries(modId, fileId);
            if (matches.Count == 0)
            {
                return false;
            }

            var entryName = matches[0].Entry.Name;
            Program.helper.Log($"The file {fileId} of mod {modId} is pinned by {matches.Count} collection(s) that are still waiting on it");

            // Not asked while the collections window is open, as a link arriving then came from a row the user
            // clicked in it. They have already picked the entry, so a prompt naming it back at them adds nothing
            if (Program.settings.IsAskingBeforeAcceptingNXM && _collectionsWindow is null)
            {
                var collectionNames = String.Join(Environment.NewLine, matches.Select(m => $"  {m.Collection.Name}"));
                var requestWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.confirm_collection_entry_capture"), entryName, collectionNames));
                KeepDialogAboveSiblings(requestWindow);

                // Declining asks for the ordinary install rather than cancelling, so the caller carries on from here
                if (await requestWindow.ShowDialog<bool>(this) is false)
                {
                    return false;
                }
            }

            var fileSafetyResult = await Nexus.Client.ValidateFileSafety(modId, fileId);
            if (fileSafetyResult is false)
            {
                await ReportCollectionEntryResult(Program.translation.Get("ui.warning.file_quarantined"), isFailure: true);
                return true;
            }

            if (fileSafetyResult is null)
            {
                var safetyWindow = new MessageWindow(Program.translation.Get("ui.warning.failed_to_verify_mod_file"));
                KeepDialogAboveSiblings(safetyWindow);

                if (await safetyWindow.ShowDialog<bool>(this) is false)
                {
                    return true;
                }
            }

            var archivePath = await DownloadCollectionEntryViaNXM(nxmLink, modId, fileId);
            if (String.IsNullOrEmpty(archivePath))
            {
                await ReportCollectionEntryResult(String.Format(Program.translation.Get("ui.warning.failed_nexus_install"), entryName), isFailure: true);
                return true;
            }

            // The count of what a collection still needs is dropped while its window is open, as the details panel
            // above already carries it and repeating it in the footer says the same thing twice
            var summary = await InstallEntryIntoCollections(entryName, archivePath, matches, includeRemainingCount: _collectionsWindow is null);
            var hasFailure = matches.Any(m => m.Entry.IsSatisfied() is false);

            _viewModel.EvaluateRequirements();
            _viewModel.UpdateEndorsements();
            _viewModel.UpdateFilter();

            // The row the user clicked to get here is still showing the old status, so the window behind is brought
            // back in step before the result is written into it
            _collectionsWindow?.RefreshCollections();

            await ReportCollectionEntryResult(String.Join(Environment.NewLine, summary), hasFailure);

            return true;
        }

        /// <summary>
        /// Reports what a single collection entry's link did. With the collections window open it goes into that
        /// window's footer, since a user working down a list of missing entries is sending links over faster than
        /// they could dismiss a dialog for each one. With the window closed there is nowhere to put it but a window
        /// of its own.
        /// </summary>
        private async Task ReportCollectionEntryResult(string message, bool isFailure = false)
        {
            if (_collectionsWindow is not null)
            {
                _collectionsWindow.ShowStatusMessage(message, isFailure);
                return;
            }

            await CreateWarningWindow(message, Program.translation.Get("internal.ok"), windowWidth: 560);
        }

        /// <summary>
        /// Installs one archive into every entry it satisfies, then removes it. The archive belongs to Stardrop by
        /// this point, whether it was downloaded or copied out of the way of a file the user supplied, so clearing
        /// it up here is the same cleanup the main install performs.
        /// </summary>
        private async Task<List<string>> InstallEntryIntoCollections(string entryName, string archivePath, List<(CollectionInstall Collection, CollectionModEntry Entry)> matches, bool includeRemainingCount = true)
        {
            var summary = new List<string>();
            foreach (var match in matches)
            {
                // Each collection installs into a folder of its own, so a file two of them pin lands in both
                var wasInstalled = await InstallCollectionEntryArchive(match.Collection, match.Entry, archivePath);
                if (wasInstalled)
                {
                    AddEntryToCollectionProfile(match.Collection, match.Entry);
                }

                // After the profile, as enabling the mod can rename the collection's record of which profile it owns
                CollectionCache.Save(match.Collection);

                if (wasInstalled is false)
                {
                    var reason = String.IsNullOrEmpty(match.Entry.FailureReason) ? String.Empty : $" ({match.Entry.FailureReason})";
                    summary.Add(String.Format(Program.translation.Get("ui.message.collection_entry_failed"), entryName, match.Collection.Name) + reason);
                    continue;
                }

                summary.Add(String.Format(Program.translation.Get("ui.message.collection_entry_installed"), entryName, match.Collection.Name));

                if (includeRemainingCount is false)
                {
                    continue;
                }

                var remaining = match.Collection.GetManualDownloads().Count;
                if (remaining > 0)
                {
                    summary.Add(String.Format(Program.translation.Get("ui.message.collection_entry_remaining"), remaining));
                }
            }

            TryDelete(archivePath);

            return summary;
        }

        /// <summary>
        /// Installs files the user fetched themselves into the collections waiting on them. This is the only route
        /// open to a Browse or Direct entry, which produces no nxm link at all, and the checksum recorded in
        /// collection.json is what identifies the file, since such an entry has no file ID behind it either.
        /// </summary>
        private async Task HandleCollectionFileDrop(string[] filePaths)
        {
            var summary = new List<string>();
            foreach (var filePath in filePaths)
            {
                if (File.Exists(filePath) is false)
                {
                    continue;
                }

                var checksum = Toolkit.GetFileChecksum(filePath);
                var matches = String.IsNullOrEmpty(checksum) ? new List<(CollectionInstall Collection, CollectionModEntry Entry)>() : FindUnsatisfiedCollectionEntries(checksum);
                if (matches.Count == 0)
                {
                    summary.Add(String.Format(Program.translation.Get("ui.message.collection_drop_no_match"), Path.GetFileName(filePath)));
                    continue;
                }

                // Installing removes the archive it worked from, which must never be the user's own copy sitting
                // wherever they saved it
                var archivePath = CopyIntoDownloadFolder(filePath);
                if (String.IsNullOrEmpty(archivePath))
                {
                    summary.Add(String.Format(Program.translation.Get("ui.message.collection_drop_copy_failed"), Path.GetFileName(filePath)));
                    continue;
                }

                summary.AddRange(await InstallEntryIntoCollections(matches[0].Entry.Name, archivePath, matches));
            }

            if (summary.Count == 0)
            {
                return;
            }

            _viewModel.EvaluateRequirements();
            _viewModel.UpdateEndorsements();
            _viewModel.UpdateFilter();

            // Reported the same way an nxm link is, so a drop and a download read the same in the same window. The
            // caller refreshes the window after this returns, which is why nothing here clears the footer
            await ReportCollectionEntryResult(String.Join(Environment.NewLine, summary));
        }

        /// <summary>
        /// Copies a file the user supplied into the download folder, under a name nothing there is already using.
        /// </summary>
        private static string? CopyIntoDownloadFolder(string filePath)
        {
            try
            {
                var downloadPath = Pathing.GetNexusPath();
                Directory.CreateDirectory(downloadPath);

                var targetPath = Path.Combine(downloadPath, GetAvailableDownloadName(Path.GetFileName(filePath)));
                File.Copy(filePath, targetPath);

                return targetPath;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to copy the dropped file {filePath} into the download folder: {ex}", Helper.Status.Alert);

                return null;
            }
        }

        /// <summary>
        /// Every cached collection with an unsatisfied entry pinned to this checksum. It is the one identifier a
        /// file the user supplied carries, as an entry Stardrop cannot fetch has no download name or file ID.
        /// </summary>
        private static List<(CollectionInstall Collection, CollectionModEntry Entry)> FindUnsatisfiedCollectionEntries(string checksum)
        {
            var matches = new List<(CollectionInstall Collection, CollectionModEntry Entry)>();
            foreach (var collection in CollectionCache.LoadAll())
            {
                foreach (var entry in collection.Mods)
                {
                    if (entry.IsSatisfied() || String.IsNullOrEmpty(entry.Md5Checksum))
                    {
                        continue;
                    }

                    if (String.Equals(entry.Md5Checksum, checksum, StringComparison.OrdinalIgnoreCase) is false)
                    {
                        continue;
                    }

                    matches.Add((collection, entry));
                    break;
                }
            }

            return matches;
        }

        /// <summary>
        /// Every cached collection pinning this exact file that has not accounted for it yet. Skipped entries count,
        /// as an optional mod fetched by hand is the user asking for it, and so do failed ones, since downloading the
        /// file again is the natural way to repair an entry whose first attempt did not land.
        /// </summary>
        private static List<(CollectionInstall Collection, CollectionModEntry Entry)> FindUnsatisfiedCollectionEntries(int modId, int fileId)
        {
            var matches = new List<(CollectionInstall Collection, CollectionModEntry Entry)>();
            foreach (var collection in CollectionCache.LoadAll())
            {
                foreach (var entry in collection.Mods)
                {
                    if (entry.IsFromNexus() is false || entry.IsSatisfied())
                    {
                        continue;
                    }

                    if (entry.NexusModId!.Value != modId || entry.NexusFileId!.Value != fileId)
                    {
                        continue;
                    }

                    // A collection pins any given file once, so there is nothing further to find in this one
                    matches.Add((collection, entry));
                    break;
                }
            }

            return matches;
        }

        /// <summary>
        /// Fetches the file the link points at. The link itself is handed to GetFileDownloadLink rather than the two
        /// IDs, as the key and expiry pair it carries is what authorises a download without a Premium account.
        /// </summary>
        private async Task<string?> DownloadCollectionEntryViaNXM(NXM nxmLink, int modId, int fileId)
        {
            if (Nexus.Client is null)
            {
                return null;
            }

            var modFile = await Nexus.Client.GetFile(modId, fileId);
            if (modFile is null || String.IsNullOrEmpty(modFile.Name))
            {
                return null;
            }

            var downloadLink = await Nexus.Client.GetFileDownloadLink(nxmLink, EnumParser.GetDescription(Program.settings.PreferredNexusServer));
            if (String.IsNullOrEmpty(downloadLink))
            {
                return null;
            }

            var downloadResult = await Nexus.Client.DownloadFileAndGetPath(downloadLink, GetAvailableDownloadName(modFile.Name, fileId));
            if (downloadResult.ResultKind is not DownloadResultKind.Success)
            {
                return null;
            }

            return downloadResult.DownloadedModFilePath;
        }

        /// <summary>
        /// Installs a single archive into the collection it belongs to, taking the same two paths the main install
        /// does: an archive carrying a manifest goes through AddMods, while anything else is the curator's
        /// configuration and is copied over the mods it targets.
        /// </summary>
        private async Task<bool> InstallCollectionEntryArchive(CollectionInstall collection, CollectionModEntry entry, string archivePath)
        {
            var installPath = Pathing.GetCollectionInstallPath(collection.SourceId);
            Directory.CreateDirectory(installPath);

            entry.SourceArchivePath = archivePath;

            // The rules decide which entry wins where two of them write the same path, and an entry arriving after
            // the install has finished is written last whatever rank they gave it
            var installOrder = collection.GetInstallOrder();
            var entryRank = installOrder.IndexOf(entry);
            if (entryRank >= 0 && installOrder.Skip(entryRank + 1).Any(m => m.IsSatisfied()))
            {
                Program.helper.Log($"Writing {entry.Name} into {collection.Name} after entries the curator's rules place below it, as it arrived once the install had finished", Helper.Status.Warning);
            }

            if (IsArchiveFile(archivePath) is false || ArchiveHasManifest(archivePath) is false)
            {
                Program.helper.Log($"Handling {entry.Name} as configuration rather than as a mod, as it carries no manifest of its own");

                var overwritten = GetOverwrittenTargets(collection, entry, archivePath);
                if (TryExtractOverlay(archivePath, installPath) is false)
                {
                    entry.Status = CollectionModStatus.Failed;
                    entry.FailureReason = Program.translation.Get("ui.message.collection_reason_overlay_failed");

                    return false;
                }

                entry.OverlayTargets = overwritten;
                entry.Status = CollectionModStatus.AppliedAsOverlay;

                // Configuration only lands at this point, so the mods it touched need a second pass to report that
                // they now have a config
                _viewModel.DiscoverMods(Pathing.defaultModPath);

                return true;
            }

            var installedModsByArchive = new Dictionary<string, List<Mod>>(StringComparer.OrdinalIgnoreCase);
            await AddMods(new string[] { archivePath }, installPath, installedModsByArchive);

            _viewModel.DiscoverMods(Pathing.defaultModPath);

            var installedMods = _viewModel.Mods.Where(m => String.Equals(m.SourceId, collection.SourceId, StringComparison.OrdinalIgnoreCase)).ToList();
            var entriesByArchive = new Dictionary<string, CollectionModEntry>(StringComparer.OrdinalIgnoreCase) { [archivePath] = entry };
            RecordInstalledMods(entriesByArchive, installedModsByArchive, installedMods);

            return entry.Status is CollectionModStatus.Installed;
        }

        /// <summary>
        /// Adds a late arrival to the collection's generated profile. Matched on the source ID rather than the
        /// profile name, as a user is free to rename the profile and the source ID is what ties the two together.
        /// </summary>
        private void AddEntryToCollectionProfile(CollectionInstall collection, CollectionModEntry entry)
        {
            var profile = _editorView.Profiles.FirstOrDefault(p => String.Equals(p.SourceId, collection.SourceId, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                Program.helper.Log($"Installed {entry.Name} into {collection.Name}, though its profile could not be found to enable the mod in", Helper.Status.Warning);
                return;
            }

            var addedReference = false;
            foreach (var installedMod in entry.InstalledMods)
            {
                var reference = new ModReference(installedMod.UniqueId, collection.SourceId);
                if (profile.EnabledModIds.Contains(reference))
                {
                    continue;
                }

                profile.EnabledModIds.Add(reference);
                addedReference = true;
            }

            if (addedReference is false)
            {
                return;
            }

            _editorView.CreateProfile(profile, force: true);
            collection.ProfileName = profile.Name;

            // Only when it is the profile on screen, as enabling mods by a profile the user is not looking at would
            // leave the grid showing one profile's mods under another's name
            if (GetCurrentProfile() == profile)
            {
                _viewModel.EnableModsByProfile(profile);
            }
        }

        /// <summary>
        /// Hands the UI thread back for long enough that the lock window can open and repaint. The sentinel that
        /// opens it is a timer, so a run of synchronous work with no await in it never gives that timer a turn and
        /// the window would either never appear or appear only once the work it was meant to cover had finished.
        /// </summary>
        private static async Task YieldToLockWindow()
        {
            await Task.Delay(_lockWindowYieldMilliseconds);
        }

        /// <summary>
        /// One summary at the end, rather than a dialog per mod. A large collection can easily have a dozen entries
        /// Stardrop cannot fetch itself and a dozen modals is not a usable experience.
        /// </summary>
        private async Task ReportCollectionResult(CollectionInstall collection)
        {
            var manualDownloads = collection.GetManualDownloads();
            var failures = collection.GetFailures();
            var conflicts = collection.GetConflicts();
            var installedCount = collection.Mods.Count(m => m.Status is CollectionModStatus.Installed);

            // Overlays are configuration rather than mods, so counting them in the total would misreport the install
            var modCount = collection.Mods.Count - collection.GetOverlays().Count;

            // Escaped, as the report is parsed for links further down and a mod name can hold the same characters
            var summary = String.Format(Program.translation.Get("ui.message.collection_install_summary"), HyperlinkParser.Escape(collection.Name), installedCount, modCount);

            if (manualDownloads.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_manual_downloads"), manualDownloads.Count);
                summary += BuildEntryLines(collection, manualDownloads, includeFailureReasons: false);
            }

            if (failures.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_failures"), failures.Count);
                summary += BuildEntryLines(collection, failures, includeFailureReasons: true);
            }

            if (conflicts.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_conflicts"), conflicts.Count);
                foreach (var conflict in conflicts)
                {
                    summary += Environment.NewLine + $"  {String.Format(Program.translation.Get("ui.message.collection_conflict_entry"), HyperlinkParser.Escape(conflict.Source.Name), HyperlinkParser.Escape(conflict.Target.Name))}";
                }
            }

            // Left as written, as the curator may have included links of their own
            if (String.IsNullOrEmpty(collection.InstallInstructions) is false)
            {
                summary += Environment.NewLine + Environment.NewLine + Program.translation.Get("ui.message.collection_curator_notes");
                summary += Environment.NewLine + collection.InstallInstructions;
            }

            await CreateWarningWindow(summary, Program.translation.Get("internal.ok"), windowWidth: 560, enableHyperlinks: true);
        }

        /// <summary>
        /// One line per entry, stopping once the list would be long enough to push the rest of the summary off the
        /// window. The heading above it already carries the full count and the collections window holds the whole
        /// list, so what is left is reported as a number rather than named.
        /// </summary>
        private static string BuildEntryLines(CollectionInstall collection, List<CollectionModEntry> entries, bool includeFailureReasons)
        {
            var lines = String.Empty;
            foreach (var entry in entries.Take(_maxListedEntries))
            {
                var reason = includeFailureReasons is false || String.IsNullOrEmpty(entry.FailureReason) ? String.Empty : $" ({HyperlinkParser.Escape(entry.FailureReason)})";
                lines += Environment.NewLine + $"  {HyperlinkParser.CreateLink(entry.Name, collection.GetEntryPageUri(entry, Program.settings.UseNXMLinks))}{reason}";
            }

            if (entries.Count > _maxListedEntries)
            {
                lines += Environment.NewLine + $"  {HyperlinkParser.Escape(String.Format(Program.translation.Get("ui.message.collection_entries_truncated"), entries.Count - _maxListedEntries))}";
            }

            return lines;
        }
    }
}
