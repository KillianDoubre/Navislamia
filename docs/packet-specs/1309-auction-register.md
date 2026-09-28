# `TM_CS_AUCTION_REGISTER` (1309) — fiche de paquet, Epic 7.3

> Fiche de l'archéologue (`navis-ref`) pour la branche `hermes/packet-1309-auction-register`,
> sixième carte de la famille « enchères » après 1300, 1302, 1304, 1306 et 1308.
> Le lot de dev ajoute sa livraison en **section 14** ; les sections 1 à 13 ne sont pas réécrites.
> Rien dans cette fiche n'a été obtenu en exécutant le client, un Lua ou un script du client : tout
> vient de la lecture statique de `SFrame.exe` (`objdump -d`), des trois dépôts de référence et du
> dépôt. Les adresses du client sont des adresses virtuelles (image base `0x400000`).

| | |
|---|---|
| id décimal | **1309** (jamais 2309 en 7.3, §4) |
| nom dans le dépôt | `TM_CS_AUCTION_REGISTER` |
| sens | client → serveur |
| taille de trame | **32 octets** (§3.1) |
| branche | `hermes/packet-1309-auction-register` |
| base | `master` = `b56967a07430422add88e0e5cdf292b41b18f6c6` |
| socle | `docs/packet-specs/socle-encheres.md` (§2, §3.6, §4.2, §5.3, §5.5, §6.2, §6.3, §8.4, §9, §11) |

---

## 1. Identité

* **Nom et identifiant dans le dépôt** : `op_codes.md:210` (`[1309] = "TM_CS_AUCTION_REGISTER"`).
  Le nom est celui du fichier de rzu, sans transformation.
* **rzu** : `reference/rzu/librzu/src/packets/GameClient/TS_CS_AUCTION_REGISTER.h` —
  `_(simple)(ar_handle_t, item_handle)` (l. 6), `_(simple)(int32_t, item_count)` (l. 7),
  `_(simple)(int64_t, start_price)` (l. 8), `_(simple)(int64_t, instant_purchase_price)` (l. 9),
  `_(simple)(uint8_t, duration_type)` (l. 10),
  `X(1309, version < EPIC_9_6_3)` / `X(2309, version >= EPIC_9_6_3)` (l. 13-14),
  `CREATE_PACKET_VER_ID(TS_CS_AUCTION_REGISTER, SessionType::GameClient,
  SessionPacketOrigin::Client)` (l. 16). Aucune ligne d'en-tête du type « Last tested » n'existe dans
  ce fichier — contrairement à `TS_CS_AUCTION_INSTANT_PURCHASE.h` : il n'y a donc pas de jalon
  déclaré pour `1309`, seule la paire `X(...)` de l'identifiant est datée.
* **NGemity** : `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_AUCTION_REGISTER.h:6-11`
  (`uint32_t item_handle`, `int32_t item_count`, `int64_t start_price`,
  `int64_t instant_purchase_price`, **`int8_t`** `duration_type`), `CREATE_PACKET(TS_CS_AUCTION_REGISTER,
  1309)` (l. 13) — donc **sans** `X(...)`, inclus par `reference/ngemity/shared/Server/XPacket.h:58`,
  énuméré en `reference/ngemity/shared/Server/ClientPackets.h:217`.
* **État du dépôt à la base** : l'id 1309 n'est déclaré nulle part. La famille n'a, sur `master`, que
  les trois réponses serveur → client 1301/1303/1305 (`Game/Network/Packets/Enums/GamePackets.cs:156-158`)
  et leur bras « log and drop » (`Game/Network/Clients/GameClient.cs:1405-1411`). Le membre d'énumération
  et le bras de dispatch de `1309` sont donc à ajouter **ensemble** (§5.6, critère 4).
* **Prérequis du socle §6.3 : les deux sont livrés.** `S1` = le motif d'objet de 75 octets
  (`Game/Network/Packets/Game/ItemFixedInfoWriter.cs`, `ItemFixedInfo.Size = 75`) et `S3` = les
  catégories (`Game/DataAccess/Repositories/AuctionCateryResourceRepository.cs`,
  `DbSet<AuctionCateryResourceEntity> AuctionCateryResources` dans `ArcadiaContext.cs:30`). Vérifié sur
  `master` ce réveil. **La trame de `1309` ne porte aucun motif d'objet** : ses cinq champs sont
  scalaires et `S1` n'est ici qu'un contexte de famille — ne pas chercher de `item_info` là où il n'y
  en a pas.
* **La carte jumelle** est `1308` (`hermes/packet-1308-auction-instant-purchase`, MR #69), dont les
  conventions de code (§5.2, §5.3) et la méthode d'archéologie (§9) sont reprises ici. Les deux actes
  naissent dans les deux mêmes fenêtres, mais **pas depuis le même bouton ni le même contrôle** (§2).
* **Aucune méthode `Try*` d'enchère n'existe** : `grep -rn "TryReadAuction" --include=*.cs Game/ Tests/`
  est vide sur `master` (§13). Le lecteur de `1309` est à écrire.
* **Aucun service, dépôt ni chargement d'annonces ne sait exécuter une inscription** : mesuré en §5.5.
  C'est ce qui délimite le lot du dev.

## 2. Ce que le joueur fait

Le joueur ouvre l'hôtel des ventes. La fenêtre de dépôt **`SUIAuctionDepositWnd`** (nom de type RTTI en
ligne à `0xc1448c`, descripteur `0xc14484`, constructeur qui écrit sa vtable `0xa251cc` en `0x5019dd`)
héberge la sous-fenêtre d'inscription **`SUIAuctionRegisterWnd`** (nom en ligne à `0xc14ee4`,
descripteur `0xc14edc`, vtable `0xa25df4` écrite par le constructeur en `0x502fb5`). Les ressources
`window_system_auction_sub1..4.nui` (`SFrame.exe` `0xa25d14`, `0xa25cf4`, `0xa25cd4`, `0xa25cb4`) sont
celles des sous-fenêtres ; leurs contenus ne sont pas lisibles dans cet environnement (§7a).

Le geste mesuré, dans `SUIAuctionRegisterWnd` :

1. le joueur choisit un objet dans la liste d'objets — contrôle `item_list` (`0xa2541c`), créé par le
   constructeur en `0x502fef` sur le membre `this+0x4f8` ; les **données** de la liste sont sur le membre
   `this+0x4c8` (constructeur de liste appelé en `0x502fe5`) et la ligne choisie s'affiche dans le
   contrôle `item_selected` (`0xa25d88`, membre `this+0x558`, rempli par le gestionnaire `0x4fb520` →
   `0x5ba190` en `0x4fb58b`) ;
2. il saisit le **prix de départ** (`r_edit_01`, `0xa25568`, global `0xc147b8`) et le **prix d'achat
   immédiat** (`r_edit_02`, `0xa2555c`, global `0xc147bc`) ; les deux champs sont résolus par leur nom
   quand la fenêtre se met en place et rangés comme **pointeurs de contrôle** en `this+0x4e8` et
   `this+0x4ec` (`0x4f7860`-`0x4f7882`), avec `r_edit_03` en `this+0x4f0` (`0x4f7888`) et un bouton
   `button_common01_02` en `this+0x4f4` (`0x4f784d`-`0x4f785a`) ;
3. il coche **une des trois cases d'option de durée** — `common_radiobutton00` (`0xa25544`,
   global `0xc147c0`), `common_radiobutton01` (`0xa2552c`, global `0xc147c4`),
   `common_radiobutton02` (`0xa25514`, global `0xc147c8`) ;
4. il clique sur le bouton **`register`** (`0xa25804`) — c'est ce clic qui émet `1309` (§3.3).

Deux relevés utiles pour les cartes voisines :

* le bouton voisin `deregister` (`0xa25820`) **n'émet pas `1309`** : le même gestionnaire
  (`0x4fb923`-`0x4fb945`) appelle `0x4f8a50`, dont le corps construit à `0x4f8b16` un message interne
  `0x482` = **1154 = `REQ_AUCTION_SELLING_LIST`**, `page_num = 1` (`movl $0x1,0x13(%eax)` en `0x4f8b34`),
  enveloppe `0xa25730`. Ce que le clic de retrait fait d'autre n'est pas instruit ici : c'est le
  périmètre de la carte `1310`.
* le dépose-glisser est routé par la clé `UI_RECV_DROP` (`0xa25844`) lue dans la méthode `0x4fab50`
  (slot de vtable n° 1 de `SUIAuctionRegisterWnd`) ; sans rapport avec la trame de `1309`.

## 3. Structure sur le fil

### 3.1 Trame `TM_CS_AUCTION_REGISTER` — **32 octets**

| offset | taille | type | nom (rzu) | source |
|---|---|---|---|---|
| 0 | 4 | `int32` | `Length = 32` | client `0x48cb0f` (`movl $0x20,(%eax)`, initialiseur partagé `0x48cad0`) ; reconfirmé par `0x48df6e` (`movl $0x20,-0x20(%ebp)`) |
| 4 | 2 | `uint16` | id de trame = `0x51d` = **1309** | client `0x48cb04` (`mov $0x51d,%ecx`) puis `0x48cb09` (`mov %cx,0x4(%eax)`) |
| 6 | 1 | `uint8` | somme de contrôle des 6 premiers octets | client `0x48caeb` (`mov %cl,(%edx)`, `edx = frame+6` posé en `0x48cad2`) ; recalculée par le sender en `0x48df80`-`0x48df91` |
| **7** | 4 | `int32` (`ar_handle_t`) | `item_handle` | rzu `TS_CS_AUCTION_REGISTER.h:6` ; client `0x48df4a` (`frame+7 ← message+0x13`) |
| **11** | 4 | `int32` | `item_count` | rzu `:7` ; client `0x48df50` (`frame+11 ← message+0x17`) |
| **15** | 8 | `int64` | `start_price` | rzu `:8` ; client `0x48df56` et `0x48df5c` (`frame+15 ← message+0x1f`, `frame+19 ← message+0x23`) |
| **23** | 8 | `int64` | `instant_purchase_price` | rzu `:9` ; client `0x48df65` et `0x48df6b` (`frame+23 ← message+0x27`, `frame+27 ← message+0x2b`) |
| **31** | 1 | `uint8` | `duration_type` | rzu `:10` ; client `0x48df62` et `0x48df68` (octet `frame+31 ← message+0x2f`) |

Total : `7 + 25 = 32`. **C'est le seul paquet de la famille qui porte deux prix 64 bits et un octet de
durée.** Les huit relevés du socle §3.6 (`0x48df6e`, `0x48df4a`, `0x48df50`, `0x48df56`, `0x48df5c`,
`0x48df65`, `0x48df6b`, `0x48df62`, `0x48df68`) **tiennent** : ils ont été relus un par un ce réveil,
et l'ordre rzu (deux `int32` puis deux `int64` puis un `uint8`) est exactement celui des écritures.

### 3.2 Où le client construit la trame

Un seul site de construction dans tout `SFrame.exe` : la fonction `0x48df30`, dont le seul appelant est
le stub interne `0x49e3ae` (appel en `0x49e3b1`). Elle délègue son en-tête à l'initialiseur partagé
`0x48cad0` (appelé en `0x48df3c`), puis fait, dans l'ordre :

1. `0x48caed`-`0x48cb01` : remise à zéro de `frame+4` à `frame+0x1c` ;
2. `0x48cb04`/`0x48cb09` : l'id — `mov eax,0x51d` puis `mov [frame+4],ax` ;
3. `0x48cb0f` : la longueur — `movl $0x20,(%eax)` (l'initialiseur pose d'abord `7` en `0x48cad8` et
   calcule la somme de contrôle de l'en-tête nu, comme ses voisins `0x48ca70` pour `1306`) ;
4. `0x48df4a`-`0x48df6b` : les cinq champs, lus **dans le message interne** (`frame+7` ← `message+0x13`,
   `frame+11` ← `+0x17`, `frame+15`/`+19` ← `+0x1f`/`+0x23`, `frame+23`/`+27` ← `+0x27`/`+0x2b`,
   `frame+31` ← `+0x2f`) ;
5. `0x48df6e` : la longueur, réécrite sur la pile (`movl $0x20,-0x20(%ebp)`) ;
6. `0x48df80`-`0x48df91` : la somme de contrôle, recalculée sur `frame+0..5` et posée en `frame+6` ;
7. `0x48df8a`-`0x48df9e` : l'envoi par la vtable de connexion (`[conn+0xb8]` → slot `+0xc4`).

**Les 4 octets réservés du message interne** : le message porte un `dword` en `message+0x1b`, entre
`item_count` (`+0x17`) et `start_price` (`+0x1f`), que le sender **saute**. Il est bien renseigné côté
client (`0x4fa7d0`, cf. §3.4) et il **n'atteint pas le fil** : la trame reste de 32 octets, sans trou.
À écrire comme tel pour que personne n'ajoute un champ au lecteur.

### 3.3 Le chemin complet, du geste à la trame (rejouable)

1. **Geste** : clic sur le bouton `register` (`0xa25804`) → gestionnaire de notification `0x4fb820`,
   qui compare le nom du contrôle (`0x4fb96b`-`0x4fb979`) et appelle l'émetteur (`0x4fb998`).
2. **Émetteur** : méthode `0x4fa6e0` de `SUIAuctionRegisterWnd` (c'est un membre non virtuel de la
   classe : le même `this` porte `item_list` en `+0x4f8`, la liste de données en `+0x4c8`, les deux prix
   analysés en `+0x4e8`/`+0x4ec` et l'indice de durée en `+0x4b0`, tous initialisés par le constructeur
   `0x502f90`). Il lit les champs (§3.4), réserve le message (0x30 octets, `0x4fa7a8`, `0x4fa7b0`) et
   le construit par `0x4f2600` (`0x4fa7be`).
3. **RTTI du message** : la vtable `0xa25748` est précédée en `0xa25744` du localisateur `0xbbd3d4`
   (`signature 0`, `offset 0`, `cdOffset 0`), dont le `pTypeDescriptor` vaut `0xc148f0` ; le nom du type
   est **en ligne** à `0xc148f8` = `.?AUSIMSG_REQ_AUCTION_REGISTER@@`. C'est bien le symbole que le socle
   §3.6 cite, et c'est la preuve que l'émetteur construit *ce* message et pas un homonyme.
4. **Table de dispatch interne** : la fonction `0x49e21d` fait `sub eax,0x403`, bornage, puis
   `movzx eax,BYTE PTR [eax+0x49ea50]` et `jmp DWORD PTR [eax*4+0x49e98c]`. Pour la clé **1160**
   (`0x488`) l'octet `0x49ea50[133]` vaut `0x26` = 38, et l'entrée 38 de la table de sauts
   (`0x49ea24`) vaut `0x49e3ae` — le stub de `1309`. Les trois clés de réponse de la même bande
   (1153, 1155, 1157) portent toutes l'octet `0x30`, le cas « pas pour cette fenêtre ».
5. **Stub → message → trame** : `0x49e3ae` (`push %edi` ; `mov %esi,%ecx` ; `call 0x48df30`) enfile la
   trame. Le message interne est lui-même mis en file par `0x649e20` sur l'objet `[this+0x440]`
   (`0x4fa812`-`0x4fa819`).

Les clés internes de la famille, mesurées par la même méthode (elles fixent aussi le contexte de `1310`) :

| clé interne | octet `0x49ea50` | stub | sender | trame |
|---|---|---|---|---|
| 1152 (`0x480`) | `0x21` | `0x49e36d` | `0x48dc80` | 1300 |
| 1154 (`0x482`) | `0x22` | `0x49e37a` | `0x48dd10` | 1302 |
| 1156 (`0x484`) | `0x23` | `0x49e387` | `0x48dda0` | 1304 |
| 1158 (`0x486`) | `0x24` | `0x49e394` | `0x48de30` | 1306 |
| 1159 (`0x487`) | `0x25` | `0x49e3a1` | `0x48dea0` | 1308 |
| **1160 (`0x488`)** | **`0x26`** | **`0x49e3ae`** | **`0x48df30`** | **1309** |
| 1161 (`0x489`) | `0x27` | `0x49e3bb` | `0x48dfc0` | 1310 |
| 1162 (`0x48a`) | `0x28` | `0x49e3c8` | `0x48e050` | 1350 |

La clé `1160` se lit aussi sur l'objet du message (`movl $0x488,0x4(%eax)` en `0x4f2604`), ce qui est le
contrôle indépendant du tableau ci-dessus.

### 3.4 Ce que contient chaque champ, et d'où il vient

L'émetteur `0x4fa6e0`, dans l'ordre exact du code :

* `0x4fa6eb`-`0x4fa6fb` : `0x5b9260` lit la cellule **`icon`** (`0xa253e4`) de la ligne sélectionnée
  dans la liste `this+0x4c8` ; la valeur est le `dword` en `+0x20` de la cellule → **`item_handle`** ;
* `0x4fa6fe`-`0x4fa712` : cette valeur est cherchée dans la table d'objets du personnage
  (`this+0x484` → `0x4a4c40`, qui compare `[objet+8]`) ; **si rien ne correspond, rien n'est émis** ;
* `0x4fa718`-`0x4fa722` : `0x4f94b0(this, valeur)` est le **test de registrabilité** du client ; s'il
  est faux, rien n'est émis. Mesuré : il résout la ressource d'objet par `[objet+0xc]`
  (`0x4f94d9`-`0x4f9500`) puis refuse sur les bits `0xa00` de `+0x66` et `0x8` de `+0x6c`
  (`0x4f951b`, `0x4f9524`) — les noms de ces bits ne sont pas établis, mais ce sont bien des refus
  (objet lié / non échangeable), pas des erreurs de saisie ;
* `0x4fa728`-`0x4fa747` : les **pointeurs** des deux champs de saisie (`this+0x4e8` = `r_edit_01`,
  `this+0x4ec` = `r_edit_02`, cf. §2) sont passés à `0x501f70(contrôle, &int64)`, qui **lit le texte du
  contrôle et l'analyse directement en `int64`** (la paire 64 bits est écrite en `(%ecx)`/`0x4(%ecx)` en
  `0x501fac`/`0x501fb1`) → `start_price` et `instant_purchase_price` ;
* `0x4fa74b`-`0x4fa761` : `0x5b9260` lit la cellule **`item_count`** (`0xa253d8`) ; `+0x20` donne
  `item_count` (fil) et `+0x24` le `dword` réservé non transmis ;
* `0x4fa779`-`0x4fa7ad` : un descripteur local de 8 `dword` (code d'objet, les deux `dword` de la
  cellule `item_count`, les deux prix) est écrit dans le conteneur `this+0x5b8` par `0x4f5670` ;
* `0x4fa80f` : `duration_type` (voir §3.5), puis mise en file et réinitialisation locale (§5.3).

Conséquence pour le serveur : les deux prix sont **relus dans les champs de saisie au moment du clic**
(rien n'est mémorisé entre-temps) et ils sont analysés **directement en `int64`** : il n'y a, dans le
chemin lu, aucun rétrécissement à `int32`. La plage réellement atteignable dépend de ce que le contrôle
accepte comme texte, ce qui n'est pas mesurable ici (§7b) : ne pas supposer un plafond à `2^31`.

### 3.5 `duration_type` — la valeur est mesurée, la durée ne l'est pas

Le client n'envoie **que 1, 2 ou 3**, et la correspondance avec les trois cases d'option est mesurée
de bout en bout :

1. clic sur `common_radiobutton00` (`0x4fb5b4`) → `push %eax` avec `eax = 0` → `0x4f21d0(this, 0)` ;
   clic sur `common_radiobutton01` (`0x4fb5d4`) → `push $0x1` ; clic sur `common_radiobutton02`
   (`0x4fb5fa`) → `push $0x2` ;
2. `0x4f21d0` refuse tout indice `> 2` (`0x4f21d3`), décoche les trois contrôles du groupe
   (`0x4f21de`-`0x4f2201`, membres `this+0x4dc` à `this+0x4e4`), coche celui qui est choisi
   (`0x4f2206`-`0x4f2213`) puis **stocke l'indice en `this+0x4b0`** (`0x4f222d`, `mov %ecx,0x4b0(%esi)`) ;
3. à l'émission, `0x4fa7eb`-`0x4fa80f` traduit cet indice : `0 → 1`, `1 → 2`, `2 → 3`, **toute autre
   valeur → 1** (`sub $0x0`/`je`, `dec`/`je`, `dec`/`jne`, repli `mov $0x1` en `0x4fa80a`) ;
4. le constructeur de la fenêtre met `this+0x4b0` à `0` (`0x502fcd`) : **si le joueur ne touche à aucune
   case, le client envoie quand même `1`.**

Donc l'énumération *émise* est `{1, 2, 3}`, avec `1` par défaut, et `0` n'est jamais émis. Ce que
chaque valeur **vaut en durée** (heures, jours) n'est pas dans le binaire : les libellés des trois cases
sont dans les `.nui` non lisibles ici (§7a), et le serveur ne peut pas le déduire de la trame. Le seul
lien prouvable côté dépôt est la colonne `EndTime` du modèle d'annonce
(`Game/DataAccess/Entities/Telecaster/AuctionEntity.cs:20`) et le commentaire de `RegistrationTax`
(`:22`, « short, mid, longterm ») — c'est une **décision de Killian**, §8 question 1.

## 4. Gating de version, tranché pour 7.3

| champ / identifiant | gating rzu | décision pour 7.3 | raison |
|---|---|---|---|
| id du paquet | `X(1309, version < EPIC_9_6_3)`, `X(2309, version >= EPIC_9_6_3)` (`TS_CS_AUCTION_REGISTER.h:13-14`) | **1309** | 7.3 (`EPIC_7_3 = 0x070300`, `rzu .../PacketEpics.h:59`) est **antérieur** à `EPIC_9_6_3` (`0x090603`, `:96`) : la branche `<` gagne. **`2309` n'existe pas en 7.3 et ne doit jamais être déclaré** — le client 7.3 n'a qu'un site pour `0x51d` (§3.2) |
| `item_handle` | aucun (`_(simple)`) | 4 octets `int32` à l'offset 7, lu `uint32` dans le dépôt (même 4 octets) | rzu ne gate pas le champ ; le client écrit un `dword` plein |
| `item_count` | aucun | 4 octets `int32` à l'offset 11 | idem |
| `start_price`, `instant_purchase_price` | aucun | 8 octets `int64` aux offsets 15 et 23 | idem |
| `duration_type` | aucun | 1 octet à l'offset 31 | aucun `X(...)` de champ ; seul le domaine des valeurs est contraint par le client (§3.5) |
| sens | `SessionPacketOrigin::Client` | client → serveur | cohérent avec l'opcode et le site d'émission |

**Rien, dans le fichier de rzu, n'est conditionné par une version hormis l'identifiant** : la trame de
`1309` est la même avant et après `EPIC_9_6_3`. C'est exactement le cas de `1308` et le contraire de
`1300`, dont le champ `is_equipable` apparaît en `EPIC_7_2`. **Aucun piège de taille ne se cache
derrière ce gating** pour ce paquet, et l'alias tardif `2309` est le seul à refuser.

**Écart de signedness, tranché** : rzu déclare `uint8_t duration_type`, NGemity `int8_t`. Les deux
valent 1 octet et le client n'émet que des valeurs positives ; le dépôt retient **`byte`** (c'est déjà
le type de `AuctionInfo.DurationType`, le champ homologue des trois réponses, écrit en `packet+79`
d'une entrée de 96 octets). L'écart est sans effet sur le fil ; il est écrit ici pour que le dev n'ouvre
pas un débat de signe.

## 5. Traitement attendu

### 5.1 NGemity déclare `1309` et ne le gère pas — il n'y a rien à porter

* NGemity **déclare** la trame (`shared/Server/Packets/GameClient/TS_CS_AUCTION_REGISTER.h:6-11`), la
  `CREATE_PACKET` sans gating (l. 13), l'inclut (`shared/Server/XPacket.h:58`) et l'énumère
  (`shared/Server/ClientPackets.h:217`).
* NGemity **n'a aucun handler** :
  `grep -rni auction reference/ngemity/Chihiro/src/` ne rend que
  `Entities/Item/ItemTemplate.hpp:216` (`BY_AUCTION = 12`), `Entities/Item/Item.cpp:91,127` et
  `Entities/Item/ItemInstance.h:36,59,81,103` (`m_nAuctionID`, la colonne `auction_id` d'un objet) et
  `Map/WorldLocation.h:50` (`SecRouteAuction = 130107`, identifiant de routage de zone). Rien à porter.
* NGemity compile `EPIC_4_1_1` (socle §7.4) : pour `1309` cela ne change rien, la trame est la même de
  part et d'autre de `9.6.3` (§4).
* La logique d'acceptation ou de refus d'une inscription ne peut donc venir **que** du client (§3.4)
  et du modèle du dépôt (§5.5). C'est un fait, pas un obstacle.

### 5.2 Le lecteur, à ajouter dans `GameAuctionPackets`

Rien n'existe aujourd'hui (`grep -rn "TryReadAuction" --include=*.cs Game/ Tests/` vide). La convention
à suivre est celle du lot `1308` et, sur `master`, celle de
`GameActionPackets.TryReadSummonCardSkillList` (`Game/Network/Packets/Game/GameActionPackets.cs:87`),
qui est le patron le plus proche — une trame de taille fixe, un seul `uint32` en `+7`, refus si la
longueur n'est pas exactement la bonne :

```csharp
public static bool TryReadAuctionRegister(ReadOnlySpan<byte> packet, out uint itemHandle,
    out int itemCount, out long startPrice, out long instantPurchasePrice, out byte durationType)
```

* refuser toute trame dont la longueur **n'est pas exactement 32** (ni courte, ni complétée) : c'est la
  discipline du dépôt, et `TryReadSummonCardSkillList` la formalise ;
* `item_handle` en `packet[7..11]`, lu `uint32` (§4) ;
* `item_count` en `packet[11..15]`, lu `int32` ;
* `start_price` en `packet[15..23]` et `instant_purchase_price` en `packet[23..31]`, lus `int64`
  **little-endian** (`BinaryPrimitives.ReadInt64LittleEndian`) ;
* `duration_type` en `packet[31]`, et **rien après** : l'octet 31 est le dernier de la trame ;
* aucun champ inventé : il n'y a ni page, ni motif d'objet, ni identifiant d'annonce dans cette trame.

### 5.3 Ce que le client attend en retour — et ce qu'il fait juste après

**Prouvé.** `1309` n'a **aucun paquet dédié** en réponse (socle §5.3, ligne `1309`, reconfirmé ici : la
famille n'a que trois trames serveur → client, 1301/1303/1305). Ce que le client 7.3 fait de l'accusé de
réception générique, mesuré :

1. le handler de `TM_SC_RESULT` (id 0, `0x66db80`) construit son message interne puis dispatche sur
   `request_msg_id` (`movzwl 0x7(%esi),%eax`) : après `cmp $0x119` et `sub $0x5`, la chaîne de
   comparaisons `sub $0x51c,%eax` / `0x66e04a` → branche `1308`, `dec %eax` / `0x66e052` → **branche
   `1309` en `0x66e07c`**, `dec %eax` / `0x66e054` → branche `1310` en `0x66e05b` ;
2. la branche `1309` (`0x66e07c`-`0x66e098`) lit le champ *result* (`movzwl 0x9(%esi)`) et formate une
   ligne de journal : le libellé est la chaîne coréenne à `0xa524d4`, **`경매 등록    요청결과-%s[%d]`**
   (« inscription à l'hôtel des ventes — résultat de la requête »). C'est le seul traitement dédié ;
3. elle rejoint la queue commune `0x66e279`, qui **enfile un message interne `.?AUSMSG_RESULT@@`**
   (vtable `0xa51ee8`, id `4` en `+4`, posé en `0x66db9a`/`0x66dbb2` ; `request_msg_id` recopié en `+0x13`
   en `0x66dbd0`) sur la file `[objet+0x2c]` par `0x64d0e0` (`0x66e283`). Aucun `1309` n'est réémis,
   aucune liste n'est redemandée depuis ce chemin.

**Prouvé aussi, côté émission :** juste après avoir mis la trame en file, l'émetteur rafraîchit son
affichage et **vide sa liste d'objets** — `0x4fa81e`-`0x4fa82c` (`0x4f89f0` sur `this+0x5c8`) puis la
queue commune `0x4fa831`-`0x4fa84a` qui appelle `0x4f9800(this, NULL, 0)` — la même queue étant
atteinte **aussi** en cas de refus local (§3.4). Le client ne conserve donc localement aucun état de
l'inscription : il n'y a pas de contrepartie à attendre côté client pour lui faire « voir » sa nouvelle
annonce.

**Conséquence pour le serveur** (décision, pas mesure) : après une inscription acceptée, c'est le
**serveur** qui doit pousser une liste rafraîchie (`1303`, avec `page_num` réémis) — le client ne la
redemande pas, et il n'a aucun handler serveur → client de la famille hors 1301/1303/1305. Un refus
passe par `SendResult` (`Game/Network/Clients/GameClient.cs:68`, `TM_SC_RESULT = 0` avec
`request_msg_id = 1309`) : c'est précisément ce que le client sait afficher (§5.3.2).

### 5.4 Ce que le serveur doit répondre, paquet par paquet

| cas | réponse |
|---|---|
| trame bien formée, valeurs dans le domaine mesuré | `SendResult(1309, ResultCode.Success)` une fois l'inscription appliquée, puis **un `1303` rafraîchi** (ou rien, tant que l'exécution n'existe pas — §5.5) |
| trame de longueur ≠ 32, `duration_type` hors `{1,2,3}`, prix négatifs, `item_count ≤ 0` | `SendResult(1309, ResultCode.InvalidArgument)` ; ne rien appliquer, ne pas répondre de liste |
| objet introuvable / non registrable / déjà en vente | `SendResult(1309, ResultCode.<à trancher, §8 question 5>)` |
| aucune inscription exécutable (§5.5) | journaliser la trame décodée et **ne rien envoyer** : c'est le « cas pas de code » partiel, §5.7 |

### 5.5 Ce que le serveur peut exécuter aujourd'hui : rien — et c'est mesuré

* `grep -rn "\.Auctions" --include=*.cs Game/` (hors migrations) est **vide** : `DbSet<AuctionEntity>
  Auctions` est déclaré (`Game/DataAccess/Contexts/TelecasterContext.cs:11`, configuré en `:146-169`)
  mais **jamais lu ni écrit** par un service ou un dépôt.
* Il n'existe **aucun `AuctionRepository`** : la liste de `Game/DataAccess/Repositories/` ne porte que
  `AuctionCateryResourceRepository.cs` (les catégories, lues par le socle, prérequis `S3`).
* `grep -rn -i "auction" --include=*.cs Game/Services/` ne rend que `StorageRules.cs` (lignes 100-125),
  qui exclut les lignes d'enchère d'un calcul de stockage. Ce n'est pas une mécanique d'inscription.
* Les entités existent et suffisent à *décrire* une inscription (`AuctionEntity` :
  `ItemId`, `SellerId`, `SellerName`, `IsHiddenVillageOnly`, `EndTime`, `InstantPurchasePrice`,
  `RegistrationTax`, `BiddersIds`, `HighestBiddingPrice`, `HighestBidderId`, `HighestBidderName`,
  `ItemStorage` ; `ItemStorageEntity.RelatedAuctionId:20` ; les neuf `StorageType` :
  `ItemBySuccessfulBid = 1`, `ItemByInstantPurchase = 2`, `ItemByExpiration = 3`, `ItemByCancel = 4`,
  `GoldByItemSell = 30`, `GoldByRegTax = 31`, `GoldByHigherBid = 32`, `GoldByCancel = 33`,
  `GoldByItemSoldOut = 34`). **Elles ne prouvent pas l'exécution** : rien ne calcule `EndTime` à partir
  d'un `duration_type`, rien ne prélève `RegistrationTax`, rien ne déplace l'objet vers le stockage
  d'enchère (`ItemStorageEntity` + `StorageType`), rien ne plafonne le nombre d'annonces.
* Conclusion : le lot du dev s'arrête au **décodage borné + journalisation + refus** ; exécuter une
  inscription demande les décisions du §8, qui ne sont dans aucune source locale.

### 5.6 Le lecteur, le membre et le bras de dispatch

`master` place ses bras sous forme de `if (header.ID == (ushort)GamePackets.X) { …; continue; }`
au-dessus du `switch` final qui jette (`_ => throw new Exception($"Unknown Packet Type {header.ID}")`).
Le membre d'énumération et le bras doivent être ajoutés **ensemble** (critère 4) :

* membre : `TM_CS_AUCTION_REGISTER = 1309`, dans la bande des enchères de `GamePackets.cs:156-158`,
  avec le commentaire de gating au patron de `TM_CS_SUMMON_CARD_SKILL_LIST` (`:116-122`) — c'est-à-dire
  en disant explicitement que `2309` ne doit pas être déclaré ;
* bras : `HandleAuctionRegister(byte[] buffer)`, placé à côté des autres bras de la famille ; il appelle
  le lecteur, envoie `ResultCode.InvalidArgument` sur une trame refusée (§5.4) et journalise la trame
  décodée sinon. **Un membre d'énumération sans bras atteindrait le `throw` final** : c'est le seul
  critère transversal que ce lot peut casser, et il se teste.

### 5.7 Le « cas pas de code » : partiel, et nommé

La trame est entièrement fixée et le critère 4 **impose** le bras de dispatch dès que le membre existe :
ce lot **ne peut pas** être un « 4008 » (MR sans code). Mais il ne peut pas non plus exécuter
l'inscription (§5.5). Ce qui est livrable sans aucune décision de Killian :

1. le membre `TM_CS_AUCTION_REGISTER = 1309` et son bras de dispatch ;
2. le lecteur borné à 32 octets, avec les cinq champs aux offsets du §3.1 ;
3. la journalisation de la trame décodée (objet, quantité, les deux prix, `duration_type`) ;
4. `SendResult(1309, ResultCode.InvalidArgument)` sur une trame mal formée ;
5. les tests d'offsets (§13, critère 3).

Ce qui n'est livrable **que** contre une décision du §8 : l'écriture de la ligne `AuctionEntity`, le
calcul de `EndTime`, la taxe, le plafond d'annonces, le déplacement de l'objet, et le contenu des
`ResultCode` de refus métier. Le dev doit livrer 1 à 5 et s'arrêter là, en le disant dans sa §14.

## 6. Écarts assumés avec NGemity

| point | NGemity | dépôt / rzu | traitement |
|---|---|---|---|
| identifiant | `CREATE_PACKET(..., 1309)`, sans gating | `X(1309, < 9.6.3)` / `X(2309, >= 9.6.3)` | **1309** ; NGemity compile `EPIC_4_1_1`, il ne peut donc pas trancher `2309` — la décision vient de `PacketEpics.h` (§4) |
| `duration_type` | `int8_t` | `uint8_t` | 1 octet dans les deux cas ; le dépôt écrit `byte` (§4) |
| `item_handle` | `uint32_t` | `ar_handle_t` (rzu `typedef`, 4 octets) | même largeur ; le dépôt lit `uint32` |
| logique | aucun handler (déclarations seulement) | — | rien à porter ; la validation vient du client (§3.4) et le refus du dépôt (§5.4) |
| schéma de données | table `Auction` de `Database/Telecaster.sql` (déjà relevée par la fiche `1308` §5.1) | `AuctionEntity` + `ItemStorageEntity` | le schéma NGemity recoupe le modèle du dépôt ; il n'ajoute rien pour `1309` |

Aucun écart de **trame** : rzu et le client mesuré donnent les mêmes 32 octets dans le même ordre.

## 7. NON ÉTABLI

Les points ci-dessous sont des **questions ouvertes**, pas des valeurs à deviner. Aucun champ de la
trame n'est concerné : la trame est entièrement établie (§3.1).

* **7a. La durée que porte chaque valeur de `duration_type`.** Mesuré : le client n'émet que 1, 2 ou 3,
  par trois cases d'option, et `1` par défaut (§3.5). **Non établi** : à combien de temps correspond
  chacune, et dans quelle unité. Les libellés des trois cases sont dans les `.nui`
  (`window_system_auction_sub1..4.nui`), qui ne sont pas lisibles ici : `reference/client73/` n'en
  contient aucun, et `data.000` ne porte en clair ni `window_system_auction_sub`, ni
  `common_radiobutton`, ni `limittime` (recherche faite sur les 3 646 701 octets du fichier). Question
  précise : *`duration_type = 1/2/3` vaut combien de temps, et la valeur `0` (jamais émise par le
  client) doit-elle être acceptée comme « non précisé » ou refusée ?*
* **7b. La plage des deux prix.** Mesuré : les deux valeurs sont analysées en `int64` depuis le texte
  des champs de saisie au moment de l'émission (§3.4), sans borne visible dans le code lu autour des
  deux contrôles. **Non établi** : le nombre de chiffres que le champ accepte (donc la plage réellement
  atteignable), et si le client accepte `0`, un prix négatif, ou un prix d'achat immédiat inférieur au
  prix de départ. Le client **envoie** ces trames ; il ne les filtre pas.
* **7c. La valeur `0` de `item_count`.** Mesuré : `item_count` vient de la colonne `item_count` de la
  ligne sélectionnée (§3.4), sans contrôle visible dans l'émetteur. **Non établi** : le client peut-il
  envoyer `0` (ou négatif) si la liste affiche une ligne vide. Question précise : *le lecteur doit-il
  refuser `item_count ≤ 0`, ou le laisser au service d'inscription ?*
* **7d. Le `dword` interne `message+0x1b`.** Mesuré : il est renseigné (`0x4fa7d0`, deuxième `dword` de
  la cellule `item_count`, `+0x24`) et **n'est pas transmis** (§3.2). **Non établi** : ce qu'il porte
  (identifiant d'instance ? index de ligne ?). Sans effet sur la trame : à ne pas chercher à lire.
* **7e. La réaction du client à l'`AUSMSG_RESULT` enfilé en `0x66e279`.** Mesuré : le message est
  enfilé sur `[objet+0x2c]` par `0x64d0e0` avec `request_msg_id = 1309` en `+0x13` (§5.3). **Non
  établi** : quelle fenêtre le consomme et ce qu'elle en fait. Indice mesuré, non concluant : le
  gestionnaire de `SUIAuctionRegisterWnd` porte une branche qui compare `0x13(%msg)` à `0x516` = **1302**
  avec `result == 1` et qui vide alors la liste `this+0x4f8` (`0x4fb8d1`-`0x4fb8f1`) — donc un
  rafraîchissement *existe*, mais pour la réponse à `1302`, pas pour `1309`.
* **7f. La valeur de `status` des réponses** (socle §8.1) n'est pas de mon lot, mais elle touche
  l'inscription : une annonce qui vient d'être inscrite doit apparaître dans `1303` avec un `status`
  dont l'énumération n'est pas établie. À ne pas inventer ici.
* **7g. `1307` reste le trou documenté du socle** : ni dans le client (aucun site mesuré), ni dans
  `op_codes.md`. Cette fiche ne le déclare pas et ne l'invente pas.

## 8. A VERIFIER PAR KILLIAN

Décisions qui ne sont dans **aucune** source locale (ni rzu, ni NGemity, ni le client, ni le dépôt).
Chaque question porte ce qui est mesuré, pour qu'aucune réponse n'ait à être devinée par le dev.

1. **Durées.** `duration_type = 1 / 2 / 3` (mesuré, §3.5) vaut quoi ? Ordre croissant
   (`1` court → `3` long) ? Unité ? Et `0`, jamais émis, est-il refusé ? *(Socle §9 rang 2 : cette
   réponse conditionne `1306`, `1308` et l'expiration ; `EndTime` en découle.)*
2. **Taxe d'inscription.** `RegistrationTax` existe dans le modèle
   (`AuctionEntity.cs:22`, avec le commentaire « short, mid, longterm => decimal * factor 10000 »),
   et `StorageType.GoldByRegTax = 31` nomme la destination de l'or. Quel barème, prélevé quand, et
   sur quel prix (départ ou achat immédiat) ?
3. **Bornes de prix.** Prix de départ minimal (`socle §6.3` rang 3), prix maximal, `instant_purchase_price`
   facultatif (`0` = pas d'achat immédiat ?), et relation d'ordre entre les deux.
4. **Plafonds et visibilité.** Nombre maximal d'annonces par personnage et par compte ;
   `IsHiddenVillageOnly` (modèle) — réservé aux villages cachés ou laissé à `false` ?
5. **`ResultCode` de refus.** Lesquels, et pour quels cas : objet introuvable, objet lié / non
   échangeable (le client refuse déjà, §3.4 — c'est donc une trame forgée), quantité invalide, prix
   hors bornes, plafond atteint, or insuffisant pour la taxe, `duration_type` hors domaine.
   Le dépôt a `ResultCode.Success` / `InvalidArgument` ; le vocabulaire métier est à trancher.
6. **Qui rafraîchit la liste après une inscription acceptée.** Mesuré : le client ne redemande rien
   (§5.3). Le serveur doit-il pousser un `1303` complet (3899 octets) après un `SendResult` de succès,
   ou attendre que le joueur rouvre l'onglet ?
7. **Horloge de l'expiration.** `EndTime` se calcule-t-il à l'horodatage de réception de `1309`, et
   l'expiration est-elle jugée par un balayage périodique (aucun service de ce genre n'existe : §5.5) ?

Aucune de ces questions ne bloque le lot du §5.7 (membre + bras + lecteur + journal + refus) : elles
bloquent toutes l'**exécution** de l'inscription.

## 9. Commits et binaires épinglés

Vérifiés dans cet environnement ce réveil (`git -C <référence> rev-parse HEAD`, `sha256sum`), puis
recopiés — ce sont ceux du réveil du PO (`2026-09-28 09:00 CEST`).

| référence | empreinte | vérification |
|---|---|---|
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02, « packets: fix TS_SC_INVENTORY with older epics ») | `git -C /srv/navislamia/reference/rzu rev-parse HEAD` — identique |
| NGemity | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03, « Fix compilation issue for GCC ») | `git -C /srv/navislamia/reference/ngemity rev-parse HEAD` — identique |
| client 7.3 | `reference/client73/SFrame.exe`, `sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | `sha256sum` — identique. `reference/client73/` **n'est pas un dépôt Git** : aucune révision à citer, seul le binaire est opposable |
| NavisLamia | `origin/master = b56967a07430422add88e0e5cdf292b41b18f6c6` | `git rev-parse origin/master` — identique ; `git log --oneline origin/master..master` **vide** (aucun commit sur `master`) |

Méthode de lecture statique du client (socle §11, rejouée telle quelle, **aucune méthode nouvelle**) :

1. tailles et ordre des champs : en-tête rzu et sites d'écriture du constructeur de trame ;
2. identifiant du message interne : symbole RTTI du type (`locator.ptd + 8`) ;
3. chemin du geste : clé interne → table d'octets `0x49ea50` → table de sauts `0x49e98c` → stub → sender ;
4. champs : sites d'écriture du message (`0x4f26xx`/`0x4fa7xx`) puis sites de recopie vers la trame
   (`0x48df4a`-`0x48df6b`) ;
5. contrôle indépendant par les chaînes : les noms de contrôles et de colonnes (`icon`, `item_count`,
   `register`, `common_radiobutton00/01/02`) sont lus dans `.rdata` et les références croisées
   (`push $<adresse>`, `mov <global>,%reg`) situent chaque contrôle dans une classe de fenêtre.

Conversions d'adresses utilisées (sections PE) : `.text` → `va - 0x400c00`, `.rodata` → `va - 0x401200`,
`.rdata` → `va - 0x401600`, `.data` → `va - 0x401e00`.

## 10. Bloc destiné à `CLAUDE.md`

À reprendre dans la description de la MR par `navis-dev` (Hermes protège `CLAUDE.md` : le dev ne
l'écrit pas). Le socle pointe déjà vers `docs/packet-specs/` ; ce bloc ne remplace pas la fiche.

```markdown
### `TM_CS_AUCTION_REGISTER` (1309) — inscrire une annonce

Fiche : `docs/packet-specs/1309-auction-register.md` (branche `hermes/packet-1309-auction-register`).

- **32 octets**, la trame la plus large de la famille : en-tête 7, `item_handle` `uint32` @7,
  `item_count` `int32` @11, `start_price` `int64` @15, `instant_purchase_price` `int64` @23,
  `duration_type` `uint8` @31. Le message interne du client réserve 4 octets de plus entre
  `item_count` et `start_price` (il renseigne `message+0x1b` et le sender le saute) : ce trou
  **n'existe pas** sur le fil, la trame reste contiguë. Ne pas ajouter de champ au lecteur.
- **`duration_type` vaut 1, 2 ou 3, et rien d'autre.** Mesuré de bout en bout : trois cases d'option
  (`common_radiobutton00/01/02`, membres `this+0x4dc..0x4e4` de `SUIAuctionRegisterWnd`) écrivent
  l'indice `0/1/2` en `this+0x4b0` (`0x4f21d0`, `0x4f222d`), et l'émetteur le traduit en `indice + 1`
  (`0x4fa7eb`-`0x4fa80f`, repli `1`). `0` n'est jamais émis ; le constructeur initialise `this+0x4b0`
  à `0` (`0x502fcd`), donc **sans clic sur une case le client envoie `1`**. Ce que chaque valeur vaut
  en durée est une décision, pas une mesure (fiche §8 question 1).
- **Piège d'offsets, à ne pas confondre** : `+0x4b0` est un **indice de durée** dans
  `SUIAuctionRegisterWnd`, mais un **pointeur de contrôle** (`edit_bid`) dans la classe qui porte
  `search_edit`/`edit_bid` (`0x4fc330`-`0x4fc383`), et les constructeurs des deux fenêtres
  d'enchères (`SUIAuctionDepositWnd` `0x5019dd`, `SUIAuctionRegisterWnd` `0x502fb5`) partagent la
  moitié de leurs décalages (`+0x4a0`, `+0x4a4`, `+0x4a8`, `+0x4b0`, et un contrôle `item_list` —
  respectivement en `+0x4b8` et `+0x4f8`). Identifier la classe par le décalage de son contrôle
  `item_list`, jamais par son numéro de décalage seul. Même leçon pour les prix : `+0x4e8`/`+0x4ec`
  sont des **pointeurs vers `r_edit_01`/`r_edit_02`** dans `SUIAuctionRegisterWnd` (`0x4f7860`,
  `0x4f7874`), les deux prix étant **analysés au clic** par `0x501f70` — ce ne sont pas des `int32`
  mémorisés.
- **Le client n'attend aucun paquet dédié** : la famille n'a que trois trames serveur → client
  (1301/1303/1305). La réponse est `TM_SC_RESULT` (0) avec `request_msg_id = 1309`, dont la branche
  `0x66e07c` se contente de journaliser ; le client vide sa liste d'objets après l'envoi
  (`0x4fa831`-`0x4fa84a`) et **ne redemande pas** la liste des ventes. Un rafraîchissement de `1303`
  est donc à la charge du serveur.
- **Aucun service ne peut exécuter une inscription** sur `master` : `DbSet<AuctionEntity> Auctions`
  (`TelecasterContext.cs:11`) n'est jamais utilisé, il n'existe pas d'`AuctionRepository`, et
  `grep -rn -i "auction" --include=*.cs Game/Services/` ne rend que `StorageRules.cs` (exclusion des
  lignes d'enchère). Le lot livre le décodage borné, la journalisation, le refus `InvalidArgument` et
  le bras de dispatch ; l'écriture de l'annonce attend les décisions de la fiche §8.
- **Ordre de reprise** : `1309` est le prérequis nommé de `1306`, `1308` et `1310` (socle §6.3 rangs
  4, 5, 6). `2309` ne doit **jamais** être déclaré (rzu ne remappe qu'à partir d'`EPIC_9_6_3`).
```

## 11. Références croisées

* **Socle** : `docs/packet-specs/socle-encheres.md` — §3.6 (la trame, confirmée ici champ par champ),
  §4.2 (les identifiants pour 7.3), §5.3 (ligne `1309` : aucun paquet dédié), §5.5 (le modèle
  d'annonce et les neuf `StorageType`), §6.2/§6.3 (le découpage, l'ordre de reprise), §8.4 et §9
  (les décisions ouvertes, dont `duration_type` au rang 2), §11 (méthode de lecture).
* **`CLAUDE.md:1202-1230`** : le bloc « Enchères » du socle, qui acte que les sept paquets
  client → serveur restent à implémenter, chacun avec son bras de dispatch.
* **Fiches sœurs**, toutes sur leur branche et **non mergées** (`master` ne porte que le socle) :
  `hermes/packet-1300-auction-search` (MR #65), `hermes/packet-1302-auction-selling-list` (#66),
  `hermes/packet-1304-auction-bidded-list` (#67), `hermes/packet-1306-auction-bid` (#68),
  `hermes/packet-1308-auction-instant-purchase` (#69, la plus proche : conventions de lecteur et de
  bras, patron de la §14). Le lot `1309` est le **sixième** de la famille et le seul des sept qui
  **crée** l'objet de l'enchère.
* **`1310`** (`TM_CS_AUCTION_CANCEL`) partage le même prérequis `1309` (socle §6.3) ; le §2 relève que
  son bouton `deregister` demande une liste (`1154` → `1302`) avant de faire autre chose — à reprendre
  par cette carte, pas ici.
* **`GamesActionPackets.TryReadSummonCardSkillList`** (`Game/Network/Packets/Game/GameActionPackets.cs:87`)
  et son bras (`GameClient.cs:1525-1529`) : le patron de lecteur borné + bras « lire, journaliser,
  ne rien répondre » du dépôt.
* **Tests** : `Tests/Game/AuctionPacketsTests.cs` (16 assertions d'offsets du motif de 75 octets, les
  trois tailles de réponse, `AuctionIds_AreTheEpic73Ones` en `:88`) — le patron de la discipline
  d'offsets que le dev doit reprendre pour `1309`.

## 12. Ce que cette fiche resserre, corrige ou confirme

* **Le socle §8.4 est à moitié clos.** Il donnait `duration_type` comme « la seule valeur de la famille
  dont l'énumération n'est pas établie ». La **première** moitié de la question est maintenant mesurée :
  le client 7.3 émet `1`, `2` ou `3`, jamais `0`, par trois cases d'option, avec `1` par défaut. La
  **seconde** moitié (la durée que chaque valeur représente) reste ouverte, et remonte telle quelle en
  §8 question 1, avec la raison de l'échec (libellés dans des `.nui` non lisibles ici).
* **Le socle §9 rang 2 est dédoublé.** Il demandait « valeurs de `duration_type` et durées associées » a
  deux volets : l'énumération est tranchée par la mesure (§3.5), la durée reste à Killian (§8).
* **Les huit relevés du socle §3.6 tiennent** (§3.1), y compris le constat sur les 4 octets réservés du
  message interne — dont la fiche ajoute la **raison** : le sender saute `message+0x1b` parce que ce
  `dword` est le deuxième de la cellule `item_count` de la liste (`0x4fa7d0`), une donnée d'affichage
  qui n'a pas de place sur le fil.
* **Confirmation indépendante de la clé 1160** : le socle et la fiche `1308` la donnaient
  (stub `0x49e3ae`, sender `0x48df30`) ; cette fiche la re-mesure par les **deux** chemins (table
  d'octets `0x49ea50[133] = 0x26` → table de sauts `0x49e98c[0x26] = 0x49e3ae`, et `movl $0x488,0x4(%eax)`
  en `0x4f2604`).
* **Un piège d'offsets est nommé** (§10) : `+0x4b0` est un indice dans `SUIAuctionRegisterWnd` et un
  pointeur de contrôle (`edit_bid`) dans la classe voisine `0x4fc330` ; les deux constructeurs de
  fenêtres d'enchères se partagent la moitié de leurs décalages, y compris un contrôle `item_list`
  homonyme mais situé ailleurs (`+0x4b8` contre `+0x4f8`). C'est le genre de collision qui fait
  écrire une fausse ligne si l'on identifie une classe par un seul décalage. S'y ajoute la correction
  du §3.4 : dans `SUIAuctionRegisterWnd`, `+0x4e8`/`+0x4ec` ne sont **pas** des `int32` mémorisés mais
  les **pointeurs** de `r_edit_01`/`r_edit_02` (`0x4f7860`, `0x4f7874`), les deux prix étant analysés
  au clic par `0x501f70`. Une lecture par décalage seul, sans contrôle par les noms de contrôles,
  aurait écrit ici une ligne fausse — c'est la deuxième fois que cette famille le fait.
* **Le chemin d'émission est complet et rejouable** (§3.3-§3.4), ce que le socle ne donnait pas :
  bouton `register` → gestionnaire `0x4fb820` → émetteur `0x4fa6e0` → message
  `.?AUSIMSG_REQ_AUCTION_REGISTER@@` → stub `0x49e3ae` → sender `0x48df30` → initialiseur d'en-tête
  `0x48cad0`.
* **Ce que le client ne fait pas** est écrit aussi : il ne redemande aucune liste après une inscription
  (§5.3), il vide sa liste d'objets localement, et le seul traitement dédié à `1309` dans tout le
  binaire est une ligne de journal (`0x66e07c`, libellé `경매 등록    요청결과-%s[%d]`).

## 13. Vérifications exécutées

Dans `/srv/navislamia/Navislamia`, sur `master` `b56967a074…` non modifié puis sur la branche créée :

| commande | résultat |
|---|---|
| `export NUGET_PACKAGES=/srv/navislamia/.nuget-cache && dotnet build Navislamia.sln -c Debug` | **exit 0** (23 avertissements, 0 erreur) — base de référence du lot |
| `dotnet test Tests/Tests.csproj` | **exit 0** — `Passed: 1302, Failed: 0, Skipped: 0, Total: 1302` : la baseline du PO est reproduite telle quelle, le seuil transversal de 366 est très au-delà |
| `git status --short` avant branchement | vide (arbre propre) |
| `git checkout -B hermes/packet-1309-auction-register origin/master` | branche créée sur `b56967a07430422add88e0e5cdf292b41b18f6c6` |
| `git log --oneline origin/master..master` | **vide** (aucun commit sur `master`) |
| `grep -rn "TryReadAuction" --include=*.cs Game/ Tests/` | vide — le lecteur est à écrire |
| `grep -rn "\.Auctions" --include=*.cs Game/ \| grep -v Migrations/` | vide — le `DbSet` n'est jamais utilisé |
| `grep -rn -i "auction" --include=*.cs Game/Services/` | 4 occurrences, toutes dans `StorageRules.cs` (commentaires et exclusion) |
| `git -C reference/rzu rev-parse HEAD`, `git -C reference/ngemity rev-parse HEAD` | identiques aux empreintes du §9 |
| `sha256sum reference/client73/SFrame.exe` | `41e0af2e…b9500e`, identique au §9 |

Ce qui n'a **pas** été exécuté, et ne peut pas l'être ici : `SFrame.exe` (jamais lancé), aucun Lua ni
script du client, aucun serveur de jeu, aucune base de données. Tout le client de cette fiche vient de
la lecture statique (`objdump -d`, chaînes `.rdata`, RTTI, références croisées).

## 14. Livraison dev

Branche `hermes/packet-1309-auction-register`, partie de `origin/master = b56967a074…`. Commit de code
et de tests : `f9a175a` (suivi du commit de documentation qui porte cette section).

### 14.1 Ce qui a été livré

| fichier | apport |
|---|---|
| `Game/Network/Packets/Game/GameAuctionPackets.cs` | les cinq constantes d'offsets (`AuctionRegisterRequestItemHandleOffset` = 7, `…ItemCountOffset` = 11, `…StartPriceOffset` = 15, `…InstantPurchasePriceOffset` = 23, `…DurationTypeOffset` = 31), `AuctionRegisterRequestSize` = 32, et `TryReadAuctionRegister(ReadOnlySpan<byte>, out uint itemHandle, out int itemCount, out long startPrice, out long instantPurchasePrice, out byte durationType)` — la signature du §5.2, au caractère près |
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_AUCTION_REGISTER = 1309` dans la bande des enchères, avec le commentaire de gating au patron de `TM_CS_SUMMON_CARD_SKILL_LIST` (dont le rappel que `2309` ne doit jamais être déclaré) |
| `Game/Network/Clients/GameClient.cs` | `HandleAuctionRegister(byte[] buffer)` et son bras `if (header.ID == (ushort)GamePackets.TM_CS_AUCTION_REGISTER) { …; continue; }`, **avant** le `switch` qui jette |
| `Tests/Game/AuctionRegisterPacketsTests.cs` | 26 cas de test neufs : offsets, endianness, domaines des cinq champs, refus de longueur, consommation par la boucle, refus par `TM_SC_RESULT` |

Le lecteur lit les cinq champs aux offsets du §3.1, tous en **little-endian**, `duration_type` en
`packet[31]` et rien après. Le bras journalise la trame décodée (les cinq champs) et, sur une trame que
le lecteur refuse, répond `SendResult(1309, ResultCode.InvalidArgument)`.

### 14.2 Le périmètre du refus, et pourquoi il s'arrête là

Le §5.4 range quatre cas sous `InvalidArgument` : longueur ≠ 32, `duration_type` hors `{1,2,3}`, prix
négatifs, `item_count ≤ 0`. Le lot n'en applique **qu'un**, la longueur ≠ 32, pour trois raisons :

1. le §5.2 — le contrat du lecteur — ne borne **que** la longueur, et le §5.7 (ce qui est livrable sans
   décision de Killian) ne nomme que « une trame mal formée » ;
2. les trois autres cas sont explicitement des **questions ouvertes** du §7a, §7b et §7c, et le §8
   question 5 demande à Killian de trancher les `ResultCode` correspondants. Les refuser ici déciderait
   `item_count ≤ 0`, la plage des prix et le sort de `duration_type = 0` **contre** la consigne du §7 ;
3. un `duration_type` hors `{1,2,3}` n'est pas ignoré pour autant : le bras le **signale** en
   `Warning` (« outside the measured domain 1..3 of the 7.3 client »), sans rien refuser. Si Killian
   répond « refus », la modification est d'une ligne dans `HandleAuctionRegister`.

Le prix à payer est nommé : sur une trame forgée portant `duration_type = 0`, `item_count = 0`,
`item_count < 0` ou un prix négatif, le serveur ne répond **rien** au lieu d'`InvalidArgument`. Le §5.4
ligne 2 attend l'inverse ; le §7 et le critère transversal 8 (« aucun champ `NON ÉTABLI` deviné »)
interdisent de le faire maintenant. C'est une décision de lecture, pas un oubli.

### 14.3 Le cas pas-de-code, tel que le §5.7 le borne

Les points 1 à 5 du §5.7 sont livrés ; le point 6 (l'exécution de l'inscription) ne l'est pas. Sur une
trame bien formée, le serveur **ne répond rien** : c'est la ligne 4 du §5.4 (« aucune inscription
exécutable → journaliser et ne rien envoyer »), pas un choix du dev. Un `Success` annoncerait une
inscription qui n'a pas eu lieu, un refus inventerait un code métier (§8 question 5), et le `1303`
rafraîchi du §5.4 ligne 1 suppose une annonce qui existe. Le verrou de non-régression est porté par les
tests `RegisterRequest_ExecutesNothingAndAnswersNothing` et
`RegisterRequest_OutOfDomainDurationStillAnswersNothing`.

### 14.4 Tests d'offsets (critère 3)

Le fichier `Tests/Game/AuctionRegisterPacketsTests.cs` porte les 26 cas neufs ; les offsets sont tenus
par ceux-ci :

* `Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame` écrit la trame **à la main**, en octets
  littéraux, et affirme chaque octet de chaque champ avec sa position et son endianness (lecture
  `ReadUInt32BigEndian` / `ReadInt32BigEndian` en contre-épreuve, `NotBe`) ; l'en-tête est vérifié
  octet par octet (`0x20`, `0x1D 0x05`, somme de contrôle 66) ;
* `Request_IsThirtyTwoBytesWithTheMeasuredOffsets` et `Request_LeavesNoGapBetweenItsFields` affirment
  la taille totale (32 = 7 + 4 + 4 + 8 + 8 + 1) et la contiguïté des cinq champs — donc que le `dword`
  réservé du message interne n'a pas de place sur le fil ;
* `TryReadAuctionRegister_RejectsAnyLengthOtherThanThirtyTwo` couvre 0, 7, 15, 23, 31, 33 et **36**
  (la trame qu'un lecteur écrit d'après le message interne, et non d'après le fil, demanderait) et
  vérifie qu'un refus ne laisse aucun champ partiellement lu derrière lui ;
* `TryReadAuctionRegister_ReadsTheTwoPricesInSixtyFourBits` prend 3 000 000 000 et 2 500 000 000, qui
  ne tiennent pas dans un `int32` ; `…ReadsTheItemHandleUnsigned` prend `uint.MaxValue` ;
  `…ReadsTheCountAsASignedInt32WithoutJudgingIt` et `…HandsTheDurationTypeOverWithoutDecidingItsDomain`
  (1, 2, 3, **0** et 255) fixent les domaines que le lecteur ne juge pas (§7a, §7b, §7c).

### 14.5 Mesures exécutées ce réveil

| commande (depuis `/srv/navislamia/Navislamia`) | résultat |
|---|---|
| `export NUGET_PACKAGES=/srv/navislamia/.nuget-cache` puis `dotnet build Navislamia.sln -c Debug` | **exit 0**, 0 erreur |
| `dotnet test Tests/Tests.csproj --filter "FullyQualifiedName!~AuctionRegisterPacketsTests"` | **exit 0** — `Passed: 1302, Failed: 0, Skipped: 0` : la base du §13 est reproduite, le lot ne retire rien |
| `dotnet test Tests/Tests.csproj` | **exit 0** — `Passed: 1328, Failed: 0, Skipped: 0` : **+26** cas, soit la base 1302 + les 26 du lot (seuil transversal 366 : très au-delà) |
| `dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~AuctionRegisterPacketsTests.RegisterRequest"` | **exit 1**, `Failed: 5, Passed: 0` — c'est la morsure du §14.6, pas un état livré |
| `git status --porcelain` après restauration de la mutation | vide |
| `git log --oneline origin/master..master` | **vide** (aucun commit sur `master`) |

### 14.6 Morsure par mutation : les tests de dispatch mordent vraiment

Membre d'énum **sans** son bras (la faute que le critère 4 interdit), ajouté sur l'arbre commité, puis
`dotnet test … --filter "…RegisterRequest"` : les **cinq** tests de boucle échouent, chacun avec
`System.Exception: Unknown Packet Type 1309` — `Unknown Packet Type` est bien le `switch` final de
`GameClient.cs`, et un membre déclaré sans bras casse la boucle de réception. Restauration par
`git checkout -- Game/Network/Clients/GameClient.cs`, arbre propre vérifié. Les tests de lecture seuls
ne prouvent pas le dispatch : cette morsure le prouve.

### 14.7 Géométrie de fusion, mesurée après le lot (`git merge-tree --write-tree`)

| branche sœur | résultat |
|---|---|
| `hermes/packet-1300-auction-search` | **fusion propre** |
| `hermes/packet-1302-auction-selling-list` | **fusion propre** |
| `hermes/packet-1304-auction-bidded-list` | **fusion propre** |
| `hermes/packet-1306-auction-bid` | conflit, **un seul fichier** : `GamePackets.cs` |
| `hermes/packet-1308-auction-instant-purchase` | conflit, **un seul fichier** : `GamePackets.cs` |

Les choix de placement ont été faits pour cela : `GameAuctionPackets.cs` reçoit le bloc `1309` après
`WriteFixedAscii`, avant `CreatePacket` — un interstice que **aucune** des cinq branches sœurs
n'utilise (elles écrivent après `SellerNameSize`, après les trois constructeurs, à la fin de la classe,
après `TableOffset` et entre l'écrivain d'en-tête de page et l'écrivain d'enregistrement) ;
`GameClient.cs` reçoit le bras entre la trame `TM_CS_EMOTION` et `TM_CS_RANKING_TOP_RECORD`, là où les
cinq autres lots n'écrivent pas (1300 au-dessus du `switch`, 1302 après le garde de réponse de la
famille, 1304 avant le bloc `TM_SC_MIX_RESULT`, 1306 après le bras de la carte d'invocation, 1308 avant
`TM_CS_DROP_QUEST`) ; les deux fichiers fusionnent proprement avec les cinq branches.

Le seul conflit restant est la **bande des enchères de `GamePackets.cs`** (lignes 153-162) : 1306 et
1308 y insèrent leur membre au même endroit, donc se télescopent déjà entre eux, et le §5.6 impose à
`1309` de s'écrire dans cette bande. Le conflit est trivial à résoudre (trois déclarations
indépendantes à garder côte à côte) et n'affecte ni `GameClient.cs` ni `GameAuctionPackets.cs`. La même
épreuve sur `hermes/packet-1306-auction-bid` contre `hermes/packet-1308-auction-instant-purchase`
montrerait le même conflit, sans `1309` : ce point chaud **préexiste** au sixième lot.

### 14.8 Ce que le dev n'a pas livré

* **`CLAUDE.md`** : fichier protégé par Hermes, non écrit par le dev (le refus est normal). Le bloc du
  §10 est repris dans la description de la MR par `navis-dev`, avec la **correction** que le §10 portait
  `item_handle` `int32` @7 alors que le §4, le §5.2, le §6 et le code implémenté disent `uint32` (rzu :
  `ar_handle_t`, 4 octets). Le §10 est corrigé ici même : le bloc à recopier dans `CLAUDE.md` porte
  bien `uint32`.
* L'exécution de l'inscription (points 6 et suivants du §5.7) : aucune ligne `AuctionEntity`, aucun
  calcul d'`EndTime`, aucune taxe, aucun déplacement d'objet, aucun `ResultCode` métier (§5.5).
* Aucun `1303` rafraîchi : il suppose une annonce qui existe (§5.3, §5.4 ligne 1).
* Aucune vérification côté client : `SFrame.exe` n'a pas été lancé (interdit ici), les cinq valeurs de
  `duration_type` restent celles de la lecture statique du §3.5.
