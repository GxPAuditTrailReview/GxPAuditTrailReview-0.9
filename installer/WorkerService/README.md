Builds a Windows installer for `AuditTrailReview.IntegrationHub.WorkerService` that registers it
as a real Windows Service (survives reboots/logoff), as an alternative to `dotnet run` or Docker --
see `AuditTrailReview.Docs/Tech/Installation Manual.md` section 8 for those.

Requires [Inno Setup](https://jrsoftware.org/isinfo.php) 6.x (free, including for commercial use)
and the .NET 10 SDK. Run both commands from the repository root.

## 1. Publish a self-contained win-x64 build

```powershell
dotnet publish AuditTrailReview.IntegrationHub.WorkerService\AuditTrailReview.IntegrationHub.WorkerService.csproj -c Release -r win-x64 --self-contained true -o installer\WorkerService\publish
```

This bundles the .NET 10 runtime into the output, so the target machine needs nothing
pre-installed. The `publish\` folder this produces is build output, not source -- it's
git-ignored; re-run this command any time the Worker Service's code changes before rebuilding the
installer.

## 2. Build the installer

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\WorkerService\AuditTrailReview.WorkerService.iss
```

Produces `installer\WorkerService\Output\AuditTrailReviewWorkerService-Setup.exe` (also
git-ignored -- the built `.exe` is a release artifact, distributed the same way a Docker image
is: built on demand, never committed).

Bump `MyAppVersion` at the top of the `.iss` script before cutting a new installer release.

## What the installer does

- Installs to `C:\Program Files\AuditTrailReview\WorkerService\` (admin rights required).
- Registers a Windows Service named `AuditTrailReviewWorkerService`, set to start automatically on
  boot -- but does **not** start it immediately. The shipped `appsettings.json` has a LocalDB
  connection string and blank FTP credentials, so an admin needs to edit it first (same file, same
  settings documented in Installation Manual.md section 8 -- including `Ftp:SystemId`, which is
  **not** in the shipped file and must be added manually) and then start the service themselves,
  e.g. `sc start AuditTrailReviewWorkerService` or via `services.msc`.
- Configures the service to auto-restart itself if the process ever stops unexpectedly (1 minute
  after the 1st failure, 5 minutes after each one after that, counter reset after a quiet day) --
  see the `sc.exe failure`/`sc.exe failureflag` commands and their comments in the `.iss` script.
  Without this, a crash (or any unhandled exception reaching past `Worker.cs`'s own two
  try/catches) leaves the service down until an admin notices and restarts it by hand; the Worker
  Service's own startup recovery (stuck `ScanUploadJob`/`IngestedFile` rows reset for retry) only
  ever runs again once the process actually restarts.
- On an upgrade (reinstalling over an existing install), `appsettings.json` is left untouched if
  it already exists, so an admin's configured connection string/FTP settings survive.
- Uninstalling (Add/Remove Programs) stops and deletes the service, then removes the install
  directory.

Does not collect any configuration through the installer UI -- this matches every other
component in this project, none of which have a config wizard either.
