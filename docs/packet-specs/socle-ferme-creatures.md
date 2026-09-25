# Socle ferme de créatures (farm) — trames `6000`-`6008`

Fiche d'archéologie du socle `ferme de créatures`. Elle porte tout ce qui est établi sur la famille
`6000`-`6008` au 25 septembre 2026, tranche pour **Epic 7.3**, et fixe le lot minimal que la branche
`hermes/packet-socle-ferme-creatures` implémente. Elle ne modifie aucun code serveur.

Références épinglées (§8) : `rzu` `87c1e83`, `ngemity` `38ceb2c`, client `reference/client73/SFrame.exe`
`sha256 41e0af2e…b9500e`. Base de la branche : `master` `b56967a`.

---

## 1. Objet et statut

La **ferme de créatures** est l'écran où le joueur dépose une carte d'invocation pour la faire
grandir hors du monde (boutons `assign`, `regain`, `ministration_01..03`, `tiket_buy_01` du client,
§2). C'est une famille **entièrement absente** du serveur : mesuré sur `master` `b56967a` :

| Mesure | Commande | Résultat |
|---|---|---|
| Membres `6000`-`6008` déclarés | `grep -cE '\b600[0-8]\b' Game/Network/Packets/Enums/GamePackets.cs` | **0** |
| Code serveur parlant de la ferme | `grep -rniE 'foster\|nurse\|creaturefarm' --include=*.cs Game/ \| wc -l` | **2**, et hors sujet : `Game/DataAccess/Entities/Enums/ItemFlag.cs:14` et `ItemStatus.cs:16` (§5.4) |
| Entité EF `CreatureFarm*` | `grep -rn "CreatureFarm\|CreatureEnhance" --include=*.cs .` | **0** |
| Table de ressource | `ArcadiaSchemaPSQL.sql:46-52` | `CreatureFarmResource` existe (`rate`, `form`, `enhance_level`, `ticket_count`) — table de **référence**, jamais chargée par le code |
| État de la ferme côté serveur | `Game/DataAccess/Entities/Telecaster/SummonEntity.cs` (lu en entier) | **aucun** champ de ferme : `SummonResourceId`, `CardItemId`, `Exp`, `Jp`, `Name`, `Lv`, `Jlv`, `MaxLevel`, `Fp`, `Sp`, `Hp`, `Mp` |
| Motif d'objet de 75 octets | `Game/Network/Packets/Game/ItemFixedInfoWriter.cs:71` | `Size = 75`, écrit par les encheres (1301/1303/1305) et l'inventaire — **réutilisable tel quel** (§3.7) |

Conséquence directe sur le lot : le serveur **ne peut pas** peupler `TM_SC_FARM_INFO` (6001), faute de
stockage de ferme. Le lot est donc un lot « lecture et bornage » plus la seule réponse que le serveur
puisse produire honnêtement : un `6001` vide (§5.3).

---

## 2. Identité des trames

| id | nom `TM_*` | sens | source du nom | structure rzu |
|---|---|---|---|---|
| 6000 | `TM_CS_REQUEST_FARM_INFO` | C→S | `op_codes.md:256` | `…/TS_CS_REQUEST_FARM_INFO.h:11-13` |
| 6001 | `TM_SC_FARM_INFO` | S→C | `op_codes.md:257` | `…/TS_SC_FARM_INFO.h:25-33` |
| 6002 | `TM_CS_FOSTER_CREATURE` | C→S | `op_codes.md:258` | `…/TS_CS_FOSTER_CREATURE.h:21-32` |
| 6003 | `TM_SC_RESULT_FOSTER` | S→C | `op_codes.md:259` | `…/TS_SC_RESULT_FOSTER.h:6-14` |
| 6004 | `TM_CS_RETRIEVE_CREATURE` | C→S | `op_codes.md:260` | `…/TS_CS_RETRIEVE_CREATURE.h:6-12` |
| 6005 | `TM_SC_RESULT_RETRIEVE` | S→C | `op_codes.md:261` | `…/TS_SC_RESULT_RETRIEVE.h:6-12` |
| 6006 | `TM_CS_NURSE_CREATURE` | C→S | `op_codes.md:262` | `…/TS_CS_NURSE_CREATURE.h:8-14` |
| 6007 | `TM_SC_RESULT_NURSE` | S→C | `op_codes.md:263` | `…/TS_SC_RESULT_NURSE.h:6-14` |
| 6008 | `TM_CS_REQUEST_FARM_MARKET` | C→S | `op_codes.md:264` | `…/TS_CS_REQUEST_FARM_MARKET.h:11-13` |

(`op_codes.md` porte les neuf ids côte à côte, lignes `256-264`, ce qui est la source dépôt du nom de
chacun ; les fichiers `reference/rzu/librzu/src/packets/GameClient/TS_{CS,SC}_*.h` portent la structure.)

Le **sens** ne vient pas seulement de `CREATE_PACKET_VER_ID(…, SessionPacketOrigin::Server|Client)`
(`TS_SC_FARM_INFO.h:33`, `TS_CS_FOSTER_CREATURE.h:32`, etc.) : le client 7.3 le **prouve** par son
répartiteur entrant. En `0x67e828` il soustrait `0x1771` (6001) et accepte `0..6` avant de sauter par
la table `0x67f68c` (`SFrame.exe`) :

| id | index table | cible | suite |
|---|---|---|---|
| 6001 | 0 | `0x67e83d` → `0x672150` | traité |
| 6002 | 1 | `0x67ef21` | **non traité** (« unhandled message », voir ci-dessous) |
| 6003 | 2 | `0x67e84a` → `0x672220` | traité |
| 6004 | 3 | `0x67ef21` | **non traité** |
| 6005 | 4 | `0x67e864` → `0x6722e0` | traité |
| 6006 | 5 | `0x67ef21` | **non traité** |
| 6007 | 6 | `0x67e857` → `0x672280` | traité |

`6000` et `6008`, hors de la plage `0x1771..0x1777`, tombent sur le même défaut `0x67ef21`. Cette
cible est le journal des messages non traités : `0x67ef21` pousse le format `0xa53df0`
(`"처리되지 않은 메세지 : %d\n"`, « message non traité : %d ») puis `0x824240`, avant de rendre le
tampon au pool en `0x67ef39`. Autrement dit : **le client 7.3 traite exactement `6001`, `6003`,
`6005`, `6007` en entrant et journalise-sans-traiter `6000`, `6002`, `6004`, `6006`, `6008`** —
confirmation indépendante du sens, et de l'interdiction d'envoyer une trame `TM_CS_*` au client.

### 2.1 Ce que le joueur fait pour que le client les envoie

Pris dans les constructeurs de trame du client (chaque bouton passe par une comparaison de nom de
widget, chaîne `.rdata` puis `call 0x977ff7`) :

| trame | déclencheur mesuré | adresse |
|---|---|---|
| 6000 | ouverture / rafraîchissement de la fenêtre de ferme | constructeur `0x6109f0`, appelé en `0x61246b`, `0x613d0a`, `0x613d74`, `0x613d96` |
| 6002 | widget `"assign"` (chaîne `0xa40050`), poignée passée = `this+0x54c` | `0x610910`, appelé en `0x61493f` |
| 6004 | widget `"regain"` (chaîne `0xa40048`), poignée = `this+0x54c` | `0x6108c0`, appelé en `0x614998` |
| 6006 | boutons `button_ministration_01/02/03` (`0xa3feb8` / `0xa3fea0` / `0xa3fe88`), poignées `this+0x610` / `+0x710` / `+0x810`, index d'emplacement 0/1/2 écrit dans la globale `0xc1b4c8` | `0x610a40`, appelé en `0x61593c`, `0x6159ad`, `0x615a1e` |
| 6008 | bouton `button_tiket_buy_01` (chaîne `0xa40070`) | `0x610a90`, appelé en `0x6157fb` |

Le client dispose donc de **trois emplacements de « ministration »**, chacun avec sa poignée de
carte, et d'un bouton d'achat de tickets séparé de l'assignation.

---

## 3. Structure sur le fil

### 3.0 En-tête commun — 7 octets

| offset | type | nom | valeur/source |
|---|---|---|---|
| 0 | `uint32` | `length` | total de la trame, en-tête compris. `MessageBuffer.h:73-74` (rzu) ; `Game/Network/Packets/Header.cs:9,22` (dépôt) |
| 4 | `uint16` | `id` | `MessageBuffer.h:75` ; `Header.cs:10,23` |
| 6 | `uint8` | `checksum` | somme **octet par octet** des octets `0..5`. `MessageBuffer.h:76` ; `PacketExtensions.cs:13-25` ; le client fait exactement ce calcul (`0x6104e4`, `0x610516`, `0x610a18`) |

Le client pose lui-même cet en-tête sur les cinq trames C→S : `movl $0x7,(%eax)` puis boucle de
somme jusqu'à `+6` (`0x6104d8-0x61051d` pour 6002, `0x610538-0x610577` pour 6004,
`0x610588-0x6105c7` pour 6006, `0x610a0b-0x610a28` pour 6000, `0x610aab-0x610ac8` pour 6008).

### 3.1 `TM_CS_REQUEST_FARM_INFO` (6000) — **7 octets**

Aucun champ. Constructeur client `0x6109f0` : `mov $0x1770,%eax` en `0x610a02`, `length = 7` en
`0x610a0b`, somme `0..5` en `0x610a18-0x610a20`, envoi `0x610ad0`. rzu `TS_CS_REQUEST_FARM_INFO.h:6`
(`_DEF(_)` vide), NGemity `TS_CS_REQUEST_FARM_INFO.h:6`.

**Taille totale attendue : 7 octets** (0 de charge utile).

### 3.2 `TM_SC_FARM_INFO` (6001) — **8 + 120 × N octets**

| offset | type | nom | source |
|---|---|---|---|
| 0..6 | — | en-tête | §3.0 |
| 7 | `int8` | `summons` (nombre d'entrées) | `TS_SC_FARM_INFO.h:26` ; côté client : octet lu en `0x672196` (`mov 0x7(%eax),%al`) |
| 8 + 120×(n-1) | — | entrée n° `n` (0-based) | pas de `summons` côté client : `mul` par `0x78` en `0x6721a8` et copie de `summons × 120` octets depuis `packet+8` en `0x6721e3-0x6721fa` |

`summons` est un `int8_t` **borné à 127** par le sérialiseur (`PacketDeclaration.h:83-88`
`getClampedCount`, `:343-345` `writeSize(#ref, (type) ref_size)`) : le plafond de la trame est
`8 + 120 × 127 = 15 248` octets, sous la limite des 16 ko. Le client lit ce champ en **signé**
(`movsbl` en `0x6721a1`) : une valeur négative annule l'allocation.

Une entrée `TS_FARM_SUMMON_INFO` fait **120 octets**, offsets relatifs au début de l'entrée
(soit `offset paquet + 8`) :

| offset | type | nom | source / décision |
|---|---|---|---|
| 0 | `int32` | `index` | `TS_SC_FARM_INFO.h:9`. Sémantique **NON ÉTABLIE** (§7.2) |
| 4 | `int64` | `exp` | `TS_SC_FARM_INFO.h:10` |
| 12 | `char[19]` | `name` | `TS_SC_FARM_INFO.h:12-14` (`19` pour `version < EPIC_9_6`, cf. §4) ; ASCII + remplissage à zéro comme `GameSummonPackets.WriteName:273-278`, `NameSize = 19` en `:31` |
| 31 | `int32` | `duration` | `TS_SC_FARM_INFO.h:15` |
| 35 | `int32` | `elasped_time` | `TS_SC_FARM_INFO.h:16` (orthographe de la référence conservée) |
| 39 | `int32` | `refresh_time` | `TS_SC_FARM_INFO.h:17` |
| 43 | `int8` | `using_cash` | `TS_SC_FARM_INFO.h:18` |
| 44 | `int8` | `using_cracker` | `TS_SC_FARM_INFO.h:19` |
| 45 | — | `card_info`, **75 octets** | `TS_SC_FARM_INFO.h:20` (`TS_ITEM_FIXED_INFO`), motif établi au §3.7 |
| 120 | — | fin de l'entrée | 45 + 75 |

**Le total de 120 est mesuré, pas déduit** : le client alloue `summons × 0x78` octets (`0x6721a8`) et
recopie `summons × 120` octets depuis `packet + 8` (`0x6721b6` multiplication
`(n<<4) - n` doublée trois fois en `0x6721e9-0x6721f2`, puis `memcpy` en `0x6721fa`). La somme des
neuf champs rzu 7.3 (`4+8+19+4+4+4+1+1 = 45`, puis `card_info = 75`) tombe exactement sur 120 : c'est
la même chaîne arithmétique que celle déjà validée par l'enregistrement d'inventaire de 85 octets
(`socle-encheres.md:176-190`), et ce total **confirme à lui seul** `name = 19` (20 donnerait 121) et
l'absence du champ `unknown` de 9.8.1 (qui donnerait 128).

**Taille totale attendue : `8 + 120 × N` octets**, soit **8 octets** pour une ferme vide (N = 0).

### 3.3 `TM_CS_FOSTER_CREATURE` (6002) — **19 + 8 × T + 8 × C octets**

| offset | type | nom | source |
|---|---|---|---|
| 0..6 | — | en-tête | §3.0 |
| 7 | `uint32` (`ar_handle_t`) | `creature_card_handle` | `TS_CS_FOSTER_CREATURE.h:22` ; client : `mov %edi,-0x3f9(%ebp)` en `0x610933` |
| 11 | `int32` | `ticket_info` (nombre d'entrées) | `:23` ; client : `movl $0x1,-0x3f5(%ebp)` en `0x61096a` |
| 15 | `int32` | `cracker_info` (nombre d'entrées) | `:24` ; client : `mov %ecx,-0x3f1(%ebp)` en `0x61097a` |
| 19 | `uint32` | `ticket_info[0].ticket_handle` | `:8` ; client : `mov 0x544(%esi),%edi` + `0x610974` |
| 23 | `int32` | `ticket_info[0].ticket_count` | `:9` ; client : `mov %edi,-0x3e9(%ebp)` en `0x610959` |
| 27 | `uint32` | `cracker_info[0].cracker_handle` (si `C ≥ 1`) | `:15` ; client : `mov %eax,-0x3e5(%ebp)` en `0x61098a` |
| 31 | `int32` | `cracker_info[0].cracker_count` (si `C ≥ 1`) | `:16` ; client : `mov %edx,-0x3e1(%ebp)` en `0x610990` |

Les deux tableaux sont sérialisés **à la suite l'un de l'autre** (`dynarray` après les deux `count`,
`:25-26`) ; chaque entrée fait 8 octets.

Mesure côté client (constructeur `0x6104d0` : `length = 0x13` = 19 en `0x61050a`, trous de 15 octets
mis à zéro de `+4` à `+0x12` en `0x6104ef-0x6104fc`) puis constructeur complet `0x610910` :

* `ticket_info` (nombre) vaut **toujours 1** dans ce client (constante `0x61096a`) ;
* `cracker_info` (nombre) vaut **0 ou 1** (`sete %cl` en `0x610965`) ;
* `length` finale = `add $0x1b,%eax` en `0x61099b`, `eax` valant 0 ou 8 : **27 octets** (1 ticket,
  0 cracker) ou **35 octets** (1 ticket, 1 cracker). Les tailles de trame que le serveur doit donc
  accepter d'un vrai client 7.3 sont exactement ces deux-là.

**Taille totale attendue : `19 + 8 × T + 8 × C` octets** (19 minimum), chaque entrée de 8 octets
étant `{handle int32, count int32}`.

### 3.4 `TM_SC_RESULT_FOSTER` / `TM_SC_RESULT_RETRIEVE` / `TM_SC_RESULT_NURSE` (6003/6005/6007) — **8 octets**

| offset | type | nom | source |
|---|---|---|---|
| 0..6 | — | en-tête | §3.0 |
| 7 | `int8` | `result` | `TS_SC_RESULT_FOSTER.h:8`, `TS_SC_RESULT_RETRIEVE.h:6`, `TS_SC_RESULT_NURSE.h:8` |

Les trois handlers du client lisent **un seul octet en `+7`** et rien d'autre : `mov 0x7(%ecx),%dl`
en `0x67225e` (6003), `0x6722be` (6007) et `0x67231e` (6005). La charge utile s'arrête donc là.

**Taille totale attendue : 8 octets.** Les **valeurs** de `result` sont hors d'atteinte (§7.5).

### 3.5 `TM_CS_RETRIEVE_CREATURE` (6004) et `TM_CS_NURSE_CREATURE` (6006) — **11 octets**

| offset | type | nom | source |
|---|---|---|---|
| 0..6 | — | en-tête | §3.0 |
| 7 | `uint32` (`ar_handle_t`) | `creature_card_handle` | `TS_CS_RETRIEVE_CREATURE.h:6`, `TS_CS_NURSE_CREATURE.h:8` |

Mesure client : constructeurs `0x610530` (id `0x1774`, `length = 0xb` en `0x610564`) et `0x610580`
(id `0x1776`, `length = 0xb` en `0x6105b4`) ; les enveloppes `0x6108c0` et `0x610a40` écrivent la
poignée en `mov %edi,-0x5(%ebp)` (`0x6108ed`, `0x610a6d`), soit `+7` de la trame, avant d'envoyer.
Même poignée pour les deux : `this+0x54c` côté client (`0x61498f`, `0x615933`, `0x6159a4`,
`0x615a15`).

**Taille totale attendue : 11 octets.**

### 3.6 `TM_CS_REQUEST_FARM_MARKET` (6008) — **7 octets**

Aucun champ (`TS_CS_REQUEST_FARM_MARKET.h:6`, `_DEF(_)` vide). Constructeur client `0x610a90` :
id `0x1778` en `0x610aa2`, `length = 7` en `0x610aab`, somme `0..5` en `0x610ab8-0x610ac0`.

**Taille totale attendue : 7 octets.**

### 3.7 `card_info` — le motif d'objet de 75 octets, déjà écrit par le dépôt

`card_info` est `TS_ITEM_FIXED_INFO` (`TS_SC_FARM_INFO.h:20`), c'est-à-dire le motif d'objet de base
**sans** les trois champs de position de l'inventaire. En 7.3, `TS_ITEM_FIXED_INFO_DEF` n'émet que
`TS_ITEM_BASE_INFO_DEF(_, true, true, true)` : `type`, `wear_position`, `index` et
`own_summon_handle` sont tous gatés `version >= EPIC_9_8_1`
(`TS_SC_INVENTORY.h:103-114`). Le dépôt a déjà établi et **centralisé** ce motif :

* `ItemFixedInfoWriter.Size = 75` (`Game/Network/Packets/Game/ItemFixedInfoWriter.cs:71`) ;
* offsets internes : `handle@0`, `code@4`, `uid@8`, `count@16`, `ethereal_durability@24`,
  `endurance@28`, `enhance@32`, `level@33`, `flag@34`, `sockets@38` (4 × int32),
  `remain_time@54`, `elemental_effect.type@58`, `.remain_time@59`, `.attack_point@63`,
  `.magic_point@67`, `appearance_code@71` (`ItemFixedInfoWriter.Write:85-108`, `:73-75`) ;
* lecture depuis une entité : `ItemFixedInfoWriter.FromItem(ItemEntity):40-59` remplit le motif
  depuis l'objet lui-même (`uid`, `count`, `enhance`, `level`, `flag`, `sockets`, …) et fixe
  `AppearanceCode: 0` en `:58` — c'est le chemin que le lot utilise pour `card_info` ;
* la justification de la taille 75 (contre 71 chez rzu) est la même qu'aux encheres : le client 7.3
  lit `appearance_code`, et c'est cette variante qui fait 85 octets en inventaire
  (`docs/packet-specs/socle-encheres.md:176-190`, `ItemFixedInfoWriter.cs:8-13,33-38`).

Ici elle est **reconfirmée par une seconde mesure indépendante** : `120 - 45 = 75` (§3.2). Le lot
réutilise donc `ItemFixedInfoWriter` tel quel, sans deuxième écrivain d'objet.

### 3.8 Récapitulatif des tailles

| trame | taille totale | = |
|---|---|---|
| 6000 | 7 | en-tête seul |
| 6001 | `8 + 120 N` | 8 pour une ferme vide |
| 6002 | `19 + 8 T + 8 C` | 27 ou 35 pour un vrai client 7.3 |
| 6003 | 8 | 7 + `result int8` |
| 6004 | 11 | 7 + poignée |
| 6005 | 8 | 7 + `result int8` |
| 6006 | 11 | 7 + poignée |
| 6007 | 8 | 7 + `result int8` |
| 6008 | 7 | en-tête seul |

---

## 4. Gating de version — tranché pour Epic 7.3

| champ / famille | gating rzu | décision 7.3 | source |
|---|---|---|---|
| Les neuf ids | `X(600x, true)` sous `// Since EPIC_7_3` | **existent**, aucun id de remplacement | `TS_SC_FARM_INFO.h:29-31`, `TS_CS_FOSTER_CREATURE.h:28-30`, etc. ; NGemity confirme par un `// Since EPIC_7_3` sur chacun de ses neuf en-têtes |
| `TS_FARM_SUMMON_INFO.unknown` (`int64`) | `version >= EPIC_9_8_1` | **absent** en 7.3 | `TS_SC_FARM_INFO.h:11` |
| `name` | `19` si `version < EPIC_9_6`, `20` sinon | **19 octets** (18 caractères + nul) | `TS_SC_FARM_INFO.h:12-14` ; confirmé par le pas de 120 octets mesuré (§3.2) et par NGemity qui écrit 19 en dur (`TS_SC_FARM_INFO.h:10`) |
| `TS_ITEM_FIXED_INFO` (donc `card_info`) | `type`, `wear_position`, `index`, `own_summon_handle` seulement `>= EPIC_9_8_1` | **aucun de ces quatre champs** : `card_info` = motif de base de 75 octets | `TS_SC_INVENTORY.h:103-114` ; `ItemFixedInfoWriter.cs:8-13,71` |
| `appearance_code` de ce motif | `version >= EPIC_7_4 && version < EPIC_9_8_1` | **7.3 est sous le gate** (7.4 > 7.3) : rzu ne l'émet pas, le client 7.3 le lit | `TS_SC_INVENTORY.h:93` ; décision déjà tranchée et appliquée aux encheres (`socle-encheres.md:176-190`) |
| `summons` | `int8_t` | **1 octet**, borné à 127 | `TS_SC_FARM_INFO.h:26` ; `PacketDeclaration.h:83-88,343-345` |
| `ticket_info` / `cracker_info` (nombres) | `int32_t` | **4 octets** chacun, avant les tableaux | `TS_CS_FOSTER_CREATURE.h:23-24` |
| champs conditionnels de la famille | **aucun** autre `version >=` dans les neuf en-têtes | — | lecture intégrale des neuf fichiers |

---

## 5. Traitement attendu

### 5.1 Ce que Chihiro fait du paquet : rien

NGemity connaît les neuf ids et les neuf structures, et **ne les traite pas** :

* `shared/Server/ClientPackets.h:279-287` énumère les neuf ids ;
* `shared/Server/XPacket.h:97,125,140,141,146,232` inclut les six en-têtes C→S et `TS_SC_FARM_INFO.h` ;
* `grep -rniE 'farm|foster|nurse' Chihiro/src/ \| wc -l` → **0**. Aucun `case`, aucun handler, aucun
  `TS_FARM_SUMMON_INFO` construit : la logique de la ferme est **hors de NGemity**, donc aucune
  référence ne dit ce que le serveur doit répondre (ni `duration`, ni `result`, ni la validité d'un
  foster).

### 5.2 Ce que le serveur doit répondre

| reçu | réponse | justification |
|---|---|---|
| 6000 | **6001, 8 octets, `summons = 0`** | aucune référence ne sanctionne un contenu non vide, et le serveur n'a aucun état de ferme (§1). Une `summons = 0` est traitée proprement par le client : octet nul → saut de l'allocation (`cmp %bl,%al` / `je 0x672203` en `0x67219c-0x67219e`), compteur à 0 et tableau nul. C'est la seule charge utile que le serveur puisse produire sans inventer (§7.10) |
| 6002 | **rien** | le résultat (`6003`) porte un `result int8` dont les valeurs ne sont pas établies (§7.5), et le serveur ne peut ni valider ni consommer les tickets (§7.6) |
| 6004 | **rien** | idem, `6005` non établi |
| 6006 | **rien** | idem, `6007` non établi |
| 6008 | **rien** | rzu et NGemity ne déclarent **aucune** réponse à 6008, et le client n'a aucun handler entrant pour 6008 (§2) : un 6008 est une demande dont la réponse, si elle existe, n'est pas identifiée (§7.7) |

Ce choix suit la discipline déjà appliquée à `304` et à `TM_SC_REGION_ACK` : lire, borner, journaliser,
ne rien répondre tant qu'aucune référence ne sanctionne la réponse
(`Game/Network/Clients/GameClient.cs:1327-1335` pour `TM_SC_REGION_ACK`, `:1337-1351` pour `304`).

### 5.3 Le lot exact de `hermes/packet-socle-ferme-creatures`

1. **`Game/Network/Packets/Enums/GamePackets.cs`** — déclarer les six membres employés :
   `TM_CS_REQUEST_FARM_INFO = 6000`, `TM_SC_FARM_INFO = 6001`, `TM_CS_FOSTER_CREATURE = 6002`,
   `TM_CS_RETRIEVE_CREATURE = 6004`, `TM_CS_NURSE_CREATURE = 6006`,
   `TM_CS_REQUEST_FARM_MARKET = 6008`. Les trois résultats `6003`/`6005`/`6007` **ne sont pas
   déclarés** : le lot ne les envoie jamais, et tout membre déclaré doit être routé (critère 4). Le
   jour où un lot les enverra, il devra les déclarer **et** router un entrant (§5.2).
2. **`Game/Network/Clients/GameClient.cs`** — cinq bras, placés avant le `switch` qui lève
   `Unknown Packet Type` (`:1854-1865`), plus un garde-fou entrant :
   * `6000` : exiger `header.Length == 7` ; répondre `6001` (8 octets, `summons = 0`) ; toute autre
     longueur est une anomalie journalisée, sans réponse ;
   * `6002` : exiger `header.Length >= 19`, lire `creature_card_handle`, `T`, `C`, refuser
     (`T < 0 || C < 0`) et refuser toute longueur différente de `19 + 8T + 8C` ; journaliser les
     poignées ; ne rien répondre ;
   * `6004` et `6006` : exiger `header.Length == 11`, lire `creature_card_handle` ; ne rien répondre ;
   * `6008` : exiger `header.Length == 7` ; ne rien répondre ;
   * `6001` entrant (trame S→C envoyée par un client) : journaliser et abandonner, sur le modèle de
     l'arm `TM_SC_REGION_ACK` (`:1327-1335`). La consigne de lot admet qu'un id strictement S→C n'ait
     **pas** de bras de réception ; on garde ici le bras existant du dépôt, parce qu'il satisfait le
     critère 4 sans argument et qu'il ne coûte qu'un `if`. Si le dev préfère l'exception, il doit
     l'écrire dans la MR — les deux conduites sont défendables, une seule est retenue.
3. **`Game/Network/Packets/Game/GameFarmPackets.cs`** — un lecteur par trame C→S (mêmes conventions
   que `GameActionPackets.cs`, qui expose déjà des `record struct` + `TryRead…`) et **un** écrivain,
   `BuildFarmInfo(uint count, …)`/`BuildEmptyFarmInfo()`, qui produit `8 + 120 N` octets et écrit le
   `card_info` en appelant `ItemFixedInfoWriter.FromItem` (`:40-59`) puis
   `ItemFixedInfoWriter.Write` (`:77-108`, `Size = 75`). Aucun seuil, aucun coût, aucune
   politique : le fichier ne décide pas *quand* la ferme se remplit (§7.1).
4. **`CLAUDE.md`** — n'est **pas** modifié par le dev : le bloc à y insérer est porté par la
   description de la MR (critère 5).
5. **Tests** (critères 1-3) : au minimum un test d'**offsets** pour `6001` — taille totale `8` pour
   `N = 0` **et** `8 + 2 × 120` pour `N = 2` (cas vide et cas à plusieurs entrées exigés pour les deux
   trames à taille variable), `summons` `int8` en `+7`, `index` `+8`, `exp` `+12`, `name`
   `+20` (19 octets, seul le paquet complet), `duration` `+39`, `elasped_time` `+43`,
   `refresh_time` `+47`, `using_cash` `+51`, `using_cracker` `+52`, début de `card_info` `+53`,
   fin d'entrée `+128` — plus la trame vide de `6000` → `6001`, un cas `6002` à plusieurs entrées
   (2 tickets + 2 crackers → `19 + 16 + 16 = 51` octets) et le refus des longueurs fausses de
   `6002`/`6004`/`6006`/`6008`. Un cas négatif par lecteur (compteur négatif, longueur incohérente)
   est attendu : sans lui, le test ne peut pas échouer.

### 5.4 Le seul savoir de ferme déjà présent dans le dépôt

`Game/DataAccess/Entities/Enums/ItemFlag.cs:13-14` et `ItemStatus.cs:15-16` portent
`FarmedSummon = 27` et `NursedSummon = 28`, **sans aucun usage** dans le code. Ce sont les deux
drapeaux d'objet que le modèle client associe à une carte passée par la ferme ; ils ne suffisent pas
à établir le stockage (aucune relation ferme ↔ personnage n'existe), mais ils indiquent où le lot
suivant devra regarder avant d'inventer une table (§7.1).

---

## 6. Écarts assumés avec NGemity

1. **Même structure, deux noms** : NGemity nomme le motif d'objet `TS_ITEM_BASE_INFO`
   (`TS_SC_FARM_INFO.h:16`) là où rzu dit `TS_ITEM_FIXED_INFO` : même contenu, 75 octets en 7.3. On
   suit `ItemFixedInfoWriter`, déjà validé par les encheres.
2. **NGemity ne connaît pas le champ `unknown` de 9.8.1** (absent de son en-tête) : sans effet ici,
   rzu lui donne un gate `>= EPIC_9_8_1` donc il n'existe pas en 7.3. On ne l'émet pas.
3. **NGemity n'a aucun gate de version** (un seul id par trame, `// Since EPIC_7_3`) alors que rzu
   porte `X(id, true)` et le `name` 19/20 : pour 7.3 les deux références coïncident, et c'est rzu qui
   tranche (client 7.3 en dernier ressort).
4. **NGemity n'a pas de handler** (§5.1) : la logique de réponse vient donc d'un choix de lot
   documenté ici, jamais d'une référence. Aucun `result` ni `duration` n'est recopié de NGemity.
5. **`TS_CS_FOSTER_CREATURE` côté NGemity** garde deux tableaux (`ticket_info`, `cracker_info`)
   identiques à rzu — mais NGemity n'a aucune politique d'assignation, donc rien n'y dit quelle
   quantité de tickets partir : la référence ne tranche pas, le client si (§3.3 : T = 1, C ∈ {0,1}).

---

## 7. NON ÉTABLI

1. **Le stockage de la ferme.** Aucune table ne relie un personnage à ses cartes déposées ; les
   drapeaux `FarmedSummon = 27` / `NursedSummon = 28` (`ItemFlag.cs:13-14`) sont la seule piste, sans
   usage. *Question : la ferme se modélise-t-elle par un drapeau d'objet ou par une table ?
   `CreatureFarmResource` (`ArcadiaSchemaPSQL.sql:46-52`) est une table de référence, pas un
   stockage.*
2. **`index`** (`TS_SC_FARM_INFO.h:9`) : domaine de valeurs inconnu. Le client a trois emplacements de
   « ministration » et une globale d'index `0..2` (§2.1), ce qui suggère l'emplacement dans la ferme,
   mais **rien ne le prouve** : l'entrée est recopiée en bloc par `memcpy` (`0x6721fa`), le client ne
   relit jamais `index` champ par champ. *Question à trancher : `index` est-il l'emplacement 0..2 ?*
3. **`duration`, `elasped_time`, `refresh_time`** : ni rzu, ni NGemity, ni le client ne documentent
   leur unité ou leur origine côté serveur. Aucune référence ne dit ce qui les remplit. *Piste
   vérifiable : le client 7.3 embarque `reference/client73/db_creaturefarm.rdb`, 420 octets,
   `sha256 36564e4a4405afa01133a4e519e6e83d5a379228ad4202ec4ed91b7d2dfdea3d`, en-tête daté `20110523`,
   `count = 72` en `0x80`, puis 72 enregistrements de 4 octets — c'est-à-dire 4 champs `int8`,
   exactement le nombre de colonnes de `CreatureFarmResource` (`rate`, `form`, `enhance_level`,
   `ticket_count`). La correspondance octet→colonne et le passage de ces valeurs aux trois
   temporalités de la trame **ne sont pas établis** et ne doivent pas être devinés.*
4. **`exp`** : modèle d'expérience de la ferme inconnu (`SummonEntity.Exp` existe mais sans lien
   établi avec la ferme).
5. **Les valeurs de `result`** de `6003`/`6005`/`6007` : aucune référence ne donne l'énumération
   (0 = succès ? un code d'échec ?). Rien ne doit être envoyé tant que ce n'est pas tranché (§5.2).
6. **Les identités des objets** : quelle ressource d'objet est un « ticket » (`ticket_handle`,
   `ticket_count`) et un « cracker » (`cracker_handle`), et ce que `using_cash` / `using_cracker`
   signifient exactement, ne sont pas établis. `ItemFlag.cs`/`ItemStatus.cs` ne les nomment pas.
7. **La réponse à `6008`** : la référence déclare la demande sans réponse, le client n'a pas de
   handler entrant pour cet id (§2). *Question : `6008` attend-il une trame non identifiée (peut-être
   une des cinq `TM_SC_*` de la famille) ou rien du tout ?*
8. **La validation serveur de `6002`** : faire valider par la table `CreatureFarmResource` quelle
   quantité de tickets est due pour une carte donnée demande de trancher le point 3 ; le lot se borne
   donc à vérifier la forme de la trame (§5.3).
9. **`card_info` : quel objet ?** Le candidat naturel est la carte d'invocation
   (`SummonEntity.CardItemId` → `ItemEntity`), mais rien dans les références ne dit si `card_info`
   décrit la carte du personnage ou une ligne de catalogue. `appearance_code` reste à 0, seule valeur
   que le dépôt ait jamais mise sur le fil (`ItemFixedInfoWriter.cs:33-38`).
10. **La réponse vide à `6000`** est un **choix de lot** (§5.2, §5.3), pas un fait de référence : il
    est réversible le jour où la ferme existera. À confirmer par Killian.

---

## 8. Références épinglées

| référence | commit / empreinte | usage dans cette fiche |
|---|---|---|
| `reference/rzu` (`rzu` `master`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02) | tailles, ordre des champs, gating de version des neuf trames |
| `reference/ngemity` (`ngemity` `master`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03) | structures de repli, absence de handler (`Chihiro/src` : 0 occurrence) |
| `reference/client73/SFrame.exe` | `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | répartiteur `0x67e828` / table `0x67f68c`, handlers `0x672150`, `0x672220`, `0x672280`, `0x6722e0`, constructeurs `0x6104d0`, `0x610530`, `0x610580`, `0x610910`, `0x6108c0`, `0x610a40`, `0x6109f0`, `0x610a90`, noms de widgets |
| `reference/client73/db_creaturefarm.rdb` | `sha256 36564e4a4405afa01133a4e519e6e83d5a379228ad4202ec4ed91b7d2dfdea3d` (420 octets) | piste de données de la ferme (§7.3) |
| `Navislamia` `master` | `b56967a07430422add88e0e5cdf292b41b18f6c6` | base de la branche `hermes/packet-socle-ferme-creatures` |

---

## A VERIFIER PAR KILLIAN

Aucun de ces points n'est deviné dans cette fiche : ils sont posés en question ouverte (§7) et aucun
ne bloque le lot du §5.3, qui se borne à la forme des trames. Par ordre d'impact :

| # | décision attendue | détail |
|---|---|---|
| 1 | Modèle de stockage de la ferme : drapeaux d'objet `FarmedSummon = 27` / `NursedSummon = 28` ou table dédiée | §7.1 |
| 2 | Sens et unité de `duration`, `elasped_time`, `refresh_time` | §7.3 |
| 3 | Correspondance octet → colonne de `db_creaturefarm.rdb` / `CreatureFarmResource` | §7.3, §7.8 |
| 4 | Domaine de `index` : emplacement 0..2 de la ferme ou autre chose | §7.2 |
| 5 | Modèle d'`exp` de la ferme | §7.4 |
| 6 | Énumération des valeurs de `result` (`6003`/`6005`/`6007`) | §7.5 |
| 7 | Quels objets sont les « tickets » et les « crackers », et ce que `using_cash` / `using_cracker` signifient | §7.6 |
| 8 | Ce que `6008` échange, et si une réponse existe | §7.7 |
| 9 | `card_info` décrit-il la carte du joueur ou une ligne de catalogue ? | §7.9 |
| 10 | La réponse vide à `6000` (choix de lot) est-elle acceptée telle quelle ? | §7.10 |

La branche ne livre donc **aucune** constante de gameplay : ni durée, ni coût, ni plafond, ni sort
d'une créature à l'expiration, ni formule d'expérience.
