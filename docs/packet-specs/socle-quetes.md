# Socle « quêtes » — 600, 601, 602, 603, 604, 605

Fiche de socle établie par `navis-ref` (archéologue de protocole), branche
`hermes/packet-socle-quetes`, depuis `master` `ec76b218cd0bd7c6498d725f253abb8b431f0cd6`
(HEAD relevé le 2026-09-21 ; `dotnet test Tests/Tests.csproj` y relève **448 tests passés,
0 échec**, vérifié à nouveau pendant la rédaction de cette fiche). Cette fiche ne contient **aucun**
changement de code serveur : elle fixe les opcodes, les formats 7.3, le gating, le périmètre
implémentable et les réserves vérifiables.

Elle couvre six paquets qui n'existent pas isolément dans le dépôt : le moteur de quêtes est absent,
et c'est le socle — modèle de quête, état du joueur, liste/état, abandon — que cette fiche délimite.
Les trois cartes `TM_CS_DROP_QUEST` (603), `TM_CS_QUEST_INFO` (604) et `TM_CS_END_QUEST` (605)
parquées en `THINKING` le 2026-09-21 ne sont pas traitées ici comme trois paquets indépendants :
elles dépendent de ce socle.

## 1. Identité

| id | nom | sens | rzu | `op_codes.md` | client 7.3 (`SFrame.exe`) |
| --- | --- | --- | --- | --- | --- |
| **600** | `TM_SC_QUEST_LIST` | serveur → client | `librzu/src/packets/GameClient/TS_SC_QUEST_LIST.h:25-35` | l. 155 | **handler de réception** `0x00670cd0` ; nom en table `0x00677e82` |
| **601** | `TM_SC_QUEST_STATUS` | serveur → client | `librzu/src/packets/GameClient/TS_SC_QUEST_STATUS.h:7-19` | l. 156 | **handler de réception** `0x0067db20` ; nom en table `0x00677ee4` |
| **602** | `TM_SC_QUEST_INFOMATION` | serveur → client | `librzu/src/packets/GameClient/TS_SC_QUEST_INFOMATION.h:13-22` | l. 157 (`TM_SC_QUEST_INFORMATION`) | nom en table `0x00677f46` ; **aucun handler** (§5.4) |
| **603** | `TM_CS_DROP_QUEST` | client → serveur | `librzu/src/packets/GameClient/TS_CS_DROP_QUEST.h:5-12` | l. 158 | **constructeur de trame** `0x0048cd70` ; nom en table `0x00677fa8` |
| **604** | `TM_CS_QUEST_INFO` | client → serveur | `librzu/src/packets/GameClient/TS_CS_QUEST_INFO.h:7-15` | l. 159 | **constructeur de trame** `0x0048d1b0` ; nom **absent** du binaire |
| **605** | `TM_CS_END_QUEST` | client → serveur | `librzu/src/packets/GameClient/TS_CS_END_QUEST.h:5-14` | l. 160 | **constructeur + envoi** `0x0048d6c0` ; nom **absent** du binaire |

Précisions de nomenclature :

- `op_codes.md:157` écrit `TM_SC_QUEST_INFORMATION` ; rzu et NGemity écrivent
  `TM_SC_QUEST_INFOMATION` (faute de frappe conservée par les deux références) et c'est cette seconde
  graphie que le client 7.3 porte en clair (`SFrame.exe`, chaîne à `VA 0x00a5325c`). Le nom à retenir
  pour le code est celui de `op_codes.md` si le dépôt le suit déjà, sinon la graphie des références :
  cette fiche utilise `TM_SC_QUEST_INFOMATION` (§8.7).
- Les identifiants `1600`-`1605` que rzu associe à `version >= EPIC_9_6_3` (`TS_CS_DROP_QUEST.h:9-10`
  et symétriques) sont **hors cible** : la cible est `0x070300` (§4).
- `reference/client73` n'est pas un dépôt git : il n'existe aucun SHA de client. Toutes les citations
  `SFrame.exe` renvoient au fichier local, empreinte
  `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, et à une **lecture
  statique** (`objdump -d -M intel`, tables du binaire lues octet par octet). Aucun script du client
  n'a été exécuté.

### Tailles — tableau de synthèse (détail et sources au §3)

| paquet | taille totale attendue | vérifiée par le client lui-même ? |
| --- | --- | --- |
| 600 `TM_SC_QUEST_LIST` | **11 + 61·N + 8·M** octets (`11` à vide) | oui : base du tableau à `frame+11`, pas d'élément `0x3d` = 61 (`0x00670cf4`, `0x00670dc0`) |
| 601 `TM_SC_QUEST_STATUS` | **40** octets, fixe | partiellement : `nProgress` lu à `frame+0x23` = 35 = 7+28 (`0x0067dba3`) |
| 602 `TM_SC_QUEST_INFOMATION` | **17** octets, fixe | non : paquet sans handler dans le client, forme de rzu reprise telle quelle (§3.6) |
| 603 `TM_CS_DROP_QUEST` | **11** octets, fixe | oui : le constructeur écrit `length = 0xb` (`0x0048cda4`) |
| 604 `TM_CS_QUEST_INFO` | **11** octets, fixe | oui : le constructeur écrit `length = 0xb` (`0x0048d1e4`) |
| 605 `TM_CS_END_QUEST` | **12** octets, fixe | oui : le constructeur écrit `length = 0xc` (`0x0048d6da`) |

## 2. Ce que le joueur fait pour que le client l'envoie

**603 — abandon d'une quête (preuve d'émission établie).** Le client possède un gestionnaire d'actions
d'interface (fonction couvrant `0x0049d030`-`0x0049e763`) qui compare l'action cliquée à une chaîne :

- `0x0049d04f` : `push 0x00a2025c` → chaîne `drop_quest` ; comparée à l'action de l'élément cliqué par
  `call 0x0048ef10` (`0x0049d055`) ; si l'action **n'est pas** `drop_quest`, saut à l'action suivante
  (`0x0049d092`, `req_summon_formation`) ;
- si c'est `drop_quest` : construction de la trame 603 (`0x0049d064` → `call 0x0048cd70`), remplissage
  du champ `code` par la valeur lue à `[edi+0x4b]` (la quête sélectionnée, `0x0049d06f`), écriture à
  `frame+7` (`0x0049d072`), puis envoi par la méthode de session `[[esi+0xb8]]+0xc4`
  (`0x0049d069`-`0x0049d08b`).

Le geste est donc le bouton d'abandon de la fenêtre de quête. Le bloc ne modifie **rien** localement
après l'envoi (retour immédiat au gestionnaire) : la liste affichée n'est corrigée que par une trame
descendante (§5.5, question 3).

**605 — fin de quête avec récompense optionnelle.** Le constructeur `0x0048d6c0` est appelé à
`0x0049d659` (même gestionnaire d'actions) ; il lit le `code` à `[arg+0x13]` et le champ
`nOptionalReward` à `[arg+0x17]` (`0x0048d6fd`, `0x0048d700`) et envoie immédiatement
(`0x0048d709`-`0x0048d715`). C'est le chemin « terminer une quête en choisissant une récompense
optionnelle ». Le libellé d'action exact de ce cas n'a pas été identifié (le bloc n'est pas gardé par
une des chaînes comparées dans ce gestionnaire) → §8.4.

**604 — interrogation d'une quête.** Le constructeur `0x0048d1b0` est appelé à `0x0049d46b` dans le
même gestionnaire ; le `code` vient de `[edi+0x13]` (`0x0049d476`) et part aussitôt
(`0x0049d469`-`0x0049d492`). Le geste précis (ouverture du détail d'une quête, rafraîchissement d'une
ligne) **n'est pas établi** : le bloc n'est protégé par aucune chaîne d'action identifiable dans la
région, et rien dans les ressources présentes ne le tranche → §8.4.

**600 / 601 / 602** sont descendants : aucun geste du joueur ne les déclenche.

## 3. Structure sur le fil

### 3.0 En-tête commun (7 octets) — base de tous les calculs de taille

| offset | type | nom | source |
| --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` (total, en-tête comprise) | dépôt `Game/Network/Packets/Header.cs:9,22` ; client : `0x0048cd78`/`0x0048cda4` (603) |
| 4 | `uint16` LE | `ID` (opcode) | `Header.cs:10,23` ; client : `0x0048cd9e` (écriture mot de l'id 603) |
| 6 | `uint8` | `Checksum` = somme des octets 0..5 | `Header.cs:11,24` ; client : boucle `0x0048cdb0`-`0x0048cdb7` |
| 7 | — | corps | `Game/Network/Packets/Game/GameActionPackets.cs:8` (`HeaderSize = 7`) |

L'id est écrit sur **2 octets** par le client lui-même (`mov WORD PTR [eax+0x4],dx` aux
`0x0048cd9e`/`0x0048d1de`), ce qui couvre 600-605 sans ambiguïté.

### 3.1 `TM_CS_DROP_QUEST` = 603 — abandon (client → serveur) — **11 octets**

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | **11** (`0xb`) | rzu `TS_CS_DROP_QUEST.h:6` ; client `0x0048cda4` |
| 4 | `uint16` LE | `ID` | **603** (`0x25b`) | `op_codes.md:158` ; client `0x0048cd99` |
| 6 | `uint8` | `Checksum` | — | client `0x0048cdb0`-`0x0048cdb7` |
| 7 | `int32` LE | `code` | identifiant de quête (celui de la quête sélectionnée) | rzu `TS_CS_DROP_QUEST.h:6` ; NGemity `TS_CS_DROP_QUEST.h:7` ; client `0x0049d06f` → `frame+7` |

Aucun autre champ : la trame est **fixe à 11 octets**. `code` est **signé** (`int32_t` chez rzu et
NGemity) : une valeur négative est représentable et doit être refusée comme telle, sans être
réinterprétée (même piège que `count` de 203, voir `docs/packet-specs/203-drop-item.md:71-72`).

### 3.2 `TM_CS_QUEST_INFO` = 604 — interrogation (client → serveur) — **11 octets**

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | **11** (`0xb`) | rzu `TS_CS_QUEST_INFO.h:8` ; client `0x0048d1e4` |
| 4 | `uint16` LE | `ID` | **604** (`0x25c`) | `op_codes.md:159` ; client `0x0048d1d9` |
| 6 | `uint8` | `Checksum` | — | client `0x0048d1f0`-`0x0048d1f7` |
| 7 | `int32` LE | `code` | identifiant de quête | rzu `TS_CS_QUEST_INFO.h:8` ; NGemity `TS_CS_QUEST_INFO.h:7` ; client (`à partir de` `[edi+0x13]`, `0x0049d476`) |

### 3.3 `TM_CS_END_QUEST` = 605 — fin de quête (client → serveur) — **12 octets**

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | **12** (`0xc`) | rzu `TS_CS_END_QUEST.h:6-7` ; client `0x0048d6da` |
| 4 | `uint16` LE | `ID` | **605** (`0x25d`) | `op_codes.md:160` ; client `0x0048d6d1` |
| 6 | `uint8` | `Checksum` | — | client `0x0048d6e7`-`0x0048d6fa` |
| 7 | `int32` LE | `code` | identifiant de quête | rzu `TS_CS_END_QUEST.h:6` ; NGemity `TS_CS_END_QUEST.h:7` ; client `0x0048d6fd` → `frame+7` |
| 11 | `int8` | `nOptionalReward` | index de la récompense optionnelle choisie | rzu `TS_CS_END_QUEST.h:7` ; NGemity `TS_CS_END_QUEST.h:8` ; client `0x0048d700` → `frame+11` |

Le client écrit le champ à `frame+11` (`[ebp-0x1]`, `0x0048d706`) après un `length` de `0xc` : la
présence du `int8` et l'absence de tout autre champ sont donc prouvées par le client, pas seulement par
rzu. Bornage : NGemity borne l'index par `MAX_OPTIONAL_REWARD = 3`
(`Chihiro/src/Quests/QuestBase.h:21`), mais **aucune borne n'est établie pour 7.3** → §8.6.

### 3.4 `TM_SC_QUEST_LIST` = 600 — liste des quêtes du joueur (serveur → client)

Structure des deux sous-structures (rzu `TS_SC_QUEST_LIST.h:7-23`, identiques champ par champ à
NGemity `TS_SC_QUEST_LIST.h:6-20`) :

`TS_QUEST_INFO` — **61 octets** (`0x3d`) :

| offset élément | type | nom | source |
| --- | --- | --- | --- |
| +0 | `uint32` LE | `code` | rzu `TS_SC_QUEST_LIST.h:8` ; client lit `[edi]` (`0x00670d2e`) |
| +4 | `uint32` LE | `startID` | rzu `:9` ; client lit `[edi+0x4]` (`0x00670d27`) |
| +8 | `uint32[6]` LE | `value` (24 o) | rzu `:10` ; client passe `edi+0x8` (`0x00670d30`) |
| +32 | `uint32[6]` LE | `status` (24 o) | rzu `:11-12` ; client passe `edi+0x20` (`0x00670d34`) |
| +56 | `uint8` | `progress` | rzu `:14` ; client lit l'octet `[edi+0x38]` (`0x00670d20`) |
| +57 | `uint32` LE (`ar_time_t`) | `timeLimit` | rzu `:15` ; client lit l'entier `[edi+0x39]` (`0x00670d24`) |

`ar_time_t` est un `strong_typedef` sur `uint32_t` (`librzu/src/lib/Types/GameTypes.h:44`) → **4
octets** ; NGemity déclare le même champ `uint32_t` (`TS_SC_QUEST_LIST.h:14`).

`TS_QUEST_PENDING` — **8 octets** : `code` `uint32` à +0, `startID` `uint32` à +4
(rzu `TS_SC_QUEST_LIST.h:19-23`).

Trame complète :

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | `11 + 61·N + 8·M` | rzu `:25-29` ; client (pas d'élément 61, base à +11) |
| 4 | `uint16` LE | `ID` | **600** (`0x258`) | `op_codes.md:155` ; nom en table client `0x00677e82` |
| 6 | `uint8` | `Checksum` | — | `Header.cs:11,24` |
| 7 | `uint16` LE | `activeQuests` (compte `N`) | nombre d'entrées actives | rzu `:26` ; client `movzx edx,WORD PTR [ecx+0x7]` (`0x00670dbb`) |
| 9 | `uint16` LE | `pendingQuests` (compte `M`) | `0` (voir §4 et §7) | rzu `:27` ; client : **présent**, le tableau commence à +11 |
| 11 | `TS_QUEST_INFO[N]` | `activeQuests` | chaque entrée fait 61 octets | client `lea edi,[esi+0xb]` (`0x00670cf4`) et `add edi,0x3d` (`0x00670dc0`) |
| 11+61·N | `TS_QUEST_PENDING[M]` | `pendingQuests` | 8 octets par entrée | rzu `:29` |

**Taille** : `11 + 61·N + 8·M`. À vide (**11 octets**), la trame est parfaitement formée.

Le client prouve deux choses que rzu seul n'aurait pas tranchées :

- le **compte des `pendingQuests` occupe bien 2 octets** avant le tableau : le handler lit le tableau
  à `frame+0xb` (`0x00670cf4`), pas à `frame+9`. Un serveur qui omettrait ce second compte décalerait
  tout le tableau de 2 octets ;
- l'élément `TS_QUEST_INFO` fait **exactement 61 octets** : l'avancement de boucle est
  `add edi,0x3d` (`0x00670dc0`), et la borne de boucle est le seul compte `activeQuests`
  (`0x00670d0d`, `0x00670dbb`).

### 3.5 `TM_SC_QUEST_STATUS` = 601 — état d'une quête (serveur → client) — **40 octets**

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 7 | `int32` LE | `code` | identifiant de quête | rzu `TS_SC_QUEST_STATUS.h:8` ; client lit `[esi+0x7]` (`0x0067db9d`) |
| 11 | `uint32[6]` LE | `status` (24 o) | six entiers | rzu `:9-11` ; client lit `[esi+0xb]` … `[esi+0x1f]` (`0x0067dba9`-`0x0067dbc7`) |
| 35 | `int8` | `nProgress` | valeur d'état (voir §7, question de politique) | rzu `:12` ; client lit l'octet `[esi+0x23]` (`0x0067dba3`) |
| 36 | `uint32` LE (`ar_time_t`) | `nTimeLimit` | `0` par défaut côté référence | rzu `:13` ; NGemity `TS_SC_QUEST_STATUS.h:12` |

`code` est `int32_t` dans les deux références, alors que le `code` de `TS_QUEST_INFO` (600) est
`uint32_t` : c'est le même mot de 4 octets, la différence de signe n'a d'effet qu'au-delà de `2^31`,
hors du domaine des identifiants de quête.

Point relevé et **non contourné** : le client 7.3 ne lit **pas** `nTimeLimit` dans ce handler (aucune
lecture de `[esi+0x24]`), alors qu'il lit bien `nProgress`. Le champ existe pourtant en 7.3 (§4) : la
trame reste à 40 octets, et le fait que le client l'ignore est une tolérance, pas une dispense.

### 3.6 `TM_SC_QUEST_INFOMATION` = 602 — (serveur → client) — **17 octets, à ne pas envoyer**

| offset | type | nom | source |
| --- | --- | --- | --- |
| 7 | `int32` LE | `code` | rzu `TS_SC_QUEST_INFOMATION.h:14` |
| 11 | `int32` LE | `nProgress` (type `TS_QUEST_PROGRESS`) | rzu `:5-10,15` ; NGemity `:6-11,16` |
| 15 | `uint16` LE | `trigger_length` | rzu `:16` |

`TS_QUEST_PROGRESS` : `IS_STARTABLE = 0`, `IS_IN_PROGRESS = 1`, `IS_FINISHABLE = 2`
(rzu `TS_SC_QUEST_INFOMATION.h:5-10`).

Deux réserves, qui justifient la décision de §5.6 :

1. rzu lui-même note le paquet « Seems unused » (`:12`) ;
2. la structure déclarée porte un **compte** (`trigger_length`) sans aucun tableau derrière : la forme
   transmise par les deux références est probablement incomplète, et rien dans les ressources 7.3
   présentes ne permet de la compléter. La « taille 17 » n'est donc qu'une somme de champs déclarés,
   pas une taille validée.

## 4. Gating de version — chaque champ est tranché pour Epic 7.3

Valeurs de référence : `EPIC_6_1 = 0x060100`, `EPIC_6_3 = 0x060300`, `EPIC_7_3 = 0x070300`,
`EPIC_9_6_3 = 0x090603` (`librzu/src/lib/Packet/PacketEpics.h:54,56,59,96`). La cible du serveur est
`0x070300`.

| champ / id | gating montré par rzu | décision pour 7.3 | source |
| --- | --- | --- | --- |
| id 600 (`1600` au-delà) | `X(600, version < EPIC_9_6_3)` | **600** (`0x070300 < 0x090603`) | `TS_SC_QUEST_LIST.h:31-33` |
| id 601 | idem | **601** | `TS_SC_QUEST_STATUS.h:15-17` |
| id 602 | idem | **602** | `TS_SC_QUEST_INFOMATION.h:18-20` |
| id 603 | idem | **603** | `TS_CS_DROP_QUEST.h:8-10` |
| id 604 | idem | **604** | `TS_CS_QUEST_INFO.h:11-13` |
| id 605 | idem | **605** | `TS_CS_END_QUEST.h:10-12` |
| `pendingQuests` (compte **et** tableau) de 600 | `version >= EPIC_6_3` | **présent** (2 octets de compte + `M` entrées de 8 o) — prouvé par le client : tableau à `frame+11` | `TS_SC_QUEST_LIST.h:27,29` ; client `0x00670cf4` |
| `status` de `TS_QUEST_INFO` | `6` si `version >= EPIC_6_1`, `3` sinon | **6 entiers de 32 bits (24 octets)** | `TS_SC_QUEST_LIST.h:11-12` ; client lit `edi+0x20` sur 24 octets |
| `progress` (u8) de `TS_QUEST_INFO` | `version >= EPIC_6_3` | **présent, 1 octet à l'offset élément +56** | `TS_SC_QUEST_LIST.h:14` ; client lit `[edi+0x38]` |
| `timeLimit` de `TS_QUEST_INFO` | `version >= EPIC_6_3` | **présent, 4 octets à +57** (`ar_time_t` = `uint32`) | `TS_SC_QUEST_LIST.h:15` ; `GameTypes.h:44` ; client lit `[edi+0x39]` |
| `status` de 601 | `6` si `version >= EPIC_6_1` | **6 entiers de 32 bits** | `TS_SC_QUEST_STATUS.h:9-11` ; client lit six mots à `[esi+0xb]`..`[esi+0x1f]` |
| `nProgress` de 601 | `version >= EPIC_6_3` | **présent, 1 octet à l'offset 35** | `TS_SC_QUEST_STATUS.h:12` ; client lit l'octet `[esi+0x23]` |
| `nTimeLimit` de 601 | `version >= EPIC_6_3` | **présent, 4 octets aux offsets 36-39** (le client ne le lit pas) | `TS_SC_QUEST_STATUS.h:13` |
| aucune version 7.3 n'est en cause pour 603/604/605 | pas de champ gaté | trame fixe 11/11/12 | §3.1-3.3 |

Le `+1000` d'`EPIC_9_6_3` (600 → 1600, etc.) n'affecte **aucun** des six paquets en 7.3 ; il n'est cité
que pour expliquer la double notation de rzu.

## 5. Traitement attendu

### 5.1 Ce que NGemity en fait

NGemity compile en **`EPIC_4_1_1`** (`shared/Common/Define.h:25`) : il est plus ancien que 7.3 et sa
logique vaut sous réserve des écarts du §6. Il n'y a, côté NGemity, **aucun handler pour 604 ni 605**
(`grep` sur `Chihiro/src/Network/GameNetwork/WorldSession.*` : seuls 603 et les paquets voisins sont
déclarés). Le seul paquet montant traité est 603 :

- déclaration : `declareHandler(STATUS_AUTHED, &WorldSession::onDropQuest)`
  (`Chihiro/src/Network/GameNetwork/WorldSession.cpp:135`, signature
  `WorldSession.h:107`) ;
- corps (`WorldSession.cpp:2033-2043`) : si le joueur existe,
  `m_pPlayer->DropQuest(code)` → `Messages::SendResult(m_pPlayer, pRecvPct->getReceivedId(),
  TS_RESULT_SUCCESS, 0)` ; sinon `TS_RESULT_NOT_ACTABLE, 0` ;
- `Player::DropQuest` (`Chihiro/src/Entities/Player/Player.cpp:3173-3187`) :
  `m_QuestManager.FindQuest(code)` → si absent, `return false` (**seule condition d'éligibilité**) ;
  sinon `CHARACTER_DEL_QUEST` (suppression de la ligne du personnage), `onDropQuest(q)`,
  `Messages::SendQuestList(this)` ;
- `Player::onDropQuest` (`Player.cpp:3189-3200`) : pour une quête `QUEST_PARAMETER`, retrait de
  « chaos » selon des valeurs de quête (`GetValue(i) == 99`, `GetValue(i+1) == 1`,
  `AddChaos(-GetValue(i+2))`) ; puis `m_QuestManager.PopFromActiveQuest(pQuest)` (retrait de la liste
  en mémoire, `QuestManager.cpp:497-503`) et `Messages::SendNPCStatusInVisibleRange(this)` ;
- `QuestManager::FindQuest` (`QuestManager.cpp:253-260`) cherche dans **la liste active du joueur**,
  pas dans un catalogue de quêtes.

Descendants utiles comme modèle :

- `Messages::SendQuestList` (`Messages.cpp:722-755`) : parcourt les quêtes actives
  (`Player::DoEachActiveQuest`), remplit `activeQuests` — `code`, `startID`, `value[0..5]` (six valeurs
  de la définition de quête, ou couples clé/valeur pour les quêtes aléatoires), `status[0..2]` — et
  **ne remplit ni `progress` ni `timeLimit`** (initialisation à zéro) ; `pendingQuests` reste vide.
  Il **n'envoie qu'à un seul joueur** et ne remet pas la liste à zéro.
- `Messages::SendQuestStatus` (`Messages.cpp:882-890`) : `code` + `status[i]` pour
  `i < MAX_QUEST_STATUS` (= 3, `QuestBase.h:24`) ; `nProgress` et `nTimeLimit` laissés à zéro.
- `Messages::SendQuestInformation` (`Messages.cpp:622+`) : seul et unique appelant
  `Chihiro/src/Scripting/XLua.cpp:837` (script de quête), avec des types d'interface 3/7/8 calculés
  depuis un contact PNJ (`Player::GetLastContactLong("npc")`).
- Les acquittements passent tous par `TS_SC_RESULT` (`shared/Server/TS_MESSAGE.h:55,60` :
  `TS_RESULT_SUCCESS = 0`, `TS_RESULT_NOT_ACTABLE = 5`).

### 5.2 Ce que le serveur doit répondre

| reçu | réponse attendue | source |
| --- | --- | --- |
| 603 `code` | `TM_SC_RESULT` (0) avec `RequestMsgID = 603`, `Result = Success (0)`, `Value = 0` si la quête du joueur a été retirée ; `Result = NotActable (5)` sinon — puis **`TM_SC_QUEST_LIST` (600)** pour que la liste affichée reflète le retrait | NGemity `WorldSession.cpp:2039,2042` et `Player.cpp:3185` ; `TS_SC_RESULT` : rzu `TS_SC_RESULT.h:7-14`, dépôt `Game/Network/Packets/Game/TS_SC_RESULT.cs` (15 octets : 7 + `RequestMsgID` u16 + `Result` u16 + `Value` i32) ; codes : dépôt `Game/Network/Packets/ResultCode.cs:6,12` |
| 604 `code` | aucune réponse établie (voir §5.6) — la seule réponse plausible serait 602, que le client 7.3 ne traite pas | §5.4, §8.5 |
| 605 `code`, `nOptionalReward` | hors du socle : exige le catalogue de quêtes et la politique de récompenses (§5.6) | NGemity `Player.cpp:2333-2418` |

`TM_SC_RESULT` est déjà disponible dans le dépôt et utilisé pour les acquittements
(`GameClient.cs:432`, `GameClient.cs:767`) : le socle n'a pas de paquet d'acquittement à inventer.

### 5.3 Ce que le dépôt porte déjà

| élément | état | source |
| --- | --- | --- |
| bande 600-699 dans `GamePackets` | **absente** (aucun membre 600-605, aucun 600-699) | `Game/Network/Packets/Enums/GamePackets.cs` (82 membres) |
| réception | chaîne de `if (header.ID == …) continue;` puis `IPacket msg = header.ID switch { …, _ => throw new Exception("Unknown Packet Type") }` | `Game/Network/Clients/GameClient.cs:690-694` (modèle 203) et `:791-803` (switch final) |
| convention « membres descendants déclarés sans bras de réception » | déjà établie pour 202/205/207/209/210 | `Tests/Game/DropItemPacketsTests.cs:100-109` |
| conditions de limite | `QUEST_STATUS` existe déjà comme condition de carte | `Game/Maps/Enums/LimitCondition.cs:6` |
| fenêtre de dialogue PNJ | `TM_CS_CONTACT` / `TM_CS_DIALOG` + `NpcDialogService` existent ; la fenêtre de quête spécialisée, non | `GameClient.cs:744-754` ; `docs/npc-dialogs.md:62-64` |
| entités de données de quête | **aucune** : ni `QuestResource`, ni `QuestStringResourse`, ni `QuestLinkResource` | `Game/DataAccess/Entities/Arcadia/` (19 entités, aucune « Quest ») ; tables au schéma : `ArcadiaSchemaPSQL.sql:906`, `:1234`, `:1789` |
| table par personnage | il n'en existe aucune pour les quêtes ; le contexte des données par personnage est `Telecaster` | `Game/DataAccess/Entities/Telecaster/` (`CharacterSkillEntity.cs` comme modèle) ; `Game/DataAccess/Migrations/Telecaster/20260714164541_Version0007_CharacterSkills.cs` |
| tests | 448 passés, 0 échec sur `master` `ec76b21` | relevé pendant cette fiche (`dotnet test Tests/Tests.csproj`, code de sortie 0) |

### 5.4 Le client 7.3 : ce qui est prouvé sur trois niveaux indépendants

Le client est la référence qui tranche. Pour ces six opcodes, trois niveaux concordent :

1. **table de noms** (`name → id`, remplie par une chaîne d'insertions observée entre
   `0x00677d5c` et `0x006780ce`) : 600 `TM_SC_QUEST_LIST` (`0x00677e82`, chaîne `VA 0x00a53288`),
   601 `TM_SC_QUEST_STATUS` (`0x00677ee4`, `0x00a53274`), 602 `TM_SC_QUEST_INFOMATION`
   (`0x00677f46`, `0x00a5325c`), 603 `TM_CS_DROP_QUEST` (`0x00677fa8`, `0x00a53248`). Les chaînes
   `TM_CS_QUEST_INFO` et `TM_CS_END_QUEST` sont **absentes du binaire** (`strings` : 0 occurrence).
   La table de noms est une annotation, pas une preuve de traitement (`docs/packet-specs/203-drop-item.md`
   a déjà relevé cette limite).
2. **tables de dispatch des trames reçues** (`0x0067e1d9`) : table d'octets `0x0067f35c` (205 entrées,
   505-710), table de sauts `0x0067f310`, défaut `0x0067ef21`. Résultat pour la bande :
   **600 → index 8 → stub `0x0067e425` → handler `0x00670cd0`** et
   **601 → index 9 → stub `0x0067e432` → handler `0x0067db20`** ; **602, 603, 604, 605 → index 18 →
   `0x0067ef21`, c'est-à-dire « non traité »**. Autrement dit : le client 7.3 traite 600 et 601 et
   **ignore 602** (cession croisée avec la note « Seems unused » de rzu, §3.6).
3. **constructeurs de trames montantes** : 603 à `0x0048cd70` (id `0x25b` en `0x0048cd99`, longueur
   `0xb` en `0x0048cda4`), 604 à `0x0048d1b0` (id `0x25c` en `0x0048d1d9`, longueur `0xb` en
   `0x0048d1e4`), 605 à `0x0048d6c0` (id `0x25d` en `0x0048d6d1`, longueur `0xc` en `0x0048d6da`,
   remplissage `frame+7`/`frame+11`, envoi immédiat). Les trois ont un appelant identifié
   (`0x0049d064`, `0x0049d46b`, `0x0049d659`, §2).

### 5.5 Question 3 de la carte — quelle réponse descendante après un abandon ?

**Réponse : un acquittement `TM_SC_RESULT` (0) taggé 603, puis `TM_SC_QUEST_LIST` (600). 601 n'est pas
la bonne trame pour un retrait.**

Ce qui est prouvé :

- après l'envoi de 603, le client **ne modifie rien localement** : le bloc `drop_quest` envoie et
  retourne au gestionnaire (`0x0049d08d` → `jmp 0x0049e763`). La disparition de la quête à l'écran ne
  peut donc venir que du serveur ;
- le handler de 600 remet à zéro le conteneur de quêtes avant de le reconstruire
  (`call 0x004c1720` en `0x00670cfe`, avant la boucle ; cette fonction compacte deux vecteurs
  d'éléments de 64 octets, c'est-à-dire vide les listes), puis ajoute chaque entrée :
  600 est donc une **resynchronisation complète**, par construction ;
- le handler de 601 (`0x0067db20`) écrit `code`, les six `status` et `nProgress` dans l'objet de quête
  connu du client (`0x0067dba0`-`0x0067dbb8`), puis l'enregistre (`call 0x0064d0e0` en `0x0067dc3a`) :
  il **met à jour** une quête, il n'en **retire** aucune ; il exige en outre que la quête soit connue
  de la base de quêtes locale pour produire son libellé (`0x0067dbcd`-`0x0067dc08`).

Ce qui reste non établi : « 601 seule suffirait-elle ? ». Aucun élément du binaire ne montre un
retrait par 601 → **NON ÉTABLI** (§8.3). La recommandation de la fiche (600 + `TM_SC_RESULT`) est la
seule forme dont le comportement client est prouvé.

### 5.6 Ce que le socle retient et ce qu'il exclut

**Retenu (structure, sans politique de jeu) :**

1. énumération et réception de **603** : lire `code` (11 octets, trame tronquée refusée), refuser une
   valeur négative, chercher la quête **dans l'état du joueur**, la retirer, acquitter par
   `TM_SC_RESULT` taggé 603 ;
2. émission de **600** (liste complète, `11 + 61·N + 8·M`) et de **601** (`40` octets), avec le
   second compte `pendingQuests = 0` et la trame construite par le serveur, jamais par le client ;
3. le **modèle** de l'état de quête d'un personnage, tel que la trame l'impose :
   `code`, `start_id`, six `value`, six `status`, `progress`, `time_limit` — les six `status` pris
   comme six entiers **opaques** (leur sémantique au-delà des trois premiers est inconnue en 7.3, §6) ;
4. sa **persistance** : table par personnage dans le contexte `Telecaster`, migration sur le modèle
   `Version0007_CharacterSkills`, suppression par personnage + `code` (l'équivalent de
   `CHARACTER_DEL_QUEST`) ;
5. les **tests d'offsets** : 603 (11 o), 600 (11 o à vide, `11 + 61` pour une entrée),
   601 (40 o), plus le contrôle `Enum.IsDefined` sur les ids, selon la discipline de
   `Tests/Game/DropItemPacketsTests.cs`.

**Exclu, et pourquoi :**

| exclu | raison |
| --- | --- |
| **604** `TM_CS_QUEST_INFO` | aucune réponse n'est établie (§5.2, §8.5) : la seule réponse plausible (602) est un paquet que le client 7.3 **n'a pas de handler**. Implémenter une réception sans réponse établie serait inventer une politique |
| **605** `TM_CS_END_QUEST` | la fin de quête exige le catalogue de quêtes (récompenses, or, exp, scripts) : c'est de la politique de jeu, et le catalogue n'existe pas dans le dépôt (§5.3). Carte parquée `BAMvMQ7u` |
| **602** | le client ne le traite pas et rzu le dit « Seems unused » (§3.6) : envoyer 602 est inopérant |
| acceptation et progression des quêtes (déclencheurs PNJ) | question 4 : **hors du socle** (§7). Le déclencheur de fenêtre de quête et le suivi de contact PNJ ne sont pas résolus, et l'acceptation exige le catalogue + les scripts de quête |
| `QuestResource` / `QuestStringResourse` / `QuestLinkResource` | inutiles au socle : côté NGemity, `DropQuest` ne consulte que la liste active du joueur (`QuestManager.cpp:253-260`). Ces tables restent des prérequis des cartes 604/605, pas du retrait |
| effets du retrait (chaos, notifications de statut PNJ) | politique de jeu (§8.1, §8.2) |

**Conséquence de périmètre à écrire dans la MR :** le socle n'a **aucun chemin d'écriture qui crée une
quête** (l'acceptation est exclue). En vie réelle, un client ne pourra donc obtenir que
`NotActable (5)` sur 603 tant que l'acceptation n'est pas livrée ; l'état testable est celui que le
socle sait lire et exposer (600/601) et que les tests alimentent directement. Ce n'est pas un défaut de
la fiche, c'est la conséquence assumée de la question 4.

## 6. Écarts assumés avec NGemity, et pourquoi

NGemity compile en `EPIC_4_1_1` (`shared/Common/Define.h:25`) ; les écarts ci-dessous ne sont pas des
erreurs de NGemity, ce sont les différences entre sa version et 7.3.

| # | écart | rzu / client 7.3 | NGemity | décision |
| --- | --- | --- | --- | --- |
| 1 | nombre d'états de quête sur le fil | **6** `uint32` (`EPIC_6_1+`) | 3 (`MAX_QUEST_STATUS = 3`, `QuestBase.h:24`) ; `SendQuestStatus` n'en remplit que 3 (`Messages.cpp:886-888`) | en 7.3 : **6 mots sur le fil**, les trois derniers alimentés par défaut à `0` — ne pas suivre NGemity ici |
| 2 | `progress` et `timeLimit` de `TS_QUEST_INFO` | **présents** (`EPIC_6_3+`) | absents de la structure à 4.1.1 ; jamais remplis | en 7.3 : **présents** ; valeurs = question de politique (§7, §8.6) |
| 3 | `nTimeLimit` de 601 | présent, 4 octets | présent dans sa structure 4.1.1 mais non gaté, jamais rempli | en 7.3 : **présent** ; le client 7.3 ne le lit pas → `0` par défaut, à documenter |
| 4 | valeur « quête terminable » | rzu : `IS_FINISHABLE = 2` (enum de 602) | `QUEST_IS_FINISHABLE = 255` (`QuestBase.h:46`) et `SetProgress(QUEST_IS_FINISHABLE)` (`QuestManager.cpp:247`) | divergence **non tranchable localement** sur la valeur de `nProgress` en 7.3 → §8.6 |
| 5 | handlers montants | 603 (et 604/605 attendus par le client pour d'autres usages) | **603 seulement** ; rien pour 604/605 (`WorldSession.cpp:135` et alentours) | le socle suit NGemity pour 603 et s'arrête là |
| 6 | réponse à 603 | 600 (`SendQuestList`) + `TS_SC_RESULT` | identique (`Player.cpp:3185`, `WorldSession.cpp:2039`) | pas d'écart : c'est exactement ce que la fiche retient |
| 7 | effets du retrait | aucun effet établi côté client au-delà du rafraîchissement | retrait de « chaos » pour les quêtes `QUEST_PARAMETER`, notification de statut PNJ | non porté : politique de jeu (§8.1, §8.2) |
| 8 | éligibilité à l'abandon | non établie | seule condition : la quête est dans la liste active (`Player.cpp:3173-3177`) | le socle suit NGemity (aucune autre condition inventée) — voir §8.1 |
| 9 | 602 | rzu « Seems unused » ; client sans handler | émis uniquement depuis un script Lua (`XLua.cpp:837`) | le socle n'émet pas 602 |
| 10 | `pendingQuests` de 600 | compte présent + tableau de 8 octets par entrée | vide en permanence | le socle émet le compte à `0` ; les entrées non nulles sont **non établies** (§8.7) |

## 7. Décisions de périmètre — les quatre questions de la carte

**Question 1 — quel sous-ensemble est purement structurel ?**
Structurel : lire la trame 603, retrouver la quête du joueur par `code`, retirer son état, acquitter par
`TM_SC_RESULT` (0), puis savoir construire 600 et 601 avec les seuls octets que la trame impose.
Politique de jeu (→ `## A VERIFIER PAR KILLIAN`) : les **conditions** d'abandon (aucune n'est établie,
§8.1), un éventuel cooldown (aucune colonne ni champ de trame ne le porte ; `cool_time` de
`QuestResource` est une colonne de catalogue, non un champ de ces trames, §5.3), les **effets** du
retrait (chaos, notifications, §8.1-8.2), la valeur de `progress`/`nProgress` (§8.6), la signification
des `status[3..5]` (§8.8) et la forme de 602/604/605.

**Question 2 — le socle se scinde-t-il ?**
**Oui, en deux, et c'est le PO qui scinde la carte Trello.** (a) Le socle « état + retrait » de cette
fiche (modèle, persistance, 600/601, 603, dispatch, tests) ; (b) le « cycle d'acceptation et de
progression » (déclencheur de fenêtre de quête par contact PNJ, catalogue `QuestResource`, scripts,
puis 604/602 et 605). La raison du découpage est mécanique : (b) ne peut pas être écrit sans le
catalogue de quêtes ni les scripts, alors que (a) n'en dépend pas — NGemity lui-même ne consulte que la
liste active du joueur pour le retrait. Les trois cartes parquées (`603` `SvpqMqO5`, `604` `RjcBclVP`,
`605` `BAMvMQ7u`) restent donc parquées, à l'exception du sous-ensemble 603 décrit ci-dessus.

**Question 3 — quelle réponse descendante après un abandon ?**
`TM_SC_RESULT` (0) taggé 603 **puis** `TM_SC_QUEST_LIST` (600). 601 n'est pas la trame d'un retrait
(preuves au §5.5). Le point « 601 seule suffirait-elle ? » est **NON ÉTABLI** (§8.3).

**Question 4 — le socle inclut-il l'acceptation et la progression ?**
**Non.** Le socle s'arrête à l'état (600/601) et au retrait (603). Motifs : le déclencheur de fenêtre de
quête n'existe pas dans le dépôt (`docs/npc-dialogs.md:63` ; aucune notion de dernier contact PNJ dans
`Game/`), le catalogue de quêtes n'a ni entité ni chargement (§5.3), et l'acceptation dépend de scripts
de quête (Lua) qui sortent du champ du serveur C#. Conséquence assumée : voir la fin de §5.6.

## 8. NON ÉTABLI — questions ouvertes, à trancher

Les points ci-dessous **ne doivent pas être devinés** par l'implémentation. Aucun n'est nécessaire pour
livrer le sous-ensemble du §5.6 ; chacun est à porter dans `## A VERIFIER PAR KILLIAN`.

1. **Conditions d'abandon.** NGemity n'en connaît qu'une : la quête est présente dans la liste active
   du joueur (`Player.cpp:3173-3177`). Aucune autre condition n'est lisible localement : ni drapeau
   « abandonnable », ni cooldown, ni exclusion par type de quête. Question : le serveur doit-il
   refuser l'abandon de certaines quêtes (principale, événement, quête répétable, quête
   `is_auto_quest` — colonne de `ArcadiaSchemaPSQL.sql:1789+` dont la sémantique n'est pas établie) ?
   La fiche retient « suivre NGemity » ; tout autre choix est une décision de jeu.
2. **Effets du retrait.** NGemity retire du « chaos » pour les quêtes `QUEST_PARAMETER`
   (`Player.cpp:3189-3197`, valeurs `99`/`1` lues dans `value[]`) et diffuse le statut PNJ
   (`SendNPCStatusInVisibleRange`). Aucun de ces deux mécanismes n'existe dans le dépôt (pas de
   « chaos », pas de statut PNJ) : faut-il les porter, et plus tard, ou jamais ?
3. **`TM_SC_QUEST_STATUS` (601) seule suffirait-elle ?** §5.5 : le client remet sa liste à zéro sur
   600, met à jour sur 601, et rien dans le binaire ne montre un retrait par 601. Question : un
   serveur qui n'enverrait que 601 (sans 600) est-il conforme au client 7.3 ? NON ÉTABLI ; le socle
   envoie 600.
4. **Geste exact de 604 et 605.** Les deux constructeurs existent et sont appelés (604 en
   `0x0049d46b`, 605 en `0x0049d659`), mais le libellé d'action de ces blocs n'a pas été identifié
   (`drop_quest` l'a été pour 603 en `0x0049d04f`). Question : quel geste exact ouvre 604, et quel
   bouton émet 605 avec `nOptionalReward` ? À défaut, seule l'émission est établie, pas le parcours
   utilisateur.
5. **Réponse à 604.** Aucune n'est établie. L'hypothèse (604 demande l'état d'une quête → 602 le rend)
   est plausible — `TS_QUEST_PROGRESS` (`IS_STARTABLE`/`IS_IN_PROGRESS`/`IS_FINISHABLE`) décrit
   exactement les trois états d'un bouton de fenêtre de quête — mais 602 **n'a aucun handler** dans le
   client 7.3 (§5.4) et rzu le note « Seems unused ». Question : le serveur 7.3 d'origine répondait-il
   à 604, et par quoi ?
6. **Valeurs de `progress` (600) et `nProgress` (601), et borne de `nOptionalReward` (605).**
   Divergence nette : rzu porte `IS_FINISHABLE = 2` pour 602, NGemity utilise `255`
   (`QuestBase.h:46`) ; NGemity ne remplit jamais `progress`. Le client lit ces octets et les
   stocke (`0x0067dba3`, `0x00670d20`) mais l'usage qu'il en fait n'est pas exploitable en lecture
   seule. Idem pour l'index de récompense optionnelle : NGemity borne à `MAX_OPTIONAL_REWARD = 3`
   (`QuestBase.h:21`), ce qui est sa constante interne, pas une preuve 7.3. Question : quelles valeurs
   pour 7.3 ? La fiche recommande `progress`/`nProgress = 1` (quête en cours) et `nTimeLimit = 0`,
   **explicitement sans preuve** — à confirmer ou à remplacer.
7. **Nom de 602 et `pendingQuests`.** (a) `op_codes.md:157` écrit `TM_SC_QUEST_INFORMATION` ; rzu,
   NGemity et le client écrivent `TM_SC_QUEST_INFOMATION`. (b) Les entrées `pendingQuests` de 600
   (8 octets chacune) ne sont jamais parcourues par le handler 7.3 : le compte doit être là
   (prouvé), les entrées non nulles ne sont **pas établies**. Question : le serveur 7.3 d'origine
   remplissait-il `pendingQuests`, et pour quel usage ?
8. **Signification de `status[3]`, `status[4]`, `status[5]`.** Six mots en 7.3, trois seulement dans
   le modèle de NGemity (`MAX_QUEST_STATUS = 3`). Le client les copie dans son objet de quête
   (`0x0067dba9`-`0x0067dbc7`) sans que la lecture seule révèle à quoi ils servent. Le socle les
   transporte **opaque** (`0`) ; toute sémantique est une question de jeu.
9. **Forme de 602.** Le `trigger_length` déclaré n'a aucun tableau derrière (§3.6) : la structure
   transmise est probablement tronquée. Tant que 602 n'est pas émis, le point est sans effet.
10. **Données de quête.** `db_quest.rdb` (248 692 octets, `sha256
    70a06f1abaf61fe42b612babbddba5c3ae66e8a685c008b72e72f8445c5b8ac1`) et `db_queststring.rdb`
    (246 852 octets, `sha256 23d04b251b6e7ccfb4c064fa86e8f6566eb1722b969529a3294ad811c51e7a6f`)
    sont présents dans `reference/client73/`, mais **aucun .nfe/.nfa ni donnée de carte** n'y figure :
    toute question qui dépend du contenu réel des ressources 7.3 (codes de quête valides, chaînes,
    valeurs de `cool_time`) reste **NON ÉTABLI** et n'a pas été supposée ici. La forme binaire de
    `db_quest.rdb` n'a pas été décodée : elle demanderait un travail séparé, et les `.rdb` disponibles
    portent un en-tête de réécriture tiers qui n'authentifie pas leur contenu comme celui du client
    retail (limite déjà signalée dans `CLAUDE.md` pour les ressources).
11. **Index de l'assemblage `Telecaster`.** Aucune migration de quête n'existe ; la fiche propose le
    contexte `Telecaster`, une table par personnage et une migration sur le modèle de
    `Version0007_CharacterSkills`, mais le nom exact de la table et de la migration relève de la
    convention du dev. Ce n'est pas une incertitude de protocole.

## 9. Commits épinglés

| référence | commit | autorité |
| --- | --- | --- |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | tailles, ordre des champs, gating de version |
| NGemity / Chihiro | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | logique, sous réserve de sa version (`EPIC_4_1_1`) |
| dépôt `Navislamia` | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (`master`) | état du code et des tests (448 passés, 0 échec) |
| client 7.3 | `reference/client73/SFrame.exe`, `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | résolution côté client — dossier sans dépôt git : aucune référence de commit n'existe, c'est la méthode de lecture statique qui est citée |

## Annexe — bloc destiné à `CLAUDE.md` (proposition)

Ce bloc est à porter par la **description de la MR** : `CLAUDE.md` est un fichier d'instructions
protégé, `navis-ref` et `navis-dev` ne l'écrivent pas.

```
## Quêtes — socle 7.3 (600/601/603)

- 603 `TM_CS_DROP_QUEST` : 11 octets, `code` int32 à l'offset 7, signé (refuser < 0). Réponse :
  `TM_SC_RESULT` (0) taggé 603 (`Success` 0 / `NotActable` 5) **puis** `TM_SC_QUEST_LIST` (600).
- 600 `TM_SC_QUEST_LIST` : `11 + 61·N + 8·M` octets. Deux comptes u16 obligatoires (actives à 7,
  en attente à 9) — le client 7.3 lit le tableau à l'offset 11 et avance de 61 octets par entrée
  (`SFrame.exe 0x00670cf4`, `0x00670dc0`). Une entrée `TS_QUEST_INFO` : code u32, startID u32,
  value[6], status[6], progress u8, timeLimit u32.
- 601 `TM_SC_QUEST_STATUS` : 40 octets, six `status` u32, `nProgress` int8 à 35, `nTimeLimit` u32 à
  36 (que le client ne lit pas).
- 602 `TM_SC_QUEST_INFOMATION` : le client 7.3 **n'a pas de handler** pour 602 (dispatch
  `0x0067e1d9`, défaut `0x0067ef21`) : ne pas l'envoyer.
- 604 et 605 : le client les émet, le serveur ne les traite pas encore (catalogues et politique de
  récompenses requis).
- Détail, sources et réserves : `docs/packet-specs/socle-quetes.md`.
```
