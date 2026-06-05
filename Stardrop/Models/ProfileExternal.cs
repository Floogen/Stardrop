using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stardrop.Models;

namespace Stardrop.Models
{
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
                ModData.Add(mod.GetPortableData());
            }
        }
    }
}
