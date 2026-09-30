# 221 — TM_CS_HIDE_EQUIP_INFO

Direction : **client → serveur**. Pendant serveur : `TM_SC_HIDE_EQUIP_INFO (222)`.
Références épinglées en §8. Toute preuve issue de `reference/client73/` vient d'une lecture statique
(`strings`, ressources déchiffrées) : aucun binaire client n'a été exécuté.

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id décimal | `221` (`0x00DD`) | `op_codes.md:70` — `[221] = "TM_CS_HIDE_EQUIP_INFO"` |
| nom | `TM_CS_HIDE_EQUIP_INFO` | `op_codes.md:70` |
| pendant serveur | `TM_SC_HIDE_EQUIP_INFO = 222` | `op_codes.md:71`, `Game/Network/Packets/Enums/GamePackets.cs:39` |
| présence dans l'enum du dépôt | **absent** — `GamePackets.cs` passe de `TM_SC_HAIR_INFO = 220` (l. 38) à `TM_SC_HIDE_EQUIP_INFO = 222` (l. 39) | `Game/Network/Packets/Enums/GamePackets.cs:38-39` |
| dispatch | aucun bras : le `switch` final lève `Unknown Packet Type` | `Game/Network/Clients/GameClient.cs:670-686` (repli `_ => throw ...` l. 681) |

Le dev doit donc ajouter le membre d'enum **et** le bras de traitement dans le même commit (critère
d'acceptation 4) : un membre d'enum sans bras atteint le `throw` de `GameClient.cs:681`.

## 2. Ce que le joueur fait pour que le client l'envoie

Le client Epic 7.3 expose le réglage dans la fenêtre **« Game Option »** (`db_string.rdb`,
clé `ui_option_text_01` = `Game Option`, boutons `ui_option_text_24` `Apply`, `ui_option_text_25`
`Confirm`, `ui_option_text_26` `Cancel`), onglet Gameplay/UI. Deux cases à cocher, exactement :

| Clé de ressource | Libellé | Source (`db_string.rdb`) |
| --- | --- | --- |
| `ui_option_text_65` | `Show Helmet` | `reference/client73/db_string.rdb` (dump `strings -n 4` l. 41730-41731) |
| `ui_option_text_68` | `Show Mantle` | `reference/client73/db_string.rdb` (dump l. 41736-41737) |

Textes d'aide associés — ils donnent la sémantique du flag :

| Clé de ressource | Texte | Source (dump `strings -n 4 db_string.rdb`) |
| --- | --- | --- |
| `ui_option_tooltip_29` | `Uncheck this option to hide the helmet wearing from others.` | l. 41877-41878 |
| `ui_option_tooltip_32` | `Uncheck this option to hide the mantle wearing from others.` | l. 41883-41884 |

Conséquences établies par ces deux phrases :

1. le réglage porte sur **le casque** et **la cape** — soit au plus deux états, ce qui correspond
   exactement au champ unique `uint32_t nHideEquipFlag` du paquet ;
2. il est **visible par les autres joueurs** (« from others ») : il ne peut donc pas rester local, le
   serveur doit le connaître et le rediffuser. C'est la raison d'être du 221 (montant) et du 222
   (descendant, avec `hPlayer`).

État local : le client persiste l'option cape dans `\rappelz_v1.opt` (clé `opt.pl.PLAY_MANTLE`,
`SFrame.exe` dump `strings -n 4` l. 25483). Aucune clé `PLAY_HELMET` n'existe dans cette build
(dump l. 25464-25539) : le stockage local du casque n'est pas établi.

**Émission du 221 : présomption forte, preuve directe NON ÉTABLIE** — voir §7.1. La chaîne
d'indices est : deux options dont l'effet est « vu des autres », un seul paquet montant portant ce
nom et un `uint32` nu, un handler descendant `222` présent dans le client (§5). Ce qui manque est
l'observation du paquet sur le fil.

## 3. Structure sur le fil

Taille totale attendue : **11 octets** (7 d'en-tête + 4 de charge utile).

| Offset | Taille | Type | Nom | Valeur observée | Source |
| --- | --- | --- | --- | --- | --- |
| 0 | 4 | `uint32` LE | `Length` | `11` | `CLAUDE.md:40` |
| 4 | 2 | `uint16` LE | `ID` | `221` (`0xDD`) | `op_codes.md:70` ; rzu `librzu/src/packets/GameClient/TS_CS_HIDE_EQUIP_INFO.h:9` |
| 6 | 1 | `uint8` | `Checksum` | somme des 6 premiers octets | `CLAUDE.md:40-41` |
| 7 | 4 | `uint32` LE | `nHideEquipFlag` | masque de 2 bits utilisés (voir §2, §7.3) | rzu `librzu/src/packets/GameClient/TS_CS_HIDE_EQUIP_INFO.h:6` ; NGemity `shared/Server/Packets/GameClient/TS_CS_HIDE_EQUIP_INFO.h:7` |

Les deux serveurs de référence s'accordent sur **un seul champ**, sans autre donnée ni compteur :
`_(simple)(uint32_t, nHideEquipFlag)`. Il n'y a donc pas d'ambiguïté de structure ; la seule
inconnue est la sémantique des bits (§7.3). Comme le champ est unique et de taille fixe, un
`[StructLayout(LayoutKind.Sequential, Pack = 1)] public struct TS_CS_HIDE_EQUIP_INFO { public uint
HideEquipFlag; }` suffit — même convention que `TS_CS_LOGIN` / `TM_CS_VERSION`
(`Game/Network/Packets/Game/TS_CS_LOGIN.cs:5-12`).

## 4. Gating de version

Décisions prises pour **Epic 7.3** :

| Champ | Gating rzu | Décision 7.3 | Source |
| --- | --- | --- | --- |
| id du paquet | `X(221, version < EPIC_9_6_3)` / `X(1221, version >= EPIC_9_6_3)` | **221** | rzu `librzu/src/packets/GameClient/TS_CS_HIDE_EQUIP_INFO.h:8-10` |
| `nHideEquipFlag` | aucune clause de version sur le champ | **présent** | rzu `.../TS_CS_HIDE_EQUIP_INFO.h:6` |
| id du 222 | `X(222, version < EPIC_9_6_3)` / `X(1222, version >= EPIC_9_6_3)` | **222** | rzu `.../TS_SC_HIDE_EQUIP_INFO.h:11-13` |

NGemity n'applique aucune clause de version à ces deux paquets : `CREATE_PACKET(TS_CS_HIDE_EQUIP_INFO,
221)` (`shared/Server/Packets/GameClient/TS_CS_HIDE_EQUIP_INFO.h:9`) et `CREATE_PACKET(..., 222)` pour
le descendant (`.../TS_SC_HIDE_EQUIP_INFO.h:10`), alors que son arbre sait gater par version ailleurs
et va jusqu'à `EPIC_9_5_2` (`shared/Server/Packets/GameClient/TS_CS_MOVE_REQUEST.h:22-24`,
`grep -rho "EPIC_[0-9_]*" shared/`). L'id est donc identique dans toutes les versions que NGemity
couvre, ce qui est cohérent avec la valeur 7.3.

Le flag lui-même existe bien en 7.3, il n'est pas une nouveauté :

- liste de personnages du lobby : `hide_equip_flag, version >= EPIC_7_1` — rzu
  `librzu/src/packets/GameClient/TS_SC_CHARACTER_LIST.h:14`, NGemity
  `shared/Server/Packets/GameClient/TS_SC_CHARACTER_LIST.h:12` ;
- entrée en jeu : `hideEquipFlag, version >= EPIC_7_1` — NGemity
  `shared/Server/Packets/GameClient/TS_SC_ENTER.h:94`, rzu `librzu/src/packets/GameClient/TS_SC_ENTER.h:119`.

Navislamia transporte déjà ce champ des deux côtés : `Game/Network/Clients/LobbyCharacterInfo.cs:17`,
`Game/Network/Packets/Game/TS_SC_ENTER_PLAYER.cs:34`,
`Game/Network/Clients/Actions/GameActions.cs:174` et `:287`.

Le paquet descendant 222 (15 octets) est déjà conforme aux deux références (`hPlayer` puis
`nHideEquipFlag`, aucun gating de champ — rzu `.../TS_SC_HIDE_EQUIP_INFO.h:7-9`, NGemity
`.../TS_SC_HIDE_EQUIP_INFO.h:7-8`) : `Game/Network/Packets/Game/GameCharacterPackets.cs:45-52`.

## 5. Traitement attendu

### 5.1 Ce que les références en font — rien

- **NGemity** : le paquet est *déclaré* mais sans aucun traitement. Preuves : déclaration
  `shared/Server/ClientPackets.h:86` (`TS_CS_HIDE_EQUIP_INFO = 221`), inclusion
  `shared/Server/XPacket.h:105`, puis aucun `HIDE_EQUIP` nulle part sous `Chihiro/src`, et la liste
  exhaustive des handlers de session n'en contient pas (`Chihiro/src/Network/GameNetwork/WorldSession.h:58-122`).
- **rzu** : idem côté serveur — `rzgame/` ne référence jamais le paquet. rzu ne fait que persister le
  champ et le renvoyer dans la liste du lobby : colonne `hide_equip_flag`
  (`rzgame/src/Database/DB_Character.h:45`, `DB_Character.cpp:55`) recopiée dans
  `LOBBY_CHARACTER_INFO` (`rzgame/src/StateHandler/LobbyHandler/LobbyHandler.cpp:78`). rzu n'émet
  jamais `TS_SC_HIDE_EQUIP_INFO`.

Il n'y a donc **aucune logique serveur à porter** : la sémantique vient du client (§2). C'est le cas
typique où la référence s'arrête à la structure.

### 5.2 Ce que le serveur Navislamia doit faire

1. `GamePackets.TM_CS_HIDE_EQUIP_INFO = 221` (`Game/Network/Packets/Enums/GamePackets.cs`, entre
   l. 38 et 39) **et** un bras de traitement avant le `switch` final (`GameClient.cs:670-686`) : lire
   l'identifiant dans la chaîne de `if` du `Receive` (comme `TM_CS_SET_PROPERTY`, `GameClient.cs:617-620`).
2. Lire `uint32` LE à l'offset 7. Taille attendue : 11 octets ; une trame plus courte doit être
   ignorée (résultat d'erreur) et non lue au-delà de la trame.
3. Persister le flag sur le personnage : `Characters.HideEquipFlag`
   (`Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:60`).
4. **Répondre 222 à l'émetteur** : `GameCharacterPackets.BuildHideEquipInfo(handle, flag)` — id 222,
   **15 octets**, `hPlayer` (`uint32`, handle du personnage) puis `nHideEquipFlag` (`uint32`) —
   fonction existante (`GameCharacterPackets.cs:45-52`), déjà utilisée à l'entrée en jeu
   (`GameActions.cs:192`). C'est le seul accusé établi : le client possède un bras de réception pour
   222 (`case MSG_HIDE_EQUIP_INFO            :`, `SFrame.exe` dump `strings -n 4` l. 19735, dans le
   dispatcher `SCommandSystem::ProcMsgAtStatic`) et Navislamia le lui envoie déjà au bootstrap.
5. **Rediffusion aux observateurs** : `hPlayer` n'a d'intérêt que si le 222 concerne aussi les autres
   créatures — le client de test rzu applique d'ailleurs le paquet à tout handle, joueur local comme
   créature tierce (`rzclientreconnect/src/ConnectionToServer.cpp:758-770`), et le texte d'aide dit
   « hide … from others ». Un personnage déjà affiché chez un autre joueur n'a pas d'autre moyen
   d'apprendre le changement (son `TS_SC_ENTER` est passé). Le dépôt n'a pas de diffusion de zone
   générique : seules les boucles de monstre diffusent (`Game/Services/MonsterAiService.cs:230`).
   Périmètre proposé pour cette tâche : **répondre à l'émetteur seul**, et tracer la rediffusion
   comme écart assumé (§6) avec la réserve en §7.4.

Résumé de la réponse attendue : **id 222, taille 15 octets, contenu `handle` + `nHideEquipFlag`**.
Aucun `TS_SC_RESULT` n'est identifié pour ce paquet (§7.5) : ne pas en envoyer.

## 6. Écarts assumés avec NGemity

| Écart | Justification |
| --- | --- |
| NGemity ne traite pas le 221 : la sémantique n'est pas portée de NGemity | le client 7.3 tranche (§2) ; NGemity s'arrête à la déclaration du paquet |
| NGemity et rzu n'émettent jamais le 222 côté serveur, Navislamia si | l'émission existait avant cette fiche et est validée par le client (`docs/character-bootstrap.md:39-40`) ; on ne la retire pas |
| NGemity écrit les id en dur (pas de gating), on garde le gating rzu | rzu tranche l'ordre des champs et le gating par version ; sa branche 7.3 donne 221/222 |
| Pas de rediffusion aux observateurs dans le périmètre | la structure `hPlayer` et le libellé client l'appellent (§5.2-5), mais aucun chemin client n'est prouvé pour une créature déjà affichée (§7.4) : réserve plutôt que comportement inventé. À documenter dans `CLAUDE.md` par le dev. |
| Le client de test rzu expose le flag comme propriété entière `hide_equip_flag` | convention des clients de test rzu (Epic 9.x) ; Navislamia et NGemity portent le flag comme champ direct de `TS_SC_ENTER` (`TS_SC_ENTER_PLAYER.cs:34`) — ne rien ajouter au bootstrap 7.3 |

## 7. NON ÉTABLI

1. **Preuve que le client Epic 7.3 émet le 221 — absente.** Le nom figure dans la table
   d'annotation du client (paires `-%s[%d]` + nom, `SFrame.exe` dump l. 26171-26429, entrée
   `TM_CS_HIDE_EQUIP_INFO` l. 26381), mais cette table est **partielle et non fiable comme liste
   d'émission** : elle omet des paquets montants que ce client envoie à coup sûr —
   `TM_CS_ARRANGE_ITEM`, `TM_CS_CHANGE_ITEM_POSITION`, `TM_CS_LOGOUT`, `TM_CS_REQUEST_RETURN_LOBBY`,
   `TM_CS_ACCOUNT_WITH_AUTH` (comparaison de la table du binaire avec `op_codes.md`, 68 noms CS dans
   le binaire contre 112 dans `op_codes.md`). La présence du nom ne prouve donc pas l'émission.
   Ce qui l'établirait : une capture réseau décodée d'une session 7.3 pendant un basculement des
   cases *Show Helmet* / *Show Mantle*, ou un désassemblage du chemin d'envoi dans `SFrame.exe`.
   Impossible ici : le client n'est pas exécutable sur ce VPS (les archives `data.001`–`data.008`
   sont absentes, et l'exécution de `SFrame.exe`/Lua est interdite).
2. **Moment exact de l'émission** : au clic sur la case, sur `Apply`, sur `Confirm`, ou à la
   fermeture de la fenêtre. Non établi (la fenêtre possède les quatre boutons, `db_string.rdb`
   `ui_option_text_23-26`).
3. **Correspondance bit ↔ partie.** Le flag est un `uint32` dont seuls deux états sont visibles dans
   cette build (casque, cape). L'ordre (`bit0` = casque / `bit1` = cape, ou l'inverse) n'est établi
   nulle part : ni rzu ni NGemity ne nomment les bits, et le client ne laisse aucun nom de bit dans
   ses chaînes. Les 30 autres bits sont inconnus. `0` = tout visible est *inféré* (libellés
   « Uncheck … to hide » + défaut de colonne), pas prouvé. Ce qui l'établirait : trace de session, ou
   envoi manuel des valeurs `1` puis `2` et observation du rendu du casque/de la cape.
4. **Destinataires du 222** : émetteur seul, ou aussi les joueurs qui voient déjà le personnage. La
   structure (`hPlayer`) et le libellé client désignent les observateurs, mais le comportement du
   client 7.3 pour un handle déjà à l'écran n'est prouvé par aucun élément lu.
5. **Accusé** : aucun `TS_SC_RESULT` n'a été identifié en réponse au 221 ; à ne pas envoyer (en
   revanche `TM_CS_HIDE_EQUIP_INFO` figure dans la table d'annotation du client, donc l'id est connu
   du binaire — rien de plus).
6. **Valeur de flag inconnue du client** (bits > 2) : réaction inconnue. Non bloquant : le client
   n'enverra que ce qu'il affiche.

## 8. Commits et binaires épinglés

| Référence | Version | Usage dans cette fiche |
| --- | --- | --- |
| Navislamia (`KillianDoubre/Navislamia`) | `master` = `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` ; branche `hermes/packet-221-hide-equip-info` | état du dépôt, §1, §5 |
| rzu (`glandu2/rzu`) | HEAD `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02) | structure, ids (§3, §4) |
| rzu — `TS_CS_HIDE_EQUIP_INFO.h` | introduit par `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2020-08-28, « add all known GS packets as of 9.4 »), id versionné par `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-28) | §3, §4 |
| rzu — `TS_SC_HIDE_EQUIP_INFO.h` | `hPlayer` présent dès `d7c58ee…` ; mention « Last tested: EPIC_9_8_1 » ajoutée par `3b31db1e5aea84565926f6910e2437646c8a3c51` (2023-10-01) | §3, §4 |
| NGemity (`NGemity/RZEmulator`) | HEAD `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03) | structure, absence de handler (§4, §5) |
| client de référence | `reference/client73/SFrame.exe` sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | §2, §5 |
| client de référence | `reference/client73/db_string.rdb` sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` | §2 |

Les numéros de ligne cités pour les binaires sont ceux du dump `strings -n 4 <fichier>` (reproductible
localement) : un binaire n'a pas de lignes, la clé de ressource `db_string.rdb` et le sha256 sont les
identifiants stables.

## Note de livraison

Le dépôt ignorait `/docs/*` : `docs/packet-specs/` a été ajouté aux exceptions de `.gitignore`
(`!/docs/packet-specs/`, à côté des trois fiches déjà suivies) pour que cette fiche et les suivantes
soient versionnables. Aucun fichier de code n'est touché par cette fiche.
Baseline relevée sur la branche avant la fiche : `dotnet build Navislamia.sln -c Debug` code 0,
`dotnet test Tests/Tests.csproj` code 0, **366 tests** réussis sur 366.
