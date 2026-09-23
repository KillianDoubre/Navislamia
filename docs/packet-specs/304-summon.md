# TM_CS_SUMMON (304) — demande d'invocation portée par une carte (Epic 7.3)

Fiche d'archéologie : elle fixe la disposition sur le fil de `TM_CS_SUMMON` (304) pour l'Epic 7.3,
tranche son gating de version, et relève que **le client 7.3 n'émet pas cette trame** : le geste
d'invocation y passe par un **sort** (§2), comme le fait NGemity côté serveur (§5.2). Décision 7.3 :
id **304**, charge utile `int8_t is_summon` puis `ar_handle_t card_handle`, **12 octets en-tête
compris** (§3).

Statut : **la fiche est le seul livrable attendu** ; le paquet doit en plus être déclaré dans
`GamePackets` et routé (§5.3, critère « enum et dispatch modifiés ensemble »). Aucun émetteur client
n'ayant été trouvé, aucun test de bout en bout n'est possible : les tests à écrire sont ceux
d'offsets (§10).

## 1. Identité

| | |
| --- | --- |
| id | **304** (décimal) = `0x130` en 7.3 |
| nom | `TM_CS_SUMMON` |
| source de l'id et du nom | `op_codes.md:106` |
| sens | client → serveur (`SessionPacketOrigin::Client`, rzu `TS_CS_SUMMON.h:14`) |
| source de la structure | rzu `reference/rzu/librzu/src/packets/GameClient/TS_CS_SUMMON.h:5-12` |
| second témoin | NGemity `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_SUMMON.h:6-11` (identité de disposition, §3.3), id dans `shared/Server/ClientPackets.h:119` |
| NavisLamia | **absent** de `Game/Network/Packets/Enums/GamePackets.cs` : la famille d'invocation y saute de `TM_EQUIP_SUMMON = 303` (`:72`) à `TM_SC_UNSUMMON = 305` (`:73`). C'est ce trou que la tâche comble. |
| nom d'usage | « TM_CS_SUMMON » — le nom est celui de la famille `TS_*` d'rzu, la famille `TM_*` de `op_codes.md` est la même table d'ids |

Les deux références portent le **même commentaire** au-dessus de la définition : `// Seems unused`
(rzu `:5`, NGemity `:6`). C'est la seule indication de rzu et de NGemity sur ce paquet ; elle est
confirmée côté client au §2.3.

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Le geste, tel que le client 7.3 le décrit lui-même

`reference/client73/db_string.rdb` (chaînes, ligne 34762 ; les mêmes textes reviennent en 10338,
10962, 59087, 59857, 135042 avec d'autres formulations) :

> « To ride a creature, you need to summon the creature. To summon a creature, press the R key to
> open the creature slot and select the Ornitho, then **summon it with summon creature skill**. »

Le geste d'invocation en 7.3 est donc : ouvrir la fenêtre de créature (R / Alt+R), y sélectionner la
carte formée, puis **lancer le sort d'invocation de créature** sur cette carte. C'est un **sort**, pas
une commande d'interface dédiée — et c'est exactement la chaîne que NGemity implémente (§5.2 :
`EF_SUMMON = 601` → `Skill::DO_SUMMON`, qui résout la cible comme le handle de la carte).

Trois relevés client confirment que l'action est un sort et non une commande propre :

- la classe de disposition de compétence `ff::rp::disp::USkillSummonUnsummon` (`SFrame.exe`, dump de
  chaînes, ligne 43973) : le client modélise « invoqué / renvoyé » comme une **disposition de sort**,
  au même titre que `USkillTaming` ou `USkillCritical` ;
- la liste des commandes d'entrée typées du client (`SGameInput`, lignes 44546-44555) :
  `SInputMove`, `SInputAttack`, `SInputCastCancel`, `SInputSkill`, `SInputTakeItem`, `SInputSit`,
  `SInputSitUp`, `SInputMount`, `SInputUnMount`, `SInputUseItem` — **aucune** entrée « summon » ou
  « unsummon ». L'invocation se fait donc par `SInputSkill`.
- les animations de travail associées sont `SWorkSummonCall` / `SWorkSummonReCall` (lignes 44589-44590),
  c'est-à-dire des *works* de sort, pas des commandes réseau.

### 2.2 Ce que le client envoie réellement pour ce geste

Le client n'a **aucune classe de message** client → serveur pour 304, alors qu'il en a une pour chaque
autre paquet d'invocation qu'il émet :

| id | classe RTTI côté client | ligne du dump |
| --- | --- | --- |
| 303 | `?AUSMSG_REQUEST_EQUIP_SUMMON@@` (C→S) | 44180 |
| 323 | `?AUSIMSG_UI_CHANGE_SUMMON_NAME@@` | 43871 |
| 452 | `?AUSIMSG_UI_SUMMON_CARD_SKILL_LIST@@` | 43863 |
| 303 | `?AUSIMSG_UI_ACT_EQUIP_SUMMON@@` (action d'interface) | 43998 |
| **304** | **aucune** | — |

Le nom `TM_CS_SUMMON` **est** connu du client : il figure dans sa table de noms de paquets
(`SFrame.exe`, ligne 26358 du dump), et le même binaire possède une table `id → nom` où
`0x130` (= 304) est associé à la chaîne `TM_CS_SUMMON` (`SFrame.exe`, `0x6772a4`
`push 0xa534b4` puis `0x6772c6` `mov eax,0x130` ; l'entrée précédente du même bloc est
`TM_EQUIP_SUMMON` = 303, `0x677242` puis `0x677264`).
Autrement dit : le client *nomme* 304 sans jamais le *construire*.

### 2.3 Relevé négatif côté client : méthode et preuves

Aucune trame 304 n'est observable côté serveur (aucun émetteur identifié), donc la question se
tranche par lecture du client, sans l'exécuter. Trois relevés, tous sur
`reference/client73/SFrame.exe` (`strings -a -n 4` = 63 099 lignes, md5
`7d92f691a46e6768d4cf00e85747e776` ; désassemblage `objdump -d -M intel` = 2 256 750 lignes) :

1. **Aucune écriture de l'id 304 dans un en-tête de trame.** Le motif d'écriture d'id du client est
   `mov <reg>,<id>` suivi d'un store 16 bits de ce même registre (`mov WORD PTR [<reg>+0x4],<reg16>`
   pour une trame construite en objet, `mov WORD PTR [ebp-0xN],<reg16>` pour une trame construite sur
   la pile — les deux formes existent : 5000 en `0x48d182`, 402 en `0x48ebe7`). Un balayage complet du
   `.text` sur ce motif (immédiat `0x64 ≤ id ≤ 0x2000` + store 16 bits du même registre dans les 12
   instructions suivantes) relève **123 sites**, dont les voisins d'invocation `303` (`0x48cd44`),
   `323` (`0x48c612`), `324` (`0x48c662`), `354` (`0x48c6c5`) — et **aucun site pour 304**.
2. **Aucun immédiat `0x130` sous une autre forme non plus.** Recherche directe de `0x130` dans toutes
   les formes possibles (`mov reg,imm`, `mov [mem],imm`, `push imm`) : **4 sites**, tous expliqués et
   aucun n'est un id de paquet — `0x6772c6` (la table id→nom du point ci-dessus), `0x737092`
   (`mul edx` d'une taille d'élément de tableau), `0x88c410` (analyse de noms de types : la même
   fonction, en `0x88c3e6-0x88c410`, compare `textureCUBE`/`texture3D`, et en `0x88c41a-0x88c42e`,
   `this`/`throw`), `0x471f1a` (variable locale). Les cinq `push 0x130` (`0x7356ac`, `0x7370cf`,
   `0x8491dc`, `0x8a6992`, `0x946da1`) sont des tailles d'allocation, de remplissage ou des pas de
   tableau (`operator new(304)`, `memset(…, 304)`, `count * 304`), jamais un id.
3. **Aucun gestionnaire entrant non plus**, ce qui est cohérent mais sans objet pour un paquet C→S :
   pas de `?AUSMSG_SUMMON@@` dans les noms RTTI, pas de `MSG_SUMMON` dans la liste des messages
   traités du client (lignes 19724-19729 et 25130-25167 du dump ; `MSG_UNSUMMON` et `MSG_UNSUMMON_PET`
   y sont, `MSG_SUMMON` non), et pas de `cmp eax,0x130` dans le répartiteur entrant.

La conclusion de rzu (« Seems unused ») est donc confirmée pour 7.3 par le client lui-même. Elle reste
une preuve par **absence d'émetteur**, pas par impossibilité : voir `NON ÉTABLI` 1 au §7.

## 3. Structure sur le fil

Taille totale attendue : **12 octets** (7 d'en-tête + 5 de charge utile). Aucune trame de ce paquet
n'a été observée : les valeurs des offsets 0 à 6 découlent de cette taille calculée (en-tête 7 octets
du dépôt), pas d'une capture réseau.

| Offset | Taille | Type | Nom | Valeur observée | Source |
| --- | --- | --- | --- | --- | --- |
| 0 | 4 | `uint32` LE | `Length` | 12 (longueur totale, en-tête compris) | `Game/Network/Packets/Header.cs:9` |
| 4 | 2 | `uint16` LE | `ID` | `304` | `Game/Network/Packets/Header.cs:10` ; `op_codes.md:106` |
| 6 | 1 | `uint8` | `Checksum` | aucune (le client n'émet pas la trame) | `Game/Network/Packets/Header.cs:11` ; contrôle en réception `Game/Network/Clients/GameClient.cs:914` |
| 7 | 1 | `int8_t` | `is_summon` | **aucune** — aucun émetteur identifié (§2.3) | rzu `.../GameClient/TS_CS_SUMMON.h:7` ; NGemity `shared/Server/Packets/GameClient/TS_CS_SUMMON.h:8` |
| 8 | 4 | `uint32` LE (`ar_handle_t`) | `card_handle` | **aucune** — aucun émetteur identifié (§2.3) | rzu `.../TS_CS_SUMMON.h:8` ; NGemity `.../TS_CS_SUMMON.h:9` |

**Taille totale attendue : 12 octets** (`7 + 1 + 4`).

- `ar_handle_t` est un `uint32_t` : `reference/rzu/librzu/src/lib/Packet/GameTypes.h:40`
  (`struct ar_handle_t : strong_typedef<ar_handle_t, uint32_t>`).
- Les deux champs sont déclarés `_(simple)` dans rzu : aucun n'est un tableau, une chaîne ou un
  conteneur, donc aucun n'a de taille variable ni de sentinelle à connaître.
- L'en-tête du dépôt est bien de 7 octets (`uint Length`, `ushort ID`, `byte Checksum`,
  `Header.cs:7-11`), et le contrôle de réception recalcule le checksum sur les six premiers octets
  (`GameClient.cs:914`) : une trame de 12 octets dont les octets 0-5 ne donnent pas l'octet 6 est
  refusée en amont, sans que ce paquet y soit pour rien.

### 3.1 Ce que les deux références disent identiquement

| | rzu | NGemity |
| --- | --- | --- |
| ordre des champs | `is_summon` puis `card_handle` | idem |
| type du drapeau | `int8_t` | `int8_t` |
| type du handle | `ar_handle_t` (= `uint32_t`) | `uint32_t` |
| commentaire | `// Seems unused` (`:5`) | `// Seems unused` (`:6`) |

Il n'y a **aucun désaccord de disposition** entre les deux références : c'est le cas le plus simple de
la famille d'invocation. Le seul écart est lexical (`ar_handle_t` contre `uint32_t`, §6).

### 3.2 Le sens des champs n'est pas établi

Les noms eux-mêmes n'engagent à rien : rzu ne documente ni la valeur de `is_summon` (0/1 ? 0/2 ?
énumération tronquée sur un octet ?) ni ce que désigne `card_handle` — handle d'instance d'objet
(comme partout ailleurs dans la famille : `card_handle` de 301 en `TS_SC_ADD_SUMMON_INFO.h:7`, de 303
en `TS_EQUIP_SUMMON.h:7`), code de ressource de carte, ou handle d'invocation. Le §7 les laisse
ouverts ; la fiche refuse de les deviner, et le lecteur ne doit pas s'en servir pour écrire une
politique de jeu.

## 4. Gating de version — décisions prises pour 7.3

rzu déclare deux ids (`.../TS_CS_SUMMON.h:10-12`) :

```
#define TS_CS_SUMMON_ID(X) \
    X(304,  version <  EPIC_9_6_3) \
    X(1304, version >= EPIC_9_6_3)
```

| Champ ou id | gating rzu | **décision pour 7.3** | Justification |
| --- | --- | --- | --- |
| id du paquet | `304` si `version < EPIC_9_6_3`, `1304` sinon | **`304`** | `EPIC_7_3 = 0x070300` (`reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59`) est **inférieur** à `EPIC_9_6_3 = 0x090603` (`:96`, commentaire « GS packet ID modified with version 20200713 ») : la branche 7.3 est celle de `304`. Confirme `op_codes.md:106`. |
| `is_summon` | aucun (`_(simple)`, `:7`) | **présent**, 1 octet en offset 7 | pas de `version >=` sur la ligne |
| `card_handle` | aucun (`_(simple)`, `:8`) | **présent**, 4 octets en offset 8 | pas de `version >=` sur la ligne |

**Piège à ne pas armer : ne pas déclarer `1304`.** En 7.3, `1304` est un **autre** paquet,
`TM_CS_AUCTION_BIDDED_LIST` (`op_codes.md:206`), dont rzu montre le gating inverse — `X(1304,
version < EPIC_9_6_3)` / `X(2304, version >= EPIC_9_6_3)`
(`reference/rzu/librzu/src/packets/GameClient/TS_CS_AUCTION_BIDDED_LIST.h:11-12`). L'id 1304 a donc
été **réaffecté** par 9.6.3 : ventes aux enchères → invocation. Une fiche qui recopierait les deux ids
d'rzu dans `GamePackets` créerait une collision avec la famille des enchères (dont le socle est déjà
écrit, `docs/packet-specs/socle-encheres.md` et `Game/Network/Packets/Enums/GamePackets.cs:118-120`
pour les ids serveur → client voisins). Le dépôt ne porte déjà que l'id 7.3 des autres paquets
remappés (motif identique à `TM_CS_CHAT_REQUEST = 20`, `GamePackets.cs:21`) : **`304` seul**.

## 5. Traitement attendu

### 5.1 NGemity : aucun gestionnaire, et le paquet est « inconnu »

- La table de dispatch du monde (`reference/ngemity/Chihiro/src/Network/GameNetwork/WorldSession.cpp:92-137`)
  contient `onGetSummonSetupInfo` (`:109`), `onEquipSummon` (`:122`) et `onSkill` (`:124`) — **aucun
  `onSummon`**, et `grep -rn "TS_CS_SUMMON" Chihiro/src` ne rend rien : le type n'est même pas
  utilisé.
- 304 n'est pas non plus dans la liste des paquets ignorés en connaissance de cause
  (`ignoredPackets`, `WorldSession.cpp:140-141`). Il tombe donc dans le cas par défaut
  (`:159-160`) : `NG_LOG_DEBUG("server.network", "Got unknown packet '%d' …")`, puis rien. Le
  comportement de référence est donc : **journaliser et ne rien faire**.

### 5.2 Le chemin d'invocation de NGemity est le sort, pas 304

- `WorldSession::onSkill` (`:1074`) reçoit `TS_CS_SKILL` (400) ; la cible est résolue en objet du monde
  (`:1105-1111`) ;
- l'effet `EF_SUMMON = 601` (`Chihiro/src/Skills/SkillBase.h:344`) est préparé par
  `Skill::PrepareSummon` (appel `src/Skills/Skill.cpp:300`, définition `:604-648`) : la cible doit être une carte de
  `ItemGroup::GROUP_SUMMONCARD` possédée par le joueur, être l'une des 6 cartes **liées** de la
  formation, ne pas être déjà dans le monde ;
- il est exécuté par `case EF_SUMMON` (`Skill.cpp:1318-1321`) → `Skill::DO_SUMMON` (`:1537-1558`),
  qui **re-résout la cible comme un item** (`player->FindItemByHandle(m_hTarget)`, `:1543`) et appelle
  `Player::DoSummon` (`src/Entities/Player/Player.cpp:1574`). L'effet symétrique est
  `EF_UNSUMMON = 602` (`SkillBase.h:345-347`).

Autrement dit, la référence porte l'action d'invocation dans `TM_CS_SKILL` (400) avec la carte pour
cible, ce qui correspond exactement au geste décrit par le client au §2.1 (« summon it with summon
creature skill »). **304 est un vestige** : ni émis (client, §2.3), ni traité (NGemity, §5.1).

### 5.3 Ce que NavisLamia doit faire

1. **Déclarer l'id** : `TM_CS_SUMMON = 304` dans `Game/Network/Packets/Enums/GamePackets.cs`, entre
   `TM_EQUIP_SUMMON = 303` (`:72`) et `TM_SC_UNSUMMON = 305` (`:73`) — `304` seul, pas `1304` (§4).
2. **Router l'id** : un bras de réception, sinon une trame 304 atteint le `switch` final et lève
   `throw new Exception("Unknown Packet Type")` (`Game/Network/Clients/GameClient.cs:1369`), ce que le
   critère « enum et dispatch modifiés ensemble » interdit un membre de `GamePackets` de faire.
3. **Lire la trame et ne rien décider** : le bras lit `is_summon` (octet 7) et `card_handle`
   (octets 8-11) et **journalise en Debug** la trame reçue (id, `Length`, les deux valeurs brutes), à
   la manière du bras anti-triche (`GameClient.cs:1348-1356` et
   `Game/Network/Packets/Game/GameAntiHackPackets.cs:35-47`, dont le lecteur expose des valeurs non
   interprétées sans jamais les comparer ni les valider).
4. **Longueur** : `Length` d'une trame conforme vaut 12. Le bras peut journaliser un
   `Length != 12` sans le refuser (la boucle de réception ne garantit que `Length` et `Checksum`,
   `GameClient.cs:914-920`) ; aucune règle de refus n'est établie pour ce paquet.
5. **Aucune politique de jeu** : ni invocation, ni renvoi, ni consommation de carte depuis ce paquet.
   Le chemin d'invocation existe déjà par le sort (400) et le socle d'invocation
   (`docs/packet-specs/socle-invocations.md`, `socle-invocation-monde.md`) ; dupliquer la logique ici
   inventerait une règle que ni le client ni NGemity n'ont.

### 5.4 Ce que le serveur ne doit pas répondre

**Rien.** Aucune réponse n'est établie pour 304 : aucun des deux serveurs de référence n'en émet
(§5.1), et le client n'a pas de gestionnaire entrant pour 304 (§2.3). En particulier, ne pas répondre
`TS_SC_RESULT` : le client possède bien le code `93` — `NotEnoughSummonCard = 93`
(`Game/Network/Packets/ResultCode.cs:112`), vérifié dans la table du client (`SFrame.exe`, table de
pointeurs de chaînes à `0xc10000`, index 93 → `RESULT_NOT_ENOUGH_SUMMON_CARD`, index 99 →
`RESULT_ERROR_MAX`) — mais **aucun élément** ne dit comment le client associerait un
`TS_SC_RESULT` de `request_id = 304` à une fenêtre : il n'a pas de gestionnaire pour cet id. Une
réponse serait donc au mieux ignorée, au pire un affichage erroné. La fiche laisse cette question
ouverte (§7.4) plutôt que d'inventer une trame.

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | NGemity | NavisLamia | Pourquoi c'est assumé |
| --- | --- | --- | --- |
| type du handle | `uint32_t` (`shared/Server/Packets/GameClient/TS_CS_SUMMON.h:9`) | `ar_handle_t` d'rzu, c'est-à-dire `uint32` en C# | écart **lexical** : rzu est l'autorité des tailles et des types de fil, NGemity nomme le type sous-jacent. Aucune différence d'octet, l'ordre et la taille des champs sont identiques. |
| ids multiples | un seul id, pas de gating : `CREATE_PACKET(TS_CS_SUMMON, 304)` (`:11`) | `304` seul, `1304` **documenté et non déclaré** (§4) | NGemity compile un seul Epic par construction (`#define EPIC EPIC_4_1_1`, `shared/Common/Define.h:25`, version de paquets surchargeable par `Game.PacketVersion`, `shared/Configuration/Config.cpp:70`) : son fichier ne peut pas montrer le remap 9.6.3. rzu le montre, la fiche le tranche pour 7.3, et le dépôt ne porte que l'id de sa version cible. |
| traitement | aucun gestionnaire → « Got unknown packet » en Debug (`WorldSession.cpp:159-160`) | bras de réception qui journalise et ne répond pas (§5.3) | même **effet** observable (aucune réponse, aucune mutation), mais NavisLamia doit router explicitement l'id pour ne pas lever `Unknown Packet Type` (`GameClient.cs:1369`) : le dépôt n'a pas de « table de paquets inconnus ». |
| commentaire de la définition | `// Seems unused` | repris et **étayé** par le relevé client (§2.3) | NGemity recopie le commentaire d'rzu sans le vérifier ; la fiche apporte la vérification côté binaire client pour la version cible. |

## 7. NON ÉTABLI

1. **Le client 7.3 émet-il 304 dans un cas non couvert par le balayage ?** Le relevé du §2.3 porte sur
   le `.text` de `SFrame.exe` (motif `mov reg,imm` + store 16 bits, 123 sites ; immédiat `0x130` sous
   toutes ses formes, 4 sites). Il ne couvre pas un id calculé à l'exécution ni un envoi « par nom » :
   le client contient bien une table `id → nom` (celle qui associe `0x130` à `TM_CS_SUMMON`, §2.2),
   dont le consommateur n'a pas été identifié. **Limite matérielle constatée** : les scripts et
   définitions d'interface du client vivent dans `data.000` (83 822 entrées,
   `reference/client73/extraction-manifest.json:3`) et **seuls les `db_*.rdb` ont été extraits** (60
   fichiers listés) : un émetteur scripté n'a donc pas pu être écarté par lecture. Question précise à
   trancher : *existe-t-il, dans les ressources client non extraites, un appel émettant le paquet 304
   (par id ou par nom) ?*
2. **Sémantique de `is_summon`** (`int8_t`, offset 7) : un drapeau 0/1, une énumération, ou un
   compte ? Aucune source ne le dit — ni rzu, ni NGemity (les deux recopient le nom sans
   commentaire), ni le client (aucune occurrence exploitable). Si un jour une trame 304 est
   effectivement reçue, la question se tranchera par comparaison avec l'état d'invocation du joueur
   (invocation présente ou non), **jamais par supposition**.
3. **Ce que désigne `card_handle`** : handle d'instance d'objet (lecture la plus probable par
   analogie avec 301/303), `code` de ressource de carte, ou handle d'invocation
   (`TS_SC_ADD_SUMMON_INFO` porte les deux notions séparément, `card_handle` puis `summon_handle`,
   `docs/packet-specs/socle-invocations.md:170-175`). Non établi.
4. **Faut-il répondre quelque chose, et quoi ?** Aucune réponse n'est établie (§5.4) ; le code
   `93 = NotEnoughSummonCard` existe (`ResultCode.cs:112`, table client vérifiée) mais son association
   à un `request_id` de 304, et l'affichage qui en découlerait, ne sont pas établis.
5. **Le gating 9.6.3 est-il un simple déplacement de table ou un changement de sémantique ?** rzu
   réaffecte 1304 (enchères → invocation) ; la même trame à deux ids signifie au minimum que le
   paquet n'a pas bougé de format. Utile seulement pour un futur portage ≥ 9.6.3, hors périmètre 7.3.

## 8. Commits épinglés

| Référence | Commit | Contenu utilisé |
| --- | --- | --- |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (`packets: fix TS_SC_INVENTORY with older epics`) | `librzu/src/packets/GameClient/TS_CS_SUMMON.h` (structure et gating), `librzu/src/packets/GameClient/TS_CS_AUCTION_BIDDED_LIST.h` (collision d'id), `librzu/src/lib/Packet/GameTypes.h` (`ar_handle_t`), `librzu/src/lib/Packet/PacketEpics.h` (`EPIC_7_3`, `EPIC_9_6_3`) |
| NGemity | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (`Fix compilation issue for GCC`) | `shared/Server/Packets/GameClient/TS_CS_SUMMON.h`, `shared/Server/ClientPackets.h:119`, `Chihiro/src/Network/GameNetwork/WorldSession.cpp` (table de dispatch, `onSkill`, `onEquipSummon`, `ignoredPackets`), `Chihiro/src/Skills/Skill.cpp` (`EF_SUMMON` → `DO_SUMMON`), `Chihiro/src/Skills/SkillBase.h:344-347` |
| Client | `reference/client73/SFrame.exe` (dump de chaînes md5 `7d92f691a46e6768d4cf00e85747e776`, 63 099 lignes) et `db_string.rdb` | nom de paquet, absence d'émetteur, texte d'invocation, table des codes de résultat |
| Dépôt | `a1c495002c290cd1fb2e19edffeb8eff49553f46` (`master` au moment de la rédaction) | `GamePackets.cs`, `Header.cs`, `GameClient.cs`, `ResultCode.cs`, `CLAUDE.md` |

## 9. Bloc destiné à `CLAUDE.md`

Le dev ne modifie pas `CLAUDE.md` (fichier protégé) : ce bloc va dans la description de la MR.

```markdown
- `TM_CS_SUMMON` (304) : **le client 7.3 ne l'émet pas** — l'invocation passe par le sort
  d'invocation de créature (`db_string.rdb:34762`), donc par `TM_CS_SKILL` (400), et NGemity ne le
  traite pas non plus (aucun gestionnaire, « Got unknown packet »). Disposition 7.3 : 7 octets
  d'en-tête + `int8_t is_summon` (7) + `ar_handle_t card_handle` (8-11), **12 octets**. Le second id
  d'rzu, `1304`, est celui de 9.6.3 (`version >= EPIC_9_6_3`) et **ne doit pas être déclaré** : en
  7.3, `1304` est `TM_CS_AUCTION_BIDDED_LIST`. Le bras de réception journalise et **ne répond pas**.
  Détail et réserves : `docs/packet-specs/304-summon.md`.
```

## 10. Tests d'offsets attendus (forme)

Le dépôt n'a pas de capture réseau pour ce paquet : les tests portent sur la **mise en forme attendue**
et sur l'armement du dispatch, pas sur une trame observée.

1. **Taille totale** : la constante du lecteur/formateur de 304 vaut **12** octets (en-tête compris) —
   soit `7 + 1 + 4`, et l'assertion doit nommer séparément les 7 octets d'en-tête et les 5 de charge
   utile, pour que la prochaine modification de l'en-tête casse le test.
2. **Position de chaque champ** : `is_summon` lu à l'offset **7** (un octet, valeur signée) et
   `card_handle` lu aux offsets **8 à 11** en **little-endian**. Le test doit lire les deux champs
   depuis une trame fabriquée à la main (`Length = 12`, `ID = 304`, checksum quelconque) et asserter
   les valeurs exactes, y compris `is_summon = 0` et `card_handle = 0` (le cas « zéro » ne doit pas
   être confondu avec « champ absent »).
3. **Répartition du dispatch** : un test vérifiant que `TM_CS_SUMMON` (304) est routé et qu'aucun
   membre de `GamePackets` ne tombe sur `throw new Exception("Unknown Packet Type")`
   (`GameClient.cs:1369`) — la forme utilisée par les paquets déjà intégrés.
4. **Non-réponse** : si le bras est testable isolément, asserter qu'aucune trame n'est écrite au
   client pour 304 (§5.4). Sinon, le dire dans la MR plutôt que d'écrire un test complaisant.

## 11. Réserves de méthode

- Aucune trame 304 n'a été capturée ni ne peut l'être : le VPS ne démarre pas de client ni de serveur
  de jeu, et aucun émetteur client n'existe côté 7.3 (§2.3). Toute affirmation sur `Length`,
  `Checksum` ou les valeurs des champs est donc **calculée**, jamais observée.
- Le relevé client est statique (chaînes et désassemblage). Il ne remplace pas une exécution, qu'il
  est interdit de faire ici (« ne jamais exécuter `SFrame.exe`, de Lua ni de script du client »).
- Les ressources client au-delà des `db_*.rdb` ne sont pas extraites (voir `NON ÉTABLI` 1) : la
  conclusion « le client n'émet pas 304 » est une absence d'émetteur **dans le binaire**, pas une
  impossibilité démontrée pour l'ensemble du client assemblé.

## 12. Implémentation livrée (dev)

Commit `6eb2982` sur `hermes/packet-304-summon`, après la fiche `aa9999a`.

| Fichier | Livré |
| --- | --- |
| `Game/Network/Packets/Enums/GamePackets.cs:78` | `TM_CS_SUMMON = 304`, entre `TM_EQUIP_SUMMON` (303) et `TM_SC_UNSUMMON` (305), avec le commentaire qui interdit de déclarer `1304` (id 9.6.3, `TM_CS_AUCTION_BIDDED_LIST` en 7.3). |
| `Game/Network/Packets/Game/GameActionPackets.cs:576-621` | `SummonPacketSize = 12`, `SummonPayloadSize = 5`, `SummonFlagOffset = 7`, `SummonCardHandleOffset = 8`, et `TryReadSummon(ReadOnlySpan<byte>, out sbyte isSummon, out uint cardHandle)`. |
| `Game/Network/Clients/GameClient.cs:994-1022` | Bras de réception : lecture, journal **Debug** des deux valeurs brutes, `continue`. Aucune écriture vers le client. |
| `Tests/Game/SummonPacketsTests.cs` | 16 cas : ids (§7.5 compris), voisinage de l'id, constantes de disposition, offsets et endianness, drapeau signé, trames courtes refusées, trame longue lue, et **5 cas de dispatch** contre la vraie boucle de réception. |

Décisions de mise en œuvre — et ce qu'elles ne disent pas :

- Le lecteur lit une trame dès qu'elle fait 12 octets et **accepte une trame plus longue** : §5.3.4
  laisse la disposition d'une longueur non conforme ouverte, le lecteur ne l'invente donc pas
  (même choix que le lecteur anti-triche pour 54). Une trame **plus courte que 12 octets** n'est pas
  lisible (`false`) : le bras journalise un avertissement et **ne refuse rien** — aucun `TS_SC_RESULT`
  n'est émis (§5.4).
- `is_summon` est lu en **signé** (`int8_t`) et `card_handle` en **little-endian** ; aucune des deux
  valeurs n'est comparée, validée ni interprétée (§7.2, §7.3). Un test fixe la lecture de `0xFF` à
  `-1` : c'est une assertion de **type de fil**, pas une sémantique de jeu.
- Le bras est placé **après le bras isolé `TM_SC_REGION_ACK`**, et non à la fin de la chaîne : la zone
  qui précède le `switch` final est déjà partagée par les branches sœurs (`hermes/packet-57-…`,
  `-59-…`, `-60-…`, `-9005-…`, `-4003-…` y insèrent leur bras) et la famille invocation y insère
  aussi. Le commentaire de placement est dans le code.
- Aucune politique de jeu (§5.3.5) : ni invocation, ni renvoi, ni consommation de carte.

Preuves d'exécution (conteneur .NET 8, aucun serveur de jeu démarré) :

| Commande / essai | Résultat |
| --- | --- |
| `dotnet build Navislamia.sln -c Debug` | code 0, 0 erreur |
| `dotnet test Tests/Tests.csproj` | code 0 : **992 réussis, 0 échec, 0 ignoré** |
| Détail du delta | la fiche ajoute 16 cas ; aucun fichier de test existant n'est modifié, la suite passe donc de 976 à 992 sans perte |
| Banc d'essai par mutation | bras re-pointé sur un autre id : **les 5 cas de dispatch échouent** (`Unknown Packet Type` levée dans la boucle réelle) et le lecteur continue de passer ses 11 cas. Le filtre NUnit `~SummonPacketsTests` retient 31 cas : les 15 autres appartiennent à `GameSummonPacketsTests` (homonyme partiel) et passent aussi. |
| `git merge-tree --write-tree --name-only hermes/packet-324-get-summon-setup-info HEAD` | mêmes fichiers en conflit (`ConnectionInfo.cs`, `GameClient.cs`, `GameActionPackets.cs`) que `hermes/packet-324-…` **contre `origin/master` seul** : ce commit n'ajoute aucun conflit. `GamePackets.cs` fusionne automatiquement — l'id 304 et l'id 324 sont insérés à deux endroits distincts. |

## A VERIFIER PAR KILLIAN

| Question ouverte de §7 | État du code livré | Renvoi |
| --- | --- | --- |
| Un émetteur scripté existe-t-il dans les ressources client non extraites (`data.000`) ? | Non tranché et **non deviné**. Le paquet est routé et sans réponse : un émetteur hypothétique ne produirait aujourd'hui qu'une ligne de journal Debug. À rouvrir seulement sur une trame réellement observée. | §7.1 |
| Que signifie `is_summon` (0/1, énumération, compteur) ? | Valeur brute signée, journalisée seulement. Aucun branchement sur sa valeur. | §7.2 |
| Que désigne `card_handle` (instance d'objet, `code` de carte, handle d'invocation) ? | Valeur brute little-endian, journalisée seulement. Aucun usage. | §7.3 |
| Faut-il répondre à 304, et le code `93 = NotEnoughSummonCard` doit-il servir ? | Aucune réponse n'est émise : le client n'a pas de gestionnaire entrant pour 304, la réponse serait au mieux ignorée. | §7.4 |
| Le gating 9.6.3 (`1304`) est-il un déplacement ou un changement de sémantique ? | Hors périmètre 7.3 : `1304` reste **non déclaré**, et un test l'assure. | §7.5 |

## Revue avant fusion (2026-09-23)

Fusion de `origin/master` : un conflit dans `GameClient.cs`, les bras 304 et `TM_CS_REQUEST` (60)
ayant été insérés au même endroit ; les deux sont gardés, 304 d'abord. Deux corrections :

- **`Ids_AreTheEpic73Ones` exigeait que `1304` ne soit jamais déclaré.** Or `1304` est
  `TM_CS_AUCTION_BIDDED_LIST` en 7.3, l'un des sept paquets d'enchères encore à implémenter : le test
  aurait cassé le jour de leur intégration. Il vérifie désormais que `1304` est absent ou déclaré sous
  son nom d'enchère — jamais comme demande d'invocation. Le commentaire de l'énumération dit la même chose.
- Le journal `Debug` du bras porte cinq propriétés : il est gardé par `IsEnabled(LogEventLevel.Debug)`
  (règle de `CLAUDE.md`, *Logging*).

Constaté pendant la revue, **hors de ce paquet** : `TM_EQUIP_SUMMON` (303), que le client émet
(`SMSG_REQUEST_EQUIP_SUMMON`, §2.2), est déclaré dans `GamePackets` sans bras de réception. Une trame
303 atteint le `throw "Unknown Packet Type"`, attrapé par `Connection.OnReceive` : erreur journalisée,
session maintenue, trames coalescées après elle perdues. À traiter avec son propre paquet.

Construction `Release` et `dotnet test` : 1 181 tests, 0 échec.
