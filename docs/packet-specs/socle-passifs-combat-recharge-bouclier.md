# Socle — recharges au combat, bouclier et esquive, passifs d'invocation

Livré le 2026-10-05. Cartes Trello `oeIhWb7i` (10063-10065), `bYwqpP9l` (10009/10011) et `XkmbLRkb` (10032/10013).
Source : serveur officiel 2015 (`Game/Struct/CalculateStat.cpp`, `StructProc.cpp`, `StructCreature.cpp`,
`Logic/Damage.cpp`), données Epic 7 importées dans Arcadia. Aucun nouveau paquet : 403, stats et dégâts existants.

## 1. Recharges modifiées au combat (`EF_INC_SKILL_COOL_TIME_ON_*`, 10063-10070)

- **Inscription** (`CalculateStat.cpp:1096-1182`) : chaque compétence s'inscrit dans la liste `m_vProcBy*` de son
  évènement — 10063 attaque, 10064 coup reçu, 10065 meurtre, 10066 critique, 10067 critique reçu, 10068 esquive,
  10069 blocage, 10070 blocage parfait — avec une disposition de `var` propre : chance `var9 + var10 × niv`, armes
  `var11`/`var12` (99 = toutes), type d'attaque `var13`, bornes de PV `var14..17`, élément `var18` ; pour un meurtre
  (`_KILL_TAG`), écart de niveau `var18` et PM minimum `var19`. `CooldownProcs.Conditions` replace ces valeurs dans la
  disposition que lit `AttackProcConditions` (lot de Codex, `socle-passifs-etats-combat.md`), qui reste la seule
  implémentation des conditions.
- **Effet** (`StructCooldownProc`, `StructProc.cpp:154-165`) : toujours sur le **porteur** du passif ; `var0` non nul =
  toutes ses compétences de `inc = var1 + var2 × niv` secondes, puis `var3` de `var4 + var5 × niv` et `var6` de
  `var7 + var8 × niv`. L'entier est tronqué comme en C++ (-2,5 → -2).
- **`AddRemainCoolTime`** (`StructCreature.cpp:3102-3166`) : seule une compétence **encore en recharge** change, à
  `reste + inc × 100` ticks, prête si le résultat est négatif. Grâce (50401) n'est jamais touchée ; Maître du temps
  (31324) est épargnée par un changement global pendant qu'on la lance. Une compétence nommée doit être apprise. Les
  compétences changées repartent dans **un seul 403** (temps total et restant), sur le handle du joueur ou de
  l'invocation.
- **Code** : `Combat/CooldownProcs.cs` (catalogue, conditions, décalage), `SkillCastService.ApplyCooldownProc` (joueur
  sous `CastLock`, invocation sous `SummonLock`), branché dans `CombatService.RunStateProcs` après les états — donc
  sur les mêmes évènements (coups, critiques, esquives, blocages, meurtre). Un monstre ou un état ne porte pas ces
  passifs.
- **Données** : 10063 ×5, 10064 ×1, 10065 ×4 ; 10066-10070 aucune ligne mais gérés.
- **`EF_INC_SKILL_COOL_TIME_ON_SKILL_OF_ID` (32281), porté le 2026-10-05** : la même `StructCooldownProc`
  (`var0..8`), rangée par compétence déclenchante — `var11..13`, la liste s'arrêtant au premier 0
  (`CalculateStat.cpp:1184-1203`, `m_mapProcBySkillId`). Elle part quand l'une d'elles **touche** :
  `StructSkill.cpp:2828` appelle `OnAttack(..., GetSkillId())` pour chaque résultat non esquivé, puis
  `StructCreature::ProcBySkillId` (`StructCreature.cpp:3589`) juge le `_PROC_TAG` — chance `var9 + var10 × niveau`,
  PV du lanceur `var14..15`, PV de la cible `var16..17`, ni arme ni type ni élément. Côté dépôt :
  `CooldownProcs.ResolveForSkill`/`ListensTo`, `CombatService.RunSkillIdProcs` (joueur ou invocation, jamais un
  monstre), appelé par `NotifyHit(..., skillId)` après les procs d'attaque ; `NotifySkillHit` passe l'id du sort.
  Données : 41309 (3901 −1 s × niveau, 3 % × niveau, sur 41306) et 41331 (3751 −6 s × niveau, 100 %, sur 63441 ou
  63442). Tests : `StateProcsTests.A_skill_of_id_proc_fires_on_its_listed_skills_only`,
  `A_listed_skill_landing_shortens_the_named_skill_of_its_caster`.

## 2. Bouclier, esquive et attributs de base (10008, 10009, 10011)

- **Lecture officielle** (`CalculateStat.cpp:681-765`) : dix valeurs `var[i] × niveau`, **pas des paires**.
  - 10008 (plat) et 10011 (`m_AttributeAmplifier`, ratios) : P.Atk, P.Def, M.Atk, M.Def, Vit. att., Vit. dépl.,
    Précision, Préc. magique, Esquive, Esq. magique.
  - 10009 : régén. PV %, régén. PV, régén. PM %, régén. PM, chance de blocage, défense de blocage, critique,
    puissance critique, vitesse d'incantation, vitesse de recharge (soustraite).
- **Correction de 10008** : l'ancienne lecture par paires (`(base, par niveau)`) donnait le même résultat pour
  Defense Training et Mind Defense, mais faussait 19 lignes (40041 : +5 attaque × niveau lu comme +5 défense ;
  9002 : vitesse d'attaque ignorée).
- **Bouclier** (`applyStatByPassiveSkill`, `IsWearShield`, `StructCreature.cpp:3312-3316`) : un passif qui exige
  une arme et qui est utilisable avec un bouclier (`vf_shield_only`, `SkillWeaponFlag.Shield`) exige un **bouclier de
  classe 210 dans l'emplacement bouclier**, quelle que soit l'arme. `ItemStatCatalog.IsShield`,
  `ConnectionInfo.WearsShield` (posé par `StatService.SeedHands`, donc à l'entrée en jeu et à chaque équipement).
- **Données** : Shield Mastery 1211 (blocage +1 × niv, bouclier exigé), Avoidance Expert 1221 (esquive +2 % × niv ;
  ses valeurs étaient nulles dans l'import 9.4, l'import Epic 7 les a remplies), et les passifs d'invocations
  4xxxx de 10009/10011.
- **Écart** : l'officiel double l'attaque, la vitesse et la précision sur la main gauche en double arme ; ici seule la
  main droite reçoit le passif, comme les autres maîtrises.

## 3. Passifs d'invocation (10031/10032, 10013)

- **10031/10032** (`StructPlayer::applyPassiveSkillEffect`, `CalculateStat.cpp:1492-1570`) : compétences **du
  maître** (1831, 1832, 1851, 21528 en 10032) vers `m_ParameterForSummon` : PV, PM, PS max et régénération PV/PM,
  plat (10031) ou amplifié (10032). `SkillPassiveCatalog.ResolveForSummon`, appliqué par `CreatureService.StatsOf`
  (les ajouts avant l'amplification). L'apprentissage d'une compétence du maître rafraîchit ses invocations
  (`ICreatureService.RefreshSummonStats`, `setSummonUpdate`).
  - **Écart** : l'officiel ajoute à la régén. PM de l'invocation la valeur de **régén. PV** (`CalculateStat.cpp:578`,
    copier-coller évident) ; ici la régén. PM vient de ses propres valeurs. La régén. PS (paire 6) n'a pas de stat.
- **10013 `EF_HUNTING_TRAINING`** (`CalculateStat.cpp:828-856`) : **la prémisse de la carte était fausse**, ce n'est
  pas un passif du maître sur l'invocation mais un passif **de l'invocation elle-même** (41001-41007, 41526) : trois
  groupes `(type, dégâts base, par niv, esquive base, par niv)` par type de créature (`CREATURE_TYPE` 0-9, 99 = tous).
  `ProvideAttackerInfo`/`ProvideTargetInfo` multiplient le **dégât brut, avant la défense** (`Damage.cpp:287-288`) par
  `1 + dégâts[type de la cible]` et `1 − esquive[type de l'attaquant]`. `CreatureExpertise`, appliquée au coup de
  l'invocation, à ses compétences offensives et aux coups qu'un monstre lui porte (`RollMonsterHitOn`). Le type d'un
  monstre est sa colonne `grp` (`MonsterInstance.CreatureGroup`), celui d'un joueur 9 (`CREATURE_HUMAN`).
  - Non porté : `m_nChangingGroup` (états qui changent le type d'une créature).

## 4. Vérifications

`StateProcsTests.Cooldown.cs` (disposition des `var` sur les lignes réelles 31323/21120/21110, décalage, coup magique
qui raccourcit tout sauf Grâce avec un seul 403, coup reçu sur deux compétences nommées, invocation),
`SkillPassiveShieldTests` (bouclier porté ou non, 10009, 10011, lecture officielle de 10008 sur 40041/9002/1003,
`IsShield`), `CreatureTests.SummonPassives.cs` (10031/10032 pour l'invocation, expertise pure, coup d'invocation
renforcé et PV max suivant l'apprentissage du maître).

En jeu : un sort magique offensif avec Ruler of Time/31323 appris → les recharges baissent ; Shield Mastery → le
blocage ne bouge qu'avec un bouclier ; Creature HP Expansion appris → les PV max de l'invocation montent.
