using Stardrop.Models;
using Stardrop.Utilities;
using ReactiveUI.Reactive;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Stardrop.ViewModels
{
    public class SettingsWindowViewModel : ViewModelBase
    {
        // Setting bindings
        public string SMAPIPath { get { return Program.settings.SMAPIFolderPath; } set { Program.settings.SMAPIFolderPath = value; Pathing.SetSmapiPath(Program.settings.SMAPIFolderPath, string.IsNullOrEmpty(Program.settings.ModFolderPath)); } }
        public string ModFolderPath { get { return Program.settings.ModFolderPath; } set { Program.settings.ModFolderPath = value; Pathing.SetModPath(Program.settings.ModFolderPath); } }
        public string ModInstallPath { get { return Program.settings.ModInstallPath; } set { Program.settings.ModInstallPath = value; } }
        public string CollectionInstallPath { get { return Program.settings.CollectionInstallPath; } set { Program.settings.CollectionInstallPath = value; } }
        public bool IgnoreHiddenFolders { get { return Program.settings.IgnoreHiddenFolders; } set { Program.settings.IgnoreHiddenFolders = value; } }
        public bool IsAskingBeforeAcceptingNXM { get { return Program.settings.IsAskingBeforeAcceptingNXM; } set { Program.settings.IsAskingBeforeAcceptingNXM = value; } }
        public bool EnableProfileSpecificModConfigs { get { return Program.settings.EnableProfileSpecificModConfigs; } set { Program.settings.EnableProfileSpecificModConfigs = value; } }
        public bool EnableModsOnAdd { get { return Program.settings.EnableModsOnAdd; } set { Program.settings.EnableModsOnAdd = value; } }
        public bool AlwaysAskToDelete { get { return Program.settings.AlwaysAskToDelete; } set { Program.settings.AlwaysAskToDelete = value; } }
        public bool AlwaysAskForCollectionInstallTarget { get { return Program.settings.AlwaysAskForCollectionInstallTarget; } set { Program.settings.AlwaysAskForCollectionInstallTarget = value; } }
        public bool CollectionsSkipSMAPIUpdateCheck { get { return Program.settings.CollectionsSkipSMAPIUpdateCheck; } set { Program.settings.CollectionsSkipSMAPIUpdateCheck = value; } }
        public bool ShouldAutomaticallySaveProfileChanges { get { return Program.settings.ShouldAutomaticallySaveProfileChanges; } set { Program.settings.ShouldAutomaticallySaveProfileChanges = value; } }
        public bool ShowModThumbnails { get { return Program.settings.ShowModThumbnails; } set { Program.settings.ShowModThumbnails = value; } }
        private List<Theme> _themes = [];
        public List<Theme> Themes
        {
            get => _themes;
            set => this.RaiseAndSetIfChanged(ref _themes, value);
        }

        // Other UI controls
        public bool ShowMainMenu { get; }
        public bool ShowNXMAssociationButton { get; }
        public bool ShowNexusServers { get; }

        public SettingsWindowViewModel()
        {
            ShowMainMenu = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            ShowNXMAssociationButton = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            ShowNexusServers = Program.settings.NexusDetails is not null && Program.settings.NexusDetails.IsPremium;
#if DEBUG
            ShowNexusServers = true;
#endif
        }
    }
}
