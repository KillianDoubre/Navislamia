# Classement des dons : source persistante des paquets 5000/5001

Lot du 2026-10-04. Cette fiche remplace les hypothèses de source/rang des lots K2/K3 de
`socle-classements.md` ; le format de trame établi dans cette fiche reste identique.

## Preuve officielle

Sous `A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer\Game` :

- `DaemonProc/RankingManager.h` : 0 = donation, 1 = liste de bénéficiaires des récompenses,
  2/3/4 = Hunta du mois, du mois précédent et total ; `RANK_UNKNOWN = 0xffffffff`.
- `DaemonProc/RankingManager.cpp:14` : dix entrées ; seuil de 100 points pour le top des dons.
  Le règlement mensuel utilise le calendrier local du serveur.
- `Message/GameMessage.cpp:7266` : `AddRankingScore(DONATION, uid, name, immoral_avant - immoral_après, true)`.
- `Message/GameMessage.cpp:7351` : réponse au top, score brut `c_fixed10`, rang inconnu converti en
  `uint16` = **65535** ; le type 1 ne répond pas à une demande de scores.

## Données et calcul

Migration Telecaster `Version0022_DonationScores` : une ligne active unique par personnage et période
`YYYYMM` dans `DonationScores`, score `numeric(18,4)`, FK vers le personnage. La période suit
`TimeZoneInfo.Local`, comme le mois civil du serveur officiel. L'historique des mois précédents reste
en base ; le mois nouveau interroge un autre ensemble de lignes.

Le score est le **gain moral réellement crédité** par les dons d'or et d'objets. Ce n'est ni
`-ImmoralPoint`, ni le solde d'or, ni un score recalculé après une récompense ou un meurtre PK.
JP : le débit historique du dépôt reste pris en charge, mais aucun crédit de classement JP n'est ajouté
(la branche de récompense JP est commentée dans la source officielle).

`DonationStore` sérialise le personnage et valide tous les objets avant consommation. Débit d'or/JP,
objets, moralité, classement individuel et points entiers de guilde partagent une transaction PostgreSQL.
Les points de guilde sont incrémentés par une mise à jour SQL, pour ne pas perdre un don simultané
d'un autre membre. Échec de transaction : valeurs de session restituées et écritures annulées.
Les objets équipés, en entrepôt, en enchère ou liés à une formation ne sont pas consommés.

`DonationRankingService` lit les scores du mois courant et les personnages non supprimés :

- demandeur : position parmi tous les scores positifs, même en dessous de 100 ; score brut × 10 000 ;
- absent : rang 65535, score 0 ;
- top : dix scores au moins égaux à 100, décroissants ; égalités départagées par identifiant ;
- limite de stockage : 99 999 999 999 999,9999 points ; rang trop grand saturé à 65535 ;
- seuls le top et le rang du demandeur sont lus, pas une liste complète de tous les joueurs ;
- type 0 pris en charge ; les types 1 et Hunta ne produisent pas de classement de dons déguisé ;
- un échec DB est journalisé ; il ne produit pas une fausse liste vide réussie.

Une base sans historique de dons commence à zéro. Les dons antérieurs à ce compteur ne peuvent
être déduits de la moralité actuelle et ne sont pas inventés.

## Trames

| Paquet | Format, en-tête de 7 octets compris |
|---|---|
| 5000 | 8 octets : `int8 ranking_type` à 7 |
| 5001 | `20 + 41 × N` octets : type 7, rang demandeur `uint16` 8, score demandeur `int64` 10, N `uint16` 18 |
| entrée | rang `uint16` +0, nom ASCII `char[31]` +2, score `int64` +33 |

Maximum dix entrées et 430 octets. Le nom contient au plus 30 octets puis NUL. La réponse n'est
adressée qu'à la session qui a demandé le classement ; le personnage est revérifié après la lecture DB.

## Tests

`DonationRankingTests` : débit atomique, refus sans consommation partielle, reprise avec un nouveau
service, score après changement de moralité, mois nouveau, seuil, top dix, égalités, demandeur hors
top et trame 5000→5001. `RankingTopRecordPacketsTests` conserve les vérifications binaires.
`CommunityPostgreSqlTests` impose un échec d'écriture du score et vérifie le rollback du don et du cumul
de guilde sur une vraie base PostgreSQL, dans un schéma temporaire supprimé en fin de test.
