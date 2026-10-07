# TM_CS_FOSTER_CREATURE (6002) — le dépôt d'une invocation à la ferme de créatures

Fiche du paquet traité par le lot `navis-dev` « Implémentation — `TM_CS_FOSTER_CREATURE` (6002) :
dépôt d'une invocation à la ferme et réponse 6003 ». Elle **épingle** ce que
`docs/packet-specs/socle-ferme-creatures.md` (les neuf trames de la famille) et
`docs/packet-specs/socle-ferme-creatures-officielle.md` (le système et son sous-ensemble 7.3) ont déjà
établi, et elle **tranche ce que ces deux fiches laissaient ouvert** : la correspondance octet → colonne
de la table de coût (§4.3, §5.5), l'identité des objets ticket et cracker (§5.4), les valeurs de `result`
de la réponse (§5.6), la garde du tableau de tickets vide (§6.4).

Références lues pour cette fiche : `op_codes.md` ; rzu (`reference/rzu/librzu/src/packets/`) ; NGemity
(`reference/ngemity/shared/Server/Packets/`, `Chihiro/src`, `Database/Arcadia.sql`) ; la source
officielle (`reference/official2015/GameServer`) ; le serveur de l'ère 7.3 **avec symboles**
(`reference/epic7part4/server/2012-11/CaptainHerlockServer.exe` + `CaptainHerlockServer.symbols.tsv`) ;
le client (`reference/client73/SFrame.exe`) ; les données `reference/client73/db_creaturefarm.rdb`.

**Aucun code du serveur n'est écrit ni modifié par cette fiche. Aucun binaire, aucun Lua et aucun
script du client n'a été exécuté** : désassemblage `objdump -d` et lecture de fichiers uniquement.

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id | **6002** (`0x1773`) | `op_codes.md:258` — `[6002] = "TM_CS_FOSTER_CREATURE"` |
| Nom Navislamia | `TM_CS_FOSTER_CREATURE` | `op_codes.md:258` ; `Game/Network/Packets/Enums/GamePackets.cs:449` (base `f4f3f10`) |
| Nom rzu / NGemity | `TS_CS_FOSTER_CREATURE` | rzu `TS_CS_FOSTER_CREATURE.h:32` ; NGemity `shared/Server/Packets/GameClient/TS_CS_FOSTER_CREATURE.h:26` |
| Sens | **client → serveur** | rzu `TS_CS_FOSTER_CREATURE.h:32` (`SessionPacketOrigin::Client`) ; NGemity `shared/Server/ClientPackets.h:281` ; le client en a un constructeur de trame (`0x610910`), le serveur officiel un handler (`GameMessage.cpp:11899`, dispatch `:12306-12310`) |
| En-tête | 7 octets (`length` u32, `id` u16, `checksum` u8) | `socle-ferme-creatures.md` §3.0 ; `Game/Network/Packets/Header.cs:9-11` ; le client pose lui-même cet en-tête (`0x6104d8-0x61051d`, somme `0..5` en `0x610516`) |
| **Taille totale** | **`19 + 8 × T + 8 × C` octets** ; un vrai client 7.3 envoie **27** (T = 1, C = 0) ou **35** (T = 1, C = 1) | §3.2, §3.4 |
| État dans Navislamia (`master`) | **lecteur présent, handler présent, aucune réponse** | `GameFarmPackets.cs:75-98,155-156,208` ; `GameClient.cs:1109-1127` ; dispatch `GameClient.cs:3719-3723` |

C'est le paquet « **confier** » de la ferme de créatures : le client y nomme la carte d'invocation qu'il
laisse à la ferme, la pile de tickets qui la paie et, si le joueur en a mis un, la pile de crackers qui
accélérera sa croissance. La réponse est `TM_SC_RESULT_FOSTER` (6003), **un octet de résultat** (§3.3).

---

## 2. Ce que le joueur fait pour que le client l'envoie

1. Il ouvre la fenêtre de ferme (`window_creature_farm.nui`, chaîne présente dans `SFrame.exe`) : le
   client envoie `6000` et reçoit `6001`, qui liste ses invocations déposées
   (`socle-ferme-creatures.md` §2.1, §3.2).
2. Il choisit un emplacement avec sa carte, la pile de tickets qui paie et, facultativement, la pile de
   crackers : la fenêtre garde ces poignées dans son objet — **carte** en `this+0x54c` (argument du
   constructeur, `0x61493f`), **ticket** en `this+0x544` (lu en `0x61095f`), **cracker** en `this+0x548`
   (lu en `0x610984`).
3. Il presse le widget **`"assign"`** (chaîne `.rdata 0xa40050`) : le site d'appel `0x61493f` construit
   et envoie la trame (`socle-ferme-creatures.md` §2.1, repris sur attribution). Le constructeur
   `0x610910` est le seul de la famille dont l'argument est la carte ; les messages locaux de la fenêtre
   sont `msgbox_creatureFarmAssign` et `msgbox_creatureFarmDoNotAssign1..4` (chaînes présentes dans
   `SFrame.exe`).

Mesuré dans le constructeur `0x610910` (désassemblage local de `SFrame.exe`) :

| observation | instruction | adresse |
|---|---|---|
| carte écrite à l'offset 7 de la trame | `mov %edi,-0x3f9(%ebp)` (`edi` = argument) | `0x610933` |
| `ticket_info` (nombre) vaut **toujours 1** | `movl $0x1,-0x3f5(%ebp)` | `0x61096a` |
| `cracker_info` (nombre) vaut **0 ou 1** | `sete %cl` puis `mov %ecx,-0x3f1(%ebp)` | `0x610965`, `0x61097a` |
| `length` finale = `27` ou `35` | `mov $0x8,%eax` puis `add $0x1b,%eax` | `0x610996`, `0x61099b` |
| poignée de ticket écrite à l'offset 19 | `mov 0x544(%esi),%edi` puis `mov %edi,-0x3ed(%ebp)` | `0x61095f`, `0x610974` |

Deux comptes **ne sont pas calculés** dans le constructeur : les valeurs envoyées (`+23` `ticket_count`,
`+31` `cracker_count`) sont recopiées de quatre variables de la fenêtre (`0xc4c2c0` → `ticket_count`,
`0xc4c2c4` → `cracker_count`), et l'envoi est refusé (retour 0, `0x6109e2`) si `0xc4c2c0 != 0xc4c2c8` ou
`0xc4c2c4 != 0xc4c2cc` (`0x61093f-0x610953`). Le client charge par ailleurs le fichier
**`db_CreatureFarm.rdb`** (chaîne présente dans `SFrame.exe`, dump `strings`) : le nombre de tickets dû
est donc calculable côté client, mais **le site qui remplit ces quatre variables n'est pas établi** (§7.2).

---

## 3. Structure sur le fil

### 3.1 En-tête — 7 octets

| offset | type | nom | valeur / source |
|---|---|---|---|
| 0 | `uint32` | `length` | total de la trame, en-tête compris. `socle-ferme-creatures.md` §3.0 ; `Header.cs:9,22` |
| 4 | `uint16` | `id` | `6002` = `0x1773`. `socle-ferme-creatures.md` §3.0 ; `Header.cs:10,23` |
| 6 | `uint8` | `checksum` | somme octet par octet de `0..5`. `PacketExtensions.cs:13-25` ; le client fait le même calcul (`0x610516`) |

### 3.2 Champs

| offset | type | nom | valeur observée / source |
|---|---|---|---|
| 7 | `uint32` (`ar_handle_t`) | `creature_card_handle` | la carte à déposer. rzu `TS_CS_FOSTER_CREATURE.h:22` ; NGemity `:19` (`uint32_t`) ; client `0x610933` ; serveur 7.3 : `mov 0x7(%rsi),%r8d` en `0x14011e50d` |
| 11 | `int32` | `ticket_info` (nombre d'entrées) | rzu `:23` ; NGemity `:20` ; client `0x61096a` (constante **1**) ; serveur 7.3 : `mov 0xb(%rsi),%edx` en `0x14011e492` |
| 15 | `int32` | `cracker_info` (nombre d'entrées) | rzu `:24` ; NGemity `:21` ; client `0x610965` (**0 ou 1**) ; serveur 7.3 : `mov 0xf(%rsi),%edx` en `0x14011e4ab` |
| 19 | `uint32` | `ticket_info[0].ticket_handle` | rzu `TS_CS_FOSTER_CREATURE.h:8` (struct `TS_TICKET_INFO`) ; client `0x610974` |
| 23 | `int32` | `ticket_info[0].ticket_count` | rzu `:9` ; client `0x610959` |
| 27 | `uint32` | `cracker_info[0].cracker_handle` (si `C ≥ 1`) | rzu `:15` ; client `0x61098a` |
| 31 | `int32` | `cracker_info[0].cracker_count` (si `C ≥ 1`) | rzu `:16` ; client `0x610990` |

Les deux tableaux (`_(dynarray)(TS_TICKET_INFO, …)` puis `_(dynarray)(TS_CRACKER_INFO, …)`, rzu `:25-26`)
sont sérialisés **à la suite l'un de l'autre**, chacun fait de `T` (resp. `C`) entrées de **8 octets**
`{handle uint32, count int32}`. La source officielle le confirme en la lisant ainsi :
`reinterpret_cast<TICKET_INFO*>(pMsg + 1)` puis `crackerInfo = ticketInfo + pMsg->ticket_info_count`
(`GameMessage.cpp:11849-11852`), c'est-à-dire les tableaux **après la structure entière** (7 + 4 + 4 + 4
= 19), et non après le seul en-tête.

### 3.3 Réponse `TM_SC_RESULT_FOSTER` (6003) — 8 octets

| offset | type | nom | valeur observée / source |
|---|---|---|---|
| 7 | `int8` | `result` | **1 = l'invocation est confiée, 0 = refus** (§5.6). rzu `TS_SC_RESULT_FOSTER.h:8` ; NGemity `:7` |

**Taille totale attendue : 8 octets.** Le client ne lit **qu'un octet en +7** et rien d'autre
(`mov 0x7(%ecx),%dl` en `0x67225e`), le recopie tel quel dans l'événement interne d'id `0xae` qu'il remet
à son interface (`0x672220-0x672272`, sans aucun test de la valeur). Le serveur 7.3-era bâtit exactement
cette trame : `movl $0x8,0x88(%rsp)` (longueur), `movw $0x1773,0x8c(%rsp)` (id en +4),
`setne 0x8f(%rsp)` (résultat en +7), puis l'envoi en `0x1401496e0` — relevé dans `onFosterCreature`
`0x14011e681-0x14011e6b6`.

### 3.4 Taille totale attendue

**`19 + 8 × T + 8 × C` octets**, soit **19 octets** pour la trame minimale (`T = C = 0`), **27** pour ce
que le client envoie quand le joueur ne met pas de cracker (`T = 1`, `C = 0`) et **35** quand il en met
un (`T = 1`, `C = 1`). L'arithmétique est la même que celle du constructeur client, qui part de 19
(`call 0x6104d0` fixe `length = 0x13` en `0x61050a`) et ajoute 8 octets par cracker (`0x610996`) puis
`0x1b` = 27 (`0x61099b`). Les deux seules tailles qu'un vrai client 7.3 puisse produire sont donc 27 et
35 : ce sont les deux que le serveur doit accepter en priorité, sans refuser pour autant les tailles
plus grandes (une trame forgée peut porter plusieurs piles, §5.2).

Dans le dépôt, les offsets sont déjà posés : `GameFarmPackets.cs:75-91`
(`FosterCardHandleOffset = 7`, `FosterTicketCountOffset = 11`, `FosterCrackerCountOffset = 15`,
`FosterArraysOffset = 19`, `FosterStackEntrySize = 8`), la taille par `GetFosterSize`
(`:155-156`) et la lecture bornée par `TryReadFosterCreature` (`:208`).

---

## 4. Gating de version — tranché pour Epic 7.3

| élément | gating rzu | décision 7.3 | source |
|---|---|---|---|
| id de la demande | `X(6002, true)` sous `// Since EPIC_7_3` | **6002**, aucune variante d'id | rzu `TS_CS_FOSTER_CREATURE.h:28-30` ; NGemity `:25-26` (`// Since EPIC_7_3`, `CREATE_PACKET(…, 6002)`) |
| id de la réponse | `X(6003, true)` sous `// Since EPIC_7_3` | **6003**, aucune variante | rzu `TS_SC_RESULT_FOSTER.h:10-12` ; NGemity `TS_SC_RESULT_FOSTER.h:9-10` |
| `ar_handle_t` | `strong_typedef<ar_handle_t, uint32_t>` — **aucun** `version >=` | **4 octets** | rzu `GameTypes.h:40` |
| `ticket_info` / `cracker_info` (nombres) | `_(count)(int32_t, …)` | **4 octets** chacun, avant les tableaux | rzu `:23-24` ; NGemity `:20-21` |
| entrées des deux tableaux | `_(simple)(ar_handle_t, handle)` + `_(simple)(int32_t, count)` | **8 octets** chacun | rzu `:8-9, :15-16` |
| `result` de la réponse | `_(simple)(int8_t, result)` | **1 octet** | rzu `TS_SC_RESULT_FOSTER.h:8` |

L'en-tête rzu de ce paquet a été lu **en entier** : il ne porte **aucune** condition `version >= EPIC_*`
et **aucun** `_(def)` (donc aucun champ émis par erreur). Le seul gating de `CREATE_PACKET_VER_ID` porte
sur l'id, et sa condition est `true` : **rien à trancher, rien à retirer** pour 7.3.

Les deux pièges documentés de `CLAUDE.md` ne touchent pas cette famille : elle n'a ni champ `limit_*`
(ceux du groupe de sorts) ni branche de rareté à `break` manquant (`SRT_ADD_HP`). Le seul piège de la
famille est la **garde des tableaux vides**, qui vient de la logique et non du format (§6.4).

---

## 5. Traitement attendu

### 5.1 Ce que NGemity fait de ce paquet : rien

NGemity déclare le paquet et ne le traite pas : `shared/Server/ClientPackets.h:281-282` énumère
`TS_CS_FOSTER_CREATURE = 6002` et `TS_SC_RESULT_FOSTER = 6003`, `shared/Server/XPacket.h:97,284` inclut
les deux en-têtes, et **`grep -rni farm Chihiro/src` rend 0** : aucun `case`, aucun handler, aucune
construction de `TS_SC_RESULT_FOSTER`. NGemity ne donne donc **ni la réponse, ni ses valeurs, ni la
validation** ; il sert seulement de seconde source de layout (§4), et il ne dit rien du coût en tickets
(sa base `Database/Arcadia.sql` ne porte aucune table `CreatureFarmResource`, et `Telecaster.sql` non
plus : `grep -i creaturefarm Database/*.sql` → 0).

### 5.2 L'algorithme de la référence officielle (vérifié mot pour mot)

`onFosterCreature` (`GameMessage.cpp:11899-11908`) appelle `FosterCreature` (`:11847-11897`) sous le
verrou du joueur et **répond toujours**, quel que soit le résultat :

```cpp
resultMsg.result = FosterCreature( pClient, pMsg ) ? 1 : 0;   // :11905
PendMessage( pClient, &resultMsg );                            // :11907
```

`FosterCreature` (`:11847-11897`), dans l'ordre :

1. **la carte** (`:11854-11864`) : `StructItem::FindItem( pMsg->creature_card_handle )` doit rendre un
   objet non nul, `IsInInventory()`, `GetOwnerUID() == pClient->GetPlayerUID()`, et
   `GetSummonStruct()` doit être non nul ;
2. **le coût** (`:11866`) :
   `requiredTicketCount = GameContent::GetCreatureFarmTicketCount( creature->GetRate(), creature->GetTransformLevel(), creature->GetEnhance() )` ;
3. **les tickets** (`:11766-11800`, appelé `:11868`) : pour chaque entrée, l'objet doit exister, être de
   classe `ItemBase::CLASS_FARM_PASS` (`:11776`), porter au moins `ticket_count` unités (`GetCount() >=
   ticketInfo[i].ticket_count`), être en inventaire et appartenir au joueur (`:11778`) ; la **somme** des
   `ticket_count` doit égaler `requiredTicketCount` (`:11780`, `:11799`) ; à partir de la deuxième entrée,
   la **durée** et le **caractère premium** doivent être identiques à ceux de la première
   (`:11787-11796`) ;
4. **les crackers** (`:11802-11828`, appelé `:11869`) : `cracker_info_count == 0` est accepté tel quel
   (aucun cracker) ; sinon chaque objet doit être de classe `ItemBase::CLASS_CREATURE_FOOD` (`:11815`),
   porter au moins `cracker_count` unités, être en inventaire et appartenir au joueur, et la **somme des
   `cracker_count` doit égaler le même nombre que les tickets** — la fonction reçoit
   `requiredTicketCount` comme `requiredFoodCount` (`:11869`, `:11827`) ;
5. **la durée et le drapeau premium** viennent de la **donnée de l'objet ticket**, pas de la trame :
   `duration = ticket->GetItemBase().fOptVar1[0]` (`:11873`),
   `isCash = ( ticket->GetItemBase().fOptVar2[0] == 1 )` (`:11874`). Le binaire 7.3 lit la même donnée
   dans la base d'objet sous sa forme point-fixe ×10000 : `trunc( (int64) +0xcc / 10000.0 )`
   (`0x14011e34f-0x14011e372`, le double de `0x140403328` vaut `10000.0` — octets
   `00 00 00 00 00 88 c3 40`) et `(int64) +0xec == 10000` (`0x14011e364`). Les deux ères **disent la même
   chose** : un ticket de 5 jours porte `86400 × 5 = 432000` (le C++ stocke `4320000000`) et un ticket
   premium porte `1` (le C++ stocke `10000`). Côté NavisLamia, cette donnée est déjà modélisée :
   `ItemResourceEntity.OptVar1` / `OptVar2` (`decimal[]`, `ItemResourceEntity.cs:49-50`) ; la fiche
   `socle-artisanat-objets-officiel.md:47` relie déjà explicitement `fOptVar1[0]` / `fOptVar2[0]` à ces
   colonnes, et `socle-effets-resurrection.md:63-64` les y lit (`opt_var1_0`, `opt_var2_0`) ;
6. `useCracker = ( pMsg->cracker_info_count > 0 )` (`:11875`) ;
7. **l'emplacement** : `FindSuitableFarmSlot( pClient, isCash )` (`:11830-11845`, appelé `:11877`) rend
   `-1` si la ferme est pleine pour ce type de ticket ;
8. **le dépôt** : `pClient->FarmSummon( slot, pMsg->creature_card_handle, duration, useCracker, isCash )`
   (`:11879`) — c'est lui qui écrit la ligne de ferme et pose `ITEM_FLAG_FARMED_SUMMON`, cf.
   `socle-ferme-creatures-officielle.md` §5.2/§5.6 ;
9. **la consommation** : chaque pile de tickets (`:11881-11885`) puis chaque pile de crackers
   (`:11887-11891`) est retirée par `pClient->EraseItem( item, count )` — **après** le dépôt réussi, donc
   jamais de tickets perdus sur un refus ;
10. `return true` ⇒ `result = 1` ; toute sortie en échec ⇒ `result = 0`.

L'ère 7.3 (binaire 2012-11, symboles `FosterCreature` `0x14011e3e0`, `onFosterCreature` `0x14011e5f0`,
`ValidateFarmTicket` `0x14011e2d0`, `ValidateCracker` `0x14010cb20`, `FindSuitableFarmSlot` `0x14010cbc0`)
suit **le même ordre**, et confirme au passage les trois points de format que la source 2015 ne prouve pas
seule :

- les offsets lus sur la trame : carte en `+7` (`0x14011e50d`), `ticket_info` en `+0xb`
  (`0x14011e492`), `cracker_info` en `+0xf` (`0x14011e4ab`), tableaux à partir de `+0x13` ; la réponse
  est bâtie en 8 octets, id `0x1773` en `+4`, résultat en `+7` (`0x14011e681-0x14011e6a3`) ;
- les classes d'objets : `cmpl $0x193,0x10(%rdx)` = **403** pour un ticket (`0x14011e330`) et
  `cmpl $0x192,0x10(%rcx)` = **402** pour un cracker (`0x14010cb74`) ;
- `GetCreatureFarmTicketCount` `0x14018bb10` : la table est un vecteur d'enregistrements de **4 octets**
  dont la recherche compare **octet 0, octet 1, octet 2** aux trois paramètres et rend **l'octet 3**
  (`0x14018bbc1`, `0x14018bbd4`, `0x14018bbe8`, `mov 0x3(%rax),%al` en `0x14018bc0d`), et elle rend **0**
  quand aucune ligne ne correspond (`xor %al,%al` en `0x14018bc12`) — c'est le point qui tranche la
  correspondance octet → colonne de la table de coût (§5.5).

### 5.3 Ce que le dépôt fournit déjà au lot

| besoin du dépôt | primitive en place | source |
|---|---|---|
| écrire la ligne de ferme **et** le bit 27 de la carte en une transaction | `ICreatureFarmStore.InsertAsync(FarmedSummonDeposit)` | `Game/Services/Creatures/CreatureFarmStore.cs:28-29,50` et `:127-155` |
| le contenu du dépôt | `FarmedSummonDeposit(CharacterName, Slot, CardItemId, MaxLevel, IsUsingCracker, IsCash, RegistrationTime, Duration)` | `CreatureFarmStore.cs:28-29` |
| choisir l'emplacement (0 pour les tickets ordinaires, 1..2 pour les premium) | `CreatureFarmRules.TryFindSlot(isCash, takenSlots, out slot)` | `CreatureFarmRules.cs:109-123` |
| refuser une carte **déjà** déposée | `CreatureFarmRules.IsFarmed(flag)` (bit 27) | `CreatureFarmRules.cs:86` |
| les classes d'objets | `ItemType.FarmPass = 403`, `ItemType.CreatureFood = 402` | `Game/DataAccess/Entities/Enums/ItemType.cs:44-45` |
| résoudre une poignée d'objet | `CharacterService.GetItemByHandleAsync(characterName, uint)` | `Game/Services/CharacterService.cs:604` |
| l'emplacement occupé des 3 cases | `ICreatureFarmStore.LoadAsync(characterName)` | `CreatureFarmStore.cs:44` |

Deux points restent au lot : **déclarer `TM_SC_RESULT_FOSTER = 6003`** dans `GamePackets` **avec son
`case`** (critère 4 d'acceptation des lots dev ; l'id est délibérément absent de l'énumération aujourd'hui,
`GamePackets.cs:442-452`), et remplacer le `HandleFosterCreature` qui journalise sans répondre
(`GameClient.cs:1109-1127`).

### 5.4 L'identité des objets — **établi** (levée du `NON ÉTABLI` §7.6 de la fiche socle)

| rôle | règle | source |
|---|---|---|
| ticket | objet de classe **403** = `ItemBase::CLASS_FARM_PASS` | `Game/Resource/ItemBase.h:194` ; binaire 7.3 `0x14011e330` (`0x193`) ; dépôt `ItemType.cs:45` |
| cracker | objet de classe **402** = `ItemBase::CLASS_CREATURE_FOOD` | `Game/Resource/ItemBase.h:193` ; binaire 7.3 `0x14010cb74` (`0x192`) ; dépôt `ItemType.cs:44` |
| `using_cracker` | `cracker_info_count > 0` | `GameMessage.cpp:11875` (et le champ du `6001`, §3.2 de la fiche socle) |
| `using_cash` | la donnée de l'objet ticket : `OptVar2[0] == 1` | `GameMessage.cpp:11874` (`fOptVar2[0] == 1`) ; binaire 7.3 : entier de `+0xec` comparé à `10000` (`0x14011e364`, voir §6.2 point 4) ; dépôt : `ItemResourceEntity.cs:50` |

Ce que le dépôt n'a **pas** : la liste des **ids** d'objets qui sont des tickets ou des crackers (le
catalogue d'objets est en base ; la classe 403/402 suffit à les reconnaître) ni les ids de tickets
premium (`710007` / `710008` cités par la carte Trello, réservés à Killian, §7.2).

### 5.5 La table de coût en tickets — mesurée ici, **et hors de ce lot** (décision)

Le nombre de tickets que coûte un dépôt n'est ni dans la trame ni dans une constante : il vient de la
table de ressources `CreatureFarmResource` de la référence serveur, lue par
`GameContent::GetCreatureFarmTicketCount` (`GameContent.cpp:2910-2925`), qui **rend 0** quand aucune
ligne ne correspond (après un `assert`). La seule copie lisible localement de cette table est celle que
le **client 7.3 embarque** : `reference/client73/db_creaturefarm.rdb` — le client charge bien ce fichier
(chaîne `db_CreatureFarm.rdb` dans `SFrame.exe`).

**Forme, mesurée sur le fichier** (`sha256 36564e4a4405afa01133a4e519e6e83d5a379228ad4202ec4ed91b7d2dfdea3d`, 420 octets) :

| élément | mesure |
|---|---|
| octets 0-7 | `20110523` en ASCII, puis des zéros : en-tête de 128 octets |
| octets 128-131 | `48 00 00 00` = **72** enregistrements |
| octets 132-419 | **72 enregistrements de 4 `uint8`** : `128 + 4 + 72 × 4 = 420`, sans reste |
| marque d'outil tiers | **absente** : `strings` ne rend que `20110523`, aucun `Written by Archemedes` |

**Correspondance octet → colonne, tranchée par le binaire de l'ère 7.3** :
`GameContent::GetCreatureFarmTicketCount` (`0x14018bb10`) parcourt un vecteur d'enregistrements de
4 octets et compare l'`octet 0` au 1er paramètre, l'`octet 1` au 2e, l'`octet 2` au 3e, puis rend
l'`octet 3` (`0x14018bbc1`, `0x14018bbd4`, `0x14018bbe8`, `mov 0x3(%rax),%al` en `0x14018bc0d`). Le site
d'appel passe `GetRate()`, `GetTransformLevel()` puis `GetEnhance()` dans cet ordre
(`GameMessage.cpp:11866`) : les quatre colonnes sont donc **`rate`, `form`, `enhance_level`,
`ticket_count`**, celles de `CreatureFarmResource` (`ArcadiaSchemaPSQL.sql:46-52`, dont la ligne porte le
même nom). Le `NON ÉTABLI` §7.3 de la fiche socle (« la correspondance octet → colonne ... ne sont pas
établis ») est **levé** : les quatre colonnes de la déclaration DDL du dépôt sont les mêmes et dans le
même ordre (`rate`, `form`, `enhance_level`, `ticket_count` — `ArcadiaSchemaPSQL.sql:46-52`), et les
trois sources s'accordent.

**Les 72 lignes**, décodées telles quelles (les six renforcements 0-5 dans l'ordre) :

```
rareté (rate) 0, forme 1 : 1 2 2 3 3 4        rareté 0, forme 2 : 3 6 6 9 9 12
rareté 1, forme 1 : 1 2 2 3 3 4               rareté 1, forme 2 : 3 6 6 9 9 12
rareté 2, forme 1 : 1 2 2 3 3 4               rareté 2, forme 2 : 3 6 6 9 9 12
rareté 3, forme 1 : 2 3 3 4 4 5               rareté 3, forme 2 : 6 9 9 12 12 15
rareté 4, forme 1 : 2 3 3 4 4 5               rareté 4, forme 2 : 6 9 9 12 12 15
rareté 5, forme 1 : 3 4 4 5 5 6               rareté 5, forme 2 : 9 12 12 15 15 18
```

Contrôle indépendant : la ligne « Basic » publiée de la table Gala Lab (3 6 6 9 9 12) est exactement la
ligne `rate 0, form 2` — et rien ne contredit le reste (72 clés `(rate, form, enhance)` distinctes, aucun
doublon, aucun trou). Les trois clés existent bien côté dépôt : `SummonResourceInfo` porte `Rate` et
`Form` (`Game/Services/Creatures/CreatureCatalog.cs:13`), le renforcement vient du niveau de
`CreatureEnhanceInfo` de la carte (`:18`, `:82-83`).

**Décision : le chargement de cette table n'entre pas dans ce paquet.** Il justifie une carte séparée
« **Socle : ressource de coût des tickets de la ferme** », que le PO doit créer (c'est dit dans le compte
rendu de ce lot, pas sous-entendu). Motifs, tous vérifiables :

1. **Aucune donnée dans le dépôt ni dans les références de logique** : la table n'y existe qu'en
   déclaration DDL (`ArcadiaSchemaPSQL.sql:46-52`), sans entité EF, sans `DbSet` et sans le moindre
   `INSERT` (le seul `CreatureFarmResource` des sources et des `.sql` est cette ligne 46) ; **aucun dump
   NGemity** ne la porte non plus (`grep -i creaturefarm Database/*.sql` → 0 sur `Arcadia.sql` et
   `Telecaster.sql`).
2. **Aucun lecteur `.rdb` dans le dépôt** : les `.rdb` de `reference/client73/` y servent de référence
   lue à la main (comme les ids de catégorie d'enchères), ils ne sont pas chargés au démarrage ; la
   table devrait donc venir d'une migration/du seed, avec une décision de provenance.
3. **Cette provenance est une décision de données**, celle de Killian : le fichier n'est ni une table SQL
   serveur ni un `.csv` de l'export 9.4, c'est un fichier du **client**, et la seule marque temporelle
   qu'il porte est `20110523` (§7.5).
4. Le lot 6002 reste livrable sans elle : tout le reste de l'algorithme (§5.2 points 1, 3-10) se valide
   avec des primitives qui existent (§5.3).

Ce que le lot doit faire **en attendant** : écrire la recherche derrière une couture (une méthode de
règle ou une interface injectée, comme `ICreatureFarmStore` l'est déjà) qui **rend 0 quand la clé est
absente** — c'est le comportement de la référence (`GameContent.cpp:2924` : la clé absente rend 0, après l'`assert`
de `:2922`), et
il entraîne le refus du dépôt (une somme de tickets ≥ 1 ne peut pas égaler 0) au lieu d'accepter une
trame non vérifiée. Les 72 lignes ci-dessus sont épinglées ici pour que le lot « socle » n'ait **rien** à
re-dériver.

### 5.6 La réponse `TM_SC_RESULT_FOSTER` (6003) — les valeurs, **établies**

| valeur de `result` | sens | source |
|---|---|---|
| **1** | l'invocation est confiée : carte, tickets et crackers validés, ligne écrite, piles retirées | `GameMessage.cpp:11905` (`? 1 : 0`) ; binaire 7.3 `setne 0x8f(%rsp)` en `0x14011e6a3` |
| **0** | refus : toute sortie en échec de `FosterCreature` (carte inconnue / hors inventaire / pas au joueur / sans invocation, coût introuvable, tickets ou crackers insuffisants ou hétérogènes, ferme pleine, écriture refusée) | `GameMessage.cpp:11854-11893` ; binaire 7.3 `je 0x14011e5dc` / `je 0x14011e5d8` |

La référence **répond toujours** à une trame bien formée (`onFosterCreature` construit et envoie la
réponse sans condition, `:11899-11908`). Règle pour le lot : une trame bien formée reçoit donc toujours
son `6003` (8 octets), `result = 0` en cas de refus ; une trame **malformée** (tronquée, compteurs
incohérents avec la longueur) garde la convention du dépôt — journal d'avertissement **sans** réponse
(`GameClient.cs:1113-1115`), faute de trame de référence pour ce cas. Le client, lui, ne teste jamais la
valeur : il la recopie dans son événement interne d'id `0xae` (`SFrame.exe` `0x672220-0x672272`) et
laisse son interface conclure (§7.4).

### 5.7 Ce que le lot a livré — 7 octobre 2026

Le paquet est implémenté de bout en bout : une trame bien formée reçoit **toujours** son `6003`.

| pièce | fichi...[truncated]

---

## 6. Écarts assumés

### 6.1 Avec NGemity

**Aucun écart de logique à mesurer : NGemity ne traite pas ce paquet** (§5.1), et ses deux structures ne
divergent pas de rzu (`uint32_t` pour la poignée au lieu d'`ar_handle_t`, même 4 octets). Ce qui suit
vient de la source officielle et du binaire de l'ère 7.3, jamais de NGemity : pour cette famille,
NGemity n'est qu'une seconde source de format. C'est un écart de **périmètre** qu'il faut écrire :
répondre à `6002` n'est pas « porter NGemity », c'est trancher un comportement que NGemity n'a pas.

### 6.2 Entre la source 2015 (ère 9.x) et l'ère 7.3

1. **Le contrôle de propriété de chaque pile.** En 2015, `ValidateFarmTicket` et `ValidateCracker`
   reçoivent le joueur et vérifient `IsInInventory()` **et** `GetOwnerUID() == pClient->GetPlayerUID()`
   sur **chaque** ticket et **chaque** cracker (`GameMessage.cpp:11778`, `:11817`). Dans le binaire de
   l'ère 7.3 les deux fonctions ont une signature **sans joueur** (`ValidateFarmTicket(const
   TICKET_INFO*, int, int)`, `ValidateCracker(const CRACKER_INFO*, int, int)` — symboles du `.tsv`,
   `0x14011e2d0` / `0x14010cb20`) et se bornent à la classe et au nombre d'unités de la pile
   (`0x14011e330-0x14011e349`, `0x14010cb74-0x14010cb82`). **Écart assumé : on garde le contrôle de
   2015.** Un client légitime ne nomme que ses propres objets, donc rien d'observable ne change, tandis
   que le trou de 7.3 laisserait une trame forgée consommer la pile d'un autre joueur par sa poignée.
2. **La somme des crackers.** La référence ajoute `crackerInfo->cracker_count` — c'est-à-dire
   **l'entrée 0** — à chaque tour de boucle, l'index étant manquant (`GameMessage.cpp:11819`), et le
   binaire 7.3 fait exactement la même chose (`add 0x4(%rsi),%edi` en `0x14010cb8b`). Les deux ères
   comptent donc `n × cracker_count[0]` quand `n` entrées sont envoyées. Avec `C ≤ 1` (§2) les deux
   lectures coïncident : l'écart n'est **jamais observable par un vrai client**. **Écart assumé : on
   somme chaque entrée** (`crackerInfo[i].cracker_count`, la forme correcte qu'utilise déjà la boucle des
   tickets en `:11780`), pour qu'une trame forgée à deux piles soit jugée sur son vrai total.
3. **Le tableau de tickets vide.** `ticket_info_count == 0` passe la validation de la référence
   (`ticketCount == 0 == requiredTicketCount` quand le coût vaut 0, `GameMessage.cpp:11799`), puis
   `FosterCreature` lit `ticketInfo[0]` **hors de la trame** (`:11871` ; binaire 7.3 : `mov
   (%r12),%ecx` en `0x14011e4c1`). **Écart assumé : `T ≥ 1` est requis** ; `T = 0` est traité comme une
   trame malformée (journal, pas de réponse, §5.6). Le client 7.3 envoie toujours 1 (§2), donc rien de
   légitime ne tombe.
4. **La donnée du ticket (durée, premium).** 2015 lit `fOptVar1[0]` et `fOptVar2[0] == 1`
   (`:11873-11874`) ; le binaire 7.3 lit les deux mêmes variables sous leur forme point-fixe ×10000
   (`trunc((int64)+0xcc / 10000.0)` et `(int64)+0xec == 10000`, §5.2 point 5). **Il n'y a pas d'écart de
   valeur**, seulement d'unité interne : le dépôt lit la donnée déjà « humaine » dans
   `ItemResourceEntity.OptVar1/OptVar2` (`:49-50`) et suit 2015 sans conversion. Reste à confirmer
   l'unité de `OptVar1[0]` (§7.1).

### 6.3 Avec les données 7.3 du dépôt

Le modèle d'objet du dépôt ne porte **pas** la table de coût (§5.5) : c'est le seul manque, et il ne se
comble pas ici. Il porte en revanche tout le reste : les classes 403/402 (`ItemType.cs:44-45`), les
variables d'option du ticket (`ItemResourceEntity.cs:49-50`), le drapeau de ferme (`ItemFlag.cs` bit 27,
`CreatureFarmRules.cs:86-95`) et le magasin (`CreatureFarmStore.cs:41-56`). Aucune de ces pièces n'est à
réécrire : le lot les assemble.

### 6.4 Les trois pièges à ne pas porter (récapitulatif)

| piège de la référence | ce qu'il produirait | ce que fait NavisLamia |
|---|---|---|
| `T = 0` accepté puis `ticketInfo[0]` lu (`:11871`) | dépôt d'une carte **sans payer un seul ticket** quand le coût vaut 0 | `T ≥ 1` exigé, `T = 0` refusé (`result = 0`) |
| `foodCount += crackerInfo->cracker_count` (`:11819`) | somme des crackers fausse dès 2 entrées | somme par entrée (`crackerInfo[i]`) |
| pas de contrôle propriétaire/inventaire par pile en 7.3 (`0x14011e330`) | consommation d'une pile d'autrui par une trame forgée | contrôle de 2015 gardé (`:11778`, `:11817`) |

---

## 7. `NON ÉTABLI`

1. **L'unité de `OptVar1[0]` du ticket.** Tous les indices concordent sur des **secondes** : la valeur
   est affectée à un `int duration` (`GameMessage.cpp:11873`) que le dépôt utilise comme le `duration`
   en secondes de la ligne de ferme (le calcul d'heures de `socle-ferme-creatures-officielle.md` §5.3
   divise par 3600), et le binaire 7.3 rend le même entier (`trunc(valeur / 10000)`). Rien ne
   l'affirme mot pour mot, et aucun objet ticket n'est lisible (`db_item.rdb` n'est pas dans le
   périmètre). *Question : un ticket de 5 jours porte-t-il bien `opt_var1_0 = 432000` ?* (§A VERIFIER 2)
2. **Les ids d'objets.** La **classe** d'un ticket (403) et d'un cracker (402) est établie (§5.4), mais
   pas la liste des ids : ni le dépôt, ni la source 2015, ni NGemity ne la donnent, et les tickets
   premium que la carte Trello nomme (`710007`, `710008`) ne sont vérifiés nulle part dans les
   références locales. *Question : quels ids sont les tickets ordinaires, les tickets premium et les
   crackers ?* (§A VERIFIER 3)
3. **Le calcul des deux comptes dans le client.** Le constructeur `0x610910` les recopie de quatre
   variables
   (`0xc4c2c0`, `0xc4c2c4` et leurs homologues `0xc4c2c8`, `0xc4c2cc`) et refuse d'envoyer quand les
   paires diffèrent ; le client **charge** `db_CreatureFarm.rdb` (chaîne présente dans `SFrame.exe`),
   donc il peut calculer le coût — mais **le site qui remplit ces variables n'est pas identifié**. Le
   serveur ne peut donc pas se reposer sur « le client envoie exactement le coût de la table » pour
   valider : c'est précisément le rôle de la validation serveur.
4. **Ce que le client affiche selon la valeur de `result`.** Le handler de `6003` ne teste jamais
   l'octet : il le recopie dans l'événement interne `0xae` (`SFrame.exe` `0x672220-0x672272`). Quel
   libellé l'interface en tire (`msgbox_creatureFarmAssign` ? `msgbox_creatureFarmDoNotAssign1..4` ?)
   n'est pas établi — **sans conséquence** : le sens réseau de l'octet est établi (§5.6).
5. **La provenance des 72 lignes de la table de coût** (§5.5) : `db_creaturefarm.rdb` est le seul
   exemplaire local, il porte `20110523` et aucune marque d'outil tiers, et sa ligne `rate 0 form 2`
   recoupe la table publiée. En faire la source serveur est une **décision de données** (§A VERIFIER 1).

Les points **repris sans re-mesure** de la fiche socle : les constantes 7.3 (`FARM_MAX_LEVEL = 100`,
bases d'EXP, cracker ×1.5), le `max_level` gelé au dépôt (100 en premium, sinon le niveau du
personnage), et la règle des cases (0 pour les tickets ordinaires, 1-2 pour les premium) —
`socle-ferme-creatures-officielle.md` §5.2/§5.6/§6.2, déjà en place dans `CreatureFarmRules` et testée.

---

## A VERIFIER PAR KILLIAN

| # | question | ce que la fiche propose (défaut appliqué si rien n'est tranché) |
|---|---|---|
| 1 | La table de coût (`CreatureFarmResource`) : carte « Socle » séparée, ou repliée dans ce lot ? | **Séparée** (§5.5). Les 72 lignes sont décodées et épinglées dans §5.5 : si tu replies les deux lots, le dev pose la table et la couture devient un vrai *lookup*. Sinon, le dépôt refuse (coût inconnu = 0). |
| 2 | L'unité de `OptVar1[0]` d'un ticket | **Secondes** (§7.1). Sans réponse, on garde l'unité de 2015 et du binaire 7.3 : `duration = OptVar1[0]`, sans conversion. |
| 3 | Les **ids** des tickets (ordinaires et premium `710007`/`710008` ?) et des crackers | Non tranché (§7.2). Le code n'en a pas besoin : il reconnaît les piles par leur **classe** 403 / 402. |
| 4 | Garde du tableau de tickets vide (`T = 0`) | **Refuser** (`result = 0`, journal d'avertissement) — la référence, elle, le laisse passer puis lit hors trame (§6.4). |
| 5 | Somme des crackers à plusieurs entrées | **Sommer chaque entrée** (défaut `crackerInfo->` de la référence non porté, §6.2 point 2). Inobservable pour un vrai client (`C ≤ 1`). |
| 6 | Contrôle propriétaire/inventaire de chaque pile | **Garder celui de 2015** (absent du binaire 7.3, §6.2 point 1). |
| 7 | Réponse à une trame malformée | **Aucune** (journal seul, convention du dépôt `GameClient.cs:1113-1115`) : la référence n'a pas ce cas. Une trame bien formée est toujours répondue. |

---

## 8. Commits et fichiers épinglés

| référence | empreinte |
|---|---|
| Navislamia, base du lot | `master` = `f4f3f109d2014285540554211d9bcb13e6abfb32` (« fix(movement): trust the client's detours out of the dungeons, as 7.3 did ») |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` — `librzu/src/packets/GameClient/TS_CS_FOSTER_CREATURE.h`, `TS_SC_RESULT_FOSTER.h`, `src/lib/Packet/GameTypes.h` |
| NGemity | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` — `shared/Server/Packets/GameClient/TS_CS_FOSTER_CREATURE.h`, `TS_SC_RESULT_FOSTER.h`, `shared/Server/ClientPackets.h`, `shared/Server/XPacket.h`, `Database/Arcadia.sql`, `Database/Telecaster.sql` |
| source officielle `GameMessage.cpp` | `sha256 e7f5399ad9f2ab1e1ac0af49dcf6cc24774ecc1f4c2e80eee91da0130dbe5ac2` |
| source officielle `GameMessage.h` | `sha256 183e131508428a0cfa23513e41d80a6f28536aa584cf5803c88f9bedaf85e484` |
| source officielle `ItemBase.h` | `sha256 c8e4e5ff68add3d857a0bcd9b4f19ecb208f7ca7f47bcd52e9017069c920a87a` (nouveau dans le jeu de pins de cette fiche) |
| source officielle `GameContent.cpp` | `sha256 c297222bec0e04e7aba0398e9e0f35c32d38e0ff4f731c9ae990adc70894ae10` (idem) |
| source officielle `GameRule.h` | `sha256 eb293df87152b0e5ec4b23d2e3279333dd6a4a728eeaa6acf9ef8b332e086426` |
| source officielle `CreatureFarmLoader.cpp` | `sha256 fd6f5ac2cbdeef48744556ebf63045393912da0ecad2ddb4d1e77a9cd5003f7b` |
| serveur ère 7.3 (2012-11) | `CaptainHerlockServer.exe` `sha256 83b54fe18f15601578e35bcad1832a5c254bb09f512ab6297217fa49d9dd3eac` + `CaptainHerlockServer.symbols.tsv` `sha256 aef7e6bc8d436a3641fa5a589b53c557ae940e6b6ed4b555a648f8aab7c7e7f3` |
| client 7.3 | `SFrame.exe` `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` — constructeur `0x610910`, constructeur de base `0x6104d0`, handler `6003` `0x672220` |
| données 7.3 | `db_creaturefarm.rdb` `sha256 36564e4a4405afa01133a4e519e6e83d5a379228ad4202ec4ed91b7d2dfdea3d` (420 octets) |

Fichiers du dépôt lus pour cette fiche : `op_codes.md:258`, `ArcadiaSchemaPSQL.sql:46-52`,
`Game/Network/Packets/Game/GameFarmPackets.cs`, `Game/Network/Packets/Enums/GamePackets.cs:442-452`,
`Game/Network/Clients/GameClient.cs:1102-1127,3719-3723`,
`Game/Services/Creatures/CreatureFarmRules.cs`, `CreatureFarmStore.cs`, `CreatureCatalog.cs`,
`Game/DataAccess/Entities/Telecaster/CreatureFarmEntity.cs`, `ItemEntity.cs`,
`Game/DataAccess/Entities/Arcadia/ItemResourceEntity.cs`, `Game/DataAccess/Entities/Enums/ItemType.cs`,
`docs/packet-specs/socle-ferme-creatures.md`, `docs/packet-specs/socle-ferme-creatures-officielle.md`.

---

## Bloc pour CLAUDE.md

Bloc à recopier dans la description de la MR du lot (le dev n'écrit **pas** `CLAUDE.md`, le fichier est
protégé) :

```markdown
### Ferme de créatures — le dépôt `6002` (`TM_CS_FOSTER_CREATURE`)

- Format : en-tête 7 octets, puis `creature_card_handle` en `+7`, `ticket_info` (nombre) en `+11`,
  `cracker_info` (nombre) en `+15`, puis les deux tableaux d'entrées de 8 octets (`{handle u32,
  count i32}`) : **`19 + 8×T + 8×C`**. Un vrai client 7.3 envoie **27** (T=1, C=0) ou **35** (T=1, C=1) :
  il ne met jamais plus d'une pile de chaque. Réponse **`6003`, 8 octets**, `result` en `+7` :
  **1 = confiée, 0 = refus** — la référence répond **toujours** à une trame bien formée, le client ne
  teste pas l'octet (il le remet à son interface).
- Version : `X(6002, true)` et `X(6003, true)` sous `// Since EPIC_7_3`, **aucun** `version >=` dans les
  structures, `ar_handle_t` = 4 octets : rien à retirer pour 7.3. `TM_SC_RESULT_FOSTER` (6003) est **déclaré** depuis ce
 lot, avec son bras de dispatch (journal + `continue` pour une trame entrante, anomalie de protocole) :
 tout membre de `GamePackets` doit être routé avant le `switch` qui lève.
- Objets : un **ticket** est de classe **403** (`ItemType.FarmPass`), un **cracker** de classe **402**
  (`ItemType.CreatureFood`). Le nombre de tickets dû vient de la table `CreatureFarmResource`
  (`rate`, `form`, `enhance_level`, `ticket_count`) que le client porte dans `db_creaturefarm.rdb`
  (72 lignes de 4 octets, décodées dans la fiche) et que **le dépôt ne charge pas encore** : c'est une
  carte « Socle » séparée.
- Durée et drapeau premium du ticket : `OptVar1[0]` (secondes) et `OptVar2[0] == 1` de sa ressource
  (`ItemResourceEntity.OptVar1/OptVar2`), exactement ce que la source officielle appelle `fOptVar1[0]` /
  `fOptVar2[0]`. La case du dépôt : 0 pour un ticket ordinaire, 1-2 pour un premium
  (`CreatureFarmRules.TryFindSlot`), le dépôt écrit la ligne et le bit 27 en une transaction
  (`ICreatureFarmStore.InsertAsync`).
- Pièges de la référence à **ne pas** porter : elle accepte `ticket_info = 0` (validation vraie quand le
  coût vaut 0) puis lit `ticketInfo[0]` hors trame ; sa somme de crackers ajoute toujours
  `crackerInfo[0].cracker_count` (index manquant, `GameMessage.cpp:11819`) ; le binaire de l'ère 7.3 ne
  vérifie ni propriétaire ni inventaire sur les piles de tickets/crackers.
- Couture du coût : `ICreatureFarmTicketCost.GetTicketCount(rate, form, enhanceLevel)` est enregistrée
  **sans aucune ligne** tant que la table n'est pas chargée : elle rend 0 et **tout dépôt est refusé**
  (`result = 0`). Remplir la table est le seul geste du lot « Socle » — le service n'a pas à changer.
- Deux gardes que la référence n'a pas, et qui évitent un dépôt gratuit : une entrée de `ticket_count` /
  `cracker_count` **≤ 0** (une somme *négative* peut égaler le coût en ne consommant rien) et un ticket
  dont la ressource ne porte **pas de durée** exploitable (`OptVar1[0] ≤ 0`).
- Savoir complet et questions ouvertes : `docs/packet-specs/6002-foster-creature.md`.
```

## Revue d'intégration du 2026-10-07 (Claude, fusion des PR #84, #85, #86, #87)

Corrections apportées à la fusion, la fiche ci-dessus restant la trace du lot :

- **Heures en UTC.** Le dépôt et le soin écrivaient `DateTime.Now` (heure locale) dans des colonnes
  `timestamp with time zone` : Npgsql 8 lève une exception au `SaveChanges`, que les magasins factices des tests ne
  voient pas. `CreatureFarmStore` convertit en UTC à l'écriture. L'expiration d'une entrée (`IsExpired`, 6000)
  comparait l'heure UTC relue en base à l'heure locale : elle passe par `CreatureFarmRules.ElapsedSeconds`.
- **Table des coûts branchée.** `ICreatureFarmTicketCost` était enregistré vide (tout dépôt refusé) alors que le socle
  de la PR #86 charge `creature-farm-costs.73.json` : `CreatureFarmTicketCost.FromOptions`.
- **Refus de `FarmSummon` ajoutés au dépôt** (`StructPlayer.cpp:11264-11300`, `CreatureFarmRules.DepositRefusal`) :
  invocation au plafond (100, forme 1 à 60, forme 2 à 115), ticket ordinaire pour une invocation pas sous le niveau du
  maître, carte formée, invocation sortie ou carte de ceinture (`HeldItemRules`).
- **La carte quitte le sac du client** (`PopItem` → 254, `RemoveSummon` → 302 ; `PushItem` → 207, `AddSummon` → 301) :
  `ICreatureService.OnCardFarmedAsync`, `BuildInventory` n'envoie jamais une carte au bit 27, `HeldItemRules` et la
  formation la refusent. Le choix « la carte reste dans le sac » laissait une carte en ferme invocable, échangeable et
  vendable.
- **La fenêtre s'ouvre** : `show_creature_farm_window()` du PNJ 11467 envoie `TS_SC_DIALOG` type 9 `CreatureFarm`
  (`SCRIPT_ShowCreatureFarmWindow`). 6008 (marché de la ferme) reste sans réponse.
- Non repris, faute de champ dans le dépôt : le refus d'une carte à durée limitée ou à durabilité éthérée épuisée.
