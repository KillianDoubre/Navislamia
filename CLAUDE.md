# CLAUDE.md

This file describes the repository state and the constraints that matter when changing it.

## Project

Navislamia is an open-source .NET reimplementation of the Rappelz Epic 7.3 game server. The
`AuthServer` project is a working authentication server used by a real Epic 7.3 client. The game host
is `DevConsole`.

## Commands

```powershell
dotnet restore Navislamia.sln
dotnet build Navislamia.sln -c Release
dotnet test Tests/Tests.csproj -c Release

.\start-server.ps1
.\launch-client.ps1

dotnet run --project AuthServer
dotnet run --project DevConsole
```

AuthServer must be listening before DevConsole. The solution uses the .NET 8 x64 toolchain for all
projects referencing `Game`. PostgreSQL databases are `Arcadia`, `Telecaster` and `auth`.

## Solution layout

- `Game`: networking, packets, EF Core entities, repositories, world services, scripting and maps
- `DevConsole`: generic host for the game server
- `AuthServer`: client login, game-server registration, account storage, crypto and auth packets
- `Configuration`: strongly typed server options
- `MigrateDatabase`: legacy migration utilities
- `Tests`: NUnit, FluentAssertions and FakeItEasy tests
- `docs`: current technical documentation and historical implementation plans

## Protocol fundamentals

The packet header is seven packed bytes: `uint Length`, `ushort ID`, `byte Checksum`. The checksum is
the sum of the first six header bytes. Fixed packets use packed structs; variable packets are built or
parsed manually with little-endian primitives.

Some client packets are header-only (exactly 7 bytes): `TM_CS_RETURN_LOBBY (23)`,
`TM_CS_REQUEST_RETURN_LOBBY (25)` and `TM_CS_LOGOUT (27)`. Receive loops must therefore use
`remainingData >= Marshal.SizeOf<Header>()`; the historical `>` comparison silently dropped a
header-only packet whenever it arrived alone in a TCP read, which made return-to-lobby hang
nondeterministically (it only worked when packet 23 was coalesced with other traffic).

Client-to-auth and client-to-game traffic uses the community client's custom XRC4 key configured in
both appsettings files. The `/notenc` auth path still uses XRC4 transport encryption. Passwords in
`TS_CA_ACCOUNT` are DES-ECB encrypted with the `MERONG` passphrase.

The login flow is:

```text
TS_CA_VERSION -> TS_CA_ACCOUNT -> TS_AC_RESULT -> TS_CA_SERVER_LIST
-> TS_AC_SERVER_LIST -> TS_CA_SELECT_SERVER -> one-time key -> game login
```

The game client reaches character selection, enters the world, receives stats and appearance, moves
and uses local/channel chat.

Returning to character selection is a strict two-request exchange. Pressing the menu button sends
`TM_CS_REQUEST_RETURN_LOBBY (25)`; its successful result tagged as 25 authorizes SFrame to display the
confirmation popup. The server must do nothing else at this stage. Clicking Yes sends
`TM_CS_RETURN_LOBBY (23)`. Only packet 23 stops combat, persists progress, clears the active
character/world state while preserving the account session, and receives the successful result tagged
as 23. Sending result 23 before the user confirms invokes the final scene handler too early and crashes
`SFrame.exe`. The client then requests the lobby list with `TM_CS_CHARACTER_LIST (2001)`, which receives
`TS_SC_CHARACTER_LIST (2004)` normally. This SFrame keeps the same game connection throughout the
exchange; no delayed response, reconnect or temporary transfer session is involved.

## World entry and movement

`GameActions.OnLogin` sends the login result and player enter packet, followed by the Epic 7.3
character bootstrap: stats, inventory, summon slots, wear information, gold/chaos, level/job level,
experience/JP, job properties, learned skills, belt slots, game time and status. It then synchronizes
NPC and monster visibility. See `docs/character-bootstrap.md` for packet layouts and model ordering.

Epic 7.3 key bindings are character data, not a local `.opt` setting. The server sends the single
string property `client_info` with `TS_SC_PROPERTY (507)` during world entry, and the client writes it
back with `TS_CS_SET_PROPERTY (508)`, normally when leaving the game. The value is an opaque,
pipe-delimited list of `QS2`, `KMT` and chat-mode entries stored as text in
`Characters.ClientInfo`. `CharacterDefaults` supplies the complete default map for new and legacy
characters. Do not split this value into the `quick_slot`, `current_key` or `saved_key` properties
used by later clients such as Epic 9.4; this Epic 7.3 executable only registers `client_info`.

Movement uses the client's current `x/y` fields for visibility; the final waypoint is a future
destination and must never be used as the current position.

This client build uses the calibrated model order `face, hair, armor, gloves, boots`. Its extended
`TS_SC_LOGIN_RESULT` appearance block after `race` is `faceTextureId, skinColor, faceId, hairId`; the
name starts at absolute packet offset 82. This exact order comes from the client deserializer: absolute
offset 70 feeds the primary body colorizer, while offsets 74 and 78 feed the face and hair model slots.
World login also sends dedicated hidden-equipment and skin information packets.
Hair and face wear slots 13 and 12 must remain empty: the values stored in `model_id` are cosmetic
model IDs, not item resource codes, and putting them in `TS_SC_WEAR_INFO` creates transparent meshes.
The local player takes its face and hair models from `TS_SC_LOGIN_RESULT`; its own `TS_SC_ENTER` does
not rebuild the already-created actor. `TS_SC_ENTER` carries the same appearance for other players.
Do not send `TS_SC_HAIR_INFO` during bootstrap: a persisted zero custom RGB is valid when paired with
a hair color index, but the runtime update path applies it as a transparent material.
`TS_SC_HAIR_INFO` is reserved for later changes with a resolved nonzero RGB color.
`TS_SC_WEAR_INFO` is the 323-byte Epic 7.3 variant and contains 24 code/enhance/level/element arrays
without the Epic 7.4 appearance array.

The same client nevertheless expects the later four-byte `appearance_code` inside each inventory
item record: records are 85 bytes, with `appearance_code` immediately before `wear_position`. This
was confirmed directly in `SFrame.exe` (`0x55`-byte copy stride). Do not reduce them to the canonical
81-byte Epic 7.3 record or equipment slots and all following items become misaligned.

The client view is a circular 540-unit window derived from three 180-unit regions. Sending far-away
enter packets can make objects permanently absent until reconnect because the client discards the
packet while the server retains the object as visible.

NPC and monster services use the shared immutable `SpatialIndex<T>` and per-client visible dictionaries.
Database queries project only packet fields. Monster queries are restricted to resource IDs referenced
by the loaded catalog. See `docs/world-spawning.md` for the current architecture and import procedure.

## NPC packets

NPCs use `TS_SC_ENTER` (`3`) with `type = 1`, `objType = 1`, a 38-byte creature payload and an 8-byte
randomized `npc_id`. The total packet is 72 bytes. The client resolves model, name and appearance from
that ID. Objects leaving the view receive `TS_SC_LEAVE` (`9`).

NPC interaction starts with `TS_CS_CONTACT (3002)`, an 11-byte packet containing the visible NPC
handle. `NpcDialogService` resolves that per-client handle back to the resource ID and sends
`TS_SC_DIALOG (3000)`. Epic 7.3 dialog fields are length-prefixed ASCII resource references; menu
entries use the original server format `TAB + label + TAB + trigger + TAB`. A selected choice returns
its trigger through `TS_CS_DIALOG (3001)`. Only a trigger advertised in the current dialog is accepted,
and it is resolved as a catalog lookup rather than executed as arbitrary Lua.

NPC visibility keeps synchronized ID-to-handle and handle-to-ID dictionaries, so contact resolution is
O(1) and cannot accept a handle outside the connection's visible set. The dialog service compiles
contact expressions and packet templates once at startup, then stores them in frozen dictionaries.
The interaction path only looks up the NPC/page, copies a template, writes the connection-specific
handle and sends it. Dialog state is cleared when its NPC leaves visibility.

`DevConsole/npc-dialogs.73.json` links 1,445 NPC resource IDs to their original contact functions and
contains 2,338 dialog definitions recovered from the server Lua. The local Arcadia database also has
the matching `ContactScript` values. See `docs/npc-dialogs.md` for the packet layout, catalog generation
and current action limitations.

## Monster packets and catalog

Monsters use the 73-byte monster variant of `TS_SC_ENTER`: `objType = 3`, the shared creature payload,
an 8-byte scrambled `monster_id` and `is_tamed = 0`. `monster_id` must be the actual
`MonsterResource.id`, never a name or location code. Packet `TS_CS_MONSTER_RECOGNIZE` (`517`) is valid
and needs no response while monsters are idle. Each monster carries a random `creatureInfo.face_direction`
(the `float` at offset 30, verified correct against `TS_CREATURE_STATUS` being a 4-byte `uint32`), set
once per instance from the factory's seeded `Random`. This client build appears to ignore the enter-packet
facing for idle monsters (setting `is_first_enter` made no difference), so they render facing the default
direction until they orient through movement; the field is kept for correctness and future clients.

`DevConsole/monster-spawns.73.json` currently contains 3,973 compatible areas, 43,443 instances and
2,457 distinct resource IDs. It is deserialized directly with `System.Text.Json`; do not add it to the
generic configuration provider because flattening the large arrays adds tens of seconds to startup.
The catalog is generated from the available 9.4 NFS/Lua spawn sources and filtered against IDs decoded
from the Epic 7.3 client `db_monster.rdb`. Rendering and streaming have been validated in game.

## Targeting and action cancel

Pressing Escape sends `TS_CS_TARGETING` (`511`) with `target = 0` and `TS_CS_CANCEL_ACTION` (`150`),
each an 11-byte packet whose only payload is a 4-byte `ar_handle_t` (IDs valid for Epic < 9.6.3).
`TS_CS_TARGETING` sets `ConnectionInfo.TargetHandle` (the handle an attack or skill acts on;
`0` deselects and stops the current attack). `TS_CS_CANCEL_ACTION` stops the current attack. Neither
sends a response. `GameActionPackets` holds the pure offset parsers.

## Combat

Double-clicking a monster sends `TS_CS_ATTACK_REQUEST` (`100`, Epic < 9.6.3): `handle` @7 +
`target_handle` @11. The server drives auto-attack: `CombatService` runs a 100 ms `PeriodicTimer`
loop that swings every 1200 ms, sending `TS_SC_ATTACK_EVENT` (`101`). For Epic 7.3
(`version >= EPIC_7_3`) every `ATTACK_INFO` field is int32 and there is no `flag_padding`, so one
swing is 83 bytes (`ATTACK_INFO` = 61) and a `count = 0` `AEAA_EndAttack` is 22 bytes. The client
plays the death animation when `target_hp` reaches 0; there is no `TS_SC_DEAD` in this version.

`MonsterWorldState` is the single source of mutable monster state: the shared `SpatialIndex`, current
HP and respawn deadlines (both sparse). `MonsterSpawnService` reads it and skips dead instances in
`Sync`; `CombatService` mutates it and re-streams a respawned monster to its last attacker. Access to
`ConnectionInfo.SpawnedMonsters` is guarded by `MonsterVisibilityLock` because the combat tick thread
and the client thread both touch it. Damage is currently the monster's max HP divided by 3 (a
fast-kill value for testing) and attack timing is fixed until the `MonsterResource` combat columns are
backfilled.

On death the killer is rewarded: `CombatRewards.Compute(level)` returns level-based placeholder exp, jp
and gold (`10 + level * 5`, `5 + level * 2`, `5 + level * 3`), added to `ConnectionInfo`
(`CharacterExp`/`CharacterJp`/`CharacterGold`, seeded in `OnLogin`) and sent with `TS_SC_EXP_UPDATE`
(`1003`) and `TS_SC_GOLD_UPDATE` (`1001`). Real per-monster exp and gold live in the `MonsterResource`
reward columns (`Exp`, `GoldMin`, `GoldMax`) and replace the placeholder once backfilled. Progress
persists once per session: `GameClient.OnDisconnect` calls `CharacterService.SaveProgress`, which writes
exp, jp, gold and chaos; there are no per-kill database writes.

Experience levels the character server-side. `LevelResource` (300 rows, columns `level`/`exp`, extracted
from the 9.4 Arcadia data into Postgres `LevelResources`) gives the cumulative exp threshold to advance
from each level. `CharacterExp` is cumulative and never reset; on each exp gain `LevelingService` runs
`LevelCurve.Resolve` (`while exp >= threshold[level]: level++`), so gaining enough for several levels at
once levels up several times. A level-up recomputes stats with `StatService`, sets HP/MP to the new
maximum, and sends `TS_SC_LEVEL_UPDATE` (`1002`), stat info and the hp/mp properties. There is no
client level-up packet; `TM_CS_QUERY` (`13`), which the client sends when its exp bar is full, is
consumed without a response because the server has already applied the level. The new level persists
through `CharacterService.SaveProgressAsync`. The exp curve comes from 9.4 data and may differ slightly
from the 7.3 client's own table.

Job level is server-driven too. The JLv-up button sends `TM_CS_JOB_LEVEL_UP` (`410`); the server spends
JP and raises `CharacterJobLevel` by one. The per-JLv JP cost is `LevelResource.JLvs[0]` (the `jp_0`
column, the first-job cost, small enough to fit `int`; `jp_1..jp_3` overflow `int` at higher levels and
are not imported). `LevelingService.ApplyJobLevelUp` uses the pure `JobLevelCurve.NextCost`, which
returns the cost for the current JLv or `0` when the tier is capped (`jp_0` drops to `0` around JLv 10).
JP is consumed, not a cumulative threshold. The response sequence mirrors the reference server: the exp
update carrying the new JP, the `job_level` property (`TS_SC_PROPERTY`, which the skill window reads to
refresh), then `TS_SC_RESULT` tagged with request 410 and the target handle as value (which triggers the
client-side flow; failures answer `NotEnoughJP` or `LimitMax`). Do not send `TS_SC_LEVEL_UPDATE` here.
Only the first job tier is wired; higher tiers need `jp_1..jp_3` in a `bigint` array. The job level
persists through `SaveProgressAsync`.

Skill learning is server-authoritative. Epic 7.3 sends `TM_CS_LEARN_SKILL` (`402`, 17 bytes) with the
character handle, skill id and requested level. `SkillCatalog` validates that the request advances by
exactly one level, belongs to the current job tree, satisfies character/JLv/skill prerequisites and
does not exceed the configured maximum. It then derives the JP cost from
`DevConsole/skill-catalog.73.json`; this immutable runtime index contains 1,339 job/skill definitions
for the 42 classic jobs and is generated from `JobResource`, `SkillTreeResource` and `SkillJPResource`
by `tools/Export-SkillCatalog.ps1`.

JP and the learned level are committed together in Telecaster (`CharacterSkills`, unique on
character/skill) before runtime state changes. On success the client receives `TS_SC_EXP_UPDATE`
(`1003`) with the remaining JP, a one-record `TS_SC_SKILL_LIST` (`403`) and the result for request
`402`. Login sends the complete learned list through `403`, followed by the existing empty added-skill
marker `404`. The catalog uses the available 9.4 classic-job tables behind the Epic 7.3 wire format;
the client only requests skills exposed by its own 7.3 resources. See `docs/skill-learning.md`.

The death sequence lets the client play its death animation: the killing swing is followed by
`TS_SC_STATUS_CHANGE` (`500`) with the dead flag (`1 << 8`), and the `TS_SC_LEAVE` that removes the
corpse is deferred by `DeathAnimationSeconds` (6 s) through a pending-leave list on the combat tick
rather than sent immediately. Item drops are a later milestone that will hook the same death branch.

## Monster movement

Monsters visible to at least one player idle-wander: every 6-12 seconds they pick a random destination
75-150 units from their spawn point and walk there via `TS_SC_MOVE` (`8`, reused from the
player-move echo: `start_time` @7, `handle` @11, `tlayer` @15, `speed` @16, `count` @17, then
`tx/ty` floats). `MonsterMovementService` runs a 500 ms loop in two phases: it unions every client's
visible monster set, calls `MonsterWorldState.TryBeginWander` once per active instance to decide a
shared destination, then broadcasts the move to each client that sees it using that client's handle.
`MonsterWorldState` holds the mutable current position and next-move deadline; a respawned monster
returns to its origin. The `SpatialIndex` keeps culling on spawn positions (wander radius is far
smaller than the view range), while enter and move packets use the current position. Movement is timed
against the client clock: `ConnectionInfo.ClientClockOffset` is captured from each `TS_CS_MOVE_REQUEST`
and applied to `start_time` so the walk does not teleport. Walk speed is a placeholder.
`AuthorizedGameClients` is a `ConcurrentDictionary` so the movement thread can iterate it safely.

## Equipment

Both directions are wired, parsed by `GameActionPackets` and served by `EquipmentService`, which mirrors
the reference `WorldSession::onPutOnItem` / `onPutOffItem`.

`TS_CS_PUTON_ITEM` (`200`, Epic < 9.6.3) is 16 bytes: `position` (int8 @7), `item_handle` (uint32 @8),
`target_handle` (uint32 @12). `TS_CS_PUTOFF_ITEM` (`201`) is 12 bytes: `position` (int8 @7),
`target_handle` (uint32 @8). Only the player is supported: a `target_handle` that is neither `0` nor the
character handle answers `NotExist` (summons are ignored). `position` is a raw client byte, so it is
bounds-checked against the 24 wear slots before it reaches the database; an out-of-range slot answers
`InvalidArgument` rather than persisting a `WearInfo` that `TS_SC_WEAR_INFO` would then skip, which would
strand the item outside both the bag and the model.

`CharacterService.EquipItemAsync` loads the character with its items, resolves the item by handle
(`(uint)ItemEntity.Id`), clears any item already worn at the target slot (the displaced item returns to
the bag), assigns `WearInfo` and persists both changes in one `SaveChangesAsync`. It returns an
`EquipItemResult` carrying the outcome, the character and the equipped/displaced entities.
`UnequipItemAsync` is the mirror and returns the cleared `ItemEntity`. An unknown handle answers
`AccessDenied`, an item already worn `NotActable`, an empty slot `NotExist`.

The client needs two different packets, and sending only one leaves half the UI stale:
`TS_SC_ITEM_WEAR_INFO` (`287`, 22 bytes: `item_handle` @7, `wear_position` int16 @11, `target_handle`
@13, `enhance` int32 @17, `elemental_effect_type` @21) updates the **inventory** record, while
`TS_SC_WEAR_INFO` (`202`) rebuilds the **3D model**. `TS_SC_WEAR_INFO` only indexes slot to item code and
never references the inventory item, so without `287` the item stays visually equipped in the bag until
relog. The response order is: `TS_SC_ITEM_WEAR_INFO` for the displaced item (equip only), then for the
affected item, stat info, `TS_SC_RESULT` tagged with the request id, and finally the refreshed
`TS_SC_WEAR_INFO` (a cleared slot falls back to the base body model through `InjectBaseModelIfEmpty`).

Stats are unchanged for now because `StatService` is level/race based and does not yet read equipment.

## Client clock

The client keeps its own notion of server time and every timed UI depends on it. Three packets feed it,
all of them `ar_time_t`, which is a **millisecond tick, never a wall clock**: `TS_TIMESYNC` (`2`,
bidirectional, `time`), `TS_SC_SET_TIME` (`10`, `int32 gap`) and `TS_SC_GAME_TIME` (`1101`, `t` +
`game_time`).

The handshake mirrors the reference `WorldSession::onTimeSync` and the server drives it entirely: this
client **never initiates a `TS_TIMESYNC`**, it only answers one, immediately and one for one. So the
server sends `TS_TIMESYNC` at world entry, each answer yields `gap = serverTick - clientTick` (the clock
offset), and the server keeps asking until it holds four samples, then sends `TS_SC_SET_TIME` with their
average. The whole exchange completes in milliseconds. Because the client never pings on its own, the
clock is established once per session and never refreshed; sampling stops after `TS_SC_SET_TIME`.
`GameClient.HandleTimeSync` also seeds `ConnectionInfo.ClientClockOffset` from the same gap, which is why
the offset is now known at world entry instead of only after the first move request.

Without this handshake nothing time-based on the client ever elapses: the inventory sort button greyed
itself for `ITEM_ARRANGE_COOL_TIME` and never came back, survived closing the window, and only cleared
by returning to character selection, because the countdown had no clock to run against. `TS_SC_GAME_TIME.t`
used to carry unix seconds, which is the wrong base for an `ar_time_t`; it now carries the client tick
like every other time field. `game_time` (the in-world day/night clock) is still `0`.

## Inventory ordering

Bag placement is the `index` field of `TS_ITEM_INFO`, the last field of the 85-byte inventory record
(offset 81), persisted as `Items.Idx`. Nothing used to assign it, so every legacy row holds `Idx = 0`
and the client placed bag items by packet arrival order. Degenerate indices make a swap a no-op (it
exchanges `0` with `0` and reports success), so `InventoryArrange.EnsureContiguousIndices` renumbers a
bag whose indices are not a permutation of `0..n-1`, keeping a valid permutation untouched so a player
arrangement survives. It runs from `CharacterDefaults.Apply` (the existing legacy backfill hook, which
already persists once per load) and before any swap. There is no `TS_SC_ARRANGE_ITEM`: both ordering packets answer
with `TS_SC_INVENTORY` (`207`) plus a `TS_SC_RESULT`. Sending a partial `207` is safe because the client
merges item records by handle — login already streams the inventory in chunks, so a chunk cannot be a
full replace. Only bag items (`WearInfo == None`) are renumbered; worn items keep their slot.

`TS_CS_CHANGE_ITEM_POSITION` (`218`, Epic < 9.6.3) is the drag-and-drop swap: `is_storage` (bool @7) +
`item_handle_1` (uint32 @8) + `item_handle_2` (uint32 @12), 16 bytes. It carries no slot number, so it
can only ever swap two existing items; the bag is a dense list rather than a sparse grid. Both handles
must resolve to bag items, otherwise the answer is `NotExist`. `InventoryService.SwapPositionsAsync`
exchanges the two `Idx` values and echoes the whole bag back through `207`, because the normalization
that precedes the swap can renumber the other items too.

`TS_CS_ARRANGE_ITEM` (`219`) is the inventory sort button: `is_storage` (bool @7), 8 bytes total.
`InventoryArrange` packs the order into one `ulong` per resource — `group` (8 bits) `<< 48`, `item_type`
`<< 40`, inverted `rank` `<< 32`, then the resource id in the low 32 bits — so the sort is a single
integer comparison per pair, with the item id breaking ties to keep repeated arranges stable. Every key
is precomputed once at startup by `ItemSortCatalog` into a `FrozenDictionary`, so the comparator never
touches the database. Unknown resources get group 255 and sort last. The observed ranges make the packing
safe: `group <= 140`, `item_type <= 7`, `rank <= 7`, `id <= 700000886`.

Storage is not implemented, so `is_storage = 1` answers `NotActable` for both packets.

The original server's ordering could not be recovered exactly. The shipped `Game_bin` PDB proves the
shape — `StructInventory::_ItemArrangeGreater(const StructItem*, const StructItem*)` is a comparator
(so the sort is by item fields, not by name), `StructPlayer::ArrangeItem(bool)` returns a result code,
and `ITEM_ARRANGE_COOL_TIME` with `m_nLastInvenArrangedTime`/`m_nLastStorageArrangedTime` proves a
per-inventory/storage cooldown — but that PDB does not match the shipped executable (RSDS GUID
`8F49E0DD-…` against the PDB's `912BD391-…`, 9 sections against 6), so its addresses are unusable and
the comparator body is unrecoverable without the matching build. The group/type/rank/id order is therefore our own choice.

`ITEM_ARRANGE_COOL_TIME` itself was recovered: the constants live in the PDB as `S_CONSTANT` (`0x1107`)
records, whose payload is the record kind, a type index and a numeric leaf, so the bytes preceding the
name decode to the value. It is **3000 ms**, matching `InventoryService.ArrangeCooldown`; the decoding
checks out against its neighbours (`MAX_ACCOUNT_LEN` 60, `MAX_BOOTH_ITEM_COUNT` 8,
`DONATE_GOLD_UNIT_COUNT` 10000, `MAX_LAYER` 256). Spamming is refused with `ResultCode.CoolTime`.

That 3 s is the **server's** anti-spam floor and is not what the player sees: the client greys its own
sort button for a measured **30 s**, so the client is the stricter of the two and the server guard never
fires in normal play. The client's countdown only runs once the clock handshake above has completed;
before it existed the button greyed permanently.

`ItemResources` holds 33,142 rows imported from the 9.4 SQL Server `Arcadia.ItemResource`. Like
`MonsterResource`, only the directly mapped scalar columns were imported; `RaceRestriction`, `SetPart`
and `JobRestriction` are bitfields derived from `limit_*` columns and are left at zero, and the
`NameId`/`SetId`/`SummonId`/`EffectId`/`SkillId`/`StateId` foreign keys are left null because the
referenced resource tables are still empty.

## Current limitations

- Monsters auto-attack (kill + respawn) and idle-wander, but have no aggro, chase, retaliation, loot or
  taming; damage, attack speed and walk speed are placeholders
- NPC dialogs render their original text and static follow-up pages; gameplay actions such as shops,
  teleportation and quest mutation are not executed yet
- Skill learning and persistence work; casting, passive application and skill effects are not implemented
- Equipping and unequipping work and persist, but equipment does not affect stats yet, and item
  requirements (level, job, race) are not validated
- Inventory sorting and drag-swap work for the bag; storage/warehouse is not implemented and the sort
  order is our own rather than the original one
- The client clock is synchronized, but `TS_SC_GAME_TIME.game_time` (the in-world day/night clock) is
  still zero, and movement still applies `ClientClockOffset` by hand rather than trusting the sync
- Remaining 9.4 resource data has not all been globally filtered for 7.3 compatibility
- Features beyond login, character handling, world entry, movement, chat, stats and object streaming
  remain POC work

## Change guidelines

- Preserve the 7-byte header, little-endian layout and exact client packet sizes.
- Keep world-object enter decisions inside the 540-unit view.
- Use `MonsterResource.id` for monster enter packets.
- Keep resource queries no-tracking and project only fields required at runtime.
- Add tests for packet offsets, encodings, spatial boundaries and spawn expansion.
- Do not edit generated EF migration designer files manually unless the migration itself changes.
