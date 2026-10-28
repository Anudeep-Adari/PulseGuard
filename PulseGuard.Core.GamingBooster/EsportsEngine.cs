using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using PulseGuard.Data;
using PulseGuard.Safety;

namespace PulseGuard.Core.GamingBooster
{
    public static class EsportsEngine
    {
        private static string _originalPowerPlanGuid = null;
        private static uint _originalTimerResolution = 0;
        private static bool _timerResolutionSet = false;
        private static Thread _monitorThread = null;
        private static bool _isMonitoring = false;
        private static string _activeGameProcess = null;

        [DllImport("ntdll.dll")]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryTimerResolution(out uint MaximumResolution, out uint MinimumResolution, out uint CurrentResolution);

        /// <summary>
        /// Starts the background thread to automatically monitor the launch of watched games.
        /// </summary>
        public static void StartAutoMonitoring()
        {
            if (_isMonitoring) return;
            _isMonitoring = true;

            _monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = "PulseGuard_GameMonitor"
            };
            _monitorThread.Start();
        }

        /// <summary>
        /// Stops the background game monitor.
        /// </summary>
        public static void StopAutoMonitoring()
        {
            _isMonitoring = false;
            if (_monitorThread != null && _monitorThread.IsAlive)
            {
                _monitorThread.Join(2000);
            }
        }

        private static void MonitorLoop()
        {
            while (_isMonitoring)
            {
                try
                {
                    var profiles = DatabaseManager.GetGameProfiles();
                    var runningProcesses = Process.GetProcesses();

                    string detectedGameProc = null;
                    GameProfile activeProfile = null;

                    foreach (var proc in runningProcesses)
                    {
                        try
                        {
                            var profile = profiles.FirstOrDefault(p => string.Equals(p.ProcessName, proc.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase) || 
                                                                       string.Equals(p.ProcessName, proc.ProcessName, StringComparison.OrdinalIgnoreCase));
                            if (profile != null)
                            {
                                detectedGameProc = proc.ProcessName;
                                activeProfile = profile;
                                break;
                            }
                        }
                        catch
                        {
                            // Access denied for system processes
                        }
                    }

                    if (detectedGameProc != null && _activeGameProcess == null)
                    {
                        // Game started!
                        _activeGameProcess = detectedGameProc;
                        EnterGameSession(activeProfile);
                    }
                    else if (detectedGameProc == null && _activeGameProcess != null)
                    {
                        // Game exited!
                        ExitGameSession();
                        _activeGameProcess = null;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in Game Monitor Loop: {ex.Message}");
                }

                Thread.Sleep(5000); // Poll every 5 seconds
            }
        }

        /// <summary>
        /// Applies all gaming performance boosts for a specific game profile.
        /// </summary>
        public static void EnterGameSession(GameProfile profile)
        {
            Console.WriteLine($"Entering Gaming Session for: {profile.DisplayName}");
            
            // 1. Elevate process priorities and cores affinity
            ApplyProcessPriorityAndAffinity(profile);

            // 2. Set active scheme to Ultimate Performance
            ApplyUltimatePowerPlan();

            // 3. Set High Precision Timer Resolution (0.5ms)
            ApplyHighPrecisionTimer();

            // 4. Disable Game DVR & DVR Capture
            DisableGameDVR();

            // 5. Optimize Network Latency (TCP_NODELAY & QoS)
            if (profile.DisableNagleAlgorithm)
            {
                OptimizeNetworkLatency(true);
            }
        }

        /// <summary>
        /// Reverts system settings to default states after a game exits.
        /// </summary>
        public static void ExitGameSession()
        {
            Console.WriteLine("Exiting Gaming Session. Reverting tweaks...");

            // 1. Revert Power Plan
            RevertPowerPlan();

            // 2. Revert Timer Resolution
            RevertTimerResolution();

            // 3. Revert Network tweaks
            OptimizeNetworkLatency(false);
        }

        private static void ApplyProcessPriorityAndAffinity(GameProfile profile)
        {
            try
            {
                string procName = Path.GetFileNameWithoutExtension(profile.ProcessName);
                var procs = Process.GetProcessesByName(procName);
                foreach (var proc in procs)
                {
                    // Elevate priority
                    proc.PriorityClass = profile.CpuPriority switch
                    {
                        4 => ProcessPriorityClass.RealTime,
                        3 => ProcessPriorityClass.High,
                        2 => ProcessPriorityClass.AboveNormal,
                        _ => ProcessPriorityClass.Normal
                    };

                    // Apply CPU affinity (excluding Intel E-Cores / thread-pinning helper)
                    if (profile.CpuAffinityMask != -1)
                    {
                        proc.ProcessorAffinity = (IntPtr)profile.CpuAffinityMask;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to apply priority/affinity: {ex.Message}");
            }
        }

        private static void ApplyUltimatePowerPlan()
        {
            try
            {
                // Capture active power plan GUID
                _originalPowerPlanGuid = GetActivePowerPlanGuid();

                // Guid of Ultimate Performance plan
                string ultimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

                // Check if Ultimate Performance plan exists
                var checkProc = Process.Start(new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/list",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                string output = checkProc.StandardOutput.ReadToEnd();
                checkProc.WaitForExit();

                if (!output.Contains(ultimateGuid))
                {
                    // Duplicate/Create Ultimate Performance scheme
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "powercfg.exe",
                        Arguments = $"/duplicatescheme {ultimateGuid}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    })?.WaitForExit();
                }

                // Set Active Scheme
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = $"/setactive {ultimateGuid}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })?.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Power plan apply failed: {ex.Message}");
            }
        }

        private static void RevertPowerPlan()
        {
            if (string.IsNullOrEmpty(_originalPowerPlanGuid)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = $"/setactive {_originalPowerPlanGuid}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })?.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Power plan revert failed: {ex.Message}");
            }
        }

        private static string GetActivePowerPlanGuid()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/getactivescheme",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                string outStr = proc?.StandardOutput.ReadToEnd();
                proc?.WaitForExit();

                if (outStr != null)
                {
                    // Parse GUID from format "Power Scheme GUID: e9a42b02-d5df-448d-aa00-03f14749eb61  (Ultimate Performance)"
                    int guidIndex = outStr.IndexOf("GUID: ");
                    if (guidIndex != -1)
                    {
                        return outStr.Substring(guidIndex + 6, 36);
                    }
                }
            }
            catch {}
            return null;
        }

        private static void ApplyHighPrecisionTimer()
        {
            try
            {
                NtQueryTimerResolution(out _, out _, out uint current);
                _originalTimerResolution = current;

                // Request 0.5ms resolution (5000 units in 100ns steps)
                int status = NtSetTimerResolution(5000, true, out _);
                if (status == 0) // STATUS_SUCCESS
                {
                    _timerResolutionSet = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set timer resolution: {ex.Message}");
            }
        }

        private static void RevertTimerResolution()
        {
            if (!_timerResolutionSet) return;

            try
            {
                NtSetTimerResolution(_originalTimerResolution, false, out _);
                _timerResolutionSet = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to revert timer resolution: {ex.Message}");
            }
        }

        private static void DisableGameDVR()
        {
            try
            {
                // Disable Game DVR
                using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore", writable: true);
                if (key != null)
                {
                    key.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                }

                using var key2 = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", writable: true);
                if (key2 != null)
                {
                    key2.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
                }
            }
            catch {}
        }

        private static void OptimizeNetworkLatency(bool optimize)
        {
            try
            {
                // Retrieve active interfaces
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                int regVal = optimize ? 1 : 0;

                foreach (var i in interfaces)
                {
                    if (i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        string id = i.Id; // Interface GUID
                        string regPath = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{id}";
                        
                        using var key = Registry.LocalMachine.OpenSubKey(regPath, writable: true);
                        if (key != null)
                        {
                            if (optimize)
                            {
                                key.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                                key.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                            }
                            else
                            {
                                key.DeleteValue("TcpAckFrequency", false);
                                key.DeleteValue("TCPNoDelay", false);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Network latency optimize error: {ex.Message}");
            }
        }

        /// <summary>
        /// Audits Hardware-Accelerated GPU Scheduling (HAGS).
        /// </summary>
        /// <returns>1 = Enabled, 0 = Disabled/Supported, -1 = Not Supported.</returns>
        public static int CheckHagsStatus()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                if (key != null)
                {
                    var val = key.GetValue("HwSchMode");
                    if (val != null)
                    {
                        return Convert.ToInt32(val); // 2 = Enabled, 1 = Supported but Disabled
                    }
                }
            }
            catch {}
            return -1; // Not supported or registry key missing
        }

        /// <summary>
        /// Enables HAGS. (Requires system reboot to apply).
        /// </summary>
        public static bool EnableHags()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", writable: true);
                if (key != null)
                {
                    key.SetValue("HwSchMode", 2, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch {}
            return false;
        }

        /// <summary>
        /// Safely purges Direct3D and graphics shader caches.
        /// </summary>
        public static void ClearShaderCache()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            
            var targetFolders = new List<string>
            {
                Path.Combine(localAppData, "D3DSCache"),
                Path.Combine(localAppData, "NVIDIA", "GLCache"),
                Path.Combine(localAppData, "LocalLow", "NVIDIA", "PerDriverVersion", "DXCache"),
                Path.Combine(localAppData, "AMD", "DxCache")
            };

            foreach (var path in targetFolders)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                        foreach (var f in files)
                        {
                            if (WhitelistGuard.IsPathModificationAllowed(f))
                            {
                                File.Delete(f);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error purging shader cache in {path}: {ex.Message}");
                    }
                }
            }
        }
    }
}
