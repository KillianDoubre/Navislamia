# Socle — haine des monstres

| | |
|---|---|
| Paquets | aucun : la cible choisie se voit par la poursuite et l'attaque (8, 101) |
| Référence d'autorité | `CaptainHerlockServer.exe` 2012-11 : `StructMonster::AddHate` (`0x1400b87d0`), `StructMonster::addHate` (`0x1400b6710`), `StructCreature::Attack` (`0x1400a18d0`, appel `0x1400a24bb`), `SkillBase::GetHatePoint` via `StructSkill::EnhancedValue` (`0x140202f70`), `StructCreature::AddHateToEnemyList` (`0x1400902d0`), `StructMonster::findNextEnemy` (`0x140162190`) |
| Code | `HateRules`, `MonsterWorldState.AddHate` / `AddHateFromHelp` / `DropTarget` / `GetHate`, `CombatService.ApplyDamage(…, hate)`, `SkillCastService`, `MonsterAiService.GoHome` |
| Tests | `Tests/Game/HateTests.cs` |

## 1. La liste

- Chaque monstre tient une liste « joueur → haine » (`_HATE_TAG`). `addHate` ajoute, plancher 0.
- **Cible** : un monstre sans cible prend celui qui vient de gagner de la haine ; un monstre qui a une cible n'en change
  que si un autre ennemi la dépasse **strictement** (comparaison avec la haine que la cible avait au moment où elle
  l'est devenue ou l'est restée, `+0x2414`). À égalité, la cible reste.
- **Cible perdue** (sortie de la vue, mort, téléportation) : elle est rayée et la plus haïe des restantes la remplace
  (`findNextEnemy`). Tirée au-delà de la portée de poursuite, le monstre abandonne tout et rentre. La mort du monstre et
  le départ du joueur effacent sa place dans les listes.

## 2. Ce qui rapporte de la haine

| Source | Haine |
|---|---|
| coup normal | les dégâts (`Attack` : dégâts + élémentaires, multipliés par des modificateurs qui valent 1 ici) |
| acquisition à vue, ralliement de groupe | 1 (`processFirstAttack`) |
| compétence offensive | `GetHatePoint(niveau, dégâts)` |
| malus (état nocif) | `GetHatePoint(niveau, 0)` |
| soin | `GetHatePoint(niveau, PV rendus)`, ajoutés chez **chaque monstre qui hait le soigné** (`AddHateToEnemyList`) |

`GetHatePoint` : `hate_mod = 0` → 0 ; `hate_mod < 0` → `hate_basic + hate_per_skl × niveau` ; `hate_mod > 0` →
`montant × hate_mod + hate_basic` (le terme d'amélioration vaut 0).

## 3. `NON ÉTABLI` et écarts

- L'état 4503 (`0x1197`) que `addHate` pose au monstre à la première haine n'est pas porté.
- Le partage de haine des « rôdeurs » (`StructRoamer::PendHateShare`) et l'appel à l'aide de `AddHate` (drapeau 3 de la
  ressource, monstres du même groupe à 0,8 × la portée de première attaque) ne sont pas portés.
- Les compétences de provocation (`TAUNT`, `REMOVE_HATE`) ne sont pas lançables ici.
- Le malus ajoute sa haine quand il est lancé, que l'état prenne ou non ; la place exacte de l'appel dans
  `STATE_SKILL_FUNCTOR::onCreature` n'a pas été relue.
