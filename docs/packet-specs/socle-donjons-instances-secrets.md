# Donjons : accès, instances et secrets

## Ressources

Le catalogue embarqué est produit par `python tools/export_dungeon_resources.py` depuis les exports
Arcadia : 81 variantes régionales pour 21 donjons, 9 instances, 37 difficultés, 1 654 lignes de
réapparition et 6 donjons secrets. Les 12 gestionnaires de donjon conservent l'identifiant de leur
donjon depuis `NPCResource.contact_script`.

Les destinations de sortie viennent de `exit_dungeon` dans le script officiel `ETC_dungeon_prop.lua`.
Le catalogue historique des props conserve ses coordonnées pour compatibilité ; les accès en production
passent par `DungeonService` et le catalogue régional.

Configuration facultative, dans `Dungeons` :

```json
{
  "LocalFlag": 1,
  "TimeZone": "Europe/Paris",
  "ClosedDungeons": []
}
```

La région détermine les horaires utilisés. Une région sans ligne correspondante empêche le démarrage
du catalogue. Les horaires sont exprimés en secondes depuis lundi dans le fuseau configuré ; les deux
bornes sont incluses. Les entrées publiques restent ouvertes hors des horaires de raid et de siège,
conformément à `SCRIPT_WarpToDungeon`.

## Parcours livrés

| Action | Contrôles et destination |
| --- | --- |
| `enter_dungeon(id)` | Donjon connu et ouvert, personnage vivant, niveau au moins `dungeon_level - 40`. Couche publique 0 ; rejoint le raid privé existant de sa guilde si le personnage a un groupe de cette guilde et que les horaires le permettent. |
| `exit_dungeon(id)` | Destination extérieure officielle et couche 0. |
| `warp_to_instance_dungeon(id,type)` | Niveau dans `[min_level,max_level)`, difficulté connue, aucune instance déjà occupée par ce joueur. Salle partagée par les membres du groupe ; salle individuelle sans groupe. |
| `enter_vulcanus()` | Niveau adapté, absence de groupe, consommation atomique de 20 clés 1000401, éventuellement sur plusieurs piles. Une entrée annulée pendant la sauvegarde rembourse les clés. |
| `exit_instance_dungeon()` / `leave_instance_dungeon()` | Retour à la position extérieure enregistrée au début de la visite. |
| `begin_dungeon_raid(id)` | Chef de groupe, guilde, membres en ligne de cette guilde, horaires de raid et guilde différente du propriétaire. Salle privée par guilde et donjon, monstres avec statistiques de raid. |
| `warp_to_siege_dungeon(id)` | Groupe de la guilde, horaires de siège, guilde propriétaire ou attaquante dans Telecaster. Couche 1 commune ; positions de défense et d'attaque distinctes. |
| `warp_to_secret_dungeon(id)` / `enter_to_secret_dungeon(prop)` | Donjon secret connu et ouvert, niveau requis ; couche publique 0. Les six identifiants de portail officiels sont résolus. |
| `scf_teleport_to_owned_secret_dungeon()` | Lecture du donjon réellement possédé par la guilde dans Telecaster, puis choix du secret correspondant et contrôle de son niveau/fermeture. |

Les gestionnaires proposent les accès selon les contrôles immédiats ; l'appartenance réelle du donjon
est revérifiée lors de la sélection. Aucun texte reçu du client n'est exécuté comme Lua. Le dialogue
doit avoir annoncé exactement l'action sélectionnée. Les réponses de base devenues obsolètes après un
changement de dialogue, de personnage ou de position ne provoquent aucune téléportation.

Les pages `question_secret_dungeon_*` donnent les explications officielles. Leurs arguments, perdus lors
de l'export des concaténations Lua, sont reconstruits depuis le PNJ contacté. Elles n'ouvrent pas un donjon.

## Isolation et départ

Les salles privées utilisent les couches 2 à 127, compatibles avec les champs signés du protocole.
Une couche active n'est jamais réattribuée. Une difficulté différente ne remplace pas une salle occupée.
Les monstres d'instance proviennent des lignes non contrôlées par un scénario Lua ; leurs périodes de
réapparition sont conservées, et une période nulle donne un monstre à apparition unique.

Le changement de couche intervient après les notifications de départ de l'ancienne vue. Les compétences
de zone et multi-coups fonctionnent dans les couches privées et filtrent leurs cibles par couche.

La dernière sortie supprime les monstres, leurs renforts et le butin de la salle avant de réutiliser la
couche. Une téléportation extérieure, une résurrection et la déconnexion libèrent également la visite.
Au retour au lobby ou à la déconnexion, la position extérieure est restaurée avant la sauvegarde.
Une couche privée retrouvée en base après un arrêt brutal est abandonnée à la connexion suivante,
avec retour au point d'apparition public.

Toutes les cinq secondes, le serveur vérifie le groupe, la guilde, la fermeture du donjon et les horaires
de raid/siège. Un joueur qui ne satisfait plus les règles retourne à sa position d'origine.

## Vérification en jeu

1. Au téléporteur des instances, tester les cinq destinations et les quatre difficultés : 20 choix.
2. Entrer avec deux membres du même groupe ; un autre groupe doit voir ses propres monstres.
3. Tester sous le niveau minimum, un changement de difficulté pendant une visite, puis quitter le groupe.
4. Quitter une instance, revenir au lobby et se reconnecter : vérifier la position extérieure.
5. Tester Vulcanus sans clés, avec 19 clés, avec 20 clés réparties sur plusieurs piles, puis en groupe.
6. Chez un gestionnaire, parcourir les explications du secret ; tester le raccourci avec une guilde
   propriétaire puis avec une autre guilde.
7. Tester raid/siège pendant et hors des horaires configurés, avec les guildes propriétaire,
   attaquante et une guilde étrangère.

## Scénario Vulcanus (2026-10-04)

Port du Lua Epic 7 `ETC_dungeon_prop.lua` (`on_create_vulcanus_instance`, `vulcanus_check_respawn_group_clear`,
`vulcanus_clear_reward`, `enter_other_indun`, `warp_indun`, `warp_floor`) dans `VulcanusScenario` / `VulcanusRules`.

- **Données** (`export_dungeon_resources.py`) : `respawn_group` de chaque ligne, les props que l'instance pose sur sa
  couche (`InstanceDungeonHealingPropResource` : les 12 portes de sortie 126023 de chaque difficulté de Vulcanus), les
  `FieldPropResource` de ces props et des portes d'étage 126024-126027 (script compris), et les 56 récompenses
  (14 difficultés × 4 étages) lues dans `vulcanus_clear_reward` (`--lua`).
- **Création** : les monstres d'un groupe apparaissent groupe par groupe (chaque monstre connaît sa salle), les props
  de l'instance sont posés sur la couche (`IDynamicFieldProps`, retirés avec la salle), `Vul1`..`Vul3` = 0 et la
  difficulté = le type de la salle.
- **Salle vidée** (`on_dead_script`, via `DungeonEvents` depuis `CombatService`) : le bit de la salle (1/2/4/8) dans le
  drapeau de l'étage, une seule fois ; à chaque joueur de l'instance l'EXP, les JP et l'or de la difficulté et de l'étage,
  et `@9813`/`@9250` hors salle du boss ; la porte de l'étage (126024/25/26, sortie 126027 après le boss, hauteur 10 ou
  35) à l'endroit du dernier monstre.
- **Porte d'étage** (`enter_other_indun(d, étage, suivant, clés, clés suivantes)`) : `@9812`, puis la fenêtre
  `TYPE_OTHER_INSTANCE_DUNGEON_CONFIRM_WINDOW` (10), titre `InDun`, texte `Warp`, deux entrées étiquetées par le nombre
  de clés : `warp_indun(d, étage)` (une autre salle, `-1` une fois l'étage fini) et `warp_indun(d, suivant)`.
- **Trajet** (`warp_indun` → `warp_floor`) : une salle tirée parmi celles dont le bit est libre (le boss : la seule),
  clés prises dans le sac en une sauvegarde (20 × 1000401, 10 × 1000402, 5 × 1000403, 1 × 1000404 ; `@9810` sans elles),
  téléport sur la couche de l'instance ; à l'étage du boss, `@90604914` puis `@90604915`.
- **Portes de sortie** : `exit_instance_dungeon(prop)` était refusé par `PropScript` (il n'acceptait pas l'argument) :
  les portes de sortie n'avaient jamais fonctionné. Elles ramènent maintenant à la position d'entrée.
- **Non porté** : l'interface de mission (`send_mission_*`, lignes `MTITLE|…` de `@DUNGEON`, type 170) — le client 7.3
  n'en connaît aucune ligne ; le Lua `cprint` écrit dans la console du serveur 2015 (aucune sortie branchée), ses
  lignes `@…` sont pourtant destinées au joueur : elles partent ici comme les `message()` (`@SCRIPT`, type 40), choix
  du dépôt. Les scénarios Cubric (30000, `cubric_*`) et ses `warp_gate` ne sont pas portés : la branche Cubric est
  postérieure au client 7.3.

## Portails secrets (2026-10-04)

Les gestionnaires l'expliquent (`@90408807`/`@90408808`/`@90408811`) : tuer le **monstre clé**, qui fuit sans
attaquer, ouvre l'espace dimensionnel du donjon secret pour une dizaine de minutes. Ni le C++ ni le Lua disponibles
ne le codent en dur : c'est la donnée.

- **Monstres clés** : six `MonsterResource` ont pour `script_on_dead`
  `add_field_prop(portail, 600, #@pos_x@#, #@pos_y@#, #@pos_layer@#, 16, 0, 0, 0, 1, 1, 1)` — 10070016 → 70191
  (Vallée de cristal), 10090023 → 120291 (Palmir), 10125009 → 110191 (Elcasia), 10146009 → 100191 (Dragon blanc),
  10158006 → 90191 (Dragon noir), 10165006 → 80191 (Dragon de sable). `SCRIPT_AddFieldProp` : durée × 100 ticks, puis
  position, couche, hauteur, rotation, échelle.
- **Leur apparition** : `random_respawn.lua` (le serveur 2015 ; le dump Epic 7 n'a pas ce fichier), entrées 1 à 8 —
  un exemplaire tenu en vie (`prespawn_count` 1), revenu 60 000 ticks (10 min) après sa mort dans une boîte tirée au
  hasard parmi celles de sa zone (6 à 9 boîtes). Rien d'autre ne les faisait apparaître : sans eux, aucun portail.
- **Données** : `export_dungeon_resources.py` relève les `add_field_prop` des `script_on_dead` (`DeathProps`), les
  entrées de `random_respawn.lua` dont le monstre est l'un d'eux (`KeyMonsterRespawns`, `--random-respawn-lua`) et les
  modèles des six portails (`enter_to_secret_dungeon(prop)`, sort 6904).
- **Exécution** (`SecretPortals`, branché sur `DungeonEvents` et le balayage de 5 s) : la mort pose le portail, prop de
  couche (`IDynamicFieldProps`, `ExpiresAt`), là où le monstre est tombé, et le pousse aux joueurs qui voient l'endroit ;
  il disparaît 600 s plus tard (`RespawnedFieldPropManager`). Le cadavre reste jusqu'à la réapparition (le combat lit
  encore l'instance après le crochet). Le portail mène au donjon secret par le chemin existant (`warp_to_secret_dungeon`).
- **Non porté** : la fuite du monstre clé (son IA est celle d'un monstre non agressif) et le reste de
  `random_respawn.lua` (Baphomet et ses suivants, entrées 9+), sans lien avec les portails.

## Limites conservées

Le raccourci de guilde est utilisable dès maintenant.

La création de guilde, les alliances, les équipes d'attaque intergroupes, l'inscription officielle aux
raids, leur classement, les taxes et les objectifs du siège restent des lots de guilde. Les contrôles
actuels utilisent le groupe ordinaire et les guildes propriétaire/attaquante enregistrées en base.
Le droit de commencer un raid est ici accordé au chef d'un groupe de sa guilde, sans registre d'inscription.

## Tests automatisés

Les tests couvrent le catalogue régional, les bornes horaires et le fuseau, les niveaux, les 20 choix,
les accès de guilde, l'isolation des groupes, les sorties/déconnexions, les clés et leur remboursement,
les dialogues autorisés, les pages d'information, les monstres/renforts, les réapparitions et le
nettoyage du butin. Les compétences de zone et multi-coups sont aussi testées sur une couche privée.

## Libellés du gestionnaire (xHHwc9Z2, 2026-10-05)

*Remplacé le même jour* : le gestionnaire exécute son Lua officiel (`socle-dialogues-guilde-siege.md`) ; la section ci-dessous décrit le menu écrit qu'il remplace.

Tous les libellés transmis utilisent les chaînes officielles, sous `GameServer/Resource/Script/` :
`NPC_QuestClient.lua:1469-1477` (informations et fermeture), `:1836` (`@90010116`, or),
`:1839` (`@90010125`, lak), `:1955` (`@90010119`, taxe +), `:1957` (`@90010120`, taxe -).
`NPC_TeleportTown.lua:210` établit `@90605270` pour le secret possédé.
`NPC_Dungeon.lua:46,55,64` établit respectivement `@1090600104` (130000), `@1060600104`
(130300), `@1070500104` (130200). Aucun identifiant générique de siège n'est extrapolé.
Ces raccourcis restent une présentation aplatie du Lua ; les contrôles de sélection restent actifs.

**NON ÉTABLI** : libellés de menu `enter_dungeon`, `begin_dungeon_raid`, inscription et annulation
au raid chez ce gestionnaire, et accès au siège des autres donjons. Ces entrées françaises ajoutées
par Navislamia ne sont plus annoncées ici. Les commandes `/graid` et `/graidcancel` restent disponibles,
et les accès officiels par props/téléporteurs conservent leur chemin. Ne pas inventer de chaîne.
Tests : quatre variantes de `DungeonTests.Siege_manager_emits_only_official_client_labels`.
Vérifier en jeu les textes localisés, les taxes et le téléporteur du siège pendant ses horaires.
