# 801 — `TM_CS_TURN_OFF_PK_MODE` (Epic 7.3)

Fiche de paquet écrite par `navis-ref` le 25/09/2026, sur la branche
`hermes/packet-801-turn-off-pk-mode`, créée depuis `master` **`b56967a`**. Elle traite le **jumeau
éteint** de la fiche 800 : ce que le client envoie quand le joueur quitte le mode PK.

Trois documents la précèdent et sont la base de son analyse :

| Document | État | Rôle ici |
|---|---|---|
| `docs/packet-specs/socle-mode-pk.md` (fusionné, `9542c34`) | sur `master` | le socle : le masque de statut, le bit 11, le verrou de zone du client, les paquets restants (§9) |
| `docs/packet-specs/800-turn-on-pk-mode.md` | **branche non fusionnée** `hermes/packet-800-turn-on-pk-mode` (`a62dab3`, `5ef4009`) | le jumeau allumé : wire format identique, traitement, tests, bloc `CLAUDE.md` |
| cette fiche | branche `hermes/packet-801-turn-off-pk-mode` | **801 seul** : identité, gating, traitement, et la décision d'intégration avec le lot 800 (§5.6) |

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| id décimal | **801** (`0x321`) | `op_codes.md:183` (`[801] = "TM_CS_TURN_OFF_PK_MODE"`) |
| nom | `TM_CS_TURN_OFF_PK_MODE` | `op_codes.md:183` |
| sens | client → serveur | rzu `CREATE_PACKET_VER_ID(TS_CS_TURN_OFF_PK_MODE, SessionType::GameClient, SessionPacketOrigin::Client)`, `librzu/src/packets/GameClient/TS_CS_TURN_OFF_PK_MODE.h:11` |
| nature | **trame à en-tête seul** : 7 octets, corps vide, aucune réponse serveur | §3, §5.4 |
| déclaration rzu | `librzu/src/packets/GameClient/TS_CS_TURN_OFF_PK_MODE.h:5-11` | corps : `#define TS_CS_TURN_OFF_PK_MODE_DEF(_)` (aucun champ) |
| déclaration NGemity | `shared/Server/Packets/GameClient/TS_CS_TURN_OFF_PK_MODE.h:6,8` (`CREATE_PACKET(TS_CS_TURN_OFF_PK_MODE, 801)`, `EPIC_4_1_1`) | — |
| constructeur client | `SFrame.exe+0x684c00` (id `0x321` écrit à `+0x684c22`) | §3.1, vérifié au désassemblage |
| fiche sœur (allumage) | 800, `hermes/packet-800-turn-on-pk-mode` | wire format **identique**, traitement **symétrique** |

`find reference/rzu reference/ngemity -iname '*PK*' -not -path '*/.git/*'` ne rend **que** les deux
en-têtes `TS_CS_TURN_ON_PK_MODE.h` et `TS_CS_TURN_OFF_PK_MODE.h` de chaque dépôt (plus `vcpkg.json`
et `vcpkg/`, sans rapport). Il n'existe donc **aucune** trame `TM_SC_*` de PK, ni chez les deux
références, ni dans `op_codes.md` (`grep -n "PK" op_codes.md` → lignes 182-183 seulement).

### 1.1 Périmètre de ce lot

Cette branche livre **801 seul**. Le jumeau 800 appartient à sa propre branche, non fusionnée à ce
jour (§5.6) : les deux lots partagent la même base `b56967a` et la même région de code, la
décision d'ordre de fusion est donc explicitement reportée à Killian (§9 point 1).

Ce que la branche doit livrer : un membre d'énumération, un lecteur de trame à en-tête seul, un bras
de dispatch, l'émission du masque, et au moins un test d'offsets (§5.7). Ce qu'elle ne doit pas
livrer : aucune réponse serveur, aucune règle de zone, aucune règle de mort, aucune diffusion à un
tiers (§5.4, §5.6, §7).

---

## 2. Ce que le joueur fait pour que le client envoie 801

Deux gestes, **un seul mécanisme d'émission**, et deux sites seulement. Ce qui suit a été établi au
désassemblage de `SFrame.exe`
(`sha256 41e0af2e…500e`, §8) et complété par un balayage **exhaustif** des `e8 rel32` de tout
`.text` (§2.3) — aucune exécution du client.

### 2.1 La bascule : c'est l'état publié par le serveur qui choisit le paquet

Le gestionnaire de bascule commence à `SFrame.exe+0x68afcb`. Le choix entre 800 et 801 est un test du
**bit 11 du masque de statut** que le serveur a publié :

| Adresse | Instruction | Sens |
|---|---|---|
| `+0x68afcb` | entrée du gestionnaire (`call 0x68aa00`, `call 0x696d30`, `cmp eax,2`) | — |
| `+0x68b006` | `test eax,0x800` | le bit `TCS_FlagPkOn` / `CreatureStatus.PlayerPkOn`, `1 << 11` |
| `+0x68b00d` | `call 0x684c00` | **bit présent** ⇒ le client émet **801** |
| `+0x68b035` | `call 0x684bb0` | bit absent ⇒ le client émet 800 (lot frère) |

Conséquence à retenir pour le dev : **le serveur qui publie un masque faux fait osciller le client
entre 800 et 801**. Rien dans la réception de 801 ne doit donc « basculer » quoi que ce soit de son
côté (§5.3) : c'est le client qui décide, à partir du masque.

### 2.2 La commande explicite, et le verrou qu'elle ne consulte pas

La fonction `SFrame.exe+0x684fa0` prend un booléen en argument (`cmp BYTE PTR [ebp+0x8],0x0` à
`+0x684fa6`) et sert les deux sens :

| Branche | Condition | Émission |
|---|---|---|
| « on » | argument vrai, octet `[esi+0x660]` **nul** (`+0x684fb2`) | 800 (`+0x684fbb`) |
| « on » | argument vrai, `[esi+0x660]` **non nul** | **rien** : `xor al,al` et retour (`+0x684fc2`) — refus **local** du client |
| « off » | argument faux (`+0x684fc7` → `je`) | **801** (`+0x684fce`) |

**Asymétrie à noter** : le chemin « off » **ne consulte jamais** `[esi+0x660]`. Le verrou de zone du
client (dont la sémantique reste non identifiée, `socle-mode-pk.md` §12.1) ne bloque que l'allumage.
Il n'y a donc **aucune** indication, côté client, d'un état où l'extinction serait interdite — le
serveur n'a pas de raison d'en inventer un (§7 point 1).

### 2.3 Les deux seuls sites d'émission (balayage exhaustif)

Balayage de tout `.text` (`0x401000`, `0x60a8cf` octets, offset fichier `0x400`) à la recherche des
appels relatifs `e8 rel32` dont la cible est le constructeur 801 — et, comme témoin de méthode, du
constructeur de 550 (`.text` de `SFrame.exe`, aucun autre outil) :

| Constructeur | Sites d'appel trouvés | Sens |
|---|---|---|
| `0x684c00` (**801**) | **2** : `0x684fce`, `0x68b00d` | commande explicite « off », bascule (bit 11 publié) |
| `0x684bb0` (800) | 2 : `0x684fbb`, `0x68b035` | commande « on », bascule (bit 11 absent) |
| `0x684b60` (550, témoin) | 1 : `0x68bfb0` | recoupe `docs/packet-specs/550-get-region-info.md` — la méthode est validée |

Aucune occurrence de l'immédiat `0x684c00` dans `.text` (pas d'entrée de table) : les deux sites
ci-dessus sont **exhaustifs**. Deux précisions sur la façon dont les deux gestionnaires sont atteints,
toutes deux mesurées :

- `0x684fa0` **n'a aucun appel relatif** (0 site `e8 rel32`) et **une seule** référence absolue dans
  tout le binaire : `.rdata` VA `0xa54dac` — cohérent avec une entrée de table (vtable/interface),
  jamais appelée directement ;
- `0x68afcb` **n'a aucun appel relatif** non plus, et **aucune** référence absolue dans les sections
  lues (`.text`, `.rodata`, `.rdata`, `.data`, `.tls`, `.rsrc`) : sa façon d'être atteint n'est donc
  pas établie par la lecture statique (§7 point 10).

### 2.4 Les chaînes que le client attache au geste

| Chaîne | Source | Contenu |
|---|---|---|
| `/PKON /PKOFF` | `db_string.rdb`, `strings -n 5` l. 4400 (`tooltip_order_10`) | `Turn PK mode on/off.<BR>With PK mode on, you can<BR>battle with other players.<BR><#17f39c>/PKON /PKOFF` |
| libellé de touche | `db_string.rdb` l. 111993 (`Toggle PK Mode`) et l. 112057 (`Toggle PK Mode Key`) | encadre l'entrée de commande |
| `smsq_pkmode_off` | `db_string.rdb` l. 75325-75326 | texte du client, affiché **localement** |
| alias de commande locale | `db_localcommand.rdb`, `strings -n 3` l. 72-73 | `PK pkoff` puis ` pkoff` — **aucun jeton `pkon`** |

Lecture par chaînes uniquement : le rattachement exact de `/PKOFF` au handler `0x684fa0` n'est établi
qu'à moitié (§7 point 3), et c'est la seule zone d'ombre de ce chapitre.

### 2.5 Conséquence opérationnelle pour le serveur

Le client n'émet 801 que dans deux situations : **il a reçu le bit 11 allumé** (bascule, §2.1) ou
**une commande locale d'extinction** a été saisie (§2.2). Le serveur ne peut donc pas provoquer
801 ; il peut seulement le recevoir. Inversement, un serveur qui n'arme pas 801 laisse le joueur
dans un mode PK qu'il ne peut plus quitter par le geste normal — l'enjeu de ce lot est là.

---

## 3. Structure sur le fil

rzu ne déclare aucun champ :

```c
// reference/rzu/librzu/src/packets/GameClient/TS_CS_TURN_OFF_PK_MODE.h:5
#define TS_CS_TURN_OFF_PK_MODE_DEF(_)         // corps vide : rzu n'écrit aucun champ
```

L'en-tête de 7 octets du dépôt (`CLAUDE.md:51-53`, `Game/Network/Packets/Header.cs:6-25`) porte
`uint Length`, `ushort ID`, `byte Checksum`, et le checksum est **la somme des six premiers octets**
(`Game/Network/Packets/PacketExtensions.cs:13-25`).

### 3.1 `TM_CS_TURN_OFF_PK_MODE` (801) — **taille totale attendue : 7 octets**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0 | `uint32` LE | `Length` | `0x00000007` (7) | client : `mov DWORD PTR [eax],0x7` à `SFrame.exe+0x684c08`, réécrit à l'identique en `+0x684c2d` ; rzu : `TS_CS_TURN_OFF_PK_MODE_DEF(_)` vide (`TS_CS_TURN_OFF_PK_MODE.h:5`) ; dépôt : `Header.cs:9` |
| 4 | `uint16` LE | `ID` | `0x0321` = **801** | client : `mov edx,0x321` à `SFrame.exe+0x684c22` puis `mov WORD PTR [eax+0x4],dx` à `+0x684c27` ; `op_codes.md:183` ; `Header.cs:10` |
| 6 | `uint8` | `Checksum` | `0x2B` = **43** | client : `mov BYTE PTR [ecx],dl` à `SFrame.exe+0x684c47`, après une seconde passe de somme (`+0x684c33-+0x684c47`) ; règle : `PacketExtensions.cs:13-25` et `CLAUDE.md:51-53` ; `Header.cs:11` |
| 7 | — | *corps* | **vide (0 octet)** | rzu `TS_CS_TURN_OFF_PK_MODE.h:5` ; le constructeur `+0x684c00-+0x684c4a` n'écrit aucun octet après l'en-tête (`ret` en `+0x684c4a`) |

Octets attendus, tels que le client les assemble :

```
07 00 00 00 21 03 2b
```

Vérification du checksum (règle du dépôt, calcul hors client) : `0x07 + 0x21 + 0x03 = 43 = 0x2B`.
Pour mémoire, le jumeau 800 donne `07 00 00 00 20 03 2a` (checksum `0x2A` = 42).

Détail utile au lecteur du désassemblage : le constructeur calcule la somme **deux fois** — une
première passe en `+0x684c14-+0x684c1b`, avant que l'id soit écrit (le résultat est écrasé par le
`mov BYTE PTR [eax+0x6],dl` de `+0x684c1f`, `dl` remis à zéro), puis une seconde passe en
`+0x684c33-+0x684c47` sur l'en-tête **final**. Seule la seconde compte : longueur 7, id `0x321`.

### 3.2 Ce que la boucle de réception du dépôt fait déjà de cette trame

| Étape | Comportement | Source |
|---|---|---|
| longueur d'en-tête | `HeaderLength = Marshal.SizeOf<Header>()` = 7 | `Game/Network/Clients/GameClient.cs:1206` |
| trame à en-tête seul | la boucle teste `remainingData >= HeaderLength` — une trame **seule dans une lecture TCP** est donc correcte | `GameClient.cs:1232-1233` ; piège documenté `CLAUDE.md:55-59` |
| checksum | vérifié avant tout dispatch ; un checksum faux **déconnecte** | `GameClient.cs:1235`, `:1256-1262` |
| borne de longueur | `header.Length < 7` ou `> 32768` ⇒ journal `Error` + déconnexion : une trame de 6 octets n'atteint jamais le dispatch | `GameClient.cs:1239-1245`, `:1209` |
| id non déclaré | `if (!DefinedPackets[header.ID])` ⇒ journal `Debug` « Undefined packet ID » et `continue` — **c'est ce qui arrive aujourd'hui à tout 801 reçu** | `GameClient.cs:1215-1225`, `:1269-1273` |
| id déclaré sans bras | le `switch` final se termine par `_ => throw new Exception($"Unknown Packet Type {header.ID}")` : une exception dans la boucle de réception casse la session | `GameClient.cs:1854-1866` (throw en `:1865`) |

Autrement dit : **déclarer 801 sans lui donner de bras est pire que ne rien faire**, et un 801
aujourd'hui est consommé puis ignoré en silence — le mode PK ne s'éteint par le protocole dans aucun
cas (`/pk off` reste le seul chemin, `docs/gm-commands.md:54`).

### 3.3 Piège des trames à en-tête seul

801 est exactement de la famille de 23, 25 et 27 (`CLAUDE.md:55-59`). Le bras doit donc **ne rien
lire après l'en-tête** — pas de champ optionnel, pas de « corps éventuel ». La forme du bras existe
déjà dans le fichier : `GameClient.cs:1745-1771` (`TM_CS_REQUEST_RETURN_LOBBY`, `TM_CS_RETURN_LOBBY`,
`TM_CS_REQUEST_LOGOUT`, `TM_CS_LOGOUT`), `if (header.ID == …) { journal ; action ; continue; }`,
posée **avant** le `switch` final.

---

## 4. Gating de version — tranché pour l'Epic 7.3

| Élément | Gating rzu | Source | Décision 7.3 |
|---|---|---|---|
| `ID` | `X(801, version < EPIC_9_6_3)` / `X(1801, version >= EPIC_9_6_3)` | `librzu/src/packets/GameClient/TS_CS_TURN_OFF_PK_MODE.h:7-9` | **801** (et `1801` **ne doit pas** être déclaré) |
| `Length`, `Checksum` | aucun gating | `TS_CS_TURN_OFF_PK_MODE_DEF(_)` vide, `TS_CS_TURN_OFF_PK_MODE.h:5` | inchangés : 7 / somme des six premiers octets |
| corps | aucun champ ⇒ aucun gating possible | `TS_CS_TURN_OFF_PK_MODE.h:5` | néant |
| état PK lui-même | pas de gating de version, mais un **gating de protocole** : le bit 11 du masque | `CreatureStatus.cs:47`, rzu `TS_SC_STATUS_CHANGE.h:25` (`TCS_FlagPkOn = 1 << 11`) | bit 11, jamais un paquet |

Justification arithmétique et textuelle : `EPIC_7_3 = 0x070300`
(`librzu/src/lib/Packet/PacketEpics.h:59`) est strictement inférieur à
`EPIC_9_6_3 = 0x090603` (`PacketEpics.h:96`, commentaire *« GS packet ID modified with version
20200713 »*). Le 7.3 du dépôt est donc du côté `version < EPIC_9_6_3`. Vérification sur `master` :
`grep -n "1800\|1801" Game/Network/Packets/Enums/GamePackets.cs` → **aucun résultat** ; l'id 1801 est
libre et doit le rester.

Le **client** tranche dans le même sens, et indépendamment de rzu : il enregistre la chaîne
`TM_CS_TURN_OFF_PK_MODE` (dans `.rdata`, VA `0xa530f8`, offset fichier `6626040`) **avec l'id
`0x321`** — `push 0xa530f8` en `SFrame.exe+0x678566`, puis `mov eax,0x321` en `+0x678588`, suivis de
l'appel qui range la paire dans la table d'annotation ; la même séquence existe en `+0x678504`/
`+0x678526` pour `TM_CS_TURN_ON_PK_MODE` et `0x320`. Le binaire ne contient **que deux** chaînes de
nom PK (`strings -n 4 SFrame.exe` filtré sur `TM_.*PK` → l. 26309-26310) : aucune entrée
`0x708`/`0x709` (1800/1801) ne peut être enregistrée sous ces noms.

NGemity, compilé en `EPIC_4_1_1`, ne connaît qu'un id : `CREATE_PACKET(TS_CS_TURN_OFF_PK_MODE, 801)`
(`shared/Server/Packets/GameClient/TS_CS_TURN_OFF_PK_MODE.h:8`) — même verdict, sans gating.

**Aucun champ de cette fiche n'a donc de gating non statué** : le seul piège de version est dans
l'id, et il est tranché.

---

## 5. Traitement attendu

### 5.1 Déclarer **et** armer, dans le même commit

1. **Énumération** — `Game/Network/Packets/Enums/GamePackets.cs`, qui est ordonné par id :
   `TM_CS_TURN_OFF_PK_MODE = 801` s'insère entre `TM_CS_DROP_QUEST = 603` (`GamePackets.cs:141`) et
   `TM_CS_CHANGE_LOCATION = 900` (`GamePackets.cs:142`), **après** `TM_CS_TURN_ON_PK_MODE = 800`
   quand le lot frère sera fusionné (§5.6). C'est le même voisinage que le tableau d'annotation du
   client (§4), qui enchaîne 710, 800, 801 puis 900. L'énumération compte **140** membres sur
   `master` (`grep -oE '^\s+TM_[A-Z0-9_]+'`).
2. **Bras de dispatch** — avant le `switch` final (`GameClient.cs:1854-1866`), à la manière des
   trames à en-tête seul (`GameClient.cs:1745-1771`). C'est le critère transversal n° 4 : **un membre
   de `GamePackets` qui atteint le `throw` de `GameClient.cs:1865` casse la boucle de réception.**
3. **Lecteur** — l'emplacement naturel est `Game/Network/Packets/Game/GameActionPackets.cs`
   (`HeaderSize = 7` en `:9`, fichier de 750 lignes), sous la forme exacte retenue par le lot 800 :
   une constante de taille + un prédicat `packet.Length == …` (§5.2). Nom proposé, symétrique :
   `TurnOffPkModeLength` et `TryReadTurnOffPkMode`.

### 5.2 Validation de longueur

Le client n'a **qu'un** producteur de cette trame et il écrit toujours 7 octets (§3.1). Une longueur
différente est donc une anomalie de protocole, pas une variante :

- 6 octets et moins : **ne peuvent pas arriver au dispatch** (déconnexion en `GameClient.cs:1239-1245`) ;
- 8 octets et plus : arrivent, et doivent être **refusés** — journal `Warning` puis trame ignorée,
  **aucun octet lu après l'en-tête**. C'est la recommandation déjà écrite pour 800
  (`800-turn-on-pk-mode.md` §7 point 5), à reprendre à l'identique pour que les deux jumeaux se
  comportent pareil.

### 5.3 L'action : muter l'état, republier le masque

C'est **exactement** ce que fait déjà la commande `/pk off` livrée avec le socle :

| Étape | Code de `master` | Source |
|---|---|---|
| 1. éteindre l'état | `info.PkMode = false;` | équivalent de `GmCommandService.cs:298` (`/pk`) et de `ConnectionInfo.PkMode` (`ConnectionInfo.cs:99-105`) |
| 2. republier le masque | `TM_SC_STATUS_CHANGE` (500), **15 octets**, `handle` en 7, `status` en 11, checksum d'en-tête `0x04` (15 + 0xF4 + 0x01 → dernier octet) | `GameCharacterPackets.BuildStatusChange(uint handle, uint status = 0)` (`Game/Network/Packets/Game/GameCharacterPackets.cs:326`) |
| 3. composition du masque | `ActorStatus.ForPlayer(info.PkMode, info.IsSitting, info.IsBattleMode, info.IsWalking)` — **instantané complet, jamais un delta** | `Game/Network/Packets/Game/ActorStatus.cs:25-50` |
| 4. modèle d'émission déjà écrit | `GmCommandService.SendStatus(GameClient)` — `private static`, donc **non réutilisable tel quel** | `Game/Services/GmCommands/GmCommandService.cs:632-637` |

**Réserve de méthode, à ne pas confondre avec une source de politique** : que la commande
d'administration `/pk off` éteigne le drapeau **sans condition** ne tranche **pas** la conduite de
801. Ce qui précède est une **observation du dépôt** — l'emplacement exact où le drapeau existe, se
lit et se publie — et c'est ce dont le paquet a besoin pour être branché sur le socle. La question
« existe-t-il un état où l'extinction doit être refusée ? » reste **ouverte** et se porte en
`## A VERIFIER PAR KILLIAN` (§7 point 1, §9 point 2), jamais en règle devinée.

Un masque partiel effacerait les autres drapeaux : un joueur assis qui éteint son mode PK doit
rester assis. Les tests qui tiennent cet invariant existent déjà
(`PkModeStatusTests.ActorStatus_ComposesTheWholeMaskByActorNature`,
`GmCommandRulesTests.cs:309`).

**Le paquet n'est pas une bascule.** Recevoir 801 deux fois de suite republie le **même** masque
(bit 11 à zéro) : la seule source de vérité est `ConnectionInfo.PkMode`, et c'est le client qui
choisit entre 800 et 801 d'après le masque publié (§2.1). Aucun comptage, aucun état d'attente côté
serveur.

### 5.4 Ce que le serveur doit répondre

**Rien.** Aucun `TM_SC_*` PK n'existe (§1), et un refus n'aurait aucun véhicule établi :
`ResultCode.PKLimit = 29` existe (`Game/Network/Packets/ResultCode.cs:38`) et `SendResult(ushort id,
ushort result, int value = 0)` est disponible (`GameClient.cs:68`), mais **rien n'établit que le
client 7.3 lise un `TM_SC_RESULT` portant l'id 801** (même réserve que pour 800, §7 point 4). Choix :
silence.

### 5.5 Persistance

Aucune écriture nouvelle. `ConnectionInfo.PkMode` est chargé depuis `Characters.PkMode` à l'entrée en
monde (`Actions/GameActions.cs:127`) et réécrit à la déconnexion (`GameClient.cs:771-773` →
`CharacterService.SaveProgressAsync(…, pkMode)` `Game/Services/CharacterService.cs:462-463,493`,
colonne `CharacterEntity.PkMode` `Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:85`).
Le paquet n'a qu'à muter `info.PkMode`.

### 5.6 Le jumeau 800 : état des lieux, et ce que cette branche doit décider

Le lot 800 (branche `hermes/packet-800-turn-on-pk-mode`, 6 commits, **même base `b56967a`**,
**non fusionné**) apporte déjà :

| Ce que le lot 800 apporte | Emplacement sur sa branche |
|---|---|
| `TM_CS_TURN_ON_PK_MODE = 800` | `GamePackets.cs:149` (entre 603 et 900, même ancre que 801) |
| `TurnOnPkModeLength` + `TryReadTurnOnPkMode` | `GameActionPackets.cs` (fin de fichier) |
| `GameClient.SendActorStatus()` (**public**, 8 lignes : `BuildStatusChange` + `ActorStatus.ForPlayer`) | `GameClient.cs:111` |
| `HandleTurnOnPkMode(byte[])` + bras de dispatch | `GameClient.cs:448`, `:1906` |
| `GmCommandService.SendStatus` **supprimée**, ses 5 appels (`/sitdown`, `/standup`, `/battle`, `/walk`, `/pk`) passent par `client.SendActorStatus()` | `GmCommandService.cs` |
| `Tests/Game/TurnOnPkModePacketsTests.cs` (14 tests) | dont `OnDataReceived_ConsumesTheUndeclaredTwin801WithoutThrowing` |

**Décision de cette fiche (à appliquer par le dev, révisable par Killian — §9 point 1)** :

- Cette branche part de `master`, donc elle **ne peut pas** appeler `GameClient.SendActorStatus()` :
  la méthode n'y existe pas. Elle doit publier le masque par elle-même.
- **Recommandation : ajouter la même méthode `GameClient.SendActorStatus()`, avec le même nom, la même
  signature et le même corps que le lot 800, et faire passer le bras 801 par elle.** Au rebasage des
  deux branches, le conflit est une **insertion identique au même endroit** : on en garde une seule,
  et les deux bras partagent alors le même site d'émission du masque. L'alternative — composer le
  masque *inline* dans le handler 801 — évite le conflit mais laisse deux compositions parallèles du
  même masque après la fusion, ce que le socle PK a justement supprimé.
- Ne **pas** toucher `GmCommandService.SendStatus` dans cette branche : elle est légitime sur
  `master` et c'est le lot 800 qui la supprime.
- Le test `OnDataReceived_ConsumesTheUndeclaredTwin801WithoutThrowing` du lot 800 **reste vert** après
  la fusion : il n'assert rien sur ce que 801 publiera, seulement qu'aucune exception ne sort de la
  boucle, que la trame est consommée (`BytesAvailable == 0`) et que `PkMode` reste faux
  (`TurnOnPkModePacketsTests.cs:240-260` sur la branche 800).

### 5.7 Tests exigés (critères transversaux)

1. `dotnet build Navislamia.sln -c Debug` code 0.
2. `dotnet test Tests/Tests.csproj` code 0, **au moins 366 tests**, et le compte ne baisse jamais.
   État de `master` mesuré par cette branche le 25/09/2026 sur `b56967a` :
   `Passed! - Failed: 0, Passed: 1302, Skipped: 0, Total: 1302`, durée 4 s, code de sortie **0**
   (`dotnet test Tests/Tests.csproj`, `NUGET_PACKAGES=/srv/navislamia/.nuget-cache`).
3. **Test d'offsets** pour 801 : la trame compte **7 octets** ; `Length` en 0-3 (`= 7`), `ID` en 4-5
   (`= 801`), `Checksum` en 6 (somme des octets 0-5 = `0x2B`, **valeur figée en dur** en plus de la
   règle du dépôt), corps vide. Le lecteur **accepte 7** et **refuse 6**, 8 et 15 ; les longueurs 8 et
   15 doivent en plus être **exercées à travers la vraie boucle** (`OnDataReceived`) pour montrer
   qu'elles sont consommées sans rien changer. Nom de fichier proposé :
   `Tests/Game/TurnOffPkModePacketsTests.cs` (symétrique de `TurnOnPkModePacketsTests.cs`).
   Modèles de structure et de harnais déjà présents sur `master` :
   `Tests/Game/PkModeStatusTests.cs:155-167` (cycle de vie du drapeau, `ClearCharacterSession`) et
   `:38` (`ActorStatus` compose le masque entier par nature d'acteur) ;
   `Tests/Game/GmCommandServiceTests.cs:555-563` (`/pk on` : le drapeau **et** la publication du bit
   dans `TM_SC_STATUS_CHANGE`) et `:152-158` (`/sitdown` : le bit PK est conservé).
   **Prouver que le test mord** : une assertion d'offset qui ne peut pas échouer ne prouve rien. La
   méthode retenue par le lot 800, à reprendre : muter temporairement l'id attendu (`801` → `1801`) et
   la comparaison de longueur (`== 7` → `>= 7`), relever le nombre d'échecs, puis annuler la mutation
   et vérifier l'arbre propre (`800-turn-on-pk-mode.md` §11.11).
4. **Enum et dispatch modifiés ensemble** : aucun membre de `GamePackets` ne peut atteindre le
   `throw` de `GameClient.cs:1865`.
5. Savoir durable dans cette fiche, commitée ; le bloc destiné à `CLAUDE.md` (§10) va dans la
   description de la MR — **le dev n'écrit pas `CLAUDE.md`**.
6. Version tranchée : 801 déclaré, 1801 non déclaré (test : `Enum.IsDefined(typeof(GamePackets),
   (ushort)1801).Should().BeFalse()`).
7. `git log --oneline origin/master..master` vide (aucun commit sur `master` locale).
8. Aucun `NON ÉTABLI` deviné : §7 est reporté tel quel dans `## A VERIFIER PAR KILLIAN` (§9).

### 5.8 Points de collision à signaler

Mesure du 25/09/2026 sur `master` (`b56967a`), `git branch -r --no-merged origin/master` puis
`git diff --name-only origin/master...<branche>` : **18 branches non fusionnées**, dont **14**
touchent `GamePackets.cs` et/ou `GameClient.cs` (`10000`, `214`, `215`, `221`, `223`, `258`, `259`,
`260`, `281`, `284`, `285`, `323`, `4003`, et **800** elle-même). Les quatre autres (`212`, `262`,
`263`, `264`) ne touchent aucun des deux.

Cette branche est la **quinzième** de ces points chauds, et la seule qui partage une base *et* une
ancre d'insertion avec une branche sœur précise :

```
hotspot: Game/Network/Packets/Enums/GamePackets.cs — insertion entre 603 et 900, même ancre que la branche 800
hotspot: Game/Network/Clients/GameClient.cs — bras de dispatch dans la même région, et SendActorStatus() dupliqué si le lot 800 fusionne après
```

Règle du dépôt : la fusion se fait **par rebasage**, jamais en éditant à la main une branche sœur.

---

## 6. Écarts assumés avec NGemity

NGemity de référence : `38ceb2c` (`EPIC_4_1_1`).

| Point | NGemity | NavisLamia : ce que cette branche décide | Écart assumé ? |
|---|---|---|---|
| Traitement de 801 | Déclaré (`shared/Server/Packets/GameClient/TS_CS_TURN_OFF_PK_MODE.h:6,8`), énuméré (`shared/Server/ClientPackets.h:194`), inclus (`shared/Server/XPacket.h:169`) — et **jamais traité** : `grep -rn "TS_CS_TURN" reference/ngemity/Chihiro/src/` ne rend **aucun** handler | Traité : mutation de `ConnectionInfo.PkMode` + republication du masque | **Oui, et c'est le but** : il n'y a rien à porter, seulement à brancher sur le socle PK déjà livré |
| État PK interne | `Player.h:265-267` : `IsPKOn()`, `SetPKOn()`, `SetPKOff()` ; `m_bIsPK` (`Player.h:437`, initialisé à `false`) — **aucun appel** à `SetPKOn`/`SetPKOff` dans `Chihiro/src/` | `ConnectionInfo.PkMode`, propriété au cycle de vie complet (chargement, publication, sauvegarde) | Non : NGemity n'offre qu'un squelette mort, jamais allumé |
| Publication du bit 11 | `Messages::GetStatusCode` **compose bien** `FLAG_PK_ON` à partir de `IsPKOn()` (`Chihiro/src/Network/Messages.cpp:557-583`, bit en `:578-579` ; `shared/Server/TS_MESSAGE.h:35` : `FLAG_PK_ON = 1 << 11`). Mais comme rien n'appelle `SetPKOn`, le bit est **toujours** faux chez NGemity | Le bit reflète un état réellement mutable, chargé et persisté ; c'est aussi le masque que le client teste en `SFrame.exe+0x68b006` pour choisir 800/801 | Non sur le principe, **oui sur le fait** : NavisLamia publie un bit qui peut être vrai. *Nuance corrigée par rapport à `800-turn-on-pk-mode.md` §6, qui lit `m_bIsPK` directement (`Player.cpp:3298`,`:3303`) et manque la lecture via `IsPKOn()` en `Messages.cpp:578` — la ligne reste à corriger dans la fiche 800, voir §7 point 9* |
| Persistance | Colonne `pkmode` (`Database/Telecaster.sql:151`) lue en `Player.cpp:208` (`SetInt32Value(PLAYER_FIELD_PK_MODE, (*result)[57].GetInt8())` ; champ `Object.h:123`) — **jamais réécrite** | Sauvegardée à la déconnexion (`GameClient.cs:771-773`) | Non : NavisLamia est plus complète, l'écart est en sa faveur |
| Gating d'id | Un seul id, 801, en `EPIC_4_1_1` | 801 / 1801, gating rzu rebasé sur 7.3 | Non : même id pour 7.3 |
| Message au joueur | `smsq_pkmode_off` présent en base Arcadia, jamais émis par le serveur | Aucun message émis : l'écho texte n'existe que pour `/pk` (`GmCommandService.cs:300`) | Assumé : ces libellés sont affichés **par le client lui-même**, localement, avant même d'envoyer (§2.4) |
| Règle de jeu (zone, moral, minuteur d'allumage, mort) | Aucune | **Aucune** : le mode est un état de session publié, sans restriction | Assumé — le verrou de zone est **local au client** et ne concerne pas 801 (§2.2, §7 point 1) |
| Extinction interdite dans certains états | Sans objet | Aucun état d'interdiction : le chemin « off » du client ne consulte aucun verrou (§2.2) | Non : rien à imiter |

---

## 7. NON ÉTABLI

1. **Un refus serveur a-t-il un sens pour 801 ?** Le client n'attend aucun paquet et n'affiche rien
   sur un refus ; le chemin d'extinction ne consulte même pas le verrou de zone (§2.2). Aucune des
   trois références ne refuse, et aucune n'établit un code de refus lisible pour l'id 801.
   **Choix retenu : silence.** Question précise : *existe-t-il un état (combat, instance, arène) où
   l'extinction doit être refusée, et avec quel véhicule ?*
2. **Le verrou de zone `[player+0x660]`.** Sa table de vérité est certaine pour l'**allumage**
   (valeurs 1, 3 ou 10 ⇒ autorisé, cf. `800-turn-on-pk-mode.md` §2.1), sa sémantique non identifiée
   (`socle-mode-pk.md` §12.1). Pour **801** la question est plus simple : le verrou n'est **jamais**
   consulté (§2.2), donc le serveur n'a aucun état de zone à reproduire pour ce paquet.
3. **`/PKOFF` rejoint-il le même handler ?** `db_string.rdb` l. 4400 annonce `/PKON /PKOFF`, mais
   `db_localcommand.rdb` ne contient qu'une entrée dont les jetons sont `PK` puis `pkoff`
   (`strings -n 3` l. 72-73) et **aucun jeton `pkon`**. Lecture par chaînes seulement, aucune
   exécution : le rattachement exact du mot à la fonction `0x684fa0` n'est établi qu'à moitié.
4. **Le refus, et son code.** `ResultCode.PKLimit = 29` (`ResultCode.cs:38`) et
   `SendResult(ushort id, ushort result, int value = 0)` (`GameClient.cs:68`) existent, mais rien
   n'établit que le client 7.3 lise un `TM_SC_RESULT` portant l'id 801. **Choix proposé : aucune
   réponse.**
5. **Faut-il exiger `header.Length == 7` avant d'appliquer ?** Les trames à en-tête seul de `master`
   (23/25/27) ne le font pas (`GameClient.cs:1745-1771`). Recommandation (§5.2) : oui, journal
   `Warning` et trame ignorée — c'est un choix, pas une lecture des références.
6. **L'annulation pendant le décompte de 360 s.** `smsq_pkmode_on` promet *« You can cancel it by
   clicking on the PK icon »* et le client arme un décompte local (`800-turn-on-pk-mode.md` §2.1).
   Est-ce que cette annulation émet 801 ? Non établi : la bascule ne teste que le bit publié, qui peut
   ne pas encore être allumé pendant le décompte. **Aucune conséquence serveur** : si le client
   n'émet rien, le serveur n'a rien à faire.
7. **Le mode PK survit-il à la mort du joueur ?** Aucun code de `master` ne touche `PkMode` au combat
   ni à la résurrection, aucune référence ne tranche. Question **hors périmètre de ce paquet**
   (elle appartient à `socle-mort-respawn`) ; 801 ne fait qu'éteindre ce qu'on lui envoie.
8. **La visibilité par un tiers.** Le dépôt ne diffuse pas encore les autres joueurs à un client
   (`800-turn-on-pk-mode.md` §5.4) : le masque n'atteint que le client de l'acteur. Le paquet est
   correct du point de vue du client concerné ; le sous-ensemble B du socle PK reste à faire.
9. **La correction de la fiche 800** (§6 de cette fiche) : `800-turn-on-pk-mode.md` §6 affirme que
   `m_bIsPK` « n'est lu qu'en `Player.cpp:3298` et `:3303` ». C'est exact pour les **lectures
   directes** du champ, mais `Messages.cpp:578` le lit via `IsPKOn()` pour composer `FLAG_PK_ON`.
   La fiche 800 n'est pas fusionnée : la correction appartient à son propre lot (ou à Killian au
   rebasage). Elle ne change **rien** au traitement de 800 ni de 801.
10. **Comment le gestionnaire de bascule `0x68afcb` est atteint.** Il n'a ni appel relatif ni
    référence absolue dans les sections lues (§2.3) : la lecture statique ne l'établit pas. **Sans
    conséquence serveur** — c'est un chemin du client, et les deux sites d'émission de 801, eux, sont
    exhaustifs et vérifiés.

---

## 8. Commits épinglés

| Référence | Commit | Contenu utilisé |
|---|---|---|
| `reference/rzu` | **`87c1e83b`** | `librzu/src/packets/GameClient/TS_CS_TURN_OFF_PK_MODE.h:5-11`, `…TS_CS_TURN_ON_PK_MODE.h:5-11`, `librzu/src/lib/Packet/PacketEpics.h:59,96`, `librzu/src/packets/GameClient/TS_SC_STATUS_CHANGE.h:25` |
| `reference/ngemity` | **`38ceb2c`** | `shared/Server/Packets/GameClient/TS_CS_TURN_OFF_PK_MODE.h:6,8`, `shared/Server/ClientPackets.h:194`, `shared/Server/XPacket.h:169`, `shared/Server/TS_MESSAGE.h:35`, `Chihiro/src/Network/Messages.cpp:557-583`, `Chihiro/src/Entities/Player/Player.h:265-267,437`, `Chihiro/src/Entities/Player/Player.cpp:208,3298,3303`, `Chihiro/src/Entities/Object/Object.h:123`, `Database/Telecaster.sql:151` |
| `Navislamia` | **`b56967a07430422add88e0e5cdf292b41b18f6c6`** | `master` au moment de l'écriture (merge PR #44) ; branche `hermes/packet-801-turn-off-pk-mode` créée depuis ce commit. Le lot frère `hermes/packet-800-turn-on-pk-mode` part de la **même** base |
| `reference/client73` | `SFrame.exe` **`41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`** (9 841 664 octets) — `client73` **n'est pas un dépôt git** : il n'y a pas de commit à épingler, l'empreinte du binaire est ce qui fixe la lecture | désassemblage `objdump -d -M intel` : `+0x684c00` (constructeur 801), `+0x684bb0` (800), `+0x684fa0` (commande), `+0x68afcb` … `+0x68b035` (bascule), `+0x678504-0x67859f` (table d'annotation) ; balayage `e8 rel32` de `.text` et recherche d'immédiats dans les sections (scripts locaux, aucune exécution du client) ; `db_string.rdb` (`strings -n 5`, l. 4400, 111993, 112057, 75319-75326) ; `db_localcommand.rdb` (`strings -n 3`, l. 72-73) |

Aucun accès réseau n'a été nécessaire : toutes les références sont locales.

---

## 9. A VERIFIER PAR KILLIAN

> Note du dev (25/09/2026) : aucun de ces sept points n'a été tranché ni contourné par le lot
> d'implémentation. Le traitement livré est le choix minimal documenté au §11, et **§11.8 reprend ces
> sept réserves** une par une ; le §9 est livré tel quel dans `## A VERIFIER PAR KILLIAN`.

1. **Ordre de fusion des deux jumeaux, et choix du site d'émission.** Les branches 800 et 801
   partagent la base `b56967a` et la même ancre dans `GamePackets.cs` : fusionner **800 d'abord**, puis
   rebaser 801 sur `master`, donne le résultat le plus propre (801 réutilise alors
   `GameClient.SendActorStatus()` au lieu de la dupliquer, §5.6). Si tu préfères 801 d'abord, la
   duplication est assumée et documentée — et si tu préfères que 801 **n'introduise aucune** méthode
   publique et compose son masque *inline* dans son bras, la fiche l'accepte aussi : c'est ce second
   choix, et lui seul, qui laisserait deux compositions parallèles du masque après la fusion.
2. **Aucune réponse serveur, aucune restriction** : confirmer que 801 reste muet (§5.4) et qu'aucun
   état n'interdit l'extinction (§7 point 1). En l'absence d'arbitrage, cette branche **n'invente
   aucune règle**.
3. **Longueur stricte** : exiger `header.Length == 7` avant d'appliquer, avec journal `Warning` et
   trame ignorée (§5.2, §7 point 5) ?
4. **Nom du fichier de tests** : `Tests/Game/TurnOffPkModePacketsTests.cs` (§5.7).
5. **Point chaud de fusion** : `GamePackets.cs` et `GameClient.cs` sont touchés par 14 branches non
   fusionnées (§5.8) ; le rebasage reste la règle.
6. **Bloc `CLAUDE.md`** : le §10 ci-dessous est à coller par toi (Hermes protège `CLAUDE.md` ; le dev
   ne doit pas l'écrire).
7. **Correction de la fiche 800** (§6, §7 point 9) : `Messages.cpp:578` compose `FLAG_PK_ON` via
   `IsPKOn()` — à intégrer au lot 800, sans effet sur le traitement.

---

## 10. Bloc destiné à `CLAUDE.md` (à coller par Killian)

```markdown
### Mode PK — les paquets 800 et 801

`TM_CS_TURN_ON_PK_MODE (800)` et `TM_CS_TURN_OFF_PK_MODE (801)` sont **des trames à en-tête seul**
(7 octets, corps vide, ids `0x320`/`0x321`), construites par le client
(`SFrame.exe+0x684bb0` et `+0x684c00`) — de la même famille que les trames à en-tête seul déjà
nommées en 23/25/27. Elles n'ont **aucune réponse serveur** : l'état PK circule dans le **bit 11 du
masque de statut** de `TM_SC_STATUS_CHANGE (500)`, avec `ActorStatus.ForPlayer`, qui est un
instantané complet (bit 11 = `CreatureStatus.PlayerPkOn`). C'est ce bit que le client teste
(`SFrame.exe+0x68b006`) pour choisir entre 800 et 801 : publier un masque faux fait osciller le
client entre les deux paquets.

Gating : rzu renomme les deux ids en 1800/1801 **à partir d'`EPIC_9_6_3`** — pour 7.3, ce sont 800
et 801. Traitement attendu : muter `ConnectionInfo.PkMode` puis republier le masque, exactement ce
que fait la commande `/pk` — un seul site compose le masque du joueur (`GameClient.SendActorStatus()`
quand les deux lots sont fusionnés). Le chemin « extinction » du client ne consulte **aucun** verrou
de zone : le serveur n'a rien à y reproduire. NGemity ne traite ni l'un ni l'autre paquet et
n'appelle jamais son `SetPKOn`/`SetPKOff` : il n'y a rien à y porter.

Détail complet : `docs/packet-specs/800-turn-on-pk-mode.md` et
`docs/packet-specs/801-turn-off-pk-mode.md`.
```
---

## 11. Implémentation livrée (dev)

Section ajoutée par `navis-dev` le 25/09/2026 ; l'analyse de l'archéologue (§1 à §10) est laissée
intacte. La branche `hermes/packet-801-turn-off-pk-mode` a été créée par `navis-ref` depuis `master`
(`b56967a`), qui y a commité cette fiche (`37f4678`). Commits du lot : `bc84521` (membre
d'énumération, lecteur, méthode d'envoi, handler et bras de dispatch), `d21d556`
(`Tests/Game/TurnOffPkModePacketsTests.cs`, 15 tests), puis le commit `docs(packet-specs)` de cette
section.

### 11.1 Checklist des critères transversaux, avec les codes de sortie relevés

| # | Critère | État | Mesure |
|---|---|---|---|
| 1 | `dotnet build Navislamia.sln -c Debug` code 0 | **OK** | code **0**, `0 Error(s)`, **164** avertissements — aucun ne cite un fichier du lot (`grep -iE` sur les quatre fichiers livrés → aucune ligne) |
| 2 | `dotnet test Tests/Tests.csproj` code 0, compte jamais en baisse | **OK** | base mesurée par la fiche (§5.7) : **1302** réussis / 1302 sur `b56967a`. Après le lot : code **0**, **1317** réussis / 1317, 0 échec, 0 ignoré → **+15** |
| 3 | Au moins un test d'offsets (taille totale et position de chaque champ) | **OK** | `Tests/Game/TurnOffPkModePacketsTests.cs` (§11.3) : `ClientFrame_IsSevenBytes_WithLengthAtZeroIdAtFourAndChecksumAtSix` (les quatre assertions d'offset et les sept octets exacts), `TryReadTurnOffPkMode_AcceptsTheSevenByteFrame`, `…RefusesEveryOtherLength` (6, 8, 15), plus l'exercice de la vraie boucle sur 8 et 15 ; la **preuve que les tests mordent** est en §11.11 |
| 4 | Enum et dispatch modifiés ensemble | **OK** | membre `TM_CS_TURN_OFF_PK_MODE = 801` (`GamePackets.cs:150`) **et** bras `if (header.ID == …)` en `GameClient.cs:1909`, **avant** le `switch` de `GameClient.cs:1924` qui lève `Unknown Packet Type` (balayage exhaustif en §11.7) |
| 5 | Savoir durable dans la fiche commitée + bloc `CLAUDE.md` dans la description de la MR | **OK côté fiche** | cette section ; le bloc complet à coller est en §11.9 (le dev n'écrit pas `CLAUDE.md`, Hermes protège ce fichier) |
| 6 | Version est tranchée | **OK** | **801 déclaré, 1801 non déclaré** (`Ids_AreTheEpic73Ones` vérifie `Enum.IsDefined(1801) == false`) ; 800 reste non déclaré ici et appartient à sa propre branche |
| 7 | Aucun commit sur `master` locale | **OK** | `git log --oneline origin/master..master` → aucune ligne (relevé §11.10) |
| 8 | Aucun champ `NON ÉTABLI` deviné | **OK** | aucune réponse, aucun code de refus, aucune restriction de zone, aucune règle de mort, aucune diffusion à un tiers : §11.6 point par point, et §9 est reporté tel quel dans `## A VERIFIER PAR KILLIAN` |

### 11.2 Fichiers livrés

| Fichier | Modification |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_TURN_OFF_PK_MODE = 801` (+10 lignes, commentaire de gating inclus), inséré entre `TM_CS_DROP_QUEST = 603` et `TM_CS_CHANGE_LOCATION = 900` |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `TurnOffPkModeLength` (7, `:758`) et `TryReadTurnOffPkMode(ReadOnlySpan<byte>)` (`:771`) (+22) |
| `Game/Network/Clients/GameClient.cs` | `SendActorStatus()` public (`:111`), `HandleTurnOffPkMode(byte[])` (`:450`), bras de dispatch (`:1909`) (+52) |
| `Tests/Game/TurnOffPkModePacketsTests.cs` | **nouveau**, 15 tests |
| `Game/Services/GmCommands/GmCommandService.cs` | **inchangé** — décision §5.6 : c'est le lot 800 qui supprime sa copie privée de la composition du masque |

Aucun constructeur de trame descendante n'est ajouté : la fiche établit qu'il n'existe **aucune**
trame serveur → client de cette famille (§1, §5.4) — `GameCharacterPackets.cs` est inchangé.

### 11.3 Offsets livrés, et les tests qui les tiennent

| Offset | Taille | Champ | Valeur livrée | Test |
|---|---|---|---|---|
| 0 | 4 | `Length` `uint32` LE | **7** | `ClientFrame_IsSevenBytes_WithLengthAtZeroIdAtFourAndChecksumAtSix` |
| 4 | 2 | `ID` `uint16` LE | **801** (`0x321`), l'id du client | `…WithLengthAtZeroIdAtFourAndChecksumAtSix`, `Ids_AreTheEpic73Ones` |
| 6 | 1 | `Checksum` | somme des octets 0-5 = **0x2B** (`07 00 00 00 21 03`) | `…ChecksumAtSix` (deux assertions : la règle du dépôt **et** la valeur figée `0x2B`) |
| 7 | 0 | — corps vide — | taille totale = `GameActionPackets.TurnOffPkModeLength` = **7** | `frame.Should().HaveCount(7)` et `frame.Should().Equal(07 00 00 00 21 03 2B)` dans le même test |

`TryReadTurnOffPkMode` **n'accepte que les 7 octets** (`packet.Length == TurnOffPkModeLength`) : le
client n'a qu'un producteur de cette trame et il écrit toujours 7 (§3.1), donc 8 et plus sont des
anomalies de protocole. Le refus de 6 est testé aussi, mais il **ne peut pas venir de la boucle** : un
`Length` inférieur à l'en-tête fait `Connection.Disconnect()` avant tout dispatch
(`GameClient.cs:1287-1292`) — c'est écrit dans le commentaire du lecteur, et c'est pour ça que le
lecteur le refuse lui aussi (appel direct). Les longueurs 8 et 15 sont en plus **exercées à travers la
vraie boucle de réception** : `OnDataReceived_ConsumesAPaddedFrameWithoutTurningTheModeOff` et
`…ALongerFrameWithoutTurningTheModeOff` (trame consommée en entier, `PkMode` intact, `Connection.Sent`
vide).

### 11.4 Ce qui est publié : le masque, et rien d'autre

Par trame 801 acceptée, **exactement une** `TM_SC_STATUS_CHANGE` (500) est envoyée : 15 octets,
`Length` 0-3 = 15, `ID` 4-5 = 500, checksum en 6, `handle` en 7 (= `ConnectionInfo.CharacterHandle`),
masque en **11** avec le **bit 11 effacé**. C'est le seul envoi :
`OnDataReceived_TurnsThePkModeOffAndPublishesTheStatusMaskAsTheOnlyAnswer` assert
`connection.Sent.Should().ContainSingle()`. Aucun `TM_SC_RESULT`, aucun paquet PK (§5.4, §7 point 4).
Une seconde trame 801 republie le **même** masque — le paquet n'est pas une bascule
(`OnDataReceived_TurningTheModeOffTwiceKeepsTheSameMask`) : c'est bien le lecteur du bit 11 côté
client qui choisit entre 800 et 801, pas le serveur qui alterne.

Le masque publié est l'**instantané complet**, jamais un delta : un joueur assis et en mode combat qui
éteint son mode PK reste assis et en combat
(`OnDataReceived_ClearsThePkBitWithoutTouchingTheOtherFlagsOfTheSnapshot`, qui compare au masque entier
`ActorStatus.ForPlayer(pkModeOn: false, sitting: true, battleMode: true)`). C'est l'invariant que le
socle PK a introduit et que `PkModeStatusTests` tient déjà pour les autres drapeaux.

Journalisation : trame valide → **`Debug`** (l'idiome du dépôt pour une trame client reçue) ; longueur
différente de 7 → **`Warning`** puis trame ignorée, sans rien lire après l'en-tête — la recommandation
explicite de §5.2 et §7 point 5, appliquée telle quelle (§11.8 réserve 1).

### 11.5 Le site d'envoi du masque, et le jumeau 800

`GameClient.SendActorStatus()` (`GameClient.cs:111`) compose
`BuildStatusChange(info.CharacterHandle, ActorStatus.ForPlayer(info.PkMode, info.IsSitting,
info.IsBattleMode, info.IsWalking))`, avec **le même nom, la même signature et le même corps** que la
méthode du lot frère 800 : c'est la recommandation de §5.6, choisie pour que le rebasage des deux
branches soit une **insertion identique au même endroit** (on en garde une seule) au lieu de laisser
deux compositions parallèles du masque après la fusion. Le bras 801 publie par cette méthode
(`Packet801AndTheGmCommandPublishTheSameFrame` : la trame du paquet et l'appel direct de
`SendActorStatus()` produisent des octets identiques, donc le paquet ne compose pas son masque à part).

`GmCommandService.SendStatus` n'est **pas** touchée par cette branche : elle est légitime sur `master`
et c'est le lot 800 qui la supprime (§5.6). Conséquence assumée et bornée : tant que 800 n'est pas
fusionné, `master` + ce lot comptent deux compositions du masque, **octet pour octet identiques**
(même expression `ActorStatus.ForPlayer(...)`) ; après la fusion de 800, il n'en reste qu'une. Le test
`Pk_SetsTheModeAndPublishesItsBit` (`GmCommandServiceTests.cs:555-563`) et les tests de
`PkModeStatusTests` passent **sans modification**.

### 11.6 Ce qui n'est pas porté, et pourquoi

- **Aucune réponse, aucun code de résultat** : aucun `TM_SC_*` PK n'existe (§1) et rien n'établit que
  le client 7.3 lirait un `TM_SC_RESULT` portant l'id 801 (§5.4, §7 point 4). `ResultCode.PKLimit`
  reste inutilisé.
- **Aucune restriction, aucun refus** : le chemin « extinction » du client ne consulte **aucun** verrou
  de zone (`SFrame.exe+0x684fc7`, §2.2), donc aucun état d'interdiction n'est établi ; §7 point 1 reste
  ouvert et le paquet ne devine rien (§9 point 2).
- **Aucune condition de zone ni de monde** dans `HandleTurnOffPkMode` : le seul fait de recevoir 801
  sur une session suffit. La conséquence est bornée — `PkMode` est **rechargé depuis
  `Characters.PkMode` à l'entrée en monde** (`Actions/GameActions.cs:127`) et remis à **faux** par
  `ClearCharacterSession` (`ConnectionInfo.cs:336`), donc une trame reçue hors session de personnage ne
  survit pas à la session suivante.
- **Aucune écriture de persistance** : elle est déjà en place (§5.5), le paquet ne fait que muter
  `ConnectionInfo.PkMode`.
- **Aucune diffusion à un tiers** : impossible dans cette base (§5.4, §7 point 8) ; c'est le
  sous-ensemble B du socle PK, non fusionné.
- **Aucune limitation de fréquence** : rien ne l'établit, et les deux sites d'émission du client sont
  des gestes manuels (§2.3).
- **Le jumeau 800 reste non déclaré** : une trame 800 est consommée comme un id inconnu
  (« Undefined packet ID », `Debug`) et la connexion reste ouverte.
  `OnDataReceived_ConsumesTheUndeclaredTwin800WithoutThrowing` le pin, et il est écrit pour **rester
  vert** quand le lot 800 fusionnera (il part d'un mode déjà allumé et n'assert rien d'autre).
- **L'état PK n'est toujours visible que du client de l'acteur** (§7 point 8) : le paquet rend
  l'extinction correcte du point de vue du client concerné, pas le PK visible par un tiers.

### 11.7 Point de collision : mesure refaite

Mesure du 25/09/2026 sur cette branche, `git merge-tree --write-tree` (aucun index ni worktree
touché), contre les **18** branches sœurs non fusionnées de `origin/master`. Deux comptages par
branche sœur : les conflits contre `origin/master` seul (la base, conflits qui **précèdent** ce lot) et
contre `HEAD` (base + ce lot) :

| Branche sœur | conflits base | conflits avec le lot | ajoutés par le lot |
|---|---|---|---|
| `packet-800-turn-on-pk-mode` (**le jumeau**) | 0 | **3** | **3** — `GamePackets.cs`, `GameClient.cs`, `GameActionPackets.cs` |
| `packet-258-donate-item` | 4 | 4 | **0** |
| `packet-285-unbind-skillcard` | 6 | 6 | **0** |
| `packet-284-bind-skillcard` | 5 | 5 | **0** |
| `packet-214-puton-card` | 3 | 3 | **0** |
| `packet-10000-open-item-shop` | 2 | 2 | **0** |
| `packet-221-hide-equip-info` | 2 | 2 | **0** |
| `packet-223-swap-equip` | 2 | 2 | **0** |
| `packet-259-donate-reward` | 1 | 1 | **0** |
| `packet-281-puton-item-set` | 1 | 1 | **0** |
| `packet-4003-huntaholic-create-instance` | 1 | 1 | **0** |
| les 7 autres (`212`, `215`, `260`, `262`, `263`, `264`, `323`) | 0 | 0 | **0** |

Contre les **17** branches qui ne sont pas le jumeau, le lot **n'ajoute aucune zone de conflit** : les
27 conflits constatés sont ceux que chaque branche sœur a **déjà contre la base**, et l'insertion de ce
lot tombe hors de leurs points d'ancrage (membre d'énumération entre 603 et 900, bras de dispatch
juste après celui de `TM_CS_XTRAP_CHECK` (59), handler et méthode d'envoi dans des régions disjointes).

**Le seul conflit ajouté est celui du jumeau 800, et il est attendu** (§5.6, §5.8) : les deux branches
partagent la base `b56967a` et insèrent au même endroit. Les trois conflits se résolvent en gardant
**les deux** côtés — deux membres d'énumération adjacents (800 puis 801), deux bras de dispatch
adjacents, deux blocs de fin de `GameActionPackets.cs` — et **une seule** copie de
`SendActorStatus()`, qui a le même corps des deux côtés. La résolution doit se faire **par rebasage**,
jamais en éditant une branche sœur à la main.

`hotspot: Game/Network/Packets/Enums/GamePackets.cs` et
`hotspot: Game/Network/Clients/GameClient.cs` restent les deux fichiers les plus disputés du dépôt
(§5.8) ; ce lot y ajoute la quinzième insertion.

**Invariant « aucun membre de `GamePackets` n'atteint le `switch` final »**, refait après le lot :
l'énumération compte **141** membres, **42** ne sont référencés ni dans `GameClient.cs` ni dans
`GameActions.cs`, et ces 42 sont **tous** des `TM_SC_*` (aucun `TM_CS_*`, donc aucun id reçu). 801 est
référencé **7** fois dans `GameClient.cs` (commentaires compris).

```
for n in $(grep -oE '^\s+TM_[A-Z0-9_]+' Game/Network/Packets/Enums/GamePackets.cs | tr -d ' ')
do grep -q "GamePackets\.$n" Game/Network/Clients/GameClient.cs \
     Game/Network/Clients/Actions/GameActions.cs || echo "ABSENT: $n"; done
→ 42 lignes ABSENT, toutes TM_SC_*
```

### 11.8 Réserves

1. **`header.Length == 7` exigé (§5.2, §7 point 5)** : la fiche recommandait « journaliser en
   `Warning` et ignorer la trame », c'est ce qui est livré — le refus est dans
   `TryReadTurnOffPkMode`, donc une trame rembourrée n'est jamais appliquée. C'est un **choix**,
   explicitement laissé à Killian (§9 point 3) : revenir à « accepter toute longueur » est une ligne,
   et le comportement observable d'une trame anormale resterait un `Warning`.
2. **`SendActorStatus()` dupliquée jusqu'à la fusion de 800** : sur la base de cette branche, la
   méthode publique recommandée par §5.6 n'existe pas encore (elle appartient au lot 800), donc 801
   l'introduit avec le **même nom, la même signature et le même corps** — c'est la recommandation de
   §9 point 1 (« fusionner 800 d'abord, puis 801 réutilise `GameClient.SendActorStatus()` ») rendue
   inoffensive quel que soit l'ordre de fusion : au rebasage, deux insertions identiques se réduisent à
   une seule copie, et il ne reste alors qu'un site qui compose le masque. Tant que 800 n'est pas
   fusionné, `master` + ce lot comptent deux compositions du masque, **octet pour octet identiques**.
   La troisième option de §9 point 1 (aucun membre public, masque composé *inline* dans le bras 801)
   n'a **pas** été retenue : c'est elle, et elle seule, qui laisserait deux compositions divergentes
   après la fusion (§11.5).
3. **Aucune restriction de zone, de moral, de niveau ou de ville** : §7 point 1 n'est pas tranché et le
   paquet ne devine rien. Si Killian veut gater, le point d'insertion est `HandleTurnOffPkMode`
   (`GameClient.cs:450`), avant la mutation.
4. **Le niveau de journal de la trame valide est `Debug`** : une ligne par extinction manuelle, pas de
   trace persistante. Un niveau supérieur est un changement d'une ligne.
5. **`GmCommandService.cs` est volontairement inchangé** (§5.6) : ce n'est pas un oubli, c'est pour ne
   pas créer un conflit avec le lot 800 qui supprime sa copie privée. Après la fusion des deux lots,
   les cinq appels de `/sitdown`, `/standup`, `/battle`, `/walk` et `/pk` passent par
   `SendActorStatus()` comme le prévoit le lot 800.
6. **§7 point 9 (correction de la fiche 800) n'est pas appliquée ici** : elle appartient au lot 800 ou
   à Killian au rebasage, et ne change rien au traitement de 801.
7. **La visibilité par un tiers reste absente** (§7 point 8) : le mode PK n'est publié qu'au client de
   l'acteur, comme pour 800.

### 11.9 Bloc `CLAUDE.md` prêt à coller (le §10, avec la réserve du §5.6 précisée)

Texte à coller dans `CLAUDE.md` (le dev n'écrit pas ce fichier : Hermes le protège). Il reprend le
§10, en précisant que tant que le lot 800 n'est pas fusionné, `GmCommandService` en garde une copie
privée identique :

```markdown
### Mode PK — les paquets 800 et 801

`TM_CS_TURN_ON_PK_MODE (800)` et `TM_CS_TURN_OFF_PK_MODE (801)` sont **des trames à en-tête seul**
(7 octets, corps vide, ids `0x320`/`0x321`), construites par le client
(`SFrame.exe+0x684bb0` et `+0x684c00`) — de la même famille que les trames à en-tête seul déjà
nommées en 23/25/27. Elles n'ont **aucune réponse serveur** : l'état PK circule dans le **bit 11 du
masque de statut** de `TM_SC_STATUS_CHANGE (500)`, avec `ActorStatus.ForPlayer`, qui est un
instantané complet (bit 11 = `CreatureStatus.PlayerPkOn`). C'est ce bit que le client teste
(`SFrame.exe+0x68b006`) pour choisir entre 800 et 801 : publier un masque faux fait osciller le
client entre les deux paquets. Une longueur autre que 7 n'est pas une variante : elle est
journalisée en `Warning` et ignorée, sans rien lire après l'en-tête.

Gating : rzu renomme les deux ids en 1800/1801 **à partir d'`EPIC_9_6_3`** — pour 7.3, ce sont 800
et 801. Traitement attendu : muter `ConnectionInfo.PkMode` puis republier le masque, exactement ce
que fait la commande `/pk` — quand les deux lots sont fusionnés, les deux passent par
`GameClient.SendActorStatus()`, seul site qui compose le masque du joueur (tant que 800 n'est pas
fusionné, `GmCommandService.SendStatus` en garde une copie privée, identique octet pour octet). Le
chemin « extinction » du client ne consulte **aucun** verrou de zone : le serveur n'a rien à y
reproduire. NGemity ne traite ni l'un ni l'autre paquet et n'appelle jamais son `SetPKOn`/`SetPKOff` :
il n'y a rien à y porter.

Détail complet : `docs/packet-specs/800-turn-on-pk-mode.md` et
`docs/packet-specs/801-turn-off-pk-mode.md`.
```

### 11.10 Commandes relevées

```
git branch --show-current                  → hermes/packet-801-turn-off-pk-mode
git status --porcelain                     → vide avant le lot
git log --oneline origin/master..master    → aucune ligne
export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
dotnet build Navislamia.sln -c Debug --no-incremental
                                           → code 0, 0 Error(s), 164 avertissements (hors lot)
dotnet test Tests/Tests.csproj             → code 0, 1317 réussis / 1317, 0 échec, 0 ignoré
                                             (base §5.7 : 1302 / 1302 sur b56967a)
```

`dotnet ef` reste absent du conteneur : aucun schéma n'est touché par ce paquet (rien à migrer).

### 11.11 Preuve que les tests mordent (critère 3 : « une assertion fausse doit échouer »)

Deux mutations **transitoires** ont été portées sur le code de production, mesurées, puis annulées
(`git checkout -- <fichier>`, arbre de travail revérifié propre avant et après) :

1. `GamePackets.cs` : `TM_CS_TURN_OFF_PK_MODE = 801` remplacé par `= 1801` →
   `dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~TurnOffPkMode"` → **code 1**,
   `Failed: 2, Passed: 13` :
   ```
   Failed ClientFrame_IsSevenBytes_WithLengthAtZeroIdAtFourAndChecksumAtSix
     Expected BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) to be 801us because ID sits
     at offset 4 and reads 0x321, but found 1801us (difference of 1000).
   Failed Ids_AreTheEpic73Ones
     Expected ((ushort)GamePackets.TM_CS_TURN_OFF_PK_MODE) to be 801us, but found 1801us (difference of 1000).
   ```
   L'assertion d'offset sur l'`ID` (octets 4-5) mord donc bien. À noter : les tests de la vraie boucle
   restent verts sous cette mutation, parce que la trame de test est construite **depuis le membre
   d'énumération** — c'est voulu (le test suit l'enum, il ne fige pas une constante dupliquée), et
   c'est la paire `Ids_AreTheEpic73Ones` / `…AtSix` qui fige la valeur 801.
2. `GameActionPackets.cs` : `TryReadTurnOffPkMode` passé de `packet.Length == 7` à `>= 7` (la règle
   de longueur stricte retirée) → **code 1**, `Failed: 4, Passed: 11` :
   ```
   Failed TryReadTurnOffPkMode_RefusesAPaddedFrame   → Expected …BeFalse, but found True
   Failed TryReadTurnOffPkMode_RefusesALongerFrame   → Expected …BeFalse, but found True
   Failed OnDataReceived_ConsumesAPaddedFrameWithoutTurningTheModeOff
     Expected info.PkMode to be true because a frame of another length is refused before anything is
     applied, but found False.
   Failed OnDataReceived_ConsumesALongerFrameWithoutTurningTheModeOff  (même message)
   ```
   Les tests de taille totale et de cas de bord (8 et 15 octets) mordent donc eux aussi, au niveau du
   lecteur **et** de la vraie boucle de réception.

Après annulation des deux mutations : arbre propre (`git status --porcelain` vide) et
`TurnOffPkMode` de nouveau **15 réussis / 15** en code 0.
