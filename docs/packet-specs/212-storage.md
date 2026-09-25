# 212 — `TM_CS_STORAGE` (lot résiduel du socle « entrepôt du personnage »)

Cette fiche est le **lot résiduel** : elle reprend les points restés `NON ÉTABLI` dans
`docs/packet-specs/211-212-storage.md` (§7, points 1 à 8), les tranche par lecture quand c'est
possible, et laisse nommés ceux qui demandent Killian. Elle **ne remplace pas** le socle : les
sections d'identité, de structure et de gating y sont rappelées avec leurs sources pour que le dev
n'ait pas à ouvrir deux fichiers, mais le socle reste la fiche de référence du dépôt livré.

> Le nom `docs/packet-specs/socle-entrepot-personnage.md` cité par la carte Trello n'existe pas :
> le socle s'appelle `docs/packet-specs/211-212-storage.md` et il est déjà sur `master` à la base
> de cette fiche. Aucun second fichier de socle n'est créé.

Base de rédaction : Navislamia `master` `b56967a` (le socle 211/212 y est mergé). Tout est vérifié
sur cette base ; les commits de référence sont épinglés en §8.

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id (Epic 7.3) | **212** | `op_codes.md:61` (`[212] = "TM_CS_STORAGE"`) ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_STORAGE.h:14-16` (`X(212, version < EPIC_9_6_3)`) |
| id (≥ 9.6.3) | 1212 — **hors 7.3**, ne pas le déclarer | `TS_CS_STORAGE.h:15-16` |
| nom | `TM_CS_STORAGE`, `SessionPacketOrigin::Client` | `TS_CS_STORAGE.h:14-18` ; `Game/Network/Packets/Enums/GamePackets.cs:42` |
| réponse d'ouverture | 211 `TM_SC_OPEN_STORAGE` (1211 ≥ 9.6.3) | `op_codes.md:60` ; `librzu/src/packets/GameClient/TS_SC_OPEN_STORAGE.h:10-12` |
| trame de refus | 0 `TM_SC_RESULT` (1000 ≥ 9.6.3) — **id 0 en 7.3** | `librzu/src/packets/GameClient/TS_SC_RESULT.h:12-14` ; `Game/Network/Packets/Game/TS_SC_RESULT.cs:6-17` |
| client 7.3 | les deux noms sont dans la table d'id du binaire : 211 ↔ `TM_SC_OPEN_STORAGE` (longueur déclarée `0x12` = 18) en `.text 0x676522-0x676558` ; 212 ↔ `TM_CS_STORAGE` (longueur `0xd` = 13) en `.text 0x676598-0x6765ce` | `reference/client73/SFrame.exe` (sha256 §8), lecture directe du désassemblage |

## 2. Ce que le joueur fait pour que le client l'envoie

1. **Ouvrir** la fenêtre : le dialogue du PNJ déclenche la routine cliente `open_storage()`
   (chaîne `open_storage()` en `.rdata 0xa2e694`, unique référence en `.text 0x57d01c`, qui est la
   table d'enregistrement des fonctions de dialogue du client). Le **serveur** ouvre par
   `StorageService.OpenAsync` (fiche socle §5.3), appelé par le dialogue PNJ — rien d'autre
   n'ouvre l'entrepôt.
2. **Déplacer** un objet ou de l'or, puis **fermer** : c'est ce qui émet le `212`. Le constructeur
   de la trame est unique (`.text 0x48e8f0`, §3) et son **seul appelant direct est `.text 0x49d673`**
   (dans la fonction `0x49cd00`), elle-même appelée depuis **10 sites** : `.text 0x57dfed`,
   `0x640bc7`, `0x640bda`, `0x681bc7`, `0x68773f`, `0x6879ab`, `0x687dd1`, `0x687eb9`, `0x68b28b`,
   `0x68e895`. Le geste est donc un **helper d'envoi partagé**, pas un bouton unique ; la
   correspondance bouton → mode n'est pas établie (§7.3).
3. **Fermer** la fenêtre : le mode est comparé à `4` dans le constructeur lui-même
   (`.text 0x48e92d`), et la trame est **émise quand même** (l'envoi virtuel est en
   `.text 0x48e965-0x48e977`, atteint quel que soit le mode). Le client 7.3 **sait donc émettre le
   mode 4** (§7.4), sans qu'on sache s'il le fait à la fermeture.

## 3. Structure sur le fil

### 3.1 `212` `TM_CS_STORAGE` — **20 octets** (en-tête 7 + charge 13)

L'en-tête est `Length` (uint32) + `ID` (uint16) + `Checksum` (uint8) = 7 octets, identique à celui
que le client construit lui-même. Chaque ligne est vérifiée sur la **table de génération du client**
(le client écrit lui-même cette trame pour tous les modes, y compris 4) :

| Offset | Taille | Type | Nom | Valeur / origine | Source |
| --- | --- | --- | --- | --- | --- |
| 0 | 4 | uint32 | `Length` | `0x14` = 20 | SFrame.exe `.text 0x48e913` (`mov DWORD PTR [ebp-0x14],0x14`) |
| 4 | 2 | uint16 | `ID` | `0xd4` = 212 | `.text 0x48e8fb` (`mov ecx,0xd4`) puis `0x48e906` (`mov WORD PTR [ebp-0x10],cx`) |
| 6 | 1 | uint8 | `Checksum` | somme des 6 octets précédents (boucle `0x48e81a-0x48e928`), écrite par `mov BYTE PTR [ebp-0xe],dl` en `0x48e934` | idem |
| 7 | 4 | uint32 | `item_handle` | champ `+0x13` de l'objet source | `.text 0x48e934-0x48e937` (`mov edx,[ecx+0x13]` ; `mov [ebp-0xd],edx`) |
| 11 | 1 | int8 | `mode` | champ `+0x1f` de l'objet source, comparé à `4` | `.text 0x48e946-0x48e949` (`mov dl,[ecx+0x1f]` ; `mov [ebp-0x9],dl`) ; comparaison `0x48e92d` |
| 12 | 8 | int64 | `count` | dword bas = `+0x17`, dword haut = `+0x1b` | `.text 0x48e93a-0x48e943` (`[ebp-0x8]` puis `[ebp-0x4]`) |

Types et ordre côté protocole : `rzu librzu/src/packets/GameClient/TS_CS_STORAGE.h:7-12` —
`item_handle` (`ar_handle_t`), `mode` (`int8_t`), alors que le `count` est déclaré
`_(def)(simple)(int64_t, count)` avec deux `_(impl)`. La trame client (20 octets) et la déclaration
rzu se superposent champ à champ.

Côté serveur, la lecture est déjà livrée : `Game/Network/Packets/Game/GameActionPackets.cs:133-147`
(`TryReadStorage`, `packetLength = HeaderSize + 13`), avec le `count` lu en **int64** et le `mode`
en `byte`. Le mode est typé `int8_t` par rzu et `byte` par le dépôt : la différence ne se voit que
sur les valeurs ≥ `0x80`, que `StorageRules.IsKnownMode` refuse de toute façon (`mode <= 4`).

### 3.2 `211` `TM_SC_OPEN_STORAGE` — **7 octets en 7.3**

`rzu TS_SC_OPEN_STORAGE.h:7-8` : `_(simple)(int32_t, maxStorageItemCount, version >= EPIC_7_4, 10000)`
— le champ n'existe **qu'à partir d'`EPIC_7_4`**. En 7.3 la trame est l'en-tête seul, sans charge.
Le dépôt envoie exactement cela (`Game/Network/Packets/Game/GameStoragePackets.cs`, `BuildOpenStorage`)
et la fiche socle §3.2 le documente. La question de la tolérance du client à 4 octets surnuméraires
reste ouverte et est traitée en §7.1.

### 3.3 Refus — `TM_SC_RESULT` (id 0 en 7.3) — **15 octets**

Charge 8 octets : `request_msg_id` (uint16) + `result` (uint16) + `value` (int32)
(`rzu TS_SC_RESULT.h:7-10` ; `Game/Network/Packets/Game/TS_SC_RESULT.cs:6-17`), construit par
`GameClient.SendResult(id, result, value)` (`Game/Network/Clients/GameClient.cs:68-72`). Total
7 + 8 = **15 octets**. NGemity passe l'id du paquet reçu et le `item_handle` reçu comme `value`
(`WorldSession.cpp:1596`, `1610`, `1614`, `1646`). Un test d'offsets existant couvre déjà cette
trame ailleurs dans `Tests/` ; le lot résiduel n'a pas à la re-spécifier.

## 4. Gating de version

`EPIC_7_3` est la cible : les trois gating de rzu sont **tranchés pour 7.3**.

| Champ / élément | Gating rzu | Décision 7.3 | Source |
| --- | --- | --- | --- |
| `count` de `TS_CS_STORAGE` | `int64_t` si `version >= EPIC_4_1_1`, `uint32_t` sinon | **int64 signé** (7.3 > 4.1.1) | `TS_CS_STORAGE.h:10-12` |
| id `TS_CS_STORAGE` | 212 si `version < EPIC_9_6_3`, 1212 sinon | **212** ; 1212 non déclaré | `TS_CS_STORAGE.h:14-16` |
| id `TS_SC_OPEN_STORAGE` | 211 si `version < EPIC_9_6_3`, 1211 sinon | **211** ; 1211 non déclaré | `TS_SC_OPEN_STORAGE.h:10-12` |
| `maxStorageItemCount` de `TS_SC_OPEN_STORAGE` | `version >= EPIC_7_4` (défaut 10000) | **absent** : la trame 7.3 fait 7 octets | `TS_SC_OPEN_STORAGE.h:7-8` |
| id `TS_SC_RESULT` | 0 si `version < EPIC_9_6_3`, 1000 sinon | **0** | `TS_SC_RESULT.h:12-14` |

Le binaire client corrobore l'absence du champ : `SFrame.exe` ne contient **aucune** occurrence de
la chaîne `maxStorageItemCount` (`strings -n 6`, 36 813 chaînes). Le gating `EPIC_7_4` n'est donc
pas un simple retard de rzu.

## 5. Traitement attendu

### 5.1 Ce que NGemity fait — `WorldSession::onStorage` (`Chihiro/src/Network/GameNetwork/WorldSession.cpp:1590-1676`)

| Ligne | Comportement |
| --- | --- |
| 1595-1596 | pas d'entrepôt ouvert, sort en cours, cible d'échange, ou joueur non actable → `NOT_ACTABLE` (5) |
| 1600 | `switch ((STORAGE_MODE)mode)` — **aucun contrôle de borne** sur le mode |
| 1601-1602 | modes 0 et 1 (objets) |
| 1603-1604 | `count <= 0` → `NOT_ENOUGH_MONEY` (10) — **ce test est dans les modes 0/1 seulement** |
| 1608-1610 | objet introuvable dans le monde → `NOT_EXIST` (1) |
| 1613-1614 | propriétaire différent → `ACCESS_DENIED` (6) |
| 1619-1624 | en dépôt : objet à drapeau `ITEM_FLAG_EVENT` portant une invocation **liée à un emplacement de ceinture** (`m_aBindSummonCard`) → `ACCESS_DENIED` |
| 1633 | `MoveInventoryToStorage` (qui commence par `IsErasable`, `Player.cpp:2999-3002`) |
| 1636 | `MoveStorageToInventory` |
| 1638 | `Save(true)` après un déplacement d'objet |
| 1641 / 1656 | modes 2 / 3 (or) |
| 1643-1644 / 1657-1658 | solde insuffisant → **retour silencieux** (aucune réponse) |
| 1645-1646 / 1659-1660 | dépassement de borne → `TOO_MUCH_MONEY` (53) |
| 1650 / 1664 | `ChangeGold` **et** `ChangeStorageGold` doivent réussir, puis `Save(true)` |
| 1670-1672 | mode 4 : `m_bIsUsingStorage = false`, **aucune réponse** |
| 1673-1674 | mode inconnu : `default: break` silencieux |

Bornes d'or, **établies** : `MAX_GOLD_FOR_STORAGE = 100000000000` et
`MAX_GOLD_FOR_INVENTORY = 100000000000` (`Chihiro/src/Entities/Item/ItemTemplate.hpp:4-5`, toutes
deux `constexpr int64_t`). C'est une **politique serveur** (aucun champ de trame), donc portable
telle quelle si Killian la valide ; voir `## A VERIFIER PAR KILLIAN`.

Où NGemity range l'or d'entrepôt (point le plus structurant du lot) :

* une **ligne d'objet factice `code = 0`** par compte : `UPDATE Item SET cnt = ? WHERE account_id = ?
  AND owner_id = 0 AND auction_id = 0 AND keeping_id = 0 AND code = 0`
  (`reference/ngemity/shared/Database/Implementation/CharacterDatabase.cpp:97`, entrée
  `CHARACTER_UPD_STORAGE_GOLD` en `shared/Database/Implementation/CharacterDatabase.h:42`) ;
  **le chemin `Chihiro/src/Database/Implementation/CharacterDatabase.cpp:93-97` cité par la fiche
  socle §5.1 n'existe pas dans ce clone** : le fichier est sous `shared/`, pas sous `Chihiro/src/` ;
* cette ligne est **reconnue par son `code == 0` et jamais poussée dans `m_Storage`**
  (`Player.cpp:318-324` : `if (code == 0) { … ChangeStorageGold(+cnt) … continue; }`), créée si elle
  manque (`Player.cpp:384-390`), et son UID est gardé dans
  `PLAYER_FIELD_STORAGE_GOLD_SID` (`Object.h:113`, posé `Player.cpp:333` et `:386`) ;
* l'accès passe par `PLAYER_FIELD_STORAGE_GOLD` (`Object.h:112`), `GetStorageGold`
  (`Player.h:142`), `ChangeStorageGold` avec contrôle de borne et rejet négatif
  (`Player.cpp:2939-2956`).

**Le piège symétrique côté Navislamia** : la requête d'entrepôt du dépôt ne filtre pas les lignes
« sans objet » — `StorageRepository.StorageRows` retient `AccountId == accountId && CharacterId == null
&& AuctionId == null && StorageId == null` (`Game/DataAccess/Repositories/StorageRepository.cs:121-124`).
Une ligne d'or `code = 0` (donc `ItemResourceId == 0` côté dépôt) **satisfait ces quatre conditions**
et serait listée comme un objet d'entrepôt, puis envoyée au client. Si le dev retient la conception
NGemity, le filtre `ItemResourceId != 0` doit être ajouté **partout où la liste d'entrepôt est
construite**, pas seulement dans la requête de lecture.

### 5.2 Ce que le serveur Navislamia doit répondre

* mode inconnu (> 4), entrepôt non ouvert, ou **modes d'or 2/3 aujourd'hui** → `TM_SC_RESULT`,
  id 0, 15 octets, `result = NotActable (5)`, `value = item_handle` reçu
  (`Game/Services/StorageService.cs:92-101` et `:107-114`) ;
* `count <= 0` → `NotEnoughMoney (10)` (`StorageService.cs:118-123`) — y compris modes d'or, écart
  assumé avec NGemity dont le test est dans les modes 0/1 seulement ;
* mode 4 → état local `StorageSecurityCheck = false`, **aucune réponse** (`StorageService.cs:103-107`) ;
* déplacement réussi → `TM_SC_DESTROY_ITEM` (254) pour le handle source puis la liste d'inventaire
  (`GameCharacterPackets.BuildInventory`) pour la destination ; déplacement partiel →
  `TM_SC_UPDATE_ITEM_COUNT` (255) pour le reste, puis la même liste
  (`StorageService.cs:130-146` ; ids dans `GamePackets.cs:48-49`) ;
* handle inconnu → `NotExist (1)` ; ligne d'un autre propriétaire → `AccessDenied (6)` ;
  personnage disparu → `NotActable (5)` (`StorageService.cs:147-162`).

### 5.3 État livré par le socle (chemins vérifiés sur `master` `b56967a`)

`Game/Services/StorageService.cs` (188 l.), `Game/Services/StorageRules.cs` (152 l.),
`Game/Services/StorageMoveResult.cs` (42 l.), `Game/Services/Interfaces/IStorageService.cs` (17 l.),
`Game/Network/Packets/Game/GameStoragePackets.cs` (47 l.),
`Game/DataAccess/Repositories/StorageRepository.cs` (135 l.),
`Game/DataAccess/Entities/Telecaster/ItemStorageEntity.cs`,
`Game/DataAccess/Entities/Enums/StorageType.cs`, plus `Tests/Game/StoragePacketsTests.cs` (249 l.),
`StorageRulesTests.cs`, `StorageServiceTests.cs`, `StorageTestHarness.cs`,
`Tests/Game/CommercialStoragePacketsTests.cs`.

Primitives réellement présentes dans `StorageRules` : `ItemToStorage = 0`, `ItemToInventory = 1`,
`GoldToStorage = 2`, `GoldToInventory = 3`, `CloseMode = 4`, `IsKnownMode`, `IsItemMode`,
`IsGoldMode`, `MovesToStorage`, `MoveCount`, `NextFreeIndex`, `IsInventoryRow`, `IsStorable`,
`IsStorageRow`, `Own`, `Divide` (`StorageRules.cs:22-151`). **La primitive `IsErasable` annoncée par
la carte n'existe pas** : la clause de port est portée par `IsStorable` (`StorageRules.cs:95`),
et c'est le seul morceau d'`IsErasable` livré.

## 6. Écarts assumés avec NGemity

1. **Mode inconnu** : NGemity le laisse tomber (`default: break`, 1673-1674) ; le dépôt répond
   `NotActable`. Assumé : un client qui se trompe de mode doit le savoir.
2. **Solde d'or insuffisant** : NGemity retourne silencieusement (1643-1644, 1657-1658) ; le dépôt
   répond `NotActable` sur les seuls modes d'or, aujourd'hui fermés.
3. **`count` trop grand** : NGemity abandonne le déplacement sans répondre
   (`Player.cpp:2983-2987`), le dépôt **clampe** (`StorageRules.MoveCount`) pour que les trames
   d'objet annoncent la quantité réelle.
4. **Portée** : NGemity indexe l'entrepôt par `account_id` (`Player.cpp:2956`), le dépôt commande par
   le personnage en session et résout le compte (`StorageRepository.cs:107-111`).
5. **Bornes d'or** : NGemity borne à `1e11` (`ItemTemplate.hpp:4-5`) ; le dépôt n'a **aucune** borne
   d'or aujourd'hui. Les modes d'or étant fermés, l'écart n'est pas encore visible.

## 7. `NON ÉTABLI`

Les cinq points résiduels, chacun séparé en « décidable par lecture » (fait ici) et « relève de
Killian » (nommé, non deviné).

### 7.1 Tolérance du client à 4 octets après l'en-tête du `211` — **reste ouvert**

* **Lecture faite** : dans `SFrame.exe`, la table d'id nomme 211/212 (§1) ; le **constructeur du
  `212` est lu octet par octet** (§3.1) ; le cas d'interface qui ouvre la fenêtre d'entrepôt est
  atteint par `jmp DWORD PTR [edx*4+0x640dec]` (`.text 0x6395e5`, table en `.text 0x640dec`, entrée
  d'index 13 → `.text 0x63c301`) et journalise `SGameInterface - MSG_OPEN_STORAGE`
  (chaîne `.rdata 0xa4b5e4`, unique référence `.text 0x63c30b`) ; la fenêtre est créée par la
  fonction `.text 0x633440` (chaîne `Create : SUIStorageWnd`, `.rdata 0xa49df8`), appelée en
  `.text 0x639083` **avec des arguments de géométrie seulement**.
* **Lecture non faite** : je **n'ai pas localisé le parsing de la trame `211`** (le récepteur qui
  décode l'en-tête puis la charge). La « chaîne de cas » déjà repérée par le socle
  (`.text 0x8901a0-0x890252`) est un aiguillage sur les ids 0xcb→0xda+ : chaque cas pousse **une
  valeur** — le registre `edi` pour la plupart, `0x2` pour 0xcd/0xd7/0xd8, `0x3` pour 0xd0/0xd2 —
  puis l'id, et saute à un tronc commun en `.text 0x89059b` qui lit les globales `0xc94728` et
  `0xc94714`. **Sa sémantique n'est pas établie et je ne m'appuie pas dessus** ; en particulier rien
  n'y montre 4 octets chargés après l'en-tête.
* **Décision appliquée** : envoyer la forme 7.3 stricte, **7 octets**, parce que rzu la borne et que
  le binaire 7.3 ne connaît pas le nom du champ. Si Killian sait que le client 7.3 lit réellement
  4 octets à l'offset 7, c'est une **correction de la trame** (11 octets) et non un détail.
* **Ce qui l'établirait** : une capture d'une session 7.3 réelle, ou le désassemblage du récepteur
  `211` (le dispatcher réseau, pas la table de noms). C'est un point cher, à décider par Killian.

### 7.2 Capacité de l'entrepôt — **aucune source 7.3, ne pas inventer**

* rzu : `10000` n'existe qu'à partir de `EPIC_7_4` (`TS_SC_OPEN_STORAGE.h:8`) ; et
  `TS_SC_OPEN_PAID_STORAGE` n'a **aucun champ**, même en 9.8 (`TS_SC_OPEN_PAID_STORAGE.h:5`) — la
  capacité n'est donc pas non plus portée par l'entrepôt payant.
* NGemity : **aucune borne** — `Inventory` n'a ni capacité ni compteur maximum
  (`Chihiro/src/Entities/Item/Inventory.h`), et le seul plafond de la famille est celui de l'or
  (`ItemTemplate.hpp:4-5`).
* Client 7.3 : aucune chaîne `maxStorageItemCount`, aucune propriété de capacité dans la liste de
  propriétés du binaire (§5 du socle et liste `.rdata 0xa53a2c-0xa53b50`), et la fenêtre construit
  ses emplacements sur des ressources d'interface (`storage_slot%02d` en `.rdata 0xa30488`,
  `storage_itemcount%02d` en `.rdata 0xa30470`, toutes deux utilisées dans le constructeur
  `.text 0x598a00-0x59a420`). Le **nombre** d'emplacements n'a pas été établi.
* Conclusion à écrire dans le code : **pas de borne serveur en 7.3**, comme aujourd'hui. Une
  troisième source (les archives d'interface `data.001`…`data.008` du client) n'est pas disponible
  dans `reference/client73/`.

### 7.3 Geste exact d'émission du `212` — **partiellement établi, mode par site non établi**

Le constructeur (`0x48e8f0`) et son appelant (`0x49d673`) sont confirmés, et l'appelant est une
fonction (`0x49cd00`) appelée depuis **10 sites** (§2). La correspondance **site → mode** (bouton
`storage_button_payingout` / `storage_button_receipts` en `.rdata 0xa2f06c` / `0xa2f088`, champ de
quantité, glisser-déposer) n'a pas été établie : le mode vient du champ `+0x1f` de l'objet poussé,
qui est construit sur la pile du site appelant. Ce qui l'établirait : une capture en jeu, ou la
résolution de la table de commandes de `SUIStorageWnd` (constructeur `.text 0x598a00`, vtables
`0xa302ec` et `0xa302c8`). **Aucun impact sur le serveur**, qui traite les cinq modes.

### 7.4 Le mode 4 est-il réellement émis ? — **oui côté client, usage non établi**

Le constructeur compare le mode à `4` (`.text 0x48e92d`) et **n'arrête pas l'envoi** : quand le mode
vaut 4 il écrit un état local (`[esi+0xa0] = eax`, `[ecx+0x20] = al`) puis passe par le même envoi
virtuel (`.text 0x48e965-0x48e977`). Le client 7.3 sait donc émettre le mode 4 ; ce que fait le
bouton de fermeture (réseau ou état local seul) n'est pas établi. Conséquence pratique inchangée :
le serveur **doit** accepter le mode 4 sans erreur et sans réponse, et **ne doit pas** compter sur
lui pour libérer son état.

### 7.5 Où persister l'or d'entrepôt — **décision de Killian, options mesurées**

Les trois voies, avec ce que la base dit de chacune :

1. **Ligne d'objet `code = 0` (conception NGemity)** — pas de migration ; exige le filtre
   `ItemResourceId != 0` dans toute construction de liste d'entrepôt (§5.1) et une unicité par
   compte. Le dépôt n'a **aucun** antécédent de ligne d'or.
2. **Colonne sur `AccountEntity`** — `AccountEntity` vit dans la base **Auth**
   (`Game/DataAccess/Entities/Auth/AccountEntity.cs:5`), base créée par `EnsureCreated()` sans
   migrations (`AuthServer/Program.cs:47`, `Game/DataAccess/Repositories/AccountRepository.cs:30-32`),
   et distincte de la base des objets : écrire l'or d'entrepôt là est une **écriture
   inter-bases** sur un schéma **non versionné**.
3. **Table ou colonne dédiée dans Telecaster** — la base Telecaster est migrée par EF
   (`Game/DataAccess/Migrations/Telecaster/`, dernière `Version0009_LookupIndexes` ;
   `MigrateDatabase/Program.cs:101` et `DevConsole/Program.cs:47`) : voie la plus propre
   techniquement, mais aucun précédent non plus.

Une colonne sur `CharacterEntity` serait **fausse** : `CharacterEntity.Gold` existe
(`CharacterEntity.cs:54`) mais l'entrepôt est au niveau du **compte**
(`StorageRepository.cs:107-111` dérive le compte du personnage ; NGemity indexe par `account_id`,
`Player.cpp:2956`). Deux personnages du même compte doivent voir le même or d'entrepôt.

**Bornes** : `MAX_GOLD_FOR_STORAGE = 1e11` (`ItemTemplate.hpp:4-5`) est la seule valeur sourçable ;
l'adopter est un portage de politique NGemity, à valider par Killian.

**Tant que ce n'est pas tranché**, les modes 2/3 restent refusés en `NotActable`
(`StorageService.cs:107-114`) : refuser garde l'or du personnage intact, alors qu'accepter
retirerait de l'or que le serveur ne saurait pas rendre.

### 7.6 Les clauses d'`IsErasable` non portées — **état disponible mesuré**

`Player::IsErasable` (`Player.cpp:3136-3160`) a six clauses ; seule la clause de **port**
(`GetItemWearType() != WEAR_NONE → false`, `Player.cpp:3142-3143`) est portée, par
`StorageRules.IsStorable` (`StorageRules.cs:95`). Les autres, une par une :

| Clause NGemity | Source | État dans le dépôt aujourd'hui |
| --- | --- | --- |
| `!IsInInventory()` | `Player.cpp:3138-3139` | **portée** : `IsInventoryRow`/`IsStorageRow` + la requête par handle (`StorageRepository.cs:121-124`) |
| propriétaire ≠ joueur | `Player.cpp:3140-3141` | **portée** : la résolution du handle ne sort pas des lignes du personnage/compte → `AccessDenied` (`StorageService.cs:151-153`) |
| port ≠ `WEAR_NONE` | `Player.cpp:3142-3143` | **portée** par `IsStorable` |
| carte de compétence liée (`m_hBindedTarget != 0`) | `Player.cpp:3144-3145` | **aucun état** : aucun champ de liaison dans `ItemEntity` (`ItemEntity.cs:8-43`) ni `CharacterEntity` ; les paquets 284/285 sont encore en MR ouverte |
| carte d'invocation liée à une ceinture (`m_aBindSummonCard[6]`) | `Player.cpp:3147-3155` | **état disponible** : `CharacterEntity.BeltItemIds` est un `long[]` de **6** (`CharacterEntity.cs:62`, `TelecasterContext.cs:142`), envoyé au client par `BuildBeltSlotInfo` (`GameActions.cs:248`). NGemity itère 6 emplacements — l'équivalent existe donc déjà, contrairement à ce que dit la fiche socle (`StorageRules.cs:89-94`) |
| `!IsInStorage()` | `Player.cpp:3160` | **portée** : le côté est porté par `CharacterId`/`AccountId` (`StorageRules.Own`) |

Et NGemity refuse en plus, **dans le chemin du dépôt lui-même**, une carte d'invocation liée
portant le drapeau `ITEM_FLAG_EVENT` (`WorldSession.cpp:1619-1624`), en `ACCESS_DENIED` — donc pas
par les codes 51/88.

Clause de compétence : à porter quand 284/285 seront mergés ; clause d'invocation liée : portable
dès maintenant avec `BeltItemIds`, si Killian veut la fermer. La sémantique exacte de `BeltItemIds`
(id d'objet ou id de ressource) est **incertaine** — le commentaire du champ le dit lui-même
(`CharacterEntity.cs:62`) — et doit être vérifiée avant de comparer avec `item.Id`.

### 7.7 Refus par les codes 51 / 88 — **confirmé : à ne pas employer**

`RESULT_NOT_ACTABLE_WHILE_USING_STORAGE = 51` (`shared/Server/TS_MESSAGE.h:104`) et
`RESULT_TARGET_IS_USING_STORAGE = 88` (`:139`) sont **déclarés mais jamais renvoyés** dans tout
l'arbre NGemity (recherche sur les deux identifiants : seules ces deux définitions). Le dépôt les a
dans son énumération (`Game/Network/Packets/ResultCode.cs:63` et `:104`). Les employer serait un
choix de conception, pas un portage : **rester sur `NotActable` (5)**, comme livré.

### 7.8 Le conteneur : `ItemEntity` élargi ou table dédiée

Non tranché, et **le socle l'a déjà laissé au dev** (fiche socle §7.6). `ItemStorageEntity` est
l'entrepôt d'enchères (`StorageType.cs`) et ne peut pas servir de conteneur au niveau du compte sans
ambiguïté. La recommandation du socle (élargir `ItemEntity`) s'appuie sur des colonnes déjà migrées,
pas sur une donnée 7.3 : aucune table de base retail 7.3 n'est dans le dépôt.

### 7.9 Forme exacte de la notification de déplacement d'objet

Inchangé (socle §7.7) : NGemity notifie par ses rappels d'`Inventory`, le choix du dépôt le suit.
Ce qui l'établirait : une capture d'un déplacement réel.

## 8. Collisions avec les MR ouvertes (mesure, registre local des branches)

Mesure faite sur les 48 références `refs/remotes/origin/hermes/*` du clone : **16 sont en avance sur
`master`** — ce sont les 16 MR ouvertes de la carte. Pour chacune, la liste des fichiers touchés vient
de `git diff --name-only master...origin/hermes/<branche>`.

| MR | Branche | Touche le noyau du socle |
| --- | --- | --- |
| #1 | `hermes/packet-221-hide-equip-info` | `GamePackets.cs`, `GameClient.cs`, `GameActionPackets.cs` |
| #6 | `hermes/packet-223-swap-equip` | `GamePackets.cs`, `GameClient.cs` |
| #13 | `hermes/packet-281-puton-item-set` | `GamePackets.cs`, `GameClient.cs`, `GameActionPackets.cs` |
| #21 | `hermes/packet-214-puton-card` | idem |
| #22 | `hermes/packet-215-putoff-card` | idem |
| #23 | `hermes/packet-284-bind-skillcard` | idem |
| #24 | `hermes/packet-285-unbind-skillcard` | idem |
| #26 | `hermes/packet-258-donate-item` | idem |
| #27 | `hermes/packet-259-donate-reward` | idem |
| #33 | `hermes/packet-10000-open-item-shop` | `GamePackets.cs`, `GameClient.cs` |
| #38 | `hermes/packet-4003-huntaholic-create-instance` | `GamePackets.cs`, `GameClient.cs` |
| #40 | `hermes/packet-323-change-summon-name` | `GamePackets.cs`, `GameClient.cs`, `GameActionPackets.cs` |
| #45 | `hermes/packet-260-soulstone-craft` | `GameClient.cs`, **`Tests/Game/StorageTestHarness.cs`** |
| #46 | `hermes/packet-262-repair-soulstone` | — |
| #47 | `hermes/packet-263-transmit-ethereal-durability` | — |
| #48 | `hermes/packet-264-transmit-ethereal-durability-to-equipment` | `GameActionPackets.cs` |

Décompte : **13/16** touchent `Game/Network/Clients/GameClient.cs`, **12/16**
`Game/Network/Packets/Enums/GamePackets.cs`, **10/16**
`Game/Network/Packets/Game/GameActionPackets.cs`, **1/16** (`#45`) touche le **harnais de test de
l'entrepôt** `Tests/Game/StorageTestHarness.cs`.

**Aucune** des 16 ne touche `Game/Services/StorageService.cs`, `StorageRules.cs`,
`StorageMoveResult.cs`, `IStorageService.cs`, `GameStoragePackets.cs`, `StorageRepository.cs`,
`ConnectionInfo.cs`, `StoragePacketsTests.cs` ni `StorageServiceTests.cs`. Conséquence pour le lot :
le traiter **entièrement dans `Game/Services/Storage*`, `Game/DataAccess/` et `Tests/Game/Storage*`**
le tient hors des deux fichiers chauds ; toucher `GamePackets.cs` ou `GameClient.cs` rouvre une
collision avec 12 à 13 MR. Le mapping branche → n° de MR vient des comptes rendus du board
(`task_events`), pas d'un accès GitHub : le registre local ne porte pas les numéros de MR.

Le harnais `StorageTestHarness.cs` est le seul point de contact réel : la MR #45 le modifie, donc le
dev du lot résiduel doit **rebaser** et relire ce fichier avant de le modifier.

## 9. Tests d'offsets : ce qui suffit et ce que le lot doit ajouter

`Tests/Game/StoragePacketsTests.cs` (249 l., sur `master`) **couvre déjà intégralement la forme sur
le fil** : les deux ids (211/212) et l'absence de 1211/1212, la trame d'ouverture à **7 octets**,
la lecture de la trame `212` à **20 octets** (`Length`, `ID`, `handle@7`, `mode@11`, `count@12`,
`count` int64 signé), le rejet des trames courtes, et le cas `count` négatif.

**Donc, pour ce lot : aucun nouvel offset à écrire** — et **ne pas réécrire** ces assertions. Le lot
n'ajoute pas de paquet : il change le **comportement** des modes et des clauses. Les tests à ajouter
sont donc comportementaux, avec vérification des octets des **réponses** :

1. modes 2/3 : si les modes restent fermés → `TM_SC_RESULT` id 0, **15 octets**, `request=212`,
   `result=5`, `value=handle` ;
2. modes 2/3 si Killian choisit de les ouvrir → l'or ne peut pas descendre en dessous de 0, et la
   réponse de dépassement est `TooMuchMoney (53)` sur la borne retenue ;
3. clause « carte d'invocation liée » : un objet dont l'id est dans `BeltItemIds` et qui porte le
   drapeau `ItemFlag` correspondant à `ITEM_FLAG_EVENT` (source NGemity : `WorldSession.cpp:1619-1624`)
   → `AccessDenied (6)` ;
4. filtre de liste : si la voie 1 de §7.5 est retenue, une ligne `ItemResourceId == 0` du compte ne
   doit **jamais** apparaître dans la liste d'entrepôt envoyée (test sur la trame `TM_SC_INVENTORY`
   construite par `BuildInventory`) ;
5. le compte ne baisse pas : `dotnet test Tests/Tests.csproj` doit rester **≥ 1302 tests**, 0 échec.

## 10. Commits et binaires épinglés

| Référence | Commit / empreinte | Usage |
| --- | --- | --- |
| Navislamia `master` (base de cette fiche) | `b56967a` (merge de `hermes/fix-devconsole-compilation`, PR #44) | état du dépôt vérifié ici |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | ids, champs, gating (`TS_CS_STORAGE.h`, `TS_SC_OPEN_STORAGE.h`, `TS_SC_RESULT.h`, `TS_SC_OPEN_PAID_STORAGE.h`) ; liaison `storage_data` (`rzgame/src/Database/DB_StorageItem.cpp:7`) |
| NGemity (Chihiro) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | logique (`WorldSession.cpp`, `Player.cpp`, `WorldSession.h`, `Inventory.h`, `ItemTemplate.hpp`, `shared/Database/Implementation/CharacterDatabase.{h,cpp}`, `shared/Server/TS_MESSAGE.h`) |
| client 7.3 `SFrame.exe` | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 o) | table d'id, constructeur du `212`, cas d'ouverture de fenêtre, liste des propriétés |

## 11. Bloc pour `CLAUDE.md` (à recopier dans la description de la MR)

```markdown
### Entrepôt du personnage — paquets 211 / 212 (lot résiduel)

- `212` = `TM_CS_STORAGE`, 20 octets : en-tête 7 (length uint32, id uint16, checksum), `handle` @7
  (uint32), `mode` @11 (int8, 0=objet→entrepôt, 1=objet→inventaire, 2=or→entrepôt,
  3=or→inventaire, 4=fermeture), `count` @12 (int64 **signé**). Vérifié sur le générateur du client
  7.3 lui-même, pas seulement sur rzu.
- `211` = `TM_SC_OPEN_STORAGE`, **7 octets** en 7.3 : `maxStorageItemCount` est gaté `>= EPIC_7_4`
  par rzu et le binaire 7.3 ne connaît pas ce nom. Ne pas envoyer les 4 octets du 7.4.
- `1211`/`1212` n'existent qu'à partir d'`EPIC_9_6_3` : ne jamais les déclarer. `TM_SC_RESULT`
  vaut **0** en 7.3 (1000 à partir de 9.6.3), charge 8 octets.
- L'or d'entrepôt n'a **pas de colonne** dans le dépôt : NGemity le range dans une ligne d'objet
  `code = 0` (`CharacterDatabase.cpp:97`) qu'il exclut de la liste par `code == 0`
  (`Player.cpp:318-324`). Si cette voie est reprise, filtrer `ItemResourceId != 0` dans **toute**
  construction de liste d'entrepôt, sinon la ligne d'or part au client comme un objet.
- Bornes d'or NGemity : `MAX_GOLD_FOR_STORAGE = MAX_GOLD_FOR_INVENTORY = 1e11`
  (`ItemTemplate.hpp:4-5`) — politique serveur, pas un champ de trame.
- Les codes `51` (`NOT_ACTABLE_WHILE_USING_STORAGE`) et `88` (`TARGET_IS_USING_STORAGE`) ne sont
  **jamais** renvoyés par NGemity : rester sur `NotActable` (5).
- `IsErasable` (`Player.cpp:3136-3160`) n'est porté que pour la clause de port ; la clause
  « carte d'invocation liée » est portable avec `CharacterEntity.BeltItemIds` (6 emplacements),
  pas la clause de carte de compétence (aucun état avant 284/285).
- Pas de capacité d'entrepôt en 7.3 (aucune source) : ne pas inventer de borne.
- Hotspot : 13/16 MR ouvertes touchent `GameClient.cs` et 12/16 `GamePackets.cs` ; le lot résiduel
  se tient dans `Game/Services/Storage*` + `Game/DataAccess/` + `Tests/Game/Storage*`.
```

## A VERIFIER PAR KILLIAN

1. **4 octets après l'en-tête du `211`** — la carte affirme que le client 7.3 les charge ; je n'ai
   pas su le reproduire par lecture (le récepteur `211` n'a pas été localisé, et la chaîne de cas
   `.text 0x8901a0-0x890252` n'a pas de sémantique établie). Si c'est exact, la trame d'ouverture
   doit faire **11 octets** et non 7 : c'est un changement de trame, pas un détail. Dites-moi si je
   dois creuser le récepteur (coût : désassemblage ciblé du dispatcher réseau) ou si la valeur 7 est
   confirmée.
2. **Or d'entrepôt (§7.5)** — trois voies mesurées : ligne d'objet `code = 0` (NGemity, pas de
   migration, mais filtre `ItemResourceId != 0` partout), colonne sur `AccountEntity` (base Auth,
   `EnsureCreated()`, écriture inter-bases — déconseillé), table/colonne dédiée Telecaster (migrée).
   Le socle laissait le choix au dev ; je maintiens que c'est un choix d'architecture de données.
3. **Borne d'or 1e11** — `ItemTemplate.hpp:4-5` est la seule valeur sourçable. L'adopter comme
   borne de serveur 7.3, ou en choisir une autre ?
4. **Clause « carte d'invocation liée »** — `CharacterEntity.BeltItemIds` existe (6 emplacements) :
   faut-il la fermer maintenant, ou attendre ? Sa sémantique (id d'objet ou id de ressource) reste à
   confirmer avant de la comparer à `item.Id`.
5. **Capacité d'entrepôt** — aucune source 7.3 (ni rzu, ni NGemity, ni le binaire client) : je
   conclus « pas de borne ». Si le client affiche un nombre fixe d'emplacements, la valeur m'est
   inconnue ; les archives d'interface (`data.001`…`data.008`) ne sont pas dans `reference/client73/`.
