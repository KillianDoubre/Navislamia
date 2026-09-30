# Paquet 280 — `TS_TRADE` (échange entre joueurs)

| | |
|---|---|
| Id | **280** (`1280` seulement à partir d'`EPIC_9_6_3` — ne pas le déclarer), `TM_TRADE` dans `GamePackets` |
| Sens | **les deux** : la même trame en entrée et en sortie (`SessionPacketOrigin::Any` chez rzu) |
| Taille | **97 octets**, fixe |
| Référence d'autorité | serveur officiel `CaptainHerlockServer.exe` (dump Epic 7 Part 4, build 2012-11, PDB assortie) : `onTrade` `0x140144840` et ses gestionnaires |
| Référence secondaire | NGemity `WorldSession::onTrade` et suivants (même machine d'états, deux défauts, §5) |
| Code | `PlayerTradePackets`, `Services/Trade/PlayerTradeService.cs`, `CharacterService.ExchangeItemsAsync` ; tests `PlayerTradeTests`, `ItemTransferTests` |

## 1. Disposition

| Offset | Champ | Type |
|---|---|---|
| 0 | en-tête | 7 |
| 7 | `target_player` | `uint32` |
| 11 | `mode` | `int8` |
| 12 | `item_info` (`TS_ITEM_TRADE_INFO`) | 85 octets : l'enregistrement d'inventaire de ce client |
| 16 | ↳ `code` | `int32` |
| 20 | ↳ `uid` | `int64` — l'objet est **nommé par son uid** (`StructPlayer::FindItem(uid)`) |
| 28 | ↳ `count` | `int64` — le nombre échangé, et **l'or** pour le mode 6 |

97 = 7 + 4 + 1 + 85 : c'est la longueur que l'officiel écrit (`0x61`, `onAddItem`) et c'est l'enregistrement
de 85 octets de l'inventaire (`appearance_code` compris, voir *Inventory*). Toute autre longueur est refusée.

## 2. Modes (`TRADE_MODE`, table de saut d'`onTrade`)

| Mode | Nom | Client → serveur | Serveur → client |
|---|---|---|---|
| 0 | `REQUEST` | demande | à la cible, `target_player` = demandeur |
| 1 | `ACCEPT` | acceptation | — |
| 2 | `BEGIN` | ignoré | aux deux, `target_player` = partenaire |
| 3 | `CANCEL` | annulation | à chacun, `target_player` = partenaire |
| 4 | `REJECT` | refus | au demandeur, `target_player` = refusant |
| 5 | `ADD_ITEM` | offre | aux deux, `target_player` = offrant, objet complet au **compte offert** |
| 6 | `ADD_GOLD` | or | aux deux, `count` = or |
| 7 | `FREEZE` | verrou | aux deux |
| 8 | `CONFIRM` | confirmation | aux deux |
| 9 | `PROCESS` | ignoré | aux deux, l'échange a eu lieu |
| 10 | `REMOVE_ITEM` | retrait | aux deux |
| 11 | `MODIFY_COUNT` | nouveau compte | aux deux (NGemity n'a pas ce mode) |

## 3. Règles (officiel)

- **Validité de la cible** (`0x1401422a0`, demande/acceptation/refus) : joueur dans le monde sinon
  `NotExist` (1) ; à plus de `g_nRegionSize` dans le plan sinon `TooFar` (2). Ici :
  `WorldVisibility.RegionSize` (180, la valeur annoncée au client ; 150 est le défaut du binaire) et même
  couche.
- **Demande** : l'un des deux déjà en échange → `AccessDenied` (6) portant la cible ; règle PK
  (`IsTradableWith`) → `PKLimit` (29) — non modélisée.
- **Acceptation** : l'un des deux en échange (ou en apprivoisement) → `AccessDenied` ; sinon les deux
  fenêtres s'ouvrent (`BEGIN`).
- **Offre** (5, 11) : rien si sa fenêtre est verrouillée ; compte négatif ou supérieur à la pile →
  `NotExist` (l'officiel bloque aussi le compte, « Add Trade Bug ») ; objet non échangeable → `NotActable` (5).
- **Or** (6) : négatif ou supérieur au solde → `NotExist`.
- **Confirmation** (8) : les deux fenêtres doivent être verrouillées, sinon annulation des deux ; à la
  seconde confirmation : poids → `TooHeavy` (11), objets → `AccessDenied`, solde au-delà du plafond →
  `TooMuchMoney` (53) portant le handle concerné, aux deux ; sinon `PROCESS` aux deux, puis fermeture.
- **Soi-même comme cible** : ferme l'échange ouvert.

## 4. Ce que fait Navislamia

`PlayerTradeService` tient les deux côtés d'un échange sous un verrou unique. L'exécution :

1. l'or sous le verrou d'or de chaque session (`TryDebitGold`, puis les crédits) ;
2. `CharacterService.ExchangeItemsAsync` : **les deux sens jugés avant tout déplacement**, puis appliqués
   avec les deux soldes **en une sauvegarde**, sous les deux verrous de personnage (`RunPairAsync`) ; pile
   entière = même ligne, partie de pile = nouvelle ligne aux mêmes attributs ;
3. tout échec rend l'or et répond `AccessDenied` aux deux ;
4. succès : `PROCESS`, `TS_SC_GOLD_UPDATE` (1001), 254/255 au donneur, 207 au receveur.

Un départ du monde (lobby, déconnexion) ferme l'échange ; le partenaire reçoit `CANCEL`.

## 5. Écarts assumés

- **Une acceptation doit répondre à une demande** : l'officiel accepte n'importe quel `ACCEPT` et ouvrirait
  une fenêtre chez quelqu'un qui n'a rien demandé.
- **Écho au compte offert** : comme l'officiel (il réécrit `count` @28) ; NGemity renvoie le compte de la pile.
- **Non modélisés** : poids (aucun système de poids), règle PK, entrepôt ouvert (51, 88 : aucun état
  d'entrepôt en session), `GameRule::bDisableTrade`, blocage de compte sur « Add Trade Bug ». Échangeable =
  non porté et pas une carte d'invocation liée (`0x8000_0000`) ; les autres règles d'`IsTradable` ne sont
  pas lues.

## 6. NON ÉTABLI

- Ce que la fenêtre du client affiche pour chaque code de refus ; aucun n'a été observé en jeu.
- Si le client attend `CANCEL` ou seulement le résultat après un refus à la confirmation (l'officiel ferme
  sans `CANCEL`, ce que fait ce dépôt).
- Le geste client qui envoie `MODIFY_COUNT` plutôt que `REMOVE_ITEM` puis `ADD_ITEM`.
