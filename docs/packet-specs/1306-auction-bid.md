# `TM_CS_AUCTION_BID` (1306) — fiche de paquet, Epic 7.3

Fiche du **quatrième paquet du rang 2** de la famille des enchères (socle
`docs/packet-specs/socle-encheres.md` §6.3, rang 4), après `1300`, `1302` et `1304`. Elle porte
**la requête** `1306` — l'acte d'enchérir — et **la réponse générique** que le client attend
réellement pour cet acte (§5.4). Elle ne touche à aucun fichier de code.

Branche : `hermes/packet-1306-auction-bid`, créée depuis `origin/master`
`b56967a07430422add88e0e5cdf292b41b18f6c6`. **Aucun commit sur `master`.**

**Correction portée à la promotion de la carte.** Le commentaire de promotion annonçait « prérequis
`S2` **livré** ». C'est faux pour `1306` : le §6.3 du socle donne au rang 4 le prérequis **`1309`**
(« une annonce doit exister »), décision restante « pas de surenchère sur soi-même, montant minimal,
prix de réserve ». `S2` (pagination partagée) est le prérequis du rang 2 (`1302`/`1304`), livré par
le socle. Ce que le prérequis `1309` bloque exactement est mesuré au §5.5.

---

## 1. Identité

| | |
|---|---|
| id décimal | **1306** |
| nom | `TM_CS_AUCTION_BID` |
| source du nom | `op_codes.md:208` (`[1306] = "TM_CS_AUCTION_BID"` ; `[1307]` est absent, socle §8.7) |
| nom de référence rzu | `TS_CS_AUCTION_BID` (`librzu/src/packets/GameClient/TS_CS_AUCTION_BID.h:5-13`) |
| nom de référence NGemity | `TS_CS_AUCTION_BID` (`shared/Server/ClientPackets.h:215`, `Packets/GameClient/TS_CS_AUCTION_BID.h:6-10`) |
| sens | le joueur **enchérit sur une annonce existante** en saisissant un montant |
| session / origine | `SessionType::GameClient`, `SessionPacketOrigin::Client` (`TS_CS_AUCTION_BID.h:13`) |
| réponse | **aucun `TS_SC_AUCTION_*`** : la réponse est la trame générique `TM_SC_RESULT` (id `0`) portant `request_msg_id = 1306` — mesure §5.4 |

L'identifiant `1306` est **daté**, comme `1304` : rzu ne le déclare que pour `version < EPIC_9_6_3`
et le remappe en `2306` à partir d'`EPIC_9_6_3` (`TS_CS_AUCTION_BID.h:9-11`). En 7.3 c'est bien
`1306` (§4). Contrairement à `1304`/`304`, **aucun autre paquet de 7.3 ne revendique `1306`** : le
seul autre porteur du numéro est `TS_SC_UNSUMMON_NOTICE` et il ne l'est qu'à partir
d'`EPIC_9_6_3` (`reference/rzu/librzu/src/packets/GameClient/TS_SC_UNSUMMON_NOTICE.h:11`). Il n'y a
donc pas de piège d'alias à contourner ici, mais il y a une tentation à refuser : déclarer `2306`
(un identifiant qui n'appartient pas à 7.3).

---

## 2. Ce que le joueur fait

Le joueur **sélectionne une annonce** dans une liste d'enchères, **saisit un montant** et **valide**
(« enchérir »). Aucune autre action ne construit la trame : la chaîne est mesurée de bout en bout,
et elle n'a qu'un seul émetteur de trame (§3.4).

| élément | mesure (lecture statique de `SFrame.exe`) |
|---|---|
| message interne de demande | `.?AUSIMSG_REQ_AUCTION_BID@@` — vtable `0xa2596c`, COL `0xbbd60c`, `type_info` `0xc14bcc`, nom mangé à **`0xc14bd4`** (confirme le socle §3.4) |
| identifiant interne du message | **`0x486` = 1158** (écrit en `+4` de l'objet, `0x4fdf5a` et `0x50094a`) |
| taille de l'objet-message | 31 octets (`push 0x1f` puis `operator new` `0x97671b`, sites `0x4fdf4c` et `0x50093c`) |
| **site d'émission 1** | fonction `0x4fdef0`, écriture du message à `0x4fdf46` |
| **site d'émission 2** | fonction `0x5008e0`, écriture du message à `0x500937` |
| fenêtre du site 1 | `.?AVSUIAuctionSearchWnd@@` : la fonction `0x4feb60` est le **slot 6** de la vtable `0xa259cc`, et elle appelle `0x4fdef0` (`0x4fef28`) |
| fenêtre du site 2 | `.?AVSUIAuctionTenderWnd@@` : la fonction `0x501220` est le **slot 6** de la vtable `0xa25b0c`, et elle appelle `0x5008e0` (`0x501500`) |

**Deux fenêtres peuvent enchérir, un seul émetteur de trame.** Le socle §2 n'attribuait l'acte
qu'à `SUIAuctionTenderWnd` ; la mesure montre que la **fenêtre de recherche** a le même handler
(slot 6 de sa vtable, geste « enchérir » sur l'annonce sélectionnée dans les résultats). Les deux
chemins convergent sur le message interne 1158, puis sur un seul sender de trame (même slot 6 = même
geste, deux fenêtres). C'est une précision, pas une contradiction : le socle §3.4 annonçait déjà
« ses deux sites client ».

D'où viennent les deux valeurs, dans les deux sites (mesuré, mêmes appels dans `0x4fdef0` et
`0x5008e0`) :

1. l'**identifiant d'annonce** vient de l'élément **sélectionné** de la liste : `call 0x5b9250` sur
   la chaîne `"id"` (`0xa25418`), puis lecture du champ `+0x10` du résultat → `message+0x13` ;
2. le **montant** vient d'un **contrôle de saisie** dont la poignée est mémorisée dans la fenêtre
   (`[this+0x4b0]` pour le site 1, `[this+0x4bc]` pour le site 2), passé à `0x501f70` puis
   `0x501d40`, qui produisent un **entier 64 bits** sur la pile (`[ebp-0xc]`, `[ebp-0x8]`) → les
   deux moitiés écrites en `message+0x17` et `message+0x1b`.

C'est la raison mécanique du `int64` de `price` : la source est une saisie convertie en entier
64 bits, pas un champ de structure. Aucune borne n'est mesurable dans le client (§7.2).

---

## 3. Structure sur le fil

### 3.1 Trame `TM_CS_AUCTION_BID` — **19 octets**

| offset | taille | type | nom (rzu) | source rzu | source client (7.3) |
|---|---|---|---|---|---|
| 0 | 4 | `int32` | `length` | en-tête commun | `0x48caaa` (`mov DWORD PTR [eax],0x13` = 19) |
| 4 | 2 | `uint16` | `id` = `0x51a` | `TS_CS_AUCTION_BID.h:10` (`X(1306, …)`) | `0x48ca9f` (`mov ecx,0x51a`) + `0x48caa4` (`mov WORD PTR [eax+4],cx`) |
| 6 | 1 | `uint8` | `checksum` | en-tête commun | `0x48de60`-`0x48de73` (somme des octets 0..5, écrite en `+6`) |
| **7** | **4** | `int32` | **`auction_uid`** | `TS_CS_AUCTION_BID.h:6` | `0x48de4d` (`mov DWORD PTR [ebp-0xd],ecx`, `ecx = message+0x13`) |
| **11** | **8** | `int64` | **`price`** | `TS_CS_AUCTION_BID.h:7` | `0x48de53` (moitié basse, `message+0x17`) et `0x48de50` (moitié haute, `message+0x1b`) |

**Taille totale : 19 octets** (`7 + 4 + 8`). C'est la seule trame de la famille dont le prix est un
`int64` côté **requête** (socle §3.4, §3.10 : écart rzu/client = 0).

### 3.2 Comment la trame est construite et envoyée (rejoué depuis le socle §11.3)

Deux fonctions distinctes, qui expliquent les six adresses citées par le socle :

1. **initialisation commune `0x48ca70`** (appelée avec `ecx = &trame`, depuis `0x48de3c`) :
   `0x48ca8f`-`0x48ca9f` zéroissent 15 octets (`+4` `dword`, `+8` `dword`, `+0xc` `dword`, `+0x10`
   `word`, `+0x12` `byte`), `0x48ca9f`/`0x48caa4` écrivent l'**Id `0x51a` en `+4`**, `0x48caaa`
   écrit `Length = 0x13` en `+0`, puis `0x48cab6`-`0x48cabd` recalculent le **checksum** (somme des
   octets 0..5) en `+6`. C'est la routine qui porte l'identité de la trame ; le socle la cite par
   ses conséquences (`0x48de56` pour le `Length`), ce qui **tient** : `0x48de56` réécrit bien `0x13`.
2. **sender par paquet `0x48de30`** : appelle l'initialiseur (`0x48de3c`), lit
   `message+0x13`/`+0x17`/`+0x1b` (`0x48de44`-`0x48de4a`), remplit `trame+7`/`+15`/`+11`
   (`0x48de4d`, `0x48de50`, `0x48de53`), réécrit `Length` (`0x48de56`), recalcule le checksum
   (`0x48de60`-`0x48de73`) puis **émet** si la session est établie (`cmp DWORD PTR [esi+0xb8],0x0`
   à `0x48de6c`, puis appel virtuel `[eax+0xc4]` à `0x48de8a`).

**Contrôle de complétude.** Le sender `0x48de30` n'a **qu'un seul appelant** dans tout `.text` :
`0x49e397` (mesure par balayage des `E8`), et cet appel est le corps du **stub `0x49e394`**. Aucun
autre site ne construit ni n'écrit l'Id `0x51a` : l'immédiat `0x51a` n'apparaît que 3 fois dans
`.text`, dont la seule écriture de trame (`0x48ca9f`) — les deux autres (`0x66de65`, `0x721014`)
sont une comparaison et un déplacement de saut sans rapport (§5.4 pour la comparaison, qui est
justement le handler de résultat).

### 3.3 Le chemin interne → stub → trame

`0x49e394` est atteint par la **table de dispatch des messages internes** en `0x49e21d`
(`sub eax,0x403`, `cmp eax,0xde`, table d'octets `0x49ea50`, table de sauts `0x49e98c`) :

| clé | index | octet | cible | stub |
|---|---|---|---|---|
| 1152 | 125 | 33 | `0x49e36d` | `0x48dc80` (1300) |
| 1154 | 127 | 34 | `0x49e37a` | `0x48dd10` (1302) |
| 1156 | 129 | 35 | `0x49e387` | `0x48dda0` (1304) |
| **1158** | **131** | **36** | **`0x49e394`** | **`0x48de30` (1306)** |
| 1160 | 133 | 38 | `0x49e3ae` | `0x48df30` (1308) |
| 1162 | 135 | 40 | `0x49e3c8` | `0x48e050` (`TM_CS_ITEM_KEEPING_LIST` 1350) |

La chaîne complète est donc : geste (slot 6 d'une des deux fenêtres) → message interne `1158`
(`.?AUSIMSG_REQ_AUCTION_BID@@`) → stub `0x49e394` → sender `0x48de30` → 19 octets sur le fil.

---

## 4. Gating de version, tranché pour Epic 7.3

| # | point mesuré dans rzu | décision pour 7.3 |
|---|---|---|
| 4.1 | identifiant : `X(1306, version < EPIC_9_6_3)` / `X(2306, version >= EPIC_9_6_3)` (`TS_CS_AUCTION_BID.h:9-11`) | **`1306`**. `EPIC_7_3 = 0x070300` (`PacketEpics.h:59`) `< EPIC_9_6_3 = 0x090603` (`:96`) ; `2306` **ne doit pas être déclaré** : il n'existe pas en 7.3 |
| 4.2 | aucun `_(def)`/`_(impl)` conditionnel dans la trame : les deux champs sont des `_(simple)` sans gating (`:5-7`) | **aucun champ gaté** : `auction_uid` et `price` sont présents et de taille fixe en 7.3 |
| 4.3 | `price` est un `int64` **tel quel**, sans condition de version (`:7`) | `int64` sur 8 octets, moitié basse à `+11`, moitié haute à `+15` |
| 4.4 | NGemity déclare `CREATE_PACKET(TS_CS_AUCTION_BID, 1306)` sans remap (`…/TS_CS_AUCTION_BID.h:10`) | concorde avec 7.3 : sa version plus récente ne change pas l'identifiant |
| 4.5 | `TS_SC_UNSUMMON_NOTICE` prend `1306` à partir d'`EPIC_9_6_3` (`TS_SC_UNSUMMON_NOTICE.h:11`) | **sans effet en 7.3** ; à ne jamais citer comme alias de `1306` |

Aucun champ de cette trame n'a de gating non statué : la §4 du socle (identifiants + champs) est
satisfaite sans réserve pour `1306`.

---

## 5. Traitement attendu

### 5.1 NGemity ne traite rien, et ne peut rien trancher

`grep -rni "auction"` sur `reference/ngemity/Chihiro/src` ne remonte que trois familles de lignes,
toutes hors sujet :

* `Entities/Item/Item.cpp:91,127` et `Entities/Item/ItemInstance.h:36,59,81,103` : un champ
  **persisté** `auction_id` / `m_nAuctionID` sur l'instance d'objet ;
* `Map/WorldLocation.h:50` : `SecRouteAuction = 130107`, constante **orpheline** (socle §5.1).

Le côté `shared/` ne contient que les **déclarations** (`ClientPackets.h:215`,
`Packets/GameClient/TS_CS_AUCTION_BID.h:6-10`) et la structure de réponse `TS_SC_RESULT`
(`shared/Server/Packets/GameClient/TS_SC_RESULT.h`). **Il n'y a rien à porter** : ni handler, ni
service, ni règle. La validation ne peut venir que du client (mesuré ici) et du modèle du dépôt
(§5.5).

### 5.2 Le lecteur

Aucun lecteur client → serveur n'existe sur `master` pour la famille (le socle n'a livré que les
trois réponses). Le patron du dépôt est une méthode statique testable hors socket, à contrôle de
longueur, dans le fichier des paquets de la famille —
`Game/Network/Packets/Game/GameAuctionPackets.cs`, à côté des trois constructeurs livrés —, sur le
modèle de `GameActionPackets.cs:133` (`TryReadStorage`) :

- longueur attendue : `HeaderSize + 4 + 8` = **19** ; `packet.Length < 19` → refus ;
- `auction_uid` = `BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize, 4))` (offset 7) ;
- `price` = `BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(HeaderSize + 4, 8))` (offset 11) —
  **`long`, pas `int`** : c'est la seule lecture 64 bits du fichier pour une requête ;
- **aucun contrôle de checksum** : le dépôt lit l'octet d'en-tête (`Header.cs:24`) et ne le compare
  nulle part sur le chemin entrant ; un lecteur qui l'inventerait créerait une règle que rien ne
  soutient.

La question « que faire d'une trame **plus longue** que 19 » est commune aux quatre requêtes de la
famille et se tranche **une fois pour la famille** (voir §7.6), pas différemment par paquet.

### 5.3 Le bras de dispatch

`GameClient.cs` : les paquets client → serveur sont traités par une suite de `if (header.ID == …)`
au-dessus du `switch` final, dont le défaut lève `Exception("Unknown Packet Type {header.ID}")`
(`GameClient.cs:1865`). Le garde-fou des trois **réponses** de la famille est déjà en place
(`:1405-1408`, `log + continue`) et le patron d'un bras est `:1413-1417`
(`if (header.ID == (ushort)GamePackets.TM_CS_TAKEOUT_COMMERCIAL_ITEM) { HandleTakeoutCommercialItem(msgBuffer); continue; }`,
méthode `:317`).

Le bras de `1306` s'ajoute dans cette suite, avec sa méthode privée
(`HandleAuctionBid(byte[] buffer)`) qui lit la trame, journalise, puis répond (§5.4). Le membre
`TM_CS_AUCTION_BID = 1306` est ajouté à `GamePackets` **dans le même commit** que ce bras (critère
transversal 4 : aucun membre de `GamePackets` ne peut atteindre le `switch` final). La bande `13xx`
de `GamePackets.cs` ne porte aujourd'hui que les trois réponses (`:156-158`) : `1306` serait le
premier membre `TM_CS_AUCTION_*`, et le commentaire de `:74-78` (qui acte le cas `1304`/invocation)
n'a **pas** à être complété : il n'existe aucun alias tardif de `1306` à consigner, seulement le
remap `2306` d'`EPIC_9_6_3` (§4.5).

### 5.4 Ce que le client fait après avoir émis `1306` — **la réponse existe, et elle est générique**

C'est le livrable propre de cette fiche. Le socle §5.3 dit « aucun paquet dédié » : c'est vrai, et
incomplet. Mesure :

**(a) Aucun satisfier pour `1306`.** Les objets enregistrés à la construction de la fenêtre
d'enchères sont exhaustivement énumérables par leur vtable :

| objet | vtable | occurrences dans `.text` | identifiants portés |
|---|---|---|---|
| `.?AUResultSatisfier@LazyMsgProc@auction@@` | `0xa25d80` | **4** (`0x5039cd`, `0x503a01`, `0x503a35`, `0x503a69`) | `0x514` (1300), `0x516` (1302), `0x518` (1304), `0x546` (1350) |
| `.?AUTypeSatisfier@LazyMsgProc@auction@@` | `0xa25d74` | 4 (`0x503901`, `0x503932`, `0x503963`, `0x503994`) | `0x481` (1153), `0x483` (1155), `0x485` (1157), `0x48b` (1163) — identifiants **internes** |

`0x51a` (1306) **n'y figure pas**, et l'immédiat de vtable n'apparaît nulle part ailleurs : la
règle est cohérente — le client n'enregistre une attente que pour les quatre **listes qui ont une
réponse** (`1301`, `1303`, `1305`, et la liste d'objets en garde dont l'identifiant 7.3 est
`TM_CS_ITEM_KEEPING_LIST = 1350`, `TS_CS_ITEM_KEEPING_LIST.h:11`). Pour `1306`/`1308`/`1309`/`1310`
il n'existe ni `AUSIMSG_RES_AUCTION_BID` ni aucun `AUSIMSG_RES_AUCTION_*` de ces quatre actes dans
tout le binaire (`grep` des chaînes `AUSIMSG_*AUCTION*` : `REQ_*` pour les sept requêtes, `RES_*`
pour les quatre listes seulement).

**(b) Le client traite `TM_SC_RESULT` (`id = 0`) pour `request_msg_id = 1306`.** La trame générique
est reçue par l'aiguillage id → case de `0x67df59` : table d'octets `0x67f0a0`, table de sauts
`0x67f020`, **id `0` → case `0` → `0x67df89`** (`push ebx; mov ecx,esi; call 0x66db80`), et
`TM_SC_RESULT = 0` en 7.3 (`op_codes.md:2`, `rzu TS_SC_RESULT.h:13` `X(0, version < EPIC_9_6_3)`).
Le handler `0x66db80` :

- lit `word [packet+7]` = `request_msg_id`, `word [packet+9]` = `result`, `dword [packet+0xb]` =
  `value` (`0x66dbcc`, `0x66dbd4`, `0x66dbdc`) — **exactement** les offsets de
  `rzu TS_SC_RESULT.h:8-10` ;
- aiguille sur `request_msg_id` et possède **quatre cas d'enchère explicites** : `0x51a` (1306,
  `cmp eax,0x51a` `0x66de65`, `je 0x66e00b`), puis, dans la branche `> 0x51a`, `0x51c` (1308,
  `sub eax,0x51c` `0x66e04a`), `0x51d` (1309, `dec eax` `0x66e051`) et `0x51e` (1310, `dec eax`
  `0x66e054` + corps `0x66e05b`) ;
- pour `1306`, journalise l'étiquette **`경매 입찰`** (« enchère — mise ») via le format `0xa52524`
  (`경매 입찰    요청결과-%s[%d]`), les trois autres cas portant `즉구` (achat immédiat),
  `등록` (enregistrement) et `취소` (annulation) (`0xa524ac`, `0xa524d4`, `0xa524fc`) ;
- dans **tous** les cas (y compris le défaut `0x66e258`), construit un message interne de 27 octets,
  **id 4**, vtable `0xa51ee8` = `.?AUSMSG_RESULT@@` (occurrence unique `0x66dbb4`), avec
  `+0x13 = request_msg_id`, `+0x15 = result`, `+0x17 = value` (`0x66dbcc`-`0x66dbc3`), puis le met
  en file pour l'interface (`lea ecx,[ebx+0x2c]`, `0x66e283`).

**(c) `1300`, `1302`, `1304` et `1350` ne sont pas comparés dans ce handler.** Mesure : énumération
exhaustive des comparaisons de la fonction — les ids `> 0x119` passent par l'échelle
`0x51a`/`0x1c4`, puis les tables `0x11c..0x19a` et `0x1f7..0x2c7` (`sub eax,0x1f7 ; cmp eax,0xd0 ;
ja défaut`, `0x66df69`) : `0x514`, `0x516`, `0x518` et `0x546` y tombent hors bornes (indices 797,
799, 801, 847 > `0xd0`) et partent au **corps par défaut**, qui construit le même
`.?AUSMSG_RESULT@@` avec une étiquette générique. Autrement dit : **le client affiche un résultat
d'enchère quel que soit l'identifiant, et connaît nommément les quatre actes de la famille.**

**Conséquences pour le serveur.**

1. `1306` **a une réponse** : `TM_SC_RESULT` (`id = 0`, `GamePackets.cs:5`) avec
   `request_msg_id = 1306`, une valeur de `result` (`ResultCode`, `Game/Network/Packets/ResultCode.cs`)
   et `value`. L'émission est déjà outillée : `GameClient.SendResult(ushort id, ushort result, int value = 0)`
   (`GameClient.cs:68`), patron d'appel `:543`, `:629`, `:753`, `:903`. Le socle §5.3 disait « aucun
   paquet dédié » ; il faut lire « aucune trame `TM_SC_AUCTION_*`, mais **la trame générique de
   résultat est attendue**, et le client a une étiquette dédiée pour `1306` ».
2. `value` **n'est pas** contraint par la trame : le client le transporte tel quel dans
   `AUSMSG_RESULT+0x17`. Ce que le serveur doit y mettre pour une enchère n'est établi par aucune
   source (§7.1).
3. Le changement d'état visible (prix courant, « j'ai enchéri ») ne peut venir que des **listes** :
   le client ne reçoit que `1301`/`1303`/`1305` pour la famille, donc un rafraîchissement se fait par
   réémission de ces trames avec le `page_num` demandé (socle §5.4). Une enchère réussie se traduit
   donc par `TM_SC_RESULT(1306, Success)` **et** un nouvel envoi de liste si la fenêtre ouverte doit
   voir le nouveau prix.

### 5.5 Le prérequis `1309`, nommé et délimité

**Ce que le modèle prouve.** `AuctionEntity` (`Game/DataAccess/Entities/Telecaster/AuctionEntity.cs:5-36`)
porte tout ce qu'une enchère devrait écrire : `SellerId:10`, `SellerName:17`,
`HighestBiddingPrice:24`, `HighestBidderId:26`, `HighestBidderName:33`, `BiddersIds:23`,
`EndTime:20`, `InstantPurchasePrice:21`, `RegistrationTax:22`, `IsHiddenVillageOnly:19`,
`ItemStorage:35` ; la table est configurée (`TelecasterContext.cs:11` `DbSet<AuctionEntity> Auctions`,
`:146-157` `ConfigureAuctions`, index `IX_Auctions_HighestBidderId` et `IX_Auctions_SellerId`) et les
neuf motifs de mouvement de la famille existent déjà dans `StorageType`
(`ItemBySuccessfulBid = 1`, `GoldByHigherBid = 32`, `GoldByItemSoldOut = 34`, …).

**Ce que `master` ne possède pas.** Mesure `grep -rni "auction" --include=*.cs Game/` :

- **aucun service d'enchère** (`Game/Services/` : rien), **aucun repository d'annonce** : le seul
  repository de la famille est `AuctionCateryResourceRepository` (arbre de **catégories** Arcadia) ;
- `DbSet<AuctionEntity> Auctions` **n'est consommé par aucun code** ; la seule mention d'enchère dans
  un repository est un commentaire d'exclusion dans `StorageRepository.cs:116-123`
  (`row.AuctionId == null` : une ligne d'annonce ne doit pas s'afficher comme objet d'entrepôt) ;
- **rien n'écrit** `Auctions` : `1309` (`TM_CS_AUCTION_REGISTER`) n'est pas déclaré, aucune enchère
  automatique par PNJ n'existe (`AutoAuctionResource` est une table non lue).

**Conséquence, dite sans maquillage.** Sans annonce persistée, il n'existe **aucun `auction_uid` à
recevoir** et aucune enchère à exécuter : l'exécution de `1306` est **hors lot** (elle suppose
`1309`, donc les décisions de §8). Ce qui reste **livrable et testable** :

1. la **déclaration** `TM_CS_AUCTION_BID = 1306` + le **bras de dispatch** (indissociables, critère 4) ;
2. le **lecteur borné** (§5.2), testable hors socket avec des tampons fabriqués ;
3. la **journalisation** du refus ou de l'acceptation, et l'**émission d'un `TM_SC_RESULT(1306, …)`**
   — c'est la seule réponse que le client sait lire (§5.4) ;
4. les **tests d'offsets** du nouveau paquet (critère transversal 3) : taille 19, `Length`/`Id` en
   0/4, `auction_uid` en 7, `price` en 11 sur 8 octets, refus d'une trame courte ;
5. **l'exécution de l'enchère** : hors lot, la fiche ne fixe ni la validation, ni le montant minimal,
   ni le refus (décisions de Killian, §8).

### 5.6 Les décisions restantes portent sur l'exécution, pas sur la trame

**Prouvable (et suffisant pour livrer) :** la trame (§3), son gating (§4), l'identifiant interne, les
deux fenêtres émettrices, l'unique émetteur de trame, l'absence de satisfier, la présence du cas
`0x51a` dans le handler de `TM_SC_RESULT` (§5.4), l'absence de tout exécutant côté serveur (§5.5).

**Décision de Killian (aucune source ne les tranche) :**

| # | décision | pourquoi elle n'est pas devinable ici |
|---|---|---|
| 1 | « pas de surenchère sur soi-même » : refuser une mise sur sa propre annonce ? | aucune trace dans le client (il envoie `auction_uid` + `price`, sans contrôle local mesurable) ni dans NGemity |
| 2 | montant minimal d'une enchère : `> HighestBiddingPrice` strict, ou palier (`+x %`, `+1 000`) ? | le client n'applique aucun incrément visible ; le modèle n'a qu'un `HighestBiddingPrice` |
| 3 | prix de réserve : existe-t-il un `start_price` non visible sous lequel la vente ne se conclut pas ? | `AuctionEntity` **n'a pas** de colonne de prix de départ ; le début d'enchère ne serait donc représenté nulle part |
| 4 | `result`/`value` de la réponse `TM_SC_RESULT(1306, …)` : quels codes de refus, et que met-on dans `value` ? | le client affiche le code via sa table de noms (`0x425980`) mais aucune source ne dit quelles valeurs sont produites pour une enchère |
| 5 | devenir de l'enchérisseur dépassé : `BiddersIds` (sans montant) suffit-il, ou faut-il une table de mises ? | le modèle ne stocke aucun montant par enchérisseur (`BiddersIds` = identifiants) : une contre-offre est mal représentable |
| 6 | remboursement de la mise dépassée (or restitué, `StorageType.GoldByHigherBid = 32`) : à quelle condition ? | aucune écriture existante de ce motif n'existe |
| 7 | durées, taxe, visibilité, expiration, plafond d'annonces | décisions du socle §8/§9 qui conditionnent l'annonce à enchéprir, pas la trame |

Toutes ces lignes vont en `## A VERIFIER PAR KILLIAN` (§8) ; aucune valeur n'est choisie dans cette
fiche.

### 5.7 Ce que le lot doit livrer, et le cas « pas de code »

**Il y a du code à livrer** : la trame est entièrement fixée, le lecteur est borné, le bras de
dispatch est obligatoire (critère 4) et la réponse générique est outillée. Le cas « pas de code »
n'est donc **pas** applicable ici (le précédent `4008` livrait une trame sans réponse mesurable côté
client ; ce n'est pas notre cas, la réponse est mesurée au §5.4).

1. `GamePackets.cs` : `TM_CS_AUCTION_BID = 1306` (bande `13xx`, à côté de `:156-158`), sans `2306`.
2. `GameAuctionPackets.cs` : le lecteur de §5.2 (`auction_uid` à 7, `price` `long` à 11, refus si
   `< 19`).
3. `GameClient.cs` : `HandleAuctionBid`, et le bras `if (header.ID == …) { …; continue; }` (§5.3).
4. La réponse : `SendResult((ushort)GamePackets.TM_CS_AUCTION_BID, (ushort)ResultCode.InvalidArgument)`
   pour un refus, `TM_SC_RESULT(1306, …)` pour l'accusé ; **aucune** trame `TM_SC_AUCTION_*`
   supplémentaire, aucun `1307` inventé.
5. Les tests d'offsets du nouveau paquet (critère transversal 3), sur le patron de
   `Tests/Game/AuctionPacketsTests.cs` (socle) — le fichier de tests de `1306` est le sien.
6. Le bloc destiné à `CLAUDE.md` dans la description de la MR (§10), **sans écrire `CLAUDE.md`**.

**Non livré, et c'est délibéré** : aucune validation d'enchère (montant minimal, surenchère sur
soi-même, prix de réserve), aucune écriture d'annonce, aucun débit ni remboursement d'or, aucune
requête d'annonces.

---

## 6. Écarts assumés avec NGemity

1. **NGemity n'implémente pas le paquet** (§5.1) : aucun écart n'est un choix, c'est une absence.
   La validation ne peut venir que du client et du modèle du dépôt.
2. **NGemity compile `EPIC_4_1_1`** (`shared/Common/Define.h:25`) et ses tailles ne sont pas celles
   de 7.3 (socle §7.4). Ici la conséquence est nulle : sa trame est fixe à `1306` avec les **mêmes**
   deux champs dans le **même** ordre (`int32_t auction_uid`, `int64_t price`,
   `…/TS_CS_AUCTION_BID.h:7-8`). Sa seule utilité est de confirmer cet ordre — il le confirme.
3. **La réponse n'est pas dans NGemity non plus** : `shared/Server/Packets/GameClient/TS_SC_RESULT.h`
   déclare la trame générique (comme rzu, `request_msg_id` + `result` + `value`), sans aucun cas
   d'enchère. Que `1306` soit un `request_msg_id` valide pour cette trame se prouve **dans le
   client** (§5.4) et non dans les références serveur : c'est un écart de méthode assumé, et c'est
   ici le client 7.3 qui tranche.
4. **Aucune divergence rzu/NGemity à arbitrer** sur les champs : les deux références sont
   identiques au type près (`int32_t`/`int64_t`), et le client les confirme. C'est le seul paquet de
   la famille sans désaccord de nom de motif d'objet (`item_info` est absent de cette trame).

---

## 7. NON ÉTABLI

1. **`value` de `TM_SC_RESULT` pour `1306`.** Le client transporte `value` tel quel jusqu'à
   `.?AUSMSG_RESULT@@+0x17` (`0x66dbdc`), mais rien n'indique ce que le serveur doit y mettre pour
   une enchère (montant ? prix courant ? `0` ?). *Question : quel contenu pour `value` sur une
   enchère acceptée ou refusée ?*
2. **Échelles et bornes du montant dans le client.** Le montant vient d'un contrôle de saisie
   converti par `0x501f70`/`0x501d40` ; aucune borne supérieure/inférieure n'a été mesurée dans
   cette fonction ni dans la fenêtre. *Question : le client limite-t-il la saisie (maximum, pas
   d'incrément), et à quelle valeur ?*
3. **Consommation effective de `.?AUSMSG_RESULT@@` (id interne 4) par l'interface.** La fiche mesure
   la **construction** et la **mise en file** du message (`0x66e283`, `call 0x64d0e0`), pas son
   affichage : la vtable `0xa51ee8` n'est écrite qu'une fois (`0x66dbb4`) et aucune comparaison de
   cette vtable n'apparaît dans `.text`, ce qui laisse deux lectures possibles (dispatch par
   identifiant interne, ou message non consommé en 7.3). *Question : un `TM_SC_RESULT` produit-il
   une fenêtre/ligne visible, ou seulement une ligne de journal en build de développement ?*
4. **Sémantique exacte du `result` par le client.** Le handler journalise le **nom** du code
   (`call 0x425980`, table de noms), mais la table n'a pas été décodée : aucune correspondance
   code → texte pour une enchère n'est établie. *Question : quels codes le client sait-il nommer, et
   en utilise-t-il un pour décider d'un affichage particulier ?*
5. **Le geste exact de chaque site émetteur.** Les deux sites sont atteints par le **même slot 6**
   de deux fenêtres (`SUIAuctionSearchWnd`, `SUIAuctionTenderWnd`) ; le libellé du contrôle qui les
   déclenche (bouton « enchérir » de la fenêtre de recherche, bouton de la fenêtre de dépôt) n'a pas
   été relevé dans les ressources d'interface (.nui). *Question : les deux fenêtres proposent-elles
   le même geste, avec le même libellé ?*
6. **Trame plus longue que 19 octets.** Aucune source ne dit si le serveur doit refuser une trame
   `> 19` ou lire les 19 premiers octets (question commune aux quatre requêtes de la famille).
   *Question : politique unique de lecture pour la famille ?*
7. **`1350` en 7.3.** Le client enregistre un `ResultSatisfier` pour `0x546` (1350) et rzu déclare
   `TS_CS_ITEM_KEEPING_LIST` `X(1350, version < EPIC_9_6_3)` : la cohérence 7.3 est établie, mais
   **la fiche de `1350` n'existe pas** et l'objet en garde n'est traité nulle part côté serveur.
   *Question : `1350` appartient-il au même lot que `1306` ou à une carte distincte ?*
8. **`1307`** : toujours le trou documenté du socle §8.7 (absent de `op_codes.md`, de rzu et de
   NGemity). La mesure de cette fiche ne le rouvre pas : aucun `0x51b` n'apparaît dans le client.
   *À confirmer comme trou définitif.*
9. **Writers de l'état d'une enchère.** Aucun code de `master` n'écrit `AuctionEntity` ; les
   décisions §5.6 (montant minimal, surenchère sur soi-même, prix de réserve, remboursement)
   resteront non testables jusqu'à `1309`. *Question : ces règles s'écrivent-elles avec `1309`, ou
   dans une carte distincte après lui ?*

---

## 8. A VERIFIER PAR KILLIAN

| # | question | ce qui est bloqué sans réponse |
|---|---|---|
| 1 | `result`/`value` de `TM_SC_RESULT(1306, …)` : quels codes de refus, quel contenu de `value` ? (§7.1, §7.4) | contenu de la seule réponse que le client lit pour `1306` |
| 2 | Refus d'une mise sur **sa propre** annonce ? (§5.6 q1) | validation de l'enchère |
| 3 | Montant minimal : strictement supérieur au prix courant, ou palier d'incrément ? (§5.6 q2) | validation de l'enchère |
| 4 | Prix de réserve : existe-t-il un prix de départ caché, non représenté dans `AuctionEntity` ? (§5.6 q3) | conclusion de la vente |
| 5 | Mise dépassée : simple `BiddersIds`, ou faut-il une table de mises avec montants ? (§5.6 q5) | remboursement et historique |
| 6 | Remboursement de la mise dépassée (`StorageType.GoldByHigherBid = 32`) : à quelle condition ? (§5.6 q6) | or du personnage dépassé |
| 7 | Politique de lecture d'une trame `> 19 octets`, commune à la famille (§7.6) | lecteur des quatre requêtes |
| 8 | `1350` (objet en garde) : même lot ou carte distincte ? (§7.7) | périmètre du prochain lot |
| 9 | La famille doit-elle porter les règles d'enchère (montant minimal, réserve) dans une carte dédiée après `1309`, ou dans `1309` lui-même ? (§7.9) | découpage des lots suivants |

---

## 9. Commits et binaires épinglés

| référence | commit / empreinte | vérification |
|---|---|---|
| `rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02, « packets: fix TS_SC_INVENTORY with older epics ») | `git -C reference/rzu log -1` → identique |
| `ngemity/Chihiro` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03, « Fix compilation issue for GCC ») | `git -C reference/ngemity log -1` → identique |
| `master` du dépôt | `b56967a07430422add88e0e5cdf292b41b18f6c6` | `git rev-parse origin/master` → identique |
| client 7.3 | `SFrame.exe`, `sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 octets) | `sha256sum` rejoué → identique |

Les trames rzu ont été relues aux commits ci-dessus. `reference/client73/` n'est pas un dépôt Git :
seule l'empreinte du binaire est opposable, il n'y a **aucun sha de client à citer**.

**Méthode rejouée** (socle §11, aucune méthode nouvelle ; aucun `SFrame.exe`, aucun Lua, aucun
script client n'a été exécuté — lecture statique seule) :

1. `objdump -d -M intel` sur les plages `0x48ca20`-`0x48e040` (initialiseurs et senders des sept
   requêtes) : trame de `1306` = init `0x48ca70` + sender `0x48de30` ;
2. balayage des appels `E8`/`E9` et des immédiats 32 bits dans `.text` : appelant unique du sender
   (`0x49e397`), immédiats `0x51a`, `0x486`, vtables `0xa2596c`, `0xa25d74`, `0xa25d80`, `0xa51ee8` ;
3. tables de dispatch (octets + sauts) lues aux adresses des `movzx`/`jmp [table]` :
   `0x49ea50`/`0x49e98c` (messages internes), `0x67f0a0`/`0x67f020` (paquets id 0..250),
   `0x66e2d8`/`0x66e290`, `0x66e484`/`0x66e470` (échelle du handler de résultat) ;
4. chaînes RTTI MSVC résolues par `vtable-4 → COL → type_info → nom mangé` (`0xa2596c` →
   `.?AUSIMSG_REQ_AUCTION_BID@@`, `0xa259cc` → `.?AVSUIAuctionSearchWnd@@`, `0xa25b0c` →
   `.?AVSUIAuctionTenderWnd@@`, `0xa25d80` → `.?AUResultSatisfier@LazyMsgProc@auction@@`,
   `0xa51ee8` → `.?AUSMSG_RESULT@@`) ;
5. chaînes `AUSIMSG_*AUCTION*` listées par recherche d'octets dans tout le binaire (13 chaînes :
   9 `REQ_*`, 4 `RES_*`).

**Non rejoué / non vérifiable ici** : l'affichage réel du résultat par l'interface (§7.3), les
libellés des boutons des ressources `.nui` (§7.5), et toute exécution du client — impossible par
consigne et par construction.

---

## 10. Bloc destiné à `CLAUDE.md`

À recopier dans la description de la MR (le dev n'écrit pas `CLAUDE.md`, Hermes le protège).

```markdown
### Enchères — l'acte d'enchérir `TM_CS_AUCTION_BID` (1306)

- `1306` est un identifiant **daté** : rzu le déclare `version < EPIC_9_6_3` et le remappe en `2306`
  au-delà. **En 7.3 c'est `1306`**, et `2306` ne doit jamais être déclaré.
- Trame client → serveur, **19 octets** : `Length` (0), `id` = `0x51a` (4), `checksum` (6),
  `auction_uid` `int32` (7), `price` `int64` (11). C'est la **seule** requête de la famille dont le
  prix est un `int64` : le lire en `long`, jamais en `int`.
- Le client l'émet depuis **deux fenêtres** (`SUIAuctionSearchWnd` et `SUIAuctionTenderWnd`, slot 6
  de chacune) via le message interne `.?AUSIMSG_REQ_AUCTION_BID@@` (1158), avec un **unique** sender
  de trame (`SFrame.exe 0x48de30`).
- **`1306` a une réponse** : `TM_SC_RESULT` (id `0`) avec `request_msg_id = 1306`. Le handler client
  de `TM_SC_RESULT` (`0x66db80`) a un cas explicite pour `1306` (et pour `1308`/`1309`/`1310`), et
  construit son message interne `.?AUSMSG_RESULT@@`. Il n'existe **aucune** trame `TS_SC_AUCTION_*`
  de réponse, et **aucun** `ResultSatisfier` pour `1306` (les quatre satisfiers de la fenêtre
  portent `1300`, `1302`, `1304`, `1350`).
- Le rafraîchissement d'une liste se fait par **réémission de `1301`/`1303`/`1305`** avec le
  `page_num` demandé : c'est le seul chemin mesuré pour montrer le nouvel état.
```

---

## 11. Références croisées

| document | ce qu'il fixe pour `1306` |
|---|---|
| `docs/packet-specs/socle-encheres.md` §1.1, §2, **§3.4**, §3.10, §4.2, §5.2, §5.3, §5.4, §5.5, §6.3, §8, §9, §11 | identité, geste, trame de 19 octets, gating des identifiants, contrainte des 40 emplacements, absence de trame dédiée, modèle du dépôt, prérequis `1309`, décisions ouvertes, méthode de lecture |
| `docs/packet-specs/1300-auction-search.md` (branche `hermes/packet-1300-auction-search`, MR #65) | la lecture de la **recherche** et de sa réponse `1301` |
| `docs/packet-specs/1302-auction-selling-list.md` (MR #66) | la requête `1302` et la réponse `1303` |
| `docs/packet-specs/1304-auction-bidded-list.md` (MR #67) | la requête `1304` et la réponse `1305` ; ses §7.9/§8 q1 restent ouvertes, cette fiche apporte l'élément manquant sur le **handler de résultat** (§5.4) |
| `docs/packet-specs/socle-booths.md` | voisinage des loges (`705`), correction de motif d'objet portée au socle §7.5 |

Cette fiche ne reprend ni `1300`, ni `1302`, ni `1304`, ni les trois réponses : elle ne traite que
l'acte d'enchérir. `1308`, `1309` et `1310` sont cités pour ce que le client révèle d'eux (§5.4) et
parce qu'ils partagent le prérequis `1309` — ils restent hors lot.

---

## 12. Ce que cette fiche resserre ou corrige dans le socle

1. **§5.3, ligne `1306` — « aucun paquet dédié » : à préciser.** Le socle a raison sur l'absence de
   trame `TS_SC_AUCTION_*`, mais il manque que le client **attend et traite** la trame générique
   `TM_SC_RESULT` avec `request_msg_id = 1306` (§5.4), avec une étiquette dédiée (`경매 입찰`). La
   ligne du socle doit se lire : « aucun paquet dédié ; réponse par `TM_SC_RESULT` (id 0) ».
2. **§2, fenêtre émettrice.** Le socle n'attribue l'acte qu'à `SUIAuctionTenderWnd` ; la mesure
   montre **deux** fenêtres émettrices (`SUIAuctionSearchWnd` et `SUIAuctionTenderWnd`, toutes deux
   par le slot 6 de leur vtable), pour un **unique** sender de trame.
3. **§3.4, adresses.** Les quatre adresses citées (`0x48de56`, `0x48de4d`, `0x48de50`, `0x48de53`)
   sont exactes et rejouées ici. Précision : l'Id `0x51a` et le `Length = 19` sont écrits par une
   **routine d'initialisation commune** `0x48ca70` (appelée depuis `0x48de3c`), aux sites `0x48ca9f`,
   `0x48caa4` et `0x48caaa` ; `0x48de56` ne fait que réécrire la longueur.
4. **§5.5, modèle.** Le socle annonce `AuctionEntity` ; la mesure ajoute qu'**aucun service, aucun
   repository et aucun écrivain** de cette table n'existe sur `master` (`DbSet<AuctionEntity> Auctions`
   n'est consommé nulle part) : le prérequis `1309` bloque l'**exécution**, pas la réception.
5. **§8.7 (`1307`).** Confirmé comme trou : aucun `0x51b` dans le client, aucun id dans
   `op_codes.md`, rzu et NGemity. La présente fiche ne le rouvre pas.

---

## 13. Implémentation livrée

*(Section à remplir par `navis-dev`, sur cette branche, comme dans les fiches sœurs : ce qui est
livré, les décisions prises, les réserves, et les vérifications exécutées — build, tests, compte de
tests.)*
