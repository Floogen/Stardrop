using System.Collections.Generic;

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

        public void AddMods(List<Mod> mods)
        {
            foreach (var mod in mods)
            {
                ModData.Add(mod.GetPortableData());
            }
        }
    }
}
