<#
.SYNOPSIS
  Applies the GxPAuditTrail SQL schema scripts to an Azure SQL database.

.DESCRIPTION
  There are no EF migrations (ADR-0004), so this is the whole schema deployment:
    1. If dbo.Scan does not exist the database is empty, so the snapshot script runs first.
    2. Every incremental script numbered 0.20 and above runs, sorted by version number (not by
       file name: "0.7" sorts after "0.20" alphabetically). The scripts are written to be safe to
       re-run (existence checks), so no schema-version table is needed.
  The scripts begin with "USE [GxPAuditTrail]", which Azure SQL rejects when the database has a
  different name, so those lines are stripped before execution. A failing script stops the run.

  Must run under an Azure CLI login (AzureCLI@2). That identity must be the SQL server's Entra
  admin (deploy/infra/foundation.bicep sets this) or a db_owner contained user.
#>
param(
    [Parameter(Mandatory)] [string] $SqlServerName,
    [Parameter(Mandatory)] [string] $DatabaseName,
    [string] $ResourceGroup,
    [string] $SchemaRoot = 'AuditTrailReview.Database/Database schema',
    [string] $SnapshotScript = 'ATR 0.8 20261020-SQL2025.sql',
    [int] $FirstIncrementalMinor = 20,
    [switch] $OpenFirewall,
    # Optional: managed identity the apps connect with. When given, a contained database user for
    # it is created (db_owner) if missing. Created WITH SID from the client id instead of FROM
    # EXTERNAL PROVIDER, so the pipeline identity needs no Microsoft Graph permission.
    [string] $AppIdentityName,
    [string] $AppIdentityClientId
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Module -ListAvailable -Name SqlServer)) {
    Install-Module SqlServer -Force -Scope CurrentUser -AllowClobber
}
Import-Module SqlServer

$server = "$SqlServerName.database.windows.net"
$token = az account get-access-token --resource https://database.windows.net --query accessToken -o tsv
if (-not $token) { throw 'Could not get an Azure SQL access token. Run this step under AzureCLI@2.' }

function Invoke-Script([string] $path, [switch] $Snapshot) {
    Write-Host "Running $(Split-Path $path -Leaf)"
    $text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    if ($Snapshot) {
        # The snapshot is an SSMS "script the whole database" export (UTF-16): it begins with
        # USE [master] / CREATE DATABASE ... FILENAME = N'C:\...' / ~40 ALTER DATABASE SET options,
        # none of which Azure SQL accepts (the database already exists), and ends with
        # USE [master] / ALTER DATABASE ... SET READ_WRITE. Keep only what lies between the first
        # USE of the application database and the last USE [master].
        $first = [regex]::Match($text, '(?im)^\s*USE\s+\[(?!master\])[^\]]+\]\s*;?\s*$')
        if (-not $first.Success) { throw "Snapshot $path has no USE [<database>] marker; is the format unchanged?" }
        $text = $text.Substring($first.Index + $first.Length)
        $last = [regex]::Matches($text, '(?im)^\s*USE\s+\[master\]\s*;?\s*$') | Select-Object -Last 1
        if ($last) { $text = $text.Substring(0, $last.Index) }
    }
    $text = $text -replace '(?im)^\s*USE\s+\[[^\]]+\]\s*;?\s*$', ''
    Invoke-Sqlcmd -ServerInstance $server -Database $DatabaseName -AccessToken $token `
        -Query $text -QueryTimeout 600 -ErrorAction Stop
}

$ruleName = $null
try {
    if ($OpenFirewall) {
        if (-not $ResourceGroup) { throw '-ResourceGroup is required with -OpenFirewall.' }
        $ip = (Invoke-RestMethod -Uri 'https://api.ipify.org').Trim()
        $ruleName = "pipeline-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
        Write-Host "Opening SQL firewall for $ip as $ruleName"
        az sql server firewall-rule create -g $ResourceGroup -s $SqlServerName -n $ruleName `
            --start-ip-address $ip --end-ip-address $ip | Out-Null
        Start-Sleep -Seconds 20
    }

    $hasScan = (Invoke-Sqlcmd -ServerInstance $server -Database $DatabaseName -AccessToken $token `
        -Query "SELECT CASE WHEN OBJECT_ID('dbo.Scan','U') IS NULL THEN 0 ELSE 1 END AS HasScan").HasScan
    if ($hasScan -eq 0) {
        Write-Host 'Empty database: applying the snapshot script first.'
        Invoke-Script (Join-Path $SchemaRoot $SnapshotScript) -Snapshot
    }

    $incrementals = Get-ChildItem -LiteralPath (Join-Path $SchemaRoot 'Migration') -Recurse -Filter 'ATR *.sql' |
        ForEach-Object {
            if ($_.Name -match '^ATR (\d+)\.(\d+)\b') {
                [pscustomobject]@{ Version = [version]"$($Matches[1]).$($Matches[2])"; Path = $_.FullName }
            }
        } |
        Where-Object { $_.Version.Minor -ge $FirstIncrementalMinor } |
        Sort-Object Version

    foreach ($script in $incrementals) { Invoke-Script $script.Path }
    Write-Host "Done: $($incrementals.Count) incremental script(s) applied."

    if ($AppIdentityName) {
        if (-not $AppIdentityClientId) { throw '-AppIdentityClientId is required with -AppIdentityName.' }
        if ($AppIdentityName -notmatch '^[A-Za-z0-9_-]+$') { throw "Unexpected identity name '$AppIdentityName'." }
        [void][guid]::Parse($AppIdentityClientId)   # validates before it is put into SQL text
        Write-Host "Ensuring database user [$AppIdentityName] (db_owner)."
        $sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$AppIdentityName')
BEGIN
    DECLARE @sid varbinary(16) = CONVERT(varbinary(16), CAST('$AppIdentityClientId' AS uniqueidentifier));
    DECLARE @sql nvarchar(max) = N'CREATE USER [$AppIdentityName] WITH SID = ' + CONVERT(nvarchar(34), @sid, 1) + N', TYPE = E';
    EXEC (@sql);
END
IF IS_ROLEMEMBER('db_owner', N'$AppIdentityName') <> 1
    ALTER ROLE db_owner ADD MEMBER [$AppIdentityName];
"@
        Invoke-Sqlcmd -ServerInstance $server -Database $DatabaseName -AccessToken $token `
            -Query $sql -QueryTimeout 120 -ErrorAction Stop
    }
}
finally {
    if ($ruleName) {
        Write-Host "Removing SQL firewall rule $ruleName"
        az sql server firewall-rule delete -g $ResourceGroup -s $SqlServerName -n $ruleName | Out-Null
    }
}
