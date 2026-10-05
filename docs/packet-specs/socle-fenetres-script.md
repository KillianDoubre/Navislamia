# Socle — fenêtres de confirmation natives du serveur officiel

Livré le 2026-10-05 (lot de Codex, relu et corrigé). Carte Trello `MwyZrBMy`. Les fenêtres passent par la trame
déjà livrée `TM_SC_SHOW_WINDOW` (3003, [3003-show-window.md](3003-show-window.md)) et leur réponse par
`TM_CS_DIALOG` (3001).

## 1. Les six fenêtres et leur rappel

Format mesuré dans `SFrame.exe` (sha256 `41e0af2e…`) par `tools/client73_windows.py` : chaque gestionnaire de
fenêtre construit le texte du 3001 à partir du déclencheur reçu dans la 3003.

| Fenêtre | Ouverte par (officiel) | Argument | Déclencheur | Rappel 3001 accepté |
|---|---|---|---|---|
| `secret_dungeon_confirm_window` | `enter_secret_dungeon(id)`, `ScriptPlayer.cpp:2640-2670` | id | `warp_to_secret_dungeon` | `warp_to_secret_dungeon(<argument>)` |
| `instance_dungeon_confirm_window` | `enter_instance_dungeon(id)`, `:2718-2748` | id | `warp_to_instance_dungeon` | `warp_to_instance_dungeon(<argument>)` |
| `instance_dungeon_confirm_window2` | `leave_instance_dungeon(id)`, `:2862-2895` | id | `exit_indun` | `exit_indun(<argument>)` |
| `dungeon_raid_confirm_window` | `enter_dungeon` (`SCRIPT_WarpToDungeon`) pour le chef d'une équipe d'attaque avant le début du raid, `:3185-3206` | nom du joueur | `begin_dungeon_raid` | `begin_dungeon_raid()` |
| `recall_feather_confirm_window` | objet effet `RECALL` (112), `StructCreature.cpp:4854-5021` | nom du lanceur | `recall_feather( x, y, layer )` (position figée) | le déclencheur tel quel |
| `number_input_window` | `show_channel_set()`, `ScriptPlayer.cpp:2569-2581` | nom du joueur | `on_channel_set` | `on_channel_set(<nombre saisi>)` |

Annuler une fenêtre n'envoie aucun 3001 (le client efface son déclencheur local, `0x006428C9`).

## 2. Jugement du rappel

`ScriptWindow.Matches` remplace le `strstr` de l'officiel par une égalité stricte : un rappel est accepté **une
seule fois**, pour le personnage et la fenêtre qui l'ont reçu, et jamais s'il porte autre chose que la forme
attendue (`…;set_flag(…)` est refusé). Le nombre de `on_channel_set` doit être un entier décimal sans
expression ; la fenêtre de canal exige en plus le même PNJ et la même révision de dialogue. Une fenêtre en
remplace une autre ; changer de personnage ou quitter le monde efface l'attente.

Exécution : les fenêtres de donjon rejouent l'action retenue à l'ouverture (`ScriptWindow.DungeonAction`,
`IDungeonService.ExecuteAsync`) ; la plume et le canal exécutent leur Lua officiel dans le bac à sable
(`recall_feather`, `on_channel_set`, ce dernier ajouté à l'export par `tools/export_npc_runtime.py`).

## 3. Qui ouvre une fenêtre (correction de relecture)

Chez l'officiel, **`warp_to_instance_dungeon` et `warp_to_secret_dungeon` téléportent sans fenêtre**
(`SCRIPT_WarpToInstanceDungeon`, `SCRIPT_WarpToSecretDungeon`) ; seules `enter_instance_dungeon`,
`enter_secret_dungeon`, `leave_instance_dungeon` et le cas raid de `enter_dungeon` ouvrent une fenêtre native.
Le premier jet faisait confirmer tout `warp_to_instance_dungeon(…)` choisi dans un menu (les 20 menus de
`NPC_Dungeon`) et le donjon secret possédé : c'est corrigé par `NpcDialogService.OpensConfirmation`.

Conséquence : **aucun Lua Epic 7 n'appelle les trois fonctions `enter_*`/`leave_*`** (dump Part 4, `trunk` et
`trunk_testserver`) ; les confirmations d'entrée et de sortie de l'époque passent par
`dlg_special('confirm_window', …)`, la 3003 générique (`ETC_dungeon_prop.lua`). Les fenêtres natives d'instance
et de donjon secret sont donc branchées mais ne s'ouvrent que si un script les appelle. En jeu, s'ouvrent : la
fenêtre de raid, la plume de rappel et la saisie de canal.

## 4. Plume de rappel

`RecallFeatherService` : `var1 = 1` vise un membre du groupe (mort, hors groupe, entrepôt ouvert ou soi-même →
`AccessDenied`), sinon tout le groupe en ligne (`NotActable`, `TargetInHuntaholic`) ; refus dans un donjon de
siège ou de raid (`NotActableHere`) et en instance (`NotActableInInstanceDungeon`) ; garde de niveau du donjon
où se tient le lanceur. Les jugements ont lieu **avant** la consommation de l'objet. Écart : les lignes
`@563` de l'officiel (« niveau trop bas ») ne sont pas envoyées ; « entrepôt ouvert » lit
`StorageSecurityCheck`.

## 5. Canaux

Ce serveur n'a aucun canal public : `get_layer_of_channel` et `get_user_count_in_channel` rendent 0,
`get_min/max_channel_num` 1, comme l'officiel pour un canal non alloué. `on_channel_set` répond donc « canal
vide » (`@90300518`) sans téléporter.

## 6. Vérifications

`ScriptWindowsTests` (tailles et offsets de chaque 3003, rappel accepté une fois, Lua et dépassements refusés,
fenêtre remplacée ou personnage changé), `DungeonIntegrationTests` (menu `warp_to_instance_dungeon` exécuté
directement ; règle `OpensConfirmation`).

## NON ÉTABLI

- Le rendu des fenêtres natives dans le client 7.3 n'a pas été vu en jeu.
- Le `strstr` de l'officiel accepte un rappel qui *contient* le déclencheur ; l'égalité stricte d'ici pourrait
  refuser une variante d'espacement que le client n'a pas été mesuré à produire.
