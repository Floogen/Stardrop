using Avalonia.Controls;
using Semver;
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

            var safeName = String.Join("_", collectionName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
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

            // Anything the user already has is reused rather than downloaded again. The launch-time junction pulls
            // mods in from wherever they live, so a collection profile can point outside its own folder
            ReuseInstalledMods(collection);

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

            _viewModel.DiscoverMods(Pathing.defaultModPath);

            // The profile is built from what actually landed on disk rather than from what was requested, so a
            // partial install still produces a working profile
            var installedMods = _viewModel.Mods.Where(m => String.Equals(m.SourceId, collection.SourceId, StringComparison.OrdinalIgnoreCase)).ToList();
            RecordInstalledMods(entriesByArchive, installedModsByArchive, installedMods);

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

            CreateProfileForCollection(collection);
            CollectionCache.Save(collection);

            await ReportCollectionResult(collection);
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
        /// Marks entries the user already satisfies, so they are never downloaded. Matching goes through the Nexus
        /// mod ID taken from a mod's update keys, which is exact, rather than through display names.
        /// </summary>
        private void ReuseInstalledMods(CollectionInstall collection)
        {
            var candidates = _viewModel.Mods.Where(m => m.IsFromCollection is false && m.NexusModId is not null).ToList();
            foreach (var entry in collection.Mods.Where(e => e.Status is CollectionModStatus.Pending or CollectionModStatus.AwaitingManualDownload))
            {
                if (entry.IsFromNexus() is false)
                {
                    continue;
                }

                var match = candidates.FirstOrDefault(m => m.NexusModId == entry.NexusModId && SatisfiesPin(m, entry));
                if (match is null)
                {
                    continue;
                }

                Program.helper.Log($"Reusing the already installed {match.Name} ({match.ParsedVersion}) for the collection entry {entry.Name}");

                entry.SatisfiedBy = match.ToReference();
                entry.SatisfiedByVersion = match.ParsedVersion;
                entry.Status = CollectionModStatus.SatisfiedExternally;
            }
        }

        /// <summary>
        /// Whether an installed mod meets a collection entry's pin. An exact pin needs the versions to agree, while
        /// prefer and latest accept anything at or above the pinned version.
        /// </summary>
        private static bool SatisfiesPin(Mod mod, CollectionModEntry entry)
        {
            if (mod.HasValidVersion() is false)
            {
                return false;
            }

            // Without a pinned version there is nothing to compare, so any installed copy will do
            if (String.IsNullOrEmpty(entry.Version))
            {
                return true;
            }

            if (SemVersion.TryParse(entry.Version.Replace("v", String.Empty), SemVersionStyles.Any, out var pinnedVersion) is false)
            {
                return false;
            }

            if (entry.IsPinnedExactly())
            {
                return mod.Version.CompareSortOrderTo(pinnedVersion) == 0;
            }

            return mod.Version.CompareSortOrderTo(pinnedVersion) >= 0;
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

            // Collection files are frequently not in the MAIN category, so category filtering has to be relaxed here
            var modFile = await Nexus.Client.GetFileByVersion(entry.NexusModId!.Value, String.IsNullOrEmpty(entry.Version) ? String.Empty : entry.Version, ignoreCategory: true);
            if (modFile is null || String.IsNullOrEmpty(modFile.Name))
            {
                entry.Status = CollectionModStatus.Failed;
                entry.FailureReason = Program.translation.Get("ui.message.collection_reason_no_file");
                return null;
            }

            var downloadLink = await Nexus.Client.GetFileDownloadLink(entry.NexusModId.Value, entry.NexusFileId!.Value, serverName: EnumParser.GetDescription(Program.settings.PreferredNexusServer));
            if (String.IsNullOrEmpty(downloadLink))
            {
                entry.Status = CollectionModStatus.AwaitingManualDownload;
                return null;
            }

            var downloadResult = await Nexus.Client.DownloadFileAndGetPath(downloadLink, modFile.Name);
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

        private static string? FindBundledFile(CollectionModEntry entry, string extractedArchivePath)
        {
            if (String.IsNullOrEmpty(extractedArchivePath) || Directory.Exists(extractedArchivePath) is false)
            {
                entry.Status = CollectionModStatus.Failed;
                entry.FailureReason = Program.translation.Get("ui.message.collection_reason_missing_bundle");
                return null;
            }

            var searchName = String.IsNullOrEmpty(entry.FileExpression) ? entry.LogicalFilename : entry.FileExpression;
            if (String.IsNullOrEmpty(searchName))
            {
                entry.Status = CollectionModStatus.Failed;
                entry.FailureReason = Program.translation.Get("ui.message.collection_reason_missing_bundle");
                return null;
            }

            var match = new DirectoryInfo(extractedArchivePath).GetFiles(searchName, SearchOption.AllDirectories).FirstOrDefault();
            if (match is null)
            {
                entry.Status = CollectionModStatus.Failed;
                entry.FailureReason = Program.translation.Get("ui.message.collection_reason_missing_bundle");
                return null;
            }

            return match.FullName;
        }

        /// <summary>
        /// Generates the profile that represents this collection. The profile is protected, as editing it directly
        /// would drift it away from the curator's pins. Users wanting changes clone it into a plain profile.
        /// </summary>
        private void CreateProfileForCollection(CollectionInstall collection)
        {
            var enabledMods = collection.GetEnabledModReferences();

            var profileName = GetAvailableProfileName(collection);
            collection.ProfileName = profileName;

            var profile = new Profile(profileName, isProtected: true, enabledMods: enabledMods, sourceId: collection.SourceId);
            _editorView.AddProfile(profile);
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
        /// One summary at the end, rather than a dialog per mod. A large collection can easily have a dozen entries
        /// Stardrop cannot fetch itself and a dozen modals is not a usable experience.
        /// </summary>
        private async Task ReportCollectionResult(CollectionInstall collection)
        {
            var manualDownloads = collection.GetManualDownloads();
            var failures = collection.GetFailures();
            var overlays = collection.GetOverlays();
            var conflicts = collection.GetConflicts();
            var installedCount = collection.Mods.Count(m => m.Status is CollectionModStatus.Installed);
            var reusedCount = collection.GetReusedCount();

            // Overlays are configuration rather than mods, so counting them in the total would misreport the install
            var modCount = collection.Mods.Count - overlays.Count;

            // Escaped, as the report is parsed for links further down and a mod name can hold the same characters
            var summary = String.Format(Program.translation.Get("ui.message.collection_install_summary"), HyperlinkParser.Escape(collection.Name), installedCount, modCount);

            if (reusedCount > 0)
            {
                summary += Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_reused"), reusedCount);
            }

            if (overlays.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_overlays"), overlays.Count);
                foreach (var entry in overlays)
                {
                    if (entry.OverlayTargets.Count == 0)
                    {
                        summary += Environment.NewLine + $"  {String.Format(Program.translation.Get("ui.message.collection_overlay_entry_unmatched"), HyperlinkParser.Escape(entry.Name))}";
                        continue;
                    }

                    var targets = HyperlinkParser.Escape(String.Join(", ", entry.OverlayTargets));
                    summary += Environment.NewLine + $"  {String.Format(Program.translation.Get("ui.message.collection_overlay_entry"), HyperlinkParser.Escape(entry.Name), targets)}";
                }
            }

            if (manualDownloads.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_manual_downloads"), manualDownloads.Count);
                foreach (var entry in manualDownloads)
                {
                    summary += Environment.NewLine + $"  {HyperlinkParser.CreateLink(entry.Name, GetEntryPageUri(collection, entry))}";
                }
            }

            if (failures.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_failures"), failures.Count);
                foreach (var entry in failures)
                {
                    var reason = String.IsNullOrEmpty(entry.FailureReason) ? String.Empty : $" ({HyperlinkParser.Escape(entry.FailureReason)})";
                    summary += Environment.NewLine + $"  {HyperlinkParser.CreateLink(entry.Name, GetEntryPageUri(collection, entry))}{reason}";
                }
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
        /// The page to send the user to for an entry they have to handle themselves, preferring the curator's own
        /// link over one built from the Nexus IDs. Returns null when neither is available, which leaves the entry
        /// as plain text in the report.
        /// </summary>
        private static string? GetEntryPageUri(CollectionInstall collection, CollectionModEntry entry)
        {
            if (String.IsNullOrEmpty(entry.ExternalUri) is false)
            {
                return entry.ExternalUri;
            }

            if (entry.NexusModId is null)
            {
                return null;
            }

            var domainName = String.IsNullOrEmpty(collection.DomainName) ? "stardewvalley" : collection.DomainName;
            if (entry.NexusFileId is null)
            {
                return $"https://www.nexusmods.com/{domainName}/mods/{entry.NexusModId}";
            }

            // The collection pins a specific file, so the files tab saves the user hunting for it themselves
            return $"https://www.nexusmods.com/{domainName}/mods/{entry.NexusModId}?tab=files&file_id={entry.NexusFileId}";
        }
    }
}
