# 3003 — TS_SC_SHOW_WINDOW

Livré le 2026-10-05. Sources officielles 2015 et binaires épinglés dans
[322](322-show-summon-name-change.md).

## Trame Epic 7.3

Serveur → client, **13 + W + A + T octets**, avec W/A/T = longueurs UTF-8 en octets.
En-tête commun de 7 octets ; trois chaînes contiguës **sans NUL**.

| Offset | Type | Champ | Source |
|---|---|---|---|
| 7 | uint16 | W, longueur du nom de fenêtre | `Game/Message/GameMessage.h:2337` ; `TS_SC_SHOW_WINDOW.h:6` |
| 9 | uint16 | A, longueur des arguments | `GameMessage.h:2338` ; rzu `:7` |
| 11 | uint16 | T, longueur du déclencheur | `GameMessage.h:2339` ; rzu `:8` |
| 13 | W octets | fenêtre | `SendMessage.cpp:1882` ; rzu `:9` |
| 13 + W | A octets | arguments | `SendMessage.cpp:1883` ; rzu `:10` |
| 13 + W + A | T octets | déclencheur | `SendMessage.cpp:1884` ; rzu `:11` |

SFrame : branche `0x67E70F`, gestionnaire `0x67AD90`. Lectures W en `0x67AE06`, A en
`0x67AE10`, T en `0x67AE0A`, début des données en `0x67AE16`.
Le champ argument est présent depuis 5.1, donc **présent en 7.3**. L'id reste 3003 dans toutes
les versions de rzu (`TS_SC_SHOW_WINDOW.h:7,10,13-14`).

## Fonction Lua et réponse

Le nom enregistré est **`dlg_special(window, trigger, argument[, name])`** :
`CaptainHerlockServer.cpp:680`, `Script/ScriptPlayer.cpp:2609-2635`. Le trigger et l'argument
sont inversés par rapport à leur ordre sur le fil. `SendMessage.cpp:1866-1886` impose une
limite de **1024 octets pour toute la trame**, sinon aucune émission. Les longueurs suivent
`strlen`, y compris l'arrêt à un NUL fourni dans une chaîne Lua.

`NpcScriptService` lie cette fonction au bac à sable de dialogue, props et zones d'événement.
La fenêtre est envoyée après réussite du script et de sa sauvegarde. Son déclencheur est
retenu dans la session et peut revenir par le **3001 existant**, même sans contact PNJ.
Seul le déclencheur exact annoncé est accepté, ou `nom()` pour un nom de fonction nu ;
l'autorisation est consommée une fois. Les actions de donjon utilisent `DungeonService`, les
autres fonctions prises en charge utilisent le même bac à sable. Aucun Lua transmis librement
par le client n'est exécuté. Une trame 3003 provenant d'un client est sans effet.

## Vérifications

`SmallPacketsTests` et `NpcScriptTests.SmallPackets` : tailles, ordre, UTF-8, limite de tampon,
rollback, destinataire, refus d'un callback altéré, consommation unique et callback sans PNJ.
En jeu : ouvrir une `confirm_window` depuis un script, confirmer une action annoncée et
vérifier qu'une répétition du 3001 ne la relance pas.

## NON ÉTABLI

- Les fenêtres de saisie qui ajoutent des paramètres fournis par le joueur au callback ne
  bénéficient pas d'une autorisation par préfixe. Leur syntaxe et validation demandent une
  mesure spécifique avant d'élargir le 3001 ; ce lot accepte les callbacks annoncés exactement.
- Le paramètre optionnel `name` est limité au joueur du contexte. La résolution d'un autre
  joueur par un script PNJ n'est pas portée ici.
- Le paquet ne prouve pas que chaque nom de fenêtre existe dans les fichiers `.nui` du client.
