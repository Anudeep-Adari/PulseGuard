<div align="center">

# ⚡ PulseGuard

### Next-Generation Windows Optimization, Diagnostic Telemetry & Gaming Performance Suite
**.NET 8 LTS • C# 12 • WPF Fluent UI • Low-Latency Gaming Booster • Predictive NVMe Health**

<p align="center">
  <em>Enterprise-grade system telemetry, MMCSS latency tuning, and predictive storage wear forecasting with zero-data-loss safety restore points.</em>
</p>

<!-- Badges -->
<p align="center">
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8.0_LTS-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8" /></a>
  <a href="https://docs.microsoft.com/en-us/dotnet/csharp/"><img src="https://img.shields.io/badge/C%23-12-239120?style=for-the-badge&logo=csharp&logoColor=white" alt="C# 12" /></a>
  <a href="https://github.com/Anudeep-Adari/PulseGuard"><img src="https://img.shields.io/badge/Platform-Windows_11_%7C_10-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Windows" /></a>
  <a href="https://coderabbit.ai"><img src="https://img.shields.io/badge/CodeRabbit-AI_Reviewed-FF5C00?style=for-the-badge&logo=coderabbit&logoColor=white" alt="CodeRabbit" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue?style=for-the-badge" alt="License" /></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Architecture-Clean_Hexagonal-8957E5?style=flat-square" />
  <img src="https://img.shields.io/badge/Safety_Guard-Restore_Point_Protected-10B981?style=flat-square" />
  <img src="https://img.shields.io/badge/Telemetry-Local_Only_%7C_No_Cloud-informational?style=flat-square" />
</p>

---

</div>

## 📌 Architectural Blueprint

```mermaid
flowchart TB
    %% Styling & Spacing
    classDef zone fill:#0d1117,stroke:#30363d,stroke-width:1.5px,color:#c9d1d9;
    classDef primary fill:#161b22,stroke:#58a6ff,stroke-width:2px,color:#ffffff;
    classDef security fill:#161b22,stroke:#f85149,stroke-width:2px,color:#ffffff;
    classDef data fill:#161b22,stroke:#3fb950,stroke-width:2px,color:#ffffff;

    subgraph Entry [" 🖥️ CLIENT PRESENTATION & ORCHESTRATION LAYER "]
        direction TB
        Shell["PulseGuard.Shell (WPF Fluent UI)<br/><sub>Dark-Mode Desktop Dashboard • Real-Time Visual Gauges</sub>"]:::primary
        Scheduler["PulseGuard.Core.Scheduler<br/><sub>High-Resolution Event Loop • Background Dispatcher</sub>"]:::primary
    end

    subgraph DiagnosticsZone [" 🔍 REGION 1: HARDWARE & STORAGE DIAGNOSTICS "]
        direction TB
        Health["HealthMonitorManager<br/>• CPU, RAM & VRAM Pollers<br/>• Thermal Throttling Alerts"]:::primary
        DiskHealth["DiskHealthManager<br/>• S.M.A.R.T. Drive Interrogation<br/>• IOPS & Storage Pool Sentry"]:::primary
        WearForecast["StorageForecaster<br/>• NVMe Endurance Decay Model<br/>• Daily Lifetime Projection (GB/d)"]:::primary
    end

    subgraph BoosterZone [" ⚡ REGION 2: ESPORTS GAMING & OS OPTIMIZER "]
        direction TB
        Esports["EsportsEngine<br/>• Process Affinity Lock<br/>• Dedicated CPU Core Isolation"]:::primary
        OSOptimizer["AdvancedOSOptimizer<br/>• MMCSS Priority Realignment<br/>• Non-Blocking Standby List Trim"]:::primary
        Cleanup["SystemCleanupManager<br/>• Safe Temp & Dump Shredder<br/>• Cache Reclaim Engine"]:::primary
    end

    subgraph SecurityGate [" 🛡️ CENTRAL TRANSIT: SAFETY & INTEGRITY GATEWAY "]
        direction TB
        VSS["RestorePointManager<br/><sub>VSS System Restore Point Checkpoint</sub>"]:::security
        Whitelist["WhitelistGuard<br/><sub>Critical Driver Store & Boot Cache Guard</sub>"]:::security
        Audit["AuditEngine<br/><sub>Pre-Execution Dry-Run Verifier</sub>"]:::security
    end

    subgraph Persistence [" 💾 PERSISTENCE & ANALYTICS DATA PLANE "]
        direction LR
        DB[("PulseGuard.Data<br/>SQLite Storage Engine")]:::data
        Reporting["PulseGuard.Core.Reporting<br/>• BenchmarkEngine<br/>• HTML Telemetry Exporter"]:::data
    end

    %% Spacious Architectural Connections
    Shell ------------->|User Directives & Settings| Scheduler
    Scheduler ---------->|High-Frequency Sampling| Health
    Scheduler ---------->|Game Launch Detection| Esports
    
    Health ------------->|Raw Drive Telemetry| DiskHealth
    DiskHealth --------->|Wear Metrics Stream| WearForecast
    
    Esports ------------>|Audio / Render Priority| OSOptimizer
    OSOptimizer -------->|Scheduled Cache Maintenance| Cleanup

    Cleanup ------------>|Sanitization Request| SecurityGate
    SecurityGate -.-.-.->|Restore Point Verified| DB
    
    WearForecast ------->|Long-Term Projections| DB
    Health ------------->|Aggregated Metrics| DB
    DB ----------------->|Historical Aggregation| Reporting
```

---

## ✨ Flagship Capabilities

### 🎮 Esports Gaming Booster (`PulseGuard.Core.GamingBooster`)
* **Dynamic Core Pinning:** Relocates background Windows services away from CPU cores hosting game processes.
* **MMCSS (Multimedia Class Scheduler Service) Alignment:** Reconfigures audio and thread scheduling to high-priority latency modes.
* **Working-Set Compaction:** Non-destructively flushes idle standby lists without hitching the active GPU thread.

### 📊 Predictive NVMe & Disk Forecaster (`PulseGuard.Core.DiskHealth`)
* **S.M.A.R.T. Attribute Interrogation:** Direct kernel-level drive query for reallocated sectors and wear levels.
* **Exponential Moving Average Decay:** Projects remaining SSD write endurance based on daily gigabyte write cadence.

### 🛡️ Ironclad Safety Subsystem (`PulseGuard.Safety`)
* **Pre-Execution Checkpointing:** Invokes Volume Shadow Copy Service (`VSS`) to create a system restore point prior to any systemic tweak.
* **Kernel & Essential Whitelists:** Prevents deletion of running driver stores, system-critical registry anchors, and boot caches.

---

## ⚡ Live Telemetry Mockup

```
╔════════════════════════════════════════════════════════════════════════════════╗
║ PulseGuard v2.4 ─ Mission Control                      [● ONLINE] [PROFILING]  ║
╠════════════════════════════════════════════════════════════════════════════════╣
║ CPU  [████████████░░░░░░░░░░░░░░░░] 38% @ 4.85 GHz  │ Temp: 52°C (Healthy)     ║
║ RAM  [████████████████████░░░░░░░░] 19.8 / 32.0 GB   │ Standby Cache: 4.2 GB    ║
║ NVMe [████████████████████████░░░░] 74% Used         │ Health: 99% (Good)       ║
║ FPS Latency Queue: 2.1ms (Optimized via MMCSS Priority)                        ║
╚════════════════════════════════════════════════════════════════════════════════╝
```

---

## 🚀 Quickstart & Building from Source

### Prerequisites
* Windows 10/11 (x64)
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* Visual Studio 2022 or JetBrains Rider

### Compilation

```powershell
# Clone the repository
git clone https://github.com/Anudeep-Adari/PulseGuard.git
cd PulseGuard

# Restore NuGet dependencies
dotnet restore PulseGuard.sln

# Build Solution in Release mode
dotnet build PulseGuard.sln -c Release

# Run Unit Tests
dotnet test PulseGuard.Tests/PulseGuard.Tests.csproj
```

---

## 📂 Project Organization

```
PulseGuard/
├── PulseGuard.sln                       # Solution entrypoint
├── PulseGuard.Shell/                    # Modern Fluent WPF desktop application
├── PulseGuard.Core.HealthMonitor/       # Real-time hardware telemetry pollers
├── PulseGuard.Core.GamingBooster/       # Thread affinity & OS performance optimizer
├── PulseGuard.Core.DiskHealth/          # S.M.A.R.T. and wear forecasting
├── PulseGuard.Core.SystemCleanup/       # High-efficiency storage reclaim engine
├── PulseGuard.Core.Reporting/           # Benchmark audit and HTML report generator
├── PulseGuard.Core.Scheduler/           # Background task timing manager
├── PulseGuard.Data/                     # SQLite persistence models & seeders
├── PulseGuard.Safety/                   # Restore-point gate & whitelist guard
└── PulseGuard.Tests/                    # Unit & regression test suite
```

---

<div align="center">

Crafted with high engineering precision by **[Anudeep Adari](https://github.com/Anudeep-Adari)**  
Continuous AI Code Reviews powered by **CodeRabbit**

</div>

