# Socle — visibilité entre joueurs : entrée, sortie et déplacement des autres joueurs

Carte Trello `SIUdjpbc` (cycle « visibilité entre joueurs »), tâche kanban parente `t_28cf3d23`,
tâche dev `t_0d739bdc`.

| | |
|---|---|
| Branche | `hermes/packet-socle-visibilite-joueurs` |
| Base | `master` `3021f68` (`3021f681e98544112ce508b11ce1df305b3a0dd9`) |
| Fiche | `docs/packet-specs/socle-visibilite-joueurs.md` (ce document) |
| Références épinglées | rzu `87c1e83bf84efe29bb6405e8e6da80349712f3fa` ; NGemity/Chihiro `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (compilé `EPIC_4_1_1`, `shared/Common/Define.h:25`) |
| Client de référence | `reference/client73/SFrame.exe`, `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (dossier non git ; jamais exécuté, lecture statique seule) |
| Paquets | `TM_SC_ENTER` (3), `TM_SC_LEAVE` (9), `TM_SC_MOVE` (8) — **aucun identifiant nouveau** |

## Réponses courtes

1. **Aucun paquet nouveau.** Les trois trames du socle existent déjà et sont déjà émises par le
   serveur : `TS_SC_ENTER` variante joueur (118 octets) au client qui entre
   (`Actions/GameActions.cs:178-210`), `TS_SC_MOVE` en écho au marcheur (`GameClient.cs:141-201`).
   Le socle n'ajoute ni id, ni membre d'énumération, ni constructeur : il **diffuse ce qui existe
   déjà** à des destinataires qui n'en reçoivent rien aujourd'hui.
2. **La variante joueur est complète.** La charge utile `PLAYER_INFO` de 7.3 fait **92 octets** et
   `TS_SC_ENTER_PLAYER` (le `struct` du dépôt) la porte déjà **champ pour champ** : 26 + 92 = **118
   octets**, ce que la fiche vérifie deux fois (dérivation rzu §3.2, allocation de l'objet-évènement
   du client §3.6, plus le test existant `Tests/Game/PkModeStatusTests.cs:21` `PlayerEnterSize = 118`).
   **Rien à ajouter à la trame pour qu'un autre joueur la voie** — c'est le résultat principal de
   cette fiche, et c'est la réponse à la question laissée ouverte par `CLAUDE.md:199-200` («
   `TS_SC_ENTER` carries the same appearance for other players ») : la variante joueur porte déjà le
   sexe, le visage (`faceId`, `faceTextureId`), les cheveux (`hairId`, couleur) et le masque
   d'équipement d'un pair. Seuls les **objets portés** (`TS_SC_WEAR_INFO`) restent hors lot (§5.5).
3. **Ce qui manque est purement serveur** : un **index des présences** (grille mutable, comparable à
   `SpatialIndex<T>` mais non figée), une **résolution handle → session en O(1)**, la **diffusion
   réciproque** d'entrée/sortie entre deux clients, et le câblage aux cinq déclencheurs (§5.3).
4. **Version tranchée** : id `3` (et non `1003`), **`layer` @24 avant `objType` @25** (l'ordre
   `>= EPIC_9_6_7` est différent), `faceTextureId` présent (`>= EPIC_6_3`), `hairColorIndex` /
   `hairColorRGB` / `hideEquipFlag` présents (`>= EPIC_7_1`), `title_code` / `back_board` **absents**,
   `name` sur 19 octets. Détail et sources au §4.
5. **Ids strictement S→C ⇒ aucun bras de réception** (§5.6) : `rzu` les déclare
   `SessionPacketOrigin::Server` et le client ne construit jamais ces trames. Le critère transversal 4
   n'est pas déclenché par ce lot, qui n'ajoute **aucun** membre à `GamePackets`.
6. **Découpage** : le lot livrable seul est l'index + le registre + le service de diffusion + le
   câblage + les tests d'offsets (§9). Restent **hors lot**, nommés et motivés : l'équipement des
   autres joueurs (`TS_SC_WEAR_INFO`, §5.5), les états (mort, siège, mode PK) des autres joueurs, et
   toute cadence de re-diffusion périodique.
7. **Réserves à trancher par Killian** : la vérification en jeu à deux clients (§7.1), la valeur de
   `is_first_enter` diffusée (§7.2), le fait que le handler `TS_SC_MOVE` du client 7.3 ne lit pas
   `start_time` @7 (§7.3), la fenêtre circulaire 540 contre la fenêtre carrée 7×7 régions du client
   (§7.4), et la position de la garde de taille de `TM_CS_REGION_UPDATE` (§7.5).

## 1. Identité

| id | nom (`op_codes.md`) | rôle dans le socle | rzu | `GamePackets` |
|---|---|---|---|---|
| **3** | `TM_SC_ENTER` | faire apparaître un joueur chez un observateur | `TS_SC_ENTER.h:167-171` (`X(3, version < EPIC_9_6_3)`) | `Enums/GamePackets.cs:9` |
| **8** | `TM_SC_MOVE` | faire marcher ce joueur chez l'observateur | `TS_SC_MOVE.h:22-26` (`X(8, version < EPIC_9_6_3)`) | `Enums/GamePackets.cs:13` |
| **9** | `TM_SC_LEAVE` | le retirer de la vue de l'observateur | `TS_SC_LEAVE.h:10-14` (`X(9, version < EPIC_9_6_3)`) | `Enums/GamePackets.cs:14` |

Les trois sont des paquets **serveur → client** (`CREATE_PACKET_VER_ID(..., SessionPacketOrigin::Server)` :
`TS_SC_ENTER.h:171`, `TS_SC_MOVE.h:26`, `TS_SC_LEAVE.h:14`). Les trames **client → serveur** qui les
déclenchent sont `TM_CS_MOVE_REQUEST` (5, `GamePackets.cs:11`) et `TM_CS_REGION_UPDATE` (7,
`GamePackets.cs:12`) : deux bras existent déjà dans la boucle de réception
(`GameClient.cs:1440-1444` et `1446-1450`) et **le socle ne les modifie pas**.

`op_codes.md` nomme aussi `[14] = "TM_SC_FORCE_MOVE"` : cet id n'a **aucune** structure dans
`reference/rzu/librzu/src/packets/GameClient/` (fichier absent) et **aucun** handler dans NGemity
(`grep -rn FORCE_MOVE` sur `shared/Server/Packets/GameClient/` et `Chihiro/src/` : 0 occurrence). Rien
ne le sanctionne : il n'est **pas** utilisé par ce lot (§7.6).

## 2. Ce que le joueur fait — et ce que le client envoie vraiment

Le socle ne fait **jamais** envoyer de paquet nouveau au joueur : les trois trames sont des
conséquences serveur de gestes déjà gérés.

### 2.1 Entrer dans le monde (et donc devenir visible)

Le joueur se connecte, choisit un personnage, et le serveur décide : `GameActions.OnLogin` envoie
`TS_SC_LOGIN_RESULT` (4) puis le `TS_SC_ENTER` du joueur **à ce joueur seul**
(`Actions/GameActions.cs:176` et `:210`), puis tout le bootstrap 7.3
(`docs/character-bootstrap.md`). Le client ne demande rien : **il n'existe aucun paquet « je suis
entré »**. La conséquence pour le socle : l'inscription du joueur dans l'index est une décision
serveur, prise **après** le bootstrap (§5.3, point 1), et il n'y a pas de paquet à lire.

### 2.2 Marcher — `TM_CS_MOVE_REQUEST` (5)

Déjà reçu et traité (`GameClient.cs:1440-1442` → `HandleMoveRequest`, `:141-201`) : le client envoie
`handle` @7, `x` @11, `y` @15, `cur_time` @19, `speed_sync` @23, `count` @24, puis `count` couples
`(tx, ty)` de 8 octets — soit **26 + 8 × N** (rzu `TS_CS_MOVE_REQUEST.h:14-22` ; garde de taille
`MoveRequestFixedLength = 26`, `GameClient.cs:139`). Aujourd'hui le serveur **n'écho** la trame qu'au
marcheur (`GameClient.cs:166-179`) : c'est exactement la trame à diffuser aux observateurs (§5.3,
point 2).

Le `handle` de la requête **n'est pas validé** contre l'identité du client (`GameClient.cs:155` lit
le champ, `:168` le recopie à l'identique) : un client peut donc aujourd'hui désigner un autre
handle. NGemity, lui, compare (`WorldSession.cpp:350`) et traite la différence comme un ordre de familier. Conséquence pour
le socle : **la diffusion doit filtrer sur le handle de l'émetteur**, jamais sur le handle reçu
(§5.3, point 2, et §7.7).

### 2.3 Franchir une frontière de région — `TM_CS_REGION_UPDATE` (7)

Le client l'envoie **en se déplaçant** : c'est le flux de position du client, et sa trame d'id 7 de 24
octets est attestée comme émise par le client 7.3 parmi les paquets de la marche
(`docs/packet-specs/57-check-illegal-user.md:156-159`). Son dernier octet est le drapeau d'arrêt
(`bIsStopMessage`, nom rzu ; NGemity le lit, `WorldSession.cpp:682-690`) :
`update_time` @7, `x` @11, `y` @15, `z` @19, `bIsStopMessage` @23 — **24 octets** (rzu
`TS_CS_REGION_UPDATE.h:8-12`) — id **7** pour 7.3 (`X(7, version < EPIC_9_2)`, `:14-17`). Le bras
existe (`GameClient.cs:1446-1450` →
`HandleRegionUpdate`, `:203-221`) et met à jour `X`, `Y`, `Z` puis re-synchronise la visibilité :
c'est le second déclencheur naturel du socle, et le seul endroit où `bIsStopMessage` vit (§7.5).

### 2.4 Déplacement imposé (warp) et sortie

Aucun paquet joueur : le serveur décide. `WarpService.Warp` (`Game/Services/WarpService.cs:34-68`)
envoie déjà `TS_SC_WARP` (12) et **retire tous les objets visibles** avant de re-synchroniser
(`LeaveEverything`, `:74-99`) ; `ReturnToLobbyAsync` (`GameClient.cs:870-895`) et `OnDisconnect`
(`:851-868`) ferment la session. Ces trois chemins sont les points de sortie du socle (§5.3,
points 4 et 5).

## 3. Structure sur le fil

### 3.1 En-tête commun (7 octets)

| offset | taille | type | nom | source |
|---|---|---|---|---|
| 0 | 4 | `uint32` | `length` | `Packet<T>` (`Game/Network/Packets/Packet.cs`) ; conventions du dépôt |
| 4 | 2 | `uint16` | `id` | idem |
| 6 | 1 | `uint8` | `checksum` | somme des octets 0…5, tronquée à 8 bits (`GameMovePackets.WriteChecksum`, `GameMovePackets.cs:75-84`) |

### 3.2 `TS_SC_ENTER` variante joueur (`TM_SC_ENTER`, 3) — **118 octets**

`type` vaut `ET_Player = 0` (`TS_SC_ENTER.h:24`), `objType` vaut `EOT_Player = 0`
(`TS_SC_ENTER.h:13`) : c'est la variante que `TS_SC_ENTER_PLAYER` du dépôt écrit déjà.

| offset | taille | type | nom | valeur | source |
|---|---|---|---|---|---|
| 7 | 1 | `uint8` | `type` | `0` (`ET_Player`) | rzu `TS_SC_ENTER.h:146` ; client `0x66e6a1` (`movzx edx,BYTE PTR [eax+0x7]`) ; `GameActions.cs:180` |
| 8 | 4 | `uint32` | `handle` | **`character.Id`** | rzu `:152` (`ar_handle_t`) ; client `0x66e695` (`[eax+0x8]`) ; `GameActions.cs:181` |
| 12 | 4 | `float` | `x` | position au fil | rzu `:153` ; client `0x66e698` (`[eax+0xc]`) |
| 16 | 4 | `float` | `y` | position au fil | rzu `:154` ; client `0x66e6a5` (`[eax+0x10]`) |
| 20 | 4 | `float` | `z` | position au fil | rzu `:155` ; client `0x66e6ae` (`[eax+0x14]`) |
| 24 | 1 | `uint8` | `layer` | `character.Layer` | rzu `:156` (`version < EPIC_9_6_7`) ; client `0x66e6b1` (`[eax+0x18]`) ; `GameActions.cs:185` |
| 25 | 1 | `uint8` | `objType` | `0` (`EOT_Player`) | rzu `:157` + `:13` ; client `0x66e6b8` (`[eax+0x19]`) ; `GameActions.cs:186` |
| 26 | 92 | `PLAYER_INFO` | `playerInfo` | §3.3 | rzu `TS_SC_ENTER.h:158` (`objType == EOT_Player`) ; client `0x66e6e9-0x66e6f4` (copie de `0x17` dwords = 92 octets depuis `frame+0x1a`) |
| **118** | | | **taille totale** | | allocation client `push 0x82` = 130 = 12 + 118 (`0x66e6cc`), §3.6 |

L'ordre **`layer` puis `objType` après `z`** n'est pas un détail : `>= EPIC_9_6_7`, rzu place les deux
**avant** `handle` (`:147-151`). Pour 7.3 c'est bien `z` @20, `layer` @24, `objType` @25, charge @26.

### 3.3 `TS_SC_ENTER__PLAYER_INFO` — **92 octets** (offsets absolus dans la trame)

| offset | taille | type | nom | source rzu | source client | `TS_SC_ENTER_PLAYER` |
|---|---|---|---|---|---|---|
| 26 | 4 | `uint32` | `creatureInfo.status` | `:68` | offset 26 = `CreatureStatus.cs:5` | `Status` |
| 30 | 4 | `float` | `creatureInfo.face_direction` | `:69` | — | `FaceDirection` |
| 34 | 4 | `int32` | `creatureInfo.hp` | `:70` | — | `Hp` |
| 38 | 4 | `int32` | `creatureInfo.max_hp` | `:71` | — | `MaxHp` |
| 42 | 4 | `int32` | `creatureInfo.mp` | `:72` | — | `Mp` |
| 46 | 4 | `int32` | `creatureInfo.max_mp` | `:73` | — | `MaxMp` |
| 50 | 4 | `int32` | `creatureInfo.level` | `:74` | — | `Level` |
| 54 | 1 | `uint8` | `creatureInfo.race` | `:75` (`< EPIC_9_6_7`) | `0x66e705` (`[eax+0x36]`, table `race-3`) | `Race` |
| 55 | 4 | `uint32` | `creatureInfo.skin_color` | `:76` (`>= EPIC_4_1 && < EPIC_9_6_7`) | — | `SkinColor` |
| 59 | 1 | `bool` | `creatureInfo.is_first_enter` | `:77` (`< EPIC_9_6_7`) | copié sans branchement (§7.2) | `IsFirstEnter` |
| 60 | 4 | `int32` | `creatureInfo.energy` | `:78` (`>= EPIC_4_1 && < EPIC_9_6_7`) | — | `Energy` |
| 64 | 1 | `uint8` | `sex` | `:113` | `0x66e6f6` (`[eax+0x40]`, choix du modèle) | `Sex` |
| 65 | 4 | `uint32` | `faceId` | `:114` | — | `FaceId` |
| 69 | 4 | `uint32` | `faceTextureId` | `:115` (`>= EPIC_6_3`) | — | `FaceTextureId` |
| 73 | 4 | `uint32` | `hairId` | `:116` | — | `HairId` |
| 77 | 4 | `uint32` | `hairColorIndex` | `:117` (`>= EPIC_7_1`) | — | `HairColorIndex` |
| 81 | 4 | `uint32` | `hairColorRGB` | `:118` (`>= EPIC_7_1`) | — | `HairColorRGB` |
| 85 | 4 | `uint32` | `hideEquipFlag` | `:119` (`>= EPIC_7_1`) | — | `HideEquipFlag` |
| 89 | 19 | `string` | `name` (NUL compris) | `:120-121` (`19` pour `version < EPIC_9_6`) | — | `Name` (`SizeConst = 19`) |
| 108 | 2 | `uint16` | `job_id` | `:123` | — | `JobId` |
| 110 | 4 | `uint32` | `ride_handle` | `:124` | — | `RideHandle` |
| 114 | 4 | `uint32` | `guild_id` | `:125` | — | `GuildId` |
| **118** | | | **fin de la trame** | | | |

La somme est exacte : `creatureInfo` = 4 + 4 + 4×6 + 1 + 4 + 1 + 4 = **38** (le « motif créature de
38 octets » déjà écrit par `TS_SC_ENTER` pour les PNJ, monstres, invocations et familiers), puis
25 (`sex` → `hideEquipFlag`) + **19** (`name`) + 10 (`job_id`, `ride_handle`, `guild_id`) = 38 + 25 +
19 + 10 = **92**. `26 + 92 = 118`.

Le `struct` du dépôt `Game/Network/Packets/Game/TS_SC_ENTER_PLAYER.cs:8-41` déclare **exactement**
cette suite, dans cet ordre, en `Pack = 1` : sa taille est donc **111 octets de charge** + 7
d'en-tête = 118. Il n'y a **aucun champ à ajouter** pour la diffusion à un tiers.

### 3.4 `TS_SC_LEAVE` (`TM_SC_LEAVE`, 9) — **11 octets**

| offset | taille | type | nom | source |
|---|---|---|---|---|
| 7 | 4 | `uint32` | `handle` | rzu `TS_SC_LEAVE.h:8` (seul champ) ; client `0x66e62e` (`mov edx,[ecx+0x7]`), allocation `push 0x17` = 23 = 12 + 11 (`0x66e5f4`) |
| **11** | | | **taille totale** | |

Le dépôt a déjà le constructeur : `GameSpawnPackets.BuildLeave(handle)`, utilisé par
`WorldObjectStreamer.cs:90` et par `WarpService.cs:95`. **Le socle le réutilise tel quel**, avec le
handle partagé du joueur qui sort (§5.3).

### 3.5 `TS_SC_MOVE` (`TM_SC_MOVE`, 8) — **19 + 8 × N octets**

| offset | taille | type | nom | source |
|---|---|---|---|---|
| 7 | 4 | `ar_time_t` | `start_time` | rzu `TS_SC_MOVE.h:15` ; écrit par `GameMovePackets.CreateMove` à `:66` |
| 11 | 4 | `uint32` | `handle` | rzu `:16` ; client `0x673898` (`mov ecx,[ebx+0xb]`) |
| 15 | 1 | `int8` | `tlayer` | rzu `:17` ; `GameMovePackets.cs:68` (écrit `0` sur le chemin d'écho `GameClient.cs:169`) |
| 16 | 1 | `uint8` | `speed` | rzu `:18` ; client `0x6738a3` (`[ebx+0x10]`) ; `ConnectionInfo.EchoedMoveSpeed` = 100 |
| 17 | 2 | `uint16` | `count` | rzu `:19` ; client `0x6738ac` (`[ebx+0x11]`) |
| 19 | 8 × N | `MOVE_INFO[]` | `tx` @19, `ty` @23, pas de 8 | rzu `:7-20` ; client `0x6738b9` (`fld [eax+ebx+0x13]`) et `0x6738c3` (`+0x17`) |
| **19 + 8 × N** | | | **taille totale** | |

`ar_time_t` et `ar_handle_t` sont des `uint32_t` (`lib/Packet/GameTypes.h:40,44`) ; l'unité est le
**tick de 10 ms** (`Game/Services/ServerClock.cs:9-15`). `GameMovePackets.BuildMove`
(`GameMovePackets.cs:39-49`) écrit **N = 1** (le point d'arrivée), `BuildStopMove` (`:51-56`) écrit
**N = 0** (vitesse 0). Pour diffuser le trajet complet que le marcheur a envoyé, il faut une
surcharge à `N` variable (§9, lot L2) : la trame d'écho actuelle recopie déjà les `N` points du
client (`GameClient.cs:174`).

### 3.6 Contrôle croisé : l'allocation de l'objet-évènement du client

Le handler d'entrée du client alloue l'objet-évènement par `push <taille>; call 0x9767b1` — les huit
branches d'`objType` passent toutes par ce même allocateur (`0x66e6d1`, `0x66e755`, `0x66e78a`,
`0x66e7bd`, `0x66e7f2`, `0x66e827`, `0x66e85c`, `0x66e87c`) ; le handler de sortie, lui, appelle
`0x97671b` (`0x66e5f8`). La taille allouée vaut **12 (en-tête d'évènement) + la longueur de la
trame** : la charge de la trame est recopiée à `objet+0x0c` et le champ 1 de chaque trame se
retrouve donc en `objet+0x13`.

| `objType` (`EOT_*`, `TS_SC_ENTER.h:13-20`) | allocation client (`push <taille>`) | trame déduite | trame déjà établie par le dépôt |
|---|---|---|---|
| 0 joueur | `0x82` = 130 (`0x66e6cc`) | **118** | `Tests/Game/PkModeStatusTests.cs:21` = 118 |
| 1 PNJ | `0x54` = 84 (`0x66e753`) | 72 | `CLAUDE.md:222-224` = 72 |
| 2 objet | `0x52` = 82 (`0x66e788`) | 70 | `CLAUDE.md:386` = 70 |
| 3 monstre | `0x55` = 85 (`0x66e7bb`) | 73 | `CLAUDE.md:246` = 73 |
| 4 invocation | `0x6c` = 108 (`0x66e7f0`) | 96 | `CLAUDE.md:154-155` = 96 |
| 5 compétence (`EOT_Skill`) | `0x32` = 50 (`0x66e825`) | 38 | — (aucune variante jouée par le socle) |
| 6 objet de terrain | `0x4b` = 75 (`0x66e85a`) | 63 | `CLAUDE.md:968` = 63 |
| 7 familier (`EOT_Pet`) | `0x6b` = 107 (`0x66e87a`) | 95 | — (§ hors lot) |

Les huit `objType` du client y sont couverts, et **six tailles sur six concordent** avec les valeurs
déjà établies **et vérifiées en jeu** par le dépôt (les deux autres — `EOT_Skill` et `EOT_Pet` —
n'ont pas de variante dans le dépôt, le socle ne les émet pas) :
c'est ce qui valide la lecture du handler et, en particulier, les **118 octets** de la variante
joueur et le **début de charge à 26** (le client recopie 23 `dwords` = 92 octets depuis
`frame+0x1a`, `0x66e6e9-0x66e6f4`).

## 4. Gating de version — tranché pour Epic 7.3

Toutes les conditions de la famille, lues dans `reference/rzu/librzu/src/packets/GameClient/`.

| élément | gating rzu | décision 7.3 | preuve |
|---|---|---|---|
| id de `TS_SC_ENTER` | `X(3, version < EPIC_9_6_3)` / `X(1003, …)` (`:167-169`) | **3** | `GamePackets.cs:9` ; `op_codes.md:6` |
| id de `TS_SC_MOVE` | `X(8, version < EPIC_9_6_3)` / `X(1008, …)` (`:22-24`) | **8** | `GamePackets.cs:13` ; `op_codes.md:11` |
| id de `TS_SC_LEAVE` | `X(9, version < EPIC_9_6_3)` / `X(1009, …)` (`:10-12`) | **9** | `GamePackets.cs:14` ; `op_codes.md:12` |
| id de `TM_CS_MOVE_REQUEST` (trame entrante du socle) | `X(5, version < EPIC_9_2)` / `X(65, …)` / `X(63, …)` / `X(1063, …)` (`TS_CS_MOVE_REQUEST.h:24-28`) | **5** | `GamePackets.cs:11` ; `op_codes.md:8` |
| id de `TM_CS_REGION_UPDATE` (trame entrante du socle) | `X(7, version < EPIC_9_2)` / `X(67, …)` / `X(1067, …)` (`TS_CS_REGION_UPDATE.h:14-17`) | **7** | `GamePackets.cs:12` ; `op_codes.md:10` |
| champs conditionnels de ces deux trames | aucun `version …` dans leurs `_DEF` (`TS_CS_MOVE_REQUEST.h:14-22`, `TS_CS_REGION_UPDATE.h:7-12`) | **la lecture du dépôt s'applique telle quelle** (26 et 24 octets, §2.2 et §2.3) | — |
| `objType` / `layer` **avant** `handle` | `version >= EPIC_9_6_7` (`:147-151`) | **non** : c'est l'ordre bas qui s'applique | `:156-157` (`version < EPIC_9_6_7`) confirmé par le client (`layer` @24, `objType` @25, §3.2) |
| `creatureInfo.race` en `uint8` | `version < EPIC_9_6_7` (`:75`) | **oui**, 1 octet @54 | le client lit un octet en `[frame+0x36]` = 54 (`0x66e705`) |
| `creatureInfo.skin_color` | `>= EPIC_4_1 && < EPIC_9_6_7` (`:76`) | **oui**, `uint32` @55 | 7.3 `0x070300` est entre les deux bornes |
| `creatureInfo.is_first_enter` | `version < EPIC_9_6_7` (`:77`) | **oui**, 1 octet @59 | 38 octets de `creatureInfo` confirmés par l'allocation client |
| `creatureInfo.energy` | `>= EPIC_4_1 && < EPIC_9_6_7` (`:78`) | **oui**, `int32` @60 | idem |
| `PLAYER_INFO.race` / `.skin_color` (doublons) | `version >= EPIC_9_6_7` (`:111-112`) | **absents** | sinon la charge ferait 100 octets, pas 92 |
| `PLAYER_INFO.faceTextureId` | `>= EPIC_6_3` (`:115`) | **présent**, @69 | `EPIC_6_3` antérieur à 7.3 |
| `PLAYER_INFO.hairColorIndex`, `.hairColorRGB`, `.hideEquipFlag` | `>= EPIC_7_1` (`:117-119`) | **présents**, @77, @81, @85 | `EPIC_7_1` (`0x070100`) ≤ `0x070300` |
| `PLAYER_INFO.name` | `19` pour `version < EPIC_9_6` (`:120-121`) | **19 octets** @89 | `TS_SC_ENTER_PLAYER.cs:36` (`SizeConst = 19`) |
| `PLAYER_INFO.title_code` | `>= EPIC_8_1` (`:126`) | **absent** | postérieur à 7.3 |
| `PLAYER_INFO.back_board` | `>= EPIC_9_3` (`:127`) | **absent** | postérieur à 7.3 |
| `PLAYER_INFO.energy` (doublon) | `< EPIC_4_1 \|\| >= EPIC_9_6_7` (`:128`) | **absent** | 7.3 est `>= EPIC_4_1` |
| `TS_SC_MOVE` : `tlayer` signé, `speed` octet, `count` `uint16` | aucun gating (`:14-20`) | **tel quel** | `GameMovePackets.cs:58-73` |

Décision de méthode : les trois ids du socle sont **déjà** dans `GamePackets` et **déjà** émis par le
dépôt ; le socle n'ajoute donc **aucun** membre et le critère « énumération et dispatch ensemble »
n'est pas déclenché (§5.6).

## 5. Traitement attendu

### 5.1 Ce que NGemity fait (et ce qu'il en faut)

| étape NGemity | source | ce qu'il en faut pour NavisLamia |
|---|---|---|
| Un objet qui entre dans le monde est placé dans la région et ses voisins sont visités | `World::AddObjectToWorld`, `World.cpp:304-317` ; `World::enterProc`, `:287-303` | la même chose, mais l'unité de découpage est ici la **fenêtre de 540 unités** et non une région |
| Entrée **réciproque** : la visite envoie l'`ENTER` de l'objet à chaque client de la région **et**, si l'objet est un joueur, l'`ENTER` de ce client à l'objet | `SendEnterMessageEachOtherFunctor::Run`, `Functors.cpp:25-35` | **la règle centrale à porter** : A voit B ⟺ B voit A |
| Un objet en mouvement est présenté à sa **position courante interpolée**, et suivi immédiatement d'un `MOVE` | `WorldObject::SendEnterMsg`, `Object.cpp:371-412` (`GetCurrentPosition(sWorld.GetArTime())` puis `Messages::SendMoveMessage` si `bIsMoving`, `Messages.cpp:393-406`) | un pair qui marche déjà doit être vu **en marche** : `ENTER` puis `MOVE` |
| Le joueur qui entre reçoit en plus l'`ENTER` des objets **mobiles et statiques** de la région | `AddObjectFunctor::Run`, `Functors.cpp:47-61` (`MovableVisitor \| StaticVisitor`) | déjà fait par `WorldObjectStreamer` pour les PNJ/monstres/objets de terrain (`CLAUDE.md:123-128`) |
| La sortie d'un objet du monde envoie `LEAVE` aux clients qui le voyaient | `World::RemoveObjectFromWorld`, `World.cpp:330-345` | à porter pour un joueur qui se déconnecte ou retourne au lobby |
| Un franchissement de région recalcule l'entrée/sortie (`Run2` = « nouvelles régions seulement ») | `World::onRegionChange`, `World.cpp:319-328` ; `AddObjectFunctor::Run2`, `Functors.cpp:63-79` | à porter sur la frontière de fenêtre (déclencheur 3, §2.3) |
| La marche est diffusée aux clients de la région visible | `World::SetMultipleMove`, `World.cpp:208-241` (functor `SetMoveFunctor`, `Functors.cpp:81-85`) | à porter : diffusion aux clients **dans la fenêtre du marcheur** |
| L'`ENTER` d'un joueur à un observateur est **suivi de son `TS_SC_WEAR_INFO`** | `Object.cpp:409-411` → `Messages::SendWearInfo`, `Messages.cpp:429-445` | **hors lot** (§5.5) |
| Le déplacement reçu du client est validé (possession du handle, vitesse, points) avant diffusion | `WorldSession::onMoveRequest`, `WorldSession.cpp:341-440` | à porter au minimum pour le handle (§2.2) ; le reste (vitesse, points) est déjà borné côté NavisLamia |

Deux écarts NGemity sont **assumés** et développés au §6 : le temps des `MOVE` (NGemity envoie son
tick brut, NavisLamia corrige par client) et le `LEAVE` (NGemity n'en envoie **jamais** quand un
joueur sort simplement de la fenêtre d'un autre : `grep -rn TS_SC_LEAVE` sur `Chihiro/src` ne rend
que `World.cpp:333`).

### 5.2 Les deux briques réutilisables (livrable nommé, pas enfoui)

1. **Index des présences (grille mutable)** — `SpatialIndex<T>` est **immuable** : construit depuis
   un `IEnumerable<T>`, sans `Add`, `Remove` ni `Move`, et son `Count` est en lecture seule
   (`Game/Services/SpatialIndex.cs:11-37`). Il sert trois services (`NpcSpawnService.cs:30`,
   `FieldPropService.cs:25`, `MonsterWorldState.cs:654`) et **ne doit pas** être
   modifié : la fiche demande une **grille mutable distincte**, sur le même modèle (cellules de
   côté `WorldVisibility.ViewRange` = 540, requête par voisinage puis filtre euclidien,
   `SpatialIndex.cs:39-74`), avec en plus `Add` / `Move` / `Remove` et **la couche dans la clé de
   cellule** (deux joueurs de couches différentes ne doivent pas se voir : NGemity indexe par
   `GetRegion(rx, ry, layer)`, `RegionContainer.h:53-54`, et `TS_SC_ENTER` comme `TS_SC_MOVE`
   portent la couche).
2. **Résolution handle → session en O(1)** — aujourd'hui les étals la cherchent par **balayage de
   toutes les sessions** (`BoothWatchService.TryFindBoothOwner`, `Game/Services/BoothWatchService.cs:155-175`,
   appelé en `:74` ; le commentaire `:149-153` dit explicitement que c'est « la primitive
   manquante » et « le seul identifiant que le dépôt peut construire sans nouveau registre »). Le
   registre du socle **est** ce registre : `ConcurrentDictionary<uint handle, GameClient>`, alimenté
   à l'entrée en jeu et vidé à la sortie, sur le modèle de `NetworkService.AuthorizedGameClients`
   (déjà un `ConcurrentDictionary`, `CLAUDE.md:417-418`). Il rend en plus possible l'action d'un
   client sur un pair (duel, étal, échange) sans balayage.

Les deux briques vivent dans des fichiers **neufs** et sont utilisables sans le service de diffusion
(§9 : L1 est livrable et testable seul).

### 5.3 Les cinq déclencheurs, et ce que le serveur émet

| # | déclencheur (dépôt) | ce que le serveur émet | taille |
|---|---|---|---|
| 1 | **Entrée en jeu** — fin du bootstrap de `GameActions.OnLogin` (`Actions/GameActions.cs:178-260`) | inscription dans le registre **et** dans l'index (position `result.X/Y/Z`, couche `character.Layer`) ; puis, pour chaque pair déjà présent dans la fenêtre : `ENTER` du pair → au nouveau venu **et** `ENTER` du nouveau venu → au pair | 118 par trame |
| 2 | **Marche** — `HandleMoveRequest` (`GameClient.cs:141-201`), après la mise à jour de `X`/`Y` et de `Destination` (`:180-197`) | déplacement de l'entrée dans l'index, re-synchronisation de la visibilité (comme `:199`), puis `MOVE` du marcheur à **chaque client qui le voit**, avec `start_time = ServerClock.Now + pair.ClientClockOffset`, `handle` = **handle d'émetteur** (jamais le handle reçu, §2.2), `tlayer` = couche du marcheur, `speed` = 100 (`EchoedMoveSpeed`), et les `N` points reçus | 19 + 8 × N |
| 3 | **Frontière de région** — `HandleRegionUpdate` (`GameClient.cs:203-221`) | mise à jour de l'entrée dans l'index + re-synchronisation de la visibilité ; **aucune** trame `MOVE` n'est émise (`World::onRegionChange` n'en émet pas non plus) ; le drapeau `bIsStopMessage` @23 est **ignoré** dans ce lot (§7.5) | — |
| 4 | **Warp** — `WarpService.Warp` (`Game/Services/WarpService.cs:34-68`) | sortie de l'ancienne place pour **tous** les observateurs (`LEAVE` réciproque), puis réinscription à la nouvelle place et réentrée réciproque ; `LeaveEverything` (`:74-99`) est l'endroit où la passe des joueurs se branche | 11, puis 118 |
| 5 | **Sortie** — `ReturnToLobbyAsync` (`GameClient.cs:870-895`) et `OnDisconnect` (`:851-868`) | retrait du registre et de l'index, `LEAVE` du partant à **chaque** observateur, purge de la vue `SpawnedPlayers` du partant — `ClearCharacterSession` (`ConnectionInfo.cs:366-400`) et `ClearVisibleObjects` (`:334-353`) sont les deux purges existantes à étendre | 11 par observateur |

La purge de la sortie doit être **avant** la sauvegarde/le nettoyage asynchrone : `OnDisconnect` est
`async void` et `ReturnToLobbyAsync` fait un `SaveProgressSafelyAsync` (`:882`) — un observateur qui
regarderait un joueur qui retourne au lobby pendant la sauvegarde ne doit pas le voir figé.

### 5.4 Réciprocité, verrous et ordre des opérations (le piège du lot)

Trois règles, parce qu'elles évitent le défaut qui rendrait le socle incohérent ou bloquant :

1. **Un seul verrou à la fois.** La synchronisation d'un client A touche aussi les vues des pairs.
   L'ordre à suivre est : `lock (A.PlayerVisibilityLock) { calculer l'ensemble en portée + le delta }`,
   **puis relâcher**, puis pour chaque pair `lock (B.PlayerVisibilityLock) { ajouter/retirer }`. Prendre
   le verrou de A puis celui de B sur un thread et B puis A sur un autre est un interblocage : le
   socle ne doit **jamais** imbriquer deux verrous de visibilité. Les envois se font **hors** de tout
   verrou, sur une liste de trames construite avant (contrairement à `WorldObjectStreamer.Stream`, qui
   envoie sous le verrou du client courant — acceptable là parce qu'un seul verrou y est pris).
2. **La portée est symétrique, donc le delta l'est aussi.** `|A−P| ≤ 540` et même couche est une
   relation symétrique : pour chaque sync de A on calcule l'ensemble en portée, puis on **réconcilie
   les deux côtés** contre cet ensemble (entrée manquante côté pair → `ENTER` de A au pair ;
   présence en trop côté pair → `LEAVE` de A au pair). Sinon un pair immobile garde indéfiniment un
   acteur qui s'est éloigné.
3. **Filtrer soi-même.** `WorldObjectStreamer.Stream` accepte un prédicat `canEnter`
   (`WorldObjectStreamer.cs:38`) : pour les joueurs il porte `handle != soi`, sinon le joueur reçoit
   son propre `ENTER` (le client a déjà son acteur, créé par le bootstrap, `CLAUDE.md:199-200`) et
   son propre `MOVE` en double (l'écho de `HandleMoveRequest` le lui envoie déjà).

### 5.5 Ce que ce lot ne fait **pas** (et pourquoi)

- **L'équipement des autres joueurs** (`TS_SC_WEAR_INFO`, 202). NGemity l'envoie juste après l'`ENTER`
  (`Object.cpp:409-411`, `Messages.cpp:429-445`) : sans lui, un observateur voit le pair **sans son
  équipement**. Il est **hors lot** parce que `GameCharacterPackets.BuildWearInfo`
  (`Game/Network/Packets/Game/GameCharacterPackets.cs:92`) exige une `CharacterEntity` **entière**,
  que le dépôt charge à la demande (`EquipmentService.cs:67,105`) et ne garde **pas** dans la
  session : le socle devrait donc soit lire la base à chaque entrée de pair (chemin chaud : une
  entrée à chaque franchissement de fenêtre), soit mettre l'entité en cache dans la session, ce qui
  le couplerait aux sept chemins d'équipement (portée, retrait, échange, carte, `hide_equip`, dépôt)
  que **six MR ouvertes** touchent déjà (§9). C'est un lot à part, à ouvrir sur décision de Killian
  (§A VERIFIER 3).
- **Les états des autres joueurs** : mort (`TS_SC_STATUS_CHANGE`, 500), siège, mode PK, combat,
  animation. `ActorStatus.ForPlayer(...)` (`Game/Network/Packets/Game/ActorStatus.cs`, appelé
  `GameActions.cs:187`) est figé **au moment de l'entrée** : un pair qui s'assied, meurt ou passe en
  PK après son entrée ne l'est pas pour l'observateur. C'est le prolongement naturel du socle, sur
  sa propre carte.
- **Le familier, les invocations, les étals et les objets au sol des autres joueurs** : ils ont leurs
  cartes (`350/351`, `301/305`, `705/706/707`) et le card dev les exclut explicitement.
- **Toute cadence de re-diffusion périodique** : la visibilité des autres types est **évènementielle**
  (`SyncVisibleObjects`, `GameClient.cs:529-534`, appelée depuis la marche `:199` et le franchissement
  `:221`) ; le socle suit le même modèle et n'ajoute aucun minuteur.

### 5.6 Les ids strictement S→C : argument du critère transversal 4

`TM_SC_ENTER` (3), `TM_SC_MOVE` (8) et `TM_SC_LEAVE` (9) sont `SessionPacketOrigin::Server`
(`TS_SC_ENTER.h:171`, `TS_SC_MOVE.h:26`, `TS_SC_LEAVE.h:14`) : le client ne les construit jamais et
le serveur ne doit pas les recevoir. **Aucun bras de réception n'est ajouté** et **aucun handler
n'est inventé** ; c'est la forme correcte admise par le critère (« un id strictement S→C n'a pas de
bras de réception — le dire, ne pas inventer un handler »).

Ce lot ne déclenche pas l'exigence par ailleurs : il **n'ajoute aucun membre** à `GamePackets` (les
trois ids y sont déjà, `Enums/GamePackets.cs:9,13,14`) et ne modifie pas l'énumération. Le trou
**préexistant** — une trame entrante d'id 3, 8 ou 9 traverse `DefinedPackets` (`GameClient.cs:1400`)
puis atteint le `switch` final qui lève (`:2062-2073`) — n'est **pas** refermé ici : le refermer
demanderait des bras de journalisation sur le modèle de `TM_SC_REGION_ACK` (`:1461-1466`) ou de
`TM_SC_FARM_INFO` (`:2051-2059`), et c'est une décision globale (tous les ids S→C de l'énumération
sont dans ce cas), pas une décision de ce socle (§A VERIFIER 4).

## 6. Écarts assumés avec NGemity, et pourquoi

| point | NGemity | NavisLamia | motif |
|---|---|---|---|
| Version compilée | `EPIC_4_1_1` (`shared/Common/Define.h:25`) | 7.3 (`0x070300`) | son `PLAYER_INFO` fait **76 octets** (38 + `sex` 1 + `faceId` 4 + `hairId` 4 + `name` 19 + `job_id` 2 + `ride_handle` 4 + `guild_id` 4), contre **92** en 7.3 : il lui manque `faceTextureId` (`>= EPIC_6_3`) et les trois champs `>= EPIC_7_1` (`hairColorIndex`, `hairColorRGB`, `hideEquipFlag`), soit 16 octets. **La trame 7.3 ne doit donc pas être reprise de son `struct`.** Sa variante est en revanche identique **d'ordre** (`TS_SC_ENTER.h:86-100` vs rzu `:109-128`), ce qui confirme la séquence des champs. |
| Temps de `MOVE` | envoie son tick brut (`pUnit->lastStepTime`, `Messages.cpp:411`) à **tous** | `start_time = ServerClock.Now + <offset du destinataire>` | le dépôt corrige déjà par client depuis chaque `TS_CS_MOVE_REQUEST` (`GameClient.cs:180`, `CLAUDE.md:416-417`) ; c'est le comportement établi et testé des monstres (`MonsterMovementService.cs:138`). L'écart va dans le sens du client, pas contre lui. |
| `LEAVE` à la sortie de fenêtre | **jamais** : `TS_SC_LEAVE` n'est émis qu'à `RemoveObjectFromWorld` (`World.cpp:330-345`) | émis dès qu'un pair quitte la fenêtre | le dépôt le fait déjà pour les PNJ/monstres/objets (`WorldObjectStreamer.cs:78-105`) et le client garde un acteur connu jusqu'à son `LEAVE` : ne pas l'envoyer laisserait un joueur fantôme. |
| Unité de découpage | régions (conteneurs par couche) | fenêtre de 540 unités, cercle euclidien | `WorldVisibility.cs:5-7` et `CLAUDE.md:130-133, 212-214` : c'est la primitive du dépôt pour les trois autres types, et le client rejette une entrée hors de sa fenêtre (objet **définitivement absent** jusqu'à reconnexion). |
| Entrée d'un joueur | `AddObjectFunctor` visite **toutes** les régions visibles, et `Run2` les **nouvelles** seulement | requête d'index + réconciliation symétrique | même effet, sans la structure de régions que NavisLamia n'a pas. |
| `TS_SC_FORCE_MOVE` (14) | aucune occurrence | aucune occurrence ; structure absente de rzu | rien à porter (ni NGemity ni rzu ne le déclarent) : un champ sans structure est un `NON ÉTABLI`, pas une trame à émettre. |

## 7. `NON ÉTABLI`

Chaque point porte la question exacte à trancher. **Aucun de ces points ne doit être comblé par une
valeur devinée** : là où le dev doit choisir pour avancer, le choix retenu est donné **et** la
réserve est écrite.

### 7.1 Le rendu d'un acteur pair par le client 7.3 (vérification en jeu, deux clients)

La trame est **établie** (« la variante joueur de `TS_SC_ENTER` » : rzu `objType == EOT_Player`,
NGemity `Player::EnterPacket`, et le handler client qui recopie 92 octets au branchement
`objType == 0`, §3.2-3.6). Ce qui n'est **pas** mesurable par lecture statique : que le client
**crée** un acteur pour un handle qu'il n'a pas créé lui-même, et qu'il l'anime. Le socle ne peut pas
le prouver sans lancer le client ; il faut deux clients sur le VPS de Killian. Le test à faire est
explicite : deux personnages se rejoignent à moins de 540 unités, chacun doit voir l'autre
**apparaître, marcher et disparaître** (entrée, `MOVE`, `LEAVE`).

### 7.2 La valeur de `is_first_enter` (offset 59) diffusée à un tiers

Le dépôt envoie `IsFirstEnter = 1` dans l'entrée **locale** (`GameActions.cs:196`), et rien n'a jamais
été envoyé à un tiers. NGemity envoie le drapeau **de l'objet** (`Unit.cpp:119`,
`STATUS_FIRST_ENTER`), donc la **même** valeur à tous les destinataires — ce qui plaide pour `1` aussi
côté pair. Mais le handler du client **recopie** cet octet sans brancher dessus
(`0x66e6e9-0x66e6f6`) : son effet est en aval et non mesuré. **Le socle diffuse `1`** (seule valeur que
le dépôt ait jamais envoyée et que le client ait acceptée), et la question « faut-il `0` pour un pair
déjà connu du client » reste ouverte. Si Killian voit l'apparence du pair se reconstruire à chaque
ré-entrée de fenêtre, c'est ce champ qu'il faut basculer — un test le verrouille.

### 7.3 Le handler `TS_SC_MOVE` du client 7.3 ne lit pas `start_time` (@7)

Mesuré dans `SFrame.exe`, fonction `0x673800` (cible de l'index 4 de la table d'octets `0x67f0a0` /
sauts `0x67f020`, id 8) : les seuls accès à la trame sont `handle` `[ebx+0xb]` (`0x67387a`), `speed`
`[ebx+0x10]` (`0x673880`), `count` `[ebx+0x11]` (`0x673886`, relu `0x6739b6`) et les points
`[eax+ebx+0x13]`/`+0x17` (`0x6738b9`, `0x6738c3`) ; la fenêtre `0x673800-0x673a17` — la fonction rend
`ret 0x4` en `0x673a17` — **ne contient aucune lecture de `+7`** : les emplacements internes de la
marche sont mis à zéro à l'allocation (`0x67385e`, `0x673861`, `0x673864`) et le `start_time` reçu
n'est donc jamais lu. Autrement dit, sur le chemin du destinataire, la marche semble démarrer à l'arrivée et non au tick annoncé — ce qui n'empêche pas le
champ d'être lu plus loin, par le traitement de l'acteur, hors de cette fonction. **Conséquence pour
le socle : la correction par `ClientClockOffset` est conservée** (c'est le comportement établi du
dépôt pour les monstres, `MonsterMovementService.cs:136-141`, et le seul qui reste juste si le client
lit le champ ailleurs), mais la fiche **ne prétend pas** que le client l'utilise. À vérifier en jeu :
un pair doit marcher **sans téléport** au moment où l'observateur le reçoit.

### 7.4 Fenêtre circulaire de 540 unités contre fenêtre carrée 7 × 7 régions

Le dépôt streame dans un **cercle** de rayon 540 (`WorldVisibility.cs:5-7`, `WithinRange` filtre
`dx² + dy² ≤ 540²`, `SpatialIndex.cs:39-74`) alors que la fenêtre du client est celle de ses régions
(7 × 7 régions de 180 = un **carré** de ±540, `GameMovePackets.cs:14-21`). Le cercle est **inclus**
dans le carré : le serveur n'enverra donc jamais d'entrée que le client rejetterait (`CLAUDE.md:212-214`),
mais il n'enverra **pas** les acteurs des coins. C'est le comportement déjà en vigueur pour les PNJ,
monstres et objets de terrain, et le socle le reprend **tel quel** pour rester cohérent ; la question
« faut-il un carré pour les joueurs » reste ouverte et n'a pas de réponse dans les références locales.

### 7.5 La garde de taille de `TM_CS_REGION_UPDATE` et le drapeau d'arrêt

`HandleRegionUpdate` (`GameClient.cs:206`) exige `buffer.Length < 23`, alors que rzu déclare
`update_time` (4) + `x` (4) + `y` (4) + `z` (4) + `bIsStopMessage` (1) = **17 octets de charge, 24
avec l'en-tête** (`TS_CS_REGION_UPDATE.h:8-12`, aucun champ conditionnel ; id **7** pour 7.3, `:14-17`). Deux questions
ouvertes : (a) la garde devrait-elle
être 24 ? (b) faut-il lire `bIsStopMessage` @23, et lui faire émettre un `TS_SC_MOVE` d'arrêt
(`BuildStopMove`, N = 0) aux observateurs ? **Aucune référence ne sanctionne (b)** : NGemity ne
diffuse rien dans `onRegionChange` (`World.cpp:319-328`) et le socle n'émet donc **rien** sur ce
déclencheur (le client interpole jusqu'au dernier point reçu, donc l'observateur s'arrête au même
endroit). Le lot **ne corrige pas la garde** non plus : la corriger sans besoin réel élargirait le
diff pour rien. Les deux questions vont à Killian.

### 7.6 Position d'un pair en cours de chargement

Un client dont le bootstrap n'est pas terminé n'a pas de position fiable (`ConnectionInfo.X/Y/Z`
restent à 0 jusqu'à `OnLogin`) : l'inscrire trop tôt le ferait apparaître à l'origine de la carte
pour tout le monde. **Le socle inscrit le joueur à la fin du bootstrap uniquement** (§5.3 point 1) et
le retire dès `ReturnToLobbyAsync`/`OnDisconnect`. Si Killian veut qu'un joueur en chargement soit
déjà visible (ou, à l'inverse, invisible jusqu'au premier `MOVE_REQUEST`), c'est une politique à
trancher ; elle n'est dans aucune référence locale.

### 7.7 Le handle reçu dans `TM_CS_MOVE_REQUEST` n'est pas validé

`HandleMoveRequest` recopie le `handle` du client (`GameClient.cs:155`, recopié `:168`) et n'écho qu'à lui ; dès que
le socle diffuse, un client peut faire marcher **l'acteur d'un autre** chez les observateurs s'il
envoie le handle d'autrui. Le socle **doit** filtrer (`handle == ConnectionInfo.CharacterHandle`,
sinon journal + abandon), ce qui est un changement de comportement par rapport à aujourd'hui. Reste
ouvert : faut-il **répondre** un `TM_SC_RESULT` de refus (NGemity rend `TS_RESULT_NOT_EXIST`,
`WorldSession.cpp:382-385`) ou journaliser seulement ? Le dépôt n'a pas de `TM_SC_RESULT` identifié
pour 5 ; **le socle journalise et abandonne**, sans inventer de réponse, et la question reste ouverte.

### 7.8 Le pair qui marche au moment de l'entrée (NGemity envoie `ENTER` + `MOVE`)

`WorldObject::SendEnterMsg` (`Object.cpp:371-412`) présente l'objet à sa position **interpolée** et le
fait suivre d'un `MOVE` s'il est en mouvement. NavisLamia n'interpole la position d'aucun acteur
côté serveur (seuls les monstres le font, `MonsterWorldState.GetPosition`, `CLAUDE.md:420-428`) : la
position diffusée dans l'`ENTER` d'un pair est donc sa dernière position **rapportée**, et le pair
recevra le `MOVE` suivant de ce joueur (au plus tard à sa prochaine requête). **Non tranché** : faut-il
mémoriser le trajet en cours du marcheur (comme `MonsterWorldState`) pour présenter un pair en marche
dès l'entrée ? Le lot ne le fait pas (cela dupliquerait l'interpolation des monstres) et le défaut
est borné : un pair est vu immobile jusqu'à sa prochaine trame de marche, soit au pire une seconde.

## 8. Commits épinglés et méthode de lecture

Références :

| dépôt | commit | date | rôle |
|---|---|---|---|
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | 2023-10-02 | structures et gating (`librzu/src/packets/GameClient/`) |
| NGemity/Chihiro | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | 2025-12-03 | logique de référence, compilée `EPIC_4_1_1` |
| client 7.3 | `SFrame.exe` `sha256 41e0af2e…fb9500e` | — | lecture statique seule (dossier non git) |
| NavisLamia | `3021f68` (`3021f681e98544112ce508b11ce1df305b3a0dd9`) | — | base de la branche |

Commandes de lecture utilisées (reproductibles, aucune exécution du client) :

```bash
# structure et gating
awk '{printf "%d|%s\n", NR, $0}' reference/rzu/librzu/src/packets/GameClient/TS_SC_ENTER.h
awk '{printf "%d|%s\n", NR, $0}' reference/rzu/librzu/src/packets/GameClient/TS_SC_MOVE.h
awk '{printf "%d|%s\n", NR, $0}' reference/rzu/librzu/src/packets/GameClient/TS_SC_LEAVE.h
awk '{printf "%d|%s\n", NR, $0}' reference/rzu/librzu/src/packets/GameClient/TS_CS_MOVE_REQUEST.h
awk '{printf "%d|%s\n", NR, $0}' reference/rzu/librzu/src/packets/GameClient/TS_CS_REGION_UPDATE.h

# logique de référence — toutes les citations NGemity de §5.1, §5.5, §6 et §7 sont relatives à
# reference/ngemity/Chihiro/src/ : World/World.cpp, Functors/Functors.cpp, Entities/Object/Object.cpp,
# Entities/Unit/Unit.cpp, Network/Messages.cpp, Network/GameNetwork/WorldSession.cpp,
# Map/RegionContainer.h ; la version compilée est dans reference/ngemity/shared/Common/Define.h:25

# répartiteur entrant : plage 0..250 (base 0, borne 0xfa), table d'octets 0x67f0a0, sauts 0x67f020
objdump -d -M intel --start-address=0x67df40 --stop-address=0x67df95 SFrame.exe
# id 8 -> index 4 -> stub 0x67dfbd -> handler 0x673800 ; id 9 -> index 5 -> 0x67dfca -> 0x66e5f0
# id 2 -> index 1 -> 0x67df96 ; id 10 -> index 6 -> code en ligne 0x67dfe4 (lit [ebx+7])
objdump -d -M intel --start-address=0x67df89 --stop-address=0x67e000 SFrame.exe

# handler d'entrée : branchement par objType, allocation et offsets
objdump -d -M intel --start-address=0x66e650 --stop-address=0x66e8e0 SFrame.exe
# handler de sortie : [frame+7] -> [obj+0x13], allocation 0x17
objdump -d -M intel --start-address=0x66e5f0 --stop-address=0x66e650 SFrame.exe
# handler de marche : handle @11, speed @16, count @17, points @19 (pas 8)
objdump -d -M intel --start-address=0x673800 --stop-address=0x673a20 SFrame.exe
```

Les deux tables du répartiteur entrant se lisent dans `.text` (VA `0x401000` ↔ offset fichier
`0x400`) ; le script utilisé pour les extraire est `/tmp/read_tables.py` de ce run (`struct.unpack` sur
les octets de la table d'octets puis les `uint32` de la table de sauts). L'index `0x1f` (31) mène au
défaut `0x67ef21` (« message non traité ») : `TM_CS_LOGIN` (1), `TM_CS_MOVE_REQUEST` (5),
`TM_CS_REGION_UPDATE` (7) tombent bien sur le défaut, ce qui **confirme le sens** de chaque id.

## 9. Mise en œuvre (lot dev)

### 9.1 Découpage — quatre livrables, du plus sûr au plus exposé

| lot | contenu | fichiers | testable seul |
|---|---|---|---|
| **L1** | les deux briques : grille mutable des présences (couche dans la clé) + registre `handle → session`, avec leurs tests unitaires purs | **neufs** `Game/Services/PlayerVisibilityIndex.cs`, `Game/Services/PlayerRegistry.cs` (+ tests) | oui |
| **L2** | le service de diffusion : réconciliation symétrique, constructions de trames, ordre des verrous (§5.4) ; **plus** la surcharge de `GameMovePackets` à `N` points | neuf `Game/Services/PlayerVisibilityService.cs` ; `Game/Network/Packets/Game/GameMovePackets.cs` | oui (tests sur la construction des trames) |
| **L3** | le câblage aux cinq déclencheurs (§5.3) et les deux `ConnectionInfo` de vue (`SpawnedPlayers` + `PlayerVisibilityLock`) | `Game/Network/Clients/GameClient.cs`, `Game/Network/Clients/ConnectionInfo.cs`, `Game/Network/Clients/Actions/GameActions.cs`, `Game/Network/NetworkService.cs`, `Game/Services/WarpService.cs` | oui (via `StorageTestHarness`) |
| **L4** | la réutilisation par les étals : `BoothWatchService` résout le propriétaire par le registre au lieu du balayage | `Game/Services/BoothWatchService.cs` (+ `BoothWatchTests`) | oui |

**L4 est recommandé mais détachable** : le card dev nomme le balayage des étals comme le motif
d'origine de la brique « handle → session ». S'il est livré, `TryFindBoothOwner` garde sa signature
(deux appels de production et cinq assertions de test existent :
`BoothWatchService.cs:74`, `Tests/Game/BoothWatchTests.cs:269-291`), mais lit le registre ; ses tests
doivent continuer à passer **sans modification** ou être étendus, jamais affaiblis.

### 9.2 Ce qui est réutilisé tel quel (à dire dans le compte rendu)

- `WorldObjectStreamer.Stream` — la boucle unique de visibilité (entrée, `LEAVE`, tenue des cartes,
  `canEnter`) **est** la boucle du socle : lui ajouter un paramètre optionnel
  `Func<T, uint> handleFor` (défaut `WorldObjectHandle.Next()`, comportement actuel des trois
  appelants **inchangé**) permet de réutiliser la boucle au lieu d'en écrire une quatrième copie — le
  commentaire du fichier (`WorldObjectStreamer.cs:13-17`) dit que trois copies avaient déjà dérivé :
  ne pas en ajouter une quatrième. La différence de politique de handle est le seul écart : pour un
  joueur le handle transmis est **`character.Id`**, partagé par tous les observateurs (imposé par
  `GameActions.cs:181`, et par le fait que `TS_SC_MOVE` ne transporte qu'un handle commun), alors que
  pour un PNJ/monstre/objet il est alloué **par client** (`WorldObjectStreamer.cs:63`).
- `GameSpawnPackets.BuildLeave`, `GameMovePackets.BuildMove`/`BuildStopMove`, `TS_SC_ENTER_PLAYER`,
  `ServerClock`, `ConnectionInfo.ClientClockOffset`, `NetworkService.AuthorizedGameClients`,
  `WarpService.LeaveEverything`, `ConnectionInfo.ClearVisibleObjects`.
- **Ne pas** modifier `SpatialIndex<T>` ni `WorldVisibility` : la grille est neuve, les valeurs de
  fenêtre sont reprises telles quelles (`WorldVisibility.ViewRange`, 540).

### 9.3 Zone de recouvrement mesurée (registre local, `refs/remotes/origin` au dernier `fetch`)

Mesure faite avant d'écrire, sans `git fetch` (`git fetch` est proscrit) : **34 branches
`origin/hermes/*` sont en avance sur `origin/master`**, et le compte par fichier est le suivant.

| fichier | branches ouvertes qui le touchent |
|---|---|
| `Game/Network/Clients/GameClient.cs` | **29** |
| `Game/Network/Packets/Enums/GamePackets.cs` | **28** |
| `Game/Network/NetworkService.cs` | **7** |
| `Tests/Game/StorageTestHarness.cs` | **3** |
| `Game/Network/Clients/ConnectionInfo.cs` | **1** (`packet-251-buy-item`) |
| `TS_SC_ENTER_PLAYER.cs`, `GameMovePackets.cs`, `GameActions.cs`, `WorldObjectStreamer.cs`, `SpatialIndex.cs`, `WorldVisibility.cs`, `BoothWatchService.cs`, `WarpService.cs`, `MonsterWorldState.cs`, `MonsterMovementService.cs`, `PetBehaviorService.cs` | **0** |

Autrement dit : **le lot est presque entièrement hors des fichiers chauds**. Les deux seuls points de
contact inévitables sont `GameClient.cs` (29) et `NetworkService.cs` (7) ; `GamePackets.cs` (28)
n'est touché **que si** un membre est ajouté — ce lot n'en ajoute aucun (§5.6), donc **laisser
l'énumération intacte** est à la fois exact et la meilleure façon de rester hors du fichier le plus
disputé du dépôt. Le harnais de test `Tests/Game/StorageTestHarness.cs` (3 branches) est le point de
contact qu'on n'attend pas : ne le modifier que si c'est indispensable, et le signaler.

Signalement de point chaud pour l'orchestrateur (à reprendre dans le compte rendu) :
`hotspot: Game/Network/Clients/GameClient.cs — 29 branches ouvertes le touchent ; le lot doit s'y
limiter à cinq appels (enrôlement, marche, frontière, warp, sortie)`.

### 9.4 Tests exigés (au-delà du plancher de la suite)

1. **Offsets de `TS_SC_ENTER` variante joueur** : les 118 octets et **chaque** champ à sa position
   (`type` @7, `handle` @8, `x`/`y`/`z` @12/16/20, `layer` @24, `objType` @25, charge @26), avec une
   assertion par champ — le test existant `Tests/Game/PkModeStatusTests.cs:80-120` couvre déjà la
   taille et le statut @26 : **l'étendre**, ne pas le dupliquer.
2. **Offsets de `TS_SC_LEAVE`** : 11 octets, `handle` @7 seul champ.
3. **Offsets de `TS_SC_MOVE`** : `19 + 8 × N`, `start_time` @7, `handle` @11, `tlayer` @15, `speed`
   @16, `count` @17, points @19/23 — **cas négatif** : un `N` à 1 et un `N` à plusieurs points (la
   surcharge à `N` variable), et une assertion que le `count` écrit correspond au nombre de points.
4. **Le handle d'un joueur est partagé** : deux observateurs reçoivent le **même** handle pour le
   même pair, et ce handle vaut `character.Id` — le test qui interdit la politique « handle par
   client » de `WorldObjectStreamer` pour les joueurs.
5. **Réciprocité** : A voit B quand B voit A (l'entrée d'un pair produit **deux** trames : une par
   sens), et une sortie de fenêtre produit **deux** `LEAVE`.
6. **Filtrage du marcheur** : un `TM_CS_MOVE_REQUEST` dont le `handle` n'est pas celui de l'émetteur
   est journalisé et **non diffusé** (§7.7) — cas négatif explicite.
7. **Sortie** : après `ReturnToLobbyAsync`/`OnDisconnect`, le registre ne contient plus le handle et
   chaque observateur a reçu un `LEAVE`.
8. **Non-régression de la boucle partagée** : les trois appelants historiques de
   `WorldObjectStreamer.Stream` continuent d'allouer un handle neuf par client (le paramètre optionnel
   ne change rien pour eux).
9. **Le socle n'envoie rien au client qui n'a pas de pair** : un client seul dans la fenêtre ne reçoit
   aucune trame de la part du service (garde contre un envoi inconditionnel).

Baseline à ne pas baisser : `dotnet test Tests/Tests.csproj` mesuré sur `master` `3021f68` au réveil
du 2026-09-30 : **1494 réussis, 0 échec, 0 ignoré** (build `exit 0`, 190 avertissements, 0 erreur) ;
plancher du critère : 366. Relever **séparément** le code de sortie du build et celui des tests.

## 10. Bloc `CLAUDE.md` (livré dans la description de la MR, recopié par la QA)

> **Visibilité entre joueurs — entrée, sortie et déplacement (`TM_SC_ENTER` 3 / `TM_SC_MOVE` 8 /
> `TM_SC_LEAVE` 9)**
>
> Aucune trame nouvelle : la variante **joueur** de `TS_SC_ENTER` fait **118 octets** (en-tête 7,
> `type` @7 = 0, `handle` @8 = `character.Id`, `x/y/z` @12/16/20, **`layer` @24**, **`objType` @25** = 0,
> charge `PLAYER_INFO` de **92** octets @26) et le `struct` `TS_SC_ENTER_PLAYER` la porte déjà champ
> pour champ — il n'y a rien à ajouter pour qu'un autre joueur la voie. Les deux autres trames
> existent aussi : `TS_SC_LEAVE` (**11** octets, `handle` @7) et `TS_SC_MOVE`
> (**19 + 8 × N**, `start_time` @7, `handle` @11, `tlayer` @15, `speed` @16, `count` @17, points @19
> par pas de 8). Ce qui manquait est **serveur** : un index des présences (grille **mutable**, sur le
> modèle de `SpatialIndex` mais avec `Add`/`Move`/`Remove` et la **couche** dans la clé de cellule) et
> la **résolution handle → session en O(1)** (`ConcurrentDictionary`, comme `AuthorizedGameClients`),
> qui remplace le balayage de `BoothWatchService.TryFindBoothOwner`. La visibilité des joueurs est
> **réciproque** (A voit B ⟺ B voit A, comme le `SendEnterMessageEachOtherFunctor` de NGemity), se
> calcule sur la même fenêtre que les autres types (`WorldVisibility.ViewRange`, 540 unités), et se
> déclenche aux **cinq** moments déjà câblés : entrée en jeu, `TM_CS_MOVE_REQUEST`,
> `TM_CS_REGION_UPDATE`, warp, sortie (lobby/déconnexion). Le handle diffusé est **toujours**
> `character.Id`, jamais un handle par client comme pour les PNJ et monstres, et la marche diffusée
> porte `start_time = ServerClock.Now + ClientClockOffset` du **destinataire** (le `handle` reçu du
> client est filtré avant diffusion). Règles de verrouillage : **jamais deux verrous de visibilité
> imbriqués**, une seule acquisition à la fois, et les envois hors verrou. Hors lot : l'équipement des
> autres joueurs (`TS_SC_WEAR_INFO`, que NGemity envoie après l'`ENTER`), leurs états (mort, siège,
> PK) et la présentation d'un pair déjà en marche à l'instant de son entrée. Trois pièges : `layer`
> **avant** `objType` (l'ordre `>= EPIC_9_6_7` est différent), `name` sur **19** octets (et non 20,
> `>= EPIC_9_6`), et une entrée hors de la fenêtre du client est **définitivement ignorée** par lui.
> Détail, sources et réserves : `docs/packet-specs/socle-visibilite-joueurs.md`.

## 11. Livraison du lot dev — L1 à L3, écarts et corrections de fiche

Implémenté sur `hermes/packet-socle-visibilite-joueurs`, en suivant le découpage de §9.1 : **L1, L2 et
L3 sont livrés, L4 ne l'est pas** (§11.3 point 6). Les trois ids du socle étant déjà déclarés et déjà
émis, **`Game/Network/Packets/Enums/GamePackets.cs` n'est pas touché** (§5.6) : `git diff
origin/master -- …/GamePackets.cs` est vide, et il en va de même de `WorldObjectStreamer.cs`,
`SpatialIndex.cs`, `WorldVisibility.cs` et `TS_SC_ENTER_PLAYER.cs`.

### 11.1 Fichiers livrés

| lot | code | tests |
|---|---|---|
| **L1** | `Game/Services/PlayerVisibilityIndex.cs`, `Game/Services/PlayerRegistry.cs`, `Game/Services/PlayerPresence.cs` (la présence **et** `PlayerAppearance`) | `Tests/Game/PlayerVisibilityIndexTests.cs`, `Tests/Game/PlayerRegistryTests.cs` |
| **L2** | `Game/Services/PlayerVisibilityService.cs`, `Game/Services/Interfaces/IPlayerVisibilityService.cs`, surcharge `GameMovePackets.BuildMove(handle, startTime, layer, speed, ReadOnlySpan<byte> waypoints)` | `Tests/Game/PlayerVisibilityFrameTests.cs`, `Tests/Game/PlayerVisibilitySocleTests.cs` |
| **L3** | `Actions/GameActions.cs` (déclencheur 1), `GameClient.cs` (2, 3, 5), `WarpService.cs` (4), `ConnectionInfo.cs` (`SpawnedPlayers`, `PlayerVisibilityLock`, `Appearance`), `NetworkService.cs` + `DevConsole/Program.cs` (injection), `Tests/Game/StorageTestHarness.cs` (paramètre optionnel) | les deux fichiers de L2 |

Les deux briques de §5.2 sont **des fichiers neufs et autonomes** : ni l'index ni le registre ne vivent
dans le service de diffusion. Le service les **possède** (`Index`, `Registry`) et les expose par
`IPlayerVisibilityService`, donc un autre service — les étals, demain — peut résoudre un handle sans
passer par la diffusion.

### 11.2 Ce qui est réutilisé tel quel (aucune réécriture)

`GameSpawnPackets.BuildLeave` (11 octets, §3.4), `GameMovePackets.CreateMove`/`WriteChecksum` (la
surcharge à `N` points ne réécrit que le corps de `BuildMove`), `TS_SC_ENTER_PLAYER` et
`ActorStatus.ForPlayer` (le statut d'un pair), `GameCharacterPackets.GetFaceId`/`GetHairId` (les mêmes
que l'entrée locale), `ServerClock` et `ConnectionInfo.ClientClockOffset` (le temps du `MOVE` du
destinataire), `ConnectionInfo.ClearVisibleObjects` (étendue à la vue des joueurs),
`WarpService.LeaveEverything` (qui appelle désormais la passe des joueurs), `WorldVisibility.ViewRange`
(540) comme côté de cellule de la grille neuve.

### 11.3 Écarts assumés, et corrections apportées à cette fiche

1. **`WorldObjectStreamer.Stream` n'est pas réutilisé**, alors que §9.2 le recommandait avec un
   paramètre optionnel `Func<T, uint> handleFor`. Le paramètre aurait porté la **politique de handle**,
   mais pas les deux propriétés qui font le socle : la boucle tient **une** carte handles-par-client et
   envoie **sous le verrou du client courant**, alors qu'un joueur est vu sous un handle **partagé**
   (§9.2) et que la réconciliation symétrique impose de toucher **deux** sessions sous **deux** verrous
   distincts, jamais imbriqués (§5.4 règle 1). Le service de diffusion écrit donc sa propre passe, et
   les trois appelants historiques de la boucle sont **inchangés** : le point 8 de §9.4 tient par
   construction (aucune assertion à écrire, aucun comportement à préserver n'a bougé).
2. **`Sync` réindexe la présence — §5.3 déclencheur 3 l'exige et le premier jet ne le faisait pas.**
   La fiche demande « mise à jour de l'entrée dans l'index + re-synchronisation » pour le
   franchissement de région. Or `Sync` lisait la position **indexée** : un client qui franchissait une
   frontière sans envoyer de chemin laissait son entrée à l'ancienne place, la fenêtre se calculait
   sur une position périmée, et les pairs déjà hors de la **nouvelle** fenêtre n'étaient jamais
   détachés. `Sync` appelle donc `Index.Move(handle, X, Y)` avant de calculer l'ensemble en portée —
   c'est la **session** qui fait foi sur la place, l'index n'en est que le reflet. Verrouillé par
   `RegionBorder_ReIndexesThePresenceAndTellsBothSides` (les deux côtés reçoivent leur `LEAVE`, la
   présence porte la nouvelle position, aucune trame d'entrée n'est émise).
3. **Les traits d'un pair viennent d'un instantané de session.** `ConnectionInfo.Appearance`
   (`PlayerAppearance`) est posé par `GameActions.OnLogin` **depuis l'entité qui construit l'entrée
   locale** : la session ne garde aucune `CharacterEntity`, et une trame de pair ne peut donc pas être
   fabriquée sans ce portrait. Tout ce qui change en session (niveau, job, hp/mp, position, états) est
   relu **en direct** dans `ConnectionInfo` à chaque construction de trame, jamais figé dans
   l'instantané.
4. **`max_mp` d'un pair est sa mana courante**, exactement le choix de l'entrée locale
   (`Actions/GameActions.cs`, `MaxMp = mp`) : la session ne porte pas de mana maximale. Assumé comme
   tel, pas deviné — c'est la seule valeur que le dépôt ait jamais mise dans ce champ.
5. **`is_first_enter = 1`** pour un pair, seule valeur que le dépôt ait jamais envoyée (§7.2, réserve
   `## A VERIFIER` 3).
6. **L4 (étals) non livré** : `BoothWatchService.TryFindBoothOwner` continue son balayage. Le registre
   rend le balayage inutile, mais rebrancher les étals dans ce lot élargirait le diff d'un fichier qui
   appartient à la carte des étals, et la fiche le classe explicitement « recommandé mais détachable »
   (§9.1). La brique est livrée réutilisable (`IPlayerVisibilityService.Registry`), le branchement est
   une décision de Killian (réserve `## A VERIFIER` 8).
7. **Aucune politique de jeu, aucune constante nouvelle** : la portée est `WorldVisibility.ViewRange`
   (540, comme les trois autres types), la vitesse diffusée est `ConnectionInfo.EchoedMoveSpeed`, la
   couche est celle du marcheur (`ConnectionInfo.Layer`), et **aucune cadence de re-diffusion** n'est
   ajoutée : les cinq déclencheurs de §5.3 sont évènementiels, comme `SyncVisibleObjects`.
   `SpawnedPlayers` est un `Dictionary<long, uint>` **par symétrie avec `SpawnedMonsters`/`SpawnedProps`
   du même fichier** — la clé y est le handle partagé et la valeur le handle affiché, les deux n'étant
   égaux que pour un joueur ; les autres types gardent leur carte id → handle.

### 11.4 Tests exigés par §9.4, et où ils sont

| §9.4 | test |
|---|---|
| 1 — offsets de l'`ENTER` joueur (118, champ par champ) | `PlayerVisibilityFrameTests.PlayerEnterFrame_IsOneHundredAndEighteenBytesWithEveryFieldAtItsOffset`, `…_ReservesTheNineteenNameBytesWithoutRunningIntoTheJobId` |
| 2 — offsets du `LEAVE` (11, `handle` @7) | `…LeaveFrame_IsElevenBytesAndCarriesTheHandleAtSeven` |
| 3 — offsets du `MOVE` (`19 + 8 × N`), cas minimal et à plusieurs points, cas négatif | `…MoveFrame_WithTwoWaypoints_IsThirtyFiveBytesAndKeepsTheReceivedPoints`, `…_WithOneWaypoint_KeepsTheHistoricalLayout`, `…_WithoutAWaypointOrWithAPartialOne_IsRefused` |
| 4 — handle partagé par tous les observateurs | `PlayerVisibilitySocleTests.Entry_EveryObserverIsGivenTheSameSharedHandle` |
| 5 — réciprocité (entrée et sortie) | `…Entry_PairsBothSidesWithTheirOwnEnterFrame`, `…WindowExit_TellsBothSidesLeavingTheWindow`, `…RegionBorder_ReIndexesThePresenceAndTellsBothSides` |
| 6 — filtrage du handle du marcheur (cas négatif) | `…MoveRequest_ClaimingAForeignHandle_IsDroppedWithoutEchoOrDiffusion` |
| 7 — sortie (registre vidé, `LEAVE` chez les observateurs) | `…Exit_TellsEveryObserverAndKeepsNoPresence` |
| 8 — non-régression des trois appelants de la boucle partagée | sans objet : `WorldObjectStreamer.cs` n'est pas modifié (§11.3 point 1) |
| 9 — le socle n'envoie rien à un client sans pair | `…Entry_OutOfTheWindow_ProducesNothingOnEitherSide`, `…Move_AloneInTheWorld_SendsNothing` |

Plus, hors §9.4 : la couche exclut la visibilité (`Entry_OnAnotherLayer_…`), la frontière n'émet aucune
trame de marche (`RegionUpdate_ReachesTheSocle`, socle appelé avec un faux service), le warp annonce le
départ et réinscrit à la nouvelle place (`Warp_AnnouncesTheDepartureAndRegistersTheNewPlace`) et un
`MOVE_REQUEST` bien formé remet ses points au socle (`MoveRequest_WellFormed_HandsTheWaypointsToTheSocle`).

Enfin, un écart de forme connu et non mesuré en jeu : la variante joueur du client 7.3 n'est établie
que par lecture statique et par l'accord des six tailles déjà vérifiées du handler (§3.6) ; les neuf
réserves de `## A VERIFIER PAR KILLIAN` restent ouvertes, et **aucune** n'est comblée par ce lot.

## A VERIFIER PAR KILLIAN

1. **Vérification en jeu, deux clients** (§7.1) : deux personnages à moins de 540 unités doivent se
   voir **apparaître, marcher et disparaître**. C'est la seule preuve que la trame joueur crée bien un
   acteur pair chez le client ; la fiche l'établit par les références et l'accord des six tailles
   connues du handler client, pas par une exécution.
2. **`TS_SC_WEAR_INFO` des autres joueurs** (§5.5) : sans lui, un observateur voit son pair **sans
   équipement**. NGemity l'envoie juste après l'`ENTER` (`Object.cpp:409-411`). C'est **hors lot** ici
   faute de source bon marché de l'équipement d'une autre session (lire la base à chaque entrée de
   pair, ou mettre l'entité en cache et la coupler aux six MR ouvertes des chemins d'équipement) :
   faut-il ouvrir la carte maintenant, ou accepter un pair « en tenue par défaut » dans un premier
   temps ?
3. **La valeur de `is_first_enter`** diffusée à un tiers (§7.2) : la fiche retient `1` (seule valeur
   que le dépôt ait envoyée). Si l'apparence du pair se reconstruit à chaque ré-entrée de fenêtre,
   c'est ce champ qu'il faudra basculer à `0` — décision non tranchable sur les références locales.
4. **Le trou de réception des ids S→C** (§5.6) : une trame entrante d'id 3, 8 ou 9 — ou de tout autre
   id serveur → client de `GamePackets` — traverse `DefinedPackets` puis atteint le `switch` final qui
   lève, donc **termine le processus**. Le socle ne l'a pas refermé (le card dev l'admet comme cas
   particulier argumenté, et l'énumération n'est pas touchée). Faut-il le refermer globalement (une
   garde pour tous les ids S→C, sur le modèle de `TM_SC_REGION_ACK`) et sur quelle carte ?
5. **`bIsStopMessage` et la garde de `TM_CS_REGION_UPDATE`** (§7.5) : le drapeau d'arrêt @23 n'est lu
   par personne et la garde accepte 23 octets là où rzu en déclare 24. Le lot ne touche ni l'un ni
   l'autre (aucune référence ne sanctionne une diffusion d'arrêt) : faut-il les traiter ?
6. **La position d'un pair déjà en marche** (§7.8) : NGemity présente l'objet à sa position
   **interpolée** et enchaîne un `MOVE` ; NavisLamia n'interpole rien pour un joueur, donc un pair qui
   marche est vu immobile jusqu'à sa prochaine trame (au pire une seconde). Faut-il mémoriser le
   trajet en cours d'un marcheur comme le fait `MonsterWorldState` pour les monstres ?
7. **La fenêtre circulaire contre la fenêtre carrée** (§7.4) : le dépôt streame dans un cercle de 540
   alors que la fenêtre du client est un carré de ±540 (7 × 7 régions de 180). Les acteurs des coins
   ne sont donc pas envoyés — comportement déjà en vigueur pour tous les autres types, conservé ici.
8. **La réutilisation des étals (L4)** : `BoothWatchService` cherche son propriétaire par balayage de
   toutes les sessions (`:155-175`). Le registre du socle rend ce balayage inutile ; faut-il l'y
   brancher dans ce lot (le fichier a **0** collision ouverte) ou sur la carte des étals ?
9. **Le filtrage du handle de marche** (§7.7) : le socle abandonne sans réponse un
   `TM_CS_MOVE_REQUEST` dont le `handle` n'est pas celui de l'émetteur. NGemity répond
   `TS_RESULT_NOT_EXIST` ; le dépôt n'a pas de réponse identifiée pour 5. Faut-il en inventer une, ou
   rester au journal ?


