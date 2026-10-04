# Props du monde : usages, réapparition, durée de vie, butin, conditions, scripts

Lot du 2026-10-04. Sources :
- serveur officiel 2015 : `StructFieldProp.cpp` (`IsUsable`, `UseProp`, `Cast`/`CancelCast`), `FieldPropManager.cpp`
  (`onProcess`, `RegisterFieldProp`), `FieldPropLoader.cpp` (temps ×100), `StructSkill.cpp` (`EF_ACTIVATE_FIELD_PROP` :
  `IsCastable`, `GetCastingDelay`, `GetRemainCount`) ;
- données : `FieldPropResource` Epic 7 (`data/epic7/FieldPropResource.csv`).

## 1. Données

`tools/export_field_prop_rules.py` enrichit `DevConsole/field-props.73.json` depuis l'export Epic 7. Il faut le
relancer après `tools/Export-FieldProps`, qui lit le SQL Server 9.4.

| Champ | Contenu |
|---|---|
| `CastingTime` | `casting_time` ×100 ticks |
| `UseCount` | nombre d'usages, 0 = illimité |
| `RegenTime` | `regen_time` ×100 ticks |
| `LifeTime` | `life_time` ×100 ticks |
| `MinLevel` / `MaxLevel` / `LimitJobId` | `limit_min_level`, `limit_max_level`, `limit_job` |
| `Activations` | les **deux** conditions |
| `Drops` | les deux emplacements : `ratio` sur 100 000 000, quantités, niveaux |
| `LuaScript` | le script Epic 7 quand le script 9.4 n'a pas d'action |

Le catalogue garde l'action du script 9.4. Un script Epic 7 que `PropScript` sait lire (`enter_vulcanus()`) devient
l'action ; tout autre script est du Lua.

Chiffres des 454 modèles posés dans le monde : 80 à usage limité, 54 avec butin, 182 avec un script Lua.

## 2. Vie d'un prop (`FieldPropStates`)

- **Prop sans état** : sans `use_count`, `regen_time` ni `life_time`, un prop est toujours là, comme avant ce lot.
- **Prop suivi** :
  - **Première apparition** : il apparaît après `regen_time` comptée **depuis le démarrage**. `RegisterFieldProp`
    met chaque prop en attente de sa réapparition.
  - **Durée de vie** : s'il a une `life_time`, il disparaît strictement après elle, puis revient au bout de
    `regen_time`.
- **Usage** (`UseProp`) :
  - `use_count` 0 : un usage éternel (`m_nUseCount = 1` jamais décrémenté) ;
  - sinon : un usage de moins ; au dernier, le prop quitte le monde jusqu'à sa réapparition.
- **Visibilité** : `FieldPropService.Sync` ne diffuse que les props présents, et le flux envoie le `LEAVE` des autres.
  Le tick d'une seconde (`FieldPropManager::onProcess`) et chaque prop épuisé resynchronisent les joueurs qui voient
  l'endroit (`IFieldPropService.Refresh`).

## 3. Incantation (`SkillCastService` + `FieldPropUse`)

- **Durée** : celle du prop (`GetCastingDelay`), pas celle de la compétence d'activation.
- **Un incanteur à la fois** (`m_bIsCasting`) :
  - un deuxième joueur reçoit `NotActable` ;
  - le prop se libère à l'annulation (`CancelCast`) et à l'usage ;
  - il se libère aussi après la fin prévue + 2 s, si l'incantation ne se termine jamais (garde du dépôt).
- **Refus avant l'incantation** : un prop absent, ou à usage limité sans usage restant (`GetRemainCount() < 1`), est
  refusé avant de commencer.

## 4. Conditions (`IsUsable`)

`FieldPropBase::CHECK_TYPE_*` :

| Type | Condition | Où c'est jugé |
|---|---|---|
| 1 objet | la **première pile** de l'objet (`FindItem(code)->GetCount()`) contient au moins la valeur 2 | avant le lancement, de façon asynchrone (`FieldPropUse.CheckConditionsAsync`) ; un refus répond `NotActable` sur le `ST_Casting` |
| 2 quête | `get_quest_progress(valeur 1) == valeur 2` | idem |
| 3 compétence | niveau de base **au moins** égal à la valeur (avant : exactement égal) | à la validation synchrone (`FieldPropUsage`) |
| 4 objet porté | valeur 2 non nulle : il doit être porté ; nulle : il ne doit pas l'être | de façon asynchrone |
| 11 / 12 monstre proche | un monstre vivant (de l'id valeur 1, ou n'importe lequel si 0) à `valeur 2 × 12` unités, sur la couche du joueur, présent pour 11, absent pour 12 | de façon asynchrone |
| 5-7 | non implémentés à l'officiel non plus | — |
| 13 position tactique de siège | reste au service des donjons et sièges | `DungeonService` |
| 14 | arènes (Epic 8.1) | hors 7.3 |

Sans `FieldPropUse` (tests isolés), les conditions 1, 2 et 4 refusent toujours, comme avant ce lot.

## 5. Butin et script (`UseProp`)

- **Butin** : chaque emplacement tire `XRandom(1, 100 000 000) <= ratio`, puis une quantité entre ses bornes. L'objet
  va au sac (`AddItemAsync`, 207), annoncé par `TS_SC_RESULT(204, Success, handle)` comme un ramassage.
  - Aucun butin de prop n'a de niveau supérieur à 1 ni de groupe : ni l'un ni l'autre n'est modélisé.
- **Script Lua** : il passe par le bac à sable de `NpcScriptService` (`RunPropScriptAsync`), sans dialogue. Un échec
  est silencieux, comme une erreur Lua sous `UseProp`.
  - **Export** : `tools/export_npc_runtime.py` embarque les 99 fonctions de props qui existent
    (`NPC_Dungeon.lua`, `NPC_QuestClient.lua`, `ETC_dungeon_prop.lua`, etc.). Aucune page de contact n'est ajoutée
    pour elles, donc aucune ne détourne un dialogue PNJ.
  - **API ajoutées au bac à sable** : `get_quest_status` (`IQuestService.GetQuestStatusAsync`) et `del_flag`.
  - **Fonctions absentes de toute source** : `supply_event_item` (879 caisses) et 5 autres. Elles ne donnent que
    leur butin, comme chez l'officiel, où l'appel échoue.

## NON ÉTABLI

- **API non branchées** : `add_npc` (PNJ temporaire), `respawn_guardian_object`, `change_tactical_position_owner`,
  `force_start_quest`, `show_huntaholic_lobby_window` et `dlg_special`. Les scripts qui les appellent échouent à cet
  appel.
- **Script avant l'objet** : `get_flag` d'un drapeau absent rend `""`, comme l'officiel. Un script qui le compare à
  un nombre échoue donc tant que rien n'a posé le drapeau.
- **Couches** : l'officiel pose une copie de chaque prop par couche de canal (`ChannelManager`). Ici l'état est par
  prop, toutes couches confondues.
