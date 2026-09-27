# `TM_CS_AUCTION_SEARCH` (1300) — fiche de paquet, Epic 7.3

Fiche du premier paquet client → serveur de la famille des enchères, sur le socle mergé
`docs/packet-specs/socle-encheres.md`. Le socle fixe la famille, le motif d'objet de 75 octets et les
trois réponses ; cette fiche **vérifie** la trame de 1300 dans les références locales, tranche son
gating pour 7.3, et spécifie le bras de dispatch, le lecteur et la réponse à écrire.

Rien ici n'est recopié de mémoire : chaque mesure a été refaite le 2026-09-27 sur les binaires et les
dépôts listés en §9, et chaque ligne du tableau de §3 porte sa source.

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id décimal | `1300` | `op_codes.md:202` (`[1300] = "TM_CS_AUCTION_SEARCH"`) |
| Nom | `TM_CS_AUCTION_SEARCH` | `op_codes.md:202` ; rzu `librzu/src/packets/GameClient/TS_CS_AUCTION_SEARCH.h:18` ; NGemity `shared/Server/Packets/GameClient/TS_CS_AUCTION_SEARCH.h:13` |
| Sens | client → serveur (`SessionPacketOrigin::Client`) | rzu `TS_CS_AUCTION_SEARCH.h:18` |
| Branche du lot | `hermes/packet-1300-auction-search` | carte `t_ea27f462` ; fiche présente |
| Suivi | `navislamia:packet:1300` | carte Trello `XWY9Zbr8` |

Commande émise par le client, réponse attendue : `TM_SC_AUCTION_SEARCH` = `1301`
(`op_codes.md:203`, `GamePackets.cs:156`).

---

## 2. Ce que le joueur fait

Ouvre l'hôtel des ventes et **lance une recherche** : choisit une catégorie (et éventuellement une
sous-catégorie), saisit un mot-clé, coche ou non « équipable seulement », puis demande une page. Le
client construit alors une trame de 51 octets et l'envoie ; c'est la seule origine de 1300.

Ce qui est mesuré, sur `SFrame.exe` :

- la fenêtre concernée est `SUIAuctionSearchWnd` et le message interne qu'elle poste porte le nom RTTI
  MSVC `.?AUSIMSG_REQ_AUCTION_SEARCH@@`, présent dans le binaire à l'adresse de fichier `0x812DF8`,
  soit la VA `0xC14BF8` (vérifié par `strings -t x`, identique à la citation du socle §2) ;
- ce message est consommé par le répartiteur interne `0x49E21D` (`sub $0x403,%eax`, `cmp $0xde,%eax`,
  table d'octets `0x49EA50`, table de sauts `0x49E98C`) ; la case de ce répartiteur qui envoie 1300 est
  l'entrée **125**, soit l'id de message **1152** (`0x480`) ;
- la case (stub `0x49E36D`, `call` à `0x49E370`) appelle le **sender** `0x48DC80`, qui remplit la trame
  et la remet à la session.

Les voisins de cette case confirment la lecture : 1154 (`0x482`) → `0x48DD10`, qui construit
`Length = 0x0B` (11) et `Id = 0x516` (**1302**) ; 1156 (`0x484`) → `0x48DDA0`, 11 octets, `Id = 0x518`
(**1304**). Les ids internes 1152/1154/1156 forment donc la paire demande/état de la famille, et
chacun a exactement un sender.

---

## 3. Structure sur le fil

### 3.1 Trame `TM_CS_AUCTION_SEARCH` — **51 octets**

La trame est construite par le constructeur `SFrame.exe:0x48CA20`
(`movl $0x7,(%esi)` pour l'en-tête commun, `push $0x33` + `memset`, `mov $0x514,%eax` /
`mov %ax,0x4(%esi)`, `movl $0x33,(%esi)`), puis remplie par le sender `0x48DC80` dont la trame vit
en `ebp-0x34`. Les cinq champs sont écrits par le client lui-même ; les offsets ci-dessous sont ceux
de ces écritures.

| Offset | Type | Nom | Valeur observée / origine côté client | Source |
|---:|---|---|---|---|
| 0 | `uint32` | `Length` | `51` (`0x33`) — écrit par le constructeur, réécrit par le sender avant l'envoi | `SFrame.exe:0x48CA56` (constructeur) ; `0x48DCC7` (`movl $0x33,-0x34(%ebp)`) |
| 4 | `uint16` | `Id` | `1300` (`0x514`) — `mov $0x514,%eax` puis `mov %ax,0x4(%esi)` | `SFrame.exe:0x48CA48`, `0x48CA52` |
| 6 | `uint8` | checksum | somme des octets 0…5, recalculée juste avant l'envoi | `SFrame.exe:0x48DCD1-0x48DCE4` (`add (%eax),%cl` sur `[frame, frame+6[`, `mov %cl,-0x2e(%ebp)`) |
| 7 | `int32` LE | `category_id` | copié depuis l'objet message, `[msg+0x13]` (racine choisie dans l'arbre, §5.5) | `SFrame.exe:0x48DC99`, `0x48DCA2` (`mov %eax,-0x2d(%ebp)`) |
| 11 | `int32` LE | `sub_category_id` | copié depuis `[msg+0x17]` | `SFrame.exe:0x48DC9C`, `0x48DCA8` (`mov %ecx,-0x29(%ebp)`) |
| 15 | `char[31]` | `keyword` | `strncpy(frame+15, [msg+0x1b], 31)` ; la source est le `std::string` MSVC du message (test `cmpl $0x10,0x2f(%esi)` = petite chaîne en ligne) | `SFrame.exe:0x48DC95`, `0x48DCA5`, `0x48DCB2-0x48DCB9` (`push $0x1f`, `call 0x9798D0` = `strncpy`) |
| 46 | `int32` LE | `page_num` | copié depuis `[msg+0x37]` | `SFrame.exe:0x48DC9F`, `0x48DCAB` (`mov %edx,-0x6(%ebp)`) |
| 50 | `bool` (1 octet) | `is_equipable` | copié depuis `[msg+0x3b]` (octet de la case « équipable seulement ») ; dernier octet de la trame | `SFrame.exe:0x48DCBE`, `0x48DCC4` (`mov %cl,-0x2(%ebp)`) |

**Taille totale attendue : 51 octets** (`0x33`, littéral du constructeur et du sender ; la somme des
champs — 7 d'en-tête + 4 + 4 + 31 + 4 + 1 — donne le même total). Le champ `keyword` occupe 15…45
inclus, `page_num` 46…49, `is_equipable` l'octet 50 : aucun recouvrement, aucun remplissage.

Remise au réseau : le sender termine par un appel indirect
`cmpl $0,0xb8(%edi)` / `ecx = [edi+0xb8]` / `eax = [ecx]` / `eax = [eax+0xc4]` / `push frame` /
`call *eax` (`SFrame.exe:0x48DCDD-0x48DCFB`).

### 3.2 Ce que rzu dit de la même trame

| Élément rzu | Valeur | Source |
|---|---|---|
| `category_id` | `int32_t` simple | `reference/rzu/librzu/src/packets/GameClient/TS_CS_AUCTION_SEARCH.h:8` |
| `sub_category_id` | `int32_t` simple | ibid. `:9` |
| `keyword` | `(string)` de taille 31 | ibid. `:10` |
| `page_num` | `int32_t` simple | ibid. `:11` |
| `is_equipable` | `bool`, `version >= EPIC_7_2` | ibid. `:12` |

Un `(string)` rzu est une **zone fixe NUL-terminée** de la taille déclarée, pas une chaîne préfixée
par sa longueur : l'écriture copie au plus `31 - 1` octets et remplit le reste de zéros
(`librzu/src/lib/Packet/MessageBuffer.cpp:87-95`), la lecture s'arrête au premier NUL dans les
30 premiers octets (`MessageBuffer.cpp:104-109`), et la taille comptée est bien 31
(`PacketDeclaration.h:263`). Total rzu : 7 + 4 + 4 + 31 + 4 + 1 = **51**, identique au client.

Conséquence pratique pour le lecteur : la copie cliente est bornée à 31 octets sans garantie de NUL
(aucune preuve que le client ajoute un zéro si le mot-clé saisi fait 31 caractères ou plus) ; le
lecteur serveur doit donc traiter le champ comme une zone de 31 octets et s'arrêter au premier NUL
**sans exiger** que le NUL soit présent.

### 3.3 Le client 7.3 ne reçoit jamais 1300

Le répartiteur des trames **serveur → client** est le `switch` du client sur l'id de trame
(`SFrame.exe:0x67E67A`) : `sub $0x4B1,%eax` (= 1201), `cmp $0x96,%eax` (= 150, donc la plage
1201…1351), puis `movzbl 0x67F4C8(%eax),%edx` et `jmp *0x67F4B0(,%edx,4)`
(`0x67E67A-0x67E698`). La table d'octets de 151 entrées `0x67F4C8` et la table de sauts `0x67F4B0`
donnent, relues octet par octet :

| Indice | Id | Octet | Stub | Gestionnaire |
|---:|---:|---:|---|---|
| 0 | 1201 | `0x00` | `0x67E698` | `0x671280` |
| 99 | **1300** | `0x05` | `0x67EF21` | **chemin « non traité »** |
| 100 | 1301 | `0x01` | `0x67E6A5` | `0x670660` |
| 102 | 1303 | `0x02` | `0x67E6B2` | `0x6706D0` |
| 104 | 1305 | `0x03` | `0x67E6BF` | `0x670730` |
| tout autre | — | `0x05` | `0x67EF21` | chemin « non traité » |

`0x05` est le dernier créneau de la table de sauts (`0x67EF21`), utilisé par les 147 indices sans
gestionnaire. **1300 est dans la plage mais n'a pas de case : sa trame est rejetée par le client comme
non traitée.** Seuls 1201, 1301, 1303 et 1305 ont un gestionnaire. Le sous-arbre 0x1201-0x1351
appartient d'ailleurs à un `switch` plus large sur le même registre (les branches 0x386…, 0x44D,
0x7D4, 0x1195 sont traitées avant), donc la recherche exhaustive a porté non seulement sur la sous-table
mais sur la chaîne de comparaisons qui y mène.

Le gestionnaire de 1301 (`0x670660`) confirme au passage l'obligation des quarante emplacements, et
pourquoi cette fiche ne les rouvre pas : il recopie **5120 octets en bloc** sans jamais lire le compte
— `mov 0x7(%eax),%ecx` → `obj+0x13`, `mov 0xb(%eax),%edx` → `obj+0x17`, `mov 0xf(%eax),%ecx` →
`obj+0x1b`, puis `push $0x1400`, `push frame+0x13`, `lea obj+0x1f`, `call 0x9767C0`
(`SFrame.exe:0x670660-0x6706C1`) : 0x1400 = 5120 = 40 × 128. Il ne vérifie pas `Length`.

### 3.4 Une seconde comparaison à `0x514` existe — et ne change rien

Une deuxième occurrence de `0x514` comme valeur de comparaison existe dans `.text`
(`SFrame.exe:0x63D3DF`, `cmp $0x514,%eax`), à l'intérieur du répartiteur de messages de
`SGameInterface` (les libellés d'assertion voisins, `0xA4B310`, sont de la forme
`SGameInterface - MSG_*`). Elle vaut d'être notée, mais **elle ne lit ni n'écrit aucun champ d'une
trame 1300** :

- la valeur testée est un `uint16` de l'objet message (`0x63CA10` : `movzwl 0x13(%esi),%eax`,
  `cmp $0x118,%eax`, `jg 0x63D3DF`), pas une trame reçue ;
- la case `0x514` (`0x63D77B`) compare le mot `[esi+0x15]` à `0x1c`, affiche éventuellement le texte
  `0x3D3` via `0x554480`, transmet l'objet à `0x62F470` avec `0x39`, puis journalise
  `SGameInterface - MSG_RESULT` — la même case sert aussi `0x546` (1350) ;
- aucune lecture/écriture à `[frame+7]`, `[frame+15]`, `[frame+50]` ni aucune copie de 5120 octets
  n'y apparaît.

C'est un répartiteur de messages de l'interface, pas un parseur de trame. Ce que cette table
représente exactement (ids de message ou opcodes confondus) n'est pas établi ici : voir `## NON
ÉTABLI`, point 6.

### 3.5 Contrôle de complétude

Recherches exhaustives sur `SFrame.exe` (méthode en §9), pour ne pas conclure d'un seul point
d'entrée :

| Recherche | Résultat |
|---|---|
| `call`/`jmp` rel32 vers le sender `0x48DC80` | **1 site** : `0x49E370` (case 1152 du répartiteur interne) |
| `call`/`jmp` rel32 vers le constructeur `0x48CA20` | **1 site** : `0x48DC8D` (le sender) |
| `mov $0x514,%eax` (`B8 14 05 00 00`) | 3 sites : `0x48CA48` (constructeur 1300) et deux faux positifs de milieu d'instruction — `0x68A7B9` et `0x68A825` tombent dans `80 B8 14 05 00 00`, soit `cmpb $0x0,0x514(%eax)` (un décalage de structure) aux adresses `0x68A7B8` et `0x68A824` |
| `push $0x514` (`68 14 05 00 00`) | 2 sites : `0x634240`, `0x636FAA` — allocations de 0x514 octets (`call 0x97671B`), hors sujet |
| `sub`/`cmp` immédiat 0x514 dans `.text` | 1 seul site : `3D 14 05 00 00` à `0x63D3DF` (§3.4) ; `2D 14 05 00 00` : aucun |
| `sub $0x4B1,%eax` (base du dispatch 1201) | 1 seul site : `0x67E67A` — la sous-table de §3.3 est la seule indexée sur cette plage |

---

## 4. Gating de version, tranché pour 7.3

| Champ | Gating dans rzu | Décision pour Epic 7.3 | Source |
|---|---|---|---|
| `category_id` | aucun | présent | `TS_CS_AUCTION_SEARCH.h:8` |
| `sub_category_id` | aucun | présent | `:9` |
| `keyword` | aucun | présent, 31 octets | `:10` |
| `page_num` | aucun | présent | `:11` |
| `is_equipable` | `version >= EPIC_7_2` | **présent** : 7.3 > 7.2 | `:12` |

Le gating est tranché par deux mesures indépendantes et concordantes : le client 7.3 **écrit** l'octet
50 (`SFrame.exe:0x48DCBE`, `0x48DCC4`) et le constructeur dimensionne la trame à 51 octets
(`0x48CA56`). Un lecteur qui ignorerait `is_equipable` laisserait 4 octets non consommés et
désalignerait la fin de trame.

**Le trou 1307 n'est pas à combler** : `op_codes.md` saute de 1306 à 1308
(`op_codes.md:208-209`) et rzu n'a que **sept** en-têtes client → serveur dans
`librzu/src/packets/GameClient/` pour cette famille (`TS_CS_AUCTION_` : `SEARCH`, `SELLING_LIST`,
`BIDDED_LIST`, `BID`, `INSTANT_PURCHASE`, `REGISTER`, `CANCEL` — aucun `1307`). Sur le client, les
constructeurs mesurés portent `Id` `0x514` (1300), `0x516` (1302), `0x518` (1304), `0x51E` (1310) :
aucune trame 1307 n'existe. 1307 ne se déclare pas.

Identifiants rzu, pour mémoire de version : `TS_CS_AUCTION_SEARCH_ID` déclare `1300` sous
`version < EPIC_9_6_3` et `2300` ensuite (`TS_CS_AUCTION_SEARCH.h:15-16`) ; l'énumération de
`op_codes.md` est celle de 7.3. NGemity compile la même structure avec `#define EPIC EPIC_4_1_1`
(`reference/ngemity/shared/Common/Define.h:25`), donc avec `is_equipable` également présent.

---

## 5. Traitement attendu

### 5.1 Le lecteur

Un `TryRead…` du dépôt (modèle : `GameActionPackets.TryReadPutonItem`, appelé depuis
`GameClient.cs:783`), par exemple `GameAuctionPackets.TryReadAuctionSearch(byte[] packet, out
AuctionSearchRequest request)` :

1. refuser si `packet.Length < 51` ;
2. `category_id` = `Int32` LE à 7, `sub_category_id` = `Int32` LE à 11 (`BinaryPrimitives`) ;
3. `keyword` = les 31 octets de 15 à 45, **arrêt au premier NUL**, décodage ASCII (§3.2) ;
4. `page_num` = `Int32` LE à 46 ;
5. `is_equipable` = `packet[50] != 0` ;
6. ne lire **que** ces offsets : rien dans la trame ne porte d'identité de joueur, de code d'objet ou
   de montant, et le reste des 51 octets est du remplissage à zéro.

Refus : `SendResult((ushort)GamePackets.TM_CS_AUCTION_SEARCH, (ushort)ResultCode.InvalidArgument)`,
jamais une trame `TM_SC_AUCTION_*` d'erreur — cette trame n'existe ni dans les références ni dans le
client (socle §5.3, `GameClient.SendResult`, `GameClient.cs:68-72`).

### 5.2 Le bras de dispatch

`TM_CS_AUCTION_SEARCH = 1300` doit être déclaré dans `GamePackets` **et** avoir son bras, ensemble
(critère transversal 4). La bande 13xx ne porte aujourd'hui que les trois réponses
(`GamePackets.cs:156-158`) ; aucun `TM_CS_AUCTION_*` n'existe. Emplacement du bras : avec les autres
`if (header.ID == …)` **au-dessus** du `switch` final (`GameClient.cs:1854-1866`), après
`TM_CS_CHECK_ILLEGAL_USER` (l. 1839) et `TM_CS_XTRAP_CHECK` (l. 1848), sous la même forme :

```csharp
if (header.ID == (ushort)GamePackets.TM_CS_AUCTION_SEARCH)
{
    HandleAuctionSearch(msgBuffer);
    continue;
}
```

Un membre de `GamePackets` qui atteindrait le `switch` final tomberait dans
`throw new Exception($"Unknown Packet Type {header.ID}")` (`GameClient.cs:1865`) et casserait la
boucle de réception : c'est la raison d'être de la contrainte.

Le commentaire de `GamePackets.cs:74-78` (**ne pas y toucher**) rappelle que 1304 est, en 7.3,
`TM_CS_AUCTION_BIDDED_LIST` ; ce lot ne déclare que 1300.

### 5.3 La réponse : `1301`

`Connection.Send(GameAuctionPackets.BuildAuctionSearch(pageNum, totalPageCount, entries))`.

- Signature existante et livrée par le socle : `BuildAuctionSearch(int pageNum, int totalPageCount,
  IReadOnlyList<SearchedAuctionInfo> entries = null)` (`GameAuctionPackets.cs:75-92`), qui produit
  **5139 octets** (`SearchPacketSize`, `:63`), écrit `page_num` à 7, `total_page_count` à 11, le
  compte à 15 (plafonné à 40, `:136-144`), et **laisse toujours les 40 emplacements de 128 octets
  écrits**, vides ou non (`:82-88`) ;
- `page_num` est l'**écho** de la requête (socle §5.3) ;
- `total_page_count` est un paramètre : sa règle n'est pas établie (§7, point 4) ;
- ne pas réécrire le motif d'objet de 75 octets : il vient de `ItemFixedInfoWriter`
  (`ItemFixedInfoWriter.Size = 75`, `GameAuctionPackets.cs:149`).

Le client copie 5120 octets sans regarder le compte (§3.3) : une réponse plus courte désaligne
silencieusement l'affichage. 5139 est donc la seule taille acceptable.

`SearchedAuctionInfo` (128 = 96 + 31 + 1) porte `Auction` (96), `SellerName` (31) et `Flag` (1)
(`GameAuctionPackets.cs:56-57,86-87`) ; le contenu de `Flag` est une réserve (§7, point 1).

### 5.4 D'où viennent les annonces : ce qui est prouvable

**Ce lot n'a aucune annonce à servir, et c'est démontré.** Le dépôt ne contient **aucun écrivain** de
la table des annonces : `TelecasterContext.Auctions` n'est déclaré qu'une fois
(`Game/DataAccess/Contexts/TelecasterContext.cs:11`, plus sa configuration à `:146`) et rien dans
`Game/` n'ajoute, ne modifie ni ne supprime d'`AuctionEntity` (aucun `Auctions.Add`/`Remove`/
`Update` dans le dépôt). La seule mention d'enchère hors `DataAccess` est `StorageRules.cs:107`
(`item.AuctionId is null`, une garde de rangement). Conséquence : pour ce lot, la page de 1300 est
nécessairement **vide** — 0 entrée, 40 emplacements à zéro, 5139 octets — et c'est vérifiable en
test.

Ce qui reste **prouvable** pour la suite, et que le dev peut écrire dès maintenant sans inventer :

| Règle | Élément du modèle | Source |
|---|---|---|
| Une annonce expirée ne se montre plus | `AuctionEntity.EndTime` (l'unique borne temporelle du modèle) | `AuctionEntity.cs:20` |
| Une annonce réglée ne se montre plus | `ItemStorageEntity.RelatedAuctionId` + `StorageType` : `ItemBySuccessfulBid = 1`, `ItemByInstantPurchase = 2`, `ItemByExpiration = 3`, `ItemByCancel = 4` — les **quatre** façons dont une annonce se termine | `ItemStorageEntity.cs:20` ; `StorageType.cs:7-10` |
| Le vendeur, le prix courant, l'achat immédiat, la durée | `SellerId`/`SellerName`, `HighestBiddingPrice`, `InstantPurchasePrice`, la durée étant un type porté par la trame (96 octets), pas par le modèle | `AuctionEntity.cs:10-24` ; `GameAuctionPackets.cs:150-152` |

Ce qui est une **décision de Killian**, donc jamais à trancher par le dev : l'ordre des annonces
dans une page, le nombre d'annonces par page (40 est imposé par la trame, pas par le jeu), la règle de
`total_page_count` (plafond, arrondi), la sémantique exacte de « visible » (voir §7 points 2 et 4) et
le filtre de catégorie (§5.5).

### 5.5 Le filtre de catégorie : ce que le fichier client établit

Le socle (§8.5) laissait les colonnes de catégorie non interprétées, faute d'avoir décodé
`reference/client73/db_auctioncategoryresource.rdb`. Ce fichier est lisible en grille régulière, et
le décodage est reproductible (`sha256 ca4dad3ccbd482275d3de3f62f4ed9fad1d2e966d2f525c4c5161aa8291fb415`) :

- en-tête de 132 octets : `20081107` en ASCII aux octets 0-7, puis des zéros, puis le **nombre
  d'enregistrements `38`** en `int32` LE aux octets `0x80-0x83` ;
- 38 enregistrements de **24 octets** (6 `int32` LE) jusqu'à la fin du fichier
  (132 + 38 × 24 = 1044, taille exacte du fichier) ;
- colonnes observées, dans l'ordre : `catery_id` 0…18 (19 valeurs), `sub_catery_id` `-1` puis 0…11
  (`-1` = pas de sous-catégorie), `name_id` 6700…6820 (**38 valeurs distinctes**, une par
  enregistrement), `local_flag` = 0 sur les 38 lignes, `item_group` ∈ {1,2,3,4,5,6,7,8,9,10,13,17,
  21,22,23,93,97,99,110}, `item_class` ∈ {-1} ∪ {101…113, 201…204, 301…303} ;
- 18 des 19 valeurs de `item_group` tombent exactement sur l'énumération `ItemGroup` du dépôt
  (`Weapon = 1`, `Armor = 2`, `Shield = 3`, `Helm = 4`, `Glove = 5`, `Boots = 6`, `Belt = 7`,
  `Mantle = 8`, `Accessory = 9`, `Skillcard = 10`, `Summoncard = 13`, `Bag = 17`, `StrikeCube = 21`,
  `DefenceCube = 22`, `SkillCube = 23`, `Soulstone = 93`, `Consumable = 99`, `Deco = 110` —
  `Game/DataAccess/Entities/Enums/ItemGroup.cs`) ; seule la valeur 97 n'a pas d'entrée ;
- les `item_class` se rangent par famille : 1xx pour `item_group = 1`, 201…204 pour `item_group = 2`,
  301…303 pour `item_group = 9`, `-1` quand la ligne est une racine.

En-tête à comparer à `docs/packet-specs/203-drop-item.md:383-387` : les `.rdb` **régénérés** de ce
répertoire portent `20251207` puis `Written by Archemedes v0.1.0`. Celui-ci porte `20081107` et **ne
contient pas** la chaîne `Archemedes` : `db_auctioncategoryresource.rdb` n'est pas un artefact de cet
outil, ce qui en fait une source plus directement opposable que les `.rdb` régénérés — mais
`db_string.rdb`, lui, **est** régénéré (en-tête `20251207` + `Written by Archemedes v0.1.0`), donc la
résolution de `name_id` vers un libellé ne se fait pas dans ce lot.

Ce que cela permet et ce que cela ne permet pas :

- **permet** de dire que `category_id`/`sub_category_id` de 1300 sont les deux premières colonnes de
  cette table, et que le filtre de catégorie s'exprime en `item_group` (+ `item_class` quand
  `sub_catery_id ≠ -1`) ;
- **ne permet pas** de l'implémenter complètement : `item_class` (101…113, 201…204, 301…303) n'a
  **aucun correspondant établi** dans le catalogue d'objets du dépôt (`ItemResourceEntity` expose
  `Group`, `ItemType`, `WearType`, `SetPart`, mais aucun « class » : `ItemResourceEntity.cs:11-20`) ;
- **ne permet pas** d'affirmer que la table serveur `AuctionCateryResource`
  (`ArcadiaSchemaPSQL.sql:1-9`, exploitée par `AuctionCateryResourceRepository.GetAll()`,
  `AuctionCateryResourceRepository.cs:23-39`) porte les mêmes lignes : aucune donnée n'est disponible
  sur ce VPS (pas de PostgreSQL), et le dépôt ne contient que le DDL.

Décision à porter au dev, dans l'ordre : lire `category_id`, `sub_category_id` et `is_equipable`,
les **journaliser**, et répondre une page vide (§5.4) sans filtrer. Toute autre politique (filtrer
sur `item_group`, page vide sur catégorie inconnue, erreur sur catégorie inconnue) est une décision de
Killian (`## A VERIFIER PAR KILLIAN`, point 2) : elle devient observable dès la première annonce
créée. `AuctionCateryResourceRepository.GetAll()` n'a aujourd'hui aucun consommateur
(`DevConsole/Program.cs:303` l'enregistre sans l'utiliser) : ce lot peut rester le premier
consommateur, mais rien ne l'exige tant qu'aucune annonce ne peut exister.

### 5.6 `is_equipable`

Le champ est **lu** au sens strict : l'octet 50 fait partie de la trame et doit être consommé (§4).
Son **effet** sur la sélection n'est pas établi. Le seul signal « peut être porté » du dépôt est
`ItemResourceEntity.WearType` (`ItemWearType`, `CantWear = None = -1`,
`Entities/Enums/ItemWearType.cs:5-6`), ce qui en fait un candidat documenté — pas une preuve : le
nom du champ est côté client, aucun handler NGemity ne le lit, et rien ne dit si le filtre porte sur
« l'objet se porte » ou sur « l'objet est en cours d'équipement chez son propriétaire ». Le dev
**n'invente pas** : il lit et journalise, et la sémantique va en réserve (`## A VERIFIER PAR
KILLIAN`, point 3).

### 5.7 Ce que le lot doit livrer, et le cas « pas de code »

Le « cas pas de code » ne s'impose pas : la trame est entièrement mesurée (§3), la réponse est
entièrement spécifiée par le socle et par la mesure cliente (§5.3), et l'absence d'écrivain de
`Auctions` (§5.4) rend la page vide exécutable et testable. Le lot se limite donc à :

1. `GamePackets.cs` : `TM_CS_AUCTION_SEARCH = 1300`, dans la bande 13xx, avec un commentaire d'une
   ligne renvoyant à cette fiche ;
2. `GameAuctionPackets.cs` : le `TryReadAuctionSearch` de §5.1 ;
3. `GameClient.cs` : le bras `if (… TM_CS_AUCTION_SEARCH)` de §5.2 et son `HandleAuctionSearch`, qui
   lit, journalise (catégorie, mot-clé, page, indicateur) et envoie
   `BuildAuctionSearch(pageNum, 0, null)` ;
4. `Tests/` : au moins un test d'**offsets** du lecteur — taille 51, positions 7/11/15/46/50, arrêt du
   mot-clé au premier NUL — plus un refus sur trame trop courte et l'écho de `page_num` dans la
   réponse. Les tests existants du socle (`Tests/Game/AuctionPacketsTests.cs` : 13 `[Test]`, soit 14
   attributs `[Test…]` — le brief de carte compte le `[TestFixture]` de la classe — motif de 75 octets
   et tailles 5139/3899) ne se réécrivent pas ; la baseline de ce réveil est
   `dotnet test Tests/Tests.csproj` = **1302 tests, code 0**.

Le dev n'écrit ni `CLAUDE.md` (protégé par Hermes) ni `op_codes.md` ; le bloc destiné à `CLAUDE.md`
est en §10 de cette fiche et doit figurer dans la description de la MR.

---

## 6. Écarts assumés avec NGemity

NGemity **ne traite aucun paquet d'enchère** : `TS_CS_AUCTION_SEARCH` n'existe chez lui que comme
déclaration (`shared/Server/Packets/GameClient/TS_CS_AUCTION_SEARCH.h:6-13`), incluse par
`shared/Server/ClientPackets.h` et `XPacket.h`, sans `case` dans un gestionnaire et sans
`SecRouteAuction` consommé ; aucune classe de ressource d'enchère n'y existe. Ses fichiers d'aide
`Item.cpp` / `ItemInstance.h` / `WorldLocation.h` mentionnent les enchères sans les traiter, et
`CharacterDatabase.cpp` ne les touche pas.

Il n'y a donc **rien à porter** : c'est le client 7.3 et le modèle déjà présent dans le dépôt
(`AuctionEntity`, `ItemStorageEntity`, `AuctionCateryResource`) qui tranchent. Deux écarts sont
assumés et motivés :

1. **La composition de la trame** est celle du client (51 octets, cinq champs), pas celle de NGemity :
   NGemity compile la même déclaration mais avec `EPIC_4_1_1` (`Define.h:25`) et sans gestionnaire,
   donc son code ne prouve rien sur 7.3.
2. **La réponse est toujours pleine** (40 emplacements, 5139 octets) alors que NGemity n'a pas de
   réponse du tout : la mesure cliente de §3.3 (copie de 5120 octets sans lire le compte) impose ce
   format, comme le socle l'a déjà établi (§5.2).

---

## 7. NON ÉTABLI

Formulés comme des questions à trancher, jamais comme des valeurs. Aucun de ces points n'est
implémentable sans décision : aucun n'est deviné dans cette fiche.

1. **Sémantique et valeurs de `flag`** (`TS_SEARCHED_AUCTION_INFO.flag`, octet 127 de chaque entrée de
   1301). Question : que représente cet octet (le socle §8.1 le porte sans l'interpréter) ? Quelle
   valeur pour une annonce en cours, pour une annonce sur laquelle le demandeur a misé, pour une
   annonce expirée ? Sources disponibles : aucune. Effet tant que la question est ouverte : `0`.
   Socle §8.1, §9 q3.
2. **Règle de visibilité « village caché »** : `AuctionEntity.IsHiddenVillageOnly`
   (`AuctionEntity.cs:19`) est annoté « check usage -> leftover of premium "vip" content ». Question :
   une annonce marquée est-elle cachée à tous, ou réservée aux joueurs d'un même village ? Socle
   §8.8, §9 q4.
3. **Plafond d'annonces et pagination** : combien d'annonces par page (la trame en impose 40, pas le
   jeu), dans quel ordre, et quelle règle pour `total_page_count` (nombre total de pages, 0 quand la
   recherche est vide, plafond) ? `BuildAuctionSearch` prend `totalPageCount` en paramètre faute de
   règle (`GameAuctionPackets.cs:71-73`). Socle §8.6, §9 q5.
4. **`appearance_code`** dans le motif de 75 octets : le client 7.3 le lit alors que rzu le gate à
   `>= EPIC_7_4`. Question : quelle valeur pour une annonce dont l'objet ne porte pas de code
   d'apparence (0, ou recopie depuis la ressource d'objet) ? Le socle a tranché la **taille** (75,
   `CLAUDE.md:1207-1215`) mais pas la valeur. Socle §8.3, §9 q6.
5. **Signification des colonnes de catégorie côté serveur** : §5.5 établit la table du client ; reste
   ouvert (a) l'identité entre cette table et `AuctionCateryResource` du serveur, (b) la
   correspondance `item_class` → catalogue d'objets, (c) `local_flag`, constant à 0 dans le fichier
   client, donc indéchiffrable par lecture. Socle §8.5, §9 q7.
6. **Nature exacte de la seconde table de `SGameInterface`** (§3.4, `0x63D3DF`) : ses valeurs
   comparées (0x192, 0x2C2, 0x514, 0x516-0x51E, 0x546) recouvrent des opcodes de la famille sans
   qu'on puisse dire si la table indexe des ids de message interne ou des opcodes, ni ce que la case
   1300 déclenche réellement. Ce qui est établi : elle ne lit aucun champ d'une trame 1300.
7. **`item_info.flag` d'un objet sans flag** : quelle valeur écrire dans un motif d'objet pour un
   objet que le modèle ne marque pas (`ItemEntity.Flag` est un `ItemFlag` non nullable) ? Socle §9 q10.
8. **Résolution de `name_id`** (6700…6820) vers un libellé : `db_string.rdb` est un `.rdb` régénéré
   par un outil tiers (en-tête `20251207` + `Written by Archemedes v0.1.0`) : sa structure
   d'enregistrement n'est pas décodée ici et un tel fichier n'est pas une source d'autorité
   (`docs/packet-specs/203-drop-item.md:383-387`). Aucun paquet de 1300 n'en dépend : question de
   documentation seulement.

---

## 8. A VERIFIER PAR KILLIAN

Ce que le dev ne peut pas trancher et qui est visible dès la première annonce créée.

1. **Le contenu de `flag`** (§7, point 1) — décision de format, à trancher avant que la première
   annonce ne soit servie ; tant qu'elle manque, la valeur écrite reste 0.
2. **Politique de recherche** : catégorie inconnue → page vide, ou `TS_SC_RESULT` avec
   `ResultCode.InvalidArgument`, ou refus silencieux ? Et pour une catégorie valide : filtrer sur
   `item_group` (`+ item_class`), ou tout renvoyer et laisser le client filtrer ? Le décodage de
   §5.5 rend le filtre écrivable pour `item_group`, mais `item_class` n'a pas de correspondant dans
   le catalogue d'objets.
3. **Sémantique de `is_equipable`** (§5.6) : filtre « objet portable » (`ItemWearType != None`),
   « objet actuellement équipé chez un tiers », ou champ ignoré ?
4. **Visibilité et ordre** : règle « village caché » (§7, point 2) ; ordre de page (date de fin
   croissante, prix, uid) ; plafond et `total_page_count` (§7, point 3) ; visibilité d'une annonce
   déjà réglée mais non retirée (la table des rangements porte quatre types de fin, `StorageType` 1 à
   4 — lequel fait foi ?).
5. **`AuctionCateryResource` du serveur** : la table est-elle la copie de celle du client (§5.5), et
   faut-il la lire comme un catalogue de référence d'objets (`item_group` → `ItemResourceEntity.Group`),
   ou comme une simple liste d'affichage ? Aucune donnée serveur n'est disponible sur ce VPS : la
   réponse viendra de la base de Killian.

### Ajouté par le dev (§13)

6. **Une trame de 1300 plus longue que 51 octets est-elle un client légitime ?** La fiche ne tranche
   que le cas court (§5.1 : refus si `< 51`), et le lecteur livré suit cette lettre : une trame de
   52 octets ou plus est lue, ce qui suit l'octet 50 étant ignoré. Les lecteurs de 57 et 59, eux,
   refusent toute longueur autre que l'exacte (`!=`). Resserrer ici serait une ligne
   (`packet.Length != SearchRequestSize`) ; desserrer là-bas en serait une autre. Tant que Killian
   n'a pas tranché, la divergence reste telle quelle.

---

## 9. Commits et binaires épinglés

Vérifiés dans les dépôts de référence le 2026-09-27 (réveil du 2026-09-27 18:00 CEST), pas recopiés.
Méthode de lecture statique : `objdump -d` (désassemblage), balayage linéaire de tous les `call`/`jmp`
`rel32` de `.text` pour les xrefs directes (les appels indirects, par vtable ou pointeur de fonction,
ne sont pas couverts — le client en utilise, p. ex. l'envoi de §3.1), `xxd` et lecture d'octets aux
adresses (`va = offset de fichier + delta de section`, `.text` : delta `0x400C00`).

| Élément | Empreinte | Vérification |
|---|---|---|
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` — « packets: fix TS_SC_INVENTORY with older epics », 2023-10-02 | `git rev-parse HEAD` dans `reference/rzu` |
| ngemity / Chihiro | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` — « Fix compilation issue for GCC », 2025-12-03 | `git rev-parse HEAD` dans `reference/ngemity` |
| client 7.3 | `reference/client73/SFrame.exe`, `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 octets | `sha256sum` |
| client 7.3 — table des catégories | `reference/client73/db_auctioncategoryresource.rdb`, `sha256 ca4dad3ccbd482275d3de3f62f4ed9fad1d2e966d2f525c4c5161aa8291fb415`, 1 044 octets, en-tête `20081107` | `sha256sum`, `xxd` |
| Navislamia | `origin/master` = `b56967a07430422add88e0e5cdf292b41b18f6c6` — merge PR #44, 2026-09-24 ; baseline `dotnet test Tests/Tests.csproj` = 1302 tests, code 0 | `git rev-parse origin/master`, `dotnet test` |

Ce qui n'est pas vérifiable ici et qui est dit comme tel : `reference/client73/` n'est **pas** un
dépôt Git — aucune empreinte de commit cliente n'est opposable, seule celle des fichiers l'est. Aucun
serveur de jeu, aucune base de données, aucun lancement de `SFrame.exe`, de Lua ou d'un script client :
toute la lecture est statique.

---

## 10. Bloc destiné à `CLAUDE.md`

À recopier par le dev dans la description de la MR (jamais dans `CLAUDE.md` lui-même, protégé par
Hermes), sous la section « Enchères » existante (`CLAUDE.md:1202`) :

```markdown
### `TM_CS_AUCTION_SEARCH` (1300) — la demande de recherche

`docs/packet-specs/1300-auction-search.md` fixe la trame : **51 octets**, `category_id` `int32` @7,
`sub_category_id` `int32` @11, `keyword` `char[31]` @15 (zone fixe NUL-terminée, pas de longueur
préfixée), `page_num` `int32` @46, `is_equipable` `bool` @50 — gating rzu `version >= EPIC_7_2`,
donc **présent** en 7.3, mesuré sur le client (`SFrame.exe` `0x48CA20` constructeur, `0x48DC80`
sender).

Le client 7.3 **envoie** cette trame et **ne la reçoit jamais** : dans le répartiteur serveur → client
`0x67E67A`, la table `0x67F4C8` laisse l'indice de 1300 sur le chemin « non traité » (`0x67EF21`) ;
seuls 1201, 1301, 1303 et 1305 y ont un gestionnaire. Ne jamais répondre 1300.

La réponse est `TM_SC_AUCTION_SEARCH` (1301), **5139 octets**, `page_num` recopié, les **40
emplacements toujours écrits** (`GameAuctionPackets.BuildAuctionSearch`) : le client copie 5120 octets
en bloc sans lire le compte (`0x670660`).

Aucun code du dépôt n'écrit dans `TelecasterContext.Auctions` : la recherche répond une page vide,
et c'est vérifiable. La sélection (ordre, plafond, `total_page_count`), le filtre de catégorie
(`AuctionCateryResource`, dont la correspondance serveur n'est pas prouvée), `is_equipable` et la
valeur de `flag` sont des décisions ouvertes, listées dans la fiche.
```

---

## 11. Références croisées

- Socle : `docs/packet-specs/socle-encheres.md` §1.2, §3.2 (trame de 1300), §3.8 (réponse 1301),
  §3.10 (tailles), §4 (gating), §5.2-§5.3 (40 emplacements, réponse attendue), §6.3 (ordre de
  reprise), §8 (`NON ÉTABLI`), §9 (`A VERIFIER PAR KILLIAN`), §11 (métrique de lecture), §12 (ce que
  le socle a livré).
- Dépôt : `CLAUDE.md:1202-1230` (section Enchères), `GamePackets.cs:74-78` (avertissement sur 1304,
  **inchangé**), `GamePackets.cs:156-158` (les trois réponses), `GameClient.cs:1405-1411` (bras `log +
  continue` des trois réponses), `GameClient.cs:1839-1866` (embrasure du nouveau bras et `switch`
  final), `GameAuctionPackets.cs:46-92` (tailles et `BuildAuctionSearch`),
  `ItemFixedInfoWriter.cs` (motif de 75 octets), `AuctionEntity.cs`, `ItemStorageEntity.cs`,
  `StorageType.cs`, `AuctionCateryResourceRepository.cs`, `ArcadiaSchemaPSQL.sql:1-9`,
  `Tests/Game/AuctionPacketsTests.cs`.
- NGemity : `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_AUCTION_SEARCH.h`,
  `shared/Common/Define.h:25` — déclaration seule, aucun handler.

---

## 12. Ce que cette fiche resserre ou corrige dans le socle

Le socle reste la référence de la famille ; ce lot a refait les mesures et apporte cinq écarts, tous
mineurs et tous argumentés, à porter au socle quand Killian le voudra (cette fiche ne le modifie pas).

1. **Source de l'écriture de `Id` en §3.1 du socle.** Le socle attribue l'écriture de `Id` à
   `0x48dcc4`-`0x48dcc7` (`socle-encheres.md:101`). La mesure montre que `Id = 1300` est écrit par le
   **constructeur** (`0x48CA48`, `0x48CA52`) et que `0x48DCC7` réécrit `Length = 51` dans le sender. La
   conclusion du socle (en-tête de 7 octets, `Id` à 4, checksum à 6) est inchangée ; seule la source
   d'une ligne est à corriger.
2. **`AuctionCateryResource` en §5.5 du socle.** Le socle écrit « aucune entité ni repository ne la
   lit » (`socle-encheres.md:388`), ligne écrite avant que son propre §12.3.4 ne livre
   `AuctionCateryResourceEntity`, `AuctionCateryResourceRepository.GetAll()` et son enregistrement
   (`DevConsole/Program.cs:303`). L'état courant du dépôt est celui décrit en §5.4/§5.5 ci-dessus :
   l'entité et le repository existent, sans consommateur.
3. **Le `.rdb` des catégories, laissé « non décodé » (§5.5 et §8.5 du socle).** §5.5 de cette fiche le
   décode et en tire la sémantique des quatre premières colonnes. La réserve n'est pas levée pour
   autant : l'identité avec la table serveur et la correspondance de `item_class` restent ouvertes
   (§7, point 5).
4. **Les slots du répartiteur interne.** Le socle §2 associe chaque action à un nom RTTI de message
   interne ; cette fiche donne en plus, pour la famille, le mapping *id de message → sender*
   (§2 : 1152 → 1300, 1154 → 1302, 1156 → 1304) et le nom de la fenêtre de recherche
   (`SUIAuctionSearchWnd`).
5. **`keyword` : remplissage à zéro et copie bornée.** Le socle §3.2 conclut « le client garantit le
   remplissage à zéro » — exact pour le `memset` du constructeur, mais la copie `strncpy` bornée à
   31 octets (`0x48DCB2`) réécrit la totalité des 31 octets si le mot-clé saisi atteint 31 caractères,
   sans NUL final. Le lecteur serveur doit s'arrêter au premier NUL **sans exiger** sa présence.

---

## 13. Implémentation livrée par `navis-dev`

Branche `hermes/packet-1300-auction-search`, poursuivie depuis cette fiche ; commit de code `933ed8a`.
Les §1 à §12 restent la mesure de `navis-ref` : rien n'y a été réécrit.

| Fichier | Nature du changement |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | membre `TM_CS_AUCTION_SEARCH = 1300`, commenté (trame émise par le client, jamais reçue ; 1304 reste non déclaré) |
| `Game/Network/Packets/Game/GameAuctionPackets.cs` | `AuctionSearchRequest`, `KeywordSize` (31), les cinq `SearchRequest*Offset`, `SearchRequestSize` (51), `TryReadAuctionSearch`, `ReadFixedAscii` |
| `Game/Network/Clients/GameClient.cs` | `HandleAuctionSearch` (après `HandleXtrapCheck`) et le bras d'aiguillage, juste avant le `switch` final |
| `Tests/Game/AuctionSearchPacketsTests.cs` | **nouveau** : 18 tests (offset, borne, arrêt au premier NUL, aiguillage, réponse vide) |

### 13.1 Ce que fait le code

- `TryReadAuctionSearch(ReadOnlySpan<byte> packet, out AuctionSearchRequest request)` : refus si
  `packet.Length < 51`, puis `category_id` et `sub_category_id` en `Int32` **little endian** aux
  offsets 7 et 11, `keyword` = les 31 octets de 15 à 45 lus jusqu'au premier NUL, `page_num` en
  `Int32` LE à 46, `is_equipable` = `packet[50] != 0`. Le mot-clé passe par `ReadFixedAscii`, qui
  s'arrête au NUL quand il existe et **ne l'exige pas** (§3.2, §12.5) : un mot-clé de 31 caractères,
  sans terminateur, se lit entier.
- Le bras lit, journalise les cinq champs (une ligne `Debug` derrière `_logger.IsEnabled(…)`, idiome
  de `HandleEquipSummon`) puis envoie `BuildAuctionSearch(request.PageNum, 0)` : page vide,
  5139 octets, `total_page_count` à 0 — la règle de ce dernier n'étant pas établie (§7, point 3).
- Une trame de moins de 51 octets reçoit
  `SendResult(TM_CS_AUCTION_SEARCH, ResultCode.InvalidArgument)` ; jamais une trame
  `TM_SC_AUCTION_*` d'erreur, qui n'existe ni dans les références ni dans le client (§5.1).
- `BuildAuctionSearch`, le motif de 75 octets, `AuctionInfo` et les trois réponses ne sont pas
  touchés : ce lot n'ajoute que la moitié « demande ».
- Aucune lecture de base, aucune requête, aucune écriture : la page est vide parce que rien dans le
  dépôt n'écrit `TelecasterContext.Auctions` (§5.4), et c'est ce que le test vérifie octet par octet.

### 13.2 Ce qui n'a pas été décidé, et n'a donc pas été inventé

`total_page_count` à 0, `flag` laissé à 0, aucun filtre de catégorie, aucun tri, aucune consommation
d'`AuctionCateryResourceRepository`, et `is_equipable` lu sans effet : ce sont les huit points `NON
ÉTABLI` de §7 et les cinq points de §8, repris sans y répondre. Le code n'en tranche aucun.

### 13.3 Réserves du dev

1. **Trame rembourrée acceptée** (§8, point 6 ajouté par le dev) : le lecteur suit §5.1 à la lettre —
   `< 51` refusé — donc une trame de 52 octets ou plus est lue et ce qui suit l'octet 50 est ignoré.
   C'est la seule divergence de forme avec l'idiome strict de 57 et 59 (`<` contre `!=`), laissée
   telle quelle parce que la fiche ne tranche que le cas court.
2. **`BuildAuctionSearch(pageNum, 0)` sans liste** : appelé avec `null`, comme §5.7 le prescrit.
   Aucun contenu n'est fabriqué pour rendre le test possible, et le test d'aiguillage vérifie que les
   40 emplacements sortent effectivement à zéro.
3. **`keyword` vide et `is_equipable` non nul quelconque** sont acceptés sans distinction : la fiche
   n'établit aucun refus pour l'un ni borne pour l'autre (§5.6), donc le lecteur ne les invente pas.

### 13.4 Vérifications exécutées (conteneur, sans serveur de jeu ni base)

```
dotnet build Navislamia.sln -c Debug                  → code 0, 0 Error(s)
dotnet test  Tests/Tests.csproj                       → code 0, 1320 réussis / 1320, 0 échec
                                                         (base avant ce lot : code 0, 1302 / 1302)
dotnet test --filter AuctionSearchPacketsTests        → code 0, 18 / 18
```

Morsures prouvées par mutation temporaire, puis restauration (`git status --porcelain` vide) :

- **bras d'aiguillage retiré** (membre d'énum conservé) → 4 tests échouent, tous avec
  `System.Exception: Unknown Packet Type 1300` : le critère transversal n° 4 est bien morsu, et le
  test ne passe pas par accident ;
- **`SearchRequestKeywordOffset` décalé de 15 à 14** → 9 tests échouent (offsets, lecture, réponse) :
  les tests d'offsets portent donc sur les positions écrites à la main, pas sur une relecture de la
  même constante.

Les trames des tests sont assemblées octet par octet, jamais par `GameAuctionPackets` : l'en-tête est
littéral (`0x33 00 00 00 | 0x14 0x05`, checksum `0x4C`), chaque champ est relu à sa position en
little endian **et** en gros-boutiste avec un `NotBe`, et le mot-clé est vérifié avec un NUL suivi
d'un octet parasite (`"Swo\0"` puis `'d'`) pour prouver l'arrêt au premier NUL.

Aucun code de production n'a été exécuté contre un client : `SFrame.exe` n'est pas lancé, et aucune
base de données n'est disponible sur ce VPS. Les faits de protocole viennent des mesures de §3 et des
références épinglées en §9 ; ils ne sont pas re-mesurés ici.

