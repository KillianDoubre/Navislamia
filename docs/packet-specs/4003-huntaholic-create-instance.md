# 4003 — `TM_CS_HUNTAHOLIC_CREATE_INSTANCE`

Suivi : `navislamia:packet:4003` ; carte Trello du cycle `AWSLmWER` ; lot **S4** du socle
`docs/packet-specs/socle-instances-jeu.md`.

Fiche du paquet **4003** seul (client → serveur) : format sur le fil, gating Epic 7.3, conduite
serveur attendue. Elle reprend et vérifie la §3.3.2 de `docs/packet-specs/socle-instances-jeu.md`
(lot **S4** du socle, qui range 4003 **et** 4004 dans le même lot) ; **4004**
(`TM_CS_HUNTAHOLIC_JOIN_INSTANCE`, 28 octets) a sa propre carte et sa propre branche : il n'est
mentionné ici que là où la preuve client est **commune aux deux ids**, jamais déclaré.

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id décimal | `4003` (`0x0FA3`) | `op_codes.md:229` — `[4003] = "TM_CS_HUNTAHOLIC_CREATE_INSTANCE"` |
| Nom `TM_CS_*` | `TM_CS_HUNTAHOLIC_CREATE_INSTANCE` | idem |
| Sens | client → serveur | rzu `TS_CS_HUNTAHOLIC_CREATE_INSTANCE.h:13` (`SessionPacketOrigin::Client`) |
| Taille totale | **56 octets** (en-tête de 7 + 49 de charge utile) | §3.2 |
| État du dépôt | **absent** de `GamePackets.cs` au commit de base `a1c4950` : l'énumération passe de `TM_CS_CHECK_CHARACTER_NAME = 2006` à `TM_CS_INSTANCE_GAME_ENTER = 4250` (`Game/Network/Packets/Enums/GamePackets.cs:144-148`) | lecture du fichier |
| Nature | **commande de création de salle** du lobby HuntaHolic : pas de réponses serveur → client déclarées pour cet id dans rzu ni dans NGemity (§5.1) | — |

---

## 2. Ce que le joueur fait pour que le client l'envoie

Le joueur ouvre la fenêtre de création d'instance HuntaHolic (`SUIHuntaHolicCreateInstanceWnd`,
chaîne `Create: SUIHuntaHolicCreateInstanceWnd` présente dans `SFrame.exe`), saisit un **nom de
salle**, un **effectif maximum** et un **mot de passe** (la fenêtre de confirmation
`SUIHuntaHolicConfirmPasswordWnd` existe aussi), puis valide. Le client construit alors la trame
**4003** et l'envoie par le chemin réseau habituel.

Preuve client (constructeur / chemin d'envoi) :

- `0x563e57` : `lea ecx,[ebp-0x8c]` puis `0x563e5d` : `call 0x563c20` — construction de la trame
  dans un tampon de pile de 56 octets (le tampon va de `[ebp-0x8c]` à `[ebp-0x54]`).
- `0x563e62`-`0x563e7c` : copie **bornée à 31 octets** (`push 0x1f` en `0x563e79`) de la chaîne
  `std::string` du nom (tampon `[ebp-0x54]`, taille `[ebp-0x40]`) vers `[ebp-0x85]`.
- `0x563e81`-`0x563e8d` : lecture d'un **octet** de la fenêtre (`mov dl,BYTE PTR [ecx+0xa2bf68]`,
  0x563e84) recopié en `[ebp-0x66]`.
- `0x563e90`-`0x563ea7` : **si** le champ de mot de passe est renseigné (`cmp DWORD PTR [ebp-0x28],ebx`
  avec `ebx = 0` en `0x563e90`, saut en `0x563eaf` sinon), copie **bornée à 17 octets**
  (`push 0x11` en `0x563ea4`) vers `[ebp-0x65]`.
- `0x563eaf`-`0x563edb` : envoi — `lea ecx,[ebp-0x8c]` (la trame), `mov ecx,DWORD PTR [edx+0x4a0]`,
  `mov edx,[eax+4]`, `call edx` (emplacement 4 de la vtable du gestionnaire réseau).

La taille maximale acceptée par les zones de saisie (30 caractères pour le nom, 16 pour le mot de
passe) est **reprise du socle** (`docs/packet-specs/socle-instances-jeu.md:118` : « nom (30 car.
max), effectif max., mot de passe (16 car. max) ») : elle n'est **pas** re-vérifiée ici (§7, point 4).
Ce qui est vérifié ici, c'est la **borne** des copies : 31 et 17 octets, jamais plus (§3.3).

Le paquet **4003 est donc émis uniquement par cette fenêtre** : le seul constructeur de trame
identifié est `0x563c20`, atteint depuis la fenêtre de création (`0x563e5d`). Les autres occurrences
de la constante `0xfa3` dans le binaire sont des emplois **non constructeurs** : comparaison côté
réception (`cmp eax,0xfa3` en `0x4774f8`, `mov ecx,0xfa3` en `0x47cbce` et `0x47cc31`, §5.2),
journal de débogage (`sub eax,0xfa3` en `0x66e0df`, relevé par le socle `:91`), argument d'appels de
fenêtre UI (`push 0xfa3` en `0x5af964` et `0x5af982`, suivi de `call 0x439d30` / d'un appel de vtable)
et entrée d'un autre commutateur (`push 0xfa3` suivi d'un `jmp` en `0x8b9c89` et `0x8b9e9c`). Aucun
d'entre eux n'écrit 4003 dans un en-tête de trame.

---

## 3. Structure sur le fil

### 3.1 Table des champs

| Offset | Taille | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` LE | `Length` | **56** (`0x38`) | rzu : en-tête fixe de 7 octets (`PacketDeclaration.h:576-580`, `CREATE_STRUCT_IMPL(name_, 7, …)`) ; client : `mov DWORD PTR [esi],0x38` en `0x563c56` |
| 4 | 2 | `uint16` LE | `ID` | **4003** (`0x0FA3`) | `op_codes.md:229` ; rzu `TS_CS_HUNTAHOLIC_CREATE_INSTANCE.h:10-11` (`X(4003, true)`) ; client : `mov eax,0xfa3` en `0x563c48` puis `mov WORD PTR [esi+0x4],ax` en `0x563c52` |
| 6 | 1 | `uint8` | `Checksum` | somme des octets 0..5, modulo 256 | client : boucle `add cl,BYTE PTR [eax]` sur `[esi..esi+5]` en `0x563c31`-`0x563c3a`, puis **recalcul** en `0x563c5e`-`0x563c6b` et écriture en `[edi]` (= `esi+6`) ; serveur : `Game/Network/Packets/PacketExtensions.cs:13-21` (même somme sur `Length` + `ID`) |
| 7 | 31 | `char[31]` | `name` | tampon **fixe** de 31 octets, NUL final | rzu `TS_CS_HUNTAHOLIC_CREATE_INSTANCE.h:6` (`_(string)(name, 31)`) ; client : copie bornée de 31 octets vers `[ebp-0x85]` (0x563e79-0x563e7c) et charge utile de la trame à `[ebp-0x85]` = `[ebp-0x8c]+7` |
| 38 | 1 | `int8` | `max_member_count` | octet de la fenêtre | rzu idem `:7` (`_(simple)(int8_t, max_member_count)`) ; client : `mov BYTE PTR [ebp-0x66],dl` en `0x563e8d`, soit `7+31` |
| 39 | 17 | `char[17]` | `password` | tampon **fixe** de 17 octets, NUL final | rzu idem `:8` (`_(string)(password, 17)`) ; client : copie bornée de 17 octets vers `[ebp-0x65]` (0x563ea0-0x563ea7), soit `38+1` |
| 56 | — | — | *fin de trame* | `[ebp-0x54]` = `[ebp-0x8c] + 0x38` | client : `push 0x38` en `0x563c3c` (taille du `memset`) et `mov DWORD PTR [esi],0x38` en `0x563c56` |

**Taille totale attendue : 56 octets.** `7 + 31 + 1 + 17 = 56`.

### 3.2 Concordance des trois sources

| Source | Ce qu'elle dit | Somme |
|---|---|---|
| rzu `TS_CS_HUNTAHOLIC_CREATE_INSTANCE_DEF` (`…CREATE_INSTANCE.h:5-8`) | `string(31)` + `simple(int8_t)` + `string(17)` | 31 + 1 + 17 = 49, plus l'en-tête rzu de 7 octets = **56** |
| NGemity `TS_CS_HUNTAHOLIC_CREATE_INSTANCE_DEF` (`shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_CREATE_INSTANCE.h:6-9`) | **la même `DEF`, à l'identique** | 56 |
| Client 7.3 (`0x563c20`-`0x563c6f`) | `memset(trame, 0, 0x38)`, `Length = 0x38`, `ID = 0xfa3`, nom de 31 octets à +7, octet à +38, mot de passe de 17 octets à +39, fin de tampon à +56 | 56 |

Les trois concordent exactement : **aucun écart de format à arbitrer**.

### 3.3 Sémantique des chaînes

`_(string)(nom, N)` de rzu s'écrit par `MessageBuffer::writeString` (`reference/rzu/librzu/src/lib/Packet/MessageBuffer.cpp:87-94`) :
`stringSize = min(val.size(), N-1)`, puis complément par des NUL jusqu'à `N`. C'est un **tampon
fixe** : au plus `N-1` caractères utiles, le NUL est **dans** le champ, le reste du champ est nul.

Le client fait exactement la même chose : le `memset(trame, 0, 0x38)` de `0x563c43` (appel à
`0x976b30` avec `(trame, 0, 0x38)`) met à zéro les 56 octets **avant** que l'identifiant et la
longueur ne soient écrits (`0x563c48`-`0x563c56`), puis les deux copies bornées remplissent les champs.
Conséquence pour le serveur (reprise du socle `:506`) : un nom de 31 caractères **sans NUL**
déborde sur `max_member_count` à la lecture — la lecture serveur doit traiter le champ comme un
`char[31]` et ne jamais supposer un NUL au-delà.

### 3.4 Forme du test d'offsets attendu

Un test d'offsets sur `byte[]` construit à la main, dans le style du dépôt
(`Tests/Game/InstanceGamePacketsTests.cs` pour le lot S1) :

1. `Length` à l'offset 0 = **56** et `ID` à l'offset 4 = **4003** ;
2. `name` : champ de 31 octets **à l'offset 7** — un nom de 30 caractères est suivi de son NUL **dans**
   les 31 octets, un nom de 31 caractères est refusé/tronqué (jamais de débordement sur l'octet 38) ;
3. `max_member_count` : 1 octet **à l'offset 38**, type `int8_t` chez rzu — un test sur `0xFF`
   fige la convention de lecture (octet signé ou non) ;
4. `password` : champ de 17 octets **à l'offset 39** — 16 caractères + NUL, fin de trame à 56 ;
5. **refus** : toute `Length` ≠ 56 (55, 57 ou 7) est rejetée et journalisée, sans réponse (§5.3) ;
6. le gating 7.3 (§4) : le test doit porter sur l'id **4003** déclaré comme non gaté — aucune
   variante de version n'existe pour ce paquet.

---

## 4. Gating de version

| Question | Réponse pour Epic 7.3 | Source |
|---|---|---|
| L'id lui-même est-il gaté ? | **Non.** `X(4003, true)` : dans rzu, le second argument de `X(...)` est la **condition de version** (`SERIALISATION_F_ID2(id_, condition_)` → `if (condition_) id = id_;`, `PacketDeclaration.h:609-612`). `true` = l'id vaut 4003 pour **toutes** les versions. | rzu `TS_CS_HUNTAHOLIC_CREATE_INSTANCE.h:10-11` |
| Un champ de la charge utile est-il gaté ? | **Non, aucun.** Le `_DEF` (lignes 5-8) ne contient **aucun** `#if EPIC_*` ni entrée conditionnelle : les trois champs sont inconditionnels. | rzu `…CREATE_INSTANCE.h:5-8` |
| La version du socle (7.3) est-elle celle du paquet ? | **Oui.** Le client Epic 7.3 construit la trame (constructeur `0x563c20`, id `0xfa3`) et la compare en réception (§5.2) : la forme à 56 octets est celle de 7.3. | client `0x563c20`, `0x4774f8` |

**Décision écrite : 4003 est un paquet non gaté, identique en 7.3 ; aucun champ n'est à trancher
par version.** C'est la différence avec les pièges de version de `CLAUDE.md` : ici il n'y a rien à
gater, et il ne faut **pas** inventer de variante.

En-tête : 7 octets (`CREATE_STRUCT_IMPL(name_, 7, …)` via `CREATE_PACKET_VER_ID`), confirmé côté
client par la boucle de checksum sur les 6 premiers octets et par l'écriture du checksum en +6.

---

## 5. Traitement attendu

### 5.1 Ce que les deux références serveur en font : **rien**

| Référence | Traitement de 4003 | Source |
|---|---|---|
| rzu | **Aucun code serveur.** `grep -rn "HUNTAHOLIC_CREATE_INSTANCE" reference/rzu --include=*.cpp --include=*.h` hors `librzu/src/packets` : **zéro** résultat. `rzgame` ne référence aucun `TS_CS_HUNTAHOLIC_*`. | recherche sur `reference/rzu` (commit `87c1e83`) |
| NGemity | **Aucun handler.** 4003 n'apparaît que comme déclaration (`shared/Server/ClientPackets.h:236`, `shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_CREATE_INSTANCE.h:11`, inclusion dans `shared/Server/XPacket.h:107`) ; `Chihiro/src` ne contient **aucun** traitement de la famille HuntaHolic (`grep -rn "HUNTAHOLIC" reference/ngemity/Chihiro/src` → uniquement `ItemTemplate.hpp`, `Unit.h`, `MonsterBase.h`, `Skill*.h`, `GroupManager.h` : rien sur le lobby). | recherche sur `reference/ngemity` (commit `38ceb2c`) |

**Conséquence : la conduite serveur ne peut pas être portée depuis NGemity** (le profil dit
« NGemity tranche la logique », mais NGemity n'implémente pas ce lobby du tout — §6). Le client 7.3
reste le seul arbitre, et c'est lui qui donne la seule contrainte dure ci-dessous.

### 5.2 Ce que le client 7.3 attend en retour — et ce qu'il n'attend pas

**Le client n'a aucune réaction au succès, mais il en a une au refus par quota épuisé.** Chaîne de
preuve, sur `SFrame.exe` désassemblé (`objdump -d -M intel`, 2 256 750 lignes, sha256 en §8) :

1. `0x47fa80` = `SCommandSystem::ProcMsgAtStatic` (chaîne `.rdata 0xa1d688`, juste après l'étiquette
   `case MSG_RESULT\t\t  : ` en `0xa1d670`). Le dispatcher lit le type du message en
   `[esi+0x4]` (`0x47fabc`), traite quelques ids à part (1003/1004/1005/1031/1032/1034/1066/1071 en
   `0x47fac6`-`0x47fb2c`), puis indexe la table d'octets en `0x4801dc` et la table de sauts en
   `0x480120` pour les valeurs 4..0xac.
2. L'entrée **0** de la table de sauts (`0x480120` → `.text 0x47fb40`) est
   `push esi; mov ecx,edi; call 0x4770a0` (0x47fb43), suivie de l'étiquette `0xa1d670` =
   `"case MSG_RESULT…"` : **`0x4770a0` est le handler du cas `MSG_RESULT`**. Ce chemin est bien
   celui de la **réception** : `ProcMsgAtStatic` est aussi appelé en `0x68e8c1`, dans une fonction
   qui vient elle-même de lire un id de message en `[edi+0x4]` (`0x68e8de`).
3. Dans `0x4770a0`, le filtre est **`movzx eax,WORD PTR [edi+0x13]`** (`0x4770fa`), puis une
   cascade de comparaisons dont les valeurs sont des **ids de paquets client → serveur** —
   vérifiés un à un dans `op_codes.md` : 5 (`sub eax,0x5` en `0x477120`), 204 (`sub eax,0xc7`
   en `0x477125`, soit 5 + 0xC7), 253 (`sub eax,0x31` en `0x47712c`), 259 (`cmp eax,0x103` en
   `0x47710f`), 260 (`sub eax,0x104` en `0x477266`), 280 (`sub eax,0x14` en `0x47726d`),
   400 (`sub eax,0x78` en `0x477272`), 402 (`cmp eax,0x192` en `0x477103`), 410 (`cmp eax,0x19a`
   en `0x4774ea`), 700 (`cmp eax,0x2bc` en `0x4774f1`),
   **`0xfa3` (4003, `0x4774f8`)** et `0xfa4` (4004, `0x4774dd`).
4. **4003 et 4004 partagent le même corps** (`je 0x477503` depuis 4004 en `0x4774e8`, et
   retombée directe depuis 4003 en `0x4774fd`) :
   `0x477503 : cmp WORD PTR [edi+0x15],0x20` → si **et seulement si** ce champ de 16 bits vaut
   `0x20`, `push 0x2415` (`0x47750e`) puis affichage par le même chemin que les autres notices
   (`0x4776d2`). Aucune autre valeur ne produit quoi que ce soit pour 4003.
5. `0x2415` = **9237** dans `db_string.rdb` (lecture statique du fichier, §8) :
   **« You cannot create the room because you have used all of your entries for the day. »**

Donc : **la seule réaction client identifiée à une réponse à 4003 est un refus « quota journalier
épuisé »**, porté par un champ de 16 bits valant `0x20` dans le message MSG_RESULT, et le texte
affiché est celui de la chaîne 9237. Le champ de 16 bits voisin (`[edi+0x13]`) est l'id de la
requête : c'est ce que montre la cascade ci-dessus, dont dix valeurs au moins (5, 204, 253, 259,
260, 280, 400, 402, 410, 700) se lisent exactement comme des ids de `op_codes.md`.

Ce que **4003 ne déclenche pas** :

- aucune fenêtre de lobby n'est ouverte par 4003 lui-même : l'entrée dans le lobby HuntaHolic est
  nourrie par `4001` (`TM_SC_HUNTAHOLIC_INSTANCE_LIST`) / `4002` (`TM_SC_HUNTAHOLIC_INSTANCE_INFO`),
  hors périmètre de cette carte ;
- aucun paquet serveur → client propre à 4003 n'existe : ni rzu ni NGemity n'en déclarent (§5.1), et
  le client ne compare `0xfa3` que dans les deux handlers de réception décrits ici.

**Le socle §7j** (`docs/packet-specs/socle-instances-jeu.md:584-593`) citait
`0x47cbce`/`0x47cbd3` et `0x47cc31`/`0x47cc36` comme indices non concluants, dans un « handler
d'Act de fenêtre ». Précision apportée par cette fiche : ces quatre VA appartiennent à **un autre**
handler, `0x47ca70`, atteint par le même dispatcher (appel en `0x47fea2`, étiquette `push 0xa1d3e0`
= `"case MSG_SKILL_EVENT…"`). Là, `mov ecx,0xfa3` + `cmp cx,WORD PTR [esi+0x13]` (`0x47cbce`,
`0x47cc31`) ne lèvent **aucune notice** : ils se contentent de positionner un octet d'état de
fenêtre (`mov BYTE PTR [edi+0x150],0x1` en `0x47cbec`, `…,0x0` en `0x47cc47`). Ils confirment que
« 4003 » est bien une valeur que le client compare, mais la **notice** — donc le champ de code —
vit dans le handler `MSG_RESULT` du point 4 ci-dessus. La lecture (2) du socle (« l'octet
`[edi+0x150]` est-il un drapeau en attente de réponse ? ») reste ouverte : son **déclencheur** est
désormais connu (le champ id = 4003 dans un message vu par ce handler), sa **signification** non.

### 5.3 Ce que le serveur doit faire dans ce lot

Le socle §5.4 (`:479`) fixe le critère propre de S4 : « chaînes bornées à 30 et 16 caractères +
NUL ; refus propre si `Length` ≠ attendu ». Cette fiche ne va **pas** au-delà :

| Point | Règle | Source |
|---|---|---|
| Lecture | `TryRead…` retournant `bool`, testé sur des `byte[]` construits à la main, comme le lot S1 | socle `:487-497` |
| Longueur | **56** exigés ; toute autre `Length` (7, 55, 57, …) → refus et journal, **sans réponse** | socle `:479`, `:499-510` |
| Champs | `name` = `char[31]` à 7 (NUL **dans** le champ), `max_member_count` = un octet à 38, `password` = `char[17]` à 39 | §3.1 |
| Réponse | **aucune réponse inventée** : la forme exacte de la trame de refus n'est pas établie (§7, points 1 à 3). Le socle concluait déjà « S4 doit être livré sans réponse si la question reste ouverte » (`:592-593`) ; cette fiche **lève la moitié de la question** (le client réagit bien à un refus porté par un champ de 16 bits valant `0x20`) mais **pas l'autre** (la forme sur le fil de cette réponse) | §5.2, §7 |
| Journalisation | tracer l'id, la `Length` et le contenu des trois champs (nom, effectif, présence d'un mot de passe) sans les écrire en base | style du lot S1 (`GameClient.cs:844-845`) |
| État | **aucun** état de lobby, aucune instance, aucune table : constat du socle §6 (le socle livré est un socle de protocole) | socle `:487`, `:643-645` |

Dépendance à **nommer** (constat recevable, pas un échec) : le socle §5.4 ordonne **S2 → S4**
(« créer suppose lister », `:481-482`). Sans `4000`/`4001`/`4002` (lot S2), un 4003 reçu ne peut
produire **aucun effet observable côté client** — la seule sortie client identifiée est le refus par
quota (§5.2), et l'apparition de la salle dans la liste viendra de 4001. Si le lot S4 est livré
avant S2, il se limite donc à *lire, valider, journaliser* ; c'est son périmètre maximal honnête.

### 5.4 Points d'implémentation

- **Enum et dispatch modifiés ensemble** (critère transversal n° 4) : `TM_CS_HUNTAHOLIC_CREATE_INSTANCE = 4003`
  dans `Game/Network/Packets/Enums/GamePackets.cs`, routé dans la boucle d'`if` **avant** le `switch`
  final de `GameClient.OnDataReceived` (`Game/Network/Clients/GameClient.cs:555-790`, piège
  `_ => throw new Exception("Unknown Packet Type")` en `:791-803`, source socle `:487-497`).
- **Classe de paquet** : suivre `Game/Network/Packets/Game/GameInstanceGamePackets.cs` (lot S1) —
  constante de taille `HeaderSize + 49`, `record struct` de lecture, `TryRead…`.
- **Tests** : au moins 366 tests au total, plus le test d'offsets §3.4.

### 5.5 Zones de recouvrement à nommer (collisions)

`Game/Network/Packets/Enums/GamePackets.cs` et `Game/Network/Clients/GameClient.cs` sont touchés par
**toutes** les branches HuntaHolic ouvertes (les cinq autres cartes de la famille 4000-4012) et par
la famille 10000/10003/10004/10005 (MR #33 et #35 ouvertes) : la zone d'insertion de l'id et la zone
d'insertion du dispatch sont des points de conflit **connus**, à signaler dans la MR plutôt qu'à
résoudre au jugé. Aucun autre fichier n'est nécessaire pour ce paquet.

---

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | Pourquoi | Source |
|---|---|---|
| NGemity déclare 4003 mais **n'implémente aucun traitement** (ni lobby, ni réponse) : la logique serveur ne peut pas être portée depuis lui | `Chihiro/src` n'a aucun code de la famille HuntaHolic ; rien dans `rzgame` non plus. Le seul arbitre utilisable est le client 7.3 | §5.1 |
| Nous **ne reprenons pas** l'idée que le champ de code vaut un `TS_RESULT` : rzu et NGemity donnent tous deux `TS_RESULT_NOT_ENOUGH_BULLET = 32` (`0x20`), nom qui ne correspond pas à la notice réellement affichée par le client (« … used all of your entries for the day ») | le champ comparé par le client n'est pas démontré comme un `TS_RESULT` ; la même table de notices compare des valeurs **hors** de l'énumération (87, 90-93 pour l'id 253, 78 pour 4250, alors que `TS_RESULT_ERROR_MAX = 0x48` = 72) | rzu `librzu/src/packets/PacketEnums.h:37` et `:76` ; NGemity `shared/Server/TS_MESSAGE.h:86` ; client `0x4771ea`-`0x477261` (253), `0x4776ab`-`0x4776c6` (4250) |
| NGemity et rzu s'accordent sur le **format** (`_(string)(name, 31)`, `int8`, `_(string)(password, 17)`) : cet accord est retenu | concordance exacte des deux `DEF` et du client (§3.2) | §3.2 |

---

## 7. `NON ÉTABLI`

1. **Forme sur le fil de la réponse à 4003** — établi : le client attend un champ de 16 bits valant
   `0x20` dans un message `MSG_RESULT` dont le champ id vaut 4003 ; **non établi** : la position de
   ce champ dans la trame reçue. Le client compare `[msg+0x13]` et `[msg+0x15]` sur un **objet
   message interne** (`ProcMsgAtStatic`), pas sur le tampon brut : le constructeur d'émission, lui,
   place `Length` en 0, `ID` en 4, le checksum en 6 et la charge utile en 7 (`0x563c20`-`0x563c4d`).
   Une base `+0x0f` pour un tampon embarqué (id en `+0x13`, charge utile en `+0x15`, checksum absent
   en mémoire) est **cohérente mais non prouvée** ; un relevé voisin donne des champs de charge utile
   en `+0x14`/`+0x18`/`+0x1c`/`+0x20` pour un autre message (handler `MSG_ENTER`, `0x47ec70`), ce qui
   interdit de conclure par simple arithmétique d'offsets.
2. **Signification de la valeur `0x20`** — trois lectures possibles, aucune prouvée : (a) un code de
   résultat de l'espace `TS_RESULT` (rzu/NGemity : `TS_RESULT_NOT_ENOUGH_BULLET`, nom incompatible
   avec la notice affichée) ; (b) un code de motif propre à la table de notices du client (la même
   table utilise des valeurs hors `TS_RESULT` pour d'autres ids) ; (c) un **compteur** comparé à un
   maximum côté client (le champ ne serait alors pas un code). Tranché : `0x20` est la valeur qui
   déclenche la notice « quota journalier épuisé » pour 4003/4004 (§5.2) — **pas** tranché : ce que
   le serveur doit y mettre.
3. **L'id de la réponse** : le champ id du message vaut 4003 pour la branche concernée ; si la
   réponse du serveur portait un autre id, rien ne serait affiché. La lecture « le serveur répond
   avec le **même id** » est la seule qui donne un sens à une table indexée par id de requête — mais
   c'est une déduction, pas une preuve.
4. **Longueurs maximales réellement imposées par le client** (30 / 16 caractères) : reprises du socle
   (`socle-instances-jeu.md:118`) et **non re-vérifiées** ici (les fenêtres n'ont pas été
   désassemblées). Ce qui est vérifié : les copies sont bornées à 31 et 17 octets.
5. **Comportement attendu en cas de succès** : aucune notice de succès identifiée pour 4003 ; on ne
   sait donc pas si une réponse est attendue du tout en cas de création réussie (l'effet visible
   passerait par 4001/4002, lot S2).
6. **Le mot de passe est-il obligatoire ?** : le client saute la copie du mot de passe quand le champ
   est vide (`0x563e90`-`0x563e93`) : le champ reste alors à zéro (memset de la trame). Aucune
   indication sur ce que le serveur doit faire d'un mot de passe vide (accepter la salle publique,
   ou refuser avec un code) n'a été trouvée. Politique de jeu : à trancher par Killian.

---

## 8. Commits et binaires épinglés

| Référence | Identifiant | Emploi |
|---|---|---|
| Navislamia, base de branche | `a1c495002c290cd1fb2e19edffeb8eff49553f46` (`master`, `origin/master`) | état du serveur au moment de la fiche ; `4003` absent de `GamePackets.cs` |
| rzu (`reference/rzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | taille, ordre des champs, gating (`X(4003, true)`), en-tête de 7 octets, `writeString`, énumération `TS_ResultCode` |
| NGemity (`reference/ngemity`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | déclaration du paquet (identiques), absence de traitement, `TS_ResultCode` |
| Client 7.3 (`reference/client73/SFrame.exe`) | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 octets) | constructeur d'émission, handler de réception, tables de sauts, chaînes `.rdata` |
| Ressource de chaînes (lecture seule) | `reference/client73/db_string.rdb` (14 294 729 octets) — enregistrement de 32 octets : `u32 id`, 20 octets réservés, `u32 keylen`, `u32 textlen`, clé NUL, texte NUL | résolution de `0x2415` → id **9237**, texte de 81 octets « You cannot create the room because you have used all of your entries for the day. » (enregistrement à l'offset 10 662 285 ; l'id 9236 porte le **même** texte, ce qui rend la lecture insensible au décalage de ±1 des noms de clés dans ce fichier) |
| Ressource HuntaHolic (lecture seule) | `reference/client73/db_huntaholicresource.rdb` (152 octets) | écartée : elle ne contient qu'une ligne, sans liste de codes (aucun usage dans cette fiche) |

Commandes de reproduction (depuis `reference/client73/`, aucune exécution de `SFrame.exe`, de Lua
ou d'un script du client) :

```bash
sha256sum SFrame.exe
objdump -d -M intel SFrame.exe > /tmp/sframe-ref.asm   # 2 256 750 lignes
strings -n 4 SFrame.exe | grep -i huntaholic           # noms de fenêtres SUIHuntaHolic*
```

La lecture de `db_string.rdb` s'est faite par un lecteur Python local jetable (`/tmp/`, non
versionné), en lecture seule : enregistrements parcourus séquentiellement à partir de la clé
`instancegame_text9238`, ids croissants vérifiés sur 20 enregistrements consécutifs.

---

## 9. Bloc prêt à coller dans `CLAUDE.md`

> ### Paquet 4003 — `TM_CS_HUNTAHOLIC_CREATE_INSTANCE` (création de salle HuntaHolic)
>
> **56 octets, `X(4003, true)` chez rzu : aucun gating de version, aucune variante.** En-tête de 7
> octets + charge utile de 49 : `name` = tampon **fixe** de 31 octets à l'offset 7 (au plus 30
> caractères, NUL **dans** le champ), `max_member_count` = `int8` à 38, `password` = tampon fixe de
> 17 octets à 39. Le client 7.3 le construit en `0x563c20` (`Length` = `0x38`, `ID` = `0xfa3`,
> checksum = somme des 6 premiers octets mod 256). Fiche : `docs/packet-specs/4003-huntaholic-create-instance.md`.
>
> **Le client réagit à une réponse, mais seulement à un refus par quota** : le cas `MSG_RESULT` du
> dispatcher `SCommandSystem::ProcMsgAtStatic` (handler `0x4770a0`, appelé en `0x47fb43`) compare
> l'id en `[msg+0x13]` — 4003 en `0x4774f8`, 4004 en `0x4774dd`, corps commun en `0x477503` — et, si
> le champ de 16 bits en `[msg+0x15]` vaut `0x20`, affiche la chaîne 9237 de `db_string.rdb` :
> « You cannot create the room because you have used all of your entries for the day. » Aucune autre
> valeur n'affiche quoi que ce soit, et **aucun succès n'a de notice** : l'entrée dans le lobby
> dépend de 4001/4002 (lot S2).
>
> **La forme sur le fil de cette réponse n'est pas établie** : le client compare un objet message
> interne, pas le tampon brut. Ne pas inventer de paquet de résultat. `TS_RESULT_NOT_ENOUGH_BULLET`
> vaut bien **32** chez rzu (`PacketEnums.h:37`) **et** NGemity (`TS_MESSAGE.h:86`), mais son nom ne
> correspond pas à la notice affichée : contradiction à trancher par Killian avant d'implémenter une
> réponse (`## A VERIFIER PAR KILLIAN`, points 1 à 3).
>
> **NGemity ne tranche rien** : 4003 y est déclaré (`shared/Server/ClientPackets.h:236`) et jamais
> traité, comme toute la famille HuntaHolic (`Chihiro/src` n'en contient aucun code), et `rzgame`
> non plus. Le client 7.3 est le seul arbitre.
>
> **Lot S4** : lecture, refus propre si `Length` ≠ 56, journalisation — sans réponse, sans état, sans
> table. `TM_CS_HUNTAHOLIC_CREATE_INSTANCE = 4003` doit être déclaré dans `GamePackets` **et** routé
> dans `GameClient.OnDataReceived` (aucun id ne doit atteindre le `throw` final). Ordre du socle :
> `S2 → S4` (créer suppose lister).

---

## A VERIFIER PAR KILLIAN

| # | Point | Vérification à faire | Pourquoi la lecture ne suffit pas |
|---|---|---|---|
| 1 | **La réponse à 4003** (§5.2, §7 points 1-3) | jouer la création d'une salle HuntaHolic **après avoir épuisé le quota journalier d'entrées**, et observer si le client affiche « You cannot create the room because you have used all of your entries for the day. » — d'abord sans réponse serveur (aucune notice attendue), puis avec une trame `4003` écho portant un `uint16` = 32 en offset 7 | l'emplacement du champ de code n'est démontrable que sur une trame réelle ; le client compare un objet message interne, pas le tampon brut (§7 point 1) |
| 2 | **Signification de `0x20`** (§7 point 2) | confirmer, sur le serveur officiel 7.3, le code de refus utilisé pour le quota épuisé, et si la création réussie reçoit une réponse | trois lectures concurrentes (§7 point 2) ; `TS_RESULT_NOT_ENOUGH_BULLET = 32` dans rzu **et** NGemity contredit la notice affichée |
| 3 | **Quota et colonne cible** | la salle se crée-t-elle sur `CharacterEntity.HuntaholicEnterCount` / `HuntaholicPoint` (Telecaster) ? Ces colonnes existent-elles dans la base cible ? | le dépôt ne contient que le schéma Arcadia ; la fiche S1 (`socle-instances-jeu.md:788-789`, `:816-819`) laisse déjà cette question ouverte |
| 4 | **Ordre S2 → S4** (§5.3) | décider si S4 (4003) est livré seul — donc inerte côté client — ou après S2 (4000/4001/4002) | dépendance posée par le socle `:481-482` ; arbitrage d'ordonnancement, pas de protocole |
| 5 | **Longueurs 30 / 16 et mot de passe vide** (§7 points 4 et 6) | tester la saisie de 31 caractères dans la fenêtre, et la validation avec un mot de passe vide | les limites viennent du socle (non re-vérifiées) ; la politique d'un mot de passe vide est un choix de jeu |
