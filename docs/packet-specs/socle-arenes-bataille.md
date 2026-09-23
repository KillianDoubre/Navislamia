# Socle — arènes de bataille (famille `TM_*_BATTLE_ARENA_*`)

Epic 7.3. Base : `master` `a1c4950`, 2026-09-23. La carte supposait que les voies `RT_Compete` (3)
et `RT_Deathmatch` (4) de `TM_CS_RESURRECTION` (513) dépendent des arènes de bataille.

## Conclusion : aucun sous-ensemble à livrer, la branche ne code rien

**Les arènes de bataille n'existent pas en Epic 7.3.** Tous les paquets de la famille sont
« Since EPIC_8_1 » dans rzu, par exemple :

| Paquet | Id | Source |
|---|---|---|
| `TS_CS_BATTLE_ARENA_JOIN_QUEUE` | 4701 | `TS_CS_BATTLE_ARENA_JOIN_QUEUE.h:8-10` (« Since EPIC_8_1 ») |
| `TS_SC_BATTLE_ARENA_JOIN_QUEUE` | 4702 | `TS_SC_BATTLE_ARENA_JOIN_QUEUE.h:10-12` |
| `TS_CS_BATTLE_ARENA_LEAVE` | 4704 | `TS_CS_BATTLE_ARENA_LEAVE.h:7-9` |
| `TS_SC_BATTLE_ARENA_RESULT` | 4716 | `TS_SC_BATTLE_ARENA_RESULT.h:37-39` |

Les **20** fichiers `TS_*_BATTLE_ARENA_*.h` de rzu portent tous la mention « Since EPIC_8_1 » (vérifié fichier par fichier). `EPIC_7_3 = 0x070300`
est sous `EPIC_8_1`. Le client 7.3 le confirme : `SFrame.exe` ne contient aucune chaîne de fenêtre
ou de paquet d'arène, seulement trois noms de codes de résultat partagés
(`RESULT_NOT_ACTABLE_IN_BATTLE_ARENA`, `RESULT_NOT_ENOUGH_ARENA_POINT`,
`RESULT_TARGET_IN_BATTLE_ARENA`) qui font partie de l'énumération commune. Le dépôt est déjà cohérent
avec ce gating : `GameInstanceGamePackets.cs:74` omet les 32 octets `battle_arena_*` (≥ 8.1) et
`GameTradePackets.cs:15` omet `arena_point` (≥ 8.1).

## Correction de prémisse

Les voies 3 et 4 de 513 ne sont **pas** des voies d'arène :

- `RT_Compete` (3) est la réapparition dans une **compétition entre joueurs** — le duel, famille
  4500-4506 (`socle-competition-joueurs.md`). NGemity le nomme `REVIVE_COMPETE` /
  `CRT_COMPETE` et laisse son traitement en `@todo CompeteDead`.
- `RT_Deathmatch` (4) est la réapparition dans une **instance de match à mort** — famille des
  instances de jeu 4250-4253, dont la 4253 porte déjà `deathmatch_kill_count` et
  `deathmatch_death_count` (`socle-instances-jeu.md`).

Aucune référence n'implémente l'une ou l'autre voie. Tant que ni duel ni instance de match à mort
n'existent, un joueur ne peut pas mourir dans ce contexte : le refus actuel (`TS_SC_RESULT(513,
NotActable)`, `ResurrectionRules.CheckRequest`) est la réponse exacte.

## Paquets restants pour rouvrir 513 (voies 3 et 4) et 4500/4502

| Voie | Prérequis | Où |
|---|---|---|
| 3 `RT_Compete` | duel effectif : lots C2-C4 de la compétition (4501, 4503-4506), registre de joueurs visibles | `socle-competition-joueurs.md` §5.5 |
| 4 `RT_Deathmatch` | instance de match à mort : lots S2-S6 des instances, message qui déclenche 4250 | `socle-instances-jeu.md` §5.4 |

La carte « arènes » peut être fermée : son sujet n'existe pas dans ce client.

## A VERIFIER PAR KILLIAN

1. Fermer la carte « arènes de bataille » et rattacher les voies 3/4 de 513 aux cartes duel et
   instances, plutôt qu'à ce socle.
