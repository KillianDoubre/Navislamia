# 512 — TS_SC_TARGET

Livré le 2026-10-05. Sources officielles 2015 et binaires épinglés dans
[322](322-show-summon-name-change.md).

## Trame Epic 7.3

Serveur → client, **11 octets** : en-tête commun 7 + **un seul uint32 target à +7**.
Source : `Game/Message/GameMessage.h:905-910`, champ `:909` ; rzu `TS_SC_TARGET.h:7`.
SFrame : branche `0x67E4B4`, gestionnaire `0x670600`, lecture du dword +7 en `0x67063E`.
L'acteur est le joueur contrôlé par le destinataire ; le paquet ne contient pas son handle.
Id 512 en 7.3 ; 1512 réservé à 9.6.3 et suivantes (`TS_SC_TARGET.h:9-11`).

## Raccordement

`SendMessage.cpp:3077-3082`, `SendTargetMsg`, envoie uniquement au joueur concerné.
`GameClient.SetTarget` fait de même et met à jour sa cible serveur. `/passist` le raccorde
au choix de cible d'un vrai membre du groupe, en traduisant les handles de monstres par
observateur et en gardant la ligne officielle `ASSIST|handle|`.

511 reste la déclaration de cible du client, bornée à 11 octets et sans écho 512 : un aller-retour
automatique de sélection pourrait boucler. Le 512 reçu d'un client ne produit aucun effet.

## Vérifications

`SmallPacketsTests` : taille, handle unique, zéro accepté pour désélectionner, cible serveur
mise à jour et absence d'écho 511. `PartyServiceTests` couvre l'appartenance réelle au groupe
et la traduction des handles. En jeu : assistance sur joueur, monstre puis cible vide ;
un second observateur ne doit pas changer de cible.

## NON ÉTABLI

La source officielle disponible définit `SendTargetMsg` sans aucun appelant retrouvé ; rzu
indique également « Seems unused » (`TS_SC_TARGET.h:5`). **L'ajout du 512 à `/passist` est un
raccordement Navislamia demandé pour exploiter ce paquet**, pas un appelant démontré du C++.
Le comportement officiel d'assistance par chat est conservé. L'effet visuel exact et la
compatibilité de ces deux notifications doivent être vérifiés en jeu.
