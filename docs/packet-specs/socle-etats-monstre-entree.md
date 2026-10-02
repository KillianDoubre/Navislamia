# 505 — `TM_SC_STATE` : les états d'un monstre qui entre dans une vue

Cette fiche ne décrit pas un paquet inconnu : `TS_SC_STATE` (505) est déjà encodé
(`GameSkillPackets.BuildState`, `Game/Network/Packets/Game/GameSkillPackets.cs:168-190`), écrit par les
compétences (`socle-lancer-competences.md`) et retiré par la fenêtre d'états
(`408-request-remove-state.md`). Elle tranche **qui émet 505, quand, à qui, avec quels états**, dans un
seul cas : un monstre qui entre dans la vue d'un joueur. Aujourd'hui ce cas n'émet rien —
« Un monstre qui entre dans une vue n'y amène pas ses états »
(`docs/packet-specs/socle-diffusion-combat.md:55`), et `CLAUDE.md:265` ne mentionne des états diffusés
qu'« à l'entrée d'un pair », c'est-à-dire entre joueurs.

## 1. Identité

- Id décimal **505**, nom **`TM_SC_STATE`**, direction **serveur → client** :
  `op_codes.md:139` (`[505] = "TM_SC_STATE"`, table d'annotations extraite du client 7.3).
- Enfant de ce lot : le paquet **505 n'est pas nouveau** ; le présent document est le socle
  « états d'un monstre à l'entrée », patron jumeau de l'entrée d'un pair
  (`Game/Services/CompanionFrames.cs:62-95`, appelé par `PlayerVisibilityService.cs:152-153,200-201`).
- Paquets voisins, mêmes sources : `TM_SC_ENTER` = 3 (`op_codes.md:6`), `TM_SC_WEAR_INFO` =
  202 (`op_codes.md:52`).

## 2. Ce que le joueur fait pour que le serveur l'envoie

Rien : le client n'envoie aucun paquet pour cela. Les trames sont **poussées par le serveur**, et le
déclencheur est le déplacement du joueur lui-même :

1. le client envoie un déplacement — `TM_CS_MOVE` traité par `GameClient` qui, après avoir écrit la
   nouvelle position, appelle `SyncVisibleObjects()` (`Game/Network/Clients/GameClient.cs:276`, `:302`,
   `:323` ; définition `:811-817`) ;
2. `SyncVisibleObjects()` appelle `MonsterSpawnService.Sync(this)` (`GameClient.cs:814`), qui relit
   l'index spatial autour de la position courante et diffuse ce qui vient d'entrer dans la vue
   (`Game/Services/MonsterSpawnService.cs:21-49`) ;
3. le même geste a lieu à l'entrée en jeu (`Game/Network/Clients/Actions/GameActions.cs:309`).

Autrement dit : **le joueur marche vers un monstre** (ou se téléporte à portée), et le monstre entre
dans sa vue. Le handle du monstre pour ce client est alloué à cet instant précis
(`WorldObjectStreamer.cs:63`), donc un état déjà posé sur le monstre doit être envoyé **dans la même
fenêtre** pour que le client le rattache à la bonne créature (§5.3 point 1).

## 3. Structure sur le fil

`TS_SC_STATE` pour Epic 7.3 : **63 octets** = en-tête 7 + charge 56. Ordre des champs confirmé
indépendamment par le binaire du serveur officiel (§5.2), qui écrit `length = 0x3f` (63) et pose chaque
champ aux offsets ci-dessous.

| Offset | Type | Champ | Valeur observée | Source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` | `length` | **63** (`0x3f`) | officiel : `movl $0x3f,0x20(%rsp)` @`0x14014cd9e` ; `GameSkillPackets.cs:176` |
| 4 | `uint16` | `id` | **505** (`0x1f9`) | officiel : `movw $0x1f9,0x24(%rsp)` @`0x14014cd97` ; `GameSkillPackets.cs:177` |
| 6 | `uint8` | checksum | somme des 6 premiers octets | `GameSkillPackets.cs:197-202` (convention du dépôt, déjà validée en jeu) |
| 7 | `uint32` | `handle` | handle **du monstre pour ce client** (celui de l'`ENTER`) | officiel : `mov %edx,0x27(%rsp)` @`0x14014cd7e` ; rzu `handle` (`TS_SC_STATE.h:8`) ; `GameSkillPackets.cs:179` |
| 11 | `uint16` | `state_handle` | `ActiveBuff.StateHandle` (UID d'instance) | officiel : `state+0x8` → `0x2b(%rsp)` @`0x14014cd82` ; rzu `TS_SC_STATE.h:9` ; `ActiveBuff.cs:4` |
| 13 | `uint32` | `state_code` | `ActiveBuff.StateId` | officiel : `state+0xc` → `0x2d(%rsp)` @`0x14014cd8f` ; rzu `TS_SC_STATE.h:12` ; `ActiveBuff.cs:5` |
| 17 | `uint16` | `state_level` | `ActiveBuff.StateLevel` | officiel : `state+0x10` → `0x31(%rsp)` @`0x14014cdcd` ; rzu `TS_SC_STATE.h:13` ; `ActiveBuff.cs:7` |
| 19 | `uint32` | `end_time` | `ActiveBuff.EndTick` (`0xffffffff` si le drapeau `state+0x3c` est posé) | officiel : `state+0x20` → `0x33(%rsp)` @`0x14014cde1`, `or $0xffffffff` @`0x14014cdd4` ; rzu `TS_SC_STATE.h:15` ; `ActiveBuff.cs:9` |
| 23 | `uint32` | `start_time` | `ActiveBuff.StartTick` | officiel : `state+0x1c` → `0x37(%rsp)` @`0x14014cde7` ; rzu `TS_SC_STATE.h:16` ; `ActiveBuff.cs:8` |
| 27 | `int32` | `state_value` | **0** (le dépôt n'a pas de valeur par état de monstre) | officiel : `state+0x40` → `0x3b(%rsp)` @`0x14014cda9` ; rzu `TS_SC_STATE.h:17` ; `GameSkillPackets.cs:186` |
| 31 | `uint8[32]` | `state_string_value` | 32 octets **à zéro** | officiel : copie de la chaîne `state+0x44` (vide en pratique) @`0x14014cdb0-0x14014cdbd` ; rzu `TS_SC_STATE.h:18` ; `GameSkillPackets.cs:171` |

Chemin **retrait** (le même paquet, `bIsCancel != 0`) : `state_level`, `end_time` et `start_time` sont
mis à zéro, `state_value` et la chaîne restent ceux de l'état — officiel : saut vers `0x14014ce23`
(`jne` @`0x14014cdc2`), zéros posés @`0x14014ce25`, @`0x14014ce2c`, @`0x14014ce30`. C'est ce que fait
`BuildStateRemoval` (`GameSkillPackets.cs:192-195`), sauf pour `state_value`/la chaîne, déjà nuls ici.

## 4. Gating de version

Valeurs de référence : `EPIC_3 = 0x030100`, `EPIC_4_1 = 0x040100`, `EPIC_9_5_2 = 0x090502`,
`EPIC_9_6_3 = 0x090603` (`reference/rzu/librzu/src/lib/Packet/PacketEpics.h:49,50,73,96`, et
`EPIC_7_3 = 0x070300` `:59`). La cible du serveur est **`EPIC_7_3 = 0x070300`**.

| Champ / id | Gating montré par rzu | Décision pour Epic 7.3 | Source |
| --- | --- | --- | --- |
| id | `X(505, version < EPIC_9_6_3)` / `X(1505, >=)` | **505** (`0x070300 < 0x090603`) | rzu `TS_SC_STATE.h:20-22` |
| `handle` | aucun | `uint32` @7 | rzu `TS_SC_STATE.h:8` |
| `state_handle` | aucun | `uint16` @11 | rzu `TS_SC_STATE.h:9` |
| `state_level` | `int8` avant 4.1 ; **`uint16` après `state_code`** entre 4.1 et 9.5.2 ; `uint16` **avant** `state_code` depuis 9.5.2 | **`uint16` @17, après `state_code`** | rzu `TS_SC_STATE.h:10-14` |
| `state_code` | aucun | `uint32` @13 | rzu `TS_SC_STATE.h:12` |
| `end_time` | aucun | `uint32` @19 | rzu `TS_SC_STATE.h:15` |
| `start_time` | `version >= EPIC_3` | **présent** @23 | rzu `TS_SC_STATE.h:16` |
| `state_value` | `version >= EPIC_4_1` | **présent** @27 | rzu `TS_SC_STATE.h:17` |
| `state_string_value` | `version >= EPIC_4_1 && version < EPIC_9_5_2` | **présent**, 32 octets @31 | rzu `TS_SC_STATE.h:18` |

**Tout le gating est tranché.** C'est le piège nommé dans `CLAUDE.md:929-933` :
`state_level` est **après** `state_code` en 7.3, rzu ne le déplace avant qu'à partir de 9.5.2 — et la
chaîne de 32 octets **existe** en 7.3 (elle disparaît à 9.5.2), ce qui est précisément ce qui donne
63 octets plutôt que 31. Vérification indépendante : le serveur officiel de 2012-11 (Epic 7.x) écrit
`length = 63` et `state_level` à l'offset **17**, après `state_code` (§3, §5.2).

## 5. Traitement attendu

### 5.1 Ce que NGemity en fait : rien à l'entrée (c'est le trou)

`reference/ngemity/Chihiro` (commit `38ceb2c6065fabf6ff4ba71d52f955f362c6c839`) :

- l'entrée d'un objet dans la vue d'un client passe par `Messages::sendEnterMessage`
  (`src/Network/Messages.cpp:393-405`) qui ne fait que `pObj->SendEnterMsg(pPlayer)`
  (`src/Entities/Object/Object.cpp:371`) puis, si l'objet bouge, `SendMoveMessage` — **aucune trame
  d'état**. Tous les chemins d'entrée y aboutissent : `SendEnterMessageEachOtherFunctor::Run` et
  `SendEnterMessageFunctor::Run` (`src/Functors/Functors.cpp:25-45`), `WorldSession::onQuery`
  (`src/Network/GameNetwork/WorldSession.cpp:851-857`) ;
- les états ne sortent qu'à la pose, à l'expiration et au retrait : `Unit::onUpdateState`
  (`src/Entities/Unit/Unit.cpp:1796-1799`) → `Messages::BroadcastStateMessage`
  (`src/Network/Messages.cpp:775-798`), qui broadcast dans la **région courante**
  (`sWorld.Broadcast`, `:797`) ; l'invocation prévient son maître
  (`src/Entities/Summon/Summon.cpp:557`) ;
- conséquence : chez NGemity, **un observateur qui arrive après la pose ne voit jamais l'icône** —
  c'est exactement le manque que `socle-diffusion-combat.md:55` constate dans Navislamia. Ce n'est donc
  pas un écart à porter, mais un trou à combler, et la référence qui le comble est le serveur officiel
  (5.2).

### 5.2 Ce que fait le serveur officiel : l'ENTRE est suivi des états

Binaire `reference/epic7part4/server/2012-11/CaptainHerlockServer.exe` (Epic 7.x, 2012-11), lu
statiquement (`objdump`), symboles de `CaptainHerlockServer.symbols.tsv` :

- **`SendEnterMsg(struct ArObject const*, struct ArObject const*)` @`0x1401515e0`**
  (`CaptainHerlockServer.symbols.tsv:3132`) est la fonction qui fait entrer un objet dans la vue d'un
  joueur. Elle construit la trame `TM_SC_ENTER` (`id = 3` @`0x140151617`, `length = 0x1a`
  @`0x140151621`), la `PendMessage` au socket de l'observateur (`0x14015207a`, destinataire =
  `0x38(%rsp)` = 1ᵉʳ argument), puis, quand l'octet de genre écrit dans cette trame vaut 0
  (`cmpb $0x0,0x227(%rsp)` @`0x1401520b3`), une trame **202** (`id = 0xca` @`0x1401520d7`,
  `length = 0x1a3` @`0x1401520de`, remplie par `GetWearMsg` @`0x1401520e6`, `PendMessage`
  @`0x140152125`) — et **ensuite les états** :
  - trois sites appellent `StructCreature::IsNeedStateNotify()` à l'intérieur de cette fonction :
    @`0x140151893`, @`0x140151ccb`, @`0x140151dc9` ;
  - `StructCreature::IsNeedStateNotify()` @`0x1401452f0`
    (`symbols.tsv:2973`) se lit `0x1401452f0-0x140145328` : début non nul **et**
    `(end - begin) / 0x2c != 0` — soit « **la liste d'états de la créature n'est pas vide** »
    (`0x2c` = 44 = `sizeof(StructState)`, `movabs $0x4ec4ec4ec4ec4ec5` @`0x140145306`) ;
  - quand le prédicat est vrai, l'objet est mémorisé dans `%rbp` et le drapeau `%bl` est posé
    (`0x14015189c-0x1401518aa`, `0x140151b95-0x140151b98`, `0x140151cd8-0x140151cdb`) ;
  - après l'ENTRE et la trame 202, `test %bl,%bl` puis `test %rbp,%rbp` (`0x140152131-0x14015213c`)
    gardent une **boucle sur le conteneur d'états de l'objet** (`0xe38(%rbp)`/`0xe40(%rbp)`,
    `0x140152142-0x1401521b3`) qui, par état, appelle le getter virtuel de handle de l'objet
    (`0x14015218c-0x140152193`) et émet :
    `SendStateMessage(observateur, handle, &state, bIsCancel = 0)` —
    `xor %r9d,%r9d` @`0x140152195`, `call 0x14014cd30` @`0x1401521a0`.
- **`SendStateMessage(struct StructPlayer const*, unsigned int, struct StructState const*, bool)`
  @`0x14014cd30`** (`symbols.tsv:3074`) est exactement l'encodeur de §3 : longueur 63, id 505, puis
  les huit champs, et zéro sur `state_level`/`end_time`/`start_time` quand `bIsCancel` est vrai
  (`0x14014ce23`). La trame est poussée par `PendMessage` (`call 0x140149350` @`0x14014ce68`) après
  deux gardes (`call *0x30(%rax)` sur le joueur @`0x14014ce3a`, session `0x23d0(%rbx)` non nulle
  @`0x14014ce41` puis `call *0x18(%rax)` @`0x14014ce55`).
- **Le handle passé à `SendStateMessage` est celui de l'`ENTER`** : dans les deux cas c'est le getter
  virtuel du même objet (`call *(%rax)` sur l'objet entrant) — `0x14015169a` pour la trame
  d'entrée, `0x140152193` pour la boucle d'états. C'est ce qui permet au client de rattacher l'état à
  la créature qu'il vient de créer.

**Conclusion d'autorité** : entre deux visiteurs, le serveur officiel envoie `ENTER` (3) →
`WEAR_INFO` (202) → **un 505 par état actif**, dans le même appel, sous le handle de l'ENTRE, et
**rien** quand la créature ne porte aucun état (le prédicat est demandé, pas deviné). Les états d'un
monstre voyagent donc par le même paquet que ceux d'un joueur, à l'identique.

**Et immédiatement, pas au tick suivant** : c'est le même appel, sous le même verrou, sans point de
reprise. Un délai d'un tick serait nuisible dans les deux sens — une pose survenue entre-temps part
déjà vers les observateurs par le chemin de pose (l'observateur est enregistré dès la ligne 66 de
`WorldObjectStreamer`), et l'instantané d'états la renverrait une seconde fois ; et un état venu à
échéance dans l'intervalle serait annoncé échu. Le seul ordre sûr est celui de l'officiel : lecture des
états **dans** la fenêtre qui vient de créer le handle.

### 5.3 Ce que le serveur Navislamia doit faire

1. **Un seul point d'insertion.** `GameSpawnPackets.BuildEnterMonster` n'a **qu'un seul site de
   production** : `MonsterSpawnService.cs:36` (les autres occurrences sont des tests :
   `Tests/Game/SpawnPacketsTests.cs:56,93`, `SummonWorldTests.cs:187`, `PetWorldTests.cs:264`,
   `PkModeStatusTests.cs:144`). Un monstre n'entre dans une vue que par
   `MonsterSpawnService.Sync` → `WorldObjectStreamer.Stream` (`MonsterSpawnService.cs:29-43`,
   `WorldObjectStreamer.cs:30-76`) : c'est là que les états doivent partir, à côté de l'ENTRE.
2. **Même verrou, même handle.** `WorldObjectStreamer.Stream` tient le verrou de visibilité du client
   (`lock (visibilityLock)` `WorldObjectStreamer.cs:42`, verrou = `info.MonsterVisibilityLock`,
   `ConnectionInfo.cs:286`) et appelle le constructeur avec le handle qu'il vient d'allouer
   (`buildEnter(item, handle)` `:64`, `handle = WorldObjectHandle.Next()` `:63`). Les trames d'états
   doivent partir **sous ce même verrou** et **avec ce même handle** : le handle d'un monstre est
   propre à chaque client (`ObserverFrames.cs:9-11`, `WorldObjectHandle.cs:5-11`), et une trame bâtie
   avec un autre handle désignerait une créature que ce client ne connaît pas.
   - Détail qui décide de la correction : `handlesById[id] = handle` est écrit **après**
     `client.Connection.Send(buildEnter(item, handle))` (`WorldObjectStreamer.cs:64-66`). Une trame
     d'états émise avant cette ligne peut donc courir contre le chemin de pose, qui retrouve
     l'observateur par `GetMonsterHandle` (`MonsterSkillService.cs:161-167`,
     `ObserverFrames.cs:30`) : il lirait `0`, ne lui enverrait rien, et **le client ne verrait jamais
     cet état**. La sortie doit donc être **après** l'enregistrement du handle (l. 66). C'est la
     lecture de ce fichier qui l'impose, pas une préférence.
3. **Quels états.** `MonsterWorldState.GetStates(instanceId)` (`MonsterWorldState.cs:295-303`) rend une
   **copie** prise sous `_stateLock` (`:42`) ; l'itérer ensuite ne tient plus le verrou d'état.
   `ActiveBuff` (`Game/Services/Buffs/ActiveBuff.cs:3-9`) porte tout ce qu'il faut :
   `StateHandle`, `StateId`, `StateLevel`, `StartTick`, `EndTick`.
   - **Ordre des verrous** : on prend donc `MonsterVisibilityLock` **puis** `_stateLock`. L'ordre
     inverse n'existe nulle part dans le dépôt (aucune méthode de `MonsterWorldState` n'appelle
     l'extérieur, et le chemin de pose envoie ses trames **après** le retour de `AddState`,
     `MonsterSkillService.cs:384-390`) — à conserver tel quel, c'est la même discipline que celle
     documentée pour les joueurs (`CompanionFrames.cs:62-66`).
4. **Filtrer les états morts.** Ne garder que `unchecked((int)(now - state.EndTick)) < 0`, c'est-à-dire
   exactement le test d'expiration de `MonsterWorldState.RemoveExpiredStates`
   (`MonsterWorldState.cs:321-325`), avec `now = ServerClock.Now` (comme le tick d'expiration,
   `SkillCastService.cs:1293`). Raison : le tick d'expiration tourne toutes les **500 ms**
   (`SkillCastService.cs:41`, `:1288-1295`) ; sans ce filtre, un monstre qui entre dans cet intervalle
   annonce au client un état déjà échu, juste avant la trame de retrait. Réserve §7.
5. **La trame, par état retenu** — le même appel que le chemin de pose existe déjà :
   `GameSkillPackets.BuildState(handle, state.StateHandle, (uint)state.StateId,
   (ushort)state.StateLevel, state.EndTick, state.StartTick)`
   (`MonsterSkillService.cs:386-390`, encodeur `GameSkillPackets.cs:168-190`). 63 octets,
   `state_value = 0`, `state_string_value` à zéro.
6. **Rien quand il n'y a rien.** Aucune lecture de la carte d'états pour un monstre qui n'entre pas
   réellement : la discipline existante est déjà écrite sur place
   (`MonsterSpawnService.cs:33-34` et `:41-43`, `canEnter`) et doit rester vraie — le prédicat
   officiel `IsNeedStateNotify()` (§5.2) dit la même chose, un cran plus loin.
7. **Un seul observateur.** La boucle est par client (`WorldObjectStreamer` est appelé par
   `MonsterSpawnService.Sync(client)`), donc les états partent vers **le client dont la vue vient de
   recevoir le monstre**, sous son handle : pas de broadcast, pas d'`ObserverFrames` ici. Le même
   client reçoit l'ENTRE et ses états dans le même souffle, comme le serveur officiel.
8. **Ordre.** `ENTER` d'abord, états ensuite — l'ordre du serveur officiel (§5.2), et le seul que la
   lecture du client 7.3 soutienne (5.4). Navislamia n'émet pas de trame 202 pour un
   monstre : `TM_SC_WEAR_INFO` est la tenue d'un joueur (`op_codes.md:52`) et l'ENTRE d'un monstre
   porte déjà ses infos de créature (`GameSpawnPackets.cs:65`).
9. **Cas déjà couverts, à ne pas casser.** Un monstre qui sort puis rentre dans la vue reçoit un
   ENTER neuf et un handle neuf (`WorldObjectStreamer.cs:90-103` : `LEAVE` puis oubli) : après ce lot
   il recevra aussi ses états neufs, sans doublon possible côté client. La pose d'un état sur un
   monstre déjà visible continue de partir vers les observateurs par le chemin existant
   (`ObserverFrames.SendMonsterFrame`, `MonsterSkillService.cs:388-390`) — ce lot ne le remplace pas.

### 5.4 Lecture du client 7.3 (statique, sans exécution)

`reference/client73/SFrame.exe` (`pei-i386`, base `0x400000`, `.text` `0x401000-0xa0b8cf`) :

- la classe de message **`SMSG_STATE`** existe : `.?AUSMSG_STATE@@` à `.data` VA `0xc11725`
  (offset fichier `0x80f925`), juste après `.?AUSMSG_ENTER@@` (`0xc115d9`) ;
- l'annotation `TM_SC_STATE` est présente à `.rdata` VA `0xa53350` (offset fichier `0x651d50`) ;
- le répartiteur de `SGameInterface` traite `MSG_STATE` : la trace `SGameInterface - MSG_STATE`
  (`.rdata` VA `0xa4b69c`) n'est référencée qu'une fois, depuis `.text 0x63c1ea`, fin du `case`. Le
  corps du case (`0x63c163-0x63c1e5`) lit le **handle du message** (`mov 0x13(%esi),%ecx`,
  `0x63c16d`), le compare au handle d'un objet atteint par l'interface (`mov 0x10(%edi),%ecx` →
  `0x210(%ecx)` → `0x1e8(%ecx)`, appels `0x52a3f0`/`0x4e8f00`), et **saute à la fin du case**
  (`je 0x63c1ea`, `0x63c182`, `0x63c1a4`) dès que la comparaison ou le test de drapeau échoue ;
- la chaîne de handlers nommée par le PDB client de référence
  (`reference/epic7part4/client-pdb/2011-12-14-part4-design/SFrame_Release.symbols.tsv`) range l'état
  **dans un avatar** : `SGameSystem::OnMsgState(SMSG_STATE *)` (l. 3787),
  `SGameInterface::add_ui_state(SMSG_STATE *)` (l. 11718),
  `SGameAvatarEx::AddState(SMSG_STATE *)` (l. 15276), `…::GetOldStateInfo` / `DeleteState`
  (l. 15136, 15166).

Autrement dit, côté client l'état est ajouté **à une créature déjà connue**, et le répartiteur abandonne
le message quand la cible ne correspond pas. Cela **soutient** l'ordre du serveur officiel (ENTRE, puis
états) ; cela ne **prouve** pas qu'un 505 précédant l'ENTRE soit ignoré (voir §7.1). Aucun Lua, aucun
script, aucune exécution du client n'a été lancé : tout est lecture.

## 6. Écarts assumés avec NGemity, et pourquoi

| Sujet | NGemity | Navislamia | Pourquoi |
| --- | --- | --- | --- |
| États à l'entrée d'un objet dans une vue | aucun (`Messages.cpp:393-405`) | un 505 par état actif | le manque est constaté (`socle-diffusion-combat.md:55`) et le serveur officiel le comble à cet endroit exact (§5.2) |
| Diffusion | broadcast à la région (`Messages.cpp:775-798`) | par observateur, avec **son** handle | un handle de monstre est propre à chaque client (`ObserverFrames.cs:9-11`) ; le serveur officiel fait de même (`0x140152193`, getter virtuel par observateur) |
| Encodeur | `TS_SC_STATE` rempli par `Messages::SendStateMessage` (`Messages.cpp:1105-1126`) | `GameSkillPackets.BuildState` | identique, 63 octets, même ordre de champs (§3) |
| Aura d'un monstre | `end_time = -1` si `IsAura()` (`Messages.cpp:787-792`) | pas d'équivalent : les auras sont des 407 de joueur | le dépôt ne modélise aucune aura de monstre (`MonsterWorldState` ne porte que des états) — non concerné par ce lot (§7.4) |
| `state_value` / chaîne | portés par l'état (`Messages.cpp:794-795`) | toujours 0 / 32 zéros pour un monstre | `ActiveBuff` n'a pas de champ de valeur (`ActiveBuff.cs:3-9`) ; les états de monstre du dépôt n'en produisent pas |

## 7. NON ÉTABLI / A VERIFIER PAR KILLIAN

1. **Client 7.3 : un 505 sans ENTER préalable.** Non prouvé. Ce qui est établi : le corps du case
   `MSG_STATE` abandonne le message quand la cible ne correspond pas (5.4). Ce qui ne l'est pas : que
   le client *jette* un état arrivé avant la créature. **Décision** : suivre l'ordre du serveur
   officiel (ENTRE puis états) — rien n'oblige à supposer une tolérance (aucune exécution de client
   possible sur ce VPS).
2. **États échus à l'entrée.** La boucle d'états du serveur officiel (`0x140152142-0x1401521b3`) ne
   compare **aucune** horloge : rien n'établit qu'il ne transmette pas un état échu entre deux
   `proc`. **Décision** : filtrer avec le test d'expiration du dépôt (§5.3 point 4) — une réserve
   vérifiable, pas une supposition.
3. **Genre d'objet par branche.** Que les trois branches d'`IsNeedStateNotify` correspondent à des
   genres précis (champ `+0x78` de l'objet) n'est pas tranché ; ce qui l'est : la boucle d'états est
   gardée par le prédicat sur l'objet entrant et utilise le handle de l'ENTRE (§5.2). Sans effet sur
   le dev, qui n'a qu'un seul genre à traiter (monstre).
4. **`state_value` et chaîne de 32 octets.** Le dépôt n'a aucune source de valeur pour un état de
   monstre : les trames porteront 0 et 32 zéros. L'official montre **où** ces valeurs vivent
   (`state+0x40`, `state+0x44`), pas de quel champ de compétence elles viennent.
5. **État de monstre à durée infinie.** L'official envoie `end_time = 0xffffffff` quand le drapeau
   `state+0x3c` de l'état est posé (§3). Le dépôt n'a pas d'équivalent (`EndTick` est toujours un
   échéancier) : hors périmètre de ce lot, à traiter le jour où une compétence de monstre posera un
   état permanent.
6. **`IsNeedStateNotify()`** a été lu par son nom dans les symboles et par son corps
   (`0x1401452f0-0x140145328`) ; le conteneur d'états n'est pas un `std::map` (comptage par division
   de `0x2c`), ce qui ne change rien au comportement observé (« liste non vide »).
7. **Données réelles de Killian** : ni la durée, ni le `state_level` des états de monstre atteignables
   dans son Arcadia n'ont été vérifiés ici. Le dev fera la preuve avec un état posé par une
   compétence de monstre (`socle-competences-monstres.md`), comme le chemin de pose le fait déjà.

## 8. Commits et binaires épinglés

| Référence | Version / empreinte | Usage |
| --- | --- | --- |
| `reference/rzu` | commit `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | `librzu/src/packets/GameClient/TS_SC_STATE.h` : structure, gating, id |
| `reference/ngemity` | commit `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | `Chihiro` : entrée sans états, diffusion, encodeur |
| `reference/epic7part4/server/2012-11/CaptainHerlockServer.exe` | sha256 `83b54fe18f15601578e35bcad1832a5c254bb09f512ab6297217fa49d9dd3eac` | autorité : `SendEnterMsg`, `SendStateMessage`, `IsNeedStateNotify` |
| `reference/epic7part4/server/2012-11/CaptainHerlockServer.symbols.tsv` | sha256 `aef7e6bc8d436a3641fa5a589b53c557ae940e6b6ed4b555a648f8aab7c7e7f3` | noms des fonctions officielles |
| `reference/client73/SFrame.exe` | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | client cible (`TM_SC_STATE`, `SMSG_STATE`, case `MSG_STATE`) |
| `reference/epic7part4/client-pdb/2011-12-14-part4-design/SFrame_Release.symbols.tsv` | sha256 `018f4f576c62e0818b78bed4643eaa115102a46c93037c65da75094ffbbe2993` | build client le plus proche de 7.3 : noms de la chaîne d'états |
| `Navislamia` (base de la branche) | `master` `d9d013a8e4c392771626d26b07d97d7f13753121` | état du dépôt au moment de la fiche |

Lecture seule : `objdump -d/-h/-s` et recherche d'octets sur les binaires, `strings` sur le client,
`grep` dans les sources. Aucun exécutable du client ni du serveur officiel n'a été lancé.

## 9. Note de livraison

- Branche : `hermes/packet-socle-etats-monstre-entree`, créée depuis `master`
  `d9d013a8e4c392771626d26b07d97d7f13753121`. Cette fiche est le **seul** fichier du lot : elle ne
  touche **aucun** fichier de code, ni `GamePackets`, ni `GameClient`, ni les services — elle
  **n'ajoute et ne modifie aucun test**.
- Le répertoire `docs/packet-specs/` est déjà autorisé sur `master`
  (`.gitignore:475`, `!/docs/packet-specs/`) : rien à décommenter, contrairement au premier lot.
- Le dev qui reprend la branche implémente les points 1 à 9 de §5.3, écrit le **test d'offsets** (taille
  totale 63 octets et position de chaque champ — discipline du dépôt) **et** un test qui prouve les
  deux moitiés du critère de la carte : (a) un monstre porteur d'états actifs fait suivre son ENTER
  d'une trame 505 **par état**, avec le handle de ce client ; (b) un monstre sans état actif ne produit
  **que** l'ENTRE. Il recopie ensuite le bloc de §10 dans la description de la MR — le dev **n'écrit
  pas** `CLAUDE.md`.
- Pièges à ne pas manquer : (a) l'enregistrement du handle est **après** l'envoi de l'ENTRE
  (`WorldObjectStreamer.cs:64-66`) : les états partent après l'enregistrement, sinon une pose
  concurrente les perd (§5.3 point 2) ; (b) ne pas lire la carte d'états pour un monstre qui n'entre
  pas (`MonsterSpawnService.cs:33-34`, `:41-43`) ; (c) l'ordre des verrous
  `MonsterVisibilityLock` → `_stateLock`, jamais l'inverse (§5.3 point 3) ; (d) `state_level` reste
  **après** `state_code` tant que la cible est 7.3 (§4).

## 10. Bloc pour CLAUDE.md (à recopier dans la description de la MR)

```markdown
- **Un monstre qui entre dans une vue amène ses états** (socle des états à l'entrée, jumeau du
  « ENTER puis états » des joueurs) : `MonsterSpawnService.Sync` fait suivre l'`ENTER` (3) d'un
  `TM_SC_STATE` (505) **par état actif**, sous le **même** `info.MonsterVisibilityLock` et avec le
  **handle de ce client** — celui de l'`ENTER`, alloué par `WorldObjectStreamer` (`:63-66`).
  Les trames partent **après** `handlesById[id] = handle` : une pose concurrente retrouve l'observateur
  par `GetMonsterHandle` et perdrait l'état sinon. Rien à envoyer quand la liste est vide.
- **Autorité** : le serveur officiel de 2012-11 fait exactement cela dans `SendEnterMsg`
  (`0x1401515e0`) — `ENTER`, puis `WEAR_INFO` (202), puis la boucle d'états
  (`0x140152142-0x1401521a0`), gardée par `StructCreature::IsNeedStateNotify()`
  (`0x1401452f0`, « liste d'états non vide ») et encodée par `SendStateMessage`
  (`0x14014cd30`). **NGemity n'en fait rien** (`Messages.cpp:393-405`) : son trou est comblé ici, pas
  contourné.
- **505 = 63 octets** (rappel `CLAUDE.md` déjà en place) : `handle` @7, `state_handle` u16@11,
  `state_code` @13, `state_level` u16@17 — **après** `state_code` en 7.3, c'est 9.5.2 qui le déplace —
  `end_time` @19, `start_time` @23, `state_value` i32@27, `state_string_value[32]` @31. Le serveur
  officiel écrit `length = 0x3f` et le même ordre : la lecture du binaire tranche le gating rzu.
- **Verrous** : `MonsterVisibilityLock` (visibilité du client) peut être pris **avant** `_stateLock`
  (`MonsterWorldState.GetStates` rend une copie), jamais l'inverse ; le chemin de pose envoie ses
  trames après le retour de `AddState`, comme aujourd'hui.
- **Réserve** : qu'un état **échu** mais pas encore `proc` (tick de 500 ms) ne soit pas annoncé est une
  décision du dépôt (filtre `unchecked((int)(now - EndTick)) < 0`), pas une observation de l'officiel ;
  la tolérance du client à un 505 précédant l'`ENTER` n'est pas prouvée (lecture statique seulement).
```
