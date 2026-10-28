using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using PulseGuard.Data;

namespace PulseGuard.Safety
{
    public class SystemChangeDescription
    {
        public string ActionType { get; set; } // "RegistryWrite", "ServiceStop", "FileDelete"
        public string TargetPath { get; set; }  // Key path, service name, or file path
        public string ValueName { get; set; }   // For registry
        public string PreValue { get; set; }    // Original value
        public string PostValue { get; set; }   // New value
    }

    public static class AuditEngine
    {
        private static readonly List<SystemChangeDescription> CurrentSessionChanges = new List<SystemChangeDescription>();

        /// <summary>
        /// Registers a pending change for user preview (Dry-Run mode).
        /// </summary>
        public static void RegisterProposedChange(string actionType, string targetPath, string valueName, string preValue, string postValue)
        {
            CurrentSessionChanges.Add(new SystemChangeDescription
            {
                ActionType = actionType,
                TargetPath = targetPath,
                ValueName = valueName,
                PreValue = preValue,
                PostValue = postValue
            });
        }

        public static List<SystemChangeDescription> GetProposedChanges()
        {
            return new List<SystemChangeDescription>(CurrentSessionChanges);
        }

        public static void ClearProposedChanges()
        {
            CurrentSessionChanges.Clear();
        }

        /// <summary>
        /// Logs a change to the SQLite database.
        /// </summary>
        public static void CommitChange(string module, string description, List<SystemChangeDescription> changes)
        {
            var auditEntry = new AuditLogEntry
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow.ToString("o"),
                Module = module,
                Description = description,
                PreStateJson = JsonSerializer.Serialize(changes),
                PostStateJson = "", // Can be extended if needed
                IsRolledBack = false
            };

            DatabaseManager.InsertAuditLog(auditEntry);
        }

        /// <summary>
        /// Rolls back a specific audit log entry by reverting all registered changes.
        /// </summary>
        public static bool RollbackEntry(string auditLogId)
        {
            try
            {
                var logs = DatabaseManager.GetAuditLogs();
                var target = logs.Find(l => l.Id == auditLogId);

                if (target == null || target.IsRolledBack)
                    return false;

                var changes = JsonSerializer.Deserialize<List<SystemChangeDescription>>(target.PreStateJson);
                if (changes == null) return false;

                // Process changes in reverse order (LIFO) for rollback safety
                for (int i = changes.Count - 1; i >= 0; i--)
                {
                    var change = changes[i];
                    bool success = RevertChange(change);
                    if (!success)
                    {
                        Console.WriteLine($"Warning: Failed to revert change: {change.ActionType} - {change.TargetPath}");
                    }
                }

                DatabaseManager.MarkAuditLogAsRolledBack(auditLogId);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rolling back entry {auditLogId}: {ex.Message}");
                return false;
            }
        }

        private static bool RevertChange(SystemChangeDescription change)
        {
            switch (change.ActionType)
            {
                case "RegistryWrite":
                    return RevertRegistryWrite(change);
                case "ServiceStop":
                    return RevertServiceStop(change);
                case "FileDelete":
                    return RevertFileDelete(change);
                default:
                    Console.WriteLine($"Unknown action type for rollback: {change.ActionType}");
                    return false;
            }
        }

        private static bool RevertRegistryWrite(SystemChangeDescription change)
        {
            try
            {
                // Parse registry hive
                string hiveStr = change.TargetPath.Split('\\')[0];
                string subKeyPath = change.TargetPath.Substring(hiveStr.Length + 1);

                RegistryKey rootKey = hiveStr.ToUpper() switch
                {
                    "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                    "HKEY_CURRENT_USER" => Registry.CurrentUser,
                    _ => throw new ArgumentException($"Unsupported hive: {hiveStr}")
                };

                using var key = rootKey.OpenSubKey(subKeyPath, writable: true);
                if (key == null) return false;

                if (change.PreValue == null)
                {
                    // If it was null, delete the value
                    key.DeleteValue(change.ValueName, throwOnMissingValue: false);
                }
                else
                {
                    // Write the previous value back
                    // Note: Registry types are simplified here to string or int.
                    if (int.TryParse(change.PreValue, out int intVal))
                    {
                        key.SetValue(change.ValueName, intVal, RegistryValueKind.DWord);
                    }
                    else
                    {
                        key.SetValue(change.ValueName, change.PreValue, RegistryValueKind.String);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Registry rollback failed: {ex.Message}");
                return false;
            }
        }

        private static bool RevertServiceStop(SystemChangeDescription change)
        {
            try
            {
                // To revert a stop, we start the service again if it was originally running
                if (change.PreValue == "Running")
                {
                    using var sc = new System.ServiceProcess.ServiceController(change.TargetPath);
                    if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped)
                    {
                        sc.Start();
                        sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Service start rollback failed: {ex.Message}");
                return false;
            }
        }

        private static bool RevertFileDelete(SystemChangeDescription change)
        {
            try
            {
                // If a file was deleted and quarantined, copy it back from the quarantine folder
                string quarantinePath = change.PreValue; // We store quarantine path in PreValue
                if (File.Exists(quarantinePath))
                {
                    string targetDir = Path.GetDirectoryName(change.TargetPath);
                    if (targetDir != null && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(quarantinePath, change.TargetPath, overwrite: true);
                    File.Delete(quarantinePath); // Clean up quarantine file
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"File restore rollback failed: {ex.Message}");
                return false;
            }
        }
    }
}
