# Test de charge (`tools/LoadTest`)

Carte Trello `lBAK61Tg`. Des clients sans affichage jouent le protocole Epic 7.3 de bout en bout contre l'AuthServer
et le serveur de jeu, pendant que l'outil mesure ce qu'ils vivent et ce que vit le serveur.

## Lancer

AuthServer et DevConsole doivent tourner (`.\start-server.ps1 -Configuration Release`). Depuis la racine du dépôt :

```powershell
# Une fois : les comptes load001…load100 (mot de passe « load ») dans la base auth.
dotnet run --project tools/LoadTest -c Release -- seed --count 100

# Monter à 50 clients, tenir 120 s, monter à 100, tenir 120 s, écrire loadtest-AAAAMMJJ-HHMM.md.
dotnet run --project tools/LoadTest -c Release -- run --stages 50,100 --hold 120
```

Les adresses, les clés XRC4 et les bases sont lues dans `AuthServer/appsettings.json` et `DevConsole/appsettings.json`
(l'outil remonte jusqu'à `Navislamia.sln`). Options : `--auth ip:port`, `--game ip:port`, `--auth-key`, `--game-key`,
`--server-index`, `--ramp` (clients par seconde, 5), `--no-combat`, `--move-interval`, `--chat-interval`,
`--walk-radius`, `--hunt-range`, `--attack-seconds`, `--server-pid` (si plusieurs `DevConsole` tournent), `--report`.
`Ctrl+C` arrête la montée et écrit le rapport des étapes déjà jouées.

## Ce que fait un bot

- **Connexion** : `TS_CA_VERSION`, `TS_CA_ACCOUNT` (mot de passe DES `MERONG`), liste des serveurs, sélection, clé à
  usage unique ; puis au jeu `TM_CS_VERSION`, `TM_CS_ACCOUNT_WITH_AUTH`, liste des personnages, création au premier
  passage (`Load001`… — l'apparence est copiée d'un personnage existant de chaque race, les races tournent, donc les
  bots se répartissent sur les trois points de départ de l'île des apprentis), `TM_CS_LOGIN`.
- **En jeu** : répond aux `TS_TIMESYNC` ; marche en ligne droite vers un point tiré dans un rayon de 120 autour de
  son point d'apparition (≈ 50 unités/s, sous l'estimation du serveur pour ne pas déclencher l'anti-triche) ; parle
  en chat local ; attaque le monstre le plus proche à moins de 250 unités, après avoir marché jusqu'à lui, 15 s par
  cible ; ressuscite en ville (513 type 0) 3 s après sa mort. Sa position suit les échos `TS_SC_MOVE` du serveur et
  les téléportations.
- La couche réseau est le `CipherConnection` du serveur, utilisé côté client : le XRC4 a un flot par sens, la même
  classe parle des deux bouts. `Tests/Tools/LoadTestFramesTests.cs` vérifie chaque trame contre la structure ou les
  offsets que le serveur lit.

## Ce qui est mesuré

**Côté bots** (millisecondes, p50/p95/p99/max) : connexion auth, entrée en jeu, écho de marche, écho de chat, premier
coup après l'arrivée au contact ; trafic reçu ; marches sans écho en 5 s ; refus (`TS_SC_RESULT` non nul,
`TM_SC_CANT_ATTACK`) et déconnexions.

**Côté serveur**, si `DevConsole` tourne sur la même machine (aucun réglage : l'outil s'attache par EventPipe) :

- CPU (part de la machine), mémoire, threads du processus ;
- compteurs du runtime : pool de threads, GC, allocations, exceptions ;
- **attente de verrous** : les évènements de contention du runtime portent leur durée ; le rapport donne le nombre
  d'attentes et les millisecondes perdues par seconde ;
- le compteur `Navislamia` (`Game/Network/ServerMetrics.cs`), inerte sans auditeur :
  - durée d'un tick de chaque boucle (`combat`, `monster-ai`, `monster-movement`, `casts`, `buffs`, `creatures`,
    `pets`, `regeneration`, `ground-items`, `skill-effects`), comparée à son intervalle ;
  - traitement synchrone de chaque trame reçue, par identifiant (la partie asynchrone d'un gestionnaire n'y est pas) ;
  - passes de visibilité : objets (`SyncVisibleObjects`) et joueurs (`PlayerVisibilityService.OnMove`) ;
  - délai d'envoi : du moment où le plus ancien message d'un lot est mis en file à son écriture sur la socket ;
  - octets envoyés, trames reçues.

Les histogrammes du serveur sont publiés seconde par seconde : le rapport donne le p50 typique (médiane des médianes)
et les p95/p99 **de la pire seconde**. `dotnet-counters monitor -n DevConsole --counters Navislamia` montre les mêmes
mesures en direct.

## Lire le rapport

La section « Goulets détectés » liste les seuils franchis : une boucle dont le pire p99 dépasse son intervalle, un
écho de marche ou de chat p95 > 100 ms, une entrée en jeu p95 > 2 s, un délai d'envoi p99 > 50 ms, plus de 50 ms
d'attente de verrous par seconde, une file du pool de threads > 100 (des pointes de quelques dizaines au réveil simultané des minuteries sont normales), un CPU > 80 %, des bots hors jeu, plus de 1 %
de marches perdues. Chaque goulet confirmé devient une carte.

Limites : les bots tournent sur la machine du serveur et lui prennent du CPU ; le journal `Debug` de `DevConsole`
écrit une ligne par trame, ce qui pèse aussi (le rapport mesure la configuration réelle) ; les bots ne lancent ni
compétences, ni échanges, ni groupes.

## Premier passage (2026-10-06)

Machine de développement (12 cœurs), serveur et bots sur la même machine, journal `Debug`. Rapports :
[`load-tests/2026-10-06-50-100.md`](load-tests/2026-10-06-50-100.md) et, avant le correctif ci-dessous,
[`load-tests/2026-10-06-50-100-avant-correctif.md`](load-tests/2026-10-06-50-100-avant-correctif.md).

- **50 puis 100 clients sont tous entrés en jeu et y sont restés.** À 100 : écho de marche p95 0,9 ms (max 3,4),
  écho de chat p95 0,8 ms, entrée en jeu p95 4,3 ms, connexion auth p95 15 ms ; CPU du serveur 6 % de la machine au
  plus, 674 Mo ; 0,4 ms d'attente de verrous par seconde ; aucune boucle ne dépasse 3 ms dans sa pire seconde (le
  combat a 100 ms) ; délai d'envoi p99 de quelques millisecondes. **Aucun goulet à cette charge.**
- **Bug trouvé et corrigé** : `Client.Dispose` remettait `Connection` et `ConnectionInfo` à `null` à la déconnexion,
  alors que le registre de dégâts d'un monstre, son dernier attaquant, un cadavre en attente et le propriétaire d'un
  objet au sol gardent le client. Le tick suivant levait une `NullReferenceException` : **le tick de combat entier
  était abandonné pour tous les joueurs**, l'expiration des objets au sol aussi, et le coup fatal sur un monstre
  frappé par un joueur parti échouait — le monstre ne mourait plus (19 exceptions au premier passage, 0 au second).
  `Dispose` garde désormais les deux objets (drapeau `Client.Released`), une connexion fermée ignore les envois, et un
  joueur parti ne prend plus de part de récompense (`MonsterContribution`).
- **Écarts propres aux bots, pas au serveur** : 7 à 16 % des marches sont refusées par l'anti-triche (verdict
  « Correct ») parce que les bots marchent en ligne droite à travers les obstacles `.nfa`, là où un vrai client
  contourne ; et 60 à 70 % des attaques n'aboutissent à aucun coup en 15 s, le monstre ayant bougé depuis
  la position visée (les bots ne poursuivent pas).
- Ce que ce passage ne couvre pas : compétences, groupes, échanges, plusieurs zones, une machine dédiée, plus de
  100 clients.
