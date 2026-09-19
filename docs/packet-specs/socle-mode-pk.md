# Socle — mode PK et combat joueur contre joueur (Epic 7.3)

Fiche d'archéologie du socle demandé par la carte
`Socle mode PK et combat joueur contre joueur — fiche et archéologie (prérequis de 800 et 801)` :
ce que portent les paquets client `800` et `801`, ce que l'état « mode PK » devient sur le fil en
Epic 7.3, et **tout ce que le combat joueur contre joueur exige en plus**. Elle borne le lot : les
paquets `800` et `801` sont traités par des cartes distinctes et **ne sont pas implémentés ici**.

Cette branche ne modifie aucun fichier de code serveur : elle ajoute cette fiche et rien d'autre.

- Dépôt `KillianDoubre/Navislamia`, branche `hermes/packet-socle-mode-pk`, créée depuis
  `master` = `ec76b218cd` (« Merge pull request #4 from KillianDoubre/hermes/packet-203-drop-item »).
- Aucun binaire ni script client n'a été exécuté : toute la lecture du client 7.3 est statique
  (chaînes, table d'annotation id → nom, désassemblage).
- Binaire client épinglé : `reference/client73/SFrame.exe`, 9 841 664 octets,
  `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`.
- Les renvois `SFrame.strings l. N` désignent la ligne N de
  `strings -n 4 reference/client73/SFrame.exe` (63 099 lignes) ; les renvois
  `db_string.rdb l. N` désignent `strings -n 5 reference/client73/db_string.rdb` (254 061 lignes) ;
  les renvois `db_localcommand.rdb l. N` désignent `strings -n 3`.
- Les renvois `SFrame.exe+0xADDR` désignent une adresse virtuelle du binaire ; les numéros de ligne
  du désassemblage ne sont pas cités, l'adresse l'est.

## 1. Identité et cadrage

| nom | id décimal 7.3 | corps | source du nom | état sur `master` |
|---|---|---|---|---|
| `TM_CS_TURN_ON_PK_MODE` | **800** | vide — trame de 7 octets | `op_codes.md:182`, annotation client `.rdata 0xa53110` | absent de `GamePackets.cs` |
| `TM_CS_TURN_OFF_PK_MODE` | **801** | vide — trame de 7 octets | `op_codes.md:183`, annotation client `.rdata 0xa530f8` | absent de `GamePackets.cs` |

Ce sont les deux seuls paquets PK de tout le corpus de référence :
`find reference/rzu -iname '*PK*'` ne rend que ces deux en-têtes ; `reference/ngemity` n'a que leurs
homologues `shared/Server/Packets/GameClient/TS_{CS_TURN_ON,CS_TURN_OFF}_PK_MODE.h` ; et la table
d'annotation du client 7.3 — la liste la plus large de noms de messages côté client — ne contient
`PK` que deux fois : les lignes `SFrame.strings l. 26309` et `l. 26310`.

**Il n'existe aucun paquet serveur `TM_SC_*PK*`.** Ni rzu, ni NGemity, ni le client 7.3 n'en
déclarent. La conséquence structure tout le socle : le serveur ne « répond » pas aux 800/801 par un
paquet PK, il publie l'état dans le champ `status` de trames qui existent déjà (§5).

### 1.1 Cadrage du PO : ce qui est confirmé, ce qui est démenti

- `master` = `ec76b21` : **confirmé** (`git rev-parse`, arbre propre).
- « Le répertoire `docs/packet-specs/` n'existe pas encore sur master : le créer » : **démenti**.
  Le répertoire existe et est suivi depuis le merge de la fiche 203 — il contient
  `203-drop-item.md`, `253-use-item.md`, `550-get-region-info.md`, `1202-emotion.md` — et
  `.gitignore:473` porte déjà l'exception `!/docs/packet-specs/`. Aucun `.gitignore` à modifier,
  aucune branche intermédiaire à créer : cette fiche se pose dans le répertoire tel quel, exactement
  comme `socle-mort-respawn.md` sur sa branche non mergée.
- « disposition des paquets `800`/`801` inconnue » : **confirmé**, et le §3 la tranche.
- La description de la carte Trello `BY6qivuo`, citée comme source du constat, **n'a pas été lue** :
  le profil `navis-ref` n'utilise pas Trello. Le constat a été re-dérivé des références locales ; les
  écarts de cadrage sont signalés ici et en §12.

## 2. Ce que le joueur fait pour que le client envoie le 800 ou le 801

Aucun paquet préalable du joueur : le mode PK s'allume depuis l'interface, une touche liée ou le
chat local. Les trois gestes sont attestés dans les ressources du client 7.3.

1. **L'icône PK du HUD.** Les ressources d'icône sont `SFrame.strings l. 20902 "_pk_icon"`,
   `l. 20874 "icon_pk_normal_0001"` et `l. 20879 "icon_pk_demoniac_0001"` ; l'infobulle du contrôle
   est `l. 19450 "PK Mode On/Off"`. Le gestionnaire de clic est la fonction qui contient le site
   d'émission `SFrame.exe+0x68afcb` (voir §5.3).
2. **Une touche liée.** `db_string.rdb l. 111993 "Toggle PK Mode"` et
   `l. 112057 "Toggle PK Mode Key"`. Cette liaison vit dans les *key bindings* du client, que
   NavisLamia persiste déjà dans `client_info` (migration
   `20260713183644_Version0006_ClientInfoKeyBindings`).
3. **Les commandes locales.** `db_string.rdb l. 4400` annonce
   « `Turn PK mode on/off.` … `/PKON /PKOFF` », et `db_localcommand.rdb` enregistre les alias
   `PK` et `pkoff` vers la commande interne `pkoff` (tokens `l. 72-73`). `pkon` n'y figure pas :
   deux alias seulement ont été trouvés.

### 2.1 Le client pré-juge avant d'émettre, et c'est un pré-jugement local

Deux sites d'émission distincts, tous deux dans `SFrame.exe` :

- `SFrame.exe+0x68afcb` : une **bascule**. La fonction lit le bit `0x800` des drapeaux de l'acteur
  du joueur local (`call 0x6b1520` → `[[player+0x35c]+0xf0]`, `SFrame.exe+0x68b003`), puis
  `je 0x68b035` → `call 0x684bb0` (**800**) quand le bit est **absent**, sinon `call 0x684c00`
  (**801**). Une même icône allume et éteint. Le site est atteint après
  `push 0x18 ; call 0x698a00 ; call 0x696d30 ; cmp eax,2` — l'entier `0x18` (24) est un **id
  interne** de registre de contrôles, pas un id du fil ; ne pas le citer comme un id de paquet.
- `SFrame.exe+0x684fa0` : une **commande explicite** `f(bool on)`. À `on = 0` elle émet toujours
  `801` (`SFrame.exe+0x684fce`, rend `1`). À `on = 1` elle exige `[player+0x660] != 0`
  (`SFrame.exe+0x684faf`) ; sinon elle n'émet **rien** et rend `0` (`SFrame.exe+0x684fc2`).

`[player+0x660]` est le verrou de zone : il est écrit à `SFrame.exe+0x6883e2` (valeur `1`) et
`+0x6883eb` (valeur `0`) selon qu'un champ d'identité vaut `1`, `3` ou `10`
(`SFrame.exe+0x6883d3-0x6883e0`). C'est le pendant client de
`db_string.rdb l. 75322 "You are currently in an area where PK MODE cannot be activated."`.
Le client ne demande donc pas au serveur l'autorisation d'allumer le mode PK ; il refuse lui-même
dans ces zones. **Le serveur ne doit pas compter sur ce refus** : `[player+0x660]` est un état du
client, et un client modifié peut l'ignorer (§12).

Le même gestionnaire utilise le flottant `.rdata 0xa54330` = `360.0` (secondes) et le champ
`[player+0x6cc]` passé à `SFrame.exe+0x478410` sur le troisième id interne `0x19` (25), avec
`smsq_pkmode_on` / `smsq_pkmode_off` (`db_string.rdb l. 75319-75326`) dont les textes portent le
gabarit `#@second@#` : il existe une temporisation avant activation et avant extinction. Sa
sémantique exacte n'est pas tranchée (§12).

## 3. Structure sur le fil — trame de 7 octets, corps vide

Les deux paquets sont **des trames à en-tête seul** : 7 octets, aucun corps. C'est ce que rzu
déclare et ce que le constructeur du client 7.3 écrit, octet pour octet.

### 3.1 `TM_CS_TURN_ON_PK_MODE` (800)

| offset | taille | type | champ | valeur attendue | source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` | `7` (`07 00 00 00`) | client `SFrame.exe+0x684bb8` (`mov DWORD PTR [eax],0x7`) ; rzu `TS_CS_TURN_ON_PK_MODE.h:5` (`_DEF(_)` vide) |
| 4 | 2 | `uint16` | `ID` | `800` (`20 03`) | client `SFrame.exe+0x684bd2` (`mov edx,0x320`) ; `op_codes.md:182` |
| 6 | 1 | `uint8` | `Checksum` | `0x2A` | somme des six premiers octets (`CLAUDE.md:47-48`) ; client `SFrame.exe+0x684b74-0x684b7b` |
| 7 | — | — | *corps* | **aucun octet** | rzu `TS_CS_TURN_ON_PK_MODE.h:5` ; client `SFrame.exe+0x684bb0-0x684bdf` |

**Taille totale : 7 octets.** Octets attendus : `07 00 00 00 20 03 2A`.

### 3.2 `TM_CS_TURN_OFF_PK_MODE` (801)

| offset | taille | type | champ | valeur attendue | source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` | `7` (`07 00 00 00`) | client `SFrame.exe+0x684c0a` (`mov DWORD PTR [eax],0x7`) ; rzu `TS_CS_TURN_OFF_PK_MODE.h:5` |
| 4 | 2 | `uint16` | `ID` | `801` (`21 03`) | client `SFrame.exe+0x684c22` (`mov edx,0x321`) ; `op_codes.md:183` |
| 6 | 1 | `uint8` | `Checksum` | `0x2B` | somme des six premiers octets |
| 7 | — | — | *corps* | **aucun octet** | rzu `TS_CS_TURN_OFF_PK_MODE.h:5` |

**Taille totale : 7 octets.** Octets attendus : `07 00 00 00 21 03 2B`.

Contrôle de lecture : `od -j $((0x400 + 0x684bd2 - 0x401000)) -N 2` sur `SFrame.exe` rend
`ba 20 03 00 00` (`mov edx,0x320`) et `…+0x684c22` rend `ba 21 03 00 00`. Les deux constructeurs
sont voisins (celui de `550` est à `SFrame.exe+0x684b60`), ce qui interdit de confondre l'id avec un
numéro interne voisin.

### 3.3 Piège de réception : trois trames à en-tête seul existaient déjà

`CLAUDE.md:51-56` documente le piège : `TM_CS_RETURN_LOBBY (23)`, `TM_CS_REQUEST_RETURN_LOBBY (25)`
et `TM_CS_LOGOUT (27)` sont elles aussi des trames de 7 octets, et la boucle de réception doit
comparer `remainingData >= Marshal.SizeOf<Header>()` — la comparaison stricte historique perdait le
paquet. **800 et 801 sont les quatrième et cinquième trames à en-tête seul du protocole client.** Le
correctif est en place sur `master` ; le gestionnaire ne doit lire **aucun** octet de corps, et il ne
doit surtout pas exiger `header.Length > 7`.

## 4. Gating de version — tranché pour l'Epic 7.3

rzu montre un gating sur chaque trame du socle. Pour toutes, `< EPIC_9_6_3` désigne la forme
antérieure, donc celle du client 7.3 ; et le client 7.3 lui-même tranche sans ambiguïté : sa table
d'annotation nomme `800` et `801`, pas `1800`/`1801`.

| trame | id rzu `< EPIC_9_6_3` | id rzu `>= EPIC_9_6_3` | **7.3** | source |
|---|---|---|---|---|
| `TS_CS_TURN_ON_PK_MODE` | 800 | 1800 | **800** | `rzu/.../TS_CS_TURN_ON_PK_MODE.h:7-9` ; client `.rdata 0xa53110` + imm `0x320` |
| `TS_CS_TURN_OFF_PK_MODE` | 801 | 1801 | **801** | `rzu/.../TS_CS_TURN_OFF_PK_MODE.h:7-9` ; client `.rdata 0xa530f8` + imm `0x321` |
| `TS_SC_STATUS_CHANGE` | 500 | 1500 | **500** | `rzu/.../TS_SC_STATUS_CHANGE.h:44-46` ; `op_codes.md:136` |
| `TS_SC_ENTER` | 3 | 1003 | **3** | `rzu/.../TS_SC_ENTER.h:168-169` ; `GamePackets.cs:9` |
| `TS_SC_LEAVE` | 9 | 1009 | **9** | `rzu/.../TS_SC_LEAVE.h:9-11` |
| `TS_SC_RESULT` | 0 | 1000 | **0** | `rzu/.../TS_SC_RESULT.h:13-14` |
| `TS_CS_ATTACK_REQUEST` | 100 | 1100 | **100** | `rzu/.../TS_CS_ATTACK_REQUEST.h:12-13` |
| `TS_SC_CANT_ATTACK` | 102 | 1102 | **102** | `rzu/.../TS_SC_CANT_ATTACK.h:9-10` |
| `TS_SC_STATE` | 505 | 1505 | **505** | `rzu/.../TS_SC_STATE.h:22-24` |
| `TS_SC_HPMP` | 509 | 1509 | **509** | `rzu/.../TS_SC_HPMP.h:24-25` |
| `TS_CS_TARGETING` | 511 | 1511 | **511** | `rzu/.../TS_CS_TARGETING.h:9-10` |

Gating de champs utile au socle : les en-têtes rzu portent `// Last tested: EPIC_9_8_1`. Pour la
trame d'entrée du joueur en 7.3, `< EPIC_9_6_7` est la forme active, donc
`type, handle, x, y, z, layer, objType` puis l'information de créature
(`status, face_direction, hp, max_hp, mp, max_mp, level, race, skin_color, is_first_enter, energy`)
puis l'information de joueur (`sex, faceId, faceTextureId, hairId, hairColorIndex, hairColorRGB,
hideEquipFlag, name[19], job_id, ride_handle, guild_id`) ; `title_code` (`>= EPIC_8_1`) et
`back_board` (`>= EPIC_9_3`) sont absents. C'est exactement la structure de
`Game/Network/Packets/Game/TS_SC_ENTER_PLAYER.cs`, donc **118 octets** et `Status` à l'offset 26
(§5.3).

## 5. Le `status` de l'acteur : seul véhicule de l'état PK en 7.3

### 5.1 Les bits

rzu, NGemity et le client 7.3 donnent la même table, à trois sources indépendantes.

| bit | rzu (`TS_CREATURE_STATUS`) | NGemity (`TS_PLAYER_FLAG`) | effet vu dans le client 7.3 | portée |
|---|---|---|---|---|
| `1 << 0` | `TCS_FlagBattleMode` | `FLAG_BATTLE_MODE` | — | unité |
| `1 << 1` | `TCS_FlagInvisible` | `FLAG_INVISIBLE` | — | unité |
| `1 << 8` | `TCS_FlagDead` / `TCS_FlagHasStartableQuest` / `TCS_FlagSitdown` | `FLAG_DEAD` / `FLAG_HAS_STARTABLE_QUEST` / `FLAG_SITDOWN` | — | **monstre** / NPC / **joueur** |
| `1 << 9` | `TCS_FlagBuyBooth` / `TCS_FlagHasInProgressQuest` | `FLAG_BUY_BOOTH` / idem | — | joueur / NPC |
| `1 << 10` | `TCS_FlagSellBooth` / `TCS_FlagHasFinishableQuest` | `FLAG_SELL_BOOTH` / idem | — | joueur / NPC |
| **`1 << 11`** | **`TCS_FlagPkOn`** | **`FLAG_PK_ON`** | test `0x800` — `SFrame.exe+0x68b006`, `+0x506287` | **joueur** |
| `1 << 12` | `TCS_FlagBloody` | `FLAG_BLOODY` | `<#fa5f4bFF>` — `SFrame.exe+0x506217` | joueur |
| `1 << 13` | `TCS_FlagDemoniac` | `FLAG_DEMONIAC` | `<#e71a00FF>` + `icon_pk_demoniac_0001` — `SFrame.exe+0x50621c`, `+0x50625b` | joueur |
| `1 << 14` | `TCS_FlagGm` | `FLAG_GM` | — | joueur |
| `1 << 16` | `TCS_FlagWalking` | `FLAG_WALKING` | — | joueur |
| `1 << 18-20` | `TCS_FlagShovelingSearch/Approach/Dig` | idem | — | joueur |
| `1 << 21` | `TCS_FlagCompeting` | `FLAG_COMPETING` | test `0x200000` — `SFrame.exe+0x5061c2` | joueur |
| `1 << 22-23` | `TCS_FlagBattleArenaTeam0/1` | idem | — | joueur |
| `1 << 15`, `1 << 17` | `TCS_FlagDungeonOriginalOwner/Sieger` | `TS_MONSTER_FLAG` idem | — | monstre |

Sources : `rzu/librzu/src/packets/GameClient/TS_SC_STATUS_CHANGE.h:8-38` ;
`ngemity/shared/Server/TS_MESSAGE.h:30-47` ; client : les tests de bits ci-dessus, tous dans la même
fonction d'affichage du nom et de l'icône (`SFrame.exe+0x506211-0x5062a0`), qui lit le champ de
statut de l'objet (`[objet+0x610]`) et en dérive la couleur du nom puis l'icône.

**`1 << 11` est donc le seul bit PK**, et il vaut `0x800`. Les trois sources concordent : c'est la
règle de lecture la plus utile de cette fiche, et elle relie le seul geste du joueur (§2) à l'état
serveur.

### 5.2 Le masque est un instantané complet, jamais un delta

NGemity ne stocke pas de « bit à envoyer » : `Messages::GetStatusCode(WorldObject*, Player*)`
(`Chihiro/src/Network/Messages.cpp:557-620`) **recalcule le masque entier** à partir de l'objet, et
tous les sites d'envoi passent par lui : `BroadcastStatusMessage`
(`Messages.cpp:757-772`, diffusion régionale), `SendNPCStatusInVisibleRange` (`:865-880`), et
`Unit::EnterPacket` (`Chihiro/src/Entities/Unit/Unit.cpp:110`).

Conséquence pour NavisLamia : `GameCharacterPackets.BuildStatusChange`
(`Game/Network/Packets/Game/GameCharacterPackets.cs:316-323`) accepte un `status` brut et deux sites
l'appellent avec un bit isolé — `GameActions.cs:234` avec `0`, `CombatService.cs:217` avec
`MonsterDeadStatus = 1 << 8` (`CombatService.cs:21`). Aujourd'hui c'est sans effet parce qu'aucun
autre bit n'est jamais levé. **Dès que plusieurs bits existent, envoyer un bit isolé éteint les
autres** : publier le mode PK par `BuildStatusChange(handle, 1 << 11)` alors que le joueur est
déjà mort (monstre) ou assis effacerait l'autre bit. Le socle doit donc introduire une fabrique de
masque **par nature d'objet** avant d'ajouter le bit PK — c'est l'objet du §8.

Piège de valeur : `1 << 8` signifie « mort » pour un monstre et « assis » pour un joueur
(`rzu/.../TS_SC_STATUS_CHANGE.h:14` et `:22`). Un `1 << 8` envoyé sur un handle de joueur ne le tue
pas, il l'assoit.

### 5.3 Où le bit PK voyage aujourd'hui, et ce qui manque

| site NavisLamia | trame | statut écrit | ce que le client en fait |
|---|---|---|---|
| `GameActions.cs:150-180` (entrée en jeu) | `TM_SC_ENTER` (3) de type `ET_Player` | `Status = 0` (`GameActions.cs:157`) | champ `status` de l'information de créature, **offset 26** de la trame, 118 octets au total (`TS_SC_ENTER_PLAYER.cs`, `Marshal.SizeOf` + en-tête de 7 via `Packet<T>`) |
| `GameActions.cs:234` | `TM_SC_STATUS_CHANGE` (500) | `0` | 15 octets, `handle` à l'offset 7, `status` à l'offset 11 (`GameCharacterPackets.cs:316-323`, test `Tests/Game/GameCharacterPacketsTests.cs:143-163`) |
| `CombatService.cs:217` | `TM_SC_STATUS_CHANGE` (500) | `1 << 8` | idem — mort d'un monstre |
| `GameSpawnPackets.cs:150-170` | `TM_SC_ENTER` (3) monstre/NPC | `0` écrit à l'offset 26 (`:158`) | idem joueur : l'information de créature occupe 26..63 |

Côté client, la réception est bien vivante : le répartiteur `.rdata 0xa1d150`
(`SFrame.strings l. 19702`, `case MSG_STATUS_CHANGE    :`) saute en `SFrame.exe+0x480064` vers le
gestionnaire `SFrame.exe+0x476e90`, qui lit le handle et le statut dans le message, retrouve
l'acteur et lui applique le statut ; l'affichage (§5.1) en dérive la couleur du nom et l'icône.
**Le bit PK est donc publié par le serveur, et le client le rend.** Le §2 montre la réciproque : le
client relit ce même bit pour décider d'envoyer 800 ou 801.

## 6. Traitement attendu, et écarts assumés avec NGemity

### 6.1 NGemity ne traite pas les 800/801 — et n'a jamais allumé le mode PK

- Les deux en-têtes existent (`shared/Server/Packets/GameClient/TS_CS_TURN_{ON,OFF}_PK_MODE.h`) et
  sont énumérés (`shared/Server/ClientPackets.h:193-194`), inclus (`shared/Server/XPacket.h:169-170`).
  `CREATE_PACKET(..., 800)` sans `VER_ID` : une seule id, pas de branche 1800.
- **Aucun gestionnaire** : `grep -rn 'TS_CS_TURN_ON_PK_MODE\|TS_CS_TURN_OFF_PK_MODE'` sur tout
  NGemity ne rend que les quatre lignes ci-dessus. Le `ProcessIncoming` de
  `Chihiro/src/Network/GameNetwork/WorldSession.h:48` ne connaît pas ces id.
- `SetPKOn()` / `SetPKOff()` (`Chihiro/src/Entities/Player/Player.h:266-267`) ne sont **appelés nulle
  part** ; `m_bIsPK` (`Player.h:437`) n'est nulle part levé, et n'est lu que par
  `Player::IsTradableWith` (`Player.cpp:3246-3250`) qui est aujourd'hui un `return true` avec un
  `// Todo: Implement this one PKing is implemented.` La vraie règle n'existe qu'en commentaire
  (`Player.cpp:3253-3305`, issu du binaire retail) : un joueur est attaquable hors serveur PK s'il
  est *bloody* ou *demoniac*, sinon l'attaque se décide sur l'appartenance de guilde/alliance et sur
  l'égalité des modes PK.
- NGemity compile en **`EPIC_4_1_1`** (`shared/Common/Define.h:25`), donc sous `EPIC_9_6_3` : ses
  identifiants coïncident avec ceux du 7.3 pour toutes les trames de la §4, mais ses dispositions de
  champs doivent être vérifiées trame par trame. Pour 800/801, la trame est vide : NGemity et le 7.3
  sont ici rigoureusement identiques, et l'écart est nul.
- NGemity porte la persistance : `pkmode` est lu dans la base au chargement
  (`Player.cpp:208`, `SetInt32Value(PLAYER_FIELD_PK_MODE, …)`) et déclaré dans le schéma
  (`Database/Telecaster.sql:151`, `Database/Arcadia.sql` pour les chaînes). rzu fait de même
  (`rzu/rzgame/src/Database/DB_Character.h:67`, `DB_Character.cpp:96`).

### 6.2 Ce que NavisLamia doit faire, en résumé

1. **Aucune réponse dédiée.** Il n'existe pas de `TM_SC_*_PK_MODE` : ne pas en inventer un, ne pas
   répondre `TM_SC_RESULT` non plus (NGemity ne le fait pas et aucun texte client ne l'attend).
2. **L'état se publie par le masque.** Le geste du joueur change un booléen d'état ; le serveur
   recalcule le masque complet de l'acteur et l'émet en `TM_SC_STATUS_CHANGE` (500, 15 octets) — et
   le réémet dans la trame d'entrée (3) pour tout client qui verrait l'acteur (§7, sous-socle B).
3. **La validation d'entrée tient en deux lignes** : exiger `header.Length == 7` (aucun corps, §3.3)
   et ne rien lire. `TM_CS_TURN_ON_PK_MODE` et `TM_CS_TURN_OFF_PK_MODE` sont distincts : le serveur
   n'a pas à déduire l'intention de l'état courant, il l'a déjà.
4. **Ne pas déduire les droits du refus client.** `[player+0x660]` (§2.1) est un état du client ;
   la zone interdite doit être décidée côté serveur, et c'est un choix de conception (§12).

## 7. Découpage du socle en quatre sous-ensembles

Le socle demandé est trop large pour une branche : il contient au moins quatre chantiers
indépendants, dont trois dépendent de décisions ou de branches non mergées.

**A — État et statut de l'acteur.** Un booléen « mode PK » par personnage, persistant dans la
colonne `Characters.PkMode` qui existe déjà, une fabrique de masque de statut complète par nature
d'objet (§5.2), et sa publication par les deux sites d'envoi existants. **Aucune décision de
gameplay** : les bits viennent de rzu et du client, la persistance vient des deux serveurs de
référence. C'est le prérequis strict des 800/801.

**B — Visibilité entre joueurs.** Aucun joueur n'est aujourd'hui visible d'un autre :
`TS_SC_ENTER_PLAYER` n'est construit qu'à un seul endroit du dépôt, `GameActions.cs:180`, et il est
adressé à `client.Connection`, c'est-à-dire au client qui entre, jamais à ses voisins. Il
faut donc un index handle → client, une diffusion d'entrée/sortie par région et la trame 3 de type
`EOT_Player` pour un tiers, plus `TM_SC_LEAVE` (9). Le patron existe déjà pour les monstres
(`Game/Services/MonsterMovementService.cs:47-105`, visibilité portée par
`ConnectionInfo.SpawnedMonsters`) mais `NetworkService.AuthorizedGameClients` est indexé par **compte**
(`AuthActions.cs:67`, `Client.cs:85`) et non par handle : c'est un index à créer. Chantier lourd.

**C — Cible joueur, attaque et mort.** `CombatService.StartAttack` résout la cible par
`info.TryResolveMonster` (`CombatService.cs:48-51`) et `SkillCastService.TryValidateTarget`
(`SkillCastService.cs:265-290`) refuse explicitement tout ce qui n'est ni un monstre ni le lanceur —
« Summons and other players are not modelled ». Il faut donc étendre ces deux résolutions à un
handle de joueur, appliquer les règles d'attaquabilité (NGemity `Player.cpp:3253-3305`), émettre
`TS_SC_CANT_ATTACK` (102) sur refus, et **tuer un joueur** — ce que `master` ne sait pas faire
(`CLAUDE.md:387`, « there is no player death or respawn »). C est donc **bloqué par la branche
`socle-mort-respawn`**, non mergée (§11).

**D — Alignement, criminalité et conséquences.** `TCS_FlagBloody` / `TCS_FlagDemoniac` viennent de
`ImmoralPoint` avec des seuils (`GameRule.h:66-67`, `MORAL_LIMIT` = `CRIME_LIMIT` = 100 — valeurs de
référence douteuses), la perte de `PkMode` et l'ajustement des points à la mort d'un joueur, la
limitation d'invitation de guilde (`db_string.rdb l. 40868`) et l'échange
(`l. 232126`, `TS_RESULT_PK_LIMIT`). C'est de la règle de jeu pure : **arbitrage de Killian**.

## 8. Sous-ensemble minimal implémenté par cette branche (sous-socle A)

**Aucun paquet 800 ni 801 n'entre dans ce lot** : ils restent portés par leurs propres cartes. Le
lot implémente le sous-socle A, qui est exactement ce qui rend ces deux cartes triviales plus tard,
sans trancher une seule règle de gameplay.

1. **Constantes de statut** (`TCS_Flag*`), au minimum `PkOn = 1 << 11`, avec la source rzu citée en
   commentaire. Elles n'existent nulle part dans le dépôt aujourd'hui : `grep -rn '1 << 11' Game/`
   ne rend que des enums sans rapport (`Jobs.cs`, `SkillWeaponGate.cs`, `LocalFlag.cs`).
2. **Fabrique de masque par nature d'objet** : un point unique qui compose le masque complet d'un
   acteur (aujourd'hui : `PkOn` pour le joueur, `Dead = 1 << 8` pour le monstre/NPC) et le remplace
   aux deux sites d'envoi — `GameActions.cs:157` (`Status = 0`) puis `GameActions.cs:234`
   (`BuildStatusChange(handle)`) et `CombatService.cs:217` (`MonsterDeadStatus`). C'est le correctif
   du piège §5.2, et il n'invente aucune valeur.
3. **`ConnectionInfo.PkMode`** : le booléen d'état de session, alimenté par la colonne existante
   `Characters.PkMode` (`Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:85`, colonne créée par
   `20231213221150_Version0001_TheBeginning.cs:120`) à l'entrée en jeu, republié dans le masque, remis à
   zéro par la réinitialisation de session, et persisté à la sauvegarde. La colonne est un `boolean`
   NOT NULL par défaut faux : **aucune migration**.
4. **Tests d'offsets** : la trame 500 fait **15 octets** (`handle` à 7, `status` à 11) et la trame 3
   d'un joueur **118 octets** avec `status` à l'offset **26** — c'est-à-dire la position que le
   socle commence à remplir. Plus les valeurs de bits et la composition du masque.

Ce qui reste explicitement **hors** du lot, au-delà de 800/801 : le verrou de zone côté serveur, la
temporisation avant activation, `Bloody`/`Demoniac` et leurs seuils, l'attaquabilité, la mort d'un
joueur, la visibilité entre joueurs.

Variante acceptable si Killian préfère un lot encore plus petit : livrer le point 1 et le point 2
seuls, et replier le point 3 dans la carte 800/801. Le découpage reste le même ; seul le point
d'arrêt change.

### 8.1 Ce que la branche ne doit pas faire

- Ne pas ajouter `TM_CS_TURN_ON_PK_MODE` / `TM_CS_TURN_OFF_PK_MODE` à `GamePackets` : un membre sans
  branche de traitement atteint le `switch` final de `GameClient.cs:791-803` et lève
  `Unknown Packet Type` (critère transversal 4).
- Ne pas envoyer `1 << 8` sur un handle de joueur : c'est le bit « assis » (§5.2).
- Ne pas fabriquer de paquet serveur PK : il n'en existe aucun (§1).

## 9. Paquets restants — pour que 800/801 rouvrent sans refaire l'archéologie

| trame | id 7.3 | taille 7.3 | disposition | ce qu'il faut encore décider |
|---|---|---|---|---|
| `TM_CS_TURN_ON_PK_MODE` | 800 | 7 | en-tête seul (§3.1) | ce qui autorise l'activation : zone, niveau, serveur PK, coût |
| `TM_CS_TURN_OFF_PK_MODE` | 801 | 7 | en-tête seul (§3.2) | si l'extinction est toujours permise |
| `TM_SC_STATUS_CHANGE` | 500 | 15 | `handle` u32 @7, `status` u32 @11 | quels bits sont publiés et quand |
| `TM_SC_ENTER` (joueur) | 3 | 118 | `status` u32 @26 (créature) | visibilité entre joueurs (sous-socle B) |
| `TM_SC_LEAVE` | 9 | 11 | `handle` u32 @7 | idem |
| `TM_CS_TARGETING` | 511 | 11 | `target` u32 @7 | déjà reçu (`GameClient.cs:209`) mais le handle n'est résolu que contre des monstres |
| `TM_CS_ATTACK_REQUEST` | 100 | 15 | `handle` u32 @7, `target_handle` u32 @11 | attaquabilité, refus |
| `TM_SC_CANT_ATTACK` | 102 | 19 | `attacker` u32 @7, `target` u32 @11, `reason` i32 @15 | codes de refus ; `TS_RESULT_PK_LIMIT = 0x1D` (29) est le seul code PK (`rzu/.../PacketEnums.h:34`) |
| `TM_SC_HPMP` | 509 | 36 | `handle` @7, `add_hp` @11, `hp` @15, `max_hp` @19, `add_mp` @23, `mp` @27, `max_mp` @31, `need_to_display` @35 | le dépôt envoie aujourd'hui les points de vie par la propriété `hp` |
| `TM_CS_RESURRECTION` | 513 | 12 | voir `docs/packet-specs/socle-mort-respawn.md` | mort d'un joueur (sous-socle C) |
| `TM_SC_RESULT` | 0 | variable | — | refus génériques |
| propriété `immoral` | 507 | variable | déjà émise (`GameActions.cs:233`) | seuils `Bloody` / `Demoniac` |

## 10. Persistance et migrations

Rien à créer. `CharacterEntity.PkMode` (`CharacterEntity.cs:85`) est un `bool` adossé à la colonne
`PkMode` créée par la migration `20231213221150_Version0001_TheBeginning.cs:120` ; elle est **lue par
personne** aujourd'hui (`grep -rn PkMode Game/` hors migrations ne rend que cette déclaration). Le
socle la lit et l'écrit : le seul point d'attention est
`CharacterService.SaveProgressAsync` (`Game/Services/CharacterService.cs:388-429`), qui ne persiste
que niveau, niveaux de métier, expérience, JP, or, chaos et position — un paramètre ou une méthode
dédiée est nécessaire pour que l'état survive à une reconnexion. Les deux serveurs de référence
persistent cet état (NGemity `Player.cpp:208` ; rzu `DB_Character.h:67`), donc persister ne relève
pas d'un choix de gameplay.

## 11. État de `master`, branches ouvertes et dépendances

`master` = `ec76b218cd`, arbre propre, `git log --oneline origin/master..master` vide.

- Mergées : `hermes/packet-1202-emotion`, `hermes/packet-203-drop-item`, `hermes/packet-253-use-item`,
  `hermes/packet-550-get-region-info`.
- **Non mergées** : `hermes/packet-221-hide-equip-info`, `hermes/packet-223-swap-equip`,
  `hermes/packet-281-puton-item-set`, `hermes/packet-408-request-remove-state`,
  `hermes/packet-socle-anti-triche`, `hermes/packet-socle-invocations`,
  `hermes/packet-socle-meteo-monde`, **`hermes/packet-socle-mort-respawn`**,
  `hermes/packet-socle-zones-evenement`.
- **Dépendance dure** : `hermes/packet-socle-mort-respawn` porte la mort et la réapparition du
  personnage joueur, sans quoi le sous-socle C ne peut pas aboutir. Le sous-ensemble A peut être
  mergé avant ou après, sans conflit attendu : il touche `GameActions`, `CombatService` et
  `GameCharacterPackets`, c'est-à-dire les fichiers que `socle-mort-respawn` modifie aussi
  (`CombatService` : chemin de dégâts, plancher à 1 PV). **Zone de collision à surveiller** :
  `Game/Services/CombatService.cs`.
- 355 tests `[Test]` sur `master` ; le plancher d'acceptation reste 366 cas rapportés par
  `dotnet test`.

## 12. NON ÉTABLI

1. **Sémantique du verrou de zone `[player+0x660]`.** Le client n'émet pas 800 hors des zones
   autorisées (§2.1), mais la nature du champ reste ouverte : les valeurs `1`, `3` et `10` testées à
   `SFrame.exe+0x6883d3-0x6883e0` sont des identifiants internes de type de carte ou de zone, sans
   chaîne associée dans les ressources extraites. Question précise : quelle notion de `db_fieldprop`
   / ressource de carte porte ces trois valeurs, et faut-il la reproduire côté serveur ?
2. **Temporisation d'activation.** `smsq_pkmode_on` / `smsq_pkmode_off`
   (`db_string.rdb l. 75320-75326`) portent `#@second@#`, le gestionnaire utilise `360.0`
   (`.rdata 0xa54330`) et le champ `[player+0x6cc]`, et `SFrame.strings l. 19513/19515` contient le
   gabarit `#@second@# \n PK MODE`. Est-ce un délai avant activation (360 s semble long), une durée
   de vie du mode, ou un affichage purement local ? Non tranché — et **c'est au serveur de décider**
   si l'activation est immédiate, ce qui n'a aucune incidence sur la trame 800 elle-même.
3. **Le refus côté serveur d'une activation interdite.** La carte 800 doit-elle répondre un
   `TM_SC_RESULT` avec un code, rester muette, ou renvoyer le masque inchangé ? Le client ne réclame
   rien (`db_string.rdb l. 75322` est un texte local), mais un client modifié peut envoyer 800 hors
   zone. Choix de conception, pas de référence.
4. **`TCS_FlagBloody` et `TCS_FlagDemoniac`** : les seuils rzu/NGemity (`GameRule.h:66-67`) valent
   100 et 100, ce qui rend les deux états simultanés — la valeur de référence est douteuse et la
   source retail de `PLAYER_FIELD_IP` → `ImmoralPoint` n'a pas été vérifiée. Le socle ne publie donc
   pas ces deux bits.
5. **Le bit `1 << 15` de `TS_SC_ENTER__CREATURE_INFO.status`** (`TCS_FlagDungeonOriginalOwner`) et
   les bits `1 << 22-23` (arène) sont listés par rzu sous « Last tested: EPIC_9_8_1 ». Leur présence
   dans le client 7.3 n'a pas été vérifiée ; aucun n'est utilisé par le socle.
6. **L'ordre d'émission à l'entrée en jeu.** Le socle publie le masque avant ou après les statuts
   existants ? Non observable sans client ; à figer en implémentant, la trame 500 acceptant
   n'importe quel ordre vis-à-vis des autres paquets d'entrée.
7. **La provenance du `status` d'entrée pour un tiers.** Le client applique-t-il bien le `status` de
   l'information de créature d'un `EOT_Player` reçu en `TM_SC_ENTER` (3) ? La lecture statique montre
   le champ, pas l'ordre d'initialisation des couches d'affichage. À vérifier lors d'un essai client
   réel, quand le sous-socle B existera.

## 13. A VERIFIER PAR KILLIAN

1. **Le lot est-il le bon ?** La fiche propose de livrer le sous-socle A (état + masque, §8) et de
   laisser 800/801, la visibilité entre joueurs, la cible joueur et l'alignement à leurs cartes. Si
   tu préfères un lot plus étroit, dis-le : les points 1 et 2 seuls suffisent à eux seuls comme
   socle.
2. **Le mode PK doit-il survivre à une reconnexion ?** Les deux références le persistent (§10), et
   la colonne existe ; le socle l'applique donc. Un serveur qui préfère « le mode PK retombe à zéro
   à chaque connexion » est une variante à une ligne.
3. **Le serveur doit-il refuser une activation dans les zones interdites ?** Le client refuse
   déjà (§2.1) et on ne sait pas nommer ces zones (§12.1). Tant que la réponse est non, le serveur
   accepte tout 800.
4. **`Bloody` / `Demoniac`** : la référence donne deux seuils égaux à 100 (§12.4). Il faut une
   source retail ou un choix explicite avant de publier ces bits.
5. **Séquencement avec `socle-mort-respawn`** : le sous-socle C en dépend, et les deux lots touchent
   `CombatService.cs`. À merger dans l'ordre qui t'arrange, mais pas en parallèle.

## 14. Bloc destiné à `CLAUDE.md` (à coller par Killian)

### Mode PK

Le mode PK s'allume depuis le client par `TM_CS_TURN_ON_PK_MODE` (**800**) et s'éteint par
`TM_CS_TURN_OFF_PK_MODE` (**801**). Les deux sont des **trames à en-tête seul de 7 octets**, corps
vide — les quatrième et cinquième du protocole client après `TM_CS_RETURN_LOBBY (23)`,
`TM_CS_REQUEST_RETURN_LOBBY (25)` et `TM_CS_LOGOUT (27)`, donc soumises au même piège de boucle de
réception (`remainingData >= Marshal.SizeOf<Header>()`, `CLAUDE.md:51-56`). Octets attendus :
`07 00 00 00 20 03 2A` et `07 00 00 00 21 03 2B`. Le client les construit à `SFrame.exe+0x684bb0` et
`+0x684c00` et les nomme dans sa table d'annotation (`.rdata 0xa53110`, `0xa530f8`). rzu les remappe
en `1800`/`1801` à partir d'`EPIC_9_6_3` : l'Epic 7.3 garde `800`/`801`.

**Il n'existe aucun paquet serveur PK.** L'état voyage dans le champ `status` de trames existantes,
et `1 << 11` (`TCS_FlagPkOn`, `NGemity TS_PLAYER_FLAG::FLAG_PK_ON`) est le seul bit du mode PK ; le
client le teste en `SFrame.exe+0x68b006` pour choisir entre 800 et 801, et s'en sert pour colorer le
nom du porteur (`<#FF8000FF>`) et son icône (`icon_pk_normal_0001` / `icon_pk_demoniac_0001`). Le
`status` est un **instantané complet, jamais un delta** : NGemity le recalcule
(`Messages::GetStatusCode`) à chaque diffusion. Attention à `1 << 8`, qui vaut « mort » pour un
monstre et « assis » pour un joueur.

NGemity ne traite ni 800 ni 801 : ses deux en-têtes existent, son `WorldSession` les ignore, et
`SetPKOn()` / `SetPKOff()` n'y sont jamais appelés. rzu et NGemity se contentent de persister une
colonne `pkmode`, comme `Characters.PkMode` ici.

## 15. Commits et binaires épinglés

| référence | commit | date constatée |
|---|---|---|
| `KillianDoubre/Navislamia` (`master`) | `ec76b218cd` | merge de la PR #4 |
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | `packets: fix TS_SC_INVENTORY with older epics` |
| `reference/ngemity` (RZEmulator) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | `Fix compilation issue for GCC` |

| binaire | taille | empreinte |
|---|---|---|
| `reference/client73/SFrame.exe` | 9 841 664 | `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` |

## 16. Note de livraison

Fiche seule, aucun fichier de code applicatif modifié, aucun test exécuté par cette branche — elle
n'ajoute que de la documentation, et le socle de code décrit en §8 reste à implémenter par la carte
de développement. Les sources de chaque ligne du §3, du §4 et du §5 sont citées fichier:ligne ou
adresse virtuelle ; aucune valeur n'a été supposée, et les sept questions ouvertes sont nommées au
§12 avec la question précise à trancher.
