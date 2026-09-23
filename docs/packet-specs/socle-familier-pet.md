# Socle familier (pet) — apparition dans le monde, retrait et filtre

Fiche d'archéologie du socle `familier`. Elle porte tout ce qui est établi sur la famille des
trames `350`-`355` au 23 septembre 2026, tranche pour **Epic 7.3**, et fixe le lot minimal que la
branche `hermes/packet-socle-familier-pet` implémente. Elle ne modifie aucun code serveur.

Références épinglées (§13) : `rzu` `87c1e83`, `ngemity` `38ceb2c`, client `reference/client73/SFrame.exe`
`sha256 41e0af2e…b9500e`. Base de la branche : `master` `4be98d4`.

---

## 1. Objet et statut

Le familier (pet) est le compagnon lié au **personnage** (un seul à la fois :
`CharacterEntity.PetId`, `Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:73-74`), rangé dans
une **cage** (`PetEntity.ItemId`, `Game/DataAccess/Entities/Telecaster/PetEntity.cs:12-13`, relation
`TelecasterContext.cs:187-190`, `ItemEntity.cs:42`). C'est un objet **du monde** : le client 7.3 le
reçoit par `TS_SC_ENTER` avec `objType = 7` (§5) et le retire par la trame `350` (§4.1, §4.6).

État mesuré du dépôt à `4be98d4` :

| Mesure | Commande | Résultat |
|---|---|---|
| Membres `350`-`355` déclarés | `grep -n "= 35[0-9]" Game/Network/Packets/Enums/GamePackets.cs` | **aucun** (la famille `3xx` s'arrête à `301,302,303,305,306,307,320,321`, lignes 70-77) |
| Code serveur parlant du familier | `grep -rniE '\bpet\b' Game/Network/` | **0** (seul le modèle `PetEntity` existe) |
| Porteur du filtre | `grep -rn "filter" --include=*.cs Game/Services Game/Network/Packets` | **1** occurrence, hors sujet : commentaire de `Stats/IStateCatalog.cs:15` |
| Ramassage d'objets | `Game/Services/GroundItemService.cs:22` (`PickupRange = 300f`), `:174`, `:252-256` | **joueur seul**, aucune notion de familier ramasseur |
| Table de ressource | `ArcadiaSchemaPSQL.sql:882` | `PetResource` existe (id, type, name_id, `cage_id`, rate, size, scale, …, model, motion_file_id, texture_group, local_flag) |

Deux repères de la carte parente mesurés faux, corrigés ici : le `grep filter` rend **1** résultat (et
non 0), et les lignes utiles de `GroundItemService.cs` sont `:22`, `:174`, `:252-256` (`:165` et
`:243` sont des accolades). Le repère `socle-invocations.md:455` est également faux : la liste des
paquets « hors lot » est à **`:428`**.

---

## 2. Identité des trames

| id | nom `TM_*` | sens | source du nom | structure |
|---|---|---|---|---|
| 350 | `TM_SC_UNSUMMON_PET` | S→C | `op_codes.md:117` | `reference/rzu/librzu/src/packets/GameClient/TS_SC_UNSUMMON_PET.h:7-12` |
| 351 | `TM_SC_ADD_PET_INFO` | S→C | `op_codes.md:118` | `…/TS_SC_ADD_PET_INFO.h:7-17` |
| 352 | `TM_SC_REMOVE_PET_INFO` | S→C | `op_codes.md:119` | `…/TS_SC_REMOVE_PET_INFO.h:5-10` |
| 353 | `TM_SC_SHOW_SET_PET_NAME` | S→C | `op_codes.md:120` | `…/TS_SC_SHOW_SET_PET_NAME.h:5-10` |
| 354 | `TM_CS_SET_PET_NAME` | C→S | `op_codes.md:121` | `…/TS_CS_SET_PET_NAME.h:5-13` |
| 355 | `TM_CS_SET_PET_FILTER` | C→S | `op_codes.md:122` | **aucune référence dépôt** — cadre établi au §4.5 par lecture du client |

Le sens vient de `CREATE_PACKET_VER_ID(…, SessionPacketOrigin::Server|Client)` de chaque en-tête rzu
(`TS_SC_*.h:14/19/12/12` et `TS_CS_SET_PET_NAME.h:15`). `op_codes.md:122` est la seule source dépôt
du **355** (`grep -rn "X(355," reference/rzu/librzu/src/packets/` → **0**).

Classes de message internes du client, nommées par RTTI MSVC (descripteur de type atteint par le
`CompleteObjectLocator` en `vtable-4`, nom à `descripteur+8`) :

| trame | `vtable` du message | classe RTTI | type interne |
|---|---|---|---|
| 350 | `0x00a520e0` | `AUSMSG_UNSUMMON_PET` (nom en `0x00c1e18c`) | `0x7a` (122) |
| 351 | `0x00a520e8` | `AUSMSG_ADD_PET_INFO` (`0x00c1e1ac`) | `0x7b` (123) |
| 352 | `0x00a520f0` | `AUSMSG_REMOVE_PET_INFO` (`0x00c1e1cc`) | `0x7c` (124) |
| 353 | `0x00a520f8` | `AUSMSG_SHOW_SET_PET_NAME` (`0x00c1e1f0`) | `0x7d` (125) |
| 355 | — | `AUSIMSG_REQ_SET_PET_FILTER` (`0x00c1788a`) | `0x2db5` (11701) |

---

## 3. Ce que le joueur fait pour que le client les envoie

Seules `354` et `355` vont du client vers le serveur ; **aucune trame `350`-`353` n'est émise par le
client** (§6.2). Le déclencheur du `355` est établi, celui du `354` est traité par sa propre branche
(§9.3).

**355 — filtre de ramassage.** Le client garde une option `PET_PICKUP_FILTER`
(`reference/client73/SFrame.exe` @`0xa4e480`, chaîne `.rdata` voisine des autres options de jeu :
`PLAY_WEATHER_QUALITY` `0xa4e468`, `PLAY_CRITICAL_CAMERA` `0xa4e494`), construite par la fenêtre
d'options (`push 0xa4e480` @`0x650472` et @`0x651f16`) et stockée dans l'objet de configuration à
`+0x288`. Le message `AUSIMSG_REQ_SET_PET_FILTER` (type `11701`) est sérialisé en trame `355` par
l'expéditeur de messages sortants : `cmp eax,0x2db5` puis `call 0x48e1f0` (@`0x49e750`-`0x49e75a`).

Ce que le filtre *fait* est cohérent avec le familier ramasseur décrit par le client :
`db_string.rdb` — « This pet will collect loot for you in a 15 meter radius. » (`db_string.txt:5255`,
`5257`, `5267`, `5269`, `5271`, `5273`, `5367`) et « Pickup Range: #@pickup_range@# meters »
(`db_string.txt:2030`). Le pendant serveur du ramassage par familier **n'existe pas** (§1).

---

## 4. Structure sur le fil

En-tête commun du dépôt : `Length` u32 @0, `ID` u16 @4, `checksum` u8 @6, charge utile à partir de
`+7` (`GameSpawnPackets.cs:10`, `WriteHeader`/`WriteChecksum`). Toutes les trames `350`-`353` sont
**statiques** : leur taille ne dépend d'aucun compteur.

### 4.1 `350 TM_SC_UNSUMMON_PET` — 11 octets

| offset | type | nom | source |
|---|---|---|---|
| 0 | u32 | `Length` = 11 | en-tête dépôt (`GameSpawnPackets.cs:10`) |
| 4 | u16 | `ID` = 350 | `op_codes.md:117` |
| 6 | u8 | `checksum` | en-tête dépôt |
| 7 | u32 | `handle` | `TS_SC_UNSUMMON_PET.h:8` |

**Preuve client** : répartiteur entrant @`0x67de80` (2ᵉ étage @`0x67e1d9` : `sub eax,0xff`, table
d'octets `0x67f218`, table de sauts `0x67f19c`) ; id `350` (indice 95) → aiguillage `0x67e28a` →
`call 0x66f5a0`. Le corps @`0x66f5a0` alloue `0x17` (23) octets, écrit `[+4] = 0x7a`, `[+0] =
0x00a520e0`, puis **lit un seul dword, `[paquet+7]`**, vers `[+0x13]` (`0x66f5db`-`0x66f5e1`). Aucune
autre lecture : la charge utile fait 4 octets.

### 4.2 `351 TM_SC_ADD_PET_INFO` — **42 octets** (rzu et NGemity en déclarent 38)

| offset | type | nom | source |
|---|---|---|---|
| 0 | u32 | `Length` = **42** | §4.2.2 (client) — rzu/NGemity disent 38 |
| 4 | u16 | `ID` = 351 | `op_codes.md:118` |
| 6 | u8 | `checksum` | en-tête dépôt |
| 7 | u32 | `cage_handle` | `TS_SC_ADD_PET_INFO.h:8` |
| 11 | u32 | `pet_handle` | `TS_SC_ADD_PET_INFO.h:9` |
| 15 | 19 o | `name` (18 caractères utiles + terminateur) | `TS_SC_ADD_PET_INFO.h:10-12`, gating `>= EPIC_9_6` (§6.1) |
| 34 | i32 | `code` | `TS_SC_ADD_PET_INFO.h:13` |
| 38 | i32 | **5ᵉ champ, sans nom dans les deux références** | client seul, §4.2.2 |

#### 4.2.1 Ce que disent les deux références serveur

`TS_SC_ADD_PET_INFO.h:7-13` déclare `cage_handle`, `pet_handle`, `name 20` (19 pour `< EPIC_9_6`),
`code int32` → 31 octets de charge utile, **38 au total**. NGemity est identique
(`shared/Server/Packets/GameClient/TS_SC_ADD_PET_INFO.h:6-12`, `_(string)(name, 19)`).
`git log -p --follow` sur l'en-tête rzu montre qu'**aucun champ n'a jamais été retiré** : le champ
manquant n'a jamais été déclaré.

#### 4.2.2 Preuve client : 35 octets de charge utile

Le corps du répartiteur pour `351` est @`0x66f600` (id `351`, indice 96 → aiguillage `0x67e297` →
`call 0x66f600`). Il alloue `0x36` (54) octets, écrit `[+4] = 0x7b` et `[+0] = 0x00a520e8`
(`AUSMSG_ADD_PET_INFO`), puis **recopie depuis le paquet** :

| lecture paquet | destination | rôle |
|---|---|---|
| `+0x07` | `+0x17` | `cage_handle` |
| `+0x0b` | `+0x13` | `pet_handle` |
| `+0x0f`, `+0x13`, `+0x17`, `+0x1b` (4×dword), `+0x1f` (word), `+0x21` (byte) | `+0x1f`..`+0x31` | `name`, 19 octets |
| `+0x22` | `+0x1b` | `code` |
| `+0x26` | `+0x32` | **5ᵉ dword** |

(instructions @`0x66f651`-`0x66f692`). La fenêtre lue va de `+7` à `+0x29` inclus, soit **35 octets de
charge utile → 42 octets au total** ; l'objet de message fait `54 = 0x36` octets et son dernier champ
`+0x32` est **explicitement** rempli puis remis à zéro à la construction (`0x66f634`-`0x66f64a`) :
ce n'est pas un dépassement de tampon.

**Calibrage de la méthode** (obligatoire avant de contredire deux références) : la trame sœur
`301 TS_SC_ADD_SUMMON_INFO`, dont rzu déclare 39 octets de charge utile
(`TS_SC_ADD_SUMMON_INFO.h:7-15` : `card_handle`, `summon_handle`, `name 19`, `code`, `level`, `sp`),
est lue par le client @`0x66f020` (id `301` → aiguillage `0x67e208` → `call 0x66f020`) sur exactement
`+7` … `+0x2d`, soit **39 octets de charge utile** (les trois derniers dwords en `+0x22`, `+0x26`,
`+0x2a`). Le client lit donc exactement la longueur déclarée par rzu quand elle est juste. Sur `351`,
il lit 4 octets de plus : **c'est la trame qui porte un champ de plus, pas le client qui déborde**.

Le nom `code` du 4ᵉ i32 et l'usage du 5ᵉ sont **NON ÉTABLI** (§11.2). Les deux références sont
symétriques sur le reste (`cage_handle`/`card_handle`), ce qui suggère une trame `ADD_PET_INFO`
construite comme `ADD_SUMMON_INFO` privée de `sp`, mais aucune source ne le nomme.

### 4.3 `352 TM_SC_REMOVE_PET_INFO` — 11 octets

Idem `350` : `handle` u32 @7 (`TS_SC_REMOVE_PET_INFO.h:6`). **Preuve client** : aiguillage `0x67e2a4`
→ `call 0x66f6b0` alloue `0x17`, écrit `[+4] = 0x7c`, `[+0] = 0x00a520f0`, lit `[paquet+7]` vers
`[+0x13]` (`0x66f6eb`-`0x66f6f1`) → charge utile de 4 octets.

### 4.4 `353 TM_SC_SHOW_SET_PET_NAME` — 11 octets

`handle` u32 @7 (`TS_SC_SHOW_SET_PET_NAME.h:6`). **Preuve client** : aiguillage `0x67e2b1` →
`call 0x66f710`, alloue `0x17`, écrit `[+4] = 0x7d`, `[+0] = 0x00a520f8`, lit `[paquet+7]`
(`0x66f74b`-`0x66f751`). Cette trame a sa propre carte et sa propre branche (§9.3) ; elle est décrite
ici pour la complétude de la famille.

### 4.5 `355 TM_CS_SET_PET_FILTER` — 15 octets (cadre établi par le client)

Le constructeur de la trame est @`0x48e1f0` :

| offset | type | nom | preuve |
|---|---|---|---|
| 0 | u32 | `Length` = `0xf` (15) | `mov DWORD PTR [ebp-0x10],0xf` @`0x48e211`, tampon en `[ebp-0x10]` |
| 4 | u16 | `ID` = `0x163` (355) | `mov eax,0x163` @`0x48e208` puis `mov WORD PTR [ebp-0xc],ax` |
| 6 | u8 | checksum (somme des 6 octets d'en-tête) | boucle `add dl,[eax]; inc eax` @`0x48e220`-`0x48e22d` |
| 7 | u32 | `handle` | `mov edx,[eax+0x13]` @`0x48e230` → `[ebp-0x9]` |
| 11 | u32 | valeur du filtre | `mov eax,[eax+0x17]` @`0x48e233` → `[ebp-0x5]` |

La chaîne est complète : `AUSIMSG_REQ_SET_PET_FILTER` (type `11701`) → `0x49e750` → `0x48e1f0`.
Les deux dwords proviennent du message interne aux offsets `+0x13` et `+0x17`.

**Décision** : le **cadre** (opcode, en-tête, 15 octets, deux dwords à `+7` et `+11`) est établi ; la
**sémantique du 2ᵉ dword** (booléen, index, bitmask ; 5 valeurs ? l'option d'interface a une valeur par
défaut `0x1f` @`0x65048e`, non rattachée avec certitude à cette option) reste **NON ÉTABLI** (§11.4).
La réserve « la disposition et la largeur de 355 ne sont pas établies » de la carte parente est donc
partiellement levée : elle ne vaut plus que pour la sémantique.

### 4.6 Ce que le client fait de chaque trame reçue

| trame | message interne | répartiteur | effet lu |
|---|---|---|---|
| 350 | 122 `AUSMSG_UNSUMMON_PET` | dispatcher **monde** @`0x47fb27` (`sub eax,4`, table d'octets `0x4801dc`, table de sauts `0x480120`), cas @`0x47fcc3` | retrouve l'acteur par `[msg+0x13]` (`call 0x475d50`), vérifie qu'il rend bien `7` (`call 0x6b2a40` puis `cmp al,0x7`), marque `[acteur+0x5e8] = 1`, puis le retire via `[edi+0x118]` → `vtable+0x1b4(handle)` ; journalise `case MSG_UNSUMMON_PET` (`push 0xa1d4c0`) |
| 351 | 123 `AUSMSG_ADD_PET_INFO` | `SGameInterface` @`0x639500` (`sub eax,2`, tables `0x640f00`/`0x640dec`), cas @`0x63c430` | ouvre/rafraîchit une fenêtre d'interface (`push 0x8a` @`0x63c431`, puis action `0x35` @`0x63c464`) |
| 352 | 124 `AUSMSG_REMOVE_PET_INFO` | **même cas** @`0x63c430` que 123 | idem ; les deux trames partagent un seul gestionnaire |
| 353 | 125 `AUSMSG_SHOW_SET_PET_NAME` | `SGameInterface`, cas @`0x63c472` | ouvre la boîte de saisie du nom (`call 0x631340`, `call 0x6490e0`) |
| 354 | 1170 `AUSIMSG_REQ_SET_PET_NAME` | **non traité** (indice 99 → défaut `0x67ef21`) | — |
| 355 | 11701 `AUSIMSG_REQ_SET_PET_FILTER` | **non traité** (indice 100 → défaut `0x67ef21`) | — |

Le fait que `350` soit traité par le répartiteur des messages **du monde** (celui des `MSG_ENTER`,
`MSG_LEAVE`, `MSG_MOVE`, `MSG_ATTACK`, `MSG_LOGIN`, `MSG_REGION_ACK` — chaînes voisines en
`0xa1d4bc`-`0xa1d670`) et qu'il contrôle l'`objType 7` de la cible avant d'agir est la meilleure
preuve disponible que **`350` retire un familier présent dans le monde**.

---

## 5. Apparition du familier dans le monde — `TS_SC_ENTER` (3), `objType = 7`

C'est la seule voie d'apparition : le client 7.3 connaît `EOT_Pet = 7`
(`reference/rzu/…/TS_SC_ENTER.h:12-21`, identique dans NGemity
`shared/Server/Packets/GameClient/TS_SC_ENTER.h:17`) et **exécute** le cas `7` de son gestionnaire
d'entrée.

**Preuve client** : id `3` → aiguillage `0x67dfa3` → `call 0x66e650`. Le préfixe est lu à
`[+7]` (type), `[+8]` (handle), `[+0x0c]` (x), `[+0x10]` (y), `[+0x14]` (z), `[+0x18]` (layer),
`[+0x19]` (`objType`) — instructions @`0x66e688`-`0x66e6bb` — exactement l'ordre rzu 7.3 (`layer`
puis `objType` **après** `z`, les `impl` `>= EPIC_9_6_7` n'existant pas en 7.3, `TS_SC_ENTER.h:145-157`).
Le cas `objType == 7` est @`0x66e876` : `push 0x6b` (**107**) puis `call 0x9767b1` (allocation),
recopie du bloc de base de 38 octets (`rep movs` 9 dwords + `movs WORD`), puis de la charge utile
depuis `[paquet+0x1a]` : `mov ecx,0x11` → `rep movs` 68 octets + `movs BYTE` 1 → **69 octets**
(`0x66e87a`-`0x66e8a4`). `107 = 38 + 69` ferme la structure.

`TS_SC_ENTER__PET_INFO` en 7.3 (`TS_SC_ENTER.h:132-143`) vaut donc
`creatureInfo(38) + master_handle(4) + pet_code(8) + name(19) = 69` — le champ `enhance` du frère
`SUMMON_INFO` (`TS_SC_ENTER.h:97`) **n'existe pas** pour le familier.

### 5.1 Trame complète — **95 octets**

| offset | type | champ | source |
|---|---|---|---|
| 0 | u32 | `Length` = **95** | `HeaderSize + 1 + 4 + 12 + 1 + 1 + 38 + 4 + 8 + 19` |
| 4 | u16 | `ID` = 3 | `GamePackets.TM_SC_ENTER` |
| 6 | u8 | `checksum` | en-tête dépôt |
| 7 | u8 | `type` = `ET_NPC` (1) | convention du dépôt (`GameSpawnPackets.cs:21`, `TS_SC_ENTER.h:23-27`) |
| 8 | u32 | `handle` | `TS_SC_ENTER.h:152` |
| 12/16/20 | f32 | `x`, `y`, `z` | `TS_SC_ENTER.h:153-155` |
| 24 | u8 | `layer` | `TS_SC_ENTER.h:156` |
| 25 | u8 | `objType` = **7** (`EOT_Pet`) | `TS_SC_ENTER.h:20`, `:157` |
| 26 | u32 | `creatureInfo.status` | `TS_SC_ENTER.h:68` (`TS_CREATURE_STATUS`, 4 o) |
| 30 | f32 | `creatureInfo.face_direction` | `:69` |
| 34/38 | i32 | `hp`, `max_hp` | `:70-71` |
| 42/46 | i32 | `mp`, `max_mp` | `:72-73` |
| 50 | i32 | `creatureInfo.level` | `:74` |
| 54 | u8 | `creatureInfo.race` (`< EPIC_9_6_7`) | `:75` |
| 55 | u32 | `creatureInfo.skin_color` (`>= EPIC_4_1 && < EPIC_9_6_7`) | `:76` |
| 59 | u8 | `creatureInfo.is_first_enter` (`< EPIC_9_6_7`) | `:77` |
| 60 | i32 | `creatureInfo.energy` (`>= EPIC_4_1 && < EPIC_9_6_7`) | `:78` |
| 64 | u32 | `master_handle` | `:137` |
| 68 | 8 o | `pet_code` (`EncodedInt<EncodingRandomized>`) | `:138` |
| 76 | 19 o | `name` (18 utiles + terminateur) | `:139-141` |

Total `76 + 19 = 95`. Les offsets `64`, `68`, `76` sont **identiques** à ceux du frère `summon`
(`GameSpawnPackets.cs:84-95` : `masterHandle` @64, `summon_code` @68, `name` @76) — la seule
différence est l'absence de l'octet `enhance` @95.

---

## 6. Gating de version, tranché pour 7.3

### 6.1 Champs

| champ | gating rzu | décision 7.3 |
|---|---|---|
| id `350`/`351`/`352`/`353`/`354` | `X(350…) / X(1350…)` (`<`/`>= EPIC_9_6_3`) | **350-354** ; les `1350`-`1354` ne sont pas déclarés |
| `name` de `351` et `354` | `19` si `< EPIC_9_6`, `20` sinon | **19 octets** (18 utiles) |
| `handle` de `350`/`352`/`353` | pas de variante | u32 à `+7` |
| `TS_SC_ENTER` id | `3` (`< EPIC_9_6_3`) / `1003` | **3** |
| `TS_SC_ENTER` `objType` | `def` en position 2 + `impl >= EPIC_9_6_7` | en 7.3 l'`impl` n'existe pas : `objType` est écrit **une seule fois, après `z`** (`+25`) |
| `layer` | `def` + `impl >= EPIC_9_6_7` | idem : une seule fois, `+24` |
| `TS_SC_ENTER__PET_INFO` | `creatureInfo` si `< EPIC_9_6_7`, sinon `status`+`face_direction`+`level` | **`creatureInfo` (38 o)** |
| `race`, `skin_color`, `is_first_enter`, `energy` | bornes hautes `< EPIC_9_6_7` | **présents** (bloc de 38 o) |
| `enhance` du frère `SUMMON_INFO` | `>= EPIC_7_1` (donc présent pour le summon en 7.3) | **absent** de `PET_INFO` : ne pas le copier du summon |
| id `355` | absent des deux références | 355 (charge utile `handle` + 4 o, §4.5) |

### 6.2 Sens et conséquence sur le répartiteur

`350`, `351`, `352` sont **S→C** : le client ne les émet jamais (vérifié : les tables d'entrée
`0x67f218` et `0x67f0a0` ne les routent que comme entrées). Le lot n'ajoute donc **aucun bras de
répartition** dans `GameClient.cs` : le critère « aucun membre de `GamePackets` n'atteint le `switch`
final » est satisfait *a fortiori*, exactement comme le lot `354` l'a fait pour `353`.

---

## 7. Traitement attendu

### 7.1 Ce que fait NGemity (`38ceb2c`)

**Rien.** Les trames sont déclarées (`shared/Server/ClientPackets.h:129-133` : `TS_SC_UNSUMMON_PET`
350, `TS_SC_ADD_PET_INFO` 351, `TS_SC_REMOVE_PET_INFO` 352, `TS_SC_SHOW_SET_PET_NAME` 353,
`TS_CS_SET_PET_NAME` 354 — l'énumération s'arrête là avant `TS_CS_SKILL = 400` au `:134`) et
structurées, mais **aucun gestionnaire** : `grep -rniE '\bpet\b' Chihiro/src` ne rend que trois
lignes, toutes des commentaires ou des zéros :
`Chihiro/src/Entities/Player/Player.cpp:905` (`stmt->setInt32(i++, 0); // Pet`),
`Chihiro/src/Network/GameNetwork/WorldSession.cpp:376` (`// Todo Epic 5: Pet handle)`),
`Chihiro/src/World/World.cpp:433` (`// same for pet`). NGemity déclare pourtant la variante
`EOT_Pet` de l'entrée (`TS_SC_ENTER.h:17`, `:126`, `:132-142`).

### 7.2 Ce que le serveur doit répondre

1. **Apparition** : `TS_SC_ENTER` (3) avec `objType = 7` et le bloc `PET_INFO` du §5.1 → **95 octets**.
   C'est la trame qui crée l'objet client ; aucune autre voie d'apparition n'existe.
2. **Retrait** : `TM_SC_UNSUMMON_PET` (350) sur le handle du familier → **11 octets** (§4.1). Le
   client, en la recevant, retire lui-même l'acteur du monde (§4.6). Le `TS_SC_LEAVE` (9, 11 octets,
   `GameSpawnPackets.cs:188`) qui suit `TM_SC_UNSUMMON` (305) chez l'invocation est repris **par
   symétrie** dans le service (§9.2) ; son besoin réel pour le familier est **NON ÉTABLI** (§11.5).
3. **Informations de cage** : `TM_SC_ADD_PET_INFO` (351) → **42 octets** (§4.2) et
   `TM_SC_REMOVE_PET_INFO` (352) → **11 octets** (§4.3) alimentent la fenêtre d'interface
   (`AUSMSG_ADD_PET_INFO`/`AUSMSG_REMOVE_PET_INFO`, un seul gestionnaire partagé).
4. **Aucune réponse** aux trames entrantes `354`/`355` : ni l'une ni l'autre n'a de paquet de
   résultat déclaré dans les deux références, et le client ne les route pas en sortie de serveur
   (indices 99/100 → défaut `0x67ef21`, §4.6).

---

## 8. Écarts assumés avec NGemity

| point | NGemity | décision du socle | pourquoi |
|---|---|---|---|
| `351` longueur | 38 (`_(string)(name, 19)` + `code`) | **42** | le client 7.3 lit 35 octets de charge utile ; méthode calibrée sur `301` (§4.2.2) |
| `pet_code` | absent de toute logique | `EncodedInt<EncodingRandomized>` 8 o, comme `summon_code`/`npc_id` | `TS_SC_ENTER.h:138` ; l'encodage est le piège documenté de `CLAUDE.md` |
| `enhance` | n'existe pas dans `PET_INFO` | absent | `TS_SC_ENTER.h:132-143` ne le déclare pas, contrairement à `SUMMON_INFO:97` |
| conduite serveur | aucune | §7.2 | NGemity n'a pas de logique familier ; le vide n'est pas une décision |
| `objType` de l'entrée | 7 déclaré, non émis | **7 émis** | le client 7.3 exécute le cas `7` (§5) |

---

## 9. Le découpage du socle (contrat du lot)

### 9.1 Lot 1 — implémenté sur `hermes/packet-socle-familier-pet`

Sous-ensemble minimal qui rend le familier *présent dans le monde, puis absent*, sans décider d'aucune
règle de jeu :

1. **`GamePackets.cs`** — trois membres S→C : `TM_SC_UNSUMMON_PET = 350`,
   `TM_SC_ADD_PET_INFO = 351`, `TM_SC_REMOVE_PET_INFO = 352`. **Aucun bras de répartition** (§6.2).
2. **`GameSpawnPackets.BuildEnterPet(...)`** — `TM_SC_ENTER` (3), `objType = 7`, **95 octets**
   (§5.1) : le préfixe partagé (`BuildEnterCreature`, `GameSpawnPackets.cs:211-232` : `type` @7,
   `handle` @8, `x/y/z` @12/16/20, `layer` @24, `objType` @25, `status` @26, `hp`/`max_hp` @34/38,
   `mp`/`max_mp` @42/46, `level` @50), puis
   `master_handle` @64, `pet_code` @68 (`WriteEncodedInt`), `name` @76 (`WriteName`), **rien** @95.
   Ajouter la constante `ObjectTypePet = 7` à côté de `ObjectTypeSummon` (`GameSpawnPackets.cs:26`).
   Le fichier n'est touché par **aucune** branche ouverte (mesure §10) : c'est l'emplacement à coût nul.
3. **`GamePetPackets`** — nouveau fichier `Game/Network/Packets/Game/GamePetPackets.cs` (fichier neuf,
   donc hors des 16 branches qui se disputent `GamePackets.cs`/`GameClient.cs`) portant les trois
   reconstructeurs : `BuildUnsummonPet(uint handle)` (350, 11 o), `BuildAddPetInfo(uint cageHandle,
   uint petHandle, string name, int code, int unknown)` (351, 42 o) et
   `BuildRemovePetInfo(uint handle)` (352, 11 o). Le 5ᵉ paramètre de `BuildAddPetInfo` est **nommé
   `unknown`** : aucune source ne le nomme (§11.2), il ne doit pas recevoir de constante inventée.
4. **`PetWorldEntry` + `PetWorldService`** — sur le patron validé du socle invocation
   (`Game/Services/SummonWorldEntry.cs`, `Game/Services/SummonWorldService.cs:51`/`:86`) : `Enter`
   envoie **351 puis 3**, `Leave` envoie **350 puis 9**. Le service **ne décide rien** : tout ce qui
   est incertain (position, `z`, `layer`, `hp`/`max_hp`/`mp`/`max_mp`/`level`, `race`,
   `is_first_enter`, `pet_code`) est fourni par l'appelant, comme `SummonWorldEntry`.
5. **Tests** — au minimum un test d'offsets par trame, comme le dépôt le fait déjà :
   `350` (11 o, id @4, handle @7), `351` (**42 o**, cage @7, pet @11, nom @15 sur 19 octets, `code`
   @34, 5ᵉ champ @38), `352` (11 o), entrée familier (**95 o**, id @4, `type` @7, handle @8, x @12,
   y @16, z @20, layer @24, `objType` @25 = 7, `status` @26, `master_handle` @64, `pet_code` @68,
   nom @76, **octet 94 = terminateur, aucun octet 95**), et les deux séquences du service.

**Ce que le lot ne fait pas** : il ne câble aucune règle de jeu (quel événement fait apparaître le
familier, ce que le filtre autorise, ce que le familier ramasse). Aucun appelant de jeu n'est modifié.

### 9.2 Préréquis nommés du lot 1

- `CharacterEntity.PetId` (`:73`) garantit **au plus un familier** par personnage ; le lot ne suppose
  rien d'autre du modèle.
- **Aucune source pour `creatureInfo`** : `PetEntity` (`:3-18`) n'a ni `Level`, ni `Hp`, ni `Mp`, et
  `PetResource` (`ArcadiaSchemaPSQL.sql:882-905`) n'a aucune colonne de statistique (id, type,
  name_id, `cage_id`, rate, size, scale, …, model, motion_file_id, texture_group, local_flag).
  Ces valeurs sont donc **fournies par l'appelant** ; les inventer serait une règle de jeu (§11.3).
- `pet_code` ← `PetEntity.PetResourceId` et `cage_handle` ← le handle de l'objet `PetEntity.ItemId`
  sont des **rapprochements par symétrie** avec `SummonEntity.SummonResourceId`/`CardItemId`
  (§11.1) : le lot les prend en paramètres, sans jointure implicite.

### 9.3 Ce que le lot laisse de côté

- **`355 TM_CS_SET_PET_FILTER`** — reste à la carte `cskTn1nR` (parquée). Le §4.5 suffit pour la
  rouvrir sans refaire l'archéologie : opcode, en-tête, 15 octets, `handle` @7, valeur @11,
  constructeur client `0x48e1f0`, chaîne `AUSIMSG_REQ_SET_PET_FILTER` → `0x49e750`. Seule manque la
  sémantique de la valeur (§11.4) **et** un stockage (`PetEntity` n'a aucune colonne de filtre) : la
  carte doit trancher les deux avant tout travail structurel. Si elle autorise ce travail, il se
  limite au cadre ci-dessus, lus et journalisés, sans réponse serveur (§7.2.4).
- **`353 TM_SC_SHOW_SET_PET_NAME`** — sa propre carte et sa propre branche
  (`socle-invocations.md:428`). Le lot `354` a explicitement refusé de la déclarer
  (`docs/packet-specs/354-set-pet-name.md` §5.3) : ne pas la déclarer ici non plus.
- **`354 TM_CS_SET_PET_NAME`** — **déjà livré** par `hermes/packet-354-set-pet-name`
  (`docs/packet-specs/354-set-pet-name.md`, `Tests/Game/SetPetNamePacketsTests.cs`). Branche de cette
  fiche-ci : `4be98d4`, où ce lot n'est pas encore mergé. **Règle d'intégration** : le second des deux
  lots à atterrir retire son doublon (membre `GamePackets`, bras de `GameClient.cs`, reconstructeur) ;
  ne pas réimplémenter.
- **Invocations `301`-`324`** (`socle-invocations.md`, `socle-invocation-monde.md`) et **élevage
  `6000`-`6008`** : hors socle, chacun a sa carte.

---

## 10. Zone de recouvrement (mesurée)

Sur les **43** branches `hermes/*` présentes dans le clone, **16** modifient à la fois
`GamePackets.cs` et `GameClient.cs` (`hermes/packet-10000-open-item-shop`, `-214`, `-215`, `-221`,
`-223`, `-258`, `-259`, `-281`, `-284`, `-285`, `-304`, `-323`, `-324`, `-354`, `-4003`, `-452`).
Chaque branche y ajoute ses membres : c'est le point de conflit structurel du dépôt.

Pour ce lot, la zone évitable est nette :

| fichier | branches ouvertes qui le touchent |
|---|---|
| `Game/Network/Packets/Game/GameSpawnPackets.cs` | **0** |
| `Tests/Game/SummonWorldTests.cs` | **0** |
| nouveau `GamePetPackets.cs` | **0** (fichier neuf) |
| `Game/Network/Packets/Game/GameSummonPackets.cs` | 1 (`hermes/packet-354-set-pet-name`) |
| `Game/Network/Packets/Enums/GamePackets.cs` | 16 |
| `Game/Network/Clients/GameClient.cs` | 16 |

Seul recouvrement **sémantique** : `hermes/packet-354-set-pet-name` (le `354`, §9.3). Aucune branche
ouverte ne déclare de membre `350`-`353` : le lot n'entre en conflit avec personne sur ses opcodes.

---

## 11. NON ÉTABLI

1. **`pet_code` ↔ `PetEntity.PetResourceId`** — rapprochement par symétrie avec
   `SummonEntity.SummonResourceId` → `summon_code` ; aucune clé étrangère ne le garantit.
   *Question* : `PetResource.id` est-il bien la valeur à encoder ?
2. **Le 5ᵉ dword de `351`** (offset 38) — nom, type et source inconnus ; les deux références
   serveur ne le déclarent pas, le client le recopie sans le nommer. *Question* : quel champ de
   quelle table ? (La symétrie avec `ADD_SUMMON_INFO` — `code`, `level`, `sp` — suggère un niveau,
   rien ne le prouve.)
3. **Les statistiques du familier** (`hp`, `max_hp`, `mp`, `max_mp`, `level`, `race`) — aucune
   colonne dans `PetEntity` ni dans `PetResource`. *Question* : d'où viennent-elles, et faut-il
   étendre le modèle ?
4. **La valeur du filtre `355`** — domaine inconnu (booléen, index, bitmask) ; l'option d'interface
   `PET_PICKUP_FILTER` a une valeur par défaut `0x1f` @`0x65048e` mais le rattachement n'est pas
   certain. *Question* : sur quoi porte le filtre, et comment le stocker ?
5. **`TS_SC_LEAVE` (9) après `350`** — la symétrie avec `305` + `9` (`SummonWorldService.cs:93-94`)
   n'est pas une preuve pour le familier, et le client retire déjà l'acteur sur `350` seul (§4.6).
   *Question* : mesurable en jeu — le familier disparaît-il sans le `9` ? reste-t-il fantôme avec ?
6. **`cage_handle`** — l'hypothèse « handle de l'objet `PetEntity.ItemId` » vient du nom rzu, pas
   d'une lecture du client. *Question* : le client attend-il le handle d'objet ou l'`ItemId` ?
7. **`is_first_enter`, `energy`, `skin_color`, `face_direction`, `z`, `layer` du familier** — champs
   présents en 7.3 mais sans source côté serveur ; fournis par l'appelant, valeurs non tranchées.
8. **Le déclencheur de l'apparition** — aucun événement de jeu n'est établi (ni connexion, ni
   commande de la fenêtre du familier) : le service est appelable, mais rien ne l'appelle ce lot.

---

## 12. Mesures du réveil

| contrôle | commande | résultat |
|---|---|---|
| compilation | `dotnet build Navislamia.sln -c Debug` (`NUGET_PACKAGES=/srv/navislamia/.nuget-cache`) | **code de sortie 0** (22 avertissements, 0 erreur) |
| tests | `dotnet test Tests/Tests.csproj` | **code de sortie 0**, `Passed: 1165, Failed: 0, Skipped: 0` — base du lot, à ne pas faire baisser |
| branche | `git branch --show-current` | `hermes/packet-socle-familier-pet` |
| base | `git log --oneline -1 master` | `4be98d4 Merge branch 'feature/rates'` |
| `master` locale | `git log --oneline origin/master..master` | vide (base de branchement) ; `origin/master` a avancé à `c342ada` (merge de `304`), non mergé dans cette branche |

Le clone était propre sur `master` au réveil ; la branche a été créée depuis `master` `4be98d4`.

---

## 13. Commits épinglés

| référence | commit | date |
|---|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | 2023-10-02 — `packets: fix TS_SC_INVENTORY with older epics` |
| `reference/ngemity` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | — |
| `reference/client73/SFrame.exe` | `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | fichier local, jamais exécuté (PE `pei-i386`, base d'image `0x400000`) |

---

## A VERIFIER PAR KILLIAN

1. **`351` fait 42 octets, pas 38** — rzu et NGemity déclarent 38 ; le client 7.3 lit un 5ᵉ dword
   (fenêtre `paquet+7` … `paquet+0x29`). Le lot écrit 42. Si un serveur de référence émet 38 en jeu,
   le contrôle « le familier s'affiche dans la fenêtre » le dira : **à mesurer en jeu**.
2. **La valeur du filtre `355`** (§11.4) — seul point bloquant de la carte `cskTn1nR`, avec le choix
   de stockage (aucune colonne `PetEntity`).
3. **Le besoin réel du `TS_SC_LEAVE` après `350`** (§11.5).
4. **Les statistiques du familier** (§11.3) : le lot les fait fournir par l'appelant ; confirmer
   qu'aucune table serveur n'est censée les porter.
5. **`cage_handle`** (§11.6) : handle d'objet ou `ItemId` ?

---

## Bloc destiné à CLAUDE.md

```markdown
## Familier (pet) — 350-353, entrée dans le monde, filtre 355

Fiche de référence : `docs/packet-specs/socle-familier-pet.md`.

- `TS_SC_ADD_PET_INFO` (351) fait **42 octets en 7.3**, alors que rzu et NGemity en déclarent 38 :
  le client lit un 5ᵉ dword après le nom (`SFrame.exe` @`0x66f600`, fenêtre `paquet+7`…`+0x29`).
  Méthode calibrée sur `301`, dont la fenêtre lue vaut exactement les 39 octets déclarés par rzu.
  Le 5ᵉ champ n'est nommé par aucune source.
- Le familier entre dans le monde par `TS_SC_ENTER` (3) avec `objType = 7` (`EOT_Pet`) :
  **95 octets**, `master_handle` @64, `pet_code` @68 (8 octets `EncodedInt<EncodingRandomized>`),
  `name` @76 sur 19 octets. Le `enhance` du frère `SUMMON_INFO` (`>= EPIC_7_1`) **n'existe pas** ici.
- Il en sort par `TM_SC_UNSUMMON_PET` (350) : 11 octets, `handle` @7. Le client retire lui-même
  l'acteur du monde en recevant cette trame.
- `350`, `351`, `352` sont S→C : aucun bras de répartition n'est nécessaire dans `GameClient.cs`.
- `355 TM_CS_SET_PET_FILTER` : cadre de 15 octets établi par le client (`handle` @7, valeur @11,
  `SFrame.exe` @`0x48e1f0`), sémantique de la valeur NON ÉTABLIE ; porte
  `AUSIMSG_REQ_SET_PET_FILTER` (type 11701) et l'option `PET_PICKUP_FILTER`.
- `GameSpawnPackets.cs` et un nouveau `GamePetPackets.cs` sont hors des 16 branches ouvertes qui se
  disputent `GamePackets.cs`/`GameClient.cs`.
```
