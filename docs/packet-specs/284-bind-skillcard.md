# 284 — TM_CS_BIND_SKILLCARD

Fiche de paquet établie par `navis-ref` (archéologue de protocole), branche
`hermes/packet-284-bind-skillcard`, à partir de `master` `ec76b218cd0bd7c6498d725f253abb8b431f0cd6`.
Cette fiche ne contient **aucun** changement de code serveur : elle fixe le format, le gating de
version, le comportement attendu et les réserves vérifiables. Le jumeau `285`
(`TM_CS_UNBIND_SKILLCARD`) et la réponse `286` (`TM_SC_SKILLCARD_INFO`) sont décrits ici parce que
le 284 ne se comprend pas sans eux ; la carte 285 garde sa propre fiche.

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id décimal | **284** (`0x011C`) | `op_codes.md:98` — `[284] = "TM_CS_BIND_SKILLCARD"` |
| sens | client → serveur | rzu `librzu/src/packets/GameClient/TS_CS_BIND_SKILLCARD.h:13` (`SessionPacketOrigin::Client`) |
| nom client | `TM_CS_BIND_SKILLCARD` | `op_codes.md:98` ; dump `strings -n 4` de `SFrame.exe`, l. 26365 |
| jumeau montant | **285** `TM_CS_UNBIND_SKILLCARD`, même forme | `op_codes.md:99` ; rzu `TS_CS_UNBIND_SKILLCARD.h:5-11` ; dump l. 26364 |
| réponse serveur | **286** `TM_SC_SKILLCARD_INFO`, même forme | `op_codes.md:100` ; rzu `TS_SC_SKILLCARD_INFO.h:5-13` ; dump l. 26363 |
| taille de trame | **15 octets**, fixe, dans les trois sens | §3 |
| direction de 286 | serveur → client | rzu `TS_SC_SKILLCARD_INFO.h:13` (`SessionPacketOrigin::Server`) — le fichier est rangé dans `librzu/src/packets/GameClient/` : la direction vient de l'origine déclarée, pas du répertoire |
| carte Trello | `trello.com/c/oZa5d852` | corps de la carte d'archéologie (étape 1/3) ; suivi `navislamia:packet:284` |
| état dans le dépôt | 284, 285 et 286 absents de `GamePackets` | `Game/Network/Packets/Enums/GamePackets.cs` : 283 l. 47, 287 l. 30, aucun membre 284-286 |

Le client Epic 7.3 connaît la paire id ↔ nom, et pas seulement le nom : sa routine
d'enregistrement construit trois entrées consécutives `{id, nom}` — `push 0x14` (longueur de
`"TM_CS_BIND_SKILLCARD"` = 20), `push 0xa53550` (pointeur de la chaîne en `.rdata`), appel du
constructeur de chaîne, `mov eax,0x11c`, puis rangement dans la table — et de même pour
`0x11d` / `0xa53538` / longueur `0x16` (= 22) et `0x11e` / `0xa53520` / longueur `0x14` (= 20).
L'entrée suivante est `0x11f` / `0xa53508` = `TM_SC_ITEM_WEAR_INFO` (287), ce qui recale la table
sur les ids du corpus `op_codes.md`. Preuves brutes : `SFrame.exe` VA `0x676fba`-`0x6770fb`
(désassemblage `objdump -d -M intel`), chaînes aux VA `0xa53520` / `0xa53538` / `0xa53550`
(fichier `SFrame.exe`, sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`).

## 2. Ce que le joueur fait pour que le client l'envoie

Ce qui est établi par les deux références :

- la cible du 284 est **le joueur lui-même** : NGemity refuse tout `target_handle` différent de son
  propre handle (`WorldSession.cpp:1688-1691`), et `Unit::BindSkillCard` lie la carte au porteur du
  paquet (`Unit.cpp:2604-2612`). Le geste est donc « lier cette carte de compétence à mon
  personnage », jamais à un tiers ;
- l'objet doit être une **carte de compétence de mon inventaire** : groupe `GROUP_SKILLCARD` (= 10),
  présent dans l'inventaire, non équipé, et pas déjà lié (`WorldSession.cpp:1692`) ;
- la compétence visée doit être **déjà apprise** : `GetSkill(item->skill_id)` renvoie `nullptr` sinon
  (`Unit.cpp:2606`, `WorldSession.cpp:1697-1698`) ;
- côté client, la réponse est traitée : trace `SGameInterface - MSG_SKILLCARD_INFO`
  (dump `SFrame.exe`, l. 25158) et classe de message d'IHM `USMSG_SKILLCARD_INFO`
  (RTTI `.?AUSMSG_SKILLCARD_INFO@@`, l. 44393). L'IHM sait donc afficher l'état renvoyé par 286 ;
- le corpus de textes du client décrit bien la carte de compétence comme un objet à attributs de
  compétence : `tooltip_skillcard_9001` … `tooltip_skillcard_9028` (`db_string.rdb`, dump
  `strings -n 4`, l. 189317-189371) énumèrent MP, Casting Delay, Cool Time, Accuracy Correction,
  HP/MP Absorption Boost, etc. ;
- la carte arrive avec un niveau d'*enhance* propre à l'objet (boîtes `Basic Skill Card Box`,
  `+2 Bear Skill Card Box`, `+4 Skill Card Box`, `Lucky Skill Card Box`, dump `db_string.rdb`,
  l. 8363-8371 et 8605-8609) : c'est la valeur que `Unit::BindSkillCard` recopie dans la
  compétence (`Unit.cpp:2608`).

Ce qui reste **non établi** : le geste exact qui fait émettre le 284 par le client. Le binaire ne
porte **aucune** classe de fenêtre dédiée (`SkillCard` n'apparaît que dans `static_common_skillcardicon`,
la trace `MSG_SKILLCARD_INFO` et le RTTI `USMSG_SKILLCARD_INFO` : 5 occurrences au total dans le dump
des chaînes), aucun texte de confirmation ni de refus local ne mentionne la liaison, et
`MSG_SKILLCARD_INFO` ne figure pas dans la bascule `case MSG_*` du binaire (bloc l. 19715-19745 de
`SCommandSystem::ProcMsgAtStatic`, qui est de toute façon incomplète : la même absence existe pour
`MSG_USE_ITEM_RESULT` alors que le 253 est, lui, prouvé par ailleurs). Voir §7.1.

## 3. Structure sur le fil

Trame client → serveur, en-tête Navislamia de 7 octets (`Length` `uint32` @0, `ID` `uint16` @4,
`Checksum` `uint8` @6 — `Game/Network/Packets/Header.cs:6-24`, `GameActionPackets.cs:7`), puis les
deux champs de rzu `TS_CS_BIND_SKILLCARD.h:6-7` dans cet ordre :

| Offset | Taille | Type | Nom (rzu) | Valeur observée | Source |
| --- | --- | --- | --- | --- | --- |
| 0 | 4 | `uint32` | `Length` | 15 | `Header.cs:20-22` ; `Packet<T>.Length` |
| 4 | 2 | `uint16` | `ID` | 284 | `op_codes.md:98` |
| 6 | 1 | `uint8` | `Checksum` | non vérifié (le dépôt ne l'impose pas en réception) | `Header.cs:11`,`:24` |
| 7 | 4 | `ar_handle_t` (`uint32`) | `item_handle` | handle d'inventaire de la carte — dans ce dépôt `(uint)ItemEntity.Id` | rzu `TS_CS_BIND_SKILLCARD.h:6` ; `GameCharacterPackets.cs:343` (le handle émis pour un objet est `item.Id`) ; `CharacterService.cs:436-439` ; lecture modèle `GameActionPackets.cs:180-195` |
| 11 | 4 | `ar_handle_t` (`uint32`) | `target_handle` | handle du personnage porteur — dans ce dépôt `(uint)CharacterEntity.Id` | rzu `TS_CS_BIND_SKILLCARD.h:7` ; NGemity `WorldSession.cpp:1688` ; `GameActions.cs:96` |

**Taille totale attendue : 15 octets** — 7 (en-tête) + 4 + 4. Les trois paquets ont la même forme :
rzu donne exactement les deux mêmes champs à `TS_CS_UNBIND_SKILLCARD` (`:6-7`) et à
`TS_SC_SKILLCARD_INFO` (`:6-7`), donc 285 et 286 font aussi 15 octets. La réponse 286 porte l'état
**après** l'opération : `target_handle` = le handle du porteur après liaison, `0` après déliaison
(`Messages.cpp:1010-1017`, `Item.cpp:326-329`).

Pourquoi 4 octets par champ, et pas autre chose : `ar_handle_t` est un `strong_typedef<uint32_t>`
qui stocke sa valeur dans un membre `T value_` (`librzu/src/lib/Packet/GameTypes.h:6-24`, `:40-42`),
il est déclaré « primitive » par `StructSerializer::is_strong_typed_primitive`
(`StructSerializer.h:22-31`), et `PacketDeclaration::getSizeOf` renvoie alors `sizeof(value)` = 4
(`PacketDeclaration.h:68-76`), accumulé par `SIZE_F_SIMPLE2` (`:215`). Les 7 octets de base viennent
de `CREATE_PACKET_VER_ID` (`PacketDeclaration.h:616-621`).

Le champ `Checksum` n'est pas une inconnue de format mais une inconnue de valeur : la boucle de
réception lit les trois champs d'en-tête et n'utilise que `ID` (`Header.cs:19-24` ; `GameClient.cs`
ne teste que `header.ID`). La valeur que le client place en @6 n'est pas établie ici ; elle
n'influence aucun décalage.

## 4. Gating de version

Tranché pour Epic 7.3 :

- **seul l'id est gaté** : rzu déclare `X(284, version < EPIC_9_6_3)` et `X(1284, version >= EPIC_9_6_3)`
  (`TS_CS_BIND_SKILLCARD.h:10-11`) ; idem `285`/`1285` (`TS_CS_UNBIND_SKILLCARD.h:10-11`) et
  `286`/`1286` (`TS_SC_SKILLCARD_INFO.h:10-11`). `EPIC_9_6_3 = 0x090603`
  (`librzu/src/lib/Packet/PacketEpics.h:96`) est postérieur à `EPIC_7_3 = 0x070300` (`:59`) :
  **pour Epic 7.3 les ids sont 284, 285 et 286**, et la famille 1284/1285/1286 est hors sujet ;
- **aucun champ n'est gaté dans les trois paquets** : les blocs `_DEF` n'ont ni `_(simple)(..., version >= ...)`
  ni `_(def)`/`_(impl)` — contrairement à `TS_CS_TAKE_ITEM.h:6` (`version >= EPIC_5_2`) ou
  `TS_CS_DROP_ITEM.h:7-9` (`version < EPIC_4_1`). Le critère 6 est donc satisfait par décision
  explicite : rien à gater, les deux champs sont toujours présents en 7.3 ;
- contre-épreuve côté client : le binaire 7.3 enregistre ses noms sur **284/285/286** (§1), pas sur
  une famille +1000. Le gating rzu et le corpus client concordent.

## 5. Traitement attendu

### 5.1 Ce que fait la référence NGemity (`38ceb2c`, EPIC `4_1_1`)

`WorldSession::onBindSkillCard` (`Chihiro/src/Network/GameNetwork/WorldSession.cpp:1678-1701`,
déclaré `STATUS_AUTHED` l. 133, prototype `WorldSession.h:100`) juge dans cet ordre :

1. résolution de l'objet par `item_handle` ; handle inconnu →
   `SendResult(received_id, TS_RESULT_NOT_EXIST (1), item_handle)` (`:1683-1687`) ;
2. `target_handle != m_pPlayer->GetHandle()` →
   `SendResult(received_id, TS_RESULT_NOT_ACTABLE (5), target_handle)` (`:1688-1691`) ;
3. objet hors inventaire, ou propriétaire différent, ou groupe ≠ `GROUP_SKILLCARD`, ou **déjà lié**
   (`m_hBindedTarget != 0`) → `SendResult(received_id, TS_RESULT_ACCESS_DENIED (6), item_handle)`
   (`:1692-1695`) ;
4. compétence absente **ou** enhance déjà non nul → **aucune réponse** (`:1697-1700`) ;
5. sinon `Player::BindSkillCard(item)` (`Unit.cpp:2604-2612`) : `skill.m_nEnhance = item.Enhance`,
   `Item::SetBindTarget(this)` (`Item.cpp:314-332` : socket 0 = UID du porteur, socket 1 = 0,
   `m_hBindedTarget` = handle), puis **seul** `TS_SC_SKILLCARD_INFO` (286) part
   (`Messages.cpp:1010-1017`), avec `item_handle` et `target_handle` du nouvel état. Pas de
   `TS_SC_RESULT` en succès, pas de rafraîchissement d'inventaire.

`onUnBindSkilLCard` (`:1703-1720`, `WorldSession.h:101`) est le miroir : `NOT_EXIST` si le handle est
inconnu, `NOT_ACTABLE` si la cible n'est pas soi, `ACCESS_DENIED` si la carte n'est pas dans
l'inventaire, pas à moi, pas du groupe 10 ou **pas liée** (`m_hBindedTarget == 0`), puis
`Player::UnBindSkillCard` (`Unit.cpp:2614-2622` : enhance remis à 0, `SetBindTarget(nullptr)`) et 286.

L'état persistant est **le socket de l'objet**, pas une table à part : `SetBindTarget` écrit le socket 0
(ou 1 pour un familier) et `m_bIsNeedUpdateToDB = true` (`Item.cpp:330`), les quatre sockets sont
sauvegardés avec la ligne d'objet (`Item.cpp:99-102`, `:138-141`), et à la connexion le serveur
relie automatiquement toute carte du groupe 10 dont le socket 0 vaut l'UID du joueur
(`Player.cpp:800-810`). Conséquence de bord : une carte liée n'est pas effaçable
(`Player::IsErasable`, `Player.cpp:3144`) et est déliée à la destruction de l'objet
(`Player.cpp:1229-1233`).

Enums de résultat : `TS_RESULT_NOT_EXIST = 1`, `TS_RESULT_NOT_ACTABLE = 5`,
`TS_RESULT_ACCESS_DENIED = 6` (`shared/Server/TS_MESSAGE.h:55-63`), identiques aux valeurs du dépôt
(`Game/Network/Packets/ResultCode.cs:6,8,12,13`).

### 5.2 Ce que Navislamia doit faire (décisions)

**a. Énumération.** Ce cycle ajoute `TM_CS_BIND_SKILLCARD = 284` et
`TM_SC_SKILLCARD_INFO = 286` à `GamePackets.cs`, à côté de leurs voisins déjà présents
(`TM_SC_ITEM_WEAR_INFO = 287` l. 30, `TM_CS_USE_ITEM = 253` l. 41, `TM_SC_USE_ITEM_RESULT = 283`
l. 47). `TM_CS_UNBIND_SKILLCARD = 285` est décrit ici comme le miroir de 284 mais **n'appartient pas
à ce cycle** : sa propre carte et sa propre branche existent et son membre d'énumération sera ajouté
par ce cycle-là (deux trames, deux cartes, deux fiches, deux branches).

```
TM_CS_BIND_SKILLCARD = 284,     // ce cycle
TM_SC_SKILLCARD_INFO = 286,     // ce cycle (réponse commune à 284 et 285)
TM_CS_UNBIND_SKILLCARD = 285,   // cycle suivant, sa carte
```

**b. Lecture.** Un `TryReadBindSkillCard` calqué sur `TryReadUseItem` (`GameActionPackets.cs:180-195`) :
exiger `HeaderSize + 8` octets, lire `item_handle` à 7 et `target_handle` à 11 en little-endian ;
rejeter une trame plus courte par `SendResult(id, ResultCode.InvalidArgument (28))`, comme
`GameClient.cs:485-487` le fait pour le 253. Taille attendue : **15** (§3).

**c. Écriture de la réponse.** Un `BuildSkillCardInfo(uint itemHandle, uint targetHandle)` jumeau de
`BuildUseItemResult` (`GameCharacterPackets.cs:205-213` : `CreatePacket`, handle à 7, deuxième handle
à 11, `WriteChecksum`) : 15 octets, id 286. Le 286 réutilise exactement la forme du 283.

**d. Distribution (critère transversal 4).** La bascule finale de `GameClient.cs:791-803` lève
`Unknown Packet Type` sur tout id non traité : 284 exige donc un bras dans la chaîne
de dispatch (modèle : `TM_CS_USE_ITEM` en `GameClient.cs:708-712`, appel `_ = Handle…Async(msgBuffer)`
puis `continue`) ; le 285 recevra le sien dans son cycle.

Pour **286** (paquet serveur → client), la décision retenue est celle du précédent explicite du
dépôt : `TM_SC_REGION_ACK` (11) est déclaré dans l'énumération **et** possède un bras qui journalise
puis ignore la trame, avec le commentaire qui fixe la règle — « server to client packet: the 7.3
client never sends it. An incoming one is a protocol anomaly, not a request, so it is logged and
dropped instead of reaching the "Unknown Packet Type" throw below » (`GameClient.cs:620-628`).
286 suit la même règle : membre déclaré (nécessaire pour émettre) **plus** bras « journaliser et
ignorer ». C'est ce qui satisfait le critère 4 pour le nouveau paquet. Observation hors périmètre :
283 et 287, déclarés avant cette règle, n'ont pas ce bras et atteignent encore la bascule finale —
`GamePackets.cs:30,47`. À ne pas imiter.

**e. Traitement.** Un service dédié (modèle `ItemUseService` : `ItemUseService.cs:1-80`) qui applique
la séquence NGemity en §5.1, dans cet ordre :

1. `ICharacterService.GetItemByHandleAsync(characterName, itemHandle)` (`CharacterService.cs:204-208`) ;
   `null` → `SendResult(284, NotExist (1), unchecked((int)itemHandle))` ;
2. `target_handle != info.CharacterHandle` → `SendResult(284, NotActable (5), unchecked((int)targetHandle))` ;
   la définition « soi » du dépôt est `CharacterHandle = (uint)character.Id` (`GameActions.cs:96`,
   usage « cible soi » : `InventoryService.cs:36`) ;
3. groupe de l'objet : `ItemResourceEntity.Group` (`ItemResourceEntity.cs:13`) via
   `IItemGroupCatalog.TryGetGroup` (`ItemGroupCatalog.cs:28-31`), comparé à `ItemGroup.Skillcard`
   (`ItemGroup.cs:15`, valeur 10) ; plus `WearInfo == None` et « pas déjà liée » →
   `SendResult(284, AccessDenied (6), itemHandle)` ;
4. liaison ; sinon rien (§5.1 point 4, voir §7.4) ;
5. réponse `BuildSkillCardInfo(itemHandle, targetHandle)` (286), et **rien d'autre**.

**f. Persistance.** La carte liée se matérialise sur le socket 0 de l'objet, comme NGemity :
`socket[0] = CharacterEntity.Id` à la liaison, `0` à la déliaison. Le champ existe déjà
(`ItemEntity.SocketItemIds`, `ItemEntity.cs:35`), il est déjà sérialisé (`GameCharacterPackets.cs:353-356`,
4 × `int32` aux offsets 38/42/46/50 de l'enregistrement d'objet) et l'enregistrement porte déjà la
valeur d'enhance de la carte à l'offset 32 (`GameCharacterPackets.cs:349`). Aucune colonne nouvelle
n'est nécessaire. Il faut en revanche une écriture en base : un chemin du type
`CharacterService.SwapItemPositionsAsync`/`ConsumeItemAsync`, qui charge le personnage avec ses
objets (`CharacterRepository.GetCharacterByNameWithItems`, l. 43-52 — la requête inclut déjà les
compétences, l. 47) et repasse par la porte `_databaseGate`.

**g. Compétence.** `ItemResourceEntity.SkillId` (`ItemResourceEntity.cs:88`) et
`CharacterSkillEntity` (`SkillId` l. 7, `Level` l. 8) suffisent à vérifier que le joueur connaît la
compétence, ce que NGemity exige (`Unit.cpp:2606`). Cette vérification est **facultative en 7.3**
(§7.4). Aucun champ d'enhance de compétence n'existe dans le dépôt et la fiche n'en demande pas
(§7.5).

**h. Tests.** Un test d'offsets conforme au critère 3, dans
`Tests/Game/ActionPacketsTests.cs` (modèle des paquets d'action) : longueur totale 15 pour 284 et
pour 286 ; `item_handle` à 7-10, `target_handle` à 11-14 ; id aux offsets 4-5 ; présence de l'octet de
checksum recalculé ; rejet des trames < 15. Le 285 aura le sien dans son cycle. Plancher de la suite :
**366 tests** (critère 2) ; le compte relevé sur `master` à la rédaction de cette fiche est **448**.

**i. Primitives absentes de `master` — à ne pas supposer disponibles.** La famille « cartes de châsse »
(214/215) porte `Game/Services/CardSocketService.cs`, `CardSocketCatalog.cs`, `CardSocketRules.cs` et
`ICardSocketService` : **aucun de ces fichiers n'existe sur `master`** (vérifié : pas une occurrence de
`CardSocketService` dans l'arbre de `master` ; ils vivent sur `hermes/packet-214-puton-card`, `d9b6fb2`).
Le socle « invocations et familiers » (`hermes/packet-socle-invocations`) n'est pas mergé non plus. Le
284 nominal n'en dépend pas — la cible doit être le joueur (`WorldSession.cpp:1688-1691`) — mais la
**liaison à une créature invoquée** (socket 1, `Item.cpp:322-324`, reprise automatique au login
`Player.cpp:806-810`) dépend de ce socle : hors périmètre, à traiter là où le socle arrive.

### 5.3 Primitives réutilisables (vérifiées sur `master`)

| Fichier | Primitives |
| --- | --- |
| `Game/Network/Packets/Enums/GamePackets.cs` | membres voisins 283 (l. 47), 287 (l. 30), 253 (l. 41) |
| `Game/Network/Packets/Header.cs` | en-tête 7 octets (l. 6-24) |
| `Game/Network/Packets/ResultCode.cs` | `Success 0` (6), `NotExist 1` (8), `NotActable 5` (12), `AccessDenied 6` (13), `InvalidArgument 28` (37) |
| `Game/Network/Packets/Game/TS_SC_RESULT.cs` | `RequestMsgID`, `Result`, `Value` (l. 8-10) |
| `Game/Network/Clients/GameClient.cs` | `SendResult` (l. 56-60), bras 253 (l. 708-712), bras « S→C reçu, journaliser et ignorer » (l. 620-628), bascule finale (l. 791-803) |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `HeaderSize = 7` (l. 7), `TryReadUseItem` (l. 180-195) comme gabarit de lecture |
| `Game/Network/Packets/Game/GameCharacterPackets.cs` | `BuildUseItemResult` (l. 205-213) comme gabarit d'écriture, `WriteInventoryItem` (l. 341-364 : handle 343, UID 345, enhance 349, sockets 353-356) |
| `Game/Services/CharacterService.cs` | `GetItemByHandleAsync` (l. 204-208), `FindByHandle` (l. 436-439 : `(uint)item.Id == handle`) |
| `Game/Services/ItemGroupCatalog.cs` | `TryGetGroup` (l. 28-31) |
| `Game/Services/InventoryService.cs` | `SendInventory` (l. 125-131), usage après mutation (l. 53) |
| `Game/Services/ItemUseService.cs` | squelette de service d'action d'objet (erreurs DB → `DBError`) |
| `CLAUDE.md:1028-1035` | groupe 10 = `Skillcard`, classé « Cards » par l'ordre des onglets du client |

## 6. Écarts assumés avec NGemity

1. **Espace des handles.** NGemity résout `item_handle` dans une mémoire d'objets globale
   (`sMemoryPool.GetObjectInWorld<Item>`, `WorldSession.cpp:1683`) : un objet d'un autre joueur s'y
   résout puis échoue sur le contrôle de propriétaire en `ACCESS_DENIED`. Le dépôt résout le handle
   **parmi les objets du personnage** (`CharacterService.cs:436-439`), donc un objet qui n'est pas le
   mine est indistinguable d'un handle inexistant : `NotExist` (1), pas `AccessDenied`. C'est l'écart
   déjà assumé et documenté pour le 253 (`docs/packet-specs/253-use-item.md:409-410`) ; le client ne
   perd rien : il reçoit un refus explicite au lieu de `AccessDenied`.
2. **Le garde d'enhance devient un garde de socket.** NGemity teste `pSkill->GetSkillEnhance() == 0`
   (`WorldSession.cpp:1698`) ; le dépôt n'a pas d'enhance de compétence et ce test est l'image du
   socket 0 : « déjà liée » ⇔ `SocketItemIds[0] != 0`. Un seul contrôle au lieu de deux, même
   sémantique.
3. **Le socket est un champ à usage multiple.** NGemity y range successivement des identifiants
   d'objets sertis (`Item.cpp:233-234` additionne l'endurance des pierres), l'UID du porteur d'une
   carte (socket 0/1, `Item.cpp:317-324`), le SID d'une carte de familier (`Item.h:54`, `Player.cpp:739`)
   et les niveaux de job précédents d'un familier (`Summon.cpp:303-305`). Le champ du dépôt s'appelle
   `SocketItemIds` (`ItemEntity.cs:35`) et son commentaire d'usage n'existe pas : écrire l'UID du
   personnage dans le socket 0 est conforme à la référence mais élargit de fait la sémantique du
   champ. Voir §7.8.
4. **Pas de mise à jour d'inventaire.** NGemity n'envoie que 286 et laisse le client appliquer.
   Le dépôt sait renvoyer un enregistrement d'objet (`InventoryService.SendInventory`) ; la fiche ne
   le demande pas, mais c'est le levier à utiliser si le contrôle en jeu montre une carte qui ne se
   met pas à jour (§7.2).
5. **Aucune réponse quand la liaison est impossible pour cause de compétence** (NGemity
   `:1697-1700` — silence total). Écart volontaire conservé ? Voir §7.4 : c'est le seul point de
   cette fiche qui mérite un arbitrage, pas une supposition.
6. **Les enums d'amélioration n'éclairent pas la politique de refus.** `EnhancementFailResult`
   (`MiscFail 0`, `GearFail 1`, `SkillCardFail 2`, `AccessoryFail 3`) n'a **aucun usage** dans le
   dépôt : seule sa définition existe (`Game/DataAccess/Entities/Enums/EnhancementFailResult.cs`).
   `FailResultType` (`Fail 1`, `SkillCard 2`, `Accessory 3`) n'est porté que par
   `EnhanceResourceEntity.FailResult` (`EnhanceResourceEntity.cs:8`). Les deux décrivent le
   **résultat d'une tentative d'amélioration** d'objet (données `db_enhance.rdb`), pas le refus d'une
   liaison ; le corpus client le dit aussi : *« May enchant skill card. Must use combination. »*
   (`db_string.rdb`, dump `strings -n 4`, l. 5513-5515) — l'enhance d'une carte s'obtient par
   combinaison, pas par le 284. Aucun code de refus de cette fiche n'en dérive.

## 7. NON ÉTABLI

1. **Le geste client.** Quelle action (double-clic sur la carte, entrée de menu contextuel, bouton
   d'une fenêtre de carte) fait émettre le 284 n'est pas lisible statiquement : pas de classe de
   fenêtre dédiée, pas de texte de confirmation ou de refus dans `db_string.rdb`, pas de `case
   MSG_SKILLCARD_INFO` dans la bascule du binaire. Le format (§3) et la sémantique (§5) n'en
   dépendent pas ; la question ne se tranche qu'en jeu ou par désassemblage du gestionnaire d'IHM de
   la fenêtre d'inventaire.
2. **Ce que le client fait du 286 seul.** NGemity n'envoie que le 286 après liaison
   (`Unit.cpp:2610`). Le client sait aussi lire l'état *persistant* : à la connexion il rebâtit la
   liaison depuis le socket 0 de l'objet (`Player.cpp:800-810`). Question précise : le client
   rafraîchit-il l'affichage de la compétence sur la seule réception du 286, ou faut-il lui renvoyer
   l'enregistrement d'objet (`InventoryService.SendInventory`) pour que la carte apparaisse liée ?
   Les textes `#@skill_enhance@#` du client (`db_string.rdb`) montrent qu'un niveau d'enhance est
   affiché avec le nom de la compétence, mais pas d'où le client le tire : l'hypothèse la plus
   cohérente avec NGemity est qu'il le déduit de la carte liée de son inventaire (enhance à l'offset
   32 de l'enregistrement, `GameCharacterPackets.cs:349`) — **hypothèse, non démontrée ici**.
3. **Les données d'objets.** Quelles ressources sont réellement des cartes de compétence, avec quel
   `skill_id` et quel groupe, vient de la base `Arcadia`, **absente de ce VPS** ; le corpus client
   contient bien `db_item.rdb` (176 Mo) mais le dépôt n'a aucun lecteur de sa table d'objets
   (`tools/` ne sait lire que les fichiers de terrain). La chaîne complète 284 → objet → compétence
   n'est donc pas vérifiable ici : à contrôler sur une base importée.
4. **Le contrôle de compétence apprise.** NGemity exige `GetSkill(skill_id) != nullptr` et ne
   répond rien sinon. Faut-il le reproduire tel quel (silence), le refuser proprement
   (`ResultCode.NoSkill = 27`, `ResultCode.cs:36`), ou l'ignorer à ce cycle puisque le dépôt ne
   calcule aucune effet de compétence ? Tant que la liaison n'a pas d'effet, un `NoSkill` inventerait
   un refus que la référence n'émet pas.
5. **Le devenir de l'enhance.** « Lier » doit, dans la référence, recopier l'enhance de la carte dans
   la compétence (`Unit.cpp:2608`) et cet enhance multiplie les effets en combat
   (`Skills/SkillFunctor.h:39-40,86,98`, `Skill.cpp:432`). Le dépôt n'a **pas** d'enhance de
   compétence (`CharacterSkillEntity` = `SkillId` + `Level`) et le 403 envoyé au client porte le
   niveau de base deux fois (`GameCharacterPackets.cs:290-293`, `current_skill_level` = `base_skill_level`).
   Aucun effet observable de la liaison n'est donc démontrable dans le dépôt à ce stade : **NON
   ÉTABLI** et hors du périmètre de cette fiche.
6. **Effets de bord sur d'autres paquets.** La référence refuse l'effacement d'une carte liée
   (`Player.cpp:3144`) et la délie à sa destruction (`Player.cpp:1229-1233`) ; ces comportements
   touchent le 208 (`TM_CS_ERASE_ITEM`), hors périmètre ici.
7. **La valeur de l'octet 6 en émission client** (`Checksum`) : inconnue, sans incidence sur les
   décalages (§3).
8. **Le sens du socket 0 dans le dépôt.** `SocketItemIds` est nommé d'après les pierres serties, que
   NGemity y range aussi (`Item.cpp:233-234`) ; y écrire `CharacterEntity.Id` élargit la sémantique du
   champ (§6.3). À ce commit, rien d'autre ne lit ce champ : il est défini (`ItemEntity.cs:35`), borné à
   quatre éléments (`TelecasterContext.cs:55`) et sérialisé (`GameCharacterPackets.cs:353-356`), sans
   autre lecteur. Question précise pour la suite : valider que les cartes de familier (socle
   invocations, §5.2 i) ne réutilisent pas ces slots avec une autre convention.

## 8. Commits épinglés

| Dépôt | Commit | Rôle |
| --- | --- | --- |
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | tailles, ordre des champs, gating (`TS_CS_BIND_SKILLCARD.h`, `TS_CS_UNBIND_SKILLCARD.h`, `TS_SC_SKILLCARD_INFO.h`, `PacketEpics.h`, `GameTypes.h`, `StructSerializer.h`, `PacketDeclaration.h`) |
| `reference/ngemity/Chihiro` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | logique : `WorldSession.cpp:1678-1720`, `Unit.cpp:2604-2622`, `Messages.cpp:1010-1017`, `Item.cpp:99-141,314-332`, `Player.cpp:800-810,1229-1233,3144`, `TS_MESSAGE.h:55-63` |
| `Navislamia` (`master`) | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | primitives du dépôt et état de l'énumération |
| client Epic 7.3 | `reference/client73/SFrame.exe`, sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | table `{id, nom}` VA `0x676fba`-`0x6770fb` ; chaînes VA `0xa53520`/`0xa53538`/`0xa53550` ; dump `strings -n 4` l. 25158, 26363-26365, 44393 ; `db_string.rdb` idem l. 8363-8371, 189317-189371 |

## 9. Bloc destiné à `CLAUDE.md` (proposition — à porter par la description de la MR, jamais écrit par l'archéologue)

> `TM_CS_BIND_SKILLCARD` (`284`), `TM_CS_UNBIND_SKILLCARD` (`285`) et `TM_SC_SKILLCARD_INFO` (`286`)
> portent la même trame de 15 octets : en-tête 7, puis `item_handle` (`uint32` @7) et `target_handle`
> (`uint32` @11). Les trois ids ne sont gatés que par la version (`>= EPIC_9_6_3` les décale à
> 1284/1285/1286) ; **aucun champ n'est gaté**. `target_handle` est le porteur lui-même
> (`(uint)character.Id`) ; `item_handle` est `(uint)ItemEntity.Id`. L'état « lié » vit dans le socket 0
> de l'objet (= `CharacterEntity.Id`), comme dans NGemity qui relie la carte de son socket au login ;
> il n'existe donc pas de colonne dédiée. 286 est **serveur → client** : déclaré dans `GamePackets`
> avec un bras « journaliser et ignorer » sur le même modèle que `TM_SC_REGION_ACK`
> (`GameClient.cs:620-628`), sinon il atteint la bascule finale qui lève `Unknown Packet Type`.
> Sur refus, `TS_SC_RESULT` porte `NotExist (1)`, `NotActable (5)` ou `AccessDenied (6)` ; en succès
> NGemity **ne renvoie rien d'autre** que le 286.

## 10. Implémentation livrée (`navis-dev`)

Le paquet est porté sur la branche de l'archéologue, commit `67319ce` (« Bind a skill card with
`TM_CS_BIND_SKILLCARD` (284) »). Les numéros de ligne de cette section sont ceux de la **branche**
après ce commit ; ceux de §5.3 restent ceux de `master` `ec76b21` (l'insertion de `BuildSkillCardInfo`
décale de +16 tout ce qui suit la ligne 214 de `GameCharacterPackets.cs`).

| Fichier | Ce qui y a été porté |
| --- | --- |
| `Game/Network/Packets/Enums/GamePackets.cs:48-49` | `TM_CS_BIND_SKILLCARD = 284`, `TM_SC_SKILLCARD_INFO = 286` (285 reste à son propre cycle) |
| `Game/Network/Packets/Game/GameActionPackets.cs:21,199-215` | `BindSkillCardRequest`, `TryReadBindSkillCard` (gabarit `TryReadUseItem`), rejette toute trame < 15 octets |
| `Game/Network/Packets/Game/GameCharacterPackets.cs:214-229` | `BuildSkillCardInfo` (gabarit `BuildUseItemResult`) |
| `Game/Services/SkillCardBindRules.cs` | règles pures : `BearerSocketIndex` (36), `IsSelfTarget` (46), `IsBound` (56), `CheckBindable` (66) |
| `Game/Services/SkillCardBindResult.cs` | `SkillCardBindOutcome` {`Success`, `NotFound`, `NotActable`, `AccessDenied`} et le verdict porté avec le personnage et l'objet |
| `Game/Services/ICharacterService.cs:36-49` | contrat gated de la liaison |
| `Game/Services/CharacterService.cs:211-249` | `BindSkillCardAsync` sous `_databaseGate` : résolution du handle → cible → groupe/porté/lié → écriture → `SaveChangesAsync` |
| `Game/Services/CharacterService.cs:251-262` | `WriteBearerSocket` : tableau **refait** à quatre slots (`TelecasterContext.cs:55`) pour que le change tracker voie la ligne modifiée ; sockets 1-3 conservés |
| `Game/Services/SkillCardService.cs`, `Game/Services/Interfaces/ISkillCardService.cs` | verdict → `TS_SC_RESULT` ; succès → `BuildSkillCardInfo` seul ; exception → `DBError` |
| `Game/Network/Clients/GameClient.cs:519-533` | `HandleBindSkillCardAsync` (lecture ratée → `InvalidArgument`) |
| `Game/Network/Clients/GameClient.cs:744-748` | bras de réception du 284 |
| `Game/Network/Clients/GameClient.cs:648-658` | bras « S→C reçu, journaliser et ignorer » du 286, sur le modèle du 283 (§5.2 b) |
| `Game/Network/NetworkService.cs:32,56,71` + `DevConsole/Program.cs:240` | injection du service |

### 10.1 Décisions prises au-delà de la fiche

1. **L'ordre de NGemity est porté à l'intérieur de la porte.** La résolution du handle précède le
   contrôle de cible (`WorldSession.cpp:1683` puis `:1688`) : un handle inconnu porteur d'une cible
   fausse répond `NotExist` (1), jamais `NotActable` (5). Test
   `BindSkillCard_AnswersNotFoundForAHandleTheCharacterDoesNotOwn`.
2. **Handle nul refusé sur les deux faces.** `IsSelfTarget` exige `characterHandle != 0`, motif repris
   de `SkillService.cs:35`. Motif : après une liaison, `socket[0]` vaut `CharacterEntity.Id` ; or
   `socket[0] == 0` **est** la lecture « non liée ». Accepter un handle 0 répondrait `Success` tout en
   écrivant l'état opposé. Un personnage entré en jeu a toujours un handle non nul
   (`ConnectionInfo.CharacterHandle = (uint)character.Id`, `GameActions.cs:96`) : ce cas n'existe pas
   pour le client 7.3, il ne change donc aucun comportement observable.
3. **Ressource inconnue du catalogue → règle non gardée, pas de refus.** C'est le contrat écrit de
   `IItemGroupCatalog.cs:12-14` (« *the caller must then leave the group-gated rule ungated rather than
   refuse an item it cannot judge* ») et la politique déjà tenue par `GroundItemService.ResolveDropCount`
   et `ItemUseService`. Un `AccessDenied` sur une ressource absente de `db_item_resource` inventerait un
   refus que ni NGemity ni la fiche n'émettent. Test
   `BindSkillCard_LeavesACardOfAnUnknownResourceBindable`.
4. **Aucune écriture quand le verdict est un refus** : `SaveChangesAsync` n'est appelé que sur le chemin
   `Success` (`MustNotHaveHappened` dans trois tests).
5. **Pas de `SendInventory` en plus du 286** : la fiche ne le demande pas (§6.4), le levier reste décrit
   pour §7.2.

### 10.2 Tests

| Fichier | Ajout |
| --- | --- |
| `Tests/Game/ActionPacketsTests.cs` | 284 : trame de 15 octets (`item_handle` @7, `target_handle` @11, id @4-5), cible nulle, rejet d'une trame < 15, ids 284/286 |
| `Tests/Game/GameCharacterPacketsTests.cs` | 286 : longueur 15, en-tête et checksum via `AssertFrame`, les deux handles, cible nulle = état délié |
| `Tests/Game/SkillCardBindTests.cs` (nouveau, 17 tests) | règles pures (`IsSelfTarget`, `IsBound`, `CheckBindable` et ses six cas, codes de réponse) et exécution sous la porte : socket 0 = `CharacterEntity.Id`, sockets 1-3 conservés, `NotFound`/`NotActable`/`AccessDenied` sans écriture, ressource inconnue non gardée |

`dotnet build Navislamia.sln -c Debug` → 0 erreur ; `dotnet test Tests/Tests.csproj` → **471 tests
réussis, 0 échec** (448 au commit précédent, +23). `git log --oneline origin/master..master` vide.

### 10.3 Réserves reportées à l'arbitrage

- **§7.4, contrôle de compétence apprise : non porté.** NGemity ne répond rien quand la carte référence
  une compétence absente (`:1697-1700`) ; le dépôt ne calcule aucun effet de compétence et un
  `NoSkill (27)` inventerait un refus que la référence n'émet pas. La liaison est donc accordée sans ce
  contrôle : à trancher par Killian (silence, `NoSkill`, ou ignorer à ce cycle).
- **§7.1, §7.2, §7.3, §7.5 à §7.8 : inchangées.** Le geste client, l'effet du 286 seul, les données
  `Arcadia` réelles, l'enhance de compétence et le sens élargi du socket 0 restent à contrôler sur une
  base importée et en jeu.

## Note de livraison

- Branche : `hermes/packet-284-bind-skillcard`, créée depuis `master`
  `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (dépôt propre, `git log --oneline origin/master..master`
  vide avant et après ce commit).
- Aucun fichier de code touché : cette fiche est le seul livrable.
- `master` n'est pas modifié, aucune autre branche n'est créée, aucun rebase ni merge.
- Contrôle de non-régression sur cette branche (changement documentaire uniquement) :
  `dotnet build Navislamia.sln -c Debug` → 0 erreur (160 avertissements préexistants) ;
  `dotnet test Tests/Tests.csproj` → **448 tests réussis, 0 échec** (`NUGET_PACKAGES=/srv/navislamia/.nuget-cache`).
  Ce nombre est le plancher réel du dépôt à ce commit ; le critère 2 en exige 366 au minimum, il ne
  doit jamais baisser.
- Réserves à arbitrer : §7.4 (contrôle de compétence : silence ou `NoSkill`) et §7.2 (faut-il, en plus
  du 286, renvoyer l'enregistrement d'objet pour que la carte apparaisse liée).
- Les numéros de ligne du brief PO viennent d'une lecture antérieure de `master` et ont bougé :
  `TM_SC_USE_ITEM_RESULT = 283` est en `GamePackets.cs:47` (et non 54), la convention
  « `item_handle` == `ItemEntity.Id` » s'ancre en `CharacterService.cs:436-439` (et non 377-380), et
  `ar_handle_t` vit en `librzu/src/lib/Packet/GameTypes.h:40` (et non `Types/`) ; cette fiche cite les
  lignes de `master` `ec76b21`, revérifiées une à une.
