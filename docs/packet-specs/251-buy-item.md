# 251 — `TM_CS_BUY_ITEM` (achat chez un marchand)

Fiche du lot `navislamia:packet:251`, branche `hermes/packet-251-buy-item`, base
`b56967a07430422add88e0e5cdf292b41b18f6c6` (`origin/master`, « Merge pull request #44 »).
Fiche de socle de référence : `docs/packet-specs/socle-marche-npc.md` (l'ouverture de la
fenêtre, `TM_SC_MARKET` 250, y est tranchée ; elle n'est pas réécrite ici).

Périmètre : **251 seul**. `TM_CS_SELL_ITEM` (252) a sa propre carte (`RamDNqcl`) ; il n'est cité
ici que comme contre-épreuve de lecture.

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| Id décimal | `251` | `op_codes.md:78` (`[251] = "TM_CS_BUY_ITEM"`) |
| Id 9.6.3+ | `1251` — **hors 7.3, à ne jamais déclarer** | rzu `TS_CS_BUY_ITEM.h:14-15` |
| Nom | `TM_CS_BUY_ITEM` | idem |
| Sens | client → serveur | rzu `TS_CS_BUY_ITEM.h:17` (`SessionPacketOrigin::Client`) |
| Taille sur le fil | **13 octets** (7 d'en-tête + 6 utiles) | §3 |
| Origine de l'id | `0xfb` écrit en clair par le client (`0x48f3a4`) | §3 |

Le nom du paquet existe dans le client : chaîne `TM_CS_BUY_ITEM` dans le vivier de noms de
`SFrame.exe`, offset fichier `0x65207c` (relevé `strings -t x`, ce lot). Ce vivier n'est référencé
par aucun pointeur trouvé — il ne prouve pas l'émission, il la rend plausible ; la preuve
d'émission est en §3.

## 2. Ce que le joueur fait pour que le client l'envoie

1. Le joueur ouvre la fenêtre de commerce : clic sur un PNJ marchand, puis sur l'entrée de
   dialogue `open_market(` → `TM_SC_MARKET` (250) ouvre la fenêtre (chaîne complète et sources :
   `docs/packet-specs/socle-marche-npc.md:35-62`).
2. Le joueur sélectionne une ou plusieurs lignes du catalogue et valide l'achat.

**Ce qui est établi par lecture statique de `SFrame.exe`** (sha256
`41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 octets) :

- l'émetteur de 251 est la fonction `0x48f380` (prologue `55 8b ec 83 ec 10`) ; elle **boucle**
  sur un vecteur d'éléments de **12 octets** (`[ebx+0x13]` = début, `[ebx+0x17]` = fin,
  `add esi,0xc` en `0x48f3f7`) et **envoie une trame 251 par élément** — le client découpe donc un
  achat multi-lignes en autant de 251, pas en un seul paquet ; seuls les 6 premiers octets de
  chaque élément sont lus (`mov eax,[esi]` puis `mov cx,word [esi+4]`) ;
- appel unique depuis `0x49e2f7`, qui est le bras de la fonction de dispatch de messages d'UI
  `0x49cd00` pour l'id interne **`0x416` (1046)** : table de commutation en `0x49e20c`
  (`sub eax,0x403`, `movzx eax,byte [eax+0x49ea50]`, `jmp [eax*4+0x49e98c]`), entrée `0x416` →
  `0x49e2f4` (relevé par lecture des deux tables) ;
- la fonction jumelle `0x48f410`, appelée en `0x49e304` (bras **`0x418` = 1048**), émet 252 avec
  la même forme d'en-tête et de charge utile (`mov eax,0xfc` en `0x48f434`, longueur `0xd` en
  `0x48f43d`, `dword` en `+7` en `0x48f46a`, `word` en `+11` en `0x48f46d`) — deux paquets voisins
  lus par la même méthode, ce qui recoupe le résultat ;
- l'envoi passe par un appel indirect de la connexion : `[edi+0xb8]` = objet de connexion,
  `[edx+0xc4]` = envoi (`0x48f473-0x48f485`) ;
- la fenêtre concernée est celle du commerce : chaînes `SUIShopWnd::RefreshItems` (`0x62e9c4`),
  `SUIShopKartWnd::IMSG_UI_SEND_DATA` (`0x62ea4c`), `Create : SUIShopWnd` (`0x648928`),
  `window_business_shop.nui` (`0x643584`).

**`NON ÉTABLI`** : le nom de commande de l'UI (ou le bouton `.nui`) qui porte l'id interne `1046`.
Le vivier de noms de commandes (`close_all_message_box`, `Ranking_Top_Record`, `close_item_shop`,
`drop_quest`…, offsets fichier `0x61eb6c`-`0x61ec78`) ne contient aucune entrée d'achat, et aucun
pointeur vers ces chaînes n'a été trouvé dans le fichier (recherche du motif `68 <VA>` et du
pointeur 32 bits) : la table id ↔ nom n'est pas lisible statiquement. Le fait « clic d'achat dans
la fenêtre de commerce » reste établi par les chaînes et par la structure d'émission ci-dessus.

## 3. Structure sur le fil — **13 octets**

| Offset | Type | Nom | Valeur observée | Source |
| ---: | --- | --- | --- | --- |
| 0 | `uint32` | taille totale | `13` | client : `mov dword [ebp-0x10],0xd` en `0x48f3ad` ; dépôt : `GameCharacterPackets.cs:383-388` (`CreatePacket`) |
| 4 | `uint16` | id | `251` (`0xfb`) | client : `mov eax,0xfb` `0x48f3a4` + `mov word [ebp-0xc],ax` `0x48f3a9` ; rzu `TS_CS_BUY_ITEM.h:14` (`X(251, version < EPIC_9_6_3)`) ; `op_codes.md:78` |
| 6 | `uint8` | somme de contrôle | somme des octets 0 à 5 | client : boucle `0x48f3c0`-`0x48f3c8` (`add cl,[eax]`, borne `[ebp-0xa]` = trame+6), écriture `mov byte [ebp-0xa],cl` en `0x48f3d3` ; dépôt : `GameCharacterPackets.cs:391-396` (`WriteChecksum`) |
| 7 | `int32` | `item_code` | code d'objet du catalogue | client : `mov dword [ebp-0x9],eax` en `0x48f3da` (valeur prise en `[esi]`) ; rzu `TS_CS_BUY_ITEM.h:8` |
| 11 | `uint16` | `buy_count` | quantité demandée | client : `mov word [ebp-0x5],cx` en `0x48f3dd` (valeur prise en `[esi+4]`) ; rzu `TS_CS_BUY_ITEM.h:9` |
| 13 | — | fin de trame | — | la trame locale est allouée en `[ebp-0x10]` et zéro-remplie sur **13** octets (`0x48f396`-`0x48f3a3` : trois `mov dword` puis `mov byte`, soit 4+4+4+1) |

**En-tête de 7 octets confirmée.** Les trois champs d'en-tête du dépôt (`Length` 0-3, `ID` 4-5,
`Checksum` 6 — `Game/Network/Clients/ConnectionInfo.cs` n'est pas concerné ; voir
`Game/Network/Packets/Header.cs:9-11` et `Game/Network/Packets/Game/GameCharacterPackets.cs:19`,
`HeaderSize = 7`) coïncident **octet pour octet** avec la trame construite par le client :
le client écrit la longueur totale (13, en-tête comprise) en 0, l'id en 4 et la somme de
contrôle en 6, exactement comme `CreatePacket`/`WriteChecksum`. La somme de contrôle du client
porte sur les 6 premiers octets, comme `WriteChecksum`.

Taille utile : **6 octets**, sans préfixe de longueur ni remplissage — les champs `_(simple)` de
rzu s'ajoutent sans en-tête de champ (`PacketDeclaration.h:529-536`, `getSize` = `size_base_`
cumulé), et la disposition ci-dessus est celle que le client écrit réellement.

### 3.1 Trames de la réponse (tailles à respecter)

| Paquet | Taille | Disposition | Source |
| --- | ---: | --- | --- |
| `TM_SC_RESULT` (0) | **15** | `request_msg_id` `uint16` @7, `result` `uint16` @9, `value` `int32` @11 | rzu `TS_SC_RESULT.h:8-10` ; `TS_SC_RESULT.cs:6-17` ; `GameClient.cs:68-72` (`Packet<T>` = 7 + `Marshal.SizeOf`) ; `Packet.cs:31-34` |
| `TM_SC_NPC_TRADE_INFO` (240) | **36** | `is_sell` `int8` @7, `code` `int32` @8, `count` `int64` @12, `price` `int64` @20, `huntaholic_point` `int32` @28, `target` `uint32` @32 | rzu `TS_SC_NPC_TRADE_INFO.h:8-14` ; `GameTradePackets.cs:39` (`NpcTradeInfoSize = 36`), `:83-96` |
| `TM_SC_GOLD_UPDATE` (1001) | **19** | `gold` `uint64` @7, `chaos` `uint32` @15 | rzu `TS_SC_GOLD_UPDATE.h:8-11` ; `GameCharacterPackets.cs:239-246` (`HeaderSize + 12`) |

## 4. Gating de version — tranché pour 7.3

`EPIC_7_3 = 0x070300` (rzu `librzu/src/lib/Packet/PacketEpics.h:59`) ; `EPIC_4_1 = 0x040100`
(`:50`) ; `EPIC_9_6_3 = 0x090603` (`:96`).

| Champ | Gating rzu | Décision pour 7.3 |
| --- | --- | --- |
| id `251` / `1251` | `X(251, version < EPIC_9_6_3)` / `X(1251, version >= EPIC_9_6_3)` (`TS_CS_BUY_ITEM.h:14-15`) | **251** : `0x070300 < 0x090603`. Déclarer 1251 serait faux. |
| `item_code` | aucun gating (`:8`) | `int32`, présent |
| `buy_count` | `_(def)(simple)(uint16_t, buy_count)` puis `_(impl)… uint16_t … version >= EPIC_4_1` / `_(impl)… uint8_t … version < EPIC_4_1` (`:9-11`) | **`uint16`** : `0x070300 >= 0x040100`. La variante `uint8` est morte pour 7.3 — et le client écrit bien 2 octets (`mov word [ebp-0x5],cx`). |

Aucun autre champ n'est gaté : la charge utile est de 6 octets en 7.3, sans variante.

## 5. Traitement attendu

### 5.1 Ce que fait la référence (`Chihiro`)

`onBuyItem` — `reference/ngemity` `Chihiro/src/Network/GameNetwork/WorldSession.cpp:731-815`,
déclaré en `WorldSession.h:109` et enregistré pour `STATUS_AUTHED` en `WorldSession.cpp:112`.
Le paquet est désérialisé avec `item_code` `int32` et `buy_count` `uint16` (NGemity
`shared/Server/Packets/GameClient/TS_CS_BUY_ITEM.h:6-12` ; oui, `ClientPackets.h:91`
`TS_CS_BUY_ITEM = 251`).

1. `buy_count == 0` → `TS_SC_RESULT` de refus, code `TS_RESULT_UNKNOWN` (7), valeur `0` (`:735-738`).
2. Marché vide ou introuvable (`GetLastContactStr("market")` en `:733`, `GetMarketInfo` en `:740`) →
   même refus 7 (`:741-744`).
3. Balayage des lignes du marché (`:749`) ; la ligne dont `code == item_code` est traitée (`:750`) ;
   un `item_code` sans base d'objet connue est ignoré (`continue`, `:751-753`) ; une ligne d'objet
   non empilable (`FLAG_DUPLICATE != 1`) force `buy_count = 1` (`:754-761`).
4. Prix total : `(int32_t)floor(buy_count * mt.price_ratio)` (`:763`) ; garde
   `nTotalPrice / buy_count != mt.price_ratio || or < nTotalPrice || nTotalPrice < 0`
   → refus `TS_RESULT_NOT_ENOUGH_MONEY` (10), valeur `0` (`:764-767`).
5. Poids : `nMaxWeight - poids courant < poids de l'objet × buy_count` → refus
   `TS_RESULT_TOO_HEAVY` (11), valeur `item_code` (`:769-772`).
6. Débit de l'or par `ChangeGold` (`:775-779` ; `Player.cpp:1132-1143`, qui envoie lui-même la mise
   à jour d'or et de chaos) — un code de retour non nul est réexpédié tel quel (`:776-778` ; les
   deux seuls codes possibles sont `TS_RESULT_TOO_MUCH_MONEY` 53 et `TS_RESULT_TOO_CHEAP` 50,
   `Player.cpp:1135-1138`, `TS_MESSAGE.h:103, 106`, inatteignables à l'achat puisque l'or ne peut
   que diminuer) ; ajout de l'objet (`:781-803` : une pile de `buy_count` si empilable `:782-790`,
   sinon `buy_count` objets unitaires `:792-802`) ; puis, **dans cet ordre** :
   `TS_SC_RESULT` succès (code `TS_RESULT_SUCCESS` = 0, valeur `item_code`, `:804`) **puis** l'écho
   `TS_SC_NPC_TRADE_INFO` (240) avec `is_sell = false`, `code = item_code`, `count = buy_count`,
   `price = nTotalPrice`, `huntaholic_point = mt.huntaholic_ratio`,
   `target = GetLastContactLong("npc")` (`:805-812`).
7. **Objet absent du marché : aucune réponse.** La boucle `for (auto &mt : *market)` se termine
   sans `else` (`:749-814`) : ni refus, ni écho. C'est la conduite de la référence, pas un oubli
   de lecture.
8. **La boucle ne s'interrompt pas après une correspondance** : ni `break` ni `return` après
   l'écho (`:805-812`), donc deux lignes de marché portant le même `code` achètent et répondent
   **deux fois** (`:749-814`). Quirk de la référence, porté en `NON ÉTABLI` §7 point 8.

Codes : `TS_MESSAGE.h:55` (`TS_RESULT_SUCCESS = 0`), `:62` (`TS_RESULT_UNKNOWN = 7`),
`:65` (`TS_RESULT_NOT_ENOUGH_MONEY = 10`), `:66` (`TS_RESULT_TOO_HEAVY = 11`).
Le message de refus est `Messages::SendResult(Player *, uint16_t nMsg, uint16_t nResult, uint32_t
nValue)` (`Messages.h:58-61`, `Messages.cpp:361-371`) : il recopie `request_msg_id = nMsg`.
L'id transmis par le gestionnaire est `pRecvPct->getReceivedId()` — **l'id réellement reçu de la
trame**, donc 251 (`PacketDeclaration.h:525-528`).

### 5.2 Ce que `Navislamia` doit répondre

Pour toute trame 251 valide (id 251, longueur **13**, somme de contrôle déjà vérifiée en amont par
la boucle de réception, `GameClient.cs:1234-1263`) et pour une ligne de catalogue vendable :

| Ordre | Paquet | Id | Taille | Contenu attendu |
| ---: | --- | ---: | ---: | --- |
| 1 | mise à jour d'or | 1001 | 19 | or après débit, chaos inchangé (patron du dépôt : `GmCommandService.cs:203`, `CombatService.cs:258`) |
| 2 | résultat | 0 | 15 | `request_msg_id = 251`, `result = 0`, `value = item_code` |
| 3 | écho de transaction | 240 | 36 | `is_sell = 0`, `code = item_code`, `count = buy_count`, `price =` total débité, `huntaholic_point =` celui de la ligne, `target =` handle du PNJ marchand |

Le constructeur de 3 est **déjà livré** et n'a pas à être réécrit : `GameTradePackets.cs:83-96`
(`BuildNpcTradeInfo`, `NpcTradeInfoSize = 36`). Les ordres 2 puis 3 sont ceux de la référence
(`WorldSession.cpp:804-812`) ; la place de 1 dans cet ordre n'est pas établie (`NON ÉTABLI` §7).

Refus, chacun **sourcé** sur la référence (aucune constante choisie ici) :

| Situation | Réponse | Source |
| --- | --- | --- |
| `buy_count == 0` | `TS_SC_RESULT(TM_CS_BUY_ITEM, 7, 0)`, rien d'autre | `WorldSession.cpp:735-738` |
| marché non ouvert / inconnu | `TS_SC_RESULT(TM_CS_BUY_ITEM, 7, 0)` | `WorldSession.cpp:740-744` |
| or insuffisant (ou total négatif) | `TS_SC_RESULT(TM_CS_BUY_ITEM, 10, 0)` | `WorldSession.cpp:764-767` |
| dépassement de capacité | `TS_SC_RESULT(TM_CS_BUY_ITEM, 11, item_code)` — **non applicable ici**, §6 |
| objet absent du marché | **aucune réponse** (journal seulement) | `WorldSession.cpp:749-814` |
| longueur ≠ 13 | trame refusée (journal), rien n'est lu ni renvoyé | patron du dépôt : `GameActionPackets.cs:65-76`, `GameXtrapPackets.cs:53` |

Les constantes existent dans le dépôt : `Game/Network/Packets/ResultCode.cs:6` (`Success = 0`),
`:14` (`Unknown = 7`), `:17` (`NotEnoughMoney = 10`), `:19` (`TooHeavy = 11`), `:37`
(`InvalidArgument = 28`, déjà employé pour un argument illisible, `GameClient.cs:990`).

### 5.3 Ce que le socle a livré, ce qui manque (chemins vérifiés sur `b56967a`)

Livré, à **utiliser** et non à réécrire :

| Chemin | Contenu |
| --- | --- |
| `Game/Network/Packets/Enums/GamePackets.cs:67-68` | `TM_SC_NPC_TRADE_INFO = 240`, `TM_SC_MARKET = 250` |
| `Game/Network/Packets/Game/GameTradePackets.cs:20-23, 30, 33, 36, 39, 45-76, 83-96, 104-113` | `BuildMarketInfo` (250), `BuildNpcTradeInfo` (240, 36 o), `NpcTradeInfoSize`, `HeaderSize = 7`, `WriteChecksum` — **sans appelant, livré pour ce lot** |
| `Configuration/Options/MarketCatalogOptions.cs:16`, `Game/Services/MarketCatalog.cs:14, 26, 63-71, 88-99`, `Game/Services/Interfaces/IMarketCatalog.cs` | `MarketLine(Code, Price, HuntaholicPoint)`, regroupement par nom, `TryGetMarket` |
| `Game/Services/MarketService.cs:25-56`, `Game/Services/Interfaces/IMarketService.cs` | ouverture (250) ou refus journalisé |
| `Game/Services/NpcDialogService.cs:25-28, 130-138` | routage `PropActionKind.OpenMarket` ; le dialogue courant **reste** ouvert |
| `DevConsole/Program.cs:104, 243-258, 299`, `DevConsole/market-catalog.73.json` | section `"MarketCatalog"`, `AddSingleton<IMarketCatalog, MarketCatalog>()`, catalogue livré **vide** (47 octets) |
| `Tests/Game/MarketPacketsTests.cs:35, 42, 56, 72, 82, 90, 109`, `Tests/Game/MarketCatalogTests.cs` | offsets de 240 et 250, chargeur, refus du service |

Manquant, et c'est le lot : `TM_CS_BUY_ITEM` **n'est déclaré nulle part** et **aucun bras** de
`GameClient.OnDataReceived` ne le traite (`grep -n "BUY_ITEM\|251" Game/Network/Packets/Enums/GamePackets.cs
Game/Network/Clients/GameClient.cs` ne remonte que les familles 4251/4252 et le `TM_CS_USE_ITEM`
`253`). Aujourd'hui la trame est jetée en `Debug` par `DefinedPackets` (`GameClient.cs:1215-1226,
1269-1273`), connexion conservée.

Forme d'intégration attendue, calquée sur l'existant :

- `GamePackets` : `TM_CS_BUY_ITEM = 251` ;
- `GameTradePackets` : un lecteur borné `TryReadBuyItem(ReadOnlySpan<byte>, out int itemCode, out
  ushort buyCount)` qui **refuse** toute longueur ≠ `HeaderSize + 6` (patron exact de
  `GameActionPackets.cs:65-76` / `:87-98`) ;
- `GameClient.OnDataReceived` : un bras `if (header.ID == (ushort)GamePackets.TM_CS_BUY_ITEM) { …
  continue; }` placé **avant** le `switch` final (`GameClient.cs:1854-1866`) — un membre de
  `GamePackets` qui l'atteint déclenche `throw new Exception("Unknown Packet Type …")` et tue la
  boucle de réception ;
- le corps du geste : un service appelé par ce bras, sur le modèle de `ItemUseService`
  (`Game/Services/ItemUseService.cs:19-60`, envoi de `client.SendResult(<id>, <code>, <value>)`).

Primitives du corps du geste, vérifiées : `ICharacterService.AddItemAsync`
(`Game/Services/CharacterService.cs:408`, `ICharacterService.cs:90`),
`GameCharacterPackets.BuildGoldUpdate` (`:239-246`), `CharacterEntity.Gold` (`long`,
`Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:54`), l'état de session
`ConnectionInfo.CharacterGold` / `CharacterChaos` / `NpcDialogHandle`
(`Game/Network/Clients/ConnectionInfo.cs:41, 42, 209`), `GameClient.SendResult` (`:52-72`),
la porte par personnage `CharacterService.RunExclusiveAsync` (`:554-570`).

**Réserve de conception, mesurable dans le code** : `MarketService.Open` (`:25-56`) envoie 250 sans
mémoriser quel marché est ouvert, et `ConnectionInfo` ne porte aucun champ de marché
(`grep -n "Market" Game/Network/Clients/ConnectionInfo.cs` → vide). Or la référence résout le
catalogue d'un achat par `GetLastContactStr("market")` (`WorldSession.cpp:733`). Sans
mémorisation au moment de l'envoi de 250, un 251 entrant n'est rattachable à aucun catalogue. Le
choix de ce qui est mémorisé (nom de marché, ou lignes compilées) et de son invalidation
(fermeture du dialogue, changement de PNJ) appartient au lot `navis-dev` ; la fiche l'exige comme
prérequis sourcé, elle ne le tranche pas.

## 6. Écarts assumés avec NGemity, et pourquoi

NGemity tranche la **logique** ; sa version est plus ancienne que 7.3
(`reference/ngemity` `shared/Common/Define.h:25` : `#define EPIC EPIC_4_1_1`), donc ses tailles ne
font pas foi — ses **décisions** si.

1. **`buy_count` : pas d'écart de structure.** À `EPIC_4_1_1`, `version >= EPIC_4_1` est vrai :
   NGemity lit bien un `uint16`, comme 7.3. `TS_CS_BUY_ITEM.h:6-12`.
2. **Poids : non porté.** NGemity refuse en `TOO_HEAVY` (11) (`WorldSession.cpp:769-772`). Le
   dépôt n'a **aucun** état de charge du personnage et ne résout aucune ressource d'objet dans ses
   services (`grep -rn "ItemResourceEntity" Game/Services/ Game/Network/` → vide ; seule la table
   existe, `ArcadiaContext.cs:186`, `ItemResourceEntity.cs:33`). Porter la comparaison demanderait
   d'inventer un plafond de poids : interdit par le lot. Conséquence assumée : le refus 11 n'est
   pas émis, et le code `TooHeavy` reste déclaré (`ResultCode.cs:19`) sans producteur. À trancher
   (`A VERIFIER PAR KILLIAN`).
3. **Empilement : non porté.** NGemity force `buy_count = 1` quand l'objet n'est pas empilable
   (`FLAG_DUPLICATE != 1`, `WorldSession.cpp:754-761`) — et crée alors `buy_count` objets unitaires
   au lieu d'une pile (`:792-802`), alors que le cas empilable crée une pile de `buy_count`
   (`:782-790`). Le référentiel d'objets du dépôt n'expose pas ce drapeau là où le service d'achat
   pourrait le lire. La quantité demandée est donc traitée telle que le client l'a envoyée, et
   l'ajout passe par `AddItemAsync(code, buy_count)`, c'est-à-dire la branche « empilable » de la
   référence. Écart assumé, à trancher (idem).
4. **Prix : pas d'écart de modèle, un écart de précision.** La référence calcule
   `(int32_t)floor(buy_count * mt.price_ratio)` (`WorldSession.cpp:763`) où `price_ratio` est,
   après chargement, un prix **absolu unitaire** (`ObjectMgr.cpp:847, 851` :
   `info.price_ratio = floor(info.price_ratio * itemBase->price)`), et c'est ce même prix qui part
   dans 250 (`Messages.cpp:243`). Le catalogue du dépôt porte exactement cette grandeur
   (`MarketLine.Price`, `long`, prix absolu unitaire — `socle-marche-npc.md:386-390`). La
   troncature en `int32` du total n'est **pas** reproduite : le champ `price` de 240 est un
   `int64` dans la déclaration de référence (rzu `TS_SC_NPC_TRADE_INFO.h:11`, §3.1), et le
   caster en `int32` perdrait des montants que le client sait lire. Total = `(long)buy_count *
   line.Price`.
5. **Taille de la mise à jour d'or : ne pas recopier NGemity.** À `EPIC_4_1_1`, `chaos` est gaté
   `version > EPIC_4_1_1` (rzu `TS_SC_GOLD_UPDATE.h:11`) : la trame de NGemity fait 14 octets.
   En 7.3 (`0x070300 > 0x040101`) le champ est présent → **19 octets**, ce que le dépôt écrit déjà
   (`GameCharacterPackets.cs:239-246`, `HeaderSize + 12`). Piège de gating typique : recopier
   NGemity ici désaligne le client.
6. **Objet absent du marché : silence, comme la référence** (§5.1 point 7). Ce n'est pas un écart,
   c'est la conduite retenue — mais elle est signalée pour que la QA ne la lise pas comme un
   manque de code.
7. **Pièges non portés :** rien de cette fiche ne touche aux bits `limit_*` ni à `SRT_ADD_HP` ; les
   pièges documentés de `CLAUDE.md` ne sont donc pas concernés, et aucun n'est recopié.

## 7. `NON ÉTABLI`

1. **Effet visible d'un refus sur le client 7.3.** Les codes 0/7/10/11 sont sourcés, mais le
   consommateur client de `TS_SC_RESULT` pour `request_msg_id == 251` n'a pas été tracé par lecture
   statique : rien n'établit qu'un `10` affiche un message plutôt que rien. Question précise : le
   client 7.3 distingue-t-il le refus d'un achat sur `TS_SC_RESULT` (id 0) de l'absence de réponse ?
2. **Ordre relatif de la mise à jour d'or** face à `TS_SC_RESULT` et à 240 : la référence débite
   l'or avant les deux autres (le `ChangeGold` de `Player.cpp:1140` envoie sa propre trame), mais
   l'ordre exact reçu par le client n'est pas établi ici.
3. **`value` du refus d'or.** La référence envoie `0` (`WorldSession.cpp:765`). Rien n'établit que
   le client l'utilise ; la valeur `item_code` est utilisée pour le succès (`:804`) et pour
   `TOO_HEAVY` (`:770`). Question : `0` ou `item_code` pour un refus d'or ?
4. **Valeur de `huntaholic_point` de 240 à l'achat.** La référence envoie
   `mt.huntaholic_ratio` (`:810`), forcé à `0` au chargement (`ObjectMgr.cpp:852`) — donc `0` en
   pratique. Le catalogue du dépôt porte `HuntaholicPoint` par ligne (`MarketCatalog.cs:14`), et
   250 envoie déjà cette valeur (`GameTradePackets.cs:70`). Question : recopier la valeur de la
   ligne (cohérent avec ce que 250 vient d'annoncer) ou `0` comme la référence ?
5. **Achat pendant qu'aucun marché n'est ouvert** (`NpcDialogHandle == 0`, dialogue fermé, ou
   trame forgée). La référence répond 7 ; le dépôt devra décider *ce qui* vaut « marché non
   ouvert » (champ de session absent, PNJ disparu, distance au PNJ). Aucune règle de portée
   (distance) n'est établie pour l'achat : la référence n'en applique aucune dans `onBuyItem`.
6. **Achats concurrents.** Deux 251 du même client (le client en envoie un par ligne, §2) doivent
   ne pas pouvoir passer tous deux sous le seuil d'or. La porte par personnage
   (`CharacterService.RunExclusiveAsync`, `:554-570`) est le patron disponible, mais aucune
   primitive combinant « vérifier l'or, débiter, ajouter l'objet » n'existe
   (`ICharacterService.AddItemAsync` et `SaveProgressAsync` sont séparées) : la forme sûre reste à
   écrire par le dev, elle n'est pas décidable des seules références.
7. **Correspondance PNJ → marché et peuplement du catalogue** : réserves ouvertes, déjà portées
   par la MR #34 et le socle (`socle-marche-npc.md:340-346, 391-393`). Sans elles, aucun marchand
   ne s'ouvre, donc aucun 251 légitime n'arrive. Elles ne bloquent pas ce lot.
8. **Doublon de `code` dans un même marché.** La référence balaie **toutes** ses lignes sans
   `break` : deux lignes de même `code` produisent deux achats et deux échos pour un seul 251
   (`WorldSession.cpp:749-814`). Le catalogue du dépôt est une liste ordonnée par `SortId`
   (`MarketCatalog.cs:63-71`) qui n'interdit pas deux lignes de même `code`. Question précise :
   première ligne trouvée, ou toutes les lignes comme la référence ?
9. **Nom de commande d'UI du bras `0x416`** (§2) — confort de documentation, sans effet sur la
   trame.

## 8. Commits épinglés

| Référence | Commit | Usage dans cette fiche |
| --- | --- | --- |
| `reference/rzu` (clone local) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | `TS_CS_BUY_ITEM.h` (structure, gating, id), `PacketEpics.h` (seuils), `TS_SC_RESULT.h`, `TS_SC_NPC_TRADE_INFO.h`, `TS_SC_GOLD_UPDATE.h` |
| `reference/ngemity` (clone local) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | `TS_CS_BUY_ITEM.h`, `Define.h` (version compilée), `WorldSession.cpp` (`onBuyItem`), `Messages.cpp/h`, `TS_MESSAGE.h` (codes), `ObjectMgr.cpp` (prix), `PacketDeclaration.h` |
| `reference/client73` | pas un dépôt git — aucun SHA | `SFrame.exe`, sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, lecture statique (`objdump`, motif d'octets) ; `data.000` (aucune chaîne de commande exploitable) |
| Dépôt `Navislamia` | base `b56967a07430422add88e0e5cdf292b41b18f6c6` (`origin/master`) | chemins et lignes de §5.3 |

## 9. Forme du test d'offsets attendu (lot `navis-dev`)

Deux tests NUnit (+ FluentAssertions), dans `Tests/Game/MarketPacketsTests.cs` ou un fichier voisin,
sur le patron des tests existants de 240/250 (`Tests/Game/MarketPacketsTests.cs:90-115`), le compte
de tests de `master` ne descendant jamais (1302 réussis, 0 échec, mesuré sur `b56967a`) :

1. **Trame entrante** — construire une trame de 13 octets (longueur 13, id 251, somme de contrôle,
   `item_code`, `buy_count`) puis asserter **la position de chaque champ** :
   `ReadUInt32(0) == 13`, `ReadUInt16(4) == (ushort)GamePackets.TM_CS_BUY_ITEM` (`251`),
   `[6] == somme des 6 premiers octets`, `ReadInt32(7) == item_code`,
   `ReadUInt16(11) == buy_count`, et **la taille totale** `packet.Length == 13`.
   La preuve que le test mord : une lecture décalée (par exemple `ReadUInt16(7)`) doit **échouer**
   l'assertion correspondante — le test doit porter un cas qui prouve qu'une position fausse ne
   passe pas.
2. **Refus de longueur** — 7, 11, 12, 14 et 36 octets doivent tous être refusés par le lecteur
   (`false`), sans lecture partielle.
3. **Réponses** — `TS_SC_RESULT` de 15 octets avec `request_msg_id = 251`, 240 de 36 octets avec
   `is_sell = 0` et `target` = handle du PNJ, mise à jour d'or de 19 octets : le test de 240 existe
   déjà et n'est pas à affaiblir.

## 10. Zone de collision

19 MR étaient ouvertes au réveil du 2026-09-25 — numéros 1, 6, 13, 21, 22, 23, 24, 26, 27, 33, 38,
40, 45, 46, 47, 48, 49, 50, 51 — et **presque toutes touchent
`Game/Network/Packets/Enums/GamePackets.cs` et `Game/Network/Clients/GameClient.cs`**, exactement
comme ce lot : ce sont les deux fichiers à conflit probable. Aucune de ces branches n'a été prise
comme base ; la base est `master` `b56967a`. La branche du socle marché
(`hermes/packet-socle-marche-npc`) est mergée et n'a pas été réutilisée.

## 11. Bloc destiné à `CLAUDE.md`

`CLAUDE.md` n'est pas modifié par ce lot (fichier protégé par Hermes). Le bloc ci-dessous voyage
dans la description de la MR, portée par la QA.

> ### Paquet 251 — `TM_CS_BUY_ITEM` (achat chez un marchand)
>
> - 7.3 : trame client → serveur de **13 octets** — en-tête de 7 (taille `uint32` à 0, id `uint16`
>   à 4, somme des 6 premiers octets à 6), `item_code` `int32` à **7**, `buy_count` `uint16` à
>   **11**. L'id 7.3 est **251** : rzu remappe à 1251 à partir d'`EPIC_9_6_3` (`0x090603`), jamais
>   en 7.3 (`EPIC_7_3 = 0x070300`). `buy_count` est `uint16` dès `EPIC_4_1` (la variante `uint8`
>   est morte) — relevé client concordant : `SFrame.exe` écrit un `word` en `+11` (`0x48f3dd`).
> - Le client émet **un 251 par ligne d'achat** (boucle sur un vecteur de 12 octets, `0x48f380`),
>   pas un paquet multi-lignes.
> - Réponse : mise à jour d'or (1001, **19** octets en 7.3 — `chaos` est gaté
>   `version > EPIC_4_1_1`, absent de la trame de NGemity qui compile à `EPIC_4_1_1`), puis
>   `TS_SC_RESULT` (0, 15 octets : `request_msg_id = 251`, `result`, `value`), puis l'écho
>   `TM_SC_NPC_TRADE_INFO` (240, **36** octets, `is_sell = 0`) — seul producteur de 240 avec la
>   vente (252). `GameTradePackets.BuildNpcTradeInfo` est le constructeur livré.
> - Refus sourcés : `buy_count == 0` et marché inconnu → code **7** (`Unknown`), or insuffisant →
>   **10** (`NotEnoughMoney`), capacité → **11** (`TooHeavy`, non applicable faute de modèle de
>   poids dans le dépôt). **Objet absent du marché : aucune réponse** (la référence balaie ses
>   lignes sans `else`).
> - Poids et empilement ne sont **pas** portés : aucun état de charge et aucun drapeau
>   `FLAG_DUPLICATE` lisibles dans les services. Ne pas inventer de plafond ni de règle de pile.
> - En déclarant 251 dans `GamePackets`, son bras de réception devient obligatoire **avant** le
>   `switch` final de `GameClient.OnDataReceived` : un membre déclaré qui l'atteint déclenche
>   `throw new Exception("Unknown Packet Type …")` et tue la boucle de réception.
> - Le savoir durable est dans `docs/packet-specs/251-buy-item.md` ; le socle marché reste
>   `docs/packet-specs/socle-marche-npc.md`.

## A VERIFIER PAR KILLIAN

1. **Ce qui décide du refus d'un achat.**
   - *Objet absent du marché* : la référence ne répond **rien** (`WorldSession.cpp:749-814`). La
     fiche retient ce silence, faute de règle établie. Faut-il au contraire un
     `TS_SC_RESULT(251, 7, item_code)` ? (§5.2, §7 point 5)
   - *Or insuffisant* : code **10** (`NotEnoughMoney`, `ResultCode.cs:17`) avec `value = 0`
     (`WorldSession.cpp:764-767`). À confirmer : `0` ou `item_code` en `value` ? (§7 point 3)
   - *Capacité / poids* : la référence refuse en **11** (`WorldSession.cpp:769-772`), mais le dépôt
     n'a **aucun** état de charge ni résolution de ressource d'objet
     (`grep -rn "ItemResourceEntity" Game/Services/ Game/Network/` → vide). La fiche **ne porte pas**
     ce contrôle plutôt que d'inventer un plafond : décision attendue (§6 point 2).
   - *Empilement* : la référence force `buy_count = 1` sur un objet non empilable
     (`WorldSession.cpp:754-761`) ; le drapeau n'est pas lisible dans nos services. La quantité du
     client est donc honorée telle quelle (§6 point 3).
   - *Achat sans marché ouvert* : la référence répond **7** ; ce qui vaut « marché ouvert » côté
     dépôt (le service qui envoie 250 ne mémorise pas le marché, `MarketService.cs:25-56`, et
     `ConnectionInfo` n'a aucun champ de marché) doit être construit par le lot dev (§5.3, §7
     point 5).
2. **Réserves ouvertes de la correspondance PNJ → marché** : le déclencheur `open_market(` est
   tronqué (le nom était concaténé en Lua) et `DevConsole/market-catalog.73.json` est livré vide
   (47 octets). Ce sont les réserves déjà portées par la MR #34 : sans elles aucun marchand ne
   s'ouvre en jeu, donc aucun 251 légitime. Elles **ne bloquent pas** la livraison du paquet.
3. **`huntaholic_point` de l'écho 240 à l'achat** : valeur de la ligne de catalogue (ce que 250
   vient d'annoncer) ou `0` comme la référence (`WorldSession.cpp:810`,
   `ObjectMgr.cpp:852`) ? (§7 point 4)
4. **Ordre des trames de réponse** et atomicité de la transaction (or + objet) : la fiche fixe
   `TS_SC_RESULT` puis 240 comme la référence ; l'ordre de la mise à jour d'or et la forme sûre
   contre deux achats concurrents restent des décisions du lot dev (§7 points 2 et 6).
