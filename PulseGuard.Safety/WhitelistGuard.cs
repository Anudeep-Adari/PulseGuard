using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PulseGuard.Safety
{
    public static class WhitelistGuard
    {
        // Critical system services that should never be stopped, disabled, or modified
        private static readonly HashSet<string> ProtectedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Winmgmt",     // Windows Management Instrumentation
            "EventLog",    // Windows Event Log
            "CryptSvc",    // Cryptographic Services
            "RpcSs",       // Remote Procedure Call
            "DcomLaunch",  // DCOM Server Process Launcher
            "gpsvc",       // Group Policy Client
            "ProfSvc",     // User Profile Service
            "MpsSvc",      // Windows Defender Firewall
            "WinDefend",   // Microsoft Defender Antivirus Service
            "SamSs",       // Security Accounts Manager
            "LSM",         // Local Session Manager
            "RpcEptMapper" // RPC Endpoint Mapper
        };

        // Critical directory fragments that must not be deleted or modified
        private static readonly List<string> ProtectedDirectoryPrefixes = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };

        /// <summary>
        /// Validates if a Windows service is safe to modify.
        /// </summary>
        public static bool IsServiceModificationAllowed(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                return false;

            return !ProtectedServices.Contains(serviceName.Trim());
        }

        /// <summary>
        /// Validates if a file or directory path is safe to modify or delete.
        /// </summary>
        public static bool IsPathModificationAllowed(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                string fullPath = Path.GetFullPath(path);

                // Check if path resides directly inside protected system folders
                foreach (var prefix in ProtectedDirectoryPrefixes)
                {
                    if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        // Allow temp directories if they are explicitly nested inside Windows
                        if (fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        return false;
                    }
                }

                // Check if the path targets the root of critical system drives
                var root = Path.GetPathRoot(fullPath);
                if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false; // Safest default on parsing failure
            }
        }
    }
}
