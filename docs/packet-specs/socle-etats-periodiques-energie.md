# États périodiques (406), énergie (515), attaque impossible (102), commandes officielles (2026-10-04)

Lot de Codex, relu, complété et fusionné le 2026-10-05. Source : le serveur officiel 2015
(`A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer\Game`).

## 1. Trames

| Id | Nom | Taille | Champs | Source |
|---|---|---|---|---|
| 406 | `TS_SC_STATE_RESULT` | 36 | `caster_handle` @7, `target_handle` @11, `code` (état) i32 @15, `level` u16 @19, `result_type` u16 @21, `value` i32 @23, `target_value` i32 @27, `final` bool @31, `total_amount` i32 @32 | `Message/GameMessage.h:1282` |
| 515 | `TS_SC_ENERGY` | 13 | `handle` @7, `energy` i16 @11 | `GameMessage.h:996` |
| 102 | `TS_SC_CANT_ATTACK` | 19 | `attacker_handle` @7, `target_handle` @11, `reason` i32 @15 | `GameMessage.h:1380` |

`result_type` : 1 dégâts PV, 2 dégâts PM, 3 dégâts PS, 4 soin PV, 5 soin PM, 6 soin PS. Les trois ids sont serveur →
client ; ils ont un bras « journal + abandon » dans `GameClient`. Écriture : `GameStateResultPackets`.

## 2. États périodiques (`Game/Services/SkillCastService.Periodic.cs`, `Buffs/PeriodicState.cs`)

- Un état périodique est un état dont `StateResource.base_effect_id` n'est pas nul (`StructMisc.h:109`), indépendamment
  de son `effect_type`. Pris en charge : dégâts 1-4 et 6, soins 11, 12, 21, 22, 24, 25 (221 états en base).
- Intervalle `fire_interval × 100` ticks. Montant : `dégâts de base × (amplify + amplify_per_level × niveau) + add +
  add_per_level × niveau` ; l'effet 6 et 25 tirent des pourcentages des PV/PM max dans `value_*`.
- **Les dégâts de base sont figés à la pose** (`StatePulse.BaseDamage`) : attaque physique pour 1, 2 et 6, attaque
  magique sinon, du lanceur au moment de la pose (joueur, invocation ou monstre).
- Chaque coup passe par la défense (1 et 3), le critique du lanceur et la résistance élémentaire, puis par le chemin de
  dégâts ordinaire (`DamagePlayer`, `ApplyDamage`, `DamageSummon`) : mort, butin et récompenses restent au même endroit.
- `final` marque le dernier coup avant la fin de l'état ; `total_amount` cumule. La 406 part au joueur et à ses
  observateurs, ou à chaque joueur qui voit le monstre, avec **son** handle du monstre.
- Un état de dégâts dont le lanceur a disparu (monstre mort ou réapparu, joueur parti) est retiré.
- Persistance : `CharacterStates.PeriodicBaseDamage` et `RemainingFireTicks` (migration
  **`Version0024_PeriodicStateSnapshot`**), relus à la connexion.

## 3. Énergie (`Game/Services/EnergyCounter.cs`)

- **Compteur** : 10 au plus (`ENERGY_MAX`), capacité = niveau de `SKILL_INCREASE_ENERGY` (1082) ; chaque énergie dure
  `DEFAULT_ENERGY_UPKEEP_TIME` = 180 000 ticks (30 min) puis expire. 515 au joueur et à ses observateurs à chaque
  changement ; `energy` du `TS_SC_ENTER` du joueur porte le compte.
- **Coût** : `cost_energy + cost_energy_per_skl × niveau`, refusé `NotEnoughEnergy` avant le lancement ; un état d'effet
  3301 (`m_nEnergyUnconsumptionRate`) donne une chance de ne pas consommer, et de garder l'énergie à l'expiration.
- **Production** : `SKILL_GAIA_FORCE_SAVING` (2631, une énergie par niveau), les dégâts 125 (`var6 + var7 × niveau`) et
  30003 (`var4 + var5 × niveau`).
- **Passifs** (ajoutés à la relecture, `Combat/EnergyProcs.cs`) : `EF_ADD_ENERGY_ON_ATTACK` (32262, compétence 41119)
  et `EF_ADD_ENERGY_ON_BEING_ATTACKED` (32263, 41218), enregistrés par `CalculateStat.cpp:1074-1096` comme un
  `_ATTACK_TAG` porteur d'un `StructEnergyProc`. Sur chaque coup qui touche (`StructCreature::OnAttack`) : chance
  `var6 + var7 × niveau` %, énergie `var0 + var1 × niveau`, armes `var8..var11` (99 = toutes), PV `var14..var17`,
  masque de type d'attaque `var12` (1 coup normal, 2 compétence physique, 4 magique, 8 bénéfique, 16 nuisible ; 23
  dans les données) et élément `var18` (99 = tous). Branchés : coup normal du joueur (hors coup supplémentaire d'une
  double attaque), coup entre joueurs, coup ou compétence d'un monstre reçu, compétences offensives du joueur (cible
  unique et zones).

## 4. Attaque impossible (102)

`onAttackRequest` : poignée d'attaquant qui n'est ni le personnage ni une de ses invocations → `NotOwn` ; cible
inexistante → `NotExist` ; cible pas ennemie ou morte → `NotActable`. Une attaque déjà en cours est arrêtée au lieu
d'envoyer la 102, comme l'officiel. `target = 0` arrête l'attaque. Un mort n'attaque pas.

## 5. Commandes

Voir `docs/gm-commands.md`. `/passist`, `/change_name` (joueur) ; `block_chat`, `check_auto_user`, `force_warp`,
`invisible`, `kick`, `rebirth`, `lv` (MJ, `Permission >= 100`).

**`/change_name`, corrigé à la relecture** : les messages sont ceux de `StructPlayer::ChangeName` et
`DB_ChangeCharacterName` (`@NOTICE`, type 20) — `@128` invalide ou interdit, `@118` même nom, `@734` suivi du nom à la
bonne casse, `@130` déjà renommé, `@18` refus de la base, `@131` réussi — au lieu d'un texte maison. Et comme
`onChangeName` passe `bRemoveFromFriendAndDenial`, le personnage renommé **quitte les listes d'amis et de blocage des
autres** (`IFriendStore.RemoveFromOthersAsync`, `FriendService.OnRenamedAsync`).

## 6. Migration

Codex avait numéroté sa migration `Version0023`, comme celle des amis : elle a été régénérée en
`Version0024_PeriodicStateSnapshot` au-dessus de `Version0023_CharacterFriends`.

## NON ÉTABLI

- L'affichage exact de la 406 côté client (chiffres flottants, couleur) n'a pas été observé en jeu.
- Les états périodiques de dégâts PM/PS (2, 3) touchent les PV de la même façon dans ce lot.
- Les guildes, enchères et noms de propriétaire hors groupe ne suivent pas un `/change_name` avant reconnexion.
- `NOT_ENOUGH_BULLET` et `ACCESS_DENIED` de `onAttackRequest` (flèches, `IsAttackable`) ne partent pas en 102.

Tests : `StateEnergyTests`, `CastMechanicsTests.StateEnergy`, `EnergyProcsTests`, `GmCommandServiceTests.Official`,
`CharacterCommandsTests`, `OfficialCommandPathsTests`, `FriendServiceTests`.
