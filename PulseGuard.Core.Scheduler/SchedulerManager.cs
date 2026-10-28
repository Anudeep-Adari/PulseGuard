using System;
using System.Diagnostics;
using Microsoft.Win32.TaskScheduler;

namespace PulseGuard.Core.Scheduler
{
    public static class SchedulerManager
    {
        private const string WeeklyTaskName = "PulseGuard_WeeklyScan";
        private const string IdleTaskName = "PulseGuard_IdleCleanup";

        /// <summary>
        /// Registers a weekly scheduled task targeting the main PulseGuard worker executable.
        /// </summary>
        public static bool RegisterWeeklyScanTask(string appPath)
        {
            try
            {
                using var ts = new TaskService();
                var td = ts.NewTask();
                td.RegistrationInfo.Description = "Performs weekly system health scans with PulseGuard.";

                // Trigger weekly on Sunday at 3 AM
                var trigger = new WeeklyTrigger
                {
                    StartBoundary = DateTime.Today.AddHours(3),
                    DaysOfWeek = DaysOfTheWeek.Sunday
                };
                td.Triggers.Add(trigger);

                // Action to launch the app with arguments
                td.Actions.Add(new ExecAction(appPath, "--scheduled-scan", null));

                // Settings: run with highest privileges
                td.Principal.RunLevel = TaskRunLevel.Highest;
                td.Settings.DisallowStartIfOnBatteries = true;
                td.Settings.StopIfGoingOnBatteries = true;

                ts.RootFolder.RegisterTaskDefinition(WeeklyTaskName, td);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to register weekly task: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Registers an idle-time task that triggers when the system goes idle.
        /// </summary>
        public static bool RegisterIdleCleanupTask(string appPath)
        {
            try
            {
                using var ts = new TaskService();
                var td = ts.NewTask();
                td.RegistrationInfo.Description = "Applies automatic system cleanup when system is idle.";

                // Trigger when idle
                td.Triggers.Add(new IdleTrigger());

                // Action to run cleanup
                td.Actions.Add(new ExecAction(appPath, "--idle-cleanup", null));

                // Settings: require idle for 15 minutes
                td.Principal.RunLevel = TaskRunLevel.Highest;
                td.Settings.RunOnlyIfIdle = true;
                td.Settings.IdleSettings.IdleDuration = TimeSpan.FromMinutes(15);
                td.Settings.IdleSettings.WaitTimeout = TimeSpan.FromHours(2);
                td.Settings.IdleSettings.StopOnIdleEnd = true;
                td.Settings.DisallowStartIfOnBatteries = true;

                ts.RootFolder.RegisterTaskDefinition(IdleTaskName, td);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to register idle task: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Defers scheduled Antivirus scans to late night to prevent background lag spikes while gaming.
        /// Does NOT disable real-time protection.
        /// </summary>
        public static bool DeferAntivirusScans(bool defer)
        {
            try
            {
                // Defer scheduled scan to 4 AM using Windows Defender Preferences PowerShell Command
                string hour = defer ? "04:00:00" : "02:00:00";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"Set-MpPreference -ScanScheduleTime '{hour}'\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                process?.WaitForExit();
                return process?.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to defer AV scans: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Dispatches a native Windows shell or notification toast.
        /// </summary>
        public static void DispatchToast(string title, string message)
        {
            try
            {
                // Send standard Windows toast notification using powershell helper to keep binary footprint small
                string escTitle = title.Replace("\"", "\\\"");
                string escMessage = message.Replace("\"", "\\\"");
                string command = $"[void] [System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms'); [System.Windows.Forms.ToolTipIcon]::Info; " +
                                 $"$t = New-Object System.Windows.Forms.NotifyIcon; $t.Icon = [System.Drawing.SystemIcons]::Information; $t.Visible = $true; " +
                                 $"$t.ShowBalloonTip(5000, '{escTitle}', '{escMessage}', [System.Windows.Forms.ToolTipIcon]::Info); Start-Sleep -s 5; $t.Dispose()";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{command}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Toast notification dispatch failed: {ex.Message}");
            }
        }
    }
}
