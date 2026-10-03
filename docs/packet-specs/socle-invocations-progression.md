# Invocations — progression, combat, compétences, monture et évolution

Suite de `socle-apprivoisement-invocation.md` (apprivoisement, formation, appel). Source : le serveur officiel
(`A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer\Game`, cp949) — `StructSummon.cpp`,
`StructPlayer.cpp`, `CalculateStat.cpp`, `GameProc.cpp`, `GameRule.h`, et le Lua `NPC_CreatureSetup.lua`.
NGemity ne porte presque rien de tout cela.

## 1. Statistiques (`SummonProgression.Stats`)

Le `CalculateStat` officiel d'une invocation, dans cet ordre :

1. caractéristiques de base du `stat_id` de `SummonResource` ;
2. amplificateur `CreatureEnhance.stat_amplify` de l'amélioration de la carte ;
3. `GameContent::GetSummonLevelBonus` : `bonus × niveau` pour une première forme,
   `bonus × (niveau + 1 − (forme − 1) × 50)` au-delà, chaque caractéristique tronquée
   (`CreatureLevelBonus`) ;
4. `GetSummonStatPenalty` : les sept caractéristiques d'une invocation plus haute que son maître diminuent ;
5. les attributs (`StructCreature::calcAttribute`, `GetMaxHP`…) lus au **niveau de combat**
   `m_fBattleLevel` = niveau du maître + `GetSummonLevelPenalty`, jamais au niveau de l'invocation, avec le
   coefficient de créature `0,7 + 0,03 × Creature Mastery (1811)` ;
6. la correction de précision de `StructSummon::calcAttribute`.

Viennent ensuite les objets portés (§6), les passifs sans arme et les états.

## 2. Expérience et niveaux

- `StructPlayer::distributeExpToSummons` : une invocation dehors, vivante, à `VISIBLE_RANGE` (525) de son maître
  au plus, reçoit **toute** l'expérience de chasse du maître. `applyLimitBySummonLevel` : tant qu'une
  invocation dépasse son maître, le gain du maître est plafonné à `GetPlayerEXPLimit` (`niveau^1,8 × 30 + 240`).
- `StructSummon::AddExp` : un gain est plafonné à `GetSummonEXPLimit` (`niveau² × 200`) et à l'expérience du
  dernier niveau de la forme (60 / 115 / 170).
- `onExpChange` : un JP par niveau jamais atteint (`m_nMaxReachedLevel`), **deux** au-delà du niveau
  d'évolution (50 / 100) pour une forme qui peut encore évoluer ; la montée remplit PV et PM et part en
  1002 + 1003 + 1000 + 509. Tout est sauvé dans la ligne `Summons` (`SaveSummonProgressAsync`).

## 3. Combat

- Un coup sur l'invocation met la haine sur elle (`StructMonster::onDamage`) : la liste de haine reste indexée
  par le maître, `MonsterWorldState` garde la part de l'invocation, et un monstre dont la haine est surtout
  celle de l'invocation la poursuit et la frappe (`RollMonsterHitOn`, portée `CombatRange.InterUnitReach`).
- Chaque coup envoie la 509 de l'invocation. À 0 PV (`StructSummon::onDead`) : elle s'arrête, les monstres
  l'oublient, une mort par un monstre au-dessus du niveau 5 coûte `GetDeadEXPPenalty`
  (`besoin(niveau) × (0,15 / (niveau − 1) + 0,0005)`), elle est rappelée après 6 000 ticks (60 s) et **reste
  morte** jusqu'à sa résurrection (§5). Une monture morte fait tomber son cavalier.
- Régénération toutes les 3 s, comme les joueurs.

## 4. Compétences

- À Epic 7, l'arbre d'une invocation est indexé par son id `SummonResource` (91 arbres, `skill-catalog.73.json`,
  avec les bornes `cenhance_min/max` de chaque règle sur l'amélioration de la carte).
- `StructSummon::IsLearnableSkill` : les formes quittées d'abord, au niveau qu'elles avaient atteint, puis la
  forme courante. Payé avec les JP de l'invocation ; `TM_CS_LEARN_SKILL` (402) sur le handle de l'invocation.
- Table `SummonSkills` (migration `Version0014_SummonSkills`). La 301 est suivie de la 403 de l'invocation ; le
  retournement d'une carte (452) y répond aussi. Les passifs de l'invocation entrent dans ses stats.

## 5. Monture, miroir, évolution, gardien des créatures

- **Monture** (320/321) : `/ride <handle>` — la commande du client 7.3 lui-même, commande joueur de la table
  officielle ; un second `/ride` ou `/unride` descend. L'invocation doit être dehors et connaître Creature
  Riding (11001), hors donjons et arènes. Vitesse = `riding_speed / 7 × modificateur` si elle dépasse celle du
  cavalier. Un coup sur le cavalier ou la monture le fait tomber 30 fois sur 100 (`UNMOUNT_FALL`, 5 % des PV max).
- **Miroir de carte d'apprivoisement** (`ProcTame`) : le premier de 9000111 / 960019 / 960021 trouvé se brise au
  tirage, protège la carte d'un échec et affiche `@243`.
- **Évolution** (`StructSummon::DoEvolution`, `NPC_CreatureSetup.lua`) : une invocation gardée dans sa carte au
  niveau 50 (première forme) ou 100 (deuxième) devient son `evolve_target` ; forme et niveau quittés sont gardés
  (`PreviousSummonIds`/`PreviousLevels`), 307 puis les stats.
- **Gardien des créatures** (`CreatureDialogService`) : les pages de soin, les récupérations payantes, la
  résurrection d'une invocation morte aux prix du Lua et le menu d'évolution, portés comme le changement de
  métier. Une invocation y est nommée par sa carte.

## 6. Équipement

Voir `socle-equipement-invocation.md`.

## 7. Écarts et NON ÉTABLI

- `/ride` bascule (le client n'a pas de `/unride`) ; vitesse de monture `max(propre, monture)`.
- L'état de chute 9001 n'est pas appliqué ; la chance de chute de 75 % sur un coup critique n'est pas
  modélisée (30 % seulement).
- Une invocation ne lance pas ses compétences actives ; la commande de maintien (hold) n'existe pas.
- L'amélioration d'une carte de créature (`MIX_ENHANCE_CREATURE_CARD`, 104/105) n'est pas portée.
