namespace Stardrop.Models.Data
{
    /// <summary>
    /// How a single nxm link ended. A failure belongs to the one link that produced it and the links queued behind
    /// it can still be worked through, while a block is a condition every one of them would run into as well.
    /// </summary>
    public enum NXMLinkResult
    {
        Success,
        Failed,
        Canceled,
        Blocked
    }
}
