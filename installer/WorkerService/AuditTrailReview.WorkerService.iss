; Inno Setup script for AuditTrailReview.IntegrationHub.WorkerService.
; Builds a Windows installer that copies a self-contained win-x64 publish of the Worker Service
; into Program Files and registers it as a Windows Service. See README.md in this folder for the
; exact publish + ISCC.exe commands. This script is source (committed to git); the compiled
; installer .exe and the "publish\" folder it packages are build output (git-ignored -- see
; AuditTrailReview.Docs/Tech/Installation Manual.md section 8 for the manual-config, non-installer
; alternative, which stays supported alongside this).
;
; Deliberately does NOT collect FTP/database configuration through the installer UI -- the
; service is installed stopped, and the admin edits appsettings.json and starts it themselves,
; matching how every other component in this project is configured (no GUI config exists anywhere
; else either). See the FinishedLabel override in [Messages] below for the exact reminder shown.

#define MyAppName "AuditTrailReview Integration Hub Worker Service"
#define MyAppVersion "1.0.0"
#define MyServiceName "AuditTrailReviewWorkerService"
#define MyAppExeName "AuditTrailReview.IntegrationHub.WorkerService.exe"

[Setup]
; Fixed, never-changing AppId -- keeps upgrade/uninstall tracking stable across versions.
AppId={{A9C1F2E4-6B3D-4F1A-9E77-2D8B5C4A0F31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=AuditTrailReview
DefaultDirName={autopf}\AuditTrailReview\WorkerService
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma
SolidCompression=yes
WizardStyle=modern
OutputDir=Output
OutputBaseFilename=AuditTrailReviewWorkerService-Setup
UninstallDisplayName={#MyAppName}

[Files]
; appsettings.json is installed separately below (onlyifdoesntexist) so an upgrade never
; overwrites an admin's already-configured connection string/FTP settings.
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Excludes: "appsettings.json"
Source: "publish\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Run]
; Registered with start= auto (starts on next boot) but NOT started immediately -- the shipped
; appsettings.json still has placeholder/blank DB and FTP settings, so starting right away would
; just begin failing before the admin has had a chance to configure it. See [Messages] below.
Filename: "sc.exe"; Parameters: "create ""{#MyServiceName}"" binPath= ""{app}\{#MyAppExeName}"" start= auto DisplayName= ""{#MyAppName}"""; Flags: runhidden
Filename: "sc.exe"; Parameters: "description ""{#MyServiceName}"" ""Polls FTP for new audit trail files and processes large web/API scan uploads queued for background handling. See AuditTrailReview.Docs\Tech\Installation Manual.md section 8."""; Flags: runhidden
; Auto-restart on failure -- without this, sc.exe's default is "take no action", so if the process
; ever stops unexpectedly (crash, an unhandled exception past the two try/catches in Worker.cs,
; the machine's own hiccups) it just stays down until an admin notices and restarts it by hand.
; ScanUploadJobProcessor.RecoverStuckWorkerJobsAsync/FtpFileCheckerService.RecoverStuckClaimsAsync
; only ever run again once the process restarts -- they can't fix a service that never comes back.
; reset= 86400: a failure counter that's been quiet for a full day resets to zero, so a single old
; failure doesn't count against today's. actions=: restart after 1 minute on the 1st failure, then
; 5 minutes on the 2nd and every one after (a short backoff instead of a hot-restart-crash-loop).
Filename: "sc.exe"; Parameters: "failure ""{#MyServiceName}"" reset= 86400 actions= restart/60000/restart/300000/restart/300000"; Flags: runhidden
; failureflag 1: by default SCM only treats a nonzero exit code as a "failure"; a clean exit(0)
; (e.g. the BackgroundService.StopHost path Worker.cs's own comments describe -- an unhandled
; exception makes the generic host shut itself down gracefully, exit code 0) would otherwise NOT
; trigger the restart actions above at all. Flag 1 makes SCM treat any stop the SCM itself didn't
; request as a failure, regardless of exit code.
Filename: "sc.exe"; Parameters: "failureflag ""{#MyServiceName}"" 1"; Flags: runhidden

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop ""{#MyServiceName}"""; Flags: runhidden; RunOnceId: "StopService"
Filename: "sc.exe"; Parameters: "delete ""{#MyServiceName}"""; Flags: runhidden; RunOnceId: "DeleteService"

[Messages]
FinishedLabel=Setup has installed the AuditTrailReview Integration Hub Worker Service files and registered it as a Windows Service (currently stopped).%n%nBefore starting it, edit:%n%n    {app}\appsettings.json%n%nto set your database connection string and Security:EncryptionKey (the same value the web app uses). FTP/FTPS ingestion is configured and switched on in the web app under Admin Panel > Application Settings > FTP (it is OFF by default) -- see AuditTrailReview.Docs\Tech\Installation Manual.md section 8. Then start the service from Services.msc, or by running:%n%n    sc start {#MyServiceName}
