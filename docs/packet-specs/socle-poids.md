# Socle — poids porté et cycle jour/nuit

| | |
|---|---|
| Paquets | aucun nouveau : `TS_SC_GAME_TIME` (1101) porte enfin l'heure ; les refus passent par `TS_SC_RESULT` (`TooHeavy`, 11) ; la vitesse par l'écho `TS_SC_MOVE` (8) |
| Référence d'autorité | serveur officiel `CaptainHerlockServer.exe` (dump Epic 7 Part 4, build 2012-11, PDB) |
| Référence secondaire | NGemity (`Player::GetMoveSpeed`, `IsTakeable`, `CheckTradeWeight`, `onBuyItem`, `Unit::putoffItem`) |
| Code | `Game/Services/Weight/`, `WorldClock`, `/gametime` ; tests `WeightAndGameTimeTests`, `PlayerTradeTests`, `BuyItemPacketsTests` |

## 1. Cycle jour/nuit

`SendGameTime` (`0x14014e560`) écrit une trame de **19 octets** : `t` (`ar_time`) @7, puis `game_time`
@11 = **`_time64()`, l'heure Unix en secondes** (NGemity : `time(nullptr)`). Elle part à l'entrée en jeu
(`StructPlayer::SendCharacterInfo`) et en réponse à `TS_CS_GAME_TIME` (1100). Navislamia envoyait `0` :
le client n'avait aucune heure du monde. Le client ne garde que les 32 bits bas (`SFrame.exe 0x671258`) et
les confie à son horloge du monde (message interne `0x52`).

`WorldClock` fournit cette heure, décalable pour tous par la commande MJ `/gametime <heures>` (0 remet
l'heure réelle), qui renvoie aussitôt la 1101 à chaque joueur. Le décalage n'est pas persisté.

## 2. Poids

| Règle | Source |
|---|---|
| poids d'un objet = `weight` de la ressource × quantité | `Item::GetWeight` |
| **un objet porté ne pèse rien** | `Inventory::Push` (modificateur de poids) |
| poids max = `10 × (niveau + force)` + sacs (`CarryWeight`) + états (bit « Max. Wt. ») | `GameRule::GetMaxWeight` `0x140200ef0` — déjà dans `StatCalculator` |
| vitesse de marche : ×1 sous 75 %, **×0,5 à partir de 75 %**, **×0,1 à 100 % et plus** (ou ratio négatif) | `StructPlayer::GetMoveSpeed` `0x1400c3050` |
| ramassage refusé si porté + objet > max (`TooHeavy`) | `Player::IsTakeable` |
| achat refusé si max − porté < objet × quantité (`TooHeavy`, valeur = code de l'objet) | `onBuyItem` |
| sortie d'entrepôt vers le sac : même règle | `MoveStorageToInventory` → `IsTakeable` |
| échange : ce que chaque côté reçoit doit tenir (`TooHeavy` aux deux) | `CheckTradeWeight` |
| sac retiré seulement si non surchargé et si la charge tient sans sa capacité | `Unit::putoffItem` (`WEAR_BAG_SLOT`) |

## 3. Ce que fait Navislamia

- `ConnectionInfo.CarriedWeight` est posé à l'entrée en jeu depuis les objets déjà chargés, puis relu
  en base après **chaque opération d'inventaire** : `CharacterService` publie un changement
  (`IInventoryChangeFeed`) à la fin de toute opération qui touche des objets (`RunInventoryAsync`,
  `RunPairInventoryAsync`), l'entrepôt aussi, et `CarriedWeightService` relit le sac du joueur en ligne —
  une relecture à la fois par session, relancée si un changement arrive pendant.
- `ConnectionInfo.MoveSpeed` est la vitesse de la marche en cours : fixée à chaque `TM_CS_MOVE_REQUEST`
  depuis `EchoedMoveSpeed` (100) et la charge, puis utilisée pour l'écho, la copie envoyée aux pairs et
  l'estimation de position.
- Les cinq refus du §2 répondent `TooHeavy`.

## 4. Écarts assumés

- La vitesse d'une marche **déjà en cours** ne change pas quand la charge change : l'officiel la renvoie
  (`procMoveSpeedChangement`), ici la marche suivante la prend.
- Entre une opération et la relecture du poids, quelques millisecondes : deux ramassages collés peuvent
  passer tous deux.
- Étal (705/706) et retrait commercial (10005) ne jugent pas le poids : aucune référence ne le fait.

## 5. NON ÉTABLI

- Comment le client passe de l'heure Unix à l'heure du jour et à l'éclairage (le consommateur du message
  interne `0x52` n'a pas été lu) ; `/gametime` suppose que décaler l'heure décale le cycle.
- Si le client ralentit aussi son propre personnage de lui-même, ou seulement d'après l'écho.
