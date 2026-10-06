# Socle ferme de créatures — portage officiel (6000-6008)

Fiche **compagnon** de `docs/packet-specs/socle-ferme-creatures.md`. Elle ne remplace pas la fiche
d'origine : elle **remplit son §7** à partir de la source officielle désormais disponible et **lève
explicitement la décision du 2026-09-30** (voir §8). Le texte de la fiche d'origine n'est pas
réécrit et **aucune de ses lignes n'est supprimée** : seul un **avertissement d'obsolescence daté** a
été ajouté en tête de sa section « Décision de Killian » — le **second** `## 8`, ligne 508 de ce
fichier-là, qui en porte deux (« Références épinglées » ligne 475 puis « Décision de Killian »).

Texte de la décision levée (`socle-ferme-creatures.md`, 2026-09-30) :

> « Les trames 6000-6008 sont prises en charge au niveau lecture/écriture de trame, mais le système de
> ferme n'est pas implémenté : le serveur ne stocke aucune ferme, ne consomme aucun ticket et ne
> renvoie qu'un `TM_SC_FARM_INFO` vide. »

Décision de Killian du 2026-10-06 : **cet arbitrage est périmé**. Le sous-ensemble 7.3 minimal passe
en portée d'implémentation : stockage, drapeau, `6001` rempli, fenêtre, `6008`, constantes (§5.6).

## 1. Identité

Neuf trames, id et nom lus dans `op_codes.md:256-264` (dépôt) et confirmés par la source officielle :

| id | nom | source officielle |
|---|---|---|
| 6000 | `TM_CS_REQUEST_FARM_INFO` | `official2015/GameServer/Game/Message/GameMessage.h:356` |
| 6001 | `TM_SC_FARM_INFO` | `GameMessage.h:357` |
| 6002 | `TM_CS_FOSTER_CREATURE` | `GameMessage.h:358` |
| 6003 | `TM_SC_RESULT_FOSTER` | `GameMessage.h:359` |
| 6004 | `TM_CS_RETRIEVE_CREATURE` | `GameMessage.h:360` |
| 6005 | `TM_SC_RESULT_RETRIEVE` | `GameMessage.h:361` |
| 6006 | `TM_CS_NURSE_CREATURE` | `GameMessage.h:362` |
| 6007 | `TM_SC_RESULT_NURSE` | `GameMessage.h:363` |
| 6008 | `TM_CS_REQUEST_FARM_MARKET` | `GameMessage.h:364` |

**Six des neuf ids sont déjà déclarés** — `GamePackets.cs:447-452` : 6000, 6001, 6002, 6004, 6006,
6008 — et tous sont lus par `Game/Network/Clients/GameClient.cs:1047-1160` (aiguillages
`:3676-3720`). **Les trois trames de résultat 6003/6005/6007 ne sont pas déclarées** : le commentaire
`GamePackets.cs:442-446` le dit explicitement, au motif que leur octet `result` n'était pas établi —
motif que **cette fiche lève** (§3.4). Le lot qui émet 6002/6004/6006 doit donc déclarer les trois
ids **dans le même commit que leurs `case`** (critère d'acceptation 4) et mettre le commentaire à
jour. Le lot `hermes/packet-socle-ferme-creatures` a livré les codecs (`GameFarmPackets.cs`) et est
mergé sur `master` (`8b3061aa`). Cette fiche ne touche pas au code serveur.

## 2. Ce que le joueur fait pour que le client l'envoie

Le seul point d'entrée est le PNJ « ferme de créatures ». La source donne son nom et ses menus :
`Resource/Script/NPC_Creature_farm.lua:61-71` (`NPC_Creature_Farm_contact`) — titre `@91000350`
(« créature farm owner Sonya »), texte `@91000351`, menu `@91000352` (`NPC_Creature_Farm_explain()`,
explications) et menu `@91000353` → `show_creature_farm_window()` (gestion des créatures). Le nom du
PNJ est posé par `NPC_Creature_Farm_init` (`NPC_Creature_farm.lua:14-17`, `@91000349`).

| geste joueur | trame |
|---|---|
| ouvre la fenêtre / bouton « rafraîchir » | 6000 |
| « confier » (bouton `assign`) : carte + tickets (+ crackers) | 6002 |
| « reprendre » (bouton `regain`) | 6004 |
| « soigner » (`button_ministration_01..03`) | 6006 |
| bouton d'achat de ticket (`button_tiket_buy_01`) | 6008 |

Les noms de boutons sont mesurés dans le client 7.3 (`client73/SFrame.exe`, chaînes lues aux
offsets **fichier** `0x63e810` `button_regain_01`, `0x63e888/0x63e8a0/0x63e8b8`
`button_ministration_03/02/01`, `0x63ea70` `button_tiket_buy_01`, `0x63e788` `tiket_icon_slot_%02d`,
`0x63e7a0` `food_icon_slot_%02d`, `0x63e86c` `creature_card_icon_slot_01`,
`0x63e6c4/0x63e6e4/0x63e704/0x63e72c`
`icon_commerce_farmticket_{senior,junior}[_premium]`, fenêtre `window_creature_farm.nui` à
`0x642610`, aide `window_creature_farm_help.nui` à `0x6425f0`). Les deux **légendes** de bouton
`regain` (`0x63ea48`) et `assign` (`0x63ea50`) sont voisines, et les **boîtes de confirmation** de
ces deux gestes sont `msgbox_creatureFarmAssign` (`0x641c70`) et `msgbox_creatureFarmRegain`
(`0x641c54`) ; les refus « pas d'assignation » sont `msgbox_creatureFarmDoNotAssign4..1`
(`0x641bd4`, `0x641bf4`, `0x641c14`, `0x641c34`).
Ce sont des **offsets fichier, pas des adresses virtuelles** (`SFrame.exe` : `image_base`
`0x400000`, `.text` en `RVA 0x1000` / `raw 0x400` — même convention que le commentaire de
`GameFarmPackets.cs`) : une adresse virtuelle équivalente n'aurait aucun sens ici, les chaînes
tombant hors des sections mappées. Le PNJ lui-même n'est pas établi (§7.3).

## 3. Structure sur le fil

En-tête commun : 7 octets, identique à la fiche d'origine (§3.0) ; la source officielle le confirme :
`TS_MESSAGE { unsigned int size; unsigned short id; unsigned char msg_check_sum; }`
(`GameMessage.h:432-441`).

### 3.1 `TM_CS_REQUEST_FARM_INFO` (6000) — **7 octets**

`GameMessage.h:3866-3869` : aucune charge utile.

### 3.2 `TM_SC_FARM_INFO` (6001) — **8 + 120 × N octets**

`GameMessage.h:3872-3891` : `char creature_count` (1), puis `creature_count` fois `SUMMON_INFO`.
Avec `N = creature_count` (0..3, §5.4), et en-tête 7 : **7 + 1 + 120 × N**.

| offset | type | nom | source / valeur établie |
|---|---|---|---|
| 0 | `uint32` | `size` | `GameMessage.h:439` |
| 4 | `uint16` | `id` = 6001 | `GameMessage.h:440`, `:357` |
| 6 | `uint8` | `msg_check_sum` | `GameMessage.h:441` |
| 7 | `int8` | `creature_count` | `GameMessage.h:3876` ; posé par `GameMessage.cpp:11760` (nombre d'entrées non nulles) |
| 8 | — | début du 1er `SUMMON_INFO` | `GameMessage.h:3878` |
| +0 | `int32` | `index` | `GameMessage.h:3879` ; **le numéro de case de ferme** : `GameMessage.cpp:11754` (`info->index = i`, `i` = slot 0..2, cf. §5.4) |
| +4 | `int64` | `exp` | `GameMessage.h:3880` ; `GameMessage.cpp:11715` = `creatureCard->GetSummonStruct()->GetEXP()`, c'est-à-dire **l'EXP déjà acquise par l'invocation**, pas la projection de la ferme (§5.3) |
| +12 | `char[19]` | `name` | `GameMessage.h:3881` ; `GameMessage.cpp:11716-11717` = `s_strcpy( name, _countof(name), … )` puis `name[_countof(name) - 1] = 0` : copie bornée à 19 octets, **dernier octet forcé à 0** (18 caractères visibles au plus) ; le tampon du message est mis à zéro avant remplissage (`GameMessage.cpp:11729`), donc les octets non écrits de la trame envoyée sont à 0 — la question ouverte §7.9 ci-dessous est **levée** |
| +31 | `int32` | `duration` | `GameMessage.h:3882` ; `GameMessage.cpp:11720` = `farmInfo->duration`, la valeur du ticket (§5.1) |
| +35 | `int32` | `elasped_time` | `GameMessage.h:3883` ; `GameMessage.cpp:11684` = `maintenant - registration_time`, **en secondes** |
| +39 | `int32` | `refresh_time` | `GameMessage.h:3884` ; `GameMessage.cpp:11705/11707` = secondes jusqu'au **prochain 06:00**, 0 si jamais soigné (§3.6) |
| +43 | `int8` | `using_cash` | `GameMessage.h:3885` ; `GameMessage.cpp:11718` = `is_cash` du dépôt (= ticket premium) |
| +44 | `int8` | `using_cracker` | `GameMessage.h:3886` ; `GameMessage.cpp:11719` = cracker consommé au dépôt |
| +45 | — | `card_info` | `GameMessage.h:3887` = `TS_ITEM_BASE_INFO` (`GameMessage.h:1390-1430`), rempli par `fillItemBaseInfo` (`GameMessage.cpp:11723`) : **la carte du joueur elle-même**, jamais une ligne de catalogue ; cela lève la question « `card_info` : quel objet ? » de la fiche d'origine (§7.9 là-bas) |
| +120 | — | `SUMMON_INFO` suivant | pas d'alignement intercalaire (source `#pragma pack(1)`, `ContentStruct.h:316`) |

`120 = 45 + 75` : le motif d'objet de 75 octets est déjà écrit par `GameFarmPackets.cs` (fiche
d'origine §3.7), et `45 = 4+8+19+4+4+4+1+1` est calculé ici. `_MARKET_INFO` mis à part, la source
officielle est packée sur 1 octet (`ContentStruct.h:316-345`), donc aucune surprise d'alignement.

### 3.3 `TM_CS_FOSTER_CREATURE` (6002) — **19 + 8 × T + 8 × C octets**

`GameMessage.h:3894-3913` : `AR_HANDLE creature_card_handle` (4), `int ticket_info_count` (4),
`int cracker_info_count` (4), puis `TICKET_INFO { AR_HANDLE ticket_handle; int ticket_count; }`
× T, puis `CRACKER_INFO { AR_HANDLE cracker_handle; int cracker_count; }` × C. La source lit
effectivement les tableaux à la suite du message (`GameMessage.cpp:11849-11852`), donc **T et C
portent chacun leur compte et l'ordre est ticket puis cracker**.

### 3.4 `6003` / `6005` / `6007` — **8 octets**

`TS_SC_RESULT_FOSTER { char result }` (`GameMessage.h:3916-3921`), idem `TS_SC_RESULT_RETRIEVE`
(`:3932-3937`) et `TS_SC_RESULT_NURSE` (`:3948-3960`).

**Valeurs tranchées** (elles étaient `NON ÉTABLIES` dans la fiche d'origine §7.5) :

| trame | valeurs | source |
|---|---|---|
| 6003 | `0` = refus, `1` = confiée | `GameMessage.cpp:11905` (`FosterCreature(...) ? 1 : 0`) |
| 6005 | `0` = refus, `1` = reprise | `GameMessage.cpp:11916` (`RegainSummon(...) ? 1 : 0`) |
| 6007 | `0` = `FAILED`, `1` = `NO_REWARD`, `2` = `REWARDED` | `GameMessage.h:3954-3959` ; `GameMessage.cpp:11927-11938` |

`6007` distingue donc bien trois cas : `FAILED` = la demande était invalide (aucune exécution),
`NO_REWARD` = soin effectué sans cadeau, `REWARDED` = soin effectué **et** cadeau donné. Le cadeau
est tiré par le Lua `NPC_Creature_Farm_nurse_handler` (`NPC_Creature_farm.lua:52-59` : `math.random(1,10)`,
cadeau si `< 6`, `insert_item(710009, 1)`), la valeur de retour `"1"` du Lua devenant `REWARDED`
(`GameMessage.cpp:11931-11933`).

### 3.5 `6004` / `6006` — **11 octets**

`TS_CS_RETRIEVE_CREATURE { AR_HANDLE creature_card_handle }` (`GameMessage.h:3924-3929`) et
`TS_CS_NURSE_CREATURE` (`:3940-3945`).

### 3.6 `TM_CS_REQUEST_FARM_MARKET` (6008) — **7 octets**

`GameMessage.h:3962-3965` : aucune charge utile. **La réponse est un `TM_SC_MARKET` (250)**, pas une
trame de ferme : `onRequestFarmMarket` prend `GameContent::GetMarketInfo("creature_farm")` et appelle
`SendMarketInfo(pClient, 0, pInfo)` (`GameMessage.cpp:11943-11952`), c'est-à-dire la fenêtre de
magasin standard, avec le handle PNJ **0**. `TS_SC_MARKET` en 7.3 :
`uint32 size; uint16 id = 250; uint8 check_sum; ar_handle npc_handle; uint16 items; suite de
{ int32 code; int64 price; int32 huntaholic_point; }` (rzu
`TS_SC_MARKET.h:5-31` : id `250` pour `version < EPIC_9_6_3`, `arena_point` seulement à partir de
`EPIC_8_1`, `huntaholic_point` à partir de `EPIC_5_2`). Soit **13 + 16 × N octets** — l'écrivain du
dépôt donne déjà la même taille (`GameTradePackets.cs:33` `MarketInfoHeaderSize = 13`).

**Piège de taille :** rzu déclare après la liste un `_(pad)( 4 * items.size(), item_list_marker )` sans gating de version (`TS_SC_MARKET.h:22-23`), ce qui donnerait `13 + 20 × N` ; le client 7.3 lit `items × 16` octets contigus à partir de l'offset 13, et le dépôt émet donc compact, sans ce remplissage (`GameTradePackets.cs:16-18`). **Ne pas écrire le pad** : il désaliigne la trame.

Le catalogue `creature_farm` existe des deux côtés : données 7.3
(`reference/sqlserver/Arcadia/MarketResource.csv:4468-4469`) et données client-ère 2011-12
(`epic7part4/csv/MarketResource.csv:684-685`) donnent `creature_farm` → `710005`, `710006` (prix
par `price_ratio`, cf. l'export du dépôt `DevConsole/market-catalog.73.json:4778-4791` :
`710005` = 200 000 rut, `710006` = 400 000 rut, `HuntaholicPoint 0`). Deux lignes → **45 octets**.

### 3.7 Récapitulatif des tailles

| trame | taille (octets) |
|---|---|
| 6000, 6008 | 7 |
| 6001 | 8 + 120 × N (N ∈ [0, 3]) → 8, 128, 248, 368 |
| 6002 | 19 + 8 × T + 8 × C |
| 6003, 6005, 6007 | 8 |
| 6004, 6006 | 11 |
| réponse 6008 (`TM_SC_MARKET` 250) | 13 + 16 × N ; 45 pour les deux lignes `creature_farm` |

## 4. Gating de version — tranché pour Epic 7.3

Statut de version des sources : `official2015` = arbre officiel de **2015, ère Epic 9.x** (extrait
copié le 2026-10-06, `official2015/README.md:3-4`) ; `epic7part4` = données/serveur de **2012-11,
ère Epic 7.x** ; `client73/SFrame.exe` = client de l'ère 7.3. rzu = `87c1e83` (librzu, « last tested
EPIC_9_8_1 »), NGemity = `38ceb2c`.

| champ / trame | gating rzu | tranché pour 7.3 |
|---|---|---|
| `6001` en-tête et champs `index`/`exp`/`duration`/`elasped_time`/`refresh_time`/`using_cash`/`using_cracker`/`card_info` | aucun gating (`TS_SC_FARM_INFO.h:9-20`) | **présents** |
| `name[19]` | `20` en déclaration, `19` si `version < EPIC_9_6` (`TS_SC_FARM_INFO.h:12-14`) | **19 octets** |
| `unknown` (`int64`) | ajouté si `version >= EPIC_9_8_1` (`TS_SC_FARM_INFO.h:11`) | **absent** |
| `unknown` (`int32`, après `index`) | **aucune trace** : l'en-tête rzu `87c1e83` ne porte que le `int64` de `:11`, et NGemity `38ceb2c` ne porte aucun `unknown` (ni `int32`, ni `int64`) | **absent** |
| `card_info` / `TS_ITEM_FIXED_INFO` | motif de 75 octets retenu par le dépôt (`118` en `>= EPIC_9_6_3`) | **75 octets**, motif déjà écrit par le dépôt |
| id de `6001` | `Since EPIC_7_3` puis `X(6001, true)` (`TS_SC_FARM_INFO.h:29-31`) | **en vigueur** |
| réponse 6008 | `250` si `version < EPIC_9_6_3` | **250** (pas 1250) |
| ligne de marché | `huntaholic_point` (`>= EPIC_5_2`) oui, `arena_point` (`>= EPIC_8_1`) non | **16 octets par ligne** |
| ouverture de la fenêtre | `TM_SC_DIALOG = 3000` (`GameMessage.h:291` ; `GamePackets.cs:347`) et `TYPE_CREATURE_FARM_WINDOW = 9` (`GameMessage.h:2287`) | **3000 / type 9** (§5.5) |

**Gates propres à la logique** (aucun n'est dans rzu : ils sont dans la source officielle et
divergent entre 2015 et 2012-11 — mesurés, cf. §6.2) : plafond de ferme (`FARM_MAX_LEVEL` = 150 en
2015, **100 en 2012-11**), nombre de formulaires supportés (3 en 2015, **2 en 2012-11**), prime
d'EXP premium (×5 / ×12.5 en 2015, **absente en 2012-11**), et les EXP/h (§5.3).

## 5. Traitement attendu

Sauf mention contraire, tout ce paragraphe vient de `official2015/GameServer/Game/Message/GameMessage.cpp:11681-11953`
(handlers et dispatch `:12306-12310`) et `Game/Struct/StructPlayer.cpp:11264-11562` (état et calculs).

### 5.1 Validation, dépôt, reprise, soin (source officielle)

- **6000** `onRequestFarmInfo` (`GameMessage.cpp:11726-11764`) : pour chaque case 0..2 occupée,
  `fillFarmedSummonTimeInfo` (`:11681-11709`) puis `fillFarmedSummonInfo` (`:11711-11724`) ; une
  entrée **expirée** (`registration_time + duration < now`, `:11747-11752`) est d'abord reprise
  (`RegainSummon`) et signalée au joueur par le message `@1158` (`:11752`), puis `index = i`
  (`:11754`) et `creature_count` (`:11760`). C'est **la seule lecture du 6001 : le serveur ne
  projette rien**, il montre l'état stocké.
- **6002** `FosterCreature` (`:11847-11897`), après `onFosterCreature` (`:11899-11908`) :
  la carte doit être dans l'inventaire et appartenir au joueur (`:11854-11858`), porter une
  invocation (`:11860-11864`) ; `requiredTicketCount = GameContent::GetCreatureFarmTicketCount(
  creature->GetRate(), creature->GetTransformLevel(), creature->GetEnhance())` (`:11866`) ;
  `ValidateFarmTicket` (`:11766-11800`) exige **la somme des `ticket_count` égale au compte requis**
  (`:11799`), des objets de classe `ItemBase::CLASS_FARM_PASS` (`:11776` — `ItemBase.h:194` = 403),
  dans l'inventaire et appartenant au joueur (`:11778`), et **homogènes** : tous les tickets du
  message doivent porter la même durée `fOptVar1[0]` et le même `isCash` (`fOptVar2[0] == 1`)
  (`:11787-11796`) ; `ValidateCracker` (`:11802-11828`) accepte `cracker_info_count == 0`
  (aucun cracker, `:11804-11807`) ou alors des objets de classe `CLASS_CREATURE_FOOD`
  (`:11815` — `ItemBase.h:193` = 402) dont la somme vaut **le même compte que les tickets**
  (`:11827`, paramètre `requiredFoodCount`). Le cracker ne change rien d'autre au dépôt.
  Puis la case libre est choisie (`FindSuitableFarmSlot`, `:11830-11845`), les tickets et crackers
  sont réellement retirés (`:11881-11891`) et le résultat `1` renvoyé.
- **6004** `onRetrieveCreature` (`:11910-11919`) → `StructPlayer::RegainSummon`
  (`StructPlayer.cpp:11332-11421`) : voir §5.3.
- **6006** `onNurseCreature` (`:11921-11941`) → `StructPlayer::NurseSummon`
  (`StructPlayer.cpp:11423-11467`), puis le Lua ci-dessus (§3.4).
- **6008** `onRequestFarmMarket` (`:11943-11952`) : marché `creature_farm`, `SendMarketInfo` avec
  handle 0 (réponse 250, §3.6).

### 5.2 Les cases de ferme — établi

`FARM_MAX_COUNT = 3`, `FARM_NON_CASH_MAX_COUNT = 1`, `FARM_MAX_LEVEL = 150`
(`GameRule.h:49-51`, ère Epic 9.x). `FindSuitableFarmSlot` (`:11830-11845`) réserve la **case 0 aux
tickets normaux** et les **cases 1 et 2 aux tickets premium** (`:11833-11834`). `index` (§3.2) est
donc le numéro de case 0..2, et la case voyage bien sur le fil — l'ancienne fiche le laissait
`NON ÉTABLI` (§7.2) : **résolu**. En 2012-11 les deux constantes sont identiques (3 et 1, mesurées
dans `StructPlayer::FarmSummon`, §6.2).

### 5.3 L'EXP de ferme — modèle établi

`RegainSummon` (`StructPlayer.cpp:11332-11421`) et `GetFarmedSummonLevel`
(`StructPlayer.cpp:11493-11562`) donnent, pour une entrée `f` et un temps `t` :

1. `tHour = (min(now, registration_time + duration) - registration_time) / 3600` — les heures
   **entières** écoulées depuis le dépôt, plafonnées par la durée du ticket (`:11367`). Ni
   `elapsed_time` (la colonne lue au `6001`, §3.2) ni `nursing_time` n'entrent dans ce calcul.
2. `dRate = 1.5` si `f.is_using_cracker` sinon `1.0` (`:11368`).
3. EXP gagnée : `nExp = tHour × EXP_par_heure × dRate`, l'EXP par heure dépendant du formulaire et du
   tarif (`:11373-11398` ; `EVOLVE_NORMAL` / `EVOLVE_GROWTH` / `EVOLVE_EVOLVE`, tarif premium si
   `f.is_cash`, tout autre formulaire = `return false`).
4. `nLevelLimit = min(f.max_level, FARM_MAX_LEVEL)` (`:11371`), puis `min` avec le plafond du
   formulaire (`NORMAL_SUMMON_MAX_LEVEL`, `GROWTH_SUMMON_MAX_LEVEL`, `EVOLVE_SUMMON_MAX_LEVEL`,
   `:11380` / `:11387` / `:11394`). `f.max_level` est figé **au dépôt** :
   `bCash ? FARM_MAX_LEVEL : player level au dépôt` (`FarmSummon`, `:11319` et `:11323`), et un dépôt
   avec ticket normal est refusé d'emblée si l'invocation a déjà un niveau supérieur ou égal à celui
   du joueur (`:11290`).
5. Application : `nExpLimit = GetNeedSummonExp(nLevelLimit) - 1` ; **seulement si** l'EXP de
   l'invocation est en dessous, `nExp = min(nExp, nExpLimit - exp)` puis `pSummon->AddExp(...)`
   (`:11402-11408`). La ferme amène donc l'invocation **au plus** au dernier niveau autorisé et ne
   touche **jamais** l'EXP du joueur.
6. L'EXP par heure de ferme : `nFarmNormalSummonEXP = 145763`, `nFarmGrowthSummonEXP = 1118029`,
   `nFarmEvolveSummonEXP = 3708799`, premiums `728814` / `13975356` / `37087982`
   (`GameRule.cpp:140-146`, ère Epic 9.x — **valeurs 7.3 différentes, §6.2**).

L'EXP n'est donc **appliquée qu'à la reprise** (`:11367-11408`) : le `6001` montre l'EXP courante de
l'invocation (§3.2) et il n'existe **aucun** calcul périodique dans la source. Le plafond « pas
au-delà du niveau du propriétaire » du texte retail 7.3 (`smsg_creaturefarm13`, §6.3) est réalisé
par le `max_level` figé au dépôt (`bCash ? FARM_MAX_LEVEL : player level au dépôt`, `:11319`) et par
le refus de dépôt de `:11290`, **pas** par un contrôle au moment de la reprise.

### 5.4 Le soin (6006) — une fois par jour, après 06:00

`NurseSummon` (`StructPlayer.cpp:11423-11467`) : refuse si la carte ne porte pas
`ITEM_FLAG_FARMED_SUMMON` (`:11429`) ou si aucune entrée de ferme ne lui correspond (`:11432-11464`) ;
construit le repère « aujourd'hui 06:00 heure locale serveur », recule d'un jour si l'heure courante
est avant 06:00, et **refuse si `nursing_time >= ce repère`** (`:11439-11456`) — **une seule fois par
jour, la journée commençant à 06:00 heure serveur** ; puis écrit `nursing_time = maintenant`
(`:11458-11459`). Le soin **n'exige pas** de cracker (la source ne teste jamais `is_using_cracker`
ici) et **ne pose aucun drapeau** : `ITEM_FLAG_NURSED_SUMMON` (bit 28, `ItemInstance.h:91`) est
déclaré mais `NurseSummon` ne le pose pas. Le `6001` en déduit `refresh_time` :
`refresh_time = (nursing_time < dernier 06:00) ? secondes jusqu'au prochain 06:00 : 0`
(`GameMessage.cpp:11686-11708`), et pour une entrée jamais soignée 0 aussi. La convention 06:00 est
donc **convention de remise à zéro**, pas une fenêtre.

### 5.5 La fenêtre (dialog 3000, type 9)

Le Lua `show_creature_farm_window()` est servi par `SCRIPT_ShowCreatureFarmWindow`
(`Script/ScriptPlayer.cpp`, enregistré comme `show_creature_farm_window` et
`get_farmed_summon_list/count/value/level` / `farm_summon`, `Script/ScriptPlayer.h:55-62`) :
`SetDialogTitle(..., TS_SC_DIALOG::TYPE_CREATURE_FARM_WINDOW)` puis `ShowDialog()`, ce dernier
envoyant `SendDialogMessage(this, npc, type, title, text, menu)`
(`StructPlayer.cpp:5521-5530`) — soit une trame **`TM_SC_DIALOG` = 3000 avec `type = 9`**
(`GameMessage.h:2287`). `TS_SC_SHOW_WINDOW` (3003, `GameMessage.h:294`/`:2333`) existe par ailleurs,
mais **la ferme n'y est pas accrochée dans la source officielle** : la fiche d'origine (§5.4) citait
3003 comme point d'entrée de la fenêtre, la source officielle le remplace par `3000/type 9`.

Côté dépôt, la brique manque : `NpcDialogService.cs` / `CreatureDialogService.cs` servent les types
`0`, `4` (enchères, `NpcScriptService.cs:45`) et `5` (lobby hunterholic,
`HuntaholicService.cs:1183`), jamais `9` ; le déclencheur `show_creature_farm_window` n'a pas de
branche.

### 5.6 Le sous-ensemble 7.3 minimal à porter (modèle)

Décision de Killian du 2026-10-06, dans cet ordre :

1. **Stockage** — une ligne de ferme par invocation déposée, avec exactement l'information mesurée
   côté officiel (`DB_Farm.cpp:16-29` et `DB_Login.cpp:1330-1360`) : un id de ligne, la **case**
   (0..2, 1-base en base officielle — `slot - 1` à la lecture, `DB_Login.cpp:1333-1334`), la carte
   (`item_id` = UID de l'objet), le propriétaire, `max_level`, `is_using_cracker`, `is_cash`,
   `registration_time`, `duration`, `nursing_time` (procédures `smp_read_farm_info` /
   `DB_InsertFarmInfo` / `DB_UpdateNursingTime` / `DB_DeleteFarmInfo`, `DB_Commands.h:2566-2632`,
   allocateur de sid `DB_ReadMaxItemIndex.cpp:231-247`). Le contexte du dépôt qui porte déjà les
   objets est Telecaster (`ItemEntity`), et `ItemStorageEntity` (`StorageType` + `ExpirationTime`)
   montre la forme de stockage déjà en place ; **la forme exacte retenue par le dépôt est à trancher
   par le dev** dans le respect des colonnes ci-dessus (§7, question ouverte 1).
2. **Drapeau** — `ITEM_FLAG_FARMED_SUMMON` = bit **27** (`ItemInstance.h:90`), posé au dépôt et levé
   à la reprise ; `ITEM_FLAG_NURSED_SUMMON` = bit **28** (`ItemInstance.h:91`) au soin. Les deux
   existent déjà côté dépôt (`ItemFlag.cs`, valeurs `27`/`28`) mais ne sont pas utilisés.
3. **`6001` rempli** depuis le stockage (jamais de projection).
4. **Fenêtre** — `3000` type `9` sur le déclencheur Lua.
5. **`6008`** — réponse `TM_SC_MARKET` `250` avec les lignes du marché `creature_farm` déjà
   présentes dans l'export du dépôt.
6. **Constantes** de `GameRule.h:49-51` — **avec les valeurs 7.3 mesurées, pas celles de 2015**
   (§6.2) — et les EXP/h 7.3.

Les points 1 à 3 forment le socle sans lequel le `6001` resterait vide ; 4 et 5 rendent la fenêtre
utilisable ; 6 évite de porter des constantes Epic 9.x dans un serveur 7.3.

**Hors périmètre de ce lot** — les trois gestes qui écrivent en base et répondent une trame de
résultat gardent leurs cartes : `TM_CS_FOSTER_CREATURE` 6002 (réponse 6003, format §3.3 et §3.4),
`TM_CS_RETRIEVE_CREATURE` 6004 (réponse 6005, 11 → 8 octets, §3.5/§3.4) et `TM_CS_NURSE_CREATURE`
6006 (réponse 6007, idem). Leurs prérequis sont ceux du socle : table de ferme (§5.6 point 1),
drapeau bit 27 (point 2), table de coûts en tickets (`db_creaturefarm.rdb` / `CreatureFarmResource`,
§6.3, non chargée par le dépôt) et courbe d'EXP d'invocation (`ICreatureCatalog.NeedExp`, §7.8).
Aucune trame de résultat ne doit être émise avant que ces prérequis existent, et l'id 6003/6005/6007
doit être déclaré avec son `case` dans le même commit.

## 6. Écarts assumés

### 6.1 Avec NGemity

NGemity (`ngemity`, `38ceb2c`) **n'a aucune logique de ferme** : les trois en-têtes de trame
(`shared/Server/Packets/GameClient/TS_CS_REQUEST_FARM_INFO.h`, `TS_SC_FARM_INFO.h`,
`TS_CS_REQUEST_FARM_MARKET.h`) et les tables de dispatch, rien dans `Chihiro/src` (0 occurrence de
`Farm`). Il n'y a donc **rien à porter de NGemity** et aucune divergence d'implémentation : sur
la ferme, NGemity est un simple relecteur de structures, et la référence de logique est la source
officielle. rzu est également muet au-delà des structures. C'est l'écart principal avec les autres
paquets du dépôt : ici, NGemity ne tranche pas la logique.

### 6.2 La source 2015 est Epic 9.x — écarts mesurés contre l'ère 7.3

L'extrait `official2015` est un arbre **2015 / Epic 9.x** (`official2015/README.md:3-4`) ; la ferme
existe déjà en 2012-11 (`epic7part4/server/2012-11/CaptainHerlockServer.symbols.tsv` contient
`FarmSummon`, `RegainSummon`, `NurseSummon`, `GetFarmedSummonLevel`, `onRequestFarmInfo`,
`onFosterCreature`, `onNurseCreature`, `onRequestFarmMarket`, `fillFarmedSummonInfo`,
`ValidateFarmTicket`, `ValidateCracker`, `CreatureFarmLoader`, `DB_InsertFarmInfo`,
`GetCreatureFarmTicketCount`, `SendMarketInfo`), **mais pas à l'identique**. Mesures faites sur
`epic7part4/server/2012-11/CaptainHerlockServer.exe` (adresses lues dans le `.tsv`, octets lus aux
offsets PE calculés) :

| point | 2015 (source) | 2012-11 (mesuré) | conséquence pour 7.3 |
|---|---|---|---|
| EXP/h normal | `145763` (`GameRule.cpp:140`) | `137700` (`0x1404f7e68`) | **prendre la valeur 7.3** |
| EXP/h croissance | `1118029` (`:141`) | `347264` (`0x1404f7e6c`) | idem |
| EXP/h évolution | `3708799` (`:142`) | **pas de constante ni de branche** (le `.tsv` ne porte que les deux globales `nFarmNormalSummonEXP`/`nFarmGrowthSummonEXP`, et `RegainSummon` `1400d5e60` ne lit que celles-là) | la ferme 7.3 ne sert que 2 formulaires |
| EXP/h premium | `728814` / `13975356` / `37087982` (`:144-146`) | **aucune branche premium** dans `RegainSummon` ni aucune globale `PremiumFarm` dans le `.tsv` (0 occurrence) | pas de prime d'EXP premium en 7.3 |
| plafond de ferme | `FARM_MAX_LEVEL = 150` (`GameRule.h:49`) | `100` (`0x1404096c0`) | **100** |
| cases | `3` / `1` (`GameRule.h:50-51`) | `3` / `1` (`cmp $0x3` `1400d5938`, `cmp $0x1` `1400d5965`) | identiques |
| plafond formulaire 1 | `60` | `60` (`0x1404096b8`) | identiques |
| plafond formulaire 2 | `115` | `115` (`0x1404096bc`) | identiques |
| cracker | ×1.5 (`StructPlayer.cpp:11365-11371`) | `1.5` (`0x14040a2c8`) vs `1.0` (`0x140400730`), deux **doubles** chargés par `movsd` (`1400d6016`, `1400d6020`) | identiques |
| drapeau de ferme | bit 27 (`ItemInstance.h:90`) | `orl $0x8000000` sur l'objet (`1400d5a1e`) | identique |
| soin quotidien | dernier 06:00 (`StructPlayer.cpp:11439-11456`) | heure `6` dans `NurseSummon` (`1400d6446`) | identique |

Autrement dit : **la logique se porte, les constantes d'EXP et le plafond non**. Les valeurs 7.3
d'EXP/h croissance (`347264`) et normal (`137700`) remplacent celles de 2015 ; les constantes
premium et évolution de 2015 sont des ajouts postérieurs à 7.3 et ne doivent pas être portées.
Le reste des algorithmes (heures entières, `dRate`, plafonds, jours à 06:00, cases) est commun. Les
plafonds sont bien appliqués en 2012-11 : `FarmSummon` (`1400d57d0`) compare `cmp $0x64` (100),
`cmp $0x3c` (60) et `cmp $0x73` (115) en `1400d592f` / `1400d5977` / `1400d5994`, et le drapeau y est
posé par `orl $0x8000000` (`1400d5a1e`).

### 6.3 Avec les données 7.3 déjà présentes dans le dépôt

- **Table des coûts en tickets** : `db_creaturefarm.rdb` (client 7.3, sha256
  `36564e4a…dea3d`) est la même table que `CreatureFarmResource` (4 colonnes `tinyint` :
  `rate`, `form`, `enhance_level`, `ticket_count`, `CreatureFarmLoader.cpp:10-23`, lue par
  `GetCreatureFarmTicketCount(rate, form, enhance_level)`, `GameContent.h:573` ; `GetNeedSummonExp` est déclarée deux lignes plus bas, `:575`) ; le dépôt la
  connaît dans `ArcadiaSchemaPSQL.sql:46` mais **ne la charge pas** (aucune entité EF). Le décodage
  de la table client figure déjà dans la fiche d'origine §8.2 et n'est pas refait ici.
- **Textes 7.3** : `reference/sqlserver/Arcadia/StringResource_DE.csv:1038-1057` porte
  `smsg_creaturefarm01..20` marqués `version:7.3` et confirme les règles : reprise automatique à
  l'expiration, disparition après 30 jours sans reprise, pas de croissance au-delà du niveau du
  propriétaire, niveau max **60** (formulaire 1) et **100** (formulaire 2), soin quotidien, cadeau
  possible, contrats de 3 et 7 jours, tickets vendus à la guilde des marchands, coût en tickets
  montant avec le niveau et la puissance. Ces textes 7.3 corroborent le plafond de ferme mesuré
  (100) et **contredisent** les constantes 2015 (150) : la valeur 7.3 est bien 100.
- **Identités d'objets** : le Lua officiel porte en commentaire la table
  `ticket_code_table[duration][cash]` (`NPC_Creature_farm.lua:41-45`) : durée `259200` → `710005`
  (cash `710007`), durée `604800` → `710006` (cash `710008`) — **la durée est en secondes** (3 et 7
  jours), ce que confirment les noms d'icônes client
  (`icon_commerce_farmticket_{junior,senior}[_premium]`, §2) et le marché `creature_farm`
  (`710005`/`710006`, §3.6). Le cadeau de soin est `710009`
  (`NPC_Creature_farm.lua:55`). Les crackers sont des objets `CLASS_CREATURE_FOOD` (402) et les
  tickets des `CLASS_FARM_PASS` (403) (`ItemBase.h:193-194`) — le dépôt connaît déjà ces deux
  classes (`ItemType.cs:44-45`, `CreatureFood = 402`, `FarmPass = 403`).

### 6.4 Ce que le drapeau ne remplace pas

Le drapeau `ITEM_FLAG_FARMED_SUMMON` (bit 27) est **posé sur la ligne d'objet** (la carte) et suffit
à marquer la carte comme « à la ferme », mais il **ne porte aucune des données nécessaires** : ni la
case, ni `duration`, ni `registration_time`, ni `nursing_time`, ni `max_level`, ni `is_using_cracker` /
`is_cash` — toutes ces valeurs viennent de `FARMED_SUMMON_INFO`, c'est-à-dire de la ligne de la table
`Farm` relue à l'entrée en jeu (`DB_Login.cpp:1309-1410`, qui retrouve la carte par
`ITEM_FLAG_FARMED_SUMMON` puis lui réassocie son entrée, `DB_Login.cpp:1565-1568`). Le portage doit
donc livrer **les deux** : le drapeau *et* la ligne de ferme. Le drapeau seul ne permet ni de
reconstruire un `6001` ni de calculer quoi que ce soit.

## 7. `NON ÉTABLI`

Questions ouvertes, à trancher par Killian ou par le dev avant/pendant le lot :

1. **Forme du stockage côté dépôt** : la source officielle utilise une procédure et une table
   dédiées ; le dépôt a deux candidats (table dédiée à l'officielle, ou `ItemStorageEntity` avec
   `StorageType`). Les colonnes et la sémantique sont établies (§5.6) ; **le choix de la forme
   appartient au dev**. À vérifier au passage : `ItemEntity.StorageId` est `int?` et
   `ItemStorageEntity.CharacterId` est en clair — la carte confiée reste-t-elle liée au personnage
   (comme dans la source, où `owner_id = SID du joueur`) ?
2. **La source officielle 2012-11 ne dit pas tout** : les 4 constantes 7.3 restantes (EXP premium,
   EXP évolution) n'ont **pas** été localisées dans le binaire — la table est proposée comme
   « absente en 7.3 » (§6.2) par l'absence de branche dans `RegainSummon`, ce qui est un indice fort
   mais pas une preuve que le tarif premium 7.3 n'existait pas. Les textes 7.3 (`smsg_creaturefarm…`)
   mentionnent les tickets premium sans donner de taux d'EXP. À confirmer avant de figer le tarif.
3. **Le NPC de la ferme** : la source donne son nom textuel (`@91000349`, `@91000350`) mais pas son
   `npc_id` numérique ; la table client 7.3 n'a pas été relue pour l'identifier. Tant qu'il n'est pas
   identifié, la fenêtre ne peut être ouverte ni par dialogue ni par le trigger.
4. **Version du plafond de ferme** : 100 (mesuré 2012-11 + texte 7.3) vs 150 (source 2015). Tranché
   pour **100** ici (§6.2) ; à confirmer si un jour un arbre 7.3 côté serveur devient disponible.
5. **`SendMarketInfo` en 2012-11** : la fonction existe (symbole
   `0x14014c730 SendMarketInfo(StructPlayer*, unsigned int, _MARKET_INFO*)`) mais son corps n'a pas
   été relu ; le handle `0` est celui que la source 2015 passe, pas une mesure 7.3.
6. **Réaction du client 7.3 au `250` avec `npc_handle = 0`** : non mesurée. Le `6008` est lu par le
   dépôt (garde-fou) et le catalogue `creature_farm` existe, mais rien ne prouve ici que la fenêtre
   de magasin 7.3 accepte un handle nul — à vérifier sur le client avant de considérer `6008` fermé.
7. **Les accesseurs Lua `get_farmed_summon_list/count/value/level`** : présents dans la source
   (`ScriptPlayer.h:55-62`) mais **appelés par aucun script de l'extrait** (`NPC_Creature_farm.lua`
   ne les utilise pas) ; le script qui les consomme n'est pas dans l'extrait. Le tableau de ferme
   affiché par le client reste, lui, alimenté par `6001`, donc ce n'est pas bloquant.
8. **`GameContent::GetNeedSummonExp` a un équivalent dans le dépôt** (correction du 2026-10-06) :
   `ICreatureCatalog.NeedExp(int)` (`Game/Services/Creatures/CreatureCatalog.cs:38-42`, `:80`), chargé
   depuis `CreatureCatalogOptions.SummonExp` (`_summonExp`, `:74`) et documenté comme la courbe
   cumulée (colonne `normal_exp`, lue pour chaque formulaire, 0 au-delà de la table). Côté officiel
   elle est déclarée en `GameContent.h:575`, juste après `GetCreatureFarmTicketCount` (`:573`). **Le
   calcul d'EXP de §5.3 est donc applicable tel quel.** Ce qui reste à trancher n'est plus
   l'existence de la courbe mais **sa source de vérité** (`CreatureCatalogOptions.SummonExp` de la
   configuration du dépôt, `client73/db_exp.rdb` ou `SummonResource.normal_exp`) et l'indexation
   (le dépôt rend 0 hors table et indexe au niveau 1 ; à confirmer contre la table `ExpResource`).
9. **Remplissage du `name[19]`** — **levé** : `s_strcpy( name, _countof(name), … )` puis
   `name[_countof(name) - 1] = 0` (`GameMessage.cpp:11716-11717`), tampon mis à zéro avant remplissage
   (`:11729`, détail en §3.2). Ce qui reste ouvert n'est plus la trame mais la **source du nom** :
   la source officielle lit `GetSummonStruct()->GetName()`, alors que le dépôt nomme les invocations
   lui-même (`RandomName`, `CreatureCatalog.cs:98-106`) — envoyer le nom de la ressource d'invocation
   brute ou celui porté par l'objet/invocation du joueur est un choix de rédaction laissé au dev.

## A VERIFIER PAR KILLIAN

Ce que la référence locale ne tranche pas et qui demande son arbitrage. Rien de ce qui suit ne bloque
le socle décrit en §5.6 points 1 à 3 : le dev peut livrer le stockage, le drapeau et un `6001` rempli
sans réponse à ces questions.

1. **Portée des tickets premium** — la source officielle réserve la **case 0 aux tickets normaux** et
   les **cases 1 et 2 aux tickets premium** (`FARM_MAX_COUNT = 3`, `FARM_NON_CASH_MAX_COUNT = 1`,
   §5.2, `GameMessage.cpp:11833-11834`), et les objets premium sont `710007`/`710008` (3 et 7 jours,
   `NPC_Creature_farm.lua:41-45`, table `ticket_code_table[duration][cash]`). Ce qui reste à trancher
   n'est pas la case mais **l'existence commerciale et le tarif** : les identités `2012232`/`2012233`
   portées par la carte d'origine n'ont **pas** été localisées dans les données 7.3 relues
   (`reference/sqlserver/Arcadia/MarketResource.csv`, `epic7part4/csv/MarketResource.csv`,
   `DevConsole/market-catalog.73.json` n'ont que `710005`/`710006`), et les quatre constantes d'EXP
   premium/évolution ne sont pas dans le binaire 2012-11 (§6.2, §7.2). **Question : le serveur 7.3
   doit-il ouvrir les cases premium, et avec quel tarif d'EXP, alors que la référence 2012-11 les
   ignore ?**
2. **Plafond de ferme** : 100 (mesuré 2012-11 `0x1404096c0` + textes `smsg_creaturefarm*` 7.3) contre
   150 (source 2015). Tranché pour **100** (§6.2, §7.4) ; confirmer si un arbre serveur 7.3
   apparaît un jour.
3. **PNJ de la ferme** : identité textuelle établie (`@91000349`/`@91000350`,
   `NPC_Creature_farm.lua:14-17`, `:61-71`) mais **`npc_id` numérique non identifié** — sans lui, ni le
   dialogue ni le déclencheur `show_creature_farm_window` ne peuvent être accrochés (§7.3).
4. **Forme du stockage** : à trancher par le dev (table dédiée calquée sur l'officielle ou
   `ItemStorageEntity`), les colonnes et la sémantique étant établies (§5.6 point 1, §7.1). Le point
   à surveiller est le rattachement de la carte au personnage (`owner_id` côté officiel ; `int?`
   `StorageId` côté dépôt).
5. **`6008` fermé seulement après essai client** : `TM_SC_MARKET` `250` avec `npc_handle = 0` est ce
   que la source 2015 envoie (`GameMessage.cpp:11943-11952`) ; la réaction du client 7.3 à un handle
   nul n'est **pas** mesurée (§7.6). À vérifier sur le client avant de considérer la trame close.
6. **Source de vérité de la courbe d'EXP d'invocation** : `CreatureCatalogOptions.SummonExp`,
   `client73/db_exp.rdb` ou `SummonResource.normal_exp` (§7.8). Le calcul de §5.3 ne peut être
   implémenté qu'avec la réponse.

## Bloc pour CLAUDE.md

Bloc à insérer dans `CLAUDE.md` par la MR du lot (le dev ne modifie pas `CLAUDE.md` lui-même, le
fichier est protégé) :

```markdown
### Ferme de créatures (6000-6008)

- La logique de référence est la source officielle locale `reference/official2015` (2015, ère Epic 9.x),
  confrontée à l'ère 7.3 : `reference/epic7part4` (serveur 2012-11 + `.symbols.tsv`), client
  `reference/client73/SFrame.exe`, données `reference/sqlserver/Arcadia`.
- **NGemity ne tranche rien ici** : aucune logique de ferme dans `Chihiro/src` — seulement trois
  en-têtes. rzu ne donne que les structures.
- Constantes : prendre les **valeurs 7.3 mesurées**, pas celles de 2015 — EXP/h normal `137700`,
  croissance `347264`, `FARM_MAX_LEVEL = 100` (la source 2015 dit 150), cracker ×1.5 (`dRate`).
  Pas d'EXP premium ni d'EXP évolution en 7.3 (absentes du binaire 2012-11).
- `6001` = `8 + 120 × N` octets, `N` = cases occupées (0..3) ; la case voyage dans `index` ; `exp` est
  l'EXP déjà acquise par l'invocation ; l'EXP n'est **appliquée qu'à la reprise** (6004), jamais
  périodiquement ; soin une fois par jour, journée à partir de 06:00 serveur.
- Les trames de résultat **6003/6005/6007 ne sont pas encore déclarées** dans `GamePackets` ; les
  lots qui les émettent les déclarent avec leur `case` (critère 4 d'acceptation).
- La réponse à `6008` n'est pas une trame de ferme : c'est `TM_SC_MARKET` `250` avec le catalogue
  `creature_farm` — compact, **sans le `pad` de 4 × n que déclare rzu**.
- Savoir complet et questions ouvertes : `docs/packet-specs/socle-ferme-creatures-officielle.md`
  (la décision du 2026-09-30 « paquets seuls, pas de système » est levée).
```

## 8. Commits et fichiers épinglés

- **Navislamia (base du lot)** : `8b3061aab5d31763127777484288b331317ec4cf` (`master`,
  2026-10-06, « fix(props): event props are no longer placed outside their event »).
- **rzu / librzu** : `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02).
- **NGemity/Chihiro** : `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03).
- **Source officielle 2015** (pas un dépôt git : épinglée par sha256 des fichiers utilisés) :
  - `Game/Message/GameMessage.cpp` `e7f5399a…be5ac2`, `GameMessage.h` `183e1315…85e484`
  - `Game/Struct/StructPlayer.cpp` `40c1fbcf…c76046`, `StructPlayer.h` `b3e7d115…55a78b`
  - `Game/Rule/GameRule.cpp` `90e2600b…4cc7a5`, `GameRule.h` `eb293df8…e086426`
  - `Game/Resource/CreatureFarmLoader.cpp` `fd6f5ac2…003f7b`, `ItemInstance.h` `516fabbe…535c64`
  - `Game/Db/DB_Farm.cpp` `1a428983…189db1`, `DB_Login.cpp` `21043b6f…000d1f`,
    `DB_Commands.h` `ae433e9a…16890f`
  - `Game/Script/ScriptPlayer.cpp` `53865b29…63ad8e`, `Resource/Script/NPC_Creature_farm.lua`
    `4c713c92…537543`, `README.md` `1ece34cd…1c1403`
- **Serveur 7.3-era** : `epic7part4/server/2012-11/CaptainHerlockServer.exe`
  `83b54fe1…d9dd3eac` (+ son `.symbols.tsv`) ; `epic7part4/csv/MarketResource.csv` `5992cd8a…cf9ac4`.
- **Données 7.3** : `client73/SFrame.exe` `41e0af2e…fb9500e` ; `client73/db_creaturefarm.rdb`
  `36564e4a…dea3d` ; `reference/sqlserver/Arcadia/StringResource_DE.csv` `df9e1454…d31670e`.

### Cette fiche lève la décision du 2026-09-30

La décision « paquets pris en charge, système non implémenté » (`socle-ferme-creatures.md`, second `## 8`,
ligne 508) est
**caduque** : la référence officielle de logique est désormais disponible localement
(`reference/official2015`, épinglée ci-dessus), elle a été confrontée à l'ère 7.3
(`reference/epic7part4`, client 7.3, données `reference/sqlserver/Arcadia`), et le sous-ensemble minimal est
défini (§5.6). Le lot de code correspondant appartient au **dev** ; cette fiche ne contient que le
savoir ; le texte de la fiche d'origine est intact et reste en place comme trace de la décision
retirée, avec seulement l'avertissement d'obsolescence daté ajouté en tête de son second `## 8`.
