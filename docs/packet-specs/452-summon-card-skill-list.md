# 452 — `TM_CS_SUMMON_CARD_SKILL_LIST`

Fiche d'archéologie de protocole, Epic 7.3. Écrite en lecture seule sur les références
(`reference/rzu`, `reference/ngemity`, `reference/client73`) — aucun Lua, aucun script ni
exécutable client n'a été lancé, aucune base de données n'a été démarrée. Le client 7.3 tranche
l'émission et la forme ; rzu tranche les tailles et le gating ; NGemity ne tranche rien ici
(§6) : il ne traite pas ce paquet.

Identifiant de suivi : `navislamia:packet:452`. Carte Trello : `IO6rRlut`. Branche :
`hermes/packet-452-summon-card-skill-list`.

Résumé des arbitrages :

| Question | Verdict de cette fiche |
|---|---|
| 7.3 émet-il 452 ou 1452 ? | **452** : le client 7.3 écrit l'id 452 (`SFrame.exe 0x48EE35`) et rzu ne bascule sur 1452 qu'à partir d'`EPIC_9_6_3` (§4). |
| Taille de la trame | **11 octets** : en-tête de 7 + un seul champ de 4 `item_handle` (§3). |
| Réponse serveur | **Hypothèse de travail** = `TM_SC_SKILL_LIST` (403), `14 + 14×N` octets. Aucune référence n'implémente 452 : c'est une déduction, à confirmer (§5, §7a). |
| Déclencheur client | Clic sur le bouton d'interface nommé `button_flip`, sous garde `[fenêtre+0x4C8] ≠ 0` (§2). |

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id client → serveur | **452** (`0x01C4`) | `op_codes.md:134` ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_SUMMON_CARD_SKILL_LIST.h:11-13` |
| Nom | `TM_CS_SUMMON_CARD_SKILL_LIST` | `op_codes.md:134` ; table de noms du client : littéral à `SFrame.exe 0xA5338C`, enregistré avec l'id 452 à `SFrame.exe 0x677884` |
| Id alternatif | **1452** à partir d'`EPIC_9_6_3` (non retenu en 7.3) | `TS_CS_SUMMON_CARD_SKILL_LIST.h:12-13` |
| Sens | client → serveur (`SessionPacketOrigin::Client`) | `TS_CS_SUMMON_CARD_SKILL_LIST.h:15` |
| Référence NGemity | `TS_CS_SUMMON_CARD_SKILL_LIST = 452` | `reference/ngemity/shared/Server/ClientPackets.h:144` ; `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_SUMMON_CARD_SKILL_LIST.h:10` |
| Classe de message interne du client (déclencheur) | `.?AUSIMSG_UI_SUMMON_CARD_SKILL_LIST@@`, id interne `0x4E1` (1249), vtable `0xA27390` | RTTI résolu depuis `SFrame.exe` ; littéral `strings -n 4 SFrame.exe` l. 43863 |
| État dans Navislamia | **absent** : aucun `452` ni `SUMMON_CARD` dans `GamePackets.cs` ; seul `TM_EQUIP_SUMMON = 303` existe dans la famille | `Game/Network/Packets/Enums/GamePackets.cs:72` ; `grep -rn "452\|TM_CS_SUMMON_CARD" --include=*.cs Game/` → 0 |

Le client journalise lui-même ce paquet comme une **requête** :
`'TM_CS_SUMMON_CARD_SKILL_LIST\t요청결과-%s[%d]\n'` (`SFrame.exe 0xA525E4`, imprimé par le cas
`cmp eax,0x1C4` à `SFrame.exe 0x66DE76` / `0x66DF48` ; `요청결과` = « résultat de la requête »).
C'est la confirmation, côté binaire client, que 452 appartient au lexique `TM_CS_*` et qu'il est
émis par le client — le nom n'est pas une invention de rzu.

## 2. Ce que le joueur fait pour que le client l'envoie

**Clic sur le bouton d'interface `button_flip`** de la fenêtre de carte de créature (bascule de
la carte). La chaîne complète a été relevée par désassemblage ; elle comporte quatre maillons :

1. **Le bouton et sa garde.** Le gestionnaire de commandes de la fenêtre (`SFrame.exe 0x517190`)
   compare la commande reçue au littéral `button_flip` (`strcmp` à `0x517243`, littéral
   `0xA27394`) et, dans ce cas, joue le son `ui_button_click.wav` (`0xA21B2C`, `0x517262`), poste
   des ouvertures/fermetures de fenêtres (`SIMSG_SHOW_UIWINDOW`, paramètres `0x74`/`0x75` = 116/117,
   `0x51728F` et `0x5172E7`), puis n'émet **que si** l'octet `[fenêtre+0x4C8]` est non nul
   (`cmp bl,[esi+0x4C8]` à `0x517330`, `je` de sortie à `0x517335`).
2. **La valeur transmise.** Le dword `[fenêtre+0x4C4]` (`mov eax,[esi+0x4C4]` à `0x517338`) est
   copié dans le champ prévu pour le handle de la trame à construire (`0x51735E`, objet local
   `[ebp-0x24]`, `obj+0x13` — c'est la convention de charge utile de l'objet de message, cf. §3).
3. **La fenêtre a été renseignée au préalable.** Le message interne
   `.?AUSMSG_SUMMON_CARD_ITEM_INFO@@` (id `0xB1` = 177, constructeur `SFrame.exe 0x4E9C20`,
   vtable `0xA25034`) est construit par la couche `SGameInterface` (`0x6154BF`), gardé par le nom
   d'enfant d'interface `creature_card_icon_slot_01` (`0xA3FE6C`, `strcmp` à `0x615466`), et
   rempli depuis l'enregistrement de carte `[interface+0x610 + [interface+0x540]×0x100]`
   (74 octets copiés en `obj+0x17`, `0x6154C4-0x6154ED`). Le gestionnaire de la fenêtre pour cet
   id (`SFrame.exe 0x519540`, cas `sub eax,0x49` ⇒ `0xB1` à `0x519563`) pose alors
   `[fenêtre+0x4C8] = 1` (`0x519579`) et recopie le premier dword de cet enregistrement
   (`[message+0x17]`) dans `[fenêtre+0x4C4]` via `0x5182F0` (`mov [ebx+0x4C4],eax` à `0x518382`).
   Sans ce message préalable, la garde de l'étape 1 bloque l'émission.
4. **Le répartiteur interne du client.** L'objet `SIMSG_UI_SUMMON_CARD_SKILL_LIST` est posté
   (`0x6491C0`), puis routé par le répartiteur d'index `SFrame.exe 0x49E21D-0x49E234`
   (`sub eax,0x403` ; `cmp eax,0xDE` ; table d'octets `0x49EA50` ; table de sauts `0x49E98C`).
   La clé **1249** (`0x4E1`) y tombe sur l'entrée 47 ⇒ `0x49E703` ⇒ constructeur de trame
   `0x48EE20`. C'est le **seul** appelant de ce constructeur.

Balayage des immédiats `0x1C4` alignés dans `.text` : l'id 452 n'apparaît que dans le
constructeur de trame (`0x48EE35`), dans la table de noms (`0x677884`), dans le journal de
paquet (`0x66DE76`) et dans deux constructeurs d'objet qui stockent l'id en donnée
(`0x550361`, `0x550661`). **Aucun autre chemin d'émission de 452 n'existe.**

Réserve de nommage : les littéraux `window_creature_card_front.nui` (`0x644480`) et
`window_creature_card_back.nui` (`0x644460`) existent dans `SFrame.exe` mais ne sont référencés
par aucun code du binaire (0 référence) ; la liaison « fenêtre de carte ↔ `button_flip` » repose
donc sur le nom de l'enfant, sur les deux poseurs d'infobulle de la même famille
(`button_flip` → id d'infobulle `0x1C36` à `0x516CF8`, et `0x1C35` à `0x51695C`) et sur la famille
de commandes partagée `button_close` / `button_flip` des gestionnaires voisins. Les fichiers
`.nui` ne sont pas dans les archives extraites (`reference/client73/extraction-manifest.json` :
0 occurrence de « nui ») : **la preuve directe par le fichier d'interface est impossible ici**.

## 3. Structure sur le fil

En-tête Navislamia : 7 octets — `Length` (uint32, 0), `ID` (uint16, 4), `Checksum` (octet, 6)
(`CLAUDE.md:47-49`). `Checksum` = somme des 6 premiers octets. Le constructeur du client 7.3
applique exactement cette convention, ce qui a été relevé sur ses instructions :

### 3.1 `TM_CS_SUMMON_CARD_SKILL_LIST` (452) — client → serveur — **11 octets**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0 | uint32 | `Length` | **11** (`0xB`) | `SFrame.exe 0x48EE3E` (`movl $0xB,-0xC(%ebp)`) ; rzu : 7 + 4 |
| 4 | uint16 | `ID` | **452** (`0x1C4`) | `SFrame.exe 0x48EE35` + `0x48EE3A` (écriture 16 bits dans un emplacement 32 bits remis à zéro en `0x48EE28`) ; `op_codes.md:134` |
| 6 | uint8 | `Checksum` | somme des 6 octets 0-5 | `SFrame.exe 0x48EE50-0x48EE58` (boucle `add (%eax),%dl` de `[ebp-0xC]` à `[ebp-0x6]`), écrit en `0x48EE60` |
| 7 | `ar_handle_t` (4) | `item_handle` | dword `[fenêtre+0x4C4]` du client (aucune valeur constante) | charge utile : `SFrame.exe 0x48EE66` (`mov 0x13(%edx),%eax`) + `0x48EE69` (`mov %eax,-0x5(%ebp)`) ; champ déclaré : `TS_CS_SUMMON_CARD_SKILL_LIST.h:8` ; nom du champ : `op_codes.md` + rzu |

Taille totale attendue : **11 octets** = 7 d'en-tête + 4 de charge utile. Un seul champ, aucune
chaîne, aucun tableau : ni `_(count)`, ni `_(dynstring)`, ni `_(dynarray)` dans la déclaration
rzu (`TS_CS_SUMMON_CARD_SKILL_LIST.h:7-8`), donc pas de longueur variable ni d'alignement.

Le type du champ est `ar_handle_t`, défini comme `strong_typedef<ar_handle_t, uint32_t>`
(`reference/rzu/librzu/src/lib/Packet/GameTypes.h:40`) : **4 octets**, identique au `uint32_t`
de NGemity (`shared/Server/Packets/GameClient/TS_CS_SUMMON_CARD_SKILL_LIST.h:7`). Navislamia
représente déjà ses handles en `uint32` (`Game/Network/Packets/Header.cs:6-11`).

Envoi : `SFrame.exe 0x48EE6C-0x48EE78` (session `[ecx+0xB8]`, méthode de vtable `+0xC4`).

## 4. Gating de version

| Champ / élément | Gating montré par rzu | Décision pour 7.3 |
|---|---|---|
| Id du paquet | `X(452, version < EPIC_9_6_3)` / `X(1452, version >= EPIC_9_6_3)` (`TS_CS_SUMMON_CARD_SKILL_LIST.h:12-13`) | **452**. 7.3 < 9.6.3 ; le client 7.3 écrit 452 (`SFrame.exe 0x48EE35`, enregistré dans sa table de noms à `0x677884`). 1452 n'existe pas en 7.3. |
| Existence du paquet | commentaire `// Since EPIC_7_3` (`:10`) | Le paquet **apparaît** en 7.3 (d'où son absence de la table `TM_*` antérieure) ; l'id 452 y est **natif**. |
| `item_handle` | aucun gating par champ (`:8`) | **Présent et unique**, 4 octets, `ar_handle_t`. Aucune variante de disposition en 7.3. |
| Réponse `TM_SC_SKILL_LIST` (403, hypothèse §5) | `X(403, version < EPIC_9_6_3)` / `X(1403, version >= EPIC_9_6_3)` ; champ `modification_type` gated `version >= EPIC_4_1` (`TS_SC_SKILL_LIST.h:20,23-25`) | **403** et **`modification_type` présent** (7.3 ≥ 4.1). `TS_SKILL_INFO` : 14 octets (`ar_time_t` = `uint32_t`, `GameTypes.h:44`). |
| Infobulles de `button_flip` | sans objet (client) | Ids `0x1C36` / `0x1C35` relevés mais **non résolus** (§7d). |

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` (NGemity) en fait : rien

`grep -rn "SUMMON_CARD_SKILL_LIST" reference/ngemity/` ne renvoie que **deux** lignes de
déclaration : l'énumération `shared/Server/ClientPackets.h:144` et l'`#include` de
`shared/Server/XPacket.h:162`. Zéro occurrence dans `Chihiro/`. NGemity **déclare** ce paquet et
**ne le traite pas** : aucun gestionnaire, aucune réponse, aucune réserve à porter. La logique
attendue ne peut donc pas venir de là.

### 5.2 Le geste analogue, lui, existe dans NGemity (à titre d'hypothèse)

Le seul endroit où NGemity associe une carte d'invocation à une liste de compétences est
l'entrée d'une carte dans l'inventaire :

- `Player::onAdd` : `if (pItem->IsSummonCard() && pItem->m_pSummon != nullptr) { AddSummon(...); Messages::SendSkillList(this, pItem->m_pSummon, -1); }`
  (`reference/ngemity/Chihiro/src/Entities/Player/Player.cpp:1152-1157`) ;
- `Messages::SendSkillList(Player*, Unit*, int32)` (`Chihiro/src/Network/Messages.cpp:172-204`)
  envoie `TS_SC_SKILL_LIST` avec `target = pUnit->GetHandle()` — donc le **handle de l'invocation**,
  pas celui de la carte — `modification_type = 0`, et une entrée `TS_SKILL_INFO` par compétence
  dont l'`m_nSkillUID` est ≥ 0 (appel `-1` = liste complète).

Autres appelants, pour situer la sémantique : `Player.cpp:780` (entrée dans le monde),
`Player.cpp:1421` et `Summon.cpp:371` (une compétence à la fois, après un incrément de niveau),
`Skill.cpp:849`.

### 5.3 Ce que le client 7.3 fait de la réponse (403)

Le traitement de `TM_SC_SKILL_LIST` est dans la couche `SGameInterface` : le désassembleur
retrouve les traces de journal `SGameInterface - MSG_SKILL_LIST 01` à `05` puis `End` aux
adresses `SFrame.exe 0x63B6A2`, `0x63B6B9`, `0x63B759`, `0x63B766` (et voisines), dans une
fonction qui lit la charge utile aux deux premiers offsets de la convention d'objet
(`mov 0x13(%esi)` en `0x63B606`, `mov 0x17(%esi)` en `0x63B688`) et les passe à des
résolutions unitaires (`0x4C5730`, `0x48ACB0`). Autrement dit : le premier champ de la charge
utile (offset 7 de la trame) est traité comme un **handle d'unité**, et le paquet est découpé en
plusieurs passes (`01`…`05`) — cohérent avec une liste paginée, non démontré plus loin.

### 5.4 Ce que le serveur Navislamia doit répondre

Aucune référence n'implémente la réponse à 452 ; ce qui suit est une **déduction**, pas une
preuve (§7a).

| Élément | Valeur proposée | Source |
|---|---|---|
| Id | `TM_SC_SKILL_LIST` = **403** | hypothèse ; `GamePackets.cs:81` ; `reference/rzu/librzu/src/packets/GameClient/TS_SC_SKILL_LIST.h:24` |
| Taille | **`14 + 14×N` octets** (en-tête 7 + `target` 4 + `count` 2 + `modification_type` 1 + N×14) | `TS_SC_SKILL_LIST.h:17-21` ; `TS_SKILL_INFO` = 4 + 1 + 1 + 4 + 4 (`:7-12`, `GameTypes.h:44`) |
| Contenu | `target` = handle de l'invocation liée à la carte, `count` = nombre de compétences, `modification_type` = 0 (reset), une entrée `(skill_id int32, base_skill_level int8, current_skill_level int8, total_cool_time uint32, remain_cool_time uint32)` par compétence | déduction depuis `Messages.cpp:172-204` ; disposition confirmée par le constructeur Navislamia existant |
| Constructeur disponible | `GameCharacterPackets.BuildSkillList(handle, IReadOnlyCollection<SkillListEntry>)` | `Game/Network/Packets/Game/GameCharacterPackets.cs:276-300` (`fixedPayloadSize = 7`, `skillRecordSize = 14`) |

Le serveur peut donc **déjà** émettre la trame hypothétique : le seul travail de la fiche est de
lier la carte reçue à une invocation (§7c) et de décider s'il faut répondre.

### 5.5 Réserve d'implémentation, non contournable par le dev

Lier `item_handle` (carte reçue) → invocation est le point dur. La fiche
`docs/packet-specs/socle-invocations.md` §7 relève que le champ `SummonSlotItemIds`
(`CharacterEntity.cs:63`) n'est **alimenté nulle part** et que `MainSummonId` / `SubSummonId`
ne sont jamais renseignés. Tant que ce point n'est pas résolu, le serveur ne peut pas choisir le
`target` de la 403 : **ne pas inventer** de table carte → invocation, et ne pas répondre avec
l'`item_handle` reçu sans preuve (§7c).

## 6. Écarts assumés avec NGemity, et pourquoi

1. **Le gating de version.** NGemity compile en `EPIC_4_1_1` (`reference/ngemity/shared/Common/Define.h:25`).
   Son `CREATE_PACKET(TS_CS_SUMMON_CARD_SKILL_LIST, 452)` est donc **inconditionnel** et ne porte
   aucune trace du basculement 452 → 1452 d'`EPIC_9_6_3` : NGemity est ici **muet** sur la version,
   et ne peut pas servir d'arbitre. Nous suivons rzu (§4) et le client 7.3.
2. **Le traitement.** NGemity n'a pas de gestionnaire ; nous déduisons la réponse du client et de
   son propre geste analogue (`Player::onAdd` → `SendSkillList(..., pSummon, -1)`), ce que NGemity
   fait *ailleurs*. Cet écart est un **choix de déduction**, signalé comme tel.
3. **`modification_type`.** rzu et NGemity le gatent tous deux sur `version >= EPIC_4_1`
   (`TS_SC_SKILL_LIST.h:20`, NGemity `TS_SC_SKILL_LIST.h:18`) : présent en 7.3 dans les deux cas,
   aucun écart.
4. **Aucun écart sur la taille.** rzu (`ar_handle_t` = `uint32_t`) et NGemity (`uint32_t`)
   déclarent le même champ de 4 octets : la trame cliente fait 11 octets dans les deux références,
   et c'est ce que le constructeur du client 7.3 écrit.

## 7. `NON ÉTABLI`

### (a) La réponse à 452 est-elle `TM_SC_SKILL_LIST` (403) ?

**Non établi.** Aucune référence n'implémente 452 : NGemity le déclare sans le traiter, rzu ne
fournit que la structure côté client, et le client 7.3 n'a pas de table « requête → réponse »
lisible ici. Les indices qui soutiennent l'hypothèse : le nom même du paquet
(`SUMMON_CARD_SKILL_LIST`), la classe de message interne
`SIMSG_UI_SUMMON_CARD_SKILL_LIST` qui déclenche l'émission, et le geste analogue de NGemity qui
envoie une 403 dès qu'une carte d'invocation entre en inventaire. Ce qu'il faut pour trancher :
une capture d'un client 7.3 réel cliquant sur `button_flip` avec une carte présente.

### (b) Que contient exactement le dword envoyé (`item_handle`) ?

**Non établi.** La preuve de ce que le client envoie s'arrête au dword `[fenêtre+0x4C4]` :
il est recopié du premier dword de l'enregistrement de carte interne
(`[interface+0x610 + [interface+0x540]×0x100]`, `SFrame.exe 0x6154C4-0x6154ED` → `obj+0x17`)
par le gestionnaire du message `SMSG_SUMMON_CARD_ITEM_INFO` (`0x519540` → `0x5182F0` →
`0x518382`). rzu nomme le champ `item_handle`, mais **rien dans le binaire ne prouve que le
premier dword de cet enregistrement est le handle d'objet de la carte** plutôt qu'un index, un
identifiant de ressource ou un handle d'invocation. Le serveur doit donc journaliser les valeurs
reçues avant de bâtir une résolution dessus.

### (c) Le `target` de la 403 doit-il être le handle d'invocation, ou le handle de carte reçu ?

**Non établi.** NGemity envoie le handle de l'invocation (`Messages.cpp:173-174` appelé avec
`pItem->m_pSummon`, `Player.cpp:1156`) ; le client 7.3 lit le premier champ de la 403 comme un
handle d'unité (`0x63B606`) mais on ne peut pas démontrer par lecture seule qu'il **refuse** un
handle de carte. À vérifier en jeu, en même temps que (a).

### (d) Quelle fenêtre porte `button_flip`, et que disent ses infobulles ?

**Non établi.** Les littéraux `.nui` ne sont référencés par aucun code (0 référence) et les
fichiers d'interface ne sont pas dans les archives extraites (`extraction-manifest.json` : 0
occurrence de « nui »). Les ids d'infobulle `0x1C36` (recto) et `0x1C35` (verso) sont posés par
`SFrame.exe 0x516CF8` / `0x51695C` mais ne sont pas résolus : les identifiants de chaîne du client
ne sont pas indexables dans les `.rdb` extraits (les clés de `db_string.rdb` sont des noms, pas
des entiers). **Aucun texte d'infobulle n'est cité dans cette fiche** — ne pas en inventer un.

### (e) `modification_type` : 0 ou 1 ?

**Non établi pour 452.** NGemity envoie 0 (« reset »), rzu documente seulement
« if true then refresh » (`TS_SC_SKILL_LIST.h:30`). Le geste client (`button_flip` = bascule) peut
aussi bien demander un rafraîchissement qu'une remise à zéro. À trancher au même moment que (a).

### (f) L'entier journalisé par le client pour 452

Le cas de journal `SFrame.exe 0x66DF48` imprime un `uint16` lu à `[objet+0x9]` avec la chaîne
`요청결과-%s[%d]`. La nature de ce champ (numéro de séquence ? code de résultat attendu ?) **n'est
pas établie** ; elle n'a aucun effet sur la trame de 11 octets ci-dessus.

## 8. Commits et binaires épinglés

| Référence | Commit / empreinte |
|---|---|
| rzu (`reference/rzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (HEAD) |
| rzu — `TS_CS_SUMMON_CARD_SKILL_LIST.h` | sha256 `c88ca9fd8964856d44f74500ab0c3253848229b269dd3381adf5f1d6ee96aff4` |
| rzu — création du paquet (historique suivi du fichier) | `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07) |
| rzu — mention `// Since EPIC_7_3` (pdbs 5.2 → 8.1) | `bdd362a600d104fd676facf12d6c6beb85a23ec4` (2017-02-26, « Update packets based on available GS pdbs (5.2, 6.1, 6.2, 7.1, 7.2, 7.3, 7.4, 8.1) ») |
| rzu — bascule d'id 452 → 1452 à `EPIC_9_6_3` | `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11, « packets: use versionned ID for all packets and update their ID with epic 9.6.3 ») |
| rzu — `ar_handle_t` (strong typedef, 4 octets) | `05bc2d82dac16003bc06162584e8559826310bff` (2020-03-29) |
| NGemity (`reference/ngemity`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (HEAD) |
| NGemity — `TS_CS_SUMMON_CARD_SKILL_LIST.h` | sha256 `6ea6100b35f37484daae679ce02581b3b67d4e453ab4732ad012c068c5b08476` |
| NGemity — plus ancien commit de l'historique suivi du fichier | `90a500d46acc39d21ef943a047a37d0a641f7af4` (2018-08-24, « WIP: add support for serializable packets ») |
| NGemity — version de compilation | `EPIC_4_1_1` (`shared/Common/Define.h:25`) |
| Navislamia — base de la branche | `b6f24ddf2bea8402573f87e69f1f7b5d1fa09e43` (`master` = `origin/master`) |
| Client 7.3 — `SFrame.exe` | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 o.) |
| Client 7.3 — `db_string.rdb` | sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` |
| Client 7.3 — `extraction-manifest.json` | `archive_entries: 83822`, `source_index_sha256 b88ac39a0976f49746671d413ba68af616af37e6e26095c3a109d5884510adbf` |

Convention de citation client : `SFrame.exe 0x…` désigne une **adresse virtuelle** du binaire
épinglé ci-dessus (reproductible par `objdump -d reference/client73/SFrame.exe
--start-address=0x… --stop-address=0x…`) ; les littéraux sont donnés par leur adresse et, quand
utile, par leur numéro de ligne du dump `strings -n 4 SFrame.exe` (l. 21015 `button_flip`,
l. 24785 `window_creature_card_front.nui`, l. 26343 `TM_CS_SUMMON_CARD_SKILL_LIST`, l. 43863
`.?AUSIMSG_UI_SUMMON_CARD_SKILL_LIST@@`). Cette fiche ne contient aucune valeur devinée : les
six questions ouvertes de §7 sont les seules zones non tranchées, et chacune dit précisément ce
qui manque.

## 9. Implémentation livrée (dev)

Section ajoutée par `navis-dev` le 23/09/2026 ; l'analyse de l'archéologue (§1 à §8) est laissée
intacte. Commits de code : `a3afcef` (4 fichiers, +307 lignes) et `f290595` (test de chaîne de
dispatch), sur la branche `hermes/packet-452-summon-card-skill-list` créée par `navis-ref` depuis
`master` (`b6f24dd`).

**Règle tenue par le code : lire, borner, journaliser `item_handle`, ne rien répondre, ne rien
résoudre.** Aucune table carte → invocation n'est introduite et le dword reçu n'est jamais réémis
comme `target` (§5.5, §7b, §7c).

### 9.1 Checklist des critères transversaux, avec les codes de sortie relevés

| # | Critère | État | Mesure |
|---|---|---|---|
| 1 | `dotnet build Navislamia.sln -c Debug` code 0 | **OK** | code de sortie **0**, `0 Error(s)`, `162 Warning(s)` (toutes préexistantes, `MigrateDatabase` et nullabilité) |
| 2 | `dotnet test Tests/Tests.csproj` code 0, compte jamais en baisse | **OK** | compte **avant** le lot, relevé par exclusion de la nouvelle fixture : code 0, **1099** réussis / 1099. Après : code **0**, **1117** réussis / 1117, 0 échec, 0 ignoré → **+18** |
| 3 | Au moins un test d'offsets (taille totale + position de chaque champ) | **OK** | `Tests/Game/SummonCardSkillListPacketsTests.cs` : `ClientPacket_UsesTheEpic73Layout` (11 octets, `Length` en 0, `ID` en 4, checksum en 6, `item_handle` en 7), `ClientPacket_HasNoFieldOutsideTheHeaderAndItemHandle`, `TryReadSummonCardSkillList_ReadsItemHandleAtOffsetSeven`, `…_ReadsTheValueLittleEndian` |
| 4 | Enum et dispatch modifiés ensemble | **OK** | membre `TM_CS_SUMMON_CARD_SKILL_LIST = 452` (`GamePackets.cs:92`) **et** bras en `GameClient.cs:1380`, avant le `switch` final dont le `_` lève `Unknown Packet Type` (`GameClient.cs:1628`) ; mesuré par `Packet_IsDispatchedBeforeTheUnknownPacketThrow` (balayage du source) et par `OnDataReceived_ConsumesThePacketWithoutThrowing` (exécution) |
| 5 | Savoir durable dans la fiche commitée + bloc `CLAUDE.md` dans la description de la MR | **OK côté fiche** | présente section + §10, remis à `navis-qa` pour la description de la MR (le dev n'écrit pas `CLAUDE.md`) |
| 6 | Version tranchée | **OK** | 452 déclaré, **1452 non déclaré** ; `Ids_AreTheEpic73Ones` vérifie que 1452 n'est pas un membre de `GamePackets`. Aucun champ gated : `item_handle` n'a aucun gating par champ chez rzu |
| 7 | Aucun commit sur `master` locale | **OK** | `git log --oneline origin/master..master` → aucune ligne (§9.6) |
| 8 | Aucun champ `NON ÉTABLI` deviné | **OK** | `item_handle` est lu et journalisé, **jamais interprété** ; aucune réponse n'est émise, donc ni `target` (§7c) ni `modification_type` (§7e) n'ont eu à être choisis ; les six questions de §7 restent dans `## A VERIFIER PAR KILLIAN` |

### 9.2 Fichiers livrés

| Fichier | Modification |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_SUMMON_CARD_SKILL_LIST = 452` inséré après `TM_CS_JOB_LEVEL_UP = 410`, avec le rappel du gating (1452 à ne pas déclarer) et du non-traitement |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `TryReadSummonCardSkillList(ReadOnlySpan<byte>, out uint)` |
| `Game/Network/Clients/GameClient.cs` | `HandleSummonCardSkillList(byte[])` et son bras de dispatch avant le `switch` final |
| `Tests/Game/SummonCardSkillListPacketsTests.cs` | 18 tests (nouveau) |

Aucun constructeur de trame descendante n'est ajouté : la seule réponse envisageable (403) n'est
pas établie (§7a) et le constructeur nécessaire existe déjà —
`GameCharacterPackets.BuildSkillList(handle, IReadOnlyCollection<SkillListEntry>)`
(`GameCharacterPackets.cs:276-300`), donc une réponse ultérieure n'aura pas de trame à écrire, seulement
une résolution carte → invocation à trancher.

### 9.3 Offsets livrés, et les tests qui les tiennent

| Offset | Taille | Champ | Valeur livrée | Test |
|---|---|---|---|---|
| 0 | 4 | `Length` `uint32` LE | **11** (`0xB`) | `ClientPacket_UsesTheEpic73Layout` |
| 4 | 2 | `ID` `uint16` LE | **452** (`0x1C4`) | `ClientPacket_UsesTheEpic73Layout`, `Ids_AreTheEpic73Ones` |
| 6 | 1 | `Checksum` | somme des octets 0-5, **charge exclue** | `ClientPacket_UsesTheEpic73Layout`, `ClientPacket_ChecksumIgnoresThePayload` |
| 7 | 4 | `item_handle` (`ar_handle_t`) `uint32` LE | lu et journalisé, **jamais résolu** | `TryReadSummonCardSkillList_ReadsItemHandleAtOffsetSeven`, `…ReadsTheValueLittleEndian`, `…AcceptsAZeroHandleWithoutInventingOne` |

`TryReadSummonCardSkillList` **refuse toute longueur autre que 11** (`packet.Length != HeaderSize + 4`),
sur le modèle de `TryReadCheckIllegalUser` et de `TryReadGetRegionInfo` : le constructeur client écrit
11 en dur (`SFrame.exe 0x48EE3E`) et l'en-tête est de taille fixe, donc 7, 10 et 12 sont des anomalies.
Les cas sont testés (`TryReadSummonCardSkillList_RejectsAnyLengthOtherThanEleven`, quatre cas) **et**
exercés à travers la vraie boucle de réception (`OnDataReceived_ConsumesAMalformedFrameWithoutThrowing`,
longueurs 7 et 15). Une trame refusée est consommée en totalité et ne désynchronise pas la suivante
(`OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne`).

### 9.4 Réponses émises : aucune

Trois tests le tiennent par **exécution**, pas par relecture : `OnDataReceived_AnswersNothing` (rien
dans `Connection.Sent`), `OnDataReceived_ConsumesThePacketWithoutThrowing` et
`OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne` (`Sent` vide, tout le flux consommé).
Aucun `Connection.Send` n'est atteint par le paquet 452 : ni la 403 hypothétique, ni un `TS_SC_RESULT`
de refus. **Effet en jeu à attendre : le clic sur `button_flip` produit une ligne de journal `Debug`
avec la valeur de `item_handle`, et la liste de compétences de la carte reste vide côté client** —
c'est le comportement voulu tant que §7a/§7c ne sont pas tranchés, pas une régression à corriger.

Le refus par `TS_SC_RESULT` a été écarté : aucun code de résultat n'est établi pour 452 (ni rzu, ni
NGemity, ni `op_codes.md`), et `NotEnoughSummonCard = 93` (`ResultCode.cs:112`) décrit une autre
situation (pas de carte en stock), donc l'émettre afficherait une boîte de message inventée. Si Killian
préfère une trace visible en jeu, c'est un ajout d'une ligne, à trancher dans
`## A VERIFIER PAR KILLIAN`.

### 9.5 Réserves du dev

1. **Le journal est la seule raison d'être du bras.** `item_handle` est écrit en `Debug`
   (`GameClient.cs:419-421`) précisément parce que §7b demande de relever la valeur réelle avant toute
   résolution. Si ce niveau est trop bavard en production, il peut descendre en `Verbose` sans rien
   casser — mais la donnée qui manque à §7b devient alors invisible.
2. **Le nom `item_handle` est celui de rzu**, pas une conclusion : rien n'établit que le premier dword
   de l'enregistrement de carte soit un handle d'objet (§7b). Le code s'interdit de s'en servir.
3. **La contrainte de résolution reste entière** (§5.5) : `SummonSlotItemIds` (`CharacterEntity.cs:63`)
   n'est alimenté nulle part et `MainSummonId` / `SubSummonId` ne sont jamais renseignés. Tant que ce
   point est ouvert, aucune 403 ne peut être construite sans inventer une table.
4. **La preuve d'émission côté client est un désassemblage**, pas une capture : les six questions de §7
   restent les seules zones non tranchées, et la vérification en jeu (clic sur `button_flip` avec une
   carte présente) est la seule qui les ferme.

### 9.6 Commandes relevées

```
dotnet build Navislamia.sln -c Debug       → code 0, 0 Error(s), 162 Warning(s) (préexistantes)
dotnet test Tests/Tests.csproj             → code 0, 1117 réussis / 1117, 0 échec, 0 ignoré
                                             (base avant le lot : code 0, 1099 / 1099)
git log --oneline origin/master..master    → aucune ligne
```

## 10. Bloc prêt à coller dans `CLAUDE.md`

À insérer dans la section `## Paquets`, à la suite des blocs 57 / 59 / 60 (client → serveur, lus sans
réponse) est un emplacement cohérent ; l'ordre des sous-sections y est chronologique, pas numérique.

```markdown
### Paquet 452 — `TM_CS_SUMMON_CARD_SKILL_LIST` (client → serveur)

- Trame cliente de **11** octets : en-tête 7 + `item_handle` (`ar_handle_t` = `uint32`) à l'offset 7.
  Taille fixe, aucun rembourrage, un seul champ : toute autre longueur est refusée avant lecture.
- Déclencheur : clic sur le bouton `button_flip` de la fenêtre de carte de créature, sous la garde
  `[fenêtre+0x4C8] ≠ 0` que pose au préalable le message interne `SMSG_SUMMON_CARD_ITEM_INFO`.
- **Aucune réponse émise.** Aucune référence n'implémente 452 (NGemity le déclare sans gestionnaire,
  rzu ne fournit que le côté client). La seule réponse déductible, `TM_SC_SKILL_LIST` (403), exigerait
  la résolution carte → invocation : `SummonSlotItemIds` n'est alimenté nulle part et
  `MainSummonId` / `SubSummonId` ne sont jamais renseignés. **Ne pas inventer de table carte →
  invocation, et ne pas réémettre le `item_handle` reçu comme `target`.**
- Le serveur **lit, borne et journalise** `item_handle` (niveau `Debug`) : la valeur que le client
  met dans ce champ n'est pas établie, et ce journal est ce qui permettra de la relever un jour.
- Gating : 452 à l'Epic 7.3, `1452` seulement à partir d'`EPIC_9_6_3` — **ne pas déclarer 1452**.
  `modification_type` de la 403 est présent en 7.3 (gated `>= EPIC_4_1`), mais cette réponse n'est pas
  émise.
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.
```

## A VERIFIER PAR KILLIAN

Points 1 à 6 repris de l'archéologue (§7, §5.5), 7 à 9 ajoutés par le dev au vu du code livré (§9).

1. **La réponse à 452 est-elle `TM_SC_SKILL_LIST` (403) ?** (§7a) **Non établi.** Aucune référence
   n'implémente 452. Ce qu'il faut pour trancher : une capture d'un client 7.3 réel cliquant sur
   `button_flip` avec une carte présente. Tant que ce n'est pas fait, le serveur ne répond rien
   (§9.4) et la fenêtre de carte reste sans liste de compétences.
2. **Que contient exactement le dword envoyé (`item_handle`) ?** (§7b) **Non établi.** La preuve
   s'arrête au dword `[fenêtre+0x4C4]`. Le serveur journalise donc la valeur reçue
   (`item_handle=` en `Debug`, `GameClient.cs:419-421`) — c'est le relevé à comparer avec une capture
   ou avec le contenu de l'inventaire.
3. **Le `target` de la 403 doit-il être le handle d'invocation, ou le handle de carte reçu ?** (§7c)
   **Non établi.** NGemity envoie le handle de l'invocation ; on ne peut pas démontrer par lecture
   seule que le client 7.3 refuse un handle de carte. Le code ne prend aucun des deux partis : il
   n'émet rien.
4. **Quelle fenêtre porte `button_flip`, et que disent ses infobulles ?** (§7d) **Non établi.** Les
   fichiers `.nui` ne sont pas dans les archives extraites. **Aucun texte d'infobulle n'est cité ni
   inventé** ; sans effet sur le code livré.
5. **`modification_type` : 0 ou 1 ?** (§7e) **Non établi pour 452.** Question sans objet tant que la
   403 n'est pas émise ; à trancher en même temps que le point 1.
6. **L'entier journalisé par le client pour 452** (§7f) : nature non établie, sans effet sur la trame
   de 11 octets.
7. **Liaison carte → invocation** (§5.5) : `SummonSlotItemIds` (`CharacterEntity.cs:63`) n'est
   alimenté nulle part, `MainSummonId` / `SubSummonId` ne sont jamais renseignés. C'est le point dur
   qui bloque toute réponse ; il dépasse le paquet 452 et touche le socle des invocations.
8. **Faut-il refuser en jeu plutôt que ne rien répondre ?** (§9.4) Le dev n'émet volontairement aucun
   `TS_SC_RESULT` : aucun code de résultat n'est établi pour 452 et `NotEnoughSummonCard = 93` décrit
   une autre situation. Si Killian préfère une trace visible côté client, c'est un ajout d'une ligne.
9. **Niveau de journal** (§9.5.1) : `Debug` retenu pour rendre `item_handle` observable comme le
   demande §7b. À desserrer en `Verbose` si c'est trop bavard en production.
