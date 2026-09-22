# 253 — TM_CS_USE_ITEM

Direction : **client → serveur**. Pendants serveur : `TM_SC_USE_ITEM_RESULT (283)` et, sur le chemin de
succès comme d'échec, l'accusé générique `TM_SC_RESULT (0)`.
Toute preuve issue de `reference/client73/` vient d'une lecture statique (`strings`) : aucun binaire
client n'a été exécuté. Les numéros de ligne cités pour les binaires sont ceux du dump
`strings -n 4 <fichier>` (reproductible localement) ; les identifiants stables sont le sha256 et la clé
de ressource (§8).

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id décimal | `253` (`0x00FD`) | `op_codes.md:80` — `[253] = "TM_CS_USE_ITEM"` |
| nom | `TM_CS_USE_ITEM` | `op_codes.md:80` |
| pendant serveur | `TM_SC_USE_ITEM_RESULT = 283` | `op_codes.md:97` |
| accusé générique | `TM_SC_RESULT = 0` | `Game/Network/Packets/Enums/GamePackets.cs:5` |
| présence dans l'enum du dépôt | **absente** — `GamePackets.cs` passe de `TM_CS_ARRANGE_ITEM = 219` (l. 37) à `TM_EQUIP_SUMMON = 303` (l. 41) | `Game/Network/Packets/Enums/GamePackets.cs:37-41` |
| présence du pendant 283 | **absente** — les ids SC déclarés dans cette zone sont 202, 207, 209, 210, 216, 217, 220, 222, 224, 287 | `Game/Network/Packets/Enums/GamePackets.cs:27-40` |
| dispatch montant | aucun bras : le `switch` final lève `Unknown Packet Type` | `Game/Network/Clients/GameClient.cs:670-682` (repli `_ => throw ...` l. 681) |

Le dev doit donc ajouter `TM_CS_USE_ITEM = 253` **et** son bras de traitement dans le même commit
(critère d'acceptation 4), et `TM_SC_USE_ITEM_RESULT = 283` pour la réponse descendante. Le 283 suit la
convention du dépôt pour un id descendant (222, 287, 209, 210, 224 sont déclarés sans bras montant) :
il n'a pas à figurer dans la chaîne de `if` du `Receive`.

## 2. Ce que le joueur fait pour que le client l'envoie

Éléments relevés dans le client Epic 7.3 :

| Élément | Contenu | Source (dump `strings -n 4`) |
| --- | --- | --- |
| libellé de menu contextuel | `Use Item` | `reference/client73/db_string.rdb` clé `ui_text_6781`, l. 112939-112940 |
| classe de commande d'entrée | `SInputUseItem` (manglé `.?AVSInputUseItem@@`) | `reference/client73/SFrame.exe` l. 44555 |
| même famille | `SInputMove` l. 44546, `SInputSkill` l. 44549, `SInputTakeItem` l. 44550 | `SFrame.exe` |

Trois conséquences établies :

1. `SInputUseItem` appartient à la famille `SGameInput` : l'émission du 253 part d'une **commande
   d'entrée joueur**, auprès de `SInputMove` / `SInputTakeItem` / `SInputSkill` (qui correspondent aux
   paquets montants 5, 204 et 400, déjà traités par le dépôt). C'est le mécanisme, pas une preuve de forme.
2. Ce n'est **pas** une commande clavier locale : `db_localcommand.rdb` (133 entrées) ne contient
   aucune entrée contenant `item`. Le libellé `Use Item` est un libellé de menu contextuel
   (`ui_text_6781`, dans un bloc contigu de libellés de menus contextuels, l. 112927-112945) ;
   l'écran exact qui l'affiche n'est pas établi (§7.2).
3. L'objet visé est un objet de l'inventaire porté par un **handle d'instance**, pas un index de case :
   le dépôt résout déjà les handles d'objet ainsi pour les paquets voisins
   (`Game/Services/CharacterService.cs:379` — `(uint)item.Id == handle`), et construit les handles
   d'inventaire de la même façon (`Game/Network/Packets/Game/GameCharacterPackets.cs:289`).

**Émission du 253 par ce client : présomption, preuve directe NON ÉTABLIE** — voir §7.1. La chaîne
d'indices est : un libellé de menu « Use Item », une classe de commande d'entrée `SInputUseItem`, un id
montant 253 unique portant ce nom, et un chemin de réception descendant 283 présent dans le même
binaire (§5.3). Ce qui manque est l'observation du paquet sur le fil.

## 3. Structure sur le fil

Taille totale attendue : **47 octets** (7 d'en-tête + 40 de charge utile). Aucune trame de ce paquet n'a
été observée : les valeurs des offsets 0 à 6 découlent de cette taille calculée (en-tête 7 octets du
dépôt), pas d'une capture réseau.

| Offset | Taille | Type | Nom | Valeur attendue | Source |
| --- | --- | --- | --- | --- | --- |
| 0 | 4 | `uint32` LE | `Length` | 47 (longueur totale, en-tête compris) | `Game/Network/Packets/Header.cs:9`, `:22` |
| 4 | 2 | `uint16` LE | `ID` | `253` | `Game/Network/Packets/Header.cs:10`, `:23` |
| 6 | 1 | `uint8` | `Checksum` | non vérifié (le dépôt ne l'impose pas en réception) | `Game/Network/Packets/Header.cs:11`, `:24` |
| 7 | 4 | `uint32` LE (`ar_handle_t`) | `item_handle` | handle d'instance de l'objet utilisé | `reference/rzu/librzu/src/packets/GameClient/TS_CS_USE_ITEM.h:8` ; taille : `librzu/src/lib/Packet/GameTypes.h:40` (`ar_handle_t : strong_typedef<ar_handle_t, uint32_t>`) |
| 11 | 4 | `uint32` LE (`ar_handle_t`) | `target_handle` | `0` si l'objet ne se lance pas sur une cible ; sinon handle de l'unité visée | `.../TS_CS_USE_ITEM.h:9` ; usage : NGemity `Chihiro/src/Network/GameNetwork/WorldSession.cpp:1359`, `:1377` |
| 15 | 32 | `char[32]` fixe, complété par des `\0` | `szParameter` | inconnu du client (§7.3) | `.../TS_CS_USE_ITEM.h:10` — `_(string)(szParameter, 32, version >= EPIC_4_1)` |

Sources de la taille du champ texte :

- la macro `string` d'rzu écrit **exactement** `size` octets : `SIZE_F_STRING3` ajoute `_size` au total
  (`reference/rzu/librzu/src/lib/Packet/PacketDeclaration.h:264-266`) et `SERIALIZATION_F_STRING3`
  appelle `writeString(name, size)` (`:360-362`) ; `MessageBuffer::writeString` tronque puis complète à
  `maxSize` par des zéros (`librzu/src/lib/Packet/MessageBuffer.cpp:87-95`), et `readString` lit
  `maxSize - 1` caractères avant d'avancer de `maxSize` (`:104-110`) ;
- NGemity porte la même définition (`reference/ngemity/shared/Server/Packets/GameClient/TS_CS_USE_ITEM.h:9`) ;
- le champ est **dans** la trame à la version 7.3 : voir §4.

Le dépôt possède l'équivalent C# du champ fixe : `[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]`
(`Game/Network/Packets/Game/TS_CS_LOGIN.cs:8` pour un nom de 19 octets). Un struct
`[StructLayout(LayoutKind.Sequential, Pack = 1)]` avec un `uint ItemHandle`, un `uint TargetHandle` et
cette chaîne donne `Marshal.SizeOf == 40`, donc une trame `Packet<TS_CS_USE_ITEM>` de 47 octets.

## 4. Gating de version

Version de référence : **Epic 7.3 = `0x070300`** (`reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59`).

| Champ | Gating rzu | Décision pour 7.3 |
| --- | --- | --- |
| id du paquet | `X(253, version < EPIC_9_6_3)` / `X(1253, version >= EPIC_9_6_3)` (`.../TS_CS_USE_ITEM.h:12-14`) | **`253`**. `EPIC_9_6_3 = 0x090603` (`PacketEpics.h:96`, remap des ids GS `+= 1000` pour `< 2000` : commentaire l. 91-95). `0x070300 < 0x090603` → l'id 7.3 est bien 253, jamais 1253. |
| `szParameter` (32 o) | `version >= EPIC_4_1` (`.../TS_CS_USE_ITEM.h:10`) | **PRÉSENT**. `EPIC_4_1 = 0x040100` (`PacketEpics.h:50`) ; `0x070300 >= 0x040100`. Le champ est donc dans la trame à 7.3 et la taille totale est 47, pas 15. C'est l'erreur à ne pas commettre : lire 15 octets désaligne la suite du flux. |
| `item_handle`, `target_handle` | aucun gating (`.../TS_CS_USE_ITEM.h:8-9`) | présents, 4 octets chacun, à toutes les versions. |

Le gating de `szParameter` a été **établi par test** sur clients anciens :
`94885467536899bd53031f190d4488fe9e05c70a` (2017-03-03, « Update packets with old clients tests ») a
transformé `_(string)(szParameter, 32)` en `_(string)(szParameter, 32, version >= EPIC_4_1)` — le champ
existait déjà pour les versions au moins égales à 4.1 et manquait avant. Le champ lui-même vient du test
du client 9.3 (`6584164baee55aa94762587efcfd3ac8149386aa`, 2016-02-26). L'id versionné est de
`11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11).

Corroboration supplémentaire : le commit de définition de la borne 9.6.1/9.6.2
(`PacketEpics.h:81-83`) liste `253` **et** `283` parmi les paquets testés sur la version FR du
2020-03-07, c'est-à-dire juste avant le remap 9.6.3 — les deux ids sont donc stables jusqu'à 9.6.3
inclus.

## 5. Traitement attendu

### 5.1 NGemity en fait la référence la plus complète

- Montage : `{TS_CS_USE_ITEM, STATUS_AUTHED, &WorldSession::onUseItem}`, handler déclaré
  `reference/ngemity/Chihiro/src/Network/GameNetwork/WorldSession.h:111`, corps
  `.../WorldSession.cpp:1317-1379`.
- Déroulé (`WorldSession.cpp`) :
  1. résolution de l'objet par handle **et** vérification du propriétaire → sinon
     `TS_RESULT_NOT_EXIST` avec `value = item_handle` (`:1321-1325`) ;
  2. type d'objet / objet déjà en cours d'utilisation → `TS_RESULT_ACCESS_DENIED` (`:1327-1330`) ;
  3. `flaglist[FLAG_MOVE] == 0` et joueur en mouvement → `TS_RESULT_NOT_ACTABLE` (`:1332-1335`) ;
  4. `Player::IsUseableItem` → cool-down, `use_max_level`, `use_min_level`, niveaux de cible
     (`Chihiro/src/Entities/Player/Player.cpp:2088-2107`) ;
  5. si `flaglist[FLAG_TARGET_USE] == 0` (index 3, `Chihiro/src/Entities/Item/ItemTemplate.hpp:185`,
     tableau `flaglist[19]` `:408`) : `UseItem(item, nullptr, szParameter)` puis
     `SendResult(getReceivedId(), nResult, item_handle)` et retour si non nul (`:1351-1356`) ;
  6. sinon : la cible est résolue dans le monde (`sMemoryPool.GetObjectInWorld<Unit>(target_handle)`),
     absente → `TS_RESULT_NOT_EXIST` avec `value = target_handle` (**pas** `item_handle`, `:1360-1363`),
     puis `IsUseableItem(item, unit)` / `UseItem(item, unit, szParameter)` et le même `SendResult`
     (`:1364-1372`) ;
  7. **sur succès seulement** : `TS_SC_USE_ITEM_RESULT` avec `item_handle` et `target_handle` recopiés
     de la requête (`:1375-1378`).
- `Player::UseItem` (`Player.cpp:2109-2183`) applique les effets `base_type` / `opt_type` de l'objet puis
  exécute le script `on_use_item` du `script_text` en lui passant `szParameter` (`:2121`, `:2127`,
  `:2155`), et consomme 1 exemplaire quand le type n'est pas `TYPE_USE` (`:2179-2180`).
- Réponses, forme sur le fil :
  - `TS_SC_RESULT` : `request_msg_id` `uint16`, `result` `uint16`, `value` `int32`
    (`reference/ngemity/shared/Server/Packets/GameServer/TS_SC_RESULT.h:7-9`) → **15 octets** avec
    l'en-tête de 7 ; écrit par `Messages::SendResult` (`Chihiro/src/Network/Messages.cpp:361-371`) ;
  - `TS_SC_USE_ITEM_RESULT` : `item_handle` `uint32`, `target_handle` `uint32`
    (`shared/Server/Packets/GameServer/TS_SC_USE_ITEM_RESULT.h:7-8`) → **15 octets** ;
  - les codes de résultat employés sont identiques en valeur à ceux du dépôt :
    `TS_RESULT_SUCCESS = 0`, `NOT_EXIST = 1`, `NOT_ACTABLE = 5`, `ACCESS_DENIED = 6`,
    `LIMIT_MAX = 16`, `LIMIT_MIN = 17`, `COOL_TIME = 22`
    (`shared/Server/TS_MESSAGE.h:55-77`) vs `Game/Network/Packets/ResultCode.cs:6-31`.
- Aucun accès base de données, aucun état persistant : `onUseItem` est entièrement en mémoire
  (l'écriture est déclenchée plus loin par `Save`, `Player.cpp:2182-2183`).

### 5.2 rzu n'apporte aucune logique serveur

`grep -rn "USE_ITEM" reference/rzu --include=*.cpp --include=*.h` ne renvoie rien hors des deux
en-têtes de définition sous `librzu/src/packets/` ; `rzgame/` ne référence jamais le paquet. rzu tranche
l'id et l'ordre des champs (§3, §4), pas le comportement.

### 5.3 Ce que le serveur Navislamia doit faire

1. `GamePackets.TM_CS_USE_ITEM = 253` (`Game/Network/Packets/Enums/GamePackets.cs`, entre l. 37 et 41)
   **et** `GamePackets.TM_SC_USE_ITEM_RESULT = 283` (même enum, zone SC l. 27-40).
2. Un bras dans la chaîne de `if` du `Receive`, avant le `switch` final
   (`Game/Network/Clients/GameClient.cs:670-682`), sur le modèle de `TM_CS_PUTON_ITEM`
   (`GameClient.cs:557-561`) : lecture, puis `continue`.
3. Lecture dans `Game/Network/Packets/Game/GameActionPackets.cs` (constante `HeaderSize = 7`, l. 8), sur
   le modèle de `TryReadPutonItem` (l. 124-138) : `packet.Length < HeaderSize + 40` → faux ; sinon
   `item_handle` à l'offset 7, `target_handle` à l'offset 11, `szParameter` à l'offset 15 sur 32 octets
   (le lire comme chaîne terminée par `\0`, ou l'ignorer : voir §7.3). Le style du dépôt est
   **tolérant au surplus** (`<` et non `==`), ce qui accepte une trame plus longue sans désaligner.
4. Trame invalide → `SendResult((ushort)GamePackets.TM_CS_USE_ITEM, (ushort)ResultCode.InvalidArgument)`
   (`Game/Network/Clients/GameClient.cs:56-60`), comme pour les paquets d'objet voisins
   (`GameClient.cs:306-312`).
5. Objet inconnu / non possédé → `SendResult(TM_CS_USE_ITEM, ResultCode.NotExist, …)` ; objet non
   utilisable dans l'état courant → `ResultCode.NotActable` ; niveau insuffisant → `ResultCode.LimitMin`.
   Les valeurs sont celles de `Game/Network/Packets/ResultCode.cs` et coïncident avec NGemity (§5.1).
6. **Succès → deux réponses, dans cet ordre** (ordre NGemity `WorldSession.cpp:1353` puis `:1375`) :
   - `TM_SC_RESULT (0)`, **15 octets**, `request_msg_id = 253`, `result = 0`,
     `value = item_handle` (voir §7.4) — via `SendResult` / `Packet<TS_SC_RESULT>`
     (`Game/Network/Packets/Game/TS_SC_RESULT.cs:8-10`) ;
   - `TM_SC_USE_ITEM_RESULT (283)`, **15 octets**, `item_handle` **recopié** de la requête puis
     `target_handle` **recopié** de la requête, y compris `0` (`WorldSession.cpp:1376-1377`).
   Un struct `[StructLayout(LayoutKind.Sequential, Pack = 1)] { uint ItemHandle; uint TargetHandle; }`
   lu par `Marshal.SizeOf` donne 8 octets de charge utile → trame de 15 octets, comme `TS_SC_RESULT`.
7. Le client 7.3 possède bien le chemin de réception du 283, sur quatre indices concordants du même
   binaire : le nom dans la table d'annotation (`TM_SC_USE_ITEM_RESULT`, `SFrame.exe` l. 26366), un type
   de message dédié (`case MSG_USE_ITEM_RESULT           :`, l. 19723 ; trace
   `SGameInterface - MSG_USE_ITEM_RESULT`, l. 25133), une structure de message descendante
   (`SMSG_USE_ITEM_RESULT`, manglé `.?AUSMSG_USE_ITEM_RESULT@@`, l. 44391, voisine de
   `SMSG_TAKE_ITEM_RESULT` l. 44392), et le chemin générique `case MSG_RESULT` l. 19741 pour
   l'accusé 0. Les deux réponses ont donc un destinataire identifié.
8. **Aucun effet d'objet n'existe dans le dépôt** : pas de moteur d'effets, pas de `script_text` évalué,
   pas de gestion de cool-down d'objet. Le périmètre réaliste de cette tâche est donc la lecture, la
   validation et la réponse ; l'application des effets (`base_type`/`opt_type`) et le passage de
   `szParameter` à un script sont hors de portée sans le sous-système correspondant (§6, §7.3).

Résumé de la réponse attendue : **id 0 (`TM_SC_RESULT`), 15 octets, `(253, résultat, valeur)`** puis,
sur succès seulement, **id 283, 15 octets, `item_handle` + `target_handle` recopiés**.

## 6. Écarts assumés avec NGemity

| Écart | Justification |
| --- | --- |
| On n'utilise **pas** `flaglist[FLAG_TARGET_USE]` pour décider si `target_handle` doit être lu | NGemity indexe un tableau de 19 entiers (`ItemTemplate.hpp:179-200`, `:408`) où `FLAG_TARGET_USE = 3` ; le dépôt modélise la même donnée en **un seul** enum `ItemUseFlag` alimenté par la colonne MSSQL `item_use_flag`, et y nomme `TargetUse = 7` (`Game/DataAccess/Entities/Arcadia/ItemResourceEntity.cs:19`, `Game/DataAccess/Entities/Enums/ItemUseFlag.cs:15`, `MigrateDatabase/MssqlEntities/Arcadia/MSSQLItemResource.cs:44`, mapper `MigrateDatabase/Mappers/ArcadiaResourcesMappingProfile.cs:27`). Les deux conventions ne concordent pas : `TargetUse = 7` n'a pas la valeur 3 de NGemity. Le champ `target_handle` étant **fixe sur le fil**, la lecture ne dépend pas de ce flag ; ne pas gater la lecture dessus, et ne pas interpréter `ItemUseFlag` sans trancher §7.6. |
| Aucun effet d'objet appliqué, alors que NGemity applique `base_type`/`opt_type` et le script `on_use_item` | le dépôt n'a ni moteur d'effets d'objet ni interpréteur Lua ; porter NGemity supposerait d'inventer un sous-système. La fiche fixe la réponse, pas l'effet. |
| `value` de `TM_SC_RESULT` : NGemity passe `item_handle`, le dépôt a deux précédents divergents | ne désaligne rien (champ `int32` fixe) ; voir §7.4 pour l'arbitrage. |
| Les contrôles NGemity `IsMoving` / cool-down / niveaux de cible ne sont pas portés | ils dépendent de sous-systèmes (mouvement de session, cool-down d'objet, niveaux de cible) absents ou non exposés ici ; les inventer produirait des refus non prouvés. |
| NGemity écrit les id en dur, sans gating de version ; on garde le gating rzu | rzu tranche l'id et le gating par version : 253 pour 7.3 (§4). |
| Aucune persistance, aucun état | NGemity non plus (§5.1) ; le dépôt ne doit rien écrire pour ce paquet. |

## 7. NON ÉTABLI

1. **Preuve que le client 7.3 émet le 253 — absente.** Le nom figure dans la table d'annotation du
   binaire (`SFrame.exe` l. 26376) et la classe de commande `SInputUseItem` existe (l. 44555), mais
   cette table est **partielle et non fiable comme liste d'émission** — elle omet par exemple
   `TM_CS_ARRANGE_ITEM` et `TM_CS_CHANGE_ITEM_POSITION`, deux paquets montants que ce client envoie à
   coup sûr. La présence du nom ne prouve donc pas l'émission. Ce qui l'établirait : une capture réseau
   décodée d'une session 7.3 pendant un usage d'objet, ou un désassemblage du chemin d'envoi de
   `SFrame.exe`. Hors de portée ici : l'exécution de `SFrame.exe` et de Lua est interdite par le profil
   de ce rôle, et aucune trace de session n'est fournie.
2. **Geste exact d'émission.** Le libellé `Use Item` (`ui_text_6781`) est un libellé de menu
   contextuel, mais le bloc contigu (l. 112927-112945) mêle des entrées d'avatar (« Become Party
   Leader », « Block Character ») et de guilde (« Create Guild », « Create Alliance ») : l'écran qui
   l'affiche n'est pas déterminé par la lecture. Une case de barre rapide
   (`db_string.rdb` contient `Hotbar: 5` … `Hotbar: 8`, `ui_text_6762-6765`) pourrait aussi émettre le
   253 — rien ne l'établit ni ne l'exclut. À trancher par capture réseau.
3. **Contenu de `szParameter`.** Aucun nom de champ ni chaîne de paramètre d'usage d'objet n'apparaît
   dans le binaire ; NGemity ne fait que **transmettre** la valeur au script `on_use_item`
   (`Player.cpp:2155`) sans jamais la lire. Sa longueur (32) et sa position sont établies, son contenu
   non. Le dev doit la consommer pour la taille et l'ignorer pour le comportement.
4. **`value` de `TM_SC_RESULT` pour ce paquet.** NGemity passe `item_handle`
   (`WorldSession.cpp:1353`, `:1367`) ; dans le dépôt, `EquipmentService` passe `0`
   (`Game/Services/EquipmentService.cs:66`) et `InventoryService` passe le **handle du personnage**
   (`Game/Services/InventoryService.cs:65-80`). Aucun précédent n'est donc décisif et le client ne
   laisse pas lire son usage de `value`. Le choix par défaut proposé est `item_handle` (la référence qui
   implémente l'usage d'objet), à arbitrer par Killian. Aucun risque de désalignement : `value` est un
   champ `int32` de taille fixe. Le second cas de NGemity (`value = target_handle` quand la cible
   n'existe pas, `WorldSession.cpp:1361`) suit la même incertitude.
5. **Nécessité et ordre des deux réponses.** NGemity émet `TM_SC_RESULT` **puis**
   `TM_SC_USE_ITEM_RESULT` uniquement sur le chemin de succès (`WorldSession.cpp:1353`/`:1367` puis
   `:1375`). Aucun élément lu ne prouve que le client 7.3 exige l'un, l'autre, ou les deux, ni qu'un
   ordre inverse serait accepté (les deux chemins de réception existent pourtant dans le binaire, §5.3-7).
   À vérifier par capture.
6. **Sémantique de `ItemUseFlag` dans le dépôt.** L'enum est marqué `[Flags]` mais ses membres sont des
   **indices séquentiels** (`CantDonate = 0`, `CantStorage = 1`, … `TargetUse = 7`), donc utilisables
   comme masques binaires seulement si l'on décale explicitement
   (`Game/DataAccess/Entities/Enums/ItemUseFlag.cs:5-35`) ; NGemity, lui, lit l'index 3 d'un tableau
   `flaglist[19]` pour le même concept. La valeur réellement stockée par l'import MSSQL n'est pas
   documentée dans le dépôt (aucun importeur d'objets sous `tools/`). Toute lecture de `ItemUseFlag`
   avant arbitrage serait une supposition.
7. **Cible légitime.** NGemity accepte toute unité du monde
   (`sMemoryPool.GetObjectInWorld<Unit>(target_handle)`) et refuse avec `NOT_EXIST` sinon. Le dépôt n'a
   pas d'équivalent de résolution générique par handle d'unité ; la portée exacte (soi-même, membre du
   groupe, monstre) n'est établie ni par le client ni par les références pour 7.3.
8. **Nature du handle d'objet côté client.** Le dépôt résout les handles d'objet par `(uint)item.Id`
   pour tous les paquets d'objet montants existants ; supposer la même identité pour le 253 est
   cohérent mais non prouvé par le binaire client.

## 8. Commits et binaires épinglés

| Référence | Version | Usage dans cette fiche |
| --- | --- | --- |
| Navislamia (`KillianDoubre/Navislamia`) | `master` = `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` ; branche `hermes/packet-253-use-item` | état du dépôt, §1, §5.3, §6 |
| rzu (`glandu2/rzu`) | HEAD `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | structure, id, gating (§3, §4) |
| rzu — `TS_CS_USE_ITEM.h` | introduit par `6584164baee55aa94762587efcfd3ac8149386aa` (2016-02-26, « Update packets with 9.3 » : `item_handle`, `target_handle`, `szParameter` 32) ; gating `>= EPIC_4_1` ajouté par `94885467536899bd53031f190d4488fe9e05c70a` (2017-03-03, tests clients anciens) ; id versionné par `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11, remap 9.6.3) ; `ar_handle_t` par `05bc2d82dac16003bc06162584e8559826310bff` ; mention « Last tested: EPIC_9_8_1 » par `3b31db1e5aea84565926f6910e2437646c8a3c51` (2023-09-30) | §3, §4 |
| rzu — `TS_SC_USE_ITEM_RESULT.h` | introduit par `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07, « add all known GS packets as of 9.4 ») : `item_handle`, `target_handle`, id 283 non versionné ; id versionné par `11f2b6fd…` | §3, §5.1 |
| NGemity (`NGemity/RZEmulator`) | HEAD `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | traitement attendu (§5.1, §6) |
| NGemity — montage de `onUseItem` | introduit par `fd21c947f2713c2f2dfe37b284af287f368b8771` (2018-01-08) : entrée `{TS_CS_USE_ITEM, STATUS_AUTHED, &WorldSession::onUseItem}` et premier envoi de `TS_SC_USE_ITEM_RESULT` | §5.1 |
| NGemity — `EPIC` de compilation | `EPIC_4_1_1` (`shared/Common/Define.h:25`) : `version >= EPIC_4_1` y est vrai, donc la copie NGemity du paquet porte bien `szParameter` | §3, §4 |
| client de référence | `reference/client73/SFrame.exe` sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | §2, §5.3 |
| client de référence | `reference/client73/db_string.rdb` sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` | §2 |
| client de référence | `reference/client73/db_localcommand.rdb` sha256 `4d05c593da2ee84d123afd8def6464021631185663b8d81f5fc0f6cf2a4d02f9` | §2 |

## Note de livraison

Cette fiche n'ajoute **aucun fichier de code** : elle ne touche ni `Game/`, ni `Tests/`, ni `CLAUDE.md`.
Le dépôt ignorait `/docs/*` : `docs/packet-specs/` est ajouté aux exceptions de `.gitignore`
(`!/docs/packet-specs/`, à côté des trois fiches déjà suivies) pour que cette fiche soit versionnable.
La branche `hermes/packet-221-hide-equip-info` porte **exactement** la même ligne : les deux branches
produisent le même blob `.gitignore` (`c8c9d96`), donc le second merge se résout sans conflit. Il
faudra revérifier ce point si la ligne diffère un jour (hotspot de coordination à deux branches).

Point relevé au passage, hors périmètre de cette fiche : `CLAUDE.md` **ne contient aucune mention** de
`docs/packet-specs` ni des fiches de paquet (seules `docs/superpowers/specs/…` y sont citées). Le
repère annoncé (« `CLAUDE.md` pointe déjà vers le répertoire des fiches ») n'existe donc pas sur
`master` ; il devra être ajouté par un opérateur quand Killian mergera, ou la référence corrigée.

Baseline relevée sur `master` (`6a982c81e6c87eb6dfca37fe3baf432d811ad1c7`) avant la fiche :
`dotnet build Navislamia.sln -c Debug` code **0** (0 erreur, 160 avertissements) ;
`dotnet test Tests/Tests.csproj` code **0**, **366 tests** réussis sur 366.

## 9. Implémentation — navis-dev

Statut : implémenté sur `hermes/packet-253-use-item`, commit `c8a29d7` (base de la fiche `1e014ce`).
Périmètre tenu : lire la trame, juger l'objet, répondre. Aucun effet d'objet, aucune consommation,
aucune écriture d'état.

### 9.1 Fichiers livrés

| Fichier | Rôle |
| --- | --- |
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_USE_ITEM = 253` (zone CS, tri numérique) et `TM_SC_USE_ITEM_RESULT = 283` (zone SC) |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `UseItemRequest(uint ItemHandle, uint TargetHandle)` et `TryReadUseItem` |
| `Game/Network/Packets/Game/GameCharacterPackets.cs` | `BuildUseItemResult(itemHandle, targetHandle)` : 15 octets |
| `Game/Network/Clients/GameClient.cs` | `HandleUseItemAsync` + bras de dispatch sur `TM_CS_USE_ITEM` |
| `Game/Services/ItemUseService.cs`, `Game/Services/Interfaces/IItemUseService.cs` | résolution de l'objet, arbitrage, consommation, réponses |
| `Game/Services/ItemUseRules.cs` | `CheckUseLevel` : règle de niveau pure ; `IsConsumedOnUse` : tout type sauf `Use` (6) |
| `Game/Services/ItemUseCatalog.cs`, `Game/Services/Interfaces/IItemUseCatalog.cs` | `use_min_level` / `use_max_level` et le type (réutilisable ou non) chargés une fois depuis `IItemResourceRepository.GetUseFields()` |
| `Game/DataAccess/Repositories/ItemResourceRepository.cs` + interface | `ItemUseFields(int Id, int UseMinLevel, int UseMaxLevel, ItemBaseType BaseType)` et `GetUseFields()` |
| `Game/Services/CharacterService.cs`, `Game/Services/ICharacterService.cs` | `GetItemByHandleAsync(characterName, handle)` : l'objet est cherché **dans les objets du personnage** (la possession est donc prouvée par la résolution) ; `ConsumeItemAsync` retire un exemplaire, supprime la ligne au dernier et renvoie le reste |
| `Game/Network/NetworkService.cs`, `DevConsole/Program.cs` | `IItemUseService` et `IItemUseCatalog` enregistrés et injectés |
| `Tests/Game/UseItemPacketsTests.cs`, `Tests/Game/ItemUseTests.cs` | offsets du paquet et règle de niveau |

### 9.2 Offsets livrés et tests

- Requête : **47** = 7 (en-tête) + 4 (`item_handle` @7) + 4 (`target_handle` @11) + 32 (`szParameter` @15).
  Le paramètre est consommé **pour sa taille seulement** : son contenu n'est pas interprété (§7.3).
  Une trame de moins de 47 octets est refusée (`InvalidArgument`) sans lecture hors borne.
- Réponse : **15** = 7 + 4 (`item_handle` @7) + 4 (`target_handle` @11), l'ordre du rzu.

Tests : `UseItemIds_MatchTheEpic73Protocol`, `TryReadUseItem_ReadsTheEpic73Layout`,
`TryReadUseItem_ReadsTheSelfTargetFrame`, `TryReadUseItem_RejectsAFrameShorterThanTheParameter`,
`BuildUseItemResult_LaysOutTheFifteenByteAnswer`, plus `ItemUseTests` (catalogue et règle).

### 9.3 Réponses émises

| Cas | Réponse | `Value` |
| --- | --- | --- |
| trame < 47 octets | `TS_SC_RESULT` (253) `InvalidArgument` | 0 |
| handle inconnu ou non possédé | `TS_SC_RESULT` (253) `NotExist` | `item_handle` |
| niveau < `use_min_level` | `TS_SC_RESULT` (253) `LimitMin` | `item_handle` |
| `use_max_level != 0` et niveau > plafond | `TS_SC_RESULT` (253) `LimitMax` | `item_handle` |
| succès, objet consommable | `TM_SC_UPDATE_ITEM_COUNT` (255, reste) **ou** `TM_SC_DESTROY_ITEM` (254, dernier exemplaire), **puis** `TS_SC_RESULT` (253) `Success`, **puis** 283 | `item_handle` |
| succès, objet réutilisable (type 6) | `TS_SC_RESULT` (253) `Success` **puis** `TM_SC_USE_ITEM_RESULT` (283) | `item_handle` |
| objet disparu entre lecture et consommation | `TS_SC_RESULT` (253) `NotExist` | `item_handle` |
| erreur de lecture ou d'écriture base | `TS_SC_RESULT` (253) `DBError` | `item_handle` |

L'ordre des deux trames du succès est celui de NGemity (`WorldSession.cpp:1336`, puis `:1375`) et
celui du §5.3-6 : l'accusé générique d'abord, le résultat d'utilisation ensuite.

**Consommation.** NGemity retire l'exemplaire dans `Player::UseItem` (`Player.cpp:2179`,
`EraseItem(pItem, 1)`), donc **avant** le `SendResult` : la mise à jour de pile part en premier.
`Inventory::Erase` notifie par `TS_SC_DESTROY_ITEM` (254, `item_handle`, 11 octets) quand la pile
s'épuise, sinon par `TS_SC_UPDATE_ITEM_COUNT` (255, `item_handle` + `count` **int64** depuis
EPIC_4_1, 19 octets) — formats du rzu (`librzu/src/packets/GameClient/`). Seul `TYPE_USE` (6),
`ItemBaseType.Use`, est épargné : 404 ressources réutilisables dans les données importées. Une
ressource absente du catalogue est consommée, le cas par défaut de la référence. L'exception
NGemity des plumes de retour (`ITEM_CODE_FEATHER_OF_*`, consommées par leur script) n'est pas
portée, les scripts d'objet n'étant pas exécutés. La suppression passe par
`ICharacterRepository.DeleteItem` et le sac est renuméroté par `EnsureContiguousIndices`, comme
pour `TS_CS_ERASE_ITEM` (208).

### 9.4 Ce qui n'est pas porté, et pourquoi

1. **`NotActable` (déplacement)** : le seul chemin NGemity est
   `flaglist[FLAG_MOVE] == 0 && IsMoving(ct)` (`WorldSession.cpp:1332-1335`). Le dépôt n'expose aucun
   état de déplacement et le §6 l'exclut. Aucun refus `NotActable` n'est donc émis ; le code de
   réponse reste disponible pour la tâche qui apportera cet état.
2. **`AccessDenied` sur le type d'objet** : le contrôle NGemity (`WorldSession.cpp:1327-1330`) est
   neutralisé par le `&& false /*!item->IsUsingItem()*/` : la condition est **toujours fausse**, donc
   la référence n'émet jamais ce refus. Le porter créerait un refus que NGemity lui-même ne produit
   pas.
3. **Cool-down** (`TS_RESULT_COOL_TIME`, `Player::IsUseableItem` :2090-2092) : le dépôt n'a aucun
   état de cool-time d'objet et `TM_SC_ITEM_COOL_TIME` (217) n'est pas émis.
4. **Niveaux de cible** (`target_min_level` / `target_max_level`) et résolution générique de la
   cible : §6 et §7-7.
5. **Effets** (`base_type` / `opt_type`) et écriture d'état : hors périmètre §5.3-8. La
   consommation d'un exemplaire, elle, est portée (§9.3).
6. **`ItemUseFlag`** : jamais lu (§7-6), la valeur réellement importée n'étant pas documentée.

### 9.5 Réserves

1. **Catalogue vide.** Si la base Arcadia n'est pas présente ou n'est pas importée,
   `TryGetLevels` renvoie `false` et l'utilisation n'est pas filtrée. Choix assumé et symétrique du
   §6 : refuser un objet qu'on ne sait pas juger serait un refus non prouvé. Le pendant à vérifier
   côté données est la sémantique de `use_min_level = 0`.
2. **Ordre plafond/plancher.** NGemity teste le plafond avant le plancher
   (`IsUseableItem` :2094-2100) : sur un gabarit contradictoire (`use_min_level` > `use_max_level`)
   c'est `LimitMax` qui sort. Comportement conservé, testé, mais non tranché par le client.
3. **Handle d'objet.** Le service résout `(uint)item.Id` par `FindByHandle`, comme tous les paquets
   d'objet montants du dépôt ; l'identité côté client reste non prouvée par le binaire (§7-8).
4. **`szParameter`** : taille seulement, contenu ignoré (§7.3) — donc `GrantSkill` reste interdit
   tant que la skill list (403) n'existe pas.
5. **Ordre des deux trames du succès non testé automatiquement** : `ItemUseService` dépend de
   `GameClient` (socket), aucun test du dépôt n'instancie ce type. L'ordre est fixé par le code et
   la référence, pas par un test.
6. **Base de données non sollicitée en test** : `ItemUseCatalog` est testé avec un
   `IItemResourceRepository` simulé. Aucun test n'exerce `GetUseFields()` contre une vraie base,
   conformément à la règle « build et tests seulement » du conteneur.

### 9.6 Vérifications relevées

```
dotnet build Navislamia.sln -c Debug     → code 0, 0 erreur, 160 avertissements
dotnet test Tests/Tests.csproj           → code 0, 378 réussis / 378, 0 échec, 0 ignoré
git log --oneline origin/master..master  → (aucune ligne : aucun commit sur master locale)
```

Soit `366 + 12` tests : le compte ne baisse pas.

## 10. Bloc prêt à coller dans `CLAUDE.md`

`CLAUDE.md` est protégé par Hermes côté worker : il est livré ici et dans la description de la MR,
à coller par l'opérateur.

```markdown
### Paquet 253 — `TM_CS_USE_ITEM` (utilisation d'un objet)

- Trame cliente de **47** octets : en-tête 7, `item_handle` à 7, `target_handle` à 11,
  `szParameter` sur 32 octets à 15. Le paramètre est consommé pour sa taille seulement : son
  contenu n'est pas établi.
- Réponse en **deux** trames, dans cet ordre : `TS_SC_RESULT` (253, `Success`, `item_handle`) puis
  `TM_SC_USE_ITEM_RESULT` (283), qui réémet les deux handles.
- Seul le niveau de l'objet est jugé : `use_min_level` → `LimitMin`, `use_max_level` → `LimitMax`,
  le plafond testé avant le plancher comme dans NGemity `Player::IsUseableItem`. Un handle inconnu
  ou non possédé donne `NotExist`.
- `ItemUseFlag` n'est pas lu : la valeur réellement importée n'est pas documentée dans le dépôt.
  Ne jamais l'utiliser comme masque binaire sans arbitrage.
- Le refus `ACCESS_DENIED` sur le type d'objet de NGemity est du **code mort**
  (`&& false` commenté, `WorldSession.cpp:1327`) : ne pas le porter.
- Un succès consomme un exemplaire, sauf pour le type `Use` (6, réutilisable) : `TM_SC_UPDATE_ITEM_COUNT`
  (255, count int64) ou `TM_SC_DESTROY_ITEM` (254) au dernier, envoyé **avant** le `TS_SC_RESULT`
  comme dans NGemity `Player::UseItem`. Les effets de l'objet ne sont pas appliqués.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.
```

