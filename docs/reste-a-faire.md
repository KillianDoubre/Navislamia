# Reste à faire — état au 2 octobre 2026

Liste de ce qui n'est pas fini sur `master`, par priorité. Le détail de chaque point est dans la fiche citée
(`docs/packet-specs/`) ou dans `CLAUDE.md` (*Current limitations*).

## 1. À vérifier en jeu (livré, jamais essayé avec le client)

- **Combat réel** (`socle-combat-reel.md`) : dégâts, esquive, blocage, critique, cadence, des deux côtés.
- **Récompenses des monstres** (`socle-recompenses-monstres.md`) : EXP/JP importés, or au sol et
  ramassage en groupe, chaos avec capacité et animation, sauvegarde au retour en jeu.
- **Progression** (`socle-progression-monstres-quetes-titres.md`) : dégâts contributifs, stamina,
  bonus de donjon configurable, raid, états à l'entrée en vue, 52 offres Lua et titres persistants.
- **Compétences des monstres** (`socle-competences-monstres.md`) : dégâts, états, soin sur soi.
- **Buffs et soins alliés** (`socle-buffs-groupe-invocations.md`) : reconnexion et retour au lobby,
  durée hors ligne, groupe, invocations présentes en monde, auras et soins HP/MP de zone.
- **Zones, multi-coups et déclencheurs Lua** (`socle-competences-zone-multi-coups.md`) : familles demandées,
  cadence, zone persistante 271, sélection des victimes, renforts Lua, marquage anti-bot,
  dégâts hors visibilité, portées (cible et arme), positions interpolées et diffusion à deux clients.
- **Mécaniques de combat** (`socle-mecaniques-combat.md`) : double attaque, deux armes, arc (visée, flèches),
  dégâts additionnels, renvoi, bouclier de mana ; portée réelle des armes et des monstres à distance.
- **Résistances élémentaires** (`socle-resistances-elementaires.md`) : objets, passifs et états,
  compétences des joueurs et monstres, zones, multi-coups, dégâts additionnels et renvoi.
- **PvP** (`socle-pvp-terrains-competences-immoralite.md`) : terrains et villes protégées,
  compétences entre joueurs, zones persistantes, immoralité, PKC/DKC, Nemesis et reconnexion.
- **Lancer** (`socle-lancer-competences.md`) : `TooFar`, annulation par Échap, recul sous les coups,
  empilement des états (refus 9), résistance des monstres aux malus.
- **Haine** (`socle-haine.md`) : changement de cible au soin et aux compétences, retour à la cible suivante.
- **Mort et résurrection** (`socle-mort-joueur.md`) : potions de résurrection, résurrection par un autre joueur
  (animation `HIT_REBIRTH`), expérience rendue.
- **Duel** (`socle-competition-joueurs.md` §10) : invitation, compte à rebours, fin, résurrection type 3,
  à deux clients.
- **Exigences d'équipement** (`socle-exigences-equipement.md`) : refus `NotActable` d'un objet de rang trop
  élevé ou interdit à la race, la classe ou la profondeur de métier, à 200 comme à 281.
- **Artisanat des cartes de compétence** (`socle-artisanat-cartes-competences.md`) : deux cartes de même
  compétence et amélioration, cube normal ou spécial, piles partagées, réussite et échec.
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
- **Migrations Telecaster** : `QuestLifecycle`, `Version0011_StorageGoldAndFavors` et
  `Version0012_CharacterStates`, `Version0013_CharacterTitles` n'ont pas encore tourné
  sur la base réelle (PostgreSQL arrêté pendant le travail) ; elles s'appliquent au démarrage du serveur.
- **Migration Arcadia** : `BackfillItemWearRestrictions` remplit les masques de race/classe et la
  profondeur pour les 29 647 objets Epic 7. Testée sur PostgreSQL temporaire ; elle attend le prochain
  démarrage sur la base réelle.

## 2. Cartes Trello confiées à la pipeline (lot « rapide et utile »)

Lot initial : cartes `jPLdNuge`, `XPItUChw` et `WF2LPAb1`. Les états actifs sont maintenant envoyés
à l'entrée en vue (`socle-progression-monstres-quetes-titres.md`).

Les récompenses de `MonsterResource` sont désormais branchées et testées localement
(`socle-recompenses-monstres.md`). Le statut externe des cartes n'a pas été modifié.

## 3. Combat et monstres

- Lua des monstres : 148 profils reconstruits depuis les ressources avec accord de l'utilisateur
  (105 alias officiels, 24 profils de compétences, 19 sans compétence configurée). Ces 19 profils
  nécessitent des données supplémentaires pour reconstruire une action. Raid et états à l'entrée
  en vue sont livrés (`socle-progression-monstres-quetes-titres.md`).
- Récompenses : bonus PC bang/premium restant ; pondération, stamina et bonus de donjon configurable
  livrés. Le partage égal à l'intérieur de chaque groupe est conservé.
- Équipement : le port sur invocation, la bascule 223 et la revalidation des objets déjà portés à la
  connexion restent à traiter (`socle-exigences-equipement.md`, lot 3). Les restrictions de race, classe
  et profondeur sont contrôlées sur les équipements 200 et 281.
- Titres : obtention, persistance, choix et effets du titre principal livrés. Restent les titres
  secondaires et les événements spécifiques d'invocation, d'artisanat, de siège et de PC bang.
- PvP : restent les règles d'arène/siège, les alliances de guildes, le compte à rebours de bascule PK
  et la conversion des dons de l'autel ; terrains PK, compétences et immoralité sont livrés et testés
  (`socle-pvp-terrains-competences-immoralite.md`).
- Mort du joueur : objets lâchés seulement sur un serveur PK, comme l'officiel ; à trancher si on le veut
  ailleurs.

## 4. Systèmes absents

- Changement de métier : livré (`socle-changement-metier.md`) ; restent le changement de race, la réinitialisation des
  compétences de classe maître et les compétences de talent.
- Invocations et apprivoisement (`socle-apprivoisement-invocation.md`) : rien n'émet 301/305, aucune carte
  n'est liée, les sorts 4001-4003 sont refusés : l'invocateur n'est pas jouable.
- Guildes : création (`show_guild_create`), alliance, taxe, donjon de guilde et siège (`warp_to_siege_dungeon`).
- Donjons d'instance et donjons secrets (20 dialogues `warp_to_instance_dungeon`, `question_secret_dungeon_*`) :
  `enter_dungeon` ne fait que téléporter.
- HuntaHolic (lobby 4001/4002, marché, points), hôtel des ventes (pages vides, aucune mécanique).
- Ferme de créatures : non implémentée par décision (2026-09-30).
- Dialogues PNJ non exécutés : timbres et quêtes d'événement (`question_stamp_*`, `valentine_*`, `event_*`),
  durabilité des objets (`max_item_durability`), `tp_skill`, cadeaux (`second_present_*`,
  `dormancyuser_item_*`), `random_item_change_menu`.
- Quêtes 701 : les 52 offres et le contrôle Lua des objectifs sont livrés. Les scénarios de donjon
  ou d'événement dépendent encore des systèmes correspondants. Faveur côté client : aucun paquet
  connu n'affiche la faveur.

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
