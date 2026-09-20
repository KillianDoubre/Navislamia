# 60 — `TM_CS_REQUEST` (client → serveur)

| | |
|---|---|
| **Statut** | Fiche de paquet sourcée — **aucun code serveur modifié** |
| **Carte** | Trello `Xn2fBde2` (Hermes `t_b5cf5b0e`, assignée `navis-ref`) ; identifiant de suivi **`navislamia:packet:60`** |
| **Base** | `master` = `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (état au moment de la fiche) |
| **Branche** | `hermes/packet-60-request` (fiche seule ; **non poussée**, la publication est la tâche du QA) |
| **Références épinglées** | rzu `87c1e83bf84efe29bb6405e8e6da80349712f3fa` ; NGemity/Chihiro `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` ; client 7.3 = `SFrame.exe` sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (`reference/client73/` n'est pas un dépôt git : aucune révision à épingler, seule l'empreinte du binaire) |
| **Nature du paquet** | Canal de **commande** client → serveur, seule trame **variable** de la famille anti-triche |

> `docs/packet-specs/` **existe déjà sur `master`** (4 fiches : `1202-emotion.md`, `203-drop-item.md`,
> `253-use-item.md`, `550-get-region-info.md` — `git ls-tree master docs/packet-specs/`), et `.gitignore:473`
> (`!/docs/packet-specs/`) l'autorise comme exception. Cette fiche est donc un ajout, pas une création de
> répertoire. La remarque de la carte (« Le dossier `docs/packet-specs/` n'existe pas sur `master` : c'est
> normal, tu le crées ») est **périmée** ; elle avait déjà été relevée sur la branche `socle-anti-triche`.

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id décimal | **60** | `op_codes.md:41` |
| Nom `TM_CS_*` | **`TM_CS_REQUEST`** | `op_codes.md:41` |
| Direction | **client → serveur** | rzu `librzu/src/packets/GameClient/TS_CS_REQUEST.h:13` (`SessionPacketOrigin::Client`) ; NGemity `shared/Server/Packets/GameClient/TS_CS_REQUEST.h:10` (dossier `GameClient` = session de jeu, cf. §6) |
| Déclaration rzu | `_(simple)(uint8_t, t)` + `_(endstring)(command, true)` | `TS_CS_REQUEST.h:6-7` |
| Déclaration NGemity | identique | `shared/Server/Packets/GameClient/TS_CS_REQUEST.h:7-8` |
| Membre `GamePackets` | **absent du dépôt** — 60 est libre | `Game/Network/Packets/Enums/GamePackets.cs` (aucun membre 51–59 ni 60 ; voisins : `TM_SC_RESULT = 0` `:5`, `TM_CS_VERSION = 50` `:87`, `TM_NONE = 9999` `:101`) |
| Membre `op_codes.md` | présent, 60 = `TM_CS_REQUEST` | `op_codes.md:41` |
| Paquet serveur→client associé | **aucun** : le seul accusé visible dans les références est le générique `TM_SC_RESULT` (0) — cf. §5.3, §5.5 | — |

Le paquet est **uni-directionnel entrant** : 60 n'existe pas côté serveur→client dans `op_codes.md`, et
le client 7.3 n'a **aucun bras** pour une trame d'id 60 en réception (§2.3).

---

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Réponse courte

**Aucune action du joueur ne fait émettre 60 par le client 7.3 livré.** Les arbres de référence qui
portent une logique de jeu (`librzu`, `rzgame`, `Chihiro`) n'ont **aucun producteur** pour 60 ; le seul
producteur de `TS_CS_REQUEST` trouvé tous dépôts confondus est un **outil de supervision** de NGemity qui
parle à un serveur tiers (§5.3). Autrement dit : le champ « ce que le joueur fait » de cette fiche est
**vide**, et c'est un résultat, pas une lacune de relevé — la méthode et les relevés qui l'établissent
sont en §2.3.

### 2.2 Statut dans `NavisLamia` à ce jour

Rien n'est déclaré : `TM_CS_REQUEST` n'est ni dans `GamePackets`, ni dans `GameActions._actions`
(`Game/Network/Clients/Actions/GameActions.cs:43-50`), ni dans la chaîne de `GameClient.OnDataReceived`
(`Game/Network/Clients/GameClient.cs:590-789`), ni dans le `switch` final (`:791-802`). Un 60 reçu
aujourd'hui tombe donc dans `Enum.IsDefined` → journal Debug + `continue` (`:584-588`).

### 2.3 Méthode et relevés — le client 7.3 ne nomme ni n'émet 60

Le client tranche (`reference/client73/SFrame.exe`, sha256 ci-dessus, `pei-i386`, 9 841 664 octets ;
`objdump` + lecture binaire directe, **aucune exécution** de `SFrame.exe`, de Lua ou d'un script client).

**a) Absence du nom.** Recherche de la chaîne C terminée par NUL :

| Chaîne cherchée dans tout le binaire | Résultat |
|---|---|
| `TM_CS_REQUEST\0` | **absente** |
| `TM_CS_REQUEST_CLEAR_SECURITY_NO\0` | présente (offset fichier `0x651830`) |
| `TM_CS_REQUEST_SECURITY_NO_CHANGE\0` | présente (`0x65186c`) |
| `TM_CS_QUERY\0` | présente (`0x6523b0`) |
| `TM_CS_CHAT_REQUEST\0` | présente (`0x65231c`) |

Les seules occurrences du préfixe sont donc deux paquets **distincts** (les deux `REQUEST_*_SECURITY_NO`).
Le client nomme par ailleurs beaucoup de paquets de contrôle (`TM_CS_CHAT_REQUEST`, `TM_CS_QUERY`,
`TM_CS_REQUEST_CLEAR_SECURITY_NO`, …) : l'absence de nom pour 60 n'est pas un artefact de convention.

**b) Table id→nom complète.** Le client enregistre ses noms par appels à l'insertion de table
`0x674350`, précédés de l'idiome `mov $ID,%eax` puis `divl 0x4(%esi)` (recherche automatisée sur tout
`.text`) :

| Mesure | Valeur |
|---|---|
| paires (id, nom) détectées | **166** |
| ids couverts | 1…10005 (166 ids distincts) |
| **60 présent ?** | **non** |
| ids voisins présents | 50, 53, 54, 58, 59 |
| ids voisins absents | 55, 56, 57, **60** |

Ce relevé reproduit exactement le compte brut (166 paires) et la liste des noms de la fiche socle
(`socle-anti-triche.md:293-317` : « Le client nomme 53, 54, 58 et 59, mais ne nomme ni 55, ni 56, ni 57,
ni 60 »). Le socle avertit lui-même : *« l'absence d'un nom ne prouve pas l'absence d'émission »* — d'où c).

**c) Absence d'émission.** Un paquet sortant du client porte son id **à l'offset +4 de la trame** ; le
constructeur de trame écrit la longueur en `+0` et l'id en `+4` (exemple canonique, constructeur de 57 :
`mov $0x39,%edx` ; `mov %dx,0x4(%eax)` ; `movl $0xb,(%eax)` à `0x649609-0x649614`), et le
désassembleur sortant relit cet id en `+4` (`movzwl 0x4(%ebx),%ecx` à `0x6498f0`). Recherche exhaustive
de tout écrit de 60 dans `.text` :

| Motif recherché | Occurrences |
|---|---|
| `mov $0x3c,%edx` (registre de l'idiome ci-dessus) | **0** |
| `movw $0x3c,0x4(…` / `mov $0x3c,0x4(…` | **0** |
| `movl $0x3c,0x4(…)` | **1** → `0x66ff4a`, **rejeté** (voir ci-dessous) |
| `mov $0x3c,%<reg>` (eax/ecx/ebx/esi/edi/bl) | 16, tous en contexte arithmétique ou IHM (voir ci-dessous) |
| `push $0x3c` | 76, dont **7** suivis de `call 0x97671b` = `operator new(60)` |
| `movb $0x3c,-0x4(%ebp)` | 6 — marqueurs d'état SEH de MSVC, pas des ids |

Le candidat unique `0x66ff4a` est un **objet C++**, pas une trame : il est alloué par
`push $0x1d; call 0x97671b` (29 octets) et reçoit **une vtable en `+0`** (`movl $0xa52020,(%eax)`,
`0xa52020` = tableau de pointeurs `.text` : `0x66c360`, `0xbc86b8`, `0x6284c0`, …), là où une trame de
paquet reçoit sa **longueur** en `+0`. Les 16 sites `mov $0x3c,%<reg>` restants ont été examinés un par un : **aucun** n'écrit dans `0x4(…)`.
Ce sont des divisions ou multiplications par 60 (`div %ecx`, `idiv %ebx`, `imul %eax,%edx`), des bornes
locales (`cmp %eax,-0x4(%ebp)`), des valeurs de retour (`pop %esi; ret`), un octet de pile
(`mov %bl,0x8(%ebp)`) et une écriture de champ d'IHM (`mov %ax,0x4e8(%esi)`).

**d) Réception côté client.** La table de discriminants du dispatcheur entrant est un tableau de 251
octets à **VA `0x67f0a0`** (offset fichier `0x27e4a0`) indexé par l'id, suivi d'une table de sauts de 32
entrées dwords à **VA `0x67f020`** (offset `0x27e420`). Relevé : `table[60] = 31` → `0x67ef21`, qui
pousse la chaîne `0xa53df0` (`처리되지 않은 메세지 : %d\n`, « message non traité : %d ») vers
`0x824240`, puis saute au tronc commun `0x67ef39` (libération de la trame). Autrement dit : **si le
serveur émettait une trame d'id 60, le client la classerait en « message non traité »**. À titre de
comparaison, `table[58] = 14` → `0x67ef39` directement = cas **vide** mais reconnu (les trois cas vides
du client sont 53, 55, 58).

**e) Le client est bien sur la numérotation 7.3.** Le même relevé d'enregistrements donne, dans cet
ordre : `(9999, "TM_NONE")` (chaîne `0xa53a14`, longueur 7 poussée en `0x675326`, id `0x270f` en
`0x67535a`), `(50, "TM_CS_VERSION")` (chaîne `0xa53a04`, longueur `0xd` en `0x67539c`, id en `0x6753ca`)
et `(0, "TM_SC_RESULT")` (chaîne `0xa539f4`, longueur `0xc` en `0x675409`, id en `0x675437`) — les trois
chaînes relues à leur VA. Or rzu place `TS_CS_VERSION` en **50** pour `version < EPIC_7_4` (0x070400) et
en 51 entre `EPIC_7_4` et `EPIC_9_6_3` (`librzu/src/packets/GameClient/TS_CS_VERSION.h:12-14`). Le
client livré est donc **strictement antérieur à `EPIC_7_4`**, ce qui confirme le palier de §4 par le
client lui-même.

---

## 3. Structure sur le fil

### 3.1 En-tête commun (7 octets)

| Offset | Taille | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` LE | `Length` | = taille **totale** de la trame, en-tête inclus | `Header.cs:9` + `:20-25` ; `Packet.cs:31`/`:41` (`_headerLen = 7`) et `:56` (`Length = (uint)Data.Length`) ; rzu `MessageBuffer.h:73-74` |
| 4 | 2 | `uint16` LE | `ID` | `0x003c` (**60**) | `Header.cs:10`, `:23` ; `op_codes.md:41` |
| 6 | 1 | `uint8` | `Checksum` | somme des 6 octets précédents, modulo 256 | `PacketExtensions.cs:13-25` ; rzu `MessageBuffer.h:17-28` et `:76` |

`Length` inclut l'en-tête de 7 octets : la base de taille est `7` dans rzu
(`PacketDeclaration.h:576-581`, `CREATE_STRUCT_IMPL(name_, 7, …)`, idem NGemity `:576`) et
`Marshal.SizeOf<Header>() == 7` côté dépôt (`GameClient.cs:559-561`).

### 3.2 Corps — 2 champs, un seul variable

| Offset | Taille | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|---|
| 7 | **1** | `uint8` | `t` | `0x75` (`'u'`) — **seule valeur observée à ce jour**, et elle vient d'un outil tiers, jamais du client (§5.3) | rzu `TS_CS_REQUEST.h:6` ; NGemity `TS_CS_REQUEST.h:7` ; valeur : `Tools/ServerMonitor/src/Client/MonitorSession.cpp:85` |
| 8 | **`L` + 1** | octets bruts (`std::string`) + **1 NUL terminal** | `command` | chaîne ASCII hexadécimale de 2 × *n* caractères (`"53454c…"`) | rzu `TS_CS_REQUEST.h:7` (`_(endstring)(command, true)`) ; taille : `PacketDeclaration.h:287` ; écriture : `:383-384` ; valeur : `MonitorSession.cpp:86` |

`L` = `command.size()`, c'est-à-dire le nombre d'octets **avant** le NUL terminal.

### 3.3 Taille totale attendue

```
Length = 7 (en-tête)  +  1 (champ t)  +  L (command)  +  1 (NUL terminal)
       = 9 + L   octets
```

Cette formule est **la** taille attendue : rzu compte le terminateur dans la taille
(`SIZE_F_ENDSTRING2(name, hasNullTerminator) → size += name.size() + hasNullTerminator`,
`PacketDeclaration.h:287`) et l'écrit (`SERIALIZATION_F_ENDSTRING2 → writeDynString(…,
name.size() + hasNullTerminator)`, `:383-384`). Corollaire : `Length` **n'est jamais constante**,
contrairement aux huit autres paquets de la famille — et le `Checksum` non plus.

Exemples calculés (checksum = `Length & 0xFF` + octets d'id, cf. §3.1) :

| `L` | `Length` | 4 octets de `Length` | `Checksum` |
|---|---|---|---|
| 0 (commande vide : un NUL seul en offset 8) | **9** | `09 00 00 00` | `0x45` |
| 1 (`t='u'`, `command="u"`) | **10** | `0a 00 00 00` | `0x46` |
| 30 (`"SELECT TOP 1 id FROM Character"`) | **39** | `27 00 00 00` | `0x63` |
| 46 (blob hexadécimal de 23 octets) | **55** | `37 00 00 00` | `0x73` |
| 100 | **109** | `6d 00 00 00` | `0xa9` |

> **Réconciliation avec la fiche socle.** Le tableau §7 de `socle-anti-triche.md` (`:590`) annonce
> « 1 + `endstring` » et « **8 + longueur** », tandis que son §7.4 (`:630`) annonce
> « `7 + 1 + longueur(command) + 1` ». Les deux ne coïncident que si la « longueur » du tableau inclut
> le NUL terminal. La forme normative est celle du §7.4, écrite ici en §3.3 : **`Length = 9 + L`** avec
> `L` = nombre d'octets de `command` **hors** NUL terminal.

### 3.4 Sémantique exacte de `endstring` (le piège de ce paquet)

`endstring` **n'a pas de préfixe de longueur** : la taille du champ se déduit de la fin du datagramme.
Déroulé, `hasNullTerminator = true` :

1. écriture (rzu) : `memcpy` de `L + 1` octets depuis `val` (donc NUL terminal compris),
   `MessageBuffer.cpp:97-102` ;
2. lecture (rzu) : `remainingSize = getSize() - getParsedSize()`, puis
   `val.assign(p, remainingSize - hasNullTerminator)` et `p += remainingSize`
   (`MessageBuffer.cpp:124-137`).

Trois conséquences opérationnelles pour le dev :

- **la fin du champ est la fin du datagramme**, jamais la fin du tampon de réception : côté dépôt, il
  faut lire `Connection.Read((int)header.Length)` (`GameClient.cs:580`), donc un datagramme exact, et
  prendre `L = packet.Length - 9` ;
- **il n'y a pas de second paquet possible dans le champ** : `Length` délimite la trame, deux trames
  concaténées dans le même segment TCP sont traitées séparément par la boucle `:559-582` ;
- **le NUL terminal est compté dans `Length`** : une trame de 10 octets porte `command` de **1** octet,
  pas 2.

L'encodage n'est pas déclaré : rzu copie les octets de la `std::string` tels quels (aucun charset,
aucune conversion) et le seul producteur connu envoie de l'**ASCII hexadécimal** (§5.3). Aucun des deux
arbres ne normalise la casse ni ne valide le contenu.

### 3.5 Bornes maximales

| Borne | Valeur | Source |
|---|---|---|
| Borne de champ (rzu, par conception) | un champ exige `size < 65536` **et** sa fin dans les limites du tampon ; pour `command` (`L + 2` octets consommés après l'en-tête) : `L ≤ 65533`, donc `Length ≤ 65542` | `MessageBuffer.h:58-68` (`checkAvailableBuffer`), appelé par `readEndString` (`MessageBuffer.cpp:130`) |
| Borne de fait du dépôt | tampon de réception de `Connection` = **32768 octets** (`Connection.cs:27`), réception qui refuse d'avancer tant que `Length > remainingData` (`GameClient.cs:564-571`) → `L ≤ 32759`, `Length ≤ 32768` | `Connection.cs:27`, `:190-206`, `:309-316` ; `GameClient.cs:564` |
| Borne de politique | **aucune** dans les références | aucun `limit_*` ni constante de commande (`TS_CS_REQUEST.h` n'a que 15 lignes) |

La borne opérante est donc celle du dépôt : **32759 octets de commande**. La valeur de rzu est plus
large et ne peut pas être atteinte dans `NavisLamia` en l'état.

---

## 4. Gating de version — tranché pour Epic 7.3

| Palier de version | Id | Source |
|---|---|---|
| `version < EPIC_9_6_3` | **60** | rzu `TS_CS_REQUEST.h:10` |
| `version >= EPIC_9_6_3` | `1060` | rzu `TS_CS_REQUEST.h:11` |

Constantes : `EPIC_7_3 = 0x070300` (`PacketEpics.h:59`), `EPIC_9_6_3 = 0x090603` (`:96`), et
`EPIC_7_4 = 0x070400` (`:60`). Epic 7.3 = `0x070300 < 0x090603` → **l'id de `TM_CS_REQUEST` en 7.3 est
60, jamais 1060**. Décision écrite : le dépôt ne doit connaître que `60` ; `1060` n'a pas à être
déclaré (le numéro 1000+série est un décalage post-9.6.3, même famille que `1000`/`1051`).

**Gating au niveau des champs : il n'y en a aucun.** Les deux champs sont inconditionnels
(`_(simple)(uint8_t, t)` et `_(endstring)(command, true)` sans quatrième argument conditionnel,
`TS_CS_REQUEST.h:6-7`). **Aucun champ de `TS_CS_REQUEST` n'a donc besoin d'arbitrage de version
au-delà de l'id** — c'est le seul membre de la famille dans ce cas avec 54 (§7 de la fiche socle).

Piège de la macro d'id, à ne pas recopier : `CREATE_PACKET_VER_ID` pose
`static constexpr packetID = PACKET_IDS::getLatest()` (`PacketDeclaration.h:592-593`), c'est-à-dire
**1060** — la constante *déclarée* est le dernier palier, alors que l'id réellement écrit sur le fil est
celui de `getId(version)` (`:596-601`, `:610-612`). En 7.3 c'est **60** : le dépôt ne doit connaître que
60, et surtout pas lire un « packetID » constant pour décider (c'est le pendant exact de la réserve
`_epic_extension` déjà écrite pour 550).

Triple confirmation indépendante du palier :
1. le client livré enregistre `(50, "TM_CS_VERSION")` (§2.3e), or 50 est la valeur de
   `TS_CS_VERSION` pour `version < EPIC_7_4` → le client est `7.3.x`, donc `< EPIC_9_6_3` ;
2. `op_codes.md:41` donne 60 et `:33` donne `TM_CS_VERSION = 50`, cohérent avec rzu et avec le client ;
3. NGemity compile à un palier plus récent et son énumération est **décalée** en 50/51/52
   (`shared/Server/ClientPackets.h:50-52` : `TS_CS_UNKN = 50`, `TS_CS_VERSION = 51`,
   `TS_CS_VERSION2 = 52`). **Cet enum ne doit jamais servir à décider un id** : c'est exactement la
   réserve `_epic_extension` de la fiche 550.

---

## 5. Traitement attendu

### 5.1 Ce que rzu fait du paquet

**Rien.** `TS_CS_REQUEST` est **déclaré et jamais consommé** : le seul `TS_CS_REQUEST*` traité par
`rzgame` est `TS_CS_REQUEST_LOGOUT` (`rzgame/src/StateHandler/GameHandler/GameHandler.cpp:5` et `:44`).
Aucun `rzgame`, `rzfilter`, `rztest` ni script ne référence `TS_CS_REQUEST` (recherche sur tout l'arbre,
hors le fichier de déclaration lui-même). rzu tranche donc **la forme**, pas la logique.

### 5.2 Ce que `Chihiro` fait du paquet

**Rien.** 60 n'est ni dans la table de handlers du monde (`WorldSession.cpp:92-138`,
`worldTableSize` en `:138`) ni dans la liste des paquets ignorés (`:140`, test `:159`). Il tombe donc
dans la branche « inconnu » : `NG_LOG_DEBUG("server.network", "Got unknown packet '%d' …")`
(`:160`) puis `ReadDataHandlerResult::Ok` — **le paquet est journalisé en Debug et la connexion reste
ouverte**, sans réponse ni sanction. (Le même log existe côté outil de supervision,
`Tools/ServerMonitor/src/Client/MonitorSession.cpp:73`.)

Conclusion : **ni rzu ni Chihiro ne portent une ligne de logique pour 60.** La fiche ne peut donc pas
« porter » un comportement depuis les références ; elle peut seulement décrire la forme et nommer les
décisions.

### 5.3 Le seul producteur connu de 60 : un outil de supervision, pas le client

NGemity embarque un tableau de bord de supervision (`Tools/ServerMonitor`) dont la session **client**
émet un `TS_CS_REQUEST` :

| Élément | Valeur | Source |
|---|---|---|
| champ `t` | `'u'` (`0x75`) | `Tools/ServerMonitor/src/Client/MonitorSession.cpp:85` |
| champ `command` | `XStrZlibWithSimpleCipherUtil::Encrypt("SELECT TOP 1 id FROM Character")` | `MonitorSession.cpp:86` |
| transformation | `MXEncrypt("EV", 0x00030000, …)` (zlib + chiffrement simple) puis **encodage hexadécimal** (2 caractères par octet) | `shared/Encryption/cipher/XStrZlibWithSimpleCipherUtil.h:346-365` (`:353` pour `MXEncrypt`, `:360` pour `%02x`) |
| réponse attendue par l'outil | un `TS_SC_RESULT` dont `getReceivedId() == 60` (garde `:99`) et dont la `value` est lue comme `value ^ 0xADADADAD` (`:103`) | `MonitorSession.cpp:97-110` |
| version annoncée par l'outil | `"ASER"` (défaut de `monitor.version`) | `MonitorSession.cpp:92` ; `Tools/Shizue/src/Main.cpp:26` |
| serveur visé | un serveur tiers de la famille « ASER », **absent des arbres de `reference/`** | idem |

Ce que cela établit, et ce que cela n'établit pas :

- **établi** : la chaîne `command` est **opaque** (blob zlib+chiffrement, transporté en ASCII hexadécimal),
  et le canal est un **canal d'opérateur** dont la charge utile visible est une **requête SQL** ; un
  serveur qui exécuterait cette commande exécuterait du SQL fourni par le client ;
- **établi** : l'accusé attendu est un `TS_SC_RESULT` portant `request_msg_id = 60`, ce qui correspond
  exactement au premier champ de `TS_SC_RESULT` (rzu `TS_SC_RESULT.h:8-10` ; dépôt `Game/Network/Packets/Game/TS_SC_RESULT.cs:8-10`,
  émis par `GameClient.SendResult`, `GameClient.cs:56-60`) ;
- **non établi** : qui, du client officiel ou d'un émulateur, émet 60 en production ; la liste des
  valeurs de `t` ; qui exécute quoi. Le producteur trouvé est un outil NGemity visant un serveur tiers,
  pas le client Epic 7.3.

### 5.4 Ce que `t` n'est pas

La fiche socle §7.4 affirme : « le `t` d'un `TM_CS_REQUEST`/`TM_SC_RESULT` est le même champ que celui
des `ResultCode` du dépôt ». **Faux au sens du fil**, et le dev ne doit pas s'appuyer dessus :

| Fait | Source |
|---|---|
| `t` fait **1 octet** (`uint8_t`) | rzu `TS_CS_REQUEST.h:6` |
| `ResultCode` est un `ushort` = **2 octets** | `Game/Network/Packets/ResultCode.cs:4` |
| le champ homonyme de `TS_SC_RESULT` est `uint16_t result` (2 octets), précédé de `uint16_t request_msg_id` | rzu `TS_SC_RESULT.h:8-9` ; `TS_SC_RESULT.cs:8-9` |
| la seule valeur observée à ce jour pour `t` est un **caractère ASCII** (`'u'`), sans signification de `ResultCode` (les `ResultCode` vont de 0 à 93) | `MonitorSession.cpp:85` ; `ResultCode.cs:6-112` |

Conclusion : `t` est un **sélecteur de commande sur 1 octet**, de type inconnu ; il ne peut pas être
confondu avec le champ `result` (16 bits) des accusés. Cette correction ne change pas la forme de la
trame, mais elle interdit toute « table de commandes = valeurs de `ResultCode` ».

### 5.5 Ce que le serveur doit répondre

**Rien, par défaut — et c'est une décision, pas un oubli.**

| Question | Réponse | Source / raison |
|---|---|---|
| Une réponse est-elle définie pour 60 ? | **Non** | aucun paquet serveur→client 60 dans `op_codes.md` ; aucun handler ni émission dans rzu et Chihiro (§5.1, §5.2) |
| Le client attend-il une réponse ? | **Non** : il n'a **aucun bras** pour une trame d'id 60 entrante (table[60] = discriminant par défaut `0x67ef21`, §2.3d) | `SFrame.exe` |
| La seule forme d'accusé jamais observée | `TS_SC_RESULT (0)`, 15 octets sur le fil, `request_msg_id = 60` | `MonitorSession.cpp:99` ; rzu `TS_SC_RESULT.h:8-10` ; dépôt `TS_SC_RESULT.cs:8-10` (7 + 2 + 2 + 4 = 15) |
| Cette forme vient-elle d'un client 7.3 ? | **Non** : elle est attendue par un outil de supervision NGemity | §5.3 |

Si `NavisLamia` devait un jour répondre, ce serait `TM_SC_RESULT (0)` avec `request_msg_id = 60`, sur
décision explicite de Killian (§12), et jamais une trame d'id 60 (le client la classerait
« message non traité »).

### 5.6 Espace d'insertion côté dépôt (pour le dev, sans l'écrire ici)

Trois points d'ancrage existent, dans cet ordre d'appel :

| Point | Fichier:ligne | Ce qu'un traitement y ferait |
|---|---|---|
| Consommation des octets | `Game/Network/Clients/GameClient.cs:580` | `Connection.Read((int)header.Length)` — déjà fait pour tout paquet |
| Chaîne d'`if` par id (paquets sans `IPacket`) | `GameClient.cs:590-789` | y placer le bras `header.ID == (ushort)GamePackets.TM_CS_REQUEST` (précédent : `TM_SC_REGION_ACK` refusé explicitement en `:623-628`) |
| `switch` final typé | `GameClient.cs:791-802` | **ne pas y ajouter 60** : il exige un `IPacket` à taille fixe, ce que `endstring` n'est pas |
| Table d'actions typées | `Game/Actions/GameActions.cs:32`, `:43-50`, `:55` | concerne `TM_CS_*` typés ; sans intérêt si 60 reste « lire + journaliser » |

Lecture d'une trame variable : le seul précédent du dépôt est un champ **préfixé par un `uint16`** avec
contrôle de longueur exacte (`Game/Network/Packets/Game/GameNpcDialogPackets.cs:29-45` : lecture du
`uint16`, refus si `length > maxTriggerLength` ou si `packet.Length != SelectionHeaderSize + length`,
puis `Encoding.ASCII.GetString`). `endstring` **n'a pas** ce préfixe : l'équivalent est `L = packet.Length - 9`
avec refus si `packet.Length < 9`. Le nom de fichier libre pour un nouveau lecteur est
`Game/Network/Packets/Game/GameRequestPackets.cs` (aucune branche ouverte ne le prend, cf. §11).

---

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | rzu (autorité des tailles) | NGemity | Décision |
|---|---|---|---|
| **Terminateur NUL compté et écrit** | `size += name.size() + hasNullTerminator` (`:287`) et `writeDynString(…, name.size() + hasNullTerminator)` (`:384`) | `size += name.size()` (`:281`) et `writeDynString(…, name.size())` (`:378`) : **le NUL ni compté ni écrit** | **Suivre rzu** : `Length = 9 + L`, NUL écrit. NGemity est ici **incohérent avec lui-même** — sa lecture `readEndString` fait bien `val.resize(remainingSize - hasNullTerminator)` puis `read_skip(1)` (`MessageSerializerBuffer.cpp:52-61`) : relire une trame produite par son propre écrivain lui ferait perdre le dernier octet de la commande. Sa taille n'est donc pas une source admissible. |
| **Id 60** | `60` pour `version < EPIC_9_6_3` (`:10-11`) | `CREATE_PACKET(TS_CS_REQUEST, 60)` sans gating (`:10`) | **Concordance** à ce palier : 60 dans les deux. Aucun écart à porter. |
| **Numérotation globale** | 50 = `TS_CS_VERSION` en 7.3 (`TS_CS_VERSION.h:12`) | 50 = `TS_CS_UNKN`, 51 = `TS_CS_VERSION` (`ClientPackets.h:50-52`) | **Ne jamais reprendre un id de l'enum NGemity** ; l'énumération NGemity est d'un palier plus récent. |
| **Logique** | aucune (déclaration seule) | aucune (paquet inconnu → Debug, connexion conservée) | **Aucun portage**. L'absence chez NGemity n'est pas une décision métier : c'est un vide à combler par Killian, pas par imitation. |
| **Semantique du canal** | muette | visible seulement dans un **outil** de supervision (SQL chiffré, `t = 'u'`) | NGemity renseigne la **nature** du canal (opérateur/SQL), pas le **traitement serveur**. Aucun de ces deux éléments n'autorise à exécuter quoi que ce soit. |

---

## 7. `NON ÉTABLI`

1. **Qui émet 60 en production — la distinction est décisive.** Ce qui est **établi** : *aucun chemin du
   client Epic 7.3 livré ne produit la trame* (§2.3a–c : nom absent, aucun écrit d'id 60 dans `.text`).
   Ce qui reste **non établi** : *qui* l'émet dans la nature — le seul producteur trouvé dans les arbres
   de référence est un outil de supervision NGemity visant un serveur tiers « ASER » (§5.3). Question
   précise : 60 appartient-il au protocole officiel (INCA), à un émulateur, ou aux deux ?
2. **Dans quelles conditions et à quelle cadence** 60 est émis (uniquement par un opérateur ? par un
   serveur de supervision ? jamais par un client de jeu ?).
3. **L'encodage exact de `command`.** rzu copie des octets bruts sans charset (`MessageBuffer.cpp:97-102`) ;
   le seul producteur connu écrit de l'ASCII hexadécimal (§5.3). Aucune source ne dit si une commande
   en clair est ou a été acceptée.
4. **La signification et la liste des valeurs de `t`.** Aucun enum, aucune table, aucune constante dans
   les quatre arbres (`TS_CS_REQUEST.h` fait 15 lignes). Seule valeur observée à ce jour : `'u'`.
5. **La liste des commandes légitimes** (liste blanche) : politique, cf. §8.
6. **Le traitement serveur attendu** : journaliser ? répondre ? sanctionner ? Rien dans les références
   ne le dicte.
7. **L'accusé réellement attendu par un client Epic 7.3** : aucun. `TS_SC_RESULT(request_msg_id = 60)`
   est l'attente d'un outil de supervision (§5.3, §5.5), pas celle du client.
8. **La borne à appliquer dans `NavisLamia`** : aucune source ne choisit entre la borne de champ de rzu
   (`< 65536`, `MessageBuffer.h:58-68`) et la borne de fait du dépôt (32768, `Connection.cs:27`). La
   recommandation de §9 est empirique.
9. **Le comportement à tenir devant une commande non terminée par un NUL** : rzu tronque silencieusement
   d'un octet (`MessageBuffer.cpp:131-132`), NGemity ferait pire (`MessageSerializerBuffer.cpp:57`), et
   aucun des deux ne refuse la trame. Aucune source ne tranche.

---

## 8. Levée de la réserve §7.4 de la fiche socle

La fiche socle (`socle-anti-triche.md:626-636`, branche `hermes/packet-socle-anti-triche`, MR #9)
recommandait de **ne pas laisser 60 être traité avant que Killian ait statué sur l'anti-triche**, au
motif qu'« un `TM_CS_REQUEST` non filtré est une surface d'attaque à lui seul ». La carte `t_b5cf5b0e`
demande de statuer. Formulation imposée par la carte, telle quelle :

> la recommandation vise un traitement qui exécuterait des commandes. Tant que NavisLamia se borne à
> déclarer l'identifiant, lire et borner la trame et la journaliser — sans exécuter aucune commande,
> sans réponse et sans sanction — la surface d'attaque redoutée n'existe pas, et la liste blanche des
> commandes reste une politique ouverte portée à l'arbitrage de Killian.

**Verdict : la réserve est levée**, et l'archéologie la renforce plutôt qu'elle ne la contredit.

- La surface redoutée est **conditionnée à une action que rien n'oblige** : rzu et Chihiro n'ont
  *aucun* consommateur pour 60 (§5.1, §5.2) ; le seul usage jamais observé est un canal SQL d'opérateur
  dans un **outil** tiers (§5.3). « Lire la trame » et « exécuter la commande » sont deux actes
  distincts ; le premier ne peut pas dégénérer en le second sans que quelqu'un écrive un déchiffreur,
  un exécuteur et une table de commandes — c'est-à-dire sans une décision explicite.
- Corollaire **exigible** du dev : aucune tentative de déchiffrement (`MXEncrypt`/« EV »/zlib), aucune
  exécution SQL, **aucune réponse** (répondre ferait du serveur un participant du canal), aucune
  sanction, et un simple journal (niveau Debug, comme le « paquet inconnu » de Chihiro `:160`) borné à
  la **longueur** de la commande — pas son contenu, qui est opaque et potentiellement volumineux.
- Le canal n'est pas anodin pour autant : §5.3 documente une charge utile SQL fournie par le client.
  La liste blanche, la journalisation du contenu et toute réponse restent donc des **politiques
  ouvertes** pour Killian (§12) : la levée porte sur le séquencement, pas sur ces politiques.

**Clause de révisabilité de la carte.** Aucune source lue ne démontre qu'un `TM_CS_REQUEST` doive être
déchiffré, exécuté, accusé ou puni pour être traité correctement ; la levée tient donc, sans réserve
contraire de l'archéologue. Elle est **vérifiable** par le QA en trois points sur la branche du dev :
(a) aucun déchiffrement introduit (ni `MXEncrypt`, ni tag `EV`, ni zlib, ni table de commandes) ;
(b) aucun `SendResult`/`TS_SC_RESULT` émis en réponse à 60 (`GameClient.cs:56-60` inchangé pour ce cas) ;
(c) aucun `SendDisconnectDesription`/`Disconnect` déclenché par 60 (`DisconnectType.cs` inchangé pour ce
cas). Si l'un des trois tombe, la levée doit être revue avant publication.

---

## 9. Cas limites à inventorier

Numérotés pour être cités tels quels en revue. « Refuser » = journal + `continue`, sans réponse.

| # | Cas | Décision recommandée | Motif / source |
|---|---|---|---|
| 1 | `Length < 9` (dont `Length = 7` : en-tête seul) | **refuser**, journal + `continue` | en dessous du minimum de §3.3 ; doctrine du dépôt : lire `HeaderSize = 7` puis refuser si `packet.Length < HeaderSize + charge` (`GameActionPackets.cs:8`, `:37-42`, `:58-63`, `:76-85`) |
| 2 | `Length = 9`, `command` vide (un NUL en offset 8) | **accepter**, `command` = chaîne vide | `SIZE_F_ENDSTRING2` avec `L = 0` (`PacketDeclaration.h:287`) ; ne pas confondre « commande vide » et « pas de commande » |
| 3 | dernier octet ≠ NUL (donc `Length = 8 + L`) | **refuser** (trame non conforme) ou lire les `Length - 9` premiers octets ; à trancher par le dev, **défaut recommandé : refuser** | le dépôt écrit `hasNullTerminator = true` ; une trame sans NUL ferait silencieusement perdre un octet chez rzu (`MessageBuffer.cpp:131-132`) — voir §7.9 |
| 4 | NUL **interne** à `command` | accepter : ne **jamais** s'arrêter au premier NUL ; la longueur est `Length - 9` | `std::string` conserve les NUL internes (rzu copie puis affecte des octets bruts) |
| 5 | `Length = 32768` (= tampon de réception) | accepter si la trame est intégralement reçue ; c'est la borne de fait | `Connection.cs:27` ; `GameClient.cs:564` |
| 6 | `Length > 32768` | jamais « reçu » : la boucle attend indéfiniment des octets qui ne peuvent pas tenir dans le tampon — à traiter comme **au mieux un journal**, au pire un déni de service lent | `Connection.cs:27` (`ReceiveBuffer`), `:314` ; `GameClient.cs:564-571` |
| 7 | `t` quelconque (aucune valeur n'est validable) | **ne rien filtrer** : aucune liste n'existe (§7.4) | ne pas inventer d'énumération de commandes |
| 8 | 60 reçu avant l'entrée en jeu | aucune garde de session dans la chaîne `GameClient.cs:590-789` ; le précédent du dépôt (550) journalise et abandonne si `ConnectionInfo.CharacterHandle == 0` | fiche 550 §5 ; `GameClient.cs` chaîne par id |
| 9 | rafale de 60 (volumétrie) | aucun limiteur par paquet dans le dépôt ; une trame de 32768 octets coûte peu — politique, pas structure | `Connection.cs:38` (`MaxSendBatchBytes` est côté envoi, non transposable) |
| 10 | deux trames concaténées dans un même segment TCP | sans objet : `Connection.Read(header.Length)` (`GameClient.cs:580`) isole la trame ; « fin de datagramme » ≠ « fin du tampon » | §3.4 |
| 11 | **`Length = 0` (hors périmètre, pré-existant et générique)** | **anomalie à signaler** : `Connection.Read(0)` renvoie 0 octet, `remainingData -= 0` et `_dataLength -= 0` laissent l'état inchangé → la boucle `while (remainingData >= 7)` de `GameClient.cs:559` **tourne sans fin** (rotation CPU sur la connexion) pour toute trame dont l'en-tête a un checksum valide et `Length = 0` (checksum = id seul ; pour id 60 : `0x3C`). Ce n'est pas un défaut de 60 mais un défaut de la boucle, atteignable par tout client modifié | `GameClient.cs:559-582` ; `Connection.cs:190-206` |
| 12 | client déjà en train de se déconnecter | si le traitement se limite à « lire + journaliser », rien à faire | doctrine « lire et borner » (§8) |
| 13 | commande volumineuse dans le journal | ne jamais journaliser le contenu ; journaliser `Length` et `L` | §8 ; le contenu est opaque (zlib+chiffrement), cf. §5.3 |

Le cas 11 est **indépendant de cette fiche** et concerne toutes les fiches de la famille : il est signalé
ici, pas corrigé (voir §12).

---

## 10. Zone de collision et fichiers à réserver

Le gating de 60 touche **deux fichiers déjà modifiés par trois branches ouvertes** :

| Branche (poussée, MR selon la carte) | Touche `GamePackets.cs` | Touche `GameClient.cs` | Ajoute |
|---|---|---|---|
| `hermes/packet-socle-anti-triche` (MR #9) | oui | oui | `Game/Network/Packets/Game/GameAntiHackPackets.cs` |
| `hermes/packet-57-check-illegal-user` (MR #15) | oui | oui | — (modifie `GameActionPackets.cs`) |
| `hermes/packet-59-xtrap-check` (MR #16) | oui | oui | `Game/Network/Packets/Game/GameXtrapPackets.cs` |

`hotspot: Game/Network/Packets/Enums/GamePackets.cs` et `hotspot: Game/Network/Clients/GameClient.cs` —
trois ajouts concurrents d'un membre d'énumération et d'un bras de chaîne. En conséquence :

- nom de fichier **libre** pour un lecteur de 60 : `Game/Network/Packets/Game/GameRequestPackets.cs`
  (vérifié : aucune des trois branches ne crée ce chemin) ;
- l'ajout du membre `TM_CS_REQUEST = 60` doit être une **ligne isolée** dans `GamePackets.cs`, pour rester
  fusionnable ;
- si 60 n'émet aucune réponse, `GameClient.SendResult` (`:56-60`) n'est pas touché, ce qui réduit la
  surface de conflit.

---

## 11. Bloc destiné à `CLAUDE.md` (à coller par Killian dans la description de la MR, jamais écrit par le dev)

```markdown
### Paquet 60 — `TM_CS_REQUEST` (client → serveur)

Seule trame **variable** de la famille anti-triche : `t` (`uint8`, offset 7) + `command`
(`endstring`, offset 8, `L` octets + **1 NUL terminal**), **`Length = 9 + L`**, checksum = somme des
6 premiers octets. Gating : **60** pour `version < EPIC_9_6_3`, 1060 au-delà — Epic 7.3 garde **60**
(`TS_CS_REQUEST.h:10-11`). Aucun champ n'a de gating propre. Borne : 32768 octets de tampon de
réception → `L ≤ 32759`.

`endstring` n'a **aucun préfixe de longueur** : la fin du champ est la fin du **datagramme**, donc
`L = packet.Length - 9`, jamais « jusqu'au premier NUL », et jamais « jusqu'à la fin du tampon ».

Le client Epic 7.3 **ne nomme pas** 60 (166 paires id→nom relevées, 60 absent alors que 50, 53, 54, 58,
59 sont présents) et **ne l'émet pas** (aucun écrit de `0x3c` dans une trame : le seul
`movl $0x3c,0x4(%eax)` est un objet C++ à vtable, pas une trame). En réception, une trame d'id 60 tombe
sur le cas par défaut du client (`table[60] = 0x67ef21`) qui journalise « message non traité » : **le
serveur ne doit jamais répondre avec une trame d'id 60**.

Ni `librzu` ni `Chihiro` n'ont de consommateur pour 60. Le seul producteur visible dans les arbres de
référence est l'outil de supervision NGemity (`Tools/ServerMonitor`), qui envoie `t = 'u'` et une
**requête SQL** chiffrée zlib + chiffrement simple, encodée en hexadécimal — donc le canal est un canal
d'**opérateur/SQL**, pas un canal de jeu. Il attend pour réponse le générique `TS_SC_RESULT (0)` avec
`request_msg_id = 60`.

Règle : **lire et borner, journaliser la longueur, n'exécuter aucune commande, ne pas répondre, ne pas
sanctionner.** Le `t` fait **1 octet** — ce n'est **pas** un `ResultCode` (qui est un `ushort`) : ne
jamais dériver une table de commandes des `ResultCode`. La liste blanche des commandes est une politique
ouverte pour Killian.
```

---

## 12. `A VERIFIER PAR KILLIAN`

1. **Traitement de 60.** La fiche recommande « déclarer, lire, borner, journaliser la longueur, sans
   réponse ni sanction » (§8, §9). Confirmez-vous ce défaut, ou voulez-vous un journal du **contenu**
   (opaque) et/ou un refus explicite avec `TM_SC_DISCONNECT_DESC` ?
2. **Liste blanche des commandes.** Faut-il un jour en définir une (et avec quelles commandes) ? Aucun
   des deux arbres de référence ne fournit ni liste ni exécuteur : c'est une décision produit.
3. **Réponse.** Confirmez-vous **aucun** `TM_SC_RESULT` pour 60 ? La seule forme d'accusé jamais
   observée (`TS_SC_RESULT`, `request_msg_id = 60`) est l'attente d'un **outil de supervision** NGemity
   visant un serveur tiers, pas d'un client 7.3 (§5.3, §5.5).
4. **60 avant l'entrée en jeu.** Journal seulement, ou abandon comme pour 550 (`CharacterHandle == 0`) ?
5. **Cas 11 de §9 (boucle de réception sur `Length = 0`).** Ce défaut est **générique** (toute trame,
   tout id) et atteignable par un client modifié : la boucle `GameClient.cs:559-582` tourne sans fin sur
   `Length = 0`. Il n'est pas dans le périmètre de cette fiche ; faut-il une carte dédiée
   (`navis-po` → `navis-dev`) ?
6. **`1060`.** Confirmez-vous qu'il n'a pas à être déclaré dans `GamePackets` (palier post-9.6.3) ?

### 12 bis. Points ajoutés par le dev (`navis-dev`, branche `hermes/packet-60-request`)

7. **Cas 3 de §9 — trame sans NUL terminal (§16.3).** La fiche laissait le choix au dev avec « refus »
   pour défaut recommandé : le code **refuse** (journal `Warning`, aucune réponse). Confirmez-vous, ou
   préférez-vous lire les `Length - 9` premiers octets d'une trame non terminée au lieu de la refuser ?
8. **Journal du sélecteur `t` (§16.4).** Le handler journalise `t` en **valeur brute** (sans nom, sans
   table) en plus de `Length` et de la longueur de `command` : c'est le seul moyen d'obtenir un jour la
   réponse au point 1 de `NON ÉTABLI` sans instrumenter `SFrame.exe`. Si vous préférez un journal
   strictement borné aux tailles, la ligne se retire sans rien casser.
9. **Placement de la ligne `TM_CS_REQUEST = 60` (§16.5).** Elle est volontairement posée après
   `TM_SC_DISCONNECT_DESC = 28`, hors de l'ancre `TM_CS_VERSION = 50` que se partagent les branches 54,
   57 et 59 : mesure à l'appui, ce placement fusionne là où l'autre produit un conflit dès la première
   fusion. Si vous préférez le regroupement avec les 50s quitte à traiter le conflit au merge, dites-le.

---

## 13. Commits et binaires épinglés

| Référence | Révision / empreinte | Ce qui a été lu |
|---|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (`87c1e83b packets: fix TS_SC_INVENTORY with older epics`) | `librzu/src/packets/GameClient/TS_CS_REQUEST.h`, `TS_SC_RESULT.h`, `TS_CS_VERSION.h`, `TS_CS_QUERY.h` ; `librzu/src/lib/Packet/PacketDeclaration.h`, `MessageBuffer.h`, `MessageBuffer.cpp`, `PacketEpics.h` ; `rzgame/src/StateHandler/GameHandler/GameHandler.cpp` |
| `reference/ngemity` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (`38ceb2c Fix compilation issue for GCC`) | `shared/Server/Packets/GameClient/TS_CS_REQUEST.h` ; `shared/Server/Packets/PacketDeclaration.h` ; `shared/Server/Packets/MessageSerializerBuffer.{h,cpp}` ; `shared/Server/ClientPackets.h` ; `shared/Encryption/cipher/XStrZlibWithSimpleCipherUtil.h` ; `Chihiro/src/Network/GameNetwork/WorldSession.cpp` ; `Tools/ServerMonitor/src/Client/MonitorSession.cpp` (+ `.h`), `Tools/ServerMonitor/src/ServerMonitor/ServerMonitor.cpp`, `Tools/Shizue/src/Main.cpp` |
| Client Epic 7.3 | `reference/client73/SFrame.exe`, sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 octets, `pei-i386`, 7 sections | lecture binaire (`objdump -d`, `objdump -h`, recherche d'octets) : chaînes de noms, table d'enregistrements id→nom (166 paires), table de discriminants entrante (VA `0x67f0a0`), table de sauts (`0x67f020`), constructeurs de trames |
| `NavisLamia` | base `master` = `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | `op_codes.md`, `GamePackets.cs`, `GameClient.cs`, `GameActions.cs`, `Packet.cs`, `Header.cs`, `PacketExtensions.cs`, `ResultCode.cs`, `TS_SC_RESULT.cs`, `DisconnectType.cs`, `Connection.cs`, `GameNpcDialogPackets.cs`, `CLAUDE.md`, `docs/` |

Emplacements VR cités (conversion `VA = 0x401000 + (offset_fichier - 0x400)` pour `.text` ;
`VA = 0xa0f000 + (offset_fichier - 0x60da00)` pour `.rdata`) :

| Objet | VA | Offset fichier |
|---|---|---|
| table de discriminants entrante (251 octets, index = id) | `0x67f0a0` | `0x27e4a0` |
| table de sauts entrante (32 dwords) | `0x67f020` | `0x27e420` |
| cas par défaut du dispatcheur (`unprocessed message : %d`) | `0x67ef21` | `0x27e321` |
| tronc commun / cas vide | `0x67ef39` | `0x27e339` |
| chaîne `TM_CS_REQUEST_CLEAR_SECURITY_NO` | `0xa52e30` | `0x651830` |
| chaîne `TM_CS_REQUEST_SECURITY_NO_CHANGE` | `0xa52e6c` | `0x65186c` |
| chaînes noms 50/9999/0 (`TM_CS_VERSION`, `TM_NONE`, `TM_SC_RESULT`) | `0xa53a04`, `0xa53a14`, `0xa539f4` | `0x652404`, `0x652414`, `0x6523f4` |
| insertion de la table id→nom | `0x674350` | `0x273750` |
| constructeur de trame (57) — modèle de l'idiome | `0x649609-0x649614` | `0x248a09-0x248a14` |
| lecture de l'id en sortie (`movzwl 0x4(%ebx),%ecx`) | `0x6498f0` | `0x248cf0` |

**Méthode écartée, pour mémoire** : exécuter `SFrame.exe`, un script Lua ou un script client (interdit
par le rôle). Toute cette fiche est établie par **lecture** : `objdump` sur le binaire, lecture des
sources des deux arbres de référence, lecture du dépôt.

---

## 14. Plan de vérification pour `navis-qa`

Cette fiche **ne modifie aucun fichier de code** ; il n'y a donc rien à compiler pour elle. Les points
suivants sont à vérifier sur la branche du dev qui la reprendra.

1. `dotnet build Navislamia.sln -c Debug` → **code 0**.
2. `dotnet test Tests/Tests.csproj` → **code 0**, et **au moins 448 tests** (compte relevé sur
   `master` `ec76b21` au moment de cette fiche : *« Passed! - Failed: 0, Passed: 448, Total: 448 »*).
   Le compte ne baisse jamais ; le plancher du profil (366) est déjà dépassé de 82.
3. Au moins **un test d'offsets** pour 60 : `Length` total = `9 + L` pour au moins deux valeurs de `L`
   (dont `L = 0`), position de `t` en 7, position du NUL terminal en `8 + L`, et checksum recalculé
   (formule §3.1, exemples §3.3).
4. Aucun membre de `GamePackets` sans bras : si `TM_CS_REQUEST = 60` est ajouté, la chaîne
   `GameClient.cs:590-789` doit porter le bras correspondant, sinon `Enum.IsDefined` laisse passer vers
   le `switch` final et le `throw` `Unknown Packet Type` (`:802`) devient atteignable.
5. `git log --oneline origin/master..master` vide (aucun commit sur `master` locale).
6. `docs/packet-specs/60-request.md` présent et commité sur la branche ; **`CLAUDE.md` non écrit par le
   dev** (bloc de §11 recopié dans la description de la MR).
7. Aucun champ `NON ÉTABLI` de §7 deviné dans le code (pas d'énumération de `t`, pas de liste blanche
   inventée, pas de réponse, pas d'exécution).

**État relevé sur la base `master` au moment de la fiche** : `dotnet build Navislamia.sln -c Debug` →
code de sortie **0** (160 avertissements, 0 erreur) ; `dotnet test Tests/Tests.csproj` → code de sortie
**0** (448 réussis, 0 échec, 0 ignoré, 945 ms). Relevé par l'archéologue, à rejouer par le QA.

---

## 15. Note de livraison

- Branche : **`hermes/packet-60-request`**, créée depuis `master` `ec76b21`.
- Livrable : **ce document uniquement** (`docs/packet-specs/60-request.md`). **Aucun fichier de code,
  de test ou d'instruction n'est touché** : ni `GamePackets.cs`, ni `GameClient.cs`, ni `CLAUDE.md`.
- `docs/packet-specs/` existait déjà sur `master` (4 fiches) : ce commit **ajoute** une fiche, il ne crée
  pas le répertoire.
- Points `NON ÉTABLI` : 9, listés §7. Aucun n'est comblé par une supposition.
- Réserves touchant d'autres tâches : le cas 11 de §9 (boucle de réception sur `Length = 0`) et la
  formulation approximative de `socle-anti-triche.md:631-632` sur le `t`, corrigée en §5.4.

---

## 16. Implémentation livrée par le dev

Cette section est écrite par `navis-dev` sur la branche `hermes/packet-60-request`, après la fiche. Les
sections 1 à 15 sont laissées intactes : elles sont l'archéologie de `navis-ref` et restent valides. Ce
qui suit **remplace la note de livraison §15 pour le périmètre du dev** : le code et les tests de 60 sont
désormais dans cette branche.

### 16.1 Fichiers et commits

| Commit | Contenu |
|---|---|
| `72327db` | `GamePackets.cs` (+7), `GameRequestPackets.cs` (nouveau, 101 lignes), `GameClient.cs` (+39) |
| `e5bf3c8` | `Tests/Game/RequestPacketsTests.cs` (nouveau, 25 tests, 412 lignes) |
| commit de documentation | cette section 16 et les points 7 à 9 de §12 bis |

Aucun autre fichier n'est touché : ni `CLAUDE.md` (Hermes le protège ; le bloc de §16.6 est destiné à la
description de la MR), ni `op_codes.md`, ni `GameActions.cs`, ni `Connection.cs`, ni `DisconnectType.cs`.

### 16.2 Ce que le code fait

- **`GameRequestPackets.TryReadRequest`** (fichier neuf `Game/Network/Packets/Game/`, le nom que §10
  réservait) : lit `t` en offset 7 et renvoie une **vue** des octets de `command`, `Length - 9` octets
  avant le NUL terminal. Aucune allocation, aucun décodage, aucune interprétation.
  Constantes publiées et testées : `SelectorOffset = 7`, `CommandOffset = 8`, `MinPacketSize = 9`,
  `MaxPacketSize = 32768`, `MaxCommandLength = 32759`.
- **`GameClient.HandleRequest`** : refuse la trame malformée (journal `Warning`) ou journalise en `Debug`
  `Length`, le sélecteur `t` **brut** et `commandLength`. Aucune réponse, aucune sanction, aucune
  exécution, aucun déchiffrement, aucune liste blanche, aucun journal du contenu de `command`.
- **`GameClient.OnDataReceived`** : bras `header.ID == (ushort)GamePackets.TM_CS_REQUEST` placé **avant le
  `switch` final**, à côté de l'autre bras « journaliser et abandonner » (`TM_SC_REGION_ACK`), et non à la
  fin de la chaîne : trois branches ouvertes (54, 57, 59) ajoutent le leur juste avant le `switch`, et ce
  bras isolé les laisse fusionner sans conflit. L'invariant §4 du profil est tenu : le membre d'énumération
  et son bras sont dans le même commit.
- **`GamePackets`** : `TM_CS_REQUEST = 60` en **ligne isolée**, volontairement hors de l'ancre
  `TM_CS_VERSION = 50` que 54, 57 et 59 se partagent (voir §16.5). `1060` n'est **pas** déclaré.

### 16.3 Décisions que la fiche laissait au dev (§9)

| Cas de §9 | Décision livrée | Pourquoi |
|---|---|---|
| 1 — `Length < 9` (dont 7, en-tête seul) | **refus**, journal `Warning`, `continue` | minimum de §3.3 ; doctrine du dépôt (`GameActionPackets`) |
| 2 — `Length = 9`, commande vide | **accepté**, `command` = vue de 0 octet | `SIZE_F_ENDSTRING2` avec `L = 0` |
| 3 — dernier octet ≠ NUL | **refus** (défaut recommandé par la fiche, tranché par le dev) | rzu perdrait silencieusement un octet ; rien n'établit qu'une commande sans terminator soit légitime |
| 4 — NUL **interne** | **accepté**, `command` = `Length - 9` octets, arrêt **jamais** au premier NUL | `std::string` conserve les NUL internes |
| 5 — `Length = 32768` | **accepté** (testé de bout en bout dans la boucle) | borne de fait de `Connection` |
| 7 — `t` quelconque | **rien n'est filtré**, aucune énumération, aucune table | §7.4 : aucune liste n'existe |
| 8 — 60 avant l'entrée en jeu | **journal seulement**, pas de garde de session | voir §16.4, point ouvert 7 de §12 |
| 13 — commande volumineuse | journal borné à `Length`, `t` et `commandLength` | §8 ; le contenu est opaque |

Le cas 6 (`Length > 32768`) n'est pas traitable dans le handler : une telle trame n'est jamais délivrée par
la boucle, qui attend des octets qui ne peuvent pas tenir dans le tampon. Le cas 11 (`Length = 0`) est le
défaut générique pré-existant signalé par la fiche : **non corrigé ici** (hors périmètre, il touche toutes
les fiches de la famille) et **non testable sans faire tourner la boucle à vide** ; il reste l'objet de la
question 5 de §12.

### 16.4 Ce qui n'est pas porté, et pourquoi

Rien n'a été porté des références, parce qu'il n'y a rien : rzu déclare `TS_CS_REQUEST` sans jamais le
consommer, et Chihiro tombe dans sa branche « paquet inconnu » (journal Debug, connexion conservée). Le
seul producteur connu est l'outil de supervision NGemity, qui vise un serveur tiers : il renseigne la
**nature** du canal (opérateur, SQL chiffré, `t = 'u'`), jamais le **traitement serveur**. En conséquence,
le code ne contient : ni `MXEncrypt`/tag `EV`/zlib, ni table de commandes, ni `SendResult`, ni
`SendDisconnectDesription`/`Disconnect` — les trois points de vérification exigés par §8 restent donc
vérifiables tels quels sur cette branche.

Le sélecteur `t` est **journalisé tel quel** (valeur brute, sans nom ni table) : c'est le seul moyen de
répondre un jour au point 1 de `NON ÉTABLI` (le client émet-il 60 ?) sans instrumenter le binaire. La
fiche interdit de journaliser le **contenu** de `command` ; elle ne nomme pas `t`, qui est un champ déclaré
et non la charge utile opaque.

### 16.5 Mesures relevées sur la branche

| Mesure | Commande | Résultat |
|---|---|---|
| Build | `dotnet build Navislamia.sln -c Debug` | code de sortie **0** — 160 avertissements, 0 erreur (identique à `master`) |
| Tests | `dotnet test Tests/Tests.csproj` | code de sortie **0** — **473 réussis**, 0 échec, 0 ignoré |
| Baseline `master` `ec76b21` relevée avant de commencer | mêmes commandes | build 0 ; tests **448** réussis |
| Tests ajoutés | — | **+25** (`Tests/Game/RequestPacketsTests.cs`), aucun test existant modifié |
| `git log --oneline origin/master..master` | — | **vide** — aucun commit sur `master` locale |

**Fusionnabilité de la ligne d'énumération, mesurée.** Un banc d'essai local (clone dans `/tmp`, aucune
écriture sur le dépôt) a fusionné en séquence `socle-anti-triche`, `57-check-illegal-user` et
`59-xtrap-check` sur deux variantes de placement de la ligne `TM_CS_REQUEST = 60` :

| Placement de la ligne | Résultat de la séquence de fusions |
|---|---|
| juste après `TM_CS_VERSION = 50,` (ancre des trois branches) | **conflit dès la première fusion** (`GamePackets.cs`) |
| juste après `TM_SC_DISCONNECT_DESC = 28,` (retenu) | fusionne avec `socle-anti-triche` ; le conflit qui suit vient de `57` contre `socle` |

Le conflit `57`/`socle` est **pré-existant** : le même banc, sans aucun ajout de cette branche, le
reproduit à l'identique sur `Game/Network/Clients/GameClient.cs` et
`Game/Network/Packets/Enums/GamePackets.cs`. La ligne isolée de 60 n'y ajoute donc aucun conflit, ce qui
est exactement ce que §10 demandait.

### 16.6 Bloc destiné à `CLAUDE.md`

Le dev n'écrit pas `CLAUDE.md` (Hermes le protège). La fiche §11 en propose déjà une version ; celle-ci
est la version courte et **exacte après implémentation**, à coller dans la description de la MR :

```markdown
### Paquet 60 — `TM_CS_REQUEST` (client → serveur)

Seule trame **variable** de la famille : `t` (`uint8`, offset 7) + `command` (`endstring`, offset 8,
`L` octets + **1 NUL terminal**), **`Length = 9 + L`**, checksum = somme des 6 premiers octets. Gating :
**60** pour `version < EPIC_9_6_3`, 1060 au-delà — Epic 7.3 garde **60** ; aucun champ n'a de gating
propre. Borne réelle : tampon de réception de 32768 octets → `L ≤ 32759`.

`endstring` n'a **aucun préfixe de longueur** : la fin du champ est la fin du **datagramme**, donc
`L = packet.Length - 9`, jamais « jusqu'au premier NUL » et jamais « jusqu'à la fin du tampon ». Une
trame dont le dernier octet n'est pas le NUL, ou de moins de 9 octets, est refusée.

Le client 7.3 ne nomme ni n'émet 60, et n'a aucun bras en réception (une trame d'id 60 y tombe sur
« message non traité ») : **le serveur ne répond jamais par une trame d'id 60**. Ni rzu ni Chihiro n'ont
de consommateur ; le seul producteur connu est l'outil de supervision NGemity, qui envoie `t = 'u'` et
une requête SQL chiffrée zlib + chiffrement simple encodée en hexadécimal — c'est un canal
d'**opérateur/SQL**, pas un canal de jeu.

Règle tenue par `GameRequestPackets` / `GameClient.HandleRequest` : **lire et borner, journaliser
`Length`, `t` et la longueur de `command`, n'exécuter aucune commande, ne pas répondre, ne pas
sanctionner, ne jamais journaliser le contenu**. Le `t` fait **1 octet** — ce n'est **pas** un
`ResultCode`. Liste blanche et réponse restent des politiques ouvertes.
```

### 16.7 Réserves du dev

1. Le cas 11 de §9 (`Length = 0` ⇒ boucle de réception qui tourne) **reste ouvert** et n'est pas corrigé
   ici : il est générique, pré-existant, et le corriger change la boucle partagée par toutes les fiches.
2. La fidélité de `Length = 9 + L` repose sur la lecture de rzu (§3.3) ; **aucune trame réelle de 60 n'a
   été observée** — le seul producteur connu n'est pas un client 7.3. Si un jour une capture montre un
   terminator absent ou un second NUL, le refus du cas 3 est le premier point à revoir.
3. Le journal de `t` est un choix du dev (voir §16.4) : il peut être retiré sans rien casser si Killian
   préfère un journal strictement borné à la taille.
