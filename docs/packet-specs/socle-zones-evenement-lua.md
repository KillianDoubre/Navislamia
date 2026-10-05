# Zones d'événement : ressources et exécution Lua

Lot du 2026-10-04. Complète la fiche historique `socle-zones-evenement.md`.

## Sources importées

`EventAreaResource.rdu`, plus ancienne table complète trouvée localement :
`A:\Rappelz Kiff\Rappelz\Rappelz\Epic 8 Part 2\trunk\Server\RDU`, **187 lignes**.
Les fonctions Lua proviennent d'abord d'`Epic 7 Part 4/trunk`, puis des copies Epic 8 Part 2
pour les fonctions absentes : **147 fonctions**, avec leur fermeture de dépendances.
La migration Arcadia `EventAreaResources` charge les 187 lignes depuis un SQL embarqué.
`tools/export_event_areas.py` régénère CSV, SQL et fonctions JSON ; `tools/import_epic7.py` sait
réimporter les colonnes et les tableaux depuis le CSV généré.

Colonnes : identifiant, début/fin en secondes dans la journée, niveaux, masque race/classe 64 bits,
six conditions et leurs deux valeurs chacune, limite d'activations, script d'entrée, script de sortie.
Les polygones restent ceux des `.nfe` du client installé : un même identifiant peut avoir plusieurs
polygones, tous conservés par le chargeur.

Sources du serveur officiel, sous `program/server/GameServer/Game` :

- `Resource/MapLoader.cpp:758` : chargement des 27 colonnes ; scripts `0`/vides.
- `Resource/GameContent.h:453` : masques 64 bits des races et métiers, conditions 0..6.
- `Resource/GameContent.cpp:72` : conditions d'activation ; `:2244` : index et compteur d'entrée.
- `Message/GameMessage.cpp:7712` et `:7737` : entrée autorisée, Lua, mémorisation de l'entrée,
  sortie seulement après entrée ; incrément du compteur quand une limite existe.
- `Script/ScriptNPC.cpp:348` : objets de terrain, durée en secondes convertie en ticks × 100.

## Cycle exécuté

1. Le serveur compare ses positions aux polygones à l'entrée en jeu, au déplacement, aux mises à jour
   de région et à la téléportation. Les paquets 15/16 sont des déclencheurs supplémentaires.
2. Une trame de 15 octets porte `event_area_id` int32 en 7 et `area_index` int32 en 11. Taille,
   identifiant/index chargé et position sont vérifiés ; aucun texte Lua ne vient du paquet.
3. Toutes les zones contenant le joueur sont suivies, y compris les recouvrements. Sorties avant
   entrées ; une entrée ou une sortie n'est pas répétée par une seconde notification identique.
4. Sous `CharacterGate`, le script d'entrée vérifie les horaires locaux, les niveaux, les masques
   race/classe et les six conditions : quantité d'objet, progression de quête, compétence,
   objet équipé, invocation active, état actif. Une condition inconnue est ignorée, une race ou un métier
   hors des listes officielles n'est pas refusé (`IsActivatable` n'a pas de cas par défaut, §Relecture).
5. Le compteur `event_area_<id>_count` est enregistré dans `Character.FlagList`, comme compteur
   persistant par joueur/zone. Il est incrémenté uniquement si l'exécution réussit.
6. Le script officiel s'exécute dans une VM Lua neuve, sans `io`/`os`, avec un budget de 100 000
   instructions. API du personnage identique au bac à sable PNJ : flags, valeurs, objectifs,
   messages, objets, buffs et téléportation. `del_flag`, `random`, `get_state_level` sont disponibles.
7. Les objectifs de quêtes 701 sont préparés dans la même unité de travail que les flags et le
   compteur : l'échec d'un objectif ne consomme pas une activation à usage unique. Messages de statut
   et effets de monde sont publiés après sauvegarde.
8. Une sortie exécute `LeaveHandler` uniquement si l'entrée avait été activée. La file est ordonnée
   par session ; la déconnexion la ferme et attend la fin des sorties. Une nouvelle entrée en jeu
   rouvre le suivi. Une opération retardée ne s'applique pas au personnage suivant de la connexion.

`add_field_prop` crée un objet de terrain sur la couche et gère sa durée. Pour les scripts Epic 7
exportés, la forme `add_npc(x,y,code_monstre,nombre,durée)` est raccordée aux monstres temporaires
du monde, avec durée en ticks ; cette forme de compatibilité diffère du `SCRIPT_AddNPC` à trois
arguments de la copie C++ disponible, qui crée un PNJ. Les scripts exportés utilisent des codes
de monstres dans cette forme à cinq arguments. Les spawns arrivent après sauvegarde et disparaissent
à expiration ; les objets de terrain sont retirés à l'usage ou à expiration.

## Relecture du 2026-10-05 (carte Trello `qnvX3BU6`)

La carte demandait l'import, les conditions, les scripts et le compteur : tout était déjà livré par ce lot. La relecture
contre `GameContent.cpp:72-230` a corrigé deux écarts (`EventAreaActivation`) : une race hors Gaïa/Déva/Asura et un
métier hors des 42 listés étaient refusés, alors que les deux `switch` officiels n'ont pas de cas par défaut ; une
condition inconnue est ignorée (déjà corrigé le 2026-10-04). `EventAreaActivationTests` couvre maintenant chaque
condition : fenêtre horaire, progression de quête (la seule condition des données : 31 lignes), compétence, objet
porté, invocation, état (présent 1 / absent 2), première pile d'objet, condition inconnue, race et métier hors listes.

**Données confrontées aux cartes 7.3** (`DevConsole/Maps/*.nfe`, 183 identifiants) : **167** zones ont un polygone et
une ligne, et sont donc actives. Les **18 scripts introuvables** (`10110`-`10119`, `10126`-`10129`, `10146`-`10149`)
n'ont **aucun polygone** dans les cartes 7.3 — la numérotation des polygones saute exactement aux mêmes endroits — et
n'existent dans aucune arborescence officielle cherchée (Lua 2015 `Resource/Script`, Epic 7 Part 4 à Epic 9 Part 1,
`Rappelz_Release`) : ces lignes ne se déclenchent jamais, ce n'est pas un manque. Restent sans polygone 20
(`Quest_Link_18_1`) et 10152 ; restent sans ligne, donc sans effet, les polygones 1000, 9000, 9001 et 120101-120113.

À tester en jeu : la zone **10101** (vers x 20568, y 30037, carte m001_001) avec la quête 3217 en cours (statut 1) :
l'entrée valide l'objectif et affiche `@91000797`, une seule fois (`count_limit` 1).

## Limites de données explicites

La table contient 18 noms **absents de toutes les copies Lua recherchées** :
`mainquest2_region_espoir_level_10110` à `10119`, `10126` à `10129`, `10146` à `10149`.
Les lignes sont conservées pour un futur remplacement officiel. Elles échouent dans le bac à sable,
avec un journal de refus et sans compteur consommé ; aucun effet n'est fabriqué. Plusieurs fonctions
voisines présentes dans les sources sont elles-mêmes vides.

Dans la table importée, les scripts de sortie sont vides ; le moteur les prend en charge et les tests
exercent une vraie sortie Lua. Une ressource ne crée pas de polygone manquant dans les cartes 7.3.
Le jeu n'exécute donc que les zones dont la géométrie est effectivement chargée.

## Tests

`EventAreaScriptTests` : compilation du programme officiel, sandbox, entrée/sortie réelle avec flags,
refus des claims hors polygone, limite persistante, quête 701 rendue terminable, rollback du compteur
et des flags sur objectif inconnu, buff officiel 1011, spawns, recouvrements et sortie/reconnexion.
Le chargeur `.nfe` est testé avec deux polygones du même ID et des indices invalides.
`CommunityPostgreSqlTests` migre Arcadia et relit les 187 lignes, tableaux et handlers en PostgreSQL.
Les tests historiques de géométrie et de paquets sont conservés.
