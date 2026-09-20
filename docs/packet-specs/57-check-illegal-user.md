# 57 — `TM_CS_CHECK_ILLEGAL_USER` (client → serveur, sans réponse)

Fiche d'archéologie de protocole, Epic 7.3. Écrite en **lecture seule** sur les références
(`reference/rzu`, `reference/ngemity/Chihiro`, `reference/client73`) : aucun Lua, aucun script du
client, aucun exécutable client n'a été lancé. Le désassemblage de `SFrame.exe` a été produit hors
ligne par `objdump -d` (2 s, 2 256 750 lignes) et **n'est pas** une exécution du binaire. rzu tranche
la forme, la taille et le gating ; NGemity ne tranche rien ici puisqu'il ne traite pas le paquet ;
le client 7.3 tranche l'émission (§2).

Résumé des arbitrages demandés :

| Question | Verdict de cette fiche |
|---|---|
| Le client 7.3 émet-il 57 ? | **Oui** : il contient un constructeur dédié qui écrit une trame de **11 octets** dont l'id vaut `0x39` (57), et un unique chemin d'envoi (§2, §3.1). Le **déclencheur** de ce chemin n'est pas dans le client extrait → `NON ÉTABLI` (§7a). |
| Format et gating à 7.3 | **11 octets** sur le fil : en-tête de 7 + `log_code` `uint32` à l'offset 7. Gating rzu `X(57, version < EPIC_9_6_3)` → **57** (et non 1057) (§4). |
| Réponse attendue du serveur | **Aucune.** Il n'existe aucune trame S→C de cette famille dans rzu, NGemity ni `op_codes.md`, et le répartiteur entrant du client place 57 sur le chemin par défaut (§5.4). |
| Sort de `log_code` | `NON ÉTABLI` : ni rzu ni NGemity n'ont de handler, et aucun des deux ne dit ce que le serveur en ferait. Trois questions distinctes portées dans `## A VERIFIER PAR KILLIAN` (§5.5, §7b). |
| Que fait NavisLamia aujourd'hui d'un 57 ? | Rien d'observable : l'id n'est pas dans `GamePackets`, la trame est **journalisée en `Debug` puis jetée**, sans désalignement ni réponse (`GameClient.cs:584-588`) (§5.3). |
| Zone de collision | La branche `hermes/packet-socle-anti-triche` (MR #9 ouverte) modifie les **deux mêmes fichiers**, `GamePackets.cs` et `GameClient.cs` (§1, §5.6). |

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id client → serveur | **57** | `op_codes.md:38` (`[57] = "TM_CS_CHECK_ILLEGAL_USER"`, entre `:37` « 56 » et `:39` « 58 ») |
| Nom | `TM_CS_CHECK_ILLEGAL_USER` | `op_codes.md:38` |
| Id alternatif | `1057` à partir d'`EPIC_9_6_3` | `reference/rzu/librzu/src/packets/GameClient/TS_CS_CHECK_ILLEGAL_USER.h:10` |
| Référence rzu | `_(simple)(uint32_t, log_code)` ; `CREATE_PACKET_VER_ID(...)` | `…/TS_CS_CHECK_ILLEGAL_USER.h:5-6`, `:12` |
| Référence NGemity | même champ unique ; `CREATE_PACKET(TS_CS_CHECK_ILLEGAL_USER, 57)` | `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_CHECK_ILLEGAL_USER.h:6-7`, `:9` ; `shared/Server/ClientPackets.h:57` ; include dans `shared/Server/XPacket.h:81` |
| Trame serveur → client | **aucune** | `find /srv/navislamia/reference -iname '*ILLEGAL*'` → 2 fichiers, tous deux `TS_CS_*` ; `grep -n '1057' op_codes.md` → 0 ligne |
| État dans NavisLamia | **absent** : aucun membre 57 dans `GamePackets` (fichier de 102 lignes ; voisins présents `:84` `TM_CS_LOGOUT = 27`, `:87` `TM_CS_VERSION = 50`, `:99` `TM_CS_REPORT = 8000`, `:101` `TM_NONE = 9999`) ; `grep -rn 'ILLEGAL' --include=*.cs .` → 0 résultat | `Game/Network/Packets/Enums/GamePackets.cs` |
| Branche anti-triche | `origin/hermes/packet-socle-anti-triche` (MR #9, ouverte) ajoute `TM_CS_ANTI_HACK = 54` à `GamePackets.cs:77` de sa branche et **rien** pour 57 | `git show origin/hermes/packet-socle-anti-triche:Game/Network/Packets/Enums/GamePackets.cs` |
| Taille sur le fil | **11 octets** | §3 |

La valeur 57 est libre dans `GamePackets` : les identifiants déclarés qui l'encadrent sont 28
(`TM_SC_DISCONNECT_DESC`, `:85`), 50 (`TM_CS_VERSION`, `:87`) et 100 (`TM_CS_ATTACK_REQUEST`, `:23`) —
aucun membre ne prend 57. `op_codes.md` ne connaît pas non plus de `[1057]` : à 7.3, c'est **57**.

## 2. Ce que le joueur fait pour que le client l'envoie

**Rien.** 57 n'est déclenché par aucune action d'interface, aucun écran, aucun bouton : c'est un
**rapport automatique de la surveillance interne du client**. Le seul chemin d'émission trouvé dans
`SFrame.exe` est le suivant (toutes les adresses sont des VA du binaire
`reference/client73/SFrame.exe`, sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`,
9 841 664 octets ; méthode : `objdump -d`, lecture d'adresses, aucune exécution) :

1. une chaîne de comparaisons de noms d'événements teste le nom contre `"game_security_msg"`
   (`push $0xa55448` VA `0x69436b`, comparaison type `strcmp` VA `0x977ff7`, branche `jne 0x6943d2`
   VA `0x69437b`). La chaîne `game_security_msg` est, dans le répertoire client extrait, **présente une
   seule fois au monde, dans `SFrame.exe`** (balayage des 53 fichiers de `reference/client73`) : c'est
   un nom d'événement **Lua**, et rien dans le client extrait ne le publie ;
2. si le nom correspond, le code construit un petit objet de 8 octets (VA `0x69437d`-`0x694396` :
   `operator new(8)` puis `movl $0x17,(%eax)` et `mov %esi,0x4(%eax)` avec `%esi = 0`) et le remplit
   avec la conversion `atoi`-like (thunk VA `0x97762c` → VA `0x977616` : `push $0xa` / `push $0x0` /
   `push ptr` / `call 0x978480`) d'un champ chaîne de l'objet événement, avant d'appeler
   `0x64a150` avec cet objet (VA `0x6943ba`, **unique appelant** de `0x64a150`) ;
3. la fonction `0x64a150` répartit sur le **premier** mot de cet objet : `mov 0x8(%ebp),%eax` /
   `mov (%eax),%eax` / `cmp $0x16,%eax` / `ja 0x64a333` (VA `0x64a16b`-`0x64a17d`) puis saut par
   table `jmp *0x64a348(,%eax,4)` (VA `0x64a184`). Le **chemin par défaut ne fait rien** : au-delà de
   22, la fonction sort par `0x64a333` sans construire ni envoyer quoi que ce soit ;
4. pour les codes 0 à 22 **sauf le 9** (dont l'entrée de table pointe directement sur la sortie, §2.7),
   le tronc commun (VA `0x64a2a8`-`0x64a2b0`) construit la trame 57 avec le
   constructeur dédié VA `0x6495e0` (instruction d'id : `mov $0x39,%edx` / `mov %dx,0x4(%eax)`, VA
   `0x649609` / `0x64960e`) ;
5. il recopie **le second mot de l'objet événement** (+4) dans les 4 octets de charge de la trame :
   `mov 0x8(%ebp),%ecx` / `mov 0x4(%ecx),%edx` / `mov %edx,-0x15(%ebp)` (VA `0x64a2b5`-`0x64a2c1`,
   `-0x15(%ebp)` = buffer`+7`, le buffer étant à `-0x1c(%ebp)`). Au seul site d'appel existant, ce
   second mot a été initialisé à **0** juste avant (VA `0x69438d`, `mov %esi,0x4(%eax)` avec
   `%esi = 0`),
6. il l'envoie : `lea -0x1c(%ebp),%eax` / `push %eax` / `push %edx` / `call 0x6498e0` (VA
   `0x64a2ce`-`0x64a2d3`) — voir §3.3 pour la justification que `0x6498e0` est bien la routine
   d'émission sortante ;
7. il empile enfin une **boîte de message localisée** : `operator new(0x61)` (VA `0x64a2e7`),
   constructeur VA `0x490c50`, champs `+0x13` = EDI et `+0x18` = EBX (VA `0x64a316` / `0x64a319`),
   insertion dans la file à `this+0x48` (VA `0x767140`). La table de saut a **23 entrées** (lues
   octet à octet à la VA `0x64a348`) ; les 22 blocs de cas vont de la VA `0x64a18b` à `0x64a2a3` et
   renseignent chacun un couple (ESI, EBX) plus EDI :

   | code | bloc | ESI (+0x32…+0x48) | EBX (+0x18) | EDI (+0x13) |
   |---|---|---|---|---|
   | 0 | `0x64a18b` | `0x32` | `0x32a` | 0 |
   | 1…8 | `0x64a19a`…`0x64a203` | `0x33`…`0x3a` | `0x32b`…`0x332` | 1 |
   | 9 | `0x64a332` | — | — | — : **aucun envoi**, la cible est la sortie du tronc commun |
   | 10…13 | `0x64a212`…`0x64a23c` | `0x3c`…`0x3f` | `0x333`…`0x336` | 1 |
   | 14 | `0x64a29c` | `0x40` | `0x6b` (valeur initiale, VA `0x64a177`) | 1 |
   | 15 | `0x64a2a3` | `0x41` | `0x6b` (valeur initiale) | 1 |
   | 16…22 | `0x64a248`…`0x64a290` | `0x42`…`0x48` | `0x312`, `0x312`, `0x313`, `0x314`, `0x315`, `0x315`, `0x316` | 1 |

   Le cas 11 est le seul à passer par l'entrée secondaire VA `0x64a2ad` avec `lea -0x3b(%esi),%edi`
   (EDI = 2) ; le cas 0 garde EDI = 0 (`xor %edi,%edi`, VA `0x64a172`).

Conséquence pratique : un 57 n'arrive **jamais** à la suite d'un geste du joueur ; il arrive quand le
client croit avoir détecté un programme illégal. La boîte de message qui l'accompagne est du même
vocabulaire que les clés `msgboxdetect_*` (`game_hack`, `general_hack`, `module_change`, `automacro`,
`speedhack`, `speedhack_app`, `messagehook`, `hookfunction`, `driverfailed`, `kdtrace`,
`kdtrace_changed`, `automouse`, table de pointeurs en `.data` VA `0xc1c0e0`-`0xc1c14c`) et que
`smsq_protect01`…`smsq_protect23` de `db_string.rdb` (« Game Guard is running »,
« Illegal program detected.<BR>… »). **La correspondance exacte entre les valeurs `0x312`-`0x336` et
ces clés n'est pas établie** (§7e) : la table `.data` n'est référencée par aucune adresse de `.text`
(`grep` des 32 bits `0xc1c0e0`…`0xc1c14c` dans `.text` → 0 occurrence), comme déjà relevé par la fiche
socle (§4.4).

**Réserve de méthode, importante.** La chaîne `TM_CS_CHECK_ILLEGAL_USER` est **absente** du binaire
(`strings -n 6 SFrame.exe | grep -c 'ILLEGAL_USER'` → 0 ; recherche d'octets `ILLEGAL_USER` et
`CHECK_ILLEGAL` → 0 occurrence) et la table id→nom du client ne nomme pas 57 (fiche socle §4.2) :
le nom du paquet est **rzu/NGemity**, pas le client. L'identification de la trame repose donc sur
l'id `0x39` écrit dans un en-tête constructeur, sur la taille 11, et sur le fait que cette trame passe
par la routine d'émission sortante — pas sur une chaîne de caractères. C'est ce qui rend le point 6
ci-dessus structurant.

## 3. Structure sur le fil

### 3.1 Table des offsets — `TM_CS_CHECK_ILLEGAL_USER` — client → serveur — **11 octets**

| Offset | Taille | Type | Champ | Valeur observée | Source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` LE | `Length` | **11** (`0x0B`) | rzu `TS_CS_CHECK_ILLEGAL_USER.h:6` (4 o de charge + 7) ; client : `movl $0xb,(%eax)` VA `0x649614` |
| 4 | 2 | `uint16` LE | `ID` | **57** (`0x39`) | `op_codes.md:38` ; rzu `…:9-10` ; client : `mov $0x39,%edx` VA `0x649609` + `mov %dx,0x4(%eax)` VA `0x64960e` |
| 6 | 1 | `uint8` | `Checksum` | somme des octets **0 à 5** | client : boucle VA `0x649620`-`0x649627` (position du total : `lea 0x6(%eax),%ecx` VA `0x6495e2`) ; dépôt : `Game/Network/Packets/PacketExtensions.cs:13-25` |
| 7 | 4 | `uint32` LE | `log_code` | **0** au seul site d'appel observé | rzu `…:6` (`_(simple)(uint32_t, log_code)`) ; client : écriture VA `0x64a2c1` depuis l'offset +4 de l'objet événement |

**Taille totale : 11 octets** (`7 + 4`). Aucun rembourrage : le constructeur client **remet à zéro**
les octets 7 à 10 (`mov %edx,0x4(%eax)`, `mov %dx,0x8(%eax)`, `mov %dl,0xa(%eax)`, VA
`0x6495ff`-`0x649606`) puis n'écrit que ces 4 octets ; la charge est donc exactement un `uint32`.

### 3.2 Les quatre champs, un par un

- `Length` — le client l'écrit en dur à 11 après avoir posé un en-tête provisoire de 7
  (`movl $0x7,(%eax)` VA `0x6495e8`) : la trame est de taille **fixe**, il n'existe pas de variante.
- `ID` — 57 écrit sur 2 octets (`%dx`), donc `0x0039`.
- `Checksum` — le client calcule la somme des **six** octets `Length`(0-3) + `ID`(4-5) et la range en
  offset 6 ; c'est exactement `Header.CalculateChecksum()` du dépôt (`PacketExtensions.cs:17-22`), la
  même règle qui valide les trames entrantes (`GameClient.cs:562`). **La charge n'entre pas dans la
  somme** : un `log_code` modifié en transit ne casse pas le checksum.
- `log_code` — unique champ de charge. **Son ordre d'octets n'est pas observable chez le client** :
  la seule valeur observée est `0`, qui ne discrimine pas l'endianness. La source d'ordre est rzu :
  `_(simple)(uint32_t, …)` est un type scalaire du sérialiseur, écrit tel quel sur une plateforme
  x86/Windows — donc **petit-boutiste**, comme les autres `uint32` du dépôt. `log_code` est un **nom
  rzu** : la chaîne `log_code` n'existe pas dans le client (0 occurrence).

### 3.3 L'émission, contre-épreuve

L'appel de la VA `0x64a2d3` vise `0x6498e0` (corps VA `0x6498e0`-`0x649abe`, `__stdcall` à deux
arguments : `ret $0x8` VA `0x649abe`), qui :

- lit l'id du paquet reçu en second argument (`mov 0xc(%ebp),%ebx`, puis `movzwl 0x4(%ebx),%ecx`)
  et trace `"!!!ConnPendMessage : ID: %d  Size: %d\n"` (format VA `0xa4d724`) ;
- résout le **nom** du paquet (`call 0x674540` avec l'id et un `std::string` en `.data` VA `0xc4ebe8`)
  et trace `"네트워크 메세지 Send : %s [%d]\n"` — « message réseau Send : nom [id] » en euc-kr — via le
  format VA `0xa4d700`, **référencé une seule fois dans tout `.text`, en VA `0x649972`** ;
- termine par un appel virtuel qui reçoit le **buffer du paquet** en argument (`push %ebx` VA
  `0x6499ad` puis `call *%eax` VA `0x6499ae`, et de même VA `0x649a31`-`0x649a32`).

C'est le seul point de `.text` qui associe un id, le nom résolu et un envoi : la routine est la
**routine d'émission sortante** du client, et elle reçoit bien la trame de 11 octets construite juste
avant. Deux traces de plus justifient la même lecture : `0x6498e0` a 12 appelants, dont `0x649ca2` et
`0x649d06` qui lui passent la trame construite par le constructeur voisin VA `0x649590` — une trame
d'id **7** (`TM_CS_REGION_UPDATE`) de 24 octets — paquet que le client 7.3 envoie effectivement en se
déplaçant.

## 4. Gating de version

| Palier | Source | Valeur |
|---|---|---|
| `EPIC_7_3` | `reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59` | `0x070300` |
| `EPIC_9_6_3` | `reference/rzu/librzu/src/lib/Packet/PacketEpics.h:96` (commenté « GS packet ID modified with version 20200713 ») | `0x090603` |

Gating déclaré : `X(57, version < EPIC_9_6_3)` / `X(1057, version >= EPIC_9_6_3)`
(`TS_CS_CHECK_ILLEGAL_USER.h:9-10`).

**Décision pour 7.3 : l'identifiant est 57.** `0x070300 < 0x090603`, la branche `1057` est
inatteignable à ce palier. La valeur 1057 **ne doit pas être déclarée** : `op_codes.md` ne la connaît
pas, et l'ajouter ferait entrer un identifiant 9.6.3 dans une énumération 7.3.

- NGemity ne porte **aucun axe de version** : `CREATE_PACKET(TS_CS_CHECK_ILLEGAL_USER, 57)`
  (`…/TS_CS_CHECK_ILLEGAL_USER.h:9`) sur une base compilée à `EPIC_4_1_1`
  (`reference/ngemity/shared/Common/Define.h:25`). Les deux références donnent donc **le même
  numéro** ; il n'y a pas d'écart à arbitrer à ce palier.
- Le gating versionné de rzu date du commit `11f2b6f` (2020-08-11, « packets: use versionned ID for
  all packets and update their ID with epic 9.6.3 ») ; la déclaration du paquet, elle, est plus
  ancienne (`d7c58ee`, 2017-02-07), donc **antérieure** à 9.6.3 : le numéro 57 est bien celui des
  époques anciennes (§8).
- Corollaire pour la famille : 54 (`TM_CS_ANTI_HACK`), 55, 56, 57, 58, 59 et 60 basculent **tous** en
  `10xx` à 9.6.3. Toute fiche de cette famille doit trancher son gating de la même façon ; celle-ci ne
  tranche que 57.

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` fait du paquet : rien

`reference/ngemity` porte la **déclaration** du format et **aucun handler** :

- `grep -rn 'ILLEGAL' Chihiro/src` → **0** occurrence ;
- la table de dispatch du monde (`declareHandler(...)`, table `worldPacketHandler` refermée par
  `constexpr int32_t worldTableSize = …` en `Chihiro/src/Network/GameNetwork/WorldSession.cpp:138`)
  ne contient aucune entrée 57 ;
- `ignoredPackets` (`WorldSession.cpp:140-141`) ne liste que `TS_CS_VERSION`, `TS_CS_VERSION2`,
  `TS_CS_UNKN`, `TS_CS_REPORT`, `TS_CS_TARGETING` — 57 n'y est pas ;
- un 57 tombe donc dans le journal de débogage `WorldSession.cpp:159-160` (« Got unknown packet
  '%d' ») et `ProcessIncoming` rend `ReadDataHandlerResult::Ok` : **la connexion est conservée et
  aucune réponse n'est envoyée**.

NGemity est ici muet : c'est le constat, pas une lacune de recherche. Un serveur qui ne fait que
journaliser fait exactement ce que la référence la plus complète du projet fait.

### 5.2 Ce que rzu fait du paquet : il le jette dans son proxy

rzu ne porte aucune logique de serveur pour 57, mais **une** occurrence de code, dans son proxy :

- `rzfilter/filter/converter/SpecificPacketConverter.cpp:443-444` : une branche explicite
  `else if(packet->id == TS_CS_CHECK_ILLEGAL_USER::getId(version)) { return false; }` ;
- `convertGamePacketAndSend` rend `true` quand il n'a **pas** traité le paquet spécifiquement (c'est
  le cas de toutes les autres branches) et `false` quand il l'a traité — voir la même fonction pour
  `TS_CS_REPORT` (`:494-500`, « Already sent with version ») et pour le paquet factice 9999 ;
- `PacketConverterFilter::convertPacketAndSend:65` ne convertit donc rien pour 57, et rend `false`
  (`:93`) ; `FilterProxy::onClientPacket:33-34` n'envoie vers le serveur que si la valeur est vraie.

Effet net : **le proxy rzu ne convertit pas 57 et ne le transmet pas**. La branche est prise avant
tout test de mode strict (`isStrictForwardEnabled`, `:87`), donc c'est vrai aussi en mode strict. La
branche ne porte aucun commentaire : l'effet est certain, l'intention ne l'est pas — mais elle
démontre qu'un proxy rzu de production juge le paquet **inutile au serveur**, ce qui conforte le
§5.4.

### 5.3 Ce que NavisLamia fait d'un 57 reçu : le jette proprement

`Game/Network/Clients/GameClient.cs:584-588` : `!Enum.IsDefined(typeof(GamePackets), header.ID)`
→ `_logger.Debug("Undefined packet ID: {id} Length: {length}) received from {clientTag}", …)` puis
`continue`. La trame a déjà été lue intégralement (`Connection.Read((int)header.Length)`, `:580`, et
`remainingData -= msgBuffer.Length`, `:582`), donc **le flux reste aligné, aucun octet du paquet
suivant n'est consommé**, la connexion reste ouverte et rien n'est envoyé en retour. Le datagramme est
donc, aujourd'hui, sans autre effet qu'une ligne de journal en niveau `Debug`.

### 5.4 Ce que le serveur doit répondre : rien

Trois vérifications indépendantes, refaites ici :

1. **Aucune trame S→C de cette famille n'existe** : `find /srv/navislamia/reference -iname
   '*ILLEGAL*'` ne rend que `TS_CS_CHECK_ILLEGAL_USER.h` (rzu et NGemity), deux fichiers
   client → serveur. Pas de `TS_SC_CHECK_ILLEGAL_USER`. `op_codes.md` ne nomme ni `[1057]` ni de
   pendant serveur.
2. **Le répartiteur entrant du client 7.3 n'a pas de cas pour 57** — revérifié ici octet à octet,
   indépendamment de la fiche socle : la table d'octets des discriminants est en `.text` à la VA
   `0x67f0a0` (251 entrées) et la table de sauts à la VA `0x67f020` (32 entrées ; l'entrée 31 pointe
   sur `0x0067ef21`, l'entrée 14 sur `0x0067ef39`). Lecture directe du fichier :
   `0x67f0a0 + 57` → **31** → `0x0067ef21` (**chemin par défaut**). Contrôle : les ids 53, 55 et 58
   donnent 14 → `0x0067ef39` (cas explicite vide du tronc de sortie), et les ids 1, 5, 7, 20, 50
   (client → serveur) donnent tous 31 → `0x0067ef21`, comme le veut la direction. Aucun identifiant
   client → serveur n'est « traité », donc **rien dans le client ne lit 57 et rien n'attend de
   réponse**.
3. **Le client n'attend pas de réponse pour continuer** : la boîte de message est construite et mise
   en file **immédiatement après** l'envoi (VA `0x64a2d3` puis `0x64a2e7`), sans qu'aucun état
   d'attente ne soit posé.

**Verdict : le serveur ne doit rien répondre.** Le seul comportement qui reste à choisir est
d'ordre **éditorial et politique** (journaliser ? à quel niveau ? sanctionner ?) et il n'est tranché
par aucune référence (§5.5, §7b).

### 5.5 Ce qui reste à trancher pour `log_code`, et ce qui ne l'est pas

Trois questions **distinctes**, dans les termes de la carte :

| Question | Ce que disent les sources | État |
|---|---|---|
| Journaliser ? | Aucune référence ne journalise `log_code`. NavisLamia connaît aujourd'hui le seul `_logger.Debug` de `GameClient.cs:586`. | `NON ÉTABLI` — décision de Killian |
| Répondre ? | Aucune trame de réponse n'existe (§5.4). | **tranché par cette fiche : non** |
| Sanctionner / déconnecter ? | Aucune des trois références ne sanctionne. NGemity journalise en `debug` et garde la connexion (`WorldSession.cpp:159-160`). | `NON ÉTABLI` — décision de Killian |

Ce que la fiche peut affirmer sans décider : le nom `log_code` vient de rzu, le client envoie `0`
sur le seul chemin trouvé, et **rien** dans les références ne définit sa valeur ni son emploi.

### 5.6 Où le traitement s'insérerait, et les cas limites

Deux idiomes existent déjà dans le dépôt ; la fiche ne tranche pas lequel retenir :

- la chaîne de `if (header.ID == (ushort)GamePackets.X) { …; continue; }` de `GameClient.cs` —
  `TM_CS_GAME_TIME` `:596-600`, `TM_CS_MOVE_REQUEST` `:602-606`, `TM_CS_REGION_UPDATE` `:608-612`,
  `TM_CS_GET_REGION_INFO` `:614-618` — **avant** le `switch` final `:791-803` dont le bras `_` lève
  `Unknown Packet Type` (`:802`). C'est le placement obligé de tout membre ajouté à `GamePackets`
  (critère transversal n° 4 : enum et dispatch modifiés ensemble) ;
- la table `_actions` de `Game/Network/Clients/Actions/GameActions.cs:43-50` (8 entrées, alimentée par
  `_actions.Add((ushort)GamePackets.X, OnX)`, consultée par `_actions.TryGetValue(packet.Id, …)` en
  `:55`), qui suppose la construction d'un `Packet<T>` par le `switch` final.

Le dépôt a un **idiome de lecture défensive** à réutiliser plutôt qu'un accès direct au buffer :
`Game/Network/Packets/Game/GameActionPackets.cs:237-250` (`TryReadGetRegionInfo`) teste
`packet.Length != HeaderSize + 8` et rend `false` plutôt que de lire un buffer court ;
`GameClient.cs:175-179` journalise alors en `Warning`. C'est nécessaire ici aussi, parce que
`Packet<T>(byte[])` copie `Marshal.SizeOf<T>()` octets depuis l'offset 7 sans vérifier `Length`
(`Game/Network/Packets/Packet.cs:81-84`) : construire le paquet sans contrôle de taille lèverait une
`ArgumentException` au milieu de la boucle de lecture.

Cas limites à couvrir, quel que soit le choix :

1. **trame plus courte que 11 octets** (par exemple `Length = 7`, en-tête seul) : c'est une anomalie
   (le client écrit toujours 11, VA `0x649614`) ; à refuser avant toute lecture de `log_code`, sans
   réponse et sans désaligner le flux ;
2. **trame plus longue que 11** : `Length` est écrit en dur à 11 par le client ; une longueur
   supérieure est anormale, et le surplus serait consommé par `Connection.Read` — donc accepté ou
   refusé, mais jamais ignoré en laissant des octets dans le flux ;
3. **`log_code = 0`** : c'est la valeur que le client émet sur le seul chemin connu (§2.5) —
   à ne pas traiter comme une absence d'information sans décision (§7b) ;
4. **avant l'entrée en jeu** : la routine d'émission est une routine standard, rien ne garantit
   qu'elle ne parte qu'en jeu ; le dépôt a déjà ce réflexe pour d'autres paquets
   (`ConnectionInfo.CharacterHandle == 0`, `GameClient.cs:184-189`) ;
5. **rafale / répétition** : le chemin d'émission ne comporte aucune limitation de fréquence
   (il n'y a ni compteur ni délai avant l'appel de la VA `0x64a2d3`) ; une politique de limitation
   serait donc une décision, pas un portage ;
6. **client en cours de déconnexion** : le paquet peut arriver après un `TM_CS_LOGOUT` (27, traité en
   `GameClient.cs:785-789` par un simple `Debug`), sans effet attendu.

**Zone de collision à signaler** : la branche `hermes/packet-socle-anti-triche` (MR #9, ouverte)
touche **`Game/Network/Packets/Enums/GamePackets.cs`** et **`Game/Network/Clients/GameClient.cs`**,
les deux fichiers où 57 s'insérerait. Le merge de l'un après l'autre demandera une résolution
manuelle dans la chaîne de `if` et dans l'énumération.

## 6. Écarts assumés avec NGemity, et pourquoi

| Point | NGemity / Chihiro | Cette fiche | Raison |
|---|---|---|---|
| Format | `_(simple)(uint32_t, log_code)` + `CREATE_PACKET(…, 57)` | identique, 11 octets | aucune divergence : même numéro, même champ unique |
| Gating | aucun axe de version, base `EPIC_4_1_1` | 57 (pas 1057) | rzu tranche le gating ; 7.3 < 9.6.3 |
| Traitement | aucun handler, journal `debug`, connexion conservée | identique, et **aucune réponse** | il n'existe pas de trame de réponse dans les références ni dans le répartiteur du client |
| `log_code` | déclaré, jamais lu | non tranché | aucune référence ne dit ce qu'un serveur en ferait : le combler serait inventer une politique |
| Émission côté client | sans objet | établie dans `SFrame.exe` (§2) | NGemity ne documente pas le client |

Le seul écart **constaté**, et il n'oppose pas cette fiche à NGemity mais rzu à rzu : le proxy de rzu
**jette** 57 (§5.2) alors que rzu en déclare le format. Ce n'est pas une divergence de spécification —
c'est la preuve qu'un outil rzu vivant considère le paquet comme non nécessaire au serveur.

## 7. `NON ÉTABLI`

a. **Qui lève l'événement `game_security_msg`.** La chaîne d'émission existe et est complète côté C++
(§2), mais la chaîne `game_security_msg` n'apparaît **qu'une seule fois dans tout le client extrait**
(comparaison en VA `0x69436b`) : aucun script ne la publie dans `reference/client73`. Cela recoupe le
relevé de la fiche socle (§4.1 : aucun module anti-triche n'est importé par le binaire) et la liste
d'imports. Ce qui l'établirait : le ou les scripts Lua du client, ou le module anti-triche
(`GameMon.des`/NPGame et ses `.erl`) — **absents du répertoire extrait** : le manifeste
`reference/client73/extraction-manifest.json` déclare **83 822 entrées** dans l'archive source mais
seuls **48** fichiers ont été extraits (`SFrame.exe`, `data.000` et les 46 `.rdb`) ; `data.000`
(3 646 701 octets, entropie de Shannon **7,997 bit/octet**, en-tête non textuel) est **chiffré** et
aucune chaîne `lua`/`function`/`local` n'y est lisible. Conclure « le client 7.3 n'émet jamais 57 »
serait donc faux, et conclure « il l'émet à telle occasion » serait inventé.

b. **Sémantique et emploi de `log_code` côté serveur** : journalisation, niveau, sanction,
déconnexion — aucune source (§5.5). À trancher par Killian.

c. **Domaine de valeurs réel de `log_code`** : `0` sur le seul chemin trouvé. Le second mot de l'objet
événement est initialisé à 0 par le code appelant (VA `0x69438d`) et jamais réécrit avant l'appel ;
en revanche le **premier** mot, lui, reçoit le résultat d'une conversion `atoi`-like (VA `0x97762c` →
`0x977616`) d'un champ chaîne de l'objet événement. Que ce premier mot soit bien « le code 0…22 »
n'est donc établi que par la comparaison `cmp $0x16` du §2.3 et par les 22 messages associés — pas par
une correspondance de symboles. Aucun symbole n'est disponible : `SFrame.exe` ne porte **aucune table
de symboles** (`objdump -t` → « no symbols » ; 0 symbole COFF dans l'en-tête), le désassemblage ne
comporte qu'une étiquette de section `.text`, et les noms de fonctions cités dans cette fiche sont des
adresses, pas des symboles.

d. **Correspondance entre les valeurs de message `0x312`…`0x336` et les clés `msgboxdetect_*` /
   `smsq_protect*`** : non établie (même point ouvert que la fiche socle §4.4 : la table `.data`
   `0xc1c0e0`-`0xc1c14c` n'est référencée par aucune adresse dans `.text`). Les deux cas `0x6b`
   (codes 14 et 15) ne rentrent dans aucune des deux plages.

e. **Ce que le client fait après l'envoi** : la boîte de message est mise en file, et deux écritures
d'un octet d'état encadrent l'envoi (`movb $0x0,0x128(%eax)` VA `0x64a2c4` avant,
`movb $0x1,0x128(%eax)` VA `0x64a2e0` après, sur l'objet à `this+0x64`). La signification de ce
drapeau et l'éventuelle fermeture de session ne sont pas établies.

f. **Comportement du serveur officiel d'origine vis-à-vis de 57** : inconnu ; ni rzu ni NGemity n'en
gardent trace, et le nom « CHECK_ILLEGAL_USER » suggère un contrôle côté serveur que rien ne confirme.

g. **Réception d'un 57 par le client** : sans objet fonctionnel (aucune trame S→C n'existe), mais le
comportement du répartiteur par défaut sur un id inattendu n'a pas été établi au-delà du fait que 57
n'y a pas de cas propre (§5.4.2).

## 8. Commits et binaires épinglés

| Référence | Commit / empreinte | Date | Objet |
|---|---|---|---|
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | 2023-10-02 | HEAD du clone local ; contient `TS_CS_CHECK_ILLEGAL_USER.h` et le converter de `rzfilter` |
| rzu | `d7c58ee6927b0a8391a22500c7edfc39153d36d3` | 2017-02-07 | « Adjust count management for packets and add all known GS packets as of 9.4 » — création de la déclaration |
| rzu | `11f2b6fd69913c192e6103afd3953a358ef6226a` | 2020-08-11 | « packets: use versionned ID for all packets and update their ID with epic 9.6.3 » — origine du gating 57/1057 |
| rzu | `librzu/src/lib/Packet/PacketEpics.h:59`, `:96` | — | `EPIC_7_3 = 0x070300`, `EPIC_9_6_3 = 0x090603` |
| NGemity | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | 2025-12-03 | HEAD du clone local (« Fix compilation issue for GCC ») |
| NGemity | `44b7d25dac7a1f869490dc8b6ea3255ea68b8eb4` | 2018-08-26 | « Network: Rewriting network code to use @glandu2 's structs » — import de la structure |
| Client 7.3 | `SFrame.exe`, sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 octets | — | `reference/client73` n'est **pas** un dépôt git : pas de SHA à épingler |
| Client 7.3 | `extraction-manifest.json` : 83 822 entrées, 48 extraites ; `data.000` sha256 `b88ac39a0976f49746671d413ba68af616af37e6e26095c3a109d5884510adbf`, 3 646 701 octets | — | limites de l'extraction (§7a) |
| NavisLamia | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | 2026-09-19 | `origin/master` au moment de la rédaction (« Merge pull request #4 … hermes/packet-203-drop-item ») |
| NavisLamia | `origin/hermes/packet-socle-anti-triche` | — | branche de la MR #9 ; `TM_CS_ANTI_HACK = 54` en `GamePackets.cs:77` de cette branche |

Branche de cette fiche : `hermes/packet-57-check-illegal-user`, créée depuis `master` à jour
(`ec76b21`). Fichier livré : `docs/packet-specs/57-check-illegal-user.md`. **Aucun code applicatif
modifié.**

## 9. Note de livraison

- Livrable : la présente fiche, et rien d'autre. Aucun fichier de `Game/`, aucun test, aucun
  `CLAUDE.md` (fichier d'instructions protégé).
- Méthode : lecture statique. `objdump -d reference/client73/SFrame.exe` (désassemblage hors ligne),
  lectures d'octets à des VA par script (nom de section résolu depuis les en-têtes PE, `ImageBase =
  0x00400000`), `strings`/recherche d'octets, `objdump` ciblé par plage d'adresses, `git log`,
  `grep`. **Aucun exécutable client, aucun Lua, aucun script du client n'a été lancé** ; aucun
  serveur ni base de données n'a été démarré.
- Contrôle de direction de la famille : `find … -iname '*ILLEGAL*'` (2 fichiers, tous `TS_CS_*`),
  `grep -n '1057' op_codes.md` (0), `grep -rn 'ILLEGAL' --include=*.cs` (0).
- Contrôle du dépôt : `git log --oneline origin/master..master` → **aucune ligne** (aucun commit sur
  `master` locale). L'arbre n'étant pas modifié par cette fiche, la base a tout de même été relevée
  avant le commit :

  ```
  export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
  dotnet build Navislamia.sln -c Debug     → code 0, 0 erreur, 160 avertissements
  dotnet test Tests/Tests.csproj           → code 0, 448 réussis / 448, 0 échec, 0 ignoré
  ```
- Référence amont reprise et **revérifiée** : la fiche `docs/packet-specs/socle-anti-triche.md` de la
  branche `hermes/packet-socle-anti-triche` (§4.2, §4.3, §4.4, §7.2). Ses relevés sur la table
  entrante (`0x67f0a0` / `0x67f020`) ont été relus ici octet à octet et concordent ; son §7.2 sur 57
  (4 octets de charge, 11 sur le fil, gating, absence de handler, nom absent du binaire) n'est ni
  contredit ni étendu par la présente fiche, qui tranche en plus **l'émission par le client** (§2) et
  **le sort du paquet côté proxy rzu** (§5.2).

## 10. Bloc prêt à coller dans `CLAUDE.md`

```markdown
### Paquet 57 — `TM_CS_CHECK_ILLEGAL_USER`

- Trame cliente de **11** octets : en-tête 7 + `log_code` `uint32` à l'offset 7. Taille fixe, aucun
  rembourrage, un seul champ.
- `log_code` est un nom **rzu** ; le client n'envoie que `0` sur le seul chemin d'émission connu.
  Sa sémantique et le sort du paquet côté serveur ne sont pas établis : ne rien en déduire.
- **Aucune réponse** : il n'existe aucune trame serveur → client de cette famille (ni rzu, ni
  NGemity, ni `op_codes.md`), et le répartiteur entrant du client 7.3 place 57 sur le chemin par
  défaut. Le proxy rzu, lui, **jette** le paquet.
- Gating : 57 à l'Epic 7.3, `1057` seulement à partir d'`EPIC_9_6_3` — **ne pas déclarer 1057**.
- Le client 7.3 **émet** 57 depuis sa surveillance interne (événement `game_security_msg`, jamais une
  action du joueur) et affiche une boîte de message du vocabulaire `msgboxdetect_*` /
  `smsq_protect*`. Ce qui déclenche cet événement n'est pas dans le client extrait (module
  anti-triche absent, `data.000` chiffré) : ne pas conclure à l'absence d'émission.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.
```

## A VERIFIER PAR KILLIAN

1. **Journaliser un 57 ?** À quel niveau (`Debug`, comme aujourd'hui en `GameClient.cs:586`, ou
   au-dessus) et pour quel effet observable ? Aucune référence ne tranche (§5.5).
2. **Sanctionner ou déconnecter sur 57 ?** Aucune des trois références ne le fait ; NGemity journalise
   en `debug` et garde la connexion (`WorldSession.cpp:159-160`). La fiche ne choisit pas.
3. **Implémenter 57 dans `GamePackets` + un traitement, ou laisser l'id non déclaré ?** Les deux
   comportements sont aujourd'hui équivalents du point de vue du client (aucune réponse n'est
   attendue, §5.4) ; la différence est le niveau de journal et la traçabilité. Si l'id est déclaré,
   le critère transversal n° 4 (enum et dispatch ensemble) s'applique et le bras doit précéder le
   `switch` de `GameClient.cs:791-803`.
4. **Coordination avec la MR #9** (`hermes/packet-socle-anti-triche`) : elle modifie les deux mêmes
   fichiers. Lequel des deux merge-t-on d'abord ?
5. **Ce qui déclenche `game_security_msg`** (§7a) : établi seulement en présence du module
   anti-triche ou des scripts Lua du client. Vérification impossible sur ce VPS (interdiction
   d'exécuter le client, `data.000` chiffré, archive non extraite intégralement).
6. **Faut-il limiter la fréquence des 57 ?** Le client n'impose aucune limite (§5.6.5) ; une
   limitation serait une décision, pas un portage.
