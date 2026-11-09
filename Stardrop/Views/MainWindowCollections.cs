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
        /// <summary>Length of the random prefix a kept configuration archive is stored under, ahead of its own name</summary>
        private const int STORED_OVERLAY_PREFIX_LENGTH = 8;

        /// <summary>Whether the revision check has already run this session, so a refresh does not repeat it</summary>
        private bool _hasCheckedCollectionRevisions;

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

            return await InstallOrUpdateCollection(domainName, slug, revisionNumber, nxmLink.Link);
        }

        /// <summary>
        /// Installs a collection, or applies a revision over the one already on disk. Split out from the nxm handler
        /// so the collections window can start an update from a record it already holds, without a link to parse.
        ///
        /// A null revision takes whatever the curator has published most recently, which is what an update wants. A
        /// stalled update passes the revision it was reaching for, so resuming finishes that one rather than jumping
        /// to a newer one the user has not agreed to.
        /// </summary>
        private async Task<bool> InstallOrUpdateCollection(string domainName, string slug, int? revisionNumber, string? sourceDescription = null)
        {
            if (Nexus.Client is null)
            {
                await CreateWarningWindow(Program.translation.Get("ui.message.require_nexus_login"), Program.translation.Get("internal.ok"));
                return false;
            }

            // Named in the failure messages, which read better pointing at the link the user clicked where there was
            // one and at the collection otherwise
            var failureSubject = String.IsNullOrEmpty(sourceDescription) ? slug : sourceDescription;

            // A collection keeps one identity across revisions, so a request for one already on disk is an update of
            // it rather than a second install
            var existingInstall = CollectionCache.Load(CollectionInstall.CreateSourceId(domainName, slug));

            var revision = await Nexus.Client.GetCollectionRevision(slug, revisionNumber, domainName);
            if (revision is null || String.IsNullOrEmpty(revision.DownloadLink))
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.message.failed_collection_get"), failureSubject), Program.translation.Get("internal.ok"));
                return false;
            }

            var collectionName = revision.Collection is null || String.IsNullOrEmpty(revision.Collection.Name) ? slug : revision.Collection.Name;
            var resolvedRevision = revision.RevisionNumber is null ? (revisionNumber is null ? 0 : revisionNumber.Value) : revision.RevisionNumber.Value;

            if (existingInstall is null)
            {
                if (Program.settings.IsAskingBeforeAcceptingNXM)
                {
                    var requestWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.confirm_nxm_collection_install"), collectionName));
                    if (await requestWindow.ShowDialog<bool>(this) is false)
                    {
                        return false;
                    }
                }
            }
            else
            {
                // Nothing to apply where the revision on disk is the one being asked for, unless it never finished
                // landing, in which case going round again is how the remaining entries get picked up
                if (resolvedRevision == existingInstall.RevisionNumber && existingInstall.IsUpdateInProgress() is false)
                {
                    await CreateWarningWindow(String.Format(Program.translation.Get("ui.message.collection_already_installed"), existingInstall.Name, resolvedRevision), Program.translation.Get("internal.ok"));
                    return false;
                }

                // Asked whatever the nxm confirmation setting says, as this writes over a collection the user
                // already has rather than adding something new
                var updateWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.confirm_collection_update"), collectionName, existingInstall.RevisionNumber, resolvedRevision));
                if (await updateWindow.ShowDialog<bool>(this) is false)
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

            var collection = Nexus.Client.CreateCollectionInstall(index, slug, resolvedRevision, domainName);

            // Worked out before anything is downloaded, so the install pass, the profile amendment and the summary
            // all read one answer rather than each deciding for themselves what changed
            var updatePlan = existingInstall is null ? null : ReconcileCollectionUpdate(existingInstall, collection);

            await InstallCollection(collection, extractedArchivePath, cancellationSource, updatePlan);

            return true;
        }

        /// <summary>
        /// Matches a newly fetched revision's entries against the ones already on disk, carrying the install state
        /// forward wherever the curator has not moved the pin. Entries whose file has changed are left Pending so
        /// the install pass treats them as work, and entries the revision no longer lists are handed back so the
        /// profile can drop them.
        ///
        /// Mutates the incoming record, which has not been saved or shown to anyone at this point.
        /// </summary>
        private static CollectionUpdatePlan ReconcileCollectionUpdate(CollectionInstall previous, CollectionInstall updated)
        {
            var plan = new CollectionUpdatePlan(previous);

            // Matched out of a pool rather than by lookup, as two entries in one collection can share a mod ID and
            // would otherwise both resolve to whichever of them was found first
            var unmatched = previous.Mods.ToList();

            foreach (var entry in updated.Mods)
            {
                var match = TakeMatchingEntry(unmatched, entry);
                if (match is null)
                {
                    plan.Added.Add(entry);
                    continue;
                }

                // An entry the previous revision never managed to install is work either way, so only a satisfied
                // one carries anything forward. Held configuration counts as well, as its file is already kept and
                // fetching it again would only put the user through another download
                if (IsSamePin(match, entry) && (match.IsSatisfied() || IsKeptHeldOverlay(previous, match)))
                {
                    entry.Status = match.Status;
                    entry.InstalledMods = match.InstalledMods;
                    entry.OverlayTargets = match.OverlayTargets;
                    entry.SourceArchivePath = match.SourceArchivePath;
                    entry.StoredOverlayName = match.StoredOverlayName;
                    entry.UnmatchedFileCount = match.UnmatchedFileCount;

                    plan.Unchanged.Add(entry);
                    continue;
                }

                // Left with the status CreateCollectionInstall gave it, so the install pass downloads it afresh
                plan.Replaced.Add(new CollectionEntryReplacement(match, entry));
            }

            plan.Removed.AddRange(unmatched);

            // The profile name is the user's, and the install date is when this collection first arrived rather than
            // when it was last touched
            updated.ProfileName = previous.ProfileName;
            updated.InstallTimestamp = previous.InstallTimestamp;
            updated.LatestRevisionNumber = previous.LatestRevisionNumber;

            // Held back until every entry lands, so a stalled update never reads as the revision it was reaching for
            updated.PendingRevisionNumber = updated.RevisionNumber;
            updated.RevisionNumber = previous.RevisionNumber;

            Program.helper.Log($"Applying revision {updated.PendingRevisionNumber} of {updated.Name} over revision {previous.RevisionNumber}: {plan.Added.Count} added, {plan.Replaced.Count} replaced, {plan.Removed.Count} removed, {plan.Unchanged.Count} unchanged");

            return plan;
        }

        /// <summary>
        /// Finds the previous revision's counterpart for an entry and takes it out of the pool, so a second entry
        /// sharing the same mod ID pairs with the second candidate rather than the first.
        ///
        /// The keys are tried in order of how well each survives a revision. A curator's own tag outlives a file
        /// change where a file ID does not, an external address outlives a checksum, and the name is a last resort
        /// for entries carrying none of the others.
        /// </summary>
        private static CollectionModEntry? TakeMatchingEntry(List<CollectionModEntry> candidates, CollectionModEntry entry)
        {
            CollectionModEntry? match = null;

            if (String.IsNullOrEmpty(entry.Tag) is false)
            {
                match = candidates.FirstOrDefault(c => String.Equals(c.Tag, entry.Tag, StringComparison.OrdinalIgnoreCase));
            }

            if (match is null && entry.NexusModId is not null)
            {
                match = candidates.FirstOrDefault(c => c.NexusModId == entry.NexusModId);
            }

            if (match is null && String.IsNullOrEmpty(entry.ExternalUri) is false)
            {
                match = candidates.FirstOrDefault(c => String.Equals(c.ExternalUri, entry.ExternalUri, StringComparison.OrdinalIgnoreCase));
            }

            if (match is null && String.IsNullOrEmpty(entry.Name) is false)
            {
                match = candidates.FirstOrDefault(c => String.Equals(c.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (match is not null)
            {
                candidates.Remove(match);
            }

            return match;
        }

        /// <summary>
        /// Whether two revisions point the same entry at the same file. A Nexus entry is settled by its file ID,
        /// where a Bundle, Browse or Direct entry has none and is judged on everything that identifies its file.
        /// </summary>
        private static bool IsSamePin(CollectionModEntry previous, CollectionModEntry updated)
        {
            if (previous.SourceType != updated.SourceType)
            {
                return false;
            }

            // The same file placed somewhere else is still a change, as is a record from before placements were
            // read, which placed the file by its archive's layout rather than where the curator had it
            if (HasSameFileHashes(previous, updated) is false)
            {
                return false;
            }

            if (updated.IsFromNexus())
            {
                return previous.NexusModId == updated.NexusModId && previous.NexusFileId == updated.NexusFileId;
            }

            return String.Equals(previous.Md5Checksum, updated.Md5Checksum, StringComparison.OrdinalIgnoreCase)
                && String.Equals(previous.ExternalUri, updated.ExternalUri, StringComparison.OrdinalIgnoreCase)
                && String.Equals(previous.FileExpression, updated.FileExpression, StringComparison.OrdinalIgnoreCase)
                && String.Equals(previous.LogicalFilename, updated.LogicalFilename, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether two revisions give an entry the same file placements, in any order</summary>
        private static bool HasSameFileHashes(CollectionModEntry previous, CollectionModEntry updated)
        {
            if (previous.FileHashes.Count != updated.FileHashes.Count)
            {
                return false;
            }

            var previousHashes = previous.FileHashes.Select(h => $"{h.Path}|{h.Md5}").ToHashSet(StringComparer.OrdinalIgnoreCase);

            return updated.FileHashes.All(h => previousHashes.Contains($"{h.Path}|{h.Md5}"));
        }

        /// <summary>
        /// Reads config.json out of each folder an entry being replaced occupies, so the user's own settings survive
        /// the delete AddMods performs when it writes the new version over the old one. Held as bytes rather than
        /// text, so nothing about the file's encoding or line endings is decided here.
        ///
        /// Only for the mods the curator supplies no configuration for. Where an overlay covers one of these, it is
        /// written after this is restored and wins, which is the point: the collection owns the configuration it
        /// ships and the user owns the rest.
        /// </summary>
        private static Dictionary<string, byte[]> CaptureReplacedConfigs(CollectionUpdatePlan plan, string installPath)
        {
            var configsByFolder = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var folderName in plan.GetReplacedFolderNames())
            {
                var configPath = Path.Combine(installPath, folderName, "config.json");

                try
                {
                    if (File.Exists(configPath) is false)
                    {
                        continue;
                    }

                    configsByFolder[folderName] = File.ReadAllBytes(configPath);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to hold onto the configuration in {folderName} across the update: {ex}", Helper.Status.Warning);
                }
            }

            return configsByFolder;
        }

        /// <summary>
        /// Puts the captured configuration back, skipping any folder the replacement did not recreate. Run before
        /// the overlays, so a curator's configuration is written over this rather than under it.
        /// </summary>
        private static void RestoreReplacedConfigs(Dictionary<string, byte[]> configsByFolder, string installPath)
        {
            foreach (var folderName in configsByFolder.Keys)
            {
                var folderPath = Path.Combine(installPath, folderName);

                try
                {
                    if (Directory.Exists(folderPath) is false)
                    {
                        continue;
                    }

                    File.WriteAllBytes(Path.Combine(folderPath, "config.json"), configsByFolder[folderName]);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to restore the configuration in {folderName} after the update: {ex}", Helper.Status.Warning);
                }
            }
        }

        /// <summary>
        /// Removes the folders a replaced entry left behind that its replacement did not write into, which happens
        /// when a mod renames its folder between versions. Left in place they would be discovered as part of this
        /// collection while nothing enables them.
        /// </summary>
        private static void RemoveStaleCollectionFolders(CollectionUpdatePlan plan, string installPath)
        {
            foreach (var folderName in plan.GetStaleFolderNames())
            {
                Program.helper.Log($"Removing {folderName}, as the entry that placed it now installs somewhere else");
                TryDeleteDirectory(Path.Combine(installPath, folderName));
            }
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
                        if (String.IsNullOrEmpty(entry.Key) || entry.Key.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase) || entry.Key.Contains(".DS_Store", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // The destination is built here rather than by ExtractFullPath, which writes the entry's key
                        // as it was recorded. Characters in the keys can contain values that Windows rejects, so they need to be parsed out
                        var outputPath = Pathing.GetSafeDestinationPath(targetFolder, entry.Key);
                        if (String.IsNullOrEmpty(outputPath))
                        {
                            Program.helper.Log($"Skipping {entry.Key} in the collection archive, as it does not resolve to a path inside {targetFolder}", Helper.Status.Warning);
                            continue;
                        }

                        var outputFolder = Path.GetDirectoryName(outputPath);
                        if (String.IsNullOrEmpty(outputFolder) is false)
                        {
                            Directory.CreateDirectory(outputFolder);
                        }

                        entry.WriteToFile(outputPath, new ExtractionOptions() { ExtractFullPath = false, Overwrite = true });
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
        private async Task InstallCollection(CollectionInstall collection, string extractedArchivePath, CancellationTokenSource cancellationSource, CollectionUpdatePlan? updatePlan = null)
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
                await AbandonCollectionInstall(collection, entriesByArchive.Keys.ToList(), installPath, extractedArchivePath, updatePlan is not null);
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

            // AddMods deletes the folder it is writing over, so anything the user configured in a mod being replaced
            // is read out first and put back below
            var replacedConfigs = updatePlan is null ? new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) : CaptureReplacedConfigs(updatePlan, installPath);

            var installedModsByArchive = new Dictionary<string, List<Mod>>(StringComparer.OrdinalIgnoreCase);
            if (orderedArchives.Count > 0)
            {
                // Never asked how to handle a mod already sitting at the target. The collection owns the version, so
                // the previous copy always goes, and the configuration the prompt exists to protect is carried
                // across separately above
                await AddMods(orderedArchives.ToArray(), installPath, installedModsByArchive, replaceWithoutAsking: true);

                RestoreReplacedConfigs(replacedConfigs, installPath);

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

            // Only knowable once the replacements have recorded where they landed, so this waits until here rather
            // than clearing the folders before the install
            var staleFolderCount = 0;
            if (updatePlan is not null)
            {
                staleFolderCount = updatePlan.GetStaleFolderNames().Count;
                RemoveStaleCollectionFolders(updatePlan, installPath);
            }

            UpdateLockWindow(Program.translation.Get("ui.message.collection_finalizing_configuration"));
            await YieldToLockWindow();

            // Last, as an overlay is copied into a mod folder that only exists once the mods above are installed and
            // their folder names have been recorded. Every configuration archive is kept first, so one whose mods
            // are still to be downloaded manually can be written once they arrive rather than ahead of them
            StoreCollectionOverlays(collection, overlaysByArchive);
            var appliedOverlays = ApplyReadyOverlays(collection, installPath);

            // Only once the new record's entries have taken what they need, so nothing still in use is cleared
            if (updatePlan is not null)
            {
                RemoveUnusedStoredOverlays(collection, updatePlan);
            }

            // Configuration only lands at this point, so a second pass is needed for the mods it touched to report
            // that they now have a config
            if (appliedOverlays.Count > 0 || staleFolderCount > 0)
            {
                _viewModel.DiscoverMods(Pathing.defaultModPath);
            }

            // Bundled entries and overlays are both read out of the extracted archive, so this can only go once
            // installing has finished
            TryDeleteDirectory(extractedArchivePath);

            UpdateLockWindow(Program.translation.Get("ui.message.collection_finalizing_profile"));
            await YieldToLockWindow();

            var profile = updatePlan is null ? CreateProfileForCollection(collection) : UpdateProfileForCollection(collection, updatePlan);

            // Only moves onto the new revision once nothing is left outstanding, so a collection waiting on a manual
            // download stays on the revision it actually has
            collection.TryCompleteUpdate();

            CollectionCache.Save(collection);

            // The rescans above ran before a first install's record existed, which left its mods showing the source ID
            _viewModel.RefreshCollectionDetails();

            // An nxm collection link is the one thing not held back while this window is open, so a collection can
            // finish installing behind it. Without this the window keeps showing the list it read when it opened,
            // which is missing the collection the user has just watched install
            _collectionsWindow?.RefreshCollections();

            // Handed straight over to the summary, so the two never overlap
            SetLockState(false);

            if (updatePlan is null)
            {
                await ReportCollectionResult(collection);

                // After the summary rather than before it. Switching profiles can raise its own dialog over unsaved
                // configuration on the profile being left, which would collide with the summary still being open
                SelectCollectionProfile(profile);

                return;
            }

            // The row the user pressed update on is still showing the previous revision, so the window behind is
            // brought back in step before the summary lands over it
            _collectionsWindow?.RefreshCollections();

            await ReportCollectionUpdateResult(collection, updatePlan);

            if (await OfferToRemoveDroppedEntries(collection, updatePlan, installPath))
            {
                _viewModel.DiscoverMods(Pathing.defaultModPath);
                _viewModel.EvaluateRequirements();
                _collectionsWindow?.RefreshCollections();
            }

            // An update does not move the user, as they may well be sitting on a profile of their own. The grid is
            // put back in step only where the collection's own profile is the one on screen
            if (GetCurrentProfile() == profile)
            {
                _viewModel.EnableModsByProfile(profile);
                _viewModel.UpdateFilter();
            }

            RefreshCollectionUpdateCount();
        }

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

        /// <summary>
        /// Removes the entries that carry no manifest, handing them back for separate treatment. AddMods has nothing
        /// to install from a file with no manifest and skips it, which is the wrong outcome here: a curator's
        /// configuration arrives this way, whether bundled in the collection archive or downloaded like any other
        /// entry and the mod rules point at it as their source.
        /// </summary>
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
        /// Keeps a copy of each manifest-less entry's archive under the collection's overlay store, then clears the
        /// original. Nothing is written into the mod folder here, as <see cref="ApplyReadyOverlays"/> decides when
        /// each one is safe to write.
        /// </summary>
        private static void StoreCollectionOverlays(CollectionInstall collection, Dictionary<string, CollectionModEntry> overlaysByArchive)
        {
            foreach (var archivePath in overlaysByArchive.Keys)
            {
                StoreOverlay(collection, overlaysByArchive[archivePath], archivePath);
                TryDelete(archivePath);
            }
        }

        /// <summary>
        /// Copies a configuration archive into the collection's overlay store and marks the entry as held until
        /// <see cref="ApplyReadyOverlays"/> writes it. Copied rather than moved, as a single archive dropped in by
        /// the user can satisfy entries in more than one collection and each keeps a copy of its own.
        /// </summary>
        private static bool StoreOverlay(CollectionInstall collection, CollectionModEntry entry, string archivePath)
        {
            var previousName = entry.StoredOverlayName;

            try
            {
                var storeFolder = Pathing.GetCollectionOverlaysFolderPath(collection.SourceId);
                Directory.CreateDirectory(storeFolder);

                // Prefixed, as two entries can pin files that share a name
                var storedName = $"{Guid.NewGuid().ToString("N").Substring(0, STORED_OVERLAY_PREFIX_LENGTH)}_{Path.GetFileName(archivePath)}";
                File.Copy(archivePath, Path.Combine(storeFolder, storedName), true);

                entry.StoredOverlayName = storedName;
                entry.Status = CollectionModStatus.AwaitingTargets;
                entry.FailureReason = null;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to keep the configuration {entry.Name} for the collection {collection.Name}: {ex}", Helper.Status.Alert);

                entry.Status = CollectionModStatus.Failed;
                entry.FailureReason = Program.translation.Get("ui.message.collection_reason_overlay_failed");

                return false;
            }

            // The entry arriving a second time replaces what it kept the first time
            if (String.IsNullOrEmpty(previousName) is false)
            {
                TryDelete(Pathing.GetCollectionOverlayPath(collection.SourceId, previousName));
            }

            return true;
        }

        /// <summary>
        /// Writes every held configuration entry whose targets have all arrived, whole and with its structure
        /// intact, then records which mods it landed on top of. Walked in install order, so where two of them place
        /// the same path the one the rules put last wins. One ordered after another is only written once that
        /// one has been.
        ///
        /// An entry with a target still missing stays held. Writing it now would put its files into a folder the mod
        /// later lands in, where any file the mod includes at the same path would take the curator's place.
        /// </summary>
        private static List<CollectionModEntry> ApplyReadyOverlays(CollectionInstall collection, string installPath)
        {
            var applied = new List<CollectionModEntry>();

            foreach (var entry in collection.GetInstallOrder().Where(m => m.Status is CollectionModStatus.AwaitingTargets).ToList())
            {
                var blockingTargets = collection.GetBlockingTargets(entry);
                if (blockingTargets.Count > 0)
                {
                    Program.helper.Log($"Holding the configuration {entry.Name} in {collection.Name} until {String.Join(", ", blockingTargets.Select(t => t.Name))} is installed");
                    continue;
                }

                var storedPath = GetStoredOverlayPath(collection, entry);
                if (storedPath is null)
                {
                    entry.Status = CollectionModStatus.Failed;
                    entry.FailureReason = Program.translation.Get("ui.message.collection_reason_overlay_failed");
                    continue;
                }

                // A replicated entry is placed file by file where the curator had each one, which is what lets it
                // replace files inside another mod's folder. Anything else is written as its archive lays it out
                if (entry.FileHashes.Count > 0)
                {
                    var placement = PlaceOverlayByHashes(collection, entry, storedPath, installPath);
                    if (placement.WrittenPaths.Count == 0)
                    {
                        entry.Status = CollectionModStatus.Failed;
                        entry.FailureReason = Program.translation.Get(placement.CouldNotRead ? "ui.message.collection_reason_overlay_failed" : "ui.message.collection_reason_no_hash_matches");
                        continue;
                    }

                    entry.UnmatchedFileCount = placement.UnmatchedCount;
                    entry.OverlayTargets = placement.TouchedTargets;
                }
                else
                {
                    if (ExtractOverlay(storedPath, installPath) <= 0)
                    {
                        entry.Status = CollectionModStatus.Failed;
                        entry.FailureReason = Program.translation.Get("ui.message.collection_reason_overlay_failed");
                        continue;
                    }

                    entry.OverlayTargets = GetOverwrittenTargets(collection, entry, storedPath);
                }

                entry.Status = CollectionModStatus.AppliedAsOverlay;

                applied.Add(entry);
            }

            return applied;
        }

        /// <summary>
        /// Writes configuration the rules place after a late arrival back over it. A mod downloaded once its
        /// configuration was already written lands on top of it. Anything the mod includes at the same path, such
        /// as a default config.json, would otherwise take the curator's place.
        ///
        /// Only the files inside the folders the arrival wrote are touched, so a setting the user has since changed
        /// in any other mod is left as it is. Configuration written before archives were kept has nothing to write
        /// back from and is passed over.
        /// </summary>
        private static int ReapplyOverlaysOverEntry(CollectionInstall collection, CollectionModEntry arrivedEntry, List<string> arrivedFolders, string installPath)
        {
            if (arrivedFolders.Count == 0)
            {
                return 0;
            }

            var installOrder = collection.GetInstallOrder();
            var arrivedRank = installOrder.IndexOf(arrivedEntry);
            if (arrivedRank < 0)
            {
                return 0;
            }

            var reappliedCount = 0;
            foreach (var overlay in installOrder.Skip(arrivedRank + 1).Where(m => m.Status is CollectionModStatus.AppliedAsOverlay))
            {
                var storedPath = GetStoredOverlayPath(collection, overlay);
                if (storedPath is null)
                {
                    continue;
                }

                var written = overlay.FileHashes.Count > 0 ? PlaceOverlayByHashes(collection, overlay, storedPath, installPath, arrivedFolders).WrittenPaths.Count : ExtractOverlay(storedPath, installPath, arrivedFolders);
                if (written <= 0)
                {
                    continue;
                }

                Program.helper.Log($"Wrote {written} file(s) of the configuration {overlay.Name} back over {arrivedEntry.Name}, as the curator's rules place it after that mod");

                if (overlay.OverlayTargets.Contains(arrivedEntry.Name, StringComparer.OrdinalIgnoreCase) is false)
                {
                    overlay.OverlayTargets.Add(arrivedEntry.Name);
                }

                reappliedCount += 1;
            }

            return reappliedCount;
        }

        /// <summary>
        /// The folders an installed entry's mods sit in, relative to the collection's folder and separated by forward
        /// slashes, which is the form an overlay archive's entries are compared in. Read from the discovered mods
        /// rather than from <see cref="InstalledModRecord.FolderName"/>, which drops any parent folder a mod was
        /// nested in.
        /// </summary>
        private static List<string> GetEntryModFolders(string installPath, List<Mod> collectionMods, CollectionModEntry entry)
        {
            var folders = new List<string>();

            foreach (var installedMod in entry.InstalledMods)
            {
                var mod = collectionMods.FirstOrDefault(m => m.UniqueId.Equals(installedMod.UniqueId, StringComparison.OrdinalIgnoreCase));
                if (mod is null || mod.ModFileInfo.Directory is null)
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(installPath, mod.ModFileInfo.Directory.FullName).Replace('\\', '/');
                if (relativePath == "." || relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
                {
                    continue;
                }

                folders.Add(relativePath.TrimEnd('/'));
            }

            return folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// The name a kept file had before it was stored. Matters only for a loose configuration file, which is
        /// copied across under its own name rather than extracted
        /// </summary>
        private static string GetOriginalOverlayName(string storedPath)
        {
            var fileName = Path.GetFileName(storedPath);
            if (fileName.Length > STORED_OVERLAY_PREFIX_LENGTH + 1 && fileName[STORED_OVERLAY_PREFIX_LENGTH] == '_')
            {
                return fileName.Substring(STORED_OVERLAY_PREFIX_LENGTH + 1);
            }

            return fileName;
        }

        /// <summary>The kept archive for a configuration entry, or null where none was kept or it has since gone missing</summary>
        private static string? GetStoredOverlayPath(CollectionInstall collection, CollectionModEntry entry)
        {
            if (String.IsNullOrEmpty(entry.StoredOverlayName))
            {
                return null;
            }

            var storedPath = Pathing.GetCollectionOverlayPath(collection.SourceId, entry.StoredOverlayName);
            if (File.Exists(storedPath) is false)
            {
                Program.helper.Log($"The kept configuration for {entry.Name} in {collection.Name} is missing from {storedPath}", Helper.Status.Warning);
                return null;
            }

            return storedPath;
        }

        /// <summary>Whether an entry is held configuration whose kept archive is still on disk</summary>
        private static bool IsKeptHeldOverlay(CollectionInstall collection, CollectionModEntry entry)
        {
            return entry.Status is CollectionModStatus.AwaitingTargets && GetStoredOverlayPath(collection, entry) is not null;
        }

        /// <summary>
        /// Clears the kept archives an update left without an owner, being those of entries the revision dropped or
        /// pinned to a different file. The files they wrote stay where they are, the same as a dropped mod's do.
        /// </summary>
        private static void RemoveUnusedStoredOverlays(CollectionInstall collection, CollectionUpdatePlan plan)
        {
            var namesInUse = collection.Mods.Where(m => String.IsNullOrEmpty(m.StoredOverlayName) is false).Select(m => m.StoredOverlayName!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var previousEntries = plan.Removed.Concat(plan.Replaced.Select(r => r.Previous));

            foreach (var entry in previousEntries)
            {
                if (String.IsNullOrEmpty(entry.StoredOverlayName) || namesInUse.Contains(entry.StoredOverlayName))
                {
                    continue;
                }

                TryDelete(Pathing.GetCollectionOverlayPath(collection.SourceId, entry.StoredOverlayName));
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

        /// <summary>
        /// Writes a configuration archive's files into the collection's folder, returning how many were written or
        /// -1 where the archive could not be read.
        /// </summary>
        /// <param name="onlyFolders">
        /// When given, limits the write to files inside these folders, relative to the destination and separated by
        /// forward slashes. A loose file sits outside every folder, so it is never written under this limit
        /// </param>
        private static int ExtractOverlay(string archivePath, string destinationPath, List<string>? onlyFolders = null)
        {
            try
            {
                Directory.CreateDirectory(destinationPath);

                if (IsArchiveFile(archivePath) is false)
                {
                    if (onlyFolders is not null)
                    {
                        return 0;
                    }

                    var originalName = GetOriginalOverlayName(archivePath);
                    File.Copy(archivePath, Path.Combine(destinationPath, originalName), true);
                    Program.helper.Log($"Copied the configuration file {originalName} into {destinationPath}");

                    return 1;
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

                    if (onlyFolders is not null && IsInsideAnyFolder(String.Join('/', segments), onlyFolders) is false)
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

                return written;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to place the contents of {archivePath} into {destinationPath}: {ex}", Helper.Status.Alert);

                return -1;
            }
        }

        /// <summary>Whether a forward slash separated path sits inside one of the given folders, compared the same way</summary>
        private static bool IsInsideAnyFolder(string relativePath, List<string> folders)
        {
            return folders.Any(f => relativePath.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>What placing a replicated entry by its checksums did, for the caller to record on the entry</summary>
        private class HashPlacementResult
        {
            /// <summary>Destinations written, relative to the collection's folder with forward slashes</summary>
            public List<string> WrittenPaths { get; } = new List<string>();
            /// <summary>Names of the mods whose folders received at least one file</summary>
            public List<string> TouchedTargets { get; } = new List<string>();
            /// <summary>Placements whose checksum matched no file in the archive</summary>
            public int UnmatchedCount { get; set; }
            /// <summary>Whether the archive could not be read at all, as opposed to holding nothing that matched</summary>
            public bool CouldNotRead { get; set; }
        }

        /// <summary>
        /// Places a replicated entry's files where the curator had them, the way Vortex does for its replicate mode.
        /// Each file in the archive is identified by its checksum rather than its name or folder, then written to
        /// every path the entry's hashes list gives that checksum. One file can therefore land in several places,
        /// and anything in the archive the list does not mention is left out.
        ///
        /// The paths describe the curator's mod folder, which Stardrop does not necessarily lay out the same way. See
        /// <see cref="ResolveHashDestination"/> for how each one is mapped onto the collection's folder.
        /// </summary>
        /// <param name="onlyFolders">When given, only placements landing inside these folders are written. Used to write files back over a mod that arrived late</param>
        private static HashPlacementResult PlaceOverlayByHashes(CollectionInstall collection, CollectionModEntry entry, string storedPath, string installPath, List<string>? onlyFolders = null)
        {
            var result = new HashPlacementResult();
            var neededChecksums = entry.FileHashes.Select(h => h.Md5).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tempFolder = Path.Combine(Path.GetTempPath(), "Stardrop", Guid.NewGuid().ToString("N"));

            try
            {
                var filesByChecksum = ReadFilesByChecksum(storedPath, tempFolder, neededChecksums);
                if (filesByChecksum is null)
                {
                    result.CouldNotRead = true;
                    return result;
                }

                Directory.CreateDirectory(installPath);
                var installRoot = Path.GetFullPath(installPath) + Path.DirectorySeparatorChar;
                var fileIndex = BuildFileIndex(installPath);
                var (modFolders, targetsByFolder, isLimitedToTargets) = FindPlacementFolders(collection, entry, fileIndex);

                foreach (var fileHash in entry.FileHashes)
                {
                    if (filesByChecksum.TryGetValue(fileHash.Md5, out var sourcePath) is false)
                    {
                        Program.helper.Log($"No file in {entry.Name} matches the checksum the curator recorded for {fileHash.Path}", Helper.Status.Warning);
                        result.UnmatchedCount += 1;
                        continue;
                    }

                    var destination = ResolveHashDestination(fileHash.Path, fileIndex, modFolders, isLimitedToTargets);
                    if (destination is null)
                    {
                        Program.helper.Log($"Skipping {fileHash.Path} from {entry.Name}, as it does not resolve to a path inside the collection's folder", Helper.Status.Warning);
                        continue;
                    }

                    if (onlyFolders is not null && IsInsideAnyFolder(destination, onlyFolders) is false)
                    {
                        continue;
                    }

                    // The recorded path comes from a third party, so it is checked again once it is a real path
                    var destinationPath = Path.GetFullPath(Path.Combine(installPath, Path.Combine(destination.Split('/'))));
                    if (destinationPath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase) is false)
                    {
                        Program.helper.Log($"Skipping {fileHash.Path} from {entry.Name}, as it points outside {installPath}", Helper.Status.Warning);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    File.Copy(sourcePath, destinationPath, true);

                    // Recorded so a later placement in the same pass resolves against it, keeping the casing consistent
                    fileIndex.TryAdd(destination, destination);
                    result.WrittenPaths.Add(destination);

                    var touchedFolder = targetsByFolder.Keys.FirstOrDefault(f => destination.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase));
                    if (touchedFolder is not null && result.TouchedTargets.Contains(targetsByFolder[touchedFolder], StringComparer.OrdinalIgnoreCase) is false)
                    {
                        result.TouchedTargets.Add(targetsByFolder[touchedFolder]);
                    }
                }

                Program.helper.Log($"Placed {result.WrittenPaths.Count} file(s) from {entry.Name} by checksum, with {result.UnmatchedCount} of {entry.FileHashes.Count} recorded file(s) missing from the archive");
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to place the files of {entry.Name} by checksum: {ex}", Helper.Status.Alert);
                result.CouldNotRead = result.WrittenPaths.Count == 0;
            }
            finally
            {
                TryDeleteDirectory(tempFolder);
            }

            return result;
        }

        /// <summary>
        /// Extracts every file the entry needs into a scratch folder, keyed by checksum. Files whose checksum no
        /// placement asks for are discarded as soon as they are read, so the scratch folder only ever holds what is
        /// about to be written. Returns null where the archive could not be read.
        /// </summary>
        private static Dictionary<string, string>? ReadFilesByChecksum(string storedPath, string tempFolder, HashSet<string> neededChecksums)
        {
            var filesByChecksum = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // A loose file is its own single entry
            if (IsArchiveFile(storedPath) is false)
            {
                var checksum = Toolkit.GetFileChecksum(storedPath);
                if (checksum is null)
                {
                    return null;
                }

                if (neededChecksums.Contains(checksum))
                {
                    filesByChecksum[checksum] = storedPath;
                }

                return filesByChecksum;
            }

            try
            {
                Directory.CreateDirectory(tempFolder);

                var fileNumber = 0;
                using var archive = ArchiveFactory.OpenArchive(storedPath);
                foreach (var archiveEntry in archive.Entries.Where(e => e.IsDirectory is false && String.IsNullOrEmpty(e.Key) is false))
                {
                    if (archiveEntry.Key!.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Numbered rather than named, as the archive's names play no part in where anything goes
                    var tempPath = Path.Combine(tempFolder, fileNumber.ToString());
                    fileNumber += 1;

                    archiveEntry.WriteToFile(tempPath, new ExtractionOptions() { ExtractFullPath = false, Overwrite = true });

                    var checksum = Toolkit.GetFileChecksum(tempPath);
                    if (checksum is null || neededChecksums.Contains(checksum) is false || filesByChecksum.ContainsKey(checksum))
                    {
                        TryDelete(tempPath);
                        continue;
                    }

                    filesByChecksum[checksum] = tempPath;
                }

                return filesByChecksum;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to read {storedPath} while matching its files by checksum: {ex}", Helper.Status.Alert);
                return null;
            }
        }

        /// <summary>
        /// Every file under the collection's folder, relative to it with forward slashes. Keyed without regard to
        /// case and holding the path as it is on disk, so a placement written with different casing lands on the
        /// existing file rather than beside it on a case-sensitive file system.
        /// </summary>
        private static Dictionary<string, string> BuildFileIndex(string installPath)
        {
            var fileIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(installPath) is false)
            {
                return fileIndex;
            }

            foreach (var filePath in Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(installPath, filePath).Replace('\\', '/');
                fileIndex.TryAdd(relativePath, relativePath);
            }

            return fileIndex;
        }

        /// <summary>
        /// The mod folders a replicated entry's placements are anchored to, relative to the collection's folder.
        /// Where the entry's rules name mods, only their folders are used, which keeps a file from landing in an
        /// unrelated mod that happens to share a file name. Otherwise every mod folder in the collection is a
        /// candidate and matching has to be stricter.
        /// </summary>
        private static (List<string> Folders, Dictionary<string, string> TargetsByFolder, bool IsLimitedToTargets) FindPlacementFolders(CollectionInstall collection, CollectionModEntry entry, Dictionary<string, string> fileIndex)
        {
            // A folder holding a manifest is a mod folder, wherever it sits
            var manifestFolders = fileIndex.Values.Where(p => String.Equals(Path.GetFileName(p), "manifest.json", StringComparison.OrdinalIgnoreCase)).Select(p => Path.GetDirectoryName(p)?.Replace('\\', '/')).Where(p => String.IsNullOrEmpty(p) is false).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var targetsByFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in collection.GetOverlayTargets(entry))
            {
                foreach (var installedMod in target.InstalledMods.Where(m => String.IsNullOrEmpty(m.FolderName) is false))
                {
                    foreach (var folder in manifestFolders.Where(f => String.Equals(GetLastSegment(f), installedMod.FolderName, StringComparison.OrdinalIgnoreCase)))
                    {
                        targetsByFolder.TryAdd(folder, target.Name);
                    }
                }
            }

            if (targetsByFolder.Count > 0)
            {
                return (targetsByFolder.Keys.ToList(), targetsByFolder, true);
            }

            return (manifestFolders, targetsByFolder, false);
        }

        /// <summary>
        /// Maps one of the curator's paths onto the collection's folder, trying each of these in turn:
        ///
        /// 1. The path exactly as recorded, where a file already sits there.
        /// 2. Anchored on a mod folder: the deepest folder in the path that shares its name with one of the mod
        ///    folders, with the rest of the path placed under that folder. This is what absorbs the curator's folder
        ///    layout differing from Stardrop's above the mod folder itself.
        /// 3. The longest ending of the path that matches exactly one existing file in the mod folders. A single
        ///    file name only counts where the folders are limited to the entry's own targets.
        /// 4. The path as recorded, as a new file.
        ///
        /// Whatever is chosen is corrected to the casing already on disk. Returns null for a path that tries to
        /// climb out of the collection's folder.
        /// </summary>
        private static string? ResolveHashDestination(string recordedPath, Dictionary<string, string> fileIndex, List<string> modFolders, bool isLimitedToTargets)
        {
            var segments = recordedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(s => s == "." || s == ".."))
            {
                return null;
            }

            var normalizedPath = String.Join('/', segments);
            if (fileIndex.TryGetValue(normalizedPath, out var existingPath))
            {
                return existingPath;
            }

            // Walked from the deepest folder up, so a wrapper folder sharing the mod's name never wins over the mod's own
            for (int index = segments.Length - 2; index >= 0; index--)
            {
                var anchor = modFolders.FirstOrDefault(f => String.Equals(GetLastSegment(f), segments[index], StringComparison.OrdinalIgnoreCase));
                if (anchor is null)
                {
                    continue;
                }

                var anchoredPath = $"{anchor}/{String.Join('/', segments.Skip(index + 1))}";

                return fileIndex.TryGetValue(anchoredPath, out var anchoredExisting) ? anchoredExisting : MatchExistingCasing(anchoredPath, fileIndex);
            }

            var candidates = fileIndex.Values.Where(p => modFolders.Any(f => p.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase))).ToList();
            var shortestEnding = isLimitedToTargets ? 1 : 2;
            for (int length = segments.Length - 1; length >= shortestEnding; length--)
            {
                var ending = "/" + String.Join('/', segments.Skip(segments.Length - length));
                var matches = candidates.Where(p => ("/" + p).EndsWith(ending, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 1)
                {
                    return matches[0];
                }

                // Several files share this ending. A shorter one would only match more of them
                if (matches.Count > 1)
                {
                    break;
                }
            }

            return MatchExistingCasing(normalizedPath, fileIndex);
        }

        /// <summary>
        /// Rewrites a new file's folders in the casing they already have on disk, so it joins the existing folder
        /// on a case-sensitive file system rather than creating a second one beside it.
        /// </summary>
        private static string MatchExistingCasing(string relativePath, Dictionary<string, string> fileIndex)
        {
            var segments = relativePath.Split('/');
            var folderDepth = segments.Length - 1;

            for (int depth = folderDepth; depth > 0; depth--)
            {
                var folder = String.Join('/', segments.Take(depth));
                var existingFile = fileIndex.Values.FirstOrDefault(p => p.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));
                if (existingFile is null)
                {
                    continue;
                }

                var existingFolder = existingFile.Substring(0, folder.Length);

                return $"{existingFolder}/{String.Join('/', segments.Skip(depth))}";
            }

            return relativePath;
        }

        private static string GetLastSegment(string relativePath)
        {
            var separatorIndex = relativePath.LastIndexOf('/');

            return separatorIndex < 0 ? relativePath : relativePath.Substring(separatorIndex + 1);
        }

        /// <summary>
        /// Undoes a cancelled install. Nothing was written to the mod folder yet, as installing only happens after
        /// the download loop, so this comes down to clearing the archives that had already been fetched.
        /// </summary>
        private async Task AbandonCollectionInstall(CollectionInstall collection, List<string> downloadedArchives, string installPath, string extractedArchivePath, bool isUpdate = false)
        {
            Program.helper.Log($"Discarding the cancelled install of the collection {collection.Name}, removing {downloadedArchives.Count} downloaded archive(s)");

            foreach (var archivePath in downloadedArchives)
            {
                TryDelete(archivePath);
            }

            // The collection's own archive and the folder it was extracted into
            TryDeleteDirectory(extractedArchivePath);

            // Created before the loop, so it exists even when nothing was ever placed in it. Skipped for an update,
            // where the folder holds the revision the user already had and nothing has been written over it yet,
            // and skipped when a record exists, as that means an earlier install owns the folder and its contents
            if (isUpdate is false && CollectionCache.Load(collection.SourceId) is null)
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
                var safeName = Pathing.GetSafePathSegment(searchName);
                var candidates = root.GetFiles("*", SearchOption.AllDirectories).Where(f => IsBundleNameMatch(f.Name, safeName)).ToList();
                if (candidates.FirstOrDefault(f => f.Name.Equals(safeName, StringComparison.OrdinalIgnoreCase)) is FileInfo exact)
                {
                    return exact;
                }

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
                var safeName = Pathing.GetSafePathSegment(searchName);
                var candidates = root.GetDirectories("*", SearchOption.AllDirectories).Where(d => IsBundleNameMatch(d.Name, safeName)).ToList();

                return candidates.FirstOrDefault(d => d.Name.Equals(safeName, StringComparison.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to search {root.FullName} for the bundled folder {searchName}: {ex.Message}", Helper.Status.Warning);

                return null;
            }
        }

        /// <summary>
        /// Whether something on disk answers to the name collection.json recorded for a bundled entry. The names are
        /// compared rather than handed to a search pattern (due to bundled entries possibly containing wildcard characters)
        /// </summary>
        private static bool IsBundleNameMatch(string candidateName, string safeSearchName)
        {
            if (candidateName.Equals(safeSearchName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Path.GetFileNameWithoutExtension(candidateName).Equals(Path.GetFileNameWithoutExtension(safeSearchName), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Packs a bundled folder into an archive of its own, written alongside the collection's other downloads so
        /// that the cleanup after installing removes it along with the rest. Whether the folder's own name is kept
        /// as the archive's root depends on its shape, as decided by <see cref="IsBundleContainer"/>.
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

                var isContainer = IsBundleContainer(folder);
                if (isContainer)
                {
                    Program.helper.Log($"Packing the contents of the bundled folder {folder.Name} without the folder itself, as it contains mod related data");
                }

                // Uncompressed, as this is read back and deleted within the same install
                ZipFile.CreateFromDirectory(folder.FullName, archivePath, CompressionLevel.NoCompression, includeBaseDirectory: isContainer is false);

                return archivePath;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to pack the bundled folder {folder.FullName}: {ex}", Helper.Status.Alert);

                return null;
            }
        }

        /// <summary>
        /// Whether a bundled folder only holds an entry's contents rather than being part of them.
        ///
        /// A folder with manifest.json directly inside is the mod itself, so its name is what the mod installs as.
        /// Anything else with subfolders is laid out like a Mods folder (such as a config bundle holding ModName/config.json), 
        /// where keeping the bundle's own name would place every file one level deeper than the mod it is meant for.
        /// A folder of loose files keeps its name.
        /// </summary>
        private static bool IsBundleContainer(DirectoryInfo folder)
        {
            if (folder.EnumerateFiles().Any(f => f.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return folder.EnumerateDirectories().Any();
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
        /// Amends the collection's existing profile rather than generating a new one. The profile carries state the
        /// collection does not own: mods the user enabled alongside it, mods of the collection's own that they
        /// turned off, the name they may have given it, their notes and their preserved configuration. Rebuilding it
        /// would take all of that with it.
        ///
        /// Falls back to generating one where the profile has been deleted since the collection was installed.
        /// </summary>
        private Profile UpdateProfileForCollection(CollectionInstall collection, CollectionUpdatePlan plan)
        {
            // Matched on the source ID rather than the name, as the user is free to rename the profile
            var profile = _editorView.Profiles.FirstOrDefault(p => IsCollectionProfile(p, collection));
            if (profile is null)
            {
                Program.helper.Log($"The profile for {collection.Name} could not be found, so one is generated for the revision being applied", Helper.Status.Warning);
                return CreateProfileForCollection(collection);
            }

            collection.ProfileName = profile.Name;

            // The install pass enables everything it installs, so a mod the previous revision placed that the
            // profile does not list is one the user turned off. Absence is the only record of that choice, which is
            // why it is read against what was installed rather than taken from the profile alone
            var previouslyInstalled = plan.GetPreviouslyInstalledIds();
            var disabledByUser = previouslyInstalled.Where(id => profile.EnabledModIds.Any(r => IsCollectionReference(r, collection) && String.Equals(r.UniqueId, id, StringComparison.OrdinalIgnoreCase)) is false).ToList();

            var updatedReferences = collection.GetEnabledModReferences();

            // A reference pointing at this collection that neither the previous revision nor this one accounts for
            // is a mod the user added into the collection's folder themselves. The record knows nothing about it,
            // so rebuilding the collection's references from the record alone would turn it off on every revision
            var addOns = profile.EnabledModIds.Where(r => IsCollectionReference(r, collection) && previouslyInstalled.Contains(r.UniqueId, StringComparer.OrdinalIgnoreCase) is false && updatedReferences.Contains(r) is false).ToList();

            // References pointing anywhere but this collection are the user's own additions and are left as they are
            var references = profile.EnabledModIds.Where(r => IsCollectionReference(r, collection) is false).ToList();
            references.AddRange(addOns);

            foreach (var reference in updatedReferences)
            {
                if (disabledByUser.Contains(reference.UniqueId, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                references.Add(reference);
            }

            Program.helper.Log($"Amending the profile {profile.Name}: {references.Count} mod(s) enabled, with {disabledByUser.Count} left off as the user had disabled them and {addOns.Count} kept as add-on(s) the collection does not pin");

            // Notes, PreservedModConfigs, Name and IsProtected are deliberately untouched
            profile.EnabledModIds = references;
            _editorView.CreateProfile(profile, force: true);

            return profile;
        }

        /// <summary>Whether a profile reference points at a mod this collection installed</summary>
        private static bool IsCollectionReference(ModReference reference, CollectionInstall collection)
        {
            return String.Equals(reference.SourceId, collection.SourceId, StringComparison.OrdinalIgnoreCase);
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
            // The revision is deliberately absent. The profile now outlives the revision it was generated for, so a
            // number in the name would be wrong from the first update onwards
            var baseName = String.IsNullOrEmpty(collection.Name) ? collection.Slug : collection.Name;
            var candidate = baseName;

            int suffix = 2;
            // Compared on file name, so a name differing only in characters a file can't hold still gets its own suffix
            while (_editorView.IsProfileNameTaken(candidate))
            {
                candidate = $"{baseName} [{suffix}]";
                suffix++;
            }

            return candidate;
        }

        /// <summary>
        /// Handles an nxm mod link that matches an entry a collection is still waiting on. A Mod Manager Download
        /// link carries the key and expiry pair that authorises the download, so this is the one route a non-premium
        /// account has into a collection Stardrop could not fetch on its behalf.
        /// </summary>
        /// <returns>Whether the link was handled here, declined in favour of an ordinary install, or matched nothing</returns>
        private async Task<CollectionEntryLinkResult> TryProcessCollectionEntryLink(NXM nxmLink)
        {
            if (Nexus.Client is null || String.IsNullOrEmpty(nxmLink.Link))
            {
                return CollectionEntryLinkResult.NotMatched;
            }

            if (NexusClient.TryParseModNxmLink(nxmLink.Link, out _, out var modId, out var fileId) is false)
            {
                return CollectionEntryLinkResult.NotMatched;
            }

            var matches = FindUnsatisfiedCollectionEntries(modId, fileId);
            if (matches.Count == 0)
            {
                return CollectionEntryLinkResult.NotMatched;
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
                    return CollectionEntryLinkResult.Declined;
                }
            }

            var fileSafetyResult = await Nexus.Client.ValidateFileSafety(modId, fileId);
            if (fileSafetyResult is false)
            {
                await ReportCollectionEntryResult(Program.translation.Get("ui.warning.file_quarantined"), isFailure: true);
                return CollectionEntryLinkResult.Handled;
            }

            if (fileSafetyResult is null)
            {
                var safetyWindow = new MessageWindow(Program.translation.Get("ui.warning.failed_to_verify_mod_file"));
                KeepDialogAboveSiblings(safetyWindow);

                if (await safetyWindow.ShowDialog<bool>(this) is false)
                {
                    return CollectionEntryLinkResult.Handled;
                }
            }

            var archivePath = await DownloadCollectionEntryViaNXM(nxmLink, modId, fileId);
            if (String.IsNullOrEmpty(archivePath))
            {
                await ReportCollectionEntryResult(String.Format(Program.translation.Get("ui.warning.failed_nexus_install"), entryName), isFailure: true);
                return CollectionEntryLinkResult.Handled;
            }

            // The count of what a collection still needs is dropped while its window is open, as the details panel
            // above already carries it and repeating it in the footer says the same thing twice
            var summary = await InstallEntryIntoCollections(entryName, archivePath, matches, includeRemainingCount: _collectionsWindow is null);
            // Configuration held for its mods landed where it should, even though it has not been written yet
            var hasFailure = matches.Any(m => m.Entry.IsSatisfied() is false && m.Entry.Status is not CollectionModStatus.AwaitingTargets);

            _viewModel.EvaluateRequirements();
            _viewModel.UpdateEndorsements();
            _viewModel.UpdateFilter();

            // The row the user clicked to get here is still showing the old status, so the window behind is brought
            // back in step before the result is written into it
            _collectionsWindow?.RefreshCollections();

            await ReportCollectionEntryResult(String.Join(Environment.NewLine, summary), hasFailure);

            return CollectionEntryLinkResult.Handled;
        }

        /// <summary>
        /// Decides where a newly added mod is written. A collection profile being active means the user is looking
        /// at that collection, so a mod added there is treated as an add-on to it and installed into its folder.
        /// Anywhere else this answers with the ordinary install, leaving the rest of the add paths as they were.
        ///
        /// An add-on is the collection's from that point on, the same as anything the curator pinned: it takes the
        /// collection's SourceId from its folder, which is what keeps Stardrop, the profile and the SMAPI update
        /// suppression all describing it the same way.
        /// </summary>
        /// <param name="skipPrompt">Installs into the ordinary folder without asking, for a path that has already put the question to the user</param>
        private async Task<ModInstallTarget> ResolveModInstallTarget(bool skipPrompt = false)
        {
            var profile = GetCurrentProfile();
            if (profile is null || profile.IsFromCollection is false || String.IsNullOrEmpty(profile.SourceId))
            {
                return ModInstallTarget.Default();
            }

            if (skipPrompt)
            {
                return ModInstallTarget.Default();
            }

            // A profile outlives the collection that generated it, since removing one can keep its mods and leave
            // the profile behind. There is no folder to add to in that case, so the ordinary install is the answer
            var collection = CollectionCache.Load(profile.SourceId);
            if (collection is null)
            {
                Program.helper.Log($"The profile {profile.Name} names the collection {profile.SourceId}, which has no record, so the mod installs as an ordinary one");
                return ModInstallTarget.Default();
            }

            var installPath = Pathing.GetCollectionInstallPath(collection.SourceId);
            if (Program.settings.AlwaysAskForCollectionInstallTarget is false)
            {
                return new ModInstallTarget(true, installPath);
            }

            var requestWindow = new FlexibleOptionWindow(String.Format(Program.translation.Get("ui.message.confirm_collection_install_target"), collection.Name), Program.translation.Get("internal.add_to_collection"), Program.translation.Get("internal.install_normally"), Program.translation.Get("internal.cancel"), windowWidth: 450);
            KeepDialogAboveSiblings(requestWindow);

            var response = await requestWindow.ShowDialog<Choice>(this);
            if (response is Choice.Third)
            {
                return ModInstallTarget.Cancelled();
            }

            if (response is Choice.Second)
            {
                return ModInstallTarget.Default();
            }

            Program.helper.Log($"Installing into {collection.Name} as an add-on, at the user's request");

            return new ModInstallTarget(true, installPath);
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
                var (wasInstalled, appliedOverlays) = await InstallCollectionEntryArchive(match.Collection, match.Entry, archivePath);
                if (wasInstalled && match.Entry.IsOverlay() is false)
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

                // Configuration that arrived ahead of its mods says what it is waiting on, so the user knows the
                // setup is not finished and which downloads finish it
                if (match.Entry.Status is CollectionModStatus.AwaitingTargets)
                {
                    var blockingNames = match.Collection.DescribeBlockingTargets(match.Entry, CollectionInstall.MAX_LISTED_TARGET_NAMES, Program.translation.Get("ui.message.collection_names_truncated"));
                    summary.Add(String.Format(Program.translation.Get("ui.message.collection_entry_held"), entryName, match.Collection.Name, blockingNames));
                }
                else
                {
                    summary.Add(String.Format(Program.translation.Get("ui.message.collection_entry_installed"), entryName, match.Collection.Name));
                }

                foreach (var appliedOverlay in appliedOverlays)
                {
                    summary.Add(String.Format(Program.translation.Get("ui.message.collection_entry_released"), appliedOverlay.Name, match.Collection.Name));
                }

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

            // Each entry's rescan ran before the record above was saved, so until this the mods it placed are treated as add-ons rather than as the pins they are
            _viewModel.RefreshCollectionDetails();

            return summary;
        }

        /// <summary>
        /// Asks Nexus for the latest revision of every installed collection and records it against each one. Kept
        /// apart from the mod update check, which needs SMAPI's log, the game details and the version cache and
        /// gives up early without them, where this needs nothing beyond a signed in client.
        ///
        /// Runs once a session unless forced, as a curator publishing a revision mid-session is rare enough that
        /// the collections window's own refresh covers it.
        /// </summary>
        internal async Task CheckForCollectionUpdates(bool forceCheck = false)
        {
            if (Nexus.Client is null)
            {
                return;
            }

            if (_hasCheckedCollectionRevisions && forceCheck is false)
            {
                return;
            }

            var collections = CollectionCache.LoadAll();
            if (collections.Count == 0)
            {
                _hasCheckedCollectionRevisions = true;
                return;
            }

            Program.helper.Log($"Checking {collections.Count} installed collection(s) for a newer revision");

            foreach (var collection in collections)
            {
                // A null revision asks for whatever the curator has published most recently
                var revision = await Nexus.Client.GetCollectionRevision(collection.Slug, null, collection.DomainName);
                if (revision is null || revision.RevisionNumber is null)
                {
                    Program.helper.Log($"Unable to check the collection {collection.Name} for a newer revision", Helper.Status.Alert);
                    continue;
                }

                collection.LatestRevisionNumber = revision.RevisionNumber.Value;
                collection.LastRefreshTimestamp = DateTime.Now;

                if (collection.HasUpdate())
                {
                    Program.helper.Log($"The collection {collection.Name} has revision {collection.LatestRevisionNumber} available, against the installed revision {collection.RevisionNumber}");
                }

                CollectionCache.Save(collection);
            }

            _hasCheckedCollectionRevisions = true;

            RefreshCollectionUpdateCount();
        }

        /// <summary>
        /// Recounts the collections sitting behind a newer revision, read from the cache rather than the network so
        /// that an install or a removal can call it without spending a request.
        /// </summary>
        internal void RefreshCollectionUpdateCount()
        {
            _viewModel.CollectionsWithUpdates = CollectionCache.LoadAll().Count(c => c.HasUpdate());
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
        /// configuration and is kept, then written once every mod it is ordered after is installed.
        ///
        /// A mod arriving here has missed its place in the install order. The configuration ordered after it is
        /// written back over it. Any configuration that was only waiting on it is written for the first time.
        /// </summary>
        /// <returns>Whether the entry was accounted for, along with any other configuration its arrival let through</returns>
        private async Task<(bool WasInstalled, List<CollectionModEntry> AppliedOverlays)> InstallCollectionEntryArchive(CollectionInstall collection, CollectionModEntry entry, string archivePath)
        {
            var installPath = Pathing.GetCollectionInstallPath(collection.SourceId);
            Directory.CreateDirectory(installPath);

            entry.SourceArchivePath = archivePath;

            // Configuration is written back over this mod further down, though another mod the rules rank after
            // it is not, so this can still land on top of one where the two share a path
            var installOrder = collection.GetInstallOrder();
            var entryRank = installOrder.IndexOf(entry);
            if (entryRank >= 0 && installOrder.Skip(entryRank + 1).Any(m => m.Status is CollectionModStatus.Installed))
            {
                Program.helper.Log($"Writing {entry.Name} into {collection.Name} after mods the curator's rules place below it, as it arrived once the install had finished", Helper.Status.Warning);
            }

            if (IsArchiveFile(archivePath) is false || ArchiveHasManifest(archivePath) is false)
            {
                Program.helper.Log($"Handling {entry.Name} as configuration rather than as a mod, as it carries no manifest of its own");

                if (StoreOverlay(collection, entry, archivePath) is false)
                {
                    return (false, new List<CollectionModEntry>());
                }

                // Written straight away only where every mod it is ordered after is already installed. Otherwise it is
                // held until they are. Anything ordered after this configuration can come through with it
                var appliedOverlays = ApplyReadyOverlays(collection, installPath);
                if (appliedOverlays.Count > 0)
                {
                    // Configuration only lands at this point, so the mods it touched need a second pass to report
                    // that they now have a config
                    _viewModel.DiscoverMods(Pathing.defaultModPath);
                }

                CompleteUpdateIfReady(collection);

                // The entry itself is reported by the caller, so only what came through alongside it is handed back
                return (entry.IsOverlay(), appliedOverlays.Where(o => o != entry).ToList());
            }

            var installedModsByArchive = new Dictionary<string, List<Mod>>(StringComparer.OrdinalIgnoreCase);
            await AddMods(new string[] { archivePath }, installPath, installedModsByArchive, replaceWithoutAsking: true);

            _viewModel.DiscoverMods(Pathing.defaultModPath);

            var installedMods = _viewModel.Mods.Where(m => String.Equals(m.SourceId, collection.SourceId, StringComparison.OrdinalIgnoreCase)).ToList();
            var entriesByArchive = new Dictionary<string, CollectionModEntry>(StringComparer.OrdinalIgnoreCase) { [archivePath] = entry };
            RecordInstalledMods(entriesByArchive, installedModsByArchive, installedMods);

            var releasedOverlays = new List<CollectionModEntry>();
            if (entry.Status is CollectionModStatus.Installed)
            {
                // Written back before anything held is looked at, so configuration already on disk is put back on
                // top of this mod in the same order the main install would have written it
                var reappliedCount = ReapplyOverlaysOverEntry(collection, entry, GetEntryModFolders(installPath, installedMods, entry), installPath);
                releasedOverlays = ApplyReadyOverlays(collection, installPath);

                if (reappliedCount > 0 || releasedOverlays.Count > 0)
                {
                    _viewModel.DiscoverMods(Pathing.defaultModPath);
                }
            }

            CompleteUpdateIfReady(collection);

            return (entry.Status is CollectionModStatus.Installed, releasedOverlays);
        }

        /// <summary>
        /// Moves a stalled update onto its revision once nothing it needs is outstanding. The last outstanding entry
        /// of an update is what completes it. It usually arrives through the single entry path rather than
        /// through the install pass.
        /// </summary>
        private void CompleteUpdateIfReady(CollectionInstall collection)
        {
            if (collection.TryCompleteUpdate() is false)
            {
                return;
            }

            Program.helper.Log($"Revision {collection.RevisionNumber} of {collection.Name} is now fully installed");
            RefreshCollectionUpdateCount();
        }

        /// <summary>
        /// Adds a late arrival to the collection's generated profile. Matched on the source ID rather than the
        /// profile name, as a user is free to rename the profile and the source ID is what ties the two together.
        /// </summary>
        private void AddEntryToCollectionProfile(CollectionInstall collection, CollectionModEntry entry)
        {
            var profile = _editorView.Profiles.FirstOrDefault(p => IsCollectionProfile(p, collection));
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

            summary += BuildHeldOverlaysSection(collection);
            summary += BuildUnmatchedFilesSection(collection);

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
        /// Reports what applying a revision changed. Built around the difference rather than the totals, as a user
        /// who already had this collection is here to find out what moved rather than how many mods it holds.
        /// </summary>
        private async Task ReportCollectionUpdateResult(CollectionInstall collection, CollectionUpdatePlan plan)
        {
            var appliedRevision = collection.IsUpdateInProgress() ? collection.PendingRevisionNumber!.Value : collection.RevisionNumber;

            // Escaped, as the report is parsed for links further down and a collection name can hold the same characters
            var summary = String.Format(Program.translation.Get("ui.message.collection_update_summary"), HyperlinkParser.Escape(collection.Name), plan.Previous.RevisionNumber, appliedRevision);

            if (plan.HasChanges() is false)
            {
                summary += Environment.NewLine + Environment.NewLine + Program.translation.Get("ui.message.collection_update_no_changes");
            }
            else
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_update_counts"), plan.Added.Count, plan.Replaced.Count, plan.Removed.Count, plan.Unchanged.Count);
            }

            // Named rather than counted, as their files are still on disk and the user is the one who decides
            // whether to keep them
            if (plan.Removed.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_update_removed"), plan.Removed.Count);
                summary += BuildEntryLines(collection, plan.Removed, includeFailureReasons: false);
            }

            var manualDownloads = collection.GetManualDownloads();
            if (manualDownloads.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_manual_downloads"), manualDownloads.Count);
                summary += BuildEntryLines(collection, manualDownloads, includeFailureReasons: false);
            }

            var failures = collection.GetFailures();
            if (failures.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_failures"), failures.Count);
                summary += BuildEntryLines(collection, failures, includeFailureReasons: true);
            }

            summary += BuildHeldOverlaysSection(collection);
            summary += BuildUnmatchedFilesSection(collection);

            // Says outright that the revision has not landed, as the collections window will keep reporting an
            // update until whatever is outstanding is dealt with
            if (collection.IsUpdateInProgress())
            {
                summary += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_update_incomplete"), collection.RevisionNumber);
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
        /// Offers to clear the folders left behind by entries the revision no longer lists. The update itself never
        /// removes them, as a profile of the user's own may enable them and the curator dropping a mod is not the
        /// same as the user wanting it gone. Asked after the summary, so the answer is given having just read which
        /// mods were dropped.
        /// </summary>
        private async Task<bool> OfferToRemoveDroppedEntries(CollectionInstall collection, CollectionUpdatePlan plan, string installPath)
        {
            var droppedFolders = GetDroppedFolderNames(collection, plan);
            if (droppedFolders.Count == 0)
            {
                return false;
            }

            // Named from the entries that actually lose a folder rather than from everything the revision dropped,
            // so an entry whose mod another one still places is not offered up for deletion
            var removableEntries = plan.Removed.Where(e => e.InstalledMods.Any(i => droppedFolders.Contains(i.FolderName!, StringComparer.OrdinalIgnoreCase))).ToList();

            var message = String.Format(Program.translation.Get("ui.message.confirm_collection_remove_dropped"), removableEntries.Count);
            message += Environment.NewLine + BuildPlainEntryLines(removableEntries);

            // The collection dropping a mod says nothing about whether the user still wants it. A profile of their
            // own may be the reason to keep the files, and they cannot answer this without being told
            var dependentProfiles = GetProfilesUsingDroppedMods(collection, removableEntries);
            if (dependentProfiles.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_dropped_dependents"), dependentProfiles.Count);
            }

            var requestWindow = new MessageWindow(message);
            KeepDialogAboveSiblings(requestWindow);

            if (await requestWindow.ShowDialog<bool>(this) is false)
            {
                return false;
            }

            foreach (var folderName in droppedFolders)
            {
                Program.helper.Log($"Removing {folderName}, as revision {collection.RevisionNumber} of {collection.Name} no longer includes it");
                TryDeleteDirectory(Path.Combine(installPath, folderName));
            }

            return true;
        }

        /// <summary>
        /// The profiles still enabling one of the mods about to be removed. The collection's own profile is left out
        /// by source ID rather than by name, since the user is free to rename it, and its references to these mods
        /// were dropped by the amendment further up.
        /// </summary>
        private List<Profile> GetProfilesUsingDroppedMods(CollectionInstall collection, List<CollectionModEntry> entries)
        {
            var references = new List<ModReference>();
            foreach (var entry in entries)
            {
                foreach (var installedMod in entry.InstalledMods)
                {
                    references.Add(new ModReference(installedMod.UniqueId, collection.SourceId));
                }
            }

            if (references.Count == 0)
            {
                return new List<Profile>();
            }

            return _editorView.Profiles.Where(p => IsCollectionProfile(p, collection) is false && p.EnabledModIds.Any(m => references.Contains(m))).ToList();
        }

        /// <summary>Whether a profile is the one generated for this collection</summary>
        private static bool IsCollectionProfile(Profile profile, CollectionInstall collection)
        {
            return String.Equals(profile.SourceId, collection.SourceId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The folders belonging to entries this revision dropped. A folder the revision still writes into is never
        /// among them: a curator splitting one entry into two, or moving a mod between entries, leaves the survivor
        /// holding a folder the dropped entry used to place.
        /// </summary>
        private static List<string> GetDroppedFolderNames(CollectionInstall collection, CollectionUpdatePlan plan)
        {
            var claimedFolders = collection.Mods.SelectMany(m => m.InstalledMods).Select(i => i.FolderName).Where(f => String.IsNullOrEmpty(f) is false).ToList();

            var droppedFolders = new List<string>();
            foreach (var entry in plan.Removed)
            {
                foreach (var installedMod in entry.InstalledMods)
                {
                    if (String.IsNullOrEmpty(installedMod.FolderName) || claimedFolders.Contains(installedMod.FolderName!, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    droppedFolders.Add(installedMod.FolderName!);
                }
            }

            return droppedFolders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Entry names one per line, capped the same way the summary caps its lists. Kept apart from BuildEntryLines,
        /// which writes hyperlinks for a window that renders them, where this feeds a plain message box.
        /// </summary>
        private static string BuildPlainEntryLines(List<CollectionModEntry> entries)
        {
            var lines = String.Empty;
            foreach (var entry in entries.Take(_maxListedEntries))
            {
                lines += Environment.NewLine + $"  {entry.Name}";
            }

            if (entries.Count > _maxListedEntries)
            {
                lines += Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_entries_truncated"), entries.Count - _maxListedEntries);
            }

            return lines;
        }

        /// <summary>
        /// Names the configuration still waiting on its mods, which for a user without Premium is usually all of
        /// it straight after the install. Empty where nothing is held, so callers can append it unconditionally.
        /// </summary>
        private static string BuildHeldOverlaysSection(CollectionInstall collection)
        {
            var heldOverlays = collection.GetHeldOverlays();
            if (heldOverlays.Count == 0)
            {
                return String.Empty;
            }

            var section = Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_overlays_held"), heldOverlays.Count);
            foreach (var overlay in heldOverlays.Take(_maxListedEntries))
            {
                var blockingNames = collection.DescribeBlockingTargets(overlay, CollectionInstall.MAX_LISTED_TARGET_NAMES, Program.translation.Get("ui.message.collection_names_truncated"));
                section += Environment.NewLine + $"  {HyperlinkParser.Escape(String.Format(Program.translation.Get("ui.message.collection_overlay_held_entry"), overlay.Name, blockingNames))}";
            }

            if (heldOverlays.Count > _maxListedEntries)
            {
                section += Environment.NewLine + $"  {HyperlinkParser.Escape(String.Format(Program.translation.Get("ui.message.collection_entries_truncated"), heldOverlays.Count - _maxListedEntries))}";
            }

            return section;
        }

        /// <summary>
        /// Names the replicated entries that went on with some of their files missing from the download. The entry
        /// itself reads as applied, so without this the gap would only show up in game as an unchanged file.
        /// </summary>
        private static string BuildUnmatchedFilesSection(CollectionInstall collection)
        {
            var partialEntries = collection.Mods.Where(m => m.Status is CollectionModStatus.AppliedAsOverlay && m.UnmatchedFileCount > 0).ToList();
            if (partialEntries.Count == 0)
            {
                return String.Empty;
            }

            var section = Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_files_unmatched"), partialEntries.Count);
            foreach (var entry in partialEntries.Take(_maxListedEntries))
            {
                section += Environment.NewLine + $"  {HyperlinkParser.Escape(String.Format(Program.translation.Get("ui.message.collection_files_unmatched_entry"), entry.Name, entry.UnmatchedFileCount, entry.FileHashes.Count))}";
            }

            if (partialEntries.Count > _maxListedEntries)
            {
                section += Environment.NewLine + $"  {HyperlinkParser.Escape(String.Format(Program.translation.Get("ui.message.collection_entries_truncated"), partialEntries.Count - _maxListedEntries))}";
            }

            return section;
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
                // Marked so that a list of pages to work through says which of them can be passed on
                var optional = entry.IsOptional is false ? String.Empty : $" {HyperlinkParser.Escape(Program.translation.Get("ui.message.collection_entry_optional"))}";
                var reason = includeFailureReasons is false || String.IsNullOrEmpty(entry.FailureReason) ? String.Empty : $" ({HyperlinkParser.Escape(entry.FailureReason)})";
                lines += Environment.NewLine + $"  {HyperlinkParser.CreateLink(entry.Name, collection.GetEntryPageUri(entry, Program.settings.UseNXMLinks))}{optional}{reason}";
            }

            if (entries.Count > _maxListedEntries)
            {
                lines += Environment.NewLine + $"  {HyperlinkParser.Escape(String.Format(Program.translation.Get("ui.message.collection_entries_truncated"), entries.Count - _maxListedEntries))}";
            }

            return lines;
        }
    }
}
