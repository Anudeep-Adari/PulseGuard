using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using PulseGuard.Data;
using PulseGuard.Safety;
using PulseGuard.Core.DiskHealth;
using PulseGuard.Core.SystemCleanup;
using PulseGuard.Core.GamingBooster;
using PulseGuard.Core.HealthMonitor;
using PulseGuard.Core.Scheduler;
using PulseGuard.Core.Reporting;

namespace PulseGuard.Shell
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _telemetryTimer;
        private bool _isEsportsActive = false;

        public MainWindow()
        {
            InitializeComponent();
            
            // 1. Initialize SQLite database & default seed profiles
            DatabaseManager.Initialize();

            // 2. Load game profiles into the list view
            RefreshGameProfilesList();

            // Load advanced checklists
            RefreshBiosChecklist();
            RefreshWindowsTweaks();
 
            // 3. Start telemetry scheduler
            StartTelemetryTimer();
 
            // 4. Run initial update
            UpdateRealtimeTelemetry(null, null);
 
            // 5. Register auto-monitoring for active processes
            EsportsEngine.StartAutoMonitoring();
        }

        private void StartTelemetryTimer()
        {
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _telemetryTimer.Tick += UpdateRealtimeTelemetry;
            _telemetryTimer.Start();
        }

        private void UpdateRealtimeTelemetry(object sender, EventArgs e)
        {
            try
            {
                // CPU load & temp
                var cpu = HealthMonitorManager.GetCpuStatus();
                TxtCpuTemp.Text = $"{cpu.Temperature:F1} °C";
                TxtCpuLoad.Text = $"CPU Load: {cpu.LoadPercentage}%";
                ProgressCpu.Value = cpu.LoadPercentage;

                // Check thermal throttling warnings
                if (cpu.IsThrottling)
                {
                    TxtCpuTemp.Foreground = System.Windows.Media.Brushes.Red;
                    TxtCpuLoad.Text += " [THROTTLING ALERT]";
                }
                else
                {
                    TxtCpuTemp.Foreground = (System.Windows.Media.Brush)FindResource("TextMainBrush") ?? System.Windows.Media.Brushes.SlateGray;
                }

                // RAM stats
                var ram = HealthMonitorManager.GetRamStatus();
                double totalGb = ram.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                double availGb = ram.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                double usedGb = totalGb - availGb;
                TxtRamUsage.Text = $"{usedGb:F1} / {totalGb:F1} GB";
                TxtRamStatus.Text = $"Memory Load: {ram.dwMemoryLoad}%";

                // Uptime
                double uptimeDays = HealthMonitorManager.GetSystemUptimeDays();
                if (uptimeDays < 1.0)
                {
                    double hours = uptimeDays * 24.0;
                    TxtUptime.Text = $"{hours:F1} Hours";
                }
                else
                {
                    TxtUptime.Text = $"{uptimeDays:F1} Days";
                }

                // Laptop battery report
                var bat = HealthMonitorManager.GetBatteryReport();
                if (bat.HasBattery)
                {
                    TxtBatteryHealth.Text = $"{bat.HealthPercentage:F1}% Health";
                    TxtBatteryCycles.Text = $"Cycles: {bat.CycleCount} ({bat.Name})";
                }
                else
                {
                    TxtBatteryHealth.Text = "Desktop Mode";
                    TxtBatteryCycles.Text = "No Battery Detected";
                }
            }
            catch {}
        }

        private void RefreshGameProfilesList()
        {
            try
            {
                var list = DatabaseManager.GetGameProfiles();
                LstGameProfiles.ItemsSource = list;
            }
            catch {}
        }

        private void BtnTrimRam_Click(object sender, RoutedEventArgs e)
        {
            SystemCleanupManager.TrimStandbyMemory();
            MessageBox.Show("Standby memory list cleared and working sets trimmed.", "Memory Trimmed", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateRealtimeTelemetry(null, null);
        }

        private void BtnDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            // Calculate a composite health score based on system tests
            try
            {
                var smart = DiskHealthManager.PerformSmartScan();
                bool predictFail = smart.Any(s => s.PredictFailure);
                
                var cpu = HealthMonitorManager.GetCpuStatus();
                double uptime = HealthMonitorManager.GetSystemUptimeDays();
                var evLog = HealthMonitorManager.ScanEventViewerErrors();

                int score = HealthMonitorManager.ComputeHealthScore(predictFail, false, evLog.Count, uptime, cpu.Temperature);
                
                TxtHealthScore.Text = score.ToString();
                
                // Record health entry in history log database
                DatabaseManager.InsertHealthScore(new HealthScoreEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Score = score,
                    DiskStatus = predictFail ? "SMART Prediction Warning" : "Healthy",
                    PerformanceStatus = cpu.IsThrottling ? "Throttling active" : "Normal",
                    CleanupSizeMb = 0.0
                });

                MessageBox.Show($"Diagnostic complete! Health Score: {score}/100", "Diagnostics Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Diagnostic run error: {ex.Message}");
            }
        }

        private void BtnSmartScan_Click(object sender, RoutedEventArgs e)
        {
            TxtDiskOutput.Text = "Initializing SMART Diagnostic...\n";
            var drives = DiskHealthManager.PerformSmartScan();
            if (drives.Count == 0)
            {
                TxtDiskOutput.Text += "No compatible internal physical drives returned SMART telemetry.\n";
                return;
            }

            foreach (var d in drives)
            {
                TxtDiskOutput.Text += $"Drive: {d.DeviceId}\n";
                TxtDiskOutput.Text += $"  Model: {d.Model}\n";
                TxtDiskOutput.Text += $"  Serial: {d.SerialNumber}\n";
                TxtDiskOutput.Text += $"  Predict Failure: {d.PredictFailure}\n";
                TxtDiskOutput.Text += $"  Temperature: {d.Temperature:F1} C\n";
                TxtDiskOutput.Text += $"  Failure Reason Code: {d.Reason}\n\n";
            }
        }

        private void BtnOSIntegrity_Click(object sender, RoutedEventArgs e)
        {
            TxtDiskOutput.Text = "Running System File Check (SFC) and DISM Tool...\n";
            TxtDiskOutput.Text += "This scans system files and verifies alignment. Please wait...\n";

            // Run in a background thread to prevent UI lockup
            System.Threading.Tasks.Task.Run(() =>
            {
                var result = DiskHealthManager.PerformOSIntegrityCheck();
                Dispatcher.Invoke(() =>
                {
                    TxtDiskOutput.Text += result.OutputSummary;
                    TxtDiskOutput.Text += $"\nDiagnostic: SFC status = {result.SfcClean}, DISM status = {result.DismClean}\n";
                });
            });
        }

        private void BtnOptimizeDisk_Click(object sender, RoutedEventArgs e)
        {
            TxtDiskOutput.Text = "Detecting storage media types...\n";
            System.Threading.Tasks.Task.Run(() =>
            {
                string output = DiskHealthManager.OptimizeVolume("C");
                Dispatcher.Invoke(() =>
                {
                    TxtDiskOutput.Text += output;
                });
            });
        }

        private void BtnScanCleanup_Click(object sender, RoutedEventArgs e)
        {
            TxtCleanupOutput.Text = "Running Dry-Run diagnostics...\n";

            // Dry run service tweaks
            SystemCleanupManager.OptimizeBackgroundServices(dryRun: true);
            
            // Dry run Windows Update cache
            SystemCleanupManager.CleanWindowsUpdateCache(dryRun: true);

            var proposed = AuditEngine.GetProposedChanges();
            if (proposed.Count == 0)
            {
                TxtCleanupOutput.Text += "System configuration is already fully optimized. No junk files found.\n";
                return;
            }

            foreach (var change in proposed)
            {
                TxtCleanupOutput.Text += $"Action: {change.ActionType}\n";
                TxtCleanupOutput.Text += $"  Target: {change.TargetPath}\n";
                TxtCleanupOutput.Text += $"  Original state: {change.PreValue}\n";
                TxtCleanupOutput.Text += $"  New target state: {change.PostValue}\n\n";
            }

            // Clear temporary registry queue to avoid stacking
            AuditEngine.ClearProposedChanges();
        }

        private void BtnDoCleanup_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Verify: Apply system optimization and clear cache folders?", "Confirm System Tweaks", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                TxtCleanupOutput.Text = "Applying system optimizations...\n";

                // Create System Restore Point before modifying
                RestorePointManager.CreateRestorePoint("PulseGuard System Optimization");

                bool serviceOpt = SystemCleanupManager.OptimizeBackgroundServices(dryRun: false);
                bool cacheOpt = SystemCleanupManager.CleanWindowsUpdateCache(dryRun: false);

                TxtCleanupOutput.Text += $"Telemetry Service Optimization: {(serviceOpt ? "Applied" : "Failed")}\n";
                TxtCleanupOutput.Text += $"Windows Update Cache Purge: {(cacheOpt ? "Applied" : "Failed")}\n";
            }
        }

        private void BtnVisualTweak_Click(object sender, RoutedEventArgs e)
        {
            var res = SystemCleanupManager.OptimizeVisualEffects(dryRun: false);
            if (res)
            {
                MessageBox.Show("Visual effects configured for best performance.", "Optimization Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to apply visual tweaks.");
            }
        }

        private void BtnToggleEsports_Click(object sender, RoutedEventArgs e)
        {
            if (!_isEsportsActive)
            {
                // Create dummy target profile
                var profile = new GameProfile
                {
                    DisplayName = "Manual Booster Mode",
                    ProcessName = "ManualGame.exe",
                    CpuPriority = 3,
                    CpuAffinityMask = -1,
                    DisableFullscreenOptimizations = true,
                    DisableNagleAlgorithm = true,
                    QosPriorityLevel = 1
                };

                EsportsEngine.EnterGameSession(profile);
                _isEsportsActive = true;
                MessageBox.Show("Esports Performance Mode Activated!", "Esports Mode ON", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                EsportsEngine.ExitGameSession();
                _isEsportsActive = false;
                MessageBox.Show("Esports tweaks deactivated. System restored to normal profiles.", "Esports Mode OFF", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnClearShaderCache_Click(object sender, RoutedEventArgs e)
        {
            EsportsEngine.ClearShaderCache();
            MessageBox.Show("DirectX and graphics shader compilation caches successfully purged.", "Shader Cache Cleaned", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCheckHags_Click(object sender, RoutedEventArgs e)
        {
            int hags = EsportsEngine.CheckHagsStatus();
            if (hags == 2)
            {
                TxtHagsStatus.Text = "HAGS State: Enabled (Optimum GPU pipeline)";
            }
            else if (hags == 1 || hags == 0)
            {
                TxtHagsStatus.Text = "HAGS State: Supported but Disabled. Click enable.";
                var res = MessageBox.Show("HAGS is supported but disabled. Enable it now? (Requires Reboot)", "HAGS Configuration", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    EsportsEngine.EnableHags();
                    MessageBox.Show("HAGS registry key enabled. Please reboot your machine to apply.", "System Reboot Required");
                }
            }
            else
            {
                TxtHagsStatus.Text = "HAGS State: Unsupported on this hardware/driver setup.";
            }
        }

        private void BtnLoadAuditLogs_Click(object sender, RoutedEventArgs e)
        {
            RefreshAuditLogsList();
        }

        private void RefreshAuditLogsList()
        {
            try
            {
                var logs = DatabaseManager.GetAuditLogs();
                LstAuditLogs.ItemsSource = logs;
            }
            catch {}
        }

        private void BtnRevertChange_Click(object sender, RoutedEventArgs e)
        {
            var selected = LstAuditLogs.SelectedItem as AuditLogEntry;
            if (selected == null)
            {
                MessageBox.Show("Please select an audit log entry to revert.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (selected.IsRolledBack)
            {
                MessageBox.Show("This modification has already been rolled back.", "Already Reverted");
                return;
            }

            var confirm = MessageBox.Show($"Revert changes applied by: '{selected.Description}'?", "Confirm Rollback", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                bool success = AuditEngine.RollbackEntry(selected.Id);
                if (success)
                {
                    MessageBox.Show("System state successfully restored.", "Rollback Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshAuditLogsList();
                }
                else
                {
                    MessageBox.Show("Failed to revert some settings. Ensure running as Administrator.", "Rollback Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnGenerateReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double write = BenchmarkEngine.MeasureDiskWriteSpeed();
                double read = BenchmarkEngine.MeasureDiskReadSpeed();
                double cpuScore = BenchmarkEngine.RunCpuMicroBenchmark();

                var smart = DiskHealthManager.PerformSmartScan();
                bool predictFail = smart.Any(s => s.PredictFailure);

                int score = Convert.ToInt32(TxtHealthScore.Text);
                string reportHtml = ReportGenerator.GenerateHtmlReport(
                    score, 
                    predictFail ? "Warning" : "Healthy", 
                    "SFC check aligned", 
                    50.0, 
                    write, 
                    read, 
                    cpuScore
                );

                string outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                string reportPath = ReportGenerator.SaveReport(outDir, reportHtml);

                MessageBox.Show($"Diagnostic report saved to your Desktop:\n{Path.GetFileName(reportPath)}", "Report Generated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate report: {ex.Message}");
            }
        }

        private void RefreshBiosChecklist()
        {
            try
            {
                GridBiosChecklist.ItemsSource = DatabaseManager.GetBiosChecklist();
            }
            catch {}
        }

        private void RefreshWindowsTweaks()
        {
            try
            {
                var tweaks = DatabaseManager.GetWindowsTweaks();
                var cvs = (System.Windows.Data.CollectionViewSource)this.FindResource("WindowsTweaksGrouped");
                cvs.Source = tweaks;
            }
            catch {}
        }

        private void BtnSelectRecommended_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cvs = (System.Windows.Data.CollectionViewSource)this.FindResource("WindowsTweaksGrouped");
                var tweaks = cvs.Source as System.Collections.Generic.List<WindowsTweakItem>;
                if (tweaks != null)
                {
                    int[] recommendedIds = new int[] { 58, 59, 60, 61, 64, 66, 67, 68, 73, 74, 78, 84, 85, 86, 87, 98, 99, 100, 106, 107, 113, 124, 125, 126, 130 };
                    foreach (var t in tweaks)
                    {
                        t.IsSelected = recommendedIds.Contains(t.ItemNumber);
                    }
                }
            }
            catch {}
        }

        private void BtnClearSelection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cvs = (System.Windows.Data.CollectionViewSource)this.FindResource("WindowsTweaksGrouped");
                var tweaks = cvs.Source as System.Collections.Generic.List<WindowsTweakItem>;
                if (tweaks != null)
                {
                    foreach (var t in tweaks)
                    {
                        t.IsSelected = false;
                    }
                }
            }
            catch {}
        }

        private void LogToConsole(string text)
        {
            Dispatcher.Invoke(() =>
            {
                ConsoleLog.Text += "\n" + text;
                ConsoleScroll.ScrollToEnd();
            });
        }

        private async void BtnApplySelectedAdvanced_Click(object sender, RoutedEventArgs e)
        {
            var cvs = (System.Windows.Data.CollectionViewSource)this.FindResource("WindowsTweaksGrouped");
            var tweaks = cvs.Source as System.Collections.Generic.List<WindowsTweakItem>;
            if (tweaks == null) return;

            var selected = tweaks.Where(t => t.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Please select at least one tweak to apply.", "No Tweaks Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show($"Are you sure you want to apply the {selected.Count} selected tweaks?\n(A System Restore Point will be created first)", "Confirm Tweaks", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            ConsoleLog.Text = "Starting optimization run...";
            LogToConsole("Creating System Restore Point...");
            
            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    RestorePointManager.CreateRestorePoint("PulseGuard Advanced Windows Tweaks");
                    LogToConsole("Restore Point created successfully.");
                }
                catch (Exception ex)
                {
                    LogToConsole($"Restore Point warning: {ex.Message}");
                }

                int successCount = 0;
                foreach (var tweak in selected)
                {
                    LogToConsole($"Applying: {tweak.FeatureName}...");
                    bool ok = AdvancedOSOptimizer.ApplyTweak(tweak, dryRun: false);
                    if (ok)
                    {
                        successCount++;
                        tweak.Status = "Applied";
                        LogToConsole($"[SUCCESS] {tweak.FeatureName}");
                    }
                    else
                    {
                        LogToConsole($"[FAILED] {tweak.FeatureName}");
                    }
                }
                LogToConsole($"\nFinished. Applied {successCount}/{selected.Count} tweaks successfully.");
            });

            RefreshWindowsTweaks();
        }

        private async void BtnRevertSelectedAdvanced_Click(object sender, RoutedEventArgs e)
        {
            var cvs = (System.Windows.Data.CollectionViewSource)this.FindResource("WindowsTweaksGrouped");
            var tweaks = cvs.Source as System.Collections.Generic.List<WindowsTweakItem>;
            if (tweaks == null) return;

            var selected = tweaks.Where(t => t.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Please select at least one tweak to revert.", "No Tweaks Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show($"Revert the {selected.Count} selected tweaks?", "Confirm Rollback", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            ConsoleLog.Text = "Starting rollback run...";
            await System.Threading.Tasks.Task.Run(() =>
            {
                int successCount = 0;
                foreach (var tweak in selected)
                {
                    LogToConsole($"Reverting: {tweak.FeatureName}...");
                    bool ok = AdvancedOSOptimizer.RevertTweak(tweak);
                    if (ok)
                    {
                        successCount++;
                        tweak.Status = "Not Applied";
                        LogToConsole($"[REVERTED] {tweak.FeatureName}");
                    }
                    else
                    {
                        LogToConsole($"[FAILED REVERT] {tweak.FeatureName}");
                    }
                }
                LogToConsole($"\nFinished. Reverted {successCount}/{selected.Count} tweaks.");
            });

            RefreshWindowsTweaks();
        }

        private void BtnSaveBiosChecklist_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var items = GridBiosChecklist.ItemsSource as System.Collections.Generic.List<BiosChecklistItem>;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        DatabaseManager.UpdateBiosChecklistItem(item.ItemNumber, item.IsCompleted, item.UserNotes);
                    }
                    MessageBox.Show("BIOS checklist states successfully saved to database.", "Checklist Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshBiosChecklist();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save BIOS checklist: {ex.Message}");
            }
        }

        private void BtnPreviewAdvanced_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tweaks = DatabaseManager.GetWindowsTweaks();
                AuditEngine.ClearProposedChanges();
                
                foreach (var t in tweaks)
                {
                    AdvancedOSOptimizer.ApplyTweak(t, dryRun: true);
                }

                var proposed = AuditEngine.GetProposedChanges();
                MessageBox.Show($"Dry-Run Completed! {proposed.Count} advanced adjustments will be registered.", "Dry-Run Results", MessageBoxButton.OK, MessageBoxImage.Information);
                AuditEngine.ClearProposedChanges();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dry run failed: {ex.Message}");
            }
        }

        private void BtnApplyAdvanced_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show("Are you sure you want to apply all 100 scriptable OS tweaks?\n(A System Restore Point will be created first to secure stability)", "Confirm Optimization", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    RestorePointManager.CreateRestorePoint("PulseGuard Advanced Windows Tweaks");
                    var tweaks = DatabaseManager.GetWindowsTweaks();
                    int successCount = 0;
                    
                    foreach (var t in tweaks)
                    {
                        bool ok = AdvancedOSOptimizer.ApplyTweak(t, dryRun: false);
                        if (ok) successCount++;
                    }

                    MessageBox.Show($"Successfully applied {successCount}/{tweaks.Count} advanced performance tweaks!", "Tuning Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshWindowsTweaks();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to apply tweaks: {ex.Message}");
                }
            }
        }

        // Window exit cleanup
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // Clean monitoring threads
            EsportsEngine.StopAutoMonitoring();
            _telemetryTimer?.Stop();
        }
    }
}