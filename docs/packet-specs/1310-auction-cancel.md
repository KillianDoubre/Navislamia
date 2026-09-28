# `TM_CS_AUCTION_CANCEL` (1310) — fiche de paquet, Epic 7.3

> Fiche de l'archéologue (`navis-ref`) pour la branche `hermes/packet-1310-auction-cancel`,
> septième et **dernière** carte de la famille « enchères » après 1300, 1302, 1304, 1306, 1308 et
> 1309. Le lot de dev ajoute sa livraison en **section 14** ; les sections 1 à 13 ne sont pas
> réécrites.
> Rien dans cette fiche n'a été obtenu en exécutant le client, un Lua ou un script du client : tout
> vient de la lecture statique de `SFrame.exe` (`objdump -d`, chaînes, RTTI), des trois dépôts de
> référence et du dépôt. Les adresses du client sont des adresses virtuelles (image base `0x400000`).

| | |
|---|---|
| id décimal | **1310** (jamais `2310` en 7.3, §4) |
| nom dans le dépôt | `TM_CS_AUCTION_CANCEL` |
| sens | client → serveur |
| taille de trame | **11 octets** (§3.1) |
| branche | `hermes/packet-1310-auction-cancel` |
| base | `master` = `b56967a07430422add88e0e5cdf292b41b18f6c6` |
| socle | `docs/packet-specs/socle-encheres.md` (§2, §3.5, §4.2, §5.3, §6.2, §6.3, §7.2, §8.5, §9, §11) |

---

## 1. Identité

* **Nom et identifiant dans le dépôt** : `Navislamia/op_codes.md:211`
  (`[1310] = "TM_CS_AUCTION_CANCEL"`). Le nom est celui du fichier de rzu, sans transformation.
  `op_codes.md` ne porte que 1300-1306, 1308, 1309 et 1310 pour la famille : **1307 est un trou**
  (socle §8.7), et aucun `TM_SC_AUCTION_*` n'existe au-delà de 1305.
* **rzu** : `reference/rzu/librzu/src/packets/GameClient/TS_CS_AUCTION_CANCEL.h` —
  `_(simple)(uint32_t, auction_uid)` (ligne 6), `X(1310, version < EPIC_9_6_3)` /
  `X(2310, version >= EPIC_9_6_3)` (lignes 9-10),
  `CREATE_PACKET_VER_ID(TS_CS_AUCTION_CANCEL, SessionType::GameClient, SessionPacketOrigin::Client)`
  (ligne 12). Particularité mesurée : ce fichier **ne porte pas de ligne `// Last tested:`**, là où
  `TS_CS_AUCTION_INSTANT_PURCHASE.h:5` en porte une (`EPIC_9_8_1`). L'absence d'un tel marqueur ne
  change pas la structure déclarée, elle est relevée pour que le dev ne la cherche pas.
* **NGemity** : `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_AUCTION_CANCEL.h:6-9`
  (`_(simple)(uint32_t, auction_uid)`, `CREATE_PACKET(TS_CS_AUCTION_CANCEL, 1310)` — **sans**
  variante `2xxx`), inclus par `reference/ngemity/shared/Server/XPacket.h:56`, énuméré en
  `reference/ngemity/shared/Server/ClientPackets.h:218`.
* **État du dépôt à la base** : l'id 1310 n'est déclaré nulle part. Sur `master`, la famille se
  réduit aux trois réponses serveur → client 1301/1303/1305
  (`Game/Network/Packets/Enums/GamePackets.cs:156-158`) et à leur bras « log and drop »
  (`Game/Network/Clients/GameClient.cs:1405-1411`). `git grep -n "TryReadAuction" origin/master --
  Game Tests` ne rend **rien** : aucun lecteur d'enchère n'existe, et
  `Game/Network/Packets/Game/GameAuctionPackets.cs` (186 lignes) ne contient que le modèle
  `AuctionInfo`/`SearchedAuctionInfo`/`RegisteredAuctionInfo`/`BiddedAuctionInfo` et les trois
  écrivains `Build*`.
* **La carte jumelle est `1308`** (`TM_CS_AUCTION_INSTANT_PURCHASE`,
  `docs/packet-specs/1308-auction-instant-purchase.md`) : même taille de trame, même en-tête
  construit en ligne, même décision de largeur pour `auction_uid`. Ce que cette parenté partage
  exactement — et pourquoi elle ne dispense pas d'un lecteur propre — est en §3.2.
* **La carte dont `1310` dépend est `1309`** (`TM_CS_AUCTION_REGISTER`) : le socle §6.3, ordre 6,
  donne « `1310 CANCEL` | prérequis `1309` | décision restante : remise de l'objet (entrepôt) et de
  la taxe ». Une annulation n'a de sens que sur une annonce qui a pu être inscrite.

## 2. Ce que le joueur fait

Le joueur ouvre l'hôtel des ventes. La fenêtre de dépôt **`SUIAuctionDepositWnd`** (nom de type RTTI
en ligne à `0xc1448c`) héberge la sous-fenêtre **`SUIAuctionRegisterWnd`** (nom en ligne à
`0xc14ee4`, descripteur `0xc14edc`, vtable `0xa25df4`). C'est **cette** sous-fenêtre qui porte
l'action de retrait, et elle seule : c'est le seul endroit du client où la trame est construite
(§3.2).

Le geste mesuré, dans `SUIAuctionRegisterWnd` :

1. le joueur choisit la ligne de son annonce — contrôle `item_list` (`0xa2541c`, membre `this+0x4f8`
   pour l'affichage et `this+0x4c8` pour les données), dont la ligne choisie est recopiée dans le
   contrôle `item_selected` (`0xa25d88`, membre `this+0x558`) ;
2. il déclenche l'action de retrait. Le chemin est en **deux temps** dans le client, et les deux
   temps sont mesurés :
   * la fenêtre ouvre d'abord une **boîte de confirmation** : son propre gestionnaire de notification
     (slot 6, `0x4fb100`) appelle `0x4f96d0` en `0x4fb4ac` ; `0x4f96d0` alloue et remplit un
     `AUSIMSG_REQ_OPEN_MSGBOX` (type `0x42a`, vtable `0xa1fba0`, RTTI `.?AUSIMSG_REQ_OPEN_MSGBOX@@`
     en ligne à `0xc129bc`) par le constructeur `0x490e10` appelé en `0x4f97b6` — **le nom de la
     boîte est la chaîne `deregister`** (`push $0xa25820` en `0x4f97ac`, 10 octets) et son texte est
     le nom de l'objet sélectionné (gabarit `#@itemname#@`, chaîne `0xa2582c`, appliqué en
     `0x4f978c`-`0x4f9791`) ;
   * quand le joueur confirme, la fenêtre reçoit un `AUSIMSG_UI_SEND_DATA` (type `0x426`, vtable
     `0xa1cc10`, RTTI `.?AUSIMSG_UI_SEND_DATA@@`) **dont le nom vaut exactement `deregister`** —
     c'est ce que teste la branche `0x4fb901`-`0x4fb941` de son slot 50 (comparaison sur 10
     caractères exactement, avec la chaîne `0xa25820`), et c'est **ce message** qui émet `1310`.
     Le même gestionnaire porte le bouton voisin `register` (chaîne `0xa25804`, 8 octets), qui
     émet `1309` — la fiche `1309-auction-register.md` §2 renvoie explicitement le chemin
     `deregister` à la présente carte.

Relevés utiles pour qui relit ceci :

* **Le seul site d'émission de `1310` dans tout le client** est `0x4f8a50`, atteint depuis
  `0x4fb945` (§3.2). Ce que le socle §2 attribue à `SUIAuctionWnd` / `SUIAuctionDepositWnd` est
  donc **resserré** : la mesure donne `SUIAuctionRegisterWnd`, la sous-fenêtre, et non les deux
  enveloppes (§12, point 1).
* La chaîne `msgboxAuctionDeregister` existe dans `.rdata` à `0xa43768`, au milieu de la table des
  noms de boîtes (`msgboxGuildOkCancel` `0xa43308`, `msgbox_itemdestruction` `0xa4328c`, …). Aucune
  référence directe à cette adresse n'existe dans `.text` ; le nom de boîte réellement passé au
  constructeur en `0x4f97ac` est le littéral `deregister`. Le lien entre les deux est **non
  établi** (§7, question 1) — ne pas s'appuyer sur cette chaîne comme preuve du geste.
* Le saut intermédiaire qui recopie le nom de la boîte dans le `AUSIMSG_UI_SEND_DATA` est du code
  générique de la couche interface : la construction d'un `AUSIMSG_UI_SEND_DATA` se fait par une
  famille de constructeurs (`0x474130`, `0x490aa0`, `0x490b93`, …) dont aucun, dans le désassemblage,
  ne pousse le littéral `0xa25820`. C'est **cohérent** avec un nom repris de la boîte à la
  confirmation, mais ce saut précis n'est pas épinglé (§7, question 2).

## 3. Structure sur le fil

### 3.1 Trame `TM_CS_AUCTION_CANCEL` — **11 octets**

En-tête commun de la famille (socle §3.1) : les trois helpers du dépôt sont
`HeaderSize = 7` (`Game/Network/Packets/Game/GameCharacterPackets.cs:19`), `CreatePacket`
(`:383`) et `WriteChecksum` (`:391`), avec leurs jumeaux de la famille en
`Game/Network/Packets/Game/GameAuctionPackets.cs:168-174` et `:176-185`. Les tailles ci-dessous sont
**totales, en-tête compris**.

| offset | taille | type | nom (rzu) | valeur observée | source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` | **11** (`0x0b`) | client `0x48dfe4` (`mov %esi,-0xc(%ebp)`, `esi = 0xb` posé en `0x48dfdb`) ; réécrit à l'identique en `0x48e006` |
| 4 | 2 | `uint16` | `Id` | **`0x051e`** = 1310 | client `0x48dfd6` (`mov $0x51e,%eax`) puis `0x48dfe0` (`mov %ax,-0x8(%ebp)`) |
| 6 | 1 | `uint8` | `Checksum` | **`0x2e`** (46), constante pour ce paquet | client `0x48dff0`-`0x48dff8` (somme des 6 premiers octets), `0x48dffa` et `0x48e021` (`mov %dl,-0x6(%ebp)`) |
| 7 | 4 | `uint32` | `auction_uid` | identifiant de l'annonce de la ligne sélectionnée | rzu `TS_CS_AUCTION_CANCEL.h:6` (`uint32_t`) ; NGemity `…CANCEL.h:7` (`uint32_t`) ; client `0x48e000`-`0x48e003` (`mov 0x13(%edx),%eax` puis `mov %eax,-0x5(%ebp)`, `frame+7 ← message+0x13`) |

**Taille totale attendue : 11 octets.** Le `Checksum` vaut toujours `0x2e` : le champ
`auction_uid` est **hors** du calcul (la boucle `0x48dff0`-`0x48dff8` s'arrête avant `frame+6`), et
`0x0b + 0x1e + 0x05 = 0x2e`, comme le `WriteChecksum` du dépôt
(`GameAuctionPackets.cs:176-185`, qui somme `packet[0..5]`).

Il n'y a **rien d'autre** : pas de `count`, pas de champ conditionnel, pas de remplissage. La trame
de `1310` et celle de `1308` sont identiques au bit près **hors l'identifiant**.

### 3.2 Où le client construit la trame, et ce que `1310` partage avec `1308`

* **Constructeur** : `0x48dfc0`, en **ligne** — il n'y a pas d'initialiseur partagé à chercher
  ailleurs (les `1306` et `1309` en ont un ; `1308` et `1310` n'en ont pas). Ordre exact :
  `0x48dfc6`-`0x48dfd2` remise à zéro d'un tampon local de 12 octets à `ebp-0xc` (la trame) ;
  `0x48dfd6` id, `0x48dfdb` longueur, `0x48dfe4` écriture de `Length`, `0x48dfe0` écriture de l'`Id`,
  boucle de somme `0x48dff0`-`0x48dff8`, `0x48dffa` écriture du `Checksum`, `0x48e003` écriture de
  l'`auction_uid` relu dans l'objet message en `+0x13` (`0x48e000`), recalcul et réécriture du
  `Checksum` (`0x48e010`-`0x48e021`), puis envoi par la vtable de connexion en `0x48e027`-`0x48e039`
  (`[conn]+0xb8`, slot `0xc4`) ; `ret $0x4` en `0x48e03e`, la méthode est `__thiscall` à **un
  argument** (le message interne à sérialiser).
* **Ce que `1310` partage littéralement avec `1308`** : la taille (11), la forme de l'en-tête, la
  position du champ (7) et la fabrique en ligne. **Ce qu'il ne partage pas** : le sender
  (`0x48dfc0` pour `1310`, `0x48dea0` pour `1308`), l'identifiant de trame (`0x51e` contre `0x51c`),
  la clé interne du message (`1161` contre `1159`), la classe du message interne
  (`AUSIMSG_REQ_AUCTION_CANCEL`, vtable `0xa25738`, nom en ligne à `0xc148a8`, contre
  `AUSIMSG_REQ_AUCTION_INSTANT_PURCHASE`, vtable `0xa25964`) et **la fenêtre émettrice**
  (`SUIAuctionRegisterWnd` contre `SUIAuctionSearchWnd` + `SUIAuctionTenderWnd`). Un lecteur propre
  est donc requis : rien, côté serveur, ne permet de distinguer une annulation d'un achat immédiat
  sinon l'`Id` de la trame — les confondre reviendrait à vendre un objet au lieu de le retirer.
* **Chien de garde de lecture** : `0x51e` n'apparaît comme immédiat de code qu'**une seule fois**
  dans tout `SFrame.exe` (`mov $0x51e,%eax` en `0x48dfd6`). Les autres occurrences de `0x51e` sont
  des constantes de division (`0x51eb851f`) : un `grep '$0x51e'` brut rend **19** lignes, dont 18
  constantes de division et une seule occurrence du paquet. La branche `1310` du gestionnaire de
  résultat (§5.3) ne contient elle-même **aucun** immédiat `0x51e` : elle est atteinte par
  `sub $0x51c` puis deux `dec %eax` (traitement par « voisinage », §5.3).

### 3.3 Le chemin complet, du geste à la trame (rejouable)

1. **Geste** : confirmation de la boîte `deregister` → `AUSIMSG_UI_SEND_DATA` nommé `deregister`
   reçu par le slot 50 de `SUIAuctionRegisterWnd` (`0x4fb820`, adresse dans la vtable `0xa25df4` en
   `0xa25ebc`), branche `0x4fb901` → appel de l'émetteur en `0x4fb945`.
2. **Émetteur** : `0x4f8a50` (méthode non virtuelle, appelée seulement en `0x4fb945`). Il lit la
   ligne sélectionnée par `0x5b9040` (`push $0x0` en `0x4f8a63`, appel en `0x4f8a69`) sur le membre
   `item_selected` (`lea 0x558(%edi)` en `0x4f8a5b`) puis son champ **`id`** (`push $0xa25418` en
   `0x4f8a7e`, appel `0x5b9250` en `0x4f8a85`), dont le `dword` en `+0x10` est recopié en `esi`
   (`0x4f8a8a`).
3. **Message interne** : l'émetteur réserve 23 octets (`push $0x17` en `0x4f8a8d`, `call 0x97671b`),
   écrit la clé **`0x489` = 1161** en `+4` (`0x4f8a9b`), la vtable **`0xa25738`** en `+0`
   (`0x4f8ab3`), l'`auction_uid` en `+0x13` (`mov %esi,0x13(%eax)` en `0x4f8ac0`), et met le message
   en file par `0x649e20` sur l'objet `[this+0x440]` (`0x4f8aca`).
4. **RTTI** : la vtable `0xa25738` est précédée en `0xa25734` du localisateur `0xbbd33c` dont le
   `pTypeDescriptor` vaut `0xc148a0` ; le nom du type est **en ligne** à `0xc148a8` =
   `.?AUSIMSG_REQ_AUCTION_CANCEL@@` (c'est l'adresse citée par le socle §2). La chaîne est donc bien
   celle du message interne, et non celle d'un homonyme.
5. **Table de dispatch interne** : la fonction `0x49e21d` fait `sub eax,0x403` / `cmp eax,0xde` /
   `movzx eax,BYTE PTR [eax+0x49ea50]` / `jmp DWORD PTR [eax*4+0x49e98c]`. La table d'octets
   `0x49ea50` couvre les clés 1027 à 1249. Pour la clé **1161** (`0x489`) : indice `134` (`0x86`),
   octet `0x49ead6` = `0x27` = 39, entrée 39 de la table de sauts = `0x49e3bb`.
6. **Stub → trame** : `0x49e3bb` fait `push %edi` / `mov %esi,%ecx` / `call 0x48dfc0` (en `0x49e3be`)
   / `jmp 0x49e75f`. Ce stub est le **seul** appelant de `0x48dfc0` : la trame de `1310` a un
   unique lieu de naissance.

Correspondance mesurée par la même méthode pour la famille entière (les clés 1159, 1160 et 1161 sont
celles que la fiche `1308` a établies) :

| clé interne | stub | sender | trame |
|---|---|---|---|
| 1159 (`0x487`) | `0x49e3a1` | `0x48dea0` | 1308 (11 o) |
| 1160 (`0x488`) | `0x49e3ae` | `0x48df30` → `0x48cad0` | 1309 (32 o) |
| **1161 (`0x489`)** | **`0x49e3bb`** | **`0x48dfc0`** | **1310 (11 o)** |
| 1162 (`0x48a`) | `0x49e3c8` | `0x48e050` (id `0x546`) | 1350 (`ITEM_KEEPING_LIST`) |
| 1163 (`0x48b`) | — | — | **non câblée** : l'octet `0x49ead8` vaut `0x30`, l'indice du gestionnaire par défaut `0x49e763` |
| 1164 (`0x48c`) | `0x49e3d5` | `0x48e0e0` (id `0x548`) | 1352 (`ITEM_KEEPING_TAKE`) |

Deux autres `push $0x489` existent dans `.text` (`0x615f20`, `0x615fcc`) : l'un et l'autre sont
consommés immédiatement par `call 0x43f6d0` (résolution d'une ressource gardée par `ds:0xc49438`).
Ce sont des **identifiants de ressource**, pas des clés de message — l'écriture d'une clé se fait
par `movl $0x489,0x4(%eax)`, et il n'y en a qu'une.

### 3.4 Ce que le client fait juste après avoir émis `1310` (mesuré)

La fin de `0x4f8a50` (toujours atteinte, y compris quand l'allocation échoue : les deux `je`
tombent sur les mêmes branches) enchaîne, dans cet ordre :

* `0x4f8acf`-`0x4f8b03` : remise à jour du contrôle de détail (`0x5b8c90`, `0x4f0510`… `0x4ea2a0`) ;
* `0x4f8b08`-`0x4f8b4b` : allocation d'un **second** message interne, clé **`0x482` = 1154 =
  `REQ_AUCTION_SELLING_LIST`**, vtable `0xa25730`, `page_num = 1` (`movl $0x1,0x13(%eax)` en
  `0x4f8b34`), mis en file par `0x4f0510` sur `[this+0x4a8]` (`0x4f8b3f`-`0x4f8b4b`) ;
* `0x4f8b50`-`0x4f8b5c` : appel de `0x5b9d80` puis `0x5b9310` sur la vue `item_selected`.

Autrement dit le client **redemande lui-même la liste de ses ventes en page 1** juste après avoir
demandé l'annulation : c'est cette demande-là qui lui rend l'état à jour, pas une trame dédiée
(§5.3). Le serveur n'a donc pas à pousser `1303` « en avance » : il doit répondre à la demande
`1302` que le client vient d'émettre.

## 4. Gating de version, tranché pour 7.3

`EPIC_7_2 = 0x070200`, `EPIC_7_3 = 0x070300`, `EPIC_9_6_3 = 0x090603` (socle §4).

| # | champ / identifiant | gating rzu | décision pour 7.3 | source |
|---|---|---|---|---|
| 4.1 | **id du paquet** | `X(1310, version < EPIC_9_6_3)`, `X(2310, version >= EPIC_9_6_3)` | **1310**. 7.3 (`0x070300`) est antérieur à `EPIC_9_6_3` : la branche `<` gagne. `2310` **ne doit jamais être déclaré** | `TS_CS_AUCTION_CANCEL.h:9-10` ; contrôle indépendant côté client : `mov $0x51e,%eax` en `0x48dfd6` et aucune occurrence de `0x906` (= 2310) comme immédiat de code dans `SFrame.exe` |
| 4.2 | `auction_uid` | aucun (`_(simple)`) | **4 octets** au décalage 7, lu en **`uint32`** | `TS_CS_AUCTION_CANCEL.h:6` ; la boucle du client écrit un `dword` en `frame+7` (`0x48e003`) |
| 4.3 | **signedness d'`auction_uid`** (largeur signée ou non) | rzu : **`uint32_t`** pour `1310`, quand `1300`/`1302`/`1304`/`1306`/`1308` déclarent `int32_t` | **`uint32` non signé**, comme rzu et comme NGemity (`…CANCEL.h:7`) le déclarent tous deux pour ce paquet. Socle §3.5 : « le dépôt retient `uint32` pour l'identifiant d'annonce ». Ce paquet est donc le seul de la famille où **les deux références s'accordent** sur l'absence de signe | `TS_CS_AUCTION_CANCEL.h:6` ; `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_AUCTION_CANCEL.h:7` |
| 4.4 | alias tardif | `2310` à partir de `EPIC_9_6_3` | **établi, non déclaré** : le client 7.3 ne le construit pas et son gestionnaire de résultat ne connaît que `0x51e` (§5.3) | `TS_CS_AUCTION_CANCEL.h:10` ; §3.2 |

Contrôle croisé, pour qui relit un `grep 1310` dans rzu : le numéro **1310 est réattribué** à partir de
`EPIC_9_6_3` — il porte alors `TS_SC_TAMING_INFO` (`reference/rzu/librzu/src/packets/GameClient/TS_SC_TAMING_INFO.h:12`,
`X(1310, version >= EPIC_9_6_3)`), une trame serveur → client sans rapport avec les enchères. Seul le
paquet dont le gating est `< EPIC_9_6_3` concerne cette carte : c'est bien `TM_CS_AUCTION_CANCEL`.

**Aucun champ de `1310` n'a de gating non statué** : la trame est entièrement déterminée pour 7.3.
Le seul reste est un **écart de modèle**, pas de gating : le lecteur rendra un `uint32` alors que le
modèle existant porte `int AuctionUid` (`GameAuctionPackets.cs:14`) et que `Entity.Id` est un `long`
(`Game/DataAccess/Entities/Entity.cs:8`). La comparaison avec une ligne d'annonce doit donc être
explicite et non une conversion implicite (§7, question 3).

## 5. Traitement attendu

### 5.1 NGemity déclare `1310` et ne le gère pas — il n'y a rien à porter

* NGemity **déclare** la trame (`shared/Server/Packets/GameClient/TS_CS_AUCTION_CANCEL.h:6-9`),
  l'inclut (`shared/Server/XPacket.h:56`) et l'énumère (`shared/Server/ClientPackets.h:218`).
* NGemity **n'a aucun handler** : `grep -rni auction reference/ngemity/Chihiro/src/` rend 8 lignes,
  toutes des identifiants de colonne ou d'énumération d'objet — `Entities/Item/ItemTemplate.hpp:216`
  (`BY_AUCTION = 12`), `Entities/Item/Item.cpp:91,127` et `Entities/Item/ItemInstance.h:36,59,81,103`
  (`m_nAuctionID`, colonne `auction_id`), `Map/WorldLocation.h:50` (`SecRouteAuction = 130107`, un
  identifiant de routage de zone). Rien à porter.
* Son schéma confirme le **modèle**, pas la mécanique : `Database/Telecaster.sql:46-60` définit la
  table `Auction` (`sid`, `item_id`, `seller_id`, `seller_name`, `is_secroute_only`, `end_time`,
  `instant_purchase_price`, `registration_tax`, `bidder_list`, `highest_bidding_price`,
  `highest_bidder_id`, `highest_bidder_name`) et `:403-414` la table `ItemKeeping`
  (`item_id`, `owner_id`, `expiration_time`, `keeping_type`, `related_auction_id`,
  `related_item_code`, `related_item_enhance`, `related_item_level`), qui est exactement l'entité
  `ItemStorageEntity` du dépôt. **Aucune de ces deux tables ne porte de colonne d'état** : une
  annulation n'y laisse aucune trace autre que la disparition ou le déplacement des lignes (§7,
  question 4).
* **rzu ne fait pas mieux** : `grep -rn "Auction" reference/rzu/rzgame/src/` rend **0** ligne. Les
  seuls artefacts rzu qui parlent d'enchères au-delà des en-têtes de paquet sont l'outil
  `rzauctionmonitor` (lecture de la base vivante d'un serveur officiel) et la définition rzu de la
  table, aucune logique de serveur. Il n'y a donc **rien à porter de NGemity ni de rzu** pour le
  traitement : la validation viendra du client, dont toutes les mesures ci-dessus proviennent.

### 5.2 Le lecteur, à ajouter dans `GameAuctionPackets`

Patron du dépôt pour une trame courte à refuser plutôt qu'à lire partiellement :
`GameActionPackets.TryReadSummonCardSkillList` (`Game/Network/Packets/Game/GameActionPackets.cs:87-98`,
`packet.Length != HeaderSize + 4` → `false`) et son bras `GameClient.cs:1525-1529`. Attendu pour
`1310` :

* `TryReadAuctionCancel(ReadOnlySpan<byte> packet, out uint auctionUid)` — accepte **exactement 11
  octets** (`HeaderSize + 4`), lit `BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(7, 4))`,
  rend `false` sur toute autre taille. Le `Checksum` constant (`0x2e`) peut être vérifié comme
  invariant de test, mais **aucune** contrainte de ce type n'existe dans les autres lecteurs du
  dépôt : ne pas en inventer une exigence de rejet.
* Retour sur trame malformée : le patron de la famille (fiche `1308` §5.2) est
  `SendResult((ushort)GamePackets.TM_CS_AUCTION_CANCEL, (ushort)ResultCode.InvalidArgument)`
  (`GameClient.SendResult`, `Game/Network/Clients/GameClient.cs:68` ; `ResultCode.InvalidArgument = 28`,
  `Game/Network/Packets/ResultCode.cs:37`). C'est **tout** ce que le serveur sait dire d'utile
  aujourd'hui, et c'est cohérent avec la §5.4.

### 5.3 Le client n'attend aucun paquet dédié — et le serveur n'a donc rien à répondre

* Le socle §5.3, ligne `1310`, l'établit : « aucun paquet dédié ». Il n'existe **aucune** trame
  `TS_SC_AUCTION_*` au-delà de 1305, ni dans rzu, ni dans NGemity, ni dans le client.
* Mesure directe, côté client : le gestionnaire de `TM_SC_RESULT` (`0x66db80`) alloue un
  `AUSMSG_RESULT` (type 4 en `+4`, vtable `0xa51ee8`, RTTI `.?AUSMSG_RESULT@@` en ligne à `0xc180b0`,
  taille 27 octets) et y recopie `id` (`0x66dbcc`), `result` (`0x66dbd4`) et `value` (`0x66dbdc`) de
  la trame avant de dispatcher sur l'id reçu (`0x66dbe2`). La chaîne de comparaisons atteint `1310`
  par voisinage : `0x66e04a` `sub $0x51c` → `0x66e09d` (branche `1308`), `0x66e051` `dec` → `0x66e07c`
  (branche `1309`), `0x66e054` `dec` + `jne` → **repli sur `0x66e05b`, la branche `1310`**, qui
  journalise avec son propre libellé `경매 취소    요청결과-%s[%d]\n` (chaîne `0xa524fc` ; à comparer à
  `0xa524d4` pour `1309`, `0xa524ac` pour `1308`, `0xa52524` pour `1306`) puis rejoint la queue
  commune `0x66e279` (mise en file par `0x64d0e0` sur `[ebx+0x2c]`). Un id inconnu tombe sur le
  libellé générique `0xa52264` (`0x66e267`-`0x66e276`).
* **La fenêtre, elle, ignore la réponse de `1310`** : le dispatcher de `SUIAuctionRegisterWnd`
  traite un `AUSMSG_RESULT` en `0x4fb8d1`-`0x4fb8f1` et n'y compare `0x13(%msg)` qu'à **`0x516` =
  1302**, avec `result == 1` en `0x15(%msg)` ; il n'y a **aucune** comparaison à `0x51e`. Le seul
  retour que la fenêtre consomme est donc celui de sa propre demande `1302` — celle qu'elle vient
  d'émettre (§3.4). Conséquence pour le serveur : **aucun paquet n'est attendu en retour de `1310` ;
  le rafraîchissement passe par la réponse à la demande `1302` du client** (trame `1303`, sortie de
  ce lot).
* Si un refus doit être signifié, le dépôt n'a qu'un canal : `TM_SC_RESULT` (id **0** en 7.3,
  `GamePackets.cs:5`, contenu `id`+`result`+`value`, écrit par `GameClient.SendResult`
  `Game/Network/Clients/GameClient.cs:68-72`) avec un `ResultCode`. Aucun `ResultCode` de la famille
  enchères n'existe.

### 5.4 Ce que le serveur peut exécuter aujourd'hui : **rien** — et c'est mesuré

Sur `master`, il n'existe :

* **aucune** déclaration ni aucun lecteur d'enchère (§1) ;
* **aucun** service ni repository d'enchère : `Game/DataAccess/Repositories/` ne contient que
  `AuctionCateryResourceRepository` (la table de catégories d'`ArcadiaSchemaPSQL.sql`, hors
  mécanique), et `AuctionEntity`/`ItemStorageEntity` ne sont lus nulle part en dehors de leur
  contexte et de leurs migrations ;
* **aucune** écriture d'objet ni de monnaie pour ce chemin. Ce qui existe et doit être **utilisé,
  pas réinventé** :
  * `AuctionEntity` (`Game/DataAccess/Entities/Telecaster/AuctionEntity.cs:5-35`) — `ItemId`,
    `SellerId`/`SellerName`, `EndTime:20`, `InstantPurchasePrice:21`, `RegistrationTax:22`,
    `BiddersIds:23`, `HighestBiddingPrice:24`, `HighestBidderId:26`, `ItemStorage:35`. **Aucun champ
    d'état** : l'entité ne sait pas dire « annulée » (§7, question 4) ;
  * `ItemStorageEntity` (`…/ItemStorageEntity.cs:9-28`) : `StorageType` en `:18`,
    `RelatedAuctionId` en `:20`, `RelatedItemId` en `:23`, `RelatedItemEnhance`/`RelatedItemLevel` ;
    FK `RelatedAuctionId` déclarée en `Game/DataAccess/Contexts/TelecasterContext.cs:166-169` ;
  * `StorageType` (`Game/DataAccess/Entities/Enums/StorageType.cs`) : **`ItemByCancel = 4` (ligne 10)**
    et **`GoldByCancel = 33` (ligne 15)** sont déjà là, à côté de `ItemByInstantPurchase = 2`,
    `ItemByExpiration = 3`, `GoldByItemSell = 30`, `GoldByRegTax = 31`, `GoldByHigherBid = 32`,
    `GoldByItemSoldOut = 34`. Ces deux membres nomment les deux moitiés de l'acte d'annulation :
    **l'objet rendu** et **l'or remboursé** ;
  * `ItemEntity.AuctionId` (`Game/DataAccess/Entities/Telecaster/ItemEntity.cs:18-19`), qui est ce
    par quoi le dépôt distingue une ligne d'annonce d'une ligne d'entrepôt :
    `StorageRepository.StorageRows` (`Game/DataAccess/Repositories/StorageRepository.cs:120-124`) et
    `StorageRules.IsStorageRow` (`Game/Services/StorageRules.cs:104-108`) excluent explicitement
    `AuctionId != null` de l'entrepôt de comptoir. Une annulation doit donc **dénouer** cette
    marque, pas seulement changer un état.

Le socle §6.3, ordre 6, borne d'ailleurs la carte : « `1310 CANCEL` | prérequis `1309` | décision
restante : remise de l'objet (entrepôt) et de la taxe ». Tant que `1309` n'existe pas (il en est à
sa fiche), une annulation n'a **rien** à annuler : le lot de `1310` livre la porte d'entrée et la
mesure, pas la mécanique.

### 5.5 Le cas « pas de code »

Partiel, et symétrique de celui de `1308` : le dev peut livrer et vérifier (a) le membre
d'énumération, (b) le lecteur borné, (c) le bras de dispatch qui lit, journalise et ne répond rien
(ou refuse par `ResultCode.InvalidArgument` sur trame courte), (d) les tests d'offsets. Il ne peut
pas livrer l'effet de l'annulation : ni le service d'enchère qui la porte, ni la décision de
Killian sur le sort de l'objet et de la taxe. C'est la même limite que pour `1306`, `1308` et
`1309` — et elle doit être écrite dans la MR, pas contournée par un `TODO` silencieux.

## 6. Écarts assumés avec NGemity

1. **NGemity n'implémente pas la famille** (§5.1) : aucun handler, aucune mécanique, et **rzu non
   plus** (`rzgame` : 0 occurrence). S'en écarter n'est pas un choix, c'est une absence — la
   validation ne peut venir que du client, dont toutes les mesures de cette fiche proviennent.
2. **Alias `2xxx`** : rzu déclare `2310` à partir de `EPIC_9_6_3` (`TS_CS_AUCTION_CANCEL.h:10`),
   NGemity déclare `1310` **sans** variante (`…CANCEL.h:9`) et le client 7.3 ne connaît que
   `0x51e`. Décision : `1310` seul, `2310` jamais déclaré (§4.1). C'est le même écart que le socle
   §7 a acté pour les neuf autres trames de la famille.
3. **Nommage du message interne** : NGemity n'a pas d'équivalent ; le nom
   `AUSIMSG_REQ_AUCTION_CANCEL` est celui du client (`0xc148a8`, cité par le socle §2). Aucun écart
   sémantique, seulement l'absence de pendant.
4. **`auction_uid` non signé** : NGemity et rzu **s'accordent** ici (`uint32_t`), contrairement à
   `1308`. Il n'y a donc pas d'écart à assumer pour ce paquet, et surtout pas de raison d'aligner
   `1310` sur l'`int32_t` de ses voisins (§4.3).

## 7. NON ÉTABLI

1. **Lien entre la chaîne `msgboxAuctionDeregister` (`0xa43768`) et le nom de boîte réellement
   construit.** Le constructeur de boîte reçoit le littéral `deregister` (`0xa25820`, `0x4f97ac`) ;
   la chaîne `msgboxAuctionDeregister` existe dans `.rdata` sans référence directe dans `.text`.
   *Question : la boîte de confirmation est-elle bien la ressource `msgboxAuctionDeregister`, et
   sous quel nom le clic de confirmation est-il émis ?* (Réponse dans les données d'interface, non
   lisibles ici : les `.nui` de la famille ne sont pas dans l'extraction de `reference/client73`.)
2. **Le saut qui recopie le nom de la boîte dans l'`AUSIMSG_UI_SEND_DATA` reçu par la fenêtre**
   n'est pas épinglé (§2). Ce qui est mesuré, c'est l'entrée du chemin : un `AUSIMSG_UI_SEND_DATA`
   dont le nom vaut exactement `deregister` déclenche l'émission. *Question : cette reprise est-elle
   générique (toute boîte confirmée renvoie son nom à sa fenêtre propriétaire) ?* La réponse
   n'empêche pas d'implémenter : le serveur ne voit qu'une trame de 11 octets.
3. **Largeur du modèle à la réception.** Le lecteur rendra un `uint32` ; `AuctionInfo.AuctionUid`
   est un `int` (`GameAuctionPackets.cs:14`) et `Entity.Id` un `long`
   (`Game/DataAccess/Entities/Entity.cs:8`). *Question : une annonce dont l'`Id` dépasse
   `uint.MaxValue` peut-elle exister, et quelle conversion appliquer à la comparaison ?* En pratique
   la clé d'annonce tient sur 32 bits dans les deux références (`Auction.sid int(11)`,
   `Telecaster.sql:47`), mais le dépôt ne le contraint pas explicitement.
4. **Le devenir de l'annonce et des deux flux** — le point que le socle §6.3 et §9 rang 8
   laissaient ouvert, inchangé par cette fiche : **l'objet rendu** et **la taxe**. Ce qui est
   mesuré est la *nomination* de l'acte, pas sa mécanique : `ItemByCancel = 4` et `GoldByCancel = 33`
   existent (`StorageType.cs:10,15`), `ItemStorageEntity.RelatedAuctionId` existe (`:20`) et sa FK
   est déclarée (`TelecasterContext.cs:166-169`). Ce qui manque : la ligne d'annonce doit-elle être
   **supprimée** ou marquée — aucune des deux tables de référence ne porte de colonne d'état
   (`Telecaster.sql:46-60`, et `AuctionEntity` non plus) ? L'objet rendu va-t-il dans l'inventaire ou
   dans `ItemStorageEntity` (fenêtre « objets gardés », dont le client porte les trames `1350` et
   `1352`, clés internes 1162 et 1164 — la clé intermédiaire 1163 n'est pas câblée, §3.3) ? La taxe
   est-elle remboursée, partiellement ou pas du tout ? *Questions à trancher par Killian — ce sont
   des règles de jeu, pas des mesures.*
5. **Le refus sur trame courte** : `ResultCode.InvalidArgument` est le patron de la famille
   (fiche `1308` §5.2), mais aucun `ResultCode` d'enchère n'existe dans le dépôt. *Question : à
   défaut, `InvalidArgument` suffit-il, ou faut-il un code dédié ?* Aucune source ne permet de
   trancher ; la mesure client (§5.3) montre seulement que la fenêtre ne consomme **aucun** résultat
   pour `0x51e`.

## 8. A VERIFIER PAR KILLIAN

1. **Le sort de l'objet et de la taxe** (§7 question 4) : entrepôt de comptoir ou `ItemStorageEntity`
   « objets gardés », remboursement de `RegistrationTax` ou non. C'est une règle de jeu, et elle
   conditionne la carte suivante, pas celle-ci.
   *État du code (28/09/2026) : inchangé, aucune exécution livrée — l'acte reste à trancher (§14.5).*
2. **La disparition ou le marquage de la ligne d'annonce annulée** : ni le modèle du dépôt ni le
   schéma de NGemity ne portent de colonne d'état ; si le serveur doit conserver l'historique, c'est
   une colonne à ajouter, donc une décision.
   *État du code (28/09/2026) : inchangé — aucun modèle d'annonce n'existe pour l'instant ; le lot n'a
   créé ni entité ni migration (`dotnet ef` est absent de ce conteneur).*
3. **Le `ResultCode` d'un refus** (§7 question 5), si un refus doit sortir autrement qu'en
   journalisation.
   *État du code (28/09/2026) : le refus livré est `InvalidArgument` sur trame mal formée seulement,
   comme les cinq autres lots de la famille ; rien n'est envoyé sur trame valide.*
4. **Le nom de la boîte de confirmation** (§7 question 1) : à confirmer depuis les données
   d'interface si un jour un texte de client doit être cité (aucune exécution de client ici).
   *État du code (28/09/2026) : sans objet dans le code livré — aucun texte de client n'y est cité.*

## 9. Commits et binaires épinglés

| référence | révision | usage dans cette fiche |
|---|---|---|
| client de référence | `SFrame.exe` sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 octets) | toutes les mesures de code, de chaînes et de RTTI ; jamais exécuté |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | `librzu/src/packets/GameClient/TS_CS_AUCTION_CANCEL.h`, `TS_SC_AUCTION_SEARCH.h` (`TS_AUCTION_INFO.auction_uid` : `int32_t`), `rzgame/src/` (aucune logique d'enchère) |
| NGemity | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | `shared/Server/Packets/GameClient/TS_CS_AUCTION_CANCEL.h`, `shared/Server/XPacket.h`, `shared/Server/ClientPackets.h`, `Database/Telecaster.sql`, `Chihiro/src/` (aucun handler) |
| dépôt | `master` = `b56967a07430422add88e0e5cdf292b41b18f6c6` | état à la base, `op_codes.md`, `GamePackets.cs`, `GameClient.cs`, `GameAuctionPackets.cs`, le modèle d'entités et du stockage |

## 10. Bloc destiné à `CLAUDE.md`

> ### Enchères — l'acte d'annulation `TM_CS_AUCTION_CANCEL` (1310)
>
> Septième et dernier acte de la famille (après 1300/1302/1304/1306/1308/1309). **11 octets** :
> `Length` (4) + `Id` `0x51e` (2) + `Checksum` (1, **constante `0x2e`** car le champ est hors somme)
> + `auction_uid` **`uint32` en +7** (4). Rien d'autre. rzu gate l'identifiant (`1310` si
> `< EPIC_9_6_3`, `2310` sinon) : en 7.3 c'est **1310**, et `2310` n'apparaît nulle part dans le
> client. Seul paquet de la famille où rzu **et** NGemity déclarent tous deux `uint32_t`.
>
> Le client ne l'émet que depuis **`SUIAuctionRegisterWnd`** (slot 50 de la vtable `0xa25df4`,
> `0x4fb820`), sur le nom d'action **`deregister`** (chaîne `0xa25820`, 10 caractères exactement) —
> et non depuis `SUIAuctionSearchWnd`/`SUIAuctionTenderWnd` comme `1308`. Chemin : message interne
> **1161** (`0x489`, vtable `AUSIMSG_REQ_AUCTION_CANCEL` `0xa25738`) → stub `0x49e3bb` → sender
> unique `0x48dfc0`. À ne pas confondre avec `1159` (1308) ni `1160` (1309).
>
> **Aucun paquet de réponse dédié** : le gestionnaire de `TM_SC_RESULT` (`0x66db80`) a une branche
> pour `0x51e` (atteinte par `sub $0x51c` puis deux `dec`, libellé `경매 취소`, chaîne `0xa524fc`),
> mais **la fenêtre ne consomme pas ce résultat** : elle compare `0x13(%msg)` à `0x516` = **1302**
> seulement. Juste après avoir émis `1310`, elle **redemande elle-même** sa liste de ventes
> (`1154` → `1302`, page 1) : le rafraîchissement passe par la réponse `1303`, jamais par une trame
> de `1310`.
>
> Le lot livre l'énumération + le lecteur borné (11 octets exactement) + le bras de dispatch
> (journalisation, refus `ResultCode.InvalidArgument` sur toute trame qui n'a pas exactement 11 octets) et **n'exécute rien** :
> aucun service ni repository d'enchère n'existe, et le sort de l'objet et de la taxe est une règle
> de jeu non tranchée. Détail, sources et questions ouvertes :
> `docs/packet-specs/1310-auction-cancel.md`.

## 11. Références croisées

* `docs/packet-specs/socle-encheres.md` — §2 (le chemin client de la famille ; sa ligne `1310` est
  resserrée par la présente fiche, §12), §3.1 (en-tête commun, où le client `0x48dff0` est déjà cité
  pour `1310`), §3.5 (les 11 octets et la décision `uint32`), §4.2 (les identifiants 7.3),
  §5.3 (`1310` : aucun paquet dédié), §5.5 (le modèle disponible), §6.3 ordre 6 (prérequis `1309`,
  décisions restantes), §7 (les écarts avec NGemity), §8.7 (le trou `1307`), §9 rang 8 (le sort de
  l'objet et de la taxe), §11 (méthode de lecture).
* `docs/packet-specs/1308-auction-instant-purchase.md` — la carte jumelle : mêmes 11 octets, même
  en-tête en ligne, mêmes conventions de lecteur et de bras ; sa §3.3 est la table des clés
  internes reprise ici, et sa §14 est le patron de la livraison de dev.
* `docs/packet-specs/1309-auction-register.md` — la carte prérequise ; sa §2 relève que le bouton
  `deregister` « demande une liste (`1154` → `1302`) avant de faire autre chose » et renvoie
  explicitement l'instruction de ce clic à la carte `1310` : la présente fiche la donne (§2, §3.4).
* `docs/packet-specs/1306-auction-bid.md` — le troisième acte sans réponse dédiée, même conclusion
  « rien à exécuter ».
* `docs/packet-specs/1302-auction-selling-list.md` — la demande que le client réémet lui-même après
  une annulation (§3.4) et la liste `1303` qui la suit.
* `CLAUDE.md` — renvoie au répertoire `docs/packet-specs/` ; le bloc §10 est destiné à y être
  recopié par Hermes.

## 12. Ce que cette fiche resserre, corrige ou confirme

1. **La fenêtre émettrice est resserrée.** Le socle §2 range `1310` sous « `SUIAuctionWnd` /
   `SUIAuctionDepositWnd` ». La mesure dit plus précisément : **une seule** classe construit le
   message interne, `SUIAuctionRegisterWnd` (slot 50 de la vtable `0xa25df4`, `0x4fb820`), et le
   seul appelant de l'émetteur est la branche `deregister` de ce slot (`0x4fb945`). Il n'existe
   qu'un site écrivant la vtable `0xa25738` (`0x4f8ab3`) et qu'un site écrivant la clé `0x489`
   (`0x4f8a9b`). `1310` est donc le paquet de la famille dont la fenêtre émettrice est la plus
   étroite, là où `1308` en a deux.
2. **La trame de `1310` est identique à celle de `1308` hors identifiant** — la fiche `1308` §3.2
   l'avait relevé pour la forme (« `1308` et `1310` construisent leur en-tête en ligne ») ; ce qui
   est ajouté ici est la liste exacte de ce qui diffère (§3.2), c'est-à-dire la raison pour laquelle
   un lecteur propre est nécessaire.
3. **`2310` n'existe pas en 7.3, contrôle indépendant.** La fiche `1308` §4.1 s'appuyait sur le
   client pour `0x51c` ; pour `0x51e`, le contrôle est fait ici des deux côtés : `mov $0x51e,%eax`
   unique en `0x48dfd6`, et aucun immédiat `0x906` de code dans `SFrame.exe`.
4. **Le rafraîchissement après annulation est celui du client.** Le socle §5.4 énonçait la règle
   générale (« un flash de liste se fait par un nouvel envoi de 1301/1303/1305 ») ; pour `1310`, la
   mesure est plus forte : **le client émet lui-même la demande `1302`** dans le corps de
   l'émetteur (§3.4), et il **n'écoute aucun résultat** sur `0x51e` (§5.3). Le serveur n'a donc rien
   à pousser spontanément.
5. **`ItemByCancel` et `GoldByCancel` sont nommés par le modèle existant** (`StorageType.cs:10,15`) :
   le socle §5.5 les listait déjà, mais sans les rapprocher de `1310`. Cette fiche les rattache à
   l'acte d'annulation et en fait des réserves nommées (§7 question 4, §8 rang 1) plutôt qu'une
   mécanique devinée.
6. **La leçon de la fiche `1308` sur les clés internes tient pour la dernière clé de la famille.**
   Le contrôle indépendant (site d'écriture de la clé sur l'objet message) et le contrôle par la
   table de dispatch (`0x49ea50`/`0x49e98c`) donnent tous deux `1161` → `0x48dfc0` (§3.3). Aucune
   clé voisine ne produit cet indice.

## 13. Vérifications exécutées

Sur `master` = `b56967a07430422add88e0e5cdf292b41b18f6c6`, avant toute écriture :

| commande | code de sortie | résultat |
|---|---|---|
| `dotnet build Navislamia.sln -c Debug` (`NUGET_PACKAGES=/srv/navislamia/.nuget-cache`) | **0** | `0 Error(s)`, 4 s |
| `dotnet test Tests/Tests.csproj` | **0** | `Passed! - Failed: 0, Passed: 1302, Skipped: 0, Total: 1302` |
| `git log --oneline origin/master..master` | 0 | **vide** (aucun commit local sur `master`) |
| `git grep -n "TryReadAuction" origin/master -- Game Tests` | 1 (aucune correspondance) | confirme l'absence de lecteur d'enchère (§1) |
| `grep -rn "Auction" reference/rzu/rzgame/src/` | — | **0** occurrence : aucune logique d'enchère dans rzu (§5.1) |
| `grep -rni auction reference/ngemity/Chihiro/src/` | — | 8 occurrences, toutes des colonnes/énums d'objet (§5.1) |

Cette fiche ne livrant que du documentaire, le plancher de tests et le build restent ceux mesurés
ci-dessus : c'est la base sur laquelle le lot de dev (section 14) doit poser son incrément, et
`1302` est le plancher qu'il ne peut pas faire baisser.

## 14. Livraison dev

Livré le **28/09/2026** sur cette branche, par le lot `navis-dev`, en un commit de code et de tests
(`dab93b6`) après les deux commits documentaires de `navis-ref` (`1712a26`, `a5d6f62`). Les sections 1
à 13 ne sont pas réécrites.

### 14.1 La checklist de la fiche, point par point

| # | attendu (§) | état | preuve |
|---|---|---|---|
| 1 | membre d'énumération `TM_CS_AUCTION_CANCEL = 1310`, `2310` jamais déclaré (§4.1) | **fait** | `Game/Network/Packets/Enums/GamePackets.cs:168` ; test `Id_IsTheEpic73OneAndTheRemapStaysClosed` (`Enum.IsDefined(2310) == false`) |
| 2 | lecteur borné `TryReadAuctionCancel`, **exactement 11 octets**, `uint32` @7 (§5.2, §4.2, §4.3) | **fait** | `Game/Network/Packets/Game/GameAuctionPackets.cs:196-208`, constantes publiques `:179-187` |
| 3 | bras de dispatch **avant** le `switch` final, lecture + journalisation, aucune exécution (§5.5) | **fait** | `Game/Network/Clients/GameClient.cs:1864-1875` ; handler `:448-474` |
| 4 | refus `ResultCode.InvalidArgument` sur trame mal formée (§5.2) | **fait** | handler `GameClient.cs:462-467` ; test `CancelRequest_MalformedFrameIsRefusedWithTheFamilyResult` |
| 5 | aucun paquet de réponse sur trame valide (§5.3) | **fait** | test `CancelRequest_ExecutesNothingAndAnswersNothing` (`Sent` vide) |
| 6 | aucun service, aucun repository, aucune exécution (§5.4) | **fait par construction** | seul fichier touché côté `Game` : les trois ci-dessus ; `git diff` ne crée aucun service |
| 7 | tests d'offsets (§5.5d) | **fait** | `Tests/Game/AuctionCancelPacketsTests.cs`, **14 cas** |
| 8 | rien de nouveau dans `CLAUDE.md` (§10 est le bloc destiné à Hermes, qui n'écrit pas ce fichier) | **fait** | le bloc §10 reste la source ; il est recopié dans la description de la MR par le QA |

### 14.2 Fichiers livrés

| fichier | nature | repère |
|---|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | +10 lignes : commentaire daté + `TM_CS_AUCTION_CANCEL = 1310` | après `TM_SC_AUCTION_BIDDED_LIST = 1305` |
| `Game/Network/Packets/Game/GameAuctionPackets.cs` | +41 lignes : constantes `AuctionCancelRequestAuctionUidOffset` / `AuctionCancelRequestSize` et lecteur `TryReadAuctionCancel` | entre `WriteFixedAscii` et `CreatePacket` |
| `Game/Network/Clients/GameClient.cs` | +28 lignes de handler `HandleAuctionCancel` (+13 lignes de bras dans `OnDataReceived`) | handler après `HandleGetSummonSetupInfo` ; bras entre l'anti-triche (54) et `TM_CS_CHECK_ILLEGAL_USER` (57) |
| `Tests/Game/AuctionCancelPacketsTests.cs` | fichier neuf, 14 cas | — |

Aucun fichier des jumelles n'est touché : le lecteur, les constantes et le handler sont **définis sur
cette branche**, jamais empruntés à `1308` (qui en a des homonymes proches, `TryReadAuctionInstantPurchase`
et `InstantPurchaseRequest*`). La branche compile donc seule, sur la base `b56967a` : c'est la propriété
que mesure le `merge-tree` du §14.6, il n'y a pas de dépendance de compilation croisée.

### 14.3 Offsets livrés et noms des tests qui les épinglent

| offset | taille | champ | test |
|---|---|---|---|
| 0 | 4 | `Length` = 11 (`0B 00 00 00`) | `Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame` (+ `NotBe` gros-boutiste) |
| 4 | 2 | `Id` = 1310 (`1E 05`) | idem (+ `BinaryPrimitives.ReadUInt16LittleEndian == 1310`) |
| 6 | 1 | `Checksum` = **`0x2E`** (11 + 0x1E + 0x05) | idem, et `Request_IsElevenBytesWithTheMeasuredOffsets` pour les constantes |
| 7 | 4 | `auction_uid` `uint32` | idem, `Request_CarriesTheUidInThirtyTwoBits` (3 000 000 000), `TryReadAuctionCancel_ReadsTheUidUnsigned` (`uint.MaxValue`) |

Toutes les trames de test sont écrites **octet par octet à la main** (`0x0B`, `0x1E`, `0x05`), jamais
reconstruites par un helper de production : le contrôle est bien celui du client, pas un aller-retour
par le lecteur testé.

Dispatch : `CancelRequest_IsConsumedByTheReceiveLoopWithoutThrowing`,
`CancelRequest_ExecutesNothingAndAnswersNothing`, `CancelRequest_MalformedFrameIsRefusedWithTheFamilyResult`,
`CancelRequest_KeepsTheLoopOnAFrameCoalescedWithAnotherOne` passent par la vraie boucle
(`StorageTestHarness.FrameConnection` + `NewGameClient`) : `BytesAvailable == 0` (trame consommée),
`Sent` vide sauf pour la trame mal formée (un `TM_SC_RESULT` de 15 octets, `request_msg_id = 1310`,
`result = InvalidArgument`).

### 14.4 Une divergence assumée avec la jumelle `1308` : le lecteur refuse **toute** taille autre que 11

Le §5.2 prescrit « accepte **exactement 11 octets** (`HeaderSize + 4`) … rend `false` sur toute autre
taille » et cite le patron `GameActionPackets.TryReadSummonCardSkillList` (`:87-98`), qui teste
`packet.Length != HeaderSize + 4`. Le lecteur livre donc `packet.Length != AuctionCancelRequestSize`
(comme `1309`, qui a fait le même choix pour la même raison), là où sa jumelle `1308` a retenu
`packet.Length < InstantPurchaseRequestSize`. Conséquence mesurée, et elle est testée : une trame de
**12 octets** est refusée (cas `TryReadAuctionCancel_RejectsAPaddedFrame`) et déclenche le refus
`InvalidArgument`, alors que `1308` lirait ses quatre octets et l'accepterait. Cette forme n'est pas
atteignable depuis le réseau — `OnDataReceived` recopie `header.Length` octets, donc le lecteur reçoit
exactement la longueur déclarée — mais elle est distincte, et un relecteur QA qui compare les deux
jumelles doit la lire comme un choix de fiche, pas comme une inattention.

### 14.5 Ce qui n'est pas porté, et pourquoi

* **L'acte lui-même** (objet rendu, taxe, ligne d'annonce) : le §5.4 est catégorique — aucun service ni
  repository d'enchère n'existe, `TM_CS_AUCTION_REGISTER` (1309) n'est pas déclaré, et le devenir des
  deux flux est une règle de jeu (§7 question 4, §8 rang 1). Aucun `TODO` silencieux : le handler le dit
  dans son commentaire.
* **Le rafraîchissement après annulation** : c'est le client qui réémet `1302` lui-même (§3.4) ; le
  serveur ne pousse rien, et aucun `1303` n'est envoyé ici (la réponse à `1302` est le lot `1302/1303`).
* **`2310`** : jamais déclaré (§4.1) ; l'id `1310` est par ailleurs réattribué à `TS_SC_TAMING_INFO` à
  partir de `EPIC_9_6_3`, ce qui est rappelé dans le commentaire de l'énumération et dans un test.
* **Aucun `ResultCode` d'enchère** : il n'en existe aucun dans le dépôt ; le refus utilise
  `InvalidArgument`, le patron de la famille (§7 question 5).

### 14.6 Fusionnabilité mesurée (`git merge-tree --write-tree --name-only <sœur> HEAD`)

Relevé le 28/09/2026 sur le commit de code `dab93b6` (le `HEAD` final `8061d5c` ne touche que cette
fiche, aucun fichier fusionné par les sœurs), git 2.39.5 :

| sœur | code | fichiers en conflit |
|---|---|---|
| `hermes/packet-1300-auction-search` | **0** | — |
| `hermes/packet-1302-auction-selling-list` | **0** | — |
| `hermes/packet-1304-auction-bidded-list` | **0** | — |
| `hermes/packet-1306-auction-bid` | 1 | `Game/Network/Packets/Enums/GamePackets.cs` seul |
| `hermes/packet-1308-auction-instant-purchase` | 1 | idem |
| `hermes/packet-1309-auction-register` | 1 | idem |

Trois collisions, **toutes dans la bande `13xx` de l'énumération et aucune ailleurs** : `GameClient.cs`
et `GameAuctionPackets.cs` fusionnent proprement avec les six sœurs, ce qui est le résultat du placement
choisi (§14.2 : le bras dans un interstice que personne n'occupe — anti-triche / illegal-user — et le
lecteur entre `WriteFixedAscii` et `CreatePacket`, quand 1300/1302/1304/1306/1308/1309 ont chacune pris
une autre bande de ces deux fichiers).

Cette collision d'énumération est **préexistante et non imputable à ce lot** : les trois sœurs concernées
insèrent exactement à la même ancre (après la ligne 159 de base, le blanc qui suit
`TM_SC_AUCTION_BIDDED_LIST = 1305`) et collisionnent déjà **entre elles sans cette branche** — mesuré :
`1306 vs 1308`, `1306 vs 1309`, `1308 vs 1309` → code 1 sur `GamePackets.cs`, alors que
`1304 vs 1306` et `1304 vs 1309` → code 0. Un emplacement alternatif a été essayé (bloc collé à la ligne
`1305`, sans blanc intercalaire, donc inséré une ligne plus haut) : **même résultat**, les trois mêmes
sœurs collisent. Il n'existe aucun point d'insertion dans la bande à trois lignes de distance de la
grappe sans tomber sur l'ancre d'une autre sœur, et déplacer le membre hors de la bande `13xx` (par
exemple après `TM_CS_CONTACT = 3002`) ferait payer au lot un défaut de rangement visible en relecture.

`hotspot: Game/Network/Packets/Enums/GamePackets.cs` — quatre branches de la famille (1306, 1308, 1309,
1310) insèrent leur membre à la même ancre (après `TM_SC_AUCTION_BIDDED_LIST = 1305`) ; ce fichier est le
seul point de collision de la famille, et il collisionne déjà sans le lot 1310.

### 14.7 Morsure du bras de dispatch (mesurée, puis annulée)

Le bras a été rendu inatteignable **sans casser la compilation** — `header.ID == (ushort)GamePackets.TM_CS_AUCTION_CANCEL`
remplacé par `header.ID == (ushort)GamePackets.TM_NONE`, condition fausse à cet endroit puisque le
keepalive est déjà traité plus haut — puis :

```
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~AuctionCancelPacketsTests"
→ exit 1, Failed: 4, Passed: 10, Total: 14
→ 4 occurrences de « Unknown Packet Type 1310 » dans la sortie
```

Les quatre échecs sont les quatre cas de boucle de réception
(`…IsConsumedByTheReceiveLoopWithoutThrowing`, `…ExecutesNothingAndAnswersNothing`,
`…MalformedFrameIsRefusedWithTheFamilyResult`, `…KeepsTheLoopOnAFrameCoalescedWithAnotherOne`) : sans le
bras, `1310` tombe bien dans le `switch` final. Restauration par `git checkout -- Game/Network/Clients/GameClient.cs`,
`git status --porcelain` **vide**, puis le filtre rejoué rend `Failed: 0, Passed: 14`.

### 14.8 Invariant énumération / dispatch, mesuré avant et après

* Avant (`GamePackets.cs` de `origin/master`, 139 membres) : **44** membres sans bras dans
  `GameClient.cs`, tous des `TM_SC_*` (émissions serveur → client).
* Après : **140** membres, **44** sans bras — **la même liste, au nom près d'aucun** (`diff` vide).
  `TM_CS_AUCTION_CANCEL` est référencé dans `OnDataReceived`, donc aucun membre neuf ne peut atteindre le
  `throw` final. Le seul membre bidirectionnel de la famille reste `TM_EQUIP_SUMMON` (303), qui a son bras.

### 14.9 Commandes relevées et codes de sortie

| commande | code | résultat |
|---|---|---|
| `dotnet build Navislamia.sln -c Debug` (`NUGET_PACKAGES=/srv/navislamia/.nuget-cache`) | **0** | `0 Error(s)`, 164 avertissements préexistants |
| `dotnet test Tests/Tests.csproj` (base `b56967a`, avant écriture) | **0** | `Passed: 1302` — plancher de la §13 confirmé |
| `dotnet test Tests/Tests.csproj` (après le lot) | **0** | `Passed: 1316, Failed: 0` — +14, aucun test retiré |
| `dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~AuctionCancelPacketsTests"` | **0** | `Passed: 14` |
| le même filtre, bras muté (§14.7) | 1 | `Failed: 4` (tous `Unknown Packet Type 1310`) |
| le même filtre, après `git checkout --` | **0** | `Passed: 14`, `git status --porcelain` vide |
| `git merge-tree --write-tree --name-only <sœur> HEAD` × 6 | voir §14.6 | 0, 0, 0, 1, 1, 1 |
| `git log --oneline origin/master..master` | — | **vide** (aucun commit sur `master` locale) |

### 14.10 Réserve qui ne relève pas du code

Comme les lots `1306`, `1308` et `1309`, la validation **côté client** reste hors de portée de ce
profil : `SFrame.exe` n'est jamais exécuté ici, et ce conteneur n'a ni désassembleur ni `objdump`
(relevé le 22/09/2026). Les mesures d'adresses du §3 sont celles de `navis-ref`, lues statiquement ; le
lot les prend telles quelles et les épingle par des tests d'octets, ce qui est le maximum vérifiable
sans client.

