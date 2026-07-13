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

## World entry and movement

`GameActions.OnLogin` sends the login result and player enter packet, followed by the Epic 7.3
character bootstrap: stats, inventory, summon slots, wear information, gold/chaos, level/job level,
experience/JP, job properties, learned skills, belt slots, game time and status. It then synchronizes
NPC and monster visibility. See `docs/character-bootstrap.md` for packet layouts and model ordering.

Movement uses the client's current `x/y` fields for visibility; the final waypoint is a future
destination and must never be used as the current position.

This client build uses the calibrated model order `face, hair, armor, gloves, boots`. Its extended
`TS_SC_LOGIN_RESULT` appearance block after `race` is `faceId, skinColor, hairId, faceTextureId`; the
name starts at absolute packet offset 82. This exact order comes from the client deserializer: absolute
offset 70 feeds the primary body colorizer. World login also sends dedicated hidden-equipment and skin
information packets.
Hair changes use `TS_SC_HAIR_INFO`, but initial login relies on the login result plus the
client-calibrated hair wear slot 13 to avoid an equipment refresh dropping the hairstyle. Face wear
slot 12 must remain empty; filling it replaces the textured login face with a transparent mesh.
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

## Monster packets and catalog

Monsters use the 73-byte monster variant of `TS_SC_ENTER`: `objType = 3`, the shared creature payload,
an 8-byte scrambled `monster_id` and `is_tamed = 0`. `monster_id` must be the actual
`MonsterResource.id`, never a name or location code. Packet `TS_CS_MONSTER_RECOGNIZE` (`517`) is valid
and needs no response while monsters are idle.

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
and the client thread both touch it. Damage is a placeholder (`30 + level * 5`) and attack timing is
fixed until the `MonsterResource` combat columns are backfilled.

## Current limitations

- Monsters can be auto-attacked, killed and respawn, but have no AI, movement, aggro, retaliation,
  loot or taming; damage and attack speed are placeholders
- NPC behavior beyond rendering and existing scripts is incomplete
- Advanced cosmetics, equipment changes and learned-skill persistence are not implemented
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
