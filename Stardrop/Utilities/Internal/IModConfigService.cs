using System.Collections.Generic;
using Stardrop.Models;

namespace Stardrop.Utilities.Internal;

public interface IModConfigService
{
    void DiscoverConfigs(string modsFilePath, IReadOnlyList<Mod> mods, bool useArchive = false);
    
    

}