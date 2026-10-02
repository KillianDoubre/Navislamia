# Socle — entrepôt : capacité et or (212 modes 2/3)

| | |
|---|---|
| Paquets | `TM_CS_STORAGE` (212), réponses `TM_SC_GOLD_UPDATE` (1001), `TM_SC_PROPERTY` (507, `storage_gold`), `TS_SC_RESULT` (0) — **aucun identifiant nouveau** |
| Référence d'autorité | `CaptainHerlockServer.exe` 2012-11 : `onStorage` (`0x140125ef0`), `StructPlayer::ChangeGold` (`0x1400c0700`), `StructPlayer::ChangeStorageGold` (`0x1400dd200`) |
| Code | `StorageRules.Capacity`/`JudgeGold`, `GoldRules`, `StorageService.MoveGoldAsync`, `StorageRepository.GetStorageGoldAsync`/`SaveGoldAsync` |
| Données | Telecaster `AccountStorageGolds` (migration `Version0011_StorageGoldAndFavors`) |
| Tests | `Tests/Game/StorageServiceTests.cs` |

Complète `211-212-storage.md`, qui laissait la capacité et l'or ouverts (§7.2, §7.5).

## 1. Capacité

- L'officiel refuse un dépôt quand l'entrepôt contient déjà `game.max_storage_item_count` piles, **1 000** par
  défaut, avec le résultat **11**, sauf si l'objet rejoint une pile existante.
- Ici : `StorageRules.Capacity = 1000`, contrôlé dans `StorageRepository.MoveAsync` sur les lignes de
  l'entrepôt du compte ; refus `TS_SC_RESULT(212, 11)` (`ResultCode.TooHeavy`, la même valeur).
- **Écart** : le dépôt ne fusionne jamais une pile avec une pile existante (chaque dépôt prend une ligne), donc
  l'exception de l'officiel n'a pas de cas. Un entrepôt plein refuse tout dépôt.

## 2. Or (modes 2 et 3)

Ordre de l'officiel, porté par `StorageRules.JudgeGold` :

| mode | source insuffisante | destination au-delà du plafond |
|---|---|---|
| 2, sac → entrepôt | `NotEnoughMoney` (10) | entrepôt > 100 000 000 000 → `TooMuchMoney` (53) |
| 3, entrepôt → sac | `NotEnoughMoney` (10) | sac > 10 000 000 000 → `TooMuchMoney` (53) |

- **Les deux plafonds sont ceux de l'officiel** (`GoldRules`) : `ChangeGold` plafonne l'or porté à
  10 000 000 000, `ChangeStorageGold` l'or stocké à 100 000 000 000. Le plafond de NGemity
  (`MAX_GOLD_FOR_INVENTORY`, 100 000 000 000) servait aussi à l'or porté (étal, échange) : `BoothTradeRules.MaxGold`
  vaut désormais le plafond officiel.
- Succès : 1001 (or porté) puis la propriété `storage_gold`. Un compte nul ou négatif reste refusé en
  `NotEnoughMoney` avant tout (règle déjà en place).

## 3. Persistance

- L'or stocké est **par compte**, comme les objets de l'entrepôt : l'officiel l'écrit par
  `smp_update_storage_gold`, appelé par `ChangeStorageGold`. NGemity le cachait dans une ligne d'objet de code 0,
  qui n'est pas reproduite.
- Table `AccountStorageGolds` (`AccountId` unique, `Gold`). `SaveGoldAsync` écrit **dans la même sauvegarde** l'or
  porté du personnage et l'or stocké du compte : un arrêt ne peut garder qu'un côté du mouvement.
- L'or de session bouge sous `GoldLock` (`TryDebitGold`/`TryCreditGold`), puis la base ; un échec d'écriture rend
  l'or de session et répond `DBError`.
- L'ouverture (`open_storage()`) envoie l'or réellement stocké.

## 4. `NON ÉTABLI`

- Deux messages système de l'officiel autour de ces modes (chaînes `@575` et `@576`, envoyées en chat par
  `@SYSTEM`) ne sont pas reproduits : leur déclencheur exact n'a pas été relu.
- La fusion d'une pile déposée avec une pile existante (et donc l'exception de capacité) reste à faire.
