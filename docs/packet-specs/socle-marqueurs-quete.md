# Marqueurs de quête au-dessus des PNJ (« ! », « ? »)

| | |
|---|---|
| Code | `Game/Services/QuestMarkRules.cs`, `QuestService.RefreshNpcMarksAsync`, `ConnectionInfo.NpcQuestMarks`, `ActorStatus.ForNpc`, `CreatureStatus.Npc*` |
| Trames | `TS_SC_ENTER` (3, statut @26), `TS_SC_STATUS_CHANGE` (500 : `handle` @7, `status` @11, 15 octets) — aucune nouvelle |
| Tests | `Tests/Game/QuestMarkRulesTests.cs`, `QuestLifecycleTests.The_mark_over_the_npc_follows_the_quest_from_offer_to_hand_in` |

## 1. Le constat

Bug noté le 2026-10-06 : aucun « ? » ni « ! » au-dessus des PNJ. Aucun paquet de marqueur n'existe : le marqueur est
**le statut de créature du PNJ, tel que ce joueur le voit**. `ActorStatus.ForNpc()` valait 0 pour tout le monde.

## 2. Sur le fil

rzu `TS_SC_STATUS_CHANGE.h`, section « Npc » de `TS_CREATURE_STATUS` ; serveur officiel `GameMessage.h:820`
(`TS_ENTER::NPCInfo`) :

| bit | rzu | officiel | sens |
|---|---|---|---|
| `1 << 8` | `TCS_FlagHasStartableQuest` | `FLAG_HAS_STARTABLE_QUEST` | une quête à prendre : « ! » |
| `1 << 9` | `TCS_FlagHasInProgressQuest` | `FLAG_HAS_IN_PROGRESS_QUEST` | une quête en cours qu'il suit |
| `1 << 10` | `TCS_FlagHasFinishableQuest` | `FLAG_HAS_FINISHABLE_QUEST` | une quête à rendre : « ? » |

Le même bit 8 vaut « mort » pour un monstre et « assis » pour un joueur : le sens dépend du type d'acteur.

## 3. La règle officielle

`GetStatusCode` (`GameMessage.cpp:1843-1856`) pose **un seul** bit, le premier qui tient : à rendre
(`StructNPC::HasFinishableQuest`), sinon à prendre (`HasStartableQuest`), sinon en cours (`HasInProgressQuest`)
(`StructNPC.cpp:127-180`) :

- **à rendre** : un lien `flag_end` dont la quête est terminable ;
- **à prendre** : un lien `flag_start` dont la quête est démarrable (`StructPlayer::IsStartableQuest` : horaires, niveaux,
  niveau de métier, profondeur, métier ou classe, race, faveur, puis le gestionnaire de quêtes). **Les contrats aléatoires
  (901, 902)** ne comptent que si aucun contrat aléatoire de ce PNJ n'est en cours ou terminable ;
- **en cours** : un lien `flag_progress` dont la quête est active et non terminable — une quête échouée compte
  (`IsInProgressQuest` : « la quête échouée est rendue comme en cours »).

`SendNPCStatusInVisibleRange` (`SendMessage.cpp:3735`) renvoie une 500 à ce joueur pour chaque PNJ de sa vue, avec le
statut qu'il voit. Appels officiels : un niveau gagné (`onExpChange`, « une nouvelle quête cachée par le niveau peut
apparaître »), le début (`onStartQuest`), la fin (`onEndQuest`) et l'abandon (`onDropQuest`) d'une quête, une quête qui
devient terminable (`onStatusChanged`) ou qui échoue (`onProgressChanged`), l'expiration, la remise à zéro d'une quête
finie (`ResetFinishedQuest`).

## 4. Ce qui est porté

- `QuestMarkRules.Mark` : la règle du §3, pure. Le caractère démarrable vient de `QuestService.CanStart`, le même juge
  que les offres du dialogue (`GetNpcOffersAsync`) : un « ! » annonce toujours une offre réelle, et une quête d'un type
  non pris en charge (`QuestRules.Supported`) n'en montre pas.
- `QuestService.RefreshNpcMarksAsync` lit en une fois les quêtes actives, l'historique et les acceptations du personnage,
  calcule le marqueur de **chaque PNJ lié** (`QuestLinkResources`) et le range dans `ConnectionInfo.NpcQuestMarks` ; une
  500 part pour chaque PNJ en vue dont le marqueur a changé. Un seul calcul à la fois par joueur ; une demande pendant
  un calcul le relance une fois.
- L'`ENTER` d'un PNJ (`NpcSpawnService.Sync`) porte le marqueur gardé : un PNJ qui entre plus tard dans la vue l'a tout
  de suite, sans requête.
- Déclencheurs : la liste des quêtes (entrée en jeu, début, abandon, remise : chacun la renvoie), un état de quête qui
  n'est plus « en cours » (terminable, échouée ou expirée, `SendStatus`) et tout changement de niveau
  (`ILevelingService.LevelChanged`, montée comme perte à la mort).

## 5. Écarts et NON ÉTABLI

- `CanStart` refuse aussi au-delà de 20 quêtes actives : un joueur au plafond ne voit plus de « ! ». L'officiel teste ce
  plafond au démarrage seulement (`StructPlayer.cpp:8794`) ; pas de « ! » pour une quête qu'on ne peut pas prendre est le
  choix retenu.
- Un niveau de métier gagné ou un changement de métier ne rafraîchit pas les marqueurs, comme chez l'officiel ; ils
  suivent au prochain déclencheur.
- Le rendu en jeu (icônes « ! » / « ? » du client 7.3 sur ces bits) n'a pas encore été vérifié en jeu.
