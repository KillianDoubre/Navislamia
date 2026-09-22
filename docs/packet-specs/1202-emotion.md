# 1202 — `TM_CS_EMOTION` / `TM_SC_EMOTION`

Fiche d'archéologie de protocole, Epic 7.3. Écrite en lecture seule sur les références
(`reference/rzu`, `reference/ngemity`, `reference/client73`) — aucun Lua, aucun script client,
aucun exécutable client n'a été lancé. Le client 7.3 tranche ; rzu tranche la forme du paquet ;
NGemity ne tranche rien ici (voir §6).

Résumé des trois arbitrages demandés :

| Question | Verdict de cette fiche |
|---|---|
| 7.3 émet-il 1202 ou un `CHAT_REQUEST` de type `CHAT_EMOTION` ? | **Non tranché par lecture seule** (§7a) : les deux mécanismes existent côté client. La charge utile de 1202 est certaine, l'émetteur ne l'est pas. |
| Domaine des ids d'émotion | **Encadré** : 14 émotions existent (animations + icônes) et 11 messages dirigés sont numérotés 700…710 dans `db_string.rdb`. L'énumération 1…14 est une hypothèse, pas un fait (§7b). |
| À qui le serveur envoie-t-il 1201 ? | **Non établi** ; Navislamia n'a aucune visibilité joueur↔joueur. Recommandation : n'émettre que vers l'acteur (§5, §7c). |

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id client → serveur | **1202** | `op_codes.md:200` ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_EMOTION.h:9` |
| Nom | `TM_CS_EMOTION` | `op_codes.md:200` |
| Réponse serveur → client | **1201**, `TM_SC_EMOTION` | `op_codes.md:199` ; `reference/rzu/librzu/src/packets/GameClient/TS_SC_EMOTION.h:10` |
| Ids alternatifs | `2201` / **`2202`** à partir d'`EPIC_9_6_3` | `TS_SC_EMOTION.h:11`, `TS_CS_EMOTION.h:10` |
| Référence NGemity | `TS_SC_EMOTION = 1201`, `TS_CS_EMOTION = 1202` | `reference/ngemity/shared/Server/ClientPackets.h:207-208` |
| État dans Navislamia | **absent** : ni 1201 ni 1202 dans `GamePackets`, et aucune occurrence de « emotion » dans les sources C# | `Game/Network/Packets/Enums/GamePackets.cs` ; `grep -rni emotion --include=*.cs .` → 0 |

Le 7.3 du dépôt est antérieur à 9.6.3 : les ids sont **1202 (CS) et 1201 (SC)**, voir §4.

## 2. Ce que le joueur fait pour que le client l'envoie

Le client 7.3 possède **deux** surfaces d'émotion, et rien dans les artefacts disponibles ne
permet de dire laquelle émet 1202 :

1. **Commandes locales tapées dans le chat**, avec barre oblique. Le client contient la table des
   motifs de commandes locales, où les émotions figurent sans paramètre :
   `/makeangry`, `/rage`, `/clap`, `/dance`, `/pish`, `/cheers`, `/apology`, `/boring`, `/yes`,
   `/sadness`, `/laugh`, `/greeting` — `SFrame.exe` l. 21365-21376 (convention de citation client :
   voir §8). Ces entrées voisinent `/rp_gjoin %d %d`, `/pjoin %d`, `/gjoin %d %d`
   (l. 21349-21357) : c'est bien la table des motifs de commandes locales, et **aucune commande
   d'émotion ne prend d'argument** — la cible n'est donc jamais transmise par la commande.
2. **Panneau « Emotion » à 14 icônes** : `icon_emotion_0014` … `icon_emotion_0001`
   (`SFrame.exe` l. 20321-20334) ; le libellé `Emotion` (`db_string.rdb` id 6034 =
   `ui_text_6034`, `Arcadia.sql:53531`) appartient au même groupe que `Basic`, `Community` et
   `Creature` (ids 6033-6036) — une barre d'onglets.

Le lexique client comporte en outre 13 commandes d'émotion sans barre oblique
(`db_localcommand.rdb`, 2 218 octets, fichier texte `<alias coréen> <commande canonique>`) :

| Commande | Premier octet de la ligne |
|---|---|
| `apology` | 1523 |
| `boring` | 1593 |
| `greeting` | 1662 |
| `cheers` | 1696 |
| `clap` | 1748 |
| `dance` | 1770 |
| `laugh` | 1808 |
| `hi` | 1868 |
| `no` | 1906 |
| `pish` | 1944 |
| `makeangry` | 1977 |
| `sadness` | 2059 |
| `yes` | 2115 |

Le même lexique apparaît dans SFrame.exe, en clair et sans barre oblique, l. 19854-19863
(`sadness`, `makeangry`, `pish`, `laugh`, `dance`, `clap`, `cheers`, `greeting`, `boring`,
`apology`). Les 14 animations existent bien côté client :

- 14 clips `*_emote_*_biped` par race/sexe (`db_charactermotion.rdb`, ex. `def_emote_angry_biped`
  → `def_emote_yes_biped`, 14 enregistrements contigus) ;
- 14 entrées `ANI_*` d'émotion dans `db_motionset.rdb` (enregistrements 34-44 puis 53-55 d'une
  table à pas de 452 octets : `ANI_BOW`, `ANI_CHEER`, `ANI_CLAP`, `ANI_DANCE`, `ANI_HAPPY`,
  `ANI_HI`, `ANI_NO`, `ANI_POUT`, `ANI_PROVOKE`, `ANI_SORROW`, `ANI_YES`, puis `ANI_ANGRY`,
  `ANI_APOLOGIZE`, `ANI_BORING`).

**Réserve** : ni l'ordre ni le numéro d'émotion ne découle de ces listes (les deux fichiers
ressortent triés ou regroupés par l'outil d'export `Archemedes v0.1.0`). Voir §7b.

## 3. Structure sur le fil

En-tête Navislamia : 7 octets — `Length` (uint32, 0), `ID` (uint16, 4), `Checksum` (octet, 6)
(`Game/Network/Packets/Header.cs:6-11` ; `Game/Network/Packets/Game/GameChatPackets.cs:10`
`HeaderSize = 7`). `Checksum` = somme des 6 premiers octets
(`GameChatPackets.cs:55-60`, `GameMovePackets.cs:49-56`).

### 3.1 `TM_CS_EMOTION` (1202) — client → serveur — **11 octets**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0 | uint32 | `Length` | 11 | `Header.cs:9` ; total = 7 + 4 |
| 4 | uint16 | `ID` | 1202 (`0x04B2`) | `op_codes.md:200` ; `TS_CS_EMOTION.h:9` |
| 6 | uint8 | `Checksum` | somme des octets 0-5 | `Header.cs:11` ; `GameChatPackets.cs:55-60` |
| 7 | int32 | `emotion` | valeur opaque, voir §4 et §7b | `reference/rzu/librzu/src/packets/GameClient/TS_CS_EMOTION.h:6` ; `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_EMOTION.h:7` |

Taille totale attendue : **11 octets**. Un seul champ, aucune chaîne, aucun tableau : pas de
longueur variable, pas d'alignement (`CREATE_PACKET` rzu sans `_(count)`/`_(dynstring)`).

### 3.2 `TM_SC_EMOTION` (1201) — serveur → client — **15 octets**

| Offset | Type | Nom | Valeur attendue | Source |
|---|---|---|---|---|
| 0 | uint32 | `Length` | 15 | `Header.cs:9` ; 7 + 4 + 4 |
| 4 | uint16 | `ID` | 1201 (`0x04B1`) | `op_codes.md:199` ; `TS_SC_EMOTION.h:10` |
| 6 | uint8 | `Checksum` | somme des octets 0-5 | `Header.cs:11` |
| 7 | uint32 | `handle` | handle de l'auteur de l'émotion | `TS_SC_EMOTION.h:6` ; `ClientPackets.h:207` + `TS_SC_EMOTION.h:7` (NGemity) |
| 11 | int32 | `emotion` | la valeur reçue en 1202, réémise telle quelle | `TS_SC_EMOTION.h:7` |

Taille totale attendue : **15 octets**. L'ordre `handle` puis `emotion` est identique dans rzu et
NGemity ; le handle est un `uint32` dans les deux (rzu : `ar_handle_t` défini comme
`strong_typedef<ar_handle_t, uint32_t>`, `librzu/src/lib/Packet/GameTypes.h:40` ; NGemity :
`uint32_t`, `TS_SC_EMOTION.h:7`), et Navislamia représente déjà ses handles en `uint32`
(`GameChatPackets.cs:23` écrit le handle à l'offset 7 ; `CharacterHandle = (uint)character.Id`,
`Game/Network/Clients/Actions/GameActions.cs:96`).

## 4. Gating de version

| Champ | Gating rzu | Décision 7.3 |
|---|---|---|
| Id de `TS_CS_EMOTION` | `X(1202, version < EPIC_9_6_3)` / `X(2202, version >= EPIC_9_6_3)` (`TS_CS_EMOTION.h:8-10`) | **1202** |
| Id de `TS_SC_EMOTION` | `X(1201, version < EPIC_9_6_3)` / `X(2201, version >= EPIC_9_6_3)` (`TS_SC_EMOTION.h:9-11`) | **1201** |
| `emotion` (int32) | aucun gating | lecture directe, offset 7 / 11 |
| `handle` (uint32) | aucun gating (`TS_SC_EMOTION.h:6` : pas de `version >=`) | 4 octets |

Justification : 7.3 = `0x070300`, `EPIC_9_6_3` = `0x090603` — la version est **inférieure**, donc la
branche basse (1201/1202) s'applique. La remontée d'id est le même motif que pour les autres
paquets remappés par 9.6.3 (`TM_CS_CHAT_REQUEST` : 20 → 1020,
`TS_CS_CHAT_REQUEST.h:52-54`), et `GamePackets` porte déjà `TM_CS_CHAT_REQUEST = 20`
(`GamePackets.cs:19`), cohérent.

NGemity ne peut pas contredire ce gating : il compile pour un seul Epic, `EPIC_4_1_1`
(`reference/ngemity/shared/Common/Define.h:25`), donc également sous 9.6.3 — ses ids fixes
1201/1202 (`ClientPackets.h:207-208`) confirment la branche basse.

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` en fait : rien

`grep -rni emotion reference/ngemity/Chihiro/` → **0 occurrence**. Il n'existe ni handler
(`WorldSession`), ni service, ni validation d'émotion. Idem côté rzu :
`grep -rn TS_CS_EMOTION --include=*.cpp --include=*.h reference/rzu` ne renvoie que le fichier de
déclaration lui-même — **aucune logique de traitement dans les deux références**. Il n'y a donc
aucun comportement à porter : la référence ne tranche ici que la forme du paquet.

### 5.2 Ce que le client 7.3 sait faire (réception de 1201)

Chemin de réception établi par lecture du binaire :

- table d'annotation des messages : `TM_SC_EMOTION` (`SFrame.exe` l. 26302) ;
- dispatch de réception : `case MSG_EMOTION` (`SFrame.exe` l. 19700), dans la même table de cas que
  `MSG_MOVE`, `MSG_HPMP`, `MSG_RESULT` (l. 19741) ;
- structure de message descendant : `.?AUSMSG_EMOTION@@` (l. 44410) ;
- travail d'animation : `.?AVSWorkEmotion@@` (l. 44574), aux côtés de `SWorkWarp`, `SWorkPickUp`,
  `SWorkMount` (l. 44575-44581) — cohérent avec une animation jouée sur le handle (lecture, pas
  preuve).

Les 11 messages dirigés sont **dans le client**, pas dans une réponse serveur :
`db_string.rdb` les stocke avec des ids consécutifs, structure d'enregistrement
`[len_key][len_text][key\0][text\0][id uint32][group uint32]` :

| Id | Clé | Texte | Octet de début (clé) |
|---|---|---|---|
| 700 | `smsq_emotion_happy` | `You are happy with "#@someone_name@#".` | 11214911 |
| 701 | `smsq_emotion_rusty` | `#@someone_name@# has infuriated you!` | 11215001 |
| 702 | `smsq_emotion_apology` | `You apologize to "#@someone_name@#".` | 11215089 |
| 703 | `smsq_emotion_boring` | `You are fed up with "#@someone_name@#".` | 11215179 |
| 704 | `smsq_emotion_cheer` | `You give "#@someone_name@#" a hearty cheer!` | 11215271 |
| 705 | `smsq_emotion_clap` | `You clap for "#@someone_name@#".` | 11215367 |
| 706 | `smsq_emotion_dance` | `You dance excitedly with "#@someone_name@#".` | 11215450 |
| 707 | `smsq_emotion_greeting` | `You greet "#@someone_name@#".` | 11215546 |
| 708 | `smsq_emotion_sadness` | `You grieve to "#@someone_name@#".` | 11215630 |
| 709 | `smsq_emotion_rude` | `You direct a rude gesture at "#@someone_name@#".` | 11215737 |
| 710 | `smsq_emotion_thank` | `You thank "#@someone_name@#".` | 11215849 |

Ces ids sont vérifiés dans les octets du fichier client (700 = `bc 02 00 00` après le texte du
happy, etc.) et concordent avec `Arcadia.sql:52714-52724`
(`('smsq_emotion_happy', 99, 700, …)`). Deux conséquences directement exploitables :

1. le texte de l'émotion est **fabriqué par le client** — le serveur n'a aucune chaîne à envoyer ;
2. les 11 messages attendent un **nom de personne** (`#@someone_name@#`) que le paquet 1202 ne
   transporte pas (§7a).

### 5.3 Ce que le serveur Navislamia doit faire

1. **Énumération et dispatch dans le même commit** (critère transversal n° 4) :
   `TM_SC_EMOTION = 1201` et `TM_CS_EMOTION = 1202` dans
   `Game/Network/Packets/Enums/GamePackets.cs` (noms et ids repris de `op_codes.md:199-200`), plus
   la branche correspondante dans `GameClient.OnDataReceived`. Sans branche, 1202 atteint le
   `switch` final et lève `Unknown Packet Type` (`GameClient.cs:670-681`).
2. **Lecture** : exiger au moins 11 octets ; lire `emotion` = `ReadInt32LittleEndian(buffer[7..11])`.
   La boucle de réception ne garantit que `Length`/`Checksum` (`GameClient.cs:468-501`), pas la
   taille métier : la garde appartient au handler (modèle : `TryReadArrangeItem`,
   `GameActionPackets.cs:31-33`, qui compare la taille attendue).
3. **Réponse** : une seule trame `TM_SC_EMOTION` de **15** octets,
   `handle` = `ConnectionInfo.CharacterHandle` (offset 7), `emotion` = la valeur reçue (offset 11),
   checksum recalculé. Le handle de l'auteur est `(uint)character.Id`
   (`GameActions.cs:96`), déjà disponible dans l'état de session.
4. **Portée** : voir §5.4 — un écho vers l'acteur est la seule émission dont on sache qu'elle ne
   référence pas un handle inconnu.
5. **Aucune interprétation de la valeur** : ni table d'émotions, ni validation d'intervalle. Ni rzu
   ni NGemity n'en valident, et le client 7.3 interprète la valeur tout seul (§7b). Une borne
   inventée casserait des émotions légitimes.
6. **Aucun `TM_SC_RESULT`** par défaut : rien n'indique que le client attende un accusé de
   réception pour 1202 (§7d). Ne pas ajouter de trame non prouvée.
7. **Tests** : taille et offsets des deux trames (11 et 15 octets ; `ID` à 4, `emotion` à 7 pour
   1202 ; `handle` à 7 et `emotion` à 11 pour 1201), checksum, et la présence de la branche de
   dispatch — dans un fichier au format du dépôt (`Tests/Game/…PacketsTests.cs`, cf.
   `Tests/Game/ChatPacketsTests.cs:13-27`).

### 5.4 Ce qui existe réellement pour diffuser (sans le présumer suffisant)

- Diffusion existante : `NetworkService.AuthorizedGameClients` (`Game/Network/NetworkService.cs:43`),
  parcourue par `MonsterMovementService` (`Game/Services/MonsterMovementService.cs:47-52`, envoi
  l. 98-118) et `MonsterAiService` (l. 70-75). Elle est **globale** : aucun filtre de carte, de
  couche ni de distance (le filtre réel est le dictionnaire de visibilité *par client* de ses PNJ,
  monstres et props, `Game/Network/Clients/ConnectionInfo.cs:47-60`).
- **Il n'existe aucune visibilité joueur↔joueur** : `TS_SC_ENTER_PLAYER` est envoyé au seul client
  qui entre (`GameActions.cs:148` et `:180`), jamais aux autres, et aucun `ConnectionInfo` ne
  mémorise les handles d'autres joueurs. Un `TM_SC_EMOTION` diffusé à tous référencerait donc, chez
  les autres clients, un handle qu'ils ne connaissent pas.
- La cible sélectionnée est en revanche connue du serveur : `TM_CS_TARGETING` (511) →
  `ConnectionInfo.TargetHandle` (`GameClient.cs:175-184`, `ConnectionInfo.cs:30`). C'est le seul
  élément serveur qui pourrait servir à une émotion *dirigée*, et c'est une piste, pas une preuve
  (§7a).

Conséquence retenue : **émettre la 1201 vers l'acteur** (écho), et ne pas diffuser aux autres
tant qu'aucune visibilité joueur n'existe. Une diffusion globale est un choix réversible à
documenter par Killian, pas une évidence à coder.

## 6. Écarts assumés avec NGemity, et pourquoi

1. **NGemity n'a pas de logique** : le seul « écart » est que nous écrivons un traitement que
   NGemity n'a jamais eu. `grep -rni emotion reference/ngemity/Chihiro/` → 0.
2. **Correction du cadrage** : « aucune occurrence de Emotion dans Chihiro » est exact pour
   `Chihiro/`, mais **faux pour le dépôt NGemity** : les définitions existent dans
   `shared/Server/Packets/GameClient/TS_CS_EMOTION.h` (6-11), `TS_SC_EMOTION.h` (6-12) et
   `shared/Server/ClientPackets.h:207-208`. Elles confirment l'ordre et la taille des champs, donc
   elles servent de contrôle croisé.
3. **Gating** : NGemity compile en `EPIC_4_1_1` (`shared/Common/Define.h:25`) et code les ids en
   dur ; il ne peut pas exprimer le remappage 9.6.3. Nous suivons rzu (`TS_*_EMOTION.h`), pas
   NGemity, pour les ids.
4. **`CHAT_EMOTION` (= 0x5)** existe des deux côtés (`TS_CS_CHAT_REQUEST.h:14`) et dans rzu il est
   consommé par la passerelle de chat comme **type de message venant du serveur de jeu**
   (`rzchatgateway/src/IrcClient.cpp:189`, atteint par `sendMsgToIRC`, l. 211-217). Ce n'est **pas**
   une raison d'acheminer l'émotion par un `TM_CS_CHAT_REQUEST` ; c'est un type d'affichage de chat
   côté serveur, à ne pas confondre avec le paquet d'émotion.
5. **`SInput*`** : le client possède une famille de commandes entrantes typées
   (`SInputMove`, `SInputAttack`, `SInputSkill`, `SInputTakeItem`, `SInputSit`, `SInputMount`,
   `SInputUseItem`, `SFrame.exe` l. 44546-44555) et **aucune classe d'émotion**. Aucune référence
   n'en parle non plus ; à ne pas inventer.

## 7. `NON ÉTABLI`

### (a) Le client 7.3 émet-il vraiment 1202, ou un `CHAT_REQUEST` de type `CHAT_EMOTION` ?

Faisceau réuni, et sa limite :

- pour 1202 : table d'annotation du client (`TM_CS_EMOTION`, `SFrame.exe` l. 26301), nom de
  paquet documenté par rzu et NGemity, 14 animations d'émotion et 14 icônes dans le client,
  13 commandes locales d'émotion. La charge utile (un int32) est cohérente avec une commande
  **sans argument**, et le nom `#@someone_name@#` des 11 messages serait alors résolu localement.
- **contre l'utilisation de la table d'annotation comme preuve** : elle est incomplète. Elle
  contient `TM_CS_CHAT_REQUEST` (l. 26412), `TM_CS_USE_ITEM` (l. 26376), `TM_CS_TARGETING`
  (l. 26331)… mais **omet** `TM_CS_ARRANGE_ITEM`, `TM_CS_CHANGE_ITEM_POSITION` et
  `TM_CS_MOUNT_SUMMON`, que le client envoie pourtant. Sa présence est donc un indice, pas une
  preuve (même réserve que la fiche 253).
- pour `CHAT_REQUEST` : `CHAT_EMOTION = 0x5` existe (`TS_CS_CHAT_REQUEST.h:14`) et la trame
  transporte un `szTarget` de 21 octets — ce qui expliquerait à la fois la cible et le
  `#@someone_name@#`. Rien dans les artefacts disponibles ne montre le client employant ce type
  en émission (la seule occurrence côté rzu est une réception de passerelle, §6.4).

**Question à trancher** : capture réseau (ou désassemblage du chemin d'envoi) sur une session 7.3
pendant `/dance` et pendant une émotion dirigée. Le dev ne doit **pas** implémenter la variante
chat : la partie prouvée est 1202 → 1201. Si la capture montre que la cible voyage ailleurs, elle
sera traitée dans une fiche séparée.

### (b) Domaine et ordre des valeurs d'émotion

- Établi : **14** émotions existent (14 clips `*_emote_*_biped`, 14 entrées `ANI_*`, 14 icônes
  `icon_emotion_0001..0014`) ; **11** d'entre elles ont un message dirigé numéroté **700…710**
  (§5.2), dans l'ordre `happy, rusty(=colère), apology, boring, cheer, clap, dance, greeting,
  sadness, rude, thank`.
- Hypothèse H1 (à arbitrer) : la valeur transmise va de 1 à 14, suit l'ordre des messages pour les
  11 dirigées, et vaut 12…14 pour les 3 restantes. Les 3 restantes sont nécessairement prises dans
  {`bow`, `no`, `pout`, `yes`} — l'appariement `thank` ↔ `bow` est une **déduction sémantique non
  prouvée**, et l'ordre de 12…14 n'est **pas** établi.
- Ce qui manque : l'asset d'interface qui porte la table valeur → animation (panneau « Emotion »)
  est dans les archives `data.001`…`data.008`, **absentes du VPS**
  (`reference/README.md` ; `extraction-manifest.json` ne décrit que `data.000`, l'index).
- Ce que le dev ne doit pas faire : écrire une table de correspondance, ou rejeter une valeur hors
  d'un intervalle supposé. Relayer l'int32 tel quel. Si Killian veut une borne, H1 (1…14) est la
  piste la plus étayée, et elle reste une hypothèse.

### (c) Portée de la 1201

Aucun moyen de savoir, par lecture seule, si le vrai serveur 7.3 envoie 1201 à tous les joueurs de
la zone, de la couche, ou au seul acteur : NGemity n'a pas de logique, rzu non plus, et Navislamia
n'a pas de visibilité joueur (§5.4). Recommandation : écho vers l'acteur ; la diffusion élargie
exige d'abord une visibilité joueur↔joueur, à créer dans une tâche dédiée si Killian la veut.

### (d) Le client attend-il un `TM_SC_RESULT` pour 1202 ?

Aucun élément. Le dispatch client traite bien `case MSG_RESULT` (l. 19741) et certains envois
client sont suivis d'un accusé, mais 1201 suffit à faire jouer l'animation et le message est local.
Décision par défaut : pas d'accusé. Se vérifie par la capture de (a).

### (e) Localisation du client fourni

Le client étudié n'est pas la localisation anglaise : `db_localcommand.rdb`
contient des alias coréens en EUC-KR et des alias japonais en EUC-JP. Le lexique canonique anglais
des commandes est le même d'une localisation à l'autre (c'est lui qui apparaît en second), mais
l'appariement alias → commande, lui, n'a été lu que sur ce client.

## 8. Commits et binaires épinglés

| Référence | Commit / empreinte |
|---|---|
| rzu (`reference/rzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (HEAD) |
| rzu — création de `TS_CS_EMOTION.h` / `TS_SC_EMOTION.h` | `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07) |
| rzu — `ar_handle_t` sur le handle de `TS_SC_EMOTION` | `05bc2d82dac16003bc06162584e8559826310bff` (2020-03-29, `Packets: Use strong typedef for handles and game time values`) |
| rzu — gating 9.6.3 (1201/1202 → 2201/2202) | `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11) |
| NGemity (`reference/ngemity`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (HEAD) |
| NGemity — plus ancien commit de l'historique suivi de `TS_SC_EMOTION.h` | `44b7d25dac7a1f869490dc8b6ea3255ea68b8eb4` (2018-08-26) |
| NGemity — plus ancien commit de l'historique suivi de `TS_CS_EMOTION.h` | `90a500d46acc39d21ef943a047a37d0a641f7af4` (2018-08-24) |
| Navislamia (`master`) | `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` |
| Client — `SFrame.exe` | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 o.) |
| Client — `data.000` (index d'archive) | sha256 `b88ac39a0976f49746671d413ba68af616af37e6e26095c3a109d5884510adbf` |
| Client — `db_string.rdb` | sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` |
| Client — `db_localcommand.rdb` | sha256 `4d05c593da2ee84d123afd8def6464021631185663b8d81f5fc0f6cf2a4d02f9` (2 218 o.) |
| Client — `db_charactermotion.rdb` | sha256 `a67448f028a92ffdbd409ffc778318b6606614c148893e7e859bd062fa23e595` |
| Client — `db_motionset.rdb` | sha256 `e9e193d3424d4933c60de7dfac6a13984b00053acf2fc2a085f029df28ec92fb` |
| NGemity — `Arcadia.sql` (tables `StringResource`, `CharacterMotion`) | sha256 `c05c200ae67f8cd2483a27784a976f8afc2ed6b80ff3729fb4f1734f205214fe` |

Convention de citation client : `SFrame.exe` désigne le numéro de ligne du dump
`strings -n 4 SFrame.exe` ; les `.rdb` sont cités par leur octet de début dans le fichier.

## 9. Implémentation — navis-dev

Statut : implémenté sur `hermes/packet-1202-emotion`, commit `a401fc9` (base de la fiche `975041f`).
Périmètre tenu : enum + dispatch, lecture de la 1202, écho de la 1201 vers l'acteur. Aucune table
d'émotion, aucune borne de valeur, aucun `TM_SC_RESULT`, aucune diffusion.

### 9.1 Checklist de la fiche, satisfaite point par point

- [x] `TM_SC_EMOTION = 1201`, `TM_CS_EMOTION = 1202` dans `GamePackets` **et** la branche de
      dispatch, dans le même commit (`a401fc9`).
- [x] Lecture de la 1202 (11 octets, `emotion` à 7), garde de taille, journalisation au niveau
      Debug comme les autres handlers.
- [x] Émission de la 1201 (15 octets, `handle` à 7 = `ConnectionInfo.CharacterHandle`, `emotion`
      à 11), avec checksum.
- [x] Aucune table d'émotions, aucune borne de valeur inventée, aucun `TM_SC_RESULT`.
- [x] Tests d'offsets pour les deux trames (`Tests/Game/EmotionPacketsTests.cs`), sans baisser le
      compte de tests (366 → 383).
- [x] Aucun commit sur `master` locale.

### 9.2 Fichiers livrés

| Fichier | Rôle |
| --- | --- |
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_SC_EMOTION = 1201` et `TM_CS_EMOTION = 1202`, insérés entre `TM_SC_GAME_TIME` (1101) et `TM_SC_DIALOG` (3000), donc dans la zone 12xx |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `TryReadEmotion(packet, out int emotion)` : lecture du seul champ, garde de taille |
| `Game/Network/Packets/Game/GameCharacterPackets.cs` | `BuildEmotion(handle, emotion)` : la trame de 15 octets |
| `Game/Network/Clients/GameClient.cs` | `HandleEmotion(buffer)` et le bras de dispatch sur `TM_CS_EMOTION`, dans le même commit |
| `Tests/Game/EmotionPacketsTests.cs` | offsets des deux trames, garde de taille, écho verbatim |
| `docs/packet-specs/1202-emotion.md` | cette fiche |

Le bras de dispatch reprend la chaîne existante (`if (header.ID == …) { …; continue; }`, placé avant
`TM_CS_CHAT_REQUEST`) plutôt que d'entrer dans le `switch` final : il est donc atteint avant le
`_ => throw new Exception("Unknown Packet Type")` (`GameClient.cs:681`). Enum et dispatch ont été
modifiés ensemble, comme l'exige le critère transversal n° 4 : un membre ajouté à l'enum sans bras
casserait la boucle de réception.

### 9.3 Offsets livrés et tests

- Requête : **11** = 7 (en-tête) + 4 (`emotion`, int32, @7). Aucun autre champ : ni `count`, ni
  chaîne, ni alignement.
- Réponse : **15** = 7 + 4 (`handle`, uint32, @7) + 4 (`emotion`, int32, @11), l'ordre de rzu et de
  NGemity.
- `handle` = `ConnectionInfo.CharacterHandle`, c'est-à-dire le `(uint)character.Id` posé à l'entrée
  dans le monde.
- Trame de moins de 11 octets : journal `Warning`, aucune trame émise, aucune lecture hors borne.
  La boucle de réception ne garantit que `Length`/`Checksum`, la garde métier appartient au handler,
  comme `TryReadArrangeItem` (`GameActionPackets.cs:31-33`).

Tests livrés (`EmotionPacketsTests`, 17 cas) : `EmotionIds_AreTheEpic73Ones`,
`ClientPacket_UsesTheEpic73Layout`, `TryReadEmotion_ReadsTheValueAtOffsetSeven`,
`TryReadEmotion_ReturnsTheRawValue` (0, 1, 14, 999, -1), `TryReadEmotion_RejectsAShortFrame`
(0, 7 et 10 octets), `AnswerPacket_LaysOutHandleThenEmotion`,
`AnswerPacket_EchoesTheRequestedEmotionVerbatim` (0, 1, 14, 999, -1).

### 9.4 Réponses émises

| Cas | Réponse |
| --- | --- |
| trame ≥ 11 octets | `TM_SC_EMOTION` (1201), 15 octets : `handle` = handle de session, `emotion` = valeur reçue |
| trame < 11 octets | aucune trame, un `Warning` en journal |
| toute autre valeur | rien de plus : pas de `TM_SC_RESULT`, pas d'envoi aux autres clients |

### 9.5 Ce qui n'est pas porté, et pourquoi

1. **Table émotion → animation, et borne d'intervalle** : §7b. Le domaine n'est pas établi ; le
   client 7.3 résout la valeur seul. Une borne inventée refuserait des émotions légitimes.
2. **`TM_SC_RESULT` pour 1202** : §7d. Rien n'en identifie un, et la 1201 suffit à faire jouer
   l'animation et le message local.
3. **Diffusion aux autres joueurs** : §7c et §5.4. Aucune visibilité joueur↔joueur n'existe ; une
   diffusion globale référencerait un handle inconnu des autres clients.
4. **La variante `CHAT_REQUEST` + `CHAT_EMOTION`** : §7a, non tranché. La partie prouvée
   (1202 → 1201) est la seule écrite.

### 9.6 Réserves

1. **L'émetteur n'est pas tranché** (§7a) : le code est écrit pour 1202. Si une capture réseau montre
   que la 7.3 émet un `CHAT_REQUEST` de type `CHAT_EMOTION`, ce bras 1202 restera simplement inerte
   — il ne casse rien puisque le dispatch ne lève jamais — et le sujet relèvera d'une fiche séparée.
2. **La portée réelle de la 1201 n'est pas établie** (§7c). L'écho vers l'acteur est le seul envoi
   dont on sache qu'il ne référence pas un handle inconnu ; l'élargir exige d'abord une visibilité
   joueur↔joueur, tâche distincte.
3. **Le domaine des valeurs n'est pas établi** (§7b) : ni test ni garde ne suppose 1…14. Une valeur
   hors intervalle est relayée telle quelle — c'est testé avec 0, 999 et -1.
4. **Aucun test automatique ne couvre le bras de dispatch** : `GameClient` dépend d'une socket et
   aucun test du dépôt ne l'instancie. Le lien enum ↔ dispatch est tenu par la revue, comme pour 221.
5. **Handle nul hors session** : `ConnectionInfo.CharacterHandle` vaut 0 avant l'entrée dans le
   monde, donc une 1202 reçue avant celle-ci produirait un écho à `handle = 0`. Aucun refus n'a été
   inventé pour ce cas, que le client 7.3 ne peut pas produire puisqu'il ne parle au serveur de jeu
   qu'après le login.
6. **Aucune persistance** : la 1202 ne touche à aucune écriture d'état, donc à aucune base.

### 9.7 Vérifications relevées

```
dotnet build Navislamia.sln -c Debug     → code 0, 0 erreur, 160 avertissements
dotnet test Tests/Tests.csproj           → code 0, 383 réussis / 383, 0 échec, 0 ignoré
git log --oneline origin/master..master  → (aucune ligne : aucun commit sur master locale)
```

Soit `366 + 17` tests : le compte ne baisse pas.

Baseline de la fiche, sur `master` (`6a982c81`) avant sa rédaction :
`dotnet build Navislamia.sln -c Debug` → code 0, 0 erreur, 160 avertissements ;
`dotnet test Tests/Tests.csproj` → code 0, **366** tests passés, 0 échec.

Point relevé au passage, à traiter par l'opérateur : sur `master`, `CLAUDE.md` **ne contient aucune
mention** de `docs/packet-specs` ni des fiches de paquet. Le repère annoncé (« `CLAUDE.md` pointe
déjà vers le répertoire des fiches ») n'existe donc pas dans ce fichier ; le renvoi vers
`docs/packet-specs/` reste à ajouter en même temps que le bloc §10.

## 10. Bloc prêt à coller dans `CLAUDE.md`

`CLAUDE.md` est protégé par Hermes côté worker : il est livré ici et dans la description de la MR,
à coller par l'opérateur.

```markdown
### Paquet 1202 — `TM_CS_EMOTION` (émotion)

- 7.3 = ids **1202** (CS) / **1201** (SC) : rzu remappe en 2202/2201 à partir d'`EPIC_9_6_3`
  (`TS_CS_EMOTION.h:8-10`). NGemity compile en `EPIC_4_1_1` et ne voit pas ce gating.
- Trame cliente de **11** octets : en-tête 7, `emotion` (int32) à 7. Réponse de **15** octets :
  en-tête 7, `handle` (uint32) à 7, `emotion` (int32) à 11. Aucun tableau, aucune chaîne.
- La valeur d'émotion est **opaque** : le client 7.3 la résout lui-même (14 animations `emote_*`,
  14 icônes `icon_emotion_0001..0014`, 11 messages client `smsq_emotion_*` id 700…710). Le serveur
  la réémet telle quelle — **ne jamais** écrire de table émotion → animation ni de borne
  d'intervalle : l'ordre des ids n'est pas établi (l'asset d'interface est dans les archives
  `data.001..008`, absentes).
- Le serveur répond par un simple **écho** : `handle` = `ConnectionInfo.CharacterHandle`,
  `emotion` inchangée, checksum recalculé. **Aucun `TM_SC_RESULT`** n'est identifié pour 1202, et
  aucun refus n'est inventé sur la valeur.
- La boucle de réception ne garantit que `Length`/`Checksum` : la garde de taille (11 octets) est
  dans le handler, et elle répond par un `Warning` seul.
- Portée : Navislamia n'a **aucune** visibilité joueur↔joueur (`TS_SC_ENTER_PLAYER` n'est envoyé
  qu'au client qui entre, `GameActions.cs:180`) : n'émettre que vers l'acteur tant qu'elle n'existe
  pas.
- Aucun traitement dans NGemity ni dans rzu (0 occurrence) : rien à porter. Ne pas confondre avec
  `CHAT_EMOTION` (0x5, type de chat reçu par la passerelle, `IrcClient.cpp:189`), qui n'est pas le
  véhicule de l'émotion.
- Restes ouverts (voir la fiche) : le client émet-il 1202 ou un `CHAT_REQUEST` de type
  `CHAT_EMOTION` ; la portée réelle de la 1201.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.
```
