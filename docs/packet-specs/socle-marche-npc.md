# Socle marché NPC — `TM_SC_NPC_TRADE_INFO` (240) et `TM_SC_MARKET` (250)

Fiche de paquet — archéologie de protocole, 22/09/2026.
Branche : `hermes/packet-socle-marche-npc` (depuis `master` `ec76b218`).
Carte Trello : `https://trello.com/c/h5z5f060` — non lue (aucun accès Trello ni GitHub dans ce
profil) ; le constat du PO du 18/09/2026 utilisé ici est celui recopié dans le corps de la tâche.

> **La prémisse de la carte est corrigée par le relevé.** La carte présente « MarketResource +
> `TM_SC_NPC_TRADE_INFO` (240) … il ouvre la fenêtre de commerce ». `240` n'ouvre rien : c'est
> l'**écho d'une transaction** achetée ou vendue, et il n'a de producteur que dans les deux
> gestionnaires `251`/`252` — précisément les deux paquets que la carte met hors périmètre.
> La fenêtre de commerce est ouverte par **`TM_SC_MARKET` (250)**, que la carte ne nomme pas.
> Voir §9 pour le découpage qui en découle.

## 1. Identité

| Paquet | Id | Sens | Source du nom |
| --- | ---: | --- | --- |
| `TM_SC_NPC_TRADE_INFO` | 240 | serveur → client, uniquement | `op_codes.md:75` |
| `TM_SC_MARKET` | 250 | serveur → client, uniquement | `op_codes.md:77` |
| `TM_CS_BUY_ITEM` | 251 | client → serveur | `op_codes.md:78` (carte propre) |
| `TM_CS_SELL_ITEM` | 252 | client → serveur | `op_codes.md:79` (carte propre) |
| `TS_CS_CONTACT` | 3002 | client → serveur | `op_codes.md` (`GamePackets.cs:79`, déjà implémenté) |
| `TS_CS_DIALOG` | 3001 | client → serveur | `docs/npc-dialogs.md:52-53` |
| `TS_SC_DIALOG` | 3000 | serveur → client | `docs/npc-dialogs.md:32-44` |

Déclencheur de dialogue concerné : la chaîne littérale `open_market(` — **176 occurrences** dans
`DevConsole/npc-dialogs.73.json` (relevé `grep -o`, une seule valeur distincte, aucun argument).

`MarketResource` est la table du catalogue marchand : `ArcadiaSchemaPSQL.sql:462-469`
(`sort_id`, `name`, `code`, `price_ratio`, `huntaholic_ratio`). Aucune entité, aucun chargement,
aucune ligne de code ne la référence aujourd'hui dans `Navislamia` (`grep -rn MarketResource Game/`
ne rend que la table SQL).

## 2. Ce que le joueur fait pour que le client l'envoie

1. Le joueur clique un PNJ marchand (ex. `1002 = NPC_Merchant_Equip_Deva_contact()`,
   `1007 = NPC_Merchant_Etc_Deva_contact()`, `DevConsole/npc-dialogs.73.json`). Le client envoie
   `TS_CS_CONTACT` (3002) avec le handle temporaire du PNJ à l'offset 7
   (`docs/npc-dialogs.md:5-8`).
2. Le serveur répond par `TS_SC_DIALOG` (3000) dont les entrées de menu sont
   `\t<label>\t<trigger>\t` (`docs/npc-dialogs.md:46-50`). Pour un marchand, l'entrée « acheter /
   vendre » porte le déclencheur `open_market(` (`DevConsole/npc-dialogs.73.json`).
3. Le joueur clique cette entrée : le client renvoie le déclencheur **tel quel** dans
   `TS_CS_DIALOG` (3001) — longueur `uint16` à l'offset 7, octets du déclencheur à l'offset 9
   (`docs/npc-dialogs.md:52-53`).
4. **C'est là que la fenêtre s'ouvre** : le serveur doit répondre `TS_SC_MARKET` (250) portant le
   handle du PNJ et la liste `(code, prix)` du marché (§3.2). Chez NGemity, le déclencheur est
   exécuté comme Lua (`WorldSession.cpp:726`, `sScriptingMgr.RunString`) et `open_market` est la
   fonction liée (`XLua.cpp:57`) → `SCRIPT_ShowMarket` (`XLua.cpp:485-496`) → `SendMarketInfo`
   (`Messages.cpp:229-247`) → paquet 250.
5. Le joueur achète (251) ou vend (252) une ligne du catalogue ; le serveur applique la
   transaction, répond `TS_SC_RESULT` **puis** l'écho `TS_SC_NPC_TRADE_INFO` (240) de cette
   transaction (`WorldSession.cpp:805-812` pour l'achat, `:1065-1071` pour la vente). Hors
   périmètre de cette carte, mais c'est **le seul producteur de 240**.

Contre-épreuve client : le littéral `open_market(` existe dans `SFrame.exe` (offset fichier
`0x62d084`, VA `0x00a2e684`) et le code de la fenêtre de dialogue PNJ le compare comme **préfixe**
de 12 octets (recherche à `0x57d08d`, après `push $0xc` en `0x57d082` et `push $0xa2e684` en
`0x57d085`), à côté des comparateurs `start_quest` / `end_quest`. Le client traite donc
`open_market(...)` comme un déclencheur de dialogue PNJ connu — cohérent avec le point 3, et
incompatible avec l'idée que 240 ouvre la fenêtre.

## 3. Structure sur le fil

En-tête client, identique dans les deux sens (7 octets) : `uint32` taille totale, `uint16` id,
`uint8` somme de contrôle = somme des 6 premiers octets — `GameCharacterPackets.cs:382-395`
(`CreatePacket` / `WriteChecksum`), `HeaderSize = 7` (`GameCharacterPackets.cs:19`).

### 3.1 `TM_SC_NPC_TRADE_INFO` — serveur → client — **36 octets** (7 d'en-tête + 29 utiles)

| Offset | Type | Nom | Valeur observée | Source |
| ---: | --- | --- | --- | --- |
| 0 | `uint32` | taille totale | `36` | `GameCharacterPackets.cs:382-387` |
| 4 | `uint16` | id | `240` | `op_codes.md:75` ; rzu `TS_SC_NPC_TRADE_INFO.h:17` |
| 6 | `uint8` | somme de contrôle | somme des 6 premiers octets | `GameCharacterPackets.cs:391-395` |
| 7 | `int8` | `is_sell` | `0` à l'achat, `1` à la vente | rzu `TS_SC_NPC_TRADE_INFO.h:8` ; NGemity `WorldSession.cpp:806` (`false`) et `:1066` (`true`) |
| 8 | `int32` | `code` | code objet acheté ou vendu | rzu `:9` ; NGemity `:807` et `:1067` |
| 12 | `int64` | `count` | quantité de la transaction | rzu `:10` ; NGemity `:808` (achat) et `:1068` (vente) |
| 20 | `int64` | `price` | montant total payé / reçu | rzu `:11` ; NGemity `:809` (achat, `nTotalPrice`) et `:1069` (vente, `sell_count * nPrice`) |
| 28 | `int32` | `huntaholic_point` | `0` | rzu `:12` (`version >= EPIC_5_2`, présent en 7.3 — §4) ; NGemity `:810` (`mt.huntaholic_ratio`, forcé à 0 en `ObjectMgr.cpp:852`) |
| 32 | `uint32` | `target` | handle du PNJ marchand | rzu `:14` (`ar_handle_t` = `uint32`, `GameTypes.h:40`) ; NGemity `:811` et `:1070` (`GetLastContactLong("npc")`) |
| 36 | — | fin du paquet | — | — |

`arena_point` (`version >= EPIC_8_1`) est **absent** : charge utile 29 octets et non 33. Relevé
client concordant : le convertisseur `0x66c960` lit exactement huit emplacements dans la trame —
1 octet à `+7`, puis des `dword` à `+8`, `+0xc`, `+0x10`, `+0x14`, `+0x18`, `+0x1c`, `+0x20` — soit
`1 + 7*4 = 29` octets, dernière lecture à `+0x20` (`0x66c995`), fin de trame à `+0x23` = 36 octets.
La correspondance avec rzu est bijective si `count` et `price` sont 64 bits : `+0xc`/`+0x10` sont
les deux moitiés de `count`, `+0x14`/`+0x18` celles de `price`, `+0x1c` = `huntaholic_point`,
`+0x20` = `target`.

### 3.2 `TM_SC_MARKET` — serveur → client — **13 + 16 × n octets** (n = nombre de lignes)

Paquet **non nommé par la carte** mais indispensable à son objectif (§9).

| Offset | Type | Nom | Valeur observée | Source |
| ---: | --- | --- | --- | --- |
| 0 | `uint32` | taille totale | `13 + 16 × n` | `GameCharacterPackets.cs:382-387` |
| 4 | `uint16` | id | `250` | `op_codes.md:77` ; rzu `TS_SC_MARKET.h:26` |
| 6 | `uint8` | somme de contrôle | somme des 6 premiers octets | `GameCharacterPackets.cs:391-395` |
| 7 | `uint32` | `npc_handle` | handle du PNJ marchand | rzu `TS_SC_MARKET.h:19` ; NGemity `Messages.cpp:237` (`ar_handle_t`) |
| 11 | `uint16` | nombre de lignes `n` | — | rzu `:20` (`_(count)(uint16_t, items)`) ; relevé client `0x66ff7e` (`movzwl 0xb`) |
| 13 | — | début du tableau de lignes | — | relevé client `0x66ffa9` (`add $0xd,%edi`) |
| +0 | `int32` | `code` | code objet du catalogue | rzu `TS_SC_MARKET.h:8` |
| +4 | `int64` | `price` | prix **unitaire** en or | rzu `:9-11` ; NGemity `Messages.cpp:243` après `ObjectMgr.cpp:851` (`floor(price_ratio * itemBase->price)`) |
| +12 | `int32` | `huntaholic_point` | `0` | rzu `:12` ; NGemity `Messages.cpp:241`, ratio forcé à 0 en `ObjectMgr.cpp:852` |
| 13 + 16n | — | fin du paquet | — | — |

Chaque ligne fait **16 octets** : `code` 4 + `price` 8 + `huntaholic_point` 4, `arena_point`
(`>= EPIC_8_1`) absent. Contre-épreuve client : `0x66ffa5` calcule `count << 4` (donc `16 × n`) et
`0x66ffb1` copie ce bloc contigu depuis `+0xd` (`0x9767c0` = `memcpy`) — la trame est donc
**compacte**, sans trou de 4 octets entre lignes.

rzu ajoute au contraire un remplissage final de `4 × n` octets (`_(padmarker)` + `_(pad)(4 *
items.size(), …)`, `TS_SC_MARKET.h:22-23`), c'est-à-dire une taille de `13 + 20n` : voir §7.

### 3.3 Cadre des paquets d'action (hors périmètre, mais couplés)

| Paquet | Id | Charge utile | Total | Source |
| --- | ---: | --- | ---: | --- |
| `TM_CS_BUY_ITEM` | 251 | `int32 item_code` + `uint16 buy_count` | 13 | rzu `TS_CS_BUY_ITEM.h:8-11` (`buy_count` en `uint16` dès `EPIC_4_1`) |
| `TM_CS_SELL_ITEM` | 252 | `uint32 handle` + `uint16 sell_count` | 13 | rzu `TS_CS_SELL_ITEM.h:6-10` (`uint16` entre `EPIC_4_1` et `EPIC_8_2`) |

## 4. Gating de version

7.3 = `EPIC_7_3` = `0x070300` (`rzu`, `librzu/src/lib/Packet/PacketEpics.h:59`).

| Élément | Gating rzu | Décision pour 7.3 | Source |
| --- | --- | --- | --- |
| Id de 240 | `version < EPIC_9_6_3` → 240 ; sinon 1240 | **240** | `TS_SC_NPC_TRADE_INFO.h:16-18` ; confirmé par le répartiteur client `0x67df30` (table d'octets `0x67f0a0`, table de sauts `0x67f020` : l'id 240 a une entrée réelle, cible `0x67e17b`) |
| Id de 250 | `version < EPIC_9_6_3` → 250 ; sinon 1250 | **250** | `TS_SC_MARKET.h:25-27` ; entrée réelle dans la même table, cible `0x67e0cf` |
| `huntaholic_point` (240) | `version >= EPIC_5_2` | **présent, émis** (4 octets à l'offset 28) | `TS_SC_NPC_TRADE_INFO.h:12` ; relevé client `0x66c98b` (lecture de `+0x1c`) |
| `arena_point` (240) | `version >= EPIC_8_1` | **absent** | `:13` ; relevé client : 7 `dword` et non 8 (§3.1) |
| `huntaholic_point` (250) | `version >= EPIC_5_2` | **présent, émis** (4 octets, 3ᵉ mot de chaque ligne) | `TS_SC_MARKET.h:12` ; relevé client : lignes de 16 octets (`0x66ffa5`) |
| `arena_point` (250) | `version >= EPIC_8_1` | **absent** | `:13` ; relevé client : `count << 4`, pas `count * 20` |
| `price` (ligne de 250) | `version >= EPIC_4_1_1` → `int64` | **`int64`** (8 octets) | `TS_SC_MARKET.h:9-11` ; relevé client : `count << 4` suppose 4 + 8 + 4 |
| `pad` de 250 | **aucun gating** (appliqué à toutes les versions) | **non tranché** — voir §7 | `TS_SC_MARKET.h:22-23`, `PacketDeclaration.h:375-378` |
| `buy_count` (251) | `>= EPIC_4_1` → `uint16` | **`uint16`**, charge utile 6 octets | `TS_CS_BUY_ITEM.h:9-11` |
| `sell_count` (252) | `>= EPIC_4_1 && < EPIC_8_2` → `uint16` | **`uint16`**, charge utile 6 octets | `TS_CS_SELL_ITEM.h:7-10` |
| `is_sell`, `code`, `count`, `price`, `target` (240) | aucun gating | présents tels quels | `TS_SC_NPC_TRADE_INFO.h:8-14` |

Aucun champ de cette fiche ne reste sans décision de version. Le seul point non tranché est le
remplissage final de 250, qui ne change pas la position d'un champ (§7, question 2).

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` fait du paquet

Chaîne complète, telle que lue :

1. `WorldSession::onContact` (`Chihiro/src/Network/GameNetwork/WorldSession.cpp:697-705`) : mémorise
   le PNJ contacté (`SetLastContact("npc", pRecvPct->handle)`, `:702`) puis **exécute le script de
   contact** du PNJ (`:703`).
2. `WorldSession::onDialog` (`:707-729`) : refuse un déclencheur qui n'a pas été annoncé
   (`IsValidTrigger`, `:712`), puis **exécute le déclencheur comme Lua** (`RunString`, `:726`).
3. `XLua::SCRIPT_ShowMarket` (`Chihiro/src/Scripting/XLua.cpp:485-496`) : `open_market(<nom>)`
   reçoit le **nom du marché en argument** (`:485`), cherche le catalogue
   (`sObjectMgr.GetMarketInfo(szMarket)`, `:491`) et envoie `Messages::SendMarketInfo(player,
   GetLastContactLong("npc"), *info)` (`:494`).
4. `Messages::SendMarketInfo` (`Chihiro/src/Network/Messages.cpp:229-247`) : remplit
   `TS_SC_MARKET` — `npc_handle` (`:237`), puis une ligne par élément du catalogue avec
   `code` (`:240`), `arena_point = 0` (`:241`, commentaire « @ Epic 9 »),
   `huntaholic_point = huntaholic_ratio` (`:242`), `price = price_ratio` (`:243`) — et mémorise le
   marché courant (`SetLastContact("market", pMarket[0].name)`, `:235`) avant d'envoyer (`:247`).
   Le prix est déjà un prix absolu : au chargement,
   `info.price_ratio = floor(info.price_ratio * itemBase->price)` (`ObjectMgr.cpp:851`) et
   `info.huntaholic_ratio = 0` (`:852`).
5. `WorldSession::onBuyItem` (`:731-816`) / `onSellItem` (`:1021-1074`) répondent
   `Messages::SendResult(...)` (`Messages.cpp:361`) **puis** l'écho `TS_SC_NPC_TRADE_INFO`
   (`:805-812`, `:1065-1071`).

Catalogue chargé depuis la base : `SELECT sort_id, name, code, price_ratio, huntaholic_ratio FROM
MarketResource ORDER BY name, sort_id;` (`ObjectMgr.cpp:831`), regroupé par `name`
(`_marketResourceStore[lastMarket]`, `:858` et `:867`), recherché par `GetMarketInfo(szKey)`
(`:1358-1363`) ; conteneur déclaré en `ObjectMgr.h:74` et `:156`.

### 5.2 Ce que le serveur doit répondre

| Requête client | Réponse du serveur | Détail |
| --- | --- | --- |
| `TS_CS_DIALOG (3001)` = `open_market(` | **`TM_SC_MARKET (250)`**, `13 + 16n` octets, `npc_handle` = PNJ du dialogue, lignes dans l'ordre du catalogue | §3.2 ; NGemity `Messages.cpp:229-247` |
| `TS_CS_BUY_ITEM (251)` | `TS_SC_RESULT` succès puis **`TM_SC_NPC_TRADE_INFO (240)`** `is_sell=0` | hors périmètre (§9) ; NGemity `:805-812` |
| `TS_CS_SELL_ITEM (252)` | `TS_SC_RESULT` succès puis **`TM_SC_NPC_TRADE_INFO (240)`** `is_sell=1` | hors périmètre (§9) ; NGemity `:1065-1071` |
| paquet **entrant** 240 ou 250 | rien : anomalie de protocole, journalisée puis ignorée | les deux ids sont strictement S→C (aucune entrée dans la table d'émission du client pour 240, `TM_SC_MARKET` présent seulement dans la table de noms, `SFrame.exe` fichier `0x65208c`) — patron déjà en place : `GameClient.cs:620-628` (`TM_SC_REGION_ACK`) |

Le serveur n'a jamais à envoyer `250` de sa propre initiative : il ne le fait **que** sur un
déclencheur de dialogue qu'il a lui-même annoncé (`docs/npc-dialogs.md:57-58`) et pour lequel il
retient le handle du PNJ (`ConnectionInfo.cs:101`, `NpcDialogService.cs:134`).

### 5.3 Points d'implémentation à respecter dans `Navislamia`

- **Déclarer les deux ids** dans `GamePackets` (`Game/Network/Packets/Enums/GamePackets.cs`), dans
  le bloc inventaire/échange (après `TM_SC_SKIN_INFO = 224`, ligne 46) : `TM_SC_NPC_TRADE_INFO = 240`
  et `TM_SC_MARKET = 250`.
- **Construire avec l'en-tête du dépôt** : `CreatePacket(id, total)` + écritures
  `BinaryPrimitives.Write…LittleEndian(span.Slice(HeaderSize + …))` + `WriteChecksum`
  (`GameCharacterPackets.cs:382-395`). Un fichier `GameTradePackets.cs` calqué sur
  `GameNpcDialogPackets.cs` est l'emplacement naturel.
- **Empêcher les deux membres d'atteindre le `switch` final** (`GameClient.cs:791-803`,
  `_ => throw new Exception("Unknown Packet Type")`) : ajouter, à côté du bras
  `TM_SC_REGION_ACK` (`GameClient.cs:620-628`), un bras qui journalise en `Warning` et fait
  `continue` — 240 et 250 sont strictement serveur → client.
- **Déclencheur** : `PropScript.Parse` exige aujourd'hui une parenthèse fermante
  (`Game/Services/Props/PropScript.cs:38-41`) ; la chaîne du catalogue étant `open_market(`, elle
  rend `PropAction.None` et `NpcDialogService.Select` retombe sur
  « NPC dialog action open_market is not implemented yet » (`NpcDialogService.cs:108-116`). Un
  `PropActionKind.OpenMarket` doit reconnaître le préfixe `open_market(` **avec ou sans**
  parenthèse fermante, et `Select` (`:96-116`) doit router vers le service marché au lieu de
  chercher une page de dialogue.
- **Catalogue** : le morceau de dépôt en place pour une ressource sans PostgreSQL est le catalogue
  JSON versionné + `IOptions` + `services.Configure<T>` : `ConfigureMonsterDrops`
  (`DevConsole/Program.cs:126-142`) et `ConfigureNpcDialogs` (`:170-189`) lisent
  `monster-drops.73.json` / `npc-dialogs.73.json` par `JsonDocument.Parse` sur une section racine
  (`"MonsterDropCatalog"`, `"NpcDialogCatalog"`) et configurent les options ; le fichier absent
  laisse des options vides. Un `market-catalog.73.json` + `MarketCatalogOptions` + section
  `"MarketCatalog"` suit exactement ce patron (`Configuration/Options/NpcDialogOptions.cs:5-9`) —
  le fichier peut être livré avec zéro ligne, comme le tolère NGemity.
- **Prix servi en clair** : le client ne connaît que les noms et icônes de ses propres `.rdb` ; le
  paquet 250 ne porte que `code` + `price` + `huntaholic_point` (§3.2), donc le serveur doit fournir
  le prix **absolu** (§5.1 point 4), pas un ratio.

### 5.4 Cas limites

- Déclencheur non annoncé : refusé avant toute action (`NpcDialogService.cs:84-92`,
  `docs/npc-dialogs.md:57-58`).
- Marché introuvable dans le catalogue : NGemity refuse d'envoyer le paquet
  (`Messages.cpp:231-232`, `if (pPlayer == nullptr || pMarket.empty()) return;`) et
  `SCRIPT_ShowMarket` ne fait rien si le catalogue est vide (`XLua.cpp:493`). Un `250` de taille
  `13` (n = 0) n'a donc pas de producteur connu : ne pas l'inventer.
- `buy_count == 0` ou `sell_count == 0` dans 251/252 : refus (`WorldSession.cpp:735`,
  `:1031`), hors périmètre.
- Catalogue vide au démarrage : journaliser comme NGemity
  (`Loaded 0 Markettemplates`, `ObjectMgr.cpp:833`) plutôt que de faire échouer le démarrage.

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | NGemity | Décision Navislamia | Pourquoi |
| --- | --- | --- | --- |
| Exécution du déclencheur | exécute le Lua du client (`WorldSession.cpp:726`) | analyse la chaîne et cherche une action connue (`PropScript.Parse`) | règle du dépôt : l'entrée client n'est jamais évaluée comme Lua (`docs/npc-dialogs.md:57-60`) |
| Version de compilation | `#define EPIC EPIC_4_1_1` (`shared/Common/Define.h:25`) | 7.3 = `EPIC_7_3` | à 4.1.1, `huntaholic_point` (`>= EPIC_5_2`) ne serait pas sérialisé ; en 7.3 il est **présent** (§4). Les deux paquets diffèrent donc de 4 octets par rapport à ce que NGemity émettrait |
| `arena_point` dans 240/250 | NGemity pose `0` (`Messages.cpp:241`, champ déclaré `>= EPIC_8_1`) | champ **absent**, ni dans 240 ni dans les lignes de 250 | le gating l'exclut en 7.3 ; le relevé client le corrobore (240 : 8 emplacements, 250 : 16 octets par ligne — §3) |
| `huntaholic_point` | forcé à `0` au chargement (`ObjectMgr.cpp:852`) | idem `0`, mais champ **émis** à l'offset 28 (240) et dans chaque ligne (250) | le champ est gated `>= EPIC_5_2`, présent en 7.3 : l'omettre désaligne le paquet |
| Remplissage final de 250 | rzu en ajoute `4 × n` (`TS_SC_MARKET.h:22-23`) | **non tranché**, §7 question 2 | aucun gating de version et le relevé client n'exige pas ces octets (§3.2) |
| Bug de `onSellItem` | `tradePct.code = pRecvPct->sell_count;` écrase `code` juste après l'avoir posé (`WorldSession.cpp:1067-1068`) | **à ne pas porter** : `code` = code de l'objet, `count` = quantité | erreur manifeste de la référence (la variable `code` capturée à `:1053` est perdue) |
| Source des données | base SQL Server `MarketResource` (`ObjectMgr.cpp:831`) | catalogue JSON versionné, patron des socles voisins (`DevConsole/Program.cs:126-189`) | aucun PostgreSQL sur le VPS (voir §9) |

## 7. `NON ÉTABLI`

1. **Quel marché pour quel PNJ.** Les 176 déclencheurs `open_market(` du catalogue sont
   **tronqués** : le motif du générateur n'accepte qu'un littéral quoté et rien d'autre entre les
   guillemets (`MENU_PATTERN`, `tools/import_npc_dialogs.py:12-14`), donc le nom du marché était
   concaténé en Lua (`"open_market(" .. <nom> .. ")"`) et n'a pas été capturé ; le
   `--lua-directory` du générateur (`tools/import_npc_dialogs.py:84`) **n'existe pas sur ce VPS**.
   Question précise : quel nom de marché (`MarketResource.name`) correspond à chaque PNJ marchand ?
   Sans cette table de correspondance, un `250` servi à un marchand ne peut pas choisir son
   catalogue — même vide de données, le lien PNJ → marché est inconnu. À noter : le serveur choisit
   **le texte du déclencheur qu'il envoie** ; il peut donc annoncer `open_market(<nom>)` complet et
   retrouver le nom dans l'écho, à condition de connaître le nom à la construction du dialogue.
2. **Remplissage final de 250.** Faut-il émettre les `4 × n` octets de rzu (`13 + 20n`) ou la forme
   compacte (`13 + 16n`) ? Le client lit `count × 16` octets contigus depuis `+0xd`
   (`0x66ffa5-0x66ffb6`) ; il ignore donc des octets placés **après** ces `16n`. Trancher par un
   test sur le vrai client (impossible ici : SFrame n'est jamais lancé). Les deux formes
   fonctionnent *a priori* ; la forme compacte est celle que le relevé client justifie.
3. **Quelle fenêtre le client ouvre sur 250.** Le répartiteur entrant construit bien un
   `USMSG_MARKET` (id de paquet 250 → cible `0x67e0cf`, objet de 29 octets, vtable `0x00a52020`,
   descripteur RTTI `.?AUSMSG_MARKET@@`) et 240 un `USMSG_NPC_TRADE_INFO` (cible `0x67e17b`, objet
   de 52 octets, vtable `0x00a52210`, descripteur `.?AUSMSG_NPC_TRADE_INFO@@`), mais le
   consommateur de l'id interne `60` (`0x3c`, posé à `0x66ff4a`) n'a pas été suivi jusqu'à la
   classe de fenêtre. `SUIShopWnd` / `SUIShopKartWnd` existent dans le binaire mais aucun de leurs
   littéraux de création n'est référencé par du code. Question : la fenêtre ouverte par 250 est-elle
   `SUIShopWnd` (`TM_CS_BUY_ITEM` est bien émis par le client, chaîne présente en `0x65207c`) ?
   La conclusion de §1 n'en dépend pas : 240 ne peut pas porter de catalogue (un seul `code`, pas de
   liste) et n'a de producteur que dans 251/252.
4. **Contenu de `MarketResource`.** Aucune ligne n'est disponible localement (ni SQL Server, ni
   PostgreSQL) : ni les noms de marchés, ni les couples `(code, prix)`. Les prix servis sont des
   prix absolus fabriqués à la génération (`ObjectMgr.cpp:851`) : ils ne peuvent pas être recalculés
   sans la table `ItemResource`.
5. **Ordre d'affichage des lignes de 250.** NGemity trie par `name, sort_id` à la requête
   (`ObjectMgr.cpp:831`) ; rien ne prouve que le client 7.3 respecte l'ordre reçu plutôt que de
   retrier par `code` — non tranchable sans client.
6. **Sémantique réelle de `huntaholic_point`** sur 250/240 en 7.3 : NGemity force le ratio à `0` au
   chargement (`ObjectMgr.cpp:852`) et pose `0` dans le paquet. Rien n'indique ici une autre valeur.
7. **`sort_id`** : colonne `not null` sans contrainte d'unicité (`ArcadiaSchemaPSQL.sql:464`) ;
   aucune clé primaire sur la table — l'ordre des lignes d'un même `name` n'est donc pas défini par
   le schéma.
8. **Le client valide-t-il la taille annoncée ?** Non établi : si la trame courte est acceptée, la
   question 2 disparaît ; si le client exige une taille exacte, elle devient bloquante.

## 8. Commits et binaires épinglés

| Référence | Version | Usage dans cette fiche |
| --- | --- | --- |
| `rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | tailles, ordre des champs, gating, ids |
| `ngemity/Chihiro` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | logique serveur (chaîne contact → 250 → 251/252 → 240) |
| `Navislamia` `master` | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | état du dépôt au moment de la fiche |
| `reference/client73/SFrame.exe` | `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | contre-épreuve : répartiteur entrant, trames 240 et 250, préfixe `open_market(` |

Repères du binaire client (image de base `0x00400000`, `.text` VA = offset fichier + `0x400C00`) :

| Repère | Adresse | Ce qu'il prouve |
| --- | --- | --- |
| répartiteur entrant | `0x67df30` (`cmp $0xfa` à `0x67df70`) | l'id est lu à `+4` de la trame, charge utile à `+7` |
| table d'octets / table de sauts | `0x67f0a0` / `0x67f020` | entrées réelles pour 240 (`0x67e17b`) et 250 (`0x67e0cf`) |
| convertisseur 240 | `0x66c960` | 1 octet + 7 `dword` (`+7` … `+0x20`), donc 29 octets utiles |
| convertisseur 250 | `0x66ff30` | `npc_handle` `+7`, `count` `uint16` `+0xb`, lignes de 16 octets `+0xd` |

## 9. Découpage recommandé pour le dev

**État final : la carte, telle qu'écrite, n'est pas livrable en l'état.** Le paquet qu'elle nomme
(240) n'a aucun producteur hors de 251/252, et l'ouverture de la fenêtre passe par 250, qu'elle ne
nomme pas. Le reste ci-dessous dit ce qui est livrable **sans donnée nouvelle** et ce qui est
bloqué, avec les prérequis exacts.

### 9.1 Livrable possible dans ce lot (aucune donnée nouvelle requise)

1. `GamePackets.TM_SC_NPC_TRADE_INFO = 240` et `GamePackets.TM_SC_MARKET = 250`, plus un bras
   d'anomalie S→C pour chacun dans `GameClient.cs` (§5.3).
2. `GameTradePackets.cs` : construction de `240` (36 octets, §3.1) et de `250` (`13 + 16n`, §3.2),
   avec l'ordre de champs et les décalages ci-dessus, plus un **test d'offsets** par paquet
   (taille totale + position de chaque champ), selon la forme des tests existants
   (`Tests/Game/UseItemPacketsTests.cs:14-30`, `RegionInfoPacketsTests.cs`).
3. Le **socle de chargement du catalogue** : `MarketCatalogOptions` + `market-catalog.73.json`
   (section `"MarketCatalog"`, livrable avec zéro ligne) + un service de
   regroupement par `name` et de recherche par nom, sur le patron exact de
   `DevConsole/Program.cs:126-189` et `Configuration/Options/NpcDialogOptions.cs:5-9`. Tests sur
   catalogue synthétique : regroupement, recherche d'un nom absent, catalogue vide.
4. Le **routage du déclencheur** : `PropActionKind.OpenMarket` dans
   `Game/Services/Props/PropScript.cs` (accepte `open_market(` sans parenthèse fermante **et**
   `open_market(<nom>)`), branche dans `NpcDialogService.Select` (`:96-116`) qui retient le contexte
   (handle du PNJ via `ConnectionInfo.NpcDialogHandle`, `ConnectionInfo.cs:101`) et remet la main au
   service marché. Le service marché envoie `250` **seulement** si le catalogue du PNJ est
   résolu ; sinon il refuse en le journalisant — jamais de fenêtre vide inventée.

### 9.2 Bloqué, avec le prérequis exact

| Objectif de la carte | Prérequis manquant | Où il vit |
| --- | --- | --- |
| Choisir le catalogue d'un marchand | correspondance **PNJ → nom de marché** (§7 question 1) | argument du `open_market(<nom>)` du Lua serveur, perdu à la génération du catalogue (`tools/import_npc_dialogs.py:12-14`) ; `--lua-directory` absent du VPS |
| Servir des lignes réelles | lignes de `MarketResource` (nom, code, prix absolu, ratio) | SQL Server `Arcadia` / PostgreSQL local, aucun des deux présent ; aucune donnée dans le dépôt |
| Vérifier 250 sur le vrai client | exécution de `SFrame.exe` | interdit par le profil |

Conséquence : la fenêtre de commerce ne peut pas s'ouvrir **remplie** dans ce lot. Le socle peut
être livré complet *en code* (§9.1) avec un catalogue vide, ce qui est vérifiable par les tests
d'offsets et par les tests du chargeur, mais qui ne doit pas être présenté comme « la fenêtre
s'ouvre » à l'opérateur.

### 9.3 Décision à trancher (PO / Killian)

- **250 doit-il entrer dans cette carte ?** Sans lui il n'y a pas d'ouverture de fenêtre, donc pas
  d'objectif atteint ; avec lui la carte dépasse les paquets qu'elle nomme. Recommandation :
  inclure 250 dans ce lot (§9.1 point 2), documenté comme ici, et le dire dans la description de la
  MR.
- **251/252 restent dehors** : leurs cartes propres portent la transaction, la bourse, le poids,
  le résultat `TS_SC_RESULT` et l'algorithme de prix de vente
  (`Chihiro/src/Globals/GameContent.cpp:195-220`, `GetItemSellPrice`) ; les points d'accroche
  existent (`ICharacterService.AddItemAsync`, `RemoveItemAsync`, `SaveProgressAsync`).
- **Ne pas inventer de nom de marché** ni de ligne de catalogue pour faire ouvrir la fenêtre : la
  réserve ci-dessus est vérifiable, une valeur inventée ne l'est pas.

## 10. Bloc prêt à coller dans `CLAUDE.md`

> ### Marché NPC — `TM_SC_MARKET` (250) ouvre la fenêtre, `TM_SC_NPC_TRADE_INFO` (240) en est l'écho
>
> - `TM_SC_MARKET` (250) porte `npc_handle` (`uint32` à 7), un compte `uint16` à 11 puis des lignes
>   de **16 octets** (`code` `int32`, `price` `int64` absolu, `huntaholic_point` `int32`) :
>   `13 + 16n`. `arena_point` (`>= EPIC_8_1`) est absent ; rzu ajoute un remplissage final de `4n`
>   non gaté (`TS_SC_MARKET.h:22-23`) dont le client n'a pas besoin (`count << 4`, `0x66ffa5`).
> - `TM_SC_NPC_TRADE_INFO` (240) est l'**écho d'une seule transaction**, pas une liste : `is_sell`
>   (`int8` à 7), `code` (`int32` à 8), `count` (`int64` à 12), `price` (`int64` à 20),
>   `huntaholic_point` (`int32` à 28, présent car `>= EPIC_5_2`), `target` (`uint32` à 32) —
>   **36 octets**. Ses seuls producteurs sont les gestionnaires de `251`/`252`.
> - Le déclencheur de dialogue des marchands est le littéral `open_market(` (176 entrées de
>   `DevConsole/npc-dialogs.73.json`), **tronqué** : le nom du marché n'est pas dans le catalogue.
>   `PropScript.Parse` exige aujourd'hui une parenthèse fermante et rend `None` sur cette chaîne.
> - `MarketResource` (`ArcadiaSchemaPSQL.sql:462-469`) n'a ni entité ni chargement : le catalogue
>   marchand vient de l'extérieur du dépôt, aucune ligne n'est disponible localement.

## A VERIFIER PAR KILLIAN

1. **Le 250 doit-il rejoindre cette carte ?** La carte ne nomme que 240, qui n'ouvre pas la
   fenêtre (§9.3). Réponse attendue : « oui, 250 dans ce lot » ou « non, carte dédiée à 250 ».
2. **Correspondance PNJ → nom de marché** : à extraire du Lua serveur décompressé (absent du VPS)
   ou du SQL Server `Arcadia`. Sans elle, aucun marchand ne peut recevoir son catalogue (§7 q.1).
3. **Lignes de `MarketResource`** (nom, code, prix absolu, `huntaholic_ratio`) : à exporter, comme
   `monster-drops.73.json` / `npc-dialogs.73.json` l'ont été (`DevConsole/Program.cs:126-189`).
4. **Remplissage final de 250** : garder la forme compacte (`13 + 16n`) ou reproduire les `4n`
   octets de rzu ? (§7 q.2 ; non tranchable sans `SFrame.exe`, interdit ici.)
5. **Faut-il corriger la génération des déclencheurs** pour ne plus tronquer les appels de type
   `open_market(<nom>)` dans `DevConsole/npc-dialogs.73.json` ? Le générateur ne lit qu'un littéral
   quoté par entrée de menu (`tools/import_npc_dialogs.py:12-14`).
