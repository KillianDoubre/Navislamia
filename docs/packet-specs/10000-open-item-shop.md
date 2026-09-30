# 10000 — `TM_CS_OPEN_ITEM_SHOP` (boutique d'objets) / réponse `TM_SC_OPEN_ITEM_SHOP` (10001)

Fiche d'archéologie de protocole, Epic 7.3. Écrite en **lecture seule** sur les références
(`reference/rzu`, `reference/ngemity`, `reference/client73`) : aucun exécutable client, aucun Lua,
aucun script client n'a été lancé. Le client 7.3 tranche les faits de protocole (§2, §3.2), rzu
tranche la forme, les tailles et le gating (§1, §3, §4), NGemity tranche la logique serveur sous
réserve de sa version compilée (`EPIC_4_1_1`) — et ici NGemity **ne traite rien du tout** (§5).

## Verdict — arbitrages demandés

| Question | Verdict de cette fiche |
|---|---|
| 7.3 possède-t-il 10000 / 10001 ? | **Oui.** Ce sont les ids de la branche `< EPIC_9_6_3` : 10000 (`CS`) et 10001 (`SC`). Les ids 9000/9001 ne sont **pas** ces paquets en 7.3 : ils y désignent `TM_SC_OPEN_URL`/`TM_SC_URL_LIST` (§4). |
| Taille de la 10000 | **7 octets**, corps vide (en-tête seul) : c'est ce que déclarent rzu et NGemity (§3.1). Réserve : le convertisseur paquet→événement du client possède une branche 10000 qui lit 8 octets de charge utile — §7a. |
| Taille de la 10001 | **51 octets** (7 + 3 × `int32` + `char[32]` fixe, complété d'octets nuls, sans préfixe de longueur) — §3.2. La branche de conversion du client 7.3 pour 10001 **confirme** cette disposition (trois `int32` à 7/11/15, chaîne en clair à 19) : §2.3. |
| Le joueur peut-il déclencher la 10000 ? | **NON ÉTABLI.** Aucun constructeur de trame d'id 10000 n'existe dans `SFrame.exe` (§2.2, preuve négative exhaustive) : l'ouverture de la boutique est **entièrement locale** (clé `shop_url`, §2.1). Le fait que le client sache *recevoir* une 10001 (§2.3) est établi, l'émission de la demande ne l'est pas. |
| Le serveur doit-il répondre ? | **Non, pas aujourd'hui.** Les quatre champs de la 10001 sont des identifiants du **service web officiel** de boutique (`itemshop.rappelz.com`) : Navislamia n'a ni boutique d'objets, ni mot de passe à usage unique, ni nom de serveur de boutique. Inventer ces valeurs est exclu par le lot (§5.4, §7c). |
| Décision de code attendue | Déclarer `TM_CS_OPEN_ITEM_SHOP = 10000` dans `GamePackets` **avec** sa branche de dispatch (trace + rejet, aucun corps lu, **aucune réponse**), sur le modèle déjà en place pour `TM_SC_REGION_ACK` (`GameClient.cs:620-627`). **Aucun membre d'énumération pour 10001**, aucun envoi de 10001 tant qu'aucune valeur n'est décidée (§5.4). |

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id client → serveur | **10000** | `op_codes.md:273` ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_OPEN_ITEM_SHOP.h:8` |
| Nom | `TM_CS_OPEN_ITEM_SHOP` | `op_codes.md:273` ; `TS_CS_OPEN_ITEM_SHOP.h:11` |
| Id serveur → client | **10001**, `TM_SC_OPEN_ITEM_SHOP` | `op_codes.md:274` ; `reference/rzu/librzu/src/packets/GameClient/TS_SC_OPEN_ITEM_SHOP.h:14` |
| Ids alternatifs | `9000` (CS) et `9001` (SC) à partir d'`EPIC_9_6_3` — **collision** avec `TS_SC_OPEN_URL` (9000) et `TS_SC_URL_LIST` (9001) de la branche 7.3, voir §4 | `TS_CS_OPEN_ITEM_SHOP.h:9` ; `TS_SC_OPEN_ITEM_SHOP.h:15` ; `TS_SC_OPEN_URL.h:12-14` ; `TS_SC_URL_LIST.h:15-17` |
| Référence NGemity | `TS_CS_OPEN_ITEM_SHOP = 10000`, `TS_SC_OPEN_ITEM_SHOP = 10001` (non gatés) | `reference/ngemity/shared/Server/ClientPackets.h:297-298` ; `shared/Server/Packets/GameClient/TS_CS_OPEN_ITEM_SHOP.h:8` ; `.../TS_SC_OPEN_ITEM_SHOP.h:12` |
| Version de compilation NGemity | `EPIC 4.1.1` → branche « anciens ids », donc 10000/10001 : cohérent avec 7.3 | `shared/Common/Define.h:25` |
| État dans Navislamia | **absent** : aucun paquet ni handler. `grep -rn "10000\|10001\|OPEN_ITEM_SHOP" --include=*.cs Game/ Tests/` ne renvoie que des homonymes numériques sans rapport (`Game/Entities/World/VNumber.cs:15` `factor = 10000`, `Game/DataAccess/Entities/Enums/SkillEffectType.cs:143` `WeaponMastery = 10001`, `Game/Services/Stats/SkillPassiveCatalog.cs:13`, `Tests/AuthServer/AuthProtocolTests.cs:17`) | `Game/Network/Packets/Enums/GamePackets.cs` |
| En-tête commun client/serveur | 7 octets : `Length` (`uint32`), `ID` (`uint16`), `Checksum` (`uint8`) | `Game/Network/Packets/Header.cs:9-11,22-24` ; `Game/Network/Packets/PacketExtensions.cs:13` |
| Taille 10000 | **7 octets** | §3.1 |
| Taille 10001 | **51 octets** | §3.2 |

Le 7.3 du dépôt est antérieur à 9.6.3 : les ids sont **10000 (CS) et 10001 (SC)** (voir §4).
Famille voisine, **hors lot** : `10003` `TM_SC_COMMERCIAL_STORAGE_INFO`, `10004`
`TM_SC_COMMERCIAL_STORAGE_LIST`, `10005` `TM_CS_TAKEOUT_COMMERCIAL_ITEM` (`op_codes.md:275-277`).

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Le déclencheur est une commande d'interface, et elle n'envoie rien au serveur

Dans `SFrame.exe`, la commande d'interface `open_item_shop` est traitée par le répartiteur de
commandes NUI (zone `0x6399xx`) :

1. comparaison du nom de commande reçu avec la chaîne `open_item_shop`
   (littéral à la VA `0xa4c650`) : VA `0x6399fa`-`0x639a11` ;
2. si la commande est reconnue, lecture de la clé de configuration `shop_url`
   (littéral `0xa4c644`, toujours la même chaîne que celle trouvée par `strings`) :
   VA `0x639a32` et `0x639a53` ; la clé est résolue par le lecteur de configuration
   (`0x824160`/`0x823400`) ;
3. à défaut de clé, repli sur l'URL par défaut : la routine à la VA `0x62a5b0` (prologue
   `0x62a5b0`-`0x62a5b1`) reçoit la clé `shop.url` (VA `0x62a615`, littéral `0xa48a28`) et la valeur
   par défaut `http://itemshop.rappelz.com/login.aspx` (VA `0x62a608`, littéral `0xa48a34`), puis
   complète l'URL avec les paramètres locaux du joueur (VA `0x62a656`-`0x62a671` : longueur puis
   copie de la chaîne de paramètres, via `0x408b90`). Les quatre valeurs d'entrée y sont au passage
   repliées par des `xor` sur des constantes (VA `0x62a5f3`-`0x62a627`) : le client obfusque les
   paramètres avant de les mettre dans l'URL ;
4. repli « paramètres joueur » : VA `0x639a85`-`0x639aa7` (lecture de la chaîne `[esi+0x2f]` et
   des entiers `[esi+0x4b]`, `[esi+0x4f]`, `[esi+0x53]`, puis appel de `0x62a5b0` avec ces quatre
   valeurs).

Conséquence à retenir : en 7.3, **ouvrir la boutique d'objets ne produit aucune trame vers le
serveur de jeu**. Le client ouvre une vue web dont l'URL est construite localement à partir de
`shop_url` et des données du personnage déjà connues du client. C'est le même schéma que les autres
commandes web voisines du même bloc de littéraux (`.rdata` VA `0x00a4c5b4`-`0x00a4c6a0` :
`shop_url` `0xa4c644`, `open_item_shop` `0xa4c650`, gabarit de requête
`%s?name=%s&sex=%d&race=%d&job=%s&jlv=%d&lv=%d` `0xa4c5b4`, `ghelp_url`, `open_help_web`,
`open_guil_web` ; et `close_item_shop` `0xa20234`).

### 2.2 Le client ne construit aucune trame d'id 10000 (preuve négative)

Méthode (`objdump -d --no-show-raw-insn -M intel` sur `SFrame.exe`, puis recherche exhaustive des
immediats) :

- balayage de `.text` (VA `0x00401000`, taille `0x0060a8cf`) pour toutes les formes d'instruction
  pouvant porter l'id 10000 : `mov r32, 10000` (`b8`/`b9`/`ba`/`bb`…), `push 10000` (`68`),
  `cmp r32, 10000` (`3d`, `81 f8`-`81 ff`), `sub r32, 10000` (`2d`, `81 e8`-`81 ef`),
  `mov WORD PTR [reg+4], imm16` (`66 c7 40 04 …`), `mov DWORD PTR [reg+4], imm32` (`c7 40 04 …`) ;
- balayage de `.rdata` et `.data` pour l'octet-mot `10000` en `uint32`/`uint16` (table d'ids
  éventuelle) ;
- recherche des références croisées vers les littéraux `TM_CS_OPEN_ITEM_SHOP` (`0xa52de8`) et
  `TM_SC_OPEN_ITEM_SHOP` (`0xa52dd0`).

Résultats :

| Usage de 10000 trouvé dans le client | VA | Nature |
|---|---|---|
| Table id ↔ nom de paquet (aide au débogage) | `0x6792ee` (`mov eax,0x2710` après le `push` du littéral `0xa52de8`) | Référence de nom, **pas** de construction |
| Branche de conversion paquet → événement (cas `eax == 0x2710`) | `0x67e906` (`cmp eax,0x2710`) puis `0x67ebfa` | **Réception** d'une 10000, pas émission (§7a) |
| Constantes numériques sans rapport (arithmétique `float`/timers ; `push 0x2710` en VA `0x583bed`, `0x5863a9`, `mov eax,0x2710` en VA `0x68fdc9`) | `0x583bed`, `0x5863a9`, `0x68fdc9`, … | Hors protocole |
| Table de nombres décimaux en `.data`/`.rdata` | `0xc10610`, `0xaba468` | Hors protocole |

**Aucun constructeur de trame avec l'id 10000 n'existe.** La famille de constructeurs du client
(VA `0x658a80`, `0x658ad0`, `0x658b30`, `0x658b90`, `0x658be0`, `0x658c30`, …) suit toujours le même
gabarit : `mov DWORD PTR [eax], <longueur>`, zéro-fill de la charge utile, puis
`mov ecx, <id>` + `mov WORD PTR [eax+4], cx`, recalcul du checksum en `[eax+6]`. Dans cette famille
on trouve `9012` (`0x658a80`, 45 o), **`10001`** (`0x658ad0`, 27 o), `10010` (`0x658b30`, 129 o),
`10011` (`0x658b90`, 116 o), `10012` (`0x658be0`, 116 o) — et **jamais 10000**. Les littéraux
`TM_CS_OPEN_ITEM_SHOP`/`TM_SC_OPEN_ITEM_SHOP` n'ont chacun qu'**une** référence croisée
(`0x6792cc`, `0x679321` : la table de noms), donc aucun constructeur générique « par nom » n'existe
non plus.

Réserve de méthode : les archives `data.001`-`data.008` du client (Lua/NUI) ne sont pas présentes
sur le VPS (`reference/README.md`), donc un chemin scripté qui déclencherait une émission n'est pas
vérifiable ici ; voir §7e.

### 2.3 Le client sait recevoir une 10001, et c'est tout l'objet de la réponse

Le convertisseur paquet → événement du client possède une branche dédiée à 10001
(`0x67ec3d` : `sub eax,0x2711 ; cmp eax,3 ; jmp [eax*4+0x67f6cc]`, table `0x67f6cc` →
`10001` = `0x67ecd4`, `10002` = `0x67ec63`, `10003` = `0x67ed27`, `10004` = `0x67ed64`) :

- VA `0x67ecd4` : allocation d'un objet de `0x57` = 87 octets, puis appel du constructeur
  `0x598930` avec, dans l'ordre de pile `[ebx+0xf]` (offset **15**), le littéral `open_item_shop`
  (`0xa4c650`), `[ebx+7]` (offset **7**), `[ebx+0xb]` (offset **11**), et `lea ecx,[ebx+0x13]`
  (offset **19**, pointeur vers la chaîne en clair) : VA `0x67ecf2`-`0x67ed03` ;
- l'objet est ensuite placé dans la file d'événements `[esi+0x2c]` (même épilogue que les autres
  cas : `call 0x64d0e0` puis `jmp 0x67ef39`).

Traduction : recevoir 10001 revient, côté client, à **exécuter sa propre commande
`open_item_shop`** avec les valeurs reçues. C'est exactement ce que la §2.1 fait localement, mais
ici les valeurs viennent du serveur. Le nom `raw_server_name` doit donc être une chaîne en clair
lisible par le client (il en prend l'adresse : pointeur vers un tampon en ligne, **pas** un
`uint32` de longueur), et les trois entiers sont lus dans l'ordre de la déclaration rzu.

### 2.4 Réserve : le client sait aussi *construire* une trame d'id 10001, et elle fait 27 octets

Le constructeur `0x658ad0`-`0x658b2a` remplit une trame de **27 octets** (`Length = 0x1b`) avec
l'id `0x2711` (10001) et zéro-fill de `+4` à `+0x1a` ; **six** sites d'appel de la zone `0x666xxx`
la construisent puis l'émettent par `0x657750` (`0x66529d`, `0x666227`, `0x6663b0`, `0x666624`,
`0x6666b2`, `0x666d3a`), après y avoir copié (`strcpy` manuelle, VA `0x66620d`-`0x66621f`) une
chaîne globale à l'**offset 7**. L'émission est un envoi réel sur l'objet de connexion stocké en
`this+0x24c` (`0x657750` : `mov ecx,[eax+0x24c]`, puis `call 0x6498e0` via `[eax+0x25c]`).
La zone qui appelle ces constructeurs manipule les littéraux `auth_ip` (`0xa4a08c`), `auth_port`
(`0xa50f18`), `devel` (`0xa50f24`) et `Rappelz-Error` (`0xa50ee8`) : VA `0x6660f3`-`0x6661a3`.

Cette trame de 27 octets **contredit** la seule structure attestée pour 10001 (51 octets, §3.2).
Je ne tranche pas ici : voir §7b. Conséquence pratique : le dev ne doit figer **aucune** des deux
variantes comme « la » trame 10001 émise par le serveur.

## 3. Structure sur le fil

Convention de citation client : toutes les adresses sont des **VA** lues par `objdump -d` sur
`reference/client73/SFrame.exe` (sha256
`41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 o., `pei-i386` ;
`.text` VA `0x00401000` / `0x0060a8cf` ; `.rdata` VA `0x00a0f000` ; `.data` VA `0x00c10000`).
Le client et NavisLamia partagent le même en-tête de 7 octets (`Header.cs:9-11`) : `Length`
(`uint32` LE), `ID` (`uint16` LE), `Checksum` (`uint8`, somme des octets 0-5 tronquée à 8 bits —
`PacketExtensions.cs:13` ; côté client, les constructeurs calculent la même somme sur les 6 premiers
octets, p. ex. VA `0x658ae0`-`0x658aeb` et `0x658b18`-`0x658b27`).

### 3.1 `TM_CS_OPEN_ITEM_SHOP` — client → serveur — **7 octets**

| Offset | Type | Nom | Valeur attendue | Source |
|---|---|---|---|---|
| 0 | `uint32` LE | `Length` | `7` | `reference/rzu/librzu/src/packets/GameClient/TS_CS_OPEN_ITEM_SHOP.h:5` (macro `TS_CS_OPEN_ITEM_SHOP_DEF(_)` **vide** → aucune charge utile) ; `Game/Network/Packets/Header.cs:9` ; convention client : les constructeurs écrivent la longueur en `+0` avant l'id (VA `0x658ad8`, `0x658b10`) |
| 4 | `uint16` LE | `ID` | `10000` (`0x2710`) | `TS_CS_OPEN_ITEM_SHOP.h:8` ; `op_codes.md:273` ; table id↔nom du client à la VA `0x6792ee` |
| 6 | `uint8` | `Checksum` | somme des octets 0-5 | `Game/Network/Packets/PacketExtensions.cs:13` ; côté client la boucle de somme et l'écriture en `+6` : VA `0x658b16`-`0x658b27` |
| — | — | *(aucun champ)* | — | `TS_CS_OPEN_ITEM_SHOP.h:5-6` ; NGemity `shared/Server/Packets/GameClient/TS_CS_OPEN_ITEM_SHOP.h:6` (`DEF` vide également) |

**Taille totale attendue : 7 octets.** Le corps est vide : une trame 10000 ne porte, au-delà de
l'en-tête, **rien** que rzu ou NGemity attestent. Toute lecture de champ au-delà de l'offset 6 est
une invention (voir la réserve §7a).

### 3.2 `TM_SC_OPEN_ITEM_SHOP` — serveur → client — **51 octets**

| Offset | Type | Nom | Valeur | Source |
|---|---|---|---|---|
| 0 | `uint32` LE | `Length` | `51` | `Game/Network/Packets/Header.cs:9` ; somme des 4 champs ci-dessous |
| 4 | `uint16` LE | `ID` | `10001` (`0x2711`) | `reference/rzu/librzu/src/packets/GameClient/TS_SC_OPEN_ITEM_SHOP.h:14` ; `op_codes.md:274` ; table id↔nom du client VA `0x679331` |
| 6 | `uint8` | `Checksum` | somme des octets 0-5 | `Game/Network/Packets/PacketExtensions.cs:13` |
| 7 | `int32` LE | `client_id` | identifiant côté boutique (voir §7c) | `TS_SC_OPEN_ITEM_SHOP.h:8` ; lu par le client en `[ebx+0x7]` (VA `0x67ecfa`) |
| 11 | `int32` LE | `account_id` | identifiant de compte côté boutique (voir §7c) | `TS_SC_OPEN_ITEM_SHOP.h:9` ; lu par le client en `[ebx+0xb]` (VA `0x67ecf6`) |
| 15 | `int32` LE | `one_time_password` | mot de passe à usage unique (voir §7c) | `TS_SC_OPEN_ITEM_SHOP.h:10` ; lu par le client en `[ebx+0xf]` (VA `0x67ed00`) |
| 19 | `char[32]` | `raw_server_name` | nom de serveur en clair, chaîne terminée par un octet nul, **complétée d'octets 0 jusqu'à 32 au total** (aucun préfixe de longueur) | `TS_SC_OPEN_ITEM_SHOP.h:11` ; sémantique de `_(string)(name, 32)` : `librzu/src/lib/Packet/MessageBuffer.cpp:87-98` (écrit `maxSize` octets, `memset` à 0 au-delà de la chaîne) ; NGemity identique : `shared/Server/Packets/MessageSerializerBuffer.cpp:15-23` ; lu par le client comme **pointeur** vers un tampon en ligne à l'offset 19 (VA `0x67ecf2`, `lea ecx,[ebx+0x13]`) |

**Taille totale attendue : 51 octets** = 7 (en-tête) + 4 + 4 + 4 + 32.
Les trois entiers sont dans l'ordre de la déclaration rzu ; l'ordre des arguments du constructeur
client (`0x598930`, §2.3) n'est **pas** l'ordre des champs et ne doit pas servir de source pour les
renommer.

### 3.3 Ne pas confondre avec la chaîne *longueur + données* du voisin 10002

La branche de conversion du client pour **10002** (`TS_SC_OPEN_PAID_STORAGE`, table `0x67f6cc`,
VA `0x67ec63`-`0x67ecb4`) lit `u16@7`, `u16@9`, `u32@11`, **`u32` longueur en `+15`** puis les octets
de la chaîne en `+19` (`mov eax,[ebx+0xf] ; … malloc(len+1) ; memcpy(ptr, ebx+0x13, len)` ; VA
`0x67ec94`-`0x67ecae`). C'est un idiome **différent** de la 10001, où la chaîne est un tampon fixe
pris par adresse. Recopier l'idiome « longueur + données » sur la 10001 produirait 4 octets de
décalage et un nom de serveur faux.

## 4. Gating de version — tranché pour 7.3

| Élément | Verdict 7.3 | Source |
|---|---|---|
| Constante de version | `EPIC_7_3 = 0x070300` **<** `EPIC_9_6_3 = 0x090603` ⇒ branche « anciens ids » | `reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59` ; `:96` |
| Id 10000 (CS) | **retenu** (`X(10000, version < EPIC_9_6_3)`) | `TS_CS_OPEN_ITEM_SHOP.h:8` |
| Id 10001 (SC) | **retenu** (`X(10001, version < EPIC_9_6_3)`) | `TS_SC_OPEN_ITEM_SHOP.h:14` |
| Ids 9000/9001 (branche `>= EPIC_9_6_3`) | **écartés** : ce sont ceux d'une autre génération. En 7.3, 9000/9001 désignent déjà `TM_SC_OPEN_URL`/`TM_SC_URL_LIST` (`op_codes.md:268-269`) | `TS_CS_OPEN_ITEM_SHOP.h:9` ; `TS_SC_OPEN_ITEM_SHOP.h:15` ; `TS_SC_OPEN_URL.h:12-14` ; `TS_SC_URL_LIST.h:15-17` |
| Champs gatés dans la 10001 | **aucun** : `TS_SC_OPEN_ITEM_SHOP_DEF` n'a pas de variante conditionnelle par version (`_(impl)`/`_(def)` absents). Les quatre champs sont donc ceux de 7.3 | `TS_SC_OPEN_ITEM_SHOP.h:7-11` |
| Champ gaté dans la 10000 | **aucun** : `DEF` vide | `TS_CS_OPEN_ITEM_SHOP.h:5` |
| NGemity | compile `EPIC_4_1_1` (`shared/Common/Define.h:25`) et déclare `CREATE_PACKET(..., 10000/10001)` **sans gating** : cohérent avec 7.3, mais muet sur la branche 9.6.3+ | `shared/Server/Packets/GameClient/TS_CS_OPEN_ITEM_SHOP.h:8` ; `.../TS_SC_OPEN_ITEM_SHOP.h:12` |

Piège à écrire dans `CLAUDE.md` (bloc de MR) : **9000/9001 ne sont pas les ids de la boutique en
7.3**. Un serveur 7.3 qui répondrait 9001 pour ouvrir la boutique enverrait en réalité une
`TS_SC_URL_LIST` (`TS_SC_URL_LIST.h:9-13`, `count` `uint16` en 7.3 car `< EPIC_9_6_2`), et un
serveur 9.6.3+ qui répondrait 10001 enverrait un paquet dont l'id n'existe plus.

## 5. Traitement attendu

### 5.1 NGemity ne traite ni 10000 ni 10001

- Déclarations seulement : `shared/Server/Packets/GameClient/TS_CS_OPEN_ITEM_SHOP.h:6-8` et
  `TS_SC_OPEN_ITEM_SHOP.h:6-12`, incluses par `shared/Server/XPacket.h:126,267`, ids listés dans
  `shared/Server/ClientPackets.h:297-298` ;
- **aucun** handler : `grep -rn "OPEN_ITEM_SHOP\|ItemShop" Chihiro/ shared/` ne renvoie que ces
  déclarations (fichiers `Packets/**` et `XPacket.h`), rien dans le code serveur ;
- la famille voisine « stockage commercial » n'est pas traitée non plus : `TS_CS_TAKEOUT_COMMERCIAL_ITEM`,
  `TS_SC_COMMERCIAL_STORAGE_INFO/LIST` et `TS_SC_OPEN_PAID_STORAGE` n'apparaissent que dans
  `XPacket.h:164,214,215` et les en-têtes de paquets.

Conclusion : **il n'y a aucune logique serveur de référence à porter.** Ce qui est connu du
comportement officiel vient du client (§2) : la 10001 n'existe que pour donner au client les
identifiants de la boutique web.

### 5.2 Ce que rzu permet de dire du serveur

rzu déclare la sérialisation des deux paquets (`TS_CS_OPEN_ITEM_SHOP.h:5-11`,
`TS_SC_OPEN_ITEM_SHOP.h:7-15`) mais n'embarque aucun serveur : rien à en tirer de plus que la forme.
La 10000 ne porte **aucun champ** : un serveur qui la reçoit ne peut rien en déduire d'autre que
« le client demande la boutique ».

### 5.3 Ids inconnus dans Navislamia aujourd'hui

`GameClient` filtre déjà les ids non déclarés : `if (!Enum.IsDefined(typeof(GamePackets), header.ID))`
→ trace `"Undefined packet ID: …"` puis `continue` (`Game/Network/Clients/GameClient.cs:584-588`).
Une trame 10000 reçue aujourd'hui est donc **journalisée et jetée sans erreur** : rien ne casse,
mais rien n'est explicite non plus.

### 5.4 Décision de code pour le lot (à implémenter tel quel)

1. **Déclarer** `TM_CS_OPEN_ITEM_SHOP = 10000` dans `GamePackets.cs`, à la suite de
   `TM_CS_REPORT = 8000` et **avant** `TM_NONE = 9999` (fin de fichier) — cette énumération ne
   contient aujourd'hui ni `9000`/`9001`, ni `10000`/`10001`, donc aucune collision à gérer.
2. **Dispatcher** ce membre dans `GameClient.Process`, avant le `switch` final
   (`GameClient.cs:791-802`, `_ => throw new Exception("Unknown Packet Type")`) — les deux ensemble,
   sinon le membre atteint le `throw` (critère transversal 4). Modèle exact à suivre : la branche
   déjà écrite pour un paquet « déclaré mais jamais émis par le client 7.3 »,
   `TM_SC_REGION_ACK` (`GameClient.cs:620-627`) : `_logger.Warning(...)` + `continue`, aucune
   réponse, aucun corps lu.
3. **Ne rien répondre.** Pas de `Packet<TS_SC_OPEN_ITEM_SHOP>`, pas de `SendResult`, pas de
   constante de remplacement : les quatre champs sont des valeurs métier (§7c).
4. **Ne pas déclarer 10001** dans `GamePackets` tant qu'aucune émission n'existe : un membre sans
   branche de dispatch violerait le critère 4, et une branche sans contenu serait du code mort.
   La structure de §3.2 reste documentée ici, prête à être activée sur décision de Killian.
5. **Test d'offsets du lot** : au minimum, une trame 10000 de 7 octets (en-tête valide, `Length = 7`,
   `ID = 10000`, checksum correct) est **acceptée par le chemin de dispatch, journalisée et
   ignorée**, sans déconnexion ni `throw` ; le flux lit exactement `header.Length` octets
   (`GameClient.cs:580`), donc la trame doit être consommée en entier, sans reste. Si le dev choisit
   de *déclarer* aussi les structures de trame de §3.2 pour documentation, il doit leur adjoindre le
   test d'offsets correspondant (51 octets, positions 0/4/6/7/11/15/19) — sans jamais les émettre.

## 6. Écarts assumés avec NGemity, et pourquoi

| Point | NGemity | Navislamia (proposé) | Pourquoi |
|---|---|---|---|
| Traitement de la 10000 | déclarée, jamais lue | déclarée **et** dispatchée, trace + rejet | Le projet exige qu'aucun membre de `GamePackets` ne tombe dans le `throw` final (`GameClient.cs:802`) ; le précédent `TM_SC_REGION_ACK` (`:620-627`) montre l'idiome retenu pour un paquet que le client 7.3 n'émet pas. |
| Réponse 10001 | inexistante | inexistante aussi, mais structure figée dans cette fiche | Les valeurs sont des données de boutique web absentes du dépôt (§7c) ; ne pas les inventer. |
| Gating de version | `CREATE_PACKET` sans gating, compilation `EPIC_4_1_1` | même ids que la branche rzu `< EPIC_9_6_3`, **sans** alias 9000/9001 | NGemity est plus ancien que rzu et ne code pas la bascule 9.6.3 ; la suivre à l'aveugle ferait entrer une collision d'ids 7.3 (§4). |
| Sémantique de `_(string)(x, 32)` | `MessageSerializerBuffer.cpp:15-23` : `maxSize` octets, complétés à 0 | idem rzu (`MessageBuffer.cpp:87-98`) | Les deux implémentations concordent : champ fixe de 32 octets, pas de préfixe de longueur. C'est ce que confirme la lecture client (§2.3). |
| Nom du fichier de structure | `TS_CS_OPEN_ITEM_SHOP.h` / `TS_SC_OPEN_ITEM_SHOP.h` | mêmes noms, préfixes `TM_` côté énum | Convention du dépôt (`op_codes.md`, `GamePackets.cs`). |

## 7. `NON ÉTABLI` / `A VERIFIER PAR KILLIAN`

> **Statut après implémentation** : les sept réserves ci-dessous sont **reprises telles quelles** ;
> aucune n'a été devinée ni fermée par le code. Le traitement réservé à chacune est récapitulé en
> §9.5, et la liste complète est reportée dans la section « A VERIFIER PAR KILLIAN » de la MR.

**a) Le convertisseur du client attend-il 8 octets de charge utile pour la 10000 ?**
Le cas `eax == 0x2710` (`0x67e906`, `je 0x67ebfa`) construit un objet de 27 octets = 12 (en-tête
d'objet) + **15** octets de trame, et y recopie « trame + 7 » (`u16`), « trame + 9 » (`u16`) et
« trame + 11 » (`u32`) : VA `0x67ec13`, `0x67ec1b`, `0x67ec23`. Or rzu et NGemity déclarent la 10000
**sans aucun champ** (§3.1). Deux lectures possibles, non tranchables en lecture seule :
(i) le convertisseur est un formateur couvrant les deux sens (il couvre bien des ids CS voisins : la
plage `4002`-`4253` ouverte en VA `0x67e736` contient par exemple `4004`
`TM_CS_HUNTAHOLIC_JOIN_INSTANCE` (`op_codes.md:230`) et la plage `6001`-`6007` ouverte en VA
`0x67e828` contient `6002` `TM_CS_FOSTER_CREATURE` (`op_codes.md:258`)) et sa branche 10000 serait
une survivance d'un ancien gabarit ; (ii) la 10000 possède réellement
8 octets de charge utile en 7.3, et rzu/NGemity (7.x aussi, mais reconstruits) se trompent.
*Question précise à trancher : faut-il, oui ou non, lire des champs au-delà de l'offset 6 dans une
10000 reçue ? La fiche recommande « non » (§5.4) ; une capture de trafic officielle trancherait.*

**b) Le client émet-il lui-même une trame d'id 10001 de 27 octets ?** Six sites d'appel
(`0x66529d`, `0x666227`, `0x6663b0`, `0x666624`, `0x6666b2`, `0x666d3a`) construisent, via
`0x658ad0`, une trame de **27 octets** d'id `10001` contenant une chaîne ASCIIZ à l'**offset 7**, et
l'émettent par `0x657750` (envoi sur l'objet `this+0x24c`). Cette trame est incompatible avec les
51 octets de §3.2. Les appelants vivent dans la zone réseau/login (littéraux `auth_ip`, `auth_port`,
`devel`, `Rappelz-Error` : VA `0x6660f3`-`0x6661a3`), ce qui suggère un autre service que la 10001
de jeu — mais **je ne peux pas le prouver** (pas de capture, `data.001`-`data.008` absentes).
*Question précise : la trame 27 octets parle-t-elle au service web/erreurs du client (autre
protocole, ids réutilisés) ou au serveur de jeu ? Ne figer aucune variante sans cette réponse.*

**c) Que mettre dans `client_id`, `account_id`, `one_time_password`, `raw_server_name` ?**
Inconnu, et hors de portée du dépôt : ces valeurs sont produites par le **service web officiel**
(`http://itemshop.rappelz.com/login.aspx`, littéral client `0xa48a34`) et par la comptabilité
d'exploitation, pas par le serveur de jeu. Navislamia n'a **aucun** modèle de compte marchand, de
boutique, de catalogue ni de mot de passe à usage unique — le seul `OneTimePassword` du dépôt est
celui de `TM_CS_ACCOUNT_WITH_AUTH` (`Game/Network/Packets/Game/TM_CS_ACCOUNT_WITH_AUTH.cs:11`), qui
est un secret de **login**, à ne surtout pas réutiliser ici. *Sans décision de Killian (boutique
hébergée ? désactivée ? URL de serveur ?), aucune 10001 ne doit être émise.*

**d) Faut-il répondre quelque chose à une 10000 reçue ?** La fiche recommande « non » (§5.4).
Si Killian veut un refus explicite, l'idiome du dépôt serait `SendResult(TM_CS_OPEN_ITEM_SHOP, ...)`
avec un `ResultCode` — mais aucun code ne décrit ici « boutique indisponible », ce serait un choix
métier.

**e) Chemin scripté non vérifiable.** Les archives `data.001`-`data.008` du client (Lua/NUI) ne sont
pas présentes sur le VPS (`reference/README.md`) ; si `open_item_shop` (ou un équivalent) déclenche
dans un script une construction de trame par nom, elle ne serait pas visible dans `SFrame.exe`. La
preuve négative de §2.2 porte donc sur le binaire seul.

**f) Statut de `op_codes.md` pour les ids voisins.** La table du client attribue aussi `10010`
(`0x658b30`, 129 o), `10011` (`0x658b90`, 116 o), `10012` (`0x658be0`, 116 o), absents de
`op_codes.md` : l'inventaire d'ids du dépôt est incomplet au-delà de 10005 (`op_codes.md:277`).
Sans effet sur ce lot, à savoir pour les lots suivants.

**g) `9000`/`9001`.**
`op_codes.md:268-269` les décrit (`TM_SC_OPEN_URL`, `TM_SC_URL_LIST`) sans gating ; rzu montre que
les deux changent d'id en 9.6.3 (9000→8100, 9001→8101). Aucune des deux entrées n'existe
aujourd'hui dans `GamePackets` : les prochains lots qui les toucheront devront trancher la version
avant de coder.

## 8. Commits épinglés

| Référence | Commit / empreinte | Usage dans cette fiche |
|---|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | ids et gating (`TS_CS_OPEN_ITEM_SHOP.h`, `TS_SC_OPEN_ITEM_SHOP.h`, `TS_SC_OPEN_URL.h`, `TS_SC_URL_LIST.h`), constantes d'épique (`PacketEpics.h`), sémantique `_(string)(x, N)` (`MessageBuffer.cpp`) |
| `reference/ngemity` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | déclarations d'ids (`ClientPackets.h`), absence de handler, version compilée (`Define.h`), `writeString` (`MessageSerializerBuffer.cpp`) |
| `reference/client73/SFrame.exe` | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | arbre de décision du client : commande `open_item_shop` (§2.1), absence de constructeur 10000 (§2.2), branche de réception 10001 (§2.3), constructeur 27 o (§2.4), table id↔nom (`0x6792cc`/`0x679321`/`0x679364`), convertisseur (`0x67e906`-`0x67ef39`) |
| `Navislamia` (`master` au moment de la fiche) | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | en-tête (`Header.cs`), checksum (`PacketExtensions.cs`), dispatch et précédent `TM_SC_REGION_ACK` (`GameClient.cs`), énumération (`GamePackets.cs`), `op_codes.md` |
| Branche de ce lot | `hermes/packet-10000-open-item-shop` | porte cette fiche ; le dev y ajoute l'implémentation et les tests |

## 9. Implémentation livrée (dev)

**Périmètre : §5.4, à la lettre.** La demande `TM_CS_OPEN_ITEM_SHOP` (10000) est déclarée et
traitée ; `TM_SC_OPEN_ITEM_SHOP` (10001) et les ids 9000/9001 restent **non déclarés**, et le serveur
n'émet aucune trame pour ce lot.

*Note de nommage* : §5.3 et §5.4 nomment `GameClient.Process` ; la méthode réelle est
`GameClient.OnDataReceived` (`Game/Network/Clients/GameClient.cs`). Les numéros de ligne de la fiche
ont bougé avec les fusions : l'ancre du `switch` final est désormais en `GameClient.cs:803`, et le
précédent `TM_SC_REGION_ACK` en `:620-627`.

### 9.1 Critères transversaux

| Critère | État | Preuve |
|---|---|---|
| 1. `dotnet build Navislamia.sln -c Debug` | code **0** | `0 Error(s)`, 18 avertissements, aucun dans un fichier livré |
| 2. `dotnet test Tests/Tests.csproj` | code **0**, **458** cas | 448 avant le lot, 458 après ; `Failed: 0, Skipped: 0` |
| 3. Test d'offsets | 8 méthodes / **10 cas** (`Tests/Game/OpenItemShopPacketsTests.cs`) | trame 10000 : `Length` @0 (4 o), `ID` @4 (2 o), `Checksum` @6 (1 o), taille totale **7**, rien au-delà de l'offset 6 |
| 4. Énumération et dispatch ensemble | oui | test de `Enum.IsDefined` **et** test de la boucle réelle ; bras rendu inatteignable ⇒ 6 échecs `Unknown Packet Type` (§9.6) |
| 5. Savoir durable dans la fiche | ce chapitre + bloc `CLAUDE.md` en §9.9 | le dev n'écrit pas `CLAUDE.md` (fichier protégé), le bloc part dans la MR |
| 6. Version tranchée | 7.3, gating rzu suivi | §4 : 10000/10001 retenus, 9000/9001 écartés ; deux cas de test verrouillent l'absence de 9000/9001 |
| 7. Aucun commit sur `master` locale | `git log --oneline origin/master..master` **vide** | |
| 8. Aucun champ `NON ÉTABLI` deviné | les sept réserves a) à g) intactes | §9.5 |

### 9.2 Fichiers livrés

| Fichier | Changement |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_OPEN_ITEM_SHOP = 10000,` inséré entre `TM_CS_REPORT = 8000` et `TM_NONE = 9999` |
| `Game/Network/Clients/GameClient.cs` | bras `if (header.ID == (ushort)GamePackets.TM_CS_OPEN_ITEM_SHOP)` : `_logger.Warning(...)` puis `continue` — aucun octet lu après l'en-tête, aucune réponse |
| `Tests/Game/OpenItemShopPacketsTests.cs` | nouveau : 8 méthodes, 10 cas, harnais `FrameConnection` local |

Commit de code : `791330a` (branche `hermes/packet-10000-open-item-shop`) ; la fiche est dans
`3c0a5f2`, ce chapitre dans le commit de documentation qui suit.

### 9.3 Trame livrée et tests d'offsets

`TM_CS_OPEN_ITEM_SHOP`, **7 octets** :

| Offset | Type | Champ | Valeur imposée par les tests |
|---|---|---|---|
| 0 | `uint32` LE | `Length` | `7` |
| 4 | `uint16` LE | `ID` | `10000` (`0x2710`, octets `10 27` en LE) |
| 6 | `uint8` | `Checksum` | `0x3e` = `0x07 + 0x10 + 0x27`, somme des octets 0-5 |
| 7+ | — | *(aucun)* | la trame s'arrête là ; plusieurs cas vérifient qu'aucun champ n'est lu au-delà |

| Test | Ce qu'il fige |
|---|---|
| `Ids_AreTheEpic73Ones` | `TM_CS_OPEN_ITEM_SHOP == 10000` défini ; `10001`, `9000` et `9001` **non** définis |
| `ClientPacket_IsHeaderOnly` | `Marshal.SizeOf<Header>() == 7`, `Header.Length == 7`, `Header.ID == 10000`, aucun octet après l'offset 6 |
| `ClientPacket_ReadsLengthAndIdLittleEndian` | octets écrits à la main : `07 00 00 00` → 7 (et non `0x07000000`), `10 27` → 10000 (et non `0x1027`) |
| `ClientPacket_ChecksumCoversTheLengthAndTheId` | checksum = somme des six premiers octets, et il **dépend** de l'id |
| `OnDataReceived_ConsumesThePacketWithoutThrowing` | boucle de réception réelle : aucune exception, 7 octets consommés, aucun reste |
| `OnDataReceived_AnswersNothing` | aucun octet émis en retour |
| `OnDataReceived_LogsTheRefusalAsAWarning` | c'est bien le bras dédié qui traite la trame (journal `Warning`), et non la garde générique « Undefined packet ID » (`Debug`) |
| `OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne` | trame 10000 suivie d'un keepalive : les deux consommées, aucune réponse |
| `OnDataReceived_IgnoresAFrameCarryingTheHypotheticalPayload` (8 o) et `OnDataReceived_IgnoresAPaddedFrame` (15 o) | l'hypothèse de §7a est tolérée : une trame plus longue est consommée **en entier** et ignorée, le flux reste synchrone |

### 9.4 Ce qui n'est pas livré, volontairement

- Aucune trame **10001** n'est déclarée ni construite : la structure de §3.2 reste documentée ici,
  inactive. Déclarer l'id sans bras de dispatch ferait atteindre le `throw` final à une trame
  entrante (critère 4), et un bras sans contenu serait du code mort.
- Aucun `SendResult`, aucune constante de boutique, aucun modèle de compte marchand (§7c).
- Aucune réponse à 9000/9001 : ce ne sont pas les ids de la boutique en 7.3 (§4).
- Aucun parseur de charge utile : la `DEF` de rzu est vide et §7a n'est pas tranché.

### 9.5 Réserves de §7 → traitement

| Réserve | Traitement de ce lot |
|---|---|
| a) 8 octets de charge utile pour la 10000 ? | **Non lus** (recommandation §5.4 suivie). Deux cas de test prouvent qu'une trame de 8 ou 15 octets est consommée et ignorée sans décaler le flux, quel que soit le verdict de Killian. |
| b) trame cliente de 27 o d'id 10001 | Non tranché, **sans effet ici** : rien n'est émis. |
| c) valeurs de `client_id` / `account_id` / `one_time_password` / `raw_server_name` | Non produites (aucun modèle de boutique dans le dépôt). |
| d) répondre à une 10000 reçue ? | **Non** : journal `Warning` + `continue`, aucun `SendResult` inventé. |
| e) chemin scripté (archives `data.00x` absentes) | Inchangé ; non vérifiable sur le VPS. |
| f) ids voisins 10010-10012 absents d'`op_codes.md` | Inchangé, hors périmètre. |
| g) gating de 9000/9001 | Aucun des deux n'est déclaré, et leur absence est verrouillée par un test. |

### 9.6 Invariant énumération / dispatch, et mesure d'inachèvement

- `GamePackets` : **84** membres ; **48** référencés dans `GameClient.cs` (dont le nouveau) ;
  **36** absents de `GameClient.cs`, dont **35** `TM_SC_*` (jamais envoyés par un client 7.3) et
  `TM_EQUIP_SUMMON` (id serveur→client, référencé ailleurs dans `Game/`). Sur l'ensemble de `Game/`,
  seuls `TM_SC_CHAT_RESULT` et `TM_SC_ITEM_COOL_TIME` ne sont référencés nulle part — état
  préexistant, non touché.
- Mesure d'inachèvement : avec la condition du bras remplacée par `(ushort)GamePackets.TM_NONE`
  (bras inatteignable), **6 des 10 cas échouent** sur `System.Exception: Unknown Packet Type` :
  `ConsumesThePacketWithoutThrowing`, `AnswersNothing`, `LogsTheRefusalAsAWarning`, `KeepsTheLoop`,
  `IgnoresAFrameCarryingTheHypotheticalPayload`, `IgnoresAPaddedFrame`. Fichier restauré à
  l'identique — sha256
  `cf8beb56af6f86b66b85d3b395e7ca7170f8cec677986f39dcbe622ccae3596c` avant et après.

### 9.7 Placement du bras et conflits de fusion

Le bras est posé **à côté du bras `TM_SC_REGION_ACK`** (le précédent nommé par §5.4), et non à
l'ancre du `switch` final. Mesure `git merge-tree --write-tree --name-only <branche> HEAD` :

- à l'ancre du `switch`, le bras créait un conflit **neuf** avec `hermes/packet-223-swap-equip`,
  `hermes/packet-57-check-illegal-user`, `hermes/packet-59-xtrap-check` et
  `hermes/packet-socle-anti-triche` — quatre branches propres contre `master` seul avant ce lot ;
- à l'emplacement livré, ces quatre branches fusionnent proprement. Les seuls conflits restants
  (`221-hide-equip-info`, `9005-security-no`, `socle-invocations`, `socle-mort-respawn`,
  `socle-zones-evenement`) existent déjà contre `master` seul : ils sont antérieurs et étrangers à
  ce lot.

*Point chaud* : `Game/Network/Clients/GameClient.cs`, ancre du `switch` final — au moins cinq
branches ouvertes y insèrent leur bras au même endroit.

### 9.8 Commandes et codes de sortie relevés

```
export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
dotnet build Navislamia.sln -c Debug     # exit 0 — 0 Error(s), 18 warnings
dotnet test Tests/Tests.csproj           # exit 0 — Failed: 0, Passed: 458, Skipped: 0
git log --oneline origin/master..master  # vide
```

### 9.9 Bloc prêt à coller dans `CLAUDE.md`

```markdown
### Paquet 10000 — `TM_CS_OPEN_ITEM_SHOP` (boutique d'objets)

- 7.3 = ids **10000** (CS) / **10001** (SC) : rzu bascule sur **9000/9001** à partir d'`EPIC_9_6_3`
  (`TS_CS_OPEN_ITEM_SHOP.h:8-10`, `TS_SC_OPEN_ITEM_SHOP.h:14-16`), et `EPIC_7_3 = 0x070300` est sous
  `0x090603`. **Piège** : en 7.3, 9000/9001 désignent déjà `TM_SC_OPEN_URL` / `TM_SC_URL_LIST`
  (`op_codes.md:268-269`) — ne jamais s'en servir comme ids de la boutique.
- La 10000 est une **trame d'en-tête seule, 7 octets** (`TS_CS_OPEN_ITEM_SHOP_DEF` est vide, NGemity
  identique) : aucun champ au-delà du checksum à l'offset 6. **Ne rien lire en `+7`** — le
  convertisseur du client a une branche 10000 qui lit 8 octets de charge utile, mais rzu et NGemity
  n'en déclarent aucun (réserve a de la fiche, non tranchée).
- Le client 7.3 **n'émet jamais** cette trame : ouvrir la boutique est une commande locale
  (`open_item_shop`) qui construit l'URL depuis la clé `shop_url`. L'id est donc déclaré **et**
  traité par un bras `Warning` + `continue` (aucun corps lu, **aucune réponse**), posé à côté du
  précédent `TM_SC_REGION_ACK`.
- **Aucune trame 10001 n'est émise** : ses quatre champs (`client_id`, `account_id`,
  `one_time_password`, `raw_server_name`) sont produits par le service web officiel et n'existent
  nulle part dans ce dépôt. La réponse fait **51 octets** = 7 + 3 × `int32` + 32 ; **jamais**
  l'idiome « longueur + données » du voisin 10002, qui décalerait tout de 4 octets.
- `raw_server_name` est un **tampon fixe de 32 octets** terminé par un zéro (aucun préfixe de
  longueur), complété de zéros au-delà de la chaîne — sémantique `_(string)(name, 32)`.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.
```

### 9.10 A VERIFIER PAR KILLIAN (reprise de §7, à reporter dans la MR)

1. **Réserve a** — une 10000 reçue porte-t-elle 8 octets de charge utile que le serveur devrait lire ?
   Le code actuel n'en lit aucun et consomme la trame quelle que soit sa longueur annoncée : le
   choix est réversible et sans effet sur la synchronisation du flux.
2. **Réserve b** — la trame de 27 octets d'id 10001 construite par six sites du client parle-t-elle
   au serveur de jeu ou à un autre service (web/erreurs) ? Décision requise **avant** toute émission.
3. **Réserve c** — la boutique : hébergée, désactivée, ou servie par Navislamia ? Sans cette décision,
   aucune 10001 ne doit être émise (les quatre valeurs sont des données métier du service web).
4. **Réserve d** — faut-il répondre `TM_SC_RESULT` à une 10000 reçue ? Le lot ne répond rien ; aucun
   `ResultCode` ne décrit « boutique indisponible » aujourd'hui.
5. **Réserve e** — archives `data.001`-`data.008` absentes du VPS : un chemin scripté déclenchant la
   trame par nom ne serait pas visible dans `SFrame.exe`. Non vérifiable ici.
6. **Réserve f** — `op_codes.md` est incomplet au-delà de 10005 (10010-10012 connus du client et
   absents du dépôt) : à savoir pour les lots suivants.
7. **Réserve g** — 9000/9001 (`TM_SC_OPEN_URL`, `TM_SC_URL_LIST`) changent d'id en 9.6.3 : les lots
   qui les toucheront devront trancher la version avant de coder. Aucun des deux n'est déclaré ici.
