# Copilot Instructions

## Build & test

Requires the .NET 10 SDK (`global.json` pins 10.0.x). No separate lint step.

```bash
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~Get_malformed_date_returns_400"   # single test (xUnit)
dotnet publish src/TimeTracker.App -c Release -r <rid> --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish/<rid>
```

`<rid>` = `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`. Quit the running app (tray → Quit) before republishing on Windows, or the exe is locked.

## Architecture

Three layers; dependencies only point downward:

- `src/TimeTracker.Core` – `TimeEntry`/`RunningSession` models, `IReportStore` + `JsonReportStore` (reports grouped by UTC date in `timetracker.json`), and `TrackingService` (start/stop/pause/resume, persisted to `running.json`, UTC-midnight rollover splitting).
- `src/TimeTracker.Api` – minimal-API endpoints (`ReportEndpoints`) and a Kestrel host (`ApiHost`) bound to `127.0.0.1:7777`. Depends only on `IReportStore`.
- `src/TimeTracker.App` – Avalonia UI, tray icon, autostart, single-instance guard. `Program.Main` is the composition root: it creates one `JsonReportStore` and one `TrackingService`, hands them to the UI via static properties on `App` (`App.Store`, `App.Tracking`), and passes the same store to `ApiHost`. Reports added via the HTTP API reach the UI through the store's `ReportAdded` event.
- `tests/TimeTracker.Tests` – xUnit; `ApiTests` exercise the endpoints, `TrackingTests` the service.

## Conventions

- **All stored and API timestamps are UTC**; only the UI converts to local time. The API rejects non-UTC offsets, ranges > 24h, and descriptions > 500 chars. A report's day key is the UTC date of `StartUtc` (`JsonReportStore.DayKey`).
- `TrackingService` takes a `TimeProvider` (and optional state path) so time-dependent logic is testable; don't use `DateTime.UtcNow` directly there.
- `JsonReportStore` serializes access with a `SemaphoreSlim`, writes via temp file + move, and backs up corrupt JSON to `.bak`.
- Data directory resolution: `--data <folder>` / `TIMETRACKER_DATA` → executable folder → `%APPDATA%\TimeTracker` (first writable wins).
- Nullable reference types and implicit usings are enabled in every project; Avalonia compiled bindings are on by default.

## Releases & packaging

- `scripts\release.ps1 -Version x.y.z` performs the whole gitflow release (`develop` → `release/x.y.z` → `master` + tag `vx.y.z`), builds zips/`.deb`/`.rpm`, creates the GitHub release and pushes updated manifests to the Scoop bucket (`nmandzyk/scoop-bucket`) and Homebrew tap (`nmandzyk/homebrew-tap`).
- Package manifests are generated from `packaging\` templates (`{{PLACEHOLDER}}` tokens); edit the templates, not the generated files in the external repos.
- The main branch is `master`, not `main`.

## Contributing

- Follow the gitflow branching policy.
- Feature branch names must include the ticket number: `#<ticket>_short_description` (e.g. `#1_short_description`).
