# Socle — enchères (hôtel des ventes) — famille `TM_*_AUCTION_*` 1300-1310

| Élément | Valeur |
|---|---|
| Suivi | `navislamia:socle:encheres` |
| Branche | `hermes/packet-socle-encheres` |
| Base | `master` = `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` |
| Version tranchée | Epic 7.3 (`EPIC_7_3` = `0x070300`) |
| Nature | fiche de cadrage (format + prérequis + découpage) — aucun code serveur |

Cette fiche fixe le cadre de la famille **avant** d'implémenter quoi que ce soit : identité des
onze identifiants, format de chaque trame avec sa source, gating de version tranché pour 7.3,
traitement attendu, et le sous-ensemble minimal que le lot de développement doit livrer.

Les sept paquets **client → serveur** de la famille restent parkés : leurs cartes de paquet
rouvriront sur la base de la §3 et de la §6.3.

---

## 1. Identité

`op_codes.md` est une table Lua (`local opcode_names = {`) livrée avec le dépôt ; les dix
identifiants de la famille y figurent aux lignes 202-211.

| id | nom | sens | `op_codes.md` | rzu |
|---|---|---|---|---|
| 1300 | `TM_CS_AUCTION_SEARCH` | client → serveur | :202 | `TS_CS_AUCTION_SEARCH.h:15` |
| 1301 | `TM_SC_AUCTION_SEARCH` | serveur → client | :203 | `TS_SC_AUCTION_SEARCH.h:31` |
| 1302 | `TM_CS_AUCTION_SELLING_LIST` | client → serveur | :204 | `TS_CS_AUCTION_SELLING_LIST.h:11` |
| 1303 | `TM_SC_AUCTION_SELLING_LIST` | serveur → client | :205 | `TS_SC_AUCTION_SELLING_LIST.h:19` |
| 1304 | `TM_CS_AUCTION_BIDDED_LIST` | client → serveur | :206 | `TS_CS_AUCTION_BIDDED_LIST.h:11` |
| 1305 | `TM_SC_AUCTION_BIDDED_LIST` | serveur → client | :207 | `TS_SC_AUCTION_BIDDED_LIST.h:19` |
| 1306 | `TM_CS_AUCTION_BID` | client → serveur | :208 | `TS_CS_AUCTION_BID.h:10` |
| — | *(1307 : aucun nom)* | — | absent | absent |
| 1308 | `TM_CS_AUCTION_INSTANT_PURCHASE` | client → serveur | :209 | `TS_CS_AUCTION_INSTANT_PURCHASE.h:11` |
| 1309 | `TM_CS_AUCTION_REGISTER` | client → serveur | :210 | `TS_CS_AUCTION_REGISTER.h:13` |
| 1310 | `TM_CS_AUCTION_CANCEL` | client → serveur | :211 | `TS_CS_AUCTION_CANCEL.h:9` |

Sources dépôt et références : NGemity déclare exactement les mêmes dix identifiants
(`shared/Server/ClientPackets.h:209-218`), dans le même ordre, avec le même trou en 1307.

**Le sens de chaque paquet est mesuré, pas supposé** : dans le client, la seule table de
dispatch serveur → client couvrant la plage est celle du `switch` à `SFrame.exe` `0x67e67a`
(`sub eax,0x4b1` puis `cmp eax,0x96`, table d'octets `0x67f4c8`, table de sauts `0x67f4b0`), qui
couvre exactement `1201..1351`. Sur ces 151 clés, seules quatre sont traitées et elles
correspondent aux seuls paquets serveur → client : `1201` (`TM_SC_EMOTION`), `1301`, `1303`,
`1305` — tout le reste, y compris `1300`, `1302`, `1304`, `1306`, `1308`, `1309`, `1310`,
retombe sur le chemin « message non traité » (`0x67ef21`). Les trois réponses de la famille sont
atteintes par des stubs dédiés : `1301` → `0x67e6a5` → handler `0x670660` ; `1303` → `0x67e6b2` →
handler `0x6706d0` ; `1305` → `0x67e6bf` → handler `0x670730`.

`1307` est absent des trois références à la fois (`op_codes.md`, rzu, NGemity), et le client ne
construit aucune trame `1307` : dans tout `.text`, les cinq seules occurrences des octets
`17 05 00 00` sont des déplacements de saut (`e9`, `0f 8x`), jamais des identifiants de paquet.
Le trou est assumé, rien à implémenter (voir §8.7).

---

## 2. Ce que le joueur fait

Les fenêtres de l'hôtel des ventes du client sont nommées par leurs ressources d'interface
(`window_system_auction.nui`, `window_system_auction_sub1..4.nui`), et le client passe par un
message interne par action, dont le nom RTTI MSVC identifie l'origine (lecture statique de
`SFrame.exe`, chaînes aux adresses indiquées).

| paquet | action du joueur | fenêtre / procédure | message interne (RTTI) |
|---|---|---|---|
| 1300 | ouvre l'hôtel des ventes et lance/recharge une recherche (page, catégorie, mot-clé, « équipable seulement ») | `SUIAuctionSearchWnd` (`0xc14c20`) | `.?AUSIMSG_REQ_AUCTION_SEARCH@@` (`0xc14bf8`) |
| 1301 | — (réponse affichée dans la liste de résultats) | `SUIAuctionSearchWnd` | `.?AUSIMSG_RES_AUCTION_SEARCH@@` (`0xc1e6fc`) |
| 1302 | ouvre l'onglet « mes ventes en cours » et pagine | `SUIAuctionWnd` (`0xc1ceb8`) | `.?AUSIMSG_REQ_AUCTION_SELLING_LIST@@` (`0xc14878`) |
| 1303 | — (remplit la liste des ventes) | `SUIAuctionWnd` | `.?AUSIMSG_RES_AUCTION_SELLING_LIST@@` (`0xc1e580`) |
| 1304 | ouvre l'onglet « mes enchères » et pagine | `SUIAuctionTenderWnd` (`0xc14d8c`) | `.?AUSIMSG_REQ_AUCTION_BIDDED_LIST@@` (`0xc14d60`) |
| 1305 | — (remplit la liste des enchères posées) | `SUIAuctionTenderWnd` | `.?AUSIMSG_RES_AUCTION_BIDDED_LIST@@` (`0xc1e5b0`) |
| 1306 | saisit un montant et valide (« enchérir ») | `SUIAuctionTenderWnd` | `.?AUSIMSG_REQ_AUCTION_BID@@` (`0xc14bd4`) |
| 1308 | clique « achat immédiat » sur une annonce | `SUIAuctionTenderWnd` | `.?AUSIMSG_REQ_AUCTION_INSTANT_PURCHASE@@` (`0xc14ba0`) |
| 1309 | dépose un objet : choisit l'objet, la quantité, le prix de départ, l'achat immédiat et la durée, puis valide | `SUIAuctionDepositWnd` (`0xc1448c`), `SUIAuctionRegisterWnd` (`0xc14ee4`) | `.?AUSIMSG_REQ_AUCTION_REGISTER@@` (`0xc148f8`) |
| 1310 | retire une de ses annonces (ou récupère l'objet invendu) | `SUIAuctionWnd` / `SUIAuctionDepositWnd` | `.?AUSIMSG_REQ_AUCTION_CANCEL@@` (`0xc148a8`) |

Le client dispose aussi de deux messages internes de liste d'objets stockés
(`.?AUSIMSG_REQ_AUCTION_ITEM_KEEPING_LIST@@` `0xc1426c`, `..._ITEM_KEEPING_TAKE@@` `0xc142a0`)
et d'une légende de statut par entrée (`kui_window_status_caption_property@auction`,
`kui_window_deposit_status_caption_property@auction`,
`kui_window_bidded_status_caption_property@auction`). Le premier point indique que la reprise de
l'objet passe par l'entrepôt du personnage (voir §5.5 et §8.8) ; le second, que les octets
`status`/`flag` de la §3 sont bien des états affichés, pas des résidus.

---

## 3. Structure sur le fil

### 3.1 En-tête commun

Toutes les trames de la famille portent l'en-tête de 7 octets déjà utilisé par le dépôt
(`Game/Network/Packets/Game/GameCharacterPackets.cs:19` `HeaderSize = 7`, écrit par
`CreatePacket` `:382` et `WriteChecksum` `:390`) ; le client écrit et relit exactement la même
chose (octets `0..5` sommés dans l'octet `6` : `SFrame.exe` `0x48dff0` pour 1310).

| offset | taille | type | nom | source |
|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` (total, en-tête compris) | `GameCharacterPackets.cs:382-388` ; client `0x48dcc7` (`mov [ebp-0x34],0x33`) |
| 4 | 2 | `uint16` | `Id` | idem ; client `0x48dcc4`-`0x48dcc7` |
| 6 | 1 | `uint8` | `Checksum` = somme des octets 0..5 | `GameCharacterPackets.cs:390-401` ; client `0x48dff0-0x48dffa` |

Les tailles ci-dessous sont **totales, en-tête compris** : c'est ce que le client compare à
`Length` et ce que `Length` doit valoir.

### 3.2 `TM_CS_AUCTION_SEARCH` (1300) — **51 octets**

Trame construite sur 51 octets par le client (`SFrame.exe` `0x48ca20` : `push 0x33`,
`memset`, `mov eax,0x514`, `mov DWORD PTR [eax],0x33`) puis remplie par le sender `0x48dc80`.

| offset | taille | type | nom (rzu) | valeur observée | source |
|---|---|---|---|---|---|
| 0-6 | 7 | — | en-tête | `Length = 51` | client `0x48dcc7` |
| 7 | 4 | `int32` | `category_id` | — | rzu `TS_CS_AUCTION_SEARCH.h:8` ; client `0x48dc99`, `0x48dca2` (frame+7 ← message+0x13) |
| 11 | 4 | `int32` | `sub_category_id` | — | rzu `:9` ; client `0x48dc9c`, `0x48dca8` (frame+11 ← message+0x17) |
| 15 | 31 | `char[31]` | `keyword` | — | rzu `:10` (`_(string)(keyword, 31)`) ; client `0x48dcb2-0x48dcb9` (`push 0x1f` puis copie vers frame+15) |
| 46 | 4 | `int32` | `page_num` | — | rzu `:11` ; client `0x48dc9f`, `0x48dcab` (frame+46 ← message+0x37) |
| 50 | 1 | `bool` | `is_equipable` | — | rzu `:12` (condition `version >= EPIC_7_2`, voir §4.1) ; client `0x48dcbe` (`mov cl,[esi+0x3b]` → frame+50) |

Total : `7 + 4 + 4 + 31 + 4 + 1 = 51`. Le client comme rzu placent les champs dans cet ordre
exact ; la chaîne est copiée en clair sur 31 octets (pas de longueur préfixée), et le client
garantit le remplissage à zéro (le constructeur `0x48ca20` fait un `memset` des 51 octets avant
remplissage).

### 3.3 `TM_CS_AUCTION_SELLING_LIST` (1302) et `TM_CS_AUCTION_BIDDED_LIST` (1304) — **11 octets**

| offset | taille | type | nom (rzu) | source |
|---|---|---|---|---|
| 0-6 | 7 | — | en-tête (`Length = 11`) | client `0x48dd2b` / `0x48ddbb` (`mov esi,0xb`) |
| 7 | 4 | `int32` | `page_num` | rzu `TS_CS_AUCTION_SELLING_LIST.h:8` et `TS_CS_AUCTION_BIDDED_LIST.h:8` ; client `0x48dd53` (1302) et `0x48dde3` (1304), frame+7 ← message+0x13 |

Les deux trames sont identiques au champ près. Le client les envoie lui-même (constructeurs
`0x48dd10` et `0x48dda0` appelés depuis le dispatch interne `0x49e37d` / `0x49e38a`), donc
`page_num` part bien du client.

### 3.4 `TM_CS_AUCTION_BID` (1306) — **19 octets**

| offset | taille | type | nom (rzu) | source |
|---|---|---|---|---|
| 0-6 | 7 | — | en-tête (`Length = 19`) | client `0x48de56` (`mov [ebp-0x14],0x13`) |
| 7 | 4 | `int32` | `auction_uid` | rzu `TS_CS_AUCTION_BID.h:6` ; client `0x48de4d`, frame+7 ← message+0x13 |
| 11 | 8 | `int64` | `price` | rzu `:7` ; client `0x48de50` et `0x48de53` (frame+11 et frame+15 ← message+0x17 et +0x1b, soit 8 octets) |

Total `7 + 12 = 19` : c'est la seule trame de la famille dont le prix est un entier 64 bits côté
requête.

### 3.5 `TM_CS_AUCTION_INSTANT_PURCHASE` (1308) et `TM_CS_AUCTION_CANCEL` (1310) — **11 octets**

| offset | taille | type | nom (rzu) | source |
|---|---|---|---|---|
| 0-6 | 7 | — | en-tête (`Length = 11`) | client `0x48debb` (1308) / `0x48dfdb` (1310) |
| 7 | 4 | `int32` / `uint32` | `auction_uid` | rzu `TS_CS_AUCTION_INSTANT_PURCHASE.h:8` (`int32_t`) et `TS_CS_AUCTION_CANCEL.h:6` (`uint32_t`) ; client `0x48dee3` (1308), `0x48dfe4`-`0x48e003` (1310), frame+7 ← message+0x13 |

Le désaccord de signe entre les deux références rzu est sans effet sur le fil ; le dépôt retient
`uint32` pour l'identifiant d'annonce (aucun identifiant d'annonce négatif n'existe).

### 3.6 `TM_CS_AUCTION_REGISTER` (1309) — **32 octets**

| offset | taille | type | nom (rzu) | source |
|---|---|---|---|---|
| 0-6 | 7 | — | en-tête (`Length = 32`) | client `0x48df6e` (`mov [ebp-0x20],0x20`) |
| 7 | 4 | `int32` | `item_handle` (`ar_handle_t`) | rzu `TS_CS_AUCTION_REGISTER.h:6` ; client `0x48df4a`, frame+7 ← message+0x13 |
| 11 | 4 | `int32` | `item_count` | rzu `:7` ; client `0x48df50`, frame+11 ← message+0x17 |
| 15 | 8 | `int64` | `start_price` | rzu `:8` ; client `0x48df56` et `0x48df5c` (frame+15 ← message+0x1f, frame+19 ← message+0x23) |
| 23 | 8 | `int64` | `instant_purchase_price` | rzu `:9` ; client `0x48df65` et `0x48df6b` (frame+23 ← message+0x27, frame+27 ← message+0x2b) |
| 31 | 1 | `uint8` | `duration_type` | rzu `:10` ; client `0x48df62`, `0x48df68` (octet frame+31 ← message+0x2f) |

Total `7 + 25 = 32`. Le message interne du client réserve 4 octets de plus entre `item_count` et
`start_price` (le sender saute `message+0x1b`), ce qui n'affecte pas la trame.

`item_handle` est l'identifiant d'objet de l'inventaire (le dépôt utilise `ItemEntity.Id` comme
`handle` dans `GameCharacterPackets.WriteInventoryItem:342-344`) ; `duration_type` est la seule
valeur de la famille dont l'énumération n'est pas établie (voir §8.4).

### 3.7 `item_info` — le motif d'objet de 75 octets

C'est le point dur de la famille : `item_info` est le motif d'objet **de base**, sans les trois
champs de position que l'inventaire ajoute.

rzu le nomme `TS_ITEM_FIXED_INFO` (`TS_SC_AUCTION_SEARCH.h:10`), NGemity `TS_ITEM_BASE_INFO`
(`TS_SC_AUCTION_SEARCH.h:9`). Pour 7.3, rzu calcule 71 octets : les trois champs de position de
`TS_ITEM_FIXED_INFO` ne sont émis que par les `_(impl)` de condition `version >= EPIC_9_8_1`
(`TS_SC_INVENTORY.h:109-111`) — les `_(def)` des lignes 104-106 n'émettent rien par eux-mêmes.
C'est le même mécanisme qui place `wear_position` **après** le motif de base dans
`TS_ITEM_DYNAMIC_INFO` (`:126-128`, condition `version < EPIC_9_8_1`), mécanisme déjà prouvé par
l'enregistrement d'inventaire de 85 octets du dépôt (`GameCharacterPackets.cs:21`) où
`appearance_code` (offset 71) précède immédiatement `wear_position` (offset 75).

**Le client lit 75 octets**, pas 71 : mesuré deux fois, indépendamment.

1. Handler `1301` (`SFrame.exe` `0x670660`) : après les trois entiers de tête, il copie
   `push 0x1400` (= 5120) octets par `memcpy` (`0x9767c0`) depuis la trame + 19 vers l'objet
   interne + 31 (`0x670697`, `0x67069c`, `0x6706a0`, `0x6706a7`), **sans jamais regarder**
   `auction_info_count`. 5120 / 40 = **128** octets par entrée, et
   `128 - 4 - 1 - 8 - 8 - 31 - 1 = 75`.
2. Handler `1303` (`0x6706d0`) — et `1305` (`0x670730`), identique : `mov ecx,0x3ca` puis
   `rep movs DWORD` (`0x670712`, `0x67071a`) = 970 × 4 = 3880 octets. 3880 / 40 = **97** = 75 + 4
   + 1 + 8 + 8 + 1.

Le même motif de 75 octets se retrouve dans une trame **client → serveur** sans rapport avec les
enchères : pour `705` (`TS_CS_BUY_FROM_BOOTH`, `rzu TS_CS_BUY_FROM_BOOTH.h:11`
`_(dynarray)(TS_ITEM_FIXED_INFO, items)`), le client copie 18 `dword` + 1 `word` + 1 `byte`
(= 75 octets) par objet, avec un pas de destination valant `0x4b` = 75 et un pas de source de
`0x53` = 83 (`SFrame.exe` `0x48e684-0x48e79a`). `TS_ITEM_FIXED_INFO` vaut donc **75 octets** dans
ce client, à l'identique de l'`item_info` des enchères.

Disposition du motif (offsets relatifs à `item_info`) :

| offset | taille | type | nom (rzu) | source rzu | source dépôt (motif de 85) |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `handle` | `TS_SC_INVENTORY.h:68` | `GameCharacterPackets.cs:342` |
| 4 | 4 | `int32` | `code` | `:69` | `:343` |
| 8 | 8 | `int64` | `uid` | `:70` | `:344` |
| 16 | 8 | `int64` | `count` | `:72` (`version >= EPIC_4_1_1`) | `:345` |
| 24 | 4 | `int32` | `ethereal_durability` | `:75` (`>= EPIC_6_3 && < EPIC_9_8_1`) | `:346` |
| 28 | 4 | `uint32` | `endurance` | `:77` (`>= EPIC_4_1 && < EPIC_9_8_1`) | `:347` |
| 32 | 1 | `uint8` | `enhance` | `:78` | `:348` |
| 33 | 1 | `uint8` | `level` | `:79` | `:349` |
| 34 | 4 | `uint32` | `flag` | `:87` (`< EPIC_9_8_1`) | `:350` |
| 38 | 16 | `int32[4]` | `sockets.socket` | `:88` → `:8` | `:352-355` |
| 54 | 4 | `int32` | `remain_time` | `:91` (`< EPIC_9_8_1`) | `:357` |
| 58 | 1 | `uint8` | `elemental_effect.type` | `:92` → `:46` (`version < EPIC_9_8_1`) | `:359` |
| 59 | 4 | `int32` | `elemental_effect.remain_time` | `:47` | *(non écrit — reste à 0)* |
| 63 | 4 | `int32` | `elemental_effect.attack_point` | `:48` | `:360` |
| 67 | 4 | `int32` | `elemental_effect.magic_point` | `:49` | `:361` |
| 71 | 4 | `int32` | `appearance_code` | `:93` (`>= EPIC_7_4 && < EPIC_9_8_1`) → **gardé, voir §4.6** | `:362` (valeur 0) |
| **75** | | | **fin de `item_info`** | | |

Les offsets 75 à 84 du motif d'inventaire (`wear_position` `:363`, `own_summon_handle` `:364`,
`index` `:365`) **ne font pas partie** de `item_info`.

### 3.8 `TM_SC_AUCTION_SEARCH` (1301) — **5139 octets**

| offset | taille | type | nom (rzu) | source |
|---|---|---|---|---|
| 0-6 | 7 | — | en-tête (`Length = 5139`) | client `0x670660-0x6706b9` (5120 = 40 × 128) |
| 7 | 4 | `int32` | `page_num` | rzu `TS_SC_AUCTION_SEARCH.h:25` ; client `0x670688`, objet+0x13 (l'écho du `page_num` demandé) |
| 11 | 4 | `int32` | `total_page_count` | rzu `:26` ; client `0x67068e`, objet+0x17 |
| 15 | 4 | `int32` | `auction_info_count` | rzu `:27` ; client `0x670694`, objet+0x1b |
| 19 | 5120 | `TS_SEARCHED_AUCTION_INFO[40]` | `auction_info` | rzu `:28` (`_(array)(..., 40)`) ; client `0x670697-0x6706a7` (copie inconditionnelle de 5120 octets) |

Total : `7 + 12 + 5120 = 5139`.

Une entrée `TS_SEARCHED_AUCTION_INFO` = `TS_AUCTION_INFO` (96 octets) + `seller_name` (31) +
`flag` (1) = **128 octets** :

| offset | taille | type | nom (rzu) | source |
|---|---|---|---|---|
| 0 | 4 | `int32` | `auction_uid` | rzu `:9` |
| 4 | 75 | — | `item_info` | rzu `:10` → §3.7 |
| 79 | 1 | `uint8` | `duration_type` | rzu `:11` |
| 80 | 8 | `uint64` | `bidded_price` | rzu `:12` |
| 88 | 8 | `uint64` | `instant_purchase_price` | rzu `:13` |
| 96 | 31 | `char[31]` | `seller_name` | rzu `:19` (`_(string)(seller_name, 31)`) |
| 127 | 1 | `uint8` | `flag` | rzu `:20` → §8.1 |

### 3.9 `TM_SC_AUCTION_SELLING_LIST` (1303) et `TM_SC_AUCTION_BIDDED_LIST` (1305) — **3899 octets chacun**

| offset | taille | type | nom (rzu) | source |
|---|---|---|---|---|
| 0-6 | 7 | — | en-tête (`Length = 3899`) | client `0x6706d0-0x670720` et `0x670730-0x670789` (3880 = 40 × 97) |
| 7 | 4 | `int32` | `page_num` | rzu `TS_SC_AUCTION_SELLING_LIST.h:13` / `TS_SC_AUCTION_BIDDED_LIST.h:13` ; client objet+0x13 |
| 11 | 4 | `int32` | `total_page_count` | rzu `:14` ; client objet+0x17 |
| 15 | 4 | `int32` | `auction_info_count` | rzu `:15` ; client objet+0x1b |
| 19 | 3880 | `[40]` | `auction_info` | rzu `:16` ; client `0x670712`/`0x67071a` (970 `dword`) |

Total : `7 + 12 + 3880 = 3899`. Entrée = `TS_AUCTION_INFO` (96) + `status` (1) = **97 octets**,
le nom du type d'entrée différant seul entre les deux paquets
(`TS_REGISTERED_AUCTION_INFO` `TS_SC_AUCTION_SELLING_LIST.h:6-9` vs
`TS_BIDDED_AUCTION_LIST` `TS_SC_AUCTION_BIDDED_LIST.h:6-9`).

### 3.10 Récapitulatif des tailles, et l'écart rzu/client

| id | charge utile | total client | total recalculé depuis rzu à 7.3 | écart |
|---|---|---|---|---|
| 1300 | 44 | 51 | 51 | 0 |
| 1301 | 5132 | **5139** | 4979 | +160 (4 × 40) |
| 1302 | 4 | 11 | 11 | 0 |
| 1303 | 3892 | **3899** | 3739 | +160 (4 × 40) |
| 1304 | 4 | 11 | 11 | 0 |
| 1305 | 3892 | **3899** | 3739 | +160 (4 × 40) |
| 1306 | 12 | 19 | 19 | 0 |
| 1308 | 4 | 11 | 11 | 0 |
| 1309 | 25 | 32 | 32 | 0 |
| 1310 | 4 | 11 | 11 | 0 |

L'écart des trois réponses est exactement `4 × 40` : c'est `appearance_code`, que rzu gate à
`>= EPIC_7_4` alors que ce client l'écrit et le lit. Décision §4.6. Toutes les autres tailles
coïncident au bit près entre les deux références et le client : c'est la meilleure preuve
disponible que la lecture des `_(impl)` est la bonne.

---

## 4. Gating de version, tranché pour Epic 7.3

`EPIC_7_2 = 0x070200`, `EPIC_7_3 = 0x070300`, `EPIC_7_4 = 0x070400`, `EPIC_9_6_3 = 0x090603`
(`rzu librzu/src/lib/Packet/PacketEpics.h:58,59,60,96`). À 7.3, les identifiants de la famille
gardent donc leurs valeurs « basses » et non les valeurs `2xxx` introduites à 9.6.3.

| # | champ | condition rzu | décision 7.3 | source |
|---|---|---|---|---|
| 4.1 | `1300.is_equipable` | `version >= EPIC_7_2` | **PRÉSENT**, 1 octet à l'offset 50 | `TS_CS_AUCTION_SEARCH.h:12` ; `0x070300 >= 0x070200` ; confirmé par la taille de trame 51 côté client (`0x48ca20`) |
| 4.2 | identifiants | `version < EPIC_9_6_3` | **1300, 1301, 1302, 1303, 1304, 1305, 1306, 1308, 1309, 1310** (pas `23xx`) | `TS_CS_AUCTION_SEARCH.h:15-16` et les `_ID` des neuf autres en-têtes ; NGemity identique `ClientPackets.h:209-218` |
| 4.3 | `item_info.count` | `>= EPIC_4_1_1` → `int64` ; `< EPIC_4_1_1` → `int32` | **`int64`** (8 octets) | `TS_SC_INVENTORY.h:71-72` |
| 4.4 | `item_info.endurance` | `>= EPIC_4_1` → `uint32` ; `< EPIC_4_1` → `uint16` | **`uint32`** (4 octets) | `:76-77` |
| 4.5 | `item_info.ethereal_durability` | `>= EPIC_6_3 && < EPIC_9_8_1` | **PRÉSENT**, `int32` | `:75` |
| 4.6 | `item_info.appearance_code` | `>= EPIC_7_4 && < EPIC_9_8_1` | **PRÉSENT malgré le gating**, `int32` à `item_info+71` | `:93` contredit par la mesure client : pas d'entrée de 128/97 octets sans ces 4 octets (§3.7, §3.10 locale) |
| 4.7 | `item_info.elemental_effect` | `version >= EPIC_6_1` | **PRÉSENT**, 13 octets (`uint8 type`, 3 × `int32`) | `:92` → `:46-49` (`version < EPIC_9_8_1`) |
| 4.8 | `TS_ITEM_SOCKETS.dummy_socket` | `version < EPIC_6_1` | **ABSENT** à 7.3 (sockets = 16 octets, pas 24) | `:9` |
| 4.9 | `item_info.sockets` | `version < EPIC_9_8_1 || base_info2_condition` ; `TS_ITEM_FIXED_BASE_INFO` passe `true` | **PRÉSENT** | `:88`, `:99` |
| 4.10 | `TS_ITEM_FIXED_INFO.wear_position` / `own_summon_handle` / `index` | `version >= EPIC_9_8_1` uniquement | **ABSENTS** : `item_info` s'arrête à 75 octets | `:104-106`, `:109-111` (à comparer à `:126-128`, qui les ajoutent bien pour `< EPIC_9_8_1` dans `TS_ITEM_DYNAMIC_INFO`) |
| 4.11 | `enhance_chance` | `>= EPIC_9_2` | **ABSENT** | `:80` |
| 4.12 | `awaken_option` | `>= EPIC_8_1` | **ABSENT** | `:89` |
| 4.13 | `random_stats` | `>= EPIC_8_2` | **ABSENT** | `:90` |
| 4.14 | `summon_code`, `item_effect_id`, `luciad_power_level`, et les re-typages `>= EPIC_9_8_1` (`flag`, `remain_time`, `endurance`, `ethereal_durability`, `index` en `int16`) | `>= 9_8_1` (ou `9_6_1`, `8_2`) | **ABSENTS** | `:81-86`, `:94-95` |

Aucun champ de la famille n'a de gating non statué : les dix trames sont entièrement déterminées
pour 7.3.

---

## 5. Traitement attendu

### 5.1 NGemity ne traite aucun de ces paquets

C'est le constat qui structure tout le reste : NGemity **déclare** les dix trames mais n'en
**traite** aucune.

* Aucun handler `TS_CS_AUCTION_*` : le seul `grep` qui remonte dans `Chihiro/` est
  `Item.cpp:91,127` (colonne `auction_id` de l'instance d'objet) et `ItemInstance.h:36,59,81,103`
  (`m_nAuctionID`) — c'est-à-dire un champ persisté, pas un service.
* `WL_SpecLocId::SecRouteAuction = 130107` est **déclaré et jamais utilisé**
  (`Chihiro/src/Map/WorldLocation.h:50`, aucune autre occurrence dans `Chihiro/`, `shared/` ou
  `Mononoke/`). Il correspond à la colonne `secroute_apply` de `AutoAuctionResource` : le pendant
  NGemity de l'« NPC vendeur d'enchères » existe sous forme de constante orpheline.
* Aucune classe de ressource d'enchère (`AuctionCateryResource` ou équivalent) n'existe.

En conséquence : **la logique de l'hôtel des ventes ne peut pas être portée depuis NGemity**, il
n'y a rien à porter. Le modèle du dépôt (§5.5) et le client sont les deux seules sources.

### 5.2 La contrainte non négociable des quarante emplacements

Les trois réponses copient leur tableau en bloc, sans consulter `auction_info_count` :
5120 octets pour 1301 (`0x670697`), 3880 octets pour 1303 (`0x670712`) et 1305. Le serveur doit
donc **toujours** écrire les 40 emplacements, même pour une page incomplète : `auction_info_count`
dit au client combien d'entrées afficher, mais la taille de la trame et le nombre d'emplacements
copiés sont fixes. Une trame courte de 12 + `n × 128` octets est un désalignement silencieux.

Les entrées au-delà de `auction_info_count` doivent être des zéros sur toute la longueur d'entrée
(128 ou 97 octets), et non des résidus de tampon.

### 5.3 Réponses attendues, paquet par paquet

| requête | réponse | id, taille | contenu minimal |
|---|---|---|---|
| 1300 | 1301 | 1301, 5139 | `page_num` = écho de la requête ; `total_page_count` ; `auction_info_count` ≤ 40 ; 40 entrées `TS_SEARCHED_AUCTION_INFO` zéro-remplies au-delà |
| 1302 | 1303 | 1303, 3899 | idem, entrées = annonces du personnage (`status`) |
| 1304 | 1305 | 1305, 3899 | idem, entrées = annonces où le personnage a enchéri (`status`) |
| 1306 | aucun paquet dédié | — | le changement d'état est notifié par les listes et par le retour de la mise à jour ; aucun `TS_SC_AUCTION_*` n'existe pour l'accusé de réception |
| 1308 | aucun paquet dédié | — | idem |
| 1309 | aucun paquet dédié | — | idem |
| 1310 | aucun paquet dédié | — | idem |

Autrement dit, la famille ne contient **que trois paquets serveur → client** : toute réponse de
refus passe par l'accusé de réception générique du dépôt (`GameClient.SendResult`
`Game/Network/Clients/GameClient.cs:56`, trame `TM_SC_RESULT = 0` `GamePackets.cs:5`, contenu
`id` + `result` + `value`) avec un `ResultCode`, jamais par une trame `TM_SC_AUCTION_*`
d'erreur : aucun de ces identifiants n'existe dans les références ni dans le client. La trame
1300 n'a pas non plus de contrepartie d'erreur dédiée.

### 5.4 Une annonce change d'état sans notification dédiée

Le client ne dispose que de trois handlers serveur → client pour toute la famille. Un flash de
liste se fait donc par un nouvel envoi de 1301/1303/1305 (`page_num` réémis), pas par une trame
de mise à jour. C'est cohérent avec les messages internes du client (§2), qui ne consomment que
ces trois listes.

### 5.5 Modèle déjà présent dans le dépôt

Ces éléments existent et doivent être **utilisés, pas réinventés** :

| élément | emplacement | remarque |
|---|---|---|
| `AuctionEntity` | `Game/DataAccess/Entities/Telecaster/AuctionEntity.cs:5-36` | `ItemId`, `SellerId`, `SellerName`, `IsHiddenVillageOnly:19`, `EndTime:20`, `InstantPurchasePrice:21`, `RegistrationTax:22`, `BiddersIds:23`, `HighestBiddingPrice:24`, `HighestBidderId:26`, `HighestBidderName:33`, `ItemStorage:35` |
| `ItemStorageEntity.RelatedAuctionId` | `.../ItemStorageEntity.cs:20-21` | conteneur de l'enchère ; FK déclarée `TelecasterContext.cs:139-142` |
| `StorageType` | `.../Enums/StorageType.cs:7-16` | les neuf motifs de la famille sont déjà là : `ItemBySuccessfulBid = 1`, `ItemByInstantPurchase = 2`, `ItemByExpiration = 3`, `ItemByCancel = 4`, `GoldByItemSell = 30`, `GoldByRegTax = 31`, `GoldByHigherBid = 32`, `GoldByCancel = 33`, `GoldByItemSoldOut = 34` |
| table `AuctionCateryResource` | `ArcadiaSchemaPSQL.sql:1-9` | `catery_id`, `sub_catery_id`, `name_id`, `local_flag`, `item_group`, `item_class` — **aucune entité ni repository ne la lit** |
| table `AutoAuctionResource` | `ArcadiaSchemaPSQL.sql:11-23` | vendeur automatique d'PNJ (`auctionseller_id`, `secroute_apply`, `auction_enrollment_time`, `repeat_apply`, `repeat_term`, `auctiontime_type`) — hors socle |
| schéma de base `Auctions` | `TelecasterContext.cs:119` `ConfigureAuctions` | pas de table `Auctions` dans `ArcadiaSchemaPSQL.sql`, qui est bien le schéma **Arcadia** |

Côté client, la ressource de catégories existe aussi et porte le même nom :
`/srv/navislamia/reference/client73/db_auctioncategoryresource.rdb`, 1044 octets. Son contenu
n'a pas été décodé (format RDB) ; la table serveur de `ArcadiaSchemaPSQL.sql:1-9` en est le
pendant lisible (voir §8.5).

---

## 6. Découpage du socle

### 6.1 Ce que le lot livrera

**S1. Le facteur commun de sérialisation `item_info` (75 octets).** Le motif est aujourd'hui
écrit en ligne dans `GameCharacterPackets.WriteInventoryItem` (`GameCharacterPackets.cs:341-365`).
Il doit devenir un writer réutilisable (même projet, fichier dédié), avec les 16 offsets de la
§3.7 et la taille 75 testés. L'inventaire `207` doit continuer à produire 85 = 75 + 10
(`wear_position` 75, `own_summon_handle` 77, `index` 81), donc aucun changement visible pour
l'existant. C'est le vrai prérequis de toute la famille : sans lui, chaque carte de paquet
réécrirait seize champs et le désaccord `appearance_code` (4.6) se propagerait.

**S2. Les trois trames serveur → client** `1301` (5139), `1303` (3899), `1305` (3899) :
constructeurs purs + membres `GamePackets` + tests d'offsets et de taille, **40 emplacements
toujours écrits** (§5.2). Le socle les livre avec une page vide (`auction_info_count = 0`,
40 × zéro) : le client affiche une liste vide sans désalignement, et la population des entrées
revient aux cartes métier. Aucune décision de gameplay n'est nécessaire pour cela.

Ces trois trames ne font pas partie des sept cartes de paquet différées (`1300`, `1302`, `1304`,
`1306`, `1308`, `1309`, `1310`) : elles n'ont aujourd'hui aucun producteur côté serveur et
constituent, avec S1, le squelette de réponse de la famille.

**S3. Le chargement des catégories** : `AuctionCateryResourceEntity` + `DbSet` dans
`ArcadiaContext` + repository/interface, sur le modèle exact de `ArcadiaSchemaPSQL.sql:1-9` et
selon le patron des dix-neuf ressources déjà branchées (`ArcadiaContext.cs:11-30`). Les valeurs
sémantiques des colonnes restent à vérifier (§8.5) : l'entité les expose, elle ne les interprète
pas.

**S4. Rien à ajouter au modèle d'enchère** : `AuctionEntity`, `ItemStorageEntity.RelatedAuctionId`
et les neuf `StorageType` existent déjà (§5.5). Le socle les référence et documente leur usage ;
il ne les modifie pas.

### 6.2 Ce que ce lot ne fait pas

* **Les sept paquets client → serveur** (`1300`, `1302`, `1304`, `1306`, `1308`, `1309`, `1310`) :
  leurs cartes restent parkées. Motif mesurable, au-delà du cadrage : le dispatch entrant du
  dépôt se termine par le `switch` de `Game/Network/Clients/GameClient.cs:792-802`, dont le bras
  par défaut lève `Exception("Unknown Packet Type")` — c'est exactement le critère « aucun membre
  de `GamePackets` ne peut atteindre le `switch` final ». Sept paquets métier s'ajoutent donc à
  `GamePackets` en même temps que leur branche, selon le patron déjà utilisé par les paquets
  d'objets (`if (header.ID == ...)` au-dessus du `switch`, `GameClient.cs:328-414`). Leur format
  est en revanche entièrement fixé ici (§3.2 à §3.6), ce qui est la raison d'être de cette fiche.
* **Les décisions de gameplay** : barème de taxe d'inscription, durées par `duration_type`,
  visibilité « village caché », prix plancher, règle d'expiration, nombre maximal d'annonces par
  personnage, contenu des `ResultCode` de refus. Toutes sont listées en §9 : elles appartiennent à
  Killian.
* **Le vendeur automatique par PNJ** (`AutoAuctionResource:11-23`) : dépend de `SecRouteAuction`,
  d'une horloge de réapprovisionnement et de règles de tirage. Hors socle.

### 6.3 Prérequis par carte, dans l'ordre de reprise conseillé

| ordre | carte | prérequis | décision restante |
|---|---|---|---|
| 1 | 1300 `SEARCH` | S1 (item_info absent de la requête mais nécessaire à la réponse), S2, S3 | sémantique des catégories ; filtre « équipable seulement » |
| 2 | 1302 / 1304 `*_LIST` | S2 (pagination partagée) | définition d'« annonce du personnage » et de « annonce où j'ai enchéri » |
| 3 | 1309 `REGISTER` | S1 (l'objet déposé sort d'un conteneur d'objet), S3 | barème de taxe, durées, `IsHiddenVillageOnly`, `duration_type` |
| 4 | 1306 `BID` | 1309 (une annonce doit exister) | pas de surenchère sur soi-même, montant minimal, prix de réserve |
| 5 | 1308 `INSTANT_PURCHASE` | 1309 | devenir du « premier enchérisseur » face à l'achat immédiat |
| 6 | 1310 `CANCEL` | 1309 | remise de l'objet (entrepôt) et de la taxe |
| — | 1301 / 1303 / 1305 | **livrés par le socle (S2)** | contenu des listes seulement |

### 6.4 Cas « pas de code » du socle

Aucun : S1, S2 et S3 exigent tous du code. Le socle ne peut pas être « documentaire ».

---

## 7. Écarts assumés avec NGemity

1. **NGemity n'implémente pas la famille** (§5.1) : aucun handler, aucune ressource, aucune
   mécanique. Le fait de s'en écarter n'est donc pas un choix, c'est une absence — et cela
   signifie que la validation ne pourra venir que du client, dont toutes les mesures de cette
   fiche proviennent.
2. **Nom du motif d'objet** : NGemity écrit `TS_ITEM_BASE_INFO` dans `TS_SC_AUCTION_SEARCH.h:9`
   (défini `TS_SC_INVENTORY.h:11-39`) là où rzu écrit `TS_ITEM_FIXED_INFO`
   (`TS_SC_AUCTION_SEARCH.h:10`). Le contenu est le même, et à 7.3 le calcul de rzu coïncide avec
   la mesure client : les deux références désignent la même chose. **On garde le nom rzu**
   (`TS_ITEM_FIXED_INFO`) pour se conformer au format de référence, en documentant l'équivalence.
3. **NGemity n'a pas de variante « position » séparée** : `TS_ITEM_INFO` y porte toujours
   `wear_position`, `own_summon_handle` et `index` (`TS_SC_INVENTORY.h:41-46`), alors que rzu les
   isole dans `TS_ITEM_DYNAMIC_INFO` (`:116-128`). Sur le fil, les deux donnent le même
   enregistrement d'inventaire ; la distinction ne compte que parce qu'elle explique pourquoi
   `item_info` de l'enchère s'arrête à 75 octets (§4.10).
4. **Les tailles NGemity ne sont pas celles de 7.3** : NGemity compile `EPIC_4_1_1`
   (`shared/Common/Define.h:25`), donc sa condition `version >= EPIC_7_2` sur `is_equipable`
   (`TS_CS_AUCTION_SEARCH.h:11`) est **fausse** et son `item_info` vaut 62 octets (pas de
   `ethereal_durability`, `dummy_socket` présent, pas d'`appearance_code` ni d'effet élémentaire).
   Ne jamais recopier une taille NGemity pour cette famille : s'en servir uniquement pour
   vérifier l'**ordre** des champs, qui coïncide avec rzu.
5. **Correction d'une fiche voisine** : `docs/packet-specs/socle-booths.md` annonce
   `705 = 13 + 85 × N` et un `TS_ITEM_FIXED_INFO` de 85 octets. La mesure directe du client donne
   75 octets par objet pour `705` (`SFrame.exe` `0x48e684-0x48e79a` : 18 `dword` + 1 `word` +
   1 `byte`, pas de destination `0x4b` = 75, pas de source `0x53` = 83), soit `13 + 75 × N`.
   Le motif de 75 octets est confirmé par les deux handlers d'enchères (128 et 97 octets par
   entrée). La fiche des loges doit être reprise dans un lot distinct ; la présente fiche fait foi
   pour `item_info`.

---

## 8. NON ÉTABLI

1. **`flag` de `TS_SEARCHED_AUCTION_INFO`** (octet 127 de chaque entrée de 1301), sémantique et
   valeurs. Le client l'affiche : légende `kui_window_status_caption_property@auction` alimentée
   par une ressource `StatusInfo` (le patron RTTI est visible sur la variante « dépôt »,
   `.?AU?$Resource@UStatusInfo@kui_window_deposit_status_caption_property@auction@@@property@sui@@`
   à `SFrame.exe` `0xc14628`) : la correspondance valeur → texte est dans les données d'interface,
   pas dans le binaire. *Question : quelle valeur pour « en vente », « achetée », « expirée »,
   « retirée » ?*
2. **`status` des entrées de 1303 et 1305** (octet 96), même question, avec **deux** légendes
   distinctes côté client (`deposit_status` pour les ventes, `bidded_status` pour les enchères) :
   les deux énumérations peuvent différer.
3. **Valeur d'`appearance_code`** : la source du contenu n'est pas établie. Le dépôt écrit `0`
   pour l'inventaire (`GameCharacterPackets.cs:362`). *Question : `0` suffit-il pour 7.3, le champ
   étant apparu à 7.4 côté rzu ?*
4. **`duration_type`** (octet 31 de 1309) : énumération et durées associées inconnues. Le modèle
   du dépôt suggère trois paliers (`AuctionEntity.cs:22` : « short, mid, longterm ») et
   `AutoAuctionResource.auctiontime_type` est un `smallint`. *Question : quelles valeurs le client
   émet-il, et quelle durée chacune vaut-elle ?*
5. **Colonnes de `AuctionCateryResource`** : signification de `local_flag`, `item_group`,
   `item_class`, et destination de `name_id`. *Question : `name_id` indexe-t-il `StringResource`,
   et `local_flag` reprend-il `AutoAuctionResource.local_flag` ?*
6. **`total_page_count`** : la règle de calcul (40 par page) et un éventuel plafond ne figurent
   nulle part dans le client, qui se contente d'afficher le compte. *Question : existe-t-il un
   maximum d'annonces par personnage ou par serveur ?*
7. **1307** : trou dans `op_codes.md`, absent de rzu et de NGemity, aucune trame construite par le
   client — les cinq occurrences des octets `17 05 00 00` dans `.text` sont des déplacements de
   saut, et aucun `mov eax,0x517` ni écriture du `word` `0x517` n'existe. *À confirmer comme trou
   définitif.*
8. **`IsHiddenVillageOnly`** : le champ existe (`AuctionEntity.cs:19`) et est annoté « check usage
   -> leftover of premium "vip" content ». *Question : filtre-t-il la visibilité des annonces, et
   selon quel critère ?*
9. **Contenu de `db_auctioncategoryresource.rdb`** (1044 octets) : non décodé, l'énoncé de format
   RDB n'étant pas fixé ici. La table serveur `ArcadiaSchemaPSQL.sql:1-9` fournit le modèle
   lisible, mais rien ne prouve que les deux jeux de catégories coïncident.

---

## 9. A VERIFIER PAR KILLIAN

| # | question | ce qui est bloqué sans réponse |
|---|---|---|
| 1 | Barème de la taxe d'inscription par durée (et qui la perçoit) | remplissage de `RegistrationTax`, `GoldByRegTax`, prix net versé au vendeur |
| 2 | Valeurs de `duration_type` et durées associées (§8.4) | 1309, donc 1306, 1308 et l'expiration |
| 3 | Sémantique et valeurs de `flag` et `status` (§8.1, §8.2) | contenu des trois réponses 1301/1303/1305 |
| 4 | Règle de visibilité « village caché » (§8.8) | filtrage des annonces dans 1301 |
| 5 | Plafond d'annonces et règle de pagination (§8.6) | `total_page_count` |
| 6 | `appearance_code` : écrire 0 ou fournir la vraie valeur (§8.3) | contenu des entrées |
| 7 | Signification des colonnes de catégorie (§8.5) | recherche par catégorie de 1300 |
| 8 | Sort de l'objet invendu ou rendu : entrepôt du personnage ou inventaire direct ? | 1310, 1303, et la reprise via `AUSIMSG_REQ_AUCTION_ITEM_KEEPING_LIST` |
| 9 | La correction de `socle-booths.md` (§7.5) doit-elle donner lieu à une carte de correction ? | cohérence entre les fiches |
| 10 | `item_info.flag` d'un objet sans flag part en `0xFFFFFFFF` (suite de `ItemFlag.None = -1`, §12.3.1) : écrire `0`, ou corriger la conversion ? | contenu des trois réponses, comme la question 3 |
| 11 | Nom de table retenu pour `AuctionCateryResource` (convention EF `AuctionCateryResources` vs `AuctionCateryResource` du SQL) et voie de provisionnement (§12.3.3) | la requête de catégories lira la mauvaise table si les deux diffèrent |
| 12 | La paire `(catery_id, sub_catery_id)` est-elle unique dans la table réelle ? (§12.3.2) | suivi d'entités de `AuctionCateryResourceRepository` |

Les constats de la carte Trello `UFWEvY0q` ont tous été revérifiés dans les références locales
(§7.5 pour le seul écart relevé) : le champ et le gating `version >= EPIC_7_2` de
`TS_CS_AUCTION_SEARCH` (`rzu TS_CS_AUCTION_SEARCH.h:12`), les trois entiers de tête et le tableau
de 40 entrées de `1301` (`rzu TS_SC_AUCTION_SEARCH.h:25-28`), le désaccord
`TS_ITEM_FIXED_INFO` / `TS_ITEM_BASE_INFO` (§3.7, §7.2), la présence des dix identifiants dans
`op_codes.md:202-211`, l'absence de toute structure d'objet dans le dépôt, et
`GameCharacterPackets.cs:327`. En revanche la carte elle-même **n'a pas pu être lue** : ce profil
n'a pas d'accès Trello, la vérification a donc porté sur les références locales et non sur le
texte de la carte.

---

## 10. Bloc destiné à `CLAUDE.md`

À recopier dans la description de la MR (le dev n'écrit pas `CLAUDE.md`, Hermes le protège).

```markdown
## Enchères (famille `TM_*_AUCTION_*`, 1300-1310)

`docs/packet-specs/socle-encheres.md` fixe le format de la famille. Trois points à ne pas
redécouvrir :

- Une seule structure d'objet sur le fil vaut **75 octets** à Epic 7.3 : le motif d'objet de base,
  sans `wear_position` / `own_summon_handle` / `index`. L'inventaire `TM_SC_INVENTORY` y ajoute ces
  dix octets et porte 85 ; les enchères s'arrêtent à 75. rzu nomme ce motif `TS_ITEM_FIXED_INFO`,
  NGemity `TS_ITEM_BASE_INFO` : c'est la même disposition. `TS_ITEM_FIXED_INFO` ne contient ses
  champs de position qu'à partir de `EPIC_9_8_1`, contrairement à `TS_ITEM_DYNAMIC_INFO`.
- Le client **lit `appearance_code`** dans ce motif (offset 71) alors que rzu gate le champ à
  `>= EPIC_7_4`. Le client prime : sans ces 4 octets, chaque entrée d'enchère est désalignée de
  4 octets, et les réponses valent 4979/3739 au lieu de **5139/3899**.
- Les trois réponses `1301` (5139), `1303` et `1305` (3899) copient leur tableau en bloc, **sans
  regarder** `auction_info_count` : les 40 emplacements doivent toujours être écrits, même vides.

L'hôtel des ventes n'est **pas** porté depuis NGemity : il n'y implémente aucun handler, aucune
ressource, aucune mécanique (`SecRouteAuction = 130107` y est une constante orpheline). La
validation vient du client et du modèle déjà présent dans le dépôt (`AuctionEntity`,
`ItemStorageEntity.RelatedAuctionId`, les neuf `StorageType`, la table `AuctionCateryResource` de
`ArcadiaSchemaPSQL.sql:1-9`, qui n'est encore lue par aucune entité).
```

---

## 12. Livraison du socle — `navis-dev`, branche `hermes/packet-socle-encheres`

S1, S2 et S3 de la §6.1 sont livrés ; S4 n'exigeait aucun code. Le motif de la §3.7 et les trois
trames de la §3.8/§3.9 sont désormais **exécutables et testés**, ce qui fixe leur disposition mieux
qu'aucune relecture : toute divergence future des offsets casse un test au lieu de désaligner le
client en silence.

### 12.1 Ce qui est livré

| lot | fichier | contenu |
|---|---|---|
| S1 | `Game/Network/Packets/Game/ItemFixedInfoWriter.cs` | `ItemFixedInfo` (les 16 champs de la §3.7), `ItemFixedInfoWriter.Size = 75`, `Write(Span<byte>, in ItemFixedInfo)`, `ItemFixedInfo.FromItem(ItemEntity)` |
| S1 | `Game/Network/Packets/Game/GameCharacterPackets.cs` | `InventoryItemSize` devient `ItemFixedInfoWriter.Size + 10` ; `WriteInventoryItem` délègue les 75 premiers octets et n'écrit plus que la queue de position |
| S2 | `Game/Network/Packets/Game/GameAuctionPackets.cs` | `AuctionInfo` (96), `SearchedAuctionInfo` (128), `RegisteredAuctionInfo` (97, 1303), `BiddedAuctionInfo` (97, 1305), et `BuildAuctionSearch` / `BuildAuctionSellingList` / `BuildAuctionBiddedList` |
| S2 | `Game/Network/Packets/Enums/GamePackets.cs` | `TM_SC_AUCTION_SEARCH = 1301`, `TM_SC_AUCTION_SELLING_LIST = 1303`, `TM_SC_AUCTION_BIDDED_LIST = 1305` |
| S2 | `Game/Network/Clients/GameClient.cs` | un bras `log + continue` pour ces trois identifiants, au-dessus du `switch` final, sur le patron de `TM_SC_REGION_ACK` |
| S3 | `Game/DataAccess/Entities/Arcadia/AuctionCateryResourceEntity.cs` | les six colonnes de la §5.5 |
| S3 | `Game/DataAccess/Contexts/ArcadiaContext.cs` | `DbSet<AuctionCateryResourceEntity> AuctionCateryResources` et clé composée `(CateryId, SubCateryId)` |
| S3 | `.../Repositories/AuctionCateryResourceRepository.cs` + `.../Interfaces/IAuctionCateryResourceRepository.cs` | `GetAll()` sans suivi, sur le patron de `LevelResourceRepository` |
| S3 | `DevConsole/Program.cs` | enregistrement singleton de l'interface |
| tests | `Tests/Game/AuctionPacketsTests.cs` | 13 tests, dont les 16 offsets du motif et les trois tailles de trame |

Les sept paquets client → serveur restent hors lot (§6.2) : **aucun** membre `TM_CS_AUCTION_*`
n'a été ajouté à `GamePackets`, un membre sans branche de dispatch atteindrait le
`throw new Exception("Unknown Packet Type")`.

### 12.2 Décisions prises par le lot

1. **Le motif est partagé, pas dupliqué.** L'inventaire continue de produire exactement 85 octets
   (`wear_position` 75, `own_summon_handle` 77, `index` 81), mais les 75 premiers octets viennent
   maintenant du même writer que les enchères : `appearance_code`, qui avait déjà valeur 0 dans
   l'inventaire, est écrit explicitement et ne peut plus diverger entre les deux familles.
2. **`elemental_effect.remain_time` (59) et `appearance_code` (71) restent à 0.** Ce sont les deux
   champs que le sérialiseur d'inventaire n'a jamais remplis ; `FromItem` les pose à zéro
   explicitement au lieu de compter sur un tampon vierge. La question du contenu reste la §8.3.
3. **Les 40 emplacements sont toujours écrits, et leur contenu hors compte est zéro.** Les
   constructeurs zéro-remplissent la table par construction (`new byte[]`), écrivent
   `auction_info_count` plafonné à 40 et refusent d'écrire au-delà du quarantième emplacement : un
   appelant négligent ne peut pas produire une trame courte ni un débordement.
4. **`flag` (127) et `status` (96) sont transportés, jamais interprétés.** Le socle les recopie
   tels quels : leur sémantique est la §8.1/§8.2, donc la §9 question 3.
5. **`page_num` et `total_page_count` sont des paramètres, pas des calculs.** Le premier est l'écho
   de la requête (§3.8) ; le second n'a pas de règle établie (§8.6), le socle ne l'invente pas.

### 12.3 Réserves de la livraison

1. **`item_info.flag` sur le fil quand l'objet n'a pas de flag.** `ItemFixedInfo.FromItem` reprend
   la conversion de l'inventaire, `unchecked((uint)item.Flag)` : un objet dont `Flag` vaut
   `ItemFlag.None = -1` envoie `0xFFFFFFFF` au client, et non `0`. C'est le comportement déjà livré
   par l'inventaire et le motif §5.5 de `203-drop-item.md` ; le lot ne le corrige pas, il le rend
   visible (le test de `FromItem` épingle la valeur pour `ItemFlag.Card` uniquement).
2. **Clé composée de `AuctionCateryResource`.** `ArcadiaSchemaPSQL.sql:1-9` ne déclare ni clé ni
   contrainte d'unicité : la paire `(catery_id, sub_catery_id)` est la clé naturelle retenue, par
   analogie avec `EnhanceResourceEntity` et `SetItemEffectResourceEntity`. Si la table réelle
   admettait des doublons sur cette paire, le suivi d'entités échouerait.
3. **Nom de table et migration.** Aucune surcharge de nom n'est posée : l'entité suit la convention
   du dépôt, où le nom du `DbSet` fait le nom de table (`NpcResources`, `MonsterResources` —
   `Migrations/Arcadia/20260712205315_AddNpcResource.cs:16`). La table de la base de production
   s'appelle `AuctionCateryResource` (singulier, `ArcadiaSchemaPSQL.sql:1`). Aucune migration n'a pu
   être générée : `dotnet ef` n'est pas installé dans le conteneur. À trancher avec la voie de
   provisionnement retenue (migrations EF ou script SQL).
4. **`GetAll()` n'a aucun consommateur.** L'interface est enregistrée en singleton mais rien ne
   l'injecte encore : c'est le point d'entrée de la carte 1300, pas un service en fonctionnement. Ce
   chemin n'est donc couvert par aucun test (le modèle EF l'est, pas la requête).

### 12.4 Bloc destiné à `CLAUDE.md` (version livrée)

Remplace le bloc de la §10, qui décrivait le cadrage et non le code.

```markdown
## Enchères (famille `TM_*_AUCTION_*`, 1300-1310)

`docs/packet-specs/socle-encheres.md` fixe le format de la famille ; le socle est implémenté.
Trois points à ne pas redécouvrir :

- Une seule structure d'objet sur le fil vaut **75 octets** à Epic 7.3 : le motif d'objet de base,
  sans `wear_position` / `own_summon_handle` / `index`. L'inventaire `TM_SC_INVENTORY` y ajoute ces
  dix octets et porte 85 ; les enchères s'arrêtent à 75. Ce motif est écrit une seule fois, dans
  `Game/Network/Packets/Game/ItemFixedInfoWriter.cs` (`Size = 75`, `Write`, `FromItem`) : l'inventaire
  passe par lui, et toute nouvelle famille d'objets doit en faire autant. rzu nomme ce motif
  `TS_ITEM_FIXED_INFO`, NGemity `TS_ITEM_BASE_INFO`.
- Le client **lit `appearance_code`** dans ce motif (offset 71) alors que rzu gate le champ à
  `>= EPIC_7_4`. Le client prime : sans ces 4 octets, chaque entrée d'enchère est désalignée de
  4 octets, et les réponses valent 4979/3739 au lieu de **5139/3899**.
- Les trois réponses `1301` (5139), `1303` et `1305` (3899) copient leur tableau en bloc, **sans
  regarder** `auction_info_count` : `GameAuctionPackets` écrit toujours les 40 emplacements, vides
  ou non, et plafonne le compte à 40.

Les trois identifiants serveur → client (`1301`, `1303`, `1305`) sont dans `GamePackets` et ont un
bras `log + continue` dans `GameClient`, comme `TM_SC_REGION_ACK` : le client ne les envoie jamais,
mais un membre d'enum sans branche atteindrait le `throw "Unknown Packet Type"`. Les sept paquets
client → serveur de la famille (`1300`, `1302`, `1304`, `1306`, `1308`, `1309`, `1310`) restent à
implémenter, chacun avec son bras de dispatch.

L'hôtel des ventes n'est **pas** porté depuis NGemity : il n'y implémente aucun handler, aucune
ressource, aucune mécanique (`SecRouteAuction = 130107` y est une constante orpheline). La
validation vient du client et du modèle déjà présent dans le dépôt (`AuctionEntity`,
`ItemStorageEntity.RelatedAuctionId`, les neuf `StorageType`, la table `AuctionCateryResource`,
lue par `AuctionCateryResourceRepository`).
```

---

## 11. Commits épinglés et méthode de lecture

| référence | commit / empreinte |
|---|---|
| `rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02, « packets: fix TS_SC_INVENTORY with older epics ») |
| `ngemity/Chihiro` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03, « Fix compilation issue for GCC ») |
| `master` du dépôt | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` |
| client 7.3 | `SFrame.exe`, `sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 octets) ; `db_auctioncategoryresource.rdb` (1 044 octets) |

`reference/client73/` n'est pas un dépôt Git : seule l'empreinte du binaire est opposable.

Méthode de lecture statique du client, pour rejouer les mesures sans exécuter le client
(aucun `SFrame.exe`, aucun Lua, aucun script client n'a été lancé) :

1. **Table de dispatch serveur → client** : le `switch` à `0x67e67a` couvre `1201..1351`
   (`sub eax,0x4b1`, `cmp eax,0x96`), indexe la table d'octets `0x67f4c8` (`index = id - 1201`),
   puis la table de sauts `0x67f4b0`. Chaque stub est un bloc de 1 + 2 + 5 + 5 octets qui fait
   `call` vers le handler : `0x67e6a5` → `0x670660` (1301), `0x67e6b2` → `0x6706d0` (1303),
   `0x67e6bf` → `0x670730` (1305).
2. **Tailles de trame** : les handlers serveur → client copient leur tableau en bloc
   (`0x1400` = 5120 = 40 × 128 pour 1301 ; `mov ecx,0x3ca` + `rep movs` = 3880 = 40 × 97 pour
   1303 et 1305), ce qui donne directement la taille d'entrée, donc celle d'`item_info`.
3. **Trames client → serveur** : chaque identifiant de la famille possède un constructeur
   « zéro + `Length` + `Id` » dans `0x48ca20`-`0x48e040` (`mov eax,<id>` puis écriture du `Length`
   dans le premier `dword`), appelé par un sender qui remplit les champs depuis le message interne
   (`frame+7`, `frame+11`, … lus sur `message+0x13`, `message+0x17`, …).
4. **Motif commun de 75 octets** : la trame `705` du client copie 18 `dword` + 1 `word` + 1 `byte`
   par objet depuis un conteneur à pas `0x53` vers une destination à pas `0x4b`.
