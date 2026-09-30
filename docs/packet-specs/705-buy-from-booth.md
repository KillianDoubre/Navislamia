# Commerce d'étal — `TM_CS_BUY_FROM_BOOTH` (705), `TM_CS_SELL_TO_BOOTH` (706), noms (707/708), fermeture (709), relevé (710)

Epic 7.3. Base : `master` `ce000be` (visibilité entre joueurs et visibilité des étals mergées). Cartes
Trello `3z9k1hUw` (705), `fjVehXJy` (706), `g441PhKy` (707). Cette fiche prolonge `socle-booths.md`
(700/701) et `socle-booths-visibilite.md` (702/703/704) et **corrige** la taille de 705 qu'y donnait le
premier socle.

## 1. Identité

| id | nom | sens | 7.3 | ≥ EPIC_9_6_3 |
|---|---|---|---|---|
| 705 | `TM_CS_BUY_FROM_BOOTH` | C→S | 705 | 1705 |
| 706 | `TM_CS_SELL_TO_BOOTH` | C→S | 706 | 1706 |
| 707 | `TM_CS_GET_BOOTHS_NAME` | C→S | 707 | 1707 |
| 708 | `TM_SC_GET_BOOTHS_NAME` | S→C | 708 | 1708 |
| 709 | `TM_SC_BOOTH_CLOSED` | S→C | 709 | 1709 |
| 710 | `TM_SC_BOOTH_TRADE_INFO` | S→C | 710 | — |

Les six ids sont déclarés dans `GamePackets` et ont un bras dans `GameClient` : 705/706/707 sont traités,
708/709/710 sont journalisés et abandonnés s'ils arrivent du client (un membre déclaré sans bras atteint le
`throw` final). Aucun champ n'est gaté entre 7.3 et 9.6.3 hormis ceux du motif d'objet (§3.1).

NGemity déclare les structures et ne traite **aucun** paquet de la famille (`Messages.cpp:573-577`,
commentaires) : il n'y a **aucune logique de référence**. Tout ce que fait le serveur est soit mesuré dans
le client, soit une décision de Navislamia listée au §6.

## 2. Ce que le joueur fait

- **Voir un étal** : un joueur qui en voit un autre reçoit son `TS_SC_ENTER` avec le bit d'étal dans
  `status` (§5.1) ; le client demande alors le nom par 707 et l'affiche au-dessus du personnage.
- **Ouvrir la fenêtre** : clic sur le panneau → 702, réponse 703 (socle visibilité des étals).
- **Acheter** (étal de vente) : le client choisit des objets et des quantités dans sa fenêtre 703 et
  envoie **une** 705 pour tout le panier.
- **Vendre** (étal d'achat) : le client envoie **une 706 par objet proposé** (le constructeur est appelé
  dans une boucle sur la liste, `SFrame.exe 0x48E7A0`).

## 3. Structure sur le fil (mesurée dans `SFrame.exe`)

### 3.1 705 — **13 + 75 × N** octets (et non 13 + 85 × N)

| offset | taille | champ | preuve client |
|---|---|---|---|
| 0 | 4 | `Length` = `13 + 0x4B × N` | `0x48E665 imul $0x4b` / `0x48E668 add $0xd` |
| 4 | 2 | id = `0x2C1` | `0x48E684` |
| 7 | 4 | `target` (handle de l'étal = handle du propriétaire) | `0x48E6B3` (`[ebx+0x13]`) |
| 11 | 2 | `count` `int16` = N | `0x48E6CD` |
| 13 | 75 × N | motifs d'objet | copie de 18 dwords + 1 mot + 1 octet par objet, pas destination `0x4B`, pas source `0x53` (`0x48E71D-0x48E72D`) |

Chaque motif est **les 75 premiers octets de l'enregistrement de 83 octets reçu dans la 703** : handle et
code sont ceux de l'objet du propriétaire, `count` (offset 16 du motif, `int64`) est la quantité demandée.
`socle-booths.md` §3.4 donnait `13 + 85×N` d'après le `TS_ITEM_FIXED_INFO` de rzu (motif + `wear_position` +
`own_summon_handle` + `index`) : **le client ne l'envoie pas**, la ligne y est corrigée. Le serveur ne lit
que `handle` (+0), `code` (+4) et `count` (+16) ; le reste décrit l'objet du propriétaire, relu en base.

### 3.2 706 — **19** octets

`target` @7, `item_handle` @11 (objet du **vendeur**), `cnt` `int32` @15. Le constructeur alloue `0x13`
(`0x48E7C5`) et recopie un enregistrement de 16 octets de la fenêtre (`0x48E810-0x48E81D`).

### 3.3 707 — **11 + 4 × H** octets

`count` `int32` @7, puis H handles. Taille `0xB + 4 × count` (`0x48F15E-0x48F165`), id `0x2C3` (`0x48F125`).

### 3.4 708 — **11 + 53 × N** octets

`count` `uint32` @7 (comparé en dword, `0x6736C7`), enregistrements à partir de +11 (`0x67367B`) au pas
`0x35` = 53 (`0x67370B`) : `handle` (4) + `name` (49, terminé par un nul). Le serveur coupe un nom à 48
octets pour garder le nul.

### 3.5 709 — **11** octets

`target` @7 (rzu `TS_SC_BOOTH_CLOSED.h:6`, gestionnaire client `0x66D9B0`).

### 3.6 710 — **14 + 83 × N** octets

`target` @7, `is_sell` `bool` @11, `count` `uint16` @12, enregistrements de 83 octets à +14, le motif de
703 (75 + `gold` `int64`) : gestionnaire `0x6734D0` (`[esi+0x7]`, `[esi+0xb]`, `[esi+0xc]`, `[esi+0xe]`,
pas `0x53`).

## 4. Le sens de `type` — **1 = vente, 2 = achat** (déduit, à confirmer en jeu)

Aucune source ne le nomme. Deux mesures convergent :

1. la 700 écrit `type = (drapeau != 0) + 1` (`setne al` / `inc al`, `0x48D5FB`) : **1 est la valeur par
   défaut** de la fenêtre de création, et l'étal par défaut d'un joueur est un étal de vente ;
2. le gestionnaire de 703 ne garde de `type` que le booléen `type == 1` (`0x673347-0x67334F`), exactement
   comme la 710 porte un booléen que rzu nomme `is_sell`.

D'où `BoothTradeRules.SellBooth = 1`, `BuyBooth = 2`, et les bits de statut
`CreatureStatus.PlayerSellBooth` (1 << 10) pour 1, `PlayerBuyBooth` (1 << 9) pour 2 (rzu
`TCS_FlagSellBooth` / `TCS_FlagBuyBooth`, NGemity `FLAG_SELL_BOOTH` / `FLAG_BUY_BOOTH`). **Vérification en
jeu en un geste** : ouvrir un étal de vente et lire le `type=` que le serveur journalise.

## 5. Traitement (`BoothTradeService`)

### 5.1 Publication

`TS_SC_STATUS_CHANGE` (500) du propriétaire, avec son masque complet (`ActorStatus.ForPlayer(info)`, qui
compose désormais PK, assis, combat, marche **et** étal), part vers le propriétaire et vers chaque client
qui le voit (`IPlayerVisibilityService.SendToObservers`, envoi sous le verrou du destinataire) : à
l'ouverture (700 accepté) et à la fermeture. Un joueur qui entre dans la fenêtre reçoit le bit dans le
`TS_SC_ENTER` du propriétaire. NGemity compose le même masque à l'entrée (lignes commentées
`Messages.cpp:574-577`).

### 5.2 Noms (707 → 708)

Chaque handle demandé est résolu par le registre de sessions ; seuls les étals **ouverts** sont nommés,
avec les octets bruts que la 700 avait déclarés. La réponse part même vide (compte 0).

### 5.3 Achat (705)

1. Le client acheteur doit **regarder** cet étal (`WatchedBoothHandle == target`, posé par la 702) — sinon
   `NotActable` ; il ne peut pas acheter à son propre étal (`NotActable`) ; l'étal doit être ouvert
   (`NotExist`).
2. Chaque motif doit encore décrire l'objet du propriétaire (même handle, même code) — sinon `NotExist`
   (fenêtre périmée).
3. **Réservation** (`BoothTradeRules.ReservePurchase`, sous le verrou de l'étal) : étal de vente
   (`NotActable` sinon), chaque objet déclaré au plus une fois (`InvalidArgument`), 1 ≤ quantité ≤ unités
   restantes (`NotEnoughItem`), prix = quantité × **prix unitaire** déclaré, total ≤ plafond (`TooMuchMoney`).
   Les unités sont retirées de l'étal **avant** tout mouvement.
4. **Or**, sous le verrou d'or de chaque session : débit de l'acheteur (`NotEnoughMoney`), crédit du
   propriétaire plafonné à 100 000 000 000 (`TooMuchMoney`, NGemity `MAX_GOLD_FOR_INVENTORY`).
5. **Objets** : `ICharacterService.TransferItemsAsync`, sous les deux verrous de personnage pris dans un
   ordre fixe (`CharacterGate.RunPairAsync`) : tout ou rien, une pile entière change de propriétaire en
   gardant sa ligne (enchantement, châsses, familier), une partie de pile devient une nouvelle ligne aux mêmes
   attributs, et **les deux soldes d'or sont écrits dans la même sauvegarde**.
6. Tout échec après la réservation rend l'or et les unités.
7. Succès : acheteur ← 1001 (or), 207 (objets reçus), `TS_SC_RESULT(705, Success, handle de l'étal)` ;
   propriétaire ← 1001, 255 ou 254 par pile, **710** (`target` = acheteur, `is_sell` = 1, objets à leur prix
   unitaire) ; chaque fenêtre ouverte sur l'étal ← 703 rafraîchie ; étal épuisé → fermeture (§5.5).

### 5.4 Vente (706)

Symétrique : l'objet proposé est dans le sac du **vendeur**, non porté (`NotActable`), `cnt` ≤ pile
(`NotEnoughItem`) ; l'étal doit être un étal d'achat ; il déclare des **objets modèles** de son propriétaire,
et l'offre est appariée par **code** au premier modèle qui en veut encore au moins `cnt` (`NotExist` sinon,
`LimitMax` au-delà). Le propriétaire paie (`NotEnoughMoney` si son or ne suffit pas), le vendeur est crédité
(`TooMuchMoney`), puis transfert vendeur → propriétaire. 710 part avec `is_sell` = 0.

### 5.5 Fermeture

701, retour au lobby, déconnexion et étal épuisé passent par `BoothTradeService.CloseBooth` : statut sans
bit d'étal publié, puis **709** à chaque client qui regarde l'étal, dont l'observation est oubliée.

### 5.6 Garde

705 et 706 rejoignent `BoothRules.GateAction` : un joueur dont l'étal est ouvert ne commerce pas avec un
autre étal (`smsg_booth_not_use_store`). Un seul commerce par client à la fois
(`ConnectionInfo.BoothTradeInProgress`) : un double clic pendant l'attente de la base est abandonné.

## 6. Décisions de Navislamia (aucune référence)

1. `type` 1 = vente, 2 = achat (§4).
2. Le `gold` déclaré dans la 700 est un **prix unitaire** ; le `cnt` déclaré, les unités offertes (vente) ou
   voulues (achat). La 703 affiche désormais les unités **restantes** (au plus la pile), non plus la pile
   entière.
3. Il faut regarder l'étal pour commercer.
4. L'étal se ferme quand tout est vendu ou acheté.
5. Succès acquitté par `TS_SC_RESULT` taggé 705/706 avec le handle de l'étal (aucun acquittement dédié
   n'existe ; les refus prennent la même forme, comme 702).
6. 710 part au **propriétaire** seul, `target` = le client avec qui il a commercé.
7. Aucun poids n'est jugé (`smsg_booth_heavy`) : le dépôt n'a pas de système de poids.

## 7. NON ÉTABLI

- Le sens de `type` (§4) et donc le choix entre les deux bits de statut.
- La sémantique de `target` dans 710, et si 710 va aussi au client (acheteur/vendeur).
- Si le client attend une réponse précise après une 705/706 réussie pour fermer sa fenêtre d'achat.
- Si le client 7.3 demande les noms (707) à l'entrée d'un pair ou seulement au changement de statut.

## A VERIFIER PAR KILLIAN

Protocole en jeu, **deux clients** (A propriétaire, B client) proches l'un de l'autre :

1. A ouvre un étal **de vente** (fenêtre par défaut) : le journal serveur affiche `Booth opened … type=1`.
   Si c'est `type=2`, inverser §4 (une constante et deux bits).
2. B voit le panneau et le nom de l'étal au-dessus de A (bit de statut + 707/708).
3. B clique le panneau (702/703), achète une partie d'une pile : l'or passe de B à A, l'objet apparaît dans
   le sac de B, la pile de A diminue, la fenêtre de B se met à jour, A voit le relevé 710.
4. B achète le reste : l'étal se ferme chez tout le monde (709, panneau disparu).
5. A ouvre un étal **d'achat** avec un objet modèle ; B vend un objet du même code : or de A vers B, objet vers A.
6. Refus : acheter sans assez d'or → message d'or insuffisant, rien ne bouge.
