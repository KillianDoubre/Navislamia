# 215 — `TM_CS_PUTOFF_CARD` (retrait d'une carte / pierre d'âme d'une châsse)

Fiche de paquet structurante. Elle est **la principale analyse disponible pour 215** : NGemity n'a
aucun handler, rzu n'a aucun serveur, et le seul producteur côté client 7.3 identifié par lecture
statique est la **sous-commande 6** du même répartiteur interne qui produit `TM_CS_PUTON_CARD` (214,
sous-commande 5). Les deux paquets sont donc un **couple in/out émis par le même geste** — c'est le
fait le plus important de cette fiche, et il change la lecture « symétrique avec 201 » qui avait
cours pour la 214.

| | |
|---|---|
| Id (Epic 7.3) | **215** (`op_codes.md:64`) |
| Sens | client → serveur uniquement (aucun jumeau `TM_SC_*` dans rzu ni NGemity) |
| Taille | **8 octets** (en-tête 7 + `int8`), confirmée par le constructeur de trame du client |
| Gating | `1215` à partir d'`EPIC_9_6_3` ; **non applicable** en 7.3 |
| Complexité de lecture | **élevée** — le champ est *calculé* par le client, pas recopié (voir §2.4) |

---

## 1. Identité

- `docs/…`/`op_codes.md:64` : `[215] = "TM_CS_PUTOFF_CARD"`. Le voisin immédiat est
  `[214] = "TM_CS_PUTON_CARD"` (`op_codes.md:63`), et `[216] = "TM_SC_BELT_SLOT_INFO"` (`:65`).
- `master` de Navislamia **ne contient pas** ce membre : `Game/Network/Packets/Enums/GamePackets.cs`
  (102 lignes) s'arrête à `TM_SC_TAKE_ITEM_RESULT = 210` (l.36) puis `TM_SC_BELT_SLOT_INFO = 216`
  (l.37). `TM_CS_PUTON_CARD = 214` **n'existe que sur la branche 214** (voir §5.4) ; il n'y a donc
  aucune valeur d'énumération à réutiliser pour 215 sur `master`.

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Le nom du paquet dans le client 7.3 (preuve de vocabulaire)

Le client possède une table `id → nom` (`std::map`) utilisée pour le journal de paquets. L'entrée de
215 y est collée à celle de 214 :

| site (VA) | instruction | rôle |
|---|---|---|
| `0x6766f9` | `push $0x11` | longueur du nom (17 caractères) |
| `0x6766fb` | `push $0xa536fc` | pointeur sur la chaîne `TM_CS_PUTOFF_CARD` |
| `0x67672f` | `mov $0xd7,%eax` | 215 |
| `0x67674e` | `call 0x674350` | insertion dans la table |

- La chaîne est au fichier `0x6520fc` = VA `0xa536fc` (delta `.rdata` = `0x401600`), soit
  `strings -n 4 SFrame.exe` **ligne 26384** (`TM_CS_PUTON_CARD` en **26385**).
- L'entrée 214 est juste au-dessus : `0x6766b9` = `mov $0xd6,%eax`, chaîne `TM_CS_PUTON_CARD`.
- Binaire lu : `SFrame.exe`
  `sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`.

### 2.2 Le client possède un **constructeur de trame** pour 215 — correction de la fiche 214

La fiche `214-puton-card.md` §2.3 concluait qu'aucun constructeur de trame portant l'id 214/215 en
dur n'existe. **C'est inexact**, et cela vaut pour les deux paquets :

| VA | fonction | ce qu'elle écrit |
|---|---|---|
| `0x4a50d0`–`0x4a511a` | constructeur du **214** | `movl $0x7,(%eax)` (`0x4a50d8`) puis **`movl $0xc,(%eax)`** (`0x4a5100`) → `Length = 12` ; `mov $0xd6,%edx` (`0x4a50f5`) + `mov %dx,0x4(%eax)` (`0x4a50fa`) → `ID = 214` @4 |
| `0x4a5120`–`0x4a516a` | constructeur du **215** | `movl $0x7,(%eax)` (`0x4a5128`) puis **`movl $0x8,(%eax)`** (`0x4a514d`) → `Length = 8` ; `mov $0xd7,%edx` (`0x4a5142`) + `mov %dx,0x4(%eax)` (`0x4a5147`) → `ID = 215` @4 |

Les deux fonctions recalculent ensuite la somme des **6 premiers octets** et l'écrivent à l'offset 6
(boucle `0x4a5134`–`0x4a513b` puis `0x4a5160`–`0x4a5167`), ce qui décrit l'en-tête du client :
`uint32 Length` @0, `uint16 ID` @4, `uint8 Checksum` @6 — **soit exactement l'en-tête de Navislamia**
(`Game/Network/Packets/Game/GameCharacterPackets.cs:19`, `HeaderSize = 7`).

Pourquoi la fiche 214 l'avait manqué : `0x4a50f5`/`0x4a5142` sont des `mov $imm,%edx` (`ba …`), pas
des `mov $imm,%eax` (`b8 …`). Le motif affiché aux offsets du corps 215 (`6766f9`/`6766fb`/`67672f`/
`67674e`) était celui de la table de noms, pas celui du constructeur.

Chaque constructeur n'a **qu'un seul site d'appel**, à l'intérieur du répartiteur `0x4a5230` :

| VA | instruction | sens |
|---|---|---|
| `0x4a5242` | `call 0x4a50d0` | trame 214 (branche « put on ») |
| `0x4a5268` | `call 0x4a5120` | trame 215 (branche « put off ») |

`0x4a5230(ecx = gestionnaire d'objets, bool putOn, uint32 handle, uint8 position)` est atteint par
**deux seuls** sites d'appel : `0x48f333` (214) et `0x48f351` (215) — voir §2.3. Toute émission de
215 par ce client passe donc par cette unique chaîne.

### 2.3 Le producteur : sous-commande **6** du même message interne que la 214

Le producteur est `0x48f1f0`, handler d'un **message interne du client** (objet C++ à vtable). Le
message porte son identifiant de classe à l'offset +4 ; le répartiteur `0x49cd00` lit
`mov 0x4(%edi),%eax` (`0x49cd3c`), compare à `0x400` (`0x49cd3f`), puis aiguille sur
`movzbl 0x49ea50(%eax),%eax ; jmp *0x49e98c(,%eax,4)` (`0x49e22d`, `0x49e234`). La classe qui atteint
`0x49e23b → call 0x48f1f0` est **la classe interne 1036 (`0x40c`)** (table `0x49e98c` entrée 3, octet
`0x49ea50+0` = 3, ids couverts 1027–1249).

Dans `0x48f1f0`, c'est le champ **+0x13 du message** qui est le discriminant :
`mov 0x13(%esi),%eax` (`0x48f256`), puis une cascade de comparaisons :

| sous-commande | VA du test | effet |
|---|---|---|
| 0 | `0x48f259` | crée un objet à partir de (+0x17, +0x23, +0x27, chaîne en +0x2b) via `0x6b29d0` |
| 1 | `0x48f28c` | passe (qword +0x1b, dword +0x17) à `0x48ea10` |
| 2 / 3 | `0x48f2b5` / `0x48f2c4` | construisent d'autres trames (`0x48c6f0` / `0x48c740`) |
| 4 | `0x48f303` | `0x4a52a0(dword +0x17, dword +0x23)` |
| **5** | `0x48f321` | **`0x4a5230(putOn = 1, dword +0x17, octet +0x47)` → trame 214** |
| **6** | `0x48f341` | **`0x4a5230(putOn = 0, dword +0x17, 0)` → trame 215** |

Détail de la branche 215 (`0x48f346`–`0x48f351`) : `mov 0x17(%esi),%edx` ; `mov 0x74(%edi),%ecx` ;
`push $0x0` ; `push %edx` ; `push $0x0` ; `call 0x4a5230`. Le troisième argument
(celui que la 214 utilise comme `position`) vaut **0 en dur** : la sous-commande 6 ne transmet
*aucune* position au constructeur de trame.

### 2.4 Ce que le client met réellement dans l'octet de charge

C'est le point décisif, et il infirme la lecture naïve (« le client recopie une position »).
La branche 215 du répartiteur (`0x4a5265`–`0x4a5291`) :

```
0x4a5268  call 0x4a5120        ; construit la trame 215 (Length 8, ID 215)
0x4a526d  mov 0xc(%ebp),%edx   ; edx = 3e argument = champ +0x17 du message
0x4a5272  lea 0x21a8(%esi),%ecx; ecx = table de 6 dwords du gestionnaire (esi = this)
0x4a5278  cmp %edx,(%ecx)      ; recherche linéaire de la valeur dans la table
0x4a527c  inc %eax             ;   index 0..5
0x4a527d  add $0x4,%ecx
0x4a5280  cmp $0x6,%eax
0x4a5283  jl  0x4a5278
0x4a5285  or  $0xffffffff,%eax ; valeur absente de la table → -1
0x4a5288  mov %al,-0x1(%ebp)   ; octet de charge (offset 7 de la trame) = index, ou 0xFF
0x4a5291  call 0x4a2f00        ; remet la trame au répartiteur virtuel du gestionnaire
```

Donc l'octet de `position` de 215 vaut :

- **0..5** : l'ordinal du champ +0x17 du message dans une **table de 6 dwords** du gestionnaire
  d'objets (`this+0x21a8`) ;
- **0xFF** : le champ +0x17 n'est *pas* dans la table (valeur de repli explicite).

La table elle-même est écrite par le *même* répartiteur, pour la **classe interne 85 (`0x55`)** :
`0x49d63b` `mov 0x74(%esi),%ecx ; push %edi ; call 0x4a4400`, et `0x4a4400` recopie 6 dwords de
`arg+0x13` vers `this+0x21a8` (`add $0x21a8,%ecx` `0x4a4406`, `add $0x13,%eax` `0x4a440c`,
`mov $0x6,%edx` `0x4a440f`). Elle est lue à l'index par `0x4a44c0` (7 sites d'appel, dont
l'affichage de 6 emplacements d'objets dans une fenêtre : `0x5e7050`).

**Contraste avec la 214**, dans la même fonction : la branche 5 écrit le champ +0x17 **tel quel** à
l'offset 8 de la trame (`0x4a5250  mov %ecx,-0x4(%ebp)`, trame en `-0xc(%ebp)`) et le champ +0x47
**tel quel** à l'offset 7 (`0x4a5256  mov %al,-0x5(%ebp)`). Autrement dit, la 214 transporte deux
champs recopiés, la 215 un **ordinal calculé**. C'est la raison pour laquelle 215 n'a pas de
`target_handle` (ni de quelconque handle) : le handle n'est pas transmissible tel quel.

### 2.5 Le corpus UI du sertissage — et l'absence de texte de *retrait*

Corpus de la fenêtre de sertissage (`strings -n 4 SFrame.exe`) :

| ligne | chaîne | lecture |
|---|---|---|
| 24812 | `window_Socket.nui` | la fenêtre existe |
| 30293 | `static_common_socket_itemslot` | un « slot » de fenêtre |
| 30148 | `#@socket_empty@#` | état vide |
| 21802-21807 | `icon_socket_armor`, `_helm`, `_weapon`, `_boots`, `_glove`, `_shield` | **6** icônes = 6 *sortes* d'équipement sertissable |
| 21809-21816 | `icon_socket_disable4…1`, `icon_socket4…1` | **4** châsses par objet |
| 22047 / 22049 | `filter_card_chk` / `filter_soulstone_chk` | filtres d'inventaire carte / pierre d'âme |
| 24811 / 24941 | `window_SoulCharge.nui`, `Create : SUISoulChargeWnd` | fenêtre distincte de recharge d'âme |
| 25057 / 25061 | `SGameInterface - IMSG_SOULSTONE_MOVEITEM`, `… IMSG_SOULCHARGE_MOVEITEM` | traces de messages internes de déplacement |
| 44185 / 44184 | `.?AUSIMSG_SOULSTONE_MOVEITEM@@`, `.?AUSIMSG_SOULCHARGE_MOVEITEM@@` | classes RTTI correspondantes |
| 44021 / 44022 | `.?AUSIMSG_UI_SOULSTONE_CRAFT@@`, `.?AUSIMSG_UI_REPAIR_SOULSTONE@@` | sertissage / réparation |

Textes joueur (`strings -n 6 db_string.rdb`, lignes 195973-195993 ;
`db_string.rdb sha256 = 4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1`) :
`smsg_soket01` « il y a déjà une pierre d'âme dans cette châsse ; sertir une autre pierre
**détruit** la première », `smsg_soket02` (coût), `smsg_soket03` (« seules les pierres d'âme »),
`smsg_soket04` (« déjà sertie »), `smsg_soket05` (règle des 2 pierres identiques), `smsg_soket06`
(mauvais type d'objet), `smsg_soket07` (« sertissage terminé »), `smsg_soket08`-`smsg_soket11`
(recharge d'âme au Lak).

**Aucun** des onze textes ne décrit un *retrait*, une *extraction* ou un *désertissage*, et aucun
nom de message interne (`IMSG_*`/`AUSIMSG_*`) ne porte ce sens. Le seul changement de pierre décrit
au joueur est le **remplacement** (sertir une autre pierre détruit la précédente, `smsg_soket01`).
Le geste exact qui déclenche la 215 n'est donc **pas identifiable par lecture statique** : c'est une
réserve nommée, pas une valeur à deviner (§7.a).

### 2.6 Le client n'a *aucun* traitement entrant pour 215

Balayage exhaustif de l'immédiat `0xd7` dans `.text` (motifs `mov`/`push`/`cmp`/`test`/`or`/`and` de
toutes largeurs) : **13 sites**, dont :

- `0x4a5142` = le constructeur de trame 215 (§2.2) ;
- `0x67672f` = la table de noms (§2.1) ;
- `0x890230` = série d'aiguillage interne (`push $0xd7` suivi de `jmp 0x89059b`) qui traite de la même
  façon tous les ids du bloc où elle se trouve (le 214 y est à `0x890224`, la suite descend de `0xca`
  à `0xda`) : forme générique, donc non concluante ;
- `0x8a99a7` = `cmp $0xd7,%esi`, borne haute d'un test de plage `0xd0..0xd7` (`0x8a999f`) sur du
  formatage, et `0x514e9c` = littéral de données (`movl $0xd7` puis `movl $0x14`) ;
- les `push $0xd7` sous forme `6a d7` (`0x429ae2`, `0x66f972`, `0x6b7f45`) sont des `push $-41`.

Aucune **comparaison de dispatch** sur 215, aucune entrée de table d'aiguillage : 215 est un id
strictement sortant côté client, ce qui est cohérent avec un `TM_CS_*`.

---

## 3. Structure sur le fil

| offset | type | nom | valeur observée | source |
|---|---|---|---|---|
| 0-3 | `uint32` | `Length` | **8** | rzu `TS_CS_PUTOFF_CARD.h:6` + base 7 (`PacketDeclaration.h:616-621`, le `7,` est ligne **618**) ; **confirmé par le client** : `movl $0x8,(%eax)` à `0x4a514d` |
| 4-5 | `uint16` | `ID` | **215** | rzu `TS_CS_PUTOFF_CARD.h:9` ; NGemity `ClientPackets.h:79-80` ; client `mov $0xd7,%edx` `0x4a5142` + `mov %dx,0x4(%eax)` `0x4a5147` |
| 6 | `uint8` | `Checksum` | somme des 6 premiers octets | client `0x4a5134`-`0x4a513b` (1re passe), `0x4a5160`-`0x4a5167` (recalcul après `Length` définitif) ; Navislamia `GameCharacterPackets.cs:19` |
| 7 | `int8` (`sbyte`) | `position` | **0..5**, ou **0xFF** si le handle du message est absent de la table de 6 | rzu `TS_CS_PUTOFF_CARD.h:6` ; NGemity `TS_CS_PUTOFF_CARD.h:7` ; client `0x4a5278`-`0x4a5288` (§2.4) |

**Taille totale attendue : 8 octets.** Aucun remplissage, aucune chaîne, aucun handle : la charge
utile est d'**un seul octet**.

Repère de contrôle : la 214 jumelle fait `7 + 1 + 4 = 12` octets (client `0x4a5100` `movl $0xc`) —
le client confirme les deux tailles par ses propres constantes.

---

## 4. Gating de version

Valeurs de référence (`reference/rzu/librzu/src/lib/Packet/PacketEpics.h`) :
`EPIC_7_3 = 0x070300` (l.59), `EPIC_9_5 = 0x090500` (l.71), `EPIC_9_6_3 = 0x090603` (l.96),
`EPIC_9_6_7 = 0x090607` (l.104).

| champ / borne | ce que rzu montre | décision pour Epic 7.3 |
|---|---|---|
| id | `X(215, version < EPIC_9_6_3)` / `X(1215, version >= EPIC_9_6_3)` (`TS_CS_PUTOFF_CARD.h:9-10`) | **id = 215**. `0x070300 < 0x090603` |
| `position` | `_(simple)(int8_t, position)` (`:6`) — **pas** de couple `_(def)`/`_(impl)` dans ce fichier | **`int8`, 1 octet, pour toutes les versions**. Aucune décision de version à prendre sur le type |
| `EPIC_9_6_7` | le fichier 215 **ne le mentionne pas** ; c'est `TS_CS_PUTON_CARD.h:7-9` (214) qui passe `position` de `int8` à `int32` à cette borne | **sans effet sur 215** — 215 reste à 8 octets après 9.6.7 (vérifié : le fichier 215 entier fait 13 lignes, une seule ligne de définition) |
| `EPIC_9_5` | change `TS_SC_BELT_SLOT_INFO` de 6 à 8 handles (`TS_SC_BELT_SLOT_INFO.h:9-10`) | n'affecte **pas** la structure de 215 ; pertinent uniquement pour l'hypothèse « table de 6 = ceinture » (§7.a), qu'il ne faut donc pas figer en dur |
| `EPIC_9_6_3` (remap) | Tous les ids du bloc inventaire sont décalés de +1000 à partir de 9.6.3 | **sans objet** : le client cible est le client 7.3 et envoie 215 |

Conclusion : **un seul champ, une seule version utile, 8 octets.** Aucun `NON ÉTABLI` de version.

---

## 5. Traitement attendu

### 5.1 Ce que NGemity fait du paquet : **rien**

- Aucun handler : la table `worldPacketHandler` de
  `reference/ngemity/Chihiro/src/Network/GameNetwork/WorldSession.cpp:105-137` ne contient ni
  `PutOffCard` ni `PutOnCard` ; `grep -rn "PutOffCard\|PutoffCard\|PutOnCard" reference/ngemity/`
  ne renvoie **aucun résultat**.
- Aucun chemin de **retrait** de pierre n'existe non plus : `SetSocketIndex(i, 0)` n'est utilisé que
  pour les invocations (`Item.cpp:317-324`), jamais pour désertir.
- Le sertissage de NGemity passe par un **autre paquet** : `onSoulStoneCraft` (`WorldSession.cpp:1497`)
  traite `TS_CS_SOULSTONE_CRAFT` — `craft_item_handle` + `soulstone_handle[4]`
  (`reference/rzu/librzu/src/packets/GameClient/TS_CS_SOULSTONE_CRAFT.h:5-11`, id 260 puis 1260) —
  une forme plus récente, où le client envoie **directement les handles** des pierres par châsse.

### 5.2 Ce que rzu fait du paquet : **rien**

`grep -rn "PUTOFF_CARD\|PUTON_CARD" reference/rzu/librzu/src` ne renvoie que les deux fichiers de
déclaration. `librzu` est une bibliothèque de sérialisation : aucune logique serveur à réutiliser.

### 5.3 Ce que le serveur Navislamia doit faire

**a) Dispatch (obligatoire avant toute lecture).** `GameClient.cs:584`
(`if (!Enum.IsDefined(typeof(GamePackets), header.ID))`) jette le paquet *avant* tout traitement si
l'id n'est pas dans l'énumération : sans `TM_CS_PUTOFF_CARD = 215` dans `GamePackets.cs`, rien
n'arrive. Et le « switch final » `GameClient.cs:791-803` se termine par
`_ => throw new Exception("Unknown Packet Type")` : un membre d'énum **sans** bras
`if (header.ID == …)` explicite (modèle de la 201 : `GameClient.cs:465-481`, bras à `l.467`) tombe
dans cette exception. Énumération et dispatch doivent donc être modifiés **ensemble**.

**b) Décodeur.** Le modèle est `GameActionPackets.TryReadPutoffItem` (`GameActionPackets.cs:165-178`,
`HeaderSize + 5` pour 201 en 7.3 = `int8` + `uint32`) ; pour 215 c'est `HeaderSize + 1` :
`(sbyte)packet[HeaderSize]`, avec `PutoffItemRequest` (`:15`) comme précédent de forme.

**c) Primitives disponibles sur `master`** (vérifiées) :

| primitive | emplacement |
|---|---|
| `CharacterService.GetItemByHandleAsync` | `Game/Services/CharacterService.cs:204` |
| `CharacterService.ArrangeInventoryAsync` | `:210` |
| `CharacterService.EraseItemsAsync` | `:236` |
| `CharacterService.ConsumeItemAsync` | `:270` |
| `CharacterService.UnequipItemAsync` | `:159` |
| `CharacterService.AddItemAsync(characterName, itemResourceId, count)` | `:334` — **crée** l'objet et lui donne le prochain `Idx` libre (`:345-347`) |
| `CharacterService.SwapItemPositionsAsync` | `:362` |
| `CharacterService.RemoveAmount` (privé) | `:318` |
| `InventoryArrange` (`FirstIndex = 1`, tri/resserrage) | `Game/Services/InventoryArrange.cs:15-17` |
| sérialisation d'objet (85 octets, **châsses incluses** aux offsets 38-53 ; `Idx` à 81) | `GameCharacterPackets.cs:341-366`, constantes `:19-22` |
| `BuildInventory(CharacterEntity)` / `BuildInventory(ItemEntity[])` — feuilles d'objet (châsses incluses) | `GameCharacterPackets.cs:130` et `:136` |
| `BuildWearInfo` / `BuildItemWearInfo` / `BuildUpdateItemCount` / `BuildDestroyItem` | `GameCharacterPackets.cs:92`, `:76`, `:195`, `:187` |
| précédent d'un objet **matérialisé** dans le sac + envoi de sa feuille | `Game/Services/GroundItemService.cs:192` (`AddItemAsync` puis `BuildInventory(new[] { added })`) |
| envoi d'inventaire groupé | `Game/Services/InventoryService.cs:125-131` |
| `ItemEntity.SocketItemIds` (`long[]`) | `Game/DataAccess/Entities/Telecaster/ItemEntity.cs:35` |
| borne DB « 4 châsses » | `Game/DataAccess/Contexts/TelecasterContext.cs:55` (`HasMaxLength(4)`) |
| `ItemResourceEntity.SocketCount` | `Game/DataAccess/Entities/Arcadia/ItemResourceEntity.cs:26` |

**d) Primitives manquantes sur `master`** (à fournir par le dev, ou héritées de la branche 214) :

- aucun code n'**écrit** `SocketItemIds` hors sérialisation : le seul accès est la lecture
  `GameCharacterPackets.cs:353-356`. Il faut un helper d'écriture de châsse (`ClearSocket` /
  `SetSocketIndex`) ;
- aucune résolution « `position` → objet équipé » hors `EquipmentService.IsWearableSlot`
  (`Game/Services/EquipmentService.cs:115-118`, borne `0 < position < WearSlots` = 24) ;
- aucune opération **atomique** « vider une châsse **et** rendre l'objet au sac » — exactement ce que
  la 214 ajoute à côté (`CardSocketService`, `CardSocketRules`, `CardSocketCatalog`,
  `CardSocketResult`, `CharacterService.SocketCardAsync`, `ICardSocketCatalog`) ;
- **aucune notion de capacité d'inventaire** : `ResultCode`
  (`Game/Network/Packets/ResultCode.cs`) n'a pas de membre « plein », `grep` de `InventoryFull` /
  `capacity` / `MaxSlots` sur tout `Game/` ne renvoie rien, et `AddItemAsync` (`:334-360`) ajoute
  toujours. NGemity n'a pas non plus cette notion (`grep` sur `Chihiro/src` : rien).

**e) Réponse attendue au client.** Il n'existe **aucun** paquet serveur→client dans cette famille
(ni `TS_SC_PUTOFF_CARD`, ni réponse de sertissage) : la 215 ne peut donc pas attendre de « réponse
dédiée ». Ce que le client peut recevoir, et qui rend le nouvel état observable, est :

1. le verdict : `TS_SC_RESULT` (id 0) via `SendResult(215, code)` — `GameClient.cs:56-60` ;
2. l'objet équipé **avec ses châsses vidées** : elles ne voyagent *que* dans la feuille d'objet
   (`GameCharacterPackets.cs:353-356`, offsets 38-53 du record de 85 octets) — précédent direct :
   la branche 214 renvoie la feuille de l'objet cible via
   `BuildInventory(new[] { result.Target })` ;
3. la pierre revenue : un **objet neuf** dans le sac → sa propre feuille d'objet, position = son
   `Idx` (`:365`), envoyée comme `BuildInventory(new[] { added })` — le précédent exact existe déjà
   pour la ramassage au sol (`Game/Services/GroundItemService.cs:192`). C'est
   `AddItemAsync(…, codeDeLaChâsse, 1)` qui la matérialise, ce qui n'est possible que parce que la
   châsse contient un **code d'objet** et non un handle (§6).
4. si le contrôle doit refuser : `SendResult(215, InvalidArgument/NotExist/…)` puis **aucune**
   écriture de châsse.

**f) Le contenu du champ à interpréter.** L'octet vaut 0..5 ou 0xFF (§2.4). Toute implémentation qui
le traiterait comme un `ItemWearType` (patron `WearInfo == position` retenu par la branche 214) ou
comme un index d'inventaire (`ItemEntity.Idx`) doit être considérée comme **non établie** : elle
n'est étayée par aucune preuve côté client, et 0xFF n'a aucun sens dans ces deux lectures. Ce que le
serveur peut faire honnêtement sans trancher : refuser 0xFF explicitement, et journaliser la valeur
reçue en attendant l'arbitrage (§7.a, §10).

### 5.4 Dépendance réelle vis-à-vis de la branche 214 (non mergée)

Sur `master`, **rien** de la famille carte/châsse n'existe : `grep -rn "PUTOFF_CARD\|PUTON_CARD\|
CardSocket\|Soulstone" --include=*.cs .` ne renvoie que trois valeurs d'énum de ressources
(`ItemBaseType.Soulstone`, `ItemType.Soulstone`, `ItemGroup.Soulstone`). La branche
`hermes/packet-214-puton-card` (commit `d9b6fb2`) apporte en plus : `GamePackets.TM_CS_PUTON_CARD`,
`GameActionPackets.TryReadPutonCard`, le bras `HandlePutonCardAsync` dans `GameClient`,
`CardSocketCatalog`/`CardSocketRules`/`CardSocketResult`/`CardSocketService`,
`ICharacterService.SocketCardAsync`, `ICardSocketCatalog`, et les tests
`Tests/Game/CardSocketRulesTests.cs`, `Tests/Game/PutonCardPacketsTests.cs`.

Conséquence pour la carte dev 215 : la dépendance est **structurelle et forte** (quotas, catalogue de
ressources, écriture de châsse, atomicité), mais **pas bloquante sur la structure du champ** — on
peut livrer le décodeur, le gating, les tests d'offsets et un refus explicite sans attendre le merge.
Ordre recommandé, si les deux passent le pipeline : 214 mergée d'abord, puis 215 sur la même base.

---

## 6. Écarts assumés avec NGemity

1. **Aucune logique à porter.** NGemity n'a ni handler ni chemin de retrait (§5.1). La logique
   disponible est celle du *sertissage* (`onSoulStoneCraft`, `WorldSession.cpp:1497-1590`), portée sur
   un autre paquet (`TS_CS_SOULSTONE_CRAFT`, ids 260/1260) qui n'existe pas en 7.3. Reprendre sa
   sémantique est le seul appui possible : **c'est un transfert, pas un portage**.
2. **La nature de la châsse est tranchée, et elle vient de NGemity.** `Item.cpp:233-234` lit
   `sObjectMgr.GetItemBase(GetSocketIndex(i))` : la châsse contient un **code d'objet**, et
   `SetSocketIndex(i, …GetCode())` (`WorldSession.cpp:1572`) écrit bien un code. La pierre retirée se
   matérialise donc par un objet de ce code — c'est cohérent avec `AddItemAsync(char, resourceId, 1)`
   et avec la borne DB de 4 châsses (`TelecasterContext.cs:55`).
3. **La destruction/remplacement diffère.** NGemity (`WorldSession.cpp:1568-1578`) ne sertit que dans
   les châsses libres et additionne l'endurance des pierres en place ; le client 7.3, lui, propose de
   **remplacer** une pierre déjà sertie en détruisant l'ancienne (`smsg_soket01`). Cet écart a déjà
   été tranché pour 214 (réserves de la fiche 214 §6 et §10). Il **pèse directement sur 215** : si le
   client n'expose au joueur que le remplacement, « quelle châsse vider » n'a aucune réponse côté
   client, ce qui renforce le `NON ÉTABLI` de §7.b.
4. **Le coût.** NGemity facture le sertissage (`WorldSession.cpp:1553`) ; 215 ne mentionne dans le
   client **aucun texte de remboursement ni de coût de retrait** (§2.5). Ne rien facturer au retrait
   tant que §7.a n'est pas tranché.

---

## 7. `NON ÉTABLI`

### 7.a Sémantique de l'octet `position` — **la question d'arbitrage principale**

Établi : l'octet est un **ordinal 0..5** dans une table de 6 dwords du gestionnaire d'objets, ou
0xFF (§2.4). Non établi : **ce que contiennent ces 6 entrées**.

| hypothèse | pour | contre |
|---|---|---|
| **a1** les 6 sortes d'équipement sertissable (armure/casque/arme/bottes/gants/bouclier) | exactement **6** noms d'icônes dans la fenêtre de sertissage (`icon_socket_armor|helm|weapon|boots|glove|shield`, l.21802-21807) ; la table est indexée 0..5 et lue par une fenêtre à 6 emplacements (`0x5e7050`) ; un *retrait* a besoin de désigner l'objet équipé, pas la pierre | rien ne relie formellement la table de `+0x21a8` à ces icônes ; la table est remplie par un message **interne** (classe 85), pas par un paquet serveur identifié |
| **a2** les 6 emplacements de ceinture | `TS_SC_BELT_SLOT_INFO` fait **6** handles en 7.3 (`TS_SC_BELT_SLOT_INFO.h:9`) et Navislamia sait déjà l'émettre (`GameCharacterPackets.cs:302-314`) ; même cardinal | aucun rapport avec un sertissage ; la table est remplie en interne, pas par le paquet 216 ; la ceinture n'a jamais été liée aux cartes |
| **a3** les 6 handles de cartes de summon (`TS_EQUIP_SUMMON`, `card_handle[6]`) | cardinal 6 ; `.?AUSIMSG_UI_ACT_EQUIP_SUMMON@@` existe (l.43998) | aucun lien avec `PUTON/PUTOFF_CARD`, et `TM_EQUIP_SUMMON = 303` est un autre système déjà présent sur `master` |

Test qui trancherait : identifier la **classe interne 85** (qui remplit la table) et la **classe 1036**
(qui produit 214/215) par leur RTTI/vtable — les noms `.?AUSIMSG_…@@` sont présents dans l'image, donc
une observation dynamique (ou une reconstruction plus profonde des vtables) donnerait le geste exact.
Les deux `push $0x0` en dur de la branche 215 (`0x48f34c`, `0x48f34f`) montrent en tout cas que
**l'octet `position` de 215 n'est pas une « position » transmise par l'UI**, mais une valeur calculée
par le client à partir d'un handle — ce qui interdit de la traiter comme un `ItemWearType` brut.

### 7.b Quelle châsse est vidée

Le paquet ne porte **aucun index de châsse** (un seul octet, déjà occupé par l'ordinal) et aucun
handle (§2.4). Si l'arbitrage §7.a retient a1, le serveur saura *quel objet équipé* est visé, mais
**pas quelle châsse** : il faudra décider (première, dernière, toutes ?) et le documenter comme choix
de projet, faute de source.

### 7.c Destination de la pierre retirée

Le protocole ne dit **rien** de la destination : ni `target_handle` (contrairement à 201,
`TryReadPutoffItem` `GameActionPackets.cs:165-178`, et à `TS_CS_PUTOFF_ITEM` rzu `:7-11`), ni
`is_storage`, ni coordonnée. Le sac est la seule destination cohérente avec
`AddItemAsync(…, code, 1)`. À noter : `master` n'a **aucune** notion de sac plein (§5.3.d), donc la
question « et si l'inventaire est plein ? » n'a aujourd'hui **pas de réponse dans le projet** ; la
créer serait une décision de conception, pas un portage.

### 7.d Le geste exact, et l'existence même de l'émission en jeu

Le corpus de messages du client ne contient **ni texte ni nom de message** de retrait (§2.5), alors
que le constructeur de trame 215 et son unique producteur existent (§2.2-2.3). Deux lectures
possibles : (i) le retrait est un geste d'interface sans texte (annulation d'une mise en place) ;
(ii) le paquet n'est plus atteignable dans le client 7.3 et survit comme code mort. La lecture
statique ne permet pas de trancher.

### 7.e Ce qui n'est PAS incertain (pour éviter une relecture inutile)

- id **215** en 7.3 (remap `1215` à partir d'`EPIC_9_6_3`) ;
- taille **8 octets**, un seul octet de charge utile, `int8` (aucun gating de type dans rzu) ;
- en-tête 7 octets, `Length`/`ID`/`Checksum` (le constructeur client le confirme) ;
- champ recalculé, jamais recopié : 0..5 ou 0xFF ;
- absence totale de handler NGemity et de logique rzu ;
- châsses = **codes d'objet** (NGemity `Item.cpp:233-234`, `WorldSession.cpp:1572`), pas des handles ;
- borne de **4** châsses (client `icon_socket1..4`, DB `TelecasterContext.cs:55`, NGemity
  `WorldSession.cpp:1568`).

---

## 8. Commits et binaires épinglés

| référence | commit / empreinte |
|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02) |
| `reference/ngemity` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03) |
| `Navislamia` base | `master` = `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` ; `origin/master` identique |
| branche 214 | `origin/hermes/packet-214-puton-card` = `d9b6fb2b85e7f1c78ecfc221837d874584276db1` |
| client | `SFrame.exe` `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` ; `db_string.rdb` `sha256 4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` |

Méthode de lecture (toutes reproductibles, exécutées en lecture seule) :
`objdump -d -j .text --start-address=… --stop-address=… SFrame.exe` (`.text` VMA `0x401000` = fichier
`0x400` ; `.rdata` VMA `0xa0f000` = fichier `0x60da00`, delta `0x401600`) ;
`strings -n 4 SFrame.exe` et `strings -n 6 db_string.rdb` pour les citations textuelles ; scripts
Python locaux pour le balayage des immédiats et le suivi des sites d'appel. Aucun Lua ni script du
client n'a été exécuté ; aucune exécution de `SFrame.exe`.

---

## 9. Note de livraison

Cette fiche est produite par `navis-ref` ; elle ne contient **aucun** code serveur. Le champ
`position` de 215 est le premier de ce dépôt dont la valeur est *calculée* par le client plutôt que
recopiée : c'est cette particularité qui doit déterminer la revue de la carte dev, pas la symétrie
d'apparence avec 201.

## 10. A VERIFIER PAR KILLIAN

1. **§7.a — sens de l'octet 215.** Retenu comme non établi ; les trois hypothèses sont listées avec
   leurs appuis. Sans arbitrage, l'implémentation doit refuser 0xFF et ne pas supposer un
   `ItemWearType`.
2. **§7.b — quelle châsse vider** si l'objet visé est identifié (pas d'index dans le paquet).
3. **§7.c — destination de la pierre** (le sac est le seul choix cohérent avec le protocole) et
   l'absence totale de notion de sac plein dans le projet.
4. **§7.d — émission en jeu** : le client porte le code de 215, mais aucun texte d'interface de
   retrait n'existe dans le corpus 7.3.
5. **§5.4 — ordre de merge** 214 puis 215, et héritage éventuel des primitives de la branche 214.
