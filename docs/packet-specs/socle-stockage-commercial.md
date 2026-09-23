# Socle stockage commercial — `TM_SC_COMMERCIAL_STORAGE_INFO` (10003), `TM_SC_COMMERCIAL_STORAGE_LIST` (10004), `TM_CS_TAKEOUT_COMMERCIAL_ITEM` (10005)

Fiche d'archéologie du socle « stockage commercial (boutique d'objets) », branche
`hermes/packet-socle-stockage-commercial`, créée depuis `master`
`ec76b218cd0bd7c6498d725f253abb8b431f0cd6`.

Elle est **livrée en deux temps** : le lot 1 (§9.1) est le socle de protocole
livrable d'un bloc, sans aucune donnée inventée ; le lot 2 (§9.2) est le conteneur
persisté et le retrait réel, dont le **producteur** n'existe pas dans ce dépôt.
Le détail du découpage et le prérequis exact de chaque carte parkée sont en §9.

## Réponses courtes

| Question | Réponse | Statut |
|---|---|---|
| Opcodes 7.3 | **10003**, **10004**, **10005** | **Établi** (§4) : la bascule rzu vers 9003/9004/9005 est à `EPIC_9_6_3`, bien au-dessus d'`EPIC_7_3` |
| Direction | 10003 et 10004 : serveur → client. 10005 : client → serveur | **Établi** (§1, §2) : aucune trame 10003/10004 n'est construite par le client, la seule trame 10005 est un envoi |
| Tailles | 10003 = **11 o** ; 10004 = **9 + 10 × n** ; 10005 = **13 o** | **Établi** (§3) : lues instruction par instruction dans le convertisseur et l'émetteur du client 7.3 |
| Gating de champ | **Aucun** : les trois en-têtes rzu n'ont pas un seul `version >= EPIC_*` | **Établi** (§4) |
| Logique serveur de référence | **Aucune** : NGemity ne traite rien, rzu n'émet qu'une 10003 à 0/0 à l'entrée en jeu | Établi (§5.1, §5.2) — c'est le fait structurant de cette fiche |
| Producteur du contenu | **Inexistant** : la 10001 (boutique web) est hors lot, aucune boutique n'approvisionne le conteneur | Établi (§5.3, §9.2) : le conteneur est vide **par construction** |
| Persistance | Non livrable seule : voir le prérequis exact en §9.2 | **Réserve** (§7f, §9.2) |
| `commercial_item_uid` | Opaque, recopié tel quel par le client depuis la 10004 vers la 10005 | Établi comme *contrat* ; sa **source** serveur est `NON ÉTABLI` (§7c) |

---

## 1. Identité

| | 10003 | 10004 | 10005 |
|---|---|---|---|
| Nom `op_codes.md` | `TM_SC_COMMERCIAL_STORAGE_INFO` | `TM_SC_COMMERCIAL_STORAGE_LIST` | `TM_CS_TAKEOUT_COMMERCIAL_ITEM` |
| Ligne `op_codes.md` | `:275` | `:276` | `:277` |
| Sens | serveur → client | serveur → client | client → serveur |
| En-tête rzu | `TS_SC_COMMERCIAL_STORAGE_INFO.h:1-16` | `TS_SC_COMMERCIAL_STORAGE_LIST.h:1-24` | `TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:1-16` |
| Origine déclarée | `SessionPacketOrigin::Server` (`:15`) | `SessionPacketOrigin::Server` (`:23`) | `SessionPacketOrigin::Client` (`:15`) |
| En-tête NGemity | `shared/Server/Packets/GameClient/TS_SC_COMMERCIAL_STORAGE_INFO.h:1-12` | `…/TS_SC_COMMERCIAL_STORAGE_LIST.h:1-19` | `…/TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:1-12` |

Autres références d'identité :

- inclusions NGemity : `shared/Server/XPacket.h:164` (10005), `:214` (10003), `:215` (10004) ;
- table id → nom du client 7.3 : entrée `10003` = `mov eax,0x2713` VA `0x6793b7` avec `push 0xa52d98`
  (`TM_SC_COMMERCIAL_STORAGE_INFO`) VA `0x6793a7` ; entrée `10004` = VA `0x6793fa` avec `push 0xa52d78` ;
  entrée `10005` = VA `0x67943d` avec `push 0xa52d58` ;
- noms de types d'exécution du client, chaînes MSVC dans `.data` :
  `.?AUSMSG_COMMERCIAL_STORAGE_INFO@@` (VA `0xc1dfcc`), `.?AUSMSG_COMMERCIAL_STORAGE_LIST@@`
  (VA `0xc1e918`), `.?AUSMSG_TAKEOUT_COMMERCIAL_ITEM@@` (VA `0xc15a08`) — les trois familles ont
  bien une classe côté client.

**Client 7.3** : `reference/client73` n'est pas un dépôt git ; la référence est le binaire
`reference/client73/SFrame.exe`, sha256
`41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, image de base `0x400000`.
Conversions d'adresses utilisées partout dans cette fiche (relevées sur les en-têtes de sections) :
`.text` VA = offset fichier + `0x400c00` ; `.rdata` VA = offset fichier + `0x401600` ;
`.data` VA = offset fichier + `0x401e00`. Lecture **statique** uniquement (`objdump -d -M intel`,
lecture de tables d'octets) : aucun `SFrame.exe`, aucun script, aucun Lua n'a été exécuté.

Grille `TM_SC_*` / `TM_CS_*` : `op_codes.md:273-277`. Ni 10000 ni 10001 ne sont dans le périmètre
(§9.3, collision MR #33).

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Rien pour 10003 et 10004 : ce sont des paquets poussés

Le client **ne construit jamais** de trame 10003 ni 10004. Recherche exhaustive des ids dans tout
le `.text` désassemblé :

- `0x2713` (10003) : **deux** sites — `0x49e20c` (`cmp eax,0x2713`, répartiteur de messages) et
  `0x6793b7` (`mov eax,0x2713`, table id → nom). Aucun
  `mov WORD PTR [<tampon>+0x4],0x2713` : pas de constructeur de trame.
- `0x2714` (10004) : **six** sites — deux `sub` (`0x49e725`, `0x54fcf6`), un `cmp` (`0x665bfd`),
  la table de noms (`0x6793fa`), le constructeur de **message interne** `0x56fd94` et un objet
  local construit sur la pile (`0x68aebd`). Aucun constructeur de trame.
- `0x2715` (10005) : **sept** sites, dont **un seul constructeur de trame** (`0x48ce60`, dont
  `mov edx,0x2715` VA `0x48ce88`), appelé d'un seul endroit, `0x49dc59`, qui **remplit puis
  envoie** (§2.3). Les autres sont deux constructeurs de message interne (`0x56801e`, `0x5688f0`),
  trois `cmp` (`0x49a88d`, `0x640d21`, `0x66e178`) et la table de noms (`0x67943d`).

Ces deux paquets sont en revanche **attendus** : le convertisseur paquet → message interne du client
possède une branche pour chacun (§3.1, §3.2).

Conclusion : comme pour `TM_SC_REGION_ACK` (fiche `550-get-region-info.md`), le joueur ne déclenche
rien ; c'est le serveur qui décide de pousser le couple (compteurs, liste).

### 2.2 La fenêtre de stockage commercial est locale au client

Le client ouvre la boutique/le conteneur **lui-même**, par commandes internes, sous deux verrous de
configuration de ressource :

1. le bouton « cash » de l'interface passe par `commercial_shop` : `push 0xa2d934` (chaîne
   `"commercial_shop"`) VA `0x68a692`, suivi de `cmp al,0x1 ; jne <sortie>` VA `0x68a6aa` —
   si la clé n'est pas à `1`, rien n'est fait ;
2. le lanceur de commandes `0x4999b0` teste d'abord la clé `"cash"` : `push 0xa1fd0c` VA `0x4999e3`,
   et si elle est active il ne fait rien de plus ;
3. les deux chaînes de commande existent dans `.rdata` : `/cshop` (VA `0xa1fcf8`) et `/cstorage`
   (VA `0xa1fd00`), voisines de `Nothing`, `cash`, `none`, `/assist`, `/run`, `stand_up`
   (région fichier `0x61e6f0`-`0x61e730`).

Le déclencheur du bouton construit un **message interne d'id 0x60** avec la charge utile `1` :
`push 0x17` + `call 0x97671b` (allocation) puis `push 0x1 ; mov ecx,eax ; call 0x56fd50`
(VA `0x68a6b2`-`0x68a6c4`). Le répartiteur de ce message, VA `0x49d747`, lit sa charge utile à
l'offset `+0x13` : `1` → `push 0xa1fd00` (`/cstorage`, VA `0x49d74f`), `2` → `push 0xa1fcf8`
(`/cshop`, VA `0x49d76d`), puis `call 0x4999b0`.

Conséquence directe pour le serveur : **il n'existe aucune demande cliente à recevoir** pour ouvrir
ce conteneur — ni dans `op_codes.md` (aucun id entre 10000 et 10005 n'est un « open » entrant autre
que 10000, hors lot), ni dans rzu, ni dans NGemity. Le serveur ne peut donc pas répondre à une
demande ; il **pousse** (10003 à l'entrée en jeu, cf. §5.2) et le client affiche.

### 2.3 Le geste de retrait (10005)

Le joueur retire un objet de la fenêtre : le client émet alors la trame 10005.

Chaîne complète, telle que lue :

1. la fenêtre crée un message interne d'id `0x5e` (94) portant l'objet visé : `uid` à l'offset
   `+0x13` (4 octets) et `count` à `+0x17` (2 octets) ;
2. le répartiteur de ce message, VA `0x49dc59`, construit la trame :
   `lea ecx,[ebp-0x30] ; call 0x48ce60` (constructeur de trame, VA `0x49dc5c`) ;
3. il recopie les deux champs : `mov [ebp-0x29],ecx` (VA `0x49dc68`, soit trame **+7**) et
   `mov [ebp-0x25],dx` (VA `0x49dc71`, soit trame **+11**) ;
4. il envoie : `mov ecx,[esi+0xb8] ; mov eax,[ecx] ; mov eax,[eax+0xc4] ; push <trame> ; call eax`
   (VA `0x49dc75`-`0x49dc83`) — c'est le même envoi par `[objet+0xb8]`-`vtbl+0xc4` que la trame
   d'erreur de §5.3.

Le constructeur de trame `0x48ce60` est donc la preuve de la forme : `mov DWORD PTR [eax],0x7`
(longueur initiale 7, VA `0x48ce68`), remise à zéro des 9 octets `+4`..`+0xc`, puis
`mov WORD PTR [eax+0x4],0x2715` (ID, VA `0x48ce8d`), `mov DWORD PTR [eax],0xd` (**longueur 13**,
VA `0x48ce93`), puis calcul et écriture du checksum en `+6` (boucle VA `0x48ce9b`-`0x48cea7`,
somme des 6 premiers octets). Aucun autre site n'écrit l'id `0x2715` dans une trame.

## 3. Structure sur le fil

En-tête commun de 7 octets, non gated : `uint32 Length` @0, `uint16 ID` @4, `uint8 Checksum` @6,
somme des octets 0..5. Côté Navislamia : `Game/Network/Packets/Header.cs:6-25` (`[StructLayout(...,
Pack = 1)]`) et la constante `HeaderSize = 7` (`Game/Network/Packets/Game/GameCharacterPackets.cs:19`).
Côté rzu : `TS_MESSAGE` = `{uint32 size; uint16 id; int8 msg_check_sum}` (`librzu/src/lib/Packet/
PacketBaseMessage.h:17-20`, `#pragma pack(push,1)` à `:13`), base d'en-tête **7** pour les paquets
`CREATE_PACKET_VER_ID` (`librzu/src/lib/Packet/PacketDeclaration.h:616-621`). Côté client : le
constructeur `0x48ce60` écrit exactement ces trois champs.

Aucun des trois paquets n'est `_(string)`, `_(count)`-seul ou `_(dynarray)`-seul : il n'y a donc
aucun piège de longueur implicite dans cette famille (contrairement à 10001/10002, cf. fiche
`10000-open-item-shop.md` §3.2).

### 3.1 `TM_SC_COMMERCIAL_STORAGE_INFO` (10003) — serveur → client — **11 octets**

| Offset | Taille | Type | Nom | Source |
|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` = **11** | le client n'a **aucun** constructeur de trame 10003 : longueur déduite des 4 octets lus (§3.1) et de la convention « `Length` = total de la trame », établie par le constructeur 10005 (`mov DWORD PTR [eax],0xd` VA `0x48ce93`, 13 = 7 + 6) et par Navislamia `Header.cs:9` |
| 4 | 2 | `uint16` | `ID` = **10003** (`0x2713`) | client : table id → nom VA `0x6793b7` ; rzu `TS_SC_COMMERCIAL_STORAGE_INFO.h:12` |
| 6 | 1 | `uint8` | `Checksum` | `Header.cs:11` ; somme des octets 0..5 |
| 7 | 2 | `uint16` | `total_item_count` | client : `mov dx,WORD PTR [ebx+0x7]` VA **`0x67ed48`** → objet `+0x13` ; rzu `:8` ; NGemity `:7` |
| 9 | 2 | `uint16` | `new_item_count` | client : `mov cx,WORD PTR [ebx+0x9]` VA **`0x67ed40`** → objet `+0x15` ; rzu `:9` ; NGemity `:8` |

**Total attendu : 11 octets.** Les deux champs sont de même largeur et l'ordre de déclaration rzu
(`total_item_count` puis `new_item_count`) correspond exactement à l'ordre des offsets lus
(`+7` → objet `+0x13` ; `+9` → objet `+0x15`, où `+0x13` est le premier champ de charge utile de la
classe, cf. §3.2). Le client **ne lit rien** au-delà de l'offset 10.

Réception, côté client : index du convertisseur `0x67ec4e` (`sub eax,0x2711 ; cmp eax,0x3 ;
jmp DWORD PTR [eax*4+0x67f6cc]`), table à `0x67f6cc` = `[0x67ecd4, 0x67ec63, 0x67ed27, 0x67ed64]`
pour 10001..10004 → branche **`0x67ed27`**. Elle alloue 23 octets (`push 0x17` VA `0x67ed27`),
construit la classe par `call 0x66c3d0` (id de message interne `0x5d` = 93, VA `0x66c3d4`), lit les
deux `u16`, puis remet l'objet à la file d'événements du monde (`lea ecx,[esi+0x2c] ;
call 0x64d0e0` VA `0x67ed57`).

### 3.2 `TM_SC_COMMERCIAL_STORAGE_LIST` (10004) — serveur → client — **9 + 10 × n octets**

| Offset | Taille | Type | Nom | Source |
|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` = **9 + 10 × n** | le client n'a **aucun** constructeur de trame 10004 : longueur déduite du `count` @7 et du pas de 10 (VA `0x67ed93`, `0x67edca`), convention `Length` = total (`Header.cs:9`, confirmée par le constructeur 10005) |
| 4 | 2 | `uint16` | `ID` = **10004** (`0x2714`) | table id → nom VA `0x6793fa` ; rzu `TS_SC_COMMERCIAL_STORAGE_LIST.h:20` |
| 6 | 1 | `uint8` | `Checksum` | `Header.cs:11` |
| 7 | 2 | `uint16` | `count` (nombre d'entrées, `items`) | client : `mov cx,WORD PTR [ebx+0x7]` VA **`0x67ed7d`**, relu à `0x67ed85` et `0x67edc5` ; rzu `_(count)(uint16_t, items)` `:16` |
| 9 | 10 × n | `TS_COMMERCIAL_ITEM_INFO[]` | `items` | client : `lea edi,[ebx+0x9]` VA **`0x67ed93`**, pas de boucle `add edi,0xa` VA **`0x67edca`** |

Détail d'une entrée — **10 octets**, contiguë, premier élément à l'offset 9 :

| Offset local | Taille | Type | Nom | Source |
|---|---|---|---|---|
| +0 | 4 | `uint32` | `commercial_item_uid` | client : `mov ecx,DWORD PTR [edi]` VA **`0x67eda3`** ; rzu `:8` ; NGemity `:7` |
| +4 | 4 | `int32` | `code` | client : `mov edx,DWORD PTR [edi+0x4]` VA **`0x67eda0`** ; rzu `:9` ; NGemity `:8` |
| +8 | 2 | `uint16` | `count` | client : `mov ax,WORD PTR [edi+0x8]` VA **`0x67eda5`** ; rzu `:10` ; NGemity `:9` |

**Total attendu : `9 + 10 × n` octets**, soit 9 pour une liste vide, 29 pour deux entrées.
L'en-tête du `_(dynarray)` rzu ne porte aucun préfixe de longueur : `SERIALIZATION_F_COUNT2`
écrit le seul `uint16` (`PacketDeclaration.h:343-345`) et `SERIALIZATION_F_DYNARRAY2` écrit les
éléments un par un (`:335`). `TS_COMMERCIAL_ITEM_INFO` est un `CREATE_STRUCT` (`:12`) ;
`4 + 4 + 2 = 10` octets, ce que confirme le pas de boucle du client (`add edi,0xa`).

Réception, côté client : branche **`0x67ed64`** de la même table. Elle alloue 37 octets
(`push 0x25`), construit la classe par `call 0x672eb0` (id de message interne `0x5c` = 92, VA
`0x672eb4`), écrit `count` à `+0x13`, puis, si `count != 0` (`cmp WORD PTR [ebx+0x7],0x0 ;
jbe <fin>` VA `0x67ed85`), empile `count` structures de 10 octets dans un `std::vector` à `+0x15`
(`call 0x4a60c0` VA `0x67edbd`), et remet l'objet à la file du monde (VA `0x67e13d`).

L'objet de la classe de message interne `0x2714` (constructeur `0x56fd80`, qui écrit un **octet** de
charge utile à `+0x13` : `mov BYTE PTR [eax+0x13],cl` VA `0x56fda8`, vtable `0xa2d7dc` VA `0x56fda2`)
est traité par la branche VA `0x4933a0`, qui **retourne immédiatement** si cet octet est non nul
(`cmp BYTE PTR [eax+0x13],bl ; jne <retour>` VA `0x4933c4`, `bl = 0`) et ne fait quelque chose que
s'il est nul : elle construit alors un message interne `0x42a` (1066) et le remet au client réseau
(VA `0x4933cd`-`0x493447`). Lecture : le client possède un chemin dédié au cas « conteneur vide ».
Le lien exact entre cet octet et le `count` de la trame de §3.2 (dont il ne peut au mieux être que
l'octet de poids faible) reste une **interprétation** : réserve §7k.

### 3.3 `TM_CS_TAKEOUT_COMMERCIAL_ITEM` (10005) — client → serveur — **13 octets**

| Offset | Taille | Type | Nom | Source |
|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` = **13** (`0xd`) | client : `mov DWORD PTR [eax],0xd` VA **`0x48ce93`** ; Navislamia `Header.cs:9` |
| 4 | 2 | `uint16` | `ID` = **10005** (`0x2715`) | client : `mov WORD PTR [eax+0x4],0x2715` VA **`0x48ce8d`** ; rzu `TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:12` |
| 6 | 1 | `uint8` | `Checksum` | client : boucle de somme VA `0x48ce9b`-`0x48cea7`, écriture VA `0x48cea7` ; `Header.cs:11` |
| 7 | 4 | `uint32` | `commercial_item_uid` | client : `mov [ebp-0x29],ecx` VA **`0x49dc68`** (tampon à `ebp-0x30` → offset 7) ; rzu `:8` ; NGemity `:7` |
| 11 | 2 | `uint16` | `count` | client : `mov [ebp-0x25],dx` VA **`0x49dc71`** (offset 11) ; rzu `:9` ; NGemity `:8` |

**Total attendu : 13 octets.** Le constructeur remet à zéro les 9 octets `+4`..`+0xc` avant
d'écrire l'id : la charge utile est bien de 6 octets et il n'y a **aucun** octet de remplissage
entre `Length`/`ID` et `commercial_item_uid`.

### 3.4 Ce que le client lit, et ce qu'il ne lit pas

- **10003** : les octets 7 à 10 seulement. Rien après l'offset 10 n'est consulté.
- **10004** : le client **fait confiance** au champ `count` et ne compare jamais `count` à la
  longueur annoncée. Un `count` supérieur au nombre réel d'entrées fait avancer le lecteur au-delà
  de la charge utile annoncée (`add edi,0xa` boucle sur le même tampon). Le serveur doit donc
  écrire **exactement** `9 + 10 × count` octets et les compter dans `Length`.
- **10005** : aucune contrainte de lecture côté client (c'est lui qui émet) ; le serveur, lui, doit
  vérifier `Length == 13` avant de lire quoi que ce soit (§5.4).
- Aucun des trois paquets ne porte de champ `_(string)` ni de préfixe de longueur, donc aucun des
  pièges `limit_*` de `CLAUDE.md` ne s'applique ici.

## 4. Gating de version

`EPIC_7_3 = 0x070300` (`librzu/src/lib/Packet/PacketEpics.h:59`) et
`EPIC_9_6_3 = 0x090603` (`:96`, commentaire « GS packet ID modified with version 20200713 »).
`0x070300 < 0x090603` : **Epic 7.3 est dans la branche `version < EPIC_9_6_3`** des trois
en-têtes. Décision, prise une fois pour les trois :

| Paquet | Id rzu `< EPIC_9_6_3` | Id rzu `>= EPIC_9_6_3` | Id **retenu 7.3** | Source rzu |
|---|---|---|---|---|
| `TS_SC_COMMERCIAL_STORAGE_INFO` | 10003 | 9003 | **10003** | `TS_SC_COMMERCIAL_STORAGE_INFO.h:12-13` |
| `TS_SC_COMMERCIAL_STORAGE_LIST` | 10004 | 9004 | **10004** | `TS_SC_COMMERCIAL_STORAGE_LIST.h:20-21` |
| `TS_CS_TAKEOUT_COMMERCIAL_ITEM` | 10005 | 9005 | **10005** | `TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:12-13` |

**Gating de champ : aucun.** Les trois `_DEF` de rzu ne contiennent pas un seul `version >= EPIC_*` :
tous les champs listés en §3 existent en 7.3, aucun n'est exclu. NGemity, compilé en
`EPIC_4_1_1` (`shared/Common/Define.h:25`), déclare les mêmes champs sans gating : les deux
références concordent, la question ne se pose pas.

**Piège des alias 9003/9004/9005 (à ne pas confondre).** rzu réutilise ces trois ids pour cette
famille à partir d'`EPIC_9_6_3`. Or en 7.3 ils appartiennent déjà à la famille « numéro de
sécurité », qui elle-même déménage à partir du même seuil :

| Id | Propriétaire **en 7.3** | Source | Devient | Source |
|---|---|---|---|---|
| 9004 | `TM_SC_REQUEST_SECURITY_NO` | `op_codes.md:270` ; rzu `TS_SC_REQUEST_SECURITY_NO.h:9` | 8104 | `:10` |
| 9005 | `TM_CS_SECURITY_NO` | `op_codes.md:271` ; rzu `TS_SECURITY_NO.h:14` | 8105 | `:15` |
| 9003 | aucun propriétaire dans l'inventaire du dépôt | `grep -n "\[90" op_codes.md` → 9000, 9001, 9004, 9005 seulement | 9003 (cette famille) | `:13` |

Conséquence : **ne jamais** utiliser 9003/9004/9005 comme ids de ce socle, et ne pas s'étonner que
la fiche `9005-security-no.md` (branche `hermes/packet-9005-security-no`) traite le même nombre pour
un autre paquet. Le `9005` de rzu `>= 9.6.3` n'a **rien** à voir avec `TM_CS_SECURITY_NO` : c'est
un simple recyclage d'id après la bascule de la famille sécurité. La duplication d'id est donc
résolue par la version, pas par le sens.

## 5. Traitement attendu

### 5.1 Ce que NGemity fait des trois paquets : rien

- déclarations seules : `shared/Server/Packets/GameClient/TS_SC_COMMERCIAL_STORAGE_INFO.h:6-8`,
  `…_LIST.h:6-17`, `TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:6-8` ;
- inclusions seules : `shared/Server/XPacket.h:164,214,215` ;
- `grep -rni "commercial" Chihiro/src` → **aucun résultat** ; `grep -rni "commercial\|itemshop\|
  takeout" Chihiro/src` → **aucun résultat** ;
- aucun modèle de données : `grep -rn "PaidItem\|ItemKeeping" Chihiro/` ne remonte que
  `m_nItemKeepingID` (`src/Entities/Item/ItemInstance.h:60,104`), c'est-à-dire la clé de la liste de
  garde d'objets (enchères/courrier), pas ce conteneur.

**Il n'y a donc aucune logique serveur de référence à porter pour cette famille.** Les seuls faits
serveur établis sont ceux du client (§2, §3) et la seule émission de référence est celle de rzu
(§5.2).

### 5.2 Ce que rzu fait : une 10003 à zéro à l'entrée en jeu

`rzu/rzgame/src/Component/Character/Character.cpp:308-311` :

```
TS_SC_COMMERCIAL_STORAGE_INFO commercialStorage;
commercialStorage.new_item_count = 0;
commercialStorage.total_item_count = 0;
session->sendPacket(commercialStorage);
```

Placement : à la fin de la séquence d'entrée en jeu, juste après `TS_SC_WEATHER_INFO` (`:303-306`)
et juste avant `client_info` (`:313`). `grep -rni commercial rzu/rzgame/src rzu/librzu/src` ne
remonte **aucun autre** usage : rzu n'émet jamais 10004 et ne traite jamais 10005. Le conteneur de
référence est donc, littéralement, **vide et constant**.

C'est un fait important pour la lecture de ce socle : rzu déclare le conteneur comme **par
personnage** (l'émission est dans le cycle d'entrée en jeu du personnage), mais ne le modélise pas.

### 5.3 Ce que le serveur Navislamia doit faire

**Pousser 10003 à l'entrée en jeu.** Ancre d'insertion : fin de la séquence d'entrée en jeu
documentée en `docs/character-bootstrap.md:52-67`, au même endroit que rzu — dans
`Game/Network/Clients/Actions/GameActions.cs`, juste avant l'envoi final `client_info` (ligne 235)
de la méthode d'entrée en jeu. La trame fait 11 octets.

**Pousser 10004 quand — et seulement quand — le contenu change.** Aucun producteur n'existe dans ce
dépôt (§9.2), donc le contenu est vide. Une liste vide est une trame de **9 octets** dont le
`count` vaut 0 ; c'est un état que le client traite explicitement (§3.2). rzu ne l'émet pas :
l'émission est donc un choix, pas un précédent — voir la réserve §7d et le point de veto §9.1.

**Recevoir 10005 sans jamais l'émettre.** Le serveur lit `commercial_item_uid` (offset 7) et
`count` (offset 11) après avoir vérifié `header.Length == 13`.

Le serveur **ne doit jamais émettre 10005**. Preuve lue dans le client : la branche du répartiteur
de messages internes pour `0x2715` (VA `0x49e72f`) appelle `0x48d2f0`, qui construit une trame de
**7 octets** d'id **27** (`mov WORD PTR [ebp-0x4],0x1b` VA `0x48d307`, en-tête `mov DWORD PTR
[ebp-0x8],0x7` VA `0x48d30b`, checksum VA `0x48d318`-`0x48d328`) et l'envoie par le même chemin
`[objet+0xb8]`→`vtbl+0xc4` (VA `0x48d322`-`0x48d337`) : `27` est `TM_CS_LOGOUT` (`op_codes.md:28`).
Autrement dit, le seul traitement identifié d'un message interne `0x2715` dans le binaire est une
**sortie de jeu**. La lecture exacte de ce chemin interne reste une interprétation (réserve §7i),
mais la conclusion opérationnelle n'en dépend pas : aucune émission serveur de 10005 n'est
justifiable, et un test doit verrouiller cette absence.

**Ne rien inventer sur le retrait.** Ni coût, ni plafond, ni code de résultat, ni
acquittement : aucune des deux références n'en a (réserve §7b et §7e).

### 5.4 Décision de code pour le lot 1 (à implémenter tel quel)

1. **Déclarer** les trois membres dans `Game/Network/Packets/Enums/GamePackets.cs`, à la suite de
   `TM_CS_REPORT = 8000` et **avant** `TM_NONE = 9999` (fin de fichier, `:99-101`) :
   `TM_SC_COMMERCIAL_STORAGE_INFO = 10003`, `TM_SC_COMMERCIAL_STORAGE_LIST = 10004`,
   `TM_CS_TAKEOUT_COMMERCIAL_ITEM = 10005`. Aucun de ces nombres n'existe dans l'énumération
   aujourd'hui (contrôle : `grep -n "1000" GamePackets.cs` ne remonte que `TM_SC_STAT_INFO = 1000`).
2. **Dispatcher les trois, en même temps que la déclaration** (critère transversal 4 : sans bras,
   le membre atteint le `throw` final `Game/Network/Clients/GameClient.cs:802`) :
   - 10003 et 10004 sont serveur → client et ne sont **jamais** émis par le client 7.3 : reprendre
     l'idiome déjà écrit pour `TM_SC_REGION_ACK` (`GameClient.cs:620-627`), `_logger.Warning(...)`
     + `continue`, aucun corps lu ;
   - 10005 est client → serveur : lire la trame, vérifier `header.Length == 13`, journaliser
     `uid` + `count`, et **ne rien répondre** dans ce lot.
3. **Poser les bras à côté de `TM_SC_REGION_ACK`**, pas à l'ancre du `switch` final : 29 branches
   ouvertes touchent déjà `GamePackets.cs`, et l'ancre du `switch` est un point chaud mesuré (§9.3).
4. **Constructeurs de trame** dans `Game/Network/Packets/Game/` (idiome du dépôt :
   `CreatePacket(GamePackets.X, HeaderSize + n)` puis `BinaryPrimitives.Write*LittleEndian(...)` puis
   `WriteChecksum(packet)`, modèle `GameCharacterPackets.cs:178-185`) :
   - `BuildCommercialStorageInfo(ushort totalItemCount, ushort newItemCount)` → 11 octets ;
   - `BuildCommercialStorageList(IReadOnlyList<(uint uid, int code, ushort count)> items)` →
     `9 + 10 × items.Count` octets, `count` à 7, première entrée à 9.
5. **Émettre** `BuildCommercialStorageInfo(0, 0)` à l'entrée en jeu (§5.3).
6. **Tests d'offsets obligatoires** (critère transversal 3) : 10003 = 11 octets avec
   `Length`, `ID`, `Checksum`, `total_item_count` @7, `new_item_count` @9 ; 10004 = 9 octets à zéro
   entrée et 29 octets à deux entrées, avec `count` @7, puis `(uid, code, count)` aux offsets
   9/13/17 et 19/23/27 ; 10005 = rejet d'une trame de longueur ≠ 13 et lecture correcte d'une trame
   de 13 octets.
7. **Ne pas toucher 10000/10001/10002** (MR #33 ouverte, hors lot).

### 5.5 Cas limites

| Cas | Comportement retenu | Pourquoi |
|---|---|---|
| 10004 avec `count = 0` | trame de 9 octets, valide | le client traite explicitement l'état vide (§3.2) |
| 10004 avec `count` incohérent | jamais émis ainsi | le client ne recoupe pas `count` et `Length` (§3.4) |
| 10003 reçue par le serveur | `Warning` + `continue` | paquet serveur → client, jamais émis par le client 7.3 |
| 10005 de longueur ≠ 13 | journaliser et ignorer le corps | le serveur est le seul garde-fou : le client n'a pas de contrainte sortante |
| 10005 avec un `uid` inconnu | **non tranché** | aucune politique de retrait n'est établie (§7b, §9.2) |
| `count` de 10005 supérieur au stock | **non tranché** | idem ; ne pas choisir de plafond à la place de Killian |

## 6. Écarts assumés avec NGemity, et pourquoi

| Point | NGemity | Navislamia (proposé) | Pourquoi |
|---|---|---|---|
| Traitement | trois en-têtes déclarés, **aucun** handler | ids déclarés **et** dispatchés | le projet exige qu'aucun membre de `GamePackets` ne tombe dans le `throw` final (`GameClient.cs:802`) ; NGemity n'a pas cette contrainte et n'a rien à porter |
| Gating de version | `CREATE_PACKET(…, 10003/10004/10005)` sans gating, compilé `EPIC_4_1_1` | mêmes ids que la branche rzu `< EPIC_9_6_3`, **sans** les alias 9003/9004/9005 | NGemity est antérieur à la bascule `9.6.3` ; le suivre sans lire rzu aurait laissé croire que les ids sont stables |
| Persistance du conteneur | aucune (pas de modèle) | `NON ÉTABLI` en lot 1, prérequis exact en §9.2 | aucune des deux références ne modélise ce conteneur ; le dump officiel offre deux tables candidates (`PaidItem`, `ItemKeeping`), c'est une décision de Killian |
| Politique de retrait | aucune | aucune en lot 1 | rien n'est établi : ni coût, ni plafond, ni acquittement (§7b) |
| Nom des fichiers de structure | `TS_SC_COMMERCIAL_STORAGE_INFO.h`, etc. | mêmes noms, préfixes `TM_` côté énumération | convention du dépôt (`op_codes.md`, `GamePackets.cs`) |

## 7. `NON ÉTABLI` / `A VERIFIER PAR KILLIAN`

**a) Que signifie exactement `new_item_count` ?** Aucune référence ne le renseigne (rzu envoie 0
en dur, NGemity ne le traite pas) et le client ne fait que le transporter (§3.1 : il devient le
champ `+0x15` du message interne 93, dont je n'ai pas identifié le consommateur d'interface).
Hypothèse non vérifiée : « entrées jamais affichées/confirmées », le dump officiel ayant une
colonne `PaidItem.confirmed` (`reference/ngemity/Database/Telecaster.sql:435`).
*Question à trancher : que compte ce champ, et qui l'incrémente puis le remet à zéro ?*
Recommandation de la fiche : **0 tant que le conteneur est vide** — c'est la valeur de rzu, donc
non inventée.

**b) Politique de retrait.** Ni coût, ni droit d'accès, ni plafond de `count`, ni condition de
niveau, ni délai, ni acquittement n'apparaissent dans les deux références serveur, et le client
n'en révèle aucun (§2.3 : le geste produit la trame et rien d'autre).
*Questions précises : le retrait est-il gratuit ? peut-il demander plusieurs exemplaires d'un
coup (`count > 1`) ? le serveur doit-il répondre quelque chose, et quoi ?*
Recommandation de la fiche : **aucune réponse inventée** en lot 1. Si Killian veut un refus
explicite, les codes existants les plus proches de l'énumération sont `ResultCode.NotExist` (1),
`NotOwn` (3) et `Misc` (4) (`Game/Network/Packets/ResultCode.cs:8,10,11`) — **aucun n'est établi
pour cette famille**, ne pas en choisir un sans décision.

**c) Origine du `commercial_item_uid`.** Le client ne fait que recopier l'`uint32` reçu dans la
10004 vers la 10005 (même nom, même largeur : §3.2 +0, §3.3 offset 7). Rien n'indique sa source
serveur. Hypothèse non vérifiée : l'identifiant de la ligne de conteneur (par exemple
`PaidItem.sid`, `Telecaster.sql:420`). *Question : sur quelle clé le serveur retrouve-t-il l'objet
à partir de cet uid, et comment garantit-il qu'il appartient bien au personnage qui demande ?*
C'est un point de **sécurité** : sans réponse, le retrait ne peut pas être implémenté sans risque
de vol d'objet entre personnages/comptes.

**d) Faut-il émettre une 10004 vide à l'entrée en jeu ?** rzu n'émet **que** la 10003 (deux
compteurs à 0) et jamais la liste. Le client accepte pourtant explicitement une liste vide (§3.2).
Deux lectures : (i) la 10004 n'est envoyée qu'au moment où la fenêtre s'ouvre — mais aucune
demande cliente n'existe pour ouvrir ce conteneur (§2.2), donc le serveur décide seul ; (ii) la
paire doit être cohérente et la liste vide accompagne la 10003 à 0/0.
Recommandation de la fiche : **émettre la paire** (11 o puis 9 o), et laisser à Killian le soin de
la retirer d'une ligne si l'observation du client le contredit.

**e) La confirmation du retrait passe-t-elle par 10004 ou par un motif dédié ?** Pas établi.
Le seul effet lisible côté client après un retrait est la mise à jour d'inventaire ordinaire :
`TS_SC_INVENTORY` (207) et `TS_SC_UPDATE_ITEM_COUNT` (255) existent déjà dans Navislamia
(`GamePackets.cs:31,43` ; constructeurs `Game/Network/Packets/Game/GameCharacterPackets.cs:187-203`).
Utiliser ces motifs-là plutôt qu'inventer un acquittement est la recommandation, mais **aucune
capture ne le prouve**.

**f) Modèle de persistance.** Aucune des deux références ne modélise ce conteneur. Le dépôt
Navislamia possède `ItemStorageEntity` (anciennement `ItemKeeping`, `Game/DataAccess/Entities/
Telecaster/ItemStorageEntity.cs:9-27`) mais son `StorageType` ne contient que des motifs
d'enchères/courrier (`…/Enums/StorageType.cs:3-17` : `ItemBySuccessfulBid`…`GoldByItemSoldOut`),
donc **pas** ce conteneur. Le dump officiel de référence propose deux tables candidates :
`ItemKeeping` (`reference/ngemity/Database/Telecaster.sql:403-414`) et `PaidItem`
(`:419-439`, colonnes `rest_item_count`, `confirmed`, `taken_*` — profil « objet de boutique
livré, puis retiré »).
*Questions : réutiliser `ItemStorages` en étendant `StorageType`, ou introduire une entité
`PaidItem` calquée sur le dump ? La migration est-elle acceptable dans ce lot ?*
Recommandation de la fiche : **ne rien créer tant que rien ne peut alimenter le conteneur** (§9.2).

**g) Aucun producteur de contenu.** La boutique web (`TM_SC_OPEN_ITEM_SHOP`, 10001) est hors lot
(elle dépend d'un backend de magasin absent) et `grep -rn "COMMERCIAL" --include=*.cs Game/` ne
remonte rien : rien, dans ce dépôt, ne peut faire entrer un objet dans ce conteneur.
*Question : Navislamia a-t-il vocation à servir de boutique (commande MJ, don GM, achat interne) ?*
Tant que non, la seule valeur exacte pour les compteurs est **0** et la seule liste exacte est
**vide**.

**h) Les verrous de configuration du client.** L'ouverture de la fenêtre dépend des clés de
ressource `commercial_shop` (VA `0xa2d934`) et `cash` (VA `0xa1fd0c`) (§2.2). Je n'ai pas pu
établir ce que contient le fichier de ressources du serveur officiel, ni si ces clés sont
actives par défaut en 7.3 : les archives `data.001`-`data.008` du client (scripts NUI/Lua) ne sont
pas présentes sur le VPS (`reference/README.md:9`). *Conséquence : l'affichage effectif de la
fenêtre n'est pas démontrable ici* — seule la présence des chemins dans le binaire l'est. Même
réserve que la fiche `10000-open-item-shop.md` §7e.

**i) Le chemin interne du client pour un message `0x2715`.** La branche VA `0x49e72f` appelle
`0x48d2f0`, qui envoie une trame 27 (`TM_CS_LOGOUT`). Je l'interprète comme un garde
« paquet entrant interdit », mais le binaire emploie cinq numérotations internes voisines
(`0x5c` = 92, `0x5d` = 93, `0x5e` = 94, `0x60` = 96, plus `0x2713`/`0x2714`/`0x2715`) dont je n'ai
pas reconstitué la table complète. L'interprétation est donc une lecture, pas une preuve.
*Question : un serveur qui émettrait 10005 provoque-t-il vraiment une sortie de jeu ?*
Recommandation : s'abstenir d'émettre, quelle que soit la réponse.

**j) Statut des ids voisins.** `op_codes.md:277` s'arrête à 10005 : ni 10002
(`TS_SC_OPEN_PAID_STORAGE`, utilisé par la fiche 10000 comme contre-exemple d'idiome), ni 9003 ne
sont inventoriés. La fiche `10000-open-item-shop.md` §7f relève en outre que le client porte des
entrées `10010`, `10011` et `10012`, elles aussi absentes de `op_codes.md` : l'inventaire d'ids du
dépôt est incomplet au-delà de 10005. À savoir pour les lots suivants ; sans effet ici.

**k) Comment le client décide-t-il qu'une 10004 est « vide » ?** La branche VA `0x4933a0`
(§3.2) ne teste qu'un **octet** de l'objet de message interne `0x2714`, pas la trame ; je l'intègre
comme « cas liste vide », mais le chemin qui relie le `count` de la trame à cet octet n'est pas
reconstitué. *Conséquence pratique : rien de ce que le lot 1 émet n'en dépend* — la seule chose qui
compte est le couple (`count` = 0, `Length` = 9, `ID` = 10004), qui est lui établi par le
convertisseur (`jbe` VA `0x67ed85` : `count` nul = zéro itération, aucune lecture d'entrée).
*Question : une capture de trafic trancherait ; sinon, la prudence est de n'émettre la 10004 vide
que si l'observation du client le confirme (réserve §7d).*

## 8. Commits et binaires épinglés

| Référence | Commit / empreinte | Usage dans cette fiche |
|---|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | structures et ids (§1, §3), gating et `EPIC_7_3`/`EPIC_9_6_3` (§4), base d'en-tête 7 et `TS_MESSAGE` (§3), sémantique `_(count)`/`_(dynarray)` (§3.2), unique émission de référence (§5.2) |
| `reference/ngemity` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | déclarations et **absence** de logique (§5.1), version compilée `EPIC_4_1_1` (§4), schéma officiel `Telecaster.sql` (`ItemKeeping`, `PaidItem`) (§6, §7f) |
| `reference/client73/SFrame.exe` | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (`reference/client73` n'est pas un dépôt git) | tailles et offsets réels (§3, §2.3), direction de chaque paquet (§2), verrous d'interface (§2.2, §7h), garde entrant (§5.3, §7i) |
| `Navislamia` `master` | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | état de l'énumération et du dispatch (§5.4), modèle `Header` (§3), conventions de constructeurs de trame (§5.4), point d'insertion (§5.3) |

**Fiche sœur à lire avant de coder**, et son chemin réel : le socle voisin « entrepôt du
personnage » est `docs/packet-specs/211-212-storage.md` sur la branche
`hermes/packet-socle-entrepot-personnage` (MR #20, non mergée) —
`git show origin/hermes/packet-socle-entrepot-personnage:docs/packet-specs/211-212-storage.md`.
Le nom `socle-entrepot-personnage.md` cité par la carte de suivi n'existe pas : à corriger du côté
du board. Il y a aussi `docs/character-bootstrap.md` (§52-67) pour la séquence d'entrée en jeu.

## 9. Découpage recommandé pour le dev

### 9.1 Lot 1 — livrable d'un bloc, sans donnée inventée

Le protocole, complet et testé :

1. trois ids déclarés dans `GamePackets` et trois bras de dispatch (§5.4) ;
2. `BuildCommercialStorageInfo` (11 o) et `BuildCommercialStorageList` (9 + 10n) avec tests
   d'offsets ;
3. émission de la 10003 à `0/0` à l'entrée en jeu (précédent rzu, `Character.cpp:308-311`, ancre
   `GameActions.cs:235`) et, sous réserve §7d, d'une **10004 vide** dans la foulée ;
4. réception de la 10005 : contrôle de longueur, journalisation `uid`/`count`, consommation exacte
   de la trame, **aucune réponse** ;
5. verrou de test : le serveur n'émet **jamais** 10005 (§5.3, §7i).

Ce que le lot 1 ne fait pas, et l'assume : le conteneur reste **vide par construction** — aucune
boutique ne l'approvisionne (§7g) — donc aucun modèle de données n'est nécessaire pour que ce lot
soit *exact*. Les compteurs `0` ne sont pas une estimation : c'est la valeur de rzu et la seule
valeur vraie dans un dépôt sans producteur.

**Point de veto pour Killian (une ligne)** : l'émission de la 10004 vide (réserve §7d). Si elle est
écartée, le constructeur et son test restent (contrat documenté), et la liste n'est simplement pas
émise.

### 9.2 Lot 2 — conteneur persisté et retrait réel : prérequis exact

Non livrable dans ce lot, et non bloquant pour le lot 1. Il faut **deux** choses :

1. **un modèle de conteneur** (table + entité + accès), inexistant :
   - voie A — table officielle `PaidItem` (`reference/ngemity/Database/Telecaster.sql:419-439`),
     profil « objet de boutique livré puis retiré » (`rest_item_count`, `confirmed`, `taken_*`) ;
   - voie B — réutiliser `ItemStorages` / `ItemStorageEntity`
     (`Game/DataAccess/Entities/Telecaster/ItemStorageEntity.cs:9-27`) en étendant `StorageType`
     (`…/Enums/StorageType.cs:3-17`), aujourd'hui limité aux enchères et au courrier ;
   - dans les deux cas : migration EF et décision sur la clé qui sert de `commercial_item_uid`
     (réserve §7c, point de sécurité) ;
2. **un producteur** : sans boutique ni commande d'administration, le conteneur reste vide
   (réserve §7g). C'est la dépendance qui commande tout le lot 2.

La carte `RidVfIoI` (10005) parkée en `THINKING` peut être rouverte dès le lot 1 livré : elle
dispose maintenant de l'opcode, du format exact (13 o, uid @7, count @11), du gating 7.3 tranché et
de la direction prouvée. Ce qui lui manquera est **la** politique de retrait (§7b) et la clé du
conteneur (§7c) — pas l'archéologie.

### 9.3 Zones de collision

- `Game/Network/Packets/Enums/GamePackets.cs` : **29 branches ouvertes** le modifient
  (`git diff --name-only origin/master...<branche>`), dont `hermes/packet-10000-open-item-shop`
  (MR #33) qui y déclare `TM_CS_OPEN_ITEM_SHOP = 10000`. **Contrôle à faire au réveil du dev** :
  `git show origin/master:Game/Network/Packets/Enums/GamePackets.cs | grep -n 1000`, puis, si la
  MR #33 est mergée entre-temps, **ne pas redéclarer 10000**.
- `Game/Network/Clients/GameClient.cs` : point chaud de l'ancre du `switch` final — au moins cinq
  branches y insèrent leur bras au même endroit (mesure de la fiche `10000-open-item-shop.md`
  §9.7). Poser les bras près de `TM_SC_REGION_ACK` (`GameClient.cs:620-627`).
- `Game/Network/Packets/Game/GameCharacterPackets.cs` : accueille les constructeurs d'inventaire et
  d'objets ; le constructeur 10003/10004 peut y aller ou dans un fichier voisin, mais vérifier
  avant de créer un nouveau fichier de paquets `TS_SC_COMMERCIAL_*` (le dépôt regroupe par
  domaine, ex. `GameSpawnPackets.cs`, `GameStatPackets.cs`).

### 9.4 Bloc prêt à coller dans `CLAUDE.md`

> À porter par la QA dans la description de la MR : le dev **n'écrit pas** `CLAUDE.md` (fichier
> protégé). Le bloc sera actualisé par la QA quand le lot 1 sera livré.

```markdown
### Socle stockage commercial — `TM_SC_COMMERCIAL_STORAGE_INFO` (10003), `TM_SC_COMMERCIAL_STORAGE_LIST` (10004), `TM_CS_TAKEOUT_COMMERCIAL_ITEM` (10005)

- 7.3 = **10003 / 10004 / 10005** : rzu bascule cette famille sur 9003/9004/9005 à partir
  d'`EPIC_9_6_3` (`TS_SC_COMMERCIAL_STORAGE_INFO.h:12-13`, `…_LIST.h:20-21`,
  `TS_CS_TAKEOUT_COMMERCIAL_ITEM.h:12-13`) et `EPIC_7_3 = 0x070300` est sous `0x090603`.
  **Piège** : en 7.3, 9004 et 9005 désignent déjà la famille « numéro de sécurité »
  (`op_codes.md:270-271`) — ne jamais s'en servir comme ids de ce socle. Aucun champ de ces trois
  paquets n'est gated par version.
- Tailles, mesurées sur le client 7.3 : 10003 = **11 octets** (`total_item_count` u16 @7,
  `new_item_count` u16 @9) ; 10004 = **9 + 10 × n** (`count` u16 @7, puis n entrées de 10 octets =
  `uint32 commercial_item_uid` @0, `int32 code` @4, `uint16 count` @8, première entrée à l'offset 9) ;
  10005 = **13 octets** (`uint32 commercial_item_uid` @7, `uint16 count` @11).
- Le client 7.3 **ne recoupe jamais** le `count` de la 10004 avec `Length` : écrire exactement
  `9 + 10 × count` octets. Une liste vide (9 octets, `count = 0`) est un état traité explicitement
  par le client.
- **Le serveur n'émet jamais 10005.** La seule trame 10005 du client est un envoi (constructeur de
  trame client VA `0x48ce60`, appelé une fois depuis l'émetteur VA `0x49dc59`), et le seul
  traitement identifié d'un message interne `0x2715` en réception est un envoi de `TM_CS_LOGOUT`
  (`27`). Côté serveur : lecture stricte (`Length == 13`), journalisation, aucune réponse.
- Aucune demande cliente n'ouvre ce conteneur : la fenêtre est locale au client (commandes
  `/cshop` / `/cstorage`, verrous de ressource `commercial_shop` et `cash`). Le serveur **pousse** la
  10003 à l'entrée en jeu, comme rzu (`Character.cpp:308-311`, à `0/0`).
- **Aucune référence n'implémente la logique du conteneur** : NGemity ne traite rien, rzu n'émet
  qu'une 10003 constante. Sans boutique, le conteneur est **vide par construction** ; ne rien
  inventer sur le retrait (coût, plafond, code de résultat, acquittement) — décisions ouvertes dans
  la fiche.
- Le savoir durable de ce socle est dans `docs/packet-specs/socle-stockage-commercial.md`, pas ici.
```

## 10. Implémentation livrée (dev) — lot 1

Commit **`8c059e2`** sur cette branche (`hermes/packet-socle-stockage-commercial`), au-dessus de la
fiche `23b6140`. Le **lot 1 de §9.1 est livré en entier** ; le lot 2 (§9.2) n'est pas ouvert et rien
n'y a été préparé.

| Fichier | Ce qui y est livré |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | les trois membres ajoutés **en fin d'énumération** (`TM_CS_REPORT = 8000`, puis `:101-103`, avant `TM_NONE = 9999`), conformément à §5.4.1 : la queue du fichier n'est touchée par aucune des branches en collision citées en §9.3, contrairement à la zone d'insertion après `TM_CS_VERSION` |
| `Game/Network/Packets/Game/GameCommercialStoragePackets.cs` | fichier **nouveau**, comme les autres socles (`GameTradePackets.cs`, `GameWeatherPackets.cs`, `GameAuctionPackets.cs`) : `CommercialStorageInfoSize = 11`, `CommercialStorageListHeaderSize = 9`, `CommercialStorageItemSize = 10`, `BuildCommercialStorageInfo(ushort totalItemCount, ushort newItemCount)` et `BuildCommercialStorageList(IReadOnlyList<(uint Uid, int Code, ushort Count)> items)` — signature tuple **telle que dictée par §5.4.4**, sur le modèle de `BuildEraseItem` (`(uint Handle, long Count)`) |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `TakeoutCommercialItemRequest(uint Uid, ushort Count)` et `TryReadTakeoutCommercialItem(…)` : `uid` @7, `count` @11, toute longueur ≠ 13 refusée (idiome exact de `TryReadGetRegionInfo`, §3.4) |
| `Game/Network/Clients/GameClient.cs` | deux bras **posés à côté de `TM_SC_REGION_ACK`** (`:652-668`), pas à l'ancre du `switch` final (§5.4.3) : `Warning` + `continue` pour 10003/10004, appel de `HandleTakeoutCommercialItem` pour 10005 ; la méthode (`:208`) est voisine de `HandleGetRegionInfo` |
| `Game/Network/Clients/Actions/GameActions.cs` | la 10003 à `0/0` **juste avant `client_info`** (`:240`), puis la 10004 vide (`:245-246`) : ancre de §5.3 |
| `Tests/Game/CommercialStoragePacketsTests.cs` | 23 tests d'offsets, détail en §10.2 |

### 10.1 Décisions prises par le dev

1. **Émission de la 10004 vide : retenue**, comme le recommande §7d. Elle tient en une seule ligne
   (`GameActions.cs:245-246`) et son retrait ne touche que l'appel : le constructeur et ses tests
   restent le contrat documenté. Point de veto inchangé (§9.1), matériellement à une ligne de la
   décision de Killian.
2. **Aucune réponse à la 10005** : le serveur journalise `uid` + `count` et s'arrête là. Aucun coût,
   aucun plafond, aucun `ResultCode` (§7b) et **aucune émission de 10005** (§5.3) : le type
   `GameCommercialStoragePackets` ne porte que les deux constructeurs serveur → client, et un test
   verrouille cette absence (§10.2, dernier point).
3. **Aucun service, aucune entité, aucune migration** : le conteneur est vide par construction, donc
   rien n'est créé pour l'alimenter (§7f, §7g, §9.2). Le lot 1 ne dépend d'aucun producteur, ce qui
   était la condition de livrabilité de §9.1.
4. **Placement des bras de dispatch** : au même endroit que la fiche le demandait, c'est-à-dire
   avant l'ancre du `switch` final et après les bras `TM_CS_GET_REGION_INFO`, pour ne pas ajouter un
   cinquième écrivain à la queue du `switch` (§9.3).
5. **Aucun `limit_*` et aucun `break` implicite** : les trois paquets n'ont ni `_(string)`, ni
   préfixe de longueur (§3), donc les pièges de `CLAUDE.md` ne s'appliquent pas ici ; les deux
   constructeurs écrivent `Length` = taille totale du tableau, comme `CreatePacket` du dépôt.

### 10.2 Ce que les tests verrouillent

Fichier `Tests/Game/CommercialStoragePacketsTests.cs`, 23 cas (le total de la suite passe de 448 à
**471** tests, `dotnet test` en code 0) :

- **ids** : 10003 / 10004 / 10005, les trois définis dans `GamePackets` (sans quoi `OnDataReceived`
  les jette avant tout dispatch) et la note sur la bascule rzu vers 9003/9004/9005 ;
- **10003 = 11 octets** : `Length` @0 = 11, `ID` @4 = 10003, `Checksum` @6 = somme des octets 0..5,
  `total_item_count` @7, `new_item_count` @9, ordre des deux compteurs vérifié par valeurs
  asymétriques (1 puis 2), et aucune écriture au-delà de l'octet 10 ;
- **10004 = 9 + 10 × n** : 9 octets et `count = 0` pour la liste vide ; 29 octets et `count = 2` pour
  deux entrées, avec `(uid, code, count)` en 9/13/17 puis 19/23/27 ; `Length` et `count` cohérents
  pour n = 0, 1, 3, 5 ; ordre `uid` puis `code` (deux `uint32`) vérifié par valeurs distinctes ;
  liste nulle refusée ;
- **10005 = 13 octets** : `uid` @7 et `count` @11 lus, `Length = 13` et `ID = 10005` sur la trame
  client ; toute longueur ≠ 13 refusée (0, 7, 12, 14, 16) en gardant la requête par défaut ;
- **verrou « le serveur n'émet jamais 10005 »** : par réflexion, `GameCommercialStoragePackets`
  n'expose que `BuildCommercialStorageInfo` et `BuildCommercialStorageList`, et les ids qu'ils
  écrivent sont exactement {10003, 10004} ; `GameActionPackets` n'expose qu'un seul membre dont le
  nom contient « Takeout » : le lecteur `TryReadTakeoutCommercialItem` (bool, `ReadOnlySpan<byte>`,
  `out TakeoutCommercialItemRequest`).

**Limite assumée de ce verrou** : il porte sur les deux types du socle, pas sur l'assembly entière.
Un constructeur de 10005 placé ailleurs ne serait pas attrapé par ce test ; il le serait par la
relecture du diffuseur, dont les trois seuls bras pour ces ids sont ceux livrés ici. C'est une
réserve de forme, pas de comportement : aujourd'hui, aucune ligne du dépôt n'écrit l'id 10005 dans
une trame.

### 10.3 Bloc `CLAUDE.md` à jour (à coller par la QA dans la description de la MR)

Le bloc de §9.4 est valable tel quel ; seule la dernière phrase doit décrire l'état livré :

```markdown
- Tailles, telles que livrées : 10003 = **11 octets** (`total_item_count` u16 @7, `new_item_count`
  u16 @9) et 10004 = **9 + 10 × n** (`count` u16 @7, puis n entrées de 10 octets = `uint32`
  `commercial_item_uid` @0, `int32 code` @4, `uint16 count` @8, première entrée à l'offset 9) dans
  `Game/Network/Packets/Game/GameCommercialStoragePackets.cs` ; 10005 = **13 octets** (`uint32`
  `commercial_item_uid` @7, `uint16 count` @11) lus par `GameActionPackets.TryReadTakeoutCommercialItem`,
  seule longueur acceptée. `TM_SC_COMMERCIAL_STORAGE_INFO` est émise à `0/0` à l'entrée en jeu, comme
  rzu, suivie d'une 10004 vide (9 octets, `count = 0`) — cette seconde ligne est une décision de
  Navislamia, rzu ne l'émet pas, et se retire d'une ligne (`GameActions.cs:245-246`).
- Aucun service, aucune entité et aucune migration pour ce conteneur : rien dans le dépôt ne peut
  l'approvisionner, donc sa seule valeur exacte est vide. Les trois bras de dispatch sont posés près
  de `TM_SC_REGION_ACK` (`GameClient.cs:652-668`), jamais à l'ancre du `switch` final.
```

### 10.4 Réserves héritées, inchangées

Aucune réserve de §7 n'est levée par le lot 1 : `new_item_count` (7a), la politique de retrait (7b),
la clé du `commercial_item_uid` (7c), le modèle de persistance (7f), le producteur (7g), les verrous
de configuration du client (7h), la lecture du garde entrant (7i) et la détection du « conteneur
vide » (7k) restent ouvertes. Le lot 1 ne dépend d'aucune d'elles, sauf de 7d qui a été **tranchée
par la fiche elle-même** (émission recommandée) et reste le point de veto de Killian.

## A VERIFIER PAR KILLIAN

1. **`new_item_count` (§7a)** — que compte-t-il, et qui le remet à zéro ? Le lot 1 envoie `0`
   (valeur de rzu, non inventée).
2. **Politique de retrait (§7b)** — coût, droits, plafond sur `count`, réponse attendue. Aucune
   n'est établie par les références ; le lot 1 ne répond rien et ne choisit aucun `ResultCode`.
3. **Clé du `commercial_item_uid` (§7c)** — sur quoi le serveur retrouve-t-il l'objet, et comment
   garantit-il qu'il appartient au personnage ? Point de sécurité à trancher avant tout retrait réel.
4. **Émission d'une 10004 vide à l'entrée en jeu (§7d)** — recommandée pour la cohérence de la
   paire, rzu ne le fait pas ; retirable d'une ligne (point de veto §9.1). **Livrée telle quelle au
   lot 1** : `GameActions.cs:245-246`, une seule ligne à supprimer.
5. **Modèle de persistance (§7f et §9.2)** — `PaidItem` (dump officiel) ou extension de
   `ItemStorages` / `StorageType` ? Le lot 1 ne crée rien : tant qu'aucune boutique n'alimente le
   conteneur, la valeur exacte est zéro.
6. **Producteur (§7g)** — Navislamia doit-il pouvoir approvisionner ce conteneur (boutique
   interne, commande MJ) ? C'est la dépendance qui commande le lot 2 et la réouverture de
   `RidVfIoI`.
7. **Archives `data.00x` absentes (§7h)** — l'affichage effectif de la fenêtre n'est pas
   démontrable sur ce VPS ; seule la présence des chemins dans le binaire l'est.
8. **Lecture du garde entrant (§7i)** — l'interprétation « une 10005 entrante fait quitter la
   partie » est une lecture, pas une preuve. La recommandation (ne jamais émettre) en est
   indépendante.
9. **Détection du « conteneur vide » côté client (§7k)** — la branche qui traite une 10004 vide
   teste un octet d'objet, pas la trame. Rien de ce que le lot 1 émet n'en dépend ; une capture
   trancherait.
