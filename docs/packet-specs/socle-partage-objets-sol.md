# Socle — partage des objets au sol (au groupe puis à tous)

Carte Trello : `owmwrG7O` — à poser par le PO. Suivi : `navislamia:socle:partage-objets-sol`.
Branche : `hermes/packet-socle-partage-objets-sol`, posée depuis `master` `b2b260c`.

Cette fiche établit **la structure et la propriété de l'objet au sol** : le bloc `pick_up_order`
de l'entrée d'objet de `TM_SC_ENTER` (`TS_SC_ENTER`, id 3) et la règle qui décide, dans le temps,
qui a le droit de le ramasser. Elle complète `203-drop-item.md` (qui traite le *jet* depuis
l'inventaire et le *résultat*) et `socle-groupe.md` (§5.1, récompenses au groupe).

Elle corrige aussi un chiffre : le verrou de ramassage vaut **30/40/50 secondes**, pas 3/4/5 —
voir §5.4 et `## A VERIFIER PAR KILLIAN`.

## 1. Identité et périmètre

| Élément | Valeur | Source |
|---|---|---|
| Entrée d'objet au sol | `TM_SC_ENTER` = **3** | `op_codes.md:6`, `Game/Network/Packets/Enums/GamePackets.cs:27` |
| Id de paquet officiel en 7.3 | **3** (1003 seulement à partir d'`EPIC_9_6_3`) | rzu `TS_SC_ENTER.h:167-169` |
| Case `objType` de l'objet | `EOT_Item` = **2** | rzu `TS_SC_ENTER.h:15`, `GameSpawnPackets.cs:24` |
| Case `type` (préfixe statique) | `ET_StaticObject` = **2** | rzu `TS_SC_ENTER.h:26`, `GameSpawnPackets.cs:22` |
| Demande de ramassage | `TM_CS_TAKE_ITEM` = **204** | `op_codes.md:54`, `GamePackets.cs:63` |
| Réponse animée | `TM_SC_TAKE_ITEM_RESULT` = **210** | `op_codes.md:59` |
| Constructeur local | `GameSpawnPackets.BuildEnterItem` | `Game/Network/Packets/Game/GameSpawnPackets.cs:171-198` |

Le paquet est **déjà correct octet pour octet** côté local (70 octets, offsets vérifiés ci-dessous
contre les deux références). Ce qui manque n'est pas le format, c'est **la sémantique du temps** :
le serveur officiel réserve l'objet à des ayants droit pendant une fenêtre, puis l'ouvre à tous.

## 2. Structure sur le fil — `TM_SC_ENTER` pour `objType = EOT_Item`, **70 octets**

En-tête Navislamia : 7 octets (longueur `uint32` sur 4, id `uint16` sur 2, somme de contrôle
`uint8` sur 1 ; `GameSpawnPackets.cs:10`, `:312-316`, `:354-364`).

| Offset | Taille | Type | Nom (rzu / officiel) | Valeur | Source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | longueur totale | 70 | `GameSpawnPackets.cs:174`, `:312-316` |
| 4 | 2 | `uint16` | id | 3 | `GamePackets.cs:27` |
| 6 | 1 | `uint8` | somme de contrôle | somme des 6 premiers | `GameSpawnPackets.cs:354-364` |
| 7 | 1 | `uint8` | `type` | 2 (`ET_StaticObject`) | rzu `TS_SC_ENTER.h:146`, `GameSpawnPackets.cs:179` |
| 8 | 4 | `ar_handle_t` | `handle` de l'objet | handle d'instance | rzu `TS_SC_ENTER.h:152`, `:180` |
| 12 | 4 | `float` | `x` | | rzu `TS_SC_ENTER.h:153`, `:181` |
| 16 | 4 | `float` | `y` | | rzu `TS_SC_ENTER.h:154`, `:182` |
| 20 | 4 | `float` | `z` | | rzu `TS_SC_ENTER.h:155`, `:183` |
| 24 | 1 | `uint8` | `layer` | | rzu `TS_SC_ENTER.h:156`, `GameSpawnPackets.cs:184` |
| 25 | 1 | `uint8` | `objType` | 2 (`EOT_Item`) | rzu `TS_SC_ENTER.h:157`, `:185` |
| 26 | 8 | `EncodedInt<EncodingRandomized>` | `itemInfo.code` | `item_code` (0 = or) | rzu `TS_SC_ENTER.h:38`, `:186-189` |
| 34 | 8 | `uint64` | `itemInfo.count` | nombre ou montant d'or | rzu `TS_SC_ENTER.h:40` |
| 42 | 4 | `ar_time_t` (`uint32`) | `pick_up_order.drop_time` | **instant de la chute** (tick de 10 ms) | rzu `TS_SC_ENTER.h:30`, `GameSpawnPackets.cs:190` |
| 46 | 12 | `ar_handle_t[3]` | `pick_up_order.hPlayer[0..2]` | `[0]` = handle du propriétaire | rzu `TS_SC_ENTER.h:31`, `GameSpawnPackets.cs:190` |
| 46 | 4 | `uint32` | `hPlayer[0]` | handle du tueur à la chute | `op_codes.md`, local `:190` |
| 50 | 4 | `uint32` | `hPlayer[1]` | 0 (laissé à zéro) | `BuildEnterItem` n'écrit pas |
| 54 | 4 | `uint32` | `hPlayer[2]` | 0 (laissé à zéro) | `BuildEnterItem` n'écrit pas |
| 58 | 12 | `int32[3]` | `pick_up_order.nPartyID[0..2]` | `[0]` = groupe à la chute | rzu `TS_SC_ENTER.h:32`, `GameSpawnPackets.cs:191` |
| 58 | 4 | `int32` | `nPartyID[0]` | id du groupe, 0 sinon | `BuildEnterItem` (`:191`) |
| 62 | 4 | `int32` | `nPartyID[1]` | 0 | `BuildEnterItem` n'écrit pas |
| 66 | 4 | `int32` | `nPartyID[2]` | 0 | `BuildEnterItem` n'écrit pas |

**Taille totale : 70 octets** = 7 + 1 + 4 + 12 + 1 + 1 + **44** (`itemInfo` = 8 `code` + 8 `count`
+ **28** `pick_up_order`), où `pick_up_order` = 4 `drop_time` + 12 `hPlayer[3]` + 12 `nPartyID[3]`.

Vérifications indépendantes du même gabarit :

- **Serveur officiel** (PDB `CaptainHerlockServer.pdb`) : `TS_ENTER::TS_ITEM_PICK_UP_ORDER`
  `sizeof 28` — `drop_time` `unsigned long` @0, `hPlayer` @4 (tableau de 3 `unsigned`, 12 octets),
  `nPartyID` @16 (tableau de 3 `int`, 12 octets) ; `TS_ENTER::ItemInfo` `sizeof 44` —
  `code` @0 (`c_encint<int,short,c_enc_rand_gen>`, 8 octets sur le fil), `cnt` `__int64` @8,
  `pick_up_order` @16 ; `TS_ENTER` : `type` @7, `handle` @8, `x` @12, `y` @16, `z` @20, `layer` @24,
  `ObjType` @25, `itemInfo` @26. Côté serveur, le modèle interne est
  `StructItem::ITEM_PICKUP_ORDER` `sizeof 24` (2 tableaux de 3, sans `drop_time`) porté par
  `StructItem::m_ItemPickupOrder` (+0x1A8) ; `drop_time` vit à part dans `StructItem::m_nDropTime`
  (+0x1A4).
- **Client 7.3** (`reference/client73/SFrame.exe`, lecture seule) : `SGameItem::SetPickUpOrder`
  (0x6CA200) lit `drop_time` à `[paquet+0x36]`, `hPlayer[0]` à `[paquet+0x3a]` et `nPartyID[0]` à
  `[paquet+0x46]` — soit, une fois retirés les 12 octets d'en-tête interne du message analysé,
  **42 / 46 / 58** sur le fil. Le client porte exactement les mêmes structures :
  `SMSG_ENTER::TS_ITEM_PICK_UP_ORDER` `sizeof 28` et `TS_ENTER::TS_ITEM_PICK_UP_ORDER`
  `sizeof 28` (`client-pdb/2011-12-14-part4-design/SFrame_Release.pdb`).

## 3. Gating de version — statué pour le client 7.3

| Champ | Règle rzu | Décision 7.3 | Source |
|---|---|---|---|
| id du paquet | `3` si `< EPIC_9_6_3`, `1003` sinon | **3** | `TS_SC_ENTER.h:167-169` |
| `layer` / `objType` | après `x/y/z` **seulement** à partir d'`EPIC_9_6_7` ; avant, en 24/25 | **24 et 25** | `TS_SC_ENTER.h:149-150` vs `:156-157` |
| `unknown` (`uint8`) | `>= EPIC_9_6_7` uniquement | **absent** (0 octet) | `TS_SC_ENTER.h:151` |
| `itemInfo.count` | `uint64` si `>= EPIC_4_1`, `uint32` sinon | **8 octets** | `TS_SC_ENTER.h:39-41` |
| `itemInfo.code` | `EncodedInt<EncodingRandomized>`, pas de gating | **8 octets** (deux mots à zéro, valeur sur 2+2) | `TS_SC_ENTER.h:38`, `librzu/src/lib/Packet/EncodingRandomized.h:14` |
| `pick_up_order.unknown` (`uint32`) | `>= EPIC_9_6_7` uniquement | **absent** → 28 octets et non 32 | `TS_SC_ENTER.h:33` |
| `drop_time`, `hPlayer[3]`, `nPartyID[3]` | aucun gating | tels quels | `TS_SC_ENTER.h:30-32` |

Le client 7.3 confirme le `>= EPIC_9_6_7` : son analyseur lit `hPlayer` à 46 et `nPartyID` à 58,
donc sans le mot `unknown` de fin, et `layer`/`objType` avant l'objet.

## 4. D'où vient l'ordre de ramassage

À la mort du monstre, le serveur officiel classe les parties virtuelles par contribution décroissante
et **remplit `takePriority` par les trois premières** : `StructMonster::onDead`, puis
`StructMonster::dropItem` (`?dropItem@StructMonster@@…`, `takePriority *`), `procDropGold` et
`procDropItem` — tous signés sur `struct takePriority *` (PDB officiel, `takePriority`
`sizeof 24` = un `ITEM_PICKUP_ORDER`). Un emplacement porte **un joueur ou un groupe, jamais les
deux** ; les emplacements restants restent à zéro (portage lisible : NGemity
`Chihiro/src/Entities/Monster/Monster.cpp:144-164`, tri à `:141`).

L'objet reçoit alors son instant de chute et son ordre, puis entre dans le monde :
`MonsterDropItemToWorld(StructCreature*, StructItem*)` (0x140043CC0) écrit
`item->m_nDropTime = GetArTime()` (0x140043D76, appel à `GetArTime` 0x140011B10) puis enregistre
l'objet auprès de l'`ItemCollector` (0x140043D7C-0x140043D87) qui le fera disparaître à
l'expiration.

Les trois emplacements ne sont donc pas une file d'attente « groupe puis tous » : ce sont **trois
ayants droit distincts** (les trois premières parties contributrices), chacun avec sa propre
échéance. Un butin de groupe à un seul groupe remplit l'emplacement 0 et laisse 1 et 2 à zéro —
c'est déjà ce que fait le local.

## 5. La règle d'ouverture

### 5.1 Unité de temps : le tick de 10 ms

`ar_time` (`ar_time_t` = `uint32`, rzu `librzu/src/lib/Packet/GameTypes.h:44`) est en **tick de
10 ms** (100 ticks/s) : `World::GetArTime()` renvoie `ms / 10`
(NGemity `Chihiro/src/World/World.cpp:52-55`), le serveur officiel fait de même (`GetArTime`
0x140011B10 : `GetTickCount()/10`), et le client appelle la même fonction (client 7.3
`SFrame.exe`, `Process` 0x6CA478 ; nommée `GetArTime` 0x6F1890 dans le client de décembre 2011).
C'est l'unité déjà établie par le dépôt (`socle-invocation-monde.md:226`,
`socle-visibilite-joueurs.md:208-209`, et `ServerClock.TicksPerSecond = 100` —
100 ticks par seconde — à `Game/Services/ServerClock.cs:15`).

**Conséquence** : les seuils `3000 / 4000 / 5000` valent **30 s / 40 s / 50 s**, pas 3/4/5 s.

### 5.2 Ce que fait le client 7.3 (c'est lui qui a le clic)

`SGameItem::SetPickUpOrder` recopie `drop_time`, `hPlayer[0..2]` et `nPartyID[0..2]`, puis arme un
compteur d'état :

- si `hPlayer[0] == 0` **et** `nPartyID[0] == 0` (ordre vide, butin de raid) → état = **3** ;
- sinon → état = **0**.

`SGameItem::Process` recalcule cet état à chaque passe, à partir de
`GetArTime() - drop_time` :

| Écoulé depuis la chute | État | Qui le client laisse cliquer (`IsPickable`) |
|---|---|---|
| ≤ 3000 ticks (30 s) | 0 | **personne** |
| 3000 → 4000 (30-40 s) | 1 | emplacement 0 seulement |
| 4000 → 5000 (40-50 s) | 2 | emplacements 0 et 1 |
| > 5000 (50 s) | 3 | **tout le monde** |

`SGameItem::IsPickable(handle, partyID)` compare le handle à `hPlayer[i]` et l'id de groupe à
`nPartyID[i]` pour `i < état` et répond vrai sur la première correspondance ; état 3 court-circuite
et répond vrai à tout appelant.

Sources (client 7.3, `reference/client73/SFrame.exe`, lecture seule) : `SetPickUpOrder` 0x6CA200
(lecture de `[pkt+0x36]`, `[pkt+0x3a]`, `[pkt+0x46]`, écriture de l'état 3 ou 0 à +0x128C) ;
`Process` **0x6CA340-0x6CA4E4** (les trois comparaisons `cmp eax,0xbb8` / `0xfa0` / `0x1388` aux
adresses 0x6CA475 / 0x6CA493 / 0x6CA4B2) ; `IsPickable` 0x6CA270. Les mêmes fonctions existent
nommées dans le client de décembre 2011 livré avec son PDB :
`SGameItem::SetPickUpOrder` 0x6C7D50, `SGameItem::IsPickable(unsigned int, int)` 0x6C7DC0,
`SGameItem::Process(unsigned long, unsigned long)` 0x6C7E90 (seuils 0x6C7FDE / 0x6C7FFC / 0x6C801B).

**Aucun Lua ne porte ce délai** : les 42 scripts Lua du dépôt de référence sont des scripts
*serveur* et aucun ne mentionne le ramassage, et le client 7.3 fourni n'a pas de Lua extrait
(`client73/extraction-manifest.json` ne recense que 50 `db_*.rdb`). La fenêtre vit dans le binaire.

### 5.3 Ce que revérifie le serveur officiel

`onTakeItem(StructPlayer*, TS_CS_TAKE_ITEM*)` (PDB officiel, **0x140134080**), dans l'ordre :

1. objet introuvable → `TS_RESULT_NOT_EXIST` (1) ; déjà pris / pas propriétaire → `NOT_OWN` (3) ;
2. trop lourd → `TOO_HEAVY` (11) ; distance > `GameRule::GetPickableRange()` → `TOO_FAR` (2) —
   cette borne vaut **20** (`0x140200F00` : `mov eax,0x14`), alors que le local utilise 300
   (`GroundItemService.cs:25`) ;
3. objet de quête non ramassable par ce joueur → `ACCESS_DENIED` (6) ;
4. **la boucle d'ordre** (0x140134848-0x140134890), identique à NGemity
   `WorldSession.cpp:1255-1271` : `drop_duration = GetArTime() - m_nDropTime`, `ry = 3000` ;
   pour `i` de 0 à 2 : emplacement vide → **accepté** ; emplacement qui me nomme (mon handle, ou
   l'id de mon groupe) → **accepté** ; sinon `drop_duration < ry` → `ACCESS_DENIED`, puis
   `ry += 1000`. Épuisement de la boucle → **accepté** ;
5. or seulement : plafond de bourse → `TOO_MUCH_MONEY` (53) ;
6. sinon `TS_SC_TAKE_ITEM_RESULT` (210) diffusé à la région.

Un butin de monstre est donc ramassable : par son ayant droit **tout de suite côté serveur**
(la boucle sort sur la correspondance), par un tiers après 3000 ticks + 1000 par emplacement
occupé non-correspondant (30 / 40 / 50 s), et par **n'importe qui** une fois la boucle épuisée.

### 5.4 Les deux références comparées

| Situation | Serveur officiel | Client 7.3 (le clic) |
|---|---|---|
| emplacement 0 me nomme | accepté à t=0 | clic à partir de **30 s** |
| emplacement 1 me nomme | accepté à 30 s | clic à partir de **40 s** |
| emplacement 2 me nomme | accepté à 40 s | clic à partir de **50 s** |
| aucun emplacement ne me nomme | refusé jusqu'à 50 s | clic seulement au-delà de 50 s |
| ordre vide (raid, butin commun) | accepté à t=0 | clic à t=0 (état 3) |

Le client est **plus strict** que le serveur d'un cran : il ne laisse pas partir la demande avant
l'échéance, si bien que le comportement visible en jeu est celui du client — 30 s pour l'ayant droit,
puis ouverture par paliers jusqu'à 50 s pour tous. Le serveur officiel seul est plus permissif
(sa borne de 30 s ne s'applique qu'au *premier emplacement non-correspondant*).

Le critère n'est **que** le délai depuis `drop_time` : ni distance (le client n'en teste aucune,
le serveur teste 20 unités pour tout le monde), ni sortie de zone, ni composition du groupe au
moment du clic.

## 6. L'état local exact

Ce qui existe et qu'il ne faut pas réinventer :

| Élément | Comportement actuel | Source |
|---|---|---|
| Ordre envoyé | emplacement 0 seul : `hPlayer[0]` = handle du tueur, `nPartyID[0]` = id du groupe à la chute | `GroundItemService.cs:510-511`, `GameSpawnPackets.cs:190-191` |
| Butin non-monstre (jet d'inventaire) | `nPartyID[0] = 0`, objet personnel | `GroundItemService.cs:511` |
| `drop_time` envoyé | **`ServerClock.Now + ClientClockOffset` au moment de l'envoi** | `GroundItemService.cs:508` |
| Base de temps | `ServerClock` en ticks de 10 ms, `ClientClockOffset` recalé sur le client | `Game/Services/ServerClock.cs:15`, `GameClient.cs:165-194`, `:274` |
| Droit de ramassage | propriétaire, **ou** butin de monstre et membre du même groupe en ligne | `GroundItemService.cs:294-296`, `PartyService.cs:309-318` |
| Portée | 300 unités (serveur officiel : 20) | `GroundItemService.cs:25`, `:430-435` |
| Demande 204 → 210 | déjà traité et dispatché | `GameClient.cs:1622-1634`, `:3084-3088` |
| Disparition | `ExpiresAt` = chute + `GroundItemLifetimeSeconds` (120 s par défaut) | `GroundItemService.cs:91`, `RatesOptions.cs:38` |
| Re-présentation | `Sync` renvoie l'entrée à chaque ré-acquisition de vue, en recalculant `drop_time` | `GroundItemService.cs:448-455`, `:502-513` |

Ce qui manque, précisément :

1. **`drop_time` n'est pas l'instant de la chute.** `ShowTo` écrit `Now + ClientClockOffset` : pour
   le premier envoi c'est à peu près l'instant de la chute, mais tout `Sync` ultérieur (entrée en
   jeu `GameClient.cs:882`, tick de visibilité `GameActions.cs:424`, retour de warp
   `WarpService.cs:96`) **redate la chute à maintenant**. Un joueur qui entre dans la zone 20 s
   après la chute voit l'objet **verrouillé 30 s de plus** au lieu d'être déjà dans sa fenêtre.
   L'objet ne mémorise aujourd'hui que `ExpiresAt`, jamais l'instant de la chute
   (`GroundItem.cs:21`).
2. **Aucune fenêtre de temps côté serveur.** `CanTake` n'a pas d'horloge : un membre du groupe
   ramasse à l'instant même de la chute (alors que le client, lui, bloque 30 s), et **un tiers ne
   peut jamais ramasser**, même très longtemps après (il n'existe pas de « puis tous »).
   `TakenBy` ne sert qu'au verrou d'exclusion (`GroundItemService.cs:265`).
3. **Les emplacements 1 et 2 ne sont jamais renseignés** ; c'est cohérent avec un butin à un seul
   groupe (le serveur officiel fait de même) mais exclut la priorité multi-parties.

## 7. LE DÉCOUPAGE

### 7.1 Sous-ensemble minimal, implémentable sans inventer de politique

Ces trois points sont **établis par la référence** et ne demandent aucune décision de jeu :

1. **Envoyer le véritable instant de chute** dans `drop_time` (emplacement 42), en ticks et dans la
   base d'horloge du destinataire, au lieu de l'instant d'envoi. L'instant de chute est fixé à la
   création de l'objet (`DropForMonster`/`DropFromInventoryAsync`, `GroundItemService.cs:106-108`,
   `:219`) et `ShowTo`/`Sync` doivent le réutiliser tel quel. Garantit que les 30/40/50 s du client
   comptent depuis la chute, y compris pour un arrivant tardif.
2. **La fenêtre serveur** calquée sur `onTakeItem` (0x140134848) : ramassage accepté si l'emplacement
   occupé me nomme, sinon refusé tant que `GetArTime() - drop_time < 3000`, et **accepté par tous**
   (plus de condition de groupe) au-delà de l'échéance de la boucle — donc à 30 s avec un seul
   emplacement, 50 s avec trois. Refus en `TS_RESULT_ACCESS_DENIED` (6) avant l'échéance.
3. **Conserver `hPlayer[0]` et `nPartyID[0]` tels quels** — c'est déjà ce que le client lit, et
   c'est ce qui fait que le groupe du tueur est l'ayant droit de l'emplacement 0.

### 7.2 Ce qui reste dehors, et son prérequis

| Mécanisme | Pourquoi hors de cette branche | Prérequis / paquet |
|---|---|---|
| Priorité sur 3 emplacements (les 3 premières parties contributrices) | le dépôt ne tient **aucun** compteur de contribution par partie : le socle de récompenses distribue à parts égales (`socle-groupe.md` §5.1). Inventer un ordre serait inventer une politique. | table de contribution par monstre à construire ; `hPlayer[i]`/`nPartyID[i]` (46+4i / 58+4i) sont déjà sur le fil |
| Objets de quête ramassables seulement par le quêteur (`ACCESS_DENIED` officiel) | la règle officielle dépend des valeurs de quête actives (`QuestManager::IsTakeableQuestItem`) ; le local a `QuestService`, à confronter dans une fiche dédiée | demande 204, exige `TS_RESULT_ACCESS_DENIED` (6) |
| Portée de ramassage 20 unités au lieu de 300 | décision de gameplay : le local a choisi 300 et l'a testé, l'officiel dit 20 | `GameRule::GetPickableRange` = 0x14 (0x140200F00) ; `GroundItemService.cs:25` |
| Contrôle de poids à la prise (`TOO_HEAVY`) | le local a `ICarriedWeightService`, l'appel existe déjà ailleurs | `onTakeItem` officiel ; NGemity le marque `TODO` |
| Or partagé au ramassage, plafond de bourse (`TOO_MUCH_MONEY`) | déjà implémenté localement (`GroundItemService.TakeGold`), hors de l'ordre de ramassage | `Game/Services/GroundItemService.cs:349-397` |
| Durée de vie au sol (disparition) | valeur officielle non établie (config d'exécution) ; le local tient 120 s | voir `## A VERIFIER PAR KILLIAN` |

**Décision demandée au PO :** la branche peut porter §7.1 seul (correctif de `drop_time` + fenêtre
serveur + tests), et la priorité multi-emplacements devient une carte séparée. Si le PO préfère
n'ouvrir qu'une branche, §7.1 reste le périmètre, §7.2 la réserve.

### 7.3 Tests d'offsets attendus

La discipline du dépôt impose un test d'offsets pour tout ce qui touche au paquet : longueur 70,
`drop_time` en 42, `hPlayer` en 46/50/54, `nPartyID` en 58/62/66, `item_code` en 26 (`EncodedInt`),
`count` en 34 ; plus, pour la fenêtre, les paliers 3000 / 4000 / 5000 ticks et le passage « à tous »
après l'échéance.

## 8. Écarts assumés avec NGemity

| Point | NGemity | Ce que dit l'officiel / le client 7.3 | Écart retenu |
|---|---|---|---|
| Format `pick_up_order` | 28 octets en 7.3 | identique (PDB serveur et client) | aucun |
| Unités des paliers | `ry = 3000`, `+= 1000` sans commentaire d'unité (`WorldSession.cpp:1256-1268`) | `ro = GetArTime()` en ticks de 10 ms → **30/40/50 s** | NGemity est juste en code, trompeur en unité ; c'est cette fiche qui fixe les secondes |
| Sortie de boucle sur correspondance | ne sort pas : continue à évaluer les emplacements suivants et peut donc refuser l'ayant droit à cause d'un emplacement plus bas | l'officiel sort de la boucle (`je 0x1401348EB`) et accepte | divergence réelle ; le local ne doit pas copier la boucle de NGemity telle quelle |
| Portée de ramassage | 20 (`GameRule::GetPickableRange`) | 20 | le local garde 300 (assumé, hors socle) |
| Poids | `TODO` | contrôle `TOO_HEAVY` présent | hors socle |
| Diffusion du `TS_SC_TAKE_ITEM_RESULT` (210) | à la région | à la région | local : à confronter dans la fiche du 210 |

## 9. NON ÉTABLI

- **La valeur du diviseur de `GetArTime`** (global `0x1404EFC20` côté serveur, `0xC53A18` côté
  client) : lue à l'exécution, elle pourrait changer l'échelle. Retenu : 10 ms, parce que c'est
  l'unité déjà établie par le dépôt et que `ms/10` est écrit noir sur blanc dans NGemity
  (`World.cpp:52-55`). Si elle valait autre chose, tous les délais de cette fiche seraient à
  remettre à l'échelle.
- **La durée de vie d'un objet au sol** : `ItemCollector::onProcess` (0x140382E10) la tire d'une
  configuration d'exécution absente du dépôt (`CONFIG_ITEM_HOLD_TIME` par défaut 18000 ms = 18 s
  chez NGemity `World.cpp:128`, 120 s chez nous `RatesOptions.cs:38`). Aucune valeur officielle
  établie.
- **La fonction exacte qui décide de la disparition et du message associé** (le binaire écrit
  `0x73` = 115 dans un en-tête à 0x140382F2C, non identifié).
- **Le critère de remplissage des emplacements 1 et 2** (contribution exacte) : non établi par
  lecture du binaire ; seul le portage NGemity (`Monster.cpp:144-164`) est lisible.
- **Ce que fait le client si `drop_time` est dans le futur** (horloge serveur en avance) : la
  comparaison est non signée (`ja`), un `drop_time` futur donnerait un écoulé énorme et un objet
  immédiatement « à tous ». Non observé.

## 10. Ce que cette branche livre (implémentation)

Le paquet n'a **pas bougé d'un octet** : les trois points du §7.1 sont dans le service, plus la règle de
fenêtre extraite pour être testable et une horloge injectable pour l'atteindre en test.

| Livré | Où | Ce que ça fait |
|---|---|---|
| Instant de chute mémorisé | `Game/Services/GroundItem.cs` (`DropTime`, `uint`, ticks serveur) | Fixé à la création de l'objet, il ne bouge plus |
| `drop_time` réel | `GroundItemService.ShowTo` | `unchecked(item.DropTime + info.ClientClockOffset)` : le décalage du destinataire, jamais l'instant d'envoi — `Sync` et un retour de warp rejouent donc le même instant |
| Horloge injectable | constructeur `GroundItemService(..., Func<uint> clock = null)` | `() => ServerClock.Now` par défaut ; même couture que `FieldPropStates` et `HuntaholicService`, c'est ce qui permet aux tests d'atteindre 30 s sans dormir |
| Règle de fenêtre | `Game/Services/GroundItemPickupRules.cs` | La boucle de `onTakeItem` : slot vide ou nommé → accepté ; slot occupé non-correspondant → refus tant que `elapsed < 3000 + 1000 × (slots déjà sautés)` ; ordre épuisé → accepté. Le local ne remplit que le slot 0, donc `occupiedSlots` vaut 0 ou 1 en production |
| Refus | `GroundItemService.TakeAsync` | `TS_RESULT_ACCESS_DENIED` (6) hors fenêtre ; `NOT_EXIST` (1) reste pour l'objet absent, `TOO_FAR` (2) pour la portée, `TOO_HEAVY` (11) pour le poids |
| Même règle pour l'animal | `TryFindNearest` / `TakeForPetAsync` | Les deux passent par le même `CanTake` : rien à viser avant l'échéance, l'objet après |

Vérification : `dotnet build Navislamia.sln -c Debug` code 0 ; `dotnet test Tests/Tests.csproj` code 0,
3751 tests (base `b2b260c` : 3732, aucun perdu). Nouveaux fichiers de test :

| Fichier | Ce qu'il épingle |
|---|---|
| `Tests/Game/GroundItemEnterItemTests.cs` | 70 octets, id 3, `type`/`handle`/`x`/`y`/`z`/`layer`/`objType` en 7/8/12/16/20/24/25, `code` en 26 (`EncodedInt` : mots à 26/30, moitiés à 28/32), `count` en 34 (64 bits), `drop_time` en 42, `hPlayer` en 46/50/54, `nPartyID` en 58/62/66 |
| `Tests/Game/GroundItemPickupRulesTests.cs` | Les paliers 3000/4000/5000 ticks, soit 30/40/50 s, un cran d'écart (2999/3999/4999 refusés) ; ordre vide et slot nommé acceptés à t=0 |
| `Tests/Game/GroundItemTakeWindowTests.cs` | Étranger refusé en `ACCESS_DENIED` (6) à 29,99 s puis accepté à 30,00 s ; propriétaire et membre de groupe acceptés à t=0 ; `drop_time` identique après un `LeaveWorld`/`Sync` à +250 ticks ; décalage d'horloge appliqué par destinataire |

**Réserve propre à l'implémentation.** La fenêtre s'applique à **tout** objet au sol, y compris le jet
d'inventaire et l'objet de quête : leur entrée remplit elle aussi `hPlayer[0]` avec le personnage et laisse
`nPartyID[0]` à zéro, donc la boucle les ouvre à tout le monde au-delà de 30 s. Le verrou « le
propriétaire seul, indéfiniment » que le local appliquait à ces deux cas disparaît. C'est la lecture fidèle
des références, mais c'est un changement visible — point 7 ci-dessous.

Le slot 0 est nommé par le **handle du fil** (`hPlayer[0]`), comme le client le compare, et par l'identité
de l'objet client : une reconnexion du même personnage reste donc ayant droit, là où l'ancienne règle ne
connaissait que l'objet. `PetPickupTests` donnait le même `CharacterHandle` à ses deux personnages ; sa
fixture en reçoit un distinct (`0x80000002`), l'assertion « le butin n'est qu'à son tueur » étant conservée
et devenant enfin réelle.

**Quelle des deux cadences est implémentée.** C'est celle de la **boucle du serveur officiel** (§7.1 :
30 s dès qu'un emplacement est occupé, 40/50 s à deux et trois), pas l'échelle du client, qui n'ouvre
l'état « tout le monde » qu'après 5000 ticks et laisse donc un tiers attendre 50 s même quand un seul
emplacement est rempli (§5.2, §5.4). Ce point reste ouvert au **point 2** ci-dessous : si Killian tranche
pour le client, il faudra ouvrir à 50 s en toutes circonstances, ce qui ne change qu'une constante de la
règle et la sortie de `GroundItemPickupRules`.

## 11. Relecture du 2026-10-05 (Claude), contre la source officielle 2015

La source `Game/Message/GameMessage.cpp` (`onTakeItem`) et `Game/Rule/GameRule.h:480`
(`GetPickupOrderTime(idx) = 3000 + idx × 1000`) confirment la boucle et les paliers 30/40/50 s. Quatre corrections :

1. **Le client ne bloque pas les clics.** `SGameItem::IsPickable` (`0x6C2950` dans le client PDB de décembre 2011)
   n'a **qu'un appelant : `SGameLocalPet::CmdIdle`**, le ramassage automatique du familier. Le joueur clique librement
   et c'est le serveur qui juge. Conséquences : le §5.2 décrit le familier, pas le joueur ; au protocole de la MR,
   l'étape 2 est fausse (le tueur ramasse **tout de suite**), l'étape 4 aussi (un tiers ramasse dès **30 s**) et le
   refus `ACCESS_DENIED` (6) **est** observable en jeu : un tiers qui clique avant 30 s le reçoit.
2. **Le familier suit le client** (`GroundItemPickupRules.PetMayCollect`) : ce que son maître a droit de prendre
   passé 30 s, tout le reste passé 50 s, tout de suite pour un ordre vide. Il ramassait jusqu'ici le butin de son
   maître immédiatement, et la branche lui aurait fait prendre celui des autres à 30 s.
3. **Ordre des refus** : `TOO_FAR` (2) est jugé avant l'ordre de ramassage, comme `onTakeItem`.
4. **Quels objets ont un ordre** : seul un butin de monstre en reçoit un (`StructMonster.cpp:1673, 1768`). Un objet
   **jeté par un joueur** (203) part avec un ordre **vide** (`hPlayer[0] = 0` sur le fil) : n'importe qui le prend
   tout de suite — ce qui tranche le point 7 ci-dessous. Un **objet de quête** reste à son propriétaire quel que soit
   le temps (`IsTakeableQuestItem`, approché par « son propriétaire ») — la branche l'ouvrait à tous après 30 s.
   `GroundItem.QuestItem`, `GroundItem.HasPickupOrder`.

Points tranchés de la liste ci-dessous : 2 (la cadence du serveur est l'officielle ; celle du client ne concerne que
le familier), 6 (objet de quête), 7 (jet d'inventaire : ordre vide). Restent ouverts : 3 (portée 300 contre
20 + demi-taille), 4 (emplacements 1-2), 5 (durée de vie), 8 (`NOT_OWN`, qui chez l'officiel vise un objet déjà dans
un inventaire).

Tests ajoutés : `GroundItemTakeWindowTests` (objet de quête gardé, portée avant l'ordre, familier 30/50 s ; le harnais
utilise un tas d'or de monstre, qui porte un ordre), `PetPickupTests.A_dropped_object_has_an_empty_order_and_anybody_takes_it_at_once`,
`MonsterRewardIntegrationTests` (le familier attend 30 s pour l'or de son maître).

## A VERIFIER PAR KILLIAN

1. **Le chiffre public des paliers.** `203-drop-item.md:285` et `CLAUDE.md:1931` annoncent un
   verrou « 3 s/4 s/5 s » ; les mesures ci-dessus donnent **30 s/40 s/50 s** (3000/4000/5000 ticks
   de 10 ms, §5.1-5.2). Confirmer avant de recopier le bloc `CLAUDE.md` et de corriger le 203.
   La correction du 203 est un fichier de doc, à faire dans une carte dédiée : je n'y touche pas.
2. **« Puis tous » à 50 s contre 30 s.** Le serveur officiel ouvre à l'échéance de la boucle
   (30 s avec un emplacement) tandis que le client n'autorise le clic qu'à 40 s/50 s, et le refus
   tombe en `ACCESS_DENIED`. Le local doit-il se caler sur le client (plus strict, ce que voit le
   joueur) ou sur le serveur officiel ? La fiche propose le client ; à trancher.
3. **Portée 300 → 20 ?** L'officiel vaut 20 unités pour tout le monde (`0x140200F00`), le local 300.
   Hors périmètre de cette branche, mais c'est un écart de gameplay connu qui change l'expérience.
4. **Priorité multi-emplacements** : la construire suppose un compteur de contribution par partie
   que le dépôt n'a pas. Ouvrir une carte dédiée, ou renoncer explicitement à la fidélité officielle ?
5. **Durée de vie au sol** : 18 s (NGemity), 120 s (nous), ou la valeur de configuration officielle
   non établie ? Aucun des deux n'est mesuré sur le client 7.3.
6. **Objets de quête** au sol : l'officiel refuse `ACCESS_DENIED` à qui n'a pas la quête active.
   Politique à valider (le local accepte aujourd'hui tout membre du groupe).
7. **La fenêtre ouvre aussi les jets qui ne sont pas des butins** (ajouté par le dev, §10). Un objet lâché
   par `TM_CS_DROP_ITEM` (203) et un objet de quête portent `hPlayer[0]` = leur propriétaire et
   `nPartyID[0]` = 0 : la boucle les ouvre donc à tout le monde après 30 s, alors que le local n'acceptait
   jusque-là que le propriétaire, indéfiniment. C'est la lecture fidèle des deux références, mais c'est un
   changement visible : confirmer, ou restreindre la fenêtre aux butins de monstre (`MonsterDrop`).
8. **`NOT_OWN` (3) n'est pas émis** (ajouté par le dev, §10). L'officiel répond `NOT_OWN` pour « déjà pris
   ou pas propriétaire » avant d'entrer dans la boucle ; ce lot ne l'a pas porté, aucun test ne l'exigeait,
   et il répond `NOT_EXIST` (1) pour l'objet absent comme pour l'objet en cours de ramassage, la fenêtre
   restant seule en `ACCESS_DENIED` (6). À trancher si la fiche du 210 doit porter `NOT_OWN`.

## Bloc pour CLAUDE.md

```
- **Objets au sol — l'ordre de ramassage de `TM_SC_ENTER` (3).** L'entrée d'objet est 7 + 1 + 4 +
  12 + 1 + 1 + **44** = **70 octets** ; les 44 de `itemInfo` sont 8 (`code`, `EncodedInt` randomisé)
  + 8 (`count`, `uint64` depuis `EPIC_4_1`) + 28 (`pick_up_order` = `drop_time` 4 + `hPlayer[3]` 12
  + `nPartyID[3]` 12). `layer`/`objType` sont en 24/25 et le mot `unknown` de `pick_up_order`
  n'existe qu'à partir d'`EPIC_9_6_7` (`rzu TS_SC_ENTER.h:29-44,145-169` ; PDB officiel
  `TS_ENTER::ItemInfo`/`TS_ITEM_PICK_UP_ORDER` ; analyseur du client 7.3 à 0x6CA200).
  `drop_time` est un `ar_time` en **tick de 10 ms** et doit porter **l'instant de la chute**, jamais
  l'instant d'envoi : le client recalcule `GetArTime() - drop_time` et ouvre le ramassage par paliers
  — personne avant **3000 ticks (30 s)**, l'emplacement 0 de 30 à 40 s, les emplacements 0 et 1 de
  40 à 50 s, **tout le monde au-delà** (`SGameItem::Process` 0x6CA340, `SGameItem::IsPickable`
  0x6CA270 ; portage lisible `WorldSession.cpp:1255-1271`). Le serveur officiel refait la même
  boucle (`onTakeItem` 0x140134080, `ry = 3000 + 1000` par emplacement occupé non-correspondant) et
  accepte tout de suite l'emplacement qui nomme le joueur. Le chiffre « 3 s/4 s/5 s » de
  `203-drop-item.md` était faux d'un facteur 10.
```

### Bloc à coller — implémentation (ajouté par le dev, §10)

```
- **Objets au sol — l'instant de chute et la fenêtre de ramassage.** `drop_time` (entrée d'objet
  `TM_SC_ENTER` 3, offset 42) porte l'instant où l'objet est tombé, en ticks serveur de 10 ms, décalé dans
  la base d'horloge du destinataire (`GroundItem.DropTime`, `GroundItemService.ShowTo`) : un `Sync` ou un
  retour de warp ne le redate **jamais**, sans quoi un arrivant tardif verrait l'objet reverrouillé 30 s.
  Côté serveur, `GroundItemPickupRules` rejoue la boucle de `onTakeItem` : le slot 0 nommé (le tueur ou son
  groupe) prend tout de suite, un tiers est refusé en `TS_RESULT_ACCESS_DENIED` (6) tant que
  `GetArTime() - drop_time < 3000` puis accepté — 30 s avec un slot rempli, 40/50 s avec deux ou trois, et
  l'ordre vide est à tous. Le local ne remplit que le slot 0 : la priorité aux trois parties contributrices
  reste une carte séparée. `Func<uint> clock` au constructeur de `GroundItemService` est la couture
  d'horloge des tests, comme dans `FieldPropStates`.
```

## Commits épinglés

- **Navislamia** : `master` `b2b260c005359237a019e65244a54c95cf45d055` (branche posée depuis ce commit).
- **rzu** : `87c1e83bf84efe29bb6405e8e6da80349712f3fa` — `librzu/src/packets/GameClient/TS_SC_ENTER.h`, `librzu/src/lib/Packet/GameTypes.h`, `EncodingRandomized.h`.
- **NGemity (Chihiro)** : `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` — `src/World/World.cpp:52-55` (`GetArTime`), `src/Network/GameNetwork/WorldSession.cpp:1226-1285` (`onTakeItem`), `src/Entities/Monster/Monster.cpp:121-190` et `:500-620` (remplissage de l'ordre), `src/Globals/GameRule.cpp:126-129` (`GetPickableRange`).
- **Serveur officiel Gala Lab** : `reference/epic7part4/server/2012-11/CaptainHerlockServer.exe` + `.pdb` (branche `gameserver_release/2011-12-12`, build 2012-11-22, RSDS `4fab8d7b…`, age 1) — `onTakeItem` 0x140134080, `GetArTime` 0x140011B10, `MonsterDropItemToWorld` 0x140043CC0 (`m_nDropTime` à 0x140043D76), `GameRule::GetPickableRange` 0x140200F00, `ItemCollector::onProcess` 0x140382E10 ; types `TS_ENTER::ItemInfo` (0x826C), `TS_ENTER::TS_ITEM_PICK_UP_ORDER` (0x825F), `StructItem::ITEM_PICK_UP_ORDER` (0x7FC5), `takePriority` (0x137FF).
- **Client 7.3** : `reference/client73/SFrame.exe` (lecture seule, pas de PDB) — `SetPickUpOrder` 0x6CA200, `IsPickable` 0x6CA270, `Process` 0x6CA340 (seuils 0x6CA475 / 0x6CA493 / 0x6CA4B2).
- **Client de décembre 2011 avec PDB** : `reference/epic7part4/client-pdb/2011-12-14-part4-design/SFrame_Release.exe` + `.pdb` — `SGameItem::SetPickUpOrder` 0x6C7D50, `SGameItem::IsPickable(unsigned int,int)` 0x6C7DC0, `SGameItem::Process(unsigned long,unsigned long)` 0x6C7E90, `GetArTime` 0x6F1890 ; types `SMSG_ENTER::ItemInfo`, `SMSG_ENTER::TS_ITEM_PICK_UP_ORDER`, `TS_ENTER::TS_ITEM_PICK_UP_ORDER`.
