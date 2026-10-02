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
- **Mécaniques de combat** (`socle-mecaniques-combat.md`) : double attaque, deux armes, arc (visée, flèches),
  dégâts additionnels, renvoi, bouclier de mana ; portée réelle des armes et des monstres à distance.
- **Lancer** (`socle-lancer-competences.md`) : `TooFar`, annulation par Échap, recul sous les coups,
  empilement des états (refus 9), résistance des monstres aux malus.
- **Haine** (`socle-haine.md`) : changement de cible au soin et aux compétences, retour à la cible suivante.
- **Mort et résurrection** (`socle-mort-joueur.md`) : potions de résurrection, résurrection par un autre joueur
  (animation `HIT_REBIRTH`), expérience rendue.
- **Duel** (`socle-competition-joueurs.md` §10) : invitation, compte à rebours, fin, résurrection type 3,
  à deux clients.
- **Exigences d'équipement** (`socle-exigences-equipement.md`) : refus `NotActable` d'un objet de rang trop
  élevé, à 200 comme à 281.
- **Récompenses des monstres** : malus d'écart de niveau, portée de 500, trame 282 avant chaque objet et tas
  d'or, propriété `chaos` (`socle-recompenses-monstres.md` §15).
- **Changement de métier** (`socle-changement-metier.md`) : PNJ de métier, tutoriel 3019, classe maître 11555,
  textes `sconv`, fenêtre de compétences et JLv au-delà de 10.
- **Vitesse d'écho** (`socle-vitesse-echo.md`) : marche du joueur et du familier vue par les autres.
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
- exigences d'équipement : le **niveau** est jugé (MR #79) ; race, classe et profondeur de métier attendent
  l'import des colonnes `limit_*` (`socle-exigences-equipement.md`, lot 2).

Les récompenses de `MonsterResource` sont désormais branchées et testées localement
(`socle-recompenses-monstres.md`). Le statut externe des cartes n'a pas été modifié.

## 3. Combat et monstres

- Compléments Lua des monstres (`socle-competences-zone-multi-coups.md`) : branches absentes du script
  disponible (148 ressources) et mode raid. Renforts `respawn_near_monster` et marquage
  `set_auto_user` sont implémentés et testés.
- Éléments : les dégâts élémentaires partent sur le fil mais aucune résistance n'est modélisée.
- Récompenses : pondération selon les dégâts, bonus de donjon/PC bang et stamina restent à modéliser ; le
  partage égal du groupe est conservé (le malus d'écart de niveau et la portée de 500 sont faits, MR #78).
- États actifs d'un monstre non envoyés quand il entre dans la vue d'un joueur.
- Buffs : ni persistance à la déconnexion, ni buff sur les membres du groupe ou les invocations, ni soins de
  zone.
- Équipement : race, classe et profondeur de métier non jugées (colonnes `limit_*` non importées).
- Titres : ne donnent rien, rien ne permet d'en obtenir un.
- PvP : les terrains PK ne sont pas connus (`GameRules:PkFieldsEverywhere`, désactivé), seules les attaques
  normales visent un joueur (pas les compétences), l'immoralité n'existe pas.
- Mort du joueur : objets lâchés seulement sur un serveur PK, comme l'officiel ; à trancher si on le veut
  ailleurs.

## 4. Systèmes absents

- Changement de métier : livré (`socle-changement-metier.md`) ; restent le changement de race, la réinitialisation des
  compétences de classe maître et les compétences de talent.
- Invocations et apprivoisement : apprivoisement, formation, invocation, renvoi, marche et attaque de
  l'invocation livrés (`socle-apprivoisement-invocation.md` §15), **à vérifier en jeu**. Restent : l'invocation
  qui prend des dégâts, son expérience, sa mort, ses compétences (452), l'évolution (307), la monture (320/321),
  le miroir d'apprivoisement.
- Guildes : création (`show_guild_create`), alliance, taxe, donjon de guilde et siège (`warp_to_siege_dungeon`).
- Donjons d'instance et donjons secrets (20 dialogues `warp_to_instance_dungeon`, `question_secret_dungeon_*`) :
  `enter_dungeon` ne fait que téléporter.
- HuntaHolic (lobby 4001/4002, marché, points), hôtel des ventes (pages vides, aucune mécanique).
- Ferme de créatures : non implémentée par décision (2026-09-30).
- Dialogues PNJ non exécutés : timbres et quêtes d'événement (`question_stamp_*`, `valentine_*`, `event_*`),
  durabilité des objets (`max_item_durability`), `tp_skill`, cadeaux (`second_present_*`,
  `dormancyuser_item_*`), `random_item_change_menu`.
- Artisanat : type 102 non résolu (codes de condition 24/25 inconnus).
- 52 quêtes 701 (scripts Lua), systèmes de faveur côté client (aucun paquet connu n'affiche la faveur).

## 5. Données

- Une partie des ressources 9.4 n'est pas filtrée contre le client 7.3.
- Le lien état → icône du client n'est pas établi : un état 9.4 s'applique mais reste invisible.

## 6. Écarts connus, petits

- Entrepôt : un dépôt ne rejoint jamais une pile existante (l'exception de capacité de l'officiel n'a donc pas
  de cas) ; messages système `@575`/`@576` de l'officiel non envoyés.
- Quête refusée au plafond d'or : la 605 répond `TooMuchMoney`, code non relu chez l'officiel.
- `GetFirstAttackRange` (portée du ralliement de groupe) supposée égale à la portée de vue.
- Le plafond d'or porté est désormais l'officiel (10 000 000 000) partout ; l'ancien (100 000 000 000, NGemity)
  ne sert plus qu'à l'or stocké.
