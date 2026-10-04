# 900 / 901 — `TM_CS_CHANGE_LOCATION` / `TM_SC_CHANGE_LOCATION`, et le cycle météo (2026-10-04)

Source : le serveur officiel 2015 (`A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer\Game`).

## 1. Trames

| Id | Sens | Taille | Champs | Source |
|---|---|---|---|---|
| 900 `TM_CS_CHANGE_LOCATION` | client → serveur | 15 | `x` float @7, `y` float @11 | `Message/GameMessage.h:3054` |
| 901 `TM_SC_CHANGE_LOCATION` | serveur → client | 15 | `prev_location_id` int32 @7, `cur_location_id` int32 @11 | `GameMessage.h:3062` |
| 902 `TM_SC_WEATHER_INFO` | serveur → client | 13 | `region_id` u32 @7, `weather_id` u16 @11 | fiche `902-weather-info.md` |

Ids 7.3 (`< EPIC_9_6_3`). Le client 7.3 aiguille la 901 (inventaire du client) et envoie la 900 (bras existant).

## 2. `StructPlayer::ChangeLocation` (`Struct/StructPlayer.cpp:11718`)

1. Sur une 900, la position du client est gardée si elle est à moins de `CHANGE_LOCATION_ERROR_RANGE`
   (10 × 12 = **120** unités, `Rule/GameRule.h:301`) de la position que le serveur estime, sinon l'estimation.
2. `location_id = GameContent::GetLocationId(x, y)` : le polygone de lieu de plus petite priorité qui contient le
   point (ici `MapService.GetLocationId`, polygones `.nfl`).
3. **901 envoyée à chaque appel**, lieu changé ou non : `{ancien, nouveau}`.
4. Si le lieu change et que le nouveau est connu :
   - `AddToLocation` : 902 avec la météo courante du nouveau lieu ;
   - en mode PK (ou PK en cours d'allumage) hors terrain PK : `TurnOffPkMode` — tout de suite dans un match à mort
     (11) ou une arène (15), dans 30 s ailleurs ; un allumage en cours est annulé ;
   - en duel hors champ (2) et champ de bataille (5) : fin du duel (`COMPETE_END_BY_ENTERING_SAFETY_ZONE`).

**Déclencheurs** : la 900 (`onChangeLocation`), l'entrée en jeu (`DB_Login.cpp:2426`), la fin d'un warp
(`GameProc.cpp:632`) et la sauvegarde périodique, toutes les 30 000 ticks (`StructPlayer.cpp:3989`).

## 3. Météo (`world/StructWorldLocation.cpp`, `world/LocationLoader.cpp`)

- `weather_change_time` est en **minutes** : le chargeur l'enregistre `× 6000` ticks. Données : 371 lieux à 60, un
  lieu à 0 (tiré à chaque passage, comme l'officiel).
- `onProcess` : pour chaque lieu dont `last_changed_time` est nul ou dépassé de `weather_change_time`, tirage
  `XRandom(0, 99)` parcouru sur `weather_ratio[tranche][météo]` ; la météo dont la part contient le tirage gagne, une
  tranche dont la somme est inférieure au tirage garde la météo. Un changement est envoyé (902) **aux seuls joueurs du
  lieu**.
- `GetCurrentTimeIdx` : cycle de 12 h de l'heure locale — 2 h 30 aube (0), 4 h jour (1), 8 h 30 soir (2), 10 h nuit (3).
  Ici l'heure est celle de `game_time` (`WorldClock`, `/gametime` compris), dans le fuseau du serveur comme
  `localtime_s`.

## 4. Mise en œuvre

- `Game/Services/PlayerLocationService.cs` : `ChangeByRequest` (900), `Refresh` (entrée en jeu, warp, contrôle
  périodique), `Tick` d'une seconde (météo et contrôle périodique) ; règles pures dans `PlayerLocationRules`.
- `ConnectionInfo.LocationId` / `LastLocationCheck` ; `WorldLocation.CurrentWeather` est désormais tiré (il valait 0
  en dur) et la 903 répond avec lui.
- `IPkModeService.LeavePkField` porte `TurnOffPkMode`.
- La 902 `{0, 0}` d'entrée en jeu (rzu) est remplacée par la 901 et la 902 du lieu réel.

## NON ÉTABLI

- Ce que le client fait de la 901 (affichage du nom du lieu, carte) : rien n'a été lu dans `SFrame.exe` au-delà de
  l'aiguillage.
- Que le client accepte une 901 sans changement (`{n, n}`) sans effet visible : c'est ce que fait l'officiel.
- Les autres effets d'un changement de lieu chez l'officiel ne sont pas portés : statut du propriétaire de donjon
  (`BroadcastStatusMessage`), et monture retirée en zone interdite après un warp (`IsMountable`).

Tests : `Tests/Game/PlayerLocationTests.cs`.
