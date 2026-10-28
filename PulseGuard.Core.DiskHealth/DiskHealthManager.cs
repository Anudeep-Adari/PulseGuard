using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using PulseGuard.Safety;

namespace PulseGuard.Core.DiskHealth
{
    public class DriveSmartStatus
    {
        public string DeviceId { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public bool PredictFailure { get; set; }
        public int Reason { get; set; }
        public double Temperature { get; set; } // Celsius, if supported
    }

    public class SystemFileCheckResult
    {
        public bool SfcClean { get; set; }
        public bool DismClean { get; set; }
        public string OutputSummary { get; set; }
    }

    public class DiskClusterState
    {
        public long ClusterIndex { get; set; }
        public bool IsAllocated { get; set; }
    }

    public static class DiskHealthManager
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FSCTL_GET_VOLUME_BITMAP = 0x0009006f;

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTING_LCN_INPUT_BUFFER
        {
            public long StartingLcn;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            ref STARTING_LCN_INPUT_BUFFER lpInBuffer,
            int nInBufferSize,
            byte[] lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        /// <summary>
        /// Scans connected drives for WMI SMART health status.
        /// </summary>
        public static List<DriveSmartStatus> PerformSmartScan()
        {
            var results = new List<DriveSmartStatus>();
            try
            {
                // Retrieve general drive properties
                var driveQuery = new ManagementObjectSearcher(@"Root\CIMV2", "SELECT DeviceID, Model, SerialNumber FROM Win32_DiskDrive");
                var tempResults = new Dictionary<string, DriveSmartStatus>();

                foreach (ManagementObject drive in driveQuery.Get())
                {
                    string deviceId = drive["DeviceID"]?.ToString();
                    string model = drive["Model"]?.ToString();
                    string serial = drive["SerialNumber"]?.ToString()?.Trim();

                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        var status = new DriveSmartStatus
                        {
                            DeviceId = deviceId,
                            Model = model,
                            SerialNumber = serial,
                            PredictFailure = false,
                            Reason = 0,
                            Temperature = 0.0
                        };
                        tempResults[deviceId] = status;
                        results.Add(status);
                    }
                }

                // Query WMI root\wmi namespace for SMART Failure Predict status
                var smartQuery = new ManagementObjectSearcher(@"Root\WMI", "SELECT InstanceName, PredictFailure, Reason FROM MSStorageDriver_FailurePredictStatus");
                foreach (ManagementObject smart in smartQuery.Get())
                {
                    string instanceName = smart["InstanceName"]?.ToString();
                    bool predictFailure = (bool)(smart["PredictFailure"] ?? false);
                    int reason = Convert.ToInt32(smart["Reason"] ?? 0);

                    // Match instanceName containing serial number or partial device path
                    foreach (var pair in tempResults)
                    {
                        if (instanceName != null && instanceName.Contains(pair.Value.SerialNumber ?? "###"))
                        {
                            pair.Value.PredictFailure = predictFailure;
                            pair.Value.Reason = reason;
                        }
                    }
                }

                // Query SMART temperature if available
                var tempQuery = new ManagementObjectSearcher(@"Root\WMI", "SELECT InstanceName, CurrentTemperature FROM MSStorageDriver_FailurePredictData");
                foreach (ManagementObject temp in tempQuery.Get())
                {
                    string instanceName = temp["InstanceName"]?.ToString();
                    // Temperature is in Kelvin tenths (0.1K) or Celsius. Usually 0.1K.
                    double curTemp = Convert.ToDouble(temp["CurrentTemperature"] ?? 0);
                    if (curTemp > 0)
                    {
                        double celsius = (curTemp - 2731.5) / 10.0; // Kelvin check
                        if (celsius < 0 || celsius > 100)
                        {
                            celsius = curTemp; // Fallback directly if it's already Celsius
                        }

                        foreach (var pair in tempResults)
                        {
                            if (instanceName != null && instanceName.Contains(pair.Value.SerialNumber ?? "###"))
                            {
                                pair.Value.Temperature = celsius;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SMART Scan failed: {ex.Message}");
            }
            return results;
        }

        /// <summary>
        /// Performs a read-only scan using chkdsk.exe.
        /// </summary>
        public static string ScanDriveWithChkdsk(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return "Invalid drive letter.";
            
            string drive = driveLetter.Trim().Substring(0, 1) + ":";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "chkdsk.exe",
                    Arguments = drive,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return "Failed to execute chkdsk.";
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
            catch (Exception ex)
            {
                return $"Error running CHKDSK: {ex.Message}";
            }
        }

        /// <summary>
        /// Schedules a repair-on-reboot in the registry if chkdsk errors are found.
        /// </summary>
        public static bool ScheduleChkdskRepair(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return false;
            string letter = driveLetter.Trim().Substring(0, 1).ToUpper();

            try
            {
                const string regPath = @"SYSTEM\CurrentControlSet\Control\Session Manager";
                using var key = Registry.LocalMachine.OpenSubKey(regPath, writable: true);
                if (key == null) return false;

                string[] bootExecute = (string[])key.GetValue("BootExecute");
                var list = new List<string>(bootExecute ?? Array.Empty<string>());

                string chkdskCmd = $"autocheck autochk /r \\??\\{letter}:";
                if (!list.Contains(chkdskCmd))
                {
                    list.Add(chkdskCmd);
                    key.SetValue("BootExecute", list.ToArray(), RegistryValueKind.MultiString);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to schedule boot CHKDSK: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Runs Windows File Integrity Checkers (SFC & DISM).
        /// </summary>
        public static SystemFileCheckResult PerformOSIntegrityCheck()
        {
            var result = new SystemFileCheckResult { SfcClean = true, DismClean = true };
            var output = new StringBuilder();

            try
            {
                // 1. Run DISM scanhealth
                output.AppendLine("--- DISM ScanHealth ---");
                var dismPsi = new ProcessStartInfo
                {
                    FileName = "dism.exe",
                    Arguments = "/Online /Cleanup-Image /CheckHealth",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var dismProc = Process.Start(dismPsi))
                {
                    if (dismProc != null)
                    {
                        string dismOut = dismProc.StandardOutput.ReadToEnd();
                        dismProc.WaitForExit();
                        output.AppendLine(dismOut);
                        if (dismOut.Contains("corruption") || dismProc.ExitCode != 0)
                        {
                            result.DismClean = false;
                        }
                    }
                }

                // 2. Run SFC verifyonly
                output.AppendLine("--- SFC VerifyOnly ---");
                var sfcPsi = new ProcessStartInfo
                {
                    FileName = "sfc.exe",
                    Arguments = "/verifyonly",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var sfcProc = Process.Start(sfcPsi))
                {
                    if (sfcProc != null)
                    {
                        string sfcOut = sfcProc.StandardOutput.ReadToEnd();
                        sfcProc.WaitForExit();
                        output.AppendLine(sfcOut);
                        if (sfcOut.Contains("corrupt") || sfcProc.ExitCode != 0)
                        {
                            result.SfcClean = false;
                        }
                    }
                }

                result.OutputSummary = output.ToString();
            }
            catch (Exception ex)
            {
                result.OutputSummary = $"Integrity check failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Runs SSD TRIM or HDD Defragmentation depending on drive media type.
        /// </summary>
        public static string OptimizeVolume(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return "Invalid drive letter.";
            string drive = driveLetter.Trim().Substring(0, 1) + ":";

            try
            {
                bool isSsd = false;
                // Query disk type
                var searcher = new ManagementObjectSearcher(@"Root\Microsoft\Windows\Storage", "SELECT MediaType FROM MSFT_PhysicalDisk");
                foreach (ManagementObject disk in searcher.Get())
                {
                    ushort mediaType = Convert.ToUInt16(disk["MediaType"] ?? 0);
                    if (mediaType == 4) // SSD
                    {
                        isSsd = true;
                        break;
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "defrag.exe",
                    Arguments = isSsd ? $"{drive} /O" : $"{drive} /D",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return "Failed to start optimizer process.";
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
            catch (Exception ex)
            {
                return $"Optimization error: {ex.Message}";
            }
        }

        /// <summary>
        /// Retrieves the cluster bitmap for a drive using Win32 API.
        /// Useful for building a visual defrag/cluster heatmap.
        /// </summary>
        public static List<DiskClusterState> GetVolumeClusterBitmap(string driveLetter)
        {
            var clusterList = new List<DiskClusterState>();
            if (string.IsNullOrWhiteSpace(driveLetter)) return clusterList;

            string volumePath = $@"\\.\{driveLetter.Trim().Substring(0, 1)}:";
            using var volumeHandle = CreateFile(
                volumePath,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (volumeHandle.IsInvalid)
            {
                Console.WriteLine($"Failed to open volume handle. Win32 Error: {Marshal.GetLastWin32Error()}");
                return clusterList;
            }

            var inputBuffer = new STARTING_LCN_INPUT_BUFFER { StartingLcn = 0 };
            byte[] outBuffer = new byte[8192]; // Fetch chunk of bitmap (representing ~65k clusters)
            
            bool success = DeviceIoControl(
                volumeHandle,
                FSCTL_GET_VOLUME_BITMAP,
                ref inputBuffer,
                Marshal.SizeOf(typeof(STARTING_LCN_INPUT_BUFFER)),
                outBuffer,
                outBuffer.Length,
                out var bytesReturned,
                IntPtr.Zero);

            if (success || Marshal.GetLastWin32Error() == 234) // 234 = ERROR_MORE_DATA
            {
                long startingLcn = BitConverter.ToInt64(outBuffer, 0);
                long bitmapSize = BitConverter.ToInt64(outBuffer, 8);

                // Parse the bitmap bytes starting at offset 16
                int byteOffset = 16;
                int maxBytes = Math.Min(bytesReturned - 16, outBuffer.Length - 16);

                for (int i = 0; i < maxBytes; i++)
                {
                    byte b = outBuffer[byteOffset + i];
                    for (int bit = 0; bit < 8; bit++)
                    {
                        clusterList.Add(new DiskClusterState
                        {
                            ClusterIndex = startingLcn + (i * 8) + bit,
                            IsAllocated = (b & (1 << bit)) != 0
                        });
                    }
                }
            }

            return clusterList;
        }

        /// <summary>
        /// Duplicate finder: Scans directory, MD5 checks files, moves duplicates to quarantine.
        /// </summary>
        public static List<string> FindAndQuarantineDuplicates(string sourceDirectory, string quarantineDirectory)
        {
            var quarantinedFiles = new List<string>();
            var fileHashes = new Dictionary<string, string>(); // MD5 -> FilePath

            if (!Directory.Exists(sourceDirectory)) return quarantinedFiles;
            if (!Directory.Exists(quarantineDirectory))
            {
                Directory.CreateDirectory(quarantineDirectory);
            }

            var files = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories);
            using var md5 = MD5.Create();

            foreach (var file in files)
            {
                if (!WhitelistGuard.IsPathModificationAllowed(file)) continue;

                try
                {
                    using var stream = File.OpenRead(file);
                    byte[] hashBytes = md5.ComputeHash(stream);
                    string hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                    if (fileHashes.TryGetValue(hash, out string originalPath))
                    {
                        // Duplicate found! Move this file to quarantine
                        string fileName = Path.GetFileName(file);
                        string uniqueName = $"{Guid.NewGuid()}_{fileName}";
                        string destPath = Path.Combine(quarantineDirectory, uniqueName);

                        // File operation audit is handled by the Safety module before calling this,
                        // but we perform safe checks here.
                        File.Move(file, destPath);

                        // Log reversion command in safety context
                        AuditEngine.RegisterProposedChange(
                            "FileDelete",
                            file,         // Original path
                            fileName,
                            destPath,     // Current quarantine path (so rollback can copy it back)
                            "Deleted"
                        );

                        quarantinedFiles.Add(file);
                    }
                    else
                    {
                        fileHashes[hash] = file;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error scanning file {file}: {ex.Message}");
                }
            }

            if (quarantinedFiles.Count > 0)
            {
                AuditEngine.CommitChange("DiskHealth", $"Quarantined {quarantinedFiles.Count} duplicate files.", AuditEngine.GetProposedChanges());
                AuditEngine.ClearProposedChanges();
            }

            return quarantinedFiles;
        }
    }
}
