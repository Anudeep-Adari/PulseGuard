using System;
using System.ComponentModel;

namespace PulseGuard.Data
{
    public class GameProfile
    {
        public int Id { get; set; }
        public string ProcessName { get; set; } // e.g., "cs2.exe"
        public string DisplayName { get; set; }
        public int CpuPriority { get; set; } // 3 = High, 4 = Realtime
        public int CpuAffinityMask { get; set; } // -1 = All cores, or bitmask
        public bool DisableFullscreenOptimizations { get; set; }
        public bool DisableNagleAlgorithm { get; set; }
        public int QosPriorityLevel { get; set; } // 1 = High, 0 = Default
    }

    public class AuditLogEntry
    {
        public string Id { get; set; } // GUID
        public string Timestamp { get; set; } // ISO 8601 UTC
        public string Module { get; set; }
        public string Description { get; set; }
        public string PreStateJson { get; set; } // Original state (for rollback)
        public string PostStateJson { get; set; } // Modified state
        public bool IsRolledBack { get; set; }
    }

    public class HealthScoreEntry
    {
        public int Id { get; set; }
        public string Timestamp { get; set; } // ISO 8601 UTC
        public int Score { get; set; }
        public string DiskStatus { get; set; }
        public string PerformanceStatus { get; set; }
        public double CleanupSizeMb { get; set; }
    }

    public class BiosChecklistItem
    {
        public int ItemNumber { get; set; }
        public string Category { get; set; }
        public string FeatureName { get; set; }
        public string HowTo { get; set; }
        public string Risk { get; set; }
        public bool IsCompleted { get; set; }
        public string CompletionDate { get; set; }
        public string UserNotes { get; set; }
    }

    public class WindowsTweakItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        private string _status;
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public int ItemNumber { get; set; }
        public string Category { get; set; }
        public string FeatureName { get; set; }
        public string RegistryPath { get; set; }
        public string ValueName { get; set; }
        public string ValueType { get; set; }
        public string OriginalValue { get; set; }
        public string OptimizedValue { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
