# 4008 — `TM_CS_HUNTAHOLIC_LEAVE_LOBBY` (quitter le lobby HuntaHolic)

Fiche d'archéologie de protocole (branche `hermes/packet-4008-huntaholic-leave-lobby`, base
`master` = `b56967a`). **Cette fiche ne modifie aucun fichier de code**, et sa conclusion est
qu'aucun code ne doit être écrit pour cet id : le client Epic 7.3 **n'émet ni ne reçoit** `4008`
(§2), il ne le **nomme** même pas (§2.4), et le dépôt a déjà une règle écrite pour ce cas exact
(§5.2).

Autorités : `rzu` tranche l'id, la taille et le gating ; NGemity tranche la logique sous réserve de
sa version (il compile `EPIC_4_1_1`, `reference/ngemity/shared/Common/Define.h:25`) ; le client Epic
7.3 (`reference/client73/SFrame.exe`) tranche en dernier ressort. Tout est lu localement, aucun
script client n'a été exécuté, aucun serveur n'a été démarré.

## Questions tranchées par cette fiche

1. **Le client 7.3 n'émet jamais `4008`.** L'immédiat `0xfa8` apparaît **une seule fois** dans tout
   le `.text` — comme déplacement de pile à `0x67902f`, jamais comme id de paquet (§2.1). Aucun des
   cinq constructeurs de trames client → serveur de la famille ne porte cet id (§2.2).
2. **Le client 7.3 ne reçoit jamais `4008` non plus.** La table du répartiteur entrant envoie
   `4008` vers la branche « message non traité » (§2.3).
3. **Le client ne nomme pas l'id.** Sa table id → nom ne contient aucune chaîne
   `TM_CS/SC_HUNTAHOLIC_*` (§2.4) — indice corroborant, pas preuve à lui seul.
4. **Décision : `4008` reste absent de `GamePackets`**, sans bras de dispatch, sans handler, sans
   réponse. C'est la règle déjà appliquée à `711` (`GamePackets.cs:124-126`). Un éventuel `4008`
   entrant est journalisé en `Debug` par le garde `DefinedPackets` et la boucle de réception survit
   (§5.2).
5. **Aucun gating de version à trancher** : `X(4008, true)` dans rzu, aucun champ (§4).
6. **Taille du paquet : 7 octets**, en-tête seul, aucun champ (§3).
7. **Le relevé des trames sans charge utile du client 7.3 donne sept sites à id immédiat** — `23`,
   `27`, `1100`, `4005`, `4011`, `6000`, `6008` — et `4008` n'en fait pas partie. Un second idiome
   (douze sites, id non relevé) ne peut pas non plus porter `4008` (§2.2 bis).
8. **Le geste « retour au lobby » du client passe par `TM_CS_RETURN_LOBBY (23)`**, pas par `4005`, pas
   par `TM_CS_CHANGE_LOCATION`, pas par `4008` : commande locale `returnlobby` → gestionnaire
   `0x4a10c0` → trame de 7 octets d'id `0x17` (§2.5). `23` est déjà déclaré et routé dans NavisLamia
   (`GamePackets.cs:164`, `GameClient.cs:1753-1756`) : le §7d du socle est clos de ce côté.

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id décimal | `4008` | `op_codes.md:234` |
| id hexadécimal | `0x0FA8` | calculé (`4008 = 0xFA8`) |
| nom | `TM_CS_HUNTAHOLIC_LEAVE_LOBBY` | `op_codes.md:234` |
| sens | client → serveur | rzu `TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:10` (`SessionPacketOrigin::Client`) |
| famille | HuntaHolic `4000`–`4012` (17 opcodes) | `docs/packet-specs/socle-instances-jeu.md:236` (§3.3.5), `:50` |
| rzu | `X(4008, true)`, `DEF(_)` vide | `reference/rzu/librzu/src/packets/GameClient/TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:5,7-8` |
| NGemity | `TS_CS_HUNTAHOLIC_LEAVE_LOBBY = 4008`, `DEF(_)` vide | `reference/ngemity/shared/Server/ClientPackets.h:241` ; `shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:6,8` |
| déclaré dans NavisLamia ? | **non** (décision §5.2) | `Game/Network/Packets/Enums/GamePackets.cs:124-126` (règle du précédent `711`) |

`op_codes.md` est une liste d'ids héritée du projet, pas une preuve d'usage : il liste aussi `711`,
délibérément absent du dépôt pour la même raison (`GamePackets.cs:125-126`).

## 2. Ce que le joueur fait pour que le client l'envoie

**Rien.** Aucun chemin de code de `SFrame.exe` (Epic 7.3) ne construit un paquet d'id `4008`, et
aucun répartiteur entrant ne le traite. La fenêtre de lobby existe pourtant bien dans le client
(§2.5) : c'est bien l'id `4008` qui est mort, pas la notion de lobby.

Méthode : lecture statique de `SFrame.exe` (`sha256 41e0af2e…`, §8), désassemblage `objdump -d -M
intel` du `.text` (`VMA 0x401000`–`0xa0b8cf`), extraction des tables par adressage VMA → offset de
fichier (`file_off = 0x400 + (vma - 0x401000)`). Commandes reproductibles au §9.

### 2.1 Scan exhaustif de l'immédiat `0xfa8` — 1 seule occurrence, non significative

```text
899673:  67902f:  mov DWORD PTR [ebp-0xfa8],ebx
```

`0x67902f` écrit une variable locale dans le cadre de pile de la fonction englobante : c'est un
déplacement (`[ebp-0xfa8]`), pas un immédiat d'id. **Aucun site n'écrit `0xfa8` comme id.**

Contrôles positifs de la méthode (mêmes formes de recherche, tailles voisines de la famille) :

| Immédiat | Sites | Attribution |
| --- | --- | --- |
| `0xfa0` (=4000) | 1 site de construction, `0x4c9205` | constructeur `0x4c91f0` (§2.2) |
| `0xfa3` (=4003) | 1 site de construction, `0x563c48` | constructeur `0x563c20` (§2.2) |
| `0xfa4` (=4004) | 1 site de construction, `0x4c91c1` | constructeur `0x4c9190` (§2.2) |
| `0xfa5` (=4005) | 3 sites : `0x4c9392` (constructeur), `0x8b9e21`, `0x9a839d` | 2 sites non-paquets (tables de messages Win32) |
| `0xfab` (=4011) | 1 site de construction, `0x4c93e2` | constructeur `0x4c93d0` (§2.2) |
| **`0xfa8` (=4008)** | **1 site : `0x67902f`, déplacement de pile** | **aucun constructeur** |

Le `4005` de la famille est donc tracé trois fois et le `4008` zéro fois : le relevé négatif n'est
pas un artefact de recherche. La forme « `push 0x…` » est également couverte (elle produit des
sites parasites dans les tables de messages Win32, `0x8b9xxx` / `0x9a8xxx`), et aucun ne concerne
`0xfa8`.

### 2.2 Les cinq constructeurs client → serveur de la famille — aucun pour `4008`

Chaque constructeur écrit son id et sa longueur en dur ; on les identifie par leur immédiat d'id,
puis on relève leurs appelants (`call <constructeur>`) :

| Id | Constructeur | Longueur écrite | Sites d'appel (émetteurs) |
| --- | --- | --- | --- |
| `4000` `TM_CS_HUNTAHOLIC_INSTANCE_LIST` | `0x4c91f0` (`mov eax,0xfa0` en `0x4c9205`) | `0xb` = 11 | `0x4c97f8`, `0x564d7f`, `0x565c84` |
| `4003` `TM_CS_HUNTAHOLIC_CREATE_INSTANCE` | `0x563c20` (`mov eax,0xfa3` en `0x563c48`) | `0x38` = 56 | `0x563e5d` |
| `4004` `TM_CS_HUNTAHOLIC_JOIN_INSTANCE` | `0x4c9190` (`mov ecx,0xfa4` en `0x4c91c1`) | `0x1c` = 28 | `0x4c933c`, `0x4c945a` |
| `4005` `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE` | `0x4c9380` | 7 | `0x5641f2`, `0x567453` (fiche 4005 §2.2-2.3) |
| `4011` `TM_CS_HUNTAHOLIC_BEGIN_HUNTING` | `0x4c93d0` | 7 | `0x564238` |
| **`4008` `TM_CS_HUNTAHOLIC_LEAVE_LOBBY`** | **aucun** | — | **aucun** |

Les longueurs ci-dessus recoupent indépendamment `CLAUDE.md:1967-1968` (11 / 56 / 28 / 7 / 7) — ce
qui valide l'attribution des constructeurs, et confirme que la famille client → serveur n'a que
cinq producteurs en 7.3, aucun n'étant `4008`.

### 2.2 bis Relevé des trames « en-tête seul » à id immédiat — `4008` n'y est pas

Toutes les trames client → serveur sans charge utile s'écrivent dans le client avec le même idiome :
`mov <reg>,<id>` puis `mov WORD PTR [ebp-0xM],<reg16>` (id, offsets 4-5) et
`mov DWORD PTR [ebp-0x(M+4)],0x7` (longueur, offsets 0-3), suivis de la boucle de somme de contrôle.
Le balayage de cet idiome sur tout le `.text` donne la liste **complète des trames de 7 octets émises
par le client 7.3 dans cet idiome** (id immédiat dans les 5 instructions qui suivent) :

| Id | Nom (`op_codes.md`) | Site du constructeur | Émetteur relevé |
| --- | --- | --- | --- |
| `23` | `TM_CS_RETURN_LOBBY` (`:24`) | `0x48d352` | commande locale `returnlobby` (§2.5) |
| `27` | `TM_CS_LOGOUT` (`:28`) | `0x48d302` | fonction voisine du même bloc |
| `1100` | `TM_CS_GAME_TIME` (`:196`) | `0x493340` | — |
| `4005` | `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE` | `0x4c9392` | `0x5641f2`, `0x567453` |
| `4011` | `TM_CS_HUNTAHOLIC_BEGIN_HUNTING` | `0x4c93e2` | `0x564238` |
| `6000` | `TM_CS_REQUEST_FARM_INFO` (`:256`) | `0x610a02` | — |
| `6008` | `TM_CS_REQUEST_FARM_MARKET` (`:264`) | `0x610aa2` | — |

**Sept ids, et `4008` n'en fait pas partie.** Ce second relevé, indépendant du §2.1 par sa méthode,
confirme `4005` et `4011` comme trames en-tête seul (il recoupe donc la fiche 4005) et donne la
frontière exacte de la famille : sur les six paquets client → serveur de la famille HuntaHolic, seuls
`4005` et `4011` sont en-tête seul, et `4000`/`4003`/`4004` portent une charge utile.

**Limite de ce relevé, nommée pour ne pas être surinterprétée.** Il existe un **second idiome** de
construction de trame de 7 octets, où la longueur est écrite dans un tampon passé en registre
(`mov DWORD PTR [reg],0x7` puis `mov WORD PTR [reg+0x4],<reg16>`) et où l'id est chargé plus tôt —
par exemple `0x48c4e8` / `0x48c50d`, avec l'id `0x109b` = `4251` (`TM_CS_INSTANCE_GAME_EXIT`) chargé
en `0x48c502`. Ce second idiome compte **douze sites** dont **l'id n'a pas été relevé
exhaustivement** (le balayage automatique n'a résolu les ids que pour l'idiome ci-dessus). Il ne peut
cependant pas porter `4008` : l'id de ces trames est lui aussi un **immédiat**, et `0xfa8` n'apparaît
nulle part dans le `.text` (§2.1). La conclusion ne dépend donc pas de l'exhaustivité de ce second
relevé, et la question laissée ouverte est consignée en `NON ÉTABLI` (g).

### 2.3 Le répartiteur entrant du client route `4008` vers « message non traité »

Le répartiteur des messages serveur → client (`0x67e1d9`, arbre de décision) atteint le bloc
d'intervalle en `0x67e6e6` :

```text
67e6e6: cmp  eax,0xfa1        ; 4001 ?
67e6eb: jg   0x67e736         ; id > 4001 : bloc d'intervalle
67e6ed: je   0x67e729         ; 4001 -> call 0x6707f0
...
67e736: sub  eax,0xfa2        ; base 4002
67e73b: cmp  eax,0xfb         ; 251 ids : 4002..4252
67e740: ja   0x67ef21         ; hors intervalle -> défaut
67e746: movzx eax,BYTE PTR [eax+0x67f580]   ; table d'octets 0x67f580
67e74d: jmp  DWORD PTR [eax*4+0x67f560]     ; table de sauts 0x67f560 (8 entrées)
```

Contenu relevé (lecture directe des deux tables, `n = 251` octets lus depuis `0x67f580`) :

| Entrée | Id | Octet | Cible |
| --- | --- | --- | --- |
| 0 | `4002` | 0 | `0x67e754` |
| 4 | `4006` | 1 | `0x67e761` |
| 5 | `4007` | 2 | `0x67e76e` |
| 7 | `4009` | 3 | `0x67e77b` |
| 8 | `4010` | 4 | `0x67e788` |
| 10 | `4012` | 5 | `0x67e795` |
| 251 | `4253` | 6 | `0x67e7a2` |
| **3 / 2 / 1 / 6 / 9** | `4005` / `4004` / `4003` / **`4008`** / `4011` | **7** | **`0x67ef21` (défaut)** |

Les **seules** entrées qui ne valent pas 7 sont exactement les six messages serveur → client de la
famille (`4002`, `4006`, `4007`, `4009`, `4010`, `4012`) plus `4253`. `0x67ef21` est la branche
« message non traité » : elle affiche la chaîne de `0xa53df0`
(`"처리되지 않은 메세지 : %d"`, « message non traité : %d ») puis rejoint l'épilogue commun
`0x67ef39`. **Deux conclusions** : le client ne traite aucun message `4008` entrant, et il ne
connaît aucun bras pour les cinq ids client → serveur de la famille — ce qui est cohérent (ils ne
sont jamais reçus par un client).

### 2.4 La table id → nom du client ne connaît pas la famille

Le client construit une table id → nom de paquet par paires `push <chaîne>` / `mov eax,<id>`
(exemple vérifié : `0xa534b4` = `"TM_CS_SUMMON"` pour `mov eax,0x130` en `0x6772a4`/`0x6772c6`,
puis `0xa534c4` = `"TM_EQUIP_SUMMON"` pour `0x12f` en `0x677242`/`0x677264`).

* `strings -a SFrame.exe | grep -cE "^TM_[A-Z]"` → **173** entrées.
* `strings -a SFrame.exe | grep -E "^TM_" | grep -ci huntaholic` → **0**.
* `strings -a SFrame.exe | grep -ciE "leave_lobby"` → **0**.

Aucun id de la famille HuntaHolic n'y figure — **pas même `4005`, que le client émet pourtant**
(§2.2). La table est donc partielle : **ne pas s'en servir comme preuve**. Elle est citée ici parce
que le précédent `304` du dépôt (`GamePackets.cs:74-79`) distingue précisément « le client nomme
l'id sans le construire » (→ déclaré et routé) de « le client ne le connaît ni ne le construit »
(→ absent, comme `711`). `4008` est dans le second cas, mais ce sont le §2.1 et le §2.3 qui le
prouvent.

### 2.5 Ce qui existe quand même : la fenêtre de lobby

| Élément | Adresse / source |
| --- | --- |
| classe de fenêtre `SUIHuntaHolicLobbyWnd` (RTTI) | `strings -a SFrame.exe \| grep -E "^\\.\\?AV"` → `.?AVSUIHuntaHolicLobbyWnd@@` |
| enregistrement de la fenêtre | `push 0xa49898` (`"Create: SUIHuntaHolicLobbyWnd"`) en `0x6364e3` |
| objet de jeu `SHuntaHolicSystem` | `.?AVSHuntaHolicSystem@@`, `.?AV?$SMsgMapper@VSHuntaHolicSystem@@@@` |
| objet `huntaholic` | chaîne `0xa1fa18`, lue entre autres en `0x5641e6`, `0x56422c`, `0x5648ae` |
| autres fenêtres de la famille | `SUIHuntaHolicInstanceWnd` (`0x636690`), `SUIHuntaHolicCreateInstanceWnd` (`0x636572`), `SUIHuntaHolicScoreBoardWnd` (fiche 4005 §2.3) |

Le joueur peut donc ouvrir un lobby HuntaHolic ; les gestes de cette famille émettent `4000`,
`4003`, `4004`, `4005` ou `4011` selon la fenêtre (§2.2). **Aucun geste n'émet `4008`.**

Le client porte en revanche une **commande locale `returnlobby`** — un nom qui dit exactement
« retour au lobby ». On la trouve à la fois dans `db_localcommand.rdb`
(`strings -a db_localcommand.rdb | grep -i lobby` → ` returnlobby`) et dans le pool de noms du
binaire (`.rdata 0xa1f27c`). Ce nom est visé par **une seule** entrée d'une table de commandes en
`.data` (structure de 20 octets par entrée, champ 0 = `char*` nom, champ 1 = gestionnaire `.text`) :

| Entrée `.data` | `char*` nom | Gestionnaire `.text` |
| --- | --- | --- |
| `0xc122d4` | `0xa1f288` = `"help"` | `0x4a1070` |
| `0xc122e8` | `0xa1f27c` = **`"returnlobby"`** | **`0x4a10c0`** |
| `0xc122fc` | `0xa1f270` = `"rpcreate"` | `0x4a1100` |

Le gestionnaire `0x4a10c0` appelle `0x48d340` (`0x4a10cc`), qui **construit une trame de 7 octets** :
`mov eax,0x17` (`0x48d352`), id écrit aux offsets 4-5 (`0x48d35a`), longueur `0x7` aux offsets 0-3
(`0x48d35e`), somme de contrôle en offset 6 (`0x48d37a`), puis émission par l'entrée `0x1cc` de la
table virtuelle de la session (`0x48d38f`). **`0x17` = 23 = `TM_CS_RETURN_LOBBY`** (`op_codes.md:24`),
que NavisLamia **déclare et route déjà** (`GamePackets.cs:164`, `GameClient.cs:1753-1756`) ; le
parcours est déjà documenté (`docs/character-bootstrap.md:95-109`).

Autrement dit, le geste « retour au lobby » du client 7.3 passe par **`TM_CS_RETURN_LOBBY (23)`**,
une trame en-tête seul de 7 octets — **pas par `4005`, pas par `TM_CS_CHANGE_LOCATION`, pas par
`4008`**, ce qui répond à la question ouverte du §7d du socle. Ce qui reste non établi est plus
étroit : que le contrôle « quitter » de `SUIHuntaHolicLobbyWnd` appelle précisément cette commande —
voir `NON ÉTABLI` (a), §7.

Les données du client confirment que le **cycle de vie du lobby est piloté par le serveur**, pas
demandé par le joueur : `db_string.rdb` porte les gabarits des annonces serveur → client (« In
#@min@# minute(s), the #@huntaholic_dungeon_name@# lobby will be closed. », « The
#@huntaholic_dungeon_name@# lobby is now open for the next #@min@# minutes. », « The entrance to the
#@huntaholic_dungeon_name@# is now closed. It will re-open shortly. », « You have failed to join the
adventure to #@huntaholic_dungeon_name@#. ») : le client ne fait que les afficher. Les mêmes données
nomment `huntaholic_lobby_menu()` et trois points de contact PNJ
(`NPC_huntaholic_koreagarlic_contact()`, `NPC_huntaholic_sseulgae_contact()`,
`NPC_huntaholic_woongdam_contact()`) — des scripts, pas des paquets. `leave_lobby` n'apparaît nulle
part, et `db_scriptstring.rdb` — le fichier que le §7d du socle proposait d'observer — ne contient ni
`huntaholic` ni `lobby`.

### 2.6 Portée du relevé négatif

Le relevé d'immédiats porte sur `SFrame.exe`, seul binaire exécutable du dump : c'est de là que
sortent les cinq constructeurs de trames de la famille (§2.2), chacun avec son id en dur, et le
répartiteur entrant (§2.3). Ce qui n'est **pas** couvert par ce relevé, nommé pour ne pas être
surinterprété :

* `reference/client73/` ne contient que `SFrame.exe`, `data.000`, `extraction-manifest.json` et 50
  fichiers `.rdb` : les mises en page `.nui` et les scripts `.lua` (`weather_script.lua`,
  `environment_script.lua`, `CreatureState_Script.lua`, `Location_Script.lua`, `FX_Script.lua`,
  `UIWindows_addon_script.lua`, tous nommés dans l'exécutable) **ne sont pas extraits de
  `data.000`**. L'exécutable embarque bien Lua 5.0.2 (`lib\Lua\LuaVM.cpp : LuaVM::RunFile`,
  `LuaVM::RegisterFunction`, `Lua 5.0.2 Copyright (C) 1994-2004 Tecgraf, PUC-Rio`). Si un script
  appelait une fonction C++ prenant un **id de paquet en argument**, l'id ne serait pas un immédiat
  et échapperait au §2.1. **Aucun indice d'un tel point d'entrée n'a été trouvé** : aucune chaîne de
  type `SendPacket` / `send_msg` exposée dans le binaire, et les seules fonctions de script liées au
  lobby sont `huntaholic_lobby_menu()` et les trois contacts PNJ, qui sont des scripts de PNJ côté
  données (§2.5). Voir `NON ÉTABLI` (f).
* les `.rdb` du dump : `grep -ail "huntaholic" *.rdb` → `db_string.rdb`, `db_fieldpropresource.rdb`
  et `db_npcresource.rdb` (noms de ressources, gabarits d'annonces, fonctions de script, §2.5) ;
  `grep -ail "leave_lobby" *.rdb` → **aucun**. Aucun `.rdb` ne porte d'id `4008`.

Conséquence pour la décision : l'absence de tout immédiat `0xfa8` dans le `.text` prouve qu'**aucun
chemin de code du client 7.3 ne construit ce paquet**. La réserve ci-dessus porte sur un éventuel
déclencheur **données / Lua**, pas sur l'id lui-même, et elle n'affaiblit pas la décision du §5.2 :
un `4008` entrant est de toute façon absorbé par le garde `DefinedPackets`.

## 3. Structure sur le fil — **7 octets**

| Offset | Type | Nom | Valeur observée | Source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | `7` | rzu `MessageBuffer.h:73-76` (`writeHeader(size, id)` : `size` puis `id` puis `msg_checksum`) ; côté NavisLamia `Game/Network/Clients/GameClient.cs` (lecture de l'en-tête) et `Tests/Game/ReceiveGuardTests.cs:80-87` |
| 4 | `uint16` LE | `ID` | `0x0FA8` = `4008` | rzu `TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:7-8` ; NGemity `TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:8` |
| 6 | `uint8` | `msg_checksum` | calculé | rzu `MessageBuffer.h:73-76` ; `StorageTestHarness.Checksum` côté tests |
| — | — | *charge utile* | **aucun octet** | rzu `TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:5` (`DEF(_)` vide) ; NGemity `TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:6` (vide) |

**Taille totale attendue : 7 octets.** Base de l'en-tête = 7 (`CREATE_PACKET_VER_ID` passe `7` à
`CREATE_STRUCT_IMPL`, rzu `PacketDeclaration.h:616-621` ; NGemity `CREATE_PACKET(…, 7)`,
`shared/Server/Packets/PacketDeclaration.h:576`) + 0 octet de définition = 7. Recoupé par
`socle-instances-jeu.md:50` (« **7 o** (rzu seul) »), `:236` (§3.3.5) et `CLAUDE.md:1968`
(« … `4005` = 7, `4006` = 48, `4007` = 15, `4008` = 7 … »).

Le client 7.3 ne fournit ici **aucune confirmation de taille** (aucun constructeur, §2.2) : la
taille de 7 vient de rzu seul, NGemity la confirmant par un `DEF` vide, mais NGemity ne compile pas
cette version. Aucun champ : il n'y a donc aucun offset de charge utile à verrouiller, seulement la
taille de l'en-tête.

## 4. Gating de version — tranché pour Epic 7.3

| Champ | Gating rzu | Décision pour 7.3 |
| --- | --- | --- |
| l'id lui-même | `TS_CS_HUNTAHOLIC_LEAVE_LOBBY_ID(X)` = `X(4008, true)` (`:7-8`) | **`4008`, inconditionnel** |
| charge utile | aucun champ | **rien à trancher** |

* `X(4008, true)` est développé par `SERIALISATION_F_ID2(id_, condition_)` →
  `if(condition_) id = id_;` (rzu `PacketDeclaration.h:587-589`) : la condition littérale est `true`,
  donc l'id vaut `4008` pour **toutes** les versions. Aucune borne `>= EPIC_*` à statuer.
* Le fichier rzu ne porte **aucun** commentaire `// Since EPIC_*`, contrairement à ses voisins
  (`TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:7`, `TS_SC_HUNTAHOLIC_BEGIN_COUNTDOWN.h:7` portent
  `// Since EPIC_6_3`) : la famille a un plancher `EPIC_6_3` (`0x060300`, `PacketEpics.h:56`), donc
  `4008` est bien dans le domaine d'Epic 7.3 (`0x070300`, `PacketEpics.h:59`).
* Aucun remap : il n'existe pas de second id pour ce paquet dans le corpus, et
  `packet_ids_list<…>::getLatest()` (`PacketDeclaration.h:591-600`) rend `4008`.
* **Aucune réserve de version** ne reste ouverte pour cette fiche.

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` (NGemity) fait du paquet : rien

`4008` est **déclaré** (`shared/Server/ClientPackets.h:241`, classe `CREATE_PACKET(…, 4008)` en
`shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:8`) mais **jamais traité** :
`grep -rn "TS_CS_HUNTAHOLIC\|TS_SC_HUNTAHOLIC" reference/ngemity/Chihiro/src/ | wc -l` → **0** et
`grep -rn "4008" reference/ngemity/Chihiro/src/ | wc -l` → **0**. Le mot `HUNTAHOLIC` y apparaît 15
fois, mais uniquement comme identifiants de **logique de jeu** (compétence
`WARP_TO_HUNTAHOLIC_LOBBY` en `Skills/Skill.cpp:1422-1423`, type de groupe
`PartyType::HUNTAHOLIC_PARTY`, état, drapeaux d'objet, résurrection de monstre) — aucun n'est un nom
de paquet ni une classe de message.
Aucun id de la famille n'est enregistré dans `worldPacketHandler`, donc un `4008` entrant tombe dans
le chemin générique : la boucle de recherche ne trouve rien, `i == worldTableSize`, et
`NG_LOG_DEBUG("server.network", "Got unknown packet '%d' from '%s'", …)` est écrit puis la fonction
rend `ReadDataHandlerResult::Ok` (`Chihiro/src/Network/GameNetwork/WorldSession.cpp:158-163`).
Effet observable : **un journal, aucune réponse, aucune mutation, pas de déconnexion**.

NGemity compile `EPIC_4_1_1` (`shared/Common/Define.h:25`) : la famille HuntaHolic n'est pas dans sa
version cible, ses 17 opcodes y sont déclarés et non traités, et le bloc `Skill.cpp:1418-1433`
(`INSTANCE_GAME_ENTER`, `WARP_TO_HUNTAHOLIC_LOBBY`, `INSTANCE_GAME_EXIT`) est entièrement commenté
(`CLAUDE.md:1976-1978`). **Ne pas porter NGemity ici.**

### 5.2 Conduite NavisLamia — décision : **ne rien déclarer, ne rien répondre**

L'id reste **absent** de `Game/Network/Packets/Enums/GamePackets.cs`. Trois raisons, dans l'ordre de
force :

1. **Le précédent `711` est le même cas et il est écrit dans `master`** : `TM_CS_CHECK_BOOTH_STARTABLE
   (711)` est « deliberately absent — the 7.3 client neither knows it nor can send it »
   (`GamePackets.cs:124-126`). `4008` remplit les deux conditions, avec des preuves plus fortes
   (aucun immédiat `0xfa8` dans `.text`, `:124-126`).
2. **Le pendant est `304`** : `TM_CS_SUMMON` n'est émis par aucun client 7.3 mais le client *nomme*
   l'id (`GamePackets.cs:74-79`) ; il est donc déclaré, routé, et son bras se contente de lire la
   trame et de journaliser en `Debug`, sans rien répondre (`GameClient.cs:1337-1363`). `4008` n'a
   pas cette caractéristique (§2.4) : rien ne justifie un membre d'énum.
3. **Déclarer sans router serait faux** : tout membre de `GamePackets` qui n'a pas de bras atteint
   `_ => throw new Exception($"Unknown Packet Type {header.ID}")` (`GameClient.cs:1865`), ce qui
   **casse la boucle de réception** (`CLAUDE.md:2224-2227`). Un id mort ne mérite pas ce risque.

**Ce que le serveur fait déjà d'un `4008` entrant** — c'est le comportement attendu et il est en
place :

```csharp
// Game/Network/Clients/GameClient.cs:1269-1273
if (!DefinedPackets[header.ID])
{
    _logger.Debug("Undefined packet ID: {id} Length: {length}) received from {clientTag}", header.ID, header.Length, ClientTag);
    continue;
}
```

Journal `Debug`, `continue`, **pas de déconnexion, pas de réponse, aucune mutation d'état**. Un
serveur NavisLamia ne peut donc jamais voir sa boucle cassée par un `4008`, même si un client non
7.3 (client privé modifié, outil de test) en envoyait un : aucune action supplémentaire n'est
nécessaire.

**Aucune réponse** n'est à émettre : le socle est formel (« `4005` (quitter) / `4008` (quitter le
lobby) → aucun accusé nécessaire », `socle-instances-jeu.md:442`), et ni rzu ni NGemity ne définissent
de paquet de réponse pour `4008`. Aucun lobby HuntaHolic n'existe côté serveur dans NavisLamia, donc
il n'y a aucun état à faire évoluer.

**Et la sortie de lobby, elle, est déjà couverte** : le client l'exprime par `TM_CS_RETURN_LOBBY (23)`
(§2.5), id déclaré et routé dans le même `GameClient`. `4008` n'est donc pas un chaînon manquant : il
n'a pas de producteur, pas de consommateur, et son rôle supposé est tenu par `23`.

### 5.3 Contrat d'implémentation pour `navis-dev`

1. **Aucune modification de code de production** : pas de membre dans
   `Game/Network/Packets/Enums/GamePackets.cs`, pas de bras dans
   `Game/Network/Clients/GameClient.cs`, pas de fichier de lecture ni d'ajout à
   `Game/Network/Packets/Game/GameHuntaholicPackets.cs`. Aucun `SendResult`, aucune trame émise.
2. **Un verrou de non-régression**, dans le fichier de tests existant
   `Tests/Game/ReceiveGuardTests.cs` (le harnais y est déjà : `StorageTestHarness.FrameConnection`,
   `StorageTestHarness.NewGameClient`, `Frame(id, length)` en `:80-87`) :
   * `Enum.IsDefined(typeof(GamePackets), (ushort)4008).Should().BeFalse(…)` — précédent exact
     `SummonCardSkillListPacketsTests.cs:62` (id `1452`) et `SummonPacketsTests.cs:66-70` (id `1304`) ;
   * une trame brute de 7 octets d'id `4008` (`Frame(4008, 7)`) : `client.OnDataReceived(…)` **ne
     lève pas** et `connection.Sent` **reste vide** — c'est aussi le test d'offsets du paquet
     demandé par le critère transversal 3 (en-tête seul : `Length` `0`–`3` LE = 7, `ID` `4`–`5` LE =
     `0x0FA8`, `msg_checksum` `6`, **total 7 octets**, aucune charge utile).
3. **Critères transversaux** : `dotnet build Navislamia.sln -c Debug` code 0 ;
   `dotnet test Tests/Tests.csproj` code 0 avec le compte de tests **strictement supérieur** au
   compte de base de la branche ; aucun membre de `GamePackets` ne peut atteindre le `switch` final
   (inchangé) ; `git log --oneline origin/master..master` vide.
4. **`CLAUDE.md` n'est pas écrit par le dev** (fichier d'instructions protégé) : le bloc du §10 part
   dans la **description de la MR**.
5. **Ne pas toucher aux fichiers du lot `4005`** (`GamePackets.cs`, `GameClient.cs`,
   `GameHuntaholicPackets.cs`, `Tests/Game/HuntaholicLeaveInstancePacketsTests.cs`) : la décision
   « ne rien déclarer » annule toute surface de collision avec les branches 4000/4003/4004/4005.

### 5.4 Cas limites

* Un `4008` avec une longueur **supérieure** à 7 est absorbé par le garde `DefinedPackets` avant
  toute lecture de charge utile : aucun risque de désynchronisation pour ce lot.
* Une trame `4008` dont la longueur déclarée est **incohérente** est traitée par la validation
  générale de l'en-tête (et non par ce lot) : elle est refusée avant le garde.
* Le paquet n'a **aucun effet de jeu** : rien à tester côté état.

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | NGemity | NavisLamia | Pourquoi c'est assumé |
| --- | --- | --- | --- |
| déclaration de l'id | classe déclarée : `CREATE_PACKET(TS_CS_HUNTAHOLIC_LEAVE_LOBBY, 4008)` (`shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:8`), membre d'énum `ClientPackets.h:241` | **absent** de `GamePackets` (§5.2) | Effet observable identique : NGemity n'enregistre aucun handler pour `4008`, donc le paquet finit en `NG_LOG_DEBUG("Got unknown packet")` (`WorldSession.cpp:158-163`) ; NavisLamia journalise `Undefined packet ID` (`GameClient.cs:1269-1273`). La différence est structurelle : NGemity a une fabrique par classe et peut déclarer une classe sans handler ; NavisLamia n'a **pas** de table de paquets inconnus, un membre d'énum sans bras atteint le `throw` final (`GameClient.cs:1865`), donc on ne déclare que ce qui est routé. |
| version de référence | `EPIC_4_1_1` (`shared/Common/Define.h:25`) | `EPIC_7_3` | La famille HuntaHolic n'existe pas chez NGemity (§5.1) : ses 17 opcodes y sont déclarés et jamais traités (`CLAUDE.md:1976-1978`). NGemity ne peut pas trancher ce paquet, il confirme seulement la taille (7 o). |
| emplacement de la classe | `shared/Server/Packets/GameClient/…` (le dossier est partagé serveur/client) | `Game/Network/Packets/…` | Écart de rangement du dépôt, sans effet sur le fil. |

**Divergence interne à assumer explicitement.** La fiche `docs/packet-specs/4005-huntaholic-leave-instance.md`
(branche `hermes/packet-4005-huntaholic-leave-instance`, non mergée) conclut elle aussi « `4008` n'est
jamais émis ni reçu par le client 7.3 : ne rien implémenter pour cet id » (`:517-518`), en s'appuyant
sur l'absence de trace client constatée par le socle. La présente fiche **converge sur la
conclusion** mais la fonde sur un relevé exhaustif indépendant (§2.1 à §2.3) et sur la règle de
maison écrite pour `711` (§5.2). Aucun conflit de fichiers entre les deux lots : le lot 4008 ne
touche ni `GamePackets.cs` ni `GameClient.cs` ni `GameHuntaholicPackets.cs`, que le lot 4005 modifie
déjà. Le §2.5 renforce la conclusion de la fiche 4005 : la sortie du lobby n'est pas dans la famille
HuntaHolic du tout, elle passe par `TM_CS_RETURN_LOBBY (23)`, un id déjà en place.

## 7. `NON ÉTABLI`

**(a) Quel contrôle de `SUIHuntaHolicLobbyWnd` déclenche la commande `returnlobby`.** Le fait central
est désormais établi (§2.5) : le geste « retour au lobby » du client 7.3 est la commande locale
`returnlobby` (`.rdata 0xa1f27c`, table `.data 0xc122e8`, gestionnaire `0x4a10c0`), qui émet
`TM_CS_RETURN_LOBBY (23)` — une trame en-tête seul de 7 octets, **pas `4005`, pas `4008`**. Reste
ouvert : *quel contrôle de la fenêtre `SUIHuntaHolicLobbyWnd` (enregistrement `0x6364e3`) invoque
cette commande, et le fait-elle même ?* La liaison contrôle → commande demande les mises en page
`.nui`, **absentes du dump** (`reference/client73` ne contient que `SFrame.exe`, `data.000`,
`extraction-manifest.json` et 50 `.rdb` ; les `.nui` sont dans l'archive `data.000`, non extraite).
Ce point n'a **aucun** effet sur la décision du §5.2 : ce qui est prouvé, c'est que la sortie du lobby
passe par `23` et que `4008` n'est émis par personne.

**(b) Le rôle exact de `huntaholic_scoreboard` (`0xa2cac8`) et l'enchaînement lobby → tableau des
scores.** Déjà ouvert par la fiche 4005 §7(f) ; inchangé ici.

**(c) Ce que le serveur officiel répondait à un `4008`.** Aucune référence ne l'implémente (§5.1) et
aucune trace serveur n'est disponible ; c'est la même question que le point `(j)` du socle. La
conduite retenue (aucune réponse) est celle qui découle de l'absence de tout paquet de réponse dans
la famille, pas une reconstitution du serveur officiel.

**(d) Portée de la table id → nom du client.** Elle est partielle (`4005` y est absent alors que le
client l'émet, §2.4) : l'absence de nom pour `4008` **ne peut pas servir de preuve à elle seule**.
Elle n'est citée qu'en corroboration ; la preuve retenue est le relevé d'immédiats (§2.1) et la table
du répartiteur (§2.3). Une preuve indépendante supplémentaire demanderait une session client
instrumentée — hors de portée sur ce VPS (aucune exécution de client, `SFrame.exe` compris).

**(e) Déclencheur côté joueur d'un éventuel lobby sans instance.** Non investigué : sans état de
lobby côté serveur NavisLamia, la question n'a pas d'effet sur ce lot et reste à traiter avec le lot
« instance HuntaHolic » (socle §5.4).

**(f) Route « données / Lua » du relevé négatif.** Le §2.1 ne voit que les ids codés en dur dans le
`.text`. Si un script `.lua` ou une mise en page `.nui` — non extraits de `data.000` — appelait une
fonction C++ prenant un **id de paquet en paramètre**, cet id échapperait au relevé. Aucun tel point
d'entrée n'a été trouvé (§2.6) et l'exécutable n'expose aucune chaîne de type `SendPacket`, mais la
question n'est pas close par lecture seule. Formulation précise à trancher : *existe-t-il dans
`SFrame.exe` une fonction enregistrée via `LuaVM::RegisterFunction` qui émet un paquet dont l'id est
un argument ?* Cette réserve **n'a aucun effet sur la décision** du §5.2 — un `4008` entrant est
absorbé par le garde `DefinedPackets` quelle qu'en soit l'origine — et elle ne doit **pas** être
comblée par supposition.

**(g) Ids du second idiome de construction de trames de 7 octets.** Le §2.2 bis a relevé les sept
sites de l'idiome à id immédiat, mais les **douze sites** de l'idiome « tampon en registre »
(`mov DWORD PTR [reg],0x7` / `mov WORD PTR [reg+0x4],<reg16>`, id chargé plus tôt) n'ont vu leurs ids
relevés que par échantillon : `0x48c4e8`/`0x48c50d` → `0x109b` = `4251`
(`TM_CS_INSTANCE_GAME_EXIT`, chargé en `0x48c502`). Question précise à trancher : *quels ids portent
les onze autres sites, et existe-t-il un paquet client → serveur en-tête seul encore inconnu du
dépôt ?* Sans effet sur `4008` (aucun immédiat `0xfa8` dans le `.text`, §2.1) ; utile au socle
« instances de jeu » et aux lots `4250`-`4253` s'il s'avérait que `4250`/`4252` sont, eux aussi,
émis sous cette forme.

## 8. Commits et binaires épinglés

| Référence | Révision | Détail |
| --- | --- | --- |
| NavisLamia (`master`, base de la branche) | `b56967a07430422add88e0e5cdf292b41b18f6c6` | `Merge pull request #44 …` |
| `rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | `packets: fix TS_SC_INVENTORY with older epics` |
| `ngemity` (Chihiro) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | `Fix compilation issue for GCC`, 3 déc. 2025 |
| Client Epic 7.3 `SFrame.exe` | `sha256 41e0af2e…f9500e` (9 841 664 o) | `.text` VMA `0x401000`+`0x60a8cf`, `.rdata` `0xa0f000`, `.data` `0xc10000` |
| Dump client | `extraction-manifest.json` : `source_index_sha256 b88ac39a…4510adbf`, 83 822 entrées d'archive | 50 `.rdb` extraits, `data.000` |

## 9. Annexe — commandes reproductibles

Depuis `/srv/navislamia` (`SFrame.exe` est le client de référence, aucune exécution) :

```bash
# en-tête rzu
cat -n reference/rzu/librzu/src/packets/GameClient/TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h
# déclaration NGemity, et absence de traitement
cat -n reference/ngemity/shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h
grep -rn "TS_CS_HUNTAHOLIC\|TS_SC_HUNTAHOLIC" reference/ngemity/Chihiro/src/ | wc -l   # -> 0
# sections du binaire
objdump -h reference/client73/SFrame.exe
# relevé des immédiats de la famille (une passe, ~2,2 M lignes de désassemblage)
objdump -d -M intel --no-show-raw-insn --start-address=0x401000 --stop-address=0xa0b8cf \
  reference/client73/SFrame.exe > /tmp/dis_text.txt
grep -nE "(^|[^0-9a-fx])0xfa[0-9a-f]([^0-9a-f]|$)" /tmp/dis_text.txt    # 67 sites, aucun 0xfa8-id
# littéraux du client
strings -a reference/client73/SFrame.exe | grep -cE "^TM_[A-Z]"                       # -> 173
strings -a reference/client73/SFrame.exe | grep -iE "huntaholic"                      # 0 nom TM_
strings -a reference/client73/SFrame.exe | grep -i "lua" | sort -u                    # Lua 5.0.2 embarqué
# données du dump (mises en page .nui et scripts .lua non extraits de data.000)
grep -ail "huntaholic"   reference/client73/*.rdb   # db_fieldpropresource, db_npcresource, db_string
grep -ail "leave_lobby"  reference/client73/*.rdb   # (vide)
```

Les deux tables du répartiteur se lisent directement par adressage VMA → offset de fichier
(`file_off = 0x400 + (vma - 0x401000)`) : table d'octets `0x67f580` (251 octets), table de sauts
`0x67f560` (8 `dword`).

Le relevé du §2.2 bis se fait sur le même désassemblage, par motif : une ligne
`mov <reg>,0x<id>`, suivie dans les 5 instructions d'un `mov WORD PTR [ebp-0xM],<reg16>` (même
registre, version 16 bits — `eax` s'écrit `ax`) et d'un `mov DWORD PTR [ebp-0x(M+4)],0x7`. Sept ids
sortent, aucun n'étant `4008`. Les tables de commandes locales se lisent en `.data` (`0xc122e8` →
`0xa1f27c` → gestionnaire `0x4a10c0`, entrées de 20 octets).

## 10. Brouillon pour la description de la MR (`navis-dev`)

Bloc proposé pour `CLAUDE.md` — **le dev ne l'écrit pas dans `CLAUDE.md`** (Hermes refuse l'écriture
de ce fichier) : il le colle dans la description de la MR, comme l'exige le critère transversal 5.

```markdown
### Paquet 4008 — `TM_CS_HUNTAHOLIC_LEAVE_LOBBY`

* **7 octets, en-tête seul, aucun champ** (rzu `X(4008, true)`, `DEF(_)` vide,
  `TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:5,7-8` ; NGemity idem, `TS_CS_HUNTAHOLIC_LEAVE_LOBBY.h:6,8`).
* **Aucun gating de version** : `SERIALISATION_F_ID2` développe `true` en `if(true) id = id_`
  (rzu `PacketDeclaration.h:587-589`) ; aucun champ, donc rien à trancher pour 7.3.
* **Le client 7.3 n'émet jamais 4008** : l'immédiat `0xfa8` n'apparaît qu'une fois dans tout le
  `.text`, en `0x67902f`, comme déplacement de pile `[ebp-0xfa8]` — pas comme id. Les cinq
  constructeurs client → serveur de la famille portent 4000, 4003, 4004, 4005, 4011.
* **Le client 7.3 ne reçoit pas non plus 4008** : la table du répartiteur entrant (`0x67f580`,
  base 4002, 251 entrées) envoie 4008 à la branche « message non traité » (`0x67ef21`) ; seuls
  4002, 4006, 4007, 4009, 4010, 4012 et 4253 y ont un bras.
* **Le client ne nomme même pas l'id** : sa table id → nom (173 entrées `TM_*`) ne contient aucune
  chaîne `TM_CS/SC_HUNTAHOLIC_*`. Indice corroborant, pas preuve.
* **Décision : 4008 reste absent de `GamePackets`**, sans bras, sans handler, sans réponse — même
  règle que `711`, « the 7.3 client neither knows it nor can send it » (`GamePackets.cs:124-126`).
  Un 4008 entrant est journalisé `Undefined packet ID` en `Debug` et la boucle survit
  (`GameClient.cs:1269-1273`) : aucun risque si un client non 7.3 en envoie un.
* **Aucune réponse** (socle `socle-instances-jeu.md:442`), aucun état serveur à modifier.
* **La sortie du lobby passe par un autre id, déjà en place** : le client a une commande locale
  `returnlobby` (`.rdata 0xa1f27c`, table `.data 0xc122e8`, gestionnaire `0x4a10c0`) dont le code
  (`0x48d340`) émet une trame en-tête seul de 7 octets d'id **23 = `TM_CS_RETURN_LOBBY`**, déjà
  déclarée et routée (`GamePackets.cs:164`, `GameClient.cs:1753-1756`). Ni `4005`, ni
  `TM_CS_CHANGE_LOCATION`, ni `4008` : la question ouverte du §7d du socle est close de ce côté.
* **Balayage des trames en-tête seul du client 7.3** : sept sites à id immédiat — 23, 27, 1100,
  4005, 4011, 6000, 6008 — plus un second idiome (tampon en registre) de douze sites dont les ids
  n'ont pas été relevés. Aucun ne peut porter `4008` : l'immédiat `0xfa8` est absent du `.text`.
* Fiche : `docs/packet-specs/4008-huntaholic-leave-lobby.md`.
```

## A VERIFIER PAR KILLIAN

1. **État de la fiche (27/09/2026)** : cette fiche ne modifie **aucun** fichier de code ; elle est le
   seul livrable de l'archéologue. Le contrat du §5.3 demandé au lot `navis-dev` est **un test de
   non-régression, pas une implémentation** : si le principe « un paquet que le client 7.3 ne peut
   ni émettre ni recevoir ne mérite pas de membre d'énum » ne te convient pas, le lot doit au
   contraire déclarer `TM_CS_HUNTAHOLIC_LEAVE_LOBBY = 4008` **et** router l'id (journal `Debug`,
   aucune réponse) pour rester dans la règle du `CLAUDE.md:2224-2227`.
2. **Point d'arbitrage (a)** — §7(a) : le geste « retour au lobby » du client 7.3 est **établi** (commande
   locale `returnlobby` → `TM_CS_RETURN_LOBBY (23)`, §2.5), et `23` est déjà déclaré et routé côté
   NavisLamia. Reste à trancher, et seulement si tu le juges utile : quel contrôle de
   `SUIHuntaHolicLobbyWnd` l'invoque. Cela demande les mises en page `.nui`, absentes du dump. Ce
   point n'a **aucun** effet sur la décision de cette fiche.
3. **Report dans la fiche de socle** : `socle-instances-jeu.md` §7(d) (ligne `:550`) pose la question
   « `4008` est-il utilisé par le client 7.3 ? ». Elle est désormais close : **non**, ni en émission
   ni en réception, avec relevé exhaustif (§2) — et la seconde moitié de la question, « le quitter le
   lobby passe-t-il par `4005` ou par un `TM_CS_CHANGE_LOCATION` ? », reçoit elle aussi sa réponse :
   il passe par **`TM_CS_RETURN_LOBBY (23)`** (§2.5). Le report est un geste de PO sur `master`, pas
   de ce lot. La ligne `:19` du socle (« 4008 sans preuve client ») et la ligne `:50` (« 7 o (rzu
   seul) ») restent exactes.
4. **Collision** : aucune. Le lot 4008 ne touche ni `GamePackets.cs`, ni `GameClient.cs`, ni
   `GameHuntaholicPackets.cs`, ni `Tests/Game/HuntaholicLeaveInstancePacketsTests.cs` — c'est-à-dire
   exactement les fichiers que les branches 4000/4003/4004/4005 modifient. L'absence de déclaration
   annule le carrefour de fusion de la famille HuntaHolic annoncé par la fiche 4005.
5. **Réserve (f)** — §7(f) : le relevé d'immédiats ne couvre pas une éventuelle route
   données/Lua qui passerait un id en argument à une fonction C++. Aucun indice d'un tel point
   d'entrée n'existe dans `SFrame.exe`, et la réserve ne change rien à la décision ; elle est
   consignée pour ne pas être comblée par supposition.
