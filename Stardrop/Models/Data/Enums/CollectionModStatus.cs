namespace Stardrop.Models.Data.Enums
{
    public enum CollectionModStatus
    {
        /// <summary>Queued but not yet acted on</summary>
        Pending,
        /// <summary>The user has to fetch this one through the browser, either because they lack Premium or because the source is not Nexus</summary>
        AwaitingManualDownload,
        Downloading,
        Installed,
        Failed,
        /// <summary>An optional entry the user chose not to install</summary>
        Skipped
    }
}
