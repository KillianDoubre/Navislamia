# Monture par objet — `TOGGLE_STATE` et état `EF_RIDING`

Bug Noté du 2026-10-06 : « Monture consommé dans l'inventaire mais pas monté in game ».

## 1. Constat

- Les montures de location (marchés `normal_riding` et `official_riding` : 540017, 540021, 540022, 540048,
  550021…) sont des objets de type 6 (`Use`, réutilisables), groupe 120, `wear_type` 22, avec
  `opt_type_0 = 8` (`ITEM_EFFECT_INSTANT::TOGGLE_STATE`), un `state_id` (7001-7022) au niveau 10,
  `cool_time` 10 s, `cool_time_group` 17.
- `ItemUseService.ApplySlots` n'avait aucune branche pour l'effet 8 : l'objet n'était **pas consommé** (il est
  réutilisable, il était toujours en base), mais seul le délai de réutilisation (217) partait, ce qui grisait
  l'icône comme un objet consommé. Aucun état, aucun équipement : pas de monture.

## 2. Règle officielle (source 2015, `StructCreature.cpp:4601-4645`)

```text
TOGGLE_STATE:
  si l'état est actif ET l'objet porté en WEAR_RIDE_ITEM (22) est cet objet → RemoveState
  sinon :
    si (IsRiding || HasRidingState || !IsMountable(true)) et l'état est EF_RIDING → ACCESS_DENIED
    si un autre objet est en 22 : Putoff(22) puis Puton(22, objet), sinon Puton(22, objet) ; échec → ACCESS_DENIED
    AddState(code, owner, level, t, -1, true)       (sans fin)
```

- `applyState` sur `EF_RIDING` (200, `CalculateStat.cpp:3059-3069`) : `fRidingSpeed = value_0`,
  `m_bUseFasterSpeedInRiding = value_8`, `m_nRidingStateCode = code`.
- `onMoveRequest` (`GameMessage.cpp:453-455`) : un joueur monté marche à `GetRealRidingSpeed()` = vitesse ÷ 7 ;
  `CalculateStat.cpp:332-335` garde la vitesse de marche si elle est plus rapide et que `value_8` le dit.
- `IsAttackable`, `IsSkillCastable`, `IsMagicCastable` sont faux sous `HasRidingState` (`StructPlayer.h:50-60, 556`) ;
  `onCastSkill` répond `NOT_ACTABLE`.
- `IsMountable(true)` : ni donjon, ni donjon secret, ni donjon d'instance, ni arène, ni salle de prière.
- Descente (`UnMount`) : pour une monture d'objet, **seul l'état est retiré** (`StructPlayer.cpp:12041`), sans la
  chute 9001 ni la pénalité de PV de la monture-invocation. Elle survient à la mort (`onDead`), sur un coup
  (`rand() % 100 <= value_6`, ou `value_7` sur un critique, `StructCreature::onDamage`), à l'arrivée dans un lieu où
  l'on ne peut pas monter (`GameProc.cpp:638`, `ChangeLocation`) et quand l'objet quitte l'emplacement 22
  (`StructPlayer::putoffItem`, `StructPlayer.cpp:10012`).
- `MountSummon` refuse de monter une invocation sous `m_nRidingStateCode`.

Données réelles (état 7022) : `value_0 = 270` (octet 38), `value_6 = 20`, `value_7 = 50`, `value_8 = 0`,
`state_time_type = 3` (`ERASE_ON_DEAD | ERASE_ON_LOGOUT`).

## 3. Implémentation

- `IStateCatalog.TryGetRiding` lit `EffectType = 200` et `Values` dans la règle d'état déjà chargée
  (`RidingStateValues` : vitesse, chances de chute, garde de la vitesse).
- `ItemUseService.ToggleStateAsync`, **avant** l'accusé de réception (un refus ne consomme rien et n'arme aucun
  délai) : la règle ci-dessus, `IEquipmentService.EquipRideItemAsync` pour l'emplacement 22 (287, stats, 202 comme
  un 200, sans résultat propre), puis `ISkillCastService.ApplyPermanentState` (`end_time = -1` sur la 505).
- `ConnectionInfo.ItemRide` garde l'état de monte ; `Riding.ItemRiding.Current` l'oublie dès que l'état n'est plus
  dans `ActiveBuffs` : aucun chemin de retrait d'état (expiration, 408, déconnexion) n'a besoin de connaître la monte.
- Vitesse : `GameClient.HandleMoveRequest` (écho, pairs, estimation de position).
- Refus : `CombatService.IsAttackable` (102 `ACCESS_DENIED`) et l'arrêt d'une attaque en cours, `SkillCastService`
  (`NotActable`), `CreatureService.Mount`.
- Descente : `CombatService.DamagePlayer` (mort, coup), `PlayerLocationService.Change` (lieu interdit),
  `EquipmentService` (201 sur l'emplacement 22, objet remplacé en 22), `/unride`. Les services qui ne peuvent pas
  dépendre de `ISkillCastService` passent par `ICastInterrupts.RemoveState`.
- Tests : `Tests/Game/ItemRidingTests.cs`.

## 4. Écarts assumés

- Le critique n'est pas connu de `DamagePlayer` : seule la chance `value_6` est tirée.
- La descente sur un coup vaut pour tout dégât qui passe par `DamagePlayer`, alors que l'officiel la limite aux dégâts
  « normaux » (pas les états périodiques).
- `IsSkillCastable` sous une monture-**invocation** n'est toujours pas jugé (inchangé) ; seule la monture d'objet
  refuse les sorts.

## 5. NON ÉTABLI

- Comment le client 7.3 obtient la vitesse de monte pour sa propre marche : la 1000 (stats) ne la porte pas. Le
  serveur l'utilise pour l'écho et les pairs. **À vérifier en jeu** : si le personnage monté avance à sa vitesse de
  marche, il faudra trouver ce que le client lit.
- Que la 505 de l'état 7001-7022 suffise au client pour afficher la monture (l'officiel n'envoie rien d'autre que
  l'état et l'équipement). **À vérifier en jeu.**
