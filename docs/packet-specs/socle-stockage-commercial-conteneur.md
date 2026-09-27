# Socle stockage commercial, lot 2 — conteneur persisté, propriété de l'`uid` et retrait réel

`TM_SC_COMMERCIAL_STORAGE_INFO` (10003), `TM_SC_COMMERCIAL_STORAGE_LIST` (10004),
`TM_CS_TAKEOUT_COMMERCIAL_ITEM` (10005)

Cette fiche est la **fiche du lot 2**. Elle ouvre les réserves §7c (clé du `commercial_item_uid`),
§7f (modèle de persistance) et §7g (producteur) de la fiche du lot 1,
`docs/packet-specs/socle-stockage-commercial.md` (mergée sur `master` en `b56967a`). Elle ne
rediscute **ni** les structures et les tailles (§3 de la fiche du lot 1), **ni** le gating (§4 de la
fiche du lot 1) : ces deux sections restent la référence, et elles sont rappelées ici seulement là
où le lot 2 change une **valeur écrite**.

Rappel de l'état livré par le lot 1, tel que la fiche du lot 1 le décrit (§10) et tel qu'il est sur
`master` : les trois ids sont déclarés (`Game/Network/Packets/Enums/GamePackets.cs:226-228`), les
deux constructeurs serveur → client existent
(`Game/Network/Packets/Game/GameCommercialStoragePackets.cs:48` et `:66`), le lecteur de la 10005
existe (`GameActionPackets.TryReadTakeoutCommercialItem`), l'entrée en jeu pousse une 10003 à `0/0`
puis une 10004 vide (`Game/Network/Clients/Actions/GameActions.cs:284-290`), et la réception de la
10005 se contente de journaliser (`Game/Network/Clients/GameClient.cs:317-329`). Le conteneur est
donc **vide par construction** : il n'y a, dans ce dépôt, aucun écrivain de son contenu.

## Réponses courtes

1. **Le modèle est tranché : voie A.** Une entité et une table **nouvelles**, calquées sur
   `Telecaster.PaidItem` du dump officiel (`reference/ngemity/Database/Telecaster.sql:419-439`).
   La voie B (réutiliser `ItemStorages` en étendant `StorageType`) est **écartée** : `ItemKeeping`
   est un autre conteneur (§5.1).
2. **La clé de `commercial_item_uid` est établie comme un contrat, pas devinée.** Le client
   recopie *verbatim* l'`uint32` reçu dans la 10004 vers la 10005, et personne d'autre que le
   serveur n'écrit dans la 10004 : **l'espace des `uid` appartient au serveur**. Règle qui en
   découle : tout `uid` s'écrit `(uint)ligne.Id` et ne se résout **jamais seul** — la requête porte
   toujours le propriétaire (§5.2).
3. **Le producteur est hors lot.** La boutique d'objets est un **service externe** (preuves en
   §5.10 : l'URL `http://itemshop.rappelz.com/login.aspx` est dans le binaire, la 10001 transporte
   `client_id`, `account_id`, `one_time_password`). C'est un **préalable d'infrastructure**, pas un
   contournement : le SQL d'amorçage qui permet de vérifier le lot en jeu est fourni en §5.10.
4. **Le retrait devient réel** : résolution `(uid, propriétaire)`, objets remis au sac par le
   service existant `ICharacterService.AddItemAsync` et par les **trames d'inventaire ordinaires**,
   décrément de `rest_item_count`, horodatage des colonnes `taken_*`. **Aucune réponse dédiée,
   aucun `ResultCode`, aucun coût, aucun plafond inventé**, et le serveur **n'émet jamais 10005**.
5. **Les tailles ne changent pas** : 10003 = **11 octets**, 10004 = **9 + 10 × n** octets, 10005 =
   **13 octets**. Ce qui change au lot 2, c'est uniquement **ce que le serveur met dedans**.
6. **Le lot se découpe en une seule livraison de code** (§9) : modèle + migration, règles pures,
   lecture réelle, retrait réel, tests. Le producteur reste dehors.

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id décimal 10003, `TM_SC_COMMERCIAL_STORAGE_INFO` | serveur → client | `op_codes.md:275` ; client : `mov eax,0x2713` VA `0x6793b7` avec `push 0xa52d98` VA `0x6793a7` |
| Id décimal 10004, `TM_SC_COMMERCIAL_STORAGE_LIST` | serveur → client | `op_codes.md:276` ; client : `mov eax,0x2714` VA `0x6793fa` avec `push 0xa52d78` VA `0x6793ea` |
| Id décimal 10005, `TM_CS_TAKEOUT_COMMERCIAL_ITEM` | client → serveur | `op_codes.md:277` ; client : `mov eax,0x2715` VA `0x67943d` avec `push 0xa52d58` VA `0x67942d` |
| Déclaration dans le dépôt | `TM_SC_COMMERCIAL_STORAGE_INFO = 10003`, `TM_SC_COMMERCIAL_STORAGE_LIST = 10004`, `TM_CS_TAKEOUT_COMMERCIAL_ITEM = 10005` | `Game/Network/Packets/Enums/GamePackets.cs:226-228` |

La famille commerciale du client s'arrête à **10005**. Le registre de noms de paquets du client
(`SFrame.exe`, `.text`) est contigu de 10000 à 10005 — `TM_CS_OPEN_ITEM_SHOP` (`0x2710`),
`TM_SC_OPEN_ITEM_SHOP` (`0x2711`), `TM_SC_OPEN_PAID_STORAGE` (`0x2712`), les trois nôtres
(`0x2713`-`0x2715`) — puis l'entrée suivante est `TM_SC_COMPETE_REQUEST` (`mov eax,0x1195` VA
`0x679480`) : **il n'y a pas de 10006 à 10012 dans le registre des paquets de jeu**.
Les entrées `10010`, `10011` et `10012` que la fiche `10000-open-item-shop.md` §7f relève dans le
binaire sont des **ids de la session Auth ↔ Client** : `TS_CA_ACCOUNT = 10010`, et la série
`10010`/`10011`/`10012`/`10021`/`10023` se retrouve telle quelle dans
`reference/ngemity/shared/Server/ClientPackets.h:307-314` sous le commentaire « Between Auth &
Client ». Le registre de noms du client ne les nomme pas (aucun `mov eax,0x271a` dans la zone
`0x6792xx`-`0x6794xx`) mais leurs constructeurs de trames sont voisins (`0x658b30`, `0x658b90`,
`0x658be0`), dans un espace d'ids qui appartient à l'autre session. **Conclusion pour ce socle :
l'inventaire des ids du jeu commercial est complet — la famille est exactement 10000..10005.**

Un id voisin reste hors lot, et il est nommé ici pour qu'aucun dev ne l'invente :

- **10002 `TM_SC_OPEN_PAID_STORAGE`** existe dans le client (`push 0xa52db8` VA `0x679364`,
  `mov eax,0x2712` VA `0x679374`) et **n'est pas inventorié** dans `op_codes.md` (qui passe de
  `:274` à `:275`). rzu le déclare **sans aucun champ** (`TS_SC_OPEN_PAID_STORAGE_DEF` est vide,
  `librzu/src/packets/GameClient/TS_SC_OPEN_PAID_STORAGE.h:5`), mais le client lui donne une
  **branche de conversion** : la table de saut du convertisseur
  (`jmp DWORD PTR [eax*4+0x67f6cc]`, VA `0x67ec54`) porte quatre entrées pour 10001..10004 —
  `[0x67ecd4, 0x67ec63, 0x67ed27, 0x67ed64]`, déjà relevée par la fiche du lot 1 (`:161`) — et
  l'entrée de 10002 est la **deuxième** (`0x67ec63`, octets vérifiés à `0x67f6d0`). Cette cible est
  un thunk (`endbr32`, sept arguments recopiés, `call 0x67e8d0`, `ret`) : je n'ai **pas** reconstitué
  son format, et je ne peux donc pas dire si rzu (zéro champ) ou le client (branche dédiée) a
  raison. **10002 n'est pas émis dans ce lot**, et rouvrir cette question appartient à la famille
  `10000`/`10001` (MR #33), pas à ce socle. Voir §7n.

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Rien, pour 10003 et 10004 : ce sont des paquets poussés

Inchangé depuis le lot 1 (§2.1 de la fiche du lot 1) : aucune demande cliente n'existe pour ce
conteneur — ni dans rzu, ni dans NGemity, ni dans le client. Le serveur décide seul quand il envoie
la paire.

### 2.2 La fenêtre s'ouvre localement, sans packet

Inchangé : la fenêtre du stockage commercial est locale au client (commandes `/cshop` VA `0xa1fcf8`
et `/cstorage` VA `0xa1fd00`, clé de ressource `commercial_shop` VA `0xa2d934`, voir la fiche du lot
1 §2.2 et §7h). Elle affiche **ce que le client a reçu pour la dernière fois** : d'où l'importance de
l'ordre et de la cohérence des deux trames, et d'où le rafraîchissement après un retrait (§5.7).

### 2.3 Le geste de retrait

Le joueur ouvre la fenêtre, y voit une ligne (`uid`, `code`, `count`) et déclenche le retrait
d'une quantité. Le client construit alors une 10005 (13 octets) et y recopie **l'`uint32` reçu dans
la 10004, tel quel** — même nom, même largeur, même ordre (`uid` @7, `count` @11, fiche du lot 1
§3.3). C'est la seule chose que le client envoie pour ce conteneur.

### 2.4 Ce que le lot 2 change côté joueur : rien

Le geste existe déjà et la trame arrive déjà au serveur (elle est lue et journalisée). Le lot 2
change **ce que le serveur en fait**, et surtout **ce qu'il met dans les deux trames descendantes** :
au lieu de `0/0` et d'une liste vide, les compteurs et les lignes réelles du conteneur.
## 3. Structure sur le fil

En-tête commun de 7 octets (`Length` u32 @0, `ID` u16 @4, `Checksum` u8 @6), tel quel depuis le
lot 1 : la structure et les tailles sont **reprises de la fiche du lot 1 §3, sans changement**.
La colonne « valeur au lot 2 » est ce que cette fiche demande d'écrire désormais.

### 3.1 `TM_SC_COMMERCIAL_STORAGE_INFO` (10003) — serveur → client — **11 octets**

| Offset | Type | Nom | Source | Valeur au lot 2 |
|---|---|---|---|---|
| 0 | u32 | `Length` | `GameCommercialStoragePackets.cs:52` | 11 |
| 4 | u16 | `ID` | `GameCommercialStoragePackets.cs:53` | 10003 |
| 6 | u8 | `Checksum` | `GameCommercialStoragePackets.cs:57` | somme des octets 0..5 |
| 7 | u16 | `total_item_count` | rzu `TS_SC_COMMERCIAL_STORAGE_INFO.h:8` ; client branche VA `0x67ed27` (fiche du lot 1 §3.1) | **nombre de lignes visibles** du conteneur (§5.3) |
| 9 | u16 | `new_item_count` | rzu `TS_SC_COMMERCIAL_STORAGE_INFO.h:9` ; même branche | **nombre de ces lignes dont `Confirmed` vaut 0** (§5.3, réserve §7b) |

Taille totale annoncée : **11 octets**. Aucun octet au-delà de l'offset 10.

### 3.2 `TM_SC_COMMERCIAL_STORAGE_LIST` (10004) — serveur → client — **9 + 10 × n octets**

| Offset | Type | Nom | Source | Valeur au lot 2 |
|---|---|---|---|---|
| 0 | u32 | `Length` | `GameCommercialStoragePackets.cs:81` | `9 + 10 × n` |
| 4 | u16 | `ID` | `GameCommercialStoragePackets.cs:82` | 10004 |
| 6 | u8 | `Checksum` | `GameCommercialStoragePackets.cs:95` | somme des octets 0..5 |
| 7 | u16 | `count` | rzu `TS_SC_COMMERCIAL_STORAGE_LIST.h:16` ; client branche VA `0x67ed64` (fiche du lot 1 §3.2) | `n` = nombre de lignes émises |
| 9 + 10 × i | u32 | `commercial_item_uid` | rzu `TS_SC_COMMERCIAL_STORAGE_LIST.h:8` | `(uint)ligne.Id` (§5.2) |
| 13 + 10 × i | i32 | `code` | rzu `TS_SC_COMMERCIAL_STORAGE_LIST.h:9` | `ligne.ItemCode` |
| 17 + 10 × i | u16 | `count` | rzu `TS_SC_COMMERCIAL_STORAGE_LIST.h:10` | `min(ligne.RestItemCount, 65535)` (§5.3) |

Les trois champs d'une ligne sont **contigus sur 10 octets, sans remplissage** (constante du dépôt :
`CommercialStorageItemSize = 10`, `GameCommercialStoragePackets.cs:42`), et les types du dump
officiel s'y alignent (`item_code` int(11) `:424`, `rest_item_count` int(11) `:426`). Taille pour
`n = 0` : **9 octets**, `count = 0` — le client traite explicitement la liste vide (fiche du lot 1
§3.2, `jbe` VA `0x67ed85`). Le client **ne recoupe jamais** `count` avec `Length` : la trame doit
être compacte.

**Ordre des lignes** : croissant sur `Id` de ligne (§5.4). Aucune référence ne l'impose ; il est
demandé ici pour que deux émissions successives du même contenu soient **identiques** (la fenêtre du
client est un instantané, §2.2) et pour rendre les tests d'offsets déterministes.

### 3.3 `TM_CS_TAKEOUT_COMMERCIAL_ITEM` (10005) — client → serveur — **13 octets**

| Offset | Type | Nom | Source | Valeur lue |
|---|---|---|---|---|
| 0 | u32 | `Length` | `GameActionPackets.TryReadTakeoutCommercialItem` (lot 1 §5.4) | 13 |
| 4 | u16 | `ID` | idem | 10005 |
| 6 | u8 | `Checksum` | idem | non vérifiée (idiome du dépôt) |
| 7 | u32 | `commercial_item_uid` | rzu `TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:8` | **recopie verbatim** de l'`uid` reçu en 10004 (§5.2) |
| 11 | u16 | `count` | rzu `TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:9` | quantité demandée, **non signée**, sans borne propre |

Toute longueur ≠ 13 est refusée par le lecteur existant, et le restera (§5.6). Le serveur n'émet
jamais cette trame, et ne l'émettra jamais : deux tests du lot 1 le verrouillent
(`Tests/Game/CommercialStoragePacketsTests.cs:249` et `:276`) et le lot 2 **ne les retire pas**.

## 4. Gating de version

Statuer ici est obligatoire, et la décision est celle du lot 1, inchangée : **les trois ids de ce
socle sont ceux de la branche `< EPIC_9_6_3`**, donc **10003 / 10004 / 10005 pour Epic 7.3**.

| Paquet | Id < `EPIC_9_6_3` | Id ≥ `EPIC_9_6_3` | Source |
|---|---|---|---|
| `TS_SC_COMMERCIAL_STORAGE_INFO` | 10003 | 9003 | `reference/rzu/librzu/src/packets/GameClient/TS_SC_COMMERCIAL_STORAGE_INFO.h:12-13` |
| `TS_SC_COMMERCIAL_STORAGE_LIST` | 10004 | 9004 | `…/TS_SC_COMMERCIAL_STORAGE_LIST.h:20-21` |
| `TS_CS_TAKEOUT_COMMERCIAL_ITEM` | 10005 | 9005 | `…/TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:12-13` |
| `TS_SC_OPEN_PAID_STORAGE` (hors lot) | 10002 | 9002 | `…/TS_SC_OPEN_PAID_STORAGE.h:8-9` |
| `TS_SC_OPEN_ITEM_SHOP` (hors lot) | 10001 | 9001 | `…/TS_SC_OPEN_ITEM_SHOP.h:14-15` |
| `TS_CS_OPEN_ITEM_SHOP` (hors lot) | 10000 | 9000 | `…/TS_CS_OPEN_ITEM_SHOP.h:8-9` |

- `EPIC_7_3 = 0x070300` (`reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59`) est **inférieur** à
  `EPIC_9_6_3 = 0x090603` (`:96`, commenté « GS packet ID modified with version 20200713 » — c'est
  la bascule qui a déplacé toute la famille commerciale de 1000x vers 900x). En 7.3, les bons ids
  sont donc bien les **petits**.
- **Piège** : en 7.3, `9004` et `9005` désignent déjà la famille « numéro de sécurité »
  (`op_codes.md:270-271`) ; `GamePackets.cs:220-224` documente déjà que rzu remappe `security_no`
  vers **8105** à partir de `EPIC_9_6_3`. Ne **jamais** déclarer 9003/9004/9005 comme alias de ce
  socle, ni aujourd'hui ni « pour plus tard ».
- **Aucun champ** des trois paquets ne porte de gating : les cinq champs de la 10003 et de la 10004
  sont des `_(simple)` sans enveloppe de version (headers rzu cités en §3), la 10005 idem. Rien à
  trancher champ par champ — ce qui est la décision, et elle est écrite ici pour que le dev ne la
  cherche pas.
- `10002` n'est pas inventorié dans `op_codes.md` et n'est pas émis (§1, §7n) ; si une carte le
  rouvre un jour, son id 7.3 est `10002` et son id ≥ 9.6.3 est `9002`.
## 5. Traitement attendu

### 5.1 Le modèle de conteneur : voie A, `PaidItem`, et pourquoi pas la voie B

**Décision : voie A.** Une table **nouvelle**, calquée sur `Telecaster.PaidItem` du dump officiel
(`reference/ngemity/Database/Telecaster.sql:419-439`). C'est le seul conteneur du schéma officiel
dont le profil corresponde à ce que la trame transporte, et le raisonnement est vérifiable :

1. **La trame ne porte aucune donnée d'instance.** Une ligne de 10004, c'est `uid`, `code`, `count`
   (§3.2) — ni niveau, ni amélioration, ni châsse, ni durabilité. Un conteneur d'objets
   *instanciés* transporterait un `TS_ITEM_BASE_INFO` complet, comme le fait la famille voisine
   `TS_SC_ITEM_KEEPING_LIST` (`reference/ngemity/shared/Server/Packets/GameClient/
   TS_SC_ITEM_KEEPING_LIST.h:7-14` : `keeping_uid`, `item_info`, `duration`, `keeping_type`,
   `related_item_*`).
2. **`ItemKeeping` n'est pas ce conteneur.** Les objets « kept » du serveur officiel restent des
   lignes de la table `Item`, marquées par `Item.keeping_id`
   (`reference/ngemity/Database/Telecaster.sql:345`), et c'est cette colonne que NGemity écrit
   (`reference/ngemity/Chihiro/src/Entities/Item/Item.cpp:92` et `:128`) : ce sont donc des objets
   **instanciés, possédés**, parqués dans un emplacement, avec métadonnées d'attente
   (`ItemKeeping`, `:403-414` : `expiration_time`, `keeping_type`, `related_auction_id`,
   `related_item_code`). `PaidItem` (`:419-439`) est tout autre chose : `item_code`, `item_count`,
   `rest_item_count`, `bought_time`, `valid_time`, `server_name`, `taken_*`, `confirmed`,
   `isCancel` — le profil « **objet de boutique livré, puis retiré, éventuellement en plusieurs
   fois** ».
3. **Argument décisif, la forme du retrait.** Le retrait de la famille `ITEM_KEEPING` est
   `TS_CS_ITEM_KEEPING_TAKE` (1352) et ne porte **qu'un `keeping_uid`**
   (`…/TS_CS_ITEM_KEEPING_TAKE.h:7`) : la quantité est dans l'instance d'objet. Le nôtre porte
   **`uid` + `count`** (`…/TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:9`) — exactement la forme d'une
   livraison partiellement retirée, c'est-à-dire `PaidItem.rest_item_count`.
4. **La voie B casserait les requêtes existantes.** `ItemStorageEntity` (ex-`ItemKeeping`) est lu
   par le conteneur d'enchères/courrier, dont les requêtes excluent explicitement les lignes qui ne
   sont pas du sac (quatre conditions, dont `StorageId == null`,
   `Game/DataAccess/Repositories/StorageRepository.cs:120-124`), et son `StorageType` ne connaît
   que des motifs d'enchères et de courrier (`Game/DataAccess/Entities/Enums/StorageType.cs`).
   Ranger une livraison de boutique dans `ItemStorages` obligerait à créer des lignes `ItemEntity`
   pour des objets **que le joueur ne possède pas encore**, indistinguables des objets possédés
   dans les requêtes qui doivent précisément les exclure.

**Forme demandée** (entité `Game/DataAccess/Entities/Telecaster/PaidItemEntity.cs`, dérivant de
`Entity` comme les autres : `Id` long identité, horodatages, suppression logique, cf.
`Game/DataAccess/Entities/Entity.cs:5-9`). Table `PaidItems` (convention EF du dépôt, cf. `Items`,
`Characters`), déclarée dans `TelecasterContext`. Correspondance colonne par colonne :

| Propriété | Colonne officielle | Ligne du dump | Actée au lot 2 ? |
|---|---|---|---|
| `Id` (long, identité) | `sid` int AUTO_INCREMENT | `:420` | **oui** — c'est la clé de l'`uid` (§5.2) |
| `AccountId` (long) | `account_id` int | `:421` | **oui** — moitié du contrôle de propriété |
| `CharacterId` (long?) | `avatar_id` int | `:422` | **oui** — livraison visée (peut être nul = livraison de compte) |
| `CharacterName` (string) | `avatar_name` varchar(61) | `:423` | non lue, conservée |
| `ItemCode` (int) | `item_code` int | `:424` | **oui** — c'est le `code` de la 10004 |
| `ItemCount` (int) | `item_count` int | `:425` | non lue, conservée (quantité achetée) |
| `RestItemCount` (int) | `rest_item_count` int | `:426` | **oui** — c'est le `count` de la 10004 et la borne du retrait |
| `BoughtTime` (DateTime?) | `bought_time` datetime | `:427` | non lue, conservée |
| `ValidTime` (DateTime?) | `valid_time` datetime | `:428` | conservée, **non appliquée** (§7d) |
| `ServerName` (string) | `server_name` varchar(30) | `:429` | conservée, **non filtrée** (§7e) |
| `TakenCharacterId` (long?) | `taken_avatar_id` int | `:430` | **oui** — horodatage du retrait |
| `TakenCharacterName` (string) | `taken_avatar_name` | `:431` | **oui** (nom du preneur au moment du retrait) |
| `TakenServerName` (string) | `taken_server_name` | `:432` | non lue, conservée |
| `TakenTime` (DateTime?) | `taken_time` datetime | `:433` | **oui** |
| `TakenAccountId` (long?) | `taken_account_id` int | `:434` | **oui** |
| `Confirmed` (int) | `confirmed` int | `:435` | **lue seule** — alimente `new_item_count`, jamais écrite par Navislamia (§7b) |
| `ConfirmedTime` (DateTime?) | `confirmed_time` date | `:436` | **jamais écrite** (§7b) |
| `IsCancel` (bool) | `isCancel` tinyint | `:437` | **oui** — exclut la ligne de la liste |

Le **nom** de l'entité et de la table est un choix Navislamia (le dump nomme la table au singulier) ;
il doit être écrit dans la migration et dans le commentaire de classe, avec la référence
`Telecaster.sql:419-439`. Les colonnes conservées mais non actées le sont **exprès** : les jeter
serait décider à la place de Killian ce que `valid_time` et `server_name` signifient.

### 5.2 La clé de `commercial_item_uid` — le point de sécurité, tranché

Ce que la fiche du lot 1 laissait ouvert (§7c) se décompose en deux questions.

**a) Qui décide de la valeur de l'`uid` ? Le serveur, et lui seul.** C'est établi : le client
recopie *verbatim* l'`uint32` qu'il a reçu dans la 10004 (fiche du lot 1 §3.2 et §3.3, même nom,
même largeur), et la 10004 n'a qu'un émetteur, le serveur (aucun autre constructeur ne contient cet
id dans le dépôt, et un test du lot 1 verrouille qu'il n'existe pas non plus de constructeur de
10005). Il n'y a donc **pas d'espace de clés étranger** à deviner : l'`uid` est une valeur que le
serveur a lui-même écrite, et la seule question qui reste est *laquelle*.

**b) Décision : `uid = (uint)ligne.Id`,** c'est-à-dire la clé primaire de la ligne de conteneur,
avec quatre règles obligatoires :

1. **Émission** : `(uint)row.Id`. Une ligne dont `Id > uint.MaxValue` **ne peut pas être adressée**
   de façon fidèle : elle est **exclue de la liste** et journalisée en `Warning`. On ne tronque
   jamais pour émettre — une troncature produirait un `uid` qui pourrait désigner une autre ligne.
2. **Résolution** : la comparaison porte sur les **64 bits**. `context.PaidItems.Where(row => row.Id
   == uid …)` — `uint` s'élargit en `long` sans perte, et c'est exactement le précédent du dépôt
   pour un handle reçu du client : `context.Items.FirstOrDefaultAsync(row => row.Id == itemHandle)`
   (`StorageRepository.cs:54`, `itemHandle` est un `uint`). Une comparaison `(uint)row.Id == uid`
   est **interdite** : deux lignes dont les `Id` diffèrent de 2^32 y seraient confondues.
3. **Propriété, toujours, dans la même requête** : la ligne n'est résolue que si
   `row.AccountId == character.AccountId` **et** (`row.CharacterId == null` **ou**
   `row.CharacterId == character.Id`) **et** `!row.IsCancel` **et** `row.RestItemCount > 0`. Jamais
   l'`uid` seul. C'est ce qui rend un `uid` forgé ou étranger inoffensif : le pire cas est « ligne
   inconnue », jamais « ligne de quelqu'un d'autre ».
4. `uid == 0` ne résout rien (clé identité, aucune ligne n'a `Id = 0`) → refus, comme un `uid`
   inconnu.

Justification du `CharacterId == null` autorisé : les colonnes `taken_avatar_id` / `taken_account_id`
n'existent que parce que **le preneur peut différer de la cible** de livraison. Une ligne sans cible
(achat au niveau du compte) doit donc rester prenable, sinon elle serait définitivement
inaccessible. Le libellé exact de la règle officielle n'est pas connu : §7a.

**Ce que cette décision n'est pas** : elle ne prétend pas que le serveur officiel utilise `sid`
(hypothèse de la fiche du lot 1 §7c, plausible — `sid` est un `int` identité unique, `:420` — mais
non prouvée). Elle n'en a pas besoin : Navislamia n'a pas à reproduire un espace de clés externe,
puisqu'il **est** la source de l'`uid`.

### 5.3 Les règles pures : `Game/Services/CommercialStorageRules.cs`

Les décisions doivent vivre dans une classe **pure**, sans base de données, parce que c'est la seule
couture testable de ce dépôt : la suite de tests ne construit **aucun** `TelecasterContext` (aucun
paquet SQLite/InMemory dans `Tests/Tests.csproj:13-22`), et elle teste au contraire les règles pures
(`Tests/Game/InventoryArrangeTests.cs`, `GmCommandRulesTests.cs`, `GroundItemDropRulesTests.cs`),
sur le modèle de `Game/Services/StorageRules.cs:19`. La classe porte :

```csharp
public static bool IsVisible(PaidItemEntity row, long characterId, long accountId);
public static bool TryToEntry(PaidItemEntity row, out (uint Uid, int Code, ushort Count) entry);
public static (uint Uid, int Code, ushort Count)[] BuildEntries(IEnumerable<PaidItemEntity> rows);
public static (ushort Total, ushort New) BuildCounters(IReadOnlyList<PaidItemEntity> visibleRows);
public static bool TryTakeable(PaidItemEntity row, ushort count, out ushort takeable);
```

Décisions exactes, toutes testables :

| Règle | Décision | Pourquoi |
|---|---|---|
| Visibilité | `!row.IsCancel && row.RestItemCount > 0 && row.AccountId == accountId && (row.CharacterId == null \|\| row.CharacterId == characterId)` | §5.2.3 ; `IsCancel` exclu parce que livrer un achat annulé donnerait au joueur un objet qui n'est pas à lui |
| Entrée | `uid = (uint)row.Id`, `code = row.ItemCode`, `count = min(row.RestItemCount, 65535)` | §5.2.1 ; `count` est un u16 sur le fil (§3.2) |
| Ligne non adressable | `row.Id > uint.MaxValue` → **pas d'entrée** (journalisée) | §5.2.1 |
| Ligne non représentable | `row.ItemCode <= 0` → **pas d'entrée** (journalisée) | un code nul ne peut pas être rendu par le client ni validé par le catalogue (§5.6) |
| Ordre | `BuildEntries` trie par `Id` croissant | §3.2 |
| Plafond de trame | si plus de 65535 lignes visibles : les 65535 premières (par `Id`) sont émises, le reste est journalisé | `count` est un u16 et le constructeur existant **lève** au-delà (`GameCommercialStoragePackets.cs:73-77`) : mieux vaut une liste bornée et un `Warning` qu'une exception dans la séquence d'entrée en jeu |
| `total_item_count` | nombre de lignes **émises** | c'est ce que la liste contient ; les deux trames restent cohérentes |
| `new_item_count` | nombre de lignes émises dont `Confirmed == 0` | hypothèse de la fiche du lot 1 §7a, adossée à une colonne réelle du dump (`:435`) ; réserve §7b |
| Borne de retrait | `takeable = min(row.RestItemCount, 65535)` ; `count` accepté si `1 ≤ count ≤ takeable` | `count` de la 10005 est un u16 non signé, sans borne propre (§3.3) |

### 5.4 Repository : un contexte par appel, comme le stockage

`Game/DataAccess/Repositories/PaidItemRepository.cs` (+ interface dans
`Game/DataAccess/Repositories/Interfaces/`), calqué sur `StorageRepository` : injection de
`DbContextOptions<TelecasterContext>`, **un contexte par appel** — la raison est écrite dans le
dépôt (`StorageRepository.cs:16-20` : un contexte de longue durée rend les données périmées et son
change tracker grossit) — personnage résolu par son **nom** (`StorageRepository.cs:34-35`),
lectures en `AsNoTracking()` et `OrderBy(row => row.Id)` :

```csharp
Task<PaidItemEntity[]> GetVisibleAsync(string characterName);          // lecture, §5.5
Task<CommercialTakeoutResult> ConsumeAsync(string characterName, uint uid, ushort count); // §5.6, étape 6
Task<PaidItemEntity?> ResolveAsync(string characterName, uint uid);    // §5.6, étape 2, lecture seule
```

Le résultat de `ConsumeAsync` suit l'idiome de `StorageMoveResult`
(`Game/Services/StorageMoveResult.cs:6-8`) : un type de résultat avec issue explicite
(`UnknownCharacter`, `UnknownItem`, `InvalidCount`, `Moved`) et **aucun code envoyé au client**.
`ResolveAsync` applique exactement la requête de §5.2.3.

### 5.5 Lecture réelle à l'entrée en jeu

`Game/Network/Clients/Actions/GameActions.cs:284-290` (entrée en jeu de `OnLoginAsync`, avant
`client_info`, la position de rzu) : remplacer les deux envois constants par un appel au service,
qui lit les lignes visibles du personnage qui entre et envoie la paire — 10003 d'abord
(compteurs calculés), 10004 ensuite (lignes calculées) — dans cet ordre et à cette position. Le
commentaire `:280-283` (« no shop feeds this container in this repository, so the exact content is
empty ») doit être réécrit : il devient faux.

Valeurs émises :

| Champ | Valeur | Source |
|---|---|---|
| `total_item_count` | nombre de lignes visibles émises | §5.3 |
| `new_item_count` | nombre de ces lignes dont `Confirmed == 0` | §5.3, réserve §7b |
| `count` de la 10004 | nombre de lignes émises | §5.3 |
| `uid` d'une ligne | `(uint)row.Id` | §5.2 |
| `code` d'une ligne | `row.ItemCode` | §5.1 |
| `count` d'une ligne | `min(row.RestItemCount, 65535)` | §5.3 |

Un personnage sans livraison reçoit donc la paire **valide et vide** que le lot 1 livrait déjà
(11 octets à `0/0`, puis 9 octets à `count = 0`) : le comportement observable du lot 1 est le cas
dégénéré du lot 2, et les tests du lot 1 restent vrais.

### 5.6 Retrait réel (10005)

Le handler `GameClient.HandleTakeoutCommercialItem` (`Game/Network/Clients/GameClient.cs:317-329`)
devient asynchrone et délègue à un service, exactement comme le stockage
(`Game/Network/Clients/GameClient.cs:950-966` : lecture, `try`/`catch`, `_networkService.X.Service`,
appel fire-and-forget `_ = Handle…` à `:1601`). Enchaînement exigé, dans cet ordre :

1. **Contrôle de forme** : `TryReadTakeoutCommercialItem` (existant) ; toute longueur ≠ 13 est
   refusée, journalisée en `Warning`, sans réponse. Inchangé.
2. **Résolution, en lecture seule, propriétaire-scopée** : `ResolveAsync(characterName, uid)`
   (§5.2.3). Pas de ligne → journalisation `Debug` et **fin** : ce n'est pas une erreur serveur.
3. **Bornes** : `CommercialStorageRules.TryTakeable(row, count, out takeable)`. `count == 0` ou
   `count > takeable` → journalisation et fin, **aucun objet remis, aucune écriture**.
4. **Validation du code** : `IItemSortCatalog.Contains(row.ItemCode)` — le même garde que la
   commande `/item`, pour la même raison : « an unknown code would persist a row the client cannot
   render and the stat catalogue cannot read » (`Game/Services/GmCommands/GmCommandService.cs:422-427`).
   Code inconnu → journalisation et fin, aucune écriture.
5. **Remise au sac** : `ICharacterService.AddItemAsync(characterName, row.ItemCode, count)`
   (`Game/Services/CharacterService.cs:408-434`, déjà utilisée par `/item` et le ramassage au sol).
   Retour `null` → journalisation et fin (rien n'a été écrit) ; sinon, **trames d'inventaire
   ordinaires** pour la pile créée : `GameCharacterPackets.BuildInventory(new[] { added })`
   (`GmCommandService.cs:436-439`). C'est la direction que le dépôt s'est déjà donnée par écrit :
   « any answer that becomes necessary later goes through the ordinary inventory packets
   (`TM_SC_INVENTORY`, `TM_SC_UPDATE_ITEM_COUNT`), never through a 10005 »
   (`Game/Network/Clients/GameClient.cs:313-315`).
6. **Consommation** : `ConsumeAsync(characterName, uid, count)` — décrémente `RestItemCount` de
   `count` et alimente les colonnes du preneur : `TakenAccountId`, `TakenCharacterId`,
   `TakenCharacterName`, `TakenTime`. Le nom du preneur vient de `ConnectionInfo.CharacterName`
   (`Game/Network/Clients/ConnectionInfo.cs:139`) ; l'id de compte n'est **pas** pris dans la session
   (`ConnectionInfo.AccountId` existe, `:224`, mais c'est un `int` posé à l'authentification) : il est
   lu **en base** avec le personnage, comme le fait déjà `StorageRepository.AccountIdOf`
   (`StorageRepository.cs:111`), pour qu'il n'y ait qu'une seule source de vérité pour le contrôle
   de propriété.
7. **Rafraîchissement** (§5.7) puis journalisation `Debug` du retrait.

Ce que ce lot **ne fait pas**, volontairement :

- **aucun coût, aucun plafond d'objet, aucune condition de niveau, aucun délai** (§7c-§7f de la
  fiche du lot 1, réserve §7b) ;
- **aucune réponse dédiée** : pas de `ResultCode`, pas d'acquittement, et surtout **jamais de 10005
  serveur** (les deux tests du lot 1 tiennent) ;
- **aucune écriture de `Confirmed` ni de `ConfirmedTime`** : leur sens n'est pas établi (§7b) ;
- **aucune suppression de la ligne** quand `RestItemCount` tombe à 0 : la ligne reste, à `0`, et
  sort simplement de la liste (c'est le seul état « entièrement retirée » que le dump offre ; les
  colonnes `taken_*` n'ont de sens que si la ligne leur survit). Réserve §7c.
- **aucun filtrage sur `ValidTime` ni sur `ServerName`** (§7d, §7e).

**Atomicité, limite assumée.** La remise au sac (5) et la consommation (6) traversent deux
contextes différents ; le dépôt n'a pas de transaction inter-services. L'ordre choisi privilégie la
perte zéro : si (6) échoue, l'objet est au sac et la ligne garde son `RestItemCount` (un nouveau
retrait le remettrait une seconde fois). L'inverse perdrait un objet payé. Les deux étapes tournent
sous la **même** clé de `CharacterGate` que le stockage et l'inventaire
(`Game/Network/Clients/GameClient.cs` → service : `CharacterGate.RunAsync(characterName, …)`,
idiome de `StorageService.cs:28-37,51-52`), ce qui sérialise deux retraits concurrents du même
personnage et ferme la course entre (2) et (6). **Sans le gate, le lot est faux** : deux 10005
concurrentes passeraient toutes les deux la résolution, et l'objet serait livré deux fois.

### 5.7 Rafraîchissement après un retrait

Après un retrait réussi, le serveur renvoie la paire 10003 + 10004 recalculée. Motif : la fenêtre du
client est un **instantané** et aucune demande cliente ne permet de la rafraîchir (§2.2, fiche du
lot 1 §2.2) ; sans cette paire, le joueur verrait un objet qu'il a déjà pris. C'est un **choix
Navislamia**, pas un précédent (ni rzu ni NGemity n'émettent de 10004) : il est isolé en une ligne
et apparaît en réserve §7g avec son point de veto.

### 5.8 Câblage, fichiers touchés, zones de collision

| Fichier | Nature | Contenu |
|---|---|---|
| `Game/DataAccess/Entities/Telecaster/PaidItemEntity.cs` | **nouveau** | §5.1 |
| `Game/DataAccess/Contexts/TelecasterContext.cs` | modifié | `DbSet<PaidItemEntity> PaidItems` + configuration ; **index** sur (`AccountId`, `CharacterId`) — la lecture a lieu à **chaque** entrée en jeu, même précédent que `Version0009_LookupIndexes` et que le commentaire d'index du stockage (`TelecasterContext.cs:58-60`) |
| `Game/DataAccess/Repositories/PaidItemRepository.cs` + interface | **nouveaux** | §5.4 |
| `Game/DataAccess/Migrations/Telecaster/<ts>_Version0010_PaidItems.cs` (+ `.Designer.cs` + `TelecasterContextModelSnapshot.cs`) | **nouveaux / modifié** | migration **additive** : une table, un index, `Down` qui les retire. Dernière migration en place : `20260923075431_Version0009_LookupIndexes` |
| `Game/Services/CommercialStorageRules.cs` | **nouveau** | §5.3 |
| `Game/Services/CommercialStorageService.cs` + `Game/Services/Interfaces/ICommercialStorageService.cs` | **nouveaux** | §5.5, §5.6, §5.7 ; dépendances : repository, `CharacterGate`, `ICharacterService`, `IItemSortCatalog` |
| `Game/Network/NetworkService.cs` | modifié | champ `public readonly ICommercialStorageService CommercialStorageService;` (`:32` pour le modèle) + paramètre de constructeur (`:54-70`) |
| `DevConsole/Program.cs` | modifié | deux lignes d'enregistrement, à côté des lignes du stockage (`:284` repository, `:314` service) |
| `Game/Network/Clients/GameClient.cs` | modifié | handler 10005 (§5.6) ; garder le bras de dispatch existant (`:1413-1417`) |
| `Game/Network/Clients/Actions/GameActions.cs` | modifié | §5.5 (`:284-290` + commentaire) |
| `Tests/Game/StorageTestHarness.cs` | modifié | le constructeur de `NetworkService` y est appelé **positionnellement** (`:41-67`) : le nouveau paramètre doit être inséré au bon rang, avec un `A.Fake<ICommercialStorageService>()` |
| `Tests/Game/CommercialStoragePacketsTests.cs` et/ou `Tests/Game/CommercialStorageContainerTests.cs` | modifié / **nouveau** | §5.9 |

**Zones de collision — mesurées**, sur 69 branches distantes hors `master` (`git diff
--name-only origin/master...<branche>` pour chaque fichier du lot, réveil du lot 2) :

| Fichier | Branches ouvertes qui le modifient | Conséquence pour ce lot |
|---|---|---|
| `GameCommercialStoragePackets.cs` | **0** | libre : c'est là que le lot doit vivre en priorité |
| `GameActions.cs` | **0** | libre — les deux envois constants s'y remplacent sans conflit |
| `Tests/Game/CommercialStoragePacketsTests.cs` | **0** | libre |
| `Game/DataAccess/Contexts/TelecasterContext.cs` | **0** | libre : `DbSet`, configuration et index peuvent s'y poser sans conflit |
| `Game/DataAccess/Repositories/StorageRepository.cs` | **0** | le modèle à copier est stable |
| `docs/packet-specs/socle-stockage-commercial.md` | **0** | la fiche du lot 1 n'est touchée par personne |
| `Tests/Game/StorageTestHarness.cs` | **4** (`251-buy-item`, `252-sell-item`, `260-soulstone-craft`, `socle-booths-visibilite`) | **point chaud** : le constructeur positionnel de `NetworkService` (`:41-67`) est déjà modifié par quatre branches. Ajouter `A.Fake<ICommercialStorageService>()` **au même rang** que le paramètre de production, en fin de liste, et signaler `hotspot: Tests/Game/StorageTestHarness.cs` dans la MR |
| `DevConsole/Program.cs` | **12** | **point chaud** : deux lignes d'enregistrement, à côté des lignes du stockage (`:284` repository, `:314` service), et rien d'autre |
| `Game/Network/NetworkService.cs` | **8** | **point chaud** : un champ (`:32`) et un paramètre de constructeur (`:54-70`), rien de plus |
| `Game/Network/Clients/GameClient.cs` | **24** | **point chaud maximal** : le bras 10005 existe déjà (`:1413-1417`) — **ne pas** déplacer le dispatch, ne toucher que la méthode `:317-329` |
| `Game/Network/Packets/Enums/GamePackets.cs` | 23 | **ce lot n'y touche pas** : les trois ids sont déjà déclarés et le gating est inchangé (§4) |
| `Game/Network/Packets/Game/GameActionPackets.cs` | 12 | **ce lot n'y touche pas** : le lecteur de la 10005 existe |
| `Game/Services/CharacterService.cs` | 5 | lu, pas modifié (`AddItemAsync` suffit) |

Signalements à reprendre dans le compte rendu et la MR : `hotspot: Game/Network/Clients/GameClient.cs`
(24), `hotspot: DevConsole/Program.cs` (12), `hotspot: Game/Network/NetworkService.cs` (8),
`hotspot: Tests/Game/StorageTestHarness.cs` (4).

### 5.9 Tests exigés

Les 23 tests du lot 1 (`Tests/Game/CommercialStoragePacketsTests.cs`) restent, **y compris** les
deux verrous de réflexion (« le serveur n'émet pas de constructeur 10005 »,
`GameActionPackets` n'expose qu'un lecteur). Le lot 2 ajoute, et c'est là que la fiche doit mordre :

1. **Offsets et tailles, revalidés depuis les valeurs réelles** : une 10004 construite à partir
   d'une liste produite par `BuildEntries` porte `Length = 9 + 10 × n`, `count = n`, `uid` @9+10i,
   `code` @13+10i, `count` @17+10i ; une 10003 porte les deux compteurs aux offsets 7 et 9.
2. **Règles pures** (`Tests/Game/CommercialStorageRulesTests.cs`, nouveau) :
   - visibilité : commande d'un autre compte → exclue ; cible `CharacterId` d'un autre personnage
     → exclue ; cible nulle → **visible** ; `IsCancel` → exclue ; `RestItemCount == 0` → exclue ;
   - compteurs : `total` = lignes émises, `new` = celles à `Confirmed == 0` ;
   - adressage : une ligne d'`Id` = `uid + 2^32` **ne** correspond **pas** au même `uid` (le test
     qui verrouille §5.2.2) ; une ligne d'`Id > uint.MaxValue` ne produit pas d'entrée ;
   - bornes : `1 ≤ count ≤ min(rest, 65535)`, refus de `0` et de `rest + 1` ;
   - plafond : plus de 65535 lignes → 65535 émises, ordre stable.
3. **Service** (`CommercialStorageContainerTests.cs`, nouveau) — service réel + fakes
   (`FakeItEasy`, modèle `Tests/Game/GmCommandServiceTests.cs:46-51`), connexion
   `StorageTestHarness.FrameConnection` (`:104-130`) qui enregistre ce qui est poussé :
   - `uid` inconnu ou étranger → `ICharacterService.AddItemAsync` **jamais appelé**, rien d'écrit,
     **aucune** trame émise ;
   - `count = 0` ou `count > rest` → idem ;
   - code inconnu du catalogue → idem ;
   - retrait valide → `AddItemAsync` appelé avec `(nom, code, count)`, trames d'inventaire émises,
     `RestItemCount` décrémenté, colonnes `taken_*` renseignées, paire 10003/10004 réémise ;
   - **aucun** `ResultCode` émis pour une 10005, et **aucune** trame d'id 10005, dans tous les cas.
4. **Bout en bout du client** (harness) : un `OnDataReceived` d'une 10005 de 13 octets est remis au
   service (`A.Fake<ICommercialStorageService>`), une trame tronquée reste refusée sans réponse —
   modèle exact de `Tests/Game/StoragePacketsTests.cs:184-230`.

Le compte de tests de `master` est de **1302** (mesure de la carte) ; il ne doit pas baisser.

### 5.10 Producteur : hors lot, préalable d'infrastructure

**Rien dans ce dépôt ne peut faire entrer une ligne dans le conteneur.** Vérifié :
`grep -rni "paiditem\|paid_item\|itemshop" --include=*.cs .` ne renvoie rien, et
`grep -rn "COMMERCIAL" --include=*.cs Game/` ne renvoie que le socle du lot 1.

Et ce n'est pas un oubli du dépôt, c'est l'architecture officielle. La boutique d'objets est un
**service web externe** :

1. le binaire client contient l'URL de la boutique — `http://itemshop.rappelz.com/login.aspx`
   (VA `0xa48a34` dans `.rdata`) — et les chaînes de commande `open_item_shop` (VA `0xa4c650`) et
   `close_item_shop` (VA `0xa20234`) ;
2. la trame qui ouvre la boutique, `TM_SC_OPEN_ITEM_SHOP` (10001), transporte de quoi s'authentifier
   auprès d'elle : `client_id`, `account_id`, `one_time_password`, `raw_server_name`
   (`reference/rzu/librzu/src/packets/GameClient/TS_SC_OPEN_ITEM_SHOP.h:8-11`) — un jeton à usage
   unique, c'est-à-dire une **délégation de session à un tiers web** ;
3. la table `PaidItem` est écrite par ce tiers : elle porte `account_id`, `avatar_id`,
   `server_name` (la destination choisie **hors du jeu**) et `bought_time`/`valid_time`
   (`Telecaster.sql:421-429`) ;
4. la seule table du schéma officiel dont le profil corresponde à la 10004 est celle-là (§5.1) : le
   serveur de jeu **lit** ce que la boutique a écrit, et le remet au joueur.

Conséquence pour ce lot : la livraison de contenu est un **préalable d'infrastructure**
(boutique ou outil d'administration), pas un morceau de code à simuler. Le lot 2 se vérifie donc
soit par ses tests, soit **en jeu après amorçage manuel** de la table, par exemple :

```sql
-- à réécrire avec les noms de colonnes exacts de la migration Version0010
INSERT INTO "PaidItems" ("AccountId", "CharacterId", "CharacterName", "ItemCode", "ItemCount",
                         "RestItemCount", "BoughtTime", "Confirmed", "IsCancel",
                         "CreatedOn", "ModifiedOn")
SELECT c."AccountId", c."Id", c."CharacterName", 1234, 3, 3, now(), 0, false, now(), now()
FROM "Characters" c WHERE c."CharacterName" = '<personnage de test>';
```

(le `1234` est un code d'objet à remplacer par un code réel du catalogue, sinon le retrait est
refusé par le garde de §5.6.4).

**Recommandation pour Killian, pas pour ce lot** : si Navislamia doit rester vérifiable sans
boutique, la commande MJ la plus petite qui suffise est un `/paiditem <code> [nombre]` ajouté au
catalogue de `Game/Services/GmCommands/` (une entrée + une méthode : insertion d'une ligne pour le
personnage appelant, puis réémission de la paire). C'est un **outil d'administration**, pas une
politique de jeu — mais c'est une décision produit, et elle n'est pas prise ici. À ouvrir comme
carte séparée si Killian la veut ; le dev de ce lot **ne l'implémente pas**.
## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | Référence | Pourquoi |
|---|---|---|
| NGemity déclare les trois en-têtes et **ne traite rien** (fiche du lot 1 §5.1) ; Navislamia implémente le conteneur | `reference/ngemity/shared/Server/XPacket.h` (inclusions), aucun `grep -rni "COMMERCIAL" Chihiro/src` | il n'y a pas de logique à porter : l'implémentation est **dérivée du dump officiel**, et chaque dérivation est écrite (§5.1, §5.2, §5.6) au lieu d'être supposée |
| Navislamia ne touche **pas** à `ItemStorageEntity` / `StorageType` | `Game/DataAccess/Entities/Telecaster/ItemStorageEntity.cs`, `…/Enums/StorageType.cs` | c'est le conteneur « kept » (enchères, courrier) : un autre conteneur, avec des objets instanciés (`Item.keeping_id`). L'étendre aurait mélangé deux sémantiques (§5.1) |
| Navislamia n'ajoute **aucun** membre à la famille `ITEM_KEEPING` de NGemity (1350/1351/1352) | `reference/ngemity/shared/Server/ClientPackets.h:219-221`, `TS_SC_ITEM_KEEPING_LIST.h:7-21`, `TS_CS_ITEM_KEEPING_TAKE.h:7` | elle est voisine mais distincte : elle transporte un objet instancié complet et un retrait sans quantité. Elle sert ici de **preuve de forme** (§5.1.3), pas de modèle à copier |
| Le retrait n'a **aucune** implémentation de référence, ni chez NGemity ni chez rzu | même grep, plus `grep -rni "commercial" reference/rzu` (3 en-têtes + la seule émission `Character.cpp:303-311`) | l'algorithme de §5.6 est **dérivé des colonnes du dump** (`rest_item_count`, `taken_*`) ; le contrôle de propriété, lui, est une exigence de sécurité, pas un portage : il ne peut pas être « écarté » |
| NGemity compile en `EPIC_4_1_1` (fiche du lot 1 §4) | `reference/ngemity/revision.h.in.cmake` | son silence sur ces paquets ne prouve rien pour 7.3 : **le client tranche** |
| Noms d'entité et de table (`PaidItemEntity` / `PaidItems`), clé `long`, horodatages et suppression logique | `Game/DataAccess/Entities/Entity.cs:5-9` | conventions du dépôt ; les **colonnes** restent fidèles au dump, ligne par ligne (§5.1) |

## 7. `NON ÉTABLI` / `A VERIFIER PAR KILLIAN`

**a) La règle d'appartenance officielle.** Le dump donne deux colonnes de propriétaire
(`account_id` `:421`, `avatar_id` `:422`) et deux colonnes de preneur (`taken_account_id` `:434`,
`taken_avatar_id` `:430`), sans dire laquelle commande la liste. Décision implémentée :
`AccountId` **et** (`CharacterId` nul **ou** égal au personnage demandeur), `IsCancel` exclu. C'est
la lecture **la plus étroite compatible avec un preneur différent de la cible**, donc sans fuite
possible vers un autre joueur ; le coût est qu'une livraison de compte peut être prise par
n'importe quel personnage du compte. *Question : est-ce bien la règle voulue (livraison ciblée
personnage vs livraison de compte) ?* Réponse attendue de Killian ou de la future boutique.

**b) `new_item_count` et `confirmed`.** Sens non établi (fiche du lot 1 §7a). Décision : `new` =
lignes émises dont `Confirmed == 0` ; `Confirmed` et `ConfirmedTime` ne sont **jamais écrites** par
Navislamia. Conséquence assumée : si la boutique ne pose jamais `confirmed = 1`, `new` restera égal
à `total` — c'est ce que disent les données, pas une estimation. *Si Killian préfère la valeur de
rzu (`0`), c'est une ligne de `BuildCounters`.* Question ouverte : qui remet `confirmed` à 1, la
boutique ou le serveur de jeu au retrait ?

**c) Devenir de la ligne épuisée.** Décision : la ligne **reste**, `RestItemCount = 0`, et sort de la
liste ; les colonnes `taken_*` gardent la trace. L'alternative (supprimer la ligne, ou la marquer
supprimée) est une ligne de `ConsumeAsync` à changer si Killian veut l'autre convention.

**d) `ValidTime` stockée mais non appliquée.** Un objet expiré reste listé et prenable : aucune
référence ne dit ce que `valid_time` commande (le nom suggère une date limite, le dump ne dit pas
si le serveur refuse ou supprime). *Question : le serveur doit-il exclure les lignes dont
`ValidTime` est passée ?*

**e) `ServerName` stockée mais non filtrée.** `ServerOptions.Name` existe
(`Configuration/Options/ServerOptions.cs:5`) : filtrer dessus serait possible, mais une boutique qui
écrit un autre libellé rendrait les objets invisibles pour toujours. *Question : la livraison
inter-serveurs (colonnes `server_name` et `taken_server_name`) doit-elle être filtrée ?*

**f) Politique de retrait (héritée de la fiche du lot 1 §7b).** Toujours aucune référence : ni coût,
ni plafond, ni condition de niveau, ni délai, ni acquittement. Décision : rien de tout cela n'est
implémenté, et **aucune réponse** n'est envoyée. Point de veto : ajouter un coût ou un refus
explicite est un ajout de politique, qui appartient à Killian.

**g) Le rafraîchissement après un retrait (§5.7).** Choix Navislamia : la paire 10003 + 10004 est
réémise après un retrait réussi. Aucun précédent ; la fenêtre du client ne se rafraîchit pas
autrement. Point de veto : supprimer un appel.

**h) Atomicité remise / consommation (§5.6).** Deux contextes, aucune transaction : l'ordre retenu
privilégie la perte zéro (objet livré, ligne non consommée) au prix d'une duplication possible si la
consommation échoue et que le joueur recommence. Le `CharacterGate` ferme la course entre deux
retraits concurrents, pas une panne entre les deux écritures. *Question : faut-il un chemin
transactionnel, ou la duplication résiduelle est-elle acceptable ?*

**i) Le client utilise-t-il `count` comme plafond de saisie ?** Non établi : le convertisseur de la
10004 recopie la ligne telle quelle (fiche du lot 1 §3.2), et le chemin entre le `count` de la trame
et le contrôle d'IHM n'a pas été reconstitué. Si c'est un plafond, une ligne dont
`RestItemCount > 65535` se prendra en plusieurs fois (la trame ne peut pas porter davantage).

**j) `code` = code d'objet du catalogue.** Cohérent avec le reste du dépôt (`/item <code>` remet un
objet par son code, `IItemSortCatalog.Contains(code)`, `ItemEntity.ItemResourceId`) et avec le dump
(`item_code` int). Mais les objets de boutique « cash » peuvent venir d'un autre espace de codes :
*à vérifier sur un vrai catalogue de boutique.*

**k) Le producteur (§5.10).** Boutique web externe, donc hors code. Question ouverte : Navislamia
veut-il en plus un outil d'administration (`/paiditem`) pour rendre le conteneur vérifiable en jeu ?
Décision de ce lot : **non**, pas d'outil, SQL d'amorçage fourni.

**l) `total_item_count` / `new_item_count` = nombre de lignes, pas somme des quantités.** Autre
lecture possible de « total item count ». Aucune référence ne tranche ; la cohérence avec la
longueur de la liste (nombre de lignes, §3.2) plaide pour les lignes.

**m) Bornes d'émission.** Décisions techniques prises ici, pas observées : exclusion des lignes
d'`Id > uint.MaxValue` (§5.2.1), `count` plafonné à 65535 (§5.3), liste plafonnée à 65535 lignes
(§5.3). Vérifiables par les tests du §5.9.

**n) 10002 `TM_SC_OPEN_PAID_STORAGE` : deux références se contredisent.** rzu le déclare **sans
aucun champ** (`TS_SC_OPEN_PAID_STORAGE.h:5`, `_DEF` vide) ; le client 7.3 lui donne une entrée dans
la table de saut du convertisseur (VA `0x67f6d0` → `0x67ec63`), donc un chemin de réception, et
`op_codes.md` ne l'inventorie pas (`:274` puis `:275`). Je n'ai **pas** reconstitué la cible, qui est
un thunk vers `0x67e8d0`. Conséquence : ce socle n'émet pas 10002 et n'en dépend pas. *Question :
qui a raison, et ce paquet ouvre-t-il la fenêtre côté serveur ?* À traiter avec la famille
10000/10001 (MR #33), pas ici.

**o) Fiche du lot 1, réserve §7j, close.** Les entrées `10010`, `10011`, `10012` du client sont des
ids de la session **Auth ↔ Client** (`TS_CA_ACCOUNT = 10010` et voisins,
`reference/ngemity/shared/Server/ClientPackets.h:307-314`) et non des paquets de jeu : l'inventaire
du jeu commercial s'arrête bien à 10005. Ce qui restait ouvert sur l'inventaire des ids est donc
refermé.

## 8. Commits et binaires épinglés

| Référence | Commit / empreinte | Usage dans cette fiche |
|---|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | structures et ids §3, gating §4, `EPIC_7_3`/`EPIC_9_6_3` (`librzu/src/lib/Packet/PacketEpics.h:59,96`), champs SSO de la 10001 §5.10, `_DEF` vide de 10002 §7n, unique émission de référence (`rzgame/src/Component/Character/Character.cpp:303-311`) |
| `reference/ngemity` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | schéma officiel `Telecaster.sql` (`Item` `:339-370` et `keeping_id` `:345`, `ItemKeeping` `:403-414`, `PaidItem` `:419-439`) §5.1 ; famille `ITEM_KEEPING` `ClientPackets.h:219-221` §5.1/§6 ; absence de logique `Chihiro/` §6 ; ids de la session Auth↔Client `ClientPackets.h:307-314` §1/§7o |
| `Navislamia` (`master`) | `b56967a07430422add88e0e5cdf292b41b18f6c6` | état de départ du lot 2 : `GamePackets.cs:226-228`, `GameCommercialStoragePackets.cs:48,66`, `GameClient.cs:317-329` et `:1413-1417`, `GameActions.cs:284-290`, `StorageRepository.cs:16-20,54,111,120-124`, `CharacterService.cs:408-434`, `GmCommandService.cs:422-427`, `Tests/Tests.csproj:13-22`, `Tests/Game/StorageTestHarness.cs:41-67,104-130` |
| Client de référence | `SFrame.exe`, SHA-256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | registre des noms de paquets (VA `0x6792cc`-`0x67943d`) §1 ; table de saut du convertisseur (VA `0x67f6cc` = `[0x67ecd4, 0x67ec63, 0x67ed27, 0x67ed64]`) §1/§7n ; chaînes `.rdata` : `http://itemshop.rappelz.com/login.aspx` `0xa48a34`, `open_item_shop` `0xa4c650`, `close_item_shop` `0xa20234`, `commercial_shop` `0xa2d934`, `/cshop` `0xa1fcf8`, `/cstorage` `0xa1fd00` §5.10 |

## 9. Découpage de livraison

**Une seule livraison de code** (le lot 2 de la fiche du lot 1 §9.2), dans cet ordre :

1. `PaidItemEntity` + `DbSet` + index + migration `Version0010` (§5.1, §5.8) ;
2. `CommercialStorageRules` + ses tests (§5.3, §5.9.2) — à faire **avant** le service : c'est la
   partie qui doit mordre, et elle ne dépend d'aucune base ;
3. `PaidItemRepository` + interface (§5.4) ;
4. `CommercialStorageService` + interface + câblage (`NetworkService`, `DevConsole/Program.cs`,
   `StorageTestHarness`) (§5.8) ;
5. lecture réelle à l'entrée en jeu (§5.5) ;
6. retrait réel + rafraîchissement (§5.6, §5.7) + tests de service et de bout en bout (§5.9.3-4).

**Point de coupe**, si la place manque : s'arrêter après 5 (conteneur persisté et lu, retrait
toujours journalisé) est un état **cohérent** et vérifiable, à condition de le dire dans le compte
rendu et de laisser la fiche à jour. Couper avant 4 (service non câblé) ne laisse rien de
vérifiable : ne pas le faire.

**Hors lot, explicitement** : le producteur (§5.10), la 10002 (§7n), la famille 10000/10001 (MR #33),
tout ajout de politique de retrait (§7f).

## 10. Bloc `CLAUDE.md` et description de la MR

Le dev **n'écrit pas** `CLAUDE.md` (fichier protégé). Le bloc ci-dessous est destiné à la
description de la MR, où la QA le reprendra telle quelle ; il remplace, pour ce socle, le bloc de la
fiche du lot 1 §10.3.

```markdown
### Socle stockage commercial — lot 2 : conteneur persisté (10003/10004) et retrait réel (10005)

- Le contenu du stockage commercial vient d'une table **`PaidItems`** calquée sur `Telecaster.PaidItem`
  du dump officiel (`reference/ngemity/Database/Telecaster.sql:419-439`) : `item_code` → `code`,
  `rest_item_count` → `count`, `taken_*` horodatés au retrait. Le conteneur « kept » du dépôt
  (`ItemStorageEntity`, ex-`ItemKeeping`, `StorageType` enchères/courrier) **n'est pas** ce
  conteneur : ne pas l'étendre.
- L'`commercial_item_uid` est **écrit par le serveur** (`(uint)ligne.Id`) et le client le recopie
  verbatim : la résolution d'un `uid` porte **toujours** le propriétaire (`AccountId` + cible
  `CharacterId` nulle ou égale) et compare l'`Id` sur **64 bits** (`row.Id == uid`), jamais
  `(uint)row.Id == uid`. Un `uid` étranger ne doit jamais résoudre.
- Le retrait (10005) remet l'objet au sac par `ICharacterService.AddItemAsync` et les trames
  d'inventaire ordinaires, décrémente `rest_item_count`, horodate `taken_*` et réémet la paire
  10003/10004. Le serveur **n'émet jamais 10005**, n'envoie **aucun `ResultCode`** et n'invente
  **ni coût, ni plafond, ni acquittement**. `Confirmed`/`ConfirmedTime` sont lues, jamais écrites.
- Le producteur est **hors code** : la boutique d'objets est un service web externe (URL
  `http://itemshop.rappelz.com/login.aspx` dans le binaire client ; la 10001 transporte
  `client_id`/`account_id`/`one_time_password`). Sans boutique ni outil d'administration, la table
  `PaidItems` reste vide : la paire émise est alors celle du lot 1 (11 o à 0/0, puis 9 o), mais elle
  est désormais **lue** en base à chaque entrée en jeu, plus constante.
- La table `PaidItems` et sa migration `Version0010_PaidItems` font partie de la livraison : sans
  elle, chaque entrée en jeu échoue sur la lecture du conteneur.
- Ne **jamais** tenir `CharacterGate` autour de l'appel à `ICharacterService.AddItemAsync` pendant un
  retrait : le gate n'est pas réentrant (`Game/Services/CharacterGate.cs:26-40`) et `AddItemAsync`
  prend la même clé (`Game/Services/CharacterService.cs:554-561`) — le retrait se bloquerait pour
  toujours. C'est la raison de la fenêtre d'atomicité de la réserve 7h (fiche §11, écart 1).
- Les fiches du savoir durable sont `docs/packet-specs/socle-stockage-commercial.md` (protocole) et
  `docs/packet-specs/socle-stockage-commercial-conteneur.md` (conteneur, propriété, retrait).
```

## 11. Implémentation — état du lot 2

Livré sur la branche `hermes/packet-socle-stockage-commercial-conteneur` (branche créée par
l'archéologue, base `master` `b56967a`), en cinq commits :

| Commit | Contenu |
|---|---|
| `d75dc03` | `PaidItemEntity`, `DbSet` + index, migration `20260927132211_Version0010_PaidItems` (additive : l'instantané ne fait que des ajouts, 75 lignes, 0 suppression) |
| `bb3fd4b` | `CommercialStorageRules`, `CommercialTakeoutResult`, `IPaidItemRepository`/`PaidItemRepository`, `ICommercialStorageService`/`CommercialStorageService` |
| `58f4167` | câblage : `NetworkService`, `DevConsole/Program.cs`, `GameActions`, `GameClient`, `StorageTestHarness` |
| `92da8d3` | tests des règles et du conteneur |
| `6b00ff4` | verrou de l'`uid` (§5.2.2), SQL du prédicat de propriété, bras 10005 |

**Preuves** (conteneur de dev, aucun service démarré) : `dotnet build Navislamia.sln -c Debug` → **0
erreur** (164 avertissements, tous préexistants) ; `dotnet test Tests/Tests.csproj` → **1363 réussis,
0 échec**. Départ mesuré sur la branche avant le lot : **1302** (§5.9). **61 tests ajoutés**, dans
`Tests/Game/CommercialStorageRulesTests.cs`, `Tests/Game/CommercialStorageContainerTests.cs`,
`Tests/DataAccess/TelecasterPaidItemModelTests.cs` et `Tests/DataAccess/PaidItemRepositoryQueryTests.cs`.

**Écarts avec le plan de la fiche — à connaître avant de relire :**

1. **Le gate ne tient pas une section unique de (2) à (6) (§5.6).** `CharacterGate` est un
   `SemaphoreSlim(1, 1)` **non réentrant** (`Game/Services/CharacterGate.cs:26-40`) et
   `ICharacterService.AddItemAsync` prend **la même clé** (`Game/Services/CharacterService.cs:554-561`
   appelée par `:408-434`) : tenir le gate de la résolution à la consommation et appeler `AddItemAsync`
   dedans **bloque pour toujours** (le bras 10005 est en tâche de fond, donc le blocage serait un
   retrait qui ne se termine jamais, pas une boucle de réception arrêtée). Chaque étape passe donc par
   le gate séparément — **la même clé**, l'idiome de `StorageService` — ce qui sérialise bien les
   écritures en base, mais laisse ouverte la fenêtre entre (5) et (6). Conséquence : deux 10005
   concurrentes du même personnage peuvent encore livrer deux fois ; c'est exactement l'atomicité
   assumée de la réserve **7h** (§6, item 6 ci-dessous). La fiche ne peut pas être tenue plus loin sans
   toucher `CharacterGate` (réentrance) ou déplacer la remise au sac sous le même verrou, l'un et
   l'autre **hors périmètre** de ce lot.
2. **`CommercialStorageRules` porte trois membres publics de plus que la liste du §5.3** :
   `IsAddressable` et `IsRenderable` (les deux refus que la fiche veut journaliser, réutilisés par le
   service) et `EmittedRows` — parce que `total_item_count` doit compter les lignes **émises** (§5.3) et
   que les deux trames doivent rester cohérentes. `BuildEntries` choisit exactement les lignes
   `EmittedRows` sélectionne : un test le verrouille.
3. **`PaidItemRepository.VisibleRows` est `public`** et non privé : c'est la seule façon d'exercer
   réellement le prédicat de propriété (§5.2.3) dans un conteneur sans PostgreSQL, en lisant le SQL
   produit (`ToQueryString`). Le test vérifie les quatre conditions et l'absence de troncature de
   l'`Id`.
4. **Un second appelant positionnel de `NetworkService` existait, absent du §5.8** :
   `Tests/Game/ResurrectionPacketTests.cs:602-630`. Il a reçu le même
   `A.Fake<ICommercialStorageService>()` en fin de liste.

**Ce que les tests ne prouvent pas** (à dire tel quel en relecture) :

- `ConsumeAsync` **n'est pas exécuté contre une base** : aucun moteur n'est disponible ici (pas de
  SQLite/InMemory dans `Tests/Tests.csproj`, pas de PostgreSQL dans le conteneur de dev, et il est
  interdit d'en démarrer un). La décrémentation de `RestItemCount` et l'écriture de
  `TakenAccountId`/`TakenCharacterId`/`TakenCharacterName`/`TakenTime`
  (`Game/DataAccess/Repositories/PaidItemRepository.cs:57-89`) sont donc prouvées **par lecture**, pas
  par exécution ; tout ce qui l'entoure est testé. À confirmer au premier démarrage réel (item 9
  ci-dessous).
- La paire émise est mesurée **au plafond** (65 535 lignes, 655 359 octets) et sur deux lignes, mais
  jamais sur une base réelle : le conteneur reste vide tant que le producteur (§5.10) est hors code.

## A VERIFIER PAR KILLIAN

1. **Propriété (§7a)** : livraison ciblée sur un personnage vs livraison de compte — la règle
   implémentée est la plus étroite (aucune fuite possible vers un autre compte). À confirmer ou à
   restreindre.
2. **`new_item_count` (§7b)** : dérivé de `confirmed == 0` ; rien n'écrit `confirmed`. Si la valeur
   de rzu (`0`) est préférée, c'est une ligne dans `BuildCounters`.
3. **Politique de retrait (§7f)** : aucun coût, aucun plafond, aucune réponse — comme au lot 1.
4. **`ValidTime` (§7d)** et **`ServerName` (§7e)** : stockées, non appliquées, non filtrées.
5. **Rafraîchissement après retrait (§7g)** et **ligne conservée à zéro (§7c)** : choix Navislamia,
   chacun tenant en une ligne.
6. **Atomicité (§7h)** : remise au sac puis consommation, sans transaction — duplication possible en
   cas de panne entre les deux.
7. **Producteur (§5.10, §7k)** : boutique externe = préalable d'infrastructure ; l'outil
   d'administration (`/paiditem`) n'est **pas** dans ce lot et reste une décision produit.
8. **10002 (§7n)** : rzu (zéro champ) et le client (branche de réception) se contredisent ; hors lot.
9. **`ConsumeAsync` non exécuté contre une base (§11)** : la décrémentation de `RestItemCount` et
   l'écriture des colonnes `taken_*` ne sont pas exercées par les tests — aucun moteur n'est
   disponible dans le conteneur de dev (pas de PostgreSQL, pas de SQLite/InMemory dans
   `Tests/Tests.csproj`), et il est interdit d'en démarrer un. À confirmer au premier démarrage réel.
10. **Gate du retrait (§5.6, §11 écart 1)** : la fenêtre entre la remise au sac (5) et la
   consommation (6) reste ouverte — `CharacterGate` n'est pas réentrant et `AddItemAsync` de
   `ICharacterService` prend la même clé, donc le tenir d'un bout à l'autre du retrait le bloquerait.
   Même conséquence que la réserve **7h** (item 6) ; à fermer plus tard par une réentrance du gate ou
   un verrou de service, hors périmètre de ce lot.
