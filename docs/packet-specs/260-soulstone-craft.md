# 260 — TM_CS_SOULSTONE_CRAFT

Fiche de paquet établie par `navis-ref` (archéologue de protocole), branche
`hermes/packet-260-soulstone-craft`, depuis `master`
`b56967a` (Merge PR #44). Cette fiche ne contient **aucun** changement de code serveur : elle fixe
le format, le gating de version, le traitement attendu, les écarts assumés et les réserves
vérifiables. Elle complète, sans la remplacer, la fiche de socle
`docs/packet-specs/socle-artisanat-objets.md` (§2.2, §3.3, §6.2, §11.6, §12.4).

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id décimal | **260** (`0x0104`) | `op_codes.md:88` — `[260] = "TM_CS_SOULSTONE_CRAFT"` |
| sens | client → serveur | rzu `librzu/src/packets/GameClient/TS_CS_SOULSTONE_CRAFT.h:13` (`SessionPacketOrigin::Client`) ; NGemity `Chihiro/src/Network/GameNetwork/WorldSession.cpp:131` (`declareHandler(STATUS_AUTHED, &WorldSession::onSoulStoneCraft)`) |
| nom serveur | `TM_CS_SOULSTONE_CRAFT` | `op_codes.md:88` ; NGemity `shared/Server/Packets/GameClient/TS_CS_SOULSTONE_CRAFT.h:10` |
| taille de la trame | **27 octets**, fixe | §3 (trois sources concordantes) |
| réponse associée | `TM_SC_RESULT` (0) et, en cas de succès, `TM_SC_INVENTORY` (207) portant l'objet mis à jour | §5.2 |
| état dans le dépôt | **déclaré et câblé au socle** : lecture + bornage + refus, aucun moteur | `Game/Network/Packets/Enums/GamePackets.cs:58` ; `Game/Network/Clients/GameClient.cs:1622-1630` ; `Game/Services/CraftingSocleService.cs:67-75` |
| PRÉREQUIS MANQUANT | l'**ouverture de la fenêtre de sertissage** n'a pas d'id établi (259 est contradictoire) | §5.4 et §7.1 |
| carte Trello | `trello.com/c/6tQFAXqE` | suivi `navislamia:packet:260` |

Le nom `TM_CS_SOULSTONE_CRAFT` **n'apparaît pas** dans la table de noms de paquets du binaire
client (`SFrame.exe`, dump `strings -n 4`, l. 26360-26400 : on y lit `TM_TRADE`,
`TM_CS_TRANSMIT_ETHEREAL_DURABILITY`, `TM_SC_MIX_RESULT`, `TM_CS_MIX`,
`TM_SC_UPDATE_ITEM_COUNT`, `TM_SC_DESTROY_ITEM`, … mais rien de `SOULSTONE`). Cette table est
partielle (`socle-artisanat-objets.md` §3.0) : l'absence du nom n'est pas une absence de paquet.
Le paquet lui-même est **prouvé présent** par le constructeur de trame du client (§3.1).

## 2. Ce que le joueur fait pour que le client l'envoie

1. Il parle à un **PNJ joaillier** — le catalogue du dépôt identifie le contact
   `NPC_Jewelry_contact` (`DevConsole/npc-dialogs.73.json`,
   `/NpcDialogCatalog/Dialogs/NPC_Jewelry_contact`) et la ligne de dialogue
   *« I can socket your soul stones for you, for a price. Just tell me what you need. »*
   (`db_string.rdb`, dump `strings -n 4`, l. 84250).
2. Il choisit l'entrée de menu **`show_soulstone_craft_window()`**
   (`npc-dialogs.73.json`, `NPC_Jewelry_contact`, `Menu[5]`, libellé `@90010169`). Les trois
   libellés consécutifs du menu — `question_Jewelry` `@90010167`, `show_soulstone_craft_window`
   `@90010169`, `show_soulstone_repair_window` `@90010170` — correspondent, dans l'ordre, aux trois
   textes consécutifs *« What are Soul Stones? »*, *« Socket my Equipment. »* et
   *« Charge my Soul Power. »* (`db_string.rdb`, l. 242713-242717, chacun suivi de
   `script_string_10017`). Correspondance **raisonnée** (l'id de ressource `@90010169` n'est pas
   résolu par lecture de `db_string.rdb`) mais non ambiguë : elle porte le nom du déclencheur.
3. La fenêtre de sertissage s'ouvre — ressource `window_Socket.nui`
   (`SFrame.exe`, dump `strings -n 4`, l. 24812), widget `static_common_socket_itemslot`
   (l. 30293), classe de message `.?AUSIMSG_UI_SOULSTONE_CRAFT@@` (l. 44021). Ses boutons sont
   `ui_text_9512` = *« Confirm »*, `ui_text_9513` = *« Socket »*, `ui_text_9515` = *« Cancel »*
   (`db_string.rdb`, l. 189570-189576).
4. Il place l'**équipement à sertir** dans le slot principal et **jusqu'à quatre pierres** dans les
   châsses, puis valide. Le client confirme avant d'envoyer : `smsg_soket01` (*« There is already a
   Soul Stone in this socket… Would you like to socket the Soul Stone? »*), `smsg_soket02`
   (*« Socketing cost is <B>#@cost@#</B>. … Would you like to socket the item? »*)
   (`db_string.rdb`, l. 198036-198039).
5. Le client **refuse avant envoi** : `smsg_soket03` (pierre uniquement, l. 198041),
   `smsg_soket04` (déjà serti, l. 198043), `smsg_soket05` (*« You can not have more than 2 Soul
   Stones socketed in the same weapon that increase the same stat. »*, l. 198045),
   `smsg_soket06` (mauvais type d'objet, l. 198047). Ce sont des gardes d'interface : le serveur ne
   peut pas s'y fier (§5.1).
6. Le client émet alors `260` (§3.1) ; le succès se dit `smsg_soket07` = *« Socketing is
   completed. »* (l. 198049). Les textes `smsg_soket09` à `smsg_soket11` (l. 198051-198057) sont du
   lobe voisin (recharge avec du **Lak**, paquet 262) et ne concernent pas 260.

## 3. Structure sur le fil

En-tête commun de 7 octets : `length` (`uint32`, offset 0), `id` (`uint16`, offset 4),
`checksum` (`byte`, offset 6) — `Game/Network/Packets/Header.cs:9-11` et `:20-25`,
`PacketExtensions.CalculateChecksum`. `HeaderSize = 7` : `Game/Network/Packets/Game/GameActionPackets.cs:9`.

| offset | type | nom (rzu) | valeur observée | décision 7.3 | source |
| --- | --- | --- | --- | --- | --- |
| 0 | `uint32` | `length` | **27** (`0x1b`) | retenu | `SFrame.exe:0x48cef0` et `:0x48da82` (`mov DWORD PTR [ebp-0x1c],0x1b`) ; `GameActionPackets.cs:478` |
| 4 | `uint16` | `id` | **260** (`0x0104`) | retenu | `SFrame.exe:0x48cee5` (`mov ecx,0x104`) ; `op_codes.md:88` |
| 6 | `byte` | `checksum` | somme des octets 0-5 | inchangé | `SFrame.exe:0x48da8c-0x48da9a` (boucle `add cl,[eax]`) ; `PacketExtensions.CalculateChecksum` |
| 7 | `uint32` (`ar_handle_t`) | `craft_item_handle` | handle d'un objet du joueur ; `0` n'est pas une sentinelle admise ici | retenu | rzu `TS_CS_SOULSTONE_CRAFT.h:6` ; `SFrame.exe:0x48da6a` (`mov [ebp-0x15],ecx` = offset 7) |
| 11 | `uint32` | `soulstone_handle[0]` | handle de pierre, `0` = châsse vide | retenu | rzu `…:7` (`_(array)(ar_handle_t, soulstone_handle, 4)`) ; `SFrame.exe:0x48da70` |
| 15 | `uint32` | `soulstone_handle[1]` | idem | retenu | idem ; `SFrame.exe:0x48da79` |
| 19 | `uint32` | `soulstone_handle[2]` | idem | retenu | idem ; `SFrame.exe:0x48da7f` |
| 23 | `uint32` | `soulstone_handle[3]` | idem | retenu | idem ; `SFrame.exe:0x48da7c` |

**Taille totale attendue : 27 octets** — `7 + 4 + 4 × 4`. Il n'y a **ni champ de compte, ni
padding, ni variante tronquée** : la trame porte toujours quatre slots, les châsses vides écrites
`0` (rzu `TS_CS_SOULSTONE_CRAFT.h:6-7` ; `GameActionPackets.cs:461-496`).

Trois sources indépendantes concordent sur 27 octets :

1. **rzu** : `_(simple)(ar_handle_t, craft_item_handle)` + `_(array)(ar_handle_t, soulstone_handle, 4)`
   (`TS_CS_SOULSTONE_CRAFT.h:6-7`) ; `ar_handle_t` est un `strong_typedef<ar_handle_t, uint32_t>`
   (`librzu/src/lib/Packet/GameTypes.h:40`), soit 4 octets par handle.
2. **NGemity** : mêmes champs en `uint32_t` (`shared/Server/Packets/GameClient/TS_CS_SOULSTONE_CRAFT.h:6-8`).
3. **Le client 7.3 lui-même** (§3.1) : son constructeur de trame annonce `0x1b` et remplit cinq
   `uint32` consécutifs.

### 3.1 Preuve directe dans le client de référence

Chaîne relevée par désassemblage statique de `SFrame.exe`
(`sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`) :

- `0x48ceb0` — **constructeur de la trame 260** : il met `length = 0x7`, calcule la somme de
  contrôle sur les 6 premiers octets, remet à zéro les octets 4 à 26 (donc **27 octets au total**),
  écrit `id = 0x104` (`0x48cee5` : `mov ecx,0x104`), puis `length = 0x1b` (`0x48cef0`) et recalcule
  la somme de contrôle (`0x48cf00-0x48cf07`).
- `0x48da50` — **seul émetteur** (`0x48da5c` : `call 0x48ceb0` ; référencé une seule fois dans
  tout le fichier) : il copie **cinq `uint32` contigus** depuis la structure argument (`+0x13`,
  `+0x17`, `+0x1b`, `+0x1f`, `+0x23`) vers les offsets **7, 11, 15, 19 et 23** de la trame
  (`0x48da6a`, `0x48da70`, `0x48da79`, `0x48da7f`, `0x48da7c`), puis réécrit `length = 0x1b`
  (`0x48da82`) et la somme de contrôle (`0x48da8c-0x48da9a`).
- `0x49e33c` — seul appelant, dans la table de dispatch de l'interface : la case **voisine**
  immédiatement après (`0x49e349`) appelle `0x48dad0`, qui construit avec `0x48cf10` une trame
  **id `0x106` = 262, longueur `0x1f` = 31** — c'est-à-dire `TM_CS_REPAIR_SOULSTONE`. Les deux
  trames de la famille « pierres d'âme » sortent donc bien du **même écran** du client, et 260 est
  celle dont la trame fait 27 octets.

Aucune donnée de capture n'existe pour ce paquet (pas de `dump` de trafic) : les « valeurs
observées » de 260 sont des identifiants choisis par le client, et `0` pour une châsse vide est la
sentinelle que NGemity teste explicitement (`WorldSession.cpp:1521`).

## 4. Gating de version

| Champ | Gating rzu | Décision pour Epic 7.3 |
| --- | --- | --- |
| id du paquet | `X(260, version < EPIC_9_6_3)` / `X(1260, version >= EPIC_9_6_3)` (`TS_CS_SOULSTONE_CRAFT.h:9-11`) | **260 retenu, 1260 non déclaré** : `EPIC_7_3 = 0x070300` (`librzu/src/lib/Packet/PacketEpics.h:59`) est inférieur à `EPIC_9_6_3 = 0x090603` (même fichier, l. 96). Le passage à 1260 est un simple décalage d'id à partir de 9.6.3 (commit `11f2b6fd`). |
| `craft_item_handle` | aucun | retenu, 4 octets |
| `soulstone_handle[4]` | aucun | retenu, 4 × 4 octets |

**Aucun champ de cette trame n'est gaté par version.** Le commit rzu `11f2b6fd`
(« packets: use versionned ID for all packets and update their ID with epic 9.6.3 ») ne touche que
la macro d'id de ce fichier (diff : 5 insertions, 1 suppression ; la macro `_DEF` est inchangée).
NGemity déclare `CREATE_PACKET(TS_CS_SOULSTONE_CRAFT, 260)` sans version, sur une base
`EPIC_4_1_1` (`shared/Common/Define.h:25`) : les deux références s'accordent sur 260. Le dépôt ne
doit donc rien gater pour ce paquet, ni ajouter 1260.

## 5. Traitement attendu

### 5.1 Ce que NGemity fait du paquet (`onSoulStoneCraft`, `WorldSession.cpp:1497-1588`)

| # | Condition | Réponse | Source |
| --- | --- | --- | --- |
| 1 | aucun contact armé (`GetLastContactLong("SoulStoneCraft") == 0`) | **rien**, retour silencieux | `:1499-1500` |
| 2 | `craft_item_handle` introuvable dans les objets du joueur | `TS_SC_RESULT` id=260, `NOT_EXIST` (1), valeur = handle | `:1503-1507` |
| 3 | `socket` du **modèle** de l'objet ∉ [1, 4] | `ACCESS_DENIED` (6), valeur = `craft_item_handle` | `:1509-1513` |
| 4 | boucle sur `nSocketCount` slots seulement (`:1520`) ; slot non nul introuvable | `ACCESS_DENIED` (6), valeur = handle du slot | `:1520-1526` |
| 5 | pierre dont `eType != 7` **ou** groupe `!= 93` **ou** classe `!= 401` | `NOT_ACTABLE` (5), valeur = handle du slot | `:1527-1531` |
| 6 | pierre de même profil (`base_type[0..3]`, `base_var[0..3][0]`, `opt_type[0..3]`, `opt_var[0..3][0]`) qu'une pierre **déjà sertie** dans un autre slot `k != i` | `ALREADY_EXIST` (9), valeur 0 ; seuil `nMaxReplicatableCount = nSocketCount == 4 ? 2 : 1` (`:1515`) | `:1533-1552` |
| 7 | aucune pierre non nulle fournie (`!bIsValid`) | `INVALID_ARGUMENT` (28), valeur 0 | `:1557-1561` |
| 8 | or insuffisant pour `nCraftCost` | `NOT_ENOUGH_MONEY` (10), valeur 0 | `:1562-1565` |
| 9 | succès | `207` avec l'objet mis à jour **et** `0`/`SUCCESS` (valeur 0) | `:1567-1587` |

Cœur métier, tel que le code l'écrit :

- **coût** : `nCraftCost += pSoulStoneList[i]->GetItemTemplate()->price / 10` (`:1553`), débité en
  **or** (`:1562`, `ChangeGold`) — division entière, une fois par slot fourni ;
- **écriture de la châsse** : `pItem->GetItemInstance().SetSocketIndex(i, pSoulStoneList[i]->GetItemInstance().GetCode())`
  (`:1572`) — c'est un **code d'objet** (`ItemResourceId`), pas un handle ;
- **consommation** : `m_pPlayer->EraseItem(pSoulStoneList[i], 1)` (`:1573`), puis
  `Save(false)` + `DBUpdate()` (`:1582-1583`) ;
- **endurance** : `nEndurance += pierre.GetCurrentEndurance()` pour chaque slot pourvu, `+=`
  de l'endurance **courante de l'objet** pour chaque châsse déjà occupée non re-fournie
  (`:1569-1578`), puis `pItem->SetCurrentEndurance(nEndurance)` (`:1581`) ;
- **désarmement** : `SetLastContact("SoulStoneCraft", 0)` (`:1584`) ;
- **stats** : `CalculateStat()` (`:1586`).

### 5.2 Ce que le serveur doit répondre

- Toute réponse passe par `TS_SC_RESULT` (id `0`, `request_msg_id = 260`, `result`, `value`) :
  `Game/Network/Packets/Enums/GamePackets.cs:5` et `Game/Network/Clients/GameClient.cs:68-72`
  (`SendResult`) ; NGemity `shared/Server/Packets/GameClient/TS_SC_RESULT.h:6-11` et
  `Messages.cpp:361-371`. Les codes de `ResultCode`
  (`Game/Network/Packets/ResultCode.cs:6-37`) sont identiques à ceux de NGemity
  (`shared/Server/TS_MESSAGE.h:55-83`) : `NotExist = 1`, `NotActable = 5`, `AccessDenied = 6`,
  `AlreadyExist = 9`, `NotEnoughMoney = 10`, `InvalidArgument = 28`, `Success = 0`.
- En cas de succès **seulement**, l'objet modifié est renvoyé par `TM_SC_INVENTORY` (207) :
  NGemity `Messages::SendItemMessage` (`Messages.cpp:250-259`) ; dans le dépôt,
  `GameCharacterPackets.BuildInventory(ItemEntity[])` (`GameCharacterPackets.cs:130-149`) écrit des
  enregistrements de `InventoryItemSize = 75 + 10` octets (`:21`) avec les quatre châsses aux
  offsets 38, 42, 46 et 50 du motif de 75 octets
  (`Game/Network/Packets/Game/ItemFixedInfoWriter.cs:73-75` et `:97-100`), alimentées par
  `ItemEntity.SocketItemIds` (`Game/DataAccess/Entities/Telecaster/ItemEntity.cs:35`).
- **Il n'existe pas de paquet de résultat propre à 260** : ni rzu ni NGemity n'en déclarent, et le
  client n'en attend pas (son écran se referme sur `207` + `0`).

### 5.3 État du dépôt à `b56967a` (ce qui est déjà là)

- `TM_CS_SOULSTONE_CRAFT = 260` déclaré (`GamePackets.cs:58`) et un bras unique couvre 256, 260,
  262, 263, 264 (`GameClient.cs:1617-1630`) : aucun membre de `GamePackets` n'atteint le `throw`
  final.
- Le socle lit la trame à sa taille 7.3 (`GameActionPackets.TryReadSoulstoneCraft`,
  `GameActionPackets.cs:475-496`), résout les handles non nuls (`CraftingSocleRules.cs:34-49`),
  puis **refuse tout** par `InvalidArgument` (`CraftingSocleService.cs:120-126`). Rien du §5.1
  n'est implémenté : ni coût, ni règle des deux pierres, ni écriture de châsse, ni `207`.
- Tests existants à ne pas casser : `Tests/Game/CraftingSoclePacketsTests.cs:191-243`
  (offsets, sentinelle nulle, refus 26/27/28 octets), `:387-389` (handles nommés).

### 5.4 La garde de contact — ce que le dépôt en a déjà, et ce qui manque

NGemity arme la garde dans `Messages::ShowSoulStoneCraftWindow`
(`Messages.cpp:943-948` : `SetLastContact("SoulStoneCraft", 1)` **et** envoi de
`TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW`), appelée par la liaison Lua
`show_soulstone_craft_window` (`Scripting/XLua.cpp:104`, `:917-922`). Le nom exact du déclencheur
est celui du catalogue du dépôt (§2.2).

**L'équivalent fonctionnel existe déjà** : `ConnectionInfo.NpcDialogHandle` et
`ConnectionInfo.NpcDialogTriggers` (`Game/Network/Clients/ConnectionInfo.cs:209-210`), armés par
`NpcDialogService.TryShow` (`Game/Services/NpcDialogService.cs:158-171`) et vérifiés à la sélection
suivante (`:92`). **Mais il est effacé au moment même où le joueur choisit l'entrée** : le
déclencheur `show_soulstone_craft_window()` n'a aucun gestionnaire dans `_dialogs`, donc
`TryShow` échoue et `ClearNpcDialog()` vide l'armement (`NpcDialogService.cs:140-148`, journal
`"NPC dialog action {function} is not implemented yet"`).

Conclusion **tranchée pour ce cycle** : la garde est due, mais son **point d'armement appartient au
lobe « ouverture de fenêtre »** (fiche de socle §9.4), pas au gestionnaire de 260. Le traitement de
260 ne doit donc **pas** inventer un état de contact : il reste joignable dès que le client sait
envoyer la trame, et l'écart est documenté (§6.1). Une garde branchée aujourd'hui sur
`NpcDialogTriggers` refuserait **100 %** des requêtes légitimes, puisque l'armement est effacé juste
avant et que la fenêtre ne peut pas s'ouvrir sans le paquet d'ouverture (id non établi, §7.1). La
réserve est portée en `## A VERIFIER PAR KILLIAN` (point 5).

### 5.5 Ce que le moteur devra décider, et avec quelle source

| Décision | Valeur retenue | Source | Statut |
| --- | --- | --- | --- |
| châsses disponibles | `ItemResourceEntity.SocketCount` (1 à 4) | NGemity `ItemTemplate.hpp:388` (`socket`) ; `Game/DataAccess/Entities/Arcadia/ItemResourceEntity.cs:26` ; colonne `socket` `ArcadiaSchemaPSQL.sql:2198` ; mapping `MigrateDatabase/Mappers/ArcadiaResourcesMappingProfile.cs:48` | établi |
| « est une pierre d'âme » | `ItemBaseType.Soulstone = 7` **et** `ItemGroup.Soulstone = 93` **et** `ItemType.Soulstone = 401` | NGemity `TYPE_SOULSTONE = 7`, `GROUP_SOULSTONE = 93`, `CLASS_SOULSTONE = 401` (`ItemTemplate.hpp:329`, `:356`, `:297`) ; dépôt `ItemBaseType.cs:12`, `ItemGroup.cs:27`, `ItemType.cs:43` ; mapping `item.type` → `ItemBaseType`, `item.group` → `Group`, `item.Class` → `ItemType` (`ArcadiaResourcesMappingProfile.cs:37-39`) | établi (voir §6.4) |
| contenu de la châsse | **code d'objet** (`ItemResourceId`), écrit en `int32` | NGemity `:1572` (`SetSocketIndex(i, GetCode())`) et `:1536` (relu puis passé à `GetItemBase`, qui indexe une ressource par code) ; champ rzu neutre (`TS_SC_INVENTORY.h:7-9`) | retenu, réserve §7.2 |
| coût du sertissage | `Σ price / 10` en or, division entière | NGemity `:1553`, `:1562` ; `ItemResourceEntity.Price` (`ItemResourceEntity.cs:34`) ; existence d'un coût affirmée par le client (`db_string.rdb:198039`) | **valeur de game design** → A VERIFIER 2 |
| règle des deux pierres | identiques interdites ; seuil 2 si `SocketCount == 4`, sinon 1 | NGemity `:1515`, `:1533-1552` ; texte client `db_string.rdb:198045` | établi sur le principe, seuil à confirmer (§7.5) |
| endurance de l'objet après sertissage | `Σ` des endurances des pierres portées, les châsses déjà occupées étant re-comptées par l'endurance courante de l'objet | NGemity `:1567-1581` | **non porté tel quel** → §6.3 |
| sort des pierres | consommées (`EraseItem(pierre, 1)`) | NGemity `:1573` | établi |
| réponse | `207` + `0`/`Success` | §5.2 | établi |

### 5.6 Base mesurée avant livraison (24/09/2026)

```
export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
dotnet build Navislamia.sln -c Debug     → code de sortie 0 (0 erreur)
dotnet test  Tests/Tests.csproj          → code de sortie 0 — Failed: 0, Passed: 1302, Skipped: 0, Total: 1302
git log --oneline origin/master..master  → vide (master = origin/master = b56967a)
```

C'est la base du socle déjà livré : le compte de tests doit **monter**, jamais baisser. Cette fiche
ne modifie aucun fichier de code.

## 6. Écarts assumés avec NGemity

1. **Garde de contact** : NGemity retourne silencieusement sans contact armé (`:1499-1500`) ; le
   traitement de 260 ne la porte pas dans ce cycle, parce que le point d'armement du dépôt est
   effacé par le chemin de repli des dialogues (`NpcDialogService.cs:140-148`) et que la fenêtre ne
   peut pas s'ouvrir sans paquet d'ouverture. Écart **volontaire et documenté** (§5.4, §7.1) ;
   NGemity n'est pas contredit sur le fond, seulement différé.
2. **Boucle sur `nSocketCount`** (`:1520`) : NGemity n'examine que les slots du modèle de l'objet.
   La trame, elle, porte toujours quatre handles (rzu `:7`, client §3.1). Le moteur doit donc
   **ignorer** les slots au-delà de `SocketCount` — pas les refuser : un client qui écrit quatre
   slots sur un objet à deux châsses ne doit pas provoquer d'erreur. C'est une lecture, pas une
   observation de client (§7.6).
3. **Endurance** : la règle de `:1569-1578` (re-compter l'endurance courante de l'objet pour chaque
   châsse déjà occupée non re-fournie) est un héritage peu lisible, dont l'effet côté client n'est
   pas établi ; elle n'est **pas** reprise comme acquis. Le moteur doit la trancher explicitement
   (§7.3).
4. **Correspondance des trois axes d'objet — la fiche de socle se trompe ici** :
   `socle-artisanat-objets.md` §6.2 écrit que « les `Class`/`Group`/`Type` exigés n'ont pas
   d'équivalent nommé dans `ItemResourceEntity` ». C'est inexact : le dépôt porte bien les trois
   axes, avec **exactement** les valeurs de NGemity —
   `ItemBaseType.Soulstone = 7` ← colonne `type` (`ItemBaseType.cs:12`,
   `ArcadiaResourcesMappingProfile.cs:37`), `ItemGroup.Soulstone = 93` ← colonne `group`
   (`ItemGroup.cs:27`, mapping `:38`), `ItemType.Soulstone = 401` ← colonne `Class`
   (`ItemType.cs:43`, mapping `:39`). Le contrôle `:1527-1528` est donc implémentable tel quel ; la
   réserve §7 du socle sur ce point est levée par la présente fiche.
5. **Comparaison « même stat »** (`:1537-1544`) : NGemity ne compare que la **première** colonne de
   valeur (`base_var[k][0]`, `opt_var[k][0]`) ; le dépôt expose deux colonnes
   (`BaseVar1`/`BaseVar2`, `ItemResourceEntity.cs:42-48`). Comportement retenu : reproduire la
   référence (index 0 seul) et le documenter, plutôt que d'élargir la comparaison.
6. **`ChangeGold(...) != TS_RESULT_SUCCESS`** (`:1562`) compare un solde à un code de résultat :
   bizarrerie de la référence, sans effet pour nous — le dépôt débite et répond `Success` ou
   `NotEnoughMoney`.
7. **259 est une collision de sens, pas de valeur** : NGemity envoie `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW`
   (valeur d'énumération 259) **du serveur vers le client**, alors que `TS_CS_DONATE_REWARD = 259`
   (`shared/Server/ClientPackets.h:99-100`) va du **client vers le serveur**. Sur le fil, les deux
   sens sont des espaces distincts ; seul un tableau plat (`op_codes.md`, `GamePackets`) ne peut pas
   porter les deux (§7.1).

## 7. NON ÉTABLI

1. **Id du paquet d'ouverture de la fenêtre de sertissage.** rzu et NGemity le déclarent sur 259
   (`TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW.h:7-9`, `ClientPackets.h:100`), `op_codes.md:86` met
   `TM_CS_DONATE_REWARD` sur 259 et **n'a aucune entrée** pour la fenêtre. Question précise : l'id
   entrant (serveur → client) 259 ouvre-t-il `window_Socket.nui` en 7.3 ? Non tranché ici : le seul
   moyen de le prouver serait de retrouver le bras de réception du client, ce que cette fiche n'a
   pas fait. Conséquence : **260 n'est pas atteignable en jeu** tant que l'ouverture n'est pas
   livrée, même si la trame et sa réponse sont complètement spécifiées.
2. **Contenu de la châsse de `TS_SC_INVENTORY` (code d'objet ou handle).** Retenu : code d'objet
   (§5.5), sur la foi de NGemity (`:1572`, `:1536`). Aucune observation directe du client 7.3 sur
   ce point ; l'argument de nécessité (« la pierre est effacée (`:1573`) **avant** l'envoi de
   l'objet (`:1585`), donc un handle ne résoudrait plus rien quand le client affiche la châsse, et
   l'icône d'une pierre sertie vient de `db_item`, indexée par code ») reste un raisonnement, pas
   une preuve. Voir A VERIFIER 1.
3. **Sémantique de l'endurance après sertissage** (§6.3) : la référence ajoute ensemble des valeurs
   d'endurance sans dire l'unité affichée. Le client a pourtant des libellés dédiés
   (`#@Soulstone_Durability_test@#`, `SFrame.exe`, dump l. 21521-21522 ; `#@socket_empty@#`,
   l. 21515) — leur alimentation n'est pas déterminée.
4. **Le taux `price / 10`** : source unique (NGemity, base ≤ 4.1.1). Le client confirme qu'un coût
   existe et qu'il n'est pas du Lak (`smsg_soket02` contre `smsg_soket10`), pas son montant.
5. **Seuil de duplication pour les objets à 2 ou 3 châsses** : la référence n'autorise 2 pierres
   identiques que si `SocketCount == 4` (`:1515`) ; le texte client dit « not more than 2 … in the
   same weapon » sans distinguer la taille (`db_string.rdb:198045`). Les deux lectures ne se
   contredisent pas, mais ne disent pas la même chose pour un objet à 3 châsses.
6. **Slots non nuls au-delà de `SocketCount`** : le client n'est jamais tronqué (trame fixe de 27
   octets), mais rien n'établit s'il peut remplir le 3ᵉ slot d'un objet à deux châsses. Le client
   semble indexer par `Socket 1..4` (`db_string.rdb:112380-112388`).
7. **Valeur du champ `craft_item_handle` à `0`** : NGemity teste `FindItemByHandle(0)` comme
   n'importe quel handle (`:1503`) et répond `NOT_EXIST` ; aucune sentinelle « pas d'objet » n'est
   prévue, contrairement aux quatre slots (`:1521`). À ne pas « améliorer » sans source.

## 8. Commits épinglés

| Référence | Commit | Date de lecture |
| --- | --- | --- |
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` — « packets: fix TS_SC_INVENTORY with older epics » | 24/09/2026 |
| `reference/ngemity` (Chihiro) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` — « Fix compilation issue for GCC » | 24/09/2026 |
| `reference/client73` — `SFrame.exe` | `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | 24/09/2026 |
| `reference/client73` — `db_string.rdb` | `sha256 4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` | 24/09/2026 |
| `Navislamia` (base) | `b56967a` | 24/09/2026 |

Commits rzu utiles : `11f2b6fd` (re-numérotation 9.6.3 du seul id de 260), `05bc2d82`
(`ar_handle_t` en `strong_typedef` sur `uint32_t`, sans effet sur le fil).

### Reproduction des relevés client

```bash
cd /srv/navislamia/reference/client73
strings -n 4 SFrame.exe    > /tmp/sf.dump    # n° de ligne cités en §2, §3.1, §5.5
strings -n 4 db_string.rdb > /tmp/dbs.dump   # n° de ligne cités en §2, §5.5, §7
objdump -d --start-address=0x48ceb0 --stop-address=0x48cf10 -M intel SFrame.exe   # trame 260
objdump -d --start-address=0x48da50 --stop-address=0x48daa0 -M intel SFrame.exe   # émetteur
```

## A VERIFIER PAR KILLIAN

1. **Le contenu de la châsse de `TS_SC_INVENTORY`** : la fiche tranche « code d'objet » (§5.5, §6.4,
   §7.2) d'après NGemity (`:1572`, `:1536`) et par nécessité (la pierre est effacée avant l'envoi).
   Si un jour une pierre sertie s'affiche vide ou montre la mauvaise icône dans le client 7.3, c'est
   ici qu'il faut regarder en premier : le remède est de relire le bras de réception du client, pas
   de changer au hasard.
2. **Le coût `price / 10`, en or** (§5.5) : valeur de *game design* reprise de NGemity, base ≤ 4.1.1,
   sans seconde source. La garder, la changer, ou la porter en table ?
3. **L'endurance de l'objet après sertissage** (§6.3) : la référence re-compte l'endurance des
   châsses déjà occupées (`:1569-1578`) d'une façon dont l'effet client n'est pas établi. Reproduire
   à l'identique, ou définir « endurance de l'objet = somme des pierres portées » ?
4. **Le seuil de duplication** (§7.5) : 2 pierres identiques seulement sur un objet à 4 châsses
   (référence `:1515`) ou dès qu'il y a la place (texte client) ?
5. **La garde de contact** (§5.4) : la fiche recommande de **ne pas** la porter dans le cycle de
   260, parce que le point d'armement du dépôt est effacé par le repli des dialogues
   (`NpcDialogService.cs:140-148`) et que la fenêtre ne peut pas s'ouvrir faute d'id d'ouverture.
   Si vous voulez la garde tout de suite, elle exige une modification de `NpcDialogService`
   (laisser le dialogue courant, comme le fait le bras marchand `:130-138`) — donc une autre carte.
6. **L'ouverture de la fenêtre** (§7.1) : tant que l'id entrant n'est pas tranché, 260 reste
   **inobservable en jeu**, et la QA ne pourra le vérifier que par tests unitaires. Confirmez-vous
   que cette carte reste ouverte pour l'ouverture, ou faut-il la traiter ici en acceptant 259 ?
7. **Rectification à porter au socle** (§6.4) : `socle-artisanat-objets.md` §6.2 et §7 affirment
   que la correspondance `CHECK_ITEM_CLASS` → colonne `class` reste « à établir ». Elle est établie
   (trois axes, valeurs identiques à NGemity). Faut-il amender le socle, ou la présente fiche
   suffit-elle comme correctif ?

## Bloc prêt à coller dans `CLAUDE.md`

````markdown
### `TM_CS_SOULSTONE_CRAFT` (260)

Fiche complète : `docs/packet-specs/260-soulstone-craft.md`.

- **27 octets fixes**, sans champ de compte ni padding : 7 d'en-tête, `craft_item_handle` à
  l'offset 7, puis `soulstone_handle[0..3]` aux offsets 11, 15, 19, 23, châsses vides écrites `0`.
  Trois sources concordent (rzu `TS_CS_SOULSTONE_CRAFT.h:6-7`, NGemity `…:6-8`, et le constructeur
  de trame du client `SFrame.exe:0x48ceb0` qui annonce `0x1b` et copie cinq `uint32`).
- **Aucun gating de version** : 260 vaut pour 7.3, 1260 est le décalage 9.6.3 (`EPIC_9_6_3 =
  0x090603` > `EPIC_7_3 = 0x070300`) et **n'est pas déclaré**.
- **Réponses** : `TM_SC_RESULT` (0) avec `request_msg_id = 260` pour toute issue, et
  `TM_SC_INVENTORY` (207) portant l'objet mis à jour **seulement** en cas de succès. Pas de paquet
  de résultat dédié.
- **Contenu de la châsse = code d'objet**, pas un handle (NGemity écrit `GetCode()` puis relit la
  châsse via une table indexée par code) : retenu faute de contre-preuve, réserves en fin de fiche.
- **La garde de contact n'est pas portée** : son point d'armement est effacé par le repli des
  dialogues (`NpcDialogService.cs:140-148`) et la fenêtre de sertissage n'a **pas d'id d'ouverture
  établi** (259 est contradictoire : `TM_CS_DONATE_REWARD` côté `op_codes.md`, fenêtre côté rzu et
  NGemity — collision entre deux sens, pas entre deux paquets). 260 n'est donc pas joignable en jeu
  tant que l'ouverture n'est pas livrée.
- **Correction au socle** : les trois axes d'objet de NGemity existent dans le dépôt avec les mêmes
  valeurs (`ItemBaseType.Soulstone = 7`, `ItemGroup.Soulstone = 93`, `ItemType.Soulstone = 401`) —
  la réserve du socle §6.2 sur `CHECK_ITEM_CLASS` est levée.
````
