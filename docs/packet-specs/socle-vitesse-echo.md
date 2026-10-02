# Socle — vitesse d'écho du joueur et du familier (`TM_SC_MOVE` 8)

| | |
|---|---|
| Paquets | `TM_SC_MOVE` (8), champ `speed` (`uint8`, offset 16) — **aucun identifiant nouveau** |
| Référence d'autorité | `CaptainHerlockServer.exe` 2012-11 : `onMoveRequest` (`0x140132f90`), `StructCreature::GetRealMoveSpeed` (`0x140080830`), `StructPlayer::GetMoveSpeed` (`0x1400c3050`), `StructPet::GetMoveSpeed` (`0x1400bf480`), `SendMoveMsg` (`0x14014db10`) |
| Code | `WeightRules.RealMoveSpeed`, `ICarriedWeightService.RealMoveSpeed`, `GameClient` (écho), `PetSummonDefaults.MoveSpeed` |
| Tests | `Tests/Game/WeightAndGameTimeTests.cs`, `PetBehaviorTests.cs` |

## 1. Règle officielle

- `onMoveRequest` appelle `SetPendingMove(points, GetRealMoveSpeed())`, et `SendMoveMsg` écrit cette vitesse
  (octet `+0x9a` du vecteur de déplacement) à l'offset 16 du `TM_SC_MOVE`.
- `GetRealMoveSpeed()` = `GetMoveSpeed() / 7` (division entière, constante `0x92492493`).
- `StructPlayer::GetMoveSpeed` = vitesse de déplacement des stats, ralentie par la charge (×0,5 dès 75 %, ×0,1 à
  100 % ou plus).
- `StructPet::GetMoveSpeed` renvoie **100** en dur : 14 sur le fil.

## 2. Ce qui était faux

L'écho du joueur partait à **100** et le familier à **120**, des vitesses de stats et non des vitesses de fil :
les observateurs voyaient un joueur courir ~6 fois trop vite, et l'estimation de position du serveur
(`ConnectionInfo.CurrentPosition`, `length × 30 / speed`) faisait arriver le joueur ~6 fois trop tôt. Les monstres
passaient déjà par `MonsterMovement.SpeedByte` (÷ 7).

## 3. Ce que fait Navislamia

- L'écho, la copie des pairs et l'estimation de position utilisent `RealMoveSpeed` : vitesse des stats
  (`StatService.Compute(info).Total.MoveSpeed`, bonus d'objets et d'états compris), charge appliquée, ÷ 7. Sans
  stats connues : 17 (`ConnectionInfo.EchoedMoveSpeed`, la vitesse par défaut de 120).
- Le familier marche à 14. Il suit la destination de son maître et le rejoint quand celui-ci s'arrête ; le
  rappel au-delà de 540 unités reste en place.

## 4. `NON ÉTABLI`

- `StructPlayer::GetMoveSpeed` divise par deux une copie de la vitesse quand l'octet `+0x2638` est posé, mais ne
  l'utilise que sous charge ; le sens de cet octet n'est pas établi et la règle n'est pas portée.
- La monture (`GetRidingMoveSpeed`) n'existe pas ici.
