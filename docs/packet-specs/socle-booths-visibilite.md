# Socle — étal de joueur : visibilité (`702` / `703` / `704`)

| Élément | Valeur |
|---|---|
| Suivi | `navislamia:socle:booths-visibilite` |
| Branche | `hermes/packet-socle-booths-visibilite` |
| Base | `master` = `b56967a07430422add88e0e5cdf292b41b18f6c6` |
| Version tranchée | Epic 7.3 (`EPIC_7_3` = `0x070300`) |
| Nature | fiche de cadrage (format + gating + traitement attendu) — aucun code serveur |
| Prédécesseur | `docs/packet-specs/socle-booths.md` (700/701, implémentés) |

Le socle précédent a rendu l'étal d'un joueur **créable** (`700`) et **fermable** (`701`) mais invisible :
`ConnectionInfo.Booth` n'est lu par personne, et aucun paquet de la famille ne sort du serveur. Cette
fiche cadre la visibilité : `TM_CS_WATCH_BOOTH` (`702`), la réponse `TM_SC_WATCH_BOOTH` (`703`) et
`TM_CS_STOP_WATCH_BOOTH` (`704`).

Elle tranche trois choses que le socle précédent avait laissées ouvertes et qui bloquaient la branche
« visibilité » : le **contenu** de `703` (objets déclarés ou objets résolus), le **motif d'objet** de
`703` (75 octets, contre le gating `>= EPIC_7_4` de rzu), et la **liste du verrou d'actions** (le
`smsg_booth_not_use_store` du client se rapporte précisément à `702`).

---

## 1. Identité

`op_codes.md` (table Lua livrée avec le dépôt) donne les trois identifiants aux lignes 171-173.

| id | nom | sens | `op_codes.md` | rzu | NGemity |
|---|---|---|---|---|---|
| 702 | `TM_CS_WATCH_BOOTH` | client → serveur | :171 | `TS_CS_WATCH_BOOTH.h:8,11-12` | `TS_CS_WATCH_BOOTH.h:7,9` |
| 703 | `TM_SC_WATCH_BOOTH` | serveur → client | :172 | `TS_SC_WATCH_BOOTH.h:18-26` | `TS_SC_WATCH_BOOTH.h:14-20` |
| 704 | `TM_CS_STOP_WATCH_BOOTH` | client → serveur | :173 | `TS_CS_STOP_WATCH_BOOTH.h:8,11-12` | `TS_CS_STOP_WATCH_BOOTH.h:7,9` |

Le reste de la famille reste hors de ce lot : `705` `TM_CS_BUY_FROM_BOOTH`, `706`
`TM_CS_SELL_TO_BOOTH`, `707`/`708` (noms d'étal), `709` `TM_SC_BOOTH_CLOSED`, `710`
`TM_SC_BOOTH_TRADE_INFO` (`op_codes.md:174-179`). `711` `TM_CS_CHECK_BOOTH_STARTABLE`
(`op_codes.md:180`) n'existe pas dans le client de 7.3 : absent du tableau `id → nom` du binaire (§8.2)
et hors de la plage `505…710` de sa table de dispatch entrant (§2.4) — le socle précédent l'avait déjà
constaté, cette fiche le reconfirme et ne le déclare pas.

**État du dépôt.** `GamePackets` ne connaît que `700` et `701` (`Game/Network/Packets/Enums/GamePackets.cs:124-128`) ;
`TM_CS_WATCH_BOOTH`, `TM_SC_WATCH_BOOTH` et `TM_CS_STOP_WATCH_BOOTH` n'y figurent pas et aucun bras de
dispatch ne les traite (grep `WATCH_BOOTH` sur `Game/` : zéro occurrence avant cette fiche).
`op_codes.md` n'est pas modifié par cette fiche.

---

## 2. Ce que le joueur fait — et ce que le client s'autorise

Toutes les mesures de cette section sont faites sur le client de référence
(`reference/client73/SFrame.exe`, `sha256 41e0af2e…`, §8).

### 2.1 La chaîne complète, du clic au `702`

Le socle précédent concluait que le client de 7.3 n'a aucun moyen d'émettre `702` (§5.4 du prédécesseur).
**Cette mesure est fausse** : le constructeur existe et il est atteignable depuis l'interface.

1. **Le contrôle.** Un gestionnaire d'événement d'interface (fonction `VA 0x504A50`, `ret 0x10`) compare
   l'identifiant d'événement à `0xA` (`0x504A71: cmp eax,0xa`) puis le nom du contrôle à la chaîne
   `_booth_name_back` (`0x504A7D: push 0xa2621C`, comparaison `0x504A83`). C'est le panneau de nom de
   l'étal observé.
2. **Cinq gardes avant d'émettre.** Le gestionnaire interroge le gestionnaire de fenêtres
   (`0x504AB0`, `0x504AC5`, `0x504ADA`, `0x504AEF` : `mov ecx,[esi+0x440]` puis `call 0x648F30`, qui
   cherche l'entrée dans la table du gestionnaire — `[obj+0x28]` — et rend l'octet `[entrée+0xC]`) avec
   les identifiants `0x1F`, `0x2D`, `0x38`, `0x45` : si l'un rend non nul, rien n'est émis. Un cinquième
   test précède (`0x504AA4` : `cmp [esi+0x5E8],eax` où `eax = [objet+0x1E4]`) et écarte lui aussi
   l'émission quand les deux valeurs coïncident — la nature de `[+0x1E4]` n'est pas identifiée (§7.4).
3. **Le message interne.** Le constructeur `VA 0x5044C0` (`ret 8`, deux arguments) bâtit un objet
   `SMSG_WATCH_BOOTH` (`0x504B10`, avec `push 1` / `push [esi+0x5e8]` = handle de l'étal et 1) :

   | offset | écrit à | valeur |
   |---|---|---|
   | +0 | `0x5044E5` | `vptr` `0xA260D4` |
   | +4 | `0x5044D7` | `0x43` = **67** (identifiant interne du message) |
   | +8 | `0x5044CA` | `uint16` = 0 |
   | +0xA | `0x5044CE` | `uint8` = 0 |
   | +0xB | `0x5044DE` | `0x13` = 19 |
   | +0xF | `0x5044D1` | 0 |
   | +0x13 | `0x5044EB` | `uint32` = **handle visé** (argument 1) |
   | +0x17 | `0x5044EE` | `uint8` = **drapeau** (argument 2) |

   L'identité de classe est prouvée par la chaîne RTTI : `vptr` `0xA260D4` (`0x5044E5`) → descripteur de
   classe `0x00BBD990` → descripteur de type `0x00C14FE8` → nom `".?AUSMSG_WATCH_BOOTH@@"` à
   `VA 0x00C14FF0`.
4. **Le routage.** Le message part au gestionnaire (`0x504B26: call 0x6491C0`) puis à la pompe de
   messages (`0x640BC7` / `0x640BDA` : `mov ecx,[ds:0xc4b28c]` puis `call 0x49CD00`). Le dispatcher
   interne `VA 0x49CD00` lit l'identifiant en `[edi+4]` (`0x49CD3C`) et route par la table d'octets
   `0x49E8B4` (index = id − 4) vers la table de sauts `0x49E77C` ; **l'id 67 y a un seul bras**,
   `0x49CD93`, qui appelle `VA 0x493570` — l'unique appelant de ce dernier dans tout le binaire.
5. **L'émission.** `VA 0x493570` distingue les deux trames sur le drapeau :
   `0x493590: cmp BYTE PTR [edi+0x17],0x0` puis `0x493596: je 0x4936BE`.

   - drapeau ≠ 0 → **`702`** : construction `0x4935EF` (`VA 0x48CC70`), handle écrit à `frame+7`
     (`0x4935F4-0x4935F7`, `[ebp-0x1D]` = `frame+7`), checksum recalculé (`0x4935FD`, `VA 0x4731B0`),
     envoi par la session (`0x49361D`, `vtable+0x1CC`).
   - drapeau = 0 → **`704`** : construction `0x4936C1` (`VA 0x48CCC0`), handle écrit à `frame+7`
     (`0x4936C6-0x4936C9`, `[ebp-0x11]` = `frame+7`), checksum recalculé en ligne
     (`0x4936D1-0x4936D9`, somme des six octets d'en-tête), envoi identique.

   Le client est donc bien un **bascule** : le même contrôle produit `702` ou `704` selon le drapeau.
   Mesure importante pour le lot : **le seul site de construction du binaire passe `1`** (`0x504B0A`) —
   voir §7.1.

### 2.2 Conditions mises par le client lui-même

- **Les deux objets doivent se résoudre** : le handle visé passe par un appel virtuel
  (`0x4935AE-0x4935B5`) et le joueur local est lu en `[esi+0xA4]` ; si l'un des deux est nul
  (`0x4935B9-0x4935BF`), on part au refus.
- **Distance ≤ 100,0** : la distance 3D est calculée par `VA 0x473150` (racine de la somme des carrés
  des trois écarts de coordonnées `float` — `0x4731B0` qui suit est le calcul de checksum), puis
  comparée au double constant stocké à `VA 0xA19EC0` — octets `00 00 00 00 00 00 59 40` = **100,0**.
  `0x4935E7: test ah,0x5` / `0x4935EA: jp 0x493632` envoie au refus tout comparant « supérieur ou
  non ordonné ». **Au-delà de 100 unités client, aucun `702` n'est émis.**
- **Le refus est local** : les deux issues défavorables (objets non résolus, `0x493634`) construisent un
  message système sur la pile — `[ebp-0x5F] = 0x445` (1093) en `0x49363C`, `[ebp-0x63] = 0xA1CC18` en
  `0x493657`, `[ebp-0x50] = 0x3A2` (930) en `0x49365E`, `[ebp-0x2C] = 0xF` (15) en `0x49366E` — et
  l'envoient au client par un appel virtuel de session (`0x493697`, `vtable+0x124`) : le serveur n'est
  pas consulté. La nature exacte de ces quatre champs n'est pas identifiée (§7.3).

Conséquence pour le serveur : une observation hors de portée ne lui parvient **jamais**. Le contrôle de
portée du serveur, s'il est ajouté, ne peut se déclencher que sur un client modifié — il n'est pas
prouvé par le client de référence.

### 2.3 Ce que le client fait d'un `703`

Le client n'a qu'un seul handler pour `703`, `VA 0x6732F0` (voir §2.4 pour le routage). Il alloue 41
octets (`0x6732F6`), y écrit `0x44` = **68** en `+4` (`0x673309`, `vptr` `0xA52B0C` à `0x673321`) et
remplit :

| champ interne | écrit à | source fil | opération |
|---|---|---|---|
| `+0x13` = 1 | `0x673343` | — | marque « info reçue » |
| `+0x14` = `(type == 1)` | `0x67334C-0x67334F` | `type` `u8` @11 | `cmp BYTE PTR [esi+0xb],1` puis `sete` |
| `+0x15` = `uint32` | `0x673352` | `target` @7 | `mov ecx,[esi+0x7]` |
| conteneur | `0x67335A`, `0x673363`, `0x6733A8`, `0x6733BA-0x6733C5` | `count` @12, objets @14 | boucle sur `count`, indexation **source** `imul esi,esi,0x53` = **83**, copie plate de 83 octets (20 `dword` + 1 `word` + 1 `byte`) |

Les identifiants internes 67 et 68 appartiennent à la même table de dispatch : l'arm du 68 est
`0x49CF8E` (`0x49CF94: mov ecx,[edi+0x15]`), il lit bien le `target` recopié par le handler.

Autrement dit : **le client ne valide rien du `703`** — il recopie `count` enregistrements de 83 octets,
tel quel, à partir de `+14`. Une taille ou un compte faux désaligne tout le reste de la fenêtre.

### 2.4 Ce que le client accepte en entrant

La dispatch entrante (`VA 0x67DF59`, identifiant lu en `WORD PTR [ebx+4]`) traite les ids `505…710` par
la table d'octets `0x67F35C` (index = id − 505, vérifié en `0x67E3C2-0x67E3E3`) vers la table de sauts
`0x67F310`, dont l'index 18 est le défaut `0x67EF21` (« message non traité ») :

| id | octet de table | index | cible |
|---|---|---|---|
| 703 | `0x0E` | 14 | stub `0x67E3F1` → `0x6732F0` |
| 708 | `0x0F` | 15 | stub `0x67E40B` → `0x673670` |
| 709 | `0x10` | 16 | stub `0x67E3FE` → `0x66D9B0` |
| 710 | `0x11` | 17 | stub `0x67E418` → `0x6734D0` |
| **700, 701, 702, 704, 705, 706, 707** | `0x12` | 18 | **défaut `0x67EF21`** |

Et pour les petits ids, la table `0x67F0A0` (jump table `0x67F020`) : l'id 0 (`TS_SC_RESULT`) y a un
créneau (index 0 → `0x67DF89` → `0x66DB80`), l'id 67 n'en a **aucun** (défaut) — ce qui achève de
démontrer que le 67 du §2.1 est un identifiant **interne au client** et non un paquet du protocole.

Conséquences : le serveur ne doit **jamais** émettre `702` ni `704` (aucun créneau : ils seraient
journalisés « message non traité »), et `703` est la seule réponse de la famille qu'un client 7.3 sait
afficher.

---

## 3. Structure sur le fil

### 3.1 En-tête commun (7 octets)

| offset | taille | type | nom | source |
|---|---|---|---|---|
| 0 | 4 | `uint32` | `length` | client : `0x48CC78`/`0x48CCA4` (constructeurs `702`/`704`) ; dépôt : `Game/Network/Packets/Header.cs:9` |
| 4 | 2 | `uint16` | `id` | client : `0x48CC9E` (702) / `0x48CCEE` (704) ; dépôt : `Header.cs:10` |
| 6 | 1 | `uint8` | `checksum` | client : `0x4731B0` (somme des 6 octets 0…5, écrite en `+6`) ; dépôt : `Header.cs:11`, `PacketExtensions.cs:17-22` |

Les deux constructeurs du client écrivent d'abord `length = 7` puis remettent le checksum à jour après
avoir posé `id` et `length` définitifs — la règle « somme des octets 0…5 » est donc celle du client, et
c'est celle du dépôt. `BoothPackets.HeaderSize = 7` (`Game/Network/Packets/Game/BoothPackets.cs:38`) la
reprend déjà.

### 3.2 `TM_CS_WATCH_BOOTH` — `702`, client → serveur : **11 octets**

| offset | taille | type | nom | valeur observée | source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `length` | `11` | constructeur `VA 0x48CC70`, `0x48CCA4` |
| 4 | 2 | `uint16` | `id` | `702` (`0x2BE`) | `0x48CC99` (`mov edx,0x2be`) + `0x48CC9E` |
| 6 | 1 | `uint8` | `checksum` | somme 0…5 | `0x48CCAA-0x48CCB7` |
| 7 | 4 | `uint32` | `target` | handle de l'étal visé | écrit par l'appelant `0x4935F4-0x4935F7` (`frame+7`) |
| **11** | | | **taille totale** | | |

rzu `TS_CS_WATCH_BOOTH.h:8` (`_(simple)(ar_handle_t, target)`) et NGemity `TS_CS_WATCH_BOOTH.h:7`
(`_(simple)(uint32_t, target)`) : même corps de 4 octets. **Le client de 7.3 ne construit jamais autre
chose que 11 octets** : le constructeur est le seul producteur.

### 3.3 `TM_CS_STOP_WATCH_BOOTH` — `704`, client → serveur : **11 octets**

Identique au `702` champ pour champ, à l'identifiant près : `0x2C0` (`0x48CCE9`), corps `target`
`uint32` à `+7` (`0x4936C6-0x4936C9`). rzu `TS_CS_STOP_WATCH_BOOTH.h:8`, NGemity
`TS_CS_STOP_WATCH_BOOTH.h:7`. Aucun autre champ : la trame n'existe dans le binaire du client que comme
le pendant « fermeture » du `702` (§7.1).

### 3.4 `TM_SC_WATCH_BOOTH` — `703`, serveur → client : **14 + 83 × N octets**

En-tête
| offset | taille | type | nom | source fil |
|---|---|---|---|---|
| 0 | 4 | `uint32` | `length` | — |
| 4 | 2 | `uint16` | `id` | `703` (`0x2BF`) — mesuré par la table de dispatch entrant (`0x67F35C` + 198, §2.4) |
| 6 | 1 | `uint8` | `checksum` | — |

Corps

| offset | taille | type | nom | source |
|---|---|---|---|---|
| 7 | 4 | `uint32` | `target` | client `0x673352` (`mov ecx,[esi+0x7]`) ; rzu `TS_SC_WATCH_BOOTH.h:19` ; NGemity `:15` |
| 11 | 1 | `uint8` | `type` | client `0x673347` (`cmp BYTE PTR [esi+0xb],0x1`) ; rzu `:20` ; NGemity `:16` |
| 12 | 2 | `uint16` | `count` | client `0x673363` (`cmp ax,WORD PTR [esi+0xc]`) ; rzu `:21` ; NGemity `:17` |
| 14 | 83 × N | `TS_BOOTH_ITEM_INFO[]` | `items` | client `0x67335A` (`lea edi,[esi+0xe]`) + pas `0x53` `0x6733A8` ; rzu `:22` ; NGemity `:18` |
| **14 + 83 × N** | | | **taille totale** | |

`TS_BOOTH_ITEM_INFO` (83 octets pour 7.3)

| offset dans l'enregistrement | taille | type | nom | source |
|---|---|---|---|---|
| 0 | 75 | `TS_ITEM_FIXED_BASE_INFO` | `item` | rzu `TS_SC_WATCH_BOOTH.h:12` ; NGemity `:8` ; motif mesuré §3.5 |
| 75 | 8 | `int64` | `gold` | rzu `:13` (`>= EPIC_4_1_1 && < EPIC_9_8_1`) ; NGemity `:9-10` |
| **83** | | | | |

L'ordre **`item` puis `gold`** est celui des deux références pour toute version `< EPIC_9_8_1` ; rzu
place `item_type` **avant** `item` et `gold` **avant** `item` à partir de `EPIC_9_8_1` seulement
(`TS_SC_WATCH_BOOTH.h:9-13`) : hors sujet ici, mais c'est ce qui explique pourquoi l'ordre doit être
tranché explicitement.

Tailles résultantes : `N = 0` → **14** ; `N = 1` → **97** ; `N = 8` → **678** (`MAX_BOOTH_ITEM_COUNT` = 8,
`BoothPackets.cs:57`).

### 3.5 Le motif d'objet de 75 octets — et pourquoi il vaut 75 en 7.3

Le motif est celui déjà écrit par `Game/Network/Packets/Game/ItemFixedInfoWriter.cs` (`Size = 75`, ligne
71 : `handle` 0, `code` 4, `uid` 8, `count` 16, `ethereal_durability` 24, `endurance` 28, `enhance` 32,
`level` 33, `flag` 34, `sockets` 38 (4 × 4), `remain_time` 54, `elemental_effect.type` 58,
`…remain_time` 59, `…attack_point` 63, `…magic_point` 67, `appearance_code` 71 → 75). C'est le même
motif que l'inventaire `207` (85 = 75 + 10) et que la famille des enchères ; la table des offsets et sa
justification complète sont dans `docs/packet-specs/socle-encheres.md` §3.7, la décision
`appearance_code` en §4.6. **Cette fiche ne la rouvre pas : elle la corrobore et l'applique.**

Deux mesures indépendantes confirment ici les 75 octets, sans passer par la référence rzu :

1. le pas de la boucle de `703` est **83** (`0x6733A8`, `imul esi,esi,0x53`) et la copie plate fait
   **83** octets (`0x6733BA-0x6733C5`) ; `gold` vaut `int64` en 7.3 → l'objet vaut **83 − 8 = 75** ;
2. le constructeur de `705` (`0x48E684-0x48E79A`, relevé par `socle-encheres.md` §3.10) copie depuis
   la même source de pas `0x53` = 83 vers une destination de pas `0x4B` = 75.

### 3.6 Ce que le client refuse de vérifier

Le handler `703` (`0x6732F0`) ne compare **ni** la longueur déclarée **ni** `count` à une borne. Le
serveur est donc seul garant de la cohérence : `length` doit valoir exactement `14 + 83 × count`, sinon
le client lit au-delà de la trame ou s'arrête trop tôt. Aucune source ne fixe de borne haute client pour
`count` ; le socle borne à 8 (`BoothPackets.cs:57`).

---

## 4. Gating de version — tranché pour Epic 7.3

| # | champ / identifiant | gating rzu | décision 7.3 | source |
|---|---|---|---|---|
| 4.1 | ids `702` / `703` / `704` | `version < EPIC_9_6_3` (les formes `1702`/`1703`/`1704` valent pour `>= EPIC_9_6_3`) | **`702` / `703` / `704`** ; le remap 9.6.3 ne concerne pas ce dépôt | rzu `TS_CS_WATCH_BOOTH.h:11-12`, `TS_SC_WATCH_BOOTH.h:25-26`, `TS_CS_STOP_WATCH_BOOTH.h:11-12` |
| 4.2 | `TS_CS_WATCH_BOOTH.target` | aucun | `uint32` à `+7`, 4 octets | `:8` |
| 4.3 | `TS_SC_WATCH_BOOTH.target` | aucun | `uint32` à `+7` | `:19` |
| 4.4 | `TS_SC_WATCH_BOOTH.type` | aucun | `uint8` à `+11`, recopié verbatim | `:20` |
| 4.5 | `TS_SC_WATCH_BOOTH.count` | aucun | `uint16` à `+12` | `:21` |
| 4.6 | `TS_BOOTH_ITEM_INFO.item_type` | `version >= EPIC_9_8_1` | **ABSENT** | `:9` |
| 4.7 | `TS_BOOTH_ITEM_INFO.gold` | `>= EPIC_4_1_1 && < EPIC_9_8_1` | **PRÉSENT, `int64`, 8 octets à `+75`** (la forme `int32` est celle de `< EPIC_4_1_1`, la forme postérieure à `item` est celle de `>= EPIC_9_8_1`) | `:10-14` |
| 4.8 | `item.handle` / `code` / `uid` | aucun | `uint32` / `int32` / `int64` | rzu `TS_SC_INVENTORY.h:68-70` |
| 4.9 | `item.count` | `>= EPIC_4_1_1` | `int64` (la forme `int32` est celle de `< EPIC_4_1_1`) | `:71-72` |
| 4.10 | `item.ethereal_durability` | `>= EPIC_6_3 && < EPIC_9_8_1` | **PRÉSENT, `int32`** | `:75` |
| 4.11 | `item.endurance` | `>= EPIC_4_1 && < EPIC_9_8_1` | **PRÉSENT, `uint32`** | `:76-77` |
| 4.12 | `item.enhance` / `level` | aucun | `uint8` / `uint8` | `:78-79` |
| 4.13 | `item.enhance_chance` | `>= EPIC_9_2` | **ABSENT** | `:80` |
| 4.14 | `item.flag` | `< EPIC_9_8_1` | **PRÉSENT** | `:87` |
| 4.15 | `item.sockets` (4 × `int32`) | `< EPIC_9_8_1` | **PRÉSENT, 16 octets** | `:88` |
| 4.16 | `item.dummy_socket` (2 × `int32`) | `< EPIC_6_1` | **ABSENT** | `:27` (`TS_ITEM_BASE_INFO_DEF`) |
| 4.17 | `item.awaken_option` | `>= EPIC_8_1` | **ABSENT** | `:89` |
| 4.18 | `item.random_stats` | `>= EPIC_8_2` | **ABSENT** | `:90` |
| 4.19 | `item.remain_time` | `< EPIC_9_8_1` | **PRÉSENT, `int32`** | `:91` |
| 4.20 | `item.elemental_effect` | `>= EPIC_6_1 && < EPIC_9_8_1` | **PRÉSENT** : `type` `uint8`, `remain_time` `int32`, `attack_point` `int32`, `magic_point` `int32` | `:92` |
| 4.21 | `item.appearance_code` | `>= EPIC_7_4 && < EPIC_9_8_1` | **PRÉSENT malgré le gating** — décision `socle-encheres.md` §4.6, corroborée par les mesures du §3.5 | rzu `:93` contredit par le client |
| 4.22 | `item.summon_code` | `>= EPIC_8_2 && < EPIC_9_8_1` | **ABSENT** | `:94` |
| 4.23 | `item.item_effect_id` | `>= EPIC_9_6_1` | **ABSENT** | `:95` |

Total du motif : **75 octets** (§3.5). Aucun champ de cette fiche n'a de gating non statué.

Le seul point où rzu et le client divergent est 4.21 : rzu produirait 71 octets pour 7.3, le client en
attend 75. La règle « le client tranche » s'applique, et la mesure est double (§3.5).

---

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` fait du paquet : rien

NGemity **ne traite aucun paquet de la famille**. Ses en-têtes existent — `shared/Server/ClientPackets.h:181-192`
(dont `TS_CS_WATCH_BOOTH = 702`, `TS_SC_WATCH_BOOTH = 703`, `TS_CS_STOP_WATCH_BOOTH = 704`) et les trois
définitions sont incluses par `shared/Server/XPacket.h:157-175,320` — mais un `grep -ri booth` sur
`Chihiro/src` ne rend que **cinq lignes commentées** sur un drapeau joueur
(`Chihiro/src/Network/Messages.cpp:573-577`, `// @Todo: Booth`, `FLAG_BUY_BOOTH` / `FLAG_SELL_BOOTH`) :
aucun handler, aucun état d'étal, aucune émission de `703`, aucun verrou.

NGemity ne tranche donc ** aucun comportement** ici : il ne confirme que les identifiants, le sens, et
l'ordre `item` puis `gold` pour les versions antérieures à 9.8.1. Le traitement ci-dessous est une
décision Navislamia contrainte par le client seul (§2) et par le socle précédent.

### 5.2 Ce que le serveur doit faire

1. **Déclarer et router ensemble.** `TM_CS_WATCH_BOOTH` (702), `TM_SC_WATCH_BOOTH` (703) et
   `TM_CS_STOP_WATCH_BOOTH` (704) dans `Game/Network/Packets/Enums/GamePackets.cs`, **et** leurs bras
   dans la boucle de réception. La contrainte est double : le garde `DefinedPackets[header.ID]`
   (`GameClient.cs:1215-1230`, appliqué en `:1269`) ignore un id non déclaré, et un membre déclaré **sans**
   bras atteint le `_ => throw new Exception("Unknown Packet Type")` du `switch` final
   (`GameClient.cs:1854-1866`) — critère 4 des critères d'acceptation du dépôt.
2. **`702` : lire 11 octets, résoudre la cible, répondre `703` ou refuser.**
   Lecteur : sur le modèle de `BoothPackets.TryReadStopBooth` (`BoothPackets.cs:117-120`), longueur
   minimale `HeaderSize + 4` = **11**, `target` = `uint32` à `+7` ; toute trame plus courte →
   `TS_SC_RESULT(request_msg_id = 702, InvalidArgument)` (`SendResult`, `GameClient.cs:68-72`, trame
   `TS_SC_RESULT.cs:6-17` = `RequestMsgID` `u16`, `Result` `u16`, `Value` `i32`).
3. **`703` : le contenu, tranché.** La trame porte les **objets résolus contre l'inventaire du
   propriétaire de l'étal**, et non les triplets déclarés verbatim :
   - `target` = le handle demandé dans le `702` (c'est l'identité de l'étal pour le client, §2.3) ;
   - `type` = le `type` **déclaré** par le propriétaire dans son `700`, recopié sans interprétation
     (le socle §7.2 l'avait conservé verbatim ; le seul usage client est `type == 1`, §2.3, et son sens
     n'est pas établi — §7.5) ;
   - `count` = le nombre d'objets **effectivement** écrits (≤ 8) ;
   - chaque enregistrement = 75 octets du motif (`ItemFixedInfoWriter.Write`, `ItemFixedInfo.FromItem`
     pour le remplissage depuis `ItemEntity`) suivis du `gold` **déclaré** en `int64` (§7.7 pour
     « unitaire ou total ») ;
   - `length` = `14 + 83 × count`, exactement (§3.6).
   Le triplet déclaré (`item_handle`, `cnt`, `gold`) fournit le handle et le prix ; le contenu de
   l'objet (code, uid, durabilité, sockets…) vient de l'inventaire. Le socle précédent n'avait
   volontairement pas validé les handles ; c'est ici que la validation arrive, parce que `703` doit
   écrire 75 octets que le serveur ne peut pas inventer.
4. **Comment obtenir les 75 octets d'un personnage autre que le client courant.** Les primitives
   existent déjà sur `master`, aucune n'est à inventer :
   - `NetworkService.AuthorizedGameClients` (`Network/NetworkService.cs:52`, `ConcurrentDictionary`
     indexé par **nom de compte**) pour retrouver la connexion du propriétaire ;
   - `ConnectionInfo.CharacterHandle` (`ConnectionInfo.cs:30`, renseigné avec l'id de personnage en
     `Actions/GameActions.cs:114`) pour l'identification, et `ConnectionInfo.CharacterName`
     (`ConnectionInfo.cs:139`) pour l'argument suivant ;
   - `ConnectionInfo.Booth` / `IsBoothOpen` (`ConnectionInfo.cs:248`, `:254`) pour les objets déclarés ;
   - `ICharacterService.GetItemByHandleAsync(characterName, itemHandle)`
     (`Services/ICharacterService.cs:53` ; implémentation `Services/CharacterService.cs:255-259` : verrou
     exclusif **par personnage**, chargement depuis la base, `null` si le handle n'est pas dans
     l'inventaire) ;
   - `ItemFixedInfo.FromItem(ItemEntity)` (`ItemFixedInfoWriter.cs:40-59`) et
     `ItemFixedInfoWriter.Write(Span<byte>, in ItemFixedInfo)` (`:77-109`, `Size = 75`) : **le** chemin
     de sérialisation partagé avec l'inventaire et les enchères — ne pas réécrire ces 75 octets ailleurs.
5. **`704` : fermer l'observation.** Même lecteur que `702` (11 octets, `target` à `+7`), puis oublier
   l'état d'observation de la connexion. Réponse : `TS_SC_RESULT(704, Success)`, idempotent, y compris
   sans observation en cours — décision **tranchée par analogie** avec `701` (`socle-booths.md` §7.4) ;
   aucune source ne fixe le code, §7.
6. **État d'observation.** `ConnectionInfo` doit porter l'étal observé sous le même régime que `Booth`
   (`BoothLock`, `ConnectionInfo.cs:239-281`) : lecture/écriture verrouillées, parce que le fil de
   réception écrit et que d'autres services liront.
7. **Primitive manquante à construire.** Il n'existe **aucun index handle → connexion** :
   `AuthorizedGameClients` est indexé par compte (§5.2.4) et `CharacterHandle` n'est jamais utilisé comme
   clé (les seules tables handle → id du dépôt sont par connexion et pour les PNJ/objets :
   `ConnectionInfo.cs:159`, `:168`). Le lot doit donc soit parcourir `AuthorizedGameClients.Values`, soit
   ajouter un registre — decision à écrire dans le bloc `CLAUDE.md` et à couvrir par un test.
8. **Aucune diffusion.** L'étal reste invisible en dehors de `703` : il n'existe aucun flux de joueurs
   (`TS_SC_ENTER_PLAYER` n'est envoyé qu'au client qui entre — `Actions/GameActions.cs:178-210`), et la
   fiche précédente a tranché qu'aucun état d'étal n'est persisté ni diffusé. Le serveur ne doit donc
   **rien** envoyer aux autres joueurs sur `700`, `702` ou `704`.
9. **Verrou d'actions.** `smsg_booth_not_use_store` (« accéder à un autre magasin »), laissé sans objet
   faute de `702` (`BoothRules.cs:27-28`), trouve ici son objet : `TM_CS_WATCH_BOOTH` (702) rejoint la
   liste `GuardedActionIds` (`BoothRules.cs:30-41`). La ligne de la fiche précédente
   (`socle-booths.md` §12 point 8) est ainsi soldée ; `704` reste **hors** du verrou, c'est le chemin de
   sortie comme `701`.

### 5.3 Ce que le lot doit livrer

- `GamePackets` : les trois membres, plus les bras de dispatch de `702` et `704` (`703` n'a pas de bras
  entrant : c'est une sortie).
- Un lecteur `702`/`704` (11 octets) dans `BoothPackets` et une écriture `703` (`14 + 83 × N`), sur le
  modèle des paquets existants.
- L'état d'observation et sa purge (au moins sur `704` et à la déconnexion ; voir §7.9).
- Les refus par `TS_SC_RESULT`, avec le code tranché (§7.2).
- **Tests d'offsets** : taille `11` pour `702` et `704` et position de `target` (`+7`) ; pour `703`,
  taille totale et position de chaque champ sur **au moins deux valeurs de `N`** dont `N = 0` et `N = 8`
  (`14`, `97`, `678`), l'offset `+75` du `gold` de chaque enregistrement et le fait que la taille
  déclarée égale `14 + 83 × count`.
- Tests de comportement : `702` sur un étal ouvert rend un `703` dont les objets viennent de
  l'inventaire du propriétaire (et non des triplets bruts), `702` sur un handle inconnu rend le refus
  tranché, `704` est idempotent, le verrou `55` couvre désormais `702`.
- Aucun commit sur `master`, build et tests verts (≥ 366 tests), et le bloc `CLAUDE.md` **dans la
  description de la MR** (§5.4).

### 5.4 Bloc à recopier dans `CLAUDE.md` (par Killian, depuis la MR)

```markdown
## Étal de joueur — visibilité (702/703/704)

`TM_CS_WATCH_BOOTH` (702), `TM_SC_WATCH_BOOTH` (703) et `TM_CS_STOP_WATCH_BOOTH` (704) sont déclarés dans
`GamePackets` **et** dans la boucle de réception : `702` et `704` sont des trames de 11 octets (en-tête 7,
`target uint32` à +7), `703` est la seule réponse et fait `14 + 83 × count` octets (`target` à +7,
`type uint8` à +11, `count uint16` à +12, enregistrements de 83 à +14). L'enregistrement est le motif
d'objet partagé de 75 octets (`ItemFixedInfoWriter`, `appearance_code` inclus — voir
docs/packet-specs/socle-encheres.md §3.7 et §4.6, corroboré par le client : 83 = 75 + `gold int64`) suivi
du prix déclaré en `int64` à +75. Le contenu de `703` est **résolu** contre l'inventaire du propriétaire
(`ICharacterService.GetItemByHandleAsync`), jamais les triplets bruts du `700`. Le client de 7.3 n'a aucun
créneau de dispatch entrant pour 700/701/702/704/705/706/707 : `703` est la seule trame de la famille
qu'il sait afficher, et un `702` hors de 100 unités n'est même pas émis par le client. Envoi : clic sur le
panneau de nom de l'étal, message interne `SMSG_WATCH_BOOTH` (id interne 67) → drapeau 1 = 702, 0 = 704.
Le verrou d'actions (`BoothRules.GateAction`) couvre désormais `702` (`smsg_booth_not_use_store`).
Voir docs/packet-specs/socle-booths-visibilite.md.
```

---

## 6. Écarts assumés avec NGemity

| point | NGemity | Navislamia (7.3) | pourquoi |
|---|---|---|---|
| traitement | aucun : `Chihiro/src/Network/Messages.cpp:573-577` commentés, zéro handler, zéro état | `702` → `703` résolu, `704` idempotent, état d'observation par connexion | NGemity ne tranche pas le comportement ; le client seul le contraint (§2) |
| `target` | `uint32_t` (`TS_CS_WATCH_BOOTH.h:7`, `TS_SC_WATCH_BOOTH.h:15`) | idem, 4 octets | rzu nomme le type `ar_handle_t` : même taille, aucune conséquence sur le fil |
| motif d'objet | `TS_ITEM_BASE_INFO` avec `EPIC = EPIC_4_1_1` : 54 octets (`count` `int64`, pas d'`ethereal_durability` `>= 6_3`, pas d'`elemental_effect` `>= 6_1`, pas d'`appearance_code` `>= 7_4`, `dummy_socket` `< 6_1` présent) | **75 octets** | NGemity est sur une époque antérieure : sa version ne peut pas servir d'arbitre de taille ; seule la mesure client le peut (§3.5) |
| `gold` | `int64` après `item` (`TS_SC_WATCH_BOOTH.h:9-10`) | `int64` après `item`, à `+75` | rzu et NGemity concordent pour `< EPIC_9_8_1` : c'est le seul point que NGemity confirme |
| `item_type` | absent (sa macro n'a pas le champ) | absent (9.8.1) | concordance |
| logiciel de référence | `EPIC = EPIC_4_1_1` (`shared/Common/Define.h:25`) | `EPIC_7_3` | un écart de six époques : NGemity éclaire la logique, pas les tailles |

---

## 7. `NON ÉTABLI`

1. **Aucun site ne construit `704` dans le binaire.** Le seul site d'écriture de l'identifiant interne
   67 est le constructeur `0x5044C0`, appelé une seule fois, avec le drapeau **1** (`0x504B0A`). Aucune
   occurrence de `{vptr, 0x43}` ni `{0x43, vptr}` en données (recherche sur les 8 octets complets), donc
   pas de modèle statique recopiable non plus. L'envoi de `704` par ce client **n'est pas démontré** ;
   il reste déclaré par les deux références et doit être implémenté (un client modifié ou un script le
   peut), mais **aucun test ne peut s'appuyer sur un `704` réel**. Le système de commandes locales du
   client (`SCommandSystem::Process 03.1-Booth` / `03.3-Booth`, chaînes du binaire) pourrait construire
   le message par script : invérifiable sans exécuter le client, ce que ce dépôt s'interdit.
2. **Le code de refus d'un `702` non servable.** La famille n'a aucun paquet d'acquittement : ni rzu ni
   NGemity ne nomment de code, le client n'affiche rien de spécifique. Le dépôt a déjà tranché
   « refus = `TS_SC_RESULT` avec l'id demandé » pour `700`/`701` (`socle-booths.md` §5.3) ; le **code**
   exact (générique ? `NotActable*` ? autre ?) reste à trancher sur le serveur d'origine ou par
   observation. À figer dans le bloc `CLAUDE.md` une fois retenu.
3. **La trame système du refus « trop loin » du client.** Le chemin `0x493634` pose quatre valeurs sur
   la pile (`0x445` = 1093 en `0x49363C`, `0xA1CC18` en `0x493657`, `0x3A2` = 930 en `0x49365E`, `0xF` =
   15 en `0x49366E`) puis appelle une méthode virtuelle de session (`0x493697`) : c'est un message
   système du client, mais **la nature de ces quatre champs n'est pas identifiée** (identifiant de texte,
   type de canal, drapeaux ?) et le texte affiché n'est pas établi. Seule la règle des 100 unités l'est
   (§2.2) — et elle seule compte pour le serveur.
4. **Les quatre fenêtres du contrôle** (`0x1F`, `0x2D`, `0x38`, `0x45`) et le cinquième test
   (`0x504AA4`, `[objet+0x1E4]`) ne sont pas identifiés : le helper `0x648F30` → `0x62F210` fait une
   recherche dans la table `[gestionnaire+0x28]` et rend l'octet `[entrée+0xC]`, mais ni ces fenêtres ni
   `[+0x1E4]` (probablement le joueur local, sans preuve) n'ont été nommés. **Non bloquant** : ce sont des
   contrôles du client, pas du serveur.
5. **Le sens de `type`.** Le client ne fait que mémoriser `type == 1` (`0x673347-0x67334F`) ; rzu le nomme
   `type` sans énumérer ; le socle précédent l'a conservé verbatim sans lui donner de sens
   (`socle-booths.md` §7.2, §12 point 2). Rien ne dit que `1` soit « vente » plutôt qu'« achat ». Le lot
   doit **recopier** ce que le propriétaire a déclaré, sans le réinterpréter — mais le jour où un `705`/`706`
   devra juger un achat, il faudra trancher.
6. **Un handle déclaré qui ne se résout plus** (objet déplacé, consommé, vendu entre `700` et `702`) :
   aucune source. Deux options pour le lot — sauter l'enregistrement (`count` diminue, la fenêtre est
   cohérente) ou refuser tout le `703` par `TS_SC_RESULT` — ni l'une ni l'autre n'est établie.
   `smsg_booth_cant_equip_item` suggère que le serveur d'origine refusait les objets équipés, mais le
   texte seul ne prouve pas la trame.
7. **`gold` unitaire ou total** : non établi (`socle-booths.md` §7.8). `703` doit transporter la valeur
   déclarée telle quelle, sans conversion.
8. **Le plafond d'affichage du client.** Le conteneur du handler `703` croît (`0x673370-0x6733A0`,
   réallocation `0x591930`) : aucune borne haute mesurée côté client. Le plafond de 8 du socle
   (`BoothPackets.cs:57`) est une règle serveur, pas une mesure client.
9. **Quand l'observation doit être purgée.** `704` et la déconnexion sont les cas évidents ; ni rzu ni
   NGemity ne disent ce qui arrive à un observateur quand le propriétaire ferme son étal (`701`), se
   déconnecte ou change de zone. Aucune source. Décision à prendre par le lot et à écrire dans
   `CLAUDE.md`.
10. **Le remap `1702`/`1703`/`1704`** (`>= EPIC_9_6_3`) ne concerne pas ce dépôt : tranché au §4.1, pas
    une réserve.
11. **`707`/`708` (noms d'étal) et `710` (`TM_SC_BOOTH_TRADE_INFO`)** : le client les accepte (§2.4) et
    les chaînes `AUSMSG_BOOTH_NAME` / `AUSMSG_BOOTH_TRADE_INFO` existent dans le binaire. Cette fiche ne
    les cadre pas — une fenêtre d'observation complète pourrait un jour les demander (nom de l'étal
    absent du `703`), mais aucun élément mesuré ne l'exige aujourd'hui.

---

## 8. Commits épinglés et méthode de lecture

| référence | commit / empreinte |
|---|---|
| Navislamia (base) | `master` = `b56967a07430422add88e0e5cdf292b41b18f6c6` |
| rzu | `87c1e83b` (`packets: fix TS_SC_INVENTORY with older epics`) |
| NGemity | `38ceb2c` (`Fix compilation issue for GCC`) |
| client 7.3 — `SFrame.exe` | `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` |
| client 7.3 — `db_string.rdb` | `sha256 4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` |

Méthode (lecture seule, aucun exécutable du client lancé) :

```bash
cd /srv/navislamia/reference/client73
# constructeurs 702 / 704 (id, longueur 11, checksum)
objdump -d -M intel --start-address=0x48cc70 --stop-address=0x48cd20 SFrame.exe
# émetteur unique : drapeau, distance 100.0, écriture de target à frame+7, envoi
objdump -d -M intel --start-address=0x493570 --stop-address=0x4936e0 SFrame.exe
# helper de distance (racine de somme de carrés) et de checksum (somme des 6 octets)
objdump -d -M intel --start-address=0x473150 --stop-address=0x4731d0 SFrame.exe
# seuil de distance : double 100.0
objdump -s --start-address=0xa19eb0 --stop-address=0xa19ed0 SFrame.exe
# constructeur du message interne SMSG_WATCH_BOOTH (id 67, vptr 0xA260D4)
objdump -d -M intel --start-address=0x504490 --stop-address=0x504520 SFrame.exe
# contrôle _booth_name_back : événement 0xA, cinq gardes, envoi du message
objdump -d -M intel --start-address=0x504a50 --stop-address=0x504b80 SFrame.exe
# dispatch interne : table d'octets 0x49E8B4, table de sauts 0x49E77C (id 67 -> 0x49CD93)
objdump -s --start-address=0x49e77c --stop-address=0x49e980 SFrame.exe
# handler 703 : target @7, type @11, count @12, objets @14, pas 0x53, copie de 83 octets
objdump -d -M intel --start-address=0x6732f0 --stop-address=0x6733d0 SFrame.exe
# dispatch entrant : ids 0..250 (table 0x67F0A0 / sauts 0x67F020) et 505..710 (0x67F35C / 0x67F310)
objdump -s --start-address=0x67f020 --stop-address=0x67f0a0 SFrame.exe
objdump -d -M intel --start-address=0x67e3c0 --stop-address=0x67e430 SFrame.exe
objdump -s --start-address=0x67f310 --stop-address=0x67f430 SFrame.exe
```
