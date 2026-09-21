# Socle « artisanat et enchantement d'objets » — inventaire des paquets (Epic 7.3)

Fiche établie par `navis-ref` (archéologue de protocole), branche
`hermes/packet-socle-artisanat-objets`, depuis `master`
`ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (HEAD relevé le 21/09/2026).

Cette fiche ne contient **aucun** changement de code serveur : elle fixe les formats, le gating
de version, le comportement attendu, l'état réel de `master` et le **découpage** qui dit à
`navis-dev` ce qu'il peut écrire sans trancher à la place de Killian.

Carte Trello : `trello.com/c/ekribUhu` — suivi `navislamia:socle:artisanat-objets`.

Références épinglées (voir §10) :

| Référence | Commit | Autorité |
| --- | --- | --- |
| `rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | tailles, ordre des champs, gating par version |
| NGemity / Chihiro | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | logique, sous réserve de sa version |
| Navislamia (`master`) | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | état du dépôt |
| `reference/client73` | *pas de SHA — dossier de ressources* | lecture statique seule (§2, §7) |

---

## 1. Identité — les ids de la famille

`op_codes.md` est une table Lua `local opcode_names = { … }` (`op_codes.md:1`), 278 lignes, à
laquelle le dépôt se réfère déjà dans ses fiches (`docs/packet-specs/203-drop-item.md:11`,
`253-use-item.md:14`, `550-get-region-info.md:21`, `1202-emotion.md:20`).

| id | `op_codes.md` | ligne | rzu (`librzu/src/packets/GameClient/`) | sens rzu | taille 7.3 |
| --- | --- | --- | --- | --- | --- |
| **256** | `TM_CS_MIX` | `op_codes.md:83` | `TS_CS_MIX.h` | Client | **15 + 6N** |
| **257** | `TM_SC_MIX_RESULT` | `op_codes.md:84` | `TS_SC_MIX_RESULT.h` | Server | **11 + 4N** |
| 258 | `TM_CS_DONATE_ITEM` | `op_codes.md:85` | `TS_CS_DONATE_ITEM.h` | Client | *hors périmètre* |
| 259 | `TM_CS_DONATE_REWARD` | `op_codes.md:86` | `TS_CS_DONATE_REWARD.h:16-18` | Client | *hors périmètre* |
| **260** | `TM_CS_SOULSTONE_CRAFT` | `op_codes.md:88` | `TS_CS_SOULSTONE_CRAFT.h` | Client | **27** |
| **261** | `TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW` | `op_codes.md:89` | `TS_SC_SHOW_SOULSTONE_REPAIR_WINDOW.h` | Server | **7** |
| **262** | `TM_CS_REPAIR_SOULSTONE` | `op_codes.md:90` | `TS_CS_REPAIR_SOULSTONE.h` | Client | **31** |
| **263** | `TM_CS_TRANSMIT_ETHEREAL_DURABILITY` | `op_codes.md:91` | `TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h` | Client | **11** |
| **264** | `TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT` | `op_codes.md:92` | `TS_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT.h` | Client | **11** |
| **259 ?** | *absent* | — | `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW.h` | Server | 7 — **id NON ÉTABLI**, voir ci-dessous |

Les neuf lignes `op_codes.md:83-92` sont **identiques, nom pour nom**, à l'énumération de
NGemity `shared/Server/ClientPackets.h:96-105`, à une exception près (le doublon 259, ci-dessous).
Les noms 256/257/263/264 sont aussi présents tels quels dans la table d'annotation du client 7.3
(dump `strings -a SFrame.exe`, lignes 26370-26373 : voir §2).

### 1.1 Le doublon 259 — trouvé dans les deux références

rzu et NGemity déclarent **deux paquets différents sur le même id 259** :

| Fichier | déclaration | source |
| --- | --- | --- |
| `TS_CS_DONATE_REWARD.h` | `X(259, version < EPIC_9_6_3)` / `X(1259, …)` | `reference/rzu/librzu/src/packets/GameClient/TS_CS_DONATE_REWARD.h:16-18` |
| `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW.h` | `X(259, version < EPIC_9_6_3)` / `X(1259, …)` | `…/TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW.h:7-9` |
| `ClientPackets.h` (NGemity) | `TS_CS_DONATE_REWARD = 259` **et** `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW = 259` | `reference/ngemity/shared/Server/ClientPackets.h:99-100` |

Les deux définitions vivent dans le **même** répertoire `packets/GameClient/` de rzu : l'espace
d'ids est unique, client et serveur confondus, donc la collision est réelle dans le modèle de rzu
et non un artefact de deux espaces séparés. Côté NGemity la collision est silencieuse : les deux
énumérateurs d'un `enum` C++ peuvent porter la même valeur.

`op_codes.md:86` tranche pour `TM_CS_DONATE_REWARD = 259` et **ne connaît aucun id** pour la
fenêtre de sertissage (`grep -n "SOULSTONE\|SHOW" op_codes.md` → seules les lignes 88-92, où 259
n'apparaît pas). Conséquence pour ce socle :

> **`TM_SC_SHOW_SOULSTONE_CRAFT_WINDOW` n'a AUCUN id établi en 7.3.** Il n'est donc pas
> ajoutable à `GamePackets`, et **l'ouverture de la fenêtre de sertissage n'a aucun paquet
> prouvé** (§9.4). C'est une des raisons pour lesquelles 260 ne peut pas être câblé de bout en
> bout par cette chaîne.

`TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW = 261`, lui, est **cohérent** entre `op_codes.md:89` et
`TS_SC_SHOW_SOULSTONE_REPAIR_WINDOW.h:7-9` : son id est établi.

---

## 2. Ce que le joueur fait pour que le client l'envoie (client 7.3 seul)

Méthode de lecture (aucun Lua ni binaire exécuté, `reference/README.md` : « Aucun binaire ni
script client exécuté ») :

```
strings -a db_string.rdb  > /tmp/db_string.dump     # 255 814 entrées
strings -a SFrame.exe     > /tmp/sframe.dump        #  63 099 entrées
```

Les numéros de ligne cités ci-dessous renvoient à **ces deux dumps**, dans l'ordre du fichier.
`reference/client73` n'est pas un dépôt git : on cite le fichier de ressource et la méthode,
jamais un SHA de client (il n'en existe pas). Le dossier ne contient ni `.nfe`/`.nfa` ni donnée
de carte.

### 2.1 256 — `TM_CS_MIX` : la fenêtre « combination »

Le client possède l'interface de combinaison et ses ressources :

| Preuve | Source |
| --- | --- |
| `window_main_inventory_mix.nui`, `window_main_inventory_mixlist.nui` | `sframe.dump:24848` |
| `button_mix`, `button_mixlist`, `scrollbar_mix` | `sframe.dump` (mots-clés d'interface) |
| `db_MixCategory.rdb` chargé par le client | `sframe.dump:19594` |
| `MSG_MIX_RESULT` — gestionnaire de la réponse 257 | `sframe.dump:25151` ; RTTI `.?AUSMSG_MIX_RESULT@@` à `sframe.dump:44397` |
| `text_mix_formula_content`, `text_mix_result_content` | `db_string.dump` (libellés de la fenêtre de mix) |
| `<#99ff66>Combination complete!<7.2>` / `<#b92c36>Combination failed!<7.2>` | `db_string.dump:189788` |

Les textes de **formule** portés par les objets donnent la forme exacte du geste : un
**Target Slot** (l'objet à traiter) plus des **Material Slot 1…N** (les consommables). Exemple
littéral, `db_string.dump:230197` :

> `Target Slot : Weapon to enhance` + `Material Slot 1 : 1 Cube (Strike) of same rank as weapon`
> + `Material Slot 2 : 1 E-Protect Powder` (`db_string.dump:230207`)

et pour les pierres d'âme, `db_string.dump:198039` (voir §2.2). Correspondance retenue :
**Target Slot = `main_item`**, **Material Slots = `sub_items`** (ordre de déclaration
`TS_CS_MIX.h:14-17`). La borne haute de N est corroborée par les 9 couples de matériaux de la
table de référence (§5.3, §6.1) et par la garde de NGemity ; la borne exacte du client 7.3
(9 ? autre ?) n'est pas prouvée ici → §7 « NON ÉTABLI ».

Le déclencheur d'écriture du paquet n'est pas une chaîne explicite : la table d'annotation du
client donne le **nom** du paquet (`TM_CS_MIX`, `sframe.dump:26373`) mais pas la fonction
d'émission, qui vit dans le code compilé. Même réserve que pour 203 (fiche
`docs/packet-specs/203-drop-item.md` §7.1).

### 2.2 260 — `TM_CS_SOULSTONE_CRAFT` : la fenêtre de sertissage

Le geste est décrit mot pour mot par le client (`db_string.dump`), et il **exclut** l'idée d'une
fabrication de pierre :

| Texte client | Source |
| --- | --- |
| `Soul Stone Sockets` | `db_string.dump:112380` |
| `Socket my Equipment.` / `Socketing` | `db_string.dump:242715` |
| `Only Soul Stones can be socketed.` | `db_string.dump:198041` |
| `Socketing cost is <B>#@cost@#</B>.<br>Would you like to socket the item?` | `db_string.dump:198039` |
| `You can not have more than 2 Soul Stones socketed in the same weapon that increase the same stat.` | `db_string.dump:198045` |
| `There is already a Soul Stone in this socket.` | `db_string.dump:198037` |
| `I can socket your soul stones for you, for a price. Just tell me what you need.` (PNJ) | `db_string.dump:84250` |
| RTTI `.?AUSIMSG_UI_SOULSTONE_CRAFT@@` | `sframe.dump:44021` |

Deux faits en découlent, tous deux vérifiés **indépendamment** côté NGemity (§6.2) :
4 châsses, et la règle « pas plus de 2 pierres du même stat sur une arme à 4 châsses ».

**Le nom du paquet n'est PAS dans la table d'annotation du binaire** : `grep -n
"^TM_CS_SOULSTONE_CRAFT$" sframe.dump` → **rien** (la table ne compte que 173 entrées `^TM_`,
alors que rzu déclare 219 ids distincts en bande basse). C'est une table **partielle**, comme
déjà relevé pour 203. La même absence vaut pour `TM_CS_REPAIR_SOULSTONE` (262) et pour
`TM_SC_SHOW_SOULSTONE_*` (259/261).

### 2.3 262 — `TM_CS_REPAIR_SOULSTONE` : geste NON ÉTABLI

Aucun texte, aucune entrée `TM_`, aucun gestionnaire NGemity, aucun commentaire rzu. Ce qui est
établi s'arrête à la trame (§3.4). Voir §7.

### 2.4 263 / 264 — la durabilité éthérée : deux boutons, tous deux estampillés `<7.2>`

Le client porte l'aide de la fenêtre d'inventaire « repair », qui décrit les deux gestes
`db_string.dump:222403` :

> `The Ethereal Stone Slot` — *« This slot allows you to keep track of how many points you have
> saved that can be used to restore durability to your items. »*
> `Charging the Ethereal Stone` — *« 1. Place any equipment into your Materials Slots.
> 2. Click the "Charge Stone" button. - These items will be destroyed! »*
> `Repairing an Item's Durability` — *« 1. Place the item you want to repair in the Target Slot.
> 2. Click the repair button. »*
> `Repairing Current Equipment` — *« You can repair all of your currently equipped gear by
> clicking the "Repair All Items" button in your inventory above your Mask Slot. »*

| Preuve | Source |
| --- | --- |
| `Charge Stone<7.2>` | `db_string.dump:189800` |
| `Ethereal Stone required!<7.2>` | `db_string.dump:189826` |
| `The Charge Stone charges the Ethereal Stone by extracting the durability of equipment` — formule `Ethereal Stone/Big Ethereal Stone (Main Slot) + Equipment (Consumed) + Ethereal Charge Stone (Consumed)` | `db_string.dump:137609` |
| `The Recovery Stone restores the lost durability of equipment when used with the Ethereal Stone.` — formule `Equipment in need of repairs (Main Slot) + Recovery Stone(Consumed) + Ethereal Stone/Big Ethereal Stone` | `db_string.dump:137611` |
| `You have charged the Ethereal Stone with #@Ethereal_Durability@# Ethereal. Durability points were extracted from the equipment item.` | `db_string.dump:151304` |
| Pierre éthérée : `You cannot recharge the Ethereal stone exceeding 1000.` / `… 10000.` ; `Durability #@Ethereal_Durability@# / 1000` | `db_string.dump:137571` et `:137583` |
| `TM_CS_TRANSMIT_ETHEREAL_DURABILITY`, `TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT` | `sframe.dump:26371`, `:26370` |
| `window_main_inventory_repair.nui`, `window_main_inventory_sub_repairviewer.nui`, RTTI `.?AVSUIRepairWnd@@` | `db_string.dump` ; `sframe.dump:44328` |
| `#@Ethereal_Durability@# / #@Ethereal_Durability_Max@#` (tooltip d'objet) | `db_string.dump` |

Lecture retenue, et elle est univoque côté client :

- **263** (une seule `handle`) = « Charging the Ethereal Stone » : *extraire* la durabilité d'**un**
  objet d'équipement (consommé) vers l'Ethereal Stone. Le handle est donc celui de l'équipement
  sacrifié.
- **264** (`rate` float) = « Repairing an Item's Durability » / « Repair All Items » : la
  *restitution*, depuis la pierre chargée vers l'équipement, proportionnée par `rate`. Le montant
  rechargé « increases in proportion to the item's store price » (`db_string.dump:137571`).
- Les deux sont **marqués `<7.2>` dans le client**, ce qui recoupe exactement le `// Since
  EPIC_7_2` de rzu (`TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h:8`,
  `…_TO_EQUIPMENT.h:11`).

### 2.5 257 / 261 — pas de geste joueur : ce sont des paquets descendants

`TS_SC_MIX_RESULT.h:22` et les deux fenêtres sont déclarés `SessionPacketOrigin::Server` : le
client ne les émet pas. Le joueur n'a rien à faire pour les provoquer, ils sont la **réponse**.
Les trois membres correspondants restent néanmoins concernés par la règle « enum et dispatch
ensemble » (§9.6).

---

## 3. Structure sur le fil (source par champ)

### 3.0 Conventions, toutes sourcées

L'en-tête est de **7 octets** : `uint32 Length`, `uint16 ID`, `byte Checksum`
(`Game/Network/Packets/Header.cs:9-11` ; `CLAUDE.md:48-50`). `Length` **compte l'en-tête** :
`Packet<T>.Serialize` écrit `HeaderStruct.Length = (uint)Data.Length` avec
`Data = new byte[7 + payload]` (`Game/Network/Packets/Packet.cs:31`, `:59`). Les offsets
ci-dessous sont donc absolus depuis le début de la trame.

| Convention rzu | Effet | Source |
| --- | --- | --- |
| `ar_handle_t` = `strong_typedef<ar_handle_t, uint32_t>` | tout handle sur le fil = **4 octets LE** | `reference/rzu/librzu/src/lib/Packet/GameTypes.h:40-42` |
| `_(simple)(T, f)` | `sizeof(T)` octets, à sa position de déclaration | — |
| `_(count)(type, ref)` | **n'écrit aucun champ de structure** ; sérialise `sizeof(type)` octets à cet endroit, valeur = taille réelle du tableau, bornée par `numeric_limits<type>::max()` | `PacketDeclaration.h:145-157` (`DEFINITION_F_count`), `:247-249` (`SIZE_F_COUNT2`), `:343-345` (`SERIALIZATION_F_COUNT2` → `buffer->writeSize`), `:83-88` (`getClampedCount`) |
| `_(dynarray)(T, ref)` | `ref_size` éléments de `sizeof(T)`, `ref_size` venant du champ `_(count)` | `PacketDeclaration.h:235-241`, `:448` (`readSize`) |
| `_(array)(T, ref, N)` | N éléments **fixes** de `sizeof(T)` | — |

Conséquence pratique pour 256 : le champ `_(count)(uint16_t, sub_items)` **n'est pas** le
`count` de `TS_MIX_INFO` ; c'est la longueur du tableau `sub_items`, écrite par l'émetteur.
NGemity lit `pRecvPct->sub_items.size()` et ignore donc ce champ (rzu reconstruit la taille au
décodage) — mais la taille est bien **présente sur le fil** et doit être consommée.

### 3.1 256 — `TM_CS_MIX` (variable, 15 + 6N)

rzu `TS_CS_MIX.h:7-17` :

| Offset 7.3 | Type | Champ | Source |
| --- | --- | --- | --- |
| 7 | `uint32` LE | `main_item.handle` | `TS_CS_MIX.h:8` (`ar_handle_t handle`) |
| 11 | `uint16` LE | `main_item.count` | `TS_CS_MIX.h:9` (`uint16_t count`) |
| 13 | `uint16` LE | *compte de `sub_items`* | `TS_CS_MIX.h:16` (`_(count)(uint16_t, sub_items)`) |
| 15 + 6i | `uint32` LE | `sub_items[i].handle` | `TS_CS_MIX.h:15` + `:8` |
| 19 + 6i | `uint16` LE | `sub_items[i].count` | `TS_CS_MIX.h:15` + `:9` |

`TS_MIX_INFO` = 6 octets (`TS_CS_MIX.h:7-11`). `N = 0` → **15 octets** ; `N = 9` → **69**.
`main_item.handle == 0` est une valeur **légitime** : NGemity teste explicitement
`if (pRecvPct->main_item.handle != 0 && pMainItem == nullptr) return;`
(`WorldSession.cpp:1451-1453`) — un mix sans objet principal est un cas prévu.

### 3.2 257 — `TM_SC_MIX_RESULT` (variable, 11 + 4N)

rzu `TS_SC_MIX_RESULT.h:13-16` :

| Offset 7.3 | Type | Champ | Source |
| --- | --- | --- | --- |
| 7 | `uint32` LE | *compte de `handles`* | `TS_SC_MIX_RESULT.h:14` (`_(count)(uint32_t, handles)`) |
| ~~11~~ | — | ~~`type` (`TS_MIX_TYPE`, int8)~~ | **ABSENT en 7.3** — `TS_SC_MIX_RESULT.h:15` `, version >= EPIC_8_1` |
| 11 + 4i | `uint32` LE | `handles[i]` | `TS_SC_MIX_RESULT.h:16` (`_(dynarray)(ar_handle_t, handles)`) |

`N = 0` → **11 octets** (c'est ce que NGemity envoie en cas d'échec, §6.1) ; `N = 1` → **15**
(le handle de l'objet principal, cas de succès).

### 3.3 260 — `TM_CS_SOULSTONE_CRAFT` (fixe, 27 octets)

rzu `TS_CS_SOULSTONE_CRAFT.h:5-7` :

| Offset | Type | Champ | Source |
| --- | --- | --- | --- |
| 7 | `uint32` LE | `craft_item_handle` | `TS_CS_SOULSTONE_CRAFT.h:6` |
| 11 | `uint32` LE | `soulstone_handle[0]` | `TS_CS_SOULSTONE_CRAFT.h:7` (`_(array)(ar_handle_t, …, 4)`) |
| 15 | `uint32` LE | `soulstone_handle[1]` | idem |
| 19 | `uint32` LE | `soulstone_handle[2]` | idem |
| 23 | `uint32` LE | `soulstone_handle[3]` | idem |

**Taille totale 27 octets, fixe.** `GetSocketIndex`/`SetSocketIndex` sont indexés 0..3
(NGemity `ItemInstance.h:89` ; repo `TelecasterContext.cs:55`
`HasMaxLength(4)`), donc 4 châsses maximum. Un handle à **0** signifie « châsse laissée vide » :
NGemity teste `pRecvPct->soulstone_handle[i] != 0` (`WorldSession.cpp:1521`). La trame est donc
toujours de 27 octets, jamais tronquée aux pierres réellement fournies.

### 3.4 262 — `TM_CS_REPAIR_SOULSTONE` (fixe, 31 octets)

rzu `TS_CS_REPAIR_SOULSTONE.h:5-6` :

| Offset | Type | Champ | Source |
| --- | --- | --- | --- |
| 7 | `uint32` LE | `item_handle[0]` | `TS_CS_REPAIR_SOULSTONE.h:6` (`_(array)(ar_handle_t, item_handle, 6)`) |
| 11 | `uint32` LE | `item_handle[1]` | idem |
| 15 | `uint32` LE | `item_handle[2]` | idem |
| 19 | `uint32` LE | `item_handle[3]` | idem |
| 23 | `uint32` LE | `item_handle[4]` | idem |
| 27 | `uint32` LE | `item_handle[5]` | idem |

**Taille totale 31 octets, fixe.** Le nom du champ est `item_handle`, pas `soulstone_handle`, et
le compte (6) **ne correspond pas** aux 4 châsses de 260 ni aux 6 emplacements de l'énumération
`EnhanceType` (`Game/DataAccess/Entities/Enums/EnhanceType.cs:5-14` liste Weapon…Skill = 9
valeurs, dont 9 renseignées). Aucun commentaire rzu, aucun texte client, aucun gestionnaire
NGemity : **la sémantique du tableau est NON ÉTABLIE** (§7). Ce qui est établi est la trame.

### 3.5 263 — `TM_CS_TRANSMIT_ETHEREAL_DURABILITY` (fixe, 11 octets)

rzu `TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h:5-6`, `:8` :

| Offset | Type | Champ | Source |
| --- | --- | --- | --- |
| 7 | `uint32` LE | `handle` | `TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h:6` |

**Taille totale 11 octets.** Aucun gating de champ ; le paquet lui-même n'existe qu'à partir
d'`EPIC_7_2` (`:8`).

### 3.6 264 — `TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT` (fixe, 11 octets en 7.3)

rzu `TS_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT.h:5-11` :

| Offset 7.3 | Type | Champ | Source |
| --- | --- | --- | --- |
| 7 | `float` LE (IEEE-754) | `rate` | `…_TO_EQUIPMENT.h:6` (`_(simple)(float, rate)`) |
| ~~11~~ | — | ~~`target` (int8)~~ | **ABSENT en 7.3** — `…_TO_EQUIPMENT.h:7` `, version >= EPIC_8_1, 0` |

**Taille totale 11 octets en 7.3** (12 à partir d'`EPIC_8_1`). Le commentaire de rzu
`// target = 0 for player or X for summon (1 to 6)` (`:9`) et la valeur par défaut `0` (`:7`)
situent le champ : il n'existe qu'à partir de la 8.1, donc **en 7.3 le paquet ne désigne
implicitement que le joueur**. Un `float` de 4 octets, jamais un entier.

### 3.7 259 / 261 — les deux fenêtres (fixes, 7 octets)

`TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW.h:5` et `TS_SC_SHOW_SOULSTONE_REPAIR_WINDOW.h:5`
définissent une macro `_DEF` **vide** : aucun champ. Ce sont des trames **en-tête seul**
(7 octets), comme déjà rencontrées pour 23/25/27 (`CLAUDE.md:52-56`) — donc la garde de boucle
doit être `remainingData >= Marshal.SizeOf<Header>()`, pas `>`. L'id de 261 est établi (§1.1) ;
celui de la fenêtre de sertissage ne l'est pas.

---

## 4. Taille totale attendue de chaque paquet (7.3, en-tête de 7 octets compris)

| id | Nom | Forme | Taille | Cas nominal |
| --- | --- | --- | --- | --- |
| 256 | `TM_CS_MIX` | variable | **15 + 6N** (N = nombre de `sub_items`) | N = 0 → 15 ; N = 2 (cube + powder) → 27 ; N = 9 → 69 |
| 257 | `TM_SC_MIX_RESULT` | variable | **11 + 4N** (N = nombre de handles) | échec → 11 ; succès sur l'objet principal → 15 |
| 260 | `TM_CS_SOULSTONE_CRAFT` | **fixe** | **27** | toujours 27, châsses vides à 0 |
| 261 | `TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW` | **fixe** | **7** | en-tête seul |
| 262 | `TM_CS_REPAIR_SOULSTONE` | **fixe** | **31** | toujours 31 |
| 263 | `TM_CS_TRANSMIT_ETHEREAL_DURABILITY` | **fixe** | **11** | 11 |
| 264 | `TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT` | **fixe** | **11** | 11 en 7.3 (12 à partir d'`EPIC_8_1`) |
| 259 ? | `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW` | fixe | 7 | **id NON ÉTABLI** |

Une taille fausse désaligne le lecteur en silence : c'est la raison d'être du test d'offsets
exigé par le critère 3 (§9.6).

---

## 5. Gating de version — décisions prises pour 7.3

`EPIC_7_3 = 0x070300` (`reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59`).
`EPIC_8_1 = 0x080100` (`:61`), `EPIC_9_6_3 = 0x090603` (`:96`). La comparaison de rzu porte sur
les 24 bits bas (`PacketEpics.h:8` `compare()`), donc `0x070300 < 0x080100 < 0x090603`.

### 5.1 Gating des identifiants

Les huit paquets de la famille portent tous le même remappage, sans exception :

```
X(256, version < EPIC_9_6_3)   X(1256, version >= EPIC_9_6_3)
```

déclaré séparément dans chaque en-tête (`TS_CS_MIX.h:19-21`, `TS_SC_MIX_RESULT.h:18-20`,
`TS_CS_SOULSTONE_CRAFT.h:9-11`, `TS_CS_REPAIR_SOULSTONE.h:8-10`,
`TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW.h:7-9`, `TS_SC_SHOW_SOULSTONE_REPAIR_WINDOW.h:7-9`,
`TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h:9-11`,
`TS_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT.h:12-14`).

**Décision 7.3 : les ids restent 256, 257, 260, 262, 263, 264 (+ 261).** Les valeurs `+1000`
sont postérieures à la 9.6.3 et hors sujet.

### 5.2 Gating de champs — les deux seuls de la famille, tranchés

| Paquet | Champ | Condition rzu | **Décision 7.3** | Source |
| --- | --- | --- | --- | --- |
| 257 | `type` (`TS_MIX_TYPE`, int8) | `version >= EPIC_8_1` | **ABSENT — 0 octet** | `TS_SC_MIX_RESULT.h:15` |
| 264 | `target` (int8) | `version >= EPIC_8_1` | **ABSENT — 0 octet** | `…_TO_EQUIPMENT.h:7` |

La provenance de ce gating est la pièce la plus forte du dossier : ces deux conditions ne sont
pas des déductions, elles ont été **introduites par la comparaison aux PDB officiels de chaque
epic**. Le commit `bdd362a6` (26/02/2017, « Update packets based on available GS pdbs (5.2, 6.1,
6.2, 7.1, **7.2, 7.3**, 7.4, 8.1) ») **retire** les deux champs :

```
- → _(simple)(int8_t, type)              sur TS_SC_MIX_RESULT.h
+ → _(simple)(TS_MIX_TYPE, type, version >= EPIC_8_1, MIX_TYPE_NONE)
- → _(simple)(int8_t, target)            sur …_TO_EQUIPMENT.h
+ → _(simple)(int8_t, target, version >= EPIC_8_1, 0)
+ → // target = 0 for player or X for summon (1 to 6)
+ → // Since EPIC_7_2                    (ajouté sur les deux paquets éthérés)
```

Vérification : `git show bdd362a6 -- '*TS_SC_MIX_RESULT.h' '*TS_CS_TRANSMIT_ETHEREAL_DURABILITY*'`,
et `git log -S "version >= EPIC_8_1" -- …/TS_SC_MIX_RESULT.h` ne renvoie que ce commit. **7.3 est
explicitement dans la liste des PDB utilisés** : cette décision est donc sourcée, pas inférée.

Ce même commit **ne touche pas** `TS_CS_MIX.h`, `TS_CS_SOULSTONE_CRAFT.h`,
`TS_CS_REPAIR_SOULSTONE.h` ni les deux fenêtres (`git show bdd362a6 -- <ces fichiers>` → vide) :
leurs formes 7.3 sont celles de la déclaration, sans gating de champ.

### 5.3 Le cas NGemity : `EPIC = EPIC_4_1_1`

`reference/ngemity/shared/Common/Define.h:25` : `#define EPIC EPIC_4_1_1`.

**NGemity compile en dessous d'`EPIC_7_2` et `EPIC_8_1`.** Il ne voit donc **aucun** des deux
gatings de champ, et ses deux paquets éthérés (263/264) sont *déclarés* dans son propre arbre
sans qu'aucun gestionnaire ne les référence. C'est pourquoi NGemity « confirme la branche basse »
pour les ids (256/257/260/262…) mais **ne peut rien confirmer** sur `type` et `target` : sa
version est plus basse, pas plus haute. La règle du profil s'applique ici en sens inverse de
d'habitude — pour 257 et 264, rzu tranche seul.

### 5.4 Les tables de référence ne portent aucun gating de version

`ArcadiaSchemaPSQL.sql` décrit le schéma d'une **version donnée** ; aucune ligne de `MixResource`
(`:471-582`) ni d'`EnhanceResource` (`:1345-…`) n'est conditionnée par un epic. Le contenu réel
des tables 7.3 n'est pas dans ce dossier → §7.

---

## 6. Traitement attendu (NGemity `Chihiro`, commit épinglé)

### 6.0 Ce que NGemity porte réellement

| Paquet | Gestionnaire NGemity | État |
| --- | --- | --- |
| 256 | `WorldSession.cpp:1445-1495` (`onMixRequest`) + `Crafting/MixManager.{h,cpp}` | **complet** |
| 257 | émission seulement : `Messages.cpp:910-922` (`SendMixResult`), `Messages.h:83` | émission |
| 259 | émission seulement : `Messages.cpp:943-948` (`ShowSoulStoneCraftWindow`) — **sur l'id 259 ambigu** | émission |
| 260 | `WorldSession.cpp:1497-1588` (`onSoulStoneCraft`) | **complet** |
| 261 | émission seulement : `Messages.cpp:936-941` (`ShowSoulStoneRepairWindow`) | émission |
| 262 | **aucun gestionnaire** (`grep -rn "TS_CS_REPAIR_SOULSTONE" Chihiro/ shared/` → uniquement l'en-tête et l'énumération) | **absent** |
| 263 | **aucun gestionnaire** (idem) | **absent** |
| 264 | **aucun gestionnaire** (idem) | **absent** |

Déclaration des bras de réception : `WorldSession.cpp:117` (`onMixRequest`), `:131`
(`onSoulStoneCraft`), prototype `WorldSession.h:115`. Aucun autre id de la famille n'y figure.
Note : `Messages::ShowSoulStoneRepairWindow`/`ShowSoulStoneCraftWindow` existent et posent un
`LastContact` (`Messages.cpp:939`, `:946`), mais **le seul appelant possible du 262 est absent** :
la fenêtre de réparation de pierres peut être ouverte par NGemity, la requête de réparation ne
peut pas y arriver.

### 6.1 256 — `onMixRequest` puis `MixManager`

**a. Garde de trame et refus** (`WorldSession.cpp:1445-1460`) :

| Condition | Comportement | Source |
| --- | --- | --- |
| `sub_items.size() > 9` | **`KickPlayer()`** — mur dur, aucune réponse | `WorldSession.cpp:1447-1450` |
| `main_item.handle != 0` et objet introuvable | retour **silencieux** (aucune réponse) | `:1451-1453` |
| sous-objet introuvable ou quantité insuffisante | retour **silencieux** | `:1457-1461` |
| aucun `MixResource` compatible | `TM_SC_RESULT` (0), `request_msg_id = 256`, `INVALID_ARGUMENT` (28), valeur 0 | `:1463-1466` |

`KickPlayer` sur un simple dépassement de borne est **disproportionné** : c'est un écart assumé
(§7). Le test `main_item.handle != 0` prouve que le handle 0 est une sentinelle légitime.

**b. Table `MixResource` → `MixBase`** — mapping **positionnel** `SELECT *`
(`Globals/ObjectMgr.cpp:1108-1145`), à confronter à l'ordre réel des colonnes du schéma
(`ArcadiaSchemaPSQL.sql:473-582`) :

| Source SQL (ordre) | Cible NGemity | Ligne |
| --- | --- | --- |
| `id` | `MixBase.id` | `ObjectMgr.cpp:1124` |
| `mix_type` | `MixBase.type` | `:1125` |
| `mix_value_01` … `mix_value_06` | `value[0..5]` (`MIX_VALUE_COUNT = 6`) | `:1126-1128` ; `MixManager.h:23` |
| `sub_material_count` | `sub_material_cnt` | `:1129` |
| `main_type_01`, `main_value_01` … `main_type_05`, `main_value_05` | `main_material.type[0..4]` / `.value[0..4]` (`MATERIAL_INFO_COUNT = 5`) | `:1130-1133` ; `MixManager.h:22`, `:66-69` |
| `sub01_type_01` … `sub09_type_05`, `sub09_value_05` | `sub_material[0..8].type/value[0..4]` (`MAX_SUB_MATERIAL_COUNT = 9`) | `:1134-1139` ; `MixManager.h:24`, `:77` |

L'ordre des colonnes d'`ArcadiaSchemaPSQL.sql:473-582` est **exactement** celui-ci, et il y a
bien **sub01…sub09** (9 × 5 paires). Le fichier porte 90 occurrences `subNN_*`, soit 9 × 10.

**c. Sémantique de `*_type_*` / `*_value_*` — la question du PO est tranchée**

`MixBase` déclare un `enum` de **20 codes** (`MixManager.h:79-103`) :

`CHECK_ITEM_GROUP=1`, `CHECK_ITEM_CLASS=2`, `CHECK_ITEM_ID=3`, `CHECK_ITEM_RANK=4`,
`CHECK_ITEM_LEVEL=5`, `CHECK_FLAG_ON=6`, `CHECK_FLAG_OFF=7`, `CHECK_ENHANCE_MATCH=8`,
`CHECK_ENHANCE_DISMATCH=9`, `CHECK_ITEM_COUNT=10`, `CHECK_ELEMENTAL_EFFECT_MATCH=11`,
`CHECK_ELEMENTAL_EFFECT_MISMATCH=12`, `CHECK_ITEM_WEAR_POSITION_MATCH=13`,
`CHECK_ITEM_WEAR_POSITION_MISMATCH=14`, `CHECK_ITEM_COUNT_GE=15`,
`CHECK_ITEM_ETHEREAL_DURABILITY_E=16`, `CHECK_ITEM_ETHEREAL_DURABILITY_NE=17`,
`CHECK_ITEM_GRADE=18`, `CHECK_SAME_ITEM_ID=19`, `CHECK_SAME_SUMMON_CODE=20`.

Lisibilité : **`*_type_i` est un code `CHECK_*`, `*_value_i` en est l'opérande**. Les 5 couples
d'un `MaterialInfo` sont 5 prédicats **cumulés** (`for i = 0..4` puis `switch`, sans `break` de
sortie), et `type[0] == 0` signifie « cette entrée ne sert pas » (`MixManager.cpp:348-349`).
`sub_material_count` doit être **égal** à `sub_items.size()`, sinon la ligne est ignorée
(`MixManager.cpp:245-246`).

Couverture réelle de `check_material_info` (`MixManager.cpp:345-450`) :

| Codes | État |
| --- | --- |
| 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 13, 14 | implémentés (`:356-403`, `:434-442`) |
| 11 (`CHECK_ELEMENTAL_EFFECT_MATCH`) | **corps commenté** → vérification **inerte** (`:405-418`) |
| 12 (`…_MISMATCH`) | **absent du `switch`** → `default: break`, inerte (`:443-444`) |
| 15, 16, 17, 18 | **absents du `switch`** → `default: break`, **inertes** (`:443-444`) |
| 19 (`CHECK_SAME_ITEM_ID`) | implémenté dans `post_arrange_check_material_info` (`:558-573`), mais la borne est écrite `if (nSlotIndex < 0 && nSlotIndex > nSubMaterialCount)` — un `&&` là où il faut un `||` : la garde ne se déclenche **jamais** (`:560`) |
| 20 (`CHECK_SAME_SUMMON_CODE`) | **refuse systématiquement** : `NG_LOG_DEBUG("… Not implemented yet"); return false;` (`:574-577`) |

Autrement dit : **six codes sur vingt sont déclarés mais inertes, un refuse toujours, un est mal
borné.** Pour 7.3, les codes 16/17/18 sont précisément ceux qui porteraient la durabilité
éthérée et le grade : leur inertie est un trou à combler par la fiche, pas un comportement à
porter.

**d. Répartition par `MIX_TYPE`** — l'`enum` complet est `MixManager.h:26-47` (20 valeurs,
101 → 803). Le `switch` de `onMixRequest` (`WorldSession.cpp:1468-1493`) n'en traite que cinq :

| `MIX_TYPE` | Valeur | Traitement NGemity | Source |
| --- | --- | --- | --- |
| `MIX_ENHANCE` | 101 | `EnhanceItem` (cube seul) | `:1469-1472` ; `MixManager.cpp:47-121` |
| `MIX_ENHANCE_SKILL_CARD` | 102 | `EnhanceSkillCard` (3 sous-matériaux, 2 cartes + 1 cube) | `:1473-1476` ; `MixManager.cpp:123-172` |
| `MIX_ENHANCE_WITHOUT_FAIL` | 103 | `EnhanceItem` (cube + powder) | `:1477-1480` |
| `MIX_ADD_LEVEL_SET_FLAG` | 311 | `MixItem` (pose `value[2]` en flag) | `:1481-1484` ; `MixManager.cpp:174-190` |
| `MIX_RESTORE_ENHANCE_SET_FLAG` | 501 | `RepairItem` (objet `ITEM_FLAG_FAILED` → `NORMAL`) | `:1485-1491` ; `MixManager.cpp:533-551` |
| *tout le reste* (201/202/211/212/301/302/401/402/601/701/702/**801/802/803**) | — | `default: break;` → le mix a été validé et **rien ne se passe**, sans réponse | `:1492-1494` |

**Les trois `MIX_TYPE` de la durabilité éthérée — `MIX_SACRIFICE_ITEM_FOR_ETHEREAL_DURABILITY = 801`,
`MIX_TRANSMIT_ETHEREAL_DURABILITY = 802`, `MIX_RECOVER_EXHAUSTED_ETHEREAL_DURABILITY = 803` —
sont déclarés et jamais traités** (`MixManager.h:44-46`). C'est le point d'articulation avec
263/264 (§6.4) : NGemity ne dit **pas** si la durabilité éthérée passe par 256 ou par 263/264.

**e. Calcul du taux et sort de l'objet** (`MixManager.cpp:82-116`) — c'est le cœur de la
politique de jeu :

- `itemEnhance = irand(value[1], value[2])` pour `MIX_ENHANCE`, **`1`** pour
  `MIX_ENHANCE_WITHOUT_FAIL` (`:82`) ;
- plafonné à `nMaxEnhance - enhance_actuel` (`:83-84`) ; refus si `<= 0` (`:86-89`) ;
- taux = `fPercentage[enhance_actuel] × 100000` puis `urand(0, 100000) <=` (`:96-99`) ;
- succès : `enhance += itemEnhance`, `TS_SC_INVENTORY` (207) puis `TS_SC_MIX_RESULT` (257) avec le
  handle de l'objet (`:100-105`) ;
- échec sans powder : `procEnhanceFail(pItem, nFailResult)` (`:109`) ;
- échec **avec** powder (`MIX_ENHANCE_WITHOUT_FAIL`) : `enhance -= 1`, objet **jamais détruit**
  (`:112-113`) puis 257 **vide** (`:115`).

`procEnhanceFail` (`MixManager.cpp:459-500`) :

| `nFailResult` | Effet | Source |
| --- | --- | --- |
| 0 | **forcé à 1** — « nFailResult is not calculated correctly atm » | `:461-465` |
| 1 `RESULT_FAIL` | `ITEM_FLAG_FAILED` + ***boucle infinie*** (voir ci-dessous) | `:467-476` |
| 2 `RESULT_SKILL_CARD_FAIL` | `enhance <= 3` → **objet détruit** ; sinon `enhance -= 3` | `:477-488` |
| 3 `RESULT_ACCESSORY_FAIL` | `enhance <= 3` → `enhance = 0` ; sinon `enhance -= 3` (jamais détruit) | `:489-499` |

> **Piège à ne pas porter.** `MixManager.cpp:469` écrit
> `for (int i = 0; MAX_SOCKET_NUMBER; ++i) { /* Implement set socket code here */ }` avec
> `MAX_SOCKET_NUMBER = 4` (`Entities/Item/ItemTemplate.hpp:9`). La condition est une constante
> non nulle et le corps est vide : **boucle infinie** (le compilateur peut l'éliminer, mais le
> code source ne dit pas ce qu'il faut faire). De plus, NGemity **ne vide aucun châsses** en cas
> d'échec d'enchantement — or le client dit explicitement le contraire (voir ci-dessous). C'est
> la deuxième raison (après `limit_*` et `SRT_ADD_HP` de `CLAUDE.md`) de relire NGemity avant de
> le porter. La ligne doit être remplacée par une décision explicite de Killian (§8).

Le client, lui, énonce le sort des châsses et la clé de protection
(`db_string.dump`) : *« E-Repair Powder lets you repair your equipment that has been broken from
a failed enchantment. […] The powder does not restore soul stones that had been socketed in the
item before the failed enchantment. »* — donc **l'enchantement raté détruit bien les châsses
existantes**, et la « clé E-Protect Powder » est un **objet** placé en `Material Slot 2`
(`db_string.dump:230207`), pas un drapeau. NGemity identifie ce powder **uniquement par
élimination** : `if (pSubMaterial[0] is TYPE_CUBE) pPowder = pSubMaterial[1]; else { pPowder =
pSubMaterial[0]; pCube = pSubMaterial[1]; }` (`MixManager.cpp:63-71`) — **aucun contrôle de
`item_code` sur le powder** ; seul le cube est comparé à `EnhanceResource.need_item` (`:73`). Le
powder n'est donc *pas* décrit par les ressources : c'est un trou (§7).

**f. Le cube attendu** : `pCube->GetItemCode() != pInfo->nNeedItemCode` → refus avec
`TM_SC_RESULT(0)` `request_msg_id = 256` `INVALID_ARGUMENT` (`MixManager.cpp:73-76`), et
`nNeedItemCode` vient de `EnhanceResource.need_item` (`ObjectMgr.cpp:1095`, colonne
`ArcadiaSchemaPSQL.sql:1352`). Les autres colonnes d'`EnhanceResource` alimentent
`EnhanceInfo` ainsi (`ObjectMgr.cpp:1090-1098`, `MixManager.h:49-64`) :

| Colonne (schéma `:1347-1372`) | Champ NGemity | Consommé ? |
| --- | --- | --- |
| `enhance_id` | `nSID` | oui (clé) |
| `enhance_type` | `Flag` (uint32) | **jamais lu** |
| `fail_result` | `nFailResult` | oui (`:109`) |
| `max_enhance` | `nMaxEnhance` | oui (`:83-84`) |
| `local_flag` | `nLocalFlag` | oui (filtre `GameRule::GetLocalFlag()`, `:1099`) |
| `need_item` | `nNeedItemCode` | oui (`:73`) |
| `percentage_1..20` | `fPercentage[20]` | oui (`:96`, `:154`) |
| *(aucune)* | `nRank` | **jamais affecté ni lu** |

Deux colonnes déclarées et chargées pour rien : `Flag` et `nRank`. `nFailResult` est de plus
falsifié quand il vaut 0.

**g. Résumé des réponses de 256** : `TM_SC_RESULT`(0) pour les refus d'argument, puis pour un
mix retenu soit `TM_SC_INVENTORY`(207) + `TM_SC_MIX_RESULT`(257) non vide (succès), soit
`TM_SC_MIX_RESULT`(257) **vide** (échec), soit **rien du tout** (mix validé sans branche
`switch`). Le refus `TS_RESULT_ACCESS_DENIED` (6) / `NOT_EXIST` (1) vient de
`check_mixable_item` (`MixManager.cpp:320-343`), qui teste aussi
`(status_flag & 4) != 0` → `ACCESS_DENIED`.

### 6.2 260 — `onSoulStoneCraft` (`WorldSession.cpp:1497-1588`)

Garde préalable : **contact PNJ** — `if (m_pPlayer->GetLastContactLong("SoulStoneCraft") == 0)
return;` (`:1499-1500`) : sans contact armé, **retour silencieux**. Le contact est armé par
`Messages::ShowSoulStoneCraftWindow` (`Messages.cpp:946`).

| Étape | Condition | Réponse | Source |
| --- | --- | --- | --- |
| 1 | contact `LastContact` absent | rien | `:1499-1500` |
| 2 | `craft_item_handle` introuvable | 0 / `NOT_EXIST` (1), valeur = handle | `:1503-1507` |
| 3 | `socket` de l'objet ∉ [1, 4] | 0 / `ACCESS_DENIED` (6), valeur = handle | `:1509-1513` |
| 4 | handle de pierre non nul mais introuvable | 0 / `ACCESS_DENIED` (6), valeur = handle | `:1521-1526` |
| 5 | type ≠ `TYPE_SOULSTONE` **ou** groupe ≠ `GROUP_SOULSTONE` **ou** classe ≠ `CLASS_SOULSTONE` | 0 / `NOT_ACTABLE` (5), valeur = handle | `:1527-1531` |
| 6 | pierre identique à une pierre déjà sertie (même `base_type`/`base_var`/`opt_type`/`opt_var`) | 0 / `ALREADY_EXIST` (9), valeur 0 | `:1537-1549` |
| 7 | aucune pierre non nulle fournie | 0 / `INVALID_ARGUMENT` (28) | `:1557-1561` |
| 8 | or insuffisant | 0 / `NOT_ENOUGH_MONEY` (10) | `:1562-1565` |
| 9 | succès | 207 + 0 / `SUCCESS` | `:1581-1587` |

Cœur métier :

- `nMaxReplicatableCount = nSocketCount == 4 ? 2 : 1` (`:1515`) — c'est **exactement** la règle
  écrite par le client : *« You can not have more than 2 Soul Stones socketed in the same weapon
  that increase the same stat »* (`db_string.dump:198045`). Deux sources indépendantes ;
- `nCraftCost += price / 10` (`:1553`) — **coût = dixième du prix** de la pierre, débité en or
  (`:1562`), ce qui confirme *« Socketing cost is … »* (`db_string.dump:198039`) ;
- à la pose : `nEndurance += pierre.Endurance`, `SetSocketIndex(i, pierre.Code)`,
  `EraseItem(pierre, 1)`, puis `pItem->SetCurrentEndurance(nEndurance)` (`:1567-1581`). Les
  châsses déjà occupées **non re-fournies** voient leur endurance actuelle re-comptée
  (`:1576`) — comportement non trivial, à ne pas reprendre sans le relire ;
- le code écrit dans la châsse est un **item code**, pas un handle :
  `SetSocketIndex(i, pSoulStoneList[i]->GetItemInstance().GetCode())` (`:1572`).

Sur ce dernier point, la forme de fil rzu est **neutre** — `TS_ITEM_SOCKETS` déclare
`_(array)(int32_t, socket, 4)` (`TS_SC_INVENTORY.h:7-11`), sans dire ce que la valeur désigne.
Le dépôt nomme le champ `ItemEntity.SocketItemIds` (`Game/DataAccess/Entities/Telecaster/ItemEntity.cs:35`)
et l'écrit tel quel en int32 (`Game/Network/Packets/Game/GameCharacterPackets.cs:353-355`).
**Code ou handle ? NON ÉTABLI** (§7) — c'est un prérequis direct de 260.

Enfin, les `Class`/`Group`/`Type` exigés (`TYPE_SOULSTONE`, `GROUP_SOULSTONE`,
`CLASS_SOULSTONE`) n'ont pas d'équivalent nommé dans `ItemResourceEntity` : le dépôt a
`ItemType.Soulstone = 401` (`Game/DataAccess/Entities/Enums/ItemType.cs:43`) et
`ItemGroup.Soulstone = 93` (`ItemGroup.cs:27`), et le `class` du schéma
(`ArcadiaSchemaPSQL.sql:2190`) n'est pas exposé comme propriété distincte — le mapping
`ArcadiaResourcesMappingProfile.cs:39` envoie `dst.Class` sur `ItemType`. La correspondance
`CHECK_ITEM_CLASS` → colonne `class` est donc **à établir** (§7).

### 6.3 262 / 263 / 264 — rien à porter

Aucun gestionnaire, aucun appelant, aucun `Messages::` dédié pour ces trois ids dans tout
NGemity (recherche sur `Chihiro/` et `shared/` : seuls les en-têtes et l'énumération). NGemity ne
fournit **aucune** politique de durabilité éthérée : les trois `MIX_TYPE` 801/802/803 sont
déclarés et jamais atteints (§6.1.d), et les deux paquets dédiés ne sont jamais reçus.
Répartition des rôles : **rzu tranche la trame, mais il n'y a pas de logique de référence pour
263/264** — tout ce qui n'est pas la trame est une décision de jeu (§8).

### 6.4 Articulation 263/264 ↔ `MIX_TYPE` 801/802/803 : deux hypothèses, aucune tranchée

| Hypothèse | Ce qui la soutient | Ce qui la contredit |
| --- | --- | --- |
| (A) 263/264 sont la voie **dédiée**, et `mix_type` 801/802/803 est une voie **distincte** (passant par 256) | deux paquets dédiés portent nommément `ETHEREAL_DURABILITY` ; `MIX_TRANSMIT_ETHEREAL_DURABILITY` (802) porte le **même nom** qu'un paquet dédié, ce qui est redondant mais possible | aucun moyen de distinguer les deux voies dans le client : les formules de `db_string.dump:137609`/`:137611` décrivent un « Main Slot + consommables », donc une **fenêtre de combinaison**, pas deux boutons séparés |
| (B) 263/264 **remplacent** 801/802/803 depuis 7.2, et les lignes `MixResource` de ces `mix_type` sont du reliquat | les deux paquets portent `// Since EPIC_7_2` (`…_DURABILITY.h:8`, `…_TO_EQUIPMENT.h:11`) ; le client estampille ses textes `<7.2>` (`db_string.dump:189800`, `:189826`) | 801/802/803 existent dans l'énumération de NGemity, donc au moins dans une version |

**Aucune des deux n'est établie.** La conséquence pour le socle est la même dans les deux cas :
263/264 sont deux trames fixes de 11 octets à lire, et **leur effet est une politique de jeu
entièrement à écrire** (§8). Trancher la question de la voie unique ou double est une décision de
conception, pas d'archéologie.

---

## 7. Écarts assumés avec NGemity, et pourquoi

| # | Écart | Pourquoi |
| --- | --- | --- |
| 1 | **Ne pas porter `KickPlayer()`** sur `sub_items.size() > 9` (`WorldSession.cpp:1447-1450`) | Déconnecter un joueur pour un paquet mal borné transforme une erreur de protocole en perte de session. On refuse la trame (log + `TM_SC_RESULT`), on ne coupe pas. Le seuil 9 reste la référence. |
| 2 | **Ne pas porter la boucle infinie** de `procEnhanceFail` (`MixManager.cpp:469`) ni son « vider les châsses » implicite | La source ne dit pas ce qu'elle fait (corps vide, condition constante). Le client, lui, dit que les pierres serties **sont** perdues sur échec (`db_string.dump`). L'écart à NGemity est donc double : on ne copie pas le bug **et** on tranche là où il ne tranche pas. |
| 3 | **Ne pas porter les codes `CHECK_*` inertes** (11, 12, 15, 16, 17, 18 : `MixManager.cpp:405-418`, `:443-444`) | Un prédicat déclaré mais ignoré accepte silencieusement des combinaisons que la table interdit. Soit on l'implémente, soit on refuse explicitement la ligne de ressource : jamais `default: break`. |
| 4 | **Corriger la borne de `CHECK_SAME_ITEM_ID`** (`&&` au lieu de `||`, `MixManager.cpp:560`) | La garde ne se déclenche jamais ; `pArrangedSubMaterial[nSlotIndex - 1]` peut sortir du vecteur. |
| 5 | **Ne pas porter `CHECK_SAME_SUMMON_CODE`** qui refuse toujours (`:574-577`) | Refuser une table de ressources valide à cause d'un `Not implemented yet` rendrait des lignes de `MixResource` inutilisables. À trancher par Killian (§8). |
| 6 | **`enhance_type` et `nRank` ne sont pas des champs vivants** | NGemity les charge et ne les lit jamais (`ObjectMgr.cpp:1091`, `:1089`). Le modèle du dépôt (`EnhanceResourceEntity.EnhanceType`, `EnhanceType` 0..9) n'est donc **pas validé** par NGemity : il faudra le confronter au client (§7 NON ÉTABLI). |
| 7 | **Ne pas porter `nFailResult == 0 → 1`** (`MixManager.cpp:461-465`) | Le commentaire de l'auteur (« nFailResult is not calculated correctly atm ») reconnaît que la valeur ne vient pas d'où elle devrait. On lit la colonne, on ne la falsifie pas. |
| 8 | **Ne pas porter `Messages::ShowSoulStoneCraftWindow`** (`Messages.cpp:943-948`) | Il émet sur l'id **259**, en collision avec `TM_CS_DONATE_REWARD` (§1.1) : le porter propagerait une ambiguïté non résolue dans le code du dépôt. |
| 9 | **`EPIC = EPIC_4_1_1`** (`shared/Common/Define.h:25`) | NGemity est **sous** 7.2/8.1 : il ne peut pas valider les gatings de `type` (257) et `target` (264). Sur ces deux champs, NGemity n'est pas une référence plus récente mais une référence plus ancienne. |
| 10 | **Le coût de sertissage `price / 10`** (`WorldSession.cpp:1553`) et la somme d'endurance (`:1567-1581`) | Ce sont des choix de NGemity, non corroborés par une seconde source autre que le mot « cost » du client. À traiter comme politique (ou comme calcul à valider), pas comme une constante recopiée. |

---

## 8. Ce qui existe déjà dans notre dépôt (état de `master`, revérifié)

Toutes les lignes ci-dessous ont été relues sur `master`
`ec76b218cd0bd7c6498d725f253abb8b431f0cd6`, après `git fetch`. **Le cadrage du PO est exact sur
cinq points et faux sur un** ; la correction est signalée.

### 8.1 Énumération et dispatch — le cadrage est exact

`Game/Network/Packets/Enums/GamePackets.cs` (102 lignes) ne porte **aucun** id de la bande
256-264 : la série va de `TM_CS_USE_ITEM = 253` (`:41`) à `TM_SC_UPDATE_ITEM_COUNT = 255`
(`:43`), puis saute à `TM_SC_HAIR_INFO = 220` (`:44`). `TM_SC_ITEM_WEAR_INFO = 287` est déjà
présent (`:30`), `TM_SC_INVENTORY = 207` aussi (`:31`). La bande est absente de l'énumération
**comme** du dispatch.

Point de contrat à ne pas oublier : `Game/Network/Clients/GameClient.cs:584-588` écarte tout id
non défini (`Enum.IsDefined`) par un log de debug, **avant** le `switch` final
(`:794-802`, `_ => throw new Exception("Unknown Packet Type")`). Donc :

- tant qu'un id n'est **pas** dans `GamePackets`, il est ignoré proprement ;
- dès qu'il **y est** sans bras de réception, il **tue la boucle** de réception.

C'est exactement la règle écrite dans `CLAUDE.md:1261-1265`. Le dispatch compte aujourd'hui
31 bras `if (header.ID == (ushort)GamePackets.…)` ; il porte déjà le motif « paquet S→C reçu par
erreur → log + drop » pour `TM_SC_REGION_ACK` (`GameClient.cs:620-630`), **le précédent à
suivre** pour 257 et 261.

### 8.2 Les tables de ressources — **le cadrage du PO est faux sur un point**

| Table | Schéma | Entité dans le dépôt | Chargement |
| --- | --- | --- | --- |
| `MixResource` | `ArcadiaSchemaPSQL.sql:471-582` | **AUCUNE** (`grep -rn "MixResource" --include=*.cs .` → 0 résultat hors SQL) | **aucun** |
| `EnhanceResource` | `ArcadiaSchemaPSQL.sql:1345-1372` | **`EnhanceResourceEntity` EXISTE** : `Game/DataAccess/Entities/Arcadia/EnhanceResourceEntity.cs` (15 l.), `DbSet` `ArcadiaContext.cs:18`, configuration `:166-180`, mapper `MigrateDatabase/Mappers/ArcadiaResourcesMappingProfile.cs:244-261`, source `MssqlEntities/Arcadia/MSSQLEnhanceResource.cs` | **aucun** service ne la lit |

> **Correction au cadrage du PO** (« les tables existent dans le schéma de référence et n'ont ni
> entité ni chargement ») : c'est vrai pour `MixResource` **seulement**. `EnhanceResource` a une
> entité, une clé composite `(Id, LocalFlag)` (`ArcadiaContext.cs:173`), une contrainte
> `cardinality("Percentage") <= 20` (`:177-178`) et un mapper — mais **le nom de table mappé est
> `EnhanceResources` au pluriel** (migration `20231213174355_Version0001_TheBeginning.cs:45`)
> alors que le schéma Arcadia écrit `create table "EnhanceResource"` **au singulier**
> (`ArcadiaSchemaPSQL.sql:1345`). Aucune requête runtime n'existe, donc l'écart ne s'est jamais
> manifesté. À trancher avant de charger la table (§ « A VERIFIER PAR KILLIAN »).

Deux autres écarts du même ordre, relevés sur la même entité :

- `ArcadiaResourcesMappingProfile.cs:246` fait `(EnhanceType)int.Parse(dst.enhance_type)` sur une
  colonne `char` (`ArcadiaSchemaPSQL.sql:1348`) : le mapper **jette** si la valeur n'est pas
  numérique, et `EnhanceResourceEntity.EnhanceType` est un `EnhanceType` 0..9
  (`Entities/Enums/EnhanceType.cs:5-14`) là où NGemity charge un `uint32_t Flag` sans jamais le
  lire (§6.1.f) ;
- `FailResult` est mappé depuis `fail_result` (`:248`) sur `FailResultType` (1..3,
  `Entities/Enums/FailResultType.cs:5-8`) — cohérent avec `EnhanceInfo::RESULT_*`
  (`MixManager.h:59-63`).

### 8.3 Modèle d'objet — les châsses et l'enchantement existent

| Brique | État | Source |
| --- | --- | --- |
| `ItemEntity.Enhance` (uint) | existe | `Game/DataAccess/Entities/Telecaster/ItemEntity.cs:29` |
| `ItemEntity.SocketItemIds` (`long[]`, max 4) | existe, **jamais lu hors écriture sur le fil** | `ItemEntity.cs:35` ; `TelecasterContext.cs:55` ; `GameCharacterPackets.cs:353-355` |
| `ItemEntity.EtherealDurability` (int) | existe | `ItemEntity.cs:30` |
| `ItemEntity.Endurance`, `.Flag`, `.Amount`, `.WearInfo` | existent | `ItemEntity.cs:31-34` |
| `ItemResourceEntity.EtherealDurability` | existe | `Entities/Arcadia/ItemResourceEntity.cs:36` |
| `ItemFlag` (index de bit, pas masque) | existe | `Entities/Enums/ItemFlag.cs` ; piège déjà documenté `CLAUDE.md:1172` |
| `ItemType.Soulstone = 401`, `.Cube = 306`, `.EtherealStone = 451` | existent | `ItemType.cs:41-46` |
| `ItemGroup.Soulstone = 93`, `.StrikeCube/DefenceCube/SkillCube/RestorationCube = 21-24` | existent | `ItemGroup.cs:23-27` |
| `EnhanceType` (0..9), `FailResultType` (1..3), `EnhancementFailResult` (0..3) | existent, **non consommés** | `Entities/Enums/` |

Aucun `ItemClass` (au sens de NGemity `CLASS_SOULSTONE`) n'existe : voir §6.2 et §7.

### 8.4 Primitives réutilisables — toutes vérifiées

| Primitive | Emplacement |
| --- | --- |
| `GetItemByHandleAsync(characterName, handle)` | `Game/Services/ICharacterService.cs:34` |
| `EraseItemsAsync(...)` | `ICharacterService.cs:55` |
| `AddItemAsync(characterName, itemResourceId, count)` | `ICharacterService.cs:53` |
| `ConsumeItemAsync(...)` | `ICharacterService.cs:41` |
| `InventoryService.EraseAsync(client, requests)` | `Game/Services/InventoryService.cs:64` |
| `IItemResourceRepository.GetGroupFields()` + `ItemGroupCatalog` (projection `FrozenDictionary`, précédent de catalogue no-tracking) | `Game/Services/ItemGroupCatalog.cs:12-28` |
| `TM_SC_RESULT` (0) et `ResultCode` — valeurs **identiques** à `TS_RESULT_*` de NGemity (`shared/Server/TS_MESSAGE.h:55-83`) | `Game/Network/Packets/Game/TS_SC_RESULT.cs` ; `Game/Network/Packets/ResultCode.cs` |
| `TS_SC_INVENTORY` (207), `TS_SC_GOLD_UPDATE` (1001) | `GamePackets.cs:31`, `:67` |
| Précédents de migration Arcadia | `Game/DataAccess/Migrations/Arcadia/20260712205315_AddNpcResource.cs`, `20260713084217_AddMonsterResource.cs`, `20260716140149_AddJobResourceAndJobLevelBonus.cs` |
| Tests d'offsets (modèle) | `Tests/Game/DropItemPacketsTests.cs` (NUnit + FluentAssertions) |

**Deux prérequis manquants, vérifiés :**

1. **Aucun mécanisme de contact PNJ** : `grep -rniE "lastcontact|GetLastContact" Game/ --include=*.cs`
   → **0 résultat**. Or 260 est gardé par `GetLastContactLong("SoulStoneCraft")`
   (`WorldSession.cpp:1499`). Un `LastContact` armé par l'ouverture de fenêtre est donc à créer
   intégralement (§9.4).
2. **Aucun modèle de carte de compétence** : `git ls-tree -r --name-only origin/master -- Game/Services/`
   ne contient aucun `SkillCardService.cs` → le modèle de `MIX_ENHANCE_SKILL_CARD` (102) vit
   **uniquement sur des branches non mergées** (§9.3.4).

### 8.5 Base mesurée sur ce poste (21/09/2026, branche de cette fiche)

```
export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
dotnet build Navislamia.sln -c Debug     → code de sortie 0 (160 avertissements, 0 erreur)
dotnet test  Tests/Tests.csproj          → code de sortie 0 — Failed: 0, Passed: 448, Total: 448
git rev-parse master origin/master       → ec76b218cd0bd7c6498d725f253abb8b431f0cd6 (identiques)
git log --oneline origin/master..master  → vide
```

`Git` est propre, `docs/packet-specs/` est **déjà dé-suivi** de `.gitignore`
(`.gitignore:469-473` : `/docs/*` puis `!/docs/packet-specs/`) : cette branche n'a **pas** à
toucher `.gitignore`, contrairement à ce qu'exigeait la fiche 253 à son époque.

---

## 9. Découpage — ce que cette fiche autorise

### 9.1 Le constat qui commande le découpage

| Ce qui manque | Vérification |
| --- | --- |
| Entité + chargement `MixResource` | §8.2 |
| Chargement `EnhanceResource` (entité présente, table mal nommée, mapper fragile) | §8.2 |
| Catalogue de mix et moteur de correspondance `main_material`/`sub_material` | §6.1 |
| Contact PNJ (`LastContact`) | §8.4 |
| Id établi pour la fenêtre de sertissage | §1.1 |
| Modèle de carte de compétence | §8.4 |
| **Politique** : taux, `fail_result`, sort des châsses, coût, clé de protection, durabilité éthérée | §6.1.e, §6.3, §2.4 |

Autrement dit : **le manque n'est pas une décision de jeu isolée, c'est le moteur**. C'est
exactement ce qui a motivé la mise en `THINKING` des cinq cartes, et cela reste vrai.

### 9.2 Étape 1 — purement structurel, implémentable sans trancher à la place de Killian

Le sous-ensemble ci-dessous ne demande **aucune** valeur de jeu et **aucune** table de
ressources. Il est le socle au sens strict : lire, borner, résoudre, refuser.

Pour **256, 260, 262, 263, 264** :

1. **Membres d'énumération** — ajouter les ids 256, 260, 262, 263, 264 et le pendant descendant
   257 dans `GamePackets` (`GamePackets.cs:41-43`), avec leurs noms d'`op_codes.md:83-92`.
2. **Bras de réception** — un `if (header.ID == …)` par id dans la chaîne de `GameClient.Receive`,
   et **un membre par id dans `GamePackets`** : critère 4 de la chaîne, sinon
   `GameClient.cs:802` lève.
3. **Déclaration de trame** — un `record`/struct de requête par paquet, avec un `TryRead…` qui
   valide la **taille** avant tout accès :
   - 256 : `Length >= 15`, `(Length - 15) % 6 == 0`, `N = (Length - 15) / 6`, **et** `N <= 9` ;
   - 260 : `Length == 27` ;
   - 262 : `Length == 31` ;
   - 263 : `Length == 11` ;
   - 264 : `Length == 11`.
   Toute autre taille → trame refusée, jamais lue à l'aveugle (§4, §3).
4. **Résolution des objets** — `GetItemByHandleAsync` sur chaque handle non nul, refus
   `ResultCode.NotExist` (1) / `NotOwn` (3) comme le fait déjà 203
   (`docs/packet-specs/203-drop-item.md` §5.3).
5. **Refus explicite** — `TM_SC_RESULT` (0) avec `request_msg_id` = l'id reçu et
   `ResultCode.InvalidArgument` (28), exactement le code que NGemity envoie quand aucun mix
   n'est résolu (`WorldSession.cpp:1463-1466`).
6. **Paquets descendants reçus par erreur** — motif `TM_SC_REGION_ACK`
   (`GameClient.cs:620-630`) pour **257** et **261**, qui sont `SessionPacketOrigin::Server` :
   log + drop, jamais le `switch` final.
7. **Tests d'offsets** (critère 3) — un fichier de tests par famille, sur le modèle de
   `Tests/Game/DropItemPacketsTests.cs` : taille totale et position de chaque champ, plus les cas
   de refus (trame tronquée, `N` incohérent avec `Length`, `N > 9`, `Length` des trames fixes).

Point de méthode : cette étape **n'écrit aucune logique d'artisanat**. Elle rend les cinq cartes
`THINKING` ré-ouvrables sans refaire l'archéologie, et elle est vérifiable par des tests
d'offsets — donc elle satisfait les critères transversaux 1 à 4.

### 9.3 Étape 2 — conditionnée à un arbitrage

#### 9.3.1 Socle « mix » — 256 + 257

**Conditionné à §8.2** : entité `MixResourceEntity` (nom de table à trancher, §8.2), migration,
mapper, chargement no-tracking, puis le moteur de correspondance — et à **une décision de
Killian** sur les six codes `CHECK_*` inertes (§6.1.c, §7 #3), sur `CHECK_SAME_SUMMON_CODE`
(§7 #5) et sur le sort de l'objet/châsses en cas d'échec (§6.1.e, §7 #2).

Ce qui, dans le mix, **peut** être porté sans arbitrage : la validation de `sub_material_count`
= N, la formule positionnelle `MixResource → MixBase` (§6.1.b ; l'ordre des colonnes
`ArcadiaSchemaPSQL.sql:473-582` est identique à `ObjectMgr.cpp:1124-1139`), et les codes 1-10,
13, 14 dont la sémantique est explicite dans le `switch` NGemity.

#### 9.3.2 Socle « pierres d'âme » — 260 + 261 (+ 262 si §9.3.3 se règle)

Le plus mûr du lot : **le format, la garde et les refus sont intégralement sourcés** (§3.3, §6.2),
et deux sources indépendantes confirment les 4 châsses et la règle « pas plus de 2 du même stat »
(`db_string.dump:198045` ; `WorldSession.cpp:1515`). **Mais** trois prérequis manquent :

1. `LastContact` / contact PNJ absent du dépôt (§8.4) — **bloquant** : sans lui, NGemity refuse
   en silence (pas seulement en l'occurrence : c'est sa première garde) ;
2. l'id de la fenêtre de sertissage est **NON ÉTABLI** (§1.1) ;
3. le contenu de la châsse (code d'objet ou handle) est **NON ÉTABLI** (§6.2).

#### 9.3.3 262 — non implémentable

La sémantique des 6 handles est inconnue (§3.4) et rien ne la contraint : ni rzu, ni NGemity, ni
le client. Toute implémentation serait une supposition. **262 reste parqué** ; seule sa trame
(taille 31) entre dans l'étape 1.

#### 9.3.4 `MIX_ENHANCE_SKILL_CARD` (102) — collision de branches à ne pas provoquer

NGemity traite ce type sur les mêmes châsses que les paquets **214/215** (châsses) et **284**
(cartes), dont le modèle vit **uniquement sur des branches non mergées** : `master` n'a aucun
`Game/Services/SkillCardService.cs` (§8.4). La fiche **ne suppose rien disponible** et **ne
réimplémente rien** : porter 102 dans ce socle fabriquerait exactement le conflit que le PO veut
éviter. À traiter **après** le merge de 214/215/284, sur une carte dédiée, en repartant de
`EnhanceSkillCard` (`MixManager.cpp:123-172`) dont les trois exigences sont nettes :
`nSubMaterialCount == 3`, deux cartes de **même `code` et même `enhance`**, un cube de groupe
`GROUP_SKILL_CUBE`.

#### 9.3.5 Socle « durabilité éthérée » — 263 + 264

**Rien de NGemity à porter** (§6.3). Ce qui est établi : deux trames fixes de 11 octets, un seul
`handle` pour 263, un `float rate` pour 264, tous deux « depuis 7.2 », et un sens déduit du
client (263 = charger la pierre en sacrifiant un équipement ; 264 = en restituer, sous forme de
`rate`). Ce qui reste **entièrement** à trancher : le montant rechargé (« proportionnel au prix
de l'objet », `db_string.dump:137571` — formule exacte inconnue), les plafonds
1000/10000 (§2.4), l'objet et le coût consommés, la sémantique exacte de `rate`, et
l'articulation avec `mix_type` 801/802/803 (§6.4). **Ce lobe n'est pas implémentable au-delà de
l'étape 1.**

### 9.4 Le déclencheur de fenêtre — **décision : carte dédiée, hors de ce socle**

**Tranché : le déclencheur de fenêtre (contact PNJ de 260, ouverture de la fenêtre de 262) NE
FAIT PAS PARTIE de ce socle.** Trois raisons, chacune vérifiable :

1. **Le mécanisme est absent du dépôt** : `grep -rniE "lastcontact|GetLastContact" Game/ --include=*.cs`
   → 0 résultat (§8.4). Ce n'est pas un adaptateur à écrire, c'est un sous-système (armement
   d'un contact par ouverture de fenêtre, expiration, persistance) qui dépasse un socle de
   paquets.
2. **Il touche un domaine déjà cartographié ailleurs** : `docs/npc-dialogs.md:62-64` écrit que
   les actions qui « open a specialized window, including markets, teleportation, storage,
   auctions and quests » demandent « their own packet and service implementations » et que leurs
   déclencheurs Lua ne sont **intentionnellement pas exécutés**. Le contact PNJ de l'artisanat
   appartient à cette famille, pas à celle des paquets d'inventaire.
3. **Il est bloqué par une ambiguïté non résolue** : la fenêtre de sertissage n'a pas d'id
   (§1.1). Livrer un déclencheur obligerait à choisir 259 au jugé, ou à ne pas émettre — dans
   les deux cas au-delà de ce que la référence permet.

**Conséquence assumée : 260 ne peut pas être testé de bout en bout par cette chaîne.** L'étape 1
livre le lecteur de trame et le refus ; le chemin « fenêtre ouverte → sertissage effectif » reste
dû. À découper par le PO sur une carte dédiée « contact PNJ / déclencheurs de fenêtre », qui
servira aussi 261 et, plus tard, les marchés et l'entrepôt.

`261` (`TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW`), en revanche, a un id établi : **son émission peut
entrer dans le socle** dès lors que quelque chose doit ouvrir la fenêtre de réparation — mais
aucun déclencheur n'existe (point 1). Décision retenue : la constante va dans l'énumération et le
bras de réception (motif « S→C reçu par erreur »), **aucune émission n'est câblée** tant que le
déclencheur n'existe pas.

### 9.5 Le socle se scinde-t-il ? — **oui, en trois lobes**

**Recommandation au PO** (c'est à lui de scinder la carte Trello, pas à cette fiche) :

| Lobe | Paquets | État | Motif de séparation |
| --- | --- | --- | --- |
| **A — mix / enchantement** | 256, 257 | moteur à écrire, décisions de jeu à trancher | dépend de `MixResource` + `EnhanceResource` et de la politique « échec / powder / châsses » |
| **B — pierres d'âme** | 260, 261, 262 | le plus mûr sur le format, bloqué sur le déclencheur et sur 262 | dépend d'un contact PNJ absent et d'un id de fenêtre non établi |
| **C — durabilité éthérée** | 263, 264 | **aucune** logique de référence | dépend entièrement d'une politique de jeu propre à la 7.2+ |

Ils partagent trois choses, ce qui justifie de garder la fiche unique : la bande d'ids
256-264, la même primitive de résolution des handles, et le même bloc `GamePackets`/dispatch
(carte `GamePackets.cs` déjà en tension : cf. le répertoire de dispatch, §10).

**Découpage minimal que cette fiche recommande** : livrer l'**étape 1 pour les cinq cartes**
(§9.2) comme un seul incrément, puis ouvrir un lobe à la fois, dans l'ordre **B → A → C** :
B parce qu'il a le moins d'inconnues *de format* ; A parce qu'il a les plus grosses dépendances
de schéma ; C parce qu'il n'a aucune référence de logique.

### 9.6 Le cas « pas de code » — écarté, et pourquoi

Le cas prévu par le brief (`ne rien coder`) **n'est pas retenu** : l'étape 1 (§9.2) est
réellement implémentable sans trancher à la place de Killian. Elle ne contient aucune constante
inventée — les bornes, tailles et codes de refus viennent tous de rzu ou de NGemity, ligne à
ligne. Ce qui reste hors d'atteinte, c'est l'étape 2 : elle va en « A VERIFIER PAR KILLIAN »
ci-dessous, et c'est là que doit rester une constante choisie au hasard.

Rappels de contrat pour `navis-dev`, repris de `CLAUDE.md:1261-1265` et du critère 4 :

- **enum et dispatch modifiés dans le même changement** : un membre de `GamePackets` sans bras
  de réception atteint `GameClient.cs:802` et tue la boucle ;
- **au moins un test d'offsets** par trame déclarée (taille totale en-tête compris + position de
  chaque champ) ;
- **`CLAUDE.md` n'est PAS écrit par les agents** : le bloc destiné à `CLAUDE.md` est porté par la
  description de la MR, par `navis-qa` ;
- **fichiers de dispatch à surveiller** : `Game/Network/Packets/Enums/GamePackets.cs` et
  `Game/Network/Clients/GameClient.cs` — au moins onze MR les touchent déjà. **Point de
  collision à signaler dans le compte rendu de la MR**, Killian merge seul.

---

## 10. Commits et binaires épinglés

### 10.1 Dépôts

| Dépôt | Commit | Rôle |
| --- | --- | --- |
| `Navislamia` | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | `master` au 21/09/2026, base de cette branche |
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | trame, ordre des champs, gating |
| `reference/ngemity` (RZEmulator) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | logique (`EPIC = EPIC_4_1_1`) |

### 10.2 Commits rzu qui portent les décisions 7.3

| Commit | Date | Objet | Ce qu'il tranche ici |
| --- | --- | --- | --- |
| `d7c58ee6` | 2017-02-07 | « Adjust count management for packets and add all known GS packets as of 9.4 » | **création des huit en-têtes** ; introduction de `_(count)` |
| `bdd362a6` | 2017-02-26 | « Update packets based on available GS pdbs (5.2, 6.1, 6.2, 7.1, **7.2, 7.3**, 7.4, 8.1) » | **gating `version >= EPIC_8_1`** de `type` (257) et `target` (264) ; `// Since EPIC_7_2` sur 263/264 |
| `05bc2d82` | 2020-03-29 | « Use strong typedef for handles and game time values » | `ar_handle_t` = `uint32` |
| `11f2b6fd` | 2020-08-11 | « use versionned ID for all packets and update their ID with epic 9.6.3 » | ids `X(…, version < EPIC_9_6_3)` / `X(…+1000, …)` |
| `3b31db1e` | 2023-09-30 | « add last tested epic in packet definitions » | `// Last tested: EPIC_9_8_1` (256, 257) |

### 10.3 Commits NGemity

| Commit | Date | Objet |
| --- | --- | --- |
| `a2095ac` | 2017-12-23 | « Initial commit » — `WorldSession.cpp`, `Messages.cpp` |
| `1c7d858` | 2018-01-16 | « Added support for enhancing and summon gear » — `Crafting/MixManager.cpp` |

### 10.4 Binaires et ressources client (aucun SHA de dépôt : ce ne sont pas des dépôts)

| Fichier | Taille | SHA-256 | Usage ici |
| --- | --- | --- | --- |
| `reference/client73/SFrame.exe` | 9 841 664 o | `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | table d'annotation `TM_*` (173 noms), RTTI d'interface |
| `reference/client73/db_string.rdb` | 14 294 729 o | `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` | textes d'interface, formules de combinaison, estampilles `<7.2>` |
| `reference/client73/db_combineres.rdb` | 1 654 316 o | `9d102803e455e9bc61bd38e3be6dd88cb06588cd92481cae9f6633a3df11018d` | équivalent client de `MixResource` — **contenu non décodé** |
| `reference/client73/db_enhance.rdb` | 23 787 o | `f0e8286d621fa110987ddc61d6d50fcd01e9046b51d35197aa339f870dd333de` | équivalent client d'`EnhanceResource` — **contenu non décodé** |
| `reference/client73/db_mixcategory.rdb` | 6 032 o | `6f85014a564ae5d1df06aa37d3ef33b49314f018b91f8c683d65b843f4a4401b` | catégories de la fenêtre de combinaison — **contenu non décodé** |

Méthode de lecture, reproductible :

```
strings -a SFrame.exe    > /tmp/sframe.dump       # 63 099 entrées (md5 7d92f691a46e6768d4cf00e85747e776)
strings -a db_string.rdb > /tmp/db_string.dump    # 255 814 entrées (md5 b7df20cda8e13491e8e6bd9c348dc1fb)
```

Aucun binaire ni script client n'a été exécuté (`reference/README.md` : « Aucun binaire ni
script client exécuté »). Aucun `db_*.rdb` n'a été décodé au-delà de `strings` : les trois
tables de ressources ci-dessus sont citées **par empreinte et par taille**, jamais par contenu.

`reference/commits.json` confirme les trois commits de dépôt ci-dessus (`Navislamia`
`6a982c81…` y est périmé, `master` ayant avancé depuis).

---

## NON ÉTABLI

Chaque entrée nomme la question précise à trancher — `navis-dev` ne doit pas deviner.

1. **Id de la fenêtre de sertissage.** rzu et NGemity déclarent tous deux
   `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW` sur **259**, en collision avec `TS_CS_DONATE_REWARD`
   (§1.1). `op_codes.md:86` tranche pour `DONATE_REWARD`. **Quel id la 7.3 utilise-t-elle pour
   la fenêtre de sertissage — 259, un autre, ou aucun paquet (ouverture par commande locale) ?**
2. **Sémantique des 6 handles de 262** (`TS_CS_REPAIR_SOULSTONE.h:6`). Nom, compte (6) et trame
   sont établis ; **ce qu'ils désignent** (6 objets ? 1 objet + 5 matériaux ? 6 châsses d'un
   équipement ?) ne l'est pas. Aucune source dans le corpus ne le dit.
3. **Le socket contient-il un `item_code` ou un handle ?** NGemity écrit
   `GetItemInstance().GetCode()` dans la châsse (`WorldSession.cpp:1572`) ; rzu nomme le champ
   `socket` sans le typer sémantiquement (`TS_SC_INVENTORY.h:8`) ; le dépôt l'appelle
   `SocketItemIds` (`ItemEntity.cs:35`). **Prérequis direct de 260.**
4. **Valeur de `check_material_info` pour les codes 11, 12, 15, 16, 17, 18.** Les six sont
   déclarés (`MixManager.h:79-103`) et inertes côté NGemity (`:405-418`, `:443-444`).
   **Que doivent-ils faire en 7.3** — et notamment 16/17 (`ETHEREAL_DURABILITY_E/NE`) et 18
   (`ITEM_GRADE`), qui touchent au lobe C ?
5. **`CHECK_SAME_SUMMON_CODE` (20)** : NGemity refuse toujours (`:574-577`). **Des lignes de
   `MixResource` 7.3 l'utilisent-elles ?** Si oui, elles sont inutilisables en l'état.
6. **Contenu réel de `db_combineres.rdb`, `db_enhance.rdb`, `db_mixcategory.rdb`.** Les
   empreintes et tailles sont au §10.4 ; le contenu n'est pas décodé et **aucune ligne de
   `MixResource`/`EnhanceResource` n'est connue**. Tout ce qui suit en dépend : les `mix_type`
   réellement présents, les `fail_result`, les `percentage_*`, les `need_item`. `reference/README.md`
   précise en outre que **`.nfe`/`.nfa` et toute donnée de carte sont absents** du dossier — donc
   les taux « réels » ne sont pas établissables ici, seulement leur *mécanique*.
7. **Formule exacte du rechargement éthéré.** Le client dit « proportionnel au prix de l'objet »
   (`db_string.dump:137571`) sans donner le coefficient ; NGemity n'a rien. **Quel ratio, et
   quelle unité pour `rate` (fraction 0–1, pourcentage 0–100, points) ?**
8. **Articulation 263/264 ↔ `mix_type` 801/802/803** (§6.4). Deux hypothèses, aucune tranchée.
9. **Sens exact de `EnhanceResource.enhance_type`.** NGemity le charge dans un `uint32 Flag`
   jamais lu (`ObjectMgr.cpp:1091`) ; le dépôt en fait un `EnhanceType` 0..9 et **jette** si la
   valeur n'est pas numérique (`ArcadiaResourcesMappingProfile.cs:246`). **Est-ce un index de
   famille d'équipement (0..9) ou un champ de bits ?**
10. **`EnhanceInfo.nRank`** n'est jamais affecté par NGemity (`ObjectMgr.cpp:1089`) alors qu'il
    est déclaré (`MixManager.h:52`). **À quoi correspond-il dans la table de référence ?**
11. **Nom de la table `EnhanceResource`** : le schéma écrit le singulier
    (`ArcadiaSchemaPSQL.sql:1345`), l'EF du dépôt mappe `EnhanceResources` au pluriel
    (`20231213174355_Version0001_TheBeginning.cs:45`). Lequel est la source de vérité ?
12. **Borne haute réelle de `sub_items` pour 256 en 7.3.** 9 est le maximum de la table de
    référence (`MAX_SUB_MATERIAL_COUNT`, `MixManager.h:24`) et la garde de NGemity
    (`WorldSession.cpp:1447`), mais **le client 7.3 lui-même n'a pas été observé** : sa limite
    (9 ? plus ?) n'est pas prouvée.
13. **La clé « E-Protect Powder » n'est décrite par aucune ressource.** NGemity l'identifie par
    élimination, sans vérifier son `item_code` (`MixManager.cpp:63-71`). **Quel `item_code`
    (ou quel groupe) vaut protection en 7.3 ?**
14. **Vérifier les châsses en cas d'échec d'enchantement.** Le client affirme qu'elles sont
    perdues (`db_string.dump`, tooltip de l'E-Repair Powder) ; NGemity ne les vide pas
    (`MixManager.cpp:469` est une boucle infinie vide). **Lequel des deux fait foi ?**

---

## A VERIFIER PAR KILLIAN

Politique de jeu : une constante choisie au hasard serait un défaut, pas une solution.

1. **Le taux de réussite du mix** — `percentage_1..20` d'`EnhanceResource`, indexées par
   l'enchantement courant (`MixManager.cpp:96`). Les valeurs réelles sont dans
   `db_enhance.rdb`, non décodé, et **absentes de ce dossier** (`NON ÉTABLI` 6). Quelle est la
   source de vérité pour les données, et confirme-t-on l'indexation « pourcentage[enhance
   actuel] » ?
2. **Le sort de l'objet en cas d'échec** — les trois `FailResultType` de NGemity (1 : objet
   cassé ; 2 : `enhance -= 3` ou destruction si `<= 3` ; 3 : `enhance = 0` ou `-= 3`) sont-ils
   la politique retenue, et **les pierres serties sont-elles perdues** (le client dit oui, la
   référence ne le fait pas) ?
3. **Les six codes `CHECK_*` inertes** (`NON ÉTABLI` 4) et **`CHECK_SAME_SUMMON_CODE`**
   (`NON ÉTABLI` 5) : les implémenter tels que nommés, ou refuser explicitement les lignes de
   `MixResource` qui les emploient ?
4. **Le coût de sertissage** — `price / 10` (`WorldSession.cpp:1553`) : confirmation, ou autre
   barème ? Et la somme d'endurance des pierres (`:1571`) comme endurance de l'objet : à
   conserver ?
5. **Le lobe C (263/264) : ouvre-t-on le chantier ?** Aucune référence de logique n'existe
   (`NON ÉTABLI` 7, 8). Il faudra fournir : le ratio de rechargement, les plafonds
   (1000 / 10000 ?), l'objet consommé, l'unité de `rate`, et l'arbitrage des `mix_type`
   801/802/803. Sans ces cinq valeurs, **le lobe C n'est pas implémentable au-delà du lecteur de
   trame**.
6. **Le sous-système « contact PNJ / déclencheur de fenêtre »** (§9.4) : le PO doit décider
   s'il crée la carte dédiée. Tant qu'elle n'existe pas, **260 n'est pas testable de bout en
   bout** et 261 ne peut pas être émis.
7. **`MIX_ENHANCE_SKILL_CARD` (102)** : à traiter après le merge de 214/215/284, sur carte
   dédiée — confirmation que la fiche doit continuer à ne rien en supposer (§9.3.4) ?
8. **Le découpage en trois lobes** (§9.5) et l'ordre recommandé **B → A → C** : c'est le PO qui
   scinde la carte Trello.
9. **Le nom de table `EnhanceResource`/`EnhanceResources`** et la fragilité du mapper
   `int.Parse(dst.enhance_type)` (`NON ÉTABLI` 9, 11) : à trancher avant le premier chargement,
   sinon la migration sera à refaire.

---

## 11. Implémentation — navis-dev (21/09/2026)

**Périmètre livré : l'étape 1 seule** (§9.2), sur la branche de cette fiche. Aucune table de
ressources n'est chargée, aucun taux n'est tiré, aucune politique d'échec, aucun coût, aucune
règle de sertissage : le socle **lit, borne, résout et refuse**, et rien d'autre.

### 11.1 Décisions tranchées dans le code

| Point | Décision | Source |
| --- | --- | --- |
| Ids ajoutés à `GamePackets` | 256, 257, 260, 261, 262, 263, 264 | §1, `op_codes.md:83-92` |
| 259 | **absent** de l'énumération (et verrouillé par un test) | §1.1 : id non établi |
| 257 et 261 | membres d'énumération **et** bras « serveur → client reçu par erreur » : log + drop | §9.2.6, motif `TM_SC_REGION_ACK` |
| Bras de réception | **un seul** `if (header.ID is …)` couvre les cinq ids clients | critère 4 : aucun membre ne peut atteindre le `switch` final de `GameClient.Receive` |
| 264 | `Length == 11` **exactement** : la forme de 12 octets (champ `target` d'`EPIC_8_1`) est refusée au lecteur, pas seulement ignorée | §5.2, commit rzu `bdd362a6` |
| Compte de slots de 256 | le champ de l'offset 13 est **lu et comparé** à `(Length - 15) / 6` ; une divergence refuse la trame | §3.1 : c'est la longueur du tableau écrite par l'émetteur, pas un `mix_type` |
| Handles nuls | **jamais** résolus : ce sont les sentinelles d'un emplacement vide | §6.1.a `WorldSession.cpp:1451`, §6.2 `:1521` |
| Refus d'une trame lisible | `TM_SC_RESULT` (0), `request_msg_id` = id reçu, `InvalidArgument` (28), **valeur 0** | §9.2.5, `WorldSession.cpp:1463-1466` |
| Refus d'un handle inconnu | `NotExist` (1), **valeur = handle** | §9.2.4, convention du chemin 203 (`203-drop-item.md` §5.3), `ItemUseService.cs:53` |
| Lecture impossible en base | `DBError` (8), valeur = handle | convention du dépôt (`ItemUseService.cs:45`) |
| Hors du monde | `ConnectionInfo.CharacterHandle == 0` → journal + abandon silencieux, aucune réponse | motif 550 (`GameClient.HandleGetRegionInfo`) |

### 11.2 Ce que le refus `InvalidArgument` signifie, et ce qu'il ne signifie pas

Une trame **bien formée et intégralement résolvable** reçoit aujourd'hui `InvalidArgument` : c'est
le refus de socle, pas un verdict de jeu. Le client affichera donc un échec à chaque clic de
combinaison, de sertissage ou de durabilité éthérée — c'est le comportement honnête d'une
fonctionnalité non écrite, et il est **impossible de le confondre** avec un échec métier
(`InvalidArgument` est aussi le code de NGemity quand aucun mix ne correspond,
`WorldSession.cpp:1463-1466`). Le jour où un lobe atterrit, c'est cette ligne de refus qui
disparaît : elle est isolée dans `CraftingSocleService.HandleAsync`, en fin de méthode.

### 11.3 Offsets livrés (tous en 7.3, en-tête de 7 octets compris)

| Id | Paquet | Taille | Champs lus | Lecteur |
| --- | --- | --- | --- | --- |
| 256 | `TM_CS_MIX` | `15 + 6N` | handle @7, count @11, compte déclaré @13, puis `N` × (handle @15+6i, count @19+6i) | `GameActionPackets.TryReadMix` |
| 260 | `TM_CS_SOULSTONE_CRAFT` | 27 | handle @7, 4 pierres @11, 15, 19, 23 | `TryReadSoulstoneCraft` |
| 262 | `TM_CS_REPAIR_SOULSTONE` | 31 | 6 handles @7, 11, 15, 19, 23, 27 | `TryReadRepairSoulstone` |
| 263 | `TM_CS_TRANSMIT_ETHEREAL_DURABILITY` | 11 | handle @7 | `TryReadTransmitEtherealDurability` |
| 264 | `…_TO_EQUIPMENT` | 11 | `rate` `float32` @7 | `TryReadTransmitEtherealDurabilityToEquipment` |

Bornes refusées : 256 → `Length < 15`, queue non multiple de 6, `N > 9`, compte déclaré ≠ `N` ;
260 → ≠ 27 ; 262 → ≠ 31 ; 263 → ≠ 11 ; 264 → ≠ 11 (donc la forme de 12 octets d'`EPIC_8_1`).

### 11.4 Fichiers livrés

| Fichier | Nature |
| --- | --- |
| `Game/Network/Packets/Enums/GamePackets.cs` | +7 membres, aucun autre |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `MaxSubItems`, 5 records de requête, 5 lecteurs |
| `Game/Services/CraftingSocleRules.cs` | **neuf** — quels handles une requête nomme réellement (les zéros ne sont pas des objets) |
| `Game/Services/CraftingSocleService.cs` + `Game/Services/Interfaces/ICraftingSocleService.cs` | **neuf** — lecture, bornage, résolution, refus |
| `Game/Network/NetworkService.cs` | champ + paramètre de constructeur |
| `DevConsole/Program.cs` | un enregistrement `AddSingleton` |
| `Game/Network/Clients/GameClient.cs` | **deux** bras : les cinq ids clients, et le drop de 257/261 |
| `Tests/Game/CraftingSoclePacketsTests.cs` | **neuf** — 42 tests |

Le socle vit dans un service, et non dans `GameClient`, pour une raison de collision : `GameClient.cs`
est touché par presque toutes les cartes de paquets en cours. Le diff de `GameClient` se limite à
deux `if` de dispatch, et le refus est à un seul endroit pour les trois lobes à venir (§9.5).

### 11.5 Pièges rencontrés, à ne pas réintroduire

1. **Le `switch` final de `GameClient.Receive` tue la boucle de réception.** Sept membres sont
   ajoutés à `GamePackets` : les sept doivent être dispatchables. 257 et 261 n'ont aucun bras
   « métier » et pourtant en ont un — celui qui les journalise et les jette, sinon un client
   malveillant déconnecte son lecteur en envoyant un paquet descendant.
2. **Lire 264 avec `>= 11` accepterait la trame 8.1** en lisant `rate` puis un octet `target`
   fantôme : le gating de version se joue dans la comparaison de taille, pas dans le commentaire.
3. **Le compte de slots de 256 n'est pas un `mix_type`** : la trame 7.3 ne porte pas le champ
   `type` de 257 (`EPIC_8_1`), et l'offset 13 est bien la longueur du tableau. Le lire comme un
   identifiant de recette serait une erreur silencieuse (aucun test ne l'attraperait).
4. **Un handle nul n'est pas un objet manquant.** Les quatre pierres de 260 et les six handles de
   262 sont écrits en position fixes : résoudre les zéros ferait répondre `NotExist` à une trame
   parfaitement légitime (châssis laissé vide).
5. **`client.ConnectionInfo.CharacterName` n'est lisible qu'en jeu** : les cinq bras sont atteints
   avant l'entrée en monde si le client est mal élevé, d'où la garde `CharacterHandle == 0`.

### 11.6 Ce qui n'est pas porté, et pourquoi

- **Aucun émetteur.** Ni `TM_SC_MIX_RESULT` (257), ni `TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW` (261)
  n'est construit : la première demande toute la politique de mix (§9.3), la seconde le
  déclencheur de fenêtre (§9.4, carte dédiée au PO).
- **`TM_SC_SHOW_SOULSTONE_CRAFT_WINDOW` (259) reste absent** : id non établi (§1.1).
- **La garde `LastContact` de 260 n'est pas reproduite** (§6.2 étape 1) : le sous-système de
  contact PNJ n'existe pas. NGemity répond *rien* dans ce cas ; le socle, lui, refuse
  `InvalidArgument`. C'est un écart assumé : sans contact, il n'y a de toute façon aucune
  fenêtre ouverte côté serveur.
- **La distinction `NOT_EXIST` / `ACCESS_DENIED` de NGemity** sur les pierres de 260 n'est pas
  reprise : le socle répond `NotExist` pour tout handle irrésolu, comme le chemin 203.
- **Aucune lecture de `MixResource`/`EnhanceResource`**, aucun `EnhanceInfo`, aucun châssis
  touché : rien de la §9.3, rien de la §9.5.

### 11.7 Vérifications relevées (21/09/2026)

```
export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
dotnet build Navislamia.sln -c Debug     → code de sortie 0 (160 avertissements, 0 erreur)
dotnet test  Tests/Tests.csproj          → code de sortie 0 — Failed: 0, Passed: 490, Total: 490
git log --oneline origin/master..master  → vide
```

Base mesurée par cette fiche (§8.5) : **448** tests. Après livraison : **490**, soit **+42**
(offset de chaque champ, bornes de refus, sentinelles nulles, gates de version). Aucun test
existant n'a été modifié ni supprimé.

### 11.8 Bloc prêt à coller dans `CLAUDE.md`

````markdown
### Socle artisanat et enchantement — `TM_CS_MIX` 256, `TM_CS_SOULSTONE_CRAFT` 260,
`TM_CS_REPAIR_SOULSTONE` 262, `TM_CS_TRANSMIT_ETHEREAL_DURABILITY` 263 / `…_TO_EQUIPMENT` 264

Fiche complète et références : `docs/packet-specs/socle-artisanat-objets.md`.

- **N'a été livré que le socle structurel** : `CraftingSocleService` lit la trame à sa taille 7.3,
  la borne, résout chaque handle non nul contre l'inventaire du personnage, puis **refuse**
  (`InvalidArgument`, valeur 0) — le moteur d'artisanat n'existe pas. Aucune table `MixResource` /
  `EnhanceResource` n'est chargée, aucun taux n'est tiré, aucun châssis n'est touché.
- **Tailles 7.3** : 256 = `15 + 6N` (`N <= 9`) · 260 = 27 · 262 = 31 · 263 = 11 · 264 = **11**.
  Le champ `target` de 264 et le champ `type` de 257 sont gatés `EPIC_8_1` : la trame 8.1 de 264
  fait 12 octets et **doit rester refusée**.
- **256, offset 13 = nombre de slots matériaux** (longueur du tableau écrite par l'émetteur), pas
  un identifiant de recette ; le socle la compare `(Length - 15) / 6` et refuse une divergence.
- **Sentinelles nulles** : les slots vides (4 pierres de 260, 6 handles de 262, cible absente de
  256) sont écrits `0` et ne sont **jamais** résolus — un zéro n'est pas un objet manquant.
- **257 et 261 sont descendants** (`SessionPacketOrigin::Server`) : leur bras de réception les
  journalise et les jette. Un membre de `GamePackets` sans bras atteint le `throw
  Unknown Packet Type` final de `GameClient.Receive`, qui **casse la boucle de réception** :
  énumération et dispatch se modifient ensemble.
- **259 n'est pas établi** : rzu et NGemity y déclarent `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW`,
  `op_codes.md:86` y met `TM_CS_DONATE_REWARD`. La fenêtre de sertissage ne peut pas être émise
  tant que l'id n'est pas tranché, et 260 n'est pas testable de bout en bout sans le
  déclencheur de contact PNJ.
- **Restent à trancher avant tout moteur** (détail en fin de fiche) : taux de réussite, sort des
  châsses en cas d'échec, coût `price / 10`, unité du `rate` de 264, articulation
  `mix_type` 801/802/803 ↔ 263/264.
````

---

## 12. A VERIFIER PAR KILLIAN — ajouts du dev (21/09/2026)

1. **Le refus de socle est-il le bon comportement en attendant le moteur ?** Chaque trame lisible
   reçoit aujourd'hui `InvalidArgument` (valeur 0) : le joueur voit un échec à chaque clic. La
   fiche le prescrit (§9.2.5) et cela n'invente rien, mais un **drop silencieux** serait l'autre
   lecture possible. À confirmer avant que les cinq cartes `THINKING` ne repartent.
2. **La comparaison du compte déclaré de 256** (§11.1) et le refus de la trame 8.1 de 264
   (12 octets) sont des **décisions de rigueur**, non des observations de client : ni le client
   7.3 ni une capture n'ont été vus. Si un jour une trame légitime est refusée par l'un de ces
   deux contrôles, c'est ici qu'il faut regarder en premier.
3. **Un handle irrésolu répond toujours `NotExist` (1)**, alors que NGemity distingue
   `NOT_EXIST` (item à sertir, `:1503-1507`) et `ACCESS_DENIED` (pierre, `:1521-1526`). La
   distinction ne peut pas être portée tant que le rôle de chaque handle de 262 et 263 n'est pas
   établi (`NON ÉTABLI` 2 et 3) : faut-il la rétablir lobe par lobe plus tard ?
4. **La garde `LastContact` de 260 n'est pas reproduite** (§11.6) : NGemity retourne
   silencieusement quand aucun contact n'est armé. Le socle refuse. Cette garde appartiendra au
   déclencheur de fenêtre (§9.4) — confirmation qu'elle n'a rien à faire dans le socle ?
5. **Handle falsifié** : le socle répond `NotExist` avec le handle en valeur pour tout handle qui
   n'appartient pas au personnage, donc un client peut sonder ses propres handles — sans
   distinguer « n'existe pas » de « n'est pas à moi » — exactement comme le chemin 253. Rien à
   faire si le dépôt accepte cette convention ; à confirmer.
6. **Le décompte des tests** : base 448 (§8.5) → 490. Si le PO veut un garde-fou automatique sur
   ce minimum, il faut une carte dédiée (aucun test d'inventaire du type « 366 minimum » n'existe
   dans `Tests/`).

