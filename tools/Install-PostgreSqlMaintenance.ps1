# Run explicitly from an elevated PowerShell after reviewing this script.
# CkEncmJM: this script is delivered, not executed automatically by Codex.
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot '../DevConsole/appsettings.json'),
    [string]$BackupRoot = 'A:\Rappelz Kiff\backups',
    [string]$PgDumpPath = 'C:\Program Files\PostgreSQL\18\bin\pg_dump.exe',
    [string]$PgRestorePath = 'C:\Program Files\PostgreSQL\18\bin\pg_restore.exe',
    [ValidateRange(1, 3650)][int]$RetentionDays = 14,
    [ValidatePattern('^(?:[01][0-9]|2[0-3]):[0-5][0-9]$')][string]$DailyAt = '02:30',
    [ValidateNotNullOrEmpty()][string]$TaskName = 'Navislamia-PostgreSQL-Backup'
)
$ErrorActionPreference = 'Stop'
$backupScript = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'Backup-PostgreSql.ps1'))
$configFile = [IO.Path]::GetFullPath($ConfigPath)
$target = [IO.Path]::GetFullPath($BackupRoot)
$PgDumpPath = [IO.Path]::GetFullPath($PgDumpPath)
$PgRestorePath = [IO.Path]::GetFullPath($PgRestorePath)
foreach ($file in @($backupScript, $configFile, $PgDumpPath, $PgRestorePath)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required file missing: $file" }
}
if (-not $PSCmdlet.ShouldProcess('postgresql-x64-18 and Windows Task Scheduler',
        "Set service Automatic; register $TaskName daily at $DailyAt as SYSTEM ($RetentionDays day retention)")) { return }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this installer from an elevated PowerShell.'
}
$runner = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$arguments = '-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "{0}" -ConfigPath "{1}" -BackupRoot "{2}" -PgDumpPath "{3}" -PgRestorePath "{4}" -RetentionDays {5}' -f $backupScript, $configFile, $target, $PgDumpPath, $PgRestorePath, $RetentionDays
$action = New-ScheduledTaskAction -Execute $runner -Argument $arguments -WorkingDirectory $PSScriptRoot
$at = [DateTime]::Today.Add([TimeSpan]::ParseExact($DailyAt, 'hh\:mm', [Globalization.CultureInfo]::InvariantCulture))
$trigger = New-ScheduledTaskTrigger -Daily -At $at
$taskPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$taskSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 2) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Set-Service -Name 'postgresql-x64-18' -StartupType Automatic
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $taskSettings -Description 'Navislamia: custom PostgreSQL backups (Arcadia, Telecaster, auth), verified archive catalogue and rotation.' -Force | Out-Null
Write-Output "Installed $TaskName (daily $DailyAt); postgresql-x64-18 starts automatically."
