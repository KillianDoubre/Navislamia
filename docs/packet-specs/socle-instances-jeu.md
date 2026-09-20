# Socle « instances de jeu » — `TM_CS_INSTANCE_GAME_*` (4250-4253) et famille HuntaHolic (4000-4012)

Fiche d'archéologie de protocole couvrant **17 opcodes contigus** : `TM_CS/SC_INSTANCE_GAME_*`
(4250-4253) et la famille `TM_CS/SC_HUNTAHOLIC_*` (4000-4012). Elle fournit l'archéologie, les
tailles, le gating de version et un découpage en paquets pour `navis-dev`. **Aucun code serveur
n'est écrit ici.**

Méthode : rzu tranche les tailles, l'ordre des champs et le gating ; NGemity tranche la logique ;
le client Epic 7.3 tranche en dernier ressort. Les trois ont été lus localement, et le client a été
désassemblé (`objdump -d -M intel`) pour confronter chaque taille.

## Questions tranchées par cette fiche

| Question | Verdict | Où |
|---|---|---|
| Le client 7.3 construit-il 4250/4251/4252 ? | **Oui**, avec les tailles 11 / 7 / 7 octets — trois constructeurs distincts (`0x48c490`, `0x48c4e0`, `0x48c530`) | §2, §3.2 |
| Et 4253 ? | **Oui, reçu** : la classe `USMSG_INSTANCE_GAME_SCORE` existe et son désérialiseur lit 16 octets en charge utile — mais la constante `4253` n'apparaît **nulle part** dans le code du client (§7b) | §3.2.4, §7b |
| Quelles valeurs pour `instance_game_type` ? | **0, 1 et 2** observées dans le client ; la valeur envoyée est recopiée d'un champ du message entrant déclencheur, donc le **serveur** la choisit | §2.1, §7c |
| Tailles exactes des 17 opcodes ? | Table complète en §3 ; **7 opcodes confirmés par deux sources indépendantes** (rzu et constructeur client), 9 confirmés par rzu seul, 4008 sans preuve client | §1, §3 |
| Le client 7.3 connaît-il toute la famille ? | **Oui** : 7 constructeurs/lecteurs prouvés par démontage, 6 classes UI `SUIHuntaHolic*`, 6 classes `USMSG_HUNTAHOLIC_*` (RTTI), le chargeur `db_HuntaHolicResource.rdb` | §2, §3, §5.1 |
| NGemity a-t-il une logique à porter ? | **Non** : les 17 opcodes y sont déclarés et jamais traités ; le bloc `INSTANCE_GAME_ENTER`/`WARP_TO_HUNTAHOLIC_LOBBY`/`INSTANCE_GAME_EXIT` de `Skill.cpp` est entièrement commenté | §5.1, §6 |
| Socle minimum implémentable ? | **§5.3** : 4 opcodes sans état (4250/4251/4252/4253) d'abord, puis le lobby 4000/4001, puis 4009/4010/4012 | §5.3, §5.4 |
| Où en est Navislamia ? | **Rien n'est implémenté** : ni les ids dans `GamePackets`, ni un handler, ni les 17 opcodes nulle part. Le socle social (entités, enums, colonnes) existe en revanche déjà | §1, §5.3 |

---

## 1. Identité

### 1.1 Instances de jeu — `TM_CS/SC_INSTANCE_GAME_*` (4250-4253)

| Id | Nom | Sens | Taille 7.3 | Sources |
|---|---|---|---|---|
| **4250** | `TM_CS_INSTANCE_GAME_ENTER` | client → serveur | **11 o** | `op_codes.md:240` ; rzu `TS_CS_INSTANCE_GAME_ENTER.h:8,12` ; NGemity `ClientPackets.h:246` ; client `0x48c490` |
| **4251** | `TM_CS_INSTANCE_GAME_EXIT` | client → serveur | **7 o** | `op_codes.md:241` ; rzu `TS_CS_INSTANCE_GAME_EXIT.h:11` ; NGemity `ClientPackets.h:247` ; client `0x48c4e0` |
| **4252** | `TM_CS_INSTANCE_GAME_SCORE_REQUEST` | client → serveur | **7 o** | `op_codes.md:242` ; rzu `TS_CS_INSTANCE_GAME_SCORE_REQUEST.h:11` ; NGemity `ClientPackets.h:248` ; client `0x48c530` |
| **4253** | `TM_SC_INSTANCE_GAME_SCORE_REQUEST` | serveur → client | **23 o** | `op_codes.md:243` ; rzu `TS_SC_INSTANCE_GAME_SCORE_REQUEST.h:8-20` ; NGemity `ClientPackets.h:249` ; client `0x670bb0` (lecture) |

### 1.2 Famille HuntaHolic (4000-4012)

| Id | Nom | Sens | Taille 7.3 | Sources |
|---|---|---|---|---|
| **4000** | `TM_CS_HUNTAHOLIC_INSTANCE_LIST` | client → serveur | **11 o** | `op_codes.md:226` ; rzu `TS_CS_HUNTAHOLIC_INSTANCE_LIST.h:8,11` ; NGemity `ClientPackets.h:233` ; client `0x4c91f0` |
| **4001** | `TM_SC_HUNTAHOLIC_INSTANCE_LIST` | serveur → client | **23 + 38·N o** | `op_codes.md:227` ; rzu `TS_SC_HUNTAHOLIC_INSTANCE_LIST.h:8-16` ; NGemity `ClientPackets.h:234` ; client `0x6707f0` |
| **4002** | `TM_SC_HUNTAHOLIC_INSTANCE_INFO` | serveur → client | **45 o** | `op_codes.md:228` ; rzu `TS_SC_HUNTAHOLIC_INSTANCE_INFO.h:5-19` ; NGemity `ClientPackets.h:235` ; client `0x6708a0` |
| **4003** | `TM_CS_HUNTAHOLIC_CREATE_INSTANCE` | client → serveur | **56 o** | `op_codes.md:229` ; rzu `TS_CS_HUNTAHOLIC_CREATE_INSTANCE.h:5-11` ; NGemity `ClientPackets.h:236` ; client `0x563c20` |
| **4004** | `TM_CS_HUNTAHOLIC_JOIN_INSTANCE` | client → serveur | **28 o** | `op_codes.md:230` ; rzu `TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:5-10` ; NGemity `ClientPackets.h:237` ; client `0x4c9190` |
| **4005** | `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE` | client → serveur | **7 o** | `op_codes.md:231` ; rzu `TS_CS_HUNTAHOLIC_LEAVE_INSTANCE.h:7` ; NGemity `ClientPackets.h:238` ; client `0x4c9380` |
| **4006** | `TM_SC_HUNTAHOLIC_HUNTING_SCORE` | serveur → client | **48 o** | `op_codes.md:232` ; rzu `TS_SC_HUNTAHOLIC_HUNTING_SCORE.h:5-17` ; NGemity `ClientPackets.h:239` ; client `0x670910` |
| **4007** | `TM_SC_HUNTAHOLIC_UPDATE_SCORE` | serveur → client | **15 o** | `op_codes.md:233` ; rzu `TS_SC_HUNTAHOLIC_UPDATE_SCORE.h:5-10` ; NGemity `ClientPackets.h:240` ; client `0x6709a0` |
| **4008** | `TM_CS_HUNTAHOLIC_LEAVE_LOBBY` | client → serveur | **7 o** (rzu seul) | `op_codes.md:234` ; rzu `TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:8` ; NGemity `ClientPackets.h:241` ; **aucune trace client** (§7d) |
| **4009** | `TM_SC_HUNTAHOLIC_BEGIN_HUNTING` | serveur → client | **11 o** | `op_codes.md:235` ; rzu `TS_SC_HUNTAHOLIC_BEGIN_HUNTING.h:5-9` ; NGemity `ClientPackets.h:242` ; client `0x670a00` |
| **4010** | `TM_SC_HUNTAHOLIC_MAX_POINT_ACHIEVED` | serveur → client | **7 o** | `op_codes.md:236` ; rzu `TS_SC_HUNTAHOLIC_MAX_POINT_ACHIEVED.h:8` ; NGemity `ClientPackets.h:243` ; client `0x670b10` (déduction, §3.4.6) |
| **4011** | `TM_CS_HUNTAHOLIC_BEGIN_HUNTING` | client → serveur | **7 o** | `op_codes.md:237` ; rzu `TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:9` ; NGemity `ClientPackets.h:244` ; client `0x4c93d0` |
| **4012** | `TM_SC_HUNTAHOLIC_BEGIN_COUNTDOWN` | serveur → client | **7 o** | `op_codes.md:238` ; rzu `TS_SC_HUNTAHOLIC_BEGIN_COUNTDOWN.h:9` ; NGemity `ClientPackets.h:245` ; client `0x670b60` |

### 1.3 Ce que Navislamia a déjà, et ce qui manque

| Élément | État | Source |
|---|---|---|
| Ids 4000-4012 et 4250-4253 dans `GamePackets` | **absents** | `Game/Network/Packets/Enums/GamePackets.cs` (102 lignes, aucun membre au-delà de 8000) ; `grep -rn "4250\|4251\|4252\|4253" --include=*.cs .` → 0 résultat |
| Handler pour l'un des 17 | **absent** | `Game/Network/Clients/GameClient.cs:555-809` |
| Chaîne de dispatch finale | `IPacket msg = header.ID switch { … _ => throw new Exception("Unknown Packet Type") }` — tout id non listé **lève** | `Game/Network/Clients/GameClient.cs:791-803` (cf. critère d'acceptation n° 4) |
| Ressources HuntaHolic (serveur) | **tables présentes** : `HuntaholicResource`, `HuntaholicInstanceResource`, `HuntaholicMonsterRespawnResource`, `HuntaholicHealingPropResource`, `InstanceDungeonResource`, `InstanceDungeonTypeResource`, `InstanceDungeonMonsterRespawnResource`, `InstanceDungeonHealingPropResource` | `ArcadiaSchemaPSQL.sql:343, 358, 374, 384, 1884, 1913, 2014, 2029` |
| Score de personnage | `CharacterEntity.HuntaholicPoint` et `CharacterEntity.HuntaholicEnterCount` **existent** (contexte Telecaster) | `Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:51-52` |
| Enums liés | `PartyType.HuntaholicParty = 3` ; `StateTimeType.EraseOnQuitHuntaholic = 256` ; `ItemEffectInstant.IncHuntaholicPoint = 104` ; `ItemGeneratedBy.Huntaholic = 14` ; `ItemUseFlag.CantUseInHuntaholic = 22`, `UsableInOnlyHuntaholic = 23` | `Game/DataAccess/Entities/Enums/*.cs` (lignes respectives 9, 17, 24, 18, 30, 31) |
| Colonnes de ressource | `ItemResourceEntity.HuntaholicPoint` ; `MarketResource.huntaholic_ratio` | `ItemResourceEntity.cs:35` ; `ArcadiaSchemaPSQL.sql:468` |

Autrement dit : **la couche sociale existe déjà** (points de personnage, enums, tables de
ressources), seule la couche protocole manque.

---

## 2. Ce que le joueur fait pour que le client envoie le paquet

Toutes les adresses ci-dessous sont des adresses virtuelles du `.text` du binaire
`reference/client73/SFrame.exe` (sha256 `41e0af2e…`, §8). Rappel de la convention de citation
établie par les fiches précédentes : `0x…` = VA dans le désassemblage `objdump -d -M intel`.

### 2.1 `4250` / `4251` — entrée et sortie d'instance de jeu

Ces deux paquets ne partent **pas** d'un clic isolé : le client les envoie en **réponse à un
message reçu**, dans le même gestionnaire.

| Fait | Preuve |
|---|---|
| Le constructeur de 4250 (`0x48c490`, `Length = 0x0b`) est appelé en `0x49e462` sur un tampon local (`lea ecx,[ebp-0x18]`), puis la charge utile est remplie depuis le message reçu : `mov eax,[edi+0x13]` → `mov DWORD PTR [ebp-0x11],eax` (offset 7 du paquet), puis envoi par l'appel virtuel `[vtable+0xc4]` | client `0x49e45f`-`0x49e48b` |
| Le constructeur de 4251 (`0x48c4e0`, `Length = 0x07`) est appelé en `0x49e493` (`lea ecx,[ebp-0x14]`) puis envoyé par le même appel virtuel `[vtable+0xc4]`, sans charge utile | client `0x49e490`-`0x49e4b6` |
| Les deux cas appartiennent à une table de saut du gestionnaire de messages (table d'octets en `0x49ea50`, table de sauts en `0x49e98c`, plage d'ids d'entrée 1027-1249, `sub eax,0x403` en `0x49e21d`) | client `0x49e21d`-`0x49e234` |
| L'interface construit par ailleurs des messages **internes** « entrée » et « sortie » d'instance : `USMSG_INSTANCE_GAME_ENTER` (id interne 1201) et `USMSG_INSTANCE_GAME_EXIT` (id interne 1202) | classes RTTI `.?AUSMSG_INSTANCE_GAME_ENTER@@`, `.?AUSMSG_INSTANCE_GAME_EXIT@@` ; constructeurs `0x56a310`, `0x564a30` |
| Le client **traite aussi un message entrant d'id 4250** : dispatch `sub eax,0x109a` en `0x4775ec` → corps `0x4776a7` (4500 → corps `0x477633`, 4502 → corps `0x477607` par défaut). Le corps de 4250 lit un champ de 16 bits en `[edi+0x15]` et affiche une des trois boîtes de message selon sa valeur : 10 → chaîne 9234, 56 ou 72 → 9239, 78 → 9245, toute autre valeur → rien | client `0x4775ec`-`0x4776ed` |
| Le même triplet d'ids revient dans un **journal de débogage** : `sub eax,0xfa3` (4003), puis 4004, puis `sub eax,0xf6` → 4250, chacun associé à une chaîne coréenne terminée par `%s[%d]` (p. ex. `0xa5245c` pour 4003, `0xa524ac` pour 4250) | client `0x66e0df`-`0x66e13b` ; chaînes `.rdata` `0xa5245c`, `0xa52488`, `0xa524ac` |

**Valeurs observées de `instance_game_type`** (messages internes d'entrée, toutes recopiées dans la
charge utile de 4250 par le chemin ci-dessus) :

| Valeur | Site d'appel | Contexte du code |
|---|---|---|
| **0** | `0x5b1b9d` | `push ebx` avec `ebx = 0`, dans un test `cmp BYTE PTR [eax+0xc],bl` (l'instance n'est pas encore ouverte) |
| **1** | `0x64247e` | branche `cmp DWORD PTR [esi+0x20],0` … `push 0x01` immédiatement avant l'appel |
| **2** | `0x64244b` | branche voisine, `push 0x02` immédiatement avant l'appel |

Aucune autre valeur n'a été observée. `0` doit donc être considéré comme une valeur **légitime**
(« entrée générique »), et non comme un « pas de type ».

### 2.2 `4252` — demande d'affichage des scores

Le message interne `USMSG_INSTANCE_GAME_SCORE_REQUEST` (id interne **164**) est construit en
`0x56a340` avec une charge utile d'**un octet** (un booléen : ouvert/fermé, cf. `push 0` en
`0x618824` et `0x618a84`) ; il est produit par le tableau de scores. Le paquet 4252 lui-même
(constructeur `0x48c530`, `Length = 0x07`, aucune charge utile) est le message sans argument envoyé
par le même chemin que 4251.

### 2.3 Famille HuntaHolic — ce qui déclenche chaque envoi

| Id | Déclencheur | Preuve client |
|---|---|---|
| `4000` | Ouverture du lobby HuntaHolic et changement de page de la liste. Le constructeur est une méthode de `SHuntaHolicSystem` (objet porté par `ecx`, session en `[ecx+0x20]`) qui prend la **page** en argument (`[ebp+0x8]`) | constructeur `0x4c91f0` ; 3 sites d'appel `0x4c97f8`, `0x564d7f`, `0x565c84` |
| `4003` | Fenêtre « créer une instance » (`SUIHuntaHolicCreateInstanceWnd`) : nom (30 car. max), effectif max., mot de passe (16 car. max, fenêtre `SUIHuntaHolicConfirmPasswordWnd`) | constructeur `0x563c20` (`push 0x38`, `memset` de 56 octets, `mov DWORD PTR [esi],0x38`) |
| `4004` | Fenêtre « rejoindre » (`SUIHuntaHolicLobbyWnd` → `SUIHuntaHolicConfirmPasswordWnd` si `require_password`) | constructeur `0x4c9190` (`Length = 0x1c`) ; 2 sites d'appel `0x4c933c`, `0x4c945a` |
| `4005` | Bouton « quitter l'instance » (ou sortie de l'instance) | constructeur `0x4c9380` (`Length = 0x07`) ; 2 sites d'appel `0x5641f2`, `0x567453` |
| `4011` | Bouton « commencer la chasse » du lobby | constructeur `0x4c93d0` (`Length = 0x07`) ; 1 site d'appel `0x564238` |
| `4008` | **aucune trace** : ni constructeur, ni référence à `4008` (0xfa8) dans le code du client | §7d |

Les fenêtres correspondantes sont attestées dans le binaire :
`Create: SUIHuntaHolicLobbyWnd` (chaîne .rdata `0xa49898`, poussée en `0x6364e3`),
`Create: SUIHuntaHolicInstanceWnd` (`0xa49820`, `0x636690`),
`Create: SUIHuntaHolicCreateInstanceWnd`, `Create: SUIHuntaHolicConfirmPasswordWnd`,
`Create: SUIHuntaHolicResultWnd`, `Create: SUIHuntaHolicScoreBoardWnd`, `SHuntaHolicSystem`.

Textes d'interface (`strings -n 4 SFrame.exe`, §8) : `msgboxHuntaHolicMaxPoint` (l. 24673),
`msgboxHuntaHolicScoreBoard` (l. 24674), `huntaholic_scoreboard` (l. 21759),
`huntaholic_have_aquired_max_points` (l. 20360), `text_holic_num` (l. 24280),
`huntaholicpoint` (l. 26430), `huntaholic_ent` (l. 26247) — **les deux derniers sont les propriétés
de personnage**, identiques à celles de rzu `rzgame` (voir §5.1).

---

## 3. Structure sur le fil

### 3.1 En-tête commun (7 octets)

| Offset | Type | Nom | Source |
|---|---|---|---|
| 0 | `uint32` LE | `Length` — longueur **totale** du paquet, en-tête compris | `Game/Network/Packets/Header.cs` ; convention confirmée par les 7 constructeurs clients ci-dessous |
| 4 | `uint16` LE | `ID` | idem |
| 6 | `uint8` | `Checksum` — somme des octets 0…5, modulo 256 | boucles de somme chez le client (p. ex. `0x4c9220`-`0x4c922a`) ; côté serveur `Game/Network/Packets/PacketExtensions.cs:13` |

Toutes les tailles totales annoncées ci-dessous incluent ces 7 octets. Les constructeurs clients
sont cités avec leur valeur de `Length` : c'est la preuve la plus directe, le client écrivant la
taille en dur dans le tampon.

### 3.2 `TM_CS/SC_INSTANCE_GAME_*` (4250-4253)

#### 3.2.1 `TM_CS_INSTANCE_GAME_ENTER` (4250) — 11 octets

| Offset | Type | Nom | Source |
|---|---|---|---|
| 7 | `int32` LE | `instance_game_type` | rzu `TS_CS_INSTANCE_GAME_ENTER.h:8` |

Preuve client : constructeur `0x48c490` — `mov DWORD PTR [eax],0xb` en `0x48c4c4` (11 octets), id
`mov edx,0x109a` en `0x48c4b9`, tampon de 11 octets mis à zéro jusqu'à `[eax+0xa]`. NGemity est
identique (`shared/Server/Packets/GameClient/TS_CS_INSTANCE_GAME_ENTER.h:7`).

#### 3.2.2 `TM_CS_INSTANCE_GAME_EXIT` (4251) — 7 octets

Aucune charge utile. rzu `TS_CS_INSTANCE_GAME_EXIT.h:7` (bloc `_DEF(_)` vide). Preuve client :
constructeur `0x48c4e0`, `mov DWORD PTR [eax],0x7` en `0x48c50d`, id `0x109b` en `0x48c502`.

#### 3.2.3 `TM_CS_INSTANCE_GAME_SCORE_REQUEST` (4252) — 7 octets

Aucune charge utile. rzu `TS_CS_INSTANCE_GAME_SCORE_REQUEST.h:7`. Preuve client : constructeur
`0x48c530`, `mov DWORD PTR [eax],0x7` en `0x48c55d`, id `0x109c` en `0x48c552`.

#### 3.2.4 `TM_SC_INSTANCE_GAME_SCORE_REQUEST` (4253) — 23 octets

| Offset | Type | Nom | Source |
|---|---|---|---|
| 7 | `uint32` LE | `holicpoint` | rzu `TS_SC_INSTANCE_GAME_SCORE_REQUEST.h:8` |
| 11 | `uint32` LE | `bearroad_ranking` | rzu idem `:9` |
| 15 | `uint32` LE | `deathmatch_kill_count` | rzu idem `:10` |
| 19 | `uint32` LE | `deathmatch_death_count` | rzu idem `:11` |

Preuve client : le désérialiseur `0x670bb0` alloue un objet de **35 octets** (`push 0x23` en
`0x670bb5`) = 19 octets d'entête d'objet + 16 octets de charge utile, installe la vtable
`0xa52130` (classe RTTI `.?AUSMSG_INSTANCE_GAME_SCORE@@`) et recopie quatre `DWORD` lus aux offsets
`[ecx+0x7]`, `[ecx+0xb]`, `[ecx+0xf]`, `[ecx+0x13]` (instructions `0x670bd6`, `0x670bd3`, `0x670bcf`,
`0x670bcb`).
**Le client 7.3 lit donc bien 16 octets de charge utile, pas 48** (voir gating §4).

### 3.3 Famille HuntaHolic — client → serveur

#### 3.3.1 `TM_CS_HUNTAHOLIC_INSTANCE_LIST` (4000) — 11 octets

| Offset | Type | Nom | Source |
|---|---|---|---|
| 7 | `int32` LE | `page` | rzu `TS_CS_HUNTAHOLIC_INSTANCE_LIST.h:8` |

Preuve client : constructeur `0x4c91f0` — `mov DWORD PTR [ebp-0xc],0xb` en `0x4c920e`,
`mov eax,0xfa0` / `mov WORD PTR [ebp-0x8],ax` en `0x4c9205`/`0x4c920a`, et écriture de l'argument
(`mov edx,[ebp+0x8]` → `mov DWORD PTR [ebp-0x5],edx` en `0x4c9237`, soit l'offset 7). L'argument
est la **page** demandée ; c'est le seul paquet de la famille dont la charge utile est un unique
entier.

#### 3.3.2 `TM_CS_HUNTAHOLIC_CREATE_INSTANCE` (4003) — 56 octets

| Offset | Type | Nom | Source |
|---|---|---|---|
| 7 | `char[31]` (NUL final) | `name` | rzu `TS_CS_HUNTAHOLIC_CREATE_INSTANCE.h:6` |
| 38 | `int8` | `max_member_count` | rzu idem `:7` |
| 39 | `char[17]` (NUL final) | `password` | rzu idem `:8` |

Preuve client : constructeur `0x563c20` — `push 0x38` (56) avant `memset` en `0x563ca0`, id
`mov eax,0xfa3` en `0x563c48`, `mov DWORD PTR [esi],0x38` en `0x563c56`. 31 + 1 + 17 = 49, plus
7 = **56** : les deux sources concordent.

Les tailles de chaîne sont des tailles de **tampon** : le client écrit exactement 31 et 17 octets,
chaîne tronquée à 30/16 caractères plus le NUL (sémantique `MessageBuffer::writeString`,
`reference/rzu/librzu/src/lib/Packet/MessageBuffer.cpp:87-94`).

#### 3.3.3 `TM_CS_HUNTAHOLIC_JOIN_INSTANCE` (4004) — 28 octets

| Offset | Type | Nom | Source |
|---|---|---|---|
| 7 | `int32` LE | `instance_no` | rzu `TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:6` |
| 11 | `char[17]` (NUL final) | `password` | rzu idem `:7` |

Preuve client : constructeur `0x4c9190` — `mov ecx,0xfa4` en `0x4c91c1`, `mov WORD PTR [eax+0x4],cx`
en `0x4c91c6`, `mov DWORD PTR [eax],0x1c` en `0x4c91cc`, tampon mis à zéro jusqu'à `[eax+0x1b]`
(« six dwords à partir de `[eax+0x4]` ») : 4 + 17 = 21, plus 7 = **28**. Concordance exacte.

#### 3.3.4 `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE` (4005) — 7 octets

Aucune charge utile (rzu `TS_CS_HUNTAHOLIC_LEAVE_INSTANCE.h:7`). Preuve client : `0x4c9380` —
`mov eax,0xfa5` en `0x4c9392`, `mov DWORD PTR [ebp-0x7],0x7` en `0x4c939b`.

#### 3.3.5 `TM_CS_HUNTAHOLIC_LEAVE_LOBBY` (4008) — 7 octets (rzu seul)

Aucune charge utile (rzu `TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:8`). **Aucune preuve client** : le seul
octet `0xfa8` du binaire est un déplacement de pile (`0x67902f`, `mov DWORD PTR [ebp-0xfa8],ebx`).
Voir §7d.

#### 3.3.6 `TM_CS_HUNTAHOLIC_BEGIN_HUNTING` (4011) — 7 octets

Aucune charge utile (rzu `TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:9`). Preuve client : `0x4c93d0` —
`mov eax,0xfab` en `0x4c93e2`, `mov DWORD PTR [ebp-0x7],0x7` en `0x4c93eb`.

### 3.4 Famille HuntaHolic — serveur → client

Pour ces paquets, la preuve client est un **désérialiseur** : une fonction qui alloue un objet
message (`push <taille d'objet>` → `call 0x97671b`), installe la vtable RTTI de la classe, puis
recopie la charge utile depuis le tampon brut reçu. La taille de l'objet vaut **19 octets +
charge utile** quand la charge utile est fixe, ce qui donne la taille de champ par champ.

> **Réserve de méthode.** L'appariement vtable ↔ opcode est établi par **identité de structure** et
> par le fait que les désérialiseurs sont rangés dans la table par ordre croissant d'id interne
> (126, 127, 128, 129, 130, 131, 163, 1203). Le client ne compare **jamais** les constantes
> `4006`, `4007`, `4009`, `4010`, `4012`, `4253` : la table de routage id ↔ handler est en `.data`,
> qui n'est pas désassemblé par `objdump -d` (§7e).

#### 3.4.1 `TM_SC_HUNTAHOLIC_INSTANCE_LIST` (4001) — 23 + 38·N octets

| Offset | Type | Nom | Source |
|---|---|---|---|
| 7 | `int32` LE | `huntaholic_id` | rzu `TS_SC_HUNTAHOLIC_INSTANCE_LIST.h:9` |
| 11 | `int32` LE | `page` | rzu idem `:10` |
| 15 | `int32` LE | `infos` — **nombre** d'entrées qui suivent | rzu idem `:11` (`_(count)`) |
| 19 | `int32` LE | `total_page` | rzu idem `:12` |
| 23 | `TS_HUNTAHOLIC_INSTANCE_INFO[infos]` | tableau, pas de 38 octets | rzu idem `:13` |

Preuve client `0x6707f0` : vtable `0xa52100` (`.?AUSMSG_HUNTAHOLIC_INSTANCE_LIST@@`), objet de
**39 octets** (`push 0x27` en `0x6707f6`) = 19 + 16 + 4 (le pointeur du tableau), quatre `DWORD` lus
aux offsets `[edi+0x7]`, `[edi+0xb]`, `[edi+0xf]`, `[edi+0x13]` (instructions `0x670832`, `0x670838`,
`0x67083e`, `0x670844`, rangés dans l'objet en `+0x13`, `+0x17`, `+0x1b`, `+0x1f`), puis le tableau :
`mov edx,0x26` en `0x670850` et `imul ecx,ecx,0x26` en `0x670867` (`0x26` = **38 octets**, pas du
tableau), source
placée en 23 par `add edi,0x17` en `0x67086b`, recopie par `call 0x9767c0` en `0x670873`. Taille
totale = 7 + 16 + 38·N = **23 + 38·N**.

#### 3.4.2 `TS_HUNTAHOLIC_INSTANCE_INFO` (élément) — 38 octets

| Offset relatif | Type | Nom | Source |
|---|---|---|---|
| 0 | `int32` LE | `instance_no` | rzu `TS_SC_HUNTAHOLIC_INSTANCE_INFO.h:6` |
| 4 | `char[31]` (NUL final) | `name` | rzu idem `:7` |
| 35 | `int8` | `current_member_count` | rzu idem `:8` |
| 36 | `int8` | `max_member_count` | rzu idem `:9` |
| 37 | `bool` (1 octet) | `require_password` | rzu idem `:10` |

4 + 31 + 1 + 1 + 1 = **38**. Preuve client : le pas de tableau `0x26` ci-dessus, et l'objet de
57 octets (`19 + 38`) du désérialiseur 4002. NGemity porte la même structure
(`shared/Server/Packets/GameClient/TS_SC_HUNTAHOLIC_INSTANCE_INFO.h:5-10`).

#### 3.4.3 `TM_SC_HUNTAHOLIC_INSTANCE_INFO` (4002) — 45 octets

Charge utile = un `TS_HUNTAHOLIC_INSTANCE_INFO` (§3.4.2), donc offsets 7 à 44. rzu
`TS_SC_HUNTAHOLIC_INSTANCE_INFO.h:15-19`. Preuve client `0x6708a0` : vtable `0xa52108`, objet de
**57 octets** (`push 0x39`) = 19 + 38.

#### 3.4.4 `TM_SC_HUNTAHOLIC_HUNTING_SCORE` (4006) — 48 octets

| Offset | Type | Nom | Source |
|---|---|---|---|
| 7 | `int32` LE | `huntaholic_id` | rzu `TS_SC_HUNTAHOLIC_HUNTING_SCORE.h:6` |
| 11 | `int32` LE | `personal_kill_count` | rzu idem `:7` |
| 15 | `int32` LE | `personal_score` | rzu idem `:8` |
| 19 | `int32` LE | `kill_count` | rzu idem `:9` |
| 23 | `int32` LE | `score` | rzu idem `:10` |
| 27 | `double` LE | `point_advantage` | rzu idem `:11` |
| 35 | `double` LE | `point_rate` | rzu idem `:12` |
| 43 | `int32` LE | `gain_point` | rzu idem `:13` |
| 47 | `int8` | `result_type` | rzu idem `:14` |

20 + 16 + 4 + 1 = 41 ; 41 + 7 = **48**. Preuve client `0x670910` : vtable `0xa52110`, objet de
**60 octets** (`push 0x3c`) = 19 + 41, lectures `[ecx+0x7]`, `[ecx+0xb]`, `[ecx+0xf]`,
`[ecx+0x13]`, `[ecx+0x17]` (dwords), `[ecx+0x1b]` et `[ecx+0x23]` (`QWORD`, les deux `double`),
`[ecx+0x2b]` (dword) et `[ecx+0x2f]` (octet) : **les neuf offsets concordent au champ près**.

#### 3.4.5 `TM_SC_HUNTAHOLIC_UPDATE_SCORE` (4007) — 15 octets

| Offset | Type | Nom | Source |
|---|---|---|---|
| 7 | `int32` LE | `kill_count` | rzu `TS_SC_HUNTAHOLIC_UPDATE_SCORE.h:6` |
| 11 | `int32` LE | `score` | rzu idem `:7` |

Preuve client `0x6709a0` : vtable `0xa52118`, objet de **27 octets** (`push 0x1b`) = 19 + 8,
lectures aux offsets 7 et 11.

#### 3.4.6 `TM_SC_HUNTAHOLIC_MAX_POINT_ACHIEVED` (4010) — 7 octets

Aucune charge utile (rzu `TS_SC_HUNTAHOLIC_MAX_POINT_ACHIEVED.h:8`). Preuve client **indirecte** :
en `0x670b10`, entre le gestionnaire d'id interne 130 (`BEGIN_HUNTING`) et celui d'id interne 163
(`BEGIN_COUNTDOWN`), le client construit un objet de **19 octets** (`push 0x13`, aucune charge
utile) portant l'id interne **131**. Aucune classe RTTI `USMSG_HUNTAHOLIC_MAX_POINT_ACHIEVED`
n'existe dans le binaire ; la déduction « ce handler est celui de 4010 » repose sur l'ordre
croissant des ids internes de la famille. À confronter lors de l'implémentation (§7f).

#### 3.4.7 `TM_SC_HUNTAHOLIC_BEGIN_HUNTING` (4009) — 11 octets

| Offset | Type | Nom | Source |
|---|---|---|---|
| 7 | `uint32` LE | `begin_time` (`ar_time_t`) | rzu `TS_SC_HUNTAHOLIC_BEGIN_HUNTING.h:6` ; type `struct ar_time_t : strong_typedef<ar_time_t, uint32_t>` en `reference/rzu/librzu/src/lib/Packet/GameTypes.h:44` |

Preuve client `0x670a00` : vtable `0xa52120`, objet de **23 octets** (`push 0x17`) = 19 + **4**,
lecture d'un unique `DWORD` à `[ecx+0x7]`. Le champ vaut donc **4 octets** — cohérent avec
`ar_time_t` = `uint32_t` chez rzu. Puis le client affiche la boîte de message textuelle référencée
par l'id de chaîne `0x2413` (9235) et la clé `@NOTICE`.

#### 3.4.8 `TM_SC_HUNTAHOLIC_BEGIN_COUNTDOWN` (4012) — 7 octets

Aucune charge utile (rzu `TS_SC_HUNTAHOLIC_BEGIN_COUNTDOWN.h:9`). Preuve client `0x670b60` :
vtable `0xa52128` (`.?AUSMSG_HUNTAHOLIC_BEGIN_COUNTDOWN@@`), objet de **19 octets**
(`push 0x13`), id interne **163**.

---

## 4. Gating de version

### 4.1 Ids

**Les 17 opcodes portent un `X(<id>, true)` inconditionnel** dans rzu : aucun n'est renuméroté à
un epic ultérieur, et il n'existe qu'une seule entrée par `_ID(X)` (vérifié fichier par fichier,
toutes les lignes dans `reference/rzu/librzu/src/packets/GameClient/`) :

| Fichier | Ligne du `X(...)` | Id retenu pour 7.3 |
|---|---|---|
| `TS_CS_HUNTAHOLIC_INSTANCE_LIST.h` | 11 | 4000 |
| `TS_SC_HUNTAHOLIC_INSTANCE_LIST.h` | 16 | 4001 |
| `TS_SC_HUNTAHOLIC_INSTANCE_INFO.h` | 19 | 4002 |
| `TS_CS_HUNTAHOLIC_CREATE_INSTANCE.h` | 11 | 4003 |
| `TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h` | 10 | 4004 |
| `TS_CS_HUNTAHOLIC_LEAVE_INSTANCE.h` | 8 | 4005 |
| `TS_SC_HUNTAHOLIC_HUNTING_SCORE.h` | 17 | 4006 |
| `TS_SC_HUNTAHOLIC_UPDATE_SCORE.h` | 10 | 4007 |
| `TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h` | 8 | 4008 |
| `TS_SC_HUNTAHOLIC_BEGIN_HUNTING.h` | 9 | 4009 |
| `TS_SC_HUNTAHOLIC_MAX_POINT_ACHIEVED.h` | 8 | 4010 |
| `TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h` | 9 | 4011 |
| `TS_SC_HUNTAHOLIC_BEGIN_COUNTDOWN.h` | 9 | 4012 |
| `TS_CS_INSTANCE_GAME_ENTER.h` | 12 | 4250 |
| `TS_CS_INSTANCE_GAME_EXIT.h` | 11 | 4251 |
| `TS_CS_INSTANCE_GAME_SCORE_REQUEST.h` | 11 | 4252 |
| `TS_SC_INSTANCE_GAME_SCORE_REQUEST.h` | 20 | 4253 |

Sémantique du second argument : dans `CREATE_PACKET_VER_ID`, `X(id, condition)` engendre
`if(condition) id = id_;` (`reference/rzu/librzu/src/lib/Packet/PacketDeclaration.h:625-630`).
`true` = id valable pour **toutes** les versions. Ces 17 opcodes n'existent qu'à partir
d'`EPIC_6_3` (`#define EPIC_6_3 0x060300`, `PacketEpics.h:56`), et **`EPIC_7_3` = 0x070300 >
0x060300** (`PacketEpics.h:59`) : ils sont donc tous valides en 7.3. Les commentaires
`// Since EPIC_6_3` figurent sur 4250, 4251, 4252, 4253, 4011 et 4012 ; les autres en-têtes n'ont
pas de commentaire de version, ce qui ne les gate pas (absent = pas de contrainte).

### 4.2 Champs

| Champ | Gating rzu | Décision pour 7.3 |
|---|---|---|
| `battle_arena_point` | `version >= EPIC_8_1` — `TS_SC_INSTANCE_GAME_SCORE_REQUEST.h:12` | **exclu** |
| `battle_arena_mvp_count` | `version >= EPIC_8_1` — `:13` | **exclu** |
| `battle_arena_record_classic[2]` | `version >= EPIC_8_1` — `:14` | **exclu** (8 octets) |
| `battle_arena_record_slaughter[2]` | `version >= EPIC_8_1` — `:15` | **exclu** (8 octets) |
| `battle_arena_record_bingo[2]` | `version >= EPIC_8_1` — `:16` | **exclu** (8 octets) |
| **Total 4253** | les 5 directives ci-dessus, `EPIC_8_1 = 0x080100` (`PacketEpics.h:61`) > `EPIC_7_3` | **23 octets** (16 de charge utile), et non 55 |
| tous les autres champs des 17 opcodes | **aucune directive `version`** (vérifié : `grep -n "version"` sur les 17 en-têtes ne renvoie que les 5 lignes ci-dessus) | tels quels |

C'est **le** piège de cette fiche : un développeur qui recopie `TS_SC_INSTANCE_GAME_SCORE_REQUEST`
depuis rzu sans lire le gating écrit 32 octets de trop par paquet et décale tout ce qui suit. Le
client 7.3 confirme la version courte par son désérialiseur de 16 octets (§3.2.4).

---

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` fait de ces paquets : **rien**

| Fait | Preuve |
|---|---|
| Aucun des 17 opcodes n'est déclaré comme traité : les seules occurrences de `HUNTAHOLIC`/`INSTANCE_GAME` dans le serveur NGemity sont les énumérations et les déclarations de paquets | `grep -rn "TS_CS_HUNTAHOLIC\|TS_SC_HUNTAHOLIC" --include=*.cpp Chihiro/src/` → 0 résultat ; `shared/Server/ClientPackets.h:233-249` |
| Le bloc qui devait appeler la logique d'instance est **entièrement commenté** : `case SKILL_RANKED_DEATHMATCH_ENTER` / `SKILL_FREED_DEATHMATCH_ENTER` → `INSTANCE_GAME_ENTER()`, `SKILL_WARP_TO_HUNTAHOLIC_LOBBY` → `WARP_TO_HUNTAHOLIC_LOBBY()`, `SKILL_INSTANCE_GAME_EXIT` → `INSTANCE_GAME_EXIT()` | `reference/ngemity/Chihiro/src/Skills/Skill.cpp:1418-1433` (le `/*` ouvre en 1418, le `*/` ferme en 1433) |
| Les identifiants de compétences sont bien définis (mais jamais appelés) : `SKILL_WARP_TO_HUNTAHOLIC_LOBBY = 64818`, `SKILL_INSTANCE_GAME_EXIT = 64827` | `Chihiro/src/Skills/SkillBase.h:224-225` |
| Connaissances adjacentes portées par NGemity, utiles plus tard : `CRT_HUNTAHOLIC = 8` (`Unit.h:86`) ; `MONSTER_TYPE_HUNTAHOLIC_1LV/2LV/3LV/BOSS = 15/16/17/18` (`MonsterBase.h:65-68`) ; `AF_ERASE_ON_QUIT_HUNTAHOLIC = (1 << 8)` (`StateBase.h:30`) ; `TYPE_HUNTAHOLIC_PARTY = 3` commenté (`GroupManager.h:29`) | fichiers cités |
| Détail parlant : `GameContent::HUNTAHOLIC_MONSTER_RESPAWN_INFO` et le rechargement des monstres HuntaHolic existent côté NGemity, mais la ligne 134 de `GameContent.cpp` est du **code décompilé** (noms de types démanglés du type `std::_Vector_const_iterator<…>::operator__` ; 4 lignes du fichier sont dans cet état) : rien n'y est directement portable | `Chihiro/src/Globals/GameContent.cpp:134` |

rzu apporte en revanche deux **données de personnage** qui manquent à Navislamia pour alimenter
4253 : `huntaholic_point` et `huntaholic_enter_count` dans la table de personnage de `rzgame`
(`rzgame/src/Database/DB_Character.h:36-37`, lecture `DB_Character.cpp:42-43`, affectation
`Character.cpp:72-73`) et leur exposition en propriétés client `huntaholicpoint` /
`huntaholic_ent` (`rzgame/src/Component/Character/Character.cpp:273-274`). Ces deux noms de
propriétés sont **exactement** ceux que le client 7.3 cite (`strings` l. 26430 et 26247) : le champ
`holicpoint` de 4253 est la même valeur. Navislamia possède déjà ces deux colonnes côté entité
(`CharacterEntity.cs:51-52`) ; il faut vérifier qu'elles existent bien dans la base Telecaster
réelle (§7g).

### 5.2 Ce que le serveur doit répondre

| Reçu | Réponse attendue | Statut |
|---|---|---|
| `4250` (entrée) | aucune réponse directe : le serveur doit **déplacer** le personnage dans l'instance ; le client sort de son état d'attente à la réception d'un `TM_SC_WARP`/région, pas par un accusé propre à 4250 | Le déclencheur client de 4250 est un message **entrant** non identifié (§7c) : la réponse exacte dépend de ce message |
| `4251` (sortie) | aucune : le serveur ramène le personnage au lobby | `TM_CS_RETURN_LOBBY` existe déjà côté Navislamia (`GameClient.cs:771`) |
| `4252` (demande de score) | `4253` avec `holicpoint`, `bearroad_ranking`, `deathmatch_kill_count`, `deathmatch_death_count` | **Établi** (§3.2.4) mais sans source de données en 7.3 pour les 3 derniers : voir §7h |
| `4000` (liste, page P) | `4001` même `huntaholic_id`, même `page`, `total_page` cohérent, `infos` ≤ 38 octets chacun | Format établi ; **le nombre d'entrées par page n'est pas établi** (§7i) — c'est le serveur qui le fixe, le client affiche `infos` entrées |
| `4003` (créer) | un `4001` rafraîchi, ou un `4002`, ou un paquet de **résultat** portant le même id : **non établi** (§7j) | Le client compare bien l'id d'un objet message à `4003` (`mov ecx,0xfa3` puis `cmp cx,WORD PTR [esi+0x13]` en `0x47cbce`/`0x47cbd3` et `0x47cc31`/`0x47cc36`) dans un handler d'UI, mais le sens (message émis ou reçu) et le code de retour ne sont pas tranchés |
| `4004` (rejoindre) | idem 4003 | idem |
| `4005` (quitter) / `4008` (quitter le lobby) | aucun accusé nécessaire | — |
| `4011` (commencer) | le déroulé : `4012` (compte à rebours), puis `4009` (`begin_time`), puis `4007`/`4006` (scores), puis `4010` (objectif atteint) | **Formats** établis ; **l'ordre exact et les temporisations ne sont pas établis** (§7k) |

### 5.3 Socle minimum implémentable

Un socle utile et vérifiable **sans instance de jeu, sans état partagé et sans nouveau schéma** :
les **4 opcodes** `4250`, `4251`, `4252`, `4253`.

* Raison : ce sont les seuls dont la charge utile est entièrement déterministe (4 × `int32`), dont
  la réponse est univoque (4253 répond à 4252), et dont la donnée existe déjà
  (`CharacterEntity.HuntaholicPoint`).
* Ce que cela débloque : les paquets arrivent au lieu de lever `Unknown Packet Type`
  (`GameClient.cs:802`), le client cesse de déconnecter sur ces ids, et le schéma de test
  d'offsets est immédiat.
* Ce que cela ne débloque **pas** : le lobby HuntaHolic reste vide (aucune liste), une instance ne
  peut pas être créée/rejointe, et commencer la chasse ne produira rien. Le socle ne rend donc
  **aucun** contenu jouable à lui seul : c'est le socle *protocole*, pas le socle *fonctionnel*.

Ordre ensuite, par difficulté croissante et par bénéfice :

| Rang | Contenu | Opcodes | Charge utile | Pourquoi ce rang |
|---|---|---|---|---|
| 1 | Socle protocole | 4250, 4251, 4252, 4253 | 4 / 0 / 0 / 16 o | déterministe, testable seul |
| 2 | Lobby en lecture | 4000, 4001, 4002 (+ 4005) | 4 o / 23+38·N / 45 o | rend le lobby **visible** ; déjà alimenté par les tables `HuntaholicResource`/`HuntaholicInstanceResource` |
| 3 | Lobby en écriture | 4003, 4004, 4008 | 49 o / 21 o / 0 | chaînes de 31 et 17 octets, à valider (longueur, NUL, mot de passe) |
| 4 | Déroulé de la chasse | 4011, 4012, 4009, 4007, 4006, 4010 | 0 / 0 / 4 / 8 / 41 / 0 o | nécessite monstres, résurrection (`HuntaholicMonsterRespawnResource`), minuterie |

### 5.4 Découpage proposé pour `navis-dev`

Découpage en **paquets unitaires**, chacun avec sa fiche ou sa section, ses tests d'offsets et sa
MR. Chaque paquet doit porter sa propre preuve de taille (le tableau §3 de la présente fiche).

| Paquet | Titre | Opcodes | Critère d'acceptation propre |
|---|---|---|---|
| **S1** | `TM_CS_INSTANCE_GAME_ENTER` / `EXIT` / `SCORE_REQUEST` + réponse 4253 | 4250, 4251, 4252, 4253 | test d'offsets des 4 tailles (11/7/7/23) ; 4253 relu depuis `CharacterEntity.HuntaholicPoint` ; les 4 ids déclarés dans `GamePackets` **et** routés (aucun ne doit atteindre `GameClient.cs:802`) |
| **S2** | Lobby HuntaHolic — liste | 4000, 4001, 4002 | test d'offsets du tableau : N=0 → 23 o, N=1 → 61 o, N=2 → 99 o ; pas de 38 ; `name` de 31 octets non tronqué à 30 |
| **S3** | Lobby HuntaHolic — quitter | 4005, 4008 | 7 octets, aucune charge utile, réponse à définir (§7j) |
| **S4** | Lobby HuntaHolic — créer / rejoindre | 4003, 4004 | 56 et 28 octets ; chaînes bornées à 30 et 16 caractères + NUL ; refus propre si `Length` ≠ attendu |
| **S5** | Chasse — début et déroulé | 4011, 4012, 4009 | 7/7/11 octets ; `begin_time` sur 4 octets (`ar_time_t`) et non 8 |
| **S6** | Chasse — scores | 4006, 4007, 4010 | 48/15/7 octets ; `double` à l'offset 27 et 35 ; `gain_point` à 43 ; `result_type` à 47 |

Dépendances : **S1 est indépendant** ; S2 → S4 (créer suppose lister) ; S5 → S6 ; S1 peut être
livré avant tout le reste. Rien n'oblige à tout faire dans un seul paquet : la famille est trop
large pour une seule MR, et la discipline d'offsets du dépôt impose un tableau par opcode.

### 5.5 Points d'implémentation à respecter

| Point | Règle | Source |
|---|---|---|
| Enum et dispatch ensemble | tout membre ajouté à `GamePackets` doit être routé dans la boucle de `OnDataReceived`, sinon il atteint `_ => throw new Exception("Unknown Packet Type")` | `Game/Network/Clients/GameClient.cs:791-803` ; critère transversal n° 4 |
| Forme du dispatch | la boucle existante enchaîne des `if (header.ID == (ushort)GamePackets.X) { Handle…; continue; }` **avant** le `switch` final — suivre ce style pour les 17 ids | `GameClient.cs:555-790` |
| Paquets serveur → client | les réponses s'écrivent avec `CreatePacket(GamePackets.X, taille)` puis `BinaryPrimitives.Write…` aux offsets, comme `TM_SC_REGION_ACK` (11) | `Game/Network/Packets/Game/GameMovePackets.cs:24-40` |
| Lecture client → serveur | les lecteurs sont des `TryRead…` retournant `bool`, testés sur des `byte[]` construits à la main, comme `TryReadGetRegionInfo` (550) | `Game/Network/Packets/Game/GameActionPackets.cs:232-245` |
| Longueur reçue | `Length` doit être vérifié par le handler (un 4250 de 7 octets n'a pas de champ) ; l'en-tête est déjà validé par `OnDataReceived` | `GameClient.cs:555` et suivants |
| Tests | au moins **366** tests, plus un test d'offsets par nouvel opcode (taille totale **et** position de chaque champ) | critères transversaux n° 2 et 3 |
| `CLAUDE.md` | ne pas l'écrire : le bloc de §10 est à recopier dans la description de la MR | critère transversal n° 5 |

### 5.6 Cas limites

| Cas | Ce que fait le client | Comportement serveur recommandé |
|---|---|---|
| `Length` = 7 reçu pour 4250 | le client lirait 4 octets absents → valeurs indéterminées, pas de message d'erreur identifié | refuser et journaliser, sans réponse |
| 4253 reçue par un client qui n'a pas demandé 4252 | aucun état d'attente identifié | n'envoyer 4253 **qu'en réponse** à 4252 |
| `infos` = 0 dans 4001 | le client affiche une liste vide (pas de test de cohérence identifié) | garder `total_page` ≥ 1 si des instances existent, sinon 0 — à confronter (§7i) |
| `name` sans NUL dans 4001/4003 | le client lit 31 octets fixes : un nom de 31 caractères non terminé déborde sur le champ suivant | toujours écrire le NUL **dans** les 31/17 octets |
| valeurs de score négatives | champs `uint32` : aucune interprétation signée côté client | écrire des `uint32`, clamper à 0 en amont |
| `instance_game_type` inconnue (autre que 0/1/2) | aucune table de refus identifiée | accepter et journaliser, ne pas déconnecter |

---

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | NGemity | Navislamia (cette fiche) | Raison |
|---|---|---|---|
| Ids | `CREATE_PACKET(TS_CS_INSTANCE_GAME_ENTER, 4250)` — un id unique, aucun mécanisme de version | `CREATE_PACKET_VER_ID` + `X(id, condition)` chez rzu | NGemity ne sait pas exprimer une renumérotation ; rzu si. Pour 7.3 les deux donnent le même id (aucun de ces 17 n'est renuméroté), donc l'écart est **sans effet ici** — mais il faut lire rzu pour le *gating des champs* (§4.2), que NGemity ne porte pas du tout |
| Gating des champs de 4253 | NGemity déclare la structure 8.1 complète (8 champs), sans gating | 4 champs, 16 octets, `battle_arena_*` exclus | Le client 7.3 lit 16 octets (§3.2.4). Porter NGemity tel quel produirait un paquet trop long ; porter rzu sans lire le gating aussi. Le client tranche |
| Logique | **rien** : tout est commenté (`Skill.cpp:1418-1433`) | Rien non plus dans ce socle | Il n'y a littéralement rien à porter : ce socle est un travail de protocole, pas de logique. NGemity ne sert ici que pour les énumérations adjacentes (compétences, types de monstre, drapeau de sortie) |
| Ids de compétence | `SKILL_WARP_TO_HUNTAHOLIC_LOBBY = 64818`, `SKILL_INSTANCE_GAME_EXIT = 64827` | Non traités ici | Ces compétences sont le déclencheur *jeu* de l'entrée/sortie ; les implémenter suppose le système d'instance, hors socle. Noté pour ne pas les redécouvrir |
| Structure `TS_HUNTAHOLIC_INSTANCE_INFO` | Identique à rzu (38 octets) | Idem | Aucun écart : c'est la seule partie de la famille où NGemity confirme rzu champ par champ |

---

## 7. `NON ÉTABLI`

Aucun de ces points n'a été comblé par une supposition. Chacun indique la question précise à
trancher **et** la méthode qui l'a écartée jusqu'ici.

### (a) Le client 7.3 reçoit-il 4253 ?
La constante `0x109d` (4253) n'apparaît **nulle part** dans le désassemblage du `.text`
(`grep -c "0x109d"` sur `objdump -d` = 0). Mais la classe `USMSG_INSTANCE_GAME_SCORE` et son
désérialiseur de 16 octets existent (§3.2.4). Question : la table de routage id → handler vit-elle
en `.data` (non désassemblée) ? À trancher en lisant la table du `SMsgMapper` (données brutes) ou
en observant une session réelle. **Ne pas conclure que 4253 est inutile** : la structure de 16
octets est trop spécifique pour être fortuite.

### (b) Quel message entrant déclenche 4250 / 4251 ?
Les deux constructeurs sont appelés dans un gestionnaire à table de saut (`0x49e98c`, table
d'octets `0x49ea50`, plage 1027-1249). L'id exact du message qui atteint les corps `0x49e45f`
(4250) et `0x49e490` (4251) n'a pas été résolu. C'est **la** question qui décide de la sémantique
d'entrée : si le serveur ne peut pas produire ce message, le client n'enverra jamais 4250.

### (c) Domaine de `instance_game_type`
Valeurs observées : 0, 1, 2 (§2.1). Signification inconnue ; aucune table de correspondance dans
le binaire, aucune dans rzu. Hypothèse non retenue faute de source : « 1 = deathmatch,
2 = HuntaHolic » (les noms rzu `deathmatch_*` et le tableau de scores ne le prouvent pas). La
valeur étant recopiée d'un champ du message entrant (b), elle appartient au dialogue
serveur → client ; le serveur 7.3 peut donc rester en 0/1/2 sans risque de désalignement.

### (d) `4008` (`TM_CS_HUNTAHOLIC_LEAVE_LOBBY`) est-il utilisé par le client 7.3 ?
Aucun constructeur trouvé ; le seul `0xfa8` du binaire est un déplacement de pile
(`0x67902f`). Recherche exhaustive des immediates `0xfa0`-`0xfbb` dans le `.text` : `4008` n'y est
jamais un id de paquet. Question ouverte : le « quitter le lobby » passe-t-il par `4005` ou par un
`TM_CS_CHANGE_LOCATION` ? À trancher par observation (`db_scriptstring.rdb`/UI) ou en acceptant
l'opcode comme non utilisé en 7.3.

### (e) Routage id ↔ handler des paquets serveur → client
Le client ne compare jamais `4006`, `4007`, `4009`, `4010`, `4012` comme littéraux. L'appariement
des §3.4 repose sur l'identité de structure et sur l'ordre croissant des ids internes (126 → 131,
163, 1203). Solide, mais **indirect** : à confirmer en lisant la table en `.data`. En cas de
doute, c'est le **format** qui est retenu dans cette fiche, pas le numéro de handler.

### (f) `4010`
Pire cas du point (e) : aucune classe RTTI dédiée. La taille (7 octets) est établie par rzu
(`_DEF(_)` vide) et le client n'a aucun objet à charge utile nulle non attribué hors
`0x670b10`. Risque résiduel faible mais réel si ce handler appartient à un autre message.

### (g) Les colonnes Telecaster existent-elles en base ?
`CharacterEntity.cs:51-52` déclare `HuntaholicPoint` et `HuntaholicEnterCount`, mais aucun script
de schéma du contexte Telecaster n'est présent dans le dépôt (seul `ArcadiaSchemaPSQL.sql` l'est).
À vérifier sur la base cible avant d'écrire `4253` : si la colonne manque, il faut une migration.

### (h) `bearroad_ranking`, `deathmatch_kill_count`, `deathmatch_death_count`
Aucune source en 7.3 : ni table, ni colonne, ni propriété dans Navislamia, et rien dans `Chihiro`.
Question : le serveur répond-il des zéros (acceptable côté client, un score nul n'est pas une
erreur), ou faut-il créer un stockage ? À trancher par Killian, c'est un choix de contenu.

### (i) Politique de pagination de `4001`
Le nombre d'entrées par page n'est écrit nulle part : c'est le serveur qui décide. Le client
affiche `infos` entrées et se sert de `total_page` pour sa navigation. Question : dimensionner sur
le nombre de lignes de `HuntaholicInstanceResource`, ou sur un maximum d'écran ? Aucun indice
client relevé.

### (j) Réponse à `4003` / `4004`
Le client compare l'id d'un objet message à `4003` : `mov ecx,0xfa3` puis
`cmp cx, WORD PTR [esi+0x13]`, en `0x47cbce`/`0x47cbd3` et `0x47cc31`/`0x47cc36`, dans un handler
d'« Act » de fenêtre qui positionne ensuite un octet d'état (`mov BYTE PTR [edi+0x150],0x1` en
`0x47cbec`, `…,0x0` en `0x47cc47`). Deux lectures restent ouvertes : (1) le message comparé est-il
un paquet **reçu** ou la copie locale du paquet **émis** par la fenêtre (le `cmp cx,[esi+0x13]`
suppose que l'id est à `[objet+0x13]`, comme dans le handler voisin où le premier champ de charge
utile est à `[objet+0x16]`, ce qui placerait la base du tampon brut à `objet+0x0f`) ; (2) l'octet
`[edi+0x150]` est-il un drapeau « en attente de réponse ». **Ne pas inventer de paquet de résultat**
avant d'avoir tranché : S4 doit être livré sans réponse si la question reste ouverte.

### (k) Ordre et cadence du déroulé de chasse
`4012` (compte à rebours) → `4009` (`begin_time`) → `4007`/`4006` → `4010` : l'ordre n'est établi
par aucune source. `HuntaholicResource.hunting_period` (`ArcadiaSchemaPSQL.sql:358-370`) et
`objective_point`/`max_point` suggèrent la boucle, sans la décrire. Question : qui émet, à quelle
fréquence, et `4009.begin_time` est-il en secondes epoch ou en ticks serveur ? Le client se
contente d'afficher `@NOTICE` et de comparer des scores ; aucun indice de format de temps relevé.

### (l) `huntaholic_id` de `4000` / `4001`
`4000` porte une **page** et `4001` renvoie un `huntaholic_id` : le client ne fournit donc pas
l'identifiant de la ressource HuntaHolic dans sa demande. Question : d'où vient le
`huntaholic_id` servant à filtrer (`HuntaholicInstanceResource.huntaholic_id`,
`ArcadiaSchemaPSQL.sql:343-344`) ? Aucune trace dans le paquet 4000.

### (m) 4250 est-il aussi un paquet serveur → client ?
Le client dispatche sur l'id **entrant** 4250 (`0x4775ec` → corps `0x4776a7`) et, selon un champ de
16 bits lu en `[edi+0x15]`, affiche trois textes différents (valeurs 10, 56/72, 78 → chaînes 9234,
9239, 9245), c'est-à-dire un **refus** d'entrée. Deux questions : ce champ est-il un offset du fil
(la base du tampon brut dans cet objet n'a pas été rattachée) et 4250 sert-il **dans les deux
sens** (le serveur renvoyant 4250 avec un code pour refuser l'entrée) ? À trancher avant
d'implémenter S1 : si 4250 est bidirectionnel, la réponse de refus fait partie du socle.

---

## 8. Commits et binaires épinglés

| Référence | Identifiant | Emploi |
|---|---|---|
| Navislamia `origin/master` | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | état du serveur au moment de la fiche |
| rzu (`reference/rzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | tailles, ordre des champs, gating de version, données `rzgame` |
| NGemity (`reference/ngemity`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | logique (nulle ici) et énumérations adjacentes |
| Client 7.3 (`reference/client73/SFrame.exe`) | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 octets) | constructeurs, désérialiseurs, RTTI, chaînes |
| Dump de chaînes | `strings -n 4 SFrame.exe` → 63 099 lignes (fichier local `/tmp/sframe.strings`, non versionné) | numéros de ligne cités en §2 |
| Désassemblage | `objdump -d -M intel SFrame.exe` → 2 256 750 lignes (fichier local `/tmp/sframe.asm`, non versionné) | toutes les VA citées |

Commandes de reproduction (depuis `reference/client73/`) :

```bash
sha256sum SFrame.exe
strings -n 4 SFrame.exe > /tmp/sframe.strings
objdump -d -M intel SFrame.exe > /tmp/sframe.asm
```

---

## 9. Implémentation — `navis-dev`

*(Section à remplir par `navis-dev`, sur la même branche, comme pour les fiches 550, 1202, 203 et
253. La présente fiche s'arrête à l'archéologie : aucun code serveur n'est modifié ici.)*

---

## 10. Bloc prêt à coller dans `CLAUDE.md`

> ### Socle instances de jeu — 4250-4253 et famille HuntaHolic 4000-4012
>
> **17 opcodes, tous `X(<id>, true)` chez rzu : aucun n'est renuméroté en 7.3.** Ils n'existent
> qu'à partir d'`EPIC_6_3` (4250-4253, 4011, 4012), et `EPIC_7_3 = 0x070300 > EPIC_6_3`, donc tous
> valides. Fiche complète : `docs/packet-specs/socle-instances-jeu.md`.
>
> **Le piège de cette famille est le gating des champs de 4253** :
> `TS_SC_INSTANCE_GAME_SCORE_REQUEST` porte cinq champs `version >= EPIC_8_1`
> (`battle_arena_point`, `battle_arena_mvp_count`, `battle_arena_record_classic/slaughter/bingo`,
> 32 octets au total). En 7.3 le paquet fait **23 octets**, pas 55 : `holicpoint` à 7,
> `bearroad_ranking` à 11, `deathmatch_kill_count` à 15, `deathmatch_death_count` à 19. Le client
> 7.3 lit 16 octets de charge utile — c'est la source de vérité.
>
> **Tailles à écrire en dur** (source : rzu + constructeurs du client 7.3) :
> 4250 = 11, 4251 = 7, 4252 = 7, 4253 = 23 ; 4000 = 11, 4001 = 23 + 38·N, 4002 = 45,
> 4003 = 56, 4004 = 28, 4005 = 7, 4006 = 48, 4007 = 15, 4008 = 7, 4009 = 11, 4010 = 7,
> 4011 = 7, 4012 = 7. Les chaînes de 4003/4004 sont des tampons **fixes** de 31 et 17 octets
> (NUL compris) ; `ar_time_t` de 4009 vaut **4 octets** ; le pas du tableau de 4001 est **38**.
>
> **`TM_CS_INSTANCE_GAME_ENTER` (4250) est une réponse du client** : le client copie dans sa charge
> utile un `int32` lu dans le message entrant qui la déclenche. Le serveur ne peut donc pas la
> provoquer tant que ce message n'est pas identifié (`NON ÉTABLI` (b) de la fiche).
>
> **Ne pas porter NGemity** : les 17 opcodes y sont déclarés et jamais traités, et le bloc
> `Skill.cpp:1418-1433` (`INSTANCE_GAME_ENTER`, `WARP_TO_HUNTAHOLIC_LOBBY`, `INSTANCE_GAME_EXIT`)
> est entièrement commenté. Les compétences 64818 et 64827 sont définies mais jamais appelées.
>
> **Déjà en place dans Navislamia** : `CharacterEntity.HuntaholicPoint` /
> `HuntaholicEnterCount` (`CharacterEntity.cs:51-52`), `PartyType.HuntaholicParty`,
> `StateTimeType.EraseOnQuitHuntaholic`, `ItemEffectInstant.IncHuntaholicPoint`,
> `ItemUseFlag.CantUseInHuntaholic`, et les tables de ressources HuntaHolic/InstanceDungeon
> (`ArcadiaSchemaPSQL.sql`). Le travail est purement protocole.
>
> **Socle minimum** : 4250/4251/4252 + 4253, seuls opcodes sans état et testables seuls. Découpage
> en 6 paquets (S1…S6) : §5.4 de la fiche.

---

## A VERIFIER PAR KILLIAN

1. **Les trois champs de score sans source** (`bearroad_ranking`, `deathmatch_kill_count`,
   `deathmatch_death_count`) : répondre des zéros en 7.3, ou créer un stockage ? Les tables
   Arcadia n'ont rien pour eux et `Chihiro` non plus (§7h).
2. **La colonne Telecaster** `HuntaholicPoint` / `HuntaholicEnterCount` existe-t-elle dans la base
   cible ? Le dépôt ne contient que le schéma Arcadia (§7g).
3. **`4008`** : l'opcode est-il utilisé par le client 7.3 ? Aucune trace dans le binaire (§7d).
4. **Pagination de `4001`** : combien d'instances par page — choix de contenu, sans source (§7i).
5. **Réponse à `4003`/`4004`** : le serveur doit-il renvoyer le même id avec un code de résultat ?
   Indice non concluant (§7j).
6. **Signification des valeurs 0/1/2 de `instance_game_type`** (§7c) et **identité du message
   entrant qui déclenche 4250/4251** (§7b) : à trancher si le contenu instance game est ouvert.
7. **4250 est-il bidirectionnel ?** Le client affiche un refus (trois textes selon un code de 16
   bits) sur un message entrant d'id 4250 : si le serveur doit refuser une entrée, le socle S1
   change de forme (§7m).
8. **L'appariement handler ↔ id des paquets serveur → client** : la classe
   `USMSG_HUNTAHOLIC_MAX_POINT_ACHIEVED` **n'existe pas** dans le RTTI du binaire (§7f) et les ids
   internes 126-131/163/1203 n'ont pas de table id ↔ handler lisible dans le `.text` (§7e).
   Conséquence pratique : les **formats** sont établis, le *numéro* de handler ne l'est pas.
