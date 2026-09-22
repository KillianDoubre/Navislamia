# Socle « classements de joueurs (top records) » — `TM_CS/SC_RANKING_TOP_RECORD` (5000/5001)

Deux opcodes, une seule famille, **aucun handler** dans les deux références serveur — et une
réponse nette à la question centrale du PO : **le client Epic 7.3 émet bien `5000`**, depuis un
seul point du binaire, avec `ranking_type = 0`, et **route bien `5001`**.

| Source | Ce qu'elle apporte | Commit épinglé |
|---|---|---|
| `/srv/navislamia/reference/rzu` | format, tailles, ordre des champs, gating par version | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` |
| `/srv/navislamia/reference/ngemity` | logique (version plus récente) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` |
| `/srv/navislamia/reference/client73` | client Epic 7.3, tranche les divergences. **Pas un dépôt git** : pas de commit, seulement le fichier de ressource `SFrame.exe`, 9 841 664 octets, empreinte de fichier sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, et la méthode de lecture statique décrite en §8 | — |
| `/srv/navislamia/Navislamia` | état du serveur | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` |

Méthode : **lecture statique** uniquement. Le binaire du client a été lu par `objdump -d` (balayage
linéaire complet, seule méthode fiable : un balayage par motif d'octets rate les appels dont
l'opcode `e8` est précédé d'un octet de même valeur), par lecture directe de `.rdata`/`.data`
(mapping des sections PE) et par la chaîne RTTI MSVC (`vtable-4 → COL → type descriptor → nom`).
**Aucune exécution** : ni `SFrame.exe`, ni Lua, ni script du client. État du dépôt au moment de la
fiche : `dotnet build Navislamia.sln -c Debug` → code 0, `dotnet test Tests/Tests.csproj` → code 0,
**448 tests passés** (0 en échec), aucun fichier de code touché par cette fiche (elle ajoute un seul
fichier Markdown).

Cette fiche est le prérequis de la carte parkée en `THINKING` : `TM_CS_RANKING_TOP_RECORD` (5000,
carte `y9A6OUxd`), qui se rouvrira au merge de la MR de ce socle.

---

## Questions tranchées par cette fiche

| # | Décision | § |
|---|---|---|
| D1 | Les deux ids `5000` et `5001` sont **identiques en 7.3** : `X(<id>, true)` n'exprime aucun gating de version (`true` est la condition C++ littérale substituée dans `if(condition_) id = id_;`, `librzu/src/lib/Packet/PacketDeclaration.h:587-589`). Aucun champ des deux structures n'est gaté. | §4 |
| D2 | Aucun de ces deux ids n'est **réutilisé** ailleurs : `X(5000, true)` et `X(5001, true)` sont les **seuls** ids de la plage 5000-5999 dans tout `librzu/src/packets/`. NGemity place les deux mêmes ids entre `4719` et `6000` (`shared/Server/ClientPackets.h:277-278`) : la plage 5002-5999 est **vide** dans les deux références. | §4.2 |
| D3 | **Le client 7.3 émet `5000`** : constructeur de trame en `0x48d160` (longueur `8`, id `0x1388`), appelé depuis **un seul site**, `0x49d2de`. `ranking_type` vaut `0` : c'est la **seule** valeur que le client sache écrire (`0x49d2e9`). | §2.1 |
| D4 | Le client **route `5001`** : le dispatcher entrant compare `eax` à `0x1389` (`0x67e7bc`) et appelle le constructeur-analyseur `0x671660` ; la trame est analysée puis mise en file. `5000` n'est **pas** routé (il n'est qu'émis). | §2.2 |
| D5 | Tailles totales, en-tête de 7 octets compris : `5000` = **8** octets (fixe), `5001` = **20 + 41×n** octets (`n` = `records`), soit **20** à vide. | §3 |
| D6 | Le pas de 41 octets est prouvé par le client lui-même : il incrémente son pointeur source de `0x29` par entrée (`0x67172c`) et lit `rank` en `entrée+0`, le nom en `entrée+2`, le `score` en `entrée+31`. | §3.3 |
| D7 | `records` occupe **2 octets** à l'offset **18** et les entrées commencent à l'offset **20** : le client lit `movzwl 0x12(%ebx)` puis démarre le nom à `buf+22` avec un `rank` lu à `buf+20` — donc **aucun remplissage** entre le compteur et la première entrée. | §3.3 |
| D8 | Les deux `score` (celui du demandeur et celui de chaque entrée) sont **divisés par 10 000 par le client** avant usage (`__alldiv` avec `0x2710`, appels `0x6716c9` et `0x671719`) : **la valeur du fil est la valeur affichée × 10 000**. Cette échelle n'est écrite dans aucune des deux références serveur. | §3.6 |
| D9 | Le client plafonne à **10 entrées** : son message interne fait `0x1ba` = **442** octets, dont `0x20` = 32 d'en-tête, soit `442 − 32 = 410 = 41 × 10`. Le compteur n'est **pas** borné par l'analyseur : au-delà de 10 entrées, le client écrit au-delà de son allocation. | §2.3, §5.2 |
| D10 | `ranker_name` est copié par une boucle **`strcpy` jusqu'au NUL** (`0x671700`-`0x671708`) dans une zone de 31 octets : un nom de 31 caractères **sans** NUL déborde sur le `score` de l'entrée. Le NUL dans les 31 octets est une **obligation serveur**, pas une convention. | §3.5 |
| D11 | Ni `Chihiro` ni `librzu` n'implémentent quoi que ce soit de cette famille : les deux structures sont déclarées, jamais traitées. Ce socle est du **protocole pur**, comme les socles « compétition entre joueurs » et « instances de jeu ». | §5.1 |
| D12 | **Découpage retenu** : lot minimal **K1 = `5000` + `5001` à vide** (déclaration, lecture des 8 octets, écriture de la réponse `records = 0`, tests d'offsets) — livrable sans choisir une seule politique de classement ; la **source des données** (quel classement, quelle métrique, combien d'entrées, rang du demandeur) est un lot K2 et appartient à Killian. | §5.4, §5.5 |

---

## 1. Identité

### 1.1 La famille (deux opcodes)

| Id | Nom `TM_CS/SC` | Sens | Déclaration (Navislamia) | rzu | NGemity |
|---|---|---|---|---|---|
| 5000 | `TM_CS_RANKING_TOP_RECORD` | client → serveur | `op_codes.md:253` | `librzu/src/packets/GameClient/TS_CS_RANKING_TOP_RECORD.h:8-9` | `shared/Server/Packets/GameClient/TS_CS_RANKING_TOP_RECORD.h:9` |
| 5001 | `TM_SC_RANKING_TOP_RECORD` | serveur → client | `op_codes.md:254` | `librzu/src/packets/GameClient/TS_SC_RANKING_TOP_RECORD.h:20-21` | `shared/Server/Packets/GameClient/TS_SC_RANKING_TOP_RECORD.h:20` |

Identique chez NGemity, à la même numérotation : `reference/ngemity/shared/Server/ClientPackets.h:277-278`,
entre `TS_CS_BATTLE_ARENA_ABSENCE_CHECK_ANSWER = 4719` et `TS_CS_REQUEST_FARM_INFO = 6000` — deux
familles auxquelles ce socle n'appartient pas (§1.3).

Le nom des classes internes du client confirme l'appariement id ↔ nom :

- entrant : `. ?AUSMSG_SC_RANKING_TOP_RECORD@@` (nom RTTI en `.data` `0xc1e0b8`, type descriptor
  `0xc1e0b0`, COL `0xbc8c10`, vtable `0xa520b8`, unique fonction virtuelle = destructeur `0x6284c0`) ;
- sortant : **aucune** classe `SMSG_CS_RANKING_TOP_RECORD` et **aucune** chaîne
  `TM_CS_RANKING_TOP_RECORD` dans tout le binaire : le client n'enregistre pas de nom pour sa trame
  sortante, comme pour `TM_CS_COMPETE_REQUEST` (4500) et `TM_CS_COMPETE_ANSWER` (4502) — l'absence
  de nom n'est donc **pas** une preuve d'absence d'émission. La preuve d'émission est le couple
  constructeur + site d'appel (§2.1).

L'identité `5000` ↔ `Ranking_Top_Record` est par ailleurs confirmée par la table interne du client
(id → nom, **164 entrées**, sites d'enregistrement `0x67535a`-`0x6795cf`) qui contient bien
`5001 → TM_SC_RANKING_TOP_RECORD` (site `0x6795c0`-`0x6795cf`, nom en `.rdata` `0xa52ccc`) et **pas**
`5000`.

### 1.2 Ce que Navislamia a déjà, et ce qui manque

| Élément | État | Source |
|---|---|---|
| Membre dans l'énumération `GamePackets` | **absent** : aucun `RANKING` dans l'énumération (dernière valeur déclarée `TM_NONE = 9999`) | `Game/Network/Packets/Enums/GamePackets.cs:3-102` (aucun `RANKING`) |
| En-tête de trame | **présente** : `uint32 Length` + `uint16 ID` + `uint8 Checksum`, `Pack = 1` | `Game/Network/Packets/Header.cs:8-12` |
| Construction d'une trame de taille variable à la main | **présente** : `HeaderSize = 7` et `CreatePacket(GamePackets, int total)` qui écrit longueur + id | `Game/Network/Packets/Game/GameCharacterPackets.cs:19`, `:382-388` |
| Somme de contrôle | **présente** : `WriteChecksum` = somme des octets 0 à 5 rangée en `[6]` | `Game/Network/Packets/Game/GameCharacterPackets.cs:390-397`, `Game/Network/Packets/Game/GameAttackPackets.cs:75-80` |
| Routage entrant | **présent, en chaîne de `if`** avant le `switch` final qui lève l'exception | `Game/Network/Clients/GameClient.cs:726-729` (exemple `TM_CS_EMOTION`) et `:802` |
| Émission d'un résultat générique | **présente** : `SendResult(ushort id, ushort result, int value = 0)` | `Game/Network/Clients/GameClient.cs:56-58` |
| Lecture des personnages | **par compte ou par nom seulement** : `GetCharactersByAccountNameAsync` / `GetCharacterByName` | `Game/Services/ICharacterService.cs:12`, `:20` |
| Table, entité ou service de classement | **absent** : aucun `Ranking` dans le code C# | `grep -rn "RANKING\|Ranking" --include=*.cs` → aucun résultat |
| Compteur persistant exploitable comme « score » | **absent** : aucune colonne de don, de points de classement ni d'agrégat | `Game/DataAccess/Entities/Telecaster/CharacterEntity.cs` (aucun champ `Score` ni `Donat*`) |

### 1.3 Ce que la famille n'est pas

- `4500`-`4506` (`TM_CS/SC_COMPETE_*`) sont le socle voisin **déjà traité** :
  `docs/packet-specs/socle-competition-joueurs.md`. Opcodes disjoints de ce socle.
  Attention à un détail de lecture : ce socle-ci n'a **rien** à voir avec le duel.
- `251`/`252` (cités par le PO comme « voisins ») sont `TM_CS_BUY_ITEM` / `TM_CS_SELL_ITEM`
  (`op_codes.md:78-79`), c'est-à-dire le socle « marché NPC » (carte `h5z5f060`). Ces ids ne sont
  **pas** voisins de `5000`/`5001` : dans `op_codes.md`, la plage entre `4506` et `5000` est vide, et
  entre `5001` et `6000` aussi. Aucun opcode partagé, aucun chevauchement de format — les deux
  socles sont indépendants.
- `4700`-`4719` (`TS_CS/SC_BATTLE_ARENA_*`) existent dans la table **plus récente** de NGemity
  (`shared/Server/ClientPackets.h:259-276`) mais **pas** dans `op_codes.md` : ils comblent, chez
  NGemity, la plage vide de 7.3. Ils n'appartiennent pas à ce socle et ne doivent pas être ouverts
  sous prétexte qu'ils sont « entre » les deux familles.
- `6000`-`6008` (élevage d'invocations) et `4250`-`4253` (instances de jeu) sont les autres familles
  encadrantes de la liste d'opcodes ; sans rapport.
- `258`/`259` (`TM_CS_DONATE_ITEM`, `TM_CS_DONATE_REWARD`, `op_codes.md:85-86`) ne sont pas dans
  cette famille, mais c'est le **seul indice client sur ce que classe `ranking_type = 0`** : la
  fenêtre du client s'appelle littéralement `window_donation_ranking.nui` (§7a). Hypothèse à
  trancher, pas une décision.

---

## 2. Ce que le joueur fait pour que le client envoie le paquet

### 2.1 `5000` (`TM_CS_RANKING_TOP_RECORD`) — chaîne de déclenchement prouvée

| Étape | Preuve (client 7.3) |
|---|---|
| 1. Une commande UI est **enregistrée sous le nom `Ranking_Top_Record`** au démarrage | objet de `0x19` octets avec `push $0x8f` (type `143`) en `0x5f5fc9`-`0x5f5ff3`, puis objet de `0x57` octets construit sur la chaîne `0xa20184` (`"Ranking_Top_Record"`, `.rdata`) en `0x5f5ff8`-`0x5f602a` ; les deux sont rangés dans le registre `this+0x440` par `0x649e20` |
| 2. Un message interne de type `0x3a` (58) est traité par le dispatcher `SGameInterface` `0x49cd00` (aiguillage sur `[msg+4]`, plage `4..0x3ff`, table d'octets `0x49e8b4`, table de sauts `0x49e77c`) | en-tête de fonction `0x49cd00`-`0x49cd65` ; entrée de table du type 58 = `0x49d04c` |
| 3. Le bras essaie d'abord une autre commande : **si `Alliance_Accept` est enregistrée**, il construit `/gajoin <nom>` et sort | `0x49d245`-`0x49d2c0` : sonde `0x48ef10` sur `0xa201a4`, chaîne `"/gajoin "` (`0xa20198`) |
| 4. **Sinon**, il sonde `Ranking_Top_Record` : si la commande est absente, il ne fait rien | `0x49d2c5`-`0x49d2d5` : `push $0xa20184`, appel `0x48ef10` (recherche de nom), `je 0x49e763` si le résultat est nul |
| 5. Si elle est présente, il **construit la trame** puis l'envoie par l'objet de connexion | `0x49d2db` : `lea -0x18(%ebp),%ecx` puis `call 0x48d160` (constructeur) ; `0x49d2f5`-`0x49d303` : `mov (%esi),%edx ; mov 0xc4(%edx),%edx ; push … ; call *%edx` (envoi virtuel) |
| 6. `ranking_type` est mis à **0** juste après la construction | `0x49d2e9` : `movb $0x0,-0x11(%ebp)` — le tampon est en `ebp-0x18`, donc l'octet écrit est l'offset **7** de la trame |

Le constructeur lui-même (`0x48d160`-`0x48d1aa`) est un membre de la famille des petits bâtisseurs
de trames du client (`0x48c490`…`0x48d1b0`) : il écrit la longueur `8` (`movl $0x8,(%eax)` en
`0x48d18d`), l'id `0x1388` (`mov $0x1388,%edx` + `mov %dx,0x4(%eax)` en `0x48d182`-`0x48d187`),
puis recalcule la somme de contrôle des octets 0 à 5 dans l'octet 6 (`0x48d193`-`0x48d1a7`).
Ses appelants, relevés par balayage linéaire complet : **un seul**, `0x49d2de`. Comparaison utile :
le bâtisseur de `4502` (`0x48d110`) a deux appelants (`0x49d1aa`, `0x49d1f5`), celui de `4500`
(`0x48d0c0`) un seul (`0x49d3f9`) — le nombre d'appelants n'est donc pas un indice de vitalité, la
présence d'un appelant l'est.

Conséquence pour le joueur : **ouvrir le classement depuis l'interface** (la commande
`Ranking_Top_Record`, associée à la fenêtre `window_donation_ranking.nui`) émet un `5000` de
8 octets avec `ranking_type = 0`. Aucun autre `ranking_type` n'est atteignable depuis ce client
(§7a).

### 2.2 Ce que le client fait à la réception (routage entrant)

| Élément | Preuve (client 7.3) |
|---|---|
| Le dispatcher entrant connaît `5001` | `0x67e7bc` : `cmp $0x1389,%eax` puis `je 0x67e80e` ; les ids voisins traités par le même bloc sont `4503`, `4504`, `4505`, `4506` (`0x67e7c5`-`0x67e809`) |
| Le bras appelle le constructeur-analyseur | `0x67e80e`-`0x67e816` : `push %ebx`, `mov %esi,%ecx`, `call 0x671660` |
| L'analyseur lit les champs aux offsets fixes et remplit un message interne | `0x671660`-`0x6717a8` : **aucune** lecture de l'en-tête de longueur, lecture directe de `buf[7]`, `buf[8]`, `buf[10..17]`, `buf[18]`, puis boucle sur `count` entrées de 41 octets |
| Le message est mis en file pour l'interface | fin de l'analyseur `0x671739`-`0x6717a8` : ajout du pointeur de message dans la file de l'objet de gestion (`+0x2c`…`+0x3c`) |
| Le message porte une étiquette interne `0x8c` | `0x671680` : `movl $0x8c,0x4(%eax)` — le décalage de la charge utile dans l'objet est également stocké (`movl $0x13,0xb(%eax)` en `0x67168f`) |
| `5000` n'est pas routé | il n'apparaît dans aucune table de dispatch entrant ; il n'est qu'émis |

**Ce qui reste hors de la fiche** : l'identité du consommateur interface du message (voir §7g). Ce
qui est prouvé, c'est que le client **accepte** `5001` et l'analyse — pas la fenêtre qui l'affiche.

### 2.3 Contrôles du client (ce qu'il ne fait pas, donc ce que le serveur doit garantir)

- **Aucun contrôle de longueur** : l'analyseur `0x671660` ne lit jamais le champ `Length` ; il fait
  confiance au compteur `records` du contenu. Une longueur incohérente désaligne la lecture (et une
  longueur **plus courte** que `count × 41 + 20` fait lire le client au-delà du tampon reçu).
- **Aucun contrôle de borne sur `records`** : le message interne est alloué à taille fixe (`0x1ba`
  = 442 octets, `0x671668`) et contient `32` octets d'en-tête, donc `410` octets d'entrées, soit
  exactement **10 entrées de 41 octets**. Onze entrées font écrire le client au-delà de son
  allocation.
- **Le nom est copié jusqu'au NUL** : `0x671700`-`0x671708` (`mov (%eax),%cl ; mov %cl,(%edx,%eax,1)
  ; inc %eax ; test %cl,%cl ; jne`). Un `ranker_name` sans NUL dans ses 31 octets déborde sur le
  `score` de l'entrée suivante.

---

## 3. Structure sur le fil

### 3.1 En-tête commun (7 octets)

Identique pour les deux sens : `uint32 Length` (offset 0), `uint16 ID` (offset 4), `uint8 Checksum`
(offset 6, somme des octets 0 à 5). Côté serveur : `Game/Network/Packets/Header.cs:8-12` et
`CreatePacket`/`WriteChecksum` (`Game/Network/Packets/Game/GameCharacterPackets.cs:382-388`).
Côté client, les trois champs sont écrits par le constructeur de trame
(`0x48d18d` longueur, `0x48d182`-`0x48d187` id, `0x48d195`-`0x48d1a7` somme) — le client **calcule**
cette somme, le serveur n'a donc aucune raison de s'en dispenser.

### 3.2 `TM_CS_RANKING_TOP_RECORD` (5000) — **8 octets**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0-3 | `uint32` LE | `length` | `8` | client `0x48d18d` (`movl $0x8,(%eax)`) ; `Header.cs:10` côté serveur |
| 4-5 | `uint16` LE | `id` | `5000` (`0x1388`) | client `0x48d182`-`0x48d187` ; `reference/rzu/.../TS_CS_RANKING_TOP_RECORD.h:9` |
| 6 | `uint8` | `checksum` | somme des octets 0-5 | client `0x48d195`-`0x48d1a7` |
| 7 | `int8` | `ranking_type` | **`0`** (seule valeur émise par le client) | client `0x49d2e9` (`movb $0x0,-0x11(%ebp)`, tampon en `ebp-0x18`) ; `reference/rzu/.../TS_CS_RANKING_TOP_RECORD.h:6` ; NGemity `.../TS_CS_RANKING_TOP_RECORD.h:7` |

Taille totale : `7 + 1 = 8` octets, **fixe**. Aucun autre champ n'est déclaré par rzu, par NGemity
ou par le client : rien à trancher, rien à défaut.

### 3.3 `TM_SC_RANKING_TOP_RECORD` (5001) — **20 + 41 × n octets**

| Offset | Type | Nom | Source rzu | Source client (analyseur `0x671660`) |
|---|---|---|---|---|
| 0-3 | `uint32` LE | `length` | en-tête rzu (`CREATE_PACKET_VER_ID`, `PacketDeclaration.h:610-612`) | non lu par l'analyseur (`0x671660` ne touche pas `[0..6]`) |
| 4-5 | `uint16` LE | `id` = `5001` | `.../TS_SC_RANKING_TOP_RECORD.h:21` | dispatcher `0x67e7bc` (`cmp $0x1389,%eax`) |
| 6 | `uint8` | `checksum` | en-tête rzu | non lu par l'analyseur |
| 7 | `int8` | `ranking_type` | `.../TS_SC_RANKING_TOP_RECORD.h:14` | `0x6716ad` : `mov 0x7(%ebx),%al` → `mov %al,0x13(%edi)` |
| 8-9 | `uint16` LE | `requester_rank` | `:15` | `0x6716b3` : `movzwl 0x8(%ebx),%ecx` → `0x6716b8` |
| 10-17 | `int64` LE | `requester_score` | `:16` | `0x6716bf` (poids faible, `0xa(%ebx)`) et `0x6716bc` (poids fort, `0xe(%ebx)`) |
| 18-19 | `uint16` LE | `records` (compteur) | `:17` (`_(count)(uint16_t, records)`) | `0x6716d4` : `movzwl 0x12(%ebx),%ecx` |
| 20 + 41k + 0 | `uint16` LE | `rank` | `.../TS_SC_RANKING_TOP_RECORD.h:6` (`TS_RANKING_RECORD`) | `0x6716f0` : `mov -0x2(%esi),%ax` avec `esi = buf+22` (donc `buf+20`) |
| 20 + 41k + 2 | `char[31]` | `ranker_name` | `:7` (`_(string)(ranker_name, 31)`) | `0x6716e7` (`lea 0x16(%ebx),%esi`) et boucle `strcpy` `0x671700`-`0x671708` |
| 20 + 41k + 33 | `int64` LE | `score` | `:8` | `0x67170d` (poids faible, `0x1f(%esi)`) et `0x67170a` (poids fort, `0x23(%esi)`) |
| — | — | pas de l'entrée | — | `0x67172c` : `add $0x29,%esi` (41) ; `0x67172f` : `add $0x29,%edi` |

Sonde indépendante du pas de 41 : `0x671727` relit le compteur `movzwl 0x12(%ebx)` à chaque tour et
compare avec l'index (`0x671735` : `cmp %ecx,%eax ; jl 0x6716f0`) — la boucle fait donc exactement
`records` tours, sans borne de sécurité.

**Aucun remplissage** entre le compteur (18-19) et la première entrée (20) : le décalage
`0x16` du nom (`buf+22`) et le `-0x2(%esi)` du rang (`buf+20`) encadrent le même début d'entrée.
Un alignement 4 octets de la liste, tel qu'on pourrait le supposer d'un analyseur naïf, est donc
**exclu** par le client.

### 3.4 Récapitulatif des tailles

| Id | Charge utile | Total (en-tête compris) |
|---|---|---|
| 5000 | `int8 ranking_type` = 1 octet | **8** |
| 5001, `records = 0` | `int8 + uint16 + int64 + uint16` = 13 octets | **20** |
| 5001, `records = n` | `13 + 41n` | **20 + 41n** |
| 5001, `records = 10` (maximum que le client sait recevoir, D9) | `13 + 410` | **430** |

### 3.5 Règle des chaînes

`ranker_name` est un tampon **fixe de 31 octets**, NUL compris, et il doit **contenir un NUL** :
la copie du client est un `strcpy` (`0x671700`-`0x671708`) qui s'arrête au premier octet nul et
déborde sur le champ suivant dans le cas contraire. Le nom est donc tronqué à **30 caractères**.

### 3.6 Échelle des `score` : valeur du fil = valeur affichée **× 10 000**

| Site | Preuve |
|---|---|
| `requester_score` | `0x6716bf`/`0x6716bc` chargent les deux moitiés de `buf[10..17]`, puis `0x6716c2` empile `0x2710` (10 000) et `0x6716c9` appelle `0x97c890` (division 64 bits signée, `__alldiv` : le corps de `0x97c890` signe ses opérandes puis divise) ; le quotient est rangé en poids faible `0x6716d1` et fort `0x6716ce` |
| `score` de chaque entrée | même appel en `0x671719`, quotient rangé en `0x67171e` et `0x671724` |

Conséquence : le champ est un `int64` **signé**, exprimé en unités de 1/10 000. Ni rzu ni NGemity
n'écrivent cette échelle ; seul le client la porte. Un `score` de `12345` sur le fil s'affiche
`1`, et un score négatif est un cas que l'analyseur accepte (division signée) mais dont la
sémantique n'est établie par personne (§7b).

---

## 4. Gating de version

### 4.1 Ce que `X(<id>, true)` signifie, prouvé

Les deux en-têtes rzu déclarent leurs ids par `CREATE_PACKET_VER_ID` avec une liste `_ID` d'une
seule entrée (`librzu/src/packets/GameClient/TS_CS_RANKING_TOP_RECORD.h:8-9`,
`TS_SC_RANKING_TOP_RECORD.h:20-21`). Le second argument de `X(...)` est substitué par
`SERIALISATION_F_ID2(id_, condition_)` en `if(condition_) id = id_;`
(`librzu/src/lib/Packet/PacketDeclaration.h:587-589`) : `true` est la **condition C++ littérale**,
pas une convention « valide partout ». `getId(version)` retient le dernier id dont la condition est
vraie (`PacketDeclaration.h:591-607`, `packets_ids_list::getLatest()` sur `PACKET_IDS`) ; avec
`true`, cette sélection est indépendante de la version.

La fiche voisine `docs/packet-specs/socle-competition-joueurs.md` (§4, décision D1) cite ce même
mécanisme aux lignes `585-588` ; les lignes exactes de la macro sont `587-589`. La conclusion est
la même : `true` ⇒ **aucun gating**.

### 4.2 Id par id

| Id | Déclaration | Gating | Décision pour 7.3 |
|---|---|---|---|
| 5000 | `X(5000, true)` | aucun | **id `5000` conservé** |
| 5001 | `X(5001, true)` | aucun | **id `5001` conservé** |

Le commit rzu `11f2b6fd69913c192e6103afd3953a358ef6226a` (« packets: use versionned ID for all
packets and update their ID with epic 9.6.3 », 2020-08-11) est celui qui a introduit la forme
versionnée : son diff sur ces deux fichiers montre que `CREATE_PACKET(..., 5000)` et
`CREATE_PACKET(..., 5001)` sont devenus `X(5000, true)` / `X(5001, true)` — **les valeurs d'id n'ont
pas bougé** à l'occasion du passage à 9.6.3. Aucune réutilisation : `X(5000, …)` et `X(5001, …)`
sont les seuls ids de la plage 5000-5999 de tout `librzu/src/packets/`.

### 4.3 Champs

Aucun champ des deux structures ne porte de condition de version : les deux en-têtes rzu n'ont ni
commentaire `Since EPIC_x`, ni variante de champ selon la version, et NGemity reproduit les
structures à l'identique (`shared/Server/Packets/GameClient/TS_CS_RANKING_TOP_RECORD.h:6-7`,
`TS_SC_RANKING_TOP_RECORD.h:6-18`). La version 7.3 est donc entièrement tranchée pour ce socle.

### 4.4 Confirmation par le client

Le client 7.3 tranche à son tour, sans ambiguïté : il connaît `5000` en sortie (constructeur
`0x48d160`, id `0x1388`) et `5001` en entrée (dispatcher `0x67e7bc`, `cmp $0x1389`). Aucun de ces
deux ids n'est absent du binaire, et il n'existe pas de variante d'id plus récente à craindre pour
ces deux paquets.

---

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` fait de ces paquets : **rien**

| Recherche | Résultat |
|---|---|
| `grep -rn "RANKING_TOP_RECORD" reference/ngemity` | seulement les deux en-têtes, les `#include` de `shared/Server/XPacket.h:135` et `:275`, et les deux lignes de `shared/Server/ClientPackets.h:277-278` |
| `grep -rn "Ranking\|5000\|5001" reference/ngemity/Chihiro` | aucun résultat |
| `reference/rzu` | les deux structures, aucune logique (bibliothèque de sérialisation) |
| `Navislamia` | aucun `RANKING` : ni énumération, ni handler, ni service, ni entité |

Comme les socles « compétition entre joueurs » et « instances de jeu » : **protocole pur**, aucun
comportement de référence à porter.

### 5.2 Ce que le serveur doit faire (obligations prouvées par le client)

| # | Obligation | Preuve |
|---|---|---|
| K-K1 | Une trame `5001` a une longueur de **`20 + 41 × records`** exactement, et `records` doit égaler le nombre d'entrées réellement écrites | boucle du client `0x671727`-`0x671737`, sans borne ni contrôle de longueur (§2.3) |
| K-K2 | **`records ≤ 10`** | message interne de `0x1ba` = 442 octets moins 32 d'en-tête = 410 = 41 × 10 (`0x671668`) |
| K-K3 | Chaque `ranker_name` doit contenir un **NUL dans ses 31 octets** | `strcpy` du client `0x671700`-`0x671708` |
| K-K4 | `requester_score` et chaque `score` sont des `int64` **en 1/10 000** : écrire `valeur × 10 000` | divisions du client par `0x2710`, `0x6716c9` et `0x671719` |
| K-K5 | Le `ranking_type` de la réponse doit **recopier celui de la demande** (le client n'émet que `0`) | le client stocke le champ reçu (`0x6716b0`) sans le comparer à sa demande : rien ne l'y oblige, mais c'est la seule réponse non ambiguë, et c'est gratuit |
| K-K6 | Une trame `5000` reçue a **8 octets** ; toute autre longueur est un client non conforme | le client n'émet que 8 octets (`0x48d18d`) |
| K-K7 | Le champ `checksum` doit être calculé | le client le calcule lui-même (`0x48d195`-`0x48d1a7`) : c'est la convention du protocole, pas une option |
| K-K8 | Aucune autre trame n'est attendue : pas de `TS_SC_RESULT`, pas d'acquittement | le client n'arme aucun état d'attente pour `5000` (§2.1, étape 5 : il envoie et rend la main) |

### 5.3 Réponse à une trame `5000` mal formée

Une longueur ≠ 8 ne peut pas venir du client 7.3. Le socle voisin a tranché « journaliser, sans
réponse » pour ses trames mal formées, et la même règle est retenable ici — mais elle mérite d'être
arbitrée, parce qu'un `TS_SC_RESULT` avec `RequestMsgID = 5000` n'a **aucune garantie d'affichage**
(§7i).

### 5.4 Socle minimum implémentable — décision

**Question du PO, tranchée : le socle n'est pas réductible à « pas de code ».** Le client émet
`5000` (D3) et route `5001` (D4) ; le format est entièrement déterministe (D5 à D10), donc lisible
et testable en offsets, comme les deux socles voisins.

Raisons, dans l'ordre où elles décident :

1. **L'émission est prouvée**, pas supposée : un seul site d'appel, `ranking_type` écrit en clair,
   longueur `8` codée en dur (§2.1).
2. **Le format est doublement sourcé** : rzu et NGemity donnent la même disposition, et le client
   la confirme champ par champ, en lecture (§3.3) comme par ses pas de boucle (§3.4).
3. **La réponse minimale ne demande aucune politique** : une `5001` à `records = 0` est une trame
   complète, valide et de taille connue (`20`), qui ne présuppose ni classement, ni métrique, ni
   nombre d'entrées, ni rang de demandeur.
4. **Ce qui reste dehors a une raison nommable** : aucun classement n'existe côté serveur (§1.2),
   aucune référence n'en implémente (§5.1), et les choix (type retenu, métrique, cadence, valeur du
   rang inconnu) sont des décisions de contenu qui appartiennent à Killian (§7).

Conséquence pratique : le lot K1 rend le paquet **reçu et traité proprement** au lieu de lever
`Unknown Packet Type` (`Game/Network/Clients/GameClient.cs:802`), et pose l'écrivain de la réponse
avec ses offsets. Il ne rend **aucun** classement jouable : c'est le socle *protocole*, pas le socle
*fonctionnel*.

### 5.5 Découpage proposé pour `navis-dev`

| Lot | Contenu | Opcodes | Critère d'acceptation propre |
|---|---|---|---|
| **K1** *(ce socle)* | `5000` lu et routé ; `5001` écrit à vide (`records = 0`) | 5000, 5001 | test d'offsets des deux tailles (**8** et **20**, avec la position de chaque champ) ; test de la formule **20 + 41n** pour n ∈ {0, 1, 2, 10} ; `Length` de `5000` refusée si ≠ 8 ; NUL exigé dans les 31 octets d'un nom (test avec un nom de 31 octets sans NUL) ; `5000` déclaré dans `GamePackets` **et** routé (il n'atteint plus `GameClient.cs:802` → critère transversal n° 4) ; `5001` déclaré dans `GamePackets` parce qu'il est **émis** par K1 |
| **K2** | La source des données : champ `ranking_type` accepté, jeu de données, métrique, nombre d'entrées | 5000, 5001 | suppose une décision de Killian sur §7a, §7b, §7c, §7e |
| **K3** | Le rang et le score du demandeur (`requester_rank`, `requester_score`) | 5001 | suppose la décision §7d (rang inconnu) et l'hypothèse §3.6 sur l'échelle |

Dépendances : **K1 est indépendant de tout** (aucune donnée, aucun autre paquet, aucun service).
K2 → K3. Rien n'oblige à faire les trois lots.

Le **paquet restant**, prêt à rouvrir sans refaire l'archéologie :

| Paquet | Opcode | Sens | Format (charge utile → total) | Prérequis pour le rouvrir |
|---|---|---|---|---|
| `TM_SC_RANKING_TOP_RECORD` | 5001 | S → C | `int8 ranking_type` + `uint16 requester_rank` + `int64 requester_score` + `uint16 records` + `records × (uint16 rank + char[31] name + int64 score)` → **20 + 41n** (§3.3) | lot **K1** pour la trame à vide ; lots **K2/K3** pour un contenu, plus les décisions §7a-§7e |

**Voisins immédiats de l'opcode** (aucun n'appartient à ce socle, aucun n'est à ouvrir ici) :

| Id | Nom | Pourquoi il n'est pas de ce socle |
|---|---|---|
| 4500-4506 | `TM_CS/SC_COMPETE_*` | socle « compétition entre joueurs », fiche déjà écrite ; aucun id ni format partagé |
| 4250-4253 | `TM_CS/SC_INSTANCE_GAME_*` | socle « instances de jeu », fiche déjà écrite |
| 6000-6008 | `TM_CS/SC_*FARM*`, `FOSTER`, `NURSE` | famille « élevage d'invocations », sans rapport |
| 4713-4719 | `TS_CS/SC_BATTLE_ARENA_*` | présents chez NGemity **seulement** : absents de `op_codes.md` (§1.3) |

### 5.6 Points d'implémentation à respecter

| Point | Règle | Source |
|---|---|---|
| Enum et dispatch ensemble | tout membre ajouté à `GamePackets` **reçu** par le serveur doit être routé, sinon il atteint `_ => throw new Exception("Unknown Packet Type")` | `Game/Network/Clients/GameClient.cs:802` ; critère transversal n° 4 |
| Forme du dispatch | la boucle existante enchaîne des `if (header.ID == (ushort)GamePackets.X) { Handle…; continue; }` **avant** le `switch` final — suivre ce style | `GameClient.cs:726-729` (exemple), `:790-802` |
| Écriture d'une trame de taille variable | `CreatePacket(GamePackets.TM_SC_RANKING_TOP_RECORD, 20 + 41 * n)` puis écriture aux offsets nommés, puis `WriteChecksum` — c'est le motif déjà employé pour `209`/`205` | `GameCharacterPackets.cs:19`, `:382-388` (`CreatePacket`), `GameCharacterPackets.BuildEraseItem` (motif « compteur + enregistrements ») |
| En-tête de réponse | `uint32 Length` = taille réelle, `uint16 ID` = 5001, `uint8 Checksum` | `Header.cs:8-12` |
| Lecture de la demande | exiger `Length == 8`, puis lire `ranking_type` à l'offset 7 | §3.2 |
| Tests | au moins **366** tests (448 au moment de la fiche), plus un test d'offsets par trame lue ou écrite : taille totale **et** position de chaque champ | critères transversaux n° 2 et 3 |
| `CLAUDE.md` | ne pas l'écrire : le bloc ci-dessous part dans la description de la MR, porté par la QA | critère transversal n° 5 |

### 5.7 Cas limites

| Cas | Ce que fait le client | Comportement serveur recommandé |
|---|---|---|
| `Length` ≠ 8 reçu pour `5000` | impossible depuis le client 7.3 (`0x48d18d` écrit 8) | journaliser, sans réponse (§5.3, arbitrage §7i) |
| `ranking_type` ≠ 0 reçu | le client n'en émet jamais d'autre | accepter la trame, journaliser la valeur, répondre une `5001` qui **recopie** le type demandé avec `records = 0` — ne pas déconnecter (§7a) |
| `records` ≠ nombre d'entrées réellement écrites | le client boucle `records` fois et lit au-delà du tampon reçu | interdiction absolue : le compteur du fil doit être le nombre réel d'entrées |
| `records` > 10 | au-delà de 10, le client écrit hors de son allocation de 442 octets | plafonner à 10 côté serveur ; c'est une **borne de protocole**, pas une préférence de contenu |
| Nom de 31 caractères sans NUL | `strcpy` du client : débordement sur le `score` de l'entrée | tronquer à 30 caractères et écrire le NUL |
| `score` négatif | l'analyseur accepte (division signée) | aucune règle établie ; ne rien envoyer de négatif sans arbitrage (§7b) |
| `5001` reçue sans demande préalable | analysée et mise en file ; le client ne garde aucun état d'attente | possible techniquement ; l'opportunité d'une poussée non sollicitée est un arbitrage (§7j) |

---

## 6. Écarts assumés avec NGemity, et pourquoi

| # | Écart | Décision |
|---|---|---|
| E1 | **Aucun écart de format.** NGemity reproduit les deux structures de rzu à l'identique, ids compris (`shared/Server/ClientPackets.h:277-278`, `.../TS_CS_RANKING_TOP_RECORD.h:6-9`, `.../TS_SC_RANKING_TOP_RECORD.h:6-20`) | suivre rzu/NGemity, confirmés par le client |
| E2 | **Aucune logique chez NGemity** : `Chihiro` ne traite ni `5000` ni `5001` | ne rien porter ; le socle est du protocole pur |
| E3 | **Le facteur 10 000 du `score` n'existe dans aucune des deux références** : c'est le client qui divise | l'inscrire dans l'implémentation (K-K4) ; c'est un écart des références au client, pas du serveur aux références |
| E4 | **La borne de 10 entrées n'existe dans aucune des deux références** : `_(dynarray)` n'est pas borné côté rzu/NGemity | l'inscrire comme contrainte de protocole (K-K2) et ne jamais l'ignorer au nom de la souplesse du `dynarray` |
| E5 | **Rien à porter de la famille Battle Arena voisine** (`4713`-`4719`), absente de 7.3 | ne pas l'ouvrir |

---

## 7. `NON ÉTABLI`

### (a) Domaine et sémantique de `ranking_type`

`int8`. Le client 7.3 n'émet **que** `0` (site unique, `0x49d2e9`) et ne compare jamais le type
reçu. Aucune énumération dans rzu ni NGemity. Le seul indice de sens dans le binaire est le nom de
la fenêtre déclenchante : `window_donation_ranking.nui`, `button_donation_ranking`,
`window_donation_msgbox_rankingitem.nui`, `myrank_01`, `Atext_rank_num` (chaînes `.rdata`, offsets
`strings -t x` `0x642b70`, `0x63bbdc`, `0x63b974`, `0x63eacf` du binaire) — ce qui **suggère** un
classement des dons, donc un lien avec `258`/`259` (`TM_CS_DONATE_ITEM`, `TM_CS_DONATE_REWARD`),
mais aucune preuve : la correspondance `ranking_type` ↔ jeu de données reste à trancher. Faut-il
les traiter comme opaques (recopier) ou définir un domaine ?

### (b) Métrique du `score` et sens de l'échelle 10 000

Le client divise par 10 000 (D8). Est-ce une valeur fixe à 4 décimales, un pourcentage, un montant
de dons divisé par un facteur d'affichage ? Aucune référence ne le dit. La question du signe (le
client accepte un `int64` négatif) n'est pas tranchée non plus.

### (c) Nombre d'entrées à envoyer

La **borne haute** est établie et dure : 10 (D9). Le nombre **effectif** que le serveur doit
envoyer (top 10, top 5, liste complète) est une décision de contenu, non sourçable ici.

### (d) `requester_rank` / `requester_score` d'un joueur non classé

Aucune référence ne dit quoi envoyer : `0` ? `0xFFFF` ? Une valeur sentinelle ? Le client stocke les
deux champs (`0x6716b3`-`0x6716d1`) et les affiche sans contrôle visible. La fenêtre porte un
contrôle `myrank_01` (« mon rang ») qui suggère un usage réel de ces deux champs, mais le lien n'est
pas prouvé.

### (e) Source des données

Aucun chemin n'existe : `ICharacterService` ne lit un personnage que **par compte**
(`:12`, `GetCharactersByAccountNameAsync`) ou **par nom** (`:20`, `GetCharacterByName`), il n'y a
ni table de classement, ni compteur persistant, ni tâche périodique, ni vue agrégée. Requête directe
sur les personnages (ce qui suppose une métrique et un ordre), table persistée alimentée à
l'écriture, ou tâche périodique : décision d'architecture **et** de contenu, à Killian.

### (f) Cadence de rafraîchissement et poussée non sollicitée

Rien n'indique si le serveur ne répond qu'à une demande ou s'il pousse des `5001` (ouverture de la
fenêtre par un événement, mise à jour périodique). Le client sait recevoir une `5001` sans demande
(§5.7), mais l'opportunité est un choix de contenu.

### (g) Consommateur interface du message

La classe `SUIRankingWnd` existe (RTTI `.?AVSUIRankingWnd@@`, offset `strings -t x` `0x81a7d8` en
`.data` ; création `0x635a46`, allocation `0x4a4` octets, vtable `0xa45a8c` ; journal
`"Create : SUIRankingWnd"` en `0x6484a0`) et le message analysé porte l'étiquette interne `0x8c`
(`0x671680`). Le dispatcher qui draine la file et associe cette étiquette à cette fenêtre **n'a pas
été localisé** : la preuve que `5001` alimente bien `SUIRankingWnd` reste à faire. Ce qui est
prouvé : le client analyse la trame et la met en file.

### (h) Le contrôle exact qui déclenche la commande `Ranking_Top_Record`

La commande est enregistrée sous ce nom (`0x5f5ff8`-`0x5f602a`) et l'émission est conditionnée à sa
présence (`0x49d2c5`), mais **quel bouton ou quel événement** l'invoque n'est pas déterminable
ici : les fichiers `.nui` ne sont pas dans `/srv/navislamia/reference/client73` (seuls
`SFrame.exe` et des `.rdb`), et la chaîne `"window_donation_ranking.nui"` n'est qu'un nom de
ressource.

### (i) Refus d'un `5000` mal formé

Répondre par `TS_SC_RESULT` avec `RequestMsgID = 5000` suppose que le client possède une boîte ou un
texte pour ce couple ; cela n'a **pas** été vérifié pour `5000` (le socle voisin a fait ce travail
pour `4500`/`4502`). Recommandation par défaut : journaliser sans répondre. À confirmer, ou à
remplacer par un code de `ResultCode` si l'affichage est prouvé.

### (j) Réponse à vide (`records = 0`) : effet perçu

La trame de 20 octets est valide et le client la traite (la boucle ne fait aucun tour). Ce que
l'interface affiche alors (liste vide, fenêtre vide, ou rien) n'est pas établissable sans exécuter
le client. C'est la seule réserve du lot K1, et elle se lève par un arbitrage explicite : soit la
trame à vide est assumée, soit le serveur ne répond rien du tout (§9 de la carte).

### (k) Égalité des `ranking_type` demande / réponse

Le client ne compare pas le type reçu à celui qu'il a demandé : il ne peut donc pas détecter une
réponse pour un autre type. Qu'un serveur multi-classements doive recopier le type demandé est une
règle **recommandée** (K-K5), non une obligation prouvée.

---

## 8. Commits et binaires épinglés

| Référence | Identifiant | Usage |
|---|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | format, tailles, ordre des champs, sémantique de `X(id, true)` |
| `reference/rzu` — création des deux en-têtes | `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07, « Adjust count management for packets and add all known GS packets as of 9.4 ») | ids `5000`/`5001` et dispositions d'origine |
| `reference/rzu` — ids versionnés | `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11, « use versionned ID for all packets and update their ID with epic 9.6.3 ») | `X(5000, true)`, `X(5001, true)` ; les ids n'ont pas bougé |
| `reference/rzu` — retouches d'en-tête | `39300afe815e6019f48ebf086ee39227799e487a`, `166865830359f94c3fba5a7a6f4e2f79136b9dd6` (2020-04) | `#undef` et `#pragma once`, sans effet sur le protocole |
| `reference/ngemity` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | déclarations (ids et structures) ; confirme l'absence de logique |
| `reference/ngemity` — reprise des structures de rzu | `44b7d25dac7a1f869490dc8b6ea3255ea68b8eb4` (2018-08-26, « Network: Rewriting network code to use @glandu2 's structs ») | disposition NGemity ; plus ancien commit de l'historique suivi de `TS_CS/SC_RANKING_TOP_RECORD.h` |
| `reference/ngemity` — plus ancien commit de l'historique | `90a500d46acc39d21ef943a047a37d0a641f7af4` (2018-08-24, « WIP: add support for serializable packets ») | première apparition des deux en-têtes |
| `Navislamia` (base) | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | `GamePackets`, `Header`, `CreatePacket`, `GameClient.Receive`, `ICharacterService` |
| Fiches voisines | `docs/packet-specs/socle-competition-joueurs.md` et `docs/packet-specs/socle-instances-jeu.md` (branches `hermes/packet-socle-competition-joueurs`, `hermes/packet-socle-instances-jeu`) | méthode de lecture du client, décision D1 sur `X(id, true)`, découpage des socles voisins |
| Client Epic 7.3 | fichier de ressource `reference/client73/SFrame.exe` — **pas un dépôt git, donc aucun commit** : 9 841 664 octets, empreinte de fichier sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | toutes les preuves `0x…` de cette fiche |

Méthode de lecture du client, reproductible : `objdump -d SFrame.exe` (désassemblage **linéaire
complet** ; un balayage par motif d'octets est trompeur — un motif `e8` suivi de 4 octets saute les
appels dont l'octet précédent vaut `e8`) ; mapping des sections PE `.text` VA `0x401000` / offset
`0x400`, `.rodata` VA `0xa0c000` / offset `0x60ae00`, `.rdata` VA `0xa0f000` / offset `0x60da00`,
`.data` VA `0xc10000` / offset `0x80e200` ; chaînes par `strings -t x SFrame.exe` (les offsets
cités dans cette fiche sont ceux de `strings`, convertis en VA par le mapping ci-dessus) ; noms
RTTI MSVC par `vtable-4 → COL → +0xC → type descriptor → +8`. **Aucune exécution du client.**

---

## 9. Implémentation du lot K1

Livré sur la branche `hermes/packet-socle-classements`, dans la continuité du commit de cette fiche.
L'état du dépôt après le lot : `dotnet build Navislamia.sln -c Debug` → code 0 (0 erreur, 160
avertissements préexistants), `dotnet test Tests/Tests.csproj` → code 0, **482 tests passés** (448
avant le lot, 34 ajoutés).

| Élément | Fichier | Contenu |
|---|---|---|
| Énumération | `Game/Network/Packets/Enums/GamePackets.cs:99-100` | `TM_CS_RANKING_TOP_RECORD = 5000`, `TM_SC_RANKING_TOP_RECORD = 5001` |
| Lecture de `5000` | `Game/Network/Packets/Game/GameActionPackets.cs` (`RankingTopRecordRequest`, `TryReadRankingTopRecord`) | `Length` **exactement 8** exigée, `ranking_type` lu en `sbyte` à l'offset 7 |
| Écriture de `5001` | `Game/Network/Packets/Game/GameRankingPackets.cs` | `BuildRankingTopRecord(ranking_type, requester_rank, requester_score, records)` ; constantes `AnswerHeaderSize = 20`, `RecordSize = 41`, `NameLength = 31`, `MaxNameLength = 30`, `MaxRecords = 10` ; `GetAnswerSize(n) = 20 + 41n`, `GetRecordOffset(i) = 20 + 41i` |
| Dispatch | `Game/Network/Clients/GameClient.cs` | bras `TM_CS_RANKING_TOP_RECORD` → `HandleRankingTopRecord` ; bras `TM_SC_RANKING_TOP_RECORD` → `Warning` + `continue` (patron `TM_SC_REGION_ACK`) |
| Tests | `Tests/Game/RankingTopRecordPacketsTests.cs` | 34 tests d'offsets, de tailles, de NUL et de bornes |

Comportement effectif du handler, pour un `5000` reçu :

1. `Length != 8` → `Warning` nommant le client et la longueur, **aucune réponse** (arbitrage §7i) ;
2. sinon → `BuildRankingTopRecord(ranking_type recopié, requester_rank = 0, requester_score = 0,
   records vide)` : une `5001` de **20** octets, envoyée au seul client demandeur ;
3. aucun `TS_SC_RESULT`, aucun état d'attente (K-K8) ; le `checksum` est recalculé (K-K7).

### 9.1 Décisions prises dans le code là où la fiche laissait un choix

| # | Situation | Décision du lot | Pourquoi |
|---|---|---|---|
| I1 | Plus de 10 entrées fournies à l'écrivain | **plafonnement silencieux à 10** (pas d'exception) | un `throw` dans un chemin d'envoi casse la boucle de réception ; le plafond préserve l'invariant K-K1 (compteur = entrées réellement écrites), une exception le reporterait sur tous les appelants |
| I2 | Nom de plus de 30 caractères ou sans NUL | tronqué à **30** puis NUL, champ NUL-rempli | K-K3 : le `strcpy` du client déborderait sur le `score` de l'entrée |
| I3 | `records` vide / `null` | traité comme zéro entrée → 20 octets | le lot n'a aucune source de données |
| I4 | `ranking_type` ≠ 0 reçu | accepté, journalisé, **recopié** dans la réponse, `records = 0` | K-K5, §5.7 : ne pas déconnecter, ne rien inventer sur le domaine (§7a) |
| I5 | `5001` reçue du client | `Warning` + `continue` | critère transversal n° 4 : aucun membre de `GamePackets` ne doit atteindre `_ => throw new Exception("Unknown Packet Type")` |
| I6 | Échelle des `score` | l'écrivain reçoit la **valeur du fil** et ne multiplie pas | §3.6/K-K4 : le seul endroit qui connaît la métrique est le producteur de données (lot K2/K3) ; l'écrivain ne devine ni ne transforme |

### 9.2 Ce que le lot K1 ne fait pas

Aucune donnée de classement : `records` est toujours vide et les deux champs du demandeur valent `0`.
Le lot K2 (source, métrique, nombre d'entrées) et le lot K3 (rang et score du demandeur) restent
entiers, et dépendent des arbitrages §7a-§7f. Le paquet est désormais **reçu et traité proprement**
au lieu de lever `Unknown Packet Type`.

---

## Bloc prêt à coller dans `CLAUDE.md`

> ### Socle classements de joueurs — 5000/5001 (`TM_CS/SC_RANKING_TOP_RECORD`)
>
> **Deux opcodes, tous deux `X(<id>, true)` chez rzu : aucun gating de version, aucun champ gaté.**
> `true` n'est pas une convention « valide partout » mais la **condition C++ littérale** substituée
> dans `if(condition_) id = id_;` (`PacketDeclaration.h:587-589`) : `5000` et `5001` sont donc
> identiques en 7.3 et dans toutes les versions. La plage 5002-5999 est vide dans rzu comme chez
> NGemity. Fiche complète : `docs/packet-specs/socle-classements.md`.
>
> **Tailles à écrire en dur** (source : rzu + constructeur et analyseur du client 7.3) :
> `5000` = **8** octets fixes (`int8 ranking_type` à l'offset **7**) ; `5001` = **20 + 41 × n**
> octets, soit **20** à vide — en-tête, `int8 ranking_type` (7), `uint16 requester_rank` (8),
> `int64 requester_score` (10), `uint16 records` (18), puis `records` entrées de **41** octets
> commençant à l'offset **20** : `uint16 rank` (+0), `char[31] ranker_name` (+2),
> `int64 score` (+33). **Aucun remplissage** entre le compteur et la première entrée.
>
> **Le client 7.3 émet bien `5000`** : constructeur de trame `0x48d160` (longueur `8`), appelé depuis
> l'unique site `0x49d2de`, avec `ranking_type = 0` écrit en clair (`0x49d2e9`). Il part de la
> commande UI enregistrée sous le nom `Ranking_Top_Record` (fenêtre `window_donation_ranking.nui`).
> Aucun autre `ranking_type` n'est atteignable. Le client **route `5001`** (dispatcher entrant
> `0x67e7bc`, `cmp $0x1389`, analyseur `0x671660`) et ne route pas `5000`.
>
> **Trois obligations que le client impose au serveur, et qu'aucune référence n'écrit** : (1)
> `records` égale le nombre réel d'entrées — le client boucle `records` fois et ne lit jamais
> l'en-tête de longueur ; (2) **`records ≤ 10`** — le message interne du client fait 442 octets,
> soit 32 d'en-tête + 41 × 10, et rien ne borne le compteur côté client ; (3) chaque
> `ranker_name` contient un **NUL dans ses 31 octets** — la copie du client est un `strcpy`
> (`0x671700`). Enfin, **les deux `score` sont divisés par 10 000 par le client** avant tout usage :
> la valeur du fil est la valeur affichée **× 10 000**.
>
> **Ne pas porter NGemity** : les deux structures y sont déclarées et **jamais traitées** (`Chihiro`
> n'a aucun `Ranking`). `librzu` non plus. C'est du protocole pur, comme les socles compétition et
> instances de jeu.
>
> **Socle minimum (K1)** : `5000` déclaré dans `GamePackets` **et** routé dans `GameClient.cs`
> (critère transversal n° 4), `Length == 8` exigée, puis réponse `5001` à `records = 0` (20 octets,
> `ranking_type` recopié, `requester_rank`/`requester_score` à zéro) construite avec
> `CreatePacket` + `WriteChecksum` (`GameCharacterPackets.cs:19`, `:382-388`). `5001` est déclaré
> parce qu'il est émis. Aucune donnée de classement, aucune métrique, aucune cadence : la source
> des données est le lot K2, et elle appartient à Killian. Découpage K1…K3 : §5.5 de la fiche.
>
> **Non tranché** : domaine de `ranking_type` (le client n'émet que `0` ; la fenêtre s'appelle
> `donation_ranking`, seul indice), métrique et échelle du `score`, nombre d'entrées effectif
> (≤ 10 est une borne de protocole, pas un choix), valeur du rang et du score d'un joueur non
> classé, source des données, cadence, refus d'une trame mal formée, effet perçu d'une liste vide.
> Aucune de ces valeurs n'est devinée.

---

## A VERIFIER PAR KILLIAN

1. **Le lot K1 répond-il une `5001` vide (`records = 0`) à un `5000` reçu ?** (§5.4, §7j) C'est la
   seule réserve du lot minimal : la trame est valide, mais ce que le client affiche alors (liste
   vide ? fenêtre vide ? rien ?) n'est pas établissable sans exécuter le client, ce qui est
   interdit ici. L'alternative — ne rien répondre du tout — est plus prudente et coûte une ligne.
   **État livré par K1 : oui, une `5001` à `records = 0` est envoyée** ; passer à « aucune réponse »
   ne coûte qu'une suppression dans `GameClient.HandleRankingTopRecord`.
2. **Quel classement pour `ranking_type = 0`, et y en a-t-il d'autres ?** (§7a) Le client n'émet que
   `0`. Faut-il traiter le champ comme opaque (on recopie la valeur demandée) ou définir un domaine
   et autant de jeux de données ?
3. **Le classement attendu est-il celui des dons (`258`/`259`, fiche `259` déjà écrite) ?**
   (§1.3, §7a) La fenêtre du client s'appelle `window_donation_ranking.nui` et affiche `myrank_01` :
   c'est un indice, pas une preuve. Si oui, ce socle dépend de la famille des dons et la carte doit
   le dire.
4. **Quelle métrique de `score`, et avec quelle échelle ?** (§3.6, §7b) Le client divise par 10 000 ;
   aucune référence ne l'écrit. Le serveur doit-il écrire `valeur × 10 000` (interprétation la plus
   simple) ou l'échelle a-t-elle un autre sens (pourcentage, montant de dons) ?
5. **Combien d'entrées envoyer ?** (§7c) La borne de 10 est dure côté client ; le nombre effectif
   (top 10, top 5, liste complète) est un choix de contenu.
6. **Que valent `requester_rank` et `requester_score` quand le demandeur n'est pas classé ?**
   (§7d) `0`, `0xFFFF`, ou la dernière place ? La fenêtre a un contrôle `myrank_01`, donc le cas se
   produira. Aucune valeur n'est devinée dans cette fiche. **État livré par K1 : les deux champs
   valent `0`** (aucune source de données n'existe encore) — si `0` s'affichait comme « rang 0 » dans
   la fenêtre, c'est cette valeur qu'il faut trancher avant le lot K3.
7. **Quelle source de données, et à quelle cadence ?** (§7e, §7f) Aucun chemin n'existe : ni table,
   ni compteur, ni tâche périodique, et `ICharacterService` ne lit un personnage que par compte ou
   par nom (un top de classement exige un ordre et une agrégation, donc probablement une table ou
   une vue dédiée). Le socle K2 ne peut pas être ouvert avant cet arbitrage.
8. **Que répondre à une trame `5000` de longueur ≠ 8 ?** (§5.3, §7i) Journaliser sans réponse
   (recommandé, cohérent avec les socles voisins), ou répondre par `TS_SC_RESULT` avec
   `RequestMsgID = 5000` ? L'affichage d'un tel résultat n'a **pas** été vérifié pour cet id.
9. **Le serveur doit-il pouvoir pousser une `5001` non sollicitée ?** (§7f, §5.7) Le client l'accepte
   et la met en file, mais l'opportunité (rafraîchissement, événement) est un choix de contenu.
10. **Le socle doit-il être scindé en deux cartes** — K1 (protocole, livrable tout de suite) et K2/K3
    (données et politique, bloqués sur les points 1 à 7) ? Cette fiche tranche « oui » par
    construction (§5.5) ; c'est le PO qui scinde la carte Trello.
11. **L'échelle ×10 000 des `score` doit-elle être appliquée par l'écrivain ?** (§3.6, §9.1-I6) K1
    livre un écrivain qui reçoit la **valeur du fil** telle quelle : c'est le producteur de données
    (K2/K3) qui doit écrire `valeur affichée × 10 000`. Si tu préfères que l'écrivain multiplie
    lui-même (pour rendre l'oubli impossible), c'est une ligne et un test à changer.
12. **Le plafond de 10 entrées doit-il être silencieux ou bruyant ?** (§9.1-I1) K1 tronque la liste à 10
    sans rien dire, pour ne jamais lever dans un chemin d'envoi et pour que le compteur du fil reste
    égal au nombre d'entrées écrites. Un échec explicite (log `Error` + liste vide) est une variante
    défendable si tu préfères qu'un bug d'appelant se voie.
