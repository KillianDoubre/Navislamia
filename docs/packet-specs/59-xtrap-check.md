# Fiche de paquet — `TM_CS_XTRAP_CHECK` (59), client → serveur

Statut : fiche d'archéologie de protocole, à destination de `navis-dev` puis de `navis-qa`.
Base : `master` = `ec76b218cd0bd7c6498d725f253abb8b431f0cd6`, branche `hermes/packet-59-xtrap-check`.
Aucun fichier de code serveur n'est modifié par cette fiche.

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id Epic 7.3 | **59** (décimal) | `op_codes.md:40` → `[59] = "TM_CS_XTRAP_CHECK"` |
| Nom | `TM_CS_XTRAP_CHECK` | `op_codes.md:40` |
| Sens | **client → serveur** | rzu `TS_CS_XTRAP_CHECK.h:12` (`SessionPacketOrigin::Client`) ; NGemity `TS_CS_XTRAP_CHECK.h:9` (`CREATE_PACKET(..., 59)`) |
| Paire | **58 = `TM_SC_XTRAP_CHECK`, serveur → client** | `op_codes.md:39` ; rzu `TS_SC_XTRAP_CHECK.h:9-10` ; NGemity `TS_SC_XTRAP_CHECK.h:9` |
| Charge utile | 128 octets opaques, sans champ de longueur | rzu `TS_CS_XTRAP_CHECK.h:6` |
| Taille sur le fil | **135 octets** (7 d'en-tête + 128 de charge) | rzu `PacketDeclaration.h:616-621` + `:223` + `MessageBuffer.h:73-77` |
| Déclaré dans `GamePackets` ? | **non**, ni 58 ni 59 | `Game/Network/Packets/Enums/GamePackets.cs` — les voisins présents sont `TM_CS_VERSION = 50` (`:87`) et `TM_NONE = 9999` (`:101`) |
| Émetteur connu | **aucun** (voir §2) | lecture statique de `reference/client73/SFrame.exe` |
| Handler dans les références | **aucun**, ni rzu ni Chihiro | `grep -rn 'XTRAP' reference/ngemity/` ne rend que l'énumération et les deux en-têtes ; idem dans rzu |

La famille « XTrap » se limite, dans **tout** l'arbre de référence, à quatre fichiers — vérifié par
`find /srv/navislamia/reference -iname '*xtrap*'` :

```
reference/rzu/librzu/src/packets/GameClient/TS_CS_XTRAP_CHECK.h
reference/rzu/librzu/src/packets/GameClient/TS_SC_XTRAP_CHECK.h
reference/ngemity/shared/Server/Packets/GameClient/TS_CS_XTRAP_CHECK.h
reference/ngemity/shared/Server/Packets/GameClient/TS_SC_XTRAP_CHECK.h
```

Il n'existe donc **aucune trame XTrap annexe** (pas de « query », pas de « result », pas de sous-type) :
la famille est exactement la paire 58/59, chacune avec un unique tableau de 128 octets.

---

## 2. Ce que le joueur fait pour que le client l'envoie

**Rien d'observable dans le client Epic 7.3 livré.** Le résultat précède la méthode : la lecture
statique de `SFrame.exe` (§5 pour le détail des relevés) ne trouve aucun chemin capable de construire
une trame d'identifiant 59. Ce n'est pas une lacune de recherche, c'est un résultat, et il change la
portée de l'implémentation : le paquet doit être **structurellement** traité (déclaré, lu, borné,
classé), pas **comportementalement** deviné.

Conséquences immédiates, à lire avant d'écrire la moindre ligne :

- 59 n'est pas une réponse à un écran, à un bouton ni à une entrée dans le monde : aucun déclencheur
  n'a été identifié (§5.2).
- 59 n'est pas non plus la réponse au défi 58 : 58 est **serveur → client**, et le client 7.3
  **ne réagit pas** à 58 — sa branche est un cas `switch` explicitement vide (§5.3).
- Il ne faut pas transformer « le client n'y répond pas » en « le paquet est inutile ». L'id 59
  existe dans `op_codes.md`, la trame est entièrement établie par rzu, la valeur est libre dans
  `GamePackets`, et un client patché ou un module tiers peut très bien émettre 59 un jour. Le
  travail utile porte donc sur la **borne d'entrée** (taille, lecture, classification), pas sur une
  réaction métier inventée.

Ce que la fiche **ne** fait pas : choisir ce que le serveur doit faire du tampon (journaliser,
répondre, déconnecter). C'est une décision de gameplay qui appartient à Killian — `## 9. A VERIFIER
PAR KILLIAN`.

---

## 3. Structure sur le fil

En-tête commun au dépôt : `Length` (u32 LE), `ID` (u16 LE), `Checksum` (u8) = 7 octets, somme des
six premiers octets (`Game/Network/Packets/Header.cs:6-25`, `Packet.cs:31` ; côté rzu
`MessageBuffer.h:17-27` et `:73-77`, `PacketDeclaration.h:616-621`).

| Offset | Taille | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` LE | `Length` | **135** (`0x87 0x00 0x00 0x00`) | `TS_CS_XTRAP_CHECK.h:6` + `PacketDeclaration.h:616-621` (base 7) + `:223` (128 × 1 octet) |
| 4 | 2 | `uint16` LE | `ID` | **59** (`0x3B 0x00`) — **1059** (`0x23 0x04`) si ≥ `EPIC_9_6_3`, non retenu ici | `TS_CS_XTRAP_CHECK.h:9-10` |
| 6 | 1 | `uint8` | `Checksum` | **194** (`0xC2`) pour toute trame 59 bien formée : `0x87 + 0x3B` | `MessageBuffer.h:17-27` ; `PacketExtensions.cs:13-27` |
| 7 | 128 | `uint8[128]` | `pCheckBuffer` | contenu **NON ÉTABLI** (§7.2) | `TS_CS_XTRAP_CHECK.h:6` ; NGemity `TS_CS_XTRAP_CHECK.h:7` |
| 135 | — | — | *fin* | — | — |

**Taille totale attendue : 135 octets.** Elle est constante : le tableau est de taille fixe, il n'y a
aucune longueur interne, aucun remplissage optionnel, aucune variante conditionnelle. rzu ne connaît
qu'un seul gating sur ce paquet, celui de l'id (§4).

Le paquet 58 (`TS_SC_XTRAP_CHECK`, serveur → client) a **exactement la même anatomie**, à l'id près :
`Length` 135, `ID` 58, checksum **193** (`0xC1`), puis les mêmes 128 octets. Sources : rzu
`TS_SC_XTRAP_CHECK.h:6,9-10` ; NGemity `TS_SC_XTRAP_CHECK.h:7,9`.

Deux points de méthode, pour que le développeur ne les réinvente pas :

- `_(array)(uint8_t, pCheckBuffer, 128)` est la forme à **trois** arguments : elle se développe en
  `SIZE_F_ARRAY3` (`PacketDeclaration.h:141`, `:223`), soit 128 × `sizeof(uint8_t)` = 128 octets,
  sans condition de version. Aucun `uint16` de longueur ne précède le tableau, contrairement à
  `TM_CS_ANTI_HACK` (54) qui porte un `nLength` — la tentation d'aligner 59 sur 54 serait une erreur.
- La structure est **compacte** (`uint8_t[128]` dans un `#pragma pack` de sérialisation d'octets) :
  aucun alignement, aucun octet de bourrage. Côté NavisLamia, `Header` est déjà
  `[StructLayout(LayoutKind.Sequential, Pack = 1)]` (`Header.cs:6`), donc les offsets 0/4/6/7
  s'appliquent tels quels.

---

## 4. Gating de version — tranché pour l'Epic 7.3

rzu ne définit pas d'id unique mais une **paire** :

| Version | Id | Source |
|---|---|---|
| `version < EPIC_9_6_3` | **59** | `reference/rzu/librzu/src/packets/GameClient/TS_CS_XTRAP_CHECK.h:9` |
| `version >= EPIC_9_6_3` | `1059` | `…:10` |

Bornes : `EPIC_9_6_3 = 0x090603` (`PacketEpics.h:96`), `EPIC_7_3 = 0x070300`
(`PacketEpics.h:59`). `0x070300 < 0x090603` : **l'Epic 7.3 est strictement en dessous du palier**.

**Décision écrite** : pour NavisLamia (client Epic 7.3), l'id est **59**, la taille est **135**,
et **1059 ne doit pas être accepté**. Aucun autre champ du paquet ne porte de gating : le tableau de
128 octets est inconditionnel (`PacketDeclaration.h:223`, forme `SIZE_F_ARRAY3`).

Côté NGemity, le même fichier porte `CREATE_PACKET(TS_CS_XTRAP_CHECK, 59)`
(`reference/ngemity/shared/Server/Packets/GameClient/TS_CS_XTRAP_CHECK.h:9`) : l'id **concorde** avec
la branche 7.3 de rzu, il n'y a donc aucun écart de gating à ce palier. Le paquet 58 suit le même
schéma (`X(58, …)` / `X(1058, …)`).

Réserve de méthode : **ne pas s'appuyer sur l'énumération NGemity pour décider d'un id.**
`ngemity/shared/Server/ClientPackets.h:50-52` place `TS_CS_UNKN = 50`, `TS_CS_VERSION = 51`,
`TS_CS_VERSION2 = 52`, alors que `op_codes.md:33` nomme 50 `TM_CS_VERSION` : la numérotation
NGemity est décalée juste avant la famille anti-triche. Pour 53 à 59 les deux énumérations coïncident
(et `op_codes.md:34-40` le confirme), mais la coïncidence doit être vérifiée à chaque paquet.

---

## 5. Qui émet 59, quand et pourquoi — relevé statique du client Epic 7.3

Binaire analysé : `reference/client73/SFrame.exe`, **9 841 664 octets**, **pei-i386**, sha256
`41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`. `reference/client73` n'est pas un
dépôt git : il n'y a pas de SHA de client à épingler, seules les adresses et le sha256 du fichier le
sont.

Méthode : lecture statique seule (`objdump -d`, lecture brute des sections à l'offset de fichier).
**Aucun `SFrame.exe` n'a été lancé, aucun Lua ni script du client n'a été exécuté.**

### 5.1 L'idiome du constructeur de trame, et ce qu'il aurait fallu trouver

Le client écrit ses trames dans le même buffer que celui du fil : l'objet-paquet porte l'en-tête à
partir de son offset 0. Le constructeur du paquet 57 (`TM_CS_CHECK_ILLEGAL_USER`), relevé sur
`0x6495e0`, est le modèle du genre (sortie d'`objdump -d` recopiée telle quelle) :

```
6495e8:  movl   $0x7,(%eax)        ; longueur = en-tête seul (7)
6495f4:  add    (%esi),%dl …       ; somme des six premiers octets
6495fb:  mov    %dl,(%ecx)         ; checksum en +6
6495ff:  mov    %edx,0x4(%eax)     ; id à zéro
649602:  mov    %dx,0x8(%eax)      ; premier champ du corps
649606:  mov    %dl,0xa(%eax)
649609:  mov    $0x39,%edx
64960e:  mov    %dx,0x4(%eax)      ; ** id = 57 écrit à +4 **
649614:  movl   $0xb,(%eax)        ; longueur finale = 11
649620:  …                         ; recalcul du checksum
```

Un paquet 59 aurait donc nécessairement laissé dans `.text` un `mov $0x3b,%edx` (ou un
`movw $0x3b,0x4(%eax)`, ou un `movl $0x3b,0x4(%eax)`) et un `movl $0x87,(%eax)` de longueur finale.

### 5.2 Le résultat : aucun producteur

Trois relevés indépendants, tous négatifs :

1. **Aucun écrivain d'id 59.** Balayage de `.text` (VMA `0x401000`, taille `0x60a8cf`) sur les
   formes `mov $0x3b,%edx|%ecx|%esi|%ebx`, `movw $0x3b,0x4(%eax|%ecx|%edx)`, `movl $0x3b,0x4(%eax|%edx)` :
   **0 occurrence**. Les deux seuls `mov $0x3b,%eax` de tout le binaire sont
   `0x675ecc` (enregistrement du nom, §5.4) et `0x9407e6` (accesseur sans rapport : renvoie 59 dans
   une fonction de découpage de chaîne, contexte `0x9407c0-0x9407f0`). Le seul `movl $0x3b,0x4(%eax)`
   est `0x4d20d4`, constructeur d'un objet d'IHM (vtable `0xa223ac` en `.rdata` à l'offset 0), sans
   rapport avec une trame.
2. **Aucune longueur de trame à 135.** Le seul `movl $0x87,(%eax)` du binaire est `0x44b8b7`, qui
   alloue 8 octets (`push $0x8; call 0x97671b` = `operator new`) et pose la valeur `0x87` **et** le
   pointeur `0xa121bc` : c'est un enregistrement d'alerte du moteur, pas une trame.
3. **La chaîne de nom n'a qu'une seule référence.** `TM_CS_XTRAP_CHECK` est en `.rdata` à
   `0xa53878` (chaîne voisine `TM_SC_XTRAP_CHECK` à `0xa5388c`). La recherche du pointeur
   `0x00a53878` dans tout `.text` rend **un seul** site, `0x675e99` — l'enregistrement id→nom (§5.4).
   `strings -n 4 SFrame.exe | grep -i xtrap` ne rend que deux lignes, ces deux mêmes noms. Aucun
   chemin de journalisation, aucun chemin d'envoi, aucune DLL à charger ne référence le nom.

Le chemin d'envoi lui-même ferme l'argument : `0x6498e0`, le répartiteur **sortant** du client, lit
la longueur à `(%ebx)` et **l'identifiant à `0x4(%ebx)`** — c'est-à-dire dans l'en-tête de la trame
déjà construite — puis n'appelle la table de noms que pour l'affichage
(`0x6498e0: mov (%ebx),%eax ; movzwl 0x4(%ebx),%ecx ; … call 0x674540`). Il n'existe donc aucun
envoi « par identifiant » : pour émettre un 59, il faudrait impérativement un constructeur écrivant
59 à l'offset +4, et il n'y en a aucun.

Les 78 `push $0x3b` de `.text` ont été examinés : ce sont des `push` suivis de `call 0x97671b`
(`operator new`) — donc des allocations de 59 **octets** — et non des identifiants de paquet. Les
objets construits là portent une autre marque (`movl $0x41b,0x4(%eax)`), sans rapport avec un
en-tête de trame.

### 5.3 Le raisonnement « 59 répond au défi 58 » ne tient pas

Le défi 58 est serveur → client. Pour que le client y réponde par un 59, il faudrait qu'il traite 58.
Son répartiteur **entrant** dit le contraire, et le relevé a été refait ici, indépendamment :

- table d'octets à `0x67f0a0` (251 entrées, premier étage d'ids, indexée par l'id) :
  `[53] = 14`, `[55] = 14`, `[58] = 14`, `[59] = 31` ;
- table de sauts à `0x67f020` (32 entrées) : `slot 14 → 0x67ef39` (cas **vide**, tronc commun de
  sortie), `slot 31 → 0x67ef21` (chemin **par défaut**) ;
- `0x67ef21` pousse `0xa53df0`, une chaîne coréenne qui se décode en « message non traité : %d\n »
  (`\xec\xb2\x98\xeb\xa6\xac\xeb\x90\x98\xec\xa7\x80 \xec\x95\x8a\xec\x9d\x80 \xeb\xa9\x94\xec\x84\xb8\xec\xa7\x80 : %d\n`),
  puis rejoint `0x67ef39` (libération du paquet).

Autrement dit : 58 est **parsé puis ignoré sans effet**, et 59 tomberait sur le journal « message non
traité ». Relevé propre à cette fiche : les identifiants serveur → client testés ici (53, 55, 58)
partagent tous le même cas vide `slot 14`, et 218 des 251 identifiants de la table utilisent l'entrée
par défaut — cohérent avec le constat de la fiche socle (§4.3) selon lequel les identifiants de son
premier étage sont, sans exception, des paquets serveur → client. Un paquet client → serveur comme 59
n'a donc aucune branche fonctionnelle ici : le client ne peut pas réagir à 58, et il n'a aucun moyen
de fabriquer 59 en retour.

**Ce que cela n'établit pas** : que 59 est mort. Un client patché, un client d'une autre région, un
module tiers injecté dans le processus (aucun module anti-triche n'est importé par ce binaire : 21
DLL, ni XTrap, ni GameGuard, ni nProtect, ni XIGNCODE) pourraient l'émettre. La fiche le classe
`NON ÉTABLI` (§7.1) plutôt que de trancher à la place de la réalité.

### 5.4 Reproductibilité

Un paquet 58 ou 59 est **nommé** par le client même s'il n'est jamais émis ni traité : la table
id→nom est construite par des appels successifs à `0x674350` (insertion dans la table située à
`+0xa0`), avec le nom en `.rdata`. Pour 58 et 59, les deux sites sont :

| Id | Nom | Chaîne | Push du nom | Id écrit | Insertion |
|---|---|---|---|---|---|
| 58 | `TM_SC_XTRAP_CHECK` | `0xa5388c` | `0x675e23` | `0x675e56` (`mov $0x3a,%eax`) | `0x675e75` |
| 59 | `TM_CS_XTRAP_CHECK` | `0xa53878` | `0x675e98` | `0x675ecc` (`mov $0x3b,%eax`) | `0x675eeb` |

C'est ce site `0x675ecc` qui explique l'unique référence à la chaîne, et c'est la **seule** occurrence
d'id 59 en tant qu'identifiant de paquet dans tout le binaire.

---

## 6. Traitement attendu

### 6.1 rzu : la déclaration, rien de plus

`reference/rzu/librzu/src/packets/GameClient/TS_CS_XTRAP_CHECK.h` ne contient que la définition de la
structure, le gating d'id et `CREATE_PACKET_VER_ID`. Aucun handler, aucun filtre, aucune conversion :
`grep -rn 'XTRAP' reference/rzu/` ne rend que ce fichier et son pendant 58. Le proxy `rzfilter` ne
liste pas 59 (son `SpecificPacketConverter.cpp:443` traite `TS_CS_CHECK_ILLEGAL_USER`, pas XTrap).

### 6.2 Chihiro (NGemity) : aucune logique non plus

Même constat : `grep -rn 'XTRAP' reference/ngemity/` ne rend que `shared/Server/ClientPackets.h:58-59`,
les deux `#include` dans `shared/Server/XPacket.h:176,323`, et les deux en-têtes. Aucun
`onXtrapCheck`, et l'id 59 n'apparaît **ni** dans la table `worldPacketHandler[]`
(`Chihiro/src/Network/GameNetwork/WorldSession.cpp:92-138`) **ni** dans la liste
`ignoredPackets[]` (`…:140-141`). Il tombe donc dans la branche « paquet inconnu » :

```cpp
// WorldSession.cpp:159-163
if (i == worldTableSize && std::find(..., ignoredPackets) == std::end(ignoredPackets)) {
    NG_LOG_DEBUG("server.network", "Got unknown packet '%d' from '%s'", …);
    return ReadDataHandlerResult::Ok;      // la connexion est conservée
}
```

Lecture : un 59 reçu par NGemity produit une ligne de **DEBUG** et **rien d'autre** — pas de
déconnexion, pas de réponse, pas de compteur. C'est la seule information de comportement que la
référence apporte, et elle est faible : NGemity ignore le paquet parce qu'il ne l'a jamais
implémenté, pas parce qu'il a décidé quelque chose.

### 6.3 NavisLamia aujourd'hui : un 59 est jeté proprement, en silence

`Game/Network/Clients/GameClient.cs:584-588`, dans la boucle de lecture (`:557-582`) :

```csharp
if (!Enum.IsDefined(typeof(GamePackets), header.ID))
{
    _logger.Debug("Undefined packet ID: {id} Length: {length}) received from {clientTag}", …);
    continue;
}
```

Les octets ont été consommés (`Connection.Read(header.Length)` en `:580`, `remainingData -= …` en
`:582`) avant ce test : le datagramme est **absorbé**, la boucle reste alignée, rien n'est renvoyé.
L'identifiant n'étant pas défini dans l'énumération, 59 n'atteint ni la chaîne de `if` d'aiguillage
(`:590` et suivants) ni le `switch` final (`:791-802`, `_ => throw new Exception("Unknown Packet
Type")`). **Toute évolution de ce comportement est une décision**, pas un correctif.

### 6.4 Ce que le serveur doit répondre : rien, en l'état des références

Trois vérifications, chacune suffisante :

1. **Aucun paquet de réponse n'existe.** rzu ne définit que 58 et 59, NGemity de même ; il n'y a pas
   de « result » XTrap dans l'arbre de référence.
2. **Une telle réponse serait un coup dans le vide.** Le client parse 58 puis l'ignore : la branche
   `switch` est explicitement vide (`0x67ef39`, §5.3). Envoyer un 58 en retour serait donc
   syntaxiquement valide et sans effet observable.
3. **Aucune sanction n'est spécifiée.** La liste `DisconnectType` du dépôt contient bien une valeur
   `AntiHack = 101` (`Game/Network/Packets/Enums/DisconnectType.cs:16`), mais rien, dans aucune
   source, ne dit qu'un 59 vaut une déconnexion. Le rapprochement serait une déduction, pas une
   lecture.

Conclusion : **la fiche recommande que le serveur ne réponde rien** et ne tranche **pas** ce qui suit,
qui relève du gameplay : journaliser (à quel niveau ?), réagir (quelle sanction ?), ou ne rien faire
du tout en dehors de la lecture bornée du tampon. Voir §8.

---

## 7. Écarts assumés avec NGemity, et pourquoi

| Point | rzu (7.3) | NGemity / Chihiro | Écart retenu par NavisLamia |
|---|---|---|---|
| Id | 59 (`version < EPIC_9_6_3`), 1059 au-delà | 59, sans gating | 59 seul. Le gating rzu prime (transversal critère 6) ; NGemity ne connaît de toute façon qu'une version. |
| Charge utile | `uint8_t[128]` | `uint8_t[128]` | identique, 128 octets |
| En-tête | 7 octets, `size_base_ = 7` | idem (même macro) | identique → 135 octets |
| Traitement | aucun | aucun (journal DEBUG « unknown packet ») | **écart assumé** : NavisLamia va *plus loin* que les deux références en déclarant l'id et en lisant la trame de façon bornée. Ce n'est pas un portage d'une logique de référence — il n'y en a pas — mais l'application de la discipline du dépôt (`GamePackets` + aiguillage cohérents, transversal critère 4). |
| Énumération | — | `ClientPackets.h:50-52` décale 50/51/52 | ne pas s'en servir pour décider d'un id (§4) |

Aucune « logique NGemity » n'est portée ici, puisqu'il n'y en a pas : la fiche ne comble pas ce vide
par une supposition.

Les trois pièges documentés de `CLAUDE.md` sont écartés explicitement, car ils concernent tous des
paquets à effet d'état : les bits `limit_*` lus à l'envers par la référence (`CLAUDE.md:892`), le
`break` manquant de `SRT_ADD_HP` qui retombe sur `SRT_REBIRTH` (`CLAUDE.md:831`), et les bitfields
dérivés de `limit_*` laissés à zéro (`CLAUDE.md:1097`). 59 ne porte aucun état de personnage, aucun
effet de sort, aucune colonne de base : rien de tel ne s'applique, et la fiche n'emprunte à la
référence aucune mécanique susceptible de la tromper.

---

## 8. NON ÉTABLI

1. **Qui émet 59.** Aucun producteur trouvé dans le client Epic 7.3 livré (§5.2). Ce qui
   l'établirait : un client patché, un dump de session réseau, ou une source INCA/Wizet sur XTrap.
   En l'absence, l'implémentation doit traiter 59 comme un paquet **possible mais non observé**.
2. **Contenu de `pCheckBuffer`.** Aucune source ne dit ce que le tampon contient (jeton d'intégrité,
   horodatage, empreinte de module, matériel ?). Le tableau de 128 octets est sans longueur interne :
   il y a **toujours 128 octets de charge sur le fil**, rien à dérouler, aucune longueur à valider.
   Ce qui l'établirait : une capture, ou la documentation XTrap du client d'origine.
3. **Sort du tampon côté serveur.** Trois questions distinctes, aucune n'est tranchée par une source :
   journaliser (quel niveau, quelle conséquence observable) ? répondre (58 en retour, ou sanction
   existante) ? déconnecter ? §6.4 explique pourquoi une réponse serait aujourd'hui sans effet.
4. **Condition et cadence de déclenchement.** Aucun élément dans le client ne dit quand 59 partirait
   (connexion, entrée en monde, minuterie, événement). Non déductible : pas de producteur.
5. **Comportement d'un client modifié.** Un binaire tiers pourrait émettre 59 sans passer par les
   constructeurs relevés. Non vérifiable par lecture statique du client livré.
6. **Raison des 128 octets fixes.** Aucune source n'explique la taille. À ne pas confondre avec le
   `nLength` de `TM_CS_ANTI_HACK` (54) : 59 n'a pas de champ de longueur.
7. **Lien sémantique 58 ↔ 59.** L'appariement (défi/réponse ? vérification périodique ?) est une
   hypothèse de lecture des noms ; aucune source ne le formalise.

---

## 9. A VERIFIER PAR KILLIAN

Décision de gameplay, à porter telle quelle dans la MR :

| # | Question | Options | Ce qui change |
|---|---|---|---|
| 1 | Que fait le serveur du tampon `pCheckBuffer` ? | (a) rien, lecture bornée seulement ; (b) journal au niveau `Debug`/`Verbose` avec la seule longueur, jamais le contenu ; (c) journal du contenu | (a) est le minimum vérifiable ; (b) et (c) engagent de la volumétrie et de la vie privée (contenu opaque, potentiellement matériel) |
| 2 | Le serveur répond-il quelque chose ? | (a) rien ; (b) 58 en retour ; (c) sanction via un paquet existant | (b) est **sans effet** d'après §5.3 et §6.4 : le client ignore 58 ; (c) exige un paquet du dépôt et une règle |
| 3 | Le serveur déconnecte-t-il sur 59 ? | (a) non ; (b) `DisconnectType.AntiHack` (101) | (b) coupe un joueur sur un paquet dont **on ne sait pas évaluer le contenu** — un faux positif coûte un client |
| 4 | Faut-il un garde d'ordre (avant/après entrée en monde) ? | (a) aucun ; (b) refuser avant `CharacterHandle != 0` | le dépôt connaît l'idiome (`GameClient.cs:184-189` pour 550) |
| 5 | Faut-il un limiteur de fréquence ? | (a) aucun ; (b) un par connexion | aucune référence n'en a ; une rafale de 135 octets est bon marché |
| 6 | Faut-il déclarer **58** aussi (`TM_SC_XTRAP_CHECK`) ? | (a) non, hors périmètre ; (b) oui, pour la symétrie de `GamePackets` | la carte porte sur 59 ; déclarer 58 sans jamais l'émettre ajoute un membre d'énumération inerte, et agrandit la zone de collision avec le socle anti-triche |

Aucune de ces six questions n'est tranchée par la fiche. Les valeurs par défaut proposées en (a) sont
celles du **minimum vérifiable** : elles n'engagent aucune politique.

---

## 10. Cas limites à inventorier (et ce que le dépôt fait déjà)

1. **Length < 135** (par exemple 7, ou 11 comme le paquet 57). Un lecteur naïf qui recopie
   `Marshal.SizeOf<T>()` octets depuis l'offset 7 sans vérifier `Length` lèverait une
   `ArgumentException` (`Packet.cs:39-42` puis `:81-82`). L'idiome correct du dépôt est celui de
   `GameActionPackets.TryReadGetRegionInfo` (`Game/Network/Packets/Game/GameActionPackets.cs:237-255`) :
   contrôle de longueur **exacte**, puis primitives `BinaryPrimitives` sur `ReadOnlySpan<byte>`, sans
   marshalling.
2. **Length > 135.** `Connection.Read((int)header.Length)` (`GameClient.cs:580`) lit tout ce qui est
   annoncé : un 59 allongé serait consommé et le flux resterait aligné, mais le surplus serait
   silencieusement accepté. Le dépôt a déjà tranché ce cas pour 550 (« the specification defines no
   answer at all for a request of another length, so a short or padded one is refused rather than
   partially read », `GameActionPackets.cs:233-234`) : le même raisonnement s'applique ici.
3. **Tampon entièrement nul.** Rien ne permet aujourd'hui de distinguer « pas de tampon » d'un
   tampon nul légitime : c'est une question de politique (§9.1), pas de format.
4. **Paquet reçu avant l'entrée en monde.** Aucun état de session n'est vérifié à `:584` ; l'idiome
   `ConnectionInfo.CharacterHandle == 0` existe pour 550 (`GameClient.cs:184-189`).
5. **Répétition ou rafale.** Aucun compte par connexion dans le dépôt pour ce type de paquet ; aucune
   référence n'en fournit.
6. **Paquet reçu d'un client en cours de déconnexion.** Rien à attendre : 59 n'a aucun effet de bord
   observable tant que §9.2 et §9.3 restent en (a).

---

## 11. Où le traitement s'insérerait, et zone de collision

- **Chaîne d'aiguillage.** Le traitement d'un 59 s'insère dans la chaîne
  `if (header.ID == (ushort)GamePackets.X) { …; continue; }` qui court de `GameClient.cs:590` au
  `switch` final `:791-802`. Le `switch` final lève `Unknown Packet Type` sur tout membre non traité
  (transversal critère 4) : **l'énumération et l'aiguillage doivent bouger ensemble**.
- **Table d'actions.** `Game/Network/Clients/Actions/GameActions.cs:32` porte un
  `Dictionary<ushort, Action<GameClient, IPacket>> _actions`, alimenté en `:43-50` et consulté en
  `:55` ; c'est une seconde voie d'insertion, réservée aux paquets qui portent un `IPacket` typé.
  Si 59 est traité à la main dans la chaîne de `if`, la table n'a pas à bouger.
- **Précédent utile.** `TM_SC_REGION_ACK` (serveur → client reçu par le serveur) est journalisé en
  `Warning` puis jeté explicitement (`GameClient.cs:620-628`) : c'est le modèle d'un paquet
  « direction anormale » traité sans effet de bord. 58 relèverait exactement de ce cas si Killian
  choisit de le déclarer (§9.6).
- **Nom de fichier cohérent.** Les lecteurs défensifs vivent dans
  `Game/Network/Packets/Game/Game*Packets.cs` (`GameActionPackets.cs`, `GameMovePackets.cs`, …) avec
  un test jumeau dans `Tests/Game/`. Un fichier `GameXtrapPackets.cs` (ou l'équivalent retenu par le
  socle anti-triche, `GameAntiHackPackets.cs`) serait le voisin naturel.
- **Zone de collision — à signaler, pas à arbitrer ici.** Deux branches **non fusionnées**
  (`master` = `ec76b21`) modifient **les deux mêmes fichiers** que cette fiche désigne :
  `hermes/packet-socle-anti-triche` (MR #9 ouverte) et `hermes/packet-57-check-illegal-user`
  (MR #15 ouverte) touchent toutes deux `Game/Network/Packets/Enums/GamePackets.cs` et
  `Game/Network/Clients/GameClient.cs` (membre d'énumération + bras d'aiguillage). Trois branches
  concurrentes sur les mêmes points d'insertion : l'ordre de fusion de Killian décidera des conflits,
  et la fiche recommande de ne rien toucher à `GamePackets.cs` au-delà du seul membre 59.

---

## 12. Commits et binaires épinglés

| Référence | Identifiant épinglé | Usage dans cette fiche |
|---|---|---|
| `reference/rzu` | **`87c1e83bf84efe29bb6405e8e6da80349712f3fa`** | taille, ordre, gating, absence d'handler |
| `reference/ngemity` | **`38ceb2c6065fabf6ff4ba71d52f955f362c6c839`** | id 59 confirmé, absence d'handler, comportement « paquet inconnu » |
| `reference/client73` | pas de dépôt git — `SFrame.exe`, 9 841 664 octets, pei-i386, sha256 **`41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`** | répartiteur entrant, table de noms, absence de producteur |
| NavisLamia | base de branche `master` = **`ec76b218cd0bd7c6498d725f253abb8b431f0cd6`** | état du dépôt, lignes citées |
| Fiche socle | `git show origin/hermes/packet-socle-anti-triche:docs/packet-specs/socle-anti-triche.md` (§7.3, §4.2, §4.3) | point de départ, cité et non refait |

### 12.1 Emplacements relevés, pour re-vérification

| Objet | Adresse (VMA) | Comment le revoir |
|---|---|---|
| Enregistrement id→nom de 59 | `0x675e98` (nom) / `0x675ecc` (id) / `0x675eeb` (insertion) | `objdump -d --start-address=0x675e00 --stop-address=0x675f00 SFrame.exe` |
| Enregistrement id→nom de 58 | `0x675e23` / `0x675e56` / `0x675e75` | idem |
| Insertion dans la table de noms | `0x674350` | `objdump -d --start-address=0x674350 …` |
| Résolution de nom par id | `0x674540` | idem |
| Répartiteur **sortant** (lit l'id à `+4` de la trame) | `0x6498e0` | `objdump -d --start-address=0x6498e0 --stop-address=0x649a00 …` |
| Constructeur du paquet 57 (contraste) | `0x6495e0`, id écrit en `0x649609` puis `0x64960e` | idem |
| Table de premier étage entrant | `0x67f0a0` (251 octets), index 58 → 14, index 59 → 31 | lecture brute à l'offset de fichier `VMA − 0x401000 + 0x400` |
| Table de sauts entrant | `0x67f020`, `slot 14 → 0x67ef39` (vide), `slot 31 → 0x67ef21` (défaut) | idem |
| Chemin par défaut / chaîne « message non traité » | `0x67ef21` / `0xa53df0` | `objdump -d --start-address=0x67ef21 …` |

---

## 13. Bloc destiné à `CLAUDE.md` (pour la description de MR de `navis-dev`)

> **TM_CS_XTRAP_CHECK (59)** — client → serveur, 135 octets : en-tête 7 (`Length` 135, `ID` 59,
> checksum 194) + `pCheckBuffer` `uint8_t[128]`, **sans champ de longueur**. Gating rzu tranché pour
> l'Epic 7.3 : **59**, jamais 1059 (`EPIC_9_6_3 = 0x090603` > `EPIC_7_3 = 0x070300`). La paire est
> 58 (`TM_SC_XTRAP_CHECK`, serveur → client, même anatomie, checksum 193) / 59.
> **Aucun producteur de 59 dans le client 7.3** (aucun constructeur d'id 59, chaîne de nom référencée
> une seule fois, `SFrame.exe` sha256 `41e0af2e…`) et **aucun handler dans les deux serveurs de
> référence** : le traitement du tampon est une décision, pas un portage. Le client 7.3 parse 58 dans
> une branche `switch` vide — toute réponse serait sans effet. Voir
> `docs/packet-specs/59-xtrap-check.md`.

---

## 14. Plan de vérification (pour `navis-qa`)

1. La fiche est le seul fichier ajouté ou modifié : `git diff --stat master...hermes/packet-59-xtrap-check`
   ne doit montrer que `docs/packet-specs/59-xtrap-check.md`.
2. `dotnet build Navislamia.sln -c Debug` code 0 et `dotnet test Tests/Tests.csproj` code 0, au moins
   366 tests (la fiche n'ajoute aucun test : le travail de code est celui de la carte enfant).
3. `git log --oneline origin/master..master` vide (aucun commit sur `master`).
4. Recouper les quatre adresses décisives du client (§12.1) : `0x675ecc` (`mov $0x3b,%eax`),
   absence de `mov $0x3b,%edx`/`movw $0x3b,0x4(%eax)` dans `.text`, `0x67f0a0[58] = 14` et
   `0x67f0a0[59] = 31`.
5. Vérifier que la fiche ne propose aucune politique serveur en dehors de `## NON ÉTABLI` et de
   `## A VERIFIER PAR KILLIAN` : c'est le point sensible de cette carte.

---

## 15. Note de livraison

- Fiche produite par `navis-ref` ; **aucun code serveur n'a été modifié**, aucun test ajouté.
- Le dossier `docs/packet-specs/` **existe déjà sur `master`** (`ec76b21`) et est autorisé par
  `.gitignore:473` (`!/docs/packet-specs/`) : la prémisse « à créer » de la carte est périmée, sans
  conséquence sur le livrable.
- Les fiches des MR #9 (`socle-anti-triche`) et #15 (`57-check-illegal-user`) ne vivent que sur leurs
  branches : cette fiche cite la première, jamais copiée.
- Les points `NON ÉTABLI` transmis au développement sont les sept de §8 ; les décisions attendues de
  Killian sont les six de §9.

---

## 16. Implémentation livrée par `navis-dev`

Branche `hermes/packet-59-xtrap-check`, poursuivie depuis la fiche. Quatre fichiers touchés :

| Fichier | Nature du changement |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | membre `TM_CS_XTRAP_CHECK = 59`, commenté (gating, absence de 58 et de 1059) |
| `Game/Network/Packets/Game/GameXtrapPackets.cs` | **nouveau** : `XtrapCheckPacketSize` (135), `XtrapCheckBufferSize` (128), `XtrapCheckBufferOffset` (7), `TryReadXtrapCheck` |
| `Game/Network/Clients/GameClient.cs` | `HandleXtrapCheck` (placé après `HandleGetRegionInfo`) et le bras d'aiguillage, juste avant le `switch` final |
| `Tests/Game/XtrapCheckPacketsTests.cs` | 21 tests : offsets, bornes, aiguillage |

### 16.1 Ce que fait le code

- `TryReadXtrapCheck(ReadOnlySpan<byte> packet, out ReadOnlySpan<byte> checkBuffer)` : contrôle de
  **longueur exacte** (135), puis vue sur les octets de l'appelant à partir de l'offset 7 sur 128 octets.
  Zéro copie, zéro interprétation : le tampon est rendu tel quel. Un rappel de l'idiome du dépôt (§10.1)
  est appliqué : aucune lecture partielle, aucun marshalling.
- Le bras d'aiguillage ne fait que ça — lire et jeter. Aucune réponse, aucune sanction, aucun état de
  session touché, aucun garde d'ordre, aucun limiteur.
- Le `switch` final reste inchangé : le membre 59 ne peut pas l'atteindre (transversal critère 4).

### 16.2 Décisions prises, et sur quelle base

Les six questions de §9 sont tranchées aux valeurs par défaut **(a)** de la fiche, sauf la première qui
suit l'idiome déjà en place :

| §9 | Choix retenu | Justification |
|---|---|---|
| 1 — sort du tampon | lecture bornée + **une** ligne `Debug` portant l'id, la longueur et la taille du tampon ; **jamais** le contenu | c'est exactement ce que l'id non déclaré produisait déjà (`GameClient.cs:586`, « Undefined packet ID: {id} Length: {length} ») : le niveau et l'information observable sont identiques, donc aucune politique nouvelle n'est introduite. Passer à (a) strict, c'est supprimer l'appel `_logger.Debug` du handler ; passer à (c) — le contenu — n'est **pas** fait et reste interdit par défaut (tampon opaque, possiblement matériel) |
| 2 — réponse | rien | §6.4 : aucun paquet de réponse n'existe, et 58 serait un coup dans le vide (branche vide du client) |
| 3 — déconnexion | non | `DisconnectType.AntiHack` existe mais rien ne l'associe à 59 ; couper un client sur un tampon qu'on ne sait pas évaluer serait un faux positif |
| 4 — garde d'ordre | aucun | aucun effet de bord, donc rien à protéger ; l'idiome 550 n'a pas lieu d'être ici |
| 5 — limiteur de fréquence | aucun | aucune référence n'en a ; 135 octets par trame |
| 6 — déclarer 58 | non | le paquet n'est jamais émis par ce serveur ; l'énumération reste minimale |

Sur §9.6, précision utile pour la relecture : si Killian veut la symétrie plus tard, la place naturelle est
`GameXtrapPackets` (les deux ids partagent la même anatomie) et un bras « paquet serveur → client reçu par
le serveur » calqué sur celui de `TM_SC_REGION_ACK` (`GameClient.cs:620-628`).

### 16.3 Écarts et points ouverts

- Le fichier de lecture s'appelle `GameXtrapPackets.cs`, pas `GameAntiHackPackets.cs` : ce dernier nom est
  **créé par la branche non fusionnée `hermes/packet-socle-anti-triche`** (MR #9). Un second fichier
  `GameAntiHackPackets.cs` produirait un conflit *add/add* à la fusion ; `GameXtrapPackets.cs` n'existe sur
  aucune branche.
- **Zone de collision signalée** (`hotspot`) : `Game/Network/Packets/Enums/GamePackets.cs` et
  `Game/Network/Clients/GameClient.cs` sont modifiés par trois branches ouvertes (celle-ci, MR #9
  anti-triche, MR #15 paquet 57). Ici les insertions sont volontairement minimales — un membre d'énumération
  et un bras — et placées là où les deux autres branches insèrent déjà (§11) : les conflits attendus à la
  fusion sont textuels et adjacents.
- Les sept points `NON ÉTABLI` de §8 restent **non établis** : rien dans l'implémentation ne les présume.
  En particulier, aucun test n'affirme quoi que ce soit du contenu de `pCheckBuffer`.
- Un id **1059** n'est pas déclaré : une telle trame suit le chemin « id non déclaré » existant
  (`GameClient.cs:584-588`), qui la consomme entièrement et la journalise en `Debug` sans rien renvoyer.

### 16.4 Vérification exécutée

- `dotnet build Navislamia.sln -c Debug` → code 0, 0 erreur.
- `dotnet test Tests/Tests.csproj` → code 0, **469 tests passés**, 0 échec (448 avant le paquet, +21).
- Tests d'offsets livrés : taille totale constante 135 (et 7 + 128 vérifié), chaque octet de charge à son
  offset absolu `7 + i` (dernier à 134), en-tête relu à 0/4/6, checksum 194 pour 59 et 193 pour 58, refus de
  toute longueur autre que 135 (0, 7, 11, 134, 136, 143), et boucle de réception : consommation sans
  exception, aucune réponse émise, alignement conservé sur une trame coalescée, trame malformée avalée.

### 16.5 Bloc destiné à `CLAUDE.md` (version `navis-dev`, à porter dans la description de MR)

> **TM_CS_XTRAP_CHECK (59)** — client → serveur, **135 octets** : en-tête 7 (`Length` 135, `ID` 59,
> checksum 194) + `pCheckBuffer` `uint8[128]` à l'offset 7, **sans champ de longueur**. Gating rzu tranché
> pour l'Epic 7.3 : **59**, jamais 1059 (`EPIC_9_6_3 = 0x090603` > `EPIC_7_3 = 0x070300`) ; la paire est
> 58 (`TM_SC_XTRAP_CHECK`, même anatomie, checksum 193), **non déclarée** car ce serveur ne l'émet jamais.
> **Aucun producteur de 59 dans le client 7.3** (`SFrame.exe` sha256 `41e0af2e…` : aucun constructeur
> d'id 59) et **aucun handler dans rzu ni NGemity** : il n'y a pas de logique à porter. Lecture défensive
> seule (`GameXtrapPackets.TryReadXtrapCheck` : longueur exacte, tampon rendu intact) puis abandon : **pas
> de réponse, pas de sanction, pas de déconnexion**, le contenu de `pCheckBuffer` n'étant **pas établi** et
> n'étant jamais journalisé. `TM_SC_XTRAP_CHECK` (58) non déclaré ; le membre 59 est aiguillé avant le
> `switch` final qui lève `Unknown Packet Type`. Voir `docs/packet-specs/59-xtrap-check.md`.

