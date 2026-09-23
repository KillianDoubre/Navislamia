<#
.SYNOPSIS
Imports every mappable scalar column of Arcadia.StateResource (SQL Server 9.4) into the Postgres
Arcadia."StateResources" table.

.DESCRIPTION
The original insert only carried Id, EffectType and Values: every other scalar column was a NOT NULL
literal and read 0/false for all 1,949 rows — the same trap Import-SkillResourceColumns.ps1 fixed for
skills. The first casualty was TM_CS_REQUEST_REMOVE_STATE (408): its guard reads the EraseOnRequest bit
(32) of state_time_type, which was 0 everywhere, so every cancellation was refused.

EF property names are mapped to source columns by introspection, every pair that cannot be matched is
reported, and the script refuses to run if a required column is unmatched. The source is the CSV export of
the 9.4 database (tools/Export-SqlServerData.ps1), loaded whole into a temporary table.

Run -WhatIf to see the mapping without touching the database.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SourceCsv = (Join-Path (Split-Path -Parent $PSScriptRoot) 'data\sqlserver\Arcadia\StateResource.csv'),
    [string]$PgHost = 'localhost',
    [string]$PgUser = 'postgres',
    # Read from DevConsole/appsettings.json when not given, so no credential lives in this script.
    [string]$PgPassword,
    [string]$PgDatabase = 'Arcadia',
    [string]$WorkDirectory = $env:TEMP
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $SourceCsv)) {
    throw "Missing $SourceCsv. Run tools/Export-SqlServerData.ps1 once while SQL Server is up."
}

if (-not $PgPassword) {
    $settings = Join-Path (Split-Path -Parent $PSScriptRoot) 'DevConsole\appsettings.json'
    $PgPassword = (Get-Content -Raw $settings | ConvertFrom-Json).Database.Password
}

# EF property -> source column, for the pairs the snake_case rule cannot derive.
$Overrides = @{
    'UseOnCharacter'  = 'uf_avatar'
    'UseOnSummon'     = 'uf_summon'
    'UseOnMonster'    = 'uf_monster'
    'BaseEffect'      = 'base_effect_id'
    'AmplifyPerSkill' = 'amplify_per_skl'
}

# EF properties with no counterpart in the 9.4 source; they stay at their default and are reported.
$KnownUnmapped = @()

# Mappable, but their foreign key targets StringResources, which is empty: importing any non-zero value
# violates the constraint. The client resolves state names from its own resources.
$SkipColumns = @('TextId', 'TooltipId')

# Without these the 408 guard and the harmful/buff split silently resolve to nothing.
$Required = @('StateTimeType', 'IsHarmful', 'EffectType')

function ConvertTo-SnakeCase([string]$name) {
    return [System.Text.RegularExpressions.Regex]::Replace($name, '(?<!^)([A-Z])', '_$1').ToLowerInvariant()
}

function Invoke-Psql([string]$sql, [switch]$Tuples) {
    $env:PGPASSWORD = $PgPassword
    $args = @('-h', $PgHost, '-U', $PgUser, '-d', $PgDatabase, '-v', 'ON_ERROR_STOP=1')
    if ($Tuples) { $args += @('-t', '-A') }
    $args += @('-c', $sql)
    $out = & psql @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $out" }
    return $out
}

Write-Host 'Reading column lists...' -ForegroundColor Cyan

$pgColumns = (Invoke-Psql -Tuples @"
SELECT column_name FROM information_schema.columns
WHERE table_name='StateResources'
  AND data_type IN ('integer','bigint','numeric','boolean','smallint')
  AND column_name <> 'Id'
ORDER BY column_name;
"@) | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() }

# The export's header row is the source column list.
$srcColumns = @((Get-Content -TotalCount 1 $SourceCsv) -split ',' | ForEach-Object { $_.Trim('"') })

$srcLookup = @{}
foreach ($c in $srcColumns) { $srcLookup[$c] = $true }

$mapped = @()
$unmatched = @()
foreach ($col in $pgColumns) {
    if ($SkipColumns -contains $col) { continue }
    $src = if ($Overrides.ContainsKey($col)) { $Overrides[$col] } else { ConvertTo-SnakeCase $col }
    if ($srcLookup.ContainsKey($src)) { $mapped += [pscustomobject]@{ Pg = $col; Src = $src } }
    else { $unmatched += [pscustomobject]@{ Pg = $col; Tried = $src } }
}

$nullableColumns = (Invoke-Psql -Tuples @"
SELECT column_name FROM information_schema.columns
WHERE table_name='StateResources' AND is_nullable='YES';
"@) | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() }

Write-Host ("mapped: {0}   unmatched: {1}" -f $mapped.Count, $unmatched.Count) -ForegroundColor Green
if ($unmatched.Count -gt 0) {
    Write-Host 'Unmatched (left at their default):' -ForegroundColor Yellow
    $unmatched | ForEach-Object {
        $known = if ($KnownUnmapped -contains $_.Pg) { ' (known: absent from the 9.4 source)' } else { '' }
        Write-Host ("  {0} -> tried '{1}'{2}" -f $_.Pg, $_.Tried, $known)
    }
    $surprises = $unmatched | Where-Object { $KnownUnmapped -notcontains $_.Pg }
    if ($surprises) {
        throw "Unexpected unmapped column(s): $(($surprises.Pg) -join ', '). Add an override or list them in `$KnownUnmapped."
    }
}

$missingRequired = $Required | Where-Object { $mapped.Pg -notcontains $_ }
if ($missingRequired) {
    throw "Required column(s) unmapped, refusing to run: $($missingRequired -join ', ')"
}

if ($WhatIfPreference) {
    Write-Host 'WhatIf: mapping only, nothing written.' -ForegroundColor Cyan
    $mapped | ForEach-Object { Write-Host ("  {0} <- {1}" -f $_.Pg, $_.Src) }
    return
}

# Every source column as text, in header order, so \copy takes the export as it is.
$tempCols = ($srcColumns | ForEach-Object { "`"$_`" text" }) -join ', '
$csv = (Resolve-Path $SourceCsv).Path -replace '\\', '/'

# booleans arrive as 0/1 and need an explicit cast
$boolColumns = (Invoke-Psql -Tuples @"
SELECT column_name FROM information_schema.columns
WHERE table_name='StateResources' AND data_type='boolean';
"@) | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() }

# StateTimeType is a [Flags] enum on `short`, stored as smallint, while the source is an int: state 201085
# carries 33150, bit 15 set, above every declared flag. Its 16-bit pattern is stored as is (two's
# complement), so every flag bit survives the `&` test instead of the whole import failing on the range.
$smallintColumns = (Invoke-Psql -Tuples @"
SELECT column_name FROM information_schema.columns
WHERE table_name='StateResources' AND data_type='smallint';
"@) | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() }

$setParts = foreach ($m in $mapped) {
    $source = "NULLIF(v.`"$($m.Src)`", '')::numeric"
    if ($smallintColumns -contains $m.Pg) {
        "`"$($m.Pg)`" = (CASE WHEN $source > 32767 THEN $source - 65536 ELSE $source END)"
    }
    elseif ($boolColumns -contains $m.Pg) {
        "`"$($m.Pg)`" = ($source <> 0)"
    }
    elseif ($nullableColumns -contains $m.Pg) {
        # A nullable numeric column here is a foreign key id: 0 means "none".
        "`"$($m.Pg)`" = NULLIF($source, 0)"
    }
    else {
        "`"$($m.Pg)`" = $source"
    }
}
$setList = $setParts -join ', '

$sqlFile = Join-Path $WorkDirectory 'stateresource_import.sql'
@"
CREATE TEMP TABLE st_import ($tempCols);
\copy st_import FROM '$csv' WITH (FORMAT csv, HEADER)
UPDATE "StateResources" t SET $setList FROM st_import v WHERE t."Id" = v.state_id::bigint;
"@ | Set-Content -Encoding ASCII $sqlFile

if ($PSCmdlet.ShouldProcess($PgDatabase, "update $($mapped.Count) columns of StateResources")) {
    Write-Host 'Loading into Postgres...' -ForegroundColor Cyan
    $env:PGPASSWORD = $PgPassword
    $out = & psql -h $PgHost -U $PgUser -d $PgDatabase -v ON_ERROR_STOP=1 -f $sqlFile 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql import failed: $out" }
    $out | ForEach-Object { Write-Host "  $_" }
    Write-Host 'Done.' -ForegroundColor Green
}
