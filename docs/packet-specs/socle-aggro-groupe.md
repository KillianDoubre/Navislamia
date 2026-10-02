# Socle — aggro de groupe des monstres

| | |
|---|---|
| Paquets | aucun nouveau : la poursuite et l'attaque passent par le chemin d'IA existant (8, 101) |
| Référence d'autorité | `CaptainHerlockServer.exe` 2012-11 : `StructMonster::processFirstAttack` (`0x140160c90`), `GetMonsterGroup` (`0x1400b1540`), `IsGroupFirstAttacker` (`0x1400b1550`), `GetFirstAttackRange` (`0x1400b1680`) |
| Code | `MonsterAiRules.JoinsGroupAttack`, `MonsterWorldState.RallyGroup`, `MonsterAiService.Acquire` |
| Tests | `Tests/Game/GroupAggroTests.cs` |

## 1. Règle officielle

- `processFirstAttack` est l'aggro **à vue**. Quand le monstre porte `f_group_first_attack`
  (`IsGroupFirstAttacker` : bit 1 du mot de drapeaux de sa ressource), il rassemble les monstres proches dont
  `GetMonsterGroup()` est le sien et qui sont dans `GetFirstAttackRange()`, puis appelle
  `AddHate(cible, 1, …)` sur lui-même **et** sur chacun d'eux.
- `GetMonsterGroup` lit le deuxième champ de la ressource : `monster_group` (pas `grp`).
- La riposte (un monstre frappé) ne passe pas par là : elle ne rallie personne.

## 2. Ce que fait Navislamia

- `MonsterInstance` porte `MonsterGroup` et `GroupFirstAttack` (`MonsterResourceRepository` les projette ;
  `tools/import_epic7.py` mappe `f_group_first_attack`). 3 350 des 6 897 monstres Epic 7 ont le drapeau, tous
  avec un groupe non nul.
- Quand un monstre acquiert un joueur à vue (`MonsterAiService.Acquire`), `RallyGroup` donne le même joueur
  aux monstres **que ce joueur voit**, vivants, sans cible, du même groupe non nul et à portée.
- La portée est la portée de vue de l'acquisition (`12 × visible_range`, `MonsterAiRules.ScaledVisibleRange`).

## 3. Écarts et `NON ÉTABLI`

- `GetFirstAttackRange` lit un entier à `+0x2c` de la ressource, dont la colonne n'a pas été identifiée : on
  suppose la portée de vue, celle de l'acquisition elle-même.
- Un monstre que le joueur ne voit pas n'est pas rallié : il ne pourrait ni le poursuivre ni le frapper ici
  (la poursuite exige le handle du client) et lâcherait la cible au tick suivant.
- La haine (`AddHate`) n'est pas modélisée : une cible par monstre, la première prise.
