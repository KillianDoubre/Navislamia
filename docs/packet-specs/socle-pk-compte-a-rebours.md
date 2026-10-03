# Mode PK — le compte à rebours officiel (800 / 801)

Complète `800-turn-on-pk-mode.md`, `801-turn-off-pk-mode.md` et `socle-mode-pk.md`. Source :
`GameMessage.cpp` (`onTurnOnPkMode`, `onTurnOffPkMode`), `StructPlayer::TurnOnPkMode` / `TurnOffPkMode` et les
échéances de `StructPlayer::onProcess`, `GameRule::PK_ON_TIME` / `PK_OFF_TIME`.

## Règle

- **800** : refusé (`NotActable`) hors terrain PK (`IPkFieldService.IsPkField`). Sinon :
  - une extinction en attente est annulée (succès) ;
  - un allumage déjà en attente est annulé (**refus**), et le statut repart pour que le client ne garde pas un nom
    blanc sur un personnage Bloody ;
  - sinon l'allumage est fixé à `maintenant + PK_ON_TIME` (1 000 ticks, **10 s**).
- **801** : un allumage en attente est annulé (succès) ; une extinction déjà en attente reste (**refus**) ;
  sinon l'extinction est fixée à `maintenant + PK_OFF_TIME` (3 000 ticks, **30 s**).
- Chaque demande reçoit son `TS_SC_RESULT` (800 ou 801).
- `PkModeService` applique les échéances toutes les 250 ms. Les 5 points d'immoralité (0 sur un serveur PK)
  sont payés quand le mode s'allume vraiment, puis la 500 part au joueur et à ses observateurs.

État de session : `ConnectionInfo.TurnOnPkAt` / `TurnOffPkAt` sous `PkModeLock` (0 = aucune échéance). La
commande `/pk` du MJ reste immédiate.
