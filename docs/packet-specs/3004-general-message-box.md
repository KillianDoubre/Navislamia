# 3004 — TS_SC_GENERAL_MESSAGE_BOX

Livré le 2026-10-05. Sources officielles 2015 et binaires épinglés dans
[322](322-show-summon-name-change.md).

## Trame Epic 7.3

Serveur → client, **9 + L octets**, texte UTF-8 **sans terminateur**.

| Offset | Type | Champ | Source |
|---|---|---|---|
| 0..6 | en-tête commun | taille, id 3004, checksum | en-tête commun Epic 7.3 |
| 7 | uint16 | L, longueur du texte en octets | `Game/Message/GameMessage.h:2347` ; `TS_SC_GENERAL_MESSAGE_BOX.h:8` |
| 9 | L octets | texte | `SendMessage.cpp:1996` ; rzu `:9` |

SFrame : branche `0x67E702`, gestionnaire `0x67AF80`. Longueur à +7 en `0x67AFCA`,
texte à +9 en `0x67AFDA`, **NUL ajouté côté client** en `0x67AFEA`.
rzu conserve 3004 (`TS_SC_GENERAL_MESSAGE_BOX.h:12-13`), avec un commentaire « Since EPIC_7_4 »
à la ligne 11. **Ce SFrame 7.3 le route effectivement** ; le commentaire ne supprime pas sa
prise en charge démontrée dans le binaire utilisé par ce dépôt.

## Fonction Lua

Nom officiel **`dlg_general(text[, name])`**, enregistré dans `CaptainHerlockServer.cpp:679`.
`Script/ScriptPlayer.cpp:2583-2606` convertit le texte Lua en UTF-8 puis appelle
`SendGeneralMessageBox`, `SendMessage.cpp:1981-1999`. Limite totale **1024 octets**, donc
1015 octets utiles ; pas d'envoi au-delà. Le texte vide est représentable par une trame de 9 octets.

`NpcScriptService` expose cette fonction dans le bac à sable existant ; émission au joueur du
contexte, après réussite de l'exécution et de la sauvegarde. L'option `name` désignant un autre
joueur est ignorée. Le paquet reçu d'un client est consommé sans effet.

## Vérifications

`SmallPacketsTests` et `NpcScriptTests.SmallPackets` : taille, offset, checksum, caractères
accentués, absence de NUL, limites exactes et absence de boîte après échec du script.
En jeu : `dlg_general` avec un texte français accentué et une référence de texte `@...`.

## NON ÉTABLI

Le rendu des références de chaînes, du HTML et des contrôles de la boîte dépend du client et
n'a pas été capturé pendant ce lot. L'envoi à un autre joueur par nom reste hors du contexte
de script actuellement porté, comme pour le [3003](3003-show-window.md).
