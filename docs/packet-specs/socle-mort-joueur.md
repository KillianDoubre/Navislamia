# Socle — mort du joueur : expérience rendue, potions, résurrection par un autre joueur, serveur PK

| | |
|---|---|
| Paquets | `TM_CS_RESURRECTION` (513), `TM_CS_SKILL` (400), `TM_CS_USE_ITEM` (253), `TS_SC_SKILL` (401, coup `SHT_REBIRTH` 23), `TS_SC_EXP_UPDATE` (1003), 254/255, 205/209 — **aucun identifiant nouveau** |
| Référence d'autorité | `CaptainHerlockServer.exe` 2012-11 : `StructCreature::Resurrect` (`0x14009bde0`), `ResurrectByState` (`0x14008c920`), `StructPlayer::ResurrectByPotion` (`0x1400e5650`), `StructSkill::SKILL_RESURRECTION` (`0x1402312f0`), `SKILL_RESURRECTION_WITH_RECOVER` (`0x140231bb0`), `StructPlayer::procDecreaseEXPAndDropItem` (`0x1400e35f0`), `StructSkill::GetVar` (`0x1400446b0`) ; rzu `TS_SC_SKILL.h` (`HIT_REBIRTH`) |
| Code | `LevelingService.RestoreDeathExperience`, `ConnectionInfo.DeathExpLoss`, `ResurrectionService`, `ResurrectionItemCatalog.Potions`, `ResurrectionRules`, `SkillCastService` (`SkillCastKind.Resurrection`), `ItemUseService`, `Death/DeathDropService`, option `GameRules:PkServer` |
| Tests | `Tests/Game/DeathResurrectionTests.cs`, `ResurrectionPacketTests.cs`, `ItemUseTests.cs` |

## 1. L'expérience perdue est retenue, puis rendue en partie

- `procDecreaseEXPAndDropItem` écrit la perte dans `+0x1d0` ; `Resurrect(type, hp, mp, exp, …)` ajoute `exp`.
  Ici : `ConnectionInfo.DeathExpLoss`, posée par `ApplyDeathPenalty`, rendue une seule fois par
  `RestoreDeathExperience(ratio)` (plancher, puis remise à zéro) ; le niveau perdu revient si le total repasse le
  seuil (`ApplyExperience`).
- Parts rendues :
  - **état** (513 type 1) : `value_4 + value_5 × niveau de l'état` ;
  - **potion** (513 type 2) : `var2` de l'effet 114 ;
  - **compétence 504** : `var2 × niveau` ; **30501** : `var4 + var5 × niveau` (les termes d'amélioration, `var3` et
    `var11`, valent 0) ;
  - **ville** (513 type 0) : rien, la perte est définitive.

## 2. Potions (513 type 2)

`ResurrectByPotion` ne cherche que **quatre objets**, dans cet ordre : 2010454, 2902042, 910005, 910004, tous d'effet
**114** (`var1` = part des PV max, `var2` = part de l'expérience ; 910004 rend 30 % de l'expérience, les autres
100 %). Les quatre sont dans le `db_item.rdb` du client 7.3. PV = `var1 × PV max` (au moins 1), **PM inchangés**, une
unité consommée. **Correction** : le socle précédent consommait le Parchemin de résurrection 603002 — ce parchemin est
la compétence 6001 qu'on utilise sur **un autre** joueur mort (§3).

## 3. Résurrection par un autre joueur

- Effets 504 et 30501 avec `tf_avatar` (6013, réservé aux créatures, reste hors) : `SkillCastKind.Resurrection`. La
  cible est **un autre joueur, mort, en vue** (`SpawnedPlayers`) ; sinon `NotExist`, et `NotActable` s'il est vivant.
  La portée est celle de §1 de `socle-lancer-competences.md`.
- La cible revient **sur place**, sans accord de sa part (l'officiel ne demande rien) : PV/PM de `VitalsBySkill`, part
  d'expérience, propriétés `hp`/`mp` (509 aux observateurs). Le tir porte un coup **`SHT_REBIRTH`** (23) :
  `target_hp` @5, `nIncHP` @9, `nIncMP` @13, `nRecoveryEXP` @17, `target_mp` @21, tous `int32` en 7.3.
- Le **parchemin 603002** (effet `Skill`, compétence 6001 niveau 1) passe par `TM_CS_USE_ITEM` : la cible est jugée
  **avant** la consommation (`CheckItemSkillTarget`), puis la résurrection est appliquée comme la compétence.

## 4. Serveur PK (option `GameRules:PkServer`, désactivée par défaut)

- `GameRule::bIsPKServer` double la perte (`0,3 / (niveau − 1) + 0,001`).
- Il est le **seul** cas où la mort fait tomber des objets : deux jets indépendants de 3 %
  (`XFastRandom() % 999 + 1 < 30`), un pour un objet porté (emplacement de départ tiré dans 0..27, premier objet porté
  au-dessus), un pour un objet du sac (tiré au hasard). L'objet **920005** dans le sac protège des deux. Un objet porté
  est retiré (`UnequipAsync`) puis lâché, un objet du sac est lâché entier (chemin du 203). Sur un serveur normal la
  chance est nulle : rien ne tombe.

## 5. `NON ÉTABLI` et écarts

- Les PM ne sont pas rendus quand une incantation est interrompue par la mort ; rien ne le dit.
- Les états effacés par la mort et rendus par certaines résurrections (`RestoreRemovedStateByDead`) : la mort ne retire
  aucun état ici.
- Le bit 13 du drapeau d'objet (objet non lâchable) et le type d'objet 4 écarté du tirage du sac ne sont pas identifiés :
  seules les cartes d'invocation liées sont protégées (règle du 203).
- Une mort par un autre joueur (PvP) ne coûte rien hors serveur PK, et la règle PK de l'immoralité n'existe pas ici.
