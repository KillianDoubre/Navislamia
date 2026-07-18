<#
.SYNOPSIS
Imports the monster AI columns from Arcadia.MonsterResource (SQL Server 9.4) into the Postgres
Arcadia."MonsterResources" table.

.DESCRIPTION
Level, Hp and Race were imported; the AI columns (FirstAttack, GroupFirstAttack, VisibleRange,
ChaseRange, AttackRange, RunSpeed) are still the NOT NULL literals from the partial insert and read 0
for all rows, which lies silently to the AI. This backfills exactly those columns, mapping the two
frozen source typos (f_fisrt_attack, f_group_first_attack) explicitly.

Run -WhatIf to see the mapping without touching the database.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SqlServer = 'localhost\SQLEXPRESS',
    [string]$SourceDatabase = 'Arcadia',
    [string]$PgHost = 'localhost',
    [string]$PgUser = 'postgres',
    [string]$PgPassword = 'Stbrice@35',
    [string]$PgDatabase = 'Arcadia',
    [string]$WorkDirectory = $env:TEMP
)

$ErrorActionPreference = 'Stop'

# Postgres column -> SQL Server source column. f_fisrt_attack keeps the source's frozen typo.
$Columns = [ordered]@{
    'FirstAttack'      = 'f_fisrt_attack'
    'GroupFirstAttack' = 'f_group_first_attack'
    'VisibleRange'     = 'visible_range'
    'ChaseRange'       = 'chase_range'
    'AttackRange'      = 'attack_range'
    'RunSpeed'         = 'run_speed'
    # Body size feeds the real attack range: GetUnitSize = size * 12 * scale.
    'Size'             = 'size'
    'Scale'            = 'scale'
}

function Invoke-Pg([string]$sql) {
    # Run through a file, not -c: PowerShell strips the double quotes that Postgres needs around the
    # PascalCase identifiers when they are passed as a native-command argument.
    $env:PGPASSWORD = $PgPassword
    $sqlFile = Join-Path $WorkDirectory ('pg-' + [guid]::NewGuid().ToString('N') + '.sql')
    [System.IO.File]::WriteAllText($sqlFile, $sql, (New-Object System.Text.UTF8Encoding($false)))
    try {
        $result = & psql -h $PgHost -U $PgUser -d $PgDatabase -v ON_ERROR_STOP=1 -t -A -f $sqlFile
        if ($LASTEXITCODE -ne 0) { throw "psql failed: $sql" }
        return $result
    }
    finally {
        Remove-Item $sqlFile -ErrorAction SilentlyContinue
    }
}

$sourceList = ($Columns.Values | ForEach-Object { $_ }) -join ', '
$query = "SET NOCOUNT ON; SELECT id, $sourceList FROM MonsterResource ORDER BY id;"

Write-Host "Reading MonsterResource ($($Columns.Count) AI columns) from $SqlServer\$SourceDatabase..."
$rows = & sqlcmd -S $SqlServer -E -C -d $SourceDatabase -h -1 -W -s '|' -Q $query |
    Where-Object { $_ -match '^\d+\|' }

if (-not $rows) { throw 'No rows read from MonsterResource.' }
Write-Host "  $($rows.Count) rows."

if ($PSCmdlet.ShouldProcess($PgDatabase, "import $($Columns.Count) AI columns for $($rows.Count) monsters")) {
    $csvPath = Join-Path $WorkDirectory 'monster-ai-columns.csv'
    $header = @('id') + ($Columns.Keys | ForEach-Object { $_ })
    # sqlcmd emitted pipe-delimited rows (-s '|'); keep the header and \copy on the same delimiter.
    $lines = @($header -join '|') + $rows
    [System.IO.File]::WriteAllLines($csvPath, $lines, (New-Object System.Text.UTF8Encoding($false)))

    # Stage every column as numeric so AttackRange (decimal(x,2), values like 0.60) is not truncated;
    # Postgres assignment-casts numeric back to the int columns (FirstAttack, ranges, RunSpeed).
    $pgColumns = ($Columns.Keys | ForEach-Object { """$_"" numeric" }) -join ', '
    $setClause = ($Columns.Keys | ForEach-Object { """$_"" = t.""$_""" }) -join ', '

    # A plain table, not TEMP: each psql call is a separate session, so a TEMP table would be gone
    # before the \copy runs. Dropped at the end.
    Invoke-Pg "DROP TABLE IF EXISTS _monster_ai_import;" | Out-Null
    Invoke-Pg "CREATE TABLE _monster_ai_import (id bigint, $pgColumns);" | Out-Null

    $env:PGPASSWORD = $PgPassword
    & psql -h $PgHost -U $PgUser -d $PgDatabase -v ON_ERROR_STOP=1 `
        -c "\copy _monster_ai_import FROM '$csvPath' WITH (FORMAT csv, HEADER true, DELIMITER '|')"
    if ($LASTEXITCODE -ne 0) { throw 'psql \copy failed.' }

    # Single-quoted segments keep the identifier quotes literal; a double-quoted here-string mangles
    # them under PowerShell's quoting rules.
    $update = 'UPDATE "MonsterResources" m SET ' + $setClause +
              ' FROM _monster_ai_import t WHERE m."Id" = t.id;'
    Invoke-Pg $update | Out-Null

    Invoke-Pg "DROP TABLE IF EXISTS _monster_ai_import;" | Out-Null
    Write-Host 'Import complete.'
}

$verify = Invoke-Pg 'SELECT count(*) FILTER (WHERE "FirstAttack" <> 0), count(DISTINCT "ChaseRange") FROM "MonsterResources";'
Write-Host "Verify (FirstAttack<>0, distinct ChaseRange): $verify"
