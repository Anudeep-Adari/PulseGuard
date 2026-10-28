using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using PulseGuard.Data;
using PulseGuard.Safety;

namespace PulseGuard.Core.GamingBooster
{
    public static class AdvancedOSOptimizer
    {
        /// <summary>
        /// Applies a specific Windows OS tweak based on its configuration.
        /// </summary>
        public static bool ApplyTweak(WindowsTweakItem tweak, bool dryRun)
        {
            if (tweak == null) return false;

            try
            {
                if (tweak.RegistryPath == "CLI_CMD")
                {
                    return ExecuteCliTweak(tweak, dryRun);
                }

                return WriteRegistryTweak(tweak, dryRun);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error applying tweak #{tweak.ItemNumber} ({tweak.FeatureName}): {ex.Message}");
                return false;
            }
        }

        private static bool WriteRegistryTweak(WindowsTweakItem tweak, bool dryRun)
        {
            string hive = tweak.RegistryPath.Split('\\')[0];
            string subKeyPath = tweak.RegistryPath.Substring(hive.Length + 1);

            // Handle escaped registry strings if present
            subKeyPath = subKeyPath.Replace("[Escaped]", "");

            RegistryKey rootKey = hive.ToUpper() switch
            {
                "HKLM" => Registry.LocalMachine,
                "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKCU" => Registry.CurrentUser,
                "HKEY_CURRENT_USER" => Registry.CurrentUser,
                _ => throw new ArgumentException($"Unsupported hive: {hive}")
            };

            // If it is an interface-specific registry write (DWORD_IF)
            if (tweak.ValueType == "DWORD_IF")
            {
                return ApplyDwordIfTweak(rootKey, subKeyPath, tweak.ValueName, tweak.OptimizedValue, dryRun, tweak.ItemNumber);
            }

            // Normal registry write
            if (dryRun)
            {
                string currentVal = "Not Checked";
                try
                {
                    using var testKey = rootKey.OpenSubKey(subKeyPath);
                    currentVal = testKey?.GetValue(tweak.ValueName)?.ToString() ?? "Null";
                }
                catch {}

                AuditEngine.RegisterProposedChange(
                    "RegistryWrite",
                    tweak.RegistryPath,
                    tweak.ValueName,
                    currentVal,
                    tweak.OptimizedValue
                );
                return true;
            }

            // Real execution
            if (!WhitelistGuard.IsPathModificationAllowed(tweak.RegistryPath)) return false;

            using var key = rootKey.CreateSubKey(subKeyPath, writable: true);
            if (key == null) return false;

            string originalValue = key.GetValue(tweak.ValueName)?.ToString() ?? "";
            
            // Set values based on type
            if (tweak.ValueType.Equals("DWORD", StringComparison.OrdinalIgnoreCase))
            {
                int val = int.Parse(tweak.OptimizedValue);
                key.SetValue(tweak.ValueName, val, RegistryValueKind.DWord);
            }
            else
            {
                key.SetValue(tweak.ValueName, tweak.OptimizedValue, RegistryValueKind.String);
            }

            // Save original state to SQLite
            DatabaseManager.UpdateWindowsTweakStatus(tweak.ItemNumber, "Applied", originalValue);

            // Register audit change
            AuditEngine.RegisterProposedChange(
                "RegistryWrite",
                tweak.RegistryPath,
                tweak.ValueName,
                originalValue,
                tweak.OptimizedValue
            );

            return true;
        }

        private static bool ApplyDwordIfTweak(RegistryKey rootKey, string baseKeyPath, string valName, string optVal, bool dryRun, int itemNum)
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            int intVal = int.Parse(optVal);

            foreach (var i in interfaces)
            {
                if (i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    string id = i.Id; // Interface GUID
                    string fullPath = Path.Combine(baseKeyPath, id);

                    if (dryRun)
                    {
                        AuditEngine.RegisterProposedChange(
                            "RegistryWrite",
                            $"{rootKey.Name}\\{fullPath}",
                            valName,
                            "Not Checked",
                            optVal
                        );
                        continue;
                    }

                    if (!WhitelistGuard.IsPathModificationAllowed($"{rootKey.Name}\\{fullPath}")) continue;

                    using var key = rootKey.CreateSubKey(fullPath, writable: true);
                    if (key != null)
                    {
                        string original = key.GetValue(valName)?.ToString() ?? "";
                        key.SetValue(valName, intVal, RegistryValueKind.DWord);

                        DatabaseManager.UpdateWindowsTweakStatus(itemNum, "Applied", original);
                    }
                }
            }
            return true;
        }

        private static bool ExecuteCliTweak(WindowsTweakItem tweak, bool dryRun)
        {
            if (dryRun)
            {
                AuditEngine.RegisterProposedChange(
                    "CLI_CMD",
                    tweak.ValueName,
                    "Execution",
                    "DryRun queued",
                    tweak.OptimizedValue
                );
                return true;
            }

            try
            {
                string binary = tweak.ValueName;
                string args = tweak.OptimizedValue;

                if (tweak.ValueType == "PowerShell")
                {
                    binary = "powershell.exe";
                    args = $"-NoProfile -Command \"{tweak.OptimizedValue}\"";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = binary,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                process?.WaitForExit();

                DatabaseManager.UpdateWindowsTweakStatus(tweak.ItemNumber, "Applied", "CLI_Run");
                return process?.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CLI tweak failed: {ex.Message}");
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("ntdll.dll")]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        /// <summary>
        /// Reverts a specific Windows OS tweak back to its recorded original state.
        /// </summary>
        public static bool RevertTweak(WindowsTweakItem tweak)
        {
            if (tweak == null) return false;

            try
            {
                if (tweak.RegistryPath == "CLI_CMD")
                {
                    if (tweak.ValueName == "MMAgent")
                    {
                        RunProcessSilently("powershell.exe", "-NoProfile -Command \"Enable-MMAgent -MemoryCompression\"");
                    }
                    else if (tweak.ValueName == "timer")
                    {
                        NtSetTimerResolution(156250, false, out _); // Revert to default (~15.6ms)
                    }
                    else if (tweak.ValueName == "powercfg")
                    {
                        RunProcessSilently("powercfg.exe", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e"); // Balanced plan
                    }
                    else if (tweak.ValueName == "netsh")
                    {
                        RunProcessSilently("netsh.exe", "int tcp set global autotuninglevel=normal");
                    }
                    
                    DatabaseManager.UpdateWindowsTweakStatus(tweak.ItemNumber, "Not Applied", "");
                    return true;
                }

                return RevertRegistryTweak(tweak);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reverting tweak #{tweak.ItemNumber}: {ex.Message}");
                return false;
            }
        }

        private static bool RevertRegistryTweak(WindowsTweakItem tweak)
        {
            string hive = tweak.RegistryPath.Split('\\')[0];
            string subKeyPath = tweak.RegistryPath.Substring(hive.Length + 1).Replace("[Escaped]", "");

            RegistryKey rootKey = hive.ToUpper() switch
            {
                "HKLM" => Registry.LocalMachine,
                "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKCU" => Registry.CurrentUser,
                "HKEY_CURRENT_USER" => Registry.CurrentUser,
                _ => throw new ArgumentException($"Unsupported hive: {hive}")
            };

            if (tweak.ValueType == "DWORD_IF")
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var i in interfaces)
                {
                    if (i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        string fullPath = Path.Combine(subKeyPath, i.Id);
                        if (!WhitelistGuard.IsPathModificationAllowed($"{rootKey.Name}\\{fullPath}")) continue;

                        using var interfaceKey = rootKey.CreateSubKey(fullPath, writable: true);
                        if (interfaceKey != null)
                        {
                            if (string.IsNullOrEmpty(tweak.OriginalValue) || tweak.OriginalValue == "Null")
                            {
                                interfaceKey.DeleteValue(tweak.ValueName, throwOnMissingValue: false);
                            }
                            else
                            {
                                int val = int.Parse(tweak.OriginalValue);
                                interfaceKey.SetValue(tweak.ValueName, val, RegistryValueKind.DWord);
                            }
                        }
                    }
                }
                DatabaseManager.UpdateWindowsTweakStatus(tweak.ItemNumber, "Not Applied", "");
                return true;
            }

            if (!WhitelistGuard.IsPathModificationAllowed(tweak.RegistryPath)) return false;

            using var key = rootKey.CreateSubKey(subKeyPath, writable: true);
            if (key == null) return false;

            if (string.IsNullOrEmpty(tweak.OriginalValue) || tweak.OriginalValue == "Null")
            {
                key.DeleteValue(tweak.ValueName, throwOnMissingValue: false);
            }
            else
            {
                if (tweak.ValueType.Equals("DWORD", StringComparison.OrdinalIgnoreCase))
                {
                    int val = int.Parse(tweak.OriginalValue);
                    key.SetValue(tweak.ValueName, val, RegistryValueKind.DWord);
                }
                else
                {
                    key.SetValue(tweak.ValueName, tweak.OriginalValue, RegistryValueKind.String);
                }
            }

            DatabaseManager.UpdateWindowsTweakStatus(tweak.ItemNumber, "Not Applied", "");
            return true;
        }

        private static void RunProcessSilently(string binary, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = binary,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit();
            }
            catch {}
        }
    }
}
