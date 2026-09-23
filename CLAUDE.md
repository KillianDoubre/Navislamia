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
.\start-server.ps1 -Watch      # game server under dotnet watch: Hot Reload, the client stays connected
.\launch-client.ps1

dotnet run --project AuthServer
dotnet run --project DevConsole
```

AuthServer must be listening before DevConsole. `-Watch` (Debug only) patches method-body edits into the
running game server; a change Hot Reload cannot apply (signature, new field or enum member, startup code
such as catalogues and DI) restarts it automatically (`DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1`), and a loop
already running (combat, AI, rate ticks) keeps its old body until then. The solution uses the .NET 8 x64 toolchain for all
projects referencing `Game`. PostgreSQL databases are `Arcadia`, `Telecaster` and `auth`.

## Solution layout

- `Game`: networking, packets, EF Core entities, repositories, world services, scripting and maps
- `DevConsole`: generic host for the game server
- `AuthServer`: client login, game-server registration, account storage, crypto and auth packets
- `Configuration`: strongly typed server options
- `MigrateDatabase`: legacy migration utilities
- `Tests`: NUnit, FluentAssertions and FakeItEasy tests
- `docs`: current technical documentation and historical implementation plans
- `docs/packet-specs`: **one sheet per client packet integrated since 2026-09-18**, named
  `<opcode>-<name>.md`. Each sheet is the durable record for that packet: wire layout with a
  `file:line` source per field, the total byte size, the Epic 7.3 decision for every field rzu
  gates by version, what the reference servers do with it, the assumed deviations, and an
  explicit `NON ÉTABLI` section for what could not be established. **Read the sheet before
  touching a packet it covers** — it is where the reasoning lives, so this file does not repeat
  it per packet.

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
**XRC4 is a stream cipher**: the combat, movement and cast ticks and the client's own thread all send on
one connection, and encoding in any order other than the wire order is undecodable — rare enough to look
like a random disconnect. The encoding therefore happens **on the send loop**, in `EncodeOutgoing`, on the
loop's own pooled copy: the loop is the single reader of the queue, so it encodes in wire order by
construction and needs no lock. It used to happen in `Send`, **in place on the caller's array**, under a
lock held across encode-and-queue — which also meant one packet array could never be sent to two
connections (the second copy went out encrypted twice). A packet array is now never modified by sending.

On the receive side, `CipherConnection` decodes each byte **once, in place**, the first time `Peek` or
`Read` reaches it (`_decodedLength`). `Peek` used to decode a copy of the header and roll the keystream
back — a copy plus a 256-byte cipher state per packet — for `Read` to decode the same bytes again.
`Connection` keeps a read offset instead of moving the unread remainder to the front on every `Read`
(quadratic in a coalesced burst); the remainder moves once per receive, in `Listen`.

**A disconnect is signalled by the receive, not polled.** A loop per connection used to wake every
100 ms and call `Socket.Poll(1000 µs)`, which blocks a pool thread for that millisecond whenever nothing
is pending. An orderly close completes the pending receive with 0 bytes and a reset makes it throw; both
call `SignalDisconnect`, once. **`OnReceive` catches whatever the packet handlers throw**: it runs on an
I/O completion thread, where an escaping exception terminates the process — one short
`TM_CS_MOVE_REQUEST` used to be enough. The game receive loop also refuses a frame length shorter than
the header (it would read zero bytes forever) or larger than the 32 KiB buffer (it could never complete).
Covered end to end over loopback sockets by `Tests/Network/ConnectionTests.cs`.

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

The summon socle's server-to-client layouts live in `GameSummonPackets`, sized from
`docs/packet-specs/socle-invocations.md`: `TS_SC_ADD_SUMMON_INFO (301)` 46 bytes,
`TS_SC_REMOVE_SUMMON_INFO (302)` 11, `TS_SC_UNSUMMON (305)` 11, `TS_SC_UNSUMMON_NOTICE
(306)` 15, `TS_SC_SUMMON_EVOLUTION (307)` 38, `TS_SC_MOUNT_SUMMON (320)` 24,
`TS_SC_UNMOUNT_SUMMON (321)` 16. Epic 7.3 gives the name field 19 bytes — 18 usable
characters plus the nul terminator — and `bool` one byte, which is what fixes the 320 size.
Nothing emits these packets yet: how many summons exist, for how long, at what cost and
what they become is still an open decision, so `BuildAddSummonInfo` takes `code` (source not
established) and `summon_handle` from its caller instead of inventing either.

A summon enters the world as `TS_SC_ENTER` (`3`) with `type = ET_NPC (1)` and `objType = EOT_Summon (4)` — the
same rzu authority that fixes 1/2/3/6 for npc/item/monster/field prop, corroborated by NGemity's `SubType`
mapping (`Object.cpp:381`, `Object.h:37`). The packet is **96 bytes**: the 26-byte creature prefix, the 38-byte
shared creature payload, then `master_handle` u32 @64, `summon_code` as an 8-byte randomized `EncodedInt` @68,
the 19-byte name @76 (18 usable, zero padded, the writer the creature window already uses) and `enhance`
(Epic >= 7.1) @95. A trap worth naming: rzu declares those ids one field at a time — `npc_id` (`:105`), an
item's `code` (`:38`) and `summon_code` (`:93`) are `EncodedInt<EncodingRandomized>`, and only `monster_id`
(`:85`) is `EncodedInt<EncodingScrambled>`. Both share the same 8-byte layout (`EncodingScrambled::serialize`
wraps `EncodingRandomized::serialize` after permuting: `EncodingScrambled.h:11-16`), so a single writer serves
all four, but `ScrambledInt.Encode(...)` on a randomized field permutes an id the client reads straight — and a
permuted `summon_code` is a summon that never shows up. `race`, `skin_color` and `energy` are 0 — nothing sets
them for a summon. `max_hp` @38 and
`max_mp` @46 are *not* copies of `hp`/`mp`: the caller supplies them, no reference settles a summon's maxima.
`SummonWorldService.Enter(session, tag, connection, entry)` is their caller: it allocates the handle with
`WorldObjectHandle.Next()`, emits 301 (it fills the creature window) then 3 (it puts the object in the world) —
one call because no login-properties emission exists for summons yet — and `Leave` emits `TS_SC_UNSUMMON` (305)
then `TS_SC_LEAVE` (9) on the master's connection. Only that direct copy is sent: NavisLamia has no
player-to-player visibility, so the regional broadcast of 305/9 from §5.3 is not ported. A summon's position is
never persisted: it is the master's (`ConnectionInfo.X/Y`, `Layer`, `master_handle = CharacterHandle`) plus a
bounded jitter (`AddNoise` in integer arithmetic: `raw % range - range/2`, 70 on summon, 50 on login, 35 on
warp, 0 = exact position); the `z` stays the caller's — NGemity's own summon `z`, never set, is 0 — and the
region-cancel step of `AddNoise` is not portable either, since nothing resolves a position to a location id
here. `code` and `summon_code` carry the same value (`SummonResource.id`, `Summon.cpp:35,88-91`), supplied by
the caller. 302, 306, 307, 320 and 321 still have no caller: their trigger is untranched game policy (unbind
rule, summon duration, evolution table, mount rules). No service writes `MainSummonId`/`SummonSlotItemIds` yet,
and the reference stores *summon* sids in those six columns, not card ids.

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
(`1003`) and `TS_SC_GOLD_UPDATE` (`1001`). The `Exp`/`Jp`/`Gold` rates multiply the three amounts (see
*Rates*). Real per-monster exp and gold live in the `MonsterResource`
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
catalog produced. The `ItemDrop` rate scales every chance (clamped at 1.0) and defaults to **1, the
authentic rate**; the `CreatureCardDrop` rate adds a factor to a slot whose direct item is a summon card
(see *Rates*). It replaced the `GroundItemService.DropChanceMultiplier` constant.

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
disappears. Items expire after `Rates:GroundItemLifetimeSeconds` (120 by default) through a 1 s tick.

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
  attacker and the player as target; the player loses `maxHp / 15` HP (**test formula**), sent as the
  `hp` property. HP can reach 0: that is the player's death (see *Mort et réapparition du personnage
  joueur*), and a monster drops a target at 0 HP. **A monster stands still to attack**: if a chase move is still in flight when it strikes, `StopMove`
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

`tools/Import-SkillResourceColumns.ps1` now imports **every mappable scalar column in one pass** (96 of
them), deriving EF property → source column by introspection and refusing to run if a required column is
unmapped. It reads the CSV export (see *Source data*), not SQL Server. **`UseOnCharacter` and
`UseOnMonster` were listed as "absent from the source" and read `false` for every skill**: they are
`tf_avatar` and `tf_monster`, now overridden — the same trap one more time, and the flag that tells the
Resurrection Scroll's 6001 (a character) from the creature scroll's 6013 (summons only). Two traps it encodes: **a nullable column here is always a foreign key id where `0` means
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

`state_code` is the `StateResource` id, and **how the client turns it into an icon and a name is
not established**. This file used to say it resolved them from its own `db_state.rdb`, like
`npc_id` and item codes. **There is no `db_state.rdb` in this client**: its `data.000` index holds
83 822 entries and exactly 50 `db_*.rdb` files, none of them for states, and no per-state icon
asset exists either (measured 2026-09-18 with `tools/provision-navislamia/extract_client.py`; the
50 names are listed in `reference/client73/extraction-manifest.json` on the pipeline VPS). The
plausible candidates are `db_skill.rdb` and `db_effectresource.rdb`, since a state's visual may
hang off the skill that applied it — but nothing has been read to prove it.

So the practical consequence — **a 9.4-only state id may render nothing** — stays a presumption
rather than a proven mechanism, and "the same unresolved 7.3 gap as ground items" was an
inference from a file that does not exist. **The client does render state icons** (observed
2026-09-23 with `/buff 164401`: icon, countdown and double-click cancel), and **one 9.4-only id is
now observed rendering nothing**: `/buff 41102536` (Guardian of Gaia) moves the stats — the server
side works — but shows no icon, so the player cannot see or cancel it. The mechanism (which client
file lists the known states) is still not established.

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
(`Environment.TickCount64 / 10` truncated to 32 bits, `TicksPerSecond = 100`) and everything on the wire goes
through it. **Not `Environment.TickCount`**: that `int` turns negative after 24.9 days of *machine*
uptime, and dividing it before the cast made the clock jump by ~49.7 days at that instant, so every
`(int)(now - end)` comparison read the past as the future — buffs stopped expiring and cooldowns locked.
The 64-bit count wraps cleanly every 497 days, which the unchecked comparisons handle. Three
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

`is_storage = 1` answers `NotActable` for both packets: the character storage of 211/212 is a
separate path and neither ordering packet acts on it.

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

**Every operation gets its own `TelecasterContext`.** `CharacterService` asks
`ICharacterRepositoryFactory` for a new `CharacterRepository` (a disposable unit of work) per operation,
and `StorageRepository` opens one per call. Both used to be singletons each holding **one long-lived
context**: every character, item and skill loaded since startup stayed tracked, so each
`SaveChangesAsync` scanned all of them and slowed down with uptime, the memory never came back, the two
contexts could read each other's rows stale (the storage risk), and one context forced **every player's
database work through a single global semaphore**. Entities returned by an operation are detached
afterwards: read them, never mutate one to save it later (the old delete path staged a removal and
relied on a separate unawaited `SaveChanges`; `DeleteCharacterByNameAsync` now saves itself).

**What still has to be atomic is a read-modify-write on the *same* character**: `GameClient` dispatches
handlers fire-and-forget (`_ = HandleXxxAsync(...)`) and the client sends `208` twice per destroy. The
`CharacterGate` singleton serialises by character name (64 stripes, bounded), and **`CharacterService`
and `StorageService` take the same gate**, so a storage move and an inventory operation on one character
still exclude each other while two players no longer wait on each other. Pure reads (`CharacterExistsAsync`,
`CharacterCountAsync`, `GetCharacterByNameAsync`, quests) take no gate. All of them are asynchronous now:
the synchronous ones blocked a thread on `SemaphoreSlim.Wait()`. World entry loads **only** the character
entering (`GetCharacterForWorldEntryAsync`, which also checks it belongs to the account), not every
character of the account with all of their items.

**A context per operation has no identity map to hide a missing `Include`, which is the point.**
`GetCharacterByNameWithItems` once included only `Items` and `character.Skills` was populated anyway,
because login had loaded it into the shared context. `SaveLearnedSkillAsync` relied on exactly that and
now loads `GetCharacterByNameWithSkillsAsync`: without the skills, an already learned skill would be
inserted a second time. There is no lazy loading here: nothing registers `UseLazyLoadingProxies`, so a
`virtual` navigation is only a promise. Load what the operation reads.

`Characters.CharacterName`, `AccountName`, `AccountId` and `Items.AccountId` are indexed
(`Version0009_LookupIndexes`): every character operation resolved its row by name, the lobby by account
and the storage by account, each with a full scan. The indexes are not unique — existing data is not
guaranteed to be.

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

## Client anti-cheat packets

`TM_SC_ANTI_HACK` (`53`) and `TM_CS_ANTI_HACK` (`54`) both carry a fixed 402-byte payload: a `uint16`
`nLength` at offset 7 followed by `uint8 byBuffer[400]` at offsets 9-408, for 409 bytes on the wire.
Neither rzu nor NGemity/Chihiro shows a handler: NGemity has the headers and nothing else, and an
unregistered packet there ends in a DEBUG "Got unknown packet" log. The Epic 7.3 client `SFrame.exe`
imports no anti-cheat module at all, and its incoming dispatcher treats `53` as an explicit empty case,
so the shipped client can neither answer the challenge nor produce the `54` blob. `nLength` semantics
are not established by the reference - do not interpret the value. NavisLamia declares `54`, describes
the frame, and consumes the datagram without any disposition; the operational decision (verify, record,
ignore, or refuse) is still open. See `docs/packet-specs/socle-anti-triche.md`.

## Enchères (famille `TM_*_AUCTION_*`, 1300-1310)

`docs/packet-specs/socle-encheres.md` fixe le format de la famille ; le socle est implémenté.
Trois points à ne pas redécouvrir :

- Une seule structure d'objet sur le fil vaut **75 octets** à Epic 7.3 : le motif d'objet de base,
  sans `wear_position` / `own_summon_handle` / `index`. L'inventaire `TM_SC_INVENTORY` y ajoute ces
  dix octets et porte 85 ; les enchères s'arrêtent à 75. Ce motif est écrit une seule fois, dans
  `Game/Network/Packets/Game/ItemFixedInfoWriter.cs` (`Size = 75`, `Write`, `FromItem`) : l'inventaire
  passe par lui, et toute nouvelle famille d'objets doit en faire autant. rzu nomme ce motif
  `TS_ITEM_FIXED_INFO`, NGemity `TS_ITEM_BASE_INFO`.
- Le client **lit `appearance_code`** dans ce motif (offset 71) alors que rzu gate le champ à
  `>= EPIC_7_4`. Le client prime : sans ces 4 octets, chaque entrée d'enchère est désalignée de
  4 octets, et les réponses valent 4979/3739 au lieu de **5139/3899**.
- Les trois réponses `1301` (5139), `1303` et `1305` (3899) copient leur tableau en bloc, **sans
  regarder** `auction_info_count` : `GameAuctionPackets` écrit toujours les 40 emplacements, vides
  ou non, et plafonne le compte à 40.

Les trois identifiants serveur → client (`1301`, `1303`, `1305`) sont dans `GamePackets` et ont un
bras `log + continue` dans `GameClient`, comme `TM_SC_REGION_ACK` : le client ne les envoie jamais,
mais un membre d'enum sans branche atteindrait le `throw "Unknown Packet Type"`. Les sept paquets
client → serveur de la famille (`1300`, `1302`, `1304`, `1306`, `1308`, `1309`, `1310`) restent à
implémenter, chacun avec son bras de dispatch.

L'hôtel des ventes n'est **pas** porté depuis NGemity : il n'y implémente aucun handler, aucune
ressource, aucune mécanique (`SecRouteAuction = 130107` y est une constante orpheline). La
validation vient du client et du modèle déjà présent dans le dépôt (`AuctionEntity`,
`ItemStorageEntity.RelatedAuctionId`, les neuf `StorageType`, la table `AuctionCateryResource`,
lue par `AuctionCateryResourceRepository`).

## Étal de joueur (socle 700/701)

Le client de 7.3 n'ouvre un étal que par `TM_CS_START_BOOTH` (`700`, `59 + 16×N` octets : en-tête 7,
nom de 49 octets terminé par un nul, `type` 1 ou 2, `count` `uint16`, puis des objets de 16 octets
`item_handle`/`cnt`/`gold int64`) et `TM_CS_STOP_BOOTH` (`701`, 7 octets). Les deux constructeurs de
trame sont dans `SFrame.exe` (`0x48CBD0` et `0x48CC20`) et donnent la taille directement.

`TM_CS_CHECK_BOOTH_STARTABLE` (`711`) **n'existe pas dans le client de 7.3** : ni nom, ni créneau de
dispatch. `op_codes.md:180` le liste pourtant — ne pas s'en servir pour déduire un comportement client.

Pendant qu'un étal est ouvert, le client annonce lui-même que l'équipement/usage d'objets, l'usage de
compétences et l'accès à un autre magasin sont refusés (`smsg_booth_not_*` dans `db_string.rdb`), ce qui
correspond au `ResultCode.NotActableWhileUsingBooth` (`55`) déjà déclaré des deux côtés : le socle
réutilise ce code existant au lieu d'en inventer un.

Les objets de `703`/`710` mesurent **83** octets par enregistrement (stride `0x53` mesuré dans le
client) = 75 + `gold int64`, donc la structure d'objet du client **inclut** le `appearance_code` de 4
octets que rzu gate à `>= EPIC_7_4` ; c'est la même conclusion que les 85 octets de l'inventaire, avec
laquelle elle s'additionne exactement (75 + 2 + 4 + 4 = 85). Ne pas reconstruire les 71/81 octets de rzu.

---


`TM_CS_START_BOOTH` (700) et `TM_CS_STOP_BOOTH` (701) sont déclarés dans `GamePackets` **et** dans la
boucle de réception (`GameClient.OnDataReceived`) : un `700` est lu par `BoothPackets.TryReadStartBooth`
(59 + 16×N octets, nom brut de 49 octets terminé au premier nul, `type` à 56, `count` à 57, objets à 59
avec `item_handle`/`cnt`/`gold int64` à +0/+4/+8), jugé par `BoothRules` (type ∈ {1,2}, au moins un
objet, nom de 6 à 40 octets, niveau ≥ 10 — dans cet ordre, le niveau en dernier) et rangé dans
`ConnectionInfo` sous son propre verrou. Refus = `TS_SC_RESULT` avec le code et `request_msg_id = 700`
(`LimitMax` au-delà de 8 objets, `NotEnoughLevel` sous le niveau 10, `InvalidArgument` sinon) ; un `700`
accepté ne reçoit **aucune** réponse et un `701` répond `Success`, idempotent.

Tant qu'un étal est ouvert, **un seul garde** en tête de la chaîne de dispatch (`BoothRules.GateAction`)
répond `55` (`ResultCode.NotActableWhileUsingBooth`) aux actions que le client annonce lui-même comme
refusées : 200, 201, 203, 204, 208, 218, 219, 253, 400. `700` et `701` sont hors de cette liste. Le
garde n'est pas une protection générique : toute action ajoutée plus tard doit être pesée contre elle.

Le garde `DefinedPackets[header.ID]` (`GameClient.OnDataReceived`, une table construite une fois depuis
`GamePackets` ; c'était `Enum.IsDefined`, réflexion et boxing à chaque paquet) précède la
chaîne : un id **non déclaré** est journalisé en `Debug` puis ignoré, **sans exception**. Le
`_ => throw new Exception("Unknown Packet Type")` du `switch` final n'est donc atteint que par un
membre **déclaré** sans bras de dispatch — c'est la raison exacte du critère « enum et dispatch se
modifient ensemble ».

L'état d'étal n'est ni persisté ni diffusé : aucun joueur ne le voit, pas même son propriétaire, et la
validation des handles contre l'inventaire, le sens du `type` et l'unité du `gold` restent ouverts
(`docs/packet-specs/socle-booths.md` §7 et §12).

## Quêtes — socle 7.3 (600/601/603)

- 603 `TM_CS_DROP_QUEST` : 11 octets, `code` int32 à l'offset 7, signé (refuser < 0). Réponse :
  `TM_SC_RESULT` (0) taggé 603 (`Success` 0 / `NotActable` 5) **puis** `TM_SC_QUEST_LIST` (600).
- 600 `TM_SC_QUEST_LIST` : `11 + 61·N + 8·M` octets. Deux comptes u16 obligatoires (actives à 7,
  en attente à 9) — le client 7.3 lit le tableau à l'offset 11 et avance de 61 octets par entrée
  (`SFrame.exe 0x00670cf4`, `0x00670dc0`). Une entrée `TS_QUEST_INFO` : code u32, startID u32,
  value[6], status[6], progress u8, timeLimit u32.
- 601 `TM_SC_QUEST_STATUS` : 40 octets, six `status` u32, `nProgress` int8 à 35, `nTimeLimit` u32 à
  36 (que le client ne lit pas).
- 602 `TM_SC_QUEST_INFOMATION` : le client 7.3 **n'a pas de handler** pour 602 (dispatch
  `0x0067e1d9`, défaut `0x0067ef21`) : ne pas l'envoyer.
- 604 et 605 : le client les émet, le serveur ne les traite pas encore (catalogues et politique de
  récompenses requis).
- Détail, sources et réserves : `docs/packet-specs/socle-quetes.md`.

## GM commands

A chat line whose first character is `/`, on any channel but a whisper, is a command: it is run and
never relayed as chat (NGemity's `WorldSession::onChatRequest` rule). The 7.3 client does forward
such a line — `/position` came back as an echo before this module. `GameClient.HandleChatRequest`
hands it to `GmCommandService` (`Game/Services/GmCommands/`), whose parser, catalogue and argument
rules are pure and tested. Full list, sources and what is deliberately not ported:
`docs/gm-commands.md`.

**Privileged commands need `Characters.Permission >= 100`**, NGemity's threshold, read into
`ConnectionInfo.CharacterPermission` at world entry. Without it a privileged command answers exactly
like an unknown one, so its existence is not revealed. Answers go to the system chat line, sender
`@SYSTEM`, type `0x1E` (`CHAT_EXP`), NGemity's convention.

**No command has a path of its own**: `/warp` is `WarpService.Warp`, `/doit` is
`ICombatService.ApplyDamage` with the remaining HP, `/level` raises the cumulative exp to the target's
threshold and lets `LevelingService.ApplyExperience` run the ordinary level-up, `/item` is
`CharacterService.AddItemAsync` after an `ItemSortCatalog.Contains` check, `/joblevel` credits each step's
exact JP cost then calls `LevelingService.ApplyJobLevelUp` (JP balance unchanged, the button's own
sequence), `/learn` is `SaveLearnedSkillAsync` with the JP untouched and ignores the job restriction,
`/buff` is `ISkillCastService.ApplyState` after an `IStateCatalog.Exists` check, and `/immortal` is a
session flag `MonsterAiRules.PlayerDamage(maxHp, immortal)` turns into a zero-damage swing. A command
therefore cannot produce a state the game itself cannot. `/sitdown`, `/battle` and `/walk` are session states carried by
`ActorStatus.ForPlayer`, which now composes PK, sitting, battle mode and walking — **every status send
must pass all four**, the mask being a snapshot.

**Not ported, on purpose**: `/run` (executes Lua, which this repository never does), `/suicide` (it
**shuts the server down** in NGemity, `World::StopNow`), `/regenerate` (spawning needs a mutable
`SpatialIndex`, which the monster infrastructure does not have) and the party commands (no party).
The `&`-prefixed command lists found online do not exist in this client: none of their strings is in
`SFrame.exe`.

`/rate` and `/rates` read and drive the server rates; see *Rates* below.

## Rates

The server rates are the `Rates` section of `DevConsole/appsettings.{env}.json` — tracked, one per
server, and **read live through `IOptionsMonitor`**, so an edit applies without a restart — multiplied by
the `/rate` event running on that type. A x5 server in a x2 event runs at x10. `IRateService`
(`Game/Services/Rates/`) is the only reader; the keys, their NGemity origin and the GM commands are in
`docs/gm-commands.md`, *Rates*.

- **What they touch**: exp, JP and gold per kill (`CombatService.AwardKill`), the drop chance and the
  summon-card factor (`GroundItemService.DropForMonster` → `DropRoll.Roll`), the monster respawn delay,
  the ground-item lifetime, and the JP cost of a skill level (`SkillCatalog.Evaluate`) and of a job level
  (`LevelingService`). A key exists only once something reads it: no quest, chaos-drop or PvP rate until
  those systems do.
- **`Jp` follows `Exp` when unset**, which is NGemity's single `EXPRate` (`World.cpp:529`).
- **Amounts are rounded at random** (`RateMath.ScaleRandom`: 7 × 1.5 gives 10 or 11), which is what
  NGemity's `GetIntValueByRandomInt64` means to do — **its test is always true, so it always truncates**;
  the intent is ported, not the defect. **Costs are rounded up** (`ScaleCost`), so only a rate of 0 is free.
- **A job-level cost of 0 was the "tier capped" signal.** A `JobLevelJpCost` of 0 would have read as capped,
  so `ILevelingService.NextJobLevelCost` became `TryGetNextJobLevelCost(level, out cost)`: `false` is the
  capped tier, `cost` is what is really charged, and `/joblevel` credits exactly that.
- **The card factor is judged on the slot, before group resolution**, like NGemity's `World::checkDrop`
  (`code > 0`): a card reached through a drop group does not get it.
- **Events** replace, never stack (x2 then x3 is x3); a duration is required; they are saved with their UTC
  end to `EventStatePath` (`DevConsole/rate-events.json`, ignored by git), so a restart resumes them with
  their remaining time. An event stops counting at its end even before the tick removes it.
- **`RateEventTicker` announces the end and the reminder** and holds `NetworkService` for the client list,
  like `MonsterAiService`. `RateService` must not: `CombatService` and `GroundItemService` depend on it and
  are injected into `NetworkService` — the DI cycle that only fails at runtime.
- Whether the 7.3 client accepts a learn or a job-level request priced **below its own table** is not
  established: it may grey the button on its own figure, and it always displays its own price.

**One Telecaster per server.** `Database:TelecasterCatalog` (default `Telecaster`) and
`Database:ArcadiaCatalog` (default `Arcadia`) replace the two names `Program.ConfigureDataAccess` used to
hard-code; `InitialCatalog` is still overridden by them. A second game server sets its own
`TelecasterCatalog`, otherwise it shares the characters of the first.

## Current limitations

- Monsters auto-attack (kill + respawn), idle-wander, drop items at authentic rates, **retaliate when
  hit and aggro/chase/attack the player on sight** (aggressive monsters via `FirstAttack`); not
  modelled: taming, group aggro (`GroupFirstAttack`) and pathfinding; monster damage is the
  `maxHp/15` test formula, and a player at 0 HP is dead until `TM_CS_RESURRECTION` (513) brings them
  back in town, or in place with a resurrection state or a Resurrection Scroll (resurrection by another
  player is not implemented). Damage-to-monster, attack speed, walk speed
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
- Inventory sorting and drag-swap work; the character storage (211/212) moves items between the bag and
  the account storage, but its capacity is unbounded and the two gold modes answer `NotActable` (no
  column holds the stored gold), and the sort order follows the client's tab categories rather than the
  original server's comparator
- The client clock is synchronized, but `TS_SC_GAME_TIME.game_time` (the in-world day/night clock) is
  still zero, and movement still applies `ClientClockOffset` by hand rather than trusting the sync
- Remaining 9.4 resource data has not all been globally filtered for 7.3 compatibility
- Features beyond login, character handling, world entry, movement, chat, stats and object streaming
  remain POC work

## Paquets

### Event areas (7.3): `TM_CS_ENTER_EVENT_AREA` (15) and `TM_CS_LEAVE_EVENT_AREA` (16)

The retail client loads the event-area polygons itself (`.nfe`, one file per map tile next to the
location/attribute files) and has compiled enter/leave notifications for them, but **no reference
proves that the Epic 7.3 client actually emits 15 or 16**; the client's packet name table has no name
for any id in 14..19. Treat these packets as a redundant trigger, never as the only one: the server
already loads the same polygons (`MapService._eventAreaInfo`, `.nfe`, read as id + polygon list only)
and knows the session position, so it checks containment itself instead of trusting the claim.
`EventAreaService` (`Game/Services/EventAreaService.cs`) does it, from the two dispatch branches in
`GameClient.OnDataReceived` *and* from every position change (move request, region update, change of
location); the packet is 15 bytes: header (7) + `event_area_id` (int32, offset 7) + `area_index`
(int32, offset 11).

Containment is `PolygonF.IsIncluded` (`Game/Maps/X2D/PolygonF.cs`, bounding box + crossing parity),
**not** `PolygonF.Contains`, which only compares against the vertex list. Two port errors in
`LineF.IntersectCcw` made `IsIncluded` answer `false` for every point inside any polygon and had to be
fixed against NGemity (`src/X2D/Linef.cpp`): the crossing test compared `ccw123` against itself instead
of `ccw124`, and the Y precheck compared `l2MinY` against its own maximum instead of `l1MaxY`. Also
`PointF` has no value equality, so the reference's "point equals a vertex" shortcut never fires on a
zone corner; and never test a `PolygonF` against `null` — its `==` overload compares to `null` through
the same operator, so `polygon != null` recurses until the stack dies (`ReferenceEquals` instead).
`new PolygonF(BoxF)` throws `NullReferenceException` because it calls `Set` on the null elements of a
`PointF[]` (dead code path today, `MapService` only clones polygons).

Neither rzu nor NGemity has any server packet for event areas, and NGemity has no handler at all
(15/16 fall into its "unknown packet" debug log). The server therefore sends **nothing** back.
`EventAreaInfo`'s other fields (times, level/race/job limits, six activation conditions,
`count_limit`, enter/leave scripts) are not in the `.nfe`: they mirror the `EventAreaResource` table of
`ArcadiaSchemaPSQL.sql`, which nothing imports, so they are all zero/empty today and
`EventAreaInfo.IsActivatable` stays `false`. NGemity's `Telecaster.EventAreaEnterCount`
(player_id, event_area_id, enter_count) shows the retail server kept a per-character, per-area entry
counter, but the socle persists nothing.

The full spec (offsets, sources, version gating, NGemity deltas, scope, open questions) is in
`docs/packet-specs/socle-zones-evenement.md`.

### Paquet 57 — `TM_CS_CHECK_ILLEGAL_USER`

- Trame cliente de **11** octets : en-tête 7 + `log_code` `uint32` à l'offset 7. Taille fixe, aucun
  rembourrage, un seul champ.
- `log_code` est un nom **rzu** ; le client n'envoie que `0` sur le seul chemin d'émission connu.
  Sa sémantique et le sort du paquet côté serveur ne sont pas établis : ne rien en déduire.
- **Aucune réponse** : il n'existe aucune trame serveur → client de cette famille (ni rzu, ni
  NGemity, ni `op_codes.md`), et le répartiteur entrant du client 7.3 place 57 sur le chemin par
  défaut. Le proxy rzu, lui, **jette** le paquet.
- Gating : 57 à l'Epic 7.3, `1057` seulement à partir d'`EPIC_9_6_3` — **ne pas déclarer 1057**.
- Le client 7.3 **émet** 57 depuis sa surveillance interne (événement `game_security_msg`, jamais une
  action du joueur) et affiche une boîte de message du vocabulaire `msgboxdetect_*` /
  `smsq_protect*`. Ce qui déclenche cet événement n'est pas dans le client extrait (module
  anti-triche absent, `data.000` chiffré) : ne pas conclure à l'absence d'émission.
- Traitement : `GameActionPackets.TryReadCheckIllegalUser` (11 octets exacts, toute autre longueur
  refusée et journalisée en `Warning`), puis `log_code` journalisé en `Debug` et abandon — **pas de
  réponse, pas de sanction, pas de déconnexion**. Toute politique (enregistrer, sanctionner) reste à
  trancher.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.

### Paquet 59 — `TM_CS_XTRAP_CHECK`

- Trame cliente de **135** octets : en-tête 7 (`Length` 135, `ID` 59, checksum 194) + `pCheckBuffer`
  `uint8[128]` à l'offset 7, **sans champ de longueur**. Taille constante, aucun rembourrage, aucune
  variante : ne pas l'aligner sur `TM_CS_ANTI_HACK` (54), qui porte un `nLength`.
- Gating rzu : **59** à l'Epic 7.3, `1059` seulement à partir d'`EPIC_9_6_3` (`0x090603` > `0x070300`)
  — **ne pas déclarer 1059**. La paire est 58 (`TM_SC_XTRAP_CHECK`, même anatomie, checksum 193),
  **non déclarée** : ce serveur ne l'émet jamais.
- **Aucun producteur de 59 dans le client 7.3** (`SFrame.exe` sha256 `41e0af2e…` : aucun constructeur
  d'id 59, la chaîne de nom n'est référencée qu'une fois, par la table id→nom) et **aucun handler dans
  rzu ni NGemity** : il n'y a **aucune logique à porter**, seulement une borne d'entrée à tenir.
- Traitement : lecture défensive seule (`GameXtrapPackets.TryReadXtrapCheck`, longueur exacte, tampon
  rendu intact) puis abandon — **pas de réponse, pas de sanction, pas de déconnexion**. Le contenu de
  `pCheckBuffer` n'est **pas établi** et n'est **jamais** journalisé : une trame valide ne laisse que sa
  longueur et la taille du tampon, en `Debug` ; une trame d'une autre longueur, sa longueur seule, en
  `Warning`, et elle est consommée entièrement pour garder la suivante alignée.
- Le client 7.3 **parse 58 dans une branche `switch` vide** : toute réponse serait sans effet
  observable. « Le client ne réagit pas » ne veut pas dire « le paquet est inutile » : un client patché,
  une autre région ou un module tiers peuvent émettre 59.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.

### Paquet 60 — `TM_CS_REQUEST` (client → serveur)

Seule trame **variable** de la famille : `t` (`uint8`, offset 7) + `command` (`endstring`, offset 8,
`L` octets + **1 NUL terminal**), **`Length = 9 + L`**, checksum = somme des 6 premiers octets. Gating :
**60** pour `version < EPIC_9_6_3`, 1060 au-delà — Epic 7.3 garde **60** ; aucun champ n'a de gating
propre. Borne réelle : tampon de réception de 32768 octets → `L ≤ 32759`.

`endstring` n'a **aucun préfixe de longueur** : la fin du champ est la fin du **datagramme**, donc
`L = packet.Length - 9`, jamais « jusqu'au premier NUL » et jamais « jusqu'à la fin du tampon ». Une
trame dont le dernier octet n'est pas le NUL, ou de moins de 9 octets, est refusée.

Le client 7.3 ne nomme ni n'émet 60, et n'a aucun bras en réception (une trame d'id 60 y tombe sur
« message non traité ») : **le serveur ne répond jamais par une trame d'id 60**. Ni rzu ni Chihiro n'ont
de consommateur ; le seul producteur connu est l'outil de supervision NGemity, qui envoie `t = 'u'` et
une requête SQL chiffrée zlib + chiffrement simple encodée en hexadécimal — c'est un canal
d'**opérateur/SQL**, pas un canal de jeu.

Règle tenue par `GameRequestPackets` / `GameClient.HandleRequest` : **lire et borner, journaliser
`Length`, `t` et la longueur de `command`, n'exécuter aucune commande, ne pas répondre, ne pas
sanctionner, ne jamais journaliser le contenu**. Le `t` fait **1 octet** — ce n'est **pas** un
`ResultCode`. Liste blanche et réponse restent des politiques ouvertes.

### Paquet 203 — `TM_CS_DROP_ITEM` (objet lâché au sol)

- **`TM_CS_DROP_ITEM` (203) est implémenté** : trame fixe de **15 octets** — en-tête 7, `item_handle`
  `uint32` à l'offset 7, `count` `int32` à l'offset 11 (gating rzu `version >= EPIC_4_1`, donc
  `int32` en 7.3 ; le paquet bascule à 1203 seulement à partir d'`EPIC_9_6_3`). Aucune position n'est
  transmise : l'objet au sol est créé à la position du joueur (`ConnectionInfo.X/Y/Z/Layer`), sans
  dispersion, avec la durée de vie des drops de monstres (120 s).
- **Réponses** : `TM_SC_DROP_RESULT` (205), **12 octets** — `item_handle` recopié puis `isAccepted`
  `uint8` — précédé en cas de succès de `TM_SC_ENTER` (70 octets, objet au sol, `BuildEnterItem`) puis
  de `TM_SC_ERASE_ITEM` (209, 20 octets pour une paire `handle`/`count`, `BuildEraseItem`). Le retrait
  passe par `CharacterService.RemoveItemAsync`, qui juge les refus et borne le compte **dans la même
  section exclusive** que le retrait (un équipement traité entre deux ne peut pas s'intercaler), et
  renvoie ce qui a réellement été retiré : on n'acquitte `isAccepted = true` que dans ce cas (NGemity acquitte `true` même quand
  `popItem` a échoué — défaut à ne pas répliquer). Aucun `TS_SC_RESULT` de succès, aucun 254/255.
- L'objet lâché n'est **visible et ramassable que par le joueur qui l'a lâché** :
  `TakeAsync` exige `ReferenceEquals(item.Owner, client)` et `ConnectionInfo` ne suit aucun objet au
  sol (pas de `SpawnedItems`). L'écart avec NGemity (diffusion à la région, ordre de ramassage 3/4/5 s)
  est assumé et documenté dans `docs/packet-specs/203-drop-item.md`.
- **Réserves vérifiables** (fiche §7) : l'émission du 203 par le client 7.3 n'est pas prouvée (table
  d'annotation partielle) ; le geste d'émission (aucune classe `SInput*Drop*`) ; le flag de jetabilité
  (`flag_drop` / `item_use_flag` bit 15) n'est pas exploitable dans le dépôt et **aucun refus « non
  jetable » n'est implémenté** — le client refuse déjà localement (`smsg_dump_fail`) ; `count > pile`
  est borné (choix fixé, NGemity refuse en bloc).
- **Un objet équipé est refusé** (`WearInfo != None` → `205 { handle, 0 }`,
  `GroundItemDropRules.IsEquipped`) : aucune référence ne le fait, mais sans ce refus la ligne est
  supprimée alors que ni 202 ni 287 ne partent, et le modèle et les stats gardent l'objet porté.
- La garde NGemity « carte d'invocation liée » est portée : `ItemGroup.Summoncard = 13` correspond à
  `GROUP_SUMMONCARD = 13`, et la garde teste le **bit 31** du bitset retail
  (`GroundItemDropRules.SummonFlagMask = 0x80000000u` = `ITEM_FLAG_SUMMON`). Attention au piège :
  `ItemFlag.Summon = 31` est l'**index** du bit, pas le masque, et `ItemFlag.None = -1` vaut tous les
  bits une fois lu en `uint` (il est exclu explicitement). La garde restera inerte tant que rien
  n'écrit ce bit (`AddItemAsync` ne pose aucun flag).
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.

### Paquets 211 / 212 — entrepôt de personnage (`TM_SC_OPEN_STORAGE` / `TM_CS_STORAGE`)

- **L'entrepôt de personnage (`TM_CS_STORAGE` 212 / `TM_SC_OPEN_STORAGE` 211) est implémenté.** Le
  `212` (client → serveur) est une trame fixe de **20 octets** : en-tête 7, `item_handle` `uint32` à
  l'offset 7, `mode` `uint8` à l'offset 11 (0 = objets inventaire→entrepôt, 1 = entrepôt→inventaire,
  2 = or inventaire→entrepôt, 3 = or entrepôt→inventaire, 4 = fermeture) et `count` **`int64`** à
  l'offset 12 (gating rzu `version >= EPIC_4_1_1`). Le `211` (serveur → client) fait **7 octets en
  7.3, en-tête seul** : le champ `maxStorageItemCount` de rzu n'apparaît qu'à partir d'`EPIC_7_4`
  (`0x070400`), et son `10000` par défaut n'est donc **pas** une capacité 7.3. Les deux ids basculent
  à 1211/1212 seulement à partir d'`EPIC_9_6_3`.
- **Déclencheur : une action de dialogue, pas une fenêtre client.** 20 PNJ du catalogue 7.3
  (`DevConsole/npc-dialogs.73.json`, contacts `NPC_Storage_*`) proposent une entrée de menu dont le
  déclencheur est `open_storage()`, et le client 7.3 porte ce littéral en propre. Le serveur doit donc
  le reconnaître dans `NpcDialogService.Select`, comme il reconnaît déjà `RunTeleport`, puis envoyer
  `211` + le contenu en `TM_SC_INVENTORY` (207, mêmes tranches que l'inventaire) + la propriété
  `storage_gold`. Aucun Lua n'est exécuté.
- **Le stockage est commandé par le compte, pas par le personnage** : rzu (`DB_StorageItem.cpp`) et
  NGemity (`CharacterDatabase.cpp:93-97`) écrivent tous deux
  `account_id = ? AND owner_id = 0 AND auction_id = 0 AND keeping_id = 0` sur la **même** table
  d'objets que l'inventaire (qui est `account_id = 0 AND owner_id = ?`). `ItemStorageEntity` du dépôt
  est l'entrepôt **des enchères** (`StorageType` 1..4/30..34), pas l'entrepôt de comptoir.
- **Réponses et refus** : aucun accusé de succès pour le 212 (NGemity ne fait que `Save(true)`) — ce
  sont les trames d'items (207/255/254) et les propriétés d'or qui informent le client ; les refus sont
  des `TS_SC_RESULT` portant l'id **212** et un `item_handle` recopié, avec les codes NGemity réels
  `NotActable` (5), `NotExist` (1), `AccessDenied` (6), `TooMuchMoney` (53), `NotEnoughMoney` (10).
  **Correction de prémisse** : NGemity déclare `NOT_ACTABLE_WHILE_USING_STORAGE` (51) et
  `TARGET_IS_USING_STORAGE` (88) mais ne les envoie **jamais** ; le client 7.3 sait les afficher, donc
  les employer reste possible, mais ce serait un choix du dépôt et non un portage.
- **Réserves vérifiables** (fiche §7) : la tolérance du client 7.3 à une charge après l'en-tête du
  `211` n'est pas établie (on envoie la forme stricte à 7 octets) ; la **capacité maximale** de
  l'entrepôt en 7.3 n'est établie par aucune source (le `10000` de rzu est ≥ 7.4, la mise en page du
  client n'est pas extraite) et **aucune borne serveur n'est inventée** ; le geste exact qui émet le
  `212` n'a pas été identifié ; le mode 4 (fermeture) doit être accepté sans erreur mais ne doit pas
  être le seul chemin de libération de l'état serveur ; la persistance de l'or d'entrepôt reste à
  trancher (NGemity détourne une ligne d'objet de code 0 — défaut visible à ne pas répliquer).

### Paquet 253 — `TM_CS_USE_ITEM` (utilisation d'un objet)

- Trame cliente de **47** octets : en-tête 7, `item_handle` à 7, `target_handle` à 11,
  `szParameter` sur 32 octets à 15. Le paramètre est consommé pour sa taille seulement : son
  contenu n'est pas établi.
- Un succès consomme **un exemplaire**, sauf pour le type `ItemBaseType.Use` (6, réutilisable, 404
  ressources) comme NGemity `Player::UseItem`. La mise à jour de pile part **avant** le résultat :
  `TM_SC_UPDATE_ITEM_COUNT` (255, `item_handle` + `count` int64, 19 octets) ou, au dernier
  exemplaire, `TM_SC_DESTROY_ITEM` (254, `item_handle`, 11 octets) et la ligne supprimée via
  `DeleteItem`.
- Puis la réponse en **deux** trames, dans cet ordre : `TS_SC_RESULT` (253, `Success`,
  `item_handle`) puis `TM_SC_USE_ITEM_RESULT` (283), qui réémet les deux handles.
- Seul le niveau de l'objet est jugé : `use_min_level` → `LimitMin`, `use_max_level` → `LimitMax`,
  le plafond testé avant le plancher comme dans NGemity `Player::IsUseableItem`. Un handle inconnu
  ou non possédé donne `NotExist`.
- `ItemUseFlag` n'est pas lu : la valeur réellement importée n'est pas documentée dans le dépôt.
  Ne jamais l'utiliser comme masque binaire sans arbitrage.
- Le refus `ACCESS_DENIED` sur le type d'objet de NGemity est du **code mort**
  (`&& false` commenté, `WorldSession.cpp:1327`) : ne pas le porter.
- Les effets de l'objet (`base_type` / `opt_type`) ne sont pas encore appliqués.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.

### Socle artisanat et enchantement — `TM_CS_MIX` 256, `TM_CS_SOULSTONE_CRAFT` 260, `TM_CS_REPAIR_SOULSTONE` 262, `TM_CS_TRANSMIT_ETHEREAL_DURABILITY` 263 / `…_TO_EQUIPMENT` 264

Fiche complète et références : `docs/packet-specs/socle-artisanat-objets.md`.

- **N'a été livré que le socle structurel** : `CraftingSocleService` lit la trame à sa taille 7.3,
  la borne, résout chaque handle non nul contre l'inventaire du personnage, puis **refuse**
  (`InvalidArgument`, valeur 0) — le moteur d'artisanat n'existe pas. Aucune table `MixResource` /
  `EnhanceResource` n'est chargée, aucun taux n'est tiré, aucun châssis n'est touché.
- **Tailles 7.3** : 256 = `15 + 6N` (`N <= 9`) · 260 = 27 · 262 = 31 · 263 = 11 · 264 = **11**.
  Le champ `target` de 264 et le champ `type` de 257 sont gatés `EPIC_8_1` : la trame 8.1 de 264
  fait 12 octets et **doit rester refusée**.
- **256, offset 13 = nombre de slots matériaux** (longueur du tableau écrite par l'émetteur), pas
  un identifiant de recette ; le socle la compare `(Length - 15) / 6` et refuse une divergence.
- **Sentinelles nulles** : les slots vides (4 pierres de 260, 6 handles de 262, cible absente de
  256) sont écrits `0` et ne sont **jamais** résolus — un zéro n'est pas un objet manquant.
- **257 et 261 sont descendants** (`SessionPacketOrigin::Server`) : leur bras de réception les
  journalise et les jette. Un membre de `GamePackets` sans bras atteint le `throw
  Unknown Packet Type` final de `GameClient.Receive`, qui **casse la boucle de réception** :
  énumération et dispatch se modifient ensemble.
- **259 n'est pas établi** : rzu et NGemity y déclarent `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW`,
  `op_codes.md:86` y met `TM_CS_DONATE_REWARD`. La fenêtre de sertissage ne peut pas être émise
  tant que l'id n'est pas tranché, et 260 n'est pas testable de bout en bout sans le
  déclencheur de contact PNJ.
- **Restent à trancher avant tout moteur** (détail en fin de fiche) : taux de réussite, sort des
  châsses en cas d'échec, coût `price / 10`, unité du `rate` de 264, articulation
  `mix_type` 801/802/803 ↔ 263/264.

### Paquet 304 — `TM_CS_SUMMON` (demande d'invocation par carte)

- **Le client 7.3 ne l'émet pas** : l'invocation passe par le sort d'invocation de créature
  (`db_string.rdb:34762`, compétence 4001, `EF_SUMMON = 601` ; renvoi 4002, `602`), donc par
  `TM_CS_SKILL` (400). Le client *nomme* 304 dans sa table id → nom mais n'a aucune classe de message
  pour le construire, alors qu'il en a une pour 303, 323 et 452. NGemity ne le traite pas non plus
  (« Got unknown packet »).
- Disposition 7.3 : en-tête 7 + `int8_t is_summon` (7) + `ar_handle_t card_handle` (8-11), **12 octets**.
  Le sens des deux champs n'est pas établi : ils sont lus bruts (`GameActionPackets.TryReadSummon`,
  trame de moins de 12 octets refusée, trame plus longue lue sur ses 12 premiers) et seulement
  journalisés en `Debug`. **Aucune réponse**, aucune invocation, aucune consommation de carte.
- `1304` est l'id 9.6.3 de ce paquet (`version >= EPIC_9_6_3`). En 7.3, `1304` est
  `TM_CS_AUCTION_BIDDED_LIST` : il ne doit **jamais** être déclaré comme demande d'invocation, et un
  test l'assure — il pourra l'être sous son nom d'enchère.
- Détail et réserves : `docs/packet-specs/304-summon.md`.

### Paquet 324 — `TM_CS_GET_SUMMON_SETUP_INFO` / réponse `TM_EQUIP_SUMMON` (303)

- 7.3 = id **324**, **8 octets** : 7 d'en-tête + `show_dialog` (1 octet à l'offset 7). rzu remappe en
  1324 à partir d'`EPIC_9_6_3` ; le client 7.3 écrit l'id en dur (`0x144` en VA `0x48c662`,
  `Length = 8` en `0x48c66d`).
- Le client l'émet à **l'ouverture de la fenêtre de formation des créatures** (Alt + R, bouton
  `button_formation` ou `button_common_quick_creature_edit`, commande d'interface
  `req_summon_formation`). Avant ce lot, l'id n'était pas déclaré : chaque ouverture laissait un
  `Undefined packet ID: 324` et la fenêtre sans réponse.
- `show_dialog` n'est pas constant (négation du réglage client 44) : le serveur le relit et le rend dans
  `open_dialog`, jamais le fixer.
- **Réponse : 303 seul**, 32 octets, `open_dialog` à l'offset 7 puis six handles aux offsets 8, 12, 16,
  20, 24, 28 (NGemity `WorldSession.cpp:692-695`, `Messages.cpp:122-135`). Aucune écriture en base.
  `BuildEquipSummon(slots, openDialog)` sert les deux sites : l'entrée en jeu passe `false`.
- Les six handles viennent de `ConnectionInfo.SummonSlots`, posé **une seule fois** à l'entrée en jeu
  depuis `CharacterEntity.SummonSlotItemIds` (et remis à vide par `ClearCharacterSession`) : les deux 303
  ne peuvent pas diverger. La colonne n'est alimentée par personne, donc la réponse vaut **six zéros**
  aujourd'hui ; une carte qui l'écrira devra aussi rafraîchir `SummonSlots`.
- **303 va dans les deux sens.** Le client émet aussi 303 (constructeur VA `0x48cd10`, 32 octets,
  `open_dialog = 0`, six `card_handle`) quand le joueur valide sa formation ; sans bras, il atteignait le
  `throw` (observé en jeu : deux `Unknown Packet Type` juste après l'ouverture de la fenêtre).
  `GameClient.HandleEquipSummon` le lit (`TryReadEquipSummon`, 32 octets exacts), le journalise et
  **renvoie la formation stockée** avec l'`open_dialog` reçu. C'est la réponse de NGemity
  (`onEquipSummon`) quand aucune carte n'est retenue : il ne garde qu'une carte d'invocation du joueur
  portant `ITEM_FLAG_SUMMON` (bit 31, carte apprivoisée), dans la limite de Creature Control (1801), puis
  renvoie **toujours** la formation résultante. Rien ne pose ce bit ici (pas d'apprivoisement, `/item`
  n'écrit aucun drapeau) : toute carte est refusée, rien n'est écrit. Le jour où une carte peut être
  apprivoisée, ce bras devient le portage d'`onEquipSummon`.
- Le `throw` final porte désormais l'id (`Unknown Packet Type 303`) : l'erreur nomme le paquet orphelin.
- Détail et réserves : `docs/packet-specs/324-get-summon-setup-info.md`.

### Paquet 452 — `TM_CS_SUMMON_CARD_SKILL_LIST` (client → serveur)

- Trame cliente de **11** octets : en-tête 7 + `item_handle` (`uint32`) à l'offset 7, toute autre longueur
  refusée avant lecture (`GameActionPackets.TryReadSummonCardSkillList`).
- Déclencheur : le bouton `button_flip` de la fenêtre de carte de créature (seul appelant du
  constructeur de trame, `SFrame.exe 0x48EE20`), sous la garde `[fenêtre+0x4C8] ≠ 0` que pose au préalable
  le message interne `SMSG_SUMMON_CARD_ITEM_INFO`.
- **Aucune réponse.** NGemity le déclare sans gestionnaire, rzu ne fournit que le côté client. La seule
  réponse déductible, `TM_SC_SKILL_LIST` (403) avec `target` = handle de l'invocation (NGemity
  `Messages::SendSkillList`), exige la résolution carte → invocation, qui n'existe pas
  (`SummonSlotItemIds`, `MainSummonId`, `SubSummonId` ne sont alimentés nulle part). **Ne pas inventer de
  table carte → invocation, ni réémettre `item_handle` comme `target`.**
- `item_handle` est journalisé en `Debug` : c'est le relevé qui dira ce que le client y met.
- Gating : 452 à l'Epic 7.3, `1452` seulement à partir d'`EPIC_9_6_3` (et `1452` n'a pas de sens en 7.3,
  `op_codes.md`) : ne pas le déclarer.
- Détail et réserves : `docs/packet-specs/452-summon-card-skill-list.md`.

### Familier (pet) — 350-352, entrée dans le monde, filtre 355

- `TS_SC_ADD_PET_INFO` (351) fait **42 octets en 7.3**, alors que rzu et NGemity en déclarent 38 : le
  client lit un 5ᵉ `int32` après `code` (`SFrame.exe` @`0x66f600`, fenêtre `paquet+7`…`+0x29`), méthode
  calibrée sur la 301, dont la fenêtre lue vaut exactement les 39 octets de rzu. Ce 5ᵉ champ n'est nommé
  par aucune source (`unknown`), et `code` n'est **pas** unifié avec `pet_code` : aucune référence ne le dit.
- Le familier entre dans le monde par `TS_SC_ENTER` (3), `type = ET_NPC (1)`, `objType = EOT_Pet (7)` :
  **95 octets**, `master_handle` @64, `pet_code` @68 (8 octets `EncodedInt<EncodingRandomized>`, le même
  écrivain que `npc_id`/`summon_code` — vérifié dans `TS_SC_ENTER.h`), `name` @76 sur 19 octets. Le
  `enhance` de l'invocation (@95) **n'existe pas** pour un familier.
- Il en sort par `TM_SC_UNSUMMON_PET` (350), 11 octets, `handle` @7 : le client retrouve l'acteur, vérifie
  `objType = 7` et le retire lui-même. `TM_SC_REMOVE_PET_INFO` (352) a la même forme mais **ferme la
  fenêtre de créature** : les deux ne sont pas interchangeables. Le `TS_SC_LEAVE` (9) envoyé après la 350
  l'est par symétrie avec les invocations, sans preuve qu'il soit nécessaire.
- 350, 351 et 352 sont serveur → client (le client n'en construit aucune), mais chacune a un bras
  « journal + abandon » dans `GameClient.cs`, sans quoi un id déclaré atteindrait le `throw` final.
- `PetWorldService.Enter`/`Leave` séquencent 351 → 3 et 350 → 9 et **ne décident rien** : placement,
  statistiques (`max_hp` n'est pas `hp`), `race`, `pet_code`, `cage_handle` et les deux `int32` ouverts de
  la 351 viennent de `PetWorldEntry`, fournis par l'appelant.
- **Son appelant est la cage** (`PetSummonService`, fiche §15). Une cage (groupe 18, type `Use`,
  réutilisable) porte l'effet `SummonPet` (90) **sans familier dans ses valeurs** : le familier est la ligne
  de `db_pet.rdb` dont `cage_id` est l'objet. **Source : la table du client 7.3**, 106 familiers et 106 cages
  distinctes, exportée par `tools/export_pet_catalog.py` dans `DevConsole/pet-catalog.73.json` — **pas**
  l'export 9.4, qui réutilise 27 cages pour deux familiers. Après l'acquittement de la 253, un familier à la
  fois : la même cage le range, une autre cage l'échange. Il apparaît aux pieds de son maître, `cage_handle`
  = handle de la cage utilisée, `pet_code` = `id` du client ; il suit une téléportation (`FollowWarp`), pas la
  marche.
- **L'ordre d'entrée est 3 puis 351, jamais l'inverse** : 351 puis 3 **plante le client 7.3**, mesuré en jeu
  par élimination (chaque trame seule passe, 3 puis 351 passe ; fiche §16). L'ordre d'origine, présenté comme
  celui de la référence, venait du socle des invocations : `SummonWorldService.Enter` envoie encore 301 puis
  3 et n'a jamais été essayé en jeu — à vérifier au premier appelant. Les valeurs sans source (niveau 1, PV 100, PM 0, `code`/`unknown` à 0) sont dans
  `PetSummonDefaults`. Rien n'est persisté.
- `ActorStatus.ForPet()` vaut 0, comme les invocations.
- **Il suit, ramasse et se nomme** (fiche §17). `PetBehaviorService` (250 ms, `NetworkService` pour la
  seule liste des clients) fait marcher le familier vers **la destination** de son maître
  (`ConnectionInfo.DestinationX/Y`, dernier point de passage du `TM_CS_MOVE_REQUEST`), à 2 m près, sans
  réémettre de `TS_SC_MOVE` tant que la cible n'a pas dérivé de 3 m, et le rappelle au-delà de 540 unités.
  **Le rayon de ramassage vient de la compétence « Collect Items » (effet 10047, `var1` en mètres : 5/10/15)**,
  **× 12 unités par mètre** (règle de NGemity pour toute portée de compétence) ; le familier prend le butin
  de son maître par le ramassage manuel, **`item_taker` = le familier et aucun `TS_SC_RESULT`**. Le filtre
  355 est déclaré, lu, gardé (`PetPickupFilter`) et **jamais appliqué**. Le nom vit dans `Pets` (une ligne
  par cage) ; un familier jamais nommé reçoit la **353 sur son handle** à chaque appel, l'objet 920010
  (`RenamePet`, 120) la rouvre, refusé **avant consommation** sans familier dehors ; la 354 n'est acceptée que
  pour le handle proposé, avec **la règle des noms de personnage** (4-18 lettres/chiffres, mots interdits),
  un refus rouvrant la boîte.
- `TM_CS_SET_PET_FILTER` (355, 15 octets, `handle` @7, valeur @11) est émis par la fenêtre d'options
  (`PET_PICKUP_FILTER`), mais n'est **pas déclaré** : sa valeur n'est pas établie et le ramassage par
  familier n'existe pas. Il tombe dans `Undefined packet ID`, sans erreur.
- 353/354 (nom du familier) ont leur propre fiche et leur propre branche.
- Détail et réserves : `docs/packet-specs/socle-familier-pet.md` ; tests : `Tests/Game/PetWorldTests.cs`.

### Paquet 408 — `TM_CS_REQUEST_REMOVE_STATE` (annuler un état)

- **`TM_CS_REQUEST_REMOVE_STATE` (408) est implémenté** : trame fixe de **15 octets** — en-tête 7,
  `target` `uint32` à l'offset 7 (handle de la créature dont la fenêtre d'états est affichée),
  `state_code` `int32` à l'offset 11 (le `StateId`). Aucun gating de champ : rzu ne versionne que
  l'id (408 pour `< EPIC_9_6_3`, 1408 au-delà), donc **408 en 7.3**. C'est le clic d'une icône d'état
  dans `window_main_state_h_effect.nui` qui l'émet (les infobulles 9.4 disent « double-cliquer pour
  annuler »), et le client ne le construit que pour un état dont le mot de drapeaux porte `1 << 5` —
  `StateTimeType.EraseOnRequest = 32`, le même bit que `AF_ERASE_ON_REQUEST` de NGemity. **Ce drapeau
  n'était lu par aucune projection du dépôt** : `StateEffectFields` ne transporte que
  `Id`/`EffectType`/`Values`, et `StateCatalog` ne charge que les états à effet de stat (`EffectType`
  1 ou 2) alors qu'`ActiveBuffs` en contient d'autres — d'où la seconde projection
  `GetEraseOnRequestStateIds` → `IStateCatalog.IsEraseOnRequest`. Le savoir du paquet vit dans
  `docs/packet-specs/408-request-remove-state.md`.
- **`state_time_type` n'avait jamais été importé** : `StateResources` portait `0` pour les 1 949
  lignes, comme toutes ses colonnes scalaires hors `EffectType`/`Values` (le piège des littéraux NOT
  NULL, une fois de plus), donc la garde refusait **tout**. `tools/Import-StateResourceColumns.ps1`
  (modèle de `Import-SkillResourceColumns.ps1`, depuis le CSV) importe les 20 colonnes scalaires : 63
  états portent le bit 32, 350 sont `IsHarmful`. Piège : `StateTimeType` est un enum `short`
  (`smallint`) et l'état 201085 vaut `33150` (bit 15, au-delà de tout drapeau déclaré) — le script
  stocke le motif 16 bits tel quel (complément à deux), ce qui garde chaque bit pour le test `&`.
  **Aucune aura et presque aucun buff castable ne porte le bit** : en jeu, on le teste par `/buff`.
  **Validé en jeu le 2026-09-23** avec 164401 (Strength Boost) : icône, double-clic sur l'icône →
  retrait et stats rétablies ; 13472 (sans le bit) ne s'annule pas. 41102536 (Guardian of Gaia)
  applique ses stats mais **n'affiche aucune icône** dans ce client (id 9.4 inconnu du 7.3, voir
  *Buffs*) : il est donc inannulable par la fenêtre, et le retrait de `max_hp`/`max_mp` par la 408
  n'a pas été observé — seul cet état du lot annulable touche les PV/PM max.
- **Réponse** : `TM_SC_STATE` (505), **63 octets**, `state_level`/`end_time`/`start_time` à zéro —
  c'est exactement `BuildStateRemoval`, déjà validé en jeu à l'expiration ; NGemity encode le retrait
  de la même façon (`Messages.cpp:1105-1123`). Suivi de `SendStatRefresh` (`RefreshBuffs` +
  `TM_SC_STAT_INFO`/`TM_SC_PROPERTY`), puis `TS_SC_RESULT` taggé 408 `Success` (choix du dépôt, isolé
  dans `SkillCastService.RemoveState(client, request)`). En cas d'échec, `TS_SC_RESULT` taggé 408
  (`NotExist`/`NotActable`, `InvalidArgument` pour une trame d'une autre longueur) sans aucun effet de
  bord : le paquet n'a aucune réponse dédiée dans tout le protocole.
- **Une aura annulée par cette voie doit être défaite comme une aura** : couper `ActiveAuras` et
  envoyer `TM_SC_AURA` (407) à `false`, comme `RemoveAura` le fait à la bascule. Sans cela, le client
  garde l'icône d'aura allumée alors que le serveur l'a retirée. **Le groupe d'aura du plan est un
  `int?`** : le groupe `0` est un vrai groupe (voir *Toggle auras*), et la première version, qui s'en
  servait comme « pas d'aura », laissait une aura du groupe 0 allumée dans `ActiveAuras`.
- Aucune diffusion : les états ne partent que vers la connexion du joueur concerné, ici comme pour
  l'expiration et la bascule (NGemity, lui, diffuse à la région — écart assumé).
- **Réserves vérifiables** (fiche §7) : l'émission effective de la trame par le client n'est pas
  prouvée par la seule lecture (le dernier saut message interne → socket n'est pas résolu, l'opcode
  n'apparaît dans aucun immédiat du `.text`) ; le geste exact de déclenchement ; le fait qu'une cible
  tierce soit légitime (`window_target_state_h_effect.nui` existe) — décision : n'accepter que son
  propre handle ; aucune valeur sentinelle « tous les états » — décision : code inconnu =
  `NotExist` ; le client juge le bit 32 sur **ses propres** données d'état, que rien n'a lues.

### Mort et réapparition du personnage joueur

Le client Epic 7.3 **déclare** `TM_SC_DEAD` (504) mais son répartiteur le libère **sans effet**
(aucun handler : la table id→nom de `SFrame.exe` mappe 504 vers `TM_SC_DEAD` et le cas `cmp $0x1f8`
saute directement à la queue de libération). Aucun paquet serveur→client de mort n'existe donc :
le joueur est mort quand ses points de vie sont à 0, publiés par `TS_SC_ATTACK_EVENT` (`target_hp`)
et par la propriété `hp`. La phrase précédente de ce fichier (« there is no TS_SC_DEAD in this
version ») doit se lire ainsi.

La réapparition est demandée par le client avec `TM_CS_RESURRECTION` (513) : trame fixe de **12
octets**, `handle` (uint32) à l'offset 7 et `type` (int8) à l'offset 11. En 7.3, `type` remplace la
paire pré-6.1 `use_state`/`use_potion` (qui ferait 13 octets) : 0 = réapparition à la ville,
1 = par état, 2 = par objet, 3/4 = compétition/match à mort. NGemity compile en `EPIC_4_1_1`, donc
son `WorldSession::onRevive` lit `use_state`/`use_potion` et ignore `type` : à traduire, pas à
recopier.

`TM_SC_STATUS_CHANGE` (`500`) avec `1 << 8` est le drapeau mort **d'un monstre** : pour un handle de
joueur le même bit vaut `TCS_FlagSitdown`. Ne jamais envoyer 500 + `1 << 8` pour un joueur.

La fiche complète (enchaînement côté client, écarts NGemity, découpage, réserves) est dans
`docs/packet-specs/socle-mort-respawn.md`.

Le socle est en place : `TM_CS_RESURRECTION` (513) est décodé (trame de 12 octets, toute autre taille
refusée plutôt que lue), le personnage réapparaît à sa position persistée avec ses PV/MP au maximum,
et un monstre lâche une cible tombée à 0 PV (les PV d'un joueur n'ont plus de plancher à 1). Le
serveur n'émet toujours aucun paquet de mort.

**La voie « état » (513 type 1, `RT_UseState`) est livrée** (`docs/packet-specs/socle-effets-resurrection.md`) :
port de NGemity `Unit::ResurrectByState`. Le mort doit porter un état d'effet `SEF_RESURRECTION`
(**109**, `StateEffectType.Resurrection`) posé de son vivant — l'état 13472 du buff 3472 (métier 112),
ou `/buff 13472` pour tester. On garde l'état de **plus haut niveau**, PV rendus
`(value_0 + value_1 × niveau) × PV max` (planchés à 1, emprunt à `Unit::Resurrect`), PM
`(value_2 + value_3 × niveau) × PM max` ajoutés aux PM gardés, puis l'état est **consommé**
(`ISkillCastService.RemoveState`) et 513 répond `Success`. **Sur place, sans `Warp`**, contrairement à
la ville. Sans état : `NotActable`. La mort ne retire aucun état ici, donc l'état posé avant la mort
est toujours là.

**La voie « objet » (513 type 2, `RT_UsePotion`) est livrée** : le mort consomme un objet de
résurrection de son sac et revient **sur place**. Le lien objet → compétence n'est ni
`ItemResources.SkillId` (vide, et réservé aux cartes de compétence) ni `ItemEffectInstant.Resurrection`
(4, sur aucun objet) : c'est l'emplacement d'effet `ItemEffectInstant.Skill` (5), `var1` = compétence,
`var2` = niveau (NGemity `Unit::onItemUseEffect`). Le Parchemin de résurrection 603002 porte
`opt_type_0 = 5`, 6001, niveau 1 — **10 % des PV max** (`SKILL_RESURRECTION` : `PV max × var0 ×
niveau`, `PM max × var1 × niveau` ; 30501 a sa propre formule). `ResurrectionItemCatalog` ne retient que
les compétences 504/30501 qui visent un personnage (`UseOnCharacter`, colonne `tf_avatar`) : sur les
données réelles, 603002 est le seul objet. Réponse : 255 (ou 254 à la dernière unité), `hp`/`mp`, puis
513 `Success` ; sans objet, `NotActable`. `ConnectionInfo.ResurrectionInProgress` refuse une seconde
demande pendant que la première attend la base, sinon deux demandes groupées consommeraient deux
parchemins. Test en jeu : `/item 603002`, `/die`. **Les voies 3 et 4 ne sont pas des arènes** : les
arènes de bataille (4701+) sont toutes `Since EPIC_8_1` et n'existent pas en 7.3
(`socle-arenes-bataille.md`) ; `RT_Compete` relève du duel (4500) et `RT_Deathmatch` des instances
(4250), qui n'existent pas encore — le refus est la réponse exacte.

### Paquet 550 — `TM_CS_GET_REGION_INFO` / réponse `TM_SC_REGION_ACK` (11)

- 7.3 = ids **550** (CS) / **11** (SC) : rzu remappe en 1550/1011 à partir d'`EPIC_9_6_3`
  (`TS_CS_GET_REGION_INFO.h:9-11`, `TS_SC_REGION_ACK.h:11-13`) ; `EPIC_7_3 = 0x070300` est sous
  `0x090603`. NGemity compile en `EPIC_4_1_1` et confirme la branche basse.
- **15 octets des deux côtés** : en-tête 7, `x` (float) @7 et `y` (float) @11 pour la demande
  (taille confirmée par le constructeur client VA `0x684b60`, `Length = 0xf`) ; `rx` (int32) @7 et
  `ry` (int32) @11 pour la réponse. Aucun autre champ, aucun handle.
- Le client 7.3 construit lui-même la 550 dans `SGameWorld::Process`, à chaque **franchissement de
  frontière de région** — pas à chaque pas : elle n'est pas un flux, et le client ne redemande pas
  tant que sa paire d'indices n'a pas changé (caches `0xc4f6e0` / `0xc4f6dc`).
- **Diviseur : `WorldVisibility.RegionSize` (180)**, la valeur annoncée au login dans
  `TS_SC_LOGIN_RESULT.RegionSize` (`GameActions.cs:128`). **Jamais** `WorldOption.RegionSize`
  (150) : c'est le défaut pré-login du client (global `.data` `0xc20508`), et l'utiliser fait
  dériver la fenêtre de visibilité du client d'un facteur 6/5.
- Division **tronquée vers zéro** (`(int)(x / 180f)`), jamais arrondie ; **ne pas** caster en `uint`
  (une position négative deviendrait un indice énorme).
- `rx`/`ry` sont les indices de la grille de régions **du client** (fenêtre 7 × 7, rayon 3), pas des
  identifiants de bloc terrain. Ils se calculent sur les `float` **reçus dans la 550**, pas sur
  `ConnectionInfo.X/Y` (qui peut retarder d'un déplacement).
- Réponse **au seul client demandeur**, jamais diffusée. `Length != 15` → journal + abandon, sans
  `TM_SC_RESULT` ; `ConnectionInfo.CharacterHandle == 0` → journal + abandon.
- NGemity ne lit jamais la 550 (`WorldSession.h:59-122`) et pousse la 11 de sa propre initiative
  depuis `World::enterProc` (`World.cpp:287-302`) : écart assumé, le push reste hors périmètre.
- Restes ouverts (voir la fiche) : la 11 est-elle indispensable, redemande-t-elle après un warp,
  150 vs 180, contrôle de taille côté client.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.

### Statut d'acteur et mode PK (sous-socle)

`status` — l'information de créature de `TM_SC_ENTER` (3, offset 26) et `TM_SC_STATUS_CHANGE`
(500, `handle` @7 puis `status` @11, 15 octets) — est un **instantané complet de l'acteur, jamais
un delta** : publier un seul bit éteint tous les autres. Il ne se compose donc plus en dur :
`ActorStatus.ForPlayer(bool pkModeOn)` / `ForMonster(bool dead = false)` / `ForNpc()`
(`Game/Network/Packets/Game/ActorStatus.cs`) est le point unique des quatre sites d'envoi
(`GameActions` deux fois — entrée en jeu et trame 500 —, `CombatService` à la mort du monstre, et
`GameSpawnPackets.BuildEnterCreature` dont le statut est devenu un paramètre). Les bits vivent dans
`CreatureStatus` (`Game/Network/Packets/Enums/CreatureStatus.cs`) avec leur source rzu :
`PlayerPkOn = 1 << 11` est le **seul** bit du mode PK, et `1 << 8` vaut « mort » pour un monstre et
« assis » pour un joueur — ne jamais envoyer un masque de mort sur un handle de joueur.

`ConnectionInfo.PkMode` porte l'état de session : lu depuis `Characters.PkMode` dans
`GameActions.OnLogin`, remis à `false` par `ClearCharacterSession`, réécrit par
`CharacterService.SaveProgressAsync` (d'où le paramètre `bool pkMode`). Aucune migration : la
colonne existe depuis `Version0001_TheBeginning`. Le protocole n'a **aucun paquet serveur PK** —
`800` et `801` n'existent pas encore côté serveur, donc rien ne bascule `PkMode` en jeu aujourd'hui.

Les tests d'offsets des deux trames sont dans `Tests/Game/PkModeStatusTests.cs`.

### Paquet 902 / 903 — `TM_SC_WEATHER_INFO` / `TM_CS_GET_WEATHER_INFO`

(Epic 7.3 ; fiche `docs/packet-specs/902-weather-info.md`)

`TM_SC_WEATHER_INFO` (902, 13 octets : `region_id` `uint32` à 7, `weather_id` `uint16` à 11) et
`TM_CS_GET_WEATHER_INFO` (903, 11 octets : `region_id` `uint32` à 7). Les ids `1902`/`1903` sont
`version >= EPIC_9_6_3` et ne doivent pas être ajoutés.

Deux pièges. (1) `region_id` n'est **pas** un indice de région de visibilité (la 550/11, pas de
180) : c'est l'id de `WorldLocation`, encodé `x × 10000 + y × 100 + n` sur les colonnes `x`/`y`
de la table ; NGemity y met l'id de l'emplacement, rzu y met 0. (2) La table a **une ligne par
`(id, weather_id, time_id)`** (la copie client en compte 6497 lignes et 407 ids, dans un ordre
physique qui n'est pas groupé par id (114 ruptures de l'ordre `(id, weather_id, time_id)`), ce qui
rend le tri `ORDER BY Id, WeatherId, TimeId` du dépôt nécessaire) : elle doit être repliée en un
enregistrement par id avec `weather_ratio[weather_id][time_id]`, comme
`WorldLocationManager::RegisterWorldLocation`, sinon les lignes s'écrasent.

Le client 7.3 **consomme** la 902 (il lit `+7` et `+11`) et, dans le binaire fourni, **n'émet
jamais** la 903 : la constante `0x387` n'y apparaît que dans la table id→nom, sans constructeur.
Aucune référence (NGemity, rzu) n'implémente de réponse à la 903. La réponse est donc défensive :
une 902 à l'id demandé si l'id est connu, rien sinon.

NGemity ne charge la table que pour la replier, **n'affecte jamais `current_weather`** (sa 902 vaut
toujours `weather_id = 0`) et ne pousse la 902 qu'au changement d'emplacement, calculé depuis les
données de carte du client — données que Navislamia n'a pas. Le socle suit rzu : une 902 `{0, 0}`
à l'entrée dans le monde. L'appariement position → id d'emplacement reste `NON ÉTABLI` (taille de
cellule inconnue) et mérite une carte dédiée.

### Paquet 1202 — `TM_CS_EMOTION` (émotion)

- 7.3 = ids **1202** (CS) / **1201** (SC) : rzu remappe en 2202/2201 à partir d'`EPIC_9_6_3`
  (`TS_CS_EMOTION.h:8-10`). NGemity compile en `EPIC_4_1_1` et ne voit pas ce gating.
- Trame cliente de **11** octets : en-tête 7, `emotion` (int32) à 7. Réponse de **15** octets :
  en-tête 7, `handle` (uint32) à 7, `emotion` (int32) à 11. Aucun tableau, aucune chaîne.
- La valeur d'émotion est **opaque** : le client 7.3 la résout lui-même (14 animations `emote_*`,
  14 icônes `icon_emotion_0001..0014`, 11 messages client `smsq_emotion_*` id 700…710). Le serveur
  la réémet telle quelle — **ne jamais** écrire de table émotion → animation ni de borne
  d'intervalle : l'ordre des ids n'est pas établi (l'asset d'interface est dans les archives
  `data.001..008`, absentes).
- Le serveur répond par un simple **écho** : `handle` = `ConnectionInfo.CharacterHandle`,
  `emotion` inchangée, checksum recalculé. **Aucun `TM_SC_RESULT`** n'est identifié pour 1202, et
  aucun refus n'est inventé sur la valeur.
- La boucle de réception ne garantit que `Length`/`Checksum` : la garde de taille (11 octets) est
  dans le handler, et elle répond par un `Warning` seul.
- Portée : Navislamia n'a **aucune** visibilité joueur↔joueur (`TS_SC_ENTER_PLAYER` n'est envoyé
  qu'au client qui entre, `GameActions.cs:180`) : n'émettre que vers l'acteur tant qu'elle n'existe
  pas.
- Aucun traitement dans NGemity ni dans rzu (0 occurrence) : rien à porter. Ne pas confondre avec
  `CHAT_EMOTION` (0x5, type de chat reçu par la passerelle, `IrcClient.cpp:189`), qui n'est pas le
  véhicule de l'émotion.
- Restes ouverts (voir la fiche) : le client émet-il 1202 ou un `CHAT_REQUEST` de type
  `CHAT_EMOTION` ; la portée réelle de la 1201.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.

### Socle instances de jeu — 4250-4253 et famille HuntaHolic 4000-4012

**17 opcodes, tous `X(<id>, true)` chez rzu : aucun n'est renuméroté en 7.3.** Ils n'existent
qu'à partir d'`EPIC_6_3` (4250-4253, 4011, 4012), et `EPIC_7_3 = 0x070300 > EPIC_6_3`, donc tous
valides. Fiche complète : `docs/packet-specs/socle-instances-jeu.md`.

**Le piège de cette famille est le gating des champs de 4253** :
`TS_SC_INSTANCE_GAME_SCORE_REQUEST` porte cinq champs `version >= EPIC_8_1`
(`battle_arena_point`, `battle_arena_mvp_count`, `battle_arena_record_classic/slaughter/bingo`,
32 octets au total). En 7.3 le paquet fait **23 octets**, pas 55 : `holicpoint` à 7,
`bearroad_ranking` à 11, `deathmatch_kill_count` à 15, `deathmatch_death_count` à 19. Le client
7.3 lit 16 octets de charge utile — c'est la source de vérité.

**Tailles à écrire en dur** (source : rzu + constructeurs du client 7.3) :
4250 = 11, 4251 = 7, 4252 = 7, 4253 = 23 ; 4000 = 11, 4001 = 23 + 38·N, 4002 = 45,
4003 = 56, 4004 = 28, 4005 = 7, 4006 = 48, 4007 = 15, 4008 = 7, 4009 = 11, 4010 = 7,
4011 = 7, 4012 = 7. Les chaînes de 4003/4004 sont des tampons **fixes** de 31 et 17 octets
(NUL compris) ; `ar_time_t` de 4009 vaut **4 octets** ; le pas du tableau de 4001 est **38**.

**`TM_CS_INSTANCE_GAME_ENTER` (4250) est une réponse du client** : le client copie dans sa charge
utile un `int32` lu dans le message entrant qui la déclenche. Le serveur ne peut donc pas la
provoquer tant que ce message n'est pas identifié (`NON ÉTABLI` (b) de la fiche).

**Ne pas porter NGemity** : les 17 opcodes y sont déclarés et jamais traités, et le bloc
`Skill.cpp:1418-1433` (`INSTANCE_GAME_ENTER`, `WARP_TO_HUNTAHOLIC_LOBBY`, `INSTANCE_GAME_EXIT`)
est entièrement commenté. Les compétences 64818 et 64827 sont définies mais jamais appelées.

**Déjà en place dans Navislamia** : `CharacterEntity.HuntaholicPoint` /
`HuntaholicEnterCount` (`CharacterEntity.cs:51-52`), `PartyType.HuntaholicParty`,
`StateTimeType.EraseOnQuitHuntaholic`, `ItemEffectInstant.IncHuntaholicPoint`,
`ItemUseFlag.CantUseInHuntaholic`, et les tables de ressources HuntaHolic/InstanceDungeon
(`ArcadiaSchemaPSQL.sql`). Le travail est purement protocole.

**Socle minimum** : 4250/4251/4252 + 4253, seuls opcodes sans état et testables seuls. Découpage
en 6 paquets (S1…S6) : §5.4 de la fiche.

**Lot S1 implémenté** (`3fc8b8c`) : les 4 ids `TM_CS/SC_INSTANCE_GAME_*` sont dans `GamePackets`
**et** routés dans `GameClient.OnDataReceived` (aucun n'atteint le `throw` final), les tailles
11 / 7 / 7 / 23 sont dans `Game/Network/Packets/Game/GameInstanceGamePackets.cs` et verrouillées
par `Tests/Game/InstanceGamePacketsTests.cs`. La 4253 répond à la 4252 **seulement** et porte
`CharacterEntity.HuntaholicPoint` ; les trois champs de score sans source en 7.3 partent à zéro
(placeholder, §9.4 de la fiche). Les lots S2…S6 (famille HuntaHolic 4000-4012) restent à faire.

### Paquets 240 / 250 — marché NPC (`TM_SC_NPC_TRADE_INFO` / `TM_SC_MARKET`)

- 7.3 : **240** est l'écho d'**une seule** transaction — `is_sell` (`int8` à 7), `code` (`int32` à
  8), `count` (`int64` à 12), `price` (`int64` à 20), `huntaholic_point` (`int32` à 28, présent car
  `>= EPIC_5_2`), `target` (`uint32` à 32) : **36 octets**, sans `arena_point` (`>= EPIC_8_1`).
  **250** ouvre la fenêtre — `npc_handle` (`uint32` à 7), compte `uint16` à 11, puis des lignes de
  **16 octets** (`code` `int32`, `price` `int64` absolu, `huntaholic_point` `int32`) : `13 + 16n`.
  Forme **compacte** : rzu ajoute `4n` octets finaux non gatés, dont le client n'a pas besoin
  (`count << 4` depuis `+0xd`, `0x66ffa5`).
- Les deux ids sont **strictement serveur → client**. Comme `TM_SC_REGION_ACK` (11), ils ont dans
  `GameClient.cs:803-811` un bras « anomalie » qui journalise en `Warning` et fait `continue` : ne
  jamais les laisser atteindre `_ => throw new Exception("Unknown Packet Type")`.
- **240 n'a aucun producteur** hors des gestionnaires de `TM_CS_BUY_ITEM` (251) / `TM_CS_SELL_ITEM`
  (252), restés hors périmètre ; `GameTradePackets.BuildNpcTradeInfo` est livré pour eux.
- Le déclencheur des marchands est le littéral **tronqué** `open_market(` (176 entrées de
  `DevConsole/npc-dialogs.73.json`) : `PropScript.Parse` l'accepte **avec ou sans** parenthèse
  fermante et rend `PropActionKind.OpenMarket` avec le nom du marché, vide dans la forme tronquée.
  `NpcDialogService.Select` route vers `MarketService` et **laisse le dialogue courant**.
- `MarketService` n'envoie 250 que si le nom résout un catalogue **non vide** ; sinon il refuse en
  `Warning` — jamais de fenêtre vide, aucun producteur connu d'un `250` de 13 octets (`n = 0`).
- Le catalogue vient de `DevConsole/market-catalog.73.json` (section `"MarketCatalog"`, livré
  **vide**) via `MarketCatalogOptions` / `MarketCatalog` : regroupement par `name`, tri par
  `sort_id` (égalité = ordre du fichier), comparaison ordinale, lignes de `code` nul écartées.
  `price` est le prix **absolu**, pas le `price_ratio` de la base (multiplié par le prix de base à
  l'ouverture, `ObjectMgr.cpp:851`) ; `huntaholic_point` est émis, attendu `0`
  (`ObjectMgr.cpp:852`).
- **Bloqué par des données, pas par du code** : correspondance PNJ → nom de marché (le nom était
  concaténé en Lua et n'a pas été capturé) et lignes de `MarketResource` (ni SQL Server ni
  PostgreSQL ici). Sans elles, tout marchand est refusé et journalisé.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.

### Paquet 9005 — `TM_CS_SECURITY_NO` (client → serveur, 30 octets)

Fiche : `docs/packet-specs/9005-security-no.md`. En 7.3 : `Length` (4) + `ID` = 9005 (2) +
`checksum` (1) + `mode` (`int32`, offset 7) + `security_no` (19 octets, offset 11, lu jusqu'au
premier zéro). L'id passe à `8105` à partir d'`EPIC_9_6_3`, et `account(64)`/`result`/`security_no_1/_2`
n'existent qu'à partir d'`EPIC_9_6_7` : ne jamais recopier un relevé 9.x (94 octets au lieu de 30).
Le client 7.3 émet réellement ce paquet (constructeur de trame en `0x48cf70`, appelé depuis `0x6658a1`
et `0x49dd9e`) en réponse à `TM_SC_REQUEST_SECURITY_NO` (9004, `int32 mode`) — que ce serveur n'émet
jamais : **tout 9005 reçu est donc non sollicité**. `mode` nomme l'opération (`0` aucun, `1` ouverture du
coffre, `2` suppression de personnage) mais **aucune source ne fixe le domaine effectivement émis** : ne
pas valider `mode`. Aucune référence n'implémente la réponse, et Navislamia n'a ni stockage du code ni
transport 40000/40001 vers le serveur d'authentification : la vérification est **hors périmètre** tant
que Killian n'a pas tranché (la référence stocke `md5(sel + code)` dans le champ `password` de la table
`account` de la base d'authentification).

**Le code est un secret réutilisable, traité comme tel** :
- jamais journalisé : le bras ne journalise que `mode` et la **longueur** du code, et ne répond rien ;
- jamais copié en `string` : `GameSecurityPackets.TryReadSecurityNo(packet, out mode, out securityNo)`
  (30 octets exacts, 18 caractères au plus) rend le code comme une **vue** (`ReadOnlySpan<byte>`) sur la
  trame. Une chaîne serait une copie immuable sur le tas que rien ne peut effacer. Le jour où un
  vérificateur existera, il comparera cette vue en temps constant ;
- effacé après lecture : `HandleSecurityNo` met la trame à zéro (`CryptographicOperations.ZeroMemory`)
  dans un `finally`, trame mal formée comprise, et `Connection.Read` efface du tampon de réception
  chaque octet consommé. Ce tampon vit aussi longtemps que la connexion et `CipherConnection` y déchiffre
  sur place : sans cet effacement, le code y restait en clair jusqu'à ce qu'un autre trafic l'écrase.
  L'effacement profite à toute trame secrète, pas seulement à la 9005.

Code : `GamePackets.TM_CS_SECURITY_NO = 9005`, lecteur `GameSecurityPackets.TryReadSecurityNo`, bras de
dispatch et `HandleSecurityNo` dans `Game/Network/Clients/GameClient.cs`, tests
`Tests/Game/SecurityNoPacketsTests.cs` et `Tests/Network/ConnectionTests.cs` (tampon de réception).

### Socle stockage commercial — `TM_SC_COMMERCIAL_STORAGE_INFO` (10003), `TM_SC_COMMERCIAL_STORAGE_LIST` (10004), `TM_CS_TAKEOUT_COMMERCIAL_ITEM` (10005)

- 7.3 = **10003 / 10004 / 10005** : rzu bascule cette famille sur 9003/9004/9005 à partir
  d'`EPIC_9_6_3` (`TS_SC_COMMERCIAL_STORAGE_INFO.h:12-13`, `…_LIST.h:20-21`,
  `TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:12-13`) et `EPIC_7_3 = 0x070300` est sous `0x090603`.
  **Piège** : en 7.3, 9004 et 9005 désignent déjà la famille « numéro de sécurité »
  (`op_codes.md:270-271`) — ne jamais s'en servir comme ids de ce socle. Aucun champ de ces trois
  paquets n'est gated par version.
- Tailles, telles que livrées : 10003 = **11 octets** (`total_item_count` u16 @7, `new_item_count`
  u16 @9) et 10004 = **9 + 10 × n** (`count` u16 @7, puis n entrées de 10 octets = `uint32`
  `commercial_item_uid` @0, `int32 code` @4, `uint16 count` @8, première entrée à l'offset 9) dans
  `Game/Network/Packets/Game/GameCommercialStoragePackets.cs` ; 10005 = **13 octets** (`uint32`
  `commercial_item_uid` @7, `uint16 count` @11) lus par `GameActionPackets.TryReadTakeoutCommercialItem`,
  seule longueur acceptée. `TM_SC_COMMERCIAL_STORAGE_INFO` est émise à `0/0` à l'entrée en jeu, comme
  rzu, suivie d'une 10004 vide (9 octets, `count = 0`) — cette seconde ligne est une décision de
  Navislamia, rzu ne l'émet pas, et se retire d'une ligne (`GameActions.cs:259-260`).
- Le client 7.3 **ne recoupe jamais** le `count` de la 10004 avec `Length` : écrire exactement
  `9 + 10 × count` octets. Une liste vide (9 octets, `count = 0`) est un état traité explicitement
  par le client.
- **Le serveur n'émet jamais 10005.** La seule trame 10005 du client est un envoi (constructeur de
  trame client VA `0x48ce60`, appelé une fois depuis l'émetteur VA `0x49dc59`), et le seul
  traitement identifié d'un message interne `0x2715` en réception est un envoi de `TM_CS_LOGOUT`
  (`27`). Côté serveur : lecture stricte (`Length == 13`), journalisation, aucune réponse.
- Aucune demande cliente n'ouvre ce conteneur : la fenêtre est locale au client (commandes
  `/cshop` / `/cstorage`, verrous de ressource `commercial_shop` et `cash`). Le serveur **pousse** la
  10003 à l'entrée en jeu, comme rzu (`Character.cpp:308-311`, à `0/0`).
- **Aucune référence n'implémente la logique du conteneur** : NGemity ne traite rien, rzu n'émet
  qu'une 10003 constante. Sans boutique, le conteneur est **vide par construction** ; ne rien
  inventer sur le retrait (coût, plafond, code de résultat, acquittement) — décisions ouvertes dans
  la fiche.
- Aucun service, aucune entité et aucune migration pour ce conteneur : rien dans le dépôt ne peut
  l'approvisionner, donc sa seule valeur exacte est vide. Les trois bras de dispatch sont posés près
  de `TM_SC_REGION_ACK` (`GameClient.cs:813-840`), jamais à l'ancre du `switch` final.
- Le savoir durable de ce socle est dans `docs/packet-specs/socle-stockage-commercial.md`, pas ici.

### Socle compétition entre joueurs — 4500-4506 (`TM_CS/SC_COMPETE_*`)

**Sept opcodes, tous `X(<id>, true)` chez rzu : aucun gating de version, aucun champ gaté.**
`true` n'est pas une convention « valide partout » mais la **condition C++ littérale** substituée
dans `if(condition_) id = id_;` (`PacketDeclaration.h:585-588`) : les sept ids sont donc identiques
en 7.3 et dans toutes les versions. Fiche complète : `docs/packet-specs/socle-competition-joueurs.md`.

**Tailles à écrire en dur** (source : rzu + constructeurs et lecteurs du client 7.3) :
**4500 = 39**, 4501 = 39, **4502 = 9**, 4503 = 40, **4504 = 43**, 4505 = 39, **4506 = 71** octets.
Les chaînes de `requestee` / `requester` / `competitor` / `winner` / `loser` sont des tampons
**fixes de 31 octets** (NUL compris) ; le handle de 4504 est à l'offset **39** et le second nom de
4506 à l'offset **40** — ce sont les deux preuves indépendantes de la largeur 31.

**Direction : le client route 4501, 4503, 4504, 4505 et 4506, et ne route NI 4500 NI 4502** (ils
tombent dans le journal « message non traité » du dispatcher entrant). Le serveur ne doit jamais
émettre ces deux ids ; il les **reçoit**.

**Ce que le joueur fait** : `4500` part du contrôle de fenêtre `request_compete` (nom de la cible,
`compete_type = 0`), `4502` des contrôles `battle_start` (`answer_type = 0`) et `battle_reject`
(`answer_type = 1`), plus une branche par défaut (`answer_type = 2`).

**Refus** : par `TS_SC_RESULT` (id 0) avec `RequestMsgID = 4500` ou `4502`. Navislamia a déjà la
trame (`TS_SC_RESULT.cs`, `ushort + ushort + int` = 15 octets) et l'émetteur
(`GameClient.SendResult`). Les codes de la famille sont déjà déclarés sans lecteur :
`ResultCode.cs:74-81` (61-68). Attention : `66` n'a **aucune** boîte pour 4500, et `63`/`66`/`67`
aucune pour 4502 — le refus serait silencieux ; la correspondance existe en `0x4774dd`-`0x4776f7`.

**Ne pas porter NGemity** : les sept ids et structures y sont déclarés et **jamais traités**
(`Chihiro` n'a que `CRT_COMPETE`, `AF_ERASE_ON_COMPETE_START`, `AF_NOT_ACTABLE_IN_COMPETE`,
`REVIVE_COMPETE`). `librzu` non plus. C'est du protocole pur, comme le socle instances de jeu.

**Socle minimum (C1), livré** : `4500` et `4502` sont lus, validés et refusés — 39 octets exigés
pour le premier avec un nom NUL-terminé dans ses 31 octets, 9 pour le second ; une trame mal
formée est journalisée sans réponse. Le refus part par `TS_SC_RESULT(4500, 64)`
(`NotInCompetablePlace`) et `TS_SC_RESULT(4502, 62)` (`NotInCompete`), deux codes que le client
**affiche** (boîte 1633) : le socle ne joue aucun duel, donc il énonce son état réel et n'invente
aucune règle. Les deux ids sont déclarés dans `GamePackets` **et** routés dans `GameClient.cs`
(critère transversal n° 4) ; les cinq ids serveur → client (4501, 4503-4506) ne sont pas déclarés
tant qu'ils ne sont pas émis. La réussite (4501/4503) exige un registre de joueurs visibles,
absent (`ConnectionInfo.cs:47-60`, `SkillCastService.cs:284-287`, `CombatService.cs:42-50`) — d'où
la frontière avec le socle PK 800/801, qui partage ce prérequis sans partager d'opcode. Découpage
C1…C4 : §5.5 de la fiche ; décisions d'implémentation : §9.1.

**Non tranché** : `compete_type` (seule valeur observée 0, jamais validé), `answer_type` (0/1/2,
hors domaine journalisé puis refusé par le code 62), `end_type`, durée du compte à rebours,
`handle_competitor`, politique de duel. Aucune de ces valeurs n'est devinée. Le choix des deux
codes de refus et l'opportunité de répondre à un `4500` (§7m) restent l'arbitrage de Killian, et
les deux sont des constantes d'une ligne.


### Socle classements de joueurs — 5000/5001 (`TM_CS/SC_RANKING_TOP_RECORD`)

**Deux opcodes, tous deux `X(<id>, true)` chez rzu : aucun gating de version, aucun champ gaté.**
`true` n'est pas une convention « valide partout » mais la **condition C++ littérale** substituée
dans `if(condition_) id = id_;` (`PacketDeclaration.h:587-589`) : `5000` et `5001` sont donc
identiques en 7.3 et dans toutes les versions. La plage 5002-5999 est vide dans rzu comme chez
NGemity. Fiche complète : `docs/packet-specs/socle-classements.md`.

**Tailles à écrire en dur** (source : rzu + constructeur et analyseur du client 7.3) :
`5000` = **8** octets fixes (`int8 ranking_type` à l'offset **7**) ; `5001` = **20 + 41 × n**
octets, soit **20** à vide — en-tête, `int8 ranking_type` (7), `uint16 requester_rank` (8),
`int64 requester_score` (10), `uint16 records` (18), puis `records` entrées de **41** octets
commençant à l'offset **20** : `uint16 rank` (+0), `char[31] ranker_name` (+2),
`int64 score` (+33). **Aucun remplissage** entre le compteur et la première entrée.

**Le client 7.3 émet bien `5000`** : constructeur de trame `0x48d160` (longueur `8`), appelé depuis
l'unique site `0x49d2de`, avec `ranking_type = 0` écrit en clair (`0x49d2e9`). Il part de la
commande UI enregistrée sous le nom `Ranking_Top_Record` (fenêtre `window_donation_ranking.nui`).
Aucun autre `ranking_type` n'est atteignable. Le client **route `5001`** (dispatcher entrant
`0x67e7bc`, `cmp $0x1389`, analyseur `0x671660`) et ne route pas `5000`.

**Trois obligations que le client impose au serveur, et qu'aucune référence n'écrit** : (1)
`records` égale le nombre réel d'entrées — le client boucle `records` fois et ne lit jamais
l'en-tête de longueur ; (2) **`records ≤ 10`** — le message interne du client fait 442 octets,
soit 32 d'en-tête + 41 × 10, et rien ne borne le compteur côté client ; (3) chaque
`ranker_name` contient un **NUL dans ses 31 octets** — la copie du client est un `strcpy`
(`0x671700`). Enfin, **les deux `score` sont divisés par 10 000 par le client** avant tout usage :
la valeur du fil est la valeur affichée **× 10 000**.

**Ne pas porter NGemity** : les deux structures y sont déclarées et **jamais traitées** (`Chihiro`
n'a aucun `Ranking`). `librzu` non plus. C'est du protocole pur, comme les socles compétition et
instances de jeu.

**Socle minimum (K1)** : `5000` déclaré dans `GamePackets` **et** routé dans `GameClient.cs`
(critère transversal n° 4), `Length == 8` exigée, puis réponse `5001` à `records = 0` (20 octets,
`ranking_type` recopié, `requester_rank`/`requester_score` à zéro) construite avec
`CreatePacket` + `WriteChecksum` (`GameCharacterPackets.cs:19`, `:382-388`). `5001` est déclaré
parce qu'il est émis. Aucune donnée de classement, aucune métrique, aucune cadence : la source
des données est le lot K2, et elle appartient à Killian. Découpage K1…K3 : §5.5 de la fiche.

**Non tranché** : domaine de `ranking_type` (le client n'émet que `0` ; la fenêtre s'appelle
`donation_ranking`, seul indice), métrique et échelle du `score`, nombre d'entrées effectif
(≤ 10 est une borne de protocole, pas un choix), valeur du rang et du score d'un joueur non
classé, source des données, cadence, refus d'une trame mal formée, effet perçu d'une liste vide.
Aucune de ces valeurs n'est devinée.

## Source data (9.4 SQL Server export)

The 9.4 resource database lives in a local SQL Server (`localhost\SQLEXPRESS`, database `Arcadia`) that
is stopped by default and needs an administrator to start. **Nothing needs it running any more**:
`tools/Export-SqlServerData.ps1` wrote every table (115 tables, 1 460 707 rows, ~180 MB) to
`data/sqlserver/Arcadia/<Table>.csv` with `_manifest.json` (columns, SQL types, row counts). The folder
is **git-ignored**: it is local data, regenerated by re-running the script while SQL Server is up.

The CSV is what PostgreSQL's `\copy … WITH (FORMAT csv, HEADER)` reads as is: header row, NULL as an
empty unquoted field and every string quoted (an empty string stays distinct), invariant numbers, UTF-8
without a BOM (the Korean and Chinese string tables survive). An import script loads the whole file into
a temporary table of `text` columns and copies the mapped ones — `Import-SkillResourceColumns.ps1` is the
model. `Import-MonsterResourceColumns.ps1` and `Import-MonsterSpawns.ps1` still query SQL Server
directly. The other databases of that instance (`CHARACTER_01_DBF`, `ACCOUNT_DBF`, `RANKING_DBF`,
`LOGGING_01_DBF`) belong to another game, not to Rappelz: they are not exported and nothing here reads
them.

## Logging

Serilog is configured in `DevConsole/appsettings.json`, which is **local and not tracked** (it carries the
database credentials). Its console and file sinks must sit inside an `Async` sink
(`Serilog.Sinks.Async`): the Windows console is slow and writes synchronously, and at `Debug` every sent
and received packet is a line, so a synchronous console stalled the network and tick threads that logged.
Keep `System` at `Warning`. A log call with more than three properties allocates an `object[]` and boxes
its arguments **before** Serilog checks the level, so a per-packet one is wrapped in
`_logger.IsEnabled(LogEventLevel.Debug)` (`GameClient.SendMessage` and the receive loop do).

## Change guidelines

- Preserve the 7-byte header, little-endian layout and exact client packet sizes.
- Keep world-object enter decisions inside the 540-unit view.
- Use `MonsterResource.id` for monster enter packets.
- Keep resource queries no-tracking and project only fields required at runtime.
- Add tests for packet offsets, encodings, spatial boundaries and spawn expansion.
- Do not edit generated EF migration designer files manually unless the migration itself changes.
- A newly handled client packet gets a sheet in `docs/packet-specs/` (see Solution layout), and
  its id must be added to the `GamePackets` enum and to the `GameClient.Receive` dispatch chain
  **in the same change**: a declared id with no dispatch arm reaches
  `_ => throw new Exception("Unknown Packet Type")` and kills the receive loop.
- That rule covers client packets. An id the server only ever emits needs no arm in
  `GameClient.Receive`, so `GameSummonPackets`' seven strictly server-to-client ids are
  declared in `GamePackets` with no dispatch entry; 33 `TM_SC_*` members already had none.
