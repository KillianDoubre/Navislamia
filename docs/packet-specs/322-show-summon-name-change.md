# 322 — TS_SC_SHOW_SUMMON_NAME_CHANGE

Livré le 2026-10-05. Sources : serveur officiel 2015, `Game/Message/`, `Game/Script/` et
`Resource/Script/` sous `A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer`.
Les mentions rzu désignent `librzu/src/packets/GameClient/`, commit
`87c1e83bf84efe29bb6405e8e6da80349712f3fa` (copie locale vérifiée).
Client mesuré : `A:\Rappelz Kiff\Epic_7_3\SFrame.exe`, SHA256
`41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`.

## Trame Epic 7.3

Serveur → client, **11 octets**, sans rembourrage. En-tête commun de 7 octets
(`uint32 Length`, `uint16 ID`, checksum = somme des six premiers octets).

| Offset | Type | Champ | Source |
|---|---|---|---|
| 7 | uint32 | handle de l'invocation | `GameMessage.h:1152` ; `TS_SC_SHOW_SUMMON_NAME_CHANGE.h:6` |

Aiguillage client `0x67DF59` → branche `0x67E27D` → gestionnaire `0x66F540`.
Lecture `mov edx, dword ptr [ecx+7]` en `0x66F57E`, mesurée avec
`tools/client73_packets.py` et Capstone. rzu remappe 322 en 1322 seulement à partir de 9.6.3
(`TS_SC_SHOW_SUMMON_NAME_CHANGE.h:8-10`) ; le serveur 7.3 déclare 322.

## Cycle porté

- `SCRIPT_ShowCreatureNameChangeBox`, `ScriptPlayer.cpp:4705-4742` : chercher une invocation du
  joueur par slot 0..5 ou handle, refuser `RIDING_LENT`, retenir la cible, ouvrir 322.
- `SendShowCreatureNameChangeBox`, `SendMessage.cpp:4038-4051` : autorisation de session
  `creature_name_change`, handle dans 322, envoi au propriétaire.
- `NPC_CreatureSetup.lua:2695-2808` : menu dynamique des invocations formées, contrôle de
  l'or avant l'ouverture, tarif affiché `niveau × 1000`. Ces fonctions officielles sont exportées
  dans `npc_dialogs.json` et exécutées par `NpcScriptService`.
- [323](323-change-summon-name.md) : **26 octets**, nom seul, aucun handle. La cible retenue
  dans la session est consommée une fois, même lors d'un refus.
- `GameMessage.cpp:7409-7446` : refus `@117` si trop court, `@118` si identique exactement,
  `@17` si invalide ou identique sans tenir compte de la casse. Noms de 4 à 18 caractères,
  filtre des mots interdits, aucune unicité globale des noms d'invocations.
- `NPC_CreatureSetup.lua:2813-2827` : débiter `niveau × 1000` et modifier le nom. Ici le nom
  et le solde sont sauvegardés ensemble par `CharacterService`, avec vérification du propriétaire
  et de la carte portée. Le débit en mémoire est remboursé si la sauvegarde échoue ; l'or est
  contrôlé à nouveau à la réponse pour éviter une ouverture suivie d'une dépense concurrente.
- `GameMessage.cpp:7458-7476` : `@129` si échec, propriété 507 `name` au maître,
  30 aux observateurs lorsque l'invocation est sortie. Son nom en mémoire, dans sa carte et
  dans son entrée mondiale est mis à jour. Une sauvegarde de progression retardée ne réécrit
  plus le nom et ne peut donc annuler le renommage.

`RidingKind` conserve maintenant la valeur importée de `SummonResource.is_riding_only` :
2 = prêt, 1 = monture payante. La restriction n'interdit pas toutes les montures.
Les changements de nom en cours rejoignent `NameChangeCompletion`, attendue lors de la sauvegarde
de déconnexion. Les trames serveur 322 reçues d'un client sont consommées sans effet.

## Vérifications

`SmallPacketsTests`, `CreatureTests.SmallPackets`, `NpcScriptTests.SmallPackets`,
`SmallPacketsPersistenceTests` : taille, handle, cible détenue, ouverture depuis le Lua,
tarif, refus, consommation de l'autorisation, remboursement, sauvegarde nom/or et protection
contre une ancienne sauvegarde. En jeu : choisir l'invocation chez le dompteur, renommer,
observer le nom depuis un second client, puis se reconnecter.

## NON ÉTABLI

- Les noms internationaux restent hors du transport ASCII existant du 323/30. Le port
  utilise la validation ASCII commune du serveur et le filtre officiel des mots interdits.
- Aucune capture en jeu de cette fenêtre n'a été effectuée pendant ce lot. La disposition
  de ses contrôles dépend des ressources du client ; la trame et ses champs sont mesurés.
