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
                // The archive has served its purpose once extracted, and downloads use FileMode.CreateNew, so
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

            var installedModsByArchive = new Dictionary<string, List<Mod>>(StringComparer.OrdinalIgnoreCase);
            if (entriesByArchive.Count > 0)
            {
                await AddMods(entriesByArchive.Keys.ToArray(), installPath, installedModsByArchive);

                // Same reasoning as the collection archive: AddMods has extracted what it needs, and leaving dozens
                // of archives behind would make reinstalling this collection fail on the first repeated filename
                foreach (var archivePath in entriesByArchive.Keys)
                {
                    TryDelete(archivePath);
                }
            }

            // Bundled entries point at files inside the extracted archive, so this can only go once installing is done
            TryDeleteDirectory(extractedArchivePath);

            _viewModel.DiscoverMods(Pathing.defaultModPath);

            // The profile is built from what actually landed on disk rather than from what was requested, so a
            // partial install still produces a working profile
            var installedMods = _viewModel.Mods.Where(m => String.Equals(m.SourceId, collection.SourceId, StringComparison.OrdinalIgnoreCase)).ToList();
            RecordInstalledMods(entriesByArchive, installedModsByArchive, installedMods);
            CreateProfileForCollection(collection);
            CollectionCache.Save(collection);

            await ReportCollectionResult(collection);
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
        /// sizes, and falls back to a mod count when it does not.
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
        /// Stardrop cannot fetch itself, and a dozen modals is not a usable experience.
        /// </summary>
        private async Task ReportCollectionResult(CollectionInstall collection)
        {
            var manualDownloads = collection.GetManualDownloads();
            var failures = collection.GetFailures();
            var installedCount = collection.Mods.Count(m => m.Status is CollectionModStatus.Installed);
            var reusedCount = collection.GetReusedCount();

            var summary = String.Format(Program.translation.Get("ui.message.collection_install_summary"), collection.Name, installedCount, collection.Mods.Count);

            if (reusedCount > 0)
            {
                summary += Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_reused"), reusedCount);
            }

            if (manualDownloads.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_manual_downloads"), manualDownloads.Count);
                summary += Environment.NewLine + String.Join(Environment.NewLine, manualDownloads.Select(m => $"  {m.Name}"));
            }

            if (failures.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_failures"), failures.Count);
                summary += Environment.NewLine + String.Join(Environment.NewLine, failures.Select(m => $"  {m.Name}{(String.IsNullOrEmpty(m.FailureReason) ? String.Empty : $" ({m.FailureReason})")}"));
            }

            if (String.IsNullOrEmpty(collection.InstallInstructions) is false)
            {
                summary += Environment.NewLine + Environment.NewLine + Program.translation.Get("ui.message.collection_curator_notes");
                summary += Environment.NewLine + collection.InstallInstructions;
            }

            await CreateWarningWindow(summary, Program.translation.Get("internal.ok"), windowWidth: 560);
        }
    }
}
