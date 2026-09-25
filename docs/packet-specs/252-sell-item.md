# 252 — `TM_CS_SELL_ITEM` (vente d'un objet à un marchand)

Fiche d'archéologie du paquet **252**, client → serveur, trame fixe de **13 octets**. Elle est
établie sur `master` `b56967a07430422add88e0e5cdf292b41b18f6c6`, avec `reference/rzu`
`87c1e83bf84efe29bb6405e8e6da80349712f3fa` et `reference/ngemity`
`38ceb2c6065fabf6ff4ba71d52f955f362c6c839`. Aucun fichier de code n'est touché par ce lot : la
fiche est le livrable, l'implémentation appartient au lot `navis-dev` qui poursuit la même branche.

Carte Trello : [TM_CS_SELL_ITEM (252)](https://trello.com/c/RamDNqcl) — identifiant de suivi
`navislamia:packet:252`.

Résumé exécutable : **13 octets** = en-tête de 7 octets du dépôt (`Length` 0-3, `ID` 4-5,
`Checksum` 6) + `handle` `uint32` à l'offset 7 + `sell_count` `uint16` à l'offset 11. Réponse : un
`TM_SC_RESULT` (0) toujours, puis en cas de succès un écho `TM_SC_NPC_TRADE_INFO` (240)
`is_sell = 1`. Enseignements principaux : l'id 7.3 est **252** (jamais 1252), `sell_count` fait
**2 octets** (jamais 8), et le prix de vente (`GetItemSellPrice`) n'est **pas** portable tel quel —
il tronque ses incréments à chaque tour de boucle et son barème contient un cas mort (§5.3, §7).

## 1. Identité

| Champ | Valeur | Source |
| --- | --- | --- |
| Id décimal | **252** (`0xfc`) | `op_codes.md:79` (`[252] = "TM_CS_SELL_ITEM"`) |
| Nom `rzu` | `TS_CS_SELL_ITEM` | `reference/rzu/librzu/src/packets/GameClient/TS_CS_SELL_ITEM.h:16` |
| Nom `NGemity` | `TS_CS_SELL_ITEM`, déclaré `252` | `reference/ngemity/shared/Server/ClientPackets.h:92` |
| Nom du dépôt | `TM_CS_SELL_ITEM` (préfixe `TM_` du dépôt pour les mêmes ids) | convention `Game/Network/Packets/Enums/GamePackets.cs:34, 46-47` |
| Sens | **client → serveur** uniquement | `SessionPacketOrigin::Client` (`TS_CS_SELL_ITEM.h:16`) |
| Taille sur le fil | **13 octets**, fixe | §3 |
| Charge utile | 6 octets (`uint32` + `uint16`) | §3 |
| Réponses couplées | `TM_SC_RESULT` (0) **toujours** ; `TM_SC_NPC_TRADE_INFO` (240) en cas de succès ; `TM_SC_GOLD_UPDATE` (1001) via la mise à jour d'or | §5.1 |
| État dans le dépôt (`b56967a`) | **id absent** de `GamePackets` : aucun membre `TM_CS_SELL_ITEM`, ni 252 ni 1252 | `GamePackets.cs` (231 lignes, aucune occurrence de `SELL_ITEM` ; `grep -rn "SELL_ITEM" --include=*.cs .` → seule occurrence : un commentaire de `GameTradePackets.cs:22`) |

Conséquence de cette absence, mesurée : `DefinedPackets[252]` vaut `false`, donc une trame 252 reçue
aujourd'hui est **journalisée puis ignorée** — `GameClient.cs:1269-1273`
(`if (!DefinedPackets[header.ID]) { _logger.Debug("Undefined packet ID: …"); continue; }`). Elle
n'atteint jamais le `switch` final (`GameClient.cs:1854-1866`). C'est le point de départ du lot :
déclarer 252 **et** lui donner un bras avant ce `switch`, faute de quoi l'id rejoindrait le
`throw new Exception($"Unknown Packet Type {header.ID}")` (`GameClient.cs:1865`).

## 2. Ce que le joueur fait pour que le client l'envoie

1. Le joueur ouvre la fenêtre de commerce chez un marchand : contact du PNJ puis déclencheur
   `open_market(` → `TM_SC_MARKET` (250) ouvre la fenêtre. Chaîne complète, sources et réserves :
   `docs/packet-specs/socle-marche-npc.md:35-62` (fiche du socle, sur `master`).
2. Il **dépose un ou plusieurs objets dans le panier de vente** de la fenêtre, puis valide la
   vente. Le client envoie alors **une trame 252 par objet** déposé (§3, boucle de l'émetteur).

Ce qui est établi par **lecture statique** de `reference/client73/SFrame.exe` (9 841 664 octets,
sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` ; `reference/client73`
n'est pas un dépôt git, il n'y a donc pas de SHA de commit à épingler, seulement l'empreinte du
binaire) :

- **l'émetteur de 252 est la fonction `0x48f410`** (prologue `55 8b ec 83 ec 10` aux adresses
  `0x48f410`-`0x48f413`) ; elle **boucle sur un vecteur d'éléments de 12 octets** — début en
  `[ebx+0x13]` (`mov esi,[ebx+0x13]`, `0x48f41b`), fin en `[ebx+0x17]`
  (`cmp esi,[ebx+0x17]`, `0x48f421`), avance de `add esi,0xc` (`0x48f487`) — et **émet une trame
  252 par élément** : le client découpe donc une vente multi-objets en autant de 252, jamais en un
  seul paquet. Seuls les 6 premiers octets de chaque élément sont lus (`mov eax,[esi]` en
  `0x48f461`, `mov cx,word [esi+0x4]` en `0x48f466`) ;
- ce vecteur 12 octets est **le même emplacement que celui du bras d'achat** : l'émetteur jumeau de
  251 (`0x48f380`, `mov eax,0xfb` en `0x48f3a4`, longueur `0xd` en `0x48f3ad`) lit lui aussi
  `[esi]` et `word [esi+4]` (`0x48f3da`, `0x48f3dd`). La **disposition** est donc commune aux deux
  messages ; ce que `[esi]` **contient** diffère (code d'objet à l'achat — `rzu`
  `TS_CS_BUY_ITEM.h:8`, `item_code` ; handle à la vente — `TS_CS_SELL_ITEM.h:6`, `handle`) ;
- l'appel est unique et vient du bras de la fonction de dispatch des messages d'UI `0x49cd00` :
  table de commutation en `0x49e20c` (`sub eax,0x403`, `cmp eax,0xde`, `movzx eax,byte [eax+0x49ea50]`,
  `jmp [eax*4+0x49e98c]`), et l'entrée **`0x418` (1048)** donne l'octet `0x0f` donc la cible
  `0x49e301`, d'où `call 0x48f410` en `0x49e304` (relevé par lecture des deux tables, refait ici
  indépendamment de la fiche sœur 251) ;
- l'envoi passe par la connexion : `[edi+0xb8]` = objet de connexion, `[edx+0xc4]` = envoi
  indirect (`0x48f473`-`0x48f485`) ;
- la fenêtre concernée est celle du commerce : chaînes `SUIShopWnd::RefreshItems` (fichier
  `0x62e9c4`), `SUIShopKartWnd::IMSG_UI_SEND_DATA` (`0x62ea4c`), `SUIShopKartWnd::ProcMsgAtStatic`
  (`0x62ea90`), `button_sell` (`0x62e9f8`), `button_tokart` (`0x62ea04`), `window_business_shop.nui`
  (`0x643584`), `Create : SUIShopKartWnd` (`0x64890c`). La table des noms contient `TM_CS_SELL_ITEM`
  au fichier `0x65206c`, à côté de `TM_CS_BUY_ITEM` (`0x65207c`) et `TM_SC_MARKET` (`0x65208c`).

**`NON ÉTABLI`** : le **nom de la commande d'UI** (ou du bouton `.nui`) qui porte l'id interne
`1048`. La chaîne `SUIShopKartWnd::IMSG_UI_SEND_DATA` est voisine dans le pool de chaînes, mais
aucune table ne lie statiquement `1048` à cette commande par la seule lecture : c'est la même
réserve, honnête, que pour le bras `1046` de la jumelle 251 (fiche `251-buy-item.md` §2 et §7 q.9).

## 3. Structure sur le fil — **13 octets**

| Offset | Type | Nom | Valeur observée | Source |
| ---: | --- | --- | --- | --- |
| 0 | `uint32` | taille totale | `13` | client : `mov dword [ebp-0x10],0xd` en `0x48f43d` (la trame locale commence en `[ebp-0x10]`, zéro-remplie sur 13 octets en `0x48f426`-`0x48f431`) ; forme du dépôt : `GameCharacterPackets.cs:383-389` (`CreatePacket`, longueur totale en 0) et `GameTradePackets.cs:88` |
| 4 | `uint16` | id | **`252`** (`0xfc`) | client : `mov eax,0xfc` en `0x48f434` puis `mov word [ebp-0xc],ax` en `0x48f439` (`[ebp-0xc]` = trame+4) ; rzu `TS_CS_SELL_ITEM.h:13` (`X(252, version < EPIC_9_6_3)`) ; `op_codes.md:79` |
| 6 | `uint8` | somme de contrôle | somme des octets 0 à 5 | client : boucle `xor cl,cl` `0x48f447`, `add cl,[eax]` / `inc eax` `0x48f450`-`0x48f452`, borne `lea edx,[ebp-0xa]` (= trame+6) `0x48f453`, `cmp eax,edx` / `jne` `0x48f456`-`0x48f458`, écriture `mov byte [ebp-0xa],cl` en `0x48f463` ; dépôt : `GameCharacterPackets.cs:391-400` (`WriteChecksum`, boucle sur 6 octets) et `GameClient.cs:174-176` |
| 7 | `uint32` | `handle` | handle d'inventaire de l'objet vendu | client : valeur lue en `[esi]` (`0x48f461`), écrite `mov dword [ebp-0x9],eax` en `0x48f46a` (`[ebp-0x9]` = trame+7) ; rzu `TS_CS_SELL_ITEM.h:6` (`ar_handle_t`, soit `uint32` — `librzu/src/lib/Packet/GameTypes.h:40` : `struct ar_handle_t : strong_typedef<ar_handle_t, uint32_t>`) ; NGemity `shared/Server/Packets/GameClient/TS_CS_SELL_ITEM.h:7` (`uint32_t handle`) |
| 11 | `uint16` | `sell_count` | quantité vendue | client : valeur lue `mov cx,word [esi+0x4]` (`0x48f466`), écrite `mov word [ebp-0x5],cx` en `0x48f46d` (`[ebp-0x5]` = trame+11) ; rzu `:9` (`uint16_t` pour `version >= EPIC_4_1 && version < EPIC_8_2`) |
| 13 | — | fin de trame | — | la trame locale est zéro-remplie sur **13** octets : `0x48f426`-`0x48f428` (`mov dword [ebp-0x10],eax`), `0x48f42b` (`[ebp-0xc]`), `0x48f42e` (`[ebp-0x8]`) puis `0x48f431` (`mov byte [ebp-0x4],al`), soit 4+4+4+1 |

**L'en-tête de 7 octets du dépôt est bien celui du client**, et il est établi ici pour 252 comme
pour les autres paquets clients du dépôt : le client construit une trame de 13 octets dont il écrit
la longueur totale en 0 (`0xd`), l'id en 4 (`0xfc`) et la somme de contrôle des 6 premiers octets
en 6 — octet pour octet la disposition de `Header` (`Game/Network/Packets/Header.cs:6-11` :
`Length` 0-3, `ID` 4-5, `Checksum` 6), de `CreatePacket` et de `WriteChecksum`. La taille utile est
de **6 octets**, sans préfixe de longueur ni remplissage : les champs `_(simple)` de `rzu`
s'ajoutent sans en-tête de champ.

Le lecteur à écrire est donc trivial et de la même famille que `TryReadDropItem`
(`GameActionPackets.cs:155-176`, 7 + 8 octets) et `TryReadEraseItem` (`:191-217`, qui lit déjà un
handle `uint32` à l'offset 0 d'un enregistrement) : longueur **exacte** `HeaderSize + 6`, `handle`
en `ReadUInt32LittleEndian(packet.Slice(7, 4))`, `sell_count` en `ReadUInt16LittleEndian(packet.Slice(11, 2))`.
Refuser une trame de longueur différente est la discipline du dépôt (voir §9).

### 3.1 Trames à respecter en réponse

| Paquet | Taille | Disposition | Source |
| --- | ---: | --- | --- |
| `TM_SC_RESULT` (0) | **15** | `request_msg_id` `uint16` @7, `result` `uint16` @9, `value` `int32` @11 | rzu `TS_SC_RESULT.h:8-10` ; `TS_SC_RESULT.cs:6-17` ; `GameClient.cs:68-72` ; `Packet.cs:31` (`_headerLen = 7`) et `:34` (tampon = en-tête + charge utile) |
| `TM_SC_NPC_TRADE_INFO` (240) | **36** | `is_sell` `int8` @7, `code` `int32` @8, `count` `int64` @12, `price` `int64` @20, `huntaholic_point` `int32` @28, `target` `uint32` @32 — **sans `arena_point`** | rzu `TS_SC_NPC_TRADE_INFO.h:8-14` ; décision et relevé client du socle : `docs/packet-specs/socle-marche-npc.md:70-92`, `:133-135` ; constructeur livré : `GameTradePackets.cs:83-97` (`NpcTradeInfoSize = 36`, `:39`) |
| `TM_SC_GOLD_UPDATE` (1001) | **19** | `gold` `uint64` @7, `chaos` `uint32` @15 | rzu `TS_SC_GOLD_UPDATE.h:8-11` (`chaos` gaté `version > EPIC_4_1_1`) ; `GameCharacterPackets.cs:239-246` (`BuildGoldUpdate`, `HeaderSize + 12`) |

## 4. Gating de version — tranché pour 7.3

7.3 = `EPIC_7_3` = `0x070300` (`rzu`, `librzu/src/lib/Packet/PacketEpics.h:59`) ; `EPIC_4_1` =
`0x040100` (`:50`), `EPIC_4_1_1` = `0x040101` (`:51`), `EPIC_5_2` = `0x050200` (`:53`),
`EPIC_8_1` = `0x080100` (`:61`), `EPIC_8_2` = `0x080200` (`:63`), `EPIC_9_6_3` = `0x090603` (`:96`).

| Élément | Gating `rzu` | Décision pour 7.3 | Source |
| --- | --- | --- | --- |
| Id du paquet | `X(252, version < EPIC_9_6_3)` / `X(1252, version >= EPIC_9_6_3)` | **252** : `0x070300 < 0x090603`. Déclarer 1252 serait faux. | `TS_CS_SELL_ITEM.h:13-14` ; corroboré par le client (`mov eax,0xfc`, `0x48f434`) |
| `handle` | aucun gating ; `ar_handle_t` = `strong_typedef<…, uint32_t>` | **`uint32`**, 4 octets | `TS_CS_SELL_ITEM.h:6` ; `GameTypes.h:40` |
| `sell_count` | `_(def) int64_t` puis `int64_t` si `>= EPIC_8_2`, `uint16_t` si `>= EPIC_4_1 && < EPIC_8_2`, `uint8_t` si `< EPIC_4_1` | **`uint16`**, 2 octets : `0x040100 <= 0x070300 < 0x080200`. La variante `int64` (8 octets) ferait une trame de 19 octets et désalignerait le client. | `:7-10` ; corroboré par le client (`mov cx,word [esi+0x4]`, `0x48f466`) |
| Id de l'écho | `X(240, version < EPIC_9_6_3)` / `X(1240, …)` | **240** | `TS_SC_NPC_TRADE_INFO.h:17-18` ; socle `socle-marche-npc.md:133` |
| `huntaholic_point` (240) | `version >= EPIC_5_2` | **présent, émis** (4 octets à l'offset 28) | `TS_SC_NPC_TRADE_INFO.h:12` ; socle `:135` |
| `arena_point` (240) | `version >= EPIC_8_1` | **absent** | `:13` ; socle `:134` |
| `gold` (1001) | `uint64` si `>= EPIC_4_1_1`, sinon `uint32` | **`uint64`** : `0x070300 > 0x040101` | `TS_SC_GOLD_UPDATE.h:8-10` |
| `chaos` (1001) | `version > EPIC_4_1_1` | **présent** → 19 octets | `:11` ; déjà écrit ainsi par le dépôt (`GameCharacterPackets.cs:239-246`) |
| Id de `TM_SC_RESULT` | `X(0, version < EPIC_9_6_3)` / `X(1000, …)` | **0** | `TS_SC_RESULT.h:13-14` |

**Aucun champ de 252 n'a de gating à trancher qui change sa position** : `handle` et `sell_count`
sont les seuls champs, et la seule variante vivante en 7.3 est `uint16` pour le second. Le seul
piège de cette fiche est précisément là : recopier NGemity (`#define EPIC EPIC_4_1_1`, frontière à
`EPIC_9_4`, `shared/Server/Packets/GameClient/TS_CS_SELL_ITEM.h:9-11`) donne bien `uint16` ici, mais
pour la mauvaise raison — la frontière `rzu` est `EPIC_8_2`, celle de NGemity `EPIC_9_4`
(§6, écart 1).

## 5. Traitement attendu

### 5.1 Ce que fait la référence (`Chihiro`)

`WorldSession::onSellItem`, `reference/ngemity/Chihiro/src/Network/GameNetwork/WorldSession.cpp:1021-1072`,
déclaré `WorldSession.h:110`, enregistré pour `STATUS_AUTHED` en `WorldSession.cpp:123`, lu avec
`handle` (`uint32`) et `sell_count` (`uint16`) d'après
`shared/Server/Packets/GameClient/TS_CS_SELL_ITEM.h:6-13`. Étapes, dans l'ordre du code :

1. `:1023-1024` — `m_pPlayer` nul → retour silencieux, aucune réponse.
2. `:1026-1030` — `FindItemByHandle(handle)` (`Player.cpp:970-973`, lecture par handle dans
   l'inventaire) ; si l'objet est introuvable, sans template d'objet, d'un propriétaire différent
   (`GetOwnerHandle() != GetHandle()`) ou hors inventaire (`!IsInInventory()`) →
   `TS_SC_RESULT` code **`TS_RESULT_NOT_EXIST` (1)**, valeur **0**.
3. `:1031-1034` — `sell_count == 0` → `TS_SC_RESULT` code **`TS_RESULT_UNKNOWN` (7)**, valeur 0.
   *(C'est le garde de la vente : le socle cite `:1031` pour 252, `:735` étant celui de l'achat —
   `socle-marche-npc.md:231-232`.)*
4. `:1035` — le contrôle « vendable » est **commenté** dans la référence :
   `// if(!m_pPlayer.IsSelllable) @todo`. Le refus correspondant est reporté plus bas, à `:1042`.
5. `:1037-1038` — prix **unitaire** :
   `GameContent::GetItemSellPrice(item->GetItemTemplate()->price, item->GetItemTemplate()->rank, item->GetItemInstance().GetLevel(), item->GetItemInstance().GetCode() >= 602700 && item->GetItemInstance().GetCode() <= 602799)`
   (algorithme en §5.3).
6. `:1039` — `nResultCount = count - sell_count` (la pile restante).
7. `:1040` — `nEnhanceLevel = level + 100 * enhance` : **calcul mort**, la variable n'est plus
   jamais lue (le trait `enhance` n'entre donc pas dans le prix).
8. `:1042-1045` — `!IsSellable(item)` **ou** `nResultCount < 0` (vendre plus que la pile) →
   `TS_RESULT_NOT_EXIST` (1), valeur = **code d'objet** (`GetItemInstance().GetCode()`).
9. `:1046-1049` — `GetGold() + sell_count * nPrice > MAX_GOLD_FOR_INVENTORY` →
   **`TS_RESULT_TOO_MUCH_MONEY` (53)**, valeur = code d'objet.
10. `:1050-1053` — même somme `< 0` → **`TS_RESULT_NOT_ACTABLE` (5)**, valeur = code d'objet.
11. `:1054-1058` — capture du code d'objet, puis `EraseItem(item, sell_count)`
    (`Player.cpp:1308-1311` → `Inventory::Erase`, `Inventory.h/.cpp:89-108` : décrémente la pile ou
    supprime l'objet quand la quantité est atteinte) ; échec → `TS_RESULT_NOT_ACTABLE` (5),
    valeur = **handle**.
12. `:1059-1062` — `ChangeGold(gold + sell_count * nPrice)` ; code de retour non nul →
    `TS_RESULT_TOO_MUCH_MONEY` (53), valeur = code d'objet. `ChangeGold`
    (`Player.cpp:1132-1143`) écrit l'or, **envoie lui-même la mise à jour d'or et de chaos**
    (`SendGoldChaosMessage()`, `:1140`) et ne peut rendre que 53 (plafond `MAX_GOLD_FOR_INVENTORY`,
    `ItemTemplate.hpp:4` : `constexpr int64_t MAX_GOLD_FOR_INVENTORY = 100000000000`) ou 50
    (`TS_RESULT_TOO_CHEAP`, or négatif).
13. `:1064` — **succès** : `TS_SC_RESULT`, code `TS_RESULT_SUCCESS` (0), valeur = **handle**.
14. `:1065-1071` — puis l'écho `TS_SC_NPC_TRADE_INFO` (240) : `is_sell = true`, `price =
    sell_count * nPrice`, `target = GetLastContactLong("npc")` (dernier PNJ contacté,
    `Player.cpp:1069-1073`, posé par `SetLastContact` au contact du PNJ, `WorldSession.cpp:702`).
    `huntaholic_point` reste à `0` (initialisation `{}`).

**Deux défauts de la référence, mesurés ligne à ligne** :

- `:1067-1068` — `tradePct.code = code;` puis, à la ligne suivante,
  `tradePct.code = pRecvPct->sell_count;` : le code d'objet capturé à `:1054` est **écrasé par la
  quantité**, et le champ `count` de la trame **n'est jamais renseigné** (il reste `0`). Le socle l'a
  déjà consigné (`socle-marche-npc.md:245`) et le classe « à ne pas porter » : la fiche retient
  `code` = code d'objet, `count` = `sell_count` (§6, écart 3 — l'arbitrage reste demandé, car c'est
  le contrat de l'écho).
- `:1040` — calcul mort de `nEnhanceLevel` (§5.1 point 7) : le recopier donnerait une variable non
  lue, sans effet observable.

### 5.2 Ce que `Navislamia` doit répondre

| Requête | Réponse | Détail |
| --- | --- | --- |
| `TM_CS_SELL_ITEM` (252), objet inconnu / pas au joueur / hors inventaire | `TS_SC_RESULT` (0), `request_msg_id` **252**, `result` **1** (`NotExist`), `value` **0** | NGemity `:1026-1030` ; `ResultCode.cs:8` |
| 252, `sell_count == 0` | `TS_SC_RESULT` 252, `result` **7** (`Unknown`), `value` 0 | `:1031-1034` ; `ResultCode.cs:14` |
| 252, objet non vendable ou `sell_count > Amount` | `TS_SC_RESULT` 252, `result` **1**, `value` = **code d'objet** (`ItemResourceId`) | `:1042-1045` |
| 252, plafond d'or dépassé | `TS_SC_RESULT` 252, `result` **53**, `value` = code d'objet | `:1046-1049` ; **le plafond n'existe pas dans le dépôt** (§5.4, `A VERIFIER` 1) |
| 252, échec du retrait | `TS_SC_RESULT` 252, `result` **5** (`NotActable`), `value` = **handle** | `:1054-1058` |
| 252, succès | `TS_SC_RESULT` 252, `result` **0**, `value` = **handle**, **puis** `TM_SC_NPC_TRADE_INFO` (240) `is_sell = 1`, `count = sell_count`, `price = sell_count × prix unitaire`, `target` = handle du marchand, `huntaholic_point = 0` | `:1064-1071` ; trame 240 : §3.1, constructeur `GameTradePackets.cs:83-97` |
| 252, or modifié | `TM_SC_GOLD_UPDATE` (1001), 19 octets, émis **par** la mise à jour d'or (ordre de la référence : le changement d'or précède l'écho 240, `:1059` puis `:1064-1071`) | `:1059` ; `Player.cpp:1140` ; `GameCharacterPackets.cs:239-246` |
| 252, trame de longueur ≠ 13 | `TS_SC_RESULT` 252, `result` **28** (`InvalidArgument`), `value` 0 — convention du dépôt pour une trame illisible | patron : `GameClient.cs:803`, `:821`, `:990` ; `ResultCode.cs:37` |

Le contrat de forme est donc : **une réponse `TS_SC_RESULT` par trame 252 reçue** (jamais de
silence), puis les trames de succès. Comme le client émet **une 252 par objet**, une vente de *n*
objets produit *n* réponses.

### 5.3 L'algorithme de prix de vente — ce qui est portable, ce qui ne l'est pas

`GameContent::GetItemSellPrice`, `reference/ngemity/Chihiro/src/Globals/GameContent.cpp:195-220`,
hérité explicitement par cette carte (`socle-marche-npc.md:354-356`).

```
int64_t GetItemSellPrice(int64_t price, int32_t rank, int32_t lv, bool same_price_for_buying)
{
    int64_t k = price;
    float_t f[8]{1.35f, 0.4f, 0.2f, 0.13f, 0.1f, 0.1f, 0.1f, 0.1f};

    ASSERT(rank > 8, "Rank cannot be bigger than 8: %d - GameContent::GetItemSellPrice", rank);

    for (int32_t i = 2; i <= lv; i++) {          // (lv - 1) itérations
        switch (rank) {
        case 0:   k += (price * f[rank]     * 0.1f)   * 10;    break;
        case 1:   k += (price * f[rank - 1] * 0.1f)   * 10;    break;
        case 2:   k += (price * f[rank - 1] * 0.01f)  * 100;   break;
        default:  k += (price * f[rank - 1] * 0.001f) * 1000;  break;
        }
    }

    return (k * (same_price_for_buying ? 1.0f : 0.25f));
}
```

Ce qui **est** portable, et l'est sans ambiguïté :

| Élément | Valeur | Source |
| --- | --- | --- |
| Entrées | `price` et `rank` du **modèle d'objet** ; `lv` de **l'instance** | `:1037-1038` ; `ItemResourceEntity.cs:23` (`Rank`), `:34` (`Price`) ; `ItemInstance.h:53` (`GetLevel`) |
| Barème par niveau | multiplicateur par incrément : **1,35** (rank 0 **et** 1), **0,4** (rank 2), **0,2** (rank 3), **0,13** (rank 4), **0,1** (ranks 5 à 8) | `:198`, `:204-215` |
| Nombre d'incréments | `lv - 1` (la boucle démarre à `i = 2`, borne incluse) : un objet de niveau 1 vaut **exactement** `price × 0,25` | `:202` |
| Facteur final | `× 0,25` en vente ; `× 1,0` si `same_price_for_buying` | `:219` |
| Drapeau `same_price_for_buying` | `code d'objet ∈ [602700, 602799]` | `:1038` |
| Cas mort | `case 0` indexe `f[rank]` (= `f[0]` = 1,35) au lieu de `f[rank-1]`, ce qui lui donne **la même valeur que `case 1`** : les ranks 0 et 1 partagent le barème 1,35 | `:204-209` |

Ce qui **n'est pas** portable tel quel, et pourquoi :

1. **`ASSERT(rank > 8, …)` est inversé.** `ASSERT` = `WPAssert` (`shared/Debugging/Errors.h:85`),
   qui **déclenche quand la condition est fausse** (`:44-51` : `if (!(cond)) Assert(...)`). Écrite
   `rank > 8`, la garde se déclenche donc pour **tout objet légitime** (rank 0 à 8), pas pour un
   rank hors table : le message dit « Rank cannot be bigger than 8 » et la condition fait le
   contraire. À ne pas porter. Ce qui doit être porté, en revanche, c'est ce que la référence
   **n'a pas** : `case default` indexe `f[rank - 1]` **sans borne**, donc un `rank` de base
   supérieur à 8 lit hors du tableau. Le port doit borner `rank` (0 à 8) et refuser, journaliser ou
   replier — décision à trancher (§6 écart 2, `A VERIFIER` 6).
2. **La troncature se fait à chaque incrément, pas à la fin.** `k` est `int64_t`, le membre de
   droite est en virgule flottante : chaque `+=` convertit en entier et **tronque vers zéro**. Un
   barème décimal calculé d'un bloc ne donne donc pas le même résultat. Exemple mesuré (émulation
   float32 du chemin de la référence, `price = 2`, `rank = 2`, `lv = 5`, `same_price_for_buying =
   false`) : l'incrément vaut `f32(f32(f32(2 × 0.4000000059604645) × 0.01) × 100) =
   0.800000011920929`, **tronqué à 0** à chaque tour → `k` reste 2 → retour `0` ; le même calcul en
   arithmétique décimale exacte donne `(2 + 4 × 0.8) × 0.25 = 1.3`, donc **1**. Les deux règles
   diffèrent de 1 pièce d'or dès ce prix, et les écarts apparaissent aussi pour `rank 3` et
   `rank 4` (premier désaccord à `price = 3` : 0 contre 1).
3. **`float_t` n'est pas défini dans le dépôt NGemity** : c'est le type de `<math.h>` (C99), dont la
   largeur est choisie par l'implémentation (`float` en SSE, `double` en x87). La démonstration
   ci-dessus suppose **float32** ; si la référence a été compilée avec un `float_t` de 64 bits, les
   écarts tombent à d'autres prix. **L'égalité bit à bit avec la référence n'est donc pas
   démontrable par lecture statique** — c'est une réserve, pas une consigne d'arrondi (§7 q.4).
4. **Le plafond d'or n'existe pas dans le dépôt.** Recherche des motifs `MAX_GOLD` et `MaxGold`
   dans les `.cs` de `Game/` (`grep -rn`, `--include=*.cs`) : **0 occurrence** ; la constante est
   `ItemTemplate.hpp:4` = `100000000000`. La porter, c'est choisir un plafond de jeu : interdit
   d'inventer (§7 q.1, `A VERIFIER` 1).
5. **La provenance de `lv` est un piège de nom.** Le troisième argument de `GetItemSellPrice` est le
   niveau de **l'instance** (`ItemInstance.h:53`, `m_nLevel`), c'est-à-dire, côté dépôt,
   `ItemEntity.Level` (`Game/DataAccess/Entities/Telecaster/ItemEntity.cs:28`). Ce n'est **pas**
   `ItemResourceEntity.Level` (`:24`), qui est le niveau requis pour porter l'objet. Confondre les
   deux ferait varier le prix avec le niveau de l'objet et non avec son niveau d'amélioration
   d'instance.

### 5.4 Ce que `master` a déjà livré, et ce qui manque (chemins vérifiés sur `b56967a`)

| Élément | État | Chemin:ligne |
| --- | --- | --- |
| Id 252 déclaré | **manquant** | `Game/Network/Packets/Enums/GamePackets.cs` (231 lignes, aucune occurrence de `SELL_ITEM` ; le bloc inventaire est `:40-47`) |
| Bras de réception pour 252 | **manquant** ; les patrons voisins existent | `Game/Network/Clients/GameClient.cs:1555-1560` (208, `HandleEraseItemAsync`), `:1605-1610` (253, `HandleUseItemAsync`) ; `HandleEraseItemAsync` défini `:817-833` ; `switch` final `:1854-1866` |
| Lecteur de trame `TryReadSellItem` | **manquant** | motif à suivre : `Game/Network/Packets/Game/GameActionPackets.cs:155-176` (203), `:191-217` (208, lit déjà `uint32` + `int64`) ; `EraseItemRequest` `:123` |
| Écho 240 | **livré, sans appelant, explicitement pour 251/252** | `Game/Network/Packets/Game/GameTradePackets.cs:83-97` (`BuildNpcTradeInfo`), `NpcTradeInfoSize = 36` `:39`, commentaire de classe `:20-23`, écriture de l'id 240 `:89` |
| Mise à jour d'or (1001, 19 o) | **livré** | `Game/Network/Packets/Game/GameCharacterPackets.cs:239-246` (`BuildGoldUpdate`) ; patron d'envoi `Game/Network/Clients/Actions/GameActions.cs:225` |
| Trames de retrait/count | **livrées** | `GameCharacterPackets.cs:187` (`BuildDestroyItem`), `:195` (`BuildUpdateItemCount`) |
| `TS_SC_RESULT` | **livré** | `Game/Network/Packets/Game/TS_SC_RESULT.cs:6-17` ; `GameClient.cs:68-72` (`SendResult`) ; codes `Game/Network/Packets/ResultCode.cs:6-66` (`Success` 0, `NotExist` 1, `NotActable` 5, `Unknown` 7, `DBError` 8, `TooCheap` 50, `TooMuchMoney` 53, `InvalidArgument` 28) |
| Lecture d'un objet par handle | **livrée** | `Game/Services/ICharacterService.cs:53` (`GetItemByHandleAsync`), impl. `Game/Services/CharacterService.cs:255` |
| Retrait d'une quantité | **livré** | `ICharacterService.cs:60` (`ConsumeItemAsync` → reste de pile ou `null`), `:86` (`RemoveItemAsync`, `ItemRemoval`), `:92` (`EraseItemsAsync`, conçu pour la trame 208) ; helpers privés `CharacterService.cs:391` (`RemoveAmount`), `:548` (`FindByHandle`), `:563-570` (`RunExclusiveAsync`) |
| Écriture de l'or | **livrée** | `ICharacterService.cs:100` (`SaveProgressAsync(…, long gold, …)`), impl. `CharacterService.cs:462-495` (`character.Gold = gold`, `:491`) |
| `Rank` d'un objet | **atteignable** | `Game/DataAccess/Repositories/Interfaces/IItemResourceRepository.cs:6` (`ItemSortFields(int Id, int Category, int Group, int Rank)`) via `GetSortFields()`, impl. `ItemResourceRepository.cs:20-26` |
| `Price` d'un objet | **NON atteignable** : aucune projection de `IItemResourceRepository` ne l'expose | entité `ItemResourceEntity.cs:34` (`Price`), projections `IItemResourceRepository.cs:25-37` (`SortFields`, `EffectFields`, `InstantSkillItems`, `GroupFields`, `UseFields`) ; impl. `ItemResourceRepository.cs:20-66`. Un accès neuf (projection ou service de barème) est **requis** par la vente — c'est le seul manque de données du lot, et il est borné |
| Plafond d'or | **absent du dépôt** (§5.3 point 4) | — |
| Mémoire du marchand (cible de l'écho 240) | **partielle** : `ConnectionInfo` n'a que `NpcDialogHandle` (`:209`) et `NpcDialogTriggers` (`:210`), remis à zéro par `ClearNpcDialog()` (`:375-378`) ; **aucune** mémoire de marché ou de PNJ marchand du type `SetLastContact("npc")` | `Game/Network/Clients/ConnectionInfo.cs:209-210`, `:375-378` |
| Règle de « vendable » | **absente** : recherche des motifs `SellPrice`, `GetSellPrice`, `Sellable`, `sell_price`, `SellValue` dans les `.cs` de `Game/` et `Tests/` (`grep -rn`, `--include=*.cs`) → **0 occurrence** ; colonnes utiles présentes : `ItemEntity.cs:34` (`WearInfo`), `:22` (`StorageId`), `:15` (`EquippedBySummonId`), `:32` (`Flag`) | — |

Le lot `navis-dev` part donc de `master` : il n'y a **aucun service de vente** à réutiliser. La
branche sœur `origin/hermes/packet-251-buy-item` (MR #52, **non mergée**) apporte
`Game/Services/MarketTradeService.cs` et `Game/Services/Interfaces/IMarketTradeService.cs`, **qui
n'existent pas sur `master`** (vérifié : `find Game -name 'MarketTrade*'` → vide) : la fiche ne
présuppose ni leur présence ni leur API, et le lot 252 doit fonctionner sans eux.

## 6. Écarts assumés avec NGemity, et pourquoi

NGemity est compilé à `EPIC_4_1_1` (`shared/Common/Define.h:25`) : ses **valeurs** ne font pas foi,
ses **décisions** si.

| # | Écart | NGemity | Décision Navislamia | Pourquoi |
| --- | --- | --- | --- | --- |
| 1 | Frontière de version de `sell_count` | `uint16` pour `>= EPIC_4_1 && < EPIC_9_4`, `int64` ensuite (`shared/Server/Packets/GameClient/TS_CS_SELL_ITEM.h:9-11`) | **`uint16`** pour 7.3, borne haute `EPIC_8_2` | à 7.3 les deux références donnent le même octet, mais `rzu` (`TS_CS_SELL_ITEM.h:8-9`) est la source de gating et c'est **lui** qui tranche, pas NGemity |
| 2 | Garde de `rank` | `ASSERT(rank > 8, …)` (inversé) puis `f[rank-1]` **non borné** (`GameContent.cpp:200`, `:215`) | garder les rangs 0-8, **borner** l'accès, ne pas recopier l'assertion | l'assertion se déclenche pour tout objet légitime (`Errors.h:44-51, 85`) et l'indexation peut sortir du tableau : deux défauts qu'un portage mécanique importerait. La conduite de repli (refus `Misc` ? journalisation ? prix de base ?) est **réservée** (`A VERIFIER` 6) |
| 3 | Bug de l'écho 240 | `tradePct.code = code;` puis `tradePct.code = pRecvPct->sell_count;` (`WorldSession.cpp:1067-1068`) : `code` reçoit la quantité, `count` reste `0` | **à ne pas porter** : `code` = code d'objet, `count` = `sell_count` | déjà consigné par le socle (`socle-marche-npc.md:245`) ; le constructeur du dépôt prend d'ailleurs six paramètres nommés (`BuildNpcTradeInfo`, `GameTradePackets.cs:83-84`), ce qui rend le bug impossible à reproduire par accident. L'arbitrage formel reste demandé (`A VERIFIER` 5) |
| 4 | Calcul mort `nEnhanceLevel` | `:1040`, jamais lu | non porté | aucune trace observable : le porter ajouterait une variable morte |
| 5 | Contrôle « vendable » | `IsSellable` (`Player.cpp:3158-3166`) = `IsErasable` (`:3136-3156`) **et** pas de `ITEM_FLAG_TAMING` (`ItemTemplate.hpp:174`, `0x20000000`) ; `IsErasable` teste inventaire, propriétaire, `WEAR_NONE`, skillcard liée, summoncard liée, hors stockage ; le code porte son propre aveu : `// this is not 100% correct, needs to be reworked` (`:3164`) | porter **le sous-ensemble démontrable** sur les colonnes du dépôt (non équipé : `WearInfo == ItemWearType.None` ; hors stockage : `StorageId == null` ; hors invocation : `EquippedBySummonId == null`) et refuser en `NotExist` (1) ; le reste (cartes liées, drapeau `Taming`) est **réservé** | les « cartes liées » n'ont pas d'équivalent lisible dans le dépôt (`ItemEntity` n'a pas de cible liée ; le drapeau du dépôt est un **indice de bit** — `ItemFlag.Taming = 29`, `Game/DataAccess/Entities/Enums/ItemFlag.cs:15` — alors que NGemity compare un **masque** `0x20000000` : comparer les deux directement serait faux). Une règle de « vendable » improvisée est interdite (`A VERIFIER` 2) |
| 6 | Poids | **aucun** contrôle de poids à la vente (`:1021-1072`) | rien à porter | contrairement à l'achat, la vente allège : la référence ne teste pas la charge |
| 7 | Arrondi du prix | troncature par incrément, arithmétique flottante de largeur non définie (§5.3 points 2-3) | **à trancher**, pas à supposer : soit reproduire pas à pas le chemin flottant, soit calculer en décimal et tronquer une fois — les deux donnent des résultats différents (exemple mesuré §5.3 point 2) | décision de jeu, réservée (`A VERIFIER` 3) |
| 8 | Drapeau `same_price_for_buying` | plage **codée en dur** `[602700, 602799]` (`:1038`) | non tranché : porter la plage telle quelle (elle est **sourcée**) ou la laisser de côté | le sens métier du drapeau (et l'existence de cette bande de codes dans la table d'objets 7.3) n'est pas documenté par la référence ; `A VERIFIER` 4 |
| 9 | Cible de l'écho 240 | `GetLastContactLong("npc")` (`:1070`, posé `:702`) | `ConnectionInfo.NpcDialogHandle` (`:209`) est le seul équivalent présent ; il n'est pas une « dernière cible de commerce » et il est effacé par `ClearNpcDialog()` (`:375-378`) | le handle du marchand doit être **retenu** au moment du dialogue pour survivre à la vente : c'est une donnée à ajouter au suivi de session (`A VERIFIER` 4) |
| 10 | Codes et valeurs de refus | 1 (valeur 0), 7 (valeur 0), 1 (valeur code), 53 (valeur code), 5 (valeur code), 5 (valeur handle) | porter **les codes et les valeurs tels quels** | ils sont sourcés ligne à ligne (§5.1) et le dépôt possède déjà tous ces codes (`ResultCode.cs:6-66`) ; les harmoniser serait une invention |
| 11 | Pièges `CLAUDE.md` | — | aucun contact | cette fiche ne touche ni les bits `limit_*` (allow-list lue à l'envers par la référence, `CLAUDE.md:959`) ni le `break` manquant de `SRT_ADD_HP` (`CLAUDE.md:898`) : aucun n'est recopié |

## 7. `NON ÉTABLI`

1. **Nom de la commande d'UI portant l'id interne `1048`** (§2) : confort de documentation, sans
   effet sur la trame.
2. **Effet visible d'un refus dans le client 7.3.** Les codes 0/1/5/7/28/53 sont sourcés, mais le
   consommateur client de `TS_SC_RESULT` pour `request_msg_id == 252` n'a pas été tracé par lecture
   statique : rien n'établit qu'un `1` ou un `53` affiche un message plutôt que rien. Question
   précise : le client 7.3 distingue-t-il un refus de vente de l'absence de réponse ? (Même réserve
   que pour 251, fiche sœur §7 q.1.)
3. **Ce que le client fait de l'écho 240 pour une vente** (`is_sell = 1`) : la trame est spécifiée
   (§3.1) et la référence l'émet (`:1065-1071`), mais son consommateur client — mise à jour du
   panier de vente, ligne de journal, rien — n'a pas été tracé. Question précise : 240 est-il
   **nécessaire** au bon fonctionnement de la fenêtre après une vente, ou seulement informatif ?
4. **Règle d'arrondi du prix de vente.** Les deux règles en présence (troncature par incrément en
   virgule flottante ; calcul décimal tronqué une fois) donnent des prix différents (§5.3 point 2),
   et la largeur exacte de `float_t` dans la compilation de la référence n'est pas établie
   (§5.3 point 3). Non tranchable par lecture statique seule.
5. **Sens métier du drapeau `same_price_for_buying`** et réalité de la bande `[602700, 602799]`
   dans la table d'objets 7.3 (§6 écart 8) : la plage est sourcée, sa signification ne l'est pas.
6. **Conduite de repli sur un `rank` hors 0-8** (§6 écart 2) : la référence lit hors tableau ; le
   dépôt doit choisir un comportement (refus, journalisation, repli sur le prix de base) — décision
   de jeu, pas de protocole.
7. **Ce que le client met exactement dans le vecteur 12 octets** au-delà des 6 premiers octets, et
   si le panier de vente peut contenir un objet que le joueur ne possède pas : les seules lectures
   établies sont `dword` en 0 et `word` en 4 (§2, §3). Question précise : le client borne-t-il
   `sell_count` par la pile réelle, ou le serveur doit-il s'y fier ? (La référence, elle, refuse
   `count - sell_count < 0` en `:1042`, donc elle ne s'y fie pas.)
8. **Faut-il déclarer `TM_CS_BUY_ITEM` (251) dans ce lot ?** Non : 251 a sa propre carte
   (`jZyAqheC`, corps de la carte du 2026-09-25), sa branche et sa MR #52. Mais les deux lots
   écriront dans `GamePackets.cs` et `GameClient.cs` (§10), et le panier de vente partage le même
   vecteur que le panier d'achat : la collision est réelle.

## 8. Commits épinglés

| Dépôt | Commit | Détail |
| --- | --- | --- |
| `reference/rzu` | **`87c1e83bf84efe29bb6405e8e6da80349712f3fa`** (2023-10-02, *packets: fix TS_SC_INVENTORY with older epics*) | `librzu/src/packets/GameClient/TS_CS_SELL_ITEM.h` (trame, gating, ids), `librzu/src/lib/Packet/PacketEpics.h` (bornes), `librzu/src/lib/Packet/GameTypes.h:40` (`ar_handle_t`) |
| `reference/ngemity` | **`38ceb2c6065fabf6ff4ba71d52f955f362c6c839`** (2025-12-03, *Fix compilation issue for GCC*) | `shared/Server/ClientPackets.h:92`, `shared/Server/Packets/GameClient/TS_CS_SELL_ITEM.h`, `Chihiro/src/Network/GameNetwork/WorldSession.cpp:1021-1072`, `Chihiro/src/Globals/GameContent.cpp:195-220`, `Chihiro/src/Entities/Item/*`, `Chihiro/src/Entities/Player/Player.cpp` |
| `reference/client73` | **pas un dépôt git** : aucun SHA à épingler | `SFrame.exe`, 9 841 664 octets, sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, lu **statiquement** (désassemblage `objdump`, `strings -t x`). Aucune exécution : ni `SFrame.exe`, ni Lua, ni script client. `db_string.rdb` (14 294 729 octets, sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1`) a été ouvert aussi : il ne contient **aucun** nom `TM_*` (0 occurrence) — c'est la table de chaînes de ressources, la table de noms de paquets lue est celle de `SFrame.exe` (`0x65206c`) |
| `Navislamia` (base de la branche) | **`b56967a07430422add88e0e5cdf292b41b18f6c6`** (`master`, *Merge pull request #44*) | état mesuré : `master` propre, `git log --oneline origin/master..master` vide, `dotnet build Navislamia.sln -c Debug` → **0 erreur**, `dotnet test Tests/Tests.csproj` → **`Failed: 0, Passed: 1302, Skipped: 0`** |

## 9. Forme du test d'offsets attendue (lot `navis-dev`)

Discipline du dépôt : un test qui **mord** sur la taille et sur la position de chaque champ, dans le
style de `Tests/Game/DropItemPacketsTests.cs:20-33` (203) et `Tests/Game/MarketPacketsTests.cs:34-45`
(240/250). Fichier attendu : `Tests/Game/SellItemPacketsTests.cs`.

**Lecteur (`TryReadSellItem`)** — au minimum :

1. la trame de référence fait **exactement 13 octets** (`packet.Length.Should().Be(13)`), l'id en 4
   vaut `(ushort)GamePackets.TM_CS_SELL_ITEM` = **252**, la longueur en 0 vaut 13, la somme de
   contrôle en 6 est la somme des octets 0-5 ;
2. `handle` lu à l'**offset 7** en `uint32` : une valeur sentinelle `0x80000123` doit ressortir
   intacte (elle dépasse `int.MaxValue` : un `int` la casserait) ;
3. `sell_count` lu à l'**offset 11** en `uint16` : `0xffff` (65535) doit ressortir à 65535 et
   **non** à -1, ce qui distingue la variante `uint16` de 7.3 de la variante `int64` d'`EPIC_8_2` ;
4. une trame **tronquée** (12 octets) et une trame **trop longue** (14 octets, cas d'une trame 252
   construite avec `sell_count` en `int64`) sont refusées ;
5. le contrat d'`Enum`/dispatch : `GamePackets.TM_CS_SELL_ITEM` existe, vaut 252, et une trame 252
   fait **13 octets** — donc le membre ne peut pas atteindre le `switch` final
   (`GameClient.cs:1865`) ; la valeur 1252 **ne doit pas** être déclarée.

**Service (le geste et ses refus)** — au minimum :

6. une vente réussie répond **`TS_SC_RESULT` (15 octets, `request_msg_id = 252`, `result = 0`,
   `value = handle`) puis `TM_SC_NPC_TRADE_INFO` (**36 octets**, `is_sell = 1` à l'offset 7,
   `code` = code d'objet à 8, `count` = `sell_count` à 12, `price` = total à 20,
   `huntaholic_point = 0` à 28, `target` = handle du marchand à 32) — l'ordre est vérifiable ;
7. l'or est mis à jour par `TM_SC_GOLD_UPDATE` : **19 octets**, `gold` `uint64` à 7, `chaos`
   `uint32` à 15 ;
8. chaque refus de §5.2 est testé avec son **code** et sa **valeur** exacts (objet inconnu → 1
   valeur 0 ; `sell_count == 0` → 7 valeur 0 ; objet non vendable ou `sell_count > Amount` → 1
   valeur = code d'objet ; échec de retrait → 5 valeur = handle) ;
9. le barème de prix est testé sur des cas **nommés avec leur règle d'arrondi**, une fois celle-ci
   tranchée (`A VERIFIER` 3) : `price × 0,25` pour `lv = 1`, une itération pour `lv = 2`, et au
   moins un cas par `rank` (0 et 1 identiques, 2, 3, 4, 5+) ; tout test de prix écrit avant
   l'arbitrage devra porter la règle qu'il applique dans son nom ;
10. la vente de `n` objets = `n` trames 252 = `n` réponses (§2, boucle de l'émetteur).

Le compte de tests ne baisse jamais : 1302 sur `b56967a` (§8).

## 10. Zone de collision

**20 MR étaient ouvertes** au réveil du PO du 2026-09-25 18:03 CEST — #1, #6, #13, #21, #22, #23,
#24, #26, #27, #33, #38, #40, #45, #46, #47, #48, #49, #50, #51, #52 (relevé du corps de la carte,
non revérifiable ici : ce poste n'a pas d'accès GitHub). Presque toutes touchent
`Game/Network/Packets/Enums/GamePackets.cs` et `Game/Network/Clients/GameClient.cs`, exactement
comme ce lot.

La collision qui compte est **#52** (la jumelle 251, branche `origin/hermes/packet-251-buy-item`,
tip `f7c4134`, **non mergée**) : `git diff --stat origin/master...origin/hermes/packet-251-buy-item`
→ **15 fichiers**, dont `GamePackets.cs`, `GameClient.cs`, `GameTradePackets.cs`, `ConnectionInfo.cs`,
`NetworkService.cs`, `IMarketService.cs`, `MarketService.cs`, `NpcDialogService.cs`, plus **quatre
fichiers neufs absents de `master`** (`git diff --name-status` : deux fichiers source —
`Game/Services/MarketTradeService.cs`, `Game/Services/Interfaces/IMarketTradeService.cs` — plus
`Tests/Game/BuyItemPacketsTests.cs` et `docs/packet-specs/251-buy-item.md`) — relevé refait ici.

Conséquences pour le lot :

- **interdit de présupposer** l'existence ou l'API de `MarketTradeService` ; le lot 252 part de
  `master` et doit compiler sans la branche 251 ;
- les deux lots déclareront un membre dans `GamePackets.cs` et un bras dans `GameClient.cs` au même
  endroit : attendre un conflit textuel local, le résoudre en gardant les deux entrées ;
- la fiche sœur est lue par `git show origin/hermes/packet-251-buy-item:docs/packet-specs/251-buy-item.md`
  (elle n'est **pas** sur `master` — vérifié : `git cat-file -e master:docs/packet-specs/251-buy-item.md`
  échoue). Elle n'a pas été prise comme base et n'a pas à être corrigée.

## 11. Bloc destiné à `CLAUDE.md`

*(À recopier dans la description de la MR par la QA — ce lot n'écrit pas `CLAUDE.md`, fichier
protégé par Hermes. Bloc à insérer après la section « Paquets 240 / 250 — marché NPC »,
`CLAUDE.md:1996`.)*

> ### Paquet 252 — vente chez un marchand (`TM_CS_SELL_ITEM`)
>
> - 7.3 : trame **client → serveur de 13 octets**, en-tête de 7 comprise — `handle` (`uint32` à 7),
>   `sell_count` (`uint16` à **11**, 2 octets). L'id est **252** (jamais 1252), et `sell_count` n'est
>   **pas** le `int64` d'`EPIC_8_2` : la variante qui compte pour 7.3 est `uint16` (rzu
>   `TS_CS_SELL_ITEM.h:7-10`). Vérifié sur le client : `mov eax,0xfc` (`0x48f434`), longueur `0xd`
>   (`0x48f43d`), `dword` en +7 (`0x48f46a`), `word` en +11 (`0x48f46d`).
> - **Le client émet une 252 par objet vendu** : l'émetteur `0x48f410` boucle sur un vecteur
>   d'éléments de 12 octets (`add esi,0xc` en `0x48f487`) et n'en lit que les 6 premiers octets.
>   Une vente de *n* objets = *n* trames = *n* réponses.
> - Réponse : **toujours** un `TS_SC_RESULT` (15 octets) avec `request_msg_id = 252`, puis, en cas
>   de succès, l'écho **`TM_SC_NPC_TRADE_INFO` (240) `is_sell = 1`** — 36 octets, `code` = code
>   d'objet, `count` = quantité, `price` = total, `huntaholic_point` = 0, `target` = handle du
>   marchand. `GameTradePackets.BuildNpcTradeInfo` (`:83`) est livré pour ce seul usage.
> - Codes de refus de la référence, à porter tels quels : objet inconnu / pas au joueur / hors
>   inventaire → `NotExist` (1) valeur 0 ; `sell_count == 0` → `Unknown` (7) valeur 0 ; objet non
>   vendable ou quantité supérieure à la pile → `NotExist` (1) valeur = code d'objet ; échec du
>   retrait → `NotActable` (5) valeur = handle.
> - **Deux pièges de la référence :** `onSellItem` écrit `tradePct.code` deux fois
>   (`WorldSession.cpp:1067-1068`) et n'alimente jamais `count` — **à ne pas porter** ; et
>   `GetItemSellPrice` tronque son incrément à **chaque** tour de boucle (l'incrément de rank 2
>   pour `price = 2` vaut `0.80000001`, donc **0**), avec un `ASSERT(rank > 8)` **inversé** qui se
>   déclenche pour tout objet légitime (`GameContent.cpp:200`). Le barème (1,35 pour rank 0 **et** 1 ;
>   0,4 ; 0,2 ; 0,13 ; 0,1) est portable, la règle d'arrondi ne l'est pas : voir la fiche.
> - Le plafond `MAX_GOLD_FOR_INVENTORY` (`ItemTemplate.hpp:4`, `100000000000`) n'a **aucun**
>   équivalent dans le dépôt : `TOO_MUCH_MONEY` (53) reste sans producteur tant que la décision
>   n'est pas prise.
> - Voir `docs/packet-specs/252-sell-item.md`.

## 12. Implémentation livrée (lot `navis-dev`, branche `hermes/packet-252-sell-item`)

Commit `020a2d9` (code, tests et branchement) ; la fiche est commitée juste après. Base : `master`
`b56967a`, comme la fiche le prescrit — **aucun symbole de la branche sœur 251 n'est utilisé**.

### 12.1 Fichiers

| Fichier | Nature | Rôle |
|---|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | modifié | `TM_CS_SELL_ITEM = 252`, déclaré avec la famille des objets (à côté de 253-255) et non sous l'ancre 250/283 que le lot 251 écrit |
| `Game/Network/Packets/Game/GameTradePackets.cs` | modifié | `SellItemSize = 13` et `TryReadSellItem` (`handle` `uint32` à 7, `sell_count` `uint16` à 11, tout autre longueur refusée), placés après `BuildNpcTradeInfo`, loin de l'insertion du lot 251 |
| `Game/Network/Clients/GameClient.cs` | modifié | bras de dispatch 252 (avant le `switch` qui lève `Unknown Packet Type`) et `HandleSellItemAsync` (lecture bornée puis geste) |
| `Game/Network/NetworkService.cs` | modifié | `MarketSellService`, paramètre inséré après `IItemUseService` (le lot 251 ajoute le sien en fin de liste) |
| `Game/Services/Interfaces/IMarketSellService.cs`, `Game/Services/MarketSellService.cs` | neufs | le geste : prix, refus, retrait, paiement, écho |
| `Game/Services/MarketSellPrice.cs` | neuf | le barème (`f[rank - 1]`, un incrément par niveau au-dessus de 1, quart final) et la bande 602700-602799 |
| `Game/Services/Interfaces/IItemSellCatalog.cs`, `Game/Services/ItemSellCatalog.cs` | neufs | `(rank, price)` de chaque objet, chargé une fois comme les autres catalogues d'objets |
| `Game/DataAccess/Repositories/Interfaces/IItemResourceRepository.cs`, `Game/DataAccess/Repositories/ItemResourceRepository.cs` | modifiés | `ItemSellFields(Id, Rank, Price)` et `GetSellPriceFields()` — l'accès `Price` que §5.4 signalait comme manquant |
| `DevConsole/Program.cs` | modifié | enregistrement des deux nouveaux services |
| `Tests/Game/SellItemPacketsTests.cs` | neuf | 48 tests (trame, dispatch, branchement, geste, barème) |
| `Tests/Game/StorageTestHarness.cs`, `Tests/Game/ResurrectionPacketTests.cs` | modifiés | `NetworkService` a un paramètre de plus : les deux points de construction des tests suivent |

### 12.2 Décisions prises là où la fiche laissait ouvert

Aucune n'est une supposition : chacune est soit une règle de la référence portée telle quelle, soit un
refus explicite là où la référence n'a pas de comportement reproductible. Les six réserves restent
ouvertes ci-dessous.

- **Arrondi du prix (A VERIFIER 3)** : le lot porte le **chemin de la référence**, troncature de
  l'incrément à chaque tour de boucle, en `float` 32 bits, puis un quart du total
  (`MarketSellPrice.TryComputeUnitPrice`). L'exemple mesuré de §5.3 (`price = 2`, `rank = 2`, `lv = 5`
  → **0** pièce) est reproduit par un test nommé pour cette règle, avec le résultat de l'autre règle
  (**1**) écrit en commentaire : la substitution de l'une par l'autre fera rougir le test. La largeur
  de `float_t` reste non établie, la règle reste donc à confirmer.
- **`rank` hors 0-8 (§6 écart 2)** : refus, `NotExist` (1) valeur = code d'objet, journalisé en
  avertissement. C'est le code que la référence répond à un objet non vendable ; la référence, elle,
  lit hors de son tableau (`GameContent.cpp:214`) et ne produit aucune valeur reproductible. Le refus
  remplace donc un débordement, pas une politique de jeu. *(La fiche renvoie ce point à « A VERIFIER
  6 », qui traite en fait de la bande de prix : la conduite de repli n'a pas de question numérotée
  propre — elle est signalée ici, et son état de code est en §6 écart 2.)*
- **`target` de l'écho 240 (A VERIFIER 4)** : `ConnectionInfo.NpcDialogHandle`, l'équivalent du
  `GetLastContactLong("npc")` de la référence. `NpcDialogService` laisse délibérément le dialogue
  courant à l'ouverture d'une fenêtre de marché, donc le handle survit à la vente ; `ClearNpcDialog()`
  l'efface, et une vente sans dialogue courant émet `target = 0` (la référence n'a pas de refus pour
  ce cas non plus). **Aucune donnée de session neuve n'a été ajoutée là où la fiche envisageait un
  champ « dernier marchand »** : à trancher.
- **Politique « vendable » (A VERIFIER 2)** : le sous-ensemble démontrable que la fiche fixe —
  `WearInfo == ItemWearType.None`, `StorageId == null`, `EquippedBySummonId == null` — refusé en
  `NotExist` (1) valeur = code d'objet. Cartes liées et drapeau `Taming` : hors périmètre, comme la
  fiche le réserve.
- **Bande `[602700, 602799]` (A VERIFIER 6)** : **portée telle quelle** (`× 1,0` au lieu de `× 0,25`),
  bornes comprises, avec un test des quatre codes frontière et un test de bout en bout sur 602700.
  C'est la branche sourcée de `WorldSession.cpp:1038` ; son sens métier reste à confirmer.
- **Plafond d'or (A VERIFIER 1)** : **non porté**, faute d'équivalent dans le dépôt ;
  `TOO_MUCH_MONEY` (53) reste sans producteur. La garde de débordement de la référence
  (`:1050-1053`) est en revanche portée telle quelle (`NotActable` 5 valeur = code d'objet) : sur un
  or `int64` elle ne se déclenche qu'en débordement.
- **Bug `code`/`count` de la référence (A VERIFIER 5)** : non porté. `code` = code d'objet et
  `count` = quantité vendue, la fiche tranche déjà dans ce sens pour le contrat de la trame.

### 12.3 Trames et ordre, tels que testés

Une vente réussie émet, dans cet ordre : `TM_SC_UPDATE_ITEM_COUNT` (255, 19 octets) ou
`TM_SC_DESTROY_ITEM` (254, 11 octets) quand la pile est vidée — le retrait de la référence précède son
paiement, et c'est ce retrait qui fait disparaître les unités chez le client —, puis
`TM_SC_GOLD_UPDATE` (19 octets, or + chaos), puis `TS_SC_RESULT` (15 octets, `request_msg_id = 252`,
`result = 0`, `value` = handle), puis l'écho `TM_SC_NPC_TRADE_INFO` (240, 36 octets, `is_sell = 1`,
`code` = code d'objet, `count` = quantité, `price` = **total**, `huntaholic_point` = 0, `target` =
handle du marchand). Les refus n'émettent **qu'un `TS_SC_RESULT`** : `NotExist` (1) valeur 0 (objet
inconnu, hors du sac, sans template), `Unknown` (7) valeur 0 (`sell_count == 0`), `NotExist` (1) valeur
= code (non vendable, quantité supérieure à la pile, `rank` hors échelle), `NotActable` (5) valeur =
handle (retrait refusé), `NotActable` (5) valeur = code (débordement de bourse), `DBError` (8) valeur 0
(magasin muet, convention du dépôt et non de la référence).

### 12.4 Ce que le lot ne fait pas

- Aucune vérification de fenêtre ouverte : la référence n'en a pas non plus à la vente.
- Aucune persistance de l'or : la session est mise à jour et la trame part, comme `CombatService` et
  la commande GM le font déjà ; l'écriture en base reste l'affaire de `SaveProgressAsync`.
- `TOO_MUCH_MONEY` (53) : sans producteur (A VERIFIER 1).
- Cartes liées, drapeau `Taming`, barème hors 0-8 : hors périmètre (A VERIFIER 2 et 6).

### 12.5 Tests livrés et preuve de morsure

- 48 tests neufs dans `Tests/Game/SellItemPacketsTests.cs` : offsets de la trame (octets posés à la
  main, en-tête de 7 vérifié par `Marshal.SizeOf<Header>()`, longueurs 0/7/12/14/36 refusées),
  présence du bras de dispatch **avant** le `throw` (analyse de source), consommation de la trame dans
  la boucle de réception, branchement des valeurs décodées au service, ordre et charge utile de chaque
  trame répondue, chaque refus, la bande de prix, le barème par rang et la règle d'arrondi.
- `dotnet build Navislamia.sln -c Debug` → **0 erreur** ; `dotnet test Tests/Tests.csproj` →
  **1350 réussis, 0 échec** (1302 sur `master` + 48), donc le compte ne baisse pas.
- Desarmorçage dans un worktree jetable (`git worktree add --detach`), mutant par mutant, chacun
  annulé ensuite — témoin 48/48 vert, puis : garde de longueur relâchée en `<` → 2 échecs ; borne de
  `rank` retirée → 3 échecs ; bras de dispatch vidé → 2 échecs ; écho portant le prix unitaire au lieu
  du total → 1 échec. Les tests mordent sur la trame, le gating, le dispatch et le calcul.

### 12.6 Collision mesurée avec la jumelle 251

`git merge-tree --write-tree --name-only hermes/packet-252-sell-item hermes/packet-251-buy-item` →
**un seul fichier en conflit** : `Tests/Game/StorageTestHarness.cs`, où les deux lots ajoutent un
paramètre optionnel à `NewGameClient` (résolution : garder les deux). `GamePackets.cs`,
`GameTradePackets.cs`, `GameClient.cs`, `NetworkService.cs`, `ResurrectionPacketTests.cs` et
`DevConsole/Program.cs` fusionnent **automatiquement** : le membre d'enum, le lecteur, le bras de
dispatch, le paramètre de constructeur et le point de construction des tests ont été placés à distance
des insertions du lot 251. `StorageTestHarness.cs` est donc le point chaud à signaler (§10) : c'est le
seul fichier que tous les lots qui ajoutent un service doivent toucher.

## A VERIFIER PAR KILLIAN

Six décisions ne sont pas tranchables par lecture statique ; aucune n'a été comblée par une
constante choisie au jugé. Les points 1 à 4 sont les attendus minimaux de la carte ; le point 5 est
la question laissée ouverte par le socle ; le point 6 borne un défaut de la référence.

1. **Plafond d'or.** La référence refuse la vente quand `or + sell_count × prix >
   MAX_GOLD_FOR_INVENTORY` = `100000000000` (`WorldSession.cpp:1046-1049`,
   `ItemTemplate.hpp:4`). Le dépôt n'a **aucune** notion de plafond d'or (recherche des motifs
   `MAX_GOLD` et `MaxGold` dans les `.cs` de `Game/` → 0 occurrence) : `TOO_MUCH_MONEY` (53)
   resterait donc sans producteur. Faut-il introduire le plafond de la référence, un autre, ou
   laisser la vente toujours aboutir tant que l'or tient dans un `int64` ? Réponse attendue :
   « plafond de la référence » / « autre valeur : … » / « pas de plafond ».
   *État du code : non porté, `TOO_MUCH_MONEY` (53) n'est produit nulle part ; la garde de
   débordement `:1050-1053` est portée telle quelle (`NotActable` 5 valeur = code, testée).*
2. **Politique de « vendable ».** La référence a laissé le contrôle commenté (`:1035`), porte son
   propre aveu « not 100% correct » (`Player.cpp:3164`) et lit `ITEM_FLAG_TAMING` comme un masque
   `0x20000000` alors que le drapeau du dépôt est un **indice de bit** (`ItemFlag.Taming = 29`).
   Peut-on livrer la vente avec le sous-ensemble démontrable (non équipé, hors stockage, hors
   invocation) et laisser les cas « carte liée » et « familier apprivoisé » hors périmètre, ou
   faut-il un arbitrage plus complet avant d'ouvrir la fenêtre de vente ?
   *État du code : le sous-ensemble démontrable est livré (`MarketSellService.cs`, `IsSellable`), refus
   `NotExist` (1) valeur = code d'objet ; cartes liées et `ItemFlag.Taming` ne sont pas jugés.*
3. **Arrondi du prix de vente.** Deux règles vérifiables, deux résultats différents : troncature de
   l'incrément **à chaque** tour de boucle (comportement mesuré de la référence — `price = 2`,
   `rank = 2`, `lv = 5` → **0** pièce) ou calcul décimal tronqué **une fois** à la fin (même cas →
   **1** pièce). La largeur exacte de `float_t` dans la compilation de NGemity n'est pas établie.
   La fiche ne choisit pas : laquelle retenir, et laquelle documenter dans le test ?
   *État du code : le chemin de la référence est porté (troncature à chaque tour, `float` 32 bits), avec
   un test nommé sur le cas mesuré `price = 2`, `rank = 2`, `lv = 5` → 0 ; le résultat de l'autre règle
   (1) est écrit dans le commentaire du test, qu'un basculement fera rougir.*
4. **Provenance du `target` de l'écho 240.** NGemity utilise `GetLastContactLong("npc")`
   (`WorldSession.cpp:1070`, posé au contact du PNJ `:702`). Le dépôt n'a que
   `ConnectionInfo.NpcDialogHandle` (`ConnectionInfo.cs:209`) et `NpcDialogTriggers` (`:210`),
   effacés par `ClearNpcDialog()` (`:375-378`) — et aucune mémoire de ce type pour un marchand
   ouvert par `TM_SC_MARKET`. Faut-il retenir le handle du marchand à l'ouverture de la fenêtre
   (donnée de session neuve) et l'émettre comme `target`, ou émettre autre chose ?
   *État du code : `target` = `ConnectionInfo.NpcDialogHandle` au moment de la vente, aucune donnée de
   session neuve n'a été ajoutée ; un dialogue effacé donne `target = 0`. Testé avec un handle de
   marchand distinctif.*
5. **Bug `code`/`count` de la référence.** Le socle le classe « à ne pas porter »
   (`socle-marche-npc.md:245`) et cette fiche retient `code` = code d'objet, `count` = quantité.
   Confirmation demandée, puisque c'est le contrat exact de la trame 240 lue par le client.
   *État du code : non porté — `code` = code d'objet, `count` = quantité vendue, `price` = total ; les
   trois sont lus aux offsets 8/12/20 par un test d'offsets.*
6. **Bande `[602700, 602799]`.** `same_price_for_buying` vaut vrai pour ces codes (`:1038`) et la
   vente se fait alors à `× 1,0` au lieu de `× 0,25`. La plage est sourcée, son sens métier ne
   l'est pas : faut-il la porter telle quelle, ou la laisser de côté en attendant ?
   *État du code : portée telle quelle (`MarketSellPrice.IsSamePriceForBuying`, bornes comprises), avec
   un test des quatre codes frontière et une vente de bout en bout sur 602700. Le retrait est sans effet
   ailleurs : l'éteindre, c'est changer une ligne et faire rougir deux tests.*
