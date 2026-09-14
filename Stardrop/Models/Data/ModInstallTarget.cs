namespace Stardrop.Models.Data
{
    /// <summary>
    /// Where a newly added mod should be written. A null <see cref="InstallPathOverride"/> means the ordinary mod
    /// install folder, which is the fallback the install pass uses when it is given nothing.
    /// </summary>
    /// <param name="Proceed">False where the user backed out of the install entirely</param>
    /// <param name="InstallPathOverride">The folder to install into, or null for the ordinary one</param>
    public record ModInstallTarget(bool Proceed, string? InstallPathOverride = null)
    {
        /// <summary>The ordinary install, used wherever nothing has a reason to redirect it</summary>
        public static ModInstallTarget Default()
        {
            return new ModInstallTarget(true);
        }

        public static ModInstallTarget Cancelled()
        {
            return new ModInstallTarget(false);
        }
    }
}
