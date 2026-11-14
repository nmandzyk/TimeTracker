# Time Tracker

A small, portable, always-on-top time tracker. Enter what you are working on, press the green ▶ to start and the red ■ to stop. Reports are stored in a JSON file grouped by date, and a local HTTP API lets other tools add and read reports.

- Cross-platform: Windows, Linux, macOS (built with .NET 10 + Avalonia UI)
- Portable: one self-contained executable, no installer, no .NET runtime needed
- Tray-only: no taskbar/Dock button, borderless thin-border window, draggable, always on top
- All data is stored in **UTC**; the UI shows **local time**

## Download / build output

Release builds are in `publish\<runtime>\` and zipped in `publish\dist\`:

| Platform | Folder | File |
|---|---|---|
| Windows x64 | `publish\win-x64` | `TimeTracker.exe` |
| Linux x64 | `publish\linux-x64` | `TimeTracker` |
| macOS Apple Silicon | `publish\osx-arm64` | `TimeTracker` |
| macOS Intel | `publish\osx-x64` | `TimeTracker` |

## Installing with a package manager

| Platform | Command |
|---|---|
| Windows (Scoop) | `scoop bucket add nmandzyk https://github.com/nmandzyk/scoop-bucket` then `scoop install timetracker` |
| macOS / Linux (Homebrew) | `brew install nmandzyk/tap/timetracker` |
| Debian / Ubuntu | download `timetracker_<version>_amd64.deb` from the [releases](https://github.com/nmandzyk/TimeTracker/releases) page, then `sudo dpkg -i <file>.deb` |
| Fedora / RHEL | download the `.rpm` from the releases page, then `sudo rpm -i <file>.rpm` |

Homebrew on Linux only has an x64 build. The `.deb`/`.rpm` packages install the binary as `/usr/bin/TimeTracker`.

## Starting the app

### Windows
Double-click `TimeTracker.exe` (or run it from a terminal). SmartScreen may warn because the exe is unsigned: *More info → Run anyway*.

### Linux
```bash
chmod +x TimeTracker
./TimeTracker
```
The tray icon needs StatusNotifier/AppIndicator support. On GNOME install the "AppIndicator and KStatusNotifierItem Support" extension. If the window is the only way to reach the app, the tray is required to show/hide it.

### macOS
```bash
chmod +x TimeTracker
xattr -d com.apple.quarantine TimeTracker   # only if downloaded; the binary is unsigned
./TimeTracker
```
The app appears as a menu-bar item. A Dock icon may briefly appear because the binary is not wrapped in an `.app` bundle with `LSUIElement=true`.

Only one instance runs at a time; starting a second copy exits immediately and brings the already-running window to the front (even if it was hidden in the tray).

> Note: the Linux and macOS builds were produced by cross-publishing from Windows and have **not been run** on those systems yet.

## Using the app

- **Window**: type a description, press **▶** (or Enter) to start. The big counter shows the current session; the dimmed `Today hh:mm` shows the total tracked today.
- **■ Stop** saves the entry. **⏸ Pause / ▶ Resume** (shown while tracking) excludes paused time: the saved entry starts at the original start and ends at start + active time.
- **Today panel** (bottom, collapsed by default): click to expand the list of today's entries (local times, duration, description).
- **✕** hides the window to the tray; the app keeps running. Drag the window anywhere by its background; the position is remembered.
- **Tray icon** (grey = idle, red = tracking): click to show/hide. Menu: *Show / Hide*, *Start / Stop*, *Open data folder*, **Start with system** (checkbox, launches the app at login), *Quit*.
- **UTC midnight**: "Today" resets at 00:00 UTC. A session still running at that moment is saved under the previous day and a new one with the same description continues automatically.
- A session left running survives restarts/crashes (`running.json`).

## Data files

Stored next to the executable (falls back to `%APPDATA%\TimeTracker` / `~/.config/TimeTracker` if that folder is not writable). Override with `--data <folder>` or the `TIMETRACKER_DATA` environment variable.

| File | Content |
|---|---|
| `timetracker.json` | Reports grouped by UTC date |
| `running.json` | Currently running session (only while tracking) |
| `window.json` | Last window position |
| `error.log` | Written if the API port could not be opened |

```json
{
  "2026-11-12": [
    { "id": "…", "startUtc": "2026-11-12T08:00:00Z", "endUtc": "2026-11-12T09:30:00Z", "description": "Design review" }
  ]
}
```

## HTTP API

Base URL: `http://127.0.0.1:7777` (localhost only, fixed port, no authentication). The app must be running. All timestamps are **UTC**. A report is grouped by the UTC date of `startUtc`.

### Add a report — `POST /api/reports`

```bash
curl -X POST http://127.0.0.1:7777/api/reports \
  -H "Content-Type: application/json" \
  -d '{"startUtc":"2026-11-12T08:00:00Z","endUtc":"2026-11-12T09:30:00Z","description":"Design review"}'
```
PowerShell:
```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:7777/api/reports -ContentType application/json `
  -Body '{"startUtc":"2026-11-12T08:00:00Z","endUtc":"2026-11-12T09:30:00Z","description":"Design review"}'
```
- `201 Created` with the stored entry (including its `id`) and `Location: /api/reports/2026-11-12`.
- `400` with validation details if: `description` is empty or > 500 characters; a timestamp is missing or not UTC (must end with `Z`/`+00:00`); `endUtc` is not after `startUtc`; the range is longer than 24 hours.

### Get reports for a day — `GET /api/reports/{date}`

`date` is `yyyy-MM-dd` (UTC date, no time).
```bash
curl http://127.0.0.1:7777/api/reports/2026-11-12
```
- `200` → JSON array sorted by start time (empty array if none):
  `[{"id":"…","startUtc":"2026-11-12T08:00:00Z","endUtc":"2026-11-12T09:30:00Z","description":"Design review"}]`
- `400` if the date is malformed.

Reports added through the API appear in the app's Today panel automatically.

## Building from source

Requires the .NET 10 SDK (`global.json` pins 10.0.x).

```bash
dotnet test
dotnet publish src/TimeTracker.App -c Release -r <rid> --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish/<rid>
```
`<rid>` = `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`. Close the running app (tray → Quit) before republishing to Windows, otherwise the exe is locked.

## Creating a release

Releases follow gitflow (`develop` → `release/<version>` → `master`). Commit all changes, quit the running app, then run:

```powershell
.\scripts\release.ps1 -Version 1.2.0
```

The script:
1. Creates `release/<version>` from `develop` and runs `dotnet test`.
2. Publishes `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64` and zips them into `publish\dist\TimeTracker-<version>-<rid>.zip`.
3. Builds a `.deb` and `.rpm` from the `linux-x64` build with [nfpm](https://nfpm.goreleaser.com) (skipped with a warning if `nfpm` is not installed: `scoop install nfpm` or `winget install goreleaser.nfpm`).
4. Merges the release branch into `master` (tagged `v<version>`) and back into `develop`, then pushes them.
5. Creates a GitHub release with the zips, `.deb` and `.rpm` attached (requires the `gh` CLI).
6. Updates the package managers from the templates in `packaging\`, creating the repos on first use: the Scoop bucket `nmandzyk/scoop-bucket` (`bucket/timetracker.json`) and the Homebrew tap `nmandzyk/homebrew-tap` (`Formula/timetracker.rb`). Download URLs and SHA-256 hashes are filled in automatically.

Options: `-SkipTests`, `-NoGitHubRelease`, `-SkipPackageManagers`, `-Runtimes <rid>,<rid>`, `-GitHubRepo`, `-ScoopBucketRepo`, `-HomebrewTapRepo`. The Homebrew tap needs all of `osx-arm64`, `osx-x64` and `linux-x64`.

## Project layout

```
src/TimeTracker.Core   models, JSON store, tracking service (start/stop/pause, midnight split)
src/TimeTracker.Api    minimal API endpoints + Kestrel host
src/TimeTracker.App    Avalonia UI, tray, autostart
tests/TimeTracker.Tests
```
