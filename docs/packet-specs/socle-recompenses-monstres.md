# Récompenses de mort d'un monstre — expérience, JP, or, chaos et butin d'`tf_monster_resource` (Epic 7.3)

Fiche du socle « récompenses réelles des monstres » (carte Trello `jPLdNuge`, carte Hermes `t_89593120`
→ dev `t_02c4b6f3`), écrite par `navis-ref` : archéologie et spécification seulement, aucun code serveur
touché. **Le découpage de §9 est le livrable central** ; §12 porte tout ce qui n'a pas pu être établi.

**Méthode.** Aucun exécutable du client n'a été lancé, aucun Lua, aucun script du client. Aucun serveur de
jeu, aucune base Postgres. La lecture a porté sur : `reference/epic7part4/server/2012-11/`
(`CaptainHerlockServer.exe` + `symbols.tsv` — les adresses citées sont les VA de la PDB),
`reference/epic7part4/csv/MonsterResource.csv`, `reference/epic7part4/rdu/MonsterResource.rdu`,
`reference/sqlserver/Arcadia/MonsterResource.csv`, `reference/ngemity/Chihiro`, `reference/rzu`,
`docs/`, `CLAUDE.md`, `op_codes.md`. La collision a été relevée sur l'API GitHub publique.

**Correction de repères (la carte cite `data/epic7/MonsterResource.csv` : ce chemin n'existe pas ici).**
Les exports réellement présents, et **celui que j'ai lu** :

| export | lignes | remarque |
|---|---|---|
| `reference/epic7part4/csv/MonsterResource.csv` | **6 898 lignes** (6 897 monstres + en-tête) | **lu et utilisé pour tous les chiffres du §6** ; 76 colonnes, sha256 `d2ad9977…` |
| `reference/sqlserver/Arcadia/MonsterResource.csv` | 8 165 lignes (8 164 monstres + en-tête) | export 9.4, 81 colonnes (il porte `affiliation`, `species_id`, `monster_grade_icon`) ; sert de contrôle de noms, pas de valeurs |
| `reference/epic7part4/rdu/MonsterResource.rdu` | 6 897 enregistrements | table du serveur officiel : dictionnaire de **76 colonnes** en clair (`RC_RDU`) — c'est lui qui donne l'ordre et le type des colonnes (§6) |

Le dépôt annonce 7 431 lignes en base (carte) : ce chiffre **n'est pas vérifiable ici** (pas de Postgres) et
ne l'est pas non plus depuis les exports (6 897 / 8 164). À confirmer par Killian (§A VERIFIER 6).

---

| | |
|---|---|
| Paquets | `TM_SC_EXP_UPDATE` (1003, **27 o**), `TM_SC_GET_CHAOS` (213, **25 o**), `TM_SC_PROPERTY` (507, « chaos », 37 o), `TM_SC_GOLD_UPDATE` (1001, **19 o**), `TM_SC_ITEM_DROP_INFO` (282, **15 o**), `TM_SC_BONUS_EXP_JP` (1004, 13 o) — **aucun identifiant nouveau** |
| Référence d'autorité | serveur officiel 2012-11 : `StructMonster::onDead` `0x1400ba600`, `procEXP` `0x1400b7bb0`, `addEXP` joueur `0x1400b73c0` / groupe `0x1400b7830`, `procDropChaos` `0x1400b7e20`, `addChaos` `0x1400b6c30`, `procDropGold` `0x1400b3ac0`, `procDropItem` `0x1400ba360`, `MonsterDropItemToWorld` `0x140043cc0`, `StructItem::AllocGold` `0x1400af7c0`, `SendExpMsg` `0x14014b5c0`, `SendGoldChaosUpdateMsg` `0x14014b720`, `StructPlayer::AddChaos` `0x1400c0a30`, `StructPlayer::AddExp` `0x1400e14e0` |
| Référence secondaire | NGemity/Chihiro `Monster::onDead` `Monster.cpp:121`, `calcPartyContribute` `:199`, `procEXP` `:342`, `procDropItem` `:475`, `dropItem` `:497`, `dropItemGroup` `:551`, `procDropGold` `:583`, `procDropChaos` `:1359`, `MemoryPoolMgr::AllocGold` `Globals/MemPool.cpp:168`, `MonsterBase.h:100-140` |
| Code | `Game/Services/CombatService.cs:394` (`AwardKill`), `Game/Services/CombatRewards.cs`, `Game/Services/GroundItemService.cs:52`, `Game/Services/MonsterDropCatalog.cs`, `Game/DataAccess/Entities/Arcadia/MonsterResourceEntity.cs:66-79`, `Game/DataAccess/Repositories/MonsterResourceRepository.cs:20`, `Game/Services/MonsterInstance.cs:22`, `Game/Services/MonsterInstanceFactory.cs:16`, `Game/Network/Packets/Game/GameCharacterPackets.cs:269,288`, `Game/Network/Packets/Game/GameStatPackets.cs:117` |
| Tests | `Tests/Game/CombatRewardsTests.cs` (3 tests — le test du lot disparaît avec lui, §10) ; socle mesuré **2 554 réussis / 0 échec** |
| Mesures | `dotnet build Navislamia.sln -c Debug` → **exit 0** ; `dotnet test Tests/Tests.csproj` → **exit 0**, 2 554 réussis, 0 échec, 0 ignoré ; collision : **0 MR ouverte**, aucune des 83 branches distantes ne porte de commit sur les fichiers visés |

---

## 1. Identité — les six paquets du socle, aucun identifiant nouveau

Ids et noms relevés dans `op_codes.md` (table Lua locale du client, aucune version) ; le gating, l'ordre des
champs et les tailles viennent de `reference/rzu` ; la taille est **confirmée sur le fil** par la constante de
longueur écrite par le serveur officiel (dernière colonne).

| paquet | id | `op_codes.md` | rzu | taille 7.3 | preuve officielle |
|---|---|---|---|---|---|
| `TM_SC_EXP_UPDATE` | 1003 | `op_codes.md:193` | `TS_SC_EXP_UPDATE.h:14-17` (1003 < `EPIC_9_6_3`, 2003 au-delà) | **27** | `movl $0x1b` + `movw $0x3eb` (`0x14014b5f7`, `0x14014b5f0`) |
| `TM_SC_GET_CHAOS` | 213 | `op_codes.md:62` | `TS_SC_GET_CHAOS.h:17-19` (213 < `EPIC_9_6_3`) | **25** | `movl $0x19` + `movw $0xd5` (`0x1400b6d08`, `0x1400b6cfe`) |
| `TM_SC_PROPERTY` | 507 | `op_codes.md:141` | `TS_SC_PROPERTY.h:16-18` (507 < `EPIC_9_6_3`) | 37 (nom `chaos`) | `SendPropertyMessage(handle, "chaos", valeur)` (`0x1400c0aa3`, chaîne `0x1404097d8`) |
| `TM_SC_GOLD_UPDATE` | 1001 | `op_codes.md:191` | `TS_SC_GOLD_UPDATE.h:16-18` | **19** | `movl $0x13` + `movw $0x3e9` (`0x14014b757`, `0x14014b745`) |
| `TM_SC_ITEM_DROP_INFO` | 282 | `op_codes.md:96` | `TS_SC_ITEM_DROP_INFO.h:10-12` (282 < `EPIC_9_6_3`) | **15** | `movl $0xf` + `movw $0x11a` (`0x140043cf5`, `0x140043cee`) |
| `TM_SC_BONUS_EXP_JP` | 1004 | `op_codes.md:194` | `TS_SC_BONUS_EXP_JP.h:20-22` | 13 (liste vide) | `movl $0xd` + `movw $0x3ec` (`0x1400c173b`, `0x1400c172d`) |

Le socle **n'introduit aucun id** : tout part sur des paquets déjà déclarés chez nous (507, 1001, 1003) ou
déjà spécifiés par les fiches sœurs. Deux ids manquent au dépôt (§10) : **213** et **282**.

## 2. Ce que le joueur fait

Le joueur (ou son familier) **tue un monstre** : la mort de la créature déclenche
`StructMonster::onDead(StructCreature *pKiller, bool)` (`0x1400ba600`, 0x8e0 octets), qui enchaîne **toute** la
distribution des récompenses, une fois par monstre, dans un ordre fixe (§5). Aucun paquet client n'est
nécessaire pour l'obtenir : c'est le serveur qui décide et **pousse** les mises à jour.

## 3. Structure sur le fil

En-tête commun de 7 octets : `uint32` taille, `uint16` id, `uint8` somme de contrôle (7 = `HeaderSize` du
dépôt). Les offsets ci-dessous sont **dans la trame**, l'offset 7 étant le premier octet de charge utile.

### 3.1 `TM_SC_EXP_UPDATE` (1003) — 27 octets, au bénéficiaire

| offset | type | nom | source |
|---|---|---|---|
| 0 | `uint32` | taille = 27 | officiel `0x14014b5f7` (`$0x1b`) ; rzu `TS_SC_EXP_UPDATE.h:10-13` |
| 4 | `uint16` | id = 1003 | officiel `0x14014b5f0` (`$0x3eb`) |
| 7 | `uint32` | `handle` du personnage | officiel `0x14014b604` (`GetHandle()` virtuel) ; rzu `:10` — champ `ar_handle_t` |
| 11 | `uint64` | `exp` **total** (pas le gain) | officiel `0x14014b620` (champ `+0x1c8` du personnage) ; rzu `:11` |
| 19 | `uint64` | `jp` **total** | officiel `0x14014b614` (`*0xf8(%rax)`) ; rzu `:12-13` |

C'est la forme déjà écrite par `GameCharacterPackets.BuildExpUpdate` (`GameCharacterPackets.cs:288-296`) :
`handle @7`, `exp @11`, `jp @19`, total 27 — **elle est exacte, rien à changer**.

### 3.2 `TM_SC_GET_CHAOS` (213) — 25 octets, **diffusé à la région**

| offset | type | nom | source |
|---|---|---|---|
| 0/4 | `uint32`/`uint16` | taille = 25 / id = 213 | officiel `0x1400b6d08`, `0x1400b6cfe` |
| 7 | `uint32` | `hPlayer` | officiel `0x1400b6d61` (écrit `0xa7(%rsp)`, valeur du `GetHandle()` virtuel du joueur `0x1400b6d6c`) |
| 11 | `uint32` | `hCorpse` (le monstre) | officiel `0x1400b6d8b` (écrit `0xab(%rsp)`) |
| 15 | `int32` | `nChaos` (le gain, arrondi au hasard) | buffer `+0xaf` ; tirage `XRandom` `0x1400b6d13`, arrondi `0x1400b6e42-0x1400b6e9d` |
| 19 | `int8` | `nBonusType` (**1 octet en 7.3**) | buffer `+0xb3` ; rzu `TS_SC_GET_CHAOS.h:13-15` |
| 20 | `int8` | `nBonusPercent` (**1 octet en 7.3**) | buffer `+0xb4` ; rzu `:16-18` |
| 21 | `int32` | `nBonus` | buffer `+0xb5` ; rzu `:19` |

7 + 4 + 4 + 4 + 1 + 1 + 4 = 25 ✓ (la constante `0x19` de l'officiel ferme le compte). Les deux bonus valent 0
sauf mode PC-bang / temps de jeu limité, où ils viennent de `fAllyPCBangChaosBonusRate` (`0x1404f7e04`,
`0x1400b6e6c`) et de `GameRule::GetGameTimeLimitPenalty` (`0x1400b6dcc`, `0x1400b7fc5`).

### 3.3 `TM_SC_PROPERTY` (507), nom `chaos` — 37 octets, au seul bénéficiaire

`StructPlayer::AddChaos(int)` (`0x1400c0a30`) écrit le champ chaos (`+0x2930`) puis appelle
`SendPropertyMessage(player, GetHandle(), "chaos", &valeur)` (`0x1400c0aa3` ; la chaîne est en `0x1404097d8`).
Format rzu `TS_SC_PROPERTY.h:7-15` : `handle` `uint32` @7, `is_number` `bool` @11 (= 1), `name` chaîne fixe de
16 octets @12, `value` `int64` @28, octet terminal @36. **Déjà implémenté** :
`GameStatPackets.BuildProperty` (`GameStatPackets.cs:117-140`) calcule exactement 37 et le nom `chaos` est déjà
employé (`GameActions.cs:249`).

### 3.4 `TM_SC_GOLD_UPDATE` (1001) — 19 octets, au seul bénéficiaire

| offset | type | nom | source |
|---|---|---|---|
| 0/4 | — | taille = 19 / id = 1001 | officiel `0x14014b757`, `0x14014b745` |
| 7 | `uint64` | `gold` (bourse) | officiel `0x14014b73e` (champ `+0x2448`) ; rzu `:11-12` |
| 15 | `uint32` | `chaos` | officiel `0x14014b751` (`+0x2930`) ; rzu `:13` |

**La mort d'un monstre ne l'envoie pas** : `SendGoldChaosUpdateMsg` a 20 sites d'appel dans l'exécutable
(`ChangeGold`, `PushItem`, `processTradeGold`, `SellToBooth`, `BuyFromBooth`, `ProcessTrade`, `EndQuest`,
`SendCharacterInfo`, `onChangeProperty`, `SCRIPT_SendGoldChaosUpdate`) et **aucun** n'est dans `onDead`,
`procDropGold` ou `procEXP`. L'or du monstre **tombe au sol** (§7.3) : la bourse ne bouge qu'au ramassage.
`BuildGoldUpdate` (`GameCharacterPackets.cs:269-275`) est exact.

### 3.5 `TM_SC_ITEM_DROP_INFO` (282) — 15 octets, **diffusé à la région**

Construit par `MonsterDropItemToWorld(StructCreature *pMonster, StructItem *pItem)` (`0x140043cc0`) pour **toute**
chose lâchée au sol par un monstre — butin **et** or (§7.3).

| offset | type | nom | source |
|---|---|---|---|
| 0/4 | — | taille = 15 / id = 282 | officiel `0x140043cf5`, `0x140043cee` |
| 7 | `uint32` | `monster_handle` | `0x140043cfd` (`GetHandle()` virtuel du monstre) |
| 11 | `uint32` | `item_handle` | `0x140043d0a` (handle de l'objet) |

Puis `ArcadiaServer::Broadcast(x / 150, y / 150, layer, trame)` (`0x140043d5c` ; la division lit l'entier
`0x1404efbf0` = **150**, la maille de région) et `ArcadiaServer::AddObject(item)` (`0x140043d6c`) — l'objet
entre ensuite dans le champ de vision par son propre `TM_SC_ENTER`, et
`ItemCollector::RegisterItem` (`0x140043d87`) le rend ramassable.

### 3.6 `TM_SC_BONUS_EXP_JP` (1004) — 13 octets, liste vide en 7.3

`sendBonusExpJpMsg` (`0x1400c1700`) : `7 + handle(4) + count(2)` = 13 avec `count = 0`. C'est le message
« bonus d'expérience » (PC-bang, événements) ; l'officiel l'envoie **vide** dans le chemin de récompense. Hors
périmètre du lot, cité pour ne pas le confondre avec 1003.

## 4. Gating de version — tout est tranché pour 7.3

| paquet / champ | condition rzu | décision 7.3 |
|---|---|---|
| `TS_SC_EXP_UPDATE` id | `1003` si `version < EPIC_9_6_3`, sinon 2003 | **1003** |
| `TS_SC_EXP_UPDATE.jp` | `uint64` si `version >= EPIC_7_3`, sinon `uint32` | **`uint64`** (c'est le piège : un `uint32` décalerait la trame) |
| `TS_SC_GOLD_UPDATE` id | `1001` si `< EPIC_9_6_3` | **1001** |
| `TS_SC_GOLD_UPDATE.gold` | `uint64` si `>= EPIC_4_1_1` | **`uint64`** |
| `TS_SC_GOLD_UPDATE.chaos` | **présent** si `version > EPIC_4_1_1` | **présent, `uint32`** |
| `TS_SC_GET_CHAOS` id | `213` si `< EPIC_9_6_3` | **213** |
| `TS_SC_GET_CHAOS.nBonusType` | `int8` si `>= EPIC_4_1 && < EPIC_9_6_2` | **`int8`** (1 octet) |
| `TS_SC_GET_CHAOS.nBonusPercent` | `int8` si `>= EPIC_4_1 && < EPIC_9_6_2` | **`int8`** (1 octet) |
| `TS_SC_GET_CHAOS.nBonus` | présent si `>= EPIC_4_1` | **présent, `int32`** |
| `TS_SC_PROPERTY` id / `value` | 507 si `< EPIC_9_6_3` ; `int64` si `>= EPIC_4_1_1` | **507** / **`int64`** |
| `TS_SC_ITEM_DROP_INFO` id | `282` si `< EPIC_9_6_3` | **282** |
| `TS_SC_BONUS_EXP_JP` id / `TS_BONUS_INFO.exp` | 1004 si `< EPIC_9_6_3` ; `int64` si `>= EPIC_6_1` | **1004** / `int64` |

Les six constantes de longueur écrites par l'exécutable 2012-11 (`0x1b`, `0x19`, `0x13`, `0xf`, `0xd`) tombent
**exactement** sur ces décisions : la lecture du client 7.3 n'a pas eu à corriger rzu.

## 5. Le flux officiel, dans l'ordre (`StructMonster::onDead`, `0x1400ba600`)

| # | étape | adresse | ce que ça produit |
|---|---|---|---|
| 1 | `StructCreature::onDead` (base), statut = 4 (`STATUS_DEAD`) dans `+0x2440`, `BroadcastStatusMessage` | `0x1400ba660`, `0x1400ba6a5` | la mort visible |
| 2 | retrait de l'index spatial (`QuadTreeItem::RemoveMe`) | | |
| 3 | si le monstre a un dresseur (`+0x2390` ≠ 0) : `ProcTame` → drapeau `+0x2398` | `0x1400ba6bd` | un monstre apprivoisé **ne donne rien** (§6, étape 8) |
| 4 | `calcPartyContribute(pKiller, vector<VIRTUAL_PARTY>)` | `0x1400ba6e9` → `0x1400b8ae0` | les contributions des membres (jusqu'à 3 retenus) |
| 5 | si `base->exp[0]` (`+0x80`) > 0 : `procEXP` | `0x1400b7bb0` | exp + JP, **et 1003** (via `AddExp` → `onExpChange` → `SendExpMsg`) |
| 6 | si `base+0xF0` ≠ 0 : script Lua de mort | `0x1400ba715` | remplace `#@pos_x@#`, `#@pos_y@#`, `#@pos_layer@#`, `#@monster_handle@#` (chaînes `0x1404086f0`, `0x1404086e0`, `0x1404086d0`, `0x1404086b8`) puis `LuaVM::RunString` — **rien sur le fil** |
| 7 | tri des parties par contribution décroissante, horodatage, position du cadavre | `0x1400b1430`, `0x1400bac1b` | la priorité de ramassage (`takePriority`, 3 entrées max) |
| 8 | si apprivoisé : saut à la fin (aucune récompense) | `0x1400baad?` | |
| 9 | malus de butin : `niveau max de la partie − niveau du monstre ≥ 10` → `max(1 − 0,2 × (écart − 10), 0)` | `0x1400baa8d-0x1400baad6`, constante `0,2` en `0x140405fc8`, clamps `max(…, 0)` | un **seul** facteur, passé aux trois tirages suivants |
| 10 | `procDropChaos(pKiller, parties, malus, bonus)` | `0x1400bac79` → `0x1400b7e20` | **213** diffusé, puis `AddChaos` → **507 « chaos »** |
| 11 | bonus de quête : si `player->chaos < 1` et quête 1032 en cours → `addChaos(…, 1,0)` | `0x1400bac8c`, `0x1400bacb6` | +1 chaos |
| 12 | `procDropGold(position, pKiller, priorité, parties, malus)` | `0x1400bacfa` → `0x1400b3ac0` | tas d'or au sol : **282** + `ENTER` |
| 13 | `procDropItem(position, pKiller, priorité, parties, malus)` | `0x1400bad29` → `0x1400ba360` | butin au sol : **282** + `ENTER` |
| 14 | purge de la liste d'ennemis (`RemoveFromEnemyList`) | `0x1400badd7-0x1400badde` | |

Le contrôle de type/rang qui ouvre `takePriority` (`0x1400ba947` : `monster+0x2468` ≠ 0 **et** `base+0x8 >= 13`) et
la priorité « tueur seul » qui en découle ne sont pas résolus quant à la colonne lue — §12.1.

## 6. D'où viennent les valeurs

Les colonnes du serveur officiel sont lues à des offsets **mesurés dans les fonctions de récompense** ; la
correspondance colonne/clé est celle du dictionnaire de `MonsterResource.rdu` (76 colonnes, dans le même ordre
que `reference/epic7part4/csv/MonsterResource.csv`).

| colonne (CSV / entité) | offset officiel | qui la lit | usage |
|---|---|---|---|
| `exp` / `Exp` | `base+0x80` | `procEXP` `0x1400b7c20` ; garde `onDead` `0x1400ba6f5` | exp de base, mis à l'échelle par la contribution |
| `jp` / `Jp` | `base+0x88` | `procEXP` `0x1400b7c27` | JP de base |
| `taming_exp_mod` / `TamingExpMod` | `base+0x7C` | `procEXP` `0x1400b7cff`, `0x1400b7d08` | multiplicateur d'exp/JP du **dresseur** |
| `gold_drop_percentage` / `GoldDropPercentage` | `base+0xB8` | `procDropGold` `0x1400b3b14` | **chance en pour cent** (comparée à `rand % 100`) |
| `gold_min` / `GoldMin` | `base+0xBC` | `procDropGold` `0x1400b3dfe` | borne basse du tirage |
| `gold_max` / `GoldMax` | `base+0xC0` | `procDropGold` `0x1400b3df7` | borne haute du tirage |
| `chaos_drop_percentage` / `ChaosDropPercentage` | `base+0xCC` | `procDropChaos` `0x1400b7e5e` | **chance en pour cent** |
| `chaos_min` / `ChaosMin` | `base+0xD0` | `procDropChaos` `0x1400b8123` | borne basse |
| `chaos_max` / `ChaosMax` | `base+0xD4` | `procDropChaos` `0x1400b811c` | borne haute |
| `drop_table_link_id` / `DropTableLinkId` | non mesuré (hors `onDead`) | `procDropItem` → `dropItemGroup` `0x1400b9d90` | table de butin (`DropGroupResource`) |
| `script_on_dead` / `ScriptOnDead` | `base+0xF0` (drapeau) | `onDead` `0x1400ba715` | script Lua de mort |
| `exp_2`, `jp_2`, `gold_min_2`, `gold_max_2`, `chaos_min_2`, `chaos_max_2` | **aucun lecteur trouvé** | — | voir §12.1 |

Types relevés dans `MonsterResource.rdu` : `exp`, `jp`, les quatre bornes or/chaos et leurs variantes `_2`
sont des **entiers 32 bits** (`8` dans le dictionnaire) ; `taming_percentage` et `taming_exp_mod` sont des
**décimaux** (`j`, 16 octets) ; `model` et `script_on_dead` sont des chaînes ; `walk_type`, `slant_type`,
`monster_type` et les cinq drapeaux `f_*` tiennent sur **un octet** (`0`/1) — d'où l'importance de ne pas
lire `monster_type` comme un entier 32 bits.

Le CSV 7.3 porte bien 76 colonnes, dont les six `_2` (indices 66-71) ; l'export 9.4 les porte aussi mais
ajoute trois colonnes absentes du 7.3.

## 7. Les formules

### 7.1 Expérience et JP (`procEXP` → `addEXP`)

- **Partage par contribution** : `exp_part = (int)(exp × fContribute)` (`procEXP` : `cvtsi2ss %r14d` puis
  `mulss 0xc(%rbx)` en `0x1400b7c9e`/`0x1400b7ca3`) et `jp_part = jp × fContribute` (`0x1400b7cb7`/`0x1400b7cbc`),
  **planché à 1** pour l'exp.
- `fContribute` (officiel `calcPartyContribute` `0x1400b8ae0`, NGemity `Monster.cpp:283-306`) :
  `0,5 × (dégâts de la partie / dégâts totaux)` + `0,1` (ou `0,4` si la première attaque date de plus de
  `MAX_FIRST_ATTACK_BONUS_TIME`) pour la partie de tête + `0,3` pour le premier attaquant + `0,1` pour le
  dernier attaquant. Un participant qui n'a pas frappé depuis 30 s est purgé (`Monster.cpp:334-337`).
- **Malus d'écart de niveau — il existe** : dans `addEXP` (variante joueur, `0x1400b73c0`), si le niveau du
  joueur récompensé est **supérieur** à celui du monstre alors
  `exp *= 1 − 0,05 × (niveau_joueur − niveau_monstre)` (coefficient `0,05` double en `0x140408458`, `1,0` en
  `0x140400730`, calcul `0x1400b754e-0x1400b757c`), **et le JP subit le même facteur** (même calcul répété
  juste après, `0x1400b7581-0x1400b75ac`).
  Pas de seuil de 10 niveaux, pas de plancher : à 20 niveaux d'écart le gain est nul.
- **Distance** : le gain n'est accordé qu'à **500 unités ou moins** du cadavre (`0x1400b75ff`, constante
  `500,0f` en `0x14040476c`).
- **Taux du serveur** : `GameRule::fEXPRate` (`0x1404f7db4`) multiplie **exp et JP** (`0x1400b766e`), chacun
  arrondi **au hasard** (`XRandom` `0x14003b620` en `0x1400b7689` et `0x1400b76d2`, `rand % 100` en `0x140402ca0`).
- **Dresseur** : `× taming_exp_mod` (`base+0x7C`) quand le bénéficiaire est le dresseur du monstre
  (`0x1400b7cff`/`0x1400b7d08`, appliqué aussi à la variante de groupe `0x1400b7830`).
- Distribution finale : `StructPlayer::AddExp(exp, jp, true)` (virtuel `*0x118`, `0x1400b7717`) →
  `onExpChange` (`0x1400dc080`) → **1003** (`SendExpMsg` appelé en `0x1400dc0c3`) → `ApplyExperience` (montée
  de niveau = fiche `socle-perte-experience.md`).
- Le familier et l'invocation du joueur suivent à 500 unités (`StructCreature::OnKill`, `0x1400980b0`) : leur
  propre courbe d'exp est hors de ce lot.

### 7.2 Chaos (`procDropChaos` `0x1400b7e20` → `addChaos` `0x1400b6c30`)

- **Chance en pour cent** : `chance = chaos_drop_percentage × fChaosDropRate(0x1404f7dc0) × malus_de_butin × bonus`
  (`0x1400b7e5e-0x1400b7e72`), comparée à `XRandom() % 100` : rien si `chance <= rand % 100`.
- Montant : `irand(chaos_min, chaos_max)` puis **arrondi au hasard** à l'entier (`0x1400b6e42-0x1400b6e9d`).
- **Diffusion** : **213 sur 25 octets** à la région (maille 150) `0x1400b6f1d`, **puis** ajout au portefeuille.
- Ajout : `StructPlayer::AddChaos` (`0x1400c0a30`) plafonne à `+0x292c` (jamais au-dessus du maximum, jamais
  sous 0), met à jour le paramètre de quête **99** (`0x1400c0a74`) et envoie **507 « chaos »** (`0x1400c0aa3`).
- Écart de niveau : la distance est **500 unités** (`0x1400b6cb8`) ; le **même** malus `0,2` que le butin est
  appliqué (`0x1400b7e6e`), plus un bonus d'appelant (temps de jeu limité, PC-bang).
- **Taxe** : en donjon de guilde, `DungeonManager::GetTaxRate` (`0x1400b81f1`) puis `GuildManager::GiveTax`
  (`0x1400b8239`, `StructGold` = couple or/chaos) prélèvent une part. Hors périmètre.

### 7.3 Or (`procDropGold` `0x1400b3ac0`)

- **Chance en pour cent** : `gold_drop_percentage × fGoldDropRate(0x1404f7db8) × malus` comparée à
  `XRandom() % 100` (`0x1400b3b14` `cvtsi2ssl 0xb8(%rax)` — la valeur est donc bien un pourcentage, pas un
  pour mille).
- Montant : `irand(gold_min, gold_max)` (`0x1400b3dfe`/`0x1400b3df7`).
- **Il tombe au sol, il n'est pas crédité** : `StructItem::AllocGold(StructGold, GenerateCode)` (`0x1400af7c0`)
  → `StructItem::AllocItem(0, 0, or, GenerateCode, -1, -1, -1, 0, …)` (`0x1400af823` avec `rcx = 0`, `rdx = 0`)
  — c'est-à-dire un **objet de code 0** dont la quantité est le montant d'or, exactement comme NGemity
  (`Globals/MemPool.cpp:168-171`, `Item::AllocItem(0, 0, gold, gcode, …)`), puis
  `MonsterDropItemToWorld` (`0x1400b4156`) → **282** + `ENTER`. La bourse ne change qu'au ramassage.
- Le tirage suit le **même** malus que le chaos (§7.1, étape 9) ; aucun multiplicateur de dresseur, aucune
  distance propre.

### 7.4 Butin (`procDropItem` `0x1400ba360` → `dropItemGroup` `0x1400b9d90` / `dropItem` `0x1400b2a30`)

- Table liée au monstre (`drop_table_link_id`), tirage par groupe (`DropGroupResource`), malus de butin, puis
  `MonsterDropItemToWorld` → **282** + `ENTER` (mêmes paquets que l'or).
- Les butins de quête sont un sous-cas : NGemity les déclenche **dans** la fonction de butin
  (`dropItemGroup` → `procQuest`, `Monster.cpp:494`, `:1325`). Le `onDead` officiel, lui, **n'appelle aucun**
  crochet de quête : Navislamia garde le sien (`CombatService.cs:389`, `QuestService.OnMonsterKilledAsync`).

## 8. Ce que Navislamia fait aujourd'hui, et ce qui s'en écarte

| point | état du dépôt | écart |
|---|---|---|
| valeurs | `CombatRewards.Compute(level)` = `10 + 5 × niveau` exp, `5 + 2 × niveau` JP, `5 + 3 × niveau` or (`CombatRewards.cs:5-16`) | **provisoire** ; `docs/reste-a-faire.md:27` et `socle-combat-reel.md:106-107` le disent |
| taux | `IRateService.Scale` sur `Exp`/`Jp`/`Gold` (`CombatService.cs:398-400`) | conforme au cadre (taux appliqués après) ; **pas de taux de chaos** |
| partage | `IPartyService.RewardMembers` + division entière, restes aux premiers (`CombatService.cs:401-409`, `socle-groupe.md:83-88`) | reste en place ; ce n'est **pas** la formule de contribution officielle (à ce jour Navislamia ne la calcule pas) |
| envois | `BuildExpUpdate` puis `BuildGoldUpdate` par bénéficiaire, `ApplyExperience` (`CombatService.cs:410-412`) | 1003 exact ; **1001 n'est pas envoyé à la mort** par l'officiel (l'or tombe au sol) |
| or | crédité en bourse (`info.AddGold`) | l'officiel = tas au sol (code d'objet 0) |
| chaos | champ `CharacterChaos` et `chaos` affiché (`GameActions.cs:246,249`), mais aucun gain de monstre | **213 absent de `GamePackets`**, aucun tirage |
| butin | `GroundItemService.DropForMonster` (`GroundItemService.cs:52`) + `MonsterDropCatalog` (tables par monstre, groupes, taux `ItemDrop`/`CreatureCardDrop`) | déjà conforme au 7.3 ; **282 non envoyé** (`ShowTo`/`ShowToNearby` diffusent `ENTER` seul), pas de tas d'or, pas de malus |
| colonnes | `MonsterResourceEntity.cs:66-79` porte les 12 colonnes + `DropTableLinkId` + `ScriptOnDead` ; migration `AddMonsterResource.cs:82-96` les crée `NOT NULL` | **la projection `MonsterResourceRepository.GetByIds` (`:32-60`) ne les demande pas** → zéro partout, sans avertissement |
| instance | `MonsterInstance` (`:22-43`) ne porte ni `Exp`/`Jp` ni or/chaos ; il porte déjà `TamingId`/`TamingPercentage` par le même patron | à étendre (précédent : `socle-apprivoisement-invocation.md` §13.4) |
| import | `tools/import_epic7.py:84` mappe `MonsterResources` par introspection (`exp`→`Exp`, `exp_2`→`Exp2`…) depuis des CSV lus sous `data/epic7/` ; `tools/Import-MonsterResourceColumns.ps1` ne porte **que 8 colonnes d'IA** | **les valeurs en base ne sont pas vérifiables ici** (pas de Postgres, chemin `data/epic7/` absent) → §A VERIFIER 6 |

## 9. Découpage — le livrable central

### 9.1 Étape 1 — le socle, **sans aucune décision de jeu** (c'est la branche à ouvrir)

Tout ce qui suit est écrit noir sur blanc dans l'exécutable officiel et n'appelle aucun arbitrage :

1. **Porter les colonnes jusqu'à l'instance** : `Exp`, `Jp`, `GoldDropPercentage`, `GoldMin`, `GoldMax`,
   `ChaosDropPercentage`, `ChaosMin`, `ChaosMax` (et, si le lot envoie aussi le butin, `DropTableLinkId` déjà
   consommé par `MonsterDropCatalog`) — d'abord dans la projection de `MonsterResourceRepository.GetByIds`,
   puis dans `MonsterInstance` et `MonsterInstanceFactory`, patron `TamingId`/`MonsterCombatStats`
   (une lecture par ressource au démarrage, **jamais une requête par mort**).
2. **Remplacer `CombatRewards`** par les colonnes : exp/JP = colonne, or = tirage `[gold_min, gold_max]`
   précédé du test de pourcentage ; chaos = tirage `[chaos_min, chaos_max]` précédé du test de pourcentage.
   `CombatRewards` et `Tests/Game/CombatRewardsTests.cs` disparaissent (le cadre le prévoit).
3. **Trois ajouts sur le fil** : `TM_SC_GET_CHAOS` (213, 25 o) diffusé à la région avec les bonus à 0 ;
   `TM_SC_ITEM_DROP_INFO` (282, 15 o) au moment de chaque dépôt au sol ; le **tas d'or** (code d'objet 0,
   quantité = montant) qui passe par le chemin de dépôt existant. Les taux restent appliqués **après**
   (`RateType.Gold` sur le montant, `RateType.ItemDrop` sur la chance), plus un taux **chaos** à ajouter
   (`RateType`/`RatesOptions`, aligné sur `GameRule::fChaosDropRate` `0x1404f7dc0`).
4. **Les deux règles de distance et d'écart de niveau qui ne sont pas des choix** :
   - distance **500 unités** pour l'exp (officiel `0x1400b75ff`, `0x14040476c`) et pour le chaos (`0x1400b6cb8`) ;
   - malus d'exp **`1 − 0,05 × écart`** quand le bénéficiaire est plus haut niveau que le monstre
     (`0x1400b7533-0x1400b757c`, `0x140408458`) ;
   - malus de butin **`max(1 − 0,2 × (écart − 10), 0)`** au-delà de 10 niveaux (`0x1400baa8d-0x1400baad6`,
     `0x140405fc8`), appliqué à l'or, au chaos et au butin.
   Ces trois règles changent le comportement actuel (aucun malus aujourd'hui) : elles sont **authentiques**,
   pas inventées, mais Killian peut vouloir les différer (§A VERIFIER 3).
5. **Tests** : un test d'offsets par paquet nouveau (taille totale et position de chaque champ — 213 → 25,
   282 → 15), plus le test de la formule pure sur un monstre **réel** de l'export présent
   (`reference/epic7part4/csv/MonsterResource.csv`) et le chemin `AwardKill` avec un groupe.

### 9.2 Étape 2 — dépend d'un autre socle

- **Contribution de groupe exacte** (`0,5 × part de dégâts + bonus premier/dernier attaquant`) : exige la liste
  des dégâts par participant et leur purge à 30 s — c'est le socle de combat/aggro (`socle-combat-reel.md`),
  pas ce lot. L'étape 1 garde le partage actuel (`socle-groupe.md` §5.1).
- **Appel des paquets de récompense par le familier/invocation** : dépend de `socle-invocations.md`.

### 9.3 Étape 3 — décisions de jeu ou de version, à ne pas deviner

- Les colonnes `*2` (§12.1) ; le seuil de rang `base+0x8 >= 13` et sa colonne (§12.2) ; le bonus de quête 1032 ;
  la taxe de guilde sur le chaos ; les bonus PC-bang / temps de jeu limité ; le script Lua de mort
  (`script_on_dead`) ; le texte de bonus 1004 ; le partage aux parties sans joueur (identifiant de groupe)
  dont l'expérience est répartie `addEXP` variante groupe (`0x1400b7830`) — Navislamia n'a pas ce cas.

**Le socle ne se scinde pas** : l'étape 1 est un lot auto-suffisant (colonnes + trois paquets + formules
authentiques), les étapes 2 et 3 sont des suites motivées. Rien n'oblige à créer une seconde carte Trello.

## 10. Ce que doit changer le trajet actuel, en clair

1. `Game/DataAccess/Repositories/MonsterResourceRepository.cs:32-60` — ajouter les colonnes de récompense à
   la projection explicite (tout ce qui n'y est pas est perdu : la requête ne ramène aujourd'hui que 24 champs).
2. `Game/Services/MonsterInstance.cs:22` — porter les valeurs figées (`Exp`, `Jp`, `GoldMin`, `GoldMax`,
   `ChaosDropPercentage`, `ChaosMin`, `ChaosMax`, les deux pourcentages) ; `MonsterInstanceFactory`
   (`IndexResources` `:83`, `AddInstances` `:97`) les recopie comme il recopie déjà les colonnes
   d'apprivoisement.
3. `Game/Services/CombatService.cs:387` — `AwardKill(client, instance.Level, …)` ne reçoit que le niveau :
   il lui faut **l'instance** (ou l'identifiant du monstre) pour lire ses récompenses, et le lieu du dépôt
   (position/layer) pour **282**. `CombatRewards` et `Tests/Game/CombatRewardsTests.cs` sortent du chemin.
4. `Game/Services/GroundItemService.cs:52` — envoyer **282** (handle du monstre, handle de l'objet) à la
   région au moment du dépôt, et accepter un dépôt d'or (code d'objet 0, quantité = montant).
5. `Game/Network/Packets/Enums/GamePackets.cs` — déclarer `TM_SC_GET_CHAOS = 213` et
   `TM_SC_ITEM_DROP_INFO = 282` **avec** leurs bras de dispatch (aucun membre ne doit atteindre le `switch`
   final), et les constructeurs correspondants (27/25/15 octets, écrits après `WriteChecksum`).
6. `RateType`/`RatesOptions` — un taux de chaos (`GameRule::fChaosDropRate`).
7. `docs/gm-commands.md` — la table *Rates* gagne la ligne chaos.

## 11. Écarts assumés avec NGemity

| point | NGemity | officiel 2012-11 | choix |
|---|---|---|---|
| unité des pourcentages | `rand % 100 >= gold_drop_percentage` (`Monster.cpp:590`) | identique (`XRandom % 100`) | **pour cent**, tranché |
| malus de butin | `max(1 − (écart − 10) × 0,2, 0)` (`:173-174`) | identique (`0x140405fc8`) | identique |
| malus d'exp | NGemity l'applique **aussi** dans `World::addEXP` (`World.cpp:492`) | `1 − 0,05 × écart` dans `addEXP` | suivre l'officiel (0,05 par niveau) |
| colonnes `*2` | chargées en tableaux `[2]` (`Globals/ObjectMgr.cpp:255-266`) mais **seul l'indice 0 est lu** (`:137`, `:347-348`, `:590`, `:593`, `:1368`) | aucun lecteur trouvé | §12.1 — `NON ÉTABLI`, ne rien porter |
| or | `AllocGold` → objet **code 0** au sol (`MemPool.cpp:168`) | identique (`0x1400af7c0`) | identique |
| chaos | 213 diffusé à la région + `AddChaos` (`World.cpp:730-756`) | 213 **puis** 507 « chaos » | suivre l'officiel (les deux) |
| butin | `drop_item_id` (table plus ancienne) | `drop_table_link_id` + `DropGroupResource` | Navislamia suit déjà le 7.3 |
| butin de quête | `procQuest` appelé **dans** le butin (`:494`) | aucun appel dans `onDead` | garder le crochet de quête existant |
| script de mort | pas de Lua de mort dans `Monster::onDead` | `script_on_dead` exécuté (`LuaVM::RunString` `0x1400ba849`) | hors lot (étape 3) |

## 12. NON ÉTABLI

1. **Les colonnes `*2`** — aucun lecteur trouvé, ni dans l'exécutable (les seules lectures de la famille
   récompense sont `exp +0x80`, `jp +0x88`, `gold_drop_percentage +0xB8`, `gold_min +0xBC`, `gold_max +0xC0`,
   `chaos_drop_percentage +0xCC`, `chaos_min +0xD0`, `chaos_max +0xD4`), ni dans NGemity qui les charge mais
   n'utilise que l'indice 0. Question précise : **quelle condition fait basculer un serveur 7.3 sur
   `exp_2`/`jp_2`/`gold_min_2`/`gold_max_2`/`chaos_min_2`/`chaos_max_2` ?** (hypothèse non vérifiée : un
   mode événementiel — rien dans le chemin de mort ne la porte). Les valeurs restent donc **à ne pas porter**.
2. **Le seuil de rang du butin** — `onDead` teste `monster+0x2468 != 0` **et** `base+0x8 >= 13` (`0x1400ba947`)
   avant de basculer la priorité de ramassage sur le tueur ; NGemity teste `monster_type >= 31` (`:149`,
   `:186`) et saute alors or **et** butin. Quelle colonne officielle est en `base+0x8` n'a pas été résolue
   (la structure en mémoire du serveur réordonne les colonnes de la table). Question précise : **est-ce
   `monster_type`, et le seuil est-il 13 ou 31 en 7.3 ?**
3. **La part d'or/chaos par membre en cas de groupe** : l'officiel tire **une** fois par monstre puis répartit
   proportionnellement à la contribution (`procDropGold` reçoit le vecteur de parties) ; Navislamia tire par
   site de dépôt. Les arrondis exacts du partage d'or (comme du butin) n'ont pas été tracés.
4. **Le ramassage d'un tas d'or** : le monstre lâche un objet de code 0 ; à quel moment le code 0 devient de
   l'or dans la bourse côté serveur 7.3 (le chemin de ramassage `TakeAsync`) n'a pas été lu — c'est le socle
   de ramassage, pas celui-ci.
5. **`SendPropertyMessage`** : je n'ai pas établi le nom de la fonction (« +0x20 » dans la PDB) ; je déduis
   `TM_SC_PROPERTY` de la chaîne `"chaos"` (`0x1404097d8`) et de la signature `(player, handle, nom, int64)`
   (`0x14014b340`) — le format 507 est confirmé par ailleurs (ruzu + `BuildProperty` du dépôt).
6. **Le nombre de lignes en base** (7 431 annoncées par la carte) : non vérifiable sans Postgres.

## 13. Mesures de ce réveil (base `45f3571`)

- `dotnet build Navislamia.sln -c Debug` → **exit 0** (`NUGET_PACKAGES=/srv/navislamia/.nuget-cache`).
- `dotnet test Tests/Tests.csproj` → **exit 0** : `Passed! - Failed: 0, Passed: 2554, Skipped: 0, Total: 2554`.
  Les tests sont **NUnit** (`[Test]`/`[TestCase]`) : compter les `[Fact]` renvoie 0.
- **Collision : 0 MR ouverte** (`GET /repos/KillianDoubre/Navislamia/pulls?state=open` → `[]`, 83 branches
  distantes) ; **aucune** branche distante ne porte de commit touchant les fichiers du lot
  (`CombatRewards.cs`, `CombatService.cs`, `MonsterInstance*.cs`, `MonsterResourceEntity.cs`,
  `MonsterResourceRepository.cs`, `GroundItemService.cs`, `GamePackets.cs`) : zone de recouvrement **nulle**.

## 14. Commits épinglés

- `reference/rzu` @ `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (relevé ce réveil, `git rev-parse HEAD`).
- `reference/ngemity/Chihiro` @ `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (relevé ce réveil).
- serveur officiel 2012-11 (hors git) : `CaptainHerlockServer.exe`
  sha256 `83b54fe18f15601578e35bcad1832a5c254bb09f512ab6297217fa49d9dd3eac`,
  `CaptainHerlockServer.symbols.tsv` sha256 `aef7e6bc8d436a3641fa5a589b53c557ae940e6b6ed4b555a648f8aab7c7e7f3`.
- export `reference/epic7part4/csv/MonsterResource.csv` sha256 `d2ad9977f6731b0827c2e90eb71690e2982312a78527285948de0f5f135c9e13` ;
  table officielle `reference/epic7part4/rdu/MonsterResource.rdu` sha256 `c720d58b21c3239f13dddf0aabc608505329205f6818e09d82b5934234703ca0`.
- dépôt, base du lot : `master` `45f35713c5ee8e300c5ecdbdeed7e006bf3464ea`.

---

## A VERIFIER PAR KILLIAN

1. **Colonnes `*2` (§12.1)** : personne ne les lit, ni dans l'exécutable 2012-11, ni dans NGemity (qui les
   charge et n'utilise que l'indice 0). Les ignorer est le choix de l'étape 1 ; si tu sais quel mode les
   active (événement, double butin…), dis-le et je les tranche.
2. **Seuil de rang (§12.2)** : `base+0x8 >= 13` côté officiel contre `monster_type >= 31` chez NGemity. Un
   monstre de raid ne donne ni or ni butin chez NGemity — veux-tu cette règle dans l'étape 1, et avec quel
   seuil ?
3. **Malus de distance et d'écart de niveau (§9.1 point 4)** : ils changent le comportement actuel (aujourd'hui
   une récompense ne dépend ni de la distance ni du niveau). Ce sont des règles authentiques, mais si tu
   préfères un serveur indulgent, il faut le dire : je les mets en étape 1 ou en étape 3.
4. **Politique de jeu non tranchable ici** : répartition exacte de l'or/du chaos entre membres d'un groupe
   (l'officiel tire une fois puis répartit à la contribution, Navislamia divise à parts entières) ; arrondis
   aléatoires des taux ; plafonds d'or/chaos ; **ce que devient le butin quand le sac est plein ou quand plus
   personne n'est dans la plage de ramassage** (l'officiel le laisse au sol jusqu'à expiration).
5. **Le familier qui porte le coup fatal** : l'officiel attribue au maître ; Navislamia n'a pas ce cas
   aujourd'hui. À confirmer en jeu.
6. **Les valeurs en base sont-elles remplies ?** L'entité et la migration portent les 12 colonnes, mais
   l'importeur Epic 7 lit des CSV sous `data/epic7/` (absent de ce VPS) et l'outil PowerShell ne couvre que
   8 colonnes d'IA. Sans Postgres ici, impossible de vérifier que `Exp`/`GoldMin`… ne valent pas 0 : **c'est
   le seul prérequis d'infrastructure du lot** (si elles sont vides, il faut un import avant le code).
7. **Confirmation en jeu** : un tas d'or au sol est-il bien l'objet de code 0 pour le client 7.3, et le
   ramassage d'un tel objet crédite-t-il la bourse ?

## Bloc destiné à `CLAUDE.md` (à recopier tel quel dans la description de la MR — `navis-ref` n'écrit pas `CLAUDE.md`)

```markdown
**Monster rewards come from the resource row, not from a formula** (`docs/packet-specs/socle-recompenses-monstres.md`):
experience and JP are `tf_monster_resource.exp` / `.jp`, gold is a roll in `[gold_min, gold_max]` behind a
`gold_drop_percentage` **per-cent** check (`rand % 100`), chaos the same behind `chaos_drop_percentage`, and
items keep coming from `drop_table_link_id` / `DropGroupResource`. Values are carried on `MonsterInstance`
(projected by `MonsterResourceRepository`) so a kill never queries the database — same pattern as
`TamingId` and `MonsterCombatStats`. The official server drops **gold as a ground item of code 0** (amount =
count), never as a wallet credit: the magic item codes stay in `GameCharacterPackets`. Three authentic rules
accompany the columns: exp/JP lose `0.05 × (player level − monster level)` when the beneficiary outlevels the
monster, gold/chaos/items lose `max(1 − 0.2 × (level gap − 10), 0)` beyond ten levels, and exp/chaos are only
granted within **500 units**. Chaos gain broadcasts `TM_SC_GET_CHAOS` (213, 25 bytes, region message) and then
sends the `chaos` property (507); a ground drop announces `TM_SC_ITEM_DROP_INFO` (282, 15 bytes) before the
item's `ENTER`. The six `*2` columns (`exp_2`, `gold_min_2`, …) have **no reader in either the official server
or NGemity** — they stay unported until their trigger is known.
```
