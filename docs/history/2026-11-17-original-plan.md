# Ticket File Organizer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows tray utility that batches files and folders from a configurable inbox and safely archives them under a numeric ticket folder.

**Architecture:** Keep filesystem and validation behavior in a small `net8.0` core library so it can be tested independently; keep tray UI, WinForms dialogs, settings integration, and Windows startup integration in a `net8.0-windows` app. Use only .NET platform APIs at runtime; use MSTest for automated core tests.

**Tech Stack:** C#, .NET 8, WinForms, `FileSystemWatcher`, `System.Text.Json`, MSTest (`MSTest.Sdk`; run with `dotnet run` in this restricted environment).

**Spec:** `SPEC.md`

## Global Constraints

- Target .NET 8 and Windows x64; use WinForms for the tray utility.
- Default inbox is `D:\Downloads\_收件`; default target is unset; default quiet period is 10 seconds.
- Ticket identifiers contain one or more ASCII digits only.
- A conflict is never overwritten without an explicit user choice.
- A failed move must leave the source item available in the inbox; process items individually and report partial completion.
- Startup-at-login is opt-in and defaults off.
- Persist settings under `%APPDATA%\log-organizer\config.json`.
- Publish a self-contained `win-x64` single-file exe; report actual artifact size rather than promising 10–15 MB.

## Review Focus

- A file still being copied or a folder with changing contents must not be archived before the quiet period restarts after the latest observed change; pin the debounce-window behavior in core tests.
- Existing nested folders merge, but same-name files always require an explicit conflict decision; pin merge and each conflict choice in archive-service tests.
- Copy/verification/commit failure must not lose the source; pin failure-path preservation with temporary-directory tests.
- Invalid or empty ticket input and an unset/nonexistent target must not start file movement; test validation and destination preconditions.
- Repeated watcher events, pause/resume, and manual “check now” must not create duplicate prompts or lose the pending batch; verify the coordinator behavior and perform a Windows smoke test.
- Symlinks/reparse points in inbox items or destination ancestors must not escape the configured roots; verify they are rejected safely.

---

### Task 1: Create solution and tested core contracts

**Files:**
- Create: `LogOrganizer.sln`
- Create: `src/LogOrganizer.Core/LogOrganizer.Core.csproj`
- Create: `src/LogOrganizer.Core/TicketNumber.cs`
- Create: `tests/LogOrganizer.Core.Tests/LogOrganizer.Core.Tests.csproj`
- Create: `tests/LogOrganizer.Core.Tests/TicketNumberTests.cs`

**Interfaces:**
- Produces `TicketNumber.TryParse(string? value, out string ticketNumber) -> bool`; accept one or more ASCII digits only.

- [ ] Write tests for valid ASCII digit strings and rejection of empty, whitespace, letters, punctuation, and non-ASCII numerals.
- [ ] Run `dotnet build tests/LogOrganizer.Core.Tests` and confirm compilation fails because `TicketNumber` is missing.
- [ ] Add the core and MSTest projects, implement `TicketNumber.TryParse`, and keep the tests package-limited to Microsoft test packages.
- [ ] Run `dotnet run --project tests/LogOrganizer.Core.Tests` and `dotnet build LogOrganizer.sln`; all tests must pass and the build must exit 0. The test project uses `MSTest.Sdk` and is invoked via `dotnet run` because this environment's VSTest child host cannot open its parent process.
- [ ] Commit as `feat: add ticket number validation core`.

### Task 2: Implement quiet-period batch tracking

**Files:**
- Create: `src/LogOrganizer.Core/QuietPeriod.cs`
- Create: `src/LogOrganizer.Core/InboxWatcher.cs`
- Create: `tests/LogOrganizer.Core.Tests/QuietPeriodTests.cs`
- Create: `src/LogOrganizer.WinForms/LogOrganizer.WinForms.csproj`

**Interfaces:**
- `QuietPeriod.RecordChange(DateTimeOffset changedAt)` updates the next due time to `changedAt + delay`; `IsDue(DateTimeOffset now)` reports whether the quiet period elapsed.
- `InboxWatcher` observes top-level inbox entries and changes beneath pending folders; it emits at most one ready batch after the configured quiet period. Its snapshot contains top-level files and directories only.

- [ ] Test that each new change restarts the quiet period and that readiness begins only after the full configured delay.
- [ ] Run `dotnet run --project tests/LogOrganizer.Core.Tests` and confirm the focused test fails before implementation.
- [ ] Implement the quiet-period state and watcher lifecycle (start, pause, resume, stop), disposing the underlying `FileSystemWatcher` cleanly.
- [ ] Add the WinForms project targeting `net8.0-windows` with Windows Forms enabled and a reference to Core.
- [ ] Run `dotnet run --project tests/LogOrganizer.Core.Tests` and `dotnet build LogOrganizer.sln`; both must pass.
- [ ] Commit as `feat: detect quiet inbox batches`.

### Task 3: Implement safe archive and conflict outcomes

**Files:**
- Create: `src/LogOrganizer.Core/ArchiveService.cs`
- Create: `src/LogOrganizer.Core/ArchiveConflict.cs`
- Create: `tests/LogOrganizer.Core.Tests/ArchiveServiceTests.cs`

**Interfaces:**
- `ArchiveService.ArchiveAsync(inboxPath, targetRoot, ticketNumber, items, conflictResolver, cancellationToken)` archives a selected snapshot and returns per-item outcomes.
- Conflict decisions are `Overwrite`, `Rename(newName)`, or `Skip`; the resolver is supplied by the UI and is invoked only for file-name collisions.

- [ ] Write temp-directory tests for new ticket-folder creation, merging an existing ticket folder, nested-directory merge, explicit overwrite/rename/skip, invalid destination, and source preservation when transfer fails.
- [ ] Run `dotnet run --project tests/LogOrganizer.Core.Tests` and confirm the new archive cases fail before implementation.
- [ ] Implement same-volume moves and cross-volume copy-to-temp, verify, destination commit, then source removal; never delete or overwrite silently.
- [ ] Run `dotnet run --project tests/LogOrganizer.Core.Tests`; all tests pass and temporary test directories are cleaned after each run.
- [ ] Commit as `feat: archive inbox batches safely`.

### Task 4: Build settings and tray workflow

**Files:**
- Create: `src/LogOrganizer.WinForms/Program.cs`
- Create: `src/LogOrganizer.WinForms/TrayApplicationContext.cs`
- Create: `src/LogOrganizer.WinForms/SettingsForm.cs`
- Create: `src/LogOrganizer.WinForms/ArchiveForm.cs`
- Create: `src/LogOrganizer.WinForms/ConflictForm.cs`
- Create: `src/LogOrganizer.WinForms/SettingsStore.cs`
- Create: `src/LogOrganizer.WinForms/StartupRegistration.cs`

**Interfaces:**
- `SettingsStore.Load()` returns validated settings, supplying the default inbox and 10-second delay while leaving target unset on first run; `Save(settings)` writes JSON under `%APPDATA%`.
- `TrayApplicationContext` owns the watcher, tray icon, and forms; menu commands are Settings, Check now, Open inbox, Open target, Pause/Resume, Exit.
- The archive form appears centered without taking focus on initial display; user interaction permits typing. Empty/non-digit input cannot submit.

- [ ] Implement JSON settings storage and tests for first-run defaults and invalid/missing configuration recovery (use a temporary app-data path seam).
- [ ] Run `dotnet run --project tests/LogOrganizer.Core.Tests` and confirm settings tests fail before implementation.
- [ ] Implement tray menu, settings, target precondition messaging, manual check, pause/resume, and orderly exit/disposal.
- [ ] Implement archive and conflict forms; ensure Later preserves pending items and conflict choices map to the Core outcomes.
- [ ] Add opt-in HKCU Run startup registration; default off and remove the entry when unchecked.
- [ ] Run `dotnet run --project tests/LogOrganizer.Core.Tests` and `dotnet build LogOrganizer.sln`; manually smoke-test tray, settings, initial non-activation/click-to-type, batch prompt, archive, and exit on Windows.
- [ ] Commit as `feat: add tray and archive workflow`.

### Task 5: Add publish script and user documentation

**Files:**
- Create: `build.ps1`
- Create: `README.md`
- Create: `.gitignore`

- [ ] Add a PowerShell publish command targeting `win-x64`, self-contained, single-file; set a clear output directory and propagate nonzero exit codes.
- [ ] Document first run, inbox/target settings, tray menu, conflict choices, safe partial-failure behavior, and actual self-contained runtime/size tradeoff.
- [ ] Add standard .NET build output exclusions to `.gitignore`.
- [ ] Run `dotnet test LogOrganizer.sln`, `dotnet build LogOrganizer.sln -c Release`, and `./build.ps1`; confirm tests pass, build exits 0, and the single-file exe exists.
- [ ] Commit as `build: publish and document tray utility`.
