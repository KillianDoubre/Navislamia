# Socle : dialogues de guilde et du gestionnaire de siège en Lua officiel (2026-10-05)

Carte Trello `g2ZL9GqD`. Les officiers de guilde et les gestionnaires de siège étaient des menus écrits dans le code
(`GuildService.Dialogs`, la branche `NPC_dungeon_siege_manager_contact` de `NpcDialogService.TryShow`), en partie en
français. Ils exécutent désormais le **Lua officiel**, avec les fonctions moteur qu'il appelle portées du C++ officiel.

## 1. Sources

- Lua : Epic 7 Part 4, `trunk/NPC_CreateGuild.lua` et `trunk/NPC_QuestClient.lua` (plus proches du client 7.3 que les
  copies de 2015, mêmes ensembles de fonctions). `tools/export_guild_dialogs.py` en extrait 53 fonctions — tout ce
  qu'atteignent les contacts des officiers (`NPC_CreateGuild_*_contact`), `NPC_dungeon_siege_manager_contact` et les
  deux rappels de fenêtre (`on_create_guild`, `on_create_alliance`), appelé ou proposé en menu — dans
  `Game/Scripting/Scripts/guild_dialogs.json`, embarqué (`Navislamia.GuildDialogs.json`).
- Fonctions moteur : source 2015, `Game/Script/ScriptGuild.cpp` (création, alliance, codes `GUILD_CREATE_*`,
  `ALLIANCE_CREATE_*`, `ALLIANCE_DESTROY_*` en tête de fichier) et `ScriptPlayer.cpp:3345-3729` (relation au donjon,
  taxes, abandon) ; enregistrement dans `CaptainHerlockServer.cpp:654-877` ; `GameRule::IsValidName`
  (`GameRule.cpp:403`) ; `onGuildCreate` (`GameMessage.cpp:4312`).

## 2. Déroulé

- **Contact** : `NpcDialogService` reconnaît une fonction de contact de ces familles et lance
  `GuildService.RunDialogAsync` avec l'appel complet — `NPC_CreateGuild_Deva_contact()`, ou
  `NPC_dungeon_siege_manager_contact( 130000 )` avec le donjon du PNJ (`DungeonCatalog.NpcDungeons`, le catalogue ne
  garde que le nom de la fonction).
- **Choix** : un déclencheur annoncé dont la fonction appartient aux familles repart dans le même hôte, consommé une fois.
- **Fenêtre** : `show_guild_create()` / `show_alliance_create()` ouvrent la saisie (650/660) ; le nom revient en
  `/gcreate nom` / `/gacreate nom` et lance `on_create_guild( 'nom' )` / `on_create_alliance( 'nom' )` avec l'officier de
  la fenêtre, tant que sa page est la page courante. **Une apostrophe, une barre oblique inverse ou un caractère de
  contrôle est refusé** (l'officiel ferme la connexion), le nom ne sortant jamais de sa chaîne Lua.
- **Exécution** : sous le verrou de guilde puis le verrou du personnage (l'ordre de toute opération de guilde), dans
  une transaction ; les effets (or, création, taxe, abandon) sont validés avec la page, ou pas du tout. La page revient
  à `NpcDialogService`, qui la rend comme les autres pages Lua (offres de quête sur une page `dlg_text`), applique le
  warp de `click_dungeon_drop` et la page de quête de `show_quest_info_without_npc`.

## 3. Fonctions moteur

| Fonction | Règle officielle portée |
|---|---|
| `get_dungeon_relation(d)` | 0 sans guilde ; propriétaire : chef 1, gestionnaire (`PRA_DUNGEON_MANAGEMENT`) 9, membre 2 ; alliée du propriétaire : chef 3, sinon 4 ; guilde du raid : 5/6 ; alliée du raid : 7/8 |
| `get_tax_rate`, `set_tax_rate(d, r)` | taux 1-10, guilde propriétaire et permission de gestion |
| `get_tax_amount`, `get_tax_chaos_amount` | or et chaos de la guilde (`Guilds.Gold`/`Chaos`) |
| `draw_tax` | permission ; refus si l'or porté dépasserait le plafond ; tout l'or |
| `draw_tax_chaos` | permission ; ce que le personnage peut porter |
| `drop_dungeon_owner_ship` | permission ; donjon de la guilde et possédé ; + gardes du dépôt (pas de raid, siège ou visite en cours), blocage de sept jours |
| `get_own_guild_name(d)` | nom du propriétaire, sinon la chaîne 633 |
| `check_valid_guild_name`, `create_guild` | 1-16 lettres ou chiffres ASCII, mot interdit, déjà en guilde (1), blocage après départ (5), nom pris (3) ; **le Lua débite les 100 000 or** (`set_value`) après un succès |
| `create_alliance`, `check_valid_alliance_name`, `destroy_alliance` | codes `ALLIANCE_*` ; capacité initiale 3 (`DEFAULT_MAX_ALLIANCE_CNT`) |
| `is_guild_leader`, `is_alliance_leader`, `get_max_alliance_member_count`, `increase_max_alliance_member_count` | comme l'officiel ; le Lua facture 1 000 000 or l'emplacement |
| `get_quest_progress`, `show_quest_info_without_npc` | service de quêtes (Rondoh, 6012) |
| `cprint` | journal serveur (`LuaVM` : `_outputConsole`), jamais une ligne de chat |

## 4. Retiré

Le menu de l'officier (« Creer une guilde… », « Ouvrir la guilde ») et la confirmation française, les déclencheurs
`guild_confirm_*` et `guild_dungeon_*`, le menu écrit du gestionnaire de siège. Les commandes de chat de Codex
(`/gtax`, `/gwithdraw`, `/gdropdungeon`, `/gacreate`…) restent. La règle de nom des guildes et alliances créées par les
officiers est désormais l'officielle (1-16, sans espace) ; `GuildRules.ValidName` reste la garde des lignes de chat.

## 5. Écarts et NON ÉTABLI

- `get_own_guild_name` d'un donjon sans propriétaire rend `@633` : l'officiel pousse le texte de sa table de chaînes ;
  que le client résolve cette référence à l'intérieur d'un `sconv` n'est pas vérifié.
- `drop_dungeon_owner_ship` garde les refus du dépôt pendant un raid, un siège ou une visite ; l'officiel les laisse à
  `DungeonManager::DropDungeonOwnership`, non lu. La ligne `GINFO` officielle est remplacée par la resynchronisation
  ordinaire de la guilde.
- Le contact de Rondoh (6012) et de la relique (7037) n'a pas été vu dans le client 7.3.

## 6. Vérifications

`Tests/Game/GuildTests.Dialogs.cs` : menu de l'officier, création complète par la fenêtre avec le débit du Lua, nom
invalide et niveau 19, alliance créée puis détruite, pages du gestionnaire pour un étranger (donjon gardé, aucun warp),
le chef propriétaire règle la taxe, la tire et abandonne le donjon (warp couche 0), un simple membre ne voit pas la
gestion. `GuildTests` existants passés sur le flux Lua.

En jeu :
1. Officier de sa race : menu officiel ; créer une guilde au niveau 20 avec 100 000 or ; nom avec espace → refusé.
2. Chef de guilde : Alliance → créer, puis détruire.
3. Gestionnaire de siège d'un donjon possédé : Gestion → taxe +1, collecte de l'or ; Abandonner → le donjon est libre.
