# TM_CS_RETRIEVE_CREATURE (6004) — la reprise d'une invocation déposée à la ferme

Fiche du paquet traité par le lot `navis-dev` « Implémentation — `TM_CS_RETRIEVE_CREATURE` (6004) :
reprise d'une invocation à la ferme de créatures et réponse `TM_SC_RESULT_RETRIEVE` (6005) ». Elle
**épingle** ce que `docs/packet-specs/socle-ferme-creatures.md` (les neuf trames de la famille) et
`docs/packet-specs/socle-ferme-creatures-officielle.md` (le système et son sous-ensemble 7.3) ont déjà
établi, et elle **tranche** ce qui restait ouvert pour la reprise : l'ordre exact du geste et le plafond
d'EXP qu'il applique (§5.2, §5.5), le sort du formulaire évolué en 7.3 (§5.2 point 4), le fait que la
réponse vaut `1` même quand aucune EXP n'est gagnée (§5.3), et la façon dont le lot s'accroche aux
primitives déjà présentes dans le dépôt (§5.4).

Références lues pour cette fiche : `op_codes.md` ; rzu (`reference/rzu/librzu/src/packets/GameClient/`,
`src/lib/Packet/GameTypes.h`) ; NGemity (`reference/ngemity/shared/Server/Packets/GameClient/`,
`shared/Server/ClientPackets.h`, `Chihiro/src`) ; la source officielle (`reference/official2015/GameServer`) ;
le serveur de l'ère 7.3 **avec symboles** (`reference/epic7part4/server/2012-11/CaptainHerlockServer.exe`
+ `CaptainHerlockServer.symbols.tsv`) ; le client (`reference/client73/SFrame.exe`) ; le dépôt Navislamia
sur `master` = `a9f01fb`.

**Aucun code du serveur n'est écrit ni modifié par cette fiche. Aucun binaire, aucun Lua et aucun
script du client n'a été exécuté** : désassemblage `objdump -d`, lecture d'octets aux offsets PE calculés
et lecture de fichiers, uniquement.

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id | **6004** (`0x1774`) | `op_codes.md:260` — `[6004] = "TM_CS_RETRIEVE_CREATURE"` |
| Nom Navislamia | `TM_CS_RETRIEVE_CREATURE` | `op_codes.md:260` ; `Game/Network/Packets/Enums/GamePackets.cs:450` (`master` `a9f01fb`) |
| Nom rzu / NGemity | `TS_CS_RETRIEVE_CREATURE` | rzu `TS_CS_RETRIEVE_CREATURE.h:12` ; NGemity `shared/Server/Packets/GameClient/TS_CS_RETRIEVE_CREATURE.h:10` |
| Sens | **client → serveur** | rzu `TS_CS_RETRIEVE_CREATURE.h:12` (`SessionPacketOrigin::Client`) ; NGemity `shared/Server/ClientPackets.h:283` ; le client 7.3 en a un constructeur de trame (`0x610530`) et un émetteur (`0x6108c0`), le serveur 7.3 un handler (`onRetrieveCreature`, `0x14011e750`) et un `case` de dispatch (`GameMessage.cpp:12308`) |
| En-tête | 7 octets (`length` u32, `id` u16, `checksum` u8) | `socle-ferme-creatures.md` §3.0 ; `Game/Network/Packets/Header.cs:9-11` ; le client pose lui-même cet en-tête (`0x610564`, somme `0..5` en `0x61056a-0x610577`) |
| **Taille totale** | **11 octets** (`7 + 4`) | §3.2 ; `socle-ferme-creatures-officielle.md` §3.5/§3.7 ; `GameFarmPackets.cs:39` (`CreatureCardHandleLength`) |
| Réponse | **`TM_SC_RESULT_RETRIEVE` (6005)**, **8 octets**, `result` en `+7` : `1` = reprise, `0` = refus | rzu `TS_SC_RESULT_RETRIEVE.h:7` ; `socle-ferme-creatures-officielle.md` §3.4 ; §3.3 et §5.3 ci-dessous |
| État dans Navislamia (`master` `a9f01fb`) | **lecteur présent, handler présent, aucune réponse, id de réponse non déclaré** | lecteur `GameFarmPackets.cs:172` ; handler `GameClient.cs:1134` ; dispatch `GameClient.cs:3725-3729` ; `GamePackets.cs:442-452` ne déclare ni 6003, ni 6005, ni 6007 |

C'est le paquet « **reprise** » de la ferme de créatures : le client y nomme la carte d'invocation qu'il
reprend, et le serveur rend cette carte à son propriétaire en lui versant l'EXP de ferme accumulée, puis
répond `6005` avec **un octet de résultat** (§3.3). Dans le vocabulaire du client, c'est le bouton
`button_regain_01` ; la source officielle appelle le geste `RegainSummon`.

---

## 2. Ce que le joueur fait pour que le client l'envoie

1. Il ouvre la fenêtre de ferme (`window_creature_farm.nui`, chaîne présente dans `SFrame.exe`) : le
   client envoie `6000` et reçoit `6001`, qui liste ses invocations déposées (`socle-ferme-creatures.md`
   §2.1, §3.2).
2. Dans cette fenêtre il **sélectionne la carte** qu'il veut reprendre : la fenêtre garde la poignée de
   la carte sélectionnée dans son objet, en **`this+0x54c`** (`mov 0x54c(%ebx),%ecx` en `0x61498f` —
   c'est **le même champ** que celui que le chemin « assigner » du `6002` pousse en `0x614936`).
3. Il presse le widget **`"regain"`** : la chaîne de commande est en `.rdata 0xa40048` (offset fichier
   `0x63ea48`, `b'regain\x00\x00assign\x00\x00...'`), poussée en `0x61495f` (`push $0xa40048`) et
   comparée en `0x614965` (`call 0x977ff7`, une comparaison de `std::string`) ; toute autre commande
   part en `0x614aae` (`jne`, `0x61496f`). Les deux chaînes voisines `button_regain_01` (offset fichier
   `0x63e810`) et `msgbox_creatureFarmRegain` (offset fichier `0x641c54`) sont présentes dans la table du
   client — attribution de la fiche socle ; rien dans le désassemblage ne les relie au site d'appel.
4. **Une confirmation est demandée** avant l'envoi : `lea -0x24(%ebp),%eax` (`std::string` vide), `push`,
   `lea 0x33(%esi),%ecx`, `call 0x4f2330` (`0x61497b-0x614982`), et `je 0x614aae` sur refus
   (`0x614989`) — le refus **n'envoie rien**. Puis la poignée de `this+0x54c` est poussée
   (`0x61498f-0x614995`) et l'émetteur est appelé (`call 0x6108c0` en `0x614998`).
5. **Une poignée nulle n'est jamais envoyée** : l'émetteur `0x6108c0` teste son argument en `0x6108cd`
   (`test %edi,%edi`) et rend `0` sur une poignée `0` (`xor %eax,%eax` en `0x6108d2`), sans construire
   de trame. La sélection d'une carte est donc un préalable du côté client, pas une validation du
   serveur.

Mesuré dans le constructeur de trame `0x610530` (désassemblage local de `SFrame.exe`) :

| observation | instruction | adresse |
|---|---|---|
| `length` finale = **11** | `movl $0xb,(%eax)` | `0x610564` |
| `id` = **6004** (`0x1774`) écrit en `+4` | `mov $0x1774,%edx` puis `movw %dx,0x4(%eax)` | `0x610559`, `0x61055e` |
| charge utile remise à zéro de `+4` à `+0xa` | `mov %edx,0x4(%eax)`, `movw %dx,0x8(%eax)`, `mov %dl,0xa(%eax)` | `0x61054f-0x610556` |
| somme de contrôle de `0..5` écrite en `+6` (recalculée **après** que `length` vaut 11) | boucle sur `buffer..buffer+6` | `0x61056a-0x610577` |
| poignée écrite en `+7` par l'émetteur | `mov %edi,-0x5(%ebp)` (tampon = `-0xc(%ebp)`) | `0x6108ed` |

Le constructeur `0x610530` ne pose **que** l'en-tête (11 octets, charge utile nulle à ce stade) ; c'est
l'émetteur `0x6108c0` qui écrit la poignée en `+7` et envoie (`call 0x649ad0` en `0x6108f0`, retour `1`
en `0x6108f6`). Un vrai client 7.3 ne produit donc **que des trames de 11 octets** pour ce paquet.

---

## 3. Structure sur le fil

### 3.1 En-tête — 7 octets

| offset | type | nom | valeur / source |
|---|---|---|---|
| 0 | `uint32` | `length` | total de la trame, en-tête compris : **11**. `socle-ferme-creatures.md` §3.0 ; `Header.cs:9,22` ; client `0x610564` |
| 4 | `uint16` | `id` | **6004** = `0x1774`. `socle-ferme-creatures.md` §3.0 ; `Header.cs:10,23` ; client `0x610559` |
| 6 | `uint8` | `checksum` | somme octet par octet de `0..5`. `PacketExtensions.cs:13-25` ; le client fait le même calcul (`0x61056a-0x610577`) |

### 3.2 Champ — la carte, 4 octets

| offset | type | nom | valeur observée / source |
|---|---|---|---|
| 7 | `uint32` (`ar_handle_t`) | `creature_card_handle` | la carte à reprendre. rzu `TS_CS_RETRIEVE_CREATURE.h:7` (`_(simple)(ar_handle_t, creature_card_handle)`) ; NGemity `TS_CS_RETRIEVE_CREATURE.h:7` (`uint32_t`) ; source officielle `GameMessage.h:3924-3929` (`AR_HANDLE`) ; client `0x6108ed` ; serveur 7.3 `mov 0x7(%rbx),%edx` en `0x14011e7f6` ; dépôt `GameFarmPackets.cs:194` (`ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4))`) |

**Taille totale attendue : 11 octets.** Aucun tableau, aucun champ optionnel, aucun compteur : ni le
paquet ni la rotation de l'ère 7.3 n'en connaissent d'autre forme (le serveur 7.3 lit la poignée en `+7`
sans jamais tester la taille, et le client n'en construit qu'une seule).

Dans le dépôt, les offsets sont déjà posés : `GameFarmPackets.cs:27` (`HeaderSize = 7`),
`:39` (`CreatureCardHandleLength = HeaderSize + 4`), lecture bornée par `TryReadRetrieveCreature`
(`:172`) via `TryReadCreatureCardHandle` (`:186-196`, refus de toute longueur ≠ 11).

### 3.3 Réponse `TM_SC_RESULT_RETRIEVE` (6005) — 8 octets

| offset | type | nom | valeur observée / source |
|---|---|---|---|
| 7 | `int8` | `result` | **`1` = la carte est rendue, `0` = refus** (`socle-ferme-creatures-officielle.md` §3.4, `GameMessage.cpp:11916`). rzu `TS_SC_RESULT_RETRIEVE.h:6-7` (`_(simple)(int8_t, result)`) ; NGemity `TS_SC_RESULT_RETRIEVE.h:7` ; source officielle `GameMessage.h:3932-3937` (`char result`) |

**Taille totale attendue : 8 octets.** Le serveur 7.3-era bâtit exactement cette trame dans
`onRetrieveCreature` (`0x14011e750`) : `mov %rcx,0x80(%rsp)` remet la trame de 8 octets à zéro
(`0x14011e7d9`), `movw $0x1775,0x84(%rsp)` pose l'id **`0x1775` = 6005** en `+4` (`0x14011e7e1`),
`movl $0x8,0x80(%rsp)` pose la longueur en `+0` (`0x14011e7eb`), `test %al,%al` / `setne 0x87(%rsp)`
écrit le résultat en `+7` (`0x14011e801-0x14011e803`), puis `call 0x1401496e0` = `PendMessage`
(`0x14011e816`).

Le client ne lit **qu'un octet en `+7`** et rien d'autre : `mov 0x7(%ecx),%dl` puis `mov %dl,0x13(%eax)`
(`0x67231e`, `0x672321`), recopié sans aucun test dans un objet d'événement interne de 20 octets
(`push $0x14`, `call 0x97671b`) dont l'id est **`0xb0`** (`movl $0xb0,0x4(%eax)` en `0x6722f6`) et la
vtable `0xa52160` (`0x67230e`) ; l'objet est remis à la file interne par `call 0x64d0e0` (`0x67232e`).
La table de commutation de la ferme (`0x67f68c`, index = `id − 0x1771`, `sub $0x1771,%eax` /
`cmp $0x6,%eax` / `ja` en `0x67e828-0x67e830`) route `6005` sur ce handler (`0x67e864`), et laisse
`6002`, `6004` et `6006` au défaut `0x67ef21` — même relevé que le commentaire de `GameFarmPackets.cs:12-14`.

### 3.4 Récapitulatif des tailles

| trame | sens | taille |
|---|---|---|
| `TM_CS_RETRIEVE_CREATURE` (6004) | client → serveur | **11** |
| `TM_SC_RESULT_RETRIEVE` (6005) | serveur → client | **8** |

Les deux seuls ids de la famille que ce lot a besoin de lire ou d'émettre : l'arithmétique est celle de
`socle-ferme-creatures-officielle.md` §3.7, sans exception.

---

## 4. Gating de version — tranché pour Epic 7.3

| élément | gating rzu | décision 7.3 | source |
|---|---|---|---|
| id de la demande | `X(6004, true)` sous `// Since EPIC_7_3` | **6004**, aucune variante d'id | rzu `TS_CS_RETRIEVE_CREATURE.h:8-10` ; NGemity `TS_CS_RETRIEVE_CREATURE.h:9-10` (`// Since EPIC_7_3`, `CREATE_PACKET(TS_CS_RETRIEVE_CREATURE, 6004)`) |
| id de la réponse | `X(6005, true)` sous `// Since EPIC_7_3` | **6005**, aucune variante | rzu `TS_SC_RESULT_RETRIEVE.h:8-10` ; NGemity `TS_SC_RESULT_RETRIEVE.h:9-10` ; NGemity `ClientPackets.h:283-284` |
| `creature_card_handle` | `_(simple)(ar_handle_t, …)` — `ar_handle_t` est un `strong_typedef<…, uint32_t>` **sans** `version >=` | **4 octets**, non signé | rzu `TS_CS_RETRIEVE_CREATURE.h:7` ; `GameTypes.h:40` |
| `result` de la réponse | `_(simple)(int8_t, result)` | **1 octet** signé | rzu `TS_SC_RESULT_RETRIEVE.h:7` |

Les deux en-têtes rzu de ce paquet ont été lus **en entier** (13 lignes chacun) : ils ne portent
**aucune** condition `version >= EPIC_*`, aucun champ versionné et aucun `_(pad)`. Rien n'est à retirer
ni à décaler pour 7.3 ; le lot ne déclare donc aucune variante d'id ni branche de version.

Détail relevé au passage, sans conséquence : contrairement à rzu `TS_CS_NURSE_CREATURE.h:5`
(`// Last tested: EPIC_9_8_1`), `TS_CS_RETRIEVE_CREATURE.h` ne porte pas de ligne « Last tested ».

Décision : `TM_SC_RESULT_RETRIEVE` (6005) entre dans `GamePackets` comme les autres membres de la famille
(§5.4), et son bras de dispatch suit la convention déjà posée pour `TM_SC_FARM_INFO` et `TM_SC_RESULT_FOSTER`
(journal + `continue` sur une trame entrante, qui est une anomalie de protocole).

---

## 5. Traitement attendu

Sauf mention contraire, l'algorithme vient de `official2015/GameServer/Game/Message/GameMessage.cpp:11910-11919`
(le handler et le dispatch `:12308`) et `Game/Struct/StructPlayer.cpp:11332-11421` (`RegainSummon`).

### 5.1 Ce que la référence fait du paquet

```cpp
void onRetrieveCreature( StructPlayer * pClient, TS_CS_RETRIEVE_CREATURE * pMsg )   // :11910
{
    ARCADIA_LOCK( … );
    TS_SC_RESULT_RETRIEVE resultMsg;
    resultMsg.result = pClient->RegainSummon( pMsg->creature_card_handle ) ? 1 : 0;   // :11916
    PendMessage( pClient, &resultMsg );                                              // :11918
}
```

Le serveur répond **inconditionnellement** à une trame bien formée, avec l'octet renvoyé par
`RegainSummon`. Le binaire de l'ère 7.3 fait exactement cela et rien de plus
(`0x14011e7f6` lecture de la poignée, `0x14011e7fc` `call 0x1400d5e60` = `StructPlayer::RegainSummon`,
`0x14011e803` `setne`, `0x14011e816` `PendMessage`).

### 5.2 `RegainSummon` pas à pas — et ce que le binaire 7.3 en confirme

Source 2015 (`StructPlayer.cpp:11332-11421`), chaque étape recoupée sur
`CaptainHerlockServer.exe` (ère 7.3, `RegainSummon` = **`0x1400d5e60`**, fin `0x1400d6335`) :

| # | 2015 | mesure 7.3 |
|---|---|---|
| 1 | `StructItem *pCard = StructItem::FindItem( hHandle )` ; `!pCard` → `false` (`:11334-11335`) | `call 0x1400ad020` = `StructItem::FindItem(unsigned int)` en `0x1400d5e8e` |
| 2 | la carte doit porter `ITEM_FLAG_FARMED_SUMMON` (`:11338-11341`) | `btl $0x1b,0x15c(%rax)` = **bit 27** du drapeau de l'instance, en `0x1400d5e9f` ; refus en `0x1400d5ea9` |
| 3 | une entrée de ferme doit nommer cette carte : boucle sur `FARM_MAX_COUNT` cases, `pInfo->item->GetItemUID() == pCard->GetItemUID()` (`:11343-11348`) | `lea 0x2ac0(%r13)` = `m_vFarmedSummonInfo[0]` (`0x1400d5eb7`) ; `cmp $0x3,%rdx` = **3 cases** (`0x1400d5ee7`) ; `mov 0x8(%rsi),%rax` = `pInfo->item` (`0x1400d5ec8`), UID de l'objet à `+0x100` comparé à `+0x100` de la carte (`0x1400d5ecc-0x1400d5ed3`) |
| 4 | `pCard->GetSummonStruct()` ; nul → `assert(0)` puis `false` (`:11350-11355`) | `mov 0x190(%rdi),%r12` = l'invocation à `+0x190` (`0x1400d5ef3`) ; nul → refus en `0x1400d5eff` |
| 5 | `SetOwnerInfo(0,0,0)`, `pSummon->SetMaster(NULL)`, drapeau **éteint** (`:11358-11360`) | maître à `+0x23e0` mis à `NULL` (`mov %rbx,0x23e0(%r12)` en `0x1400d5f3e`) ; `andl $0xf7ffffff,0x15c(%rdi)` = bit 27 effacé (`0x1400d5f46`) |
| 6 | `PushItem( pCard, pCard->GetCount() )` (`:11362`) | `call 0x1400c5430` = `StructPlayer::PushItem` (`0x1400d5f70`) |
| 7 | `DB_UpdateItem` puis `SendItemMessage` (`:11364-11365`) | `call 0x140357f10` = `GameDBManager::DBProc::operator new` (`0x1400d5f7a`), `call 0x140044150` = `DB_UpdateItem::DB_UpdateItem(StructItem*, bool)` (`0x1400d5f95`), `call 0x1400af5a0` = `StructItem::DBQuery` (`0x1400d5fa5`), puis `call 0x14014bda0` = `SendItemMessage` (`0x1400d5fb0`) |
| 8 | `tHour = (min(now, registration_time + duration) − registration_time) / 3600` (`:11367`) | `movslq 0x20(%rsi),%rbx` = `duration` (+0x20), `add 0x18(%rsi),%rbx` = `+ registration_time` (+0x18) (`0x1400d5fb5-0x1400d5fb9`), `call 0x1403a5b30` = `_time64` (`0x1400d5fc7`), `sub 0x18(%rsi),%rcx` (`0x1400d5fee`), division par 3600 par la constante magique `0x48d159e26af37c05` (`0x1400d5ff2-0x1400d6009`) |
| 9 | `dRate = f.is_using_cracker ? 1.5 : 1.0` (`:11368`) | `cmpb $0x0,0x14(%rsi)` = `is_using_cracker` (+0x14) en `0x1400d6010` ; `movsd` de **`1.5`** en `0x1400d6016` (`0x14040a2c8`) et de **`1.0`** en `0x1400d6020` (`0x140400730`) |
| 10 | `nLevelLimit = min( f.max_level, FARM_MAX_LEVEL )` (`:11371`) | `lea 0x10(%rsi),%rax` = `max_level` (+0x10) en `0x1400d602c`, `cmpl $0x64,(%rax)` / `cmovg` vers la constante **100** `0x1404096c0` (`0x1400d6030-0x1400d603e`) |
| 11 | `switch( pSummon->GetTransformLevel() )` sur `EVOLVE_NORMAL` / `EVOLVE_GROWTH` / `EVOLVE_EVOLVE`, tarif premium si `is_cash`, `default: return false` (`:11373-11398`) | `call 0x1401066b0` = `StructSummon::GetTransformLevel()` (`0x1400d604c`), puis `sub $0x1,%eax` / `je` → branche **1**, `sub $0x1,%eax` / `je` → branche **2**, **tout autre valeur → `false`** (`xor %al,%al` en `0x1400d605b`) |
| 11a | branche 1 = `EVOLVE_NORMAL` : `nExp = tHour × nFarmNormalSummonEXP × dRate`, `nLevelLimit = min( nLevelLimit, NORMAL_SUMMON_MAX_LEVEL )` | `movslq 0x1404f7e68` = **137700** (`0x1400d609f-0x1400d60aa`) puis `cmp $0x3c,%r14d` / `cmovg` vers la constante **60** `0x1404096b8` (`0x1400d60cf-0x1400d60d7`) |
| 11b | branche 2 = `EVOLVE_GROWTH` : idem avec `nFarmGrowthSummonEXP` et `GROWTH_SUMMON_MAX_LEVEL` | `movslq 0x1404f7e6c` = **347264** (`0x1400d6062-0x1400d606d`) puis `cmp $0x73,%r14d` / `cmovg` vers la constante **115** `0x1404096bc` (`0x1400d6092-0x1400d609a`) |
| 12 | `nExpLimit = GameContent::GetNeedSummonExp( nLevelLimit ) − 1` (`:11402`) | `call 0x140188970` (`0x1400d60fd`) puis `sub $0x1,%rax` (`0x1400d6102`) |
| 13 | `if( nExpLimit > pSummon->GetEXP() ) { nExp = min( nExp, nExpLimit − exp ) ; pSummon->AddExp( nExp, 0, false, true ) }` (`:11404-11408`) | `mov 0x1c8(%r12),%rcx` = `GetEXP()` (`0x1400d6106`), `cmp` / `jle` = rien à gagner (`0x1400d6111`), `sub` / `cmovge` = `min` (`0x1400d6113-0x1400d6131`), puis `movb $0x1,0x20(%rsp)` (`0x1400d6139`) et `call *0x408(%rax)` = `AddExp( nExp, 0, false, true )` (`0x1400d614a`) |
| 14 | `LOG::Log11N4S( LM_FARM_REGAIN, … )` (`:11410`) | `mov $0xeda,%cx` (id de message **0xeda**) en `0x1400d62de`, `call 0x14038cb50` en `0x1400d62e2` |
| 15 | `DBQuery( new DB_DeleteFarmInfo( this, pInfo->farm_sid ) )` ; `delete pInfo` ; `SetFarmedSummonInfo( i, 0 )` ; `return true` (`:11412-11416`) | `call 0x1400c2380` = ctor `DB_DeleteFarmInfo` (`0x1400d6307`), `call 0x1400d2f10` = `StructPlayer::DBQuery` (`0x1400d6317`), `call 0x14039cfd0` = `operator delete` (`0x1400d631f`), case vidée (`mov %rdi,0x2ac0(%r13,%r11,8)` en `0x1400d6327`), `mov $0x1,%al` en `0x1400d632f` |
| 16 | aucune case ne correspond → `return false` (`:11420`) | `xor %al,%al` en `0x1400d6333` (cible du refus) |

Deux conséquences que la fiche socle laissait implicites et que le lot doit connaître :

- **Tous les refus précèdent toute écriture.** Les étapes 1 à 4 sont des contrôles ; l'étape 5 est la
  première modification. Il n'existe donc **aucun** état partiel : soit la carte est rendue avec sa
  ligne de ferme supprimée (`true`), soit rien n'a bougé (`false`).
- **`result = 1` ne veut pas dire « de l'EXP a été gagnée »** : il veut dire « la carte est reprise ».
  Une reprise à 0 heure entière, ou sur une invocation dont l'EXP a déjà atteint `nExpLimit`, rend
  `true` sans qu'un seul point d'EXP soit versé (étape 13, branche `jle`). Il n'y a pas de troisième
  valeur : `0` = refus, `1` = reprise, et c'est tout (`socle-ferme-creatures-officielle.md` §3.4).

### 5.3 Ce que le serveur NavisLamia doit répondre

| cas | réponse |
|---|---|
| carte inconnue pour ce personnage (aucun objet de ce handle) | `TM_SC_RESULT_RETRIEVE` (6005), 8 octets, `result = 0` |
| la carte ne porte pas le bit de ferme (`ItemFlag.FarmedSummon`, bit 27) | idem, `result = 0` |
| aucune ligne de ferme ne nomme cette carte | idem, `result = 0` |
| la carte ne porte aucune invocation | idem, `result = 0` |
| le formulaire de l'invocation n'est ni 1 (normal) ni 2 (croissance) — formulaire évolué compris | idem, `result = 0` ; **mesuré**, `0x1400d605b` (§5.2 étape 11) |
| sinon | idem, `result = 1`, après avoir versé l'EXP, éteint le bit et supprimé la ligne |
| trame malformée (longueur ≠ 11) | **aucune réponse**, journal seul — convention du dépôt (`GameClient.cs:1136-1141`) ; la référence n'a pas ce cas, elle reçoit toujours 11 octets |

Convention de nommage proposée, parallèle au lot `6002` : `GameFarmPackets.RetrieveResultTaken = 1` /
`RetrieveResultRefused = 0`, `RetrieveResultLength = HeaderSize + 1`, `RetrieveResultOffset = HeaderSize`,
et un constructeur `BuildResultRetrieve(byte result)` bâti comme `BuildResultFoster` (écriture de
`length` et `id`, l'octet en `+7`, puis `WriteChecksum` — `GameFarmPackets.cs:331`).

### 5.4 Ce que le dépôt fournit déjà au lot

| besoin du lot | primitive en place (`master` `a9f01fb`) | source |
|---|---|---|
| retrouver la ligne de ferme de la carte | `ICreatureFarmStore.LoadAsync(characterName)` → `FarmedSummon{Id, Slot, CardItemId, MaxLevel, IsUsingCracker, IsCash, RegistrationTime, Duration, NursingTime, Experience, Name, CardInfo}` | `Game/Services/Creatures/CreatureFarmStore.cs:19-21,44` |
| supprimer la ligne **et** éteindre le bit 27 | `ICreatureFarmStore.RemoveAsync(characterName, cardItemId)` | `CreatureFarmStore.cs:46-64` (son commentaire réserve déjà ce geste au lot de la reprise) |
| la carte et son invocation (niveau, EXP, `Form`) | `ICharacterService.GetCreatureStateAsync(characterName, cardIds)` → `CreatureCardRecord(ItemEntity Card, SummonEntity Summon)` / `CreatureState` ; `ICreatureService.FindCard(info, cardHandle)` pour la carte vivante | `Game/Services/ICharacterService.cs:272,340,343` ; `Game/Services/Creatures/CreatureService.cs:97,129` |
| verser l'EXP de ferme | `CreatureService.GainExperience(GameClient master, CreatureCard card, long gain, bool force = false)` (envoie la mise à jour d'EXP au maître, et la montée de niveau le cas échéant) | `CreatureService.cs:973-1009` ; persistance : le maître sauve les invocations (`:553`), et une montée de niveau sauve tout de suite (`:1076`) |
| les heures entières gagnées | `CreatureFarmRules.FarmedHours(registrationTime, durationSeconds, now)` | `CreatureFarmRules.cs:130` |
| l'EXP de base | `CreatureFarmRules.GainedExp(farmedHours, expPerHour, isUsingCracker)` | `CreatureFarmRules.cs:157` |
| le plafond de niveau de la ligne | `CreatureFarmRules.LevelLimit(maxLevel, formLevelCap)` = `min(min(maxLevel, 100), formLevelCap)` | `CreatureFarmRules.cs:164-165` ; `MaxLevel = 100` `:39`, `NormalFormLevelCap = 60` `:42`, `GrowthFormLevelCap = 115` `:45` |
| les taux horaires et le cracker | `NormalExpPerHour = 137_700` `:57`, `GrowthExpPerHour = 347_264` `:63`, `CrackerRate = 1.5` `:51` | `CreatureFarmRules.cs` |
| le drapeau de ferme | `CreatureFarmRules.IsFarmed(flag)` `:86`, `WithoutFarmedSummon(flag)` `:95` (appelé par `RemoveAsync`) | `CreatureFarmRules.cs` |
| la courbe de l'invocation | `ICreatureCatalog.NeedExp(int level)` | `Game/Services/Creatures/CreatureCatalog.cs:42,80` |
| le plafond d'EXP de `AddExp` | `SummonProgression.CapGain(currentExp, gain, level, form, needExp, force)` | `SummonProgression.cs:177-203` |
| la trame entrante | `GameFarmPackets.TryReadRetrieveCreature` `:172` (longueur 11, poignée en `+7`) | `GameFarmPackets.cs:39,172-196` |

Ce qui **manque** et que le lot ajoute : la déclaration `TM_SC_RESULT_RETRIEVE = 6005` dans `GamePackets`
(avec son bras de dispatch, critère d'acceptation 4), le constructeur `BuildResultRetrieve`, le geste
lui-même (un service du style `ICreatureFarmRetrieveService`, ou une méthode de la couture existante) et
le passage du handler `HandleRetrieveCreature` en asynchrone (§5.6).

### 5.5 L'ordre exact à reproduire, et les deux pièges de plafond

1. **Contrôles, avant toute écriture** : la carte existe et appartient au personnage ; elle porte
   `ItemFlag.FarmedSummon` ; une ligne de `LoadAsync` porte `CardItemId == creature_card_handle` ; la
   carte porte une invocation. Un échec ici → `result = 0`, rien de modifié.
2. **Le formulaire** : `resource.Form` de l'invocation de la carte (`ICreatureCatalog.TryGetSummon`) —
   `1` → normal, `2` → croissance, **tout autre valeur (3 = évolution) → `result = 0`, rien de
   modifié**. C'est le comportement mesuré du 7.3, et c'est aussi ce que conclut la fiche socle
   (`socle-ferme-creatures-officielle.md` §6.2 : « la ferme 7.3 ne sert que 2 formulaires »). Le champ
   `is_cash` de la ligne **n'entre pas** dans le calcul en 7.3 : `RegainSummon` ne lit que `+0x08`
   (`item`), `+0x10` (`max_level`), `+0x14` (`is_using_cracker`), `+0x18` (`registration_time`),
   `+0x20` (`duration`) et `+0x0` (`farm_sid`) — relevé exhaustif des accès à `%rsi` dans
   `0x1400d5e60-0x1400d6335`, aucune lecture de `+0x15` (`is_cash`).
3. **Le plafond de niveau** : `nLevelLimit = CreatureFarmRules.LevelLimit(row.MaxLevel, formCap)` avec
   `formCap = NormalFormLevelCap` (formulaire 1) ou `GrowthFormLevelCap` (formulaire 2).
4. **Le gain brut** : `hours = FarmedHours(row.RegistrationTime, row.Duration, now)` (heures entières,
   plafonnées par la durée du ticket), puis
   `gain = GainedExp(hours, expPerHour, row.IsUsingCracker)` avec le taux horaire du formulaire.
5. **Le plafond d'EXP — piège n° 1, à ne pas omettre** :
   `nExpLimit = _catalog.NeedExp(nLevelLimit) − 1` ; si `nExpLimit <= <exp de l'invocation>` alors
   `gain = 0`, sinon `gain = min(gain, nExpLimit − exp)`. **Ce plafond-là n'est pas celui de
   `AddExp`** : `SummonProgression.CapGain` plafonne au dernier niveau du **formulaire** (60 ou 115),
   qui est *au-dessus* du `max_level` gelé de la ligne quand le joueur était plus bas au dépôt
   (`bCash ? 100 : niveau du joueur`, `socle-ferme-creatures-officielle.md` §5.3 point 4). Sans l'étape
   du `min` sur `NeedExp(nLevelLimit) − 1`, une reprise ferait dépasser à l'invocation le plafond que le
   dépôt a gelé.
6. **Le versement** : `GainExperience(client, card, gain, force: true)` — `force` parce que le gain de
   ferme n'est **pas** plafonné par `SummonProgression.ExpLimit(level)` (`level² × 200`, le plafond des
   gains de chasse) mais seulement par la courbe `NeedExp` de l'étape 5 (`SummonProgression.cs:177-203`
   et son commentaire).
7. **La sortie de ferme** : `RemoveAsync(characterName, cardItemId)` (ligne + bit 27 dans un seul
   `SaveChanges`). L'ordre de la référence — carte rendue, puis EXP, puis ligne supprimée — est
   conservé : si `GainExperience` ne verse rien (étape 5), la ligne disparaît quand même, et c'est
   `result = 1`.
8. **La réponse** : `BuildResultRetrieve(1)` ; sur un refus des étapes 1-2, `BuildResultRetrieve(0)`.

Ce que la référence fait et que le dépôt **n'a pas à faire** : `SetOwnerInfo(0,0,0)` et `PushItem`
(étapes 5-6). Le modèle de la fiche socle garde la carte **dans le sac** pendant la ferme, le bit 27
étant la seule marque (c'est ce que teste `CreatureFarmRules.IsFarmed`) ; il n'y a donc rien à
re-pousser à la reprise (§6.3).

### 5.6 Ce que le lot doit toucher dans le dépôt, et les tests

| geste | fichier / point d'accroche |
|---|---|
| déclarer `TM_SC_RESULT_RETRIEVE = 6005` | `GamePackets.cs:442-452` (le bloc de la famille, juste après `TM_CS_RETRIEVE_CREATURE = 6004`), **avec** son bras de dispatch (critère 4) |
| le constructeur de la réponse | `GameFarmPackets.cs` (à côté de `BuildFarmInfo` `:270` et `WriteChecksum` `:331`) |
| le handler qui répond | `GameClient.cs:1129-1150` : `HandleRetrieveCreature` devient asynchrone (`HandleRetrieveCreatureAsync`), et le dispatch `GameClient.cs:3725-3729` l'appelle en `_ = …` — exactement le motif que le lot `6002` a posé pour `HandleFosterCreatureAsync` (diff de `GameClient.cs` sur la branche `hermes/packet-6002-foster-creature` : le handler devient `async Task` et le bras de dispatch passe à `_ = HandleFosterCreatureAsync(msgBuffer)`) |
| le geste | une couture injectée, sur le modèle de `ICreatureFarmStore` / du service de dépôt : elle reçoit le client et la poignée, et rend `bool` (repris / refusé) ; le handler répond **toujours**, y compris sur exception, pour qu'aucun chemin d'échec ne laisse le client sans son `6005` |
| le commentaire d'en-tête | `GameFarmPackets.cs:9-19` et le bloc de commentaire du dispatch `GameClient.cs:3706-3712` annoncent « 6004 est lu et non répondu » : les deux textes deviennent faux avec ce lot et font partie du livrable |

**Tests.** Deux points d'accroche existants, plus les nouveaux :

1. `Tests/Game/FarmPacketsTests.cs:617-635` — `FarmFrames_NeverThrowAndOnlyTheFarmInfoRequestIsAnswered`
   passe aujourd'hui le cas `TM_CS_RETRIEVE_CREATURE` (`:619-620`) dans un `connection.Sent.Should().BeEmpty()` :
   ce cas **change de sens** avec ce lot (une trame bien formée est désormais répondue). Le lot `6002` a
   fait exactement cette bascule pour son propre cas.
2. `Tests/Game/FarmPacketsTests.cs:524-595` — la famille « 11 octets, poignée en +7 » couvre déjà la
   lecture (`CreatureCardHandleLength`, offsets, refus de toute autre longueur) : rien à y reprendre.
3. **Critère d'acceptation 3** : au moins un test d'offsets pour la **réponse** — taille totale 8,
   `id` en `+4`, `result` en `+7` — dans la discipline du dépôt.
4. État mesuré de la base du lot, pour que le compte ne baisse jamais : `dotnet build Navislamia.sln -c Debug`
   → **0 erreur** (139 avertissements préexistants) ; `dotnet test Tests/Tests.csproj` → **4008 tests,
   0 échec**, code de sortie 0 (relevé sur `master` `a9f01fb` avant la rédaction de cette fiche).

### 5.7 Point de collision à connaître

`GamePackets.cs:442-452` (le bloc de commentaire de la famille) et `GameFarmPackets.cs:9-19` (le
commentaire d'en-tête) sont **déjà touchés** par la branche `hermes/packet-6002-foster-creature` (MR #84,
en attente de merge) : son diff ajoute `TM_SC_RESULT_FOSTER = 6003` dans le même bloc et réécrit les
mêmes phrases. Le lot `6004` doit se poser **après** le merge de #84 s'il veut éviter la réécriture à la
main ; sinon la résolution est triviale (quelques lignes de commentaire + une ligne d'énumération), mais
elle doit être faite **dans les deux sens** (6003 *et* 6005 déclarés, les trois textes de commentaire
reformulés une seule fois).

### 5.8 Le retour automatique d'une entrée échue, à l'ouverture de la fenêtre (6000)

Le périmètre de ce lot comprend le retour **automatique** de la créature échue. `onRequestFarmInfo`
(`GameMessage.cpp:11726-11764`) parcourt les trois cases, et une entrée dont le ticket est échu
(`pInfo->registration_time + pInfo->duration < time( NULL )`, `:11747`) est reprise **avant** la
réponse — `pClient->RegainSummon( pInfo->item->GetHandle() )` (`:11749`) — puis signalée au joueur par
`PrintfChatMessage( false, CHAT_NOTICE, "@NOTICE", pClient, "@1158" )` (`:11750`) et **omise** de la
liste (`continue`, `:11751`) : ni `creature_count` (`:11760`) ni la taille de la trame (`:11761`) ne la
comptent.

Le geste est **exactement celui de `6004`** — même `RegainSummon`, mêmes contrôles (§5.5 points 1-2),
mêmes plafonds (points 3-6), même EXP — et la seule différence est qu'il n'y a pas de `6005` à émettre :
le joueur apprend la reprise par le message `@1158`. Les deux appels doivent donc partager **la même
couture** ; le lot n'en écrit pas deux.

C'est la dette que le socle a explicitement laissée ouverte, dans le commentaire de
`CreatureFarmService.cs:32-37` : « *the reference retrieves the entries whose ticket has expired
**before** answering (onRequestFarmInfo → RegainSummon, §5.1). That gesture grants farm experience, whose
curve's source of truth is not settled yet (A VERIFIER 6), so an expired entry is served as it is stored
rather than silently emptied of its experience. The deposit and retrieval lots own that.* » — et la
fiche `6002` ne l'a pas prise (aucune mention d'une entrée échue dans
`docs/packet-specs/6002-foster-creature.md`). Décision de cette fiche : **le lot `6004` la prend**,
puisque c'est la même fonction et que le plafond d'EXP est désormais tranché. Conséquence pratique :
`ICreatureFarmService.SendFarmInfoAsync` (`CreatureFarmService.cs:54`) appelle la couture de reprise sur
chaque entrée échue **avant** de construire la liste des `SUMMON_INFO` (`CreatureFarmService.cs:54-89`).

Le message `@1158` est le seul point du lot qui n'a **pas** de primitive identifiée dans le dépôt : la
référence émet un jeton `@1158` (résolu par la table de chaînes du client) sur le canal `CHAT_NOTICE` et
la catégorie `@NOTICE`. Le dépôt a bien le type de canal (`ChatType.cs:22`) et un chemin d'annonce
(`GmCommands/GmCommandService.cs:518`), mais aucun chemin de jeton `@<id>` : le lot émet le jeton tel
quel sur le canal notice (c'est le client qui le résout) et le signale en réserve (§A VERIFIER 8), faute
de pouvoir tester un client ici.

---

## 6. Écarts assumés

### 6.1 Avec NGemity

NGemity porte les **deux** en-têtes du paquet — `shared/Server/Packets/GameClient/TS_CS_RETRIEVE_CREATURE.h`
(id `6004`, `uint32_t creature_card_handle`, `// Since EPIC_7_3`) et `TS_SC_RESULT_RETRIEVE.h`
(id `6005`, `int8_t result`) — et il les déclare aussi dans `shared/Server/ClientPackets.h:283-284`.
Il ne porte **aucune logique** : `grep -rn 'Farm' reference/ngemity/Chihiro/src` rend **0** occurrence,
et aucun fichier de `Chihiro/src` ne mentionne `RetrieveCreature` ni `RegainSummon`. Il n'y a donc **rien
à porter de NGemity** pour ce paquet, et aucune divergence d'implémentation à trancher avec lui : sur la
ferme, NGemity est un relecteur de structures, et la référence de logique est la source officielle
(recoupée sur le binaire 7.3).

**Correction d'une mesure de la fiche socle** : `socle-ferme-creatures-officielle.md` §6.1 écrit que
NGemity n'a que « trois en-têtes de trame » de la famille. Mesuré, `reference/ngemity/shared/Server/Packets/GameClient/`
en porte **neuf** (`TS_CS_REQUEST_FARM_INFO.h`, `TS_SC_FARM_INFO.h`, `TS_CS_FOSTER_CREATURE.h`,
`TS_SC_RESULT_FOSTER.h`, `TS_CS_RETRIEVE_CREATURE.h`, `TS_SC_RESULT_RETRIEVE.h`, `TS_CS_NURSE_CREATURE.h`,
`TS_SC_RESULT_NURSE.h`, `TS_CS_REQUEST_FARM_MARKET.h`). La conclusion de la socle — rien à porter — est
inchangée ; seule l'énumération était courte.

### 6.2 La source 2015 est Epic 9.x — les écarts mesurés contre l'ère 7.3

| point | 2015 (source) | 2012-11 (mesuré) | décision pour 7.3 |
|---|---|---|---|
| EXP/h normal | `145763` (`GameRule.cpp:140`, cité par la socle §5.3) | **137700** (`0x1404f7e68`) | prendre la valeur 7.3 |
| EXP/h croissance | `1118029` | **347264** (`0x1404f7e6c`) | prendre la valeur 7.3 |
| branche évolution | `case EVOLVE_EVOLVE` + `nFarmEvolveSummonEXP` (`:11389-11395`) | **aucune** : `GetTransformLevel()` rend 1 ou 2, sinon `return false` (`0x1400d605b`) | **ne pas porter** ; un formulaire évolué est refusé (`result = 0`) |
| prime premium | `if( pInfo->is_cash )` dans les trois branches (`:11376`, `:11383`, `:11390`) | **aucune** : `is_cash` (+0x15) n'est jamais lu dans `RegainSummon` | **ne pas porter** |
| `FARM_MAX_LEVEL` | `150` (`GameRule.h:49`) | **100** (`0x1404096c0`) | **100** (déjà `CreatureFarmRules.MaxLevel`) |
| plafonds de formulaire | `60` / `115` | `60` (`0x1404096b8`) / `115` (`0x1404096bc`) | identiques |
| cracker | `×1.5` (`:11368`) | `1.5` (`0x14040a2c8`) vs `1.0` (`0x140400730`), deux `double` | identiques |
| le reste (heures entières, `nExpLimit`, ordre du geste) | `:11367-11416` | mêmes étapes, mêmes cibles | **porté tel quel** |

Le lot porte donc **la logique de 2015 moins ses deux ajouts postérieurs à 7.3** (évolution et prime
premium), avec les deux constantes d'EXP de l'ère 7.3 — c'est-à-dire ce que `CreatureFarmRules` porte
déjà.

### 6.3 Avec le modèle 7.3 du dépôt

- La **carte reste dans le sac** pendant la ferme (le bit 27 est la marque ; `CreatureFarmRules.IsFarmed`),
  alors que la référence la sort de l'inventaire (`SetOwnerInfo(0,0,0)`) et la re-pousse à la reprise
  (`PushItem`). Conséquence pour ce lot : **rien à re-pousser**, et **pas de test d'inventaire** à
  l'arrivée (§5.5 point 8) — le dépôt refuse par « la carte n'est pas dans le sac » là où la référence
  refuse par « l'objet n'existe pas » (`FindItem`, `0x1400ad020`). C'est un écart de la socle, pas de ce
  paquet, et il ne change ni la trame ni l'octet de réponse.
- La **ligne de ferme du dépôt** (`CreatureFarms` via `FarmedSummon`) porte tout ce que le calcul
  consomme : `MaxLevel`, `IsUsingCracker`, `RegistrationTime`, `Duration` (`CreatureFarmStore.cs:19-21`),
  et `Experience` de l'invocation. Aucune colonne n'est à ajouter.
- La **courbe d'EXP** vient du catalogue (`ICreatureCatalog.NeedExp`) et non d'un fichier serveur
  (`GetNeedSummonExp`) : le calcul est le même, la source de la donnée reste la question ouverte §7.1.

### 6.4 Micro-écart de précision, sans conséquence

La référence calcule `nExp = tHour × EXP/h × dRate` en **simple précision** (`cvtsi2ss` → `mulss` →
`cvttss2si`, `0x1400d60aa-0x1400d60b3` et `0x1400d606d-0x1400d6076`) avant de tronquer, alors que le
dépôt la calcule en `double` (`CreatureFarmRules.GainedExp`, `:157-158`). L'écart ne peut apparaître que
sur de très gros produits (au-delà de `2^24`) et reste borné à quelques unités d'EXP. Le calcul du dépôt
est conservé tel quel : il est testé, et le gain réel dépend de la courbe, dont la source de vérité est
ouverte (§7.1).

---

## 7. `NON ÉTABLI`

1. **La source de vérité de la courbe** (`GetNeedSummonExp` de la référence ↔ `ICreatureCatalog.NeedExp`
   du dépôt). Le lot applique la courbe **telle que le dépôt la charge** : `nExpLimit = NeedExp(nLevelLimit) − 1`,
   puis `min`. Ce qui reste ouvert est **d'où viennent ses nombres** (`CreatureCatalogOptions.SummonExp`,
   `client73/db_exp.rdb`, la colonne `normal_exp` de `SummonResource`) et comment elle est indexée —
   c'est le point `A VERIFIER 6` de la fiche socle, que ce lot **ne tranche pas** et **ne bloque pas** :
   le comportement (gagner de l'EXP de ferme, au plus au dernier niveau autorisé) est indépendant de la
   provenance des chiffres. *Question : quelle entrée de configuration est la courbe de référence ?*
2. **Le nom du quatrième argument de `AddExp`.** La source 2015 écrit `pSummon->AddExp( nExp, 0, false, true )`
   (`:11407`) et le binaire 7.3 passe bien un `1` dans le cinquième emplacement d'arguments
   (`movb $0x1,0x20(%rsp)` en `0x1400d6139`), mais l'extrait `official2015` **ne contient pas** le
   `StructSummon` (aucun fichier `*summon*` dans `Game/`, et `StructPlayer.h:142` ne donne que le
   `StructCreature::AddExp( exp, jp, bApplyStamina )` à trois paramètres). La correspondance avec le
   `force` de `SummonProgression.CapGain` est **sémantique**, pas déclarative : elle repose sur l'effet
   mesuré (le gain de ferme n'est pas plafonné par `level² × 200`, seulement par la courbe).
   *Question : le paramètre s'appelle-t-il « forcer » et couvre-t-il exactement notre `force` ?*
3. **Ce que le client affiche selon la valeur de `result`.** Le handler `6005` ne teste jamais l'octet :
   il le recopie dans l'événement interne d'id `0xb0` (`0x6722e0-0x672335`) dont la vtable `0xa52160`
   n'est référencée qu'à cet endroit du binaire (aucun consommateur identifié). Quel libellé la fenêtre
   en tire (une des chaînes `msgbox_creatureFarmRegain` ?) n'est pas établi — **sans conséquence** : le
   sens réseau de l'octet est établi (`GameMessage.cpp:11916`, fiche socle §3.4).
4. **Le sort d'une carte au formulaire évolué.** Le refus mesuré (§5.5 point 2) est un comportement de
   l'ère 7.3, pas une règle écrite : rien ne dit si le retail *voulait* interdire la reprise d'une
   invocation évoluée ou si c'est une branche simplement non écrite. Le défaut retenu est le
   comportement mesuré (refus, `result = 0`, carte laissée en ferme). *Question : faut-il laisser
   refuser, ou servir la croissance au taux de la branche 2 (347264) ?*
5. **La carte « dans le sac » ou hors du sac.** Le dépôt garde la carte en sac (§6.3) : le lot
   n'applique donc aucun test d'inventaire à la reprise. Si la revue de la socle décidait un jour de
   sortir la carte du sac (`StorageId`, un emplacement dédié), ce lot devrait ajouter le contrôle —
   *question laissée à Killian* (§A VERIFIER 4).

Les points **repris sans re-mesure** de la fiche socle : les constantes 7.3 (`FARM_MAX_LEVEL = 100`, 137700 /
347264, cracker ×1.5, 60 / 115), le `max_level` gelé au dépôt, la règle des cases (0 ordinaire, 1-2
premium) et la valeur de `result` (`0` refus, `1` reprise) — `socle-ferme-creatures-officielle.md`
§3.4, §5.2, §5.3, §6.2, déjà en place dans `CreatureFarmRules` et testée.

---

## A VERIFIER PAR KILLIAN

| # | question | ce que la fiche propose (défaut appliqué si rien n'est tranché) |
|---|---|---|
| 1 | La **reprise verse-t-elle l'EXP de ferme** dans ce lot, ou dans un lot ultérieur ? | **Elle la verse** (§5.5). C'est le geste de la référence (une seule fonction), la fiche socle §5.3 dit le calcul applicable tel quel, et le commentaire de `ICreatureFarmStore.RemoveAsync` (`CreatureFarmStore.cs:58-63`) réserve explicitement l'appel « une fois que le lot de la reprise peut la verser ». Un lot qui se contenterait de rendre la carte ferait perdre l'EXP de ferme au joueur. |
| 2 | Le plafond `NeedExp(nLevelLimit) − 1` sur le gain | **Appliqué avant `GainExperience`** (§5.5 point 5). Sans lui, l'invocation dépasse le `max_level` gelé au dépôt (`AddExp` ne connaît que le plafond du formulaire). |
| 3 | Le formulaire évolué (3) à la reprise | **Refusé, `result = 0`** (§5.2 point 11, §7 point 4) : c'est le comportement mesuré de l'ère 7.3, et la seule autre option (servir la croissance) serait une supposition. |
| 4 | `result = 1` sans un point d'EXP versé (0 heure entière, ou plafond atteint) | **Oui, `1`** (§5.2) : la référence répond « la carte est reprise », pas « l'EXP a bougé ». Aucune troisième valeur n'existe dans la source ni dans le binaire. |
| 5 | Le test `FarmFrames_NeverThrowAndOnlyTheFarmInfoRequestIsAnswered` (`FarmPacketsTests.cs:617-635`) | **Mis à jour** : le cas 6004 attend désormais une réponse de 8 octets, id 6005 (§5.6). Le compte de tests (4008 mesurés) ne doit pas baisser. |
| 6 | L'ordre avec la MR #84 (« dépôt 6002 »), même bloc de commentaires et même énumération | **Après #84** si possible (§5.7) ; sinon conflit de quelques lignes à résoudre en gardant 6003 **et** 6005. |
| 7 | La source de vérité de la courbe (`A VERIFIER 6` de la socle) | **Non tranchée ici** (§7.1) : le lot applique `ICreatureCatalog.NeedExp` telle qu'elle est chargée aujourd'hui. |
| 8 | Le **retour automatique** d'une entrée échue à l'ouverture de la fenêtre (6000), dans ce lot ou dans un autre | **Dans ce lot** (§5.8) : c'est le même `RegainSummon`, la dette est nommée par `CreatureFarmService.cs:32-37` (« the deposit and retrieval lots own that »), la fiche `6002` ne l'a pas prise et le périmètre de la carte le cite. Le jeton d'avis `@1158` est émis tel quel sur `CHAT_NOTICE` ; c'est le seul point sans primitive déjà écrite. |

---

## 8. Commits et fichiers épinglés

| référence | empreinte |
|---|---|
| Navislamia, base du lot | `master` = `a9f01fb` (« fix(creatures): an out-of-reach summon tells its master TOO_FAR ») ; branche de la fiche `hermes/packet-6004-retrieve-creature` |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` — `librzu/src/packets/GameClient/TS_CS_RETRIEVE_CREATURE.h`, `TS_SC_RESULT_RETRIEVE.h`, `src/lib/Packet/GameTypes.h` |
| NGemity | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` — `shared/Server/Packets/GameClient/TS_CS_RETRIEVE_CREATURE.h`, `TS_SC_RESULT_RETRIEVE.h`, `shared/Server/ClientPackets.h`, `shared/Server/XPacket.h`, `Chihiro/src` (0 occurrence de `Farm`) |
| source officielle `GameMessage.cpp` | `sha256 e7f5399ad9f2ab1e1ac0af49dcf6cc24774ecc1f4c2e80eee91da0130dbe5ac2` — handler `:11910-11919`, dispatch `:12308` |
| source officielle `GameMessage.h` | `sha256 183e131508428a0cfa23513e41d80a6f28536aa584cf5803c88f9bedaf85e484` — `TS_CS_RETRIEVE_CREATURE` `:3924-3929`, `TS_SC_RESULT_RETRIEVE` `:3932-3937` |
| source officielle `StructPlayer.cpp` | `sha256 40c1fbcfa032d0003177a81762757d1c093f0a1c2a84f66cbd03a5c3dbc76046` — `RegainSummon` `:11332-11421` |
| source officielle `StructPlayer.h` | `sha256 b3e7d115c1ab8ce99ab84447db824dcc94fabc7646d0f82f333ffebdac55a78b` — `StructCreature::AddExp` `:142` |
| serveur ère 7.3 (2012-11) | `CaptainHerlockServer.exe` `sha256 83b54fe18f15601578e35bcad1832a5c254bb09f512ab6297217fa49d9dd3eac` + `CaptainHerlockServer.symbols.tsv` `sha256 aef7e6bc8d436a3641fa5a589b53c557ae940e6b6ed4b555a648f8aab7c7e7f3` — `onRetrieveCreature` `0x14011e750`, `RegainSummon` `0x1400d5e60`, `GetNeedSummonExp` `0x140188970`, `GetCreatureFarmTicketCount` `0x14018bb10` |
| client 7.3 | `SFrame.exe` `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` — constructeur 6004 `0x610530`, émetteur `0x6108c0`, site d'appel `0x614998`, handler 6005 `0x6722e0`, table de commutation `0x67f68c` |

Fichiers du dépôt lus pour cette fiche : `op_codes.md:260-261` ; `Game/Network/Packets/Enums/GamePackets.cs:442-452` ;
`Game/Network/Packets/Game/GameFarmPackets.cs` (`:9-19`, `:27-88`, `:145-196`, `:270`, `:331`) ;
`Game/Network/Clients/GameClient.cs:1129-1150,3706-3741` ; `Game/Services/Creatures/CreatureFarmStore.cs:18-64` ;
`Game/Services/Creatures/CreatureFarmRules.cs:26-172` ; `Game/Services/Creatures/CreatureService.cs:22-129,553,973-1009,1076` ;
`Game/Services/Creatures/CreatureCatalog.cs:13-15,20-42,80` ; `Game/Services/Creatures/SummonProgression.cs:174-206` ;
`Game/Services/ICharacterService.cs:129,272,340-343` ; `Tests/Game/FarmPacketsTests.cs:524-635` ;
`docs/packet-specs/socle-ferme-creatures.md` ; `docs/packet-specs/socle-ferme-creatures-officielle.md`.

---

## Bloc pour CLAUDE.md

Bloc à recopier dans la description de la MR du lot (le dev n'écrit **pas** `CLAUDE.md`, le fichier est
protégé) :

```markdown
### Ferme de créatures — la reprise `6004` (`TM_CS_RETRIEVE_CREATURE`)

- Format : en-tête 7 octets, puis `creature_card_handle` (`uint32`) en `+7` : **11 octets**, aucune
  autre forme (le client ne sait en construire qu'une, `SFrame.exe` `0x610530` + `0x6108c0`, et le
  serveur 7.3 lit la poignée en `0x14011e7f6`). Réponse **`TM_SC_RESULT_RETRIEVE` (6005), 8 octets**,
  `result` en `+7` : **1 = carte reprise, 0 = refus** (`GameMessage.cpp:11916`) — la référence répond
  **toujours** à une trame bien formée, et `1` ne veut **pas** dire « de l'EXP a été gagnée » (une
  reprise à 0 heure entière ou au plafond rend `1` sans rien verser). Le client ne teste pas l'octet :
  il le recopie dans son événement interne `0xb0` (`0x6722e0`).
- Version : `X(6004, true)` et `X(6005, true)` sous `// Since EPIC_7_3`, **aucun** `version >=` ni
  `_(pad)` dans les deux en-têtes rzu, `ar_handle_t` = 4 octets : rien à retirer ni décaler pour 7.3.
  `TM_SC_RESULT_RETRIEVE` (6005) est **déclaré** par ce lot, avec son bras de dispatch (journal +
  `continue` sur une trame entrante, anomalie de protocole) : tout membre de `GamePackets` doit être
  routé avant le `switch` qui lève.
- Geste (`RegainSummon`, `StructPlayer.cpp:11332-11421`, recoupé sur le binaire 7.3 `0x1400d5e60`) :
  contrôles **avant toute écriture** (carte trouvée, bit 27 `ITEM_FLAG_FARMED_SUMMON` posé, une ligne
  de ferme nomme la carte, la carte porte une invocation), puis EXP, puis sortie de ferme. Tous les
  refus rendent `0` sans rien modifier ; les primitives du dépôt existent déjà
  (`ICreatureFarmStore.LoadAsync`/`RemoveAsync`, `CreatureFarmRules.FarmedHours`/`GainedExp`/`LevelLimit`,
  `CreatureService.GainExperience(..., force: true)`, `ICreatureCatalog.NeedExp`).
- Deux pièges de plafond. (1) Le gain est plafonné par `NeedExp(nLevelLimit) − 1` avec
  `nLevelLimit = min(max_level de la ligne, 100, plafond du formulaire)` : **`AddExp` ne connaît que le
  plafond du formulaire** (60/115), donc sans ce `min` explicite l'invocation dépasse le `max_level`
  gelé au dépôt. (2) `GainExperience` doit être appelé avec `force: true` — le gain de ferme n'est pas
  plafonné par `level² × 200` (`SummonProgression.ExpLimit`), seulement par la courbe.
- Formulaires servis en 7.3 : **1 (normal, 137700 EXP/h) et 2 (croissance, 347264 EXP/h) uniquement** ;
  un formulaire évolué fait rendre `false` à `RegainSummon` (`0x1400d605b`) — refus, `result = 0`.
  Aucune prime premium en 7.3 : `is_cash` n'est lu nulle part dans `RegainSummon` (relevé exhaustif des
  accès à la structure : `+0x08`, `+0x10`, `+0x14`, `+0x18`, `+0x20`, `+0x00`). Cracker : ×1.5.
- Le modèle du dépôt garde la carte **dans le sac** pendant la ferme (le bit 27 est la marque) : à la
  reprise il n'y a rien à re-pousser, contrairement au `SetOwnerInfo`/`PushItem` de la référence, et
  aucun test d'inventaire n'est appliqué.
- Ce lot porte aussi le **retour automatique** : à l'ouverture de la fenêtre, `onRequestFarmInfo`
  (`GameMessage.cpp:11747-11751`) reprend **avant** de répondre toute entrée dont
  `registration_time + duration` est passée, l'omet de la liste et avise le joueur du jeton `@1158`
  (`CHAT_NOTICE`). Même `RegainSummon`, donc **une seule couture** pour les deux appelants ; c'est la
  dette nommée par `CreatureFarmService.cs:32-37` (le socle la laisse au dépôt et à la reprise).
- Savoir complet et questions ouvertes : `docs/packet-specs/6004-retrieve-creature.md`.
```
