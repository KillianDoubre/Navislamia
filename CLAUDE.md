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

## Sending and object streaming

`Connection` queues outgoing messages on an unbounded `Channel` and the send loop parks on
`WaitToReadAsync`, draining whatever is queued into **one** socket write through a pooled buffer.

**It used to poll**: it drained the queue and then slept 100 ms unconditionally, so anything queued
just after a drain waited **up to 100 ms** before leaving the server. Everything paid it — every object
entering the view, every combat event — which is what made objects pop in late while walking. The same
loop also **spun at 100% CPU** whenever a disconnect was signalled with a non-empty queue, because the
disconnect branch `continue`d without dequeuing, so the queue never emptied. Coalescing is safe and is
what the wire already looked like: TCP is a byte stream and the client splits messages by the header
length, which is exactly why a lone header-only packet only ever arrived coalesced with other traffic.

**A derived connection must route through `base.Send`**, never touch the channel: `CipherConnection`
used to enqueue directly, so a signal added to the base would have left its messages queued forever.
Its `Send` now holds a lock across encode-and-queue, because **XRC4 is a stream cipher**: the combat,
movement and cast ticks and the client's own thread all send on one connection, and two of them
interleaving would consume the keystream out of order *and* queue in an order that no longer matches
it — undecodable, and rare enough to look like a random disconnect.

`WorldObjectStreamer.Stream` is the single visibility loop behind `NpcSpawnService`,
`MonsterSpawnService` and `FieldPropService`: enter what came into view, `TS_SC_LEAVE` what left, keep
the handle maps in step. Its `canEnter` predicate is what lets a **dead monster stay visible without
being re-streamed** — the corpse outlives the death, and its `TS_SC_LEAVE` is deferred by the combat
tick. Three hand-written copies of this loop had already drifted: only two maintained a
handle-to-id map.

**The streaming volume is not a bottleneck and measurements say so**: at most 104 monsters and 87 props
are in view at once (medians 13 and 2), so a worst-case burst is ~13 KB. `SpatialIndex` is a real grid
keyed on the 540-unit view, so a query touches ~9 cells whatever the world holds, and monster
position/HP are read from `MonsterWorldState` only for a monster that is actually entering. **Terrain
squares and map decoration are client-side**: they are loaded from the client's own `data.00X`
archives and the server sends nothing for them, so no server change can make them load faster.

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
loop that swings every 1200 ms, sending `TS_SC_ATTACK_EVENT` (`101`). **The swing is gated on
`CombatRange.InReach`** — the player's current position against the monster's — so a swing out of reach
holds and re-checks every 200 ms while the client walks the player in, rather than landing a hit from
across the view. **`CombatRange.MeleeReach` is the single real reach both directions share**: player
attacks used to have no range gate at all while monster attacks gated at a flat placeholder, and that
asymmetry read as an inconsistent attack range — a player could hit a monster that could not hit back.
The reach is now the reference's own value (see Monster AI): `(12 × attack_range) / 100` plus both body
radii, `size × 12 × scale` each. For Epic 7.3
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
**A JLv-up changes the base stats** through `JobLevelBonus`, so both `TS_SC_STAT_INFO` packets and the
`max_hp`/`max_mp` properties are sent **after** that result — appended rather than inserted, so the
sequence the client needs is untouched. Without them the stat window only caught up on the next world
entry: the JLv/stat dependency arrived with the stat work while this trigger kept its old sequence.
**Any change to what feeds the stats must revisit every trigger** (login, level-up, JLv-up,
equip/unequip, skill learn).
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

## Item drops

On death `CombatService` calls `GroundItemService.DropForMonster`, which rolls the monster's table and
puts each result on the ground near the corpse. Gold stays automatic and is not part of this path.

`DevConsole/monster-drops.73.json` is the runtime catalog (5,395 tables, 5,767 direct entries, 51,643
group-reference entries, 6,221 drop groups, 6,330 monsters). It is loaded like the spawn catalog — read
with `System.Text.Json` in `Program.ConfigureMonsterDrops` and frozen by `MonsterDropCatalog` into a
`FrozenDictionary` keyed by monster id, so a kill never queries the database. Regenerate it with
`tools/export_monster_drops.py`. Traps, all silent if you get them wrong:

- **`drop_percentage` is a probability in `[0, 1]`, not a percentage out of 100** (measured max exactly
  `1.00`). `DropRoll.Roll` compares `random.NextDouble()` against it directly.
- `MonsterDropTableResource.id` is a **monster id**, and `MonsterResource.drop_table_link_id` points at
  the monster that *owns* the table, so 107 and 108 both read 106's. The table also has a `sub_id`, so a
  monster's full table is **every row with that id across sub_ids**, ten slots each.
- **A negative `drop_item_id` is a reference to `DropGroupResource`, keyed by the negative id itself**,
  not junk — and it is where the drops actually live. **56,584 of the ~62,000 entries are group
  references** (only 5,800 are direct items), so filtering them out — which the first cut did — throws
  away ~91% of all drops and is exactly why monsters almost never dropped anything. A group is a weighted
  **pick exactly one**: its `drop_percentage` columns are the weights and sum to `1.00`. Groups nest (a
  group member can be another negative group ref, 6,022 of them), so resolution loops until a positive
  item falls out — the reference's `SelectItemIDFromDropGroup` inside `do … while (id < 0)`. Of the group
  ids a table reaches (incl. nested), 7 are empty/missing; a reference to one simply drops nothing.

The roll is **two-stage**, mirroring `Monster::procDropItem`: each slot rolls its own `drop_percentage`
independently (a monster can drop from several slots at once — 2,337 tables sum above 1, which rules out a
table-level pick-one); when a slot fires, a **positive** id drops that item with a rolled count, and a
**negative** id resolves its group by weight — once per rolled count, so a slot with `count = 6-20` drops
that many separate group picks. This is why a typical spawn monster now drops on **~78% of kills** at the
authentic rate, several items each (piles of low-value materials), rather than the ~2% the direct-only
catalog produced. `GroundItemService.DropChanceMultiplier` still scales every chance (clamped at 1.0) and
stays **1, the authentic rate** — no longer a testing knob, since drops are plentiful without it.

A ground item is `TS_SC_ENTER` with `type = ET_StaticObject (2)` and `objType = EOT_Item (2)`, 70 bytes:
the shared header through `objType`, then `code` as the 8-byte randomized `EncodedInt` (the `npc_id`
encoding), `count` as uint64, and a `pick_up_order` block of `drop_time` plus three player handles and
three party ids. Only `drop_time` and the first handle are filled; parties do not exist yet.

`TS_CS_TAKE_ITEM` (`204`, Epic < 9.6.3) is `taker_handle` @7 + `item_handle` @11, 15 bytes. Pickup checks
range (300 units), claims the item with an `Interlocked` compare-exchange so a double request cannot
duplicate it, then writes a new `ItemEntity` at `max(Idx) + 1`. The reply order is
`TS_SC_TAKE_ITEM_RESULT` (`210`: `item_handle` + `item_taker`, 15 bytes), then `TS_SC_LEAVE`, a one-record
`TS_SC_INVENTORY` and the result. **`210` is what plays the pick-up animation** — its `item_taker` tells
the client which actor to animate, which the generic `TS_SC_RESULT` cannot express, exactly like `287`
against `202` for equipment. It is sent before the `LEAVE` so the animation starts before the object
disappears. Items expire after 120 seconds through a 1 s tick.

`GroundItemService` deliberately does **not** depend on `NetworkService`: `NetworkService` already
injects the service, so taking the client list from it creates a DI cycle that only fails at runtime.
Drops are therefore sent to the killer alone, and each `GroundItem` holds its owning `GameClient` the
same way `CombatService.PendingLeave` does. Other players cannot see or take them.

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
and applied to `start_time` so the walk does not teleport. `AuthorizedGameClients` is a
`ConcurrentDictionary` so the movement thread can iterate it safely.

**Position is interpolated over time, not snapped.** `MonsterWorldState.BeginMove` records a move
(`start`, `dest`, `speed`, `startTick`) and `GetPosition` interpolates it with the exact reference math
(`MonsterMovement`, ported from `ArMoveVector::SetMove`/`Step`): a move takes `length × 30 / speed`
ar_time ticks and the position advances linearly. The server broadcasts `TS_SC_MOVE` with the **same**
start tick and speed it interpolates with, so its notion of where a monster is matches the animation the
client plays. It used to snap the stored position straight to the destination each 500 ms wander (and
each chase tick), so the server thought a monster had already arrived while the client was still
walking — which is what read as jittery, teleporting movement. `MoveOrder` is the returned
destination/speed/start-tick the caller broadcasts.

## Monster AI

Monsters fight back and hunt. `MonsterAiService` runs a 300 ms loop like `MonsterMovementService`
(it holds `NetworkService` only for the client list, never reaches back into it): acquire, then act on
every monster in combat. The pure decisions live in `MonsterAiRules` (`Idle`/`Acquire`/`Chase`/
`Attack`/`Drop`), which is what the tests exercise; the service is the I/O shell.

- **Retaliation**: when a player's swing lands without killing, `CombatService.ApplyDamage` calls
  `MonsterWorldState.SetAggro(instanceId, client)`. **Every** monster retaliates, aggressive or not.
- **Aggro on sight**: a monster with `FirstAttack` (the `f_fisrt_attack` column — a frozen source typo
  — set on 5 478 of 8 164) and no target takes a player it is streamed to and within `visibleRange`.
- **Chase**: while the target is beyond the melee reach and the monster is within `chaseRange` of home,
  it steps toward the player via `TS_SC_MOVE` (`8`), the same echo wander uses; an aggro'd monster
  **does not idle-wander** (`TryBeginWander` skips it). A new chase move is only issued when the desired
  destination has drifted past `ChaseReissueThreshold` from the one already in flight — otherwise the
  client would get a fresh move every 300 ms tick and stutter.
- **Attack**: within the melee reach and off cooldown, `TS_SC_ATTACK_EVENT` (`101`) with the monster as
  attacker and the player as target; the player loses `maxHp / 100` HP (**test formula**, floored so
  HP never drops below 1 — **there is no player death or respawn**), sent as the `hp` property. **A
  monster stands still to attack**: if a chase move is still in flight when it strikes, `StopMove`
  freezes it at its current position and a `TS_SC_MOVE` stop is sent, so it does not slide through the
  swing (the reference's `SetMove(current, current, speed 0)` before `Attack`). The player is planted
  the same way — `CombatService` sends a stop-move for the player when a swing lands, only ever in
  reach where the client has already stopped them, so it reinforces rather than fights the client.
- **Drop**: the target leaves view or pulls the monster past `chaseRange` from home → aggro clears and
  the monster **walks back to the position it held when it acquired, at twice the chase speed**, as one
  uninterrupted move — `ReturnHome` flags it and idle wander is suppressed until it arrives, otherwise a
  fresh wander destination hijacks the return the instant the target drops (which read as the monster
  not really going home). `SetAggro` records the return position; `TryGetAggroHome` returns it. `Kill`
  clears aggro (a corpse chases nothing); disconnect and warp call `ICombatService.DropAggro(client)` so
  nothing chases a ghost.

**The aggro target lives in `MonsterWorldState`** next to HP/respawn/states, sparse like they are, so
it is the single source of mutable monster state and the movement/AI/combat threads share one lock.
A monster's handle differs per client, so the attack and move packets use *that client's*
`SpawnedMonsters` handle; aggro targets exactly one player, unambiguous while the world is
single-player.

**The ranges are scaled, and the scale is not uniform** — the same trap as `cast_range`.
`MonsterAiRules` ports the reference: **chase range is `12 × chase_range`** (`Monster::GetChaseRange`,
so 100 → 1200 world units), visible range reuses `12 ×` (the reference's aggro path is an empty stub),
clamped to the client view. **Attack range is the reference's real value**, in `CombatRange.MeleeReach`:
`(12 × attack_range) / 100` (`Unit::GetRealAttackRange`) plus both body radii, where a unit's size is
`size × 12 × scale` (`Object::GetUnitSize`) and the player uses the default `1 × 12 × 1 = 12`. The
body-size term dominates the tiny weapon term, so a small monster reaches ~12 units and a big one
(`size` up to 12.45, `scale` up to 7) hundreds — **big monsters really do hit from farther**. The same
per-monster reach gates both the monster's attack and the player's swing, keeping them symmetric.
`run_speed → move speed` stays a placeholder; `GroupFirstAttack` is imported but group aggro is not
modelled.

`CharacterMaxHp` was added to `ConnectionInfo` next to `CharacterHp`, seeded at the same two points HP
is set to max (login and level-up), because the test damage reads it. The AI columns were NOT NULL
literals until `tools/Import-MonsterResourceColumns.ps1` backfilled `FirstAttack`, `GroupFirstAttack`,
`VisibleRange`, `ChaseRange`, `AttackRange`, `RunSpeed`, `Size` and `Scale` from the 9.4 source — the
same import trap the skill columns hit. See `docs/superpowers/specs/2026-07-17-monster-ai-design.md`.

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

Equipping recomputes the stats and refreshes the cached item effects, the equipped weapon class and the
passive effects on `ConnectionInfo` — a weapon change turns the gated masteries on and off, so all three
are re-seeded together. The `max_hp`/`max_mp` properties travel with the two stat packets here too. See
Character stats and Passive skills above.

## Character stats

Stats are computed server-side by the pure `StatCalculator`, ported from `rzgame`'s
`Character::updateStats` and `StatBase.cpp` (in the local rzu clone). **Base stats come from the job,
not the race, and grow with job level, not character level**; character level drives the advanced
stats:

```
job -> JobResource.stat_id -> StatResource            = base str/vit/dex/agi/int/men/luk
     + JobLevelBonus over the current and previous jobs
level -> seeds the 34 advanced stats (attack=level, attackSpeed=100, moveSpeed=120, ...)
     + worn item passives
     + stat-derived bonuses (attack += 2.8*str, defence += 1.6*vit, maxHp += 33*vit, ...)
```

`JobLevelBonus` splits the job level into **chunks of 20** (0-19, 20-39, 40+), the last chunk absorbing
the remainder, and the bonus is `sum(chunk[i] * perLevel[i]) + default`. **The per-level values are
`decimal(10,3)`, not integers** (0.34 to 5.88; a typical first job is 0.5 str per JLv). rzgame declares
them `int32_t`, which against this 9.4 schema truncates every bonus to zero — its struct targets another
data version. The same applies to its column names: the real schema has `stati_id` and `avable_job_0..3`,
typos frozen into the shipped tables.

**Two `rzgame` bugs are deliberately not reproduced.** It assigns `statBase = stats` *before* adding the
derived bonuses and then adds them to a discarded copy, so the packet it sends omits every derived
bonus. `nAccuracyLeft` is downstream of the same snapshot; we take the evident intent and mirror the
main hand (`AccuracyLeft = AccuracyRight`).

`TS_SC_STAT_INFO` (`1000`) is 96 bytes: handle, 8 int16 base fields, **34 int16 attributes** and a
`type` byte. Every attribute is int16 at Epic 7.3 — the int32 widenings all start at 9.3 or later. Two
ordering traps: `nMaxWeight` sits **after `nAttackRange`** (the earlier slot in the rzu macro is gated
`>= EPIC_9_7_0`), and the `unknown` field before `nAttackSpeed` is `>= EPIC_9_7_0` and absent.
**The client expects two packets**: `SIT_Total` (0) and `SIT_ByItem` (1), the latter carrying the worn
item contribution alone — that is what feeds the bonus column. Sending only the total leaves it empty.

Equipment passives come from `ItemResource.base_type[4]`/`opt_type[4]` with their `var1`/`var2`. Only
worn items (`WearInfo != None`) contribute. `base_type` on wearables is entirely `ItemEffectPassive`;
`opt_type` is a **mixed space** — on a consumable it holds `ItemEffectInstant` use-effects (IncHp,
AddState, SummonPet), on equipment extra passives — so only values resolving to a known
`ItemEffectPassive` are applied and everything else is ignored.

`IncParameterA` (96) and `AmpParameterA` (98) are the dominant opt effects on equipment (9 811
occurrences): **`var1` is a bitmask of target parameters and `var2` the amount**, `Inc` flat and `Amp` a
percentage (`tooltip_state_7153` is `"#@bitset_text@# #@value@#"`, `7154` the same with `%`). The bit
table is `StringResource_EN.tooltip_bitset_7101..7152`: **bit `n` maps to entry `7101 + n`** — bits 0-6
are Strength, Vitality, **Agility, Dexterity** (that order), Int., Wisdom, Luck, then P.Atk, M.Atk,
P.Def, M.Def, Atk Spd, Cast Spd, Mov. Spd, Accuracy, M. Acc, Critical Rate, Block Per., Block Def.,
Evasion, M. Res., MAX HP, MAX MP, MAX SP, HP/MP Recov., SP Recov., HP/MP Regen., Max. Wt., then the
resistances. Bits 26, 30 and 31 have no Epic 7.3 field and resolve to nothing.

**That mapping is validated by the data, not assumed**: every multi-bit mask in the 9.4 tables is
coherent under it — `63` is the six stats without Luck (the 1 328 "+N all stats" items), `384` is
P.Atk+M.Atk, `1536` P.Def+M.Def, `50331648` HP+MP Recov., `402653184` HP+MP Regen., `65536` Critical
Rate alone with var2 in 1..15. Confirmed end to end in game data: item 101221 decodes to
`AttackPointRight=77, AttackSpeed=-5, Critical=2`.

`ParameterB` (97/99) is **not decoded and is a documented gap**: the bit table holds 52 entries so B can
address 20 bits, yet the data contains a bit-28 mask. It covers **63 items** out of 33 142, all late-9.4
accessories (ids 422205+) unlikely to exist in the 7.3 client. The lever for a future attempt is the
client's `db_item.rdb`, which is also what blocks Epic 7.3 drop filtering.

`StatCatalog` (job/stat reference data) and `ItemStatCatalog` (item id -> precomputed effect list) are
frozen at startup like `ItemSortCatalog`, so a stat computation never queries the database.
`ConnectionInfo` caches the previous jobs and the resolved worn-item effects, seeded at login and
refreshed on equip/unequip, which is what lets `LevelingService` recompute from the connection alone.
The current job level stays out of that cache so a JLv-up cannot desynchronise it.

**HP/MP are set to the maximum on world entry.** The max formula changed, so a stored value can exceed
it; this discards nothing because current HP is never persisted during play (`SaveProgressAsync` writes
exp, jp, gold, chaos and level, never `Hp`), leaving `Characters.Hp` stale from character creation.

Data lives in Postgres `Arcadia`: `StatResources` (3 899), `JobResources` (42), `JobLevelBonuses` (42),
and the `ItemResources` effect arrays (17 823 rows with a base effect, 12 635 with an opt effect). All
were imported from the 9.4 SQL Server. `ItemResourceEntity` used to declare `BaseValues`/`OptValues` as
`decimal[,]`, a multidimensional array Npgsql does not map — which is why those columns were empty; they
are now flat `BaseVar1`/`BaseVar2`/`OptVar1`/`OptVar2`.

## Passive skills

Passive skills contribute to the stats, resolved at world entry and refreshed when a skill is learned.
They reuse the stat machinery entirely: `StatBlock.Add`/`Amplify` and `StatCalculator` are unchanged, and
`StatEffect` (formerly `ItemStatEffect`) carries item and passive contributions alike.

```
learned skill -> SkillResource.effect_type + var1..var20
              -> pair (base, perLevel) per slot, the slot's stat fixed by the effect type
              -> StatBlock
amount = base + perLevel * skillLevel
```

**`SkillResource.var1..var20` are ten `(base, perLevel)` pairs, and the pair index selects the stat**,
which the effect type determines. Proven by the tooltips: Body Training is `IncreaseHpMp` with pair 1 =
`(0, 30)` and reads "Maximum HP increases"; Defense Training is `IncreaseBaseAttribute` with pair 1 =
`(0, 3)` and reads "Defense power increases", while Mind Defense is the same effect type with **pair 2** =
`(0, 3)` and reads "Increases magic defense power". `Creature HP Expansion` (pair 1) against
`Creature MP Expansion` (pair 2) shows the same slot split independently.

Supported effect types (`SkillPassiveCatalog.SlotTargets`), deliberately only the proven ones:

| effect type | pair 1 | pair 2 |
|---|---|---|
| `WeaponMastery` (10001) | P. Atk | P. Atk Speed |
| `IncreaseBaseAttribute` (10008) | P. Def | M. Def |
| `IncreaseHpMp` (10021) | MAX HP | MAX MP |

`WeaponMastery`'s **pair 2 is the attack speed**, proven three times with distinct values: Fighter's
Combat Skill has `var3 = 5` and reads "Lv 1 also increases P. Atk. Spd. **by 5**", Archery Practice has
`var3 = 15` and reads "**by 15**", and Sword Mastery pairs `(0, 1)` with "increases your physical attack
**and attack speed**". A `(base, 0)` pair is therefore a flat bonus that does not scale with the level,
which is what "Lv 1 also increases" means.

**The weapon gate is uniform across every effect type, not a `WeaponMastery` special case.** A passive
applies when `vf_is_not_need_weapon` is set **or** the equipped main-hand weapon matches one of its
`vf_*` flags. That rule is what the data describes rather than an inference: all 81
`IncreaseBaseAttribute` and all 15 `IncreaseHpMp` skills carry `vf_is_not_need_weapon = 1` and zero
weapon bits, so Body Training and Defense Training are unconditional *because the data says so*, not
because they are exempt. `WeaponMastery` is simply the only effect type that uses the other branch: 5 of
its 21 skills are unconditional (Offense Training among them) and 16 are gated.

`SkillWeaponGate` owns the mapping from `ItemResource.class` (`ItemType`) to `SkillWeaponFlag`, and
`ConnectionInfo.EquippedWeapon` holds the main-hand class (`ItemWearType.Weapon`), seeded at login and
refreshed on equip/unequip. **`vf_axe` is the two-handed axe**: there are three axe flags for the three
axe `ItemType`s, exactly as `vf_spear` maps to `TwohandSpear`. Confirmed by the tooltips — Fighter's
Combat Skill flags swords plus all three axes and reads "equipped **swords and axe**", while Kahuna's
flags axes plus staves and reads "equipped **staff and axe**". `vf_shield_only` maps to nothing: no
mastery sets it, and Shield Mastery is `IncreaseExtensionAttribute` (10009), which is unsupported.
`DoubleSword`, `DoubleAxe` and `DoubleDagger` have **no items at all** in the 9.4 data, so those flags
can never match.

**Equipping a weapon changes the passives, so equip/unequip is a stat trigger like any other.**
`EquipmentService.SendStatInfo` calls `StatService.Seed`, which re-resolves the weapon, the item effects
and the passives together — do not split them.

**Both stat packets and the `max_hp`/`max_mp` properties are sent on every refresh.** `MAX HP` does not
travel in `TS_SC_STAT_INFO` — it is a property — so a passive raising it stays invisible until the next
world entry if only the stat block is resent, while a passive raising Defence updates immediately. That
asymmetry is what the learn, JLv-up and equip paths each got wrong in turn.

**This data is a minefield and three obvious readings are wrong.**

**`is_passive` does not mark a passive skill.** The real passives (Body Training, the masteries) have
`is_passive = 0`. The 1,192 skills with `is_passive = 1` and a state are mostly timed debuffs applied to a
target, and the ones that look permanent are **toggle auras** (`effect_type = 701 = ToggleAura`,
`is_toggle = 1`): Power Support, Agile Style, Aura of Inspiration. **An aura must not contribute until the
player switches it on**, which nothing tracks yet — `SkillResourceRepository` filters `!IsToggle` for
exactly this reason. Do not reintroduce them through the state path.

**There are two homonymous `effect_type` columns with different value spaces.**
`SkillResource.effect_type` is the `SkillEffectType` enum (701 ToggleAura, 10001 WeaponMastery, 10021
IncreaseHpMp, 30001+ damage, 32001+ on-hit triggers) — **this is the one that drives passives**.
`StateResource.effect_type` is a different space where 1 is a flat add and 2 a percentage. Reading the
repo's `SkillEffectType` against the state column makes `ParameterInc = 3` look wrong; it is not, it
simply describes the other column.

**`state_second = -1` marks a permanent state**, not `0` (which returns Fear, Poisoned, Stun — monster
status effects). That matters for the buff slice, not here.

Of the 608 player skills in the 7.3 catalog, 273 carry a `effect_type >= 10000`, but only **37 are stat
passives** (the other 34 have `var1 >= 1000`, a state id — they apply a state on hit). Of those 37:

- **`WeaponMastery` (10001) — all 21 are supported**, the 5 unconditional ones and the 16 gated on the
  equipped weapon, see above. Three of the 5 unconditional ones (Natural Sorcery twice, Magical Training
  Mastery) have all-zero vars and so resolve to nothing.
- **`IncreaseExtensionAttribute` (10009), Shield Mastery — not supported.** Conditional on a shield.
- **`AmplifyBaseAttribute` (10011), Avoidance Expert — cannot be supported.** Its tooltip says "Increases
  evasion" but **every one of its 20 vars is zero**; there is no value to read.
- `AmplifySummonHpMpSp` (10032) and `HuntingTraining` (10013), 5 skills, target the summon, not the
  character.
- `IncSkillCoolTimeOnAttack/OnBeingAttacked/OnKill` (10063-10065), 10 skills, are event triggers.

`SkillPassiveCatalog` is frozen at startup like every other catalog and holds **117 skills** (101
unconditional plus the 16 weapon-gated masteries); an unsupported effect type resolves to nothing.
`ConnectionInfo.PassiveEffects` sits next to `ItemEffects` and `EquippedWeapon`, seeded at login, rebuilt
by `StatService.RefreshPassives` on learn and by `Seed` on equip/unequip, which is why a new passive or a
newly drawn weapon shows without a relog.

**The 17 `vf_*` columns were imported after the fact**, and until then every one of them read `false` for
all 2,689 skills — they were NOT NULL literals from the partial insert, exactly like
`UseWithWeaponNotRequired` before it. `vf_shield_only` was imported alongside the rest for that reason
even though nothing reads it yet.

**That trap cost four rounds before it was fixed at the root.** An audit of every scalar column found that
only `Id`, `IsValid`, `EffectType`, `IsPassive`, `StateId`, `StateSecond`, `StateLevelBase`,
`StateLevelPerSkill` and `Values` held real data; every cost, delay, target, range and flag was a literal.
The fourth casualty was a scope decision: "none of the 111 buffs are harmful" came from querying
`IsHarmful` — an empty column — and once imported, **22 of them are harmful**. `IsToggle` was empty too,
so the `!IsToggle` filter documented above as the guard against toggle auras **protected nothing**; the
effect-type allowlist was always the real guard.

`tools/Import-SkillResourceColumns.ps1` now imports **every mappable scalar column in one pass** (94 of
them), deriving EF property → source column by introspection and refusing to run if a required column is
unmapped. Two traps it encodes: **a nullable column here is always a foreign key id where `0` means
"none"** and must be written `NULL` (no `StateResource` has id 0); and **`TextId`/`TooltipId`/
`DescriptionId` cannot be imported at all** because they reference the empty `StringResources`. Prefer
extending that script over hand-patching the next column.

**`SIT_ByItem` stays the item contribution only** — a passive is not an item.

**Mace Mastery (21118) is a documented gap.** Its tooltip promises "physical **and magical** attack" but
its only non-zero vars are `var2 = 30` (pair 1, attack) and an isolated `var8 = 120` — no mastery reads
pair 4, and +120 magic attack per level is implausible next to +30 attack. It gets its attack bonus and
no magic attack. Do not guess a stat for pair 4 without a second skill to cross-check it against.

Data: `SkillResources` (2,689) and `StateResources` (1,949) imported from the 9.4 SQL Server into tables
that existed but were empty. `SkillResources.Values` holds `var1..var20`. **`SkillResources.StateId` is a
foreign key to `StateResources`, so states must be imported first** (no orphans exist). `SkillResources`
has 90 NOT NULL columns with no default, so a partial insert must supply literals — generate that list by
introspection rather than by hand. `StateResources` is imported and unused today; it is what the timed
buff/aura slice will need.

## Skill casting

Six castable families, all through `SkillCastService` and one `SkillCastKind` dispatch. **`SkillService`
*learns* a skill, `SkillCastService` *casts* it** — the latter was called `BuffService` while buffs were
all it did, which stopped being true. `BuffCatalog` classifies every skill once at startup, so the cast
path switches on an enum instead of re-deriving effect types per request; its `default` branch logs
rather than falling back to buff behaviour, so a future kind cannot be silently mishandled. Specs: `docs/superpowers/specs/2026-07-16-buff-casting-design.md`,
`2026-07-17-auras-heals-debuffs-design.md` and `2026-07-17-offensive-skills-design.md`.

| kind | effect_type | target | what it does |
|---|---|---|---|
| `Buff` | 301, 302, `!is_harmful` | caster-inclusive | timed state on the caster |
| `Aura` | 701, 702 | any | untimed state, toggled off by recasting |
| `Heal` | 501, 505 | 1 (`Target`) | restores HP from the caster's magic attack |
| `Debuff` | 301, 302, `is_harmful` | 1 (`Target`) | timed state on a visible monster |
| `PhysicalAttack` | 30001 | 1 (`Target`) | damages a visible monster |
| `MagicAttack` | 231 | 1 (`Target`) | same, tagged `SHT_MAGIC_DAMAGE` |

The cast sequence is the same for all six: `ST_Casting` (mp cost + cast delay) → the effect → `ST_Fire`
→ `ST_Complete` → `TS_SC_SKILL_LIST` for the cooldown. Only a buff or an aura refreshes the caster's stat
packets; a heal moves HP (a property), and a debuff or an attack lands on a monster.

### Buffs

A player casts a learned buff on themselves and the client plays it, shows the icon with its countdown,
applies the stats and lets it expire.

**A region buff is a self buff while playing solo**, which is why 302 is in: the region around the caster
contains only the caster. It is applied to the caster alone and never expanded — invisible until a party
exists. Asuran Haste is a 302 and was unreachable when the scope was 301 only.

**The target decides who the buff lands on, and getting this wrong buffs the wrong unit.** Supported
`SkillTarget`s are the ones containing the caster: `Target` (1), `RegionWith` (2), `SelfWithSummon` (45)
and `PartyWithSummon` (51, a solo party being just the caster). Refused: `RegionWithout` (3), which
excludes the caster by definition, and `Summon` (31) / `PartySummon` (32), which target a summon that
nothing models. **The first cut ignored `target` entirely, so its 12 summon buffs would have buffed the
player.**

**rzu is authoritative for the wire format, the reference emulator for the logic, and nothing else.** The
emulator writes `hp_cost`/`mp_cost`/`caster_mp` as int16, which is the `< EPIC_7_3` variant — **at 7.3 they
are int32**, the same version boundary as `ATTACK_INFO`.

`TS_CS_SKILL` (`400`, Epic < 9.6.3) is 31 bytes: `skill_id` u16@7, `caster` @9, `target` @13, `x`/`y`/`z`
@17/21/25, `layer` i8@29, `skill_level` i8@30.

`TS_SC_SKILL` (`401`) is **57 bytes** for a buff: 41 fixed bytes then a **9-byte union region** —
`tm` + `nErrorCode` + 3 pad for `ST_Casting`/`ST_CastingUpdate`/`ST_Complete`, or the FIRE header
(`bMultiple`, `range`, `target_count`, `fire_count`, `hits` count = exactly 9) followed by `45 × hits`
bytes, one fixed 45-byte stride per hit. **A buff fires with `hits = 0`**: the emulator's `EF_ADD_STATE`
branch never fills `m_vResultList`, unlike `TOGGLE_AURA`. The buff travels in `TS_SC_STATE`.

`TS_SC_STATE` (`505`) is **63 bytes**: `handle` @7, `state_handle` u16@11, `state_code` @13,
`state_level` u16@17, `end_time` @19, `start_time` @23, `state_value` @27, `state_string_value[32]` @31.
**`state_level` sits after `state_code` at this epic**; rzu only moves it before from 9.5.2. Removal is
the same packet with level/end/start zeroed. An aura would send `end_time = -1`.

The sequence mirrors `Skill::ProcSkill`: `ST_Casting` (mp cost + cast delay) → state + `ST_Fire` → stat
refresh → `ST_Complete`. **The cooldown reaches the client through `TS_SC_SKILL_LIST` (`403`)**, whose
`TS_SKILL_INFO` record already reserved `total_cool_time`/`remain_cool_time`; there is no dedicated
cooldown packet. A failed cast answers one `ST_Casting` with a non-zero `nErrorCode`.

**Every duration column in `SkillResource` is in seconds and needs `× 100` to become ar_time ticks** —
`state_second` *and* every `delay_*`. The reference loader is explicit: `delay_cast`,
`delay_cast_per_skl`, `delay_common`, `delay_cooltime` and `delay_cooltime_mode` are each read as
`GetFloat() * 100`. All the conversions live in `BuffCurve`, which holds every formula:
`duration = (state_second + state_second_per_level × lvl) × 100`,
`state_level = state_level_base + state_level_per_skl × lvl`, `mp = cost_mp + cost_mp_per_skl × lvl`,
`cooldown = (delay_cooltime + delay_cooltime_per_skl × lvl) × 100`.

**This shipped wrong once.** I read `delay_cooltime` as already-ticks because its max of 10800 would
otherwise be a three-hour cooldown, which felt implausible — and wrote up "the column names say which unit
is which" as though it were a finding. Deep Evasion then had a **1.2 s cooldown in-client instead of
120 s**, which the user caught immediately. The loader had the answer all along, one file away from the
logic I was already reading. **Plausibility is not evidence; find the line that converts the value.**

`StateResource.value_0..value_17` are **six `(mask, base, perLevel)` triplets** and
`amount = base + perLevel × state_level`, which the emulator's `SEF_PARAMETER_INC` branch applies in
exactly that order. **Triplets 0, 1, 4 and 5 are ParameterA and triplets 2 and 3 are ParameterB** — the
same A/B split as items, and B stays undecoded and skipped. So `StateCatalog` reuses `ParameterBitset`,
`StatEffect`, `StateEffectTemplate` and `StatCalculator` unchanged; buffs are just a third effect source
next to items and passives. **`SIT_ByItem` stays items only.**

**`StateResource.effect_type` was typed `SkillEffectType` on the entity, which is the homonym trap made
concrete** — it is a different value space (0 Misc, 1 flat add, 2 percentage). It now has its own
`StateEffectType` enum. Only a minority of the castable buffs carry a state with effect 1 or 2 and move
the stats; the rest carry mechanics nothing models (double attack, additional damage) and **still get
their state, icon and countdown while contributing no stats** — the same rule as an unsupported passive
effect type.

`BuffCatalog` and `StateCatalog` are frozen at startup. `ConnectionInfo` holds `ActiveBuffs` (guarded by
`BuffLock`, since the expiry tick and the client thread both touch it), `BuffEffects`, `SkillCooldowns`
and `NextStateHandle`. `SkillCastService` runs a 500 ms expiry tick and, like `GroundItemService`, **must not
inject `NetworkService`** — that is the DI cycle that only throws at runtime. A recast replaces the active
instance of the same state, reusing its `state_handle`; `state_type` (`SG_NORMAL`/`SG_DUPLICATE`/
`SG_DEPENDENCE`) is not mapped, so real stacking rules are not modelled.

**`ConnectionInfo.CharacterMp` did not exist** — MP was only ever sent as a property, never tracked — so
casting had nothing to spend. It is seeded at login and on level-up alongside `CharacterHp`.

`state_code` is the `StateResource` id and the client resolves the icon and name from its own
`db_state.rdb`, like `npc_id` and item codes: **a 9.4-only state id renders nothing**, the same
unresolved 7.3 gap as ground items.

**Percentage values are ratios, not percent numbers.** A `ParameterAmp` state or an `AmpParameterA` item
carries `0.05` for "+5%", and `StatBlock.Amplify` does `stat * (1 + ratio)` exactly like the reference's
`stat.strength = amp * stat.strength + stat.strength`. The client's tooltip is what multiplies by 100.
Confirmed in the data: item amp values run 0.01–0.50, and the states reachable from castable skills run
−0.50 to +0.50.

### Toggle auras

`effect_type` 701/702, 46 player skills. **An aura is a buff with no duration and an off switch**: all 46
carry `state_second = -1`, which is the aura marker, and `TurnOnAura` applies an ordinary state with
`bIsAura`, which puts **`end_time = -1`** on the wire (`uint.MaxValue` here). The expiry tick must skip
them; nothing but the player removes an aura.

**`toggle_group` is the mutual-exclusion key and is what makes this a toggle.** `m_vAura` is keyed by
group, not by skill, so **one aura per group at a time**: recasting the same aura turns it off, and
casting a different aura of the same group swaps it (`AuraToggle.Resolve`). Group `0` is a real group,
not "no group" — auras that share it exclude each other, which is the reference behaviour.

**`TS_SC_AURA` (`407`) is the dedicated packet**, 14 bytes: `caster` @7, `skill_id` u16@11, `status`
byte@13. Same recurring pattern as `287` vs `202` and `210` vs the generic result — the client is told
*the aura is on* separately from *the skill fired*.

Auras are what finally make the rule below true: **an aura must not contribute until it is switched on**,
which is exactly what V13.1 got wrong by applying 32 of them at login. The aura's state feeds
`ConnectionInfo.ActiveBuffs` like any buff, so `StateCatalog` moves the stats with no new code. No upkeep
cost is modelled: the reference charges the mp once, at cast.

### Heals

`effect_type` 501 (`AddHp`) and 505 (`AddHpMp`) with `target = 1`, cast on the caster. `502` (`AddMp`) has
no player skill in this catalog. The formula is `HEALING_SKILL_FUNCTOR` verbatim, in `HealCurve`:

```
heal = magicPoint * (var0 + var1 * lvl) + var2 + var3 * lvl + enhance * var6
     + targetMaxHp * (var4 + var5 * lvl + var7 * lvl)
```

**`var[i]` is our `Values[i + 1]`.** Enhancement is not modelled, so the `var6` term is zero. Verified
against the data: skill 3202 (`0.3 / 0 / 80 / 140`) heals 250 at level 1 with 100 magic attack.

**This is the first formula in the project where a character stat produces a gameplay outcome** —
`magicPoint` comes straight from the `StatBlock` the stat/passive/buff slices built. Everything computed
before this was inert.

**A heal is also the first packet to carry a real FIRE hit.** There is no separate packet for the healed
amount, so `ST_Fire` carries one `HIT_DETAILS`: `type` u8 = `SHT_ADD_HP` (20), `hTarget` u32,
`target_stat` i32 (HP **after**), `nIncStat` i32 (amount). **Each hit occupies a fixed 45-byte stride**,
zero-filled then overwritten, so a one-hit `ST_Fire` is **102 bytes** (48 + 9 + 45). The FIRE header is
`bMultiple` @48, `range` @49, `target_count` @53, `fire_count` @54, `hits` @55 — writing `target_count`
inside `range` is a mistake a golden offset test caught here. Do not copy the reference's serializer for
this: its `SRT_ADD_HP` case **falls through to `SRT_REBIRTH`** on a missing `break` and writes five extra
fields into the padding. rzu says two int32.

### Debuffs

`effect_type` 301/302 with `is_harmful` and `target = 1`, 21 player skills. Mechanically the buff path
with a different owner: the state lives in **`MonsterWorldState`** next to HP and respawn deadlines,
sparse like they are, and `CombatService`'s death branch calls `ClearStates` — a corpse keeps no debuff
and a respawn inherits none. The target must be a **visible monster of that client**, resolved through
`ConnectionInfo.SpawnedMonsters` under `MonsterVisibilityLock` exactly like `CombatService.StartAttack`,
so a debuff can never touch an object the client cannot see.

**A debuff is visible and inert, and that is the honest description.** Monsters carry only `Id`, `Level`,
`Hp` and `Race` — there is no monster stat block, so a state lowering defence lowers nothing. Only 9 of
the 29 harmful `AddState` skills even carry a stat state; the rest are mechanics nothing models. This
slice delivers the icon, the countdown and the plumbing, and becomes real the day monsters get stats and
combat reads them. `probability_on_hit` is imported but resistance is not modelled: a debuff always lands.
Whether this client renders a state icon on a monster at all is **unverified**.

### Offensive skills

`effect_type` 30001 (`PhysicalSingleDamage`, 36 player skills) and 231 (`MagicSingleDamage`, 19), both
`is_harmful` and `target = 1`. The multi-hit (30011, 232) and region (261, 271) variants need several hit
records or area resolution and are out.

**One damage rule, one death path.** `CombatService` owns damage, death, the corpse, drops, reward and
respawn; the cast path must never reimplement any of it. `ICombatService` exposes `GetHitDamage` and
`ApplyDamage`, `ProcessSwing` is refactored onto them, and `SkillCastService` calls them — so an auto-attack
and a skill deal **the same damage through the same code**, and the whole death sequence behaves
identically for free. `SkillCastService` depends on `ICombatService`, never the reverse.

**The damage hit's payload differs from the heal's**, inside the same 45-byte stride: `type` u8@0,
`hTarget` @1, `target_hp` i32@5, then **`damage_type` as a single byte @9** and `damage` i32@10, then
`flag` and `elemental_damage[7]`. `HIT_ADD_STAT` (a heal) instead writes two adjacent int32 from offset 5,
so the two diverge at byte 9. No `TS_SC_ATTACK_EVENT` is sent for a skill: the client reads the damage
from the `ST_Fire` hit, and the death animation still comes from the shared path's `TS_SC_STATUS_CHANGE`.

The target must be a **visible monster of that client** and **alive** — casting at a corpse answers
`NotActable`. **`cast_range` is not enforced**: the column is imported (max 22 for these families) but its
unit is unverified against a world whose coordinates run in the tens of thousands, and the client already
gates the cast. A wrong conversion would refuse legitimate casts, so this stays a documented gap, like
resistance.

**The damage formula is still the placeholder** (max HP / 3), deliberately unchanged here.

## Teleporters and field props

**The portals in the world are not NPCs.** No teleporter NPC exists within 24 839 units of the spawn
point (94454, 126040, Lost Island), so a double-click on a portal is not `TS_CS_CONTACT`. They are
**field props**: map objects carrying an id, a position and a script.

**A prop is used by casting a skill on it.** Double-click makes the client cast the prop's
`FieldPropResource.activate_id` at the prop's handle — an ordinary `TS_CS_SKILL` (`400`). The effect is
`EF_ACTIVATE_FIELD_PROP = 0x251D = 9501` (`SkillResources` 6901-6910); **538 of 763 props use skill
6904**. `BuffCatalog` classifies it as `SkillCastKind.ActivateProp`, so the cast path stays one
dispatch. **A prop's activate skill is never learned** — the client casts it because the prop
advertises it — so the learned-skill gate is skipped for this kind and the level is 1; the prop itself
is the authorisation. `FieldPropUsage` ports `FieldProp::IsUsable` (level, race, job); of the four
`activation_condition` kinds only the learned-skill one is checkable, and the others (quest, item
count, worn item) **refuse** rather than let a gated prop through.

**The `limit_*` race bits are an allow-list, and the reference server reads them backwards.** It tests
one exclusion per race (`race != GAIA && (limit & LIMIT_GAIA)` refuses), but **all 454 spawned props
carry `limit = 15388`** — every race and every class bit set — so at least two of its three tests fail
whatever the race, and *every prop in the world becomes unusable for everyone*. Porting that faithfully
is exactly what shipped first, and the client answered "You may not use this skill on that target" on
every portal. A field that refuses everyone is not describing an exclusion: the only reading the data
supports is an allow-list, under which all-bits-set means everyone. The field discriminates nothing
here and is kept only for a future data set.

A prop enters as `TS_SC_ENTER` (`3`), **63 bytes**: the 26-byte prefix the ground items already use,
then `FIELD_PROP_INFO` (37) — `prop_id` u32 @26, `fZOffset` @30, `fRotateX/Y/Z` @34/38/42,
`fScaleX/Y/Z` @46/50/54, `bLockHeight` @58, `fLockHeight` @59. `type = ET_StaticObject (2)` and
**`objType = EOT_FieldProp (6)`** — rzu is the authority; the emulator's internal `OBJ_STATIC = 0` is a
different enum and must not reach the wire.

**`TS_SC_WARP` is id `12`** at this epic (`< EPIC_9_6_3`): `x`/`y`/`z` floats + `layer` int8, 20 bytes.
`WarpService` mirrors `World::WarpBegin`/`WarpEnd`: stop the attack, `TS_SC_LEAVE` **every** visible
object and clear the visible sets, set the position, warp, then re-sync NPCs/monsters/props. Skipping
the leaves strands the old zone's objects at the new one. `SaveProgressAsync` now writes
`Characters.Position`, **which it never did** — without it a warp is undone by the next login.

Scripts are resolved as a catalog lookup, never executed as Lua, the same rule as NPC dialog triggers.
`PropScript` supports `common_warp_gate(x, y)` (175 props, destination in clear, arrival jittered by
`rand(0,10)` like the reference), `enter_dungeon(id)`/`exit_dungeon(id)` (28) and `RunTeleport(cost, x,
y)` for NPC dialogs. **203 of the 3 189 props teleport**; the rest stream, are visible, and answer
`NotActable`.

**`enter_dungeon` is an approximation, not the original rule.** Its Lua is not in the corpus
(`/tmp/rappelz-ela-all` defines `common_warp_gate` but not `enter_dungeon`), and `DungeonResource`
123000 — the Lost Island portal — is a **level-180 raid dungeon** with `raid_opening_time`/
`raid_closing_time`, party and guild requirements. We warp to `raid_start_pos` and model **none** of
that gating. `RunTeleport`'s `cost` argument is `0` in all 9 dialogs and is not charged.

### Extracting the prop positions

Positions live only in the client's `Resource/NewMap/*.qpf` files, inside `data.00X`; the database has
none. `tools/Export-FieldProps` reads them with `DataCore` and writes `DevConsole/field-props.73.json`
(3 189 spawns, 454 templates, 21 dungeons), loaded with `System.Text.Json` like the spawn and drop
catalogs — never through the configuration provider.

**Three traps, all silent — they corrupt rather than fail:**

- **The client's XOR key differs from `DataCore`'s `DefaultKey` at 5 indices**: `key[40]=0x4a`,
  `[80]=0x9d`, `[87]=0x2d`, `[163]=0x21`, `[236]=0xa9`. With the stock key ~1 byte in 100 decodes
  wrong and yields *plausible* text: `TILE_LENGTH=41` instead of the true **42**, and 11 of 85
  `MAPFILE` lines unreadable. Recovered from the data (modal raw byte per `offset % 256` over `.rdb`
  entries, where plaintext zeros dominate) and confirmed independently — indices 163 and 236 reproduce
  exactly the XOR deltas measured on the corrupted text. Only sample genuinely encrypted files: most
  extensions are stored plain and their modal byte is `0x00`.
- **The key must be set after constructing `Core`** (its constructor resets it) **and before `Load`**:
  the index in `data.000` is itself ciphered, so loading it with the wrong key corrupts entry offsets,
  which then read as garbage.
- **`.qpf` files are version 3 with a 49-byte stride** — a **9-byte tail**, not the 7 the reference
  emulator uses for v2. Following it literally gives NaN coordinates. Verified as `(len - 26) / count
  = 49.000` across all 83 files, and the reader asserts the stride per file.

Geometry: `MapLength = SEGMENTCOUNT_PER_MAP × TILE_LENGTH × TILECOUNT_PER_SEGMENT = 64 × 42 × 6 =
16128`; a prop's absolute position is its file-local `x`/`y` plus `mapX/mapY × 16128`. The exporter
**asserts `MapLength == 16128`** — that assert is what catches a wrong key. It also refuses to write
unless 3 189 spawns over 454 ids resolve with 0 unresolved, 0 NaN and 0 out-of-world.

## Client clock

The client keeps its own notion of server time and every timed UI depends on it. Three packets feed it,
all of them `ar_time_t`: `TS_TIMESYNC` (`2`, bidirectional, `time`), `TS_SC_SET_TIME` (`10`, `int32 gap`)
and `TS_SC_GAME_TIME` (`1101`, `t` + `game_time`).

**`ar_time_t` is a 10 ms tick, never a wall clock.** `ServerClock` is the single place that defines it
(`Environment.TickCount / 10`, `TicksPerSecond = 100`) and everything on the wire goes through it. Three
independent confirmations: rzgame's `typedef ar_time_t rztime_t; // unit [10ms] since first call`; the
reference emulator's `GetArTime() = ms / 10`; and the client itself, since `ITEM_ARRANGE_COOL_TIME = 3000`
greys the sort button for a measured 30 s.

This file used to call it a millisecond tick, and **nothing broke for a long time because no feature
depended on the unit**: movement, drops and combat all derive `start_time` from the client's own tick via
`ConnectionInfo.ClientClockOffset` and never add a duration. That also hid a real defect — an offset
between two clocks ticking at different rates is only valid at the instant it is captured, and movement
survived purely because `TS_CS_MOVE_REQUEST` re-captures it constantly. Buffs were the first feature to
need a duration, which is what exposed it.

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
full replace.

**`index` and `wear_position` are independent fields of `TS_ITEM_INFO`, so a worn item still holds an
inventory position** and the client keeps showing it in the bag. Every ordering path therefore covers
*all* of a character's items; do not filter on `WearInfo == None`.

**`index` is 1-based: the client treats `0` as unset and pushes that item to the end of the grid.**
`InventoryArrange.FirstIndex` is `1` and every path — renumbering, the permutation check and pickup —
starts there. Numbering from zero misplaces exactly one item, which reads as a random glitch rather than
a bug: 16 of 17 items sit correctly and only the index-0 one jumps to the end. Diagnose ordering
complaints by comparing `Items.Idx` in the database against the client grid; the packet order and the
database agreed all along, only the base did not.

`BuildInventory(CharacterEntity)` orders by `Idx` before sending. Login used to stream the EF load order
(by primary key) while claiming `index` was authoritative — the two must not disagree.

`TS_CS_CHANGE_ITEM_POSITION` (`218`, Epic < 9.6.3) is the drag-and-drop swap: `is_storage` (bool @7) +
`item_handle_1` (uint32 @8) + `item_handle_2` (uint32 @12), 16 bytes. It carries no slot number, so it
can only ever swap two existing items; the bag is a dense list rather than a sparse grid. Both handles
must resolve to one of the character's items, worn or not, otherwise the answer is `NotExist`.
`InventoryService.SwapPositionsAsync` exchanges the two `Idx` values and echoes every item back through
`207`, because the normalization that precedes the swap can renumber the others too.

`TS_CS_ARRANGE_ITEM` (`219`) is the inventory sort button: `is_storage` (bool @7), 8 bytes total.
`InventoryArrange` packs the order into one `ulong` per resource — category order (8 bits) `<< 48`,
`group` `<< 40`, inverted `rank` `<< 32`, then the resource id in the low 32 bits — so the sort is a
single integer comparison per pair, with the item id breaking ties to keep repeated arranges stable.
Every key is precomputed once at startup by `ItemSortCatalog` into a `FrozenDictionary`, so the
comparator never touches the database. Unknown resources sort last. The observed ranges make the packing
safe: `group <= 140`, `rank <= 7`, `id <= 700000886`.

The primary key is the client's own tab order — Equippable, Consumable, Cards, Creature, then everything
else — which is `ItemResource`'s `type` column, mapped by `InventoryArrange.CategoryOrder`: `1`
(25,338 of 25,340 rows wearable) is Equippable, `3` (holds group 99 Consumable) is Consumable, `2`
(groups 10 and 13, Skillcard and Summoncard) is Cards, `6` (group 18, PetCage) is Creature, and `0`,
`4`, `5`, `7` fall into Other. This mapping is inferred from what each `type` contains, not documented;
no Quest category was found in the data, so quest items land in Other.

**Beware the 9.4 `ItemResource` column names**: `class` is the `ItemType` enum (113 OnehandAxe, 200
Armor, 401 Soulstone) and `type` is the coarse tab category, while `group` is the `ItemGroup` enum
(1 Weapon, 2 Armor, 17 Bag). Mapping `type` onto `ItemType` compiles and imports cleanly but silently
produces a nonsensical order.

Storage is not implemented, so `is_storage = 1` answers `NotActable` for both packets.

`TS_CS_ERASE_ITEM` (`208`) destroys items: a `count` byte @7 then that many 12-byte records of
`item_handle` (uint32) + `count` (int64). A record whose count reaches the stack amount removes the
`ItemEntity`; a smaller one decrements `Amount`. **Removing the item from `character.Items` is not
enough** — `ItemEntity.CharacterId` is nullable, so EF orphans the row instead of deleting it; the
repository's `DeleteItem` is what actually removes it. The bag is renumbered with
`EnsureContiguousIndices` before saving. The answer is `TS_SC_ERASE_ITEM` (`209`, same counted
12-byte layout, carrying the **erased** count), which since EPIC_7_2 replaces the plain `TS_SC_RESULT`
this packet used to get. The client sends `208` **twice** per destroy action, so the second request
finds nothing left and answers `NotExist`; the client ignores it.

## Database access

`CharacterRepository` is a singleton holding **one long-lived `TelecasterContext`**, and `GameClient`
dispatches every packet handler fire-and-forget (`_ = HandleXxxAsync(...)`), so two packets can reach
the context at the same time — EF then throws *"A second operation was started on this context
instance"*. This is not hypothetical: the duplicate `208` above triggered it reliably.
`CharacterService` is the only consumer of the repository and serializes every operation behind a
`SemaphoreSlim`, so each read/mutate/`SaveChangesAsync` sequence is atomic against the shared context.
Any new repository consumer must go through `CharacterService`, or the guarantee is gone.

**That shared context also masks missing `Include`s, which is a trap.** `GetCharacterByNameWithItems` used
to `Include` only `Items`, yet `character.Skills` was still populated in the equip path — because login
had already loaded it into the same context and EF's identity map returns that instance. Anything reading
a navigation this way works by luck and breaks the day the load order changes. There is no lazy loading
here: nothing registers `UseLazyLoadingProxies`, so a `virtual` navigation is only a promise. The method
now includes `Skills` explicitly.

The original server's ordering could not be recovered exactly. The shipped `Game_bin` PDB proves the
shape — `StructInventory::_ItemArrangeGreater(const StructItem*, const StructItem*)` is a comparator
(so the sort is by item fields, not by name), `StructPlayer::ArrangeItem(bool)` returns a result code,
and `ITEM_ARRANGE_COOL_TIME` with `m_nLastInvenArrangedTime`/`m_nLastStorageArrangedTime` proves a
per-inventory/storage cooldown — but that PDB does not match the shipped executable (RSDS GUID
`8F49E0DD-…` against the PDB's `912BD391-…`, 9 sections against 6), so its addresses are unusable and
the comparator body is unrecoverable without the matching build. The group/type/rank/id order is therefore our own choice.

`ITEM_ARRANGE_COOL_TIME` itself was recovered: the constants live in the PDB as `S_CONSTANT` (`0x1107`)
records, whose payload is the record kind, a type index and a numeric leaf, so the bytes preceding the
name decode to the value. It is **3000**, and the decoding checks out against its neighbours
(`MAX_ACCOUNT_LEN` 60, `MAX_BOOTH_ITEM_COUNT` 8, `DONATE_GOLD_UNIT_COUNT` 10000, `MAX_LAYER` 256).
Spamming is refused with `ResultCode.CoolTime`.

**3000 is in ar_time ticks, so it is 30 seconds** — exactly the delay measured on the client's own greyed
sort button. `InventoryService.ArrangeCooldown` derives it from `ServerClock.TicksPerSecond` rather than
hardcoding it. This file previously read the constant as 3000 **milliseconds** and explained the gap away
with "the server floor is 3 s, the client is simply stricter at 30 s". There was never a gap: one
constant, one clock, read in the wrong unit. Two numbers that refuse to reconcile are a measurement worth
trusting, not an inconsistency to narrate around.

The client's countdown only runs once the clock handshake above has completed; before it existed the
button greyed permanently.

`ItemResources` holds 33,142 rows imported from the 9.4 SQL Server `Arcadia.ItemResource`, with `class`
in `ItemType`, `type` in `ItemBaseType` (the tab category) and `group` in `Group`. Like `MonsterResource`,
only the directly mapped scalar columns were imported; `RaceRestriction`, `SetPart` and `JobRestriction`
are bitfields derived from `limit_*` columns and are left at zero, and the
`NameId`/`SetId`/`SummonId`/`EffectId`/`SkillId`/`StateId` foreign keys are left null because the
referenced resource tables are still empty.

## Current limitations

- Monsters auto-attack (kill + respawn), idle-wander, drop items at authentic rates, **retaliate when
  hit and aggro/chase/attack the player on sight** (aggressive monsters via `FirstAttack`); not
  modelled: taming, group aggro (`GroupFirstAttack`), pathfinding, and **player death** — monster
  damage is the `maxHp/100` test formula floored at 1 HP. Damage-to-monster, attack speed, walk speed
  and the scaled attack range stay placeholders. **An offensive skill deals the same placeholder damage
  as a swing**, through the same `ICombatService` path
- Ground items are visible to their killer only, are not filtered for Epic 7.3 compatibility (the
  client's `db_item.rdb` is unavailable, so a 9.4-only code will not render), and cannot be dropped back
  on the ground by the player
- NPC dialogs render their original text and static follow-up pages, and **`RunTeleport` triggers now
  warp**; other gameplay actions such as shops and quest mutation are not executed yet
- **Field props stream and warp gates work**: 203 of 3 189 props teleport. Not modelled: `use_count`,
  `regen_time`, `life_time`, prop drop tables, `casting_time` interruption, and the quest/item/worn
  activation conditions (those props refuse). **`enter_dungeon` warps to `raid_start_pos` while
  ignoring the raid schedule and the party/guild requirements** — instance dungeons do not exist
- Skill learning, persistence and the **passive stat effects** work, including the 21 `WeaponMastery`
  skills gated on the equipped main-hand weapon; Shield Mastery needs the shield slot
- **Casting works for buffs, toggle auras, heals, monster debuffs and single-target offensive skills**
  (physical 30001 and magic 231): MP cost, cooldown, cast delay, duration, expiry, damage, death and
  reward. **Debuffs are visible but inert** — monsters have no stat block. Not implemented: multi-hit and
  region offensive skills, `cast_range`, expanding a region buff beyond the caster, buffing other players
  (no party), summon buffs, resurrection, region heals, debuff resistance, `state_type` stacking rules,
  cast interruption and buff persistence across sessions
- Equipping and unequipping work and persist and now feed the stats, but item requirements (level, job,
  race) are still not validated
- Stats cover job/JLv/level, equipment, the supported passive skills, active buffs and toggled auras;
  **titles still contribute nothing because nothing can grant one**. `ParameterB` is undecoded for both
  items (63) and states. **Stats still barely drive gameplay**: a heal reads `magicPoint`, but combat
  ignores them entirely — damage is the monster's max HP divided by 3 and attack speed is fixed, so an
  attack-speed buff changes nothing
- Inventory sorting and drag-swap work; storage/warehouse is not implemented and the sort order follows
  the client's tab categories rather than the original server's comparator
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
