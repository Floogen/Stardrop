namespace Stardrop.Models.Data.Enums
{
    /// <summary>
    /// What an nxm mod link turned out to be, once the collections waiting on files have been checked. Declining
    /// the capture is different from never matching one, as the user has then already been asked where the file
    /// should go.
    /// </summary>
    public enum CollectionEntryLinkResult
    {
        NotMatched,
        Declined,
        Handled
    }
}
