# PostgreSQL operations requested in CkEncmJM; see exploitation-postgresql.md.
# No game rule is defined here. Official data loaders use Arcadia/Telecaster,
# e.g. Game/Community/PartyLoader.cpp:264-276; credentials stay out of arguments/logs.
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot '../DevConsole/appsettings.json'),
    [string]$BackupRoot = 'A:\Rappelz Kiff\backups',
    [string]$PgDumpPath = 'C:\Program Files\PostgreSQL\18\bin\pg_dump.exe',
    [string]$PgRestorePath = 'C:\Program Files\PostgreSQL\18\bin\pg_restore.exe',
    [ValidateRange(1, 3650)][int]$RetentionDays = 14
)
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($BackupRoot).TrimEnd('\', '/')
if (-not $PSCmdlet.ShouldProcess($target, "Back up Arcadia, Telecaster and auth; rotate archives older than $RetentionDays days")) { return }
foreach ($toolPath in @($PgDumpPath, $PgRestorePath)) {
    if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) { throw "Missing PostgreSQL tool: $toolPath" }
}
# Suppress parse exception contents: a malformed settings file may contain secrets.
try { $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json }
catch { throw 'Cannot read database settings (contents suppressed).' }
$dbConfig = $config.Database
if (-not $dbConfig -or -not $dbConfig.User) { throw 'Missing Database.User in settings.' }
$dbHost = if ($dbConfig.DataSource) { [string]$dbConfig.DataSource } else { 'localhost' }
$dbPort = if ($dbConfig.Port) { [string]$dbConfig.Port } else { '5432' }
$connection = @('--host', $dbHost, '--port', $dbPort, '--username', [string]$dbConfig.User, '--no-password')
$previousPassword = [Environment]::GetEnvironmentVariable('PGPASSWORD', 'Process')
$lock = $null
$partial = $null
try {
    if ($null -ne $dbConfig.Password) { $env:PGPASSWORD = [string]$dbConfig.Password }
    [IO.Directory]::CreateDirectory($target) | Out-Null
    $lockPath = Join-Path $target '.navislamia-backup.lock'
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    foreach ($database in @('Arcadia', 'Telecaster', 'auth')) {
        $archive = Join-Path $target "$database-$stamp.dump"
        $partial = "$archive.partial"
        & $PgDumpPath @connection '--format=custom' '--file' $partial $database 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "pg_dump failed for $database (exit $LASTEXITCODE)." }
        if (-not (Test-Path -LiteralPath $partial) -or (Get-Item -LiteralPath $partial).Length -eq 0) {
            throw "Empty or missing archive for $database."
        }
        & $PgRestorePath '--list' $partial 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Invalid PostgreSQL archive for $database (exit $LASTEXITCODE)." }
        # A finished archive is published only after successful creation and catalogue reading.
        Move-Item -LiteralPath $partial -Destination $archive
        $partial = $null
        $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        [IO.File]::WriteAllText("$archive.sha256", "$hash  $([IO.Path]::GetFileName($archive))`r`n")
        Write-Output "Saved $database -> $archive"
    }
    # Rotate only after ALL three backups succeeded. Only this script's dated files
    # directly inside the resolved backup directory are eligible; never recurse.
    $cutoff = [DateTime]::UtcNow.AddDays(-$RetentionDays)
    foreach ($file in Get-ChildItem -LiteralPath $target -File) {
        if ($file.Name -notmatch '^(Arcadia|Telecaster|auth)-[0-9]{8}T[0-9]{9}Z\.dump$' -or $file.LastWriteTimeUtc -ge $cutoff) { continue }
        if (-not [string]::Equals($file.DirectoryName.TrimEnd('\', '/'), $target, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Rotation path escaped the intended backup directory.'
        }
        Remove-Item -LiteralPath $file.FullName
        $checksum = "$($file.FullName).sha256"
        if (Test-Path -LiteralPath $checksum -PathType Leaf) { Remove-Item -LiteralPath $checksum }
        Write-Output "Removed expired archive $($file.Name)"
    }
}
finally {
    try {
        if ($partial -and (Test-Path -LiteralPath $partial -PathType Leaf)) { Remove-Item -LiteralPath $partial }
    }
    finally {
        try { if ($null -ne $lock) { $lock.Dispose() } }
        finally { [Environment]::SetEnvironmentVariable('PGPASSWORD', $previousPassword, 'Process') }
    }
}
