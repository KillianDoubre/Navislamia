# Titres : siège, meurtre PK et PC bang

Lot du 2026-10-04. Complète `socle-titres-secondaires-evenements.md` et vérifie les raccordements
de siège déjà présents dans `GuildService.Dungeons.cs`.

Sources locales du serveur officiel : `Game/Struct/StructTitleManager.cpp` et
`Game/Struct/StructPlayer.cpp`, sous `program/server/GameServer`.

| Catégorie | Déclencheur | Sélection des conditions |
|---|---|---|
| 5201 | début de participation au siège | donjon exact ou valeur 0 = tous ; camp attaquant/défenseur |
| 5202 | résultat du siège | mêmes critères ; succès/victoire attendue |
| 6002 | meurtre d'un joueur | `Values[0] <= immoralité de la victime avant pénalité de mort` |
| 9000 | mode PC bang authentifié | mode exact, `Values[1] == 0` et `Values[2] == 0` |

## Siège

La participation est persistée dans `GuildSiegeParticipants`. `StartCredited` et `EndCredited`
empêchent de regagner les événements en réentrant ou en répétant la clôture. Les conditions et ces
marqueurs sont sauvegardés avec le siège, dans la même transaction. Les participants déconnectés
reçoivent aussi le résultat ; `TitleService.RefreshAsync` attribue les titres à la reconnexion.
Pas de deuxième branche qui compterait les mêmes événements une nouvelle fois.

## Meurtre PK

`StructPlayer.cpp:10713` passe `GetImmoralPoint()` de la **victime** à
`UpdateTitleConditionByPlayerKill`; `StructTitleManager.cpp:516` compare le seuil et ajoute 1.
Le dépôt enregistre cet événement au coup réellement fatal de `LandPlayerDamage`, y compris pour
une invocation dont le propriétaire est le tueur. Le constat du coup fatal et la moralité capturée
sont faits sous le verrou de progression de la victime. Un coup supplémentaire sur le cadavre ne
compte pas ; un coup non fatal dont la victime meurt ensuite ne compte pas non plus.

Les duels ne donnent pas cet événement. Les vraies morts en guerre de guilde peuvent l'alimenter
sans ajouter la pénalité d'immoralité d'un meurtre ordinaire. La sélection fonctionne aussi lorsque
la victime est criminelle/PK et que la branche de pénalité du tueur ne s'exécute pas.

## PC bang

`ConnectionInfo.PcBangMode` vient de la réponse d'authentification, pas d'un flag Lua ou d'une
commande du joueur. `TitleService` évalue ce mode comme un état courant, à 0 ou 1. Une reconnexion
ne transforme pas le critère en compteur de connexions. Le rafraîchissement des titres est maintenant
attendu à l'entrée en monde, après les informations de guilde.

Les conditions ouvrent et attribuent les titres via le moteur existant, avec persistance et bonus
de statistiques. Le client 7.3 conserve `/titles`, `/title <id>` et `/subtitle <slot> <id>` : aucun
paquet de titre d'une version ultérieure n'est introduit.

## Tests

- `TitleProgressionTests` : seuil de victime, cumul, persistance, mode PC bang et absence de farming
  par rafraîchissement répété.
- `PvpTests` : coup fatal compté une fois, absence de crédit en duel, seuil capturé avant réduction
  de l'immoralité à la mort sur serveur PK.
- `GuildTests` : titres des participants déconnectés, clôture répétée, changement de propriétaire,
  défense et réentrée sans double crédit.
