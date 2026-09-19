# Socle — zones d'événement du monde : `TM_CS_ENTER_EVENT_AREA` (15) et `TM_CS_LEAVE_EVENT_AREA` (16), Epic 7.3

Fiche d'archéologie de protocole, branche `hermes/packet-socle-zones-evenement`, auteur `navis-ref`.
Elle ne modifie aucun code du serveur : elle établit ce que les références disent, ce qu'elles ne
disent pas, et le découpage minimal que la carte d'implémentation doit livrer.

## 0. Base mesurée avant rédaction

Dépôt `/srv/navislamia/Navislamia`, branche créée depuis `master` à
`6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` (`git rev-parse master`), arbre propre avant création
(`git status --short` vide), `git log --oneline origin/master..master` vide.

    export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
    dotnet build Navislamia.sln -c Debug   -> exit 0 (160 avertissements, 0 erreur, 10,92 s)
    dotnet test Tests/Tests.csproj         -> exit 0 (366 réussis, 0 échec, 882 ms)

Les 366 tests sont le plancher : le dev de ce socle ne doit pas le baisser (critère transversal 2).

## 1. Identité et cadrage

| Élément | Valeur |
|---|---|
| Id entrant | `15` (décimal) — `TM_CS_ENTER_EVENT_AREA` |
| Id sortant | `16` (décimal) — `TM_CS_LEAVE_EVENT_AREA` |
| Source de l'identité | `op_codes.md:18` (`[15] = "TM_CS_ENTER_EVENT_AREA",`) et `op_codes.md:19` (`[16] = "TM_CS_LEAVE_EVENT_AREA",`) |
| Sens | client → serveur (les deux) : `SessionPacketOrigin::Client` dans rzu, `PacketOrigin::Client`/`_DEF` client dans NGemity |
| Taille sur le fil | **15 octets** (7 d'en-tête + 8 de charge) pour les deux |
| Version tranchée | Epic 7.3 (voir §4) |
| Émission par le client 7.3 | non établie — voir §10, item 1 |

`op_codes.md` est une table Lua `opcode_names` (`op_codes.md:1`) : c'est une source d'identité, pas de
taille ni d'ordre de champs. Les deux ids **ne sont pas** dans `GamePackets`
(`Game/Network/Packets/Enums/GamePackets.cs`) : l'énumération va de `TM_CS_MOVE_REQUEST = 5`
à `TM_CS_QUERY = 13` puis saute à `TM_CS_CHAT_REQUEST = 20` (`GamePackets.cs:11-18`) ; 15 et 16 sont
absents, de même que 14. Le socle doit donc créer ces deux membres (cf. §7).

### 1.1 Ce que la carte demande, et ce que la référence confirme

| Affirmation de la carte | Verdict |
|---|---|
| rzu ne porte pas de taille : c'est la structure des champs qui l'établit | **confirmé** — rzu ne donne que `_(simple)(int32_t, event_area_id)` / `_(simple)(int32_t, area_index)` (`reference/rzu/librzu/src/packets/GameClient/TS_CS_ENTER_EVENT_AREA.h:6-7`) |
| Le client charge lui-même les polygones de zone d'événement | **confirmé** — voir §2.2 |
| `EventAreaInfo` porte des champs (temps, niveaux, conditions, compteur, handlers) jamais renseignés | **confirmé** — `Game/Maps/Entities/EventAreaInfo.cs:14-26`, initialisés par défaut `EventAreaInfo.cs:33-46` |
| Le lecteur `.nfe` ne lit que l'id et les polygones | **confirmé** — `Game/Maps/MapService.cs:256-292` |
| `_eventAreaInfo` est chargée et jamais utilisée | **confirmé** — `MapService.cs:34` porte le commentaire « TODO: currently only updated but never used » |
| NGemity ne traite pas ces paquets | **confirmé** — voir §5.1 |

## 2. Ce que le joueur fait pour que le client envoie le paquet

### 2.1 L'action

Le joueur **franchit la frontière d'un polygone de zone d'événement** posé sur la carte : il entre
dans la zone (entrée) ou en sort (sortie). Il n'y a ni touche, ni fenêtre, ni dialogue : c'est un
franchissement de frontière, détecté par le client sur les polygones qu'il a chargés lui-même.

C'est la seule lecture compatible avec les trois éléments suivants, tous vérifiés dans le binaire du
client 7.3 (`/srv/navislamia/reference/client73/SFrame.exe`, sha256
`41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, format `pei-i386`).

### 2.2 Le client charge lui-même les polygones `.nfe`

Trois relevés indépendants, tous obtenus par lecture (`strings -t x`, en-têtes de sections lus par
`objdump -h`) :

| Relevé | Fichier:offset | VA | Section |
|---|---|---|---|
| chaîne `.nfe` | `SFrame.exe:0x677404` | `0xa78a04` | `.rdata` (`0xa0f000`+`0x2006a8`, fichier `0x60da00`) |
| nom de type `. ?AUEventAreaPolygon@CTerrainMapEngine@@` | `SFrame.exe:0x8207ec` | `0xc225ec` | `.data` (`0xc10000`+`0x38600`, fichier `0x80e200`) |
| noms de type `AUSMSG_ENTER_EVENTAREA` / `AUSMSG_LEAVE_EVENTAREA` | `0x81cf34` / `0x81cf58` | `0xc1ed34` / `0xc1ed58` | `.data` |

`CTerrainMapEngine::EventAreaPolygon` est une **structure imbriquée du moteur de terrain du client** :
le client possède donc bien sa propre représentation des polygones de zone d'événement, et l'extension
`.nfe` (celle que NavisLamia lit, `Game/Maps/Entities/TerrainSeamlessWorldInfo.cs:34`) est compilée
dans le binaire. La chaîne de noms `AUSMSG_*` compte 150 occurrences et `AUSIMSG_*` 199 : ces deux
familles sont la nomenclature normale des types du client, pas une chaîne isolée.

Contrôle de vivacité des deux types `AUSMSG_*_EVENTAREA` : leur descripteur de type (`0xc1ed2c` et
`0xc1ed50`) est référencé deux fois chacun dans `.rdata`
(`0x7c8d10`/`0x7c8d34` pour `ENTER`, `0x7c8d5c`/`0x7c8d80` pour `LEAVE`), avec une forme de structure
**identique** à celle d'une classe de fenêtre réelle du client prise comme témoin
(`.?AVSUIResurrectWnd@@`, descripteur `0xc1c54c`, références `0x7c42c8`/`0x7c42f8`). Autrement dit : ces
deux types sont compilés et vivants comme une classe de fenêtre, ils ne sont pas des chaînes mortes.

En revanche, cette notification « entrée / sortie de zone d'événement » est **interne au client**
(elle existe exactement comme `ENTER` et comme `LEAVE`) : elle ne prouve pas que le client émette 15
ou 16. C'est le point laissé `NON ÉTABLI` en §10 item 1.

### 2.3 Ce qu'on ne peut pas vérifier ici

- Aucun fichier `.nfe` n'existe sur le VPS (`find /srv/navislamia -iname '*.nfe'` : 0 résultat) : le
  format ne peut être confronté à une instance réelle, et aucune valeur de `event_area_id` ou
  `area_index` ne peut être observée.
- Le client ne peut pas être lancé (`SFrame.exe`, Lua et scripts du client interdits), donc aucun
  relevé réseau n'est possible : toutes les valeurs « observées » ci-dessous restent vides.

## 3. Structure sur le fil — 15 octets, pour les deux paquets

Les deux paquets ont **exactement la même structure** (`event_area_id` puis `area_index`), donc la même
taille. Convention du dépôt : l'en-tête fait 7 octets (`Game/Network/Packets/Header.cs:7-11` :
`uint32 Length`, `uint16 ID`, `uint8 Checksum`, `Pack=1`) et la longueur annoncée **inclut** l'en-tête
(`Game/Network/Packets/PacketExtensions.cs:13-25`, six additions `:17-22`, calcule le checksum sur les six premiers octets, donc
sur `Length`+`ID` ; `Tests/Game/MovePacketsTests.cs:11-18` vérifie `27` pour un en-tête + 20 octets de
charge).

| Offset | Taille | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` LE | `Length` | **15** | `Header.cs:9` ; longueur = 7 + 8 ; convention `Tests/Game/MovePacketsTests.cs:11-18` |
| 4 | 2 | `uint16` LE | `ID` | **15** (ENTER) / **16** (LEAVE) | `op_codes.md:18-19` ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_ENTER_EVENT_AREA.h:10-12` ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_LEAVE_EVENT_AREA.h:10-12` |
| 6 | 1 | `uint8` | `Checksum` | somme des octets 0..5 | `Header.cs:11` ; `PacketExtensions.cs:13-25` |
| 7 | 4 | `int32` LE | `event_area_id` | `NON ÉTABLI` (aucune capture possible) | `rzu/.../TS_CS_ENTER_EVENT_AREA.h:6`, `rzu/.../TS_CS_LEAVE_EVENT_AREA.h:6`, `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_ENTER_EVENT_AREA.h:7`, `.../TS_CS_LEAVE_EVENT_AREA.h:7` |
| 11 | 4 | `int32` LE | `area_index` | `NON ÉTABLI` (aucune capture possible) | `rzu/.../TS_CS_ENTER_EVENT_AREA.h:7`, `rzu/.../TS_CS_LEAVE_EVENT_AREA.h:7`, `ngemity/.../TS_CS_ENTER_EVENT_AREA.h:8`, `ngemity/.../TS_CS_LEAVE_EVENT_AREA.h:8` |

**Taille totale attendue : 15 octets.** Ni rzu ni NGemity ne portent de taille explicite : elles
s'établissent par les champs (`_(simple)` = petit-boutiste, sans alignement ni remplissage,
`reference/rzu/librzu/src/packets/Packet/PacketDeclaration.h`). Le client est le seul juge en dernier
ressort, et il rejette une taille fausse par désalignement silencieux : le test d'offsets du socle doit
donc asserter 15 **et** les quatre positions 0/4/6/7/11 (cf. §7).

### 3.1 Sémantique de `area_index` : deux lectures, aucune tranchée

Deux lectures restent possibles et **aucune n'est établie** par les références :

1. l'index du **polygone** dans la liste des polygones d'une même zone (`area_index` accompagnerait
   `event_area_id` comme `(id, index)`), ce que suggère le format `.nfe` lui-même, où une zone
   d'événement porte un nombre de polygones avant la liste de leurs points
   (`MapService.cs:269-271`) ;
2. l'index de la **zone dans le fichier** ou dans la carte (numérotation locale, non signifiante).

Contrainte NavisLamia à signaler au dev : `LoadEventAreaFile` écrit `_eventAreaInfo[eventAreaId] = ...`
**à l'intérieur** de la boucle des polygones (`MapService.cs:289`, boucle ouverte en `:272`) : pour une
zone à plusieurs polygones, seul le dernier survit. Si la lecture 1 était la bonne, la structure
actuelle ne permettrait pas de représenter la zone complète. Le socle ne doit donc **pas** interpréter
`area_index` au-delà d'un contrôle de forme (`>= 0`), et doit journaliser la valeur reçue pour qu'une
capture ultérieure tranche. Toute lecture plus ambitieuse serait une supposition.

## 4. Gating de version — tranché pour 7.3

rzu ne gate **que l'identifiant**, pas les champs : les deux champs sont déclarés sans condition
(`_(simple)(int32_t, event_area_id)` et `_(simple)(int32_t, area_index)`, `rzu/.../TS_CS_ENTER_EVENT_AREA.h:5-7`
et `rzu/.../TS_CS_LEAVE_EVENT_AREA.h:5-7`).

| Paquet | Macro de version (rzu) | Id pour 7.3 | Décision |
|---|---|---|---|
| `TS_CS_ENTER_EVENT_AREA` | `X(15, version < EPIC_9_6_3)` / `X(1015, version >= EPIC_9_6_3)` (`rzu/.../TS_CS_ENTER_EVENT_AREA.h:10-12`), commentaire `// Since EPIC_6_3` (`:9`) | **15** | branche `< EPIC_9_6_3` : 7.3 < 9.6.3, donc 15. `1015` est une borne ultérieure, sans objet ici. Retenu. |
| `TS_CS_LEAVE_EVENT_AREA` | `X(16, version < EPIC_9_6_3)` / `X(1016, version >= EPIC_9_6_3)` (`rzu/.../TS_CS_LEAVE_EVENT_AREA.h:10-12`), commentaire `// Since EPIC_7_3` (`:9`) | **16** | **`LEAVE` est bien actif en 7.3** : sa borne est `< EPIC_9_6_3`, et l'apparition annoncée est 7.3, pas 9.x. Retenu. Le piège aurait été de lire « Since EPIC_7_3 » comme une borne *supérieure*. |

Points de décision explicitement statués :

- **Aucun champ n'est gaté** par `>= EPIC_*` : il n'y a donc aucun champ dont il faudrait retirer ou
  ajouter la présence pour 7.3. Les deux champs sont présents en 7.3.
- Les ids `1015`/`1016` (branche `>= EPIC_9_6_3`) ne s'appliquent pas à 7.3 et **ne doivent pas** être
  acceptés par le serveur : NavisLamia n'a pas de mécanisme de gating par version dans
  `GamePackets` (énumération unique, `GamePackets.cs:1-90`), donc la décision 7.3 s'encode simplement en
  ajoutant 15 et 16.
- **NGemity n'est pas une autorité de version ici** : il compile en `EPIC_4_1_1`
  (`reference/ngemity/shared/Common/Define.h:25`) tout en déclarant `TS_CS_LEAVE_EVENT_AREA` avec le
  commentaire « Since EPIC_7_3 » (`ngemity/.../TS_CS_LEAVE_EVENT_AREA.h:10`) : l'en-tête déclare un
  paquet plus récent que la version que NGemity cible effectivement. C'est une incohérence interne de
  NGemity, sans conséquence pour nous : rzu tranche, et rzu est d'accord avec l'existence des deux ids
  en 7.3.
- NGemity déclare aussi 15/16 dans son énumération `ClientPackets.h:37-38`, ce qui est cohérent avec
  rzu sur les ids (ce que NGemity ne fournit pas, c'est le traitement, cf. §5.1).

## 5. Traitement attendu

### 5.1 Ce que NGemity fait : rien, et il le dit

- Les deux en-têtes sont présents (`ngemity/shared/Server/Packets/GameClient/TS_CS_ENTER_EVENT_AREA.h`,
  `.../TS_CS_LEAVE_EVENT_AREA.h`, inclus par `ngemity/shared/Server/XPacket.h:95` et `:119`) et les ids
  15/16 dans `ngemity/shared/Server/ClientPackets.h:37-38`.
- **Aucun gestionnaire** : la table `worldPacketHandler[]`
  (`reference/ngemity/Chihiro/src/Network/GameNetwork/WorldSession.cpp:92-137`) ne contient ni
  `onEnterEventArea` ni `onLeaveEventArea`, et la classe n'a aucune de ces méthodes
  (`Chihiro/src/Network/GameNetwork/WorldSession.h`) ; une recherche `EVENT_AREA` sur tout l'arbre
  `ngemity/` ne remonte que les en-têtes, `XPacket.h`, `ClientPackets.h` et `Database/Telecaster.sql`.
- Conséquence mécanique : ces ids tombent dans la branche « paquet inconnu »
  (`WorldSession.cpp:158-162`) — les 15 et 16 **ne sont pas** dans `ignoredPackets[]`
  (`WorldSession.cpp:140-141`, qui ne liste que `TS_CS_VERSION`, `TS_CS_VERSION2`, `TS_CS_UNKN`,
  `TS_CS_REPORT`, `TS_CS_TARGETING`) — donc NGemity journalise `Got unknown packet` en debug et
  **ne répond rien**. Le paquet est jeté, la session continue (`ReadDataHandlerResult::Ok`).
- Le seul apport de NGemity est **schéma de données**, pas logique : `Telecaster.EventAreaEnterCount`
  (`reference/ngemity/Database/Telecaster.sql:221-230`), clé primaire `(player_id, event_area_id)`, avec
  une colonne `enter_count`. C'est la trace d'un compteur **par personnage et par zone**, ce qui éclaire
  `LimitActivateCount` (§5.6).

### 5.2 Les accroches déjà présentes dans NavisLamia

| Accroche | Où | État |
|---|---|---|
| Dictionnaire des zones | `Game/Maps/MapService.cs:34` (`private static Dictionary<int, EventAreaInfo> _eventAreaInfo;`), initialisé `:59` | chargé, **jamais lu** (commentaire TODO dans le code), **aucun accesseur public** : ni `IMapService`, ni `MapService` n'expose quoi que ce soit |
| Chargement des polygones | `MapService.cs:153-163` (appel) et `:256-292` (lecture) | fonctionnel ; échelle `point.X = mapLength * x + point.X * attrLen` (`:285-286`) ; un `.nfe` absent est ignoré silencieusement (`:258-261`) |
| Nom de fichier `.nfe` | `Game/Maps/Entities/TerrainSeamlessWorldInfo.cs:34` (`EventAreaPolygonFileExt = ".nfe"`), `:220` (`GetEventAreaFileName`) | fonctionnel |
| Données par zone | `Game/Maps/Entities/EventAreaInfo.cs:6-26` (`Id`, `Area`, `BeginTime`, `EndTime`, `MinLevel`, `MaxLevel`, `RaceJobLimit`, `ActivateCondition[6]`, `ActivateValue[6][2]`, `LimitActivateCount`, `EnterHandler`, `LeaveHandler`) | déclarées, **jamais renseignées** : le constructeur les met à `0`/`""` (`:28-47`) |
| Prédicat d'activation | `EventAreaInfo.cs:10` (`IsActivatable(...) => false; // TODO`) | **renvoie faux en dur** |
| Nombre de conditions | `Game/Maps/Constants/EventAreaInfoConstants.cs:5` (`MaxActivateConditions = 6`) | constant |
| Test d'appartenance | `Game/Maps/X2D/PolygonF.cs:9` (classe), `:136` (`public bool Contains(PointF pt)`) | disponible, **non utilisé pour les zones** |
| Position du personnage | `Game/Network/Clients/ConnectionInfo.cs:104-106` (`X`, `Y`, `Z`, `float`), `:46` (`Layer`) | état de session |
| Mise à jour de la position | `Game/Network/Clients/GameClient.cs:117` (`HandleMoveRequest`), `:151` (`HandleRegionUpdate`), `:160` (`HandleChangeLocation`) | **la position de session vient du client** : `HandleMoveRequest` affecte `X`/`Y` (`:146-147`) depuis le paquet de déplacement, `HandleRegionUpdate` affecte `X`/`Y`/`Z` (`:154-156`) depuis le paquet de région |
| Position persistée | `Game/Services/CharacterService.cs:363-366` (`character.Position = new[] { (int)x, (int)y, 0 }`) | écrite lors de la sauvegarde/warp ; `CharacterEntity.Position` est un `int[3]` (`Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:27`) |
| Remise à zéro de session | `ConnectionInfo.cs:153` (`ClearCharacterSession`, `Layer` `:171`, `X/Y/Z` `:172-174`) ; `Game/Network/Clients/Client.cs:110-115` (`Dispose` remet `ConnectionInfo = null`, `:114`) | à étendre par le socle (nouvel état « zone courante ») |
| Compteur par personnage, précédent | `Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:52` (`HuntaholicEnterCount`) | précédent direct d'un compteur d'entrées persistant par personnage, mot de passe du vocabulaire « EnterCount » |
| Moteur de script | `Game/Scripting/ScriptService.cs:8` (`using MoonSharp.Interpreter;`), `:19` (`Script _luaVm = new()`), `:64` (`RunString`), `:75` (`_luaVm.DoString`) | Lua embarqué ; déjà utilisé pour exécuter une chaîne de script de carte (`MapService.cs:338`) |
| Dispatch des paquets | `GameClient.cs:468` (`OnDataReceived`), `:497` (`Enum.IsDefined`), `:509-668` (chaîne de `if`), `:670-682` (`switch` final, `_ => throw new Exception("Unknown Packet Type")` en `:681`) ; table `_actions` de `GameActions.cs:32`, alimentée `:43-50`, exécutée `:53-59` (un id non enregistré y est **ignoré silencieusement**) | deux chemins : `GameActions` (paquets de phase de connexion) et la chaîne de `if` de `GameClient` (paquets de jeu) |
| Parseurs d'aide | `Game/Network/Packets/Game/GameActionPackets.cs:8` (`HeaderSize = 7`), `:31-42` (modèle `TryRead…` avec contrôle de longueur) | modèle à suivre |

### 5.3 Décision : le paquet est un indice, le polygone est l'autorité

Aucune référence ne dit qui, du client ou du serveur, décide qu'un personnage est « dans » une zone.
La décision est donc prise ici, et elle est motivée par ce que NavisLamia est déjà :

1. **La position de session n'a qu'une source : le client** (`GameClient.cs:117`, `:151`, `:160`) et la
   position persistée est écrite depuis ces mêmes valeurs (`CharacterService.cs:363-366`). Le serveur
   n'a aucune source de position indépendante et ne valide pas les déplacements aujourd'hui : accorder
   à 15/16 la même confiance qu'aux paquets de déplacement n'ajoute **aucune** surface de confiance.
2. **Mais le polygone est déjà chargé côté serveur** (`_eventAreaInfo`, `MapService.cs:256-292`) et
   `PolygonF.IsIncluded` (`PolygonF.cs:156`) est le test d'appartenance du moteur. Le contrôle
   d'appartenance coûte un test de point dans un polygone et transforme un indice client en fait
   vérifié serveur. *Note de livraison* : `PolygonF.Contains` (`PolygonF.cs:136`), cité dans la
   première rédaction de cette fiche, n'est **pas** un test d'appartenance — c'est une comparaison à la
   liste des sommets (`_points.Any(t => t == pt)`), qui plus est dégradée en comparaison de références
   faute d'`operator==` sur `PointF`. Voir §15.

Décision retenue, à implémenter par le socle : **le serveur ne fait rien sur la seule foi du paquet.**
Il l'accepte comme déclencheur, puis :

- vérifie que `event_area_id` correspond à une zone réellement chargée pour la carte/layer courants :
  sinon, il ignore (journal debug), **sans réponse** ;
- vérifie que la position serveur courante (`ConnectionInfo.X`/`Y`, `Layer`) est **à l'intérieur** du
  polygone de la zone pour une entrée, et **à l'extérieur** pour une sortie — l'inverse étant un
  mensonge ou un désalignement, il est ignoré ;
- maintient un état de session « zone courante » pour rendre l'opération **idempotente** (`ENTER` pour
  la zone déjà courante, ou `LEAVE` sans zone courante, ne déclenchent rien de nouveau) ;
- ne peut pas, et ne doit pas, se reposer sur 15/16 comme seul déclencheur : puisque le serveur connaît
  position et polygones, il *peut* détecter les franchissements lui-même. Le socle livre la détection
  côté serveur aux trois seuls points où la position change (`GameClient.cs:117`, `:151`, `:160`) et
  garde 15/16 comme **déclencheur redondant et vérifié**. C'est la seule conception qui reste juste si
  l'émission par le client 7.3 s'avérait absente (§10 item 1).

Ce que le socle **ne** fait pas, et pourquoi : il n'ajoute pas de validation de déplacement
(anti-triche) — c'est un chantier distinct, absent des références, et hors périmètre ; il ne fait pas
confiance au paquet pour *décider* ; il ne « corrige » pas la position du client.

### 5.4 Réponse attendue du serveur : aucune

Aucune référence n'atteste d'un paquet serveur lié aux zones d'événement :

- rzu : la seule famille `*_EVENT_AREA*` est celle des deux paquets client
  (`rg -i 'enter_event|leave_event|event_area'` sur `reference/rzu/librzu/src/packets/` ne remonte que
  ces deux fichiers) ; rien en `SessionPacketOrigin::Server`.
- NGemity : même conclusion (§5.1), et sa branche « paquet inconnu » ne répond rien.
- Client : la notification d'entrée/sortie est interne (types `AUSMSG_ENTER_EVENTAREA`,
  `AUSMSG_LEAVE_EVENTAREA`, §2.2) et aucune chaîne de nom de paquet serveur ne la référence ; la table
  id→nom du client ne contient aucun nom pour 14..19 (§10 item 1), donc rien à quoi accrocher une
  réponse.
- Aucun texte de message localisé n'a été trouvé pour ces deux types dans les `.rdb` extraits
  (`strings -n 6 db_string.rdb`, `db_scriptstring.rdb`, `db_worldlocation.rdb` : 0 occurrence de
  `AUSMSG` et de `EVENTAREA`) — la formulation d'un éventuel message au joueur reste donc indéterminée
  et **ne doit pas** être inventée.

Décision : **le serveur ne répond rien** à 15 et 16. Il n'envoie ni `TM_SC_RESULT` (l'écho de résultat
sur 15/16 n'est attesté nulle part, et le client ne nomme pas ces ids : un résultat tagué 15 serait au
mieux ignoré), ni message. Si une capture ultérieure montre un `TM_SC_RESULT`, la carte devra être
rouverte — c'est une réserve, pas une hypothèse.

### 5.5 Conditions d'activation : la donnée n'existe pas localement

`EventAreaInfo` porte temps, niveaux, limites race/emploi, conditions et compteur ; **aucune** de ces
valeurs n'est disponible :

- le format `.nfe` ne porte que la géométrie : `LoadEventAreaFile` lit un nombre de zones, puis par zone
  `id` + nombre de polygones, puis par polygone le nombre de points et les points
  (`MapService.cs:265-287`). Rien d'autre. Les autres champs restent donc aux valeurs du constructeur
  (`EventAreaInfo.cs:33-46`).
- le schéma déclare bien une table qui correspond **champ à champ** :
  `ArcadiaSchemaPSQL.sql:250-279` (`EventAreaResource`) avec `id`, `begin_time`, `end_time`,
  `min_level`, `max_level`, `race_job_limit`, `activate_condition1..6`, `activate_value1_1/1_2 …
  activate_value6_1/6_2`, `count_limit`, `script_enter_text varchar(512)`,
  `script_leave_text varchar(512)`. La correspondance est exacte avec `EventAreaInfo.Id`, `BeginTime`,
  `EndTime`, `MinLevel`, `MaxLevel`, `RaceJobLimit`, `ActivateCondition[6]`, `ActivateValue[6][2]`,
  `LimitActivateCount` (`count_limit`), `EnterHandler`/`LeaveHandler`
  (`script_enter_text`/`script_leave_text`). Autrement dit : les champs de `EventAreaInfo` ne sont pas
  spéculatifs, ils recopient une table réelle.
- **mais rien ne l'importe** : aucune entité `EventAreaResource` sous
  `Game/DataAccess/Entities/Arcadia/` (19 entités, aucune ne la couvre), aucune référence au fichier
  `ArcadiaSchemaPSQL.sql` dans le dépôt (recherche sur `.md`, `.cs`, `.json`, `.yml`, `.sh`), aucune
  donnée dans le dépôt, et le projet `MigrateDatabase` ne couvre pas cette table. Le seul commit de ce
  fichier est `46bb289` (« wip »).
- Côté références, la table est **absente** du dump client de NGemity
  (`reference/ngemity/Database/Arcadia.sql`, 65 tables, aucune `EventAreaResource`), alors que ses
  colonnes `count_limit`/`script_enter_text` n'y apparaissent pas non plus. Elle est donc attestée une
  seule fois, par le schéma du dépôt.

Décision pour le socle : `EventAreaInfo.IsActivatable` (`EventAreaInfo.cs:10`) **reste à `false`** — le
socle transporte, valide et journalise le franchissement, il n'active rien. Implémenter une activation
avec des champs à `0` serait une invention : avec `MinLevel = 0` et `MaxLevel = 0` toutes les zones
seraient refusées, avec `BeginTime = EndTime = 0` toutes seraient acceptées — deux comportements
également faux, et non documentables. La carte « zones d'événement actives » rouvrira sur un prérequis
nommé : **importer `EventAreaResource`** (données client, cf. §10 item 3).

### 5.6 `LimitActivateCount` (`count_limit`) : ce que la référence dit, et pas plus

Ce que l'on sait : (a) le nom de la colonne est `count_limit` (`ArcadiaSchemaPSQL.sql:276`), mappé sur
`EventAreaInfo.LimitActivateCount` (`EventAreaInfo.cs:23`) ; (b) NGemity conserve
`Telecaster.EventAreaEnterCount(player_id, event_area_id, enter_count)`
(`reference/ngemity/Database/Telecaster.sql:224-230`), donc le serveur retail persistait un compteur
d'entrées **par personnage et par zone** ; (c) le seul compteur équivalent déjà porté par NavisLamia est
`CharacterEntity.HuntaholicEnterCount` (`CharacterEntity.cs:52`), par personnage, sans dimension de zone.

Lecture la plus probable : `count_limit` = **nombre maximal d'entrées autorisées dans la zone, par
personnage et par zone**, compteur persisté par couple `(personnage, zone)`. C'est la lecture que la clé
primaire de `EventAreaEnterCount` soutient. Elle n'est **pas** certaine : une lecture « compteur global
de la zone, toutes sessions confondues » (une zone « ouverte N fois par jour ») n'est pas exclue par les
références, et la fenêtre de remise à zéro (par jour ? par événement ? jamais ?) n'est attestée nulle
part. Le socle ne doit donc **rien** persister ni incrémenter (aucun consommateur, et la sémantique est
ouverte) — voir §10 item 4.

### 5.7 `EnterHandler` / `LeaveHandler` : des **textes** de script, pas des noms de fonction

La correspondance avec `script_enter_text` / `script_leave_text varchar(512)`
(`ArcadiaSchemaPSQL.sql:277-278`) est décisive sur la nature : ce sont des **chaînes**, pas des
identifiants de fonctions ni des noms de fichiers. NavisLamia dispose déjà d'un moteur Lua
(`ScriptService.cs:8`, `:64`) et exécute déjà une chaîne de script issue d'une donnée de carte
(`MapService.cs:338`), donc le point d'accroche naturel existe. En revanche le langage réel de ces
textes (Lua du client ? langage de script interne du serveur retail ? texte destiné à un autre
consommateur ?) **n'est établi par aucune référence** : NGemity ne les lit pas, rzu ne les voit pas, et
aucune donnée ne les remplit. Le socle doit les laisser intacts, journaliser leur présence le cas
échéant, et **ne pas** exécuter de chaîne provenant d'une table non importée (donc vide). À noter : le
rôle ne s'exécute pas non plus côté client (`AUSMSG_*` est une notification, pas un script).

## 6. Écarts assumés avec NGemity

1. **NGemity déclare les paquets et ne les traite pas** (§5.1). On ne prend pas son silence comme une
   spécification : il ne fournit que les ids et la forme des champs (identiques à rzu). Sa branche
   « paquet inconnu » journalise en debug et jette ; NavisLamia, elle, validera et suivra le
   franchissement (§5.3), parce qu'elle a déjà les polygones chargés — NGemity, lui, n'a pas de lecteur
   `.nfe`.
2. **NGemity compile en `EPIC_4_1_1`** (`Define.h:25`) alors que son propre en-tête place `LEAVE` en
   `EPIC_7_3` : ses ids ne sont pas une autorité de version. rzu tranche (§4), et il est cohérent.
3. **`Telecaster.EventAreaEnterCount` est pris comme preuve d'un comportement retail, pas comme
   exigence** : on n'ajoute pas de table tant qu'aucun code ne la consomme (§5.6, §8).
4. **Aucun idiome de NGemity n'est porté** (pas de `declareHandler`, pas de table `ignoredPackets`) :
   NavisLamia a ses propres points d'accroche (`_actions`, chaîne de `if` de `GameClient`, `switch`
   final) et le critère transversal 4 impose que les deux membres ajoutés y soient traités.
5. **`script_enter_text`/`script_leave_text` ne sont pas exécutés** (§5.7) là où un portage naïf
   appellerait `RunString` sur une chaîne vide.

## 7. Découpage : le minimum que cette branche doit livrer

C'est le livrable central de cette fiche : le socle doit rendre le franchissement **représentable,
validé et observable**, sans rien activer.

### 7.1 Sous-ensemble minimal (branche `hermes/packet-socle-zones-evenement`)

1. **Énumération** — `Game/Network/Packets/Enums/GamePackets.cs` : ajouter
   `TM_CS_ENTER_EVENT_AREA = 15` et `TM_CS_LEAVE_EVENT_AREA = 16`. Sans cela, `Enum.IsDefined`
   (`GameClient.cs:497`) rejette le paquet en debug avant même le dispatch.
2. **Lecture des champs** — `Game/Network/Packets/Game/GameActionPackets.cs` (ou un fichier
   `GameEventAreaPackets.cs` si l'on préfère isoler) : un `EventAreaRequest(int EventAreaId, int
   AreaIndex)` et un `TryReadEventAreaRequest(ReadOnlySpan<byte> packet, out EventAreaRequest request)`
   sur le modèle de `TryReadArrangeItem` (`GameActionPackets.cs:31-42`) : `HeaderSize = 7`
   (`:8`), longueur exigée **exactement 15**, sinon `false`. Exposer la constante de taille.
   *Note* : le socle ne doit **pas** interpréter `area_index` (§3.1), seulement le transporter et le
   journaliser.
3. **Dispatch** — `Game/Network/Clients/GameClient.cs` : deux bras dans la chaîne de `if` de
   `OnDataReceived` (`:509-668`), **avant** le `switch` final (`:670-682`, `_ => throw new
   Exception("Unknown Packet Type")` en `:681`). C'est le critère transversal 4 : l'énumération et le
   dispatch sont modifiés ensemble, aucun membre de `GamePackets` ne peut atteindre le `switch` final.
   Les deux bras doivent être des appels à un service, pas de la logique inline (cf. 5).
4. **État de session** — `Game/Network/Clients/ConnectionInfo.cs` : un champ « zone courante »
   (`int CurrentEventAreaId`, `0` = aucune) et sa remise à zéro dans `ClearCharacterSession` (`:153`,
   à côté de `Layer = 0` `:171` et `X/Y/Z` `:172-174`) ainsi qu'à toute sortie de carte
   (`HandleChangeLocation`, `GameClient.cs:160`, et `Client.Dispose`, `Client.cs:110-115`). C'est la
   réponse à « comment tuer proprement l'état à la sortie de jeu » : il vit dans `ConnectionInfo`, qui
   est déjà l'état par session, remis à zéro en un seul endroit.
5. **Accroche serveur et service** — `Game/Maps/` + `Game/Services/` :
   - exposer les zones chargées : accesseur en lecture sur `_eventAreaInfo` (`MapService.cs:34`) —
     `TryGetEventArea(int eventAreaId, out EventAreaInfo)` et, pour la détection serveur, la liste des
     zones candidates pour une carte/layer donnés (aujourd'hui la structure ne porte ni carte ni layer :
     `EventAreaInfo` n'a que `Id` et `Area`, `EventAreaInfo.cs:8-12` ; le socle peut se contenter du
     dictionnaire global + test d'appartenance, et documenter la limite en réserve) ;
   - `EventAreaService` (interface + implémentation) qui reçoit `(GameClient, EventAreaRequest, bool
     isEnter)`, applique §5.3 (existence de la zone, test `PolygonF.IsIncluded` — et non `Contains`,
     voir §5.3 et §15 — sur `ConnectionInfo.X`/`Y`, idempotence via `CurrentEventAreaId`) et
     journalise ;
   - détection serveur aux trois points de changement de position (`GameClient.cs:117`, `:151`,
     `:160`) : c'est ce qui rend le socle indépendant de l'émission client (§5.3 dernière puce).
6. **Tests** — `Tests/...` (le dépôt compte 366 tests, plancher) :
   - **offsets** pour les deux paquets : longueur totale 15, `Length` en 0, `ID` en 4 (`15` puis `16`),
     `Checksum` en 6, `event_area_id` en 7 (int32 LE), `area_index` en 11 (int32 LE) — sur le modèle de
     `Tests/Game/MovePacketsTests.cs:11-29` ;
   - rejet d'une longueur ≠ 15 et d'un tampon trop court ;
   - appartenance polygonale : point dedans / dehors, y compris polygone dégénéré ;
   - idempotence (`ENTER` deux fois, `LEAVE` sans `ENTER`), zone inconnue, mensonge de position
     (paquet d'entrée alors que la position serveur est dehors) ;
   - remise à zéro de `CurrentEventAreaId` par `ClearCharacterSession`.
7. **`docs/packet-specs/`** — la fiche doit être **commitée** (voir §9 pour `.gitignore`) et le bloc
   destiné à `CLAUDE.md` (§12) doit figurer dans la description de la MR. Le dev **n'écrit pas**
   `CLAUDE.md`.

### 7.2 Explicitement hors périmètre de ce socle

- Conditions d'activation (`BeginTime`, `EndTime`, `MinLevel`, `MaxLevel`, `RaceJobLimit`,
  `ActivateCondition`, `ActivateValue`) : données absentes (§5.5), `IsActivatable` reste `false`.
- `LimitActivateCount` : aucune persistance, aucun incrément (§5.6).
- Exécution de `EnterHandler`/`LeaveHandler` (§5.7).
- Toute réponse serveur (§5.4).
- Correction du lecteur `.nfe` (écrasement multi-polygones, `MapService.cs:289`) : à traiter dans la
  carte qui aura besoin de `area_index` (§3.1).
- Détection de triche sur la position (§5.3).

### 7.3 Ce qui doit rester à une carte ultérieure « zones d'événement actives »

Import de `EventAreaResource` (données) → évaluation d'activation → éventuelle persistance du compteur
d'entrées → exécution des handlers → éventuelle notification au joueur. Chaque étape a un prérequis
nommé, ce qui permet de rouvrir la carte sans refaire cette archéologie.

## 8. Persistance et migrations

**Le socle n'ajoute aucune migration.** Aucun consommateur n'existe pour un compteur d'entrées (§5.6).

Prérequis documenté pour la carte suivante, si `LimitActivateCount` doit être honoré : une table
équivalente à `Telecaster.EventAreaEnterCount(player_id, event_area_id, enter_count)`
(`reference/ngemity/Database/Telecaster.sql:224-230`), portée par le contexte Telecaster, avec la même
discipline que `CharacterEntity.HuntaholicEnterCount` (`CharacterEntity.cs:52`) : entité dans
`Game/DataAccess/Entities/Telecaster/`, `DbSet` dans `TelecasterContext`, migration EF dans
`Game/DataAccess/Migrations/Telecaster/`, puis accès via un dépôt. Aucun PostgreSQL n'est disponible sur
ce VPS (build et tests seulement) : une migration ne peut y être appliquée, seulement générée et
compilée.

## 9. État du dépôt, branches ouvertes et dépendances

- `master` = `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7`, identique à `origin/master`, arbre propre,
  `git log --oneline origin/master..master` vide.
- `docs/packet-specs/` **n'existe pas sur `master`** : `.gitignore:470` ignore `/docs/*` et n'exempte
  que trois fichiers (`:471-473`). L'exception `!/docs/packet-specs/` a été ajoutée par les branches de
  fiches précédentes ; ce socle reprend `.gitignore` depuis `hermes/packet-221-hide-equip-info`
  (sha1 `c8c9d96a52f1e35076396dd0d99a96093d3ac863`, identique à celui de
  `hermes/packet-socle-mort-respawn`) plutôt que d'inventer une ligne, et le commit de la fiche porte
  **deux** fichiers : `.gitignore` et `docs/packet-specs/socle-zones-evenement.md`.
- Branches ouvertes au moment de la rédaction : `hermes/packet-socle-mort-respawn` (locale + `origin`,
  socle précédent, 18 fichiers, en attente d'arbitrage de Killian) et les six branches de paquets
  (`hermes/packet-221-hide-equip-info`, `-223-swap-equip`, `-203-drop-item`, `-253-use-item`,
  `-408-request-remove-state`, `-1202-emotion`).
- **Collision prévisible** : `hermes/packet-socle-mort-respawn` touche déjà `GamePackets.cs`,
  `GameClient.cs`, `ConnectionInfo.cs` et `GameActions.cs` — les quatre mêmes fichiers que ce socle.
  Si Killian merge d'abord `hermes/packet-socle-mort-respawn`, le dev de ce socle part de `master`
  **après** ce merge (ou rebase) ; dans le cas contraire, il part de `master` en l'état et les deux
  branches resteront indépendantes.
- Aucune autre carte du pipeline n'est en cours sur ces fichiers.

## 10. NON ÉTABLI

Chaque point dit **précisément** ce qui manque et ce qui le trancherait ; aucun n'est deviné, et le dev
ne doit rien combler par supposition.

1. **L'émission par le client Epic 7.3 de 15 et 16 n'est pas établie.** Ce qui est vérifié :
   (a) le client compile bien des notifications d'entrée et de sortie de zone d'événement
   (`AUSMSG_ENTER_EVENTAREA` / `AUSMSG_LEAVE_EVENTAREA`, §2.2) ; (b) le client charge lui-même les
   polygones `.nfe` (§2.2) ; (c) la table id→nom du client **ne contient aucun nom pour 14..19**
   (`TM_CS_ENTER_EVENT_AREA` et `TM_CS_LEAVE_EVENT_AREA` sont absents de `SFrame.exe`, alors que 167
   chaînes `TM_*` y sont présentes, dont `TM_CS_RETURN_LOBBY` = 23). Ce qui manque : une preuve, non
   une absence de nom. **Contre-argument qui interdit de conclure** : cette même table n'a rien pour
   25..29 non plus, alors que rzu atteste `TS_CS_REQUEST_RETURN_LOBBY` = 25,
   `TS_CS_REQUEST_LOGOUT` = 26, `TS_CS_LOGOUT` = 27 et `TS_SC_DISCONNECT_DESC` = 28 en 7.3
   (`reference/rzu/librzu/src/packets/GameClient/TS_CS_REQUEST_LOGOUT.h:10-11`,
   `.../TS_CS_LOGOUT.h:10-11`, `.../TS_SC_DISCONNECT_DESC.h:21-22`) : la table n'est donc pas
   exhaustive et son trou en 15/16 ne démontre rien. Ce qui le trancherait : une capture réseau d'un
   client 7.3 réel, ou la lecture de la table d'émission du client (désassemblage du chemin d'envoi).
   Méthode de relevé de la table id→nom, reproductible : dans `SFrame.exe`, pour chaque
   `push $<chaîne .rdata>` suivi d'un `mov $<imm32>,%eax` dans `.text`, associer l'immédiat (id) à la
   chaîne ; 169 paires brutes, 152 avec un id plausible (< 20000), zone `.text` `0x66e1ce`..`0x6795bf`,
   ids 1..13, 20..24, 30, 50..59, 100..320, 400..452, 500..516, … 10005.
2. **Valeurs observées de `event_area_id` et `area_index`** : aucune. Aucun `.nfe` local, client non
   exécutable (interdit). Une capture est la seule voie.
3. **Données de la table `EventAreaResource`** : le schéma du dépôt les décrit
   (`ArcadiaSchemaPSQL.sql:250-279`) mais aucune donnée n'existe localement, aucune entité ni importeur
   ne les lit (§5.5), et la table est absente du dump client de NGemity (`Arcadia.sql`, 65 tables).
   On ne sait donc **pas** si elle est peuplée, ni si `script_enter_text` contient un script ou du
   texte destiné au joueur. Le complément local est l'archive client `data.000`
   (sha256 `b88ac39a0976f49746671d413ba68af616af37e6e26095c3a109d5884510adbf`) : l'index d'extraction
   (`reference/client73/extraction-manifest.json`) ne liste que 50 fichiers et **aucun** fichier de
   zone d'événement ; extraire l'archive est le seul moyen de confirmer.
4. **Sémantique exacte de `LimitActivateCount`/`count_limit` et fenêtre de remise à zéro** (§5.6) : deux
   lectures possibles (par personnage/zone, ou globale à la zone), remise à zéro inconnue (jour,
   événement, jamais).
5. **Langage et consommateur de `EnterHandler`/`LeaveHandler`** (§5.7) : textes `varchar(512)`, moteur
   inconnu ; NavisLamia a Lua + `RunString`, mais rien n'atteste que ce soit le même langage.
6. **Dimension carte/layer des zones** : `_eventAreaInfo` est un dictionnaire **global** indexé par id
   (`MapService.cs:34`, rempli `:289`) et `EventAreaInfo` ne porte ni carte ni layer
   (`EventAreaInfo.cs:8-12`). On ne sait donc pas si un `event_area_id` est unique dans le monde ou
   seulement dans un fichier `.nfe` — et donc si le test d'appartenance peut se faire sans connaître la
   carte. Le socle doit fonctionner dans les deux cas (id + polygones), mais c'est le préalable à toute
   activation sérieuse. À noter : si un même id apparaît sur deux cartes, le second chargement écrase
   le premier (`:289`).
7. **Réponse serveur** (§5.4) : l'absence de tout `TS_SC_*` de zone d'événement est **établie** pour
   rzu et NGemity, mais on ne peut pas exclure un paquet existant sous un autre nom dans le client :
   la table id→nom ne nomme aucun id de zone, et un paquet serveur pourrait exister sans nom. Rien ne
   doit être inventé ; si une capture montre un message serveur, la carte rouvre.
8. **Détection périodique ou par franchissement** : on ne sait pas si le client n'émet qu'aux
   franchissements ou aussi après un warp / une reconnexion à l'intérieur d'une zone (le socle couvre ce
   cas par sa propre détection de position, §5.3, mais c'est une décision, pas une référence).

## 11. A VERIFIER PAR KILLIAN

1. **Décision produit : « le serveur ne répond rien »** (§5.4). C'est cohérent avec toutes les
   références, mais c'est un choix à confirmer : si le serveur doit finir par signaler quelque chose au
   joueur (bandeau de zone, message), la carte devra le porter explicitement.
2. **Décision de périmètre : le socle n'active rien** (§5.5). `IsActivatable` reste `false` ; les zones
   existent donc « sur le papier » mais aucune n'est active à la fin de ce socle. Confirmer que c'est le
   découpage souhaité (sinon le prérequis est l'import de `EventAreaResource`).
3. **Preuve d'émission** (§10 item 1) : elle demande soit une capture réseau d'un client 7.3 réel (hors
   de ce VPS, et le client ne peut pas être lancé ici), soit de l'archéologie binaire plus profonde
   (chemin d'envoi du client). À arbitrer si l'on veut sortir 15/16 de `NON ÉTABLI`.
4. **Ordre de fusion** (§9) : `hermes/packet-socle-mort-respawn` touche déjà `GamePackets.cs`,
   `GameClient.cs`, `ConnectionInfo.cs`, `GameActions.cs`. Le dev de ce socle partira de `master` ; si
   Killian merge d'abord l'autre socle, il faudra rebaser.
5. **Corrections de portage dans `Game/Maps/X2D/LineF.cs`** (§15) : deux erreurs de comparaison dans
   `IntersectCcw` rendaient `PolygonF.IsIncluded` faux pour tout point intérieur. Elles sont corrigées
   d'après NGemity, mais elles touchent de la géométrie partagée : à confirmer comme faisant partie de
   cette carte, ou à extraire dans une carte dédiée.
6. **Détection après un warp** (§15, réserve 4) : `WarpService` n'appelle pas la détection ; une entrée
   de zone par warp n'est vue qu'à la mise à jour de position suivante. À confirmer comme suffisant.

## 12. Bloc destiné à `CLAUDE.md` (à coller par Killian)

Rien dans `CLAUDE.md` ni dans `docs/` ne parle aujourd'hui de zones d'événement ou de `.nfe`
(`grep -niE "event\.?area|\.nfe|EventAreaInfo" CLAUDE.md docs/*.md` : 0 résultat) : le bloc ci-dessous
est donc un **ajout**, pas une correction — contrairement au socle précédent, qui devait corriger la
phrase sur `TS_SC_DEAD` (`CLAUDE.md:217`).

```markdown

### Event areas (7.3): `TM_CS_ENTER_EVENT_AREA` (15) and `TM_CS_LEAVE_EVENT_AREA` (16)

The retail client loads the event-area polygons itself (`.nfe`, one file per map tile next to the
location/attribute files) and has compiled enter/leave notifications for them, but **no reference
proves that the Epic 7.3 client actually emits 15 or 16**; the client's packet name table has no name
for any id in 14..19. Treat these packets as a redundant trigger, never as the only one: the server
already loads the same polygons (`MapService._eventAreaInfo`, `.nfe`, read as id + polygon list only)
and knows the session position, so it checks containment itself instead of trusting the claim.
`EventAreaService` (`Game/Services/EventAreaService.cs`) does it, from the two dispatch branches in
`GameClient.OnDataReceived` *and* from every position change (move request, region update, change of
location); the packet is 15 bytes: header (7) + `event_area_id` (int32, offset 7) + `area_index`
(int32, offset 11).

Containment is `PolygonF.IsIncluded` (`Game/Maps/X2D/PolygonF.cs`, bounding box + crossing parity),
**not** `PolygonF.Contains`, which only compares against the vertex list. Two port errors in
`LineF.IntersectCcw` made `IsIncluded` answer `false` for every point inside any polygon and had to be
fixed against NGemity (`src/X2D/Linef.cpp`): the crossing test compared `ccw123` against itself instead
of `ccw124`, and the Y precheck compared `l2MinY` against its own maximum instead of `l1MaxY`. Also
`PointF` has no value equality, so the reference's "point equals a vertex" shortcut never fires on a
zone corner; and `new PolygonF(BoxF)` throws `NullReferenceException` because it calls `Set` on the null
elements of a `PointF[]` (dead code path today, `MapService` only clones polygons).

Neither rzu nor NGemity has any server packet for event areas, and NGemity has no handler at all
(15/16 fall into its "unknown packet" debug log). The server therefore sends **nothing** back.
`EventAreaInfo`'s other fields (times, level/race/job limits, six activation conditions,
`count_limit`, enter/leave scripts) are not in the `.nfe`: they mirror the `EventAreaResource` table of
`ArcadiaSchemaPSQL.sql`, which nothing imports, so they are all zero/empty today and
`EventAreaInfo.IsActivatable` stays `false`. NGemity's `Telecaster.EventAreaEnterCount`
(player_id, event_area_id, enter_count) shows the retail server kept a per-character, per-area entry
counter, but the socle persists nothing.

The full spec (offsets, sources, version gating, NGemity deltas, scope, open questions) is in
`docs/packet-specs/socle-zones-evenement.md`.
```

## 13. Commits et binaires épinglés

| Référence | Version épinglée | Usage dans cette fiche |
|---|---|---|
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02, « packets: fix TS_SC_INVENTORY with older epics ») | ids, ordre des champs, gating de version (§3, §4) |
| rzu `18e313f2167ca4a3aee90e99145bdd0f6f15b315` (2015-10-27, « packets: add gs packets (used for login) ») | introduction des deux en-têtes | existence des paquets et commentaires `Since EPIC_6_3` / `Since EPIC_7_3` |
| rzu `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11, « use versionned ID for all packets and update their ID with epic 9.6.3 ») | ids versionnés : `15`/`1015` et `16`/`1016` | gating tranché pour 7.3 (§4) |
| NGemity | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03, « Fix compilation issue for GCC ») | en-têtes homonymes, schéma `Telecaster.EventAreaEnterCount`, absence de handler (§5.1) |
| NGemity `44b7d25dac7a1f869490dc8b6ea3255ea68b8eb4` (2018-08-26, « Network: Rewriting network code to use @glandu2 's structs ») | introduction des deux en-têtes | forme des champs côté NGemity |
| Client 7.3 | `SFrame.exe` sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (`pei-i386`) | polygones `.nfe`, types `AUSMSG_*`, table id→nom (§2.2, §10) |
| Archive client | `data.000` sha256 `b88ac39a0976f49746671d413ba68af616af37e6e26095c3a109d5884510adbf` (`reference/client73/extraction-manifest.json`, 50 fichiers extraits, 83822 entrées) | ce qui reste inextrait (§10 item 3) |

## 14. Note de livraison

- Livrable : cette fiche (`docs/packet-specs/socle-zones-evenement.md`) et l'exception `.gitignore`
  reprise telle quelle (§9). **Aucun fichier de code n'est modifié par cette tâche**
  *(constat de la tâche d'archéologie ; la livraison de code est en §15)*.
- Rien n'a été poussé : ni `push`, ni MR (`navis-qa` publie).
- Aucun serveur, aucune base PostgreSQL, aucun client lancé : les relevés client sont des lectures
  statiques (`strings`, en-têtes de sections `objdump -h`, extraction de paires
  `push`/`mov` par script Python), conformément aux règles du rôle.
- Réserve de méthode : les relevés client sont des **lectures statiques** ; ils établissent la présence
  d'un artefact, pas l'exécution d'un chemin d'appel. Toutes les fois où cette limite compte, la fiche
  le dit (§2.3, §10 item 1).

## 15. Livraison du socle (navis-dev)

Branche `hermes/packet-socle-zones-evenement`, poursuivie depuis le commit de la fiche. Commits de
code : `3e0e156` (le socle) puis `f89e555` (les tests et les deux corrections de portage de `LineF`).
Rien n'est poussé, aucune MR (c'est `navis-qa` qui publie).

### Ce qui est livré

| Fichier | Rôle |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_ENTER_EVENT_AREA = 15`, `TM_CS_LEAVE_EVENT_AREA = 16` |
| `Game/Network/Packets/Game/GameEventAreaPackets.cs` | `EventAreaRequest(EventAreaId, AreaIndex)` et `TryReadEventAreaRequest`, longueur exigée exactement 15 |
| `Game/Services/EventAreaRules.cs` | table de décision pure `Resolve(isEnter, inside, isCurrentArea)` → `None` / `Ignored` / `Entered` / `Left` |
| `Game/Services/EventAreaService.cs` + `Game/Services/Interfaces/IEventAreaService.cs` | le service : existence de la zone, appartenance, idempotence, journalisation |
| `Game/Network/Clients/GameClient.cs` | deux bras de dispatch et la détection aux trois points de changement de position |
| `Game/Network/Clients/ConnectionInfo.cs` | `CurrentEventAreaId` (0 = aucune) et sa remise à zéro dans `ClearCharacterSession` |
| `Game/Network/NetworkService.cs`, `DevConsole/Program.cs` | injection du service |
| `Game/Maps/IMapService.cs`, `Game/Maps/MapService.cs` | `TryGetEventArea` et `GetEventAreas` |
| `Game/Maps/X2D/LineF.cs` | les deux corrections de portage ci-dessous |
| `Tests/Game/EventAreaTests.cs` | 37 tests |

Décisions d'implémentation, là où la fiche laissait le choix :

1. **Le paquet reste un déclencheur redondant** (§5.3) : la détection serveur tourne de toute façon à
   chaque changement de position, donc le socle est juste même si le client 7.3 n'émet jamais 15/16.
2. **`PolygonF.IsIncluded`, pas `PolygonF.Contains`** (§5.3) : `Contains` compare à la liste des
   sommets — et, `PointF` n'ayant pas d'`operator==`, par référence. Un test épingle la différence
   (`Contains_IsVertexEqualityAndMustNotBeUsedAsAContainmentTest`).
3. **Signature du service** : les deux méthodes publiques demandées par la fiche
   (`HandlePacket(GameClient, ReadOnlySpan<byte>, bool)` et `Refresh(GameClient)`) survivent telles
   quelles ; elles délèguent à des surcharges prenant `(ConnectionInfo session, string clientTag, …)`,
   pour que le cœur soit testable sans socket. `GameClient` n'apporte que `ConnectionInfo` et son
   étiquette de journal.
4. **Pas de remise à zéro aveugle dans `HandleChangeLocation`** (écart assumé avec §7.1 item 4) : un
   `Refresh` y décide, comme aux deux autres points de position — un téléport hors zone produit un
   « sortie », un téléport dans une autre zone un « changement », et un téléport à l'intérieur de la
   même zone ne produit rien. `Client.Dispose` met déjà `ConnectionInfo = null`, ce qui emporte l'état.
5. **Accès aux zones** : `TryGetEventArea` sous verrou ; `GetEventAreas()` renvoie un instantané
   immuable remplacé au chargement (§7.1 item 5 : le dictionnaire est global, `EventAreaInfo` ne porte
   ni carte ni layer — limite inchangée, et testée).

### Corrections de portage obligatoires dans `Game/Maps/X2D/LineF.cs`

Elles ne sont pas cosmétiques : sans elles, `PolygonF.IsIncluded` répond `false` pour **tout** point à
l'intérieur d'un polygone (mesuré avant correction sur un carré 0..100 : `IsIncluded(50, 50) == false`).

1. `IntersectCcw` comparait `(int)ccw123 * (int)ccw123 < 0` : le produit d'un nombre par lui-même n'est
   jamais négatif, donc la branche `INTERSECT` ne pouvait **jamais** se déclencher et le croisement
   réel tombait dans `SEPERATE`. La référence NGemity compare bien `ccw123` et `ccw124`
   (`reference/ngemity/Chihiro/src/X2D/Linef.cpp:84`).
2. Le pré-filtre comparait `l2MinY > l2MaxY` ; la référence teste
   `std::max(p1.y, p2.y) < std::min(p3.y, p4.y)`, soit `l2MinY > l1MaxY` (`Linef.cpp:49`). `l1MaxY`
   portait d'ailleurs un commentaire « TODO: unused ? » dans le port : il ne l'est plus.

Portée : `grep` sur `Game/` et `Tests/` ne trouve **aucun** autre appelant de `IntersectCcw`,
`IsIncluded` ou `IsCollision` — aucun autre système ne peut changer de comportement aujourd'hui, et les
366 tests antérieurs passent toujours. Deux tests de `EventAreaTests.cs` épinglent ces deux
comportements, et `PolygonF.IsIncluded` est désormais testé dedans/dehors.

### Tests

`dotnet build Navislamia.sln -c Debug` : 0 erreur. `dotnet test Tests/Tests.csproj` : **403 tests, 0
échec** (366 avant cette branche, 37 ajoutés). Les tests ajoutés couvrent : les offsets des deux
paquets (longueur 15, `ID` en 4, `event_area_id` en 7, `area_index` en 11, rejet de 7/14/16 octets),
l'unicité des ids de `GamePackets`, la présence des deux bras de dispatch **avant** le `switch` final,
la table de décision complète, l'appartenance polygonale (dedans/dehors/dégénéré/sommet), les cas
`ENTER`/`LEAVE` vérifiés, mensonges et idempotents, la détection serveur (entrée, sortie, changement de
zone, hors zone), et la remise à zéro par `ClearCharacterSession`.

Aucun serveur, aucune base PostgreSQL, aucun client n'a été lancé : build et tests seulement.

### Réserves, non tranchées

1. **`PointF` n'a pas d'égalité de valeur** : le raccourci « point = sommet » de `IsIncluded`
   (`PolygonF.cs:174`) ne se déclenche jamais, donc un personnage exactement sur un **sommet** de zone
   est lu « dehors ». Corriger demande d'ajouter `operator ==`/`Equals` à `PointF`, ce qui touche
   `BoxF.Has`, `LineF.Has` et `PolygonF.Contains` : hors périmètre d'un socle paquet.
2. **Carte/layer absents** (NON ÉTABLI 6) : le socle utilise le dictionnaire global. Un déplacement de
   plusieurs tuiles est correct, mais un personnage transporté sur une autre carte est lu comme
   « sortie » de la zone courante (testé : `Refresh_FarFromTheLoadedArea_ReportsALeave`), et une zone
   de même id sur une autre carte serait confondue avec celle-ci.
3. **Zones qui se chevauchent** : la détection garde la zone courante tant que la position y est,
   sinon elle prend la **première** zone trouvée dans l'instantané (ordre du dictionnaire). Aucune
   priorité n'est attestée dans les références.
4. **Warp** : `WarpService` écrit `X`/`Y` sans passer par les trois points de détection ; une entrée
   de zone par warp n'est vue qu'à la mise à jour de position suivante (le client en émet en continu,
   c'est le mécanisme de visibilité existant). À confirmer comme suffisant, ou à accrocher aussi dans
   `WarpService`.
5. **`PolygonF(BoxF)` lève `NullReferenceException`** (`PolygonF.cs:21-30` : `PointF` est une classe,
   `new PointF[4]` n'est qu'un tableau de références nulles et `pt[0].Set(...)` déréférence `null`).
   Bug de portage **préexistant**, sans appelant atteint (`MapService` ne passe que des `PolygonF` à ce
   constructeur) ; signalé parce qu'il se déclenche dès qu'on touche aux polygones.
6. **Critère 4 vérifié par lecture du source** : la présence des deux bras de dispatch dans
   `GameClient.cs` est testée en lisant le fichier (aucun socket en test). Ce test devra changer le
   jour où le dispatch ne sera plus une chaîne de `if`.
7. Aucun des `NON ÉTABLI` (§10) n'a été deviné : `area_index` est transporté et journalisé sans
   interprétation, aucune zone n'est activée, aucune réponse serveur n'est émise.
