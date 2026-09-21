# Socle — étals de joueur : `TM_CS_START_BOOTH` (700) / `TM_CS_STOP_BOOTH` (701)

Portée : la famille complète `700`–`711` est **archéologisée** ici (ids, tailles, champs, gating de
version, présence client), mais le **socle livrable de cette branche se limite à `700` et `701`** plus
l'état d'étal qu'ils créent et le verrou qu'ils posent sur les autres actions. Les huit paquets restants
(`702`–`710`) sont hors socle : leur structure est figée ci-dessous pour que la branche qui les
attaquera ne refasse pas ce travail, mais aucun d'eux n'est implémenté, déclaré ni envoyé ici.

Ce que cette fiche tranche, et qui n'était pas tranché avant :

1. `711` (`TM_CS_CHECK_BOOTH_STARTABLE`) est **absent du client Epic 7.3** : il est listé dans
   `op_codes.md:180`, déclaré par rzu et NGemity, mais le client de référence n'a ni son nom, ni son
   créneau de dispatch. Il ne peut pas être émis par le client de cette époque — il sort du socle **et**
   de l'implémentation (§1.3).
2. La taille exacte de `700` est **mesurée dans le client** : `59 + 16 × N` octets, `N ≤ 8`
   (`MAX_BOOTH_ITEM_COUNT`, `CLAUDE.md:1081`), et non déduite (§3.2).
3. Le client de 7.3 attend, dans `703` et `710`, un enregistrement d'objet de **83 octets** (stride
   `0x53` mesuré) = `75 + 8` : la structure d'objet y **inclut** le `appearance_code` de 4 octets que
   rzu gate à `>= EPIC_7_4` (§3.5, §4). C'est le même écart que `CLAUDE.md:148` a déjà établi pour
   l'inventaire (85 octets) — les deux mesures concordent.
4. Aucun accusé de réception dédié à `700` n'existe dans la famille : le client ne traite vers le bas
   que `703`, `708`, `709`, `710` (§5.3). Le seul canal de refus est `TM_SC_RESULT` (`0`), que le client
   traite, avec le code `55` (`RESULT_NOT_ACTABLE_WHILE_USING_BOOTH`) déjà présent des deux côtés.

---

## 1. Identité

### 1.1 Les onze ids, côté serveur

`op_codes.md:169-180` (dépôt), corroboré par `reference/ngemity/shared/Server/ClientPackets.h:181-192`
et par les headers rzu :

| id | nom | sens | client 7.3 | socle |
|---|---|---|---|---|
| 700 | `TM_CS_START_BOOTH` | C→S | émis (prouvé, §2) | **oui** |
| 701 | `TM_CS_STOP_BOOTH` | C→S | émis (prouvé, §2) | **oui** |
| 702 | `TM_CS_WATCH_BOOTH` | C→S | id connu, builder non isolé | non |
| 703 | `TM_SC_WATCH_BOOTH` | S→C | traité (prouvé, §5.3) | non |
| 704 | `TM_CS_STOP_WATCH_BOOTH` | C→S | id connu | non |
| 705 | `TM_CS_BUY_FROM_BOOTH` | C→S | id connu | non |
| 706 | `TM_CS_SELL_TO_BOOTH` | C→S | id connu | non |
| 707 | `TM_CS_GET_BOOTHS_NAME` | C→S | id connu | non |
| 708 | `TM_SC_GET_BOOTHS_NAME` | S→C | traité (prouvé) | non |
| 709 | `TM_SC_BOOTH_CLOSED` | S→C | traité (prouvé) | non |
| 710 | `TM_SC_BOOTH_TRADE_INFO` | S→C | traité (prouvé) | non |
| 711 | `TM_CS_CHECK_BOOTH_STARTABLE` | C→S | **absente du binaire** | **non — voir 1.3** |

Sources d'identité nom ↔ id côté serveur : `reference/rzu/librzu/src/packets/GameClient/TS_CS_START_BOOTH.h:20-22`
(`X(700, version < EPIC_9_6_3)`, `X(1700, version >= EPIC_9_6_3)`), `TS_CS_STOP_BOOTH.h:7-9` (701/1701),
`TS_CS_WATCH_BOOTH.h:10-12` (702/1702), `TS_SC_WATCH_BOOTH.h:24-26` (703/1703),
`TS_CS_STOP_WATCH_BOOTH.h:10-12` (704/1704), `TS_CS_BUY_FROM_BOOTH.h:13-15` (705/1705),
`TS_CS_SELL_TO_BOOTH.h` (706), `TS_CS_GET_BOOTHS_NAME.h:11-13` (707/1707),
`TS_SC_GET_BOOTHS_NAME.h:17-19` (708/1708), `TS_SC_BOOTH_CLOSED.h:8-10` (709/1709),
`TS_SC_BOOTH_TRADE_INFO.h:17-21` (710), `TS_CS_CHECK_BOOTH_STARTABLE.h:7-9` (711/1711).

### 1.2 Les mêmes ids, côté client

Client de référence : `reference/client73/SFrame.exe`,
`sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`.

Le client construit une table `id → nom` (initialiseur `.text` à partir de `VA 0x676D6E`, motif
`push $<VA du nom>` puis `mov $<id>,%eax`) : les onze entrées `700`…`710` y sont **contiguës**, de
`VA 0x6780CE` (`TM_CS_START_BOOTH`, id `700`) à `VA 0x6784A2` (`TM_SC_BOOTH_TRADE_INFO`, id `710`),
soit 0x62 octets par entrée. Les couples relevés : `700`@0x6780CE, `701`@0x678130, `702`@0x678192,
`703`@0x6781F4, `704`@0x678256, `705`@0x6782B8, `706`@0x67831A, `707`@0x67837C, `708`@0x6783DE,
`709`@0x678440, `710`@0x6784A2.

Lecture reproductible :

```bash
cd /srv/navislamia/reference/client73
strings -n 4 SFrame.exe | grep -n "TM_CS_START_BOOTH\|TM_SC_BOOTH_TRADE_INFO"   # les 11 noms TM_*BOOTH*
objdump -d -M intel --start-address=0x6780c0 --stop-address=0x678100 SFrame.exe # entrée 700 de la table id → nom
```

(Le détail de toutes les commandes de lecture est en §9 ; aucun exécutable du client n'est lancé.)

### 1.3 `711` est absent du client 7.3 — décision

Trois constats indépendants dans `SFrame.exe` :

1. Aucune occurrence de la chaîne `CHECK_BOOTH` ni de `BOOTH_STARTABLE` (recherche sur tout le
   binaire, 0 résultat) : pas de `TM_CS_CHECK_BOOTH_STARTABLE` dans la table `id → nom`.
2. La table `id → nom` est contiguë de `700` à `710` et **s'arrête à `710`** (l'entrée suivante est
   `TM_SC_BOOTH_TRADE_INFO`, puis un autre groupe de paquets).
3. Le répartiteur entrant (§5.3) couvre `0…250`, `255…500`, `505…710` : `711` tombe hors des tables,
   donc dans la branche « `ja` → `0x67EF21` » (journalisation *processing-되지 않은 메세지*, c.-à-d.
   message non traité).

Conclusion : ce client **ne peut pas émettre `711`** et **ne l'attend pas**. Le socle ne le déclare pas
et ne l'implémente pas. C'est une friction documentaire assumée : `op_codes.md:180` liste un id qui
n'existe pour aucun client d'Epic 7.3 (rzu et NGemity le connaissent parce qu'ils décrivent des epics
plus récents). À trancher côté Killian : soit `op_codes.md` garde la ligne avec une note « id absent du
client 7.3 », soit elle part — la fiche ne modifie pas `op_codes.md`.

---

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Ouverture d'étal (`700`)

Le joueur ouvre la fenêtre de création d'étal — le client embarque
`window_business_booth_create.nui` et `window_business_booth_exchange.nui` (chaînes du binaire,
référencées depuis `.data` à `VA 0xC1BDB4` et `VA 0xC1BDB8`) —, y saisit un nom, choisit le mode
(vente/achat), inscrit jusqu'à huit objets avec une quantité et un prix, puis valide.

**Preuve d'émission (les trois étapes : construction, remplissage, envoi) :**

* Construction : `VA 0x48CBD0` — `mov DWORD PTR [esi],0x7` (longueur 7), boucle de somme sur les 7
  octets d'en-tête, `mov eax,0x2bc` (= 700) puis `mov WORD PTR [esi+4],ax`, `mov DWORD PTR [esi],0x3b`
  (= 59), et `memset(trame, 0, 0x3b)`. C'est le constructeur « étal vide », **trame de 59 octets**.
  Appelé depuis un unique site, `VA 0x48D5C9` (`lea ecx,[ebp-0x44]` = la trame locale).
* Remplissage (dans la fonction appelante, `VA 0x48D5C9` → `0x48D623`) :
  - nom : `push 0x30` + `call 0x9798D0` (`VA 0x48D5EE`) = `strncpy(trame+7, nom, 48)` — le nom vient
    d'une `std::string` (contrôle SSO `cmp DWORD PTR [eax+0x14],0x10` en `VA 0x48D5E1`), donc **48
    caractères au plus + octet nul dans le champ de 49** ;
  - `type` : `setne al` / `inc al` (`VA 0x48D5FB`, `VA 0x48D5FE`) à partir de `[ebx+0x63]` → **le champ
    vaut 1 ou 2, jamais autre chose** ;
  - `count` : `(fin − début) >> 2` de deux tableaux de handles (`VA 0x48D623`), écrit en `uint16`
    (`mov WORD PTR [ebp-0xb],cx`) ;
  - taille finale : `mov eax,[ebp-0x44]` (= 59, la longueur écrite par le constructeur), `shl ecx,0x4`
    (= `16 × N`, `VA 0x48D606`), `add eax,ecx`, puis `call 0x9767B1` (= `operator new`, `VA 0x48D60F`)
    et copie de 59 octets (`rep movs` de 0x0E dwords + un mot + un octet, `VA 0x48D61E`-`0x48D622`) ;
  - objets : `lea edx,[eax+0x3b]` (`VA 0x48D636`) = premiers objets à `trame+59`, avance de `0x10` par
    objet (`add edx,0x10`, `VA 0x48D66A`), chaque enregistrement écrit `handle` (`+0`), `cnt` (`+4`),
    puis un `int64` de prix (`+8`, deux dwords).
* Envoi : `lea esi,[eax+6]` puis recomputation de la somme sur les 7 octets d'en-tête et écriture en
  `trame+6` (`VA 0x48D671`-`0x48D68A`), puis appel virtuel avec la trame en argument
  (`push eax` / `mov eax,DWORD PTR [edx+0xc4]` / `call eax`, `VA 0x48D69B`-`0x48D6A4`), et
  `operator delete` de la trame.

Le `type` et les quantités/prix sont donc **prouvés** par le constructeur lui-même ; rien n'est déduit
de rzu pour ce paquet.

### 2.2 Fermeture d'étal (`701`)

Même fonction, branche voisine : `VA 0x48CC20` construit une trame d'en-tête seul (`mov DWORD PTR [esi],0x7`,
`mov eax,0x2bd` = 701, `mov WORD PTR [esi+4],ax`, somme) et l'appelant l'envoie depuis `VA 0x48D582`.
**Trame de 7 octets**, aucun champ. (C'est le pendant de « fermer ma boutique » dans la fenêtre
`window_business_booth_exchange.nui`.)

### 2.3 Ce que le client refuse lui-même, et qui borne `700`

Textes du client dans `reference/client73/db_string.rdb`
(`sha256 = 4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1`), chaînes présentes et
leur libellé exact :

| clé | texte |
|---|---|
| `smsg_booth_cant_setup` | `You must be Lv 10 or higher to open a store.` |
| `smsg_booth_cant_setup_item` | `You must have at least 1 item for sale to open a store.` |
| `smsg_booth_name_short` | `Your store name is too short. It must be at least 6 characters.` |
| `smsg_booth_name_long` | `Your store name is too long. It must be no more than 40 characters.` |
| `smsg_booth_notice_name` / `_amount` / `_price` | `Please enter a name… / set a quantity. / set a price.` |
| `smsg_booth_notice_selling` / `_buying` | les deux modes (`You have sold…` / `You have purchased…`) |
| `smsg_booth_buying_not_support` | `The purchasing store is not supported yet.` |
| `smsg_booth_cant_equip_item`, `smsg_booth_not_add_equipitem` | un objet équipé ne se met pas en étal |
| `smsg_booth_open` | `Your store has been opened.` |
| `smsg_booth_not_use_item` | `You may not equip/use an item while your store is open.` |
| `smsg_booth_not_use_skill` | `You may not use a skill while your store is open.` |
| `smsg_booth_not_use_store` | `You may not access another store while your store is open.` |
| `smsg_booth_not_add_item` | `You may not add items while your store is open.` |
| `smsg_booth_not_action` | `You may not take other actions while your store is open.` |
| `smsg_booth_notice_advice_price` | `This price is greatly different from the suggested retail price for this item…` |

Conséquences pour le socle (§5.3) : le nom fait **6 à 40 caractères**, il faut **au moins un objet** et
**niveau 10 minimum** pour ouvrir, et pendant que l'étal est ouvert le client lui-même annonce que
l'équipement/utilisation d'objets, l'usage de compétences et l'accès à un autre magasin sont refusés.
Ces textes **ne prouvent pas** le code de résultat envoyé par le serveur (voir §7) : ils prouvent la
règle et le message.

---

## 3. Structure sur le fil

### 3.1 En-tête commun (7 octets)

`Length` `uint32` @0, `ID` `uint16` @4, `Checksum` `uint8` @6 — identique au `Header` du dépôt
(`Game/Network/Packets/Header.cs:9-11`) et confirmé par le client, qui écrit 7 dans `trame[0]`, l'id en
`trame[4]` et la somme des 7 octets en `trame[6]` (§2.1). Côté serveur, la somme est calculée à l'envoi
(`Game/Network/Packets/Packet.cs:57`, `PacketExtensions.cs:13`).

### 3.2 `700` `TM_CS_START_BOOTH` — **59 + 16 × N octets**

| offset | type | nom | source |
|---|---|---|---|
| 0 | `uint32` | `Length` = `59 + 16×N` | client : `VA 0x48D5C9`+`0x48D606`+`0x48D60F` (59 puis `16×N`) |
| 4 | `uint16` | `ID` = `700` | client `VA 0x48CBF8` (`mov eax,0x2bc`) ; `rzu TS_CS_START_BOOTH.h:21` |
| 6 | `uint8` | `Checksum` | client `VA 0x48D68A` |
| 7 | `char[49]` | `name` (48 caractères utiles + nul) | client `VA 0x48D5EE` (`strncpy(...,0x30)`) ; `rzu:15` ; NGemity `TS_CS_START_BOOTH.h:15` |
| 56 | `uint8` | `type` ∈ {1, 2} | client `VA 0x48D5FB`/`0x48D5FE` ; `rzu:16` ; NGemity `:16` |
| 57 | `uint16` | `count` = N | client `VA 0x48D623` (division par 4 des tableaux de handles) ; `rzu:17` ; NGemity `:17` |
| 59 | `TS_BOOTH_OPEN_ITEM_INFO[N]` | `items` | client `VA 0x48D636` (`lea edx,[eax+0x3b]`) ; `rzu:18` |

Enregistrement d'objet (`TS_BOOTH_OPEN_ITEM_INFO`, 16 octets) :

| offset relatif | type | nom | source |
|---|---|---|---|
| +0 | `uint32` (`ar_handle_t`) | `item_handle` | client : `mov [edx],esi` en `VA 0x48D643`, alimenté par le vecteur de handles `[ebx+0x53]` (`VA 0x48D640`) ; `rzu:6` ; NGemity `:7` |
| +4 | `int32` | `cnt` | client : `mov [edx+0x4],esi` en `VA 0x48D64B` (depuis `[ebx+0x43]`) ; `rzu:7` ; NGemity `:8` |
| +8 | `int64` | `gold` | client : `mov [edx+0x8],edi` puis `mov [edx+0xc],esi` en `VA 0x48D654`/`0x48D65B` — **deux dwords, donc bien 8 octets**, depuis `[ebx+0x33]` ; `rzu:8-9` (`impl int64_t` pour `>= EPIC_4_1_1`) ; NGemity `:9-10` |

La boucle d'objets est `VA 0x48D640`-`0x48D66F` (`add edx,0x10` en `VA 0x48D66A`), bornée par
`(0x57 − 0x53) >> 2` = nombre de handles (`VA 0x48D661`-`0x48D667`).

La mesure client est directe : la trame vide fait **59** octets, `N` objets sont ajoutés à `trame+59`
avec un pas de **16** octets, et la longueur stockée en `trame[0]` vaut `59 + 16×N`. Aucun autre champ
n'existe : `N = 0` est donc une trame valide de 59 octets au niveau du fil (le client sait la
construire), mais la règle de jeu « au moins un objet » la refuse (§2.3).

Plafond : `N ≤ 8` (`MAX_BOOTH_ITEM_COUNT`, `CLAUDE.md:1081`, constante `S_CONSTANT` du PDB
`Game_bin`, décodée comme `ITEM_ARRANGE_COOL_TIME` à `CLAUDE.md:1078-1081`). Le champ `count` acceptant
`uint16`, un client hostile peut annoncer jusqu'à 65535 objets : la longueur annoncée doit être
comparée à la longueur reçue **avant** toute lecture, et le plafond 8 vérifié avant d'accepter.

### 3.3 `701` `TM_CS_STOP_BOOTH` — **7 octets**

Aucun champ (`rzu TS_CS_STOP_BOOTH.h:5` : `DEF` vide ; NGemity `TS_CS_STOP_BOOTH.h` idem). Le
constructeur client écrit 7 dans `trame[0]` et l'id 701 (`VA 0x48CC20`).

### 3.4 Le reste de la famille — tailles 7.3, pour la branche suivante

Aucune de ces trames n'est produite par le socle ; les tailles sont là pour ne pas les redécouvrir.

| id | trame 7.3 | détail | source principale |
|---|---|---|---|
| 702 | 11 | `target` `ar_handle_t` @7 | rzu `TS_CS_WATCH_BOOTH.h:8` |
| 703 | **14 + 83×N** | `target` @7, `type` `uint8` @11, `count` `uint16` @12, objets @14 | **client** : `VA 0x673352` (target), `0x673347` (type), `0x673363` (count), `0x67335A` (objets @0xE), stride `imul esi,esi,0x53` `VA 0x6733A8`, copies de 0x14 dwords+mot+octet = 83 `VA 0x6733BA` |
| 704 | 11 | `target` @7 | rzu `TS_CS_STOP_WATCH_BOOTH.h:8` |
| 705 | 13 + 85×N | `target` @7, `count` `int16` @11, objets `TS_ITEM_FIXED_INFO` @13 | rzu `TS_CS_BUY_FROM_BOOTH.h:9-11` |
| 706 | 19 | `target` @7, `item_handle` @11, `cnt` `int32` @15 | rzu `TS_CS_SELL_TO_BOOTH.h:8-10` |
| 707 | 11 + 4×H | `count` `int32` @7, handles @11 | rzu `TS_CS_GET_BOOTHS_NAME.h:8-9` |
| 708 | 11 + 53×N | `count` `int32` @7, puis (`handle` 4 + `name[49]`) | rzu `TS_SC_GET_BOOTHS_NAME.h:8-15` |
| 709 | 11 | `target` @7 | rzu `TS_SC_BOOTH_CLOSED.h:6` |
| 710 | **14 + 83×N** | `target` @7, `is_sell` `bool` @11, `count` `uint16` @12, objets @14 | **client** : handler `VA 0x6734D0` (`mov eax,[esi+0x7]`, `mov dl,[esi+0xb]`, `cmp ax,WORD PTR [esi+0xc]`, `lea edi,[esi+0xe]`, stride `0x53`) |

Les deux trames mesurées (`703`, `710`) donnent le **même** enregistrement de 83 octets
(`imul esi,esi,0x53`, copies de 83 octets, `add edi,0x53`), ce qui recoupe la décomposition ci-dessous.

### 3.5 `TS_ITEM_FIXED_BASE_INFO` pour 7.3 — **75 octets**

Cet enregistrement est le cœur de `703` et `710` (`rzu TS_SC_WATCH_BOOTH.h:12`,
`TS_SC_BOOTH_TRADE_INFO.h:12`). Décomposition par le gating rzu (`TS_SC_INVENTORY.h:58-95`), sur
l'ordre d'émission réel des `_(impl)` (`PacketDeclaration.h:512-522` : `_(def)` n'émet rien en
sérialisation, chaque `_(impl)` dont la condition est vraie émet à sa place textuelle) :

| offset | type | champ | condition appliquée à 7.3 |
|---|---|---|---|
| 0 | `uint32` | `handle` | toujours (`:68`) |
| 4 | `int32` | `code` | toujours (`:69`) |
| 8 | `int64` | `uid` | toujours (`:70`) |
| 16 | `int64` | `count` | `>= EPIC_4_1_1` (`:72`) → **oui** (variante `int32` réservée à `< 4.1.1`) |
| 24 | `int32` | `ethereal_durability` | `>= EPIC_6_3 && < EPIC_9_8_1` (`:75`) |
| 28 | `uint32` | `endurance` | `>= EPIC_4_1 && < EPIC_9_8_1` (`:77`) |
| 32 | `uint8` | `enhance` | toujours (`:78`) |
| 33 | `uint8` | `level` | toujours (`:79`) |
| 34 | `uint32` | `flag` | `< EPIC_9_8_1` (`:87`) |
| 38 | `TS_ITEM_SOCKETS` (4 × `int32` = 16) | `sockets` | `version < EPIC_9_8_1` (`:88`, rendu inconditionnel pour les vieux epics par le commit `87c1e83b`) |
| 54 | `int32` | `remain_time` | `< EPIC_9_8_1` (`:91`) |
| 58 | `TS_ITEM_ELEMENTAL_EFFECT` = `uint8 type` (1) + `int32 remain_time` (4) + `int32 attack_point` (4) + `int32 magic_point` (4) = **13** | `elemental_effect` | `>= EPIC_6_1` (`:92`) |
| 71 | `int32` | `appearance_code` | rzu dit `>= EPIC_7_4 && < EPIC_9_8_1` (`:93`) → **faux en 7.3 chez rzu, vrai dans le client** : voir ci-dessous |

Total rzu pour 7.3 : 71 octets. Total **client** : `83 − 8 = 75` octets.

**Décision : 75 est la bonne valeur pour Epic 7.3**, et l'écart porte sur `appearance_code` :

* le client de 7.3 attend `appearance_code` (4 octets) à la position de `:93`, c.-à-d. juste avant
  `wear_position` ;
* c'est exactement ce que le dépôt a déjà établi pour l'inventaire : `CLAUDE.md:148-151` mesure un
  enregistrement d'objet de **85** octets directement dans `SFrame.exe` (stride de copie `0x55`) et
  écrit que le client veut le `appearance_code` de 4 octets que rzu gate à `>= EPIC_7_4`, avec
  l'avertissement explicite de ne pas revenir à l'enregistrement canonique de 81 octets ;
* **la concordance est arithmétique** : `75 + 2 (wear_position) + 4 (own_summon_handle) + 4 (index) = 85`,
  et `75 + 8 (gold int64) = 83`, le stride mesuré dans `703` et `710`.

L'`int64 gold` de `703`/`710` en 7.3 vient de `_(impl)(int64_t, gold, version >= EPIC_4_1_1 && version <
EPIC_9_8_1)` — après l'objet (`rzu TS_SC_WATCH_BOOTH.h:13`, `TS_SC_BOOTH_TRADE_INFO.h:13`) — et il est
indépendamment confirmé par le pas de 16 octets de `700` (4 + 4 + 8), qui interdit un `gold` de 4
octets.

`TS_ITEM_FIXED_INFO` (pour `705`) = `TS_ITEM_FIXED_BASE_INFO` (75) suivi de `wear_position` `int16` (2),
`own_summon_handle` `ar_handle_t` (4), `index` `int32` (4) = **85**, d'après `rzu TS_SC_INVENTORY.h:103-113`
(les `_(impl)` `< EPIC_9_8_1` sont après la base) : `705` = `13 + 85×N`.

---

## 4. Gating de version — statué pour Epic 7.3

| ce qui est gaté dans rzu | condition | décision 7.3 |
|---|---|---|
| id des onze paquets | `< EPIC_9_6_3` → 700…711 ; `>=` → 1700…1711 | **garder `700`–`711`** : le client de 7.3 ne connaît que ces ids (§1.2) |
| `gold` de `700` | `>= EPIC_4_1_1` → `int64`, sinon `int32` | **`int64`, 8 octets** : le pas client de 16 octets le prouve |
| `gold` de `703`/`710` | position avant l'objet si `>= EPIC_9_8_1`, après si `>= 4.1.1 && < 9.8.1` | **après l'objet, `int64`** |
| `item_type` `uint16` de `703`/`710` | `>= EPIC_9_8_1` | **absent** |
| `count` de `TS_ITEM_BASE_INFO` | `>= EPIC_4_1_1` → `int64` | **`int64`** |
| `sockets` | `version < EPIC_9_8_1` (commit `87c1e83b`) | **présent, 16 octets** |
| `ethereal_durability` | `>= EPIC_6_3 && < EPIC_9_8_1` | **présent** |
| `endurance` | `>= EPIC_4_1 && < EPIC_9_8_1` → `uint32` | **présent, `uint32`** |
| `elemental_effect` | `>= EPIC_6_1` | **présent, 13 octets** |
| `appearance_code` | `>= EPIC_7_4 && < 9.8.1` | **présent malgré la borne rzu** (§3.5) |
| `augment_chance`/`awaken_option`/`random_stats`/`summon_code`/`item_effect_id`/`luciad_power_level` | `>= 9.2`, `>= 8.1`, `>= 8.2`, `>= 8.2`, `>= 9.6.1`, `>= 9.8.1` | **absents** |
| `711` | aucun gating (rzu et NGemity le déclarent pour tous les epics) | **hors socle : le client 7.3 ne l'a pas** (§1.3) |

---

## 5. Traitement attendu

### 5.1 Ce que font les références

* **rzu** ne décrit que le format : les headers ci-dessus, `SessionPacketOrigin::Client/Server`, aucune
  logique de serveur.
* **NGemity** déclare les douze paquets (`shared/Server/ClientPackets.h:181-192`) et **n'a aucun
  handler** : la table de handlers de `Chihiro/src/Network/GameNetwork/WorldSession.cpp` ne contient
  aucune entrée `booth`. Le seul code lié est **commenté** dans `buildStatus`
  (`Chihiro/src/Network/Messages.cpp:574-577`) :
  `// if (pPlayer->GetBoothStatus() == StructPlayer::BUY_BOOTH) status |= TS_PLAYER_FLAG::FLAG_BUY_BOOTH;`
  (et `SELL_BOOTH` → `FLAG_SELL_BOOTH`). Les drapeaux eux-mêmes existent
  (`shared/Server/TS_MESSAGE.h:33-34` : `FLAG_BUY_BOOTH = 1 << 9`, `FLAG_SELL_BOOTH = 1 << 10`) et le
  code de résultat aussi (`shared/Server/TS_MESSAGE.h:108`, `TS_RESULT_NOT_ACTABLE_WHILE_USING_BOOTH = 55`).
  L'énumération `StructPlayer::BUY_BOOTH`/`SELL_BOOTH` référencée par le commentaire **n'est pas dans
  l'arbre** : voir §7.
* Conclusion : **aucune référence ne fournit la logique d'étal** ; NGemity ne fournit que la forme de
  l'état (un statut d'étal par joueur, exposé dans les drapeaux du joueur) et le nom du code 55.

### 5.2 Découpage : ce qui est dans le socle, ce qui n'y est pas

**Dans le socle (implémenté par cette branche) :** `700`, `701`, l'état d'étal par joueur, le plafond
de 8 objets et le verrou d'actions qui va avec le code 55.

**Hors socle, structures figées ici seulement :** `702`–`710`. Raison, paquet par paquet :

* `703` : renvoyer le contenu d'un étal exige de savoir lire les objets **d'un autre** joueur et de
  sérialiser `TS_ITEM_FIXED_BASE_INFO` (75 octets, §3.5) — c'est le système d'objets, pas le socle ;
* `707`/`708` : lister les étals d'une région exige un registre d'étals et une visibilité (§5.4) ;
* `705`/`706`/`710` : ce sont des transactions (objets + or, poids, `smsg_booth_heavy`) ;
* `709` : ne se comprend que si un client peut regarder un étal (`702`/`703`) ;
* `704` : pendant de `702` ;
* `711` : absent du client (§1.3).

Les ids hors socle restent donc **non déclarés** dans `GamePackets`. Conséquence assumée : si un client
envoie `702`, `704`, `705`, `706`, `707` ou `711` (ce que le client de 7.3 ne fait jamais pour `711`,
et ne peut faire pour les autres que si un étal est visible — ce qui n'arrive pas dans ce socle), la
réception tombe dans le `_ => throw new Exception("Unknown Packet Type")`
(`Game/Network/Clients/GameClient.cs:802`) : voir §5.4 et §7.5.

### 5.3 Comportement attendu de `700` / `701` (à implémenter)

1. **Déclarer** `TM_CS_START_BOOTH = 700` et `TM_CS_STOP_BOOTH = 701` dans
   `Game/Network/Packets/Enums/GamePackets.cs`, **et** leurs deux bras de dispatch dans la boucle de
   réception (`GameClient.cs`, bras `if (header.ID == …)` avant le `switch` final) : la règle du dépôt
   est qu'aucun membre de `GamePackets` ne peut atteindre le `switch` final.
2. **`700` — lecture tolérante et bornée** : refuser avant toute lecture si `Length` reçu `< 59`
   (`InvalidArgument`), si `count > 8` (`LimitMax`), si la longueur reçue `< 59 + 16×count`
   (`InvalidArgument`), si `type` ∉ {1, 2} (`InvalidArgument`), si `count == 0` (`InvalidArgument`), si
   le nom compte moins de 6 ou plus de 40 caractères (`InvalidArgument`), si le joueur a moins de
   niveau 10 (`NotEnoughLevel`). Le champ `name` est lu sur 49 octets, terminé au premier octet nul ; les
   octets bruts sont conservés tels quels.
3. **`700` accepté** : l'état d'étal du personnage devient ouvert et retient le nom (≤ 48 octets), le
   `type` verbatim, et jusqu'à 8 triplets (`item_handle`, `cnt`, `gold`). Le socle **ne résout pas** les
   handles contre l'inventaire (§7.3) : il retient ce que le client a déclaré, et rien ne peut encore le
   consommer puisque aucun paquet de la famille n'est envoyé.
4. **Réponse à un `700` accepté : aucune.** Aucun paquet de la famille n'accuse réception, et le client
   n'en attend aucun (§5.4) ; le socle ne fabrique pas d'accusé. C'est un point tranché, pas un oubli,
   et il est à l'épreuve d'un enregistrement réel (§7.1).
5. **`700` refusé** : `TS_SC_RESULT` (`0`, déjà déclaré, `15` octets : `request_msg_id` `uint16` = 700,
   `result`, `value`) avec le code du point 2 — `GameClient.SendResult(...)`, la convention maison
   (`GameClient.cs:56`, exemples `GameClient.cs:328-530`). Le `request_msg_id` permet au client
   d'associer le refus.
6. **`701`** : accepté si un étal est ouvert (l'état redevient fermé et les objets déclarés sont
   oubliés) ; si aucun étal n'est ouvert, la transition est idempotente et répond `Success` — c'est un
   choix, aucun source ne le fixe (§7.4). Refus `55` si `701` arrive pendant… rien : `701` reste
   toujours acceptable.
7. **Verrou d'actions (`ResultCode.NotActableWhileUsingBooth` = 55**, déjà déclaré
   `Game/Network/Packets/ResultCode.cs:67`) : tant que l'étal est ouvert, les actions suivantes sont
   refusées par `SendResult(<id demandeur>, 55)` : `TM_CS_PUTON_ITEM` (200), `TM_CS_PUTOFF_ITEM` (201),
   `TM_CS_DROP_ITEM` (203), `TM_CS_TAKE_ITEM` (204), `TM_CS_ERASE_ITEM` (208),
   `TM_CS_CHANGE_ITEM_POSITION` (218), `TM_CS_ARRANGE_ITEM` (219), `TM_CS_USE_ITEM` (253),
   `TM_CS_SKILL` (400). Liste bornée aux textes du client (`smsg_booth_not_use_item`,
   `smsg_booth_not_use_skill`, `smsg_booth_not_action`, §2.3) et aux actions déjà gérées par la boucle
   de réception. `smsg_booth_not_use_store` (« accéder à un autre magasin ») restera sans objet tant que
   `702` n'existe pas.
8. **État par client** : `ConnectionInfo` est le porteur maison de l'état par connexion
   (`Game/Network/Clients/ConnectionInfo.cs`, à côté de `SpawnedNpcs`/`SpawnedMonsters`/`SpawnedProps`
   et de `NextInventoryArrangeAt`). L'état d'étal y va : un objet immuable (nom, type, liste des 8
   triplets) ou `null`, lu/écrit sous un verrou dédié, comme les dictionnaires de visibilité voisins.
   Rien n'est persisté en base dans ce socle (un étal ne survit pas à une déconnexion).
9. **Tests exigés** (critère 3 des critères transversaux) : offsets complets de `700` (longueur
   `59 + 16×N`, `name` @7, `type` @56, `count` @57, objets @59, `item_handle` @+0, `cnt` @+4, `gold`
   @+8) sur au moins deux valeurs de `N` dont `N = 0` et `N = 8`, taille `7` de `701`, refus de chacun
   des cas du point 2 avec le code attendu, et un test du verrou (action refusée avec `55` pendant un
   étal ouvert, autorisée après `701`).

### 5.4 Ce que le client attend du serveur — et ce qui se passe aujourd'hui

Le client n'a **aucun** créneau de dispatch pour un paquet entrant `700`, `701`, `702`, `704`, `705`,
`706`, `707` (table d'octets de `VA 0x67F35C`, indexée par `id − 505`, ces ids y valant `0x12` = index
du défaut `0x67EF21`, qui journalise un message non traité). Il traite exactement quatre paquets de la
famille, avec leurs tables et cibles vérifiées :

| id | entrée de table | handler client |
|---|---|---|
| 703 | `0x67F422` = index 14 | `VA 0x6732F0` |
| 708 | `0x67F427` = index 15 | `VA 0x673670` |
| 709 | `0x67F428` = index 16 | `VA 0x66D9B0` |
| 710 | `0x67F429` = index 17 | `VA 0x6734D0` |

(cibles obtenues en décodant le `call rel32` des stubs `0x67E3F1`, `0x67E40B`, `0x67E3FE`, `0x67E418` ;
chaque cible commence par un prologue `55 8b ec`.) Le client traite aussi l'id `0`
(`TS_SC_RESULT`) : entrée `0x67F0A0+0 = 0x00` de la table des ids `0…250` → handler `VA 0x66DB80` —
c'est ce qui rend le refus par `TS_SC_RESULT` utile.

Aujourd'hui, dans Navislamia, `700` et `701` ne sont **pas** des membres de `GamePackets`
(`Game/Network/Packets/Enums/GamePackets.cs` compte 102 lignes et saute de `TM_CS_TARGETING = 511` à
`TM_CS_GET_REGION_INFO = 550`) : un client qui ouvre un étal envoie donc une trame que la boucle de
réception ne sait pas reconnaître. **Correction mesurée à l'implémentation** (l'archéologue écrivait
« atteint le `_ => throw new Exception("Unknown Packet Type")` ») : le `throw` du `switch` final
(`GameClient.cs:802` sur la base `ec76b21`) n'est **pas** atteint, parce qu'un garde précède la chaîne —

```csharp
if (!Enum.IsDefined(typeof(GamePackets), header.ID))          // GameClient.cs:626-630
{
    _logger.Debug("Undefined packet ID: {id} ...");
    continue;
}
```

La trame est donc **journalisée en `Debug` puis ignorée** : aucune exception, aucune déconnexion, et
l'étal du joueur reste simplement sans effet. Le `_ => throw` ne peut être atteint que par un membre
**déclaré** dans `GamePackets` mais dépourvu de bras de dispatch — d'où le critère 4 des critères
d'acceptation (enum et dispatch se modifient ensemble). Les conclusions pratiques de la fiche sont
inchangées : ces deux trames doivent devenir des messages reconnus, et le détail de ce que le client
affiche ensuite (fenêtre maintenue ?) reste à observer en direct (§7.5).

---

## 6. Écarts assumés avec NGemity

1. **`TS_ITEM_BASE_INFO` vs `TS_ITEM_FIXED_BASE_INFO`** : NGemity déclare `_(simple)(TS_ITEM_BASE_INFO,
   item)` dans `TS_SC_WATCH_BOOTH.h:8`, rzu `TS_ITEM_FIXED_BASE_INFO` (`TS_SC_WATCH_BOOTH.h:12`). Les
   deux structures sont la même base ; on suit rzu, qui nomme la version « fixed » et porte le gating.
   Aucun impact tant que `703`/`710` ne sont pas implémentés.
2. **Ids non gatés** : NGemity écrit `CREATE_PACKET(TS_CS_START_BOOTH, 700)`
   (`TS_CS_START_BOOTH.h:20`), sans conscience du remap `>= EPIC_9_6_3`. On suit rzu : les ids de cette
   branche sont ceux d'Epic 7.3, et le remap 1700+ ne concerne pas ce dépôt aujourd'hui.
3. **`item_handle` en `uint32_t` (NGemity `:7`) vs `ar_handle_t` (rzu `:6`)** : même largeur (4 octets)
   sur le fil ; on écrit `uint32` comme partout dans `Game/Networking` du dépôt.
4. **NGemity n'a pas de `appearance_code` en 7.3** — mais NGemity compile en `EPIC_4_1_1`
   (`shared/Common/Define.h`) et sa structure d'objet est donc une autre génération. Le client 7.3
   tranche : 75 octets (§3.5). Écart assumé en faveur du client.
5. **`op_codes.md` et `711`** : la table du dépôt liste `711` (`op_codes.md:180`) alors que le client de
   7.3 l'ignore (§1.3). La fiche ne modifie pas la documentation d'op-codes ; la contradiction est
   signalée.

---

## 7. NON ÉTABLI — questions à trancher

1. **Le serveur d'origine répondait-il quelque chose à un `700` accepté ?** Rien dans la famille ne
   l'accuse, et aucun paquet ne peut le transporter (les quatre handlers entrants sont `703`, `708`,
   `709`, `710`). Le socle ne répond rien. Ce qui trancherait : un enregistrement de trafic retail
   (ouverture d'étal) ou la lecture du constructeur/handler de la fenêtre dans un client retail ;
   **décision provisoire : silence**.
2. **Quel `type` (1 ou 2) est « vente » et lequel est « achat » ?** Le client prouve seulement le
   domaine `{1, 2}` (`VA 0x48D5FB` : `setne al` / `inc al`) et que `703` compare `type` à `1`
   (`VA 0x673347`) ; rzu nomme le champ `type`, NGemity n'a ni `BUY_BOOTH` ni `SELL_BOOTH` dans son
   arbre (seulement le commentaire `Messages.cpp:574-577`). `db_string.rdb` porte
   `smsg_booth_notice_selling` et `smsg_booth_notice_buying` sans indice d'ordre. Ce qui trancherait :
   la valeur de `[ebx+0x63]` dans le client pour chaque bouton de la fenêtre, ou un enregistrement
   retail. **Décision provisoire : le socle conserve le `type` reçu sans lui donner de sens** (et
   n'écrit aucun drapeau joueur, cf. 4).
3. **Les handles d'objets de `700` doivent-ils être validés contre l'inventaire ?** Aucune source ne
   dit ce que le serveur d'origine vérifiait (`smsg_booth_cant_equip_item` suggère qu'il refusait les
   objets équipés, donc qu'il consultait l'inventaire). **Décision provisoire : non validés dans le
   socle** — la lecture d'inventaire existe (`ICharacterService.GetItemByHandleAsync`,
   `Game/Services/ICharacterService.cs:34`) mais l'employer ici ouvrirait la dépendance au système
   d'objets que la phase 1 doit éviter. La branche qui rendra les objets visibles (`703`) devra reprendre
   cette validation.
4. **`701` sans étal ouvert** : `Success` idempotent (choix assumé) ou refus `55` ? Aucune source. Ce
   qui trancherait : le comportement retail sur double clic, ou l'observation du client (montre-t-il
   `smsg_booth_notice_close` ?).
5. **Ce que le client fait quand le serveur reste muet.** Question initiale (avant patche) : que se
   passait-il quand la trame n'était pas reconnue. **Tranché par lecture à l'implémentation** : rien,
   la trame était journalisée en `Debug` puis ignorée par le garde `Enum.IsDefined`
   (`GameClient.cs:626-630`), sans exception ni déconnexion (voir §5.4). Ce qui reste ouvert, et qui
   demande un client 7.3 vivant : après le socle, le `700` est lu (ou refusé) sans aucune réponse quand
   il est accepté (§5.3 point 4) — **est-ce que la fenêtre de commerce du client reste ouverte et
   utilisable ?** Ce qui trancherait : ouvrir un étal sur un serveur Navislamia patché et observer la
   fenêtre ; impossible dans cette tâche (aucun exécutable client n'est lancé ici).
6. **Le client lit-il `FLAG_BUY_BOOTH`/`FLAG_SELL_BOOTH` dans le statut du joueur ?** Les drapeaux
   existent côté serveur (`TS_MESSAGE.h:33-34`) et NGemity les aurait posés dans `buildStatus`
   (commentaire), mais rien ne prouve que ce client de 7.3 affiche l'étal d'un autre joueur à partir de
   ces bits. Ce qui trancherait : désassembler le handler de `TS_SC_ENTER_PLAYER` du client, ou un
   enregistrement retail. **Décision provisoire : aucun drapeau joueur n'est écrit, et l'étal n'est
   visible d'aucun client** (le socle ne diffuse rien).
7. **Visibilité de l'étal.** Constat, pas hypothèse : le dépôt n'a **aucune visibilité entre joueurs**
   (`ConnectionInfo` porte `SpawnedNpcs`, `SpawnedMonsters`, `SpawnedProps` — pas de joueurs ;
   `CLAUDE.md:342` : les objets au sol ne sont visibles que de leur tueur ; `MonsterMovementService`
   diffuse à tous, mais ce sont des monstres). Un étal n'est donc visible de personne, pas même de son
   propriétaire : il n'existe que comme état serveur et comme verrou. Rendre l'étal visible — au
   propriétaire seul ou aux joueurs proches — est un travail de visibilité qui n'est pas mérité ici
   (§5.2).
8. **Unité de `gold`** : prix **unitaire** ou total ? `smsg_booth_notice_advice_price` compare le prix
   saisi au « suggested retail price for this item », ce qui suggère un prix unitaire, mais `cnt` et
   `gold` pourraient se lire autrement. Ce qui trancherait : la fenêtre de création (champ
   `window_business_booth_create.nui`) ou un enregistrement retail. Sans impact sur le socle, qui
   stocke les deux champs verbatim ; bloquant pour `705`/`706`/`710`.
9. **Encodage du nom** : le client copie 48 octets bruts (`strncpy`) sans conversion visible. Faut-il
   refuser les octets non-ASCII, ou les conserver ? **Décision provisoire : conserver les octets bruts
   tels quels**, sans traitement d'encodage — le nom n'est pour l'instant jamais renvoyé.
10. **Le plafond de 8 objets est-il le plafond du client ou du serveur ?** `MAX_BOOTH_ITEM_COUNT = 8`
    vient du PDB du serveur d'origine (`CLAUDE.md:1081`) ; le client, lui, construit une trame de
    `59 + 16×N` sans limite visible. **Décision provisoire : 8 est le plafond serveur**, appliqué à la
    réception (`LimitMax`).

---

## 8. Commits épinglés

| dépôt | commit | rôle |
|---|---|---|
| `reference/rzu` (librzu) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02, HEAD) | état du format lu ici ; commit « packets: fix TS_SC_INVENTORY with older epics » qui rend `sockets` inconditionnel pour `version < EPIC_9_8_1` (`TS_SC_INVENTORY.h:88`) |
| `reference/rzu` | `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11) | « packets: use versionned ID for all packets and update their ID with epic 9.6.3 » — origine du gating `700`/`1700`, et dernière écriture de `TS_CS_START_BOOTH.h`, `TS_CS_STOP_BOOTH.h`, `TS_SC_BOOTH_CLOSED.h`, `TS_CS_CHECK_BOOTH_STARTABLE.h` |
| `reference/rzu` | `3b31db1e5aea84565926f6910e2437646c8a3c51` (2023-09-30) | dernière écriture de `TS_CS_WATCH_BOOTH.h`, `TS_CS_STOP_WATCH_BOOTH.h`, `TS_CS_BUY_FROM_BOOTH.h`, `TS_CS_SELL_TO_BOOTH.h`, `TS_CS_GET_BOOTHS_NAME.h`, `TS_SC_GET_BOOTHS_NAME.h`, `TS_SC_WATCH_BOOTH.h`, `TS_SC_BOOTH_TRADE_INFO.h` |
| `reference/ngemity` (Chihiro) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03, HEAD) | arbre de référence pour la logique ; `2f22966e81917a914f0a97fc28de93050764e5b0` (2023-11-01) est la dernière écriture de `shared/Server/Packets/GameClient/TS_CS_START_BOOTH.h` et de `shared/Server/TS_MESSAGE.h` |
| `reference/client73` | `SFrame.exe` `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | client tranchant ; toutes les VA citées sont prises sur ce binaire (base d'image `0x400000`) |
| `reference/client73` | `db_string.rdb` `sha256 4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` | textes d'étal (§2.3) |
| `Navislamia` | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (base `master`) | base de la branche `hermes/packet-socle-booths` |

---

## 9. Note de livraison

* Branche : `hermes/packet-socle-booths`, créée depuis `master` = `ec76b218cd0bd7c6498d725f253abb8b431f0cd6`.
* Aucun fichier de code n'est touché : cette fiche est le seul livrable de la présente tâche
  (`docs/packet-specs/socle-booths.md`). — *L'implémentation du socle a suivi sur la même branche : voir
  le §11 (note du dev), le §12 (`A VERIFIER PAR KILLIAN`) et le §13 (bloc pour `CLAUDE.md`).*
* État de `master` mesuré avant rédaction, dans `/srv/navislamia/Navislamia` avec
  `NUGET_PACKAGES=/srv/navislamia/.nuget-cache` :
  `dotnet build Navislamia.sln -c Debug` → **code de sortie 0**, 0 erreur, 160 avertissements ;
  `dotnet test Tests/Tests.csproj` → **code de sortie 0**, **448 tests passés**, 0 échec, 0 ignoré.
  Le dev doit re-mesurer à la fin de sa tâche et ne jamais descendre sous 366 tests.
* Reproductibilité des lectures client (aucun exécutable du client n'est lancé, lecture seule) :

```bash
cd /srv/navislamia/reference/client73
sha256sum SFrame.exe db_string.rdb
strings -n 4 SFrame.exe | grep -in booth                  # noms TM_*BOOTH*, MSG_BOOTH_*, *_booth_*.nui
strings -n 4 SFrame.exe | grep -c CHECK_BOOTH             # 0 : 711 absent du client
# table id → nom (initialiseur, 0x62 octets par entrée)
objdump -d -M intel --start-address=0x6780c0 --stop-address=0x678100 SFrame.exe
# répartiteur entrant : détection des tables d'octets, puis des cibles
objdump -d -M intel --start-address=0x67df40 --stop-address=0x67df90 SFrame.exe
objdump -s --start-address=0x67f0a0 --stop-address=0x67f19c SFrame.exe   # ids 0…250
objdump -s --start-address=0x67f35c --stop-address=0x67f42a SFrame.exe   # ids 505…710
# constructeurs de trame 700 / 701
objdump -d -M intel --start-address=0x48cbd0 --stop-address=0x48cc60 SFrame.exe
# remplissage de la trame 700 (nom, type, count, objets, longueur, somme, envoi)
objdump -d -M intel --start-address=0x48d5c0 --stop-address=0x48d6b0 SFrame.exe
# handler de 703 : offsets d'en-tête et pas de 83 octets
objdump -d -M intel --start-address=0x6732f0 --stop-address=0x673470 SFrame.exe
# handler de 710 : is_sell @11 et même pas
objdump -d -M intel --start-address=0x6734d0 --stop-address=0x6735c0 SFrame.exe
# textes d'étal
grep -aio "smsg_booth_[a-z_0-9]*" db_string.rdb | sort -u
```

---

## 10. Bloc pour CLAUDE.md

À recopier dans `CLAUDE.md` par Killian (le dev n'écrit pas `CLAUDE.md` ; le bloc part dans la
description de la MR) :

```markdown
## Étal de joueur (socle 700/701)

Le client de 7.3 n'ouvre un étal que par `TM_CS_START_BOOTH` (`700`, `59 + 16×N` octets : en-tête 7,
nom de 49 octets terminé par un nul, `type` 1 ou 2, `count` `uint16`, puis des objets de 16 octets
`item_handle`/`cnt`/`gold int64`) et `TM_CS_STOP_BOOTH` (`701`, 7 octets). Les deux constructeurs de
trame sont dans `SFrame.exe` (`0x48CBD0` et `0x48CC20`) et donnent la taille directement.

`TM_CS_CHECK_BOOTH_STARTABLE` (`711`) **n'existe pas dans le client de 7.3** : ni nom, ni créneau de
dispatch. `op_codes.md:180` le liste pourtant — ne pas s'en servir pour déduire un comportement client.

Pendant qu'un étal est ouvert, le client annonce lui-même que l'équipement/usage d'objets, l'usage de
compétences et l'accès à un autre magasin sont refusés (`smsg_booth_not_*` dans `db_string.rdb`), ce qui
correspond au `ResultCode.NotActableWhileUsingBooth` (`55`) déjà déclaré des deux côtés : le socle
réutilise ce code existant au lieu d'en inventer un.

Les objets de `703`/`710` mesurent **83** octets par enregistrement (stride `0x53` mesuré dans le
client) = 75 + `gold int64`, donc la structure d'objet du client **inclut** le `appearance_code` de 4
octets que rzu gate à `>= EPIC_7_4` ; c'est la même conclusion que les 85 octets de l'inventaire, avec
laquelle elle s'additionne exactement (75 + 2 + 4 + 4 = 85). Ne pas reconstruire les 71/81 octets de rzu.
```

---

## 11. Implémentation du socle — note du dev

La fiche est implémentée telle quelle. Le socle tient en cinq fichiers de code et deux fichiers de
tests, sur la branche `hermes/packet-socle-booths`.

| fichier | rôle |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_START_BOOTH = 700`, `TM_CS_STOP_BOOTH = 701` (commentaire : `711` volontairement absent, §1.3) |
| `Game/Network/Packets/Game/BoothPackets.cs` (nouveau) | offsets, `BoothOpenItem`, `StartBoothRequest`, `TryReadStartBooth`, `TryReadStopBooth` |
| `Game/Services/BoothRules.cs` (nouveau) | `TryAcceptStartBooth`, `ValidateStartBooth`, `IsGuardedAction`, `GateAction` |
| `Game/Network/Clients/ConnectionInfo.cs` | `Booth`, `IsBoothOpen`, `OpenBooth`, `CloseBooth`, `BoothLock` (verrou dédié) |
| `Game/Network/Clients/GameClient.cs` | deux bras de dispatch, **un** garde de verrou en tête de chaîne, `HandleStartBooth` / `HandleStopBooth` |
| `Tests/Game/BoothPacketsTests.cs` (nouveau) | 14 tests d'offsets et de lecture de trame |
| `Tests/Game/BoothRulesTests.cs` (nouveau) | 20 tests de règles, de verrou et d'état |

**Partage lecture / règles.** Le lecteur (`BoothPackets`) ne juge que la **trame** : `Length < 59`,
`count > 8` (`LimitMax`), puis `Length < 59 + 16×count` — tous `InvalidArgument` sauf le plafond. Les
règles de jeu (`BoothRules`) jugent ensuite le `type` ∉ {1, 2}, `count == 0`, la longueur du nom (6 à
40 octets bruts) puis le niveau 10. L'ordre du §5.3 point 2 est donc respecté à l'identique du point de
vue du client, et `N = 0` reste une trame lisible mais refusée en `InvalidArgument` par la règle
« au moins un objet », comme le §3.2 le décrit. Un `700` accepté ne reçoit **aucune** réponse (§5.3
point 4) ; un `700` refusé reçoit `TS_SC_RESULT` avec `request_msg_id = 700` et le code du point 2. Un
`701` dont l'en-tête fait moins de 7 octets est refusé en `InvalidArgument` ; sinon il ferme l'étal et
répond `Success`, même si aucun étal n'était ouvert (choix du §7.4).

**Verrou d'actions.** Un seul garde, placé après le keepalive `TM_NONE` et avant toute la chaîne de
dispatch : `BoothRules.GateAction(ConnectionInfo.IsBoothOpen, header.ID)` répond `55` et coupe la
trame pour les neuf ids du §5.3 point 7. `700` et `701` n'en font pas partie. Conséquence utile à
garder en tête : toute action future doit être pesée contre cette liste — le verrou ne protège que les
neuf ids déclarés, pas « toute action » au sens large.

**Deux décisions prises faute de source** (à déplacer si Killian tranche) :

* un **second `700`** pendant qu'un étal est ouvert **remplace** l'état au lieu d'être refusé : la
  fenêtre de création du client ne peut pas l'envoyer sans avoir fermé la précédente, et aucune source
  ne décrit une forme cumulative ;
* la **taille d'un `701`** est vérifiée (`≥ 7`) et un en-tête tronqué est refusé en `InvalidArgument`,
  là où la fiche ne parlait que de la trame complète de 7 octets.

**Mesures de fin de tâche**, dans `/srv/navislamia/Navislamia` avec
`NUGET_PACKAGES=/srv/navislamia/.nuget-cache` :

| commande | résultat |
|---|---|
| `dotnet build Navislamia.sln -c Debug` | **code de sortie 0**, 0 erreur, 160 avertissements (identiques à `master`) |
| `dotnet test Tests/Tests.csproj` | **code de sortie 0**, **482 tests passés**, 0 échec, 0 ignoré (448 sur `master`, +34) |
| `git log --oneline origin/master..master` | **vide** — aucun commit sur `master` locale |
| `git log --oneline origin/master..hermes/packet-socle-booths` | la fiche (archéologue) puis les commits du dev : implémentation, puis cette note |

---

## 12. A VERIFIER PAR KILLIAN

Rien de ce qui suit n'est bloquant pour le socle : ce sont les points qu'un client 7.3 vivant, un
enregistrement retail ou un arbitrage métier peuvent seuls trancher. Aucun exécutable client n'a été
lancé dans cette tâche (interdit), et aucun serveur de jeu n'a été démarré.

1. **Un `700` accepté sans réponse laisse-t-il la fenêtre de commerce utilisable ?** Le socle ne répond
   rien (§5.3 point 4) et n'envoie aucun paquet de la famille. À observer : ouvrir un étal sur un
   serveur patché, puis tenter d'y déposer un objet — le client doit-il recevoir quelque chose pour
   remplir sa fenêtre ? Si ce n'est pas le cas, la réponse appartient à la branche `703`/`708`.
2. **Sens du `type` 1 / 2** (§7.2) : le socle le conserve verbatim, sans lui donner de sens et sans
   écrire de drapeau joueur. À trancher avant `703`, qui le compare à `1`.
3. **Prix `gold` unitaire ou total** (§7.8) : les triplets sont stockés verbatim. Bloquant pour
   `705`/`706`/`710`.
4. **Validation des handles contre l'inventaire** (§7.3) : volontairement absente du socle. La branche
   `703` devra la reprendre — `smsg_booth_cant_equip_item` suggère que le serveur d'origine refusait
   les objets équipés.
5. **`701` sans étal ouvert** (§7.4) : `Success` idempotent retenu, aucun source ne le fixe. Si le
   retail répond `55`, la ligne à changer est `HandleStopBooth` (`GameClient.cs`) — le test
   `GateAction_RefusesAGuardedActionOnlyBetweenSevenHundredAndSevenHundredOne` devra suivre.
6. **Encodage du nom** (§7.9) : les octets bruts sont conservés et **jamais** renvoyés. Le jour où un
   paquet les renverra (`708`/`709`), il faudra trancher l'encodage — le socle ne le fait pas.
7. **Plafond de 8 objets** (§7.10) : appliqué à la réception (`LimitMax`). Le client de 7.3 sait
   construire plus de 8 enregistrements, mais sa fenêtre n'en propose pas : ce refus est défensif et
   n'a pas pu être déclenché par un vrai client.
8. **Liste du verrou d'actions** : les neuf ids du §5.3 point 7 sont ceux des textes du client
   (`smsg_booth_not_*`) et des actions déjà gérées par la boucle. `smsg_booth_not_use_store` (« accéder
   à un autre magasin ») n'a pas d'objet tant que `702` n'existe pas. Le verrou n'est **pas** une
   protection générique : un paquet d'action ajouté plus tard doit être pesé contre cette liste.
9. **`711` toujours absent du dépôt** : `op_codes.md:180` le liste pourtant (§6.5). Le socle ne le
   déclare pas et la fiche ne modifie pas la table d'op-codes — la contradiction reste signalée.

---

## 13. Bloc à ajouter à `CLAUDE.md` (livré par le dev)

Complément au bloc du §10, à recopier dans `CLAUDE.md` par Killian (le dev n'écrit pas `CLAUDE.md` —
fichier protégé par Hermes ; le bloc part aussi dans la description de la MR) :

```markdown
## Étal de joueur — socle 700/701 implémenté

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
refusées : 200, 201, 203, 204, 208, 218, 219, 253, 400. `700` et `701` sont hors de cette liste. Le
garde n'est pas une protection générique : toute action ajoutée plus tard doit être pesée contre elle.

Le garde `Enum.IsDefined(typeof(GamePackets), header.ID)` (`GameClient.OnDataReceived`) précède la
chaîne : un id **non déclaré** est journalisé en `Debug` puis ignoré, **sans exception**. Le
`_ => throw new Exception("Unknown Packet Type")` du `switch` final n'est donc atteint que par un
membre **déclaré** sans bras de dispatch — c'est la raison exacte du critère « enum et dispatch se
modifient ensemble ».

L'état d'étal n'est ni persisté ni diffusé : aucun joueur ne le voit, pas même son propriétaire, et la
validation des handles contre l'inventaire, le sens du `type` et l'unité du `gold` restent ouverts
(`docs/packet-specs/socle-booths.md` §7 et §12).
```
