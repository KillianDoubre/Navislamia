# 262 — `TM_CS_REPAIR_SOULSTONE`

Fiche de paquet, Epic 7.3. Écrite par `navis-ref`, branche `hermes/packet-262-repair-soulstone`
depuis `master` `b56967a`. Ce document est le **livrable** : il ne modifie aucun fichier de code.

Toutes les adresses `SFrame.exe` sont des **adresses virtuelles** (`image base 0x00400000`) lues
par `objdump -d -M intel`, ou des positions dans le dump `strings`. Aucun binaire, aucun Lua,
aucun script du client n'a été exécuté ; la résolution est statique (§8).

Cette fiche **tranche deux questions ouvertes** que les documents antérieurs laissaient en
suspens :

- `socle-artisanat-objets.md` §2.3 : « 262 — geste NON ÉTABLI » ;
- `260-soulstone-craft.md` §7.1 : « l'id du paquet d'ouverture de la fenêtre (de sertissage)
  n'est pas établi ». L'id est **261**, et son bras de réception est retrouvé ci-dessous (§2.2,
  §2.6).

---

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| Id décimal / hexadécimal | **262** / `0x106` | `op_codes.md:90` (`[262] = "TM_CS_REPAIR_SOULSTONE"`) |
| Nom | `TM_CS_REPAIR_SOULSTONE` | idem |
| Sens | **client → serveur** | `reference/rzu/librzu/src/packets/GameClient/TS_CS_REPAIR_SOULSTONE.h:12` (`SessionPacketOrigin::Client`) |
| Déclaration rzu | `_(array)(ar_handle_t, item_handle, 6)` | `TS_CS_REPAIR_SOULSTONE.h:5-6` |
| Déclaration NGemity | `_(array)(uint32_t, item_handle, 6)` — **en-tête seul, aucun gestionnaire** | `shared/Server/Packets/GameClient/TS_CS_REPAIR_SOULSTONE.h:6-9` ; §5.1 |
| Énumération du dépôt | `GamePackets.TM_CS_REPAIR_SOULSTONE = 262` | `Game/Network/Packets/Enums/GamePackets.cs:60` |
| Famille | socle « artisanat et enchantement d'objets », lobe 260/262/263/264 | `docs/packet-specs/socle-artisanat-objets.md` §1 |

Le paquet d'**ouverture** de la fenêtre correspondante est **261**
`TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW` (serveur → client, corps vide) :
`op_codes.md:89`, rzu `TS_SC_SHOW_SOULSTONE_REPAIR_WINDOW.h:5` (`DEF(_)` vide), `:8-10` (id),
`:11` (`SessionPacketOrigin::Server`), NGemity `shared/Server/ClientPackets.h:102`.

---

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 La chaîne, étape par étape

| # | Ce qui se passe | Statut | Source |
| --- | --- | --- | --- |
| 1 | Le joueur parle à un **joaillier** (« soulcrafter »). Les PNJ `1087, 2085, 4083, 6045, 7042, 7043` portent le script de contact `NPC_Jewelry_contact()` | établi | `DevConsole/npc-dialogs.73.json`, `NpcDialogCatalog.Npcs` |
| 2 | Dans ce dialogue, l'entrée de menu `Menu[6]` (libellé `@90010170`) déclenche `show_soulstone_repair_window()` ; l'entrée voisine `Menu[5]` (`@90010169`) déclenche `show_soulstone_craft_window()` | établi | `npc-dialogs.73.json`, `Dialogs.NPC_Jewelry_contact.Menu` |
| 3 | Les libellés lisibles des deux entrées se suivent dans la table de chaînes : `Socket my Equipment.` puis `Charge my Soul Power.` | **lecture retenue** (association libellé ↔ entrée par ordre et par voisinage) | `db_string.rdb`, dump l. 242715 et 242717 |
| 4 | Le PNJ explique le service : les pierres d'âme se rechargent avec du **Lak**, sinon elles perdent leur effet | établi (texte client) | `db_string.rdb`, dump l. 11162 (`Merchant June`) |
| 5 | Le serveur **ouvre la fenêtre** en envoyant **261** (7 octets, aucun corps). Le client 7.3 route 261 vers la classe de message `SMSG_OPEN_SOUL_REPAIR` | **établi** (§2.2, §2.6) | client : table d'entrée `0x67F218` / sauts `0x67F19C`, stub `0x67e30c` → `0x66fc80` → vtable `0xa51fd8` |
| 6 | La fenêtre est `window_SoulCharge.nui`, classe `SUISoulChargeWnd` | **lecture retenue** (réserve n° 1, §7) | `SFrame.exe`, dump l. 24811, 44282, 24941 |
| 7 | Le joueur remplit la liste « objet à recharger » (jusqu'à six entrées, `Choose all Equipped Items`), lit le « Current Soul Power », puis clique `Charge` | établi (textes de fenêtre) | `db_string.rdb`, dump l. 112392, 112394, 112396, 112398, 112400, 112402 |
| 8 | Le clic construit le message d'interface **`UIMSG_UI_REPAIR_SOULSTONE`** (id interne `0x476` = 1142) à partir des **six slots de la fenêtre**, dans l'ordre | **établi** (§2.4) | client `0x56b2b0-0x56b342` |
| 9 | La table de dispatch de l'interface convertit **1142** en un stub unique, qui construit et envoie la trame de 31 octets | **établi** (§2.5) | client `0x49e21d-0x49e234`, `0x49e349`, `0x48dad0`, `0x48cf10` |
| 10 | Si les **six** slots sont vides, le client **n'envoie rien** | établi | client `0x56b270-0x56b2ae` |

### 2.2 Le bras de réception de 261 (ce que la fiche de socle cherchait)

La fonction d'entrée du client (`0x67df5d-0x67df82` puis `0x67e1d9-0x67e201`) compare l'id reçu
et aiguille par deux tables :

```
0x67df59  movzx ecx, WORD PTR [ebx+0x4]     ; id du paquet reçu
0x67df5d  mov   eax, ecx
0x67df5f  cmp   eax, 0xfe
0x67df6a  je    0x67e1cc                     ; id 254 (cas particulier, hors table)
0x67df70  cmp   eax, 0xfa
0x67df7b  movzx eax, BYTE PTR [eax+0x67f0a0] ; table d'octets, ids 0x00..0xfa
0x67df82  jmp   DWORD PTR [eax*4+0x67f020]   ; table de sauts
...
0x67e1ea  sub   eax, 0xff
0x67e1ef  cmp   eax, 0xf5
0x67e1fa  movzx edx, BYTE PTR [eax+0x67f218] ; table d'octets, ids 0xff..0x1f4
0x67e201  jmp   DWORD PTR [edx*4+0x67f19c]   ; table de sauts
```

La seconde table contient **30 entrées non vides** (les 223 autres portent la sentinelle `0x30`) ;
inversion faite, **les stubs 259 et 261 ne sont atteints que par un seul id chacun** :

| Id | Stub | Fonction | Classe de message construite (RTTI) |
| --- | --- | --- | --- |
| 255 | `0x67e319` | `0x6700e0` | `SMSG_UPDATE_ITEM_COUNT` (3 champs à `+0x13/+0x17/+0x1b`) |
| 257 | `0x67e326` | `0x670150` | *hors sujet* |
| **259** | `0x67e2ff` | `0x66fc20` | **`SMSG_OPEN_JEWEL_EQUIP`** |
| **261** | `0x67e30c` | `0x66fc80` | **`SMSG_OPEN_SOUL_REPAIR`** |
| 280, 282, 283, 286, 287, 301, 302, 303, 305, 306, 307, 310, 320, 321, 322, 350, 351, 352, 353, 401, 403, 404, 406, 407, 451, 500 | — | — | hors sujet (26 autres entrées) |

**260, 262 et 263 n'apparaissent pas** dans cette table : le client 7.3 n'attend aucun de ces ids
en entrée, ce qui confirme leur sens `client → serveur` (rzu `TS_CS_*`).

Les deux constructeurs de fenêtre ne lisent **rien** dans le paquet reçu : ils écrivent seulement
l'en-tête fixe du message puis le postent (`0x66fc63-0x66fc6d` et `0x66fcc3-0x66fccd`). **261 est
donc bien une trame sans corps** — ce que rzu déclare (`DEF(_)` vide) est confirmé par le client.

Classe de message `SMSG_OPEN_SOUL_REPAIR` : `0x66fcb9` `mov DWORD PTR [eax],0xa51fd8` →
vtable `0xa51fd8` → `[vtable-4]` = COL `0xbc83c0` → `[COL+0xc]` = TD `0xc1dcfc` →
nom au TD+8 = `.?AUSMSG_OPEN_SOUL_REPAIR@@` (dump l. 44396).

### 2.3 La fenêtre et ses textes

| Élément | Position dans le dump `SFrame.exe` |
| --- | --- |
| `window_SoulCharge.nui` | l. 24811 (immédiatement après `window_Socket.nui`, l. 24812) |
| `Create : SUISoulChargeWnd` (journal de fabrique) | l. 24941 |
| `.?AVSUISoulChargeWnd@@` (RTTI) | l. 44282 — vtable `0xa4698c` |
| `.?AUSIMSG_SOULCHARGE_MOVEITEM@@` (message interne de déplacement vers un slot) | l. 44184 |
| `SGameInterface - IMSG_SOULCHARGE_MOVEITEM` | l. 25061 |
| `static_common_socket_itemslot` (widget de la fenêtre de sertissage, pour comparaison) | l. 30293 |

Textes de la fenêtre (`db_string.rdb`, positions dans le dump) : `Recharge EQ` (112392),
`Item to be Recharged` (112394), `Choose all Equipped Items` (112396),
`Current Soul Power: #@per@#%` (112398), `Required` (112400), `Charge` (112402).
Textes d'échec : `Recharge all items to 100%` (198051), `You don't have enough Lak.` (198053),
`Consume #@lak@# Lak to recharge some Soul Power.` (198055),
`#@item_name@# is out of Soul Power.  You no longer get any benefit from its socketed Soul Stones.` (198057).

### 2.4 L'émission : le message d'interface et les six slots

Constructeur du message `UIMSG_UI_REPAIR_SOULSTONE` (`0x56b2b0` début de fonction, `0x56b2cd`-) :

```
0x56b2cd  mov DWORD PTR [ebp-0x34], 0x476     ; id interne du message = 1142
0x56b2e8  mov DWORD PTR [ebp-0x38], 0xa2d198  ; vtable = UIMSG_UI_REPAIR_SOULSTONE
0x56b2ec  mov edx, [ecx+0x4ac]                ; et, dans l'ordre :
0x56b2f5  mov eax, [ecx+0x4a8]   -> [ebp-0x25]  ; charge +0x13  (handle 0)
0x56b2fe  mov eax, [ecx+0x4b0]   -> [ebp-0x1d]  ; charge +0x1b  (handle 2)
0x56b307  mov eax, [ecx+0x4b8]   -> [ebp-0x15]  ; charge +0x23  (handle 4)
0x56b30d  mov [ebp-0x21], edx                   ; charge +0x17  (handle 1) <- +0x4ac
0x56b310  mov edx, [ecx+0x4b4]   -> [ebp-0x19]  ; charge +0x1f  (handle 3)
0x56b31c  mov edx, [ecx+0x4bc]   -> [ebp-0x11]  ; charge +0x27  (handle 5)
0x56b322  mov ecx, [ecx+0x440]                  ; l'interface
0x56b32b  push eax (lea [ebp-0x38]) ; call 0x6491c0   ; poste le message
```

Ce qui est **établi** par ce relevé :

1. l'objet émetteur porte **six** slots consécutifs `+0x4a8, +0x4ac, +0x4b0, +0x4b4, +0x4b8,
   +0x4bc` (24 octets, six `uint32`) ;
2. les six slots sont copiés **dans l'ordre de la fenêtre vers l'ordre de la trame** (slot `+0x4a8`
   → handle 0, …, slot `+0x4bc` → handle 5) ;
3. les six slots sont traités **identiquement** — aucun n'est distingué (à comparer à 260, §6.2) ;
4. le message porte exactement six handles, puis l'interface les transmet à l'émetteur de trame.

Le prédicat `0x56b270-0x56b2ae` teste les six slots et rend `0` si tous sont nuls, `1` sinon :
**une trame à six zéros n'est jamais émise**.

### 2.5 Le dispatch et l'émission de la trame

| Étape | Adresse | Détail |
| --- | --- | --- |
| Clé de dispatch | `0x49e21d` | `sub eax,0x403` → clé − 1027 |
| Borne | `0x49e222` | `cmp eax,0xde` (223 entrées, clés **1027..1249**) |
| Table d'octets | `0x49EA50` | index 115 = octet `0x1C` = **28** ; `0x30` (`'0'`) = sentinelle, 175 occurrences sur 223 |
| Table de sauts | `0x49E98C` | entrée 28 = `0x49e346` |
| Stub | `0x49e349` | `call 0x48dad0` — **unique appelant de l'émetteur** dans tout le binaire |
| Émetteur | `0x48dad0` | reprend l'en-tête du constructeur de trame, calcule l'octet 6, copie `[edi+0x13..+0x27]` aux offsets 7, 11, 15, 19, 23, 27 |
| Constructeur de trame | `0x48cf10` | longueur `0x1f` = 31 (`0x48cf53`), id `0x106` = 262 (`0x48cf48`), remise à zéro des octets 4..30 |

Clés voisines, relevées pour situer la famille (aucune n'atteint 262) :

| Clé | Stub | Ce qu'il émet |
| --- | --- | --- |
| 1138 (`0x472`) | `0x49e339` → `0x48da50` | 260 (`0x104`), 27 octets — l'entrée `UIMSG_UI_SOULSTONE_CRAFT` (`260-soulstone-craft.md` §3.1) |
| **1142 (`0x476`)** | `0x49e346` → `0x48dad0` | **262 (`0x106`), 31 octets** |
| 1145 (`0x479`) | `0x49e353` → `0x48db50` | trame id `0x198` (408), 15 octets — hors famille |
| 1147 (`0x47b`) | `0x49e3ef` | émetteur paramétré (`cmp BYTE PTR [edi+0x1c],0`, puis lecture de `[edi+0x13]`/`[edi+0x17]`) — hors famille |

Nom de la classe de message par la chaîne RTTI (méthode §2.6) : vtable `0xa2d198` → COL `0xbc123c`
→ TD `0xc17408` → nom `.?AUSIMSG_UI_REPAIR_SOULSTONE@@` (dump l. 44022). Pour 260, la même chaîne
donne vtable `0xa2d190` → `.?AUSIMSG_UI_SOULSTONE_CRAFT@@` (l. 44021) : les deux messages, dont les
clés sont voisines, sont bien deux classes distinctes et nommées.

### 2.6 Méthode de lecture du client (reproductible)

La chaîne « id de paquet → nom de classe » n'utilise que la lecture statique :

1. `objdump -s --start-address=<table>` pour la table d'octets et celle de sauts ;
2. `objdump -d` autour du stub pour lire l'appel ;
3. dans le constructeur appelé, relever `mov DWORD PTR [eax],<vtable>` ;
4. `[vtable-4]` = COL ; `[COL+0xc]` = TypeDescriptor ; le nom est **en ligne** à `TD+8`
   (`TypeDescriptor` MSVC : `pVFTable`, `spare`, `name[]`).

Piège rencontré et documenté : il existe **plusieurs copies** de certaines chaînes RTTI dans le
binaire ; la conversion « nom → TD » (`nom - 8`) tombe alors sur la mauvaise. **Seul le sens
vtable → nom est fiable** (c'est celui utilisé ci-dessus). Un premier essai dans l'autre sens
avait fait attribuer la vtable `0xa51fe8` à `SMSG_OPEN_SOUL_REPAIR` alors qu'elle est celle de
`SMSG_ITEM_DESTROY` (id 254, cas particulier `0x67e1cc`).

---

## 3. Structure sur le fil

### 3.0 Conventions, sourcées

En-tête de **7 octets** : `uint32 Length` (compte l'en-tête), `uint16 ID`, `byte Checksum`
(`Game/Network/Packets/Header.cs:9-11` ; convention écrite dans `CLAUDE.md:51-53`). Le `Checksum`
est la **somme des six premiers octets** — côté dépôt `PacketExtensions.cs:13-24` (`Length` sur
4 octets + `ID` sur 2), côté client la même boucle avant émission (`0x48dad0` : boucle
`0x48db14-0x48db25` écrit l'octet 6).
Les offsets ci-dessous sont absolus depuis le début de la trame.

`ar_handle_t` = `uint32` (`reference/rzu/librzu/src/lib/Packet/GameTypes.h:40-42`) ;
`_(array)(T, f, N)` = N éléments fixes de `sizeof(T)` (`socle-artisanat-objets.md` §3.0).

### 3.1 Trame 7.3 — **31 octets, fixe**

| Offset | Type | Champ | Valeur observée | Source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | **31** (`0x1f`) | rzu `TS_CS_REPAIR_SOULSTONE.h` (dérivé de la taille) ; client `0x48cf53` ; `Header.cs:9` |
| 4 | `uint16` LE | `ID` | **262** (`0x106`) | `op_codes.md:90` ; client `0x48cf48` |
| 6 | `uint8` | `Checksum` | somme des octets 0..5 | client `0x48db14-0x48db25` ; `PacketExtensions.cs:13-24` |
| 7 | `uint32` LE | `item_handle[0]` | slot 0 de la fenêtre, **0 si vide** | rzu `TS_CS_REPAIR_SOULSTONE.h:6` (`_(array)(ar_handle_t, item_handle, 6)`) ; client `0x48dae4` (copie) et `0x48dae1-0x48db08` (bloc complet) |
| 11 | `uint32` LE | `item_handle[1]` | slot 1, **0 si vide** | idem ; client `0x56b2ec` |
| 15 | `uint32` LE | `item_handle[2]` | slot 2, **0 si vide** | idem ; client `0x56b2fe` |
| 19 | `uint32` LE | `item_handle[3]` | slot 3, **0 si vide** | idem ; client `0x56b310` |
| 23 | `uint32` LE | `item_handle[4]` | slot 4, **0 si vide** | idem ; client `0x56b307` |
| 27 | `uint32` LE | `item_handle[5]` | slot 5, **0 si vide** | idem ; client `0x56b31c` |

**Taille totale attendue : 31 octets** — 7 d'en-tête + 6 × 4. Aucun champ de comptage, aucune
longueur variable, aucune chaîne. La trame est identique qu'un seul slot soit rempli ou six.

### 3.2 Preuve par lecture du client (les deux extrémités)

| Ce qui est prouvé | Relevé |
| --- | --- |
| Le client **émet** 262 | une seule occurrence de l'id `0x106` comme id de trame (`0x48cf48`) et un seul émetteur (`0x48dad0`, appelé uniquement depuis `0x49e349`) |
| La longueur est fixe | `0x48cf53` `mov DWORD PTR [eax],0x1f` ; le corps est mis à zéro de l'octet 4 à l'octet 30 (`0x48cf2f-0x48cf45`) |
| Les six handles sont en 7, 11, 15, 19, 23, 27 | `0x48dae1-0x48db08` : `[edi+0x13]→[ebp-0x19]`, `[edi+0x17]→[ebp-0x15]`, `[edi+0x1b]→[ebp-0x11]`, `[edi+0x1f]→[ebp-0xd]`, `[edi+0x23]→[ebp-0x9]`, `[edi+0x27]→[ebp-0x5]`, la trame commençant à `[ebp-0x20]` |
| Ils viennent des **six slots** d'un même objet, dans l'ordre | `0x56b2f5`/`0x56b2ec`/`0x56b2fe`/`0x56b310`/`0x56b307`/`0x56b31c` (§2.4) |
| Un slot vide vaut **0** | prédicat `0x56b270-0x56b2ae` : les six slots sont testés contre `0`, et la fonction rend `0` s'ils sont tous nuls |
| **Rien n'est lu dans la trame reçue** pour 261 | `0x66fc80` n'accède jamais à `[ebp+8]` (§2.2) |

### 3.3 Sentinelles nulles — établi des deux côtés

Un handle à `0` est un **slot vide**, pas un objet manquant :

- côté client : les six slots sont mis à zéro puis remplis à la demande, et le prédicat
  `0x56b270-0x56b2ae` traite `0` comme « rien à faire » ;
- côté dépôt : `CraftingSocleRules.AddIfSet` écarte les zéros avant résolution
  (`Game/Services/CraftingSocleRules.cs:55-68`, `:78-80`), et `CLAUDE.md:1616-1617` l'écrit comme
  règle de la famille ;
- côté NGemity : analogue pour 260 et 256 (`WorldSession.cpp:1521`, `:1451`), sans handler pour 262.

**Conséquence pour `navis-dev` : un zéro ne doit jamais produire `NotExist`.**

---

## 4. Gating de version — décisions prises pour 7.3

| Point | Ce que rzu montre | **Décision 7.3** | Source |
| --- | --- | --- | --- |
| Id du paquet | `X(262, version < EPIC_9_6_3)` / `X(1262, version >= EPIC_9_6_3)` | **262** ; `1262` postérieur à la 9.6.3, hors sujet | `TS_CS_REPAIR_SOULSTONE.h:8-10` |
| Bornes | `EPIC_7_3 = 0x070300` borne `EPIC_9_6_3 = 0x090603` ; comparaison sur 24 bits | `0x070300 < 0x090603` → branche « `< EPIC_9_6_3` » | `PacketEpics.h:59`, `:96`, `:8` (`compare()`) |
| Champs | le `DEF` ne porte **aucune** condition `version >=` : les six handles existent tels quels de la 7.2 à la 9.6.3 | **aucun champ gaté, aucun champ absent** : les six handles sont présents en 7.3 | `TS_CS_REPAIR_SOULSTONE.h:5-6` |
| Taille | 6 × `ar_handle_t` = 24 octets de charge | **31 octets**, identique à toutes les versions couvertes par rzu | §3.1 |
| Confirmation client | le binaire 7.3 émet lui-même l'id `0x106` avec 31 octets | décision confirmée par le client, qui prime | `0x48cf48`, `0x48cf53` |
| NGemity | `CREATE_PACKET(TS_CS_REPAIR_SOULSTONE, 262)` sans versionnement, projet sur `EPIC = EPIC_4_1_1` | hors sujet pour un gating 7.3 (`socle-artisanat-objets.md` §5.3) | `TS_CS_REPAIR_SOULSTONE.h:9` |

Aucun champ de cette fiche n'est laissé « gaté » sans décision : **la version est tranchée pour
tout le paquet**.

---

## 5. Traitement attendu

### 5.1 Ce que NGemity (`Chihiro`) porte réellement

| Élément | Existe ? | Source |
| --- | --- | --- |
| En-tête `TS_CS_REPAIR_SOULSTONE` + valeur d'énumération 262 + inclusion | **oui** (déclaration seule) | `shared/Server/Packets/GameClient/TS_CS_REPAIR_SOULSTONE.h:1-11`, `shared/Server/ClientPackets.h:103`, `shared/Server/XPacket.h:137` |
| Gestionnaire de la **requête** 262 (`onRepairSoulstone`, `declareHandler`, script) | **non — aucun** | `grep -rn TS_CS_REPAIR_SOULSTONE` ne rend que les trois lignes ci-dessus ; `WorldSession.cpp:131` ne déclare que `onSoulStoneCraft` (260) |
| **Ouverture** de la fenêtre (261) | **oui** | `Messages::ShowSoulStoneRepairWindow` `Chihiro/src/Network/Messages.cpp:936-941` (`TS_SC_SHOW_SOULSTONE_REPAIR_WINDOW` + `SetLastContact("RepairSoulStone", 1)`) ; déclaration `Messages.h:85` |
| Liaison Lua du déclencheur de PNJ | **oui** | `show_soulstone_repair_window` → `XLua.cpp:105`, implémentation `:924-928` |
| Garde de contact | **prévue** : le contact `RepairSoulStone` est armé par l'ouverture ; le modèle 260 lit `GetLastContactLong("SoulStoneCraft")` en tête de son gestionnaire | `Messages.cpp:939` ; `WorldSession.cpp:1499` (260) |

**Donc : rien à porter pour la requête**, et le cadrage de `socle-artisanat-objets.md` §6.3
(« 262 / 263 / 264 — rien à porter ») doit être lu comme « aucune **logique de rechargement** à
porter » : NGemity porte bien **l'ouverture**, et c'est cette moitié-là qui manque au dépôt (§5.3).

### 5.2 Ce que le serveur doit faire du paquet, et ce qu'il doit répondre

| Décision | Valeur retenue | Source / statut |
| --- | --- | --- |
| Taille acceptée | **exactement 31 octets** ; toute autre longueur = malformé | §3.1 ; déjà implémenté (`GameActionPackets.cs:509-518`) |
| Slot vide | `0` = vide, **jamais résolu** | §3.3 ; `CraftingSocleRules.cs:78-80` |
| Handle non nul inconnu du personnage | refus `NotExist` (avec le handle) | modèle NGemity `WorldSession.cpp:1521` ; dépôt `CraftingSocleService.cs:114-118` |
| Trame à six zéros | ne peut pas venir du client (§2.4) ; la traiter comme malformée ou comme refus, **au choix du dev, mais à écrire** | déduit du prédicat `0x56b270-0x56b2ae` |
| **Réponse de succès** | **aucune source** : rzu ne déclare aucun paquet de résultat dans cette famille, NGemity n'a pas de gestionnaire, et le client n'a qu'un seul message entrant lié au rechargement (261, sans corps). Il n'existe donc **rien** qui puisse transporter un prix, un taux ou un résultat par objet | §5.1, §2.2 |
| Comportement retenu pour ce cycle | **refus `InvalidArgument` (0)**, c'est-à-dire l'état actuel du dépôt : la trame est lue, bornée, ses handles résolus, puis refusée | `CraftingSocleService.cs:115-125` |

Un moteur de rechargement exigerait trois valeurs qu'aucune source du corpus ne porte : le coût
en Lak, la formule du rechargement, et l'endroit où vit la « Soul Power » d'une pierre (§7). Ce
sont des **décisions de game design**, pas des déductions — elles appartiennent à Killian.

### 5.3 État du dépôt à `b56967a` (ce qui est déjà là, ce qui manque)

Déjà livré par le socle : l'id dans l'énumération (`GamePackets.cs:60`), le lecteur de trame
(`GameActionPackets.cs:498-528` : 6 handles aux offsets 7..27, refus de 30/32 octets), la règle
des sentinelles (`CraftingSocleRules.cs:51-68`), l'armement du service (`CraftingSocleService.cs:77-85`),
le bras de dispatch de `GameClient` (`GameClient.cs:1622-1630`) et trois tests d'offsets
(`Tests/Game/CraftingSoclePacketsTests.cs:247-277`, id vérifié ligne 30).

**Ce qui manque pour que 262 soit atteignable ou traitable — trois blocages nommés :**

1. **Aucun émetteur de 261.** L'id existe dans l'énumération (`GamePackets.cs:59`) et son bras de
   réception journalise puis rejette un 261 entrant (`GameClient.cs:1419-1428`), mais rien dans le
   dépôt ne **construit** ce paquet. Sans lui, la fenêtre ne s'ouvre pas et 262 est inatteignable
   en jeu (§2.1 étape 5).
2. **Aucun gestionnaire du déclencheur de PNJ.** `show_soulstone_repair_window()` n'apparaît que
   dans les données (`DevConsole/npc-dialogs.73.json`) ; le service de dialogues n'en a pas de
   gestionnaire, donc le point d'armement du contact (`RepairSoulStone` chez NGemity,
   `Messages.cpp:939`) n'existe pas côté dépôt — même situation que celle décrite par
   `260-soulstone-craft.md` §5.4.
3. **Aucun stockage de la « Soul Power ».** `grep -ni soul ArcadiaSchemaPSQL.sql` ne rend **aucune**
   occurrence, et `Game/DataAccess/Entities/Telecaster/ItemEntity.cs:30-31` ne porte que
   `EtherealDurability` et `Endurance` (l'équivalent `ethereal_durability` de la ressource est en
   `MSSQLItemResource.cs:40`). Un rechargement ne pourrait donc rien persister aujourd'hui.

### 5.4 Garde de contact

NGemity arme le contact dans le message d'**ouverture** (`Messages.cpp:939`) et le consomme dans le
gestionnaire de la requête ; pour 260 ce contrôle est `WorldSession.cpp:1499`. La transposition
directe est **impossible aujourd'hui** : le point d'armement (le déclencheur de dialogue, et son
paquet d'ouverture 261) est un lobe non livré (`socle-artisanat-objets.md` §9.4), et
`NpcDialogService` efface l'armement dès que le déclencheur n'a pas de gestionnaire
(`260-soulstone-craft.md` §5.4, point 1). Décision de cette fiche : **la garde de contact de 262
appartient au lobe « ouverture de fenêtre »**, pas au traitement de la requête. Aucun état de
contact ne doit être inventé.

---

## 6. Écarts assumés avec NGemity, et pourquoi

1. **NGemity n'a pas de logique ; le dépôt n'en a pas non plus.** L'écart est une **absence**, pas
   un désaccord : la requête est refusée des deux côtés, par des chemins différents (§5.2).
   La correction à retenir est documentaire : NGemity **implémente l'ouverture** (261 + contact),
   contrairement à ce que §6.3 du socle laisse entendre.
2. **Six slots homogènes (262) contre « cible + matériaux » (260).** Le client traite les six slots
   de 262 **identiquement** (copie `0x56b2ec-0x56b31c`, prédicat `0x56b270-0x56b2ae`), alors que
   pour 260 la cible arrive en **argument de la fonction** (`0x56ae80-0x56ae85`) et que seules les
   quatre pierres viennent des slots `+0x4ac..+0x4b8`. C'est un argument de client, pas de
   NGemity : l'hypothèse « 1 objet + 5 matériaux » de `socle-artisanat-objets.md` NON ÉTABLI 2 est
   **écartée** ; les six handles sont de même nature.
3. **Validation de la méthode par recoupement.** Les ids entrants 254 et 255 construisent des
   messages dont les vtables portent les noms `SMSG_ITEM_DESTROY` (`0x670080`) et
   `SMSG_UPDATE_ITEM_COUNT` (`0x6700e0`), à comparer à `op_codes.md:81-82`
   (`[254] = "TM_SC_DESTROY_ITEM"`, `[255] = "TM_SC_UPDATE_ITEM_COUNT"`). La chaîne
   vtable → COL → TD → nom employée dans cette fiche recoupe donc l'énumération connue, et 263
   n'apparaît pas dans la table entrante. Aucune conséquence pour 262.
4. **Le nom de la fenêtre.** NGemity nomme la sienne par la classe de paquet uniquement
   (`TS_SC_SHOW_SOULSTONE_REPAIR_WINDOW`) ; le client 7.3 nomme la sienne `SMSG_OPEN_SOUL_REPAIR`
   (classe de message) et, côté interface, `SUISoulChargeWnd` / `window_SoulCharge.nui`. Écart de
   vocabulaire, pas de comportement ; réserve n° 1 (§7) sur le fichier de fenêtre.
5. **Lak.** NGemity ne connaît ni le mot ni la ressource ; le client, lui, affiche « You don't have
   enough Lak. » et « Consume #@lak@# Lak ». Rien n'est portable : la chaîne n'apparaît dans
   aucune source C# du dépôt (`grep -rn "Lak" Game/` : aucune occurrence).
6. **`EPIC = EPIC_4_1_1`** chez NGemity : le projet ne peut pas porter un paquet 7.3 par
   construction. Les deux seuls points où il sert ici sont l'ouverture (261, antérieure) et le
   modèle de garde de contact.

---

## 7. NON ÉTABLI

Chaque entrée nomme la question précise ; `navis-dev` ne doit rien deviner.

1. **Quel fichier de fenêtre ouvre réellement 261.** Établi : la classe de message
   `SMSG_OPEN_SOUL_REPAIR` (vtable → RTTI) et l'existence de `window_SoulCharge.nui` /
   `SUISoulChargeWnd` / `UIMSG_SOULCHARGE_MOVEITEM`. **Non établi** : le lien de code entre le
   message d'ouverture et l'ouverture de ce `.nui` précis. Lecture retenue : `SUISoulChargeWnd`
   (trois noms portent la même identité « SoulCharge ») ; l'alternative nommée est `SUIRepairWnd`
   (dump l. 44328, `window_main_inventory_repair.nui`, l. 24723), qui appartient par son nom au
   couple 263/264 (durabilité éthérée). Le nom de fichier n'a **aucune** conséquence sur la trame.
2. **Nature des six slots.** Établi : six slots homogènes, dans l'ordre de la fenêtre, `0` = vide.
   **Non établi** : chaque slot désigne-t-il un **équipement** (dont les pierres serties sont
   rechargées — lecture suggérée par `Recharge EQ`, `Choose all Equipped Items` et
   `#@item_name@# is out of Soul Power. … its socketed Soul Stones.`) ou une **pierre d'âme**
   elle-même ? Un même objet peut-il occuper deux slots ? Ces questions décident de la validation.
3. **Le coût et la formule.** Aucun champ de la trame ne porte de montant ; le client affiche
   `Required` / `Consume #@lak@# Lak to recharge some Soul Power.` **Non établi** : ce qu'est
   « Lak » (objet ? monnaie ?), le montant, et le lien entre montant et « Current Soul Power ».
   Aucune table du dépôt ne contient Lak (`grep`) et aucune colonne `soul*` n'existe
   (`ArcadiaSchemaPSQL.sql`).
4. **Où vit la « Soul Power » d'une pierre.** Aucune colonne `soul*` dans le schéma ; les champs
   voisins sont `EtherealDurability` / `Endurance` (`ItemEntity.cs:30-31`). **Quelle colonne
   (existante ou à créer) porte la durabilité de la pierre — et comment le client l'apprend-il
   après un rechargement réussi ?** Sans réponse, un moteur ne peut rien changer durablement.
5. **La réponse attendue après un rechargement réussi.** Aucun paquet de résultat dans la famille
   (rzu), aucun chez NGemity, et le seul message entrant lié au rechargement chez le client est 261
   (sans corps). **Faut-il renvoyer 261 (rafraîchir la fenêtre), un `TM_SC_INVENTORY`, les deux, ou
   rien ?** Non tranchable par lecture ici.
6. **La trame 264 dans ce client.** Un constructeur de trame à id `0x108` (264, 11 octets, avec
   `fld1`) existe (`0x5eae5b-0x5eae8f`) ; en revanche **aucun** constructeur de trame à id `0x107`
   (263) n'a été trouvé dans cette passe. Les constantes `0x107` rencontrées sont sans rapport :
   jetons du préprocesseur du client (`0x861419`, comparé aux chaînes `include` / `ifdef` / `ifndef`
   de `0xa8dc48-0xa8dc57`) et un diviseur (`0x676d2c`). Les seuls ids de trame écrits en dur dans le
   voisinage sont `0x104` = 260 (`0x48cee5`), `0x106` = 262 (`0x48cf48`) et `0x108` = 264
   (`0x5eae5b`). **Le client 7.3 émet-il vraiment 263 ?** Question ouverte, utile au socle (§3.5),
   non tranchée ici faute d'avoir suivi l'émetteur.
7. **Une seconde fonction du client construit les ids 256, 259, 260, 261 et 263 par un helper commun
   (`0x83b4ca` → `0x838360`) mais pas 262** (`0x83b4f2-0x83b580`). Le helper n'a pas été tracé :
   **non établi** qu'il s'agisse d'envois au serveur, et donc que le client puisse émettre 261
   lui-même (rzu le déclare `Server`). À trancher si un jour le lobe d'ouverture s'en approche.
8. **Le seuil de reconstruction du message.** Le message d'interface porte un champ `+0xb` = `0x13`
   (19) et un champ `+0x4` = l'id ; leur rôle exact (taille de charge ? type ?) n'est pas établi et
   n'a pas d'incidence sur la trame, qui est écrite par l'émetteur.

---

## 8. Commits épinglés et méthode de reproduction

| Référence | Commit / empreinte | Rôle ici |
| --- | --- | --- |
| `Navislamia` | `b56967a07430422add88e0e5cdf292b41b18f6c6` (`master`, = `origin/master`) | base de la branche de cette fiche |
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | trame, ordre des champs, gating, ids |
| `reference/ngemity` (Chihiro) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | ouverture de fenêtre, garde de contact, absence de logique |
| `reference/client73/SFrame.exe` | `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 o) | dispatch, trames, RTTI, textes de widgets |
| `reference/client73/db_string.rdb` | `sha256 4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` | textes de fenêtre et d'échec |
| `docs/packet-specs/260-soulstone-craft.md` | branche `hermes/packet-260-soulstone-craft` | fiche sœur du même lobe |
| `docs/packet-specs/socle-artisanat-objets.md` | `master` | socle (id, taille, sentinelles) |

Reproduction (aucune exécution de binaire ni de script client) :

```bash
cd /srv/navislamia/reference/client73
sha256sum SFrame.exe                                   # 41e0af2e…
objdump -d -M intel SFrame.exe           > /tmp/sf.asm  # 2 256 750 lignes
strings -a SFrame.exe                    > /tmp/sf.dump # 63 099 entrées (md5 7d92f691a46e6768d4cf00e85747e776)
strings -a db_string.rdb                 > /tmp/dbs.dump # 255 814 entrées (md5 b7df20cda8e13491e8e6bd9c348dc1fb)
# (les deux dumps de cette fiche ont été produits avec `strings -n 4`, strictement identique à
#  `strings -a` : mêmes comptes et mêmes md5 que les dumps du socle)

# trame 262 : constructeur et émetteur
objdump -d --start-address=0x48cf10 --stop-address=0x48cf60 -M intel SFrame.exe
objdump -d --start-address=0x48dad0 --stop-address=0x48db50 -M intel SFrame.exe
grep -n "call   0x48dad0" /tmp/sf.asm                 # une seule occurrence : 0x49e349

# dispatch d'interface (clé 1142)
objdump -d --start-address=0x49e21d --stop-address=0x49e240 -M intel SFrame.exe
objdump -s --start-address=0x49EA50 --stop-address=0x49EB2F SFrame.exe   # table d'octets
objdump -s --start-address=0x49E98C --stop-address=0x49EA50 SFrame.exe   # table de sauts

# construction du message d'interface (six slots)
objdump -d --start-address=0x56b2b0 --stop-address=0x56b350 -M intel SFrame.exe

# entrée serveur → client (259 et 261)
objdump -d --start-address=0x67df5d --stop-address=0x67e210 -M intel SFrame.exe
objdump -d --start-address=0x66fc20 --stop-address=0x66fce0 -M intel SFrame.exe
```

Les dumps `strings` de cette fiche ont les **mêmes empreintes** que ceux du socle
(`socle-artisanat-objets.md` §10.4) : les numéros de ligne cités ici sont donc directement
comparables à ceux des fiches antérieures.

---

## A VERIFIER PAR KILLIAN

Aucun de ces points ne bloque la livraison documentaire : ce sont des décisions de jeu ou des
arbitrages d'infrastructure que le corpus ne permet pas de trancher.

1. **Nature des six slots de 262** (§7.2) : équipements dont les pierres sont rechargées, pierres
   elles-mêmes, ou les deux ? La lecture retenue (six équipements, `Recharge EQ` /
   `Choose all Equipped Items`) engage la validation.
2. **Coût et formule** (§7.3) : montant en Lak, unité et règle (« Recharge all items to 100% »
   suggère un remplissage complet). Aucune source dans le corpus.
3. **Stockage de la Soul Power** (§7.4) : quelle colonne porte la durabilité d'une pierre, et le
   client l'apprend-il par 261 ou par un inventaire ? Prérequis d'un moteur.
4. **Réponse après succès** (§7.5) : 261, inventaire, les deux, rien ?
5. **Id d'ouverture pour le lobe de sertissage** : cette fiche établit que le client 7.3 **reçoit**
   259 (classe `SMSG_OPEN_JEWEL_EQUIP`) et 261 (`SMSG_OPEN_SOUL_REPAIR`), chacun par un id unique.
   259 ne contredit donc pas `TM_CS_DONATE_REWARD` sur le fil (sens opposés), mais l'entrée
   d'`op_codes.md:86` reste ambiguë pour un tableau plat comme `GamePackets` : faut-il ajouter
   `TM_SC_SHOW_SOULSTONE_CRAFT_WINDOW = 259` et `TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW = 261` au
   dispatch **sortant** du dépôt, avec un renommage documenté ?
6. **263 est-il réellement émis par le client 7.3 ?** (§7.6). Si non, la trame de 11 octets du
   socle §3.5 décrit un paquet que le client n'envoie pas — à vérifier avant d'écrire des tests.

---

## Bloc prêt à coller dans `CLAUDE.md` (par `navis-qa`, dans la description de la MR)

`CLAUDE.md` pointe déjà vers `docs/packet-specs/`. Le dev n'écrit pas `CLAUDE.md` lui-même
(fichier d'instructions protégé) : ce bloc est destiné à la description de la MR.

### Paquet 262 — `TM_CS_REPAIR_SOULSTONE` (rechargement des pierres d'âme)

- **Fiche** : `docs/packet-specs/262-repair-soulstone.md`.
- **Trame 7.3, fixe 31 octets** : en-tête 7 (comme partout), puis **six `uint32` LE** aux offsets
  **7, 11, 15, 19, 23, 27**. Aucun champ de comptage : une trame partiellement remplie a la même
  taille, les slots vides valant `0`. Le client place les six slots de la fenêtre de rechargement
  dans **cet ordre** (`0x56b2ec-0x56b31c`) et **n'émet jamais** une trame à six zéros
  (prédicat `0x56b270-0x56b2ae`).
- **Les six handles sont de même nature** : le client les traite identiquement, contrairement à 260
  qui distingue une cible (argument de la fonction) de quatre pierres. L'hypothèse « 1 objet + 5
  matériaux » est écartée.
- **Gating** : id `262` pour `version < EPIC_9_6_3` (`1262` ensuite) ; **aucun champ gaté**.
- **Ouverture de la fenêtre : `TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW` = 261**, trame de **7 octets sans
  corps**, confirmée par le client (le constructeur du message d'ouverture ne lit rien dans le
  paquet). Le client route 259 → `SMSG_OPEN_JEWEL_EQUIP` et 261 → `SMSG_OPEN_SOUL_REPAIR`, chacune
  par un id unique : 262 et 261 sont donc bien deux paquets distincts, et 262 n'est atteignable
  qu'après l'ouverture.
- **Garde de contact** : la référence (NGemity) arme `SetLastContact("RepairSoulStone", 1)` dans le
  message d'ouverture (`Messages.cpp:939`) ; elle n'a **aucun** gestionnaire pour 262. Le point
  d'armement appartient au lobe « ouverture de fenêtre ».
- **Réponse** : aucune source n'en décrit une (pas de paquet de résultat dans la famille, ni chez
  rzu ni chez NGemity). Le refus `InvalidArgument` du socle reste la seule réponse justifiable
  tant que le coût en Lak, la formule et le stockage de la Soul Power ne sont pas tranchés.
