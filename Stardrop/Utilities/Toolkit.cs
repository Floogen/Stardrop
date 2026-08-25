using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Stardrop.Utilities
{
    internal static class Toolkit
    {
        /// <summary>
        /// Formats a byte count for display, picking the largest unit that keeps the number readable.
        /// </summary>
        public static string ToHumanReadableSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):N2} {Program.translation.Get("internal.measurements.gigabytes_size")}";
            }

            if (bytes >= 1024L * 1024L)
            {
                return $"{(bytes / (1024.0 * 1024.0)):N2} {Program.translation.Get("internal.measurements.megabytes_size")}";
            }

            if (bytes >= 1024L)
            {
                return $"{(bytes / 1024.0):N2} {Program.translation.Get("internal.measurements.kilobytes_size")}";
            }

            return $"{bytes:N0} {Program.translation.Get("internal.measurements.bytes_size")}";
        }

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

        public static bool IsFromSite(string url, string expectedHostname)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) is false)
            {
                return false;
            }

            // Only allow web calls
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            // Host comparison is case-insensitive
            return string.Equals(CleanHostname(uri.Host), CleanHostname(expectedHostname), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFromNexusMods(string url)
        {
            return IsFromSite(url, "nexusmods.com");
        }

        public static bool IsFromGitHub(string url)
        {
            return IsFromSite(url, "github.com");
        }

        private static string CleanHostname(string host)
        {
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host.Substring("www.".Length) : host;
        }
    }
}
