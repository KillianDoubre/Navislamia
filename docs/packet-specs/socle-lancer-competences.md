# Socle — lancer des compétences : portée, réussite des malus, empilement des états, interruption

| | |
|---|---|
| Paquets | `TS_CS_SKILL` (400), `TS_SC_SKILL` (401 : `ST_Casting` 1, `ST_CastingUpdate` 2, `ST_Cancel` 3, `ST_Fire` 0, `ST_Complete` 5), `TS_SC_STATE` (505), `TM_CS_CANCEL_ACTION` (150) — **aucun identifiant nouveau** |
| Référence d'autorité | `CaptainHerlockServer.exe` 2012-11 : `StructCreature::CastSkill` (`0x140095080`), `StructSkill::GetCastRange` (`0x140089220`), `STATE_SKILL_FUNCTOR::onCreature` (`0x14020b0a0`), `StructSkill::GetProbabilityOnHit` (`0x140203ae0`), `StructSkill::GetHitBonus` (`0x140203b60`), `StructCreature::AddState` (`0x140093480`), `StructState::IsDuplicatedGroup` (`0x1400b0fe0`), `StructSkill::onDamage` (`0x140205ba0`), `StructSkill::Cancel` (`0x14020a4a0`) |
| Code | `Game/Services/Casting/` (`CastRules`, `StateStacking`, `PendingCast`, `CastInterrupts`), `SkillCastService`, `MonsterWorldState.TryAddState`, `MonsterSkillService`, `CombatService.DamagePlayer`, `WarpService.Warp`, `GameClient.HandleCancelAction` |
| Tests | `Tests/Game/CastMechanicsTests.cs`, `MonsterSkillTests.cs` |

## 1. Portée (`cast_range`)

- `CastSkill` ne mesure qu'un lancer sur **une autre** créature : distance des centres moins la moitié de la taille de
  chaque corps (`GetUnitSize`, `taille × 12 × échelle`, 12 pour un joueur), comparée à `12 × cast_range`
  (**en mètres**), multipliée par **1,2**, ou **1,5** si la cible marche (octet `+0x98`).
- `cast_range = -1` : la portée d'arme, `12 × attack_range / 100` (la `GetRealAttackRange` des coups).
- Hors de portée : résultat **2** (`TooFar`), rien n'est dépensé. Ici : `CastRules.InRange`, position du joueur
  estimée (`ConnectionInfo.PositionAt`), position interpolée du monstre.
- `GetCastRange` ajoute `niveau × cast_range` d'une seconde compétence liée (`+0xb8`) : sans équivalent ici.

## 2. Réussite d'un état nocif

`STATE_SKILL_FUNCTOR::onCreature`, pour une compétence `is_harmful` :

- effets **301, 302, 305 à 313** : chance = `précision magique − esquive magique de la cible + bonus de toucher + 50`
  (précision magique `+0x2e0`, esquive magique `+0x2f0` du bloc d'attributs ; bonus = `hit_bonus + écart de niveau ×
  percentage`, le même que les coups de compétence) ;
- autres effets : chance = `probability_on_hit + probability_inc_by_slv × niveau` ;
- l'état est posé si un tirage `0..99` ne dépasse pas la chance. Les sorts 6008-6010 suivent une table de niveaux,
  non portée.

Appliqué au malus d'un joueur sur un monstre (`SkillCastService.ApplyDebuff`) et à l'état nocif d'un monstre sur un
joueur (`MonsterSkillService`). Un échec ne pose rien ; le sort est quand même parti.

## 3. Lancer différé et interruption

- Le sort part maintenant **au bout de son temps d'incantation** (`delay_cast`) : `ST_Casting` au début, puis effet,
  `ST_Fire`, `ST_Complete` et la liste de compétences à l'échéance (`PendingCast`, boucle de 50 ms). Un sort sans
  délai reste immédiat. Un second lancer pendant une incantation est refusé (`NotActable`). PM et délai de recharge
  sont pris au début, comme avant.
- **Annulation** (`StructCreature::CancelSkill`) : `TM_CS_CANCEL_ACTION` (Échap), la mort, la téléportation
  (`WarpBegin`), la sortie (lobby, déconnexion) et l'arrivée d'un état « étourdissant ». `StructSkill::Cancel`
  refuse une compétence dont l'octet `+0xa` est nul : c'est la colonne **`is_passive`**, qui marque dans ces données
  976 des 977 sorts à incantation. Une annulation envoie `ST_Cancel` au lanceur et à ses observateurs ; les PM ne
  sont pas rendus (non établi).
- **États qui coupent l'incantation** (`AddState`) : les états 6005, 6006, 6008, 13601, 1000006, 1000007, tout état
  d'effet **104**, et d'effet **82** dont l'une des quatre premières valeurs est non nulle.
- **Coup reçu** (`StructSkill::onDamage`), seulement au-delà de **0,5 %** des PV max, selon `casting_type` :
  - 1 : le tir est **repoussé** de 20, 50 ou 100 ticks (`casting_level` 0, 1, 2), et le lanceur seul reçoit
    `ST_CastingUpdate` avec la nouvelle durée totale ;
  - 2 : chance de 20, 50 ou 100 % d'**annuler** ;
  - 0 : rien.
  Le facteur est celui de l'officiel, divisions entières comprises :
  `(1 + 0,4 × 10 × (dégâts / PV max)) × (100 / vitesse d'incantation)`.
- Une cible morte pendant l'incantation : `ST_Cancel` au tir.

## 4. Empilement des états (`AddState`)

Pour chaque état présent qui est **le même état** ou partage un des trois `duplicate_group` du nouveau :

1. le même état avec `reiteration_count ≥ 2` ajoute d'abord son niveau au nouveau, plafonné à ce compte ;
2. une **aura** en place refuse ;
3. si un seul des deux porte `NotErasable` (bit 16 de `state_time_type`), il l'emporte ;
4. sinon un niveau **supérieur** en place refuse, ou un niveau égal qui **dure plus longtemps** ; un état plus faible
   est retiré, et le même état est rafraîchi sur place (même `state_handle`).

Un refus est le résultat **9**. Une aura qu'on allume retire ce qu'elle percute. `StateStacking.Decide` est pur ;
le joueur (`SkillCastService.ApplyState`) et le monstre (`MonsterWorldState.TryAddState`) l'appliquent et envoient le
retrait (505 à zéro) des états déplacés.

## 5. `NON ÉTABLI` et écarts

- Le refus d'un état `NotActableToBoss` sur un boss (résultat 26) n'est pas porté : aucun drapeau de boss ici.
- Un état portant `EraseOnDead` refusé sur une créature morte : non porté.
- Les incantations des monstres restent instantanées ; les états qu'un monstre se pose à lui-même ne passent pas
  par l'empilement.
- **PostgreSQL arrêté pendant le travail** : `duplicate_group`, `reiteration_count`, `casting_type`, `casting_level`,
  `probability_*` et `cast_range` sont lus en base sans que l'import réel de ces colonnes ait pu être vérifié ici.
  Si l'une est vide, sa règle est inerte (aucun groupe, aucun repoussement, portée 0 refusée — à surveiller en jeu).
