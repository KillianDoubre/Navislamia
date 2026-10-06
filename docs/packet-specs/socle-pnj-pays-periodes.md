# Socle : quels PNJ un serveur montre, et les dialogues de Hidden Village (2026-10-06)

Deux bugs notés par Killian :

- « La boîte de dialogue des PNJ de Hidden Village ne s'ouvre pas » ;
- « Filtrer les PNJ en ville, surtout les événements, ceux à date, ceux au nom en `???????` ».

Ils avaient une cause commune : **le serveur faisait apparaître toutes les lignes de `NpcResources`** (1 182), sans
filtre de pays ni de période. À une seule position de Hidden Village (222584, 19922) se superposaient une quinzaine de
PNJ d'événement de tous les pays, souvent au nom illisible et sans dialogue. Il manquait en plus le dialogue des
téléporteurs du village, des commissaires-priseurs et du Flea Market.

## 1. La règle officielle de sélection des PNJ

Source : le serveur de 2012-11, de la même époque que le client (`onNPCData`, `0x1401c70a0`, lu dans le binaire). La
source de 2015 (`NPCLoader.cpp:143-180`) a la même logique, avec en plus une table de périodes séparée
(`NPCEventPeriodResource`) que le serveur de 2012 n'a pas : en 2012, la période vient des colonnes de `NPCResource`.

Au chargement, un PNJ est écarté si :

1. son `local_flag` porte le bit 29 (`LOCAL_EXCLUDE_TEST_SERV`) sur un serveur de test, ou le bit 30
   (`LOCAL_EXCLUDE_SERVICE_SERV`) sur un serveur de service (`game.ServiceServer`) ;
2. son `local_flag` contient le bit du pays du serveur (`g_nCurrentLocalFlag`, `game.local_flag`, défaut
   `LOCAL_INFO_KOREA` = 1) : **le `local_flag` d'un PNJ est un masque d'exclusion**, 0 = partout ;
3. il est périodique (`is_periodic`) et sa période est terminée (`time(NULL) >= end_of_period`).

En jeu, un PNJ périodique n'apparaît qu'à partir de `begin_of_period` (`NPCRespawn.cpp:71-86`) et quitte le monde à
`end_of_period` (`NPCAI.cpp:163`).

Dans le dépôt : `Game/Services/NpcSpawnRules.cs` (`IsLoaded`, `IsPresent`), appliqué par `NpcSpawnService`. Le
chargement applique `IsLoaded`, chaque synchronisation de visibilité applique `IsPresent` : un PNJ dont la période
s'achève quitte la vue à la synchronisation suivante, et non à l'instant exact comme chez l'officiel. Réglages
`GameRules:LocalFlag` (défaut 1, la Corée, la valeur que l'artisanat, les donjons et HuntaHolic utilisent déjà) et
`GameRules:ServiceServer` (défaut `true`). Le décalage `game.periodical_npc_adjustment` n'est pas porté : il vaut 0
par défaut et toutes les périodes de la donnée sont terminées depuis 2012.

Mesures sur la base Arcadia (1 182 PNJ) : 366 sont périodiques, tous terminés (au plus tard le 2012-11-27). Le choix
du pays ne change presque rien : une fois le bit 30 appliqué, la Corée et l'Amérique ne diffèrent que par les six
marchands d'orbes de l'événement américain de 2010.

## 2. Les drapeaux du client 7.3

La règle officielle seule laisse **522** PNJ, dont une centaine d'événements. Ce sont des lignes 9.4 (identifiants
11716 à 11856 : Pepero 2012, Noël 2012, 2013, arène 8.1…) importées avec `local_flag = 0` et sans période, que la 9.4
activait par sa table de périodes. Le `db_npcresource.rdb` du client porte son propre `local_flag` : enregistrements
de 1 006 octets, `int32` à +36, après `id, text_id, name_text_id, race, sex, x, y, z, face`, l'ordre des colonnes
serveur.

Comparé à la base, il diffère sur 189 lignes, dont 104 du dump Epic 7. Le client reflète un état plus tardif : il
coupe les événements terminés (Wonderland, Noël 2011, toutes les lignes d'événement 9.4) et les marchands
coréens. Dans l'autre sens, il réactive dix PNJ que le serveur coupe (Darkwood Lady, « Leveling »), au nom `????`
dans les chaînes 9.4.

**Règle retenue : un PNJ est écarté dès que le serveur ou le client l'exclut** (`NpcSpawnRules.EffectiveFlag`, union
des deux masques). Les drapeaux du client sont une ressource embarquée (`Game/Services/npc-client-flags.json`,
`tools/export_npc_client_flags.py`). Ils ne sont pas écrits en base : un nouvel `import_epic7.py` les écraserait.

Résultat au démarrage : **406 PNJ sur 1 182**. Parmi eux, aucun n'a de nom non ASCII ou en `?` dans le `db_string`
du client, contre une centaine avec la seule règle serveur. Les cinq téléporteurs de Wonderland (« Chewie »), au nom
correct, restent.

## 3. Les dialogues de Hidden Village

Les 27 PNJ du village encore affichés ont tous un dialogue. Ceux qui manquaient s'exécutent maintenant en **Lua
officiel** dans le bac à sable des PNJ (`NpcScriptService`), parce que leur menu dépend de l'identifiant du PNJ et du
pass d'accès :

| PNJ | contact | ce qui s'ouvre |
|---|---|---|
| 7005 Yurie, 11237 Faerrah | `NPC_TeleportTown_1_Secroute_contact` | les villes, la ferme, le camp des apprentis |
| 7006 Jacu, 11238 M.J. | `NPC_TeleportTown_2_Secroute_contact` | les terrains de chasse si `is_premium()`, sinon le texte `@90700118` |
| 9999-10001 (Lini, Giselle, Frat) | `NPC_Auction_Secroute_contact` | la fenêtre d'enchères |
| 11127, 11240 Bob Cat | `NPC_maricat_market_teleport_contact` | le Flea Market (`warp_to_market`) |

Le même correctif vaut ailleurs dans le monde :

- les 7 téléporteurs « vers Hidden Village » des villes (`NPC_TeleportSecroute_Town_contact` : 1016, 2016, 3024,
  4016, 6016, 7027, 7040) ;
- les PNJ du Flea Market (`NPC_maricat_market_maricat/guard_contact`) ;
- **tous les commissaires-priseurs** (`NPC_Auction_Deva/Gaia/Asura/Rondoh/maricat_market_contact`) : aucun n'ouvrait
  la fenêtre d'enchères.

Sources : le Lua Epic 7 (`branches/Live/NPC_TeleportTown.lua`, plus proche du client que la copie de 2015, qui y ajoute
un raccourci de donjon secret et une destination 8.x) ; `NPC_Auction.lua` de 2015 (absent du dump Epic 7, six
contacts identiques : `show_auction_window()`). Ils sont exportés par `tools/export_npc_runtime.py`, et
`tools/export_town_teleporters.py` relie chaque PNJ à son contact Epic 7. Le lien de 7005 pointait vers le menu de
Rondo de la 9.4.

Nouvelles fonctions moteur du bac à sable :

- `show_auction_window()` envoie `TS_SC_DIALOG` (3000) de type `TYPE_AUCTION_WINDOW` = 4 (`GameMessage.h:2282`),
  comme `SCRIPT_ShowAuctionWindow` (`ScriptPlayer.cpp:4791`) ;
- `is_premium()` répond vrai si l'état 9004 est actif (`StructState::GAIA_MEMBER_SHIP`, « Travel Pass to Hidden
  Village », `StructMisc.h:50`), l'état que `StructPlayer::SetSetSecrouteFreePass` pose ;
- `get_own_dungeon_id()` et `get_siege_dungeon_id()` valent 0 : le raccourci vers le donjon de guilde n'est servi par
  aucun téléporteur (CLAUDE.md, *Point de retour*).

## 4. Écarts et NON ÉTABLI

- **Le pass de Hidden Village** : chez l'officiel, il vient de la base commerciale (`OUT_PREMIUM_TICKET` lu à la
  connexion, `DB_CommercialItemStorage.cpp:64-69`). Les objets 910000/910001 portent l'effet 89, non porté. Sans pass,
  le téléporteur des terrains de chasse affiche donc `@90700118` : c'est le comportement officiel. Pour tester :
  `/buff 9004`. Le pass relève du lot boutique payante (Codex).
- Le fichier `db_npcresource.rdb` du client vient d'une reconstruction communautaire, comme `db_item.rdb`. Son état
  plus tardif est cohérent (fins d'événements) mais sa date n'est pas établie.
- Les dix PNJ que le client réactive (Darkwood Lady 9938-9942, « Leveling » 11037-11041, l'événement d'été japonais)
  restent écartés, la base les excluant : à revoir si l'un d'eux manque en jeu.

## 5. Vérifications

- `Tests/Game/NpcSpawnRulesTests.cs` : pays, bits 29/30, périodes, drapeaux du client, service de visibilité.
- `Tests/Game/NpcScriptTests.HiddenVillage.cs` : téléporteurs avec et sans pass, téléporteur de ville, Flea Market,
  fenêtre d'enchères (trame 3000, type 4, poignée du PNJ), et lien vers un contact exécuté pour chaque PNJ concerné.

En jeu :

1. À Hidden Village : Yurie ou Faerrah ouvre la liste des villes. Jacu ou M.J. : « réservé » sans pass, menu des
   terrains de chasse après `/buff 9004`. Lini, Giselle ou Frat ouvre l'hôtel des ventes. Bob Cat propose le Flea
   Market.
2. Dans une ville : le commissaire-priseur ouvre l'hôtel des ventes, et le téléporteur « vers Hidden Village » répond.
3. Plus aucun PNJ d'événement ni aucun nom en `???????` dans les villes.
