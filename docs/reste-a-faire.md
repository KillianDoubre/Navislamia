# Reste à faire — état au 2 octobre 2026

Liste de ce qui n'est pas fini sur `master`, par priorité. Le détail de chaque point est dans la fiche citée
(`docs/packet-specs/`) ou dans `CLAUDE.md` (*Current limitations*).

## 1. À vérifier en jeu (livré, jamais essayé avec le client)

- **Combat réel** (`socle-combat-reel.md`) : dégâts, esquive, blocage, critique, cadence, des deux côtés.
- **Récompenses des monstres** (`socle-recompenses-monstres.md`) : EXP/JP importés, or au sol et
  ramassage en groupe, chaos avec capacité et animation, sauvegarde au retour en jeu.
- **Compétences des monstres** (`socle-competences-monstres.md`) : dégâts, états, soin sur soi.
- **Zones, multi-coups et déclencheurs Lua** (`socle-competences-zone-multi-coups.md`) : familles demandées,
  cadence, zone persistante 271, sélection des victimes, renforts Lua, marquage anti-bot,
  dégâts hors visibilité, portées (cible et arme), positions interpolées et diffusion à deux clients.
- **Diffusion** (`socle-diffusion-combat.md`, `socle-diffusion-compagnons.md`) : à deux clients, coups, mort,
  assis, PK, familier, invocations, icônes d'états.
- **Déplacement des monstres** (`socle-deplacement-monstres.md`) : vitesse réelle, contournement des obstacles
  (les cartes `.nfa` se chargent enfin : `Map:SkipLoading = false`).
- **Perte d'expérience à la mort** (`socle-perte-experience.md`), y compris la perte d'un niveau.
- **Quêtes** (`socle-cycle-quete.md` §11-12) : offre, acceptation, objectifs, remise, faveur, plafond d'or.
- **Entrepôt** (`socle-entrepot-or.md`) : dépôt et retrait d'or, entrepôt plein.
- **Aggro de groupe** (`socle-aggro-groupe.md`).
- **Migrations Telecaster** : `QuestLifecycle` et `Version0011_StorageGoldAndFavors` n'ont pas encore tourné
  sur la base réelle (PostgreSQL arrêté pendant le travail) ; elles s'appliquent au démarrage du serveur.

## 2. Cartes Trello confiées à la pipeline (lot « rapide et utile »)

Lot initial : cartes `jPLdNuge`, `XPItUChw` et `WF2LPAb1`. Restent à traiter :

- états actifs d'un monstre envoyés quand il entre dans la vue d'un joueur ;
- exigences d'équipement (niveau, métier, race) jugées au `TS_CS_PUTON_ITEM`.

Les récompenses de `MonsterResource` sont désormais branchées et testées localement
(`socle-recompenses-monstres.md`). Le statut externe des cartes n'a pas été modifié.

## 3. Combat et monstres

- Compléments Lua des monstres (`socle-competences-zone-multi-coups.md`) : branches absentes du script
  disponible (148 ressources) et mode raid. Renforts `respawn_near_monster` et marquage
  `set_auto_user` sont implémentés et testés.
- `cast_range` pour les familles autres que zone et multi-coups ; résistance aux malus ; règles d'empilement `state_type` ; interruption de lancer.
- Mécaniques non modélisées : double attaque, deux armes, visée à l'arc, éléments, dégâts additionnels,
  renvoi, bouclier de mana.
- Haine (`AddHate`) : une seule cible par monstre, la première prise.
- Récompenses : pondération selon les dégâts et pénalités d'écart de niveau, bonus de donjon/PC bang
  et stamina restent à modéliser ; le partage égal du groupe est conservé.
- Duel et PvP (4500-4506, mode PK sans effet sur le combat).
- Mort du joueur : objets lâchés à la mort, expérience rendue à la résurrection, résurrection par un autre
  joueur.
- Vitesse d'écho du joueur et du familier (100/120 sur le fil contre 17 pour les monstres) à recaler.

## 4. Systèmes absents

- Invocations et apprivoisement (`socle-apprivoisement-invocation.md`) : rien n'émet 301/305, aucune carte
  n'est liée.
- Guildes, donjons d'instance (`enter_dungeon` ne fait que téléporter), HuntaHolic, hôtel des ventes.
- Artisanat : type 102 non résolu (codes de condition 24/25 inconnus).
- 52 quêtes 701 (scripts Lua), systèmes de faveur côté client (aucun paquet connu n'affiche la faveur).

## 5. Écarts connus, petits

- Entrepôt : un dépôt ne rejoint jamais une pile existante (l'exception de capacité de l'officiel n'a donc pas
  de cas) ; messages système `@575`/`@576` de l'officiel non envoyés.
- Quête refusée au plafond d'or : la 605 répond `TooMuchMoney`, code non relu chez l'officiel.
- `GetFirstAttackRange` (portée du ralliement de groupe) supposée égale à la portée de vue.
- Le plafond d'or porté est désormais l'officiel (10 000 000 000) partout ; l'ancien (100 000 000 000, NGemity)
  ne sert plus qu'à l'or stocké.
