# 264 — `TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT`

Fiche d'archéologie de protocole, Epic 7.3. Écrite **en lecture seule** sur les trois références
(`reference/rzu`, `reference/ngemity`, `reference/client73`) : aucun exécutable client, aucun Lua,
aucun script du client n'a été lancé — la lecture du client est **statique** (désassemblage
`objdump -d` dans `/tmp/sframe.asm`, extraction de chaînes dans `/tmp/db_string.dump`). Le client 7.3
tranche le **sens** ; rzu tranche la **forme** et le gating ; NGemity **ne tranche rien** ici (§6).

Cette fiche porte un **lot résiduel** : l'étape 1 de 264 (énumération, lecteur de trame, bras de
réception, refus) est **déjà sur `master`** (§5.4). Ce qui est écrit ici, c'est le *sens* du champ
`rate`, le *gating* tranché pour 7.3, et la *conduite* retenue pour le reste.

## Résumé des arbitrages demandés

| Question posée par la carte | Verdict de cette fiche |
|---|---|
| Sens et unité de `rate` | **Établi par le client 7.3** (§2.3, §5.2) : une **part de la restitution demandée**, dans (0,1], `1.0f` = la totalité. Ni des points absolus, ni un pourcentage 0-100, ni un prix. |
| Bornes de `rate` | **Établies** : le client n'écrit que (0,1] (§2.3). Le domaine admissible pour le serveur est donc (0,1] et **tout le reste est refusé**, NaN et ±∞ compris (§5.5). |
| `target` en 7.3 | **Absent** : gardé `version >= EPIC_8_1` par rzu, postérieur à 7.3 (§4). La trame fait **11 octets** ; la forme 12 octets d'`EPIC_8_1` est **refusée**, pas tronquée. |
| Ce que le serveur restitue, à qui, sous quels plafonds, en consommant quoi | **NON ÉTABLI** (§7) : aucune des trois références ne le porte. Décision de Killian. |
| Quel paquet descendant porte le résultat | **Porteur identifié** (le champ `ethereal_durability` du bloc d'info d'objet de `TM_SC_INVENTORY`, 207) mais **pas de paquet à émettre** tant que la restitution n'existe pas (§5.3, §7). |
| 263 et 264 partagent-ils une conduite ? | **Deux trames, une seule conduite** : lire, borner, refuser. Les deux sont le même lobe C ; la branche de 263 (`hermes/packet-263-transmit-ethereal-durability`, MR #47) porte la mécanique d'extraction, que ce lot **ne rejoue pas** (§5.5). |
| Conduite retenue pour ce lot | **Refus** `TM_SC_RESULT` = `InvalidArgument` (28), après bornage du `rate` — la conduite de `master` **plus** la borne (§5.5). |

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id client → serveur | **264** (décimal) | `op_codes.md:92` |
| Nom | `TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT` | `op_codes.md:92` |
| Déclaration rzu | `TS_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT`, id `264` pour `version < EPIC_9_6_3` | `reference/rzu/librzu/src/packets/GameClient/TS_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT.h:12-14` |
| Déclaration NGemity | `TS_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT = 264` | `reference/ngemity/shared/Server/ClientPackets.h:105` |
| Trame jumelle (hors périmètre) | **263** `TM_CS_TRANSMIT_ETHEREAL_DURABILITY`, un `uint32` `handle` | `op_codes.md:91` ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h:5-6` |
| Sens (SNK local) | `TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT`, famille artisanat, lobe C « durabilité éthérée » | `docs/packet-specs/socle-artisanat-objets.md` §2.4, §3.6, §9.3.5 |
| État dans Navislamia | id, lecteur, bras de réception et refus **présents sur `master`** (§5.4) | `Game/Network/Packets/Enums/GamePackets.cs:61-62` ; `Game/Network/Packets/Game/GameActionPackets.cs:558-585` ; `Game/Network/Clients/GameClient.cs:1625-1626` ; `Game/Services/CraftingSocleService.cs:97-107` |

264 **ne porte aucun `handle`** : c'est la différence de fond avec 263, qui porte un `uint32` à
l'offset 7. Les deux trames font 11 octets en 7.3 et ne transportent pas la même chose — ne pas
réutiliser la fiche 263 comme s'il s'agissait de la même trame.

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 L'émetteur unique, mesuré

`SFrame.exe` contient **cinq** `mov reg,0x108` (0x108 = 264) au total, dont **deux seulement**
construisent une trame de 11 octets (`0x5EAE5B` et `0x5EB542`) ; les trois autres (`0x676DA2`,
`0x861433`, `0x88C601`) sont des usages sans rapport. Les deux émetteurs vivent dans la **même
fonction**, `0x5EA8E0`.

Cette fonction est atteinte par un **seul** appelant, `0x5EB640` (appelée en `0x5EB88E`), qui est le
**gestionnaire de messages d'interface** d'une classe d'écran. Identification par le RTTI MSVC de
`SFrame.exe` (vtable `0xA3C87C`, pointeur de localisateur `0xBC3E2C`) :

| Fonction | Classe propriétaire (RTTI) | Méthode de vérification |
|---|---|---|
| `0x5EB640` (gestionnaire), `0x5EA8E0` (émetteur) | **`.?AVSUIEquipmentWnd@@`** — la fenêtre d'équipement | vtable `0xA3C87C`, entrée `0xA3C944`, `vtable[-1] = 0xBC3E2C` |
| contrôle du procédé : `0x5FD840` (gestionnaire d'où part **263**) | `.?AVSUIRepairWnd@@` — la fenêtre de réparation | vtable `0xA49124`, `vtable[-1] = 0xBC6F7C` |

Le contrôle est ce qui valide la méthode : appliquée à `0x5FD840`, elle rend `SUIRepairWnd`, la
classe que la fiche 263 a identifiée **indépendamment** par ses chaînes RTTI. Les deux fenêtres
émettent donc chacune sa trame, et **264 vient de la fenêtre d'équipement** (`SUIEquipmentWnd`),
non de la fenêtre de réparation.

Le gestionnaire `0x5EB640` aiguille sur `[message+4]`, la clé du message d'interface, pour les clés
`0x45B`…`0x4D0` (1115…1232) via la table de sauts `0x5EB89C` et la table d'octets `0x5EB8BC`. La
seule entrée de cette table qui mène à l'appel `0x5EB88E` est l'index **117**, soit la clé
**`0x45B + 117 = 0x4D0` = 1232**. Autrement dit : **le joueur produit un message d'interface 1232 de
la fenêtre d'équipement, et le gestionnaire de cette clé envoie 264.**

### 2.2 La garde qui précède l'émission, et les textes qui la nomment

Avant de bâtir la trame, `0x5EA8E0` lit un objet porté par la fenêtre et un de ses champs :

```
0x5EA902  mov eax,[esi+0x480]      ; l'objet
0x5EA908  mov eax,[eax+0xbc]       ; le champ
0x5EA913  cmp eax,ebx (0)
0x5EA915  jg  0x5EA9A1             ; > 0 : on continue
0x5EA91B  push 0x1EE4              ; 0x1EE4 = 7908 : texte « pierre non chargée »
          … call 0x43F6D0 (texte) → 0x43DE10 → 0x490D60 → 0x6491C0 (message d'interface)
```

Le texte associé à la clé **7908** est, mot pour mot, `db_string.rdb` (dump
`/tmp/db_string.dump:151319-151320`, clé `msg_Ethereal_Durability_7908` à `:151319`) :

> You cannot repair your equipment as your Ethereal Stone is not charged.`<BR>`Please retry after charging your Ethereal Stone.

C'est le texte du client lui-même qui nomme ce que vaut ce champ : c'est **la charge de la pierre
éthérée**, et un zéro fait **refuser l'action côté client** — aucun paquet n'est envoyé. L'objet qui
la porte (`[wnd+0x480]`, champ `+0xbc`) n'est pas identifié plus avant par la lecture statique (§7).

Le second texte de la même famille, `msg_Ethereal_Durability_7901` (`/tmp/db_string.dump:151305-151306`),
nomme le résultat attendu :

> `#@Ethereal_Durability@#` durability has been recovered.

### 2.3 Les deux chemins d'émission, et ce qu'ils écrivent dans `rate`

`0x5EA8E0` additionne la **restitution demandée** pour les emplacements d'équipement de la fenêtre
(boucle de `0x5EAE3E` à `0x5EAE50`, pas de 0x14 sur le tableau `[wnd+0x580]`), puis la compare à une
**capacité** :

```
0x5EA9FC  imul eax,eax,0x2710   ; charge de la pierre × 10000 (0x2710)
0x5EAA02  mov  [ebp-0x58],eax   ; = la capacité
0x5EAE54  cmp  edi,[ebp-0x58]   ; edi = reste à restituer, cumulé
0x5EAE57  jg   0x5EAEB5         ; dépasse la capacité → chemin partiel
```

**Chemin A — la capacité couvre tout (saut non pris).** La trame est bâtie en clair et le champ
`rate` y est **la constante `1.0f`** :

| Adresse | Instruction | Ce qu'elle écrit |
|---|---|---|
| `0x5EAE5B` | `mov ecx,0x108` | id **264** |
| `0x5EAE60-0x5EAE6A` | remise à zéro du tampon | — |
| `0x5EAE6D` | `mov WORD PTR [ebp-0x30],cx` | id à l'**offset 4** |
| `0x5EAE71` | `mov DWORD PTR [ebp-0x34],0xb` | **longueur 11** à l'offset 0 |
| `0x5EAE80-0x5EAE88` | `add cl,[eax]` sur 6 octets | **somme de contrôle** à l'offset 6 (`0x5EAE8C`) |
| `0x5EAE8A` | `fld1` | `rate = 1.0f` |
| `0x5EAE92` | `fstp DWORD PTR [ebp-0x2d]` | `rate` à l'**offset 7** (base `ebp-0x34`) |
| `0x5EAE9F` | `call 0x649AD0` | envoi |

**Chemin B — la capacité ne couvre pas tout (`0x5EAEB5`).** Le client répartit alors la charge
disponible et écrit un `rate` **calculé** : `[ebp-0x50]` reçoit la part couverte pour l'objet
concerné multipliée par 100 (`0x5EB3C1-0x5EB3CF` : `fmul QWORD PTR ds:0xa19ec0`, et `0xa19ec0`
**vaut exactement `100.0`** en double), puis l'émetteur divise par ce même 100 :

| Adresse | Instruction | Ce qu'elle écrit |
|---|---|---|
| `0x5EB542` | `mov edx,0x108` | id **264** |
| `0x5EB552` | `mov DWORD PTR [ebp-0x68],0xb` | **longueur 11** |
| `0x5EB54E` | `mov WORD PTR [ebp-0x64],dx` | id à l'offset 4 |
| `0x5EB560-0x5EB568` | somme sur 6 octets | **somme de contrôle** à l'offset 6 (`0x5EB56D`) |
| `0x5EB56A` | `fld DWORD PTR [ebp-0x50]` | part couverte **en pourcentage** |
| `0x5EB570` | `fdiv QWORD PTR ds:0xa19ec0` | **÷ 100.0** |
| `0x5EB583` | `fstp DWORD PTR [ebp-0x61]` | `rate` à l'**offset 7** (base `ebp-0x68`) |
| `0x5EB586` | `call 0x649AD0` | envoi |

**Conséquence, et c'est elle qui tranche le sens de `rate` :** les deux seuls émetteurs du client
7.3 écrivent une valeur de **(0,1]**.

- chemin A : **exactement `1.0f`** ;
- chemin B : `part / 100`, où la part en pourcentage n'est atteinte **que** parce que le total
  **dépasse** la capacité (`jg 0x5EAE57`) : la part est donc **strictement inférieure à 1**, et
  strictement positive puisque l'objet est partiellement couvert.

`rate` est donc une **part de la restitution demandée** — un rapport sans unité, `1.0f` valant « la
totalité de ce que le client a demandé » — et **ni** des points de durabilité, **ni** un
pourcentage 0-100, **ni** un prix. Le client ne dispose d'aucun autre émetteur pour cette trame.

*Réserve de lecture* : la conclusion « (0,1] » ne dépend pas du détail de la répartition du chemin B
(la boucle de tri et d'allocation `0x5EB3A0-0x5EB3C4`, avec le comparateur `0x5E6D00`), qui n'est
pas retranscrit ici ligne à ligne ; elle dépend de deux faits mesurés — le chemin A écrit la
constante `1.0f`, et le chemin B n'est atteint que par le saut `jg 0x5EAE57`, donc sur une demande
**supérieure** à la capacité, ce qui interdit une part couverte égale à 100 %. L'entrée exacte dont
la part est écrite dans `rate` est celle qui épuise la capacité ; ce point n'est pas utilisé par la
conduite du §5.5, qui ne retient que le domaine.

### 2.4 Ce que les textes du client disent du geste

| Texte (clé) | Contenu | Source |
|---|---|---|
| `msg_Ethereal_Durability_7908` | You cannot repair your equipment as your Ethereal Stone is not charged… | `/tmp/db_string.dump:151319-151320` |
| `msg_Ethereal_Durability_7901` | `#@Ethereal_Durability@#` durability has been recovered. | `/tmp/db_string.dump:151305-151306` |
| `smsg_repair_item` | There are no items in need of repairs. | `/tmp/db_string.dump:197646-197647` |
| `smsg_lack_chargetherial` | Your Ethereal Stone does not have enough charge to repair this item. | `/tmp/db_string.dump:197648-197649` |
| `inventory_ui_text9738` / `_9739` | `<#99ff66>Restoration complete!<7.2>` / `<#b92c36>Restoration failed!<7.2>` | `/tmp/db_string.dump:189819-189822` |
| Aide de la fenêtre de réparation éthérée | « How do you repair ethereal items? **The Target Slot** — where you place the item you wish to repair or recover from 0 durability. **The Materials Slots** — … recharge your ethereal stone or help repair the item in the target slot. **The items in these slots will be destroyed!** … » | `/tmp/db_string.dump:222403` |
| Info-bulles des pierres | « Ethereal Stones are used for recharging the durability of equipment… increases in proportion to the item's store price… You cannot recharge the Ethereal stone exceeding **1000** » (et `10000` pour la seconde pierre) | `/tmp/db_string.dump:137571` et `:137583` |
| Info-bulles avec compteur | `#@Ethereal_Durability@# / 5000` … `/ 30000`, `/ 50000`, `/ 100000` | `/tmp/db_string.dump:137581`, `:137585`, `:137587`, `:137589` |
| Noms d'objets | `Ethereal Stone <Durability Restoration Stone><10,000k>`, `Big Ethereal Stone <…><100 Million>`, `Ethereal Restoration Stone <Restores Destroyed Item>` | `/tmp/db_string.dump:8613`, `:8617`, `:8633` |

Le mot du client pour 264 est **Restoration** (« Restoration complete! »), et l'aide décrit la
mécanique : un emplacement **cible** (l'objet à réparer ou à récupérer d'une durabilité à 0), des
emplacements de **matériaux** détruits à l'usage, et une **pierre éthérée** rechargée puis dépensée.
264 est le geste **pierre → équipement** ; 263 est le geste inverse (**équipement → pierre**), ce que
confirme la paire de textes `msg_Ethereal_Durability_7900` (« You have charged the Ethereal Stone
with `#@Ethereal_Durability@#` … extracted from the equipment item », `/tmp/db_string.dump:151303-151304`)
et `_7901`.

Les plafonds **1000 / 10000** que le socle §2.4 annonce viennent des info-bulles des **pierres**
(`:137571`, `:137583`), c'est-à-dire du **sens 263** (chargement) : ce sont des plafonds de
**charge**, pas de restitution, et ils ne sont donc **pas** repris ici (§7).

### 2.5 Méthode de lecture du client (reproductible)

```
SFrame.exe    md5 6fcf80ff1f2b5ae9a05ccc8e73d56746   (9 841 664 octets, jamais exécuté)
db_string.rdb md5 f0aeb4bc7dce4728ef3c5be8da8a421d
# désassemblage complet, en lecture, hors client :
objdump -d -M intel SFrame.exe > /tmp/sframe.asm                  # 2 256 750 lignes
# textes : /tmp/db_string.dump (255 814 lignes) est le dump de db_string.rdb déchiffré hors
# client avec la clé XOR corrigée documentée dans CLAUDE.md:1001-1010 (5 indices de la clé de
# DataCore diffèrent de celle du client) ; le déchiffrement n'a pas été rejoué dans cette session,
# le dump utilisé est celui déposé en /tmp (2026-09-21) et ses numéros de ligne sont ceux cités ici.
# Table de sauts (8 entrées, 0x5EB89C) et table d'octets (118 octets, 0x5EB8BC) : lues par adresse
# virtuelle avec la table de sections du binaire (.text 0x401000-0xA0B8CF, .rodata 0xA0C000,
# .rdata 0xA0F000-0xC0F6A8, .data 0xC10000). RTTI : vtable[-1] → CompleteObjectLocator
# (champ +0xC) → TypeDescriptor (nom en +0x8).
```

Les numéros de ligne cités sont ceux de **ces** dumps ; les adresses sont celles de `SFrame.exe`
(`ImageBase` `0x400000`). Aucune commande de cette section n'exécute le client.

## 3. Structure sur le fil — **11 octets, fixe**

| Offset | Taille | Type | Nom | Valeur en 7.3 | Source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` LE | `length` | **11** (`0xB`) | client `0x5EAE71` et `0x5EB552` |
| 4 | 2 | `uint16` LE | `id` | **264** (`0x108`) | client `0x5EAE5B`, `0x5EAE6D` et `0x5EB542`, `0x5EB54E` ; rzu `…TO_EQUIPMENT.h:13` ; `op_codes.md:92` |
| 6 | 1 | `uint8` | somme de contrôle | somme des octets 0-5, modulo 256 | client `0x5EAE80-0x5EAE88` (écriture `0x5EAE8C`) et `0x5EB560-0x5EB568` (écriture `0x5EB56D`) |
| 7 | 4 | **`float` IEEE-754 LE** | `rate` | `1.0f` (chemin A) ou `part/100` ∈ (0,1) (chemin B) — §2.3 | client `0x5EAE8A`+`0x5EAE92` (A), `0x5EB56A`+`0x5EB570`+`0x5EB583` (B) ; rzu `…TO_EQUIPMENT.h:6` |
| 11 | — | — | *(aucun autre champ)* | pas de `handle`, pas de `target` | rzu `…TO_EQUIPMENT.h:5-7` ; client : les deux émetteurs écrivent la longueur `0xB` |

**Taille totale attendue : 11 octets**, en-tête compris (en-tête rzu de 7 :
`reference/rzu/librzu/src/lib/Packet/PacketDeclaration.h:617-618`, `CREATE_PACKET_VER_ID` déclare
la constante `7`). Le client 7.3 écrit lui-même `11` dans le champ `length` (deux fois, §2.3) : la
taille n'est pas déduite, elle est **mesurée des deux côtés**.

Le `float` est **IEEE-754 petit-boutiste**, jamais un entier : `1.0f` s'écrit `00 00 80 3F`, et les
deux émetteurs le posent par `fld1`/`fstp` (`0x5EAE8A`, `0x5EAE92`) et par un `fdiv` suivi d'un
`fstp` (`0x5EB570`, `0x5EB583`) — jamais par un registre entier.

## 4. Gating de version — décisions prises pour 7.3

`EPIC_7_3 = 0x070300` (`reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59`) ; le dépôt travaille
sur cette seule version.

| Champ / propriété | Gating rzu | Valeur retenue pour 7.3 | Décision |
|---|---|---|---|
| `rate` (`float`) | aucun gating | **présent** | lu à l'offset 7, 4 octets IEEE-754 LE (déjà le cas sur `master`) |
| `target` (`int8_t`) | `version >= EPIC_8_1` — `…TO_EQUIPMENT.h:7` ; `EPIC_8_1 = 0x080100`, `PacketEpics.h:61` | **absent** | **non lu**, et la forme 12 octets doit être **refusée** (pas tronquée) : en 7.3 la trame ne désigne que le joueur (`:9`, « target = 0 for player or X for summon (1 to 6) ») |
| Id du paquet | `X(264, version < EPIC_9_6_3)` / `X(1264, version >= EPIC_9_6_3)` — `…TO_EQUIPMENT.h:12-14` ; `EPIC_9_6_3 = 0x090603`, `PacketEpics.h:96` | **264** | `0x070300 < 0x090603` : la borne haute (le remap vers `1264`) est **postérieure** à 7.3. La borne est lue dans **cet** en-tête, pas recopiée de 263 |
| Existence du paquet | `// Since EPIC_7_2` — `…TO_EQUIPMENT.h:11` ; `EPIC_7_2 = 0x070200`, `PacketEpics.h:58` | **existe en 7.3** | `0x070200 <= 0x070300` |
| Base d'en-tête | `CREATE_PACKET_VER_ID` → constante `7` — `PacketDeclaration.h:617-618` | 7 octets | longueur + id + somme de contrôle |

Aucun champ de cette trame n'a de gating non statué pour 7.3. La somme de contrôle à l'offset 6
n'est pas un champ de rzu : c'est un octet du client, mesuré (§3), que le serveur **n'a pas à
valider** (le lecteur de `master` borne la longueur, l'id est déjà consommé par la boucle de
réception).

## 5. Traitement attendu

### 5.1 Ce que NGemity (`Chihiro`) porte réellement : rien d'exploitable

| Élément | Constat | Source |
|---|---|---|
| Déclaration | `TS_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT = 264`, plus l'inclusion de l'en-tête | `reference/ngemity/shared/Server/ClientPackets.h:105` ; `shared/Server/XPacket.h:168` |
| En-tête | `_(simple)(float, rate)` + `_(simple)(int8_t, target, version >= EPIC_8_1, 0)`, `CREATE_PACKET(…, 264)` | `shared/Server/Packets/GameClient/TS_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT.h:6-8`, `:13` |
| **Gestionnaire** | **aucun** : `grep -rn "TRANSMIT_ETHEREAL" reference/ngemity` ne rend que les en-têtes ci-dessus (deux inclusions, deux déclarations, deux définitions) — aucun `case`, aucun `WorldSession::`, aucun `MixManager` | vérifié à ce passage |
| Le vocabulaire vivant est celui du mix | `MIX_SACRIFICE_ITEM_FOR_ETHEREAL_DURABILITY = 801`, `MIX_TRANSMIT_ETHEREAL_DURABILITY = 802`, `MIX_RECOVER_EXHAUSTED_ETHEREAL_DURABILITY = 803` | `Chihiro/src/Crafting/MixManager.h:44-46` |
| Ces trois types de mix ne sont **jamais lus** | le seul `type` consommé par le moteur de mix est la famille `MIX_ENHANCE` | `Chihiro/src/Crafting/MixManager.cpp:63`, `:78`, `:82`, `:93`, `:108` |
| Même l'usure éthérée est **désactivée** | l'appel `ProcEtherealDurabilityConsumption` est à l'intérieur d'un bloc `/* … */` précédé de `// @todo: Endurance` | `Chihiro/src/Entities/Unit/Unit.cpp:1022`, `:1024-1033` (appels `:1030-1031`) |
| Version de référence | `#define EPIC EPIC_4_1_1` — NGemity compile ses trames pour 4.1.1, sans `EPIC_VER_ID` pour cette famille | `reference/ngemity/shared/Common/Define.h:25` |

Conclusion : **NGemity ne fournit ni conduite, ni constante, ni code de retour** pour 264. Il ne
tranche que la forme (déjà tranchée par rzu) et le vocabulaire (`mix_type` 801/802/803, qui appartient
à la carte 256 — lobe A, hors périmètre).

### 5.2 Ce que le serveur peut établir par lecture (décidable maintenant)

1. **La forme** : 11 octets fixes, un `float` LE à l'offset 7, ni `handle`, ni `target` (§3, §4).
2. **Le sens de `rate`** : une part de la restitution demandée, dans (0,1] (§2.3). Ce n'est plus
   « une unité non établie » : c'est ce que les deux émetteurs du client 7.3 écrivent.
3. **Le domaine admissible** : (0,1]. Le client ne peut pas produire autre chose — le chemin A écrit
   `1.0`, le chemin B n'est atteint que quand la demande dépasse la capacité et écrit alors une part
   strictement inférieure à 1 et strictement positive. Un `rate` de 0, négatif, supérieur à 1, NaN ou
   infini **n'est pas une trame que le client 7.3 sait produire**.
4. **Les porteurs de l'effet existent déjà dans le dépôt** (mais appliquer l'effet est une décision
   de jeu, §5.3) :
   - la durabilité éthérée **portée par un objet** : `ItemEntity.EtherealDurability`
     (`Game/DataAccess/Entities/Telecaster/ItemEntity.cs:30`), `ItemResourceEntity.EtherealDurability`
     (`Game/DataAccess/Entities/Arcadia/ItemResourceEntity.cs:36`), colonne `ethereal_durability`
     (`ArcadiaSchemaPSQL.sql:2219`), recopiée par `Game/Services/StorageRules.cs:136` ;
   - sa **sérialisation vers le client** : `Game/Network/Packets/Game/ItemFixedInfoWriter.cs:20`, `:47`,
     `:89` écrit `item.EtherealDurability` à l'offset **24** du bloc d'info fixe, utilisé par la
     famille d'inventaire (`Game/Network/Packets/Game/GameCharacterPackets.cs:360`) — et c'est
     exactement le champ `ethereal_durability` que `TS_SC_INVENTORY` (207, présent sur `master` :
     `GamePackets.cs:33`) porte en 7.3 (`reference/rzu/librzu/src/packets/GameClient/TS_SC_INVENTORY.h:63`
     et `:75`, gating `version >= EPIC_6_3 && version < EPIC_9_8_1`) ;
   - la **charge de pierre du personnage** : `CharacterEntity.EtherealStoneDurability`
     (`Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:53`), déjà envoyée au client comme
     propriété `ethereal_stone` (`Game/Network/Clients/Actions/GameActions.cs:275`).

### 5.3 Ce qui n'est pas décidable sans Killian

- **Le montant restitué** : quelle durabilité rendue par point de charge. Le client calcule sa propre
  arithmétique (capacité `= charge × 10000`, `0x5EA9FC`, et des parts en pourcentage, §2.3) mais rien
  dans les trois références ne dit ce que le **serveur** doit créditer. La formule « proportionnelle
  au prix de l'objet » (`/tmp/db_string.dump:137571`) est une phrase d'info-bulle du sens **263**,
  sans constante : elle ne suffit pas.
- **Ce qui est consommé** : la pierre (combien de charge), des matériaux (« The items in these slots
  will be destroyed! », `/tmp/db_string.dump:222403`), rien ?
- **Le cas « objet détruit »** : `msg_Ethereal_Durability_7902` « You can't equip a destroyed item »
  et l'aide « repair **or recover from 0 durability** » (`:222403`) décrivent deux gestes distincts
  (réparer / ressusciter un objet à 0) ; lequel des deux passe par 264 n'est pas établi.
- **Les plafonds** : 1000/10000 sont ceux de la **charge** (§2.4), pas de la restitution.
- **Le paquet descendant** : le **porteur** est identifié (§5.2.4) mais rien ne dit si le serveur
  officiel renvoie l'inventaire complet ou un rafraîchissement plus léger, ni quel texte
  (`msg_Ethereal_Durability_7901`, `inventory_ui_text9738`) il envoie — les liaisons de textes des
  fenêtres vivent dans les `.nui`, absents de `reference/client73/`.
- **L'articulation avec 263/260/262 et `mix_type` 801/802/803**.

Tout cela va en `## A VERIFIER PAR KILLIAN` (§7).

### 5.4 État du dépôt à `b56967a` (ce qui est déjà là, ce qui manque)

| Élément | État | Source |
|---|---|---|
| Id en énumération | fait | `GamePackets.cs:56-62` (`TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT = 264`) |
| Record de requête | fait | `GameActionPackets.cs:564` — `TransmitEtherealDurabilityToEquipmentRequest(float Rate)` |
| Lecture bornée | faite | `GameActionPackets.cs:571-585` — exactement 11 octets, `ReadSingleLittleEndian` à l'offset 7 |
| Bras de réception | fait, **commun** à 256/260/262/263/264 | `GameClient.cs:1622-1629` → `CraftingSocleService.HandleAsync` |
| Conduite | lecture + bornage + refus | `CraftingSocleService.cs:97-107` (`handles = Array.Empty<uint>()`) puis `:115-125` (refus) |
| Résolution de handles | **sans objet** : 264 ne nomme aucun handle ; aucune surcharge de `CraftingSocleRules` ne lui correspond | `Game/Services/CraftingSocleRules.cs:70-76` |
| Tests de trame | faits | `Tests/Game/CraftingSoclePacketsTests.cs:33`, `:308-349` |
| **Domaine du `rate`** | **absent** : le lecteur accepte n'importe quel `float` (`:323-336`) et le service jette la valeur (`out _`, `CraftingSocleService.cs:100`) | constat |

### 5.5 Conduite retenue, et le lot dev qui en découle

**Décision de cette fiche.** La restitution n'est pas implémentable (§5.3) : le patron du dépôt est
donc conservé — **lire, borner, refuser** — et le lot résiduel y ajoute **la seule chose que la
lecture du client rend décidable** : le **domaine du `rate`**. Concrètement :

1. **Ne pas toucher au lecteur.** `TryReadTransmitEtherealDurabilityToEquipment` continue de rendre
   le `float` tel quel : c'est la convention du fichier (le lecteur porte la forme, pas la politique)
   et les tests `:323-336` le verrouillent déjà.
2. **Borner dans les règles.** Une règle publique dans `CraftingSocleRules`
   (p. ex. `IsRestorableRate(float rate)`) rend vrai pour `rate > 0f && rate <= 1f`, faux pour tout
   le reste. La conjonction rejette déjà `NaN` (`NaN > 0f` est faux) **et** l'infini (`±∞ <= 1f` est
   faux) : ces deux cas doivent être **testés nommément**, car un `NaN` qui franchirait la règle
   entrerait sans bruit dans n'importe quel calcul d'objet plus tard.
3. **Refuser hors domaine.** Dans `CraftingSocleService.cs:97-107`, lire le `rate` (au lieu de
   `out _`) et, s'il est hors domaine, refuser : `TM_SC_RESULT` (0) avec `request_msg_id = 264`,
   `result = InvalidArgument` (28) et `value = 0` — le même refus que la conduite actuelle, avec une
   ligne de journal distincte qui dit *pourquoi* (valeur hors du domaine produit par le client 7.3).
4. **Ce que le serveur répond quand la trame est bien formée et le `rate` dans le domaine** : le
   refus `InvalidArgument` déjà en place (`CraftingSocleService.cs:120-125`), et **rien d'autre** :
   aucune restitution, aucun objet consommé, aucune ligne d'inventaire réécrite. Le lot **n'écrit
   aucun handler qui trancherait à la place de Killian**, et **n'ouvre aucun service parallèle** du
   même nom que ceux de la branche 263 (`EtherealDurabilityRules`, `EtherealSacrificeCatalog`) : ces
   fichiers vivent sur `hermes/packet-263-transmit-ethereal-durability` et sont hors périmètre.
5. **Réponse descendante** : aucune, pour cette raison-là. Le seul paquet de la famille est le refus
   `TM_SC_RESULT` (`Game/Network/Packets/Game/TS_SC_RESULT.cs:5-17` : `RequestMsgID`, `Result`,
   `Value`, 8 octets utiles ; envoi `GameClient.cs:68-72`). Le porteur du résultat d'une **vraie**
   restitution (§5.2.4) n'est pas émis tant que la restitution n'existe pas.

**Tests que le lot doit ajouter (et qui « mordent », critère 3 du lot dev)** :

| Test | Ce qu'il verrouille |
|---|---|
| `CraftingSoclePacketsTests` — offsets : `rate` écrit par `BinaryPrimitives.WriteSingleLittleEndian(bytes, 7, 1.0f)`, puis lecture du **quadruplet brut** `00 00 80 3F` **et** de `Rate == 1.0f` | l'offset **et** l'encodage IEEE-754 LE à la fois : une lecture entière ou grand-boutiste échoue |
| Idem avec `0.5f` (`00 00 00 3F`) | que 4 octets sont lus, ni 2 ni 8 |
| Domaine : `[TestCase(0f)]`, `[-1.5f]`, `[100f]`, `[float.NaN]`, `[float.PositiveInfinity]`, `[float.NegativeInfinity]` → règle fausse | la borne, NaN et ±∞ compris |
| Domaine : `[TestCase(0.5f)]`, `[TestCase(1f)]` → règle vraie | que le domaine du client passe |
| Le lecteur **reste permissif** : les cas `0f` / `-1.5f` / `100f` existants (`:323-336`) restent verts | que la borne est dans les règles, pas dans le lecteur — ne pas déplacer la borne |
| La forme 12 octets d'`EPIC_8_1` reste refusée (`:338-349`) | que `target` n'est pas lu en 7.3 |
| `Enum.IsDefined(264)` et le bras de réception existant (`:24-45`) | qu'aucun membre de `GamePackets` n'atteint le `throw` final |

Le test d'offsets **existe déjà** dans sa forme minimale (`:308-321`) : il est **renforcé** (quadruplet
brut + cas négatifs ci-dessus), jamais réécrit à l'identique ni affaibli.

### 5.6 Base mesurée sur ce poste (2026-09-25, branche de cette fiche)

```
export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
dotnet build Navislamia.sln -c Debug   → code de sortie 0 (0 erreur, 23 avertissements)
dotnet test  Tests/Tests.csproj        → code de sortie 0 — Failed: 0, Passed: 1302, Skipped: 0
git rev-parse origin/master            → b56967a07430422add88e0e5cdf292b41b18f6c6
git log --oneline origin/master..master → vide
```

Le dépôt est en NUnit (`[Test]`, `[TestCase]`, FluentAssertions). Le compte de tests de `master` est
**1302** : le lot dev ne peut pas descendre en dessous.

## 6. Écarts assumés avec NGemity

| Écart | Raison |
|---|---|
| **Aucune conduite reprise de NGemity** | Il n'en a aucune : déclaration sans gestionnaire, `mix_type` 801-803 déclarés mais jamais lus (`MixManager.cpp:63-108`), usure éthérée commentée (`Unit.cpp:1024-1033`). Le socle §6.3 disait « rien de NGemity à porter » : le passage le confirme, y compris pour le mix. |
| **Le gating est celui de rzu, pas celui de NGemity** | NGemity compile en `EPIC_4_1_1` (`shared/Common/Define.h:25`) et déclare l'id `264` en dur, sans `EPIC_VER_ID` : sa borne haute (le remap vers `1264`) n'existe pas chez lui. La borne est lue dans rzu (`…TO_EQUIPMENT.h:12-14`) — et elle est postérieure à 7.3. |
| **Le sens de `rate` ne vient pas des serveurs, mais du client** | Aucun serveur de référence ne distingue une part d'un montant ; le client 7.3, lui, écrit `1.0f` ou une part (§2.3). Quand rzu et NGemity se taisent, c'est le client qui tranche — et il tranche ici. |

## 7. NON ÉTABLI

1. **Le montant restitué** : combien de durabilité par point de charge éthérée, et selon quelle
   formule exacte (l'arithmétique du client §2.3 — capacité `= charge × 10000`, parts en pourcentage —
   n'est **pas** une règle serveur, et rien ne dit qu'elle soit la bonne). Question précise : *quelle
   est la formule de conversion charge → durabilité rendue, et sur quel champ d'objet elle s'applique ?*
2. **Ce qui est consommé** : charge de pierre uniquement, ou aussi des objets d'emplacement
   « Materials » (« The items in these slots will be destroyed! », `/tmp/db_string.dump:222403`) ?
   Question précise : *264 consomme-t-il des matériaux, et lesquels ?*
3. **Réparer ou ressusciter** : 264 sert-il à réparer un objet usé **et** à récupérer un objet à
   durabilité 0, ou seulement l'un des deux ? Le client décrit les deux cas d'un même souffle (aide
   de la fenêtre, `/tmp/db_string.dump:222403` : « the item you wish to repair **or** recover from 0
   durability ») et possède deux textes voisins, `msg_Ethereal_Durability_7902`
   (`/tmp/db_string.dump:151307-151308`, « You can't equip a destroyed item ») et `_7903`
   (`:151309-151310`, « You can't use a destroyed item anymore… »). Le champ `rate` ne les distingue pas.
4. **L'identité de l'objet visé** : 264 ne nomme **aucun** objet (ni `handle`, ni slot) : le serveur
   doit-il considérer tout l'équipement porté, ou s'appuyer sur un état de fenêtre qu'il ne connaît
   pas encore ? Question précise : *sans handle, comment le serveur sait-il quel objet restaurer ?*
5. **Les plafonds de restitution** : les 1000/10000 connus sont ceux de la **charge** (§2.4) ; ceux de
   la restitution ne sont écrits nulle part dans les trois références.
6. **Le paquet descendant** : le porteur du champ est `TS_SC_INVENTORY` (207) et le champ
   `ethereal_durability` du bloc d'info fixe (`ItemFixedInfoWriter.cs:89`, offset 24), mais *quel*
   paquet le serveur officiel renvoie après une restitution, et avec quel texte
   (`msg_Ethereal_Durability_7901` ? `inventory_ui_text9738` ?), n'est pas lisible dans les
   artefacts disponibles : les liaisons de textes des fenêtres vivent dans les `.nui`, absents de
   `reference/client73/`, et le client ne pousse **aucun** de ces deux ids par une constante
   (`recherche de `push 0x1ed*` dans `/tmp/sframe.asm` → seule `0x1EDE` (7902) apparaît ;
   `push 0x260a` et `push 0x260b` → aucune occurrence ; le seul `0x1EDE` est poussé en `0x4A918E`,
   sur le chemin d'équipement d'un objet détruit).
   Ce constat **ne prouve pas** que ces textes soient serveur : il ne permet pas de trancher.
7. **L'objet porteur de la charge côté client** : l'objet lu en `[wnd+0x480]`, champ `+0xbc`
   (`0x5EA902-0x5EA908`), est celui dont un zéro déclenche `msg_Ethereal_Durability_7908` — c'est la
   charge de la pierre ; mais de quel objet il s'agit (la pierre posée dans la fenêtre, la valeur du
   personnage) n'est pas établi par la lecture statique.
8. **Le widget qui produit le message d'interface 1232** : la clé est mesurée (§2.1), le geste exact
   du joueur (bouton « Restore », glisser-déposer, menu contextuel) ne l'est pas — le nom des
   messages n'est pas lisible dans `SFrame.exe` et les `.nui` sont absents du dossier de référence.

## 8. Commits épinglés et méthode de reproduction

| Référence | Commit | Fichiers décisifs |
|---|---|---|
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | `librzu/src/packets/GameClient/TS_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT.h:5-16` ; `…TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h:5-13` ; `librzu/src/lib/Packet/PacketDeclaration.h:617-618` ; `librzu/src/lib/Packet/PacketEpics.h:56,58,59,61,96` ; `librzu/src/packets/GameClient/TS_SC_INVENTORY.h:63,75,84` |
| NGemity | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | `shared/Server/ClientPackets.h:105` ; `shared/Server/XPacket.h:168` ; `shared/Server/Packets/GameClient/TS_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT.h:6-13` ; `shared/Server/Packets/GameClient/TS_SC_INVENTORY.h:18` ; `shared/Common/Define.h:25` ; `Chihiro/src/Crafting/MixManager.h:44-46` ; `Chihiro/src/Crafting/MixManager.cpp:63,78,82,93,108` ; `Chihiro/src/Entities/Unit/Unit.cpp:1022,1024-1033` |
| Client 7.3 | **pas de dépôt git** : `SFrame.exe` md5 `6fcf80ff1f2b5ae9a05ccc8e73d56746` (9 841 664 octets), `db_string.rdb` md5 `f0aeb4bc7dce4728ef3c5be8da8a421d` | adresses citées en §2, §3 ; textes en §2.4 |
| Dépôt | base `b56967a07430422add88e0e5cdf292b41b18f6c6` (`origin/master`) | `GamePackets.cs:56-62` ; `GameActionPackets.cs:558-585` ; `GameClient.cs:1622-1629` ; `CraftingSocleService.cs:97-107,115-125` ; `CraftingSocleRules.cs:70-76` ; `Tests/Game/CraftingSoclePacketsTests.cs:24-45,308-349` |

## 9. Lot dev — ce qui a été livré, et ce que les tests mordent

Implémenté sur la branche de cette fiche (`hermes/packet-264-transmit-ethereal-durability-to-equipment`),
en suivant la conduite du §5.5. Aucune décision de jeu n'a été prise : le lot **borne** et **refuse**.

### 9.1 Ce qui a changé

| Élément | Fichier | Ce qu'il fait |
|---|---|---|
| `IsRestorableRate(float rate)` | `Game/Services/CraftingSocleRules.cs:92` | `rate > 0f && rate <= 1f` — **le seul endroit du dépôt où vit le domaine (0,1]** |
| Bras 264 de `HandleAsync` | `Game/Services/CraftingSocleService.cs:100-124` | lit le `rate` (`out var restoration`, plus de `out _`), refuse hors domaine par `TM_SC_RESULT` + `InvalidArgument` (28), valeur 0, avec une ligne de journal distincte (« rate outside the domain the Epic 7.3 client can produce ») ; dans le domaine, la conduite existante (refus `InvalidArgument`) est inchangée |
| Lecteur | `Game/Network/Packets/Game/GameActionPackets.cs:571-585` | **comportement inchangé** : exactement 11 octets, `float` LE à l'offset 7 tel quel. Seul le commentaire de documentation a été corrigé — il affirmait encore « the unit of the rate is not established », ce que le §2.3 établit désormais |

Ce qui n'a **pas** été touché, et c'est un choix : l'énumération (`GamePackets.cs`), le bras de réception
commun 256/260/262/263/264 (`GameClient.cs:1622-1630`), le `target` d'`EPIC_8_1` (toujours non lu, forme
12 octets toujours refusée), l'id 264 du 7.3. Aucun service parallèle du genre de ceux de la branche 263
(`EtherealDurabilityRules`, `EtherealSacrificeCatalog`) n'a été ouvert, aucune restitution, aucune
consommation, aucun paquet descendant.

### 9.2 Tests ajoutés, et la mutation que chacun tue

Chaque ligne a été vérifiée par **mutation** : la mutation est appliquée dans un *worktree* détaché du
commit livré, `dotnet test` est lancé, puis le worktree est rendu propre. Un test qui ne tombe sur aucune
mutation n'a pas été écrit.

| Test | Verrou | Mutation jouée → ce qui tombe |
|---|---|---|
| `..._ReadsTheFullRateQuadruplet0000803FAtSeven`, `..._ReadsTheHalfRateQuadruplet0000003FAtSeven` (`CraftingSoclePacketsTests`) | le **quadruplet brut** à l'offset 7 : `ReadUInt32LittleEndian(packet[7..11]) == 0x3F800000` pour `1.0f` (`00 00 80 3F`) et `0x3F000000` pour `0.5f` (`00 00 00 3F`), **et** `Rate`, **et** la taille totale 11 | lecteur lisant les 4 octets comme un entier (`(float)ReadInt32LittleEndian`) → **5 échecs** |
| `..._RefusesTheTwelveByteEightOneForm` | que `target` n'est pas lu en 7.3 | lecteur acceptant 12 octets (`!=` → `<`) → **1 échec** |
| `..._DoesNotInterpretTheRate` (`0f`, `1f`, `100f`, `-1.5f`) | que la borne **n'est pas dans le lecteur** | inchangé (test de `master`, commentaire mis à jour) |
| `IsRestorableRate_AcceptsTheDomainTheEpic73ClientWrites` (`0.5f`, `1f`, `0.0001f`) | le domaine du client | règle déplacée en `rate >= 0f && rate < 1f` → **2 échecs** |
| `IsRestorableRate_RefusesEveryRateTheEpic73ClientCannotProduce` (`0f`, `-1.5f`, `1.0000001f`, `100f`, `NaN`, `+∞`, `-∞`) | la borne basse, `NaN` et ±∞ nommément (la conjonction les rejette : `NaN > 0f` est faux, `±∞ <= 1f` est faux) | idem ci-dessus |
| `CraftingFamily_IsDispatchedBeforeTheUnknownPacketThrow` (5 ids, scan de source) | qu'aucun membre de la famille n'atteint le `throw` final | retrait de 264 du bras de `GameClient` → **1 échec** |
| `CraftingSocleServiceTests.Handle264_*` (nouveau fichier) : refus hors domaine (6 valeurs) et refus dans le domaine (`0.5f`, `1f`) par `TM_SC_RESULT` 264 + `InvalidArgument` + valeur 0, forme 12 octets refusée par le service, silence avant l'entrée dans le monde | que le service **répond** le refus du §5.5 et que rien n'est crédité ni consommé | comportement inchangé par la mutation du câblage (§9.3) |
| `CraftingSocleServiceTests.TheTwoSixFourArm_BoundsTheRateWithTheSocleRule` (scan de source) | que le bras 264 appelle bien la règle | bras muté en `if (false)` → **1 échec** (le seul) |

Mesure de la suite complète : **1329 tests, `Failed: 0`, `Passed: 1329`** (la base de `master` en compte
1302 ; +27 cas, aucun retiré, aucun affaibli).

### 9.3 Réserve explicite — le refus hors domaine n'est pas distinguable de l'autre refus

Le §5.5.3-4 prescrit **la même** réponse `TM_SC_RESULT` + `InvalidArgument` + valeur 0 pour un `rate` hors
domaine et pour un moteur non écrit : le bras n'ajoute donc **aucune observable** qu'un client ou un test
boîte-noire puisse distinguer. C'est mesuré, pas supposé — avec le bras muté en `if (false)`, les tests de
comportement de `CraftingSocleServiceTests` restent **tous verts** et seul le scan de source tombe.

Conséquence assumée : le domaine est verrouillé par le test de la règle (§9.2, lignes 4-5) et le câblage par
le scan de source. La seule observable qui distingue les deux refus est la **ligne de journal**, et aucun
test du dépôt ne lit les journaux.

### 9.4 Base mesurée sur ce poste (dev, 2026-09-25)

```
export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
dotnet build Navislamia.sln -c Debug    → code de sortie 0 (0 erreur ; 164 avertissements, aucun nouveau)
dotnet test  Tests/Tests.csproj         → code de sortie 0 — Failed: 0, Passed: 1329, Skipped: 0
git log --oneline origin/master..master → vide (aucun commit sur master locale)
```

### 9.5 Ce qui reste NON ÉTABLI

Rien de neuf, et rien de deviné : le §7 est l'état après ce lot. Le montant restitué, ce qui est consommé,
réparer ou ressusciter, l'identité de l'objet visé, les plafonds et le paquet descendant attendent
l'arbitrage de Killian, et le lot ne les préjuge pas — il refuse comme avant, mais refuse désormais en
connaissant le domaine du champ qu'il reçoit.

## A VERIFIER PAR KILLIAN

1. **Sens et bornes de `rate`** — cette fiche l'établit **par le client 7.3** : part de la restitution,
   dans (0,1], `1.0` = totalité (§2.3) ; le lot borne à (0,1] et refuse le reste (NaN, ±∞ compris).
   La borne est-elle la bonne politique, ou faut-il accepter un `rate` hors domaine (client modifié,
   version future) et le plafonner au lieu de refuser ?
2. **Un `rate` de 0** (aucune restitution demandée) est aujourd'hui refusé comme invalide : préférez-vous
   une réponse neutre, un silence, ou ce refus ?
3. **Ce que la restitution crédite et consomme** : formule charge → durabilité, charge dépensée,
   matériaux détruits, objet cible (§7.1, §7.2, §7.4). Impossible à sourcer : vient de vous.
4. **Réparer et/ou ressusciter** un objet à 0 (§7.3).
5. **Les plafonds de restitution** (les 1000/10000 connus sont des plafonds de **charge**, §2.4).
6. **Le paquet descendant** : réémission de l'inventaire (`TS_SC_INVENTORY`, 207) ou rafraîchissement
   plus léger, et avec quel texte (`msg_Ethereal_Durability_7901` / `inventory_ui_text9738`) — §7.6.
7. **263 et 264 : une conduite ou deux ?** Cette fiche retient **une** conduite (lire, borner, refuser)
   pour les deux, et **ne rejoue pas** la mécanique d'extraction de la branche 263
   (`hermes/packet-263-transmit-ethereal-durability`, MR #47) : confirmez que la restitution (264) et
   l'extraction (263) sont bien deux volets d'une seule politique, à trancher ensemble.
8. **Collision de branches** : 264 vit dans `CraftingSocleService.cs`, que la branche #47 modifie
   (+71 −8). Si #47 est mergée avant, le lot 264 devra rebâtir sur la nouvelle version du fichier ;
   dites-nous si vous préférez sérialiser les deux MR.

## Bloc prêt à coller dans `CLAUDE.md` (par `navis-qa`, dans la description de la MR)

> **264 `TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT` — 11 octets fixes, un `float rate` à
> l'offset 7, ni `handle` ni `target`.** `target` est gardé `version >= EPIC_8_1` par rzu
> (`TS_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT.h:7`) : il est **absent** de 7.3 et la forme
> 12 octets doit être **refusée**, jamais tronquée. L'id 7.3 est `264` (`version < EPIC_9_6_3`, rzu
> `:12-14`) et la trame existe depuis `EPIC_7_2` (`:11`). Ne pas confondre avec **263**, qui fait
> 11 octets aussi mais porte un `uint32 handle`.
>
> **`rate` est une part de la restitution demandée, pas un montant.** Mesuré dans le client 7.3
> (`SFrame.exe` md5 `6fcf80ff1f2b5ae9a05ccc8e73d56746`) : l'unique émetteur (fenêtre d'équipement,
> classe RTTI `.?AVSUIEquipmentWnd@@`, clé de message d'interface 1232) écrit **`1.0f`** quand la
> charge de la pierre couvre la demande (`0x5EAE8A` `fld1` → `0x5EAE92`), et **`part/100`** — donc
> strictement moins de 1 — quand elle ne la couvre que partiellement (`0x5EB570` `fdiv [100.0]` →
> `0x5EB583`). Le domaine du client est donc **(0,1]** : le lecteur de trame reste permissif
> (`GameActionPackets.cs:571-585`), la **borne** vit dans les règles, et tout `rate` hors domaine,
> NaN ou ±∞ est refusé par `TM_SC_RESULT` + `InvalidArgument`. Le reste — montant rendu, charge
> dépensée, matériaux consommés, paquet descendant — n'est pas sourçable et attend l'arbitrage de
> Killian. Voir `docs/packet-specs/264-transmit-ethereal-durability-to-equipment.md`.
