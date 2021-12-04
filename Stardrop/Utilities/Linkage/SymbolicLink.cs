using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Utilities.Linkage
{
    static class SymbolicLink
    {
        [DllImport("kernel32.dll")]
        private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, Type dwFlags);

        internal enum Type
        {
            File = 0,
            Directory = 1
        }

        internal static void Create(string symbolicLinkName, string targetFileName, Type linkType)
        {
            CreateSymbolicLink(symbolicLinkName, targetFileName, linkType);
        }
    }
}
