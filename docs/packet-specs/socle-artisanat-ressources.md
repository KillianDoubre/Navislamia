# Socle « artisanat — ressources et moteur » — `MixResource` / `EnhanceResource`, `TM_CS_MIX` (256) et `TM_SC_MIX_RESULT` (257)

Fiche du lobe A du socle d'artisanat, ouverte par le réveil du PO du 2026-09-26 (carte Trello
`YfBek24I`, carte Hermes parente `t_1cc5bcda`, implémentation `t_8f7ccfa1`). Elle **complète** la fiche
du socle `docs/packet-specs/socle-artisanat-objets.md` (famille entière, découpage en trois lobes) et ne
la remplace pas : les décisions de format y sont déjà prises et vérifiées, elles sont reprises ici avec
leur source, pas recopiées de confiance.

Périmètre : les deux tables de ressources (`MixResource`, `EnhanceResource`), le moteur de
**résolution** d'un mix et la trame de réponse `257`. Les lobes B (260/261/262) et C (263/264) ont leurs
propres cartes et ne sont pas traités ici.

Base : `master` = `b56967a07430422add88e0e5cdf292b41b18f6c6` (2026-09-24). Branche :
`hermes/packet-socle-artisanat-ressources`. Le SHA `0576ab5` cité par une carte sœur **n'existe pas**
dans ce dépôt (`git rev-parse` : variable) — c'est bien `b56967a` qui est la base.

---

## 1. Identité — les deux ids de ce lobe

| id | nom | source |
|---|---|---|
| 256 | `TM_CS_MIX` | `op_codes.md:83` (`[256] = "TM_CS_MIX"`) |
| 257 | `TM_SC_MIX_RESULT` | `op_codes.md:84` (`[257] = "TM_SC_MIX_RESULT"`) |

État sur `master` (`b56967a`) : les deux membres sont déclarés en `Game/Network/Packets/Enums/GamePackets.cs:56-57`
(bloc commenté « The crafting and item-enchantment family », `:52-58`). Le lecteur `256` existe
(`Game/Network/Packets/Game/GameActionPackets.cs:415-459`), le bras de réception est en
`Game/Network/Clients/GameClient.cs:1618-1631` (appel `:1628`), la conduite est
`Game/Services/CraftingSocleService.cs:43-126` et les règles de poignées
`Game/Services/CraftingSocleRules.cs:18-32`. `257` est un paquet **descendant** : sur `master` il n'a
pas de bras de réception et c'est correct (`GameClient.cs:1419-1424` le cite dans la liste des ids
strictement S→C journalisés et jetés). **Aucune branche ne construit de trame 257** : mesure sur les 55
branches locales `hermes/*` — les seules occurrences de `MIX_RESULT` sont la déclaration de l'énumération,
les commentaires de `GameClient.cs` et une assertion de test.

## 2. Ce que le joueur fait pour que le client l'envoie

- **256** — le joueur ouvre la **fenêtre de combinaison** (icône « combiner » de l'inventaire), y dépose
  un objet cible et jusqu'à neuf matériaux, puis valide. Détail du geste, des ressources client
  concernées et des relevés : `socle-artisanat-objets.md` §2.1. Ressources client en lecture statique :

  | fichier | taille | sha256 |
  |---|---|---|
  | `reference/client73/db_combineres.rdb` | 1 654 316 o | `9d102803e455e9bc61bd38e3be6dd88cb06588cd92481cae9f6633a3df11018d` |
  | `reference/client73/db_mixcategory.rdb` | 6 032 o | `6f85014a564ae5d1df06aa37d3ef33b49314f018b91f8c683d65b843f4a4401b` |
  | `reference/client73/db_enhance.rdb` | 23 787 o | `f0e8286d621fa110987ddc61d6d50fcd01e9046b51d35197aa339f870dd333de` |

  `reference/client73/` n'est **pas** un dépôt git : il n'y a aucun SHA de client à épingler, seulement
  ces empreintes de fichiers (méthode de lecture : `socle-artisanat-objets.md` §10.4 ; les trois
  ressources ne sont **pas décodées**, cf. §9 NON ÉTABLI).
- **257** — **aucun geste joueur** : c'est la réponse du serveur à 256 (`SessionPacketOrigin::Server`,
  `reference/rzu/librzu/src/packets/GameClient/TS_SC_MIX_RESULT.h:22`). Le joueur voit l'infobulle de
  l'objet mis à jour (ou rien) ; la trame ne sert qu'à faire rafraîchir au client les objets dont l'état
  a changé.

## 3. Structure sur le fil

Conventions (§3.0 de la fiche sœur) : en-tête de 7 octets (`GameActionPackets.cs:9` `HeaderSize = 7`),
entiers **little-endian**, `ar_handle_t` = `uint32` (rzu `librzu/src/packets/Packet/PacketDeclaration.h`),
en-tête rzu = `[size u16][id u16][checksum u32]` (`size` = taille totale).

### 3.1 256 — `TM_CS_MIX`, variable, **15 + 6N octets** (N ≤ 9)

| offset | type | nom | valeur observée / contrainte | source |
|---|---|---|---|---|
| 0 | u16 | `size` | 15 + 6N | en-tête rzu |
| 2 | u16 | `id` | 256 | `op_codes.md:83` |
| 4 | u32 | `checksum` | — | en-tête rzu |
| 7 | u32 | `main_item.handle` | `0` = slot cible vide (sentinelle, jamais résolue) | rzu `TS_CS_MIX.h:8,15` ; `WorldSession.cpp:1452-1454` |
| 11 | u16 | `main_item.count` | la référence lit `1` en dur, ne lit pas ce champ | rzu `TS_CS_MIX.h:9` ; `WorldSession.cpp:1452` |
| 13 | u16 | `sub_items` (count) | = N, longueur du tableau écrite par l'émetteur | rzu `TS_CS_MIX.h:16` |
| 15 + 6i | u32 | `sub_items[i].handle` | `0` = slot vide | rzu `TS_CS_MIX.h:8,17` |
| 19 + 6i | u16 | `sub_items[i].count` | quantité consommée de ce slot | rzu `TS_CS_MIX.h:9,17` |

Bornes en vigueur sur `master` (`GameActionPackets.TryReadMix`, `:415-459`) : `Length >= 15`, queue
multiple de 6, `N <= 9` (`MaxSubItems`, `:17`), count déclaré = N. Le plafond 9 vient de la référence
(NGemity `MAX_SUB_MATERIAL_COUNT`, `MixManager.h:24` ; garde `WorldSession.cpp:1447-1450` qui **kick**
au-delà) et de la table de matériaux (9 groupes `sub01`..`sub09`, `ArcadiaSchemaPSQL.sql:489-582`).

Test d'offsets existant : `Tests/Game/CraftingSoclePacketsTests.cs:76-204` (cas minimal, pas de 6 octets,
ordre des slots, N = 9, N = 10 refusé, tailles courtes refusées, count déclaré divergent refusé).

### 3.2 257 — `TM_SC_MIX_RESULT`, variable, **11 + 4M octets** en 7.3

| offset | type | nom | 7.3 | source |
|---|---|---|---|---|
| 0 | u16 | `size` | 11 + 4M | en-tête rzu |
| 2 | u16 | `id` | **257** (et non 1257) | `TS_SC_MIX_RESULT.h:18-20` (remap `>= EPIC_9_6_3` seulement) |
| 4 | u32 | `checksum` | — | en-tête rzu |
| 7 | u32 | `handles` (count) | **présent**, M = nombre de poignées | `TS_SC_MIX_RESULT.h:14` |
| 11 + 4j | u32 | `handles[j]` | poignée d'un objet dont l'état a changé | `TS_SC_MIX_RESULT.h:16` |

Le champ `type` (`TS_MIX_TYPE`, `MIX_TYPE_NONE = 0` / `MIX_TYPE_AWAKEN = 1`) est **gaté
`version >= EPIC_8_1`** (`TS_SC_MIX_RESULT.h:7-11,15`) : **absent en 7.3**, décision déjà prise par la
fiche sœur §5.2 et respectée ici. Tailles 7.3 : **11 o** (M = 0), **15 o** (M = 1). Aucun plafond de M
n'est déclaré par rzu ; la référence n'émet jamais plus d'une poignée (§6.3).

## 4. Gating de version — décisions prises pour 7.3

| champ / id | gating rzu | décision 7.3 | source |
|---|---|---|---|
| id 256 / 1256 | `version < / >= EPIC_9_6_3` | **256** | `TS_CS_MIX.h:19-21` |
| id 257 / 1257 | `version < / >= EPIC_9_6_3` | **257** | `TS_SC_MIX_RESULT.h:18-20` |
| 256 : tous les champs | aucun gating | identiques en 7.3 et au-delà | `TS_CS_MIX.h:7-17` |
| 257 : `handles` (count + tableau) | aucun gating | présents | `TS_SC_MIX_RESULT.h:14,16` |
| 257 : `type` (`TS_MIX_TYPE`) | `>= EPIC_8_1` | **absent** (défaut `MIX_TYPE_NONE` si lu) | `TS_SC_MIX_RESULT.h:15` |

`EPIC_7_3 = 0x070300` ; NGemity compile en **`EPIC_4_1_1`** (`shared/Common/Define.h:25`) : ses écarts
sont des différences de version, pas des erreurs, et aucun de ses handlers ne lit un champ apparu après
4.1.1 ici.

## 5. Les deux tables de ressources

Source locale de lecture : **`reference/ngemity/Database/Arcadia.sql`** (dump HeidiSQL d'une MariaDB
`Arcadia`, `192.168.0.12`, ajouté au dépôt NGemity le 2017-12-23, dernier état `3abe24b`), 18 667 572 o,
sha256 `c05c200ae67f8cd2483a27784a976f8afc2ed6b80ff3729fb4f1734f205214fe`. **Sa donnée est du milieu
d'Epic 4** (message du commit `1d6d670` « now uses the mid-Epic 4 ItemResource »), et non de 7.3 :
elle établit la **forme** des lignes, la mécanique et les cardinalités, **jamais les valeurs 7.3**.
Ce fichier est une source de **lecture** : il n'est pas un moyen d'alimenter notre base (§5.3).

### 5.1 `MixResource` — 109 colonnes

Schéma de référence : `ArcadiaSchemaPSQL.sql:471-582` (109 colonnes, **aucune clé, aucun index,
aucune contrainte** : vérifié par `grep -n "key\|primary\|unique\|index"` sur le bloc, 0 résultat).
Dump NGemity : DDL `Database/Arcadia.sql:30557-30668`, données `:30671-31425` (**754 lignes**, 109
champs par ligne ; l'en-tête du dump annonce « ~747 rows » — approximation, l'écart de 7 est mesuré).
Les 109 noms de colonnes des deux sources sont **identiques et dans le même ordre** (comparaison
programmatique) : c'est la même table, la colonne 1-indexée `n` du dump correspond à la ligne
`470 + n` d'`ArcadiaSchemaPSQL.sql`.

| groupes de colonnes | lignes PSQL | alimente |
|---|---|---|
| `id` | :473 | `MixBase.id` (`MixManager.h:72`) |
| `mix_type` | :474 | `MixBase.type` (`:73`) |
| `mix_value_01..06` | :475-480 | `MixBase.value[0..5]` (`:74`) |
| `sub_material_count` | :481 | `MixBase.sub_material_cnt` (`:75`) |
| `main_type_01..05` / `main_value_01..05` | :482-491 | `main_material.type[0..4]` / `.value[0..4]` (`:67-68`) |
| `sub01_type_01..05` / `sub01_value_01..05` | :492-501 | `sub_material[0].type[0..4]` / `.value[0..4]` (`:67-68`) |
| `sub02_*` … `sub09_*` | :502-582 | `sub_material[1]` … `sub_material[8]` |

**Formule positionnelle** (chargement NGemity `ObjectMgr.cpp:1108-1146` : il lit `SELECT *` et consomme
les champs **dans l'ordre** `id, mix_type, value[0..5], sub_material_cnt`, puis
`main_material` `type[0], value[0], … type[4], value[4]`, puis pour chacun des **9** groupes
`sub_material[i]` la même paire `type[j], value[j]`) : le couple se lit
`subNN_type_MM` = `sub_material[NN-1].type[MM-1]` et `subNN_value_MM` = `sub_material[NN-1].value[MM-1]`
(NN = 1..9, MM = 1..5). Toute lecture par nom de colonne doit reproduire cet ordre, sinon les
109 colonnes se désalignent.

**Contrôle `sub_material_count`** — mesuré sur les 754 lignes : `sub_material_count` est **exactement**
le nombre de groupes `subNN` dont `type_01` est non nul, **0 exception**. Les groupes au-delà sont
entièrement à zéro (types **et** valeurs). Distribution : 1 (204), 2 (539), 3 (2), 5 (3), 6 (5), 7 (1).
C'est ce qui autorise la vérification recommandée : `sub_material_count < N` ⇒ ligne incohérente
(NGemity l'ignore de fait, `GetProperMixInfo` exige `sub_material_cnt == N`).

**`mix_type`** — distribution des 754 lignes : 101 (154), 102 (2), 103 (452), 202 (50), 211 (10),
212 (10), 301 (10), 311 (18), 402 (45), 501 (2), 601 (1). **Aucune ligne** pour 201, 302, 312, 401,
701, 702, 801, 802, 803 ni 0 dans ce dump Epic 4 — ces types existent dans l'énumération
(`MixManager.h:26-47`) mais ne sont pas observables localement.

**Exemples de lignes observées** (mêmes colonnes, valeurs telles quelles) :

| `mix_type` | `mix_value_01..06` | main (`type`,`value`) | sub01 | sub02 | `sub_material_count` |
|---|---|---|---|---|---|
| 101 (id 1016) | 220, 0, 0, 0, 0, 0 | (1,4) (4,2) (7,3) (7,0) — | (3,700202) | — | 1 |
| 103 (id 1154) | 240, 0, 5000, 5000, 0, 0 | (1,6) (4,4) (7,3) (7,0) — | (3,700204) | (3,950020) | 2 |
| 311 (id 1062) | 0, 0, 0, 0, 0, 0 | (1,7) (6,0) — — — | (3,800000) | — | 1 |
| 402 (id 3003) | 700104, 1, 5000, 5000, 100, 0 | (3,800003) — — — — | (1,1) (4,4) (7,0) (9,0) | (3,700504) | 2 |
| 202 (id 1805) | 1, 0, 0, 700157, 1, 100 | tous zéro | (3,950071) | (3,700112) | 2 |

Lecture immédiate et utile : pour 101/103, `mix_value_01` est l'`enhance_id` d'`EnhanceResource`
(`MixManager.cpp:51` `getenhanceInfo(pMixInfo->value[0])`), la cible est décrite par
`item_group` (code 1) + `item_rank` (code 4) + `flag off` bit 3 = `ITEM_FLAG_FAILED` 0x08, et le
matériau par `item_id` (code 3) = le `need_item` de la ligne d'enhance. Les codes observés sur les 754
lignes sont **1, 2, 3, 4, 5, 6, 7, 9, 10** (aucun 8, 11-18) ; 19/20 (post-arrangement) n'apparaissent pas.

**Clé** — le schéma de référence n'en déclare aucune ; les 754 `id` sont **distincts**. Décision de
modèle : clé primaire EF sur `Id` (l'import doit donc **échouer bruyamment** sur un `id` dupliqué au
lieu de le perdre en silence comme NGemity, `RegisterMixInfo` `MixManager.cpp:38-46` qui garde le
premier et jette les suivants).

**Nom de table** — voir §5.3.

### 5.2 `EnhanceResource` — 26 colonnes, clé composite

Schéma de référence : `ArcadiaSchemaPSQL.sql:1345-1374`, **aucune clé déclarée** non plus.
Dump NGemity : DDL `Database/Arcadia.sql:17850-17877`, données `:17882-18122` (**240 lignes**,
conformes à l'annonce « ~240 rows ») ; 26 colonnes identiques et dans le même ordre des deux côtés.

| colonne | ligne PSQL | entité du dépôt | remarque |
|---|---|---|---|
| `enhance_id` | :1347 | `EnhanceResourceEntity.Id` | 48 valeurs distinctes |
| `enhance_type` | :1348 | `EnhanceType EnhanceType` | `char(1)` ; NGemity le lit en `uint32_t Flag` **jamais utilisé** (`ObjectMgr.cpp:1093`) |
| `fail_result` | :1349 | `FailResultType FailResult` | `char(1)` |
| `max_enhance` | :1350 | `short MaxEnhance` | **0 sur les 240 lignes** (voir §6.5) |
| `local_flag` | :1351 | `LocalFlag LocalFlag` | 1, 2, 4, 16, 32 et 456 = German\|China\|France\|Russia |
| `need_item` | :1352 | `long? RequiredItemId` | constante par `enhance_id` |
| `percentage_1..20` | :1353-1372 | `decimal[] Percentage` | décroissantes, 1.00000 → 0.00500 |

**Cardinalité mesurée** : 240 lignes = 36 `enhance_id` × 6 `local_flag` + 12 × 2 (soit 48 ids), et le
couple **(`enhance_id`, `local_flag`) est unique sur les 240 lignes** (vérifié). C'est exactement la clé
composite que porte le dépôt : migration `Game/DataAccess/Migrations/Arcadia/20231213174355_Version0001_TheBeginning.cs:59`
(`table.PrimaryKey("PK_EnhanceResources", x => new { x.Id, x.LocalFlag })`), entité
`Entities/Arcadia/EnhanceResourceEntity.cs:5-15`, `DbSet` `ArcadiaContext.cs:18`, mapper
`MigrateDatabase/Mappers/ArcadiaResourcesMappingProfile.cs:244-261`, source
`MigrateDatabase/MssqlEntities/Arcadia/MSSQLEnhanceResource.cs:5-6`, transfert
`MigrateDatabase/Worker.cs:1190-1240`. **À noter** : l'index unique du dump porte sur `enhance_id`
**seul** (`Database/Arcadia.sql:17877` `KEY \`UQ__EnhanceResource__6ABAD62E\` (\`enhance_id\`)`) — son
propre contenu le contredit (6 lignes par id) : la clé composite est la seule lecture auto-cohérente,
et le dépôt a déjà raison.

**Deux pièges dans le chemin de données existant** (relevés, non introduits par ce lot) :

1. `MigrateDatabase/Worker.cs:1211-1214` :
   `if (item.fail_result != "1" || item.fail_result != "2" || item.fail_result != "3") item.fail_result = "1";`
   — la condition est **toujours vraie** : l'import force `fail_result` à `"1"` sur **toutes** les
   lignes. Conséquence : la colonne ne peut pas conserver un `'0'` de la source.
2. `Worker.cs:1230-1234` : `RequiredItemId` est mis à `null` quand l'objet n'existe pas dans
   `ItemResources` — le lien vers l'objet est donc perdu silencieusement si l'import d'items n'a pas
   encore tourné.

Ces deux points touchent la **provenance des valeurs** et sont reportés en §10 : ils ne sont pas dans le
périmètre de code de ce lobe (l'entité et le transfert existent déjà), mais une politique d'échec ne
peut pas être écrite avant qu'ils soient tranchés.

### 5.3 Nom de table — **tranché : pluriel** (`MixResources`, `EnhanceResources`)

L'écart relevé par la fiche sœur §8.2 (« le schéma de référence écrit `create table "EnhanceResource"`
au singulier, la migration crée `EnhanceResources` au pluriel ») est **tranché par trois sources
locales** :

1. la base Arcadia utilisée au runtime est **construite par les migrations EF** :
   `DevConsole/Program.cs:46` `await arcadia.Database.MigrateAsync();` (et `MigrateDatabase/Program.cs:100`) ;
2. l'`ArcadiaContext` ne force aucun nom (`aucun HasName` ; `ArcadiaContext.cs:199` ne fixe que la
   configuration), donc EF applique la pluralisation par défaut du `DbSet` — d'où `EnhanceResources`,
   `SkillResources`, `MonsterResources`… confirmé par les migrations (`20260712205315_AddNpcResource.cs:16`
   `name: "NpcResources"`, `20260713084217_AddMonsterResource.cs:16`);
3. le chemin d'import écrit dans ces tables pluriel : `tools/Import-SkillResourceColumns.ps1:4,120,197-198`
   cible `Arcadia."SkillResources"`, et `Worker.cs:1223` interroge `psqlContext.EnhanceResources`.

`ArcadiaSchemaPSQL.sql` (racine du dépôt, singulier, sans aucune clé) est un **schéma de référence**,
pas le schéma du runtime : ne pas s'y aligner, ne pas renommer `EnhanceResources`. La nouvelle entité
s'appelle donc `MixResourceEntity` et la table **`MixResources`** (pluriel EF par défaut, cohérent avec
les 21 autres `DbSet`). Le CSV d'export reste, lui, **singulier** (`data/sqlserver/Arcadia/MixResource.csv`),
puisqu'il porte le nom de la table SQL Server source (`CLAUDE.md` §« Source data »).

## 6. Traitement attendu (NGemity `Chihiro`, commit épinglé)

### 6.0 Ce que NGemity porte réellement

`Chihiro/src/Crafting/MixManager.{h,cpp}` + le handler `WorldSession.cpp:1445-1493` (enregistré
`:117`). Charges : `ObjectMgr.cpp:1074-1107` (`EnhanceResource`, filtré au chargement par
`(GameRule::GetLocalFlag() & nLocalFlag) != 0`, `:1099`) et `:1108-1146` (`MixResource`, sans filtre).
Émission : `Messages.cpp:910-923` `SendMixResult`.

**Sur les 20 `mix_type`, le handler n'en traite que cinq** — 101, 102, 103 (→ `EnhanceItem`/
`EnhanceSkillCard`), 311 (→ `MixItem`), 501 (→ `RepairItem`) ; `MIX_ADD_LEVEL_SET_FLAG` est le seul
`MIX_*_SET_FLAG` câblé. Tous les autres tombent dans `default: break`
(`WorldSession.cpp:1474-1492`) : **aucune réponse n'est envoyée**. Soit, sur le dump Epic 4,
**126 des 754 lignes (16,7 %)** — dont 202 (50 lignes) et 402 (45 lignes), 2e et 3e types les plus
nombreux — qui ne reçoivent rien de NGemity. `MixManager::CreateItem` (`MixManager.cpp:192-240`, qui
couvrirait 202/302/402/601) **n'est appelé de nulle part** : code mort. Ne pas s'en servir de modèle
sans le dire.

### 6.1 La résolution — `GetProperMixInfo` (`MixManager.cpp:242-298`)

C'est le cœur **mécanique** (aucune politique de jeu), dans cet ordre exact :

1. parcours de **toutes** les lignes `MixResource` **dans l'ordre de la table** (`m_vMixInfo`), première
   qui satisfait les étapes suivantes gagne (`:243-247`) ;
2. `sub_material_cnt == nSubMaterialCount` où `nSubMaterialCount` = nombre de slots matériaux de la
   trame 256 (`:245`) ;
3. `check_material_info(main_material, pMainMaterial, …)` sur l'objet cible (`:249`) ;
4. **appariement des matériaux par permutation, pas par position** : pour chaque groupe `sub[i]` de la
   ligne, un matériau de la trame encore libre doit satisfaire `check_material_info` (`:256-275`,
   `getProperMixInfoSub` `:300-318`) ; un matériau de la trame peut être absorbé par **plusieurs** groupes
   par incréments de quantité (`pCountList`) ;
5. `post_arrange_check_material_info` sur la cible puis sur chaque groupe, avec le tableau des matériaux
   **déjà réordonné** selon les groupes de la ligne (`:279-291`, fonction `:553-582`) — c'est ce qui
   donne leur sens aux codes 19/20.

`check_material_info` (`:345-451`) est un `switch (info.type[i])` sur les codes `CHECK_*`
(`MixManager.h:79-103`) comparant l'objet aux valeurs ; l'objet est lu par des accesseurs
(`GetItemGroup`, `GetItemClass`, `GetItemCode`, `GetItemRank`, `GetLevel`, `GetFlag`,
`GetItemEnhance`, `GetItemWearType`, `GetCount`) qui ont tous un équivalent dans le dépôt :
`ItemEntity.cs:26-37` (`ItemResourceId`, `Amount`, `Level`, `Enhance`, `Flag`, `WearInfo`) et
`ItemResourceEntity.cs:13-26` (`Group`, `Rank`, `Level`, `SocketCount`) — l'équivalent de
`GetItemClass`/`GetItemRank`/`GetItemGroup` passe donc par la **ressource d'objet** de l'instance.

### 6.2 Les vingt codes `CHECK_*` — état exact de la référence

| code | nom | NGemity | présent dans le dump 4.1 ? |
|---|---|---|---|
| 1 | `CHECK_ITEM_GROUP` | `:356-360` | oui (682) |
| 2 | `CHECK_ITEM_CLASS` | `:361-364` | oui (20) |
| 3 | `CHECK_ITEM_ID` | `:365-369` | oui (1 338) |
| 4 | `CHECK_ITEM_RANK` | `:370-374` | oui (651) |
| 5 | `CHECK_ITEM_LEVEL` | `:375-379` | oui (30) |
| 6 | `CHECK_FLAG_ON` | `:380-384` | oui (21) |
| 7 | `CHECK_FLAG_OFF` | `:385-389` | oui (1 276) |
| 8 | `CHECK_ENHANCE_MATCH` | `:390-394` | non |
| 9 | `CHECK_ENHANCE_DISMATCH` | `:395-399` | oui (45) |
| 10 | `CHECK_ITEM_COUNT` | `:400-404` | oui (34) |
| 11 | `CHECK_ELEMENTAL_EFFECT_MATCH` | **commenté**, `:405-433` | non |
| 12 | `CHECK_ELEMENTAL_EFFECT_MISMATCH` | **commenté** | non |
| 13 | `CHECK_ITEM_WEAR_POSITION_MATCH` | `:434-438` | non |
| 14 | `CHECK_ITEM_WEAR_POSITION_MISMATCH` | `:439-451` | non |
| 15 | `CHECK_ITEM_COUNT_GE` | **aucun cas** → `default: break` = *satisfait* | non |
| 16 | `CHECK_ITEM_ETHEREAL_DURABILITY_E` | **aucun cas** → *satisfait* | non |
| 17 | `CHECK_ITEM_ETHEREAL_DURABILITY_NE` | **aucun cas** → *satisfait* | non |
| 18 | `CHECK_ITEM_GRADE` | **aucun cas** → *satisfait* | non |
| 19 | `CHECK_SAME_ITEM_ID` | `post_arrange` `:558-570` (`value` = index de slot : `0` = cible, `n` = matériau réordonné `n-1`) | non |
| 20 | `CHECK_SAME_SUMMON_CODE` | `post_arrange` `:574-576` → **`return false`** (non implémenté) | non |

Les **six codes inertes** que la carte cite sont donc 11, 12 (commentés) et 15-18 (aucun cas, donc
traités comme satisfaits par le `default: break`). Aucun n'apparaît dans la donnée locale : leurs
sémantiques ne sont **pas établies** (§9).

### 6.3 L'émission de 257 — cinq sites, `N ∈ {0, 1}`

| cas | réponse | source |
|---|---|---|
| 101/103, réussite | 257 avec **la poignée de l'objet cible** (M = 1) | `MixManager.cpp:99-105` |
| 101/103, échec | 257 **sans aucune poignée** (M = 0) | `:106-116` |
| 102 (carte de compétence), réussite | 257 avec la poignée de la **seconde carte** (`skillCardSec`, celle qui est effacée) — asymétrie surprenante | `:166-167` |
| 102, échec | 257 sans poignée | `:158` |
| 311 / 501 | 257 avec la poignée de la cible (drapeau posé / retiré) | `:174-191`, `:533-552` |
| 202/302/402/601 (code mort `CreateItem`) | 257 avec la poignée de l'**objet créé**, ou sans poignée si le tirage échoue | `:192-240` (jamais appelé) |
| refus (aucune règle, cube absent, quantité nulle) | **`TM_SC_RESULT` sur 256 avec `InvalidArgument`, jamais 257** | `WorldSession.cpp:1470`, `MixManager.cpp:56,74,87,148` |

Sémantique retenue : les `handles` de 257 sont **les objets dont l'état a changé** et que le client doit
rafraîchir (le champ `type` absent en 7.3 ne porte aucune information). La troisième ligne (102) est
contredite par sa propre logique et sera re-vérifiée par la QA client (§9).

### 6.4 Ce que le socle fait aujourd'hui, et ce que le moteur ajoute

Sur `master`, 256 est lu, borné, ses poignées résolues (`NotExist` sinon), puis **refusé**
`InvalidArgument` (`CraftingSocleService.cs:122-125`) : la fiche sœur §9.2 l'a livré comme étape
structurelle. Ce lobe ajoute la **résolution** (§6.1) et deux refus distincts dans le journal, mais
**conserve le refus `InvalidArgument`** comme réponse tant que les effets ne sont pas décidés : aucune
trame 257 n'est émise, aucun objet n'est touché, aucun taux n'est tiré.

### 6.5 Pièges de la référence à **ne pas porter**

1. **`procEnhanceFail` contient une boucle infinie** (`MixManager.cpp:459-501`) :
   `for (int i = 0; MAX_SOCKET_NUMBER; ++i) { … }` avec `MAX_SOCKET_NUMBER = 4`
   (`ItemTemplate.hpp:9`) — condition constante non nulle, corps vide. Elle n'est atteinte que si
   `nFailResult == 1`, que l'import de **notre** chemin force justement sur toutes les lignes
   (§5.2 point 1). **Ne pas la porter**, ni la réparer : le sort des châsses en cas d'échec est une
   décision de Killian.
2. **`max_enhance = 0` sur les 240 lignes du dump** : `MixManager.cpp:83-84` recale alors la
   progression sur `0 - enhance` puis refuse (`:86-89`). Avec cette donnée, l'enchantement 101/103
   **ne peut jamais réussir** chez NGemity. La valeur 7.3 n'est pas observable ici (§9).
3. **101 : `mix_value_02`/`mix_value_03` sont à `0`** dans les 154 lignes, alors que
   `MixManager.cpp:82` tire `irand(value[1], value[2])` pour `MIX_ENHANCE` : le gain vaut donc `0` et
   le mix est refusé. La référence est incohérente avec sa propre donnée — ne pas en déduire une
   formule.
4. **Drapeaux : indices contre valeurs.** `check_material_info` teste `1 << (value & 0x1F)`
   (`:380-389`) — un **indice de bit** ; `ITEM_FLAG_FAILED = 0x08` (`ItemTemplate.hpp:171`) en est la
   valeur ; l'énumération du dépôt `Entities/Enums/ItemFlag.cs` porte bien des **indices**
   (`Failed = 3`), convention déjà documentée dans le dépôt (`GroundItemDropRules.cs:20-34`, « bit
   *index* 31 »). En revanche `MixItem` (`MixManager.cpp:180`) passe `mix_value_03` = `30` à
   `SetFlag()` comme s'il s'agissait d'une valeur : ambiguïté de la référence, à ne pas trancher ici.
5. **`check_material_info` : `CHECK_ITEM_COUNT` force la quantité à 1** si aucun code 10 n'apparaît
   (`bIsCountChecked`, `:345-354`) : la quantité de la trame est consommée puis remplacée. À reproduire
   explicitement ou à documenter, mais pas laissé implicite.
6. **Filtre `local_flag` au chargement** (`ObjectMgr.cpp:1099`) + déduplication par `nSID` seul
   (`MixManager.cpp:29-37`) : avec 6 `local_flag` par `enhance_id` dans la donnée, la première ligne
   compatible gagne et les autres ids sont jetés. Notre modèle à clé composite n'a pas ce défaut ;
   **ne pas** filtrer au chargement.

## 7. Écarts assumés avec NGemity

| point | NGemity | nous | pourquoi |
|---|---|---|---|
| version | `EPIC_4_1_1` (`shared/Common/Define.h:25`) | 7.3 | gating rzu (§4) |
| types traités | 5 sur 20, les autres sans réponse | la résolution couvre **les 20** types (elle ne dépend pas du type) ; les effets restent hors lot | la table est la même, seuls les effets diffèrent ; ne rien répondre est un trou de référence, pas une cible |
| code mort `CreateItem` | présent, jamais appelé | non porté | aucun appelant, décision de jeu incluse |
| codes 15-18 | `default: break` = **satisfaits** | **refusés** (fermé par défaut) | porter un `default` silencieux reproduirait un bug : un code non établi ne peut pas valider une recette (§10) |
| code 11/12 | commentés (jamais testés) | refusés | idem |
| 257 sur refus | `TM_SC_RESULT` 256 `InvalidArgument` | identique | `CraftingSocleService.cs:122-125` |
| `EnhanceResource` filtré localement | oui (`ObjectMgr.cpp:1099`) | non : toutes les lignes chargées, clé `(Id, LocalFlag)` | le `local_flag` du serveur n'existe pas dans notre configuration (§9) |

## 8. Découpage — ce que cette fiche autorise

Le sous-ensemble retenu est celui qui est **établi par les sources** et ne dépend d'aucune décision de
jeu. Trois paliers, dans cet ordre ; le dev peut s'arrêter à la fin de L1a si L1b le met en difficulté,
mais pas au milieu d'un palier.

### L1a — les ressources (aucune décision de jeu, livrable seul)

| fichier | contenu |
|---|---|
| `Game/DataAccess/Entities/Arcadia/MixResourceEntity.cs` (neuf) | 109 colonnes utiles : `Id` (clé), `MixType` (`int` — l'énumération `MIX_TYPE` n'est **pas** à écrire ici, `mix_type` est lu brut par la référence), `MixValue[6]`, `SubMaterialCount`, `MainTypes[5]`/`MainValues[5]`, `SubTypes[9,5]`/`SubValues[9,5]` ou 45 paires — **suivre la convention d'`ItemResourceEntity`** (tableaux + contraintes de cardinalité EF, `ArcadiaContext.cs:135-178`), pas de type possédé par slot |
| `Game/DataAccess/Contexts/ArcadiaContext.cs` | `DbSet<MixResourceEntity> MixResources` (`:18` voisin) + configuration sur le modèle d'`EnhanceResourceEntity` (`:199-221`) |
| `Game/DataAccess/Migrations/Arcadia/<horodatage>_AddMixResource.cs` (neuf) | table `MixResources`, 109 colonnes, PK `Id` ; migration régénérée par EF, jamais écrite à la main |
| `MigrateDatabase/MssqlEntities/Arcadia/MSSQLMixResource.cs` (neuf) | miroir `snake_case` de la table SQL Server (modèle `MSSQLEnhanceResource.cs:5-33`), `[PrimaryKey(nameof(id))]` |
| `MigrateDatabase/Mappers/ArcadiaResourcesMappingProfile.cs` | `CreateMap<MSSQLMixResource, MixResourceEntity>()` + `ReverseMap()` (modèle `:244-261`) |
| `MigrateDatabase/Worker.cs` + `TransferTables.cs` | `TransferMixResource` calquée sur `TransferEnhanceResource` (`:1190-1240`) et son drapeau (`TransferTables.cs:19`) — **sans** reproduire le `if` toujours vrai de `:1211-1214` ; upsert sur `Id` |
| `Game/DataAccess/Repositories/Interfaces/IMixResourceRepository.cs` + `MixResourceRepository.cs` (neufs) | `AsNoTracking()`, projection, modèle exact d'`AuctionCateryResourceRepository.cs:14-39` |
| `Game/DataAccess/Repositories/Interfaces/IEnhanceResourceRepository.cs` + `EnhanceResourceRepository.cs` (neufs) | même modèle ; l'entité existe déjà, **ne pas la recréer** (§5.2) |
| `Game/Services/MixResourceCatalog.cs` + interface (neuf) | chargement unique en `FrozenDictionary` indexé par `Id` (modèle `ItemGroupCatalog.cs:9-32`) ; pour `EnhanceResource`, index `(Id, LocalFlag)` |
| `DevConsole/Program.cs` | enregistrement des deux dépôts et du catalogue (`:303-327`) |

Tests : `Tests/Game/MixResourceModelTests.cs` — **la formule positionnelle** (§5.1) : une ligne complète
lue telle qu'un import la produirait, avec `sub_material_count` = nombre de groupes non nuls, et un cas
`sub_material_count` **incohérent** (doit être détecté) ; la clé `(Id, LocalFlag)` d'`EnhanceResource`
(6 lignes du même `enhance_id` coexistent). Aucun test d'offsets n'est requis ici : **aucune trame n'est
ajoutée**.

### L1b — la résolution (aucune décision de jeu, livrable seul)

| fichier | contenu |
|---|---|
| `Game/Services/MixResourceMatcher.cs` (neuf, statique et pur comme `CraftingSocleRules`) | l'algorithme du §6.1 : `sub_material_count == N`, `check_material_info` sur la cible, appariement **par permutation** des matériaux, `post_arrange` pour le code 19 ; **refus fermé** pour tout code non établi (8, 11-18, 20) |
| `Game/Services/CraftingSocleService.cs` | après la résolution des poignées : chercher la règle ; journaliser **deux cas distincts** — « aucune règle » (Debug/Information) et « règle résolue, effets non implémentés » (Warning) ; conserver le refus `InvalidArgument` dans les deux cas |
| `Tests/Game/MixResourceMatcherTests.cs` (neuf) | lignes construites à la main, aucune base : match exact, `sub_material_count` divergent, cible qui ne satisfait pas un code, **permutation** (matériaux donnés dans un autre ordre), consommation de quantité par un même groupe, code non établi ⇒ **pas** de match, post-arrangement 19 |

Ce que L1b **ne fait pas** : ni taux, ni probabilité, ni suppression d'objet, ni drapeau, ni échec, ni
trame 257 (§6.3, §10). Un constructeur de 257 sans appelant serait du code mort : la spécification
complète est au §3.2, l'écriture appartient à L2.

### L2 — conditionnée aux arbitrages (§10)

Effets (gains, taux, échec, drapeaux, effacements), politique du `local_flag`, les six codes inertes,
sort des châsses, `max_enhance = 0`, **puis** l'émission de 257 avec ses tests d'offsets (11 o à M = 0,
15 o à M = 1, poignée à l'offset 11, `size` = 11 + 4M) et la valeur 7.3 rafraîchie (§9). Prérequis
nommé, à ne pas contourner : la donnée 7.3 (`data/sqlserver/Arcadia/MixResource.csv` et
`EnhanceResource.csv`, dossier **absent** de ce poste — `.gitignore:482`).

### Collision mesurée (2026-09-26, 55 branches `hermes/*` locales)

`Game/Network/Clients/GameClient.cs` : 18 branches · `Game/Network/Packets/Enums/GamePackets.cs` : 17 ·
`Game/Network/Packets/Game/GameActionPackets.cs` : 12 (chiffres identiques à ceux du brief).
**Zone propre au lobe** : `Game/DataAccess/Entities/Arcadia/MixResourceEntity.cs`,
`MixResourceRepository.cs`, `MixResourceCatalog.cs`, `MixResourceMatcher.cs` — **aucune** branche ne
touche une entité `MixResource*` ni `EnhanceResourceEntity` ni le profil de mapping ; sur les 55
branches, une seule touche `Game/DataAccess/Contexts/ArcadiaContext.cs` et trois ajoutent une migration
EF (`AddQuestCatalogue`, `AddAuctionCateryResource`, `AddWorldLocation`) : c'est là que se situera le
conflit, pas dans l'énumération. **Ne pas réécrire `GamePackets.cs` ni la boucle de `GameClient.cs`** :
`256`/`257` y sont déjà déclarés et `257` n'a pas besoin de bras de réception (§1).

## 9. NON ÉTABLI

1. **Les valeurs 7.3 des deux tables.** Le seul contenu local est le dump NGemity **mi-Epic 4** (§5) :
   ni les 754 lignes de `MixResource`, ni les 240 d'`EnhanceResource` ne sont des données 7.3. Le
   dossier `data/sqlserver/` est **absent** de ce poste (vérifié : `test -d data` faux,
   `.gitignore:482`) et aucune base PostgreSQL n'existe ici. Prérequis précis : exécuter
   `tools/Export-SqlServerData.ps1` sur l'hôte Windows (SQL Server `localhost\SQLEXPRESS`, base
   `Arcadia`), puis importer `MixResource.csv` / `EnhanceResource.csv` — la valeur de `max_enhance`,
   les `mix_type` réellement présents en 7.3, la cardinalité (nombre de lignes, `id` dupliqués ?) et les
   `local_flag` 7.3 restent inconnus tant que ce n'est pas fait.
2. **Les six codes inertes** (11, 12, 15-18) : aucune sémantique locale, aucun usage dans la donnée
   locale. La conduite retenue est le **refus** (§7), pas la validation silencieuse de NGemity.
3. **`CHECK_SAME_SUMMON_CODE` (20)** : `return false` chez NGemity — la règle ne peut jamais
   s'appliquer ; faute de référence, elle aussi est refusée.
4. **Le `local_flag` du serveur NavisLamia** : aucun équivalent de `GameRule::GetLocalFlag()` dans ce
   dépôt (aucune clé de configuration, `grep` sur `DevConsole/*.json` : 0 résultat) ; l'entité porte
   `LocalFlag` mais rien ne choisit la valeur. Quel `local_flag` appliquer (et donc quelle ligne
   d'`EnhanceResource` utiliser pour un `enhance_id` donné) n'est pas établi.
5. **Le sens des `mix_value` par type** : `value[0]` = `enhance_id` pour 101/102/103 est établi par
   recoupement (donnée + `MixManager.cpp:51,123`), mais pour 202/302/402/601 (`value[0]` = code
   d'objet, `value[1]` = ?, `value[2]`/`value[3]`/`value[4]` = taux et bornes selon `CreateItem`, code
   mort) rien n'est vérifié.
6. **Le contenu des ressources client** `db_combineres.rdb`, `db_mixcategory.rdb`, `db_enhance.rdb` :
   non décodées (aucune lecture de `.rdb` n'a été faite pour cette fiche) — les 20 `mix_type` et la
   fenêtre du client ne sont donc pas confirmés côté client 7.3.
7. **La sémantique exacte de 257 côté client** (rafraîchissement d'infobulle vs d'inventaire) est
   déduite du code serveur ; l'asymétrie du cas 102 (§6.3) reste inexpliquée.

## 10. A VERIFIER PAR KILLIAN

1. **Politique d'échec d'un enchantement** (101/103) : `fail_result` est forcé à `"1"` par l'import
   (`Worker.cs:1211-1214`) et NGemity force aussi `0 → 1` (`MixManager.cpp:459-463`) ; le sort des
   châsses n'est implémenté nulle part (boucle infinie, §6.5 pt 1). Que doit-il rester à l'échec :
   drapeau `ITEM_FLAG_FAILED` (0x08) seul, dégradation, destruction ?
2. **Les taux et probabilités** : `fPercentage[niveau d'enhance de l'instance] × 100000` comparé à
   `urand(0, 100000)` (`MixManager.cpp:96-99`) — la formule est lisible, mais `max_enhance = 0` sur
   les 240 lignes rend la donnée inutilisable (§6.5 pt 2) : quelle source de taux retenir en l'absence
   de donnée 7.3 ?
3. **Les six codes `CHECK_*` inertes** (11, 12, 15-18) et **`CHECK_SAME_SUMMON_CODE` (20)** : les
   refuser (choix de la fiche, §7) ou les définir ?
4. **Le `local_flag` du serveur** (§9 pt 4) : quelle valeur pour filtrer/choisir les lignes
   d'`EnhanceResource` (`1`, `2`, `4`, `16`, `32`, ou la ligne multi-drapeaux `456`) ?
5. **Le nom de table** : la fiche **tranche le pluriel** (`MixResources`, `EnhanceResources` inchangé,
   §5.3) à partir de `DevConsole/Program.cs:46`, des migrations et des scripts d'import. À confirmer si
   tu veux au contraire t'aligner sur `ArcadiaSchemaPSQL.sql` — ce serait un renommage, non demandé ici.
6. **Les deux pièges du chemin de données existant** (§5.2) : le `if` toujours vrai qui écrase
   `fail_result`, et la mise à `null` de `RequiredItemId` quand l'objet manque. Faut-il les corriger
   (hors périmètre de cette branche) ?
7. **L'émission de 257** : notre première trame S→C du domaine. `type` absent en 7.3 est tranché par
   rzu (`>= EPIC_8_1`) ; reste à choisir **quand** émettre (NGemity émet à chaque mix traité, y compris
   sur échec) et si l'asymétrie du cas 102 est reproduite ou corrigée.
8. **Le déclencheur de fenêtre** (contact PNJ) : hors de ce lobe, comme l'a tranché la fiche sœur §9.4.

## 11. Bloc destiné à `CLAUDE.md`

```markdown
### Socle artisanat — ressources et moteur (lobe A) — `MixResource` / `EnhanceResource`, 256 et 257

Fiche complète et références : `docs/packet-specs/socle-artisanat-ressources.md`.

- **Le socle structurel de 256 (étape 1) était livré seul** : lecture, bornes, résolution des
  poignées, refus `InvalidArgument`. Ce lobe ajoute les **ressources** puis la **résolution** ; il
  n'ajoute aucune politique de jeu et n'émet toujours pas de 257.
- **Résolution = mécanique, pas politique** : parcours de `MixResource` dans l'ordre de la table,
  `sub_material_count == N`, contrôle de la cible, puis **appariement des matériaux par permutation**
  (pas par position) et post-arrangement (code 19). Sources : `MixManager.cpp:242-298,300-318,553-582`.
- **109 colonnes de `MixResource` lues en position**, jamais par nom isolé : `id`, `mix_type`,
  `mix_value_01..06`, `sub_material_count`, `main_type_01..05`/`main_value_01..05`, puis
  `sub01..sub09` × `type_01..05`/`value_01..05` (`ArcadiaSchemaPSQL.sql:471-582`,
  `ObjectMgr.cpp:1108-1146`). `sub_material_count` est **exactement** le nombre de groupes non nuls
  (0 erreur sur 754 lignes du dump de référence).
- **Clés** : `MixResource.id` est unique (754/754) et n'a **aucune clé** dans le schéma de référence ;
  `EnhanceResource` a la clé composite `(enhance_id, local_flag)` — 240 lignes = 36 × 6 + 12 × 2, et
  l'index unique du dump (sur `enhance_id` seul) est contredit par son propre contenu.
- **Nom de table tranché : pluriel** (`MixResources`, `EnhanceResources`) — la base Arcadia est
  construite par les migrations EF (`DevConsole/Program.cs:46`) et les imports écrivent dans les tables
  pluriel ; `ArcadiaSchemaPSQL.sql` (singulier, sans clés) est un schéma de **référence**, pas le
  schéma du runtime.
- **257 en 7.3 = 11 + 4M octets** (7 en-tête, count `u32` à 7, poignées à 11+4j) ; le champ `type` est
  gaté `>= EPIC_8_1` et **absent** en 7.3 (`TS_SC_MIX_RESULT.h:15`). NGemity n'émet jamais plus d'une
  poignée.
- **Pièges de la référence, ne pas les porter** : la boucle infinie de `procEnhanceFail`
  (`MixManager.cpp:459-501`, `MAX_SOCKET_NUMBER = 4`), `max_enhance = 0` sur toute la donnée de
  référence (l'enchantement ne peut pas réussir), `mix_value_02/03 = 0` pour les 154 lignes 101 alors
  que le code en tire `irand(value[1], value[2])`, et `CreateItem` (`:192-240`) qui n'est appelé de
  nulle part. `default: break` sur les codes 15-18 valide silencieusement : nous refusons.
- **N'écris jamais de trame 257 sans appelant** : sa spécification est complète dans la fiche, son
  écriture appartient au palier des effets.
- **Restent à trancher** (détail en fin de fiche) : taux et politique d'échec, les six codes
  `CHECK_*` inertes, `CHECK_SAME_SUMMON_CODE`, le `local_flag` du serveur, et la donnée 7.3
  (`data/sqlserver/` absent de ce poste).
```

## 12. Commits et binaires épinglés

| dépôt | commit | rôle |
|---|---|---|
| `rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | `TS_CS_MIX.h`, `TS_SC_MIX_RESULT.h`, gating des ids et du champ `type` |
| `ngemity` (RZEmulator) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | `MixManager.{h,cpp}`, `WorldSession.cpp`, `ObjectMgr.cpp`, `Messages.cpp`, `ItemTemplate.hpp`, `Database/Arcadia.sql` |
| Navislamia (base de la branche) | `b56967a07430422add88e0e5cdf292b41b18f6c6` | `master` mesuré : build 0, **1302 tests verts** |

`reference/commits.json` épingle Navislamia à `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` — plus ancien
que `master` : ne pas s'en servir comme base.

Client 7.3 (aucun SHA, ce ne sont pas des dépôts) : `db_combineres.rdb`,
`db_mixcategory.rdb`, `db_enhance.rdb` — empreintes au §2. Source de lecture des tables de
référence : `reference/ngemity/Database/Arcadia.sql`, sha256
`c05c200ae67f8cd2483a27784a976f8afc2ed6b80ff3729fb4f1734f205214fe` (18 667 572 o).
