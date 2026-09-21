# 258 — TM_CS_DONATE_ITEM

Direction : **client → serveur**. Aucun pendant dédié n'existe : `rzu` ne déclare, dans la famille
`donate`, que deux paquets montants — `TS_CS_DONATE_ITEM (258)` et `TS_CS_DONATE_REWARD (259)`
(`librzu/src/packets/GameClient/`). La réponse passe donc par l'accusé générique `TM_SC_RESULT (0)` et
par les mises à jour d'état déjà émises par le dépôt (§5.3).

Toute preuve issue de `reference/client73/` vient d'une **lecture statique** (`strings`, `objdump -d`)
du client Epic 7.3 : aucun binaire client, aucun Lua, aucun script du client n'a été exécuté. Les
numéros de ligne cités pour les binaires sont ceux d'un dump `strings -n 4` (vérifié identique à
`strings -a` par `cmp` sur les deux fichiers) ; les identifiants stables sont les sha256 (§8) et les
adresses virtuelles (VA) du désassemblage, reproductibles avec
`objdump -d -M intel --start-address=<VA> --stop-address=<VA+0x90> SFrame.exe`.

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id décimal | `258` (`0x0102`) | `op_codes.md:85` — `[258] = "TM_CS_DONATE_ITEM"` |
| nom | `TM_CS_DONATE_ITEM` | `op_codes.md:85` |
| paquet frère (montant) | `TM_CS_DONATE_REWARD = 259` | `op_codes.md:86` ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_DONATE_REWARD.h:13-16` |
| déclaration rzu | `TS_CS_DONATE_ITEM` — charge utile `int64 gold`, `int32 jp`, `count int8 items`, `dynarray TS_DONATE_ITEM_INFO items` | `reference/rzu/librzu/src/packets/GameClient/TS_CS_DONATE_ITEM.h:13-23` |
| structure d'élément | `TS_DONATE_ITEM_INFO` — `ar_handle_t handle`, `int64 count` | `.../TS_CS_DONATE_ITEM.h:5-10` ; largeur du handle : `librzu/src/lib/Packet/GameTypes.h:40` (`ar_handle_t : strong_typedef<ar_handle_t, uint32_t>`) |
| en-tête | 7 octets (`uint32 Length`, `uint16 ID`, `uint8 Checksum`) | `reference/rzu/librzu/src/lib/Packet/PacketDeclaration.h:616-618` (`CREATE_PACKET_VER_ID` → `CREATE_STRUCT_IMPL(name_, 7, …)`) ; dépôt : `Game/Network/Packets/Header.cs:7-13` |
| présence dans l'enum du dépôt | **absente** — `GamePackets.cs` saute de `TM_CS_ARRANGE_ITEM = 219` (l. 40) à `TM_CS_USE_ITEM = 253` (l. 41), puis à `TM_EQUIP_SUMMON = 303` | `Game/Network/Packets/Enums/GamePackets.cs:40-41` |
| id 259 dans l'enum | **absent** également | `Game/Network/Packets/Enums/GamePackets.cs` (0 occurrence de `DONATE`) |
| dispatch montant | aucun bras : le `switch` final lève `Unknown Packet Type` | `Game/Network/Clients/GameClient.cs:791` (switch), `:802` (repli `_ => throw …`) ; dernier bras avant le `switch` : `:785` (`TM_CS_LOGOUT`) |
| accusé générique | `TM_SC_RESULT = 0`, émis par `GameClient.SendResult(ushort id, ushort result, int value = 0)` | `Game/Network/Packets/Enums/GamePackets.cs:5` ; `Game/Network/Clients/GameClient.cs:56-60` |

Le dev doit donc ajouter `TM_CS_DONATE_ITEM = 258` **et** son bras de traitement dans le même commit
(critère d'acceptation 4). Le 259 ne fait pas partie de cette fiche : il est déclaré ici comme
contexte, pas livré.

## 2. Ce que le joueur fait pour que le client l'envoie

L'émission par ce client est **prouvée par lecture du chemin d'envoi** (et non seulement déduite d'un
nom dans une table) : le constructeur de trame écrit l'id `0x0102` dans l'en-tête, calcule une longueur
qui vaut exactement `20 + 12 × nombre d'objets`, puis appelle la méthode `Send` virtuelle de la session
et libère le tampon. Contrairement au 253 (fiche `253-use-item.md`, §7.1), il n'y a ici **aucune
présomption** : le geste (fenêtre de don), le message d'action interne et la trame sont tous les trois
identifiés.

| Élément | Contenu | Source (dump `strings -n 4`) |
| --- | --- | --- |
| fenêtre de don | `window_donation_main.nui` | `SFrame.exe` l. 24847 |
| fenêtre de classement | `window_donation_ranking.nui`, `window_donation_msgbox_rankingitem.nui`, `window_donation_msgbox.nui` | `SFrame.exe` l. 24759, 24760, 24782 |
| contrôles de la fenêtre | `donation_input_text`, `button_jp_donation`, `static_altar_bless`, `contribution_ok`, `button_donation`, `button_donation_item`, `button_donation_cancel`, `button_donation_ranking`, `button_donation_rupy` | `SFrame.exe` l. 23886, 23888, 23889, 23916, 23934, 23936, 23937, 23935, 23938 |
| icônes de récompense | `icon_donation_reward_gold` / `_silver` / `_copper` / `_gray`, à côté des paliers `30000`, `10000`, `5000`, `1000` et du libellé `#@moralpoint@#` | `SFrame.exe` l. 23917-23925 |
| message d'action interne | classe `SIMSG_UI_ACT_ITEMCONTRIBUTION` (manglé `.?AUSIMSG_UI_ACT_ITEMCONTRIBUTION@@`) | `SFrame.exe` l. 44194 (table RTTI) |
| clé du message | `0x46A` = **1130**, écrite à `[this+4]` par son constructeur | `SFrame.exe` VA `0x5EF8B0` (`mov DWORD PTR [eax+4],0x46A`) |

Chaîne complète, dans l'ordre :

1. **Le joueur clique le bouton de don** de la fenêtre `window_donation_main.nui`. La fonction de
   gestion de clic (VA `0x5F5F40`-`0x5F61B6`) compare le nom du contrôle cliqué aux chaînes `VA
   0xA3D1CC` = `button_donation`, `0xA3D1DC` = `button_donation_ranking`, `0xA3D1F4` =
   `button_donation_item` (lecture directe des chaînes pointées par le code, `SFrame.exe` l. 23934-23936).
2. **Le bras `button_donation` construit le message d'action 1130**, dans les deux configurations :
   sélection d'objets vide (`call 0x5EF8B0` à `VA 0x5F6062`, puis copie de l'`int64` de la fenêtre aux
   offsets `+0x4C0`/`+0x4C4` et de l'`int32` à `+0x4C8` vers les offsets `+0x23`/`+0x27`/`+0x2B` du
   message, `VA 0x5F6067-0x5F6093`) et sélection non vide (`call 0x5EF8B0` à `VA 0x5F60C3`, puis
   parcours du vecteur d'objets sélectionnés de la fenêtre — `+0x4AC` → `+0x4B0`, pas de `0x10` — pour
   remplir la liste d'objets du message, `VA 0x5F60C8-0x5F6133`). Le message est ensuite passé à
   `call 0x6491C0` avec le pointeur du message en argument.
3. **Le routeur de messages du client** dirige la clé 1130 vers le gestionnaire qui sérialise la trame
   258 (`VA 0x49E21D` : `sub eax,0x403` ; `cmp eax,0xDE` ; `movzx eax, BYTE PTR [eax+0x49EA50]` ;
   `jmp DWORD PTR [eax*4+0x49E98C]`). La table d'octets d'index (`.rdata` `0x49EA50`) donne
   l'indice `25` pour la clé `0x46A` — clé **unique** pour ce stub, ce qui
   est le contrôle de méthode : la lecture n'est pas ambiguë, chaque entrée de la table de sauts n'est
   atteinte que par une seule clé (les clés non routées tombent sur l'octet d'indice `0x30` = 48).
   L'entrée `25` de la table de sauts (`.rdata` `0x49E98C`) est le stub `0x49E2CD`, qui appelle
   `0x48E3F0` — le constructeur/émetteur de la trame 258.
4. **Le constructeur 258** (`VA 0x48E3F0`) **n'émet rien si tout est vide** : les objets, l'`int64`
   or et l'`int32` jp sont testés et un `je` sort de la fonction sans envoyer de trame
   (`VA 0x48E3FE-0x48E419`). Un simple clic sans montant ni objet ne produit donc aucun paquet.

Les autres contrôles de la même fenêtre ne produisent pas de 258 : `button_donation_item` construit un
objet message avec la valeur `0x77` = 119 puis l'affiche (`VA 0x5F5F77-0x5F5F9F`),
`button_donation_ranking` fait de même avec `0x8F` = 143 (`VA 0x5F5FC9-0x5F5FF3`, avant d'ouvrir la
fenêtre de classement), `button_jp_donation` positionne le mode `[window+0x4A0] = 3` et affiche un
message (`VA 0x5F5EC2-0x5F5EF2`). Le geste qui déclenche réellement l'émission du 258 est donc **le clic
sur le bouton de don, une fois le montant et/ou les objets choisis** — le champ `donation_input_text`
n'est pas relié par la lecture aux offsets `+0x4C0…+0x4C8` (§7.5). La nature exacte de ces trois
messages d'erreur/ouverture n'est pas établie (seule la valeur passée est lue).

Textes du client, dans `reference/client73/db_string.rdb` (dump `strings -n 4`) :

| Chaîne | Contenu | Ligne |
| --- | --- | --- |
| `ui_text_6744` | `My Rank` | 112870 |
| `ui_text_6745` | `It is over the maximum value of item to donate by one time. Press the 'Donation' button.` | 112872 |
| `ui_text_6746` | `Player cannot acquire item due to loss of moral points.` | 112874 |
| `ui_text_6747` | `Cost: #@moralpoint@# Points.<br>Double-click to increase redemption quantity by 1.…` | 112876 |
| `ui_text_6748` | `You have been very generous. Please visit the Altar of the Goddess for a reward.` | 112878 |
| `ui_text_6749` | `You cannot take the Donation Ranking Item right now.` | 112880 |
| `ui_text_6750` | `Thank you for your generosity. Please check your inventory for your reward.` | 112882 |
| `ui_text_6751` | `Your warehouse is full. Please remove some items before trying to add more.` | 112884 |
| `smsq_contribute_noet_item` | `You can't donate this item.` | 196171-196172 |
| `smsq_contribute_item_not_exist` | `Items needed for donation do not exist.` | 196173-196174 |
| `auto_tooltip_state_base_7800` / `_7801` | `Non-tradable` / `Non-donatable` (infobulles d'objet) | 151268-151270 |

Le nom du paquet (`TM_CS_DONATE_ITEM`) **n'apparaît pas** dans la table d'annotation du binaire
(0 occurrence de `DONATE`/`donate` dans `SFrame.exe`) : cette table est partielle, ce qui est cohérent
avec la ficelle « le nom manque mais le paquet part » déjà relevée pour 219/218. Ici la preuve
d'émission ne repose donc pas sur cette table mais sur le chemin d'envoi ci-dessus.

## 3. Structure sur le fil

**Taille totale attendue : `20 + 12 × N` octets**, où `N` est le nombre d'objets
(`N = 0` → **20 octets** ; `N = 1` → 32 ; `N = 19` → 248).

Cette formule n'est pas une déduction du seul rzu : le constructeur client calcule la longueur de
trame ainsi (`VA 0x48E479` : `lea edx,[eax*4+0x14]` avec `eax = 3 × N` → `12 × N + 0x14`), et écrit
`0x14` (= 20) comme longueur plancher quand il n'y a aucun objet (`VA 0x48E433` :
`mov DWORD PTR [ebp-0x18],0x14`).

| Offset | Taille | Type | Nom | Valeur attendue | Source |
| --- | --- | --- | --- | --- | --- |
| 0 | 4 | `uint32` LE | `Length` | `20 + 12 × N` (longueur totale, en-tête compris) | `Game/Network/Packets/Header.cs:9`, `:22` ; calcul client `VA 0x48E433`, `0x48E479-0x48E483` |
| 4 | 2 | `uint16` LE | `ID` | `258` | `Game/Network/Packets/Header.cs:10`, `:23` ; client `VA 0x48E41B` (`mov edx,0x102`) puis `0x48E42F` (`mov WORD PTR [ebp-0x14],dx`) |
| 6 | 1 | `uint8` | `Checksum` | somme des 6 premiers octets (le client la calcule ; le dépôt ne l'impose pas en réception) | `Game/Network/Packets/Header.cs:11`, `:24` ; calcul client `VA 0x48E43D-0x48E44C` ; `Game/Network/Packets/Game/GameAttackPackets.cs:74-79` |
| 7 | 8 | `int64` LE | `gold` | montant d'or donné ; `0` si le joueur ne donne que des objets ou du jp | `reference/rzu/librzu/src/packets/GameClient/TS_CS_DONATE_ITEM.h:14` ; client : deux `mov` dword aux `VA 0x48E46D` (base+7) et `0x48E476` (base+11) |
| 15 | 4 | `int32` LE | `jp` | montant de jp donné ; champ présent en 7.3 (§4) | `.../TS_CS_DONATE_ITEM.h:15` ; client `VA 0x48E480` (`mov DWORD PTR [ebp-0x9],ecx`, base+15) |
| 19 | 1 | `int8` | `items` (compte du `dynarray`) | nombre d'objets, `0` à `N` ; **signé** (un octet ≥ `0x80` est négatif, §5.3) | `.../TS_CS_DONATE_ITEM.h:16` ; client `VA 0x48E46A` (`mov BYTE PTR [ebp-0x5],al`, base+19) |
| 20 | `12 × N` | `TS_DONATE_ITEM_INFO[]` | `items` | un enregistrement par objet, dans l'ordre envoyé par le client | `.../TS_CS_DONATE_ITEM.h:5-10`, `:17` ; client : boucle de copie `VA 0x48E4CA-0x48E4F4` (`add ecx,0xc` / `add edx,0xc`) |
| 20 + 12×k | 4 | `uint32` LE (`ar_handle_t`) | `items[k].handle` | handle d'instance de l'objet donné | `.../TS_CS_DONATE_ITEM.h:6` ; `librzu/src/lib/Packet/GameTypes.h:40` |
| 24 + 12×k | 8 | `int64` LE | `items[k].count` | nombre d'unités données sur cette pile ; taille `int64` en 7.3 (§4) | `.../TS_CS_DONATE_ITEM.h:7-8` ; client : les trois dword copiés par la boucle à pas de 12 |

Notes de lecture, utiles au dev :

- Le `count` du `dynarray` **pilote** la charge utile : `SERIALIZATION_F_COUNT2` écrit
  `getClampedCount<int8_t>(items.size())` (`librzu/src/lib/Packet/PacketDeclaration.h:343-345`, bornage
  `:83-88`), puis `writeDynArray(type, items, count)` écrit **exactement `count`** enregistrements
  (`librzu/src/lib/Packet/MessageBuffer.h:159-165`). Il n'y a donc pas de longueur d'array séparée, et
  `Length == 20 + 12 × count` est une invariant à vérifier en réception.
- Le constructeur client tronque le nombre d'objets sur l'octet bas (`VA 0x48E467`, `movzx edi,al`)
  avant de calculer la longueur : la trame est cohérente avec elle-même même si la sélection dépasse
  8 bits, ce qui ne doit pas arriver (le client annonce un maximum par don, `ui_text_6745`).
- Aucun `handle` de personnage ni de cible ne figure dans la trame : le donneur est le personnage de
  la session, comme pour les autres paquets d'objet montants.

## 4. Gating de version

Trois gating `rzu` touchent ce paquet. Tous sont **tranchés pour Epic 7.3**, et pour les trois la
lecture du client 7.3 confirme la décision de `rzu` indépendamment de ses tables.

| Champ | Gating rzu | Bornes | Décision pour 7.3 | Confirmation client 7.3 |
| --- | --- | --- | --- | --- |
| id du paquet | `X(258, version < EPIC_9_6_3)`, `X(1258, version >= EPIC_9_6_3)` (`TS_CS_DONATE_ITEM.h:19-21`) | `EPIC_7_3 = 0x070300` < `EPIC_9_6_3 = 0x090603` (`librzu/src/lib/Packet/PacketEpics.h:59`, `:96`) | **258** (pas 1258) | le constructeur écrit l'immédiat `0x102` = 258 dans le champ `ID` (`VA 0x48E41B`) |
| `jp` | `_(simple)(int32_t, jp, version >= EPIC_6_2)` (`:15`) | `EPIC_7_3 = 0x070300` > `EPIC_6_2 = 0x060200` (`PacketEpics.h:55`) | **présent** → 4 octets à l'offset 15 | le client écrit un dword à `base+15` (`VA 0x48E480`) ; sans le champ, l'offset du compte serait 15 et non 19 |
| type du `count` de `TS_DONATE_ITEM_INFO` | `int64` si `version >= EPIC_6_3`, `uint16` sinon (`:7-9`) | `EPIC_7_3` > `EPIC_6_3 = 0x060300` (`PacketEpics.h:56`) | **`int64`** → enregistrement de **12** octets | la boucle de copie avance de `0x0C` par objet et la longueur vaut `12 × N + 20` (`VA 0x48E4CA`, `0x48E479`) ; un `uint16` donnerait des enregistrements de 6 octets et une trame de `17 + 6 × N` |

Nuance à conserver : la borne `EPIC_6_3` est une **correction** tardive du dépôt rzu. Le commit qui a
introduit le gating (`bdd362a6`) portait `EPIC_7_1`, et c'est `c8568e41` qui l'a ramené à `EPIC_6_3`
(§8). À 7.3 les deux bornes donnent le même résultat (`7.3 >= 6.3` et `7.3 >= 7.1` sont vrais), donc
la correction n'a aucun effet sur cette fiche — mais c'est exactement le genre d'écart qui a produit
la moitié des pièges de `CLAUDE.md`, et il doit être cité si un jour le dépôt vise 6.3 à 7.0.

## 5. Traitement attendu

### 5.1 Ce que fait rzu

Rien : `TS_CS_DONATE_ITEM` n'est qu'une déclaration de sérialisation (aucun consommateur, aucun
gestionnaire dans `librzu`). rzu tranche la forme, pas le comportement.

### 5.2 Ce que fait NGemity

Le paquet est **déclaré** — `shared/Server/Packets/GameClient/TS_CS_DONATE_ITEM.h:19`
(`CREATE_PACKET(TS_CS_DONATE_ITEM, 258)`), inclus par `shared/Server/XPacket.h:89`, énuméré par
`shared/Server/ClientPackets.h:98` — mais **il n'est traité nulle part** : aucune occurrence de
`TS_CS_DONATE_ITEM` dans `Chihiro/` (0 résultat), aucun `onDonateItem` dans
`Chihiro/src/Network/GameNetwork/WorldSession.h:58-122`. **Il n'y a donc aucune logique de référence
à porter pour ce paquet**, ni validation, ni réponse, ni effet. La seule convention réutilisable est
celle de la famille : un paquet montant reçoit un accusé
`Messages::SendResult(player, <id reçu>, <résultat>, <valeur>)`
(`Chihiro/src/Crafting/MixManager.cpp:74`, `:87` ; `Chihiro/src/Network/GameNetwork/WorldSession.cpp:1347`).

### 5.3 Ce que le serveur doit faire — périmètre livrable

La trame contient une **offre de valeur** (or, jp, objets) et rien d'autre : ni destinataire, ni cible,
ni formule. Le serveur est donc libre du modèle, mais contraint sur trois points : lire la trame sans
se désaligner, refuser tout ce qui n'est pas vérifiable, et répondre par ce que le client sait
afficher.

1. **Lecture** (`Game/Network/Packets/Game/GameActionPackets.cs`, à côté de `TryReadUseItem`) :
   lire `count` à l'offset 19 **d'abord**, borner la trame à `20 + 12 × count`, puis lire les
   enregistrements. Un `count` **négatif** (octet ≥ `0x80`) est un argument invalide, pas un grand
   nombre. Une trame plus courte que `20 + 12 × count` est refusée sans lecture hors borne.
2. **Validation métier** :
   - chaque `handle` doit être résolu **dans les objets du personnage** (même discipline que les
     autres paquets d'objet : la possession est prouvée par la résolution,
     `Game/Services/ICharacterService.cs:34` — `GetItemByHandleAsync`) → sinon `NotExist` (`ResultCode.cs:8`) ;
   - `gold >= 0`, `jp >= 0`, `count >= 0` par objet ;
   - `gold <= character.Gold` et `jp <= character.Jp` (`Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:54` et
     `:43`) → sinon `NotEnoughMoney` / `NotEnoughJP` (`ResultCode.cs:17`, `:20`). Un client malhonnête
     peut envoyer plus que ce qu'il possède : la borne est côté serveur, pas côté client ;
   - pas de doublon de `handle` dans la même trame (un même objet donné deux fois est soit une
     incohérence, soit une tentative de double-débit) → `InvalidArgument` (`ResultCode.cs:37`).
3. **Réponse** : `SendResult(258, code, valeur)` (`Game/Network/Clients/GameClient.cs:56-60`), sur le
   refus comme sur le succès (convention du dépôt et de NGemity). La valeur proposée par défaut est
   `0` : aucune référence ne la renseigne et `value` est un champ fixe, donc ce choix ne peut pas
   désaligner (§7.2).
4. **Effet** : consommer, puis notifier avec les primitives existantes, dans l'ordre utilisé par
   `ItemUseService` (`Game/Services/ItemUseService.cs:94-95`) :
   - par objet : `BuildDestroyItem(handle)` si la pile est épuisée, sinon
     `BuildUpdateItemCount(handle, reste)` (`Game/Network/Packets/Game/GameCharacterPackets.cs:187`,
     `:195`) ;
   - si `gold > 0` : `BuildGoldUpdate(character.Gold, character.Chaos)`
     (`GameCharacterPackets.cs:229`) ;
   - si `jp > 0` : `BuildExpUpdate(handle, character.Exp, character.Jp)`
     (`GameCharacterPackets.cs:248`) ;
   - persister par `ICharacterService.SaveProgressAsync(characterName, level, jobLevel, exp, jp, gold, …)`
     (`Game/Services/ICharacterService.cs:58`) et/ou `RemoveItemAsync` / `ConsumeItemAsync`
     (`:41`, `:49`).
5. **Le crédit (points moraux, bénédiction, unités de contribution) n'est pas modélisé dans le
   dépôt.** Aucun champ, aucun service, aucune constante : les seules occurrences de `moral` dans
   `Game/` sont `CharacterEntity.ImmoralPoint` (`Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:47`)
   et deux effets d'objet (`AddImmoralPoint`/`SetImmoralPoint`,
   `Game/DataAccess/Entities/Enums/ItemEffectInstant.cs:15-16`), qui relèvent tous deux de
   l'immoralité (PvP) et non du don — c'est une autre notion en 7.3 (§7.1). Le dev **ne doit inventer**
   ni formule de conversion, ni compteur, ni table de récompense. Tant que ce crédit n'est pas
   arbitré, le périmètre honnête est celui ci-dessus : lire, juger, transférer, acquitter — et le
   paquet frère 259 (retrait de récompense) reste hors périmètre.

### 5.4 Question de fond à arbitrer (métier, pas technique)

Si le serveur consomme l'or/jp/objets sans créditer quoi que ce soit et sans 259, un joueur **perd**
de la valeur en cliquant sur ce bouton. L'inverse (refuser systématiquement par `AccessDenied` en
attendant l'arbitrage) ne consomme rien et ne trompe personne, mais laisse le paquet sans effet. Les
deux options sont défendables et documentables ; ce qui ne l'est pas, c'est d'inventer un crédit. Le
choix appartient à Killian et doit figurer dans la description de la MR.

## 6. Écarts assumés avec NGemity

| Point | NGemity | Décision Navislamia | Raison |
| --- | --- | --- | --- |
| Traitement | déclaré, jamais traité (0 occurrence dans `Chihiro/`) | implémenté (lecture, validation, acquittement, transfert) | aucun comportement de référence à porter ; l'implémentation suit les conventions du dépôt (§5.3) |
| **Forme sur le fil** | compilé en `EPIC_4_1_1` (`shared/Common/Define.h:25`) → dans **son** binaire, `jp` (`>= EPIC_6_2`) disparaît et le `count` d'élément devient `uint16` (`>= EPIC_6_3` faux) : enregistrements de 6 octets, trame `17 + 6 × N` (7 en-tête + 8 or + 2 compte) | `20 + 12 × N` | le client Epic 7.3 tranche : longueur `12 × N + 20` et écriture à `base+15` constatées dans le binaire (§3, §4). Le fichier NGemity **ne doit pas** être recopié comme description de la trame 7.3, même si son texte est identique à celui de rzu |
| `handle` d'élément | `uint32_t` | `uint32` (idem) | même largeur, `ar_handle_t` étant un `uint32_t` (`GameTypes.h:40`) : aucune divergence de taille |
| Sémantique du don | inexistante | §5.3, avec le crédit laissé ouvert | NGemity ne modélise ni point moral, ni autel, ni unité de contribution : il n'y a pas d'écart à justifier, seulement une absence |
| Accusé | `Messages::SendResult` pour la famille | `GameClient.SendResult` | même convention, primitive déjà présente dans le dépôt |

## 7. NON ÉTABLI

1. **Modèle de crédit du don.** Ce que le serveur doit *créditer* en échange n'est établi nulle part
   de façon exploitable. Indices convergents, mais insuffisants pour coder une formule :
   `DONATE_GOLD_UNIT_COUNT` **= 10000** est une constante de `S_CONSTANT` du PDB `Game_bin` d'origine
   (`CLAUDE.md:1081`), ce qui suggère une conversion or → « unité » au pas de 10 000 ; la fenêtre 7.3
   affiche les paliers `30000`/`10000`/`5000`/`1000` juste avant `#@moralpoint@#`
   (`SFrame.exe` l. 23921-23925), soit 3 / 1 / 0,5 / 0,1 unité ; le client distingue en outre
   **`moral_point`** (`smsq_moral_point_increase`, `db_string.rdb:197192`) de **`immoral_point`**
   (`smsq_immoral_point_increase`, `:197248`), et le dépôt ne connaît que le second
   (`CharacterEntity.ImmoralPoint`, envoyé comme propriété `immoral`, `Game/Network/Clients/Actions/GameActions.cs:233`).
   À trancher : où va le crédit, à quel taux, et comment le client l'apprend. Rien de tout cela ne
   doit être deviné.
2. **Ensemble de réponses attendu.** rzu ne déclare **aucun** paquet descendant de la famille
   (seuls les deux montants existent) : il n'y a pas de `TS_SC_DONATE_*`. L'accusé générique
   `TM_SC_RESULT` est la convention de la famille, mais rien ne prouve que le client l'exige, ni qu'il
   se contente des 254/255/1001/1003. La valeur de `TM_SC_RESULT.value` pour ce paquet n'est
   renseignée par aucune référence (défaut proposé : `0`).
3. **Bit d'objet interdisant le don.** Le client 7.3 connaît l'interdiction par objet — infobulle
   `Non-donatable` (`auto_tooltip_state_base_7801`, `db_string.rdb:151269-151270`), message
   `You can't donate this item.` (`:196171-196172`) — et le dépôt a `ItemUseFlag.CantDonate = 0`
   (`Game/DataAccess/Entities/Enums/ItemUseFlag.cs:8`). Mais l'enum du dépôt est un `[Flags]` dont les
   membres sont des **index séquentiels** (réserve déjà ouverte dans `253-use-item.md`, §7.6) et les
   libellés client sont eux aussi une énumération séquentielle (`7800`, `7801`, `7802` vide, `7803`…),
   donc ni l'index ni le masque du bit « non donatable » ne sont établis. NGemity ne peut pas aider :
   il n'a aucun `FLAG_*` de don. Aucun refus « non donatable » ne doit être codé avant arbitrage.
4. **Plafond par don.** `ui_text_6745` (« It is over the maximum value of item to donate by one
   time ») prouve qu'il existe un maximum, côté client au moins ; sa valeur n'est pas lue. Le message
   d'action porte `0x13` = 19 à l'offset `+0xB` (`VA 0x5EF8C2`), valeur qui ressemble à un plafond
   d'objets par don mais n'est lue par aucun consommateur identifié. Le champ `count` de la trame est
   un octet signé (`int8`), donc un plafond protocolaire existe de fait à 127.
5. **Sémantique exacte des contrôles.** `button_donation` est prouvé comme émetteur ; ce que
   `donation_input_text` alimente (fenêtre `+0x4C0`/`+0x4C4`/`+0x4C8`), ce que fait réellement le
   champ de mode `[window+0x4A0]` positionné à `3` par `button_jp_donation`, et si or et jp peuvent
   être donnés dans la **même** trame (le message d'action porte les deux à la fois, donc c'est
   possible côté client) ne sont pas établis par la lecture. Le `count` par objet (pile entière ou
   quantité partielle) l'est encore moins.
6. **Classement des donateurs.** `button_donation_ranking`, `window_donation_ranking.nui`,
   `window_donation_msgbox_rankingitem.nui` (`SFrame.exe` l. 23935, 24760, 24759) et `ui_text_6744`
   (`My Rank`) impliquent qu'un classement existe et qu'un paquet descendant l'alimente. Ce paquet
   n'est pas identifié et sort du périmètre du 258.
7. **Identité des handles d'objet côté client.** Le dépôt résout les handles d'objet par
   `(uint)item.Id` pour tous les paquets d'objet montants existants ; supposer la même identité pour
   le 258 est cohérent mais non prouvé par le binaire client (même réserve que
   `253-use-item.md`, §7.8).
8. **Le nom du paquet n'est pas dans la table d'annotation du client** (0 occurrence de `DONATE`) :
   elle est partielle, comme déjà relevé pour 218/219. La preuve d'émission retenue ici passe par le
   chemin d'envoi et non par cette table.

## 8. Commits et binaires épinglés

| Référence | Version | Usage dans cette fiche |
| --- | --- | --- |
| Navislamia (`KillianDoubre/Navislamia`) | `master` = `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` ; branche `hermes/packet-258-donate-item` | état du dépôt (§1, §5.3, §6) |
| rzu (`glandu2/rzu`) | HEAD `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02) | structure, id, gating (§3, §4) |
| rzu — `TS_CS_DONATE_ITEM.h` | introduit par `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07, « Adjust count management for packets and add all known GS packets as of 9.4 ») ; `jp >= EPIC_6_2` **et** premier gating du `count` d'élément par `bdd362a600d104fd676facf12d6c6beb85a23ec4` (2017-02-26, « Update packets based on available GS pdbs (5.2, 6.1, 6.2, 7.1, 7.2, 7.3, 7.4, 8.1) », avec `EPIC_7_1`) ; borne corrigée en `EPIC_6_3` par `c8568e417c46ed324bcbaf7ff6827070301bf162` (2017-03-14) ; ids versionnés (258/1258) par `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11, remap 9.6.3) | §3, §4 |
| rzu — `TS_CS_DONATE_REWARD.h` | introduit par `d7c58ee6…` ; ids versionnés par `11f2b6fd…` | §1 (contexte du frère 259) |
| rzu — macro d'en-tête | `CREATE_PACKET_VER_ID` : en-tête de 7 octets | `librzu/src/lib/Packet/PacketDeclaration.h:616-618` (§1, §3) |
| NGemity (`NGemity/RZEmulator`) | HEAD `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | absence de traitement (§5.2, §6) |
| NGemity — `TS_CS_DONATE_ITEM.h` | introduit par `90a500d46acc39d21ef943a047a37d0a641f7af4` (2018-08-24, « WIP: add support for serializable packets ») ; en-tête 7 octets via `CREATE_PACKET` (`shared/Server/Packets/PacketDeclaration.h:576`) ; seules retouches ultérieures : `44b7d25d`, `8f5e80fa`, `e1136b84` (formatage / réécriture réseau) | §5.2, §6 |
| NGemity — `EPIC` de compilation | `EPIC_4_1_1` (`shared/Common/Define.h:25`) : dans **son** binaire, `jp` et le `count` `int64` disparaissent → enregistrements de 6 octets, trame `17 + 6 × N` | §4 (preuve de l'écart), §6 |
| client de référence | `reference/client73/SFrame.exe` sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | §2, §3, §4 |
| client de référence | `reference/client73/db_string.rdb` sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` | §2, §7 |
| client de référence | `reference/client73/extraction-manifest.json` (index d'extraction des 83 822 entrées) | provenance des ressources ci-dessus |

## Note de livraison

Cette fiche n'ajoute **aucun fichier de code** : elle ne touche ni `Game/`, ni `Tests/`, ni `CLAUDE.md`.
`docs/packet-specs/` est déjà suivi par `master` (`.gitignore:473`, `!/docs/packet-specs/`), donc
aucune modification de `.gitignore` n'est nécessaire pour cette fiche — contrairement aux fiches
antérieures qui devaient ouvrir l'exception.

Baseline relevée sur `master` (`ec76b218cd0bd7c6498d725f253abb8b431f0cd6`) avant la fiche :
`dotnet build Navislamia.sln -c Debug` code **0** (0 erreur, 160 avertissements) ;
`dotnet test Tests/Tests.csproj` code **0**, **448 tests** réussis sur 448.
Le plancher de non-régression des critères transversaux (366) est donc déjà largement dépassé ; c'est
448 qui doit être conservé comme référence pour cette branche.

Le bloc destiné à `CLAUDE.md` (sous-section `### Paquet 258 — TM_CS_DONATE_ITEM`, sur le modèle de
celles des paquets 203 et 253) reste à rédiger par le dev **après** implémentation : il doit décrire
le comportement livré, que cette fiche ne peut pas encore constater. Le dev n'écrit pas `CLAUDE.md` —
le bloc va dans la description de la MR, que le QA recopie.

Rappel de méthode pour l'audit de cette fiche : les preuves client proviennent d'une lecture statique
du binaire (`strings -n 4`, `cmp`, `objdump -d -M intel --start-address/--stop-address`, et deux
balayages d'octets pour retrouver les immédiats `0x102`/`0x46A` et les sites d'appel). Aucun binaire
client n'a été exécuté ; les adresses sont des VA du fichier `SFrame.exe` épinglé par son sha256
ci-dessus, et chaque conclusion de §2/§3/§4 est vérifiable en rejouant le désassemblage aux VA citées.
