# Hôtel des ventes — mécanique (1300-1310, coffre 1350-1352)

Suite de `socle-encheres.md` (formats 1301/1303/1305, motif d'objet de 75 octets) et des fiches 1300…1310. Source
de la logique : le serveur officiel (`AuctionManager.cpp/.h`, `AuctionBase.cpp/.h`, `GameMessage.cpp`
`onAuction*`/`onItemKeeping*`, `SendMessage.cpp` `fillAuctionMessageBuffer`, `GameRule.h`). NGemity n'implémente rien.

## 1. Données

- Tables Telecaster (migration `Version0018_AuctionHouse`) : `AuctionListings` (une enchère en cours : objet, vendeur,
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

- Échange en cours et entrepôt ouvert ne sont pas vérifiés (aucun état de session ne les expose ici).
- Un objet repris du coffre ne rejoint pas une pile existante (nouvelle ligne).

## 5. Enchères automatiques (2026-10-04)

Sources : `AutoAuctionLoader.cpp`, `AuctionManager::onProcess`, `AutoAuctionRegistrationInfoLoader.cpp` et
`StructMisc.h` (`GAIA_MEMBER_SHIP = 9004`). Les **39 définitions** de `AutoAuctionResource.csv` sont exportées dans
`AuctionCatalog.AutomaticAuctions`, avec le nom du vendeur résolu depuis `StringResource_EN`.

- Le tick de 10 secondes publie un exemplaire, sans personnage vendeur (`SellerId = 0`), sans taxe d'inscription
  ni achat immédiat. Prix initial et durée viennent des ressources : 6 h, 24 h ou 72 h.
- La date initiale est interprétée dans `AuctionCatalog:TimeZone` (`Europe/Paris` par défaut), puis convertie en
  UTC. `repeat_term` est un intervalle en jours de 86 400 secondes, comme dans l'officiel.
- `repeat_apply = 0` ne publie qu'une fois. Une ressource répétée publie à la prochaine période calculée à partir
  de son calendrier et de sa dernière inscription réelle. Après une longue interruption, elle publie un seul
  exemplaire : les semaines manquées ne créent pas de stock supplémentaire.
- La migration **`Version0019_AutomaticAuctions`** ajoute `AutoAuctionRegistrations` (dernier succès par ressource,
  index unique), `AutoAuctionResourceId` et `SecrouteOnly`. Objet, vente et historique sont écrits dans une seule
  transaction PostgreSQL. Un verrou transactionnel par ressource protège aussi la première inscription concurrente.
  L'historique reste présent après vente ou expiration ; un redémarrage ne recrée pas la même occurrence.
- Une vente réservée exige un état 9004 actif, y compris pour miser ou acheter directement par son identifiant.
  Elle est masquée dans les recherches et la liste des mises lorsque le pass est absent ou expiré.
- Une vente remportée livre l'objet au coffre du gagnant ; aucun paiement vendeur n'est créé pour le serveur.
  Sans gagnant, l'objet est supprimé à l'échéance.

### Compatibilité des données 9.4 avec le client 7.3

Les 39 définitions sont conservées. Avec `AuctionCatalog:LocalFlag = 1`, la ligne 1 est exclue par son masque
régional : `AutoAuctionLoader` **écarte** une ligne dont `local_flag & current_flag` est non nul. Parmi les 38 autres, **35 lignes
utilisent 17 codes absents du catalogue du client 7.3** ; elles sont ignorées avec un avertissement au démarrage.
Les trois lignes compatibles sont :

| Ressource | Objet | Accès | Durée | Répétition |
|---|---|---|---|---|
| 14 | 910005 | Pass village caché | 72 h | 7 jours |
| 15 | 950019 | Pass village caché | 72 h | 7 jours |
| 39 | 910005 | Public | 24 h | 7 jours |

Le mécanisme prend en charge les autres définitions dès que leurs objets sont présents dans le catalogue du
client utilisé. Il ne substitue pas un autre objet aux codes absents.

## 6. Essai PostgreSQL

`AuctionPostgreSqlTests` a été exécuté avec succès sur PostgreSQL le 2026-10-04. Chaque test crée un schéma
`auction_test_<GUID>`, y applique toutes les migrations Telecaster, puis le supprime ; le schéma de jeu reste intact.

Les deux essais vérifient : fractionnement d'une pile et prélèvement de taxe, mises et remboursement, achat
immédiat, rechargement des index, retraits d'or et d'objet, refus d'un autre propriétaire et d'un second retrait,
expiration du coffre, historique automatique après redémarrage, inscription concurrente sans doublon et retour
arrière intégral lors d'une écriture invalide (aucun objet orphelin ni taxe perdue).

Pour rejouer l'essai, fournir une connexion PostgreSQL dans `NAVIS_AUCTION_TEST_CONNECTION`, puis lancer :

```powershell
dotnet test Tests/Tests.csproj -c Release --filter FullyQualifiedName~AuctionPostgreSqlTests
```

Ces tests sont explicites : la suite normale n'exige pas un serveur PostgreSQL. Les tests `AuctionHouseTests`
couvrent aussi les horaires, répétitions, restrictions premium, incompatibilités et l'import des 39 définitions.

## Renommage immédiat (uscT2HaQ, 2026-10-05)

Source officielle 2015, chemins sous `GameServer/Game/` : `Db/DB_CreateCharacter.cpp:283,345-363`
(`smp_update_character_name`, puis groupe, guilde, enchères), `Community/GuildManager.cpp:885-888`
(`CHANGE_NAME|ancien|nouveau|`), `DaemonProc/AuctionManager.cpp:1602-1640` (vendeur et meneur).
Navislamia lit les membres/chefs par SID dans `Characters` ; aucun cache de noms de guilde séparé.
Le personnage et les deux noms dénormalisés des ventes sont enregistrés ensemble. Le verrou des enchères
couvre la sauvegarde et la mise à jour de son index, avec l'ordre enchères puis CharacterGate.
La guilde reçoit `@GUILD`/110 `CHANGE_NAME|ancien|nouveau|`. Pour un chef allié seulement,
l'alliance reçoit `@ALLIANCE`/150 `GLEADER_CHANGE|guilde|nouveau|`, format officiel de
`Message/GameMessage.cpp:4767`. Les invitations du même SID suivent aussi le nouveau nom.

**NON ÉTABLI** : le C++ 2015 n'appelle pas `GLEADER_CHANGE` au renommage lui-même ; cet envoi
est une adaptation explicite au cache de chefs du client, à vérifier en jeu. Aucun identifiant inventé.
Tests : `GuildTests.Rename_refreshes_guild_and_alliance_leader_only_when_needed`,
`AuctionHouseTests.Rename_updates_cached_seller_and_highest_bidder_and_preserves_other_names`,
`CharacterCommandsTests` (noms persistés après relecture). Vérifier `/change_name` avec les
fenêtres guilde/alliance et recherche enchères déjà ouvertes, puis après reconnexion.
