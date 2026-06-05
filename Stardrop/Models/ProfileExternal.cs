using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models
{
    public record PortableModData(string UniqueId, string Version, string Name, string Author, string ModPageUri);

    public class ProfileExternal : Profile
    {
        public List<PortableModData> ModData { get; set; }

        public ProfileExternal() : base()
        {
            IsProtected = false;

            ModData = new List<PortableModData>();
        }

        public ProfileExternal(List<Mod> mods) : this()
        {
            foreach (var mod in mods)
            {
                ModData.Add(new PortableModData(mod.UniqueId, mod.ParsedVersion, mod.Name, mod.Author, mod.ModPageUri));
            }
        }
    }
}
