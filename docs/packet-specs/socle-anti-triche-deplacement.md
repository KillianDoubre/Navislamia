# Socle : anti-triche des déplacements — vitesse, téléport, obstacles (2026-10-05)

Carte Trello `b2qPzAcW`. Avant ce lot, le serveur prenait pour vraie toute position que le client lui donnait
(`TM_CS_MOVE_REQUEST`, `TM_CS_REGION_UPDATE`, 900) : un client modifié pouvait se téléporter, courir plus vite que
sa vitesse ou traverser un mur.

## 1. Sources

Source officielle 2015, `Game/Message/GameMessage.cpp` :

- `onMoveRequest` (`:370-579`) : créature morte ou hors du monde → `return` ; taille de trame ; le joueur, son
  invocation ou son familier ; vitesse = `GetRealMoveSpeed()` du serveur, jamais celle du client ; puis
  `GetValidWayPoint` ; dernier point à plus de `g_fMapLength` → `return` ; assis → `StandUp` ; `SetMultipleMove`
  depuis la position déclarée par le client (`startPos`).
- `GetValidWayPoint` (`:261-368`) :
  1. position déclarée hors de la carte (`g_nMapWidth`/`g_nMapHeight` = 700 000) → `ACCESS_DENIED` ;
  2. position déclarée à plus de `GameRule::VISIBLE_RANGE` (525) de **la position que le serveur calcule**
     (`GetCurrentPosition`, son interpolation du dernier déplacement accepté, à sa vitesse) → `ACCESS_DENIED` ;
  3. joueur : un obstacle entre la position serveur et la position déclarée (`CollisionToLine`) →
     `ACCESS_DENIED`, et le déplacement devient **un pas vers la position serveur** ; si la position serveur est
     elle-même dans un obstacle, rien ;
  4. chaque point de passage hors carte → `ACCESS_DENIED` ; un obstacle entre deux points → comme 3.
- `onRegionUpdate` (`:927-957`) : **ne prend aucune position du client** ; la position reste celle du serveur.
- `StructCreature::onDead` (`StructCreature.cpp:5728-5737`) : une créature qui meurt en marchant est arrêtée là où
  elle est (`SetMove(pos, pos, 0)`, diffusé).

Le contrôle de vitesse est donc **implicite** : le serveur fait avancer le joueur à sa propre vitesse ; un client
plus rapide se retrouve à plus de 525 unités de la position serveur et sa demande suivante est refusée.

## 2. Ce qui est porté

- `Game/Services/Movement/PlayerMoveRules` (pur) : `Judge` rend `Accept`, `Ignore`, `Refuse` ou `Correct` selon les
  règles 1-4 ci-dessus et la cible au-delà d'une carte (16 128) ; `Trusted` pour les positions hors marche.
- **Estimation serveur sur tout le chemin** : `ConnectionInfo.BeginWalk` garde les points de passage et les fins de
  chaque segment (`MonsterMovement.PathEndTicks`) ; `PositionAt` les suit segment par segment (`PositionAlong`) au
  lieu d'aller en ligne droite vers le dernier point. Un warp, une entrée en jeu ou toute écriture de
  `MoveStartTick`/destination invalide le chemin gardé (repli sur la ligne droite). `Rebase` déplace l'origine d'une
  marche en cours sans changer sa fin.
- `GameClient.HandleMoveRequest` : un joueur **mort** ne marche pas (fin du « déplacement continu après la mort ») ;
  le déplacement est jugé contre `SkillCastRangeRules.PlayerPosition` avant tout effet ; `Refuse`/`Correct` →
  `TS_SC_RESULT(TM_CS_MOVE_REQUEST, ACCESS_DENIED)` et, pour une position dans la carte, le joueur est ramené à la
  position serveur (`PlayerMoves.WalkBackTo`, à lui et à ses observateurs) ; un joueur assis se relève (500).
- `TM_CS_REGION_UPDATE` et 900 : la position du client n'est gardée qu'à moins de 120 unités
  (`CHANGE_LOCATION_ERROR_RANGE`) de l'estimation, sinon l'estimation ; la marche continue depuis là (`Rebase`).
- **Mort** : `ICombatService.HaltOnDeath` (les deux chemins de mort d'un joueur en combat, et `/die`) arrête l'attaque,
  l'incantation et la marche (`PlayerMoves.Stop`) : arrêt à la position estimée, `TS_SC_MOVE` d'arrêt au joueur et à
  ses observateurs. `/die` ne l'appelait pas (2026-10-06) : un `/die` en marchant laissait le corps marcher.
- Obstacles : `IWorldCollision.Map` (les `.nfa` du client), injecté dans `NetworkService`. Sans cartes extraites,
  la carte est vide et seuls les contrôles de distance jouent.

## 3. Écarts assumés

1. **Refus pour distance → retour à la position serveur.** L'officiel répond `ACCESS_DENIED` seul ; on ramène aussi
   le joueur, pour qu'un client désynchronisé (latence, effet non modélisé) ne reste pas bloqué à jamais.
2. **Région et 900 : 120 unités de tolérance.** L'officiel ignore la position du client ; notre estimation part de
   l'instant de réception, pas de celui du client, et ne connaît pas tous les effets de vitesse : garder une
   position honnête proche absorbe cette dérive.
3. Le contrôle s'applique au joueur ; les invocations (`MoveSummon`) et le familier gardent leurs propres règles,
   comme l'officiel qui ne teste les obstacles que pour un joueur.
4. **Un mort qui demande à marcher reçoit un arrêt** (`TS_SC_MOVE` sans point de passage, à lui seul ; ses observateurs
   n'ont jamais eu cette marche). L'officiel l'abandonne en silence (`GameMessage.cpp:372`). Mais le client part dès le
   clic, avant l'écho : un clic envoyé juste avant que le client apprenne sa mort (coup mortel et propriété `hp` encore
   en route) promenait le cadavre jusqu'à sa destination — le « déplacement continu après la mort » observé en jeu
   (2026-10-06). Le joueur reste mort ; seule l'animation de marche s'arrête.

## 4. NON ÉTABLI

- Ce que le client 7.3 fait d'un `ACCESS_DENIED` sur 8 (rien d'affiché attendu ; notre `TS_SC_MOVE` de retour le
  replace).
- Les déplacements imposés par des compétences (charge, recul) ne sont pas tous modélisés côté serveur : s'ils
  dépassent 525 unités, la demande suivante sera ramenée (écart 1).
- Les paquets 54, 57 et 59 restent lus et ignorés (`socle-anti-triche.md`).

## 5. Vérifications

`Tests/Game/PlayerMoveTests.cs` : règles (acceptation, distance, hors carte, NaN, obstacle, position serveur dans un
obstacle, cible lointaine, tolérance de 120), estimation sur un chemin en L et `Rebase`, joueur mort, téléport
refusé et ramené, mise à jour de région lointaine, arrêt à la mort chez le joueur et un observateur.

En jeu : marcher, courir, monter, porter une charge lourde — rien ne doit être refusé (surveiller le journal :
`move refused`). Mourir en marchant : le personnage s'arrête net, chez soi et chez un autre joueur.
