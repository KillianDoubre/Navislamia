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
301 and 305 are emitted by `CreatureService` (formation, login, summon, unsummon; see *Apprivoisement et
invocation des créatures*); 307, 320 and 321 by evolution and riding (`socle-invocations-progression.md`); 302 and
306 by the card and double-summon rules (`socle-duree-invocations.md`). `BuildAddSummonInfo` takes `code`
and `summon_handle` from its caller instead of inventing either.

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
`WorldObjectHandle.Next()` (or takes the caller's), emits 301 (it fills the creature window) then 3 (it puts the
object in the world) — `CreatureService` passes `sendInfo: false`, since its 301 already went with the formation
or the login — and `Leave` emits `TS_SC_UNSUMMON` (305)
then `TS_SC_LEAVE` (9) on the master's connection; given the master's `GameClient`, the players who see the
master get the summon's `TS_SC_ENTER`/`TS_SC_LEAVE` too, and `ConnectionInfo.Summons` keeps it so a player who
comes into view later is shown it (`docs/packet-specs/socle-diffusion-compagnons.md`). A summon's position is
never persisted: it is the master's (`ConnectionInfo.X/Y`, `Layer`, `master_handle = CharacterHandle`) plus a
bounded jitter (`AddNoise` in integer arithmetic: `raw % range - range/2`, 70 on summon, 50 on login, 35 on
warp, 0 = exact position); the `z` stays the caller's — NGemity's own summon `z`, never set, is 0 — and the
region-cancel step of `AddNoise` is not portable either, since nothing resolves a position to a location id
here. `code` and `summon_code` carry the same value (`SummonResource.id`, `Summon.cpp:35,88-91`), supplied by
the caller. **306 is the double summon, 302 a bound card leaving the bag** (`docs/packet-specs/socle-duree-invocations.md`):
a second summon called while the main one is out stays `1000 + 1881 level × 700 + 112-state level × value_4 × 100` ticks
(306 on its handle, 0 under state 3121), then goes back; a card gone from the bag sends 302, one come in 301, both from a
re-read after `IInventoryChangeFeed`, and the `Summons` row follows its card. **What a creature or the pet holds never
leaves the bag** (official `IsErasable`, `Creatures/HeldItemRules`: formed card, summon out, belt slot, cage of the pet
out) — trade, sale, booth, auction, storage, drop, destruction and crafting all judge it; a tamed card that is neither
formed nor out trades freely, and a departed creature's equipment is unworn at the old master's next login (§6 of
that sheet). `CharacterService` writes `MainSummonId` and
`SummonSlotItemIds`, and those six columns hold *summon* sids like the official character row, not card ids:
the session and the 303 speak card handles, `CharacterService` translates both ways.

Epic 7.3 key bindings are character data, not a local `.opt` setting. The server sends the single
string property `client_info` with `TS_SC_PROPERTY (507)` during world entry, and the client writes it
back with `TS_CS_SET_PROPERTY (508)`, normally when leaving the game. The value is an opaque,
pipe-delimited list of `QS2`, `KMT` and chat-mode entries stored as text in
`Characters.ClientInfo`. `CharacterDefaults` supplies the complete default map for new and legacy
characters. Do not split this value into the `quick_slot`, `current_key` or `saved_key` properties
used by later clients such as Epic 9.4; this Epic 7.3 executable only registers `client_info`.

**A walk is judged against where the server has the player** (`docs/packet-specs/socle-anti-triche-deplacement.md`,
official `onMoveRequest`/`GetValidWayPoint`): `ConnectionInfo.BeginWalk` keeps the accepted path and `PositionAt`
follows it leg by leg at the echoed speed; a dead player's request is dropped; a claimed position outside the map or
more than 525 units from the estimate, or a way through a `.nfa` obstacle, answers `ACCESS_DENIED` and walks the
player back (`Movement/PlayerMoveRules`, `PlayerMoves`); a region update or a 900 keeps the client's position only
within 120 units of the estimate. A death stops the walk for the player and its observers. Any server-side change of
a player's position should go through `BeginWalk`/`Rebase`; changing `MoveStartTick` or the destination at least drops the kept path.

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

## Visibilité entre joueurs (`TM_SC_ENTER` 3 / `TM_SC_MOVE` 8 / `TM_SC_LEAVE` 9)

Les joueurs se voient. **Aucune trame nouvelle** : la variante joueur de `TS_SC_ENTER` fait **118 octets**
(`type` @7 = 0, `handle` @8 = `character.Id`, `x/y/z` @12/16/20, **`layer` @24 avant `objType` @25** = 0,
`PLAYER_INFO` de 92 octets @26, `name` sur **19** octets) et `TS_SC_ENTER_PLAYER` la porte déjà champ pour
champ ; `TS_SC_LEAVE` fait 11 octets et `TS_SC_MOVE` `19 + 8 × N`. Ce qui manquait était serveur :
`PlayerVisibilityIndex` (grille **mutable** avec la couche dans la clé de cellule, `Add`/`Move`/`Remove`),
`PlayerRegistry` (handle → session en O(1)) et `PlayerVisibilityService`, joignable par
`NetworkService.PlayerVisibilityService` et injecté dans `WarpService`.

- **Réciproque** (A voit B ⟺ B voit A, `SendEnterMessageEachOtherFunctor` de NGemity), sur la fenêtre des
  autres types (`WorldVisibility.ViewRange`, 540), et **évènementielle** : entrée en jeu (fin de
  `GameActions.OnLogin`), `TM_CS_MOVE_REQUEST`, `TM_CS_REGION_UPDATE`, warp (`LeaveEverything` puis
  `EnterWorld`), sortie (lobby **et** déconnexion, **avant** la sauvegarde asynchrone). Aucun minuteur.
- Le handle d'un joueur est **le même pour tous les observateurs** (`character.Id`), contrairement aux PNJ,
  monstres et props (un handle par client, à partir de `0x40000000`). `SpawnedPlayers` le garde sous
  `PlayerVisibilityLock`. La marche diffusée porte `start_time = ServerClock.Now + ClientClockOffset` **du
  destinataire**, les points reçus tels quels, la vitesse `EchoedMoveSpeed`.
- **Un `TM_CS_MOVE_REQUEST` dont le `handle` n'est pas celui du personnage est abandonné** (sans écho) :
  sinon un client ferait marcher l'acteur d'un autre chez tous les observateurs. Une session sans personnage
  (handle 0) garde son écho.
- **Verrous** : jamais deux verrous de visibilité imbriqués ; chaque trame est enfilée **sous le verrou de
  son destinataire**. `Connection.Send` ne fait qu'enfiler dans un canal non borné, donc tenir le verrou ne
  coûte rien, et c'est ce qui garde l'ordre : la première version envoyait après avoir relâché les verrous,
  et un `ENTER` bâti par un thread pouvait partir après le `LEAVE` bâti par un autre — un fantôme jusqu'à la
  reconnexion (corrigé à la revue du 2026-09-30).
- Les traits fixes d'un pair viennent de `ConnectionInfo.Appearance` (`PlayerAppearance`, posé à l'entrée en
  jeu : la session ne garde aucune `CharacterEntity`) ; niveau, job, PV/PM, statut sont relus en direct.
  `max_mp` = PM courants et `is_first_enter = 1`, comme l'entrée locale.
- **Diffusé aux observateurs** : l'équipement (`TS_SC_WEAR_INFO` 202, gardé en
  `ConnectionInfo.WearFrame` à l'entrée et refait à chaque équipement, envoyé juste après l'`ENTER` d'un
  pair), les PV/PM en **`TS_SC_HPMP` (509, 36 octets)** — ce que la référence diffuse
  (`Messages::BroadcastHPMPMessage`) : le joueur garde ses `TS_SC_PROPERTY`, ses observateurs reçoivent la
  509 (`GameClient.SendVitalProperty`) —, la régénération (516), le niveau (1002, `BroadcastLevelMsg`),
  l'émotion (1201), le chat local et les objets au sol ; assis et mode PK (500, `SendActorStatus`, à l'entrée
  comme au changement).
- **Combat diffusé** (`docs/packet-specs/socle-diffusion-combat.md`) : coups, compétences, poursuites et mort
  des monstres partent aussi aux joueurs qui voient la scène, **reconstruits pour chacun** avec son handle du
  monstre et son horloge (`ObserverFrames`, `MonsterAiService.ToOtherWatchers`). La mort d'un joueur se voit
  par le coup fatal (`target_hp = 0`) et le 509 : le client n'a pas de paquet de mort.
- **Compagnons diffusés** (`docs/packet-specs/socle-diffusion-compagnons.md`) : l'`ENTER` du familier et des
  invocations suit celui du maître, leur `LEAVE` précède le sien, et appel, rangement et marche du familier
  partent aux observateurs (`CompanionFrames`, handle global). La visibilité lit `ActivePet` et `Summons`
  **sans verrou** — l'inverse de l'ordre de verrouillage du familier serait un interblocage.
- **Icônes d'états diffusées** : états (505) et auras (407) d'un joueur à ses observateurs, et à l'entrée d'un pair
  dans une vue (`CompanionFrames.States`) ; malus et bonus d'un monstre aux observateurs qui le voient, **et à
  l'entrée du monstre dans une vue** (`docs/packet-specs/socle-etats-monstre-entree.md`) : `MonsterSpawnService.Sync`
  fait suivre l'`ENTER` d'un 505 par état actif, avec le handle de ce client, comme `SendEnterMsg` de l'officiel
  (NGemity n'en fait rien). Les trames partent sous `MonsterVisibilityLock` et **après** l'enregistrement du
  handle (`WorldObjectStreamer`, `onEntered`) : une pose concurrente retrouve l'observateur par ce handle.
  Ordre des verrous `MonsterVisibilityLock` → `_stateLock`, jamais l'inverse. Un état échu que le tick de 500 ms
  n'a pas encore retiré n'est pas annoncé (décision du dépôt).
- **Hors lot** : un pair déjà en marche (vu immobile jusqu'à sa prochaine trame).
  `BoothWatchService` cherche encore le propriétaire d'un étal par balayage : le registre peut le remplacer.
- Fiche, sources et réserves : `docs/packet-specs/socle-visibilite-joueurs.md`.

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

**Which NPCs stand in the world is the official `onNPCData` rule** (`docs/packet-specs/socle-pnj-pays-periodes.md`,
`NpcSpawnRules`): an NPC's `local_flag` is an **exclusion** mask against `GameRules:LocalFlag` (1, Korea), bit 30 keeps
it off a live server (`GameRules:ServiceServer`) and bit 29 off a test one, and a periodic NPC only stands inside its
period (all of the data's periods ended by 2012). The 7.3 client's own `local_flag` (`npc-client-flags.json`, embedded,
`tools/export_npc_client_flags.py`) is OR-ed in: it retires the events the server table still enables, the 9.4-only event
rows above all. 406 of 1 182 NPCs stand. Before, every row spawned, events and `???????` names included. The Hidden Village
teleporters, the towns' teleporters to it, the Flea Market and every auctioneer run their official Lua in the NPC
sandbox (`show_auction_window` = `TS_SC_DIALOG` type 4, `is_premium` = state 9004 active).

## Monster packets and catalog

Monsters use the 73-byte monster variant of `TS_SC_ENTER`: `objType = 3`, the shared creature payload,
an 8-byte scrambled `monster_id` and `is_tamed = 0`. `monster_id` must be the actual
`MonsterResource.id`, never a name or location code. Packet `TS_CS_MONSTER_RECOGNIZE` (`517`) is valid
and needs no response while monsters are idle. Each monster carries a random `creatureInfo.face_direction`
(the `float` at offset 30, verified correct against `TS_CREATURE_STATUS` being a 4-byte `uint32`), set
once per instance from the factory's seeded `Random`. This client build appears to ignore the enter-packet
facing for idle monsters (setting `is_first_enter` made no difference), so they render facing the default
direction until they orient through movement; the field is kept for correctness and future clients.

`DevConsole/monster-spawns.73.json` currently contains 4,082 compatible areas, 40,566 instances and
2,638 distinct resource IDs. It is deserialized directly with `System.Text.Json`; do not add it to the
generic configuration provider because flattening the large arrays adds tens of seconds to startup.
The catalog is generated from the client's own NFS boxes and the Epic 7 `monster_respawn.lua`, filtered
against IDs decoded from the Epic 7.3 client `db_monster.rdb`. **A box is `raw × 42 + map index × 16128`**
(official `MapLoader::LoadRegionInfo`, `TILE_LENGTH`); the importer used `raw × 48`, which stretched every
box by 8/7 and put monsters on the trainee island's start points (`docs/world-spawning.md`). Rendering and streaming have been validated in game.

## Targeting and action cancel

Pressing Escape sends `TS_CS_TARGETING` (`511`) with `target = 0` and `TS_CS_CANCEL_ACTION` (`150`),
each an 11-byte packet whose only payload is a 4-byte `ar_handle_t` (IDs valid for Epic < 9.6.3).
`TS_CS_TARGETING` sets `ConnectionInfo.TargetHandle` (the handle an attack or skill acts on;
`0` deselects and stops the current attack). `TS_CS_CANCEL_ACTION` stops the current attack. Neither
sends a response. `GameActionPackets` holds the pure offset parsers.

## Combat

Double-clicking a monster sends `TS_CS_ATTACK_REQUEST` (`100`, Epic < 9.6.3): `handle` @7 +
`target_handle` @11. The server drives auto-attack: `CombatService` runs a 100 ms `PeriodicTimer`
loop that swings at the player's attack interval, sending `TS_SC_ATTACK_EVENT` (`101`). **The swing is gated on
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
and the client thread both touch it.

**Damage, hit, block, critical and cadence follow the official rules** (`docs/packet-specs/socle-combat-reel.md`).
`CombatFormulas.Resolve` is the one pure function behind the player's swing, the monster's swing and the
offensive skills: hit roll (`7 + max(10, 88 + 2 × level gap) × accuracy / avoid + bonus`, only against a
target with avoid), block then perfect block (physical, a target with a block chance), critical
(`× (1 + criticalPower / 100)`), the defence formula `level × 1.7 × max(1 − 0.4 def/atk, 0.3) +
atk × max(1 − 0.5 def/atk, 0.05)` and a ±5 % spread — the order `DamageCalculator::SimulateDamageCalculation`
draws them in. The interval is `100 / attackSpeed × 115` ticks (1.15 s at 100), carried in `attack_speed`/
`attack_delay` as milliseconds; `ATTACK_INFO.flag` (@8 of the record) carries miss/block/critical. Players
use `StatService.Compute(info)`; **a monster's stats are `MonsterCombatStats`**: its `stat_id`'s
`StatResource` row, the level seed and derived bonuses of `StatCalculator` (the official
`StructCreature::calcAttribute` has the same coefficients) and then its `MonsterResource` columns, built
once per resource at load. **Its max HP is `hp + 20 × level + 33 × vitality`, not the `hp` column**, which
is what `MonsterInstance.Hp` now holds. A monster's active states are folded into its stats on each hit.
`ICombatRandom` is the dice, scripted in the tests.

On death `CombatRewards.Roll` uses the instance's `MonsterRewardProfile`, frozen from its loaded
`MonsterResource` (including replacement resources and scripted reinforcements). `Exp`/`Jp` rates
scale the imported amounts. `Gold`/`ChaosDrop` rates scale the percent chances, with inclusive resource
bounds for amounts. Gold is an item of code 0 on the ground, capped at 1,000,000 per pile; it credits
the wallet only on pickup, sharing among party members within 400 units of the picker regardless of
item loot mode. EXP/JP and chaos share among eligible party members at death, **within 500 units of the corpse**
(an out-of-reach member keeps the denominator and takes nothing); EXP/JP lose `0.05 × (beneficiary level −
monster level)` per beneficiary (`MonsterRewardRules.ScaleForLevelGap`), and the gold, chaos **and item**
chances share one loot factor `max(1 − 0.2 × (gap − 10), 0)` over the party's highest level. Chaos respects
effective `MaxChaos` and sends packed `TS_SC_GET_CHAOS` (213), then the `chaos` property (507); **the kill
sends no `TS_SC_GOLD_UPDATE`** (1001). Every ground drop of a monster, gold pile included, is announced by
`TM_SC_ITEM_DROP_INFO` (282) right before its `ENTER`.
`MonsterSpawns.UseSecondaryRewards` selects Exp2/Jp2 and alternate bounds (default false).
Progress is saved by `CharacterService.SaveProgressAsync` on disconnect and the existing save path;
there are no per-kill database writes. The PC bang bonus is the official one (ally +10 %, premium +120 % EXP/JP,
+10 % chaos, `GameRules`; `socle-progression-monstres-quetes-titres.md`). See `docs/packet-specs/socle-recompenses-monstres.md` (§15 for the merge of the two
implementations, MR #78 and Codex's).

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
**The tier is the job depth** (the number of jobs left behind, `ConnectionInfo.PreviousJobs.Count`): the four
`jp_0..jp_3` columns come from `DevConsole/job-level-costs.73.json` (`tools/export_job_level_costs.py`, 64-bit —
`jp_3` overflows the database's `integer[]`), capped at JLv 10, 50, 50 and 60. The job level persists through
`SaveProgressAsync`.

**Job change** (`docs/packet-specs/socle-changement-metier.md`): the official Epic 7 Lua (`NPC_JobChange.lua`,
`NPC_Tutorial.lua`) ported as `Game/Services/Jobs/` — dynamic dialog pages built by `JobChangeService` and shown by
`NpcDialogService`, menus hard-coded like the Lua (Lv/JLv 10/10, 50/40, then the master class at NPC 11555 with
147/49 and quest 3322), everything judged again at the commit, `ICharacterService.ChangeJobAsync`, then the
`job_N`/`jlv_N`/`job`/`job_level`/`job_depth` properties, the stats and a `@SCRIPT` chat line. **Talent skills, the master
class skill reset and the race change** (§8 of the sheet): a negative `need_jp` is a talent point cost
(`NotEnoughTP` 83, `ConnectionInfo.CharacterTalentPoint`); `SkillResetRules` ports `ResetSkill`/`ResetJob`/`SetRace`, and
a reset sends `TS_SC_SKILL_LIST` with `modification_type` 1 (`REFRESH`), the only way to take skills away on the client.

Skill learning is server-authoritative. Epic 7.3 sends `TM_CS_LEARN_SKILL` (`402`, 17 bytes) with the
character handle, skill id and requested level. `SkillCatalog` validates that the request advances by
exactly one level, belongs to the current job tree, satisfies character/JLv/skill prerequisites and
does not exceed the configured maximum. **The trees of the jobs left behind come first**, each with the job level
reached there, then the current tree (`SkillCatalog.EvaluateAcrossJobs`, `StructPlayer::IsLearnableSkill`). It then derives the JP cost from
`DevConsole/skill-catalog.73.json`; this immutable runtime index contains 1,320 job/skill definitions
for the 42 classic jobs and is generated from the Epic 7 `SkillTreeResource` (keyed by `job_id` at that
epic, no `JobResource` join) and `SkillJPResource` by `tools/export_skill_catalog.py`
(`tools/Export-SkillCatalog.ps1` is the former 9.4 SQL Server path, 1,339 definitions). The 19 lost are
9.4 additions — mostly Creature Taming (4003) offered to every job, which Epic 7 keeps to the summoner
lines.

JP and the learned level are committed together in Telecaster (`CharacterSkills`, unique on
character/skill) before runtime state changes. On success the client receives `TS_SC_EXP_UPDATE`
(`1003`) with the remaining JP, a one-record `TS_SC_SKILL_LIST` (`403`) and the result for request
`402`. Login sends the complete learned list through `403`, followed by the existing empty added-skill
marker `404`. The client only requests skills exposed by its own 7.3 resources. See `docs/skill-learning.md`.

The death sequence lets the client play its death animation: the killing swing is followed by
`TS_SC_STATUS_CHANGE` (`500`) with the dead flag (`1 << 8`), and the `TS_SC_LEAVE` that removes the
corpse is deferred by `DeathAnimationSeconds` (6 s) through a pending-leave list on the combat tick
rather than sent immediately. Item drops are a later milestone that will hook the same death branch.

## Item drops

On death `CombatService` calls `GroundItemService.DropForMonster`, which rolls the monster's table and
puts each result on the ground near the corpse. Resource gold uses `DropGoldForMonster`, independently
of item drop slots, and is credited through the ground pickup path.

`DevConsole/monster-drops.73.json` is the runtime catalog, from the **Epic 7 tables** (5,289 tables,
179 direct entries, 56,708 group-reference entries, 5,726 drop groups, 5,672 monsters), filtered against
the client's `db_item.rdb`: a slot whose item the client does not know is removed (1), and such a group
member (301) is written `ItemId 0`, **a blank share `MonsterDropCatalog` keeps** so that its weight drops
nothing instead of inflating the other members. The 9.4 export it replaced had 5,551 of its 5,767 direct
entries unknown to the client. It is loaded like the spawn catalog — read
with `System.Text.Json` in `Program.ConfigureMonsterDrops` and frozen by `MonsterDropCatalog` into a
`FrozenDictionary` keyed by monster id, so a kill never queries the database. Regenerate it with
`tools/export_monster_drops.py --client-items db_item.rdb` (`--source data/sqlserver/Arcadia` for the 9.4
export). Traps, all silent if you get them wrong:

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
Each `GroundItem` holds its owning `GameClient` the same way `CombatService.PendingLeave` does, and takes
the player list from `IPlayerVisibilityService` instead. **A ground item is visible to every player within
the 540-unit view** (same handle for all; `ConnectionInfo.SpawnedGroundItems` under
`GroundItemVisibilityLock`, re-synced on move, region update, warp and world entry). **Who may take it is the
official `onTakeItem`** (`docs/packet-specs/socle-partage-objets-sol.md`): a monster's drop carries a
`pick_up_order` (slot 0 = the killer and their party), whose entitled players take at once and anybody else
from **30 s** (`GetPickupOrderTime(i)` = 3000 + 1000 × i ticks), refused `ACCESS_DENIED` (6) before; a quest item
stays its owner's; what a player drops has an **empty order**, anybody takes it at once. `drop_time` is the
instant of the fall, never of the send. The client's `SGameItem::IsPickable` (state 0-3 at 30/40/50 s) only
drives the **pet**: its master's loot past 30 s, anybody's past 50 s.

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

**Speed, obstacles and paths** (`docs/packet-specs/socle-deplacement-monstres.md`). A monster moves at its
`run_speed` (`MonsterCombatStats`, states included, floor 10) and the `TS_SC_MOVE` speed byte is
`move speed / 7` (`MonsterMovement.SpeedByte`): wander at that, chase × 1.00-1.09, return × 2. The world's
blocking polygons are the client's `.nfa` files, extracted to `DevConsole/Maps` by
`tools/Export-FieldProps --extract-maps` (git-ignored) and loaded by `WorldCollision` into `CollisionMap`
(own geometry and grid, not the legacy X2D code). Spawn points are redrawn out of obstacles (the spawn
areas brush them: 8.8 % of their points), a wander into or across an obstacle is refused, and a chase or a return goes around
through `PathFinder` (A* over a visibility graph; a monster inside an obstacle may walk out). A path travels
as a multi-waypoint `TS_SC_MOVE` and is interpolated leg by leg. `GameModule.LoadMaps` read `SkipLoading`
backwards and the map parsers were culture-sensitive: the maps had never loaded. **`MapService` reads the location,
script and event area files only, one map after the other (under 100 ms)**: it used to insert the `.nfa` obstacles a
second time into two quadtrees nothing read, about 30 of the server's 40 startup seconds, while `WorldCollision` loads
them in under 100 ms; its parallel loading also raced on the shared region and script lists.

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
  attacker and the player as target, rolled by `ICombatService.RollMonsterHit` against the player's
  stats (the real rule, see *Combat*), at the monster's own attack interval, landing through
  `ICombatService.DamagePlayer` — the one place a monster's damage reaches a player, which on the killing hit
  applies the official death penalty (`LevelingService.ApplyDeathPenalty`, `socle-perte-experience.md`:
  `need(level) × (0.15 / (level − 1) + 0.0005)`, a level can be lost). **Before swinging, the monster rolls its
  skills** (see *Monster skills*); the first one that comes up replaces the swing. HP can reach 0: that is the player's death (see *Mort et réapparition du personnage
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
`SpawnedMonsters` handle. The aggro target is the top of a **hate list** (`AddHate`, official
`addHate`/`GetHatePoint`): a swing is worth its damage, a skill `hate_mod`/`hate_basic`/`hate_per_skl`, and
`DropTarget` falls back to the next hater before the monster goes home (`socle-haine.md`).

**The ranges are scaled, and the scale is not uniform** — the same trap as `cast_range`.
`MonsterAiRules` ports the reference: **chase range is `12 × chase_range`** (`Monster::GetChaseRange`,
so 100 → 1200 world units), visible range reuses `12 ×` (the reference's aggro path is an empty stub),
clamped to the client view. **Attack range is the reference's real value**, in `CombatRange.MeleeReach`:
`(12 × attack_range) / 100` (`Unit::GetRealAttackRange`) plus both body radii, where a unit's size is
`size × 12 × scale` (`Object::GetUnitSize`) and the player uses the default `1 × 12 × 1 = 12`. The
body-size term dominates the tiny weapon term, so a small monster reaches ~12 units and a big one
(`size` up to 12.45, `scale` up to 7) hundreds — **big monsters really do hit from farther**. The same
per-monster reach gates both the monster's attack and the player's swing, keeping them symmetric.
**Group aggro is the official one** (`docs/packet-specs/socle-aggro-groupe.md`): a monster with
`f_group_first_attack` that takes a player on sight gives it to the monsters of its `monster_group` the player
sees, within its sight range (`MonsterWorldState.RallyGroup`); a retaliation rallies nobody.

`CharacterMaxHp` was added to `ConnectionInfo` next to `CharacterHp`, seeded at the same two points HP
is set to max (login and level-up); the former test damage read it, the real rule reads the stats. The AI columns were NOT NULL
literals until `tools/Import-MonsterResourceColumns.ps1` backfilled `FirstAttack`, `GroupFirstAttack`,
`VisibleRange`, `ChaseRange`, `AttackRange`, `RunSpeed`, `Size` and `Scale` from the 9.4 source — the
same import trap the skill columns hit. See `docs/superpowers/specs/2026-07-17-monster-ai-design.md`.

## Monster skills

`docs/packet-specs/socle-competences-monstres.md`. `MonsterResource.monster_skill_link_id` keys
`MonsterSkillResource`, exported to `DevConsole/monster-skills.73.json` (`tools/export_monster_skills.py`,
entries, including zero-probability Lua slots, in `id, sub_id` order) and joined to `SkillResources` by
`MonsterSkillCatalog` at startup. **The pick is the official one** (`StructMonster::AI_processAttack`): when
the monster may attack, each entry in order draws 0..9999 and is cast when `probability × 10000` exceeds it;
a skill on cooldown moves on, and the first cast **replaces the swing**. A skill whose `is_harmful` is clear
lands on the monster itself. Modelled: single-target damage 101/30001 (physical) and 201/231 (magic) through
`ICombatService.RollMonsterHit` — the swing's rule with the skill's hit and critical bonuses —, states
301/302 (harmful: on the player through `ISkillCastService.ApplyState`; otherwise on the monster, whose stats
read them) and the self heal 501. The region families (111, 113, 261, 262, 30013…), multi-hit effects,
timed ground damage and Lua trigger conditions/casts/states are implemented; the export now retains
1,067 slots and 784 triggers. See `socle-competences-zone-multi-coups.md` for coverage and remaining limits.

## Combat mechanics, casting, hate and death (2026-10-02)

Ported from the official server (`CaptainHerlockServer.exe` 2012-11 and its symbols), one sheet each:

- `socle-mecaniques-combat.md`: `Game/Services/Combat/AttackMechanics.cs` — double attack (state effect 21),
  dual wield (`LeftHandItem`), bow aim then shoot with an arrow spent, additional damage (22/23), mana shield
  (49) and reflections (43/44), all inside `CombatService.ProcessSwing`/`DamagePlayer`. **`attack_range` is
  ×100 at load** (`ItemStatCatalog.GetAttackRange`) and the player's reach is now its weapon's
  (`CombatRange.PlayerReach`): the factor was missing, which made every monster a melee one.
- `socle-lancer-competences.md`: `Game/Services/Casting/` — `CastRules.InRange` (body to body, `12 ×
  cast_range`, ×1.2 or ×1.5 on a moving target, -1 = weapon), state landing rolls, `StateStacking` (refusal
  `9`), the pending cast (`PendingCast`, 50 ms tick) with Escape cancel, damage pushback by `casting_level`
  and interrupting states. **Every damage family goes through the same pending cast**, the area and
  multi-hit ones included: their fires start in `CastDamageSequence` once the delay is over.
- `socle-haine.md`: hate list (see Monster AI). `socle-vitesse-echo.md`: the echoed speed byte is ÷ 7.
- `socle-mort-joueur.md`: retained loss (`DeathExpLoss`), potions 114, resurrection by another player
  (`SkillCastKind.Resurrection`, `HIT_REBIRTH` 23), drops on a PK server only. `GameRules` options.
- `socle-competition-joueurs.md` §10: the duel and player versus player swings.

Kill experience and the death penalty both take `ConnectionInfo.ProgressLock`.

## Equipment

Both directions are wired, parsed by `GameActionPackets` and served by `EquipmentService`, which mirrors
the reference `WorldSession::onPutOnItem` / `onPutOffItem`.

`TS_CS_PUTON_ITEM` (`200`, Epic < 9.6.3) is 16 bytes: `position` (int8 @7), `item_handle` (uint32 @8),
`target_handle` (uint32 @12). `TS_CS_PUTOFF_ITEM` (`201`) is 12 bytes: `position` (int8 @7),
`target_handle` (uint32 @8). A `target_handle` that is neither `0` nor the character handle is one of the
character's summons: `GameClient` routes it to `CreatureService` (see *Summon equipment* below; not
`EquipmentService`, which cannot depend on the creatures without a DI cycle). `position` is a raw client byte,
so it is bounds-checked against the 24 wear slots — or the spare slots 24..27, see *Spare set* below — before
it reaches the database; an out-of-range slot answers
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

**Wear requirements** (`docs/packet-specs/socle-exigences-equipement.md`, official
`StructCreature::TranslateWearPosition` `0x140080e40`): before `EquipItemAsync`, both 200 and 281 refuse with
`NotActable` (5) an item whose `wear_type` is -1, whose floor `max(rank floor, use_min_level)` exceeds the
character level, or whose `use_max_level` (0 = none) is below it. The rank floors are the official server's
`{0, 0, 20, 50, 80, 100, 120, 150, 170}` (NGemity says 180 at rank 8). The official compares with
`max(level, expert level)`; no expert level exists here, so the character level alone decides. Race, class
and job depth are **allow-lists** in the official (`ItemBase::nLimit`, `job_depth`). They are now checked
on both paths using `ItemWearFields`: repository race bits Deva/Asura/Gaia = 1/2/4, class bits
fighter/hunter/magician/summoner = 1024/2048/4096/8192, and the item's depth mask tested with
`1 << depth index` like the official. Zero allows nobody. **Whether `JobResources.JobDepth` holds the index
0..3 (the 9.4 export) or a bit 1/2/4/8 is not established**: `JobDepths` reads the encoding from the whole
table (a 0 or 3 is an index, a 4 or 8 a bit) for the equipment and quest gates alike.
`EquipmentService` caches class/depth from `IJobResourceRepository.GetWearFields`, resolving the current
job (100/200/300 for job 0 by race); an unknown job refuses the equip. Class is never guessed from job ID
digits. `tools/import_epic7.py` converts all seven `limit_*` columns into the existing masks.
The embedded Arcadia migration `BackfillItemWearRestrictions` corrects 29,647 Epic 7 item IDs at startup;
it preserves other columns and IDs outside the dump. See §11 of the equipment requirements sheet.

**Spare set and swap** (`223-swap-equip.md` §11): positions 24..27 (`WEAR_SPARE_*`) take an item of their main
twin (0, 1, 22, 23) unjudged and give **no stats**; `TM_CS_SWAP_EQUIP` (223) exchanges the main and spare sets,
judging each spare item that comes to a main slot — one refusal refuses the whole swap (the official can leave an
item unworn). **World entry judges the worn items again** (`DB_Login::readEquipItemList`,
`EquipmentService.RevalidateWornItemsAsync`, before the stats): a main slot taken twice or an item whose
requirements fail goes back to the bag, in base too.

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

`ParameterB` (97/99) decodes **elemental resistance bits 0..6** (neutral, fire, water, wind, earth,
light, dark), from the official `StructMisc.h` flags. Other B bits remain undecoded; the imported data
also contains late-9.4 accessories with bit-28 masks, unlikely to exist in the 7.3 client.
See `docs/packet-specs/socle-resistances-elementaires.md` for the resistance rule and verification.

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
| `IncreaseHpMp` (10021) | MAX HP | MAX MP |

**`IncreaseBaseAttribute` (10008), `IncreaseExtensionAttribute` (10009) and `AmplifyBaseAttribute` (10011) are not
pairs**: the official `CalculateStat.cpp:681-765` reads ten single values, `var[i] × level` — 10008 and 10011 onto
P.Atk, P.Def, M.Atk, M.Def, Atk Spd, Mov Spd, Accuracy, M.Acc, Avoid, M.Avoid (10011 as `m_AttributeAmplifier`
ratios), 10009 onto HP/MP regeneration (% and points), Block chance and defence, Critical rate and power, Cast speed
and Cool time speed (subtracted). The pair reading of 10008 matched Defense/Mind Defense Training by luck and misread
19 rows (2026-10-05, `socle-passifs-combat-recharge-bouclier.md` §2).

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
flags axes plus staves and reads "equipped **staff and axe**". `vf_shield_only` is `SkillWeaponFlag.Shield`: a passive
that needs a weapon and is usable with a shield needs a **shield (class 210) in the shield slot**, whatever the weapon
(`applyStatByPassiveSkill`, `IsWearShield`; `ConnectionInfo.WearsShield`). Shield Mastery (1211) is that case.
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
- **`IncreaseExtensionAttribute` (10009)**: Shield Mastery (1211, block +1 × level with a shield) and the creatures'
  regeneration passives.
- **`AmplifyBaseAttribute` (10011)**: Avoidance Expert (1221, avoid +2 % × level). Its vars were zero in the 9.4
  import; the Epic 7 import filled them.
- `IncreaseSummonHpMpSp`/`AmplifySummonHpMpSp` (10031/10032) are the master's and reach **its summons**
  (`ResolveForSummon`: max HP, MP, SP, HP/MP regeneration), refreshed when the master learns one.
  `HuntingTraining` (10013) is **a summon's own** passive: damage dealt to and taken from a creature type
  (`CreatureExpertise`, monster `grp` → `MonsterInstance.CreatureGroup`), applied to the raw damage.
- `IncSkillCoolTimeOn*` (10063-10070) are event triggers on the combat procs (`Combat/CooldownProcs`): the owner's
  cooling skills gain or lose seconds, then a 403. `IncSkillCoolTimeOnSkillOfId` (32281) is the same proc fired when
  one of its listed skills (`var11..13`) lands: `NotifyHit(..., skillId)`.

`SkillPassiveCatalog` is frozen at startup like every other catalog. **117 skill rows** carry a supported
effect type (101 unconditional plus the 16 weapon-gated masteries), and it holds the **87** whose vars are
not all zero; an unsupported effect type resolves to nothing.
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
| `Buff` | 301, 302, `!is_harmful` | player, party, region, summons | timed states on eligible targets |
| `Aura` | 701, 702 | any | untimed state, toggled off by recasting |
| `Heal` | 501, 505, 508, 521 | player, party, region, summons | HP or HP/MP, caster magic and each target's maxima |
| `Debuff` | 301, 302, `is_harmful` | 1 (`Target`) | timed state on a visible monster |
| `PhysicalAttack` | 30001 | 1 (`Target`) | damages a visible monster |
| `MagicAttack` | 231 | 1 (`Target`) | same, tagged `SHT_MAGIC_DAMAGE` |

The cast sequence is the same for all six: `ST_Casting` (mp cost + cast delay) → the effect → `ST_Fire`
→ `ST_Complete` → `TS_SC_SKILL_LIST` for the cooldown. Only a buff or an aura refreshes the caster's stat
packets; a heal moves HP (a property), and a debuff or an attack lands on a monster.

### Buffs

A player casts a learned buff on themselves and the client plays it, shows the icon with its countdown,
applies the stats and lets it expire.

`SkillCastService.Support` resolves players, parties, regions and summons using `SkillTarget`,
`ValidRange` and the loaded `UseOn*` flags. Targets 21/32/51 select party players/summons/both;
45 selects the target's owner and summons, 31 selects a summon, 3/6 exclude the caster. Direct targets
must be visible, alive, on the same layer and within cast range; eligibility is checked again at fire.
508/521 use var10 × 12 as their healing radius, var11 as their relationship filter and, for 508,
var12 as the creature filter. Each result carries its own target and HP/MP in the multi-hit 401.

`CharacterStates` persists player and summon state snapshots on disconnect, lobby return and `/save`.
`EraseOnLogout` states are discarded; `TimeDecreaseOnLogout` states count offline time, others pause.
Login restores durations and effects before the bootstrap stats and replays icons. Summon states are
keyed by stable card handle and attach when `SummonWorldService.Enter` creates their new world handle.
Summons keep mutable HP/MP, active states and stats computed from the caller-supplied `BaseStats`
(or their entry HP/MP maxima). `CreatureService` supplies `BaseStats` (`CreatureRules.SummonStats`), and a
summon's swings read its buffed `SummonPresence.Stats`.

Party/summon auras use 11-second leases, refreshed while the source is active, and remove projected
states on exit, range/party changes or toggle-off. Borrowed states are not persisted. A summon-only aura
stores an activation marker (`StateId = 0`) without buffing its caster. Details and validation:
`docs/packet-specs/socle-buffs-groupe-invocations.md`.

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
same A/B split as items, and B decodes resistance bits 0..6. So `StateCatalog` reuses `ParameterBitset`,
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

**A debuff now moves the monster's stats**: the monster has a stat block (`MonsterCombatStats`) and
`CombatService` folds its active states into it on every hit, so a state lowering defence lowers the
damage it resists. Only 9 of the 29 harmful `AddState` skills even carry a stat state; the rest are
mechanics nothing models. `probability_on_hit` is imported but resistance is not modelled: a debuff always lands.
Whether this client renders a state icon on a monster at all is **unverified**.

### Offensive skills

`effect_type` 30001 (`PhysicalSingleDamage`, 36 player skills) and 231 (`MagicSingleDamage`, 19), both
`is_harmful` and `target = 1`. Region 30011/261, sequential 232/263, at-once 241/30016 and persistent
ground 271 now use `SkillAreaRules`, multi-result packets and `SkillEffectScheduler`.
See `socle-competences-zone-multi-coups.md` for the supported families and precise limitations.

**One damage rule, one death path.** `CombatService` owns damage, death, the corpse, drops, reward and
respawn; the cast path must never reimplement any of it. `ICombatService` exposes `RollHit` and
`ApplyDamage`, `ProcessSwing` uses the same rule, and `SkillCastService` calls them — so an auto-attack
and a skill are **judged by the same rule through the same code**, and the whole death sequence behaves
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

**The base damage is the reference's** (`SkillDamageCurve`): physical `attack × (var0 + var1 × lvl) + var2 +
var3 × lvl`, magic `magicPoint × (var0 + var1 × lvl) + var3 + var4 × lvl`, with `hit_bonus + level gap ×
percentage` and `critical_bonus + critical_bonus_per_skl × lvl`; the hit's `flag` (int32 @14) carries
miss/block/critical. Enhancement terms are zero.

## Teleporters and field props

**The portals in the world are not NPCs.** No teleporter NPC exists within 24 839 units of the spawn
point (94454, 126040, Lost Island — the former default; a new character now starts on the Island of
Trainees at its race's official point, see *Point de retour*), so a double-click on a portal is not `TS_CS_CONTACT`. They are
**field props**: map objects carrying an id, a position and a script.

**A prop is used by casting a skill on it.** Double-click makes the client cast the prop's
`FieldPropResource.activate_id` at the prop's handle — an ordinary `TS_CS_SKILL` (`400`). The effect is
`EF_ACTIVATE_FIELD_PROP = 0x251D = 9501` (`SkillResources` 6901-6910); **538 of 763 props use skill
6904**. `BuffCatalog` classifies it as `SkillCastKind.ActivateProp`, so the cast path stays one
dispatch. **A prop's activate skill is never learned** — the client casts it because the prop
advertises it — so the learned-skill gate is skipped for this kind and the level is 1; the prop itself
is the authorisation. `FieldPropUsage` ports `FieldProp::IsUsable` (level, race, job, skill level); the item, quest, worn item and
nearby monster conditions need the database or the world and are awaited by `FieldPropUse` before the cast
starts (`socle-props.md`: use count, regen and life time, drops, one caster at a time, Lua scripts).

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

**A prop is placed only where its template's `local_flag` does not exclude `GameRules:LocalFlag`**
(`FieldPropManager::RegisterFieldProp`, `FieldPropTemplateOptions.LocalFlag` from `export_field_prop_rules.py`): the
holiday and event props carry 1048575, so 1 237 of 3 189 placements stand (`socle-props.md`, *Pays et props d'événement*).

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
like every other time field. **`game_time` is Unix time in seconds** (`WorldClock`), what the official
server writes (`SendGameTime` = `_time64()`) and NGemity too: the client derives its day and night from it.
It was `0`, so the world had no hour. `/gametime <hours>` shifts it for everyone to test the cycle (0 resets,
not persisted); see `docs/packet-specs/socle-poids.md` §1.

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
the initial import only copied directly mapped scalar columns. `RaceRestriction` and `JobRestriction`
are now filled from the seven Epic 7 `limit_*` columns by the importer and an embedded data migration
(`socle-exigences-equipement.md` §11); `SetPart` maps `set_part_flag` in the Epic 7 importer. Historically the
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
client → serveur de la famille (`1300`, `1302`, `1304`, `1306`, `1308`, `1309`, `1310`) sont déclarés,
lus et bornés (une fiche chacun). **La mécanique est celle du serveur officiel** (`AuctionService`,
`docs/packet-specs/socle-encheres-mecanique.md`) : tables `AuctionListings`/`AuctionKeepings`
(`Version0018_AuctionHouse`), objet en vente = ligne `Items` sans personnage ni compte, taxe 3/4/5 % pour 6/24/72 h,
mise prélevée tout de suite (×1,01), achat immédiat, annulation, échéance, **tout l'or entre joueurs passe par le
coffre** (`TM_CS/SC_ITEM_KEEPING_LIST` 1350/1351 de 3 859 octets, `TM_CS_ITEM_KEEPING_TAKE` 1352, 15 jours), seul l'or
de l'acteur change, écrit dans la même transaction. Recherche par catégorie, nom anglais et « équipable »
(`auction-catalog.73.json`, `tools/export_auction_catalog.py`).

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

`TM_CS_CHECK_BOOTH_STARTABLE` (`711`, 7 octets, en-tête seul) **n'a pas de créneau dans le dispatch
entrant** du client de 7.3, mais le client **construit et émet** la trame (`SFrame.exe 0x48CFD0`, un seul
site d'appel `0x49A176`) : elle est déclarée, lue, journalisée et laissée sans réponse
(`docs/packet-specs/711-check-booth-startable.md`). Aucune règle n'en découle.

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
refusées : 200, 201, 203, 204, 208, 218, 219, 253, 400, puis 281 (panoplie, le geste de 200) et
251/252 (marchand, « un autre magasin »). `700` et `701` sont hors de cette liste. Le
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

### Étal de joueur — visibilité (702/703/704)

`TM_CS_WATCH_BOOTH` (702), `TM_SC_WATCH_BOOTH` (703) et `TM_CS_STOP_WATCH_BOOTH` (704) sont déclarés dans
`GamePackets` **et** dans la boucle de réception : `702` et `704` sont des trames de 11 octets (en-tête 7,
`target uint32` à +7), `703` est la seule réponse et fait `14 + 83 × count` octets (`target` à +7,
`type uint8` à +11, `count uint16` à +12, enregistrements de 83 à +14 : le motif de 75 octets
d'`ItemFixedInfoWriter`, `appearance_code` inclus, puis le prix déclaré `int64` à +75). Le contenu de `703`
est **résolu** contre l'inventaire du propriétaire (`ICharacterService.GetItemByHandleAsync`), jamais les
triplets bruts du `700`. Le client envoie `702` au clic sur le panneau de nom de l'étal (message interne
`SMSG_WATCH_BOOTH`, drapeau 1 = 702, 0 = 704), jamais au-delà de 100 unités ; `703` est la seule trame de
la famille qu'il sait afficher.

`BoothWatchService` retrouve le propriétaire en cherchant la session dont `CharacterHandle` vaut `target`
et qui tient un étal ouvert (aucun index handle → session n'existe). Choix du lot, faute de référence
(NGemity ne traite rien de la famille) : un étal inconnu ou fermé répond `TS_SC_RESULT(702, NotExist)` avec
le handle, un objet déclaré qui n'est plus dans le sac est **omis** (le `count` suit), et `704` répond
`Success`. Rien n'est envoyé au propriétaire ni à personne d'autre ; l'observation s'oublie sur `704` et
avec la session. Le verrou d'actions (`BoothRules.GateAction`) couvre désormais `702`. **Pas testable en jeu
tant qu'aucun joueur n'en voit un autre** : il faut voir l'étal pour cliquer dessus. Voir
`docs/packet-specs/socle-booths-visibilite.md`.

### Étal de joueur — commerce (705-710)

`BoothTradeService` (`docs/packet-specs/705-buy-from-booth.md`). Tailles mesurées dans le client : **705 =
13 + 75 × N** (le `13 + 85 × N` de `socle-booths.md`, tiré de rzu, était faux : le client n'envoie que les 75
premiers octets de chaque enregistrement de 703), 706 = 19, 707 = 11 + 4 × H, 708 = 11 + 53 × N (compte
`uint32`), 709 = 11, 710 = 14 + 83 × N.

- **`type` 1 = vente, 2 = achat**, déduit et non prouvé (1 est le défaut de la fenêtre de création, et 703 ne
  garde que `type == 1`, comme le `is_sell` de 710) : à confirmer en jeu par le `type=` journalisé.
- **L'étal se voit par le statut** : `ActorStatus.ForPlayer(info)` compose désormais le bit d'étal
  (`PlayerSellBooth` 1<<10 / `PlayerBuyBooth` 1<<9) avec PK, assis, combat et marche — les quatre sites
  d'envoi passent par cette surcharge. Ouverture et fermeture publient un 500 au propriétaire et à ses
  observateurs (`IPlayerVisibilityService.SendToObservers`) ; 707 reçoit les noms des étals ouverts.
- **Commerce** : il faut regarder l'étal (702). Le `gold` déclaré est un **prix unitaire**. Ordre : réserver
  les unités de l'étal sous `BoothLock` (`ConnectionInfo.UpdateBooth` + `BoothTradeRules`, pur), puis l'or sous
  `GoldLock` de chaque session (`TryDebitGold` / `TryCreditGold`, plafond officiel 10 000 000 000, `GoldRules`), puis
  `ICharacterService.TransferItemsAsync` : tout ou rien, sous les **deux** verrous de personnage pris dans un
  ordre fixe (`CharacterGate.RunPairAsync` — une tranche n'est pas réentrante, deux noms sur la même tranche
  ne la prennent qu'une fois), pile entière = même ligne, partie de pile = nouvelle ligne aux mêmes attributs,
  **les deux soldes écrits dans la même sauvegarde**. Tout échec rend l'or et les unités.
- **`CharacterGold` passe désormais par `GoldLock`** (`AddGold` pour le butin et `/gold`) : deux commerces
  simultanés auraient sinon dépensé les mêmes pièces.
- Réponses : client ← 1001, 207 ou 254/255, `TS_SC_RESULT(705|706, Success, handle de l'étal)` ; propriétaire
  ← 1001, 207 ou 254/255, 710 (`target` = le client) ; fenêtres ouvertes ← 703 rafraîchie. Étal épuisé, 701,
  lobby et déconnexion → `CloseBooth` : statut sans bit puis 709 aux spectateurs.
- 705/706 sont dans le verrou d'actions de l'étal ; un seul commerce par client à la fois
  (`BoothTradeInProgress`). Aucun poids n'est jugé.

## Quêtes — cycle 7.3 (600/601/603/604/605 et dialogues PNJ)

- `QuestService` lit les 765 définitions importées et `QuestLinkResource`. Au contact, il ajoute les
  offres admissibles au dialogue du PNJ ; `quest_info(code)` ouvre `QUEST|code|textID`, titre conservé,
  types 3/7/8 et boutons littéraux `START`, `REJECT`, `NULL`, `REWARD`, `OK`.
- Les commandes de dialogue sont une grammaire fermée et doivent avoir été annoncées. Acceptation
  et remise vérifient aussi le PNJ visible et le code de quête du dialogue courant. Une réponse
  asynchrone périmée ne peut pas rouvrir un dialogue fermé ou remplacé.
- Acceptation : plafond de 20, prérequis OU/ET, répétabilité, délais d'acceptation et de répétition,
  niveau, niveau de métier, métier, race, classe et profondeur de métier (lues dans `JobResource`).
- Objectifs : 101 chasse totale ; 102 chasse individuelle ; 103 collecte (six couples) ; 106/107/109
  objets de chasse ; 201 compétences ; 301 équipement porté amélioré ; 302 enchantement ; 401 contact ;
  501 profondeur/niveau de métier ; 601 chaos ; 901 contrats de chasse aléatoires.
- La mort d'un monstre crédite les membres proches du groupe. `TryKill` attribue la mort une seule
  fois. Les groupes de cibles négatifs et les tirages 901 utilisent `RandomPoolResources` (1 637 cibles
  importées). Les six `Value` du contrat aléatoire persistent ses trois couples cible/quantité ;
  abandonner puis reprendre conserve le tirage. Le multiplicateur de récompense suit les coefficients
  du catalogue. Les objets de quête tombent pour chaque membre concerné et restent personnels.
- Chaque changement d'inventaire recalcule les collectes et envoie 601. Les objets portés, équipés
  par une créature, stockés ou en vente ne sont pas consommables. Perdre un objet requis remet la quête
  en cours. Le délai de type 1 est sauvegardé et suspendu hors ligne ; le type 2 conserve une échéance
  UTC. Les quêtes non terminables expirées passent à l'état 100.
- 605 choisit `-1` ou un emplacement 0..5 réellement rempli. Une transaction sous `CharacterGate`
  consomme les objets, crée les récompenses (niveau du bon emplacement), crédite EXP/JP/or/points
  Huntaholic, écrit `CharacterQuestCompletions` et retire la quête active. Deux remises concurrentes
  ne paient qu'une fois. Résultat 605 puis liste 600 ; les progrès sont envoyés en 601.
- 600 garde ses entrées de 61 octets et ses deux comptes u16 ; 601 fait 40 octets. 603 retire la quête
  puis resynchronise 600 ; 604 reste sans réponse ; 602 n'est jamais émis.
- Migrations : `QuestLifecycle` (historique, temps restant, échéance, index unique des seules quêtes
  actives), `QuestGoldReward` (colonne or et reprise des 765 montants importés), `RandomQuestPools`
  (table et 1 637 cibles). Elles sont appliquées par le démarrage existant du serveur.
- Les 52 quêtes 701 sont proposées et leur progression externe est persistante via
  `set_quest_status` et `IQuestService.RunScriptAsync`. Les scénarios particuliers restent liés aux
  systèmes de donjon/événement correspondants. Six contrats 901 ont trop peu de cibles dans la plage
  niveau ±4 et sont refusés.
- **Faveur et plafond d'or** (fiche §12) : la remise crédite `favor` au PNJ (groupe 999) dans
  `CharacterFavors` et retire `favor` au groupe de haine ; `limit_favor` est jugé au démarrage (aucune quête
  Epic 7 n'en porte) ; une remise qui dépasserait l'or porté maximal répond `END|TOO_MUCH_MONEY|code` et ne
  consomme rien. Le poids n'est pas jugé, comme chez l'officiel.
- Vérification : `QuestLifecycleTests`, tests de groupe, modèles EF et essai PostgreSQL explicite
  avec migrations des deux contextes et échec SQL forcé pendant la récompense. Détail :
  `docs/packet-specs/socle-cycle-quete.md`, §11.

## GM commands

A chat line whose first character is `/`, on any channel but a whisper, is a command: it is run and
never relayed as chat (NGemity's `WorldSession::onChatRequest` rule). The 7.3 client does forward
such a line — `/position` came back as an echo before this module. `GameClient.HandleChatRequest`
hands it to `GmCommandService` (`Game/Services/GmCommands/`), whose parser, catalogue and argument
rules are pure and tested. Full list, sources and what is deliberately not ported:
`docs/gm-commands.en.md`.

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
session flag `ICombatService.RollMonsterHit` turns into a zero-damage swing. A command
therefore cannot produce a state the game itself cannot. `/sitdown`, `/battle` and `/walk` are session states carried by
`ActorStatus.ForPlayer`, which now composes PK, sitting, battle mode and walking — **every status send
must pass all four**, the mask being a snapshot.

**Not ported, on purpose**: `/run` (executes Lua, which this repository never does), `/suicide` (it
**shuts the server down** in NGemity, `World::StopNow`), `/regenerate` (spawning needs a mutable
`SpatialIndex`, which the monster infrastructure does not have) and the party commands (no party).
The `&`-prefixed command lists found online do not exist in this client: none of their strings is in
`SFrame.exe`.

`/rate` and `/rates` read and drive the server rates; see *Rates* below. `/speed [value]` (privileged) replaces
the session's stat move speed (`ConnectionInfo.MoveSpeedOverride`, read by `StatService.Compute(info)`, so the
client's own walk, the echo and the peers follow; bounded by the wire byte, 255 × 7); no value resets it.
`/ride`, `/unride`, `/titles`, `/title` and `/subtitle` are player commands.
`/job <id>` and `/race <name>` go through `IJobChangeService.ForceJobAsync`/`ForceRaceAsync` (the NPC's saves and
frames; `/race` is `SetRace` with a stone id of 0, which `ApplySkillResetAsync` reads as "no stone"), and `/maxskills`
raises the current and left-behind trees to their maximum through `SaveLearnedSkillsAsync`, one save.

## Rates

The server rates are the `Rates` section of `DevConsole/appsettings.{env}.json` — tracked, one per
server, and **read live through `IOptionsMonitor`**, so an edit applies without a restart — multiplied by
the `/rate` event running on that type. A x5 server in a x2 event runs at x10. `IRateService`
(`Game/Services/Rates/`) is the only reader; the keys, their NGemity origin and the GM commands are in
`docs/gm-commands.en.md`, *Rates*.

- **What they touch**: EXP/JP amounts and gold/chaos chances per kill (`CombatService.AwardKill`), the drop chance and the
  summon-card factor (`GroundItemService.DropForMonster` → `DropRoll.Roll`), the monster respawn delay,
  the ground-item lifetime, and the JP cost of a skill level (`SkillCatalog.Evaluate`) and of a job level
  (`LevelingService`). Separate quest and PvP rates are not exposed. `/rate chaos` and `/rate all`
  include `ChaosDrop`.
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
  summons cast their learned active skills with their own MP, stats, range, cast delays and cooldowns;
  `/hold [handle] [on|off]` stops summon actions and gates following. See
  `docs/packet-specs/socle-invocations-competences-actives.md`. Group aggro follows the official rule; **they walk at their `run_speed` and around the
  `.nfa` obstacles** (paths for chase and return), and a death costs experience. **Monsters cast their single-target,
  state, heal, region and multi-hit skills**, including Lua triggers for casts/states, reinforcements
  (`respawn_near_monster`, no automatic respawn after death) and persisted anti-bot flags
  (`set_auto_user`, account-wide clearing). Area damage includes victims outside caster streaming;
  per-observer handles filter packets and death reaches all monster viewers. Area/multi-hit casts
  validate target and weapon range before costs/cooldowns, including Lua; selection uses interpolated
  player positions. The 148 missing trigger profiles are reconstructed with user authorization
  (105 official aliases, 24 inferred skill profiles, 19 without configured skills). Raid speed, Lua
  flag, inherited layer and regular-monster drop suppression are implemented. Active monster states
  replay after ENTER (`socle-progression-monstres-quetes-titres.md`). **Damage, hit, block, critical
  and attack speed follow the official rules on both sides** (`socle-combat-reel.md`), and so do double
  attack, dual wield, bow aiming (arrows spent), additional damage, reflection and mana shield
  (`socle-mecaniques-combat.md`); elemental resistance from equipment, passive 10006 and states reduces
  skills, additional damage and reflections (`socle-resistances-elementaires.md`). A
  player at 0 HP is dead until `TM_CS_RESURRECTION` (513) brings them back in town, in place with a
  resurrection state, an official resurrection potion (effect 114, part of the lost exp back), another
  player's resurrection skill (504/30501) or, for a duel's loser, type 3 (`socle-mort-joueur.md`); items
  drop at death on a PK server only (`GameRules:PkServer`). Kill
  rewards use loaded EXP/JP, gold and chaos with damage contribution, level penalties and stamina.
  An additional dungeon bonus is configurable (default zero); the PC bang bonus follows the official
  rates (`socle-progression-monstres-quetes-titres.md`).
- Ground items are seen by nearby players; monster drops can be taken by the owner and eligible party members
- NPC dialogs render their original text and static follow-up pages, and **`RunTeleport` triggers now
  warp**; shops, the quest lifecycle and advertised Lua objective/title callbacks also execute.
  Other static dialogue actions still require their corresponding gameplay systems.
- **Field props live the official way** (`docs/packet-specs/socle-props.md`): use count, regen and life
  times, one caster at a time with the prop's own casting time, drops into the bag, item/quest/skill/worn/
  nearby-monster conditions, and the Epic 7 Lua script through the NPC script sandbox
- Skill learning, persistence and the **passive stat effects** work, including the 21 `WeaponMastery`
  skills gated on the equipped main-hand weapon, Shield Mastery (shield worn), Avoidance Expert, the master's summon
  passives and the summons' hunting expertise
- **Casting works for buffs, toggle auras, heals, monster debuffs and single-target offensive skills**
  (physical 30001 and magic 231): MP cost, cooldown, cast delay, duration, expiry, damage, death and
  reward, including region/multi-hit offensive skills and timed ground damage 271.
  **Debuffs move the monster's stats.** `cast_range`, debuff resistance, `state_type` stacking
  (`duplicate_group`, `reiteration_count`) and cast interruption (Escape, damage pushback, stun-like
  states, warp) follow the official server (`socle-lancer-competences.md`). Buff persistence, group/summon
  buffs and HP/MP region healing are implemented (`socle-buffs-groupe-invocations.md`); live client
  validation remains outstanding
- Equipping and unequipping work and persist and feed the stats; level, race, class and job depth are
  judged before 200/281 change inventory (`ItemWearRules.IsWearAllowed`, see *Equipment*).
  The Arcadia backfill is tested on disposable PostgreSQL and awaits startup on the real database.
  Lot 3 is delivered (§12 of `socle-exigences-equipement.md`): worn items are judged again at world entry,
  the spare set and the swap (223) work, and summons wear card-form items
- Stats cover job/JLv/level, equipment, the supported passive skills, active buffs and toggled auras;
  main-title effects now apply after acquisition, selection and reconnect (`/titles`, `/title <id|0>`).
  Title definitions and conditions are embedded; quest/monster/skill/gold/PK conditions and Lua
  `set_title_condition` can unlock titles; secondary titles (`/subtitle`) and the summon and crafting
  events too (`socle-titres-secondaires-evenements.md`). Siege, PK kill and PC bang events remain. Migration `Version0013_CharacterTitles` persists ownership and counters.
  `ParameterB` resistance bits 0..6
  work for items and states; other B bits remain undecoded. **Stats drive combat**: attack, defence,
  elemental resistance, accuracy, avoid, block, critical and
  attack speed all reach the damage and the swing interval
- Inventory sorting and drag-swap work; the character storage (211/212) moves items and gold between the
  bag and the account storage, capped at 1 000 stacks (`socle-entrepot-or.md`; a deposit never joins an
  existing stack), and the sort order follows the client's tab categories rather than the original
  server's comparator
- The client clock is synchronized and `game_time` carries Unix time, but movement still applies
  `ClientClockOffset` by hand rather than trusting the sync
- **Arcadia holds only what the 7.3 client knows** (2026-10-05, `docs/packet-specs/filtre-ressources-73.md`): items,
  skills, monsters, NPCs, summons and quests filtered against the client's own `db_*.rdb`, states and stats against the
  Epic 7 dump, a row still referenced kept; replay `tools/prune_to_client73.py` after any new `import_epic7.py`
- **Known gaps are listed in one place**: `docs/packet-specs/inventaire-client-73.md`, *Trous connus* — the packets the
  client reads and the server never sends, the combat procs still missing, small gaps, open decisions and what a public
  server still needs. Strike a line there when it is done.
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

### Chat et régénération

- **Chat** (`GameClient.HandleChatRequest`, règles de NGemity `WorldSession::onChatRequest`) : normal et
  cri (`TM_SC_CHAT_LOCAL` 21) vers l'émetteur et les pairs en vue ; global (4) à tous ; groupe (0x0A) aux
  joueurs de même `PartyId` (tenu par `PartyService`) et guilde (0x0B) de même `GuildId` ; **chuchotement (3)** : la cible est lue
  **jusqu'au premier NUL dans ses 21 octets** (`request_id` suit, un octet non nul s'ajoutait au nom), la
  ligne part vers elle seule et l'émetteur reçoit `TS_SC_RESULT(20, Success | NotExist)`, **jamais d'écho**.
- **Régénération** (`PlayerRegenerationService`, démarré par `Application`) : toutes les 3 s,
  `(max × pourcentage / 100 + points) × 3 / 60`, doublé assis, au moins 1, rien pour un mort — la formule
  de `Unit::regenHPMP` (`et / 6000` sur un pas de 300 ticks). `TS_SC_REGEN_HPMP` (516, **27 octets** :
  handle, `hp_regen`, `mp_regen`, `hp`, `mp`, tous `int32` à Epic 7.3) part au joueur et à ses
  observateurs, comme la référence diffuse à la région. Le cumul « 3 % ou plein » de NGemity n'est pas
  repris : une trame par pas qui change quelque chose.

### Groupe (party) — commandes de chat et lignes `@PARTY`

- **Aucun opcode** : la fenêtre de groupe du 7.3 envoie `/pcreate`, `/pinvite`, `/pjoin %d %d`,
  `/pleave`, `/pkick`, `/ppromote`, `/pdestroy`, `/pshare`, `/plist` en chat et lit des `TS_SC_CHAT`
  **type 100** (`ChatType.PartySystem`) d'émetteur `@PARTY`. `PartyService` les reçoit **avant** les
  commandes MJ (`/pk`, `/position` n'en sont pas).
- **Formats du serveur officiel** (`CaptainHerlockServer.exe` du dump Part 4, lu avec sa PDB), **pas** de
  NGemity, qui les a simplifiés : `CREATE|groupe|chef|0|`, `INVITE|invitant|groupe|id|motdepasse|`,
  `PINFO|id|groupe|chef|partage|niv.max|niv.min|type|` + une entrée par membre
  (`handle|nom|race|métier|PV%|PM%|x|y|2|`, hors ligne `0|nom|niveau|métier|0|0|0|0|0|`), `MINFO|`+entrée,
  `NEW`, `JOIN`, `LEAVE`, `KICK|groupe|membre|`, `PROMOTE`, `DESTROY`, `MODE`, `LOGIN|groupe|membre|`,
  `LOGOUT`. Règles : chef seul pour inviter/exclure/promouvoir/dissoudre/partager, **le chef ne peut pas
  partir**, 8 membres, `/pjoin` exige le mot de passe de l'invitation. PV/PM en pourcentage tronqué à
  l'octet, **1 tant que non nul** ; `MINFO` rediffusé quand un pourcentage change (PV, PM, régénération).
- **Persistés** (`socle-groupe.md` §6) : un groupe survit à la sortie de ses membres (LOGOUT, puis LOGIN et PINFO
  au retour) **et au redémarrage** : `Parties` + `Characters.PartyId` comme la table `Party` et `Character.party_id`
  de l'officiel, écrits par une file unique après chaque changement (`IPartyStore`), relus avant l'ouverture du réseau
  (`IPartyService.LoadAsync`, règles de `PartyManager::loadPartyList`). Seuls les groupes ordinaires sont stockés ; le
  mot de passe d'invitation est retiré au rechargement. `ConnectionInfo.PartyId` vient toujours du service. Les membres
  en ligne à 540 unités du monstre partagent l'expérience, les JP et le chaos. L'or se partage au
  ramassage entre membres à 400 unités du ramasseur, indépendamment du mode. Les membres du groupe
  peuvent ramasser le butin du monstre ; `monopoly` l'attribue au ramasseur, `random` tire un
  bénéficiaire proche et `linear` tourne entre les bénéficiaires proches.
- Fiche, adresses des fonctions officielles, écarts et `NON ÉTABLI` : `docs/packet-specs/socle-groupe.md`.

### Amis et liste de blocage — commandes de chat et lignes `@FRIEND`

- **Aucun opcode**, comme le groupe : `/add_friend`, `/del_friend`, `/add_denial`, `/del_denial`, et des `TS_SC_CHAT`
  d'émetteur `@FRIEND`, type **140** pour `FLIST|nom|statut|…` (1 en ligne, 0 hors ligne, **2 en ligne mais bloquant
  le lecteur**), `DLIST|nom|…` et `FSTATUS|nom|1|`/`0|`, type **13** pour les messages (`@467\v#@friend_name@#\vNom`).
  Règles du serveur officiel (`GameMessage.cpp` `onAddFriend`…, `DB_Friends.cpp`) : 25 entrées, nom 4-18,
  `@482`/`@481`/`@478`/`@469`/`@473`, entrée en jeu = `FLIST` + `DLIST` + `FSTATUS` aux joueurs qui vous ont en ami
  (sauf ceux que vous bloquez), sortie = `FSTATUS 0`, chuchotement d'un joueur bloqué = `TS_SC_RESULT(AccessDenied)`.
- `FriendService` (listes en mémoire des joueurs en jeu), `FriendStore` sur **`CharacterFriends`** (ids de personnage,
  pas les noms de l'officiel : renommage et suppression suivent seuls ; `Version0023_CharacterFriends`).
- **Suppression de personnage** : seul un nom de la liste du compte **authentifié** est accepté ; avant, n'importe quel
  personnage pouvait être supprimé par son nom, et la liste 2001 suivait le compte nommé dans la trame.
- Fiche : `docs/packet-specs/socle-amis.md`.

### Poids porté

- **Un objet pèse `weight` × quantité, un objet porté ne pèse rien** ; le max (`10 × (niveau + force)`, sacs,
  états) était déjà dans `StatCalculator`. `ConnectionInfo.CarriedWeight` suit le sac : **`CharacterService`
  publie après chaque opération d'inventaire** (`RunInventoryAsync` / `RunPairInventoryAsync` →
  `IInventoryChangeFeed`), l'entrepôt aussi, et `CarriedWeightService` relit le sac du joueur en ligne. Une
  nouvelle opération d'objets dans `CharacterService` doit passer par ces deux méthodes, sinon le poids la
  manque.
- Effets, tous ceux du serveur officiel : **marche ×0,5 dès 75 %, ×0,1 à 100 %** (`ConnectionInfo.MoveSpeed`,
  pour l'écho, les pairs et l'estimation de position — plus la constante `EchoedMoveSpeed` seule ; l'octet de
  vitesse du fil est la vitesse ÷ 7, comme pour les monstres, `socle-vitesse-echo.md`) ;
  **`TooHeavy`** au ramassage, à l'achat (valeur = code), à la sortie d'entrepôt, à l'échange et au retrait du
  sac. Fiche : `docs/packet-specs/socle-poids.md`.

### Paquet 280 — `TS_TRADE` (échange entre joueurs)

- **Une seule trame de 97 octets dans les deux sens** (`TM_TRADE`) : `target_player` @7, `mode` @11, puis
  l'enregistrement d'inventaire de 85 octets @12 — l'objet est **nommé par son `uid` @20**, le compte (et
  l'or) est @28. Modes 0-11 confirmés par la table de saut d'`onTrade` du serveur officiel ; 2 (`BEGIN`) et
  9 (`PROCESS`) sont des réponses, ignorées en entrée ; 11 (`MODIFY_COUNT`) n'existe pas chez NGemity.
- Règles de l'officiel (`CaptainHerlockServer.exe`, §3 de la fiche) : cible à `g_nRegionSize` (ici
  `WorldVisibility.RegionSize`, 180) sinon `TooFar`, absente → `NotExist`, déjà en échange → `AccessDenied` ;
  offre et or bornés par la pile et le solde ; confirmation à deux fenêtres verrouillées ; poids → `TooHeavy` ;
  plafond d'or →
  `TooMuchMoney` (53). **L'écho d'une offre porte le compte offert**, pas celui de la pile (défaut NGemity).
- Exécution : l'or sous le verrou d'or de chaque session, puis **`CharacterService.ExchangeItemsAsync`, les
  deux sens jugés avant tout déplacement et appliqués avec les deux soldes en une sauvegarde** ; tout échec
  rend l'or. **Écart** : une acceptation doit répondre à une demande (l'officiel ouvre la fenêtre de
  n'importe qui). Règle PK et entrepôt ouvert non modélisés. Lobby et déconnexion ferment l'échange.
- Fiche : `docs/packet-specs/280-trade.md`.

### Paquet 102 — `TM_SC_CANT_ATTACK` (refus d'attaque)

- Trame **19 octets** : `attacker_handle` @7, `target_handle` @11, `reason` **i32** @15
  (`GameStateResultPackets.CantAttack`) ; id 102 sous `EPIC_9_6_3`, aucun champ gaté.
- Ordre de `onAttackRequest` (source officielle `GameMessage.cpp`) : attaquant mort → rien ; **3** `NOT_OWN` ;
  **1** `NOT_EXIST` et **5** `NOT_ACTABLE` (remplacés par un `EndAttack` si une attaque est en cours) ; **32**
  `NOT_ENOUGH_BULLET` (arc/arbalète **d'un joueur** sans flèche) ; un joueur assis se relève ; **6** `ACCESS_DENIED`
  si `IsAttackable` est faux (monture, incantation, chute 9001, états `EF_MEZZ`/`EF_TRANSFORMATION`/`EF_MISC` listés
  dans `AttackMechanics.BlocksAttack`, peur). 32 et 6 partent par `Connection.Send` quel que soit l'état de session.
- En pleine volée, la dernière flèche arrête l'attaque **en silence**. Le client 7.3 n'affiche un message que pour
  32 (n° 334). Fiche : `docs/packet-specs/102-cant-attack.md` (§10 pour le cas 6).

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
- L'objet lâché est vu des joueurs alentour et a un **ordre de ramassage vide**, comme chez l'officiel (seul un butin
  de monstre reçoit un ordre, `StructMonster::SetPickupOrder`) : n'importe qui le prend tout de suite
  (`socle-partage-objets-sol.md`, relecture du 2026-10-05).
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
- **Livré depuis (2026-10-02, `socle-entrepot-or.md`)** : capacité de **1 000 piles** (`StorageRules.Capacity`,
  refus `TS_SC_RESULT(212, 11)`), modes d'or **2/3** jugés comme l'officiel (`NotEnoughMoney` puis
  `TooMuchMoney`, plafonds `GoldRules` : 10 000 000 000 porté, 100 000 000 000 stocké), l'or stocké **par
  compte** dans `AccountStorageGolds`, écrit avec l'or porté en une sauvegarde, et envoyé en `storage_gold`.

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
- **`ItemResources.ItemUseFlag` est le bitset `item_use_flag`** (`ItemBase::Flag`, `ItemLoader.cpp:176`) et les
  membres de l'enum `ItemUseFlag` sont des **indices de bit** (`ItemUseRules.HasFlag`), pas des masques — établi par
  les données : les 12 objets au bit 23 (`UsableInOnlyHuntaholic`) sont les objets « exclusifs Bear Road ». Seuls les
  bits 22/23 (HuntaHolic, `socle-huntaholic.md` §7) sont jugés ; `ItemUseCatalog.UseFlags` les lit de la table.
- Le refus `ACCESS_DENIED` sur le type d'objet de NGemity est du **code mort**
  (`&& false` commenté, `WorldSession.cpp:1327`) : ne pas le porter.
- **Effets appliqués** (`ItemUseService.ApplyEffects`, après le résultat) : `IncHp`/`IncMp` (1/2, valeur
  plate), `IncHpPercent`/`IncMpPercent` (101/102, `var1` est un **ratio** : 0,10 = 10 %), `AddState` (6,
  `state_id`/`state_level`/`state_time` en secondes), et `Skill` (5) : une compétence de récupération
  `AddHpByItem`/`AddMpByItem` (509/510, montant = `var3`) rend PV/PM, sinon `ISkillCastService.ApplyItemSkill`
  lance un buff ou un soin sans garde d'apprentissage. `DevConsole/item-use.73.json`
  (`tools/export_item_use_catalog.py`, 1 788 consommables Epic 7) recouvre les champs de la base et porte
  ces compétences de récupération.
- **Délai de réutilisation** : par `cool_time_group` **1 à 40 seulement**, `cool_time × 100` ticks, comme
  NGemity (`Player.cpp:2091-2092`, `2165-2166`) ; un groupe 0 n'arme aucun délai (18 objets Epic 7 ont un
  `cool_time` sans groupe : un délai que le client ne peut pas afficher refuserait une utilisation qu'il
  montre prête). Refus = `CoolTime`. `TM_SC_ITEM_COOL_TIME` (217, **40 × `uint32` de temps restant**,
  167 octets, groupe `g` à l'indice `g − 1`) part à chaque armement et à l'entrée en jeu.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.

### Socle artisanat et enchantement — `TM_CS_MIX` 256, `TM_CS_SOULSTONE_CRAFT` 260, `TM_CS_REPAIR_SOULSTONE` 262, `TM_CS_TRANSMIT_ETHEREAL_DURABILITY` 263 / `…_TO_EQUIPMENT` 264

Fiche complète et références : `docs/packet-specs/socle-artisanat-objets.md`.

- **Socle structurel** : `CraftingSocleService` lit la trame à sa taille 7.3, la borne, résout chaque
  handle non nul contre l'inventaire du personnage, puis **refuse** (`InvalidArgument`, valeur 0) — le
  moteur d'artisanat n'existe pas. Aucun taux n'est tiré, aucun châssis n'est touché. Depuis le lobe
  ressources (section suivante), 256 est **résolu** contre `MixResource` avant ce même refus.
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
- **259 a deux sens** : rzu et NGemity y déclarent `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW` (serveur → client),
  `op_codes.md:86` et `ClientPackets.h` y mettent `TM_CS_DONATE_REWARD` (client → serveur). Seul le second
  est déclaré (`docs/packet-specs/259-donate-reward.md`) ; la fenêtre de sertissage n'est pas émise, et 260
  (`SoulstoneCraftService`, `docs/packet-specs/260-soulstone-craft.md`) n'est pas testable de bout en bout
  sans le déclencheur de contact PNJ.
- **Restent à trancher avant tout moteur** (détail en fin de fiche) : taux de réussite, sort des
  châsses en cas d'échec, coût `price / 10`, unité du `rate` de 264, articulation
  `mix_type` 801/802/803 ↔ 263/264.

### Socle artisanat — ressources et résolution (lobe A) — `MixResource` / `EnhanceResource`, 256 et 257

- Livré : `MixResourceEntity` + migration Arcadia `20260926132113_AddMixResource`, `MixResourceCatalog`,
  `EnhanceResourceCatalog` (table `EnhanceResources`, présente depuis `Version0001`), `ItemMatchCatalog`,
  `MixResourceMatcher`, branchés dans `CraftingSocleService`. Un 256 est **résolu** contre la table puis
  **refusé comme avant** (`InvalidArgument`) : une règle trouvée est journalisée en `Warning` (« resolves to
  mix rule N … effects not implemented »), une trame sans règle en `Debug`. **Aucune trame 257 n'est émise**
  tant que les effets n'existent pas. Les catalogues sont chargés au démarrage, après les migrations.
- **Les deux tables sont vides tant qu'on ne les importe pas** : l'import livré passe par `MigrateDatabase`
  (SQL Server), mais l'export CSV local porte `MixResource.csv` et `EnhanceResource.csv`, importables comme
  les colonnes de compétences. Sans données, tout 256 finit en « aucune règle ».
- **Résolution = mécanique, pas politique** : parcours de `MixResource` dans l'ordre de la table,
  `sub_material_count == N`, contrôle de la cible, appariement des matériaux et post-arrangement (code 19).
  **Piège** : la référence apparie **par position** (`MixManager.cpp:263`, même index) ; l'appariement
  **par permutation** du port vient de `getProperMixInfoSub` (`:300-318`), **jamais appelé** — code mort,
  comme `CreateItem`. Position ou permutation n'est **pas établi** en 7.3 : à trancher par un essai client.
- **109 colonnes de `MixResource` lues en position**, jamais par nom isolé (`ArcadiaSchemaPSQL.sql:471-582`,
  `ObjectMgr.cpp:1108-1146`) ; `sub_material_count` est exactement le nombre de groupes non nuls. Clés :
  `MixResource.id` unique (754/754, sans clé au schéma de référence) ; `EnhanceResource` en clé composite
  `(enhance_id, local_flag)`, l'index unique du dump sur `enhance_id` seul étant contredit par son contenu.
  Noms de table **au pluriel** (les migrations EF font foi, `ArcadiaSchemaPSQL.sql` n'est qu'une référence).
- **257 en 7.3 = 11 + 4M octets** (count `u32` @7, poignées @11+4j) ; `type` est gaté `>= EPIC_8_1`.
- **Pièges de la référence, à ne pas porter** : la boucle infinie de `procEnhanceFail`
  (`MixManager.cpp:459-501`), `max_enhance = 0` sur toute la donnée (l'enchantement ne peut pas réussir),
  `mix_value_02/03 = 0` pour les 154 lignes 101 alors que le code en tire `irand(value[1], value[2])`. Codes
  `CHECK_*` : **11, 12 et 15-18 refusés**, **8, 9, 13, 14 implémentés**, **19 et 20 décidés au
  post-réordonnancement** (20 par un `false` constant, comme la référence) ; la quantité de la trame est
  remplacée par 1 quand aucun code 10 ne l'a contrôlée, comme NGemity.
- **Moteur (lot L2, §14-15 de la fiche)** : `CraftingEngine` (pur) exécute 101 (cube, gain tiré dans
  `[mix_value_02, mix_value_03]`, chance `percentage[enhance]`, échec par `fail_result` : 1/0/4 → bit `FAILED`
  et châsses gardées, 2 → détruit à +3 ou moins sinon −3, 3 → −3 plancher 0), 103 (cube + poudre, +1, échec −1),
  311 (bit 0 := `mix_value_03`) et 501 (efface le bit `mix_value_01`, 3 = `FAILED`) ; 102 combine deux cartes
  de même compétence et amélioration (conditions retail 24/25), consomme le cube et produit une unité
  distincte à +1 ; échec `SkillCard` : détruite jusqu'à +3, sinon nouvelle unité à −3. **601
  `MIX_CREATE_ITEM`** (2 610 recettes) crée des objets, groupes de butin compris (`socle-titres-secondaires-evenements.md` §4).
  **Tous les autres types Epic 7 suivent le `MixManager` officiel** (`socle-artisanat-objets-officiel.md`) : cartes de
  créature 104/105, niveau 2xx/3xx, recyclage 401/402, apparence 603, éléments 701/702, châsses 703/704, durabilité
  éthérée 801-806 (pierre éthérée du personnage comprise). Il **remplace** les choix NGemity du 2026-09-29 : matériaux
  **arrangés** et non appariés par position, conditions 11-38 jugées, tous les matériaux de 101/103 consommés,
  `procEnhanceFail` officiel (1 vide les châsses sauf ceinture, 0 et 4 ne font rien). Un plan porte des mutations,
  copies, créations et la pierre, appliquées en une sauvegarde (`ApplyMixAsync`). Un objet naît à sa durabilité éthérée
  maximale et **s'use au combat** (`EtherealWear`, `socle-usure-chute-scripts-pnj.md`) ; les 0 hérités ont été remplis
  une fois (`Version0021_EtherealDurabilityBackfill`), donc **0 veut dire épuisé** (plus de stats). L'application applique tout en une
  sauvegarde, **seulement si la cible est encore dans l'état où le craft a été décidé**. Réponse : 255/254 par pile,
  207 pour la cible, puis 257 (cible si réussite, vide si échec). Appariement **par position**.
  `Crafting:LocalFlag` = 1 ; données par `tools/Import-CraftingResources.ps1` (CSV 9.4), `Percentage` jusqu'à 25.
  Fiche : `docs/packet-specs/socle-artisanat-ressources.md`.
  Complément 102 : `docs/packet-specs/socle-artisanat-cartes-competences.md` ; une pile peut fournir les
  deux cartes, les unités restantes gardent leur amélioration, 257 rapporte la nouvelle poignée seulement
  sur réussite. `ItemMatchFields.SkillId` provient de la ressource, les quantités et états des matériaux
  sont revérifiés sous le verrou d'inventaire avant toute modification.

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
- Les six handles (des poignées de **carte**) viennent de `ConnectionInfo.SummonSlots`, posé par
  `CreatureService` à l'entrée en jeu (traduit des sids d'invocation de `CharacterEntity.SummonSlotItemIds`)
  et à chaque formation, remis à vide par `ClearCharacterSession`. La 303 d'entrée en jeu part **après** les
  301 des invocations formées (`SendCharacterInfo`), donc depuis `CreatureService.OnWorldEntryAsync`, plus
  depuis l'amorçage de `GameActions`.
- **303 va dans les deux sens.** Le client émet aussi 303 (constructeur VA `0x48cd10`, 32 octets,
  `open_dialog = 0`, six `card_handle`) quand le joueur valide sa formation ; sans bras, il atteignait le
  `throw` (observé en jeu : deux `Unknown Packet Type` juste après l'ouverture de la fenêtre).
  `GameClient.HandleEquipSummon` le lit (`TryReadEquipSummon`, 32 octets exacts), le journalise et
  le confie à `CreatureService.EquipAsync`, portage de `StructPlayer::EquipSummon` : seules les cartes du
  joueur portant `ITEM_FLAG_SUMMON` (bit 31, carte apprivoisée) sont liées, dans la limite de Creature
  Control (1801), une invocation dehors ne peut pas être retirée, et la formation résultante est
  **toujours** renvoyée avec l'`open_dialog` reçu.
- Le `throw` final porte désormais l'id (`Unknown Packet Type 303`) : l'erreur nomme le paquet orphelin.
- Détail et réserves : `docs/packet-specs/324-get-summon-setup-info.md`.

### Passifs qui posent un état au combat (2026-10-05)

`Combat/StateProcs` charge les effets de compétence 10048..10062 et lit les déclencheurs des
états actifs 26, 36..39, 3201..3211 et 3311. `CombatService.NotifyHit` reçoit coups, critiques,
esquives et blocages des joueurs, invocations et monstres ; mort/meurtre restent dans les chemins
de dégâts et de récompenses existants. Conditions communes avec l'énergie dans
`AttackProcConditions` : arme, probabilité entière, PV, masque, élément ; `_KILL_TAG` pour la mort.
`CastInterrupts.ApplyCombatState` rejoint le moteur de cumul, statistiques et diffusion existant,
avec source/pulse corrects et coût MP officiel. Pas de cycle DI, de migration ni de nouvel opcode.
Le déclencheur 314084 pose 314085, dont la fin relève le joueur mort sur place (3321, `AfterPlayerStatesRemoved`).
Fiche et limites : `docs/packet-specs/socle-passifs-etats-combat.md`. Tests : `StateProcsTests`,
`EnergyProcsTests`, `ServiceGraphTests`.

### Paquets 450/451/452 — niveaux de compétences des cartes

452 (11 octets, handle de carte à +7) répond désormais **451**, depuis
`GameMessage.cpp:9919-9945` du serveur officiel 2015 : niveaux de base réellement appris,
liste vide pour une carte sans invocation, résultat NotExist pour un handle absent.
451 = **9 + 5 × count**, aucun handle ni cooldown ; 403 reste le chargement/apprentissage.
**450 est commenté dans l'officiel** : déclaré et consommé sans effet, aucune structure ou
réponse inventée. Détails : `docs/packet-specs/450-skill-level-list.md`,
`451-skill-level-list.md`, mise à jour en tête de `452-summon-card-skill-list.md`.

### Paquets 322/323 — renommage d'une invocation

322 = **11 octets**, handle unique ; fenêtre ouverte par le Lua officiel du dompteur,
cible détenue conservée en session, refus des montures prêtées (`RidingKind = 2`).
323 = **26 octets**, nom seul, autorisation consommée une fois. Tarif `niveau × 1000`,
nom de 4 à 18 caractères ASCII et mots interdits vérifiés ; nom/or sauvegardés ensemble,
remboursement sur échec. Propriété 507 au maître, nom 30 à ses observateurs si l'invocation
est sortie. Une sauvegarde de progression ancienne ne réécrit plus son nom.
Détails et NON ÉTABLI : `docs/packet-specs/322-show-summon-name-change.md`.

### Paquets 512/514 — cible et SP

512 = **11 octets**, cible unique du joueur destinataire. `GameClient.SetTarget` envoie
au joueur seul ; `/passist` l'utilise avec la traduction des handles de monstres et garde
la ligne ASSIST officielle. Le C++ définit SendTargetMsg sans appelant retrouvé : ce
raccordement est une adaptation explicite. Aucune boucle d'écho 511/512.
514 = **15 octets**, handle + deux int16 (SP/maxSP), au maître seul : chargement,
recalcul, évolution et changement de SP. Base 1000, bonus 10031/10032 du maître,
options/états du bit 23 interprétés comme MaxSP sur l'invocation. SP sauvegardés dans
Summons.Sp, setter Lua lié et borné. Fiches : `512-target.md`, `514-sp.md`.

### Paquets 3003/3004 — fenêtres Lua

Fonctions officielles `dlg_special(window, trigger, argument[, name])` et
`dlg_general(text[, name])` liées au bac à sable NPC/props/zones. 3003 = **13 + W + A + T**,
3004 = **9 + L** : longueurs UTF-8 en octets, aucun NUL transmis, limite totale 1024.
Émissions après réussite du script et de sa sauvegarde ; callback 3001 annoncé exactement,
consommé une fois, utilisable sans PNJ. Une fenêtre de saisie ajoutant des paramètres au
callback exige encore sa validation spécifique. Fiches : `3003-show-window.md`,
`3004-general-message-box.md`. Ce SFrame 7.3 route 3004 malgré la note « Since 7.4 » de rzu.

**Fenêtres natives** (`socle-fenetres-script.md`, `ScriptWindows`) : `secret_dungeon_confirm_window`,
`instance_dungeon_confirm_window`(2), `dungeon_raid_confirm_window`, `recall_feather_confirm_window` (objet 112,
`RecallFeatherService`) et `number_input_window` (`show_channel_set`), rappels mesurés dans `SFrame.exe` et acceptés
par égalité stricte, une fois. **`warp_to_instance_dungeon`/`warp_to_secret_dungeon` téléportent sans fenêtre** ;
seules `enter_instance_dungeon`, `enter_secret_dungeon`, `leave_instance_dungeon` et le raid de `enter_dungeon` en
ouvrent une (`NpcDialogService.OpensConfirmation`), et aucun Lua Epic 7 n'appelle les trois premières.

### Apprivoisement et invocation des créatures — étape 0 (fiche `docs/packet-specs/socle-apprivoisement-invocation.md`)

- Les trois sorts de créature sont **4001** (invocation, effet 601), **4002** (renvoi, effet 602) et **4003**
  (apprivoisement, effet 603) : les ids sont tranchés par les *effets* et les *nombres* (l'export 9.4 et la
  table du client 7.3 donnent les mêmes `cost_mp` 60/5/80 et le client porte sur 4003 les deux coefficients
  d'apprivoisement 0.06/0.03). **Piège :** les libellés du client 7.3 sont décalés d'un cran
  (`50004001` = « Recall Creature », `50004002` = « Creature Taming ») et « Summon Creature » y est orphelin
  (`40065065`) ; ne jamais déduire l'id d'un sort de son nom. `4004` (second sort d'effet 603) n'existe pas
  dans le client 7.3.
- `TM_SC_TAMING_INFO` = **310** en 7.3 (`1310` à partir d'`EPIC_9_6_3`, à ne pas déclarer), 16 octets :
  `mode` @7, `tamer_handle` @8, `target_handle` @12. Modes : 0 début, 1 abandon, 2 réussite, 3 échec. Il est
  **diffusé à la région du monstre**, pas au seul apprivoiseur.
- La carte liée est un objet du groupe 13 (`Summoncard`) dont le champ `flag` (@34 du motif d'objet de 75
  octets) porte **le masque rétail** `0x8000_0000` (`ITEM_FLAG_SUMMON`) ; l'apprivoisement en cours utilise
  `0x2000_0000` (`ITEM_FLAG_TAMING`). `ItemFlag.Summon` vaut 31 (indice de bit) : écrire le membre au lieu du
  masque casse la lecture de `GroundItemDropRules` et le client.
- La carte requise par une cible vient de `MonsterResource.taming_id` → `SummonResource.card_id`.

### Apprivoisement, formation et invocation — livrés (fiche §15, serveur officiel)

- **Source : le serveur officiel** (`GameProc.cpp` `SetTamer`/`ClearTamer`/`ProcTame`/`AllocNewSummon`,
  `StructPlayer::EquipSummon`/`DoSummon`), pas NGemity. Données : `DevConsole/creature-catalog.73.json`
  (`tools/export_creature_catalog.py` : 147 invocations Epic 7 et leurs stats, noms préfixe/suffixe, noms des
  2 037 monstres apprivoisables), `ICreatureCatalog`. Service : `Game/Services/Creatures/CreatureService.cs`,
  règles pures dans `CreatureRules`.
- **4003** : codes officiels dans l'ordre (5, 90, 91, 92 PV non pleins, 93 pas de carte vide, 70 déjà en cours),
  310 à chaque joueur qui voit le monstre avec **sa** poignée et ligne `TAMING_*|nom|` (type 100, `@PARTY` ou
  `@SYSTEM`), fenêtre de **5 min renouvelée par chaque coup** de l'apprivoiseur, abandon (310 mode 1) au-delà ou
  quand le monstre rentre. À la mort : 500 unités au plus, tirage `taming_percentage × (var0 × niv + var1 × enh
  + 1)` ; **réussite = aucun butin** et une nouvelle carte liée (`ITEM_FLAG_SUMMON`, `Taming = 18`) avec sa ligne
  `Summons`, en une opération ; échec = une carte consommée.
- **`CombatService` ne dépend pas des créatures** : il appelle `ICreatureEvents` (`CreatureEvents`, médiateur
  auquel `CreatureService` s'attache), sinon le cycle d'injection n'échouerait qu'au démarrage.
  `Tests/Host/ServiceGraphTests.cs` construit le conteneur de DevConsole avec `ValidateOnBuild` pour l'attraper.
- **4001/4002** visent la **carte** formée ; l'invocation principale est renvoyée avant la suivante, revient à
  la connexion (bruit 50) et suit le warp. Marche (`TM_CS_MOVE_REQUEST` sur son handle, `speed_sync`) et
  attaque (`TM_CS_ATTACK_REQUEST` sur son handle) : ses dégâts passent par `ApplyDamage` **au nom du maître**.
- Écarts (fiche §15.5) : ligne `Summons` créée dès l'apprivoisement, `ITEM_FLAG_TAMING` gardé en session.
- **Suite livrée** (`socle-invocations-progression.md`) : stats officielles (`stat_id`, `CreatureEnhance`,
  `CreatureLevelBonus`, coefficient 0,7 + Creature Mastery, niveau de combat du maître), expérience de chasse
  partagée à 525 unités et JP par niveau, coups reçus, mort (pénalité, rappel après 60 s, morte jusqu'à sa
  résurrection) et régénération ; arbres de compétences par invocation (402 sur son handle, 403 au chargement et 452→451 au flip, table
  `SummonSkills`) ; monture `/ride` (320/321, Creature Riding 11001, chute 30 %) ; miroir de carte
  d'apprivoisement ; évolution à 50/100 (307) ; pages du gardien des créatures. **Équipement** (200/201 sur un
  handle d'invocation, `socle-equipement-invocation.md`) : objets en forme de carte (bit 0), au niveau de
  l'invocation, emplacements `CreatureEnhance.slot_amount` ; l'objet garde son emplacement d'invocation dans
  `WearInfo` avec `EquippedBySummonId`, donc **toute lecture côté joueur passe par
  `ItemWearRules.IsWornByPlayer`/`IsWornByPlayerAt`** — sinon un objet d'invocation à l'emplacement 0 passe pour
  l'arme du joueur.

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
- **Le flux est l'officiel** (fiche §18, `StructPlayer::Login`/`AddPet`/`SummonPet`/`onSetPetName`) : la 351
  **enregistre** un familier possédé dans le registre du client (@`0x4be8d0`), qui liera la `TS_SC_ENTER` de ce
  handle. Une 351 par cage du sac à la connexion (`SendPetInfoAsync`), le handle gardé pour la session
  (`ConnectionInfo.PetHandles`), `code` = id du familier ; une cage neuve est enregistrée à sa première
  utilisation. L'appel n'envoie plus que la 3 sous ce handle. Les lots précédents envoyaient 3 puis 351 avec
  `code = 0`, ce qui expliquerait le familier « non invoqué » du §17.4. **Le plantage mesuré au §16 (351 puis 3,
  `code = 0`) reste le risque à vérifier en jeu en premier.** Les valeurs sans source (niveau 1, PV 100, PM 0,
  `unknown` à 0) sont dans `PetSummonDefaults`. `SummonWorldService.Enter` envoie 301 puis 3.
- `ActorStatus.ForPet()` vaut 0, comme les invocations.
- **Il suit, ramasse et se nomme** (fiche §17). `PetBehaviorService` (250 ms, `NetworkService` pour la
  seule liste des clients) fait marcher le familier vers **la destination** de son maître
  (`ConnectionInfo.DestinationX/Y`, dernier point de passage du `TM_CS_MOVE_REQUEST`), à 2 m près, sans
  réémettre de `TS_SC_MOVE` tant que la cible n'a pas dérivé de 3 m, et le rappelle au-delà de 540 unités.
  **Le rayon de ramassage vient de la compétence « Collect Items » (effet 10047, `var1` en mètres : 5/10/15)**,
  **× 12 unités par mètre** (règle de NGemity pour toute portée de compétence) ; le familier prend le butin
  de son maître par le ramassage manuel, **`item_taker` = le familier et aucun `TS_SC_RESULT`**. **Le filtre
  355 suit la règle du client** (fiche §19, `PetPickupFilter`) : un bit par `type` d'objet — Consumable 0x01
  (`Supply`), Soulstone 0x02, Cube 0x04, Card 0x08, Gear 0x10 (`Armor`), Etc 0x20 —, `0x3f` « All » comparé à
  l'égalité (seul à laisser passer `Charm`), `Use` toujours ramassé, défaut `0x1f`. Aucun serveur officiel ne lit
  la 355 : le client filtre sa propre collecte et envoie une 204 dont le `taker_handle` est le familier, que
  `GroundItemService.TakeAsync(client, taker, item)` juge depuis le familier (`onTakeItem`). Le nom vit dans `Pets` (une ligne
  par cage) ; un familier jamais nommé reçoit la **353 sur son handle** **au lieu de sortir**, l'objet 920010
  (`RenamePet`, 120) la rouvre, refusé **avant consommation** sans familier dehors ; la 354 n'est acceptée que
  pour un handle proposé (`PetNameOffers`), avec **la règle des noms de personnage** (4-18 lettres/chiffres,
  mots interdits) ; refus = `@1105`/`@1106` + `TS_SC_RESULT(354)`, succès = `TS_SC_CHANGE_NAME` (30, 30 octets,
  `handle` @7, `name[19]` @11) au maître et aux observateurs, puis le résultat.
- `TM_CS_SET_PET_FILTER` (355, 15 octets, `handle` @7, valeur @11) est émis par la fenêtre « Pickup Filter »
  du client (`PET_PICKUP_FILTER`) ; déclaré et lu, sans réponse.
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
refusée plutôt que lue), le personnage réapparaît à son **point de retour** (`rx`/`ry`, voir *Point de
retour*) sur la couche 0 avec ses PV/MP au maximum,
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

### Point de retour, départ par race et téléporteurs de ville

Fiche : `docs/packet-specs/socle-point-de-retour.md` (Lua Epic 7 `Live`/`trunk`, `GetLastTownPosition`).

- **Le point de retour est la paire de drapeaux officiels `rx`/`ry`**, rangée dans `Characters.FlagList`
  (`rx:6650`, colonne existante, aucune migration), tenue en session dans `ConnectionInfo.RespawnX/Y`
  (`RespawnLayer` = 0) et **écrite dès qu'elle change** (`ICharacterService.SaveReturnPointAsync`). La mort de
  type 0 y ramène, `/home` aussi. `ReturnPointRules` (pur) porte toutes les coordonnées officielles,
  `ReturnPointService` les applique.
- **Création** (`on_first_login`) : départ sur l'île des apprentis selon la race (Déva 164474/52932, Asura
  168356/55399, Gaïa 164335/49510), retour = départ ±30. **Connexion** (`on_login`) : sans `rx`/`ry`, la ville
  de la race. **Niveau 5** (`on_player_level_up`, une fois par gain avec le niveau atteint) : le camp de l'île.
  La branche « niveau 18 » du Lua est du code mort (chaîne comparée à un nombre) et n'est pas portée.
- **Téléporteurs de ville** : leurs dialogues manquaient au catalogue. `tools/export_town_teleporters.py` les
  y ajoute depuis le Lua Epic 7 (9 contacts + bavardage de Gaïa, PNJ reliés par le `contact_script` Epic 7) ;
  le téléporteur de l'île (3005) est construit par `TownTeleportRules.BeginnerTeleporter` (quête 1025, profondeur
  de métier). `Binding_*` fixe le retour et répond par `message()` (`@SCRIPT`, type 40). `RunTeleport`,
  `RunTeleport_Begin_TO_City` et `RunTeleport_City_To_Camp` **font payer leur coût** (`PropAction.Cost`,
  `@90010008` sans assez d'or) et dispersent l'arrivée de 10 ou 100 unités. Le raccourci vers le donjon de
  guilde est omis (déclencheur non servi), les canaux de l'île ne sont pas modélisés (couche 0).
- **Aucune cinématique d'entrée en jeu n'est déclenchable** : le lecteur `SGameCutScene` du client 7.3 n'est
  construit que par l'écran de chargement et le lobby (fiche §4), et ni le serveur officiel ni le protocole
  n'ont de déclencheur.

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
`800` et `801` (trames d'en-tête seul) suivent le compte à rebours officiel (`PkModeService`, 10 s pour
l'allumer en terrain PK, 30 s pour l'éteindre, une seconde demande annule ; `socle-pk-compte-a-rebours.md`),
chacune avec son `TS_SC_RESULT`, puis `PkMode` change et le masque repart par
`GameClient.SendActorStatus`, qui passe par `ForPlayer(info)` et part aussi aux observateurs. Le mode PK
n'ouvre le combat entre joueurs que si les deux sont en terrain PK (`PkFieldService`, polygones `.nfl`
et `WorldLocation.LocationType`). Un lieu inconnu est protégé ; `GameRules:PkFieldsEverywhere` reste une
option de débogage désactivée. Compétences offensives, zones, immoralité et compteurs PK/DK sont livrés :
voir `docs/packet-specs/socle-pvp-terrains-competences-immoralite.md`. Bloody commence à 100 points,
Demoniac à 1 000 ; la propriété `immoral` transporte les points multipliés par 10 000. Les trois valeurs
sont sauvegardées avec la progression dans les colonnes existantes, sans migration PvP.

Les tests d'offsets des deux trames sont dans `Tests/Game/PkModeStatusTests.cs`.

### Lieu et météo — 900/901/902/903 (`TM_CS/SC_CHANGE_LOCATION`, `TM_SC_WEATHER_INFO`)

Fiches `docs/packet-specs/901-change-location.md` et `902-weather-info.md`. `PlayerLocationService` porte
`StructPlayer::ChangeLocation` et `WorldLocationManager` du serveur officiel :

- **901** (15 octets, `prev_location_id` @7, `cur_location_id` @11) part **à chaque** contrôle, lieu changé ou non :
  sur la 900 (position du client gardée à moins de 120 unités de l'estimation, sinon l'estimation), à l'entrée en jeu,
  à la fin d'un warp et toutes les 30 000 ticks. Le lieu vient de `MapService.GetLocationId` (polygones `.nfl`, plus
  petite priorité), tenu dans `ConnectionInfo.LocationId`.
- Lieu changé et connu : **902** avec sa météo, mode PK coupé hors terrain PK (`IPkModeService.LeavePkField` : tout de
  suite en match à mort/arène, 30 s ailleurs, allumage annulé), duel fini hors champ (2) et champ de bataille (5).
- **Météo** : `weather_change_time` est en **minutes** (× 6000 ticks, `LocationLoader`) ; tirage 0..99 sur
  `weather_ratio[tranche][météo]`, tranche = cycle de 12 h de l'heure locale de `game_time` (2 h 30 aube, 4 h jour,
  8 h 30 soir, 10 h nuit) ; un changement part aux **seuls joueurs du lieu**. La 903 répond avec la météo courante.
- `region_id` de la 902 est un `WorldLocation.id` (`x × 10000 + y × 100 + n`), jamais un indice de région de
  visibilité ; la table a une ligne par `(id, weather_id, time_id)`, repliée par `WorldLocationService`.

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
- Portée : la 1201 part vers l'acteur **et** ses observateurs (`SendToObservers(…, includeSelf: true)`),
  depuis que les joueurs se voient.
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
(placeholder, §9.4 de la fiche). De la famille HuntaHolic, 4000, 4003, 4004, 4005, 4008 (fiche seule)
et 4011 sont lus, bornés et journalisés sans réponse (`GameHuntaholicPackets`) ; le lobby (4001/4002) reste à faire.

**HuntaHolic est livré** (`docs/packet-specs/socle-huntaholic.md`, `Game/Services/Huntaholic/`, portage de
`HuntaholicManager`) : salles d'un groupe de type 3 (`nom<N_h>`, commandes `/p*` ignorées), chasse sur la couche du
numéro de salle (`MonsterWorldState.SpawnInstanceMonster`), score 1/5/10/150, maximum, fin, récompenses (points
`ceil(avantage × score)`, EXP/JP, objets), pénalité d'abandon (entrée et état 313205), sortie par warp, déconnexion et
résurrection officielles, 4250/4251 par les sorts **lancés par le serveur** 64818/64827 (`SkillCastKind.InstanceGame`),
PNJ `go_to_huntaholic` (1 000 or) et boîtes de JP. Points et 12 entrées/jour (06:00) en session, sauvés avec
`Characters.LogoutTime`. `WarpService.Warp(client, x, y, layer)` change de couche ; `IHuntaholicEvents` relie combat,
warp, sorts et résurrection sans cycle d'injection. 4008 est déclaré (bras propre). **Sortir de HuntaHolic** ramène au
point de retour `rx`/`ry`, retire les états `EraseOnQuitHuntaholic` et rend les PV/PM d'entrée ; les objets aux bits 22/23
d'`item_use_flag` sont refusés dedans / dehors (fiche §7). **Props de soin** (§8) : posés sur la couche de la salle au
départ (`IDynamicFieldProps`, props de couche que `FieldPropService` diffuse), usage unique, retour après `regen_time`,
effacés au maximum et à la fin ; leurs sorts 64807 (9502, 30 % des PV aux alliés) et 64806 (9503, zone 5 %/3 s pendant
30 s) sont `SkillCastKind.PropHeal`.

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
- **240 est produit par l'achat (251, `MarketTradeService`) et la vente (252, `MarketSellService`)**.
  L'or passe par `TryDebitGold`/`AddGold` ; l'achat envoie la pile achetée en 207 avant le résultat.
  La vente ne vérifie pas qu'un marchand est ouvert (fiche 252).
- Le déclencheur des marchands est le littéral **tronqué** `open_market(` (176 entrées de
  `DevConsole/npc-dialogs.73.json`) : `PropScript.Parse` l'accepte **avec ou sans** parenthèse
  fermante et rend `PropActionKind.OpenMarket` avec le nom du marché, vide dans la forme tronquée.
  `NpcDialogService.Select` route vers `MarketService` et **laisse le dialogue courant**.
- `MarketService` n'envoie 250 que si le nom résout un catalogue **non vide** ; sinon il refuse en
  `Warning` — jamais de fenêtre vide, aucun producteur connu d'un `250` de 13 octets (`n = 0`).
- Le catalogue vient de `DevConsole/market-catalog.73.json` (section `"MarketCatalog"`, 4 404 lignes,
  90 marchés) via `MarketCatalogOptions` / `MarketCatalog` : regroupement par `name`, tri par
  `sort_id` (égalité = ordre du fichier), comparaison ordinale, lignes de `code` nul écartées.
  `price` est le prix **absolu**, pas le `price_ratio` de la base (multiplié par le prix de base à
  l'ouverture, `ObjectMgr.cpp:851`) ; `huntaholic_point` = `trunc(huntaholic_ratio × ItemResource.huntaholic_point)`
  (`onMarketInfo` officiel) : les boutiques `bearload_*` se paient en points, débités avec l'or
  (`NotEnoughHuntaholicPoint` 58).
- **Données : `tools/export_market_catalog.py`**, depuis le dépôt SVN Epic 7 Part 4 (voir *Source data*).
  Les lignes viennent de `MarketResource.rdu`, prix = `floor(price_ratio × ItemResource.price)` de la même
  époque, lignes dont l'objet est absent du `db_item.rdb` du client 7.3 écartées (104). Le nom de marché de
  chaque dialogue vient des Lua serveur (`open_market( 'nom' )`, 20 dialogues) ou d'une règle de nommage
  écrite dans l'outil (30) ; la variante `flat_sum_*` (serveur sans boutique payante,
  `cash_usable_server == 0`) remplace sa jumelle, et les noms se comparent sans casse comme la collation
  `_CI_` d'origine. 34 dialogues restent sans marché identifiable (arène 8.1, accessoires de rang,
  `astarot`/`island`/`sealine`, HuntaHolic…) et gardent leur déclencheur tronqué, refusé.
  `PropScript` retire les apostrophes du nom.
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
- Les trois bras de dispatch sont posés près de `TM_SC_REGION_ACK`, jamais à l'ancre du `switch` final.
  Le conteneur lui-même est le lot 2, ci-dessous.
- Le savoir durable de ce socle est dans `docs/packet-specs/socle-stockage-commercial.md`, pas ici.

### Socle stockage commercial — lot 2 : conteneur persisté (10003/10004) et retrait réel (10005)

- Le contenu vient d'une table **`PaidItems`** (migration Telecaster `Version0010_PaidItems`), calquée sur
  `Telecaster.PaidItem` du dump officiel (`reference/ngemity/Database/Telecaster.sql:419-439`) :
  `item_code` → `code`, `rest_item_count` → `count`, `taken_*` horodatés au retrait. Le conteneur « kept »
  du dépôt (`ItemStorageEntity`, `StorageType` enchères/courrier) **n'est pas** ce conteneur.
- `commercial_item_uid` est **écrit par le serveur** (`(uint)ligne.Id`) et recopié par le client : la
  résolution porte **toujours** le propriétaire (`AccountId` + cible `CharacterId` nulle ou égale) et
  compare l'`Id` sur **64 bits** (`row.Id == uid`), jamais `(uint)row.Id == uid`.
- À l'entrée en jeu, `CommercialStorageService.SendContainerAsync` **lit** la table puis émet la paire
  10003/10004. Table vide = la paire du lot 1 (11 o à 0/0, puis 9 o). **Si la lecture échoue, la même paire
  vide part quand même** : la séquence d'entrée garde ses deux trames.
- Le retrait (10005) remet l'objet au sac (`ICharacterService.AddItemAsync` + trames d'inventaire),
  décrémente `rest_item_count`, horodate `taken_*` et réémet la paire. Le serveur **n'émet jamais 10005**,
  n'envoie **aucun `ResultCode`** et n'invente ni coût, ni plafond, ni acquittement.
- **Un seul retrait à la fois par session** (`ConnectionInfo.CommercialTakeoutInProgress`) : résolution,
  livraison et consommation sont trois étapes, et `CharacterGate` ne peut pas les couvrir — il n'est pas
  réentrant et `AddItemAsync` prend la même clé, donc le tenir autour bloquerait le retrait pour toujours.
  Sans ce drapeau, deux 10005 envoyés ensemble résolvaient la ligne pleine et **livraient deux fois**
  (reproduit par test à la revue du 2026-09-29) ; le second est désormais abandonné.
- Le producteur est **hors code** (boutique web externe, 10001) : sans boutique ni outil d'administration,
  `PaidItems` reste vide. Fiche : `docs/packet-specs/socle-stockage-commercial-conteneur.md`.

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

**Le duel est livré (C2…C4, 2026-10-02)** : `CompeteService` porte `CompeteManager` du serveur officiel —
règles et codes de la demande, 60 s pour répondre, compte à rebours de 10 s, 900 s dans un rayon de 500 autour
du point de départ, fins 0-5, trames 4501/4503-4506 émises, résurrection 513 type 3 du perdant (10 % des PV),
coups et compétences entre duellistes par `CombatService` (`StructPlayer::IsEnemy`). Le combat en mode PK
hors duel utilise désormais les terrains réels et l'immoralité : voir la fiche
`socle-pvp-terrains-competences-immoralite.md`. Détail du duel : §10 de sa fiche. Ce qui suit décrit
le premier lot, remplacé quand le service est présent :

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

### Socle ferme de créatures — 6000-6008

- Les neuf ids sont `X(<id>, true)` chez rzu sous « Since EPIC_7_3 » : 7.3 garde les ids nus, aucun champ
  n'est gaté. Seuls les six que le serveur lit ou émet sont déclarés ; les trois trames de résultat
  6003/6005/6007 restent non déclarées tant que les valeurs de leur `result` ne sont pas établies.
- `TM_CS_REQUEST_FARM_INFO` (6000, **7 octets**, à l'ouverture et à chaque rafraîchissement de la fenêtre)
  reçoit un `TM_SC_FARM_INFO` (6001, `8 + 120 × N` octets) **vide** : `summons = 0`, 8 octets. Le client le
  traite proprement (octet nul → pas d'allocation, `SFrame.exe 0x67219c`). C'est un **choix de lot**, pas
  un fait de référence : la ferme n'existe pas côté serveur.
- 6002 (`19 + 8 × T + 8 × C`), 6004 et 6006 (11 octets, `card_handle` @7) et 6008 (7 octets) sont lus,
  bornés, journalisés — **jamais répondus** : aucune référence n'implémente la ferme (NGemity : 0
  occurrence), et `result`, tickets, crackers, durées et `index` ne sont pas établis. 6001 reçu d'un client
  est journalisé et abandonné.
- `card_info` réutilise le motif d'objet de 75 octets (`ItemFixedInfoWriter`). Fiche :
  `docs/packet-specs/socle-ferme-creatures.md`.
- **Décision (2026-09-30) : la ferme n'est pas implémentée**, seuls ses paquets sont pris en charge. La fenêtre
  ne s'ouvre que par le déclencheur `show_creature_farm_window()` du PNJ Sonya, **volontairement non exécuté** :
  sans fenêtre, le client n'émet ni 6002 ni 6004 ni 6006 ni 6008, et on n'a pas à répondre 6003/6005/6007 avec un
  `result` inconnu. Ce que l'on sait du système retail (Epic 7.2, sources web) et le tableau décodé de
  `db_creaturefarm.rdb` (rareté, forme, renforcement → nombre de tickets) sont au §8 de la fiche.

## Usure éthérée, chute 9001, « objet obtenu », scripts PNJ (2026-10-04)

`docs/packet-specs/socle-usure-chute-scripts-pnj.md`. L'usure est jugée **en mémoire** (`ConnectionInfo.EtherealGear`,
posé par `StatService`) et écrite par lots, une écriture en vol par joueur : **ne jamais lire la base à chaque coup**.
La chute (9001, 3 s) interdit attaque, sort, objet et marche. La ligne @253/@254 suit `IsJoinable` et seuls les sites
officiels l'envoient. Les dialogues timbres/événements/cadeaux/`max_item_durability`/`tp_skill`/`random_item_change_menu`
exécutent le **Lua officiel embarqué** dans un bac à sable (MoonSharp), sur déclencheur annoncé seulement.
**`EnableRetryOnFailure` refuse les transactions ouvertes à la main** : un service qui en ouvre prend
`TelecasterOptions.WithoutRetry` (enchères, guildes), sinon il échoue sur la vraie base alors que les tests passent.

## États périodiques, énergie, attaque impossible, commandes officielles (lot Codex du 2026-10-04)

`docs/packet-specs/socle-etats-periodiques-energie.md`. **406** (36 octets) : un état dont `base_effect_id` est non nul
frappe ou soigne toutes les `fire_interval × 100` ticks, dégâts de base **figés à la pose**, par le chemin de dégâts
ordinaire, persistés (`CharacterStates.PeriodicBaseDamage`/`RemainingFireTicks`, `Version0024`). **515** (13 octets) :
énergie 10 au plus, capacité = niveau de 1082, 30 min chacune ; coût `cost_energy`, refus `NotEnoughEnergy` ; produite par
2631, les dégâts 125/30003 et les passifs 32262/32263 (`EnergyProcs`, sur chaque coup qui touche). **102** (19 octets) :
refus de `onAttackRequest` (`NotOwn`, `NotExist`, `NotActable`). Commandes `/passist`, `/change_name` (messages officiels,
retiré des listes d'amis des autres) et MJ `block_chat`, `check_auto_user`, `force_warp`, `invisible`, `kick`, `rebirth`,
`lv` (`docs/gm-commands.en.md`).

## Donjons, guildes, alliances, raids et sièges (lot Codex du 2026-10-03)

- **Donjons** (`docs/packet-specs/socle-donjons-instances-secrets.md`, `Game/Services/Dungeons/`) : catalogue régional
  embarqué (`tools/export_dungeon_resources.py`), `enter_dungeon`/`exit_dungeon` jugés (niveau, horaires régionaux,
  fermeture), les 20 choix d'instance, Vulcanus (20 clés), raids et sièges, donjons secrets ; salles privées sur les
  couches 2-127 (`DungeonRooms`), libérées par la sortie, le warp, la résurrection et la déconnexion. Une couche privée
  retrouvée en base à la connexion renvoie au point d'apparition public.
- **Guildes** (`docs/packet-specs/socle-guildes-alliances-sieges.md`, `Game/Services/Guilds/`, migration
  `Version0017_GuildsAndSieges`) : création chez un officier (fenêtres 650/660, 100 000 or), commandes `/g*`, rangs,
  alliances (`/ga*`, trois guildes), équipes d'attaque (`/rp*`), inscription et classement des raids, sièges (cœur,
  titres), taxes du donjon possédé. Lignes `@GUILD`/`@ALLIANCE`/`@RAID`. **À vérifier en jeu.**
- **Officiers de guilde et gestionnaires de siège : Lua officiel** (`docs/packet-specs/socle-dialogues-guilde-siege.md`,
  `GuildService.Lua.cs`) — `NPC_CreateGuild.lua` et `NPC_QuestClient.lua` d'Epic 7 (`tools/export_guild_dialogs.py` →
  `guild_dialogs.json`), fonctions moteur de `ScriptGuild.cpp`/`ScriptPlayer.cpp`. Exécuté sous le verrou de guilde puis
  celui du personnage, en une transaction ; `NpcDialogService` rend la page. **Ne pas y revenir avec un menu écrit dans le
  code** ; un nom de guilde ou d'alliance créé par un officier suit `IsValidName` (1-16 lettres ou chiffres).
- `WarpService.Warp(…, layer)` passe par `DungeonRooms.OnWarp` puis prend la couche : les salles HuntaHolic (couche = numéro
  de salle, autre zone) et les salles de donjon cohabitent.

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

## Source data (Epic 7 Part 4 SVN dump)

`A:\Rappelz Kiff\Epic 7 Part 4` (outside the repository, 8 GB) is Gala Lab's own SVN tree of the Epic 7
era, 2011-2012 — the closest data to the 7.3 client this project has. What it holds, mirrored on the
pipeline VPS under `/srv/navislamia/reference/epic7part4` (see its README):

- **The official game server with its symbols**: `branches/Binary/Server/CaptainHerlockServer.exe` + `.pdb`
  (x64, branch `gameserver_release/2011-12-12`, GUIDs matching), and in `branches/telnet 보안/` a later build
  plus `PrincessAuroraServer` (the auth server). The PDB names ~4 100 functions: party, guild, quests,
  taming, trade, regeneration, damage.
- **Server resource tables** as `.rdu` files: complete, self-describing dumps of the SQL Server tables
  (`MarketResource`, `QuestResource`, `MonsterSkillResource`, `InstanceDungeon*`, `ItemResource`…).
  `tools/rdu.py` reads them (150 of 152 files consume exactly; the two left are damaged copies of
  `MixResource`/`EnhanceResource` whose other copies read) and writes the same CSV as the 9.4 export.
  Format traps it encodes: a nullable column carries a leading indicator byte, and a decimal is a 16-byte
  OLE `DECIMAL`, not a length-prefixed value.
- **Server Lua in clear** (42 scripts: merchants, quest clients, job change, guild creation, login and
  level-up hooks, monster respawn) — only the scripts these branches changed, not the whole set.
- **Client builds with matching PDBs** (`SFrame_Release.exe` + `.pdb`, 2011-09 → 2012-10). **None of them
  is our client**: ours links the Visual Studio 2010 ATL (`AtlAxWin100`), every dump build the 2005 one
  (`AtlAxWinLic80`). By content it sits closest to the builds of November 2011 - January 2012 (string-set
  Jaccard 0.93): it has what they lack (instance dungeon windows, mount info, pet pickup filter 355, the
  end-quest window) and lacks what the Part 4 branch adds (mission windows, guild member list renewal,
  chat type table) — a 7.3 retail line, not the Part 4 development branch. Use these PDBs for meaning,
  never to carry an address over to our `SFrame.exe`.

The 7.3 client's own `db_item.rdb` (on the VPS, `reference/client73/`) is a community rebuild
("Written by Archemedes v0.1.0", 2025-12-07): 28 265 items, a 128-byte header, a `u32` count and records
of 6 256 bytes, item id first. It is what tells an item the client can render from a 9.4-only one.

**The Epic 7 tables are loaded into Arcadia** and win over the 9.4 import. `tools/rdu.py` writes them to
`data/epic7/*.csv` (git-ignored, like `data/sqlserver/`); `tools/import_epic7.py` (`--plan` prints the
mapping) overwrites every mapped column of an existing row, inserts the Epic 7 rows a table lacks, and
**keeps the 9.4-only rows** (nothing the 7.3 client knows points at them). Columns map by name, case and
underscores aside, plus the overrides the name cannot derive (`vf_*`, `uf_*`/`tf_*`, `str`→`Strength`,
`form`→`EvolveType`…); an array takes its numbered source columns, a NULL element becoming 0 (Npgsql
refuses an array holding a NULL). Foreign keys are written last, only when the target row exists. Result:
`ItemResources` 33 146 (3 499 9.4-only), `SkillResources` 2 721 (619), `StateResources` 1 950 (421),
`MonsterResources` 7 431 (929), `NpcResources` 1 456 (369), `StatResources` 3 899 (3 016), and from empty:
`StringResources` 102 256, `SummonResources` 147, `QuestResources` 765, `QuestLinkResources` 908,
`WorldLocations` 5 937. `MixResources` (3 965) and `EnhanceResources` (260) come from
`tools/Import-CraftingResources.ps1 -SourceDirectory data\epic7`: 20 percentages at that epic, and the
test row `enhance_id 100` (`fail_result = '-'`) is left out. Datetimes in an `.rdu` are OLE `DATE`
doubles. `pg_dump` Arcadia before re-running: the import cannot be undone row by row.

**The 9.4-only rows are then removed** (`tools/prune_to_client73.py`, `filtre-ressources-73.md`): the 7.3 set is the
client's own table where it has one (`tools/client73_ids.py` reads `db_item`, `db_skill`, `db_monster`, `db_npcresource`,
`db_creature`, `db_quest`), the Epic 7 dump for states and stats. A row something still points at stays (Telecaster, a
kept row, a catalogue, the Lua, a literal of the code). The counts above are before it: 9 908 rows went, leaving
`ItemResources` 28 254, `SkillResources` 2 171, `MonsterResources` 6 581, `NpcResources` 1 195, `StateResources` 1 579,
`StatResources` 920.

## Logging

Serilog is configured in `DevConsole/appsettings.json`, which is **local and not tracked** (it carries the
database credentials). Its console and file sinks must sit inside an `Async` sink
(`Serilog.Sinks.Async`): the Windows console is slow and writes synchronously, and at `Debug` every sent
and received packet is a line, so a synchronous console stalled the network and tick threads that logged.
Keep `System` at `Warning`. A log call with more than three properties allocates an `object[]` and boxes
its arguments **before** Serilog checks the level, so a per-packet one is wrapped in
`_logger.IsEnabled(LogEventLevel.Debug)` (`GameClient.SendMessage` and the receive loop do).

## Load test (`tools/LoadTest`, `docs/packet-specs/test-de-charge.md`)

Headless bots play the real 7.3 protocol (auth, one-time key, lobby, creation, world entry, walk, local chat, combat,
resurrection) through the server's own `CipherConnection` used client-side. `seed` creates `load001…` in the auth
database; `run --stages 50,100 --hold 120` writes a Markdown report. Attached to a local `DevConsole` over EventPipe,
it also reads CPU, runtime counters, lock contention (with durations) and the **`Navislamia` meter**
(`Game/Network/ServerMetrics.cs`): one tick per periodic loop (`ServerMetrics.Tick("name")`), synchronous handling
per received frame (`FrameTimer` in `GameClient.OnDataReceived`), visibility passes and the queue-to-socket send
delay. Instruments are disabled without a listener; **a new periodic loop wraps its tick the same way** and gets its
interval in `Report.LoopBudgetMs`.

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

## Renommage des registres (2026-10-05, uscT2HaQ)

`/change_name` sauvegarde les noms vendeur/meneur avec le personnage, puis rafraîchit groupe,
guilde et enchères. Ordre des verrous : enchères puis CharacterGate ; ne jamais faire l'inverse.
`@GUILD CHANGE_NAME` est officiel (`GuildManager.cpp:885`) ; `@ALLIANCE GLEADER_CHANGE`
pour un chef renommé est une adaptation du format `GameMessage.cpp:4767`, documentée dans la fiche.

## Équipes d'attaque persistées (2026-10-05)

`PartyService` stocke 0/1/2 ; `Parties.LeadPartyId` vaut l'id principal pour les équipes.
Au démarrage, charger les groupes puis `IGuildService.LoadAsync()` avant le réseau ; le registre
`_teams` et les guildes doivent revenir ensemble. Fiche : `socle-equipes-attaque-persistance.md`.
Pour les dialogues de siège, les libellés viennent du Lua officiel ; une entrée sans identifiant
établi est omise et consignée NON ÉTABLI (`socle-donjons-instances-secrets.md`, xHHwc9Z2).


### Filtre 7.3 : références sémantiques et chaînes (2026-10-05, RlwjZDsY)

Ne jamais protéger une famille de ressources par tous les nombres du code ou du Lua.
`tools/resource_reachability.py` suit les points d’entrée publiés et arguments typés ;
`docs/packet-specs/audit-litteraux-73.md` cite les 134 décisions et leurs sources.
`StringResources` est borné aux codes déclarés de `db_string.rdb`, sans exception hors client.
Les FK facultatives vers des chaînes supprimées sont mises à NULL dans la transaction ;
une FK obligatoire inconnue bloque le filtre. Sauvegarde Arcadia puis restauration vérifiée
avant un nouveau nettoyage des données. Le plan reste en lecture seule.


### Import Epic 7 et filtre obligatoire (2026-10-05, lbQQRm8S)

`tools/import_epic7.py` valide les fichiers client avant toute écriture puis rejoue le
filtre 7.3 après résolution des FK, même pour un import partiel. Fournir `--client-dir`
ou `NAVIS_CLIENT73`. `--plan` reste intégralement en lecture seule ; son filtre décrit
la base actuelle. Ne pas ajouter d’option permettant un import réussi sans filtrage.


### Maintenance PostgreSQL livrée, installation explicite (2026-10-05, CkEncmJM)

`tools/Backup-PostgreSql.ps1` sauvegarde Arcadia/Telecaster/auth en custom, lit le catalogue,
publie l’archive et SHA256, puis applique 14 jours de rotation après succès des trois bases.
Secrets uniquement par environnement de processus, restauré en finally. L’installateur
`tools/Install-PostgreSqlMaintenance.ps1` exige une exécution administrateur explicite
(service Automatic, tâche SYSTEM quotidienne à 02:30) ; sa livraison ne vaut pas installation.
Les deux scripts acceptent -WhatIf. Ne jamais modifier automatiquement la configuration
machine au titre d’une tâche qui demande uniquement de livrer ces scripts.
