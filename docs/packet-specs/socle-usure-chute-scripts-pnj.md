# Usure éthérée, chute de monture, ligne « objet obtenu » et scripts PNJ

Lot du 2026-10-04, commencé par Codex puis repris, corrigé et testé. Source de vérité : le serveur officiel
(`A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer`) et son Lua (`Resource/Script`).

## 1. Usure éthérée (`Game/Services/EtherealWear.cs`)

- **Formule officielle** : `(GetEtherealDurabilityConsumeRate + GetEtherealDurabilityConsumeRateByItem) × base ×
  environnement` (`GameRule.cpp:282-345`, `StructItem.cpp:599`). Base 31 au coup normal ou main gauche, 51 en
  compétence ou en dégât continu, 98 aux coups reçus. Environnement : 5 % en duel, 200 % en PK ou pendant l'extinction
  du PK. Le métier n'est compté que hors métier de base (`job_depth`), avec FIGHTER=1 … SUMMONER=4.
- **Objets visés** (`StructPlayer::ProcEtherealDurabilityConsumption`, `StructSummon::…`) :
  - à l'attaque : la main droite, ou la gauche sur un coup de main gauche ;
  - en défense : les emplacements 1 à 11 qui ne sont pas des armes, plus les cartes de ceinture du groupe 140
    (`GROUP_EQUIPMENT_ON_BELT`), avec ou sans grade ;
  - pour une invocation : ses armes en attaque et le reste en défense, avec l'environnement de son maître.
  - Un objet sans grade ou sans maximum ne s'use pas.
- **Exécution** : chaque coup appelle `Hit`. Les candidats viennent de la session (`ConnectionInfo.EtherealGear`, calculés
  par `StatService.Seed`/`RefreshEquipment`) ou de la carte de l'invocation, jamais de la base. Les pertes sont cumulées
  par objet, avec **une seule écriture en vol par joueur** (`ConsumeEtherealAsync` sous le verrou d'inventaire), qui
  emporte tout le cumul. Un 207 part par objet usé.
  - **Écart** : le premier jet de Codex rechargeait le sac en base à chaque coup, donné ou reçu.
- **Épuisement** : l'objet ne donne plus de stats (`EtherealWearRules.Exhausted`). Stats, maxima et invocations
  concernées sont rafraîchis (`EquipmentStatRefresh`).
- **Après un artisanat** : une réparation (801-806) ou tout autre changement d'un objet porté rafraîchit aussi les stats,
  comme `CalculateStat` en fin de `MixManager`.
- **Création** : un nouvel objet naît à son maximum (`EtherealWearRules.Initialize`, `StructItem::AllocItem`).
- **Objets existants** : la migration marqueur `Version0021_EtherealDurabilityBackfill` déclenche une fois
  `EtherealDurabilityBackfill`, qui porte au maximum les 0 hérités. Depuis, 0 veut dire épuisé, y compris pour
  `MixResourceMatcher.CurrentEthereal`, qui lisait autrefois 0 comme plein.

## 2. Chute de monture, état 9001 (`Creatures/SummonFall.cs`)

- **Pose** : `FALL_FROM_SUMMON` (`StructMisc.h:49`) est posée par `UnMount(UNMOUNT_FALL)` au niveau 1, pour 300 ticks,
  avec la pénalité de PV (`StructPlayer.cpp:11677`).
- **Effet** (`CalculateStat.cpp:2915`, rangée avec l'étourdissement) : ni attaque, ni compétence, ni objet, ni
  déplacement. Les gardes sont dans `CombatService`, `SkillCastService`, `ItemUseService`, l'écho de déplacement de
  `GameClient`, le montage, et `CastRules.InterruptsCasting`.

## 3. Ligne « objet obtenu » (`ItemObtainedNotice.cs`)

- **Format** : `@254\v#@item_name@#\v@<nom>\v#@item_num@#\v<n>` si l'objet est empilable, `@253\v#@item_name@#\v@<nom>`
  sinon. Envoyée sur `CHAT_ITEM` par `@SYSTEM`.
- **Empilable** : `StructItem::IsJoinable`, c'est-à-dire le bit 6 d'`item_use_flag` sans `ITEM_FLAG_SUMMON`.
  - **Écart** : Codex se fondait sur `count > 1`.
- **Qui l'envoie, comme l'officiel** :
  - `MixManager` pour les objets créés : cartes de créature, niveau, recyclage, châsses. La création 601 nomme toujours
    la quantité.
  - Les récompenses de `HuntaholicManager`.
- **Qui ne l'envoie pas** :
  - les copies d'un échec et la carte 102 (`procEnhanceFail`, `EnhanceSkillCard`) ;
  - `insert_item` du Lua (`SCRIPT_InsertItem`), donc ni la boîte JP HuntaHolic ni les cadeaux PNJ.

## 4. Scripts PNJ (`NpcScriptService`, `NpcScriptCatalog`)

- **Fonctions embarquées** : le Lua officiel des timbres, événements, cadeaux, `max_item_durability`, `tp_skill`,
  `random_item_change_menu` et forgerons de niveau d'objet. `tools/export_npc_runtime.py` les extrait vers
  `Game/Scripting/Scripts/npc_dialogs.json`.
- **Exécution** : un bac à sable MoonSharp (`Preset_HardSandbox`) neuf par action, avec un budget de 100 000
  instructions.
  - Seul un déclencheur **annoncé par le serveur** y entre, une fois.
  - Tout est jugé sous le verrou du personnage, puis sauvé d'un bloc. Une erreur annule tout.
- **Retours de l'API** : les nombres de `ScriptItem.cpp`. `delete_item` rend 1 et `is_erasable_item` rend 0/1, car le
  Lua teste `== 1` / `== 0`. `set_item_level`/`set_item_enhance` rendent la nouvelle valeur.
  - **Écart** : un `delete_item` impossible lève une erreur au lieu de rendre 0, pour annuler l'échange entier.
- **`set_quest_status`** est appliqué après le relâchement du verrou (`QuestService` le reprend), et seulement si
  l'action a été sauvée. Exemple : l'échange de cartes de Hector, quête 3335.
- **Or porté** : plafond `GoldRules.MaxCarried` (10 milliards).
- **Options aléatoires d'objet** : colonnes `RandomOption*` (`Version0020_ItemRandomOptions`), lues comme des effets
  d'option par `ItemStatCatalog.RandomEffects`.

## 5. Transactions et reconnexion Npgsql

L'hôte active `EnableRetryOnFailure`, et `NpgsqlRetryingExecutionStrategy` refuse une transaction ouverte à la main.
Sur la vraie base, **les enchères (`AuctionStore`) et les guildes (`GuildService`) échouaient toutes**, et les tests en
mémoire ne le voyaient pas.

- **Correctif** : ces deux services prennent `TelecasterOptions.WithoutRetry(options)`. Leurs unités ont des effets hors
  base (or en session, lignes de chat) qu'une reprise rejouerait.
- **Vérification** : démarrage réel sur une copie de Telecaster, enchères automatiques enregistrées sans erreur.

## NON ÉTABLI

- La liaison d'une carte de compétence (`GetBindedPlayerSID`), qui compte dans `IsJoinable`, n'est pas modélisée.
- Le métier d'une invocation dans la formule d'usure : on prend 0, faute de `JobInfo` d'invocation dans les données.
- Les dégâts additionnels et réfléchis sont exclus chez l'officiel (`ADDITIONAL_DAMAGE`, `IsProcessingReflectDamage`).
  Ici, tout coup qui passe par `ApplyDamage`/`DamagePlayer` use l'équipement.
