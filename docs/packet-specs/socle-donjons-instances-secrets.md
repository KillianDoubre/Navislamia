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

## Limites conservées

Ce lot implémente les accès et la vie des salles. Les scripts `on_create`, `on_join`, `on_leave`,
les réapparitions contrôlées par Lua et les objectifs particuliers des instances restent à raccorder.
Le déclenchement des portails secrets par leurs monstres clés n'est pas encore reproduit ; leur action
d'entrée est résolue lorsqu'un portail est présent. Le raccourci de guilde est utilisable dès maintenant.

La création de guilde, les alliances, les équipes d'attaque intergroupes, l'inscription officielle aux
raids, leur classement, les taxes et les objectifs du siège restent des lots de guilde. Les contrôles
actuels utilisent le groupe ordinaire et les guildes propriétaire/attaquante enregistrées en base.
Le droit de commencer un raid est ici accordé au chef d'un groupe de sa guilde, sans registre d'inscription.

## Tests automatisés

Les tests couvrent le catalogue régional, les bornes horaires et le fuseau, les niveaux, les 20 choix,
les accès de guilde, l'isolation des groupes, les sorties/déconnexions, les clés et leur remboursement,
les dialogues autorisés, les pages d'information, les monstres/renforts, les réapparitions et le
nettoyage du butin. Les compétences de zone et multi-coups sont aussi testées sur une couche privée.
