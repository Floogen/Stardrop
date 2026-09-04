using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Stardrop.Utilities
{
    internal enum NXMAssociationStatus
    {
        /// <summary>
        /// Stardrop has no usable NXM registration
        /// </summary>
        Unregistered,
        /// <summary>
        /// Stardrop handles NXM links, but part of the registration is missing or stale
        /// </summary>
        Incomplete,
        /// <summary>
        /// Windows will hand NXM links to Stardrop
        /// </summary>
        Registered,
        /// <summary>
        /// Another application holds the Windows UserChoice for the NXM protocol, so Stardrop's is ignored
        /// </summary>
        Overridden
    }

    internal sealed class NXMAssociationState
    {
        public NXMAssociationStatus Status { get; init; }

        /// <summary>
        /// Whether Stardrop's own registration is complete and points at the running executable.
        /// </summary>
        public bool IsStardropRegistered { get; init; }

        public string? UserChoiceProgId { get; init; }
        public string? UserChoiceCommand { get; init; }

        /// <summary>
        /// A display name for whichever application currently owns the protocol (used for warning messages)
        /// </summary>
        public string HandlerName { get; init; } = String.Empty;
    }

    internal static class NXMProtocol
    {
        private const string ProtocolName = "nxm";
        private const string ProgId = "Stardrop.nxm";
        private const string ClassesPath = @"Software\Classes";
        private const string CapabilitiesPath = @"Software\Stardrop\Capabilities";
        private const string RegisteredApplicationsPath = @"Software\RegisteredApplications";
        private const string RegisteredApplicationName = "Stardrop";
        private const string UserChoicePath = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\nxm\UserChoice";

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
                string command = GetExpectedCommand(applicationPath);
                string icon = $"\"{applicationPath}\",0";

                // The dedicated ProgId is what Windows offers under Settings > Default apps
                using (RegistryKey progIdKey = Registry.CurrentUser.CreateSubKey($@"{ClassesPath}\{ProgId}"))
                {
                    progIdKey.SetValue(String.Empty, "URL:Nexus Mods Protocol");
                    progIdKey.SetValue("URL Protocol", String.Empty);

                    using (RegistryKey iconKey = progIdKey.CreateSubKey("DefaultIcon"))
                    {
                        iconKey.SetValue(String.Empty, icon);
                    }

                    using (RegistryKey commandKey = progIdKey.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey.SetValue(String.Empty, command);
                    }
                }

                // The bare protocol key is the fallback Windows uses when no UserChoice has been made
                using (RegistryKey protocolKey = Registry.CurrentUser.CreateSubKey($@"{ClassesPath}\{ProtocolName}"))
                {
                    protocolKey.SetValue(String.Empty, "URL:Nexus Mods Protocol");
                    protocolKey.SetValue("URL Protocol", String.Empty);

                    using (RegistryKey iconKey = protocolKey.CreateSubKey("DefaultIcon"))
                    {
                        iconKey.SetValue(String.Empty, icon);
                    }

                    using (RegistryKey commandKey = protocolKey.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey.SetValue(String.Empty, command);
                    }
                }

                // Capabilities are what allow Stardrop to appear in Windows' default app picker
                using (RegistryKey capabilitiesKey = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
                {
                    capabilitiesKey.SetValue("ApplicationName", "Stardrop");
                    capabilitiesKey.SetValue("ApplicationDescription", "A mod manager for Stardew Valley.");
                    capabilitiesKey.SetValue("ApplicationIcon", icon);

                    using (RegistryKey urlAssociationsKey = capabilitiesKey.CreateSubKey("UrlAssociations"))
                    {
                        urlAssociationsKey.SetValue(ProtocolName, ProgId);
                    }
                }

                using (RegistryKey registeredApplicationsKey = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsPath))
                {
                    registeredApplicationsKey.SetValue(RegisteredApplicationName, CapabilitiesPath);
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to associate Stardrop with the NXM protocol: {ex}", Helper.Status.Alert);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines how Windows will actually route NXM links. Note that a correct Stardrop registration is not
        /// enough on its own, as a UserChoice belonging to another mod manager takes priority over it.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static NXMAssociationState GetState(string applicationPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) is false)
            {
                Program.helper.Log($"Attempted to read registery keys for NXM protocol on a non-Windows system!");
                return new NXMAssociationState() { Status = NXMAssociationStatus.Unregistered };
            }

            try
            {
                string expectedCommand = GetExpectedCommand(applicationPath);
                bool hasProgIdKey = IsExpectedCommand(GetCommandForProgId(ProgId), expectedCommand);
                bool hasProtocolKey = IsExpectedCommand(GetCommandForProgId(ProtocolName), expectedCommand);
                bool isStardropRegistered = hasProgIdKey && hasProtocolKey && HasCapabilities();

                NXMAssociationStatus localStatus = NXMAssociationStatus.Unregistered;
                if (isStardropRegistered)
                {
                    localStatus = NXMAssociationStatus.Registered;
                }
                else if (hasProgIdKey || hasProtocolKey)
                {
                    localStatus = NXMAssociationStatus.Incomplete;
                }

                // Windows checks UserChoice before it looks at the protocol's class key, so anything set there wins
                string? userChoiceProgId = GetUserChoiceProgId();
                if (String.IsNullOrEmpty(userChoiceProgId) || String.Equals(userChoiceProgId, ProtocolName, StringComparison.OrdinalIgnoreCase))
                {
                    return new NXMAssociationState()
                    {
                        Status = localStatus,
                        IsStardropRegistered = isStardropRegistered,
                        HandlerName = RegisteredApplicationName
                    };
                }

                string? userChoiceCommand = GetCommandForProgId(userChoiceProgId);
                if (String.Equals(userChoiceProgId, ProgId, StringComparison.OrdinalIgnoreCase) || IsExpectedCommand(userChoiceCommand, expectedCommand))
                {
                    return new NXMAssociationState()
                    {
                        Status = localStatus,
                        IsStardropRegistered = isStardropRegistered,
                        UserChoiceProgId = userChoiceProgId,
                        UserChoiceCommand = userChoiceCommand,
                        HandlerName = RegisteredApplicationName
                    };
                }

                return new NXMAssociationState()
                {
                    Status = NXMAssociationStatus.Overridden,
                    IsStardropRegistered = isStardropRegistered,
                    UserChoiceProgId = userChoiceProgId,
                    UserChoiceCommand = userChoiceCommand,
                    HandlerName = GetDisplayName(userChoiceProgId, userChoiceCommand)
                };
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to verify Stardrop's association with the NXM protocol: {ex}", Helper.Status.Alert);
                return new NXMAssociationState() { Status = NXMAssociationStatus.Unregistered };
            }
        }

        /// <summary>
        /// Writes the resolved NXM protocol handler to the log, so reports of "links open the wrong manager" can be
        /// diagnosed without asking the user to read their own registry.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static void LogDiagnostics(string applicationPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) is false)
            {
                return;
            }

            try
            {
                NXMAssociationState state = GetState(applicationPath);

                StringBuilder report = new StringBuilder();
                report.AppendLine($"{Environment.NewLine}-- NXM Protocol --");
                report.AppendLine($"Status: {state.Status}");
                report.AppendLine($"Expected Command: {GetExpectedCommand(applicationPath)}");
                report.AppendLine($"UserChoice ProgId: {GetLoggableValue(state.UserChoiceProgId)}");
                report.AppendLine($"UserChoice Command: {GetLoggableValue(state.UserChoiceCommand)}");
                report.AppendLine($"Stardrop ProgId Command: {GetLoggableValue(GetCommandForProgId(ProgId))}");
                report.AppendLine($"Protocol Key Command: {GetLoggableValue(GetCommandForProgId(ProtocolName))}");
                report.AppendLine($"Machine Protocol Key Command: {GetLoggableValue(GetMachineCommandForProgId(ProtocolName))}");
                report.AppendLine($"Capabilities Registered: {HasCapabilities()}");
                report.Append($"Resolved Handler: {state.HandlerName}{Environment.NewLine}------------------{Environment.NewLine}");

                Program.helper.Log(report.ToString());
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to log the NXM protocol association: {ex}", Helper.Status.Alert);
            }
        }

        private static string GetExpectedCommand(string applicationPath)
        {
            return $"\"{applicationPath}\" --nxm \"%1\"";
        }

        private static bool IsExpectedCommand(string? command, string expectedCommand)
        {
            return String.Equals(command, expectedCommand, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetLoggableValue(string? value)
        {
            return String.IsNullOrEmpty(value) ? "(none)" : value;
        }

        [SupportedOSPlatform("windows")]
        private static string? GetUserChoiceProgId()
        {
            using RegistryKey? userChoiceKey = Registry.CurrentUser.OpenSubKey(UserChoicePath);

            return userChoiceKey?.GetValue("ProgId")?.ToString();
        }

        [SupportedOSPlatform("windows")]
        private static string? GetCommandForProgId(string progId)
        {
            using RegistryKey? userKey = Registry.CurrentUser.OpenSubKey($@"{ClassesPath}\{progId}\shell\open\command");
            string? command = userKey?.GetValue(String.Empty)?.ToString();
            if (String.IsNullOrEmpty(command) is false)
            {
                return command;
            }

            return GetMachineCommandForProgId(progId);
        }

        [SupportedOSPlatform("windows")]
        private static string? GetMachineCommandForProgId(string progId)
        {
            using RegistryKey? machineKey = Registry.LocalMachine.OpenSubKey($@"{ClassesPath}\{progId}\shell\open\command");

            return machineKey?.GetValue(String.Empty)?.ToString();
        }

        [SupportedOSPlatform("windows")]
        private static bool HasCapabilities()
        {
            using RegistryKey? urlAssociationsKey = Registry.CurrentUser.OpenSubKey($@"{CapabilitiesPath}\UrlAssociations");
            if (String.Equals(urlAssociationsKey?.GetValue(ProtocolName)?.ToString(), ProgId, StringComparison.OrdinalIgnoreCase) is false)
            {
                return false;
            }

            using RegistryKey? registeredApplicationsKey = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsPath);

            return String.Equals(registeredApplicationsKey?.GetValue(RegisteredApplicationName)?.ToString(), CapabilitiesPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Pulls a friendly name out of a shell command, falling back to the ProgId when the command can't be parsed.
        /// </summary>
        private static string GetDisplayName(string progId, string? command)
        {
            if (String.IsNullOrEmpty(command) is false)
            {
                string executable = command.Trim();
                if (executable.StartsWith('"'))
                {
                    int closingQuote = executable.IndexOf('"', 1);
                    executable = closingQuote > 1 ? executable.Substring(1, closingQuote - 1) : executable.Substring(1);
                }
                else
                {
                    int firstSpace = executable.IndexOf(' ');
                    executable = firstSpace > 0 ? executable.Substring(0, firstSpace) : executable;
                }

                try
                {
                    string name = Path.GetFileNameWithoutExtension(executable);
                    if (String.IsNullOrEmpty(name) is false)
                    {
                        return name;
                    }
                }
                catch (ArgumentException)
                {
                    // The command wasn't a usable path, so fall through to the ProgId
                }
            }

            return progId;
        }
    }
}
