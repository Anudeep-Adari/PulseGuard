using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace PulseGuard.Data
{
    public static class DatabaseManager
    {
        private static readonly string DbDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PulseGuard"
        );
        private static readonly string DbPath = Path.Combine(DbDirectory, "pulseguard.db");
        private static readonly string ConnectionString = $"Data Source={DbPath}";

        /// <summary>
        /// Initializes the local database, creates schema, and seeds default profiles.
        /// </summary>
        public static void Initialize()
        {
            if (!Directory.Exists(DbDirectory))
            {
                Directory.CreateDirectory(DbDirectory);
            }

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            // Create Tables
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS GameProfiles (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ProcessName TEXT NOT NULL UNIQUE,
                        DisplayName TEXT NOT NULL,
                        CpuPriority INTEGER DEFAULT 3,
                        CpuAffinityMask INTEGER DEFAULT -1,
                        DisableFullscreenOptimizations INTEGER DEFAULT 1,
                        DisableNagleAlgorithm INTEGER DEFAULT 1,
                        QosPriorityLevel INTEGER DEFAULT 1
                    );

                    CREATE TABLE IF NOT EXISTS AuditLogs (
                        Id TEXT PRIMARY KEY,
                        Timestamp TEXT NOT NULL,
                        Module TEXT NOT NULL,
                        Description TEXT NOT NULL,
                        PreStateJson TEXT NOT NULL,
                        PostStateJson TEXT NOT NULL,
                        IsRolledBack INTEGER DEFAULT 0
                    );

                    CREATE TABLE IF NOT EXISTS HealthScoreHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp TEXT NOT NULL,
                        Score INTEGER NOT NULL,
                        DiskStatus TEXT,
                        PerformanceStatus TEXT,
                        CleanupSizeMb REAL
                    );

                    CREATE TABLE IF NOT EXISTS BiosChecklist (
                        ItemNumber INTEGER PRIMARY KEY,
                        Category TEXT NOT NULL,
                        FeatureName TEXT NOT NULL,
                        HowTo TEXT NOT NULL,
                        Risk TEXT NOT NULL,
                        IsCompleted INTEGER DEFAULT 0,
                        CompletionDate TEXT,
                        UserNotes TEXT
                    );

                    CREATE TABLE IF NOT EXISTS WindowsTweaks (
                        ItemNumber INTEGER PRIMARY KEY,
                        Category TEXT NOT NULL,
                        FeatureName TEXT NOT NULL,
                        Status TEXT DEFAULT 'Not Applied',
                        RegistryPath TEXT,
                        ValueName TEXT,
                        ValueType TEXT,
                        OriginalValue TEXT,
                        OptimizedValue TEXT
                    );
                ";
                command.ExecuteNonQuery();
            }

            // Seed popular games if table is empty
            SeedDefaultProfiles(connection);

            // Seed advanced tuning datasets
            DatabaseSeeder.SeedBiosChecklist(connection);
            DatabaseSeeder.SeedWindowsTweaks(connection);
        }

        private static void SeedDefaultProfiles(SqliteConnection connection)
        {
            using var countCmd = connection.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM GameProfiles";
            long count = (long)countCmd.ExecuteScalar();

            if (count > 0) return;

            var defaults = new List<GameProfile>
            {
                new GameProfile { ProcessName = "cs2.exe", DisplayName = "Counter-Strike 2", CpuPriority = 3, CpuAffinityMask = -1, DisableFullscreenOptimizations = true, DisableNagleAlgorithm = true, QosPriorityLevel = 1 },
                new GameProfile { ProcessName = "VALORANT-Win64-Shipping.exe", DisplayName = "VALORANT", CpuPriority = 3, CpuAffinityMask = -1, DisableFullscreenOptimizations = true, DisableNagleAlgorithm = true, QosPriorityLevel = 1 },
                new GameProfile { ProcessName = "League of Legends.exe", DisplayName = "League of Legends", CpuPriority = 3, CpuAffinityMask = -1, DisableFullscreenOptimizations = true, DisableNagleAlgorithm = true, QosPriorityLevel = 1 },
                new GameProfile { ProcessName = "FortniteClient-Win64-Shipping.exe", DisplayName = "Fortnite", CpuPriority = 3, CpuAffinityMask = -1, DisableFullscreenOptimizations = true, DisableNagleAlgorithm = true, QosPriorityLevel = 1 },
                new GameProfile { ProcessName = "r5apex.exe", DisplayName = "Apex Legends", CpuPriority = 3, CpuAffinityMask = -1, DisableFullscreenOptimizations = true, DisableNagleAlgorithm = true, QosPriorityLevel = 1 },
                new GameProfile { ProcessName = "Overwatch.exe", DisplayName = "Overwatch 2", CpuPriority = 3, CpuAffinityMask = -1, DisableFullscreenOptimizations = true, DisableNagleAlgorithm = true, QosPriorityLevel = 1 }
            };

            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var game in defaults)
                {
                    using var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = @"
                        INSERT INTO GameProfiles (ProcessName, DisplayName, CpuPriority, CpuAffinityMask, DisableFullscreenOptimizations, DisableNagleAlgorithm, QosPriorityLevel)
                        VALUES ($proc, $disp, $prio, $aff, $dfso, $dnagle, $qos);
                    ";
                    insertCmd.Parameters.AddWithValue("$proc", game.ProcessName);
                    insertCmd.Parameters.AddWithValue("$disp", game.DisplayName);
                    insertCmd.Parameters.AddWithValue("$prio", game.CpuPriority);
                    insertCmd.Parameters.AddWithValue("$aff", game.CpuAffinityMask);
                    insertCmd.Parameters.AddWithValue("$dfso", game.DisableFullscreenOptimizations ? 1 : 0);
                    insertCmd.Parameters.AddWithValue("$dnagle", game.DisableNagleAlgorithm ? 1 : 0);
                    insertCmd.Parameters.AddWithValue("$qos", game.QosPriorityLevel);

                    insertCmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // --- Audit Log Helpers ---

        public static void InsertAuditLog(AuditLogEntry entry)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AuditLogs (Id, Timestamp, Module, Description, PreStateJson, PostStateJson, IsRolledBack)
                VALUES ($id, $time, $mod, $desc, $pre, $post, $roll);
            ";
            cmd.Parameters.AddWithValue("$id", entry.Id);
            cmd.Parameters.AddWithValue("$time", entry.Timestamp);
            cmd.Parameters.AddWithValue("$mod", entry.Module);
            cmd.Parameters.AddWithValue("$desc", entry.Description);
            cmd.Parameters.AddWithValue("$pre", entry.PreStateJson);
            cmd.Parameters.AddWithValue("$post", entry.PostStateJson);
            cmd.Parameters.AddWithValue("$roll", entry.IsRolledBack ? 1 : 0);

            cmd.ExecuteNonQuery();
        }

        public static List<AuditLogEntry> GetAuditLogs()
        {
            var list = new List<AuditLogEntry>();
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Timestamp, Module, Description, PreStateJson, PostStateJson, IsRolledBack FROM AuditLogs ORDER BY Timestamp DESC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AuditLogEntry
                {
                    Id = reader.GetString(0),
                    Timestamp = reader.GetString(1),
                    Module = reader.GetString(2),
                    Description = reader.GetString(3),
                    PreStateJson = reader.GetString(4),
                    PostStateJson = reader.GetString(5),
                    IsRolledBack = reader.GetInt32(6) == 1
                });
            }
            return list;
        }

        public static void MarkAuditLogAsRolledBack(string id)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE AuditLogs SET IsRolledBack = 1 WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        // --- Game Profile Helpers ---

        public static List<GameProfile> GetGameProfiles()
        {
            var list = new List<GameProfile>();
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, ProcessName, DisplayName, CpuPriority, CpuAffinityMask, DisableFullscreenOptimizations, DisableNagleAlgorithm, QosPriorityLevel FROM GameProfiles";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new GameProfile
                {
                    Id = reader.GetInt32(0),
                    ProcessName = reader.GetString(1),
                    DisplayName = reader.GetString(2),
                    CpuPriority = reader.GetInt32(3),
                    CpuAffinityMask = reader.GetInt32(4),
                    DisableFullscreenOptimizations = reader.GetInt32(5) == 1,
                    DisableNagleAlgorithm = reader.GetInt32(6) == 1,
                    QosPriorityLevel = reader.GetInt32(7)
                });
            }
            return list;
        }

        public static void UpsertGameProfile(GameProfile profile)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO GameProfiles (ProcessName, DisplayName, CpuPriority, CpuAffinityMask, DisableFullscreenOptimizations, DisableNagleAlgorithm, QosPriorityLevel)
                VALUES ($proc, $disp, $prio, $aff, $dfso, $dnagle, $qos)
                ON CONFLICT(ProcessName) DO UPDATE SET
                    DisplayName = excluded.DisplayName,
                    CpuPriority = excluded.CpuPriority,
                    CpuAffinityMask = excluded.CpuAffinityMask,
                    DisableFullscreenOptimizations = excluded.DisableFullscreenOptimizations,
                    DisableNagleAlgorithm = excluded.DisableNagleAlgorithm,
                    QosPriorityLevel = excluded.QosPriorityLevel;
            ";
            cmd.Parameters.AddWithValue("$proc", profile.ProcessName);
            cmd.Parameters.AddWithValue("$disp", profile.DisplayName);
            cmd.Parameters.AddWithValue("$prio", profile.CpuPriority);
            cmd.Parameters.AddWithValue("$aff", profile.CpuAffinityMask);
            cmd.Parameters.AddWithValue("$dfso", profile.DisableFullscreenOptimizations ? 1 : 0);
            cmd.Parameters.AddWithValue("$dnagle", profile.DisableNagleAlgorithm ? 1 : 0);
            cmd.Parameters.AddWithValue("$qos", profile.QosPriorityLevel);

            cmd.ExecuteNonQuery();
        }

        // --- Health Score Helpers ---

        public static void InsertHealthScore(HealthScoreEntry entry)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO HealthScoreHistory (Timestamp, Score, DiskStatus, PerformanceStatus, CleanupSizeMb)
                VALUES ($time, $score, $disk, $perf, $clean);
            ";
            cmd.Parameters.AddWithValue("$time", entry.Timestamp);
            cmd.Parameters.AddWithValue("$score", entry.Score);
            cmd.Parameters.AddWithValue("$disk", entry.DiskStatus);
            cmd.Parameters.AddWithValue("$perf", entry.PerformanceStatus);
            cmd.Parameters.AddWithValue("$clean", entry.CleanupSizeMb);

            cmd.ExecuteNonQuery();
        }

        public static List<HealthScoreEntry> GetHealthScoreHistory()
        {
            var list = new List<HealthScoreEntry>();
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Timestamp, Score, DiskStatus, PerformanceStatus, CleanupSizeMb FROM HealthScoreHistory ORDER BY Timestamp DESC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new HealthScoreEntry
                {
                    Id = reader.GetInt32(0),
                    Timestamp = reader.GetString(1),
                    Score = reader.GetInt32(2),
                    DiskStatus = reader.IsDBNull(3) ? null : reader.GetString(3),
                    PerformanceStatus = reader.IsDBNull(4) ? null : reader.GetString(4),
                    CleanupSizeMb = reader.IsDBNull(5) ? 0 : reader.GetDouble(5)
                });
            }
            return list;
        }

        // --- BIOS & Windows Advanced Tuning Helpers ---

        public static List<BiosChecklistItem> GetBiosChecklist()
        {
            var list = new List<BiosChecklistItem>();
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ItemNumber, Category, FeatureName, HowTo, Risk, IsCompleted, CompletionDate, UserNotes FROM BiosChecklist ORDER BY ItemNumber ASC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BiosChecklistItem
                {
                    ItemNumber = reader.GetInt32(0),
                    Category = reader.GetString(1),
                    FeatureName = reader.GetString(2),
                    HowTo = reader.GetString(3),
                    Risk = reader.GetString(4),
                    IsCompleted = reader.GetInt32(5) == 1,
                    CompletionDate = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    UserNotes = reader.IsDBNull(7) ? "" : reader.GetString(7)
                });
            }
            return list;
        }

        public static void UpdateBiosChecklistItem(int itemNumber, bool isCompleted, string notes)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE BiosChecklist
                SET IsCompleted = $comp,
                    CompletionDate = $date,
                    UserNotes = $notes
                WHERE ItemNumber = $num;
            ";
            cmd.Parameters.AddWithValue("$comp", isCompleted ? 1 : 0);
            cmd.Parameters.AddWithValue("$date", isCompleted ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : "");
            cmd.Parameters.AddWithValue("$notes", notes ?? "");
            cmd.Parameters.AddWithValue("$num", itemNumber);

            cmd.ExecuteNonQuery();
        }

        public static List<WindowsTweakItem> GetWindowsTweaks()
        {
            var list = new List<WindowsTweakItem>();
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ItemNumber, Category, FeatureName, Status, RegistryPath, ValueName, ValueType, OriginalValue, OptimizedValue FROM WindowsTweaks ORDER BY ItemNumber ASC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new WindowsTweakItem
                {
                    ItemNumber = reader.GetInt32(0),
                    Category = reader.GetString(1),
                    FeatureName = reader.GetString(2),
                    Status = reader.GetString(3),
                    RegistryPath = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    ValueName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    ValueType = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    OriginalValue = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    OptimizedValue = reader.IsDBNull(8) ? "" : reader.GetString(8)
                });
            }
            return list;
        }

        public static void UpdateWindowsTweakStatus(int itemNumber, string status, string originalValue)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE WindowsTweaks
                SET Status = $status,
                    OriginalValue = $orig
                WHERE ItemNumber = $num;
            ";
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$orig", originalValue ?? "");
            cmd.Parameters.AddWithValue("$num", itemNumber);

            cmd.ExecuteNonQuery();
        }
    }
}
