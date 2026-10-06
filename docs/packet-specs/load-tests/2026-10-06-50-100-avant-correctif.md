# Test de charge Navislamia — 2026-10-06 10:18

- Étapes : 50, 100 clients, 120 s chacune après une montée de 5 client/s
- Comportement : marche toutes les ~3 s dans un rayon de 120, chat local toutes les ~20 s, combat contre les monstres à moins de 250 unités (15 s par cible)
- Serveur observé : DevConsole (pid 11124), EventPipe actif
- Machine : 12 cœurs logiques ; les bots tournent sur la même machine que le serveur s'il est observé

Latences en millisecondes, mesurées par les bots entre l'envoi d'une requête et la réponse du serveur : p50 / p95 / p99 / max.

## 50 clients (130 s)

En jeu à la fin de l'étape : **50 / 50**.

| Mesure (client) | n | p50 / p95 / p99 / max |
|---|---:|---|
| Connexion auth (version → clé à usage unique) | 50 | 13,2 / 16,1 / 24,0 / 24,0 |
| Entrée en jeu (TM_CS_LOGIN → TS_SC_LOGIN_RESULT) | 50 | 4,0 / 4,8 / 11,2 / 11,2 |
| Écho de marche (TM_CS_MOVE_REQUEST → TS_SC_MOVE) | 1361 | 0,4 / 0,7 / 1,0 / 2,7 |
| Écho de chat (TM_CS_CHAT_REQUEST → TS_SC_CHAT_LOCAL) | 312 | 0,4 / 0,6 / 0,9 / 2,6 |
| Premier coup (TM_CS_ATTACK_REQUEST → TS_SC_ATTACK_EVENT) | 151 | 840,7 / 15022,2 / 15039,3 / 15048,0 |

Trafic : 348 trames/s et 14,2 Kio/s reçues par l'ensemble des bots, 20 trames/s envoyées. Marches 1362 (sans écho en 5 s : 0), chats 313, attaques 162, monstres tués 0, morts 0.

Serveur :

- CPU 0 % en moyenne, 4 % au plus (part de la machine) ; mémoire 602 Mo au plus ; 35 threads au plus
- Pool de threads : 17 threads au plus, file d'attente 24 au plus
- GC : 0,0 % du temps, 0 collectes de génération 2, allocation 2,4 Mio/s ; exceptions 3
- Verrous : 385 attentes, 0,2 ms d'attente par seconde (27 ms au total)

| Boucle | ticks | p50 typique | p95 / p99 de la pire seconde | intervalle |
|---|---:|---:|---|---:|
| combat | 1290 | 0,02 | 4,1 / 4,1 | 100 |
| casts | 2580 | 0,04 | 1,4 / 1,4 | 50 |
| monster-ai | 430 | 0,09 | 1,4 / 1,4 | 300 |
| buffs | 258 | 0,05 | 1,3 / 1,3 | 500 |
| creatures | 1290 | 0,01 | 1,3 / 1,3 | 100 |
| ground-items | 129 | 0,00 | 0,5 / 0,5 | 1000 |
| regeneration | 43 | 0,10 | 0,4 / 0,4 | 3000 |
| pets | 516 | 0,02 | 0,3 / 0,3 | 250 |
| monster-movement | 258 | 0,12 | 0,3 / 0,3 | 500 |
| skill-effects | 2580 | 0,00 | 0,1 / 0,1 | 50 |

| Mesure (serveur, ms) | n | p50 typique | p95 / p99 de la pire seconde |
|---|---:|---:|---|
| Délai d'envoi (file → socket) | 30109 | 0,09 | 0,7 / 2,0 |
| Visibilité des objets (PNJ, monstres, props, objets) | 1344 | 0,03 | 0,5 / 0,5 |
| Visibilité entre joueurs (diffusion d'une marche) | 1344 | 0,03 | 0,3 / 0,3 |

Traitement synchrone des trames reçues, les dix plus lentes :

| Trame | n | p50 typique | p95 / p99 de la pire seconde (ms) |
|---|---:|---:|---|
| 2001 | 95 | 0,475 | 2,83 / 2,83 |
| 1 | 50 | 0,449 | 0,99 / 0,99 |
| 2002 | 45 | 0,340 | 0,64 / 0,64 |
| 5 | 1344 | 0,160 | 0,63 / 0,63 |
| 100 | 158 | 0,010 | 0,56 / 0,56 |
| 2 | 200 | 0,005 | 0,42 / 0,42 |
| 20 | 309 | 0,027 | 0,19 / 0,19 |
| 150 | 18 | 0,054 | 0,10 / 0,10 |
| 50 | 50 | 0,042 | 0,09 / 0,09 |
| 2005 | 50 | 0,029 | 0,07 / 0,07 |

Le serveur a écrit 14,1 Kio/s sur ses sockets ; 18 trames de jeu reçues par seconde.

Problèmes relevés par les bots :

- refus : requête 5 → code 6 : 124
- refus : attaque impossible (raison 5) : 7

## 100 clients (130 s)

En jeu à la fin de l'étape : **100 / 100**.

| Mesure (client) | n | p50 / p95 / p99 / max |
|---|---:|---|
| Connexion auth (version → clé à usage unique) | 50 | 13,0 / 15,2 / 20,7 / 20,7 |
| Entrée en jeu (TM_CS_LOGIN → TS_SC_LOGIN_RESULT) | 50 | 3,2 / 4,9 / 5,8 / 5,8 |
| Écho de marche (TM_CS_MOVE_REQUEST → TS_SC_MOVE) | 2862 | 0,7 / 1,0 / 2,2 / 3,3 |
| Écho de chat (TM_CS_CHAT_REQUEST → TS_SC_CHAT_LOCAL) | 639 | 0,6 / 1,0 / 1,3 / 3,2 |
| Premier coup (TM_CS_ATTACK_REQUEST → TS_SC_ATTACK_EVENT) | 278 | 1113,7 / 5708,4 / 15014,2 / 15038,6 |

Trafic : 1212 trames/s et 43,5 Kio/s reçues par l'ensemble des bots, 34 trames/s envoyées. Marches 2861 (sans écho en 5 s : 0), chats 638, attaques 275, monstres tués 0, morts 0.

Serveur :

- CPU 0 % en moyenne, 4 % au plus (part de la machine) ; mémoire 679 Mo au plus ; 35 threads au plus
- Pool de threads : 16 threads au plus, file d'attente 36 au plus
- GC : 0,0 % du temps, 1 collectes de génération 2, allocation 3,1 Mio/s ; exceptions 0
- Verrous : 1086 attentes, 0,8 ms d'attente par seconde (106 ms au total)

| Boucle | ticks | p50 typique | p95 / p99 de la pire seconde | intervalle |
|---|---:|---:|---|---:|
| combat | 1310 | 0,03 | 3,3 / 3,3 | 100 |
| monster-ai | 437 | 0,13 | 2,5 / 2,5 | 300 |
| buffs | 262 | 0,09 | 2,5 / 2,5 | 500 |
| casts | 2620 | 0,07 | 2,5 / 2,5 | 50 |
| pets | 524 | 0,04 | 1,6 / 1,6 | 250 |
| regeneration | 44 | 0,15 | 1,6 / 1,6 | 3000 |
| monster-movement | 262 | 0,15 | 0,6 / 0,6 | 500 |
| ground-items | 131 | 0,01 | 0,2 / 0,2 | 1000 |
| creatures | 1310 | 0,01 | 0,1 / 0,1 | 100 |
| skill-effects | 2620 | 0,00 | 0,0 / 0,0 | 50 |

| Mesure (serveur, ms) | n | p50 typique | p95 / p99 de la pire seconde |
|---|---:|---:|---|
| Délai d'envoi (file → socket) | 91729 | 0,14 | 3,0 / 7,0 |
| Visibilité des objets (PNJ, monstres, props, objets) | 2860 | 0,04 | 2,3 / 2,9 |
| Visibilité entre joueurs (diffusion d'une marche) | 2860 | 0,06 | 2,2 / 2,9 |

Traitement synchrone des trames reçues, les dix plus lentes :

| Trame | n | p50 typique | p95 / p99 de la pire seconde (ms) |
|---|---:|---:|---|
| 5 | 2860 | 0,195 | 3,05 / 3,05 |
| 1 | 50 | 0,354 | 2,82 / 2,82 |
| 2001 | 100 | 0,427 | 0,99 / 0,99 |
| 2002 | 50 | 0,310 | 0,61 / 0,61 |
| 20 | 638 | 0,043 | 0,31 / 0,31 |
| 150 | 5 | 0,091 | 0,10 / 0,10 |
| 100 | 278 | 0,009 | 0,10 / 0,10 |
| 50 | 50 | 0,034 | 0,05 / 0,05 |
| 2 | 200 | 0,004 | 0,04 / 0,04 |
| 2005 | 50 | 0,021 | 0,03 / 0,03 |

Le serveur a écrit 43,3 Kio/s sur ses sockets ; 33 trames de jeu reçues par seconde.

Problèmes relevés par les bots :

- refus : requête 5 → code 6 : 196

## Goulets détectés

- **50 clients** — file du pool de threads jusqu'à 24 éléments.
- **100 clients** — file du pool de threads jusqu'à 36 éléments.
