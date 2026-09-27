# 4004 — `TM_CS_HUNTAHOLIC_JOIN_INSTANCE`

Suivi : `navislamia:packet:4004` ; carte Trello **`U5sdseAa`**
(`https://trello.com/c/U5sdseAa/61-tmcshuntaholicjoininstance-4004`) — shortLink repris de la carte
Hermes `t_188aed49`, non lu via Trello (ce rôle n'a pas d'accès MCP Trello). Lot **S4** du socle
`docs/packet-specs/socle-instances-jeu.md` (ligne 479 : « Lobby HuntaHolic — créer / rejoindre »).

Fiche sœur pour le paquet jumeau : `docs/packet-specs/4003-huntaholic-create-instance.md` (même lot
S4, même branche de famille, mêmes conclusions sur ce que le client attend en retour).

Méthode : rzu tranche la taille, l'ordre des champs et le gating ; NGemity tranche la logique
(résultat : il n'en a aucune) ; le client Epic 7.3 tranche en dernier ressort. `SFrame.exe` a été
désassemblé localement (`objdump -d -M intel`, 2 256 750 lignes) et `db_string.rdb` lu comme fichier
de données. **Aucun code serveur n'est écrit ni modifié par cette fiche, et aucun binaire, Lua ou
script du client n'a été exécuté.**

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id | **4004** (`0x0fa4`) | `op_codes.md:230` — `[4004] = "TM_CS_HUNTAHOLIC_JOIN_INSTANCE"` |
| Nom | `TM_CS_HUNTAHOLIC_JOIN_INSTANCE` | `op_codes.md:230` |
| Nom rzu / NGemity | `TS_CS_HUNTAHOLIC_JOIN_INSTANCE` | rzu `TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:12` ; NGemity `TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:10` |
| Sens | **client → serveur** (`SessionPacketOrigin::Client`) | rzu `TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:12` ; NGemity `ClientPackets.h:237` |
| En-tête | 7 octets (`Length` u32, `ID` u16, `Checksum` u8) | rzu `PacketDeclaration.h:616-621` (en-tête de 7) ; `Game/Network/Packets/Header.cs:9-11` |
| **Taille totale** | **28 octets** (`0x1c`) | client `0x4c91cc` (voir §3.2) ; rzu 4 + 17 + 7 |
| État dans Navislamia (`master`) | **absent** de `GamePackets` | `grep -n "4004" Game/Network/Packets/Enums/GamePackets.cs` → aucun résultat |

C'est le paquet « **rejoindre une salle** » du lobby HuntaHolic : le pendant de `4003` (créer). Le
joueur choisit une salle dans la liste reçue par `4001` et le client demande à y entrer.

---

## 2. Ce que le joueur fait pour que le client l'envoie

Enchaînement établi par démontage ; les deux sites d'appel du constructeur sont exactement ceux du
socle (`socle-instances-jeu.md:119`), mais le déclencheur exact de chacun est précisé ici.

1. Le joueur ouvre le lobby HuntaHolic : le client envoie `4000` et reçoit `4001`
   (`TM_SC_HUNTAHOLIC_INSTANCE_LIST`), dont chaque entrée est un `TS_HUNTAHOLIC_INSTANCE_INFO` de
   **38 octets** dont le dernier octet est `require_password` (`socle-instances-jeu.md:279-291`).
2. Il sélectionne une salle. La méthode de sélection est la fonction `0x4c9250-0x4c9374` (méthode de
   `SHuntaHolicSystem` : session en `[ebx+0x20]`, index de la salle en `[ebp+0x8]`) :
   - contrôle de bornes contre le tableau d'entrées (`0x4c927b-0x4c9294` : la taille est
     `([ebx+0x4c] - [ebx+0x48]) / 38`, division par 38 par la magie `0x6bca1af3` et le décalage `4`) ;
   - copie de l'entrée sélectionnée vers l'état du système : `imul esi,esi,0x26` (`0x4c929a`, pas de
     **38**) puis `rep movs` de 9 `dword` + 1 `word` (`0x4c92a0-0x4c92aa`) vers `[ebx+0x58]` ;
   - **aiguillage sur `require_password`** : `cmp BYTE PTR [ebx+0x7d],0x0` (`0x4c92ac`) — `0x7d` est
     `0x58 + 37`, soit l'offset 37 de l'entrée, celui de `require_password` (`0x4c92b0` : `je`).
3. **Salle publique (`require_password == 0`)** → saut direct au **site A** `0x4c9339-0x4c935f`
   (voir §3.2). Le client envoie `4004` **immédiatement**, avec un champ mot de passe entièrement
   remis à zéro (`xor eax,eax` puis 4 `mov dword` + 1 `mov byte` en `0x4c9347-0x4c9355`).
4. **Salle protégée (`require_password != 0`)** → le client **ne construit pas le paquet** : il ouvre
   la fenêtre de mot de passe (`0x4c92b6-0x4c92e4` : `lea ecx,[ebx+0xe0]`, `call 0x4c7a00`, puis
   message *`setCallBack`* — `ds:0xc4b4e0`, dont la globale est initialisée en `0x9fe9a0-0x9fe9b5`
   à partir de la chaîne `.rdata 0xa202b0` — et appel virtuel `[eax+4]`). Quand la fenêtre rend la
   main, **site B** `0x4c9420-0x4c9486` construit et envoie `4004` avec le mot de passe saisi
   (voir §3.3).

Le handler du site B traite un message de fenêtre par identifiant comparé à des globales `.data`
elles aussi initialisées paresseusement, chacune depuis le nom du message : `ds:0xc4b4e0` ←
`.rdata 0xa202b0` (init en `0x9fe9a0-0x9fe9b5`), `ds:0xc4b4d4` ← `.rdata 0xa202bc` (init en
`0x9fe9c0-0x9fe9d5`), `ds:0xc4b4b8` ← `.rdata 0xa202d0` (init en `0x9fe9e0-0x9fe9f5`). Les chaînes
lues dans `.rdata` (offsets fichier `0x61ecb0`, `0x61ecbc`, `0x61ecd0`) nomment ces messages :
**`setCallBack`** (→ `0xc4b4e0`), **`onCreateInstance`** (→ `0xc4b4d4`) et
**`onConfirmPassword`** (→ `0xc4b4b8`). Autrement dit :

- le site B est le chemin **`onConfirmPassword`** — c'est le seul site du client qui envoie `4004`
  avec un mot de passe non vide ;
- la branche voisine `onCreateInstance` (`0x4c9434-0x4c944c`) ne construit **rien** : elle renvoie le
  tampon fourni par l'appelant (`[lparam+4]`), ce qui l'identifie au chemin de `4003` (dont le
  constructeur `0x563c20` est décrit par la fiche sœur) et **pas** à `4004`.

Aucun troisième site n'existe : `mov ecx,0xfa4` n'apparaît **qu'une fois** dans tout le démontage
(`0x4c91c1`, le constructeur de §3).

---

## 3. Structure sur le fil

### 3.1 Table des champs — **28 octets**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0 | `uint32` LE | `Length` | **`0x1c` (28)** écrit en dur | client `0x4c91cc` (`mov DWORD PTR [eax],0x1c`), confirmé par `sub esp,0x1c` du site B (`0x4c9426`) ; `Header.cs:9` |
| 4 | `uint16` LE | `ID` | **`0x0fa4` (4004)** | client `0x4c91c1` (`mov ecx,0xfa4`) → `0x4c91c6` (`mov WORD PTR [eax+0x4],cx`) ; `op_codes.md:230` |
| 6 | `uint8` | `Checksum` | somme des octets 0…5, modulo 256 | client, boucle `0x4c91a0-0x4c91ab` puis réécriture `0x4c91d4-0x4c91df` ; `PacketExtensions.cs:13-25` |
| 7 | `int32` LE | `instance_no` | numéro de la salle sélectionnée ; côté client c'est **le premier `dword` de l'entrée de 38 octets** mémorisée en `[systeme+0x58]` | rzu `TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:6` ; client site A `0x4c935c` (`mov DWORD PTR [ebp-0x45],edx`, base `[ebp-0x4c]`) et site B `0x4c9469` (base `[ebp-0x1c]`) |
| 11 | `char[17]`, NUL final **inclus** | `password` | tampon **fixe** de 17 octets : 17 zéros si `require_password == 0`, sinon copie bornée à 17 octets du texte saisi | rzu `TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:7` ; client site A `0x4c9349-0x4c9355` (zéro), site B `0x4c946f-0x4c9472` (`push 0x11` + copie bornée) |

7 + 4 + 17 = **28**. Le dernier octet du champ mot de passe est l'octet 27, soit exactement le
dernier octet de la trame.

### 3.2 Concordance des trois sources, avec les preuves de taille

**Constructeur client `0x4c9190-0x4c91e2`** (désassemblé dans cette session) :
`mov DWORD PTR [eax],0x7` (`0x4c9198`) → `Length` provisoire de 7 ; boucle de checksum sur les six
premiers octets (`0x4c91a0-0x4c91ab`) ; remise à zéro de `[eax+0x4]` à `[eax+0x1b]` par six `mov`
de `dword` (`0x4c91af-0x4c91be`, soit 24 octets = `ID` + checksum + charge utile) ; `mov ecx,0xfa4`
(`0x4c91c1`) et écriture de l'id (`0x4c91c6`) ; **`mov DWORD PTR [eax],0x1c`** (`0x4c91cc`) ;
recalcul et réécriture du checksum (`0x4c91d2-0x4c91df`) ; `ret` (`0x4c91e2`).

**Trois preuves indépendantes de la taille 28** :

1. la constante de longueur écrite par le constructeur (`0x1c`, `0x4c91cc`) ;
2. l'emplacement réservé par le site B : `sub esp,0x1c` (`0x4c9426`) puis `lea ecx,[ebp-0x1c]`
   (`0x4c9457`) — le tampon du paquet occupe exactement `[ebp-0x1c .. ebp-0x1]` ;
3. au site A, la base est `[ebp-0x4c]` et le dernier octet zéro du mot de passe est `[ebp-0x31]`
   (`0x4c9355`) : `0x4c - 0x31 + 1 = 28`.

**Position des deux champs** : elle est écrite par le client lui-même, deux fois de façon
indépendante. Site A (`0x4c9339`) : base `[ebp-0x4c]`, `instance_no` en `[ebp-0x45]` (= base + 7) et
mot de passe remis à zéro de `[ebp-0x41]` à `[ebp-0x31]` (= base + 11, longueur 17). Site B
(`0x4c9457`) : base `[ebp-0x1c]`, `instance_no` en `[ebp-0x15]` (= base + 7), destination de la copie
bornée `[ebp-0x11]` (= base + 11) avec `push 0x11` (= 17).

### 3.3 Sémantique des chaînes

`_(string)(password, 17)` est un **tampon de taille fixe** : `MessageBuffer::writeString`
(`rzu/librzu/src/lib/Packet/MessageBuffer.cpp:87-94`) copie `min(taille, 16)` caractères et remplit
le reste de zéros, puis avance de 17 octets. Conséquences, toutes vérifiées côté client :

- la trame fait **toujours 28 octets**, mot de passe vide ou non : le champ n'est jamais omis et
  `Length` n'est jamais autre chose que `0x1c` ;
- un mot de passe de 16 caractères remplit le champ sans NUL terminal ; 17 est la borne de la copie
  (`push 0x11`, `0x4c946f`), donc **au plus 16 caractères** ;
- **mot de passe absent = 17 zéros** (site A) : c'est la seule forme que le client produit pour une
  salle publique. Un lecteur serveur ne peut pas distinguer « pas de mot de passe » d'un « mot de
  passe vide » autrement que par « les 17 octets sont nuls ».

### 3.4 Forme du test d'offsets attendu

Sur `byte[]` construits à la main, sans socket :

1. `Length` = 28 → `TryRead` renvoie `true` ; `TryRead` refuse 7, 11, 27, 29 et 55 ;
2. `instance_no` (int32 signé, offset 7) : valeurs `0`, `1`, `-1`, `int.MinValue`, `int.MaxValue`
   avec les quatre octets attendus à l'offset 7 (petit-boutiste) ;
3. `password` : tampon de 17 octets à l'offset 11, lu comme tel — 17 zéros → « pas de mot de passe » ;
   `"abc"` + NUL + zéros → longueur 3 ;
4. absence de NUL sur les 17 octets : la trame est **refusée** (le client ne peut pas la produire,
   cf. §5.4) ;
5. taille totale figée : `HeaderSize + 4 + 17 == 28`.

---

## 4. Gating de version

**Le paquet lui-même n'est pas gaté, et aucun de ses champs ne l'est.** Le gating annoncé par la
carte (`X(4004, version < EPIC_9_6_3)` d'un côté, un id haut au-delà du seuil de l'autre) **n'existe
pas dans rzu** : le fichier ne contient **aucune seconde entrée `X(...)`**, il n'y a donc **aucun id
haut à écarter** et aucune variante à nommer. La source réelle dit : `TS_CS_HUNTAHOLIC_JOIN_INSTANCE`
porte `X(4004, true)` — **une seule entrée, dont la condition de version est le littéral `true`**
(`TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:9-10`). La macro est explicite :
`SERIALISATION_F_ID2(id_, condition_)` = `if(condition_) id = id_;` (`PacketDeclaration.h:587-589`),
donc `true` ⇒ l'id vaut 4004 pour **toutes** les versions, Epic 7.3 inclus
(`PacketEpics.h:59` : `EPIC_7_3 = 0x070300`, mais la condition ne le mentionne même pas). Aucune
variante d'id, aucune variante de champ.

| Question | Verdict pour Epic 7.3 | Source |
|---|---|---|
| L'id est-il gaté ? | **Non** : `X(4004, true)`, entrée unique. La variante « id bas / id haut selon la version » **n'existe pas** pour 4004 et **ne doit pas être inventée** | rzu `TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:9-10`, `PacketDeclaration.h:587-589`, `PacketEpics.h:59` (`EPIC_7_3 = 0x070300`) |
| Un champ de la charge utile est-il gaté ? | **Non** : le `_DEF` (lignes 5-7) ne contient aucun `#if EPIC_*` ni entrée conditionnelle ; `instance_no` et `password` sont inconditionnels | rzu `TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:5-7` |
| Le client 7.3 construit-il cette forme ? | **Oui** : `Length = 0x1c`, `ID = 0xfa4`, tampon de 28 octets, deux sites d'appel (§2, §3.2) | client `0x4c9190`, `0x4c9339`, `0x4c9457` |
| NGemity est-il d'accord ? | **Oui**, à l'identique (`CREATE_PACKET(TS_CS_HUNTAHOLIC_JOIN_INSTANCE, 4004)`, mêmes deux champs) | NGemity `TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:6-10` |

**En revanche, le gating de version existe pour la *réponse* — et c'est là le piège de cette fiche.**
`TS_SC_RESULT`, le seul paquet de résultat identifié (§5.2), est le paquet gaté du lot :

```
X(0,    version <  EPIC_9_6_3)      # rzu TS_SC_RESULT.h:13
X(1000, version >= EPIC_9_6_3)      # rzu TS_SC_RESULT.h:14
```

`EPIC_9_6_3 = 0x090603` (`PacketEpics.h:96`) et `EPIC_7_3 = 0x070300` (`PacketEpics.h:59`) :
**pour Epic 7.3 l'id de `TM_SC_RESULT` est 0**, pas 1000. Cette décision est doublement confirmée
hors rzu — NGemity `CREATE_PACKET(TS_SC_RESULT, 0)` et `ClientPackets.h:23` (`TS_SC_RESULT = 0`), et
Navislamia lui-même : `GamePackets.cs:5` porte déjà `TM_SC_RESULT = 0`. **Il ne faut pas utiliser
1000** : c'est l'id de la branche ≥ 9.6.3.

---

## 5. Traitement attendu

### 5.1 Ce que les deux références serveur en font : **rien**

| Référence | Traitement de 4004 | Source |
|---|---|---|
| rzu | **Aucun code serveur.** `TS_CS_HUNTAHOLIC_JOIN_INSTANCE` n'apparaît qu'à sa déclaration dans `librzu/src/packets/GameClient/` : `grep -rln "HUNTAHOLIC" reference/rzu/` ne sort que des fichiers de `librzu/src/packets/` (+ `.git/index`), **aucun** fichier de `reference/rzu/rzgame/` ; le seul `X(4004` du dépôt est dans le `.h` du paquet | `grep -rn "X(4004" reference/rzu/librzu/src/` → 1 résultat ; commit rzu `87c1e83` |
| NGemity | **Aucun handler.** 4004 n'existe que comme énumération (`ClientPackets.h:237`), `#include` (`XPacket.h:109`) et déclaration (`TS_CS_HUNTAHOLIC_JOIN_INSTANCE.h:10`). Les occurrences de « HUNTAHOLIC » dans `Chihiro/src` sont toutes ailleurs et hors du lobby : (`a`) types et ressources — `ItemTemplate.hpp:84,151,200,218`, `Unit.h:86`, `MonsterBase.h:65-68`, `GroupManager.h:29` (commenté), `GameContent.cpp:134` ; (`b`) compétences — `SkillBase.h:224`, `Skill.cpp:1422-1423` (`SKILL_WARP_TO_HUNTAHOLIC_LOBBY`), `StateBase.h:30` ; (`c`) point de personnage et ratio de marché — `Messages.cpp:242`, `WorldSession.cpp:810`, `ObjectMgr.cpp:831-852`, `DatabaseTemplates.h:61`. **Aucune ligne sur le lobby, la liste, la création ou l'entrée en salle** | `grep -rn "HUNTAHOLIC" reference/ngemity/Chihiro/src`, commit NGemity `38ceb2c` |

**Conséquence : la conduite serveur ne peut pas être portée depuis NGemity** (le profil dit
« NGemity tranche la logique » ; NGemity n'implémente pas ce lobby — §6). Le client 7.3 est le seul
arbitre, et il fournit la contrainte dure ci-dessous.

### 5.2 Ce que le client 7.3 attend en retour — et ce qu'il n'attend pas

Chaîne de preuve, sur `SFrame.exe` désassemblé dans cette session :

1. Le handler `0x4770a0` — celui du cas **`MSG_RESULT`** du dispatcher
   `SCommandSystem::ProcMsgAtStatic` (appel en `0x47fb43` ; identifié par la fiche sœur 4003, non
   re-démontré ici) ouvre la charge utile d'un objet message interne en **`0x13`** :
   `movzx eax,WORD PTR [edi+0x13]` (`0x4770fa`). C'est le `request_msg_id` (`0x13` = 19 = la taille de
   l'enveloppe des objets messages, cohérent avec le socle §3.4).
2. La cascade de comparaisons sur ce champ porte des **ids de paquets client → serveur** : dans la
   même fonction, `cmp eax,0x19a` (410 = `TM_CS_JOB_LEVEL_UP`, `op_codes.md:132`), `cmp eax,0x2bc`
   (700 = `TM_CS_START_BOOTH`, `op_codes.md:169`), `cmp eax,0xfa3` (4003) et — pour 4004 — une
   **seule** comparaison dans tout le binaire : `cmp eax,0xfa4` (`0x4774dd`) puis `je 0x477503`
   (`0x4774e8`). Le corps atteint est **commun avec 4003** : `0x477503`
   `cmp WORD PTR [edi+0x15],0x20`, et si — et seulement si — le champ de 16 bits en `[edi+0x15]`
   vaut **`0x20` (32)**, `push 0x2415` (`0x47750e`) puis `jmp 0x4776d2` (affichage de la notice).
3. `0x2415` = **9237** dans `db_string.rdb` — texte de 81 caractères :
   **« You cannot create the room because you have used all of your entries for the day. »**
   (enregistrement à l'offset 10 662 285 = `0xa2b18d`, clé `instancegame_text9238` à 10 662 317,
   texte à 10 662 339 ; l'enregistrement d'id 9236, à l'offset 10 662 149, porte le **même** texte,
   ce qui rend la lecture insensible au décalage de ±1 des noms de clés de ce fichier — le fichier
   ne contient que **2** occurrences de ce texte).
4. **Correction d'une prémisse du lot** : 4004 **n'a pas de notice propre**. Le démontage complet
   contient **un seul** `push 0x2415` (celui de `0x47750e`) et **un seul** `cmp eax,0xfa4` (celui de
   `0x4774dd`) : la branche 4004 hérite de la notice de 4003, texte compris. Rien n'indique un
   message « cannot join the room » : la table `instancegame_text*` (36 enregistrements, ids 9206 à
   9243) n'en contient aucun, et la recherche textuelle dans tout `db_string.rdb` ne trouve que les
   deux enregistrements jumeaux ci-dessus.

Ce que 4004 **ne** déclenche pas :

- **aucun** réaction au succès : aucune notice, aucune ouverture de fenêtre, aucun changement d'état
  de fenêtre n'est branché sur l'id 4004. L'entrée effective du joueur dans l'instance passe par
  d'autres paquets (`4001`/`4002`/`4005`, lot S2/S3) ;
- **aucun** drapeau « en attente de réponse » : le socle relevait pour 4003 un second site de
  comparaison (`0x47cbce`/`0x47cc31`, `mov ecx,0xfa3` + `cmp cx,[…]`, qui positionne un octet d'état
  de fenêtre en `0x150`). **4004 n'a pas d'équivalent** : `mov ecx,0xfa4` n'existe qu'au constructeur,
  donc la fenêtre de la liste ne se met jamais en attente d'une réponse pour une entrée en salle.

**Identification de la réponse.** L'objet message lu par ce handler a exactement la forme de
`TS_SC_RESULT` : `u16 request_msg_id` en `+0x13`, `u16 result` en `+0x15`, `i32 value` en `+0x17`
(cette dernière lecture est attestée en `0x477456`, `mov edi,DWORD PTR [edi+0x17]`, où le champ sert
de *handle*). C'est la disposition de `TS_SC_RESULT_DEF` (`rzu/librzu/src/packets/GameClient/TS_SC_RESULT.h:8-10`)
sans trou ni remplissage. La réponse identifiée est donc :

| Réponse | Id | Taille | Contenu | Effet client |
|---|---|---|---|---|
| `TM_SC_RESULT` — refus « quota épuisé » | **0** en 7.3 (`X(0, version < EPIC_9_6_3)`) | **15 octets** (7 d'en-tête + 2 + 2 + 4) | `request_msg_id = 4004`, `result = 0x20`, `value = 0` | affiche la notice **9237** (§5.2 point 3) |

Sources : rzu `TS_SC_RESULT.h:8-14` ; NGemity `TS_SC_RESULT.h:6-11` + `ClientPackets.h:23` ;
Navislamia `GamePackets.cs:5` et `Game/Network/Packets/Game/TS_SC_RESULT.cs:5-18` (structure et
`Pack = 1` déjà présentes). Réserve nommée : c'est une **identification par convergence** (structure
de l'objet + gating rzu + NGemity + `CLAUDE.md:1283`, qui constate déjà qu'une réponse `TM_SC_RESULT`
(0) est reçue et interprétée par le client 7.3) ; elle n'est pas prouvée par le démontage du client,
dont la table de routage id ↔ handler vit en `.data` (§7).

### 5.3 Ce que le serveur doit faire dans ce lot

Le socle §5.4 (`:479`) fixe le critère propre de S4 : « chaînes bornées à 30 et 16 caractères + NUL ;
refus propre si `Length` ≠ attendu ».

| Point | Règle | Source |
|---|---|---|
| Lecture | `TryRead…` retournant `bool`, testé sur des `byte[]` construits à la main, comme les lots S1/S4 | socle `§5.5` `:494` |
| Longueur | **28 exactement** ; toute autre `Length` → refus et journal, **sans réponse** | §3.2 ; socle `:479` |
| Champs | `instance_no` = `int32` signé à 7 ; `password` = tampon de 17 octets à 11, « pas de mot de passe » = 17 zéros | §3.1 |
| Mot de passe sans NUL sur ses 17 octets | **refus de la trame** (pas de troncature) — le client ne peut pas la produire (copie bornée à 17 octets NUL compris) | §3.3, site B `0x4c946f-0x4c9472` |
| Valeur du mot de passe | **jamais transportée hors du lecteur** (seule une longueur ou un booléen sort) | même politique que la fiche sœur 4003 (un mot de passe de salle ne doit pas finir dans un journal) |
| **Réponse** | **aucune réponse émise par ce lot.** Sans état de lobby, le serveur ne peut pas savoir si un quota est épuisé : envoyer `result = 0x20` afficherait une notice fausse. L'instrument identifié (`SendResult(4004, 0x20)`, 15 octets, id 0 en 7.3) est documenté ici mais reste une **décision de jeu**, à trancher par Killian (`## A VERIFIER PAR KILLIAN`, point 3) | §5.2 ; `GameClient.cs:69-73` (`SendResult` existe déjà) |
| Journalisation | tracer l'id, la `Length`, `instance_no` et la **présence** d'un mot de passe, sans la valeur | style des lots S1/S4 |
| État | **aucun** état de lobby, aucune instance, aucune table : S4 est un lot de protocole | socle `§5.5` `:492-495`, `:643-645` |

Dépendance à **nommer** (constat recevable, pas un échec) : le socle ordonne **S2 → S4**
(`:483`). Sans `4000`/`4001`/`4002`, un `4004` reçu ne peut produire **aucun effet observable
côté client** : la seule sortie client identifiée est la notice de refus (§5.2), et la liste des
salles vient de `4001`. Livré avant S2, le lot se limite donc à *lire, valider, journaliser* — c'est
son périmètre maximal honnête.

### 5.4 Points d'implémentation

- **Enum et dispatch modifiés ensemble** (critère transversal n° 4) : `TM_CS_HUNTAHOLIC_JOIN_INSTANCE = 4004`
  dans `Game/Network/Packets/Enums/GamePackets.cs`, **dans le bloc du socle instance game**
  (`GamePackets.cs:202-207`) et routé dans la chaîne de `if (header.ID == …)` de
  `GameClient.OnDataReceived`, **avant** le `switch` final dont le défaut est
  `_ => throw new Exception($"Unknown Packet Type {header.ID}")` (`Game/Network/Clients/GameClient.cs:1854-1866`).
  Une insertion à côté du bras `TM_CS_SUMMON` (`GameClient.cs:1345`) ou de l'anomalie
  `TM_SC_INSTANCE_GAME_SCORE_REQUEST` (`:1451-1460`) est dans le style du dépôt et hors de la zone
  d'insertion la plus disputée (fin de chaîne).
- **Classe de paquet** : le dépôt a déjà le précédent du lot S1 —
  `Game/Network/Packets/Game/GameInstanceGamePackets.cs` (`HeaderSize = 7`, `EnterLength`,
  `readonly record struct` + `TryReadEnter`). L'endroit où placer le frère de 4004 reste un choix du
  dev, pas une prescription de cette fiche.
- **Tests** : au moins un test d'offsets conforme à §3.4 ; le total de la suite ne doit pas baisser.
  Relevé sur la base de cette fiche (`b56967a`) : `dotnet build Navislamia.sln -c Debug` code 0 (23
  avertissements), `dotnet test Tests/Tests.csproj` code 0 — **1302 tests passés**, 0 échec.
- **Réponse** : rien à câbler (§5.3). `GameClient.SendResult(ushort, ushort, int)` existe déjà
  (`GameClient.cs:69-73`) : le jour où la décision de jeu est prise, aucune classe de paquet nouvelle
  n'est nécessaire.

### 5.5 Zones de recouvrement à nommer (collisions)

`Game/Network/Packets/Enums/GamePackets.cs` et `Game/Network/Clients/GameClient.cs` sont touchés par
**toutes** les branches HuntaHolic ouvertes (les six cartes de la famille 4000-4012) : la zone
d'insertion de l'id (bloc `:202-207`) et la zone d'insertion du dispatch sont des points de conflit
**connus**, à signaler dans la MR plutôt qu'à résoudre au jugé. Aucun autre fichier n'est nécessaire
pour ce paquet — en particulier, **aucun** fichier lié à `TM_SC_RESULT` n'a besoin d'être modifié.

---

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | Pourquoi | Source |
|---|---|---|
| NGemity déclare 4004 mais **n'implémente aucun traitement** (ni lobby, ni liste, ni réponse) : la logique serveur ne peut pas être portée depuis lui | `Chihiro/src` n'a aucun code de la famille HuntaHolic (§5.1) ; le seul point d'ancrage voisin est le warp `SKILL_WARP_TO_HUNTAHOLIC_LOBBY`, qui n'émet aucun de ces paquets | §5.1 |
| Nous **retenons** en revanche sa fabrique de résultats : `Messages::SendResult` / `WorldSession::_SendResultMsg` construisent exactement le triplet `request_msg_id` / `result` / `value` de rzu | c'est la transposition directe de ce que fait déjà `GameClient.SendResult` chez nous, et cela confirme la disposition des trois champs (`TS_SC_RESULT.h:6-9` NGemity ≡ `:7-10` rzu) | NGemity `Messages.cpp:361-383`, `WorldSession.cpp:176-183` ; Navislamia `GameClient.cs:69-73` |
| Nous **ne reprenons pas** le nom `TS_RESULT_NOT_ENOUGH_BULLET` pour la valeur `0x20` | rzu (`PacketEnums.h:37`) et NGemity (`TS_MESSAGE.h:86`) lui donnent tous deux la valeur 32, mais ce nom contredit la notice réellement affichée (« … used all of your entries for the day ») — même position que la fiche sœur 4003 | rzu `PacketEnums.h:37` ; NGemity `TS_MESSAGE.h:86` |
| NGemity porte `TS_SC_RESULT = 0` **en dur**, sans gating : nous suivons cette valeur pour 7.3, mais nous gardons la trace du gating rzu (id 1000 à partir de 9.6.3) | c'est la seule divergence d'id d'un paquet de cette fiche, et elle est versionnée | §4 |
| NGemity et rzu s'accordent sur la **structure de 4004** (`int32`, puis `string(17)`) : cet accord est retenu, et il est confirmé par le client | concordance exacte des deux `DEF` et du constructeur client | §3.1, §3.2 |

---

## 7. `NON ÉTABLI`

1. **Forme sur le fil de la réponse** — établi : le client réagit à un objet message dont le champ
   `request_msg_id` vaut 4004 et dont le champ `result` vaut `0x20`, et cet objet a la disposition de
   `TS_SC_RESULT` en `+0x13`/`+0x15`/`+0x17` (§5.2). **Non établi** : que cette disposition corresponde
   bien aux offsets 7/9/11 d'une trame reçue d'id 0 — le client lit un objet interne, pas le tampon
   brut, et sa table de routage id ↔ handler vit en `.data`, non désassemblée par `objdump -d`.
   La fiche sœur 4003 laisse ce point ouvert pour la même raison ; cette fiche l'a resserré (les trois
   champs s'enchaînent sans trou et couvrent exactement le `_DEF`) sans le fermer.
2. **Signification de la valeur `0x20`** — trois lectures possibles, aucune prouvée : (a) un code de
   l'espace `TS_RESULT` (nom incompatible avec la notice) ; (b) un code de motif propre à la table de
   notices du client ; (c) un compteur comparé à un maximum. Tranché : `0x20` est la valeur qui
   déclenche la notice pour 4003 **et** 4004 ; **non tranché** : ce que le serveur doit y mettre.
3. **Sort du refus** : le client affiche, pour un échec d'**entrée** en salle, le texte de la
   **création** de salle (« You cannot *create* the room… »). Fait vérifié (§5.2), pas un choix du
   serveur — mais cela signifie qu'une notice correcte pour une entrée en salle n'existe pas dans ce
   client. Toute décision d'émettre un refus affichera ce texte-là.
4. **Effet client d'un succès** : aucun identifié (§5.2). On ne sait donc pas si une réponse est
   attendue du tout quand l'entrée en salle réussit ; l'effet visible passerait par `4001`/`4002`/
   `4005` (lots S2/S3).
5. **Domaine de `instance_no`** : la valeur envoyée est, côté client, le premier `dword` de l'entrée
   de 38 octets reçue dans `4001` (§2, §3.1) — c'est donc l'identifiant attribué par le serveur dans
   la liste. **Non établi** : qui l'attribue, sur quel espace (ligne de `db_huntaholicresource.rdb` ?
   index de session ?), et s'il doit survivre à un redémarrage. Politique de jeu : à trancher par
   Killian.
6. **Longueur maximale réellement imposée par la fenêtre** (16 caractères) : reprise du socle
   (`socle-instances-jeu.md:118`) et **non re-vérifiée** ici (la fenêtre `SUIHuntaHolicConfirmPasswordWnd`
   n'a pas été désassemblée). Ce qui est vérifié : la copie est bornée à **17 octets** (§3.3).
7. **Fenêtre source du chemin sans mot de passe** : le site A est dans la fonction `0x4c9250`, dont
   l'appelant n'a pas été identifié (le message de notification qui la déclenche n'est pas établi).
   Ce qui est vérifié : elle reçoit un index valide du tableau d'entrées de `4001` et envoie 4004 avec
   le mot de passe nul. La conclusion pratique (« salle publique ⇒ mot de passe nul ») ne dépend pas
   de ce point.
8. **Mot de passe vide sur une salle protégée** : le client envoie alors 17 zéros, indiscernables du
   cas « salle publique » (§3.3). Établi : le client n'envoie jamais autre chose. **Non établi** : ce
   que le serveur doit faire d'un champ nul sur une salle dont il sait qu'elle exige un mot de passe
   — accepter, ou refuser avec le seul code connu (`0x20`). Politique de jeu, à trancher par Killian.
9. **Existence d'un descendant** : aucune source ne dit si une entrée en salle **crée** quelque chose
   côté serveur (instance, session d'instance, ligne persistée) ni ce qui en sort à la sortie ou au
   redémarrage. `db_huntaholicresource.rdb` ne porte qu'un enregistrement (§8) et le socle laisse
   `huntaholic_id` ouvert (`socle-instances-jeu.md:602` et suivantes). Ce qui manque : un paquet
   serveur → client qui porterait l'identité de l'instance rejointe — **aucun n'est identifié** dans
   le client pour 4004 (§5.2). Conséquence pour le lot : rien à créer, aucune réponse à émettre.
10. **Ce que le socle appelle « refus »** : le refus local (`Length` ≠ 28 → journal, aucune réponse)
    est prescrit par le socle (`:479`) et repris en §5.3 ; **non établi** : le comportement du client
    7.3 face à un silence du serveur après un `4004` — aucune notice de délai ou d'échec n'est
    branchée sur 4004 (§5.2). Rien ne permet donc de dire si le joueur voit quelque chose.

---

## 8. Commits et binaires épinglés

| Référence | Identifiant | Emploi |
|---|---|---|
| Navislamia, base de branche | `b56967a07430422add88e0e5cdf292b41b18f6c6` (`master`, `origin/master`) | état du serveur au moment de la fiche ; `4004` absent de `GamePackets.cs` |
| rzu (`reference/rzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | taille, ordre des champs, `X(4004, true)`, en-tête de 7 octets, `writeString`, gating de `TS_SC_RESULT`, `TS_ResultCode` |
| NGemity (`reference/ngemity`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | déclaration identique, absence totale de traitement, `SendResult`, `TS_SC_RESULT = 0` |
| Client 7.3 (`reference/client73/SFrame.exe`) | **pas de dépôt git** : aucun SHA de commit n'existe ni ne doit être fabriqué. Empreinte du **fichier** : sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 octets) | constructeur `0x4c9190`, sites `0x4c9339`/`0x4c9457`, handler `MSG_RESULT` `0x4770a0`, messages de fenêtre |
| Chaînes (`reference/client73/db_string.rdb`) | idem, empreinte du fichier : sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` (14 294 729 octets) | résolution de `0x2415` → id **9237** (enregistrements 9236/9237, offsets 10 662 149 et 10 662 285) |
| Ressource HuntaHolic (`reference/client73/db_huntaholicresource.rdb`) | idem : sha256 `c66274acd92ba6778021eaf34672e8799cbd51dba53eb416998d863b5d6e337b` (152 octets) | lue pour écarter une piste : en-tête daté `20091218`, `u32` = **1** en `0x80` puis `10000`, `80010000`, `0`, `1500`, `3000` — **un seul enregistrement**, donc aucun espace d'`instance_no` à y lire (§7 point 5) |
| Fiche sœur 4003 | **pas sur `master`** : lue dans `origin/hermes/packet-4003-huntaholic-create-instance`, `docs/packet-specs/4003-huntaholic-create-instance.md` (MR #38 ouverte) | confrontée, jamais recopiée ; ses conclusions sur la notice partagée et sur l'absence de réponse sont reprises avec sa référence |

Les empreintes sha256 sont des **empreintes de contenu de fichier**, calculées dans cette session
(`sha256sum`) : `reference/client73/` n'est pas un dépôt git, il n'y a donc **aucun commit de client à
épingler**.

Commandes de reproduction (depuis `reference/client73/`, **aucune exécution** de `SFrame.exe`, de Lua
ou d'un script du client) :

```bash
sha256sum SFrame.exe db_string.rdb
objdump -d -M intel --start-address=0x4c9190 --stop-address=0x4c91f0 SFrame.exe   # constructeur 4004
objdump -d -M intel --start-address=0x4c9250 --stop-address=0x4c9378 SFrame.exe   # aiguillage require_password + site A
objdump -d -M intel --start-address=0x4c9420 --stop-address=0x4c9486 SFrame.exe   # handler onConfirmPassword + site B
objdump -d -M intel --start-address=0x4770a0 --stop-address=0x4776ff SFrame.exe   # handler MSG_RESULT
grep -n "0xfa4" /tmp/sframe.asm            # 1 seul cmp (0x4774dd), 1 seul mov dans un constructeur (0x4c91c1)
grep -n "push   0x2415" /tmp/sframe.asm    # 1 seul site : 0x47750e
strings -t x SFrame.exe | grep -E "onConfirmPassword|onCreateInstance|setCallBack"
```

La lecture de `db_string.rdb` s'est faite par un lecteur Python local jetable (`/tmp/`, non
versionné), en lecture seule : enregistrement = `u32 id`, 20 octets réservés, `u32 keylen`,
`u32 textlen`, clé NUL, texte NUL ; parcours ancré sur la clé `instancegame_text9238` puis remontée
d'un enregistrement, ids et continuité vérifiés.

---

## 9. Bloc prêt à coller dans `CLAUDE.md`

> ### Paquet 4004 — `TM_CS_HUNTAHOLIC_JOIN_INSTANCE` (rejoindre une salle HuntaHolic)
>
> **28 octets, `X(4004, true)` chez rzu : aucun gating de version, aucune variante d'id, aucun champ
> gaté.** En-tête de 7 octets + charge utile de 21 : `instance_no` = `int32` signé à l'offset 7 (le
> premier `dword` de l'entrée de 38 octets reçue dans `4001`), `password` = tampon **fixe** de 17
> octets à l'offset 11 (au plus 16 caractères, NUL **dans** le champ ; **17 zéros** quand la salle est
> publique). Le client 7.3 le construit en `0x4c9190` (`Length` = `0x1c`, `ID` = `0xfa4`, checksum =
> somme des 6 premiers octets mod 256) et l'envoie depuis deux sites, qui sont les deux seuls :
> `0x4c9339` (salle publique, mot de passe remis à zéro) et `0x4c9457` (handler du message
> `onConfirmPassword`, `sub esp,0x1c`). Fiche :
> `docs/packet-specs/4004-huntaholic-join-instance.md`.
>
> **Le gating, dans ce lot, est celui de la *réponse* :** `TS_SC_RESULT` vaut **`X(0, version <
> EPIC_9_6_3)`** / `X(1000, version >= EPIC_9_6_3)` — **pour 7.3 l'id est 0**, comme le portent déjà
> `GamePackets.cs:5` et NGemity (`ClientPackets.h:23`). Ne pas écrire 1000.
>
> **Le client ne réagit qu'à un refus, et il n'a pas de notice propre à 4004** : le cas `MSG_RESULT`
> du handler `0x4770a0` (charge utile à `[msg+0x13]` = `request_msg_id`, `0x20` en `[msg+0x15]` =
> `result`) compare 4004 en `0x4774dd` et **partage le corps de 4003** en `0x477503` ; si `result`
> vaut `0x20`, il affiche la chaîne **9237** de `db_string.rdb` — « You cannot create the room
> because you have used all of your entries for the day. » Un seul `push 0x2415` et un seul
> `cmp eax,0xfa4` existent dans tout le binaire : la notice d'un échec **d'entrée** en salle est
> le texte de la **création**. Aucun drapeau « en attente de réponse » n'est posé pour 4004.
>
> **NGemity ne tranche rien** : 4004 y est déclaré (`ClientPackets.h:237`) et jamais traité, comme
> toute la famille HuntaHolic (`Chihiro/src` n'en contient aucun code). `rzgame` non plus. Le client
> 7.3 est le seul arbitre, et il ne réagit pas au succès.
>
> **Lot S4** : lecture, refus propre si `Length` ≠ 28, journalisation (jamais la valeur du mot de
> passe) — sans réponse, sans état, sans table. `TM_CS_HUNTAHOLIC_JOIN_INSTANCE = 4004` doit être
> déclaré dans `GamePackets` (bloc `:202-207`) **et** routé dans `GameClient.OnDataReceived` avant le
> `switch` final (aucun id ne doit atteindre le `throw`). Ordre du socle : `S2 → S4`.

---

## A VERIFIER PAR KILLIAN

1. **La réponse identifiée doit-elle être émise ?** Cette fiche identifie le seul instrument de refus
   que le client 7.3 comprend — `TM_SC_RESULT` (id **0** en 7.3), **15 octets**,
   `request_msg_id = 4004`, `result = 0x20`, `value = 0` — mais ne l'implémente pas : sans état de
   lobby, le serveur ne peut pas savoir si un quota est épuisé, et le texte affiché serait faux
   (§5.3, §7 points 2 et 3). Décider : pas de réponse (recommandé pour ce lot), ou refus explicite.
2. **Domaine de `instance_no`** : le serveur devra attribuer l'identifiant que le client renverra tel
   quel dans `4004` (§7 point 5). Sur quelle source : `db_huntaholicresource.rdb`, la base
   `HuntaholicResource` reprise par le socle, ou un compteur de session ?
3. **Salle protégée avec mot de passe nul** : le client envoie alors 17 zéros, indistinguables du cas
   « salle publique » (§7 point 8). Accepter ou refuser — et si refuser, seul le code `0x20` a un
   effet connu, avec le texte de création de salle.
4. **Réserve de méthode sur l'identification de la réponse** (§7 point 1) : la disposition
   `TS_SC_RESULT` est retenue par convergence de quatre sources (objet client, rzu, NGemity,
   `CLAUDE.md:1283`), pas par lecture de la table de routage id ↔ handler du client (qui vit en
   `.data`). Si Killian veut une preuve directe, elle demanderait une lecture de `.data` hors du
   périmètre de cette fiche.
5. **Lot inerte avant S2** : le socle ordonne « S2 → S4 » (`:483`) — c'est un ordre **fonctionnel**,
   pas un prérequis de compilation. Sans `4000`/`4001`/`4002` sur `master`, le code de 4004 est lu,
   borné et journalisé mais **ne produit aucun effet observable** côté client (§5.3) : la QA du lot S4
   l'avait déjà consigné pour 4003. Décider si l'on merge un lot inerte, ou si l'on attend S2.
6. **Fiche sœur 4003 non mergée** : `docs/packet-specs/4003-huntaholic-create-instance.md` ne vit que
   sur `origin/hermes/packet-4003-huntaholic-create-instance` (MR #38 ouverte, §8). Cette fiche s'y
   réfère ; si #38 évolue, la conclusion partagée sur la notice 9237 et sur l'absence de réponse doit
   rester cohérente entre les deux.
