# Socle des invocations — inventaire des paquets (Epic 7.3)

Fiche de socle : elle ne couvre pas un paquet unique mais la **famille entière des
paquets d'invocation et de familier** du client Epic 7.3, telle qu'elle apparaît dans
`op_codes.md:103-122`, `op_codes.md:134` et `op_codes.md:256-264`. Elle sert à décider
**ce qui peut être implémenté d'abord** sans dépendre d'un arbitrage métier de Killian.

Branche : `hermes/packet-socle-invocations`, créée depuis `master` = `b402e9c`.
Aucun code serveur modifié par cette fiche. Aucun binaire client exécuté : la lecture du
client se limite à `strings` sur `SFrame.exe` et aux `db_*.rdb` déjà extraits.

> **Mise à jour `navis-dev`** (même branche) : l'étape 1 du §8 est implémentée dans
> `Game/Network/Packets/Game/GameSummonPackets.cs`, `Game/Network/Packets/Enums/GamePackets.cs`
> et `Tests/Game/GameSummonPacketsTests.cs` ; voir §9. **Aucune émission n'est câblée** :
> l'étape 2 du §8 reste soumise à l'arbitrage de Killian, donc les constructeurs du §9
> n'ont pas encore d'appelant.


Sources épinglées (§8) : `rzu` `87c1e83bf84efe29bb6405e8e6da80349712f3fa`,
`ngemity` `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (`reference/commits.json`).

Convention d'offset : le paquet commence par l'en-tête de 7 octets
(`uint32 Length`, `ushort ID`, `byte Checksum`, sommé sur les 6 premiers octets —
`CLAUDE.md:48-50`). Les tableaux ci-dessous donnent l'offset **dans la charge utile**
(offset 0 = premier octet après l'en-tête) *et* la taille totale, en-tête compris :
`total = 7 + charge`.

---

## 1. Identité — les 29 ids de la famille

`op_codes.md` fait foi pour l'id et le nom ; `reference/rzu` fait foi pour le gating de
version et l'ordre des champs ; NGemity pour le comportement.

| id | nom | sens | op_codes.md | rzu (fichier) | gating rzu de l'id | NGemity |
|---|---|---|---|---|---|---|
| 301 | TM_SC_ADD_SUMMON_INFO | S→C | :103 | `TS_SC_ADD_SUMMON_INFO.h:18-19` | `< EPIC_9_6_3` → 301, sinon 1301 | déclaré, émis |
| 302 | TM_SC_REMOVE_SUMMON_INFO | S→C | :104 | `TS_SC_REMOVE_SUMMON_INFO.h:10-12` | 302 / 1302 | déclaré, émis |
| 303 | TM_EQUIP_SUMMON | C→S **et** S→C | :105 | `TS_EQUIP_SUMMON.h:11-15` | 303 / 1303 | déclaré, géré (les deux sens) |
| 304 | TM_CS_SUMMON | C→S | :106 | `TS_CS_SUMMON.h:10-12` | 304 / 1304 | déclaré, **aucun gestionnaire** |
| 305 | TM_SC_UNSUMMON | S→C | :107 | `TS_SC_UNSUMMON.h:10-12` | 305 / 1305 | déclaré, émis |
| 306 | TM_SC_UNSUMMON_NOTICE | S→C | :108 | `TS_SC_UNSUMMON_NOTICE.h:9-11` | 306 / 1306 | déclaré, **jamais émis** |
| 307 | TM_SC_SUMMON_EVOLUTION | S→C | :109 | `TS_SC_SUMMON_EVOLUTION.h:13-15` | 307 / 1307 | déclaré, émis |
| 310 | TM_SC_TAMING_INFO | S→C | :116 | `TS_SC_TAMING_INFO.h:10-12` | 310 / 1310 | déclaré, émis |
| 320 | TM_SC_MOUNT_SUMMON | S→C | :111 | `TS_SC_MOUNT_SUMMON.h:12-14` | 320 / 1320 | déclaré, **jamais émis** |
| 321 | TM_SC_UNMOUNT_SUMMON | S→C | :112 | `TS_SC_UNMOUNT_SUMMON.h:10-12` | 321 / 1321 | déclaré, émis |
| 322 | TM_SC_SHOW_SUMMON_NAME_CHANGE | S→C | :113 | `TS_SC_SHOW_SUMMON_NAME_CHANGE.h:8-10` | 322 / 1322 | déclaré, **jamais émis** |
| 323 | TM_CS_CHANGE_SUMMON_NAME | C→S | :114 | `TS_CS_CHANGE_SUMMON_NAME.h:10-12` | 323 / 1323 | déclaré, **aucun gestionnaire** |
| 324 | TM_CS_GET_SUMMON_SETUP_INFO | C→S | :115 | `TS_CS_GET_SUMMON_SETUP_INFO.h:10-12` | 324 / 1324 | déclaré, **géré** |
| 350 | TM_SC_UNSUMMON_PET | S→C | :117 | `TS_SC_UNSUMMON_PET.h:10-12` | 350 / 1350 | déclaré, **jamais émis** |
| 351 | TM_SC_ADD_PET_INFO | S→C | :118 | `TS_SC_ADD_PET_INFO.h:15-17` | 351 / 1351 | déclaré, **jamais émis** |
| 352 | TM_SC_REMOVE_PET_INFO | S→C | :119 | `TS_SC_REMOVE_PET_INFO.h:8-10` | 352 / 1352 | déclaré, **jamais émis** |
| 353 | TM_SC_SHOW_SET_PET_NAME | S→C | :120 | `TS_SC_SHOW_SET_PET_NAME.h:8-10` | 353 / 1353 | déclaré, **jamais émis** |
| 354 | TM_CS_SET_PET_NAME | C→S | :121 | `TS_CS_SET_PET_NAME.h:11-13` | 354 / 1354 | déclaré, **aucun gestionnaire** |
| 355 | TM_CS_SET_PET_FILTER | C→S | :122 | **absent de rzu** | NON ÉTABLI | **absent de NGemity** |
| 452 | TM_CS_SUMMON_CARD_SKILL_LIST | C→S | :134 | `TS_CS_SUMMON_CARD_SKILL_LIST.h:11-13` | 452 / 1452 | déclaré, **aucun gestionnaire** |
| 6000 | TM_CS_REQUEST_FARM_INFO | C→S | :256 | `TS_CS_REQUEST_FARM_INFO.h:10-11` | `X(6000, true)` + « Since EPIC_7_3 » | déclaré |
| 6001 | TM_SC_FARM_INFO | S→C | :257 | `TS_SC_FARM_INFO.h:30-31` | `X(6001, true)` | déclaré |
| 6002 | TM_CS_FOSTER_CREATURE | C→S | :258 | `TS_CS_FOSTER_CREATURE.h:29-30` | `X(6002, true)` | déclaré |
| 6003 | TM_SC_RESULT_FOSTER | S→C | :259 | `TS_SC_RESULT_FOSTER.h:11-12` | `X(6003, true)` | déclaré |
| 6004 | TM_CS_RETRIEVE_CREATURE | C→S | :260 | `TS_CS_RETRIEVE_CREATURE.h:9-10` | `X(6004, true)` | déclaré |
| 6005 | TM_SC_RESULT_RETRIEVE | S→C | :261 | `TS_SC_RESULT_RETRIEVE.h:9-10` | `X(6005, true)` | déclaré |
| 6006 | TM_CS_NURSE_CREATURE | C→S | :262 | `TS_CS_NURSE_CREATURE.h:11-12` | `X(6006, true)` | déclaré |
| 6007 | TM_SC_RESULT_NURSE | S→C | :263 | `TS_SC_RESULT_NURSE.h:11-12` | `X(6007, true)` | déclaré |
| 6008 | TM_CS_REQUEST_FARM_MARKET | C→S | :264 | `TS_CS_REQUEST_FARM_MARKET.h:10-11` | `X(6008, true)` | déclaré |

Deux paquets hors de la table rzu mais utiles au dossier du renommage, voir §7 :

| id | nom | sens | op_codes.md | rzu | NGemity |
|---|---|---|---|---|---|
| 30 | TM_SC_CHANGE_NAME | S→C | :31 | `TS_SC_CHANGE_NAME.h:12-14` | `ClientPackets.h:48` |
| 451 | TM_SC_SKILL_LEVEL_LIST | S→C | :133 | `TS_SC_SKILL_LEVEL_LIST.h` | `ClientPackets.h:143` |

### Taille totale attendue de chaque paquet (7.3, en-tête de 7 octets inclus)

| id | nom | charge utile | **total** | justification |
|---|---|---|---|---|
| 301 | SC_ADD_SUMMON_INFO | 4+4+19+4+4+4 = 39 | **46** | nom en 19 octets pour 7.3 (voir §4) |
| 302 | SC_REMOVE_SUMMON_INFO | 4 | **11** | |
| 303 | EQUIP_SUMMON | 1 + 6×4 = 25 | **32** | déjà vérifié par `Tests/Game/GameCharacterPacketsTests.cs:152` |
| 304 | CS_SUMMON | 1 + 4 = 5 | **12** | |
| 305 | SC_UNSUMMON | 4 | **11** | |
| 306 | SC_UNSUMMON_NOTICE | 4 + 4 = 8 | **15** | `ar_time_t` = `uint32_t` (`GameTypes.h:44`) |
| 307 | SC_SUMMON_EVOLUTION | 4+4+19+4 = 31 | **38** | |
| 310 | SC_TAMING_INFO | 1 + 4 + 4 = 9 | **16** | |
| 320 | SC_MOUNT_SUMMON | 4+4+4+4+1 = 17 | **24** | `bool` = 1 octet (`getSizeOf` → `sizeof(T)`, `PacketDeclaration.h:72-76`) |
| 321 | SC_UNMOUNT_SUMMON | 4+4+1 = 9 | **16** | |
| 322 | SC_SHOW_SUMMON_NAME_CHANGE | 4 | **11** | |
| 323 | CS_CHANGE_SUMMON_NAME | 19 | **26** | nom en 19 octets pour 7.3 |
| 324 | CS_GET_SUMMON_SETUP_INFO | 1 | **8** | |
| 350 | SC_UNSUMMON_PET | 4 | **11** | |
| 351 | SC_ADD_PET_INFO | 4+4+19+4 = 31 | **38** | |
| 352 | SC_REMOVE_PET_INFO | 4 | **11** | |
| 353 | SC_SHOW_SET_PET_NAME | 4 | **11** | |
| 354 | CS_SET_PET_NAME | 4+19 = 23 | **30** | |
| 355 | CS_SET_PET_FILTER | NON ÉTABLI | NON ÉTABLI | absent des deux références |
| 452 | CS_SUMMON_CARD_SKILL_LIST | 4 | **11** | |
| 6000 | CS_REQUEST_FARM_INFO | 0 | **7** | en-tête seul (`TS_CS_REQUEST_FARM_INFO.h:7`) |
| 6001 | SC_FARM_INFO | 1 + N×`TS_FARM_SUMMON_INFO` | **variable** | `count int8` puis tableau (`TS_SC_FARM_INFO.h:26-27`) |
| 6002 | CS_FOSTER_CREATURE | 4 + 4 + 4 + tableaux | **variable** | 2 `count int32` + 2 `dynarray` (`TS_CS_FOSTER_CREATURE.h:23-26`) |
| 6003 | SC_RESULT_FOSTER | 1 | **8** | |
| 6004 | CS_RETRIEVE_CREATURE | 4 | **11** | |
| 6005 | SC_RESULT_RETRIEVE | 1 | **8** | |
| 6006 | CS_NURSE_CREATURE | 4 | **11** | |
| 6007 | SC_RESULT_NURSE | 1 | **8** | |
| 6008 | CS_REQUEST_FARM_MARKET | 0 | **7** | en-tête seul (`TS_CS_REQUEST_FARM_MARKET.h:7`) |
| 30 | SC_CHANGE_NAME | 4 + 19 = 23 | **30** | id et taille valent tous deux 30 : coïncidence, ne pas confondre |

Les deux `count`/`dynarray` de 6001 et 6002 sont la raison pour laquelle la famille
`foster/farm` n'est pas chiffrable sans décision d'implémentation : ce sont des paquets
à taille variable côté client **et** côté serveur.

---

## 2. Ce que le joueur fait pour que le client l'envoie (client 7.3 seul)

Preuves disponibles et leur portée — le client n'est **pas** exécuté, la lecture se limite
aux chaînes de `SFrame.exe` (dump de 63 099 lignes) et à `db_string.rdb`.

Ce qui est établi :

- **La fenêtre de créature s'ouvre au clavier (`Alt + R`) et le double-clic montre les
  statistiques.** `db_string.rdb` (l'un des 50 `db_*.rdb` déjà extraits de `data.000` ;
  l'archive source compte 83 822 entrées, `extraction-manifest.json`) contient le texte de
  la quête de dressage :
  « If you activate the creature window by pressing Alt + R, you can view the stats of
  your summoned creature by double-clicking on it. » et « I assist with creature
  formations. Do you want to change your creature formation? ». Le message interne
  `AUSIMSG_UI_ACT_EQUIP_SUMMON`, `AUSIMSG_UI_ACT_SELECT_SUMMON`, `AUSIMSG_FIXED_CREATURE_SLOT`,
  `AUSIMSG_UI_CREATURE` et `AUSMSG_REQUEST_EQUIP_SUMMON` sont présents en RTTI dans
  `SFrame.exe`.
- **Le client gère en réception** : `MSG_EQUIP_SUMMON` et `MSG_REQUEST_EQUIP_SUMMON`
  (journal `SGameInterface - MSG_…`), `MSG_CHANGE_NAME`, `MSG_UNSUMMON`, `MSG_UNMOUNT_SUMMON`,
  `MSG_MOUNT_SUMMON`, `MSG_SUMMON_EVOLUTION`, `MSG_UNSUMMON_PET`, `MSG_TAMING_INFO` — liste
  des `case MSG_…` du répartiteur statique `SCommandSystem::ProcMsgAtStatic` de `SFrame.exe`.
- **Le client connaît les ids par leur nom** pour 301, 302, 303, 304, 305, 306, 307, 310,
  320, 321, 452 (table de noms `TM_*` de `SFrame.exe`, strictement décroissante par id :
  452, 451, 410, 407, 403, 402, 401, 400, 321, 320, 310, 307, 306, 305, 304, 303, 302, 301…).
- **Le client connaît aussi le renommage** : classes `AUSIMSG_UI_CHANGE_SUMMON_NAME`,
  `AUSMSG_SUMMON_NAME_CHANGE`, `AUSIMSG_REQ_SET_PET_NAME`, `AUSMSG_SHOW_SET_PET_NAME`, et
  les libellés « Creature Name Change » / « Pet Name Change » de `db_string.rdb`.

Ce qui **n'est pas** établi : l'action d'interface exacte qui émet chaque id. La table de
noms `TM_*` de `SFrame.exe` est **incomplète** — elle ne contient pas `TM_SC_ADDED_SKILL_LIST`
(404) alors que le client gère bien ce paquet (`case MSG_ADD_SKILL_LIST`) et que notre
serveur l'envoie (`GameCharacterPackets.cs`, `TM_SC_ADDED_SKILL_LIST`). Elle ne contient ni
`TM_CS_CHANGE_SUMMON_NAME` (323), ni `TM_CS_GET_SUMMON_SETUP_INFO` (324), ni
`TM_SC_SHOW_SUMMON_NAME_CHANGE` (322), ni la famille `TM_*PET*` (350-355), ni la famille
farm (6000-6008) — sans qu'on puisse en conclure quoi que ce soit sur leur usage en 7.3.
Les scripts d'interface du client (Lua) ne sont pas extraits : les archives `data.001` à
`data.008` sont absentes du VPS (`reference/README.md`).

Formulation prudente retenue pour la suite : les ids présents sont « connus du binaire » ;
les ids absents sont « non prouvés », jamais « inutilisés ».

---

## 3. Structure sur le fil (source par champ)

### 303 — TM_EQUIP_SUMMON, `rzu/…/GameClient/TS_EQUIP_SUMMON.h`

| offset | type | nom | source |
|---|---|---|---|
| 0 | `bool` (1) | `open_dialog` | `TS_EQUIP_SUMMON.h:8` |
| 1 | `ar_handle_t[6]` (24) | `card_handle` | `TS_EQUIP_SUMMON.h:9` |

`SessionPacketOrigin::Any` (`TS_EQUIP_SUMMON.h:15`) : le paquet circule dans les deux sens.
Chez nous il est déjà émis en S→C (`GameCharacterPackets.cs:160-174`). **Total 32 octets.**

### 301 — TM_SC_ADD_SUMMON_INFO, `TS_SC_ADD_SUMMON_INFO.h`

| offset | type | nom | source |
|---|---|---|---|
| 0 | `ar_handle_t` (4) | `card_handle` | `TS_SC_ADD_SUMMON_INFO.h:8` |
| 4 | `ar_handle_t` (4) | `summon_handle` | `:9` |
| 8 | `string` (19 en 7.3) | `name` | `:10-12` |
| 27 | `int32_t` (4) | `code` | `:13` |
| 31 | `int32_t` (4) | `level` | `:14` |
| 35 | `int32_t` (4) | `sp` | `:15` |

**Total 46 octets.** Aucun gating sur `code`, `level` et `sp` : `int32_t` en 7.3.

### 302 — TM_SC_REMOVE_SUMMON_INFO

| offset | type | nom | source |
|---|---|---|---|
| 0 | `ar_handle_t` (4) | `card_handle` | `TS_SC_REMOVE_SUMMON_INFO.h:7` |

**Total 11 octets.**

### 305 — TM_SC_UNSUMMON

| offset | type | nom | source |
|---|---|---|---|
| 0 | `ar_handle_t` (4) | `summon_handle` | `TS_SC_UNSUMMON.h:8` |

**Total 11 octets.**

### 306 — TM_SC_UNSUMMON_NOTICE

| offset | type | nom | source |
|---|---|---|---|
| 0 | `ar_handle_t` (4) | `summon_handle` | `TS_SC_UNSUMMON_NOTICE.h:6` |
| 4 | `ar_time_t` (4) | `unsummon_duration` | `:7` |

**Total 15 octets.** `ar_time_t` = `uint32_t` (`GameTypes.h:44`).

### 307 — TM_SC_SUMMON_EVOLUTION

| offset | type | nom | source |
|---|---|---|---|
| 0 | `ar_handle_t` (4) | `card_handle` | `TS_SC_SUMMON_EVOLUTION.h:6` |
| 4 | `ar_handle_t` (4) | `summon_handle` | `:7` |
| 8 | `string` (19) | `name` | `:8-10` |
| 27 | `int32_t` (4) | `code` | `:11` |

**Total 38 octets.**

### 303/324 — les deux entrées de la fenêtre de créature

- **324 TM_CS_GET_SUMMON_SETUP_INFO** : `bool show_dialog` (`TS_CS_GET_SUMMON_SETUP_INFO.h:8`).
  **Total 8 octets.**
- **303 TM_EQUIP_SUMMON** (C→S) : même disposition que ci-dessus, déclarée
  `SessionPacketOrigin::Any`. **Total 32 octets.**

### 304 — TM_CS_SUMMON

| offset | type | nom | source |
|---|---|---|---|
| 0 | `int8_t` (1) | `is_summon` | `TS_CS_SUMMON.h:7` |
| 1 | `ar_handle_t` (4) | `card_handle` | `:8` |

**Total 12 octets.** En-tête rzu commenté « Seems unused » (`TS_CS_SUMMON.h:5`).

### 323 — TM_CS_CHANGE_SUMMON_NAME

| offset | type | nom | source |
|---|---|---|---|
| 0 | `string` (19 en 7.3) | `name` | `TS_CS_CHANGE_SUMMON_NAME.h:6-8` |

**Total 26 octets.**

### 354 — TM_CS_SET_PET_NAME

| offset | type | nom | source |
|---|---|---|---|
| 0 | `ar_handle_t` (4) | `handle` | `TS_CS_SET_PET_NAME.h:6` |
| 4 | `string` (19 en 7.3) | `name` | `:7-9` |

**Total 30 octets.**

### 452 — TM_CS_SUMMON_CARD_SKILL_LIST

| offset | type | nom | source |
|---|---|---|---|
| 0 | `ar_handle_t` (4) | `item_handle` | `TS_CS_SUMMON_CARD_SKILL_LIST.h:8` |

**Total 11 octets.** En-tête commenté « Since EPIC_7_3 » (`:10`) : le paquet **apparaît**
en 7.3, l'id 452 y est donc natif.

### 320 / 321 / 322 / 310 / 350-353 (annexe, S→C)

| id | disposition (source) | total |
|---|---|---|
| 320 | `handle` u32, `summon_handle` u32, `float x`, `float y`, `bool success` (`TS_SC_MOUNT_SUMMON.h:6-10`) | 24 |
| 321 | `handle` u32, `summon_handle` u32, `int8 flag` (`TS_SC_UNMOUNT_SUMMON.h:6-8`) | 16 |
| 322 | `handle` u32 (`TS_SC_SHOW_SUMMON_NAME_CHANGE.h:6`) | 11 |
| 310 | `int8 mode`, `tamer_handle` u32, `target_handle` u32 (`TS_SC_TAMING_INFO.h:6-8`) | 16 |
| 350 | `handle` u32 (`TS_SC_UNSUMMON_PET.h:8`) | 11 |
| 351 | `cage_handle` u32, `pet_handle` u32, `name` 19, `code` i32 (`TS_SC_ADD_PET_INFO.h:8-13`) | 38 |
| 352 | `handle` u32 (`TS_SC_REMOVE_PET_INFO.h:6`) | 11 |
| 353 | `handle` u32 (`TS_SC_SHOW_SET_PET_NAME.h:6`) | 11 |

### 30 — TM_SC_CHANGE_NAME (annexe renommage)

| offset | type | nom | source |
|---|---|---|---|
| 0 | `ar_handle_t` (4) | `handle` | `TS_SC_CHANGE_NAME.h:6` |
| 4 | `string` (19) | `name` | `:7-9` |

**Total 30 octets.** Gating `X(30, version < EPIC_9_6_3)` (`:12-13`).

### 6000-6008 (annexe farm)

- 6000 et 6008 : corps vide → **7 octets**. Sources `TS_CS_REQUEST_FARM_INFO.h:7`,
  `TS_CS_REQUEST_FARM_MARKET.h:7`.
- 6003, 6005, 6007 : `int8 result` → **8 octets**. Sources `TS_SC_RESULT_FOSTER.h:8`,
  `TS_SC_RESULT_RETRIEVE.h:6`, `TS_SC_RESULT_NURSE.h:8`.
- 6004, 6006 : `ar_handle_t creature_card_handle` → **11 octets**. Sources
  `TS_CS_RETRIEVE_CREATURE.h:6`, `TS_CS_NURSE_CREATURE.h:8`.
- 6002 : `creature_card_handle` (4) + `count int32` ×2 + `dynarray` ×2 → variable
  (`TS_CS_FOSTER_CREATURE.h:22-26`).
- 6001 : `count int8` + `dynarray TS_FARM_SUMMON_INFO` → variable
  (`TS_SC_FARM_INFO.h:26-27`) ; chaque entrée embarque un `TS_ITEM_FIXED_INFO`
  (`:20`), donc les octets d'une carte d'objet.

---

## 4. Gating de version — décisions prises pour 7.3

1. **Ids.** Tous les `ID(X)` de la famille, sauf le farm, opposent `X(3xx, version < EPIC_9_6_3)`
   et `X(13xx, version >= EPIC_9_6_3)`. **Pour 7.3 : les ids à trois chiffres**
   (301, 302, 303, 304, 305, 306, 307, 310, 320, 321, 322, 323, 324, 350-354, 452).
   Le préfixe « 1 » des 1301/1452 est une bascule d'id d'Epic 9.6.3 et ne concerne pas 7.3.
2. **Champs chaîne.** `_(def)(string)(name, 20)` + `_(impl)(string)(name, 19, version < EPIC_9_6)`
   apparaît sur 301, 307, 323, 351, 354, 30 (et sur 6001 via `TS_FARM_SUMMON_INFO`).
   **Pour 7.3 : 19 octets**, donc 18 caractères utiles + l'octet nul, sans préfixe de
   longueur (`MessageBuffer::writeString` écrit un tampon fixe de `maxSize` octets, tronqué
   à `maxSize - 1`, `reference/rzu/.../Packet/MessageBuffer.cpp:87-94`).
3. **Farm (6000-6008).** `X(600x, true)` avec le commentaire « Since EPIC_7_3 » : ces paquets
   **apparaissent en 7.3 et leurs ids n'ont jamais bougé**. Rien à trancher, mais ils ne sont
   pas au socle (paquets à taille variable, cf. §3).
4. **`TS_FARM_SUMMON_INFO.unknown`** est gaté `version >= EPIC_9_8_1` (`TS_SC_FARM_INFO.h:11`)
   → **absent en 7.3**. À ne pas allouer si le farm est implémenté un jour.
5. **Aucun autre champ de la famille n'est gaté** : `code`, `level`, `sp`, `unsummon_duration`,
   `is_summon`, `flag`, `success`, `mode` gardent leur largeur rzu en 7.3.

---

## 5. Traitement attendu (NGemity `Chihiro`, commit épinglé)

### Entrées client

| id | NGemity | ce que fait la référence | source |
|---|---|---|---|
| 303 | `onEquipSummon` | valide les cartes de `m_aBindSummonCard` (limite `GetCurrentSkillLevel(SKILL_CREATURE_CONTROL)`, `SKILL_CREATURE_CONTROL = 1801` en `Skills/SkillBase.h:103`), crée les `Summon` manquants via `sMemoryPool.AllocNewSummon`, compacte les emplacements, puis **répond** `TS_EQUIP_SUMMON` (les 6 handles + `open_dialog` recopié de la requête) | `WorldSession.cpp:936-1018`, `Messages.cpp:122-135` |
| 324 | `onGetSummonSetupInfo` | **ne fait rien d'autre** que renvoyer le même `TS_EQUIP_SUMMON` avec le `show_dialog` reçu : aucun écrit en base | `WorldSession.cpp:692-695`, `Messages.cpp:122-135` |
| 304 | — | déclaré, **aucun gestionnaire** | `shared/Server/Packets/GameClient/TS_CS_SUMMON.h` |
| 323 | — | déclaré, **aucun gestionnaire** | `shared/Server/Packets/GameClient/TS_CS_CHANGE_SUMMON_NAME.h` |
| 354 | — | déclaré, **aucun gestionnaire** | `shared/Server/Packets/GameClient/TS_CS_SET_PET_NAME.h` |
| 452 | — | déclaré, **aucun gestionnaire** | `shared/Server/Packets/GameClient/TS_CS_SUMMON_CARD_SKILL_LIST.h` |
| 6002/6004/6006 | — | déclarés, aucun gestionnaire ; `SkillBase.h` n'a aucune constante farm | `shared/Server/Packets/GameClient/`, `Chihiro/src/Skills/SkillBase.h` |

Le point qui compte pour la conception : **la référence n'a qu'une seule réponse pour 303
et 324 — le paquet 303 (S→C)**, et 324 ne persiste rien. Un `BuildEquipSummon` unique suffit
donc aux deux entrées, à ceci près que 324 doit recevoir `show_dialog` de la requête et non
`open_dialog`.

### Sorties serveur — où NGemity émet chaque paquet

| id | émetteur | source |
|---|---|---|
| 301 | `Messages::SendAddSummonMessage` après `Player::AddSummon`, sur `card_handle` / `summon_handle` / `name` / `code` / `level` / `sp`, puis enchaîne stats, HP/MP, niveau, exp et **liste de compétences du familier** (`SendSkillList(pPlayer, pSummon, -1)`) | `Messages.cpp:99-121`, appelé par `Player.cpp:1501` et au login `Player.cpp:774` |
| 302 | `Messages::SendRemoveSummonMessage` sur le handle de la carte | `Messages.cpp:1038-1048`, appelé par `Player.cpp:1514` |
| 305 | `Player::DoUnSummon` — sortie du familier du monde, puis `sWorld.Broadcast` de `TS_SC_UNSUMMON` dans la région | `Player.cpp:1594-1616` |
| 307 | `Summon::DB_UpdateSummon` (évolution) | `Summon.cpp:277` |
| 310 | `Messages::BroadcastTamingMessage`, appelé depuis `World.cpp:631-710` (modes 0 à 3) | `Messages.cpp:800-810` |
| 321 | `Player` à la fin du montage (`TS_SC_UNMOUNT_SUMMON`, diffusé en région) | `Player.cpp:3619-3626` |
| 306 | **jamais émis** | grep complet de `Chihiro/src` |
| 320 | **jamais émis** | idem |
| 322, 350-353 | **jamais émis** | idem |

### Le familier dans le monde et dans la base (modèle de référence)

- Au login, NGemity restaure les familiers de la base, envoie 301 pour chacun
  (`Player.cpp:743-744` puis `:774`) et ne fait entrer dans le monde que le **familier
  principal** (`m_pMainSummon`, `Player.cpp:823`, `DoSummon` à `Player.cpp:1574-1590`).
- Le chemin de jeu du « summon » côté serveur est **la compétence**, pas le paquet 304 :
  `Skill::DO_SUMMON` / `Skill::DO_UNSUMMON` (`Skill.cpp:1537-1583`) sont appelés par les
  effets de compétence 601/602 et exigent une carte de `ItemGroup::GROUP_SUMMONCARD`.
- La mort du familier le renvoie (`Summon::Update` → `DoUnSummon`, `Summon.cpp:380-384`) et
  la mort du joueur démonte le familier (`Player.cpp:3539`).
- `rzu` ne modélise le familier que côté client de test : `rzgame` alloue un `sid` de
  `Summon` (`rzgame/src/ReferenceData/ReferenceDataMgr.cpp:73`) et sonde le paquet 303 au
  login (`rzgame/src/Component/Character/Character.cpp:225-226`) ; `rzclientreconnect` indexe
  `TS_SC_ADD_SUMMON_INFO` **par `card_handle`** (`ConnectionToServer.cpp:612`) et efface
  l'entrée sur 302 (`:616`), et traite 306 comme un compte à rebours de renvoi
  (`ConnectionToServer.cpp:328-333`).

---

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | Pourquoi |
|---|---|
| NGemity n'implémente aucune création de familier hors 303 ; nous devons décider qui écrit `SummonEntity` | La référence crée le `Summon` dans `onEquipSummon` (`WorldSession.cpp:990-992`), c'est-à-dire au moment où le joueur **compose sa formation**. C'est un choix de conception, pas une contrainte du protocole : le protocole admet aussi une création à l'utilisation de la carte. |
| NGemity déclare 303 `SessionPacketOrigin::Any` ; notre enum ne connaît que `TM_EQUIP_SUMMON` en S→C | Le même id sert dans les deux sens. Si nous ajoutons la gestion C→S, le `GameClient.Receive` doit accepter 303 et l'énumération ne doit pas dupliquer la valeur 303 sous deux noms : `GamePackets` est projeté en `ushort` et un doublon casserait le `switch` de `GameClient.cs:670`. |
| NGemity ignore 304, 323, 354 et 452 | La référence 7.3+ n'en a pas besoin pour sa démo, ce n'est pas une preuve d'inutilisation. Nous les documentons sans les implémenter tant que le déclencheur client 7.3 n'est pas établi (§7). |
| NGemity n'émet jamais 306 et 320, alors que rzu les modélise | Le compte à rebours de renvoi (306) est le mécanisme naturel du « familier qui reste 5 min après la mort » ; `rzclientreconnect` l'exploite (`ConnectionToServer.cpp:328-333`) mais NGemity le remplace par `Update()` + `DoUnSummon` à 6 s (`Summon.cpp:380-384`). Divergence de gameplay, pas de protocole. |
| NGemity ne modélise pas le gating `>= EPIC_9_6_3` (ses `CREATE_PACKET` sont figés sur les ids 3xx) | Sa cible est Epic 4.x/7.x : pour 7.3 nos décisions coïncident, mais **nous suivons rzu** pour tout `version <` / `version >=`, jamais NGemity. |

---

## 7. Ce qui existe déjà dans notre dépôt

| Élément | source | remarque |
|---|---|---|
| `TM_EQUIP_SUMMON = 303` — **seul membre de la famille** dans l'énumération | `Game/Network/Packets/Enums/GamePackets.cs:41` | 301, 302, 304, 305, 306, 307, 320-324, 350-355, 452, 6000-6008 et 30 sont absents |
| `BuildEquipSummon(long[] summonSlots)` → 32 octets, `open_dialog = 0` | `Game/Network/Packets/Game/GameCharacterPackets.cs:160-174` | `HeaderSize = 7` (`:14`), `CreatePacket` `:328-334`, `WriteChecksum` `:336-346` |
| Envoi de 303 au login, après l'inventaire, avant `wear_info` | `GameActions.cs:190` | NGemity fait de même au login (`Player.cpp:774`) |
| `SummonSlotItemIds` (6 max) | `CharacterEntity.cs:63`, `TelecasterContext.cs:116` | commentaire d'origine : « verify if item id or item resource id » — la nature de la valeur n'est pas tranchée |
| `MainSummonId`, `SubSummonId`, `RemainSummonTime` | `CharacterEntity.cs:65-71` | navigation vers `SummonEntity` |
| `SummonEntity` (32 lignes) | `Game/DataAccess/Entities/Telecaster/SummonEntity.cs` | `SummonResourceId`, `CardItemId`, `Exp`, `Jp`, `Name`, `Transform`, `Lv`, `Jlv`, `MaxLevel`, `Fp`, `Hp`, `Mp`, `PreviousLevel[]`, `PreviousSummonResourceIds[]` |
| `SummonResourceEntity` (64 lignes) | `Game/DataAccess/Entities/Arcadia/SummonResourceEntity.cs` | auto-références `EvolveTarget` / `EvolveSource` |
| Tables Arcadia : `SummonResource` (1616), `SummonDefaultNameResource` (1186-1192), `SummonLevelResource` (1193-1200), `SummonUniqueNameResource` (1201-1207) | `ArcadiaSchemaPSQL.sql` | `SummonLevelResource` fournit `normal_exp` / `growth_exp` / `evolve_exp` par niveau |
| Aucun service, dépôt ni catalogue pour l'invocation | `Game/Services/` | il n'y a **rien** entre les entités et `BuildEquipSummon` |
| `SkillEffectType.Summon = 601`, `Unsummon = 602`, `UnsummonAndAddState = 605` | `Game/DataAccess/Entities/Enums/SkillEffectType.cs:122-124` | les effets qui déclenchent l'invocation chez NGemity (`Skill.cpp:1537-1583`) |
| `ItemEffectInstant.SummonPet = 90`, `RenameSummon = 115`, `ResetSummonSkill = 116` | `ItemEffectInstant.cs:19,28,29` | `RenameSummon` est le consommable de renommage : c'est lui qui devrait mener à 322/323 ou à 30 |
| `ItemGroup.Summoncard = 13` | `ItemGroup.cs:18` | groupe des cartes de créature |
| Les invocations ne sont pas modélisées côté combat/buffs | `SkillCastService.cs:286`, `BuffCatalog.cs:42-52` | les cibles `Summon` (31) / `PartySummon` (32) sont refusées explicitement |
| `BuildEnterNpc` / `BuildEnterMonster` (voie `TS_SC_ENTER`) mais **aucun** `BuildEnterSummon` | `GameSpawnPackets.cs:18`, `:30` | le familier a besoin d'une entrée de monde (cf. `CLAUDE.md:163-191`) |
| `TM_SC_CHANGE_NAME (30)` absent de l'énumération | `GamePackets.cs` | pourtant géré par le client 7.3 (`case MSG_CHANGE_NAME`) |
| Le `switch` final qui tue la boucle de réception | `Game/Network/Clients/GameClient.cs:670-686` | `_ => throw new Exception("Unknown Packet Type")` ; `CLAUDE.md:1147-1153` impose enum + dispatch dans le même changement |

### État de `master` et des branches

- `master` == `origin/master` == `b402e9c` ; `git log --oneline origin/master..master` est vide.
- Le `.gitignore` de `master` ignore `docs/packet-specs/` : la fiche n'y serait pas
  committable. `.gitignore` a donc été repris **à l'identique** de la branche sœur
  `hermes/packet-221-hide-equip-info` (`git checkout hermes/packet-221-hide-equip-info -- .gitignore`),
  vérifié : `git hash-object .gitignore` = `c8c9d96a52f1e35076396dd0d99a96093d3ac863`.
- Branches `hermes/*` présentes localement et sur `origin` : `packet-1202-emotion`,
  `packet-203-drop-item`, `packet-221-hide-equip-info`, `packet-223-swap-equip`,
  `packet-253-use-item`, `packet-408-request-remove-state`, `packet-socle-anti-triche`,
  `packet-socle-mort-respawn`, `packet-socle-zones-evenement`.
- Base mesurée sur ce poste, avant toute modification de code : `dotnet build Navislamia.sln -c Debug`
  → code de sortie 0 ; `dotnet test Tests/Tests.csproj` → **366 réussis, 0 échec**
  (soit le plancher exigé par les critères transversaux).
- L'état des merge requests n'est **pas** vérifiable d'ici (pas d'accès GitHub dans ce profil).

---

## 8. Sous-ensemble minimal du socle — ce que cette fiche autorise

### Cadre imposé par le board

Source : carte de suivi `navislamia:socle:invocations` (tâche `t_caf8d932`, `navis-dev`).
Les paquets **324, 452, 323, 354, 355 et 6002-6008 ont leurs propres cartes** et ne sont
**pas** traités par le socle ; **304** attend en `THINKING` avec la sienne. Les ids restants
de la famille, seuls éligibles ici, sont donc : 301, 302, 305, 306, 307, 310, 320, 321, 322,
350, 351, 352, 353, 6000, 6001, 6003, 6004, 6005, 6006, 6007.

### Le constat qui commande le découpage

Parmi ces ids éligibles, **dix-sept sont des émissions serveur** (301, 302, 305, 306, 307, 310,
320, 321, 322, 350-353, 6001, 6003, 6005, 6007), et **aucune n'a de destinataire possible**
aujourd'hui : il n'existe dans le dépôt ni invocation en base peuplée, ni invocation dans le
monde. Les seuls ids client→serveur du socle sont 6000, 6004 et 6006 (farm : hors socle par
les cartes) — c'est-à-dire que **le socle ne contient, en propre, aucun paquet entrant**.

Conséquence directe sur le critère « énumération et dispatch ensemble » : pour un id
strictement S→C il n'y a **pas** de bras à ajouter dans `GameClient.cs:670-686` et il ne faut
pas en inventer — le `switch` final ne reçoit que ce que le client envoie. Un membre
`GamePackets` S→C ajouté sans dispatch n'atteint donc jamais ce `switch`. La règle
`CLAUDE.md:1147-1153` vise les paquets **traités en réception** ; la fiche la cite ici dans
ce sens précis pour éviter un handler fictif.

### Étape 1 — ce qui est implémentable sans aucune décision de jeu

1. Les constructeurs et leurs tests d'offsets pour les émissions d'information et de sortie :
   **301 (46 o), 302 (11 o), 305 (11 o), 306 (15 o)**, plus **307 (38 o)** et **320/321
   (24/16 o)** si le lot est pris entier. Chaque champ, sa position et la somme sont au §3 :
   rien n'est à deviner, la version est tranchée au §4.
2. La **lecture** d'un personnage et de ses invocations : résolution de
   `CharacterEntity.MainSummonId` / `SubSummonId` / `SummonSlotItemIds` (`CharacterEntity.cs:63-71`)
   vers `SummonEntity` (`SummonResourceId`, `CardItemId`, `Name`, `Lv`, `Jlv`, `MaxLevel`,
   `Exp`, `Sp`, `Hp`, `Mp`) et `SummonResourceEntity`. Aucun seuil, aucune durée, aucun coût
   n'est nécessaire pour cette lecture.
3. Le mapping `SummonEntity` → charge utile de 301, qui est entièrement déterminé par le
   schéma existant : `name` ← `SummonEntity.Name`, `level` ← `SummonEntity.Lv`,
   `sp` ← `SummonEntity.Sp`, `card_handle` ← `SummonEntity.CardItemId`, `summon_handle` ←
   identité de l'invocation. Seul `code` reste ambigu (voir ci-dessous).

Le point qui n'est **pas** tranchable ici : `code` (`int32`, `TS_SC_ADD_SUMMON_INFO.h:13`).
NGemity y met `pSummon->GetSummonCode()` (`Messages.cpp:107`), dont la source n'est pas
`SummonResourceId` avec certitude. **Question ouverte 4 ci-dessous.**

**Statut au terme de la tâche `navis-dev`** : les points **1 et 3 sont implémentés** (§9).
Le point **2 ne l'est pas** : sa seule consommatrice est l'étape 2, non autorisée, et la
résolution de `SummonSlotItemIds` exigerait de trancher la nature de ce tableau
(`CharacterEntity.cs:63`, « item id or item resource id »). Écrire la requête maintenant
serait une lecture de table jamais peuplée sur une sémantique non établie.

### Étape 2 — conditionnée à un arbitrage

**Émettre 301 au login pour chaque invocation déjà enregistrée**, comme la référence
(`Player.cpp:769-775`, puis entrée dans le monde du seul familier principal `:823`), élargit
le bootstrap de personnage décrit en `CLAUDE.md:118` et modifie ce que le client voit à
l'entrée en jeu. C'est un choix de Killian, pas une déduction.

### Si Killian refuse l'étape 2

Le socle n'a alors **aucun sous-ensemble observable** : les constructeurs de l'étape 1
seraient du code mort (aucun appelant), et le livrable honnête du dev est le constat, pas un
handler inventé. C'est explicitement un résultat recevable pour la carte `t_caf8d932`.

### Hors du minimum (à découper plus tard, dans l'ordre des dépendances)

- **305/306** : exigent une invocation **dans le monde** et un délai de renvoi
  (`unsummon_duration`) — mécanisme que NGemity n'émet jamais et remplace par
  `Summon::Update` + 6 s (`Summon.cpp:380-384`). Décision de gameplay.
- **307 (évolution)** : dépend de `SummonResourceEntity.EvolveTarget` et d'un barème
  (`SummonLevelResource.evolve_exp`, `ArcadiaSchemaPSQL.sql:1193`), donc d'une politique.
- **310 (dressage)**, **322 + 350-353 (renommage familier)**, **6000-6007 (farm)** :
  hors socle par les cartes, et sans gestionnaire chez NGemity.
- **Entrée du monde** : aucun `BuildEnterSummon` n'existe (`GameSpawnPackets.cs:18`, `:30`) ;
  c'est le prérequis de 305/306/307/320/321 (question ouverte 8).

**Le socle ne se coupe donc pas « structurellement » en trois lots indépendants** : il se
réduit à **une étape sans arbitrage (constructeurs + lecture + mapping)** et **une étape
conditionnée (émission au login)**. Toute implémentation qui irait au-delà choisirait à la
place de Killian quand une invocation existe, combien de temps elle reste et ce qu'elle
devient à la mort du maître.

---

## 9. Implémentation du socle (`navis-dev`)

Branche `hermes/packet-socle-invocations`. Aucune décision de jeu n'y est prise : les
constructeurs encodent le fil, ils ne choisissent ni le moment, ni la durée, ni le coût d'une
émission.

### 9.1 Ce qui a été écrit

| Fichier | contenu |
|---|---|
| `Game/Network/Packets/Game/GameSummonPackets.cs` | constructeurs S→C 301, 302, 305, 306, 307, 320, 321 |
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_SC_ADD_SUMMON_INFO` (301), `TM_SC_REMOVE_SUMMON_INFO` (302), `TM_SC_UNSUMMON` (305), `TM_SC_UNSUMMON_NOTICE` (306), `TM_SC_SUMMON_EVOLUTION` (307), `TM_SC_MOUNT_SUMMON` (320), `TM_SC_UNMOUNT_SUMMON` (321) |
| `Tests/Game/GameSummonPacketsTests.cs` | 15 tests d'offsets |

Tailles produites, chacune **mesurée par un test** :

| id | constructeur | charge utile | **total** |
|---|---|---|---|
| 301 | `BuildAddSummonInfo` | 39 | **46** |
| 302 | `BuildRemoveSummonInfo` | 4 | **11** |
| 305 | `BuildUnsummon` | 4 | **11** |
| 306 | `BuildUnsummonNotice` | 8 | **15** |
| 307 | `BuildSummonEvolution` | 31 | **38** |
| 320 | `BuildMountSummon` | 17 | **24** |
| 321 | `BuildUnmountSummon` | 9 | **16** |

Ces sept totaux sont identiques à ceux du §1 : aucun écart entre la fiche et le code.

### 9.2 Les décisions de version, appliquées telles quelles

- **Ids à trois chiffres** (§4.1) : 301, 302, 305, 306, 307, 320, 321. Les variantes 13xx
  d'Epic 9.6.3 ne sont pas dans l'énumération.
- **`name` en 19 octets** (§4.2) : `GameSummonPackets.NameSize = 19`. Le tampon est écrit en
  ASCII, tronqué à 18 caractères et complété par des zéros, comme `MessageBuffer::writeString`
  (`rzu/librzu/src/lib/Packet/MessageBuffer.cpp:87-94`).
- **`bool` sur un octet** (§4.5) : `success` de 320 est écrit `0`/`1` dans un seul octet, ce qui
  est ce qui donne 17 octets de charge utile ; `sizeof(bool) == 1` côté rzu
  (`PacketDeclaration.h:72-76`).
- **`unsummon_duration` en `ar_time_t`** (`GameTypes.h:44`) : `uint32`, en ticks de 10 ms. Le
  constructeur prend des ticks et ne convertit rien — `ServerClock.TicksPerSecond` reste le
  seul endroit qui sache qu'un tick vaut 10 ms.
- Aucun autre champ de ces sept paquets n'est gaté par version : `code`, `level`, `sp`, `flag`,
  `success`, `x`, `y` gardent la largeur du §3.

### 9.3 `code` et `summon_handle` restent fournis par l'appelant

`BuildAddSummonInfo(SummonEntity summon, uint summonHandle, int code)` applique le mapping du
point 3 de l'étape 1 (`card_handle` ← `CardItemId`, `name` ← `Name`, `level` ← `Lv`,
`sp` ← `Sp`) et laisse **deux** paramètres au décideur :

- `code` : la source de ce champ n'est pas établie (`NON ÉTABLI` 4). Le constructeur ne
  substitue aucune valeur par défaut, et un test vérifie que l'entier reçu est écrit tel quel.
- `summon_handle` : un handle identifie une invocation **dans le monde**, et aucune n'y entre
  (`NON ÉTABLI` 8). Le constructeur ne fabrique pas d'identité.

Le cast `(uint)summon.CardItemId` est une troncature assumée (`ar_handle_t` = 32 bits,
`CardItemId` = `long`) ; un test l'épingle.

### 9.4 Aucune émission câblée, et pourquoi

Aucun appelant n'existe : l'étape 2 du §8 (émettre 301 au login) est explicitement soumise à
l'arbitrage de Killian et n'est pas implémentée. 305, 306, 307, 320 et 321 supposent en plus une
invocation dans le monde, une politique de délai de renvoi, un barème d'évolution et une règle
de monture qui ne sont tranchés nulle part. Écrire l'un de ces appelants aurait choisi à la
place de Killian, ce que la carte interdit.

Conséquence assumée : ces constructeurs sont du code **testé mais non appelé**. C'est le cas de
figure annoncé au §8 ; le livrable utile est le layout vérifié, pas un handler inventé.

### 9.5 Énumération et dispatch

Les sept ids ajoutés sont **strictement S→C**. Aucun bras n'a été ajouté au `switch` final de
`GameClient.cs:670-682`, et il ne faut pas en inventer : ce `switch` ne voit que ce que le client
envoie, donc aucun de ces membres ne peut atteindre
`_ => throw new Exception("Unknown Packet Type")`. La règle « énumération et dispatch dans le même
changement » vise les paquets **traités en réception** (`CLAUDE.md`, *Change guidelines*) : elle
n'impose pas d'inventer une trame C→S pour un id que le serveur ne fait qu'émettre.

C'est déjà la convention du dépôt, et c'est mesurable : `GamePackets` compte 80 membres, 44 sont
référencés par la chaîne de réception (`GameClient.cs` + `GameActions.cs`), et **28 membres
`TM_SC_*` antérieurs à ce lot n'ont aucun bras** (`TM_SC_SKIN_INFO` 224, `TM_SC_HAIR_INFO` 220,
`TM_SC_ITEM_WEAR_INFO` 287, `TM_SC_CHAT` 22, `TM_SC_WARP` 12, `TM_SC_PROPERTY` 507… ). Ce lot
porte ce total de 28 à 35 sans changer la règle. 303 reste le seul id de la famille employé dans
les deux sens, et il n'est pas touché ici.

Un test garde la propriété qui compte pour ce `switch` : **aucune valeur de `GamePackets` n'est
dupliquée**. L'énumération est projetée en `ushort` ; deux noms sur la même valeur feraient
disparaître un bras sans bruit.

### 9.6 Vérifications exécutées

| Commande | code de sortie | résultat |
|---|---|---|
| `dotnet build Navislamia.sln -c Debug` | 0 | 0 erreur |
| `dotnet test Tests/Tests.csproj` | 0 | **381 réussis, 0 échec** (366 avant ce lot, +15) |

---

## NON ÉTABLI

1. **355 TM_CS_SET_PET_FILTER** : présent uniquement dans `op_codes.md:122`. Disposition,
   largeur et déclencheur **inconnus** — le paquet est absent de `rzu` (`grep -r 355` sur
   `librzu/src/packets`, aucun résultat) et de NGemity (`ClientPackets.h` n'a pas de 355).
   Question : le client 7.3 émet-il ce paquet, et avec quelle charge ?
2. **Quel id porte le renommage d'invocation en 7.3** : 322 (S→C « ouvre la boîte de
   dialogue ») puis 323 (retour) ? ou 30 `TM_SC_CHANGE_NAME` (présent dans la table du client
   et dans le répartiteur `MSG_CHANGE_NAME`, utilisé par `rzclientreconnect` comme
   « renommage d'une unité », `ConnectionToServer.cpp:721-728`) ? La table de noms de
   `SFrame.exe` ne tranche pas et il est **prouvé** qu'elle est incomplète (404 y manque).
3. **Quel paquet émet le client quand le joueur invoque** : 304 (dont le nom est dans la
   table du client, mais que ses deux références marquent « Seems unused » / sans
   gestionnaire) ou la compétence `TM_CS_SKILL (400)` avec un effet 601 (chemin de NGemity,
   `Skill.cpp:1537-1558`) ? Non tranchable par lecture seule.
4. **Le champ `code` de 301** (`int32_t`, `TS_SC_ADD_SUMMON_INFO.h:13`) : NGemity y met
   `pSummon->GetSummonCode()` (`Messages.cpp:107`) sans qu'on puisse établir si c'est
   `SummonEntity.SummonResourceId`, `SummonResourceEntity.ModelId` ou un id de monstre du
   client. C'est le **seul** champ de 301 que le schéma du dépôt ne tranche pas
   (`CharacterEntity.cs:63-71`, `SummonEntity.cs`, `SummonResourceEntity.cs`).
   Le nom (`SummonEntity.Name`), le niveau (`Lv`) et les points de compétence (`Sp`) sont
   directement disponibles, donc la question reste locale au seul `code`.
5. **Taille de 6001 et 6002** : variable par construction (`count`/`dynarray`), donc non
   chiffrable sans décision d'implémentation.
6. **Plafond de niveau du familier en 7.3** : `SummonLevelResource` (`ArcadiaSchemaPSQL.sql:1193`)
   donne une ligne par niveau, mais le nombre de lignes réel de 7.3 n'a **pas** été établi —
   le format d'enregistrement de `db_summonexp.rdb` (4 212 octets, en-tête ASCII « 20081223 »
   puis 128 octets) n'a pas été décodé dans le temps de cette fiche. Conséquence pratique
   faible : `level` est `int32_t` sans gating dans rzu (301, `:14`).
7. **Ordre et compactage des 6 emplacements** : NGemity accepte des trous puis les compacte
   (`WorldSession.cpp:1006-1016`). Rien dans le protocole ne l'impose ni ne l'interdit.
8. **Entrée du familier dans le monde** : aucun `BuildEnterSummon` ni valeur d'`objType`
   documentée pour une invocation en 7.3 (`GameSpawnPackets.cs` n'a que NPC, monstre, objet,
   prop). Le paquet 303 ne transporte pas de position : la position du familier reste à
   établir.
9. **Famille familier/animal (350-353)** : déclarée dans les deux références, gérée par
   aucune, mais le client a les classes `AUSMSG_ADD_PET_INFO` / `AUSMSG_SHOW_SET_PET_NAME` /
   `AUSIMSG_REQ_SET_PET_NAME`. La sémantique 7.3 de `cage_handle` vs `pet_handle` n'est pas
   établie.

## A VERIFIER PAR KILLIAN

- **L'étape 2 du §8** : autoriser l'émission de `TM_SC_ADD_SUMMON_INFO (301)` au login pour
  les invocations déjà enregistrées, ce qui élargit le bootstrap de personnage
  (`CLAUDE.md:118`) et change ce que le client voit à l'entrée en jeu. L'étape 1 est
  implémentée et testée (§9) mais **sans appelant** tant que cette question n'est pas
  tranchée : le lot est du code mort en l'état, assumé comme tel.
- **Le point 2 de l'étape 1** (lecture en base d'un personnage et de ses invocations, §9.4) n'est
  pas implémenté. Sa seule consommatrice est l'étape 2, et `SummonSlotItemIds` n'a pas de
  sémantique tranchée. À décider : implémenter la requête dès maintenant, ou attendre l'étape 2.
- **`code` de 301 et `summon_handle`** : les deux seuls paramètres que les constructeurs
  laissent à l'appelant (§9.3). Le premier attend la réponse à la question ouverte 4, le second
  suppose une entrée de l'invocation dans le monde (`NON ÉTABLI` 8).
- **La nature de `SummonSlotItemIds`** (« item id or item resource id », `CharacterEntity.cs:63`) :
  le champ n'est alimenté nulle part aujourd'hui, donc `BuildEquipSummon` envoie six zéros et
  `MainSummonId` / `SubSummonId` ne sont jamais renseignés. Quelle est la source de vérité
  d'une invocation possédée ?
- **Politique de liaison carte → invocation** : création de `SummonEntity` sur 303 comme
  NGemity (`WorldSession.cpp:990-992`), ou à l'usage de la carte ; et à quelle fréquence le
  `summon_handle` de 301 doit changer.
- **Question ouverte 4** : quel id mettre dans le `code` de 301.
- **Le renommage** : accepter d'implémenter 323 sans preuve du déclencheur client, ou suivre
  NGemity et ne rien faire pour l'instant.
- **La réponse à 452** : hypothèse de travail = `TM_SC_SKILL_LIST (403)` avec le handle du
  familier comme `target` (mécanisme de `Messages::SendSkillList(Player*, Unit*, int32)`,
  `Messages.cpp:172-190`, déjà employé pour le familier en 301). Aucune référence ne le
  prouve ; à confirmer.
- **Confirmation métier** que 306 (`unsummon_duration`) est bien le mécanisme de délai de
  renvoi voulu pour 7.3, puisque NGemity ne l'émet jamais.

---

## 8bis. Commits épinglés

| Référence | Commit | Empreinte du poste |
|---|---|---|
| `rzu` (glandu2/rzu) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | `reference/commits.json` |
| `ngemity` (NGemity/RZEmulator) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | `reference/commits.json` |
| Navislamia (base de la branche) | `b402e9c` (« Update md ») | `master` local = `origin/master` |
| Navislamia (code du socle §9) | `5cfdd64` (« Add the Epic 7.3 summon socle packet builders ») | branche `hermes/packet-socle-invocations` |
| Client 7.3 `SFrame.exe` | non versionné | `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` |
| Client 7.3 `db_string.rdb` | non versionné | `sha256 4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` |
| Client 7.3 `db_summonexp.rdb` | non versionné | `sha256 fe2c0f58afb967fdd941b274d7c716543a7548b82dd4106a6ebc66963be0e07b` |
| Extraction client | `archive_entries: 83822`, `source_index_sha256 b88ac39a0976f49746671d413ba68af616af37e6e26095c3a109d5884510adbf` | `reference/client73/extraction-manifest.json` |
