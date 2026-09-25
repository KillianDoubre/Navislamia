# 800 — `TM_CS_TURN_ON_PK_MODE` (et son jumeau 801 `TM_CS_TURN_OFF_PK_MODE`)

Fiche d'archéologie de protocole, Epic 7.3. Écrite en **lecture seule** sur les références
(`reference/rzu`, `reference/ngemity/Chihiro`, `reference/client73`) : aucun Lua, aucun script et
aucun exécutable du client n'a été lancé. Les adresses `SFrame.exe+0x…` sont lues par désassemblage
statique (`objdump -d`) et par lecture d'octets du binaire
(`sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 octets).

Le client 7.3 tranche : il **construit lui-même** les deux trames, à 7 octets, ids `0x320` et
`0x321` (§3.1, §3.2), et c'est son tableau d'annotation qui associe le nom au numéro (§1). rzu
tranche l'id et son gating (§4) ; NGemity ne tranche rien du traitement, car il ne traite pas ces
paquets (§6).

Résumé des arbitrages :

| Question | Verdict de cette fiche |
|---|---|
| 7.3 possède-t-il 800 ? | **Oui** : le client a un constructeur dédié, `SFrame.exe+0x684bb0`, qui écrit `Length = 7` et `ID = 0x320` (§3.1), et une entrée d'annotation `TM_CS_TURN_ON_PK_MODE` pour `0x320` (§1). |
| Quel gating de version ? | **Un seul, et il porte sur l'id** : `X(800, version < EPIC_9_6_3)`. Pour 7.3 (`0x070300 < 0x090603`) c'est **800**, pas 1800 (§4). |
| Taille sur le fil ? | **7 octets, corps vide** : c'est une trame « en-tête seul », comme les 23, 25 et 27 nommées par `CLAUDE.md:51-56` (§3.3). |
| Le serveur doit-il répondre ? | **Non.** Aucun paquet serveur PK n'existe dans les trois références (`find -iname '*PK*'` ne rend que les deux en-têtes CS). L'état PK voyage dans le **bit 11 du masque de statut** de 500 (§5.2). |
| Que fait NGemity de 800 ? | **Rien** : il déclare l'en-tête, l'énumère, l'inclut, et `grep -rn 'TS_CS_TURN_' Chihiro/src/` ne rend aucun handler (§6). |
| Piège principal ? | **L'id seul**, sans corps : le point de rupture est le dispatch (`GamePackets` déclaré mais aucun bras ⇒ `throw Unknown Packet Type`), pas le format (§5.1). |

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id client → serveur | **800** | `op_codes.md:182` |
| Nom | `TM_CS_TURN_ON_PK_MODE` | `op_codes.md:182` |
| Jumeau | **801**, `TM_CS_TURN_OFF_PK_MODE` | `op_codes.md:183` |
| Id dans rzu | 800 (`version < EPIC_9_6_3`), sinon 1800 | `reference/rzu/librzu/src/packets/GameClient/TS_CS_TURN_ON_PK_MODE.h:7-9` |
| Id dans NGemity | 800 (un seul, sans gating) | `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_TURN_ON_PK_MODE.h:8` ; `reference/ngemity/shared/Server/ClientPackets.h:193` ; `reference/ngemity/shared/Server/XPacket.h:170` |
| Id dans le client | `0x320` = 800 | `SFrame.exe+0x684bd2` (`mov $0x320,%edx`) ; nom associé : `+0x678526` (`mov $0x320,%eax`) juste après le `push $0xa53110` de `+0x678504` |
| Chaîne de nom du client | `TM_CS_TURN_ON_PK_MODE` en `.rdata 0xa53110` | `strings` de `SFrame.exe` ; octets lus à `.rdata 0xa53110` (`54 4d 5f 43 53 5f 54 55 52 4e 5f 4f 4e 5f 50 4b 5f 4d 4f 44 45 00`) |
| Paquet serveur → client associé | **aucun** | `find reference/rzu reference/ngemity -iname '*PK*'` → les deux en-têtes `TS_CS_TURN_{ON,OFF}_PK_MODE.h` seulement |
| État dans NavisLamia | **absent** : ni 800 ni 801 dans `GamePackets` (`grep -n 'PK' Game/Network/Packets/Enums/GamePackets.cs` → 0 résultat) ; aucun bras dans `GameClient.cs` | `Game/Network/Packets/Enums/GamePackets.cs` (231 lignes, ids de 0 à 10005) |
| État du socle PK dans NavisLamia | **livré et fusionné** (`hermes/packet-socle-mode-pk` est `--merged origin/master`) : `ConnectionInfo.PkMode`, `ActorStatus.ForPlayer`, `CreatureStatus.PlayerPkOn`, `/pk` | `socle-mode-pk.md` ; `Game/Network/Clients/ConnectionInfo.cs:100-105` ; `Game/Network/Packets/Game/ActorStatus.cs:25` ; `Game/Network/Packets/Enums/CreatureStatus.cs:47` |
| Taille | **7 octets** (en-tête seul) | §3.1 |

La chaîne du nom tombe bien dans le même paragraphe d'`.rdata` que celui décrit par
`docs/packet-specs/socle-mode-pk.md` §2.1 : `TM_CS_TURN_OFF_PK_MODE` à `0xa530f8` puis
`TM_CS_TURN_ON_PK_MODE` à `0xa53110`. L'appariement est **vérifié par le code**, pas déduit de la
contiguïté : le constructeur de table d'annotation pousse le pointeur du nom et enregistre le
numéro **dans la même séquence** — `push $0xa53110` (`+0x678504`), `mov $0x320,%eax` (`+0x678526`),
puis `push $0xa530f8` (`+0x678566`), `mov $0x321,%eax` (`+0x678588`). Aucune autre entrée ne
s'intercale entre les deux, et les entrées voisines portent 710 (`TM_SC_BOOTH_TRADE_INFO`,
`+0x6784a2`) et 900 (`TM_CS_CHANGE_LOCATION`, `+0x6785ea`).

### 1.1 Périmètre de ce lot

Le lot est **`TM_CS_TURN_ON_PK_MODE` (800) et lui seul** : sa branche, sa carte
(`https://trello.com/c/YGO5L6H7`, identifiant de suivi `navislamia:packet:800`) et son commit
d'entrée. Le jumeau **801** a sa propre carte (`1dk9il5O`) et sa propre branche : cette fiche le
**cite** partout où la paire est indissociable (le client choisit entre les deux sur le même bit,
§2 ; le gating de version est le même, §4) mais **l'implémenter est hors lot** — l'énumération 801
appartient à sa propre branche, et cette fiche ne l'écrit pas à sa place.

Base de travail : `master` = `b56967a07430422add88e0e5cdf292b41b18f6c6`, branche
`hermes/packet-800-turn-on-pk-mode` créée depuis ce commit (§8).

Ce que **le socle a déjà livré** et que cette fiche a revérifié sur `master` (aucune de ces lignes
n'est à refaire) : `ConnectionInfo.cs:100-105` (`PkMode`), `ConnectionInfo.cs:117` (« every send
passes all of them, together with `PkMode` »), `ConnectionInfo.cs:336` (remise à zéro avec la
session), `GameActions.cs:127` (chargement), `CharacterService.cs:493` (réécriture),
`CharacterEntity.cs:85` + migration `20231213221150_Version0001_TheBeginning.cs:120` (colonne),
`GmCommandService.cs:290-298` et `:325` (`/pk`), `:636` (`ActorStatus.ForPlayer`),
`ActorStatus.cs:25,31` et `CreatureStatus.cs:47` (le masque et le bit 11),
`GameCharacterPackets.cs:326` (la trame 500). Ce qui **manque** : rien d'autre que l'énumération de
800 et son bras (§5.1) — la conduite à tenir, elle, est à trancher (§9).

---

## 2. Ce que le joueur fait pour que le client envoie 800 ou 801

Trois gestes, et un seul mécanisme : **tout passe par la même paire de constructeurs**.

1. **La touche de bascule** — entrée de handler à `SFrame.exe+0x68afcb` :
   - `+0x68aff7` : appel `0x476710`, puis `+0x68affe` appel `0x6b1520` — lecture du masque de
     statut de l'acteur ;
   - `+0x68b006` : **`test $0x800,%eax`** — c'est-à-dire le bit 11, `TCS_FlagPkOn` /
     `CreatureStatus.PlayerPkOn` (rzu `TS_SC_STATUS_CHANGE.h:25` ; `CreatureStatus.cs:47`) ;
   - bit **absent** ⇒ `+0x68b035` appelle le constructeur **800** ; bit **présent** ⇒ `+0x68b00d`
     appelle le constructeur **801**. Autrement dit : c'est **l'état publié par le serveur** qui
     choisit le paquet, et une désynchronisation fait osciller le client entre les deux.
   - `+0x68afd9` (`cmp $0x2,%eax`) garde l'entrée du message de compte à rebours : le même point
     de comparaison existe à `+0x68b06b`.
2. **La commande de discussion annoncée** — `db_string.rdb` l. 4400
   (`Turn PK mode on/off. … /PKON /PKOFF`), encadrée par l'entrée de touche `Toggle PK Mode`
   (l. 111993) et `Toggle PK Mode Key` (l. 112057). Cette commande passe par le **second** point
   d'émission, fonction `SFrame.exe+0x684fa0`, qui prend un booléen en argument
   (`cmpb $0x0,0x8(%ebp)` en `+0x684fa6`) :
   - argument vrai et octet `+0x660` de l'état **nul** ⇒ `+0x684fbb` appelle le constructeur **800** ;
   - argument vrai et octet `+0x660` **non nul** ⇒ `+0x684fc2` : `xor %al,%al`, retour **sans rien
     émettre** — le client refuse localement, sans message serveur ;
   - argument faux ⇒ `+0x684fce` appelle le constructeur **801**.
3. **Le message de refus et les libellés** — `db_string.rdb` l. 75319-75320 (`smsq_pkmode_on`,
   `PK MODE will be activated in #@second@# sec(s).<br>You can cancel it by clicking on the PK
   icon.`), l. 75322 (`You are currently in an area where PK MODE cannot be activated.` — c'est la
   chaîne `smsg_pkmode_impossible`, l. 75321 dans `reference/ngemity/Database/Arcadia.sql:52290`),
   l. 75325-75326 (`smsq_pkmode_off`). Les textes de fiche de mode sont versionnés 7.3 :
   `PK Mode - Neutral … <(version:7.3)>` (`db_string.rdb` l. 149774).

### 2.1 L'octet `+0x660` : un verrou local, déjà en place

`+0x660` est l'octet que le refus de (2) et le chemin (1) consultent. Il est écrit par une fonction
qui commence à `SFrame.exe+0x6880f0` :

- `+0x6883d3` `cmp $0x1,%eax` ; `+0x6883d8` `cmp $0xa,%eax` ; `+0x6883dd` `cmp $0x3,%eax` sur un
  champ lu à `+0x1c` d'une structure passée ;
- si le champ vaut **1, 10 ou 3** ⇒ `+0x6883eb` écrit **0** dans `+0x660` (activation permise) ;
- sinon ⇒ `+0x6883e2` écrit **1** dans `+0x660` (activation refusée localement, item 2 ci-dessus).

Le champ comparé n'est pas identifié (voir §7, point 1) : la table de vérité ci-dessus est
**établie**, sa sémantique non.

Le compte à rebours que le client arme localement utilise la constante **360.0**
(`flds 0xa54330` à `SFrame.exe+0x68b077` ; octets `00 00 b4 43` à `.rdata 0xa54330`) et un
compteur lu à `+0x6cc` ; la branche voisine utilise **60.0** (`.rdata 0xa53ec0`, `flds` à
`+0x68b0d8`). La constante et le champ `+0x6cc` sont ceux de `socle-mode-pk.md` §2.1 (l. 95) et
§12 (l. 411-413) ; cette fiche **ajoute l'adresse de l'instruction de chargement** (`+0x68b077`),
que la fiche socle ne donnait pas, et l'existence de la seconde constante 60.0. La formule exacte
n'est pas établie (§7, point 2).

### 2.2 Les deux seuls points d'émission

Recherche exhaustive des appels relatifs dans tout `.text` (`e8 rel32`, cible recalculée) :

| Constructeur | Appelé depuis | Sens |
|---|---|---|
| `0x684bb0` (800) | `0x684fbb`, `0x68b035` | commande explicite « on », bascule (bit 11 absent) |
| `0x684c00` (801) | `0x684fce`, `0x68b00d` | commande explicite « off », bascule (bit 11 présent) |

`0x684fa0` et `0x68afcb` n'ont **aucun appel direct** : ce sont des entrées de handler atteintes par
table (vtable/interface). Contrôle croisé utile : `0x684b60`, le constructeur de `0x226` (550), est
appelé depuis `0x68bfb0` — ce qui recoupe `550-get-region-info.md` §2 et valide la méthode.

---

## 3. Structure sur le fil

`op_codes.md:182` nomme le paquet, rzu le déclare, le client le construit :

```c
// reference/rzu/librzu/src/packets/GameClient/TS_CS_TURN_ON_PK_MODE.h:5
#define TS_CS_TURN_ON_PK_MODE_DEF(_)          // corps vide : rzu n'écrit aucun champ
```

L'en-tête de 7 octets du dépôt (`CLAUDE.md:47-48`, `Game/Network/Packets/Header.cs:7-24`) porte
`uint Length`, `ushort ID`, `byte Checksum`, et le checksum est **la somme des six premiers octets**.

### 3.1 `TM_CS_TURN_ON_PK_MODE` (800) — 7 octets, corps vide

**Taille totale attendue : 7 octets.**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0 | `uint32` (LE) | `Length` | `0x00000007` | client : `movl $0x7,(%eax)` à `SFrame.exe+0x684bb8` (réécrit à l'identique en `+0x684bdd`) ; rzu : `TS_CS_TURN_ON_PK_MODE_DEF(_)` vide (`TS_CS_TURN_ON_PK_MODE.h:5`) |
| 4 | `uint16` (LE) | `ID` | `0x0320` = 800 | client : `mov $0x320,%edx` (`+0x684bd2`) puis `mov %dx,0x4(%eax)` (`+0x684bd7`) ; `op_codes.md:182` |
| 6 | `uint8` | `Checksum` | `0x2A` = 42 | client : boucle de somme `+0x684be3`-`+0x684bf7` (`add (%esi),%dl` de `base` à `base+6`, écriture en `+0x684bf7`) ; règle : `CLAUDE.md:47-48` |
| 7 | — | *corps* | **vide** | `TS_CS_TURN_ON_PK_MODE.h:5` (macro `_DEF` sans champ) ; aucun champ écrit après l'en-tête dans le constructeur |

Octets attendus, tels que le client les assemble :

```
07 00 00 00 20 03 2a
```

Vérification du checksum : `0x07 + 0x20 + 0x03 = 42 = 0x2A` (calcul hors client, sur la règle du
dépôt).

### 3.2 `TM_CS_TURN_OFF_PK_MODE` (801) — 7 octets, corps vide

**Taille totale attendue : 7 octets.**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0 | `uint32` (LE) | `Length` | `0x00000007` | client : `movl $0x7,(%eax)` à `SFrame.exe+0x684c08` |
| 4 | `uint16` (LE) | `ID` | `0x0321` = 801 | client : `mov $0x321,%edx` (`+0x684c22`) puis `mov %dx,0x4(%eax)` (`+0x684c27`) ; `op_codes.md:183` |
| 6 | `uint8` | `Checksum` | `0x2B` = 43 | boucle de somme du constructeur `+0x684c2d`-`+0x684c47` |
| 7 | — | *corps* | **vide** | `reference/rzu/librzu/src/packets/GameClient/TS_CS_TURN_OFF_PK_MODE.h:5` |

Octets attendus : `07 00 00 00 21 03 2b` — checksum `0x07 + 0x21 + 0x03 = 43 = 0x2B`.

### 3.3 Piège de réception : la famille des trames à en-tête seul

800 et 801 sont des trames **exactement égales à leur en-tête**. `CLAUDE.md:51-56` documente ce
piège pour 23, 25 et 27 : la boucle de réception doit comparer avec
`remainingData >= Marshal.SizeOf<Header>()` — la comparaison stricte `>` a déjà fait disparaître une
trame seule dans une lecture TCP, et `TM_CS_RETURN_LOBBY` pendait de façon non déterministe. La
boucle de ce dépôt est correcte (`GameClient.cs:1232`) et borne la longueur des deux côtés
(`GameClient.cs:1239` : rejet et déconnexion si `header.Length < HeaderLength`).

Conséquence pour le dev : **ne rien lire après l'en-tête**, et ne pas attendre d'octet de corps.
La famille compte aujourd'hui 23, 25 et 27 nommés par `CLAUDE.md:51-56` ; 26 est déclaré
(`GamePackets.cs:166`) et traité (`GameClient.cs:1761-1765`) mais sa taille côté client n'est pas
établie ici. 800 et 801 sont donc les trames à en-tête seul **suivantes** du dispatch.

---

## 4. Gating de version — tranché pour l'Epic 7.3

| Champ | Gating rzu | Source | Décision 7.3 |
|---|---|---|---|
| `ID` | `X(800, version < EPIC_9_6_3)` / `X(1800, version >= EPIC_9_6_3)` | `TS_CS_TURN_ON_PK_MODE.h:7-9` | **800** |
| `ID` de 801 | `X(801, version < EPIC_9_6_3)` / `X(1801, …)` | `TS_CS_TURN_OFF_PK_MODE.h:7-9` | **801** |
| `Length`, `Checksum` | aucun gating | `TS_CS_TURN_ON_PK_MODE_DEF(_)` vide, `TS_CS_TURN_ON_PK_MODE.h:5` | inchangés : 7 / somme des six premiers octets |
| corps | aucun champ ⇒ aucun gating possible | `TS_CS_TURN_ON_PK_MODE.h:5` | néant |

Justification : `EPIC_7_3 = 0x070300` (`reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59`) est
strictement inférieur à `EPIC_9_6_3 = 0x090603`
(`PacketEpics.h:96`, commentaire : *« GS packet ID modified with version 20200713 »*). Le 7.3 du
dépôt est donc du côté `version < EPIC_9_6_3`.

Le client tranche dans le même sens et à un troisième niveau : son tableau d'annotation associe
`TM_CS_TURN_ON_PK_MODE` à `0x320` (800) et `TM_CS_TURN_OFF_PK_MODE` à `0x321` (801) — pas à
`0x708`/`0x709` (1800/1801) — `SFrame.exe+0x678526` et `+0x678588` (§1). NGemity, compilé en
`EPIC_4_1_1`, ne connaît qu'un id, 800 (`TS_CS_TURN_ON_PK_MODE.h:8`) : même verdict, sans gating.

**Cette fiche ne laisse donc aucun champ dont le gating n'aurait pas été statué.** C'est le seul
piège de version du paquet, et il est dans l'id.

---

## 5. Traitement attendu

### 5.1 Réception : déclarer **et** armer, dans le même commit

La boucle de `GameClient.OnDataReceived` (`Game/Network/Clients/GameClient.cs:1228`) :

1. La trame est lue (`GameClient.cs:1265`), puis **un id absent de `GamePackets` est
   abandonné** : `if (!DefinedPackets[header.ID])` → journal `Debug` « Undefined packet ID » et
   `continue` (`GameClient.cs:1269-1273`). Autrement dit, **aujourd'hui le 800 et le 801 du client
   sont consommés et ignorés en silence** — le mode PK n'est jamais actionné par le protocole, et
   seul `/pk` le fait.
2. Le checksum est déjà vérifié (`GameClient.cs:1235`) et un checksum faux déconnecte
   (`GameClient.cs:1256-1262`) : rien à refaire.
3. Déclarer un membre **sans lui donner de bras** est en revanche pire que de ne rien faire : le
   `switch` final (`GameClient.cs:1854-1866`) se termine par
   `_ => throw new Exception($"Unknown Packet Type {header.ID}")` (`GameClient.cs:1865`), et une
   exception dans la boucle de réception casse la session. Le commentaire du fichier le dit déjà
   pour d'autres ids : *« It must stay before the throwing switch below: a member of GamePackets
   that reaches it breaks the receive loop. »* (`GameClient.cs:1836-1838`). C'est exactement le
   critère d'acceptation transversal n° 4 : **enum et dispatch dans le même commit**.
4. L'emplacement du membre dans l'énumération : `GamePackets.cs` est ordonné par id ;
   `TM_CS_TURN_ON_PK_MODE = 800` va entre `TM_CS_DROP_QUEST = 603` (`GamePackets.cs:141`) et
   `TM_CS_CHANGE_LOCATION = 900` (`GamePackets.cs:142`) — même voisinage que celui du tableau
   d'annotation du client (§1), qui enchaîne 710, 800, 801 puis 900. **Ce lot n'écrit que 800** :
   `TM_CS_TURN_OFF_PK_MODE = 801` sera déclaré par sa propre branche (§1.1), et le laisser dehors
   ne coûte rien — un id non déclaré est journalisé puis ignoré (`GameClient.cs:1269-1273`).
5. La forme du bras est **déjà écrite** dans le fichier pour les trames à en-tête seul :
   `GameClient.cs:1745-1771` (`TM_CS_REQUEST_RETURN_LOBBY`, `TM_CS_RETURN_LOBBY`,
   `TM_CS_REQUEST_LOGOUT`, `TM_CS_LOGOUT`) — `if (header.ID == (ushort)GamePackets.X) { log; action;
   continue; }`, aucun octet de corps lu. Le bras de 800 se pose au même endroit, **avant** le
   `switch` final.

### 5.2 Ce que le serveur doit répondre

**Rien.** Aucun paquet `TM_SC_*` PK n'existe : `find reference/rzu reference/ngemity -iname '*PK*'`
ne rend que les deux en-têtes `CS`, et NSL n'a rien à inventer. L'état PK ne circule que par le
**bit 11 du masque de statut** :

| Élément | Valeur | Source |
|---|---|---|
| Bit PK | `1 << 11` (`0x800`) | `Game/Network/Packets/Enums/CreatureStatus.cs:47` ; rzu `TS_SC_STATUS_CHANGE.h:25` (`TCS_FlagPkOn = 1 << 11`) ; c'est aussi le masque testé par le client à `SFrame.exe+0x68b006` |
| Composition du masque joueur | `ActorStatus.ForPlayer(pkModeOn, sitting, battleMode, walking)` — **instantané complet**, jamais un delta | `Game/Network/Packets/Game/ActorStatus.cs:25,31` ; `ConnectionInfo.cs:113-118` |
| Trame qui le publie | `TM_SC_STATUS_CHANGE` (500), 15 octets, handle à 7, masque à 11 | `Game/Network/Packets/Game/GameCharacterPackets.cs:326` ; `PkModeStatusTests.StatusChangeFrame_IsFifteenBytes_WithHandleAtSevenAndStatusAtEleven` |
| Émission vers le client | `BuildStatusChange(info.CharacterHandle, ActorStatus.ForPlayer(info.PkMode, info.IsSitting, info.IsBattleMode, info.IsWalking))` | `GmCommandService.cs:632-637` (`SendStatus`) ; aussi à l'entrée en monde `Actions/GameActions.cs:278` et à l'ouverture de séance `Actions/GameActions.cs:187` |

Le geste attendu est **exactement celui de la commande `/pk` déjà livrée** par
`hermes/packet-socle-mode-pk` : `Game/Services/GmCommands/GmCommandService.cs:289-300`
(`case GmCommand.Pk`: bascule, `info.PkMode = pk`, `SendStatus(client)`, réponse texte au joueur),
déclarée en `GmCommandCatalog.cs:29` et `:80`, documentée en `docs/gm-commands.md:54` et `:114`
(*« `/pk` : `ConnectionInfo.PkMode` et le masque de statut, exactement ce que feront 800/801 »*).
Le dev ne réinvente donc pas la logique : il la **branche sur le protocole**. Comme `SendStatus`
est `private static` dans `GmCommandService.cs:632`, il faut soit l'extraire dans un helper partagé,
soit émettre la même paire `ActorStatus.ForPlayer` + `BuildStatusChange` — les deux sites doivent
publier **le même masque** (contrainte déjà couverte par
`PkModeStatusTests.BothSendSites_PublishTheSameMask`).

### 5.3 Persistance

`ConnectionInfo.PkMode` est chargé depuis `Characters.PkMode` à l'entrée en monde et réécrit à la
déconnexion : `Game/Network/Clients/Actions/GameActions.cs:127` (`info.PkMode = character.PkMode`),
`GameClient.cs:771-773` → `CharacterService.SaveProgressAsync(…, info.PkMode)`
(`Game/Services/CharacterService.cs:462,493`), colonne `CharacterEntity.PkMode`
(`Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:85` ; migration
`20231213221150_Version0001_TheBeginning.cs:120`). Le paquet 800/801 n'a donc **rien** à persister
lui-même : il n'a qu'à muter `info.PkMode`. Un redémarrage du serveur ne peut pas perdre le mode
autrement qu'en perdant la sauvegarde de session, qui est déjà en place.

### 5.4 Ce que cette branche ne peut pas faire

Le dépôt ne diffuse **pas** les autres joueurs à un client : `TS_SC_ENTER_PLAYER` n'est construit
que pour le client qui entre (`Actions/GameActions.cs:178,210`) et `ConnectionInfo` ne connaît que
ses monstres (`ConnectionInfo.cs:160`, `SpawnedMonsters`). Le masque de statut ne peut donc
aujourd'hui atteindre que **le client de l'acteur lui-même**, ce qui suffit à faire fonctionner la
bascule du client (800/801) mais ne rend pas le PK visible par un tiers. Aucune diffusion
supplémentaire ne doit être inventée ici : c'est le sous-ensemble B du socle PK
(`docs/packet-specs/socle-mode-pk.md` §7), non fusionné.

### 5.5 Tests exigés (critères transversaux)

1. `dotnet build Navislamia.sln -c Debug` code 0.
2. `dotnet test Tests/Tests.csproj` code 0, **au moins 366 tests** — l'état de `master` avant cette
   branche est de **1302 tests passés, 0 échec** (mesuré sur `b56967a`, `dotnet test
   Tests/Tests.csproj`, `Passed! - Failed: 0, Passed: 1302, Skipped: 0, Total: 1302`).
3. **Test d'offsets** pour la nouvelle trame : elle compte **7 octets**, `Length` en 0-3 (`= 7`),
   `ID` en 4-5 (`= 800`), `Checksum` en 6, corps vide ; le lecteur **accepte 7** et **refuse** 6,
   ainsi que le cas de bord d'une trame de **8 octets et plus** (le producteur unique en écrit
   toujours 7, §3.1). Le test des trames à en-tête seul existe déjà comme modèle
   (`Tests/Game/ActionPacketsTests.cs:262` et suivants).
4. Enum et dispatch modifiés ensemble : aucun membre de `GamePackets` ne peut atteindre le `throw`
   final (`GameClient.cs:1865`).
5. `git log --oneline origin/master..master` vide (aucun commit sur `master` locale).

### 5.6 Point de collision à signaler

Le PO relève à ce réveil **17 MR ouvertes** — #1, #6, #13, #21, #22, #23, #24, #26, #27, #33, #38,
#40, #45, #46, #47, #48, #49 — et signale que « beaucoup touchent `GamePackets.cs` et
`GameClient.cs` » (carte `YGO5L6H7`, §« État de la base »). Les numéros de MR ne sont pas lisibles
depuis ce poste (aucun accès GitHub) : la mesure ci-dessous est faite sur les **branches distantes
non fusionnées** (`git branch -r --no-merged origin/master`), qui sont la contrepartie locale des MR
ouvertes — une branche `hermes/packet-*` par MR, c'est la convention du dépôt. Diff **trois-points**
contre `origin/master` (`git diff --name-only origin/master...<branche>`) :

- **17 branches non fusionnées**, dont **13 touchent `GamePackets.cs` et/ou `GameClient.cs`** :
  `hermes/packet-223-swap-equip` (8 commits d'avance), `packet-10000-open-item-shop`,
  `packet-212-storage`, `packet-214-puton-card`, `packet-215-putoff-card`,
  `packet-221-hide-equip-info`, `packet-258-donate-item`, `packet-259-donate-reward`,
  `packet-260-soulstone-craft` (n'affecte que `GameClient.cs`), `packet-262-repair-soulstone`,
  `packet-263-transmit-ethereal-durability`, `packet-264-…-to-equipment`, `packet-281-puton-item-set`,
  `packet-284-bind-skillcard`, `packet-285-unbind-skillcard`, `packet-323-change-summon-name`,
  `packet-4003-huntaholic-create-instance`.

Le patch de cette branche est minuscule (un membre d'énumération, un bras `if`), mais il tombe
sur les deux fichiers les plus disputés du dépôt : **`hotspot: Game/Network/Packets/Enums/GamePackets.cs`
et `hotspot: Game/Network/Clients/GameClient.cs`** — la fusion se fera par rebasage, jamais en
touchant à la main une autre branche.

---

## 6. Écarts assumés avec NGemity

| Point | NGemity (`38ceb2c`) | NavisLamia : ce que cette branche décide | Écart assumé ? |
|---|---|---|---|
| Traitement de 800 | Déclaré (`shared/Server/Packets/GameClient/TS_CS_TURN_ON_PK_MODE.h:8`), énuméré (`ClientPackets.h:193`), inclus (`XPacket.h:170`) — et **jamais traité** : `grep -rn 'TS_CS_TURN_' Chihiro/src/` ne rend aucun handler | Traité : mutation de `ConnectionInfo.PkMode` + publication du masque de statut | **Oui, et c'est le but.** NGemity ne construit rien à imiter : il ignore le paquet. NavisLamia s'appuie sur le socle PK qu'elle a déjà |
| État PK interne | `Player.h:265-267` : `IsPKOn()`, `SetPKOn()`, `SetPKOff()` — **aucun appel** à `SetPKOn`/`SetPKOff` dans `Chihiro/src/` ; `m_bIsPK` (`Player.h:437`) n'est lu qu'en `Player.cpp:3298` et `:3303` (règle de commerce, non finalisée) | `ConnectionInfo.PkMode`, une propriété dont tout le cycle de vie est établi (chargement, publication, sauvegarde) | Non : NGemity n'offre pas d'alternative, seulement un squelette mort |
| Persistance | Colonne `pkmode` (`Database/Telecaster.sql:151`) lue en `Player.cpp:208` (`SetInt32Value(PLAYER_FIELD_PK_MODE, (*result)[57].GetInt8())` ; champ `Object.h:123`) — **jamais réécrite** | Sauvegardée à la déconnexion (`GameClient.cs:771-773`) | Non, NavisLamia est plus complète ; l'écart est en faveur de NavisLamia |
| Gating d'id | Un seul id, 800, en `EPIC_4_1_1` (`CREATE_PACKET(TS_CS_TURN_ON_PK_MODE, 800)`) | 800 / 801, gating rzu rebasé sur 7.3 | Non : même id pour 7.3 |
| Message au joueur | `smsq_pkmode_on` / `smsg_pkmode_impossible` présents en base Arcadia (`Arcadia.sql:52289-52292`) mais jamais émis | Aucun message n'est émis par 800/801 : l'écho texte n'existe que pour `/pk` (`GmCommandService.cs:300`) | **Oui** : côté client, ces libellés sont affichés localement par le client lui-même (§2) ; le serveur n'a pas de canal pour les déclencher |
| Règle de jeu (zone, moral, minuteurs) | Aucune | **Aucune** : le mode est un état de session publié, sans restriction de gameplay | **Oui — et c'est la réserve principale**, §7 point 1 et §9 |

---

## 7. NON ÉTABLI

1. **Le verrou de zone.** L'octet `+0x660` qui décide du refus local (§2.1) est écrit d'après un
   champ lu à `[esi+0x1c]` comparé à **1, 3 ou 10** (`SFrame.exe+0x6883d3/0x6883d8/0x6883dd`) :
   la table de vérité est certaine, **la nature du champ ne l'est pas** (type de carte ? id de
   champ/zône ? état moral ?). Aucune référence ne le nomme : ni rzu ni NGemity n'ont de pendant.
   Question précise : *que représente la valeur 1, 3 ou 10 qui autorise le mode PK dans le client
   7.3, et le serveur doit-il en tenir une copie ?* Tant que ce n'est pas tranché, le serveur
   **ne refuse pas** 800.
2. **Le compte à rebours.** `smsq_pkmode_on` annonce `#@second@#` secondes ; le client charge
   **360.0** (`flds 0xa54330` à `+0x68b077`) et un compteur `+0x6cc`, et **60.0** (`.rdata
   0xa53ec0`) dans la branche voisine (`+0x68b0d8`). La relation exacte entre ces constantes et
   l'affichage `#@second@#` n'est pas établie, et cette fiche **n'en propose aucune**. Aucune
   conséquence serveur : le décompte est purement local au client.
3. **`/PKON` rejoint-il le même handler ?** `db_string.rdb` l. 4400 annonce `/PKON /PKOFF`, mais
   `db_localcommand.rdb` ne contient **qu'une** entrée, dont les jetons sont `PK` puis `pkoff`
   (`strings -n 3`, lignes 72-73 du fichier de chaînes), et **aucun jeton `pkon`**. Lecture par
   chaînes seulement — aucune exécution. Le chemin « bascule » (§2, point 1) et le chemin
   « commande explicite » (§2, point 2) sont donc tous deux établis, mais leur rattachement aux mots
   `/PKON` et `/PKOFF` ne l'est qu'à moitié.
4. **Faut-il refuser 800 côté serveur, et avec quel code ?** Le client n'attend aucun paquet de
   réponse et n'affiche rien sur un 800 refusé : il n'existe **aucun** moyen établi de signifier un
   refus. `ResultCode.PKLimit = 29` existe déjà (`Game/Network/Packets/ResultCode.cs:38`) et
   `SendResult(ushort id, ushort result, int value = 0)` est disponible
   (`GameClient.cs:68`), mais rien n'établit que le client 7.3 affiche un `TM_SC_RESULT` portant
   l'id 800. **Choix proposé : aucune réponse**, conforme aux trois références.
5. **Faut-il vérifier `header.Length == 7` ?** Les trames à en-tête seul du dépôt (23/25/27) ne le
   font pas (`GameClient.cs:1745-1771`). Le producteur unique côté client écrit toujours 7
   (§3.1-3.2) : une longueur différente est une anomalie de protocole. Recommandation : journaliser
   en `Warning` et ignorer la trame, sans jamais lire de corps — mais c'est une décision, laissée
   à Killian (§9).
6. **Le mode PK survit-il à la mort ?** Aucun code de `master` ne touche `PkMode` au combat ni à la
   résurrection (`grep -rn PkMode Game/Services/CombatService.cs Game/Services/ResurrectionService.cs`
   → 0 résultat), et aucune référence ne tranche la règle. Le client, lui, ne fait que refléter le
   masque reçu. Question ouverte, **hors périmètre de ce paquet** : elle appartient aux règles de
   mort/respawn.
7. **La visibilité par un tiers.** Voir §5.4 : impossible à vérifier ici, faute de sous-socle de
   visibilité fusionné. Le paquet est correct du point de vue du client concerné.

---

## 8. Commits épinglés

| Référence | Commit | Contenu utilisé |
|---|---|---|
| `reference/rzu` | **`87c1e83b`** | `librzu/src/packets/GameClient/TS_CS_TURN_ON_PK_MODE.h`, `…TS_CS_TURN_OFF_PK_MODE.h`, `librzu/src/lib/Packet/PacketEpics.h:59,96`, `librzu/src/packets/GameClient/TS_SC_STATUS_CHANGE.h:25` |
| `reference/ngemity` | **`38ceb2c`** | `shared/Server/Packets/GameClient/TS_CS_TURN_ON_PK_MODE.h`, `…OFF….h`, `shared/Server/ClientPackets.h:193-194`, `shared/Server/XPacket.h:169-170`, `Chihiro/src/Entities/Player/Player.h:265-267,437`, `Chihiro/src/Entities/Player/Player.cpp:208,3298,3303`, `Chihiro/src/Entities/Object/Object.h:123`, `Database/Telecaster.sql:151`, `Database/Arcadia.sql:52289-52292` |
| `Navislamia` | **`b56967a07430422add88e0e5cdf292b41b18f6c6`** | `master` au moment de l'écriture (fusion de `hermes/fix-devconsole-compilation`) ; branche `hermes/packet-800-turn-on-pk-mode` créée depuis ce commit |
| `reference/client73` | `SFrame.exe` **`41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`** (9 841 664 octets) | désassemblage `objdump -d` : `+0x684bb0`, `+0x684c00`, `+0x684fa0`, `+0x68afcb`, `+0x6880f0`, `+0x678504/0x678526`, `+0x678566/0x678588` ; `.rdata 0xa53110` ; `.rdata 0xa54330` = 360.0f ; `SFrame.strings` ; `db_string.rdb` (`strings -n 5`, 254 061 lignes) ; `db_localcommand.rdb` |

---

## 9. A VERIFIER PAR KILLIAN

1. **Aucune réponse serveur** : confirmer que 800/801 restent muets (aucun `TM_SC_*` PK n'existe,
   §5.2) — ou dire quel code de résultat renvoyer en cas de refus (§7, point 4).
2. **Refus serveur** : doit-on refuser 800 dans certaines conditions (ville, moral, niveau,
   guilde) ? Le client, lui, refuse localement d'après un champ non identifié (§2.1, §7 point 1).
   En l'absence d'arbitrage, cette branche **n'invente aucune restriction**.
3. **Longueur stricte** : faut-il exiger `header.Length == 7` avant d'appliquer (§7, point 5) ?
4. **Test d'offsets** : nom du fichier de tests — proposition `Tests/Game/TurnOnPkModePacketsTests.cs`
   (7 octets, `Length` 0-3 = 7, `ID` 4-5 = 800, `Checksum` 6, refus de 6 et de 8 et plus, masque
   publié contenant `CreatureStatus.PlayerPkOn`, `PkMode` inchangé hors session).
5. **Point chaud de fusion** : `GamePackets.cs` et `GameClient.cs` sont touchés par 13 branches
   non fusionnées (§5.6) ; l'ordre de fusion et le rebasage sont à arbitrer.
6. **Bloc `CLAUDE.md`** : le bloc §10 est à coller par Killian (Hermes protège ce fichier ; le dev
   ne doit pas l'écrire).

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
et 801. Traitement attendu : muter `ConnectionInfo.PkMode` et republier le masque, exactement ce que
fait la commande `/pk` — les deux passent désormais par `GameClient.SendActorStatus()`, seul site qui
compose le masque du joueur. NGemity ne traite ni l'un ni l'autre paquet et n'appelle jamais son
`SetPKOn` : il n'y a rien à y porter.

Détail complet : `docs/packet-specs/800-turn-on-pk-mode.md`.
```

---

## 11. Implémentation livrée (dev)

Section ajoutée par `navis-dev` le 25/09/2026 ; l'analyse de l'archéologue (§1 à §10) est laissée
intacte. La branche `hermes/packet-800-turn-on-pk-mode` a été créée par `navis-ref` depuis `master`
(`b56967a`), qui y a commité la fiche (`a62dab3`). Commits du lot : `64d6e18` (code, tests d'offsets et
dispatch, 5 fichiers, +304/−13), `983ebd0` et `b968375` (deux tests ajoutés après mesure exhaustive) ;
cette section est livrée par le commit `docs(packet-800): ce que le lot dev livre et ce qu'il laisse
ouvert`.

### 11.1 Checklist des critères transversaux, avec les codes de sortie relevés

| # | Critère | État | Mesure |
|---|---|---|---|
| 1 | `dotnet build Navislamia.sln -c Debug` code 0 | **OK** | code **0**, `0 Error(s)`, 164 avertissements — aucun ne cite un fichier du lot (vérifié par `grep -i` sur les quatre fichiers livrés) |
| 2 | `dotnet test Tests/Tests.csproj` code 0, compte jamais en baisse | **OK** | base de la fiche (§5.5) : **1302** réussis / 1302. Après le lot : code **0**, **1316** réussis / 1316, 0 échec, 0 ignoré → **+14** |
| 3 | Au moins un test d'offsets (taille totale et position de chaque champ) | **OK** | `Tests/Game/TurnOnPkModePacketsTests.cs` (§11.3) : `ClientFrame_IsSevenBytes_WithLengthAtZeroIdAtFourAndChecksumAtSix`, `TryReadTurnOnPkMode_AcceptsTheSevenByteFrame`, `…RefusesEveryOtherLength` (6, 8, 15), plus l'exercice de la vraie boucle sur 8 et 15 ; la **preuve que les tests mordent** est en §11.11 |
| 4 | Enum et dispatch modifiés ensemble | **OK** | membre `TM_CS_TURN_ON_PK_MODE = 800` (`GamePackets.cs:149`) **et** bras `if (header.ID == …)` en `GameClient.cs:1906`, **avant** le `switch` de `GameClient.cs:1912` qui lève `Unknown Packet Type` (balayage exhaustif en §11.7) |
| 5 | Savoir durable dans la fiche commitée + bloc `CLAUDE.md` dans la description de la MR | **OK côté fiche** | cette section ; le bloc §10 est corrigé en §11.9 (le dev n'écrit pas `CLAUDE.md`, Hermes protège ce fichier) |
| 6 | Version est tranchée | **OK** | **800 déclaré, 1800 non déclaré** (`Ids_AreTheEpic73Ones` vérifie `Enum.IsDefined(1800) == false`) ; 801 reste non déclaré et appartient à sa propre branche |
| 7 | Aucun commit sur `master` locale | **OK** | `git log --oneline origin/master..master` → aucune ligne (relevé §11.10) |
| 8 | Aucun champ `NON ÉTABLI` deviné | **OK** | aucune restriction de zone, aucune réponse, aucun code de refus, aucune règle de mort, aucune diffusion à un tiers : §11.6 point par point |

### 11.2 Fichiers livrés

| Fichier | Modification |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_TURN_ON_PK_MODE = 800` (+9 lignes, commentaire de gating inclus), inséré entre `TM_CS_DROP_QUEST = 603` et `TM_CS_CHANGE_LOCATION = 900` — aucun lot frère n'insère dans cet intervalle |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `TurnOnPkModeLength` (7) et `TryReadTurnOnPkMode(ReadOnlySpan<byte>)` (+20) |
| `Game/Network/Clients/GameClient.cs` | `SendActorStatus()` (`:111`), `HandleTurnOnPkMode(byte[])` (`:448`), bras de dispatch (`:1906`) (+58) |
| `Game/Services/GmCommands/GmCommandService.cs` | `SendStatus` privé supprimé ; ses 5 appels (`/sitdown`, `/standup`, `/battle`, `/walk`, `/pk`) passent par `client.SendActorStatus()` ; commentaire du `case Pk` mis à jour |
| `Tests/Game/TurnOnPkModePacketsTests.cs` | **nouveau**, 14 tests |

Aucun constructeur de trame descendante n'est ajouté : la fiche établit qu'il n'existe **aucune** trame
serveur → client de cette famille (§5.2) — `GameCharacterPackets.cs` est inchangé.

### 11.3 Offsets livrés, et les tests qui les tiennent

| Offset | Taille | Champ | Valeur livrée | Test |
|---|---|---|---|---|
| 0 | 4 | `Length` `uint32` LE | **7** | `ClientFrame_IsSevenBytes_WithLengthAtZeroIdAtFourAndChecksumAtSix` |
| 4 | 2 | `ID` `uint16` LE | **800** (`0x320`), l'id du client | `…WithLengthAtZeroIdAtFourAndChecksumAtSix`, `Ids_AreTheEpic73Ones` |
| 6 | 1 | `Checksum` | somme des octets 0-5 = **0x2A** (`07 00 00 00 20 03`) | `…ChecksumAtSix` (deux assertions : boucle du dépôt **et** valeur figée) |
| 7 | 0 | — corps vide — | taille totale = `GameActionPackets.TurnOnPkModeLength` = **7** | `frame.Should().HaveCount(7)` dans le même test |

`TryReadTurnOnPkMode` **n'accepte que les 7 octets** (`packet.Length == TurnOnPkModeLength`) : un
producteur unique (§3.1) écrit toujours 7, donc 8 et plus sont des anomalies. Le refus de 6 est testé
aussi, mais il **ne peut pas venir de la boucle** : un `Length` inférieur à l'en-tête fait
`Connection.Disconnect()` avant tout dispatch (`GameClient.cs:1285`) — c'est écrit dans le commentaire du
lecteur, et c'est pour ça que le lecteur le refuse lui aussi (appel direct). Les longueurs 8 et 15 sont
en plus **exercées à travers la vraie boucle de réception** :
`OnDataReceived_ConsumesAPaddedFrameWithoutTurningTheModeOn` et `…ALongerFrameWithoutTurningTheModeOn`
(trame consommée en entier, `PkMode` inchangé, `Connection.Sent` vide).

### 11.4 Ce qui est publié : le masque, et rien d'autre

Par trame 800 acceptée, **exactement une** `TM_SC_STATUS_CHANGE` (500) est envoyée : 15 octets,
`Length` 0-3 = 15, `ID` 4-5 = 500, checksum en 6, `handle` en 7 (= `ConnectionInfo.CharacterHandle`),
masque en 11. C'est le seul envoi : `OnDataReceived_TurnsThePkModeOnAndPublishesTheStatusMaskAsTheOnlyAnswer`
assert `connection.Sent.Should().ContainSingle()`. Aucun `TM_SC_RESULT`, aucun paquet PK (§5.2, §7
point 4). Une seconde trame 800 republie le **même** masque — le paquet n'est pas une bascule
(`OnDataReceived_TurningTheModeOnTwiceKeepsTheSameMask`) : c'est bien le lecteur du bit 11 côté client
qui choisit entre 800 et 801, pas le serveur qui alterne.

Journalisation : trame valide → **`Debug`** (l'idiome du dépôt pour une trame client reçue) ; longueur
différente de 7 → **`Warning`** puis trame ignorée sans rien lire après l'en-tête — la recommandation
explicite de §7 point 5, appliquée telle quelle (§11.8 réserve 1).

### 11.5 Le masque n'a plus qu'un seul site d'envoi, et `/pk` l'utilise aussi

`GameClient.SendActorStatus()` (`GameClient.cs:111`) compose
`BuildStatusChange(info.CharacterHandle, ActorStatus.ForPlayer(info.PkMode, info.IsSitting,
info.IsBattleMode, info.IsWalking))` — l'instantané complet, exactement la paire que la fiche §5.2
imposait. `GmCommandService.SendStatus`, qui faisait la même chose en `private static`, est supprimée :
ses cinq appels (`/sitdown`, `/standup`, `/battle`, `/walk`, `/pk`) passent par la méthode publique.
Les deux sites publient donc le même masque **par construction**, et la preuve au niveau de la trame est
`Packet800AndTheGmCommandPublishTheSameFrame` : un état déjà posé (`IsWalking`) survit au masque que 800
republie, et l'appel direct de `SendActorStatus()` produit des octets identiques à la trame du paquet.
Les tests existants de `/pk` (`GmCommandServiceTests.cs:559`, `PkModeStatusTests`) passent **sans
modification**.

### 11.6 Ce qui n'est pas porté, et pourquoi

- **Aucun refus** : le verrou de zone `+0x660` n'est pas identifié (§7 point 1) et le client refuse
  déjà localement (§2.1). Aucune des trois références ne refuse côté serveur. Rien n'a été inventé
  (§9 point 2).
- **Aucune réponse, aucun code de résultat** : aucun `TM_SC_*` PK n'existe et rien n'établit que le
  client 7.3 lirait un `TM_SC_RESULT` portant l'id 800 (§7 point 4). `ResultCode.PKLimit` reste
  inutilisé.
- **Aucune condition de zone ni de monde** dans `HandleTurnOnPkMode` : le seul fait de recevoir 800
  sur une session suffit. Ce n'est pas un oubli mais l'absence de règle établie ; la conséquence est
  bornée — `PkMode` est **rechargé depuis `Characters.PkMode` à l'entrée en monde**
  (`Actions/GameActions.cs:127`) et remis à **faux** par `ClearCharacterSession`
  (`ConnectionInfo.cs:336`), donc une trame reçue hors session de personnage ne peut pas survivre à la
  session suivante.
- **Aucune écriture de persistance** : elle est déjà en place (§5.3), le paquet n'a qu'à muter l'état.
- **Aucune diffusion à un tiers** : impossible dans cette base (§5.4) ; c'est le sous-ensemble B du
  socle PK, non fusionné.
- **Aucune limitation de fréquence** : rien ne l'établit, et le client n'émet que sur son propre
  changement d'état (§2.2).
- **Le jumeau 801 reste non déclaré** : une trame 801 est consommée comme un id inconnu
  (« Undefined packet ID », `Debug`) et la connexion reste ouverte. `OnDataReceived_ConsumesTheUndeclaredTwin801WithoutThrowing`
  le pin, et il est écrit pour **rester vert** quand le lot 801 fusionnera (il n'assert rien sur ce que
  801 publiera alors).

### 11.7 Point de collision : mesure refaite, et **zéro conflit ajouté**

Mesure du 25/09/2026 sur cette branche, `git merge-tree --write-tree` (aucun index ni worktree touché),
contre les **17** branches sœurs non fusionnées de `origin/master`. Deux comptages par branche sœur :
les conflits dans `GamePackets.cs`/`GameClient.cs` contre `origin/master` seul (la base, conflits qui
**précèdent** ce lot) et contre `HEAD` (base + ce lot) :

| Branche sœur | conflits base | conflits avec le lot | ajoutés par le lot |
|---|---|---|---|
| `packet-10000-open-item-shop` | 2 | 2 | **0** |
| `packet-214-puton-card` | 1 | 1 | **0** |
| `packet-223-swap-equip` | 1 | 1 | **0** |
| `packet-258-donate-item` | 1 | 1 | **0** |
| `packet-259-donate-reward` | 1 | 1 | **0** |
| `packet-284-bind-skillcard` | 2 | 2 | **0** |
| `packet-285-unbind-skillcard` | 2 | 2 | **0** |
| `packet-4003-huntaholic-create-instance` | 1 | 1 | **0** |
| les 9 autres (`212`, `215`, `221`, `260`, `262`, `263`, `264`, `281`, `323`) | 0 | 0 | **0** |

Le lot **n'ajoute aucune zone de conflit** : les 11 conflits constatés sont ceux que chaque branche sœur
a **déjà contre la base** (`origin/master` seul, bases communes anciennes), et l'insertion de ce lot tombe
hors de leurs points d'ancrage — membre d'énumération entre 603 et 900, bras de dispatch **juste après** celui de
`TM_CS_XTRAP_CHECK` (59), handler et méthode d'envoi dans des régions disjointes. À noter pour la
fusion : `hotspot: Game/Network/Packets/Enums/GamePackets.cs` et
`hotspot: Game/Network/Clients/GameClient.cs` restent les deux fichiers les plus disputés du dépôt
(§5.6) ; le rebasage doit rester la règle, jamais l'édition à la main d'une branche sœur.

**Invariant « aucun membre de `GamePackets` n'atteint le `switch` final »**, refait après le lot :
l'énumération compte **141** membres, **42** ne sont référencés ni dans `GameClient.cs` ni dans
`GameActions.cs`, et ces 42 sont **tous** des `TM_SC_*` (aucun `TM_CS_*`, donc aucun id reçu).
`TM_CS_TURN_ON_PK_MODE` est référencé **8** fois dans `GameClient.cs`.

```
for n in $(grep -oE '^\s+TM_[A-Z0-9_]+' Game/Network/Packets/Enums/GamePackets.cs | tr -d ' ')
do grep -q "GamePackets\.$n" Game/Network/Clients/GameClient.cs \
     Game/Network/Clients/Actions/GameActions.cs || echo "ABSENT: $n"; done
→ 42 lignes ABSENT, toutes TM_SC_*
```

### 11.8 Réserves

1. **`header.Length == 7` exigé (§7 point 5)** : la fiche recommandait « journaliser en `Warning` et
   ignorer la trame », c'est ce qui est livré — le refus est dans `TryReadTurnOnPkMode`, donc une
   trame rembourrée n'est jamais appliquée. C'est un **choix**, explicitement laissé à Killian (§9
   point 3) : le revenir à « accepter toute longueur » est une ligne (`TryReadTurnOnPkMode`), et le
   comportement observable d'une trame anormale resterait un `Warning`.
2. **Aucune restriction de zone, de moral, de niveau ou de ville** : §7 point 1 n'est pas tranché et
   le paquet ne devine rien. Si Killian veut gater, le point d'insertion est `HandleTurnOnPkMode`
   (`GameClient.cs:448`), avant la mutation.
3. **Le niveau de journal de la trame valide est `Debug`** : c'est le comportement observable actuel
   (une ligne par bascule manuelle, pas de trace persistante). Un niveau supérieur est un changement
   d'une ligne.
4. **Deux tests sont écrits pour survivre à la fusion du lot 801** :
   `OnDataReceived_ConsumesTheUndeclaredTwin801WithoutThrowing` (vert avec ou sans 801) et
   `Ids_AreTheEpic73Ones` (ne vérifie que 1800, pas 801). Aucun des deux n'assert quoi que ce soit sur
   le journal ni sur ce que 801 publiera : ils restent verts avec ou sans ce lot-là.
5. **Le mode PK n'est toujours visible que du client de l'acteur** (§5.4) : le paquet rend la bascule
   du client correcte, pas le PK visible par un tiers.
6. **Le bloc §10 a été corrigé sur place** : il citait `GmCommandService.SendStatus`, supprimée par ce
   lot. §11.9 redonne le bloc complet, prêt à coller dans `CLAUDE.md`.

### 11.9 Bloc `CLAUDE.md` prêt à coller (le §10, avec une phrase corrigée)

Le bloc §10 a été corrigé **sur place** par le commit de cette section : `GmCommandService.SendStatus`
n'existe plus, les deux sites d'envoi du masque passent par `GameClient.SendActorStatus()`. Voici le
bloc complet tel qu'il doit être collé dans `CLAUDE.md` (c'est le texte de §10, vérifié après le lot) :

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
et 801. Traitement attendu : muter `ConnectionInfo.PkMode` et republier le masque, exactement ce que
fait la commande `/pk` — les deux passent désormais par `GameClient.SendActorStatus()`, seul site qui
compose le masque du joueur. NGemity ne traite ni l'un ni l'autre paquet et n'appelle jamais son
`SetPKOn` : il n'y a rien à y porter.

Détail complet : `docs/packet-specs/800-turn-on-pk-mode.md`.
```

### 11.10 Commandes relevées

```
git branch --show-current                  → hermes/packet-800-turn-on-pk-mode
git status --porcelain                     → vide avant le lot
git log --oneline origin/master..master    → aucune ligne
export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
dotnet build Navislamia.sln -c Debug       → code 0, 0 Error(s), 164 avertissements (hors lot)
dotnet test Tests/Tests.csproj             → code 0, 1316 réussis / 1316, 0 échec, 0 ignoré
                                             (base §5.5 : 1302 / 1302 sur b56967a)
```

`dotnet ef` reste absent du conteneur : aucun schéma n'est touché par ce paquet (rien à migrer).

### 11.11 Preuve que les tests mordent (critère 3 : « une assertion fausse doit échouer »)

Deux mutations **transitoires** ont été portées sur le code de production, mesurées, puis annulées
(`git checkout -- <fichier>`, arbre de travail propre revérifié avant et après) :

1. `GamePackets.cs` : `TM_CS_TURN_ON_PK_MODE = 800` remplacé par `= 1800` →
   `dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~TurnOnPkMode"` → **code 1**,
   `Failed: 2, Passed: 12` :
   ```
   Failed ClientFrame_IsSevenBytes_WithLengthAtZeroIdAtFourAndChecksumAtSix
     Expected BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) to be 800us, but found 1800us
   Failed Ids_AreTheEpic73Ones
     Expected ((ushort)GamePackets.TM_CS_TURN_ON_PK_MODE) to be 800us, but found 1800us
   ```
   L'assertion d'offset sur l'`ID` (octets 4-5) mord donc bien.
2. `GameActionPackets.cs` : `TryReadTurnOnPkMode` passé de `packet.Length == 7` à `>= 7` (la règle
   recommandée §7 point 5 retirée) → **code 1**, `Failed: 4, Passed: 10` :
   ```
   Failed TryReadTurnOnPkMode_RefusesAPaddedFrame   → Expected …BeFalse, but found True
   Failed TryReadTurnOnPkMode_RefusesALongerFrame   → Expected …BeFalse, but found True
   Failed OnDataReceived_ConsumesAPaddedFrameWithoutTurningTheModeOn
     Expected session.PkMode to be false …, but found True
   Failed OnDataReceived_ConsumesALongerFrameWithoutTurningTheModeOn
     Expected session.PkMode to be false …, but found True
   ```
   Les tests de taille totale et de cas de bord (8 et 15 octets) mordent donc eux aussi, au niveau du
   lecteur **et** de la vraie boucle de réception.

Après annulation des deux mutations : arbre propre (`git status --porcelain` vide) et
`TurnOnPkMode` de nouveau **14 réussis / 14** en code 0.
