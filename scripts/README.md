This folder contains helper scripts to install the .NET 10 SDK on developer machines.

Files:
- install-dotnet.ps1  - PowerShell no-admin installer (Windows)
- install-dotnet.sh   - Bash no-admin installer (Linux/macOS)

Usage examples:
- PowerShell (Windows):
  PS> .\scripts\install-dotnet.ps1

- Bash (Linux/macOS):
  $ bash ./scripts/install-dotnet.sh

Notes:
- These scripts download and run the official dotnet-install scripts from Microsoft into a user-local folder (~/.dotnet or %USERPROFILE%\.dotnet).
- After running, add the install directory to your PATH and restart your shell/IDE.
- global.json is configured with "rollForward": "patch" so any installed .NET 10.x patch SDK will be accepted.
