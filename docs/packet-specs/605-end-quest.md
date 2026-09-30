# 605 — `TM_CS_END_QUEST` (terminer une quête)

Epic 7.3. Carte Trello `BAMvMQ7u`. Lot **(b1)** de `docs/packet-specs/socle-cycle-quete.md` §5.5 : la trame est
lue, jugée et **toujours refusée** ; terminer réellement une quête est le lot (b4). Base : la branche 604
(`hermes/packet-604-quest-info`) fusionnée avec `master` `74b1d80`.

## 1. Identité

| | |
|---|---|
| id 7.3 | **605** (`TM_CS_END_QUEST`) — rzu passe à 1605 à partir d'`EPIC_9_6_3`, à ne pas déclarer |
| sens | client → serveur |
| réponse | `TS_SC_RESULT` (0) taggé **605** ; aucune trame dédiée n'existe |
| références | rzu `TS_CS_END_QUEST.h:6-7` ; NGemity `Player::EndQuest` (`Player.cpp:2333-2420`) ; client `SFrame.exe` |

## 2. Ce que le joueur fait

Le bouton de confirmation de la fenêtre de quête (`quest_dialog`) ou de la fenêtre de récompense
(`SUIQuestRewardWnd`) : message interne 180 (`0x0061ac00`), texte de déclencheur `end_quest( %d, %d )`
(`0x00a2f150`). Détail : `socle-cycle-quete.md` §2.2.

## 3. Structure sur le fil — **12 octets**

| offset | type | champ | source |
|---|---|---|---|
| 0 | `uint32` | `Length` = 12 | client `0x0048d6da` |
| 4 | `uint16` | `ID` = 605 | client `0x0048d6d1` |
| 6 | `uint8` | checksum | client `0x0048d6e7-0x0048d6fa` |
| 7 | `int32` | `code` | rzu `:6`, client `0x0048d6fd` |
| 11 | **`int8`** | `nOptionalReward`, **`-1` = aucune** | rzu `:7`, client `0x0048d700` ; `0xff` écrit en `0x0061aeae` |

`GameActionPackets.TryReadEndQuest` n'accepte que 12 octets et lit `nOptionalReward` en `sbyte` : lu en octet,
le `-1` du client deviendrait l'emplacement 255.

## 4. Gating de version

Seul l'id est gaté (605 sous `EPIC_9_6_3`). Aucun champ ne l'est.

## 5. Traitement (`QuestService.EndQuestAsync`)

| cas | réponse | pourquoi |
|---|---|---|
| trame d'une autre longueur que 12 | `TS_SC_RESULT(605, InvalidArgument)` | comme une 603 mal formée |
| `code < 0` | `NotActable` | la règle de 603 (`QuestDropRules`) : aucune quête ne porte un code négatif |
| `nOptionalReward` hors `-1..5` | `InvalidArgument` | décision `socle-cycle-quete.md` §10 point 3 : six emplacements dans la donnée 9.4 |
| état illisible | `DBError` | |
| quête non portée par le personnage | `NotActable` | le verdict de NGemity pour une quête absente de l'état (§5.4 de la fiche socle) |
| **quête portée** | **`NotActable`**, journalisé en `Warning` | terminer une quête (la juger finissable, l'or, l'exp/JP, la récompense par défaut et l'optionnelle, le retrait des objets collectés, la marque « terminée », la 600) est le lot **(b4)** : il faut le catalogue de quêtes importé et la progression, qui n'existent pas. Répondre `Success` sans rien donner ferait annoncer au client une récompense fantôme |

Rien n'est écrit et aucune 600 n'est renvoyée : l'état ne change pas. Le bras de dispatch est dans
`GameClient.OnDataReceived`, à côté de 604 ; le traitement attend la base hors de la boucle de réception.

## 6. Écarts assumés avec NGemity

- NGemity répond à un échec par un message de quête texte (`SendQuestMessage(120, "END|FAIL|0")`) : son format
  d'affichage en 7.3 n'est pas établi (`socle-cycle-quete.md` §7.6), le socle répond par `TS_SC_RESULT`, le canal
  de refus de toute la famille (603).
- Aucune quête ne peut aujourd'hui être acceptée ni progresser : la réponse « quête portée » n'est atteignable
  qu'avec un état écrit à la main.

## 7. NON ÉTABLI

- Ce que le client affiche pour `TS_SC_RESULT(605, NotActable)`.
- La réponse exacte d'un serveur 7.3 à une fin de quête réussie (NGemity : messages `END|EXP|…`,
  `END|REWARD|…`, puis la 600).

## A VERIFIER PAR KILLIAN

1. En jeu : sans quête active, rien ne permet d'envoyer 605. Avec une quête écrite en base (`CharacterQuests`),
   ouvrir la fenêtre de quête et confirmer : le serveur journalise « ending a quest is not implemented,
   refused », le client ne reçoit aucune récompense, la quête reste dans la liste.
2. Le lot (b4) — acceptation, progression et fin avec récompenses — reste à planifier : il dépend de l'import du
   catalogue (`tools/Import-QuestResources.ps1`, décision §10 point 4 de la fiche socle).
