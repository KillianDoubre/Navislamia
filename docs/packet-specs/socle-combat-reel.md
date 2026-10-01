# Socle — combat réel : dégâts, toucher, blocage, critique, cadence

| | |
|---|---|
| Paquets | `TM_SC_ATTACK_EVENT` (101) et le tir de `TM_SC_SKILL` (401) — **aucun identifiant nouveau** ; leur champ `flag` est désormais rempli |
| Référence d'autorité | serveur officiel `CaptainHerlockServer.exe` 2012-11 du dump Epic 7 Part 4, lu avec sa PDB (`DamageCalculator`, `StructCreature::calcAttribute`, `StructMonster`) |
| Référence secondaire | NGemity/Chihiro `Unit::CalcDamage`, `Unit::Attack`, `Unit::GetAttackInterval`, `Skill::PHYSICAL_SINGLE_DAMAGE`, `Skill::SINGLE_MAGICAL_DAMAGE` |
| Code | `Game/Services/CombatFormulas.cs`, `MonsterCombatStats.cs`, `Buffs/SkillDamageCurve.cs`, `CombatService.cs`, `MonsterAiService.cs`, `SkillCastService.cs` |
| Tests | `Tests/Game/CombatFormulasTests.cs`, `MonsterInstanceFactoryTests.cs` |

## 1. Réponses courtes

1. **Les stats décident du combat.** Le coup du joueur, le coup du monstre et les compétences offensives
   passent par une seule fonction pure, `CombatFormulas.Resolve`, qui reprend la règle officielle : toucher,
   blocage, critique, défense, écart de ±5 %. Les provisoires `PV max / 3` (vers le monstre) et
   `PV max / 15` (vers le joueur) sont supprimés.
2. **La cadence suit la vitesse d'attaque** : `100 / vitesse × 115` ticks de 10 ms (1,15 s à 100). Un buff de
   vitesse d'attaque change désormais le rythme des coups.
3. **Un monstre a de vraies stats** : la ligne `StatResource` de son `stat_id`, le niveau, les bonus dérivés
   de toute créature, puis ses colonnes `MonsterResource`. Son PV max n'est plus la colonne `hp` seule mais
   `hp + 20 × niveau + 33 × vitalité` (monstre 710 : 105 → **158**).
4. **Les malédictions ne sont plus inertes** : les états posés sur un monstre entrent dans ses stats à chaque
   coup (une baisse de défense baisse sa défense).

## 2. Formules (serveur officiel)

`DamageCalculator::CalculateDirectDamageWithDefence` (`0x14038ab20`), identique à la formule de NGemity :

```
dégâts = niveau_att × 1,7 × max(1 − 0,4 × déf / att, 0,3) + att × max(1 − 0,5 × déf / att, 0,05)
```

`att` est planché à 1 et le résultat à 1.

`DamageCalculator::SimulateDamageCalculation` (`0x14038ad40`) tire, dans cet ordre :

| Tirage | Règle |
|---|---|
| toucher | seulement si l'esquive de la cible > 0 : `p = (int)(7 + max(10, 88 + 2 × (niv_att − niv_cible)) × précision / esquive + bonus)` ; raté si `rand() % 100 > p` |
| blocage | `SimulateAndCheckBlocking` (`0x14038a8d0`), physique seulement : `rand() % 100 < taux de blocage`, puis `rand() % 100 < perfect_block` (20) → blocage parfait, 0 dégât ; sinon la défense de blocage s'ajoute à la défense |
| critique | `rand() % 100 <= (int)(critique × 1 + bonus)` ; les dégâts sont multipliés par `1 + puissance critique / 100` (80 de base) |
| écart | `1 + (rand() % 10001 − 5000) / 100000`, soit ±5 % |

La magie lit la précision et l'esquive magiques et la défense magique, et ne se bloque pas.

Le blocage n'est tenté que si la cible a un taux de blocage (voir §7 pour le bouclier).

## 3. Cadence

`Unit::GetAttackInterval` : `(uint)(100 / vitesse_attaque × 115)` ticks, la vitesse planchée à 10. Le
`TS_SC_ATTACK_EVENT` porte `attack_speed` = `attack_delay` = l'intervalle en millisecondes (NGemity
`broadcastAttackMessage(…, GetAttackInterval() × 10, …)`), et le coup suivant attend cet intervalle. Un monstre
lit la sienne de la même façon : `100 + 0,1 × agilité + attack_speed` (colonne souvent négative : −60 → 40,
un coup toutes les 2,9 s).

## 4. Stats d'un monstre

Ordre officiel : `StructMonster::GetBaseStat` (`0x1400b1660`, la ligne `StatResource` de `stat_id`) →
`StructCreature::calcAttribute` (`0x140046fc0`) → `StructMonster::onApplyAttributeAdjustment`
(`0x140048f00`). Le désassemblage de `calcAttribute` montre les coefficients de `StatCalculator`
(2,8 × force sur l'attaque, 1,6 × vitalité sur la défense, `3 + 0,2 × chance` de critique, 80 de puissance
critique) : **les monstres réutilisent donc le calcul des joueurs**, sans les objets ni les passifs.
L'ajustement ajoute `hp`, `mp`, `attack_point`, `magic_point`, `defence`, `magic_defence`, `attack_speed`,
`magic_speed`, `accuracy`, `avoid`, `magic_accuracy`, `magic_avoid`.

`MonsterCombatStats` est calculé **une fois par ressource** au chargement (une requête `StatResource` groupée)
et partagé par ses instances ; les états actifs du monstre sont appliqués **après** les colonnes, à chaque
coup. Un `stat_id` sans ligne laisse le monstre au niveau et aux colonnes (avertissement au démarrage).

## 5. Compétences offensives

| Effet | Dégâts de base (NGemity `Skill.cpp:1652-1726`) |
|---|---|
| 30001 physique | `attaque × (var0 + var1 × niv) + var2 + var3 × niv` |
| 231 magique | `magie × (var0 + var1 × niv) + var3 + var4 × niv` |

Bonus de toucher `hit_bonus + (niv_lanceur − niv_cible) × percentage`, bonus de critique
`critical_bonus + critical_bonus_per_skl × niv`. Les termes d'enchantement valent 0 (non modélisé). Le
résultat passe par `ICombatService.RollHit`, donc par la même règle qu'un coup.

## 6. Sur le fil

- `ATTACK_INFO.flag` (octet @8 de l'enregistrement, soit @30 de la trame) : 1 blocage parfait, 2 blocage,
  4 raté, 8 critique (`ATTACK_INFO__FLAG` de rzu).
- `HIT_DAMAGE_INFO.flag` du tir de compétence : `int32` @14 de l'enregistrement de 45 octets, mêmes valeurs
  (NGemity `Skill.cpp:117-126`).
- Un raté envoie 0 dégât ; le monstre prend quand même le joueur pour cible.

## 7. Écarts assumés

- **Plancher de 1** sur tout coup qui porte : l'officiel plancher la défense à 1, puis l'écart de −5 % peut
  tronquer 1 à 0 ; on garde 1.
- **Pas de double attaque**, d'arme double, d'arc (intervalle de visée), de dégâts additionnels, d'éléments,
  d'absorption, de renvoi ni de bouclier de mana : rien de cela n'est modélisé.
- **Un monstre frappe toujours de la main droite**, et l'attaque à l'arc du joueur garde la formule de force
  (`StatCalculator` n'a pas la branche arc `1,2 × agilité + 2,2 × dextérité`).
- **Le bouclier n'est pas vérifié** : NGemity ne tente le blocage que pour un porteur de bouclier (ou une
  invocation) ; ici, toute cible dont le taux de blocage est non nul peut bloquer.
- `/immortal` : le coup est tiré normalement (drapeaux compris), ses dégâts valent 0.

## 8. NON ÉTABLI

- Le rang exact où l'officiel applique les états d'un monstre par rapport à `onApplyAttributeAdjustment` :
  les appliquer après les colonnes est un choix (sinon une malédiction de défense ne toucherait pas la partie
  « colonne », qui est l'essentiel).
- Les récompenses (expérience, JP, or) restent les valeurs provisoires par niveau : les colonnes `exp`, `jp`,
  `gold_min/max` de `MonsterResource` sont en base mais le branchement est hors de ce lot.
