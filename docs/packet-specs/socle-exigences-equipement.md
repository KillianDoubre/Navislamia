# Socle — exigences d'équipement (niveau, métier, race)

> **Mise à jour du 2 octobre 2026** : le lot 2 est livré et testé (§11). Les sept colonnes `limit_*`
> alimentent désormais les masques existants ; les équipements 200 et 281 vérifient race, classe et
> profondeur de métier. Les réserves historiques de données ci-dessous sont remplacées par §11.

| | |
|---|---|
| Paquets | aucun nouveau : la règle s'applique derrière `TM_CS_PUTON_ITEM` (200), `TM_CS_PUTON_ITEM_SET` (281) et `TM_CS_SWAP_EQUIP` (223) ; le refus passe par `TM_SC_RESULT` (0), code **5** (`NotActable`) |
| Référence d'autorité | serveur officiel `CaptainHerlockServer.exe` (dump Epic 7 Part 4, branche `gameserver_release/2011-12-12`, construit le 2012-11-22, PDB apparié) |
| Référence secondaire | NGemity/Chihiro (`WorldSession::onPutOnItem`, `Unit::TranslateWearPosition`, `Item::GetLevelLimit`, `GameRule::GetRankLevel`) |
| Données | `reference/epic7part4/csv/ItemResource.csv` (Epic 7 Part 4) ; `reference/sqlserver/Arcadia/JobResource.csv` (9.4) |
| Code | `Game/Services/EquipmentService.cs`, `ItemWearRules.cs`, `ItemWearCatalog.cs`, `Game/Services/QuestRules.cs`, `tools/import_epic7.py` |

Aujourd'hui le serveur équipe **n'importe quoi sur n'importe qui** : ni `EquipmentService.EquipAsync` (200) ni
`EquipSetAsync` (281) ne lit `rank`, `use_min_level`, `use_max_level`, `limit_*` ou `job_depth`. La fiche
établit la règle du serveur officiel, l'état des données, et le découpage : **le plancher de niveau est
faisable tout de suite, la race/le métier attendent un prérequis de données.**

## 1. Identité

- `TM_CS_PUTON_ITEM` = **200** (`op_codes.md:50`) ; rzu : `X(200, version < EPIC_9_6_3)`,
  `X(1200, version >= EPIC_9_6_3)` (`reference/rzu/librzu/src/packets/GameClient/TS_CS_PUTON_ITEM.h:14-16`)
  → **à 7.3 c'est 200**.
- Paquets parents de la règle : `TM_CS_PUTON_ITEM_SET` = 281 (`op_codes.md:95`), `TM_CS_SWAP_EQUIP` = 223
  (`op_codes.md:72`, `TS_CS_SWAP_EQUIP.h:10-12`).
- Refus : `TM_SC_RESULT` = 0 (`op_codes.md:2`), corps de 8 octets — `request_msg_id` u16 @0, `result` u16 @2,
  `value` i32 @4 (`reference/rzu/librzu/src/packets/GameClient/TS_SC_RESULT.h:8-14`) ; miroir dépôt
  `Game/Network/Packets/Game/TS_SC_RESULT.cs:6-17`, émission `GameClient.SendResult`
  (`Game/Network/Clients/GameClient.cs:68-72`).

## 2. Ce que le joueur fait

Double-clic sur un objet de l'inventaire, ou glisser-déposer sur la poupée d'équipement : le client envoie
la 200 avec **la position visée, le handle de l'objet et le handle de la cible**. Le PDB client
(`client-pdb/2011-12-14-part4-design/SFrame_Release.symbols.tsv`) ne nomme qu'un seul émetteur,
`SGameObject::Rq_PutOnOffItem` / `SGameSystem::Rq_PutOnOffItem` / `SGameWorld::Rq_PutOnOffItem` : la même
fonction sert les deux sens (équiper / retirer). Côté serveur officiel, le récepteur est
`onPutonItem` (`CaptainHerlockServer.exe` `0x14010f950`).

À l'entrée en jeu, le client rééquipe la panoplie entière par la **281** (fiche `281-puton-item-set.md`) :
c'est le deuxième chemin que la règle doit couvrir, sinon un personnage de niveau 5 équipe sa panoplie de
niveau 160 par reconnexion.

## 3. Structure sur le fil

`TM_CS_PUTON_ITEM` (200) à Epic 7.3 — corps de **9 octets**, trame de **16 octets** avec l'en-tête client
de 7 (`Game/Network/Packets/Game/GameActionPackets.cs:10`) :

| Offset | Type | Nom | Source |
|---|---|---|---|
| 0-6 | — | en-tête client (7 octets) | `GameActionPackets.cs:10` |
| 7 | int8 (`sbyte`) | `position` | rzu `TS_CS_PUTON_ITEM.h:8-9` (`version < EPIC_9_6_7`) ; `GameActionPackets.cs:386` |
| 8 | uint32 | `item_handle` | rzu `TS_CS_PUTON_ITEM.h:10` ; `GameActionPackets.cs:387` |
| 12 | uint32 | `target_handle` | rzu `TS_CS_PUTON_ITEM.h:11` ; `GameActionPackets.cs:388` |

**Taille totale attendue : 16 octets** (`HeaderSize + 9`, `GameActionPackets.cs:378`). Contre-épreuve sur le
binaire officiel : `onPutonItem` lit l'objet à `[packet+0x8]` (`0x14010f9bd`) et la cible à `[packet+0xc]`
(`0x14010fa44`), donc `position` occupe bien l'octet 7. Le dépôt **accepte les trames plus
longues** et n'ignore que les plus courtes (`packet.Length < packetLength`, `GameActionPackets.cs:379`) —
c'est le comportement établi, ne pas le changer sans mesure.

Aucune trame réelle n'a été mesurée ici (aucun client exécutable sur ce VPS, pas de capture) : les types et
tailles viennent de rzu et du PDB, pas d'une observation.

Les autres chemins portent la même règle sans la décrire : 281 = 24 handles sans position (fiche
`281-puton-item-set.md`), 223 = aucun corps (`TS_CS_SWAP_EQUIP.h:7`).

## 4. Gating de version

| Champ | Gating rzu | Décision pour 7.3 |
|---|---|---|
| id du paquet | `X(200, version < EPIC_9_6_3)` / `X(1200, version >= EPIC_9_6_3)` (`TS_CS_PUTON_ITEM.h:15-16`) | **200** |
| ordre `position` / handles | `_(impl)(simple)(int8_t, position, version < EPIC_9_6_7)` puis réimplémenté **en dernier** (`TS_CS_PUTON_ITEM.h:8-12`) | **`position` en premier** (offset 7), déjà en place (`GameActionPackets.cs:386-388`) |
| id de `TM_SC_RESULT` | `X(0, version < EPIC_9_6_3)` / `X(1000, ...)` (`TS_SC_RESULT.h:12-14`) | **0** |
| `rank`, `limit_*`, `job_depth`, `use_min_level`, `use_max_level`, `target_*` | **aucun** : ce ne sont pas des champs de fil, rzu ne les décrit pas (rzu est une bibliothèque de paquets) | arbitrés par le binaire officiel **de la même époque** (build 2012-11-22), pas par rzu |

Le piège à ne pas hériter : à partir d'`EPIC_9_6_7` la `position` passe **après** les handles. Toute reprise
de cette fiche pour un épisode supérieur doit relire `TS_CS_PUTON_ITEM.h`, pas recopier l'offset 7.

## 5. Traitement attendu

### 5.1 Les contrôles du serveur officiel, dans l'ordre

| # | Contrôle | Refus | Source (désassemblage) |
|---|---|---|---|
| 1 | joueur non actionnable (virtuelle `[vtable+0x218]` fausse) | 5 `NotActable` | `onPutonItem` `0x14010f950` : `0x14010f99a`, `mov r8w,0x5` `0x14010f9a7` |
| 2 | objet inconnu du gestionnaire d'objets (`GetObject` `0x1400ad020` rend null, log `onPutonItem() : ...`) | 1 `NotExist` | `onPutonItem` `0x14010f9bd-0x14010f9e8` |
| 3 | `StructItem::IsWearable()` faux : `wear_type == -1`, bit 3 (`ITEM_FLAG_FAILED`) de l'octet d'instance `[item+0x15c]`, ou durabilité à 0 | 6 `AccessDenied` | `StructItem::IsWearable` `0x1400adab0` ; `ItemBase::ITEM_FLAG` (`FAILED = 3`) |
| 4 | objet hors inventaire (`[item+0x178] != 0` ou `[item+0x118] == 0`) | 6 | `StructCreature::Puton` `0x14008fea0` |
| 5 | objet **déjà porté** (`[item+0x164] != -1`) | 5 | `StructCreature::Puton` `0x14008feed` |
| 6 | `TranslateWearPosition` faux — **c'est ici que vivent les exigences** | 5 | idem, `0x14008ff2d` |
| — | succès : `putonItem(position, item)`, recalcul des stats, `TS_SC_WEAR_INFO` | 0 | `StructCreature::Puton` + `StructPlayer::putonItem` `0x1400f4750` |

**Le refus d'une exigence non satisfaite est toujours 5 (`NotActable`)** : la traduction de la position
renvoie un booléen, `Puton` le convertit en 5 (`0x14008ff2d`). Les codes `LimitMin` (17), `LimitMax` (16),
`LimitRace` (24), `LimitJob` (25) existent dans l'énumération (`RESULT_*`, PDB) mais **ne sont pas ceux du
port d'objet** : NGemity s'en sert pour l'*utilisation* d'un objet (`Player::IsUseableItem`), pas pour le
port.

### 5.2 Le plancher de niveau (classe de base)

`StructCreature::TranslateWearPosition` `0x140080e40`, dans cet ordre :

1. `wear_type (ItemBase+0x64) == -1` → faux (`0x140080e57`, sortie `0x140080e69`) ;
2. `!IsWearable()` → faux (`call 0x1400adab0` en `0x140080e6d`, sortie fausse `0x140080e5d`) ;
3. `GetLevelLimit() > max(GetLevel(), m_nUnitExpertLevel)` → faux : le maximum est `[creature+0x1c4]`
   (`m_nUnitExpertLevel`, PDB `StructCreature`, offset 452) contre la virtuelle `[vtable+0x90]` =
   `StructCreature::GetLevel` (`0x1400a50c0`, slot confirmé dans la vftable `0x140406268`) ; test au
   `jg` `0x140080ea9` (sortie fausse `0x140080ee7`) ;
4. `use_min_level (ItemBase+0x38) != 0 && GetLevel() < use_min_level` → faux (`0x140080eb2`, `0x140080ec4`) ;
5. `use_max_level (ItemBase+0x3c) != 0 && GetLevel() > use_max_level` → faux (`0x140080ed0`, `0x140080ee2`) ;
6. sinon vrai (`0x140080ef8`).

`StructItem::GetLevelLimit` (`0x1400ad250`) = **`max(plancherDeRang(rank), nMinLevel)`** : il lit `nRank`
(`ItemBase+0x20`), appelle la table des rangs (`0x140200f10`, table de 8 entiers à `0x1404f7fd8`) puis prend
le plus grand des deux.

**Table des rangs, valeurs du binaire officiel** (`0x1404f7fd8`, index = rang clampé 1..8) :

| rang | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|---|
| plancher | 0 | 0 | 20 | 50 | 80 | 100 | 120 | 150 | **170** |

NGemity donne la même table **sauf le rang 8 : 180** (`GameRule.cpp:102-103`). L'officiel tranche : 170.

Le champ `level` (`ItemBase+0x24`) **n'entre pas** dans le port : dans les données Epic 7.3 il ne prend que
trois valeurs (1 : 26 941 lignes, 10 : 2 489, 6 : 217). C'est le niveau d'objet qui alimente le niveau
*recommandé* de l'infobulle (`GameRule::GetItemRecommendedLevel`, `GameRule.cpp:114-123`), pas une exigence.

### 5.3 Race, classe et profondeur (classe `StructPlayer`)

`StructPlayer::TranslateWearPosition` `0x1400db120` (3 104 octets), après avoir appelé la version
`StructCreature` :

1. bit 0 (`ITEM_FLAG_CARD`) de l'octet d'instance `[item+0x15c]` → faux (`0x1400db171`) ;
2. **classe** — `bl = 0` ; pour chacun des quatre bits de `nLimit` (`ItemBase+0x68`) : `LIMIT_HUNTER` (bit 11,
   `0x800`) → `IsHunter` (`[vtable+0x408]`, `0x1400c0210`), `LIMIT_FIGHTER` (bit 10, `0x400`) → `IsFighter`
   (`0x410`), `LIMIT_MAGICIAN` (bit 12, `0x1000`) → `IsMagician` (`0x418`), `LIMIT_SUMMONER` (bit 13, `0x2000`)
   → `IsSummoner` (`0x420`) ; si le bit est posé et le prédicat vrai, `bl = 1`. **Un objet dont aucun des
   quatre bits n'est posé refuse tout le monde** (`0x1400db218` : `test bl,bl` / `je -> faux`) ;
3. **race** — `GetRace()` (`[vtable+0x160]`, `StructPlayer::GetRace`) : `3` (GAIA) exige le bit `0x10`,
   `4` (DEVA) le bit `0x4`, `5` (ASURA) le bit `0x8` ; bit absent → faux (`0x1400db22d-0x1400db280`) ;
4. **profondeur de métier** — `bt item.JobDepth (ItemBase+0x34), GetJobDepth() ([vtable+0xd8])` ; bit absent
   → faux (`0x1400db29a-0x1400db2a1`).

**Les deux listes sont des listes blanches** : « le bit de mon camp doit être posé », et un champ à zéro
refuse tout le monde. C'est le même sens que le précédent déjà écrit pour les props de terrain
(`CLAUDE.md:1068-1075`), et l'inverse de `NGemity` (voir §6).

Chaînes d'énumération relevées dans le PDB (elles tranchent les valeurs) :

- `ItemBase::LIMIT_FLAG` : `LIMIT_DEVA = 4`, `LIMIT_ASURA = 8`, `LIMIT_GAIA = 16`, `LIMIT_FIGHTER = 1024`,
  `LIMIT_HUNTER = 2048`, `LIMIT_MAGICIAN = 4096`, `LIMIT_SUMMONER = 8192` ;
- `JobInfo::<unnamed-tag>` (classe) : `FIGHTER = 1`, `HUNTER = 2`, `MAGICIAN = 3`, `SUMMONER = 4` ;
- `JobInfo::<unnamed-tag>` (race de `GetRace`) : `GAIA = 3`, `DEVA = 4`, `ASURA = 5` ; métiers de base
  `GAIA_BASIC_JOB = 100`, `DEVA_BASIC_JOB = 200`, `ASURA_BASIC_JOB = 300`.

Les quatre prédicats se lisent entièrement (`0x1400c0210` = `IsHunter`, `0x1400c0280` = `IsFighter`,
`0x1400c02f0` = `IsMagician`, `0x1400c0360` = `IsSummoner`) : ils prennent `GetJobId()` (`[vtable+0xb8]`,
slot 184 du PDB), et si elle rend 0 le **métier de base de la race** (100/200/300), puis lisent
`JobInfo.job_class` (`JobResource`, octet 12) en le comparant à 1/2/3/4. Autrement dit la classe d'un
personnage se lit dans **`JobResource.job_class`** de son métier courant, exactement la colonne que le
dépôt importe déjà (`JobResourceEntity.JobClass`, `Game/DataAccess/Entities/Arcadia/JobResourceEntity.cs:6`).

### 5.4 Les données Epic 7.3 (valeurs relevées sur `csv/ItemResource.csv`, 29 647 lignes)

| Colonne | Valeurs | Ce que ça dit |
|---|---|---|
| les 7 `limit_*` | tout à 1 : **20 835** ; les 26 combinaisons observées ; tout à 0 : **319** | les 319 lignes tout-à-zéro sont toutes `class=0, type=1, group=130, wear_type=200` (`SummonOnly`) : non portables par construction, cohérent avec « 0 = personne » (liste blanche) |
| `job_depth` | `15` : 28 753 ; `8` : 894 | masque de profondeur (bit 3 = `Master`) ; 894 objets réservés à la 4ᵉ profondeur |
| `use_min_level` | `0` : 25 474 ; `160` : 1 440 ; `155` : 1 437 ; `100` : 355 ; `150` : 304 | l'exigence de niveau réellement portée |
| `rank` | 0 : 5 611, 1 : 4 516, 2 : 2 701, 3 : 2 677, 4 : 2 677, 5 : 2 606, 6 : 2 561, 7 : 6 298 | le plancher de rang **n'est pas décoratif** : 3 427 lignes ont rang 7 et `use_min_level = 0` (plancher 150 à lui seul) |
| `use_max_level` | `0` : 26 206 ; `300` : 3 415 | `300` sert de « pas de plafond » de fait |
| `wear_type` | `-1` : 3 967 ; `99` : 3 517 ; `100` : 2 505 ; `200` : 319 | `-1` = `CantWear`, déjà refusé ; `200` = invocation seulement |

Les 7 colonnes `limit_*` sont des **chaînes** dans la source (`limit_deva` … `limit_summoner` typées
`Korean_Wansung_CI_AS` dans `reference/epic7part4/rdu/ItemResource.rdu`), d'où les comparaisons
`!= "0"` de l'ancien chemin 9.4 (`MigrateDatabase/Worker.cs:686-720`).

### 5.5 État du dépôt

- 200 : `GameClient.HandlePutonItemAsync` (`GameClient.cs:1329-1345`) → `EquipmentService.EquipAsync`
  (`EquipmentService.cs:39-65`) → `EquipAtSlotAsync` (`:212-242`) → `CharacterService.EquipItemAsync`
  (`CharacterService.cs:252-278`).
- 281 : `EquipmentService.EquipSetAsync` (`:74-130`) → `ResolveSlotAsync` (`:190-205`, le seul autre
  consommateur du catalogue) → le même `EquipAtSlotAsync`. Les deux chemins se rejoignent : **un seul point
  de contrôle suffit à couvrir 200 et 281.**
- Aucune exigence n'est lue. `IItemWearCatalog` ne transporte que le `wear_type`
  (`ItemWearCatalog.cs:18-35`, `IItemWearCatalog.cs:10-18`, projection `ItemResourceRepository.cs:125-131`).
- Ce qu'il faut pour juger existe déjà : côté objet `ItemResourceEntity.cs:17-18` (masques), `:23` `Rank`,
  `:24` `Level`, `:27` `JobDepth`, `:28-31` `UseMinLevel`/`UseMaxLevel`/`Target*` ; côté personnage
  `Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:29` `Race`, `:31` `Lv`, `:32` `MaxReachedLv`,
  `:39` `CurrentJob`, `:41` `JobDepth`, `:42` `Jlv`.
- **La règle de profondeur est déjà écrite pour les quêtes** : `QuestRules.cs:59`
  (`LimitJobDepth & (1 << jobDepth) == 0` → refuse) et la conversion drapeaux → index
  `QuestService.cs:73-76, 88` (`1 => 0, 2 => 1, 4 => 2, 8 => 3`). L'équipement doit réutiliser cette règle
  plutôt que la réécrire.

### 5.6 Découpage — lot minimal, puis les suites

**Le socle se scinde en deux :** le plancher de niveau (§5.2) est autonome et sera implémenté sur cette
branche ; la race, la classe et la profondeur (§5.3) dépendent d'un prérequis de données et **ne peuvent pas
partir avant** que les masques soient remplis. C'est le PO qui scinde la carte Trello — pas l'archéologue.

**Lot 1 — le plancher de niveau. Autonome, à faire maintenant.**

1. `IsWearAllowed` (à côté de `ItemWearRules`, qui est déjà le domicile des règles de port) appliquant, dans
   l'ordre du §5.2 : `wear_type == -1` → refus ; `max(plancherDeRang(rank), use_min_level) > max(niveau,
   niveau expert)` → refus ; `use_min_level != 0 && niveau < use_min_level` → refus ;
   `use_max_level != 0 && niveau > use_max_level` → refus. Table des rangs = **constante de code** (officiel,
   §5.2), pas une table en base.
2. Exposition des trois champs : étendre la projection du catalogue (`ItemResourceRepository.cs:125-131` et
   `ItemWearFields`), puis `ItemWearCatalog`, plutôt qu'une lecture en base par équipement.
3. Branchement **avant** `EquipItemAsync`, dans `EquipAsync` **et** `EquipSetAsync`, refus
   `ResultCode.NotActable` (5) — le code du serveur officiel (§5.1). Un objet refusé dans une 281 ne doit pas
   interrompre les suivants (comportement déjà en place, `EquipmentService.cs:81-114`).
4. Tests : table du prédicat (rangs 0..8, `use_min_level` 0/1/20/155/160, `use_max_level` 0/300) **et** la
   suite d'offsets de la 200 (16 octets, `position` à 7) que la discipline du dépôt impose dès qu'un paquet
   est touché.

Pourquoi ce lot est autonome : `rank`, `use_min_level`, `use_max_level` sont **importés par la règle de
nom** (`norm("UseMinLevel") == norm("use_min_level")`, `tools/import_epic7.py:179-189`) — les valeurs sont
déjà en base. Rien à importer, rien à décider sur la version.

**Lot 2 — race, classe, profondeur. Bloqué par un prérequis de données.**

`RaceRestriction` et `JobRestriction` **ne sont pas remplis** : l'import apparie par introspection de nom ou
par `OVERRIDES` (`tools/import_epic7.py:184-189`), aucune colonne de la source ne s'appelle
`RaceRestriction`/`JobRestriction`, et les `OVERRIDES` d'`ItemResources` (`:73-78`) ne les listent pas. Ces
deux colonnes sont donc **laissées telles quelles sur les lignes mises à jour et mises à zéro sur les lignes
insérées** (`import_epic7.py:217-219` et `:236-239` ; `CLAUDE.md:1295-1300` le dit pour le chemin 9.4). Les
sept colonnes `limit_*` sont présentes dans la source et **inutilisées**.

Conséquence à écrire noir sur blanc : **avec la liste blanche du §5.3 et des masques à zéro, le lot 2
refuserait tous les équipements de tous les joueurs.** Il ne doit pas partir avant que les colonnes soient
remplies ; le lot 1, lui, ne dépend pas d'elles.

Prérequis du lot 2, dans cet ordre :

a. **Le vocabulaire de l'import ne sait pas exprimer un masque calculé.** `OVERRIDES` n'accepte qu'un nom de
   colonne ou une liste de colonnes numérotées, et une liste ne produit qu'un `ARRAY[...]::type[]` pour une
   colonne tableau (`import_epic7.py:190-198`). Il faut une nouvelle forme de spécification (expression
   `OR`/`CASE` au-dessus des sept colonnes) puis déclarer les deux paires dans `ItemResources`.
b. **Les bits de race du dépôt ne sont pas ceux de l'officiel.** `ItemRaceRestriction` vaut `Deva = 1,
   Asura = 2, Gaia = 4` (`Game/DataAccess/Entities/Enums/ItemRaceRestriction.cs:9-11`) alors que le masque
   officiel est `4 / 8 / 16` (§5.3) ; `ItemJobRestriction` (`1024 / 2048 / 4096 / 8192`) est déjà
   conforme. Aligner l'énumération sur l'officiel (et `MigrateDatabase/Worker.cs:686-720`, qui construit les
   mêmes bits faux), ou convertir à la frontière — **décision à trancher au lot 2**, pas ici.
c. **L'import lui-même ne peut pas être répété sur ce VPS** : pas de PostgreSQL ici, build et tests
   seulement. La modification d'`import_epic7.py` se vérifie ici par `--plan` et par test unitaire, pas par
   une exécution réelle. À signaler dans la MR, pas à contourner.
d. La profondeur, elle, n'a **rien** à importer : `ItemResource.JobDepth` est apparié par la règle de nom
   (`job_depth`) et la règle existe déjà pour les quêtes (§5.5). Reste la réserve §7.2 sur la convention de
   `JobResources.JobDepth`.

**Lot 3 — hors lot minimal, à nommer pour ne pas le croire fait.**

- 223 `TM_CS_SWAP_EQUIP` : un échange porte deux objets, donc deux exigences (et un refus qui laisse l'état
  intact). **Livré depuis (§12).**
- Branche invocation : quand `target_handle` vise un familier, l'officiel route **tout** le contrôle sur la
  créature cible (ses propres niveau/race/métier). Le dépôt répond `NotExist` dès que la cible n'est pas le
  personnage lui-même (`EquipmentService.cs:42-46`) : le port sur invocation n'existe pas encore.
- Restauration à la connexion : `WearInfo` relu de la base n'était jamais rejugé. **Correction** : cette
  fiche disait que l'officiel ne le fait pas non plus ; c'est faux, `DB_Login::readEquipItemList`
  (`DB_Login.cpp:1649`) rejuge chaque objet porté. **Livré depuis (§12).**

## 6. Écarts assumés avec NGemity

- **NGemity ne contrôle ni la race ni le métier à l'équipement** : `Unit::TranslateWearPosition`
  (`Unit.cpp:1446-1465`) ne teste que `GetLevelLimit()`, `use_min_level` et `use_max_level`. Sur ce point la
  référence secondaire est muette : le §5.3 vient du binaire officiel seul. Ne pas conclure de son silence
  que la règle n'existe pas.
- **NGemity ne répond rien quand le port échoue** : `WorldSession::onPutOnItem` (`WorldSession.cpp:616-646`)
  n'envoie la `TS_SC_RESULT` que dans la branche de succès. L'officiel répond 5. Navislamia suit l'officiel.
- **Table des rangs** : NGemity rang 8 → 180 (`GameRule.cpp:102-103`), l'officiel → 170. L'officiel tranche.
- **Lecture inversée des `limit_*`** : NGemity construit le masque puis l'utilise comme liste d'exclusion
  (`ObjectMgr.cpp:405-441`, et le piège déjà consigné en `CLAUDE.md:1068-1075` où les props finissent
  inutilisables). Le port d'objet officiel est une **liste blanche** ; c'est la seule lecture compatible avec
  les 20 835 lignes « tous bits posés » du §5.4.
- **Codes de refus déjà en place** : le dépôt répond `InvalidArgument` pour une position qui n'est pas un
  emplacement (`EquipmentService.cs:48-52`) et `AccessDenied` pour un handle inconnu (`:221-222`), là où
  l'officiel répond 5 puis 6. Aligner ces codes n'appartient pas à ce socle (ils sont couverts par les fiches
  200/281) ; le seul code à retenir ici est **5** pour une exigence non satisfaite.

## 7. NON ÉTABLI — A VERIFIER PAR KILLIAN

Chaque point est formulé comme la question exacte à trancher, pour qu'aucun dev n'ait à le deviner.

1. **Le client 7.3 refuse-t-il localement ?** Le PDB client ne nomme aucun prédicat « peut porter »
   (seuls `Rq_PutOnOffItem` et les `IsEquip*` d'inventaire existent) ; savoir si le bouton est grisé avant
   envoi, ou si le serveur est le seul juge, demande de lire le corps de `Rq_PutOnOffItem` et de son
   appelant dans `SFrame.exe` — non fait ici. La conséquence est bornée : si le client juge seul, le code 5
   n'est vu que par un client modifié.
2. **Que contient réellement `JobResources.JobDepth` — les drapeaux (1/2/4/8) ou les index 9.4 (0/1/2) ?**
   Le code suppose les drapeaux (`QuestService.cs:76, 88`, et le test `QuestLifecycleTests.cs:69` construit
   `JobDepth = 1` pour le métier 200), tandis que l'export 9.4 porte des index
   (`reference/sqlserver/Arcadia/JobResource.csv:4-10` : métier 100 → `"0"`, 101 → `"1"`, 111 → `"2"`). Un
   décalage d'un cran ici déplacerait **toutes** les portes de profondeur, quêtes comprises. Vérifiable par
   un `SELECT JobDepth FROM JobResources` (42 lignes) — à faire avant le lot 2, pas à supposer.
3. **Quel champ Navislamia correspond à `m_nUnitExpertLevel`** (`StructCreature+0x1c4`), le second terme du
   `max()` du plafond de niveau (§5.2) ? Aucun « niveau expert » n'existe sous ce nom dans le dépôt ;
   `CharacterEntity.MaxReachedLv` (`CharacterEntity.cs:32`) est le seul candidat. Tant que ce n'est pas
   tranché, le lot 1 doit **juger sur le niveau seul**, ce qui est *plus strict* que l'officiel : un écart
   assumé et bénin (refuser là où l'officiel accepte), à écrire dans la MR.

   *État du code (2026-10-02, lot 1 livré, commit `6f91d23`)* : le plafond est jugé sur le niveau du
   personnage **seul** (`ItemWearRules.IsWearAllowed`, appelé par `EquipmentService.JudgeWearAsync` pour la
   200 et par la branche de `EquipSetAsync` pour la 281) ; `MaxReachedLv` n'a **pas** été retenu, faute de
   preuve d'équivalence. La question reste ouverte pour le lot 2.

4. **L'ordre des contrôles et la valeur de refus pour la position hors bornes** : le dépôt refuse
   `InvalidArgument` avant toute lecture d'objet (`EquipmentService.cs:48-52`) alors que l'officiel traduit
   d'abord `wear_type == -1` en 5. Les deux chemins refusent, mais pas avec le même code ni au même moment ;
   aucun test client ne le départage ici.

   *État du code (2026-10-02, lot 1 livré, commit `6f91d23`)* : inchangé, le lot 1 n'y touche pas. La
   position est toujours jugée avant toute lecture d'objet (`EquipmentService.EquipAsync`), et un test le
   fige (`EquipAsync_AnswersAPositionOutsideTheWearInfoBeforeReadingTheItem`, qui vérifie en plus qu'aucun
   `GetItemByHandleAsync` n'a eu lieu). Un objet `wear_type == -1` portant une position **valide** passe
   cette porte et reçoit 5 du prédicat d'exigence (voir la réserve 7 ci-dessous).
5. **L'infobulle du client** (niveau requis, restrictions affichées) n'a pas été lue : rien n'établit que le
   client calcule son texte depuis `use_min_level`/le plancher de rang plutôt que depuis `level`.
6. **Le masque `nLimit` d'une ligne 9.4-only** : 3 499 lignes de `ItemResources` n'existent pas dans le
   fichier Epic 7 et gardent les valeurs d'un import antérieur. Comme aucun objet du client 7.3 ne les
   référence (`import_epic7.py:8`), elles ne devraient pas passer par la porte — non vérifié en base.
7. **Le code de refus d'un objet `wear_type == -1`** (réserve montée par le dev au lot 1) : sur l'officiel,
   un tel objet est refusé par `StructItem::IsWearable` au contrôle **§5.1-3**, donc avant
   `TranslateWearPosition`, avec **6** `AccessDenied` — le §5.2-1 (`wear_type == -1` dans
   `TranslateWearPosition`) est inatteignable par ce chemin. Le lot 1 applique la consigne de la fiche
   (§5.6-1 et §6 : « le seul code à retenir ici est 5 ») et répond **5** `NotActable`. À trancher : garder la
   consigne de la fiche, ou aligner le code 6 d'`IsWearable` — le bit `ITEM_FLAG_FAILED` et la durabilité à 0
   du même contrôle ne sont, eux, pas jugés du tout (voir §10.5).

*État du code (2026-10-02, lot 1 livré, commit `6f91d23`)* : les questions 1, 2, 5 et 6 ne sont pas touchées
par le lot 1 ; la 3 est appliquée « au plus strict » ; la 4 reste en l'état ; la 7 naît du lot 1.

## 8. Commits et binaires épinglés

| Référence | Version | Empreinte |
|---|---|---|
| `reference/rzu` (format de fil) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` — « packets: fix TS_SC_INVENTORY with older epics » | — |
| `reference/ngemity` (logique serveur) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` — « Fix compilation issue for GCC » | — |
| `reference/epic7part4/server/2012-11/CaptainHerlockServer.exe` (autorité) | serveur officiel, x64, branche `gameserver_release/2011-12-12`, build 2012-11-22 | sha256 `83b54fe18f15601578e35bcad1832a5c254bb09f512ab6297217fa49d9dd3eac` |
| `reference/client73/SFrame.exe` (le client tranche) | Epic 7.3, `pei-i386`, 9 841 664 octets ; **aucun SHA client à épingler** (dossier non git) | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` — donné pour re-vérification, non exploité ici |
| `reference/epic7part4/client-pdb/2011-12-14-part4-design/SFrame_Release.symbols.tsv` | build client du 2011-12-14 (le plus proche de 7.3) | lecture **statique** des noms de fonctions, aucune exécution |
| `reference/epic7part4/csv/ItemResource.csv` | données Epic 7 Part 4 (29 647 lignes) | — |
| branche de base | `master` `45f35713c5ee8e300c5ecdbdeed7e006bf3464ea` | — |

### 8.1 Emplacements relevés, pour re-vérification

**Ce qui a été lu du côté client, et comment.** `reference/client73/` n'est pas un dépôt git et le client
**n'a pas été exécuté** (`SFrame.exe`, ni Lua, ni script). La seule lecture client de cette fiche est
**statique** : les noms de fonctions du PDB d'un build contemporain
(`reference/epic7part4/client-pdb/2011-12-14-part4-design/SFrame_Release.symbols.tsv`), qui donnent
`SGameObject::Rq_PutOnOffItem`, `SGameSystem::Rq_PutOnOffItem`, `SGameWorld::Rq_PutOnOffItem` et les
`SInventoryMgr::IsEquip*` — aucune de ses ressources (`db_item.rdb`) n'a été ouverte. La question client qui
demanderait une ressource ou un corps de fonction est la réserve §7.1.

`reference/client73/` et `reference/epic7part4/` **ne sont pas des dépôts git** : d'où les empreintes.

Désassemblage (chaque plage bornée par le symbole suivant du `.symbols.tsv`) :

| Adresse | Symbole | Ce qu'on y lit |
|---|---|---|
| `0x14010f950` | `onPutonItem` | codes 5 / 1 / 6, lecture du handle à `+8` et de la cible à `+0xc` |
| `0x14008fea0` | `StructCreature::Puton` | 6 hors inventaire, 5 déjà porté, appel de `putonItem` |
| `0x140080e40` | `StructCreature::TranslateWearPosition` | plancher de rang, `use_min_level`, `use_max_level`, `m_nUnitExpertLevel` (`+0x1c4`) |
| `0x1400db120` | `StructPlayer::TranslateWearPosition` | classe (`+0x408/0x410/0x418/0x420`), race (`+0x160`), profondeur (`+0xd8`, `bt` sur `+0x34`) |
| `0x1400adab0` | `StructItem::IsWearable` | `wear_type == -1`, `ITEM_FLAG_FAILED` (bit 3) |
| `0x1400ad250` | `StructItem::GetLevelLimit` | `max(tableRang(rank), nMinLevel)` |
| `0x140200f10` | table des rangs | lecture de 8 entiers à `0x1404f7fd8` |
| `0x1400c0210` | `StructPlayer::IsHunter` | `JobInfo.job_class == 2` |
| `0x1400c0280` | `StructPlayer::IsFighter` | `== 1` |
| `0x1400c02f0` | `StructPlayer::IsMagician` | `== 3` |
| `0x1400c0360` | `StructPlayer::IsSummoner` | `== 4` |
| `0x140406268` | vftable `StructCreature` | slot `0x90` = `GetLevel` (`0x1400a50c0`) |

Types du PDB — les numéros de ligne renvoient au dump régénéré dans le dépôt par

    llvm-pdbutil dump -types CaptainHerlockServer.pdb   # 361 137 lignes


| Élément | Où |
|---|---|
| `ItemBase` — `nRank` 32, `nLevel` 36, `nJobDepth` 52, `nMinLevel` 56, `nMaxLevel` 60, `nTargetMinLevel` 64, `nTargetMaxLevel` 68, `WearType` 100, `nLimit` 104 | bloc `ItemBase` (`LF_STRUCTURE` 0x7FE3, ligne 118604 ; champ `LF_FIELDLIST` 0x7FE2, ligne 118541) |
| `ItemBase::LIMIT_FLAG` (`4/8/16/1024/2048/4096/8192`) | `LF_ENUM` 0x7FD4, ligne 118502 |
| `ItemBase::ITEM_FLAG` (`NONE = -1`, `CARD = 0`, `FULL = 1`, `INSERTED = 2`, `FAILED = 3`, `EVENT = 4`) | `LF_ENUMERATE ITEM_FLAG_*`, ligne 117826 |
| `JobInfo` — `job_class` 12, `job_depth` 13, `availble_job` 18 | `LF_FIELDLIST` 0x6C6B, ligne 94766 |
| `JobInfo::<unnamed-tag>` FIGHTER/HUNTER/MAGICIAN/SUMMONER = 1..4 | 0x6C65, ligne 94745 |
| `JobInfo::<unnamed-tag>` GAIA = 3, DEVA = 4, ASURA = 5 | 0x6C67, ligne 94753 |
| `JobInfo::<unnamed-tag>` GAIA/DEVA/ASURA_BASIC_JOB = 100/200/300 | 0x6C69, ligne 94760 |
| `StructCreature::m_nUnitExpertLevel` (offset 452) | ligne 106206 |
| `StructPlayer::GetJobId` (slot 184), `GetRace` (352), `GetJobDepth` (216) | listes de méthodes, lignes 105648, 105527 et voisines |
| `RESULT_*` (`LIMIT_RACE = 24`, `LIMIT_JOB = 25`, `NOT_ACTABLE`, `ACCESS_DENIED`) | `LF_ENUMERATE RESULT_*`, ligne 203522 |

## 9. Bloc destiné à `CLAUDE.md`

À porter dans la description de la MR par `navis-qa` : la fiche ne modifie pas `CLAUDE.md` (fichier
d'instructions protégé, écriture refusée sans opérateur). Les faits ci-dessous tiennent quel que soit ce que
`navis-dev` livre ; le dernier paragraphe, lui, décrit ce que la branche applique et doit être ajusté (ou
retiré) selon le code réellement commité.

~~~markdown
### Equipment — ce qui décide qu'un objet peut être porté

`TM_CS_PUTON_ITEM` (200) et `TM_CS_PUTON_ITEM_SET` (281) entrent par `EquipmentService` et n'appliquaient
aucune exigence avant le socle `docs/packet-specs/socle-exigences-equipement.md`. La règle est celle du
serveur officiel Epic 7 Part 4 (`reference/epic7part4/server/2012-11/`) : `StructCreature::TranslateWearPosition`
d'abord, `StructPlayer::TranslateWearPosition` ensuite.

- **Niveau** : `max(plancherDeRang(rank), use_min_level)` est comparé à `max(niveau, niveau expert)`, puis la
  fenêtre `use_min_level` / `use_max_level`. La table des rangs du binaire est `{0, 20, 50, 80, 100, 120, 150,
  170}` pour les rangs 1..8 — NGemity dit 180 au rang 8, l'officiel tranche. Le champ `level` de la ressource
  n'entre pas dans le port : il n'alimente que le niveau *recommandé* de l'infobulle.
- **Race, classe, profondeur** : des **listes blanches**. Le bit du camp du joueur doit être posé dans
  `ItemBase::nLimit` (`LIMIT_DEVA 4`, `LIMIT_ASURA 8`, `LIMIT_GAIA 16`, `LIMIT_FIGHTER 1024`, `LIMIT_HUNTER
  2048`, `LIMIT_MAGICIAN 4096`, `LIMIT_SUMMONER 8192`) et dans `job_depth` (bit indexé par la profondeur du
  métier). Un champ à zéro refuse **tout le monde** : même piège que les `limit_*` des props de terrain, et
  l'inverse de la lecture de NGemity.
- La classe d'un personnage se lit dans `JobResource.job_class` (1 fighter, 2 hunter, 3 magician,
  4 summoner) ; sa race dans `JobResource` (`GAIA = 3`, `DEVA = 4`, `ASURA = 5`) ; son métier de base dans
  les identifiants 100/200/300 quand `GetJobId` rend 0.
- Tout échec répond **`TM_SC_RESULT` (0) code 5 `NotActable`** : l'officiel ne distingue pas les causes, et
  `LimitMin` / `LimitMax` / `LimitRace` / `LimitJob` servent à l'*utilisation* d'un objet, pas au port.

Ce que la branche applique (`navis-dev`, lot 1, commit `6f91d23`) : `ItemWearRules.IsWearAllowed` juge l'objet
avant `EquipItemAsync` — un `wear_type == -1` refuse tout le monde, puis
`max(plancherDeRang(rank), use_min_level) > niveau du personnage` refuse, puis la fenêtre `use_max_level`
(0 = pas de plafond) — et l'appelant répond `NotActable` (5) sur les deux chemins (200 et 281), un handle
refusé au sein d'une 281 n'interrompant pas les suivants. Le plancher de rang est la constante de code
`{0, 0, 20, 50, 80, 100, 120, 150, 170}` indexée par le rang clampé 0..8, et les trois colonnes viennent du
catalogue (`ItemWearFields`, projeté par `ItemResourceRepository.GetWearFields`), pas d'une lecture en base
par équipement. Le plafond se juge sur le niveau du personnage **seul**, faute d'équivalent établi de
`m_nUnitExpertLevel` dans le dépôt. Race, classe et profondeur (lot 2) ne sont pas jugées, et un handle ou une
ressource que le catalogue ne peut pas lire reste jugé par le chemin d'équipement lui-même.

### Current limitations

- `ItemResources.RaceRestriction` et `JobRestriction` sont **vides** : `tools/import_epic7.py` apparie les
  colonnes par nom et la source Epic 7 n'en porte pas, tandis que ses sept colonnes `limit_*` ne sont pas
  importées. Un contrôle race/classe construit sur ces deux colonnes refuserait **tous** les équipements.
- Les bits de race du dépôt (`ItemRaceRestriction` : `Deva = 1`, `Asura = 2`, `Gaia = 4`) ne sont pas ceux de
  l'officiel (`4` / `8` / `16`) ; `ItemJobRestriction` est déjà conforme.
- Le port sur invocation (`target_handle` visant un familier), `TM_CS_SWAP_EQUIP` (223) et la restauration
  des objets portés à la connexion ne passent pas par cette règle.
~~~

## 10. Implémentation livrée (`navis-dev`, lot 1)

Branche `hermes/packet-socle-exigences-equipement`, base `fca9110`, commit de code **`6f91d23`**. **Lot 1
seulement** : les lots 2 (race, classe, profondeur) et 3 (223, invocation, restauration) ne sont pas touchés.

### 10.1 Checklist du lot 1 (§5.6)

- [x] **1. `IsWearAllowed` à côté de `ItemWearRules`** — `ItemWearRules.IsWearAllowed(wearType, rank,
      useMinLevel, useMaxLevel, characterLevel)` et sa surcharge prenant un `ItemWearFields`, à côté de
      `IsWearableSlot` / `TryResolveSlot`. Ordre appliqué : `wear_type == -1` → refus ; plancher → refus ;
      plafond → refus. Table des rangs en **constante de code** (`RankLevelFloor`, même tableau que §5.2).
- [x] **2. Les trois champs exposés par le catalogue** — `ItemWearFields` porte `Rank`, `UseMinLevel` et
      `UseMaxLevel` ; `ItemResourceRepository.GetWearFields()` projette les trois colonnes et
      `IItemWearCatalog.TryGetWearFields(long, out ItemWearFields)` les sert. Le dictionnaire gelé les porte
      tous les trois : **aucune lecture en base par équipement**.
- [x] **3. Branchement avant `EquipItemAsync`, sur les deux chemins** — `EquipmentService.JudgeWearAsync`
      pour la 200 (après le contrôle de position, avant `EquipAtSlotAsync`) ; `ResolveSlotAsync` rend
      désormais les `ItemWearFields` avec l'emplacement, donc la 281 juge sans seconde lecture et **un
      handle refusé n'interrompt pas les suivants**. Refus `ResultCode.NotActable` (5).
- [x] **4. Tests** — table du prédicat (rangs 0..8, `use_min_level` 0/1/20/155/160, `use_max_level` 0/300),
      suite d'offsets de la 200 (16 octets, `position` à 7, `item_handle` à 8, `target_handle` à 12) et le
      chemin de refus des deux chemins de service.

### 10.2 Fichiers touchés

| Fichier | Ce qui change |
|---|---|
| `Game/Services/ItemWearRules.cs` | `RankLevelFloor` (table de code, index clampé 0..8) ; `IsWearAllowed` et sa surcharge |
| `Game/DataAccess/Repositories/Interfaces/IItemResourceRepository.cs` | `ItemWearFields` gagne `Rank`, `UseMinLevel`, `UseMaxLevel` (record positionnel, ordre figé par un test) |
| `Game/DataAccess/Repositories/ItemResourceRepository.cs` | `GetWearFields()` projette les trois colonnes |
| `Game/Services/Interfaces/IItemWearCatalog.cs` | `TryGetWearType` remplacé par `TryGetWearFields(long, out ItemWearFields)` |
| `Game/Services/ItemWearCatalog.cs` | le dictionnaire gelé porte le port entier (emplacement + exigences) |
| `Game/Services/EquipmentService.cs` | `JudgeWearAsync` (200) ; `ResolveSlotAsync` rend les champs ; branche d'exigence de `EquipSetAsync` (281) |
| `Tests/Game/EquipmentWearRequirementTests.cs` | nouveau, 61 tests : table du prédicat, table des rangs, refus des deux chemins |
| `Tests/Game/PutonItemPacketsTests.cs` | nouveau, 9 tests : offsets de la 200 |
| `Tests/Game/ItemWearTests.cs` | +1 test (le port entier) ; les dix existants portés sur `TryGetWearFields` |

`CLAUDE.md` n'est **pas** touché (fichier d'instructions protégé) : le bloc du §9 part dans la description de
la MR.

### 10.3 Réponses émises par le code livré

| Cas | Avant le lot | Après le lot |
|---|---|---|
| objet sous son plancher de rang (200 et 281) | équipé | `TM_SC_RESULT` (0), id 200/281, **5** `NotActable` |
| objet au-dessus de `use_max_level` | équipé | **5** `NotActable` |
| objet `wear_type == -1` sur une position valide | équipé | **5** `NotActable` (voir la réserve §7.7 : l'officiel répond 6 par `IsWearable`) |
| objet porté par le personnage mais absent du dossier `ItemResources` | équipé | équipé, le prédicat ne juge pas ce qu'il ne peut pas lire |
| handle inconnu du personnage | 6 `AccessDenied` (chemin d'équipement) | inchangé |
| position hors bornes | 3 `InvalidArgument`, avant toute lecture | inchangé |
| 281 mixte (objet refusé puis objet valide) | tout équipé | seul le valide est équipé, la réponse porte le **premier refus** (5) |

### 10.4 Ce qui n'est pas porté (et par où ça fuit)

- **§5.1-3, `StructItem::IsWearable`** : le bit `ITEM_FLAG_FAILED` (`[item+0x15c]` bit 3) et la durabilité à
  0 ne sont jugés ni avant ni après ce lot ; seul `wear_type == -1` l'est. Refus officiel : 6 `AccessDenied`.
  Le dépôt ne dispose pas d'un état d'instance équivalent typé (`ItemEntity.Flag`, `Endurance`,
  `EtherealDurability`) — c'est une décision à prendre, pas un oubli de lecture.
- **§5.2-4 (min) et §5.2-5 (max)** : la borne basse `use_min_level` est portée par le `max()` du plancher
  (aucune divergence mesurable : `use_min_level` est toujours ≥ 0), la borne haute est un contrôle à part.
- 281 : la 223, le port sur invocation et la restauration des objets portés à la connexion (§5.6, lot 3).
- Les emplacements `position` de la 200 continuent d'être jugés **avant** l'objet (réserve §7.4, inchangée).

### 10.5 Commandes et codes de sortie mesurés (2026-10-02, VPS du pipeline)

    export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
    dotnet build Navislamia.sln -c Debug        # code 0, 0 erreur
    dotnet test Tests/Tests.csproj              # code 0, Failed 0, Passed 2625, Total 2625

Base mesurée sur ce clone au même commit que le PO (`fca9110`, worktree détaché) : `Passed 2554`. Le lot
ajoute **+71** tests et n'en supprime aucun (§5.6-4 : 61 pour le prédicat et les chemins de refus, 9 pour les
offsets de la 200, 1 pour le catalogue).

Invariant d'énumération : aucun membre de `GamePackets` n'est ajouté par ce lot ; contrôle de l'état final,
110 membres `TM_CS_*` et **0** absent du dispatch de `GameClient` (les 54 non dispatchés sont des `TM_SC_*`,
sortants par construction).

### 10.6 Réserves transmises à la QA

1. Le code de refus d'un `wear_type == -1` : **5** dans ce lot (consigne de la fiche), **6** sur l'officiel
   par `IsWearable` — réserve §7.7, à ne pas « corriger » sans arbitrage.
2. Le plafond est jugé sur le niveau du personnage seul (réserve §7.3) : plus strict que l'officiel, écart
   assumé et borné.
3. `use_min_level` en dessous du `max()` du plancher n'est pas un contrôle séparé (aucune divergence possible
   pour des valeurs ≥ 0) — à dire dans la MR pour qu'un relecteur ne le compte pas comme un oubli.
4. Les tests de service montent un `ICharacterService` factice qui répond `NotFound` : ils figent le chemin
   de refus, pas le chemin d'écriture en base (déjà couvert par les fiches 200/281).

## 11. Lot 2 livré : race, classe et profondeur de métier

Implémenté le 2 octobre 2026. Vérifié automatiquement, y compris sur PostgreSQL 18 temporaire ;
la base de jeu reste arrêtée et attend l'application de la migration au démarrage.

### 11.1 Import et remise à niveau des données

Les sept colonnes de texte `limit_*` du CSV Epic 7 sont converties en **listes d'autorisation** dans
les colonnes déjà présentes de `ItemResources`. Le schéma n'a pas besoin de sept nouvelles colonnes :

| Source | Colonne du dépôt | Bit |
| --- | --- | --- |
| `limit_deva` | `RaceRestriction` | 1 |
| `limit_asura` | `RaceRestriction` | 2 |
| `limit_gaia` | `RaceRestriction` | 4 |
| `limit_fighter` | `JobRestriction` | 1 024 |
| `limit_hunter` | `JobRestriction` | 2 048 |
| `limit_magician` | `JobRestriction` | 4 096 |
| `limit_summoner` | `JobRestriction` | 8 192 |

Ces bits de race sont ceux de `ItemRaceRestriction`, séparé du masque officiel `ItemBase.nLimit`.
Ils ne doivent pas être remplacés par 4/8/16 : cela changerait le sens des valeurs existantes en base.
Une chaîne `1` autorise son camp, `0` l'interdit. Le masque tout-à-zéro autorise **personne**.
`JobDepth`, déjà importé depuis `job_depth`, conserve son masque de profondeurs.

`tools/import_epic7.py` utilise maintenant ces correspondances lors des futurs imports de
`ItemResources`. L'importeur SQL Server `MigrateDatabase/Worker.cs` accepte également uniquement le
drapeau `1` ; une valeur absente n'ouvre plus une permission. Le mode `--plan` du script Epic 7 ne
supprime plus les tables de travail.

La migration Arcadia **`20261002150000_BackfillItemWearRestrictions`** corrige les valeurs déjà en base :

- **29 647 identifiants** Epic 7, répartis en **31 combinaisons** de race/classe/profondeur ;
- modification de `RaceRestriction`, `JobRestriction` et `JobDepth` seulement ;
- aucune insertion d'objet, aucun changement des autres colonnes, aucun changement des IDs absents du CSV ;
- SQL embarqué dans `Modules.Game`, donc pas de CSV, Python ou import manuel requis sur le serveur déployé ;
- exécution transactionnelle par EF, enregistrée dans l'historique des migrations ; les démarrages suivants
  ne réécrivent pas les modifications d'un administrateur.

Le fichier `Game/DataAccess/Migrations/Arcadia/ItemWearRestrictions.sql` est généré par
`tools/generate_item_wear_restrictions.py`. Il contient la provenance et le SHA256 du CSV ; le générateur
refuse les identifiants dupliqués et les drapeaux invalides. La migration `Down` conserve ces corrections :
elle ne peut pas reconstruire les valeurs précédentes de chaque objet.

### 11.2 Contrôle du port

`ItemWearFields` et `ItemResourceRepository.GetWearFields` transportent les deux masques et la
profondeur avec les exigences de niveau. `IJobResourceRepository.GetWearFields` projette
`JobResource.JobClass` et `JobDepth` ; `EquipmentService` les conserve en dictionnaire gelé, chargé une
fois. Il n'ajoute aucune requête de ressource à chaque équipement.

Pour les deux demandes 200 et 281, avant d'appeler `EquipItemAsync` :

1. Vérifier les exigences de niveau du lot 1.
2. Résoudre le métier courant. Si son identifiant vaut zéro, choisir 100 pour Gaia, 200 pour Deva,
   300 pour Asura, comme les prédicats officiels `IsFighter`/`IsHunter`/`IsMagician`/`IsSummoner`.
3. Lire sa classe dans **`JobResource.JobClass`**, sans la deviner à partir des chiffres du métier.
4. Exiger le bit de race et le bit de classe correspondants.
5. Exiger le bit de profondeur correspondant, comme l'officiel : `1 << GetJobDepth() & nJobDepth`
   (`StructPlayer::TranslateWearPosition`), la profondeur étant un **index** 0..3. **Correction de relecture
   (2 octobre 2026)** : ce point affirmait que `JobResource.JobDepth` contient déjà 1/2/4/8, ce qu'aucune
   lecture de la base n'a établi (§7.2) ; l'export 9.4 porte l'index (100 → 0, 101 → 1, 110 → 2, 120 → 3),
   et un index lu comme un bit interdisait tout équipement aux métiers de base. `JobDepths` lit le codage
   sur les 42 lignes au chargement (un 0 ou un 3 ne peut être qu'un index, un 4 ou un 8 qu'un bit) et rend
   l'index ; `QuestService` en dépend aussi.

Un métier inconnu, une race/classe invalide ou une profondeur invalide refuse le port. Les autorisations
vides refusent également le port. Le résultat est **`NotActable` (5)**, jamais `LimitRace` ou `LimitJob`,
conformément à `StructPlayer::TranslateWearPosition`. Un objet refusé dans un ensemble 281 ne bloque pas
les objets suivants. Chaque tentative lit le métier courant, donc un changement de métier prend effet
au prochain équipement.

Les contrôles de niveau expert, d'invocation, de bascule 223 et de revalidation des objets déjà portés à
la connexion restent les limites du lot 3 ; ce lot ne déséquipe pas rétroactivement un ancien personnage.

### 11.3 Vérifications exécutées

- **3 045 tests .NET réussis**, zéro échec, dont **55 nouveaux cas** ordinaires pour ce lot.
- Un test PostgreSQL explicite supplémentaire réussi : chaîne complète des migrations Arcadia, remise
  à niveau des objets, projections des catalogues, conservation des autres données et de l'historique.
- **7 tests Python réussis**, dont la comparaison de chaque ligne du CSV au SQL embarqué et un import
  réel des **29 647 objets** dans PostgreSQL temporaire. Les masques et profondeurs ont été comparés au
  CSV après l'import, puis après une remise à zéro suivie de la migration ; un objet extérieur au CSV
  est conservé.
- `MigrateDatabase` compile sans erreur. Le build signale l'avertissement de dépendance AutoMapper
  déjà présent (NU1903), indépendant des contrôles de port.

Commandes ordinaires :

```powershell
dotnet test Tests/Tests.csproj --no-restore -p:WarningLevel=0 -v:minimal
python -m unittest discover -s tools/tests -p test_item_wear_import.py -v
```

Le test PostgreSQL .NET est explicite : il attend une **base temporaire vide** nommée
`navis_equipment_test`, indiquée par `NAVISLAMIA_ARCADIA_TEST_CONNECTION`, et se sélectionne par son
nom complet `Tests.DataAccess.ItemWearCatalogueTests.PostgreSql_migrates_existing_items_and_preserves_other_data_and_history`.
Le test Python PostgreSQL peut ensuite vérifier l'import sur cette même base avec le rôle temporaire
`navis_equipment_test`, localhost et le port `NAVISLAMIA_ITEM_IMPORT_TEST_PORT`. Sans cette variable,
il est ignoré. Le CSV local est nécessaire aux tests Python de comparaison des données.

À vérifier avec le client après le démarrage de PostgreSQL et du serveur : essayer un objet réservé
à une autre race, un objet réservé à une autre classe et un objet de classe maître sur un métier
antérieur ; chaque demande doit être refusée. Réessayer un ensemble mêlant un objet interdit et un
objet autorisé pour vérifier que ce dernier peut être équipé.

## 12. Lot 3 livré (2026-10-03, branche `claude/invocations-suite`)

### 12.1 Revérification à la connexion

`DB_Login::readEquipItemList` (`DB_Login.cpp:1600-1680`) : pour chaque objet porté d'emplacement
`< MAX_SPARE_ITEM_WEAR`, un emplacement déjà pris (`m_anWear[pos]`), un `TranslateWearPosition` qui échoue ou un
chevauchement font que l'objet **n'est pas porté** (il revient au sac). Les objets de rechange (24-27) passent
sans jugement (`StructPlayer::TranslateWearPosition` les laisse à l'échange), ceux d'une invocation sont jugés
pour elle.

`EquipmentService.RevalidateWornItemsAsync`, appelé par `GameActions.OnLogin` **avant** les stats et la 202 :
emplacement principal (0-23) pris deux fois (ordre des ids) ou exigences (niveau, race, classe, profondeur) non
remplies → `WearInfo = None` dans le personnage chargé et en base (`ICharacterService.UnwearItemsAsync`, une
sauvegarde), ligne `Information` au journal. Les règles gauche/droite de l'officiel (balles, arme en main gauche)
ne sont pas jugées, comme à l'équipement.

### 12.2 Rechange et échange 223

Voir `223-swap-equip.md` §11. Un objet de rechange (24-27) ne donne aucune statistique.

### 12.3 Invocation

Voir `socle-equipement-invocation.md`.
