# Monstres, récompenses, quêtes Lua et titres

## Monstres

`MonsterSpawnService` envoie les états 505 immédiatement après ENTER, avec le handle du monstre
attribué au destinataire (`socle-etats-monstre-entree.md`, MR #80, dont l'implémentation a été retenue à la
fusion). Les temps sont les ticks serveur, comme pour tous les autres 505 : la première version de ce lot y
ajoutait `ClientClockOffset`, ce qu'aucun autre envoi ne fait. Les états échus sont omis ; une échéance nulle
est échue pour le tick d'expiration, il n'existe donc pas d'état de monstre permanent. Une nouvelle entrée en
vue rejoue les états encore actifs.

Les points et zones de population acceptent `Layer` et `IsDungeonRaidMonster`. Le mode raid transmet
`1` au septième argument des déclencheurs Lua, multiplie la vitesse par 1,5 et conserve cette couche
et ce mode sur les renforts. Les monstres ordinaires de raid ne donnent ni or ni objets ; les rangs
`MonsterType >= 13` les conservent. EXP, JP et chaos restent possibles.

Les 148 profils absents de la copie du script sont reconstruits avec l'accord de l'utilisateur :

- 105 profils réutilisent une branche officielle portant le même `monster_skill_link_id` ;
- 24 profils associent les déclencheurs aux compétences importées, en privilégiant les emplacements
  de probabilité nulle et en respectant la cible hostile ou personnelle ;
- 19 profils ne configurent aucune compétence. Ils sont reconnus et restent sans action : les
  ressources disponibles ne permettent pas de reconstruire une action exacte.

Il s'agit d'une reconstruction, pas d'une nouvelle copie officielle. Le générateur est
`tools/export_monster_trigger_profiles.py`. Un script Lua externe peut remplacer `trigger`.

## Récompenses

Le monde enregistre les dégâts réellement retirés aux PV, sans dégâts négatifs ou sur-dégâts. La mort
est revendiquée une seule fois. La contribution est répartie entre joueurs seuls et groupes :

- 50 % proportionnels aux dégâts ;
- 30 % pour le premier attaquant ;
- 10 % pour le groupe ayant fait le plus de dégâts ;
- 10 % pour le dernier attaquant.

Après une minute, les 30 % du premier attaquant rejoignent le groupe ayant fait le plus de dégâts.
Dans chaque groupe, le partage égal déjà choisi est conservé. Les règles de distance de 500, de
niveau, de capacité de chaos et d'immoralité continuent de s'appliquer. Le groupe ayant fait le
plus de dégâts réserve les objets et l'or au sol. Les arrondis à l'entier inférieur peuvent perdre
une unité par contribution. Le crédit de quête de chasse garde le fonctionnement précédent : le
groupe du joueur ayant terminé le monstre.

Le bonus de stamina ajoute par défaut 100 % d'EXP et de JP si la stamina couvre le coût officiel
`exp / ((int)(level*2.4 + level^1.46 + level²*0.1 + 2) * 0.00055)`.
Le niveau est borné de 1 à 300. La consommation est bornée à la stamina disponible. Un état 4003
actif donne le bonus et évite la consommation. La propriété `stamina` est envoyée et cette valeur
est sauvegardée au lobby, à la déconnexion et avec `/save`.

Options `GameRules` : `StaminaBonusRate` vaut 1 par défaut ; `DungeonRewardBonusRate` vaut 0.
Ce dernier ajoute une proportion configurable d'EXP/JP dans les 81 cellules de donjon importées
(par exemple 0,5 ajoute 50 %). Aucune proportion universelle de donjon n'est inventée : les valeurs
de base restent celles des monstres.

**Bonus PC bang** (`StructPlayer::getPCBangBonus`, livré le 2026-10-03) : le mode PC bang de la connexion — ou
`GameRules:DefaultPcBangMode`, le serveur d'authentification du dépôt n'en envoyant aucun — ajoute
`fAllyPCBangBonusRate` (0,1) ou `fPremiumPCBangBonusRate` (1,2) à l'EXP et aux JP d'un monstre, avant le bonus
d'endurance dont le coût reste celui du gain nu. `procDropChaos` ajoute le bonus de chaos (0,1) et le détaille
dans les champs bonus de la 213. Tous les taux sont des options `GameRules`.

## Les 52 quêtes 701

Les 52 quêtes de contrôle externe sont désormais éligibles aux offres PNJ, avec leurs liens,
prérequis et textes importés. Leur progression comprend six compteurs indépendants, sauvegardés
et comparés aux objectifs des colonnes `value2`, `value4`, …, `value12`.

Les scripts serveur utilisent `get_quest_progress(code)` et
`set_quest_status(code, index, valeur)`, avec un index de 1 à 6. Un quatrième argument peut nommer
le handle du joueur courant. Les quêtes non acceptées, expirées, déjà terminables, les index invalides
et un autre joueur sont refusés. Les monstres tués et les objets ordinaires ne changent pas ces
compteurs externes. La remise utilise la transaction et les récompenses du cycle de quête existant.

`IQuestService.RunScriptAsync` fournit le contexte joueur aux scripts serveur. Les actions
`set_quest_status` et `set_title_condition` d'un menu PNJ sont exécutées seulement si ce menu serveur
les a effectivement annoncées. Les déclencheurs de monstres reçoivent aussi le contexte de leur cible.
Le contexte est restauré après l'appel, même en cas d'erreur Lua. Les scénarios particuliers de donjon
et d'événement nécessitent toujours leurs propres événements et scripts ; l'ouverture des 52 offres
ne crée pas ces systèmes absents.

## Titres

Le catalogue embarque les 258 titres, 292 types de condition et 601 conditions de l'export disponible.
Il respecte les groupes (ET à l'intérieur, OU entre groupes), l'ouverture, l'acquisition et les
périodes de validité. Les compteurs, titres ouverts/obtenus et titre principal sont persistants.

L'obtention fonctionne avec les morts de monstres, les quêtes acceptées/terminées, les compétences,
l'or, le mode PK, l'immoralité et la possession d'un autre titre. `set_title_condition(id, valeur)`
alimente une condition définie : remplacement pour `is_set`, cumul sinon. Les événements propres aux
sièges et au PC bang restent à raccorder (invocations et artisanat : voir
`socle-titres-secondaires-evenements.md`) ; une condition inconnue ne donne aucun titre.

`/titles` liste les identifiants obtenus ; `/title <id>` choisit un titre obtenu et disponible ;
`/title 0` le retire. Ces commandes sont accessibles à tous. Le premier titre acquis est sélectionné
automatiquement si aucun n'est actif. Ses options modifient les statistiques totales, sans apparaître
dans les statistiques des objets ; elles sont recalculées à la reconnexion. Aucun paquet de gestion
des titres propre à un client plus récent n'est inventé pour le client 7.3. Titres secondaires et
événements d'invocation et d'artisanat : `socle-titres-secondaires-evenements.md`.

Migration Telecaster : `Version0013_CharacterTitles`, exécutée au prochain démarrage du serveur.
Les tests PostgreSQL utilisent exclusivement des schémas temporaires, retirés ensuite.

## Sources locales et vérifications

Sources officielles : `StructMonster.cpp::calcPartyContribute`, `StructPlayer.cpp::getStaminaBonus`,
`GameRule.cpp::GetStaminaRatio`, `CalculateStat.cpp` (raid et titres), `StructTitleManager.cpp`,
`ScriptQuest.cpp::SCRIPT_SetQuestStatus`, `ScriptPlayer.cpp::SCRIPT_UpdateTitleCondition`.
Ressources : Epic 7 pour les compétences, monstres et quêtes ; export Arcadia disponible pour les
titres et les cellules de donjon. Générateur : `tools/export_progression_resources.py`.

Tests : `MonsterProgressionTests`, `MonsterRewardIntegrationTests`, `QuestLifecycleTests`,
`TitleProgressionTests`, `GmCommandRulesTests`, `tools/tests/test_monster_trigger_profiles.py`.
Résultats du 2 octobre 2026 : 3 115 tests .NET réussis, deux tests PostgreSQL explicites réussis
(migration/persistance des titres et transaction de récompense), dix tests Python réussis ; le test
Python PostgreSQL d'équipement reste explicite et n'a pas été rejoué dans ce lot. Compilation du
serveur réussie. La base temporaire a été arrêtée après les vérifications.
Vérification client encore nécessaire : icônes à l'entrée en vue, raid, concurrence de deux groupes,
stamina, offres Lua, obtention/changement de titre et reconnexion.
