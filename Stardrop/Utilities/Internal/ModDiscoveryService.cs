using System;
using System.IO;

namespace Stardrop.Utilities.Internal;

public class ModDiscoveryService : IModDiscoveryService
{
    public bool ParentFolderContainsPeriod(string oldestAncestorPath, DirectoryInfo? directoryInfo)
    {
        if (directoryInfo is null)
        {
            return false;
        }
        if (directoryInfo.Name[0] == '.')
        {
            return true;
        }

        var ancestorFolder = directoryInfo.Parent;
        while (ancestorFolder is not null &&
               !ancestorFolder.FullName.Equals(oldestAncestorPath, StringComparison.OrdinalIgnoreCase))
        {
            if (ancestorFolder.Name[0] == '.')
            {
                return true;
            }

            ancestorFolder = ancestorFolder.Parent;
        }

        return false;
    }
}