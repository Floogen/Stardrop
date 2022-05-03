using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Utilities
{
    internal static class NXMProtocol
    {
        public static void Register(string applicationPath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) is false)
                {
                    Program.helper.Log($"Attempted to modify registery keys for NXM protocol on a non-Windows system!");
                    return;
                }

                var KeyTest = Registry.CurrentUser.OpenSubKey("Software", true).OpenSubKey("Classes", true);
                RegistryKey key = KeyTest.CreateSubKey("nxm");
                key.SetValue("URL Protocol", "nxm");
                key.CreateSubKey(@"shell\open\command").SetValue("", "\"" + applicationPath + "\" --nxm \"%1\"");
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to associate Stardrop with the NXM protocol: {ex}");
            }
        }
    }
}
