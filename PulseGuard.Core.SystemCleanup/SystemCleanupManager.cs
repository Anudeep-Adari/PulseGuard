using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;
using PulseGuard.Safety;

namespace PulseGuard.Core.SystemCleanup
{
    public class StartupProgram
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string Location { get; set; } // Registry path or folder
        public string Impact { get; set; }   // High, Medium, Low, None (calculated)
    }

    public class BackgroundServiceInfo
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Status { get; set; }
        public string StartupType { get; set; }
    }

    public class OemAppInfo
    {
        public string Name { get; set; }
        public string Publisher { get; set; }
        public string UninstallString { get; set; }
    }

    public class DriverInfoSummary
    {
        public string Name { get; set; }
        public string ClassGuid { get; set; }
        public string Provider { get; set; }
        public string DriverVersion { get; set; }
        public string DriverDate { get; set; }
    }

    public static class SystemCleanupManager
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        /// <summary>
        /// Analyzes and retrieves startup programs from registry and startup folder.
        /// </summary>
        public static List<StartupProgram> GetStartupPrograms()
        {
            var list = new List<StartupProgram>();
            string[] locations = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
            };

            // Read from LocalMachine & CurrentUser
            foreach (var loc in locations)
            {
                ReadRegistryRunKeys(Registry.LocalMachine, loc, "HKLM", list);
                ReadRegistryRunKeys(Registry.CurrentUser, loc, "HKCU", list);
            }

            // Read from Startup folder
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
            {
                foreach (var file in Directory.GetFiles(startupFolder))
                {
                    list.Add(new StartupProgram
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Location = "Startup Folder",
                        Impact = "Medium" // Default estimate for folder shortcuts
                    });
                }
            }

            return list;
        }

        private static void ReadRegistryRunKeys(RegistryKey root, string subKeyPath, string hive, List<StartupProgram> list)
        {
            try
            {
                using var key = root.OpenSubKey(subKeyPath);
                if (key == null) return;

                foreach (var valName in key.GetValueNames())
                {
                    string command = key.GetValue(valName)?.ToString();
                    list.Add(new StartupProgram
                    {
                        Name = valName,
                        Command = command,
                        Location = $"{hive}\\{subKeyPath}",
                        Impact = CalculateStartupImpact(command)
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading startup keys: {ex.Message}");
            }
        }

        private static string CalculateStartupImpact(string command)
        {
            if (string.IsNullOrEmpty(command)) return "None";
            // Simple heuristic based on known heavy executables or length
            string lowerCmd = command.ToLowerInvariant();
            if (lowerCmd.Contains("update") || lowerCmd.Contains("electron") || lowerCmd.Contains("discord") || lowerCmd.Contains("steam"))
                return "High";
            if (lowerCmd.Contains("helper") || lowerCmd.Contains("agent") || lowerCmd.Contains("tray"))
                return "Medium";
            return "Low";
        }

        /// <summary>
        /// Retrieves non-essential background services candidates for optimization.
        /// </summary>
        public static List<BackgroundServiceInfo> GetOptimizeableServices()
        {
            var list = new List<BackgroundServiceInfo>();
            // Vetted non-essential services list
            var targetServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SysMain",     // Superfetch: can cause micro-stuttering on SSDs
                "DiagTrack",   // Connected User Experiences: diagnostic telemetry
                "dmwappushservice", // WAP Push Message Routing Service: telemetry
                "MapsBroker",  // Downloaded Maps Manager
                "RemoteRegistry",
                "WbioSrvc"     // Windows Biometric Service (if not using fingerprint login)
            };

            try
            {
                foreach (var sName in targetServices)
                {
                    using var sc = new ServiceController(sName);
                    // Force refresh status
                    sc.Refresh();
                    list.Add(new BackgroundServiceInfo
                    {
                        Name = sc.ServiceName,
                        DisplayName = sc.DisplayName,
                        Status = sc.Status.ToString(),
                        StartupType = sc.StartType.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning services: {ex.Message}");
            }

            return list;
        }

        /// <summary>
        /// Disables background services on the vetted list.
        /// </summary>
        public static bool OptimizeBackgroundServices(bool dryRun)
        {
            var optimizeable = GetOptimizeableServices();
            var proposedChanges = new List<SystemChangeDescription>();

            foreach (var s in optimizeable)
            {
                if (s.Status == "Running" || s.StartupType != "Disabled")
                {
                    proposedChanges.Add(new SystemChangeDescription
                    {
                        ActionType = "ServiceStop",
                        TargetPath = s.Name,
                        ValueName = "Start",
                        PreValue = s.Status,
                        PostValue = "Stopped/Disabled"
                    });
                }
            }

            if (proposedChanges.Count == 0) return true;

            if (dryRun)
            {
                foreach (var change in proposedChanges)
                {
                    AuditEngine.RegisterProposedChange(change.ActionType, change.TargetPath, change.ValueName, change.PreValue, change.PostValue);
                }
                return true;
            }

            // Real execution
            bool allSucceeded = true;
            var executedChanges = new List<SystemChangeDescription>();

            foreach (var change in proposedChanges)
            {
                if (!WhitelistGuard.IsServiceModificationAllowed(change.TargetPath)) continue;

                try
                {
                    using var sc = new ServiceController(change.TargetPath);
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    }

                    // Disable startup type via registry to avoid shell-out dependencies
                    using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{change.TargetPath}", writable: true);
                    if (key != null)
                    {
                        key.SetValue("Start", 4, RegistryValueKind.DWord); // 4 = Disabled
                    }

                    executedChanges.Add(change);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to optimize service {change.TargetPath}: {ex.Message}");
                    allSucceeded = false;
                }
            }

            if (executedChanges.Count > 0)
            {
                AuditEngine.CommitChange("SystemCleanup", "Optimized background telemetry and bloat services.", executedChanges);
            }

            return allSucceeded;
        }

        /// <summary>
        /// Trims working sets of running processes to release RAM back to the OS.
        /// </summary>
        public static void TrimStandbyMemory()
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    // Empty working set
                    SetProcessWorkingSetSize(proc.Handle, -1, -1);
                }
                catch
                {
                    // Ignore processes we do not have permission to access (e.g. SYSTEM/protected processes)
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }

        /// <summary>
        /// Discovers OEM/Manufacturer apps installed on the system using registry paths.
        /// </summary>
        public static List<OemAppInfo> GetInstalledOemApps()
        {
            var list = new List<OemAppInfo>();
            string[] uninstallKeys = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            var oemKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Lenovo", "Dell", "HP", "Hewlett-Packard", "McAfee", "Norton", "ASUS", "Acer", "Bloatware"
            };

            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var path in uninstallKeys)
                {
                    try
                    {
                        using var key = root.OpenSubKey(path);
                        if (key == null) continue;

                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            string dispName = subKey.GetValue("DisplayName")?.ToString();
                            string publisher = subKey.GetValue("Publisher")?.ToString();
                            string uninstallStr = subKey.GetValue("UninstallString")?.ToString();

                            if (string.IsNullOrEmpty(dispName)) continue;

                            bool isOem = false;
                            foreach (var keyword in oemKeywords)
                            {
                                if (dispName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || 
                                    (publisher != null && publisher.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                                {
                                    isOem = true;
                                    break;
                                }
                            }

                            if (isOem)
                            {
                                list.Add(new OemAppInfo
                                {
                                    Name = dispName,
                                    Publisher = publisher ?? "OEM Vendor",
                                    UninstallString = uninstallStr
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error scanning registry uninstall keys: {ex.Message}");
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Scans signed PNP drivers to list driver version info.
        /// </summary>
        public static List<DriverInfoSummary> GetInstalledDrivers()
        {
            var list = new List<DriverInfoSummary>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT FriendlyName, ClassGuid, Manufacturer, DriverVersion, DriverDate FROM Win32_PnPSignedDriver");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["FriendlyName"]?.ToString();
                    if (string.IsNullOrEmpty(name)) continue;

                    list.Add(new DriverInfoSummary
                    {
                        Name = name,
                        ClassGuid = obj["ClassGuid"]?.ToString(),
                        Provider = obj["Manufacturer"]?.ToString(),
                        DriverVersion = obj["DriverVersion"]?.ToString(),
                        DriverDate = obj["DriverDate"]?.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Driver scanning failed: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// Purges Windows Update Cache folder.
        /// </summary>
        public static bool CleanWindowsUpdateCache(bool dryRun)
        {
            string cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            if (!Directory.Exists(cachePath)) return true;

            if (dryRun)
            {
                long proposedBytes = CalculateFolderSize(cachePath);
                AuditEngine.RegisterProposedChange("FileDelete", cachePath, "SoftwareDistributionCache", proposedBytes.ToString(), "0");
                return true;
            }

            try
            {
                // Stop Windows Update Service
                using (var sc = new ServiceController("wuauserv"))
                {
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }

                // Delete cache content
                var files = Directory.GetFiles(cachePath, "*.*", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    if (WhitelistGuard.IsPathModificationAllowed(f))
                    {
                        File.Delete(f);
                    }
                }

                // Restart service
                using (var sc = new ServiceController("wuauserv"))
                {
                    sc.Start();
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Windows Update cleanup failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets visual registry configurations for performance optimization.
        /// </summary>
        public static bool OptimizeVisualEffects(bool dryRun)
        {
            const string path = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
            
            if (dryRun)
            {
                AuditEngine.RegisterProposedChange("RegistryWrite", $"HKEY_CURRENT_USER\\{path}", "VisualFXSetting", "2", "Best Performance");
                return true;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
                if (key != null)
                {
                    int original = Convert.ToInt32(key.GetValue("VisualFXSetting") ?? 0);
                    key.SetValue("VisualFXSetting", 2, RegistryValueKind.DWord); // 2 = Adjust for best performance

                    AuditEngine.RegisterProposedChange("RegistryWrite", $"HKEY_CURRENT_USER\\{path}", "VisualFXSetting", original.ToString(), "2");
                    AuditEngine.CommitChange("SystemCleanup", "Adjusted visual effects for best performance.", AuditEngine.GetProposedChanges());
                    AuditEngine.ClearProposedChanges();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to optimize visual effects: {ex.Message}");
                return false;
            }
        }

        private static long CalculateFolderSize(string path)
        {
            long size = 0;
            try
            {
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    size += new FileInfo(f).Length;
                }
            }
            catch {}
            return size;
        }
    }
}
