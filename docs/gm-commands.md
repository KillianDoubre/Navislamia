# Commandes GM

Les commandes se tapent dans le chat du jeu. Toute ligne dont le premier caractère est `/`, quel que
soit le canal sauf le chuchotement, est une commande : le serveur l'exécute et ne la relaie jamais
comme message. C'est la règle de NGemity (`WorldSession::onChatRequest`). Le client 7.3 transmet bien
ces lignes au serveur : avant ce module, `/position` revenait en écho dans le chat.

Code : `Game/Services/GmCommands/` — `GmCommandParser` (lecture de la ligne), `GmCommandCatalog`
(table et permissions), `GmCommandRules` (bornes des arguments), `GmCommandService` (exécution).
Tests : `Tests/Game/GmCommandRulesTests.cs` et `Tests/Game/GmCommandServiceTests.cs`.

## Droits

Une commande **privilégiée** demande `Characters.Permission >= 100`, le seuil exact de NGemity
(`AllowedCommandInfo::Run`). La valeur est lue à l'entrée dans le monde, donc un changement en base
prend effet à la connexion suivante :

```sql
UPDATE "Characters" SET "Permission" = 100 WHERE "CharacterName" = 'Freezeraid';
```

Sans le droit, une commande privilégiée répond exactement comme une commande inconnue
(`Unknown command: /warp. Type /help.`) : le serveur ne révèle pas qu'elle existe, comme NGemity.
La tentative est journalisée en `Warning`, et chaque commande privilégiée exécutée l'est en
`Information` avec son auteur et ses arguments.

Les réponses partent sur la ligne système du chat, expéditeur `@SYSTEM`, type `0x1E` (`CHAT_EXP` chez
rzu et NGemity) — la convention de NGemity pour ses réponses de commande.

## Commandes

| Commande | Droit | Origine | Effet |
|---|---|---|---|
| `/help` | non | Navislamia | liste les commandes que l'appelant peut utiliser |
| `/position` | non | NGemity | `X`, `Y`, `Z` et couche du personnage |
| `/sitdown` | non | NGemity | s'asseoir : arrête l'attaque, publie le bit assis ; refusé mort |
| `/standup` | non | NGemity | se relever |
| `/battle [on\|off]` | non | NGemity | mode combat, `on` par défaut |
| `/walk [on\|off]` | non | NGemity | marche au lieu de course ; sans argument, bascule |
| `/doit` | oui | NGemity | tue tous les monstres visibles |
| `/notice <texte>` | oui | Navislamia | annonce à tous les joueurs en jeu (`CHAT_NOTICE`, `0x14`) |
| `/warp <x> <y>` | oui | Lua `warp` | téléporte le personnage |
| `/item <code> [nombre]` | oui | Lua `insert_item` | ajoute une pile au sac (1 par défaut, 10 000 au plus) |
| `/gold <montant>` | oui | Lua `insert_gold` | ajoute ou retire de l'or (`/gold -500`), jamais sous 0 |
| `/level <niveau>` | oui | Navislamia | monte au niveau indiqué, jamais vers le bas |
| `/heal` | oui | Navislamia | PV et PM au maximum ; refusé mort |
| `/die` | oui | Navislamia | PV à 0, pour tester la mort et la réapparition |

Le nom est insensible à la casse (`/Position` marche), contrairement à la comparaison exacte de
NGemity : rien dans le client ne distingue les deux, et un refus ne ferait que ressembler à une panne.

### Comment chaque commande passe par le jeu

Aucune commande n'a de chemin à elle : chacune réutilise ce que le serveur fait déjà, donc elle ne
peut pas produire un état que le jeu lui-même ne produit pas.

- **`/sitdown`, `/standup`, `/battle`, `/walk`** : `TM_SC_STATUS_CHANGE` (500) avec le masque complet
  de `ActorStatus.ForPlayer`, qui porte maintenant le mode PK, l'assise, le mode combat et la marche.
  Le masque est un instantané : chaque envoi passe les quatre états, sinon l'un éteindrait l'autre.
  Ces états sont de session (`ConnectionInfo.IsSitting`/`IsBattleMode`/`IsWalking`), non persistés.
- **`/doit`** : pour chaque monstre streamé vivant, `ICombatService.ApplyDamage` avec ses PV restants,
  puis un `TS_SC_ATTACK_EVENT` comme après un coup mortel. Cadavre, butin, récompense et réapparition
  sont donc les ordinaires. NGemity tue les monstres des régions visibles ; l'ensemble streamé au
  client est la même vue.
- **`/warp`** : `WarpService.Warp`, le chemin des portails. Coordonnées lues en culture invariante
  (`94454.5`), finies et positives.
- **`/item`** : le code est vérifié contre `ItemSortCatalog`, qui connaît chaque `ItemResource`, puis
  `CharacterService.AddItemAsync` et un `TS_SC_INVENTORY` d'une ligne, comme un ramassage. Un code
  inconnu est refusé avant la base : il créerait une ligne que le client ne sait pas afficher.
- **`/gold`** : `ConnectionInfo.CharacterGold` puis `TS_SC_GOLD_UPDATE` ; persisté par la sauvegarde
  de fin de session.
- **`/level`** : l'expérience cumulée monte au seuil du niveau cible
  (`LevelCurve.TryGetExperienceFor` : être niveau `N` demande `cumulativeExp[N - 1]`), puis
  `LevelingService.ApplyExperience` fait la montée de niveau ordinaire — mêmes paquets, statistiques
  et remplissage des PV. Seulement vers le haut : aucune séquence de perte de niveau n'est établie.
- **`/heal`** : les maxima de `StatService.Compute`, puis les propriétés `max_hp`, `hp`, `max_mp`, `mp`.
- **`/die`** : PV à 0 et la propriété `hp`. Dans cette version la mort n'a pas de paquet à elle
  (`docs/packet-specs/socle-mort-respawn.md`).

## Ce qui n'est pas porté, et pourquoi

- **`/run <lua>`** : exécute du Lua arbitraire. Le dépôt résout les scripts par catalogue et n'exécute
  jamais de Lua (portails, dialogues). Les fonctions Lua utiles sont exposées comme commandes propres
  (`/warp`, `/item`, `/gold`).
- **`/suicide`** : chez NGemity, malgré son nom, **arrête le serveur** (`World::StopNow`). Une commande
  de chat qui éteint le serveur n'est pas portée ; `/die` couvre le besoin de test.
- **`/regenerate <id> [nombre]`** : fait apparaître des monstres (`add_npc` chez NGemity). Ici
  `SpatialIndex` est immuable et construit au démarrage, et `MonsterWorldState`, le streaming, l'IA et
  le combat supposent tous des instances connues d'avance : l'ajout à chaud est un chantier de
  l'infrastructure des monstres, pas une commande.
- **`/pcreate`, `/pinvite`, `/pjoin`, `/plist`, `/pdestroy`, `/pleave`** : les groupes n'existent pas.
- **`/battle <handle>`** chez NGemity vise aussi une invocation ; les invocations n'existent pas, donc
  la commande ne vise que le personnage, et accepte `off` en plus.

## Sources

- NGemity, `Chihiro/src/Account/AllowedCommandInfo.cpp` : les 16 commandes et la règle de permission.
- NGemity, `Chihiro/src/Scripting/XLua.cpp` : les 64 fonctions Lua exposées aux scripts.
- Client 7.3, `db_localcommand.rdb` : 73 commandes que le client reconnaît lui-même (groupes, guildes,
  émotions, `returnlobby`...). Elles sont traitées par le client ; celles qui reviennent au serveur
  passent par ce module.
- Les listes de commandes en `&` qu'on trouve en ligne (`&summonplayer`, `&immortal`, `&linkto`...)
  **n'existent pas** dans ce client : aucune de ces chaînes n'est dans `SFrame.exe`. Elles appartiennent
  à un autre client et ne sont pas portées.

## À vérifier en jeu

- Que le client 7.3 affiche la ligne système `0x1E` et l'annonce `0x14`.
- Que `/sitdown` joue bien l'animation assise, et que se déplacer relève le personnage (le serveur ne
  suit pas le déplacement du personnage assis).
- Que `/die` ouvre la fenêtre de mort à partir de la seule propriété `hp`.
