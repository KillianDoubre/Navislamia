# `TM_CS_AUCTION_SELLING_LIST` (1302) — fiche de paquet, Epic 7.3

Deuxième carte du découpage du socle enchères (`docs/packet-specs/socle-encheres.md` §6.3, rang 2 :
« 1302 / 1304 `*_LIST` », prérequis S2, décision restante « définition d'« annonce du
personnage » »). Cette fiche reprend la trame du socle §3.3, la re-mesure sur le client, tranche son
gating pour 7.3, et **sépare ce qui est prouvable de ce qui est une décision de Killian**.

Le socle n'est pas recopié : ses sections sont citées (`socle-encheres.md §…`). Toute adresse,
tout offset et tout numéro de ligne marqués *(mesure du réveil)* ont été relus ici ; le reste est
une citation explicite, avec sa source.

---

## 1. Identité

| élément | valeur | source |
|---|---|---|
| id décimal | `1302` | — |
| nom | `TM_CS_AUCTION_SELLING_LIST` | `op_codes.md:204` (`[1302] = "TM_CS_AUCTION_SELLING_LIST"`) |
| sens | client → serveur | rzu `TS_CS_AUCTION_SELLING_LIST.h:14` (`SessionPacketOrigin::Client`) ; socle §1 |
| déclaration rzu | `TS_CS_AUCTION_SELLING_LIST` | `reference/rzu/librzu/src/packets/GameClient/TS_CS_AUCTION_SELLING_LIST.h:14` |
| réponse | `1303` `TM_SC_AUCTION_SELLING_LIST` | `op_codes.md:205` ; rzu `TS_SC_AUCTION_SELLING_LIST.h:19` |
| déclaration NGemity | `TS_CS_AUCTION_SELLING_LIST`, 1302 | `shared/Server/Packets/GameClient/TS_CS_AUCTION_SELLING_LIST.h:9` ; `shared/Server/ClientPackets.h:211` |
| frappe du client | trame de **11 octets**, construite et envoyée par `0x48dd10` | *(mesure du réveil)* §3 |

C'est la demande de la liste **des ventes du personnage** qui demande (la réponse 1303 ne peut
désigner aucun autre vendeur, §5.5). Elle n'est pas la liste des enchères posées : celle-ci est
`1304` `TM_CS_AUCTION_BIDDED_LIST`, trame identique et point d'entrée distinct (§3.3).

---

## 2. Ce que le joueur fait

Le joueur ouvre l'onglet des ventes de l'hôtel des ventes, puis change de page dans la liste. Le
socle §2 attribue la commande à la fenêtre `SUIAuctionWnd` (`0xc1ceb8`) et au message interne
`AUSIMSG_REQ_AUCTION_SELLING_LIST` ; la mesure du réveil va plus loin et ferme la chaîne de
l'émission, de bout en bout :

| étape | ce qui est mesuré | source *(mesure du réveil)* |
|---|---|---|
| 1. création et remplissage du message interne | objet de **23 octets** (`push $0x17` → `operator new`) dont la vtable est `0xa25730`, le COL `0xbbd2f0` nommant `.?AUSIMSG_REQ_AUCTION_SELLING_LIST@@` | `0x4f8a06-0x4f8a27`, `0x4f8b16-0x4f8b34`, `0x4f9446-0x4f9464` |
| 2. l'identifiant du message | `[objet+4] = 0x482` = **1154**, écrit aux **trois** sites | `0x4f8a13`, `0x4f8b16`, `0x4f9446` |
| 3. la charge utile | `[objet+0x13]` = `page_num` : `1` à deux sites (première page de l'onglet), la page passée en argument à un troisième | `0x4f8b34`, `0x4f9464` (`movl $0x1,0x13(%eax)`) ; `0x4f8a27` (`mov 0x8(%ebp),%ecx` → `mov %ecx,0x13(%eax)`, épilogue `ret $0x4` à `0x4f8a41`) |
| 4. envoi du message interne | `push $0x3e8` (1000) puis `call 0x4f0510` | `0x4f8a34-0x4f8a3a`, `0x4f8b45-0x4f8b4b`, `0x4f9475-0x4f947b` |
| 5. aiguillage interne sur la clé `0x482` | `sub $0x403,%eax` / `cmp $0xde,%eax` / `movzbl 0x49ea50(%eax),%eax` / `jmp *0x49e98c(,%eax,4)` ; pour 1154 : index 127, octet `0x22`, entrée de table `0x49e37a` | `0x49e21d-0x49e234` (l'aiguilleur lui-même) ; table d'octets `0x49ea50`, table de sauts `0x49e98c` |
| 6. stub → trame | `0x49e37a` : `push %edi ; mov %esi,%ecx ; call 0x48dd10 ; jmp 0x49e75f` | `0x49e37a-0x49e382` |
| 7. la trame `1302` part | `0x48dd10` écrit `Length = 11`, `Id = 1302`, la somme de contrôle, `page_num` depuis `[objet+0x13]`, puis appelle le `send` virtuel (`mov 0xb8(%ecx),%ecx` → `mov (%ecx),%edx` → `mov 0xc4(%edx),%edx` → `call *%edx`) | `0x48dd26`, `0x48dd30`, `0x48dd2b`, `0x48dd34`, `0x48dd37-0x48dd4a`, `0x48dd4d-0x48dd53`, `0x48dd77-0x48dd89` |

Conséquences utiles au lot :

- **la page est 1-based** : deux des trois sites d'émission écrivent `1` (`0x4f8b34`, `0x4f9464`),
  et le seul site qui prend une page en argument (`0x4f8a27`) reçoit cette valeur du code de
  navigation de la fenêtre ;
- **le client ne reçoit jamais `1302`** : dans le seul répartiteur serveur → client couvrant la
  plage (`0x67e67a` : `sub $0x4b1,%eax` / `cmp $0x96,%eax` / `ja 0x67ef21` / `movzbl
  0x67f4c8(%eax),%edx` / `jmp *0x67f4b0(,%edx,4)`), `1302` occupe l'index 101 et tombe sur l'octet
  `0x05`, c'est-à-dire le même chemin « message non traité » que le débordement (`0x67ef21`) —
  *(mesure du réveil)*, identique à la mesure du socle §1 ;
  `1303`, en revanche, a son handler dédié `0x6706d0` (§5.4) ;
- si le joueur n'ouvre jamais l'onglet, aucune trame `1302` n'est émise : le serveur ne peut donc
  pas compter sur cette demande pour initialiser quoi que ce soit.

---

## 3. Structure sur le fil

### 3.1 Trame `TM_CS_AUCTION_SELLING_LIST` — **11 octets**

`0x48dd10` construit sa trame dans un tampon local de 12 octets (`sub $0xc,%esp` à `0x48dd13` :
11 octets de trame + 1 octet d'alignement), **remis à zéro avant écriture** (`0x48dd16-0x48dd22`
couvrent les offsets 0 à 10).

| offset | taille | type | nom (rzu) | valeur écrite par le client | source *(mesure du réveil)* |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` | `11` (`0xb`) | `0x48dd2b` (`mov $0xb,%esi`), `0x48dd34` (`mov %esi,-0xc(%ebp)`) |
| 4 | 2 | `uint16` | `Id` | `1302` (`0x516`) | `0x48dd26` (`mov $0x516,%eax`), `0x48dd30` (`mov %ax,-0x8(%ebp)`) |
| 6 | 1 | `uint8` | `Checksum` | somme des octets 0..5 | `0x48dd37-0x48dd4a` : `lea -0xc(%ebp),%eax`, `xor %dl,%dl`, boucle `add (%eax),%dl` / `inc %eax` jusqu'à `-0x6(%ebp)`, écriture `mov %dl,-0x6(%ebp)` |
| 7 | 4 | `int32` | `page_num` | `1` à la première page ; la page courante sinon | rzu `TS_CS_AUCTION_SELLING_LIST.h:8` ; client `0x48dd4d` (`mov 0x8(%ebp),%edx`), `0x48dd50` (`mov 0x13(%edx),%eax`), `0x48dd53` (`mov %eax,-0x5(%ebp)`) |
| 11 | — | — | *(fin de trame)* | — | les deux réécritures de `Length`/`Checksum` de `0x48dd56-0x48dd75` portent sur le même tampon |

**Taille totale attendue : 11 octets** = 7 d'en-tête + 4 de charge utile. C'est le `Length` que le
client écrit et la seule longueur cohérente avec le format ; il n'y a aucun remplissage : les
quatre octets utiles occupent exactement `[7, 10]`.

L'en-tête de 7 octets (`Length` uint32, `Id` uint16, `Checksum` = somme des octets 0..5) est celui
du dépôt (`Game/Network/Packets/Game/GameCharacterPackets.cs:19` `HeaderSize = 7`, écrit par
`CreatePacket` `:382` et `WriteChecksum` `:390`), et c'est bien celui du client : la boucle de
somme de `0x48dd37-0x48dd4a` couvre exactement les six octets `Length` + `Id` et dépose le total
dans l'octet d'offset 6.

### 3.2 Ce que rzu dit de la même trame

`TS_CS_AUCTION_SELLING_LIST.h:7-8` : un seul champ, `_(simple)(int32_t, page_num)`. Aucun champ
conditionnel, aucun `_(string)`, aucune notion de quantité ou de liste dans la demande : **la
demande ne transporte que le numéro de page**, et rien d'autre — ni identité de personnage, ni
catégorie, ni filtre. C'est le serveur qui sait de qui il s'agit (la session) et quoi répondre.

La comparaison avec la sœur `1304` (`TS_CS_AUCTION_BIDDED_LIST.h:7-8`) est littérale : même champ,
même type, même position.

### 3.3 `1302` et `1304` — trame identique, points d'entrée distincts

| | `1302` `SELLING_LIST` | `1304` `BIDDED_LIST` |
|---|---|---|
| rzu | `TS_CS_AUCTION_SELLING_LIST.h:8` (`page_num`) | `TS_CS_AUCTION_BIDDED_LIST.h:8` (`page_num`) |
| constructeur + envoi (client) | `0x48dd10` | `0x48dda0` |
| écrit l'`Id` | `0x48dd26` : `0x516` (1302) | `0x48ddb6` : `0x518` (1304) |
| écrit `Length` | `0x48dd2b` : `0xb` (11) | `0x48ddbb` : `0xb` (11) |
| écrit `page_num` (offset 7) depuis `[objet+0x13]` | `0x48dd50` / `0x48dd53` | `0x48dde0` / `0x48dde3` |
| appelé depuis l'aiguillage interne | `0x49e37d` (stub `0x49e37a`), clé `0x482` = 1154 | `0x49e38a` (stub `0x49e387`), clé `0x484` = 1156 |
| sites d'émission du message | `0x4f8a13`, `0x4f8b16`, `0x4f9446` (vtable `0xa25730`, classe `.?AUSIMSG_REQ_AUCTION_SELLING_LIST@@`) | `0x5008a3`, `0x500f42` (vtable `0xa25b00`, classe `.?AUSIMSG_REQ_AUCTION_BIDDED_LIST@@`) |
| réponse | `1303`, handler client `0x6706d0` | `1305`, handler client `0x670730` |
| état affiché | `status` (octet 96), légende `deposit_status` | `status` (octet 96), légende `bidded_status` |

*(mesures du réveil, sauf la légende par liste de la dernière ligne, citée du socle §8.2.)*

Ce que le socle avait établi **séparément** pour chacun, et que cette fiche confirme par la mesure :
les deux trames sont identiques au champ près, mais chacune a son constructeur, son stub et sa clé
d'aiguillage — `0x48dd10`/`0x49e37a` pour 1302, `0x48dda0`/`0x49e387` pour 1304. Un scan des
appels relatifs (`E8`/`E9`) de tout `.text` ne trouve **qu'un seul** appelant pour chacun :
`0x49e37d` → `0x48dd10`, `0x49e38a` → `0x48dda0`. « Un point d'entrée par paquet » est donc
mesuré, pas supposé : la confusion 1302/1304 (signalée dans `GamePackets.cs:74-77` à propos de 304)
resterait invisible à l'exécution — les deux trames étant identiques — mais elle produirait une
liste d'enchères là où le joueur attend ses ventes.

### 3.4 Contrôle de complétude : le client ne construit `1302` qu'une fois

Recensement brut du motif de quatre octets `16 05 00 00` (la constante `0x516` sous sa forme
`mov`/`push`) dans tout `SFrame.exe` : **9 occurrences**, classées une par une —

| occurrence | nature | conclusion |
|---|---|---|
| `0x48dd26` | `mov $0x516,%eax` | **le constructeur de la trame 1302** (seule écriture de l'`Id`) |
| `0x4fb8d2` | `mov $0x516,%ecx` puis `cmp %cx,0x13(%esi)` en `0x4fb8d6` | comparaison d'un identifiant de **demande** contre `[objet+0x13]`, dans le bloc `0x4fb820` (`cmp $0x426` / `cmp $0x425` / cas `0x483`/`0x498`) : c'est un aiguillage sur le champ « identifiant de la demande » d'un message de résultat, pas une trame (voir §7, question 8) |
| `0x503a09` | `mov DWORD PTR [reg+8],0x516` (`c7 40 08 16 05 00 00`) | écrit la **valeur** 1302 dans un objet : pas un constructeur de trame |
| `0x63d7c5` | `sub $0x516,%eax` | table de cas d'une seconde chaîne de comparaison (`0x63d3df`, socle §3.4 de la fiche 1300), pas une trame |
| `0x47fbd9`, `0x68e7fc`, `0x874bc4`, `0x99fb03` | dépendances de sauts relatifs (l'octet précédent est `E9` ou `0F 8x`), table Win32 | bruit |
| `.rsrc` | ressource | bruit |

Aucune autre occurrence n'est une écriture d'`Id` : le client ne construit la trame `1302`
**qu'à un seul endroit** (`0x48dd10`), appelé d'un seul endroit (`0x49e37d`).

Le même contrôle, appliqué au **message interne**, est plus net : le motif
`c7 40 04 82 04 00 00` (`movl $0x482,0x4(%eax)`) apparaît exactement **3 fois** dans `.text`
(`0x4f8a13`, `0x4f8b16`, `0x4f9446`), et le motif de sa sœur `c7 40 04 84 04 00 00`
(`movl $0x484,0x4(%eax)`, message de `1304`) exactement **2 fois** (`0x5008a3`, `0x500f42`).
Les deux demandes de liste ne peuvent donc pas être confondues par accident.

La même méthode n'est **pas** exploitable pour `1304` : la constante `0x518` apparaît 172 fois
dans le binaire, essentiellement comme déplacement de champ (`8b 86 18 05 00 00`). Pour `1304`, la
preuve utilisée est donc le scan des appels relatifs (§3.3), pas le recensement d'octets.

---

## 4. Gating de version, tranché pour 7.3

rzu porte le gating **sur l'identifiant**, pas sur le champ :

```
TS_CS_AUCTION_SELLING_LIST.h:10-12
    X(1302, version <  EPIC_9_6_3)
    X(2302, version >= EPIC_9_6_3)
```

| question | réponse pour Epic 7.3 | source |
|---|---|---|
| quel identifiant le serveur 7.3 doit-il déclarer ? | **`1302`** | `EPIC_9_6_3 = 0x090603` (`PacketEpics.h:96`) et `EPIC_7_3 = 0x070300` (`PacketEpics.h:59`) : `0x070300 < 0x090603` → branche `version < EPIC_9_6_3` |
| `2302` peut-il être envoyé par un client 7.3 ? | **non** : 2302 n'apparaît que dans la branche `>= EPIC_9_6_3` | idem ; et le client 7.3 écrit `0x516` (mesure §3.1) |
| un champ de la trame est-il conditionné par une version ? | **non** : `page_num` est déclaré sans condition (`:8`) | rzu `TS_CS_AUCTION_SELLING_LIST.h:7-8` |
| la note `// Last tested: EPIC_9_8_1` (`:5`) est-elle un gating ? | **non** : c'est une note de test de rzu, pas une condition de compilation | rzu `TS_CS_AUCTION_SELLING_LIST.h:5` |

**Décision écrite : en 7.3 le paquet est `1302`, sans `2302`, et sa charge utile a la même forme à
toutes les versions déclarées par rzu.** Aucun bit `limit_*` ni champ conditionnel n'entre dans
cette trame, contrairement à `TM_CS_AUCTION_SEARCH` (dont `is_equipable` est gaté
`>= EPIC_7_2`, socle §4.1).

Pour la **réponse** `1303`, le gating de la trame n'apporte rien de neuf (`1303` pour
`< EPIC_9_6_3`, `TS_SC_AUCTION_SELLING_LIST.h:19`) ; en revanche la taille des entrées dépend du
motif d'objet de 75 octets, dont le socle a tranché la composition pour 7.3 (`socle-encheres.md`
§3.7-§3.8 et §4) : entrée = 4 + 75 + 1 + 8 + 8 = **96 octets** (`TS_AUCTION_INFO`) + 1 octet
d'état = **97**, page = 12 + 40 × 97 = 3880, trame = 3899. Ces valeurs ne sont pas re-décidées ici.

---

## 5. Traitement attendu

### 5.1 NGemity ne traite pas ce paquet

Vérifié dans ce réveil : `TS_CS_AUCTION_SELLING_LIST` n'existe chez NGemity que comme
**déclaration**

- `shared/Server/Packets/GameClient/TS_CS_AUCTION_SELLING_LIST.h:6-9` : `_(simple)(int32_t, page_num)`
  et `CREATE_PACKET(TS_CS_AUCTION_SELLING_LIST, 1302)`, sans gating ;
- `shared/Server/ClientPackets.h:211` : `TS_CS_AUCTION_SELLING_LIST = 1302` ;
- aucune occurrence d'un `case` ou d'un handler `TS_CS_AUCTION*` dans `Chihiro/` (recherche
  insensible à la casse dans `Chihiro/src` : **aucun** `TS_CS_AUCTION_SELLING_LIST` hors
  déclarations, aucun `SecRouteAuction` consommé — le seul reste est la constante inutilisée
  `SecRouteAuction = 130107` à `Chihiro/src/Map/WorldLocation.h:50`), et rien d'autre sur les
  enchères que l'identifiant d'annonce persisté d'un objet (`Chihiro/src/Entities/Item/ItemInstance.h:59`).

NGemity compile par ailleurs en Epic 4.1.1 (`shared/Common/Define.h:25` : `#define EPIC
EPIC_4_1_1`) : son code n'est donc pas une référence de comportement pour 7.3, seulement un
témoin d'existence de l'identifiant. **Rien à porter.**

### 5.2 Le lecteur

Modèle du dépôt : un `TryRead…` appelé depuis `GameClient`, comme
`GameActionPackets.TryReadPutonItem` (`GameClient.cs:783`). Pour cette trame :

```
TryReadAuctionSellingList(byte[] packet, out int pageNum)
```

1. refuser si `packet.Length < 11` (le client écrit exactement 11, mesure §3.1) ;
2. `pageNum = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4))` ;
3. **ne lire que ces quatre octets** : la trame ne transporte rien d'autre (§3.2).

Refus : `SendResult((ushort)GamePackets.TM_CS_AUCTION_SELLING_LIST,
(ushort)ResultCode.InvalidArgument)` — jamais une trame `TM_SC_AUCTION_*` d'erreur, qui n'existe
ni dans les références ni dans le client (socle §5.3 ; `GameClient.SendResult`,
`GameClient.cs:68-72`).

### 5.3 Le bras de dispatch

`TM_CS_AUCTION_SELLING_LIST = 1302` doit être déclaré dans `GamePackets` **et** avoir son bras,
ensemble (critère transversal 4). La bande 13xx ne porte aujourd'hui que les trois réponses
(`GamePackets.cs:156-158`) ; aucun `TM_CS_AUCTION_*` n'y est déclaré. Emplacement du bras : avec
les autres `if (header.ID == …)` **au-dessus** du `switch` final, après
`TM_CS_CHECK_ILLEGAL_USER` (`GameClient.cs:1839`) et `TM_CS_XTRAP_CHECK` (`:1848`), donc juste
avant `IPacket msg = header.ID switch` (`:1854`) :

```csharp
if (header.ID == (ushort)GamePackets.TM_CS_AUCTION_SELLING_LIST)
{
    HandleAuctionSellingList(msgBuffer);
    continue;
}
```

Un membre de `GamePackets` qui atteindrait le `switch` final tomberait dans
`_ => throw new Exception($"Unknown Packet Type {header.ID}")` (`GameClient.cs:1865`) et casserait
la boucle de réception : c'est la raison d'être du critère. Le commentaire de `GamePackets.cs:74-77`
(**ne pas y toucher**) rappelle que 1304 est, en 7.3, `TM_CS_AUCTION_BIDDED_LIST`.

### 5.4 La réponse : `1303`, **3899 octets**, page construite par le socle

`Connection.Send(GameAuctionPackets.BuildAuctionSellingList(pageNum, totalPageCount, entries))`
(modèle d'envoi : `GameClient.cs:99`).

- signature **déjà livrée par le socle** : `BuildAuctionSellingList(int pageNum, int totalPageCount,
  IReadOnlyList<RegisteredAuctionInfo> entries = null)` (`GameAuctionPackets.cs:95-111`) ; elle
  produit **3899 octets** (`ListPacketSize`, `:66`), écrit `page_num` à 7, `total_page_count` à 11,
  le compte à 15 (plafonné à 40, `WritePageHeader` `:136-143`), écrit `status` à l'octet 96 de
  chaque entrée (`:106`) et **laisse les 40 emplacements de 97 octets toujours écrits**, vides ou
  non ;
- `RegisteredAuctionInfo(AuctionInfo Auction, byte Status)` (`GameAuctionPackets.cs:31`) est
  `TS_REGISTERED_AUCTION_INFO` de rzu (`TS_SC_AUCTION_SELLING_LIST.h:6-9`) ; le motif d'objet de
  75 octets vient de `ItemFixedInfoWriter` (`:149`), jamais à réécrire ;
- `page_num` est l'**écho** de la demande (socle §5.3) ;
- `total_page_count` est un paramètre : sa règle n'est pas établie (socle §8.6, §7 question 2).

**Pourquoi 3899 est la seule taille acceptable** (mesure du réveil sur le handler client `1303`) :

```
0x6706d0  push %ebp / mov %esp,%ebp / push %ebx,%esi,%edi
0x6706d6  push $0xf47            ; 3911 = 0x1f (en-tête d'objet) + 3880 (table)
0x6706dd  call 0x97671b          ; operator new(3911)
0x6706eb  call 0x66c780          ; constructeur de l'objet
0x6706f4  mov 0x8(%ebp),%ecx     ; la trame reçue
0x6706f7  mov 0x7(%ecx),%edx  →  mov %edx,0x13(%eax)   ; page_num
0x6706fd  mov 0xb(%ecx),%edx  →  mov %edx,0x17(%eax)   ; total_page_count
0x670703  mov 0xf(%ecx),%edx  →  mov %edx,0x1b(%eax)   ; auction_info_count
0x670706  lea 0x13(%ecx),%esi    ; table = trame + 19
0x67070c  lea 0x1f(%eax),%edi    ; destination = objet + 31
0x670712  mov $0x3ca,%ecx        ; 970 dwords
0x67071a  rep movsl              ; copie inconditionnelle de 3880 octets
```

Le client **ne vérifie pas** `Length` ni `auction_info_count` avant de copier : une trame plus
courte que 3899 fait lire 3880 octets hors de la charge utile et désaligne l'affichage sans erreur
visible (même constat que le socle §5.2 pour `1301`, et que la fiche 1300 §5.3). Noter aussi que ce
handler **ne lit jamais l'octet `status`** : la correspondance valeur → texte se fait dans
l'affichage (ressources d'interface), pas dans le binaire — c'est ce qui rend la question
« sémantique de `status` » (§7) non tranchable par lecture du client.

### 5.5 « Annonce du personnage » — ce qui est prouvable, et ce qui ne l'est pas

C'est la décision restante de la carte (§6.3, rang 2). Voici la frontière, explicitement.

**Prouvé par la trame elle-même.** L'entrée de `1303` décrit une annonce par
`TS_AUCTION_INFO` + un octet d'état, et `TS_AUCTION_INFO` **ne porte aucun vendeur** —
`auction_uid`, `item_info`, `duration_type`, `bidded_price`, `instant_purchase_price`
(`TS_SC_AUCTION_SEARCH.h:8-14`) — alors que l'entrée de la recherche `1301` porte, elle, un
`seller_name` de 31 octets (`TS_SC_AUCTION_SEARCH.h:17-21`). Une liste qui **ne peut nommer aucun
vendeur** ne peut être qu'une liste dont le vendeur est le demandeur. Donc : *les annonces servies
par `1303` sont celles du personnage qui demande* — c'est une propriété structurelle de la trame,
pas un choix de politique, et elle ne laisse aucune place à « les annonces de quelqu'un d'autre ».

**Prouvé par le modèle du dépôt.** `AuctionEntity.SellerId` (`AuctionEntity.cs:10`, `long`, avec la
navigation `Seller`) est l'unique colonne de vendeur de l'annonce, et
`ConnectionInfo.CharacterHandle` (`ConnectionInfo.cs:30`, `uint`) **est** l'identifiant du
personnage en base : `GameActions.cs:114` écrit `info.CharacterHandle = (uint)character.Id;`. Le
prédicat `a.SellerId == ConnectionInfo.CharacterHandle` est donc **exprimable sans inventer de
correspondance d'identité** — ce qui n'est pas la même chose que « la liste s'arrête là ».

**Prouvé par l'absence de données.** Comme pour `1300` (fiche 1300 §5.4), aucun code du dépôt
n'écrit la table des annonces : `TelecasterContext.Auctions` n'est déclaré qu'une fois
(`Game/DataAccess/Contexts/TelecasterContext.cs:11`, configuration à `:146`), aucun
`Auctions.Add`/`Update`/`Remove` n'existe dans `Game/`, et la seule autre mention hors `DataAccess`
est la garde de rangement `StorageRules.cs:107`. **Aucune annonce ne peut donc exister** : la page
servie aujourd'hui est nécessairement vide, et c'est vérifiable en test.

**Ce qui reste une décision de Killian** (jamais à trancher par le dev) :

1. la liste est-elle « toutes mes annonces » ou « mes annonces encore ouvertes » — c'est-à-dire
   celles des quatre fins d'annonce du modèle qui restent visibles ;
2. l'ordre des entrées dans une page ;
3. la règle de `total_page_count` et un éventuel plafond (le 40 est imposé par la trame, pas par le
   jeu) ;
4. la sémantique de l'octet `status` (socle §8.2), qui est aussi le seul endroit où le modèle
   pourrait signaler une fin d'annonce : `ItemStorageEntity.RelatedAuctionId`
   (`ItemStorageEntity.cs:20`) et `StorageType` (`Game/DataAccess/Entities/Enums/StorageType.cs:7-10` :
   `ItemBySuccessfulBid = 1`, `ItemByInstantPurchase = 2`, `ItemByExpiration = 3`,
   `ItemByCancel = 4`) ;
5. le filtre `IsHiddenVillageOnly` (`AuctionEntity.cs:19`, socle §8.8).

### 5.6 Ce que le lot doit livrer, et le cas « pas de code »

Le « cas pas de code » **ne s'impose pas** : la trame est entièrement mesurée (§3), la réponse est
entièrement spécifiée et déjà écrite par le socle (§5.4), et l'absence d'écrivain d'annonce (§5.5)
rend la page vide exécutable et testable. Le lot se limite donc à :

1. `GamePackets.cs` : `TM_CS_AUCTION_SELLING_LIST = 1302`, dans la bande 13xx (à côté des trois
   réponses, `:156-158`), avec un commentaire d'une ligne renvoyant à cette fiche ; ne pas toucher
   au commentaire `:74-77` ;
2. `GameAuctionPackets.cs` : le `TryReadAuctionSellingList` de §5.2 (taille 11, `page_num` à 7) ;
3. `GameClient.cs` : le bras `if (… TM_CS_AUCTION_SELLING_LIST)` de §5.3 et son
   `HandleAuctionSellingList`, qui lit, journalise la page reçue et envoie
   `BuildAuctionSellingList(pageNum, 0, null)` — page vide, `total_page_count = 0` **à titre de
   valeur d'attente explicite** (la règle n'est pas établie, §7 question 2), écho de `page_num` ;
4. `Tests/` : au moins un test d'**offsets** du nouveau paquet — taille totale **11**, position de
   chaque champ (0/4/6/7), refus sur trame de 10 octets, écho de `page_num` et taille **3899** de
   la réponse. Les tests existants du socle (`Tests/Game/AuctionPacketsTests.cs` : 13 `[Test]`,
   14 attributs avec le `[TestFixture]` de la classe à `:24`) ne se réécrivent pas.

La requête d'annonces elle-même (`SellerId == CharacterHandle`, §5.5) **n'est pas** du périmètre :
elle n'a rien à lire aujourd'hui, et les points 1 à 5 de §5.5 sont des décisions. Elle appartient à
la carte suivante, quand `1309` (dépôt d'annonce) existera.

Le dev n'écrit ni `CLAUDE.md` (protégé par Hermes) ni `op_codes.md` ; le bloc destiné à `CLAUDE.md`
est en §10 et doit figurer dans la description de la MR.

---

## 6. Écarts assumés avec NGemity

NGemity **ne traite aucun paquet d'enchère** (§5.1) : sa déclaration de `1302` est identique à
celle de rzu, mais elle n'est suivie d'aucun code, et sa cible de compilation est Epic 4.1.1
(`shared/Common/Define.h:25`). Deux écarts sont donc assumés :

1. **La composition de la trame** suit le client 7.3 (11 octets, un `page_num` en 32 bits à
   l'offset 7, mesure §3.1) et non NGemity : NGemity compile la même déclaration sans gating, mais
   comme elle ne prouve rien du comportement attendu, elle ne tranche rien. Il n'y a rien à porter.
2. **La réponse existe** — `1303`, 3899 octets, page vide — alors que NGemity n'a aucune réponse du
   tout. C'est la mesure cliente (le handler `1303` copie 3880 octets sans regarder `Length`, §5.4)
   qui l'impose.

Un écart de **nom** est conservé tel quel : rzu nomme l'entrée de `1303`
`TS_REGISTERED_AUCTION_INFO` et y met `auction_info` (le motif de 96 octets), NGemity la nomme
`TS_REGISTERED_AUCTION_INFO` avec `status` (`TS_SC_AUCTION_SELLING_LIST.h:7-10`) — le dépôt suit
rzu (`GameAuctionPackets.cs:31`) et ne renomme rien.

---

## 7. NON ÉTABLI

Formulés comme des questions à trancher, jamais comme des valeurs. **Aucun de ces points n'est
implémentable sans décision** : aucun n'est deviné dans cette fiche.

1. **Valeurs du `status`** (octet 96 de chaque entrée de 1303). Le client reçoit l'octet et
   l'affiche par la légende `deposit_status` (socle §8.2), mais son handler de `1303` (`0x6706d0`)
   ne le lit jamais : la table valeur → texte est dans les ressources d'interface, hors du binaire.
   Piste disponible et **non concluante** : le format de fichier du serveur officiel, tel que rzu
   le lit, porte un `bid_flag` (`int8_t`, `rzauctionmonitor/lib/AuctionFile.h:23`) et un `deleted`
   (`bool`, `:24`) à côté de `duration_type` (`:19`), `bid_price` (`:20`), `price` (`:21`) et
   `seller` (`:22`) — même largeur que l'octet de la trame, mais **aucune source n'énonce la
   correspondance**. *Question : quelle valeur pour « en vente », « vendue », « expirée »,
   « retirée », et les deux listes 1303/1305 partagent-elles une énumération ?*
2. **`total_page_count`** : la règle (40 par page) et un éventuel plafond ne figurent nulle part
   dans le client, qui affiche le nombre reçu. *Question : combien de pages pour N annonces, et
   existe-t-il un maximum d'annonces par personnage ?* (socle §8.6.)
3. **`page_num` hors bornes** : que doit répondre le serveur si `page_num` vaut `0`, un nombre
   négatif ou une page au-delà de la dernière ? Le client établit seulement que sa propre
   navigation commence à `1` (§2) ; rien ne prouve qu'il existe un chemin de refus. *Question :
   refus `InvalidArgument`, ou page vide avec écho ?*
4. **Annonces visibles** : parmi les quatre fins d'annonce du modèle
   (`StorageType.ItemBySuccessfulBid`, `ItemByInstantPurchase`, `ItemByExpiration`, `ItemByCancel`,
   `StorageType.cs:7-10`), lesquelles restent listées par `1303` ? *Question : la liste est-elle
   « mes ventes en cours » ou « mes ventes, tous états » ?* (§5.5, point 1.)
5. **Ordre des entrées** dans une page. *Question : par date de fin croissante, par date de dépôt,
   par identifiant ?*
6. **`IsHiddenVillageOnly`** (`AuctionEntity.cs:19`, socle §8.8) : filtre-t-il la liste des ventes,
   et selon le village du personnage ou une option de dépôt ?
7. **Longueur exacte acceptée** : le client écrit **11** octets (§3.1) ; faut-il refuser `> 11`
   (trame de longueur inattendue) ou seulement `< 11` ? La fiche 1300 a laissé la même question
   ouverte pour sa propre trame (§8) : à trancher une fois pour les deux, pas deux fois
   différemment.
8. **Ce que fait le client d'un `TS_SC_RESULT` portant l'identifiant `1302`** : le bloc client
   `0x4fb820` teste `[objet+0x13] == 0x516` (1302) puis `[objet+0x15] == 1` dans un cas d'un
   aiguillage sur `[objet+4]` (cas `4`, `0x425`, `0x426`, `0x483`, `0x498`). La lecture de cet
   objet (trame reçue ou message interne) n'est **pas** établie ici, et je ne l'affirme pas :
   *question : un refus notifié par `TS_SC_RESULT(1302, code)` produit-il un affichage côté client,
   et lequel ?* La règle de refus du socle (§5.3 : toujours `SendResult` avec l'id de la demande)
   n'en dépend pas, mais la valeur du code à employer, si.
9. **Valeurs des deux prix** pour une annonce de la liste des ventes : `bidded_price` d'une annonce
   sans enchère, et `instant_purchase_price` d'une annonce sans achat immédiat. Le modèle du dépôt
   ne départage pas `0` d'un prix de départ (`AuctionEntity.cs:21`, `:24`). Sans objet tant que
   §5.5 tient (aucune annonce n'existe), bloquant dès la première annonce.
10. **`appearance_code` du motif d'objet** (socle §8.3) : la question est héritée telle quelle, la
    réponse `1303` embarquant le même motif de 75 octets que `1301`.

---

## 8. A VERIFIER PAR KILLIAN

| # | décision | ce que le lot fait en attendant |
|---|---|---|
| q1 | valeurs de `status` (§7.1) | rien : aucune entrée n'est servie |
| q2 | règle de `total_page_count`, plafond (§7.2) | `0` (liste vide), paramètre nommé dans le code |
| q3 | `page_num` hors bornes (§7.3) | écho de la page reçue, page vide, aucun refus |
| q4 | annonces visibles (§7.4) | aucune requête d'annonces : la carte suivante en dépend |
| q5 | ordre des entrées (§7.5) | sans objet (page vide) |
| q6 | `IsHiddenVillageOnly` (§7.6) | sans objet (page vide) |
| q7 | refus de `Length > 11` (§7.7) | `< 11` refusé, `> 11` accepté (cohérent avec 1300) |
| q8 | effet d'un `TS_SC_RESULT` id `1302` (§7.8) | aucun code : le refus reste `InvalidArgument` |
| q9 | valeurs des deux prix (§7.9) | sans objet (page vide) |
| q10 | `appearance_code` (§7.10) | sans objet (page vide) |

Aucune de ces lignes ne bloque le lot : la trame, le lecteur, le bras de dispatch, la réponse vide
de 3899 octets et les tests d'offsets sont déterministes et vérifiables sans elles. L'état livré par
le dev, et ce qu'il a effectivement fait de chaque ligne, est au §14.

---

## 9. Commits et binaires épinglés

Empreintes **relues dans ce réveil** (aucune reprise de confiance sur le socle) :

| référence | empreinte | où |
|---|---|---|
| rzu (`librzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02, « packets: fix TS_SC_INVENTORY with older epics ») | `git -C reference/rzu rev-parse HEAD` |
| NGemity / `Chihiro` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03, « Fix compilation issue for GCC ») | `git -C reference/ngemity rev-parse HEAD` |
| client Epic 7.3 | `reference/client73/SFrame.exe`, `sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | `sha256sum reference/client73/SFrame.exe` |
| ressource de catégories | `reference/client73/db_auctioncategoryresource.rdb`, `sha256 = ca4dad3ccbd482275d3de3f62f4ed9fad1d2e966d2f525c4c5161aa8291fb415` | `sha256sum` |
| Navislamia, base du réveil | `origin/master = b56967a07430422add88e0e5cdf292b41b18f6c6` | `git rev-parse origin/master` |

`reference/client73/` **n'est pas un dépôt Git** : aucune révision n'est citable, seule l'empreinte
du binaire est opposable (c'est déjà la règle du socle §11). Les fichiers de référence rzu et
NGemity sont cités par *chemin:ligne* sur ces deux commits.

---

## 10. Bloc destiné à `CLAUDE.md`

```markdown
#### Enchères — `TM_CS_AUCTION_SELLING_LIST` (1302)

Demande de la liste des ventes du personnage : **11 octets**, en-tête de 7 (Length/Id/Checksum)
puis un seul `int32 page_num` à l'offset 7. Gating rzu : `1302` pour `version < EPIC_9_6_3`
(trancher 7.3 : `EPIC_7_3 = 0x070300 < 0x090603`), `2302` sinon, jamais en 7.3. La trame est
1-based (`page_num = 1` au premier envoi) et le client ne la reçoit jamais.

La réponse est **1303**, et elle fait **3899 octets** (7 + 12 + 40 × 97) : le client copie 3880
octets sans vérifier `Length`, une trame courte désaligne l'affichage en silence. Entrée = 96
octets (`TS_AUCTION_INFO`) + 1 octet `status`, construite par
`GameAuctionPackets.BuildAuctionSellingList(pageNum, totalPageCount, entries)`.

L'entrée 1303 ne porte **aucun vendeur** (contrairement à 1301, qui porte `seller_name`) : la liste
est donc celle du personnage qui demande. `ConnectionInfo.CharacterHandle` est l'id du personnage
en base (`GameActions.cs:114`). Restent des décisions : valeurs de `status`, règle de
`total_page_count`, visibilité des annonces terminées, ordre, `IsHiddenVillageOnly`,
`page_num` hors bornes. Fiche : `docs/packet-specs/1302-auction-selling-list.md`.
```

---

## 11. Références croisées

- socle : `docs/packet-specs/socle-encheres.md` §1 (identité et sens), §2 (fenêtres et messages
  internes), §3.3 (les deux trames de 11 octets), §3.9 (réponses `1303`/`1305`), §5.2 (les 40
  emplacements), §5.3 (réponses et refus), §6.3 (rang 2 du découpage), §8.2 (sémantique de
  `status`), §8.6 (`total_page_count`), §8.8 (`IsHiddenVillageOnly`) ;
- fiche sœur : `docs/packet-specs/1300-auction-search.md` (même famille, même méthode de mesure,
  même question ouverte sur la longueur exacte acceptée) ;
- dépôt : `GamePackets.cs:74-77` (commentaire à ne pas modifier : 1304 en 7.3),
  `GameAuctionPackets.cs:95-111` (constructeur de la réponse), `GameClient.cs:1854-1865`
  (emplacement du bras et `throw` final).

---

## 12. Ce que cette fiche resserre ou corrige dans le socle

Rien de faux dans le socle : les points suivants le **précisent**, et deux formules peuvent se lire
de deux façons.

1. **« Constructeurs » et « points d'entrée ».** Le socle §3.3 écrit « constructeurs `0x48dd10` et
   `0x48dda0` appelés depuis le dispatch interne `0x49e37d` / `0x49e38a` », et cite par ailleurs
   `0x48dd53` (1302) et `0x48dde3` (1304) comme les écritures de `page_num`. La mesure confirme
   les quatre adresses : `0x48dd10`/`0x48dda0` **construisent et envoient** dans la même fonction
   (il n'y a pas de sender distinct, contrairement à `1300`), les appels de l'aiguillage sont bien
   `0x49e37d`/`0x49e38a` (les *stubs* commencent 3 octets plus haut, à `0x49e37a`/`0x49e387`), et
   `0x48dd53`/`0x48dde3` sont les instructions qui écrivent l'offset 7. Chacun de ces deux
   points d'entrée n'a **qu'un seul** appelant dans tout `.text` (scan `E8`/`E9`).
2. **Le lien message interne ↔ identifiant de paquet.** Le socle nomme les messages internes par
   leur RTTI et, séparément, donne les clés d'aiguillage. La mesure les relie : les trois sites
   d'émission écrivent `0x482` dans `[objet+4]` **et** la vtable `0xa25730` dont le COL
   (`0xbbd2f0`) nomme `.?AUSIMSG_REQ_AUCTION_SELLING_LIST@@`. L'identifiant 1154 du message et la
   trame 1302 sont donc le même objet, prouvé deux fois.
3. **« Ouvre l'onglet et pagine »** (socle §2). La mesure donne le détail : sur les trois sites
   d'émission, deux écrivent explicitement `page_num = 1` (l'ouverture de l'onglet) et le
   troisième prend la page en argument (la pagination), les trois postant le message avec le même
   `push $0x3e8` (1000). La page est donc **1-based**, ce que le socle n'énonçait pas.
4. **Taille de la réponse.** Le socle a mesuré `1301` (`0x67e6a5` → `0x670660`) ; cette fiche
   re-mesure `1303` indépendamment (`0x6706d0` : `operator new(0xf47)`, `rep movsl` de 970 dwords
   = 3880 octets, en-tête de page lu aux offsets 7/11/15) et retrouve **3899**. Elle ajoute que ce
   handler **ne lit jamais l'octet `status`**, ce qui ferme la porte à toute lecture du binaire
   pour la question de §7.1.
5. **Recensement de constante.** Le socle §1 a établi l'absence de `1307` par recensement des
   octets `17 05 00 00`. La même méthode, appliquée à `0x516`, donne 9 occurrences dont une seule
   écriture d'`Id` (§3.4) ; appliquée à `0x518` elle est inutilisable (172 occurrences, des
   déplacements de champ), et c'est le scan des appels relatifs qui tranche.
6. **Piège de conversion d'adresse.** Les chaînes `.?AUSIMSG_*` citées par le socle vivent dans
   `.data` (delta VA↔fichier `0x401E00`), pas dans `.rdata` (`0x401600`) : `.?AUSIMSG_REQ_AUCTION_SELLING_LIST@@`
   est à l'offset de fichier `0x812a78`, soit la VA `0xc14878` — l'adresse du socle est juste, mais
   un décalage de section donne `0xc14078` (faux) et le contrôle doit être refait à chaque lecture.

---

## 13. Vérifications exécutées

- **Dépôt, base du réveil** : `git rev-parse origin/master` = `b56967a07430422add88e0e5cdf292b41b18f6c6` ;
  arbre propre avant changement de branche ; `git log --oneline origin/master..master` **vide**
  (aucun commit sur `master`) ; travail sur `hermes/packet-1302-auction-selling-list`, créée depuis
  `origin/master`.
- **Build** : `NUGET_PACKAGES=/srv/navislamia/.nuget-cache dotnet build Navislamia.sln -c Debug` →
  **code de sortie 0** (0 erreur).
- **Tests** : `dotnet test Tests/Tests.csproj` → **code de sortie 0**, `Passed: 1302`,
  `Failed: 0`, `Skipped: 0` (base de ce réveil, avant tout code du lot).
- **Client** : lectures statiques uniquement — `objdump` sur `SFrame.exe`, `strings -t x`, et deux
  scripts Python de lecture d'octets (recensement de constantes, résolution des COL RTTI par la
  table des sections PE). Aucun `SFrame.exe`, aucun Lua, aucun script du client n'a été exécuté ;
  aucun serveur de jeu et aucune base de données n'ont été démarrés (le VPS n'en a pas).
- **NGemity** : recherche insensible à la casse de `auction` dans `Chihiro/src` (aucun handler),
  et de `TS_CS_AUCTION`/`TS_SC_AUCTION` dans `Chihiro/` + `shared/` (déclarations seulement).
- **Reproductibilité** : tous les points d'entrée et toutes les adresses de table cités ici sont
  donnés en VA, tels que `objdump` les affiche pour ce binaire ; l'empreinte du binaire (§9) est
  ce qui rend ces adresses opposables.

---

## 14. Implémentation livrée (dev)

Section ajoutée par `navis-dev` le 27/09/2026 ; l'analyse de l'archéologue (§1 à §13) est laissée
intacte, à l'exception du renvoi d'une ligne ajouté en §8. Branche
`hermes/packet-1302-auction-selling-list`, créée par `navis-ref` depuis `master`
(`b56967a07430422add88e0e5cdf292b41b18f6c6`) : fiche `f53ec51`, code `1fb6773` (3 fichiers,
+87 lignes), tests `e039806` (1 fichier, +300 lignes), puis `2d89f58` (commentaires de code : la
fusion de §14.6, mesurée après coup, y est consignée).

**Règle tenue par le code : lire les onze octets, écho de `page_num`, réponse 1303 vide de 3899
octets, aucune requête sur `AuctionEntity`, aucune politique inventée.**

### 14.1 Checklist des critères transversaux, avec les codes de sortie relevés

| # | Critère | État | Mesure |
|---|---|---|---|
| 1 | `dotnet build Navislamia.sln -c Debug` code 0 | **OK** | code de sortie **0**, `0 Error(s)`, `164 Warning(s)` (toutes préexistantes : `MigrateDatabase`, nullabilité, fixtures) |
| 2 | `dotnet test Tests/Tests.csproj` code 0, compte jamais en baisse | **OK** | base avant le lot, mesurée dans ce réveil : code **0**, **1302** réussis / 1302. Après : code **0**, **1317** réussis / 1317, 0 échec, 0 ignoré → **+15** |
| 3 | Au moins un test d'offsets (taille totale + position de chaque champ) | **OK** | `Tests/Game/AuctionSellingListPacketsTests.cs` : `Request_IsElevenBytesWithTheMeasuredOffsets` (11 octets, `page_num` en 7), `Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame` (octets littéraux du client, `NotBe` big-endian sur le seul champ utile), `TryReadAuctionSellingList_ReadsThePageNumAsSigned`, `…_ReadsAPaddedFrameAndLeavesThePaddingAlone` |
| 4 | Enum et dispatch modifiés ensemble | **OK** | membre `TM_CS_AUCTION_SELLING_LIST = 1302` (`GamePackets.cs:164`, entre 1301 et 1303) **et** bras `GameClient.cs:1450` → `HandleAuctionSellingList` (`:384`), avant le `switch` final dont le `_` lève `Unknown Packet Type`. Tenus par **exécution** (`SellingListRequest_IsConsumedByTheReceiveLoopWithoutThrowing`) et par mutation (§14.5, mutant A) |
| 5 | Savoir durable dans la fiche commitée + bloc `CLAUDE.md` dans la description de la MR | **OK côté fiche** | présente section + §10 remis à `navis-qa` pour la description de la MR (le dev n'écrit pas `CLAUDE.md`) |
| 6 | Version tranchée | **OK** | 1302 est l'id 7.3 ; `1304` et `1306` ne sont **pas** déclarés (`Id_IsTheEpic73OneAndTheFamilyStaysOnTheLowBranch` le vérifie par `Enum.IsDefined`), pas de variante 2302 ; aucun champ gated : `page_num` n'a aucun gating par version chez rzu (§4) |
| 7 | Aucun commit sur `master` locale | **OK** | `git log --oneline origin/master..master` → aucune ligne (§14.9) |
| 8 | Aucun champ `NON ÉTABLI` deviné | **OK** | aucun `SELECT` sur `AuctionEntity`, `total_page_count = 0`, page écho, `status`/prix/`appearance_code` jamais renseignés : les dix questions de §7 restent ouvertes en §8, et le code ne fait que ce que §5.5/§5.6 autorisent (page vide) |

### 14.2 Fichiers livrés

| Fichier | Modification |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_AUCTION_SELLING_LIST = 1302` (`:158-164`) : la trame, l'émetteur client, le fait que le client 7.3 ne le reçoit jamais, et le rappel que 1300/1304 sont autres |
| `Game/Network/Packets/Game/GameAuctionPackets.cs` | `SellingListRequestPageNumOffset` (`:136`), `SellingListRequestSize` (`:142`), `TryReadAuctionSellingList` (`:155-166`) |
| `Game/Network/Clients/GameClient.cs` | `HandleAuctionSellingList(byte[])` (`:384-399`) et son bras de dispatch (`:1445-1454`) |
| `Tests/Game/AuctionSellingListPacketsTests.cs` | 15 tests (nouveau, 300 lignes) |

Aucun constructeur de trame n'est ajouté : `BuildAuctionSellingList(pageNum, 0)` existe dans le
socle et est appelé tel quel. `GameAuctionPackets.cs` n'est modifié qu'après les trois constructeurs
(voir §14.6).

### 14.3 Offsets livrés, et les tests qui les tiennent

| Offset | Taille | Champ | Valeur lue / écrite | Test |
|---|---|---|---|---|
| 0 | 4 | `Length` `uint32` LE | **11** (`0xB`) | `Request_IsElevenBytesWithTheMeasuredOffsets`, `Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame` |
| 4 | 2 | `ID` `uint16` LE | **1302** (`0x0516`) | les deux précédents + `Id_IsTheEpic73OneAndTheFamilyStaysOnTheLowBranch` |
| 6 | 1 | `Checksum` | somme des octets 0-5 (charge exclue, comme la famille 1300) | `Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame` (checksum `0x26` reconstruit sur la trame littérale) |
| 7 | 4 | `page_num` `int32` LE | lu tel quel, écho vers 1303 | `TryReadAuctionSellingList_HandsBackThePageNum` (6), `…_ReadsTheFirstPageTheClientWrites` (1, écrit en dur par le client), `…_ReadsAPaddedFrameAndLeavesThePaddingAlone` (4), `…_ReadsThePageNumAsSigned` (`-1`) |
| 11 | — | fin de trame | — | `Request_IsElevenBytesWithTheMeasuredOffsets`, `Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame`, `…_ReadsAPaddedFrameAndLeavesThePaddingAlone` |

Le lecteur **accepte AVAL** toute longueur `>= 11` et **refuse** en dessous (trois cas : 0, 7, 10).
C'est la frontière de §7.7 : `> 11` est toléré (le client n'en produit jamais), `< 11` ne peut pas
être lu sans inventer un alignement. Une trame refusée est consommée en totalité et ne désynchronise
pas la suivante (test de coalescence). Une trame refusée reçoit `TS_SC_RESULT` id `1302`
`InvalidArgument` — convention du socle §5.3, appliquée comme 1300.

### 14.4 Réponses émises : une seule, la page vide du socle

`SellingListRequest_AnswersAnEmptyPageEchoingPageNum` exécute la vraie boucle de réception (page 5) et
vérifie sur l'unique trame émise : **3899 octets** (`7 + 12 + 40 × 97`), `Length = 3899`, `ID = 1303`,
checksum valide, `page_num` écho de la demande, `total_page_count = 0`, `auction_info_count = 0`,
**table intégralement nulle**, la dernière entrée finissant exactement sur le dernier octet.
`SellingListRequest_AnswersThePageTheClientAskedForAndNotAFixedOne` refait le trajet avec
`page_num = 1` puis `= 3` — et vérifie que les quatre octets de page des deux réponses **diffèrent** —
pour montrer que la page n'est pas figée dans le code, la trame d'ouverture du client valant 1.
`SellingListRequest_MalformedFrameIsRefusedWithTheFamilyResult` vérifie qu'une trame de 10 octets
produit un `TS_SC_RESULT` de 15 octets (`id = 1302`, `InvalidArgument`) et **aucune** trame 1303 —
aucune variante d'erreur de 1303 n'existe chez rzu, NGemity ou le client (§5.2).

### 14.5 Preuve par mutation (trois mutants, chacun tué par 5 tests sur 15)

| Mutant | Fichier | Effet mesuré |
|---|---|---|
| A — le bras devient inatteignable (condition préfixée par `header.ID == (ushort)GamePackets.TM_NONE`) | `GameClient.cs:1450` | **5 échecs**, dont le piège documenté : `System.Exception : Unknown Packet Type 1302` — c'est l'invariant « enum et dispatch ensemble » rendu visible |
| B — garde du lecteur décalée (`<=` au lieu de `<`) | `GameAuctionPackets.cs:157` | **5 échecs** (`page_num` non écho, réponse de 15 octets au lieu de 3899) |
| C — endianness inversée (`ReadInt32BigEndian`) | `GameAuctionPackets.cs:163` | **5 échecs** (pages 16777216, 83886080…), ce qui rend la `NotBe` big-endian du test d'offsets opposable |

Après chaque mutant : `git checkout -- <fichier>` puis `git diff --stat` **vide**, et la passe finale
complète redonne **1317 / 1317**. Les mutants n'ont pas été commités.

### 14.6 Fusion avec la branche sœur `hermes/packet-1300-auction-search` (MR #65) — mesurée sans conflit

```
git merge-tree --write-tree hermes/packet-1300-auction-search HEAD
→ code de sortie 0, arbre fusionné de64e2eff686e04d1775b892305651f2f9078f2f, aucun chemin en conflit
```

Dans l'arbre fusionné, les deux apports coexistent : l'énumération s'ordonne `1300, 1301, 1302,
1303, 1305` ; `TryReadAuctionSearch` et `TryReadAuctionSellingList` vivent dans le même
`GameAuctionPackets.cs` ; `HandleAuctionSearch` et `HandleAuctionSellingList` et **les deux bras** sont
présents dans `GameClient.cs`. Ce résultat vient des ancrages retenus, choisis pour ne toucher aucune
ligne de la sœur : les constantes et le lecteur 1302 sont après les trois constructeurs (la sœur
ajoute les siens après `SellerNameSize`), le bras 1302 est posé avec les réponses de la famille et
**non** juste au-dessus du `switch` final où la sœur pose le sien, et le fichier de test
(`AuctionSellingListPacketsTests.cs`) est distinct de `AuctionSearchPacketsTests.cs`.
Si Killian fusionne d'abord la 1300 et rebase la 1302, il n'y a donc rien à résoudre ; dans l'ordre
inverse non plus.

### 14.7 Invariant « enum et dispatch ensemble », mesuré sur le dépôt, pas seulement sur le lot

Script de comptage (`git grep` de chaque membre de `GamePackets` hors de l'énumération, sur l'arbre
du commit) : `master` → **140** membres, **2** sans aucune référence (`TM_SC_CHAT_RESULT`,
`TM_SC_ITEM_COOL_TIME` : dette héritée, paquets S→C). Branche 1302 → **141** membres, **les mêmes 2**
seulement : le nouveau membre 1302 est référencé par son bras. L'autre moitié du piège — un membre
déclaré dont le bras ne se déclenche jamais — n'est pas visible par ce comptage statique ; c'est le
mutant A (§14.5) qui la rend opposable, par l'exception `Unknown Packet Type 1302` levée en exécution.

### 14.8 Réserves du dev

1. **`Length > 11` est toléré en lecture**, comme pour 1300 : c'est la frontière de §7.7 (question q7),
   pas une décision métier. Un client 7.3 n'en produit jamais.
2. **Le refus émet `InvalidArgument`** parce que §5.2 et le socle §5.3 le prescrivent (toute demande
   reçoit un résultat portant l'id de la demande) : ce n'est pas un code choisi par le dev. La
   réaction du client 7.3 à un `TS_SC_RESULT` id `1302` reste inconnue (q8).
3. **Signature** : `TryReadAuctionSellingList(ReadOnlySpan<byte>, out int)` suit la convention du
   dépôt et de la sœur 1300, plutôt que le `byte[]` de §5.2 ; l'appel décrit en §5.2 est inchangé
   (`byte[]` s'y convertit implicitement). Le paramètre de sortie est le seul champ de la trame.
4. **`total_page_count = 0`** est passé en argument nommé, pas calculé : si Killian tranche q2, c'est
   une ligne à changer, pas une requête à écrire. De même, la page vide n'est pas une affirmation sur
   l'état de `AuctionEntity` : c'est le constat que rien dans ce dépôt n'écrit `Auctions` (§5.5).
5. **Rien n'a été exécuté côté client** : la trame de 11 octets vient du désassemblage de
   `SFrame.exe` (§3.1, §9), la vérification en jeu (ouvrir l'onglet « en vente », puis la page
   suivante) reste la seule qui ferme q2/q3/q7/q8.

### 14.9 Commandes relevées

```
dotnet build Navislamia.sln -c Debug       → code 0, 0 Error(s), 164 Warning(s) (préexistantes)
dotnet test Tests/Tests.csproj             → code 0, 1317 réussis / 1317, 0 échec, 0 ignoré
                                             (base avant le lot : code 0, 1302 / 1302)
git log --oneline origin/master..master    → aucune ligne
git merge-tree --write-tree hermes/packet-1300-auction-search HEAD → code 0, 0 conflit
```

### 14.10 Bloc `CLAUDE.md` : le complément qu'ajoute le dev

Le bloc de §10 est conservé tel quel. Le paragraphe suivant vient s'y ajouter (dernier paragraphe,
avant la phrase « Fiche : … » ou après elle), pour que `CLAUDE.md` décrive l'état **livré** et pas
seulement l'état attendu :

```markdown
Traitement livré : le bras de dispatch lit les 11 octets, journalise `page_num` en `Debug` et répond
`GameAuctionPackets.BuildAuctionSellingList(page_num, 0)` — page vide de 3899 octets,
`total_page_count = 0` tant que la règle n'est pas tranchée. Une trame de moins de 11 octets est
consommée puis refusée par un `TS_SC_RESULT` (`id = 1302`, `InvalidArgument`) ; aucune variante
d'erreur de 1303 n'existe. Aucune requête n'est faite sur `AuctionEntity` : rien dans le dépôt n'écrit
`TelecasterContext.Auctions`, donc la page est vide par construction, pas par décision. Une trame plus
longue que 11 octets est lue sans être refusée (question ouverte §7.7). Rappel du piège : un membre de
`GamePackets` sans bras atteint le `throw` « Unknown Packet Type » et casse la boucle de réception —
énumération et dispatch se modifient ensemble.
```


