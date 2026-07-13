# World object spawning

## Status

NPC and idle monster rendering is validated in the Epic 7.3 client. Objects enter and leave the
client view while the player moves. Monster AI, movement, combat, death, drops and respawn are not
implemented yet.

The generated monster catalog contains:

- 3,973 compatible spawn areas
- 43,443 idle monster instances
- 2,457 distinct client-compatible monster resource IDs

## Visibility model

The client announces a region size of 180 and a visible radius of three regions. The server therefore
uses a circular 540-unit view around the player's current position. Sending an enter packet for an
object farther away is unsafe because the client can discard it while the server still considers the
object visible.

`NpcSpawnService` and `MonsterSpawnService` build immutable `SpatialIndex<T>` grids at startup with a
540-unit cell size. A movement sync visits only the cells intersecting the view circle and applies an
exact squared-distance test. Per-client dictionaries retain the world-object handle assigned to each
visible NPC or monster. `SpawnedObjectSet` sends leave packets only for objects no longer returned by
the spatial query.

NPC database loading projects only `Id`, `X`, `Y`, `Z`, `Hp`, `Level` and `RaceId`. Monster loading
first derives the IDs referenced by the catalog, asks PostgreSQL only for those rows, and projects only
`Id`, `Hp`, `Level` and `Race`. Monster instances are readonly value records to avoid one managed-object
allocation per catalog instance.

Visibility is synchronized on world entry and on client move, region-update and location-change
packets. Returning to the lobby clears both per-client visible-object dictionaries.

## Packets

Both NPCs and monsters use `TS_SC_ENTER` (`3`) and `TS_SC_LEAVE` (`9`). The common creature payload
contains the handle, position, layer, HP, level and race.

NPC enter packets are 72 bytes. `npc_id` uses the 8-byte randomized integer layout:

```text
0000 | high 16 bits | 0000 | low 16 bits
```

Monster enter packets are 73 bytes. `monster_id` uses the same layout after `ScrambledInt.Encode`,
followed by `is_tamed = 0`. The ID sent to the client must be `MonsterResource.id`. A name or location
code such as `180009` is not interchangeable with the actual resource ID such as `150009`; the client
accepts the object but cannot render it.

`TS_CS_MONSTER_RECOGNIZE` (`517`) is accepted without a response for idle monsters.

## Monster catalog

`DevConsole/monster-spawns.73.json` contains the `MonsterSpawnCatalog` document. DevConsole
deserializes that section directly with `System.Text.Json` instead of routing tens of thousands of
array keys through `IConfiguration`. The development-only `MonsterSpawns` section remains the fallback
when the catalog file is absent. In the local smoke test, this reduced startup through monster indexing
from roughly 35 seconds to 2.58 seconds.

The catalog combines the 9.4 server data available locally with the actual Epic 7.3 client resources:

1. NFS boxes provide exact rectangular spawn areas and `mob(groupId, box)` calls.
2. `monster_respawn.lua` provides normal, rare, raid and raid-rare populations and densities.
3. Counts use the official rounded `area / 130000 * density` calculation with a minimum of one.
4. `db_monster.rdb` provides the valid Epic 7.3 `MonsterResource.id` set after scrambled-ID decoding.
5. Populations absent from the client resource set are excluded.

This is the complete compatible catalog derivable from the available sources. It is not guaranteed to
be a byte-for-byte copy of an original Epic 7.3 server database because the available map and Lua
sources are from 9.4.

Regenerate it with:

```powershell
.\tools\Import-MonsterSpawns.ps1 `
  -NfsDirectory '<9.4 NewMap directory>' `
  -MonsterRespawnLuaPath '<decompressed monster_respawn.lua>' `
  -ClientMonsterRdbPath '<7.3 db_monster.rdb>' `
  -OutputPath 'DevConsole\monster-spawns.73.json'
```

The importer validates NFS and RDB record alignment, decodes the scrambled RDB IDs, reports unmapped
spawn groups and writes catalog metadata with all final counts.

## Runtime diagnostics

Successful startup includes logs equivalent to:

```text
Loaded and indexed <count> NPCs
Loaded 43443 monster instances from 0 spawn points and 3973 official areas (<count> monster resources)
```

Visibility synchronization is silent during normal movement. Startup logs retain indexed object and
catalog counts, while synchronization failures are still reported as errors.
