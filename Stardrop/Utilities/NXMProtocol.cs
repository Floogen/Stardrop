using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Stardrop.Utilities
{
    internal static class NXMProtocol
    {
        [SupportedOSPlatform("windows")]
        public static bool Register(string applicationPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) is false)
            {
                Program.helper.Log($"Attempted to modify registery keys for NXM protocol on a non-Windows system!");
                return false;
            }
            try
            {
                using var software = Registry.CurrentUser.OpenSubKey("Software", writable: true);
                using var classes = software?.OpenSubKey("Classes", writable: true);
                if (classes is null)
                {
                    return false;
                }
                    
                using RegistryKey key = classes.CreateSubKey("nxm");
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
        [SupportedOSPlatform("windows")]
        public static bool Validate(string applicationPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) is false)
            {
                Program.helper.Log($"Attempted to modify registery keys for NXM protocol on a non-Windows system!");
                return false;
            }
            try
            {

                using var software = Registry.CurrentUser.OpenSubKey("Software", writable: true);
                using var classes = software?.OpenSubKey("Classes", writable: true);
                using var baseKey = classes?.OpenSubKey("nxm", writable: true);
                
                if(baseKey is null)
                    return false;
                
                if (!string.Equals(baseKey.GetValue("URL Protocol")?.ToString(), "nxm", StringComparison.Ordinal))
                    return false;

                using var commandKey = classes?.OpenSubKey(@"nxm\shell\open\command", writable: true);
                
                return string.Equals(
                    commandKey?.GetValue(string.Empty)?.ToString(),
                    $"\"{applicationPath}\" --nxm \"%1\"",
                    StringComparison.Ordinal);
                
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}
