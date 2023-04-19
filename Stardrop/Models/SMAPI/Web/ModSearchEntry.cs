using Semver;

namespace Stardrop.Models.SMAPI.Web
{
    public class ModSearchEntry
    {
        // Based on SMAPI's ModSearchEntryModel.cs: https://github.com/Pathoschild/SMAPI/blob/develop/src/SMAPI.Toolkit/Framework/Clients/WebApi/ModSearchEntryModel.cs

        /*********
        ** Accessors
        *********/
        /// <summary>The unique mod ID.</summary>
        public string Id { get; set; }

        /// <summary>The namespaced mod update keys (if available).</summary>
        public string[] UpdateKeys { get; set; }

        /// <summary>The mod version installed by the local player. This is used for version mapping in some cases.</summary>
        public string InstalledVersion { get; set; }


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an empty instance.</summary>
        public ModSearchEntry()
        {
            // needed for JSON deserializing
        }

        /// <summary>Construct an instance.</summary>
        /// <param name="id">The unique mod ID.</param>
        /// <param name="installedVersion">The version installed by the local player. This is used for version mapping in some cases.</param>
        /// <param name="updateKeys">The namespaced mod update keys (if available).</param>
        /// <param name="isBroken">Whether the installed version is broken or could not be loaded.</param>
        public ModSearchEntry(string id, SemVersion installedVersion, string[] updateKeys, bool isBroken = false)
        {
            this.Id = id;
            this.InstalledVersion = installedVersion.ToString();
            this.UpdateKeys = updateKeys ?? new string[0];
        }
    }
}
