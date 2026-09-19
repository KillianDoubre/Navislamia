# 902 / 903 — `TM_SC_WEATHER_INFO` / `TM_CS_GET_WEATHER_INFO`

Fiche d'archéologie de protocole, Epic 7.3. Écrite en lecture seule sur les références
(`reference/rzu`, `reference/ngemity/Chihiro`, `reference/client73`) — aucun Lua, aucun script du
client, aucun exécutable du client n'a été lancé ; aucune base PostgreSQL n'a été interrogée. Le
client 7.3 tranche le format sur le fil (§3, désérialiseur de la 902) ; rzu tranche les tailles,
l'ordre des champs et le gating (§3, §4) ; NGemity tranche la logique serveur, sous réserve de sa
version compilée (`EPIC_4_1_1`, `reference/ngemity/shared/Common/Define.h:25`, §5.1).

Cette fiche couvre le **socle** demandé par la carte (paire 902/903 : la table `WorldLocation`, la
poussée de la 902, la réponse à la 903). Les cartes suivantes de la chaîne (météo réelle, ciel et
lumière, cycle jour/nuit) ne sont pas tranchées ici ; les colonnes qui les concernent sont
seulement recensées (§5.4).

Résumé des arbitrages, avec leur source :

| Question | Verdict de cette fiche |
|---|---|
| Quels ids pour 7.3 ? | **902** (`TM_SC_WEATHER_INFO`, descendant) et **903** (`TM_CS_GET_WEATHER_INFO`, montant). Les ids `1902`/`1903` sont `version >= EPIC_9_6_3` et hors périmètre (§4). |
| L'en-tête rzu de la 902 existe-t-il pour 7.3 ? | **Oui** : `TS_SC_WEATHER_INFO.h` est présent, sans gating de champ, avec la seule bascule d'id §4. |
| Le client 7.3 consomme-t-il la 902 ? | **Oui**, prouvé par son désérialiseur : `region_id` = `uint32` à l'offset **7**, `weather_id` = `uint16` à l'offset **11** (§3.1). |
| Le client 7.3 émet-il la 903 ? | **Non dans ce binaire** : le seul emplacement de la constante `0x387` est la table id→nom (§2.2). Aucun constructeur de paquet ne porte cet id, contrairement à 900 et 550 (§2.2, point 3). |
| Que porte `region_id` dans la 902 ? | L'**id de `WorldLocation`** (`WorldLocation.id`), c'est-à-dire l'id d'emplacement — **pas** un indice de région de visibilité (celui de 550/11, pas de base 180). Preuve : le seul producteur de référence, NGemity, y met l'id de la table (§5.1), et les ids de la table sont packés `x*10000 + y*100 + n` sur les colonnes `x`/`y` du même enregistrement (§5.4). |
| Que porte `region_id` dans la 903 ? | **`NON ÉTABLI`** : aucun émetteur client, aucune implémentation de référence (§7a/§7b). La seule identité défendable est celle de la 902. |
| Quel `weather_id` pour le socle ? | **0** (`Clear`) : c'est la seule valeur que NGemity envoie (§5.1) et une valeur présente dans les données pour tous les emplacements échantillonnés (§5.4). |
| Qui déclenche la 902 ? | Le **serveur**. rzu l'envoie à l'entrée dans le monde (§5.2) ; NGemity la pousse au changement d'emplacement (§5.1). |
| D'où le socle tire-t-il `region_id` ? | **D'aucune source disponible** : Navislamia n'a ni table `WorldLocation` mappée, ni données de carte (§7c). Le socle envoie `0`, comme rzu (§5.2), et réserve l'appariement position→emplacement à une carte dédiée. |

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id serveur → client | **902** | `op_codes.md:187` ; `reference/rzu/librzu/src/packets/GameClient/TS_SC_WEATHER_INFO.h:12` |
| Nom | `TM_SC_WEATHER_INFO` | `op_codes.md:187` ; `TS_SC_WEATHER_INFO.h:12` |
| Id client → serveur | **903** | `op_codes.md:188` ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_GET_WEATHER_INFO.h:9` |
| Nom | `TM_CS_GET_WEATHER_INFO` | `op_codes.md:188` ; `TS_CS_GET_WEATHER_INFO.h:9` |
| Ids alternatifs | `1902` / `1903` à partir d'`EPIC_9_6_3` — **hors périmètre 7.3** | `TS_SC_WEATHER_INFO.h:12-13` ; `TS_CS_GET_WEATHER_INFO.h:9-10` ; `librzu/src/lib/Packet/PacketEpics.h:59,96` |
| Référence NGemity | `TS_SC_WEATHER_INFO = 902`, `TS_CS_GET_WEATHER_INFO = 903`, sans gating de version | `reference/ngemity/shared/Server/ClientPackets.h:197-198` ; `shared/Server/Packets/GameClient/TS_SC_WEATHER_INFO.h:10` ; `…/TS_CS_GET_WEATHER_INFO.h:9` |
| Voisins de la même famille | 900 `TM_CS_CHANGE_LOCATION`, 901 `TM_SC_CHANGE_LOCATION` (ids 7.3, hors périmètre de cette carte) | `op_codes.md:185-186` ; `rzu/…/TS_CS_CHANGE_LOCATION.h:12` ; `rzu/…/TS_SC_CHANGE_LOCATION.h:12` |
| Taille de la 902 | **13 octets** (7 + `uint32` + `uint16`) | §3.1 |
| Taille de la 903 | **11 octets** (7 + `uint32`) — **attestée par rzu seulement**, aucun lecteur ni émetteur client pour la confirmer | §3.2 |
| État dans Navislamia | **absent** à la date de cette fiche : `grep -rn 'WEATHER' --include=*.cs .` → 0 résultat hors fiches ; `GamePackets` s'arrête à `TM_CS_CHANGE_LOCATION = 900` dans cette famille | `Game/Network/Packets/Enums/GamePackets.cs:65` |

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 La 902 n'est déclenchée par aucune action du joueur

La 902 est **descendante** : c'est le serveur qui décide de l'envoyer. Aucune action d'interface ne
la provoque côté client. Les deux références la produisent dans deux circonstances différentes, et
aucune des deux n'est déclenchée par le joueur directement (§5.1, §5.2) :

* NGemity : au **changement d'emplacement** du personnage (`Player::ChangeLocation`,
  `Chihiro/src/Entities/Player/Player.cpp:1335-1363`), donc en marchant d'un emplacement à un autre ;
* rzu : à l'**entrée dans le monde** (`rzgame/src/Component/Character/Character.cpp:303-306`), donc
  après le choix du personnage.

### 2.2 La 903 : aucun émetteur observable dans le client 7.3

C'est le point le plus délicat de cette fiche, et il est tranché par **absence**, avec la méthode
explicite :

1. la table id→nom du client enregistre bien 903 : la fonction de VA `0x674700` construit
   l'association `0x387` → l'adresse `0xa5309c` de la chaîne `TM_CS_GET_WEATHER_INFO`
   (`push $0xa5309c` à la VA `0x6786ee`, `mov $0x387,%eax` à la VA `0x678710`) ; la même fonction
   enregistre, dans le même bloc, `0x384`→`TM_CS_CHANGE_LOCATION`, `0x385`→`TM_SC_CHANGE_LOCATION`
   et `0x386`→`TM_SC_WEATHER_INFO`. Le client **connaît donc l'id et le nom** ;
2. mais la constante `0x387` **n'apparaît qu'une seule fois** dans toute la désassemblage de
   `SFrame.exe` (`objdump -d SFrame.exe | grep '\$0x387'` → 1 résultat, VA `0x678710`, celui du
   point 1) ;
3. or le client écrit l'id d'un paquet qu'il émet **en immédiat** dans l'en-tête : le constructeur
   de la 900 fait `mov $0x384,%edx` puis `mov %dx,0x4(%eax)` (VA `0x48cdec`-`0x48cdf1`) et
   `movl $0xf,(%eax)` pour la longueur (VA `0x48cdf7`) ; celui de la 550 fait de même
   (`mov $0x226,%edx` VA `0x684b8c`, cf. fiche 550 §3.1). L'énumération des sites
   `mov $<imm>,%edx` immédiatement suivis de `mov %dx,0x4(%eax)` donne **47 valeurs distinctes** —
   `13, 25, 26, 57, 100, 200, 201, 203, 214, 215, 221, 223, 256, 263, 284, 285, 324, 503, 511, 513,
   517, 550, 603, 604, 701, 702, 704, 711, 800, 801, 900, 1202, 3001, 4250, 4251, 4252, 4502, 5000,
   6004, 6006, 8000, 9008, 9010, 10005, 10021, 10023, 50007` — **aucun 903 (`387`)** ;
4. les octets `87 03` n'existent **pas** hors de `.text` : les 13 occurrences de la paire dans le
   fichier sont aux offsets **164569, 930140, 2130431, 2587409, 2937096, 3142556, 3943683, 4213254,
   4293334, 4324186, 4495466, 4758438, 4801857**, tous inférieurs à **6335695** (`0x60accf`, dernier
   octet de `.text`), et une seule est un immédiat voulu (le point 1, offset 2587409) — donc ni
   `.rdata` ni `.data` ne portent une entrée `0x0387` (ni 16 bits ni 32 bits) ;
5. les trois seuls `movb $0x87,…` du binaire sont des gardes de pile
   (`movb $0x87,-0x4(%ebp)`, VA `0x54dbce`, `0x556b93`, `0x67758e`), pas des écritures d'en-tête.

À l'inverse, la **réception** de la 902 est attestée sans ambiguïté : le répartiteur de paquets
choisit sur l'id (`sub $0x386,%eax` à la VA `0x67e603`, table `0x67f448`/`0x67f42c`) et l'index 0
(id 902) mène au handler `0x6711a0` (`call 0x6711a0` à la VA `0x67e663`), tandis que l'index 1
(id 903) mène à la branche par défaut `0x67ef21` — normal pour un id montant, mais cohérent avec
l'absence d'émetteur.

**Conclusion opérationnelle** : dans ce binaire 7.3, la 903 n'est jamais construite, donc jamais
émise. La sémantique de rzu (le client demande la météo d'une région) n'est **pas** observée. Ce
n'est pas une conclusion métier « le paquet n'existe pas » — le client le déclare dans sa table —,
c'est une conclusion de build : rien ne l'émet. Voir §7a pour ce qui pourrait l'infirmer.

## 3. Structure sur le fil

Convention de citation client : toutes les adresses sont des **VA** lues par `objdump -d` sur
`reference/client73/SFrame.exe` (sha256
`41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 o., `pei-i386`,
`.text` VA `0x00401000`, `.rdata` VA `0x00a0f000`, `.data` VA `0x00c10000`). En-tête de 7 octets
partagé avec le serveur : `Length` (`uint32`), `ID` (`uint16`), `Checksum` (`uint8`)
(`Game/Network/Packets/Header.cs:9-11`, `Pack = 1`).

### 3.1 `TM_SC_WEATHER_INFO` (902) — serveur → client — **13 octets**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0 | `uint32` LE | `Length` | `13` (`0xd`) | rzu `TS_SC_WEATHER_INFO.h:8-9` (2 champs : 4 + 2, plus 7 d'en-tête) ; `Header.cs:9` |
| 4 | `uint16` LE | `ID` | `902` (`0x386`) | `op_codes.md:187` ; rzu `TS_SC_WEATHER_INFO.h:12` ; table id→nom client VA `0x67868c` (nom) et `0x6786ae` (id) |
| 6 | `uint8` | `Checksum` | somme des octets 0…5 | `Header.cs:11` ; règle serveur `Game/Network/Packets/PacketExtensions.cs:13-25` |
| 7 | `uint32` LE | `region_id` | **lu par le client à `pkt+7`** : `mov 0x7(%ecx),%edx` VA `0x6711e2` puis `mov %edx,0x13(%eax)` VA `0x6711e5` | client VA `0x6711e2`-`0x6711e5` ; rzu `TS_SC_WEATHER_INFO.h:8` |
| 11 | `uint16` LE | `weather_id` | **lu par le client à `pkt+11`** : `mov 0xb(%ecx),%cx` VA `0x6711e8` puis `mov %cx,0x17(%eax)` VA `0x6711ef` | client VA `0x6711e8`-`0x6711ef` ; rzu `TS_SC_WEATHER_INFO.h:9` |

**Taille totale attendue : 13 octets.** Aucun remplissage : l'en-tête fait 7 octets et le client
lit ses deux champs à 7 et à 11 sans lire entre eux (`mov` 32 bits puis `mov` 16 bits, décalage
naturel). Le désérialiseur client confirme aussi que la 902 ne porte **rien d'autre** : le message
interne qu'il construit fait 25 octets et n'est rempli qu'avec ces deux valeurs (VA `0x6711a0` ;
`movl $0x19` de taille à la VA `0x6711a4`, type interne `0x4b` = 75 à la VA `0x6711b6`, `movl $0x13`
« longueur » à la VA `0x6711c4`, puis les deux écritures ci-dessus).

### 3.2 `TM_CS_GET_WEATHER_INFO` (903) — client → serveur — **11 octets**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0 | `uint32` LE | `Length` | `11` (`0xb`) | rzu `TS_CS_GET_WEATHER_INFO.h:6-7` (1 champ de 4 octets, plus 7 d'en-tête) ; `Header.cs:9` |
| 4 | `uint16` LE | `ID` | `903` (`0x387`) | `op_codes.md:188` ; rzu `TS_CS_GET_WEATHER_INFO.h:9` ; table id→nom client VA `0x6786ee` (nom) et `0x678710` (id) |
| 6 | `uint8` | `Checksum` | somme des octets 0…5 | `Header.cs:11` ; `PacketExtensions.cs:13-25` |
| 7 | `uint32` LE | `region_id` | **aucune observation** : voir §2.2 | rzu `TS_CS_GET_WEATHER_INFO.h:6` |

**Taille totale attendue : 11 octets**, mais c'est une taille **déduite de la définition rzu** et
non observée : aucun site du client ne construit ce paquet, et il n'existe donc aucun lecteur
attestant l'offset 7 pour la 903 (contrairement à la 902). Le dev doit dimensionner sur 11 et
refuser toute autre longueur à la réception (§5.3.3), exactement comme la 550 le fait.

## 4. Gating de version

* rzu déclare les deux paquets avec une **bascule d'id**, et une seule :

  ```
  #define TS_SC_WEATHER_INFO_ID(X) \
      X(902, version < EPIC_9_6_3) \
      X(1902, version >= EPIC_9_6_3)
  ```
  (`TS_SC_WEATHER_INFO.h:11-13` ; même chose pour 903/1903, `TS_CS_GET_WEATHER_INFO.h:8-10`).

* Aucun **champ** n'est gaté : les deux blocs `…_DEF(_)` sont inconditionnels
  (`TS_SC_WEATHER_INFO.h:7-9`, `TS_CS_GET_WEATHER_INFO.h:6-7`). Il n'y a donc aucun champ à trancher
  champ par champ dans cette famille.

* Décision pour Epic 7.3 : `EPIC_7_3` vaut `0x070300` (`librzu/src/lib/Packet/PacketEpics.h:59`) et
  `EPIC_9_6_3` vaut `0x090603` (`PacketEpics.h:96`), donc `0x070300 < 0x090603` : la branche
  retenue est `version < EPIC_9_6_3`, c'est-à-dire **902** et **903**. Les ids `1902`/`1903` (règle
  `>= 0 && < 2000 → += 1000`, `PacketEpics.h:92`) **ne doivent pas** être ajoutés à `GamePackets`.

* Note de fiabilité : `TS_SC_WEATHER_INFO.h:5` porte `// Last tested: EPIC_9_8_1` et
  `PacketEpics.h:84` liste `900, 901, 902` parmi les paquets testés de la révision Epic 7.x : la
  définition de la 902 est donc éprouvée au-delà de 7.3 sans changement de format.

* NGemity ne gate pas ces paquets (`CREATE_PACKET(TS_CS_GET_WEATHER_INFO, 903)` /
  `CREATE_PACKET(TS_SC_WEATHER_INFO, 902)`) et sa version compilée est `EPIC_4_1_1`
  (`ngemity/shared/Common/Define.h:25`) : ses ids concordent avec 7.3, sa **logique** est citée
  sous cette réserve et ne peut pas servir d'autorité pour un format.

## 5. Traitement attendu

### 5.1 Ce que NGemity en fait

* **Chargement de la table** — `Chihiro/src/Globals/ObjectMgr.cpp:1049-1072`, appelé au démarrage
  (`ObjectMgr.cpp:60`) :
  `SELECT id, location_type, time_id, weather_id, weather_ratio, weather_change_time FROM WorldLocation;`
  puis, ligne par ligne,
  `sWorldLocationMgr.RegisterWorldLocation(idx, location_type, time_idx, weather_id, weather_ratio, weather_change_time * 6000, 0)`.
  Conséquence à retenir : **la table a une ligne par `(id, weather_id, time_id)`**, pas une ligne
  par emplacement — c'est `WorldLocationManager::RegisterWorldLocation`
  (`Chihiro/src/Map/WorldLocation.cpp:107-126`) qui replie les lignes dans une entrée par `idx` avec
  la matrice `weather_ratio[weather_id][time_id]` (déclarée `uint8_t weather_ratio[7][4]`,
  `WorldLocation.h:61`). Détail du repli, à reproduire tel quel : au **premier** enregistrement d'un
  `idx`, `location_type` et `weather_change_time` sont posés (`WorldLocation.cpp:119-122`) ; aux
  suivants, seuls `weather_ratio[weather_id][time_id]` et `shovelable_item` sont mis à jour
  (`:112-116`) — `location_type` et `weather_change_time` d'un emplacement sont donc ceux de sa
  **première** ligne, qui est la ligne `weather_id = 0, time_id = 0` dans les données 7.3 (§5.4).
* **Quand la 902 part** — uniquement dans `Player::ChangeLocation`
  (`Chihiro/src/Entities/Player/Player.cpp:1335-1363`) : le serveur calcule
  `nl = GameContent::GetLocationID(x, y)` (`Player.cpp:1347`), envoie d'abord
  `TS_SC_CHANGE_LOCATION` (901) avec `prev_location_id`/`cur_location_id`
  (`Player.cpp:1348-1351`), puis, **si l'id a changé**, `AddToLocation(nl, this)`
  (`Player.cpp:1353-1362`) — c'est `AddToLocation` qui pousse la 902
  (`Chihiro/src/Map/WorldLocation.cpp:39-58`, écriture `weather_info.region_id`/`weather_info.weather_id`
  lignes 50-53). NGemity **n'envoie aucune 902 à l'entrée dans le monde**.
* **Contenu envoyé** — `region_id = idx` (`WorldLocation.cpp:51`) et
  `weather_id = wl->current_weather` (`WorldLocation.cpp:52`). Or `current_weather` **n'est jamais
  affecté** dans tout NGemity : `grep -rn current_weather` ne rend que la déclaration
  (`WorldLocation.h:62`), la copie dans le constructeur de copie (`WorldLocation.cpp:32`) et les
  deux envois (`WorldLocation.cpp:51-52` et `85-86`). Aucune minuterie, aucun tirage sur
  `weather_ratio`, aucune mise à jour de `last_changed_time` (`WorldLocation.h:64`, copié seulement
  en `WorldLocation.cpp:34`). **La 902 de NGemity vaut donc toujours `weather_id = 0` (`Clear`)** et
  sa matrice `weather_ratio` est une donnée morte.
* **La 903 n'est pas lue** : `grep -rn 'WEATHER'` sur tout NGemity ne rend que la table, le
  gestionnaire, l'énumération `shared/Server/ClientPackets.h:197-198` et les deux `#include` de
  `shared/Server/XPacket.h:103,322`. Aucun handler, aucune réponse.
* `WorldLocationManager::SendWeatherInfo` existe (`WorldLocation.cpp:75-93`) et fait la même chose
  que `AddToLocation`, mais **n'a aucun appelant** (`grep -rn SendWeatherInfo` → déclaration, la
  définition, et rien d'autre).

### 5.2 Ce que rzu en fait

* rzu **envoie la 902 une fois, à l'entrée dans le monde**, juste après la 901 :
  `rzgame/src/Component/Character/Character.cpp:298-306` — `changeLocation.prev_location_id = 0`,
  `changeLocation.cur_location_id = 0`, puis `weatherInfo.region_id = 0`, `weatherInfo.weather_id = 0`.
  C'est la seule occurrence de `TS_SC_WEATHER_INFO` dans tout rzu hors `librzu`
  (`grep -rn WEATHER rzu/ rzgame/`).
* rzu **ne traite pas la 903** : `TS_CS_GET_WEATHER_INFO` n'est instancié nulle part, aucun
  gestionnaire ne l'enregistre — seuls l'en-tête et son `#include` existent.
* rzu ne fournit donc **ni source de `region_id`** (il envoie 0) ni **modèle de réponse** à la 903.

### 5.3 Ce que le serveur Navislamia doit faire

**5.3.1 Énumération.** Ajouter à `Game/Network/Packets/Enums/GamePackets.cs`, dans le bloc de la
famille 900 (après `TM_CS_CHANGE_LOCATION = 900`, ligne 65) :
`TM_SC_WEATHER_INFO = 902` et `TM_CS_GET_WEATHER_INFO = 903`. **Ne pas** ajouter 1902/1903 (§4), ni
901 (hors périmètre de cette carte).

**5.3.2 Construction de la 902.** Un `GameWeatherPackets.BuildWeatherInfo(uint regionId, ushort weatherId)`
sur le modèle exact de `GameMovePackets.BuildRegionAck`
(`Game/Network/Packets/Game/GameMovePackets.cs:28-37`) : `HeaderSize = 7` (`GameMovePackets.cs:10`),
`Length = 13` en `uint32` LE à 0, `ID = 902` en `uint16` LE à 4, `region_id` en `uint32` LE à 7,
`weather_id` en `uint16` LE à 11, puis somme des octets 0…5 dans l'octet 6
(`PacketExtensions.cs:13-25`).

**5.3.3 Réception de la 903.** Un lecteur
`TryReadGetWeatherInfo(ReadOnlySpan<byte> packet, out uint regionId)` qui **n'accepte que 11
octets** et refuse toute autre longueur (modèle : `GameActionPackets.TryReadGetRegionInfo`,
`Game/Network/Packets/Game/GameActionPackets.cs:237-250`, qui refuse tout ce qui n'est pas 15).
Un bras dans la chaîne de réception de `Game/Network/Clients/GameClient.cs` **avant** le `switch`
final (`GameClient.cs:791-803`, qui lève `Unknown Packet Type` pour tout id non traité) :
`if (header.ID == (ushort)GamePackets.TM_CS_GET_WEATHER_INFO) { HandleGetWeatherInfo(msgBuffer); continue; }`.
Sans ce bras, un membre d'énumération montant atteint le `switch` final — c'est exactement
l'invariant du profil (« aucun membre de `GamePackets` ne peut atteindre le `switch` final »).

**5.3.4 Réponse à la 903.** Trancher et écrire dans le code ce qui suit :

* `region_id` connu du service (§5.3.6) → répondre **à ce seul client** (jamais en diffusion) avec
  une 902 `{region_id = region_id demandé, weather_id = météo courante de cet emplacement}` ;
* `region_id` inconnu (aucune ligne `WorldLocation`) → **ne rien répondre**. Aucune référence
  n'atteste un `TM_SC_RESULT` pour cette famille, et aucune ne répond à une 903 : inventer un code
  d'erreur serait une supposition (voir §7b et §6).

**5.3.5 Poussée spontanée de la 902.** Le socle suit **rzu** (§5.2) : une 902 `{0, 0}` à l'entrée
dans le monde, dans la séquence d'entrée existante — à côté de `client.SendGameTime()` et
`client.SendTimeSync()` (`Game/Network/Clients/Actions/GameActions.cs:219-220`), avec une méthode
`GameClient.SendWeatherInfo(uint regionId, ushort weatherId)` sur le modèle de
`GameClient.SendGameTime()` (`GameClient.cs:62-67`). Le déclencheur de NGemity (au changement
d'emplacement) **n'est pas transposable** ici : il exige l'appariement position→emplacement, absent
de Navislamia (§7c).

**5.3.6 Table et service.** Suivre la chaîne existante des ressources Arcadia :

* une entité `WorldLocationEntity` dans `Game/DataAccess/Entities/Arcadia/` (mêmes conventions que
  `LevelResourceEntity` etc.), mappant au minimum les colonnes que NGemity lit — `id`,
  `location_type`, `time_id`, `weather_id`, `weather_ratio`, `weather_change_time`
  (`ObjectMgr.cpp:1052`) — plus `x` et `y`, qui sont nécessaires à toute carte ultérieure
  d'appariement position→emplacement (§5.4) mais que NGemity ne lit pas ;
* un `DbSet` dans `Game/DataAccess/Contexts/ArcadiaContext.cs` (liste lignes 11-29) ;
* un chargement dans `IWorldRepository`/`WorldRepository` (`Game/DataAccess/Repositories/Interfaces/IWorldRepository.cs:7`,
  `WorldRepository.cs:20-30`) et un champ dans `WorldEntity`
  (`Game/DataAccess/Entities/Navislamia/WorldEntity.cs:6-12`) ;
* un service en mémoire équivalent à `WorldLocationManager` : dictionnaire `id → { location_type,
  weather_ratio[7][4], current_weather, weather_change_time, weather_change_time_last }`, avec le
  repli `weather_ratio[weather_id][time_id] = weather_ratio` de `RegisterWorldLocation`
  (`WorldLocation.cpp:107-126`) et la règle « la première ligne de l'id fixe `location_type` et
  `weather_change_time` » (§5.1) — sans ce repli, ajouter les lignes du `DbSet` dans un dictionnaire
  indexé par `id` en garderait une seule au lieu de les fusionner ;
* l'état par connexion (emplacement courant) dans `ConnectionInfo`
  (`Game/Network/Clients/ConnectionInfo.cs`), aux côtés de `Layer` et des coordonnées, pour que la
  carte 901 puisse partager la même donnée le moment venu.

**5.3.7 Tests exigés par les critères transversaux.** Un test d'offsets pour la 902 (13 octets :
`Length` à 0, `ID` à 4, `Checksum` à 6, `region_id` à 7, `weather_id` à 11) et un pour la 903
(11 octets, `region_id` à 7), plus un test de refus des longueurs ≠ 11 pour le lecteur — sur le
modèle de `Tests/Game/RegionInfoPacketsTests.cs` (dont
`TryReadGetRegionInfo_RejectsAnyLengthOtherThanFifteen`). Le compte de tests ne doit pas baisser
(448 au départ, §9).

### 5.4 Les données 7.3 réellement présentes (décodage de `db_worldlocation.rdb`)

Le client fourni embarque sa propre copie de la table sous forme binaire :
`reference/client73/db_worldlocation.rdb`, 7 400 215 octets, sha256
`effbb9ac4bbb7e1d9348b6a4aa185525dbc73512d017c0567834e14c1e1cfaee`. Décodage **par arithmétique et
lectures ciblées** (aucun outil du dépôt ne le documente) :

* en-tête de **132 octets** = `0x84` : date `20241103`, outil `Written by Archimedes v0.1.0`
  (offsets 0 et 0x10), puis le nombre d'enregistrements `0x1961` = **6497** à l'offset `0x80` ;
* taille d'enregistrement **1139 octets**, dérivée exactement : `132 + 6497 × 1139 = 7 400 215`,
  la taille du fichier, sans reste (et la séquence de tête d'un enregistrement se retrouve bien tous
  les 1139 octets : offsets 132, 1271, 2410, 3549…) ;
* premier enregistrement à l'offset 132 : `id = 10`, `text_id = 70000010` (= `70000000 + id`),
  `x = 0`, `y = 0`, `apply_location_name = apply_light = apply_bgm = 1`, `location_type = 1`,
  `fog_application = 3`, `time_id = 0`, `weather_id = 0`, `cloud_ratio = 30`,
  `weather_change_time = 60`, `weather_ratio = 0`, puis les trois quadruplets `sky_*_a/r/g/b` et
  `sky_mid_rate = 0.5f` (`00 00 00 3f`) à l'offset 104. Chaque champ occupe **4 octets**, dans
  l'ordre des colonnes de la table `WorldLocation` de `ArcadiaSchemaPSQL.sql` (à partir de la ligne
  1672) — l'alignement est vérifié par
  `sky_mid_rate = 0.5` qui tombe exactement à l'offset attendu, et par comparaison d'enregistrements
  (`cmp -l -i 132:1271 -n 1139`) : les 48 octets qui diffèrent entre l'enregistrement 1 et
  l'enregistrement 2 sont exactement `time_id` (offset 36), `sky_start_r/g/b` (60/64/68),
  `sky_mid_r/g/b` (76/80/84) et `sky_end_r/g/b` (92/96/100) — c'est-à-dire `time_id` **et les
  couleurs du ciel**, pas `weather_id` (qui vaut 0 dans les deux) ;
* structure confirmée : les enregistrements sont groupés **par `id`, puis par `weather_id`, puis par
  `time_id`** — emplacement 10 : `weather_id 0` aux enregistrements 0-3 (`time_id` 0,1,2,3), `1`
  aux 4-7, `2` aux 8-11, `4` aux 12-15, puis l'emplacement 100 commence à l'enregistrement 16. Un
  même `id` apparaît donc **plusieurs fois** : le socle doit replier (§5.1, §5.3.6) ;
* conséquence directe sur `region_id` : l'id d'emplacement **est un encodage de cellule**, il porte
  `x` et `y` : `110901` a `x = 11, y = 9` ; `60600` a `x = 6, y = 6` ; `80302` a `x = 8, y = 3` ;
  `30001` a `x = 3, y = 0` ; `10` et `100` ont `x = y = 0`. L'id se lit
  `x × 10000 + y × 100 + n` avec `n` un numéro d'ordre dans la cellule. **Ce n'est donc pas un
  indice de région de visibilité** (celui de 550/11, pas de 180 octets :
  `Game/Services/WorldVisibility.cs:5`) ;
* domaine de `weather_id` : valeurs observées **0, 1, 2, 3 et 4** selon les enregistrements, avec
  des trous (`weather_id = 3` absent des enregistrements 0-15 de l'emplacement 10, observé à
  l'enregistrement 100 — emplacement 60600 — et à l'enregistrement 6496 — emplacement 30001) ;
  NGemity nomme 0…6 (`WL_Weather`, `WorldLocation.h:31`) et dimensionne `weather_ratio[7][4]`
  (`:61`) ; `time_id` observé sur 0…3, conforme à `WL_Time` (`:23-29`) ;
* **`0` (`Clear`) est présent pour les emplacements 10 et 100** (enregistrements 0 et 16) : c'est la
  valeur retenue pour le socle, cohérente avec NGemity (§5.1) — voir la réserve de §7d.

## 6. Écarts assumés avec NGemity

1. **`region_id = 0` à l'entrée dans le monde au lieu de l'id d'emplacement réel.** NGemity envoie
   l'id d'emplacement parce qu'il sait le calculer depuis les données de carte du client
   (`GameContent::GetLocationID`, `Chihiro/src/Globals/GameContent.cpp:309-326`, au-dessus d'un
   quadtree construit par `Maploader` : `Map/Maploader.cpp:41` `fMapLength =
   segmentCountPerMap × tileLength × tileCountPerSegment` (`Map/Maploader.h:92` porte le
   `TerrainSeamlessWorldInfo`, classe déclarée en `Map/TerrainSeamlessWorld.h:46`),
   `Map/Maploader.cpp:47-49` `GetWorldID(i,y)` puis `SetDefaultLocation(i, y, fMapLength, wid)`
   (`:79-89`), et les polygones de
   `Resource/NewMap/<fichier de localisation>` (`:53`, `:100-157`) avec priorité
   (`Map/Maploader.h:24-31`)). Navislamia n'a **ni** ces données **ni** ce chargeur (§7c) : porter
   ce calcul serait porter une capacité absente, pas une logique. Le socle suit rzu (`{0,0}`) et
   réserve l'appariement à une carte dédiée.
2. **Pas de poussée au changement d'emplacement.** Conséquence directe du point 1 : `HandleChangeLocation`
   (`Game/Network/Clients/GameClient.cs:160-166`) continue de ne mettre à jour que `ConnectionInfo.X/Y`
   et la visibilité. Aucune 902 n'y est ajoutée tant que l'appariement n'existe pas.
3. **`weather_id` figé à 0.** NGemity ne l'affecte jamais (§5.1) : le socle reproduit ce comportement
   sans porter la matrice `weather_ratio` comme moteur — elle est chargée, pas encore utilisée.
4. **La 903 reçoit une réponse, NGemity et rzu n'en donnent aucune** (§5.1, §5.2). C'est un ajout
   assumé du socle, imposé par la carte ; il n'a **aucun témoin** côté client (§2.2). Il est donc
   spécifié au minimum (répondre la 902 demandée, ne rien répondre sinon) pour ne rien inventer.
5. **`weather_change_time × 6000` n'est pas porté.** NGemity multiplie la valeur SQL par 6000
   (`ObjectMgr.cpp:1068`) alors que la donnée 7.3 vaut 60 pour l'emplacement 10 (`db_worldlocation.rdb`,
   offset 48) ; l'unité réelle de cette colonne n'est attestée nulle part (§7f). Le socle **stocke la
   valeur brute** et ne lui donne aucun sens temporel.
6. **`SendWeatherInfo` de NGemity n'est pas porté** : c'est un doublon sans appelant (§5.1) ; le socle
   n'a qu'un seul chemin de poussée.

## 7. `NON ÉTABLI`

### (a) Le client 7.3 émet-il vraiment la 903 ?

**Non dans ce binaire** (§2.2), mais la conclusion repose sur l'absence d'un constructeur, pas sur
une capture. Ce qui l'infirmerait : un journal de paquets (ou une capture réseau) d'un client 7.3
officiel montrant une 903 ; ou une deuxième copie de `SFrame.exe` d'une autre révision 7.3 dont le
constructeur existe. Impossible à produire ici : l'exécution du client est interdite par le profil.
**Question précise à trancher** : le client 7.3 émet-il la 903 dans une situation non couverte par
ce binaire (par exemple une interface de carte météo) ? Tant que non, la réponse de §5.3.4 est une
garantie défensive, pas une exigence démontrée.

### (b) Que porte `region_id` dans la 903, et sur quoi le client se base-t-il ?

`NON ÉTABLI`. Aucun émetteur, aucun lecteur client, aucune implémentation de référence. L'identité
retenue (`WorldLocation.id`, la même que la 902) est la **seule** défendable, mais elle reste une
hypothèse de cohérence de nom et de type. À noter pour la suite : Navislamia n'envoie aujourd'hui
**aucune 901** (`TM_SC_CHANGE_LOCATION` n'est pas dans `GamePackets`), donc le client n'a jamais
appris d'id d'emplacement du serveur — l'id qu'il renverrait, s'il renvoyait, ne peut pas être
observé dans l'état actuel.

### (c) L'appariement position → id d'emplacement (prérequis du `region_id` réel)

`NON ÉTABLI`, et c'est le vrai prérequis d'infrastructure de la chaîne. Faits : l'id d'emplacement
est packé sur une cellule `(x, y)` (§5.4) ; NGemity obtient la cellule depuis
`terrainseamlessworld.cfg` (`TerrainSeamlessWorld.cpp:27` `Initialize(szFilename, …)`, `:112`
`GetWorldID(nMapPosX, nMapPosY)`) et la taille de cellule depuis ce même fichier
(`Maploader.cpp:41`) ; Navislamia n'a ni ce fichier, ni les données `Resource/NewMap/*` du client,
ni de table `WorldLocation` mappée. **Question précise** : quelle est la taille d'une cellule
d'emplacement en unités de monde (et donc la formule `x = ⌊X / L⌋`, `y = ⌊Y / L⌋`), et le serveur
doit-il la lire dans `terrainseamlessworld.cfg` ou la dériver de la table `WorldLocation` ? À
trancher par Killian, ou à porter dans une carte dédiée : cette fiche ne la devine pas.

### (d) Le client filtre-t-il la 902 sur `region_id` ?

`NON ÉTABLI`. Ce qui est établi : le désérialiseur **ne filtre rien** — il recopie les deux champs
et met le message en file (message interne de type `0x4b` = 75, push `std::deque` à `this+0x2c` via
la VA `0x64d0e0`, `lea 0x2c(%esi),%ecx` VA `0x6711f4`). Le consommateur de ce message n'a **pas** été
localisé (les lectures de `+0x13`/`+0x17` sont trop génériques pour être attribuées sans ambiguïté).
**Questions précises** : le client ignore-t-il une 902 dont le `region_id` diffère de son
emplacement courant ? Utilise-t-il le `region_id` pour choisir la ligne de `db_worldlocation.rdb` à
appliquer (auquel cas un `region_id` invalide ne serait jamais appliqué, et `0` à l'entrée dans le
monde serait inoffensif mais sans effet) ? Ces deux questions décident si `{0,0}` suffit à l'entrée
dans le monde ou si le socle doit attendre l'appariement (§7c) pour être visible en jeu.

### (e) Domaine exact de `weather_id` et correspondance avec les thèmes du client

`NON ÉTABLI`. Trois témoins divergents : NGemity nomme 0…6 (`WL_Weather`, `WorldLocation.h:31`) et
dimensionne `[7][4]` (`:61`) ; les données 7.3 échantillonnées ne montrent que 0…4, en sous-ensembles
propres à chaque emplacement (§5.4) ; le poseur de thème du client n'accepte que 0…4
(`cmp $0x4,%edi` VA `0x45eaf2`, `ja` vers la sortie) et le client nomme cinq thèmes visuels
(`static_weather_fine`, `_cloudy`, `_rain`, `_snow`, `_fog`, chaînes aux offsets de fichier
`0x632fdc`, `0x632fc4`, `0x632fb0`, `0x632f88`, `0x632f9c` ; script `weather_script.lua` et trace
`change_weather_theme( %d )` à `0x618a74`/`0x618a58`). **Questions précises** : le domaine est-il
exactement 0…6, et `weather_id` est-il l'indice du thème client ou une valeur traduite localement ?
Il faudrait scanner les 6497 enregistrements (ou la table SQL) pour le maximum réel — non fait ici.

### (f) Nombre d'emplacements distincts et sens de `weather_change_time`

`NON ÉTABLI`. 6497 est un nombre de **lignes** (une par `(id, weather_id, time_id)`), pas
d'emplacements : le nombre distinct d'ids exige un balayage complet du `.rdb` (6497 enregistrements)
ou une requête sur la table SQL — aucune des deux n'est faisable ici (pas de PostgreSQL). L'unité de
`weather_change_time` (60 pour l'emplacement 10) est inconnue : NGemity la multiplie par 6000
(`ObjectMgr.cpp:1068`), ce qui suppose 1 unité = 6 s, mais aucun document ni code client ne le
confirme.

### (g) Défaut du client quand aucun enregistrement ne correspond

`NON ÉTABLI`. Aucune observation possible : il faudrait faire tourner le client, ce que le profil
interdit. Concerne directement le choix « ne rien répondre » de §5.3.4 : si le client refuse une
902 dont l'id est absent de ses données, le serveur devrait au contraire ne jamais envoyer d'id
inconnu — c'est ce que fait §5.3.4, mais par prudence, pas par preuve.

### (h) Faut-il une 901 avant la 902 ?

`NON ÉTABLI`. NGemity envoie toujours 901 puis 902 dans cet ordre
(`Player.cpp:1348-1359`) ; rzu envoie les deux à l'entrée dans le monde, dans l'ordre 901 puis 902
(`Character.cpp:298-306`). Les deux références sont donc muettes sur une 902 **sans** 901 préalable,
ce qui est pourtant l'état que produira le socle (§6.1). **Question précise** : le client exige-t-il
d'avoir reçu une 901 pour retenir un id d'emplacement ? Si oui, la carte 901 devient un prérequis
d'affichage de la météo, et non une carte indépendante.

## 8. Commits et binaires épinglés

| Référence | Identifiant | Usage dans cette fiche |
|---|---|---|
| `reference/rzu` | commit `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | forme des paquets, tailles, gating `EPIC_9_6_3`, envoi à l'entrée dans le monde |
| `reference/ngemity` (NgEmu/Chihiro) | commit `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | logique serveur : `LoadWorldLocation`, `WorldLocationManager`, `Player::ChangeLocation`, `GameContent::GetLocationID` |
| `reference/client73/SFrame.exe` | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 o.) | format sur le fil (offsets 7 et 11), table id→nom, absence d'émetteur 903, répartiteur de réception |
| `reference/client73/db_worldlocation.rdb` | sha256 `effbb9ac4bbb7e1d9348b6a4aa185525dbc73512d017c0567834e14c1e1cfaee` (7 400 215 o.) | structure de la table `WorldLocation` 7.3, encodage de l'id, domaine de `weather_id` |
| `ArcadiaSchemaPSQL.sql` | tel que versionné sur `master` (table `WorldLocation`, lignes 1672+) | ordre des colonnes, recoupement du décodage `.rdb` |

## 9. Vérifications relevées (archéologue, sur `master` avant écriture de la fiche)

| Commande | Résultat |
| --- | --- |
| `dotnet build Navislamia.sln -c Debug` (`NUGET_PACKAGES=/srv/navislamia/.nuget-cache`) | code de sortie **0**, 0 erreur, 160 avertissements (préexistants) |
| `dotnet test Tests/Tests.csproj` | code de sortie **0**, **448 réussis / 448**, 0 échec, 0 ignoré |
| `git log --oneline origin/master..master` | vide |
| `git branch --show-current` (pendant la rédaction) | `hermes/packet-socle-meteo-monde` |

Le plancher de 366 tests du profil est donc déjà dépassé de 82 : le dev part de 448.

## 10. Bloc pour `CLAUDE.md` (à recopier dans la description de la MR)

> **Paquet 902 / 903 — `TM_SC_WEATHER_INFO` / `TM_CS_GET_WEATHER_INFO`** (Epic 7.3 ; fiche
> `docs/packet-specs/902-weather-info.md`)
>
> `TM_SC_WEATHER_INFO` (902, 13 octets : `region_id` `uint32` à 7, `weather_id` `uint16` à 11) et
> `TM_CS_GET_WEATHER_INFO` (903, 11 octets : `region_id` `uint32` à 7). Les ids `1902`/`1903` sont
> `version >= EPIC_9_6_3` et ne doivent pas être ajoutés.
>
> Deux pièges. (1) `region_id` n'est **pas** un indice de région de visibilité (la 550/11, pas de
> 180) : c'est l'id de `WorldLocation`, encodé `x × 10000 + y × 100 + n` sur les colonnes `x`/`y`
> de la table ; NGemity y met l'id de l'emplacement, rzu y met 0. (2) La table a **une ligne par
> `(id, weather_id, time_id)`** (la copie client en compte 6497, groupées par `id` puis
> `weather_id` puis `time_id`) : elle doit être repliée en un enregistrement par id avec
> `weather_ratio[weather_id][time_id]`, comme `WorldLocationManager::RegisterWorldLocation`, sinon
> les lignes s'écrasent.
>
> Le client 7.3 **consomme** la 902 (il lit `+7` et `+11`) et, dans le binaire fourni, **n'émet
> jamais** la 903 : la constante `0x387` n'y apparaît que dans la table id→nom, sans constructeur.
> Aucune référence (NGemity, rzu) n'implémente de réponse à la 903. La réponse est donc défensive :
> une 902 à l'id demandé si l'id est connu, rien sinon.
>
> NGemity ne charge la table que pour la replier, **n'affecte jamais `current_weather`** (sa 902 vaut
> toujours `weather_id = 0`) et ne pousse la 902 qu'au changement d'emplacement, calculé depuis les
> données de carte du client — données que Navislamia n'a pas. Le socle suit rzu : une 902 `{0, 0}`
> à l'entrée dans le monde. L'appariement position → id d'emplacement reste `NON ÉTABLI` (taille de
> cellule inconnue) et mérite une carte dédiée.
