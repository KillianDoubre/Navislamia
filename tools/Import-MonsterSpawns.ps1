param(
    [Parameter(Mandatory = $true)]
    [string]$NfsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$MonsterRespawnLuaPath,

    [Parameter(Mandatory = $true)]
    [string]$ClientMonsterRdbPath,

    [string]$OutputPath = "DevConsole/monster-spawns.73.json"
)

$ErrorActionPreference = "Stop"
$culture = [Globalization.CultureInfo]::InvariantCulture

function Read-Int32([byte[]]$Bytes, [int]$Offset) {
    if ($Offset -lt 0 -or $Offset + 4 -gt $Bytes.Length) {
        throw "Unexpected end of binary data at offset $Offset"
    }

    return [BitConverter]::ToInt32($Bytes, $Offset)
}

function Get-NumericArray([string]$Body, [string]$Name, [double[]]$Default) {
    $match = [regex]::Match($Body, "(?m)^\s*$Name\s*=\s*\{([^}]*)\}")
    if (-not $match.Success) {
        return @($Default)
    }

    return @([regex]::Matches($match.Groups[1].Value, '-?\d+(?:\.\d+)?') | ForEach-Object {
        [double]::Parse($_.Value, $culture)
    })
}

function New-ScrambledDecodeMap {
    $encode = New-Object byte[] 32
    0..31 | ForEach-Object { $encode[$_] = [byte]$_ }

    $j = 3
    for ($i = 0; $i -lt 32; $i++) {
        $old = $encode[$i]
        $encode[$i] = $encode[$j]
        $encode[$j] = $old
        $j = ($j + $i + 3) % 32
    }

    $decode = New-Object byte[] 32
    0..31 | ForEach-Object { $decode[$encode[$_]] = [byte]$_ }
    return $decode
}

function Decode-ScrambledId([uint32]$Value, [byte[]]$DecodeMap) {
    [uint32]$result = 0
    for ($i = 0; $i -lt 32; $i++) {
        if (($Value -band 1) -ne 0) {
            $result = $result -bor ([uint32]1 -shl $DecodeMap[$i])
        }

        $Value = $Value -shr 1
    }

    return [int]$result
}

function Read-ClientMonsterIds([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path $Path))
    if ($bytes.Length -lt 132) {
        throw "Invalid db_monster.rdb: file is too short"
    }

    $recordSize = 540
    $payloadSize = $bytes.Length - 132
    if ($payloadSize % $recordSize -ne 0) {
        throw "Invalid db_monster.rdb: payload is not aligned to $recordSize-byte records"
    }

    $count = [int]($payloadSize / $recordSize)

    $decodeMap = New-ScrambledDecodeMap
    $ids = [Collections.Generic.HashSet[int]]::new()
    for ($i = 0; $i -lt $count; $i++) {
        $encoded = [BitConverter]::ToUInt32($bytes, 132 + $i * $recordSize)
        [void]$ids.Add((Decode-ScrambledId $encoded $decodeMap))
    }

    return $ids
}

function Read-RespawnGroups([string]$Path) {
    $lua = [IO.File]::ReadAllText((Resolve-Path $Path))
    $branchPattern = [regex]'(?m)^\s*(?:if|elseif)\s+ID\s*==\s*(\d+)\s+then'
    $branches = $branchPattern.Matches($lua)
    $groups = @{}

    $defaultDensity = [double[]](0.54, 0.36, 0.27, 0.2, 0.15, 0.1)
    $defaultRareCount = [double[]](1, 1, 1, 1)
    $defaultRaidDensity = [double[]](0.94, 0.52, 0.34, 0.2, 0.17, 0.02)

    for ($i = 0; $i -lt $branches.Count; $i++) {
        $id = [int]$branches[$i].Groups[1].Value
        $start = $branches[$i].Index + $branches[$i].Length
        $end = if ($i + 1 -lt $branches.Count) { $branches[$i + 1].Index } else { $lua.Length }
        $body = $lua.Substring($start, $end - $start)

        $groups[$id] = [pscustomobject]@{
            Monsters = Get-NumericArray $body "monster_ID" ([double[]](0, 0, 0, 0, 0, 0))
            Density = Get-NumericArray $body "density" $defaultDensity
            Rare = Get-NumericArray $body "Raremob_ID" ([double[]](0, 0, 0, 0))
            RareCount = Get-NumericArray $body "Raremob_count" $defaultRareCount
            Raid = Get-NumericArray $body "Raidmob_ID" ([double[]](0, 0, 0, 0, 0, 0))
            RaidDensity = Get-NumericArray $body "Raidmob_density" $defaultRaidDensity
            RaidRare = Get-NumericArray $body "Raid_Raremob_ID" ([double[]](0, 0, 0, 0))
            RaidRareCount = Get-NumericArray $body "Raid_Raremob_count" $defaultRareCount
        }
    }

    return $groups
}

function Add-Population(
    [Collections.Generic.Dictionary[int, int]]$Populations,
    [Collections.Generic.HashSet[int]]$ClientIds,
    [int]$MonsterId,
    [int]$Count
) {
    if ($MonsterId -eq 0 -or $Count -le 0 -or -not $ClientIds.Contains($MonsterId)) {
        return
    }

    if ($Populations.ContainsKey($MonsterId)) {
        $Populations[$MonsterId] += $Count
    } else {
        $Populations.Add($MonsterId, $Count)
    }
}

function Add-DensityPopulations(
    [Collections.Generic.Dictionary[int, int]]$Populations,
    [Collections.Generic.HashSet[int]]$ClientIds,
    [double[]]$Ids,
    [double[]]$Densities,
    [double]$Area
) {
    for ($i = 0; $i -lt $Ids.Count; $i++) {
        $id = [int]$Ids[$i]
        $density = if ($i -lt $Densities.Count) { $Densities[$i] } else { 0 }
        $count = [int][Math]::Floor(($Area / 130000) * $density + 0.5)
        if ($id -ne 0 -and $count -lt 1) {
            $count = 1
        }

        Add-Population $Populations $ClientIds $id $count
    }
}

function Add-FixedPopulations(
    [Collections.Generic.Dictionary[int, int]]$Populations,
    [Collections.Generic.HashSet[int]]$ClientIds,
    [double[]]$Ids,
    [double[]]$Counts
) {
    for ($i = 0; $i -lt $Ids.Count; $i++) {
        $count = if ($i -lt $Counts.Count) { [int]$Counts[$i] } else { 1 }
        Add-Population $Populations $ClientIds ([int]$Ids[$i]) $count
    }
}

$clientIds = Read-ClientMonsterIds $ClientMonsterRdbPath
$groups = Read-RespawnGroups $MonsterRespawnLuaPath
$areas = [Collections.Generic.List[object]]::new()
$unmappedGroups = [Collections.Generic.HashSet[int]]::new()
$calledGroups = [Collections.Generic.HashSet[int]]::new()
$compatibleMonsterIds = [Collections.Generic.HashSet[int]]::new()
$instanceCount = 0

Get-ChildItem (Resolve-Path $NfsDirectory) -Filter *.nfs | Sort-Object Name | ForEach-Object {
    $mapMatch = [regex]::Match($_.Name, '(?i)^m(\d{3})_(\d{3})\.nfs$')
    if (-not $mapMatch.Success) {
        return
    }

    $tileX = [int]$mapMatch.Groups[1].Value
    $tileY = [int]$mapMatch.Groups[2].Value
    $bytes = [IO.File]::ReadAllBytes($_.FullName)
    if ($bytes.Length -lt 44 -or [Text.Encoding]::ASCII.GetString($bytes, 0, 14) -ne "nFlavor Script") {
        return
    }

    $boxCount = Read-Int32 $bytes 32
    $boxes = [Collections.Generic.List[object]]::new()
    $offset = 36
    for ($i = 0; $i -lt $boxCount; $i++) {
        $left = Read-Int32 $bytes $offset
        $top = Read-Int32 $bytes ($offset + 4)
        $right = Read-Int32 $bytes ($offset + 8)
        $bottom = Read-Int32 $bytes ($offset + 12)
        $nameLength = Read-Int32 $bytes ($offset + 16)
        $offset += 20 + $nameLength

        $boxes.Add([pscustomobject]@{
            Left = ($tileX * 336 + $left) * 48
            Top = ($tileY * 336 + $top) * 48
            Right = ($tileX * 336 + $right) * 48
            Bottom = ($tileY * 336 + $bottom) * 48
        })
    }

    $scriptBoxCount = Read-Int32 $bytes $offset
    $offset += 4
    for ($i = 0; $i -lt $scriptBoxCount; $i++) {
        $boxIndex = Read-Int32 $bytes $offset
        $scriptCount = Read-Int32 $bytes ($offset + 4)
        $offset += 8

        for ($scriptIndex = 0; $scriptIndex -lt $scriptCount; $scriptIndex++) {
            $null = Read-Int32 $bytes $offset
            $scriptLength = Read-Int32 $bytes ($offset + 4)
            $offset += 8
            $script = [Text.Encoding]::ASCII.GetString($bytes, $offset, $scriptLength)
            $offset += $scriptLength

            $mobCall = [regex]::Match($script, '^\s*mob\s*\(\s*(\d+)')
            if (-not $mobCall.Success -or $boxIndex -lt 0 -or $boxIndex -ge $boxes.Count) {
                continue
            }

            $groupId = [int]$mobCall.Groups[1].Value
            [void]$calledGroups.Add($groupId)
            if (-not $groups.ContainsKey($groupId)) {
                [void]$unmappedGroups.Add($groupId)
                continue
            }

            $box = $boxes[$boxIndex]
            $group = $groups[$groupId]
            $areaSize = [Math]::Abs(($box.Right - $box.Left) * ($box.Bottom - $box.Top))
            $populations = [Collections.Generic.Dictionary[int, int]]::new()

            Add-DensityPopulations $populations $clientIds $group.Monsters $group.Density $areaSize
            Add-FixedPopulations $populations $clientIds $group.Rare $group.RareCount
            Add-DensityPopulations $populations $clientIds $group.Raid $group.RaidDensity $areaSize
            Add-FixedPopulations $populations $clientIds $group.RaidRare $group.RaidRareCount

            if ($populations.Count -eq 0) {
                continue
            }

            $monsters = @($populations.GetEnumerator() | Sort-Object Key | ForEach-Object {
                [void]$compatibleMonsterIds.Add($_.Key)
                $instanceCount += $_.Value
                [ordered]@{ ResourceId = $_.Key; Count = $_.Value }
            })

            $areas.Add([ordered]@{
                Map = $_.Name
                SpawnGroupId = $groupId
                Left = $box.Left
                Top = $box.Top
                Right = $box.Right
                Bottom = $box.Bottom
                Monsters = $monsters
            })
        }
    }
}

$catalog = [ordered]@{
    Metadata = [ordered]@{
        ClientEpic = "7.3"
        Source = "9.4 NFS rectangles and monster_respawn.lua, filtered by the client db_monster.rdb IDs"
        AreaCount = $areas.Count
        InstanceCount = $instanceCount
        CompatibleMonsterIdCount = $compatibleMonsterIds.Count
        CalledSpawnGroupCount = $calledGroups.Count
        UnmappedSpawnGroups = @($unmappedGroups | Sort-Object)
    }
    MonsterSpawnCatalog = [ordered]@{
        Spawns = @()
        Areas = $areas
    }
}

if (-not [IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path (Get-Location) $OutputPath
}

$parent = Split-Path $OutputPath -Parent
if (-not (Test-Path $parent)) {
    New-Item -ItemType Directory -Path $parent | Out-Null
}

$catalog | ConvertTo-Json -Depth 8 -Compress | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host "Generated $($areas.Count) compatible areas / $instanceCount instances / $($compatibleMonsterIds.Count) monster IDs"
Write-Host "Output: $OutputPath"
