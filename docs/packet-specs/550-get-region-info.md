# 550 — `TM_CS_GET_REGION_INFO` / réponse `TM_SC_REGION_ACK` (11)

Fiche d'archéologie de protocole, Epic 7.3. Écrite en lecture seule sur les références
(`reference/rzu`, `reference/ngemity/Chihiro`, `reference/client73`) — aucun Lua, aucun script du
client, aucun exécutable client n'a été lancé. Le client 7.3 tranche (il construit lui-même le
paquet 550 et le dimensionne à 15 octets, §2) ; rzu tranche la forme et le gating ; NGemity tranche
la logique serveur sous réserve de sa version compilée (`EPIC_4_1_1`, §4).

Résumé des arbitrages demandés :

| Question | Verdict de cette fiche |
|---|---|
| 7.3 possède-t-il 550 / 11 ? | **Oui** : le client construit un paquet de 15 octets dont l'id vaut `0x226` (550) et y écrit deux `float` (§2, §3.1) ; il possède une branche de dispatch étiquetée `MSG_REGION_ACK` (§3.2, §7a). |
| Quel diviseur pour `rx`/`ry` ? | Celui **annoncé au login**, `WorldVisibility.RegionSize = 180` — pas `WorldOption.RegionSize = 150` (§4, §5.3). C'est le piège de cette fiche. |
| Qui déclenche la réponse ? | Ici : le serveur **répond à la demande 550**. NGemity, lui, ne lit jamais 550 et pousse la 11 de sa propre initiative au changement de région (§5.1, §6). |

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id client → serveur | **550** | `op_codes.md:153` ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_GET_REGION_INFO.h:10` |
| Nom | `TM_CS_GET_REGION_INFO` | `op_codes.md:153` |
| Réponse serveur → client | **11**, `TM_SC_REGION_ACK` | `op_codes.md:14` ; `reference/rzu/librzu/src/packets/GameClient/TS_SC_REGION_ACK.h:12` |
| Ids alternatifs | `1550` (demande) et `1011` (réponse) à partir d'`EPIC_9_6_3` | `TS_CS_GET_REGION_INFO.h:11` ; `TS_SC_REGION_ACK.h:13` |
| Référence NGemity | `TS_CS_GET_REGION_INFO = 550`, `TS_SC_REGION_ACK = 11` | `reference/ngemity/shared/Server/ClientPackets.h:159` ; `reference/ngemity/shared/Server/Packets/GameClient/TS_SC_REGION_ACK.h:10` |
| État dans Navislamia | **absent** : ni 550 ni 11 dans `GamePackets`, aucun handler ; `grep -rn 'GetRegionInfo\|REGION_ACK' --include=*.cs .` → 0 résultat | `Game/Network/Packets/Enums/GamePackets.cs` |
| Taille demande | **15 octets** (7 + 2 × `float`) | §3.1 |
| Taille réponse | **15 octets** (7 + 2 × `int32`) | §3.2 |

Le 7.3 du dépôt est antérieur à 9.6.3 : les ids sont **550 (CS) et 11 (SC)**, voir §4.

## 2. Ce que le joueur fait pour que le client l'envoie

La 550 n'est déclenchée par **aucune action d'interface** : elle part automatiquement de
`SGameWorld::Process` (chaîne `SGameWorld::Process` à la VA `0xa54790`) à chaque **franchissement
d'une frontière de région** :

1. le client lit deux `float` à `+0x20` et `+0x24` de l'objet de scène
   (`mov 0x1c0(%esi),%eax`, puis `flds 0x20(%eax)` / `flds 0x24(%eax)`, VA `0x68bf69`-`0x68bf7d`) ;
2. il les convertit en indices de région par `(int)(v / region_size)` : routine VA `0x67f730`
   (`flds 0x8(%ebp)` puis `fidivl 0xc20508` à la VA `0x67f739`, puis `FISTP` avec le mot de contrôle
   forcé en troncature, `0x67f73f`-`0x67f754`) ;
3. il compare la paire obtenue aux deux globaux de cache (`cmp 0xc4f6e0,%edi` VA `0x68bf99` et
   `cmp 0xc4f6dc,%ebx` VA `0x68bfa1`) ; **si les deux sont inchangés, aucun paquet n'est émis**
   (`je 0x68c0b1`) ;
4. sinon il construit la 550 (constructeur VA `0x684b60`, §3.1), y écrit les deux `float` d'origine
   (VA `0x68bfb5`-`0x68bfba` pour `x` à l'offset 7, `0x68bfbd`-`0x68bfc9` pour `y` à l'offset 11),
   l'envoie par un appel virtuel (`call *0xc4(%edx)`, VA `0x68bfcf`) puis met à jour les caches
   (VA `0x68bfdd` et `0x68bfe4`) ;
5. il trace enfin `region x: %d y: %d %f %f\n` (format à la VA `0xa54774`), les deux `%d` étant les
   indices de région et les deux `%f` les positions envoyées — c'est la preuve que les `float` de la
   550 sont bien la position courante de l'objet, et que la paire d'entiers est l'indice de région.

Conséquence pratique pour le serveur : la 550 arrive **une fois par franchissement de frontière**,
pas à chaque pas. Ce n'est ni un flux continu ni un keep-alive ; il ne faut pas en déduire un
rythme, et il ne faut rien répondre si la demande est mal formée (§5.3).

Réserve de méthode : la table id→nom du client 7.3 (`strings -n 6 SFrame.exe | grep -c '^TM_'` →
173 chaînes, dont 73 `^TM_CS_*` et 94 `^TM_SC_*`) **ne contient pas** `TM_CS_GET_REGION_INFO`
(`strings … | grep -ci region_info` → 0) alors que le constructeur du paquet existe. Cette table est
une aide au débogage **incomplète** (elle ne contient pas non plus 25…29, que rzu atteste) : son trou
ne prouve rien, et la preuve d'existence retenue ici est le constructeur et son appelant.

## 3. Structure sur le fil

Convention de citation client : toutes les adresses sont des **VA** lues par
`objdump -d` sur `reference/client73/SFrame.exe` (sha256
`41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 o., `pei-i386`,
`.text` VA `0x00401000` taille `0x0060a8cf`, `.rdata` VA `0x00a0f000`, `.data` VA `0x00c10000`
taille virtuelle `0xbd260`). Le client et NavisLamia partagent le même en-tête de 7 octets
(`Game/Network/Packets/Header.cs:7-11`) : `Length` (`uint32`), `ID` (`uint16`), `Checksum`
(`uint8`).

### 3.1 `TM_CS_GET_REGION_INFO` — client → serveur — **15 octets**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0 | `uint32` LE | `Length` | `15` (`0xf`) | client VA `0x684b97` (`movl $0xf,(%eax)`) ; `Header.cs:9` |
| 4 | `uint16` LE | `ID` | `550` (`0x226`) | client VA `0x684b8c`-`0x684b91` (`mov $0x226,%edx` puis `mov %dx,0x4(%eax)`) ; `op_codes.md:153` ; rzu `TS_CS_GET_REGION_INFO.h:10` |
| 6 | `uint8` | `Checksum` | somme des octets 0…5 | client VA `0x684b62` (`lea 0x6(%eax),%ecx`), boucle `0x684ba3`-`0x684ba8`, écriture `0x684baa` ; `Header.cs:11` ; règle serveur : `Game/Network/Packets/PacketExtensions.cs:13` |
| 7 | `float` | `x` | position courante de l'objet (tampon+7) | client VA `0x68bfb5`-`0x68bfba` (`flds -0x18(%ebp)` / `fstps -0x5d(%ebp)`, tampon en `-0x64(%ebp)`) ; rzu `TS_CS_GET_REGION_INFO.h:6` |
| 11 | `float` | `y` | position courante de l'objet (tampon+11) | client VA `0x68bfbd`-`0x68bfc9` (`fstps -0x59(%ebp)`) ; rzu `TS_CS_GET_REGION_INFO.h:7` |

Taille totale : `7 + 2 × 4 = 15` octets. Elle est **confirmée par le producteur** : le constructeur
VA `0x684b60` écrit `Length = 0xf` puis zérote les offsets 4 à 14 (`0x684b7f`-`0x684b89`) — il n'y
a aucun octet après `y`, et le paquet est entièrement rempli par les offsets 0…14. rzu donne le même
compte (deux `float`, 8 octets de charge utile, `TS_CS_GET_REGION_INFO.h:5-7`).

### 3.2 `TM_SC_REGION_ACK` — serveur → client — **15 octets**

Aucun producteur client (c'est un paquet serveur) : la forme vient de rzu et de NGemity, et la
consommation côté client est décrite au §7a.

| Offset | Type | Nom | Valeur attendue | Source |
|---|---|---|---|---|
| 0 | `uint32` LE | `Length` | `15` | en-tête commun (`Header.cs:9`) ; rzu ne contraint que la charge utile |
| 4 | `uint16` LE | `ID` | `11` | `op_codes.md:14` ; rzu `TS_SC_REGION_ACK.h:12` |
| 6 | `uint8` | `Checksum` | somme des octets 0…5 | `Header.cs:11` |
| 7 | `int32` LE | `rx` | `(int)(x / region_size)` | rzu `TS_SC_REGION_ACK.h:8` ; NGemity `TS_SC_REGION_ACK.h:7` ; NGemity `World.cpp:289` |
| 11 | `int32` LE | `ry` | `(int)(y / region_size)` | rzu `TS_SC_REGION_ACK.h:9` ; NGemity `TS_SC_REGION_ACK.h:8` ; NGemity `World.cpp:290` |

Taille totale : `7 + 2 × 4 = 15` octets. Cohérent avec la lecture que le client fait de la réponse :
son gestionnaire étiqueté `MSG_REGION_ACK` lit **deux entiers consécutifs de 4 octets** du message
(`mov 0x17(%eax),%edx` VA `0x472ed6` et `mov 0x13(%eax),%eax` VA `0x472ed9`, tous deux empilés vers
l'appel `0x6f0d10`), soit la seule charge utile de 8 octets qui corresponde à un paquet d'id 11
(§7a). La taille exacte n'est en revanche pas vérifiée par le client : voir §7d.

Note d'implémentation : la structure C# `TS_SC_REGION_ACK` doit rester plaquée
(`[StructLayout(LayoutKind.Sequential, Pack = 1)]`, deux `int`) pour que `Marshal.SizeOf` vaille 8
et que `Packet<T>` produise 15 octets — même convention que les autres paquets du dépôt.

## 4. Gating de version

| Champ / id | Gating rzu | Décision 7.3 |
|---|---|---|
| Id de `TS_CS_GET_REGION_INFO` | `X(550, version < EPIC_9_6_3)` / `X(1550, version >= EPIC_9_6_3)` (`TS_CS_GET_REGION_INFO.h:9-11`) | **550** |
| Id de `TS_SC_REGION_ACK` | `X(11, version < EPIC_9_6_3)` / `X(1011, version >= EPIC_9_6_3)` (`TS_SC_REGION_ACK.h:11-13`) | **11** |
| `x`, `y` (`float`) | aucun gating (`TS_CS_GET_REGION_INFO.h:6-7`, aucune directive `version`) | 2 × 4 octets, offsets 7 et 11 |
| `rx`, `ry` (`int32_t`) | aucun gating (`TS_SC_REGION_ACK.h:8-9`) | 2 × 4 octets, offsets 7 et 11 |
| (connexe) id de `TS_CS_REGION_UPDATE`, qui alimente la position du client | `X(7, version < EPIC_9_2)` / `X(67, EPIC_9_2 ≤ v < EPIC_9_6_3)` / `X(1067, v >= EPIC_9_6_3)` (`TS_CS_REGION_UPDATE.h:14-17`) | **7** — déjà correct dans Navislamia (`GamePackets.cs:12`) |

Justification : `EPIC_7_3 = 0x070300` (`reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59`) est
**inférieur** à `EPIC_9_6_3 = 0x090603` (`PacketEpics.h:96`) : la branche basse s'applique pour les
deux paquets. Le gating a été introduit par le même commit rzu que pour les autres paquets remappés
par 9.6.3 (`11f2b6f`, §8), motif identique à celui de `TM_CS_EMOTION` (1202 → 2202).

NGemity ne peut pas contredire ce gating : il compile un seul Epic, `EPIC_4_1_1`
(`reference/ngemity/shared/Common/Define.h:25`), donc également sous 9.6.3, et ses ids fixes
550 / 11 (`shared/Server/ClientPackets.h:159`,
`shared/Server/Packets/GameClient/TS_SC_REGION_ACK.h:10`) confirment la branche basse. Sa seconde
branche d'id pour `TS_CS_REGION_UPDATE` s'arrête elle aussi à `EPIC_9_2` sans connaître 9.6.3
(`shared/Server/Packets/GameClient/TS_CS_REGION_UPDATE.h:13-15`) : NGemity n'est ici qu'un
témoin de la branche basse, pas une autorité sur 7.3.

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` fait du paquet

**Rien.** La 550 n'est jamais lue par NGemity :

- `reference/ngemity/Chihiro/src/Network/GameNetwork/WorldSession.h:59-122` énumère tous les
  gestionnaires (`onAuthResult`, `onMoveRequest`, `onRegionUpdate`, `onChangeLocation`, …) : il
  n'existe **aucun** `onGetRegionInfo` ;
- `grep -rn 'GET_REGION_INFO' reference/ngemity/` ne remonte que la déclaration d'énumération
  (`shared/Server/ClientPackets.h:159`) et l'`#include` de la structure
  (`shared/Server/XPacket.h:101`) — aucune occurrence dans `Chihiro/src/`.

NGemity répond malgré tout, de sa propre initiative, **au changement de région** et non à la
demande :

- `Chihiro/src/World/World.cpp:287-302` (`World::enterProc`) recalcule
  `rx = (uint32_t)(pUnit->GetPositionX() / sWorld.getIntConfig(CONFIG_MAP_REGION_SIZE))` et `ry`
  de la même façon (`:289-290`) puis, si la paire a changé et que l'unité est un joueur, appelle
  `Messages::SendRegionAckMessage(player, rx, ry)` (`:298-300`) ;
- l'appel vient du chemin de déplacement : `World.cpp:249-255` compare la région de l'ancienne et de
  la nouvelle position avant `SetMove`, et `World::onMoveObject` (`:275-285`) déplace l'objet entre
  régions selon le même calcul ;
- `Chihiro/src/Network/Messages.cpp:990-999` remplit `ackPct.rx` / `ackPct.ry` et envoie ;
- le diviseur est `CONFIG_MAP_REGION_SIZE`, alimenté par `Game.RegionSize` avec le défaut **180**
  (`Chihiro/src/World/World.cpp:123`, `Chihiro/chihiro.conf.dist:55`) — le **même** couple de
  constantes (`CONFIG_MAP_REGION_SIZE`, `CONFIG_REGION_SIZE`) est initialisé deux fois à 180
  (`World.cpp:123` et `:126`) ;
- NGemity annonce exactement cette valeur au client dans le résultat de login :
  `resultPct.region_size = sWorld.getIntConfig(CONFIG_MAP_REGION_SIZE)`
  (`Chihiro/src/Network/GameNetwork/WorldSession.cpp:264`), champ `region_size` de
  `TS_SC_LOGIN_RESULT` (`shared/Server/Packets/GameClient/TS_SC_LOGIN_RESULT.h:17`).

### 5.2 Ce que le serveur doit répondre

Sur réception d'une 550 valide, le serveur répond à **ce seul client** une 11 de 15 octets :

- `ID = 11` (`TM_SC_REGION_ACK`) ;
- `rx = (int)(x / WorldVisibility.RegionSize)` ;
- `ry = (int)(y / WorldVisibility.RegionSize)` ;
- `x`, `y` = les deux `float` **lus dans la demande** (offsets 7 et 11), pas la dernière position
  connue du serveur : c'est la paire que le client vient de lui-même convertir en indices de région
  (§2, étapes 1-2), donc celle sur laquelle porte la question. Le client met à jour
  `ConnectionInfo.X/Y` depuis la 5 (`GameClient.cs:146-147`), la 7 (`:154-156`) et la 900
  (`:163-164`) : ces valeurs sont un repli acceptable mais peuvent retarder d'un déplacement, donc
  la lecture du paquet est la référence.

Le calcul doit être une **troncature** (`(int)(x / 180f)` en C#, ou `(int)MathF.Truncate`), jamais un
arrondi : le client fait `fidivl` suivi d'un `FISTP` en mode troncature (VA `0x67f739`-`0x67f754`) et
NGemity un cast `uint32_t` d'un `float` (`World.cpp:289-290`). Pour des coordonnées positives les
deux écritures coïncident.

### 5.3 Points d'implémentation à respecter

1. **Enum et dispatch ensemble** : ajouter `TM_SC_REGION_ACK = 11` et
   `TM_CS_GET_REGION_INFO = 550` à `GamePackets` (`Game/Network/Packets/Enums/GamePackets.cs`) et,
   dans la boucle de réception de `GameClient`, la branche correspondante — aucun membre de
   `GamePackets` ne doit atteindre le `switch` final par défaut
   `GameClient.cs:539-543` donne le motif d'une branche existante).
2. **Le diviseur est celui annoncé au login** : `WorldVisibility.RegionSize = 180`
   (`Game/Services/WorldVisibility.cs:5`) est la valeur écrite dans `TS_SC_LOGIN_RESULT.RegionSize`
   (`GameActions.cs:128`, structure `Game/Network/Packets/Game/TS_SC_LOGIN_RESULT.cs:15`). La
   réponse doit utiliser **ce** diviseur. `WorldOption.RegionSize = 150`
   (`Game/Entities/World/WorldOption.cs:13`, avec `GetRegionX`/`GetRegionY` aux lignes `:36` et
   `:38`, utilisé par `WorldRegionContainer.cs:33-34` et `:91`) n'est **pas** la valeur annoncée :
   c'est le défaut de pré-login du client 150 (`0xc20508`, §7e). Mélanger les deux fait dériver la
   fenêtre de visibilité du client d'un facteur 6/5.
3. **Taille** : `Packet<TS_SC_REGION_ACK>` doit produire 15 octets (`Header.Length` est calculé par
   `Packet<T>`, `Game/Network/Packets/Packet.cs:56`, checksum ligne `:57`). Un test d'offsets est
   obligatoire (critère 3) : 15 octets, `x` à 7, `y` à 11 pour la demande, `rx` à 7, `ry` à 11 pour
   la réponse.
4. **Pas de diffusion** : la réponse est un état par client (NGemity envoie de même à un seul
   joueur, `World.cpp:298-300`).
5. **Demande mal formée** : `Length != 15` → journaliser et ignorer (pas de réponse, pas de
   `TM_SC_RESULT`). Rien dans les références ne définit d'erreur pour ce paquet.

### 5.4 Ce que représentent `rx` et `ry`

Ce ne sont **pas** des identifiants de bloc terrain ni une référence d'asset : c'est l'indice de la
grille de régions **du client lui-même**, tel qu'il le calcule pour sa propre position (§2, étape 2),
avec le diviseur qu'il a reçu au login. Le client s'en sert pour sa fenêtre de visibilité 7 × 7
(rayon 3) : la routine VA `0x6f0d10` compare l'indice reçu à ceux des objets et retient ceux à ±3
(`add $0x3` puis `cmp $0x6`, VA `0x6f0dad`-`0x6f0dc6`). La topologie est donc celle de Navislamia
(`WorldVisibility.VisibleRegionRadius = 3`, `Game/Services/WorldVisibility.cs:6`, matrice
`S_Matrix` 7 × 7 dans `Game/Entities/World/WorldOption.cs:19-34`) : un indice compatible avec
`WorldOption.GetRegionX/GetRegionY` (`WorldOption.cs:36`, `:38`) **mais calculé avec le diviseur
annoncé (180)**, pas avec le 150 de `WorldOption`. C'est cette compatibilité de grille, et non un
identifiant de map, que la réponse doit garantir.

### 5.5 Cas limites

| Cas | Ce que fait le client | Comportement serveur recommandé |
|---|---|---|
| `Length` ≠ 15 | rien n'est spécifié | journaliser et ignorer, sans réponse ; l'en-tête et le checksum sont déjà validés par `OnDataReceived` (`GameClient.cs:493`, longueur `:495`) |
| position négative | `truncate` vers zéro (`FISTP` avec mot de contrôle forcé, VA `0x67f73f`-`0x67f754`) | même troncature : `(int)(x / 180f)` tronque vers zéro en C# ; ne pas passer par un cast `(uint)` d'un `float` |
| position nulle | indice `0` | répondre `0`, ce n'est pas une erreur |
| position hors carte | le client ne borne pas | répondre la troncature telle quelle : clamper placerait le client dans une région différente de celle qu'il vient de calculer |
| 550 avant l'entrée en jeu | le paquet part de `SGameWorld::Process`, donc après le login | ajouter une garde explicite — il n'en existe aucune dans la chaîne de dispatch (`GameClient.cs:486-551`) : ignorer si `ConnectionInfo.CharacterHandle == 0` (`Game/Network/Clients/ConnectionInfo.cs:29`, remis à zéro `:155`) |

### 5.6 Recouvrement avec `TM_CS_REGION_UPDATE` (7) et `SyncVisibleObjects`

- La 7 (`TM_CS_REGION_UPDATE`, déjà dans `GamePackets.cs:12` et traitée par `HandleRegionUpdate`,
  `GameClient.cs:151-158`) est un **flux de position** émis par le client : elle n'attend aucune
  réponse, et le serveur n'en tire aujourd'hui aucun indice de région. La 550 est une **question**
  ponctuelle sur cette position. Il n'y a donc pas de recouvrement de responsabilité, mais les deux
  alimentent `ConnectionInfo.X/Y` (5 : `:146-147`, 7 : `:154-156`, 900 : `:163-164`) : la réponse
  doit partir de la paire reçue dans la 550 (§5.2), pas de la dernière position connue.
- `SyncVisibleObjects` (`GameClient.cs:168-173`, appelé par `HandleChangeLocation` `:165` et par la 7)
  synchronise les objets visibles autour du joueur via l'index spatial (cellules
  `WorldVisibility.ViewRange = 540`). Les deux mécanismes sont indépendants : ni la réponse à la 550
  ni l'index spatial n'ont à être modifiés par cette fiche.

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | NGemity | Navislamia (cette fiche) | Raison |
|---|---|---|---|
| Déclencheur de la réponse | Pousse la 11 au changement de région (`World.cpp:298-300`), sans jamais lire la 550 | Répond à la 550 reçue | Le client 7.3 **demande** (§2) : répondre à la demande est le comportement que le client attend, et cela ne demande au serveur aucune tenue de région, qu'il n'a pas. Le push de NGemity reste hors périmètre ici. |
| Gestionnaire du paquet | Aucun (`WorldSession.h:59-122`) : la 550 est ignorée | Gestionnaire explicite | Conséquence du point précédent. |
| Diviseur | `CONFIG_MAP_REGION_SIZE` (180 par défaut) | `WorldVisibility.RegionSize` (180), valeur annoncée au login | Identique en valeur ; l'écart est de **nommer la constante qui est réellement annoncée** (§5.3.2). |
| Type du calcul | `(uint32_t)(float / int)` — cast implicite, troncature | `(int)(x / 180f)` — troncature vers zéro | Même résultat pour des coordonnées positives ; on ne porte pas le `uint32_t` pour ne pas transformer un `x` négatif en valeur énorme. |
| Destination | Un joueur uniquement (`pUnit->IsPlayer()`) | Le client demandeur uniquement | Identique. |
| Doublons | NGemity n'a pas de garde : il compare à la paire précédente (`prx`, `pry`) passée en argument | Aucune garde nécessaire : le client ne demande qu'au franchissement (§2) | Le client fournit la déduplication ; ajouter un cache serveur serait un état inutile. |

## 7. `NON ÉTABLI`

a. **La 11 est-elle indispensable au client ?** Le client possède une branche de dispatch étiquetée
   `case MSG_REGION_ACK :` (chaîne à la VA `0xa1d610`, bloc VA `0x47fb76` appelant `0x472ed0`) et
   son corps appelle la routine de visibilité VA `0x6f0d10`, qui divise des positions par le
   diviseur de région (`fildl 0xc20508` VA `0x6f0d55`-`0x6f0d6a`) et reconstruit une fenêtre 7 × 7
   (`add $0x3` puis `cmp $0x6`, VA `0x6f0dad`-`0x6f0dc6`). Mais **rien dans les relevés ne prouve
   qu'un client privé de cette réponse se comporte mal** (position, visibilité, ou simple trace).
   Question précise à trancher : *la 11 est-elle un accusé obligatoire ou un simple confort ?* Cette
   fiche l'implémente comme une réponse ; aucune relance ni push n'est spécifié.

b. **Le client redemande-t-il après un warp ou un retour en jeu ?** Le seul déclencheur identifié
   est la paire de caches globaux `0xc4f6e0` / `0xc4f6dc` (VA `0x68bf99`-`0x68bfa7`), qui sont dans
   la queue BSS de `.data` (au-delà de `rawsize = 0x38600`, dans `vsize = 0xbd260`) donc **nulles au
   chargement** ; aucun code identifié ne les réinitialise. Conséquence serveur : nulle (le serveur
   répond s'il est interrogé), mais l'affirmation « le client demande exactement une fois par
   franchissement » ne vaut que pour la session courante.

c. **Table de dispatch du client : numéros internes ≠ ids du fil.** La table de saut du client
   (table d'octets VA `0x4801dc`, table de pointeurs VA `0x480120`, index `id - 4`, VA
   `0x47fb24`-`0x47fb39`) associe les numéros internes 4, 6, 8, 9, 10, 11, 48 aux blocs qui poussent
   les libellés `MSG_RESULT`, `MSG_MOVE`, `MSG_REGION_ACK`, `MSG_LOGIN`, `MSG_LEAVE`,
   `MSG_CHATTING` — or les ids du fil sont 4 = `LOGIN_RESULT`, 8 = `MOVE`, 11 = `REGION_ACK`. Les
   deux numérotations **ne coïncident pas** : le client convertit donc les ids avant ce dispatch, ou
   ces libellés ne nomment pas ce qu'ils semblent nommer. Les gestionnaires cités dans cette fiche
   le sont **par leur chaîne de libellé uniquement** ; aucun id client n'est affirmé à partir de
   cette table.

d. **Aucun contrôle de taille côté client pour la 11.** On n'a pas trouvé de test de longueur dans
   le gestionnaire ; les 15 octets reposent sur rzu (`TS_SC_REGION_ACK.h:7-9`) et sur la lecture de
   deux `int32` consécutifs (§3.2), pas sur un rejet observable. Un paquet plus long ne serait donc
   peut-être pas refusé — mais 15 est la taille à produire.

e. **Identité du diviseur côté client.** Le global `0xc20508` est initialisé à **150** dans `.data`
   (octets `96 00 00 00`) et n'a **qu'un seul écrivain** : VA `0x47c7c0` (`mov %ecx,0xc20508`), dans
   le gestionnaire étiqueté `case MSG_LOGIN :` (chaîne VA `0xa1d5f0`), qui copie les 4 octets à
   `+0x2a` de la structure reçue. Cet offset correspond à `region_size` dans la disposition en
   mémoire du client (octet `layer` à `+0x24`, un octet de remplissage, `face_direction` `float` à
   `+0x26`, `region_size` `int32` à `+0x2a`), ce qui est cohérent avec l'ordre des champs de
   `TS_SC_LOGIN_RESULT` (`shared/Server/Packets/GameClient/TS_SC_LOGIN_RESULT.h:14-17`) — mais la
   source du désérialiseur client n'est pas disponible : l'identification est **forte, pas
   certaine**. Question à trancher : *le client utilise-t-il bien la valeur de `region_size` reçue
   au login, et non son défaut 150 ?* Si la réponse était « non », l'annonce `RegionSize = 180`
   serait sans effet et les indices `rx`/`ry` devraient être produits avec 150.

f. **Le `150` de Navislamia.** `WorldOption.RegionSize = 150` (`WorldOption.cs:13`) coïncide
   exactement avec le défaut du client (`0xc20508`), alors que `WorldVisibility.RegionSize = 180`
   coïncide avec la valeur annoncée. On n'a pas établi si cette ressemblance est volontaire
   (constante héritée du client) ou fortuite ; la fiche tranche en faveur de 180 pour la réponse
   (§5.3.2) parce que c'est la seule valeur que le client connaît au moment où il calcule ses
   propres indices.

## 8. Commits et binaires épinglés

| Référence | Commit / empreinte |
|---|---|
| rzu (`reference/rzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (HEAD, 2023-10-02) |
| rzu — création de `TS_CS_GET_REGION_INFO.h` | `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07) |
| rzu — création de `TS_SC_REGION_ACK.h` | `18e313f2167ca4a3aee90e99145bdd0f6f15b315` (2015-10-27) |
| rzu — gating 9.6.3 (550 → 1550, 11 → 1011) | `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11) |
| rzu — commentaire « Last tested: EPIC_9_8_1 » de la réponse | `3b31db1e5aea84565926f6910e2437646c8a3c51` (2023-09-30) |
| rzu — `PacketEpics.h` (`EPIC_7_3`, `EPIC_9_2`, `EPIC_9_6_3`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (HEAD) |
| NGemity (`reference/ngemity`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (HEAD, 2025-12-03) |
| NGemity — import des deux structures (`Network: Rewriting network code`) | `44b7d25dac7a1f869490dc8b6ea3255ea68b8eb4` (2018-08-26) |
| Navislamia (`master`) | `6d1e9c5d2a344855091606ebed05c613026a61f6` (2026-09-19) |
| Client — `SFrame.exe` | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 o.) |

## A VERIFIER PAR KILLIAN

Points que la lecture seule ne tranche pas et qui demandent une vérification **avec le client 7.3
réel** (donc avec Killian devant l'écran, jamais par un script ni par `SFrame.exe` lancé seul) :

| # | Point | Vérification à faire | Pourquoi la lecture ne suffit pas |
|---|---|---|---|
| 1 | La 11 est-elle nécessaire ? (§7a) | répondre puis ne pas répondre à la 550 et observer si le joueur reste correctement suivi/visible | le gestionnaire étiqueté `MSG_REGION_ACK` existe, mais aucun relevé ne montre l'effet d'une absence de réponse |
| 2 | Le client redemande-t-il après un warp ou un retour en jeu ? (§7b) | compter les 550 reçues autour d'un warp | les caches du client (queue BSS de `.data`, nuls au chargement) ne sont remis à zéro par aucun code identifié |
| 3 | Diviseur 180 ou 150 ? (§7e, §7f) | répondre d'abord avec 150, puis avec 180, et comparer le comportement de visibilité du client | l'identification du champ `region_size` du login comme unique écrivain du diviseur est forte mais indirecte |
| 4 | Le client contrôle-t-il la taille de la 11 ? (§7d) | envoyer une 11 plus courte puis plus longue que 15 octets | aucun test de longueur trouvé côté client |

Ce que cette fiche **ne** demande **pas** de vérifier : le format et les ids, tranchés par le client
(§2, §3.1), rzu et NGemity (§3.2, §4) — 15 octets des deux côtés, 550 et 11 en 7.3.
