# Reste à faire — état au 2 octobre 2026

Liste de ce qui n'est pas fini sur `master`, par priorité. Le détail de chaque point est dans la fiche citée
(`docs/packet-specs/`) ou dans `CLAUDE.md` (*Current limitations*).

## 1. À vérifier en jeu (livré, jamais essayé avec le client)

- **Combat réel** (`socle-combat-reel.md`) : dégâts, esquive, blocage, critique, cadence, des deux côtés.
- **Compétences des monstres** (`socle-competences-monstres.md`) : dégâts, états, soin sur soi.
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

Cartes `jPLdNuge`, `XPItUChw` et `WF2LPAb1`, dans la file de la pipeline :

- états actifs d'un monstre envoyés quand il entre dans la vue d'un joueur ;
- exigences d'équipement (niveau, métier, race) jugées au `TS_CS_PUTON_ITEM` ;
- vraies récompenses de monstre (`Exp`, `Jp`, or, chaos de `MonsterResource`) à la place de `CombatRewards`.

## 3. Combat et monstres

- Compétences **de zone** et **multi-coups** (joueurs : 30011, 232, 261, 271 ; monstres : 111, 113, 261, 262,
  30013…) et déclencheurs de monstres (Lua).
- `cast_range` non appliqué ; résistance aux malus ; règles d'empilement `state_type` ; interruption de lancer.
- Mécaniques non modélisées : double attaque, deux armes, visée à l'arc, éléments, dégâts additionnels,
  renvoi, bouclier de mana.
- Haine (`AddHate`) : une seule cible par monstre, la première prise.
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
