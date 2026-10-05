# Standalone tests, no Pester installation or service/task mutation required.
$ErrorActionPreference = 'Stop'
$toolsRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("Navis maintenance " + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$checks = 0
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Assertion failed: $Message" }
    $script:checks++
}
function Assert-Fails([scriptblock]$Action) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert $failed 'operation must fail'
}
$initialPassword = [Environment]::GetEnvironmentVariable('PGPASSWORD', 'Process')
$fakeDump = Join-Path $testRoot 'dump tool.ps1'
$fakeRestore = Join-Path $testRoot 'restore tool.ps1'
$config = Join-Path $testRoot 'settings.json'
$secret = 'fake-test-password-do-not-log'
try {
    [IO.File]::WriteAllText($config, '{"Database":{"User":"tester","DataSource":"localhost","Port":5432,"Password":"fake-test-password-do-not-log"}}')
    [IO.File]::WriteAllText($fakeDump, @'
param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
$index = [Array]::IndexOf($Arguments, '--file')
if ($env:NAVIS_TEST_DUMP_FAIL -eq $Arguments[-1]) { $global:LASTEXITCODE = 1; return }
[IO.File]::WriteAllText($Arguments[$index + 1], 'PGDMP fake archive')
$global:LASTEXITCODE = 0
'@)
    [IO.File]::WriteAllText($fakeRestore, @'
param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
$global:LASTEXITCODE = if ($env:NAVIS_TEST_RESTORE_FAIL) { 1 } else { 0 }
'@)
    $common = @{ ConfigPath=$config; PgDumpPath=$fakeDump; PgRestorePath=$fakeRestore; RetentionDays=14 }
    $success = Join-Path $testRoot 'success'
    [IO.Directory]::CreateDirectory($success) | Out-Null
    $old = Join-Path $success 'Arcadia-20000101T000000000Z.dump'
    [IO.File]::WriteAllText($old, 'old')
    [IO.File]::WriteAllText("$old.sha256", 'old checksum')
    (Get-Item -LiteralPath $old).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-30)
    $manual = Join-Path $success 'manual-archive.dump'
    [IO.File]::WriteAllText($manual, 'manual')
    (Get-Item -LiteralPath $manual).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-30)
    $messages = & (Join-Path $toolsRoot 'Backup-PostgreSql.ps1') @common -BackupRoot $success
    Assert ((Get-ChildItem -LiteralPath $success -Filter '*.dump').Count -eq 4) 'three archives plus manual archive'
    Assert ((Get-ChildItem -LiteralPath $success -Filter '*.sha256').Count -eq 3) 'three checksums'
    Assert (-not (Test-Path -LiteralPath $old)) 'expired archive removed'
    Assert (-not (Test-Path -LiteralPath "$old.sha256")) 'expired checksum removed'
    Assert (Test-Path -LiteralPath $manual) 'unrelated archive untouched'
    Assert (($messages -join '\n') -notmatch [regex]::Escape($secret)) 'no secret in output'
    Assert ([Environment]::GetEnvironmentVariable('PGPASSWORD','Process') -eq $initialPassword) 'password environment restored'
    foreach ($failure in @('catalogue', 'dump')) {
        $destination = Join-Path $testRoot $failure
        [IO.Directory]::CreateDirectory($destination) | Out-Null
        $retained = Join-Path $destination 'auth-20000101T000000000Z.dump'
        [IO.File]::WriteAllText($retained, 'old')
        (Get-Item -LiteralPath $retained).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-30)
        if ($failure -eq 'catalogue') { $env:NAVIS_TEST_RESTORE_FAIL = '1' }
        else { $env:NAVIS_TEST_DUMP_FAIL = 'Telecaster' }
        Assert-Fails { & (Join-Path $toolsRoot 'Backup-PostgreSql.ps1') @common -BackupRoot $destination }
        Assert (Test-Path -LiteralPath $retained) 'failure never rotates existing backups'
        Assert (@(Get-ChildItem -LiteralPath $destination -Filter '*.partial').Count -eq 0) 'failed partial removed'
        Assert ([Environment]::GetEnvironmentVariable('PGPASSWORD','Process') -eq $initialPassword) 'failure restores password environment'
        Remove-Item Env:NAVIS_TEST_RESTORE_FAIL -ErrorAction SilentlyContinue
        Remove-Item Env:NAVIS_TEST_DUMP_FAIL -ErrorAction SilentlyContinue
    }
    $preview = Join-Path $testRoot 'preview'
    & (Join-Path $toolsRoot 'Backup-PostgreSql.ps1') @common -BackupRoot $preview -WhatIf
    Assert (-not (Test-Path -LiteralPath $preview)) 'backup WhatIf creates nothing'
    function Set-Service { throw 'WhatIf attempted to change service' }
    function Register-ScheduledTask { throw 'WhatIf attempted to register task' }
    & (Join-Path $toolsRoot 'Install-PostgreSqlMaintenance.ps1') @common -BackupRoot $preview -WhatIf
    Assert (-not (Test-Path -LiteralPath $preview)) 'installer WhatIf creates nothing'
    $held = [IO.File]::Open((Join-Path $success '.navislamia-backup.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { Assert-Fails { & (Join-Path $toolsRoot 'Backup-PostgreSql.ps1') @common -BackupRoot $success } }
    finally { $held.Dispose() }
    Write-Output "$checks maintenance assertions passed."
}
finally {
    [Environment]::SetEnvironmentVariable('PGPASSWORD', $initialPassword, 'Process')
    Remove-Item Env:NAVIS_TEST_RESTORE_FAIL -ErrorAction SilentlyContinue
    Remove-Item Env:NAVIS_TEST_DUMP_FAIL -ErrorAction SilentlyContinue
    # The verified test directory contains files and one-level child directories only.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^Navis maintenance [a-f0-9]{32}$') {
        throw 'Refusing test cleanup outside its temporary directory.'
    }
    foreach ($directory in Get-ChildItem -LiteralPath $resolved -Directory) {
        Get-ChildItem -LiteralPath $directory.FullName -File | Remove-Item -Force
        Remove-Item -LiteralPath $directory.FullName
    }
    Get-ChildItem -LiteralPath $resolved -File | Remove-Item -Force
    Remove-Item -LiteralPath $resolved
}
