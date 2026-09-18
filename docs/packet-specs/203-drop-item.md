# 203 — TM_CS_DROP_ITEM

Fiche de paquet établie par `navis-ref` (archéologue de protocole), branche
`hermes/packet-203-drop-item`, à partir de `master` `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7`.
Cette fiche ne contient **aucun** changement de code serveur : elle fixe le format, le gating de
version, le comportement attendu et les réserves vérifiables.

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id décimal | **203** (`0x00CB`) | `op_codes.md:53` — `[203] = "TM_CS_DROP_ITEM"` |
| sens | client → serveur | rzu `librzu/src/packets/GameClient/TS_CS_DROP_ITEM.h:15` (`PacketOrigin::Client`) |
| nom client | `TM_CS_DROP_ITEM` | `op_codes.md:53` ; trace du binaire `SFrame.exe` (dump `strings -n 4`, l. 26394) |
| réponse associée | **205** `TM_SC_DROP_RESULT`, 12 octets | `op_codes.md:55` ; rzu `TS_SC_DROP_RESULT.h` |
| taille de la trame | **15 octets**, fixe | §3 |
| état dans le dépôt | absent de `GamePackets` (ni 203 ni 205) | `Game/Network/Packets/Enums/GamePackets.cs:27-33` |
| carte Trello | `trello.com/c/UyB62RWs` | suivi `navislamia:packet:203` |

Le nom existe aussi dans le client 7.3 sous sa forme d'annotation de paquet
(`TM_CS_DROP_ITEM`) et la réponse sous `TM_SC_DROP_RESULT` (même dump, l. 26392) : le client de
référence connaît bien la paire montante/descendante (§5.3, §7.1 pour la limite de cette preuve).

## 2. Ce que le joueur fait pour que le client l'envoie

Le client possède une fonction de « lâcher un objet au sol », avec confirmation et textes dédiés :

- confirmation avant envoi : la boîte de dialogue `msgboxItemDropCheck` (dump `SFrame.exe`, l. 24697,
  entre `msgboxPartyJoinCheck` et `msgboxDeleteCharWait`) affiche le texte `smsg_dump_item_confirm`
  = *« Do you want to drop your <B>#@item_name@#</B> on the ground? »*
  (`db_string.rdb`, dump `strings -n 4`, l. 76370-76371) ;
- la fenêtre qui porte l'action existe dans le binaire : RTTI `SUIDropItemWnd`
  (`.?AVSUIDropItemWnd@@`, l. 43847) et message interne `UIMSG_UI_DROP_ITEM_NAME`, type
  `. ?AUSIMSG_UI_DROP_ITEM_NAME@@` (l. 44484) ;
- les textes de résultat, côté client, distinguent l'unité seule du lot :
  `smsg_dump_item` = *« You dropped your <B>#@item_name@#</B>. »* (l. 76352-76353) et
  `smsg_dump_item_num` = *« You dropped <B>#@item_num@#</B> <B>#@item_name@#</B>(s). »*
  (l. 76354-76355). Le pluriel paramétré prouve que l'opération peut porter sur **plusieurs unités**
  d'une même pile, donc que `count` est un nombre d'unités (§3) ;
- un refus **local** existe : `smsg_dump_fail` = *« You may not drop this item. »*
  (l. 76298-76299). Le client décide donc lui-même de la jetabilité de certains objets avant
  d'émettre : le refus côté serveur n'est pas le seul rempart (§7.3).

Ce qui reste non établi : le **geste exact** qui déclenche l'envoi (glisser la pile hors de la fenêtre
d'inventaire, entrée de menu contextuel, ou fenêtre de quantité `SUIDropItemWnd`) — voir §7.2.

## 3. Structure sur le fil

En-tête du dépôt : `Game/Network/Packets/Header.cs:9-11` (`Length` `uint32`, `ID` `uint16`,
`Checksum` `uint8`), lu aux offsets 0/4/6 (`Header.cs:22-24`). `GameActionPackets` fixe
`HeaderSize = 7` (`Game/Network/Packets/Game/GameActionPackets.cs:8`).

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | **15** | `Header.cs:9,22` ; rzu `TS_CS_DROP_ITEM.h:6-9` (2 champs de 4) |
| 4 | `uint16` LE | `ID` | **203** | `Header.cs:10,23` ; `op_codes.md:53` |
| 6 | `uint8` | `Checksum` | — | `Header.cs:11,24` |
| 7 | `uint32` LE | `item_handle` | handle d'inventaire de l'objet (== `ItemEntity.Id`) | rzu `TS_CS_DROP_ITEM.h:6` ; NGemity `TS_CS_DROP_ITEM.h:7` ; `CharacterService.cs:377-380` |
| 11 | `int32` LE | `count` | nombre d'unités à lâcher, attendu `> 0` | rzu `TS_CS_DROP_ITEM.h:7-9` ; NGemity `TS_CS_DROP_ITEM.h:8-10` ; NGemity `WorldSession.cpp:1436` |

**Taille totale attendue : 15 octets.** Aucun autre champ : en particulier **aucune position** n'est
transmise (ni `x`, `y`, `z`, ni `layer`). La position de l'objet lâché est donc une décision serveur,
prise à partir de la position connue du personnage (§5.3), exactement comme NGemity qui relocalise à
la position du joueur (`Player.cpp:3529`).

Précisions de lecture :

- `ar_handle_t` de rzu est un `strong_typedef` sur `uint32_t` (`librzu/src/lib/Types/GameTypes.h:40`)
  → 4 octets sur le fil ; c'est le même handle que celui de l'inventaire, que le dépôt dérive de
  `ItemEntity.Id` (`Game/Services/CharacterService.cs:377-380`).
- `count` est **signé** (`int32_t` dans rzu, `int32_t` dans NGemity) : une valeur négative est
  représentable sur le fil et doit être refusée (§5.3, §6).
- Le paquet n'a **aucun champ variable** (pas de tableau, pas de chaîne) : la longueur est constante,
  ce qui rend la validation de trame triviale.

## 4. Gating de version

Valeurs de référence : `EPIC_4_1 = 0x040100`, `EPIC_7_3 = 0x070300`, `EPIC_9_6_3 = 0x090603`
(`librzu/src/lib/Packet/PacketEpics.h:50,59,96`). La cible du serveur est `0x070300`.

| Champ / id | Gating montré par rzu | Décision pour Epic 7.3 | Source |
| --- | --- | --- | --- |
| id du paquet | `X(203, version < EPIC_9_6_3)` / `X(1203, version >= EPIC_9_6_3)` | **203** (`0x070300 < 0x090603`) | rzu `TS_CS_DROP_ITEM.h:12-13` |
| `count` | `_(impl)(simple)(int32_t, count, version >= EPIC_4_1)` / `(uint16_t, version < EPIC_4_1)` | **`int32` sur 4 octets** (`0x070300 >= 0x040100`) | rzu `TS_CS_DROP_ITEM.h:7-9` |
| `item_handle` | aucun gating de champ | `uint32` | rzu `TS_CS_DROP_ITEM.h:6` |
| id de la réponse | `X(205, version < EPIC_9_6_3)` / `X(1205, ...)` | **205** | rzu `TS_SC_DROP_RESULT.h:9-11` |
| `isAccepted` de la réponse | aucun gating ; `bool` sur 1 octet | `uint8` 0/1 | rzu `TS_SC_DROP_RESULT.h:7` ; §5.3 |

Le gating de `count` a été introduit par le commit rzu
`94885467536899bd53031f190d4488fe9e05c70a` (2017-03-03, « Update packets with old clients tests »),
qui remplace le `int32_t` inconditionnel par la paire « def/impl ». Son `_(def)` — la forme par défaut
utilisée hors test de version — reste `int32_t` : c'est la forme 7.3.

Le remap d'id à `EPIC_9_6_3` (`+1000`) n'affecte **aucun** des deux paquets en 7.3 ; l'id du remap
n'est mentionné ici que pour expliquer la double notation.

Le chemin de réponse réutilise `TM_SC_ERASE_ITEM` (209) : sa forme 7.3 est prouvée, et c'est un point
de gating à ne pas manquer — rzu porte le commentaire *« Since EPIC_7_2, was TS_SC_RESULT before »*
(`librzu/src/packets/GameClient/TS_SC_ERASE_ITEM.h:16`), ajouté par le commit
`bdd362a600d104fd676facf12d6c6beb85a23ec4` (2017-02-26), qui traite justement les PDB de 5.2 à 8.1
**dont 7.3**. La structure du 209 en 7.3 est `int8` compte + `dynarray` de paires
`(item_handle uint32, count int64)` (`TS_SC_ERASE_ITEM.h:12-14`) — exactement ce que le dépôt écrit
déjà (§5.3). En 7.3, `TS_ERASE_ITEM_INFO` **ne porte pas** le champ `is_in_storage` (il n'existe que
pour `>= EPIC_7_4`, `TS_CS_ERASE_ITEM.h:23`) : ne pas l'ajouter.

## 5. Traitement attendu

### 5.1 Ce que NGemity en fait

Handler déclaré sur la session authentifiée : `declareHandler(STATUS_AUTHED, &WorldSession::onDropItem)`
(NGemity `Chihiro/src/Network/GameNetwork/WorldSession.cpp:129`), corps
`WorldSession::onDropItem` (`WorldSession.cpp:1433-1443`) :

```
auto item = sMemoryPool.GetObjectInWorld<Item>(pRecvPct->item_handle);
if (item != nullptr && item->IsDropable() && pRecvPct->count > 0
    && (item->GetItemGroup() != ItemGroup::GROUP_SUMMONCARD
        || !(item->GetItemInstance().GetFlag() & FlagBits::ITEM_FLAG_SUMMON))) {
    m_pPlayer->DropItem(m_pPlayer, item, pRecvPct->count);
    Messages::SendDropResult(m_pPlayer, pRecvPct->item_handle, true);
}
else {
    Messages::SendDropResult(m_pPlayer, pRecvPct->item_handle, false);
}
```

Trois conditions, puis un seul accusé :

1. **objet résolu** dans le pool d'objets du monde ; un handle inconnu tombe dans le `else` ;
2. **`IsDropable()`** (`Chihiro/src/Entities/Item/Item.cpp:353-359`) :
   `GetItemTemplate()->flaglist[FLAG_DROP] == 0`, avec `FLAG_DROP = 5`
   (`ItemTemplate.hpp:179-201`) indexant un tableau `int8_t flaglist[19]` (`ItemTemplate.hpp:408`)
   alimenté par les colonnes `fields[29 + i]` de la table `ItemResource`
   (`Chihiro/src/Globals/ObjectMgr.cpp:151`) ;
3. **`count > 0`** et **garde carte d'invocation** : refus si le modèle est
   `ItemGroup::GROUP_SUMMONCARD` (`ItemTemplate.hpp:347` ; `Item.h:99`) **et** que l'instance porte
   le bit `ITEM_FLAG_SUMMON = 0x80000000` (`ItemTemplate.hpp:176`). Autrement dit : une carte
   d'invocation déjà liée à une créature ne peut pas être jetée.

Le travail réel est fait par `Player::DropItem` (`Player.cpp:3520-3532`) : `popItem(origItem, count,
false)` → `Relocate(pTarget->GetPosition())` → `sWorld.AddItemToWorld(pNewItem)` → renvoi de l'objet
créé. `Player::popItem` (`Player.cpp:1299-1307`) appelle `Inventory::Pop` (`Inventory.cpp:59-87`) :

- pile partielle (`GetCount() > cnt`) : une **nouvelle** instance est allouée avec `cnt` unités et
  l'original est décrémenté (`Inventory::setCount`, `Inventory.cpp:149`) ;
- pile entière (`GetCount() == cnt`) : l'instance est retirée (`Inventory::pop`) ;
- `GetCount() < cnt` : **`nullptr`** → rien n'est jeté — mais `onDropItem` a déjà répondu
  `isAccepted = true` (voir §6 : c'est un défaut de NGemity, à ne pas reproduire).

Notification d'inventaire, par les rappels du dépôt d'objets :

- `Player::onRemove` (`Player.cpp:1204-1242`, envoi l. 1239) → `Messages::SendItemDestroyMessage`
  (`Messages.cpp:515-523`) → **`TS_SC_DESTROY_ITEM` (254)**, `{ item_handle }`
  (`shared/Server/Packets/GameClient/TS_SC_DESTROY_ITEM.h:6-9`) ;
- `Player::onChangeCount` (`Player.cpp:1244-1253`, envoi l. 1247) → `Messages::SendItemCountMessage`
  (`Messages.cpp:504-513`) → **`TS_SC_UPDATE_ITEM_COUNT` (255)**, `{ item_handle, count }` où
  `count` est le **total restant** (`shared/Server/Packets/GameClient/TS_SC_UPDATE_ITEM_COUNT.h:6-12`) ;

et l'accusé `Messages::SendDropResult` (`Messages.cpp:385-391`) → **`TS_SC_DROP_RESULT` (205)**,
`{ item_handle recopié, isAccepted }`. NGemity n'envoie **aucun `TS_SC_RESULT`** pour ce paquet.

### 5.2 Ce que rzu en fait

Rien : rzu ne porte aucune logique serveur pour ce paquet (aucune référence hors de la définition
`librzu/src/packets/GameClient/TS_CS_DROP_ITEM.h`). rzu tranche l'id, l'ordre et le gating des champs
(§3, §4), pas le comportement.

### 5.3 Ce que le serveur Navislamia doit faire

1. **Enum.** Ajouter `TM_CS_DROP_ITEM = 203` **et** `TM_SC_DROP_RESULT = 205` à
   `Game/Network/Packets/Enums/GamePackets.cs` (autour des l. 27-33, où 202/207/208/209/204/210 sont
   déjà présents). Les deux membres doivent être ajoutés ensemble : la chaîne de réception et le
   `switch` final doivent rester cohérents (`GameClient.cs:670-682`).
2. **Dispatch.** Un bras dans la chaîne de `if` du `Receive`, **avant** le `switch` final
   (`Game/Network/Clients/GameClient.cs:670-682`), sur le modèle exact de `TM_CS_TAKE_ITEM`
   (`GameClient.cs:581-585` → `HandleTakeItemAsync`, `GameClient.cs:360-376`) : lecture, puis
   `continue`. Aucun membre de `GamePackets` ne peut atteindre le `switch` final (si 203 y arrivait, la
   trame serait rejetée par le `_ => throw`).
3. **Lecture.** `GameActionPackets.TryReadDropItem(ReadOnlySpan<byte>, out uint itemHandle, out int
   count)` : `packet.Length < HeaderSize + 8` → faux ; `item_handle` = `ReadUInt32LittleEndian(7)`,
   `count` = `ReadInt32LittleEndian(11)`. Le style du dépôt est **tolérant au surplus** (`<` et non
   `==`). Modèle : `TryReadTakeItem` (`GameActionPackets.cs:74-85`) et `TryReadEraseItem`
   (`GameActionPackets.cs:46-60`).
4. **Trame malformée** (< 15 octets) → `SendResult((ushort)GamePackets.TM_CS_DROP_ITEM,
   (ushort)ResultCode.InvalidArgument)` (`GameClient.cs:56-60` ; `TS_SC_RESULT` fait 15 octets =
   7 + 2 + 2 + 4, `Game/Network/Packets/Game/TS_SC_RESULT.cs:8-10`). C'est la convention maison des
   paquets d'inventaire voisins (`GameClient.cs:346` pour 208, `:364` pour 204, `:328` pour 219).
5. **Placement.** Porter le traitement dans le service qui détient déjà les objets au sol
   (`Game/Services/GroundItemService.cs`) : c'est lui qui possède `_items`, `BuildEnterItem`,
   `BuildLeave`, `WithinPickupRange` et la durée de vie. Ajouter une méthode d'entrée
   (`DropFromInventoryAsync(GameClient client, uint itemHandle, int count)`) sur le modèle de
   `DropForMonster` (`GroundItemService.cs:37-86`, envoi l. 83-84).
6. **Validation** (trancher dans l'ordre, et ne rien inventer au-delà) :

   | Cas | Décision 7.3 | Source |
   | --- | --- | --- |
   | objet inconnu ou non possédé (`(uint)item.Id != item_handle`) | refus : `205 { handle, 0 }` | NGemity `WorldSession.cpp:1441` ; `CharacterService.cs:377-380` |
   | `count <= 0` | refus : `205 { handle, 0 }` | NGemity `WorldSession.cpp:1436` (`count > 0`) |
   | `count > item.Amount` | **borner** à `item.Amount` (`Math.Min`) | précédent du dépôt : `CharacterService.EraseItemsAsync` (`CharacterService.cs:250`) ; §6 |
   | objet équipé (`WearInfo != None`) | **aucun contrôle ajouté** | ni NGemity ni `EraseItemsAsync` n'en ont ; §7.6 |
   | objet « non jetable » (`flag_drop`) | **aucun contrôle ajouté** | donnée non exploitable dans le dépôt ; refus local client (§2, §7.3) |
   | carte d'invocation liée | **garde portable** : `resource.Group == ItemGroup.Summoncard` **et** bit 31 de l'instance | NGemity `WorldSession.cpp:1436` ; correspondance exacte des bits : `ItemGroup.Summoncard = 13` (`Enums/ItemGroup.cs:18`) ≡ `GROUP_SUMMONCARD = 13` (`ItemTemplate.hpp:347`), `ItemFlag.Summon = 31` (`Enums/ItemFlag.cs:17`) ≡ `ITEM_FLAG_SUMMON = 0x80000000` (`ItemTemplate.hpp:176`) ; §6 |

   La garde carte se teste sur le **bitset** de l'instance : `ItemEntity.Flag`
   (`Game/DataAccess/Entities/Telecaster/ItemEntity.cs:32`) est déjà envoyé tel quel au client
   (`GameCharacterPackets.cs:297`) — donc `((uint)item.Flag & 0x80000000u) != 0`. Attention :
   `ItemFlag.None = -1` (`Enums/ItemFlag.cs:5`) et `AddItemAsync` ne pose aucun flag
   (`CharacterService.cs:289-295`) : la garde ne se déclenchera que si le bit est un jour écrit.
7. **Retrait d'inventaire.** Ne pas réinventer un chemin : réutiliser `CharacterService.EraseItemsAsync`
   (`CharacterService.cs:230-270`) avec une `EraseItemRequest(handle, count)` — c'est **le** chemin de
   retrait établi du dépôt (utilisé par `InventoryService.EraseAsync`, `InventoryService.cs:64-86`),
   il borne déjà le compte, gère la pile partielle (`item.Amount -= removed`), supprime l'entité quand
   la pile tombe à zéro, réindexe l'inventaire et persiste. Il renvoie les couples **réellement**
   retirés : c'est la vérité à notifier, et ce qui permet de n'acquitter `true` que si quelque chose a
   bien été retiré (§6).
8. **Réponses, dans cet ordre** (l'ajout au monde par NGemity précède l'accusé,
   `Player.cpp:3530` puis `WorldSession.cpp:1438`) :

   a. **spawn de l'objet au sol — `TM_SC_ENTER`, 70 octets**, au seul joueur qui lâche :
      `GameSpawnPackets.BuildEnterItem(handleSol, X, Y, Z, layer, itemResourceId, removedCount,
      dropTime, characterHandle)` (`GameSpawnPackets.cs:44-67`, longueur fixe
      `7 + 1 + 4 + 12 + 1 + 1 + 8 + 8 + 4 + 12 + 12` l. 47), appelé exactement comme
      `GroundItemService.cs:83-84`.
      - handle du sol : `WorldObjectHandle.Next()` (`Game/Network/WorldObjectHandle.cs:10`), comme
        `GroundItemService.cs:68` ;
      - code d'objet : `ItemEntity.ItemResourceId` (l'entité d'inventaire), qui est aussi ce que
        `GroundItem.ItemCode` porte et ce que `TakeAsync` réinjecte par `AddItemAsync`
        (`GroundItemService.cs:110-111`) ;
      - quantité : les unités **réellement** retirées (jamais le `count` brut) ;
      - position : `client.ConnectionInfo.X / Y / Z` (`ConnectionInfo.cs:104-106`) et
        `client.ConnectionInfo.Layer` (`ConnectionInfo.cs:46`), sans dispersion —
        `NextScatter()` (`GroundItemService.cs:139`) est réservé aux drops de monstres, NGemity
        relocalise à la position exacte du joueur (`Player.cpp:3529`) ;
      - `dropTime` : `unchecked(ServerClock.Now + info.ClientClockOffset)`
        (`GroundItemService.cs:82`) ;
      - propriétaire : `Owner = client`, `OwnerHandle = info.CharacterHandle`
        (`GroundItem.cs:17-18`, `GroundItemService.cs:76`) ;
      - durée de vie : `LifetimeSeconds = 120` (`GroundItemService.cs:18,61`) — la même que les drops
        de monstres : ne pas inventer une politique de temporisation propre aux joueurs.
   b. **retrait d'inventaire — `TM_SC_ERASE_ITEM` (209), 20 octets** pour une paire :
      `GameCharacterPackets.BuildEraseItem(new[] { (handle, removed) })`
      (`GameCharacterPackets.cs:142-159` ; `7 + 1 + 1×12`), envoyé comme
      `InventoryService.cs:78`. Forme 7.3 validée en §4.
   c. **accusé — `TM_SC_DROP_RESULT` (205), 12 octets** : `item_handle` `uint32` à l'offset 7
      (**recopié** de la requête, y compris inconnu), `isAccepted` `uint8` à l'offset 11 (`1` ou `0`).
      Nouveau `GameCharacterPackets.BuildDropResult(uint itemHandle, bool isAccepted)`, sur le modèle
      de `BuildTakeItemResult` (`GameCharacterPackets.cs:117-130`) : `HeaderSize + 4 + 1`.
   d. **refus** → **uniquement** `205 { handle, 0 }` (12 octets), rien d'autre : c'est le comportement
      NGemity (`WorldSession.cpp:1441`).
9. **Aucune autre réponse.** Pas de `TS_SC_RESULT` de succès (NGemity n'en envoie pas), pas de
   `TS_SC_UPDATE_ITEM_COUNT` (255) ni de `TS_SC_DESTROY_ITEM` (254) : voir §6 pour le choix du 209.
10. **Le client 7.3 sait recevoir les trois réponses.** Le nom `TM_SC_DROP_RESULT` est dans la table du
    binaire (l. 26392) et la trame est dispatchée : `case MSG_ITEM_DROP_RESULT :` (l. 19716, dans la
    série `MSG_ITEM_INVEN` / `MSG_ITEM_WEAR` / `MSG_ITEM_DROP_INFO` / `MSG_ITEM_TAKE_RESULT`), avec un
    type de message dédié `.?AUSMSG_ITEM_DROP@@` (l. 44403) et une trace
    `SGameInterface - MSG_ITEM_DROP_RESULT` (l. 25134). Il sait aussi traiter le retrait :
    `SMSG_ERASE_ITEM` (l. 44399) et `TM_CS_ERASE_ITEM` (l. 26390) pour 209, `SMSG_UPDATE_ITEM_COUNT`
    (l. 44464) et `TM_SC_UPDATE_ITEM_COUNT` (l. 26374) pour 255, `TM_SC_DESTROY_ITEM` (l. 26375) pour
    254. Le choix du 209 n'est donc pas un pari d'API côté client.

Résumé de la réponse attendue : **id 205, 12 octets, `(item_handle recopié, isAccepted)`**, précédé en
cas de succès de **`TM_SC_ENTER` 70 octets** (objet au sol) puis de **`TM_SC_ERASE_ITEM` 20 octets**
(retrait), et en cas de refus de **rien d'autre**.

### 5.4 Visibilité et droit de ramassage (trancher explicitement)

Question posée par la carte : l'objet lâché est-il visible par les autres joueurs, et peuvent-ils le
ramasser ?

Réponse dans l'état actuel du dépôt : **non, le propriétaire seul**. Trois faits le fixent :

- `GroundItemService.TakeAsync` exige `ReferenceEquals(item.Owner, client)`
  (`GroundItemService.cs:90`) — un tiers qui enverrait le 204 sur le handle serait refusé ;
- `GroundItem.Owner` est le seul champ client de l'objet (`GroundItem.cs:17`) ; il n'existe aucune
  notion de groupe ni d'ordre de ramassage ;
- `ConnectionInfo` ne suit **aucun** objet au sol : `SpawnedNpcs`, `SpawnedMonsters`, `SpawnedProps`
  existent (`ConnectionInfo.cs:50,52,58`), `SpawnedItems` n'existe pas. Le streaming
  (`WorldObjectStreamer`) ne diffuse donc pas les objets au sol aux joueurs qui entrent dans la zone.

Conséquence à assumer : l'objet lâché n'est ni vu ni ramassable par un tiers (écart avec NGemity,
dont `World::AddItemToWorld`, `Chihiro/src/World/World.cpp:544`, diffuse l'entrée aux alentours et
dont `onTakeItem` (`WorldSession.cpp:1226-1316`) gère un ordre de ramassage
`m_pPickupOrder` (3 emplacements joueur/groupe, verrou de 3 s/4 s/5 s)). Étendre la visibilité
supposerait un sous-système de streaming des objets au sol qui n'existe pas : **hors périmètre de
cette tâche**, et à ne pas maquiller par une diffusion partielle non testable.

## 6. Écarts assumés avec NGemity

| Écart | Justification |
| --- | --- |
| On n'acquitte `isAccepted = true` **que** si un retrait a réellement eu lieu | NGemity envoie `true` **avant** de savoir : `DropItem` peut renvoyer `nullptr` (`Player.cpp:1299-1307`, cas `GetCount() < cnt`) et l'accusé est quand même `true` (`WorldSession.cpp:1438`). C'est un mensonge au client (le joueur croit avoir jeté, la pile est intacte). Nous acquittons sur le résultat des couples renvoyés par `EraseItemsAsync` (`CharacterService.cs:230-270`). |
| `count > pile` → **borner** au lieu de refuser en bloc | Le dépôt borne déjà partout (`Math.Min`, `CharacterService.cs:251-253`) ; la trame de réponse n'a **aucun champ** pour renvoyer un compte corrigé, mais le 209 porte la quantité réellement retirée, donc le client reste cohérent. NGemity refuse tout (pas de retrait) tout en acquittant `true` — refus invisible et incohérent. Voir §7.5. |
| Retrait d'inventaire par **209** avec paires `(handle, count)` au lieu de **255** (pile partielle) + **254** (pile vidée) | 209 est **le** chemin de retrait établi du dépôt (`InventoryService.cs:64-86` → `BuildEraseItem`, `GameCharacterPackets.cs:142-159`), sa forme 7.3 porte exactement `(handle, count)` (§4) et couvre les deux cas en un seul paquet. 254 et 255 sont absents de `GamePackets` et exigeraient deux nouveaux membres d'enum + deux constructeurs pour le même effet. Le client 7.3 traite les trois (l. 44399, 44464, 26375). |
| La garde « objet non jetable » (`flaglist[FLAG_DROP]`) n'est pas portée | NGemity lit un tableau de 19 colonnes (`ObjectMgr.cpp:151`) absentes de l'entité du dépôt, et l'alternative `ItemUseFlag.CantDrop = 15` n'est prouvée par rien (§7.3). Le client refuse déjà localement (`smsg_dump_fail`). Porter un refus non prouvé ferait refuser des objets légitimes. |
| La garde carte d'invocation est portée, mais sur des valeurs d'énumération du dépôt | La correspondance est exacte et vérifiable bit à bit (§5.3 point 6) ; c'est la seule garde NGemity intégralement transposable. |
| Aucune dispersion (`NextScatter`) pour un drop de joueur | NGemity relocalise à la position exacte du joueur (`Player.cpp:3529`) ; la dispersion du dépôt (`GroundItemService.cs:65,139`) est un choix local pour les tables de drop de monstres. |
| Objets lâchés visibles et ramassables par le seul joueur | Limitation structurelle du dépôt (§5.4) ; à documenter plutôt qu'à contourner. |
| NGemity écrit les id en dur, sans gating ; on garde le gating rzu | rzu tranche l'id et les types par version : 203 / 205 / 209 pour 7.3 (§4). |
| Aucun `TS_SC_RESULT` de succès | NGemity n'en envoie aucun pour ce paquet (`WorldSession.cpp:1433-1443`) ; l'accusé est le 205. Le `TS_SC_RESULT` n'est utilisé que pour la trame malformée, par convention des voisins du dépôt. |

## 7. NON ÉTABLI

1. **Preuve que le client 7.3 émet le 203 — absente.** Le nom figure dans la table d'annotation du
   binaire (`SFrame.exe` l. 26394), mais cette table est **partielle et non fiable comme liste
   d'émission** : elle contient `TM_CS_TAKE_ITEM` / `TM_CS_DROP_ITEM` (l. 26393-26394) et **omet**
   `TM_CS_ARRANGE_ITEM` et `TM_CS_CHANGE_ITEM_POSITION` (`grep` sur le même dump : aucune
   occurrence), deux paquets montants que ce client envoie à coup sûr. Ce qui l'établirait : une
   capture réseau décodée d'une session 7.3 pendant un drop, ou un désassemblage du chemin d'envoi de
   `SFrame.exe`. Hors de portée ici : l'exécution de `SFrame.exe` et de Lua est interdite par le profil
   de ce rôle, et aucune trace de session n'est fournie.
2. **Geste exact d'émission.** Les indices convergent vers une action d'interface (`SUIDropItemWnd`,
   `UIMSG_UI_DROP_ITEM_NAME`, `msgboxItemDropCheck`, `smsg_dump_item_confirm`) mais **aucune classe de
   commande** `SInput*Drop*` n'existe dans le binaire : les dix classes `SInput` présentes sont
   `SInputMove`, `SInputAttack`, `SInputCastCancel`, `SInputSkill`, `SInputTakeItem`, `SInputSit`,
   `SInputSitUp`, `SInputMount`, `SInputUnMount`, `SInputUseItem` (l. 44546-44555). Le drop est donc
   porté par une fenêtre, pas par une commande — ce qui est cohérent avec un envoi depuis l'UI, mais
   ne dit pas si la quantité est choisie dans une boîte de dialogue. Ce qui l'établirait : capture.
3. **Flag de jetabilité (`flag_drop`).** Deux représentations candidates, aucune exploitable telle
   quelle dans le dépôt :
   (a) NGemity `flaglist[FLAG_DROP = 5]` ← colonnes `flag_*` de `ItemResource` — ces colonnes existent
   dans le schéma Postgres du dépôt (`ArcadiaSchemaPSQL.sql:2083`, `flag_drop char not null`, série
   l. 2078-2101) mais **ne sont mappées par aucune propriété** de `ItemResourceEntity` ;
   (b) `ItemUseFlag.CantDrop = 15` (`Enums/ItemUseFlag.cs:23`), alimenté par `item_use_flag`
   (`MigrateDatabase/MssqlEntities/Arcadia/MSSQLItemResource.cs:44`, mapper
   `MigrateDatabase/Mappers/ArcadiaResourcesMappingProfile.cs:27`) — la valeur `15` se lit comme un
   **index de bit**, et rzu confirme que la colonne 9.x est bien un scalaire unique
   (`rzgame/src/ReferenceData/ItemResource.cpp` lit `item_use_flag` comme un entier), mais rien dans
   le dépôt ne prouve que le bit 15 soit « non jetable » pour les données 7.3 ; la fiche
   `docs/packet-specs/221-hide-equip-info.md` §7.6 a déjà relevé que cette énumération ne concorde pas
   avec les index de NGemity (`TargetUse = 7` vs `FLAG_TARGET_USE = 3`). Ce qui l'établirait : la
   table des bits de `item_use_flag` pour 7.3 (SQL/PDB d'origine) ou une capture d'un refus serveur
   réel. Décision prise : ne pas implémenter ce refus.
4. **Quelle notification d'inventaire le client 7.3 attend après un drop.** Le binaire établit qu'il
   *sait traiter* 205, 209, 254 et 255 (§5.3 point 10) mais pas laquelle il *attend* : aucun des trois
   n'est exclusif. Le choix 205 + 209 suit NGemity (205) et le chemin établi du dépôt (209). Ce qui
   l'établirait : capture d'une session retail 7.3 pendant un drop (ordre et contenu observés).
5. **`count > pile` : borner ou refuser.** Le client ne devrait pas pouvoir produire un `count`
   supérieur à la pile (il connaît le compte affiché), mais la borne de la fenêtre de quantité n'est
   pas établie ici (§7.2). Les deux politiques sont défendables ; la fiche fixe **borner** (§6) parce
   que le dépôt borne déjà et que le 209 notifie la quantité réelle. Ce qui l'établirait : capture
   d'un client 7.3 modifié ou d'une session avec désynchronisation forcée.
6. **Objet équipé.** Ni NGemity (`IsDropable` ne regarde pas `WearInfo`) ni le chemin de retrait du
   dépôt (`CharacterService.EraseItemsAsync`) ne contrôlent l'équipement. Rien n'établit que le client
   7.3 puisse émettre un 203 sur un objet porté. Décision : **ne pas ajouter** de contrôle (parité avec
   le chemin établi). Si Killian veut le refus, c'est une garde d'une ligne
   (`item.WearInfo != ItemWearType.None` → `205 { handle, 0 }`), mais elle n'est pas sourcée.
7. **Refus « état » / « encombrement ».** Aucun `ResultCode` (`NotActable`, `TooHeavy`,
   `NotActableInSecroute`…) n'est fondé pour ce paquet : NGemity ne produit que `isAccepted` 0/1, et le
   205 n'a aucun champ pour un motif. La fiche ne fixe donc aucun motif de refus.
8. **Signification du `count` pour une pile de quantité 1.** Le client affiche deux textes différents
   (unité / lot, §2), ce qui suggère `count = 1` pour un objet unique, mais la valeur exacte émise pour
   un objet non empilable n'est pas prouvée. Sans conséquence sur le serveur : `Math.Min` borne.

## 8. Commits et binaires épinglés

| Référence | Commit / empreinte | Usage |
| --- | --- | --- |
| Navislamia `master` (base de cette fiche) | `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` (2026-07-18, « Fix drop and Monsters attack character back ») | état du dépôt au moment de la rédaction |
| rzu (HEAD du clone) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02) | état de `reference/rzu` |
| rzu — introduction de `TS_CS_DROP_ITEM` | `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07) | champs et id 203 |
| rzu — gating `count` (`EPIC_4_1`) | `94885467536899bd53031f190d4488fe9e05c70a` (2017-03-03) | §4 |
| rzu — `TS_SC_ERASE_ITEM` « Since EPIC_7_2 » | `bdd362a600d104fd676facf12d6c6beb85a23ec4` (2017-02-26, PDB 5.2 → 8.1 dont 7.3) | §4, forme 7.3 du 209 |
| rzu — handles `strong_typedef` | `05bc2d82dac16003bc06162584e8559826310bff` (2020-03-29) | `ar_handle_t` = `uint32` |
| rzu — ids versionnés (`EPIC_9_6_3`) | `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11) | lecture de la double notation d'id |
| NGemity / Chihiro (HEAD) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03) | logique de référence |
| Client 7.3 — `SFrame.exe` | `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | noms de paquets, dispatch, RTTI, textes de trace |
| Client 7.3 — `db_string.rdb` | `sha256 4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` | textes `smsg_dump_*`, `msgboxItemDropCheck` |
| Client 7.3 — `extraction-manifest.json` | `sha256 574d90aa329826880f107c1260338f017dd7d296b539d499841bd940266d4ebb`, entrée `db_item.rdb` l. 168-173 (`data_id 3`, `offset 454968496`, `size 176825972`) | provenance des `.rdb` |

Tous les numéros de ligne du client proviennent du dump reproductible
`strings -n 4 /srv/navislamia/reference/client73/SFrame.exe` (63 099 lignes) et
`strings -n 4 .../db_string.rdb`, pris le 2026-09-18 sur les empreintes ci-dessus : ils ne sont pas
stables si les binaires changent.

Deux observations de provenance, à ne pas confondre avec des preuves de protocole :

- l'en-tête des `.rdb` de `reference/client73/` porte la date `20251207` (offset 0) puis
  `Written by Archemedes v0.1.0` (offset 16) — vérifié par `head -c 64 db_item.rdb | xxd` et
  `grep -abo`. Les données de ces `.rdb` ont donc été régénérées par un outil tiers en décembre 2025 :
  elles ne sont pas certifiées « retail 7.3 ». Cela n'affecte pas l'id 203 ni le format (établis par
  `SFrame.exe` et rzu), mais interdit de traiter `db_item.rdb` comme une source d'autorité sur les
  données d'objets. `SFrame.exe` ne porte pas cette marque (`grep -i archemedes` : aucune occurrence).
- `CLAUDE.md:1093` écrit que le `db_item.rdb` du client est indisponible ; au 2026-09-18 le fichier est
  présent (176 825 972 octets, `sha256 e70c1858…`) et figure dans le manifeste d'extraction. La note
  du dépot est donc périmée sur ce point (voir §9).

## 9. Note de livraison

- Branche : `hermes/packet-203-drop-item`, créée depuis `master`
  `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7`. Le commit de cette fiche ne touche que
  `docs/packet-specs/203-drop-item.md` et `.gitignore`.
- `.gitignore` : l'exception `!/docs/packet-specs/` est ajoutée après `!/docs/npc-dialogs.md`
  (`.gitignore:470-473`). Le blob obtenu est
  `c8c9d96a52f1e35076396dd0d99a96093d3ac863`, **identique** à celui des branches sœurs
  `hermes/packet-221-hide-equip-info`, `hermes/packet-253-use-item` et `hermes/packet-1202-emotion`
  (vérifié par `git hash-object`) : aucune collision au merge.
- Vérification de référence sur `master` avant rédaction : `dotnet build Navislamia.sln -c Debug` →
  code 0 (0 erreur, 160 avertissements) ; `dotnet test Tests/Tests.csproj` → code 0,
  **366 réussis / 366** (au moins 366 attendus). `git log --oneline origin/master..master` → vide.
- Cette fiche **ne modifie aucun fichier de code** : ni `GamePackets`, ni `GameActionPackets`,
  ni `GameClient`, ni les services. Le dev qui reprend la branche implémente, écrit le test d'offsets
  (taille totale 15 octets et position de chaque champ) et le bloc `CLAUDE.md` de §10 va dans la
  description de la MR — le dev **n'écrit pas** `CLAUDE.md`.
- Contrôle de cohérence à faire par le dev : `docs/packet-specs/` n'existe pas sur `master` ; la
  présente fiche est le quatrième fichier du répertoire, après les branches 221, 253 et 1202.
- Écart documentaire relevé au passage, **à arbitrer par Killian** (aucune action possible de ce
  rôle) : `CLAUDE.md:1093` affirme que le `db_item.rdb` du client est indisponible, alors que le
  fichier est présent dans `reference/client73/` et listé dans `extraction-manifest.json` (§8). La
  conséquence pratique est limitée au rendu visuel des objets (`9.4-only code`), mais la phrase est
  fausse en l'état. `CLAUDE.md` ne contient par ailleurs **aucun** renvoi vers `docs/packet-specs/`
  (`grep -n packet-specs CLAUDE.md` : aucun résultat, sur `master` comme sur les trois branches
  sœurs) : le bloc de §10 est donc le premier renvoi, et la consigne « `CLAUDE.md` pointe déjà vers le
  répertoire des fiches » n'est pas vérifiée au 2026-09-18.

## 10. Bloc pour CLAUDE.md (à recopier dans la description de la MR)

```markdown
- **`TM_CS_DROP_ITEM` (203) est implémenté** : trame fixe de **15 octets** — en-tête 7, `item_handle`
  `uint32` à l'offset 7, `count` `int32` à l'offset 11 (gating rzu `version >= EPIC_4_1`, donc
  `int32` en 7.3 ; le paquet bascule à 1203 seulement à partir d'`EPIC_9_6_3`). Aucune position n'est
  transmise : l'objet au sol est créé à la position du joueur (`ConnectionInfo.X/Y/Z/Layer`), sans
  dispersion, avec la durée de vie des drops de monstres (120 s).
- **Réponses** : `TM_SC_DROP_RESULT` (205), **12 octets** — `item_handle` recopié puis `isAccepted`
  `uint8` — précédé en cas de succès de `TM_SC_ENTER` (70 octets, objet au sol, `BuildEnterItem`) puis
  de `TM_SC_ERASE_ITEM` (209, 20 octets pour une paire `handle`/`count`, `BuildEraseItem`). Le retrait
  passe par `CharacterService.EraseItemsAsync`, qui borne le compte et renvoie ce qui a réellement été
  retiré : on n'acquitte `isAccepted = true` que dans ce cas (NGemity acquitte `true` même quand
  `popItem` a échoué — défaut à ne pas répliquer). Aucun `TS_SC_RESULT` de succès, aucun 254/255.
- L'objet lâché n'est **visible et ramassable que par le joueur qui l'a lâché** :
  `TakeAsync` exige `ReferenceEquals(item.Owner, client)` et `ConnectionInfo` ne suit aucun objet au
  sol (pas de `SpawnedItems`). L'écart avec NGemity (diffusion à la région, ordre de ramassage 3/4/5 s)
  est assumé et documenté dans `docs/packet-specs/203-drop-item.md`.
- **Réserves vérifiables** (fiche §7) : l'émission du 203 par le client 7.3 n'est pas prouvée (table
  d'annotation partielle) ; le geste d'émission (aucune classe `SInput*Drop*`) ; le flag de jetabilité
  (`flag_drop` / `item_use_flag` bit 15) n'est pas exploitable dans le dépôt et **aucun refus « non
  jetable » n'est implémenté** — le client refuse déjà localement (`smsg_dump_fail`) ; `count > pile`
  est borné (choix fixé, NGemity refuse en bloc) ; aucun contrôle d'objet équipé n'est ajouté.
- La garde NGemity « carte d'invocation liée » est portée : `ItemGroup.Summoncard = 13` et bit 31 de
  `ItemEntity.Flag` (`ItemFlag.Summon = 31`) correspondent exactement à `GROUP_SUMMONCARD = 13` et
  `ITEM_FLAG_SUMMON = 0x80000000` de NGemity. Elle restera inerte tant que rien n'écrit ce bit
  (`AddItemAsync` ne pose aucun flag).
```
