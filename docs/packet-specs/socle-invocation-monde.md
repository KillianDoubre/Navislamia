# Invocation du familier dans le monde — entrée, position et sortie (Epic 7.3)

Cette fiche instruit les quatre points laissés ouverts par `socle-invocations.md` (§9.4, §9.5, `NON ÉTABLI` 4
et 8) :

1. la trame qui fait **entrer** l'invocation dans le monde : id, taille totale, offset et source de chaque
   champ, `objType` retenu et sa justification ;
2. la **provenance de la position** de l'invocation ;
3. la **sortie** du monde (305 / 306) et le **mécanisme de renvoi** ;
4. la **chaîne de persistance** : quel champ porte quoi, et par quel service il est écrit.

Elle ne modifie pas `socle-invocations.md` (livrable distinct, déjà commité) et ne touche aucun fichier de
code serveur : le dev écrira la trame et ses tests d'offsets à partir d'ici.

**Méthode — lectures statiques uniquement.** `reference/rzu` tranche les tailles, l'ordre des champs et le
gating de version ; `reference/ngemity/Chihiro` tranche la logique ; le client 7.3 est lu par `strings` sur
`SFrame.exe` (jamais exécuté, pas de désassemblage) et par ses `db_*.rdb`. Aucune exécution de Lua ni de
script client, aucun serveur de jeu, aucune base. Aucun accès Trello ni GitHub depuis ce profil : la carte
`qwO8Tc1d` n'a **pas** été lue, la commande vient du corps de la tâche du board.

---

## 1. Identité

| rôle | id 7.3 | nom | source |
|---|---|---|---|
| entrée | **3** | `TM_SC_ENTER` | `op_codes.md:6` ; `reference/rzu/librzu/src/packets/GameClient/TS_SC_ENTER.h:167-169` |
| sortie | **305** | `TM_SC_UNSUMMON` | `op_codes.md:107` ; `TS_SC_UNSUMMON.h:10-12` |
| sortie | **9** | `TM_SC_LEAVE` | `op_codes.md:12` ; `TS_SC_LEAVE.h:10-12` |
| renvoi | **306** | `TM_SC_UNSUMMON_NOTICE` | `op_codes.md:108` ; `TS_SC_UNSUMMON_NOTICE.h:9-11` |

Les quatre sont strictement serveur → client (`CREATE_PACKET_VER_ID(..., SessionPacketOrigin::Server)`),
donc **aucun bras de dispatch à ajouter** dans `GameClient.Receive` (règle `CLAUDE.md:1760-1766`). Le client
7.3 connaît les quatre noms : table de chaînes de `SFrame.exe` (`TM_SC_ENTER`, `TM_SC_LEAVE`,
`TM_SC_UNSUMMON`, `TM_SC_UNSUMMON_NOTICE`) et répartiteur statique (`case MSG_ENTER`, `case MSG_LEAVE`,
`case MSG_UNSUMMON`).

L'invocation n'a **pas** d'id de paquet propre : elle emprunte `TM_SC_ENTER`, discriminée par son `objType`
(§3.2), exactement comme le monstre (`3`), l'objet au sol (`2`) et le décor (`6`).

## 2. Ce que le joueur fait pour que le serveur émette la trame

Le joueur n'envoie rien qui produise cette trame : elle est **décidée par le serveur**. Trois actions du
joueur la déclenchent, d'après NGemity :

| action du joueur | chemin NGemity | effet |
|---|---|---|
| lancer le sort d'invocation sur une carte d'invocation liée | `Skill::DO_SUMMON` (`src/Skills/Skill.cpp:1537-1558`), atteint par l'effet `EF_SUMMON = 601` (`src/Skills/SkillBase.h:344`, dispatch `Skill.cpp:1318-1321`) ; la carte doit être l'une des 6 cartes liées (`Skill.cpp:619-628`) | `Player::DoSummon` → la trame entre dans le monde |
| se connecter avec une invocation principale enregistrée | `Player::SendLoginProperties` (`src/Entities/Player/Player.cpp:818-823`, après le bloc d'info 301 de `Player.cpp:772-775`) | l'invocation entre avec le joueur |
| se téléporter (warp) | `World::WarpBegin` / `WarpEndSummon` (`src/World/World.cpp:426-434`, `463-480`) | l'invocation sort puis rentre |

NGemity ne passe **jamais** par `TM_CS_SUMMON` (304) : rzu le marque « Seems unused »
(`TS_CS_SUMMON.h:5`) et aucun handler ne le référence. Le `NON ÉTABLI` 3 du socle (304 ou le sort 400 ?)
reste donc entier pour le client 7.3 : cette fiche ne le tranche pas, aucun élément local ne le tranche.

## 3. Structure sur le fil

### 3.1 La trame d'entrée — `TM_SC_ENTER` (3), 96 octets

**Taille totale : 96 octets** = 7 d'en-tête + 89 de charge utile. Somme : `1 (type) + 4 (handle) + 12 (x/y/z)
+ 1 (layer) + 1 (objType) + 38 (creatureInfo) + 4 (master_handle) + 8 (summon_code) + 19 (name) + 1
(enhance)` = 89.

| offset | type | champ | valeur observée (référence) | source |
|---|---|---|---|---|
| 0-3 | `uint32` | `length` | 96 | `TS_SC_ENTER.h:145-165` ; convention du dépôt `GameSpawnPackets.cs:174-178` |
| 4-5 | `uint16` | `id` | 3 | `TS_SC_ENTER.h:167-169` |
| 6 | `uint8` | checksum | somme des 6 premiers octets | `GameSpawnPackets.cs:188-196` |
| 7 | `uint8` | `type` (`ET_*`) | **1** = `ET_NPC` | `Summon.cpp:46` (`_mainType = MT_NPC`, commentaire « dont question it :^) ») ; `Object.cpp:375` ; `Object.h:30` ; enum `TS_SC_ENTER.h:23-27` |
| 8-11 | `uint32` | `handle` | handle d'exécution de l'invocation | `Object.cpp:376` ; allocation chez nous `Game/Network/WorldObjectHandle.cs:5-10` |
| 12-15 | `float` | `x` | position serveur (§5.2) | `Object.cpp:377` ; `TS_SC_ENTER.h:153` |
| 16-19 | `float` | `y` | idem | `Object.cpp:378` ; `TS_SC_ENTER.h:154` |
| 20-23 | `float` | `z` | **`NON ÉTABLI`** — voir `NON ÉTABLI` 6 | `Object.cpp:379` ; `TS_SC_ENTER.h:155` |
| 24 | `uint8` | `layer` | le layer du maître | `Object.cpp:380` ; posé sans z par `Player.cpp:822` et `Skill.cpp:641` ; placement ancien `TS_SC_ENTER.h:156-157` (§4) |
| 25 | `uint8` | `objType` (`EOT_*`) | **4** = `EOT_Summon` | `Object.cpp:381` dérive l'`objType` du `SubType` ; `Object.h:37` (`ST_Summon = 4`) ; `TS_SC_ENTER.h:17` |
| 26-29 | `uint32` | `creatureInfo.status` | 0 (sauf mode combat / invisibilité) | `Unit.cpp:110` → `Messages::GetStatusCode` (`Messages.cpp:557-566`) ; rzu `TS_SC_ENTER.h:68`, `TS_SC_STATUS_CHANGE.h:8-10` |
| 30-33 | `float` | `creatureInfo.face_direction` | orientation de l'invocation | `Unit.cpp:111` ; `TS_SC_ENTER.h:69` |
| 34-37 | `int32` | `creatureInfo.hp` | `SummonEntity.Hp` | `Unit.cpp:112` ← `UNIT_FIELD_HEALTH` ← la ligne `Summon` (`Player.cpp:727`) |
| 38-41 | `int32` | `creatureInfo.max_hp` | **`NON ÉTABLI`** — voir `NON ÉTABLI` 5 | `Unit.cpp:114` (`GetMaxHealth`, calculé par `Unit::CalculateStat`, `src/Entities/Unit/CalculateStat.cpp:29`) |
| 42-45 | `int32` | `creatureInfo.mp` | `SummonEntity.Mp` | `Unit.cpp:113` ← `UNIT_FIELD_MANA` ← la ligne `Summon` (`Player.cpp:728`) |
| 46-49 | `int32` | `creatureInfo.max_mp` | **`NON ÉTABLI`** — voir `NON ÉTABLI` 5 | `Unit.cpp:115` |
| 50-53 | `int32` | `creatureInfo.level` | `SummonEntity.Lv` | `Unit.cpp:116` ← `Player.cpp:721` |
| 54 | `uint8` | `creatureInfo.race` | **0** | `Unit.cpp:117` ; `UNIT_FIELD_RACE` n'est posé que pour le PJ (`Player.cpp:166`), le PNJ (`NPC.cpp:45`) et le monstre (`Monster.cpp:50`) — jamais pour une invocation |
| 55-58 | `uint32` | `creatureInfo.skin_color` | **0** | `Unit.cpp:118` ; `UNIT_FIELD_SKIN_COLOR` n'est posé que pour le PJ (`Player.cpp:188`) ; `TS_SC_ENTER.h:76` |
| 59 | `uint8` | `creatureInfo.is_first_enter` | 1 à la première entrée, 0 en rentrée | `Unit.cpp:119` ← `STATUS_FIRST_ENTER`, posé autour de l'ajout au monde (`World.cpp:417-423`, `471-474`) ; `TS_SC_ENTER.h:77` — réserve `NON ÉTABLI` 8 |
| 60-63 | `int32` | `creatureInfo.energy` | **0** | `Unit.cpp:120` ; `UNIT_FIELD_ENERGY` n'est utilisé que par le système d'énergie joueur (`Unit.cpp:2794-2812`), jamais initialisé pour une invocation ; `TS_SC_ENTER.h:78` |
| 64-67 | `uint32` | `master_handle` | handle du maître | `Summon.cpp:34` ; rzu `TS_SC_ENTER.h:92` |
| 68-75 | `EncodedInt<EncodingRandomized>` (8 o) | `summon_code` | `SummonEntity.SummonResourceId` | `Summon.cpp:35` (`GetSummonCode()`) ; `Summon.cpp:88-91` (`m_tSummonBase->id`) ; `ObjectMgr.cpp:1147-1190` (`SELECT ... FROM SummonResource`) ; rzu `TS_SC_ENTER.h:93`, largeur `EncodingRandomized.h:11` |
| 76-94 | `char[19]` | `name` | `SummonEntity.Name`, ASCII, complété de zéros | `Summon.cpp:36` ; largeur `TS_SC_ENTER.h:94-96` (19 pour `>= EPIC_3 && < EPIC_9_6`, 20 au-delà) ; écriture `GameSummonPackets.cs:198-203` (`WriteName`, 18 caractères utiles) |
| 95 | `uint8` | `enhance` | **0** | `TS_SC_ENTER.h:97` (`>= EPIC_7_1`) ; NGemity ne remplit jamais le champ : `Summon.cpp:32-37` remplit `creatureInfo`, `master_handle`, `summon_code`, `szName` et laisse `enhance` à zéro (`TS_SC_ENTER__SUMMON_INFO summonInfo{}`) — réserve `NON ÉTABLI` 7 |

Les offsets 26 à 63 forment exactement le `TS_SC_ENTER__CREATURE_INFO` de 38 octets déjà écrit par
`GameSpawnPackets.BuildEnterCreature` (`GameSpawnPackets.cs:145-172`) : la trame d'entrée de l'invocation est
**le même préfixe que le monstre** (26 octets) suivi de `EOT_Summon` et de 70 octets de charge spécifique.
L'octet complémentaire du monstre (73 octets, `GameSpawnPackets.cs:34,39`) n'existe pas pour l'invocation.

### 3.2 L'`objType` retenu, et sa justification

**`objType = 4` (`EOT_Summon`).** Deux sources indépendantes le donnent :

- rzu pose l'énumération complète `EOT_Player 0 / EOT_NPC 1 / EOT_Item 2 / EOT_Monster 3 / **EOT_Summon 4**
  / EOT_Skill 5 / EOT_FieldProp 6 / EOT_Pet 7` (`TS_SC_ENTER.h:12-21`) et une charge utile
  `TS_SC_ENTER__SUMMON_INFO` dédiée (`TS_SC_ENTER.h:90-98`) ; c'est déjà l'autorité retenue par le dépôt pour
  ce champ (`CLAUDE.md:335-336` pour `EOT_Item = 2`, `CLAUDE.md:914-916` pour `EOT_FieldProp = 6` :
  « rzu is the authority ») ;
- NGemity écrit `enterPct.objType = (TS_SC_ENTER__OBJ_TYPE)((uint8_t)GetSubType())` (`Object.cpp:381`) et son
  `SubType` vaut `ST_Summon = 4` pour une invocation (`Object.h:32-42`, posé `Summon.cpp:47`), tandis que ses
  valeurs 0/1/2/3/6 coïncident une à une avec celles de rzu.

Le champ `objType` de rzu n'est **pas** gating de version pour `< EPIC_9_6_7` : ses deux écritures
(`TS_SC_ENTER.h:147-148` pour `>= EPIC_9_6_7` et `:156-157` pour la variante ancienne) portent la **même**
énumération. La valeur 4 est donc la même de l'Epic 4 à l'Epic 9.6.6. Ce qui **n'est pas** prouvé ici, c'est que le binaire
7.3 fasse bien du cas 4 une invocation : `SFrame.exe` n'a été lu que par `strings` (voir `NON ÉTABLI` 1).

### 3.3 Les trames de sortie et de renvoi

| id | nom | charge utile | total | source |
|---|---|---|---|---|
| 305 | `TM_SC_UNSUMMON` | `summon_handle` `uint32` @0 | **11** | `TS_SC_UNSUMMON.h:7-12` ; taille écrite par `GameSummonPackets.BuildUnsummon` (`GameSummonPackets.cs:89-99`) |
| 9 | `TM_SC_LEAVE` | `handle` `uint32` @0 | **11** | `TS_SC_LEAVE.h:7-12` ; `GameSpawnPackets.BuildLeave` (`GameSpawnPackets.cs:132-142`) |
| 306 | `TM_SC_UNSUMMON_NOTICE` | `summon_handle` `uint32` @0, `unsummon_duration` `ar_time_t` @4 | **15** | `TS_SC_UNSUMMON_NOTICE.h:5-11` ; `GameSummonPackets` (CLAUDE.md:124-125) |

La sortie est **deux trames, dans cet ordre** : 305 puis 9 (§5.3). Aucune des trois ne porte de position :
une invocation qui sort ne laisse rien à replacer.

**La position ne vient d'aucune autre trame.** Vérifié : `TS_EQUIP_SUMMON` (303) ne porte que
`open_dialog` + 6 `card_handle` (`TS_EQUIP_SUMMON.h:7-9`), `TS_SC_ADD_SUMMON_INFO` (301) que
`card_handle`/`summon_handle`/`name`/`code`/`level`/`sp` (`socle-invocations.md` §3), `TS_CS_SUMMON` (304)
que `is_summon` + `card_handle` (`TS_CS_SUMMON.h:6-8`). Seuls x/y/z de la trame d'entrée portent une
position.

## 4. Gating de version — décisions prises pour 7.3

Epic 7.3 est **inférieur** à `EPIC_9_6_3`, à `EPIC_9_6` et à `EPIC_9_6_7` : toutes les variantes « récentes »
sont donc écartées, et tous les champs « anciens » sont présents. Aucun champ de cette fiche ne reste sans
décision.

| champ | gating rzu | présent en 7.3 ? | décision | source |
|---|---|---|---|---|
| id de la trame | `3` si `< EPIC_9_6_3`, sinon `1003` | `3` | **3** | `TS_SC_ENTER.h:167-169` |
| placement `layer`/`objType` | réordonné si `>= EPIC_9_6_7` (objType, layer, unknown avant `handle`, `:147-151`) | ordre ancien | `layer` @24, `objType` @25 | `TS_SC_ENTER.h:156-157` |
| id 305 / 306 / 9 | `305`/`306`/`9` si `< EPIC_9_6_3`, sinon `1305`/`1306`/`1009` | anciens | **305 / 306 / 9** | `TS_SC_UNSUMMON.h:10-12`, `TS_SC_UNSUMMON_NOTICE.h:9-11`, `TS_SC_LEAVE.h:10-12` |
| `creatureInfo.status` | pas de gating | oui | `uint32` (4 o) | `TS_SC_ENTER.h:68`, `TS_SC_STATUS_CHANGE.h:8` |
| `creatureInfo.race` | `< EPIC_9_6_7` | oui | 1 o, valeur 0 | `TS_SC_ENTER.h:75` |
| `creatureInfo.skin_color` | `>= EPIC_4_1 && < EPIC_9_6_7` | oui | 4 o, valeur 0 | `TS_SC_ENTER.h:76` |
| `creatureInfo.is_first_enter` | `< EPIC_9_6_7` | oui | 1 o | `TS_SC_ENTER.h:77` |
| `creatureInfo.energy` | `>= EPIC_4_1 && < EPIC_9_6_7` | oui | 4 o, valeur 0 | `TS_SC_ENTER.h:78` |
| `name` | `19` si `>= EPIC_3 && < EPIC_9_6`, `20` au-delà | 19 | **19 o, 18 utiles** | `TS_SC_ENTER.h:94-96` ; `GameSummonPackets.cs:25-29,198-203` |
| `enhance` | `>= EPIC_7_1` | oui (7.3 ≥ 7.1) | 1 o, valeur 0 | `TS_SC_ENTER.h:97` |
| `master_handle`, `summon_code` | pas de gating | oui | 4 o / 8 o | `TS_SC_ENTER.h:90-98`, `EncodingRandomized.h:11` |

Le client 7.3 corrobore la décision `enhance` par sa propre ressource : `db_creatureenhance.rdb` est présent
(204 octets, en-tête ASCII `20101022`, sha256 `1e5bf9d3b3bc69ef6e518f8af94a9d07e3bb83200e8bef18a822aaef45177044`)
et `SFrame.exe` nomme `db_CreatureEnhance.rdb`. Le champ est donc attendu par le client 7.3 ; il n'en reste pas
moins à 0 faute de source (voir `NON ÉTABLI` 7).

## 5. Traitement attendu

### 5.1 Entrée — où NGemity émet, et ce que le serveur doit répondre

NGemity émet la trame par le chemin unique de visibilité : `WorldObject::SendEnterMsg` remplit le préfixe,
dérive `objType` du `SubType`, puis appelle `Summon::EnterPacket` pour `ST_Summon`
(`src/Entities/Object/Object.cpp:371-407`, cas `ST_Summon` :390-392), qui remplit les 70 octets spécifiques
(`src/Entities/Summon/Summon.cpp:30-38`) et la partie commune par `Unit::EnterPacket`
(`src/Entities/Unit/Unit.cpp:108-121`). Côté Navislamia, le point d'accroche est le même patron :
`Game/Services/WorldObjectStreamer.cs:30-58` (un `buildEnter` par type d'objet) et
`Game/Network/Packets/Game/GameSpawnPackets.cs:18-43`.

**Ordre d'émission attendu** (NGemity, login) : pour chaque invocation, `TS_SC_ADD_SUMMON_INFO` (301) est
envoyé pendant `SendLoginProperties` (`Player.cpp:772-775` → `Messages::SendAddSummonMessage`,
`src/Network/Messages.cpp:99-120`), puis `TS_EQUIP_SUMMON` (303, `Messages.cpp:122-140`), puis
l'entrée dans le monde (`Player.cpp:818-823`). La trame d'entrée ne remplace pas 301 : **301 alimente la
fenêtre de créature, 3 fait apparaître l'objet dans le monde.**

### 5.2 Provenance de la position — établie

**La position de l'invocation est calculée par le serveur au moment de l'entrée, à partir de la position du
maître, et n'est persistée nulle part.** Quatre éléments, tous dans la référence :

1. l'invocation est placée sur son maître puis bruitée. Au login :
   `SetCurrentXY(GetPositionX(), GetPositionY())` puis `AddNoise(rand32(), rand32(), 50)` puis
   `SetLayer(GetLayer())` (`src/Entities/Player/Player.cpp:820-822`). Au warp :
   `SetCurrentXY(pos.GetPositionX(), pos.GetPositionY())` puis `AddNoise(rand32(), rand32(), 35)`
   (`src/World/World.cpp:468-472`).
2. à l'invocation par sort, la position vient de `Skill::PrepareSummon`, qui part de la position **du
   maître** (`pPlayer->GetCurrentPosition`) et bruite de 70, en **retirant** jusqu'à obtenir un point à plus
   de 24 unités du maître et différent du tirage précédent (`src/Skills/Skill.cpp:604-648`).
3. **la position envoyée par le client est explicitement ignorée** pour une invocation : `Skill::Cast` ne
   recopie `pos` (la position de la cible du paquet client) dans `m_targetPosition` que si l'effet n'est
   **pas** `EF_SUMMON` (`Skill.cpp:584-585`), et `m_targetPosition` est ce qui est transmis à `DoSummon`
   (`Skill.cpp:1555`). Il n'y a donc **aucune** provenance cliente.
4. rien ne la persiste : `Player::Save` n'écrit que la position du personnage (`Player.cpp:878-881`) et, pour
   l'invocation, des identifiants (`Player.cpp:901-905`) ; `SummonEntity` n'a aucune colonne de position
   (`Game/DataAccess/Entities/Telecaster/SummonEntity.cs:7-32`), pas plus que `CharacterEntity` pour son
   invocation (`CharacterEntity.cs:63-71`). À la reconnexion, l'invocation repart de la position du maître.

`AddNoise(r1, r2, v)` ajoute `(r1 % v - v/2, r2 % v - v/2)` et **annule** le décalage si le point obtenu
change de région (`src/Entities/Object/Object.cpp:453-467`). Le bruit est donc borné à `v` unités et ne fait
jamais franchir une frontière de région.

Résumé pour le dev : `x`/`y` = position du maître + bruit borné du palier concerné (70 à l'invocation,
50 au login, 35 au warp), `z` = voir `NON ÉTABLI` 6, `layer` = celui du maître.

### 5.3 Sortie et renvoi

**Sortie — deux trames.** `Player::DoUnSummon` (`src/Entities/Player/Player.cpp:1594-1614`) :

1. construit `TS_SC_UNSUMMON` (`305`) avec `summon_handle = pSummon->GetHandle()` ;
2. la **diffuse** à la région où se trouve l'invocation (`sWorld.Broadcast(...)`) ;
3. l'envoie **en plus directement au maître** si le maître n'est pas dans une région visible de
   l'invocation (`Player.cpp:1607-1611`) — détail à porter, sans quoi le client du maître garde l'invocation
   à l'écran ;
4. puis `sWorld.RemoveObjectFromWorld(pSummon)` (`Player.cpp:1613`), qui émet `TS_SC_LEAVE` (`9`) sur la
   région visible de l'objet (`src/World/World.cpp:330-345`).

Le décès de l'invocation emprunte le même chemin : six secondes après la mort, `Summon::Update` appelle
`GetMaster()->DoUnSummon(this)` (`src/Entities/Summon/Summon.cpp:374-384`).

**Renvoi (306) — non implémenté par la référence.** NGemity n'émet `TS_SC_UNSUMMON_NOTICE` **nulle part**
(aucune occurrence dans `src/`), et le seul champ qui s'y rapporte, `PLAYER_FIELD_REMAIN_SUMMON_TIME`
(`src/Entities/Object/Object.h:119`), est **chargé** (`Player.cpp:204`, colonne 53) puis **jamais relu**. Ce
qui est établi hors NGemity :

- rzu : `summon_handle` + `unsummon_duration` en `ar_time_t`, soit un `uint32_t`
  (`TS_SC_UNSUMMON_NOTICE.h:5-7` ; `librzu/src/lib/Packet/GameTypes.h:44-46`) dont l'unité est donnée par
  `World::GetArTime()` = millisecondes / 10, donc des ticks de 10 ms (`src/World/World.cpp:52-55`) ;
- la seule lecture de la sémantique côté client : `rzclientreconnect` mémorise l'avis et **l'annule** quand
  `unsummon_duration` vaut 0 (`reference/rzu/rzclientreconnect/src/ConnectionToServer.cpp:328-333`) ;
- le client 7.3 connaît le nom (`SGameInterface - MSG_UNSUMMON_NOTICE`, `AUSMSG_UNSUMMON_NOTICE`) — mais,
  point à ne pas confondre, `MSG_UNSUMMON_NOTICE` **n'apparaît pas** dans la liste des `case MSG_*` du
  répartiteur statique de `SFrame.exe`, contrairement à `MSG_UNSUMMON` (§1). La politique de renvoi (durée,
  qui la déclenche, ce qu'elle annonce) n'a donc **aucune source** : voir `NON ÉTABLI` 9.

## 6. Chaîne de persistance

Question posée : quel champ porte quoi, et par quel service. Réponse courte : **les champs existent, aucun
service ne les écrit ni ne les lit** — et l'invocation n'a pas de position à persister (§5.2).

| notre champ | ce que la référence y met | source NGemity | écrit par, aujourd'hui |
|---|---|---|---|
| `CharacterEntity.MainSummonId` | le `sid` de la ligne `Summon` (et **non** un handle) | `Player.cpp:903` (`m_pMainSummon->GetInt32Value(UNIT_FIELD_UID)`) ; relu `Player.cpp:248-250` | rien — FK déclarée `TelecasterContext.cs:114-116` |
| `CharacterEntity.SummonSlotItemIds[6]` | les 6 `sid` des invocations liées aux 6 cartes ; **pas** des identifiants d'objet | `Player.cpp:901-902` (boucle sur `m_aBindSummonCard`, écrit `summon->m_pSummon->UNIT_FIELD_UID`) | rien — seul `HasMaxLength(6)` (`TelecasterContext.cs:132`) |
| `CharacterEntity.SubSummonId` | 0 | `Player.cpp:904` | rien |
| `CharacterEntity.PetId` | 0 | `Player.cpp:905` | rien |
| `CharacterEntity.RemainSummonTime` | `PLAYER_FIELD_REMAIN_SUMMON_TIME` | `Object.h:119` ← `Player.cpp:204` | rien |
| `SummonEntity.SummonResourceId` | `SummonResource.id` : **la même valeur** que `code` (301) et `summon_code` (3) | `Summon.cpp:88-91` + `ObjectMgr.cpp:1147-1190` ; 301 : `Messages.cpp:108` ; 3 : `Summon.cpp:35` | rien — **aucune FK** vers `Arcadia.SummonResource` (`TelecasterContext.cs` ne lie pas `SummonResourceId`) |
| `SummonEntity.CardItemId` | l'objet-carte | `Summon.cpp:137` (`card_uid`) ; FK `TelecasterContext.cs:41-44` | rien |
| `SummonEntity.Name`, `Lv`, `Hp`, `Mp` | la ligne `Summon` | `Player.cpp:720-728` | rien |

Ce que la référence nomme `code` (`Summon::GetSummonCode()`, `Summon.cpp:88-91`) est donc
`SummonResource.id` — NGemity le tient dans `_summonResourceStore[id]` après un
`SELECT id, type, magic_type, ... FROM SummonResource` (`ObjectMgr.cpp:1147-1190`) — et c'est **la même
valeur** qui part dans le `summon_code` de la trame d'entrée (`Summon.cpp:35`). Cela **rétrécit** le
`NON ÉTABLI` 4 du socle (le `code` fourni par l'appelant) sans le fermer complètement : notre dépôt ne
charge `SummonResource` nulle part (`ArcadiaContext.cs:17` déclare le `DbSet`, aucun repository ni service
ne le lit) et `SummonEntity.SummonResourceId` n'a pas de FK, donc rien ne garantit que la colonne porte bien
cet id (voir `NON ÉTABLI` 4).

**Aucun service ne référence `SummonEntity` hors de la couche d'accès aux données** : le seul fichier de code
qui en parle est `Game/Network/Packets/Game/GameSummonPackets.cs:71` (le constructeur de 301). `CharacterService`,
`WorldObjectStreamer`, `GameActions` n'y touchent pas. Les points d'écriture à imiter quand le dev câblera la
persistance, tous dans NGemity : la liaison d'une carte crée l'invocation et l'insère
(`WorldSession::onEquipSummon`, `src/Network/GameNetwork/WorldSession.cpp:986-1005`) ; `AddSummon` /
`RemoveSummon` mettent à jour la ligne `Summon` (`Player.cpp:1496-1517` → `Summon::DB_UpdateSummon`,
`Summon.cpp:98-125`) ; `Player::Save` écrit les colonnes d'invocation du personnage (`Player.cpp:901-905`).
**Quand** notre serveur écrit (à la liaison, au logout, périodiquement) est une décision de jeu : voir
`A VERIFIER PAR KILLIAN`.

## 7. Écarts assumés avec NGemity, et pourquoi

| écart | référence | notre décision, et pourquoi |
|---|---|---|
| `enhance` à 0 | NGemity laisse le champ à zéro (`Summon.cpp:32-37`) et n'a pas de colonne équivalente | nous transmettons aussi 0, mais le dev le prend **en paramètre** : le client 7.3 porte `db_creatureenhance.rdb`, un futur niveau d'amélioration doit pouvoir le remplir sans retoucher la trame |
| `is_first_enter` | NGemity pose `STATUS_FIRST_ENTER` autour de l'ajout au monde (`World.cpp:417-423`) → **1** à l'entrée | nos constructeurs de créature écrivent **0** en dur (`GameSpawnPackets.cs:168`). Pour l'invocation je retiens 1 à la première entrée et 0 en rentrée, avec la source NGemity — réserve `NON ÉTABLI` 8 |
| `max_hp` / `max_mp` | NGemity les calcule (`Unit::CalculateStat`, `CalculateStat.cpp:29`) | notre dépôt n'a **aucun** calcul de statistiques d'invocation : la fiche interdit de réutiliser `hp` en `max_hp` (raccourci déjà présent `GameSpawnPackets.cs:161-164` pour PNJ/monstre). Le dev prend les deux valeurs en paramètres, comme `code` et `summon_handle` l'ont été au socle |
| `face_direction` | orientation de l'unité (`Unit.cpp:111`) | notre dépôt n'a pas d'orientation d'invocation ; paramètre d'appel, 0f accepté comme pour le monstre (`GameSpawnPackets.cs:35`) |
| envoi direct de 305 au maître | `Player.cpp:1607-1611` | à porter tel quel : c'est une correction de visibilité, pas une préférence |
| bruit de 70 / seuil de 24 unités | `Skill.cpp:640-646` | à porter pour l'invocation par sort ; le seuil de 24 unités est une constante interne de NGemity, pas une valeur retail établie (`NON ÉTABLI` 10) |

Ce que je **ne** porte pas : les `limit_*` et le `break` manquant de `SRT_ADD_HP` ne concernent pas ce chemin
(pièges documentés `CLAUDE.md`), et je ne recopie aucune des routines de combat/mouvement de NGemity.

## 8. NON ÉTABLI

1. **`objType = 4` n'est pas prouvé dans le binaire 7.3 lui-même.** L'accord de rzu (`TS_SC_ENTER.h:17`) et
   de NGemity (`Object.h:37`) est fort et c'est la doctrine du dépôt pour ce champ, mais `SFrame.exe` n'a été
   lu que par `strings` : aucun désassemblage. Question précise : *le client 7.3 route-t-il bien l'`objType` 4
   vers sa classe de créature (`SGameCreature` / `SGameLocalCreature`) et non vers une branche morte ?*
2. **Position exacte de la colonne `SummonSlotItemIds`.** Le nom et le commentaire de
   `CharacterEntity.cs:63` disent « item id ou item resource id » ; la seule référence écrit le `sid` de
   l'invocation (`Player.cpp:901-902`). Question : *les 6 colonnes de `Character` portent-elles le `sid` de
   `Summon` (référence) ou l'identifiant de la carte ?* Tant que ce n'est pas tranché, le dev n'écrit rien
   dans ce champ.
3. **Quel id alimente `SummonEntity.SummonResourceId` en pratique.** La référence et la trame convergent sur
   `SummonResource.id`, mais aucune FK ne l'impose (`TelecasterContext.cs`) et aucun service ne charge
   `SummonResource`. Question : *les lignes `Summon` en base portent-elles bien un `SummonResource.id`, et
   faut-il ajouter la FK ?*
4. **L'invocation par le client 7.3 : 304 ou sort 400 ?** `TS_CS_SUMMON` est marqué « Seems unused »
   (`TS_CS_SUMMON.h:5`) et NGemity ne le gère pas. `NON ÉTABLI` 3 du socle reste ouvert.
5. **`max_hp` / `max_mp` d'une invocation en 7.3.** Ni colonne de `SummonEntity` ni calcul dans notre dépôt ;
   NGemity passe par `Unit::CalculateStat` + `SummonLevelResource`/`SummonLevelBonus`
   (`ObjectMgr.cpp:904` et `:925`). Question : *quelle formule de PV/PM maximaux pour notre table `SummonResource`
   (colonne `stat_id`) ?*
6. **Le `z` de l'invocation.** NGemity envoie le `z` de l'invocation elle-même, jamais celui du maître : sa
   position est posée par `SetCurrentXY(x, y)` (`Object.h:370`), qui ne touche pas `m_positionZ`, initialisé à
   zéro (`Object.h:364`). Il enverrait donc `z = 0`. Question : *le client 7.3 accepte-t-il `z = 0` pour une
   invocation (le sol est-il reconstruit localement, comme pour les décors) ou faut-il le `z` du maître ?*
   À vérifier en jeu ; je ne le devine pas.
7. **`enhance` : que vaut-il en 7.3 ?** Le client 7.3 lit `db_CreatureEnhance.rdb` mais NGemity envoie 0
   (`Summon.cpp:32-37`) et notre base n'a pas de source. Question : *un familier de 7.3 peut-il être amélioré,
   et le niveau vient-il de la carte ou de la ligne `Summon` ?*
8. **`is_first_enter` et le choix d'animation.** Le client 7.3 porte deux travaux distincts,
   `SWorkSummonCall` et `SWorkSummonReCall` (chaînes de `SFrame.exe`), et l'effet
   `SGameSummonCreatureEffect` ; je lis dans le drapeau le discriminant « première invocation / rappel », ce
   qui reste une interprétation. Question : *1 à la première entrée et 0 en rentrée — le client joue-t-il bien
   l'effet d'invocation dans le premier cas ?*
9. **Politique de renvoi (306).** Aucun émetteur dans NGemity ; seul `RemainSummonTime` est chargé et jamais
   relu (`Player.cpp:204`, `Object.h:119`). Questions : *la durée d'invocation d'un familier de 7.3 est-elle
   bornée, dans quelle unité, et `unsummon_duration = 0` annule-t-il bien l'avis auprès de ce client (lecture
   `rzclientreconnect` seule) ?*
10. **Le seuil de 24 unités et l'amplitude du bruit.** Constantes internes à NGemity
    (`Skill.cpp:644-646`), non confirmées par une source retail.

Aucune de ces dix questions n'a été comblée par une constante plausible : là où la valeur manque, la fiche
dit « paramètre d'appel » ou « à vérifier en jeu ».

## 9. Bloc destiné à `CLAUDE.md`

Bloc **mis à jour après implémentation en §13.7** : le texte ci-dessous est celui de l'archéologue, conservé
pour l'historique — la version à coller dans la MR est celle de §13.7.

À coller par la QA dans la description de la MR (le dev n'écrit pas `CLAUDE.md`, protégé). Style et niveau de
détail alignés sur le paragraphe « The summon socle's server-to-client layouts… » (`CLAUDE.md:122-130`).

```markdown
A summon enters the world as `TS_SC_ENTER` (`3`) with `type = ET_NPC (1)` and `objType = EOT_Summon (4)` —
the same rzu authority that fixes 1/2/3/6 for npc/item/monster/field prop, corroborated by NGemity's
`SubType` mapping (`Object.cpp:381`, `Object.h:37`). The packet is **96 bytes**: the 26-byte creature prefix,
the 38-byte shared creature payload, then `master_handle` u32 @64, `summon_code` as an 8-byte randomized
`EncodedInt` @68, the 19-byte name @76 (18 usable, zero padded) and `enhance` (Epic >= 7.1) @95. `race`,
`skin_color` and `energy` are 0 — nothing sets them for a summon. `summon_code` is `SummonResource.id`
(`Summon.cpp:35,88-91`), the same value as `code` in `TS_SC_ADD_SUMMON_INFO`; emit 301 first (it fills the
creature window) and `3` after (it puts the object in the world). A summon leaves with `TS_SC_UNSUMMON`
(`305`, 11 bytes) broadcast to its region — plus a direct copy to its master when the master is out of view —
followed by `TS_SC_LEAVE` (`9`) from `RemoveObjectFromWorld`. Its position is never persisted: it is the
master's position at world-entry time plus a bounded jitter (`AddNoise`: 70 on summon, 50 on login, 35 on
warp), and the position the client sends is deliberately ignored for `EF_SUMMON` (`Skill.cpp:584-585`).
No service writes `MainSummonId`/`SummonSlotItemIds` yet, and the reference stores *summon* sids in those
six columns, not card ids.
```

## 10. Tests d'offsets attendus (forme)

Le paquet est **serveur → client** : pas d'entrée dans `GameClient.Receive`, mais les tests d'offsets restent
la discipline du dépôt (`docs/packet-specs/*`, `Tests/Game/GameSummonPacketsTests.cs` pour le style). Sont
attendus, sur la trame d'entrée de l'invocation :

1. **taille totale** : `Assert.Equal(96, packet.Length)` et `BitConverter.ToUInt32(packet, 0) == 96` ;
2. **en-tête** : id `3` @4 (`ushort`), checksum @6 = somme des octets 0..5 ;
3. **préfixe** : `packet[7] == 1` (type), handle @8, `x` @12, `y` @16, `z` @20 (`float` little-endian),
   `packet[24] == layer`, `packet[25] == 4` (`EOT_Summon`) ;
4. **charge commune** : `status` @26, `face_direction` @30, `hp` @34, `max_hp` @38, `mp` @42, `max_mp` @46,
   `level` @50, `packet[54] == race`, `skin_color` @55, `packet[59] == is_first_enter`, `energy` @60 ;
5. **charge propre** : `master_handle` @64, les 8 octets du `summon_code` @68 (encodage aléatoire : comparer
   la valeur décodée, pas les octets bruts), les 19 octets du nom @76 (dont le remplissage à zéro au-delà de
   18 caractères, comme `GameSummonPackets.WriteName`), `packet[95] == enhance` ;
6. **bornes** : aucun champ au-delà de l'octet 95, et un nom de 18 caractères ne déborde pas sur 95 ;
7. **sortie** : 305 = 11 octets, `summon_handle` @7 ; 306 = 15 octets, `summon_handle` @7 et
   `unsummon_duration` @11 ; 9 = 11 octets, `handle` @7.

Le gating de version n'a pas à être testé en 7.3 (les branches écartées en §4 ne sont pas écrites), mais
chaque largeur décidée en §4 doit apparaître dans ces assertions.

## 11. Collisions de fichiers — nommées, non résolues

- `Game/Network/Packets/Enums/GamePackets.cs` : **16** branches `hermes/packet-*` locales le modifient par
  rapport à `master` (mesuré : `git diff --name-only master...<branche> -- Game/Network/Packets/Enums/GamePackets.cs`
  sur les branches `hermes/packet*`). Cette fiche n'a **rien** à y ajouter : `TM_SC_ENTER` y est déjà
  (`GamePackets.cs:9`), et l'invocation n'a pas d'id propre (§1). Collision évitée par construction.
- `Game/Network/Clients/GameClient.cs` : les **mêmes 16** branches y touchent. Cette fiche n'exige **aucun**
  bras de dispatch (§1), donc aucun ajout ici non plus.
- `Game/Network/Packets/Game/GameSummonPackets.cs` : le socle d'invocations y est déjà, avec deux MR
  ouvertes de la même famille (324, cartes). Le nouveau constructeur d'entrée a sa place dans
  `GameSpawnPackets.cs` (à côté de `BuildEnterMonster`), pas dans `GameSummonPackets.cs` — décision de
  rangement, pas de protocole.

## 12. Commits épinglés

| référence | commit | usage dans cette fiche |
|---|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | tailles, offsets, gating (`TS_SC_ENTER.h`, `TS_SC_UNSUMMON.h`, `TS_SC_UNSUMMON_NOTICE.h`, `TS_SC_LEAVE.h`, `TS_EQUIP_SUMMON.h`, `TS_CS_SUMMON.h`, `EncodingRandomized.h`, `TS_SC_STATUS_CHANGE.h`), lecture de `rzclientreconnect` |
| `reference/ngemity/Chihiro` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | logique (`Player.cpp`, `Skill.cpp`, `World.cpp`, `Object.cpp`, `Unit.cpp`, `Summon.cpp`, `Messages.cpp`, `ObjectMgr.cpp`, `WorldSession.cpp`) |
| `reference/client73/SFrame.exe` | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | noms de paquets, répartiteur statique, classes de créature et effets d'invocation, tables `db_CreatureEnhance.rdb` |
| `reference/client73/db_creature.rdb` | sha256 `268e7cccd8a30d092140ffa6686c8e4683ad9eaa2f17d55859695845eede02a8` | ressource d'invocation du client : en-tête de 132 octets finissant par le nombre d'enregistrements (141), puis 141 enregistrements de 950 octets ; le premier porte l'id 1401 et le nom `spirit_ent_lv1` à +112 |
| `reference/client73/db_creatureenhance.rdb` | sha256 `1e5bf9d3b3bc69ef6e518f8af94a9d07e3bb83200e8bef18a822aaef45177044` | le champ `enhance` (`>= EPIC_7_1`) existe bien côté client 7.3 |
| `Navislamia` (base de la branche) | `5ef086eab3cbd9f8af8e2982968fc30b0db5ac57` | `master` au moment de la rédaction |

## 13. Implémentation livrée (dev)

### 13.1 Ce qui est écrit, et où

| fichier | ajout | ancrage |
|---|---|---|
| `Game/Network/Packets/Game/GameSpawnPackets.cs` | `BuildEnterSummon` — la trame de §3.1, 96 octets | juste après `BuildEnterMonster` |
| idem | `NameSize` (= `GameSummonPackets.NameSize`, 19) et `WriteName` : une seule largeur, un seul writer | en tête de classe / à côté de `WriteChecksum` |
| idem | `BuildEnterCreature` : `maxHp`, `mp`, `maxMp`, `isFirstEnter` deviennent des paramètres optionnels | le bloc de 38 octets n'est écrit qu'une fois |
| `Game/Network/Packets/Game/ActorStatus.cs` | `ForSummon()` → `0` | le composeur unique du masque `status` |
| `Game/Services/SummonWorldEntry.cs` | les champs que §7 laisse « paramètre d'appel » | nouveau fichier |
| `Game/Services/SummonWorldService.cs` | **l'appelant** : `Enter` (301 puis 3), `Leave` (305 puis 9), `Jitter` | nouveau fichier |
| `Tests/Game/SummonWorldTests.cs` | 27 tests | nouveau fichier |

`BuildEnterSummon` écrit exactement §3.1 : `type = 1` @7, `objType = 4` @25, `status = 0` @26, `race = 0` @54,
55-58 à zéro, `energy = 0` @60-63, `is_first_enter` @59, `master_handle` @64, `summon_code` @68 sur huit octets
(`ScrambledInt.Encode`, encodage aléatoire : la valeur se compare **décodée**, pas les octets bruts), `name` @76
sur 19 octets (18 utiles, le reste à zéro), `enhance` @95, plus longueur @0-3, id @4-5 et checksum @6. `max_hp`
@38 et `max_mp` @46 ne recopient pas `hp`/`mp` : ils viennent de l'appelant (§7, `NON ÉTABLI` 5). Les trames
NPC et monstre sortent inchangées octet pour octet : les quatre nouveaux paramètres sont optionnels et leur
défaut reproduit l'ancien comportement (test `BuildEnterSummon_DoesNotDisturbTheNpcAndMonsterTram`).

### 13.2 L'appelant, exactement

`SummonWorldService.Enter(session, clientTag, connection, entry)` — renvoie `0` sans rien émettre si la session,
la connexion ou l'entrée manquent ; alloue le handle par `WorldObjectHandle.Next()` ; émet
`TS_SC_ADD_SUMMON_INFO` (301, `BuildAddSummonInfo`) pour ce handle, puis `TS_SC_ENTER` (3) à
`(session.X + bruit, session.Y + bruit, entry.Z, session.Layer)` avec `master_handle = session.CharacterHandle` ;
renvoie le handle.

`SummonWorldService.Leave(session, clientTag, connection, handle)` — refuse `handle == 0` ; émet
`TS_SC_UNSUMMON` (305) puis `TS_SC_LEAVE` (9), même handle, sur la connexion du maître.

`SummonWorldService.Jitter(range, raw) = raw % range - range/2` en arithmétique entière (`Skill.cpp:640`), soit
`[-range/2, +range/2)` ; `range <= 0` rend `0` au lieu de diviser par zéro (`AddNoise` ferait `r % v`). Paliers :
`SummonNoiseRange = 70`, `LoginNoiseRange = 50`, `WarpNoiseRange = 35` (§5.2).

Rien d'autre n'est décidé : pas de durée de vie, pas de coût, pas de création d'invocation, aucune écriture en
base (§6 et §8 point 2 restent tels quels). Le service n'est injecté nulle part, volontairement : les trois
chemins de §2 exigent une invocation qui existe déjà (`MainSummonId` lu en base, ou une liaison carte↔invocation)
et aucun des deux n'est livré.

### 13.3 Ce qui reste sans appelant, et pourquoi

301, 3 (par `Enter`), 305 et 9 (par `Leave`) ont désormais un appelant. Les cinq autres trames nommées par la
carte restent sans appelant, parce que leur déclencheur est une politique de jeu non tranchée :

| trame | ce qui manque pour l'appeler |
|---|---|
| 302 `TS_SC_REMOVE_SUMMON_INFO` | la règle de déliaison carte↔invocation (`ConnectionToServer.cpp:612`) : autre carte, famille « cartes » |
| 306 `TS_SC_UNSUMMON_NOTICE` | tout : aucun émetteur côté NGemity, durée d'invocation inconnue (`NON ÉTABLI` 9) |
| 307 `TS_SC_SUMMON_EVOLUTION` | le barème d'évolution (`NON ÉTABLI` 11) |
| 320 / 321 `TS_SC_MOUNT_SUMMON` / `TS_SC_UNMOUNT_SUMMON` | les règles de monte et le déclencheur client (§14 points 11) |

Hors lot, comme la carte le fixe : 304, 323, 324, 354, 355, 452.

### 13.4 Écarts assumés par rapport à §5

1. **301 et 3 enchaînés par `Enter`.** La référence les émet à deux moments (301 pendant `SendLoginProperties`,
   3 à l'entrée dans le monde, §5.1) ; NavisLamia n'a pas d'émission « propriétés de login » pour les
   invocations. L'ordre est respecté, le moment ne l'est pas. Quand le chemin de login sera câblé (étape 2 du
   socle, encore en attente d'arbitrage), c'est lui qui portera le 301 et `Enter` devra cesser de l'émettre.
2. **Pas de diffusion à la région.** §5.3 demande un 305 diffusé à la région de l'invocation plus une copie
   directe au maître, puis un 9 diffusé. NavisLamia n'a aucune visibilité joueur↔joueur : seule la copie directe
   au maître — que la référence envoie aussi — et le 9 sont émis. Réserve : un tiers ne verra pas l'invocation
   disparaître.
3. **Pas d'annulation du bruit par région** (§5.2 étape 2) : aucune résolution position → id de lieu n'existe
   (`ConnectionInfo.CurrentLocationId` vaut 0 partout dans le dépôt). Le bruit borné est appliqué sans ce
   garde-fou : une invocation de bord de région peut sortir de la région de son maître.
4. **`name` écrit par le writer ASCII du socle** (`GameSummonPackets.WriteName:198-203`, dupliqué dans
   `GameSpawnPackets.WriteName:262`) : identique octet pour octet au 301 (test croisé), donc un nom non ASCII
   sort en `?` dans les deux trames.
5. **`code`** : un seul champ `int` dans `SummonWorldEntry`, casté en `uint` pour le champ encodé de 3 (§6 dit
   la même valeur). `NON ÉTABLI` 3 n'est pas fermé pour autant.
6. **`master_handle` = `ConnectionInfo.CharacterHandle`**, le handle par lequel le client connaît le joueur.

### 13.5 Lignes citées par la fiche : correspondance ancien → nouveau

`GameSpawnPackets.cs` (base `5ef086e` → commit de ce lot) : `BuildEnterNpc` 18 → 29, `BuildEnterCreature`
145 → 211, `WriteHeader` 174 → 241, `WriteEncodedInt` 180 → 247, `WriteChecksum` 188 → 269 ; `BuildEnterMonster`
31 → 42. `BuildEnterSummon` (84) et `WriteName` (262) sont nouveaux. `GameSummonPackets.cs:198-203` inchangé.
`GameClient.cs` : le `throw new Exception("Unknown Packet Type")` reste à **1358**, aucun bras n'a été ajouté.

### 13.6 Invariant, base de mesure et fusion

- `GamePackets` : **126** membres, dont **60** dans la famille `TM_CS_*`, **0** sans référence dans
  `GameClient.cs` / `GameActions.cs`. Ce lot n'ajoute ni membre ni bras ; l'id de la trame retenue (`3`) est déjà
  déclaré sur `origin/master` (`GamePackets.cs:9`) — contrôle demandé par la carte.
- Base mesurée avant modification : `dotnet build Navislamia.sln -c Debug` → 0 ; `dotnet test Tests/Tests.csproj`
  → 0, **865 réussis / 0 échec**. Après ce lot : build 0, tests 0, **892 réussis / 0 échec** (+27).
- Fusion à blanc (`git merge-tree --write-tree HEAD <branche>`) contre les 16 branches `hermes/*` actives :
  **aucun conflit** sur les cinq fichiers de ce lot. Les zones de conflit récurrentes du dépôt restent
  `GameClient.cs` (10 branches), `GamePackets.cs` (8), `GameActionPackets.cs` (5), `NetworkService.cs` (4),
  `DevConsole/Program.cs` (4), `ConnectionInfo.cs` (1) — ce lot n'y touche pas.

### 13.7 Bloc destiné à `CLAUDE.md`, actualisé

```markdown
A summon enters the world as `TS_SC_ENTER` (`3`) with `type = ET_NPC (1)` and `objType = EOT_Summon (4)` — the
same rzu authority that fixes 1/2/3/6 for npc/item/monster/field prop, corroborated by NGemity's `SubType`
mapping (`Object.cpp:381`, `Object.h:37`). The packet is **96 bytes**: the 26-byte creature prefix, the 38-byte
shared creature payload, then `master_handle` u32 @64, `summon_code` as an 8-byte randomized `EncodedInt` @68,
the 19-byte name @76 (18 usable, zero padded, the writer the creature window already uses) and `enhance`
(Epic >= 7.1) @95. `race`, `skin_color` and `energy` are 0 — nothing sets them for a summon. `max_hp` @38 and
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
```

### 13.8 Vérification client

Ce qu'un œil en jeu doit constater une fois un appelant câblé (§14 point 15) :

- à l'entrée : l'invocation apparaît au sol, nommée, à côté de son maître — jamais exactement dessus sauf
  `NoiseRange = 0` — avec son niveau, ses PV/PM et l'animation d'invocation (`is_first_enter = 1`) ;
- si rien ne s'affiche : vérifier `objType = 4` @25 et le `summon_code` @68 **décodé** (`ScrambledInt.Decode`) —
  c'est le point 1 des `NON ÉTABLI` ;
- à la sortie : l'invocation disparaît sans laisser de modèle fantôme (305 puis 9), et le maître peut la
  rappeler.

## 14. A VERIFIER PAR KILLIAN

Questions qui exigent l'arbitrage de Killian. Aucune n'a reçu de constante inventée : là où la valeur manque, le
code prend un paramètre d'appel et le champ part tel quel.

1. **`objType = 4` dans le client 7.3 lui-même** (`NON ÉTABLI` 1) : accord rzu + NGemity, jamais désassemblé.
2. **Colonnes `MainSummonId` / `SummonSlotItemIds`** (`NON ÉTABLI` 2) : sid d'invocation ou id de carte ? Ce lot
   n'y écrit rien.
3. **`SummonEntity.SummonResourceId` porte-t-il un `SummonResource.id`** (`NON ÉTABLI` 3) : c'est la valeur que
   `code` / `summon_code` attendent, et elle reste fournie par l'appelant.
4. **Chemin d'invocation du client 7.3 : 304 ou sort 400** (`NON ÉTABLI` 4) : décide de l'appelant de `Enter`.
5. **`max_hp` / `max_mp` d'une invocation** (`NON ÉTABLI` 5) : paramètres d'appel, aucune formule retenue.
6. **`z` de l'invocation** (`NON ÉTABLI` 6) : 0 (NGemity) ou `z` du maître ; paramètre d'appel, à voir en jeu.
7. **`enhance` en 7.3** (`NON ÉTABLI` 7) : paramètre d'appel, 0 tant qu'aucune source n'existe.
8. **`is_first_enter`** (`NON ÉTABLI` 8) : posé par l'appelant (1 première entrée, 0 rentrée), lecture à
   confirmer visuellement.
9. **Durée et renvoi (306)** (`NON ÉTABLI` 9) : sans elle, la trame n'a pas d'appelant.
10. **Seuil de 24 unités et amplitude du bruit** (`NON ÉTABLI` 10) : les trois paliers 70/50/35 sont portés tels
    quels ; le seuil de 24 unités n'est pas utilisé (il sert au refus « invocation au mauvais endroit »).
11. **Barème d'évolution (307)** (`NON ÉTABLI` 11) et **règles de monte (320/321)** : sans elles, ces trames
    restent sans appelant.
12. **301 et 3 dans un même appel** (§13.4 point 1) : accepter l'enchaînement tant que le login n'émet pas de
    301, ou exiger deux points d'appel dès maintenant ?
13. **Diffusion à la région** (§13.4 point 2) : NavisLamia n'a aucune visibilité joueur↔joueur — accepter la
    copie directe au maître seule, ou traiter la visibilité entre joueurs comme le prochain socle ?
14. **Noms non ASCII** (§13.4 point 4) : garder le writer ASCII du socle pour 301 et 3, ou passer les deux
    trames à UTF-8 d'un seul geste ?
15. **Où appeler `Enter` / `Leave`** : login (`MainSummonId`), sort d'invocation (304/400) ou warp — les trois
    exigent d'abord les points 2 à 4.
