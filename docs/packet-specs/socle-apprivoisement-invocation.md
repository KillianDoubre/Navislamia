# Apprivoisement et invocation des créatures — la carte liée, les drapeaux et `TM_SC_TAMING_INFO` (Epic 7.3)

Fiche du socle « apprivoisement et invocation des créatures » (carte Trello `SorqyDvt`, carte Hermes
`t_848a443a` → dev `t_eb763df1` → QA `t_cbe3c289`). Elle est écrite par `navis-ref` et ne touche pas au code
serveur. Le découpage de §11 est le livrable central : il dit ce qui est implémentable sans aucune décision de
jeu, puis ce qui reste, dans l'ordre des dépendances.

**Méthode.** Aucun exécutable du client n'a été lancé, aucun Lua, aucun script. La lecture a porté sur :
`reference/rzu` (format de fil et gating), `reference/ngemity/Chihiro` (logique), `reference/client73` (les 50
`db_*.rdb` extraits de `data.000`, `SFrame.exe` lu en clair), `reference/sqlserver/Arcadia` (**chemin réel ;
la carte dit `data/sqlserver/Arcadia`, qui n'existe pas**), `docs/` et `CLAUDE.md`.

Les trois constats de la carte sont confirmés et chiffrés :

1. `SummonWorldService` n'est appelé par personne dans l'arbre : ses 27 tests
   (`Tests/Game/SummonWorldTests.cs`) sont son unique client, et `Enter`/`Leave` ne sont injectés nulle part
   (`docs/packet-specs/socle-invocation-monde.md` §13.2, qui l'assume : « le service n'est injecté nulle part,
   volontairement »). Le premier appelant n'est donc **toujours pas livré** (§5.4, §11).
2. `CharacterEntity` porte bien `MainSummonId` (et `SubSummonId`) et `SummonSlotItemIds`
   (`Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:63,65,68`), le contexte porte
   `DbSet<SummonEntity> Summons` (`Game/DataAccess/Contexts/TelecasterContext.cs:22`) et `SummonEntity` porte
   `SummonResourceId` + `CardItemId` — mais **aucun code ne les remplit** (§5.3, §7 point 7).
3. `MonsterResourceEntity.TamingId`, `CreatureTamingCode`, `TamingPercentage`, `TamingExpMod` existent
   (`Game/DataAccess/Entities/Arcadia/MonsterResourceEntity.cs:62-65`), sont mappés
   (`Game/DataAccess/Contexts/ArcadiaContext.cs:133`) — et **rien d'autre ne les lit** (grep complet du
   dépôt) : ni le catalogue de monstres, ni la voie d'invocation. L'apprivoisement est donc entièrement à
   câbler.

---

## 1. Identité

Le lot n'introduit **qu'un seul id de paquet** : `TM_SC_TAMING_INFO` (310). Tout le reste voyage sur des ids
déjà déclarés ou déjà spécifiés par les fiches sœurs.

| élément | id / valeur | source |
|---|---|---|
| `TM_SC_TAMING_INFO` | **310** (`1310` à partir d'`EPIC_9_6_3`) | `reference/rzu/librzu/src/packets/GameClient/TS_SC_TAMING_INFO.h:10-12` |
| `TM_CS_SKILL` — le geste qui porte les sorts 4001/4002/4003 | **400** (`1400` à partir d'`EPIC_9_6_3`) | `TS_CS_SKILL.h:17-19` ; déclaré chez nous `Game/Network/Packets/Enums/GamePackets.cs:107` |
| sort d'invocation | skill `4001`, effet **`EF_SUMMON = 601`** | export `SkillResource.csv` ligne 4001 (`effect_type` 601) ; `Chihiro/src/Skills/SkillBase.h:344` |
| sort de renvoi | skill `4002`, effet **`EF_UNSUMMON = 602`** | export ligne 4002 ; `SkillBase.h:345` |
| sort d'apprivoisement | skill **`4003`**, effet **`603`** | export ligne 4003 (`effect_type` 603, `var1 = 0.060`, `var2 = 0.0300`) ; `SkillBase.h:197` `SKILL_CREATURE_TAMING = 4003` |
| second sort d'effet 603 | `4004` — **absent de la table de compétences du client 7.3** (voir §2.3) | export ligne 4004 ; relevé `db_skill.rdb` |
| `TS_SC_ADD_SUMMON_INFO` | 301, **46 octets** en 7.3 | `TS_SC_ADD_SUMMON_INFO.h:7-19` ; `docs/packet-specs/socle-invocations.md` §1 |
| `TS_SC_REMOVE_SUMMON_INFO` / `TS_SC_UNSUMMON` | 302 / 305, **11 octets** chacun | `TS_SC_REMOVE_SUMMON_INFO.h`, `TS_SC_UNSUMMON.h` ; `socle-invocations.md` §1 |
| `TM_EQUIP_SUMMON` | 303, **32 octets** | `TS_EQUIP_SUMMON.h:7-15` ; `socle-invocations.md` §1 |
| drapeau « carte apprivoisée en cours » | `ITEM_FLAG_TAMING = 0x2000_0000` | `Chihiro/src/Entities/Item/ItemTemplate.hpp:174` |
| drapeau « carte liée à une créature » | `ITEM_FLAG_SUMMON = 0x8000_0000` | `ItemTemplate.hpp:176` |
| groupe d'objet des cartes | `ItemGroup::GROUP_SUMMONCARD = 13` | `ItemTemplate.hpp` (`GROUP_SUMMONCARD`) ; chez nous `Game/DataAccess/Entities/Enums/ItemGroup.cs` (`Summoncard = 13`) |
| carte liée d'exemple | item `540001` (« Siren Card »), `ItemResource.summon_id = 1401` | export `ItemResource.csv` ligne 540001 ; `db_string.rdb` `10540001` |
| cartes d'apprivoisement | `540200`/`540201`/`540202`/`540203`, `summon_id = 0` | export `ItemResource.csv` lignes 540200-540203 ; `db_string.rdb` `10540200-10540203` |

Les trois libellés des cartes d'apprivoisement, lus dans `db_string.rdb` (ids de texte `10540200` à
`10540203`), disent à eux seuls la règle de correspondance carte↔cible :

- `[Rare] Creature Taming Card<All monsters of new rare level>`
- `[Special] Creature Taming Card<All monsters of new special level>`
- `[Unique] Creature Taming Card<All monsters of new unique level>`
- `[Exotic] Creature Taming Card<All monsters of new veracruz level>`

---

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Le geste, tel que le client 7.3 le décrit lui-même

La description du sort d'apprivoisement est dans `db_string.rdb`, id de texte **`40004002`**, et elle donne
toute la mécanique :

> `Creature Taming Lv#@level@# … Attack Spell … Attempts to tame the selected monster. You need to have the
> proper type of creature card or empty loot pet that corresponds with the target. If the tame fails, the card
> will be lost. Cool Time 20 seconds`

Trois faits en découlent, chacun recoupé plus loin :

1. il faut **la bonne carte** (« corresponds with the target ») : c'est la carte d'apprivoisement du **grade**
   de la créature visée (§1, §5.1) ;
2. **l'échec détruit la carte** (« the card will be lost ») — identique à `World::ProcTame` (§5.1) ;
3. « Cool Time 20 seconds » **et** « Attack Spell » désignent sans ambiguïté la ligne `4003` de l'export
   (`cast_range = 20`, `valid_range = 20`, `is_harmful = 1`, `is_need_target = 1`,
   `delay_cooltime = 20.00`, `cost_mp = 80`), **pas** la ligne `4002` (`is_harmful = 0`, `is_need_target = 0`,
   `delay_cooltime = 0.00`, `cost_mp = 5`, `cast_range = 0`). C'est la preuve que le sort d'apprivoisement
   est bien `4003` et que les libellés du client sont décalés d'un cran (§2.3).

Côté binaire, `SFrame.exe` (lecture de chaînes, aucun exécution) porte `MSG_TAMING_INFO` (case de dispatch) et
la classe `USMSG_TAMING_INFO`, l'effet de jeu `USGameTamingEffect`, la ressource
`rcfx_taming_intangible_lv01.nx3`, la fenêtre `window_quest_creature_taming%02d.nui`, les trois clés de message
système `TAMING_START`, `TAMING_SUCCESS`, `TAMING_FAILED` — **exactement les trois que NGemity envoie**
(`Chihiro/src/Network/Messages.cpp:813-827`) —, le code de résultat `RESULT_ALREADY_TAMING`, et
`USkillSummonUnsummon` (une seule classe d'affichage pour les deux sorts d'invocation/renvoi).

### 2.2 Les deux gestes d'invocation et de renvoi

- **Invocation.** La carte doit être **liée** : côté serveur c'est le bit `ITEM_FLAG_SUMMON` porté par la ligne
  d'objet (`ItemFixedInfoWriter.cs:93`, champ `flag` @34 du motif de 75 octets) ; côté NGemity c'est
  l'appartenance aux six cartes liées (`m_aBindSummonCard`, `Player.h:353`) et le fait que la carte ait déjà
  une invocation (`pItem->m_pSummon`). Le joueur sélectionne la carte dans la fenêtre de créature (303) puis
  lance le sort `4001` — c'est ce que la référence appelle « summon creature skill »
  (`docs/packet-specs/304-summon.md` §2.3, citation de `db_string.rdb`).
- **Renvoi.** Le sort `4002` (effet 602) sur la carte liée : `Skill::DO_UNSUMMON` exige la même carte
  (groupe 13, propriétaire, invocation du joueur) et que l'invocation **soit dans le monde**
  (`Skill.cpp:1558-1577`).
- **Apprivoisement.** Le joueur lance `4003` sur un **monstre**, puis il le tue : l'invocation de la carte se
  décide à la mort du monstre (§5.1). Le client ne connaît ni 304 ni 323 comme déclencheurs — 304 reste sans
  émetteur (`304-summon.md` §1, §5.1).

### 2.3 Décision sur les ids des trois sorts — le décalage des libellés du client

Le relevé client est gênant et doit être écrit noir sur blanc, parce qu'il est la source du seul piège de ce
lot :

| où | `4001` | `4002` | `4003` | `4004` |
|---|---|---|---|---|
| export 9.4, `effect_type` | **601** | **602** | **603** | 603 |
| export 9.4, `text_id` / `tooltip_id` | 50004001 / 40004001 | 50004002 / 40004002 | 50004003 / 40004003 | 50004003 / 40004004 |
| client 7.3, `db_skill.rdb` (id texte @4, `cost_mp` @38) | 50004001, **60 MP** | 50004002, **5 MP** | 50004003, **80 MP** + les deux flottants `0.06f` @273 et `0.03f` @277 | **absent** |
| `db_string.rdb` pour ces ids | « `<size:9>`Recall Creature » | « `<size:9>`Creature Taming » | outil d'effet « Disguise \<Skeleton\> » (id réutilisé) | — |

Ce qui tranche, dans l'ordre :

1. **Les nombres du client coïncident un pour un avec l'export** : `cost_mp` 60/5/80, et surtout le client
   porte sur **`4003`** les deux coefficients `0.06f` et `0.03f` — or `World::ProcTame` est le **seul** code
   des deux références qui consomme des variables du sort d'apprivoisement, et il consomme `var[0]` et `var[1]`
   (`World.cpp:639-641`). Ces deux flottants ne peuvent donc être que les coefficients d'apprivoisement.
2. **NGemity nomme `4003`** : `SKILL_CREATURE_TAMING = 4003` (`SkillBase.h:197`) et la branche
   `if (GetSkillId() == SKILL_CREATURE_TAMING)` (`Skill.cpp:294`) est la seule entrée de l'apprivoisement.
3. **Le texte « Creature Taming » décrit `4003`** et non `4002` : « Cool Time 20 seconds » et « Attack
   Spell » ne collent qu'à la ligne `4003` (§2.1). Le libellé *stocké* sous l'id `…4002` décrit donc le sort
   `4003` : les ids de texte de cette famille sont **décalés d'un cran** dans le client — ce que confirme le
   troisième cran (`50004003` / `40004003` ne portent plus rien d'utile : un outil d'effet « Disguise », et un
   « Pass Damage » qui est un tout autre sort), et ce que confirme aussi le nom orphelin
   « `<size:9>`Summon Creature », présent dans `db_string.rdb` sous l'id `40065065` (famille d'ids
   antérieure), rattaché à **aucune** compétence de `db_skill.rdb`.

**Décision pour 7.3 : `4001` = invocation (601), `4002` = renvoi (602), `4003` = apprivoisement (603).**
Le libellé affiché au joueur n'est pas une contrainte du fil : le serveur reçoit l'id et applique son effet.
Aucun développement ne doit se fonder sur le *nom* du sort (§7 point 1) ni sur `4004`, absent du client.

---

## 3. Structure sur le fil

### 3.1 `TM_SC_TAMING_INFO` (310) — 16 octets, en-tête rzu de 7 octets inclus

| offset | type | nom | valeur observée | source |
|---|---|---|---|---|
| @0-3 | `uint32` | `length` | 16 | en-tête de session (convention du dépôt, `GameCharacterPackets.CreatePacket`) |
| @4-5 | `uint16` | `id` | **310** | `TS_SC_TAMING_INFO.h:11` (7.3) |
| @6 | `int8` | `checksum` | — | convention du dépôt (`WriteChecksum`) |
| @7 | `int8` | `mode` | **0** début (NGemity `World.cpp:709`), **1** abandon (fin par `ClearTamer(…, true)`, `Monster.cpp:1156`), **2** réussite (`World.cpp:654`), **3** échec (`World.cpp:672`) | `TS_SC_TAMING_INFO.h:6` ; `Messages.cpp:813-827` (`0`, `1` et `3` → `TAMING_START` / `TAMING_FAILED`, `2` → `TAMING_SUCCESS`) |
| @8-11 | `ar_handle_t` (`uint32`) | `tamer_handle` | handle du **joueur** qui apprivoise | `TS_SC_TAMING_INFO.h:7` ; `Messages.cpp:807` |
| @12-15 | `ar_handle_t` (`uint32`) | `target_handle` | handle du **monstre** visé | `TS_SC_TAMING_INFO.h:8` ; `Messages.cpp:808` |

Charge utile 9 octets (1 + 4 + 4) ; **total 16**. Le même compte figure déjà à
`docs/packet-specs/socle-invocations.md` §1 (ligne « 310 | SC_TAMING_INFO | 1 + 4 + 4 = 9 | **16** »).

Ordre des trames, tel que les deux références l'imposent : `310` est **diffusé à la région du monstre**
(`sWorld.Broadcast`, `Messages.cpp:809-810`), pas envoyé à l'apprivoiseur seul — les trois autres joueurs
voient la tentative. Chaque émission est suivie d'un message système au joueur (`Messages::SendChatMessage(100,
"@SYSTEM", …)`, `Messages.cpp:829`), avec les clés `TAMING_*` que le client connaît (§2.1). **Aucune contrainte
d'ordre entre 310 et les autres trames n'est démontrable sans client vivant** (§7 point 4).

### 3.2 Les trames d'invocation déjà spécifiées

Ne sont pas re-spécifiées ici, elles sont acquises : 301 (46 octets : `card_handle` @7, `summon_handle` @11,
`name` @15 sur 19 octets, `code` @34, `level` @38, `sp` @42), 302 (11), 305 (11), 303 (32), plus la trame
d'entrée dans le monde `TS_SC_ENTER` (3) composée par `GameSpawnPackets.BuildEnterSummon`
(`docs/packet-specs/socle-invocations.md` §1, §3 ; `socle-invocation-monde.md` §3.1, §13.1). Ce lot **ne
change aucun de ces offsets**.

### 3.3 Le seul champ que ce lot ajoute à une trame existante : le drapeau de carte

Le bit `ITEM_FLAG_SUMMON`/`ITEM_FLAG_TAMING` voyage dans le **champ `flag` du motif d'objet**, 4 octets à
l'offset **34** (`ItemFixedInfoWriter.cs:93`), dans les deux familles qui l'utilisent : la liste d'inventaire
(`TM_SC_INVENTORY`, 85 octets par objet) et le motif nu de 75 octets. Conséquence directe pour le développeur,
déjà écrite dans le dépôt : la valeur stockée est **le masque retail**, pas le membre d'énumération —
`ItemFlag.Summon` vaut **31** (un indice de bit) alors que le client attend `0x8000_0000`
(`Game/Services/GroundItemDropRules.cs:20-25,32-40` : `SummonFlagMask = 0x80000000u`,
`IsBoundSummonCard` lit `unchecked((uint)flag) & SummonFlagMask`). **Une carte liée s'écrit donc
`(ItemFlag)0x80000000u`, jamais `ItemFlag.Summon`.** De même `ITEM_FLAG_TAMING` s'écrit `(ItemFlag)0x2000_0000u`.

### 3.4 Ce que le lot n'ajoute pas au fil

Aucun paquet client→serveur : le geste est `TM_CS_SKILL` (400, 22 octets, `Socle` §3 : `skill_id` @7,
`caster` @9, `target` @13, `x/y/z` @17/21/25, `layer` @29, `skill_level` @30), la carte visée étant
`target` (handle de l'**objet**) côté NGemity (`PrepareSummon`, `Skill.cpp:611-613`), et le **monstre** étant
`target` pour l'apprivoisement (`PrepareTaming`, `Skill.cpp:1616`). Aucun des deux n'utilise un paquet dédié —
c'est ce qui clôt le `NON ÉTABLI` 4 du socle des invocations (304 **ou** sort 400) : **le sort**.

---

## 4. Gating de version — décisions prises pour 7.3

1. **Id de 310.** `X(310, version < EPIC_9_6_3)` / `X(1310, version >= EPIC_9_6_3)`
   (`TS_SC_TAMING_INFO.h:11-12`). **Pour 7.3 : `310`.** `1310` ne doit pas être déclaré (§5.4 : un membre
   déclaré sans bras atteint le `switch` qui lève).
2. **`mode` reste sur 1 octet** : aucun gating sur ce champ (`TS_SC_TAMING_INFO.h:6`), et `int8_t` ne bouge
   pas entre 7.3 et 9.8.1 (`// Last tested: EPIC_9_8_1`, ligne 5).
3. **Les deux handles restent des `ar_handle_t` = `uint32`** : aucun gating (`:7-8`).
4. **Le geste est `400`, pas `1400`** : `X(400, version < EPIC_9_6_3)` (`TS_CS_SKILL.h:18`). Déjà tranché et
   déclaré chez nous (`GamePackets.cs:107`), donc rien à faire — mais **`1400` ne doit jamais être déclaré
   sous le nom `TM_CS_SKILL`**.
5. **Rappel des décisions sœurs qui s'appliquent telles quelles** : nom de 301 en **19 octets**
   (`version < EPIC_9_6`, `socle-invocations.md` §4 point 2), et les ids 3xx de toute la famille
   (`socle-invocations.md` §4 point 1).
6. **Aucun champ de ce lot n'est gaté sur autre chose que ces bascules d'id** : `tamer_handle`,
   `target_handle`, `mode`, les deux masques de drapeau et les sorts 4001-4003 sont identiques en 7.3 et en
   9.4 — seuls changent les **libellés** (§2.3), jamais les valeurs.

---

## 5. Traitement attendu

Toutes les références ci-dessous sont dans `reference/ngemity/Chihiro` (commit épinglé §8).

### 5.1 L'apprivoisement, du lancer à la carte liée (chaîne complète)

| étape | ce que fait la référence | source |
|---|---|---|
| 1. entrée | `Skill::ProcSkill` : si `GetSkillId() == SKILL_CREATURE_TAMING` (4003) → `PrepareTaming(handle)` **avant** toute autre validation d'effet | `Skill.cpp:293-297` |
| 2. contrôles | propriétaire = joueur ; `target` = **un monstre vivant** ; le monstre est **apprivoisable** (`GetTameItemCode() != 0`) ; le monstre n'est **pas déjà apprivoisé** (`GetTamer() != 0` → `TS_RESULT_ALREADY_TAMING`) ; le monstre est à **PV pleins** (`ts_result NOT_ENOUGH_HP` sinon) ; le joueur **n'apprivoise pas déjà** quelqu'un d'autre (`TS_RESULT_ALREADY_TAMING`) | `Skill.cpp:1611-1638` |
| 3. la carte requise | `Monster::GetTameItemCode()` = `MonsterResource.taming_id`, puis, si non nul, le **`card_id` du `SummonResource`** correspondant | `Monster.cpp:933-942` |
| 4. la carte en sac | `FindItem(nTameItemCode, ITEM_FLAG_SUMMON, false)` : **le code exact** et **sans le bit `ITEM_FLAG_SUMMON`** (une carte *vide*, pas encore liée) — `Inventory::Find` renvoie l'objet dont `(flag & itemFlag) != 0` vaut `bFlag` | `Skill.cpp:1632-1633`, `Inventory.cpp` (`Find`), `Player.cpp:2197` |
| 5. marquage | `SetTamer` : la carte reçoit `ITEM_FLAG_TAMING` (`\| 0x20000000`) + `DBUpdate()` ; le joueur reçoit le message d'objet (donc la carte mise à jour à l'écran) ; le monstre mémorise le dresseur et le niveau du sort ; **310 mode 0** est diffusé à la région | `World.cpp:698-712` |
| 6. la fenêtre | le dresseur doit **tuer le monstre** : chaque coup du dresseur sur le monstre repousse l'échéance de 30 s (`m_nTamedTime += ct + 30000`) ; sans coup, l'échéance expirée **efface le dresseur en silence** (`m_hTamer = 0`, `m_nTamedTime = -1`, aucun 310) ; à la **mort** du monstre, `onDead` appelle `ProcTame` si `GetTamer() != 0` | `Monster.cpp:116-117`, `:436-439`, `:121-134` |
| 7. le tirage | `World::ProcTame` vérifie que la carte porte encore `ITEM_FLAG_TAMING`, calcule la probabilité (`taming_percentage` du monstre × les `var` du sort × modificateurs) et tire au sort | `World.cpp:624-648` |
| 8. réussite | la carte passe à **`(flag & 0xDFFFFFFF) \| 0x80000000`** (retrait de `TAMING`, pose de `SUMMON`), `DBUpdate()`, message d'objet au joueur, **310 mode 2**, et `m_bTamedSuccess = true` — ce qui **supprime le butin** de la mort (`if (!m_bTamedSuccess)` enveloppe tout le chemin de drop) | `World.cpp:650-656`, `Monster.cpp:128-140` |
| 9. échec | `EraseItem(pItem, 1)` — **la carte est détruite** — puis **310 mode 3** | `World.cpp:659-673` |
| 10. abandon | `World::ClearTamer(monster, true)` : retire le drapeau `TAMING` de la carte et diffuse **310 mode 1** ; c'est le seul émetteur du mode 1 (`Monster.cpp:1156`, branche du timer de patrouille) | `World.cpp:683-696`, `Monster.cpp:1156` |

La probabilité n'est pas un pourcentage fixe : `MonsterResource.taming_percentage` est le facteur de base,
et les deux colonnes `var1`/`var2` du sort d'apprivoisement (0,060 et 0,030 dans l'export, et les deux mêmes
flottants dans la table du client 7.3, §2.3) l'ajustent. Le facteur d'expérience de la créature
apprivoisée est `taming_exp_mod` (`Monster.h:126`, `Monster.cpp:357-365`).

Ce que le serveur doit donc **répondre** : un `310` de 16 octets (mode 0 à 3) diffusé à la région du monstre,
et, à chaque changement de drapeau, la **ligne d'objet de la carte** (motif de 75 octets, `flag` @34) au
joueur — c'est ce que fait NGemity par `Messages::SendItemMessage` → `TS_SC_INVENTORY` avec un seul objet
(`Messages.cpp:250-260`), c'est-à-dire, chez nous,
`GameCharacterPackets.BuildInventory(new[] { card })` (`Game/Services/StorageService.cs:183`,
`Game/Services/GroundItemService.cs:243`, `Game/Services/GmCommands/GmCommandService.cs:436`).

### 5.2 L'invocation et le renvoi par sort

| étape | référence | source |
|---|---|---|
| contrôle d'invocation | `Skill::PrepareSummon(handle, pos)` : `target` = **l'objet** ; il doit être du groupe 13, appartenir au joueur, être **une des six cartes liées** (`m_aBindSummonCard`), avoir une invocation et que celle-ci **ne soit pas déjà dans le monde** | `Skill.cpp:604-648` |
| placement | la position d'arrivée est **bruitée** : `AddNoise(rand32(), rand32(), 70)`, puis on retire jusqu'à sortir d'un rayon de 24 unités de la position du maître | `Skill.cpp:639-647` |
| effet | `DO_SUMMON()` : retrouve la carte par le handle cible, revalide groupe + propriétaire + invocation, puis `Player::DoSummon(summon, pos)` — qui **renvoie d'abord le familier principal courant** (`DoUnSummon(m_pMainSummon)`), pose `m_pMainSummon`, place et fait entrer l'invocation dans le monde | `Skill.cpp:1537-1556`, `Player.cpp:1574-1592` |
| renvoi | `DO_UNSUMMON()` : mêmes contrôles, puis `Player::DoUnSummon` — qui **diffuse `305`** à la région du familier (plus une copie directe au maître s'il n'est pas dans une région visible) et le retire du monde | `Skill.cpp:1558-1577`, `Player.cpp:1594-1613` |
| réponse 301 | `Player::AddSummon(summon, true)` → `Messages::SendAddSummonMessage` : **301** puis, dans l'ordre, les statistiques, PV/MP, niveau, exp, **liste de compétences du familier**, SP | `Player.cpp:1496-1505`, `Messages.cpp:99-120` |
| ordre imposé | 301 **avant** l'entrée dans le monde (chez NGemity : `SendLoginProperties`, `Messages.cpp:772-778` puis `AddSummonToWorld` @`Player.cpp:818-824`) ; le renvoi diffuse 305 **avant** la sortie du monde (`Player.cpp:1602-1612`) | `socle-invocation-monde.md` §13.2 |

### 5.3 La carte, l'invocation en base, et le moment où elles sont écrites

- **La ligne `Summons` est créée par la formation (303), pas par l'apprivoisement.** `onEquipSummon`
  (`WorldSession.cpp:936-1019`) : la limite est `SKILL_CREATURE_CONTROL = 1801` plafonnée à **6**
  emplacements (`:944-949`, `SkillBase.h:103`) ; chaque carte reçue est validée (groupe 13, propriétaire, **et
  le bit `ITEM_FLAG_SUMMON`** — `:956-962`) ; si la carte est liée et n'a pas encore d'invocation, elle est
  créée (`AllocNewSummon`, `:990`) puis **`Summon::DB_InsertSummon` écrit la ligne** (`:995`), et le
  joueur reçoit **301** (`AddSummon(…, true)`, `:992`) puis la carte mise à jour (`SendItemMessage`, `:993`) ;
  enfin les emplacements sont compactés (`:1006-1017`) et la réponse est **303 S→C** (`:1018`,
  `Messages.cpp:122-135`).
- **D'où vient la créature de la carte.** `MemoryPoolMgr::AllocNewSummon` lit
  `pItem->GetItemTemplate()->summon_id`, alloue l'invocation à ce code, met à niveau 1, stocke l'UID de
  l'invocation dans le **socket 0 de la carte** et lie les deux objets (`MemPool.cpp:74-89`). C'est
  `ItemResource.summon_id` qui porte la créature, pas la ligne de carte.
- **Ce qui est persisté.** `CHARACTER_ADD_SUMMON` écrit `summon_id`, `card_uid`, `name`, `level`, `exp`, `jp`,
  `sp`, PV/MP max — un pour un avec `SummonEntity` (`SummonResourceId`, `CardItemId`, `Name`, `Lv`, `Exp`,
  `Jp`, `Sp`, `Hp`, `Mp`), `Summon.cpp:131-152` contre
  `Game/DataAccess/Entities/Telecaster/SummonEntity.cs:13-32`.
- **Ce que `MainSummonId` / `SummonSlotItemIds` reçoivent.** NGemity restaure au login les
  `PLAYER_FIELD_SUMMON + i` (les six emplacements persistés) en `m_aBindSummonCard[i] = pSummon->m_pItem`
  (`Player.cpp:229-246`) — donc `SummonSlotItemIds` = **les six identifiants de la ligne d'objet de la
  carte** ; `mainSummon` devient `m_pMainSummon` (`:248-250`), qui est la seule invocation **entrant dans le
  monde** au login, avec `AddNoise(…, 50)` et le drapeau invincible (`:818-824`) — donc `MainSummonId` = la
  ligne `Summons` de l'invocation principale. À l'inverse, `Player::AddSummon` **écrit la ligne** au moment de
  la création (`DB_UpdateSummon` si le statut est « login complete », `:1502-1504`) et `RemoveSummon` la
  réécrit puis envoie 302 (`:1507-1517`).

### 5.4 Ce que NavisLamia doit faire — état exact du dépôt

| manque | source du manque |
|---|---|
| `SkillEffectType` n'a **pas** de membre pour 603 (ni `Taming`, ni autre) : le catalogue ne peut pas classer le sort `4003` | `Game/DataAccess/Entities/Enums/SkillEffectType.cs:122-124` (`Summon = 601`, `Unsummon = 602`, `UnsummonAndAddState = 605`) — aucun `603` dans le fichier |
| 601/602/603 ne sont pas dans la liste des effets castables, donc `GetCastableSkills()` ne les charge **jamais** et un lancer de 4001/4002/4003 finit en « compétence inconnue » | `Game/Services/Buffs/BuffCatalog.cs:34-38` (`CastableEffectTypes`), `Game/DataAccess/Repositories/SkillResourceRepository.cs:70-77` (filtre), `SkillCastService.cs:302-306` (`AccessDenied`) |
| `SkillCastKind` n'a ni invocation, ni renvoi, ni apprivoisement ; `TargetsAMonster` ne connaît que debuff/attaques | `Game/DataAccess/Repositories/Interfaces/ISkillResourceRepository.cs:13-22`, `SkillCastService.cs:402-405` |
| `TM_SC_TAMING_INFO` **n'est déclaré nulle part** (grep complet : aucune occurrence de `TAMING`) | `Game/Network/Packets/Enums/GamePackets.cs` (famille 301-307, 320, 324, 350-355, 400, 452) |
| aucun écrivain ni test d'offsets pour 310 | `Game/Network/Packets/Game/GameSummonPackets.cs` (`NameSize = 19`, @31 ; rien pour 310) |
| **aucun état de dresseur** : ni sur le monstre (`MonsterWorldState`), ni sur la connexion (`ConnectionInfo` n'a que `SummonSlots`, `:50`, et `ActivePet`, `:56`) | `Game/Network/Clients/ConnectionInfo.cs` |
| aucune lecture de `TamingId` / `TamingPercentage` / `CreatureTamingCode` | `MonsterResourceEntity.cs:62-65`, `ArcadiaContext.cs:133` — et rien ailleurs |
| aucune recherche de carte par code, ni écriture de drapeau : `StorageRepository` ne charge un objet que par handle ou par personnage | `Game/DataAccess/Repositories/StorageRepository.cs:54,121` ; `ItemEntity.Flag` est typé `ItemFlag` (`ItemEntity.cs:32`) |
| aucune diffusion à la région (le 310 comme le 305 de `Leave` n'ont pas de canal) | `socle-invocation-monde.md` §13.4 point 2 (« NavisLamia n'a aucune visibilité joueur↔joueur ») |
| `SummonWorldService.Enter`/`Leave` **sans appelant** : c'est ici qu'il faut le premier | `socle-invocation-monde.md` §13.2-13.3 |
| la mort d'un monstre est un seul point de passage (`CombatService`, `_worldState.Kill`) : c'est là que le tirage d'apprivoisement doit s'accrocher, sans dupliquer le chemin de mort | `Game/Services/CombatService.cs:214` (`Kill`), `:236-242` (`AwardKill`) |

**Le premier appelant de `SummonWorldService`, nommé :** `Enter(session, tag, connection, entry)` doit être
appelé par le chemin du **sort d'invocation** (`4001`), au moment de `DO_SUMMON`, après la validation de la
carte liée — c'est-à-dire dans `SkillCastService`, à côté de l'appel à `ICombatService` pour les attaques, et
**jamais** depuis un paquet 304 (aucun émetteur, §3.4). `Leave` doit être appelé par le **renvoi** (`4002`).
Les deux attendent une entrée d'appelant (`SummonWorldEntry.FromEntity`), donc une ligne `Summons` et un
`code` établis : c'est ce qui reste bloqué (§11 étape 2).

---

## 6. Écarts assumés avec NGemity, et pourquoi

| écart | pourquoi | source |
|---|---|---|
| Nous classons l'apprivoisement par **effet 603**, NGemity par **id de sort 4003** | `SkillEffectType` est notre clé de routage (le catalogue charge par `effect_type`) ; NGemity n'a **aucune constante d'effet** pour l'apprivoisement (`SkillBase.h` n'a pas de `EF_TAMING`, vérifié). Notre clé est plus large (elle couvrirait `4004`), elle est donc bornée à 603 **et** au sort 4003 dans la classification. | `SkillBase.h` (grep `EF_TAMING` : aucune occurrence), `Skill.cpp:294` |
| Nous rendons les **codes de résultat spécifiques** (`NotTamable` 90, `TargetAlreadyBeingTamed` 91, `NotEnoughTargetHP` 92, `NotEnoughSummonCard` 93, `AlreadyTaming` 70) là où NGemity répond `TS_RESULT_NOT_ACTABLE` (5) | Les cinq codes existent **dans les deux références** : `shared/Server/TS_MESSAGE.h:141-145` et `reference/rzu/librzu/src/packets/PacketEnums.h:74` (`ALREADY_TAMING = 0x46`), et **notre `ResultCode` les porte déjà** (`Game/Network/Packets/ResultCode.cs:108-112`). Le client connaît `RESULT_ALREADY_TAMING` (§2.1). Réserver le code précis est plus informatif ; si le client 7.3 refuse un code précis, retour à 5 — voir §7 point 3. | `TS_MESSAGE.h:141-145`, `PacketEnums.h:74`, `ResultCode.cs:104-113` |
| Nous devrons **sauter le butin** de la mort quand l'apprivoisement réussit | Ce n'est pas un écart mais une règle de la référence qu'il faut porter : `Monster::onDead` n'entre dans le chemin de drop que si `!m_bTamedSuccess` (`Monster.cpp:140`). Notre chemin de mort a un point unique (`CombatService.cs:214`) et un chemin de drop distinct : la règle doit y être posée une fois. | `Monster.cpp:128-140` |
| NGemity n'implémente **pas** de pénalité d'échec côté monstre (le code est commenté) et sa fenêtre (30 s par coup) est une constante non documentée | Nous ne devons rien inventer : la fenêtre et la pénalité restent `NON ÉTABLI` (§7 point 4). La constante de 30 s est citée comme *ce que fait la référence*, pas comme la règle du 7.3. | `Monster.cpp:116-117,436-439` |
| NGemity ne modélise pas le gating `>= EPIC_9_6_3` | Sa cible est antérieure ; pour les ids nous suivons **rzu**, jamais NGemity (règle déjà posée par `socle-invocations.md` §6). | `socle-invocations.md` §6 |
| Le « Empty loot pet » du texte client ne correspond à aucune colonne | Le libellé anglais parle de « proper type of creature card **or empty loot pet** » ; les deux références ne connaissent qu'une carte (groupe 13) et la colonne `creature_taming_code` de l'export est **nulle partout** dans le relevé (tous les monstres apprivoisables testés portent `creature_taming_code = 0`). Nous ne portons donc que la carte. | export `MonsterResource.csv` colonne 60 ; `db_string.rdb` `40004002` |

---

## 7. NON ÉTABLI

1. **Le libellé affiché des trois sorts en jeu.** Nos ids sont tranchés (§2.3) par les effets, les nombres et
   NGemity ; la table de chaînes du client 7.3 est **décalée d'un cran** pour cette famille, et le nom
   « Summon Creature » y est orphelin (`db_string.rdb` `40065065`). Un relevé en jeu (survol de la fenêtre de
   compétences) dirait lequel des trois libellés le client associe à 4001 — **sans que cela change le
   protocole** : le serveur reçoit l'id. Question précise : *dans la fenêtre `Alt+R`/compétences du client
   7.3, quels noms portent respectivement 4001, 4002 et 4003 ?*
2. **Le geste client d'invocation.** Aucune capture de paquet n'existe : la déduction repose sur le texte du
   client lui-même (« summon it with summon creature skill »), sur l'effet 601 de `4001` et sur l'existence de
   la classe `USkillSummonUnsummon`. Question précise : *le client 7.3 émet-il bien `TM_CS_SKILL` (400) avec
   `skill_id = 4001` et `target` = handle de la carte ?* Tant que non établi, l'appelant de
   `SummonWorldService.Enter` reste une hypothèse de conception (§5.4), pas un fait de protocole.
3. **Les codes de résultat de 7.3.** `NotTamable` (90), `TargetAlreadyBeingTamed` (91), `NotEnoughTargetHP`
   (92), `NotEnoughSummonCard` (93) existent dans rzu et dans notre `ResultCode`, mais NGemity ne les émet
   jamais : rien ne prouve que le client 7.3 les affiche correctement. Question précise : *le client 7.3
   accepte-t-il 90-93 dans `TS_SC_RESULT` (`request_msg_id = 400`), ou faut-il retomber sur `NOT_ACTABLE`
   (5)* ? Réserve vérifiable : émettre 5 est toujours sûr, émettre 92/93 est plus précis.
4. **La fenêtre d'apprivoisement et l'abandon.** NGemity repousse l'échéance de 30 s à chaque coup du
   dresseur et efface le dresseur en silence à l'expiration (`Monster.cpp:436-439`), tandis que
   `ClearTamer(…, true)` — le seul émetteur du **310 mode 1** — est appelé depuis une branche de timer de
   patrouille (`Monster.cpp:1156`). La durée réelle en 7.3, et le fait que le client attende un mode 1 à
   l'expiration, ne sont pas établis. Aucune contrainte d'ordre entre 310 et les autres trames n'est
   démontrable non plus.
5. **Le saut du butin à la réussite.** `Monster.cpp:140` est clair pour NGemity ; notre chaîne de butin
   (`CombatRewards`, `DropRoll`, `MonsterDropTableResource`) n'a pas encore de règle « apprivoisé ⇒ pas de
   butin ». À trancher par Killian (politique de jeu) : le lot peut-il livrer l'apprivoisement sans cette
   règle, au risque de doubler la récompense ?
6. **`code` et `summon_handle` de 301** restent des entrées d'appelant (`socle-invocations.md` §7 `NON
   ÉTABLI` 3, `socle-invocation-monde.md` §13.2) : tant qu'ils ne sont pas fournis, `Enter` ne peut pas être
   câblé. Ce lot **ne les ferme pas** et ne prétend pas le faire.
7. **`SummonSlotItemIds` : ids d'objet ou ids de ressource ?** Le champ se pose lui-même la question dans le
   code (`CharacterEntity.cs:63`, commentaire d'origine : « verify if item id or item resource id »). NGemity
   persiste les **six identifiants d'invocation** (`PLAYER_FIELD_SUMMON + i`, `Player.cpp:231-241`) et
   reconstruit les cartes par `pSummon->m_pItem`, donc la sémantique de notre colonne (et celle de
   `MainSummonId`) doit être tranchée **avant** toute écriture : le type est `string` (colonne JSON) et rien
   ne l'alimente aujourd'hui.

**Précédent du dépôt.** Ces deux questions étaient déjà ouvertes, et ce lot en ferme une partie sans les
rouvrir :

- `socle-invocations.md` §7 `NON ÉTABLI` 4 posait « chemin d'invocation du client 7.3 : 304 ou sort 400 ? ».
  **Fermé ici** : c'est le sort (`TM_CS_SKILL` 400), 304 n'a aucun émetteur dans les deux références et
  aucun déclencheur client (§3.4).
- `socle-invocation-monde.md` §13.2-13.3 laissait `Enter`/`Leave` sans appelant en attendant une invocation
  et une liaison carte↔invocation. **Nommé ici** : l'appelant est le chemin du sort d'invocation (4001) pour
  `Enter` et du renvoi (4002) pour `Leave` (§5.4), mais il reste dépendant du point 6 ci-dessus, donc toujours
  non câblé.
- `304-summon.md` §7 point 3 (« comment la carte liée devient liée ») : **fermé ici** par la chaîne
  d'apprivoisement de §5.1 (drapeau `ITEM_FLAG_TAMING` → `ITEM_FLAG_SUMMON`), sans création de ligne
  `Summons` (celle-ci reste l'affaire de la formation 303, §5.3).

---

## 8. Commits épinglés

| dépôt | commit | date | rôle |
|---|---|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | 2023-10-02 | ids, tailles, gating (`TS_CS_SKILL.h`, `TS_SC_TAMING_INFO.h`, `TS_SC_ADD_SUMMON_INFO.h`, `PacketEnums.h`) |
| `reference/ngemity` (RZEmulator) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | 2025-12-03 | logique d'apprivoisement, d'invocation et de formation (`Skill.cpp`, `World.cpp`, `Monster.cpp`, `WorldSession.cpp`, `Messages.cpp`, `Player.cpp`, `MemPool.cpp`) |
| `reference/client73` | pas de dépôt git — `data.000` + `db_*.rdb` extraits (`extraction-manifest.json`, `source_index_sha256 b88ac39a…`) et `SFrame.exe` | — | table de compétences, table de chaînes, chaînes du binaire |
| `Navislamia` (base de la branche) | `3021f681e98544112ce508b11ce1df305b3a0dd9` (`master` figé au réveil) | 2026-09-29 | état du dépôt |
| branche de la fiche | `hermes/packet-socle-apprivoisement-invocation` | — | la fiche est le seul contenu commité par `navis-ref` |
| export de données | `reference/sqlserver/Arcadia/*.csv` (`SkillResource`, `ItemResource`, `SummonResource`, `MonsterResource`) | — | `effect_type`, `card_id`, `summon_id`, colonnes d'apprivoisement |

---

## 9. Bloc destiné à `CLAUDE.md`

```markdown
## Apprivoisement et invocation des créatures (fiche `docs/packet-specs/socle-apprivoisement-invocation.md`)

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
- La ligne `Summons` est créée par la **formation 303** (`Summon::DB_InsertSummon` appelé depuis
  `onEquipSummon`), pas par l'apprivoisement : l'apprivoisement ne fait que basculer les deux drapeaux de la
  carte. La créature d'une carte vient d'`ItemResource.summon_id` ; la carte requise par une cible vient de
  `MonsterResource.taming_id` → `SummonResource.card_id`.
- `MonsterResourceEntity.TamingId/TamingPercentage/CreatureTamingCode/TamingExpMod` existent mais ne sont lus
  par personne ; `SkillEffectType` n'a pas de membre pour 603 et `BuffCatalog.CastableEffectTypes` ne charge
  pas 601/602/603, donc un lancer de 4001/4002/4003 est aujourd'hui refusé en `AccessDenied`.
```

---

## 10. Tests d'offsets attendus (forme)

Sur le modèle du dépôt (`Tests/Game/GameSummonPacketsTests.cs`, `SummonWorldTests.cs`) :

1. **310 = 16 octets** : `length` @0, `id` = 310 @4, `checksum` @6, `mode` @7, `tamer_handle` @8-11,
   `target_handle` @12-15, un cas par mode (0, 1, 2, 3).
2. **Drapeaux** : `(ItemFlag)0x80000000u` et `(ItemFlag)0x20000000u` sont reconnus par
   `GroundItemDropRules.IsBoundSummonCard` et par le champ `flag` @34 du motif ; et **`ItemFlag.Summon` seul
   ne l'est pas** (test négatif explicite : c'est le piège).
3. **Classification** : `SkillEffectType.Taming = 603` et les trois sorts (4001/4002/4003) sont classés par
   `BuffCatalog` avec leur `SkillCastKind`, et un lancer de 4003 sur un monstre non apprivoisable rend le code
   de résultat décidé (§6).
4. **Les contrôles d'apprivoisement en fonctions pures** (monstre sans `taming_id`, PV non pleins, carte
   absente, carte déjà liée, monstre déjà apprivoisé, joueur apprivoisant déjà) : un test par refus, sans
   base de données.

---

## 11. Découpage — ce qui est implémentable sans décision de jeu

Le fil conducteur : **rien de ce lot ne peut s'écrire après le tirage d'apprivoisement** (§5.1 étapes 7-9), car
la réussite crée une carte liée dont l'invocation n'existe pas encore ; mais tout ce qui est *déclaratif* est
livrable immédiatement.

### Étape 0 — livrable sans aucune décision de jeu (le dev peut tout faire)

| # | travail | pourquoi c'est sans décision |
|---|---|---|
| 0.1 | `TM_SC_TAMING_INFO = 310` dans `GamePackets` + écrivain 16 octets + tests d'offsets (§10.1) | format tranché par rzu, gating tranché §4 |
| 0.2 | `SkillEffectType.Taming = 603` ; ajout de 601/602/603 à `BuffCatalog.CastableEffectTypes` ; trois `SkillCastKind` (`Summon`, `Unsummon`, `Taming`) + branches de classification bornées aux ids 4001/4002/4003 | valeurs et ids tranchés §2.3, §4 |
| 0.3 | `TargetsAMonster` étendu au seul apprivoisement (§5.4) | NGemity exige un monstre (`Skill.cpp:1616-1618`) |
| 0.4 | les **règles pures** d'apprivoisement : masques de drapeau, « monstre apprivoisable » (`taming_id != 0`), « PV pleins », « carte requise absente », « carte déjà liée » + tests (§10.2, §10.4) | chaque contrôle a sa source NGemity (`Skill.cpp:1611-1638`) |
| 0.5 | l'accès aux colonnes d'apprivoisement du monstre (`TamingId`, `TamingPercentage`) depuis le catalogue de monstres | les colonnes existent et sont mappées (§5.4) |
| 0.6 | les codes de résultat 70/90/91/92/93 dans la table de correspondance des refus de lancer, **sans** les émettre d'abord (§7 point 3) | `ResultCode.cs:104-113` les porte déjà |

Cette étape ne fait **aucune** écriture en base, aucun envoi de 310, aucune création d'invocation, et n'appelle
toujours pas `SummonWorldService` : elle rend la classification et les refus corrects, ce qui est déjà un gain
observable (aujourd'hui, lancer 4001/4002/4003 répond une erreur de compétence inconnue).

### Étape 1 — conditionnée à un arbitrage de Killian

| # | travail | ce qui bloque |
|---|---|---|
| 1.1 | appelant du chemin d'invocation : `SkillCastService` → `SummonWorldService.Enter` (301 puis entrée dans le monde) | le `code`/`summon_handle` d'appelant (`socle-invocations.md` `NON ÉTABLI` 3) et la liaison carte↔invocation (§7 point 7) |
| 1.2 | appelant du renvoi : `4002` → `Leave` (305 puis sortie du monde) | le même, plus la règle de déliaison (302) |
| 1.3 | émission du 310 et **diffusion à la région** | il n'existe aucun canal joueur↔joueur dans le dépôt (§5.4) : c'est une décision d'architecture |
| 1.4 | état de dresseur (monstre + connexion) et fenêtre d'apprivoisement | §7 point 4 |
| 1.5 | tirage à la mort du monstre : drapeaux de la carte, destruction en cas d'échec, **saut du butin** | §7 point 5 |
| 1.6 | formation 303 : création de la ligne `Summons` + écriture de `MainSummonId`/`SummonSlotItemIds` | §7 points 6 et 7 |

### Étape 2 — hors lot, à découper plus tard

304, 323, 306/320/321 (les trames sans émetteur), 452 : rien n'y change.

**Conséquence pour le board** : les cartes bloquées par ce socle (l'implémentation `t_eb763df1` et sa
relecture `t_cbe3c289`) peuvent reprendre **l'étape 0 telle quelle**, et l'étape 1 après l'arbitrage des points
1.1-1.6 — sans refaire l'archéologie, qui est entièrement ci-dessus.

---

## 12. Réserves de méthode

1. **Aucun exécutable du client n'a été lancé** : les relevés client sont des lectures de `db_skill.rdb`,
   `db_string.rdb` (extraits de `data.000`) et de chaînes de `SFrame.exe`. Une chaîne trouvée dans
   `SFrame.exe` prouve que le binaire connaît un nom ou une classe, pas qu'un chemin d'exécution l'atteint.
2. **`db_skill.rdb` est une table dérivée** écrite par l'outil d'extraction (`Archemedes v0.1.0`), de 2188
   enregistrements de 827 octets : les seuls champs que j'ai su y apparier avec l'export sont l'id, l'id de
   texte, `cost_mp`, `cost_mp_per_skl`, `cost_mp_per_enhance` et, sur 4003, les deux flottants 0.06/0.03. La
   correspondance complète des colonnes n'est pas établie et n'est pas nécessaire ici.
3. **L'export est du 9.4, le client du 7.3.** Quand les deux diffèrent, le client tranche (règle du profil) ;
   c'est exactement le cas des libellés (§2.3), où le client est incohérent **avec lui-même** (un id de texte
   réutilisé par un outil d'effet), ce qui oblige à trancher par les effets, les nombres et NGemity.
4. **Le chemin de l'export est `reference/sqlserver/Arcadia/`** : `data/sqlserver/Arcadia/`, cité par la carte,
   n'existe pas dans le dépôt (relevé `ls`).
5. **`creature_taming_code` est nul dans tous les relevés** (2437 monstres apprivoisables examinés) : la
   colonne existe mais n'est pas la source de la carte requise ; c'est `taming_id` → `SummonResource.card_id`.
6. **Un document tout entier a été lu pour ce lot** (`304-summon.md`, `socle-invocations.md`,
   `socle-invocation-monde.md`, `socle-ferme-creatures.md`, `socle-familier-pet.md`) : aucune de leurs
   conclusions n'est contredite ici ; trois de leurs questions sont fermées (§7, « Précédent du dépôt »).

---

## A VERIFIER PAR KILLIAN

1. **Arbitrer §7 point 5 (butin)** : l'apprivoisement réussit-il sans butin, comme NGemity
   (`Monster.cpp:140`), ou le lot peut-il ignorer la règle dans un premier temps ?
2. **Arbitrer §7 point 3 (codes de résultat)** : émettre les codes précis 90/91/92/93 (présents dans rzu et
   dans `ResultCode`) ou rester sur `NOT_ACTABLE` (5) comme NGemity ?
3. **Confirmer en jeu §7 points 1 et 2** : dans la fenêtre de compétences du client 7.3, quels noms portent
   4001/4002/4003, et un lancer de créature émet-il bien `TM_CS_SKILL` avec `skill_id = 4001` ? C'est le seul
   point qui décide *l'appelant* de `SummonWorldService`.
4. **Arbitrer §7 point 7 (`SummonSlotItemIds`)** : ids d'objet de carte (ce que porte NGemity) ou ids de
   ressource ? La colonne est vide aujourd'hui, donc le choix est encore libre.
5. **Confirmer §7 point 4 (fenêtre d'apprivoisement)** : la durée de 30 s par coup est celle de NGemity ;
   aucune source 7.3 ne la donne.
