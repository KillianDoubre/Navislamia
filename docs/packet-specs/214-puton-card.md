# 214 — `TM_CS_PUTON_CARD` (sertir une carte dans une châsse d'équipement)

> Fiche d'archéologie de protocole établie par **navis-ref** le 2026-09-20, branche
> `hermes/packet-214-puton-card`, base `master` = `ec76b218cd0bd7c6498d725f253abb8b431f0cd6`.
>
> Références épinglées : `reference/rzu` = `87c1e83bf84efe29bb6405e8e6da80349712f3fa`,
> `reference/ngemity` = `38ceb2c6065fabf6ff4ba71d52f955f362c6c839`.
>
> `reference/client73` **n'est pas un dépôt git**. Toute la partie client provient de la lecture
> statique de deux fichiers, sans aucune exécution (`SFrame.exe` non lancé, aucun Lua, aucun
> script du client) :
>
> - `SFrame.exe` (9 841 664 o, sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`),
>   lu par `strings -t x -n 4`, `objdump -h`, `objdump -d -j .text`, `objdump -s` et lecture
>   directe des octets. Sections utiles : `.text` VMA `0x401000` → fichier `0x400` (delta
>   `0x400c00`), `.rdata` VMA `0xa0f000` → fichier `0x60da00` (delta `0x401600`).
>   Les adresses citées `0x.......` sont des **VA** (adresses virtuelles telles qu'affichées par
>   `objdump`), convertibles en offset fichier par le delta de la section.
> - `db_string.rdb` (14 294 729 o, sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1`),
>   lu par `strings -n 4`. Les numéros de ligne cités « `db_string.rdb`, l. N » sont ceux de ce
>   dump ; ceux de `SFrame.exe` sont ceux du dump `strings -n 4` de l'exécutable.

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| id décimal | **214** (`0x00D6`) | `op_codes.md:63` |
| nom Navislamia | `TM_CS_PUTON_CARD` | `op_codes.md:63` |
| nom rzu | `TS_CS_PUTON_CARD` | `reference/rzu/librzu/src/packets/GameClient/TS_CS_PUTON_CARD.h:15` |
| nom NGemity | `TS_CS_PUTON_CARD` | `reference/ngemity/shared/Server/ClientPackets.h:79` |
| sens | client → serveur (`SessionPacketOrigin::Client`) | `reference/rzu/.../TS_CS_PUTON_CARD.h:15` |
| id après remap | `1214` pour `version >= EPIC_9_6_3` — **sans effet à 7.3** | `reference/rzu/.../TS_CS_PUTON_CARD.h:12-13` |
| jumeau | `215` `TM_CS_PUTOFF_CARD` (retirer la carte) | `op_codes.md:64` |
| présent dans `GamePackets.cs` ? | **non** (aucune occurrence de `PUTON_CARD`/`PUTOFF_CARD` dans `*.cs`) | `grep -rn "PUTON_CARD\|PUTOFF_CARD" --include=*.cs .` → 0 résultat |
| implémenté dans Navislamia ? | **non** (ni énumération, ni lecteur, ni dispatch, ni service) | idem |

Voisinage immédiat dans `op_codes.md`, qui situe le paquet dans le bloc « inventaire et
équipement » :

| id | nom | présent dans `GamePackets.cs` |
|---|---|---|
| 200 / 201 | `TM_CS_PUTON_ITEM` / `TM_CS_PUTOFF_ITEM` | oui, `GamePackets.cs:26-27` |
| 203 / 204 | `TM_CS_DROP_ITEM` / `TM_CS_TAKE_ITEM` | oui, `GamePackets.cs:29,34` |
| 207 | `TM_SC_INVENTORY` | oui, `GamePackets.cs:31` |
| 210 | `TM_SC_TAKE_ITEM_RESULT` | oui, `GamePackets.cs:36` |
| 211 / 212 | `TM_SC_OPEN_STORAGE` / `TM_CS_STORAGE` | non (chaîne « socle entrepôt du personnage ») |
| 213 | `TM_SC_GET_CHAOS` | non |
| **214 / 215** | **`TM_CS_PUTON_CARD` / `TM_CS_PUTOFF_CARD`** | **non — objet de cette chaîne** |
| 216 / 217 | `TM_SC_BELT_SLOT_INFO` / `TM_SC_ITEM_COOL_TIME` | oui, `GamePackets.cs:37-38` |
| 218 / 219 | `TM_CS_CHANGE_ITEM_POSITION` / `TM_CS_ARRANGE_ITEM` | oui, `GamePackets.cs:39-40` |

Le couple `214`/`215` fait partie des identifiants du bloc `200`-`219` encore absents de
`GamePackets.cs` sur `master` : `211`, `212`, `213`, `214`, `215` (`210`, `216`, `217`, `218`,
`219` sont présents juste après). L'id `206` n'existe ni dans `op_codes.md` ni dans le client.

**Correction du cadrage de la carte PO** : `docs/packet-specs/` **existe** sur `master`
(4 fiches : `1202-emotion.md`, `203-drop-item.md`, `253-use-item.md`, `550-get-region-info.md`,
`git ls-tree origin/master docs/packet-specs/`) et l'exception de `.gitignore` est en place
(`.gitignore:473`). Le répertoire n'est pas à créer.

---

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Le nom du paquet dans le client 7.3 (preuve de vocabulaire)

`SFrame.exe` contient une **table `id → nom`** construite au démarrage : une `std::map<int,
std::string>` remplie par une longue suite d'insertions (clé = id, valeur = nom du paquet)
dans la fonction qui commence en VA `0x6752de` ; l'insertion se fait par
`call 0x674350` (VA `0x674350`, `push %ebp`).

Entrées qui concernent cette fiche :

| id | nom inséré | longueur | chaîne (VA → offset fichier) | site du `push` de la chaîne | site du `mov $id,%eax` | site de l'insertion |
|---|---|---|---|---|---|---|
| 214 (`0xd6`) | `TM_CS_PUTON_CARD` | 16 (`0x10`) | `0xa53710` → fichier `0x652110` | VA `0x676683` / `0x676685` | VA `0x6766b9` | VA `0x6766d8` |
| 215 (`0xd7`) | `TM_CS_PUTOFF_CARD` | 17 (`0x11`) | `0xa536fc` → fichier `0x6520fc` | VA `0x6766f9` / `0x6766fb` | VA `0x67672f` | VA `0x67674e` |

Ces deux chaînes `.rdata` sont visibles en clair dans le binaire et se déduisent aussi de
`strings -t x -n 4 SFrame.exe` (offsets `0x652110` et `0x6520fc`, dump `l. 26384-26385`).

La table a été **recoupée sur ~35 identifiants** avec `op_codes.md` et correspond partout :
`200`→`TM_CS_PUTON_ITEM`, `201`→`TM_CS_PUTOFF_ITEM`, `202`→`TM_SC_WEAR_INFO`,
`203`→`TM_CS_DROP_ITEM`, `204`→`TM_CS_TAKE_ITEM`, `207`→`TM_SC_INVENTORY`,
`208`→`TM_CS_ERASE_ITEM`, `210`→`TM_SC_TAKE_ITEM_RESULT`, `211`→`TM_SC_OPEN_STORAGE`,
`212`→`TM_CS_STORAGE`, `213`→`TM_SC_GET_CHAOS`, `216`→`TM_SC_BELT_SLOT_INFO`,
`217`→`TM_SC_ITEM_COOL_TIME`, `250`→`TM_SC_MARKET`, `253`→`TM_CS_USE_ITEM`,
`254`→`TM_SC_DESTROY_ITEM`, `255`→`TM_SC_UPDATE_ITEM_COUNT`, `256`→`TM_CS_MIX`,
`257`→`TM_SC_MIX_RESULT`, `263`/`264`→`TM_CS_TRANSMIT_ETHEREAL_DURABILITY[_TO_EQUIPMENT]`,
`280`→`TM_TRADE`, `281`→`TM_CS_PUTON_ITEM_SET`, `286`→`TM_SC_SKILLCARD_INFO`,
`287`→`TM_SC_ITEM_WEAR_INFO`. C'est donc bien une table d'opcodes, pas une liste de chaînes
quelconque : **le client 7.3 nomme `214` `TM_CS_PUTON_CARD`**.

**Réserve de méthode, à ne pas ignorer** : la table est **partielle** et son usage exact n'est
pas établi (probable table de journalisation de paquets). Des identifiants du client en sont
absents — `206`, `209`, `220`, `223`, et toute la plage `258`-`262` alors que ses voisins `256`,
`257`, `263`, `264` y sont. Surtout, `218` et `219` (`TM_CS_CHANGE_ITEM_POSITION`,
`TM_CS_ARRANGE_ITEM`) en sont absents alors que le même binaire déclare
`.?AUSIMSG_REQ_CHANGE_ITEM_POSITION@@` (dump `l. 44067`) et `.?AUSIMSG_REQ_ARRANGE_ITEM@@`
(`l. 44068`). **La présence prouve que le binaire connaît le nom ; l'absence ne prouve rien.**
Voir §7.2 pour la conséquence (aucune preuve d'émission).

### 2.2 Le corpus de ressources du sertissage (pierres d'âme)

Le sous-système concerné apparaît partout dans les ressources du client, sous le vocabulaire
**Soul Stone / « châsse »** :

| Ressource | Contenu | Source |
|---|---|---|
| `smsg_soket01` | *« There is already a Soul Stone in this socket. When you put another Soul Stone in the socket, then the original will be destroyed. Would you like to socket the Soul Stone? »* | `db_string.rdb`, l. 198036-198037 |
| `smsg_soket02` | *« Socketing cost is `<B>#@cost@#</B>`. Would you like to socket the item? »* | l. 198038-198039 |
| `smsg_soket03` | *« Only Soul Stones can be socketed. »* | l. 198040-198041 |
| `smsg_soket04` | *« The item is socketed already. »* | l. 198042-198043 |
| `smsg_soket05` | *« You can not have more than 2 Soul Stones socketed in the same weapon that increase the same stat. »* | l. 198044-198045 |
| `smsg_soket06` | *« This is the wrong type of item. »* | l. 198046-198047 |
| `smsg_soket07` | *« Socketing is completed. »* | l. 198048-198049 |
| `smsg_soket09`-`smsg_soket11` | *« You don't have enough Lak. »*, recharge de Soul Power | l. 198052-198057 |
| `ui_text_6480`-`ui_text_6493` | `Socketing`, `Item`, `Soul Stone Sockets`, `Socket 1` … `Socket 4`, `Price:`, `Recharge EQ`, `Item to be Recharged`, `Choose all Equipped Items`, `Current Soul Power: #@per@#%` | l. 112375-112401 |
| `window_Socket.nui` | fenêtre de sertissage | `SFrame.exe`, dump `l. 24812` |
| `static_common_socket_itemslot`, `icon_socket1`…`icon_socket4`, `icon_socket_disable1`…`4`, `icon_socket_armor|helm|weapon|boots|glove|shield`, `crmainframe_soket_number%02d` | pastilles et icônes **par type d'équipement** (`armor`, `helm`, `weapon`, `boots`, `glove`, `shield`) | dump `l. 21802-21816`, `21063`, `30293` |
| `filter_soulstone_chk` / `filter_soulstone_lb`, `filter_card_chk` / `filter_card_lb` | filtres d'inventaire « pierres d'âme » et « cartes » (choix de l'objet à sertir) | dump `l. 22047-22062` |
| `#@socket_empty@#<offset:25>`, `#@socket_empty@#` | jeton de description d'objet pour une châsse vide | dump `l. 21515`, `30148` |
| `soulstonedrop.wav` | | dump `l. 21347` |

Ce que ces ressources établissent : à 7.3, **la châsse d'un équipement se remplit de pierres
d'âme**, avec 4 chasses numérotées `Socket 1`…`Socket 4`, un prix, une confirmation, et un
refus local possible (« Only Soul Stones can be socketed »).

Ce qu'elles n'établissent pas : **quel paquet la fenêtre `window_Socket.nui` émet**. Voir §7.2.

### 2.3 Ce qui a été cherché et n'a PAS été trouvé

1. **Aucun constructeur de trame portant l'id `214` en dur.** Balayage de `.text` sur
   `b8 d6 00 00 00` (`mov $0xd6,%eax`), `66 b8 d6 00` (`mov $0xd6,%ax`), `6a d6` /
   `68 d6 00 00 00` (`push $0xd6`) et `c7 .. d6 00 00 00` : hors de la table `id → nom` du §2.1, il
   n'y a que quatre sites, et aucun n'est un constructeur de trame (aucun n'écrit l'id dans un
   en-tête, motif `mov %ax,0x4(%reg)`) :
   - VA `0x550e70` : `cmpl $0xd6,(%edx)` (comparaison), suivi de deux `push $0xd6` en `0x550e82` et
     `0x550e87` ;
   - VA `0x63dc2c` et VA `0x63dc62` : `push $0xd5` / `push $0xd6` passés en argument d'un appel
     (`call 0x554480`, fonction à cadre SEH) puis la chaîne de trace
     `SGameInterface - MSG_CHAT_RESULT` (VA `0xa4b2d4`, fichier `0x649cd4`) — vocabulaire
     d'**interface**, pas d'en-tête de paquet ;
   - VA `0x890224` : `push $0xd6` dans la longue série homogène
     `push $<arg>; push $<opcode>; jmp 0x89059b` qui couvre tout le bloc `0xc0`→`0xff` (par exemple
     `0x890183`/`0x890185` pour `0xc8`, `0x89018f`/`0x890191` pour `0xc9`) ; le rôle de cette série
     n'est pas établi et elle traite **tous** les ids du bloc de la même manière : elle ne peut donc
     pas fonder quoi que ce soit de propre à `214`.

   **Contre-exemple décisif** : `203` (`TM_CS_DROP_ITEM`), dont
   l'émission est acquise, ne présente lui aussi qu'une seule occurrence (`b8 cb 00 00 00`,
   la table `id → nom`). Ce balayage ne prouve donc **rien** : il ne fait qu'échouer à prouver
   l'émission.
2. **Aucun bloc dédié dans le gestionnaire `TM_SC_RESULT` du client.** Ce gestionnaire est en
   VA `0x66db80` : il lit `request_msg_id` (`movzwl 0x7(%esi),%eax`, `0x66dbe2`), traite `0x119`
   (281) à part, puis `sub $0x5,%eax` / `cmp $0xfe,%eax` / `ja` défaut, et indexe une table
   d'octets en VA `0x66e2d8` (`movzbl 0x66e2d8(%eax),%eax`) qui sélectionne une table de pointeurs
   de 18 blocs en VA `0x66e290`. Résultat pour les ids de ce bloc :
   `200`,`201`,`203`,`204`,`208`,`212`,`219` → blocs à libellé dédiés ; **`214` et `215` → octet
   `17` → bloc par défaut `VA 0x66e258`**, comme `202`, `205`, `207`, `209`, `210`, `211`, `213`,
   `216`, `217`, `218`. Autrement dit : le client sait lire `TM_SC_RESULT` pour `214` (chemin par
   défaut, sans message dédié) mais n'a **pas** de libellé de résultat propre à `214`. Comme
   `218` et `207` (émis, eux) n'en ont pas non plus, ce n'est pas une preuve d'inexistence.
3. **Aucune classe d'interface ni message interne propre à `214`.** Les classes RTTI et messages
   du sertissage présents dans le binaire sont tous orientés famille `260`/`262` :
   `.?AUSIMSG_UI_SOULSTONE_CRAFT@@` (dump `l. 44021`), `.?AUSIMSG_UI_REPAIR_SOULSTONE@@`
   (l. 44022), `.?AUSIMSG_SOULSTONE_MOVEITEM@@` (l. 44185), `SGameInterface - IMSG_SOULSTONE_MOVEITEM`
   (l. 25057). Aucun `USIMSG_*PUTON_CARD*` / `USIMSG_*SOCKET*`.
4. **Aucun cas `214`/`215` dans les chaînes de dispatch des opcodes reçus.** Balayage des
   comparaisons d'immediats de la région qui porte ces chaînes (VA `0x67d000`-`0x67ef40`, celle qui
   renvoie l'opcode inconnu au bloc VA `0x67ef21`) : 62 comparaisons, dont `0xcd` (205,
   `TM_SC_DROP_RESULT`), `0xfa` (250), `0x1f8` (504), `0x1195` (4501), `0xc356`/`0xc358`, mais
   **aucune `0xd6` ni `0xd7`**. Le client ne traite donc pas `214` comme un opcode entrant — ce qui
   est cohérent avec un paquet client → serveur, et avec le fait que le gestionnaire `TM_SC_RESULT`
   (§2.3 point 2) le connaît comme identifiant de *requête*.

**Conclusion de §2** : le geste du joueur — *sertir une pierre d'âme dans une châsse d'un
équipement, depuis la fenêtre de châsse, avec confirmation et éventuel coût* — est **plausible et
documenté côté ressources**, mais **non établi** : ni la preuve d'émission de `214`, ni le
câblage entre `window_Socket.nui` et `214` n'ont été trouvés (§7.2).

---

## 3. Structure sur le fil

### 3.1 En-tête

En-tête commun du dépôt : `Game/Network/Packets/Header.cs:9-11` (`Length` `uint32`, `ID` `uint16`,
`Checksum` `uint8`), relu aux offsets 0/4/6 (`Header.cs:22-24`) ; `GameActionPackets` fixe
`HeaderSize = 7` (`Game/Network/Packets/Game/GameActionPackets.cs:8`), comme
`GameCharacterPackets` (`:19`).

### 3.2 Ordre des champs — pourquoi `position` vient en premier

rzu publie ce paquet sous forme `_(def)`/`_(impl)` :

```
#define TS_CS_PUTON_CARD_DEF(_) \
	_(def)(simple)(int32_t, position) \
	_(impl)(simple)(int8_t, position, version < EPIC_9_6_7) \
	_(simple)(ar_handle_t, item_handle) \
	_(impl)(simple)(int32_t, position, version >= EPIC_9_6_7)
```
(`reference/rzu/librzu/src/packets/GameClient/TS_CS_PUTON_CARD.h:5-9`)

Le générateur de rzu développe la macro `_DEF` **dans l'ordre du texte**, chaque entrée devenant
sa macro de sérialisation ; les entrées `_(def)` ne produisent **aucun** code de sérialisation
(`PacketDeclaration.h:512-522` : `SERIALIZATION_F_def(x)` = `DO_NOTHING`,
`DESERIALIZATION_F_def(x)` = `DO_NOTHING`, alors que `SERIALIZATION_F_impl(x)` /
`DESERIALIZATION_F_impl(x)` sont réelles ; `DEFINITION_F_def` est réelle et `DEFINITION_F_impl`
inerte). L'ordre des octets est donc **l'ordre des entrées qui matchent la version**, les entrées
`_(def)` ne servant qu'à la définition/au JSON.

Pour Epic 7.3, seule l'entrée `version < EPIC_9_6_7` matche (`0x070300 < 0x090607`), et elle
précède `item_handle` : **`position` (1 octet) puis `item_handle` (4 octets)**. Trois
recoupements indépendants :

- NGemity, portage manuel du client, déclare le même ordre et les mêmes types
  (`shared/Server/Packets/GameClient/TS_CS_PUTON_CARD.h:6-8` : `_(simple)(int8_t, position)`,
  `_(simple)(uint32_t, item_handle)`) ;
- le couple jumeau `200`/`201`, déjà implémenté dans ce dépôt, suit exactement le même schéma
  (`TS_CS_PUTON_ITEM.h:7-12` a la même forme `_(def)`/`_(impl)` + `_(simple)`), et le dépôt le lit
  `position` en premier (`GameActionPackets.cs:149-163`, `TryReadPutonItem` : `(sbyte)packet[7]`,
  puis deux `ReadUInt32LittleEndian` aux offsets `8` et `12`) — savoir déjà consigné dans
  `CLAUDE.md:430-431` (« `TS_CS_PUTON_ITEM` (200) est 16 octets : `position` (int8 @7),
  `item_handle` (uint32 @8), `target_handle` (uint32 @12) ») ;
- `ar_handle_t` est un `uint32_t` (`reference/rzu/librzu/src/lib/Packet/GameTypes.h:40`).

### 3.3 `TM_CS_PUTON_CARD` — client → serveur — **12 octets**

| Offset | Taille | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` | 12 | `Header.cs:9,22` |
| 4 | 2 | `uint16` | `ID` | `214` (`0x00D6`) | `op_codes.md:63` ; client §2.1 |
| 6 | 1 | `uint8` | `Checksum` | 0 | `Header.cs:11,24` |
| 7 | 1 | `int8` | `position` | `NON ÉTABLI` (voir §7.1) | `TS_CS_PUTON_CARD.h:7` (`version < EPIC_9_6_7`) |
| 8 | 4 | `uint32` (`ar_handle_t`) | `item_handle` | `NON ÉTABLI` (handle d'objet) | `TS_CS_PUTON_CARD.h:8` ; `GameTypes.h:40` |
| 12 | — | — | *fin* | — | — |

**Taille totale attendue : 12 octets** (7 d'en-tête + 5 de charge utile).

Rappel du jumeau, pour information seulement (hors périmètre de cette chaîne, §7.5) :
`TM_CS_PUTOFF_CARD` (`215`) = 7 + `int8 position` = **8 octets**
(`TS_CS_PUTOFF_CARD.h:5-10` ; NGemity `TS_CS_PUTOFF_CARD.h:6-7`).

**Recommandation de lecture** : accepter toute trame de longueur `>= 12` et n'exploiter que les 5
premiers octets de charge utile. Le précédent `281` (`TM_CS_PUTON_ITEM_SET`) a montré que le client
7.3 peut émettre une trame **plus longue** que ce que rzu déclare ; la taille réellement émise par
le client pour `214` n'a pas pu être mesurée (§7.4).

---

## 4. Gating de version

Bornes rzu (`reference/rzu/librzu/src/lib/Packet/PacketEpics.h`) : `EPIC_7_3 = 0x070300` (`:59`),
`EPIC_9_6_3 = 0x090603` (`:96`), `EPIC_9_6_7 = 0x090607` (`:104`).

| Champ | Gating rzu | Statué pour Epic 7.3 | Source |
|---|---|---|---|
| id du paquet | `X(214, version < EPIC_9_6_3)` / `X(1214, version >= EPIC_9_6_3)` | **214** (pas de remap : `0x070300 < 0x090603`). Le client 7.3 porte lui-même `214` (§2.1). Le remap `+1000` de 9.6.3 ne nous concerne pas. | `TS_CS_PUTON_CARD.h:11-13` ; `PacketEpics.h:59,96` |
| `position` | `_(impl)(simple)(int8_t, position, version < EPIC_9_6_7)` et `_(impl)(simple)(int32_t, position, version >= EPIC_9_6_7)` | **`int8`, 1 octet** (`0x070300 < 0x090607`). | `TS_CS_PUTON_CARD.h:7,9` ; `PacketEpics.h:104` |
| `item_handle` | aucun gating (entrée `_(simple)`) | **`uint32`, 4 octets**, à toutes les versions. | `TS_CS_PUTON_CARD.h:8` |

Aucun autre champ ni aucune autre borne de version n'apparaît dans le fichier : **aucun champ de
`214` n'a de gating non statué pour 7.3**.

Historique utile de ce gating (il vient d'une correction tardive, ce qui explique la moitié des
pièges de `CLAUDE.md`) : jusqu'au 2022-04-04 rzu déclarait `_(simple)(int8_t, position)` pour
**toutes** les versions ; le commit `54416f87` (« update 9.6.7 packets after late detection of
issues », 2022-04-04) a introduit le couple `_(def)`/`_(impl)` et la variante `int32` pour
`>= 9.6.7`, sans toucher à la variante `int8`. Autrement dit, la valeur **7.3 = `int8`** n'a
jamais été mise en cause par cette correction ; c'est la borne haute qui a bougé. NGemity, serveur
visant 9.8, déclare pourtant `int8_t` : son en-tête est donc en retard sur rzu pour la borne
`9.6.7`, mais **concorde avec nous pour 7.3** (§6.1).

---

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` (NGemity) fait du paquet : rien

- les deux paquets sont **déclarés** : `shared/Server/ClientPackets.h:79-80`,
  `shared/Server/Packets/GameClient/TS_CS_PUTON_CARD.h:6-10` (`CREATE_PACKET(TS_CS_PUTON_CARD, 214)`),
  `TS_CS_PUTOFF_CARD.h:6-9`, et inclus par `shared/Server/XPacket.h:128,130` ;
- **aucun gestionnaire** : la table `worldPacketHandler` (`Chihiro/src/Network/GameNetwork/WorldSession.cpp:120-137`)
  énumère tous les handlers du monde (`onSoulStoneCraft`, `onStorage`, `onBindSkillCard`,
  `onUnBindSkilLCard`, `onDropQuest`, `onCancelAction`, …) et **ne contient ni `onPutOnCard` ni
  `onPutOffCard`** ; `grep -rn "PutonItem\|PutoffItem"` ne renvoie rien pour les cartes, et aucune
  méthode `*Card` n'existe dans `Chihiro/src`. Un `214` entrant tombe dans le
  `NG_LOG_DEBUG("Got unknown packet …")` de `:158-162`.

NGemity n'est donc **pas une source de logique** pour `214`/`215`. En revanche il implémente le
système de châsses ailleurs, et c'est de là que vient tout ce qu'on peut transposer :

1. **`position` = index d'emplacement de port**, pour la paire voisine `200`/`201` :
   `onPutOnItem` (`WorldSession.cpp:616-646`) appelle `unit->Puton((ItemWearType)pRecvPct->position, ci)`
   (`:636`) ; `onPutOffItem` (`:648-673`) fait `unit->GetWornItem((ItemWearType)pRecvPct->position)`
   (`:665`) puis `unit->Putoff((ItemWearType)pRecvPct->position)` (`:670`). Le même savoir est déjà
   dans ce dépôt : `EquipmentService.UnequipAsync` (`Game/Services/EquipmentService.cs:77-113`)
   valide `IsWearableSlot(request.Position)` (`:115-118`, `0 <= position < 24`) puis
   `UnequipItemAsync(info.CharacterName, (ItemWearType)request.Position)`.
2. **Le modèle de châsse** : la ressource d'objet porte un **nombre de chasses**
   (`Chihiro/src/Entities/Item/ItemTemplate.hpp:388` `int32_t socket;`) et l'instance d'objet
   **quatre** chasses (`Item.cpp:26-27,57` `SetSocket({nSocket0..nSocket3})`, `:99-102,138-143`) ;
   une châsse remplie contient un **code de ressource** d'objet (ainsi `Item.cpp:227-234`
   additionne l'endurance de la ressource de chaque châsse, et `:317-324` place l'UID d'une
   invocation dans la châsse 0 d'une carte d'invocation).
3. **La logique de sertissage la plus proche** : `onSoulStoneCraft` (`WorldSession.cpp:1497-1587`,
   handler déclaré `:131`) traite `TS_CS_SOULSTONE_CRAFT` (`260`) = un *handle* d'objet plus
   **un tableau de 4 handles de pierres** (`reference/rzu/librzu/src/packets/GameClient/TS_CS_SOULSTONE_CRAFT.h:5-11`,
   ids `260`/`1260`). Gardes observées, dans l'ordre : fenêtre ouverte par le PNJ
   (`GetLastContactLong("SoulStoneCraft") == 0` → retour sans réponse, `:1499-1500`), objet trouvé
   par handle sinon `TS_RESULT_NOT_EXIST` (`:1503-1507`), nombre de chasses du modèle dans `1..4`
   sinon `TS_RESULT_ACCESS_DENIED` (`:1509-1512`), pour chaque handle non nul : pierre trouvée,
   puis contrôle de nature `TYPE_SOULSTONE` + `GROUP_SOULSTONE` + `CLASS_SOULSTONE` sinon
   `TS_RESULT_NOT_ACTABLE` (`:1522-1533`), règle « au plus **2** pierres identiques sur une arme à
   4 chasses » (`nMaxReplicatableCount = nSocketCount == 4 ? 2 : 1`, `:1514`) comparée sur
   `base_type[0..3]`, `base_var[..][0]`, `opt_type[0..3]`, `opt_var[..][0]` sinon
   `TS_RESULT_ALREADY_EXIST` (`:1535-1549`), coût cumulé `prix / 10` (`:1550`) et solde d'argent
   contrôlé sinon `TS_RESULT_NOT_ENOUGH_MONEY` (`:1556-1559`) — **prélevé, jamais remboursé**
   (le chemin échoue par `return` après débit). Réussite : châsse remplie avec le **code** de la
   pierre (`SetSocketIndex(i, …GetCode())`, `:1572`), pierre détruite (`EraseItem(..., 1)`, `:1573`),
   endurance recalculée (`:1580`), sauvegarde + `DBUpdate()` (`:1581-1582`), fenêtre PNJ refermée
   (`SetLastContact("SoulStoneCraft", 0)`, `:1583`), **renvoi de la fiche d'objet**
   (`Messages::SendItemMessage`, `:1584`), recalcul des statistiques (`:1585`), puis
   `TS_RESULT_SUCCESS` (`:1586`).
   Le renvoi de fiche mérite d'être relevé : `Messages::SendItemMessage`
   (`Chihiro/src/Network/Messages.cpp:250-260`) envoie un **`TS_SC_INVENTORY` contenant la seule
   fiche de l'objet modifié** — c'est exactement le canal qui transporte les 4 châsses (§5.3
   point 4), et la référence le confirme indépendamment.
   Cette règle des 2 pierres est **corroborée par le client 7.3 lui-même** : `smsg_soket05`
   (§2.2), comme l'est le coût par `smsg_soket02`.
4. Les paquets de fenêtre de cette famille existent : `Messages::ShowSoulStoneCraftWindow`
   (`:943-946`) et `ShowSoulStoneRepairWindow` (`:936-939`), déclenchés par les scripts PNJ
   (`Chihiro/src/Scripting/XLua.cpp:917-928`) — c'est ce chemin (`260`), et non `214`/`215`, que
   NGemity a implémenté.

### 5.2 Ce que rzu fait du paquet : rien

rzu ne fait que déclarer `214` (`CREATE_PACKET_VER_ID`, `TS_CS_PUTON_CARD.h:15`) ; il n'y a **pas
de paquet serveur → client** pour cette famille : sur l'ensemble du répertoire
`librzu/src/packets/`, les seuls paquets « carte » sont `TS_CS_PUTON_CARD` (214),
`TS_CS_PUTOFF_CARD` (215), `TS_CS_BIND_SKILLCARD` (284), `TS_CS_UNBIND_SKILLCARD` (285),
`TS_SC_SKILLCARD_INFO` (286) et `TS_CS_SUMMON_CARD_SKILL_LIST` (452). Aucun `TS_SC_PUTON_CARD`.
La réponse à `214` n'est donc **pas** un paquet dédié : l'état des châsses ne peut voyager que par
la **fiche d'objet**.

### 5.3 Ce que le serveur Navislamia doit faire

Les primitives sont déjà là ; le canal de réponse aussi.

1. **Énumération et dispatch, ensemble** (critère d'acceptation 4) : ajouter
   `TM_CS_PUTON_CARD = 214` dans `Game/Network/Packets/Enums/GamePackets.cs`, dans le bloc du
   voisinage (entre `TM_SC_GET_CHAOS = 213` et `TM_SC_BELT_SLOT_INFO = 216`, modèle
   `GamePackets.cs:36-40`), **et** son bras de dispatch dans la chaîne de `GameClient.cs` (modèle
   `GameClient.cs:698,704`), **et** son entrée dans le `switch` terminal, dont le défaut est
   `_ => throw new Exception("Unknown Packet Type")` (`GameClient.cs:802`). Aucun membre de
   `GamePackets` ajouté ici ne doit pouvoir atteindre ce défaut.
2. **Lecteur** : ajouter `TryReadPutonCard` à `Game/Network/Packets/Game/GameActionPackets.cs` sur
   le modèle exact de `TryReadPutonItem` (`:149-163`) : garde de longueur
   `HeaderSize + 5` (= 12), `Position = (sbyte)packet[HeaderSize]`,
   `ItemHandle = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 1, 4))`, et
   accepter `packet.Length >= 12` (§3.3).
3. **Service** : un `CardService`/`SocketService` (ou une extension de `EquipmentService`) avec,
   dans l'ordre : emplacement de port valide (`IsWearableSlot`, `EquipmentService.cs:115-118`),
   objet trouvé et appartenu au personnage (`CharacterService.GetItemByHandleAsync`, `:204`),
   nombre de chasses du modèle lisible (`ItemResourceEntity.SocketCount`, `Game/DataAccess/Entities/Arcadia/ItemResourceEntity.cs:26`,
   alimenté par `ArcadiaResourcesMappingProfile.cs:48` ← colonne `socket` de la ressource),
   châsse libre, nature de la carte (pierre d'âme : `ItemGroup.Soulstone = 93`,
   `ItemType.Soulstone = 401`, `ItemBaseType.Soulstone = 7` — `Game/DataAccess/Entities/Enums/ItemGroup.cs:27`,
   `ItemType.cs:43`, `ItemBaseType.cs:12`), règle « au plus 2 pierres identiques »
   (§5.1 point 3), consommation de la carte (`CharacterService.ConsumeItemAsync`, `:270`), écriture
   de `SocketItemIds` en base (`Game/DataAccess/Entities/Telecaster/ItemEntity.cs:35`, longueur
   maximale 4 imposée par `TelecasterContext.cs:55`) — **cette étape est conditionnée par
   l'arbitrage de `position` (§7.1)**.
4. **Réponse** :
   - **refus** : `SendResult((ushort)GamePackets.TM_CS_PUTON_CARD, (ushort)ResultCode.X)`
     (`GameClient.cs:56-60`) → `TM_SC_RESULT` (`GamePackets.cs:5`), structure
     `TS_SC_RESULT` = `uint16 request_msg_id`, `uint16 result`, `int32 value`
     (`reference/rzu/librzu/src/packets/GameClient/TS_SC_RESULT.h:7-10`, id `0`/`1000` `:12-14`).
     Codes disponibles : `Success = 0`, `NotExist = 1`, `AccessDenied = 6`, `DBError = 8`,
     `NotEnoughMoney = 10`, `InvalidArgument = 28` (`Game/Network/Packets/ResultCode.cs:6-37`).
     Le client 7.3 sait lire ce `TS_SC_RESULT` avec `request_msg_id = 214` (chemin par défaut du
     gestionnaire `VA 0x66db80`, §2.3 point 2) : lui en envoyer un n'est pas nuisible.
   - **succès** : le contenu des châsses ne parvient au client **que** par la fiche d'objet —
     `WriteInventoryItem` (`Game/Network/Packets/Game/GameCharacterPackets.cs:341-366`) écrit déjà
     les 4 châsses en `int32` aux offsets de charge utile **38, 42, 46, 50** depuis
     `item.SocketItemIds` (`:353-356`), dans une fiche de **85 octets** (`:21`) ; c'est cohérent
     avec `TS_ITEM_SOCKETS` de rzu (`_(array)(int32_t, socket, 4)`,
     `reference/rzu/librzu/src/packets/GameClient/TS_SC_INVENTORY.h:7-10`, utilisé dans
     `TS_ITEM_BASE_INFO` `:88` sous `version < EPIC_9_8_1` — donc bien présent à 7.3, et sans le
     `dummy_socket` de `version < EPIC_6_1` : exactement 4 × `int32`). Il faut donc **renvoyer la
     fiche de l'objet modifié**, comme le fait déjà `EquipmentService.UnequipAsync`
     (`EquipmentService.cs:102-105` : `SendItemWear(...)` = `BuildItemWearInfo` `:76,130-133`, puis
     `BuildWearInfo` `:92`) pour un objet porté, ou `InventoryService.SendInventory` →
     `BuildInventory` (`InventoryService.cs:125-130`) pour un objet resté dans le sac — le tout
     confirmé par la référence, qui renvoie un `TS_SC_INVENTORY` d'un seul objet après sertissage
     (§5.1 point 3). Une réponse `TM_SC_RESULT(214, Success)` en complément est cohérente avec le
     reste du dépôt (et avec la référence, `WorldSession.cpp:1586`), mais **son besoin pour `214`
     n'est pas démontré** (§7.3).

**Point d'attention bloquant pour l'implémentation** : les points 1 et 2 (énumération, dispatch,
lecteur d'offsets, tests) sont **indépendants** de la sémantique de `position`. Les points 3 et 4
(la logique, donc les refus) ne le sont pas : implémenter la logique sur la mauvaise lecture
conduit à refuser des trames correctes. **L'arbitrage de §7.1 doit être tranché avant d'écrire la
logique de service.**

---

## 6. Écarts assumés avec NGemity

1. **Type de `position`** : NGemity déclare `int8_t` sans gating
   (`shared/Server/Packets/GameClient/TS_CS_PUTON_CARD.h:7`) ; rzu dit `int8` pour
   `< EPIC_9_6_7` et `int32` au-delà (`TS_CS_PUTON_CARD.h:7,9`). Nous suivons **rzu** (la référence
   de tailles et d'ordre), donc `int8` à 7.3 ; l'en-tête NGemity est en retard pour la borne
   `9.6.7`, il ne la contredit pas pour 7.3.
2. **Silence puis reprise** : NGemity ne traite pas `214`/`215` ; nous implémentons `214`. Tout ce
   que nous transposons vient de sa logique de **sertissage par PNJ** (`onSoulStoneCraft`, `260`),
   qui n'est pas le même paquet.
3. **Ce que nous ne portons pas de `onSoulStoneCraft`** :
   - la garde d'état de fenêtre PNJ (`GetLastContactLong("SoulStoneCraft") == 0`, `:1499-1500`) :
     elle suppose un PNJ ouvert côté serveur, notion absente du dépôt (aucun PNJ « sertisseur ») et
     sans objet pour un paquet déclenché par une fenêtre cliente ;
   - le coût (prix/10 en argent, `WorldSession.cpp:1550-1559`, prélevé et **non remboursé**) : les
     textes du client 7.3 parlent d'un `Price:` (`ui_text_6487`) et d'un `cost` (`smsg_soket02`),
     mais rien n'établit qu'il s'applique au chemin `214` (voir §7.3) ;
   - le traitement par lot des 4 châsses en un seul paquet (`soulstone_handle[4]`) : impossible ici,
     `214` ne porte **qu'un** handle de carte.
4. **Ce que nous portons** : la règle « au plus 2 pierres identiques sur une arme à 4 châsses »
   (corroborée par `smsg_soket05`) et la nature « pierre d'âme » de la carte (`smsg_soket03`).
5. **Représentation en base** : NGemity écrit 6 colonnes `socket_0`…`socket_5`
   (`Item.cpp:138-143`) là où Navislamia ne modélise que 4 châsses utiles
   (`ItemEntity.SocketItemIds`, `TelecasterContext.cs:55`) et n'en sérialise que 4
   (`GameCharacterPackets.cs:353-356`). Nous restons à 4 : c'est ce que `TS_ITEM_SOCKETS` de rzu
   déclare (`_(array)(int32_t, socket, 4)`, `TS_SC_INVENTORY.h:7-10` — le `dummy_socket` de 2
   `int32` ne vaut que pour `version < EPIC_6_1`, donc pas à 7.3) et ce que le client 7.3
   numérote (`Socket 1`…`Socket 4`).

---

## 7. `NON ÉTABLI`

### 7.1 Sémantique de `position` — **la question d'arbitrage principale**

Aucune source locale ne décide de ce que `position` désigne. Trois lectures sont compatibles avec
la taille (1 octet) ; une quatrième est réfutable :

- **R1 — index de châsse (0..3) dans l'objet visé.** Favorisée par le modèle de châsse
  (`TS_ITEM_SOCKETS` = 4 `int32`, `reference/rzu/librzu/src/packets/GameClient/TS_SC_INVENTORY.h:7-10` ;
  `SocketItemIds[4]`, `TelecasterContext.cs:55`) et par la numérotation cliente `Socket 1`…`Socket 4`
  (`ui_text_6483-6486`, `db_string.rdb` l. 112381-112388). **Réfutée pour le couple** : ni `214`
  (qui identifie alors la carte par `item_handle` mais **pas** l'objet à sertir) ni `215` (qui ne
  porte que `position`) ne permettraient d'identifier l'objet visé, sauf à supposer un **état
  client → serveur absent du dépôt** (objet/châsse « ouvert » en cours). Si cette lecture était la
  bonne, elle impliquerait un paquet de sélection préalable inconnu — c'est précisément le
  « conteneur ou état client vers serveur absent du dépôt » signalé par le PO.
- **R2 — index d'emplacement de port (`ItemWearType`, 0..23) de l'équipement visé ; `item_handle`
  = la carte déplacée. — lecture retenue, sous réserve.** Arguments : la paire voisine `200`/`201`
  utilise l'**espace de port** et non un index de sac (`onPutOnItem` → `Puton((ItemWearType)position)`,
  `WorldSession.cpp:636` ; `onPutOffItem` → `GetWornItem((ItemWearType)position)` `:665`,
  `Putoff(...)` `:670` ; côté dépôt `IsWearableSlot` + `UnequipItemAsync(..., (ItemWearType)position)`,
  `EquipmentService.cs:115-118 et :94`) ; `201` (`PUTOFF_ITEM`) identifie son objet **par `position`
  seule**, exactement comme `215` — la symétrie `200`↔`214` et `201`↔`215` est frappante
  (`TS_CS_PUTON_ITEM.h:7-12` vs `TS_CS_PUTON_CARD.h:5-9` ; `TS_CS_PUTOFF_ITEM.h:7-11` vs
  `TS_CS_PUTOFF_CARD.h:5-6`) ; `214`/`215` **n'ont pas de `target_handle`**, contrairement à
  `200`/`201` (`TS_CS_PUTON_ITEM.h:10-11`) — donc pas de cible invocation, l'objet visé est celui
  du joueur, et un index de port y suffit pour le désigner ; le client fournit des icônes de châsse
  **par type d'équipement** (`icon_socket_armor|helm|weapon|boots|glove|shield`, dump
  `l. 21802-21807`), ce qui colle à un écran raisonnant en emplacements d'équipement.
  Conséquence assumée de R2 : le sertissage ne porterait que sur un objet **effectivement porté**.
- **R3 — index de conteneur (sac) de l'objet visé ; `item_handle` = la carte.** Même forme que R2,
  espace d'index différent (`ItemEntity.Idx`, `ItemEntity.cs:25`). Non exclue : la colonne est
  commentée comme « place de l'objet dans le conteneur », et `TM_CS_CHANGE_ITEM_POSITION` (`218`)
  nomme « position » un index d'inventaire.

Ce qui **déciderait** entre R1/R2/R3, et qui n'est pas accessible ici : soit une capture/décodage
réel d'un sertissage (objet porté vs objet dans le sac, châsse choisie), soit le site d'émission
côté client. Tant que ce n'est pas tranché, la logique de §5.3 points 3-4 est à écrire sur la
lecture R2 **en isolant la décision dans une fonction nommée** (par exemple
`ResolveCardTarget(byte position, uint itemHandle)`), pour qu'un arbitrage ultérieur ne coûte
qu'un seul point de modification.

### 7.2 Preuve d'émission et geste exact

Le client 7.3 **nomme** `214`/`215` (§2.1) et porte tout le corpus du sertissage (§2.2), mais :
aucun constructeur de trame portant l'id `214` en dur n'a été trouvé, aucun libellé dédié dans le
gestionnaire `TM_SC_RESULT`, aucune classe d'interface ni message interne propre à `214` (§2.3).
Le câblage `window_Socket.nui` → `214` et le **geste exact** (glisser la pierre sur une châsse,
bouton de la fenêtre, boîte de confirmation `smsg_soket01`) restent `NON ÉTABLI`. Question précise :
*une capture décodée d'un sertissage de pierre d'âme en 7.3 montre-t-elle bien l'opcode 214 (et non
260/262) ?* À défaut, la fiche ne prétend pas que le client émet `214`.

### 7.3 Coût, et paquets de réponse attendus au succès

- **Coût** : `smsg_soket02` (`Socketing cost is …`) et `smsg_soket09` (`You don't have enough Lak.`)
  montrent un coût côté client, et NGemity facture prix/10 ; mais rien n'établit que ce coût
  s'applique au chemin `214` plutôt qu'au service PNJ (`260`/`262`). À trancher : *prélève-t-on de
  l'argent (ou du Lak) sur `214`, et selon quelle table ?*
- **Réponse de succès** : aucun paquet serveur → client n'existe pour cette famille dans rzu
  (§5.2). L'état des châsses ne voyagent que par la fiche d'objet (§5.3 point 4). À trancher :
  *faut-il, en plus du renvoi de la fiche (`TM_SC_ITEM_WEAR_INFO` / `TM_SC_INVENTORY`), envoyer un
  `TM_SC_RESULT(214, Success)` ?* Le client sait le lire (chemin par défaut) mais aucun texte dédié
  n'y est associé (§2.3 point 2).

### 7.4 Taille réellement émise par le client

12 octets selon rzu + NGemity, mais la trame effectivement construite par le client pour `214`
n'a pas pu être mesurée ; le précédent `281` (trame cliente plus longue que la définition rzu)
justifie la tolérance de lecture recommandée en §3.3. À trancher : *le client émet-il exactement 12
octets ?*

### 7.5 Le couple `214`/`215`

`214` et `215` forment fonctionnellement un couple (sertir / retirer) comme `200`/`201`, mais
**cette chaîne ne couvre que `214`**. Corollaire à respecter : ne pas ajouter `TM_CS_PUTOFF_CARD`
à `GamePackets` dans cette chaîne, car un membre d'énumération sans bras de dispatch violerait le
critère d'acceptation 4 ; sa fiche et son implémentation viendront de la carte `215`.

### 7.6 Ce qui n'est PAS incertain (pour éviter une relecture inutile)

- l'id `214` à 7.3 : tranché par rzu **et** par la table `id → nom` du client (§2.1) ;
- l'ordre et la taille des champs : tranchés par rzu (§3.2), corroborés par NGemity et par le
  savoir déjà consigné sur `200`/`201` (`CLAUDE.md:430-431`) ;
- l'absence de gestionnaire NGemity : vérifiée exhaustivement sur la table de handlers ;
- l'emplacement du contenu des châsses dans la fiche d'objet (offsets 38/42/46/50, 4 × `int32`) :
  déjà implémenté dans le dépôt (`GameCharacterPackets.cs:353-356`).

---

## 8. Commits et binaires épinglés

### 8.1 `reference/rzu` — HEAD `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02)

Fichiers utilisés :

| Fichier | Lignes | Objet |
|---|---|---|
| `librzu/src/packets/GameClient/TS_CS_PUTON_CARD.h` | 5-9, 11-13, 15 | structure, gating, id |
| `librzu/src/packets/GameClient/TS_CS_PUTOFF_CARD.h` | 5-10, 12 | jumeau `215` |
| `librzu/src/packets/GameClient/TS_CS_PUTON_ITEM.h` | 7-12, 14-16 | modèle du couple `200`/`201` |
| `librzu/src/packets/GameClient/TS_CS_PUTOFF_ITEM.h` | 7-11 | idem |
| `librzu/src/packets/GameClient/TS_CS_SOULSTONE_CRAFT.h` | 5-11 | sertissage par PNJ (`260`/`1260`) |
| `librzu/src/packets/GameClient/TS_SC_RESULT.h` | 7-14 | réponse de refus |
| `librzu/src/lib/Packet/GameTypes.h` | 40 | `ar_handle_t` = `uint32_t` |
| `librzu/src/packets/GameClient/TS_SC_INVENTORY.h` | 7-10, 88 | `TS_ITEM_SOCKETS` = 4 `int32`, présent à 7.3 |
| `librzu/src/lib/Packet/PacketDeclaration.h` | 512-522 | sémantique `_(def)` / `_(impl)` (ordre des champs) |
| `librzu/src/lib/Packet/PacketEpics.h` | 59, 96, 104 | `EPIC_7_3`, `EPIC_9_6_3`, `EPIC_9_6_7` |

Historique des deux en-têtes (utile pour comprendre le gating) :

| Commit | Date | Sujet |
|---|---|---|
| `d7c58ee6` | 2017-02-07 | *Adjust count management for packets and add all known GS packets as of 9.4* — création des deux fichiers |
| `05bc2d82` | 2020-03-29 | *Packets: Use strong typedef for handles and game time values* (`ar_handle_t`) |
| `39300afe` | 2020-04-20 | *Packets: `#undef <packet>_DEF`…* (cosmétique) |
| `16686583` | 2020-04-22 | *Replace `#ifndef/#define` … with `#pragma once`* (cosmétique) |
| `11f2b6fd` | 2020-08-11 | *packets: use versionned ID for all packets and update their ID with epic 9.6.3* — `X(214/<9.6.3)`, `X(1214/≥9.6.3)`, idem `215`/`1215` |
| `54416f87` | 2022-04-04 | *update 9.6.7 packets after late detection of issues* — introduit `_(def)`/`_(impl)` et `int32` pour `≥ EPIC_9_6_7` (`TS_CS_PUTON_CARD` **uniquement**, pas `PUTOFF_CARD`) |

### 8.2 `reference/ngemity` — HEAD `38ceb2c6065fabf6ff4ba71d52f955f362c6c839`

| Fichier | Lignes | Objet |
|---|---|---|
| `shared/Server/ClientPackets.h` | 79-80 | ids `214`/`215` |
| `shared/Server/Packets/GameClient/TS_CS_PUTON_CARD.h` | 6-10 | structure (ordre et types) |
| `shared/Server/Packets/GameClient/TS_CS_PUTOFF_CARD.h` | 6-9 | jumeau |
| `shared/Server/XPacket.h` | 128, 130 | inclusion |
| `Chihiro/src/Network/GameNetwork/WorldSession.cpp` | 120-162 | table des handlers (aucun handler carte) |
| | 616-646, 648-673 | `position` = `ItemWearType` pour `200`/`201` |
| | 1497-1580 | `onSoulStoneCraft` (`260`) : gardes, coût, règle des 2 pierres |
| | 131-134 | handlers voisins (`onSoulStoneCraft`, `onBindSkillCard`, `onUnBindSkilLCard`) |
| `Chihiro/src/Entities/Item/ItemTemplate.hpp` | 388 | nombre de chasses (`socket`) |
| | 297, 329, 356 | `CLASS_SOULSTONE`, `TYPE_SOULSTONE`, `GROUP_SOULSTONE` |
| `Chihiro/src/Entities/Item/Item.cpp` | 26-27, 57, 99-102, 138-143 | 4 chasses sur l'instance |
| | 227-234, 317-324 | usage des chasses (endurance, UID d'invocation) |
| `Chihiro/src/Network/Messages.h` / `.cpp` | 85-86 / 936-946 | fenêtres de sertissage/réparation |
| `Chihiro/src/Network/Messages.cpp` | 250-260 | `SendItemMessage` : renvoi d'un `TS_SC_INVENTORY` d'un seul objet après sertissage |

### 8.3 Dépôt Navislamia — base `master` `ec76b218cd0bd7c6498d725f253abb8b431f0cd6`

| Fichier | Lignes | Objet |
|---|---|---|
| `op_codes.md` | 63-64 | noms `TM_CS_PUTON_CARD` / `TM_CS_PUTOFF_CARD` |
| `Game/Network/Packets/Enums/GamePackets.cs` | 5, 26-27, 30, 36-40 | `TM_SC_RESULT`, voisins présents, `214`/`215` absents |
| `Game/Network/Packets/Header.cs` | 9-11, 22-24 | en-tête 7 octets |
| `Game/Network/Packets/Game/GameActionPackets.cs` | 8, 149-163, 165-178 | `HeaderSize`, modèle de lecteur, écart `200`/`201` |
| `Game/Network/Packets/Game/GameCharacterPackets.cs` | 19-21, 76, 92, 325-366 | fiche d'objet 85 o, châsses aux offsets 38/42/46/50 |
| `Game/Network/Clients/GameClient.cs` | 56-60, 356, 374, 447, 662, 698, 704, 796-802 | `SendResult`, handlers voisins, chaîne de dispatch, `switch` terminal |
| `Game/Services/EquipmentService.cs` | 77-121, 130-133 | `position` = `ItemWearType`, renvoi de fiche |
| `Game/Services/InventoryService.cs` | 88-130 | tri et renvoi d'inventaire |
| `Game/Services/CharacterService.cs` | 159, 176, 204, 236, 270, 288, 334 | primitives d'objet |
| `Game/DataAccess/Entities/Telecaster/ItemEntity.cs` | 25, 34, 35 | `Idx`, `WearInfo`, `SocketItemIds` |
| `Game/DataAccess/Contexts/TelecasterContext.cs` | 55 | `SocketItemIds` max 4 |
| `Game/DataAccess/Entities/Arcadia/ItemResourceEntity.cs` | 26 | `SocketCount` |
| `MigrateDatabase/Mappers/ArcadiaResourcesMappingProfile.cs` | 48 | `SocketCount` ← colonne `socket` |
| `Game/DataAccess/Entities/Enums/ItemGroup.cs` | 27 | `Soulstone = 93` |
| `Game/DataAccess/Entities/Enums/ItemType.cs` | 43 | `Soulstone = 401` |
| `Game/DataAccess/Entities/Enums/ItemBaseType.cs` | 12 | `Soulstone = 7` |
| `Game/Network/Packets/ResultCode.cs` | 6-37 | codes de refus |
| `CLAUDE.md` | 430-431 | savoir déjà consigné sur `200`/`201` |
| `.gitignore` | 473 | `!/docs/packet-specs/` |

### 8.4 `reference/client73` — binaires lus

`SFrame.exe` sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` ;
`db_string.rdb` sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1`.
Méthode : lecture statique seule (`strings`, `objdump -h`, `objdump -d -j .text`, `objdump -s`) ;
aucune exécution de `SFrame.exe`, de Lua ou de script du client. Points d'entrée cités :
VA `0x6752de` (fonction de construction de la table `id → nom`), VA `0x676683`/`0x676685`/`0x6766b9`/`0x6766d8`
(entrée `214`), VA `0x6766f9`/`0x6766fb`/`0x67672f`/`0x67674e` (entrée `215`), VA `0x66db80`
(gestionnaire `TM_SC_RESULT`), VA `0x66e2d8` (table d'octets), VA `0x66e290` (table de pointeurs,
18 blocs), VA `0x66e258` (bloc par défaut), `.rdata` VA `0xa53710`/`0xa536fc` (noms).

---

## 9. Note de livraison

Livrable de cette carte : cette fiche, et **rien d'autre** — aucun fichier de code, aucun test,
aucune modification de `CLAUDE.md`. Branche `hermes/packet-214-puton-card` créée depuis
`master` (`ec76b218cd0bd7c6498d725f253abb8b431f0cd6`), fiche committée, `master` local intact
(`git log --oneline origin/master..master` vide).

Ce qui a été ajouté au cadrage PO, après vérification :

- `docs/packet-specs/` existe déjà sur `master` (4 fiches) : rien à créer ;
- `ItemEntity.SocketItemIds` **a** un consommateur d'exécution dans le dépôt :
  `GameCharacterPackets.cs:353-356` (sérialisation des 4 châsses dans la fiche d'objet). Le
  cadrage PO indiquait l'inverse ; c'est important car cela fournit gratuitement le canal de
  réponse au succès (§5.3 point 4) ;
- les numéros de ligne cités par le PO pour le dispatch (`GameClient.cs:378,451,696,702`)
  correspondent en réalité à `374` (`HandleArrangeItemAsync`), `447`
  (`HandleChangeItemPositionAsync`), `698` et `704` (bras de dispatch) ; les deux handlers
  existent bien ;
- NGemity **consomme** ses châsses (`Item.cpp:227-234`, `:317-324`) : ce sont les paquets `214`/`215`
  qui n'ont aucun consommateur, pas le modèle de châsse ;
- le gating `EPIC_9_6_7` de `position` provient d'un correctif **tardif** de rzu (`54416f87`,
  2022-04-04) : la valeur 7.3 (`int8`) est stable depuis 2017, seule la borne haute a bougé.

Ce que la fiche demande au dev de ne pas improviser : la sémantique de `position` (§7.1) et la
preuve d'émission (§7.2). Les points d'implémentation qui ne dépendent pas de ces arbitrages
(§5.3 points 1-2, tests d'offsets) peuvent être livrés sans attendre.

Aucune vérification d'exécution n'a été possible ni tentée : pas de PostgreSQL, pas de serveur de
jeu, pas de client exécuté. Les seules commandes lancées sont des lectures (`git`, `grep`,
`strings`, `objdump`, `sha256sum`) et `dotnet` n'a pas été utilisé, le livrable n'étant pas du code.

**Bloc pour `CLAUDE.md`** : non rédigé ici (consigne explicite de la carte 214 — `CLAUDE.md` est
protégé et son écriture est refusée). Les faits à y consigner, si le PO souhaite le faire porter par
la description de la MR : `TM_CS_PUTON_CARD` = `214`, **12 octets**, `position` `int8` @7 puis
`item_handle` `uint32` @8 ; gating `EPIC_9_6_3` (id, sans effet à 7.3) et `EPIC_9_6_7` (`position`
passe à `int32` — donc `int8` à 7.3) ; le jumeau `215` = **8 octets** ; le contenu des 4 châsses ne
voyage que dans la fiche d'objet (`GameCharacterPackets.cs:353-356`).

---

## 10. A VERIFIER PAR KILLIAN

1. **Sémantique de `position` (§7.1)** — arbitrage bloquant pour la logique de service. Trois
   lectures restent ouvertes : index de châsse (`R1`, réfutée sauf état client→serveur inconnu),
   index d'emplacement de port de l'équipement porté (`R2`, lecture retenue sous réserve),
   index de sac (`R3`). Si tu disposes d'une capture ou d'un souvenir du jeu (« le sertissage de
   pierre d'âme se fait-il uniquement sur un objet **porté** ? »), cela suffit à trancher ; sinon
   la chaîne devra livrer la logique derrière un point de décision unique et nommé.
2. **Preuve d'émission de `214` (§7.2)** — le client 7.3 nomme le paquet et porte tout le corpus
   du sertissage, mais aucun site d'émission n'a été trouvé ; une capture décodée d'un sertissage
   confirmerait (ou infirmerait) que c'est bien `214`, et non la famille `260`/`262`, que la
   fenêtre de châsse émet.
3. **Coût et refus** (§7.3) — le client parle d'un prix (`smsg_soket02`) et d'un manque de Lak
   (`smsg_soket09`) : faut-il prélever quelque chose sur `214`, et selon quelle règle ?
4. **Réponse de succès** (§7.3) — renvoi de la fiche d'objet seul, ou accompagné d'un
   `TM_SC_RESULT(214, Success)` ?
5. **Périmètre du couple** (§7.5) — confirmation que `215` reste hors de cette chaîne (sa fiche
   est une carte distincte du board), pour ne pas ajouter un membre d'énumération sans dispatch.
