# `TM_CS_CHECK_BOOTH_STARTABLE` (711) — « l'étal est-il démarrable ici ? »

Portée : un seul paquet client → serveur, à en-tête seul. Le socle livré
(`docs/packet-specs/socle-booths.md`) portait `700`/`701` ; la visibilité (`702`/`703`/`704`) est dans la
MR ouverte `hermes/packet-socle-booths-visibilite`. Cette fiche tranche le dernier id de la famille.

**Ce que cette fiche corrige, et qui était faux dans le dépôt.** `socle-booths.md` §1.3 concluait que
`711` « n'existe pas dans le client de 7.3 », « ni nom, ni créneau de dispatch », donc « ne peut pas
être émis ». Les trois constats sont exacts ; la conclusion est fausse. Le client de 7.3 **construit
et envoie** une trame `711` de 7 octets : constructeur en `VA 0x48CFD0`, un seul site d'appel
`VA 0x49A176`, envoi par le même emplacement virtuel que `700` (§2.2). Le commentaire
`GamePackets.cs:124-126` (« deliberately absent ») et la ligne `CLAUDE.md:1239` sont à corriger avec
ce lot (§5.4, §9).

Décision tranchée : **déclarer `711` dans `GamePackets` et router l'id**, avec lecture bornée,
journalisation en `Debug` et **aucune réponse** (§5.2). C'est la conduite déjà retenue par le dépôt
pour un id client sans politique de jeu établie (`304`, `452`, `57`, `59`).

---

## 1. Identité

| id | nom | sens | source |
|---|---|---|---|
| 711 | `TM_CS_CHECK_BOOTH_STARTABLE` | client → serveur | `op_codes.md:180` |
| 711 (`< EPIC_9_6_3`) | `TS_CS_CHECK_BOOTH_STARTABLE` | client → serveur | `reference/rzu/librzu/src/packets/GameClient/TS_CS_CHECK_BOOTH_STARTABLE.h:7-9` |
| 711 | `TS_CS_CHECK_BOOTH_STARTABLE` | client → serveur | `reference/ngemity/shared/Server/ClientPackets.h:192` |

`op_codes.md:180` est la table Lua livrée avec le dépôt ; elle liste `711` et **ce dépôt ne la
modifie pas** — c'est la convention déjà suivie par `socle-booths.md` (§6 point 5) et par
`711` lui-même (§7 point 6).

Place dans la famille (source : `socle-booths.md` §1.1, `op_codes.md:169-180`) :

| id | nom | sens | socle 7.3 |
|---|---|---|---|
| 700 | `TM_CS_START_BOOTH` | C→S | livré |
| 701 | `TM_CS_STOP_BOOTH` | C→S | livré |
| 702 / 703 / 704 | `WATCH_BOOTH` / `SC_WATCH_BOOTH` / `STOP_WATCH_BOOTH` | C→S, S→C, C→S | MR ouverte (`socle-booths-visibilite.md` §1) |
| 705…710 | achat, vente, noms, fermeture, `TM_SC_BOOTH_TRADE_INFO` | C→S, S→C | hors socle, non déclarés |
| **711** | **`TM_CS_CHECK_BOOTH_STARTABLE`** | **C→S** | **cette fiche** |

Le `1711` de rzu n'est **pas** déclaré (§4).

---

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Le geste — `NON ÉTABLI`

Ce qui est mesuré : la trame `711` est produite par le gestionnaire de messages interne situé en
`VA 0x499F60`, sur le **sous-type 5** de son énumération (table de sauts `0x49A520`, table d'octets
`0x49A55C` : l'index 5 → `0x49A166`, et l'octet à `0x49A561` vaut `05` ; le sélecteur est lu à
`[message+0x13]` et borné à `0x1F`, `0x499F9F-0x499FB6`). Ce gestionnaire est atteint depuis le
répartiteur de messages du client en `VA 0x49E2B6` (`call 0x499F60`).

Ce qui n'est pas établi : **quelle action du joueur** fait monter ce message. Le nom `CHECK_BOOTH` est
absent du binaire (§2.3), donc aucun libellé d'interface ne peut servir d'indice. Prochaine étape
précise : nommer la classe de message de l'entrée de répartiteur `0x49E2B6`, dont l'entrée voisine
`0x49E2A9` appelle `0x48D560`, le gestionnaire qui construit le `700`. Voir §7 point 1.

### 2.2 Preuve d'émission — mesurée dans `SFrame.exe`

Binaire : `reference/client73/SFrame.exe`, `sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`,
9 841 664 octets. Aucune exécution : lecture de démontage (`objdump -d -M intel`, sortie complète dans
`/tmp/sf.txt` pendant la séance).

1. **Le constructeur.** `VA 0x48CFD0` construit une trame de 7 octets
   (`mov DWORD PTR [eax],0x7`, `0x48CFD8` et `0x48CFFD`) dont l'id est écrit à l'offset 4
   (`mov edx,0x2C7` puis `mov WORD PTR [eax+0x4],dx`, `0x48CFF2-0x48CFF7`) et le checksum à l'offset 6
   (somme des octets 0…5, `0x48CFE4-0x48CFEB` puis recalcul `0x48D010-0x48D017`). C'est l'idiome
   exact des constructeurs déjà mesurés (`0x48CBD0` pour `700`, `0x48CC20` pour `701`,
   `BoothPackets.cs:31-32`), dans la même famille de fonctions : `0x48CF70` construit `9005`
   (`mov ecx,0x232D`, `0x48CFA5`), `0x48CFD0` construit `711`, `0x48D020` construit `25`
   (`mov edx,0x19`, `0x48D042`).
2. **Le site d'appel.** `call 0x48CFD0` n'apparaît **qu'une fois** dans tout le démontage :
   `VA 0x49A176`, avec `lea ecx,[ebp-0x14]` en `0x49A173` (la trame est cette zone de pile).
3. **L'envoi.** Immédiatement après (`0x49A17B-0x49A18D`) la trame part vers l'objet de session :
   `mov ecx,DWORD PTR [esi+0xB8]` → `mov edx,DWORD PTR [ecx]` → `mov edx,DWORD PTR [edx+0xC4]` →
   `push eax` (l'adresse de la trame) → `call edx`. Le seul garde est `cmp DWORD PTR [esi+0xB8],0x0`
   / `je` (`0x49A166-0x49A16D`) : l'appel n'a lieu que si l'objet de session existe.
4. **Témoin de la méthode.** Le chemin `700`, dont l'émission n'est pas discutée, suit le même
   schéma : son constructeur `0x48CBD0` n'a lui aussi qu'un seul appelant (`0x48D5C9`, dans le
   gestionnaire `0x48D560`), et la trame `700` est remise **au même emplacement `+0xC4` du même
   objet de session** une fois son checksum posé (`mov BYTE PTR [esi],dl` en `0x48D68A`, puis
   `mov ecx,DWORD PTR [ecx+0xB8]` … `mov eax,DWORD PTR [edx+0xC4]` … `call eax`,
   `0x48D695-0x48D6A4`). Le même emplacement sert aussi en `0x48D5AA-0x48D5BC`. `701` :
   constructeur `0x48CC20`, un seul appelant, `0x48D582`.
5. **Le balayage des immédiats.** L'immédiat `0x2C7` (711) apparaît **six fois** dans tout le
   démontage, et une seule de ces occurrences écrit dans une trame :

| VA | instruction | ce que c'est |
|---|---|---|
| `0x44A31E` | `mov DWORD PTR [eax],0x2C7` (suivi de `mov DWORD PTR [eax+0x4],0xA13E5C`) | une paire (id numérique, chaîne) pour l'interface : la chaîne visée est `#@itemname@#…` — sans rapport |
| **`0x48CFF2`** | **`mov edx,0x2C7`** | **le constructeur `0x48CFD0` : la seule écriture d'id de trame** |
| `0x63D5EA` | `sub eax,0x2C7` / `je` | chaîne de comparaisons (le voisin testé est `800`) |
| `0x63D7FA` | `push 0x2C7` | argument d'un id numérique (les voisins poussent `0x235`, `0x3DA`) |
| `0x6B5507` | `mov DWORD PTR ds:0xC1F614,0x2C7` | écriture d'une variable globale |
| `0x968E0D` | `cmp eax,0x2C7` | classification d'id de ressource (voisins `0x2010`, `0x2040`, `0x2122`) |

Limite assumée de ce balayage : il couvre les **immédiats**. Un id lu dans une table au moment de
l'émission lui échapperait — c'est exactement pourquoi le constat négatif de `socle-booths.md` §1.3
n'était pas concluant (§2.3) et pourquoi c'est ici une **preuve positive** (un constructeur existe et
est appelé) qui est produite.

### 2.3 Pourquoi la conclusion de `socle-booths.md` §1.3 était fausse

Les trois constats de §1.3 sont exacts, et aucun ne parle de l'émission :

| constat de §1.3 | ce qu'il prouve vraiment |
|---|---|
| la chaîne `CHECK_BOOTH` / `BOOTH_STARTABLE` n'existe pas dans le binaire (0 occurrence, revérifié sur 9 841 664 octets) | que le client ne **nomme** pas ce paquet. Le dépôt sait déjà qu'un nom absent ne prouve rien : `TM_CS_SET_PET_NAME` (émis, `CLAUDE.md` §354), `TM_CS_ARRANGE_ITEM`, `TM_CS_CHANGE_ITEM_POSITION` et `TM_CS_ACCOUNT_WITH_AUTH` ont un constructeur et **aucun** nom dans le binaire. Le bassin de noms compte 73 `TM_CS_*` quand `op_codes.md` en déclare 112. |
| la table `id → nom` s'arrête à `710` | la même chose : cette table est incomplète côté émission (73 noms pour 112 ids) |
| le répartiteur **entrant** s'arrête à `710` (`sub eax,0x1F9` = 505, `cmp eax,0xCD` = 205, `ja 0x67EF21`, `0x67E3D3-0x67E3DD`) | que le client ne **reçoit** pas `711`. C'est le sens inverse de celui qui compte ici : `711` est client → serveur. La branche par défaut pousse la chaîne `0xA53DF0` (`0x67EF21-0x67EF27`) : `« 처리되지 않은 메세지 : %d »` — « message non traité : %d ». |

Ce que §1.3 n'a pas regardé : **le sens sortant**, où le constructeur de `711` se trouve — la famille
de constructeurs examinée s'arrêtait à `0x48CC20` (`701`), alors que `711` est construit plus loin en
`0x48CFD0`, après le constructeur de `9005` (`0x48CF70`). La conséquence à retenir pour la suite :
sur ce client, la présence d'un **constructeur appelé** est la preuve d'émission ; une chaîne absente
et une table de réception bornée n'en sont pas des preuves d'absence.

---

## 3. Structure sur le fil — **7 octets, aucun champ**

| offset | taille | type | nom | valeur attendue | source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `length` | `7` | client : `mov DWORD PTR [eax],0x7` (`0x48CFD8`, réécrit `0x48CFFD`) ; dépôt : `Game/Network/Packets/Header.cs:9` |
| 4 | 2 | `uint16` | `id` | `711` | client : `mov edx,0x2C7` + `mov WORD PTR [eax+0x4],dx` (`0x48CFF2-0x48CFF7`) ; dépôt : `Header.cs:10` |
| 6 | 1 | `uint8` | `checksum` | somme des octets 0…5 | client : `add dl,BYTE PTR [esi]` puis `mov BYTE PTR [ecx],dl` (`0x48D010-0x48D017`) ; dépôt : `Header.cs:11`, `PacketExtensions.cs` |
| **7** | | | **taille totale** | | |

**Aucun champ après l'en-tête.** Les deux références déclarent `TS_CS_CHECK_BOOTH_STARTABLE_DEF(_)`
**vide** : rzu `TS_CS_CHECK_BOOTH_STARTABLE.h:5-6`, NGemity
`shared/Server/Packets/GameClient/TS_CS_CHECK_BOOTH_STARTABLE.h:6`. Le client concorde : le
constructeur écrit l'id et rien d'autre, et sa longueur finale est un **littéral 7**, jamais
recalculée à partir d'un `count` (contraste avec `700`, `59 + 16×N`, `BoothPackets.cs:41-44`).

Repère du dépôt pour un id à en-tête seul de la même famille : `BoothPackets.StopBoothLength`
(`Game/Network/Packets/Game/BoothPackets.cs:59`, valeur `HeaderSize` = 7, `:38`). Le lot ajoute la
constante jumelle `CheckBoothStartableLength` (§5.4).

---

## 4. Gating de version — statué pour Epic 7.3

| ce qui est gaté dans rzu | condition | décision 7.3 |
|---|---|---|
| l'id | `X(711, version < EPIC_9_6_3)` / `X(1711, version >= EPIC_9_6_3)` (`TS_CS_CHECK_BOOTH_STARTABLE.h:7-9`) | **garder `711` seul. Ne pas déclarer `1711`.** 7.3 < 9.6.3 ; c'est la même règle que pour `700`/`1700`, `701`/`1701` (`socle-booths.md` §4) et `452`/`1452` (`docs/packet-specs/452-summon-card-skill-list.md`) |
| les champs | aucun (`DEF` vide) | **rien à trancher** : la trame de 7.3 est l'en-tête et rien d'autre |
| NGemity | `CREATE_PACKET(TS_CS_CHECK_BOOTH_STARTABLE, 711)` (`…/TS_CS_CHECK_BOOTH_STARTABLE.h:8`) : un seul id, pas de gating | NGemity compile un seul Epic par construction ; il ne peut pas montrer le remap. rzu le montre, la fiche le tranche, le dépôt ne porte que l'id de sa version cible |

Aucun champ de cette fiche ne reste non tranché côté version.

---

## 5. Traitement attendu

### 5.1 Ce que font les références

* **rzu** ne décrit que le format : `TS_CS_CHECK_BOOTH_STARTABLE.h:5-12` (DEF vide, ids versionnés,
  `SessionPacketOrigin::Client`). Aucune logique de serveur, et **aucun paquet descendant** de cette
  famille : `BOOTH`/`STARTABLE` n'apparaît, dans tout `librzu`, que dans ce fichier d'en-tête.
* **NGemity** déclare l'id (`shared/Server/ClientPackets.h:192`, inclus par `shared/Server/XPacket.h:79`)
  et **n'a aucun gestionnaire** : `grep -rn "CHECK_BOOTH" reference/ngemity/Chihiro/` → 0 résultat, et
  la plage d'ids saute de `711` à `800` (`ClientPackets.h:192-193`).
* **Aucun paquet de réponse n'existe** chez l'une ou l'autre référence : ni un `TS_SC_*BOOTH*STARTABLE*`,
  ni un `RESULT` dédié à cette question. Les seuls canaux descendants de la famille sont `703`, `708`,
  `709`, `710` (`socle-booths.md` §5.3) et le générique `TM_SC_RESULT` (`0`).
* Le code de résultat `55` (`RESULT_NOT_ACTABLE_WHILE_USING_BOOTH`, `reference/ngemity/shared/Server/TS_MESSAGE.h:108`,
  repris par `Game/Network/Packets/ResultCode.cs:67`) existe des deux côtés — mais il concerne l'usage
  d'un étal **déjà ouvert**, pas la question « puis-je démarrer ».

### 5.2 Ce que le serveur doit faire

1. **Déclarer l'id** : un membre `TM_CS_CHECK_BOOTH_STARTABLE = 711` dans
   `Game/Network/Packets/Enums/GamePackets.cs`, à sa place numérique (famille des étals, `:124-128`).
2. **Router l'id** : un bras de réception dans `GameClient.OnDataReceived`, **avant** le `switch`
   final, sinon une trame `711` atteint `_ => throw new Exception("Unknown Packet Type 711")`
   (`Game/Network/Clients/GameClient.cs:1865`) et **casse la boucle de réception** — c'est
   l'interdiction du critère « enum et dispatch modifiés ensemble » (`CLAUDE.md:2224-2227`, `:2007`).
3. **Lire la trame et ne rien décider** : l'en-tête seul est requis (`Header` est déjà lu et
   vérifié par la boucle, `GameClient.cs:1269-1273` pour le garde d'id, la longueur et le checksum
   en amont). Le bras journalise en `Debug` (`id`, `Length`, `clientTag`) et poursuit — même forme que
   les bras existants pour `TM_CS_RETURN_LOBBY` (`GameClient.cs:1753-1759`) et pour les ids déclarés
   et ignorés (`:1738-1743`, `TM_CS_QUERY`/`TM_CS_UPDATE`).
4. **Longueur divergente** : une trame dont `Length != 7` est **journalisée en `Warning`** et
   abandonnée, sans être refusée par une réponse — c'est la règle déjà appliquée à `452`
   (`GameClient.cs:499-503`) et à `57` (`CLAUDE.md` §57). Aucune règle de refus n'est établie pour
   ce paquet.
5. **Aucune politique de jeu** : aucune vérification de zone, de niveau, de distance ou d'état
   d'étal n'est décidée depuis `711`. Le refus d'un démarrage d'étal reste ce qu'il est déjà :
   `HandleStartBooth` → `BoothRules` → `TS_SC_RESULT(700, code)`
   (`GameClient.cs:891-903`, `socle-booths.md` §5.3). Dupliquer cette logique ici inventerait une
   règle que ni le client ni NGemity ne portent.

### 5.3 Ce que le serveur ne doit **pas** répondre

**Rien.** Trois raisons, dans cet ordre :

1. aucune référence n'établit de réponse (§5.1) ;
2. le client de 7.3 **n'a pas de créneau de réception** pour `711` : son répartiteur entrant s'arrête
   à `710` et tout id supérieur part à `0x67EF21`, qui se contente de journaliser « message non
   traité » (§2.3). Une réponse serait ignorée au mieux ;
3. le choix d'un code (par exemple `TS_SC_RESULT(711, 55)`) supposerait de savoir ce que le client
   fait de la réponse — non établi (§7 point 3).

C'est la conclusion déjà écrite pour `452` (`GameClient.cs:487-503` : « nothing is answered »,
`452-summon-card-skill-list.md`) et pour `304` (`304-summon.md` §5.4).

### 5.4 Ce que le lot doit livrer

1. `Game/Network/Packets/Enums/GamePackets.cs` : le membre `TM_CS_CHECK_BOOTH_STARTABLE = 711`,
   et la mise à jour du commentaire `:124-126` qui dit aujourd'hui « deliberately absent » — il doit
   renvoyer à cette fiche et rappeler que le client **émet** la trame (7 octets, constructeur
   `0x48CFD0`).
2. `Game/Network/Clients/GameClient.cs` : le bras de réception décrit en §5.2.
3. `Game/Network/Packets/Game/BoothPackets.cs` : la constante `CheckBoothStartableLength` (= `HeaderSize`,
   7), à côté de `StopBoothLength` (`:59`), et la lecture d'une trame à en-tête seul si le lot en
   ajoute une.
4. `Tests/Game/BoothPacketsTests.cs` : **le test `BoothPackets_CarryTheirEpic73IdsAndNothingElseOfTheFamily`
   (`:221-236`) doit être amendé** — il exige aujourd'hui que `711` ne soit **pas** défini
   (`foreach (var id in new ushort[] { 702, …, 711 }) … BeFalse()`) et échouera dès la déclaration.
   Le test d'offsets du critère 3 porte ici sur la taille totale (7) et sur l'absence de champ :
   `length == 7`, `id == 711` à l'offset 4, checksum à l'offset 6, rien après l'offset 7.

---

## 6. Écarts assumés avec NGemity, et pourquoi

| point | NGemity | NavisLamia | pourquoi |
|---|---|---|---|
| ids | `CREATE_PACKET(TS_CS_CHECK_BOOTH_STARTABLE, 711)` uniquement (`:8`) | `711` seul, `1711` interdit (§4) | NGemity compile un seul Epic ; rzu montre le remap 9.6.3, le dépôt ne porte que l'id de sa version cible |
| en-tête de base | `CREATE_PACKET` → `CREATE_STRUCT_IMPL(name, 7, …)` (`shared/Server/Packets/PacketDeclaration.h:576`) | 7 (`Header.cs:9-11`) | **accord** : les deux bases valent 7 octets |
| corps | `DEF` vide (`:6`) | aucun champ (§3) | **accord** |
| gestionnaire | aucun : la trame tombe dans le journal « unknown packet » (`Chihiro/src/Network/GameNetwork/WorldSession.cpp`) | bras de réception qui journalise et ne répond pas (§5.2) | **même effet observable** (aucune réponse, aucune mutation), mais ce dépôt n'a pas de « table de paquets inconnus » pour un id **déclaré** : un membre sans bras casse la boucle de réception (`CLAUDE.md:2224-2227`). C'est le même écart que pour `304` et `452`. |

---

## 7. `NON ÉTABLI`

1. **Le geste qui déclenche la trame.** Mesuré : gestionnaire `0x499F60`, sous-type interne 5,
   atteint depuis `0x49E2B6` ; l'émission est en `0x49A166-0x49A18D`. Non établi : quelle action
   d'interface monte ce message. Prochaine étape : nommer la classe de message de l'entrée `0x49E2B6`
   (l'entrée voisine `0x49E2A9` mène au gestionnaire du `700`, `0x48D560`), puis remonter à ses
   appelants. Le nom du paquet étant absent du binaire, aucun libellé d'interface ne peut servir
   d'indice.
2. **La sémantique.** « L'étal est-il démarrable ici ? » n'est déduit que du **nom** `CHECK_BOOTH_STARTABLE`,
   nom que rzu et NGemity donnent, mais qui n'existe pas dans le client. Aucune source n'établit que
   le client subordonne l'ouverture de l'étal à cette trame, ni ce qu'il attend en retour. Ne rien en
   déduire côté serveur.
3. **La réponse.** Aucun paquet descendant n'existe dans les deux références (§5.1). La fiche répond
   **rien** ; si Killian veut une réponse, il faut d'abord trancher quel code et ce que le client en
   fait — non établi.
4. **La conduite du serveur.** « Lire et journaliser sans répondre » est la convention du dépôt
   (`304`, `452`, `57`, `59`), **pas** une source. Aucune référence ne montre de serveur traitant
   `711`.
5. **Les cinq autres sites de l'immédiat `0x2C7`** (§2.2) sont classés « sans rapport » par lecture
   de leur contexte, mais deux restent ambigus : l'écriture globale `0x6B5507` et la comparaison
   `0x968E0D`. Aucun ne construit de trame.
6. **`op_codes.md` contredit l'énumération** : la table Lua le liste (`:180`) alors que le commentaire
   de `GamePackets.cs:124-126` le disait absent. Cette fiche ne modifie pas `op_codes.md` (convention
   `socle-booths.md` §6 point 5) et signale l'écart.
7. **Le balayage par immédiats** (§2.2 point 5) ne couvre pas un id calculé au moment de l'émission.
   Le fait qu'il n'y ait **qu'un** site d'appel du constructeur a été mesuré sur la sortie complète du
   démontage, mais un second chemin d'émission assemblant la trame sans le constructeur n'est pas
   formellement exclu — il aurait fallu écrire `711` dans un immédiat, ce que les six sites couvrent.
8. **Un client d'une autre version** (≥ 9.6.3, id `1711`) n'est pas traité : hors périmètre, et
   `1711` ne doit pas être déclaré.

---

## 8. Commits épinglés et méthode de lecture

| dépôt | commit | ce qui en vient |
|---|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02) | état courant ; `TS_CS_CHECK_BOOTH_STARTABLE.h` n'a plus bougé depuis `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11, « packets: use versionned ID for all packets and update their ID with epic 9.6.3 ») — origine du gating `711`/`1711` ; base d'en-tête `librzu/src/lib/Packet/PacketDeclaration.h:614-619` |
| `reference/ngemity` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03) | état courant ; `TS_CS_CHECK_BOOTH_STARTABLE.h` n'a plus bougé depuis `44b7d25dac7a1f869490dc8b6ea3255ea68b8eb4` (2018-08-26) ; base d'en-tête `shared/Server/Packets/PacketDeclaration.h:576` |
| `NavisLamia` | `b56967a07430422add88e0e5cdf292b41b18f6c6` (base de cette branche) | `GamePackets.cs`, `GameClient.cs`, `BoothPackets.cs`, `Tests/Game/BoothPacketsTests.cs`, `CLAUDE.md:1239` |
| `reference/client73` | `SFrame.exe`, `sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | constructeur `0x48CFD0`, appel `0x49A176`, envoi `+0xC4`, répartiteur entrant `0x67E3D3`, bassin de noms |

Méthode : `objdump -d -M intel SFrame.exe > /tmp/sf.txt` (2 256 750 lignes), puis recherche des
immédiats et des sites d'appel dans cette sortie. Aucun exécutable du client n'a été lancé, aucun Lua,
aucun script du client.

---

## 9. Bloc destiné à `CLAUDE.md` (à recopier par Killian depuis la MR)

```markdown
### Paquet 711 — `TM_CS_CHECK_BOOTH_STARTABLE`

- Trame cliente de **7** octets : l'en-tête et rien d'autre. Rien à mesurer au-delà : rzu et NGemity
  déclarent tous deux un `DEF` vide.
- **Le client de 7.3 émet cette trame.** Son constructeur est en `VA 0x48CFD0` (id `0x2C7` à
  l'offset 4, `Length` littéral `7`), il n'a qu'un site d'appel (`VA 0x49A176`) et la trame part par
  l'emplacement virtuel `+0xC4` du même objet de session que le `700` (`0x48D5AA-0x48D5BC`). La ligne
  « le client de 7.3 ne peut pas l'émettre » de la section « Étal de joueur » est **fausse** : ni
  l'absence de la chaîne `CHECK_BOOTH` du binaire, ni la borne du répartiteur **entrant** (505…710,
  `0x67E3D3`) ne prouvent quoi que ce soit sur l'émission.
- Ce qui reste vrai de l'ancien constat : le client **ne reçoit pas** `711` (tout id > 710 tombe sur
  « message non traité », `0x67EF21`) et il ne le **nomme** pas. Un nom absent ne prouve jamais une
  non-émission : `TM_CS_SET_PET_NAME`, `TM_CS_ARRANGE_ITEM`, `TM_CS_CHANGE_ITEM_POSITION` et
  `TM_CS_ACCOUNT_WITH_AUTH` sont émis et absents du bassin de noms (73 `TM_CS_*` contre 112 dans
  `op_codes.md`).
- Gating : `711` à l'Epic 7.3, `1711` seulement à partir d'`EPIC_9_6_3` — **ne pas déclarer `1711`**.
- Traitement : l'id est déclaré et routé, la trame est journalisée en `Debug` (une longueur ≠ 7 en
  `Warning`) et **aucune réponse n'est envoyée** : aucune référence n'en établit, et le client n'a pas
  de créneau de réception pour `711` (elle serait ignorée). Aucune politique de jeu n'est décidée
  depuis ce paquet ; le refus d'un étal reste `TS_SC_RESULT(700, code)`.
- La sémantique de la trame et le geste qui la déclenche ne sont pas établis : ne rien en déduire.
  Spécification complète : `docs/packet-specs/711-check-booth-startable.md`.
```

---

## 10. A VERIFIER PAR KILLIAN

Réserves du lot. Aucune ne bloque : elles portent sur des décisions NavisLamia et non sur des sources.

1. **La correction d'un constat déjà mergé.** `socle-booths.md` §1.3 et `CLAUDE.md:1239` affirment que
   le client de 7.3 ne peut pas émettre `711`. La mesure du constructeur (`0x48CFD0`), de son unique
   appelant (`0x49A176`) et de l'envoi par l'emplacement `+0xC4` le contredit. Le lot corrige le
   commentaire de `GamePackets.cs` et propose le bloc `CLAUDE.md` (§9) ; la fiche `socle-booths.md`
   n'est **pas** réécrite (elle décrit le socle 700/701) — à Killian de dire s'il veut y ajouter un
   renvoi.
2. **La conduite « lire, journaliser, ne pas répondre »** est la convention du dépôt (`304`, `452`,
   `57`, `59`), pas une source (§7 point 4). Si Killian veut une réponse, le code reste à trancher.
3. **Le test `BoothPackets_CarryTheirEpic73IdsAndNothingElseOfTheFamily`** (`Tests/Game/BoothPacketsTests.cs:221-236`)
   doit perdre `711` de sa liste d'ids interdits. Il est édité en parallèle par la MR ouverte
   `hermes/packet-socle-booths-visibilite` (qui en retire `702`/`703`/`704`) : **zone de conflit
   annoncée**, résolution triviale mais à faire en connaissance de cause.
4. **Le geste déclencheur reste non établi** (§7 point 1) : la trame est bien émise, mais aucune
   action d'interface ne peut lui être rattachée aujourd'hui. La fiche donne l'adresse exacte à
   partir de laquelle continuer.
5. **`op_codes.md` n'est pas modifié** : `:180` continue de lister `711` alors que le commentaire du
   dépôt le disait absent. L'écart est signalé, pas résolu (§7 point 6).
