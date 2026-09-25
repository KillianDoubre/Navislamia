# Socle « cycle de quête » — 604 `TM_CS_QUEST_INFO`, 605 `TM_CS_END_QUEST`, catalogue et déclencheur

Cette fiche complète `docs/packet-specs/socle-quetes.md` (socle (a) : état 600/601 + retrait 603).
Elle traite les trois questions que ce socle avait explicitement parquées : **l'acceptation**,
**la progression** et **la fin** d'une quête, c'est-à-dire les deux trames montantes `604` et `605`,
le **catalogue** dans lequel les définitions de quête vivent, et le **déclencheur** qui fait ouvrir
une fenêtre de quête au client.

Rappel des décisions de (a) que cette fiche ne renverse pas :

| point laissé ouvert par (a) | où | traitement ici |
| --- | --- | --- |
| geste exact de 604 et 605 | `socle-quetes.md` §8.4 | **tranché** — §2, §3 |
| réponse descendante à 604 | §5.6, §8.5 | **tranchée** — §5.3 |
| catalogue de quêtes (définitions, colonnes, chargement) | §5.6, §8.10 | **décrit** — §5.1 |
| déclencheur de fenêtre de quête par contact PNJ | §7 question 4 | **décrit**, exécution hors socle — §5.2 |
| portée de (b) : quel sous-ensemble est purement structurel | §7 question 2 | **tranchée** — §5.5 |

Le client Epic 7.3 tranche ; rzu tranche les tailles, l'ordre des champs et le gating ; NGemity
tranche la logique, sous réserve de sa version (`EPIC_4_1_1`, `shared/Common/Define.h:25`).

## 1. Identité

| id | nom (`op_codes.md`) | sens | source |
| --- | --- | --- | --- |
| **604** | `TM_CS_QUEST_INFO` | interrogation d'une quête précise (client → serveur) | `op_codes.md:159` ; structure `reference/rzu/librzu/src/packets/GameClient/TS_CS_QUEST_INFO.h:7-15` ; NGemity `shared/Server/Packets/GameClient/TS_CS_QUEST_INFO.h` |
| **605** | `TM_CS_END_QUEST` | fin de quête, avec choix de la récompense optionnelle (client → serveur) | `op_codes.md:160` ; `TS_CS_END_QUEST.h:5-14` ; NGemity `TS_CS_END_QUEST.h` |
| 602 | `TM_SC_QUEST_INFOMATION` (graphie de rzu) / `TM_SC_QUEST_INFORMATION` (graphie d'`op_codes.md`) | candidate de réponse descendante à 604 — **écartée**, §5.3 | `op_codes.md:157` ; `TS_SC_QUEST_INFOMATION.h:12-22` |
| 600 / 601 | `TM_SC_QUEST_LIST` / `TM_SC_QUEST_STATUS` | état déjà livré par (a), réutilisé tel quel | `socle-quetes.md` §3.4-3.5 |
| 3000 / 3001 | `TM_SC_DIALOG` / `TM_CS_DIALOG` | **transport du déclencheur** : c'est par là que le serveur arme la fenêtre de quête | `op_codes.md:220-221` ; `docs/npc-dialogs.md:30-53` |

Deux identifiants supplémentaires, **internes au client** et sans rapport avec un opcode, sont utilisés
au §2 : `165` (`0xa5`) désigne le geste qui produit `604`, `180` (`0xb4`) celui qui produit `605`
(§2.3). Ils ne se confondent pas avec les ids de trame.

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.0 La chaîne d'émission côté client, établie par lecture

Les deux trames sont construites et envoyées par le **gestionnaire de messages du jeu**
(`0x0049cd00`), qui dispatche sur `[message+4]` :

- `eax = [edi+4]` ; si `eax > 0x400`, autre table ; si `eax == 0x400`, sortie ; sinon
  `eax -= 4`, borne `0xb0`, puis `movzx eax, BYTE PTR [eax+0x49e8b4]`
  (table de 177 octets) et `jmp DWORD PTR [eax*4+0x49e77c]` (table de 78 entrées)
  — `0x0049cd3c`-`0x0049cd65` ;
- les deux cas qui nous concernent : **index 72 → `0x0049d468`** et **index 76 → `0x0049d656`** ;
- l'inversion de la table d'octets (`0x49e8b4`) donne le message interne : **index 72 ↔ id 165**
  (`0xa5`), **index 76 ↔ id 180** (`0xb4`).

Le corps des deux cas a été lu, il confirme les structures du §3 :

| cas | code | ce qu'il fait |
| --- | --- | --- |
| 72 (id 165) | `0x0049d468`-`0x0049d494` | `call 0x0048d1b0` (constructeur 604 : id `0x25c`, longueur `0xb`), puis `mov [ebp-0x11], [edi+0x13]` — **le `code` de quête est recopié du message dans la trame**, à `frame+7` —, puis envoi immédiat par la session (`call [edx+0xc4]`) |
| 76 (id 180) | `0x0049d656`-`0x0049d65e` | `call 0x0048d6c0` (constructeur 605 : id `0x25d`, longueur `0xc`, `frame+7` = `[edi+0x13]`, `frame+11` = `[edi+0x17]`, envoi immédiat) |

Les deux constructeurs ont **un seul appelant** chacun (`xref` sur les rel32 de `.text`) : respectivement
`0x0049d46b` et `0x0049d659`. Il n'existe donc pas d'autre chemin d'émission.

### 2.1 Le geste de 604 — « demander l'info d'une quête de la liste »

Le message interne 165 est produit par un constructeur dédié, `0x00587150` :

```
0x00587150  mov [eax+0x4],0xa5          ; id de message 165
0x0058716b  mov [eax+0xb],0x13          ; forme du message
0x00587178  mov [eax+0x13],ecx          ; paramètre = [ebp+8]
```

Ce constructeur a **deux appelants**, tous deux dans `SUIQuestListWnd` (la fenêtre de liste des
quêtes ; les chaînes de la classe sont voisines : `set_quest_code` `0x00a2f508`, `quest_name_%02d`
`0x00a2f64c`, `SUIQuestListWnd::IMSG_UI_SEND_DATA` `0x00a2f5c0`) : `0x005890a9` et `0x005891a4`.

Le second est le plus explicite et c'est celui qui donne le geste :

```
0x0058913e  push 0x00a2f5ac             ; "quest_info_button"
0x00589144  call 0x00977ff7             ; comparaison du bouton cliqué
0x0058915f  je   0x005891ae             ; autre bouton → bouton suivant
0x00589161  cmp  DWORD PTR [esi+0x4d0],0x0
0x00589168  jl   0x00589437             ; aucune quête sélectionnée → rien
0x0058918c  mov  eax,[esi+0x4d0]       ; la quête sélectionnée
0x00589192  push 0x0
0x00589194  push eax
0x00589195  call 0x004c1cb0             ; résolution de la quête
0x0058919c  call 0x004c1150             ; -> identifiant de quête
0x005891a4  call 0x00587150             ; message 165
```

**Ce que le joueur fait : il sélectionne une quête dans la liste (`SUIQuestListWnd`, index de
sélection `[esi+0x4d0]`, garde « pas de sélection négative ») puis clique le bouton
`quest_info_button`.** Le client envoie alors 604 avec le `code` de cette quête. Le libellé du bouton
est un élément d'interface nommé dans les données du client (`data.000`, paquet chiffré — voir §7),
donc la position exacte du bouton à l'écran n'est pas lisible ; l'action, elle, l'est.

### 2.2 Le geste de 605 — « terminer la quête par le bouton de confirmation », avec ou sans récompense optionnelle

Le message interne 180 est produit par le constructeur `0x0061ac00` :

```
0x0061ac00  mov dl,[ebp+0xc]            ; 2e argument = index de récompense (octet)
0x0061ac17  mov [eax+0x4],0xb4          ; id de message 180
0x0061ac2b  mov [eax+0x13],ecx          ; 1er argument = [ebp+8] -> code de quête
0x0061ac2e  mov [eax+0x17],dl           ; index de récompense
```

Il est appelé depuis `0x0061bb51`. Le même identifiant est par ailleurs écrit **en ligne** à
`0x0061ae8d`, dans une fenêtre de donjon (chaînes voisines : `dungeon_target_progress`
`0x00a41a80`, `button_reduction` `0x00a41a3c`, `button_ok` `0x00a21b9c`), avec
`mov [eax+0x17],0xff` à `0x0061aeae` : **`0xff` est `-1`, la valeur « aucune récompense optionnelle
choisie »**, ce qui confirme d'un deuxième niveau la signature `int8_t` de `nOptionalReward` relevée
par rzu (`TS_CS_END_QUEST.h:7`) et déjà prouvée par le client en `socle-quetes.md` §3.3.

Deux fenêtres écrivent par ailleurs **le texte du déclencheur** `end_quest( %d, %d )`
(`VA 0x00a2f150`) pour ce même geste, ce qui relie le paquet au parcours utilisateur :

| site | fenêtre | source |
| --- | --- | --- |
| `0x00585367` | fenêtre de dialogue de quête (`quest_dialog` `0x00a2ed3c`, `vscroll_quest_story` `0x00a2eb80`, `button_close` `0x00a21bb8`) ; le site est précédé de `ui_click_complete01.wav` `0x0058533e` | `0x00585330`-`0x00585374` |
| `0x0058a9cc` | `SUIQuestRewardWnd` (chaîne `SUIQuestRewardWnd::ProcMsgAtStatic` `0x00a2f758` en `0x0058aa88`, `quest_selectitem_*` `0x00a2ed68`) | `0x0058a9a3`-`0x0058a9cb` |

Le format est rempli avec `[esi+0x4c0]` (code) et `[esi+0x504]` (index de récompense)
(`0x00585355`-`0x0058536c`).

### 2.3 Le geste du déclencheur (chemin 3000/3001)

Indépendamment des paquets ci-dessus, la **fenêtre de dialogue PNJ** (`SUINPCDialogWnd`) traite les
déclencheurs reçus du serveur : elle compare la chaîne de commande à `start_quest` (`0x00a2e660`,
comparaison en `0x0057d14d`) et à `end_quest` (`0x00a2e63c`, `0x0057d174`) — deux symboles voisins de
`open_market(` (`0x00a2e684`) et `open_storage()` (`0x00a2e694`), c'est-à-dire du même jeu de
commandes de dialogue. Un déclencheur non reconnu n'est pas exécuté localement : le sélecteur est
renvoyé au serveur par `TM_CS_DIALOG` (3001), dont le format est déjà décrit par
`docs/npc-dialogs.md:52-53`.

Côté serveur, NGemity **fabrique ces déclencheurs** dans `Messages::SendQuestInformation`
(`Chihiro/src/Network/Messages.cpp:622-720`), seule fonction de dialogue de quête de la référence :

| élément | valeur produite | source |
| --- | --- | --- |
| titre du dialogue | **littéral** `"Guide Arocel"` — valeur de la référence, **non dérivée du PNJ** — et `type` = 3 / 7 / 8 selon la progression | `Messages.cpp:672-675` (titre), `627-641` (type) |
| **texte** du dialogue | `QUEST\|{code}\|{textID}` | `Messages.cpp:676` |
| menu, quête terminable, récompenses optionnelles | un bouton par récompense avec déclencheur `end_quest( {code}, {i} )`, libellé `NULL`, puis libellé `REWARD` de déclencheur vide | `Messages.cpp:682-704` |
| menu, quête terminable sans récompense optionnelle | déclencheur `end_quest( {code}, -1 )` | `Messages.cpp:699` |
| menu, quête démarrable | libellé `START`, déclencheur `start_quest( {code}, {textID} )`, puis libellé `REJECT` de déclencheur vide | `Messages.cpp:713-716` |

Le **titre** est donc, chez NGemity, une chaîne constante (`"Guide Arocel"`, `Messages.cpp:672`) : la
référence ne dit pas d'où un serveur 7.3 tire ce titre. Seuls le préfixe du **texte** et la grammaire du
menu sont structurels ; le titre à émettre reste NON ÉTABLI (§7.9).

Le menu part tel quel dans `TS_SC_DIALOG` sous la forme `\t<libellé>\t<déclencheur>\t`
(`Player::AddDialogMenu`, `Chihiro/src/Entities/Player/Player.cpp:1098-1109` ; `Player::ShowDialog`,
`:1111-1120`).

Le **client 7.3 connaît ce format** : un handler de la région des trames descendantes (`0x0067ce40`,
la même région que le handler de 600 à `0x00670cd0` et de 601 à `0x0067db20`) lit les deux longueurs
`[esi+0x0f]` et `[esi+0x11]` (les longueurs `title` et `text` de 3000 selon
`docs/npc-dialogs.md:41-42`), assemble la chaîne puis :

```
0x0067ce88  push 0x6
0x0067ce8a  push 0x00a53dbc              ; "QUEST|"
0x0067ce90  call 0x0097a638              ; strncmp
0x0067ce9a  jne  0x0067cfd8              ; pas un dialogue de quête
0x0067cec2  push 0x00a1fce0              ; "|"
0x0067cecc  call 0x0067a050              ; découpage
0x0067ceea  mov  [edi+0x1b], eax         ; 1er entier = ...
0x0067cf08  mov  [edi+0x1f], eax         ; 2e entier = ...
```

et la chaîne `REJECT` (`VA 0x00a53d90`, voisine de `QUEST|`) est référencée depuis la même région
(`0x0067d5fa`, comparaison avec la longueur de libellé en `0x0067d60c`). Autrement dit : le client 7.3 **reconnaît `QUEST|<entier>|<entier>` en tête du
dialogue et y attache la présentation de quête**, et il connaît au moins un des libellés de menu que
NGemity produit. C'est ce qui fait de 3000/3001 le transport du déclencheur, et non un paquet dédié.

## 3. Structure sur le fil

### 3.0 En-tête commun (7 octets)

| offset | type | nom | source |
| --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` (total, en-tête comprise) | dépôt `Game/Network/Packets/Header.cs:9,22` ; client : `0x0048d1d9`/`0x0048d1e4` (604), `0x0048d6d1`/`0x0048d6da` (605) |
| 4 | `uint16` LE | `ID` | `Header.cs:10,23` ; client : mot écrit en `0x0048d1d9` (`0x25c`) et `0x0048d6d1` (`0x25d`) |
| 6 | `uint8` | `Checksum` = somme des octets 0..5 | `Header.cs:11,24` ; client : boucle `0x0048d1f0`-`0x0048d1f7` / `0x0048d6e7`-`0x0048d6fa` |

### 3.1 `TM_CS_QUEST_INFO` = 604 — **11 octets**

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | **11** (`0xb`) | rzu `TS_CS_QUEST_INFO.h:8` ; client `0x0048d1e4` |
| 4 | `uint16` LE | `ID` | **604** (`0x25c`) | `op_codes.md:159` ; client `0x0048d1d9` |
| 6 | `uint8` | `Checksum` | — | client `0x0048d1f0`-`0x0048d1f7` |
| 7 | `int32` LE | `code` | quête sélectionnée dans `SUIQuestListWnd` | rzu `TS_CS_QUEST_INFO.h:8` ; NGemity `TS_CS_QUEST_INFO.h` ; client : constructeur `0x0048d1b0` + recopie `0x0049d476` → `frame+7` |

Taille : **7 + 4 = 11 octets**. Aucun autre champ, aucun remplissage : le constructeur
n'initialise que les 11 octets qu'il écrit et le bloc appelant ne touche que `frame+7`
(`socle-quetes.md` §3.2 pour la preuve croisée d'absence de champ).

### 3.2 `TM_CS_END_QUEST` = 605 — **12 octets**

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | **12** (`0xc`) | rzu `TS_CS_END_QUEST.h:6-7` ; client `0x0048d6da` |
| 4 | `uint16` LE | `ID` | **605** (`0x25d`) | `op_codes.md:160` ; client `0x0048d6d1` |
| 6 | `uint8` | `Checksum` | — | client `0x0048d6e7`-`0x0048d6fa` |
| 7 | `int32` LE | `code` | quête terminée | rzu `:6` ; NGemity `:7` ; client `0x0048d6fd` → `frame+7` |
| 11 | **`int8`** | `nOptionalReward` | index de récompense, **`-1`** quand aucune n'est choisie | rzu `:7` ; NGemity `:8` ; client `0x0048d700` → `frame+11`, valeur `0xff` écrite en `0x0061aeae` |

Taille : **7 + 4 + 1 = 12 octets**.
`nOptionalReward` est **signé** : `-1` est une valeur émise par le client 7.3 lui-même
(`mov BYTE PTR [eax+0x17],0xff`, `0x0061aeae`), donc le serveur doit la lire comme un `sbyte` et non
comme un `255` à indexer dans un tableau. C'est le pendant exact de la réserve de (a) §3.3 sur la
borne : la borne est toujours NON ÉTABLIE (§7), mais le **domaine** l'est (`-1` = aucun choix).

### 3.3 `TM_SC_QUEST_INFOMATION` = 602 — 17 octets, **écartée** (§5.3)

| offset | type | nom | source |
| --- | --- | --- | --- |
| 7 | `int32` LE | `code` | rzu `TS_SC_QUEST_INFOMATION.h:14` |
| 11 | `int32` LE | `nProgress` (`TS_QUEST_PROGRESS`) | rzu `:5-10,15` |
| 15 | `uint16` LE | `trigger_length` | rzu `:16` |

Taille déclarée : **7 + 4 + 4 + 2 = 17 octets**. Les deux réserves de (a) §3.6 tiennent : rzu note
« Seems unused » (`:12`) et le `trigger_length` n'a aucun tableau derrière.

### 3.4 Le transport du déclencheur — 3000 puis 3001

| trame | taille | source |
| --- | --- | --- |
| `TM_SC_DIALOG` (3000) | `21 + len(title) + len(text) + len(menu)` octets | `docs/npc-dialogs.md:34-44` ; `type` int32 @7, `handle` u32 @11, longueurs u16 @15/@17/@19, octets à partir de 21 |
| `TM_CS_DIALOG` (3001) | `9 + len(trigger)` octets | `docs/npc-dialogs.md:52-53` ; longueur u16 @7, octets @9 ; borne applicative `MaxTriggerLength = 1024` (`Game/Services/NpcDialogService.cs:16`) |

Grammaire du contenu, telle que les deux références la produisent et que le client la lit :

| chaîne | où | sens |
| --- | --- | --- |
| `QUEST\|<code>\|<textID>` | **texte** du dialogue 3000 | NGemity `Messages.cpp:676` ; reconnue par le client (`0x0067ce8a`) |
| `start_quest( <code>, <textID> )` | déclencheur de menu | NGemity `Messages.cpp:713` ; nom connu du client (`0x0057d14d`) |
| `end_quest( <code>, <-1 ou index> )` | déclencheur de menu | NGemity `Messages.cpp:686,699` ; nom connu du client (`0x0057d174`) et **format texte présent dans le client** (`end_quest( %d, %d )`, `VA 0x00a2f150`, sites `0x00585367` et `0x0058a9cc`) |
| `\t<libellé>\t<déclencheur>\t` | champ `menu` du 3000 | `Player::AddDialogMenu`, `Player.cpp:1102-1106` ; `docs/npc-dialogs.md:46-50` |

### 3.5 Rappel des trames d'état déjà livrées (a)

- **600** `TM_SC_QUEST_LIST` : `11 + 61·N + 8·M` octets ; deux comptes u16 (actives @7, en attente @9),
  élément `TS_QUEST_INFO` de 61 octets (`code` u32 @+0, `startID` u32 @+4, `value[6]` @+8,
  `status[6]` @+32, `progress` u8 @+56, `timeLimit` u32 @+57). Source : `socle-quetes.md` §3.4.
- **601** `TM_SC_QUEST_STATUS` : **40** octets (`code` i32 @7, `status[6]` @11, `nProgress` i8 @35,
  `nTimeLimit` u32 @36). Source : `socle-quetes.md` §3.5.

Ce sont les deux seules trames par lesquelles une acceptation ou une progression devient visible ;
le cycle n'invente donc aucune trame d'état nouvelle.

## 4. Gating de version — chaque champ est tranché pour Epic 7.3

Valeurs : `EPIC_5_1 = 0x050100`, `EPIC_6_1 = 0x060100`, `EPIC_6_3 = 0x060300`, `EPIC_7_3 = 0x070300`,
`EPIC_9_6_3 = 0x090603` (`librzu/src/lib/Packet/PacketEpics.h:53,54,56,59,96`). Cible : `0x070300`.

| élément | gating montré par rzu | décision pour 7.3 | source |
| --- | --- | --- | --- |
| id 604 | `X(604, version < EPIC_9_6_3)` / `1604` au-delà | **604** (`0x070300 < 0x090603`) | `TS_CS_QUEST_INFO.h:11-13` |
| id 605 | `X(605, version < EPIC_9_6_3)` / `1605` au-delà | **605** | `TS_CS_END_QUEST.h:10-12` |
| `code` de 605 | commentaire « Since EPIC_7_3 » | **présent** : la trame montante 605 n'existe qu'à partir de 7.3 | `TS_CS_END_QUEST.h:9` |
| `nOptionalReward` de 605 | aucun gating | **présent**, `int8` @11, domaine `-1` inclus | `TS_CS_END_QUEST.h:7` ; client `0x0061aeae` |
| id 602 | `X(602, version < EPIC_9_6_3)` | **602** si un jour émise ; écartée ici | `TS_SC_QUEST_INFOMATION.h:18-20` |
| `type` 3/7/8 du dialogue 3000 | `#if EPIC >= EPIC_5_1` chez NGemity | **les trois valeurs sont disponibles en 7.3** ; 3 = neutre, 7 = en cours, 8 = terminable | NGemity `Messages.cpp:627-641,672-675` |
| `nProgress` de 602 | enum `TS_QUEST_PROGRESS` : 0/1/2 | **0/1/2** pour 602 uniquement ; la divergence avec `255` de NGemity (`QuestBase.h:46`) reste ouverte (§7) | rzu `TS_SC_QUEST_INFOMATION.h:5-10` ; `socle-quetes.md` §6 écart 4 |
| nombre de récompenses optionnelles | non gaté par rzu ; **3** chez NGemity (`MAX_OPTIONAL_REWARD`, `QuestBase.h:21`), **6** dans le schéma 7.3 (`ArcadiaSchemaPSQL.sql:1789+`, colonnes `optional_reward_*1..6`) | **6 emplacements en 7.3**, l'index de trame reste un `int8` ; l'écart avec NGemity est celui du catalogue, pas du fil | `ArcadiaSchemaPSQL.sql` (table `QuestResource`) ; `QuestBase.h:21` |
| id 3000 / 3001 | aucun gating rzu pertinent (paquets stables) | **3000 / 3001** | `op_codes.md:220-221` |

Le `+1000` d'`EPIC_9_6_3` n'affecte aucun de ces ids en 7.3.

## 5. Traitement attendu

### 5.1 Où vivent les définitions de quête, et ce qu'il faut en lire

**Les définitions ne vivent pas dans le client.** `reference/client73/db_quest.rdb` ne contient, en
dehors de son en-tête, aucune chaîne lisible (`strings -n 4` : deux lignes : `20241026` et
`Written by Archemedes v0.1.0`) : c'est un fichier **réécrit par un outil tiers** (Archemedes v0.1.0),
non un extrait retail authentifié — la limite déjà posée par `socle-quetes.md` §8.10.
`db_queststring.rdb` porte des clés `quest_string_*` et des gabarits
(`#@m_name:1@# #@status:1@#/#@num:2@#`). Aucun des deux ne peut servir de source de vérité pour un
serveur.

**Le catalogue vit dans Arcadia**, et il est déjà décrit dans le dépôt :

| table | rôle | colonnes décisives | source |
| --- | --- | --- | --- |
| `QuestResource` | la définition d'une quête | `text_id_quest`, `text_id_summary`, `text_id_status`, `limit_level`, `limit_job_level`, `limit_quest_indication`, `limit_deva`/`asura`/`gaia`, `limit_fighter`/`hunter`/`magician`/`summoner`/`summoner`, `limit_job`, `limit_favor_group_id`, `limit_favor`, `repeatable`, `invoke_condition`, `invoke_value`, `type`, `value1..12`, `drop_group_id`, `quest_difficulty`, `favor_group_id`, `hate_group_id`, `favor`, `exp`, `jp`, `default_reward_id/level/quantity`, `optional_reward_id/level/quantity 1..6`, `forequest1..3`, `or_flag`, `is_auto_quest`, `cool_time`, `accept_cool_time` | `ArcadiaSchemaPSQL.sql:1789-1882` |
| `QuestLinkResource` | le lien **PNJ ↔ quête**, c'est-à-dire le déclencheur | `npc_id`, `quest_id`, `flag_start`, `flag_progress`, `flag_end`, `text_id_start`, `text_id_in_progress`, `text_id_end` | `ArcadiaSchemaPSQL.sql:906-916` |
| `QuestStringResourse` | variante plus ancienne de la même définition (3 récompenses optionnelles, pas de limites temporelles) | `id`, `text_id_quest`, …, `forequest1..3`, `or_flag`, `script_start_text`, `script_end_text`, `script_text` | `ArcadiaSchemaPSQL.sql:1234-1297` |

La référence NGemity lit **ces deux tables** au démarrage : `ObjectMgr::LoadQuestResource`
(`Chihiro/src/Globals/ObjectMgr.cpp:289-375`, `SELECT * FROM QuestResource`) remplit un
`QuestBaseServer` (`Chihiro/src/Quests/QuestBase.h:54-91`), et `ObjectMgr::LoadQuestLinkResource`
(`ObjectMgr.cpp:454-483`, `SELECT * FROM QuestLinkResource`) remplit un `QuestLink`
(`QuestBase.h:95-105`). Ces deux structures sont le modèle de lecture à reprendre, avec trois écarts
de version documentés au §6.

Ce qu'il faut lire pour chaque étape du cycle :

| étape | à lire | où, chez NGemity | à lire côté serveur |
| --- | --- | --- | --- |
| **armer** (offrir) | les liens du PNJ en contact, et le texte à montrer selon l'état | `NPC::DoEachStartableQuest` `/ DoEachInProgressQuest` `/ DoEachFinishableQuest` (`Chihiro/src/Entities/NPC/NPC.cpp:133,142,151`), `GetQuestTextID` (`:160`), `GetProgressFromTextID` (`:183`), `NPC::LinkQuest` (`:63`) ; agrégation : `World::ShowQuestMenu` (`Chihiro/src/World/World.cpp:594-622`) | `QuestLinkResource` par `npc_id` + l'état de la quête pour le joueur |
| **accepter** | la définissabilité : pas déjà portée, `repeatable`, prérequis `forequest*`, limites de niveau/métier/race, plafond de quêtes actives | `Player::StartQuest` (`Chihiro/src/Entities/Player/Player.cpp:2222-2266`), `QuestManager::IsStartableQuest` (`Chihiro/src/Quests/QuestManager.cpp:505-529`), `Player::IsStartableQuest` (`Player.cpp:2006`), plafond `20` (`Player.cpp:2228`) | `QuestResource` (colonnes `limit_*`, `repeatable`, `forequest1..3`, `or_flag`) + la table de quêtes du personnage (a) |
| **progresser** | les couples clé/valeur de la quête et les événements qui les font bouger | `Player::updateQuestStatus` (`Player.cpp:2280+`), `QuestManager::UpdateQuestStatusByItemCount` / `ByMonsterKill` / `BySkillLevel` / `ByJobLevel` / `ByParameter` (`QuestManager.cpp`), `Player::onStatusChanged` (`Player.cpp:2050`) | `QuestResource.type` + `value1..12` (sémantique **non établie**, §7) |
| **terminer** | la finabilité, puis les récompenses | `Player::EndQuest` (`Player.cpp:2333-2418`), `CheckFinishableQuestAndGetQuestStruct` (`Player.cpp:2428`), `Player::IsFinishableQuest` (`:2039`), `QuestManager::EndQuest` (`QuestManager.cpp:245`) | `QuestResource` : `default_reward_*`, `optional_reward_*1..6`, `exp`, `jp`, `favor`, `drop_group_id` |
| **journaliser** | l'échange de messages chat | `Messages::SendQuestMessage` (`Chihiro/src/Network/Messages.cpp:860-863`) → `SendChatMessage(120, "@QUEST", …)` (`:206-218`) ; chaînes produites aux sites d'appel `Player.cpp:2229,2235,2254,2264,2337,2355,2359,2400,2407,2424` : `START\|SUCCESS\|<code>`, `START\|FAIL\|NOT_STARTABLE\|<textid>`, `START\|FAIL\|QUEST_NUMBER_EXCEED\|<textid>`, `END\|EXP\|…`, `END\|FAIL\|0`, `END\|TOO_MUCH_MONEY\|…`, `END\|REWARD\|<item>` | politique de jeu — §5.5 |

**Prérequis d'infrastructure (à signaler, non installable depuis ce rôle)** : il n'y a **pas de
PostgreSQL ni de base Arcadia** sur ce VPS ; l'entité, la migration et le dépôt peuvent être écrits et
testés sur fixtures, mais **aucune donnée de quête réelle ne peut être chargée ni validée ici**. La
provenance des lignes de `QuestResource` / `QuestLinkResource` (millésime, complétude) n'est pas
établie non plus : `docs/npc-dialogs.md:15-19` montre que le catalogue de dialogues a dû être
**généré** depuis une `NPCResource` 9.4 et du Lua décompressé, et rangé versionné dans le dépôt
(`DevConsole/npc-dialogs.73.json`, générateur `tools/import_npc_dialogs.py`). Le même dispositif
(import outillé + catalogue versionné) est la seule voie praticable pour les quêtes ; une lecture
directe de la base Arcadia n'est pas vérifiable ici.

### 5.2 Le déclencheur — ce que le serveur doit posséder pour armer la fenêtre

Pour qu'une fenêtre de quête s'ouvre, le serveur doit posséder et émettre **quatre choses**, toutes
établies :

1. **le lien PNJ ↔ quête** : `QuestLinkResource` par `npc_id`, avec les trois drapeaux
   (`flag_start`, `flag_progress`, `flag_end`) et les trois `text_id_*`
   (`ArcadiaSchemaPSQL.sql:906-916`) — c'est ce que `NPC::LinkQuest` installe (`NPC.cpp:63`) ;
2. **le texte à montrer**, choisi selon l'état : `text_id_start` / `text_id_in_progress` /
   `text_id_end` (`World.cpp:604-608`, `NPC::GetQuestTextID` `NPC.cpp:160`) ;
3. **un dialogue 3000** dont le **texte** commence par `QUEST|<code>|<textID>` et dont le **menu**
   propose les déclencheurs (`Messages.cpp:676,682-716`) ; le client 7.3 reconnaît ce préfixe
   (`0x0067ce8a`) et sait que `start_quest` / `end_quest` sont les commandes de quête
   (`0x0057d14d`, `0x0057d174`) ;
4. **le dernier contact PNJ** côté serveur, puisque le retour `TM_CS_DIALOG` ne porte qu'une chaîne :
   NGemity garde `GetLastContactLong("npc")` (`WorldSession.cpp:720` ; la ligne `:718` en est la variante
commentée) ; le dépôt a l'équivalent
   (`GameClient.cs:1714-1723`, `NpcDialogService`, `docs/npc-dialogs.md:5-13`).

Le dépôt possède **déjà la plomberie du dialogue** : `TS_SC_DIALOG` (3000) est construit
(`Game/Network/Packets/Game/GameNpcDialogPackets.cs:48-107`, menu `\t<libellé>\t<déclencheur>\t`),
`TS_CS_DIALOG` (3001) est lu et borné (`:29-45`), le service mémorise les déclencheurs réellement
envoyés et **refuse tout autre** (`Game/Services/NpcDialogService.cs:73,92,167-211`), et
`docs/npc-dialogs.md:62-64` constate que les actions qui ouvrent une fenêtre spécialisée — dont les
quêtes — ne sont pas exécutées. Ce qui manque est exactement le §5.2.1-3.

**Ce qui est hors périmètre, et pourquoi** : l'exécution du déclencheur chez NGemity passe par le
moteur Lua (`WorldSession::onDialog` → `sScriptingMgr.RunString(m_pPlayer, pRecvPct->trigger)`,
`Chihiro/src/Network/GameNetwork/WorldSession.cpp:707-727`), où `quest_info` est une fonction Lua
enregistrée (`Chihiro/src/Scripting/XLua.cpp:98`, `SCRIPT_QuestInfo` `:829`). Navislamia n'exécute
pas de Lua : le déclencheur doit donc être **reconnu par le serveur C#** (ce que le dépôt fait déjà
pour les fonctions de dialogue statiques, `docs/npc-dialogs.md:55-60`). Cela signifie lire le
déclencheur comme une **grammaire fermée** (`start_quest( <i>, <i> )`, `end_quest( <i>, <i> )`) et non
l'évaluer — c'est la transposition honnête de NGemity, et elle suffit pour 7.3 puisqu'aucun élément
du client ne montre une autre forme.

### 5.3 La réponse descendante à 604 — **tranchée**

**Décision : aucune trame descendante n'est émise en réponse à 604 dans le sous-ensemble minimal ; si
un rafraîchissement s'avère nécessaire en jeu, la seule trame utilisable est `601`, jamais `602`.**

Ce qui est prouvé :

- **602 ne peut pas être la réponse** : le client 7.3 n'a **aucun handler** pour 602 — la table de
  dispatch des trames descendantes envoie 602 sur le défaut « non traité »
  (`socle-quetes.md` §5.4.2 : `0x0067e1d9`, table d'octets `0x0067f35c`, défaut `0x0067ef21`) ;
- **601 est consommable** : son handler est `0x0067db20` (`socle-quetes.md:320-325`) et il écrit
  `code`, les six `status` et `nProgress` dans l'objet de quête **déjà connu du client**, en exigeant
  que la quête soit déclarée dans sa base locale (`socle-quetes.md` §5.5) — donc précisément les
  champs qu'une fenêtre d'information affiche ;
- **600 est une resynchronisation complète**, pas une réponse ciblée : le client **remet son
  conteneur à zéro** en la recevant, donc une 600 bâtie sur un état partiel efface (`socle-quetes.md:570,607`) ;
- l'état que la fenêtre affiche est **déjà livré** : `SUIQuestInfoWnd` est alimentée par l'état que
  le client tient de 600/601, et le geste de 604 est une **interrogation** (le `code` part, rien ne
  revient dans les deux trames que le client sait lire).

Ce qui reste **NON ÉTABLI** (§7.2) : ce que le serveur 7.3 d'origine répondait réellement à 604.
Chez NGemity, le chemin attesté est un **dialogue** : le geste de quête déclenche
`Messages::SendQuestInformation` (`Messages.cpp:622-720`), qui envoie un 3000 portant
`QUEST|<code>|<textID>` et le menu des actions. C'est la réponse la plus riche et la seule attestée
par une référence — mais elle exige le lien PNJ, un contact courant et les textes (§5.2), donc elle
n'appartient pas à un sous-ensemble sans catalogue. D'où la décision ci-dessus : on reçoit 604 sans
répondre, et on n'émet **jamais** 602.

### 5.4 La réponse descendante à 605 — la seule qui soit structurelle

605 est une **demande d'écriture**, pas une interrogation. Sans catalogue, la seule réponse
défendable est un verdict, pas un contenu : `TM_SC_RESULT` (0) taggé **605**, avec
`NotActable (5)` quand la quête n'est pas portée par le joueur — le verdict que NGemity rend déjà pour
une quête absente de l'état (`TS_RESULT_NOT_ACTABLE = 5`, `shared/Server/TS_MESSAGE.h:60`, déjà
utilisé par (a) pour 603). La trame `TM_SC_RESULT` existe dans le dépôt
(`Game/Network/Packets/Game/TS_SC_RESULT.cs`) et est déjà employée pour les acquittements
(`SendResult`, `GameClient.cs:68`, par ex. `:543`).

Le succès (`Success`) n'a de sens qu'avec l'exécution de la fin de quête : or, de l'or, objets,
retrait des objets de collecte (`Player::EndQuest`, `Player.cpp:2353,2382-2402`). C'est de la
politique de jeu, donc hors du sous-ensemble minimal — mais la **réception** de 605, elle, est
structurelle (§5.5).

### 5.5 Découpage recommandé — ce qui est implémentable sans aucune décision de jeu

| lot | contenu | dépendances | décision de jeu ? |
| --- | --- | --- | --- |
| **(b1) réception 604 + 605** — `GamePackets` (604, 605), lecture bornée et **signée** des deux `code`, `nOptionalReward` lu en `sbyte` avec `-1` reconnu, bras de dispatch, tests d'offsets (11 et 12 octets) et de refus ; 605 acquittée `NotActable` hors de l'état du joueur ; 604 sans réponse (§5.3) | aucune | **non** — c'est la trame seule |
| **(b2) modèle du catalogue** — entités `QuestResource` / `QuestLinkResource` et dépôt, strictement calquées sur `ArcadiaSchemaPSQL.sql:906,1789`, migration, tests de mapping sur fixtures | aucune à l'écriture ; **aucune donnée réelle disponible** (§5.1) | **non** pour les colonnes ; **oui** dès qu'on choisit lesquelles *jugent* l'acceptation |
| **(b3) déclencheur** — une entrée de dialogue dont le texte est `QUEST\|<code>\|<textID>` et le menu porte `start_quest( … )` / `end_quest( … )`, synthétisée depuis `QuestLinkResource` et l'état du joueur | (b2) + le contact PNJ + les textes | **non** pour la forme (§5.2), **oui** pour le choix des quêtes offertes (drapeaux, limites, textes) |
| **(b4) exécution** (accepter, progresser, terminer avec récompenses, messages `@QUEST`) | (b3) + sémantique de `type`/`value1..12` + politique de récompenses | **oui** — hors socle |

**Carte dev recommandée : (b1) + (b2)**, livrables et testables ici ; **(b3) ensuite**, sans exécution ;
**(b4) parquée** jusqu'à ce que la sémantique des valeurs de quête et la provenance du catalogue
soient tranchées (c'est le même motif que la carte 605 `BAMvMQ7u` déjà parquée par le PO).

Conséquence assumée : après (b1)+(b2), le serveur **reçoit** les deux trames et ne fait toujours
avancer aucune quête — comme après (a), aucun chemin d'écriture ne crée de quête. Ce n'est pas un
défaut de découpage, c'est le prix de ne pas inventer les conditions d'acceptation.

### 5.6 Ce que le dépôt porte déjà, et ce qui manque

| élément | état | source |
| --- | --- | --- |
| 604 / 605 dans `GamePackets` | **absents** (aucun membre 600-605 hormis 600/601/603 livrés par (a)) | `Game/Network/Packets/Enums/GamePackets.cs:139-141` |
| dispatch | chaîne de `if (header.ID == …)` (bras `TM_CS_CONTACT` :1714-1717, `TM_CS_DIALOG` :1720-1723) puis `switch` final qui lève `Unknown Packet Type` (**:1865**) : **déclarer sans bras casse la boucle de réception** | `Game/Network/Clients/GameClient.cs:1370-1374,1865` |
| dialog 3000/3001 | **livré** : construction du menu, lecture bornée du déclencheur, mémorisation et refus des déclencheurs non envoyés | `GameNpcDialogPackets.cs:29-107`, `NpcDialogService.cs:73,92,167-211` |
| contact PNJ | **livré** : `TM_CS_CONTACT` / `TM_CS_DIALOG` résolus par handle (`GameClient.cs:1714-1723`), catalogue de dialogues versionné | `docs/npc-dialogs.md:5-28` |
| `TS_SC_RESULT` | **livré**, utilisé pour les acquittements | `Game/Network/Packets/Game/TS_SC_RESULT.cs` ; `SendResult` défini en `GameClient.cs:68`, employé par ex. en `:543` |
| état de quête du personnage | **livré** par (a) : `CharacterQuests` (`Code`, `StartId`, `Value[6]`, `Status[6]`, `Progress`, `TimeLimit`), migration `Version0008_CharacterQuests` | `Game/DataAccess/Entities/Telecaster/CharacterQuestEntity.cs:16-38` ; `socle-quetes.md` §10.2 |
| catalogue de quêtes | **absent** : ni entité, ni dépôt, ni chargement, ni donnée | `Game/DataAccess/Entities/Arcadia/` (aucune entité « Quest ») ; tables au schéma `ArcadiaSchemaPSQL.sql:906,1234,1789` |
| écriture d'une quête | **absente** : la table est lue et vidée, jamais remplie | `socle-quetes.md` §10.5.1 |

## 6. Écarts assumés avec NGemity, et pourquoi

NGemity compile en `EPIC_4_1_1` (`shared/Common/Define.h:25`) ; les écarts ci-dessous sont des écarts
de version ou de périmètre, pas des erreurs de la référence.

| # | écart | rzu / client 7.3 | NGemity | décision |
| --- | --- | --- | --- | --- |
| 1 | nombre de récompenses optionnelles | schéma 7.3 : `optional_reward_id/level/quantity 1..6` (`ArcadiaSchemaPSQL.sql:1789-1882`) | `MAX_OPTIONAL_REWARD = 3` (`QuestBase.h:21`) et borne `nRewardID < MAX_OPTIONAL_REWARD` (`Player.cpp:2402`) | **6 emplacements** en 7.3 ; l'index de trame reste `int8`, `-1` = aucun choix ; la **borne** du jugement n'est pas établie (§7.1) |
| 2 | `int8` signé de 605 | `int8_t`, `-1` écrit par le client lui-même (`0x0061aeae`) | `int32_t nRewardID` + sentinelle `-1` dans le déclencheur (`Messages.cpp:686,699`) | lire **signé** ; ne jamais replier `-1` sur `255` — c'est la traduction fidèle des deux |
| 3 | table de définition | `QuestResource` (6 récompenses) **et** `QuestStringResourse` (3 récompenses, colonnes `script_*`) coexistent au schéma | lit `QuestResource` en positionnel (`ObjectMgr.cpp:289-375`) | lire `QuestResource` ; le rôle de `QuestStringResourse` n'est pas établi (§7.3) — ne pas la fusionner par supposition |
| 4 | scripts de quête | `script_start_text` / `script_end_text` / `script_text` sont des `varchar(512)` au schéma 7.3 | `strAcceptScript` / `strClearScript` / `strScript` exécutés par Lua (`Player.cpp:2257-2259`, `XLua.cpp:98`) | **pas de Lua** : le déclencheur est une grammaire fermée reconnue en C# (§5.2) |
| 5 | réponse à 604 | 602 n'a **aucun handler** côté client 7.3 (`socle-quetes.md` §5.4.2) | envoie un **dialogue 3000** (`Messages.cpp:622-720`) via un script Lua | recevoir sans répondre, ou 601 (§5.3) ; **jamais** 602 |
| 6 | réponse à 605 | aucune réponse établie | message chat `@QUEST` (`END\|EXP\|…`, `Messages.cpp:860-863`) et récompenses immédiates (`Player.cpp:2333-2418`) | `TM_SC_RESULT` taggé 605 (`NotActable` hors état) ; les récompenses sont hors socle |
| 7 | déclencheur | le client reconnaît `QUEST\|` et les commandes `start_quest` / `end_quest` (`0x0067ce8a`, `0x0057d14d`, `0x0057d174`) | fabrique lui-même ces chaînes (`Messages.cpp:676,686,713`) | reprendre le format de NGemity : c'est le seul attesté et il est confirmé par le client |
| 8 | plafond de quêtes actives | non établi | `20` (`Player.cpp:2228`) | ne pas porter : politique de jeu (§7.4) |
| 9 | chaîne `trigger_length` de 602 | sans tableau derrière (rzu « Seems unused ») | jamais émise | 602 reste écartée |

## 7. NON ÉTABLI — questions ouvertes, à trancher

1. **Borne de `nOptionalReward`.** Le domaine `-1` est prouvé (client `0x0061aeae`) et le schéma 7.3
   offre six emplacements, mais **aucune borne n'est établie** : ni rzu, ni le client, ni le schéma
   (qui ne dit pas combien d'emplacements sont remplis) ne l'imposent. Question : quel verdict pour un
   index hors des emplacements réellement définis (refus par `InvalidArgument`, ou `NotActable` comme
   un index non pourvu) ? À défaut, `QuestDropRules` de (a) donne le précédent : **refuser ce que la
   trame seule ne permet pas de juger**.
2. **Ce que le serveur 7.3 d'origine répondait à 604.** Aucune référence ne le montre : NGemity
   (4.1.1) n'a **pas de handler** pour 604 (`WorldSession.*`), et il n'y a aucun handler 602 côté
   client 7.3. La fiche tranche « pas de réponse » (§5.3) ; si l'essai en jeu montre une fenêtre
   d'information vide, la seule réponse consommable est **601**.
3. **Rôle de `QuestStringResourse`.** Elle coexiste avec `QuestResource` au schéma 7.3, avec trois
   récompenses optionnelles, des colonnes `limit_*` identiques et trois colonnes de script. Est-ce une
   table héritée, une table de chaînes, ou la table réellement lue par un serveur 7.3 ? Non établi :
   **aucune des deux ne doit être choisie par supposition**, et aucune donnée réelle n'est disponible
   ici pour trancher (§5.1).
4. **Conditions d'acceptation, de progression et de fin.** NGemity en connaît un jeu complet — plafond
   de 20 quêtes actives (`Player.cpp:2228`), `repeatable`, `forequest1..3` (`QuestManager.cpp:518-528`),
   `limit_level` / `limit_job_level` / `limit_job` / `limit_*` de race et de classe, `invoke_condition`
   / `invoke_value`, `cool_time` / `accept_cool_time`, `is_auto_quest` — mais **aucune n'est prouvée
   pour 7.3** : ce sont des colonnes de catalogue dont la sémantique d'exécution n'est pas lisible.
   Toutes relèvent de `## A VERIFIER PAR KILLIAN`.
5. **Sémantique de `value1..12` et des six `status`.** Les deux trames transportent ces nombres, le
   client les copie (`0x00670d30`, `0x0067dba9`-`0x0067dbc7`), et NGemity les interprète par type de
   quête (`Player::updateQuestStatus`, `Player.cpp:2286-2348` : couples clé/valeur, `2 * i` pour les
   objets, `i + 3` pour les monstres). Aucune correspondance 7.3 n'est établie : le socle les
   transporte **opaques** ((a) §5.6.3), et l'exécution de la progression ne peut pas être écrite sans
   cette correspondance.
6. **Textes et libellés.** Les libellés de menu que NGemity produit (`START`, `REJECT`, `REWARD`,
   `NULL`, `OK`) sont des chaînes littérales ; le client 7.3 en référence au moins une (`REJECT`,
   `0x0067d5fb`). Est-ce que le client les localise lui-même, et faut-il les produire tels quels ?
   L'essai en jeu seul peut le dire — question à porter à Killian.
7. **`db_quest.rdb`.** Son en-tête (`20241026`, `Written by Archemedes v0.1.0`) en fait un fichier
   réécrit par un outil tiers : il n'authentifie ni la liste des quêtes 7.3, ni leurs textes. Rien
   n'en a été déduit ici. `sha256 db_quest.rdb =
   70a06f1abaf61fe42b612babbddba5c3ae66e8a685c008b72e72f8445c5b8ac1`,
   `db_queststring.rdb = 23d04b251b6e7ccfb4c064fa86e8f6566eb1722b969529a3294ad811c51e7a6f`.
8. **`data.000` n'est pas lisible.** Le paquet de données du client (3 646 701 octets) est chiffré :
   les fichiers `.nui` qui portent les fenêtres de quête et le libellé exact du bouton
   `quest_info_button` n'ont pas pu être lus. Le geste de 604 est prouvé par le code, pas par la vue.
9. **`TM_SC_DIALOG` : titre ou texte ?** NGemity place `QUEST|<code>|<textID>` dans le **texte**
   (`Messages.cpp:676`) ; le handler du client 7.3 lit `[esi+0x0f]` et `[esi+0x11]`, que
   `docs/npc-dialogs.md:41-42` nomme longueurs `title` et `text`. Lequel des deux porte le préfixe
   dans l'octet-stream d'origine n'est pas tranché sans l'essai en jeu ; comme le préfixe est unique
   dans le dialogue, produire le texte comme NGemity est le choix le plus sûr et le seul attesté.
   **Le titre** n'est pas établi non plus : NGemity écrit une chaîne constante (`"Guide Arocel"`,
   `Messages.cpp:672`), qui n'est **pas** le nom du PNJ. Un serveur 7.3 y met vraisemblablement le nom
   du PNJ (c'est ce que la fenêtre affiche), mais aucune référence lue ici ne le prouve : le titre est
   donc à confirmer en jeu, au même titre que le choix titre/texte.

## 8. Commits épinglés

| référence | commit | autorité |
| --- | --- | --- |
| rzu (`librzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02) | tailles, ordre des champs, gating par version |
| NGemity (`Chihiro`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03) | logique de quête, catalogue, déclencheurs, sous réserve d'`EPIC_4_1_1` |
| client Epic 7.3 | `reference/client73/SFrame.exe`, `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | tranche en dernier ressort |

## A VERIFIER PAR KILLIAN

1. **Réponse à 604** (§5.3, §7.2). La fiche tranche « aucune réponse, jamais 602 ». Aucune référence
   ne montre ce que le serveur 7.3 d'origine faisait ; si l'essai en jeu montre une fenêtre
   d'information vide ou figée, la seule réponse utilisable est 601 (602 n'a pas de handler client).
2. **Forme du déclencheur** (§5.2, §7.6, §7.9). Le format `QUEST|<code>|<textID>` + menu
   `\tSTART\tstart_quest( code, textid )\t` est celui de NGemity (4.1.1) et le client 7.3 en reconnaît
   les deux motifs, mais l'écriture exacte de 3000 n'a pas pu être confrontée à un serveur 7.3 :
   titre ou texte, libellés de menu, page initiale. À valider en jeu. Le **titre** est chez NGemity une
   constante (`"Guide Arocel"`, `Messages.cpp:672`), donc non dérivée du PNJ ; le titre 7.3 (nom du PNJ
   affiché par la fenêtre ?) reste à confirmer.
3. **Borne de `nOptionalReward`** (§7.1). `-1` est prouvé ; la borne supérieure (3 chez NGemity,
   6 emplacements au schéma 7.3) doit être confirmée par un catalogue réel.
4. **Provenance du catalogue de quêtes** (§5.1). Le VPS n'a ni PostgreSQL ni base Arcadia, et la
   provenance des lignes de `QuestResource` / `QuestLinkResource` n'est pas établie. Décision
   attendue : importer le catalogue dans le dépôt versionné, comme `DevConsole/npc-dialogs.73.json`
   (`docs/npc-dialogs.md:15-19`) — et depuis quelle source.
5. **Conditions d'acceptation, de progression et de fin** (§7.4, §7.5) : plafond, `repeatable`,
   `forequest*`, limites, `cool_time`, `is_auto_quest`, sémantique de `type` / `value1..12`.
   Ce sont des décisions de jeu, hors socle.

## Annexe — bloc destiné à `CLAUDE.md` (proposition)

Ce bloc est à porter par la **description de la MR** : `CLAUDE.md` est un fichier d'instructions
protégé, `navis-ref` et `navis-dev` ne l'écrivent pas.

```
## Quêtes — cycle 7.3 (604/605, catalogue, déclencheur)

- 604 `TM_CS_QUEST_INFO` : 11 octets, `code` int32 à l'offset 7. Émis quand le joueur sélectionne une
  quête dans `SUIQuestListWnd` et clique `quest_info_button` (client : ctor de message `0x00587150`,
  cas `0x0049d468` -> constructeur de trame `0x0048d1b0`). Réponse : **aucune** — 602 n'a pas de
  handler client (`socle-quetes.md` §5.4.2) ; si un rafraîchissement s'avère nécessaire, la seule
  trame consommable est 601.
- 605 `TM_CS_END_QUEST` : 12 octets, `code` int32 à 7, `nOptionalReward` **int8** à 11 ; `-1` est une
  valeur réelle (client `0x0061aeae`) et signifie « aucune récompense optionnelle ». Réponse hors
  état du joueur : `TM_SC_RESULT` (0) taggé 605, `NotActable` (5).
- Définitions : `QuestResource` et `QuestLinkResource` du schéma Arcadia
  (`ArcadiaSchemaPSQL.sql:906,1789`) ; le client n'en porte pas (`db_quest.rdb` réécrit par un outil
  tiers, non authentifié). Six récompenses optionnelles en 7.3, contre trois chez NGemity.
- Déclencheur : un `TM_SC_DIALOG` (3000) dont le **texte** est `QUEST|<code>|<textID>` et dont le
  menu porte `\tSTART\tstart_quest( code, textid )\t` / `\tNULL\tend_quest( code, i )\t`. Le client
  7.3 reconnaît ces motifs (`0x0067ce8a`, `0x0057d14d`, `0x0057d174`) ; le retour est un
  `TM_CS_DIALOG` (3001) porteur du déclencheur, à lire comme une **grammaire fermée** — jamais de Lua.
  Le **titre** est chez NGemity une constante (`"Guide Arocel"`, `Messages.cpp:672`), pas le nom du PNJ :
  le titre 7.3 reste à confirmer.
- Détail, sources et réserves : `docs/packet-specs/socle-cycle-quete.md`.
```

