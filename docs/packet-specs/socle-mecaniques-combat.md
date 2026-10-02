# Socle — mécaniques de combat : double attaque, deux armes, arc, dégâts additionnels, éléments, bouclier de mana, renvoi, portées

| | |
|---|---|
| Paquets | `TS_SC_ATTACK_EVENT` (101) : `attack_flag` @20, `count` @21, `ATTACK_INFO` de 61 octets chacun avec `elemental_damage` `int32[7]` @9 ; `TS_SC_PROPERTY` (`mp`) ; 254/255 (flèches) — **aucun identifiant nouveau** |
| Référence d'autorité | `CaptainHerlockServer.exe` 2012-11 : `StructCreature::Attack` (`0x1400a18d0`), `ProcessAdditionalDamage` (`0x140084e10`), `ReflectDamage` (`0x14009ce20`), `DealDamage` (`0x14009f4f0`) ; NGemity `Unit::processAttack`, `Unit::Attack`, `Unit::DealPhysicalNormalDamage`, `Unit::DealDamage`, `Unit::applyStateEffect`, `Unit::calcAttribute`, `Unit::applyItemEffect`, `ObjectMgr` (même table d'effets d'état `SEF_*`) ; rzu `TS_SC_ATTACK_EVENT.h` |
| Code | `Game/Services/Combat/AttackMechanics.cs`, `LeftHandItem`, `CombatService.ProcessSwing` / `DamagePlayer(…, attacker, magical)`, `GameAttackPackets.BuildAttackEvent(…, hits)`, `StatService` (mains, portée d'arme), `CombatRange` |
| Tests | `Tests/Game/CombatMechanicsTests.cs`, `CombatRangeTests.cs` |

## 1. Nombre de coups

- **Deux armes** : une arme portée dans l'emplacement du bouclier (une arme à une main ; pour un archer cet emplacement
  porte les flèches). Le coup compte **2 frappes**, les impaires de la **main gauche** (`bLeftHandAttack = i % 2`).
- **Double attaque** (`SEF_DOUBLE_ATTACK` 21) : taux `value_0 + niveau × value_1` sommé sur les états dont l'arme
  (`value_8..11`, 99 = toutes) correspond ; si `irand(1,100) < taux`, le nombre de frappes double : jusqu'à **4**.
- `attack_flag` : `DoubleAttack` 8, `DoubleWeapon` 4, `UsingBow` 1, `UsingCrossBow` 2, le dernier qui s'applique
  l'emporte (`broadcastAttackMessage`).
- **Main gauche** : tout ce que la droite a (stats, armure, passifs), son arme remplaçant la droite
  (`applyItemEffect` : une arme portée au bouclier ne compte que pour la gauche). Ici : `AttackPointRight − attaque de
  l'arme droite + attaque de l'arme gauche`, idem pour la précision (approximation : les amplifications en pourcentage
  d'arme ne sont pas séparées).

## 2. Arc et arbalète

- Le tir se fait en deux temps (`processAttack`) : **visée** (`AEAA_Aiming` 2, aucune frappe, `0,8 ×` l'intervalle),
  puis **tir** (`0,2 ×` l'intervalle) qui consomme **une flèche** de l'emplacement du bouclier (`EraseBullet` ; 255, ou
  254 à la dernière). **Sans flèche, l'attaque s'arrête.**
- La portée est celle de l'arme (§5) : un arc de 24 m tire de loin.

## 3. Dégâts additionnels et éléments

- `SEF_ADDITIONAL_DAMAGE_ON_ATTACK` (22, montant fixe) et `SEF_AMP_ADDITIONAL_DAMAGE_ON_ATTACK` (23, part de la
  frappe) : `value_11` 0 mêlée, 1 distance, 99 les deux ; chance `value_6 + niveau × value_7` ; montant
  `value_0 + niveau × value_1` ; élément `value_8`. Sur une frappe qui a touché (ni raté, ni blocage parfait), chaque
  ligne tire `irand(1,100) < chance` et ajoute son montant à la frappe **et** à `elemental_damage[élément]`.
- **Résistances élémentaires non portées** : aucune résistance de monstre n'est en base, aucune modification
  d'élément d'objet n'existe ici ; les dégâts élémentaires passent entiers.

## 4. Coups reçus par un joueur (monstre → joueur)

- **Bouclier de mana** (`SEF_MANA_SHIELD` 49) : part `value_0 + niveau × value_1` (`value_4` 1 physique, 2 magique,
  99 les deux), bornée à [0, 1] ; les PM absorbent `dégâts × part`, jamais plus que les PM restants (`Unit::DealDamage`).
- **Renvoi** : `SEF_DAMAGE_REFLECT_PERCENT` (43, part physique `value_0/1`, magique `value_2/3`) et
  `SEF_DAMAGE_REFLECT` (44, fixe `value_0 + niveau × value_1`) ; chance `value_6 + niveau × value_7`. Le montant frappe
  le monstre attaquant par le chemin ordinaire (haine, mort, butin).

## 5. Portées (correctif)

- L'officiel multiplie `attack_range` **par 100** au chargement (`ObjectMgr` : `attack_range = GetFloat() × 100`, arme
  comme monstre), puis la portée vaut `12 × attack_range / 100` : **12 unités par mètre** de colonne.
  `CombatRange.MeleeReach` oubliait ce facteur : une griffe de 0,6 m ajoutait 0,07 unité au lieu de 7,2, et les **416
  monstres à 8 m** frappaient au contact au lieu de 96 unités.
- Le joueur prend la portée de **son arme** (`applyItemEffect` : `nAttackRange = range × 100`, 50 à mains nues) et son
  coup est mesuré comme l'officiel (`processAttack`) : bord à bord, ×1,2 (×1,5 si le monstre marche). Avant, il héritait
  de la portée du monstre, ce qui forçait un archer au contact.

## 6. `NON ÉTABLI` et écarts

- La part d'intervalle d'arc tirée d'une compétence (`m_fBowInterval`) n'est pas portée (NGemity non plus) : 0.
- Les dégâts additionnels des compétences (24/25), le vol de PV/PM (41/42), les soins sur attaque (27), la réduction
  par race (46/47) et l'amplification des dégâts reçus (30) ne sont pas portés.
- Le renvoi ne tient pas compte de sa portée (`REFLECT_RANGE`) ni de l'option « ignorer la défense » ; il n'envoie pas
  de trame dédiée : la vie du monstre baisse et le prochain coup ou sa mort l'affiche.
- Le `TS_SC_ATTACK_EVENT` des monstres affiche les dégâts tirés, pas ceux restés après le bouclier de mana.
