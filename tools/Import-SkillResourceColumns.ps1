<#
.SYNOPSIS
Imports every mappable scalar column of Arcadia.SkillResource (SQL Server 9.4) into the Postgres
Arcadia."SkillResources" table.

.DESCRIPTION
The original partial insert supplied NOT NULL literals for ~80 columns, so they read 0/false for all
2,689 rows and lie silently to everything downstream. This script maps EF property names to source
column names by introspection rather than by hand, reports every pair it cannot match, and refuses to
run if a required column is unmatched.

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

# EF property -> source column, for the pairs the snake_case rule cannot derive.
$Overrides = @{
    'StateLevelPerSkill'    = 'state_level_per_skl'
    'RequiredStateId'       = 'need_state_id'
    'RequiredStateLevel'    = 'need_state_level'
    'RequiredStateExhaust'  = 'need_state_exhaust'
    'RequiredLevel'         = 'need_level'
    'RequiredHp'            = 'need_hp'
    'RequiredMp'            = 'need_mp'
    'RequiredHavoc'         = 'need_havoc'
    'RequiredHavocBurst'    = 'need_havoc_burst'
    # SkillTarget Target <- target (Misc/Target/Region/Party...); SkillRequiredTarget <- is_need_target
    # (Self/Target/Ground). Two different enums; the snake_case rule sends both to 'target'.
    'RequiredTarget'        = 'is_need_target'
    'ElementalType'         = 'elemental'
    'HatePerSkill'          = 'hate_per_skl'
    'UseOnSummon'           = 'tf_summon'
    'DescriptionId'         = 'desc_id'
    'UseWithOneHandSword'   = 'vf_one_hand_sword'
    'UseWithTwoHandSword'   = 'vf_two_hand_sword'
    'UseWithDoubleSword'    = 'vf_double_sword'
    'UseWithDagger'         = 'vf_dagger'
    'UseWithDoubleDagger'   = 'vf_double_dagger'
    'UseWithSpear'          = 'vf_spear'
    'UseWithAxe'            = 'vf_axe'
    'UseWithOneHandAxe'     = 'vf_one_hand_axe'
    'UseWithDoubleAxe'      = 'vf_double_axe'
    'UseWithOneHandMace'    = 'vf_one_hand_mace'
    'UseWithTwoHandMace'    = 'vf_two_hand_mace'
    'UseWithLightbow'       = 'vf_lightbow'
    'UseWithHeavybow'       = 'vf_heavybow'
    'UseWithCrossbow'       = 'vf_crossbow'
    'UseWithOneHandStaff'   = 'vf_one_hand_staff'
    'UseWithTwoHandStaff'   = 'vf_two_hand_staff'
    'UseWithShieldOnly'     = 'vf_shield_only'
    'UseWithWeaponNotRequired' = 'vf_is_not_need_weapon'
    'UseOnSelf'             = 'uf_self'
    'UseOnParty'            = 'uf_party'
    'UseOnGuild'            = 'uf_guild'
    'UseOnNeutral'          = 'uf_neutral'
    'UseOnPurple'           = 'uf_purple'
    'UseOnEnemy'            = 'uf_enemy'
}

# EF properties with no counterpart in the 9.4 source; they stay at their default and are reported.
$KnownUnmapped = @('SummonId', 'UpgradeIntoSkillId', 'UseOnCharacter', 'UseOnMonster')

# Mappable, but their foreign key targets StringResources, which is empty: importing any non-zero value
# violates the constraint. The client resolves skill names from its own resources, so nothing needs them.
$SkipColumns = @('TextId', 'TooltipId', 'DescriptionId')

# Without these the buff formulas silently resolve to nothing.
$Required = @(
    'CostMp', 'CostMpPerSkl', 'DelayCast', 'DelayCastPerSkl', 'DelayCommon',
    'DelayCooltime', 'DelayCooltimePerSkl', 'Target', 'IsHarmful', 'IsToggle',
    'StateSecondPerLevel', 'CastRange', 'RequiredLevel'
)

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
WHERE table_name='SkillResources'
  AND data_type IN ('integer','bigint','numeric','boolean','smallint')
  AND column_name <> 'Id'
ORDER BY column_name;
"@) | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() }

$srcColumns = @(sqlcmd -S $SqlServer -E -d $SourceDatabase -h -1 -W -w 4000 -Q `
    "SET NOCOUNT ON; SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='SkillResource';" |
    Where-Object { $_ -and $_.Trim() -and $_ -notmatch '^\(' } | ForEach-Object { $_.Trim() })

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

# A nullable column here is always a foreign key id; 0 means "none" and must not be written as 0.
$nullableColumns = (Invoke-Psql -Tuples @"
SELECT column_name FROM information_schema.columns
WHERE table_name='SkillResources' AND is_nullable='YES';
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

$csv = Join-Path $WorkDirectory 'skillresource_columns.csv'
$selectList = ($mapped | ForEach-Object {
    "CAST($($_.Src) AS varchar(32))"
}) -join " + ',' + "

Write-Host 'Exporting from SQL Server...' -ForegroundColor Cyan
$lines = sqlcmd -S $SqlServer -E -d $SourceDatabase -h -1 -W -w 8000 -Q `
    "SET NOCOUNT ON; SELECT CAST(id AS varchar) + ',' + $selectList FROM SkillResource ORDER BY id;" |
    Where-Object { $_ -match '^\d+,' }

if (-not $lines -or $lines.Count -eq 0) { throw 'Export produced no rows.' }
# PowerShell 5.1 writes a BOM with -Encoding utf8; psql \copy chokes on it.
[System.IO.File]::WriteAllLines($csv, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("exported {0} rows" -f $lines.Count) -ForegroundColor Green

$tempCols = (@('id bigint') + ($mapped | ForEach-Object { "c_$($_.Pg) text" })) -join ', '
$setList = ($mapped | ForEach-Object {
    "`"$($_.Pg)`" = NULLIF(v.c_$($_.Pg), '')::numeric"
}) -join ', '

# booleans arrive as 0/1 and need an explicit cast
$boolColumns = (Invoke-Psql -Tuples @"
SELECT column_name FROM information_schema.columns
WHERE table_name='SkillResources' AND data_type='boolean';
"@) | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() }

$setParts = foreach ($m in $mapped) {
    if ($boolColumns -contains $m.Pg) {
        "`"$($m.Pg)`" = (NULLIF(v.c_$($m.Pg), '')::numeric <> 0)"
    }
    elseif ($nullableColumns -contains $m.Pg) {
        "`"$($m.Pg)`" = NULLIF(NULLIF(v.c_$($m.Pg), '')::numeric, 0)"
    }
    else {
        "`"$($m.Pg)`" = NULLIF(v.c_$($m.Pg), '')::numeric"
    }
}
$setList = $setParts -join ', '

$sqlFile = Join-Path $WorkDirectory 'skillresource_import.sql'
@"
CREATE TEMP TABLE sr_import ($tempCols);
\copy sr_import FROM '$csv' WITH (FORMAT csv)
UPDATE "SkillResources" t SET $setList FROM sr_import v WHERE t."Id" = v.id;
"@ | Set-Content -Encoding ASCII $sqlFile

if ($PSCmdlet.ShouldProcess($PgDatabase, "update $($mapped.Count) columns of SkillResources")) {
    Write-Host 'Loading into Postgres...' -ForegroundColor Cyan
    $env:PGPASSWORD = $PgPassword
    $out = & psql -h $PgHost -U $PgUser -d $PgDatabase -v ON_ERROR_STOP=1 -f $sqlFile 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql import failed: $out" }
    $out | ForEach-Object { Write-Host "  $_" }
    Write-Host 'Done.' -ForegroundColor Green
}
