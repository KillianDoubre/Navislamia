# `TM_CS_AUCTION_BIDDED_LIST` (1304) — fiche de paquet, Epic 7.3

Fiche du **troisième lot du rang 2** de la famille des enchères (socle `docs/packet-specs/socle-encheres.md`
§6.3), après `1300` et `1302`. Elle porte **la requête** `1304` et sa réponse `1305` ; elle ne reprend ni
`1300`/`1301` ni `1302`/`1303`, et elle ne touche à aucun fichier de code.

Branche : `hermes/packet-1304-auction-bidded-list`, créée depuis `origin/master`
`b56967a07430422add88e0e5cdf292b41b18f6c6`. **Aucun commit sur `master`.**

---

## 1. Identité

| | |
|---|---|
| id décimal | **1304** |
| nom | `TM_CS_AUCTION_BIDDED_LIST` |
| source du nom | `op_codes.md:206` (réponse `TM_SC_AUCTION_BIDDED_LIST = 1305`, `op_codes.md:207`) |
| nom de référence rzu | `TS_CS_AUCTION_BIDDED_LIST` (`librzu/src/packets/GameClient/TS_CS_AUCTION_BIDDED_LIST.h`) |
| nom de référence NGemity | `TS_CS_AUCTION_BIDDED_LIST` (`shared/Server/ClientPackets.h:213`, valeur `1304`) |
| sens | le joueur demande **la page de la liste des annonces sur lesquelles son personnage a enchéri** (« mes enchères »), onglet de `SUIAuctionTenderWnd` |
| session / origine | `SessionType::GameClient`, `SessionPacketOrigin::Client` (`TS_CS_AUCTION_BIDDED_LIST.h:14`) |
| réponse | `1305` `TM_SC_AUCTION_BIDDED_LIST`, **déjà déclarée et déjà construite** sur `master` (`GamePackets.cs:158`, `GameAuctionPackets.cs:114`) |

Deux paires à ne pas confondre : `1302`/`1303` (*mes ventes*), `1304`/`1305` (*mes enchères*). Les trames
de requête sont identiques (§3.3), les réponses ont la même taille et la même forme d'entrée, mais **deux
opcodes, deux fenêtres client, deux lots**.

L'identifiant `1304` est **daté** : en 7.3 il ne désigne que cette requête d'enchère (§4.5), jamais la
demande d'invocation (`TM_CS_SUMMON = 304`), ce que le dépôt a déjà acté dans un commentaire à ne pas
modifier (`Game/Network/Packets/Enums/GamePackets.cs:74-78`).

---

## 2. Ce que le joueur fait

Le client 7.3 range l'hôtel des ventes dans ses propres fenêtres, identifiées par leur nom RTTI MSVC
(lecture statique de `SFrame.exe`, chaînes de `.data` ; méthode du socle §11) :

| élément | mesure |
|---|---|
| fenêtre de l'onglet « mes enchères » | `.?AVSUIAuctionTenderWnd@@`, VA `0xc14d8c` |
| message interne de demande | `.?AUSIMSG_REQ_AUCTION_BIDDED_LIST@@`, VA `0xc14d60`, identifiant interne `0x484` = **1156** |
| message interne de réponse | `.?AUSIMSG_RES_AUCTION_BIDDED_LIST@@`, VA `0xc1e5b0` (socle §2) |
| légende de statut d'une entrée | `.?AVkui_window_bidded_status_caption_property@auction@@`, VA `0xc14db0` |

Geste : le joueur **ouvre l'onglet « mes enchères » de `SUIAuctionTenderWnd`**, puis **pagine** dans la
liste. Ce sont les deux seuls chemins qui construisent la trame (mesure §3.4) : l'ouverture écrit
`page_num = 1`, la pagination passe la page en argument. La page est donc **1-based**, comme pour `1302`.

Un troisième chemin est mesuré et n'émet aucune trame : à la construction de la fenêtre, le client
**enregistre une attente de résultat** portant l'identifiant de la demande `1304` (`0x518`), aux côtés de
`1300`, `1302` et `1350` (quatre objets `.?AUResultSatisfier@LazyMsgProc@auction@@`, vtable `0xa25d80`,
sites `0x5039cc`, `0x503a00`, `0x503a34`, `0x503a68`, enregistrés dans le conteneur `[this+0x5c4]`). Ce
que cet objet attend et affiche n'est pas établi (§7.9) ; ce qui est établi est que l'identifiant `1304`
n'est pas un inconnu pour le client.

---

## 3. Structure sur le fil

### 3.1 Trame `TM_CS_AUCTION_BIDDED_LIST` — **11 octets**

En-tête commun de 7 octets (socle §3.1), puis un seul champ.

| offset | taille | type | nom (rzu) | valeur observée | source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` | **11** | client `0x48ddbb` (`mov esi,0xb`), écrit `0x48ddc4` (`mov [ebp-0xc],esi`) |
| 4 | 2 | `uint16` | `Id` | **`0x518` = 1304** | client `0x48ddb6` (`mov eax,0x518`), écrit `0x48ddc0` (`mov [ebp-8],ax`) |
| 6 | 1 | `uint8` | `Checksum` = somme des octets 0..5 | calculé par le client | client `0x48ddd0`-`0x48ddd8` (boucle de somme), écrit `0x48ddda` (`mov [ebp-6],dl`) |
| 7 | 4 | `int32` | `page_num` | 1 à l'ouverture, la page demandée ensuite | client `0x48dde0` (`mov eax,[edx+0x13]`), écrit `0x48dde3` (`mov [ebp-5],eax`) |

**Taille totale attendue : 11 octets**, `7 + 4`. Le tampon local de la fonction est de 12 octets
(`sub esp,0xc`) et le client le zéro-remplit par quatre écritures (`0x48dda6`-`0x48ddb2`), soit un octet
de plus que la trame : la trame, elle, s'arrête à l'offset 10.

### 3.2 Ce que rzu dit de la même trame

```c
// Last tested: EPIC_9_8_1                                        (TS_CS_AUCTION_BIDDED_LIST.h:5)
#define TS_CS_AUCTION_BIDDED_LIST_DEF(_) \
	_(simple)(int32_t, page_num)                                   (:7-8)
#define TS_CS_AUCTION_BIDDED_LIST_ID(X) \
	X(1304, version < EPIC_9_6_3)                                  (:10-12)
	X(2304, version >= EPIC_9_6_3)
```

Un seul champ, `int32_t page_num`, **aucun gating de champ** : la fiche n'a donc aucune décision de
version à prendre au-delà de l'identifiant lui-même (§4). NGemity déclare la même chose sans condition de
version (`TS_CS_AUCTION_BIDDED_LIST.h:6-9` : `_(simple)(int32_t, page_num)`, `CREATE_PACKET(…, 1304)`).

### 3.3 `1304` et `1302` : trame identique, code de construction identique

Les deux trames valent 11 octets, `page_num` `int32` à l'offset 7 (socle §3.3 pour la table commune) ; la
réponse `1305` est la jumelle de `1303` (socle §3.9 : 3899 octets, entrée de 97 = 96 + `status`).

La mesure va plus loin que le socle : les deux fonctions qui construisent **et envoient** la trame sont
**le même code au champ `Id` près**.

| | `1302` | `1304` |
|---|---|---|
| fonction (construit + envoie) | `0x48dd10` | `0x48dda0` |
| `Id` | `mov eax,0x516` (`0x48dd26`) | `mov eax,0x518` (`0x48ddb6`) |
| `Length` | `mov esi,0xb` (`0x48dd2b`) | `mov esi,0xb` (`0x48ddbb`) |
| écriture de `page_num` | `mov [ebp-5],eax` (`0x48dd53`) | `mov [ebp-5],eax` (`0x48dde3`) |
| appelant unique dans tout `.text` | stub `0x49e37d` | stub `0x49e38a` |
| clé d'aiguillage interne | `1154` (`0x482`) | `1156` (`0x484`) |

Toutes les instructions intermédiaires sont identiques (prologue, zéro-remplissage, boucle de checksum,
lecture de `[message+0x13]`, remise de la trame au gestionnaire de session `[this+0xb8]` par appel
virtuel au slot `+0xc4` de sa vtable, `0x48de07`-`0x48de19`). Il n'y a donc **rien à réinventer** pour le
format : la seule différence sur le fil est l'octet `Id`.

Aiguillage interne, mesuré de bout en bout (socle §11 pour la méthode) :

- `0x49e21d` : `sub eax,0x403` (1027), `cmp eax,0xde` (222), `movzbl 0x49ea50(%eax),%eax`,
  `jmp *0x49e98c(,%eax,4)` ;
- table d'octets `0x49ea50` : `index = clé - 1027` ; clé `1156` (index 129) → **cas 35** ;
- table de sauts `0x49e98c` : cas 35 → **`0x49e387`** (le cas 48 → `0x49e763` est le défaut « non
  traité ») ;
- stub `0x49e387` : `push edi` ; `mov esi,ecx` ; `call 0x48dda0` (`0x49e38a`) → la fonction de `1304`.

### 3.4 Le message interne, et les deux seuls sites qui l'émettent

Le message interne est un objet de **23 octets** (`push 0x17` → `operator new(23)`, `0x500f34`) :

| offset objet | contenu | source |
|---|---|---|
| 0 | vtable **`0xa25b00`** | `0x5008b1`, `0x500f61` |
| 4 | `Id` = `0x484` (**1156**) | `0x5008a3`, `0x500f42` |
| 8 | `word` 0 | `0x500f49` |
| 10 | `byte` 0 | `0x500f4f` |
| 11 | `dword` **19** | `0x500f53` |
| 15 | `dword` 0 | `0x500f5a` |
| 19 (0x13) | **`page_num`** | `0x500f67` (`movl $0x1,0x13(%eax)`) ou `0x5008b7` (`mov %ecx,0x13(%eax)`) |

Résolution du COL RTTI de la vtable `0xa25b00` → `0xbbd700` → `.?AUSIMSG_REQ_AUCTION_BIDDED_LIST@@` :
l'objet, son identifiant interne `1156` et la trame `1304` sont **la même chose**, prouvé deux fois (par
la chaîne et par l'identifiant), exactement comme le socle §3.3 le fait pour `1302`.

Les deux sites d'émission, obtenus par recherche de l'octet-motif complet `c7 40 04 84 04 00 00`
(`movl $0x484,4(%eax)`) — **deux occurrences dans tout le binaire** :

| site | ce qu'il écrit | lecture |
|---|---|---|
| `0x500880` | `page_num` ← argument (`[ebp+8]`, `0x5008b7`), puis `push $0x3e8` + `call 0x4f0510` sur `[esi+0x4a8]` (`0x5008be`-`0x5008ca`) | la **pagination** : la page vient de l'appelant |
| `0x500f42` | `movl $0x1,0x13(%eax)` (`0x500f67`), mêmes `push $0x3e8` + `call` | l'**ouverture de l'onglet** : première page = 1 |

Le message de `1302` est construit par le même patron (`0x4f8a13` : `movl $0x482,4(%eax)`, `dword` 19 à
l'offset 11, vtable `0xa25730`, `page_num` ← argument à l'offset 19). Autrement dit les deux messages
internes ne diffèrent que par la vtable et l'identifiant ; `1302` en a trois sites d'émission, `1304` en a
**deux**.

### 3.5 Ce que le client ne construit pas

Recensement de la constante `0x518` dans tout `.text` (le socle §12.5 signalait déjà que ce recensement
est inutilisable seul) : **170** occurrences des octets `18 05 00 00`. La séquence
`b8 18 05 00 00` (`mov eax,0x518`) n'apparaît que **trois** fois : `0x48ddb6` — la trame de `1304`,
précédée de `push esi` — puis deux déplacements de champ où la séquence tombe dans l'encodage
(`0x5c0cf0` `mov 0x518(%eax),%edi` ; `0x5fd7d6` `cmpl $0x1,0x518(%eax)`). Les **167** autres occurrences
sont des déplacements. C'est le **scan des appels relatifs** (`E8`/`E9` vers `0x48dda0`) qui tranche, et
il ne trouve qu'un appelant : le stub `0x49e38a`. Le client ne construit donc qu'**une** trame `1304`,
depuis **un** point d'entrée.

### 3.6 La réponse `1305`, telle que le client la reçoit

Mesure indépendante du socle §3.9, sur le handler `0x670730` (aiguillage `1305` : table d'octets
`0x67f4c8`, `index = id - 1201` = 104 → cas 3 ; table de sauts `0x67f4b0` → `0x67e6bf` → `call 0x670730`) :

- `push $0xf47` (3911 = 31 + 3880) puis `operator new(3911)` et construction de l'objet de liste par
  `0x66c7c0` (`0x67074b`) ;
- `page_num` ← trame+7 (`0x670757`), `total_page_count` ← trame+11 (`0x67075d`),
  `auction_info_count` ← trame+15 (`0x670763`), copiés sur l'objet aux offsets `0x13`/`0x17`/`0x1b` ;
- `rep movsl` de **970 `dword` = 3880 octets** depuis trame+19 (`0x670766`) vers objet+31 (`0x67076c`),
  soit **40 entrées de 97 octets**, sans jamais lire `auction_info_count` ;
- total **3899** octets, et l'octet `status` de chaque entrée n'est **jamais lu** par le handler.

Le handler de `1303` (`0x6706d0`) est le **même code** au constructeur d'objet près (`0x66c780` au lieu de
`0x66c7c0`) : même `push $0xf47`, mêmes offsets, même `rep movsl` de 970 `dword`, même conteneur cible
(`lea 0x2c(%ebx),%ecx`). Les deux réponses sont donc interchangeables **au format** ; elles ne le sont pas
pour le client, qui remplit deux listes différentes. Envoyer `1305` là où `1303` est attendu est un
désalignement d'usage, pas de taille.

---

## 4. Gating de version, tranché pour Epic 7.3

`EPIC_7_3 = 0x070300`, `EPIC_9_6_3 = 0x090603` (`rzu librzu/src/lib/Packet/PacketEpics.h:58-60,96`).

| # | élément | condition rzu | décision 7.3 | source |
|---|---|---|---|---|
| 4.1 | identifiant de la requête | `version < EPIC_9_6_3` → `1304`, sinon `2304` | **`1304`** (`0x070300 < 0x090603`) | `TS_CS_AUCTION_BIDDED_LIST.h:10-12` ; NGemity identique (`ClientPackets.h:213`, sans variante) |
| 4.2 | champ `page_num` | **aucun gating** | `int32` à l'offset 7, toujours présent | `:8` ; client §3.1 |
| 4.3 | identifiant de la réponse | `version < EPIC_9_6_3` → `1305`, sinon `2305` | **`1305`**, valeur déjà déclarée dans `GamePackets.cs:158` | `TS_SC_AUCTION_BIDDED_LIST.h:18-20` |
| 4.4 | entrée de la réponse | **aucun gating** dans l'en-tête rzu | 97 octets (`TS_AUCTION_INFO` 96 + `status` 1), déjà écrit par le socle | `TS_SC_AUCTION_BIDDED_LIST.h:6-16` ; `GameAuctionPackets.cs:36,51,66` |
| 4.5 | **le piège `304`/`1304`** | `TS_CS_SUMMON` : `304` pour `version < EPIC_9_6_3`, **`1304` pour `version >= EPIC_9_6_3`** | en 7.3, `1304` **est** l'enchère et `TM_CS_SUMMON` reste `304` ; ne jamais déclarer `TM_CS_SUMMON = 1304` | `TS_CS_SUMMON.h:10-12` ; `GamePackets.cs:74-78` (commentaire existant, à ne pas modifier) |

Le remappage est un **échange d'identifiants à 9.6.3** (l'enchère passe à `2304`, l'invocation prend
`1304`), et non une simple renumérotation : c'est la raison pour laquelle le commentaire de
`GamePackets.cs:74-78` interdit d'écrire `TM_CS_SUMMON = 1304`. Aucun champ de la trame n'a de gating non
statué : **la trame est entièrement déterminée pour 7.3**.

`item_info.appearance_code` (`>= EPIC_7_4` chez rzu, présent en 7.3 d'après la mesure client) est tranché
par le socle §4.6 et ne concerne pas la requête `1304`, qui ne porte aucun objet ; il reste présent, tel
quel, dans les quarante entrées de la réponse (déjà écrit par `ItemFixedInfoWriter`).

---

## 5. Traitement attendu

### 5.1 NGemity ne traite rien, et ne peut rien trancher

`grep -rni "TS_CS_AUCTION\|TS_SC_AUCTION" reference/ngemity/Chihiro/src` ne remonte **qu'une** ligne :
`Map/WorldLocation.h:50`, la constante orpheline `SecRouteAuction = 130107`. Aucune classe de ressource,
aucun handler, aucun service d'enchère. `reference/ngemity/shared/` ne contient que les déclarations
(`ClientPackets.h:209-218`, `Packets/GameClient/TS_CS_AUCTION_BIDDED_LIST.h`) et la structure de réponse
(`…/TS_SC_AUCTION_BIDDED_LIST.h:7-18`), identique à celle de rzu.

Deux conséquences, écrites comme des faits :

1. **Il n'y a rien à porter.** La logique ne peut venir que du client (mesuré ici) et du modèle du dépôt
   (§5.5). Le socle §5.1 le disait pour la famille ; `1304` le confirme, comme `1302`.
2. NGemity compile `EPIC_4_1_1` (`shared/Common/Define.h:25`), donc sa condition `version >= EPIC_7_2` sur
   `is_equipable` est fausse et ses tailles ne sont pas celles de 7.3. Ici cela ne change rien : son
   `TS_CS_AUCTION_BIDDED_LIST` est fixe à `1304`, comme à 7.3. Sa seule utilité est de confirmer l'**ordre**
   des champs (`page_num` seul), qui coïncide.

### 5.2 Le lecteur

Aucun lecteur n'existe sur `master` pour la famille (le socle n'a livré que les trois réponses). Le patron
du dépôt est une méthode statique testable hors socket, à contrôle de longueur, dans le fichier des
paquets de la famille — `Game/Network/Packets/Game/GameAuctionPackets.cs`, à côté de
`BuildAuctionBiddedList`, sur le modèle de `GameActionPackets.cs:133-146` (`TryReadStorage`) :

- longueur attendue `HeaderSize + 4` = **11** ; `packet.Length < 11` → `false` (refus) ;
- `page_num` = `BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize, 4))` ;
- **aucun contrôle de checksum** : le dépôt lit l'octet (`Header.cs:24`) et ne le compare nulle part sur
  le chemin entrant ; un lecteur qui l'inventerait créerait une règle que rien ne soutient.

La question « que faire d'une trame **plus longue** que 11 » est commune à `1300` et `1302` et se tranche
**une fois pour la famille** (§7.11), pas trois fois différemment.

### 5.3 Le bras de dispatch

`GameClient.cs` : les paquets client → serveur sont traités par une suite de `if (header.ID == …)` au-dessus
du `switch` final, dont le défaut lève `Exception("Unknown Packet Type {header.ID}")`
(`GameClient.cs:1865`). Le garde-fou des trois **réponses** de la famille est déjà en place
(`:1405-1411`, `log + continue`), et le patron d'un bras est `:1413-1417`
(`if (header.ID == (ushort)GamePackets.TM_CS_TAKEOUT_COMMERCIAL_ITEM) { HandleTakeoutCommercialItem(msgBuffer); continue; }`).

Le bras de `1304` s'ajoute dans cette suite, avec sa méthode privée (`HandleAuctionBiddedList(byte[] buffer)`)
qui lit la trame, journalise, puis émet la réponse. Le membre `TM_CS_AUCTION_BIDDED_LIST = 1304` est ajouté
à `GamePackets` **dans le même commit** que ce bras (critère transversal 4 : aucun membre de `GamePackets`
ne peut atteindre le `switch` final).

Refus : `1360` n'existe pas, aucune trame d'erreur n'existe dans la famille (socle §5.3). Un refus passe
par `GameClient.SendResult(id, (ushort)ResultCode.InvalidArgument)` — patron `:543`, `:629` — avec l'id
`1304`. Le code exact à employer pour un refus de `1304` reste une décision (socle §9 q3, §7.9 ici).

### 5.4 La réponse : `1305`, **3899 octets**, page construite par le socle

Signature relevée, non présumée (`Game/Network/Packets/Game/GameAuctionPackets.cs:114-115`) :

```csharp
public static byte[] BuildAuctionBiddedList(int pageNum, int totalPageCount,
    IReadOnlyList<BiddedAuctionInfo> entries = null)
```

`entries` est optionnel : la page vide s'écrit `BuildAuctionBiddedList(pageNum, totalPageCount: 0)`. Le
constructeur écrit les 12 octets d'en-tête de page (`page_num` à 7, `total_page_count` à 11, compte
plafonné à 40 à 15) puis les 40 emplacements, zéro-remplis par construction (`GameAuctionPackets.cs:51-66`,
`:136`) : la contrainte des quarante emplacements du socle §5.2 est tenue par le socle, pas par le dev.

L'écho attendu : `page_num` = la page reçue (même hors bornes, cf. §7.6/§8), `total_page_count` selon la
règle non établie (§7.3).

### 5.5 « Annonce où j'ai enchéri » — ce qui est prouvable, et ce qui ne l'est pas

C'est **la** question de `1304`, nommée par le socle §6.3 (« définition d'‘annonce du personnage' et
d'‘annonce où j'ai enchéri' »). Le périmètre est délimité d'abord par la structure, ensuite par le modèle.

**Prouvable (structure du fil).**

1. L'entrée de `1305` ne porte **aucun vendeur** : `BiddedAuctionInfo` = `AuctionInfo` (96) + `status` (1)
   (`GameAuctionPackets.cs:36`), là où l'entrée de `1301` porte `seller_name` sur 31 octets
   (`SearchedAuctionInfo`, `:24` = 96 + 31 + 1 = 128). La liste de `1305` est donc, par construction,
   **celle du personnage qui demande** : il n'y a pas de champ où écrire un autre nom.
2. L'entrée ne porte **aucun nom d'enchérisseur** non plus, alors que le modèle en a un
   (`AuctionEntity.HighestBidderName`, `AuctionEntity.cs:33`) : ce champ n'a pas de destination sur le fil.
3. Il n'y a **qu'un prix** dans l'entrée (`bidded_price`, `TS_SC_AUCTION_SEARCH.h:11`, lu dans le motif
   partagé) et **qu'un prix par annonce** dans le modèle (`AuctionEntity.HighestBiddingPrice`, `:24`).
   Le modèle ne stocke **aucun montant par enchérisseur** : `BiddersIds` est un tableau d'identifiants
   (`:23`), sans montant ni horodatage. Conséquence : si le client affiche `bidded_price` comme « ma
   mise », le modèle ne peut pas la fournir — la seule valeur représentable aujourd'hui est le prix
   courant de l'annonce.

**Prouvable (modèle du dépôt, avec sa source).**

| élément | mesure | source |
|---|---|---|
| `AuctionEntity.HighestBidderId` | `bigint not null`, **indexé** (`IX_Auctions_HighestBidderId`), navigation + FK configurées | migration `20231213221150_Version0001_TheBeginning.cs:45` et `:376-380` ; `TelecasterContext.cs:153-156` |
| `AuctionEntity.BiddersIds` | `long[]` → colonne **`bigint[]` nullable**, **sans index**, aucune configuration EF | migration `:44` ; `AuctionEntity.cs:23` ; `ConfigureAuctions` `TelecasterContext.cs:146-157` (ne configure que `Seller` et `HighestBidder`) |
| `AuctionEntity.SellerId` | `bigint not null`, indexé (`IX_Auctions_SellerId`) | migration `:43`, `:382-385` |
| ce que contient `BiddersIds` | un **commentaire** : `// Referred from previous usage: List of character ids` | `AuctionEntity.cs:23` |
| identité du personnage | `ConnectionInfo.CharacterHandle` = id en base, `uint` ; à comparer à des `long` | `GameActions.cs:114`, `ConnectionInfo.cs:30` |
| écrivains de ces colonnes | **aucun** : `grep -rn "BiddersIds"` → la seule occurrence est la déclaration ; `DbSet<AuctionEntity> Auctions` n'est consommé par aucun repository ; aucune valeur de `StorageType` n'est utilisée hors de son énumération | mesures du réveil |

**Les deux prédicats candidats, et pourquoi ils ne sont pas équivalents.**

- `a.HighestBidderId == handle` → « je suis **actuellement** le plus offrant ». Sens unique, colonne
  `not null` et indexée, traduisible en SQL tel quel.
- `a.BiddersIds.Contains((long)handle)` → « j'ai enchéri **au moins une fois** ». Traduit en
  `= ANY(bidders_ids)`, parcours séquentiel (aucun index possible sur un tableau sans GIN), et les lignes
  où la colonne est `NULL` sont exclues par la sémantique SQL (`NULL` propagé), pas par un choix.

Un enchérisseur dépassé est dans `BiddersIds` sans être `HighestBidderId` ; `HighestBidderId` vaut `0`
(`not null`, aucune enchère) pour une annonce sans enchère, valeur qui ne désigne aucun personnage. Les
deux se rejoignent seulement sur les annonces où le personnage est encore en tête. **Aucune source ne dit
lequel des deux ensembles la fenêtre « mes enchères » affiche** — c'est la décision de Killian (`q1`).

**Ce qui rend la décision nécessaire mais pas urgente.** Aucune annonce ne peut exister au moment de la
livraison : rien n'écrit la table `Auctions` (ni `1309`, ni un service). Les trois candidats (aucun
prédicat, `HighestBidderId`, `BiddersIds`) produisent donc **la même page vide**, et aucun test
d'intégration ne peut distinguer l'un de l'autre. C'est le même raisonnement que la fiche sœur `1302`
(§5.5, §8 q4) : le lot livre la trame, le lecteur, le bras et la page vide, et n'écrit **pas** la requête.

**Ce que le lot doit faire en attendant** : nommer les deux prédicats dans un commentaire à l'endroit où
la requête viendra (le bras), sans en choisir un, et servir `BuildAuctionBiddedList(pageNum, 0)`. Écrire
la requête maintenant figerait par du code une décision de gameplay que la source ne tranche pas, et un
choix non testable est précisément ce que la consigne interdit de deviner.

### 5.6 Ce que le lot doit livrer

La trame est entièrement fixée et la réponse existe : **il y a du code à livrer**, contrairement à `4008`
(cas « pas de code » du précédent) — le lot n'est pas documentaire. Le socle §6.3 place `1304` au rang 2
avec un seul prérequis, **S2 (pagination partagée)**, déjà livré par le socle (`AuctionSlots`,
`WritePageHeader`, `ListPacketSize`) : contrairement à `1306`/`1308`/`1310`, ce lot **n'attend aucune carte
amont**.

1. `GamePackets.cs` : `TM_CS_AUCTION_BIDDED_LIST = 1304` (bande `13xx`, à côté de `:156-158`).
2. `GameAuctionPackets.cs` : le lecteur de §5.2 (11 octets, `page_num` à 7, refus si `< 11`).
3. `GameClient.cs` : `HandleAuctionBiddedList`, et le bras `if (header.ID == …) { …; continue; }` dans la
   suite des paquets client → serveur (§5.3).
4. La réponse : `BuildAuctionBiddedList(pageNum, 0)` (page vide), `page_num` en écho.
5. Les tests d'offsets du nouveau paquet (critère transversal 3) : taille totale **11**, `Length`/`Id`
   aux offsets 0 et 4, `page_num` à 7, et le refus d'une trame courte. Discipline :
   `Tests/Game/AuctionPacketsTests.cs` (socle) pour la famille ; le fichier de tests de `1304` est le sien.
6. Le bloc destiné à `CLAUDE.md` dans la description de la MR (§10), **sans écrire `CLAUDE.md`**.

Non livré, et c'est délibéré : aucune requête d'annonces (§5.5), aucune écriture d'enchère, aucun
`status` inventé, aucun `total_page_count` calculé.

---

## 6. Écarts assumés avec NGemity

1. **NGemity n'implémente pas le paquet** (§5.1) : aucun écart n'est un choix, c'est une absence. La
   validation ne peut venir que du client, dont toutes les mesures de cette fiche proviennent.
2. **Nom du motif de réponse** : NGemity écrit `TS_ITEM_BASE_INFO` là où rzu écrit `TS_ITEM_FIXED_INFO`
   (socle §7.2, décision conservée : **on garde le nom rzu**). Sans effet sur `1304`, qui ne porte aucun
   objet dans sa requête.
3. **Aucune variante de version chez NGemity** : son `CREATE_PACKET(TS_CS_AUCTION_BIDDED_LIST, 1304)` est
   fixe (`EPIC 4_1_1`), ce qui coïncide avec 7.3 par accident, pas par preuve. Sa valeur ne sert qu'à
   confirmer l'ordre des champs.
4. **NGemity ne connaît pas le remappage de 9.6.3** : la bascule `1304 ↔ 2304`/`304` de rzu (§4.5) n'a
   aucun pendant dans NGemity. Le dépôt suit rzu, la référence de protocole.

---

## 7. NON ÉTABLI

Formulés comme des questions à trancher, jamais comme des valeurs. **Aucun n'est deviné dans cette
fiche** ; aucun ne bloque la trame, le lecteur, le bras ni la réponse vide.

1. **Valeur du `status`** (octet 96 de chaque entrée de `1305`). Deux sources **rapprochées** mais non
   concluantes : le format de fichier du serveur officiel que rzu sait lire porte un
   `enum BidFlag { BF_Bidded = 0, BF_MyBid = 1, BF_NoBid = 2 }`
   (`rzauctionmonitor/lib/AuctionSimpleFile.h:23`), écrit comme `int8_t bid_flag` à côté de
   `duration_type`, `bid_price`, `price`, `seller` (`AuctionFile.h:19-23`) ; et la légende du client est
   `kui_window_bidded_status_caption_property@auction` (§2). Les noms se répondent (« bidded_status » ↔
   `BF_Bidded`/`BF_MyBid`/`BF_NoBid`), les largeurs aussi (1 octet), mais **aucune source n'énonce la
   correspondance** et le handler client de `1305` **ne lit jamais cet octet** (§3.6) : la table
   valeur → texte est dans les données d'interface, hors du binaire. *Question : les valeurs `0`/`1`/`2`
   de `bid_flag` sont-elles celles de `status` de `1305`, et surtout — pour un onglet « mes enchères » —
   quelle valeur porte une annonce où le personnage a été **dépassé** ?* (Le socle §8.2 laisse ouverte la
   possibilité que `deposit_status` et `bidded_status` diffèrent.)
2. **Le prédicat « j'ai enchéri »** (§5.5) : `BiddersIds` contient-il l'identifiant du personnage (y
   compris dépassé), `HighestBidderId` suffit-il, ou faut-il l'union ? *Question : que contient
   réellement `BiddersIds` dans une base de production ?* C'est la question qui commande tout le contenu
   de la réponse ; le dépôt n'a **aucun écrivain** de cette colonne, seulement un commentaire
   (`AuctionEntity.cs:23`).
3. **`total_page_count`** : la règle (40 par page) et un éventuel plafond ne figurent nulle part dans le
   client, qui affiche le nombre reçu. *Question : combien de pages pour N annonces du personnage, et
   existe-t-il un maximum d'annonces par personnage ou par serveur ?* (socle §8.6, §9 q5.)
4. **Annonces visibles** : parmi les états du modèle (`ItemBySuccessfulBid = 1`, `ItemByInstantPurchase = 2`,
   `ItemByExpiration = 3`, `ItemByCancel = 4`, et les retours d'or `GoldByHigherBid = 32`,
   `GoldByCancel = 33`, `StorageType.cs:7-16`), lesquels restent listés ? La question de `1302` (ventes)
   ne se transpose pas : ici il faut de plus savoir si l'annonce **disparaît de la liste quand le
   personnage est dépassé**. *Question : « mes enchères en cours » ou « mes enchères, tous états » ?*
5. **Ordre des entrées** dans une page. *Question : par date de fin croissante, par date d'enchère, par
   identifiant ?*
6. **`page_num` hors bornes** (`0`, négatif, au-delà de la dernière page) : le client montre seulement que
   sa première page vaut `1` (§2). *Question : refus `InvalidArgument` ou page vide avec écho ?* Même
   question que pour `1300`/`1302` — **à trancher une fois pour la famille**.
7. **`IsHiddenVillageOnly`** (`AuctionEntity.cs:19`, socle §8.8) : filtre-t-il la liste des enchères, et
   selon le village du personnage ou un choix de dépôt ?
8. **Les deux prix d'une entrée** : `bidded_price` d'une annonce sans enchère (le modèle ne départage pas
   `0` d'un prix de départ, `AuctionEntity.cs:21`, `:24`) et `instant_purchase_price` d'une annonce sans
   achat immédiat. Pour `1304` la question est **plus lourde** que pour `1302` : si le client affiche
   `bidded_price` comme « ma mise », le modèle n'a aucun montant par enchérisseur (§5.5, point 3) et
   devrait être étendu avant qu'une entrée puisse être servie.
9. **Ce que fait le client d'un `TS_SC_RESULT` portant l'identifiant `1304`.** Resserré par rapport à la
   fiche `1302` (§7.8) : le client construit et enregistre bien un `.?AUResultSatisfier@LazyMsgProc@auction@@`
   portant `0x518` dans le conteneur `[this+0x5c4]` (§2), aux côtés de `1300`, `1302` et **`1350`**
   (`0x546`). Restent non établis : ce que cet objet satisfait exactement, ce qu'il affiche, et la nature
   de l'identifiant `1350`, qui n'appartient pas à la famille décrite par `op_codes.md:202-211`.
   *Question : `TS_SC_RESULT(1304, code)` produit-il un affichage, et lequel — ce qui fixe le code de
   refus à employer ?*
10. **`appearance_code` du motif d'objet** (socle §8.3) : question héritée, présente dans les 40 entrées de
    la réponse via le motif partagé de 75 octets. *Question : `0` suffit-il en 7.3 ?*
11. **Longueur exacte acceptée** : le client écrit **11** octets. *Question : refuser `> 11` ou seulement
    `< 11` ?* Même question que `1300` et `1302` : à trancher une fois pour les trois, pas trois fois
    différemment.

---

## 8. A VERIFIER PAR KILLIAN

| # | décision | ce que le lot fait en attendant |
|---|---|---|
| q1 | prédicat « annonce où j'ai enchéri » : `BiddersIds` ⊇ personnage, `HighestBidderId`, ou union (§5.5, §7.2) | aucune requête d'annonces : page vide ; les deux prédicats sont nommés en commentaire, aucun n'est choisi |
| q2 | contenu réel de `BiddersIds` en base (§7.2), sans écrivain dans le dépôt | sans objet tant que la requête n'est pas écrite |
| q3 | valeurs de `status` par entrée, et statut d'une enchère dépassée (§7.1) | rien : aucune entrée n'est servie |
| q4 | règle de `total_page_count`, plafond (§7.3) | `0` (liste vide), paramètre nommé dans le code |
| q5 | états conservés dans la liste — dépassée, gagnée, expirée, retirée (§7.4) | sans objet (page vide) |
| q6 | ordre des entrées (§7.5) | sans objet (page vide) |
| q7 | `page_num` hors bornes (§7.6) | écho de la page reçue, page vide, aucun refus |
| q8 | `IsHiddenVillageOnly` (§7.7) | sans objet (page vide) |
| q9 | prix d'une entrée ; faut-il un montant par enchérisseur dans le modèle (§7.8) | sans objet (page vide) ; **bloquant dès la première entrée** |
| q10 | effet d'un `TS_SC_RESULT(1304, code)` et code de refus retenu (§7.9) | aucun code : le refus reste `InvalidArgument` |
| q11 | `appearance_code` (§7.10) | sans objet (page vide) |
| q12 | refus de `Length > 11` (§7.11), **pour toute la famille** | `< 11` refusé, `> 11` accepté (aligné sur `1300`/`1302`) |

Aucune de ces lignes ne bloque le lot : la trame, le lecteur, le bras, la réponse vide de 3899 octets et
les tests d'offsets sont déterministes et vérifiables sans elles. Ce qui a effectivement été livré, et
avec quelles mesures, est consigné en **§14** (section ajoutée par `navis-dev`) : les douze questions
restent ouvertes telles quelles.

---

## 9. Commits et binaires épinglés

Empreintes **relues dans ce réveil** (aucune reprise de confiance sur le socle) :

| référence | empreinte | où |
|---|---|---|
| rzu (`librzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02, « packets: fix TS_SC_INVENTORY with older epics ») | `git -C reference/rzu log -1` |
| NGemity / `Chihiro` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03, « Fix compilation issue for GCC ») | `git -C reference/ngemity log -1` |
| client Epic 7.3 | `reference/client73/SFrame.exe`, `sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, **9 841 664** octets | `sha256sum`, `stat -c %s` |
| Navislamia, base du réveil | `origin/master = b56967a07430422add88e0e5cdf292b41b18f6c6` | `git rev-parse origin/master` |

`reference/client73/` **n'est pas un dépôt Git** : aucune révision n'y est citable, seule l'empreinte du
binaire est opposable (règle du socle §11). Les références rzu et NGemity sont citées par *chemin:ligne*
sur ces deux commits. Les adresses du client sont données en **VA** telles qu'`objdump` les affiche pour
ce binaire ; c'est l'empreinte ci-dessus qui les rend opposables.

---

## 10. Bloc destiné à `CLAUDE.md`

À recopier dans la description de la MR (le dev n'écrit pas `CLAUDE.md`, Hermes le protège).

```markdown
#### Enchères — `TM_CS_AUCTION_BIDDED_LIST` (1304)

Demande de la liste des annonces sur lesquelles le personnage a enchéri : **11 octets**, en-tête de 7
(Length/Id/Checksum) puis un seul `int32 page_num` à l'offset 7. Gating rzu : `1304` pour
`version < EPIC_9_6_3` (trancher 7.3 : `EPIC_7_3 = 0x070300 < 0x090603`), `2304` sinon. **Trame
strictement identique à `1302`**, au champ `Id` près (le client envoie les deux par le même code) : deux
opcodes, deux fenêtres, deux lots. Piège daté à ne pas rouvrir : chez rzu c'est `TS_CS_SUMMON` qui prend
l'identifiant `1304` à partir de 9.6.3 ; en 7.3 `1304` est l'enchère et `TM_CS_SUMMON` reste `304`
(`GamePackets.cs:74-78`).

La réponse est **1305**, et elle fait **3899 octets** (7 + 12 + 40 × 97), comme `1303` : le client copie
3880 octets sans vérifier `Length`. Entrée = 96 octets (`TS_AUCTION_INFO`) + 1 octet `status`, construite
par `GameAuctionPackets.BuildAuctionBiddedList(pageNum, totalPageCount, entries)` — les handlers client de
`1305` et de `1303` sont le même code au constructeur d'objet près, seule la liste cible diffère.

L'entrée ne porte **ni vendeur ni nom d'enchérisseur** (contrairement à `1301`, qui porte `seller_name`) :
la liste est donc celle du personnage qui demande (`ConnectionInfo.CharacterHandle`, `GameActions.cs:114`).
Le contenu reste une décision de gameplay : le prédicat « j'ai enchéri » est ambigu entre
`AuctionEntity.BiddersIds` (`bigint[]` nullable, sans écrivain, sémantique fondée sur un commentaire) et
`AuctionEntity.HighestBidderId` (colonne `not null` indexée) ; `status`, `total_page_count`, les états
conservés et l'ordre restent ouverts. Fiche : `docs/packet-specs/1304-auction-bidded-list.md`.
```

---

## 11. Références croisées

- socle : `docs/packet-specs/socle-encheres.md` §1 (identité), §2 (fenêtres et messages internes),
  §3.1 (en-tête commun), §3.3 (les deux trames de 11 octets), §3.9 (réponses `1303`/`1305`),
  §4.2 (gating des identifiants), §5.1 (NGemity muet), §5.2 (les 40 emplacements), §5.3 (réponses et
  refus), §5.5 (le modèle du dépôt), §6.2/§6.3 (découpage et rang 2), §8.2/§8.3/§8.6/§8.8 (réserves),
  §9 (questions à Killian), §11 (commits épinglés et méthode de lecture statique) ;
- fiche sœur : `docs/packet-specs/1302-auction-selling-list.md` (branche
  `hermes/packet-1302-auction-selling-list`, MR #66) — même trame, même méthode ; sa décision propre est
  « annonce du personnage », celle d'ici est « annonce où j'ai enchéri » ; sa §7.8 (effet d'un
  `TS_SC_RESULT`) est précisée ici en §7.9 ;
- fiche voisine : `docs/packet-specs/1300-auction-search.md` (branche `hermes/packet-1300-auction-search`,
  MR #65) — même famille, même question ouverte sur la longueur exacte acceptée ;
- `docs/packet-specs/304-summon.md` : la preuve du piège d'identifiant `304`/`1304` (§4.5) ;
- dépôt : `GamePackets.cs:74-78` (commentaire à ne pas modifier), `GamePackets.cs:156-158` (bande `13xx`
  des réponses), `GameAuctionPackets.cs:36,51-66,114-115,136`, `GameClient.cs:1405-1411,1413-1417,1865`,
  `AuctionEntity.cs:23-33`, `TelecasterContext.cs:11,146-157`,
  `Migrations/Telecaster/20231213221150_Version0001_TheBeginning.cs:44-45,376-385`,
  `Tests/Game/AuctionPacketsTests.cs` (discipline des offsets).

---

## 12. Ce que cette fiche resserre ou corrige

Rien de faux dans le socle : les points ci-dessous le **précisent**, et deux formulations de la carte
PO sont rectifiées.

1. **« Constructeur de trame `0x48dde3` » (carte PO, §« Ce que la fiche doit établir », point 1).**
   Mesure : `0x48dde3` est l'instruction qui **écrit `page_num`** dans la trame ; la fonction qui
   **construit et envoie** est `0x48dda0`, appelée depuis le stub `0x49e387`. La formulation du socle
   §3.3 (« constructeurs `0x48dd10` et `0x48dda0` ») est la bonne. La carte annonçait par ailleurs la
   branche « déjà créée » : elle ne l'était pas, c'est ce lot qui la crée, depuis `origin/master`.
2. **Recensement de constante pour `0x518`.** Le socle §12.5 (et la fiche `1302`) signalaient que le
   recensement des octets `18 05 00 00` est inutilisable seul. Mesure : **170** occurrences dans
   `.text`, dont **une seule** est un `mov eax,0x518` qui écrit un `Id` de trame (`0x48ddb6`) ; les deux
   autres occurrences de cette séquence (`0x5c0cf0`, `0x5fd7d6`) tombent dans l'encodage de déplacements
   de champ. C'est le **scan des appels relatifs** qui tranche : un seul appelant de
   `0x48dda0` dans tout `.text`. La conclusion du socle §11 est confirmée, pas modifiée.
3. **« Ouvre l'onglet et pagine »** (socle §2) : le détail mesuré diffère de `1302` — `1304` n'a que
   **deux** sites d'émission (sur `0x500880` et `0x500f42`) là où `1302` en a trois, l'un écrivant
   `page_num = 1` (ouverture de l'onglet) et l'autre prenant la page en argument (pagination). La page
   est **1-based**, ce que le socle n'énonçait pas.
4. **Le lien message interne ↔ identifiant** : la clé `1156` de l'aiguillage interne, la vtable
   `0xa25b00` (COL `0xbbd700` → `.?AUSIMSG_REQ_AUCTION_BIDDED_LIST@@`) et la trame `1304` sont le même
   objet, prouvé deux fois ; les deux sites d'émission sont recensés par le motif complet
   (`c7 40 04 84 04 00 00`), exhaustivement.
5. **`status`** : le socle §8.2 et la fiche `1302` §7.1 citaient `bid_flag` comme piste « non
   concluante ». Cette fiche ajoute la **valeur** de l'énumération du serveur officiel
   (`BidFlag { BF_Bidded = 0, BF_MyBid = 1, BF_NoBid = 2 }`, `AuctionSimpleFile.h:23`), ce qui rend la
   piste nettement plus étroite — sans la transformer en preuve : la correspondance n'est énoncée nulle
   part et l'octet n'est jamais lu par le handler client. La question passe de « quelles valeurs ? » à
   « `0/1/2` sont-elles bien ces valeurs, et que porte une enchère dépassée ? » (§7.1).
6. **§7.8 de `1302` (effet d'un `TS_SC_RESULT` id de demande)** : précisée. Le client enregistre bien une
   attente par identifiant de demande, dont `1304` (§2, §7.9) ; ce qu'elle fait reste ouvert.
7. **Côté réponse**, la mesure confirme le socle §3.9 sur les 3899 octets et ajoute que les handlers
   `1303` (`0x6706d0`) et `1305` (`0x670730`) sont le même code au constructeur d'objet près
   (`0x66c780` / `0x66c7c0`) : seule la liste cible diffère, ce qui interdit d'envoyer l'une pour l'autre.
8. **Coordination de lot** : `1300`, `1302` et `1304` ajoutent chacune un bras dans la **même** suite de
   `if` de `GameClient.cs` et un membre dans la **même** bande `13xx` de `GamePackets.cs`. L'ordre de
   reprise du socle §6.3 (`1300`, puis `1302`/`1304`) donne l'ordre de fusion ; aucun de ces lots ne doit
   renuméroter les bras des autres ni redéclarer `1305`.

---

## 13. Vérifications exécutées

- **Dépôt, base du réveil** : `git rev-parse origin/master` =
  `b56967a07430422add88e0e5cdf292b41b18f6c6` ; arbre propre avant changement de branche ;
  `git log --oneline origin/master..master` **vide** (aucun commit sur `master`) ; travail sur
  `hermes/packet-1304-auction-bidded-list`, créée depuis `origin/master` (elle n'existait pas).
- **Build** : `NUGET_PACKAGES=/srv/navislamia/.nuget-cache dotnet build Navislamia.sln -c Debug` →
  **code de sortie 0** (0 erreur, 23 avertissements).
- **Tests** : `dotnet test Tests/Tests.csproj` → **code de sortie 0**, `Failed: 0`, `Passed: 1302`,
  `Skipped: 0` (base de ce réveil, avant tout code du lot).
- **Client** : lectures statiques uniquement — `objdump -d` sur `SFrame.exe`, `objdump -h` pour la table
  des sections PE, et des scripts Python de lecture d'octets (recensement de constantes, résolution des
  COL RTTI, lecture des tables de sauts). Aucun `SFrame.exe`, aucun Lua, aucun script du client n'a été
  exécuté ; aucun serveur de jeu et aucune base de données n'ont été démarrés (le VPS n'en a pas).
- **NGemity** : recherche insensible à la casse de `TS_CS_AUCTION`/`TS_SC_AUCTION` dans `Chihiro/src`
  (une seule occurrence, `WorldLocation.h:50`), et lecture des en-têtes de `shared/Server/`.
- **Dépôt (modèle)** : lecture de `AuctionEntity`, de `ConfigureAuctions`, de la migration
  `20231213221150_Version0001_TheBeginning.cs` (types et index de la table `Auctions`) et recherche des
  écrivains (`BiddersIds`, `Auctions`, `StorageType.`) — aucun.
- **Reproductibilité** : toutes les adresses du client sont données en VA, telles qu'`objdump` les
  affiche ; l'empreinte du binaire (§9) est ce qui les rend opposables.

---

## 14. Implémentation livrée (dev)

Section ajoutée par `navis-dev` ; l'analyse de l'archéologue (§1 à §13) est laissée intacte, à
l'exception du renvoi d'une phrase ajouté en fin de §8. Branche
`hermes/packet-1304-auction-bidded-list`, créée par `navis-ref` depuis `origin/master`
(`b56967a07430422add88e0e5cdf292b41b18f6c6`) : fiche `9d37e87`, code `287e570` (4 fichiers,
+430 lignes : 3 fichiers de code et 1 fichier de tests).

**Règle tenue par le code : lire les onze octets, écho de `page_num`, réponse 1305 vide de 3899
octets, aucune requête sur `AuctionEntity`, aucune politique inventée.**

### 14.1 Checklist des critères transversaux, avec les codes de sortie relevés

| # | Critère | État | Mesure |
|---|---|---|---|
| 1 | `dotnet build Navislamia.sln -c Debug` code 0 | **OK** | code de sortie **0**, `0 Error(s)`, `164 Warning(s)` (toutes préexistantes : `MigrateDatabase`, nullabilité, fixtures) |
| 2 | `dotnet test Tests/Tests.csproj` code 0, compte jamais en baisse | **OK** | base avant le lot : code **0**, **1302** réussis / 1302 (le chiffre de §13). Après : code **0**, **1318** réussis / 1318, 0 échec, 0 ignoré → **+16** |
| 3 | Au moins un test d'offsets (taille totale + position de chaque champ) | **OK** | `Tests/Game/AuctionBiddedListPacketsTests.cs` : `Request_IsElevenBytesWithTheMeasuredOffsets` (11 octets, `page_num` en 7), `Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame` (octets littéraux du client, `NotBe` big-endian sur le seul champ utile), `Request_DiffersFromTheSellingListFrameOnlyByTheId`, `TryReadAuctionBiddedList_ReadsThePageNumAsSigned`, `…_ReadsAPaddedFrameAndLeavesThePaddingAlone` |
| 4 | Enum et dispatch modifiés ensemble | **OK** | membre `TM_CS_AUCTION_BIDDED_LIST = 1304` (`GamePackets.cs:166`, entre 1303 et 1305) **et** bras `GameClient.cs:1473` → `HandleAuctionBiddedList` (`:412`), avant le `switch` final dont le `_` lève `Unknown Packet Type`. Tenus par **exécution** (`BiddedListRequest_IsConsumedByTheReceiveLoopWithoutThrowing`) et par mutation (§14.5, mutant A) |
| 5 | Savoir durable dans la fiche commitée + bloc `CLAUDE.md` dans la description de la MR | **OK côté fiche** | présente section + §10 et §14.10 remis à `navis-qa` pour la description de la MR (le dev n'écrit pas `CLAUDE.md`) |
| 6 | Version tranchée | **OK** | `1304` est l'id 7.3 (`0x070300 < 0x090603`) ; `2304` n'est pas déclaré (`Id_IsTheEpic73OneAndTheSummonTrapStaysClosed` le vérifie par `Enum.IsDefined`) ; aucun champ gaté (§4.2 : `page_num` n'a aucun gating par version chez rzu) ; le piège `304`/`1304` est vérifié par test (`TM_CS_SUMMON` vaut toujours `304`) |
| 7 | Aucun commit sur `master` locale | **OK** | `git log --oneline origin/master..master` → aucune ligne (§14.9) |
| 8 | Aucun champ `NON ÉTABLI` deviné | **OK** | aucun `SELECT` sur `AuctionEntity`, `total_page_count = 0`, page écho, `status`/prix/`appearance_code` jamais renseignés : les douze questions de §7 restent ouvertes en §8, et le code ne fait que ce que §5.5/§5.6 autorisent (page vide) |

### 14.2 Fichiers livrés

| Fichier | Modification |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_AUCTION_BIDDED_LIST = 1304` (`:158-166`) : la trame, l'émetteur client (constructeur-et-émetteur `0x48DDA0`, appelant unique `0x49E387`), le rappel que 1302/1305 sont autres et que `304` reste l'invocation |
| `Game/Network/Packets/Game/GameAuctionPackets.cs` | `BiddedListRequestPageNumOffset` (`:191`), `BiddedListRequestSize` (`:197`), `TryReadAuctionBiddedList` (`:213-222`) |
| `Game/Network/Clients/GameClient.cs` | `HandleAuctionBiddedList(byte[])` (`:412-441`) et son bras de dispatch (`:1461-1475`) |
| `Tests/Game/AuctionBiddedListPacketsTests.cs` | 16 tests (nouveau, 324 lignes) |

Aucun constructeur de trame n'est ajouté : `BuildAuctionBiddedList(pageNum, 0)` existe dans le socle et
est appelé tel quel. `GameAuctionPackets.cs` n'est modifié qu'**à la fin de la classe**, après les
helpers privés (voir §14.6).

### 14.3 Offsets livrés, et les tests qui les tiennent

| Offset | Taille | Champ | Valeur lue / écrite | Test |
|---|---|---|---|---|
| 0 | 4 | `Length` `uint32` LE | **11** (`0xB`) | `Request_IsElevenBytesWithTheMeasuredOffsets`, `Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame` |
| 4 | 2 | `ID` `uint16` LE | **1304** (`0x0518`) | les deux précédents + `Id_IsTheEpic73OneAndTheSummonTrapStaysClosed` |
| 6 | 1 | `Checksum` | somme des octets 0-5 (charge exclue, comme la famille 1300/1302) | `Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame` (checksum `0x28` reconstruit sur la trame littérale) |
| 7 | 4 | `page_num` `int32` LE | lu tel quel, écho vers 1305 | `TryReadAuctionBiddedList_HandsBackThePageNum` (6), `…_ReadsTheFirstPageTheClientWrites` (1, écrit en dur à l'ouverture de l'onglet), `…_ReadsAPaddedFrameAndLeavesThePaddingAlone` (4), `…_ReadsThePageNumAsSigned` (`-1`) |
| 11 | — | fin de trame | — | `Request_IsElevenBytesWithTheMeasuredOffsets`, `Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame`, `…_ReadsAPaddedFrameAndLeavesThePaddingAlone` |

Le lecteur **accepte aval** toute longueur `>= 11` et **refuse** en dessous (trois cas : 0, 7, 10).
C'est la frontière de §7.11 : `> 11` est toléré (le client n'en produit jamais), `< 11` ne peut pas être
lu sans inventer un alignement. Une trame refusée est consommée en totalité et ne désynchronise pas la
suivante (test de coalescence). Une trame refusée reçoit `TS_SC_RESULT` id `1304` `InvalidArgument` —
convention du socle §5.3, appliquée comme 1300 et 1302.

### 14.4 Réponses émises : une seule, la page vide du socle

`BiddedListRequest_AnswersAnEmptyPageEchoingPageNum` exécute la vraie boucle de réception (page 5) et
vérifie sur l'unique trame émise : **3899 octets** (`7 + 12 + 40 × 97`), `Length = 3899`, `ID = 1305`
— et explicitement **pas 1303**, puisque les deux réponses sont le même format mais pas la même liste
cible (§3.6) —, checksum valide, `page_num` écho de la demande, `total_page_count = 0`,
`auction_info_count = 0`, **table intégralement nulle**, la dernière entrée finissant exactement sur le
dernier octet. `BiddedListRequest_AnswersThePageTheClientAskedForAndNotAFixedOne` refait le trajet avec
`page_num = 1` puis `= 3` — et vérifie que les quatre octets de page des deux réponses **diffèrent** —
pour montrer que la page n'est pas figée dans le code, la trame d'ouverture de l'onglet valant 1.
`BiddedListRequest_MalformedFrameIsRefusedWithTheFamilyResult` vérifie qu'une trame de 10 octets produit
un `TS_SC_RESULT` de 15 octets (`id = 1304`, `InvalidArgument`) et **aucune** trame 1305 — aucune variante
d'erreur de 1305 n'existe chez rzu, NGemity ou le client (§5.2).

Le prédicat « annonce où j'ai enchéri » n'est écrit nulle part et il est **nommé** dans le code, à
l'endroit où la requête viendrait (commentaire de `GameClient.cs:436-440`) :
`AuctionEntity.BiddersIds.Contains(handle)` d'un côté, `AuctionEntity.HighestBidderId == handle` de
l'autre, aucun des deux choisi.

### 14.5 Preuve par mutation (trois mutants, chacun tué par 5 tests sur 16)

| Mutant | Fichier | Effet mesuré |
|---|---|---|
| A — le bras devient inatteignable (condition préfixée par `header.ID == (ushort)GamePackets.TM_NONE`) | `GameClient.cs:1470` | **5 échecs**, dont le piège documenté : `System.Exception : Unknown Packet Type 1304` — c'est l'invariant « enum et dispatch ensemble » rendu visible |
| B — garde du lecteur décalée (`<=` au lieu de `<`) | `GameAuctionPackets.cs:215` | **5 échecs** (`page_num` non écho, réponse de 15 octets au lieu de 3899) |
| C — endianness inversée (`ReadInt32BigEndian`) | `GameAuctionPackets.cs:221` | **5 échecs** (pages 16777216, 83886080, 100663296…), ce qui rend la `NotBe` big-endian du test d'offsets opposable |

Filtre employé : `dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~AuctionBiddedListPacketsTests"`
(16 tests, 11 réussis / 5 échoués sous mutant). Après chaque mutant : `git checkout -- <fichier>` puis
`git diff --stat` **vide**, et la passe finale complète redonne **1318 / 1318**. Les mutants n'ont pas
été commités.

### 14.6 Fusion avec les branches sœurs 1300 (MR #65) et 1302 (MR #66) — mesurée

```
git merge-tree --write-tree origin/hermes/packet-1300-auction-search HEAD  → code 0, aucune ligne de conflit
git merge-tree --write-tree origin/hermes/packet-1302-auction-selling-list HEAD → code 0
# fusion des deux sœurs, puis de ce lot dans le résultat :
git merge-tree --write-tree origin/hermes/packet-1300-auction-search origin/hermes/packet-1302-auction-selling-list
  → arbre 3c3aab6a6be538ead3d720ec337694241adfec73, code 0
git commit-tree 3c3aab6a… -p <1302> -p <1300> -m tmp  → cf328d61c6ea6346507b8e509fd7b18b2e799885
git merge-tree --write-tree --messages cf328d61 HEAD → code 0, seul message d'auto-fusion :
  Auto-merging Game/Network/Clients/GameClient.cs, GamePackets.cs, GameAuctionPackets.cs
```

Dans l'arbre fusionné (`d926ac475bbc04b6692c65a5a4a5760e5711d786`), les trois apports coexistent :
l'énumération s'ordonne `1300, 1301, 1302, 1303, 1304, 1305` ; les trois lecteurs
(`TryReadAuctionSearch`, `TryReadAuctionSellingList`, `TryReadAuctionBiddedList`) vivent dans le même
`GameAuctionPackets.cs` ; les trois bras (`HandleAuctionSearch` en `:1992`, `HandleAuctionSellingList`
en `:1526`, `HandleAuctionBiddedList` en `:1547`) sont présents dans `GameClient.cs`, tous avant le
`switch` final. Ce résultat vient des ancrages retenus, choisis pour ne toucher aucune ligne des sœurs :
les constantes et le lecteur 1304 sont **à la fin de la classe** (la sœur 1300 insère après
`SellerNameSize`, la sœur 1302 après les trois constructeurs), le bras 1304 est posé **entre deux bras
sans rapport** (la sœur 1302 le pose juste après le garde-fou des réponses, la sœur 1300 juste au-dessus
du `switch`), la méthode privée est posée après `HandleCheckIllegalUser` (la sœur 1300 la pose après
`HandleXtrapCheck`, la sœur 1302 après `HandleGetWeatherInfo`), et le fichier de test
(`AuctionBiddedListPacketsTests.cs`) est distinct de `AuctionSearchPacketsTests.cs` et de
`AuctionSellingListPacketsTests.cs`.

**Réserve mesurée, et non textuelle : deux assertions des branches sœurs deviennent fausses.** Sur
l'arbre fusionné exporté (`git archive d926ac47 | tar -x`), `dotnet build` → **0 erreur** (après copie
de `DevConsole/appsettings.json`, non suivi par Git : artefact de l'export, voir §14.8) et
`dotnet test Tests/Tests.csproj` → **1349 réussis / 2 échoués / 1351** ; les deux échoués portent le
**même nom** et sont les deux sœurs :

| fichier | ligne | assertion |
|---|---|---|
| `Tests/Game/AuctionSearchPacketsTests.cs` (branche 1300) | 82 | `Enum.IsDefined(typeof(GamePackets), (ushort)1304).Should().BeFalse()` |
| `Tests/Game/AuctionSellingListPacketsTests.cs` (branche 1302) | 86 | idem |

Ces deux assertions ont été écrites quand `1304` n'existait pas encore ; ce lot le déclare, donc elles
deviennent fausses **par la fusion, pas par un défaut de ce lot**. Correctif, d'une ligne dans chaque
fichier, à appliquer par qui fusionne (les deux fichiers n'existent pas sur cette branche) :
`… .Should().BeFalse()` → `.Should().BeTrue()`, ou suppression des deux lignes. Le fichier de tests de
ce lot n'a, lui, **aucune assertion sur les identifiants 1300/1302** : il n'affirme que `2304` non
déclaré, pour ne pas créer la même dette dans l'autre sens.

### 14.7 Invariant « enum et dispatch ensemble », mesuré sur le dépôt, pas seulement sur le lot

Script de comptage (`git grep -w` de chaque membre de `GamePackets` hors de l'énumération, restreint aux
sources `*.cs` ; les citations de `docs/` et d'`op_codes.md` ne prouvent pas qu'un identifiant est
atteignable) : `master` → **140** membres, **2** non référencés dans le code (`TM_SC_CHAT_RESULT`,
`TM_SC_ITEM_COOL_TIME` : dette héritée, paquets S→C). Branche 1304 → **141** membres, **les mêmes 2**
seulement : le nouveau membre est référencé par son bras. L'autre moitié du piège — un membre déclaré
dont le bras ne se déclenche jamais — n'est pas visible par ce comptage statique ; c'est le mutant A
(§14.5) qui la rend opposable, par l'exception `Unknown Packet Type 1304` levée en exécution.

### 14.8 Réserves du dev

1. **`Length > 11` est toléré en lecture**, comme pour 1300 et 1302 : c'est la frontière de §7.11
   (question q12), pas une décision métier. Un client 7.3 n'en produit jamais.
2. **Le refus émet `InvalidArgument`** parce que §5.2 et le socle §5.3 le prescrivent (toute demande
   reçoit un résultat portant l'id de la demande) : ce n'est pas un code choisi par le dev. La réaction
   du client 7.3 à un `TS_SC_RESULT` id `1304` reste inconnue (q10).
3. **Signature** : `TryReadAuctionBiddedList(ReadOnlySpan<byte>, out int)` suit la convention du dépôt et
   des sœurs, plutôt que le `byte[]` de §5.2 ; l'appel décrit en §5.2 est inchangé (`byte[]` s'y
   convertit implicitement). Le paramètre de sortie est le seul champ de la trame.
4. **`total_page_count = 0`** est passé en argument nommé, pas calculé : si Killian tranche q4, c'est une
   ligne à changer, pas une requête à écrire. De même, la page vide n'est pas une affirmation sur l'état
   de `AuctionEntity` : c'est le constat que rien dans ce dépôt n'écrit `Auctions` (§5.5), et l'entrée de
   1305 ne portant **aucun montant par enchérisseur**, le modèle devrait être étendu avant qu'une entrée
   puisse être servie (q9, bloquant dès la première entrée).
5. **Rien n'a été exécuté côté client** : la trame de 11 octets vient du désassemblage de `SFrame.exe`
   (§3.1, §9), la vérification en jeu (ouvrir l'onglet « mes enchères », puis la page suivante) reste la
   seule qui ferme q1/q9/q10.
6. **La mesure de fusion (§14.6) a demandé une copie manuelle** de `DevConsole/appsettings.json`, fichier
   local non suivi par Git : l'export `git archive` en est dépourvu et `MigrateDatabase.csproj` échoue
   alors sur `MSB3030` (`Could not copy the file … appsettings.json`). Cet échec appartient à l'export,
   pas à l'arbre fusionné ; une fois le fichier copié, l'arbre fusionné compile à 0 erreur.

### 14.9 Commandes relevées

```
dotnet build Navislamia.sln -c Debug       → code 0, 0 Error(s), 164 Warning(s) (préexistantes)
dotnet test Tests/Tests.csproj             → code 0, 1318 réussis / 1318, 0 échec, 0 ignoré
                                             (base avant le lot : code 0, 1302 / 1302)
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~AuctionBiddedListPacketsTests"
                                           → 16 tests ; 5 échecs sous chacun des trois mutants (§14.5)
git log --oneline origin/master..master    → aucune ligne
git merge-tree --write-tree origin/hermes/packet-1300-auction-search HEAD    → code 0, 0 conflit
git merge-tree --write-tree origin/hermes/packet-1302-auction-selling-list HEAD → code 0, 0 conflit
git merge-tree --write-tree --messages cf328d61 HEAD (cf328d61 = fusion 1300+1302) → code 0, 0 conflit
# arbre fusionné exporté dans /tmp/merged-1304-a :
dotnet build Navislamia.sln -c Debug       → code 0 après copie de DevConsole/appsettings.json
dotnet test Tests/Tests.csproj             → code 1, 1349 réussis / 2 échoués (les deux sœurs, §14.6)
bash /tmp/invariant.sh                     → master 140 membres / 2 non référencés ; branche 141 / 2
```

### 14.10 Bloc `CLAUDE.md` : le complément qu'ajoute le dev

Le bloc de §10 est conservé tel quel. Le paragraphe suivant vient s'y ajouter (dernier paragraphe,
avant la phrase « Fiche : … » ou après elle), pour que `CLAUDE.md` décrive l'état **livré** et pas
seulement l'état attendu :

```markdown
Traitement livré : le bras de dispatch lit les 11 octets, journalise `page_num` en `Debug` et répond
`GameAuctionPackets.BuildAuctionBiddedList(page_num, 0)` — page vide de 3899 octets,
`total_page_count = 0` tant que la règle n'est pas tranchée. Une trame de moins de 11 octets est
consommée puis refusée par un `TS_SC_RESULT` (`id = 1304`, `InvalidArgument`) ; aucune variante d'erreur
de 1305 n'existe, et la réponse est 1305 et jamais 1303 (même format, autre liste cible). Aucune requête
n'est faite sur `AuctionEntity` : rien dans le dépôt n'écrit `TelecasterContext.Auctions`, donc la page
est vide par construction, pas par décision ; les deux prédicats candidats (`BiddersIds`,
`HighestBidderId`) sont nommés dans un commentaire et aucun n'est choisi. Une trame plus longue que
11 octets est lue sans être refusée (question ouverte §7.11). Rappel du piège : un membre de
`GamePackets` sans bras atteint le `throw` « Unknown Packet Type » et casse la boucle de réception —
énumération et dispatch se modifient ensemble.
```
