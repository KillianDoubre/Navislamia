# 408 — TM_CS_REQUEST_REMOVE_STATE

Fiche de paquet établie par `navis-ref` (archéologue de protocole), branche
`hermes/packet-408-request-remove-state`, à partir de `master`
`6a982c81e6c87eb6dfca37fe3baf432d811ad1c7`. Cette fiche ne contient **aucun** changement de
code serveur : elle fixe le format, le gating de version, le comportement attendu et les réserves
vérifiables.

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id décimal | **408** (`0x0198`) | `op_codes.md:131` — `[408] = "TM_CS_REQUEST_REMOVE_STATE"` |
| sens | client → serveur | rzu `librzu/src/packets/GameClient/TS_CS_REQUEST_REMOVE_STATE.h:13` (`SessionPacketOrigin::Client`) |
| nom serveur | `TM_CS_REQUEST_REMOVE_STATE` | `op_codes.md:131` |
| taille de trame | **15 octets**, fixe | §3 |
| réponse associée | **aucune paire rzu** ; le client traite `TM_SC_STATE` (505) pour retirer l'icône | §5, §7.1 |
| état dans le dépôt | **absent** de `GamePackets` (ni 408, ni 405, ni 406) | `Game/Network/Packets/Enums/GamePackets.cs:42-48,51` |
| type | requête d'action, non idempotente | §5.3 |
| carte Trello | `trello.com/c/zAHsOacK` | suivi `navislamia:packet:408` |

L'opcode est encadré par `[406] = "TM_SC_STATE_RESULT"`, `[407] = "TM_SC_AURA"` et
`[410] = "TM_CS_JOB_LEVEL_UP"` (`op_codes.md:129-132`). Le dépôt porte déjà 400/401/402/403/404
(`GamePackets.cs:42-46`), 407 (`:47`) et 410 (`:48`), ainsi que `TM_SC_STATE = 505` (`:51`) : la
bande « compétences / états » existe, mais **408 n'y a jamais été ajouté**, et le paquet n'a donc
jamais été traité.

Précision de vocabulaire, utile pour toute la suite : le client 7.3 nomme la classe interne
`SIMSG_REQUEST_REMOVE_STATE` (le `U` du nom manglé MSVC
`.?AUSIMSG_REQUEST_REMOVE_STATE@@` décore le pointeur, ce n'est pas un `UIMSG`) — c'est-à-dire un
message **interne** UI → logique réseau, pas un message reçu.

## 2. Ce que le joueur fait pour que le client l'envoie

Le client 7.3 embarque **deux** fenêtres d'états : `window_main_state_h_effect.nui`
(dump `strings -n 4`, l. 24844) et `window_target_state_h_effect.nui` (l. 24820). La grille d'icônes
y est nommée `state_effect_h_icon%02d` (l. 22229), avec `state_effect_h_clock%02d`,
`state_effect_h_reiteration%02d`, `state_effect_syc_icon%02d`, `state_icon%02d`
(dump l. 22229-22240).

La chaîne, lue en désassemblant `SFrame.exe` :

1. deux méthodes distinctes construisent `SIMSG_REQUEST_REMOVE_STATE` : `0x00596f10` et `0x005ad650`.
   Toutes deux n'entrent dans leur corps que si **l'identifiant de message interne vaut 515**
   (`cmpl $0x203,0x8(%ebp)`, `0x00596f2b` et `0x005ad66b`) ; les deux têtes de fonction sont
   identiques (`0x009e55d0` comme descripteur d'exception) et correspondent donc à deux fenêtres
   jumelles, pas à deux actions différentes ;
2. chacune parcourt les 40 emplacements d'icônes en construisant le nom d'élément
   `state_effect_h_icon%02d` (format `0x00a300bc`, boucle `i = 1..40`, `0x00596f40-0x00596fb2`
   et `0x005ad680-0x005ad6f8`) et cherche la **première** dont l'état affiché correspond à celui
   visé ;
3. l'état candidat est résolu en descripteur, et le client exige que ce descripteur porte le
   **bit `0x20` de son mot de drapeaux** : `testb $0x20,0x10(%eax)` (`0x00596fdc`, idem
   `0x005ad720`). Un état sans ce bit ne produit **aucune** requête. Le bit `0x20` est
   `1 << 5` : `EraseOnRequest = 32` (`Game/DataAccess/Entities/Enums/StateTimeType.cs:14`) =
   `AF_ERASE_ON_REQUEST = (1 << 5)` (`reference/ngemity/Chihiro/src/Skills/StateBase.h:27`) ;
4. le message est finalement rempli avec **deux `uint32`** : le handle de la créature à laquelle la
   fenêtre est liée — `*(*(this+0x4ac)+0xc)` (`0x00596fe5-0x00596ff2`) — et le code d'état de
   l'emplacement — `*(this+0x4c0+4i)` (`0x00596ff8`) ;
5. le message est remis au routeur de session porté par `this+0x440` (`mov 0x440(%eax),%ecx`
   puis `call 0x006491c0`, `0x0059702f-0x00597041`, idem `0x005ad76f`).

Ce que cela décrit : **le joueur clique une icône d'état dans la fenêtre d'états pour annuler cet
état**. Les états concernés sont ceux que le client affiche, c'est-à-dire ceux qu'il a reçus par
`TM_SC_STATE` (505). Le porteur de la fenêtre pouvant être le personnage **ou** la cible, le handle
transmis n'est pas nécessairement celui du joueur (§7.3).

Ce qui reste non établi : le geste exact (clic simple, clic droit, bouton de la fenêtre) et la
dernière étape « message interne → trame de 15 octets » — voir §7.1 et §7.2.

## 3. Structure sur le fil

En-tête du dépôt : `Game/Network/Packets/Header.cs:9-11` (`Length` `uint32`, `ID` `uint16`,
`Checksum` `uint8`), soit 7 octets, la même constante que les paquets voisins
(`GameSkillPackets.cs:47` : `HeaderSize = 7`).

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | **15** | `Header.cs:9` ; rzu `TS_CS_REQUEST_REMOVE_STATE.h:6-7` (2 × 4 octets) |
| 4 | `uint16` LE | `ID` | **408** (`0x0198`) | `Header.cs:10` ; `op_codes.md:131` ; rzu `TS_CS_REQUEST_REMOVE_STATE.h:9-11` |
| 6 | `uint8` | `Checksum` | — | `Header.cs:11` |
| 7 | `uint32` LE | `target` | handle de la créature dont la fenêtre d'états est affichée : pour la fenêtre principale, `ConnectionInfo.CharacterHandle` | rzu `TS_CS_REQUEST_REMOVE_STATE.h:6` ; client : `*(*(window+0x4ac)+0xc)` (`0x00596fe5-0x00596ff2`) |
| 11 | `int32` LE | `state_code` | code d'état (`StateId` côté Navislamia = `StateResource.Id`) | rzu `TS_CS_REQUEST_REMOVE_STATE.h:7` ; client : `*(window+0x4c0+4i)` (`0x00596ff8`) |

**Taille totale attendue : 15 octets** (7 + 4 + 4). Aucun autre champ : pas de `state_handle`, pas
de niveau d'état, pas de compteur, pas de champ variable. La longueur est donc constante et la
validation de trame tient en une comparaison.

Précisions de lecture :

- **Seconde source indépendante** : NGemity déclare le même paquet avec la même disposition —
  `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_REQUEST_REMOVE_STATE.h:6-10` porte
  `_(simple)(uint32_t, target)`, `_(simple)(int32_t, state_code)` et
  `CREATE_PACKET(TS_CS_REQUEST_REMOVE_STATE, 408)`. NGemity écrit `uint32_t` là où rzu a `ar_handle_t`
  (même `strong_typedef<uint32_t>`, donc même 4 octets sur le fil) et ne versionne pas l'id : sur la
  taille, l'ordre des champs et l'id, les deux références **concordent**. Cette déclaration vit dans
  `reference/ngemity/shared/`, c'est-à-dire **hors** du serveur `Chihiro` (voir §5.1).
- `ar_handle_t` est un `strong_typedef<ar_handle_t, uint32_t>`
  (`librzu/src/lib/Packet/GameTypes.h:40`) : **4 octets** sur le fil, sérialisé sans padding
  (les types « simple » de rzu sont écrits dans l'ordre de déclaration). C'est le même handle que
  celui que le dépôt écrit partout ailleurs à l'offset 7 d'un paquet montant.
- `state_code` est **signé** (`int32_t` dans rzu) : une valeur négative ou nulle est représentable
  sur le fil et n'a aucune sémantique documentée (§7.4).
- Le paquet ne transmet **pas** de `state_handle` (UID d'instance). Si deux instances du même code
  d'état coexistent, la requête ne permet pas de les distinguer : c'est le serveur qui choisit
  (§5.3, §6).

## 4. Gating de version

Valeurs de référence : `EPIC_4_1 = 0x040100`, `EPIC_7_3 = 0x070300`, `EPIC_9_6_3 = 0x090603`
(`librzu/src/lib/Packet/PacketEpics.h:50,59,96`). La cible du serveur est `0x070300`.

| Champ / id | Gating montré par rzu | Décision pour Epic 7.3 | Source |
| --- | --- | --- | --- |
| id du paquet | `X(408, version < EPIC_9_6_3)` / `X(1408, version >= EPIC_9_6_3)` | **408** (`0x070300 < 0x090603`) | rzu `TS_CS_REQUEST_REMOVE_STATE.h:9-11` |
| `target` | aucun gating de champ | `uint32` (`ar_handle_t`) | rzu `TS_CS_REQUEST_REMOVE_STATE.h:6` |
| `state_code` | aucun gating de champ | `int32` | rzu `TS_CS_REQUEST_REMOVE_STATE.h:7` |

**Aucun champ de ce paquet n'est gaté par version** : la double notation d'id existe pour tout le
protocole depuis le commit rzu `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11,
« packets: use versionned ID for all packets and update their ID with epic 9.6.3 »), qui est l'unique
commit ayant touché ce fichier après son introduction. La seule décision de version est donc
**408 et non 1408**.

La structure n'a pas bougé depuis l'introduction du fichier par rzu
`d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07, « Adjust count management for packets and
add all known GS packets as of 9.4 ») : `git log -p --follow` sur le fichier ne montre qu'un
`#pragma once`, un `#undef` et le passage aux `strong_typedef` — jamais de champ déplacé, ajouté ou
retiré. **Ne pas confondre avec `TS_SC_STATE` (505), dont le `state_level` a réellement bougé à
`EPIC_9_5_2`** : le 408 n'a pas d'équivalent de ce piège.

## 5. Traitement attendu

### 5.1 Ce que NGemity en fait : rien

`reference/ngemity/Chihiro` (commit `38ceb2c6065fabf6ff4ba71d52f955f362c6c839`) **ne déclare aucun
handler** pour ce paquet : `grep -rn "TS_CS_REQUEST_REMOVE_STATE" .` **exécuté depuis `Chihiro`** ne
renvoie aucune occurrence — la seule déclaration du paquet vit hors du serveur, dans
`reference/ngemity/shared/Server/Packets/GameClient/` (§3) —, et
la liste des handlers de session (`src/Network/GameNetwork/WorldSession.cpp:118-141`, dont
`WorldSession::onCancelAction`) n'en contient pas. Le paquet tombe donc dans le journal « paquet
inconnu » de `WorldSession` et n'a **aucun effet** (ni mutation d'état, ni réponse).

En revanche, NGemity porte la **logique de retrait** et son **encodage de notification**, qui sont la
référence utilisable :

- `Unit::RemoveState(StateCode code, int32_t state_level)`
  (`src/Entities/Unit/Unit.cpp:2538-2548`) : `std::find_if` sur la **première** entrée dont
  `m_nCode == code && GetLevel() <= state_level`, puis `onUpdateState(*state, true)` →
  `m_vStateList.erase` → `CalculateStat()` → `DeleteThis()`. Le second surcharge
  `RemoveState(int32_t uid)` (`:2550-2561`) travaille par UID d'instance ;
- `Unit::onUpdateState(State *state, bool bIsExpire)` (`:1796-1799`) →
  `Messages::BroadcastStateMessage(this, state, bIsExpire)` (`src/Network/Messages.cpp:775-798`) et
  `Messages::SendStateMessage(Player*, handle, State*, bool bIsCancel)`
  (`src/Network/Messages.cpp:1105-1126`) ;
- **l'encodage de retrait** y est explicite : `TS_SC_STATE` est toujours rempli avec `handle`,
  `state_handle = GetUID()`, `state_code = GetCode()`, `state_value` et `state_string_value`, et
  **`state_level`, `end_time` et `start_time` restent à zéro** quand `bIsCancel` est vrai
  (`Messages.cpp:1105-1123` — la fonction écrit ces trois champs uniquement dans la branche
  `!bIsCancel`, sur une structure value-initialisée `TS_SC_STATE stateMsg{}`). C'est
  **exactement** l'encodage que le dépôt écrit déjà (`GameSkillPackets.BuildStateRemoval`,
  `Game/Network/Packets/Game/GameSkillPackets.cs:166-169` : niveau 0, `endTick` 0, `startTick` 0),
  et c'est aussi ce que `CLAUDE.md:714-715` a déjà consigné ;
- le drapeau d'annulabilité existe dans NGemity mais **n'est jamais consulté** :
  `AF_ERASE_ON_REQUEST` (`src/Skills/StateBase.h:27`) n'a aucune occurrence hors de sa définition
  (`grep -rn "AF_ERASE_ON_REQUEST" src/` : une seule ligne). Les seuls drapeaux lus le sont via la
  colonne `state_time_type` : `stateInfo->state_time_type & AF_NOT_ERASABLE`
  (`Unit.cpp:1681`) et `it->GetTimeType() & AF_NOT_ERASABLE` (`Unit.cpp:1701`) ;
- `SRST_RemoveState = 11` (`librzu/src/packets/GameClient/TS_SC_SKILL.h:49`) est le **type de
  résultat de compétence** qui accompagne un retrait déclenché par un sort
  (`TS_SC_SKILL__SKILL_RESULT`), pas une réponse au 408.

### 5.2 Ce que rzu en fait

Rien : rzu ne porte aucune logique serveur. Son unique occurrence est la définition du paquet
(`librzu/src/packets/GameClient/TS_CS_REQUEST_REMOVE_STATE.h`) — `grep -rn "REQUEST_REMOVE_STATE"
librzu/` ne renvoie que ce fichier. rzu tranche l'id, la taille, l'ordre des champs et le gating
(§3, §4), pas le comportement.

### 5.3 Ce que le serveur Navislamia doit faire

1. **Enum.** Ajouter `TM_CS_REQUEST_REMOVE_STATE = 408` à
   `Game/Network/Packets/Enums/GamePackets.cs`, entre `TM_SC_AURA = 407` (`:47`) et
   `TM_CS_JOB_LEVEL_UP = 410` (`:48`). Sans cette ligne, `Enum.IsDefined`
   (`GameClient.cs:497`) rejette la trame en amont.
2. **Dispatch.** Ajouter un bras `if (header.ID == (ushort)GamePackets.TM_CS_REQUEST_REMOVE_STATE)`
   avec `continue;` dans la chaîne de réception de `Game/Network/Clients/GameClient.cs` (les voisins
   sont groupés autour des l. 490-690). **Obligatoire** : une fois l'id déclaré dans l'enum, un
   paquet non traité atteint le `switch` final et lève `Unknown Packet Type`
   (`GameClient.cs:681`). Le point 1 et le point 2 vont ensemble.
3. **Lecture de la trame.** 15 octets exactement : refuser si `packet.Length != HeaderSize + 8` et
   répondre `SendResult(408, ResultCode.InvalidArgument)` — précédent identique au paquet voisin
   (`GameClient.cs:308-312`, `TryReadPutonItem` → `InvalidArgument`).
4. **Validation de `target`.** Le précédent du dépôt est `SkillCastService.TryValidateTarget`
   (`Game/Services/SkillCastService.cs:284-289`) : `request.Target != 0 && request.Target !=
   info.CharacterHandle` → `ResultCode.NotExist`. Appliquer la même règle (le dépôt ne modélise ni
   invocation ni autre joueur — commentaire l. 286 : « Summons and other players are not modelled ;
   everything else lands on the caster »). Refuser aussi `target == 0` : aucun état n'est indexé
   par un handle nul, et le code client ne construit jamais le message dans ce cas
   (`test %ecx,%ecx; je` à `0x00596ff2`).
5. **Résolution de `state_code`.** Chercher l'entrée de `ConnectionInfo.ActiveBuffs`
   (`Game/Network/Clients/ConnectionInfo.cs:20`, sous `BuffLock`, l. 18) dont **`StateId`** égale
   `state_code`. Introuvable → `ResultCode.NotExist` (précédent : `GroundItemService.cs:103-104`,
   `SendResult(TakeRequestId, ResultCode.NotExist)`).
   `ActiveBuff` (`Game/Services/Buffs/ActiveBuff.cs:3-9`) est le seul registre des états du
   personnage : il porte `StateHandle` (UID d'instance), `StateId` (le code), `SkillId`,
   `StateLevel`, `StartTick`, `EndTick`. Les états du monstre vivent à part, dans
   `MonsterWorldState` (§6, §7.5).
6. **Contrôle d'annulabilité.** Le client ne demande que les états dont le mot de drapeaux porte
   `1 << 5` (§2, point 3) ; le serveur doit pouvoir opposer le même refus. La donnée existe :
   colonne `state_time_type` (`ArcadiaSchemaPSQL.sql:1132`), lue par le mapper
   (`MigrateDatabase/Mappers/ArcadiaResourcesMappingProfile.cs:382`), portée par
   `StateResourceEntity.StateTimeType` (`Game/DataAccess/Entities/Arcadia/StateResourceEntity.cs:14`)
   et **déjà nommée** dans `StateTimeType.EraseOnRequest = 32`
   (`Game/DataAccess/Entities/Enums/StateTimeType.cs:14`). **Mais le drapeau n'est atteignable par
   aucune couche de service aujourd'hui** : `IStateCatalog` n'expose que
   `Resolve(int stateId, int stateLevel)` (`Game/Services/Stats/IStateCatalog.cs:7`) et la
   projection du dépôt de données ne sélectionne que `Id`, `EffectType`, `Values`
   (`Game/DataAccess/Repositories/Interfaces/IStateResourceRepository.cs:5,9` et
   `StateResourceRepository.cs:26`). Pour honorer le gate, il faut élargir
   `StateEffectFields` **ou** ajouter une seconde projection (« états annulables ») ; sans cela, la
   seule garde possible est « l'état est présent dans `ActiveBuffs` ». **Décision fixée** :
   élargir la donnée et refuser avec `ResultCode.NotActable` (précédent : état cible inactif,
   `SkillCastService.cs:280`) quand `EraseOnRequest` est absent — **et non** l'inverse. Réserve
   vérifiable en §7.6.
   - Attention au périmètre : `StateCatalog` ne charge que les états **à effet de stat**
     (`EffectType ∈ {1, 2}`, `StateResourceRepository.cs:21-25`), alors que `ApplyState`
     (`SkillCastService.cs:447-472`) enregistre dans `ActiveBuffs` **tous** les états castables,
     y compris ceux dont l'effet n'est pas modélisé (`CLAUDE.md:745-748`). Le contrôle de drapeau ne
     doit donc **pas** passer par `StateCatalog`, sous peine de refuser des états légitimes.
7. **Retrait et notification.** Utiliser la primitive déjà en place et déjà validée en jeu à
   l'expiration (tick de 500 ms, `SkillCastService.cs:542-567`) :
   `GameSkillPackets.BuildStateRemoval(info.CharacterHandle, buff.StateHandle, (uint)buff.StateId)`
   (`GameSkillPackets.cs:166-169`), c'est-à-dire `TM_SC_STATE` (505), **63 octets**, `state_level`
   / `end_time` / `start_time` à zéro (`CLAUDE.md:712-715`). Retirer l'entrée de `ActiveBuffs`
   (même chemin que `SkillCastService.cs:455-466` et `:542-552`).
8. **Effets de bord.** Appeler `SendStatRefresh(client, info)`
   (`SkillCastService.cs:492-502`) : `StatService.RefreshBuffs` (`StatService.cs:73-95`) recalcule
   `BuffEffects` depuis `StateCatalog.Resolve(StateId, StateLevel)`, puis les `TM_SC_STAT_INFO` /
   `TM_SC_PROPERTY` sont réémis. C'est exactement ce que fait déjà l'expiration
   (`SkillCastService.cs:567`).
9. **Cas de l'aura basculée.** Si l'état retiré provient d'une compétence d'aura, la bascule
   d'aura doit être défaite **en même temps**, sinon le client garde son aura « allumée » :
   `SkillCastService.RemoveAura` (`:375-398`) fait déjà les trois gestes dans cet ordre —
   `info.ActiveAuras.Remove(toggleGroup)`, `BuildAura(handle, skillId, false)` (**`TM_SC_AURA` 407**,
   8 octets, `GameSkillPackets.cs:124-140`) puis `BuildStateRemoval`. Le rattachement
   `ActiveBuff.SkillId` → groupe est disponible via `ActiveAuras` (`AuraToggle`, règle « une aura
   active par groupe », `Game/Services/Buffs/AuraToggle.cs:22-33`). **Sans ce point, une aura
   annulée par la fenêtre d'états laisserait le client et le serveur désaccordés.**
10. **Réponse en cas d'échec.** Aucune paire de réponse dédiée n'existe : le dépôt répond
    `SendResult(requestId, code)` (`GameClient.cs:56-60`) — membre d'énumération `TM_SC_RESULT = 0`
    (`Game/Network/Packets/Enums/GamePackets.cs:5`) portant la structure `TS_SC_RESULT`, 8 octets de
    charge : `RequestMsgID` u16, `Result` u16, `Value` i32
    (`Game/Network/Packets/Game/TS_SC_RESULT.cs:8-10`). C'est la convention des voisins
    (`GameClient.cs:310`, `:443`, `:454`). Réserve : rien n'établit la réaction du client 7.3 à un
    `TS_SC_RESULT` taggé 408 (§7.7) — le refus doit donc être **sans effet de bord**, jamais
    dépendant de l'affichage côté client.
11. **Diffusion.** Le dépôt n'envoie aujourd'hui les états qu'à la connexion du joueur concerné
    (`SkillCastService.cs:395,442,470,563,592` — toujours `client.Connection.Send`) : aucun
    `BroadcastStateMessage` régional n'existe (§6). Ne pas introduire de diffusion pour ce paquet.

## 6. Écarts assumés avec NGemity, et pourquoi

1. **NGemity n'a pas de handler : il n'y a donc pas de comportement de référence à copier.** Le
   handler est neuf. Ce qui est repris de NGemity, ce sont les deux pièces justifiées :
   l'encodage de retrait de `TS_SC_STATE` (`Messages.cpp:1105-1123`, niveau et horodatages à zéro)
   et la garde de drapeau `EraseOnRequest` (`StateBase.h:27`) — cette dernière étant **le seul
   fondement** du fait que le client n'émet que pour certains états.
2. **NGemity n'a pas de sélection « par code seul ».** Son `RemoveState(code, level)` exige un
   niveau maximum et prend la première entrée satisfaisante (`Unit.cpp:2540`), parce que tous ses
   appelants connaissent le niveau. Le 408 ne transporte pas de niveau : le serveur doit retenir la
   première entrée d'`ActiveBuffs` dont `StateId == state_code` (ordre d'insertion = ordre
   d'application), ce qui est déterministe. En pratique `ApplyState` réutilise le `StateHandle` et
   remplace l'entrée existante pour le même `StateId` (`SkillCastService.cs:455-466`), donc le cas
   « deux instances du même code » ne se produit pas dans le modèle actuel.
3. **Pas de diffusion régionale.** NGemity diffuse le retrait à la région
   (`sWorld.Broadcast(pUnit->GetRX(), ...)`, `Messages.cpp:797`). Le dépôt n'a aucune diffusion
   d'état : `TM_SC_AURA` et `TM_SC_STATE` ne partent que vers la connexion du joueur
   (`SkillCastService.cs:370,392,442,470,563,592`). Écart **assumé** : se limiter à la connexion de
   l'émetteur, comme tout le chemin d'états existant. Corollaire : les autres joueurs ne verront pas
   l'icône disparaître — c'est déjà le cas pour l'expiration, et ce n'est pas régressé ici.
4. **`AF_NOT_ERASABLE`.** NGemity lit ce drapeau dans son chemin d'application (priorité d'écrasement
   d'un état, `Unit.cpp:1681,1701`), pas dans un chemin de retrait à la demande. Le dépôt ne modélise
   pas non plus SG_NORMAL/SG_DUPLICATE/SG_DEPENDENCE (`CLAUDE.md:754-755`). La fiche ne demande donc
   **pas** de porter cette règle : seul `EraseOnRequest` est mobilisé (§5.3 point 6).
5. **Pas de statut de visibilité.** NGemity sépare état « bon » et « mauvais »
   (`Unit::RemoveGoodState`, `:2563-2569`) et filtre par `IsHarmful()`. Le dépôt ne modélise pas cette
   séparation pour `ActiveBuffs` (les debuffs de monstre sont dans `MonsterWorldState`). Aucune
   distinction n'est appliquée : un état présent dans `ActiveBuffs` et marqué `EraseOnRequest` est
   annulable.
6. **`TS_SC_RESULT` en cas d'échec.** NGemity n'aurait rien envoyé (il n'envoie rien du tout). Le
   dépôt répond toujours par un résultat aux requêtes d'action (`GameClient.cs:56-60`). Écart assumé,
   et choix le plus sûr : informer sans effet de bord.

## 7. NON ÉTABLI / A VERIFIER PAR KILLIAN

Les points 1 à 3 et 6 à 7 ci-dessous sont des réserves à ne **pas** deviner côté développement.
Chacun dit ce qui l'établirait.

1. **L'émission effective de la trame de 15 octets par le client 7.3 n'est pas prouvée par la
   seule lecture.** Ce qui est prouvé : la classe `SIMSG_REQUEST_REMOVE_STATE` existe bien dans
   `SFrame.exe` (RTTI `.?AUSIMSG_REQUEST_REMOVE_STATE@@`, dump `strings -n 4` l. 44066 ; descripteur
   de type à `0x00c17f64`, COL à `0x00bc1f90`, vtable à `0x00a300b8`, référencée depuis le code aux
   adresses `0x00597021` et `0x005ad753` — les deux constructeurs de §2), et les deux sites de
   construction lisent bien les deux valeurs de §3 dans le bon ordre. Ce qui **n'est pas** résolu :
   le dernier saut « message interne → trame sur la socket ». La recherche d'un immédiat `408` dans
   `.text` ne donne rien (`push $0x198` n'apparaît que 3 fois, aux adresses `0x00434fd4`,
   `0x00434ffb`, `0x00772c1c`, et ce sont des `operator new(408)` — l'allocation à `0x0097671b` est
   la même qu'utilisée ailleurs pour `$0x44`, `$0x48`…), et le routeur `0x006491c0` ne teste que les
   identifiants internes 1002 et 10006 avant de retomber sur sa liste de sous-interfaces. L'opcode
   est donc probablement porté par une table de données, non localisée ici. **Ce qui l'établirait** :
   une capture réseau d'une session retail Epic 7.3 pendant l'annulation d'un état (octets observés),
   ou le traçage du sérialiseur du client. Le format de fil, lui, reste tranché par rzu (§3).
2. **Le geste exact** qui déclenche la requête (clic simple, clic droit, ou bouton de la fenêtre)
   n'est pas établi. Indices non concluants : les noms d'éléments `state_all_off` (dump l. 20381),
   `one_state_off` (l. 20400), `state_all_on`, `one_state_on`, `creature_all_off_button`,
   `buff_all_off_button` (l. 20382-20383) sont utilisés par un dispatcher de noms d'éléments NUI
   (`0x004cf9a0-0x004cfb30`) qui les traite comme des **statuts** d'élément (`push $0/$1` avant un
   appel), pas comme des actions de paquet. Ce qui l'établirait : la lecture de
   `window_main_state_h_effect.nui` dans `data.000` (l'archive n'est pas sur le VPS pour ce fichier :
   `client73/` ne contient que SFrame.exe, data.000 et les `db_*.rdb`) ou une observation en jeu.
3. **`target` peut ne pas être le joueur.** Le client embarque `window_target_state_h_effect.nui`
   (dump l. 24820) à côté de `window_main_state_h_effect.nui` (l. 24844), et le handle transmis est
   celui de la créature **à laquelle la fenêtre est liée** (`*(*(window+0x4ac)+0xc)`). Rien
   n'établit qu'une session retail 7.3 acceptait une cible tierce, ni que le serveur d'origine la
   refusait. **Décision fixée** : n'accepter que `info.CharacterHandle`, refus `NotExist` (§5.3
   point 4), parité avec `TryValidateTarget`. Si Killian veut autoriser l'invocation ou l'état d'un
   monstre visible, c'est une extension à part entière (`MonsterWorldState`, §7.5).
4. **Aucune valeur sentinelle « tous les états » n'est établie.** Le client construit **un** message
   par état, à partir du code lu dans l'emplacement d'icône, et n'émet que si l'état résolu porte le
   bit `0x20`. Ni rzu ni NGemity ne documentent `state_code = 0` comme un joker. **Décision fixée** :
   traiter un code inconnu (y compris `0`) comme `NotExist`, **jamais** comme « tout retirer ».
   Ce qui l'établirait : la lecture du handler d'origine (source serveur retail) ou une capture.
5. **Les états d'un monstre ne sont pas concernés par la décision de §5.3.** Les debuffs de monstre
   vivent dans `MonsterWorldState` (`Game/Services/MonsterWorldState.cs`), pas dans `ActiveBuffs`.
   Si la cible autorisée devenait un monstre, il faudrait retirer côté monstre **et** notifier
   (`BuildStateRemoval(monsterHandle, …)`, déjà fait par `CombatService.cs:211` à la mort et
   `SkillCastService.cs:592` à l'expiration). Hors périmètre de la décision retenue en §7.3.
6. **Le contrôle d'annulabilité serveur exige une donnée non exposée aujourd'hui** (§5.3 point 6).
   La colonne existe (`ArcadiaSchemaPSQL.sql:1132`, `state_time_type`), le drapeau est nommé
   (`StateTimeType.EraseOnRequest = 32`) et NGemity lit bien ce champ comme un mot de drapeaux
   (`Unit.cpp:1681`), mais **aucune ligne du dépôt ne le lit** et la projection
   `StateEffectFields` ne le transporte pas. **Décision fixée** : élargir la projection et refuser
   l'état non marqué. Ce qui reste à vérifier : que les lignes `StateResource` réellement présentes
   dans l'Arcadia de Killian portent bien ce bit pour les états visés — sans quoi la garde
   refuserait tout, et il faudrait revenir au repli « présent dans `ActiveBuffs` ».
7. **La réaction du client à un `TS_SC_RESULT` taggé 408 n'est pas établie.** Le client connaît
   `TM_SC_RESULT` (table d'annotations `strings -n 4` l. 26330-26360) et son handler
   `SGameInterface` traite les résultats par identifiant de requête, mais rien n'établit qu'il
   affiche quoi que ce soit pour 408. Conséquence pratique : les refus doivent être silencieux et
   sans effet (§5.3 point 10), et **la fiche ne fixe aucun message d'erreur utilisateur**.
8. **Le nom du paquet est absent de la table d'annotations du client.** `TM_CS_SKILL`,
   `TM_CS_LEARN_SKILL`, `TM_SC_AURA`, `TM_SC_STATE` y figurent (dump l. 26339-26351) mais
   `TM_CS_REQUEST_REMOVE_STATE` non — et cette table n'est pas ordonnée par opcode (elle contient
   aussi `TM_STATE_DAMAGE`), donc son absence **n'est pas** une preuve d'absence du paquet : elle
   est cohérente avec le fait que ce nom n'est imprimé nulle part dans le code. À ne pas confondre
   avec le cadrage du PO (« une recherche des chaînes `RemoveState` et `REQUEST_REMOVE_STATE` dans
   `reference/client73` ne renvoie rien ») : la sous-chaîne `REQUEST_REMOVE_STATE` **est** présente
   dans le binaire, sous la forme manglée `.?AUSIMSG_REQUEST_REMOVE_STATE@@` (l. 44066 du dump
   `strings -n 4`).
9. **Le client attend-il une notification pour lui-même seulement ou pour les observateurs ?**
   Rien ne l'établit, et le dépôt n'a aucune diffusion d'état (§6 point 3). Décision retenue :
   pas de diffusion. À réévaluer le jour où les autres joueurs verront les états.

## 8. Commits et binaires épinglés

| Référence | Commit / empreinte | Usage |
| --- | --- | --- |
| Navislamia `master` (base de cette fiche) | `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` (2026-07-18, « Fix drop and Monsters attack character back ») | état du dépôt au moment de la rédaction |
| rzu (HEAD du clone) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02) | état de `reference/rzu` |
| rzu — introduction de `TS_CS_REQUEST_REMOVE_STATE` | `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07) | champs et id 408 |
| rzu — `ar_handle_t` = `strong_typedef<uint32_t>` | `05bc2d82dac16003bc06162584e8559826310bff` (2020-03-29) | 4 octets sur le fil (§3) |
| rzu — ids versionnés (`EPIC_9_6_3`) | `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11) | §4 : 408 (< 9.6.3) vs 1408 |
| NGemity / Chihiro (HEAD) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03) | logique de retrait et encodage de notification |
| Client 7.3 — `SFrame.exe` | `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | RTTI de la classe, sites de construction, fenêtres d'états |
| `reference/commits.json` | — | provenance des deux clones de référence |

Deux conventions de citation, pour que les chiffres soient vérifiables :

- les **numéros de ligne `l. N`** du client proviennent du dump reproductible
  `strings -n 4 /srv/navislamia/reference/client73/SFrame.exe` (**63 099 lignes**), pris le
  2026-09-18 sur l'empreinte `41e0af2e…` ci-dessus — ils ne sont pas stables si le binaire change ;
- les **adresses `0x…`** du client sont des adresses virtuelles du `.text`/`.rdata` du même binaire
  (`ImageBase 0x00400000`), obtenues par `objdump -d --start-address=… --stop-address=…` et par
  lecture directe des octets. Les correspondances fichier ↔ VMA utilisées :
  `.text` `0x00000400 → 0x00401000`, `.rdata` `0x0060da00 → 0x00a0f000`, `.data` `0x0080e200 →
  0x00c10000`.

## 9. Note de livraison

- Branche : `hermes/packet-408-request-remove-state`, créée depuis `master`
  `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7`. Les commits de cette fiche ne touchent que
  `docs/packet-specs/408-request-remove-state.md` et `.gitignore`.
- `.gitignore` : `/docs/*` ignore le répertoire ; l'exception `!/docs/packet-specs/` est ajoutée
  après `!/docs/npc-dialogs.md`, comme sur les branches sœurs `hermes/packet-221-hide-equip-info`,
  `hermes/packet-253-use-item`, `hermes/packet-1202-emotion` et `hermes/packet-203-drop-item`.
- Cette fiche **ne modifie aucun fichier de code** : ni `GamePackets`, ni `GameClient`, ni les
  services. Aucun test n'a été ajouté ni modifié.
- Le dev qui reprend la branche implémente, écrit le **test d'offsets** (taille totale 15 octets et
  position de chaque champ, discipline du dépôt), et recopie le bloc de §10 dans la description de
  la MR — le dev **n'écrit pas** `CLAUDE.md`.
- **Deux pièges à ne pas manquer** : (a) `EraseOnRequest` est présent dans l'enum d'entité mais
  absent de toute projection de données, donc la garde de §5.3 point 6 demande un élargissement
  délibéré ; (b) une aura annulée par la fenêtre d'états doit aussi couper `ActiveAuras` et envoyer
  `TM_SC_AURA` 407 à `false` (§5.3 point 9), sinon le client garde son aura allumée.
- Contrôle de cohérence à faire par le dev : `docs/packet-specs/` n'existe pas sur `master` ; la
  présente fiche est le cinquième fichier du répertoire, après les branches 221, 253, 1202 et 203.

## 10. Bloc pour CLAUDE.md (à recopier dans la description de la MR)

```markdown
- **`TM_CS_REQUEST_REMOVE_STATE` (408) est implémenté** : trame fixe de **15 octets** — en-tête 7,
  `target` `uint32` à l'offset 7 (handle de la créature dont la fenêtre d'états est affichée),
  `state_code` `int32` à l'offset 11 (le `StateId`). Aucun gating de champ : rzu ne versionne que
  l'id (408 pour `< EPIC_9_6_3`, 1408 au-delà), donc **408 en 7.3**. C'est le clic d'une icône d'état
  dans `window_main_state_h_effect.nui` qui l'émet, et le client ne le construit que pour un état
  dont le mot de drapeaux porte `1 << 5` — `StateTimeType.EraseOnRequest = 32`, le même bit que
  `AF_ERASE_ON_REQUEST` de NGemity. **Ce drapeau n'était lu par aucune projection du dépôt** :
  `StateEffectFields` ne transporte que `Id`/`EffectType`/`Values`, et `StateCatalog` ne charge même
  que les états à effet de stat (`EffectType` 1 ou 2) alors qu'`ActiveBuffs` en contient d'autres.
- **Réponse** : `TM_SC_STATE` (505), **63 octets**, `state_level`/`end_time`/`start_time` à zéro —
  c'est exactement `BuildStateRemoval`, déjà validé en jeu à l'expiration ; NGemity encode le retrait
  de la même façon (`Messages.cpp:1105-1123`). Suivi de `SendStatRefresh` (`RefreshBuffs` +
  `TM_SC_STAT_INFO`/`TM_SC_PROPERTY`). En cas d'échec, `TS_SC_RESULT` taggé 408
  (`NotExist`/`NotActable`) : le paquet n'a aucune réponse dédiée dans tout le protocole.
- **Une aura annulée par cette voie doit être défaite comme une aura** : couper `ActiveAuras` et
  envoyer `TM_SC_AURA` (407) à `false`, comme `RemoveAura` le fait à la bascule. Sans cela, le client
  garde l'icône d'aura allumée alors que le serveur l'a retirée.
- Aucune diffusion : les états ne partent que vers la connexion du joueur concerné, ici comme pour
  l'expiration et la bascule (NGemity, lui, diffuse à la région — écart assumé).
- **Réserves vérifiables** (fiche §7) : l'émission effective de la trame par le client n'est pas
  prouvée par la seule lecture (le dernier saut message interne → socket n'est pas résolu, l'opcode
  n'apparaît dans aucun immédiat du `.text`) ; le geste exact de déclenchement ; le fait qu'une cible
  tierce soit légitime (`window_target_state_h_effect.nui` existe) — décision : n'accepter que son
  propre handle ; aucune valeur sentinelle « tous les états » — décision : code inconnu =
  `NotExist` ; le contenu réel de `state_time_type` dans l'Arcadia de Killian doit porter le bit 32
  pour les états visés, sinon la garde refuserait tout.
```
