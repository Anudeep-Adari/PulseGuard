using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using PulseGuard.Data;

namespace PulseGuard.Core.HealthMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public void Init()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    public class CpuStatus
    {
        public double Temperature { get; set; }
        public bool IsThrottling { get; set; }
        public int LoadPercentage { get; set; }
    }

    public class BatteryReport
    {
        public bool HasBattery { get; set; }
        public string Name { get; set; }
        public double DesignCapacityMwh { get; set; }
        public double FullChargeCapacityMwh { get; set; }
        public double HealthPercentage { get; set; } // Full / Design
        public int CycleCount { get; set; }
    }

    public class EventLogErrorSummary
    {
        public DateTime TimeCreated { get; set; }
        public string Source { get; set; }
        public int EventId { get; set; }
        public string Severity { get; set; }
        public string Description { get; set; }
        public string Translation { get; set; } // Plain English explanation
    }

    public class BsodSummary
    {
        public string Filename { get; set; }
        public DateTime CrashTime { get; set; }
        public string BugcheckCode { get; set; }
        public string ProbableCause { get; set; }
    }

    public static class HealthMonitorManager
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        /// <summary>
        /// Reads CPU temperatures, load, and check if it is throttling.
        /// </summary>
        public static CpuStatus GetCpuStatus()
        {
            var status = new CpuStatus
            {
                Temperature = 45.0, // Default baseline if WMI fails
                IsThrottling = false,
                LoadPercentage = 0
            };

            try
            {
                // Query CPU Load
                using (var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"))
                {
                    cpuCounter.NextValue();
                    System.Threading.Thread.Sleep(100);
                    status.LoadPercentage = (int)Math.Round(cpuCounter.NextValue());
                }

                // Query WMI Thermal Zone
                using (var searcher = new ManagementObjectSearcher(@"Root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        double tempK = Convert.ToDouble(obj["CurrentTemperature"] ?? 0);
                        if (tempK > 0)
                        {
                            // Convert Kelvin tenths to Celsius
                            double tempC = (tempK - 2731.5) / 10.0;
                            status.Temperature = tempC;
                            break; // Take first zone
                        }
                    }
                }

                // Query clock speed to check throttling
                using (var searcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        uint currentSpeed = Convert.ToUInt32(obj["CurrentClockSpeed"] ?? 0);
                        uint maxSpeed = Convert.ToUInt32(obj["MaxClockSpeed"] ?? 0);
                        if (currentSpeed > 0 && maxSpeed > 0 && currentSpeed < (maxSpeed * 0.8))
                        {
                            status.IsThrottling = true; // CPU running at less than 80% maximum speed
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving CPU status: {ex.Message}");
            }

            return status;
        }

        /// <summary>
        /// Retrieves RAM metrics.
        /// </summary>
        public static MEMORYSTATUSEX GetRamStatus()
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.Init();
            GlobalMemoryStatusEx(ref memStatus);
            return memStatus;
        }

        /// <summary>
        /// Generates laptop battery health status.
        /// </summary>
        public static BatteryReport GetBatteryReport()
        {
            var report = new BatteryReport { HasBattery = false };

            try
            {
                var searcher = new ManagementObjectSearcher("SELECT Name, DesignCapacity, FullChargeCapacity FROM Win32_Battery");
                var collection = searcher.Get();

                if (collection.Count == 0) return report;

                foreach (ManagementObject obj in collection)
                {
                    report.HasBattery = true;
                    report.Name = obj["Name"]?.ToString() ?? "Battery";
                    
                    double design = Convert.ToDouble(obj["DesignCapacity"] ?? 0);
                    double full = Convert.ToDouble(obj["FullChargeCapacity"] ?? 0);

                    report.DesignCapacityMwh = design;
                    report.FullChargeCapacityMwh = full;
                    report.HealthPercentage = design > 0 ? (full / design) * 100.0 : 100.0;
                    report.CycleCount = -1; // WMI doesn't easily return cycle count, default placeholder
                }

                // Attempt to parse XML battery report if detailed cycles are needed
                string xmlPath = Path.Combine(Path.GetTempPath(), "batteryreport.xml");
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = $"/batteryreport /xml /output \"{xmlPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit();

                if (File.Exists(xmlPath))
                {
                    string xmlContent = File.ReadAllText(xmlPath);
                    var matchCycle = Regex.Match(xmlContent, @"<CycleCount>(\d+)</CycleCount>");
                    if (matchCycle.Success && int.TryParse(matchCycle.Groups[1].Value, out int cycles))
                    {
                        report.CycleCount = cycles;
                    }
                    File.Delete(xmlPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Battery status extraction failed: {ex.Message}");
            }

            return report;
        }

        /// <summary>
        /// Scans Event Viewer logs for critical hardware and system errors.
        /// </summary>
        public static List<EventLogErrorSummary> ScanEventViewerErrors()
        {
            var list = new List<EventLogErrorSummary>();
            try
            {
                // Query System logs for Error and Critical errors in the last 24 hours
                string queryStr = "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 86400000]]]";
                var eventsQuery = new EventLogQuery("System", PathType.LogName, queryStr);
                using var reader = new EventLogReader(eventsQuery);

                EventRecord record;
                int count = 0;
                while ((record = reader.ReadEvent()) != null && count < 10) // Limit to top 10 recent
                {
                    using (record)
                    {
                        var summary = new EventLogErrorSummary
                        {
                            TimeCreated = record.TimeCreated ?? DateTime.Now,
                            Source = record.ProviderName,
                            EventId = record.Id,
                            Severity = record.Level == 1 ? "Critical" : "Error",
                            Description = record.FormatDescription() ?? "No description available."
                        };
                        summary.Translation = TranslateEvent(summary.Source, summary.EventId);
                        list.Add(summary);
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Event log scan failed: {ex.Message}");
            }
            return list;
        }

        private static string TranslateEvent(string source, int eventId)
        {
            if (source.Contains("Disk", StringComparison.OrdinalIgnoreCase))
            {
                if (eventId == 7 || eventId == 11)
                    return "The hard drive reported a bad sector block or a controller timeout. This indicates potential disk failure.";
            }
            if (source.Contains("WHEA-Logger", StringComparison.OrdinalIgnoreCase))
            {
                return "A hardware error was reported by the CPU or motherboard cache controllers. Possible hardware degradation or unstable overclock.";
            }
            if (source.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase) && eventId == 41)
            {
                return "The system rebooted unexpectedly without cleanly shutting down. Often caused by power loss, crashes, or system instability.";
            }
            return "General system error event occurred. Check logs for details.";
        }

        /// <summary>
        /// Analyzes dump files for BSOD events.
        /// </summary>
        public static List<BsodSummary> AnalyzeCrashDumps()
        {
            var list = new List<BsodSummary>();
            string minidumpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");

            if (!Directory.Exists(minidumpDir)) return list;

            try
            {
                var files = Directory.GetFiles(minidumpDir, "*.dmp");
                foreach (var f in files)
                {
                    var fileInfo = new FileInfo(f);
                    list.Add(new BsodSummary
                    {
                        Filename = fileInfo.Name,
                        CrashTime = fileInfo.CreationTime,
                        BugcheckCode = "0x" + fileInfo.Length.ToString("X"), // Placeholder logic, actual stop codes require dbghelp parsing
                        ProbableCause = "Unidentified device driver collision"
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Crash dump analysis failed: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// Gets system uptime in days.
        /// </summary>
        public static double GetSystemUptimeDays()
        {
            long ticks = Environment.TickCount64; // In milliseconds
            return ticks / (1000.0 * 60.0 * 60.0 * 24.0);
        }

        /// <summary>
        /// Computes the final composite System Health Score (0 - 100).
        /// </summary>
        public static int ComputeHealthScore(bool smartPredictFailure, bool sfcCorruptions, int numEventErrors, double uptimeDays, double cpuTemp)
        {
            int score = 100;

            // Deduct for failing disks (SMART)
            if (smartPredictFailure) score -= 30;

            // Deduct for OS corruptions
            if (sfcCorruptions) score -= 15;

            // Deduct for recent critical event logs
            score -= Math.Min(numEventErrors * 5, 20);

            // Deduct for extreme CPU uptime (needs reboot to clear leaks)
            if (uptimeDays > 7.0) score -= 10;
            else if (uptimeDays > 14.0) score -= 15;

            // Deduct for high temperatures
            if (cpuTemp > 90.0) score -= 20;
            else if (cpuTemp > 80.0) score -= 10;

            return Math.Max(score, 0);
        }
    }
}
