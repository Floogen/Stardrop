using System.IO;

namespace Stardrop.Utilities.Internal;

public interface IModDiscoveryService
{

    bool ParentFolderContainsPeriod(string oldestAncestorPath, DirectoryInfo? directoryInfo);
}