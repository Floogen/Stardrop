using System.ComponentModel;

namespace Stardrop.Models.Data.Enums
{
    public enum ModGrouping
    {
        None,
        Folder,
        [Description("Content Pack")]
        ContentPack,
        [Description("Folder (Condensed)")]
        FolderCondensed
    }
}
