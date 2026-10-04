# HuntaHolic (Bear Road) — lobby, salles, chasse, points, marché

Suite de `socle-instances-jeu.md` (formats 4000-4012, 4250-4253) et des fiches 4000/4003/4004/4005/4008/4011.
Source de la logique : le serveur officiel (`HuntaholicManager.cpp/.h`, `HuntaholicLoader.cpp`, `GameMessage.cpp`
`onHuntaholic*`/`onInstanceGame*`/`onBuyItem`, `StructPlayer.cpp` `ProcessWarp`/`onLogout`/refill,
`DB_Login.cpp`, `StructSkill.cpp` `WARP_TO_HUNTAHOLIC_LOBBY`/`INSTANCE_GAME_EXIT`, `ScriptPlayer.cpp`) et le Lua
officiel `NPC_huntaholic.lua`, `ETC_huntaholicprop.lua`. NGemity ne traite rien de la famille.

## 1. Données

`tools/export_huntaholic_catalog.py` → `DevConsole/huntaholic-catalog.73.json` (export 9.4 : le dump Epic 7 n'a pas
de `Huntaholic*.rdu` ; le `db_huntaholicresource.rdb` du client 7.3 porte la même ligne de base : id 10000, 1 500 s,
objectif 3 000). Bear Road : lobby (83719, 2544) canal 300011, donjon (88935, 8622) canal 300000, 12 paliers
`[min, max)` (10 = 0-15, 11 = 15-20, 0 = 20-35 … 9 = 155-301), 312 entrées de réapparition, 30 props de soin.

- **Zones** : `HuntaholicLoader` convertit la boîte du canal en indices de région (`GetRegionX(right) + 1`), et
  `GetHuntaholicID` compare la région de la position, bornes incluses (`HuntaholicRules.InArea`, région = 180).
- **Couche du lobby** = id du palier du niveau (`GetProperLobbyLayer`, 0xFF = aucun → `NotEnoughLevel`).
  **Couche de la chasse** = numéro de la salle (1, 2…, le plus petit libre).
- `period` des réapparitions en secondes, × 100 en ticks (comme le chargeur).

## 2. Points, entrées, marché

- Session : `ConnectionInfo.HuntaholicPoint` / `HuntaholicEnterCount` sous `GoldLock`, posés à l'entrée en jeu,
  sauvés par `SaveProgressAsync` (paramètre `HuntaholicProgress`) avec `Characters.LogoutTime` (UTC). Plafond
  2 000 000 000. La quête crédite la session puis la ligne.
- **12 entrées par jour, rendues à 06:00 locale** : à la connexion si un 06:00 est passé depuis la déconnexion
  (moins d'une minute = une minute, `DB_Login.cpp:644-711`), en jeu par le tick (`StructPlayer.cpp:7077`).
- **Marché** (`onBuyItem`) : prix en points = `trunc(huntaholic_ratio × ItemResource.huntaholic_point)` (export
  `export_market_catalog.py`) ; refus `NotEnoughMoney` puis `NotEnoughHuntaholicPoint` (58) puis `TooHeavy` ; or et
  points débités ensemble (`TryDebitGoldAndHuntaholicPoint`), rendus si l'objet n'est pas ajouté ; propriété
  `huntaholicpoint` envoyée. Boutiques de `hunterholic_point_market` (Lua officiel, ordre du menu) : onlybear,
  beardeco, creturecard (la variante `_me` du Moyen-Orient n'existe pas dans la table et tombe), equipment,
  skillcard, soulstone, cube ; `NPC_huntaholic_koreagarlic_contact` → `bearload_eventshop`.
- **Boîte de JP** (`hunterholic_jpbox_sell(1/2/3)`) : 20 000 / 90 000 / 200 000 JP → 1100703 / 1100702 / 1100701,
  ligne `@91000272` (succès) ou `@91000273` (JP insuffisants).

## 3. Lobby et salles (4000-4008)

- 4000 → 4001 : salles du niveau du joueur non démarrées, 16 par page, `total_page` sur toutes les salles du
  niveau ; `AccessDenied` hors HuntaHolic, `InvalidArgument` page < 0 ou au-delà.
- 4003 (`TS_SC_RESULT(4003, code, 10000)`) : entrées < 1 → `NotEnoughBullet` ; hors lobby `AccessDenied` ; en groupe
  `NotActable` ; nom invalide `InvalidText` ; effectif ≠ 4/6/8 `InvalidArgument` ; 200 salles `LimitMax` ; aucun
  palier `NotActable`. Le groupe (type 3) s'appelle `nom<N_h>`, l'information 4002 part aux membres, puis
  `TS_TIMESYNC`.
- 4004 : `CoolTime` si le départ est lancé, `LimitMax` plein, `LimitTarget` mauvais niveau, `InvalidPassword`,
  `NotExist`, `AlreadyExist` déjà en groupe. Le mot de passe ne sort jamais d'un journal.
- 4005 : au lobby, quitter la salle (`CoolTime` une fois démarrée ; le chef passe la main ou dissout ; la dernière
  sortie supprime la salle) ; au donjon, abandonner la chasse (§4).
- 4008 (déclaré, aucun constructeur dans le client 7.3) : `AccessDenied` hors lobby, `NotActable` en groupe, sinon
  sortie et `Success`.
- 4011 : refus avec résultat seulement hors HuntaHolic (`AccessDenied`) ou sans entrée (`NotEnoughBullet`) ; les
  refus de `BeginHunting` (pas chef, déjà lancé, un membre hors lobby ou sans entrée) sont **muets**, comme
  l'officiel. Chaque membre paie une entrée, reçoit `TS_TIMESYNC` et 4012 ; départ 1 000 ticks plus tard.
- Commandes de groupe : `/pshare /pdestroy /pinvite /pkick /pleave /ppromote` ignorées sur un groupe HuntaHolic ;
  `/pcreate` et `/pjoin` dans la zone → `ERROR_IN_HUNTAHOLIC` ; inviter quelqu'un qui y est →
  `ERROR_TARGET_IN_HUNTAHOLIC`. `PINFO` porte le type 3.

## 4. Chasse

- Départ (`beginHunting`) : chaque entrée de réapparition fait apparaître `count` monstres à un point libre de sa
  boîte (10 essais), sur la couche de la salle (`MonsterWorldState.SpawnInstanceMonster`, entrée
  `is_wandering = 0` : ne se promène pas) ; les membres vont au donjon ±60 avec ligne de vue (300 essais), 4009
  (`begin_time` à l'horloge du client).
- Mort d'un monstre (`onMonsterDelete`, via `IHuntaholicEvents.MonsterKilled` depuis `CombatService`) : 1/5/10/150
  points selon `monster_type` 1-4 au plus gros contributeur et à la salle ; au maximum (6 000) le donjon est vidé et
  4010 part ; 4007 (kills personnels, score de la salle) à chaque membre ; réapparition après la période sauf au
  maximum.
- Fin (`onProcess`) : salle vide, temps écoulé (`début + 1500 × 100`) ou fin demandée → `endHunting` : monstres
  retirés, groupe dissous, chaque membre récompensé avec `FAILED_BY_DEATH` sous l'objectif (sans pénalité) ou
  `SUCCESS`, puis renvoyé au lobby de son palier. Annonce une minute avant : chat 160 `@HUNTAHOLIC`,
  `@1111\v#@min@#\v1`.
- `quitHunting` : succès → points `ceil(avantage × score de la salle)`, EXP/JP du palier, objet de réussite ; mort ou
  temps de jeu → objet d'échec ; sinon (abandon) une entrée de moins et l'état 313205 (`MOVE_SPEED_SLOWDOWN`)
  6 000 ticks. 4006 toujours, `point_rate` = 1.0 (l'officiel l'écrit en dur).
- Abandon par 4005 au donjon : résultat jugé (mort → échec, sous l'objectif → abandon, sinon succès).
- **Sortie par téléportation** (`ProcessWarp`, crochet `IHuntaholicEvents.BeforeWarp` dans `WarpService`) : salle du
  lobby quittée, ou chasse quittée **sans récompense mais avec la pénalité**, puis `TS_SC_RESULT(4005, Success)`.
- Déconnexion (`onLogout`) : au donjon abandon (mort → échec) avec récompense ou pénalité, au lobby salle quittée.
  La connexion suivante replace au lobby, sur la couche du palier (`PlaceAtLogin`).
- Résurrection (513) dans HuntaHolic : au donjon, chasse échouée par la mort ; dans tous les cas PV pleins et retour
  au lobby du palier.

## 5. Entrée et sortie

- 4250 type 0 : déjà dans HuntaHolic → `NotActableInHuntaholic`, type 1/2 (matchs à mort) → `InvalidArgument` ;
  sinon le groupe est quitté (seul : dissous ; chef : la main passe), puis le serveur **lance** le sort 64818
  (`SkillCastKind.InstanceGame`, effet 604, 8 s, interruptible comme tout sort) : 4250 répond `Success` si le sort a
  commencé, `AccessDenied` sinon. Au tir : PK → `PKLimit`, pas de palier → `NotEnoughLevel`, puis lobby sur la
  couche du palier, point de retour gardé. Un 400 du client sur 64818/64827 est refusé (`AccessDenied`).
- 4251 : sort 64827 (8 s), puis sortie.
- PNJ `go_to_huntaholic( 10000, 1.0 )` : 1 000 or (`@90996942` sinon), codes du Lua → pages `@90996945`…`@90996951`
  (1 invalide, 4 en groupe, 5 PK, 6 HuntaHolic inconnu, 7 pas de palier), téléportation directe.
- Prop `huntaholic_lobby_menu()` → `TS_SC_DIALOG` type 5 (`TYPE_HUNTAHOLIC_LOBBY`), titre `Huntaholic`, handle du prop.
- 4252 → 4253 : points de la session et rang parmi les joueurs en ligne (pas de `RankingManager` ici).

## 6. Écarts et NON ÉTABLI

- Les PV/PM d'entrée (`hhp`/`hmp`) ne vivent que dans la session : l'officiel les garde en drapeaux, donc aussi à
  travers une reconnexion (`DB_Login.cpp:2367`).

## 7. Sortie, états et objets (2026-10-04)

- **Sortie** : `GetPositionOnEnterInstanceGame` renvoie `hx`/`hy` seulement **hors** de HuntaHolic, sinon
  `GetLastTownPosition`. Quitter HuntaHolic (sort 64827, fin de chasse sans palier, connexion sans palier) ramène donc
  **au point de retour** `rx`/`ry` (`socle-point-de-retour.md`), couche 0, et non au point d'entrée.
- **Toute téléportation qui sort de HuntaHolic** (`ProcessWarp`, crochet `BeforeWarp`) commence, avant la salle, par
  `RemoveAllStateByQuittingHuntaholic` : chaque état dont `state_time_type` porte `ERASE_ON_QUIT_HUNTAHOLIC` (bit 8,
  256) est retiré (`ISkillCastService.RemoveStatesWithTimeFlag`). Ce sont les buffs exclusifs Bear Road
  (200001-200008, posés par les objets 3620000-3620007) et 41102527. Puis `RestoreStatesOnLeaveInstanceGame(true)`
  rend les PV/PM gardés à l'entrée (`StoreCurrentStatesOnEnterInstanceGame(true)` dans `PendWarpToHuntaholicLobby`),
  PV bornés par le maximum et au moins 1.
- **Objets** (`onUseItem`, après Secroute, siège et carte d'événement) : dans HuntaHolic, lobby ou donjon, un objet dont
  `item_use_flag` porte le bit 22 (`FLAG_CANT_USE_IN_HUNTAHOLIC`) répond `NotActableInHuntaholic` (56) ; hors de
  HuntaHolic, un objet portant le bit 23 (`FLAG_USABLE_IN_ONLY_HUNTAHOLIC`) répond `ActableOnlyInHuntaholic` (59).
  `ItemBase::Flag` est copié de `item_use_flag` (`ItemLoader.cpp:176`) et les membres d'`ItemUseFlag` sont des
  **indices de bit**. La colonne importée est bien ce bitset : les 12 objets au bit 23 sont les objets « exclusifs
  Bear Road » (buffs, potion de récupération du roi ours, bombes) et 2010472, 75 objets Epic 7 portent le bit 22.
- Le système anti-addiction (temps de jeu) n'existe pas : `FAILED_BY_GAMETIME_LIMIT` n'est jamais produit.
- `go_to_huntaholic` est la version 2012 (le Lua 2015 l'appelle encore ; son `warp_to_huntaholic_lobby` n'est pas
  dans le code 2015) : les codes 2 et 3 (horaire, joueur introuvable) ne sont jamais produits.
- Les lignes `@253/@254` d'objet obtenu ne sont pas envoyées (même raison que l'artisanat).
- Le classement 4253 est calculé sur les joueurs en ligne, pas sur un classement persistant.

## 8. Props de soin (2026-10-04)

Source : `HuntaholicManager::InstanceDungeon` (`beginHunting`, `onFieldPropDelete`, `procHealingPropRespawn`,
`clearHealingProps`), `StructSkill::REGION_HEAL_BY_FIELD_PROP` / `MAKE_AREA_EFFECT_PROP_BY_FIELD_PROP`,
`StructSkillProp::INIT/FIRE_AREA_EFFECT_HEAL_BY_FIELD_PROP` (sources 2015).

- **Données** : `export_huntaholic_catalog.py` écrit chaque `HuntaholicHealingpropResource` comme le
  `FIELD_PROP_RESPAWN_INFO` du loader (position, `offset_z`, rotation `around_*`, échelle, verrou de hauteur) et, par
  HuntaHolic, les `HealingPropTemplates` tirés de `FieldPropResource` (Epic 7) : 300101 → sort 64807, 300102 → sort
  64806, `use_count` 1, `regen_time` 600 s, niveaux 1-300. 30 props sur les 12 paliers.
- **Props par couche** : `IDynamicFieldProps` (`Game/Services/Props/DynamicFieldProps.cs`) garde les props créés à
  l'exécution sur une couche, avec des identifiants d'instance à partir de `1 << 40` (jamais ceux des props du monde).
  `FieldPropService.Sync` les diffuse en plus des props du monde, à ceux de leur couche seulement.
- **Cycle** : au départ de la chasse, chaque prop du palier est posé sur la couche de la salle **avant** que les membres
  n'entrent (leur téléportation les diffuse). Utilisé, il quitte le monde (LEAVE aux membres) et revient
  `regen_time × 100` ticks plus tard. Le score maximum et la fin de la chasse les effacent, réapparitions comprises.
- **Usage** : un double-clic lance le sort du prop sur son handle, comme un portail ; `SkillCastKind.PropHeal`
  (effets 9502/9503) saute la garde d'apprentissage, juge le niveau du prop et exige un prop de couche. Le premier qui
  l'utilise le prend (`TryUse`) ; un second lancement échoue (`ST_Cancel`).
  - **9502** (64807) : PV `var0 + var1 × niv` plus `(var2 + var3 × niv) × PV max`, soit **30 %**, à `var4` = 30 m
    (× 12) du prop, cibles `var5` = 2 (alliés : le lanceur et son groupe). Un `ST_Fire` porte un coup `SHT_ADD_HP` par
    joueur soigné.
  - **9503** (64806) : une zone de soin (acteur de sort au sol) de `var7 + var8 × niv` = 30 s, qui tire **tout de suite**
    puis toutes les `var5` = 3 s (11 tirs, `current_time > end` arrête), **5 %** des PV max (`var0`) et `var2` des PM max
    à 30 m, cibles `var6` = 2. Chaque tir qui soigne quelqu'un part en `REGION_FIRE` avec des coups `SHT_ADD_HP_MP_SP`.
    La zone s'arrête si son lanceur quitte le jeu.
- **Écarts** : les invocations ne sont pas soignées ; « ennemi » n'est jugé pour aucun joueur (ni PK ni duel), donc une
  cible `ONLY_ENEMY` ne soigne personne et `NOT_ENEMY` soigne tout joueur à portée. La portée de lancement
  (`casting_range` 2) n'est pas jugée, comme pour les portails.
