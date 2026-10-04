# Socle — point de retour, départ par race et téléporteurs de ville

| | |
|---|---|
| Paquets | aucun nouveau : `TM_CS_RESURRECTION` (513) type 0 utilise le point de retour ; les dialogues passent par `TS_SC_DIALOG` (3000) / `TS_CS_DIALOG` (3001), l'or par `TS_SC_GOLD_UPDATE` (1001), les messages par `TS_SC_CHAT` type 40 (`@SCRIPT`) |
| Référence d'autorité | Lua serveur Epic 7 (`Epic 7 Part 4/branches/Live` et `trunk` : `on_first_login`, `on_login`, `on_player_level_up`, `NPC_TeleportTown`) |
| Référence C++ | serveur officiel 2015 (`StructPlayer::GetLastTownPosition`, `SCRIPT_WarpToRevivePosition`, `StructPlayer::onExpChange`), `on_player_dead.lua` (`revive_in_town`) |
| Code | `Game/Services/ReturnPoints/`, `Game/Services/Props/TownTeleportRules.cs`, `PropScript`, `NpcDialogService`, `GameActions` (création, connexion), `LevelingService`, `ResurrectionService` ; outil `tools/export_town_teleporters.py` ; tests `ReturnPointTests` |

## 1. Le point de retour officiel

Le serveur officiel range le point de retour dans **deux drapeaux de script, `rx` et `ry`**
(« 스크립트상 귀환좌표는 rx, ry 임 », `StructPlayer::GetLastTownPosition`). La réapparition en ville
(`revive_in_town` → `warp_to_revive_position` → `GetLastTownPosition`) téléporte à ce point, couche 0 hors
des canaux à population limitée, PV au maximum pour une mort ordinaire (`revive_type == 0`). Si `rx` ou `ry`
vaut 0 ou manque (`atoi`, puis `!x || !y`), le C++ prend la ville de la race (6625/6980, 116799/58205,
153513/77203, plus un tirage entre 0 et 100), mais `on_login` a déjà posé les drapeaux à ce moment.

Navislamia stocke ces drapeaux dans **`Characters.FlagList`**, colonne `text[]` qui existait sans être
utilisée, au format `nom:valeur` (`rx:6650`, `ry:7001`). **Aucune migration.** `ReturnPointRules.Write`
remplace `rx`/`ry` et garde tout autre drapeau dans l'ordre. Le point de la session vit dans
`ConnectionInfo.RespawnX/RespawnY` (`RespawnLayer` = 0), et **chaque changement est écrit aussitôt**
(`ICharacterService.SaveReturnPointAsync`), pas avec la progression.

Les races suivent le codage officiel, qui est aussi celui de la base : Gaïa 3, Déva 4, Asura 5.

## 2. Qui écrit le point de retour

| Moment | Règle (Lua Epic 7) | Navislamia |
|---|---|---|
| Création (`on_first_login`, appelé par `on_login` quand `job == 0`) | départ sur l'île des apprentis selon la race — Déva 164474/52932, Asura 168356/55399, Gaïa 164335/49510 — et retour = départ `+ random(0,60) - 30` | `GameActions.OnCreateCharacter` : `Position` et `FlagList` posés à la création (le métier y est déjà posé, donc pas de « première connexion ») |
| Connexion (`on_login`) | sans `rx`/`ry` : Déva 7250/6959, Asura 116542/58190, Gaïa 152742/77401, `+ random(0,100)` | `GameActions.OnLoginAsync` ; les personnages existants reçoivent ainsi leur ville à la première connexion |
| Téléporteur, `Binding_*` | Déva 6625/6980, Asura 116799/58205, Gaïa 153513/77203, île 172185/52095 (`random(0,10)`), Rondo 135466/104917 et 140019/106038, Ruines 152634/151508, `+ random(0,100)` sauf l'île ; puis `message("@…")` | `ReturnPointService.TryBind` (table `ReturnPointRules`) ; le message part en `TS_SC_CHAT` type 40 de `@SCRIPT`, comme les autres `message()` portés |
| `RunTeleport_City_To_Camp(cost, x, y)` | retour = `x/y + random(0,10)` | `NpcDialogService.Teleport` (aucun dialogue Epic 7.3 ne l'emploie encore) |
| Montée de niveau (`on_player_level_up`, `lv == 5`) | retour = camp 172543/51847 `+ random(0,100)` | `LevelingService.ApplyExperience` → `ReturnPointService.OnLevelUp`. L'officiel lance le script **une fois par gain, avec le niveau atteint** (`StructPlayer::onExpChange`) : un gain qui saute le niveau 5 ne déclenche rien, ici comme là-bas |

**Non porté, volontairement :** la branche `lv >= 18` de `on_player_level_up`, qui renvoie le retour en ville
quand le joueur est encore sur l'île. Elle compare `get_flag("rx")`, une chaîne, au nombre `173183` : en Lua
`"173183" == 173183` est faux, donc la branche ne s'exécute jamais. C'est du code mort chez l'officiel.

Si la couche sauvegardée est privée (salle d'instance perdue au redémarrage) ou la position nulle, la
connexion place le personnage **au point de retour** (avant : 153161/80223, une ville choisie sans source).

## 3. Téléporteurs de ville

Les dialogues des téléporteurs de ville **manquaient au catalogue** `npc-dialogs.73.json`. L'importeur 9.4
(`tools/import_npc_dialogs.py`) n'a pas lu leurs fonctions de contact : elles se terminent sur un bloc
conditionnel (donjon de guilde), et leurs libellés tarifés (`"@90700620\v#@price@#\v48000"`) ne passent pas
son motif de menu. Un clic sur un téléporteur de ville n'ouvrait donc rien, et le joueur n'avait aucun moyen
de fixer son retour.

`tools/export_town_teleporters.py` lit les 9 contacts statiques dans le Lua Epic 7 `Live` (Déva, Déva 2,
Asura, Asura 2, Gaïa, Gaïa 2, Rondo, Rondo 2, Ruines de la cité), plus les trois pages de bavardage de Gaïa
(`quest_rumor6`, `quest_rumor_a_3`, `quest_rumor_b_3`). Il relie chaque PNJ à sa fonction de contact Epic 7
(`NPCResource.contact_script`) : 1005, 1014, 2005, 2017, 4005, 4098, 6005, 6014, 7033, et 3005 pour l'île.
Le `\v` des libellés est le caractère 0x0B, conservé tel quel.

Entrées laissées de côté :

- le raccourci vers le donjon de la guilde (`scf_teleport_to_owned_dungeon()`, dans un `if`) : aucun
  service ne traite ce déclencheur, l'annoncer donnerait une entrée morte ;
- l'entrée d'Halloween du téléporteur Déva (`Trick_or_treat_2011()`), active pendant l'événement seulement.

Le téléporteur de l'île (`NPC_TeleportField_Beginner_contact`, PNJ 3005) dépend du personnage :
`TownTeleportRules.BeginnerTeleporter` le construit (retour sur l'île ; côte est si la quête 1025 est
terminée, `get_quest_progress == 255` ; ville de la race pour 10 or après un changement de métier,
`job_depth > 0`). « Autre camp des apprentis » (`Teleport_channel`) est omis : les canaux de l'île ne sont pas
modélisés.

### Les trois téléports

| Déclencheur | Coût | Arrivée | Retour |
|---|---|---|---|
| `RunTeleport(cost, x, y)` | `gold < cost` → `message("@90010008")` et rien d'autre | `x/y + random(0,10)` (le commentaire Lua dit 100, le code 10) | inchangé |
| `RunTeleport_Begin_TO_City(cost, x, y)` | idem | `+ random(0,100)` | inchangé (la ligne `set_flag` est commentée) |
| `RunTeleport_City_To_Camp(cost, x, y)` | idem | `+ random(0,100)` | `x/y + random(0,10)` |

L'or passe par `ConnectionInfo.TryDebitGold`, puis `TS_SC_GOLD_UPDATE` (1001) quand le coût n'est pas nul.
Le coût vit dans `PropAction.Cost` ; avant ce lot, `RunTeleport` ignorait son premier argument (nul dans les
9 dialogues qui l'employaient). Un coût négatif ne se lit pas. Les props qui téléportent ne paient rien
(aucun n'a de coût).

## 4. Cinématique de la première entrée en jeu

Ni le serveur officiel (C++ et Lua) ni les scripts Epic 7 n'ont de déclencheur de cinématique, et le
protocole n'a aucun paquet pour en lancer une. Dans `SFrame.exe`, le lecteur `rp::SGameCutScene`
(vtable `0xa57e2c`, constructeur `0x6a6070`) n'est construit qu'à deux endroits :

- `SGameLoad::LoadMovie` (`0x642e3c`) : les écrans de démarrage (`company_scene`, `publisher_scene`,
  `copyright_scene`…) ;
- `SGameRenewalLobby::LoadMovie` (`0x656f6a`) : le lobby (`intro.xml`, `epic6_navislamia_intro.xml`,
  `epic6_golden_monarch_intro.xml`, le bouton du prologue Epic 6, l'état `GAMELOGIN_CREATECHAR_RACE_MOVIE`).

Aucun chemin n'existe depuis le monde (`SGameWorld`). Le client 7.3 ne peut donc pas jouer de cinématique
à l'entrée en jeu sur ordre du serveur.

## 5. NON ÉTABLI

- **Les canaux de l'île des apprentis** (canal 1000, `get_proper_channel_num`, `get_layer_of_channel`) : tout
  le monde arrive sur la couche 0. L'officiel répartit les apprentis sur plusieurs couches.
- **La cinématique attendue** à la première entrée (§4) : si elle existe en retail 7.3, ce n'est pas dans ce
  client, ou c'est un état du lobby que rien d'ici n'empêche.
- **L'affichage des libellés tarifés** (`\v#@price@#\v`) par ce client : la substitution est celle des
  scripts officiels, pas encore vue en jeu.
- **Le raccourci vers le donjon possédé** : à ajouter quand `scf_teleport_to_owned_dungeon` sera servi.
- **Les téléporteurs de Secroute** (`NPC_TeleportTown_1_Secroute_contact`, titre selon le PNJ) et du marché :
  hors de ce lot, ils ne fixent pas de point de retour.
