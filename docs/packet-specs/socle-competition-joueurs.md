# Socle « compétition entre joueurs (duel) » — `TM_CS/SC_COMPETE_*` (4500-4506)

Sept opcodes, une seule famille, **aucun handler** dans les deux références serveur :

| Source | Ce qu'elle apporte | Commit épinglé |
|---|---|---|
| `/srv/navislamia/reference/rzu` | format, tailles, ordre des champs, gating par version | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` |
| `/srv/navislamia/reference/ngemity` | logique (version plus récente) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` |
| `/srv/navislamia/reference/client73` | client Epic 7.3, tranche les divergences. **Pas un dépôt git** : pas de commit, seulement le fichier de ressource `SFrame.exe`, 9 841 664 octets, empreinte de fichier sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, et la méthode de lecture statique décrite en §8 | — |
| `/srv/navislamia/Navislamia` | état du serveur | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` |

Méthode : **lecture statique** uniquement. Le binaire du client a été lu par `objdump -d`, par lecture
directe de `.rdata`/`.data` (mapping des sections PE) et par la chaîne RTTI MSVC
(`vtable-4 → COL → type descriptor → nom`). **Aucune exécution** : ni `SFrame.exe`, ni Lua, ni script du
client. État du dépôt au moment de la fiche : `dotnet build Navislamia.sln -c Debug` → code 0,
`dotnet test Tests/Tests.csproj` → code 0, **448 tests passés**, aucun fichier de code touché par cette
fiche (elle ajoute un seul fichier Markdown).

Cette fiche est le prérequis des deux cartes parkées en `THINKING` :
`TM_CS_COMPETE_REQUEST` (4500, carte `k7wZP5b0`) et `TM_CS_COMPETE_ANSWER` (4502, carte `gRidt12E`).

---

## Questions tranchées par cette fiche

| # | Décision | § |
|---|---|---|
| D1 | Les sept ids `4500`…`4506` sont **identiques en 7.3** : `X(<id>, true)` n'exprime aucun gating de version (`true` est la condition C++ littérale substituée dans `if(condition_) id = id_;`). Aucun champ de la famille n'est gaté. | §4 |
| D2 | Aucun de ces ids n'est **réutilisé** par un autre paquet dans rzu : sept lignes `X(450x, true)` au total dans tout `librzu/src/packets/`. | §4 |
| D3 | Tailles totales, en-tête de 7 octets compris : `4500` = **39**, `4501` = **39**, `4502` = **9**, `4503` = **40**, `4504` = **43**, `4505` = **39**, `4506` = **71** octets. | §3 |
| D4 | Le champ chaîne de **31 octets** (30 caractères + NUL) est confirmé **deux fois** par le client lui-même : le handle de `4504` est lu à l'offset **39** et le second nom de `4506` à l'offset **40** (= 9 + 31). | §3.4 |
| D5 | Le client 7.3 **émet** `4500` et `4502` ; la chaîne de déclenchement est identifiée (contrôle de fenêtre → message interne → trame). | §2.1, §2.2 |
| D6 | Le client 7.3 **route** `4501`, `4503`, `4504`, `4505`, `4506` (un handler par id) et **ne route pas** `4500` ni `4502`, qui tombent dans le journal « message non traité ». Le serveur ne doit donc **jamais émettre** `4500` ou `4502` vers un client. | §2.3 |
| D7 | Le refus d'un `4500` ou d'un `4502` se notifie par un `TS_SC_RESULT` (id 0) dont le champ `RequestMsgID` vaut `4500`/`4502` et dont le code appartient à la famille des codes de compétition. Navislamia sait déjà écrire cette trame : `TS_SC_RESULT` = `ushort RequestMsgID` + `ushort Result` + `int Value` (15 octets), et `GameClient.SendResult` existe. | §2.4, §5.3 |
| D8 | Ni `Chihiro` ni `librzu` n'implémentent quoi que ce soit de cette famille : les sept structures sont déclarées, jamais traitées. Ce socle est du **protocole pur**, comme le socle « instances de jeu ». | §5.1 |
| D9 | **Découpage retenu** : lot minimal **C1 = `4500` + `4502`** (lecture, validation, routage, refus propre par `TS_SC_RESULT`) ; la réponse réussie (`4501`/`4503`) et le déroulé du duel (`4504`/`4505`/`4506`) sont hors du socle, faute de modèle joueur ↔ joueur et parce que la politique de duel appartient à Killian. | §5.4, §5.5 |
| D10 | Frontière avec le socle voisin « mode PK et combat joueur contre joueur (800/801) » : les deux familles **ne se recouvrent pas** (opcodes disjoints) mais **partagent un prérequis commun** — un registre de joueurs visibles et une résolution de cible joueur ↔ joueur. C1 est livrable sans ce prérequis ; tout ce qui suit ne l'est pas. | §5.8 |

---

## 1. Identité

### 1.1 La famille (sept opcodes)

| Id | Nom `TM_CS/SC` | Sens | Déclaration |
|---|---|---|---|
| 4500 | `TM_CS_COMPETE_REQUEST` | client → serveur | `op_codes.md:245` |
| 4501 | `TM_SC_COMPETE_REQUEST` | serveur → client | `op_codes.md:246` |
| 4502 | `TM_CS_COMPETE_ANSWER` | client → serveur | `op_codes.md:247` |
| 4503 | `TM_SC_COMPETE_ANSWER` | serveur → client | `op_codes.md:248` |
| 4504 | `TM_SC_COMPETE_COUNTDOWN` | serveur → client | `op_codes.md:249` |
| 4505 | `TM_SC_COMPETE_START` | serveur → client | `op_codes.md:250` |
| 4506 | `TM_SC_COMPETE_END` | serveur → client | `op_codes.md:251` |

Identique chez NGemity, à la même numérotation : `reference/ngemity/shared/Server/ClientPackets.h:250-256`.
Les sept structures rzu sont dans `reference/rzu/librzu/src/packets/GameClient/TS_CS_COMPETE_REQUEST.h`,
`TS_SC_COMPETE_REQUEST.h`, `TS_CS_COMPETE_ANSWER.h`, `TS_SC_COMPETE_ANSWER.h`,
`TS_SC_COMPETE_COUNTDOWN.h`, `TS_SC_COMPETE_START.h`, `TS_SC_COMPETE_END.h`.

Le nom des classes internes du client confirme l'appariement id ↔ nom : les gestionnaires des cinq
trames entrantes construisent des messages internes dont la RTTI MSVC donne
`SMSG_SC_COMPETE_REQUEST`, `SMSG_SC_COMPETE_ANSWER`, `SMSG_SC_COMPETE_COUNTDOWN`,
`SMSG_SC_COMPETE_START`, `SMSG_SC_COMPETE_END` (vtables `0xa52090`, `0xa52098`, `0xa520a0`, `0xa520a8`,
`0xa520b0` ; noms RTTI en `.data` `0xc1dff8`, `0xc1e020`, `0xc1e048`, `0xc1e070`, `0xc1e094`,
chaque type descriptor étant 8 octets avant le nom).
Le nom de la classe interne sortante existe aussi : `SMSG_CS_COMPETE_REQUEST` (vtable `0xa222bc`,
type descriptor `0xc13910`, nom `.?AUSMSG_CS_COMPETE_REQUEST@@` en `.data` `0xc13918`).

### 1.2 Ce que Navislamia a déjà, et ce qui manque

| Élément | État | Source |
|---|---|---|
| Membre dans l'énumération `GamePackets` | **absent** : aucun `COMPETE` dans l'énumération | `Game/Network/Packets/Enums/GamePackets.cs` (102 lignes, aucun `COMPETE`) |
| Trame `TS_SC_RESULT` | **présente** : `ushort RequestMsgID`, `ushort Result`, `int Value`, `Pack = 1` (15 octets avec l'en-tête) | `Game/Network/Packets/Game/TS_SC_RESULT.cs:5-17` |
| Émission d'un résultat | **présente** : `public void SendResult(ushort id, ushort result, int value = 0)` | `Game/Network/Clients/GameClient.cs:56-58` |
| Codes de refus de la famille | **présents, sans aucun lecteur** : `AlreadyInCompete = 61`, `NotInCompete = 62`, `WaitingCompeteRequestAnswer = 63`, `NotInCompetablePlace = 64`, `TargetAlreadyInCompete = 65`, `TargetNotInCompete = 66`, `TargetWaitingCompeteRequestAnswer = 67`, `TargetNotInCompeteablePlace = 68` | `Game/Network/Packets/ResultCode.cs:74-81` |
| Drapeaux d'état liés | `StateTimeType.EraseOnCompeteStart = 4096`, `NotActableInCompete = 8192` | `Game/DataAccess/Entities/Enums/StateTimeType.cs:23-24` |
| Modèle joueur ↔ joueur | **absent** : la `ConnectionInfo` ne tient que PNJ/monstres/props, il n'existe aucun `SpawnedPlayers` | `Game/Network/Clients/ConnectionInfo.cs:47-60` |

Les codes 61-68 de Navislamia et ceux de rzu coïncident valeur pour valeur
(`TS_RESULT_ALREADY_IN_COMPETE = 0x3D` … `TS_RESULT_TARGET_NOT_IN_COMPETIBLE_PLACE = 0x44`,
`reference/rzu/librzu/src/packets/PacketEnums.h:65-72`) ; seule l'orthographe du libellé diffère
(`Competable` chez Navislamia, `Competible` chez rzu). Ce n'est **pas** un écart de protocole.

### 1.3 Ce que la famille n'est pas

- `4700`-`4703` (`TS_SC_BATTLE_ARENA_PENALTY_INFO`, `TS_CS/SC_BATTLE_ARENA_JOIN_QUEUE`,
  `TS_SC_BATTLE_ARENA_UPDATE_WAIT_USER_COUNT`, `reference/ngemity/shared/Server/ClientPackets.h:257-260`)
  sont une **autre famille**, contiguë mais distincte : ne pas la fusionner avec ce socle.
- `4250`-`4253` (instances de jeu) sont le socle voisin déjà traité :
  `docs/packet-specs/socle-instances-jeu.md` sur la branche `hermes/packet-socle-instances-jeu`.
  Attention à une nuance de lecture de cette fiche, reprise en §2.4.

---

## 2. Ce que le joueur fait pour que le client envoie le paquet

### 2.1 `4500` (`TM_CS_COMPETE_REQUEST`) — chaîne de déclenchement prouvée

| Étape | Preuve (client 7.3) |
|---|---|
| Le joueur actionne un contrôle de fenêtre nommé **`request_compete`** | `push $0xa22dbc` (`"request_compete"`) en `0x4dbdc8`, `call 0x48ef10` (comparateur nom ↔ objet), test `%al` en `0x4dbdd6` |
| Le client construit un **message interne** (`0x33` = 51 octets) dont le constructeur `0x4d08b0` pose l'id interne `0x84` (`movl $0x84,0x4(%eax)` en `0x4d08b4`) et la vtable `0xa222bc` (`movl $0xa222bc,(%eax)` en `0x4d08cc`, RTTI `SMSG_CS_COMPETE_REQUEST`) | `0x4dbde7`-`0x4dbdf7` |
| Il y écrit `compete_type = 0` (`movb $0x0,0x13(%eax)` en `0x4dbe06`) | `0x4dbe06` |
| Il y copie le **nom de la cible** déjà présent dans la fenêtre (champ `0x4a4(%esi)`, boucle de copie terminée par le NUL) vers le message `+0x14` | `0x4dbe00`-`0x4dbe1f` |
| Le message interne est émis ; le bloc `0x49d3f6` le convertit en **trame 4500** : constructeur `0x48d0c0` appelé en `0x49d3f9`, `compete_type` recopié de `+0x13` vers l'**offset 7** de la trame (`mov 0x13(%edi),%al` en `0x49d3fe`, `mov %al,-0x41(%ebp)` en `0x49d401`), nom recopié de `+0x14` vers l'**offset 8** (boucle `0x49d410`-`0x49d418`) | `0x49d3f6`-`0x49d438` |
| Envoi par la session : `mov 0xb8(%esi),%esi` puis `mov 0xc4(%edx),%edx` et `call *%edx` | `0x49d41a`-`0x49d436` |

Valeur observée de `compete_type` sur ce chemin : **0** (`0x4dbe06`). La cible est désignée par un nom
(nom affiché dans la fenêtre), et non par un handle.

### 2.2 `4502` (`TM_CS_COMPETE_ANSWER`) — trois sites d'émission

| Site | Déclencheur | Valeurs écrites (offsets 7 et 8) | Preuve |
|---|---|---|---|
| `0x49d1a3`-`0x49d1d3` | contrôle de fenêtre **`battle_start`** (`push $0xa201c4` en `0x49d191`) | `movw $0x0,-0x11(%ebp)` en `0x49d1b5` → `compete_type` = **0**, `answer_type` = **0** | constructeur `0x48d110` appelé en `0x49d1aa`, envoi en `0x49d1cb`-`0x49d1d1` |
| `0x49d1ea`-`0x49d211` | contrôle de fenêtre **`battle_reject`** (`push $0xa201b4` en `0x49d1d8`) | `movw $0x100,-0x11(%ebp)` en `0x49d1ff` → `compete_type` = **0**, `answer_type` = **1** (piège de boutisme : `0x0100` s'écrit `00 01`, soit `0x00` à l'offset 7 et `0x01` à l'offset 8) ; suivie d'une boîte de message d'id `0x673` = **1651** (`push $0x673` en `0x49d1fa`) | constructeur `0x48d110` appelé en `0x49d1f5` |
| `0x4947e6`-`0x494833` | branche par défaut de la fonction `0x4946f0`-`0x494843` (messages internes, `jne 0x4947e6` en `0x494745`) | construction **en ligne** de la trame (id `0x1196` en `0x4947ee`, `Length = 9` en `0x4947fa`), `movw $0x200,-0x11(%ebp)` en `0x494819` → `compete_type` = **0**, `answer_type` = **2** | `0x4947e6`-`0x494831` |

Sur les trois sites, `compete_type` vaut **0** et seul `answer_type` varie (`0`, `1`, `2`). **La
sémantique de ces deux champs n'est établie par aucune source** (§7a, §7b) : elle est à trancher.

Le troisième site est celui qui formate `"#@player_name@#"` (chaîne `0xa1fc0c`, `push` en `0x49475e`,
`call 0x4912d0` en `0x494770`) avant d'afficher une boîte, et le vocabulaire de l'interface est celui
d'une « bataille » (`battle_start`, `battle_reject`, plus `battle_invite`, `/battle %u`,
`/battle_accept `, `BATTLE_START`, `BATTLE_INVITE`, `msgboxbattleaccept` présents dans le binaire).
`db_string.rdb` contient en clair des textes d'invitation
(`"'#@player_name@#' has invited you to join the Fanatics team in a Battle Arena skirmish.<BR>Will you
accept the invitation?"`, `strings -n 6 db_string.rdb`, lignes 4794, 4796 et 4798). La **concordance**
avec `4501` est probable mais **non prouvée octet à octet** (§7l) : ne pas s'en servir comme d'une
spécification.

### 2.3 Ce que le client fait à la réception (routage entrant)

Dispatcher des trames entrantes : `0x67e1d9`. Chaîne utile, id par id :

| Id reçu | Chemin | Handler | Message interne produit |
|---|---|---|---|
| 4500 | `cmp $0x1195` puis `je 0x67e7af` en `0x67e5d8`-`0x67e5e3` → non ; `cmp $0x7d4` / `jg 0x67e6e6` en `0x67e5e9` ; `cmp $0xfa1` / `jg 0x67e736` en `0x67e6e6` ; `sub $0xfa2` + `cmp $0xfb` + `ja 0x67ef21` en `0x67e736`-`0x67e740` (4500 − 4002 = 498 > 251) → **journal « message non traité »** (`push $0xa53df0` en `0x67ef22`) | **aucun** | — |
| 4501 | `je 0x67e7af` en `0x67e5e3` → `call 0x6713f0` en `0x67e7b2` | `0x6713f0` | id interne `0x85` (133), vtable `0xa52090` (`0x671408`-`0x671420`) |
| 4502 | `jg 0x67e7bc` en `0x67e5dd` → non ; puis `sub $0x1197` + `cmp $0x3` + `ja 0x67ef21` en `0x67e7c5`-`0x67e7cd` (4502 − 4503 = −1 > 3) → **journal « message non traité »** | **aucun** | — |
| 4503 | table `jmp *0x67f67c(,%eax,4)` en `0x67e7d3` (sauts `0x67e7da`, `0x67e7e7`, `0x67e7f4`, `0x67e801`), `call 0x671460` en `0x67e7dd` | `0x671460` | id interne `0x87` (135), vtable `0xa52098` |
| 4504 | table `0x67f67c`, `call 0x6714e0` en `0x67e7ea` | `0x6714e0` | id interne `0x88` (136), vtable `0xa520a0` |
| 4505 | table `0x67f67c`, `call 0x671560` en `0x67e7f7` | `0x671560` | id interne `0x89` (137), vtable `0xa520a8` |
| 4506 | table `0x67f67c`, `call 0x6715d0` en `0x67e804` | `0x6715d0` | id interne `0x8a` (138), vtable `0xa520b0` |

Ce que chaque handler lit dans la trame (c'est la seconde lecture indépendante qui verrouille les
offsets de §3) :

| Handler | Champs lus et offsets dans la trame |
|---|---|
| `0x6713f0` (4501) | `compete_type` en `0x7(%eax)` (`0x67142f`) ; chaîne depuis `0x8(%eax)` (`0x671432`), copiée jusqu'au NUL (`0x671440`-`0x671448`) |
| `0x671460` (4503) | `compete_type` en `0x7`, `answer_type` en `0x8` (`0x67149f`, `0x6714a5`) ; chaîne depuis `0x9` (`0x6714ab`) |
| `0x6714e0` (4504) | `compete_type` en `0x7` (`0x67151f`) ; `handle_competitor` lu sur **4 octets** en `0x27` = **39** (`mov 0x27(%eax),%edx` en `0x671525`) ; chaîne depuis `0x8` (`0x67152b`) |
| `0x671560` (4505) | `compete_type` en `0x7` (`0x67159f`) ; chaîne depuis `0x8` (`0x6715a2`) |
| `0x6715d0` (4506) | `compete_type` en `0x7`, `end_type` en `0x8` (`0x671611`, `0x671617`) ; `winner` depuis `0x9` (`0x67161a`) ; `loser` depuis `0x28` = **40** (`0x67162f`) |

### 2.4 Ce que le client affiche sur un `TS_SC_RESULT`

La fonction `0x4774dd`-`0x4776f7` aiguille sur la **valeur de 16 bits lue en `0x15(%edi)`** — le champ
`Result` du message interne — après un aiguillage préalable sur le champ `RequestMsgID`
(`sub $0x109a` = 4250 en `0x4775ec`, `sub $0xfa` = 4500 en `0x4775f7`, `sub $0x2` = 4502 en `0x4775fe`).
Elle affiche ensuite une des boîtes de message :

| `RequestMsgID` | Code `Result` | Boîte (id de ressource) | Preuve |
|---|---|---|---|
| 4500 | `0x41` (65 `TargetAlreadyInCompete`) | `0x664` = 1636 | `0x477637`-`0x47763a`, saut en `0x477629` |
| 4500 | `0x02`, `0x3d` (61 `AlreadyInCompete`), `0x1a` (26), `0x40` (64 `NotInCompetablePlace`), `0x44` (68 `TargetNotInCompeteablePlace`), `0x01`, `0x3f` (63 `WaitingCompeteRequestAnswer`), `0x43` (67 `TargetWaitingCompeteRequestAnswer`) | `0x661` = 1633 | `0x47763c`-`0x47765f`, cible `0x477668` |
| 4500 | tout autre code | **rien** | `jne 0x4776ef` en `0x477662` |
| 4502 | `0x3d` (61) ou `0x41` (65) | `0x664` = 1636 | `0x47760b`-`0x477613` |
| 4502 | `0x44` (68), `0x40` (64), `0x3e` (62 `NotInCompete`) | `0x661` = 1633 | `0x477615`-`0x477622` |
| 4502 | `0x01` | `0x661` = 1633 | `0x477624`-`0x477627`, cible `0x477668` |
| 4502 | tout autre code | **rien** | `jne 0x4776ef` en `0x477662` |
| 4250 (socle voisin) | `0x0a` → 9234 ; `0x38`, `0x48` → 9239 ; `0x4e` → 9245 | `0x2412`, `0x2417`, `0x241d` | `0x4776a7`-`0x4776d2` |

Le texte des boîtes 1633, 1636, 1651 (et 9234/9239/9245) **n'est pas établi** (§7e) : l'extraction de
`db_string.rdb` n'a pas abouti, faute d'un format d'enregistrement confirmé.

Enfin, après l'affichage d'une boîte pour 4500 ou 4502, le client remet à zéro le drapeau
« en attente de réponse » (`movb $0x0,0x45(%ecx)` via le global `0xc4b28c` en `0x477692`).

> **Nuance de lecture du socle voisin.** La fiche `socle-instances-jeu.md` (§ son tableau de
> l'émetteur de 4250) décrit ce même aiguillage comme « un message entrant d'id 4250 ». La lecture
> correcte est : la valeur aiguillée ici est le **`RequestMsgID` porté par `TS_SC_RESULT`** (id de
> trame 0), et non l'id d'une trame entrante. La preuve est la nature des valeurs testées ensuite :
> `0x0a`/`0x38`/`0x48`/`0x4e` pour 4250 et `61`-`68` pour 4500/4502 sont des **codes de résultat**,
> pas des ids de paquet. La conclusion de la fiche voisine (le serveur peut refuser par un résultat)
> reste juste ; seule la description du mécanisme est à corriger.

---

## 3. Structure sur le fil

### 3.1 En-tête commun (7 octets)

| Offset | Type | Nom | Source |
|---|---|---|---|
| 0 | `uint32` LE | `Length` — longueur **totale**, en-tête compris | constructeurs clients : `movl $0x27,(%esi)` en `0x48d0f6` (4500, 4 octets écrits d'un coup), `movl $0x9,-0x18(%ebp)` en `0x4947fa` (4502) |
| 4 | `uint16` LE | `ID` | `mov %ax,0x4(%esi)` en `0x48d0f2` (4500, id `0x1194`), `mov %dx,-0x14(%ebp)` en `0x4947f6` (4502, id `0x1196`) |
| 6 | `uint8` | `Checksum` — somme des octets 0…5, modulo 256 | boucle de somme `0x48d0cf`-`0x48d0da` puis écriture en `0x48d0e1` (4500) ; même boucle en `0x494804`-`0x494816` (4502) |

Toutes les tailles annoncées ci-dessous incluent ces 7 octets. Les constructeurs clients sont cités
avec leur `Length` : c'est la preuve la plus directe, le client écrivant la taille en dur dans le
tampon (`0x48d0f6` pour 4500, `0x48d140` pour 4502).

### 3.2 `TM_CS_COMPETE_REQUEST` (4500) — **39 octets**

| Offset | Type | Nom | Source | Valeur observée |
|---|---|---|---|---|
| 7 | `int8` | `compete_type` | rzu `TS_CS_COMPETE_REQUEST.h:6` ; client `0x49d3fe`/`0x49d401` (copie vers l'offset 7) | **0** (émetteur `0x4dbe06`) |
| 8 | `char[31]` | `requestee` — nom de la cible, NUL compris, tampon **fixe** | rzu `TS_CS_COMPETE_REQUEST.h:7` ; client `0x49d404`-`0x49d418` (copie terminée par le NUL vers l'offset 8) | nom de la cible (chaîne source `0x4a4(%esi)`) |

Preuve de taille : constructeur `0x48d0c0`, `movl $0x27,(%esi)` en `0x48d0f6` (**39**) ; id
`mov $0x1194,%eax` en `0x48d0e8`. 7 + 1 + 31 = 39. Appelé une seule fois, en `0x49d3f9`.

### 3.3 `TM_SC_COMPETE_REQUEST` (4501) — **39 octets**

| Offset | Type | Nom | Source | Preuve client |
|---|---|---|---|---|
| 7 | `int8` | `compete_type` | rzu `TS_SC_COMPETE_REQUEST.h:6` | lu en `0x7(%eax)` par `0x6713f0` (`0x67142f`) |
| 8 | `char[31]` | `requester` — nom du demandeur, NUL compris | rzu `TS_SC_COMPETE_REQUEST.h:7` | copie depuis `0x8(%eax)`, arrêt au NUL (`0x671432`-`0x671448`) |

NGemity est identique (`shared/Server/Packets/GameClient/TS_SC_COMPETE_REQUEST.h`).

### 3.4 `TM_CS_COMPETE_ANSWER` (4502) — **9 octets**

| Offset | Type | Nom | Source | Valeur observée |
|---|---|---|---|---|
| 7 | `int8` | `compete_type` | rzu `TS_CS_COMPETE_ANSWER.h:6` | **0** aux trois sites d'émission (`0x49d1b5`, `0x49d1ff`, `0x494819`) |
| 8 | `int8` | `answer_type` | rzu `TS_CS_COMPETE_ANSWER.h:7` | **0** (`battle_start`), **1** (`battle_reject`), **2** (`0x4947e6`) |

Preuve de taille : constructeur `0x48d110`, `movl $0x9,(%eax)` en `0x48d140` (**9**) ; id
`mov $0x1196,%edx` en `0x48d135` ; construction en ligne équivalente en `0x4947ee`-`0x4947fa`. 7 + 1 + 1 = 9.

### 3.5 `TM_SC_COMPETE_ANSWER` (4503) — **40 octets**

| Offset | Type | Nom | Source | Preuve client |
|---|---|---|---|---|
| 7 | `int8` | `compete_type` | rzu `TS_SC_COMPETE_ANSWER.h:6` | `0x67149f` |
| 8 | `int8` | `answer_type` | rzu `TS_SC_COMPETE_ANSWER.h:7` | `0x6714a5` |
| 9 | `char[31]` | `requestee` | rzu `TS_SC_COMPETE_ANSWER.h:8` | copie depuis `0x9` (`0x6714ab`-`0x6714bb`) |

7 + 1 + 1 + 31 = 40.

### 3.6 `TM_SC_COMPETE_COUNTDOWN` (4504) — **43 octets**

| Offset | Type | Nom | Source | Preuve client |
|---|---|---|---|---|
| 7 | `int8` | `compete_type` | rzu `TS_SC_COMPETE_COUNTDOWN.h:6` | `0x67151f` |
| 8 | `char[31]` | `competitor` | rzu `TS_SC_COMPETE_COUNTDOWN.h:7` | copie depuis `0x8` (`0x67152b`-`0x67153b`) |
| 39 | `uint32` | `handle_competitor` (`ar_handle_t`, 4 octets) | rzu `TS_SC_COMPETE_COUNTDOWN.h:8` ; type `strong_typedef<ar_handle_t, uint32_t>` en `librzu/src/lib/Packet/GameTypes.h:40` | lecture **32 bits** en `0x27` = 39 (`mov 0x27(%eax),%edx` en `0x671525`) |

7 + 1 + 31 + 4 = 43. **C'est la preuve n° 1 de la largeur 31** : le client lit le handle à 39 = 8 + 31.

### 3.7 `TM_SC_COMPETE_START` (4505) — **39 octets**

| Offset | Type | Nom | Source | Preuve client |
|---|---|---|---|---|
| 7 | `int8` | `compete_type` | rzu `TS_SC_COMPETE_START.h:6` | `0x67159f` |
| 8 | `char[31]` | `competitor` | rzu `TS_SC_COMPETE_START.h:7` | copie depuis `0x8` (`0x6715a2`-`0x6715b8`) |

7 + 1 + 31 = 39.

### 3.8 `TM_SC_COMPETE_END` (4506) — **71 octets**

| Offset | Type | Nom | Source | Preuve client |
|---|---|---|---|---|
| 7 | `int8` | `compete_type` | rzu `TS_SC_COMPETE_END.h:6` | `0x671611` |
| 8 | `int8` | `end_type` | rzu `TS_SC_COMPETE_END.h:7` | `0x671617` |
| 9 | `char[31]` | `winner` | rzu `TS_SC_COMPETE_END.h:8` | copie depuis `0x9` (`0x67161a`-`0x67162d`) |
| 40 | `char[31]` | `loser` | rzu `TS_SC_COMPETE_END.h:9` | copie depuis `0x28` = **40** (`0x67162f`-`0x671640`) |

7 + 1 + 1 + 31 + 31 = 71. **C'est la preuve n° 2 de la largeur 31** : le second nom commence à 40 = 9 + 31.

### 3.9 Récapitulatif des tailles

| Id | Nom | Sens | Charge utile | **Total** |
|---|---|---|---|---|
| 4500 | `TM_CS_COMPETE_REQUEST` | C → S | 32 | **39** |
| 4501 | `TM_SC_COMPETE_REQUEST` | S → C | 32 | **39** |
| 4502 | `TM_CS_COMPETE_ANSWER` | C → S | 2 | **9** |
| 4503 | `TM_SC_COMPETE_ANSWER` | S → C | 33 | **40** |
| 4504 | `TM_SC_COMPETE_COUNTDOWN` | S → C | 36 | **43** |
| 4505 | `TM_SC_COMPETE_START` | S → C | 32 | **39** |
| 4506 | `TM_SC_COMPETE_END` | S → C | 64 | **71** |

Deux trames seulement sont **à lire** (4500 = 39, 4502 = 9) ; les cinq autres sont **à écrire**.

### 3.10 Règle des chaînes

Les cinq gestionnaires entrants copient les noms avec une boucle **arrêtée par le NUL**
(`0x671440`-`0x671448`, `0x6714b3`-`0x6714bb`, `0x671533`-`0x67153b`, `0x6715b0`-`0x6715b8`,
`0x671625`-`0x67162d`) : le client traite ces champs comme des chaînes C **dans un tampon de taille
fixe de 31 octets**. Conséquence pour le serveur : écrire le NUL **à l'intérieur** des 31 octets et
zéroter le reste du tampon (c'est ce que fait rzu, `MessageBuffer.cpp:87-95`, qui écrit toujours
`maxSize` octets). Un nom de 30 caractères non terminé déborderait sur le champ suivant.

---

## 4. Gating de version

### 4.1 Ce que `X(4500, true)` signifie, prouvé

| Fait | Source |
|---|---|
| `X(...)` est le second paramètre de `CREATE_PACKET_VER_ID`, qui déplie `name_##_ID(...)` en `using PACKET_IDS = packet_ids_list<0 name_##_ID(HEADER_F_ID)>` et `packetID = PACKET_IDS::getLatest()` | `librzu/src/lib/Packet/PacketDeclaration.h:592-593`, macro `CREATE_PACKET_VER_ID` en `:616-621` |
| La forme à deux arguments est dépliée par `SERIALISATION_F_ID2(id_, condition_)` → `if(condition_) id = id_;` | `PacketDeclaration.h:585-588` |
| La « condition » est donc du **C++ littéral**, recopié tel quel dans `getId(version)` ; `true` donne `if(true) id = 4500;` — **aucune version n'est testée** | `PacketDeclaration.h:587-588` + `:599` |
| `PacketDeclaration::packet_ids_list` retourne la **dernière** valeur énumérée ; `getLatest()` sur une liste à un seul élément retourne cet unique id | `PacketDeclaration.h:90-98`, utilisé en `:593` et `:572` (`writeHeader(size, PACKET_IDS::getLatest())`) |

Autrement dit : `true` n'exprime **pas** « valide partout » par convention, mais « pas de condition du
tout ». Les deux formulations donnent le même résultat ici, mais la déduction est directe et ne repose
sur aucune convention implicite.

### 4.2 Id par id

| Id | Ligne exacte | Condition | Décision pour 7.3 |
|---|---|---|---|
| 4500 | `TS_CS_COMPETE_REQUEST.h:10` | `true` | id 4500, sans gating |
| 4501 | `TS_SC_COMPETE_REQUEST.h:10` | `true` | id 4501, sans gating |
| 4502 | `TS_CS_COMPETE_ANSWER.h:10` | `true` | id 4502, sans gating |
| 4503 | `TS_SC_COMPETE_ANSWER.h:11` | `true` | id 4503, sans gating |
| 4504 | `TS_SC_COMPETE_COUNTDOWN.h:11` | `true` | id 4504, sans gating |
| 4505 | `TS_SC_COMPETE_START.h:10` | `true` | id 4505, sans gating |
| 4506 | `TS_SC_COMPETE_END.h:12` | `true` | id 4506, sans gating |

Vérification de non-réutilisation : `grep -rn "X(450[0-6]" librzu/src/packets/` ne remonte que ces
sept lignes — aucun autre paquet ne revendique ces ids pour une autre plage de versions.

### 4.3 Champs

Aucun des sept en-têtes rzu ne porte de `_(def)` / `_(impl)` conditionnel, ni de
`version < EPIC_*` / `version >= EPIC_*` : **aucun champ de la famille n'est gaté**. Les structures
complètes de §3 sont donc valides telles quelles en 7.3. C'est l'inverse de la famille voisine
HuntaHolic (`socle-instances-jeu.md` §4.2), où le gating des champs de 4253 est le piège principal :
ici il n'y a **rien à retirer** aux structures NGemity/rzu.

---

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` fait de ces paquets : **rien**

| Fait | Source |
|---|---|
| Les sept ids sont déclarés | `reference/ngemity/shared/Server/ClientPackets.h:250-256` |
| Les sept structures existent | `reference/ngemity/shared/Server/Packets/GameClient/TS_*.h` |
| Aucun handler : `grep -rn "COMPETE" Chihiro/src/` ne remonte que des constantes | `Entities/Unit/Unit.h:80` (`CRT_COMPETE = 2`), `Skills/StateBase.h:35-36` (`AF_ERASE_ON_COMPETE_START`, `AF_NOT_ACTABLE_IN_COMPETE`), `Network/GameNetwork/WorldSession.cpp:1421` (`REVIVE_COMPETE = 2`) |
| `librzu` non plus : la famille n'apparaît que comme déclarations de structures et comme codes de résultat | `librzu/src/packets/PacketEnums.h:65-72` |

Il n'y a donc **aucune logique de référence à porter** : ni machine à états, ni délai de compte à
rebours, ni règle d'éligibilité. Tout ce qui suit est déduit du client, et tout ce qui touche à la
politique de jeu va en `## A VERIFIER PAR KILLIAN`.

### 5.2 Ce que le serveur doit faire (obligations prouvées par le client)

| Obligation | Preuve |
|---|---|
| Recevoir `4500` (39 octets) et `4502` (9 octets), refuser toute autre longueur | longueurs des constructeurs clients (§3.2, §3.4) |
| **Ne jamais émettre** `4500` ni `4502` vers un client | ces deux ids tombent dans le journal « message non traité » du dispatcher entrant (`0x67ef21`, `push $0xa53df0` en `0x67ef22`) |
| Router `4501`, `4503`, `4504`, `4505`, `4506` : le client les traite et affiche une boîte / met à jour sa fenêtre | handlers `0x6713f0`, `0x671460`, `0x6714e0`, `0x671560`, `0x6715d0` |
| Refuser par `TS_SC_RESULT` (id 0) avec `RequestMsgID = 4500` ou `4502` et un code de la famille | aiguillage `0x4774dd`-`0x4776f7` (§2.4) |
| Terminer chaque nom par un NUL dans son tampon de 31 octets | boucles de copie terminées par le NUL (§3.10) |
| Répondre à `4500` en transmettant `4501` à la cible et, en cas de refus, un `TS_SC_RESULT` au demandeur | `4501` est routé par le client ; `TS_SC_RESULT(RequestMsgID = 4500)` est le seul refus que le client sait afficher (§2.4) |

La **réussite** d'un `4500` (envoi de `4501` à la cible) suppose de savoir *qui* est la cible : le
champ `requestee` est un **nom**, pas un handle. La résolution nom → joueur n'existe pas encore côté
serveur (§1.2), et la politique d'appariement n'est pas établie (§7g).

### 5.3 Codes de refus que chaque trame peut produire

Codes **connus du client** pour chaque `RequestMsgID` (§2.4), avec leur nom dans Navislamia
(`Game/Network/Packets/ResultCode.cs:74-81`) et dans rzu (`PacketEnums.h:65-72`) :

| Trame à refuser | Codes affichés par le client (boîte 1636 / 1633) | Codes sans affichage | Peut produire ces codes ? |
|---|---|---|---|
| `4500` | 65 ; puis 26, 61, 64, 67, 68, 63, 1, 2 | tout le reste | **établi** : 61 (`AlreadyInCompete`), 63 (`WaitingCompeteRequestAnswer`), 64 (`NotInCompetablePlace`), 65 (`TargetAlreadyInCompete`), 67 (`TargetWaitingCompeteRequestAnswer`), 68 (`TargetNotInCompeteablePlace`) — six des huit codes de la famille ont un sens direct pour une demande. 66 (`TargetNotInCompete`) **n'a pas de boîte** dédiée : à trancher (§7h) |
| `4502` | 61, 65 ; puis 62, 64, 68 ; puis 1 | tout le reste | **établi** : 61, 62 (`NotInCompete`), 64, 65, 68. La famille compte huit codes (61-68) : 63, 66, 67 n'ont aucune boîte pour `4502` |

Les codes 26 (`0x1a`), 1 et 2 sont testés par le client mais **ne portent pas de nom de la famille
compétition** dans Navislamia (`ResultCode.cs:1-81`) : leur signification est **NON ÉTABLIE** (§7i).

### 5.4 Socle minimum implémentable — décision

**Hypothèse du PO, tranchée : oui, les deux trames client → serveur `4500` et `4502` sont assez
déterministes pour être lues, routées et testées en offsets, la réponse et l'exécution du duel
restant hors périmètre.**

Raisons, dans l'ordre où elles décident :

1. **Format entièrement déterministe** : 39 et 9 octets, aucun champ variable, aucun `version <` /
   `version >=` (§3, §4). Rien à déduire de l'état du monde.
2. **Double source** : rzu et NGemity donnent la même disposition, et le client la confirme par ses
   constructeurs (sites d'émission) *et* par ses lecteurs (§3.4, §3.6, §3.8) — la discipline d'offsets
   du dépôt est directement applicable.
3. **Refus prouvé sans inventer de gameplay** : le seul effet observable du refus est un
   `TS_SC_RESULT` avec un code que le client sait afficher (§2.4, §5.3). Navislamia possède déjà la
   trame (`TS_SC_RESULT.cs`) et l'émetteur (`GameClient.cs:56-58`) : le lot minimal n'invente aucune
   trame ni aucune valeur de jeu, il n'ajoute que le routage et les codes.
4. **Ce qui reste hors périmètre l'est pour une raison nommable** : la réussite d'un duel suppose
   (a) un registre de joueurs visibles et une résolution de cible par nom, absents du serveur
   (§1.2, §5.8), et (b) une politique de duel (durée, temporisation, sort du perdant, récompense,
   éligibilité) qui appartient à Killian et qu'aucune référence ne porte (§5.1).

Conséquence pratique : le lot C1 rend les deux paquets **reçus et refusés proprement** au lieu de
lever `Unknown Packet Type` (`Game/Network/Clients/GameClient.cs:802`) ou de laisser un client
attendre une réponse qui n'arrive jamais. Il ne rend **aucun** duel jouable : c'est le socle
*protocole*, pas le socle *fonctionnel*.

### 5.5 Découpage proposé pour `navis-dev`

| Lot | Contenu | Opcodes | Critère d'acceptation propre |
|---|---|---|---|
| **C1** *(ce socle)* | Lecture et refus des deux trames client → serveur | 4500, 4502 | test d'offsets des deux tailles (**39** et **9**, avec position de chaque champ) ; `Length` refusée si ≠ 39/9 ; nom non terminé refusé ; **aucun** `4500`/`4502` émis ; les deux ids déclarés dans `GamePackets` **et** routés (aucun n'atteint `GameClient.cs:802` → critère transversal n° 4) ; refus par `SendResult(4500\|4502, code)` avec un code de §5.3 |
| **C2** | Invitation : `4501` vers la cible, `4503` vers le demandeur | 4501, 4503 | chaînes de 31 octets NUL-terminées, test d'offsets (39 et 40) ; suppose la résolution nom → joueur (§5.8) |
| **C3** | Compte à rebours et début | 4504, 4505 | test d'offsets 43 (handle à **39**, littéral) et 39 ; suppose la durée du compte à rebours tranchée (§7d) |
| **C4** | Fin du duel | 4506 | test d'offsets 71 (`winner` à 9, `loser` à **40**) ; suppose la politique de fin tranchée (§7c) |

Dépendances : **C1 est indépendant de tout**. C2 → C3 → C4, et C2 exige un modèle joueur ↔ joueur.
Rien n'oblige à faire les quatre lots : `C1` seul est un lot livrable, testable et sans décision de
gameplay.

Les cinq **paquets restants**, prêts à rouvrir sans refaire l'archéologie (format détaillé au §3,
prérequis nommés) :

| Paquet | Opcode | Sens | Format (charge utile → total) | Prérequis pour le rouvrir |
|---|---|---|---|---|
| `TM_SC_COMPETE_REQUEST` | 4501 | S → C | `int8 compete_type` + `char[31] requester` → **39** (§3.3) | lot **C2** ; résolution nom → joueur (§5.8) |
| `TM_SC_COMPETE_ANSWER` | 4503 | S → C | `int8 compete_type` + `int8 answer_type` + `char[31] requestee` → **40** (§3.5) | lot **C2** ; sémantique de `answer_type` (§7b) |
| `TM_SC_COMPETE_COUNTDOWN` | 4504 | S → C | `int8 compete_type` + `char[31] competitor` + `uint32 handle_competitor` (lu à l'offset **39**) → **43** (§3.6) | lot **C3** ; durée et cadence du compte à rebours (§7d), sens du handle (§7j) |
| `TM_SC_COMPETE_START` | 4505 | S → C | `int8 compete_type` + `char[31] competitor` → **39** (§3.7) | lot **C3** |
| `TM_SC_COMPETE_END` | 4506 | S → C | `int8 compete_type` + `int8 end_type` + `char[31] winner` + `char[31] loser` (lu à l'offset **40**) → **71** (§3.8) | lot **C4** ; domaine de `end_type` (§7c) et politique de fin de duel (§7i) |

Chacun de ces cinq paquets est **à écrire**, jamais à recevoir (§2.3) : c'est le serveur qui décide
de les envoyer, et le client les traite sans condition.

### 5.6 Points d'implémentation à respecter

| Point | Règle | Source |
|---|---|---|
| Enum et dispatch ensemble | tout membre ajouté à `GamePackets` doit être routé, sinon il atteint `_ => throw new Exception("Unknown Packet Type")` | `Game/Network/Clients/GameClient.cs:802` ; critère transversal n° 4 |
| Forme du dispatch | la boucle existante enchaîne des `if (header.ID == (ushort)GamePackets.X) { Handle…; continue; }` **avant** le `switch` final — suivre ce style | `GameClient.cs:555-790` |
| Refus | passer par `SendResult(ushort id, ushort result, int value = 0)` ; ne pas réécrire `TS_SC_RESULT` | `GameClient.cs:56-58`, `Game/Network/Packets/Game/TS_SC_RESULT.cs:5-17` |
| Longueur reçue | `Length` **et** le NUL des chaînes doivent être vérifiés par le handler ; l'en-tête est déjà validé en amont | `GameClient.cs:555` et suivants |
| Tests | au moins **366** tests (448 au moment de la fiche), plus un test d'offsets par mémoire lue ou écrite : taille totale **et** position de chaque champ | critères transversaux n° 2 et 3 |
| `CLAUDE.md` | ne pas l'écrire : le bloc ci-dessous part dans la description de la MR | critère transversal n° 5 |

### 5.7 Cas limites

| Cas | Ce que fait le client | Comportement serveur recommandé |
|---|---|---|
| `Length` = 7 reçu pour 4500/4502 | le client enverrait un tampon non rempli ; aucun contrôle de sa part à l'émission | refuser et journaliser, sans réponse |
| Nom de 30 caractères sans NUL dans 4500 | le client lit 31 octets fixes : le dépassement ne le concerne pas (il émet), mais la cible serait ambiguë | valider le NUL **dans** les 31 octets, sinon refuser |
| `compete_type` ≠ 0 dans 4500 | le gestionnaire `4501` recopie l'octet sans le tester (`0x67142f`-`0x671438`) : **aucun refus client** sur une valeur inconnue | accepter la trame, journaliser la valeur, ne pas déconnecter ; la signification est à trancher (§7a) |
| `answer_type` hors `{0,1,2}` dans 4502 | aucune table de valeurs côté client : le client n'émet que 0, 1 ou 2 (§3.4) | refuser par `TS_SC_RESULT(4502, 1)` (code sans boîte) ou journaliser sans réponse — décision de lot |
| Réponse reçue sans demande en cours | le client remet à zéro son drapeau « en attente » (`0x477692`) | n'envoyer `TS_SC_RESULT` que pour une demande/réponse effectivement reçue |
| Cible inconnue (nom non résolu) | le client afficherait la boîte correspondant au code reçu | refus par un code de §5.3 ; le choix du code exact est un arbitrage (§7h) |

### 5.8 Frontière avec le socle « mode PK et combat joueur contre joueur » (800/801)

Fait établi, vérifié dans le dépôt :

| Constat | Source |
|---|---|
| Aucun registre de joueurs visibles : la `ConnectionInfo` ne tient que PNJ, monstres et props | `Game/Network/Clients/ConnectionInfo.cs:47-60` |
| Le ciblage d'une compétence sur « un autre joueur » n'est pas modélisé | `Game/Services/SkillCastService.cs:284-287` (« Summons and other players are not modelled ») |
| Le combat ne résout que des monstres | `Game/Services/CombatService.cs:42-50` (`TryResolveMonster`) |

Conclusion : les deux socles **ne se recouvrent pas** — les opcodes sont disjoints (800/801 d'un côté,
4500-4506 de l'autre) et aucun format n'est partagé — mais ils **partagent le même prérequis
manquant** : un modèle joueur ↔ joueur (visibilité, résolution de cible, application de dégâts).

Conséquence pour le découpage : **C1 est livrable maintenant** ; C2 exige la résolution nom → joueur
(qui appartient au socle PK ou à un socle « registre de joueurs » commun) ; C3 et C4 exigent en plus
un état de duel. La fiche ne propose **pas** de fusion des deux cartes : c'est le PO qui ajustera le
board (`zNSZ9eX3`, `BY6qivuo`), pas le pipeline.

---

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | NGemity | Navislamia (cette fiche) | Raison |
|---|---|---|---|
| Gating | `CREATE_PACKET(TS_CS_COMPETE_REQUEST, 4500)` — id unique, aucun mécanisme de version | `CREATE_PACKET_VER_ID` + `X(id, condition)` | NGemity ne sait pas exprimer une renumérotation. Ici les deux donnent le même id (aucun des sept n'est renuméroté, §4.2), donc l'écart est **sans effet** — mais il faut lire rzu pour *constater* l'absence de gating, pas la supposer |
| Champs | Structures identiques à rzu, sans gating | Idem | **Aucun écart** : c'est la seule partie de la famille que les deux références confirment champ par champ |
| Logique | Sept structures déclarées, **aucun handler** (§5.1) | Rien non plus | Il n'y a rien à porter ; NGemity ne sert ici que par ses constantes adjacentes (`CRT_COMPETE`, `AF_ERASE_ON_COMPETE_START`, `AF_NOT_ACTABLE_IN_COMPETE`, `REVIVE_COMPETE`) |
| Numérotation des voisins | `4700`-`4703` (Battle Arena) déclarés à la suite | Non traités ici | Famille distincte, notée en §1.3 pour ne pas être confondue avec la compétition |
| Portée | Aucune notion de captation par le client | Le client 7.3 tranche : il route `4501`/`4503`-`4506` et **pas** `4500`/`4502` (§2.3) | NGemity ne dit rien de la direction réellement traitée par le client ; `4500`/`4502` sont des trames **sortantes** et ne sont pas bidirectionnelles |

---

## 7. `NON ÉTABLI`

Aucune de ces questions n'a de réponse dans rzu, NGemity ou le client. Elles ne doivent **pas** être
devinées : elles vont en `## A VERIFIER PAR KILLIAN`.

### (a) Domaine et sémantique de `compete_type`

Tous les chemins observés écrivent `0` (§2.1, §2.2). Aucune énumération dans rzu, aucune dans
NGemity. Question : `compete_type` distingue-t-il des *types* de compétition (duel, arène, équipe) ?
Si oui, lesquels, et le serveur doit-il les distinguer ?

### (b) Domaine et sémantique de `answer_type`

Valeurs observées : `0` (`battle_start`), `1` (`battle_reject`), `2` (branche `0x4947e6`). Aucune
énumération nulle part. Question : `0` = accepte, `1` = refuse, `2` = ? (expiration ? refus par
défaut ? choix d'équipe ?). Le lot C1 peut refuser sans le savoir ; C2/C3 en dépendent.

### (c) Domaine et sémantique de `end_type` (`4506`)

Aucune énumération. Question : que distingue-t-il (victoire par K.O., abandon, expiration, égalité) ?

### (d) Délai et cadence du compte à rebours (`4504`)

Le client affiche ce que le serveur lui envoie ; la durée du compte à rebours, sa cadence de
rafraîchissement et le comportement en cas d'absence de `4505` n'ont aucune source.

### (e) Texte des boîtes de message

Les id `1633`, `1636`, `1651` (refus) et `9234`, `9239`, `9245` (instances de jeu) sont identifiés
comme identifiants de ressources, mais leur **texte** n'a pas été extrait : `db_string.rdb` est binaire
(14 294 729 octets, en-tête « `20251207` / `Written by Archemedes v0.1.0` ») et la disposition
d'enregistrement n'a pas été confirmée (une lecture séquentielle naïve casse après le premier
enregistrement ; le champ de tête de l'enregistrement n'est pas identifié avec certitude). Question :
faut-il extraire ces textes pour nommer les huit codes, ou suffit-il des valeurs ?

### (f) Signification des codes `1`, `2` et `26` (`0x1a`)

Le client affiche la boîte 1633 pour `4500` avec les codes `1`, `2`, `26`, et pour `4502` avec le code
`1`. Ces valeurs existent dans `ResultCode.cs` mais **hors** de la famille compétition
(`Game/Network/Packets/ResultCode.cs:1-81`). Question : quels codes employer pour un refus générique ?

### (g) Résolution de la cible d'un `4500`

`requestee` est un **nom** de 31 octets, pas un handle. Le serveur n'a aucun registre de joueurs
visibles (`ConnectionInfo.cs:47-60`). Question : la cible doit-elle être cherchée par nom dans la
population connectée, et que répondre si plusieurs personnages portent le nom (ou si le joueur est
hors ligne) ?

### (h) Code exact pour chaque refus

Six des huit codes (61, 63, 64, 65, 67, 68) conviennent à un `4500`, cinq (61, 62, 64, 65, 68) à un
`4502`. Le choix dépend des conditions d'éligibilité, qui sont de la politique de jeu (§5.3).
Attention : `66` (`TargetNotInCompete`) n'a **aucune** boîte pour `4500`, et `63`/`66`/`67` n'en ont
aucune pour `4502` — un refus avec ces codes serait **silencieux**.

### (i) Politique de duel (interdiction d'inventer)

Durée de validité d'une invitation, temporisation du compte à rebours, conditions d'éligibilité
(niveau, zone, état), ce qui arrive au perdant, récompenses, classement : **aucune source**. Ces choix
appartiennent à Killian.

### (j) `handle_competitor` (`4504`)

`ar_handle_t` de 4 octets est lu tel quel (`0x671525`) et stocké dans le message interne. Question :
de quel objet est-ce le handle (le rival, son invocation, un objet d'arène) et le client s'en
sert-il pour résoudre quelque chose ?

### (k) `4501` : vers qui, et le demandeur reçoit-il quelque chose ?

Le client sait traiter `4501` et `4503` ; il n'existe aucune preuve que le demandeur reçoive un écho
(`4501` n'est pas routé vers « soi-même » de façon démontrée). Question : après un `4500` accepté, le
serveur envoie-t-il `4501` à la cible seule, `4503` au demandeur seul, ou les deux ?

### (l) Concordance avec le vocabulaire « Battle Arena » du client

Le client contient `request_compete`, `battle_start`, `battle_reject`, `battle_invite`,
`/battle_accept`, `BATTLE_START`, `BATTLE_INVITE`, `msgboxbattleaccept`, et `db_string.rdb` contient
des invitations « Fanatics / Champions team » (§2.2). La **liaison** entre ces textes et `4501` n'est
pas prouvée octet à octet. Question : cet habillage d'interface est-il celui de la compétition
7.3 (et non d'une fonctionnalité ultérieure recopiée dans le binaire) ?

### (m) Le lot C1 doit-il répondre quelque chose à un `4500` reçu ?

Le client n'affiche **rien** si le serveur ne répond pas (aucun état d'attente n'est observable côté
trame ; seul le drapeau interne `+0x45` de la fenêtre existe). Question : le socle doit-il répondre un
`TS_SC_RESULT(4500, code)` systématique (refus explicite, boîte affichée) ou rester silencieux
(comme le lot S1 du socle voisin, qui ne répond à rien) ?

---

## 8. Commits et binaires épinglés

| Référence | Identifiant | Usage |
|---|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | formats, tailles, ordre des champs, sémantique de `X(id, true)` |
| `reference/ngemity` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | déclarations (ids et structures) ; confirme l'absence de logique |
| `Navislamia` (base) | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | `GamePackets`, `ResultCode`, `TS_SC_RESULT`, `SendResult`, `ConnectionInfo`, `CombatService` |
| Fiche du socle voisin | branche `hermes/packet-socle-instances-jeu`, `docs/packet-specs/socle-instances-jeu.md` | famille 4250-4253 / HuntaHolic, aiguillage `TS_SC_RESULT` (nuance de lecture en §2.4) |
| Client Epic 7.3 | fichier de ressource `reference/client73/SFrame.exe` — **pas un dépôt git, donc aucun commit** : 9 841 664 octets, empreinte de fichier sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (empreinte de fichier, pas un commit de dépôt) | toutes les preuves `0x…` de cette fiche |

Méthode de lecture du client, reproductible : `objdump -d SFrame.exe` ; mapping des sections PE
(`.text` VA `0x401000` / offset `0x400`, `.rdata` VA `0xa0f000` / offset `0x60da00`, `.data` VA
`0xc10000` / offset `0x80e200`) pour les chaînes et les tables (`strings -t x`, `objdump -s`) ; noms
RTTI MSVC par `vtable-4 → COL → +0xC → type descriptor → +8` ; tables de sauts et tables d'octets du
dispatcher recopiées à la main depuis le `.text`. **Aucune exécution du client.**

---

## 9. Implémentation livrée — lot C1 (4500, 4502)

Branche `hermes/packet-socle-competition-joueurs`, commit `3c7aead`, base
`ec76b218cd0bd7c6498d725f253abb8b431f0cd6`. État vérifié après le commit : `dotnet build
Navislamia.sln -c Debug` → code 0, `dotnet test Tests/Tests.csproj` → code 0, **482 tests passés**
(448 avant, **34 ajoutés**).

| Fichier | Ce qui y est | Lignes |
|---|---|---|
| `Game/Network/Packets/Game/GameCompetePackets.cs` (nouveau) | offsets nommés, tailles, lecteurs `TryReadRequest` / `TryReadAnswer`, règle du NUL, `IsObservedAnswerType`, les deux constantes de code de refus | 131 |
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_COMPETE_REQUEST = 4500`, `TM_CS_COMPETE_ANSWER = 4502` | 99-103 |
| `Game/Network/Clients/GameClient.cs` | `HandleCompeteRequest`, `HandleCompeteAnswer` | 250-309 |
| `Game/Network/Clients/GameClient.cs` | les deux bras de dispatch, avant le `switch` final | 794-809 |
| `Tests/Game/CompetePacketsTests.cs` (nouveau) | 34 cas : tailles, offsets, ordre des champs, règle du NUL, longueurs refusées, codes de refus | 286 |

### 9.1 Décisions prises à l'implémentation (aucune n'est de la politique de jeu)

| # | Décision | Raison |
|---|---|---|
| C1-1 | Les **deux** membres ajoutés à `GamePackets` arrivent **avec** leurs deux bras de dispatch, dans le même commit | critère transversal n° 4 : un id déclaré sans bras atteint `_ => throw new Exception("Unknown Packet Type")` (`GameClient.cs:880`) et casse la boucle de réception |
| C1-2 | Les **cinq autres ids** (4501, 4503-4506) ne sont **pas** déclarés | ils voyagent serveur → client et ne sont pas encore émis : les déclarer sans bras de dispatch violerait C1-1. Ils arrivent avec les lots C2-C4 |
| C1-3 | Un `4500` bien formé est refusé par `TS_SC_RESULT(4500, 64)` = `NotInCompetablePlace` | code **affiché** par le client (boîte 1633, §2.4) qui ne présuppose rien sur la cible : le serveur ne reconnaît aucun lieu de compétition. `66`, sans boîte pour un 4500, est écarté ; le choix exact reste l'arbitrage (h) |
| C1-4 | Un `4502` bien formé est refusé par `TS_SC_RESULT(4502, 62)` = `NotInCompete` | vrai par construction — aucune compétition ne peut être en cours dans ce socle — et la boîte 1633 s'affiche pour un 4502 (§2.4) |
| C1-5 | Une trame **mal formée** (longueur ≠ 39 / ≠ 9, ou nom sans NUL dans les 31 octets) est journalisée **sans réponse** | §5.7 le prescrit pour la longueur anormale ; la fiche voisine (instances de jeu) ne répond à rien non plus. Aucun refus n'est inventé pour une trame que le client n'a pas construite |
| C1-6 | `answer_type` hors `{0, 1, 2}` : journalisé en `Warning`, puis refusé par le **même** code 62 | §7b : la sémantique n'est pas établie. Envoyer `1` — ce que propose §5.7 — ferait porter un refus par un code **sans nom** (§7f) ; voir la réserve n° 11 |
| C1-7 | `compete_type` n'est jamais validé, seulement journalisé | §5.7 : le client 7.3 recopie l'octet sans le tester (`0x67142f`), et son domaine n'est pas établi (§7a) |
| C1-8 | Le refus est **inconditionnel** et le serveur ne tient aucun état « en attente de réponse » | il ne dépend donc d'aucune condition d'éligibilité, c'est-à-dire d'aucune règle de jeu : il décrit l'état réel du serveur (aucun duel implémenté) |

Les deux codes sont des constantes de `GameCompetePackets` (`RequestRefusalCode`,
`AnswerRefusalCode`) : les trancher autrement plus tard est une édition d'une ligne, plus le test
`RefusalCodes_AreTheCodesTheClientDisplaysForEachFrame`.

### 9.2 Ce que le lot ne fait pas

- **aucun duel n'est jouable** : ni `4501` vers une cible, ni `4503` vers le demandeur, ni compte à
  rebours, ni fin de duel. La famille reste « reçue et refusée » ;
- **aucune résolution nom → joueur** : `requestee` est lu et journalisé, jamais recherché (§5.8) ;
- **aucun état de compétition** n'est stocké sur la `ConnectionInfo` ;
- les drapeaux `StateTimeType.EraseOnCompeteStart` / `NotActableInCompete` restent sans lecteur.

### 9.3 Contradiction relevée dans cette fiche (§5.7 contre §2.4)

§5.7 propose de refuser un `answer_type` hors domaine « par `TS_SC_RESULT(4502, 1)` (code sans
boîte) », alors que le tableau de §2.4 range explicitement le code `1` dans les codes **affichés**
pour un 4502 (boîte 1633, `0x477624`-`0x477627`). La lecture de §2.4 est la lecture directe de
l'aiguillage client et prime ; c'est une des raisons pour lesquelles aucun code sans nom n'est
employé.

---

## Bloc prêt à coller dans `CLAUDE.md`

> ### Socle compétition entre joueurs — 4500-4506 (`TM_CS/SC_COMPETE_*`)
>
> **Sept opcodes, tous `X(<id>, true)` chez rzu : aucun gating de version, aucun champ gaté.**
> `true` n'est pas une convention « valide partout » mais la **condition C++ littérale** substituée
> dans `if(condition_) id = id_;` (`PacketDeclaration.h:585-588`) : les sept ids sont donc identiques
> en 7.3 et dans toutes les versions. Fiche complète : `docs/packet-specs/socle-competition-joueurs.md`.
>
> **Tailles à écrire en dur** (source : rzu + constructeurs et lecteurs du client 7.3) :
> **4500 = 39**, 4501 = 39, **4502 = 9**, 4503 = 40, **4504 = 43**, 4505 = 39, **4506 = 71** octets.
> Les chaînes de `requestee` / `requester` / `competitor` / `winner` / `loser` sont des tampons
> **fixes de 31 octets** (NUL compris) ; le handle de 4504 est à l'offset **39** et le second nom de
> 4506 à l'offset **40** — ce sont les deux preuves indépendantes de la largeur 31.
>
> **Direction : le client route 4501, 4503, 4504, 4505 et 4506, et ne route NI 4500 NI 4502** (ils
> tombent dans le journal « message non traité » du dispatcher entrant). Le serveur ne doit jamais
> émettre ces deux ids ; il les **reçoit**.
>
> **Ce que le joueur fait** : `4500` part du contrôle de fenêtre `request_compete` (nom de la cible,
> `compete_type = 0`), `4502` des contrôles `battle_start` (`answer_type = 0`) et `battle_reject`
> (`answer_type = 1`), plus une branche par défaut (`answer_type = 2`).
>
> **Refus** : par `TS_SC_RESULT` (id 0) avec `RequestMsgID = 4500` ou `4502`. Navislamia a déjà la
> trame (`TS_SC_RESULT.cs`, `ushort + ushort + int` = 15 octets) et l'émetteur
> (`GameClient.SendResult`). Les codes de la famille sont déjà déclarés sans lecteur :
> `ResultCode.cs:74-81` (61-68). Attention : `66` n'a **aucune** boîte pour 4500, et `63`/`66`/`67`
> aucune pour 4502 — le refus serait silencieux ; la correspondance existe en `0x4774dd`-`0x4776f7`.
>
> **Ne pas porter NGemity** : les sept ids et structures y sont déclarés et **jamais traités**
> (`Chihiro` n'a que `CRT_COMPETE`, `AF_ERASE_ON_COMPETE_START`, `AF_NOT_ACTABLE_IN_COMPETE`,
> `REVIVE_COMPETE`). `librzu` non plus. C'est du protocole pur, comme le socle instances de jeu.
>
> **Socle minimum (C1), livré** : `4500` et `4502` sont lus, validés et refusés — 39 octets exigés
> pour le premier avec un nom NUL-terminé dans ses 31 octets, 9 pour le second ; une trame mal
> formée est journalisée sans réponse. Le refus part par `TS_SC_RESULT(4500, 64)`
> (`NotInCompetablePlace`) et `TS_SC_RESULT(4502, 62)` (`NotInCompete`), deux codes que le client
> **affiche** (boîte 1633) : le socle ne joue aucun duel, donc il énonce son état réel et n'invente
> aucune règle. Les deux ids sont déclarés dans `GamePackets` **et** routés dans `GameClient.cs`
> (critère transversal n° 4) ; les cinq ids serveur → client (4501, 4503-4506) ne sont pas déclarés
> tant qu'ils ne sont pas émis. La réussite (4501/4503) exige un registre de joueurs visibles,
> absent (`ConnectionInfo.cs:47-60`, `SkillCastService.cs:284-287`, `CombatService.cs:42-50`) — d'où
> la frontière avec le socle PK 800/801, qui partage ce prérequis sans partager d'opcode. Découpage
> C1…C4 : §5.5 de la fiche ; décisions d'implémentation : §9.1.
>
> **Non tranché** : `compete_type` (seule valeur observée 0, jamais validé), `answer_type` (0/1/2,
> hors domaine journalisé puis refusé par le code 62), `end_type`, durée du compte à rebours,
> `handle_competitor`, politique de duel. Aucune de ces valeurs n'est devinée. Le choix des deux
> codes de refus et l'opportunité de répondre à un `4500` (§7m) restent l'arbitrage de Killian, et
> les deux sont des constantes d'une ligne.

---

## A VERIFIER PAR KILLIAN

1. **Sémantique de `compete_type`, `answer_type` et `end_type`** (§7a, §7b, §7c) : aucune
   énumération n'existe dans rzu ni NGemity, seules des valeurs observées. Faut-il les traiter
   comme opaques (les recopier sans les interpréter) ou définir leur domaine ?
2. **Le lot C1 répond-il quelque chose à un `4500` reçu ?** (§7m) Le client n'affiche rien si le
   serveur se tait ; le socle voisin (instances de jeu) ne répond à rien sur ses deux trames. Refus
   explicite par `TS_SC_RESULT`, ou silence ?
3. **Quel code exact pour chaque refus** (§7h, §7f) : six codes conviennent à `4500`, cinq à `4502`,
   et certains sont **silencieux** côté client (`66` pour 4500 ; `63`, `66`, `67` pour 4502). Le socle
   doit-il n'utiliser que les codes qui affichent une boîte ? Et quels codes employer pour un refus
   générique (`1`, `2`, `26` sont testés par le client mais n'ont pas de nom dans la famille) ?
4. **Politique de duel** (§7i, §7d) : durée de l'invitation, temporisation du compte à rebours et sa
   cadence, éligibilité, ce qui arrive au perdant, récompenses, classement. Rien n'est sourçable :
   c'est un choix de contenu, et il conditionne C3/C4.
5. **Résolution de la cible par nom** (§7g) : `requestee` est un nom, pas un handle, et le serveur
   n'a aucun registre de joueurs visibles. Le socle C2 dépend-il du socle PK 800/801 (ou d'un socle
   « registre de joueurs » à créer) ?
6. **Texte des boîtes 1633 / 1636 / 1651** (§7e) : l'extraction de `db_string.rdb` n'a pas abouti
   (format d'enregistrement non confirmé). Ces textes sont-ils nécessaires pour nommer les codes,
   ou les valeurs suffisent-elles ?
7. **Habillage « Battle Arena »** (§7l) : les chaînes `battle_start` / `battle_reject` /
   `msgboxbattleaccept` et les invitations d'équipe Fanatics/Champions du client appartiennent-elles
   bien à cette famille, ou à une fonctionnalité ultérieure présente dans le binaire ?
8. **Frontière avec la carte « PK et combat joueur contre joueur » (800/801)**
   (`https://trello.com/c/BY6qivuo` et `https://trello.com/c/zNSZ9eX3`) : la fiche conclut à deux
   socles **complémentaires** partageant le prérequis « registre de joueurs » (§5.8). Faut-il créer
   ce prérequis comme carte propre, ou le rattacher à l'un des deux socles ?
9. **`handle_competitor` de `4504`** (§7j) : le client lit 4 octets à l'offset 39 et les range dans
   son message interne sans autre usage observable. Handle du rival, de son invocation, ou d'un objet
   d'arène ? Le lot C3 ne peut pas être écrit sans cette réponse.
10. **À qui le serveur envoie-t-il `4501`** (§7k) : à la cible seule, au demandeur seul (écho), ou aux
    deux ? Aucune preuve dans le binaire : le client traite `4501` et `4503` sans indiquer lequel
    revient au demandeur.
11. **Les deux codes de refus retenus pour le lot C1** (§9.1, C1-3 et C1-4) : `64`
    (`NotInCompetablePlace`, boîte 1633) pour un `4500` et `62` (`NotInCompete`, boîte 1633) pour un
    `4502`. Ils décrivent l'état réel du serveur — aucun duel, aucun lieu de compétition — et sont
    échangés en une ligne s'il faut un autre code. Un `answer_type` hors `{0,1,2}` reçoit le même
    code 62 et non le code `1` que propose §5.7, dont la sémantique n'a pas de nom (§7f). À
    confirmer, ou à remplacer par le silence de la question 2.
12. **Une trame mal formée est journalisée sans réponse** (§9.1, C1-5) : longueur autre que 39/9, ou
    nom de `4500` sans NUL dans ses 31 octets. Choix cohérent avec §5.7 et avec le socle voisin
    (instances de jeu), mais il laisse le drapeau « en attente » du client armé si le client
    lui-même émettait un jour une telle trame. Faut-il répondre plutôt que journaliser ?
