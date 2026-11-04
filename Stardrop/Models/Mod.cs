using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Semver;
using Stardrop.Models.Data;
using Stardrop.Models.Data.Enums;
using Stardrop.Models.SMAPI;
using Stardrop.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using static Stardrop.Models.SMAPI.Web.ModEntryMetadata;

namespace Stardrop.Models
{
    public record PortableModData(string UniqueId, string Version, string Name, string Author, string ModPageUri);
    public
     class Mod : INotifyPropertyChanged
    {
        internal readonly FileInfo ModFileInfo = null!;
        internal readonly Manifest Manifest;


        public string UniqueId { get; set; }
        /// <summary>
        /// The collection this copy of the mod belongs to, or null for a loose install. Paired with UniqueId this
        /// forms the mod's real identity, as a collection can pin a version the user also has installed loosely.
        /// </summary>
        public string? SourceId { get; set; }
        public bool IsFromCollection { get { return string.IsNullOrEmpty(SourceId) is false; } }
        private string? _collectionName;
        /// <summary>
        /// The display name of the collection this copy belongs to, or null for a loose install. SourceId is a
        /// folder-safe slug rather than anything a user would recognise, so the readable name has to be resolved
        /// from the collection's cache record. Filled in by DiscoverMods, which reads that cache once per pass
        /// rather than once per mod.
        /// </summary>
        public string? CollectionName { get { return _collectionName; } set { _collectionName = value; NotifyPropertyChanged(); NotifyPropertyChanged(nameof(CollectionLabel)); } }
        private bool _isCollectionAddOn;
        /// <summary>
        /// Whether this copy sits in a collection's folder without the collection pinning it, which is a mod the
        /// user added alongside the collection rather (add-on) than one its curator chose. Nothing behaves differently for
        /// one, as an add-on belongs to the collection exactly as the rest do. It is marked because a revision will
        /// never replace it.
        /// </summary>
        public bool IsCollectionAddOn { get { return _isCollectionAddOn; } set { _isCollectionAddOn = value; NotifyPropertyChanged(); NotifyPropertyChanged(nameof(IsCuratedByCollection)); NotifyPropertyChanged(nameof(CollectionLabel)); } }
        /// <summary>Whether the collection's curator pinned this copy, which is true of every collection mod but an add-on</summary>
        public bool IsCuratedByCollection { get { return IsFromCollection && IsCollectionAddOn is false; } }
        /// <summary>What the collection icon says on hover: the collection's name, and for an add-on what being one means</summary>
        public string? CollectionLabel { get { return IsCollectionAddOn ? string.Format(Program.translation.Get("ui.main_window.tooltips.collection_add_on"), CollectionName) : CollectionName; } }
        public SemVersion Version { get; set; }
        public string ParsedVersion { get { return Version.ToString(); } }
        private string _suggestedVersion = null!;
        public string SuggestedVersion { get { return _suggestedVersion; } set { _suggestedVersion = value; NotifyPropertyChanged(nameof(SuggestedVersion)); NotifyPropertyChanged(nameof(ParsedStatus)); NotifyPropertyChanged(nameof(InstallStatus)); NotifyPropertyChanged(nameof(HasNewerVersion)); NotifyPropertyChanged(nameof(IsUpdateIgnored)); NotifyPropertyChanged(nameof(HasAvailableUpdate)); NotifyPropertyChanged(nameof(HasActionableStatus)); NotifyPropertyChanged(nameof(HasChangelog)); NotifyPropertyChanged(nameof(IgnoreUpdateText)); } }
        public string Name { get; set; }
        public string Path { get { return _path; } set { _path = value; RootPath = GetRootPath(value); } } // Whole mod path inside installed mods path for grouping mod components in the same mod
        private string _path = null!;
        public string RootPath { get; private set; } = null!; // Root mod path inside installed mods path for grouping mod components in the same mod
        public string ManifestFilePath { get { return ModFileInfo.FullName; } }
        public string Description { get; set; }
        public string Summary { get { return $"Author: {Author}\nVersion: {ParsedVersion}\nHas Config: {HasConfig}\n\n{Description}"; } }
        public string Author { get; set; }
        public DateTime? InstallTimestamp { get; set; }
        /// <summary>
        /// Left null for a mod installed by a collection. Nothing writes one, rather than the column hiding it, as
        /// the date would only ever record when Stardrop replaced the folder during a collection update. Whether
        /// such a mod is current is answered by its collection's revision.
        /// </summary>
        public DateTime? LastUpdateTimestamp { get; set; }
        public Config? _config { get; set; }
        public Config? Config { get { return _config; } set { _config = value; NotifyPropertyChanged(nameof(Config)); NotifyPropertyChanged(nameof(HasConfig)); } }
        public bool HasConfig { get { return Config is not null; } }
        public string FrameworkID { get; set; } = string.Empty;
        private List<ManifestDependency> _requirements = null!;
        public List<ManifestDependency> Requirements { get { return _requirements; } set { _requirements = value; NotifyPropertyChanged(nameof(Requirements)); NotifyPropertyChanged(nameof(MissingRequirements)); NotifyPropertyChanged(nameof(HardRequirements)); } }
        public List<ManifestDependency>? MissingRequirements { get { return _requirements is null ? null : [.. _requirements.Where(r => !string.IsNullOrEmpty(r.Name) && r.IsMissing && r.IsRequired)]; } }
        public List<ManifestDependency>? HardRequirements { get { return _requirements is null ? null : [.. _requirements.Where(r => !string.IsNullOrEmpty(r.Name) && !r.IsMissing && r.IsRequired)]; } }
        private string _updateUri = null!;
        public string UpdateUri { get { return _updateUri; } set { _updateUri = value; NotifyPropertyChanged(nameof(UpdateUri)); } }
        private string _modPageUri = null!;
        public string ModPageUri { get { return _modPageUri; } set { _modPageUri = value; NotifyPropertyChanged(nameof(ModPageUri)); } }
        public int? NexusModId { get { return GetNexusId(); } }
        private string? _nexusModThumbnailPath { get; set; }
        public string? NexusModThumbnailPath { get { return _nexusModThumbnailPath; } set { _nexusModThumbnailPath = value; NexusModThumbnailFile = TryLoadThumbnail(value); NotifyPropertyChanged(nameof(NexusModThumbnailFile)); } }
        public Bitmap? NexusModThumbnailFile { get; set; }
        private bool _isEnabled;
        public bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                _isEnabled = value;
                NotifyPropertyChanged(nameof(IsEnabled));
                NotifyPropertyChanged(nameof(ChangeStateText));
                NotifyPropertyChanged(nameof(ChangeWholeModGroupStateText));
            }
        }
        private bool _isHidden;
        public bool IsHidden { get { return _isHidden; } set { _isHidden = value; NotifyPropertyChanged(nameof(IsHidden)); } }
        private bool _isEndorsement;
        public bool IsEndorsed { get { return _isEndorsement; } set { _isEndorsement = value; NotifyPropertyChanged(nameof(IsEndorsed)); } }
        public string ChangeStateText { get { return IsEnabled ? Program.translation.Get("internal.disable") : Program.translation.Get("internal.enable"); } }
        public string ChangeWholeModGroupStateText { get { return IsEnabled ? Program.translation.Get("internal.disable_whole_mod") : Program.translation.Get("internal.enable_whole_mod"); } }
        /// <summary>
        /// Whether the suggested version is newer than the installed one.
        /// This is the base the other update properties are built from and what 
        /// the ignore toggle's visibility hangs off rather than anything that folds the ignore in, which would hide the
        /// only control capable of undoing it.
        /// </summary>
        public bool HasNewerVersion
        {
            get
            {
                // TryParse first, as IsModOutdated parses outright and this getter is reached from a binding
                if (string.IsNullOrEmpty(SuggestedVersion) || SemVersion.TryParse(SuggestedVersion, SemVersionStyles.Any, out _) is false)
                {
                    return false;
                }

                return IsModOutdated(SuggestedVersion);
            }
        }
        /// <summary>
        /// Whether the currently suggested version is the one the user chose to ignore. A newer suggestion than the
        /// ignored one leaves this false, which is what lets an ignore lapse on its own once the mod moves on.
        /// </summary>
        public bool IsUpdateIgnored { get { return !string.IsNullOrEmpty(SuggestedVersion) && !string.IsNullOrEmpty(IgnoredVersion) && IgnoredVersion.Equals(SuggestedVersion, StringComparison.OrdinalIgnoreCase); } }
        public string IgnoreUpdateText { get { return IsUpdateIgnored ? Program.translation.Get("internal.stop_ignoring_update") : Program.translation.Get("internal.ignore_update"); } }
        /// <summary>
        /// Whether the mod counts towards the updatable total. This is the single rule that both a fresh smapi.io
        /// response and the version cache are read through, so the reported number cannot differ between a manual
        /// check and a restart without the underlying data differing with it.
        /// </summary>
        public bool HasAvailableUpdate { get { return HasNewerVersion && IsUpdateIgnored is false; } }
        /// <summary>
        /// Whether the mod has something the user can still act on, which is what the updatable filter shows. An
        /// ignored update does not qualify, though a compatibility warning does even when a version update sitting
        /// alongside it has been ignored.
        /// </summary>
        public bool HasActionableStatus
        {
            get
            {
                if (_status == WikiCompatibilityStatus.Broken)
                {
                    return true;
                }

                return HasAvailableUpdate;
            }
        }
        private WikiCompatibilityStatus _status { get; set; }
        public WikiCompatibilityStatus Status { get { return _status; } set { _status = value; NotifyPropertyChanged(nameof(Status)); NotifyPropertyChanged(nameof(ParsedStatus)); NotifyPropertyChanged(nameof(InstallStatus)); NotifyPropertyChanged(nameof(HasActionableStatus)); NotifyPropertyChanged(nameof(HasChangelog)); } }
        public string ParsedStatus
        {
            get
            {
                // Ahead of the ignore, as a compatibility warning is not version scoped and ignoring a version
                // update is not a reason to stop reporting that the mod is broken
                if (_status == WikiCompatibilityStatus.Broken)
                {
                    return Program.translation.Get("ui.main_window.hyperlinks.broken_compatibility_issue");
                }

                // Reported rather than blanked, as an ignored mod is otherwise indistinguishable from an up to date
                // one and nothing would hint that the context menu can undo it
                if (IsUpdateIgnored)
                {
                    return string.Format(Program.translation.Get("ui.main_window.hyperlinks.update_ignored"), SuggestedVersion);
                }

                if (!string.IsNullOrEmpty(SuggestedVersion) && IsModOutdated(SuggestedVersion))
                {
                    if (_status == WikiCompatibilityStatus.Unofficial)
                    {
                        return string.Format(Program.translation.Get("ui.main_window.hyperlinks.unofficial_update_available"), SuggestedVersion);
                    }
                    return string.Format(Program.translation.Get("ui.main_window.hyperlinks.update_available"), SuggestedVersion);
                }

                return string.Empty;
            }
        }
        private InstallState _installState;
        public InstallState InstallState { get { return _installState; } set { _installState = value; NotifyPropertyChanged(nameof(InstallState)); } }
        public string InstallStatus
        {
            get
            {
                // If the suggested version is the same as an ignored version, treat as no update
                if (IsUpdateIgnored)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrEmpty(SuggestedVersion) && IsModOutdated(SuggestedVersion))
                {
                    var nexusModId = GetNexusId();
                    if (_status == WikiCompatibilityStatus.Unofficial || nexusModId is null)
                    {
                        return string.Empty;
                    }
                    else if (InstallState == InstallState.Unknown)
                    {
                        return Program.translation.Get("ui.main_window.hyperlinks.install_update");
                    }

                    return InstallState == InstallState.Downloading ? Program.translation.Get("ui.main_window.hyperlinks.downloading") : Program.translation.Get("ui.main_window.hyperlinks.installing");
                }

                return string.Empty;
            }
        }

        private ChangelogState _changelogState;
        public ChangelogState ChangelogState { get { return _changelogState; } set { _changelogState = value; NotifyPropertyChanged(nameof(ChangelogState)); } }

        public bool HasChangelog
        {
            get
            {
                return !string.IsNullOrEmpty(SuggestedVersion) && IsModOutdated(SuggestedVersion) && GetNexusId() is not null;
            }
        }

        private string _note = null!;
        public string Note { get { return _note; } set { _note = value; NotifyPropertyChanged(nameof(Note)); } }
        private string? _ignoredVersion;
        public string? IgnoredVersion { get { return _ignoredVersion; } set { _ignoredVersion = value; NotifyPropertyChanged(nameof(IgnoredVersion)); NotifyPropertyChanged(nameof(IsUpdateIgnored)); NotifyPropertyChanged(nameof(ParsedStatus)); NotifyPropertyChanged(nameof(InstallStatus)); NotifyPropertyChanged(nameof(HasAvailableUpdate)); NotifyPropertyChanged(nameof(HasActionableStatus)); NotifyPropertyChanged(nameof(IgnoreUpdateText)); } }

        public event PropertyChangedEventHandler? PropertyChanged;

        public Mod(Manifest manifest, FileInfo modFileInfo, string uniqueId, string version, string? name = null, string? description = null, string? author = null)
        {
            Manifest = manifest;
            ModFileInfo = modFileInfo;
            UniqueId = uniqueId;
            SourceId = Pathing.GetCollectionSourceId(modFileInfo.DirectoryName);
            Version = SemVersion.TryParse(version, SemVersionStyles.Any, out var parsedVersion) ? parsedVersion : SemVersion.ParsedFrom(0, 0, 0, "bad-version");
            Name = string.IsNullOrEmpty(name) ? uniqueId : name;
            Path = ComputeModPath(modFileInfo);
            Description = string.IsNullOrEmpty(description) ? string.Empty : description;
            Author = string.IsNullOrEmpty(author) ? Program.translation.Get("internal.unknown") : author;
            Requirements = [];
        }

        /// <summary>
        /// For designer view use only. Use the primary Mod constructor instead
        /// </summary>
        /// <param name="manifest"></param>
        public Mod(Manifest manifest)
        {
            if (Design.IsDesignMode is false)
            {
                throw new Exception("Using design-mode only Mod constructor. Use the primary Mod constructor instead.");
            }

            Manifest = manifest;
            UniqueId = manifest.UniqueID;
            Version = SemVersion.TryParse(manifest.Version, SemVersionStyles.Any, out var parsedVersion) ? parsedVersion : SemVersion.ParsedFrom(0, 0, 0, "bad-version");
            Name = string.IsNullOrEmpty(manifest.Name) ? manifest.UniqueID : manifest.Name;
            Description = string.IsNullOrEmpty(manifest.Description) ? string.Empty : manifest.Description;
            Author = string.IsNullOrEmpty(manifest.Author) ? Program.translation.Get("internal.unknown") : manifest.Author;
            Requirements = [];
        }


        /// <summary>
        /// Compute relative path to a mod from the root it was discovered under, which is what mods are grouped by.
        /// The mod folder is tested first so that grouping there is unchanged, then the collections folder, which
        /// sits outside the mod folder and would otherwise match neither.
        /// </summary>
        private static string ComputeModPath(FileInfo modFileInfo)
        {
            // Set whole mod path for grouping with other mods from the same mod.
            var modNamePath = GetPathUnderRoot(modFileInfo.DirectoryName, Program.settings.ModFolderPath) ?? GetPathUnderRoot(modFileInfo.DirectoryName, Pathing.GetCollectionsFolderPath());

            // Grouped as unknown rather than thrown over. This runs from the constructor, so throwing takes down
            // whatever was building the mod, which for an install is the whole archive
            if (string.IsNullOrEmpty(modNamePath))
            {
                Program.helper.Log($"The mod at {modFileInfo.DirectoryName} sits under neither the mod folder nor the collections folder, so it has no group", Helper.Status.Warning);
                return Program.translation.Get("internal.unknown");
            }

            // TODO: Add program config option to switch between both approaches? And to disable grouping entirely?
            // For top-level folder grouping.
            // Producing group "automation" as a single group for both "automation/Automate" and
            //  "automation/Producer Framework Mod".
            // var foundIndex = modNamePath.IndexOf(System.IO.Path.DirectorySeparatorChar);
            // For subfolders-specific grouping.
            // Producing groups "automation/Automate" (with mods `[CP] Automate/manifest.json`, `[JA] Automate/manifest.json`)
            //  and "automation/Producer Framework Mod" (with mods `[CP] PFM` and `[JA] PFM`) folders as separate groups.
            var foundIndex = modNamePath.LastIndexOf(System.IO.Path.DirectorySeparatorChar);

            var nameLength = foundIndex == -1 ? modNamePath.Length : foundIndex;
            var finalPath = modNamePath[..nameLength];
            return string.IsNullOrEmpty(finalPath) ? Program.translation.Get("internal.unknown") : finalPath;
        }

        /// <summary>
        /// The part of a mod's folder below the given root, or null when the mod does not sit under it. Matched as a
        /// prefix ending in a separator, rather than by the old Contains test, which also accepted a root appearing
        /// anywhere in the path and one that merely shared a name prefix with the folder the mod is really in.
        /// </summary>
        private static string? GetPathUnderRoot(string? modDirectoryName, string? root)
        {
            if (string.IsNullOrEmpty(modDirectoryName) || string.IsNullOrEmpty(root))
            {
                return null;
            }

            var prefix = System.IO.Path.EndsInDirectorySeparator(root) ? root : root + System.IO.Path.DirectorySeparatorChar;
            if (modDirectoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) is false)
            {
                return null;
            }

            return modDirectoryName.Substring(prefix.Length);
        }

        private static string GetRootPath(string path)
        {
            var foundIndex = path.IndexOf(System.IO.Path.DirectorySeparatorChar);
            if (foundIndex == -1)
            {
                return path;
            }

            return path[..foundIndex];
        }

        public bool IsModOutdated(string version)
        {
            if (string.IsNullOrEmpty(version) || !HasValidVersion())
            {
                return false;
            }

            return SemVersion.Parse(version, SemVersionStyles.Any).CompareSortOrderTo(Version) > 0;
        }

        public bool HasValidVersion()
        {
            if (Version.Prerelease.Equals("bad-version", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        public bool HasUpdateKeys()
        {
            if (Manifest is not null && Manifest.UpdateKeys is not null && !Manifest.UpdateKeys.Any(k => string.IsNullOrEmpty(k)))
            {
                return true;
            }

            return false;
        }

        public int? GetNexusId()
        {
            if (HasUpdateKeys() is false)
            {
                return null;
            }

            foreach (string key in Manifest.UpdateKeys)
            {
                if (TryParseNexusKey(key, out int modId, out _))
                {
                    return modId;
                }
            }

            return null;
        }

        public string? GetNexusFlag()
        {
            if (HasUpdateKeys() is false)
            {
                return null;
            }

            foreach (string key in Manifest.UpdateKeys)
            {
                if (TryParseNexusKey(key, out _, out string? flag))
                {
                    return flag;
                }
            }

            return null;
        }

        /// <summary>
        /// Parses a Nexus update key like SMAPI's UpdateKey.Parse, case-insensitive. The flag keeps its leading '@'.
        /// </summary>
        private static bool TryParseNexusKey(string key, out int modId, out string? flag)
        {
            modId = 0;
            flag = null;

            int siteIndex = key.IndexOf(':');
            if (siteIndex < 0 || key[..siteIndex].Trim().Equals("Nexus", StringComparison.OrdinalIgnoreCase) is false)
            {
                return false;
            }

            string id = key[(siteIndex + 1)..].Trim();
            int flagIndex = id.IndexOf('@');
            if (flagIndex >= 0)
            {
                flag = id[flagIndex..].Trim();
                id = id[..flagIndex].Trim();
            }

            return int.TryParse(id, out modId) && modId > 0;
        }

        private Bitmap? TryLoadThumbnail(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return null;
            }

            try
            {
                var thumbnail = new Bitmap(filePath);
                return thumbnail;
            }
            catch (Exception)
            {
                Program.helper.Log($"Failed to load thumbnail for mod {UniqueId} using following path {filePath}");
                return null;
            }
        }

        internal PortableModData GetPortableData()
        {
            return new PortableModData(UniqueId, ParsedVersion, Name, Author, ModPageUri);
        }

        /// <summary>
        /// Builds the identity used by profiles to reference this specific copy of the mod.
        /// </summary>
        public ModReference ToReference()
        {
            return new ModReference(UniqueId, SourceId);
        }

        internal void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}
