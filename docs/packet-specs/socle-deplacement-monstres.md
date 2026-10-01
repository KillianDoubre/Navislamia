# Socle — vitesse des monstres, obstacles et recherche de chemin

| | |
|---|---|
| Paquets | `TM_SC_MOVE` (8), à plusieurs points de passage pour un contournement — **aucun identifiant nouveau** |
| Données | `.nfa` du client 7.3 (polygones d'attribut, 83 cartes), extraits par `tools/Export-FieldProps --extract-maps DevConsole/Maps` (dossier ignoré par git) |
| Référence d'autorité | `CaptainHerlockServer.exe` 2012-11 : `StructMonster::onBeforeCalculateStat` (`0x1400575b0`), `StructCreature::GetRealMoveSpeed` (`0x140080830`), `StructMonster::processMove` (`0x140161460`), `GameContent::IsBlocked` (`0x140190000`), chargeur des polygones (`0x1401bbc00`), `AI_processAttack` (`PathFinder`) |
| Code | `Game/Maps/Collision/` (`CollisionMap`, `PathFinder`, `WorldCollision`), `MonsterWorldState`, `MonsterAiService`, `MonsterMovementService`, `MonsterInstanceFactory`, `MonsterCombatStats` |
| Tests | `Tests/Game/MovementAndDeathTests.cs` |

## 1. Vitesse

- La vitesse d'un monstre est sa `run_speed` : `onBeforeCalculateStat` ajoute `run_speed − 120` aux 120 de toute
  créature (NGemity `Monster.cpp:1306-1307`), plancher 10, états compris (un ralentissement compte).
- L'octet de `TS_SC_MOVE` vaut `vitesse / 7` (la division par 7 de `GetRealMoveSpeed`) : 120 donne 17, soit
  ~57 unités/s avec l'échelle de `MonsterMovement` (30 ticks), 4,7 m/s à 12 unités par mètre.
- Promenade : `run_speed / 7`. Poursuite : la même × 1,00 à 1,09 (tirage officiel). Retour : `2 × vitesse / 7`.
  Elles remplacent les constantes 25, 40 et 80.

## 2. Obstacles

- Un `.nfa` contient un `int32` de nombre de polygones, puis par polygone un `int32` de points et autant de
  paires `int32` en unités d'attribut. Monde = indice de carte × 16 128 + valeur × (42 / 8), la transformation
  du chargeur officiel. Les 20 261 polygones sont tous valides au sens officiel (pas de point répété, pas
  d'auto-intersection), et `IsBlocked` vaut « dans un polygone au moins ».
- Vérifié sur les objets du décor (positions du client, validées en jeu) : 668 sur 3 189 dans un obstacle avec
  cette transformation, plus de 1 800 avec les autres (axe Y inversé, axes échangés).
- **Les zones d'apparition de la 9.4 débordent sur les obstacles** : 52 % de leurs centres sont bloqués. Un
  monstre tire donc son point d'apparition jusqu'à 16 fois hors obstacle, et garde le dernier sinon.
- `Map:NoCollisionCheck` (le `GameRule::bIsNoCollisionCheck` officiel) ou un dossier `Maps` absent laissent la
  carte vide : tout est libre, comme avant.

## 3. Recherche de chemin

- Promenade : une destination bloquée, ou une ligne qui traverse un obstacle, est refusée ; le monstre réessaie
  au tour suivant (règle officielle et NGemity pour la destination).
- Poursuite : ligne libre → droit ; sinon A* sur un graphe de visibilité (départ, but, sommets des obstacles
  voisins décalés de 6 unités vers l'extérieur) ; pas de chemin → le monstre ne bouge pas.
- Retour : même recherche, ligne droite en dernier recours (un monstre bloqué hors de chez lui garderait son
  état de combat).
- Un obstacle qui contient le départ ou le but est ignoré : un monstre coincé peut toujours en sortir.
- Borné à 240 nœuds et 400 000 tests de segment ; au-delà, pas de chemin.
- `TS_SC_MOVE` porte tous les points de passage ; le serveur interpole la même ligne brisée
  (`MonsterMovement.PositionAlong`).

## 4. Écarts

- L'algorithme officiel (`X2D::PathFinder`) n'est pas porté, seulement le résultat attendu : des points de
  passage qui n'entrent dans aucun obstacle.
- Le joueur n'est pas contrôlé : le client gère sa propre collision.
- Les couches (donjons) ne sont pas distinguées : un `.nfa` n'en porte pas.

## 5. Constats annexes

- `GameModule.LoadMaps` lisait `Map:SkipLoading` à l'envers (vrai chargeait, faux sautait) et `float.Parse`
  dépendait de la culture (« 0.7 » refusé en français) : les cartes ne s'étaient jamais chargées. Les deux sont
  corrigés ; avec la configuration locale actuelle (`SkipLoading: true`), lieux et zones d'événement ne se
  chargent donc plus, les obstacles si.
- La vitesse renvoyée pour un joueur (`EchoedMoveSpeed = 100`) et celle du familier (120) ne suivent pas la
  règle `vitesse / 7` (17 pour 120) : NON ÉTABLI, à vérifier en jeu avant d'y toucher.
