# Artisanat des cartes de compétence — type 102

Implémenté le 2 octobre 2026. Complément au socle d'artisanat : les observations historiques sur
l'absence de définition des conditions 24/25 sont levées par le serveur officiel disponible.

## Source et recettes

Source locale : `Rappelz/program/server/GameServer/Game/GameProc/MixBase.h` et `MixManager.cpp`.
`StructItem.h:241` définit `GetSkillId()` par `ItemBase.nSkillID` : comparer les codes d'objet ou les
châsses de liaison ne convient pas.

- Condition 24 : même amélioration que la case référencée.
- Condition 25 : même compétence que la case référencée.
- Valeur 0 : cible principale ; valeur n : matériau secondaire n. Les indices invalides et compétences
  absentes sont refusés. L'appariement positionnel décidé pour le socle reste utilisé.
- Recette 6837 : deux cartes de groupe 10, cube 700401, amélioration 400.
- Recette 6838 : deux cartes de groupe 10, cube 700402, amélioration 560.
- Les deux conditions de la cible référencent la seconde case secondaire, contenant l'autre carte.
  Celle-ci doit être déclarée en quantité 1 ; le cube est consommé en quantité 1.

Les 2 505 cartes de chacun des deux exports disponibles référencent une compétence présente.
`ItemMatchFields.SkillId` est projeté depuis la colonne déjà importée : aucune migration supplémentaire.

## Effets et persistance

`EnhanceSkillCard` (`MixManager.cpp:852-946`) consomme les matériaux puis tire un entier dans
`[0,100000]`. Réussite si celui-ci est inférieur ou égal à `Percentage[amélioration] × 100000`.
La sélection du taux utilise le `Crafting:LocalFlag` existant, sans taux inventé.

La réussite consomme également une unité principale et crée une carte à +1, quantité 1, avec nouvelle
identité et copie des propriétés de la cible : niveau, drapeaux, châsses, origine, durabilité, expiration
et propriétés élémentaires. Le plafond de la ressource est vérifié avant le tirage.

Les ressources des deux recettes portent `fail_result = 2` (`SkillCard`) :

- jusqu'à +3 inclus : une unité principale est détruite ;
- au-delà : une unité principale est consommée et une carte distincte à −3 est créée.

Les piles restantes gardent leur amélioration. La cible et la seconde carte peuvent partager la même
poignée si au moins deux unités sont disponibles. Le commit additionne leurs consommations, vérifie
tous les matériaux et états avant modification et sauvegarde une seule fois sous le verrou du personnage.
Les objets portés, stockés, en vente ou équipés par une invocation ne sont pas des matériaux valides.
Les autres politiques d'échec conservent le traitement du socle : `Accessory` baisse de trois avec
plancher zéro ; les autres posent `FAILED` sans effacer les châsses.

## Paquets

Après le commit : 255 pour chaque pile restante ou 254 pour chaque pile épuisée, 207 pour la carte
créée ou modifiée, puis 257. Sur réussite, 257 contient la **nouvelle** poignée. Sur échec, il est vide,
y compris lorsqu'une carte dégradée est créée. Aucun deuxième 254 ne détruit une pile déjà signalée.

## Vérification

`Tests/Game/SkillCardCraftTests.cs` couvre les deux recettes, références de cases, compétences absentes,
améliorations incompatibles, limite inclusive du tirage, plafond, piles distinctes ou communes,
destruction et dégradation, rejeu, états modifiés avant commit, politiques d'échec, sauvegarde et
nouvelle identité par le vrai repository EF. Un test explicite PostgreSQL vérifie la projection et le
catalogue à partir d'une table isolée, supprimée après le test.

À vérifier avec le client : visibilité de la carte obtenue, quantité restante des piles, animation de
réussite/échec et conservation de la liaison d'une carte liée.
