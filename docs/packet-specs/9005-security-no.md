# 9005 — `TM_CS_SECURITY_NO`

Fiche d'archéologie de protocole, Epic 7.3. Écrite en lecture seule sur les références
(`reference/rzu`, `reference/ngemity`, `reference/client73`) — aucun Lua, aucun script client et aucun
exécutable client n'a été lancé. Le client 7.3 tranche la forme et l'émission ; rzu tranche les tailles et
le gating ; NGemity n'apporte ici **aucune logique** (§6).

Convention de citation du client : les relevés sur `SFrame.exe` sont donnés en **VR** (adresse virtuelle),
avec l'offset fichier quand il a servi. Conversion : `VR = VMA + (offset_fichier − offset_section)`, tables
de sections en fin de §3. Les relevés se revérifient par `objdump -d SFrame.exe --start-address=… --stop-address=…`.

Résumé des arbitrages demandés par la carte :

| Question | Verdict de cette fiche |
|---|---|
| Le paquet est-il client → serveur ? | **Oui, établi** (§1, §2) : le client 7.3 possède un constructeur de trame 9005 et deux appelants qui la remplissent puis l'envoient. L'`Any` de rzu est une anomalie de déclaration, pas une direction. |
| Taille sur le fil | **30 octets** (§3), soit 7 d'en-tête + 4 (`mode`) + 19 (`security_no`, dont un terminateur). Confirmé deux fois : en-tête rzu + somme des champs, et le constructeur du client qui écrit `0x1e` dans le champ longueur. |
| Gating 7.3 | **Tranché** (§4) : id **9005** (8105 à partir d'`EPIC_9_6_3`), charge `mode` + `security_no(19)` ; les champs `account(64)`, `result`, `security_no_1/_2` sont **exclus** (ajoutés à `EPIC_9_6_7`). |
| Ce que le serveur doit répondre | **NON ÉTABLI** (§5.4, §7a) : aucune référence n'implémente la réponse. La vérification du code est, dans l'architecture de référence, une requête du serveur de jeu au **serveur d'authentification** (40001 → 40000) ; Navislamia ne possède ni ce transport ni le moindre stockage du code (§5.3). Les véhicules de réponse candidats sont listés **comme hypothèses** pour l'arbitrage de Killian. |

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id client → serveur | **9005** | `op_codes.md:271` |
| Nom | `TM_CS_SECURITY_NO` | `op_codes.md:271` |
| En-tête rzu | `TS_SECURITY_NO`, `SessionType::GameClient` | `reference/rzu/librzu/src/packets/GameClient/TS_SECURITY_NO.h:17` |
| Origine déclarée par rzu | `SessionPacketOrigin::Any` (**anomalie**, cf. §6) | `TS_SECURITY_NO.h:17` |
| Ids alternatifs | **`8105`** à partir d'`EPIC_9_6_3` | `TS_SECURITY_NO.h:13-15` |
| Référence NGemity | `TS_CS_SECURITY_NO`, 9005, charge `mode` + `security_no(19)` | `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_SECURITY_NO.h:6-10` |
| Nom dans l'énumération NGemity | `TS_CS_SECURITY_NO = 9005` | `reference/ngemity/shared/Server/ClientPackets.h:295` |
| Nom dans le client 7.3 | entrée `9005 → TM_CS_SECURITY_NO` de la table id→nom | `push` VR `0x678fbc` → chaîne VR `0xa52ec8` ; `mov $0x232d,%eax` VR `0x678fde` |
| État dans Navislamia | **absent** : aucun membre 9000-9012 dans `GamePackets`, et `grep -rn "SECURITY" --include=*.cs` → **0** occurrence. Le plus grand id de l'énumération est `TM_NONE = 9999` | `Game/Network/Packets/Enums/GamePackets.cs:101` |

Le paquet est **le cousin fonctionnel** de `TM_CS_CREATE_SECURITY_NO` (9006), `TM_CS_CHANGE_SECURITY_NO` (9007)
et `TM_CS_CLEAR_SECURITY_NO` (9012) : même famille de « code de sécurité », mêmes dialogues client. La carte
ne porte que 9005 ; la famille est inventoriée en §3.4 parce que le dev a besoin de la borne haute de la
séquence pour ne pas se tromper de case dans un `switch`.

## 2. Ce que le joueur fait pour que le client l'envoie

Le code de sécurité (`security password` dans les textes du client) est un **second mot de passe** demandé
au joueur pour deux opérations seulement, et c'est écrit dans les chaînes du client :

| Texte | Clé `db_string.rdb` | Octet |
|---|---|---|
| « A security password is needed when deleting characters and accessing your warehouse. If you don't want a security password, hit Cancel. » | `ui_text_6564` | 6531322 |
| « Please enter your 6-digit security password: » | `ui_text_6559` | 6530865 |
| « Create / Modify security password » | `ui_text_6558` | 6530776 |
| « Security password is used for character deletion. » | `ui_text_lobby_1306` | 13055214 |
| « A security password is required to use the warehouse. » | `ui_text_lobby_1398` | 13062880 |

Le client embarque six fenêtres dédiées (noms RTTI présents dans `SFrame.exe`), ce qui borne la
description de la famille sans avoir à deviner :

| Classe | VR du nom RTTI |
|---|---|
| `SUISecurityWnd` | `0xc19b34` |
| `SUISecuritySettingWnd` | `0xc19b54` |
| `SUISecurityClearWnd` | `0xc19b78` |
| `SUISecuritySettingModifyWnd` | `0xc19b9c` |
| `SUISecurityCharacterWnd` | `0xc19bc8` |
| `SUISecurityStorageWnd` | `0xc19bf0` |

L'enchaînement est le suivant.

1. **Le serveur demande.** `TM_SC_REQUEST_SECURITY_NO` (9004), charge utile `int32 mode`
   (`TS_SC_REQUEST_SECURITY_NO.h:5-10`), est un paquet serveur → client
   (`SessionPacketOrigin::Server`) que le client 7.3 **connaît et nomme** : entrée `9004` de la table
   id→nom, `push $0xa52edc` VR `0x678f5a` avec `mov $0x232c,%eax` VR `0x678f7c`. Le client possède la
   classe de message interne correspondante : `.?AUSMSG_REQUEST_SECURITY_NO@@` (VR `0xc1e110`).
2. **Le joueur saisit.** Selon le texte du client, la saisie attendue fait **6 chiffres**
   (`ui_text_6559`), mais rien dans rzu ni NGemity ne contraint la longueur sur le fil (§3.2).
3. **Le client émet 9005.** Preuve directe, hors table de noms : un constructeur de trame **9005**
   existe dans `.text` à la VR **`0x48cf70`**, et il est appelé depuis deux emplacements seulement,
   `0x6658a1` et `0x49dd9e` (recherche des `E8` de portée 32 bits visant `0x48cf70`). Les deux appelants
   copient `mode` (mot de 32 bits lu à l'offset `+0x13` de l'objet de message) vers l'offset **7** de la
   trame, puis `memcpy(dst = trame + 11, src = message + 0x17, 0x12)` — **18 octets** —, puis remettent la
   trame à un appel virtuel d'émission (`call *0x1cc(%edx)`, `%ecx` = objet de session). Séquence complète
   à la VR `0x6658a1` :

   ```
   6658a1: e8 ca 76 e2 ff    call   0x48cf70        ; constructeur : longueur 30, id 9005, charge remise à 0
   6658a6: 8b 4f 13          mov    0x13(%edi),%ecx ; mode (int32) du message interne
   6658a9: 6a 12             push   $0x12           ; 18 octets
   6658ab: 8d 57 17          lea    0x17(%edi),%edx ; chaîne du message interne
   6658ae: 52                push   %edx
   6658af: 8d 85 4b ff ff ff lea    -0xb5(%ebp),%eax; trame + 11
   6658b5: 50                push   %eax
   6658b6: 89 8d 47 ff ff ff mov    %ecx,-0xb9(%ebp); trame + 7  ← mode
   6658bc: e8 0f 40 31 00    call   0x9798d0        ; memcpy(trame+11, message+0x17, 18)
   6658c1..6658d5           … push trame ; call *0x1cc(%edx)  ← émission
   ```

4. Le client conserve aussi, pour chaque paquet de la famille, une **chaîne de trace** (format de journal) ;
   celle de 9005 est à la VR `0xa52318` : `TM_CS_SECURITY_NO\t\t   <libellé coréen 요청결과>-%s[%d]\n`,
   avec un argument chaîne et un argument entier. C'est un indice compatible avec la charge
   `(mode, security_no)` ; **ce n'est pas une preuve** de l'ordre des champs (§7d).

**Ce que le joueur fait donc, en une phrase** : il ouvre son coffre (Storage) ou supprime un personnage,
le serveur lui demande le code par 9004, il tape ses 6 chiffres, et le client envoie 9005 avec le `mode`
reçu et le code saisi. L'ouverture du coffre et la suppression de personnage sont exactement les deux
opérations que le nom du champ dans la référence associe aux modes 1 et 2 (§4.3).

## 3. Structure sur le fil

### 3.1 `TM_CS_SECURITY_NO` — client → serveur — **30 octets**

| Offset | Taille | Type | Nom | Valeur / contrainte observée | Source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` | **30** (`0x1e`) | client : `movl $0x1e,(%eax)` VR `0x48cfb0` ; rzu : `TS_MESSAGE::size` (`PacketBaseMessage.h:18`) |
| 4 | 2 | `uint16` | `ID` | **9005** (`0x232d`) | client : `mov $0x232d,%ecx` / `mov %cx,0x4(%eax)` VR `0x48cfa5`-`0x48cfaa` ; rzu : `TS_SECURITY_NO_ID` `9005` (`TS_SECURITY_NO.h:14`) |
| 6 | 1 | `int8` | `checksum` | somme des octets **0 à 5**, modulo 256 | client : boucle VR `0x48cfb6`-`0x48cfc7`, écriture `mov %cl,(%edx)` VR `0x48cfc7` ; rzu : `TS_MESSAGE::checkMessage` (`PacketBaseMessage.h:30-41`) ; Navislamia valide déjà ce champ (`Game/Network/Clients/GameClient.cs:562`) |
| 7 | 4 | `int32` | `mode` | non gâté ; voir §4.3 pour les valeurs nommées | rzu : `_(simple)(int32_t, mode)` (`TS_SECURITY_NO.h:7`) ; client : copie depuis le message, VR `0x6658b6` |
| 11 | 19 | chaîne fixe | `security_no` | 18 caractères utiles au plus, le reste à zéro ; le **19ᵉ octet est un zéro** dans la trame du client | rzu : `_(string)(security_no, 19, version < EPIC_9_6_7)` (`TS_SECURITY_NO.h:8`) ; client : `memcpy` de 18 octets vers la trame + 11 (VR `0x6658bc`) après remise à zéro de toute la charge (VR `0x48cf8f`-`0x48cfa1`) |
| **Total** | **30** | | | `7 + 4 + 19` | les deux sources ci-dessus ; la longueur est aussi **écrite dans la trame** par le client |

### 3.2 La chaîne de 19 octets : conteneur, pas longueur maximale

`_(string)(security_no, 19)` est un **conteneur de taille fixe** :

- à l'écriture, rzu tronque à `19 − 1 = 18` caractères et complète par des zéros
  (`MessageBuffer.cpp:87-95`) ;
- à la lecture, rzu lit **19 octets** puis convertit au plus 18 caractères
  (`MessageBuffer.cpp:104-109`, `Utils::convertToString(p, maxSize - 1)`) ;
- le client 7.3 recopie **18 octets** et laisse le 19ᵉ à zéro (il vient d'être remis à zéro par le
  constructeur).

Conséquence pour le dev : le code se lit **jusqu'au premier zéro** dans la fenêtre de 19 octets ; il ne
faut ni exiger 19 caractères, ni lire les 19 octets comme un tout. Les « 6 chiffres » de `ui_text_6559`
sont une contrainte d'interface : **ni rzu ni NGemity ne la portent** (§6).

### 3.3 Comment la taille a été établie (deux voies indépendantes)

1. **rzu** : `7` (en-tête `TS_MESSAGE` : `uint32 size` + `uint16 id` + `int8 msg_check_sum`,
   `PacketBaseMessage.h:17-20`) `+ 4` (`mode`) `+ 19` (`security_no`) = **30**.
2. **Le client** : le constructeur VR `0x48cf70` écrit `0x7` par défaut (`0x48cf78`), met à zéro les
   octets 4 à 0x1d, écrit l'id `9005` (`0x48cfaa`), puis écrit la longueur **`0x1e`** (`0x48cfb0`) et
   recalcule le checksum. `0x1e = 30`.

Les deux voies concordent : **30 octets**, charge utile **23**.

### 3.4 Carte des sections de `SFrame.exe` (pour toute conversion VR ↔ offset)

`.text` VMA `0x00401000` / offset `0x00000400` / taille `0x0060a8cf` ; `.rodata` `0x00a0c000` / `0x0060ae00` /
`0x00002b40` ; `.rdata` `0x00a0f000` / `0x0060da00` / `0x002006a8` ; `.data` `0x00c10000` / `0x0080e200` /
`0x00038600`.

### 3.5 La famille autour de 9005 (telle que le client 7.3 la nomme)

Table id→nom du client, chaînes contiguës dans `.rdata` — utile parce qu'elle donne **les noms et les ids
du client lui-même**, indépendamment de `op_codes.md`. Méthode reproductible : décompiler la zone
`.text` `0x678c00`-`0x679400` (`objdump -d SFrame.exe --start-address=0x678c00 --stop-address=0x679400`) et
apparier chaque `push $<VR .rdata>` avec le `mov $<imm>,%eax` qui suit — les instructions étant cette fois
**alignées**, il n'y a pas d'ambiguïté (contrairement à une recherche d'octets, cf. §9) :

| Id | Nom (client) | `push` VR | Chaîne VR |
|---|---|---|---|
| 9002 | `TM_SC_CREATE_SECURITY_NO` | `0x678ef8` | `0xa52ef8` |
| 9004 | `TM_SC_REQUEST_SECURITY_NO` | `0x678f5a` | `0xa52edc` |
| **9005** | **`TM_CS_SECURITY_NO`** | **`0x678fbc`** | **`0xa52ec8`** |
| 9006 | `TM_CS_CREATE_SECURITY_NO` | `0x67901e` | `0xa52eac` |
| 9007 | `TM_CS_CHANGE_SECURITY_NO` | `0x679080` | `0xa52e90` |
| 9008 | `TM_CS_REQUEST_SECURITY_NO_CHANGE` | `0x6790e2` | `0xa52e6c` |
| 9009 | `TM_SC_CHANGE_SECURITY_NO` | `0x679144` | `0xa52e50` |
| 9010 | `TM_CS_REQUEST_CLEAR_SECURITY_NO` | `0x6791a6` | `0xa52e30` |
| 9011 | `TM_SC_CLEAR_SECURITY_NO` | `0x679208` | `0xa52e18` |
| 9012 | `TM_CS_CLEAR_SECURITY_NO` | `0x67926a` | `0xa52e00` |

Deux contrôles de cohérence de cette table, qui montrent qu'elle est bien la **carte 7.3** :

- l'entrée précédente est `3004 → TM_SC_GENERAL_MESSAGE_BOX` (`push` VR `0x678e34`) et la suivante
  `10000 → TM_CS_OPEN_ITEM_SHOP` (`0x6792cc`), ce dernier concordant avec `op_codes.md:273` ;
- elle **ne contient pas 9003** (elle passe de 9002 à 9004) : rzu y place `TS_SC_COMMERCIAL_STORAGE_INFO`
  mais seulement `>= EPIC_9_6_3` (voir le piège de §4.1).

Charges utiles correspondantes dans rzu, pour 7.3 : `9002` vide, `9004` `int32`, **`9005` `int32` + 19**,
`9006` deux `array(uint8_t, 19)`, `9007` deux `array(uint8_t, 19)`, `9008` vide, `9009` vide, `9010` vide,
`9011` `bool`, `9012` deux `array(uint8_t, 19)` (`TS_CS_CREATE_SECURITY_NO.h:6-8`,
`TS_CS_CHANGE_SECURITY_NO.h:6-8`, `TS_CS_CLEAR_SECURITY_NO.h:6-8`, `TS_SC_CLEAR_SECURITY_NO.h:7`).

`op_codes.md` ne porte que 9004 et 9005 (lignes 270-271) : les autres ids de la famille sont hors de la
présente fiche, mais le dev doit savoir qu'ils sont **connus du client**, donc qu'un client peut en envoyer.

## 4. Gating de version

Les paliers cités : `EPIC_7_3 = 0x070300`, `EPIC_9_6_3 = 0x090603`, `EPIC_9_6_7 = 0x090607`
(`reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59`, `:96`, `:104`). Le 7.3 du dépôt est **antérieur aux
deux derniers** : le gating se tranche sans ambiguïté.

### 4.1 Id : 9005 (et non 8105)

```cpp
#define TS_SECURITY_NO_ID(X) \
        X(9005, version < EPIC_9_6_3) \
        X(8105, version >= EPIC_9_6_3)
```

`TS_SECURITY_NO.h:13-15`. **Décision pour 7.3 : `9005`.** Contrôle côté client : la séquence `a9 1f 00 00`
(`8105` en immédiat 32 bits) est **absente** de `.text` (0 occurrence), et l'id 8105 n'apparaît pas non plus
sous forme d'immédiat 16 bits dans un `movw`/`cmp` vers un champ d'objet. Le client ne connaît que `9005`.

**Piège majeur — le même id `9005` désigne un autre paquet à partir d'`EPIC_9_6_3`.** rzu ne renomme pas
seulement la famille : il **réutilise** l'espace 9000-9012 pour le coffre commercial et l'hôtel des ventes,
la famille sécurité passant en 8102-8112. Relevé sur les en-têtes rzu :

| Id | 7.3 (`< EPIC_9_6_3`) | `>= EPIC_9_6_3` |
|---|---|---|
| 9000 | `TS_SC_OPEN_URL` (`TS_SC_OPEN_URL.h:13`) | `TS_CS_OPEN_ITEM_SHOP` (`TS_CS_OPEN_ITEM_SHOP.h:9`) |
| 9001 | `TS_SC_URL_LIST` (`TS_SC_URL_LIST.h:16`) | `TS_SC_OPEN_ITEM_SHOP` (`TS_SC_OPEN_ITEM_SHOP.h:15`) |
| 9002 | `TS_SC_CREATE_SECURITY_NO` (`TS_SC_CREATE_SECURITY_NO.h:8`) | `TS_SC_OPEN_PAID_STORAGE` (`TS_SC_OPEN_PAID_STORAGE.h:9`) |
| 9003 | *(rien)* | `TS_SC_COMMERCIAL_STORAGE_INFO` (`TS_SC_COMMERCIAL_STORAGE_INFO.h:13`) |
| 9004 | `TS_SC_REQUEST_SECURITY_NO` (`TS_SC_REQUEST_SECURITY_NO.h:9`) | `TS_SC_COMMERCIAL_STORAGE_LIST` (`TS_SC_COMMERCIAL_STORAGE_LIST.h:21`) |
| **9005** | **`TS_SECURITY_NO`** (`TS_SECURITY_NO.h:14`) | **`TS_CS_TAKEOUT_COMMERCIAL_ITEM`** (`TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:13`) |

Conséquence pratique : un relevé « 9005 » pris dans un contexte 9.6.3+ décrit le **retrait d'un objet du
coffre commercial**, pas le code de sécurité. En 7.3 c'est bien la famille sécurité — le client 7.3 nomme
9005 `TM_CS_SECURITY_NO` et place la famille commerciale en 10000-10005 (§3.5, `op_codes.md:273-277`).

### 4.2 Champs : `mode` + `security_no(19)`, rien d'autre

```cpp
#define TS_SECURITY_NO_DEF(_) \
        _(string)(account, 64, version >= EPIC_9_6_7) \
        _(simple)(int32_t, mode) \
        _(string)(security_no, 19, version < EPIC_9_6_7) \
        _(simple)(int32_t, result, version >= EPIC_9_6_7) \
        _(simple)(int32_t, security_no_1, version >= EPIC_9_6_7) \
        _(simple)(int32_t, security_no_2, version >= EPIC_9_6_7)
```

`TS_SECURITY_NO.h:5-11`. **Décisions pour 7.3** : `mode` présent (aucun gating) ; `security_no` sur 19
octets, dans sa forme **chaîne** (`version < EPIC_9_6_7`) ; `account(64)`, `result`, `security_no_1` et
`security_no_2` **exclus** — ils n'existent qu'à partir d'`EPIC_9_6_7`, donc jamais en 7.3. Ne pas recopier
le `account[64]` d'un relevé 9.x : il ferait 94 octets au lieu de 30.

### 4.3 Le domaine de `mode`

rzu nomme trois valeurs, dans le paquet **serveur d'authentification → serveur de jeu** qui répond à la
vérification :

```cpp
enum Mode { SC_NONE = 0x0, SC_OPEN_STORAGE = 0x1, SC_DELETE_CHARACTER = 0x2 };
```

`reference/rzu/librzu/src/packets/AuthGame/TS_GA_SECURITY_NO_CHECK.h:12-17` (et `TS_AG_SECURITY_NO_CHECK` sans
énumération). Ces deux noms recoupent exactement les deux usages attestés par les chaînes du client 7.3
(« accessing your warehouse », « deleting characters », `ui_text_6564`).

Ce qui reste **non établi** : la valeur que le client 7.3 met réellement dans `mode`. Le contrôle de
domaine est donc à **ne pas** écrire dans le serveur (§7b) : une fonction du client (VR `0x62d4c0`) teste un
mot à l'offset `+0x27` contre `1` et **sort immédiatement** pour l'id 9004 lu à `+0x19`, mais la
correspondance `mode = 1 ⇔ coffre` n'est pas démontrable par lecture seule, et le test de rzu lui-même
utilise `mode = 42` (`rzauth/test/AuthServer/GameServerSession/SecurityNo.cpp:30`), c'est-à-dire une valeur
hors énumération.

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` en fait : rien

`grep -rn "SECURITY_NO"` sur `reference/ngemity/Chihiro/src`, sur `reference/ngemity/shared` hors
`Packets/`, et sur `reference/ngemity/Mononoke` (le serveur d'authentification de NGemity) → **0 résultat**.
NGemity ne fournit que les **définitions** de paquets (`Packets/GameClient/TS_CS_SECURITY_NO.h`,
`Packets/AuthGame/TS_GA_SECURITY_NO_CHECK.h`, `TS_AG_SECURITY_NO_CHECK.h`) et les ids
(`ClientPackets.h:295`). **Il n'y a aucune logique serveur à porter depuis NGemity, et donc aucune
référence ne dit ce qu'un serveur fait d'un 9005 reçu.** C'est le fait central de cette fiche.

### 5.2 Ce que rzu atteste de l'architecture — la vérification est côté authentification

rzu n'a pas de handler de jeu non plus (`grep -rn "SECURITY_NO" rzu_game/src` → 0), mais il fournit le
**contrat** que le serveur de jeu devait respecter face au serveur d'authentification :

| Sens | Paquet | Id | Charge |
|---|---|---|---|
| serveur de jeu → auth | `TS_GA_SECURITY_NO_CHECK` | 40001 | `char account[61]`, `char security[19]`, `int32 mode` (mode « depuis e6 ») — `AuthGame/TS_GA_SECURITY_NO_CHECK.h:6-20` ; variante `_EPIC5` sans `mode` (`:22-28`) |
| auth → serveur de jeu | `TS_AG_SECURITY_NO_CHECK` | 40000 | `char account[61]`, `uint32 mode`, `uint32 result` — `AuthGame/TS_AG_SECURITY_NO_CHECK.h:7-14` |

Et la vérification elle-même, côté auth (`rzauth/src/AuthServer/`) :

- requête SQL de mise en correspondance :
  `SELECT account FROM account WHERE account = ? AND password = ?`, avec `EM_OneRow`
  (`DB_SecurityNoCheck.cpp:14-20`) — c'est-à-dire que le code de sécurité est comparé au **champ
  `password` de la table `account` de la base d'authentification** ;
- le paramètre comparé est `md5(salt + code)` écrit en hexadécimal minuscule sur 32 caractères
  (`DB_SecurityNoCheck.cpp:39-73`), le sel venant de la configuration `auth.securityno.salt`, valeur par
  défaut `"2011"` (`DB_SecurityNoCheck.cpp:29`) ;
- le résultat est simplement « exactement une ligne trouvée » :
  `bool ok = query->getResults().size() == 1;` (`GameServerSession.cpp:460`), renvoyé tel quel dans
  `result` avec `mode` recopié (`GameServerSession.cpp:468-483`) ;
- `mode` est **transporté** par l'aller-retour mais **n'entre pas** dans la requête (`DB_SecurityNoCheck.h:13-26`) :
  c'est un discriminant d'opération, pas une donnée de vérification.

### 5.3 Ce que Navislamia possède aujourd'hui — et ce qui manque

| Élément requis par §5.2 | État mesuré dans Navislamia |
|---|---|
| Membre d'énumération pour 9005 | **absent** (`Game/Network/Packets/Enums/GamePackets.cs:1-102`) |
| Paquets 40000 / 40001 avec le serveur d'authentification | **absents** : `AuthPackets` ne porte que 20001-20014 (`Game/Network/Packets/Enums/AuthPackets.cs:5-14`), alors que le client d'auth existe pourtant (`Game/Network/NetworkService.cs:81-99`, `SendMessageToAuth`) |
| Stockage du code de sécurité | **absent** : l'entité de compte ne porte que `Id`, `Username`, `PasswordHash`, `CreatedOn`, `LastServerIdx` (`Game/DataAccess/Entities/Auth/AccountEntity.cs:5-16`) |
| Colonne dans le schéma livré | **absent** : `grep -cniE "security\|password" ArcadiaSchemaPSQL.sql` → **0**. Le fichier (2 298 lignes) ne contient que des tables de **ressources** (`AuctionCateryResource`, `DropGroupResource`, …), aucune table de compte |

Conclusion : **la vérification du code ne peut pas être implémentée fidèlement aujourd'hui**. Il ne manque
pas une ligne de code, il manque une décision (quel stockage, dans quelle base, avec quel hachage) et un
transport (40000/40001). Inventer un stockage local maintenant produirait un serveur qui accepte n'importe
quel code : c'est-à-dire un contrôle décoratif, exactement ce que le dépôt s'interdit.

### 5.4 Ce que le serveur doit répondre — **NON ÉTABLI**

Aucune source ne décrit la réponse. Les véhicules candidats sont listés ici **comme hypothèses à arbitrer**,
avec ce que chacun coûte ; le dev ne doit en choisir aucun sans l'accord de Killian :

| Hypothèse | Source de l'hypothèse | Réserve |
|---|---|---|
| `TM_SC_RESULT` (0) + `ResultCode` (`InvalidPassword = 18`) | mécanisme générique du dépôt : `Game/Network/Packets/ResultCode.cs:26`, `SendResult` (`Game/Network/Clients/GameClient.cs:56`) | aucune source ne relie ce paquet au code de sécurité ; c'est le réflexe du dépôt, pas une preuve |
| `TM_SC_GENERAL_MESSAGE_BOX` (3004) portant un id de `db_string` (p. ex. `ui_text_6566` « Security password is incorrect. ») | le client 7.3 **nomme** 3004 dans sa table id→nom (`push $0xa52f24` VR `0x678e34`, id `mov $0x0bbc,%eax` VR `0x678e56`) | rzu date ce paquet d'`EPIC_7_4` (`TS_SC_GENERAL_MESSAGE_BOX.h:11-13`), donc **hors 7.3 selon rzu** — alors que le client 7.3 le nomme : contradiction à ne pas trancher à la légère |
| Renvoyer un `TM_SC_REQUEST_SECURITY_NO` (9004) | le aller-retour d'invite est attesté (`?AUSMSG_REQUEST_SECURITY_NO@@`, VR `0xc1e110`) | aucune source ne montre une seconde invite après un échec |
| Ne rien répondre | — | c'est le seul choix qui n'invente rien, mais il laisse le joueur sans retour : à arbitrer aussi |

**Arbitrage déjà rendu, à respecter** : la carte `navis-dev` fille de cette fiche
(`t_19ed6fdf`, « Implémentation 9005 — `TM_CS_SECURITY_NO` ») instruit le lot avec une disposition
**volontairement neutre** : « déclarer l'identifiant, lire et borner la trame, la journaliser — sans
vérifier le code de sécurité, sans l'enregistrer, sans réponse et sans sanction ». Les quatre hypothèses
ci-dessus sont donc **hors lot** : elles restent ouvertes pour Killian (§7a) et deviennent des candidats
documentés, pas des choix. Le présent §5.4 ne les rouvre pas.

**Ce que la fiche tranche à la place du dev** : la trame (30 octets, offsets ci-dessus), la direction, le
gating, la lecture bornée, et le fait que le code doit être **lu sans être journalisé** (c'est un secret
d'authentification : ne jamais écrire les 19 octets dans les logs, contrairement à la pratique des autres
paquets).

### 5.5 Pièges spécifiques

1. **Ne pas confondre 9005 et 9004** : la table id→nom du client et `op_codes.md` donnent les deux, et les
   deux sont dans la même zone de `.text` (`0x678f5a` pour 9004, `0x678fbc` pour 9005). Une inversion
   produirait un handler entrant pour un paquet que seul le serveur émet.
2. **Ne pas recopier la déclaration rzu telle quelle** : `SessionPacketOrigin::Any` (`TS_SECURITY_NO.h:17`)
   est une anomalie (le nom rzu lui-même n'a pas le préfixe `CS_`) ; la direction se lit sur le client
   (§2.3) et sur NGemity (`TS_CS_…`), pas sur ce champ.
3. **Ne pas déduire la taille du champ longueur reçu seul** : le contrôle d'intégrité du dépôt
   (`Game/Network/Clients/GameClient.cs:562-578`) valide le checksum, mais un `9005` de 30 octets déclaré
   court doit être rejeté comme anomalie de protocole — le client écrit toujours `0x1e`.
4. **`TM_NONE = 9999` existe dans l'énumération** (`GamePackets.cs:101`) : ajouter 9005 ne déplace rien,
   mais le critère transversal « aucun membre ne peut atteindre le `switch` final » impose un **bras
   explicite** avant le `switch` de `Game/Network/Clients/GameClient.cs:791-803`, faute de quoi un 9005
   valide lèverait `Unknown Packet Type` en `:802`. Attention au **changement d'effet observable** : sur
   `master`, un 9005 est un id **non défini**, donc journalisé en `Debug` puis `continue` en `:584-588` ;
   dès que le membre existe, ce garde-fou ne s'applique plus et le paquet tombe dans la chaîne de `:590`
   puis dans le `switch` final. Le bras est donc obligatoire, pas optionnel. Deux points de dispatch
   coexistent dans ce fichier : la chaîne de `if (header.ID == …)` (dès `:590`) et la table `GameActions`
   (`Game/Network/Clients/Actions/GameActions.cs:43-50`, `_actions.Add(...)`, consultée par
   `_actions.TryGetValue` en `:55`) — vérifier lequel attend le nouveau paquet avant d'écrire.
5. **Le code de sécurité ne se journalise jamais.** Le champ est un secret réutilisable (le même code garde
   la remise de personnage et le coffre). Le bras d'implémentation doit journaliser au plus la **longueur**
   et le **mode**, jamais les octets 11-29 ; les traces de paquets génériques du dépôt doivent être
   écartées pour ce paquet si elles recopient la charge utile.

## 6. Écarts assumés avec NGemity, et pourquoi

| Point | NGemity | Décision de la fiche | Pourquoi |
|---|---|---|---|
| Nom du paquet | `TS_CS_SECURITY_NO` | on garde `TS_CS_…` / `TM_CS_…` | rzu le nomme `TS_SECURITY_NO` **sans** préfixe de direction et le déclare `Any` ; le client 7.3 le nomme `TM_CS_SECURITY_NO` et possède un émetteur (§2). L'autorité de nommage reste `op_codes.md:271`. |
| Champs | `mode` puis `string security_no(19)` (`TS_CS_SECURITY_NO.h:6-8`) | identiques | accord parfait avec rzu 7.3 ; aucune divergence de forme à arbitrer. |
| Handler | **aucun** (Chihiro, Mononoke, shared hors `Packets/`) | rien à porter | NGemity ne tranche rien ici ; le §5.2 (vérification côté auth) vient de rzu seul. |
| `mode` dans le paquet auth | NGemity porte `mode` **inconditionnellement** (`AuthGame/TS_AG_SECURITY_NO_CHECK.h:8`, avec gating `version >= EPIC_6_1`) | pour 7.3, `mode` est présent dans les deux | 7.3 ≥ 6.1 : le gating de NGemity et la variante `_EPIC5` de rzu donnent le même résultat pour 7.3. L'écart n'existe qu'en dessous de 6.1, hors sujet — mais si le dev implémente un jour 40001, il doit envoyer `mode` (7.3 ≥ e6). |
| Contrainte de longueur | aucune | aucune contrainte serveur déduite | les « 6 chiffres » ne viennent que de `db_string.rdb` (`ui_text_6559`) : c'est l'interface, pas le protocole. Un serveur qui exigerait 6 chiffres rejetterait des codes que le client sait envoyer. |

## 7. `NON ÉTABLI`

Chaque point est formulé comme une question à trancher, avec l'endroit où la réponse devrait se trouver.

- **a) La réponse à un 9005 (succès ou échec).** Aucune référence n'en émet. Les quatre candidats de §5.4
  sont des hypothèses ; le choix est une décision de Killian, pas une lecture. Ce qui manque pour
  trancher : soit un relevé de trafic 7.3 authentique, soit un dépôt serveur 7.3 qui implémente la famille
  (aucun des deux n'est dans `reference/`).
- **b) Le domaine de `mode` que le client 7.3 émet réellement.** rzu nomme `SC_NONE/SC_OPEN_STORAGE/
  SC_DELETE_CHARACTER` (`TS_GA_SECURITY_NO_CHECK.h:12-17`) mais ne les relie pas aux fenêtres du client ; le
  contrôle de flux du client à la VR `0x62d4c0` est trop indirect pour trancher (§4.3). Conséquence
  pratique : **ne pas valider `mode`** dans le serveur.
- **c) Le client peut-il envoyer 9005 sans 9004 préalable ?** Les fenêtres de création/modification du code
  (`SUISecuritySettingWnd`, `SUISecurityClearWnd`, `SUISecuritySettingModifyWnd`) et les paquets 9006/9007/9010/9012
  suggèrent des flux purement locaux, donc un 9005 possible en réponse à une action locale. Non démontré :
  le discriminateur qui choisit entre les deux appelants du constructeur (`0x6658a1` et `0x49dd9e`) n'a pas
  été identifié.
- **d) L'ordre des deux champs de la charge.** Il est établi par rzu (`mode` puis `security_no`) et par le
  client (le `mode` va à l'offset 7, la chaîne à l'offset 11) : **accordé**. La chaîne de trace du client
  (`0xa52318`, `-%s[%d]`) est un indice supplémentaire, mais un `%s` suivi d'un `%d` ne fixe pas les
  offsets — c'est pourquoi la preuve retenue est la séquence de remplissage de la trame, pas la trace.
- **e) L'identité du compte dans un 9005 de 7.3.** La charge 7.3 ne porte **pas** de nom de compte
  (`TS_SECURITY_NO.h:6-11`), alors que la requête d'authentification l'exige (`account[61]`). Le serveur
  doit donc le prendre de la session — **hypothèse raisonnable, mais non attestée par une source** : aucune
  référence n'implémente ce point de jonction.
- **f) Où stocker le code dans Navislamia.** Mesuré : nulle part (§5.3). La référence stocke
  `md5(sel + code)` dans le champ `password` de la table `account` de la base d'**authentification**
  (`DB_SecurityNoCheck.cpp:14-20`), ce qui, dans Navislamia, voudrait dire toucher l'entité
  `AccountEntity` et le schéma d'auth — un choix de Killian.
- **g) `TM_SC_GENERAL_MESSAGE_BOX` en 7.3.** rzu le date d'`EPIC_7_4` alors que le client 7.3 nomme 3004.
  Divergence non tranchée entre les deux autorités ; elle n'est **pas** nécessaire à 9005 tant que §5.4
  reste ouvert, mais elle deviendra bloquante si la réponse choisie est un MessageBox.
- **h) L'appel virtuel final est *interprété* comme l'émission réseau.** Aux deux appelants, la trame est
  passée en argument à `call *0x1cc(%edx)` sur l'objet dont `%esi` porte le pointeur — lecture raisonnable
  (objet de session + trame), mais la table virtuelle n'a **pas** été résolue : ce qui est établi est que
  le client construit une trame 9005 complète et de 30 octets, pas le nom de la méthode qui la remet à la
  socket.

## 8. Commits et binaires épinglés

| Référence | Version | Empreinte |
|---|---|---|
| `reference/rzu` (tailles, ordre des champs, gating) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` — « packets: fix TS_SC_INVENTORY with older epics » (2023-10-02) | — |
| `reference/ngemity` (logique serveur) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` — « Fix compilation issue for GCC » (2025-12-03) | — |
| `reference/client73/SFrame.exe` (le client tranche) | Epic 7.3, `pei-i386`, **9 841 664** octets | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` |
| `reference/client73/db_string.rdb` (textes joueur) | **14 294 729** octets | sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` |
| `Navislamia` (base de la branche) | `master` `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` — « Merge pull request #4 from KillianDoubre/hermes/packet-203-drop-item » | — |

`reference/client73/` **n'est pas un dépôt git** : le binaire et le RDB sont donc épinglés par empreinte.

### 8.1 Emplacements relevés, pour re-vérification

| Élément | VR / offset |
|---|---|
| Entrée `9005 → TM_CS_SECURITY_NO` de la table id→nom | `push` VR `0x678fbc`, chaîne VR `0xa52ec8`, id `mov $0x232d,%eax` VR `0x678fde` |
| Entrée `9004 → TM_SC_REQUEST_SECURITY_NO` | `push` VR `0x678f5a`, chaîne VR `0xa52edc`, id VR `0x678f7c` |
| Constructeur de la trame 9005 | VR `0x48cf70` ; longueur `0x1e` VR `0x48cfb0` ; id VR `0x48cfaa` ; checksum VR `0x48cfc7` |
| Appelants du constructeur | VR `0x6658a1` et `0x49dd9e` (appels `E8` visant `0x48cf70`) |
| Remplissage de la charge (mode, chaîne) | VR `0x6658a6`-`0x6658d5` |
| Chaîne de trace de 9005 | VR `0xa52318` (`offset` fichier `0x650d18`) |
| Fenêtres `SUISecurity*` | noms RTTI VR `0xc19b34`, `0xc19b54`, `0xc19b78`, `0xc19b9c`, `0xc19bc8`, `0xc19bf0` |
| Messages internes `USMSG_*SECURITY*` | noms RTTI VR `0xc1c228`, `0xc1c248`, `0xc1c274`, `0xc1c29c`, `0xc1e0e4`, `0xc1e110`, `0xc1e138`, `0xc1e160` |
| Clés de boîtes de message locales | `msgboxSecuritysetting_succes` VR `0xa4366c`, `…_failed` `0xa4364c`, `…modify_succes` `0xa43628`, `…modify_failed` `0xa43604`, `…clear_impossible` `0xa43544`, `…clear_failed` `0xa43564`, `…clear_succes` `0xa43580`, `…character_access_denied` `0xa4359c`, `…character_failed` `0xa435e4`, `…storage_failed` `0xa435c4` |
| Absence de l'id 9.6.3 `8105` | immédiat 32 bits `a9 1f 00 00` dans `.text` : **0 occurrence** |

## 9. Écarts mesurés avec la fiche `socle-anti-triche` (branche `hermes/packet-socle-anti-triche`, **non mergée**)

La carte demande de confronter ses affirmations à ce qui est réellement sur `master`. Le fichier
`docs/packet-specs/socle-anti-triche.md` **n'existe pas sur `master`** (`git cat-file -e
master:docs/packet-specs/socle-anti-triche.md` → erreur fatale ; `git ls-files docs/packet-specs/` ne liste
que 1202, 203, 253 et 550) : toutes ses citations ne sont donc reproductibles que par
`git show origin/hermes/packet-socle-anti-triche:docs/packet-specs/socle-anti-triche.md`.

Sa convention de citation client, en revanche, **se rejoue** : la règle « pour chaque `push $<imm32 pointant
dans .rdata sur une chaîne TM_*/TS_*>`, l'id est le premier `mov $<imm32>,%eax` qui suit, dans la zone
`.text` `0x66e1ce`..`0x6795bf` » donne, en réexécution indépendante, les mêmes entrées pour les paquets
qu'elle cite.

| Affirmation de la fiche socle | Réalité mesurée | Mécanisme de vérification |
|---|---|---|
| §4.2 : « 166 paires brutes, 151 identifiants plausibles (< 20000) » | **166** reproduit ; **152** plausibles avec la règle énoncée (écart de 1) | réexécution de sa règle sur `SFrame.exe` |
| §4.2 : méthode « stable d'une session à l'autre » | **oui pour les entrées citées**, **non exhaustive** : le premier `0xB8` rencontré peut être un octet de déplacement et non un `mov`. C'est ainsi que l'entrée `9004` (`push` `0x678f5a`, id en `0x678f7c`) échappe au relevé automatique alors que le désassemblage la montre | idem + `objdump -d` sur `0x678f50`-`0x678f90` |
| §7.5 : « entrée `9005 → TM_CS_SECURITY_NO`, `push` VR `0x678fbc`, chaîne VR `0xa52ec8` » | **exact** | `objdump -d` VR `0x678fbc` : `push $0xa52ec8` puis `mov $0x232d,%eax` en `0x678fde` |
| §7.5 : « 23 octets de charge, 30 sur le fil » | **exact**, et confirmé par une seconde voie : le constructeur du client écrit `0x1e` dans la longueur (VR `0x48cfb0`) | `TS_SECURITY_NO.h:5-11` + `objdump -d` VR `0x48cfb0` |
| §7.5 : « gating `X(9005, version < EPIC_9_6_3)` / `X(8105, …)` » | **exact** | `TS_SECURITY_NO.h:13-15` |
| §7.5 : « NGemity concorde (`TS_CS_SECURITY_NO.h:7-8`) » | **exact** | `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_SECURITY_NO.h:6-8` |
| §7.5 : « origine `Any` » et conclusion « client → serveur » | l'`Any` est exact (`TS_SECURITY_NO.h:17`) mais **ne prouve pas** la direction ; celle-ci se démontre par le constructeur de trame du client (§2.3) | `objdump -d` VR `0x48cf70`, `0x6658a1`, `0x49dd9e` |
| §7.5 : « la carte qui le prendra devra statuer sur la vérification et le stockage du code — donc sur du schéma, ce que `ArcadiaSchemaPSQL.sql` ne porte pas aujourd'hui » | **exact** : `grep -niE "security\|password" ArcadiaSchemaPSQL.sql` → 0. Sa fiche ne dit pas **où** la référence stocke pourtant ce code : `md5(sel + code)` dans le champ `password` de la table `account` de la base d'**authentification** (`DB_SecurityNoCheck.cpp:14-20`, `:29`) — c'est l'information que la présente fiche ajoute | `grep`, `DB_SecurityNoCheck.cpp` |
| §8.1 et §13 : `GameClient.cs` « `switch` de `:670` », « groupe `:635-640` » | **faux sur `master`** : le `switch` final est en `:791` et le groupe de continuations `TM_CS_UPDATE`/`MONSTER_RECOGNIZE`/`QUERY` en `:756-761`. La citation était juste **à sa base** (`6a982c8`, où le `switch` est bien en `:670`) | `sed -n` sur `master` ; `git show 6a982c8:Game/Network/Clients/GameClient.cs \| grep -n switch` |
| §8.4 : le `.gitignore` doit recevoir `!/docs/packet-specs/` « sans laquelle le document serait ignoré » | **déjà présent sur `master`** (`.gitignore:473`), la règle d'exclusion étant `.gitignore:469` (`/docs/*`) | `grep -n docs .gitignore` |
| §13 : base `master` `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` | **ancêtre** de `master` (vérifié), mais la base courante est `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` : les numéros de ligne de sa fiche sont périmés d'un décalage de 121 lignes dans `GameClient.cs` | `git merge-base --is-ancestor`, `git rev-parse master` |

**Ce que ce tableau ne dit pas** : la fiche socle n'énonce, sur 9005, aucune taille ni aucun offset faux —
ses §7.5 et §3.1 sont exacts et concordent avec la présente fiche. Ses seules imprécisions sont des
**citations situées** (lignes de `GameClient.cs`, ligne de `.gitignore`) et l'exhaustivité de sa méthode de
relevé automatique. La désignation de forme `_packetSocke` mentionnée par la carte n'apparaît nulle part
dans le document (`grep "Socke\|_packet"` → 0 résultat) : la fiche socle ne nomme ses ajouts que par chemin
de fichier (`Game/Network/Packets/Game/GameAntiHackPackets.cs`, `Tests/Game/AntiHackPacketTests.cs`).

## 10. Note de livraison

- **Livrable** : ce document, `docs/packet-specs/9005-security-no.md`, sur
  `hermes/packet-9005-security-no`. **Aucun fichier de code applicatif n'est modifié.**
- **Ce que le dev doit lire en premier** : §3 (la trame et ses 30 octets), puis §5.4 et §7 (ce qu'il ne doit
  **pas** décider), puis §5.5 (les cinq pièges).
- **Réserve principale** : la réponse à un 9005 n'est établie par aucune source. Une fiche qui affirmerait
  « le serveur répond X » serait inventée.
- **Ce que cette fiche ajoute au savoir durable** : la preuve d'émission côté client (constructeur de trame
  et ses deux appelants), la seconde confirmation des 30 octets, l'emplacement réel du stockage dans
  l'architecture de référence (`md5(sel + code)` dans `account.password` de la base d'auth), l'inventaire
  des neuf ids de la famille tels que le client les nomme, et un bloc `CLAUDE.md` prêt à coller.

### 10.1 Plan de vérification (pour `navis-qa`)

1. `git ls-files docs/packet-specs/` contient `9005-security-no.md` ; `git log --oneline origin/master..master`
   est **vide**.
2. Chaque `fichier:ligne` cité au §1, §4, §5.2 et §5.3 se relit tel quel (`sed -n`) sur `master` `ec76b218`.
3. Les relevés `SFrame.exe` du §8.1 se revérifient par `objdump -d --start-address=… --stop-address=…`
   (deux points suffisent : `0x678fbc` pour la table de noms, `0x48cf70`-`0x48cfd0` pour la trame de 30 octets).
4. `dotnet build Navislamia.sln -c Debug` et `dotnet test Tests/Tests.csproj` restent verts : cette fiche ne
   touche aucun fichier compilé.

## 11. Bloc à recopier dans la description de la MR (destiné à `CLAUDE.md`)

> **Paquet 9005 — `TM_CS_SECURITY_NO` (client → serveur, 30 octets)**
>
> Fiche : `docs/packet-specs/9005-security-no.md`. En 7.3 : `Length` (4) + `ID` = 9005 (2) +
> `checksum` (1) + `mode` (`int32`, offset 7) + `security_no` (19 octets, offset 11, lu jusqu'au
> premier zéro). L'id passe à `8105` à partir d'`EPIC_9_6_3`, et `account(64)`/`result`/`security_no_1/_2`
> n'existent qu'à partir d'`EPIC_9_6_7` : ne jamais recopier un relevé 9.x (94 octets au lieu de 30).
> Le client 7.3 émet réellement ce paquet (constructeur de trame en `0x48cf70`, appelé depuis `0x6658a1`
> et `0x49dd9e`) en réponse à `TM_SC_REQUEST_SECURITY_NO` (9004, `int32 mode`) ; `mode` nomme l'opération
> (`0` aucun, `1` ouverture du coffre, `2` suppression de personnage) mais **aucune source ne fixe le
> domaine effectivement émis** : ne pas valider `mode`. Aucune référence n'implémente la réponse, et
> Navislamia n'a ni stockage du code ni transport 40000/40001 vers le serveur d'authentification : la
> vérification est **hors périmètre** tant que Killian n'a pas tranché (la référence stocke
> `md5(sel + code)` dans le champ `password` de la table `account` de la base d'authentification).
> Le code est un secret : ne jamais le journaliser.
>
> Code : `GamePackets.TM_CS_SECURITY_NO = 9005`, lecteur `GameSecurityPackets.TryReadSecurityNo`
> (30 octets exacts, `mode` à l'offset 7, code à l'offset 11 lu jusqu'au premier zéro, 18 caractères au
> plus), bras de dispatch et `HandleSecurityNo` dans `Game/Network/Clients/GameClient.cs`, tests
> `Tests/Game/SecurityNoPacketsTests.cs`. Le bras ne journalise que `mode` et la **longueur** du code, et
> ne répond rien.

## 12. Implémentation livrée (`navis-dev`, branche `hermes/packet-9005-security-no`)

Le lot suit la disposition neutre de §5.4 : **déclarer, lire, borner, journaliser — sans vérifier, sans
enregistrer, sans réponse, sans sanction**.

| Fichier | Modification |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_SECURITY_NO = 9005` (entre les 8000 et les 10000 depuis le merge dans `master`), avec le rappel du gating (8105 interdit) et des champs 9.6.7 exclus |
| `Game/Network/Packets/Game/GameSecurityPackets.cs` | **nouveau** : `PacketLength = 30`, `TryReadSecurityNo(packet, out int mode, out ReadOnlySpan<byte> securityNo)` (le conteneur `string` d'origine a été retiré au merge, voir 12.2) |
| `Game/Network/Clients/GameClient.cs` | `HandleSecurityNo` + bras de dispatch avant le `switch` qui lève ; la trame est mise à zéro après lecture |
| `Game/Network/Connection.cs` | `Read` efface du tampon de réception les octets consommés (ajout du merge, voir 12.2) |
| `Tests/Game/SecurityNoPacketsTests.cs` | **nouveau** : 25 tests |

### 12.1 Décisions prises (et leur raison)

1. **`mode` n'est pas validé.** §4.3 et §7b le disent : rzu nomme `0`/`1`/`2` mais son propre test
   d'authentification émet `42`. Le lecteur rend la valeur telle quelle, le handler se contente de
   l'écrire dans la trace.
2. **Longueur exacte de 30 octets, refus des autres.** Piège 3 de §5.5 : le client écrit `0x1e` dans le
   champ longueur (VR `0x48cfb0`), donc un 9005 annoncé à une autre longueur est une anomalie de
   protocole, refusé avant que `mode` soit lu — même discipline que `TryReadGetRegionInfo`.
3. **Le code est rendu par le lecteur, mais jamais journalisé.** Le handler n'écrit que
   `mode=` et `securityNoLength=` ; les octets 11-29 n'apparaissent dans aucune ligne. Un test dédié
   branche un sink Serilog et vérifie à la fois que le bras **journalise bien** (donc que le test n'est
   pas vide) et qu'**aucun évènement ne contient le code**.
4. **Aucune réponse, aucune sanction.** Les quatre véhicules candidats de §5.4 restent des hypothèses :
   le bras n'écrit rien sur la connexion, ce qu'un test vérifie sur une `Connection` en mémoire.
5. **Bras de dispatch placé après l'arm du keepalive (`TM_NONE`)**, et non en fin de chaîne : les
   branches sœurs (57, 59, 60, socle anti-triche, 221, 223, 281, 408) insèrent leurs bras ailleurs
   (`TM_SC_REGION_ACK` ou juste avant le `switch` final), ce qui garde cette zone libre de conflit.
   Même logique pour la ligne d'énumération, posée à côté de `TM_NONE` alors que toutes les branches
   sœurs s'insèrent après `TM_CS_VERSION = 50`.
6. **Le conteneur est lu comme une chaîne C, pas comme 19 octets.** §3.2 : `%s` dans la trace du client
   (VR `0xa52318`), `maxSize - 1` côté rzu, donc arrêt au premier zéro et 18 caractères au plus. Aucune
   borne sur la longueur utile : le « code vide » du chemin *Cancel* est accepté.

### 12.2 Durcissement ajouté au merge dans `master` (2026-09-23)

La revue de sécurité du merge a gardé la disposition neutre et resserré la durée de vie du secret en
mémoire :

1. **Plus de `string`.** Le lecteur rendait le code sous forme de `string` : une copie immuable sur le
   tas, qu'aucun code ne peut effacer et que seul le ramasse-miettes finit par récupérer. Il rend
   désormais une vue `ReadOnlySpan<byte>` sur la trame, sans copie. Un futur vérificateur comparera cette
   vue en temps constant (`CryptographicOperations.FixedTimeEquals`).
2. **La trame est mise à zéro après lecture**, dans un `finally` : une trame mal formée porte aussi ce
   que le joueur a tapé.
3. **Le tampon de réception est effacé.** Depuis le lot de performances, `CipherConnection` déchiffre
   sur place dans le tampon de réception de la connexion, qui vit aussi longtemps qu'elle : le code y
   restait en clair jusqu'à ce qu'un autre trafic l'écrase. `Connection.Read` efface maintenant chaque
   octet consommé, pour toutes les trames.

Ce qui ne change pas : aucun 9005 n'est sollicité (le serveur n'émet jamais 9004), rien n'est vérifié ni
répondu, donc aucun oracle de force brute n'existe ; la trame n'est acceptée qu'à 30 octets exacts.

### 12.2 Couverture de tests (`Tests/Game/SecurityNoPacketsTests.cs`)

Offsets et taille : `PacketLength == 30`, `Length` (4) + `ID` (2, 9005) + `checksum` (1), `mode` à
l'offset 7, code à l'offset 11, dix-neuvième octet du conteneur à zéro, absence de tout champ 9.6.7,
lecture petit-boutiste de `mode`, fenêtre du code qui ne commence pas à l'offset 10, arrêt au premier
zéro, plafond de 18 caractères, longueurs refusées (0, 7, 29, 31), identifiant 8105 non déclaré,
`9005 ≠ 9004`. Boucle de réception réelle : trame consommée sans lever, trame malformée consommée sans
réponse, trame coalescée avec un keepalive, aucune écriture sur la connexion, et l'absence du code dans
tous les évènements de journal.

### 12.3 Réserves

1. **Le sort du code reste tranché par Killian.** Rien n'est stocké, rien n'est vérifié : le paquet est
   lu et tracé, comme §7a/§7f l'autorisent. Une vérification demanderait le transport 40000/40001 et un
   stockage qui n'existent pas (§5.3).
2. **`mode` n'est pas contrôlé**, volontairement (§7b).
3. **Le `account[61]` de la requête d'authentification** devra être pris de la session le jour où une
   vérification sera implémentée : ce point de jonction n'est attesté par aucune source (§7e).
4. **Un 9005 annoncé à une autre longueur que 30 est refusé**, y compris une trame plus longue : c'est la
   conséquence assumée du choix « longueur exacte » (§12.1 point 2). Elle est sans effet aujourd'hui, le
   client 7.3 écrivant toujours `0x1e` et le serveur ne servant que 7.3 ; à rouvrir le jour où un client
   plus récent serait accepté.


## A VERIFIER PAR KILLIAN

Trois décisions externes ; aucune ne peut être tranchée en lisant les références (§7), et aucune ne doit
être devinée par `navis-dev`.

1. **Que répond-il à un 9005 ?** Aucune référence n'implémente la réponse côté serveur de jeu : rzu s'arrête
   au serveur d'authentification (`TS_GA_SECURITY_NO_CHECK` 40001 → `TS_AG_SECURITY_NO_CHECK` 40000, résultat
   booléen), NGemity ne définit même pas le handler. Les candidats connus et leurs sources sont en §5.4.
   Sans arbitrage, le dev doit se limiter à « connu, lu, journalisé — aucune réponse inventée ».
2. **Où vit le code de sécurité dans Navislamia ?** Ni colonne (`AccountEntity.cs:5-16`), ni table (le schéma
   livré n'a pas de table de compte), ni paquet 40000/40001 (`AuthPackets.cs:5-14`). La référence, elle,
   stocke `md5(sel + code)` — sel `auth.securityno.salt`, défaut `2011` — dans le champ `password` de la
   table `account` de la base d'**authentification** (`DB_SecurityNoCheck.cpp:16,29,49-50`). Décider si
   Navislamia veut ce stockage, et où.
3. **Le domaine de `mode` : le serveur le valide-t-il ?** rzu nomme `0`/`1`/`2`
   (`TS_GA_SECURITY_NO_CHECK.h:12-17`) mais son propre test d'authentification émet `42`
   (`rzauth/test/.../SecurityNo.cpp:30`) : un serveur qui refuserait tout `mode` hors 0-2 rejetterait ce que
   la référence elle-même accepte. La fiche recommande de **ne rien valider** ; à confirmer.

**État après le lot `navis-dev`** (§12) : aucun champ `NON ÉTABLI` n'a été deviné. Le paquet est déclaré,
lu, borné et tracé ; `mode` n'est pas validé ; le code n'est ni vérifié, ni enregistré, ni journalisé ; le
serveur ne répond rien. Les trois décisions ci-dessus restent donc entières, et les réserves du lot sont
listées en §12.3.
