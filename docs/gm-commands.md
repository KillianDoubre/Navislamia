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
| `/exp <montant>` | oui | Navislamia | ajoute de l'expérience (positif seulement), niveaux compris |
| `/jp <montant>` | oui | Navislamia | ajoute ou retire des JP, jamais sous 0 |
| `/joblevel <niveau>` | oui | Navislamia | monte le niveau de métier, sans coût en JP |
| `/learn <skill> [niveau]` | oui | Lua `learn_all_skill` | apprend une compétence (niveau max par défaut), sans JP |
| `/buff <state> [niveau] [secondes]` | oui | Lua `add_state` | pose un état (niveau 1, 300 s par défaut, 1 jour au plus) |
| `/immortal [on\|off]` | oui | Navislamia | les monstres frappent sans infliger de dégâts ; sans argument, bascule |
| `/pk [on\|off]` | oui | Navislamia | mode PK ; sans argument, bascule |
| `/home` | oui | Navislamia | retour au point de réapparition |
| `/target` | oui | Navislamia | id, niveau et PV du monstre ciblé |
| `/save` | oui | Lua `save` | sauvegarde la progression sans se déconnecter |
| `/chaos <montant>` | oui | Navislamia | ajoute ou retire du chaos, entre 0 et `int.MaxValue` |
| `/rate` | oui | Navislamia | rates effectifs (base × événement), temps restant et réglages non multiplicateurs |
| `/rate <type> <multiplicateur> <durée>` | oui | Navislamia | événement de rates annoncé à tous, `type` = `exp`, `jp`, `gold`, `drop`, `card` ou `all` |
| `/rate reset [type]` | oui | Navislamia | fin anticipée, d'un type ou de tous |
| `/rates` | non | Navislamia | rates effectifs, en lecture seule |

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
- **`/exp`** : ajoute à l'expérience cumulée, publie `TS_SC_EXP_UPDATE`, puis
  `LevelingService.ApplyExperience` résout autant de niveaux que la somme en vaut — le chemin d'une
  victoire. Positif seulement : l'expérience cumulée ne redescend jamais.
- **`/jp`** : `ConnectionInfo.CharacterJp` puis `TS_SC_EXP_UPDATE`, qui porte le JP à l'offset 19.
- **`/joblevel`** : chaque palier passe par `LevelingService.ApplyJobLevelUp`, le chemin du bouton,
  après avoir crédité exactement le coût de ce palier (`NextJobLevelCost`). Le solde de JP est donc
  inchangé et le client reçoit la séquence qu'il connaît : mise à jour d'expérience, propriété
  `job_level`, résultat 410, statistiques. La montée s'arrête là où la courbe de JP plafonne le palier
  (vers le niveau 10 pour le premier métier), et la réponse le dit.
- **`/learn`** : `CharacterService.SaveLearnedSkillAsync`, la persistance de la fenêtre d'apprentissage,
  avec le JP inchangé, puis un `TS_SC_SKILL_LIST` d'une ligne et les statistiques (une passive apprise
  les modifie). **La restriction de métier est ignorée** : un GM peut apprendre toute compétence que
  le catalogue connaît. Sans niveau, c'est le maximum de la compétence (`SkillCatalog.TryGetMaxLevel`).
  Une compétence hors de l'arbre du personnage peut ne pas apparaître dans sa fenêtre côté client.
- **`/buff`** : `ISkillCastService.ApplyState`, le chemin d'un buff lancé : même poignée d'état, même
  expiration par le tick de 500 ms, même rafraîchissement des statistiques. L'id doit exister dans
  `StateResource` (`IStateCatalog.Exists`), sinon le client recevrait un code d'état que rien ne décrit.
  La durée est en secondes, convertie en ticks `ar_time` (`× 100`).
- **`/immortal`** : `ConnectionInfo.IsImmortal`, lu par `MonsterAiRules.PlayerDamage(maxHp, immortal)`.
  Le monstre frappe toujours, le coup vaut 0. `/die` reste possible.
- **`/pk`** : `ConnectionInfo.PkMode` et le masque de statut, exactement ce que feront 800/801 ; la
  sauvegarde de fin de session le persiste déjà.
- **`/home`** : la position de retour de la mort/réapparition (`RespawnX`/`RespawnY`/`RespawnLayer`,
  la position d'entrée dans le monde), par `WarpService.Warp`.
- **`/target`** : lit la cible (`TargetHandle`), la résout parmi les monstres visibles, puis
  `MonsterWorldState` pour les PV courants. Ne modifie rien. Une cible PNJ répond « pas un monstre
  visible ».
- **`/save`** : `CharacterService.SaveProgressAsync` avec les mêmes arguments que la déconnexion.
- **`/chaos`** : `ConnectionInfo.CharacterChaos` puis `TS_SC_GOLD_UPDATE`, qui porte or et chaos.

## Rates

Le rate effectif d'un type est le **rate de base** multiplié par celui de l'**événement** en cours. Un
serveur x5 en événement x2 tourne donc à x10 et revient seul à x5 à la fin de l'événement.

**Rates de base** : section `Rates` de `DevConsole/appsettings.{env}.json` (`Dev` ou `Prod`, suivis, un
par serveur). Le fichier est relu à chaud (`reloadOnChange`) : une modification s'applique sans
redémarrage, au prochain kill ou au prochain apprentissage. Toutes les clés valent 1 (ou la valeur
d'origine) par défaut.

| Clé | Effet | Origine |
|---|---|---|
| `Exp` | exp gagnée par kill | NGemity `Game.EXPRate` |
| `Jp` | JP gagnés par kill ; **absente, elle vaut `Exp`** | NGemity (son `EXPRate` multiplie les deux, `World.cpp:529`) |
| `Gold` | or gagné par kill | NGemity `Game.GoldDropRate` |
| `ItemDrop` | chance de chaque emplacement de drop, plafonnée à 100 % | NGemity `Game.ItemDropRate` |
| `CreatureCardDrop` | facteur de plus sur un emplacement dont l'objet **direct** est une carte d'invocation (groupe 13) | NGemity `Game.CreatureCardDropRate` |
| `MonsterRespawnSeconds` | délai de réapparition d'un monstre tué (10) | constante du code |
| `GroundItemLifetimeSeconds` | durée de vie d'un objet au sol (120) | constante du code |
| `SkillJpCost` | facteur sur le coût en JP d'un niveau de compétence | Navislamia |
| `JobLevelJpCost` | facteur sur le coût en JP d'un niveau de métier | Navislamia |
| `MaxEventMultiplier` | plafond du multiplicateur de `/rate` (100) | Navislamia |
| `EventReminderMinutes` | rappel annoncé avant la fin d'un événement (5, 0 = aucun) | Navislamia |
| `EventStatePath` | fichier des événements en cours, relatif à la racine du serveur | Navislamia |

Un rate négatif, infini ou non numérique compte comme 0. Les quantités sont **arrondies au hasard** :
7 exp × 1,5 donne 10 ou 11 avec une chance sur deux, donc un rate fractionnaire est juste en moyenne.
C'est l'intention de NGemity (`GameRule::GetIntValueByRandomInt64`), pas son comportement : son test
`(rand % 100) / 100.0 + v >= v` est toujours vrai, donc il tronque toujours. Les **coûts** en JP sont
arrondis au supérieur, pour qu'un coût réduit ne devienne jamais gratuit par accident (seul un rate de
0 le rend gratuit) et que le prix ne change pas d'un essai à l'autre.

Le rate de carte suit `World::checkDrop` : il s'applique à l'emplacement dont le code est positif
(objet direct), avant toute résolution de groupe. Une carte atteinte par un groupe de drop n'en profite
pas.

**Événements** (`/rate`) :

- `/rate exp 2 1h`, `/rate drop 3 7200`, `/rate all 2 30m`. La durée est **obligatoire** (secondes, ou
  suffixe `s`, `m`/`min`, `h`, `d`), jusqu'à 30 jours ; le multiplicateur va de 0 à
  `MaxEventMultiplier`, en culture invariante (`1.5`), un `x` devant est accepté.
- Un nouvel événement sur un type **remplace** celui qui y tournait (x2 puis x3 = x3, pas x6). Un
  événement `all` suivi de `/rate exp 5 1h` laisse les autres types à x2.
- Annonces sur la ligne d'annonce (`CHAT_NOTICE`, expéditeur `@SYSTEM`) à tous les joueurs : au début
  (« Event: EXP x2 for 1h! »), un rappel `EventReminderMinutes` avant la fin si l'événement dure plus
  longtemps que ce délai, et à la fin (« The EXP x2 event is over. »). `/rate reset` annonce ce qu'il
  arrête ; il ne dit rien aux joueurs s'il n'y avait rien.
- Les événements sont **enregistrés** dans `EventStatePath` avec leur heure de fin (UTC) : un
  redémarrage les reprend avec le temps restant, et un événement fini pendant l'arrêt est abandonné
  sans annonce. Un fichier illisible est journalisé et le serveur démarre sans événement.
- Chaque début et chaque fin anticipée est journalisé en `Information` avec le nom du GM.

Code : `Game/Services/Rates/` — `RateEventBook` (règles pures), `RateService` (réglages, verrou,
fichier), `RateEventTicker` (fin et rappel, toutes les secondes). Tests : `Tests/Game/RatesTests.cs`.

**Pas encore de clé** — la fonctionnalité n'existe pas, et une clé sans effet ferait croire que ça
marche : `QuestExp`/`QuestJp`/`QuestGold` (604/605 ne sont pas traités), `ChaosDrop` (le chaos ne tombe
pas des monstres), `PvpDamage` (pas de PvP), stamina, artisanat, enchantement, apprivoisement.

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
- Que `/learn` sur une compétence d'un autre métier s'affiche, ou non, dans la fenêtre de compétences.
- Que `/buff` affiche l'icône et le compte à rebours (le rendu d'une icône d'état n'est pas établi
  pour ce client, voir CLAUDE.md, *Buffs*).
- Que le client 7.3 laisse passer un apprentissage ou une montée de métier dont le coût serveur
  (`SkillJpCost`/`JobLevelJpCost` < 1) est **inférieur** à celui de sa propre table : s'il grise le
  bouton sur sa table, un coût réduit ne sert à rien sous le prix d'origine, et son affichage reste
  celui d'origine dans tous les cas.
- Que `/joblevel` au-delà de quelques paliers ne perturbe pas le client, qui reçoit un résultat 410
  par palier.
