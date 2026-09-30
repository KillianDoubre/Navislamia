<#
.SYNOPSIS
Loads the crafting tables — Arcadia."MixResources" and Arcadia."EnhanceResources" — from a CSV export.

.DESCRIPTION
The crafting engine (docs/packet-specs/socle-artisanat-ressources.md §14) reads both tables, which the
repository creates empty. The source is the CSV export of the 9.4 database (tools/Export-SqlServerData.ps1):
MixResource.csv (4 155 rules) and EnhanceResource.csv (291 rows, 111 enhance_id). The Epic 7 tables, closer
to the 7.3 client, load the same way: -SourceDirectory data\epic7 (3 965 rules, 261 rows).

Each table is replaced whole, in one transaction: a failed load leaves the previous content in place.

Two traps of the former MigrateDatabase path are deliberately not reproduced (fiche §14 point 6):
  * fail_result is copied as it is — the old path forced it to 1, while the 9.4 data holds 0 to 4;
  * need_item is copied as it is — the old path set RequiredItemId to NULL when the item was missing.
    Here a need_item absent from "ItemResources" stops the import with the list of missing ids.

MixResources columns are matched to the CSV by name, underscores and case aside (Sub01Type01 <- sub01_type_01);
the script refuses to run if one is unmatched. EnhanceResources.Percentage is percentage_1..25, the chance to go
from +i to +i+1 at index i (migration AllowTwentyFiveEnhancePercentages raised the cap from 20 to 25).

Run -WhatIf to see the mapping without touching the database.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SourceDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'data\sqlserver\Arcadia'),
    [string]$PgHost = 'localhost',
    [string]$PgUser = 'postgres',
    [string]$PgPassword,
    [string]$PgDatabase = 'Arcadia'
)

$ErrorActionPreference = 'Stop'

$mixCsv = Join-Path $SourceDirectory 'MixResource.csv'
$enhanceCsv = Join-Path $SourceDirectory 'EnhanceResource.csv'
foreach ($file in @($mixCsv, $enhanceCsv)) {
    if (-not (Test-Path $file)) {
        throw "Missing $file. Run tools/Export-SqlServerData.ps1 once while SQL Server is up."
    }
}

if (-not $PgPassword) {
    $settings = Join-Path (Split-Path -Parent $PSScriptRoot) 'DevConsole\appsettings.json'
    $PgPassword = (Get-Content -Raw $settings | ConvertFrom-Json).Database.Password
}

function Invoke-Psql([string]$sql, [switch]$Tuples) {
    $env:PGPASSWORD = $PgPassword
    $arguments = @('-h', $PgHost, '-U', $PgUser, '-d', $PgDatabase, '-v', 'ON_ERROR_STOP=1')
    if ($Tuples) { $arguments += @('-t', '-A') }
    $arguments += @('-c', $sql)
    $out = & psql @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $out" }
    return $out
}

function Invoke-PsqlFile([string]$path) {
    $env:PGPASSWORD = $PgPassword
    $out = & psql -h $PgHost -U $PgUser -d $PgDatabase -v ON_ERROR_STOP=1 -f $path 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $out" }
    return $out
}

function Get-CsvHeader([string]$path) {
    return @((Get-Content -TotalCount 1 $path) -split ',' | ForEach-Object { $_.Trim('"') })
}

function Get-Key([string]$name) { return ($name -replace '_', '').ToLowerInvariant() }

# --- MixResources: every integer column, matched by name ------------------------------------------------
$pgMixColumns = (Invoke-Psql -Tuples @"
SELECT column_name FROM information_schema.columns
WHERE table_name='MixResources' AND data_type IN ('integer','bigint') AND column_name <> 'Id'
ORDER BY ordinal_position;
"@) | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() }

$mixHeader = Get-CsvHeader $mixCsv
$mixSource = @{}
foreach ($column in $mixHeader) { $mixSource[(Get-Key $column)] = $column }

$mixMapping = @()
$unmatched = @()
foreach ($column in $pgMixColumns) {
    $key = Get-Key $column
    if ($mixSource.ContainsKey($key)) { $mixMapping += [pscustomobject]@{ Pg = $column; Src = $mixSource[$key] } }
    else { $unmatched += $column }
}

Write-Host ("MixResources: {0} columns mapped" -f $mixMapping.Count) -ForegroundColor Green
if ($unmatched) { throw "MixResources column(s) without a CSV source: $($unmatched -join ', ')" }

$percentages = 1..25 | ForEach-Object { "percentage_$_" }
$enhanceHeader = Get-CsvHeader $enhanceCsv
# The Epic 7 table (data/epic7, tools/rdu.py) stops at percentage_20: the cap was 20 then. The columns it lacks
# are +21..+25, which it cannot reach, so they load as 0; the first twenty are required.
$missingEnhance = @('enhance_id', 'enhance_type', 'fail_result', 'max_enhance', 'local_flag', 'need_item') + $percentages[0..19] |
    Where-Object { $enhanceHeader -notcontains $_ }
if ($missingEnhance) { throw "EnhanceResource.csv lacks: $($missingEnhance -join ', ')" }

if ($WhatIfPreference) {
    $mixMapping | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host 'EnhanceResources: Id<-enhance_id, EnhanceType, FailResult, MaxEnhance, LocalFlag, RequiredItemId<-need_item, Percentage<-percentage_1..25'
    return
}

# --- One transaction: temp tables of text, checks, replace -----------------------------------------------
function Get-TempTable([string]$name, [string[]]$columns) {
    $definition = ($columns | ForEach-Object { '"' + $_ + '" text' }) -join ', '
    return "CREATE TEMP TABLE $name ($definition) ON COMMIT DROP;"
}

$mixCopyPath = (Resolve-Path $mixCsv).Path -replace '\\', '/'
$enhanceCopyPath = (Resolve-Path $enhanceCsv).Path -replace '\\', '/'

$mixInsertColumns = ($mixMapping | ForEach-Object { '"' + $_.Pg + '"' }) -join ', '
$mixSelectColumns = ($mixMapping | ForEach-Object { "NULLIF(""$($_.Src)"", '')::numeric::integer" }) -join ', '
$percentageArray = ($percentages | ForEach-Object {
    if ($enhanceHeader -contains $_) { "COALESCE(NULLIF(""$_"", '')::numeric(10,3), 0)" } else { '0' }
}) -join ', '

$script = @"
BEGIN;
$(Get-TempTable 'mix_source' $mixHeader)
\copy mix_source FROM '$mixCopyPath' WITH (FORMAT csv, HEADER)
$(Get-TempTable 'enhance_source' $enhanceHeader)
\copy enhance_source FROM '$enhanceCopyPath' WITH (FORMAT csv, HEADER)

-- enhance_type and fail_result are char(1) at the source: the Epic 7 table holds one test row (enhance_id 100)
-- whose fail_result is '-'. A row the engine cannot read is left out, not given an invented outcome.
CREATE TEMP TABLE enhance_rejected ON COMMIT DROP AS
SELECT enhance_id FROM enhance_source
WHERE enhance_type !~ '^-?[0-9]+$' OR fail_result !~ '^-?[0-9]+$';
DELETE FROM enhance_source WHERE enhance_id IN (SELECT enhance_id FROM enhance_rejected);

DO `$`$
DECLARE missing text;
BEGIN
    SELECT string_agg(DISTINCT need_item, ', ') INTO missing
    FROM enhance_source e
    WHERE NOT EXISTS (SELECT 1 FROM "ItemResources" i WHERE i."Id" = e.need_item::bigint);
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'need_item absent from ItemResources: %', missing;
    END IF;
END `$`$;

DELETE FROM "MixResources";
INSERT INTO "MixResources" ("Id", "CreatedOn", $mixInsertColumns)
SELECT id::bigint, now(), $mixSelectColumns FROM mix_source ORDER BY id::bigint;

DELETE FROM "EnhanceResources";
INSERT INTO "EnhanceResources"
    ("Id", "LocalFlag", "CreatedOn", "EnhanceType", "FailResult", "MaxEnhance", "RequiredItemId", "Percentage")
SELECT enhance_id::bigint, local_flag::integer, now(), enhance_type::integer, fail_result::integer,
       max_enhance::smallint, need_item::bigint, ARRAY[$percentageArray]
FROM enhance_source;

SELECT 'MixResources', count(*) FROM "MixResources"
UNION ALL SELECT 'EnhanceResources', count(*) FROM "EnhanceResources"
UNION ALL SELECT 'EnhanceResource rows left out (non-numeric type or fail_result): ' || coalesce(string_agg(enhance_id, ', '), 'none'), count(*) FROM enhance_rejected;
COMMIT;
"@

$sqlPath = Join-Path $env:TEMP 'navislamia-import-crafting.sql'
# psql reads a BOM as part of the first statement: write the script without one.
[System.IO.File]::WriteAllText($sqlPath, $script, (New-Object System.Text.UTF8Encoding $false))
try {
    if ($PSCmdlet.ShouldProcess("$PgDatabase", 'replace MixResources and EnhanceResources')) {
        Invoke-PsqlFile $sqlPath | Write-Host
    }
}
finally {
    Remove-Item $sqlPath -ErrorAction SilentlyContinue
}
