namespace Stardrop.Models.Data.Enums
{
    public enum ModSourceFilter
    {
        /// <summary>
        /// Show only what belongs to the active profile. A collection profile shows the mods it references, wherever
        /// they live, while a plain profile shows everything not owned by a collection.
        /// </summary>
        ActiveProfile,
        /// <summary>Show every installed mod, including copies owned by collections the user is not currently using</summary>
        All
    }
}
