using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

namespace Stardrop.Utilities
{
    internal static class NXMProtocol
    {
        public static bool Register(string applicationPath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) is false)
                {
                    Program.helper.Log($"Attempted to modify registery keys for NXM protocol on a non-Windows system!");
                    return false;
                }

                var keyTest = Registry.CurrentUser.OpenSubKey("Software", true).OpenSubKey("Classes", true);
                RegistryKey key = keyTest.CreateSubKey("nxm");
                key.SetValue("URL Protocol", "nxm");
                key.CreateSubKey(@"shell\open\command").SetValue("", "\"" + applicationPath + "\" --nxm \"%1\"");
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to associate Stardrop with the NXM protocol: {ex}");
                return false;
            }

            return true;
        }

        public static bool Validate(string applicationPath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) is false)
                {
                    Program.helper.Log($"Attempted to modify registery keys for NXM protocol on a non-Windows system!");
                    return false;
                }

                var baseKeyTest = Registry.CurrentUser.OpenSubKey("Software", true).OpenSubKey("Classes", true).OpenSubKey("nxm", true);
                if (baseKeyTest is null || baseKeyTest.GetValue("URL Protocol").ToString() != "nxm")
                {
                    return false;
                }

                var actualKeyTest = Registry.CurrentUser.OpenSubKey("Software", true).OpenSubKey("Classes", true).OpenSubKey(@"nxm\shell\open\command", true);
                if (actualKeyTest.GetValue(String.Empty).ToString() != "\"" + applicationPath + "\" --nxm \"%1\"")
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                return false;
            }

            return true;
        }
    }
}
