# Hôtel des ventes — mécanique (1300-1310, coffre 1350-1352)

Suite de `socle-encheres.md` (formats 1301/1303/1305, motif d'objet de 75 octets) et des fiches 1300…1310. Source
de la logique : le serveur officiel (`AuctionManager.cpp/.h`, `AuctionBase.cpp/.h`, `GameMessage.cpp`
`onAuction*`/`onItemKeeping*`, `SendMessage.cpp` `fillAuctionMessageBuffer`, `GameRule.h`). NGemity n'implémente rien.

## 1. Données

- Tables Telecaster (migration `Version0017_AuctionHouse`) : `AuctionListings` (une enchère en cours : objet, vendeur,
  fin UTC, prix de départ, prix immédiat, taxe, plus haute mise et son auteur, anciens enchérisseurs) et
  `AuctionKeepings` (le coffre : propriétaire, objet ou or, type `KEEPING_TYPE_*`, enchère et objet liés, échéance).
  L'ancien `AuctionEntity`/`ItemStorageEntity` (relations un-à-un obligatoires, inutilisables pour l'or) n'est pas
  employé.
- Un objet en vente ou au coffre est une ligne `Items` **sans personnage ni compte** : aucune requête de sac ou
  d'entrepôt ne la voit.
- `DevConsole/auction-catalog.73.json` (`tools/export_auction_catalog.py --client-items db_item.rdb`) : les 38
  catégories (`AuctionCategoryResource`, les mêmes que `db_auctioncategoryresource.rdb` du client) et, pour les
  28 194 objets du client, `name_id`, nom anglais (`StringResource_EN`), groupe et classe.
- `AuctionService` garde un index en mémoire chargé au premier usage ; chaque changement est une transaction
  (`AuctionStore`), sous `CharacterGate` du joueur dont l'or change.

## 2. Règles (officielles)

| | |
|---|---|
| Durées | 6 h / 24 h / 72 h pour `duration_type` 1 / 2 / 3 (toute autre valeur : 6 h) |
| Taxe d'inscription | 3 % / 4 % / 5 % du prix de départ, tronquée, payée à l'inscription |
| Prix plancher | départ ≥ prix de vente en boutique × quantité (`MarketSellPrice`), sinon `TooCheap` ; immédiat 0 ou ≥ départ |
| Mise | ≥ plus haute mise (la première peut égaler le départ), ≥ 1,01 × dès qu'il y a un enchérisseur, ≤ prix immédiat (`TooMuchMoney`) |
| Taxe de vente | 3 % du prix (tronqués), le reste au vendeur |
| Coffre | 15 jours, puis l'entrée et son objet disparaissent |
| Pages | 40 entrées ; anciens enchérisseurs gardés : 100 |
| Délais | 3 s après une recherche, 1 s après une liste (`CoolTime`) ; les actions n'en posent pas |

- **Inscription** (1309, valeur = code) : `NotExist`, `TooCheap` (×2), objet raté ou en apprivoisement
  (`ITEM_FLAG_FAILED` 1<<3, `ITEM_FLAG_TAMING` 1<<29) `NotActable`, étal ouvert ou regardé
  `NotActableWhileUsingBooth`, porté ou équipé par une invocation `NotActable`, carte de la formation `NotActable`,
  taxe non couverte `NotEnoughMoney`, objet inconnu du catalogue `NotActable`. Une partie de pile devient une
  nouvelle ligne. Réponses : 1001, 254 ou 255, résultat.
- **Mise** (1306) : or insuffisant `NotEnoughMoney` (avant tout), enchère inconnue `NotExist`, la sienne ou déjà en
  tête `AccessDenied`. La mise est **prélevée tout de suite** ; l'enchérisseur dépassé retrouve sa mise au coffre
  (type 32) et reçoit `@713`.
- **Achat immédiat** (1308) : la sienne `AccessDenied`, pas de prix immédiat `NotActable`. Le meneur ne paie que la
  différence ; un autre meneur est remboursé (34, `@997`). Objet au coffre de l'acheteur (2), vendeur : prix − 3 %
  (30) et taxe rendue (31), `@717`.
- **Annulation** (1310) : vendeur seul (`AccessDenied`), sans enchérisseur (`NotActable`) ; objet au coffre (4).
  La taxe n'est pas rendue (l'officiel non plus).
- **Échéance** (tick de 10 s) : avec enchérisseur, objet à lui (1, `@714`), vendeur payé (30, 31, `@717`) ; sans,
  objet (3) et taxe (31) au vendeur.
- **Coffre** (1350 → 1351, 1352) : autre propriétaire `AccessDenied`, poids `TooHeavy`, or au-delà de
  10 000 000 000 `TooMuchMoney` ; l'objet entre au sac (avec sa créature s'il s'agit d'une carte), l'or au solde, le
  tout écrit en une transaction.
- **Recherche** (1300) : catégorie −1 avec sous-catégorie −1 = tout, 0 = « divers » (la dernière catégorie, où
  tombent aussi les objets non classés, comme `getCategoryIndex`) ; mot-clé sur le nom anglais, sans casse ;
  « équipable » = `EquipmentService.CanWear` (niveaux, rang, classe, race, profondeur) ; drapeau 1 = meneur, 2 = aucun
  enchérisseur. Listes 1302/1304 : aucune entrée → `NotExist`, statut 1/2 (sans/avec enchérisseur ; à moi/à un
  autre).

## 3. Paquets du coffre

`TM_CS_ITEM_KEEPING_LIST` (1350, 11 octets, `page_num` @7), `TM_CS_ITEM_KEEPING_TAKE` (1352, 11, `keeping_uid` @7),
`TM_SC_ITEM_KEEPING_LIST` (1351) : `page_num`, `total_page_count`, `keeping_info_count` puis 40 ×
`TS_ITEM_KEEPING_INFO` de 96 octets (`keeping_uid` @0, motif d'objet @4, `duration` en secondes @79, `keeping_type`
@83, code/amélioration/niveau liés @84/88/92) : **3 859 octets** (même méthode que 5139/3899). Une entrée d'or est
l'objet d'or officiel : code 0, quantité = montant.

## 4. Écarts et NON ÉTABLI

- Pas d'enchères automatiques (`AutoAuctionResource`, 39 lignes 9.4) ni de vente réservée (`bSecrouteOnly`).
- Échange en cours et entrepôt ouvert ne sont pas vérifiés (aucun état de session ne les expose ici).
- Un objet repris du coffre ne rejoint pas une pile existante (nouvelle ligne).
- Le stockage n'a été exercé que sur un magasin en mémoire (tests) ; l'essai PostgreSQL reste à faire.
