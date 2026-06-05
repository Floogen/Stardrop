using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Utilities
{
    internal static class Toolkit
    {
        public static void OpenBrowser(string url)
        {
            if (String.IsNullOrEmpty(url))
            {
                return;
            }

            try
            {
                using Process process = Process.Start(new ProcessStartInfo
                {
                    FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? url :
                        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open",
                    Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "" : $"\"{url}\"",
                    CreateNoWindow = true,
                    UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                });
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to utilize OpenBrowser with the url ({url}): {ex}");
            }
        }
    }
}
