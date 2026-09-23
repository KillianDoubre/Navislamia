# 323 — `TM_CS_CHANGE_SUMMON_NAME`

Fiche d'archéologie de protocole, Epic 7.3. Écrite en lecture seule sur les références
(`reference/rzu`, `reference/ngemity`, `reference/client73`) : aucun Lua, aucun script client,
aucun exécutable client n'a été lancé. Le client 7.3 tranche ; rzu tranche la forme, la taille et
l'ordre des champs ; NGemity ne tranche **rien** ici (§5.1, §6).

Cette fiche **reprend l'item 2 du `NON ÉTABLI`** de `socle-invocations.md:611-615` (« quel id porte
le renommage d'invocation en 7.3 ? ») et le **réduit sans le clore** : il est désormais prouvé que
le binaire client 7.3 **sait émettre 323**, ce que la fiche socle ne pouvait pas établir.

| Question de la carte | Verdict de cette fiche |
|---|---|
| Ce que le paquet transporte, et sa taille | **Tranché** : un seul champ, `name`, **19 octets** en 7.3 — **26 octets** au total. Deux sources indépendantes : rzu (`TS_CS_CHANGE_SUMMON_NAME.h:6-8`) et le constructeur de trame du client (0x48c5e0). §3 |
| L'id retenu pour 7.3 | **323**. Gating `>= EPIC_9_6_3` → 1323, hors 7.3. §4.1 |
| La cible du renommage | **Tranché par la structure** : le paquet **ne porte aucun handle**. La cible ne peut donc être que l'invocation du demandeur, résolue côté serveur. §3.2, §5.3 |
| Le déclencheur exact de 323 côté client | **Non tranché** : la fenêtre de renommage et la classe de message sont prouvées, l'action d'interface qui les relie ne l'est pas. §2, §7(a) |
| 322 ou 30 `TM_SC_CHANGE_NAME` comme pendant S→C | **Non tranché** (§7(b)), mais **exclu comme émetteur** : 30 est S→C (`TS_SC_CHANGE_NAME.h:12-13`) et le client possède un émetteur 323. |
| La règle « on reçoit 26 octets, on accepte et on réessaie » | **Refusée** : rien dans rzu, NGemity ni le client ne la porte ; la réponse à 323 n'existe dans aucune des trois références. §5.5, §7(c) |

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id client → serveur | **323** (`0x0143`) | `op_codes.md:114` |
| Nom | `TM_CS_CHANGE_SUMMON_NAME` | `op_codes.md:114` |
| Sens | client → serveur (`SessionPacketOrigin::Client`) | `reference/rzu/librzu/src/packets/GameClient/TS_CS_CHANGE_SUMMON_NAME.h:14` |
| Ids alternatifs | **1323** à partir d'`EPIC_9_6_3` | `TS_CS_CHANGE_SUMMON_NAME.h:10-12` |
| Référence NGemity | `TS_CS_CHANGE_SUMMON_NAME = 323`, `szName` 19 | `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_CHANGE_SUMMON_NAME.h:7,9` ; `shared/Server/ClientPackets.h:127` ; `shared/Server/XPacket.h:76` |
| Trame jumelle S→C | **322** `TM_SC_SHOW_SUMMON_NAME_CHANGE` — un `handle`, **11 octets** | `op_codes.md:113` ; `reference/rzu/librzu/src/packets/GameClient/TS_SC_SHOW_SUMMON_NAME_CHANGE.h:6,8-10` ; `shared/Server/ClientPackets.h:126` ; `shared/Server/XPacket.h:295` |
| État dans Navislamia | **absent** : ni 322 ni 323 dans `GamePackets` (la famille d'invocation s'arrête à 321) et aucun bras dans `GameClient.cs` | `Game/Network/Packets/Enums/GamePackets.cs:70-77` ; `grep -n "TM_.*CHANGE_SUMMON_NAME" Game/` → 0 |

La définition rzu, intégralement :

```
#define TS_CS_CHANGE_SUMMON_NAME_DEF(_) \
	_(def)(string)(name, 20) \
	  _(impl)(string)(name, 19, version < EPIC_9_6) \
	  _(impl)(string)(name, 20, version >= EPIC_9_6)

#define TS_CS_CHANGE_SUMMON_NAME_ID(X) \
	X(323, version < EPIC_9_6_3) \
	X(1323, version >= EPIC_9_6_3)
```
(`TS_CS_CHANGE_SUMMON_NAME.h:5-12`)

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Ce qui est prouvé dans `SFrame.exe`

Le client 7.3 **sait émettre 323**. Quatre faits indépendants, tous obtenus par lecture du
désassemblage (`objdump -d -M intel SFrame.exe`, convention de citation en §8) :

1. **Constructeur de trame** en `0x48c5e0`, dans la famille des constructeurs de trames du client
   (`0x48c490`…`0x48d1b0`, celle dont `socle-classements.md` a identifié le constructeur de 5000 en
   `0x48d160`) : il écrit `id = 0x143` (**323**) à l'offset 4 (`0x48c612-0x48c617`), `length = 0x1a`
   (**26**) à l'offset 0 (`0x48c61d`), puis recalcule la somme de contrôle. Il **zéroise au préalable
   19 octets** aux offsets 7 à 25 (`0x48c5ff-0x48c60e`). La forme de la fonction est identique à
   celle du constructeur de 5000, vérifié par la fiche socle : écriture d'un en-tête de 7 octets,
   somme, puis id et longueur réels.
2. **Un unique appelant** : `0x48d4ec`, dans la fonction `0x48d4e0` (`ret 4`, un argument). Aucun
   autre site d'appel de `0x48c5e0` dans le binaire.
3. **Le nom y est recopié depuis une `std::string`** : `0x48d506-0x48d514` prend la chaîne à
   `arg+0x13` (pointeur de données à `arg+0x13` si la capacité dépasse 15, sinon le tampon en
   ligne — disposition MSVC), boucle de copie **jusqu'au NUL inclus** (`0x48d520-0x48d525`), puis
   `mov BYTE PTR [ebp-0x3],cl` (`0x48d531`) écrit un **NUL forcé au dernier octet du champ**
   (offset 25 = 19ᵉ octet du champ). La trame est ensuite passée à la session (`0x48d534`).
   Conséquence utile au serveur : **le client garantit un NUL à l'octet 25** ; le champ n'est donc
   pas « 19 octets pleins », il est terminé — éventuellement tronqué à l'octet 25.
4. **Deux classes de message internes** déclarées en RTTI dans `.data` :
   `.?AUSMSG_SUMMON_NAME_CHANGE@@` (0xc1dc2c — interface → réseau) et
   `.?AUSIMSG_UI_CHANGE_SUMMON_NAME@@` (0xc155dc — interface → interface graphique). Le
   constructeur de la première, `0x4b2dc0`, n'a **qu'un seul appelant** (`0x4b63a5`), lui-même
   appelé d'un seul endroit (`0x4b931a`, §2.3) : il n'existe donc **qu'un seul chemin** de
   construction du message. Ce constructeur pose le type interne `0x469` (1129) à `+4` (`0x4b2de3`),
   un champ `0x13` (19) à `+0xb` (`0x4b2df1`), une `std::string` à `+0x13` (`0x4b2dfe`), et prend un
   `const char*` en argument (recopié par `std::string::assign`, `0x4b2e2c`). Le répartiteur de
   messages de l'interface (`0x49cd00`, `mov eax,[edi+4]` puis double table d'aiguillage `0x49ea50`
   + `0x49e98c`) route ce type `0x469` vers le cas `0x49e608`, qui appelle le constructeur de trame
   323 (`0x49e60b`). La cohérence est complète : le type `0x469` posé par le constructeur du message
   est celui dont le cas appelle l'empaqueteur — et ce type, 1129, est celui que j'ai décodé dans la
   table avant de le retrouver dans le constructeur.

### 2.2 Le lexique et les données du client

- Fenêtre de renommage : un constructeur résout deux contrôles enfants **par leur nom**,
  `change_name_box` et `use_name` (`0x503e90`, chaînes `SFrame.exe` l. 20844-20845) et les range
  dans l'objet fenêtre. C'est la boîte de saisie + le bouton de validation.
- Textes (`db_string.rdb`, offsets du dump `strings -a -t x`) :
  clé `ui_text_6434` à 0x470d77, texte à 0x470d84 — « Modify name of summoned creature. … Creature
  name : … Enter the new name » ; clé `smsq_change_creaturename` à 0x44b340, texte à 0x44b359 —
  « Summoned creature name was changed to <B>#@creature_name@#</B> » ; clé
  `smsq_creature_name_change_fail` à 0x44ad5f, texte à 0x44ad7e — « Creature name is too short. » ;
  libellés d'objet « Creature Name Change » et « Pet Name Change » aux clés `ui_text_6784`
  (0x63e6a4) et `ui_text_6785` (0x63e6e6), et « Creature Name Change » également porté par la clé
  `name_item_010009` (0xa519f7, texte à 0xa51a06). Aucun de ces cinq libellés n'apparaît dans
  `SFrame.exe` (`grep` → 0 pour chacun) : ils viennent de la base de chaînes du client, pas du
  binaire, et sont résolus par clé.
- Le client 7.3 **ne possède pas** de constructeur de trame pour 322 (aucun `mov edx,0x142` /
  `mov ecx,0x142` dans la famille de constructeurs) : 322 est **reçu**, ce qui est cohérent avec
  `SessionPacketOrigin::Server` (`TS_SC_SHOW_SUMMON_NAME_CHANGE.h:12`).

### 2.3 Ce qui reste ouvert

Le **seul** chemin de construction du message est `0x4b931a` (site d'appel) → `0x4b6320` (cible),
et ce site est gardé par une comparaison de chaîne avec la constante `.rdata` 0xa1ffac (`FSTATUS`)
que je n'ai pas su attribuer à une action d'interface. Le faisceau prouve le **trajet** (nom saisi →
message interne 1129 → trame 323), pas le **geste** (fenêtre ouverte par 322 ? par l'objet
« Creature Name Change » ? par une commande ?). Voir §7(a) et §7(b) : c'est la question à trancher
par capture réseau, et le dev n'a pas à la deviner.

## 3. Structure sur le fil

### 3.1 En-tête Navislamia — 7 octets

`Length` (uint32, offset 0), `ID` (uint16, offset 4), `Checksum` (uint8, offset 6)
(`Game/Network/Packets/Header.cs:6-11`, lecture `:20-25`). `HeaderSize = 7` côté émission
(`Game/Network/Packets/Game/GameSummonPackets.cs:21`). `Checksum` = somme des octets 0 à 5
(`GameSummonPackets.cs:186-196`).

### 3.2 `TM_CS_CHANGE_SUMMON_NAME` (323) — client → serveur — **26 octets**

| Offset | Type | Nom | Valeur attendue | Source |
|---|---|---|---|---|
| 0 | uint32 | `Length` | 26 (`0x1a`) | `Header.cs:9` ; total 7 + 19 ; client : `0x48c61d` |
| 4 | uint16 | `ID` | 323 (`0x0143`) | `op_codes.md:114` ; `TS_CS_CHANGE_SUMMON_NAME.h:11` ; client : `0x48c612-0x48c617` |
| 6 | uint8 | `Checksum` | somme des octets 0 à 5 | `Header.cs:11` ; `GameSummonPackets.cs:186-196` |
| 7 | char[19] | `name` | nom du familier, terminé par un NUL | `TS_CS_CHANGE_SUMMON_NAME.h:6-8` (`_(impl)(string)(name, 19, version < EPIC_9_6)`) ; NGemity `TS_CS_CHANGE_SUMMON_NAME.h:7` |

**Taille totale attendue : 26 octets.** Un seul champ, aucune longueur variable, aucun tableau,
aucun handle, aucun alignement (`CREATE_PACKET_VER_ID`, pas de `_(count)`/`_(dynstring)`).

Sémantique d'écriture du champ, côté rzu : `writeString` tronque à `maxSize - 1` et **complète par
des zéros** (`librzu/src/lib/Packet/MessageBuffer.cpp:87-95`). En 7.3 le champ fait 19 octets :
**18 caractères utiles + le NUL**. Côté client, le comportement observé est le même dans son
résultat (§2.1 point 3), à une différence près : la copie n'est pas bornée — un nom de plus de
18 caractères remplit les octets 7 à 25 et c'est le NUL forcé à l'octet 25 qui termine le champ ;
le serveur doit donc lire **jusqu'au premier NUL** et ignorer ce qui suit (§5.3).

### 3.3 Le champ `name` n'est pas un handle déguisé

Le paquet ne transporte **aucune** cible : ni `ar_handle_t`, ni index d'emplacement. Le pendant
S→C 322 transporte, lui, un `handle` (`TS_SC_SHOW_SUMMON_NAME_CHANGE.h:6`), et les autres trames de
renommage de familier du protocole 7.3 en transportent aussi — 354 `TM_CS_SET_PET_NAME` porte un
`ar_handle_t` puis un `name` de 19 octets, soit 30 octets (`socle-invocations.md:243-250`). Le
renommage d'invocation est le seul à ne pas nommer sa cible. À retenir pour l'implémentation :
c'est une **propriété de la trame**, pas une omission.

### 3.4 La trame jumelle 322 (S→C) — **11 octets**

| Offset | Type | Nom | Source |
|---|---|---|---|
| 0 | uint32 | `Length` = 11 | `Header.cs:9` ; total 7 + 4 |
| 4 | uint16 | `ID` = 322 (`0x0142`) | `op_codes.md:113` ; `TS_SC_SHOW_SUMMON_NAME_CHANGE.h:9` |
| 6 | uint8 | `Checksum` | `Header.cs:11` |
| 7 | uint32 | `handle` | `TS_SC_SHOW_SUMMON_NAME_CHANGE.h:6` (rzu `ar_handle_t` = `uint32_t`) ; NGemity `TS_SC_SHOW_SUMMON_NAME_CHANGE.h:7` (`uint32_t`) |

Elle est documentée ici parce que c'est le seul candidat sérieux à être émis **en réponse** ou **en
amont** de 323, et parce que sa taille (11) sert de repère de test. Elle n'est émise nulle part
aujourd'hui (`socle-invocations.md:267`).

## 4. Gating de version

Base de comparaison : `EPIC_7_3 = 0x070300`, `EPIC_9_6 = 0x090600`, `EPIC_9_6_3 = 0x090603`
(`librzu/src/lib/Packet/PacketEpics.h:59,75,96`). Le dépôt sert Epic 7.3 : `0x070300` est inférieur
aux trois.

### 4.1 Décision sur l'id — **323**

rzu : `X(323, version < EPIC_9_6_3)` / `X(1323, version >= EPIC_9_6_3)`
(`TS_CS_CHANGE_SUMMON_NAME.h:10-12`). **Décision : 323 pour 7.3**, et 1323 est hors de portée du
serveur. La forme versionnée a été introduite le 2020-08-11 par
`11f2b6fd69913c192e6103afd3953a358ef6226a` (« packets: use versionned ID for all packets and update
their ID with epic 9.6.3 ») — avant ce commit, la constante était `CREATE_PACKET(..., 323)`, donc
323 quel que soit l'épique. NGemity, qui ne versionne pas, déclare aussi 323
(`TS_CS_CHANGE_SUMMON_NAME.h:9`) : les deux références concordent pour 7.3.

### 4.2 Décision sur la largeur de `name` — **19 octets**

rzu : `_(def)(string)(name, 20)` surchargé par `_(impl)(string)(name, 19, version < EPIC_9_6)` et
`_(impl)(string)(name, 20, version >= EPIC_9_6)` (`TS_CS_CHANGE_SUMMON_NAME.h:6-8`). 7.3 < 9.6 :
**19 octets**. Le `_(def)` (20) n'est qu'une déclaration de repli, il ne s'applique pas ici. La
surcharge a été introduite le 2019-03-31 par
`f0319180a3f78b8b96e342cbed2448a71491a870` (« Update 9.6 packets »), qui remplace la ligne unique
`_(string)(szName, 19)` : la largeur 7.3 n'a donc jamais changé, c'est la 9.6 qui passe à 20.
Nouvelle confirmation indépendante : le constructeur de trame du client écrit une longueur totale
de **26** octets, soit exactement 7 + 19 (§2.1 point 1).

### 4.3 Aucune autre décision de version

Le paquet n'a qu'un champ et rzu ne porte aucun autre `version` sur cette définition. Il n'y a donc
pas d'autre arbitrage de version à statuer, et aucun champ dont le gating resterait non tranché.

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` (NGemity) en fait : **rien**

`grep -rn "CHANGE_SUMMON_NAME\|SHOW_SUMMON_NAME_CHANGE\|ChangeSummonName" --include=*.cpp
--include=*.h reference/ngemity` ne renvoie que quatre lignes, toutes déclaratives : les deux
en-têtes de paquet (`TS_CS_CHANGE_SUMMON_NAME.h:7,9` et `TS_SC_SHOW_SUMMON_NAME_CHANGE.h:7,9`),
l'énumération `ClientPackets.h:126-127` et les deux `#include` de `XPacket.h:76,295`. **Aucun
`WorldSession`, aucun `Messages.cpp`, aucun gestionnaire n'utilise le paquet** : Chihiro ne le lit
pas, ne le valide pas et n'y répond pas. NGemity ne fournit donc **aucune sémantique** — c'est un
écart de méthode à assumer, pas une omission à corriger.

### 5.2 Ce que rzu en fait : **rien**

`grep -rn` sur `reference/rzu` (hors `.git`) ne trouve le paquet que dans son propre en-tête. rzu
est une bibliothèque de formes : il documente l'id, le sens et les 19 octets, et ne dit rien du
comportement. Idem pour 322.

### 5.3 Ce que le serveur Navislamia doit faire

Ce qui suit est **déduit de la structure et des données du client**, pas copié d'une référence :
aucune des deux références C++ ne fournit de règle. C'est le squelette attendu, à valider par
Killian sur les points marqués.

1. **Recevoir exactement 26 octets.** Le client écrit la longueur en dur (`0x48c61d`) : une trame
   d'une autre longueur est malformée, pas une variante courte. Modèle de lecture déjà en place
   dans le dépôt pour un nom fixe terminé par un NUL : `GameCompetePackets.cs:96-114` (longueur
   exacte, puis `IndexOf((byte)0)`, puis `Encoding.ASCII.GetString`). Ici le premier NUL est
   garanti présent (§2.1 point 3).
2. **Résoudre la cible côté serveur.** Le paquet n'en porte aucune (§3.3) : la seule cible
   cohérente est l'invocation du demandeur. Les données existent :
   `SummonEntity.CharacterId`, `SummonEntity.SummonResourceId`, `SummonEntity.CardItemId`,
   `SummonEntity.MainSummonsMaster` / `SubSummonsMaster` (`Game/DataAccess/Entities/Telecaster/SummonEntity.cs:9-16`).
   Deux invocations peuvent appartenir au même personnage (principale et secondaire) :
   **laquelle est renommée n'est pas tranchable par lecture** — voir §7(d), et c'est le seul point
   de cette section que le dev ne peut pas décider seul.
3. **Valider le nom avant de l'écrire.**
   - longueur : 1 à 18 caractères utiles (le 19ᵉ octet est le NUL). Le client refuse déjà en local
     et possède le message « Creature name is too short. » (`smsq_creature_name_change_fail`,
     `db_string.rdb` 0x44ad5f) : un refus serveur est donc attendu par l'interface, pas une
     surprise ;
   - mots interdits : l'infrastructure est là — `IBannedWordsRepository.IsBannedWord` /
     `ContainsBannedWord` (`Game/DataAccess/Repositories/BannedWordsRepository.cs:17-25`), table
     `BannedWordsResources` ;
   - **le plafond exact (18 ? autre ?) et la règle d'unicité ne sont pas établis** (§7(e)) : à
     trancher avant d'écrire un refus.
4. **Persister.** `SummonEntity.Name` (`SummonEntity.cs:21`) est la colonne visée ; c'est aussi la
   source du champ `name` de 301 (`GameSummonPackets.cs:59-60`, commentaire de provenance).
5. **Notifier.** La trame de re-publication existe déjà côté serveur : 301
   `TM_SC_ADD_SUMMON_INFO`, 46 octets, `card_handle` @7, `summon_handle` @11, `name` @15,
   `code` @34, `level` @38, `sp` @42 (`GameSummonPackets.cs:31-56` ; structure dans
   `socle-invocations.md:170-180`). **L'accusé à envoyer, lui, n'est établi par aucune des trois
   références** : ni rzu, ni NGemity, ni le client ne montrent ce que le vrai serveur renvoie à 323
   (§7(c)).

### 5.4 Ce que le dépôt fait aujourd'hui d'un 323 reçu (vérifié sur la base de cette fiche)

| Étape | Comportement actuel | Source |
|---|---|---|
| Id non déclaré | `Enum.IsDefined` échoue → log `Debug` « Undefined packet ID: 323 » puis `continue`, la trame est **jetée** | `Game/Network/Clients/GameClient.cs:936-940` |
| Après ajout du membre à `GamePackets` | plus de garde : la trame atteint le `switch` final et, sans bras, **lève** `_ => throw new Exception("Unknown Packet Type")` | `GameClient.cs:1358-1370` |

C'est le critère transversal 4 (« énumération et dispatch dans le même changement ») : le membre
`TM_CS_CHANGE_SUMMON_NAME = 323` et son bras de lecture doivent être livrés **ensemble**.

**Réserve constatée** (hors objet de cette carte, signalée pour la QA et Killian) : les huit ids
d'invocation déjà déclarés — 301, 302, 303, 305, 306, 307, 320, 321 (`GamePackets.cs:70-77`) —
n'ont **aucun** bras dans `GameClient.cs` (`grep` : aucune occurrence). Un client qui les enverrait
atteindrait donc le `throw` de la ligne 1369, ce que la règle 4 interdit. Ils sont S→C, donc le cas
ne se produit qu'avec un client hostile ou un bogue ; je ne l'ai pas corrigé ici.

### 5.5 Les pièges à ne pas porter

- **« On reçoit 26 octets, on accepte et on réessaie »** : rien ne le fonde. Le client n'attend
  aucun accusé observable, et aucune référence ne décrit de boucle de réessai. Ne pas l'implémenter
  par défaut : soit le lot n'émet rien, soit il émet ce que Killian tranche en §7(c).
- **Inventer une cible** : le paquet n'en porte pas ; ajouter un champ (handle, index) serait
  inventer un protocole que le client 7.3 ne sait pas produire. Le constructeur de trame du client
  est la preuve : il n'écrit que le nom.
- **Deviner l'accusé**, la longueur minimale/maximale du nom ou la règle d'unicité.
- **Copier `socle-invocations.md:403`** qui cite `GameClient.cs:670-686` pour le `throw` final et
  `GamePackets.cs:41` pour la famille d'invocation : ces deux lignes sont périmées (le `throw` est
  en 1369, la famille en 70-77).

## 6. Écarts assumés avec NGemity, et pourquoi

| Point | NGemity | Cette fiche | Pourquoi |
|---|---|---|---|
| Id | 323, sans versionnement de l'id | 323 pour 7.3 (1323 hors 7.3) | rzu versionne ; NGemity non. Sans effet pour 7.3 : même id. |
| Largeur du nom | 19, figée | 19 pour 7.3, 20 à partir de 9.6 | rzu porte la surcharge de version ; NGemity ne suit pas la 9.6. Sans effet pour 7.3. |
| Nom du champ | `szName` | `name` | Convention rzu (et du dépôt). Purement local. |
| Sens | `TS_CS_CHANGE_SUMMON_NAME = 323` dans `ClientPackets.h:127` | identique | accord |
| Sémantique | **aucune** (aucun gestionnaire, §5.1) | proposition argumentée en §5.3 | NGemity n'offre aucune règle à porter ; nous ne pouvons pas invoquer sa logique, il faut la construire. Aucun code NGemity n'est donc repris ici, et aucune de ses décisions n'est contredite. |
| Trame 322 | `uint32_t handle` (`TS_SC_SHOW_SUMMON_NAME_CHANGE.h:7`) | idem, `ar_handle_t` chez rzu | Même largeur (4) ; aucune divergence de trame. |

## 7. `NON ÉTABLI`

### (a) L'action d'interface exacte qui émet 323

Faisceau réuni : constructeur de trame unique (§2.1 point 1) ; un seul appelant (§2.1 point 2) ;
classe de message `AUSMSG_SUMMON_NAME_CHANGE` avec son type interne 1129 routé vers ce
constructeur (§2.1 point 4) ; fenêtre de renommage `change_name_box`/`use_name` (§2.2) ;
textes « Enter the new name » et « Summoned creature name was changed to … » (§2.2).
**Limite** : le site d'appel du constructeur de message est unique (`0x4b931a`), et il vit dans une
fonction (`0x4b92xx`) gardée par une comparaison de chaîne sur la constante `.rdata` 0xa1ffac
(`FSTATUS`, chaîne `SFrame.exe` l. 20092) — branchement que je n'ai pas su rattacher à une action du
joueur. Autrement dit : le trajet « nom saisi → message 1129 → trame 323 » est **prouvé**, le
**geste** qui le déclenche ne l'est pas (fenêtre ouverte par un paquet serveur ? par l'objet
« Creature Name Change » ? par une commande ?).
**Question à trancher** : capture réseau 7.3 sur l'ouverture de la boîte de renommage et sur la
validation, ou désassemblage du chemin `0x4b92xx → 0x4b6320 → 0x4b2dc0`. Le dev **n'a rien à
implémenter** de ce côté : le serveur ne fait que recevoir.

### (b) Quelle trame S→C ouvre la fenêtre : 322 ou 30 `TM_SC_CHANGE_NAME` ?

C'est l'item 2 de `socle-invocations.md:611-615`, **réduit mais non clos**. Ce que cette fiche
ajoute :
- 30 `TM_SC_CHANGE_NAME` est **S→C** (`TS_SC_CHANGE_NAME.h:15` : `SessionPacketOrigin::Server`) :
  il ne peut donc pas être ce que le *client émet* pour renommer, puisque le client possède un
  émetteur dédié (323, §2.1). Il porte lui aussi un handle plus un nom de 19 octets
  (`TS_SC_CHANGE_NAME.h:6,11-13` ; structure dans `socle-invocations.md:274-281`) : il peut en
  revanche être ce que le serveur envoie **après** un renommage réussi, pour rafraîchir l'affichage.
- 322 est absent de la table de noms `TM_*` du client, dont l'incomplétude est **démontrée**
  (404 y manque alors que le client gère le paquet — `socle-invocations.md:143-149`). Son absence
  n'est donc pas un indice d'inutilisation.
**Question à trancher** : le vrai serveur 7.3 émet-il 322 avant d'attendre 323 ? Une capture
tranche ; rien dans les artefacts disponibles ne le fait.

### (c) La réponse du serveur à 323

Aucune des trois références ne montre d'accusé : NGemity n'a pas de gestionnaire (§5.1), rzu n'a
pas d'usage (§5.2), et le client n'expose aucun journal du type « SGameInterface - MSG_… » pour un
résultat de renommage d'invocation. Les candidats **ne sont pas équivalents** et ne doivent pas
être choisis au hasard : 301 (re-publier le nom, 46 octets), 322 (11 octets), ou aucun.
`SendResult` (`GameClient.cs:57`) existe et est utilisé par le dépôt pour d'autres refus, mais rien
n'établit que le client 7.3 accepte ou affiche un `TM_SC_RESULT` pour 323.
**Recommandation** : premier lot **sans** accusé, refus silencieux journalisé, et une réserve
écrite ; l'émission se tranche sur capture.

### (d) Quelle invocation est renommée quand le personnage en a deux

`SummonEntity` relie les deux invocations au même `CharacterId` (`SummonEntity.cs:9-11` :
`MainSummonsMaster` / `SubSummonsMaster`). Le paquet ne porte aucun discriminant (§3.3) : si les
deux existent, le serveur doit choisir. Aucune référence ne tranche, et le client non plus (la
fenêtre porte un seul champ de saisie).
**Question à trancher** : invocation principale seulement ? celle qui est invoquée dans le monde ?
refus si les deux sont présentes ? Ce point conditionne le code, pas la trame.

### (e) Longueur minimale, longueur maximale utiles et unicité du nom

- 18 caractères utiles est une **déduction** (19 octets dont le NUL, §3.2) : c'est la largeur du
  champ, pas une règle du client.
- Le client possède « Creature name is too short. » : il existe donc une longueur minimale, dont la
  valeur n'est pas dans les artefacts lus.
- Rien n'établit si un nom déjà porté par une autre invocation du même personnage (ou d'un autre)
  doit être refusé.
**Le dev ne doit pas inventer ces bornes** : elles vont dans la description de la MR comme réserves,
avec un refus documenté et journalisé, pas une valeur devinée.

### (f) Les identifiants de la carte non retrouvés localement

La carte annonce : objet **700001**, effet **115 `RenameSummon`**, `db_string` **23147**/**23148**,
messages **4001138**/**4001139**, et la chaîne « Creature Name Change System Dialog ». Ce que j'ai
pu vérifier sur les artefacts fournis :
- effet `RenameSummon = 115` : établi **côté Navislamia** seulement
  (`Game/DataAccess/Entities/Enums/ItemEffectInstant.cs:28`, cité par `socle-invocations.md:398`) ;
  `db_item.rdb` n'a **pas** été décodé dans le temps de cette fiche ;
- objet 700001 : `grep -rn "700001"` sur `*.sql`, `*.cs` et `docs/` du dépôt → **0 occurrence** (y
  compris `ArcadiaSchemaPSQL.sql`) ; non vérifiable ici ;
- « Creature Name Change » et « Pet Name Change » : clés `ui_text_6784` et `ui_text_6785`
  (`db_string.rdb` 0x63e6a4 et 0x63e6e6, suffixes 6784 et 6785), et le libellé d'objet
  « Creature Name Change » est aussi porté par la clé `name_item_010009` (0xa519f7). Ni 23147 ni
  23148 ne sont des clés de ces libellés. Je n'ai pas décodé le format d'enregistrement de
  `db_string.rdb` au-delà de l'observation « clé + longueurs + libellé + champ numérique » (pour
  `ui_text_6784`, ce champ vaut 6784 en 0x63e6c6), donc je ne peux pas exclure qu'un id 23147
  existe pour un **autre** libellé ;
- « Creature Name Change System Dialog » : `grep -ci "System Dialog"` → **0** occurrence dans
  `SFrame.exe` comme dans `db_string.rdb` ;
- 4001138 / 4001139 : **0** occurrence dans les deux fichiers.
**Conséquence** : ces valeurs ne doivent pas entrer dans le code. Si Killian les tient d'une autre
source (autre localisation, archives `data.001`…`data.008` absentes du VPS — `reference/README.md`),
elles sont à joindre à une fiche ultérieure ; elles ne changent pas la trame 323.

## 8. Commits et binaires épinglés

| Référence | Commit / empreinte |
|---|---|
| rzu (`reference/rzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (HEAD, 2023-10-02) |
| rzu — création de `TS_CS_CHANGE_SUMMON_NAME.h` | `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07, « Adjust count management for packets and add all known GS packets as of 9.4 ») |
| rzu — surcharge de largeur 19/20 (`version < EPIC_9_6`) | `f0319180a3f78b8b96e342cbed2448a71491a870` (2019-03-31, « Update 9.6 packets ») |
| rzu — `ar_handle_t` sur le handle de 322 | `05bc2d82dac16003bc06162584e8559826310bff` (2020-03-29, « Packets: Use strong typedef for handles and game time values ») |
| rzu — ids versionnés, `323` → `1323` à partir d'`EPIC_9_6_3` | `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11) |
| NGemity (`reference/ngemity`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (HEAD, 2025-12-03) |
| NGemity — création des deux en-têtes suivis | `90a500d46acc39d21ef943a047a37d0a641f7af4` (2018-08-24) |
| Navislamia (`master`, base de cette fiche) | `a1c495002c290cd1fb2e19edffeb8eff49553f46` |
| Client — `SFrame.exe` (9 841 664 o.) | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` |
| Client — `db_string.rdb` (14 294 729 o.) | sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` |
| Client — `data.000` (index d'archive) | sha256 `b88ac39a0976f49746671d413ba68af616af37e6e26095c3a109d5884510adbf` |

Convention de citation client : `SFrame.exe` est cité **par adresse d'instruction** du désassemblage
`objdump -d -M intel SFrame.exe` (2 256 750 lignes) ; les chaînes par leur numéro de ligne du dump
`strings -n 4 SFrame.exe` (même convention que la fiche 1202) ; les `.rdb` par leur octet de début
dans le fichier. Aucun outil du client n'a été exécuté.

## 9. Vérifications relevées (archéologue, sur `master` avant écriture de la fiche)

| Commande | Code de sortie | Résultat |
|---|---|---|
| `dotnet build Navislamia.sln -c Debug` | **0** | 0 erreur, 21 avertissements (`/tmp/build_baseline.log`) |
| `dotnet test Tests/Tests.csproj` | **0** | **976 réussis**, 0 échec, 0 ignoré (`/tmp/test_baseline.log`) |
| `git log --oneline origin/master..master` | 0 | **vide** : `master` locale n'a aucun commit d'avance |

Le compte de tests de la base est **976**, et non 366 comme l'indiquent les critères transversaux du
profil : le seuil réel à ne pas franchir pour ce lot est **976**.

État de l'environnement : `objdump` et `strings` disponibles ; les archives `data.001` à `data.008`
du client sont **absentes** du VPS (`reference/README.md`) — les scripts d'interface Lua du client
ne sont donc pas consultables, ce qui borne §2 et §7.

## 10. Bloc pour `CLAUDE.md` (à recopier dans la description de la MR)

> **Paquet 323 — `TM_CS_CHANGE_SUMMON_NAME` (renommage d'invocation, client → serveur).**
> Un seul champ : `name`, **19 octets** en 7.3 (18 caractères utiles + NUL), **26 octets** au total
> — rzu surcharge la largeur en 9.6 (20) et versionne l'id en `1323` à partir d'`EPIC_9_6_3`
> (`TS_CS_CHANGE_SUMMON_NAME.h:6-12`). Le client 7.3 possède bel et bien l'émetteur : constructeur
> de trame en `0x48c5e0` (id `0x143`, longueur `0x1a`), alimenté par la classe de message
> `AUSMSG_SUMMON_NAME_CHANGE` (type interne 1129) que l'interface route vers l'envoi. **La trame ne
> porte aucune cible** : c'est le serveur qui résout l'invocation du demandeur
> (`SummonEntity.CharacterId`). NGemity ne fait **rien** de ce paquet (aucun gestionnaire) et rzu
> n'en documente que la forme : la sémantique (validation, persistance, réponse) est à construire,
> pas à porter. **Non établi** : le geste d'interface déclencheur, la trame S→C qui ouvre la
> fenêtre (322 ou 30), la réponse du serveur, et laquelle des deux invocations est renommée.
> Détail complet et sources : `docs/packet-specs/323-change-summon-name.md`.

## 11. `A VERIFIER PAR KILLIAN`

1. **La réponse du serveur** (§7(c)) : aucun accusé, un 301 de re-publication, un 322, ou un
   `TM_SC_RESULT` ? Recommandation de la fiche : aucun accusé dans un premier lot, refus journalisé.
2. **Quelle invocation est renommée** quand le personnage en a deux (§7(d)) — décision de jeu,
   aucune référence ne la porte.
3. **Bornes du nom** (§7(e)) : longueur minimale, et refus ou non d'un doublon. Le client connaît un
   message « trop court » ; la valeur, elle, n'est pas dans les artefacts lus.
4. **Les identifiants de la carte** (§7(f)) : objet 700001, `db_string` 23147/23148, messages
   4001138/4001139, « Creature Name Change System Dialog » — aucun n'est retrouvé dans les artefacts
   fournis (les clés réellement présentes sont `ui_text_6784`, `ui_text_6785` et
   `name_item_010009` ; `grep "700001"` sur le dépôt → 0). À confirmer depuis la source d'origine ou
   à écarter.
5. **Réserve hors carte** (§5.4) : les huit ids d'invocation déjà déclarés (301, 302, 303, 305, 306,
   307, 320, 321) n'ont aucun bras dans `GameClient.cs` et peuvent atteindre le `throw` final.
   Faut-il une carte dédiée ?
