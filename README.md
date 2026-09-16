# Audomatick Desktop

Production-ready Windows Forms desktop client for **Audomatick**, built for the **FACON Control Plane** with **Velopack 1.2.0** auto-updating, **Release Policy Pinning (`DESK-004`)**, **Data-Plane Metering & Limit Enforcement (`LIM-001..012`)**, **Durable Offline Queuing (`USG-003`)**, and **Zero-Secret Compliance (`USG-010`)**.

GitHub Repository: [https://github.com/Faizan-facon/AudomatickTest.git](https://github.com/Faizan-facon/AudomatickTest.git)

---

## 1. Project Architecture

The solution targets `.NET 10.0 (Windows)`:

```text
audomatick desktop/
├── Audomatick.Desktop.sln                  # Visual Studio Solution
├── Audomatick.Desktop.slnx                 # Modern .NET 10 XML Solution
├── facon.yaml                              # Canonical FACON Data-Plane manifest v1.0
├── build.ps1                               # Local build, test & packaging script with Velopack
├── .gitignore                              # Visual Studio, .NET & Velopack exclusions
├── .github/workflows/ci.yml                # Automated GitHub Actions Windows CI/CD workflow
├── src/
│   ├── FaconControlPlane.DataPlane.Contracts/  # Portable Data-Plane contracts
│   └── Audomatick.Desktop/                 # WinForms desktop application
│       ├── Program.cs                      # VelopackApp.Build().Run() startup hook
│       ├── MainForm.cs                     # Full UI: updates, metering, diagnostics
│       ├── Models/                         # DesktopUpdateStatus, DesktopReleaseInfo
│       └── Services/                       # VelopackCoordinator, ReleasePolicyClient, etc.
└── tests/
    └── Audomatick.Desktop.Tests/           # xUnit tests verifying manifest & invariants
```

---

## 2. FACON Manifest (`facon.yaml`)

The application defines an official Data-Plane contract v1.0:

```yaml
schemaVersion: "1.0"
name: "audomatick-desktop"
applicationId: "a1b2c3d4-e5f6-4a0b-8c1d-2e3f4a5b6c7d"
type: "desktop"
probes:
  liveness:
    path: "/health/live"
    port: 0
  readiness:
    path: "/health/ready"
    port: 0
metadata:
  endpoint: "/.well-known/facon"
  packaging: "velopack"
  velopackVersion: "1.2.0"
  supportedChannels:
    - "stable"
    - "beta"
    - "canary"
resources:
  - logicalName: "primary-database"
    resourceType: "database"
    engine: "postgres"
    purpose: "Primary application database"
    required: true
telemetry:
  serviceName: "audomatick-desktop"
  tracingEnabled: true
  metricsEnabled: false
limits:
  - key: "audomatick.tasks.processed.daily"
    measurementKind: "Counter"
    aggregationPeriod: "Day"
    enforcementMode: "Soft"
    description: "Daily count of automated tasks processed by Audomatick Desktop"
  - key: "sessions.active"
    measurementKind: "Gauge"
    aggregationPeriod: "None"
    enforcementMode: "Soft"
    description: "Concurrent active Audomatick Desktop client sessions"
```

---

## 3. Quick Start & Development

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Velopack CLI 1.2.0](https://velopack.io):
  ```powershell
  dotnet tool install -g vpk
  ```

### Build & Run
```powershell
# Restore & build
dotnet build Audomatick.Desktop.sln

# Run unit tests
dotnet test Audomatick.Desktop.sln

# Launch the desktop client
dotnet run --project src/Audomatick.Desktop/Audomatick.Desktop.csproj
```

### Packaging Velopack Releases
Generate standalone setup installers, portable zips, and update packages:

```powershell
.\build.ps1 -Pack -Configuration Release -Version "1.0.0"
```

Output artifacts in `artifacts/releases/`:
- `Audomatick.Desktop-win-Setup.exe`
- `Audomatick.Desktop-win-Portable.zip`
- `Audomatick.Desktop-1.0.0-full.nupkg`
- `RELEASES`, `releases.win.json`, `assets.win.json`

---

## 4. Manual Testing Scenarios

1. **Velopack Update Check**:
   - In `MainForm`, change the Update Feed path to a local directory containing release packages (e.g. `./artifacts/releases`).
   - Click **Check for Updates** $\rightarrow$ verifies release policy and feed.
   - Click **Download Update** $\rightarrow$ stages update via Velopack.
   - Click **Apply & Restart** $\rightarrow$ calls `VelopackCoordinator.ApplyUpdateAndRestart()`.

2. **FACON Metering & Limit Enforcement**:
   - Click **Check Quota Capacity** to query `audomatick.tasks.processed.daily`.
   - Click **Process Automated Task (+1)**:
     - Pre-checks quota limit.
     - Executes task.
     - Reports usage or queues locally in `%LocalAppData%/FaconDesktop/usage_queue.json` if offline.
   - Click **Flush Offline Queue** to replay cached measurements when reconnected.

3. **Zero-Secret Diagnostics & Manifest Inspection**:
   - Click **Inspect facon.yaml Manifest** to verify schema consistency.
   - Click **Run Zero-Secret Diagnostics** to confirm invariant `USG-010`.
