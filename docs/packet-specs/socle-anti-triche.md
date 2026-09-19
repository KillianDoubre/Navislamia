# Fiche de paquet — socle « anti-triche client (NPGame/GameGuard) » : `TM_SC_ANTI_HACK` (53) et `TM_CS_ANTI_HACK` (54)

- Carte Hermes : `t_2de88433` (`navislamia:socle:anti-triche`), archéologie et spécification, profil `navis-ref`.
- Carte Trello : <https://trello.com/c/15INU72W>.
- Branche : `hermes/packet-socle-anti-triche`, créée depuis `master` = `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7`
  (« Fix drop and Monsters attack character back »). C'est la branche de tout le lot : `navis-dev`
  (tâche fille `t_11fe7b99`) la poursuit pour le code.
- Nature de la tâche : **fiche seule**. Aucun fichier de code applicatif n'est modifié ici ; le seul
  autre fichier touché est `.gitignore` (§8.4).
- Livrable : ce document, `docs/packet-specs/socle-anti-triche.md`.

## 0. Ce que cette fiche conclut, en une page

1. **Les deux paquets existent, sont symétriques et sont figés en taille.** rzu définit
   `TS_SC_ANTI_HACK` (53, serveur→client) et `TS_CS_ANTI_HACK` (54, client→serveur) avec exactement les
   mêmes deux champs : `nLength` (`uint16`, offset 7) puis `byBuffer` (`uint8[400]`, offsets 9 à 408).
   La charge utile est **fixe** (402 octets), pas variable : `_(array)` est un *tableau de taille fixe*
   (`DEFINITION_F_array` → `uint8_t byBuffer[400]`, §2.1), donc le datagramme fait **409 octets** avec
   l'en-tête de 7 octets du dépôt. Le champ `Length` de l'en-tête doit valoir **409**.
2. **Le gating de version est tranché pour 7.3 : les identifiants sont 53 et 54.** rzu ne connaît que
   deux branches, `version < EPIC_9_6_3` et `version >= EPIC_9_6_3` ; `EPIC_7_3 = 0x070300` et
   `EPIC_9_6_3 = 0x090603` (rzu `PacketEpics.h:59`, `:96`). Les identifiants `1053`/`1054` sont donc
   **hors 7.3** et ne doivent apparaître nulle part dans le socle. Confirmation indépendante :
   `op_codes.md:33-34` (la table 7.3 du dépôt) et la table id→nom du client 7.3 (§4.2).
3. **La référence de logique est muette.** Ni `reference/ngemity/Chihiro/` ni NavisLamia ne traitent
   53/54. NGemity a les en-têtes et rien d'autre : `0` occurrence de `anti_hack`, `antihack`,
   `nprotect`, `xtrap`, `game_guard`, `gameguard` dans tout `Chihiro/` (§6.1). Un paquet reçu non
   enregistré y finit dans un log DEBUG « Got unknown packet » et rien de plus
   (`WorldSession.cpp:158-162`). **Il n'y a aucun modèle à recopier.**
4. **Le client Epic 7.3 tel qu'il est livré ne peut pas produire ce blob.** `SFrame.exe`
   (sha256 `41e0af2e…`) n'importe **aucun** module anti-triche : sa table d'imports ne contient que
   KERNEL32, d3d9, PSAPI, WS2_32, IMM32, VERSION, audiere, DevIL, ILU, mss32, USER32, GDI32, COMDLG32,
   ADVAPI32, SHELL32, ole32, OLEAUT32, WINMM, IPHLPAPI, dbghelp, WININET (§4.1) ; aucune chaîne
   `npgg*`, `GameMon*`, `NPGame*`, `nprotect*`, `XignCode*`, `hackshield*` n'existe dans le binaire
   (§4.1) ; et le seul bras du client pour le défi entrant 53 est un **cas vide explicite** (§4.3).
   Il connaît les *noms* 53, 54, 58, 59 (§4.2) mais rien ne l'oblige à envoyer 54.
   Les messages d'écran GameGuard (`msgbox_np_gamehack_detect`, `msgbox_np_gamehack_killed`,
   `msgboxdetect_*`) et un avis de support coréen mentionnant les fichiers `*.erl` de GameGuard existent
   dans les ressources du client, **mais aucun chemin de code ne les atteint d'une manière que j'aie pu
   établir** (§4.4, §9 item 4) : ce sont des restes de la construction coréenne.
5. **La conséquence pour le socle :** le socle ne peut pas *vérifier* le blob (aucun producteur de
   référence, aucun format de référence, aucun service), et il ne peut pas non plus *produire* 53 sans
   une décision d'exploitation. Ce qu'il peut faire — et ce que cette fiche désigne comme le minimum,
   en §8 — est **purement structurel** : nommer l'identifiant 54, décrire sa trame et son décalage,
   consommer le datagramme sans aucune disposition, et le tester. **Aucun comportement de sécurité n'est
   encodé** : ni vérification, ni relais, ni journalisation du blob, ni déconnexion, ni réponse 53.
6. **La décision d'exploitation reste entièrement à Killian**, sous forme de matrice en §10 : elle n'est
   ni tranchée ici, ni présumée par le code du socle.

Si l'option retenue par Killian est « 54 est accepté et ignoré » (option **c** de §10), alors il n'y a
effectivement **aucun paquet à spécifier** : la trame de §2 est tout ce qu'il y a à savoir, la carte
Trello « TM_CS_ANTI_HACK (54) » peut être fermée en connaissance de cause, et le socle se réduit à
l'enregistrement de l'identifiant décrit en §8.

## 1. Cadre de la carte

| Élément | Valeur |
|---|---|
| Identifiant de suivi | `navislamia:socle:anti-triche` |
| Carte Hermes | `t_2de88433` (cette fiche), tâche fille `t_11fe7b99` (`navis-dev`) |
| Carte Trello | <https://trello.com/c/15INU72W> (« Socle : anti-triche client (NPGame/GameGuard) — paquets 53 et 54 ») |
| Branche | `hermes/packet-socle-anti-triche` |
| Base | `master` `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` |
| Références épinglées | rzu `87c1e83bf84efe29bb6405e8e6da80349712f3fa` ; NGemity/Chihiro `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` ; client 7.3 = `SFrame.exe` sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` |
| Sort de la carte | Instruction de la carte « Socle : anti-triche client » ; débloque la carte « TM_CS_ANTI_HACK (54) » (en `THINKING`) et, par extension, la famille de vérification de sécurité client (57, 59, 60, 9005), en `BACKLOG`. |

**Ce que la carte n'est pas.** Ce n'est pas la carte « TM_CS_ANTI_HACK (54) » (elle reste en `THINKING`) ;
ce n'est pas non plus une carte de politique de sécurité. Le socle pose la structure, pas la disposition.

**Trois pièges de lecture, désamorcés d'entrée :**

- les en-têtes `TS_SC_*` vivent dans un répertoire **`GameClient/`** aussi bien dans rzu
  (`reference/rzu/librzu/src/packets/GameClient/TS_SC_ANTI_HACK.h`) que dans NGemity
  (`reference/ngemity/shared/Server/Packets/GameClient/TS_SC_ANTI_HACK.h`) : l'emplacement ne dit **pas**
  la direction. C'est le dernier argument de `CREATE_PACKET_VER_ID(..., SessionPacketOrigin::Server)`
  (rzu `TS_SC_ANTI_HACK.h:13`) et le préfixe `TS_SC_`/`TS_CS_` qui tranchent ;
- `DisconnectType.AntiHack = 101` (`Game/Network/Packets/Enums/DisconnectType.cs:16`) porte le mot
  « AntiHack » et **n'a aucun rapport établi avec 53/54**. Son seul usage dans le dépôt est une
  déconnexion après un échec de suppression de personnage (`GameActions.cs:428`), dans
  `OnDeleteCharacter`. Le nom vient du protocole (`DISCONNECT_TYPE_ANTI_HACK = 101`,
  NGemity `TS_SC_DISCONNECT_DESC.h:13`), pas d'un quelconque service anti-triche. **Ne pas s'appuyer sur
  ce nom pour en déduire une politique** ;
- l'ordre des champs de rzu ne suffit pas à conclure que `nLength` décrit `byBuffer` : voir §2.1 et
  §2.3. C'est exactement la question que la carte demande de trancher ou de déclarer `NON ÉTABLI`.

## 2. Inventaire des deux directions

L'en-tête du dépôt est de **7 octets** : `Length` (`uint32`, offsets 0-3, **longueur totale en-tête
comprise**), `ID` (`uint16`, offsets 4-5), `Checksum` (offset 6). Source : `Game/Network/Packets/Header.cs:6-11`
(`[StructLayout(LayoutKind.Sequential, Pack = 1)]`, champs dans cet ordre) et la preuve que `Length`
comprend l'en-tête en `Tests/Game/ActionPacketsTests.cs:208-209` (`TS_SC_RESULT` : `HaveCount(15)` et
`ReadUInt32LittleEndian(…, 0).Should().Be(15)` pour 7 + 8 octets de charge). Toutes les positions
ci-dessous sont donc exprimées **en absolu depuis le premier octet du datagramme**.

### 2.1 `TM_CS_ANTI_HACK` (54, client → serveur)

Source de la structure : `reference/rzu/librzu/src/packets/GameClient/TS_CS_ANTI_HACK.h`.

```
#define TS_CS_ANTI_HACK_DEF(_) \
	_(simple)(uint16_t, nLength) \
	_(array)(uint8_t, byBuffer, 400)

#define TS_CS_ANTI_HACK_ID(X) \
	X(54, version < EPIC_9_6_3) \
	X(1054, version >= EPIC_9_6_3)
```
(`TS_CS_ANTI_HACK.h:5-11` ; `CREATE_PACKET_VER_ID(TS_CS_ANTI_HACK, SessionType::GameClient,
SessionPacketOrigin::Client)` en `:13`.)

| Offset | Taille | Type | Nom | Source | Valeur observée |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` (en-tête du dépôt) | `Header.cs:9` | doit valoir **409** ; aucune capture locale |
| 4 | 2 | `uint16` | `ID` (en-tête) | `Header.cs:10` | **54** |
| 6 | 1 | `uint8` | `Checksum` (en-tête) | `Header.cs:11` | calculé par le client |
| 7 | 2 | `uint16` **LE** | `nLength` | rzu `TS_CS_ANTI_HACK.h:6` | **NON ÉTABLI** (§2.3) |
| 9 | 400 | `uint8[400]` | `byBuffer` | rzu `TS_CS_ANTI_HACK.h:7` | **NON ÉTABLI** (blob opaque) |
| **Total** | **409** | | | 7 + 2 + 400 | — |

**Taille totale attendue : 409 octets** (dont 7 d'en-tête), soit une charge utile de 402 octets. Le
champ `byBuffer` s'étend des offsets 9 à 408 **inclus**.

**Pourquoi « fixe » et pas « `nLength` + `nLength` octets ».** `_(array)(uint8_t, byBuffer, 400)` est un
tableau de taille **fixe** dans rzu, pas un tableau dynamique :

- `#define DEFINITION_F_array(type, name, size, ...) type name[size];` → `uint8_t byBuffer[400]`
  (`reference/rzu/librzu/src/lib/Packet/PacketDeclaration.h:141`) ;
- `SIZE_F_ARRAY3` somme **les 400** éléments, sans condition (`…:223-225`) ;
- `SERIALIZATION_F_ARRAY3` écrit **les 400** éléments (`buffer->template writeArray<type>(#name, name,
  size);`, `…:327`) et `DESERIALIZATION_F_ARRAY3` en lit 400 (`…:424`).

rzu n'enchaîne donc **pas** `_(count)` + `_(dynarray)` ici : `nLength` n'est pas utilisé comme préfixe de
longueur par le sérialiseur. Un client conforme à rzu émet **toujours** 409 octets. À l'inverse, un
client qui émettrait 2 + `nLength` octets sans remplir les 400 produirait un datagramme plus court, que
le serveur lirait comme une trame de 7 + 2 + `nLength` octets — voir §2.3 et §9 item 1.

### 2.2 `TM_SC_ANTI_HACK` (53, serveur → client)

Source : `reference/rzu/librzu/src/packets/GameClient/TS_SC_ANTI_HACK.h`.

```
#define TS_SC_ANTI_HACK_DEF(_) \
	_(simple)(uint16_t, nLength) \
	_(array)(uint8_t, byBuffer, 400)

#define TS_SC_ANTI_HACK_ID(X) \
	X(53, version < EPIC_9_6_3) \
	X(1053, version >= EPIC_9_6_3)
```
(`TS_SC_ANTI_HACK.h:5-11` ; `… SessionPacketOrigin::Server` en `:13`.)

| Offset | Taille | Type | Nom | Source | Valeur observée |
|---|---|---|---|---|---|
| 0-6 | 7 | en-tête | `Length` / `ID` / `Checksum` | `Header.cs:9-11` | `Length` = **409**, `ID` = **53** |
| 7 | 2 | `uint16` **LE** | `nLength` | rzu `TS_SC_ANTI_HACK.h:6` | **NON ÉTABLI** |
| 9 | 400 | `uint8[400]` | `byBuffer` | rzu `TS_SC_ANTI_HACK.h:7` | **NON ÉTABLI** |
| **Total** | **409** | | | 7 + 2 + 400 | — |

**Les deux paquets sont rigoureusement identiques** hors l'identifiant et la direction : même nom de
champ, même type, même taille, même ordre, dans les deux arbres de référence
(rzu `TS_SC_ANTI_HACK.h:6-7` ≡ `TS_CS_ANTI_HACK.h:6-7` ; NGemity `TS_SC_ANTI_HACK.h:7-8` ≡
`TS_CS_ANTI_HACK.h:7-8`). Rien, dans aucune des deux références, ne dit ce que 53 doit contenir.

**Aucune référence ne fait produire 53 par un serveur.** Ni rzu ni NGemity n'ont de code qui émet
`TS_SC_ANTI_HACK` : la seule occurrence des deux en-têtes dans tout NGemity hors des en-têtes eux-mêmes
est la liste d'inclusions de `reference/ngemity/shared/Server/XPacket.h:182` (§6.1), et dans rzu il n'y a
**aucune** occurrence hors des en-têtes (§3).

### 2.3 Ce que `nLength` désigne : NON ÉTABLI

C'est la question posée explicitement par la carte, et la réponse est : **on ne sait pas**, et rien dans
les références disponibles ne le tranche.

- rzu ne donne que la macro (`TS_CS_ANTI_HACK.h:6`), sans commentaire de champ. Le dépôt lui-même
  documente ailleurs certains champs par des commentaires `///` (par exemple dans les en-têtes de la
  famille `SECURITY_NO`) ; ici il n'y en a aucun. Aucun sérialiseur, aucun test, aucune fonction de
  rzu ne lit `nLength` : `grep -rn "TS_CS_ANTI_HACK\|TS_SC_ANTI_HACK"` sur tout `reference/rzu/` ne
  renvoie que les deux définitions d'en-tête (§3).
- NGemity recopie la même macro sans commentaire (`TS_CS_ANTI_HACK.h:7`, `TS_SC_ANTI_HACK.h:7`) et
  n'a aucun code qui la lise (§6.1).
- Le client 7.3 ne fournit **aucun** éclairage : son seul bras pour 53 est un cas vide (§4.3), donc
  aucun code du client ne lit ce champ non plus — pas même côté réception.

Deux lectures restent possibles, et la fiche **ne choisit pas** :

- **(L1) longueur utile de `byBuffer`** : le nom `nLength` accolé à `byBuffer` suggère que les `nLength`
  premiers octets du tableau portent le blob et que le reste est du remplissage. Sous cette lecture,
  `nLength ≤ 400`, et les octets `byBuffer[nLength..399]` seraient indéterminés (zéros, reste de tampon,
  ou données du transit précédent) — **aucune des deux références ne le dit** ;
- **(L2) taille de la charge / du paquet** : `nLength` pourrait porter la longueur totale (409) ou la
  longueur de charge (402), comme d'autres protocoles le font. Le fait que la trame soit de toute façon
  fixe (§2.1) rend cette lecture possible sans être nécessaire.

**Conséquence opérationnelle, et c'est tout ce que la fiche en tire :** aucune validation, seuil ou
troncature ne peut être écrit sur `nLength` dans le socle. Un socle qui comparerait `nLength` à 400, ou
qui tronquerait `byBuffer` à `nLength`, encoderait (L1) comme un fait. Ce point va en §9 item 1 et en
§10 (question à Killian).

## 3. Gating de version : 53 et 54 pour l'Epic 7.3

Résultat, et c'est un résultat **tranché** : pour l'Epic 7.3, les identifiants en jeu sont **54**
(client→serveur) et **53** (serveur→client).

Preuve par le gating rzu, qui ne connaît que deux branches :

| En-tête | Branche `version < EPIC_9_6_3` | Branche `version >= EPIC_9_6_3` | Source |
|---|---|---|---|
| `TS_CS_ANTI_HACK` | **54** | 1054 | `reference/rzu/librzu/src/packets/GameClient/TS_CS_ANTI_HACK.h:10-11` |
| `TS_SC_ANTI_HACK` | **53** | 1053 | `reference/rzu/librzu/src/packets/GameClient/TS_SC_ANTI_HACK.h:10-11` |

Valeurs des paliers, dans **le même arbre de référence** : `#define EPIC_7_3 0x070300`
(`reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59`) et
`#define EPIC_9_6_3 0x090603  // GS packet ID modified with version 20200713` (`…:96`).
`0x070300 < 0x090603` : la branche applicable est `version < EPIC_9_6_3`. Les identifiants `1053`/`1054`
appartiennent à un palier **postérieur à 9.6.3** et sont donc hors sujet pour ce dépôt ; ils ne doivent
apparaître ni dans `GamePackets`, ni dans un test, ni dans un commentaire du socle.

**Trois confirmations indépendantes du même résultat :**

1. **La table 7.3 du dépôt** : `op_codes.md:33-34` donne `[53] = "TM_SC_ANTI_HACK"` et
   `[54] = "TM_CS_ANTI_HACK"`. Gouvernance du dépôt : « le client Epic 7.3 tranche tout ; rzu tranche
   les tailles, l'ordre des champs et le gating par version ». Ici les trois disent la même chose.
2. **La table id→nom du client 7.3** (§4.2) : `53 → TM_SC_ANTI_HACK`, `54 → TM_CS_ANTI_HACK`.
3. **La borne basse est cohérente avec le reste de la table 7.3** : la même famille, dans rzu, porte la
   même bascule au même palier — `TS_CS_CHECK_ILLEGAL_USER` `X(57, version < EPIC_9_6_3)` /
   `X(1057, …)` (`…/TS_CS_CHECK_ILLEGAL_USER.h:9-10`), `TS_SC_XTRAP_CHECK` `X(58, …)` / `X(1058, …)`
   (`…/TS_SC_XTRAP_CHECK.h:9-10`), `TS_CS_XTRAP_CHECK` `X(59, …)` / `X(1059, …)`
   (`…/TS_CS_XTRAP_CHECK.h:9-10`), `TS_CS_REQUEST` `X(60, …)` / `X(1060, …)`
   (`…/TS_CS_REQUEST.h:10-11`), `TS_SECURITY_NO` `X(9005, …)` / `X(8105, …)` (`…/TS_SECURITY_NO.h:14-15`).
   Aucun de ces en-têtes ne porte de branche de gating antérieure à `EPIC_9_6_3`.

**Contre-épreuve de méthode, pour montrer que le gating rzu est bien celui de ce dépôt :**
`TS_CS_VERSION` porte `X(50, version < EPIC_7_4)` / `X(51, version >= EPIC_7_4 && version < EPIC_9_6_3)` /
`X(1051, version >= EPIC_9_6_3)` (`reference/rzu/librzu/src/packets/GameClient/TS_CS_VERSION.h:12-14`).
Pour 7.3 (`0x070300 < 0x070400`), l'identifiant attendu est donc **50** — ce que confirment
`op_codes.md:33` (`[50] = "TM_CS_VERSION"`) et la table du client 7.3 (§4.2 : `50 → TM_CS_VERSION`, relevé
en `SFrame.exe` à `0x67539c`). Le palier `EPIC_7_4` de rzu discrimine donc bien à l'intérieur des 7.x, et
`op_codes.md` est bien une table **7.3**.

**Ce que le gating ne dit pas.** Aucun des en-têtes de la famille ne porte de commentaire
« Since EPIC_x » ni de branche antérieure à 9.6.3 : rzu ne date donc **pas** l'apparition de 53/54. Que
ces identifiants soient stables « depuis l'Epic 6.3 » se déduit seulement de l'absence de branche
antérieure, pas d'une affirmation de la référence — la fiche ne le présente donc pas comme un fait daté.

## 4. Ce que le client Epic 7.3 attend réellement

Tout ce qui suit est une **lecture statique** de `reference/client73/` ; aucun binaire n'a été exécuté
(interdit par le rôle), aucun Lua n'a été lancé, et `reference/client73/` **n'est pas un dépôt git** : il
n'y a donc pas de SHA de client à épingler, seulement le fichier et son empreinte.

| Ressource | Empreinte / format | Méthode de lecture employée |
|---|---|---|
| `SFrame.exe` | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 octets, `pei-i386` | `objdump -h` (carte des sections), `objdump -p` (imports), `objdump -d` (désassemblage), `strings -n 4` / `strings -t x` (chaînes et offsets), scripts Python de recherche d'immediats 32 bits |
| `db_string.rdb` | 14 294 729 octets | recherche d'octets brute par script Python (`bytes.find`) — **pas** `strings`, qui sous-échantillonne un fichier de cette taille |
| `extraction-manifest.json` | index de l'archive `data.000` (sha256 `b88ac39a…`, 83 822 entrées, 50 fichiers extraits) | lecture JSON |

Carte des sections de `SFrame.exe` (`objdump -h`), nécessaire à toute conversion VA ↔ offset fichier :
`.text` VMA `0x00401000` / offset fichier `0x00000400` / taille `0x0060a8cf` ; `.rodata` `0x00a0c000` /
`0x0060ae00` ; `.rdata` `0x00a0f000` / `0x0060da00` / `0x002006a8` ; `.data` `0x00c10000` / `0x0080e200` /
`0x00038600`. Conversion utilisée partout ci-dessous : `VA = VMA + (offset_fichier - offset_section)`.

### 4.1 Aucun module anti-triche n'est importé par le client

`objdump -p SFrame.exe` liste **21 DLL importées**, et aucune n'est un composant de sécurité :

```
KERNEL32.dll  d3d9.dll   PSAPI.DLL  WS2_32.dll  IMM32.dll   VERSION.dll
audiere.dll   DevIL.dll  ILU.dll    mss32.dll   USER32.dll  GDI32.dll
COMDLG32.dll  ADVAPI32.dll  SHELL32.dll  ole32.dll  OLEAUT32.dll
WINMM.dll     IPHLPAPI.DLL  dbghelp.dll  WININET.dll
```

Aucun `npggNT.dll`, `GameMon.des`/`GameMon64.des`, `npgame*.dll`, `npkc*.dll`, `XTrap*.dll`,
`XignCode*`, `hackshield*`, `AhnLab*`. Recherche complémentaire de chaînes dans tout `SFrame.exe`
(`strings -n 4`) sur les motifs `nprotect`, `gameguard`, `game_guard`, `npgame`, `np_game`, `antihack`,
`anti_hack`, `xtrap`, `ggauth`, `guardian`, **insensible à la casse** : les seuls résultats sont

```
 610f85  GameGuard                       (dans un avis coréen, §4.4)
 652278  TM_CS_XTRAP_CHECK
 65228c  TM_SC_XTRAP_CHECK
 6522a0  TM_CS_ANTI_HACK
 6522b0  TM_SC_ANTI_HACK
 641dcc  msgbox_np_gamehack_detect
 641de8  msgbox_np_gamehack_killed
```

**Conclusion pour ce point.** Un module anti-triche ne peut pas être chargé statiquement (rien n'est
importé) et aucune chaîne ne nomme un module à charger dynamiquement (pas de `npggNT.dll` à passer à
`LoadLibrary`, pas de `GameMon.des` à lancer). Le client 7.3 livré ici **ne contient pas** l'intégration
`NPGame`/`GameGuard` : il en a gardé des chaînes d'interface (§4.4), pas le composant.

### 4.2 La table id→nom du client nomme 53, 54, 58 et 59

Méthode (reproductible, déjà employée par les fiches précédentes) : dans `.text`, pour chaque
`push $<imm32 pointant dans .rdata sur une chaîne TM_*/TS_*>`, l'identifiant est le premier
`mov $<imm32>,%eax` qui suit. Relevé sur `SFrame.exe` : **166 paires brutes, 151 identifiants plausibles
(< 20000)**, dans la zone `.text` `0x66e1ce`..`0x6795bf`.

| Id | Nom | `push $nom` (VA) | Chaîne (VA) |
|---|---|---|---|
| 50 | `TM_CS_VERSION` | `0x67539c` | `0xa53a04` |
| **53** | **`TM_SC_ANTI_HACK`** | `0x675d37` | `0xa538b0` |
| **54** | **`TM_CS_ANTI_HACK`** | `0x675dac` | `0xa538a0` |
| 58 | `TM_SC_XTRAP_CHECK` | `0x675e22` | `0xa5388c` |
| 59 | `TM_CS_XTRAP_CHECK` | `0x675e98` | `0xa53878` |

Contrôle de méthode sur des entrées déjà relevées par une fiche antérieure : ma passe sort
`504 → TM_SC_DEAD` (`push` `0x677988`) et `513 → TM_CS_RESURRECTION` (`push` `0x677dbe`), ce qui
concorde **exactement** avec les emplacements déjà cités par la fiche `socle-mort-respawn`
(sur la branche `hermes/packet-socle-mort-respawn`, `docs/packet-specs/socle-mort-respawn.md:114-116` :
`0x00677988` charge `$0x1f8`, `0x00677dbe` charge `$0x201`, `0x006778c4` = 500), et avec son relevé de la
queue commune du répartiteur à `0x0067ef39` (même fiche, `:120` et `:124`). La méthode est donc stable
d'une session à l'autre. *(Le fichier vit sur cette branche et n'existe pas encore sur `master` : la
citation n'est reproductible qu'avec `git show hermes/packet-socle-mort-respawn:docs/packet-specs/socle-mort-respawn.md`.)*

**Le client nomme 53, 54, 58 et 59, mais ne nomme ni 55, ni 56, ni 57, ni 60.** Les chaînes
`TM_SC_GAME_GUARD_AUTH_QUERY`, `TM_CS_GAME_GUARD_AUTH_ANSWER`, `TM_CS_CHECK_ILLEGAL_USER` et
`TM_CS_REQUEST` sont **absentes** de tout le binaire.

**Piège, à ne pas transformer en conclusion** : cette table est **incomplète** — la même fiche
`socle-zones-evenement` a montré qu'elle n'a rien pour 25..29 alors que rzu atteste 25/26/27/28 en 7.3,
et l'on voit ici que 55 est *traité* par le client (§4.3) sans être nommé. L'absence d'un nom ne prouve
donc **pas** que le paquet est inconnu du client, et la présence d'un nom ne prouve **pas** qu'il est
émis : rzu dit par ailleurs que `TS_CS_VERSION`, `TS_CS_SECURITY_NO` et `TS_SC_DEAD` sont connus, sans
que cela détermine l'émission. **Cette table ne peut donc pas servir à trancher « le client envoie
54 »** — d'où §4.3.

### 4.3 Le répartiteur entrant du client traite 53, 55 et 58 comme des cas vides

C'est le relevé qui répond à la question « le client attend-il une réponse 53 ? ». Le répartiteur des
paquets entrants est en `.text` :

```
67df59: movzwl 0x4(%ebx),%ecx        ; identifiant du paquet (offset 4 de la structure d'en-tête)
67df5d: mov    %ecx,%eax
67df5f: cmp    $0xfe,%eax            ; 254
67df64: jg     0x67e1d9              ; ids 255..500 -> second étage (table 0x67f218 / 0x67f19c)
67df6a: je     0x67e1cc              ; id 254 -> cas propre
67df70: cmp    $0xfa,%eax            ; 250
67df75: ja     0x67ef21              ; ids 251..253 -> chemin par défaut
67df7b: movzbl 0x67f0a0(%eax),%eax   ; table d'octets indexée par l'id (0..250)
67df82: jmp    *0x67f020(,%eax,4)    ; table de sauts de second niveau
```

Les deux tables d'étage sont **dans `.text`** : table d'octets en `0x67f0a0` (251 entrées, valeurs 0 à 31)
et table de sauts en `0x67f020` (32 entrées). Résolution :

| Id | Discriminant | Cible | Interprétation |
|---|---|---|---|
| 53 | 14 | `0x67ef39` | **cas explicite, vide** |
| 55 | 14 | `0x67ef39` | **cas explicite, vide** |
| 58 | 14 | `0x67ef39` | **cas explicite, vide** |
| 54 | 31 | `0x67ef21` | chemin par défaut |
| 56 | 31 | `0x67ef21` | chemin par défaut |
| 57 | 31 | `0x67ef21` | chemin par défaut |
| 59 | 31 | `0x67ef21` | chemin par défaut |
| 60 | 31 | `0x67ef21` | chemin par défaut |

Le discriminant **31** est celui de la très grande majorité des 251 identifiants (218 sur 251) : c'est la
branche « non traité ». La cible `0x67ef39` est le **tronc commun de sortie** du répartiteur : le code y
libère l'objet-paquet puis reboucle pour lire le paquet suivant. Les trois identifiants 53, 55 et 58 sont
donc, dans le client, des **cas `switch` explicitement vides** — reconnus et sans effet.

**Contrôle qui valide la lecture** : les **33** identifiants traités par ce premier étage sont, sans
exception, des paquets **serveur→client** — `0`, `2`, `3`, `4`, `8`, `9`, `10`, `11`, `12`, `21`, `22`,
`24`, `28`, `30`, **`53`**, **`55`**, **`58`**, `101`, `102`, `103`, `202`, `205`, `207`, `209`, `210`,
`211`, `213`, `216`, `217`, `220`, `222`, `240`, `250` — et **aucun** identifiant client→serveur n'y
figure : ni `1` (`TM_CS_LOGIN`), ni `5`, ni `7`, ni `20`, ni `23`, ni `25`/`26`/`27`, ni `50`
(`TM_CS_VERSION`), ni `54`, ni `56`, ni `57`, ni `59`, ni `60`, ni `100`, ni `150`, ni `200`. La
répartition « traités » / « non traités » correspond donc exactement à la direction des paquets, ce qui
confirme que la table a été lue correctement et que **54, 56, 57 et 59 n'ont pas à y figurer**.

**Conséquence directe** : le client 7.3 *reconnaît* le défi 53 mais ne fait **rien** de son contenu.
Interrogé sur « le client attend-il une réponse 53, et avec quel contenu ? », le client 7.3 répond :
il n'attend rien, et le contenu de 53 lui est indifférent — ce qui est cohérent avec l'absence de module
anti-triche (§4.1). Corollaire : rien dans le client ne peut produire le blob de 54, puisque rien ne
réagit à 53.

### 4.4 Les traces GameGuard du client sont des restes d'interface, pas des chemins de code

Quatre artefacts existent, et la fiche les cite pour ce qu'ils sont :

1. **Un avis coréen** en `.rdata` (`' GameGuard '` à l'offset fichier `0x610f85`, VA `0xa12585` ; début de
   la chaîne à l'offset fichier `0x610f38`, VA `0xa12538`, 193 octets). Décodé en euc-kr, il demande au
   joueur d'envoyer par courriel à `Game1@inca.co.kr` les fichiers `*.erl` présents dans le dossier
   `GameGuard`. Ce texte **est référencé par du code** : sa VA de début apparaît exactement une fois
   dans `.text`, à `0x44b5e8`, dans une séquence qui alloue une paire `{id, chaîne}`
   (`movl $0x317,(%eax)` = identifiant d'avis **791**, `movl $0xa12538,0x4(%eax)`, puis insertion par
   `call 0x43fb60`) — c'est un **registre d'avis**, pas une vérification anti-triche.
2. **Des clés de boîtes de message** en `.rdata` : `msgbox_np_gamehack_detect` (VA `0xa433cc`),
   `msgbox_np_gamehack_killed` (VA `0xa433e8`), et toute une famille `msgboxdetect_*`
   (`game_hack`, `general_hack`, `module_change`, `automacro`, `speedhack`, `speedhack_app`, `kdtrace`,
   `kdtrace_changed`, `messagehook`, `hookfunction`, `driverfailed`). Ces clés occupent des pointeurs
   contigus en `.data` (`0xc1c100`..`0xc1c13c`).
3. **Des libellés localisés** dans `db_string.rdb` :
   - clé `smsg_protectsolution01` (offset `0x44b89d`) → « GameGuard is scanning for threats. Please be
     patient. » ;
   - clé `smsq_server_errordisconnect` (offset `0xab3d71`) → « Connection Error: Connection for
     GameGuard has timed out. » ;
   - clé `smsg_gameguard_error` (offset `0x44cbea`) → la valeur stockée est **`Page 3`**, entre
     `app_string_35 Page 2` et `app_string_37 Page 4`. Autrement dit la page d'erreur GameGuard a été
     **réduite à un substitut de gabarit** dans cette construction.
4. **Rien d'autre.** Aucune chaîne `smsg_gameguard_*` au-delà de celles-ci, et aucun autre fichier `.rdb`
   concerné : recherche brute sur les **50** `.rdb` extraits pour `np_gamehack`, `msgboxdetect_`,
   `smsg_gameguard`, `GameGuard is scanning`, `Connection for GameGuard` — les seuls résultats sont les
   trois libellés ci-dessus, tous dans `db_string.rdb`.

**Ce que je peux en dire, et ce que je ne peux pas.** Présence n'est pas usage : ces quatre artefacts
prouvent que la construction du client descend d'une source coréenne équipée de
`NPGame`/`GameGuard`, et que l'interface en a gardé le vocabulaire. Je n'ai **pas** pu établir que les
clés `msgbox_np_gamehack_*` / `msgboxdetect_*` soient atteignables : leurs emplacements en `.data`
(`0xc1c0f0`..`0xc1c13c`) ne sont référencés par **aucun** pointeur 32 bits, ni dans `.text`, ni dans
`.data`, et je n'ai pas trouvé de base de tableau adressée en `.text` qui les couvrirait — mais la table
de clés est manifestement indexée (base + index × 4), et mon relevé n'a pas su localiser sa base. Ce
point reste donc **NON ÉTABLI** (§9 item 4) et n'est utilisé dans aucune conclusion.

### 4.5 Qui produit le blob, quand, à quelle fréquence : NON ÉTABLI

La carte demande explicitement ces trois paramètres. Réponse honnête : **aucun des trois n'est
établi**, et c'est un résultat en soi.

- **Qui produit le blob.** Aucun composant anti-triche n'existe dans le client livré (§4.1). Le
  composant externe nommé par la carte (`NPGame`/`GameGuard`) n'est ni importé ni nommé dans le binaire.
  Un module injecté de l'extérieur reste **théoriquement** possible (c'est le mode d'emploi historique
  de `nProtect`) : un tel module pourrait appeler la fonction d'envoi du client sans que rien n'apparaisse
  dans sa table d'imports. **Cette hypothèse n'est pas vérifiable par lecture statique** et la fiche ne
  la retient pas comme un fait.
- **À quel moment du cycle d'entrée en jeu.** Non établi. Aucune capture réseau locale, et le client ne
  peut pas être lancé sur ce VPS (interdit). Le seul enchaînement qu'on puisse *imaginer* (le serveur
  envoie 53, le client répond 54) est contredit par §4.3 : le client ne réagit pas à 53.
- **À quelle fréquence.** Non établi, et sans objet en l'absence de producteur : `nLength` et `byBuffer`
  ne portent ni compteur, ni identifiant de session, ni horodatage (§2.1), donc rien dans la trame ne
  permettrait même de raisonner sur la fréquence.

**Contre-épreuve disponible, si Killian veut trancher cela plus tard** : la seule voie est une capture
réseau d'un client Epic 7.3 réellement protégé, hors de ce VPS. Aucune analyse statique de plus ne
l'établira — la fiche le dit plutôt que de le simuler.

## 5. Traitement attendu : l'état du code NavisLamia, et ce que le serveur fait aujourd'hui

### 5.1 Il n'y a rien dans NavisLamia

Relevés faits sur la base `master` `6a982c8`, à confirmer par le lecteur :

| Recherche | Résultat |
|---|---|
| `grep -rniE "antihack\|anti_hack\|xtrap\|illegal\|checkuser\|gameguard\|nprotect\|security_no" Game --include=*.cs` | **2 lignes, aucune liée à 53/54** : `Game/Network/Packets/Enums/DisconnectType.cs:16` et `Game/Network/Clients/Actions/GameActions.cs:428` |
| `grep -rniE "hack\|guard\|security\|xtrap\|cheat" Game/DataAccess/Entities --include=*.cs` | **0 ligne** : aucune entité de sécurité client, aucun stockage du blob |
| `grep -nicE "antihack\|anti_hack\|xtrap\|gameguard\|security_no" ArcadiaSchemaPSQL.sql` | **0** : le schéma ne porte ni table, ni colonne de sécurité client |
| `Game/Network/Packets/Enums/GamePackets.cs` | **aucun membre** pour 53, 54, 55, 56, 57, 58, 59 ni 60 (les valeurs voisines déclarées sont `TM_CS_RETURN_LOBBY = 23` `:69`, `TM_CS_REQUEST_RETURN_LOBBY = 25` `:70`, `TM_CS_REQUEST_LOGOUT = 26` `:71`, `TM_CS_LOGOUT = 27` `:72`, `TM_SC_DISCONNECT_DESC = 28` `:73`, `TM_CS_VERSION = 50` `:75`, `TM_NONE = 9999` `:89`) |
| `_actions.Add(...)` dans `Game/Network/Clients/Actions/GameActions.cs` | **8 enregistrements** (`:43-50`) : `TM_CS_VERSION`, `TM_CS_LOGIN`, `TM_CS_REPORT`, `TM_CS_CHARACTER_LIST`, `TM_CS_CREATE_CHARACTER`, `TM_CS_DELETE_CHARACTER`, `TM_CS_CHECK_CHARACTER_NAME`, `TM_CS_ACCOUNT_WITH_AUTH`. **Rien de la famille anti-triche** |
| `Game/Services/` | aucun service de vérification, de relais ou de stockage de blob |

Le nom `AntiHack` du dépôt ne désigne **rien de tel** : `DisconnectType.AntiHack = 101`
(`DisconnectType.cs:16`) est le code de déconnexion 101 du protocole, et son unique usage est une
déconnexion après l'échec de suppression d'un personnage (`GameActions.cs:428`, dans
`OnDeleteCharacter`). Aucun rapport établi avec 53/54 — la fiche s'interdit d'en tirer une politique.

### 5.2 Ce que le serveur fait aujourd'hui d'un datagramme 54 : il le jette proprement

`GameClient.OnDataReceived` (`Game/Network/Clients/GameClient.cs:468-688`) lit l'en-tête, vérifie le
`Checksum` (`:475`), attend la suite si `header.Length > remainingData` (`:477`), **rejette et
déconnecte** seulement si le checksum est faux (`:486-491`), puis :

- **`:497-501`** — `if (!Enum.IsDefined(typeof(GamePackets), header.ID))` →
  `_logger.Debug("Undefined packet ID: {id} Length: {length}) received from {clientTag}", …)` puis
  `continue;`. Comme 54 n'est pas un membre de `GamePackets`, **un client qui enverrait 54 tombe
  exactement ici** : le datagramme est consommé, journalisé en `Debug`, la connexion est conservée, et
  aucune exception n'est levée.
- **`:635-640`** — un groupe de trois identifiants consommés **sans réponse ni journal** :
  `TM_CS_UPDATE`, `TM_CS_MONSTER_RECOGNIZE`, `TM_CS_QUERY` (`continue;`). C'est l'idiome du dépôt pour
  « valide, aucune réponse attendue » ; `CLAUDE.md:182` le documente pour `TS_CS_MONSTER_RECOGNIZE`
  (« valid and needs no response while monsters are idle »).
- **`:670-682`** — le `switch` final, **8 bras**, terminé par `_ => throw new Exception("Unknown Packet
  Type")`. C'est le piège que la carte signale : *déclarer* un membre de `GamePackets` **sans** lui donner
  de bras ailleurs dans `OnDataReceived` transforme aujourd'hui un paquet ignoré en exception.

**Point important, et il compte pour la décision de §10 :** l'état actuel est **sûr**. Un 54 non déclaré
est ignoré sans effet de bord ; rien n'est cassé, et rien n'est à réparer. Ce que le socle apporte n'est
donc pas une correction de sécurité, c'est de la **connaissance de protocole** : rendre l'identifiant
nommé et sa trame décrite, et préparer l'endroit unique où la disposition de Killian s'appliquera.

### 5.3 Ce que le serveur devrait répondre : rien, en l'état des références

- **Aucune référence ne documente de réponse.** Ni rzu ni NGemity n'ont de code émettant 53 (§2.2).
- **Le client visé n'attend rien non plus** : son bras pour 53 est un cas vide (§4.3).
- **NavisLamia ne sait pas produire 53** : il n'existe ni générateur de défi, ni identifiant de session
  dans la trame, ni service pour vérifier la réponse.

Conclusion, qui est aussi le constat d'infaisabilité partielle demandé par la carte : **le socle ne peut
implémenter ni émission de 53, ni vérification de 54, ni réponse, ni sanction.** Ce qui manque est nommé
en §10 (matrice des options) et §11 (matériel d'entrée manquant), et le minimum structurel réellement
implémentable est désigné en §8.

## 6. Écarts avec NGemity, et pourquoi

### 6.1 NGemity est muet : c'est le constat, pas une lacune de recherche

Recherche élargie, comme demandé, sur **tout** `reference/ngemity/Chihiro/` :

| Motif (insensible à la casse) | Occurrences |
|---|---|
| `anti_hack`, `antihack`, `nprotect`, `xtrap`, `game_guard`, `gameguard`, `XignCode`, `hackshield` | **0** |
| `hack` seul | 4 lignes, toutes des commentaires sans rapport : « Hack for epic 4 », « Some dirty hacks unknown to mankind », « it's just a dirty hack », « Temporary hack for respawn list » |
| `guard` seul | uniquement les verrous de NGemity (`NG_UNIQUE_GUARD`, `NG_SHARED_GUARD`) |

Dans tout NGemity, les seules occurrences des paquets de la famille, **hors de leurs propres en-têtes**,
sont les listes d'inclusions de `reference/ngemity/shared/Server/XPacket.h:51`, `:81`, `:176`, `:182`,
`:323` et les valeurs de l'énumération `reference/ngemity/shared/Server/ClientPackets.h:53-59`. Aucun
handler, aucun `case`, aucun `TS_CS_ANTI_HACK` passé à `GetPacketData`, aucun envoi de `TS_SC_ANTI_HACK`.

**Ce que NGemity fait d'un 54 reçu** : sa table de handlers ne le contient pas
(`worldPacketHandler[]`, **44** `declareHandler`, `reference/ngemity/Chihiro/src/Network/GameNetwork/WorldSession.cpp:92-137`),
et `ProcessIncoming` retombe alors sur un **log DEBUG et rien d'autre** :

```cpp
// Report unknown packets in the error log
if (i == worldTableSize && std::find(std::begin(ignoredPackets), std::end(ignoredPackets), (NGemity::Packets)_cmd) == std::end(ignoredPackets)) {
    NG_LOG_DEBUG("server.network", "Got unknown packet '%d' from '%s'", pRecvPct->GetPacketID(), GetRemoteIpAddress().to_string().c_str());
    return ReadDataHandlerResult::Ok;
}
```
(`reference/ngemity/Chihiro/src/Network/GameNetwork/WorldSession.cpp:158-162` ; la liste
`ignoredPackets` en `:140-141` ne contient que `TS_CS_VERSION`, `TS_CS_VERSION2`, `TS_CS_UNKN`,
`TS_CS_REPORT`, `TS_CS_TARGETING`, donc **pas 54**.)

**Écart assumé, et c'est le seul qui compte ici :** NGemity se comporte **exactement comme NavisLamia
aujourd'hui** — journaliser en DEBUG et continuer. NavisLamia le fait via
`Enum.IsDefined`/`"Undefined packet ID"` (`GameClient.cs:497-499`), NGemity via son log
« Got unknown packet ». Il n'y a donc **pas de modèle à recopier** : la référence de logique est muette
sur le fond (que faire du blob), et la fiche le dit au lieu de lui faire dire davantage.

### 6.2 Les trames, elles, concordent — avec deux détails à ne pas confondre

Aucun écart de structure sur cette famille : pour chacun, NGemity et rzu donnent les mêmes champs, dans
le même ordre et à la même taille.

| Paquet | rzu (7.3) | NGemity | Verdict |
|---|---|---|---|
| `TS_CS_ANTI_HACK` (54) | `uint16 nLength` + `uint8 byBuffer[400]` (`TS_CS_ANTI_HACK.h:6-7`) | identique (`TS_CS_ANTI_HACK.h:7-8`) | **concordent** ; NGemity n'a pas de gating de version (`CREATE_PACKET(…, 54)`, `:10`) alors que rzu en a un (`:10-11`) — sans conséquence, cf. ci-dessous |
| `TS_SC_ANTI_HACK` (53) | idem (`TS_SC_ANTI_HACK.h:6-7`) | idem (`TS_SC_ANTI_HACK.h:7-8`) | **concordent** |
| `TS_CS_XTRAP_CHECK` (59) / `TS_SC_XTRAP_CHECK` (58) | `uint8 pCheckBuffer[128]` (`:6` / `:6`) | identique (`:7` / `:7`) | **concordent** — 135 octets sur le fil |
| `TS_CS_CHECK_ILLEGAL_USER` (57) | `uint32 log_code` (`:6`) | identique (`:7`) | **concordent** — 11 octets sur le fil |
| `TS_CS_GAME_GUARD_AUTH_ANSWER` (56) / `TS_SC_GAME_GUARD_AUTH_QUERY` (55) | `TS_GAME_GUARD_AUTH`, gating `version < EPIC_9_1` → `V1` = 4 × `uint32` (`TS_SC_GAME_GUARD_AUTH_QUERY.h:7-11`, `:22-24`) | même gating `version < EPIC_9_1` (`TS_SC_GAME_GUARD_AUTH_QUERY.h:19-22`) | **concordent** ; pour 7.3 = `V1`, 16 octets de charge, **23 octets** sur le fil |
| `TS_CS_REQUEST` (60) | `uint8 t` + `endstring command` (`TS_CS_REQUEST.h:6-7`) | `XPacket.h:139` l'inclut | hors socle (§7) |
| `TS_SECURITY_NO` (9005) | `mode (int32)` + `security_no (string, 19)` pour `< EPIC_9_6_7` (`TS_SECURITY_NO.h:6-11`) | `int32 mode` + `string security_no [19]` (`TS_CS_SECURITY_NO.h:7-8`) | **concordent** — 23 octets de charge, 30 sur le fil ; noms divergents (`TS_SECURITY_NO`, origine `Any` dans rzu vs `TS_CS_SECURITY_NO` dans NGemity et `TM_CS_SECURITY_NO` dans `op_codes.md`) |

**Pourquoi le gating manquant de NGemity ne crée pas d'écart ici.** NGemity ne connaît pas du tout le
palier 9.6.3 : `#define EPIC_LATEST EPIC_9_5_2`
(`reference/ngemity/shared/Server/Packets/PacketEpics.h:32`), et `EPIC_9_6_3` n'existe **nulle part**
dans son arbre. Comme rzu place la bascule de toute cette famille à `EPIC_9_6_3` et qu'aucun palier
intermédiaire n'existe (`EPIC_7_3 = 0x070300` `:15`, `EPIC_7_4 = 0x070400` `:16`, `EPIC_9_1 = 0x090100`
`:21`, `EPIC_9_5_2 = 0x090502` `:29`), les identifiants non versionnés de NGemity coïncident avec ceux
de rzu **pour tout palier de 6.3 à 9.5.2** — donc pour l'Epic 7.3 comme pour la cible de NGemity.

**Piège de NGemity, à ne pas recopier :** son `ClientPackets.h` est un miroir **hérité** qui ne suit pas
le gating de ses propres en-têtes. Il déclare `TS_CS_UNKN = 50`, `TS_CS_VERSION = 51`,
`TS_CS_VERSION2 = 52` (`ClientPackets.h:50-52`) alors que son `TS_CS_VERSION.h:10-12` gate
`X(50, version < EPIC_7_4)` / `X(51, version >= EPIC_7_4)` — donc **50**, pas 51, pour l'Epic 7.3. Sa
liste `ignoredPackets` (`WorldSession.cpp:141`) utilise cette énumération, ce qui la rend fausse pour
7.3. Pour la famille anti-triche les valeurs 53-59 coïncident heureusement, mais **c'est une
coïncidence** : la source de vérité est l'en-tête gaté, pas cette énumération.

## 7. Périmètre : toute la famille, et ce que le socle en retient

La carte demande d'établir « si le socle couvre cette famille ou seulement 53/54 ». La réponse est
**seulement 54** (§8), mais la fiche inventorie la famille entière pour que les cartes qui suivent
n'aient pas à refaire le relevé. Vérification demandée : `op_codes.md:33-41` porte bien `[50]` puis
`[53]`…`[60]`, et `op_codes.md:271` porte `[9005]`. Les neuf lignes existent, la famille est donc
**complète dans la table** et aucun identifiant n'y manque.

**Piège de classement, à traiter avant tout dispatch.** Dans rzu **comme** dans NGemity, les en-têtes
`TS_SC_*` et `TS_CS_*` de cette famille vivent tous dans le dossier `packets/GameClient/` (rzu) ou
`Packets/GameClient/` (NGemity). Ce nom désigne la **session** (`GameClient` = session de jeu, par
opposition à `AuthClient`), **pas** la direction du paquet. La direction se lit dans rzu sur
`SessionPacketOrigin::Server` / `::Client` (`TS_SC_ANTI_HACK.h:13` / `TS_CS_ANTI_HACK.h:13`) et, côté
NavisLamia, sur le préfixe `TM_CS_*` / `TM_SC_*` de `op_codes.md`. Un rangement par dossier ferait
écrire un handler entrant pour des paquets serveur→client.

| Id 7.3 | Nom `op_codes.md` | En-tête rzu | Origine | Charge utile (octets) | **Total sur le fil** | Traité ici ? |
|---|---|---|---|---|---|---|
| 53 | `TM_SC_ANTI_HACK` (`:34`) | `TS_SC_ANTI_HACK` | serveur→client | 2 + 400 = **402** | **409** | décrit §2.2, **hors socle** |
| **54** | **`TM_CS_ANTI_HACK`** (`:35`) | `TS_CS_ANTI_HACK` | client→serveur | 2 + 400 = **402** | **409** | **décrit §2.1, retenu §8** |
| 55 | `TM_SC_GAME_GUARD_AUTH_QUERY` (`:36`) | `TS_SC_GAME_GUARD_AUTH_QUERY` | serveur→client | 16 pour 7.3 | **23** | inventorié §7.1, hors socle |
| 56 | `TM_CS_GAME_GUARD_AUTH_ANSWER` (`:37`) | `TS_CS_GAME_GUARD_AUTH_ANSWER` | client→serveur | 16 pour 7.3 | **23** | inventorié §7.1, hors socle |
| 57 | `TM_CS_CHECK_ILLEGAL_USER` (`:38`) | `TS_CS_CHECK_ILLEGAL_USER` | client→serveur | `uint32` = **4** | **11** | inventorié §7.2, hors socle |
| 58 | `TM_SC_XTRAP_CHECK` (`:39`) | `TS_SC_XTRAP_CHECK` | serveur→client | 128 | **135** | inventorié §7.3, hors socle |
| 59 | `TM_CS_XTRAP_CHECK` (`:40`) | `TS_CS_XTRAP_CHECK` | client→serveur | 128 | **135** | inventorié §7.3, hors socle |
| 60 | `TM_CS_REQUEST` (`:41`) | `TS_CS_REQUEST` | client→serveur | 1 + `endstring` | **8 + longueur** | inventorié §7.4, hors socle |
| 9005 | `TM_CS_SECURITY_NO` (`:271`) | `TS_SECURITY_NO` (origine `Any`) | client→serveur | 4 + 19 = **23** | **30** | inventorié §7.5, hors socle |

### 7.1 `TM_SC_GAME_GUARD_AUTH_QUERY` (55) et `TM_CS_GAME_GUARD_AUTH_ANSWER` (56)

Le seul membre de la famille dont la charge change **avant** 9.6.3, et c'est un palier à l'intérieur
des 7.x : `TS_GAME_GUARD_AUTH_DEF` sélectionne `TS_GAME_GUARD_AUTH_V1`
(`uint32 dwIndex`, `dwValue1`, `dwValue2`, `dwValue3` — 4 × 4 = 16 octets) pour
`version < EPIC_9_1`, et `TS_GAME_GUARD_AUTH_V2` (`uint16` de comptage, `uint16` inconnu,
`uint8` dynamique) pour `version >= EPIC_9_1`
(`reference/rzu/librzu/src/packets/GameClient/TS_SC_GAME_GUARD_AUTH_QUERY.h:7-11` et `:22-24`).
**Pour Epic 7.3 (`0x070300 < 0x090100`), c'est `V1` : 16 octets de charge, 23 octets sur le fil.**
NGemity porte le même gating au même palier (`TS_SC_GAME_GUARD_AUTH_QUERY.h:19-22`), donc sans écart.
Le paquet 55 est par ailleurs **l'un des trois cas vides du client** (§4.3) : le client 7.3 reconnaît
le défi GameGuard et n'y répond pas. `V2` est typé par un `_(count)` + `_(dynarray)` : c'est une trame
**variable**, donc si une carte ultérieure traite 55, elle devra refaire le raisonnement de version — la
fiche le signale pour éviter que le 16 octets soit recopié tel quel dans un contexte 9.x.

### 7.2 `TM_CS_CHECK_ILLEGAL_USER` (57)

`_(simple)(uint32_t, log_code)` (`reference/rzu/librzu/src/packets/GameClient/TS_CS_CHECK_ILLEGAL_USER.h:6`),
soit 4 octets de charge et **11 octets sur le fil**, gating `X(57, version < EPIC_9_6_3)` / `X(1057, …)`
(`:9-10`). NGemity identique (`TS_CS_CHECK_ILLEGAL_USER.h:7`). Le nom `log_code` suggère un code de
journal côté serveur, mais **aucune référence ne dit ce que le serveur en ferait** : ni rzu ni NGemity
n'ont de handler, et NavisLamia ne journalise rien de tel. Le client 7.3 **ne connaît pas ce nom**
(la chaîne `TM_CS_CHECK_ILLEGAL_USER` est absente du binaire, §4.2) — ce qui n'exclut pas qu'il l'émette,
la table des noms étant incomplète.

### 7.3 `TM_SC_XTRAP_CHECK` (58) et `TM_CS_XTRAP_CHECK` (59)

`_(array)(uint8_t, pCheckBuffer, 128)` des deux côtés
(`TS_SC_XTRAP_CHECK.h:6`, `TS_CS_XTRAP_CHECK.h:6`), soit **135 octets sur le fil** — la plus petite
trame malgré son nom. Gating `X(58, …)`/`X(1058, …)` et `X(59, …)`/`X(1059, …)` (`:9-10` chacune).
NGemity identique (`TS_SC_XTRAP_CHECK.h:7`, `TS_CS_XTRAP_CHECK.h:7`). Le client 7.3 **nomme 58 et 59**
(§4.2) et **traite 58 comme un cas vide** (§4.3) : il reconnaît le défi XTrap et n'y répond pas.

### 7.4 `TM_CS_REQUEST` (60) — le cas le plus dangereux de la famille

`_(simple)(uint8_t, t)` + `_(endstring)(command, true)`
(`reference/rzu/librzu/src/packets/GameClient/TS_CS_REQUEST.h:6-7`) : la **seule trame variable** des
neuf, `7 + 1 + longueur(command) + 1` octets. Gating `X(60, …)`/`X(1060, …)` (`:10-11`). Le client 7.3
**ne nomme pas** 60 (§4.2). C'est un **canal de commande** (le `t` d'un `TM_CS_REQUEST`/`TM_SC_RESULT`
est le même champ que celui des `ResultCode` du dépôt, `Game/Network/Packets/ResultCode.cs`), donc la
carte qui le traitera devra statuer sur **une liste blanche de commandes** : c'est de la politique pure,
et c'est très exactement ce que le présent socle s'interdit. **Recommandation de séquencement** : ne pas
laisser 60 être traité avant que Killian ait statué sur l'anti-triche, car un `TM_CS_REQUEST` non filtré
est une surface d'attaque à lui seul, sans aucun rapport avec le blob.

### 7.5 `TM_CS_SECURITY_NO` (9005)

`TS_SECURITY_NO` : pour 7.3 (`version < EPIC_9_6_7`) les champs présents sont `int32_t mode` et
`string security_no` de 19 octets — l'`account` de 64 octets et les `result`/`security_no_1`/
`security_no_2` ne sont ajoutés qu'à partir d'`EPIC_9_6_7`
(`reference/rzu/librzu/src/packets/GameClient/TS_SECURITY_NO.h:6-11`). Donc **23 octets de charge,
30 octets sur le fil**, gating `X(9005, version < EPIC_9_6_3)` / `X(8105, …)` (`:14-15`). NGemity
concorde (`TS_CS_SECURITY_NO.h:7-8`). **Ce n'est pas un paquet anti-triche** : c'est le code de sécurité
du compte (le « numéro de sécurité » saisi par le joueur), et il appartient à la famille par la seule
symétrie de nommage. Le client 7.3 **nomme** `TM_CS_SECURITY_NO` (VA `0xa52ec8`, entrée `9005` à
`push` `0x678fbc` de la table id→nom). La carte qui le prendra devra statuer sur la vérification et le
stockage du code — donc sur du schéma, ce que `ArcadiaSchemaPSQL.sql` ne porte pas aujourd'hui (§5.1).

### 7.6 Pourquoi le socle ne retient que 54

Trois raisons, dans l'ordre de force :

1. **La carte le cadre explicitement** (`t_11fe7b99` : « inclure uniquement `TM_CS_ANTI_HACK` »), et
   `TM_SC_ANTI_HACK` (53) est **serveur→client** : NavisLamia n'a aucune branche de dispatch entrante
   pour un paquet qu'il n'émet pas, et le critère transversal « chaque membre de `GamePackets` doit
   avoir un bras de traitement » interdit de l'introduire sans bras.
2. **Aucun des huit autres n'a d'information nouvelle à faire remonter** : 57, 59, 60, 9005 sont
   client→serveur mais chacun porte une décision (journal ? whitelist de commandes ? vérification du
   code de sécurité ?) que la fiche ne peut pas trancher à la place de Killian. 55, 56 et 58 sont
   serveur→client, donc hors d'un socle entrant.
3. **Le seul contenu de 54 est opaque** (§2.1) : c'est le seul de la famille dont on puisse décrire
   la trame sans avoir à choisir une politique, précisément parce qu'il n'y a rien à en faire.

## 8. Découpage : le minimum structurel que la branche implémentera

### 8.1 Il existe un sous-ensemble structurel, et il est étroit

**Oui, un sous-ensemble existe** — et il est plus étroit que la carte ne le suggère. Il consiste à
**rendre `TM_CS_ANTI_HACK` (54) connu et décrit, sans rien décider de son sort**. Les quatre éléments,
et rien de plus :

| # | Fichier | Modification |
|---|---|---|
| 1 | `Game/Network/Packets/Enums/GamePackets.cs` | ajouter **un** membre : `TM_CS_ANTI_HACK = 54,` (nom 7.3, `op_codes.md:35` ; id 7.3, gating rzu `TS_CS_ANTI_HACK.h:10-11`) |
| 2 | `Game/Network/Packets/Game/GameAntiHackPackets.cs` | **nouveau**, sur le modèle de `GameActionPackets.cs` (classe statique, `private const int HeaderSize = 7;`, `ReadOnlySpan<byte>` + `BinaryPrimitives`, aucune structure *marshalled*) : exposant la taille totale (`409`), la taille de la charge (`402`) et un lecteur `TryReadAntiHack(ReadOnlySpan<byte> packet, out ushort declaredLength)` qui lit le `uint16` aux offsets **7-8** |
| 3 | `Game/Network/Clients/GameClient.cs` | **un** bras, placé **avant** le `switch` de `:670`, distinct du groupe `:635-640` : si `header.ID == (ushort)GamePackets.TM_CS_ANTI_HACK`, journaliser en `Debug` `header.Length` et `nLength` puis `continue;` — **sans** réponse, **sans** `Disconnect`, **sans** validation |
| 4 | `Tests/Game/AntiHackPacketTests.cs` | **nouveau** test d'offsets (§8.3) |

**Ce que le socle ne fait pas, et le dev ne doit pas ajouter :** aucune émission de 53, aucune
vérification du blob, aucune entrée en base, aucune table, aucun réglage de configuration, aucune
sanction, aucun service, aucune modification de `GameActions.cs`, **aucune écriture dans `CLAUDE.md`**
(Hermes protège ce fichier — le bloc correspondant est fourni en §12 pour la description de MR).

### 8.2 Pourquoi ce bras ne décide rien

C'est le point délicat de la carte, donc il est explicité :

- **L'état observable ne change pas.** Aujourd'hui un 54 reçu tombe dans
  `!Enum.IsDefined` (`GameClient.cs:497-501`) : journal `Debug` puis `continue`. Après le socle, il tombe
  dans le nouveau bras : journal `Debug` puis `continue`. Même effet, **même niveau de log**, aucune
  nouvelle branche atteignable par un autre identifiant.
- **Il n'emprunte pas le groupe `:635-640`.** Ce groupe (`TM_CS_UPDATE`, `TM_CS_MONSTER_RECOGNIZE`,
  `TM_CS_QUERY`) signifie *« valide, aucune réponse attendue »* : c'est une **disposition déjà statuée**
  (documentée `CLAUDE.md:182`). Y verser 54 reviendrait à trancher l'option (c) par la porte de service.
  Le bras doit donc être un `if` séparé, avec un commentaire renvoyant à cette fiche et disant que la
  disposition est **ouverte**.
- **Il n'utilise pas `nLength` pour décider.** `nLength` est lu **pour l'observabilité seule** (§2.1 :
  sa sémantique n'est pas établie). Le bras ne compare `nLength` à rien, ne tronque rien, ne refuse rien.
  Conséquence assumée : une trame 54 trop courte est consommée comme les autres et journalisée ; il n'y a
  pas de rejet, parce qu'un rejet serait une disposition.
- **Il reste l'endroit unique où la décision s'appliquera.** Quelle que soit l'option retenue en §10,
  c'est ce `if` qui devient la vérification, le relais ou la déconnexion — un point de modification,
  pas quatre.

### 8.3 Test d'offsets attendu (critère transversal n° 3)

`Tests/Game/AntiHackPacketTests.cs`, au moins les assertions suivantes :

1. `(ushort)GamePackets.TM_CS_ANTI_HACK` vaut **54**.
2. Taille totale : `GameAntiHackPackets.AntiHackPacketSize == 409` et
   `409 == 7 + sizeof(ushort) + 400`.
3. Offsets du cadre reconstruit, sur un `byte[409]` neuf : `Length` en 0-3
   (`BinaryPrimitives.ReadUInt32LittleEndian(frame) == 409`), `ID` en 4-5 (`== 54`), `Checksum` en 6,
   `nLength` en **7-8**, `byBuffer` en **9-408**.
4. Comportement de bord du lecteur : 408 octets → refus, 409 octets → acceptation, et la valeur lue est
   bien celle écrite aux offsets 7-8 (tester par exemple `0x1234` et `0x0000`, ce dernier pour montrer
   que `nLength == 0` est accepté et **non** traité comme une erreur).
5. `Enum.IsDefined(typeof(GamePackets), (ushort)54)` est vrai **et** l'identifiant ne peut pas atteindre
   le `switch` final de `GameClient.OnDataReceived` (le bras consommateur le précède).

**Ce que le test ne doit pas faire** : échantillonner un blob « réaliste ». Aucun blob de référence
n'existe (§11) : un corpus inventé donnerait une fausse impression de validation.

### 8.4 Le seul autre fichier touché : `.gitignore`

`/docs/*` est ignoré sur `master` (`.gitignore:470`) et seuls `world-spawning.md`,
`character-bootstrap.md` et `npc-dialogs.md` sont explicitement ré-inclus (`:471-473`). Le répertoire
`docs/packet-specs/` **n'existe pas** sur `master` et serait donc ignoré en silence. Le socle ajoute
une ligne, exactement comme l'a fait la branche `hermes/packet-socle-zones-evenement` :

```
!/docs/packet-specs/
```

Ce n'est **pas** une anomalie de dépôt : la convention est déjà d'ouvrir `/docs/*` fichier par fichier.
Sans cette ligne, la fiche serait non suivie et le commit de `navis-dev` partirait sans elle.

## 9. NON ÉTABLI

Chaque point est formulé comme la question exacte à trancher, pour qu'aucun dev n'ait à le deviner.

1. **Sémantique de `nLength`.** `TS_CS_ANTI_HACK.h:6` déclare `_(simple)(uint16_t, nLength)` et rien de
   plus : la référence **ne dit pas** si `nLength` est la longueur utile de `byBuffer`, la longueur
   totale de la charge, ou un compteur sans rapport. Le nom suggère une longueur, la position (juste
   avant le tampon) suggère qu'elle le décrit — deux indices, aucune preuve. **Il faut un blob réel pour
   trancher.** En attendant : aucun code ne doit comparer `nLength` à quoi que ce soit (§8.2).
2. **Contenu de `byBuffer` au-delà de `nLength`.** `_(array)` étant un tableau **fixe** de 400 octets
   (rzu `PacketDeclaration.h:141` : `type name[size];`), les 400 octets sont **toujours écrits** sur le
   fil ; mais rien ne dit si le client zérote, laisse un reste de tampon, ou remplit. La question est à
   trancher sur un blob réel, et elle a une conséquence pratique directe : si le reste n'est pas
   zéroté, un journal ou une empreinte du blob deviennent non déterministes.
3. **Ce que le serveur devrait envoyer en 53.** Aucune référence ne définit un défi : ni contenu, ni
   cadence, ni condition de déclenchement. Impossible de savoir ce qui serait « correct » — donc
   impossible d'implémenter l'option (a) sans une source externe (voir §11).
4. **Atteignabilité des clés de messages anti-triche du client.** Les clés `msgbox_np_gamehack_detect` /
   `msgbox_np_gamehack_killed` / `msgboxdetect_*` sont dans une table de pointeurs de `.data`
   (`0xc1c0f0`..`0xc1c13c`) dont je n'ai **pas** su localiser la base adressée en `.text` : ni pointeur
   direct vers les emplacements, ni base candidate référencée dans la fenêtre explorée. Question ouverte :
   ces écrans sont-ils encore atteignables, ou sont-ils du code mort ? **La fiche ne conclut pas** dans
   un sens ni dans l'autre et n'appuie aucune décision sur eux.
5. **Un client 7.3 modifié peut-il envoyer 54 ?** Les clients privés (patchés, avec un module anti-triche
   ré-injecté) ne sont pas ce binaire-ci. Non vérifiable par lecture statique du client livré, et non
   vérifiable sans capture réseau (interdite ici).
6. **Les huit autres membres de la famille.** Hors socle, mais chacun porte une question ouverte qui
   devra être tranchée avant sa carte : que fait le serveur d'un `log_code` (57) ? quelles commandes un
   `TM_CS_REQUEST` (60) a-t-il le droit de porter, et selon quelle liste blanche ? le code de sécurité
   (9005) est-il vérifié, et stocké où (`ArcadiaSchemaPSQL.sql` ne porte rien, §5.1) ? 55/56 supposent
   un backend GameGuard qui n'existe pas plus que celui de (a).
7. **Remarque de méthode, à ne pas lire comme un doute sur 53/54.** Mon relevé de la table id→nom du
   client donne **166 paires brutes / 151 identifiants plausibles** là où la note de méthode du profil
   (`navislamia-packet-spec`, `references/client73-lecture-statique.md` §3) annonce **169 / 152** pour le
   même binaire. Les deux passes sont déterministes ; l'écart de trois vient vraisemblablement de la
   fenêtre de recherche du `mov $imm32,%eax` qui suit le `push`, ou d'un léger recadrage de la zone
   `.text`. Les cinq entrées citées en §4.2 ont été relues individuellement
   (`push` `0x67539c`, `0x675d37`, `0x675dac`, `0x675e22`, `0x675e98`) et sont exactes ; c'est la
   **comparaison chiffrée** qui est incertaine, pas les entrées.

## 10. A VERIFIER PAR KILLIAN

**Rien n'est tranché ici.** La faisabilité dépend d'abord d'une décision d'exploitation que la fiche ne
peut pas prendre à la place de Killian. Ce qui suit est une matrice d'options, pas une recommandation.

### 10.1 La matrice

| | Option | Ce que ça veut dire pour le serveur | Ce que devient le bras de §8.2 | Ce que Killian doit fournir | Faisable aujourd'hui ? |
|---|---|---|---|---|---|
| **a** | **Vérifier le blob** auprès d'un anti-triche réel | Le serveur envoie un défi 53, le client répond 54, le serveur valide et sanctionne | Un appel à un vérificateur, avec un verdict et une sanction | Le **contrat** du composant anti-triche (format du blob, clé, réponse attendue) et son **hébergement** : ni l'un ni l'autre n'existe aujourd'hui | **Non** — aucune source, aucun blob de référence (§11) |
| **b** | **Consigner / relayer le blob** | Le blob est journalisé (ou transmis) pour analyse hors ligne, sans verdict | Un écrivain (fichier, base, service) | Où consigner, combien de temps, et si le blob doit être **traçable** jusqu'au personnage | **Partiellement** — techniquement simple, mais le schéma n'existe pas (`ArcadiaSchemaPSQL.sql` muet) |
| **c** | **Accepter et ignorer** | Le blob est consommé sans effet ; le serveur assume de ne pas vérifier | **Le bras tel que désigné en §8.2, inchangé** | Seulement une confirmation d'exploitation : « ce shard ne fait pas d'anti-triche » | **Oui** — c'est l'état de fait du client livré : il ne peut pas envoyer 54 (§4.1 et §4.3) |
| **d** | **Refuser les clients non protégés** | Déconnexion (code 101 disponible : `DisconnectType.AntiHack`, `DisconnectType.cs:16`) | Une déconnexion, avec le code et le message | Le **déclencheur** : sur réception de 54 ? sur *absence* de 54 dans un délai après l'entrée en jeu ? avec quelle durée ? | **Non sans définition du déclencheur** — et le raisonnement sur l'absence de 54 est impossible en l'état (§9 items 2 et 5) |

### 10.2 Trois observations qui pèsent sur le choix (sans le faire)

- **L'option (c) n'est pas un renoncement : c'est la seule décrite par le client.** Le client 7.3 livré
  n'importe aucun anti-triche (§4.1) et son bras pour 53 est un cas vide (§4.3). Sur ce client, (a) et
  (d) n'auraient aucun effet observable, et (b) ne collecterait rien. Si l'exploitation vise ce client,
  la question de 54 est déjà close par les faits.
- **L'option (a) est bloquée par une source, pas par du code.** Elle exige un anti-triche réel en face ;
  ce n'est pas un travail de dev sur NavisLamia. Tant qu'elle n'est pas approvisionnée, tout socle qui
  « vérifierait » le blob inventerait un format.
- **L'option (d) est la plus risquée à coder à l'aveugle.** Elle repose sur un déclencheur qui doit être
  *mesuré* sur un client réellement protégé ; la poser sans mesure ferait déconnecter des joueurs sur une
  hypothèse.

### 10.3 Questions à trancher, une par une

1. Ce shard embarque-t-il, oui ou non, un composant anti-triche côté serveur ? Si non, l'option (c) est un
   choix d'exploitation à assumer explicitement — et la carte Trello « `TM_CS_ANTI_HACK` (54) » peut être
   fermée sans code au-delà de §8.
2. Si oui : quel composant, sous quel contrat, et où est-il hébergé ? (§11 liste ce qui manque.)
3. Le socle structurel de §8 est-il validé tel quel, c'est-à-dire **sans** aucune disposition encodée ?
   C'est le seul point qui débloque `navis-dev` sur la tâche fille `t_11fe7b99`.
4. La séquence recommandée en §7.4 (ne pas traiter `TM_CS_REQUEST` (60) avant que l'anti-triche soit
   statué) doit-elle être tenue ?
5. Faut-il étendre le socle à `TM_SC_ANTI_HACK` (53) malgré l'absence de tout émetteur dans le dépôt,
   étant donné que cela violerait le critère transversal « un membre ⇒ un bras de traitement » ?

## 11. Matériel d'entrée manquant

Ce que la fiche **ne peut pas** produire, et où le chercher. Aucun de ces éléments n'est dans le VPS de
référence ; ils ne sont pas non plus obtenables par analyse statique.

| Manque | Ce qu'il trancherait | Où le trouver |
|---|---|---|
| **Un blob `TM_CS_ANTI_HACK` (54) réel** | La sémantique de `nLength` (§9 item 1) et l'état des 400 octets au-delà (§9 item 2) | Une capture réseau (`tcpdump`/Wireshark, port de jeu) sur un client Epic 7.3 **équipé** de `NPGame`/`GameGuard` |
| **Un échange 53 → 54 complet** | Le contenu du défi 53, la cadence, le moment du cycle d'entrée en jeu | La même capture, sur le même client |
| **Le contrat du composant anti-triche** | Tout ce qui est nécessaire à l'option (a) : format de vérité, réponse attendue, sanction | Le fournisseur de l'anti-triche (dépôt, documentation, bibliothèque serveur) — rien dans `reference/` |
| **Une confirmation d'exploitation** | Le choix entre (a) (b) (c) (d) | Killian, §10 |
| **Une décision sur 57, 59, 60, 9005** | Leurs dispositions respectives | Killian, cartes dédiées (BACKLOG) |

**Méthode écartée, pour mémoire** : lancer `SFrame.exe` (interdit par le rôle, et le client ne peut pas
atteindre un serveur depuis ce VPS), exécuter ses Lua (interdit), ou rejouer le défi 53 depuis un client
non protégé (aucun effet, §4.3). Aucune de ces voies n'est praticable ici, et la fiche ne présente donc
ni `nLength` ni le producteur du blob comme établis.

## 12. Bloc destiné à `CLAUDE.md` (pour la description de MR de `navis-dev`)

`CLAUDE.md` ne mentionne aujourd'hui ni `packet-specs`, ni la famille anti-triche (vérifié : `grep -niE
"packet-specs|packet spec|fiche" CLAUDE.md` ne renvoie rien sur `master`). Bloc proposé, à placer près
des rubriques de protocole existantes :

```markdown
## Client anti-cheat packets

`TM_SC_ANTI_HACK` (`53`) and `TM_CS_ANTI_HACK` (`54`) both carry a fixed 402-byte payload: a `uint16`
`nLength` at offset 7 followed by `uint8 byBuffer[400]` at offsets 9-408, for 409 bytes on the wire.
Neither rzu nor NGemity/Chihiro shows a handler: NGemity has the headers and nothing else, and an
unregistered packet there ends in a DEBUG "Got unknown packet" log. The Epic 7.3 client `SFrame.exe`
imports no anti-cheat module at all, and its incoming dispatcher treats `53` as an explicit empty case,
so the shipped client can neither answer the challenge nor produce the `54` blob. `nLength` semantics
are not established by the reference - do not interpret the value. NavisLamia declares `54`, describes
the frame, and consumes the datagram without any disposition; the operational decision (verify, record,
ignore, or refuse) is still open. See `docs/packet-specs/socle-anti-triche.md`.
```

## 13. Commits et binaires épinglés

| Référence | Version | Empreinte |
|---|---|---|
| `reference/rzu` (`librzu`, format de fil) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` — « packets: fix TS_SC_INVENTORY with older epics » | — |
| `reference/ngemity` (logique serveur) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` — « Fix compilation issue for GCC » | — |
| `reference/client73/SFrame.exe` (le client tranche) | Epic 7.3, `pei-i386`, 9 841 664 octets | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` |
| `reference/client73/db_string.rdb` | extrait de `data.000` (index sha256 `b88ac39a0976f49746671d413ba68af616af37e6e26095c3a109d5884510adbf`) | 14 294 729 octets |
| `Navislamia` (base de la branche) | `master` `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` — « Fix drop and Monsters attack character back » | — |

`reference/client73/` **n'est pas un dépôt git** : il n'a pas de commit à épingler, d'où l'empreinte du
binaire et celle de `db_string.rdb`.

### 13.1 Emplacements relevés, pour re-vérification

Tous dans `SFrame.exe`, VR = adresse virtuelle ; conversion `VA = VMA + (offset_fichier - offset_section)`
avec les tables de sections données en §4.

| Élément | VR / offset |
|---|---|
| Importations (aucun module anti-triche) | table d'imports de `SFrame.exe` (`objdump -p`) |
| Chaîne `TM_CS_ANTI_HACK` | offset fichier `0x6522a0`, VR `0xa538a0` |
| Chaîne `TM_SC_ANTI_HACK` | offset fichier `0x6522b0`, VR `0xa538b0` |
| Entrée `54 → TM_CS_ANTI_HACK` de la table id→nom | `push` VR `0x675dac` |
| Entrée `53 → TM_SC_ANTI_HACK` | `push` VR `0x675d37` |
| Entrée `9005 → TM_CS_SECURITY_NO` | `push` VR `0x678fbc`, chaîne VR `0xa52ec8` |
| Répartiteur entrant, lecture de l'id | VR `0x67df59` (`movzwl 0x4(%ebx),%ecx`) |
| Aiguillage ids 0-250 | VR `0x67df7b` / `0x67df82` (table d'octets VR `0x67f0a0`, table de sauts VR `0x67f020`) |
| Cas vide de 53 / 55 / 58 (discriminant 14) | cible VR `0x67ef39` (queue commune du répartiteur) |
| Chemin par défaut (discriminant 31) | cible VR `0x67ef21` |
| Avis coréen GameGuard et son unique référence | chaîne VR `0xa12538` ; référence VR `0x44b5e8` |
| Clé `msgbox_np_gamehack_detect` / `_killed` | VR `0xa433cc` / `0xa433e8` ; emplacements `.data` `0xc1c13c` / `0xc1c138` |

## 14. Note de livraison

- **Livrable** : ce document, `docs/packet-specs/socle-anti-triche.md`, sur
  `hermes/packet-socle-anti-triche`, plus la ligne de `.gitignore` (§8.4) sans laquelle il serait ignoré.
- **Aucun fichier de code applicatif n'est modifié** par cette tâche. Les fichiers cités en §8.1 sont
  ceux que `navis-dev` devra toucher (tâche fille `t_11fe7b99`), pas ceux touchés ici.
- **Ce que `navis-dev` doit lire en premier** : §8 (le découpage exact), puis §2 (les offsets), puis
  §9-§10 pour savoir ce qu'il ne doit **pas** décider.
- **Réserve principale** : le socle ne peut rien vérifier, parce qu'il n'existe ni producteur de
  référence ni blob réel (§11). Une fiche qui prétendrait le contraire serait inventée.
- **Ce que cette fiche ajoute au savoir durable** : un bloc `CLAUDE.md` (§12), la trame exacte de 54,
  une table de version tranchée pour toute la famille, la preuve que le client 7.3 livré ne peut pas
  produire ce blob, et l'inventaire des huit autres paquets avec leurs tailles sur le fil.

## 15. Plan de vérification (pour `navis-qa`)

1. `git log --oneline origin/master..hermes/packet-socle-anti-triche` contient **au moins** ce commit et
   la ligne `.gitignore` ; aucun commit sur `master`.
2. `git diff master...hermes/packet-socle-anti-triche -- .gitignore` fait **exactement une ligne**
   (`!/docs/packet-specs/`).
3. Le fichier `docs/packet-specs/socle-anti-triche.md` existe **et est suivi** par git
   (`git ls-files docs/packet-specs/`).
4. Chaque `fichier:ligne` cité au §2, §4 et §6 se relit tel quel (`sed -n` sur le fichier réel) : ce sont
   les seules affirmations vérifiables ligne à ligne ; les relevés de `SFrame.exe` se revérifient par
   `objdump`/`strings` aux VR du §13.1.
5. La fiche **ne tranche aucune** des options (a) (b) (c) (d) : le §10 doit être une matrice, et le §8 un
   découpage sans disposition.
6. Aucun champ n'est présenté comme établi sans source : les `NON ÉTABLI` du §9 couvrent `nLength`, le
   contenu au-delà de `nLength`, le producteur, la cadence et les clés de messages.
