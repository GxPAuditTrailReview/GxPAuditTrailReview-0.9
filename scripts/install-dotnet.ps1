# PowerShell script to install .NET 10 SDK (no admin required)
# Usage: Open PowerShell and run: .\scripts\install-dotnet.ps1

$installDir = "$env:USERPROFILE\.dotnet"
$channel = "10.0"

Write-Host "Downloading dotnet-install script..."
Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1 -UseBasicParsing

Write-Host "Installing .NET $channel SDK to $installDir (no admin)..."
.
\dotnet-install.ps1 --channel $channel --install-dir $installDir

if ($LASTEXITCODE -eq 0) {
	Write-Host "Installation completed. To use the installed SDK in this session run:"
	Write-Host "  $env:USERPROFILE\\.dotnet\\dotnet --info"
	Write-Host "Add $installDir to your PATH for future shells. Example (PowerShell):"
	Write-Host "  $env:PATH = \"$installDir;$env:PATH\""
	Write-Host "Or add to your profile: Add-Content -Path $PROFILE -Value \"$env:PATH = '$installDir;$env:PATH'\""
} else {
	Write-Host "Installation failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
