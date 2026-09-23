# 211 — TM_SC_OPEN_STORAGE / 212 — TM_CS_STORAGE (socle « entrepôt de personnage »)

Fiche de paquet établie par `navis-ref` (archéologue de protocole), branche
`hermes/packet-socle-entrepot-personnage`, à partir de `master`
`ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (merge de `hermes/packet-203-drop-item`).
Cette fiche ne contient **aucun** changement de code serveur : elle fixe l'identité, le déclencheur,
le format, le gating de version, le comportement attendu et les réserves vérifiables.

La carte couvre **deux paquets** : `212` (montant) et `211` (descendant). Les deux sont traités ici
parce que ni l'un ni l'autre n'est implémenté et que le déclencheur (une action de dialogue) relie les
deux : sans 211 il n'y a pas de fenêtre, sans 212 il n'y a pas de contenu à envoyer.

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id décimal (montant) | **212** (`0x00D4`) | `op_codes.md:61` — `[212] = "TM_CS_STORAGE"` |
| sens | client → serveur | rzu `librzu/src/packets/GameClient/TS_CS_STORAGE.h:18` (`PacketOrigin::Client`) |
| id décimal (descendant) | **211** (`0x00D3`) | `op_codes.md:60` — `[211] = "TM_SC_OPEN_STORAGE"` |
| sens | serveur → client | rzu `librzu/src/packets/GameClient/TS_SC_OPEN_STORAGE.h:14` (`PacketOrigin::Server`) |
| nom client | `TM_CS_STORAGE` / `TM_SC_OPEN_STORAGE` | dump `strings -n 4 SFrame.exe` l. 26387-26388 (VA `0xa53734` / `0xa53744`) |
| taille de trame, 212 | **20 octets**, fixe | §3 |
| taille de trame, 211 | **7 octets**, en-tête seul (7.3) | §3, §4 |
| paquet de contenu réutilisé | **207** `TM_SC_INVENTORY` | `op_codes.md:56` ; NGemity `Messages.cpp:327-357` |
| état dans le dépôt | **absents** de `GamePackets` (ni 211 ni 212) | `Game/Network/Packets/Enums/GamePackets.cs:26-47` |
| carte Trello | `trello.com/c/48ekxqC3` | suivi `navislamia:packet:212` |

Les deux noms existent dans le client 7.3, et — contrairement au cas du 203 (fiche
`docs/packet-specs/203-drop-item.md` §7.1, dont la table d'annotation est partielle) — **la table
d'identification du binaire porte les deux entrées, avec leur id immédiatement adjacent** :
`push $0xa53734` (« TM_CS_STORAGE ») puis `mov $0xd4,%eax` à `.text 0x67659a`/`0x6765ce` ; `push
$0xa53744` (« TM_SC_OPEN_STORAGE ») puis `mov $0xd3,%eax` à `.text 0x676524`. Vérification positive,
pas une absence (§7.1).

## 2. Ce que le joueur fait pour que le client l'envoie

Le client 7.3 n'ouvre **jamais** l'entrepôt de lui-même : la fenêtre est ouverte par le serveur, et le
déclencheur est une **entrée de dialogue de PNJ**.

- **Le catalogue de dialogues 7.3 porte le déclencheur.** `DevConsole/npc-dialogs.73.json` contient
  21 définitions de dialogue dont le menu porte `"Trigger":"open_storage()"`
  (ex. `NPC_Storage_Deva_contact`, `NPC_Storage_2_Secroute_contact`), et **20 PNJ** y sont reliés à
  un contact `NPC_Storage_*` (`Npcs` : `1009 NPC_Storage_Deva_contact()`, `2010`, `3009`, `4011`,
  `6011`, `7009`, `7028`, `7035`, `7036`, `11129`, `11239`…). Libellés de l'entrée :
  `@90601103`, `@90700903`, `@90101003`, `@90760203` selon la race la plus proche.
- **Le client connaît ce déclencheur en propre.** Le littéral `open_storage()` est dans le binaire
  (dump l. 22027, VA `0xa2e694`) et il est référencé une seule fois, en `.text 0x57d01c`, où il est
  comparé à la chaîne du déclencheur reçu, suivi d'une chaîne de contrôles d'état locaux
  (`0x57d058-0x57d0e5` : codes d'interface `0x1f`, `0x2d`, `0x38`, `0x45`, `0x46`) puis d'une
  résolution (`0x57d0eb-0x57d112`). Autrement dit, le client traite cette entrée de menu à part.
- **Chemin effectif.** Dialogue déjà implémenté côté dépôt : `TS_CS_CONTACT` → `TS_SC_DIALOG`
  (`docs/npc-dialogs.md:52-53`), puis le client renvoie la valeur choisie dans
  `TS_CS_DIALOG (3001)` — `uint16` longueur du déclencheur à l'offset 7, octets à l'offset 9
  (`docs/npc-dialogs.md:52-53`).
- **Ce qui doit s'ensuivre.** Le serveur doit reconnaître `open_storage()` et ouvrir l'entrepôt. Le
  chemin de référence est explicite : NGemity enregistre le déclencheur Lua `open_storage`
  (`Chihiro/src/Scripting/XLua.cpp:106`, `XLua.cpp:931-935`) qui appelle `Player::OpenStorage()`.
  Aujourd'hui le dépôt **advertise** ce déclencheur puis le jette : `NpcDialogService.Select`
  (`Game/Services/NpcDialogService.cs:63-115`) résout les actions connues (`RunTeleport`, l. 96-105),
  sinon cherche une page de dialogue de suivi et, à défaut, journalise
  « NPC dialog action open_storage is not implemented yet » (l. 109-115). C'est exactement le point
  d'accroche de cette tâche, et cela confirme la note `docs/npc-dialogs.md:62-64` (« Actions … open a
  specialized window, including … storage … require their own packet and service implementations.
  Their Lua triggers are intentionally not executed yet. »).

Le contenu de la fenêtre (déplacements, or) est ensuite émis par le client dans `212` : glisser-déposer
entre inventaire et entrepôt (modes 0/1), saisie de montant d'or (modes 2/3), fermeture (mode 4). Le
geste exact reste non établi (§7.3) ; la table des modes, elle, l'est par deux sources indépendantes
(§3.1, §5.1).

## 3. Structure sur le fil

En-tête du dépôt : `Game/Network/Packets/Header.cs:9-11` (`Length` `uint32`, `ID` `uint16`,
`Checksum` `uint8`), lu aux offsets 0/4/6 (`Header.cs:22-24`) ; `GameActionPackets` fixe
`HeaderSize = 7` (`Game/Network/Packets/Game/GameActionPackets.cs:8`).

### 3.1 `212` `TM_CS_STORAGE` (client → serveur) — 20 octets

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | **20** (`0x14`) | `Header.cs` ; rzu `TS_CS_STORAGE.h:8-12` ; client `0x48e913` (`movl $0x14`) |
| 4 | `uint16` LE | `ID` | **212** (`0xD4`) | `op_codes.md:61` ; client `0x48e8fb`/`0x48e906` |
| 6 | `uint8` | `Checksum` | somme des octets 0..5 | client `0x48e920-0x48e931` (boucle somme, dépôt à l'offset 6) |
| 7 | `uint32` LE | `item_handle` | handle d'objet (`ar_handle_t` = `uint32`) | rzu `TS_CS_STORAGE.h:8` ; `librzu/src/lib/Packet/GameTypes.h:40` ; client `0x48e934-0x48e937` (lu à la source +0x13) |
| 11 | `int8` | `mode` | 0 = inventaire→entrepôt, 1 = entrepôt→inventaire, 2 = or inventaire→entrepôt, 3 = or entrepôt→inventaire, 4 = fermeture | rzu `TS_CS_STORAGE.h:9` ; NGemity `WorldSession.h:23-29` ; client `0x48e946-0x48e949` (lu à la source +0x1f, comparé à 4 en `0x48e92d`) |
| 12 | `int64` LE | `count` | unités d'objet (modes 0/1) ou montant d'or (modes 2/3) | rzu `TS_CS_STORAGE.h:10-12` ; client `0x48e93a-0x48e943` (deux dwords source +0x17 et +0x1b → offsets 12 et 16) |

**Taille totale attendue : 20 octets.** Le client 7.3 la fixe lui-même : son constructeur de la trame
`.text 0x48e8f0-0x48e969` écrit `movl $0x14,-0x14(%ebp)` (longueur 20), l'id `212` en `+4`, la somme de
contrôle en `+6`, le handle en `+7`, le mode en `+11` et un `int64` en `+12` — soit, octet pour octet,
la structure de rzu pour Epic 7.3. C'est la preuve la plus forte disponible pour ce paquet (§7.1).

### 3.2 `211` `TM_SC_OPEN_STORAGE` (serveur → client) — 7 octets en 7.3

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | **7** | §4 : aucun champ en 7.3 |
| 4 | `uint16` LE | `ID` | **211** (`0xD3`) | `op_codes.md:60` |
| 6 | `uint8` | `Checksum` | somme des octets 0..5 | `Header.cs` |

**Taille totale attendue : 7 octets — en-tête seul.** Le champ unique `maxStorageItemCount` (`int32`)
n'existe qu'à partir d'`EPIC_7_4` (§4) ; la cible 7.3 (`0x070300`) est en dessous. NGemity, compilé en
`EPIC_4_1_1`, construit le paquet vide et son propre commentaire le dit : « jk, packet is empty »
(`Chihiro/src/Network/Messages.cpp:1001-1009`).

Précisions de lecture :

- `ar_handle_t` est un `strong_typedef` sur `uint32_t` (`librzu/src/lib/Packet/GameTypes.h:40`) → 4
  octets ; c'est le même handle que celui de l'inventaire, que le dépôt dérive de `ItemEntity.Id`
  (`Game/Network/Packets/Game/GameCharacterPackets.cs:343`).
- `mode` est **non signé** dans rzu (`int8_t`) et NGemity teste `switch ((STORAGE_MODE)pRecvPct->mode)`
  sans garde de borne (`WorldSession.cpp:1600`) : une valeur hors 0..4 tombe dans le `default: break`
  silencieux (`WorldSession.cpp:1673-1675`). Le dépôt doit refuser explicitement (§5.3).
- `count` est **signé** et sur 8 octets : rzu le type `int64_t` pour `>= EPIC_4_1_1`, et le client 7.3
  le construit effectivement sur 8 octets (§3.1). Une valeur négative ou nulle est représentable sur
  le fil et doit être refusée pour les modes 0..3 (§5.3).

## 4. Gating de version

Valeurs de référence : `EPIC_4_1_1 = 0x040101`, `EPIC_7_3 = 0x070300`, `EPIC_7_4 = 0x070400`,
`EPIC_9_6_3 = 0x090603` (`librzu/src/lib/Packet/PacketEpics.h:51,59,60,96`). La cible du serveur est
`0x070300`.

| Champ / id | Gating montré par rzu | Décision pour Epic 7.3 | Source |
| --- | --- | --- | --- |
| id du 212 | `X(212, version < EPIC_9_6_3)` / `X(1212, version >= EPIC_9_6_3)` | **212** (`0x070300 < 0x090603`) | rzu `TS_CS_STORAGE.h:14-16` |
| `item_handle` | aucun gating de champ | `uint32` | rzu `TS_CS_STORAGE.h:8` |
| `mode` | aucun gating de champ | `int8` | rzu `TS_CS_STORAGE.h:9` |
| `count` | `_(impl)(simple)(int64_t, count, version >= EPIC_4_1_1)` / `(uint32_t, count, version < EPIC_4_1_1)` | **`int64` sur 8 octets** (`0x070300 >= 0x040101`) | rzu `TS_CS_STORAGE.h:10-12` |
| id du 211 | `X(211, version < EPIC_9_6_3)` / `X(1211, version >= EPIC_9_6_3)` | **211** | rzu `TS_SC_OPEN_STORAGE.h:10-12` |
| `maxStorageItemCount` | `_(simple)(int32_t, maxStorageItemCount, version >= EPIC_7_4, 10000)` | **absent en 7.3** : la trame fait 7 octets | rzu `TS_SC_OPEN_STORAGE.h:8` |

Le seul piège de version de ce paquet est le second : `maxStorageItemCount` apparaît à
`EPIC_7_4` (`0x070400 > 0x070300`), donc **jamais en 7.3**. La valeur `10000` de rzu n'est que la
valeur par défaut du champ quand il existe (à partir de 7.4) : ce n'est ni une capacité 7.3, ni une
borne à imposer au serveur (§7.2).

Le remap d'id à `EPIC_9_6_3` (`+1000`) n'affecte aucun des deux paquets en 7.3 ; il n'est mentionné
que pour expliquer la double notation.

## 5. Traitement attendu

### 5.1 Ce que NGemity en fait

**Ouverture.** `Player::OpenStorage()` (`Chihiro/src/Entities/Player/Player.cpp:2918-2937`) refuse si
une compétence est en cours, puis charge depuis la base (`DB_ReadStorage`, `Player.cpp:262-…`) et,
au tour suivant, appelle `Messages::SendItemList(this, true)` (l'entrepôt), `openStorage()` (l. 2934)
et `Messages::SendPropertyMessage(this, this, "storage_gold", GetStorageGold())` (l. 2935).
`openStorage()` (`Player.cpp:2961-2965`) pose `m_bIsUsingStorage = true` puis envoie
`Messages::SendOpenStorageMessage` (`Messages.cpp:1001-1009`) — le `211` **vide**.

**Contenu.** L'entrepôt est un second objet `Inventory` (`Player.h:433` `Inventory m_Storage`) et il est
envoyé avec **le même paquet que l'inventaire** : `Messages::SendItemList(_, bIsStorage)`
(`Messages.cpp:327-357`) découpe en tranches de 200 items et émet des `TS_SC_INVENTORY` sans aucun
champ discriminant — c'est le client qui sait où il affiche la liste, parce qu'il a ouvert la fenêtre
sur le `211`. Corollaire pour le dépôt : `GameCharacterPackets.BuildInventory(ItemEntity[])`
(`GameCharacterPackets.cs:136-153`, tranches `HeaderSize + 2 + n×89`) est réutilisable tel quel, dans
la limite du nombre d'items par trame déjà en place.

**Persistance.** Les lignes vivent dans la **même table `Item`** que l'inventaire, discriminées par
propriétaire (`Chihiro/src/Database/Implementation/CharacterDatabase.cpp:93-97`) :

```
SELECT sid, idx, code, cnt, … FROM Item WHERE account_id = ? AND owner_id = 0 AND auction_id = 0 AND keeping_id = 0
```

avec l'`account_id` du joueur (`Player.cpp:265` lit `PLAYER_FIELD_ACCOUNT_ID`) — l'inventaire, lui, est
`account_id = 0 AND owner_id = ?` (même fichier, l. 53). **L'entrepôt est donc commandé par le compte,
pas par le personnage.** L'or d'entrepôt n'a, lui, pas de colonne : NGemity le stocke comme une **ligne
d'objet bidon de code 0** (`CharacterDatabase.cpp:97`, `UPDATE Item SET cnt = ? … AND code = 0`), et
`DB_ReadStorage` la reconnaît (`Player.cpp:333`, `386`).

**Traitement du `212`.** `WorldSession::onStorage` (`Chihiro/src/Network/GameNetwork/WorldSession.cpp:1590-1676`) :

```
if (!m_pPlayer->m_bIsUsingStorage || m_castingSkill != nullptr
    || trade target != 0 || !IsActable())
    → TS_SC_RESULT(NOT_ACTABLE, item_handle)          // l. 1595-1598

switch (mode) {
  case 0: case 1:   // objets
      count <= 0            → NOT_ENOUGH_MONEY      // l. 1603-1606
      handle inconnu        → NOT_EXIST             // l. 1609-1612
      objet d'un autre joueur → ACCESS_DENIED       // l. 1613-1616
      garde carte d'invocation liée (mode 0, drapeau événement) → ACCESS_DENIED   // l. 1619-1627
      sinon MoveInventoryToStorage / MoveStorageToInventory, puis Save(true)
  case 2: case 3:   // or
      refus silencieux si solde insuffisant         // l. 1643-1644, 1657-1658
      au-delà de MAX_GOLD_FOR_STORAGE / MAX_GOLD_FOR_INVENTORY → TOO_MUCH_MONEY  // l. 1645-1648, 1659-1662
      sinon ChangeGold / ChangeStorageGold
  case 4:           // fermeture
      m_bIsUsingStorage = false                     // l. 1670-1672
  default: break                                    // l. 1673-1675 — mode inconnu : silence
}
```

Aucun accusé de succès : après un déplacement d'objet, NGemity laisse le soin au dépôt d'objets de
notifier (`Player::onAdd` → `Messages::SendItemMessage`, `Player.cpp:1200` et `Messages.cpp:250-260`,
soit un `TS_SC_INVENTORY` à un item ; `Player::onChangeCount` → `SendItemCountMessage`,
`Player.cpp:1247`, soit `TS_SC_UPDATE_ITEM_COUNT` (255) ; `Player::onRemove` →
`SendItemDestroyMessage`, `Player.cpp:1242`, soit `TS_SC_DESTROY_ITEM` (254)). Les valeurs d'or
changent par la propriété `storage_gold` (`Player.cpp:2948`).

**Modification de l'énumération de résultats — corrige une prémisse de la carte.** NGemity **ne
renvoie jamais** les codes 51 (`NOT_ACTABLE_WHILE_USING_STORAGE`) ni 88
(`TARGET_IS_USING_STORAGE`) : les deux membres existent dans son énumération
(`shared/Server/TS_MESSAGE.h:104,139`) mais aucun `grep` du dépôt NGemity ne les emploie
(`grep -rn "NOT_ACTABLE_WHILE_USING_STORAGE\|TARGET_IS_USING_STORAGE"` → seules les deux déclarations).
Ses refus réels sont `NOT_ACTABLE = 5`, `NOT_EXIST = 1`, `ACCESS_DENIED = 6`,
`TOO_MUCH_MONEY = 53`, `NOT_ENOUGH_MONEY = 10`, `TOO_CHEAP = 50` — valeurs identiques à celles du
dépôt (`Game/Network/Packets/ResultCode.cs:12,8,13,65,17`). Le client 7.3 sait **afficher** 51 et 88
(chaînes `RESULT_NOT_ACTABLE_WHILE_USING_STORAGE`, dump l. 18860, et `RESULT_TARGET_IS_USING_STORAGE`,
dump l. 18824), donc les employer reste possible — mais ce serait un choix du dépôt, pas un portage.

### 5.2 Ce que rzu en fait

Rien, et c'est un fait notable : rzu **déclare** l'entrepôt sans jamais l'utiliser. Il porte la
liaison de base `storage_data` (`rzgame/src/Database/DB_StorageItem.cpp:4-39`) avec exactement la même
requête que NGemity (`DB_StorageItem.cpp:7` : `select * from item where account_id = ? AND owner_id = 0
AND auction_id = 0 AND keeping_id = 0`), la classe `DB_StorageItem` complète
(`rzgame/src/Database/DB_StorageItem.h:8-33`, jumelle de `DB_Item.h:8-33`) — et **aucun appelant** :
`grep -rn "StorageItem\|DB_Storage"` sur tout rzu ne renvoie que ces deux fichiers. Aucun handler
`TS_CS_STORAGE`, aucun envoi de `TS_SC_OPEN_STORAGE`. rzu tranche donc l'id, les types et le gating
(§3, §4) et **confirme le périmètre de stockage au niveau du compte**, mais ne tranche pas le
comportement.

### 5.3 Ce que le serveur Navislamia doit faire

1. **Enum + dispatch, ensemble.** Ajouter `TM_SC_OPEN_STORAGE = 211` et `TM_CS_STORAGE = 212` à
   `Game/Network/Packets/Enums/GamePackets.cs` (autour des l. 26-47, où 207/210/216/218/219 sont déjà
   présents) et, pour le montant, un bras dans la chaîne de `if` du `Receive`
   (`Game/Network/Clients/GameClient.cs:696-706` pour les voisins 219/218) **avant** le `switch` final
   (`GameClient.cs:791-802`, dont le `_ => throw new Exception("Unknown Packet Type")` en l. 802 :
   aucun membre de `GamePackets` ne doit pouvoir l'atteindre). `TM_SC_OPEN_STORAGE` est descendant : il
   est déclaré sans bras, comme `TM_SC_INVENTORY` (207) ou `TM_SC_TAKE_ITEM_RESULT` (210) sur `master`.
2. **Lecture.** `GameActionPackets.TryReadStorage(ReadOnlySpan<byte>, out StorageRequest)` sur le
   modèle exact de `TryReadChangeItemPosition` (`GameActionPackets.cs:112-126`) : longueur minimale
   `HeaderSize + 13` (soit 20 octets), `item_handle` = `ReadUInt32LittleEndian(7)` via
   `ArithmeticCoding`-style `BinaryPrimitives`, `mode` = `packet[11]` (lu comme octet, validé 0..4),
   `count` = `ReadInt64LittleEndian(12)`. Trame plus courte → `SendResult(212, InvalidArgument)` par
   convention des voisins (`GameClient.cs:378`, `:451`).
3. **Déclencheur.** `NpcDialogService.Select` (`Game/Services/NpcDialogService.cs:63-115`) doit
   reconnaître `open_storage()` **comme il reconnaît `RunTeleport`** (l. 90-105) : l'action est résolue
   avant la recherche de page de dialogue de suivi, et l'entrepôt n'a de sens que si le joueur est
   **devant le PNJ** — le garde existant (`info.SpawnedNpcIdsByHandle.ContainsKey(npcHandle)`, l. 132)
   le garantit déjà. Ne pas exécuter de Lua (interdit), ne pas élargir la surface : le nom est connu
   d'avance.
4. **Conteneur.** Décision : **entrepôt au niveau du compte**, comme rzu et NGemity le montrent tous
   les deux (§5.1, §5.2 : `account_id = ? AND owner_id = 0`). Le dépôt n'a **aucun** conteneur de ce
   type : `ItemEntity.CharacterId` (`Game/DataAccess/Entities/Telecaster/ItemEntity.cs:8-9`) lie un
   objet à un personnage, et `ItemStorageEntity` — malgré un commentaire qui envisage le compte
   (`ItemStorageEntity.cs:14` : « at a alter point refactor this to use account id for global storage
   across all characters? ») — est l'entrepôt **des ventes aux enchères** (`StorageType.cs` : 1..4, 30..34
   = objets et or d'enchères), pas l'entrepôt de comptoir. Deux voies, à trancher par le dev dans le
   sens de la moins invasive : (a) `ItemEntity` avec `CharacterId = null` et `AccountId = <compte>`
   (les deux colonnes existent déjà, `ItemEntity.cs:8,13`) ; (b) une table dédiée. La voie (a) suit
   l'invariant rzu/NGemity « une seule table d'objets, discriminée par propriétaire » et n'exige aucune
   migration ; c'est la recommandation de cette fiche.
5. **Or d'entrepôt.** Ne **pas** reproduire la ligne d'objet bidon de NGemity (§5.1, `code = 0`) :
   le dépôt a `CharacterEntity.Gold` et une propriété nommée (`GameStatPackets.BuildProperty(uint,
   string, long)`, `GameStatPackets.cs:78`, employée l. 195-226 de `GameActions.cs`) : envoyer
   `BuildProperty(handle, "storage_gold", <or>)` est le chemin établi, et le client 7.3 connaît la clé
   (dump l. 26441). Où **persister** cet or est non établi (§7.5) — c'est le vrai choix ouvert.
6. **Réponses attendues.**
   - ouverture : **`211`, 7 octets, en-tête seul** (§3.2), puis le contenu en **`207`
     `TM_SC_INVENTORY`** (`BuildInventory`, une trame même si la liste est vide : `GameCharacterPackets.cs:140-144`),
     puis la propriété `storage_gold` ;
   - déplacement d'objet (mode 0/1) : les trames d'items réellement déplacées — `207` via
     `BuildInventory(new[]{ item })` (équivalent NGemity `SendItemMessage`), `255`
     `TM_SC_UPDATE_ITEM_COUNT` si seule la quantité change (`GameCharacterPackets.cs`, chemin déjà
     utilisé par `GroundItemService.cs:192`), `254` `TM_SC_DESTROY_ITEM` si la pile quitte sa liste ;
   - or (mode 2/3) : propriété `storage_gold` + propriété d'or du personnage (`BuildProperty`), à
     l'image de NGemity (`Player.cpp:2948`) ;
   - fermeture (mode 4) : aucun envoi ; remettre l'état à « entrepôt fermé » (NGemity :
     `m_bIsUsingStorage = false`, `WorldSession.cpp:1671`) ;
   - refus : `TS_SC_RESULT` avec l'id **212** et le code du tableau ci-dessous, `value` =
     `item_handle` **recopié** comme NGemity (`WorldSession.cpp:1596`), y compris pour un handle
     inconnu ; `SendResult(ushort id, ushort result, int value)` (`GameClient.cs:56`).
7. **Validation** (trancher dans l'ordre, ne rien inventer au-delà) :

   | Cas | Décision 7.3 | Source |
   | --- | --- | --- |
   | trame < 20 octets | `212` → `InvalidArgument` (15 octets) | convention du dépôt (`GameClient.cs:378,451`) |
   | mode hors 0..4 | refus `NotActable` (5) — NGemity est silencieux, ne pas copier le silence | NGemity `WorldSession.cpp:1673-1675` ; §6 |
   | aucun entrepôt ouvert (`StorageSecurityCheck` / état de session) | refus `NotActable` (5) | NGemity `WorldSession.cpp:1595` |
   | `count <= 0` avec mode 0..3 | refus `NotEnoughMoney` (10) | NGemity `WorldSession.cpp:1603-1606` |
   | handle inconnu ou n'appartenant pas au joueur | refus `NotExist` (1) / `AccessDenied` (6) | NGemity `WorldSession.cpp:1609-1616` |
   | objet déjà du bon côté (mode incohérent avec la liste) | agir comme NGemity : ne rien faire, ne rien acquitter | NGemity `WorldSession.cpp:1618-1637` |
   | `count > pile` | **borner** à la pile (précédent du dépôt) et notifier la quantité réelle | `CharacterService.cs` (`Math.Min`), §6 |
   | or demandé > solde de la source | refus silencieux chez NGemity → ici `NotActable`, jamais un succès muet | NGemity `WorldSession.cpp:1643-1644` ; §6 |

   `ConnectionInfo.StorageSecurityCheck` (`Game/Network/Clients/ConnectionInfo.cs:119`) existe déjà,
   initialisé à `false` et **jamais lu ni écrit** ailleurs : c'est l'emplacement naturel de l'état
   « entrepôt ouvert » de la session, à côté de `CharacterHandle` et `CharacterGold`
   (`ConnectionInfo.cs:40-41`).

Capacité : **aucune borne serveur n'est fondée en 7.3** (§7.2). Ne pas en inventer une.

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | Justification |
| --- | --- |
| La carte annonce « NGemity renvoie 51 et 88 » : **faux** | Les deux valeurs sont **déclarées** et jamais employées (`TS_MESSAGE.h:104,139` ; aucun usage dans `Chihiro/`). Les refus réels sont 5, 1, 6, 53, 10 (§5.1). Les codes 51/88 restent affichables par le client, donc employables par choix, mais cette fiche ne les impose pas : le portage honnête des refus NGemity, c'est 5/1/6/53/10. |
| Or d'entrepôt en **propriété persistée**, pas en ligne d'objet `code = 0` | NGemity détourne la table d'objets (`CharacterDatabase.cpp:97`, `Player.cpp:333,386`) : une ligne de code 0 dont `cnt` est l'or. Le dépôt a déjà un modèle d'or de personnage et une propriété nommée (§5.3 point 5) ; répliquer la ligne bidon ferait entrer un objet inexistant dans `BuildInventory`, donc dans la liste visible du joueur — défaut visible. |
| Mode d'objet incohérent avec la liste : **action silencieuse** (comme NGemity) mais **mode hors 0..4 refusé** | NGemity ne répond rien dans les deux cas (`1641-1637`, `1673-1675`). Le silence sur un mode inconnu est indétectable côté client (pas de trace, pas de message) ; un refus `NotActable` coûte 15 octets et laisse une trace exploitable. Le silence du mode incohérent, lui, est invisible pour le joueur dans les deux implémentations : le porter tel quel. |
| Refus de solde insuffisant explicite (`NotActable`) au lieu du `return` muet de NGemity | NGemity sort sans rien dire quand le solde est trop faible (`1643-1644`, `1657-1658`) : le joueur voit une fenêtre figée. Aucun code NGemity n'existe pour ce cas ; `NotActable` (5) est le code le plus proche et le moins inventif. À considérer comme un choix du dépôt, pas un portage. |
| `count > pile` → **borner** | Le dépôt borne déjà partout (`Math.Min`) ; les trames d'items notifient la quantité réelle, donc le client reste cohérent. NGemity, lui, sort par `MoveStorageToInventory`/`MoveInventoryToStorage` qui renvoient `false` sans réponse (`Player.cpp:2983-2987`, `3031-3035`). |
| Entrepôt commandé par le **compte** (pas le personnage) | Ce n'est pas un écart mais un alignement : rzu (`DB_StorageItem.cpp:7`) et NGemity (`CharacterDatabase.cpp:94`) écrivent la même requête `account_id = ? AND owner_id = 0`. Le dépôt n'a pas ce conteneur, il faut le créer (§5.3 point 4). |
| Aucun accusé de succès pour 212, uniquement les trames d'items | Comportement NGemity (`WorldSession.cpp:1638` : `Save(true)`, aucune réponse dédiée) ; le client n'a pas de message « déplacement réussi » à afficher, il se contente de redessiner les listes. |
| NGemity écrit les id en dur sans gating ; on garde le gating rzu | rzu tranche l'id et les types par version : 211/212, `count` `int64`, pas de `maxStorageItemCount` en 7.3 (§4). |

## 7. `NON ÉTABLI`

1. **Tolérance du client à une charge après l'en-tête du `211`.** rzu gate le champ à `EPIC_7_4`,
   NGemity envoie une trame vide et le commente, et le client 7.3 nomme le paquet — mais **aucune des
   trois sources ne dit si le client 7.3 rejette 4 octets surnuméraires**. La table d'id du binaire se
   contente de nommer (`.text 0x676524-0x6765ce`) ; la chaîne de cas voisine
   (`.text 0x8901f8-0x890229`) associe à 211 et 212 une valeur de registre non constante, dont la
   sémantique (taille attendue ? borne de charge ? autre) **n'a pas été établie** et sur laquelle je
   ne m'appuie pas. Décision appliquée : envoyer la forme 7.3 stricte, **7 octets**. Ce qui
   l'établirait : une capture d'une session 7.3 réelle, ou un désassemblage du récepteur du `211` dans
   `SFrame.exe`.
2. **Capacité de l'entrepôt.** Aucune source 7.3 : le `10000` de rzu n'existe qu'à partir de 7.4 (§4) ;
   le nombre d'emplacements affichés par le client vient de sa mise en page (`STORAGE_X` / `STORAGE_Y`,
   dump l. 25634, et `storage_slot%02d`, l. 22244, référencés depuis `.text 0x6510e4-0x598e00`) ;
   les archives d'interface du client (`data.001`…`data.008`) ne sont **pas** dans
   `reference/client73/` (seul `data.000` est extrait, cf. `extraction-manifest.json`). Question
   précise : quelle est la capacité maximale d'entrepôt en Epic 7.3, et le serveur doit-il la refuser
   au-delà ? **Ne pas inventer de borne** : le mode 0/1 d'un dépassement retomberait sur un refus
   `NotActable` non documenté.
3. **Geste exact d'émission du `212`.** Le constructeur est établi (`.text 0x48e8f0`) et son unique
   appelant direct identifié (`.text 0x49d673`, dans un aiguillage `call` sur table), mais **la
   commande d'interface qui mène à ce cas** (id de bouton, glisser-déposer, champ de quantité) n'a pas
   été identifiée, et l'appelant n'est atteignable qu'en parcourant une table de sauts dont les
   indices ne sont pas documentés. Ce qui l'établirait : la capture d'un déplacement en jeu, ou la
   résolution de la table de commandes de `SUIStorageWnd` (constructeur `.text 0x598a00`, vtables
   `0xa302ec` et `0xa302c8`).
4. **Le mode 4 est-il réellement émis ?** Le constructeur compare le champ de mode à `4`
   (`.text 0x48e92d`) et NGemity traite `STORAGE_CLOSE` (`WorldSession.cpp:1670-1672`) — mais rien ne
   prouve que la fermeture de la fenêtre passe par le réseau plutôt que par un simple état local
   client. Conséquence pratique : le serveur **doit** accepter le mode 4 sans erreur et sans réponse,
   et **ne doit pas** compter sur lui pour libérer son état (une déconnexion ou un autre paquet peut
   arriver sans mode 4).
5. **Où persister l'or d'entrepôt.** rzu ne le montre pas (sa liaison ne lit pas d'or), NGemity le
   met dans une ligne d'objet de code 0 (§5.1) et le dépôt n'a aucune colonne pour cela. Question
   précise : une colonne sur le compte (`AccountEntity` ?) existe-t-elle ou faut-il l'ajouter, et
   quelle borne appliquer ? **Non tranché** : cette fiche fixe seulement la propriété envoyée
   (`storage_gold`, §5.3 point 5) et laisse la persistance au dev, avec le choix de la table (§5.3
   point 4) qu'il devra documenter dans la MR.
6. **Le conteneur : `ItemEntity` élargi ou table dédiée.** Voir §5.3 point 4. Les deux voies sont
   compatibles avec rzu/NGemity ; le dépôt n'a pas d'antécédent de conteneur au niveau du compte
   (`ItemStorageEntity` est l'entrepôt d'enchères, `StorageType.cs`). La recommandation (voie (a))
   s'appuie sur des colonnes déjà migrées ; elle n'est **pas** prouvée par une donnée 7.3 (aucune
   table de base retail 7.3 n'est disponible dans le dépôt).
7. **Forme exacte de la notification de déplacement d'objet.** NGemity notifie par les rappels de son
   `Inventory` (`SendItemMessage` → 207 à un item, `SendItemCountMessage` → 255, `SendItemDestroyMessage`
   → 254), mais rien n'établit laquelle de ces trois trames le client 7.3 **attend** pour un
   déplacement entre listes (les trois y sont connues : `TM_SC_INVENTORY`, `TM_SC_UPDATE_ITEM_COUNT`,
   `TM_SC_DESTROY_ITEM` sont dans la table d'id du binaire). Le choix retenu suit NGemity ; ce qui
   l'établirait : une capture d'un déplacement réel.
8. **Refus par les codes 51 / 88.** Le client 7.3 sait les afficher (dump l. 18824, 18860) et le
   dépôt les a dans son énumération (`ResultCode.cs:63,104`), mais **aucune des deux références ne les
   envoie** (§5.1). Les employer (par exemple 51 pour « aucune autre action pendant que l'entrepôt est
   ouvert », 88 pour un échange visant un joueur dont l'entrepôt est ouvert) serait un choix de
   conception du serveur, non un portage. Non tranché ici : **`NotActable` (5)** est prescrit (§5.3
   point 6).

## 8. Commits et binaires épinglés

| Référence | Commit / empreinte | Usage |
| --- | --- | --- |
| Navislamia `master` (base de cette fiche) | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (merge de `hermes/packet-203-drop-item`, PR #4) | état du dépôt au moment de la rédaction |
| rzu (HEAD du clone) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02, « packets: fix TS_SC_INVENTORY with older epics ») | état de `reference/rzu` |
| rzu — `TS_CS_STORAGE` / `TS_SC_OPEN_STORAGE` | contenus dans le HEAD ci-dessus (`librzu/src/packets/GameClient/TS_CS_STORAGE.h`, `TS_SC_OPEN_STORAGE.h`) | ids, champs, gating (§3, §4) |
| rzu — liaison de base `storage_data` | contenue dans le HEAD ci-dessus (`rzgame/src/Database/DB_StorageItem.{h,cpp}`) | périmètre de stockage au niveau du compte (§5.2) |
| NGemity / Chihiro (HEAD) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03, « Fix compilation issue for GCC ») ; compile `EPIC_4_1_1` (`shared/Common/Define.h:25`) | logique de référence (§5.1) |
| Client 7.3 — `SFrame.exe` | `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 octets) | constructeur du `212`, table d'id, RTTI, textes |
| Client 7.3 — `extraction-manifest.json` | présent dans `reference/client73/` (mêmes empreintes que la fiche 203 §8) | provenance des `.rdb` ; absence de `data.001`+ (§7.2) |

Tous les numéros de ligne du client proviennent du dump reproductible
`strings -n 4 /srv/navislamia/reference/client73/SFrame.exe` (63 099 lignes, pris le 2026-09-20 sur
l'empreinte ci-dessus) ; les adresses sont des **VA** du fichier (`ImageBase 0x400000`, `.text` VA
`0x401000`), obtenues par désassemblage `objdump -d` — elles ne sont pas stables si le binaire change.

## 9. Note de livraison

- Branche : `hermes/packet-socle-entrepot-personnage`, créée depuis `master`
  `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (nom de branche imposé par la carte du PO, repris par la
  carte `navis-dev` ; il ne suit pas le motif `hermes/packet-<id>-<nom>` des branches sœurs).
- Le commit de cette fiche ne touche que `docs/packet-specs/211-212-storage.md` :
  `docs/packet-specs/` est déjà suivi sur `master` (quatre fiches : 203, 253, 550, 1202), donc **aucun**
  changement de `.gitignore` n'est nécessaire — contrairement aux branches sœurs, qui devaient créer
  le répertoire. Le dev qui reprend la branche ajoute son implémentation, ses tests d'offsets (taille
  totale **20 octets** pour le 212, **7** pour le 211, et position de chaque champ) et le bloc `§10`
  destiné à `CLAUDE.md` **dans la description de la MR** — le dev **n'écrit pas** `CLAUDE.md`.
- Vérification exécutée sur `master` avant rédaction (le 2026-09-20) : `dotnet build
  Navislamia.sln -c Debug` → code 0 ; `dotnet test Tests/Tests.csproj` → code 0, **366 réussis /
  366** ; `git log --oneline origin/master..master` → vide. Aucune commande de ce rôle n'écrit de
  code serveur : le dépôt n'est pas modifié par cette fiche.
- Points à trancher par le dev dans sa MR (au-delà de §7) : le conteneur (§5.3 point 4), la
  persistance de l'or d'entrepôt (§7.5) et l'état « entrepôt ouvert » de la session
  (`ConnectionInfo.StorageSecurityCheck`, §5.3 point 7).
- Le dépôt **advertise déjà** `open_storage()` au client sans savoir l'exécuter
  (`Game/Services/NpcDialogService.cs:109-115`, journalisé en `Debug`) : ce n'est pas une régression
  introduite ici, mais c'est la raison pour laquelle la fenêtre ne s'ouvre pas aujourd'hui, et cela
  doit être dit dans la MR.
- Le dev a livré le socle : voir **§11** (périmètre, décisions, tests, mesures) et la section
  `A VERIFIER PAR KILLIAN`. Les trois points laissés au dev — conteneur, or d'entrepôt, état de
  session — y sont tranchés, le troisième comme les deux autres par la fiche elle-même
  (`ConnectionInfo.StorageSecurityCheck`).

## 10. Bloc pour `CLAUDE.md` (à recopier dans la description de la MR)

```markdown
- **L'entrepôt de personnage (`TM_CS_STORAGE` 212 / `TM_SC_OPEN_STORAGE` 211) est implémenté.** Le
  `212` (client → serveur) est une trame fixe de **20 octets** : en-tête 7, `item_handle` `uint32` à
  l'offset 7, `mode` `uint8` à l'offset 11 (0 = objets inventaire→entrepôt, 1 = entrepôt→inventaire,
  2 = or inventaire→entrepôt, 3 = or entrepôt→inventaire, 4 = fermeture) et `count` **`int64`** à
  l'offset 12 (gating rzu `version >= EPIC_4_1_1`). Le `211` (serveur → client) fait **7 octets en
  7.3, en-tête seul** : le champ `maxStorageItemCount` de rzu n'apparaît qu'à partir d'`EPIC_7_4`
  (`0x070400`), et son `10000` par défaut n'est donc **pas** une capacité 7.3. Les deux ids basculent
  à 1211/1212 seulement à partir d'`EPIC_9_6_3`.
- **Déclencheur : une action de dialogue, pas une fenêtre client.** 20 PNJ du catalogue 7.3
  (`DevConsole/npc-dialogs.73.json`, contacts `NPC_Storage_*`) proposent une entrée de menu dont le
  déclencheur est `open_storage()`, et le client 7.3 porte ce littéral en propre. Le serveur doit donc
  le reconnaître dans `NpcDialogService.Select`, comme il reconnaît déjà `RunTeleport`, puis envoyer
  `211` + le contenu en `TM_SC_INVENTORY` (207, mêmes tranches que l'inventaire) + la propriété
  `storage_gold`. Aucun Lua n'est exécuté.
- **Le stockage est commandé par le compte, pas par le personnage** : rzu (`DB_StorageItem.cpp`) et
  NGemity (`CharacterDatabase.cpp:93-97`) écrivent tous deux
  `account_id = ? AND owner_id = 0 AND auction_id = 0 AND keeping_id = 0` sur la **même** table
  d'objets que l'inventaire (qui est `account_id = 0 AND owner_id = ?`). `ItemStorageEntity` du dépôt
  est l'entrepôt **des enchères** (`StorageType` 1..4/30..34), pas l'entrepôt de comptoir.
- **Réponses et refus** : aucun accusé de succès pour le 212 (NGemity ne fait que `Save(true)`) — ce
  sont les trames d'items (207/255/254) et les propriétés d'or qui informent le client ; les refus sont
  des `TS_SC_RESULT` portant l'id **212** et un `item_handle` recopié, avec les codes NGemity réels
  `NotActable` (5), `NotExist` (1), `AccessDenied` (6), `TooMuchMoney` (53), `NotEnoughMoney` (10).
  **Correction de prémisse** : NGemity déclare `NOT_ACTABLE_WHILE_USING_STORAGE` (51) et
  `TARGET_IS_USING_STORAGE` (88) mais ne les envoie **jamais** ; le client 7.3 sait les afficher, donc
  les employer reste possible, mais ce serait un choix du dépôt et non un portage.
- **Réserves vérifiables** (fiche §7) : la tolérance du client 7.3 à une charge après l'en-tête du
  `211` n'est pas établie (on envoie la forme stricte à 7 octets) ; la **capacité maximale** de
  l'entrepôt en 7.3 n'est établie par aucune source (le `10000` de rzu est ≥ 7.4, la mise en page du
  client n'est pas extraite) et **aucune borne serveur n'est inventée** ; le geste exact qui émet le
  `212` n'a pas été identifié ; le mode 4 (fermeture) doit être accepté sans erreur mais ne doit pas
  être le seul chemin de libération de l'état serveur ; la persistance de l'or d'entrepôt reste à
  trancher (NGemity détourne une ligne d'objet de code 0 — défaut visible à ne pas répliquer).
```

## 11. Implémentation livrée (dev)

Branche de la fiche, inchangée : `hermes/packet-socle-entrepot-personnage` (le dev n'en crée pas
d'autre). Commits : `e5e4e6f` (code), `f599fd3` (tests), `597f729` (garde de port), sur la fiche
`3c46ab4`.

**Chemin de la fiche.** La carte du PO la nomme `docs/packet-specs/socle-entrepot-personnage.md` ;
l'archéologue l'a créée sous `docs/packet-specs/211-212-storage.md`, le motif `<id>-<nom>` du dépôt.
Le dev ne renomme **pas** : renommer casserait la référence de la carte parente, et il n'y a qu'un
fichier. Le QA doit lire celui-ci.

**Ajout hors tableau (§6).** NGemity refuse de ranger un objet **porté** : `MoveInventoryToStorage`
commence par `IsErasable` (Player.cpp:3000-3002), dont la clause de port est
`GetItemWearType() != WEAR_NONE → false` (Player.cpp:3142-3143). Le socle porte cette clause
(`StorageRules.IsStorable`, garde appliquée par le dépôt pour le mode 0, **silencieuse** comme le
`return false` de NGemity) : sans elle, une pile portée quitterait l'inventaire en gardant son bonus
sur le personnage. Une ligne jamais portée vaut `ItemWearType.None`, la valeur que le dépôt écrit
lui-même (`CharacterService.cs:352`) et lit pour la wear info (`GameActions.cs:295`). Les autres
clauses d'`IsErasable` (propriétaire, carte de compétence liée, carte d'invocation liée) n'ont
**aucun état** dans le dépôt : non portées, hors périmètre.

### 11.1 Périmètre livré

| Point de §5.3 | État | Emplacement |
| --- | --- | --- |
| 1 — enum + dispatch, ensemble | livré | `GamePackets.cs:39-40`, bras `GameClient.cs:726-730`, handler `GameClient.cs:465-482` |
| 2 — lecture | livré | `StorageRequest` `GameActionPackets.cs:56`, `TryReadStorage` `:58-73` |
| 3 — déclencheur `open_storage()` | livré | `NpcDialogService.cs:19,117-126` (avant la recherche de page, comme `RunTeleport`) |
| 4 — conteneur, voie (a) | livré | `StorageRules.Own/IsStorageRow`, `StorageRepository` |
| 5 — or d'entrepôt | **partiel assumé** | `storage_gold` annoncé à l'ouverture, transferts **refusés** — §11.3 |
| 6 — réponses | livré pour 0/1/4, refus explicite pour 2/3 | `StorageService.cs` |
| 7 — validation + `StorageSecurityCheck` | livré tel quel | `StorageService.HandleAsync`, `ConnectionInfo.cs:119` |
| §7.4 — libération de l'état | livré | mode 4, **et** `ConnectionInfo.ClearCharacterSession` (`ConnectionInfo.cs:171`) |
| §7.2 — capacité | aucune borne, conforme | aucune vérification de nombre d'emplacements |

### 11.2 Fichiers

| Fichier | Contenu |
| --- | --- |
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_SC_OPEN_STORAGE = 211`, `TM_CS_STORAGE = 212`, déclarés **avec les ids d'objets** (l. 39-40) et non après `TM_CS_VERSION`, où les branches sœurs ancrent leurs membres. |
| `Game/Network/Packets/Game/GameStoragePackets.cs` (nouveau, 47 l.) | `BuildOpenStorage()` : en-tête seul de 7 octets, id 211, checksum sur les six premiers octets. Fichier propre plutôt que `GameCharacterPackets.cs` : ce dernier est la zone de collision de la famille d'objets, et la trame n'a aucun écrivain d'item. Les deux helpers d'en-tête y sont les mêmes six octets, dupliqués pour ne pas élargir le fichier partagé. |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `StorageRequest(uint ItemHandle, byte Mode, long Count)` et `TryReadStorage` (longueur minimale 20, handle `uint32` à 7, mode à 11, count `int64` à 12). Le mode est lu brut et validé par le service, comme les voisins. |
| `Game/Network/Clients/GameClient.cs` | Bras `TM_CS_STORAGE` dans la chaîne de `if` (l. 726-730), à côté des voisins d'objets 218/219 et **avant** le `switch` final ; handler `HandleStorageAsync` (l. 465-482) qui répond `InvalidArgument` sur une trame courte. |
| `Game/Services/NpcDialogService.cs` | Constante `StorageFunction` (l. 19), dépendance `IStorageService` (l. 24-30), branche `open_storage()` (l. 117-126) : ferme le lien de dialogue puis appelle l'ouverture, sans attendre le dépôt (le service est lancé par `_ =`, l'ouverture est asynchrone et la boucle de réception ne doit pas bloquer dessus). |
| `Game/Network/Clients/ConnectionInfo.cs` | `StorageSecurityCheck` (l. 119) devient l'état « entrepôt ouvert » et est remis à `false` avec la session de personnage (l. 171). |
| `Game/Services/StorageRules.cs` (nouveau, 152 l.) | Règles pures : modes, `MoveCount` (bornage), `NextFreeIndex`, `IsInventoryRow`/`IsStorageRow`, `IsStorable`, `Own`, `Divide`. |
| `Game/Services/StorageMoveResult.cs` (nouveau) | `StorageMoveOutcome` (`Moved`, `Split`, `UnknownCharacter`, `UnknownHandle`, `AccessDenied`, `Ignored`) et le résultat que le dépôt renvoie au service. |
| `Game/Services/StorageService.cs` (nouveau, 199 l.) | `OpenAsync` (211 → 207 → propriété) et `HandleAsync` (les six modes). Un sémaphore sérialise l'accès au contexte du dépôt, qui est un singleton. |
| `Game/DataAccess/Repositories/StorageRepository.cs` + interface (nouveaux) | La requête de compte des deux références, la résolution du propriétaire, le déplacement d'une pile et la création de la ligne d'un partage. |
| `Game/Network/NetworkService.cs`, `DevConsole/Program.cs` | Câblage : `StorageService` (l. 32,55,70) et les deux enregistrements DI (l. 214 et 239). |

### 11.3 Le conteneur, et l'or d'entrepôt

**Conteneur — voie (a) de §5.3 point 4, retenue.** L'entrepôt est la **même table d'objets**,
discriminée par le propriétaire : côté inventaire `CharacterId = <personnage>` et `AccountId` vide,
côté entrepôt `AccountId = <compte>` et `CharacterId` vide (`StorageRules.Own`). La requête d'entrepôt
écrit les quatre conditions des deux références — `AccountId = ? AND CharacterId IS NULL AND AuctionId
IS NULL AND StorageId IS NULL` — les deux dernières pour que les lignes d'`ItemStorageEntity`
(entrepôt **des enchères**) ne tombent pas dans la liste du comptoir. Aucune migration, aucune
nouvelle table : les deux colonnes existaient déjà. Le slot de destination est le plus petit index
libre de la liste d'arrivée (`StorageRules.NextFreeIndex`), l'esprit de l'`IssueNewIndex()` de NGemity
(Player.cpp:3006) ; aucune **capacité** n'est vérifiée (§7.2 : aucune source 7.3, aucune borne
inventée). Un partage crée une ligne neuve (`StorageRules.Divide`) qui recopie les colonnes qui
définissent l'objet et **aucun** lien de la ligne d'origine (enchère, keeping, équipement
d'invocation) ; l'`Idx` est réattribué par la liste d'arrivée.

**Or d'entrepôt — partiel assumé.** La persistance reste non tranchée (§7.5) : Ni `ItemEntity`, ni
`CharacterEntity`, ni `AccountEntity` ne portent de colonne d'or de compte, et le détour de NGemity
(ligne d'objet de code 0) est exclu par §5.3 point 5. Le socle applique donc la partie établie —
`BuildProperty(handle, "storage_gold", 0)` à l'ouverture, la propriété que le client 7.3 connaît — et
**refuse les transferts d'or** (modes 2 et 3) par `NotActable`, au lieu d'encaisser un or qu'aucune
ligne ne rendrait. Conséquence assumée : le tableau de §5.3 point 7 reste appliqué (`count <= 0` →
`NotEnoughMoney` avant tout, y compris pour 2/3), mais la ligne « or demandé > solde de la source »
recouvre en pratique **tout** transfert d'or tant que `storage_gold` vaut 0. Ce choix est en
section `A VERIFIER PAR KILLIAN`, point 1.

### 11.4 Réponses émises, mode par mode

| Mode | Réponse |
| --- | --- |
| ouverture (`open_storage()`) | `211` (7 octets) → `207` (une trame même si la liste est vide) → propriété `storage_gold` = 0. L'état de session passe à « ouvert » **avant** l'envoi. |
| 0 / 1, pile entière | `254` `TM_SC_DESTROY_ITEM` sur le handle (la pile quitte sa liste) puis `207` sur la ligne ré-appropriée. |
| 0 / 1, partiel | `255` `TM_SC_UPDATE_ITEM_COUNT` sur la source (handle conservé, quantité restante) puis `207` sur la ligne créée. |
| 0 / 1, `count > pile` | bornage à la pile (`Math.Min`, précédent du dépôt) : les trames notifient la quantité réelle. |
| 2 / 3 | refus `NotActable` (5), `value` = `item_handle` recopié (§11.3). |
| 4 | aucun envoi ; l'état repasse à « fermé ». |
| refus de cadre | trame < 20 octets → `TS_SC_RESULT(212, InvalidArgument)`, trame tout de même consommée. |

### 11.5 Ce qui n'est pas livré

- Les modes **2/3** en écriture (§11.3) : refus explicite, jamais un succès muet.
- Les clauses d'`IsErasable` sans état dans le dépôt (propriétaire, cible de carte de compétence,
  carte d'invocation liée) : seule la clause de port est portée.
- Les codes **51/88** de §7.8 : le socle emploie `NotActable` (5), comme §5.3 point 6 le prescrit.

### 11.6 Tests et mesures

- `Tests/Game/StoragePacketsTests.cs` (nouveau) — layout : **20 octets** pour le 212 (en-tête 7,
  `item_handle` `uint32` **à l'offset 7**, `mode` **à l'offset 11**, `count` `int64` **à l'offset 12**),
  lecture petit-boutiste prouvée par des octets posés à la main (`04 03 02 01` → `0x01020304`),
  `count` signé et 64 bits (`-2`, `3 000 000 000`), trames de 0/7/11/19 octets refusées, convention
  « au moins 20 » documentée pour une trame plus longue, **211 = 7 octets en-tête seul** (aucun octet
  de charge), bascule 1211/1212 non déclarée, et l'ids ne sont pas définis → la boucle s'arrête.
  Le **dispatch est prouvé pour de vrai** (modèle de la branche `hermes/packet-57-check-illegal-user`) :
  une trame 212 complète atteint `IStorageService.HandleAsync` avec `(0x80000123, 1, 250)`, une trame
  tronquée reçoit `InvalidArgument` sur l'id 212, une trame 212 coalescée avec un keepalive ne
  désynchronise pas la boucle.
- `Tests/Game/StorageRulesTests.cs` (nouveau) — modes, borne, slot libre, partage du compte et du
  personnage, exclusion enchère/keeping, objet porté, `Divide` (colonnes recopiées, sockets copiés et
  non partagés, aucun lien de la source).
- `Tests/Game/StorageServiceTests.cs` (nouveau) — les six modes, les refus, les trames d'un
  déplacement entier et d'un partage, `DBError` sur panne du dépôt, ouverture (211/207/propriété,
  liste vide, session sans personnage).
- `Tests/Game/StorageTestHarness.cs` (nouveau) — la connexion en mémoire du modèle 57 ;
  `Client.ConnectionInfo` est `internal`, le socle de test le lit par réflexion plutôt que d'élargir
  la surface de production.

**Invariant enum/dispatch, mesuré après livraison** : `GamePackets` compte **85** membres ; **35** n'ont
pas de bras dans `GameClient.cs`/`GameActions.cs`, et ce sont **exactement** les 34 trames descendantes
`TM_SC_*` plus `TM_EQUIP_SUMMON` — aucune trame `TM_CS_*` n'est sans bras, donc `TM_CS_STORAGE` ne peut
pas atteindre le `_ => throw` final.

### 11.7 Commandes et codes de sortie

| Commande | Résultat |
| --- | --- |
| `dotnet build Navislamia.sln -c Debug` | code **0** |
| `dotnet test Tests/Tests.csproj` | code **0**, **511 réussis / 511**, 0 échec (448/448 avant ce lot) |
| `git log --oneline origin/master..master` | **vide** |
| `git log --oneline origin/master..HEAD` | `597f729`, `f599fd3`, `e5e4e6f`, `3c46ab4` |

## A VERIFIER PAR KILLIAN

Rien de ce socle n'a été vérifié contre un client 7.3 : la fenêtre, les listes et les refus ci-dessous
sont des lectures de rzu, de NGemity et du dumping client, pas une observation de jeu.

| # | À vérifier | Pourquoi c'est ouvert | Ce qui l'établirait |
| --- | --- | --- | --- |
| 1 | **Or d'entrepôt (§7.5)** : le socle annonce `storage_gold = 0` et refuse les modes 2/3. | Aucune colonne d'or de compte n'existe ; le détour NGemity (ligne d'objet de code 0) est exclu par §5.3 point 5. Ajouter une colonne + migration est un choix de schéma, pas un portage. | Décider : colonne sur le compte + migration, ou « pas d'or d'entrepôt en 7.3 ». |
| 2 | **Capacité (§7.2)** : aucune borne serveur. | Le `10000` de rzu est ≥ 7.4 ; la mise en page du client n'est pas extraite. | Une capture du comptoir plein, ou l'extraction des archives d'interface. |
| 3 | **Le `211` à 7 octets ouvre-t-il la fenêtre (§7.1) ?** | La tolérance du client à une charge surnuméraire n'est établie par personne ; le socle envoie la forme stricte. | Ouvrir un comptoir avec un client 7.3 devant un PNJ `NPC_Storage_*`. |
| 4 | **Les trames d'un déplacement (§7.7)** : `254`+`207` (pile entière), `255`+`207` (partiel). | Rien n'établit laquelle des trois trames le client attend entre deux listes. | Déplacer une pile, puis une partie d'une pile, et regarder les deux fenêtres. |
| 5 | **Le mode 4 est-il émis (§7.4) ?** | Le socle l'accepte sans réponse et libère l'état ; l'état est **aussi** libéré à la déconnexion, il ne dépend donc pas du mode 4. | Fermer la fenêtre et vérifier qu'aucun `212` n'arrive en erreur dans les logs. |
| 6 | **Objet porté (hors tableau §6)** : le socle ne range pas un objet porté, **sans rien répondre**. | Portage d'`IsErasable` (Player.cpp:3142) ; une garde silencieuse est un choix, NGemity fait de même mais le client ne dit rien. | Vérifier que retirer un objet puis le ranger fonctionne, et qu'un objet porté ne bouge pas. |
| 7 | **Déclencheur** : 20 PNJ `NPC_Storage_*` du catalogue 7.3 portent `open_storage()`. | Le dépôt l'advertise déjà (`NpcDialogService` le journalisait en `Debug`) sans savoir l'exécuter ; le socle l'exécute désormais. | Parler à un comptoir et voir la fenêtre s'ouvrir. |
| 8 | **Le geste exact qui émet le `212` (§7.3)**, et les codes 51/88 non employés (§7.8). | Ni NGemity ni rzu ne renvoient 51/88 ; le socle emploie `NotActable` (5), comme prescrit. | Le premier essai en jeu le dira. |
| 9 | **Chemin de la fiche** : la carte cite `docs/packet-specs/socle-entrepot-personnage.md`, le fichier réel est `docs/packet-specs/211-212-storage.md`. | Le dev ne renomme pas une fiche livrée par l'archéologue (la carte parente la référence sous ce nom). | Rien à trancher : à savoir pour le QA et le PO. |
