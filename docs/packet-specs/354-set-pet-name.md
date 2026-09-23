# 354 — `TM_CS_SET_PET_NAME`

Fiche du couple **353 / 354** de la famille des familiers (*pet*) d'Epic 7.3 : `354` = la trame
client → serveur qui porte le nouveau nom, `353` = la trame serveur → client du même couple.
Écrite avant le lot `navis-dev` (tâche `t_bbc5f295`), sur la base `a1c4950`.

---

## 1. Identité

| | |
|---|---|
| id | **354** (`0x162`) — id 7.3, la bascule 9.6.3 vers `1354` ne concerne pas ce dépôt |
| nom | `TM_CS_SET_PET_NAME` |
| source de l'id | `op_codes.md:121` (`[354] = "TM_CS_SET_PET_NAME"`) |
| sens | client → serveur (rzu : `SessionPacketOrigin::Client`, `TS_CS_SET_PET_NAME.h:15`) |
| trame jumelle | **353** `TM_SC_SHOW_SET_PET_NAME` (`op_codes.md:120`), serveur → client, **11 octets** |
| voisin non traité ici | 355 `TM_CS_SET_PET_FILTER` (`op_codes.md:122`) — hors lot, carte distincte |
| état du dépôt | ni 353 ni 354 ne sont déclarés dans `GamePackets` et aucun bras de réception ne les lit ; le socle `socle-invocations.md:53-54` les donne « déclaré, aucun gestionnaire » / « aucune carte » |
| ancêtre | lot 323 (`hermes/packet-323-change-summon-name`, MR #40) : le renommage d'**invocation**, autre famille, autre trame |

Définition rzu (`librzu/src/packets/GameClient/TS_CS_SET_PET_NAME.h:5-9`) :

```
_(simple)(ar_handle_t, handle)
_(def)(string)(name, 20)
  _(impl)(string)(name, 19, version <  EPIC_9_6)
  _(impl)(string)(name, 20, version >= EPIC_9_6)
```

Trame jumelle (`librzu/src/packets/GameClient/TS_SC_SHOW_SET_PET_NAME.h:5-6`) : `_(simple)(ar_handle_t, handle)` — rien d'autre.

NGemity (`shared/Server/Packets/GameClient/TS_CS_SET_PET_NAME.h:6-10`) : `_(simple)(uint32_t, handle)`, `_(string)(name, 19)`,
`CREATE_PACKET(TS_CS_SET_PET_NAME, 354)` ; idem pour 353 (`TS_SC_SHOW_SET_PET_NAME.h:6-9`,
`ClientPackets.h:132-133`).

---

## 2. Ce que le joueur fait pour que le client l'envoie

La chaîne suivante est **prouvée par lecture de `reference/client73`** (aucun Lua ni script du
client n'a été exécuté) ; les adresses sont celles de `SFrame.exe`
(`sha256 41e0af2e…`, §8).

### 2.1 Le client reçoit 353 et ouvre une boîte de saisie

1. Le répartiteur de paquets entrants `0x67de80` (tables `0x67f19c` / `0x67f218`, base
   `sub $0xff`) route **l'id 353** vers le convertisseur `0x66f710` (cas `0x67e2b1`,
   indice 98 → 0xff + 98 = 0x161 = 353). Ce convertisseur construit un message
   **`AUSMSG_SHOW_SET_PET_NAME`** (type `125`) et y recopie **la charge utile du paquet** :
   `mov 0x7(%ecx),%edx` puis `mov %edx,0x13(%eax)` (`0x66f74e`-`0x66f751`) — l'offset `7` est
   l'octet qui suit l'en-tête de 7 octets, donc le `handle` de la trame 353.
2. Le répartiteur de messages `SGameInterface::ProcMsgAtStatic` `0x639500` (son nom est le
   libellé SEH poussé en `0x639531`, chaîne `0xa4c8ec`) traite le type **125**
   (cas `0x63c472`, table `0x640dec` / `0x640f00`, indice 123 → type 125) :
   - `0x63c489` : `mov 0x13(%esi),%ecx` — le `handle` porté par le message ;
   - `0x63c48e` : appel virtuel `*(vtbl+0x1a4)` pour résoudre le `handle` en créature vivante ;
     **si la résolution échoue** (`0x63c483`, `0x63c49e`) le cas saute directement au journal
     et **aucune boîte n'est ouverte** ;
   - `0x63c4a4`-`0x63c4b4` : allocation de `0x33` octets puis construction d'un message
     **`AUSIMSG_REQ_SET_PET_NAME`** (type `0x492` = **1170**, constructeur `0x628a70`) ;
   - `0x63c4bf`-`0x63c4c7` : `req+0x13 = msg+0x13` — **le handle reçu en 353 est recopié tel quel
     dans la requête** ;
   - `0x63c4ca`-`0x63c51d` : texte `db_string` d'id `-830`, formaté (`0x43c490`) avec la chaîne
     `0xa2a248` = `"#@pet_name@#"`, puis construction de la boîte (`0x52c9f0`) ;
   - `0x63c542`-`0x63c54a` : la commande `0x3e` (**62**) est activée (`0x62f370`) et la boîte est
     enregistrée pour cette commande (`0x62f470`) — la boîte détient la requête ;
   - `0x63c569` : journal `0xa4b56c` = `"SGameInterface - MSG_SHOW_SET_PET_NAME"`.

### 2.2 Le joueur valide, le client envoie 354

3. Le répartiteur de messages d'interface `0x49cd00` traite le type **1170**
   (cas `0x49e615`, tables `0x49e98c` / `0x49ea50`, indice 43 → `0x403 + 143` = 0x492) et
   appelle l'émetteur `0x48e170` (`0x49e615: push %edi ; 0x49e616: mov %esi,%ecx ; 0x49e618: call 0x48e170`).
4. `0x48e170` construit et envoie la trame (§3) : `handle` lu en `arg+0x13` (`0x48e184`, écrit en
   `packet+7` par `0x48e18e`) et `memcpy` de **`0x13` = 19 octets** du `std::string` en `arg+0x17`
   vers `packet+11` (`0x48e195`-`0x48e19c`).

**Conséquence de conception, prouvée par ces trois sites :** le `handle` de 354 **est
litéralement celui de 353** (`0x66f751` → `0x63c4c7` → `0x48e184`). Le client est un écho : le
serveur choisit le handle en émettant 353 et le retrouve tel quel dans 354. Il n'a donc jamais à
trancher `cage_handle` contre `pet_handle` pour lire la trame (réserve de sémantique de jeu
laissée par `socle-invocations.md:641-642`).

### 2.3 Ce qui est ouvert

- La **gestuelle exacte** (quel objet ou quel bouton ouvre la boîte côté serveur) n'est pas
  établie : le client seule ne la montre pas. Le `db_string` du client contient les noms d'objet
  `"Pet Name Change"` (`0x63e6f3`, clé `ui_text_6785` en `0x63e6e6`), `"Creature Name Change"`
  (`0xa51a06`), `"Decorative Pet Name Change"` (`0xa51b9f`) et la catégorie `"Pet name"`
  (`0xbdec35`) ; le client porte aussi les ressources d'interface `window_pet_skill.nui`
  (`0x643408`), `Create: SUIPetCommandWnd` (`0x6483b4`), `SUIPetCommandWnd::ProcMsgAtStatic`
  (`0x62d4fc`), `game_pet_cmd` (`0x654944`) et `PET_PICKUP_FILTER` (`0x64ce80`). Rien de tout cela
  ne dit *quelle* action déclenche l'émission serveur de 353 (§7).
- Le plafond de saisie de la boîte **n'est pas établi** : la boîte reçoit les constantes
  `0xd2` / `0x12` en `0x63c512`-`0x63c517`, mais l'unique autre appel du même constructeur
  (`0x53899f`) passe les mêmes `0xd2` / `0x12` avec une autre commande : ce ne sont pas des
  paramètres de longueur de nom mais des constantes fixes de la boîte. **Ne pas en déduire un
  plafond de 18 caractères côté client.**

---

## 3. Structure sur le fil

En-tête Navislamia — 7 octets (`Game/Network/Packets/Header.cs:6-11` : `uint Length`,
`ushort ID`, `byte Checksum` ; même disposition que rzu et que le client, §3.2).

### 3.1 `TM_CS_SET_PET_NAME` (354) — client → serveur — **30 octets**

| offset | taille | type | nom | source |
|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` = 30 (`0x1e`) | `TS_CS_SET_PET_NAME.h:5-9` ⇒ 4+19 ; **taille écrite en dur par le client** : `SFrame.exe 0x48c6d0` (`movl $0x1e,(%eax)`) |
| 4 | 2 | `uint16` | `ID` = 354 (`0x162`) | `TS_CS_SET_PET_NAME.h:11-13` ; client `0x48c6c5` (`mov $0x162,%ecx`) |
| 6 | 1 | `uint8` | `Checksum` | `MessageBuffer`/`Header` ; client : somme des octets 0..5 (`0x48e1a4`-`0x48e1c1`) |
| 7 | 4 | `ar_handle_t` | `handle` | `TS_CS_SET_PET_NAME.h:6` ; client `0x48e18e` (`mov %ecx,-0x19(%ebp)`, soit `+7`) |
| 11 | **19** | `string` fixe | `name` | `TS_CS_SET_PET_NAME.h:7-9` (`19` sous `EPIC_9_6`) ; client `0x48e199` (`push $0x13`) |
| **total** | **30** | | | 7 + 4 + 19 |

`name` : tampon **fixe de 19 octets, sans préfixe de longueur**, terminateur nul compris — donc
**18 caractères utiles** au plus. `MessageBuffer::writeString` écrit `maxSize` octets, tronque la
valeur à `maxSize - 1` et zéro-remplit le reste (`librzu/src/lib/Packet/MessageBuffer.cpp:87-95`) ;
le client, lui, fait un `memcpy` de 19 octets (`0x48e19c`) depuis un `std::string`.

### 3.2 En-tête et somme de contrôle, confirmés par le client

L'émetteur 355 voisin, `0x48e1f0` (non traité ici), écrit le même en-tête à la main :
tampon à `ebp-0x10`, zéros en `0x48e1f8`-`0x48e205`, `ID` `0x163` en `0x48e208`/`0x48e20d`
(offset 4), `Length` `0xf` en `0x48e211` (offset 0), puis boucle d'addition sur les octets
`-0x10`..`-0xa` et écriture en `-0xa` (offset 6) en `0x48e22d`. **La somme porte sur les
6 premiers octets**, exactement la règle du dépôt (`GameSummonPackets.WriteChecksum`).

### 3.3 `TM_SC_SHOW_SET_PET_NAME` (353) — serveur → client — **11 octets**

| offset | taille | type | nom | source |
|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` = 11 (`0xb`) | `TS_SC_SHOW_SET_PET_NAME.h:5-6` ⇒ 4 |
| 4 | 2 | `uint16` | `ID` = 353 (`0x161`) | `TS_SC_SHOW_SET_PET_NAME.h:8-10` |
| 6 | 1 | `uint8` | `Checksum` | idem §3.2 |
| 7 | 4 | `ar_handle_t` | `handle` | `TS_SC_SHOW_SET_PET_NAME.h:6` ; client : lu en `+7` (`0x66f74e`) |
| **total** | **11** | | | 7 + 4 |

Le client ne connaît pas de constructeur de trame pour 353 (aucun `mov $0x161` de type
constructeur dans `.text`) : c'est bien une trame que seul le serveur émet.

---

## 4. Gating de version — décisions pour Epic 7.3

Trois décisions seulement, toutes tirées de la macro `TS_CS_SET_PET_NAME_ID` / `_DEF` (§1).
Référence : `librzu/src/lib/Packet/PacketEpics.h:59` `EPIC_7_3 = 0x070300`, `:75`
`EPIC_9_6 = 0x090600`, `:96` `EPIC_9_6_3 = 0x090603`.

| champ / décision | gating rzu | **décision 7.3** | justification |
|---|---|---|---|
| id de 354 | `X(354, version < EPIC_9_6_3)` / `X(1354, version >= EPIC_9_6_3)` | **354** | la bascule `+1000` est datée « 20200713 » (`PacketEpics.h:96`), bien après 7.3 ; `1304` n'est pas un id libre en 7.3 (le dépôt déclare déjà `TM_SC_AUCTION_BIDDED_LIST = 1305`, `GamePackets.cs:120`) |
| id de 353 | `X(353, …)` / `X(1353, …)` | **353** | même bascule |
| largeur de `name` | `_(def)(…, 20)` + `_(impl)(…, 19, < EPIC_9_6)` / `(…, 20, >= EPIC_9_6)` | **19 octets** | 7.3 < 9.6 ; **confirmé par le client** : `memcpy` de `0x13` et taille de trame `0x1e` = 7+4+19 (§2.2, §3.1) |
| type de `handle` | `ar_handle_t` depuis le commit `05bc2d82` (2020-03-29) | **`uint32`** | « strong typedef » sans changement de largeur ; NGemity déclare `uint32_t` ; le client lit 4 octets |
| `1354` | id 9.6.3 | **ne pas déclarer** | au-delà de 7.3 |

**Aucun autre champ n'est gaté.** La famille `350-355` ne montre pas d'autre `>= EPIC_*`.

---

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` (NGemity) en fait : rien

`grep -rn "SET_PET_NAME" reference/ngemity` ne rend que des **déclarations** :
`shared/Server/Packets/GameClient/TS_CS_SET_PET_NAME.h`, `TS_SC_SHOW_SET_PET_NAME.h`,
`shared/Server/ClientPackets.h:132-133`, `shared/Server/XPacket.h:152,292`. **Aucun
gestionnaire**, aucun appel dans `WorldSession.cpp` — cohérent avec
`socle-invocations.md:330`. rzu non plus : `TS_CS_SET_PET_NAME` n'apparaît nulle part hors de
`librzu/src/packets/GameClient/`. La référence ne tranche donc **ni la réponse, ni la
persistance, ni la politique de nom** ; seule la lecture du client (lots 323 et celui-ci) les
tranche.

### 5.2 Ce que le serveur Navislamia doit faire du 354 reçu

1. **Lire** la trame par sa taille : en-tête 7, puis `handle` (`+7`, 4 octets) et `name` (`+11`,
   19 octets). Une longueur différente de 30 ⇒ journal d'avertissement, aucune suite
   (patron déjà en place : 323, 304, 324).
2. **Nom** : tampon de 19 octets terminé par un nul ; lire au plus 18 caractères utiles et
   **ignorer tout octet après le premier nul**. Si la trame n'est pas terminée par un nul dans
   les 19 octets, ne pas lire au-delà du champ (le tampon client est toujours zéro-rempli par,
   respectivement, `memcpy` de 19 octets d'un `std::string` (`0x48e19c`) — la partie au-delà de la
   longueur de la chaîne est du contenu non initialisé côté client, **à ne pas interpréter**).
3. **Cible** : le `handle` reçu est celui que le serveur a lui-même envoyé dans 353 (§2.2). Le
   serveur doit donc le retrouver dans **la même table que celle qui a servi à émettre 353** —
   `SummonWorldEntry`/`SummonWorldService` (`Game/Services/SummonWorldService.cs:51-93`) est le
   candidat existant ; **la résolution `handle → SummonEntity` reste à établir** (§7).
4. **Succès** : écrire `SummonEntity.Name` (`Game/DataAccess/Entities/Telecaster/SummonEntity.cs:21`,
   colonne `string` de la table `Summon`) — politique de nom (§7) : nettoyage du tampon seul,
   pas de filtre de gros mots (`BannedWordsRepository`, fiche 1202 §5.5, pose la question pour
   1202 mais aucun précédent ne l'impose ici), pas d'unicité.
5. **Émission 353** : voir 5.3.
6. **Échec** (handle inconnu, invocation non possédée par le personnage, trame malformée,
   nom vide) : **journal seulement, aucune trame de refus**. Aucune référence, aucun
   gestionnaire entrant du client pour une réponse au 354 n'existe — inventer un refus serait
   une supposition (même règle que 323 `§7(c)`).

### 5.3 La trame 353 : la précéder, pas la rejouer

Le card dit « la réponse du serveur est 353 ». La lecture du client **corrige la direction** :
353 n'est pas une réponse au 354, c'est la trame qui **précède** le 354 (elle ouvre la boîte de
saisie, §2.1). Le geste serveur correct est donc :

- **émettre 353** (11 octets, `handle` en `+7`) quand le serveur veut faire renommer un familier —
  c'est la seule trame S→C du couple, et le client n'a de handler que pour elle ;
- **ne pas la rejouer après un 354 réussi** : le cas 125 rouvre la boîte dès que le `handle` est
  résolu (`0x63c4a4`-`0x63c529`), donc un écho 353 après un renommage réafficherait la boîte.

Conséquence sur le dépôt : `TM_SC_SHOW_SET_PET_NAME = 353` doit être **déclaré** dans
`GamePackets` (c'est le seul moyen d'appeler l'encodeur) et **aucun bras de réception** n'est
requis pour la règle `CLAUDE.md:1823-1826` (id que le serveur n'émet que dans un sens). Mais le
critère transversal n° 4 exige qu'aucun membre déclaré ne puisse atteindre le `switch` final :
comme sept ids S→C de la famille (`301`, `302`, `305`, `306`, `307`, `320`, `321`) laissent
aujourd'hui ce trou ouvert (réserve de la fiche 323 §5.4), **ce lot ajoute au 353 le bras de
rejet** que le dépôt utilise déjà pour trois ids S→C (`GameClient.cs:987-992`,
`TM_SC_REGION_ACK` : journal + `continue`, sans `switch`). Le lot 354 reste ainsi conforme au
critère n° 4 sans rouvrir une réserve connue.

### 5.4 Où écrire le code (et pourquoi pas ailleurs)

Zones mesurées avec `git merge-tree --write-tree --name-only` sur la base `a1c4950` :

| branche sœur | `master` vs sœur | `master` + le lot 354 (points ci-dessous) vs sœur |
|---|---|---|
| `hermes/packet-323-change-summon-name` (MR #40) | **fusion propre** | **fusion propre** |
| `hermes/packet-304-summon` | **fusion propre** | **fusion propre** |
| `hermes/packet-324-get-summon-setup-info` | conflits `ConnectionInfo.cs`, `GameClient.cs`, `GameActionPackets.cs` (la branche est en retard sur `master`, conflits déjà présents sans 354) | idem — `GamePackets.cs` **ne conflit pas** |

Points d'insertion recommandés, choisis pour rester à plus de 7 lignes des ancres des sœurs :

1. **`Game/Network/Packets/Enums/GamePackets.cs`** : nouveau bloc `pet` juste **après
   `TM_CS_JOB_LEVEL_UP = 410` (ligne 84)**, jamais dans le bloc créature/invocation
   (`lignes 70-77`) : ses trois points d'ancrage internes sont déjà pris (304 après `303` = ligne 72,
   323 entre `307` et `320` = lignes 75-76, 324 après `321` = ligne 77) et le bloc de contexte git
   de ces trois lots couvre les lignes 69-81.
2. **`Game/Network/Clients/GameClient.cs`** : bras **`TM_CS_SET_PET_NAME` (354) juste avant le bloc
   anti-triche (ligne 1348)**, et bras de rejet **353** dans la même zone. Jamais en tête de
   chaîne (ancre de 323, avant `TM_CS_MOVE_REQUEST` = ligne 966) ni près du bras isolé
   `TM_SC_REGION_ACK` (ligne 987, ancre de 304) ni dans la queue de la chaîne (zone des lots
   invocation, 324).
3. **Lecteur et encodeur : `Game/Network/Packets/Game/GameSummonPackets.cs`**, qui porte déjà
   `NameSize = 19`, `WriteName`, l'en-tête 7 octets et la somme de contrôle, et qu'**aucune des
   trois branches sœurs ne touche** (leurs diffs ne listent que `GameActionPackets.cs` et
   `GameCharacterPackets.cs`). C'est le seul fichier de la famille sans zone partagée ; y lire le
   354 et y construire le 353 garde aussi les deux trames du couple dans un seul fichier, ce que
   la fiche 323 n'a pas pu faire. *Réserve : `GameSummonPackets.cs` s'annonce aujourd'hui « the
   summon packets the server emits (S→C) » ; y poser un lecteur C→S demande d'ajuster ce
   commentaire (`GameSummonPackets.cs:11-20`), pas de créer un fichier de plus.*
4. **Tests** : `Tests/Game/SetPetNamePacketsTests.cs` (nouveau fichier ⇒ aucun conflit de nom
   avec `ChangeSummonNamePacketsTests.cs` de 323 ni avec `SummonPacketsTests.cs` de 304).

---

## 6. Écarts assumés avec NGemity, et pourquoi

1. **NGemity ne fait rien du 354** (aucun gestionnaire, §5.1). Nous l'implantons parce que le
   client 7.3 l'émet, que sa sémantique est prouvée par le client lui-même (§2) et que le socle
   (`socle-invocations.md`) le liste comme naturel. Ce n'est pas un écart de protocole, c'est un
   écart de couverture : l'absence côté NGemity n'est pas une preuve d'inutilité
   (`socle-invocations.md:378`).
2. **NGemity ne connaît pas le gating de version** (un seul `CREATE_PACKET(…, 354)`, `_(string)(name, 19)`
   à plat) : ses 19 octets coïncident avec 7.3, mais par accident historique (il ne déclare pas
   `1354`). Nous suivons rzu, qui porte le gating, et le client, qui tranche.
3. **NGemity ne dit rien de la politique de nom** : pas de filtre, pas d'unicité, pas de
   persistance. Nous n'en inventons pas (§5.2, §7).
4. **La trame 353 est écartée du rôle de « réponse »** que le brief suggérait (§5.3) : c'est la
   lecture du client qui tranche, pas une préférence.

---

## 7. `NON ÉTABLI`

1. **Quel geste serveur émet 353.** Rien dans rzu, NGemity ni dans le client ne montre ce qui
   déclenche l'émission de 353 : objet de renommage (les noms `"Pet Name Change"` /
   `"Decorative Pet Name Change"` existent dans `db_string.rdb`, §2.3), commande MJ, ou fenêtre
   `SUIPetCommandWnd`. **À trancher : la/les règle(s) métier qui ouvrent la boîte.** Le lot dev ne
   doit pas l'inventer : il lit et applique le 354, et peut au plus exposer un envoi de 353 appelé
   par un chemin explicite.
2. **`handle` : quel objet exact, et comment le résoudre.** Prouvé : c'est un écho du 353 (§2.2).
   Non prouvé : ce que le serveur a mis dedans. `socle-invocations.md:641-642` laisse ouverte la
   distinction `cage_handle` / `pet_handle` ; `SummonEntity` (`SummonEntity.cs`) n'a pas de
   colonne de handle et `SummonWorldEntry` en fabrique un à l'entrée en jeu
   (`SummonWorldService.cs:51-67`). **À trancher : la table handle → familier, et si le handle
   est celui de l'invocation active (301) ou celui de la carte (351).**
3. **Persistance du nom.** `SummonEntity.Name` existe (`SummonEntity.cs:21`), mais rien ne dit si
   le nom vit sur `Summon` ou sur l'objet-carte, ni si le renommage est gratuit ou payant
   (`script_string_10017` du client parle d'un coût croissant, §2.3). **À trancher : la/les
   colonne(s) écrite(s) et le coût.**
4. **Politique de nom.** Longueur minimale utile, unicité par personnage, filtrage : non établis.
   Le plafond **côté client** n'est pas 18 caractères « garanti » (§2.3) : seul le tampon de 19
   octets du protocole l'est. **À trancher : ce que le serveur accepte et ce qu'il refuse.**
5. **Réponse au 354.** Aucune trame de résultat n'est établie (§5.2-6). Le lot journalise et
   n'invente rien ; si Killian veut un accusé de réception, il faudra le créer côté serveur **et**
   côté client — hors périmètre.
6. **355 `TM_CS_SET_PET_FILTER`** (voisin immédiat, `op_codes.md:122`) : absent de rzu, absent de
   NGemity. Le client 7.3 en a pourtant un émetteur complet (`0x48e1f0`, id `0x163`, **15 octets**,
   `handle` en `+7` puis un second dword en `+11`, et une classe de requête
   `AUSIMSG_REQ_SET_PET_FILTER` de type `11701`). Hors lot : **à router vers une carte dédiée.**
7. **Insertion exacte du bloc `pet`** (§5.4, ligne 84) : mesurée propre contre les trois sœurs,
   mais elle est guidée par la collision et non par la numérotation ; si le PO préfère la
   numérotation (après 321), c'est 324 qui doit alors bouger, pas ce lot.

---

## 8. Commits et binaires épinglés

| dépôt | commit | rôle |
|---|---|---|
| NavisLamia | `a1c495002c290cd1fb2e19edffeb8eff49553f46` | base de la branche |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | tête de `reference/rzu` |
| rzu | `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07) | création de `TS_CS_SET_PET_NAME.h` (« add all known GS packets as of 9.4 ») |
| rzu | `f0319180a3f78b8b96e342cbed2448a71491a870` (2019-03-31) | « Update 9.6 packets » — introduction du couple 19 / 20 |
| rzu | `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11) | ids versionnés : 354/1354 et 353/1353 |
| rzu | `05bc2d82dac16003bc06162584e8559826310bff` (2020-03-29) | `ar_handle_t` (strong typedef) |
| NGemity | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | tête de `reference/ngemity` |
| NGemity | `90a500d46acc39d21ef943a047a37d0a641f7af4` (2018-08-24) | création des deux en-têtes |
| NGemity | `44b7d25dac7a1f869490dc8b6ea3255ea68b8eb4` (2018-08-26), `8f5e80fa…` / `e1136b84…` (2020-04-27) | réécriture réseau puis formatage |

Binaires du client (pas de dépôt git pour `reference/client73` : l'empreinte tient lieu de pin) :

| fichier | `sha256` |
|---|---|
| `SFrame.exe` | `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` |
| `db_string.rdb` | `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` |

Lecture désassemblée : `objdump -d SFrame.exe` (PE i386, image base `0x400000`), adresses
virtuelles telles quelles.

---

## 9. Vérifications relevées sur la base `a1c4950`

| commande | code de sortie | résultat |
|---|---|---|
| `dotnet build Navislamia.sln -c Debug` (`NUGET_PACKAGES=/srv/navislamia/.nuget-cache`) | **0** | 21 avertissements, 0 erreur |
| `dotnet test Tests/Tests.csproj` | **0** | **976 réussis**, 0 échec, 0 ignoré |
| `git log --oneline origin/master..master` | 0 | vide (aucun commit local sur `master`) |

Le lot dev doit garder `dotnet build` à 0, `dotnet test` à 0 **et au moins 976 tests**, et
ajouter **au moins un test d'offsets** pour 354 (taille 30, `handle` à 7, `name` à 11) et pour
353 (taille 11, `handle` à 7).

---

## 10. Bloc à porter dans `CLAUDE.md`

*(Hermes protège `CLAUDE.md` : le rôle dev ne l'écrit pas, le bloc va dans la description de la MR.)*

```markdown
- A newly handled client packet gets a sheet in `docs/packet-specs/` (see Solution layout), and
  its id must be added to the `GamePackets` enum and to the `GameClient.Receive` dispatch chain
  **in the same change**: a declared id with no dispatch arm reaches
  `_ => throw new Exception("Unknown Packet Type")` and kills the receive loop. A server-to-client
  id that shares a sheet with a client-to-server one follows the same rule: `TM_SC_SHOW_SET_PET_NAME`
  (353) is declared with a reject-and-log arm, exactly like `TM_SC_REGION_ACK`, so that no member of
  `GamePackets` can reach the final `switch` (`docs/packet-specs/354-set-pet-name.md` §5.3).
- The pet rename pair is an echo: the `handle` of `TM_CS_SET_PET_NAME` (354) is the one the server
  itself sent in `TM_SC_SHOW_SET_PET_NAME` (353), so 353 *precedes* 354 and must not be replayed
  after a successful rename — the 7.3 client opens the name box again for every 353 whose handle
  resolves (`docs/packet-specs/354-set-pet-name.md` §2, §5.3).
```

---

## `A VERIFIER PAR KILLIAN`

1. **La direction du couple 353/354.** Le brief annonçait 353 comme « la réponse du serveur » au
   354. La lecture du client montre l'inverse : 353 ouvre la boîte de saisie, 354 la referme en
   portant le nom. Le lot dev tient la lecture du client. **Si tu veux malgré tout un accusé de
   réception, c'est une trame nouvelle à créer des deux côtés** — hors périmètre.
2. **Ce qui déclenche l'émission de 353.** Aucun chemin n'est établi (§7.1) : le lot dev ne peut
   donc pas appeler l'envoi de 353 depuis une règle de jeu. Décision attendue : quel geste
   (objet de renommage, commande MJ, fenêtre familier) ouvre la boîte.
3. **Le `handle` du 353.** Prouvé comme un écho, non prouvé quant à sa source (carte ou
   invocation, §7.2). Sans réponse, le lot dev ne peut pas résoudre la cible et devra se limiter
   à lire et journaliser, comme 323.
4. **Le nom : où il se persiste et ce qui est refusé** (§7.3-4). Sans politique, le lot dev
   écrira `SummonEntity.Name` au plus près (aucune unicité, aucun filtre, nom vide refusé par
   journal) — à confirmer.
5. **Le point d'insertion du bloc `pet` dans `GamePackets`** (ligne 84, §5.4) : mesuré sans
   conflit contre 323 et 304, mais guidé par la collision plutôt que par la numérotation.
6. **355 est prêt à être carté** : le client 7.3 en a un émetteur complet (15 octets) alors que
   rzu et NGemity l'ignorent (§7.6).
