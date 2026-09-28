# `TM_CS_AUCTION_INSTANT_PURCHASE` (1308) — fiche de paquet, Epic 7.3

> Fiche de l'archéologue (`navis-ref`) pour la branche `hermes/packet-1308-auction-instant-purchase`,
> cinquième carte de la famille « enchères » après 1300, 1302, 1304 et 1306.
> Le lot de dev ajoute sa livraison en **section 14** ; les sections 1 à 13 ne sont pas réécrites.
> Rien dans cette fiche n'a été obtenu en exécutant le client, un Lua ou un script du client : tout
> vient de la lecture statique de `SFrame.exe` (`objdump -d`), des trois dépôts de référence et du
> dépôt. Les adresses du client sont des adresses virtuelles (image base `0x400000`).

| | |
|---|---|
| id décimal | **1308** (jamais 2308 en 7.3, §4) |
| nom dans le dépôt | `TM_CS_AUCTION_INSTANT_PURCHASE` |
| sens | client → serveur |
| taille de trame | **11 octets** (§3.1) |
| branche | `hermes/packet-1308-auction-instant-purchase` |
| base | `master` = `b56967a07430422add88e0e5cdf292b41b18f6c6` |
| socle | `docs/packet-specs/socle-encheres.md` (§3.5, §4.2, §5.3, §6.3, §7.4, §11) |

---

## 1. Identité

* **Nom et identifiant dans le dépôt** : `Navislamia/op_codes.md:209`
  (`[1308] = "TM_CS_AUCTION_INSTANT_PURCHASE"`). Le nom est celui du fichier de rzu, sans
  transformation.
* **rzu** : `reference/rzu/librzu/src/packets/GameClient/TS_CS_AUCTION_INSTANT_PURCHASE.h` —
  `// Last tested: EPIC_9_8_1` (ligne 5), `_(simple)(int32_t, auction_uid)` (ligne 8),
  `X(1308, version < EPIC_9_6_3)` / `X(2308, version >= EPIC_9_6_3)` (lignes 11-12).
* **NGemity** : `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_AUCTION_INSTANT_PURCHASE.h:6-9`
  (`_(simple)(int32_t, auction_uid)`, `CREATE_PACKET(TS_CS_AUCTION_INSTANT_PURCHASE, 1308)`),
  inclus par `reference/ngemity/shared/Server/XPacket.h:57`, énuméré en
  `reference/ngemity/shared/Server/ClientPackets.h:216`.
* **État du dépôt à la base** : l'id 1308 n'est déclaré nulle part. La famille n'a, sur `master`,
  que les trois réponses serveur → client 1301/1303/1305
  (`Game/Network/Packets/Enums/GamePackets.cs:156-158`) et leur bras « log and drop »
  (`Game/Network/Clients/GameClient.cs:1405-1411`). Aucune méthode `Try*` d'enchère n'existe dans
  `Game/Network/Packets/Game/GameAuctionPackets.cs` : le lecteur de `1308` est à écrire (§5.2).
* **La carte jumelle** est `1306` (`TM_CS_AUCTION_BID`, branche `hermes/packet-1306-auction-bid`),
  qui a établi les conventions de code que cette fiche reprend (§5.2, §5.3). Les deux actes
  partagent les mêmes deux fenêtres émettrices (§3.3).

## 2. Ce que le joueur fait

Le joueur ouvre l'hôtel des ventes, choisit une annonce qui porte un **prix d'achat immédiat**
(`instant_purchase_price`, cf. `AuctionEntity.InstantPurchasePrice`,
`Game/DataAccess/Entities/Telecaster/AuctionEntity.cs:21`) et clique le bouton d'achat immédiat.
Le client construit alors un message interne `AUSIMSG_REQ_AUCTION_INSTANT_PURCHASE` et l'envoie ;
la trame qui part sur le fil est `0x051c` = 1308, 11 octets, portant l'`auction_uid` de la ligne
sélectionnée.

Deux fenêtres peuvent émettre cette demande, mesuré par leur slot 6 (procédure de messages/commandes) :

| fenêtre | enregistrement RTTI | vtable | slot 6 | méthode émettrice | site |
|---|---|---|---|---|---|
| `SUIAuctionSearchWnd` (liste des annonces) | TypeDescriptor `0xc14c18` | `0xa259cc` | `0x4feb60` | `0x4fdfe0` (appel en `0x4fef4c`) | `0x4fe01c`-`0x4fe05c` |
| `SUIAuctionTenderWnd` (fenêtre de l'annonce) | TypeDescriptor `0xc14d84` | `0xa25b0c` | `0x501220` | `0x5009c0` (appel en `0x501524`) | `0x5009fc`-`0x500a3c` |

C'est exactement le couple de fenêtres qui émet déjà `1306` : les mêmes slots 6 appellent les deux
frères, `0x4fdef0` (`1306`) puis `0x4fdfe0` (`1308`) en `0x4fef28`/`0x4fef4c` d'un côté,
`0x5008e0` (`1306`) puis `0x5009c0` (`1308`) en `0x501500`/`0x501524` de l'autre. Autrement dit
l'achat immédiat est proposé aussi bien depuis la liste de résultats que depuis la fiche de
l'annonce.

Le libellé coréen affiché au joueur quand la réponse arrive est `경매 즉구` — littéralement
« enchère, achat immédiat » (§5.4, chaîne `0xa524ac`).

## 3. Structure sur le fil

### 3.1 Trame `TM_CS_AUCTION_INSTANT_PURCHASE` — **11 octets**

| offset | taille | type (fil) | nom | valeur observée | source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` little-endian | `Length` | `11` (0x0b) | client `0x48debb` (`mov esi,0xb`), écrit en `0x48dec4` (`mov [ebp-0xc],esi`, base = `frame+0`) |
| 4 | 2 | `uint16` little-endian | `Id` | `0x051c` = 1308 | client `0x48deb6` (`mov eax,0x51c`), écrit en `0x48dec0` (`mov [ebp-0x8],ax` = `frame+4`) |
| 6 | 1 | `uint8` | `Checksum` | somme des octets 0 à 5 | client `0x48decc`-`0x48ded8` (boucle `add cl,[eax]` de `frame+0` à `frame+6`), écrit en `0x48ded8` (`mov [ebp-0x6],cl`) |
| 7 | 4 | `int32` (rzu) / `uint32` (dépôt) | `auction_uid` | uid de l'annonce sélectionnée, lu en `message+0x13` | rzu `TS_CS_AUCTION_INSTANT_PURCHASE.h:8` ; client `0x48dee0` (`mov eax,[edx+0x13]`) puis `0x48dee3` (`mov [ebp-0x5],eax` = `frame+7`) |

**Taille totale attendue : 11 octets** — 7 d'en-tête (`Length` + `Id` + `Checksum`) + 4 de charge.
Il n'y a **aucun champ conditionnel, aucune liste et aucun remplissage** : c'est la plus petite
trame de la famille avec `1310` (`TM_CS_AUCTION_CANCEL`, même forme). Le client n'écrit que
4 octets de charge (`mov` d'un `dword` lu en `message+0x13`), donc 11 est la seule taille valide.

Le `Checksum` suit la convention du dépôt (somme des six premiers octets, stockée en `+6`) ; cette
trame n'est jamais construite par le dépôt (elle est seulement lue), mais un test d'offsets doit
vérifier la lecture à `+7` sur une trame de 11 octets (critère 3, §13).

### 3.2 Où le client construit la trame

Un seul site de construction dans tout `SFrame.exe` : la fonction `0x48dea0`, dont le seul
appelant est le stub interne `0x49e3a1` (appel en `0x49e3a4`). Elle fait, dans l'ordre :

1. `0x48deb6` : `mov eax,0x51c` puis `0x48dec0` : `mov [frame+4],ax` — l'id ;
2. `0x48debb` : `mov esi,0xb` puis `0x48dec4` : `mov [frame+0],esi` — la longueur ;
3. `0x48dec8`-`0x48ded8` : le checksum sur les 6 premiers octets ;
4. `0x48dee0` : `mov eax,[message+0x13]` puis `0x48dee3` : `mov [frame+7],eax` — la charge ;
5. `0x48dee8`-`0x48def1` : checksum recalculé puis envoi par la vtable de connexion
   (`[conn+0xb8]` → slot `+0xc4`).

Différence de forme avec ses voisins, utile à qui relit le désassemblage : `1306` et `1309`
délèguent leur en-tête à des initialiseurs partagés (`0x48ca70` : id `0x51a`, longueur `0x13` ;
`0x48cad0` : id `0x51d`, longueur `0x20`) ; `1308` et `1310` (`0x48dfc0` : id `0x51e`, longueur
`0xb`) construisent leur en-tête **en ligne**. Il n'y a donc pas de « constructeur 1308 » à
chercher ailleurs que dans `0x48deb6`-`0x48dec4`.

### 3.3 Le chemin complet, du geste à la trame (rejouable)

1. **Geste** : slot 6 d'une des deux fenêtres (§2) → méthode émettrice (`0x4fdfe0` ou `0x5009c0`).
2. **Message interne** : l'émetteur réserve un message, y écrit l'identifiant interne **`0x487` = 1159**
   en `+4`, la vtable `0xa25964` en `+0`, met à zéro `+0xa`, `+0xb`, `+0xf`, `+0x13` (et pour la
   variante « recherche » `+0x8`), et recopie l'`auction_uid` de la ligne sélectionnée en `+0x13`
   (site `0x4fe01c`-`0x4fe05c` côté `SUIAuctionSearchWnd`, `0x5009fc`-`0x500a3c` côté
   `SUIAuctionTenderWnd` ; l'uid est lu par `0x5b9250` avec la clé `0xa25418` sur l'élément rendu
   par `0x5b9040`). Le message est ensuite mis en file par `0x649e20` sur l'objet `[this+0x440]`
   (appel en `0x4fe066` côté `SUIAuctionSearchWnd`, `0x500a46` côté `SUIAuctionTenderWnd`).
3. **RTTI** : la vtable `0xa25964` est précédée en `0xa25960` du localisateur `0xbbd5c0`
   (`signature 0`, `offset 0`, `cdOffset 0`) dont le `pTypeDescriptor` vaut `0xc14b98` ; le nom du
   type est **en ligne** à `0xc14ba0` = `.?AUSIMSG_REQ_AUCTION_INSTANT_PURCHASE@@`. La chaîne est
   donc bien celle du message interne, et non celle d'un homonyme.
4. **Table de dispatch interne** : la fonction `0x49e21d` fait
   `sub eax,0x403` / `cmp eax,0xde` / `movzx eax,BYTE PTR [eax+0x49ea50]` /
   `jmp DWORD PTR [eax*4+0x49e98c]`. La table d'octets `0x49ea50` (223 entrées, clés 1027 à 1249)
   donne l'indice dans la table de sauts `0x49e98c`. Pour la clé **1159** (`0x487`), l'octet
   `0x49ea76` vaut `0x25` = 37 et l'entrée 37 vaut `0x49e3a1` — le stub de `1308`. Aucune autre clé
   de la plage ne produit cet indice : la correspondance est bijective.
5. **Stub → trame** : `0x49e3a1` appelle `0x48dea0` (§3.2), qui envoie les 11 octets.

Les clés internes de la famille, mesurées par la même méthode (utile pour lire la fiche 1306) :

| clé interne | stub | sender | trame | nom rzu |
|---|---|---|---|---|
| 1152 (`0x480`) | `0x49e36d` | `0x48dc80` | 1300 | `REQ_AUCTION_SEARCH` |
| 1154 (`0x482`) | `0x49e37a` | `0x48dd10` | 1302 | `REQ_AUCTION_SELLING_LIST` |
| 1156 (`0x484`) | `0x49e387` | `0x48dda0` | 1304 | `REQ_AUCTION_BIDDED_LIST` |
| 1158 (`0x486`) | `0x49e394` | `0x48de30` | 1306 (19 o) | `REQ_AUCTION_BID` |
| **1159 (`0x487`)** | **`0x49e3a1`** | **`0x48dea0`** | **1308 (11 o)** | **`REQ_AUCTION_INSTANT_PURCHASE`** |
| 1160 (`0x488`) | `0x49e3ae` | `0x48df30` → `0x48cad0` (id `0x51d`, 32 o) | 1309 | `REQ_AUCTION_REGISTER` |
| 1161 (`0x489`) | `0x49e3bb` | `0x48dfc0` (id `0x51e`, 11 o) | 1310 | `REQ_AUCTION_CANCEL` |
| 1162 (`0x48a`) | `0x49e3c8` | `0x48e050` | 1350 | `REQ_AUCTION_ITEM_KEEPING_LIST` |

La clé `1159` se lit aussi sur l'objet du message (`movl $0x487,0x4(%eax)`, sites `0x4fe026` et
`0x500a06`), ce qui est le contrôle indépendant du tableau ci-dessus.

## 4. Gating de version, tranché pour 7.3

| champ / identifiant | gating rzu | décision pour 7.3 | raison |
|---|---|---|---|
| `auction_uid` | aucun (`_(simple)`) | **4 octets** au décalage 7 de la trame, `uint32` dans le dépôt | rzu ne gate pas le champ ; la trame du client lit un `dword` en `frame+7` |
| id du paquet | `X(1308, version < EPIC_9_6_3)`, `X(2308, version >= EPIC_9_6_3)` (`...h:11-12`) | **1308** | 7.3 (`EPIC_7_3`/`0x070300`) est **antérieur** à `EPIC_9_6_3` : la branche `<` gagne. `2308` n'existe pas en 7.3 et ne doit jamais être déclaré (le client 7.3 n'a qu'un site pour `0x51c`, §3.2) |
| sens | `SessionPacketOrigin::Client` | client → serveur | cohérent avec l'opcode et avec le site d'émission |

Rien d'autre dans le fichier de rzu n'est conditionné par une version : la trame est la même que
`EPIC_9_6_3` transforme ou non l'identifiant, ce qui explique qu'aucun piège de taille ne se cache
derrière ce `gating` (contraste avec `1300`, dont le champ `is_equipable` apparaît en `EPIC_7_2`,
et avec les trames serveur → client de la famille, qui changent de taille au même jalon).

**Écart de signedness, tranché** : rzu déclare `int32_t auction_uid` pour `1308` alors que `1310`
déclare `uint32_t` et que `1300`/`1302`/`1304`/`1306` déclarent `int32_t`. Le socle a tranché
pour le dépôt : `uint32` pour l'identifiant d'annonce (`socle-encheres.md` §3.5). L'écart est
**sans effet sur le fil** (complément à deux, même 4 octets) ; la fiche l'écrit pour que le dev
n'ouvre pas un débat et n'ajoute pas un champ signé pour ce seul paquet.

## 5. Traitement attendu

### 5.1 NGemity ne traite rien — et ne peut rien trancher sur ce paquet

* NGemity **déclare** la trame (`shared/Server/Packets/GameClient/TS_CS_AUCTION_INSTANT_PURCHASE.h:6-9`),
  l'inclut (`shared/Server/XPacket.h:57`) et l'énumère (`shared/Server/ClientPackets.h:216`) —
  exactement comme pour `1306`.
* NGemity **n'a aucun handler** : `grep -rni auction reference/ngemity/Chihiro/src/` ne rend que
  `Entities/Item/ItemTemplate.hpp:216` (`BY_AUCTION = 12`), `Entities/Item/Item.cpp:91,127`
  (`GetAuctionID()`, colonne `auction_id`) et `Map/WorldLocation.h:50` (`SecRouteAuction = 130107`,
  un identifiant de routage de zone, sans rapport avec la trame). Rien à porter.
* Le seul artefact NGemity qui parle de l'achat immédiat est un **schéma** :
  `reference/ngemity/Database/Telecaster.sql:46-59` définit la table `Auction`
  (`item_id`, `seller_id`, `seller_name`, `is_secroute_only`, `end_time`,
  `instant_purchase_price bigint(20)`, `registration_tax`, `bidder_list`
  (`varchar(1152)`), `highest_bidding_price`, `highest_bidder_id`, `highest_bidder_name`).
  Ce schéma recoupe le modèle du dépôt (`AuctionEntity`) et confirme la nature de
  `instant_purchase_price` : un prix, pas un drapeau. NGemity n'en fait rien de plus.
* Attention à la version compilée par NGemity (`EPIC_4_1_1`, cf. socle §7.4) : pour `1308` cela
  **ne change rien** (la trame est de 11 octets dans les deux branches), mais la méfiance reste
  de mise pour tout ce qui touche à `item_info`.

### 5.2 Le lecteur, à ajouter dans `GameAuctionPackets`

Rien n'existe aujourd'hui ; la convention à suivre est celle du lot `1306`
(`Game/Network/Packets/Game/GameAuctionPackets.cs:79` de la branche `hermes/packet-1306-auction-bid`) :

```csharp
public static bool TryReadAuctionInstantPurchase(ReadOnlySpan<byte> packet, out uint auctionUid)
```

* refuser toute trame de moins de **11 octets** (le lecteur ne doit pas lire `+7` à l'aveugle) ;
* lire l'`auction_uid` en **`packet[7..11]`**, en `uint32` (décision §4) ;
* ne rien lire d'autre : il n'y a pas d'autre champ.

### 5.3 Le bras de dispatch

`master` place ses bras sous forme de `if (header.ID == (ushort)GamePackets.X) { …; continue; }`
au-dessus du `switch` final qui jette (`Game/Network/Clients/GameClient.cs:1865`,
`_ => throw new Exception($"Unknown Packet Type {header.ID}")`). Le membre d'énumération et le
bras doivent être ajoutés **ensemble** (critère 4) :

* membre : `TM_CS_AUCTION_INSTANT_PURCHASE = 1308`, à insérer dans la bande des enchères
  (`GamePackets.cs:156-158`) — après `TM_SC_AUCTION_BIDDED_LIST = 1305` sur `master`, et après le
  bloc `TM_CS_AUCTION_BID = 1306` une fois la branche jumelle fusionnée (c'est l'ancrage naturel ;
  l'insertion de `1306` et celle de `1308` au même ancrage produisent un conflit de fusion textuel
  que Killian résoudra en gardant les deux lignes dans l'ordre 1306 puis 1308) ;
* bras : `HandleAuctionInstantPurchase(byte[] buffer)`, qui appelle le lecteur, envoie
  `ResultCode.InvalidArgument` si la trame est trop courte, et journalise sinon (§5.7).

### 5.4 Ce que le client attend en retour — la réponse existe, et elle est générique

**Il n'y a aucun paquet dédié à l'achat immédiat** : rzu ne connaît aucun
`TS_SC_AUCTION_INSTANT_PURCHASE`, et les quatre noms `RES_AUCTION_*` du client sont ceux des
quatre listes (`.?AUSIMSG_RES_AUCTION_SELLING_LIST@@` `0x81c780`, `..._BIDDED_LIST@@` `0x81c7b0`,
`..._ITEM_KEEPING_LIST@@` `0x81c7dc`, `..._SEARCH@@` `0x81c8fc`). La réponse est le **résultat
générique** `TM_SC_RESULT`, et le client lui réserve une branche dédiée :

* `TM_SC_RESULT` = **0** en 7.3 (rzu `TS_SC_RESULT.h:12-14` : `X(0, version < EPIC_9_6_3)`,
  `X(1000, version >= EPIC_9_6_3)`), soit `Navislamia/op_codes.md:2` ;
* charge = `uint16 request_msg_id`, `uint16 result`, `int32 value`
  (`Game/Network/Packets/Game/TS_SC_RESULT.cs:5-11`) ⇒ trame de **15 octets** avec l'en-tête de 7 ;
* le handler du client, fonction **`0x66db80`**, lit exactement ces décalages :
  `mov ax,WORD [esi+0x7]` (`0x66dbcc`) → `request_msg_id`, `mov cx,WORD [esi+0x9]` (`0x66dbd4`) →
  `result`, `mov edx,DWORD [esi+0xb]` (`0x66dbdc`) → `value`, puis dispatche sur
  `movzx eax,WORD [esi+0x7]` (`0x66dbe2`) ;
* ce dispatch a une **branche dédiée à 1308** : `sub eax,0x51c` / `je 0x66e09d` (`0x66e04a`),
  immédiatement suivie des branches `0x51d` (1309, `0x66e07c`) et `0x51e` (1310, `0x66e05b`). La
  branche `1308` formate son propre libellé, chaîne **`0xa524ac`** =
  `경매 즉구    요청결과-%s[%d]\n` (« achat immédiat — résultat de la demande »), à comparer à
  `0xa524d4` (`경매 등록`, enregistrement) et `0xa524fc` (`경매 취소`, annulation) ;
* le handler enfile ensuite un message interne `AUSMSG_RESULT` (type `4` en `+4`, vtable
  `0xa51ee8`) par `0x64d0e0` sur `[this+0x2c]` (`0x66e279`-`0x66e283`) : la réponse est donc
  consommée par l'interface, jamais retransmise au serveur.

Conséquence opérationnelle : **le client ne demande rien d'autre après avoir émis 1308**, et il
n'existe pas de « satisfier » dédié (`.?AUTypeSatisfier` n'est enregistré que pour les identifiants
internes `0x481`/`0x483`/`0x485`/`0x48b`, les quatre listes). Le serveur doit donc :

1. répondre `TM_SC_RESULT` (id 0, 15 octets) avec `request_msg_id = 1308`, `result` = code de la
   décision, `value` = 0 (aucun usage du champ `value` n'est visible dans la branche 1308 du
   client) ;
2. **rafraîchir les listes lui-même** s'il veut que les fenêtres montrent l'annonce disparue :
   aucun `RES` ne correspond à cet acte, le rafraîchissement se fait par un nouvel envoi de
   1301/1303/1305 (socle §5.4). Le contrat minimal du lot est la réponse 1 ; le rafraîchissement
   est une décision de jeu (§7).

### 5.5 Ce qui manque côté serveur, nommé et délimité

* Aucun écrivain d'enchère n'existe : ni achat immédiat, ni enchère, ni annulation, ni
  enregistrement. Le lot `1306` a déjà tranché pour sa part : journaliser et ne rien exécuter.
* Le **modèle** est là : `AuctionEntity` (`Game/DataAccess/Entities/Telecaster/AuctionEntity.cs:5-36`)
  porte `InstantPurchasePrice` (ligne 21), `SellerId`/`SellerName` (10-17), `EndTime` (20),
  `HighestBiddingPrice` (24), `HighestBidderId` (26), `BiddersIds` (23), `ItemStorage` (35) ;
  les motifs d'écriture de stockage sont énumérés
  (`Game/DataAccess/Entities/Enums/StorageType.cs`) : `ItemByInstantPurchase = 2` (ligne 8),
  `GoldByRegTax = 31` (13), `GoldByHigherBid = 32` (14), `GoldByItemSoldOut = 34` (16) ;
  `ItemStorageEntity` porte `RelatedAuctionId` (cf. socle §5.5).
* Ce que la transaction devra faire (débit de l'acheteur au prix `InstantPurchasePrice`, remise de
  l'objet à l'acheteur, sort du vendeur, taxe) est un travail d'exécution **hors périmètre du lot
  1308** : la fiche le décrit pour que le dev ne l'improvise pas, et renvoie les cas non tranchés
  en §7/§8.

### 5.6 Le prérequis `1309`

Comme pour `1306`, l'achat immédiat n'a de sens que sur une annonce **existante**, donc créée par
`TM_CS_AUCTION_REGISTER` (1309, clé interne 1160, trame de 32 octets) — non implémenté. Ce n'est
pas une raison de bloquer : le lot `1306` a livré sans `1309`, avec le même raisonnement (l'acte
est lu, journalisé et refusé proprement si la trame est illisible).

## 6. Écarts assumés avec NGemity

| # | écart | raison |
|---|---|---|
| 1 | NGemity n'a **aucun** handler pour 1308, le dépôt en aura un bras de dispatch | la référence la plus récente ne tranche pas cet acte ; le dépôt suit sa propre convention (bras + journalisation), comme pour `1306` |
| 2 | NGemity déclare `CREATE_PACKET(..., 1308)` sans macro de version, rzu a deux identifiants (`1308`/`2308`) | NGemity est plus récent que la famille mais son `CREATE_PACKET` simple est l'équivalent du cas 7.3 ; rzu reste l'arbitre de l'identifiant et confirme 1308 en `< EPIC_9_6_3` |
| 3 | NGemity compile `EPIC_4_1_1` : ses tailles de `item_info` (62 octets, socle §7.4) sont fausses pour 7.3 | **sans effet ici** : la trame 1308 fait 11 octets dans les deux branches, la seule divergence possible (un champ conditionnel) n'existe pas |
| 4 | Le dépôt prend `uint32` là où rzu écrit `int32_t` | décision de socle (§3.5 du socle, §4 ici), sans effet sur le fil |
| 5 | Le schéma NGemity (`Telecaster.sql`) est utilisé comme recoupement du modèle, jamais comme logique | NGemity n'en tire aucun code ; le dépôt a ses propres entités |

## 7. NON ÉTABLI

1. **Le code de résultat exact à renvoyer.** La branche `1308` du client se contente d'imprimer le
   nom du code (`0x425980`) : elle n'impose donc aucune valeur. La question ouverte n'est pas la
   trame (15 octets, id 0) mais la **table de codes** : quel `ResultCode` pour « annonce
   inexistante » / « annonce déjà vendue » / « or insuffisant » / « votre propre annonce ». La
   liste des codes vit dans `Game/Network/Packets/ResultCode.cs` ; le choix est de Killian (§8).
2. **Le rafraîchissement des listes par le serveur.** Mesuré : le handler de `TM_SC_RESULT`
   n'émet aucune trame (il enfile un message interne). Non mesuré : ce que fait la fenêtre
   `SUIAuctionTenderWnd`/`SUIAuctionSearchWnd` en recevant **`AUSMSG_RESULT`** — redemande-t-elle
   `1154`/`1156` (donc 1302/1304) d'elle-même, ou attend-elle un nouvel envoi ? La mesure est
   faisable (chercher un `push` de la clé `0x482`/`0x484` dans le chemin `AUSMSG_RESULT`), elle
   n'a pas été faite ici. Tant qu'elle ne l'est pas : le lot livre la réponse minimale (§5.4
   point 1) et **ne décide pas** du rafraîchissement.
3. **Le sort de l'enchérisseur en tête.** Si une annonce a déjà reçu une enchère et qu'un tiers
   l'achète immédiatement, faut-il rembourser l'enchérisseur en tête ? Le motif
   `StorageType.GoldByHigherBid = 32` existe et suggère que oui, mais **aucune source ne décrit la
   mécanique** (séquestre de l'enchère, remboursement à la surenchère ou à l'achat immédiat) : la
   fiche ne tranche pas, et le lot n'exécute rien.
4. **Le sens de `InstantPurchasePrice == 0`.** Une annonce sans prix d'achat immédiat est
   plausiblement codée par `0` (le client afficherait alors le champ vide), mais rien ne le
   prouve : ni rzu, ni NGemity, ni le dépôt ne documentent la sentinelle. Question à trancher au
   moment où l'achat immédiat sera exécuté, pas avant.
5. **L'auto-achat.** Aucune mesure n'établit si le client interdit au vendeur d'acheter sa propre
   annonce (le contrôle serait côté serveur de toute façon). À trancher avec la règle métier.
6. **La forme du `HandleAuctionInstantPurchase`.** La convention est celle de `1306` (§5.2/§5.3) ;
   si la branche `1306` n'est pas encore fusionnée au moment du lot, le dev reprend la forme de
   `master` (bras `if (…) { …; continue; }`) et insère le lecteur en tête de
   `GameAuctionPackets` — c'est un détail de forme, pas une décision à remonter.

## 8. A VERIFIER PAR KILLIAN

| # | question | ce que la décision change |
|---|---|---|
| 1 | Quels `ResultCode` pour « annonce inconnue », « déjà vendue », « or insuffisant », « votre annonce » ? | le contenu de la réponse `TM_SC_RESULT` ; le client affiche le nom du code, il ne le contraint pas |
| 2 | Le serveur doit-il renvoyer les listes (1301/1303/1305) après un achat immédiat réussi ? | visible par le joueur : sans renvoi, la ligne achetée peut rester affichée jusqu'à la prochaine demande |
| 3 | L'enchérisseur en tête est-il remboursé (motif `GoldByHigherBid = 32`) lors d'un achat immédiat ? | économie de l'hôtel des ventes ; aucun code à écrire dans ce lot, mais le modèle à prévoir |
| 4 | `InstantPurchasePrice = 0` signifie-t-il « pas d'achat immédiat » ? | affichage côté client et contrôle côté serveur au moment de l'exécution |

Les quatre questions portent sur l'**exécution** ou sur un **code de réponse** ; aucune ne change la
trame de 11 octets ni sa lecture, donc aucune ne bloque le lot tel que la carte le définit.

## 9. Commits et binaires épinglés

| référence | commit / empreinte |
|---|---|
| `rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02, « packets: fix TS_SC_INVENTORY with older epics ») |
| `ngemity` (Chihiro) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03, « Fix compilation issue for GCC ») |
| `master` du dépôt (base de la branche) | `b56967a07430422add88e0e5cdf292b41b18f6c6` |
| client 7.3 | `reference/client73/SFrame.exe`, `sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 octets |

`reference/client73/` n'est pas un dépôt Git : seule l'empreinte du binaire est opposable.

Méthode de lecture statique, pour rejouer les mesures sans exécuter le client (aucun `SFrame.exe`,
aucun Lua, aucun script client n'a été lancé) :

1. `objdump -d SFrame.exe` complet (`pei-i386`, base `0x401000`) puis lecture ciblée ;
2. **site d'émission** : `grep '\$0x51c'` ne rend que deux occurrences de code — `0x48deb6`
   (`mov $0x51c,%eax`, l'unique constructeur de trame) et `0x66e04a` (`sub $0x51c,%eax`, la
   branche du handler de résultat) ; les six autres sont des décalages de pile ou des `add ecx` ;
3. **chemin interne** : lecture de la table d'octets `0x49ea50` et de la table de sauts `0x49e98c`
   indexées par `clé - 0x403` (dispatch `0x49e21d`), puis lecture des stubs `0x49e340`-`0x49e3d5` ;
4. **identité RTTI** : vtable `V` → `dword(V-4)` = localisateur complet (`{signature, offset,
   cdOffset, pTypeDescriptor, pClassDescriptor}`, signature `0` en x86) → `TypeDescriptor`
   `{pVFTable, spare, name[]}` avec le nom **en ligne** à `td+8` ;
5. **fenêtres** : balayage des vtables (`dword(V-4)` dans `.rdata` et `dword(V-4+0xc)` dans
   `.data`) pour retrouver les classes `.?AVSUIAuctionSearchWnd@@` (`0xa259cc`) et
   `.?AVSUIAuctionTenderWnd@@` (`0xa25b0c`), puis lecture de leur slot 6.
6. **conversion** adresse virtuelle → offset fichier : `.text` (`vma 0x401000`, fichier
   `0x400`), `.data` (`vma 0xc10000`, fichier `0x80e200`), d'où `va - 0x401e00` pour `.data`
   (0xc14ba0 ↔ 0x812da0, la chaîne `.?AUSIMSG_REQ_AUCTION_INSTANT_PURCHASE@@`).

## 10. Bloc destiné à `CLAUDE.md`

> ### Enchères — l'acte d'achat immédiat `TM_CS_AUCTION_INSTANT_PURCHASE` (1308)
>
> Cinquième acte de la famille (après 1300/1302/1304/1306). **11 octets** : `Length` (4) + `Id`
> `0x51c` (2) + `Checksum` (1) + `auction_uid` **`uint32` en +7** (4). Rien d'autre : ni liste, ni
> champ conditionnel. rzu gate l'identifiant (`1308` si `< EPIC_9_6_3`, `2308` sinon) : en 7.3
> c'est **1308**, et le client 7.3 n'a qu'un site pour `0x51c`.
>
> Le client l'émet depuis les **deux mêmes fenêtres que 1306** (`SUIAuctionSearchWnd` slot 6
> `0x4feb60` et `SUIAuctionTenderWnd` slot 6 `0x501220`), via le message interne **1159** (`0x487`)
> et un sender unique (`0x48dea0`). À ne pas confondre avec `1158` (1306), `1160` (1309, 32 octets)
> et `1161` (1310).
>
> **Aucun paquet de réponse dédié** : la réponse est `TM_SC_RESULT` (id **0** en 7.3, 15 octets =
> `request_msg_id`/`result`/`value`), et le handler du client (`0x66db80`) a une branche dédiée à
> `0x51c` avec son propre libellé (`경매 즉구`, chaîne `0xa524ac`). Le client ne redemande rien : le
> rafraîchissement des listes, s'il est voulu, est un nouvel envoi de 1301/1303/1305.
>
> Le lot livre l'énumération + le lecteur + le bras de dispatch (journalisation, refus
> `ResultCode.InvalidArgument` sur trame courte) et **n'exécute rien** : aucun écrivain d'enchère
> n'existe dans le dépôt. Détail, sources et questions ouvertes :
> `docs/packet-specs/1308-auction-instant-purchase.md`.

## 11. Références croisées

* `docs/packet-specs/socle-encheres.md` — §3.5 (les sept identifiants de la famille et la
  décision `uint32` pour `auction_uid`), §4.2 (les identifiants 7.3 : 1300/1302/1304/1306/1308/1309/1310),
  §5.3 (`1308` : aucun paquet dédié, la réponse est le résultat générique), §5.5 (le modèle
  `AuctionEntity`/`ItemStorageEntity`/`StorageType`), §6.3 (carte 5 = 1308, prérequis 1309),
  §7.4 (`EPIC_4_1_1` de NGemity et ses tailles), §9 (arbitrages en attente), §11 (méthode).
* `docs/packet-specs/1306-auction-bid.md` — la carte jumelle : mêmes fenêtres émettrices, mêmes
  conventions de lecteur et de bras, et la même conclusion « rien à exécuter ».
* `docs/packet-specs/1302-auction-selling-list.md` et
  `docs/packet-specs/1304-auction-bidded-list.md` — le motif d'entrée de 97 octets et les deux
  listes que le serveur peut renvoyer après un achat (§5.4).
* `docs/packet-specs/1300-auction-search.md` — la recherche, dont le résultat sert la fenêtre
  `SUIAuctionSearchWnd` qui peut émettre 1308.
* `CLAUDE.md` — renvoie au répertoire `docs/packet-specs/` ; le bloc §10 est destiné à y être
  recopié par Hermes.

## 12. Ce que cette fiche resserre ou corrige

1. **La clé interne de 1308 est 1159, pas 1160.** La fiche `1306-auction-bid.md` (tableau de son
   §3.3, ligne « `1160` | `133` | `38` | `0x49e3ae` | `0x48df30` (1308) ») rattache la clé `1160` à
   `1308`. La mesure dit autre chose : la clé `1160` mène au stub `0x49e3ae` → `0x48df30`, qui
   appelle l'initialiseur `0x48cad0` écrivant `Id = 0x51d` (**1309**) et `Length = 0x20` (**32
   octets**, la taille de `TM_CS_AUCTION_REGISTER` au socle §3.6). `1308` est la clé **1159**
   (`0x487`) → stub `0x49e3a1` → `0x48dea0` (`Id = 0x51c`, `Length = 11`). Les deux autres lignes
   du tableau de `1306` (`1158` → `0x48de30` = 1306, `1162` → `0x48e050` = 1350) sont confirmées.
2. **Une seule preuve d'identité, et elle est nouvelle** : le rattachement clé ↔ nom ne repose pas
   sur l'ordre des chaînes RTTI mais sur la chaîne elle-même (`0xc14b98` → nom en ligne
   `0xc14ba0` = `.?AUSIMSG_REQ_AUCTION_INSTANT_PURCHASE@@`), lue par la vtable du message
   (`0xa25964`) effectivement écrite au site d'émission (`0x4fdfe0`, `0x5009c0`).
3. **Le site d'émission est double**, et ce sont les deux fenêtres de `1306` : le socle ne nommait
   qu'une fenêtre pour l'achat immédiat (§2, « fenêtre de l'annonce ») ; la mesure montre que la
   liste de résultats (`SUIAuctionSearchWnd`) l'offre aussi, par son slot 6, avec le message
   interne `1159`. La ligne « même slot 6 de deux fenêtres » de `1306` vaut donc aussi ici.
4. **« Aucun paquet dédié » est confirmé, mais il y a bien une branche dédiée côté client** : le
   handler `TM_SC_RESULT` (`0x66db80`) traite `0x51c` avec son propre libellé coréen. La nuance a
   une conséquence pratique : le serveur doit remplir `request_msg_id = 1308`, sinon le client
   affichera un libellé générique.
5. **Piège de lecture désamorcé** : un `push $0x487` existe aussi en `0x615f87` (et `0x488`/`0x489`
   en `0x615eaf`/`0x615f20`/`0x615fcc`/`0x6160b2`), dans un constructeur de boîte de dialogue qui
   initialise un tampon statique gardé (`0x43f6d0` teste le garde `ds:0xc49438`). Ces immédiats ne
   sont **pas** des identifiants de message : les seuls sites d'émission de `1308` sont
   `0x4fe01c`-`0x4fe05c` et `0x5009fc`-`0x500a3c` (§3.3).

## 13. Vérifications exécutées

Aucune n'a été faite en exécutant le client, un Lua ou un script client. Le lot est documentaire :
aucun fichier de `Game/` n'a été modifié.

| commande | code de sortie | résultat relevé |
|---|---|---|
| `git status --short` (base propre avant création de branche) | 0 | aucune modification |
| `git checkout -B hermes/packet-1308-auction-instant-purchase origin/master` | 0 | branche créée sur `b56967a07430422add88e0e5cdf292b41b18f6c6` |
| `export NUGET_PACKAGES=/srv/navislamia/.nuget-cache && dotnet build Navislamia.sln -c Debug` | 0 | `0 Error(s)`, 23 avertissements (build incrémental : seuls les projets restants sont recompilés) |
| `dotnet build Navislamia.sln -c Debug --no-incremental` | 0 | 164 avertissements, `0 Error(s)` (compte canonique du dépôt) |
| `dotnet test Tests/Tests.csproj` | 0 | `Passed! - Failed: 0, Passed: 1302, Skipped: 0, Total: 1302` — le plancher de 1302 de la carte est donc bien le compte de `master` |
| `git log --oneline origin/master..master` | 0 | vide (aucun commit sur `master`) |
| `git merge-tree --write-tree origin/master hermes/packet-<1300/1302/1304/1306>` | 0 pour les quatre | aucune collision : arbres `ded3186f19a`, `3facd30f98b`, `2e2d8da1657`, `1c08ee7d3c6`. La fiche n'ajoute qu'un fichier neuf ; le conflit d'énumération qui reste possible est celui des insertions de `1306` et `1308` au même ancrage (§5.3) |
| `grep -c "header.ID" Game/Network/Clients/GameClient.cs` | 0 | 97 occurrences (bras de réception déjà en place ; `1308` devra en ajouter un) |
| `grep -E '\$0x51c'` sur le désassemblage complet | 0 | 2 occurrences de code seulement (`0x48deb6`, `0x66e04a`), 6 occurrences de non-code |

Le compte de tests est celui de la base, mesuré à titre de repère pour le critère « le compte ne
baisse jamais » : le lot du dev doit atteindre **au moins 1302** tests (la carte demande 1302 ;
le socle des enchères a porté la barre de la famille par ses quatre lots précédents).

## 14. Implémentation livrée (dev)

*(section réservée au lot `navis-dev` : branche, commits, offsets livrés et tests d'offsets,
invariant « énumération et dispatch ensemble », réserves. Ne pas réécrire les sections 1 à 13 ;
compléter le §10 si la livraison ajoute une contrainte durable.)*
