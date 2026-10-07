# World object spawning

## Status

NPC and idle monster rendering is validated in the Epic 7.3 client. Objects enter and leave the
client view while the player moves. Monster AI, movement, combat, death, drops and respawn are not
implemented yet.

The generated monster catalog contains:

- 4,082 compatible spawn areas
- 40,566 idle monster instances
- 2,638 distinct client-compatible monster resource IDs

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

The catalog combines the Epic 7 era sources with the actual Epic 7.3 client resources:

1. The client's own NFS boxes (`DevConsole/Maps/*.nfs`) provide the rectangular spawn areas and the
   `mob(groupId, #box)` calls.
2. The Epic 7 `monster_respawn.lua` (`Epic 7 Part 4/trunk`) provides normal, rare, raid and raid-rare
   populations and densities. A branch gated on an event toggle (`ID == 1038 and rangifer_on == 1`,
   `halloween_on`) is not matched, and inside a branch the first `monster_ID` is the event-off one.
3. Counts use the official rounded `area / 130000 * density` calculation with a minimum of one.
4. `db_monster.rdb` provides the valid Epic 7.3 `MonsterResource.id` set after scrambled-ID decoding.
5. Populations absent from the client resource set are excluded.

**A box is placed the way the official `MapLoader::LoadRegionInfo` places it: `raw × TILE_LENGTH + map
index × 16128`, with `TILE_LENGTH = 42`** (read from `TerrainSeamlessWorld.cfg`). Until 2026-10-06 the
importer used `(map index × 336 + raw) × 48`: the same map origin, but every box stretched by 8/7 from its
map's corner, up to ~2 300 units off — monsters stood on the Deva and Asura start points of the trainee
island, and 48.6 % of the points sampled in the boxes fell inside an `.nfa` obstacle, against 8.8 % at the
right scale. `MonsterSpawnCatalogTests` holds the alignment and the start points.

The catalog was first built from the 9.4 NFS and Lua; the boxes are the same in the 7.3 client for 3,960
of them, and the Epic 7 Lua mostly adds a monster or two to a group the 9.4 one had trimmed.

**Not modelled**: the official server only registers `raid_respawn` populations in `g_vRaidMonsterRespawnInfo`,
which `GameContent::AddRespawnObjectToWorld` never reads; the importer still adds them to the open-world
area like the field ones (unchanged by this fix).

**Auto Traps.** The rare monsters whose id is below 310000 and ends in 41, 43, 44, 46 or 49 (92 ids, only ever in
`Raremob_ID`) are the anti-bot "Auto Traps": hitting one brings state 5997 for 24 h (defences, attack and speed cut)
and `set_auto_user` (`ETC_run_monster_skill.lua`). The Lua respawns them only when `game.use_auto_trap` is 1, which
the official `GameRule::bUseAutoTrap` defaults to. The importer keeps them (3 907 instances in 2 239 areas) and
`MonsterSpawns:UseAutoTrap` (default true, the official value) decides at load (`MonsterInstanceFactory.IsAutoTrap`);
`appsettings.Dev.json` turns it off.

Regenerate it with:

```powershell
.\tools\Import-MonsterSpawns.ps1 `
  -NfsDirectory 'DevConsole\Maps' `
  -MonsterRespawnLuaPath '<Epic 7 Part 4>	runk\monster_respawn.lua' `
  -ClientMonsterRdbPath '<7.3 db_monster.rdb>' `
  -OutputPath 'DevConsole\monster-spawns.73.json'
```

The importer validates NFS and RDB record alignment, decodes the scrambled RDB IDs, reports unmapped
spawn groups and writes catalog metadata with all final counts.

## Runtime diagnostics

Successful startup includes logs equivalent to:

```text
Loaded and indexed <count> NPCs
Loaded 40566 monster instances from 0 spawn points and 4082 official areas (<count> monster resources)
```

Visibility synchronization is silent during normal movement. Startup logs retain indexed object and
catalog counts, while synchronization failures are still reported as errors.
