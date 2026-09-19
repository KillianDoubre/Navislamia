# Fiche de paquet 281 — `TM_CS_PUTON_ITEM_SET`

> Fiche d'archéologie de protocole. Écrite par `navis-ref` avant toute implémentation :
> elle fixe l'id, la taille, l'ordre des champs et le gating de version. Le code serveur
> (`navis-dev`) suit cette fiche ; les écarts sont justifiés ici.
> Carte de travail : branche `hermes/packet-281-puton-item-set`, base `master`
> `ec76b218cd0bd7c6498d725f253abb8b431f0cd6`.
>
> Toute preuve issue de `reference/client73/` vient d'une **lecture statique** (`objdump`,
> `strings`, extraction des tables d'un octet et de pointeurs de SFrame.exe) : aucun binaire
> n'a été exécuté, aucun script Lua n'a été lancé. Les adresses citées sont des **adresses
> virtuelles** (VA) dans `SFrame.exe`,
> sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`.

---

## 1. Identité

| élément | valeur | source |
|---|---|---|
| id | `281` (0x119) | `op_codes.md:95` (`[281] = "TM_CS_PUTON_ITEM_SET"`) |
| nom serveur NavisLamia | `GamePackets.TM_CS_PUTON_ITEM_SET` | à créer, `Game/Network/Packets/Enums/GamePackets.cs` |
| nom client / rzu / NGemity | `TS_CS_PUTON_ITEM_SET` | `reference/rzu/librzu/src/packets/GameClient/TS_CS_PUTON_ITEM_SET.h` (1-12) ; `reference/ngemity/shared/Server/ClientPackets.h:109` |
| sens | client → serveur (requête) | `SFrame.exe` VA 0x67e1ea-0x67e201 : les deux tables de dispatch des opcodes **reçus** renvoient 281 au bloc « inconnu » VA 0x67ef21, comme 200/201/203/204 |
| voisins | 280 `TM_TRADE`, 282 `TM_SC_ITEM_DROP_INFO`, 283 `TM_SC_USE_ITEM_RESULT` | `op_codes.md:94-97` |
| remap d'id | `281` pour `version < EPIC_9_6_3`, `1281` au-delà | `TS_CS_PUTON_ITEM_SET.h:11-12` |

Le client 7.3 connaît le nom : la table interne id → nom enregistre `281` (VA 0x676e8e,
`mov $0x119,%eax`) avec la chaîne `TM_CS_PUTON_ITEM_SET` en `.rdata` VA 0xa53598
(`SFrame.exe`).

---

## 2. Ce que le joueur fait pour que le client l'envoie

**Établi (client)** — `281` fait partie du vocabulaire de **requêtes** du client 7.3 :
le gestionnaire de réception de `TM_SC_RESULT` (opcode 0) compare le champ
`request_msg_id` de l'accusé à `0x119` et journalise un libellé dédié
`셋트장착` (« équiper le set »), à côté des requêtes 200 (`아이템 장착`, équiper un
objet), 201 (`아이템 해제`), 203, 204, 208, 212, 219, 251, 252, 253, 256.

| preuve | adresse / chaîne |
|---|---|
| handler de réception de l'opcode 0 (`TM_SC_RESULT`) | VA 0x66db80, routé depuis la table de réception VA 0x67df89 (id 0) |
| comparaison `request_msg_id == 281` | VA 0x66dbe6 (`cmp $0x119,%eax`), saut sur le bloc VA 0x66de44 |
| libellé associé | format `셋트장착     요청결과-%s[%d]` en `.rdata` VA 0xa526dc |
| enregistrement du nom | VA 0x676e8e (`mov $0x119,%eax`), chaîne VA 0xa53598 |

Le client émet donc bien 281 : le constructeur existe et est câblé à un expéditeur.

| preuve d'émission | adresse |
|---|---|
| constructeur du paquet 281 (en-tête 7 o + 112 o, id écrit en +4) | VA 0x48c7e0 : `mov $0x119,%eax` (0x48c808), `mov %ax,0x4(%esi)` (0x48c812), `movl $0x77,(%esi)` (0x48c816), `memset` de 0x77 octets (0x48c7fc-0x48c803), checksum sur les 6 premiers octets stocké en +6 (0x48c82b) |
| expéditeur (unique appelant) | VA 0x48d7e0 : appel au constructeur à 0x48d7ee, copie `mov $0x1c,%ecx ; rep movsl` (0x48d800-0x48d808) de 28 dwords source+0x13 vers la charge utile (`lea -0x71(%ebp),%edi`, 0x48d805 = octet 7 du paquet), puis envoi par la vtable de `0xb8(%ebx)` |
| câblage de l'expéditeur | table d'envoi interne `sub $0x403` (VA 0x49e21d) : le bloc VA 0x49e27f correspond au **message interne 1042** (`0x412`), table d'octets VA 0x49ea50 / pointeurs VA 0x49e98c |

**Le geste exact du joueur — `NON ÉTABLI`.** Aucun relevé ne permet de trancher entre
« équiper une **configuration d'équipement sauvegardée** » (les 24 emplacements envoyés
d'un coup) et « équiper toutes les pièces d'un **set d'objet** ». Aucune classe d'entrée
`SInput*` dédiée n'existe (`SFrame.exe` : `SInputMove`, `SInputAttack`, `SInputCastCancel`,
`SInputSkill`, `SInputTakeItem`, `SInputSit`, `SInputSitUp`, `SInputMount`, `SInputUnMount`,
`SInputUseItem` — pas de `SInputPutonItemSet`), et le libellé `셋트장착` n'apparaît
qu'une fois, dans la table de journalisation des accusés. Voir §7.

---

## 3. Structure sur le fil

En-tête NavisLamia/rzu commun : `length` (uint32), `id` (uint16), `checksum` (uint8)
= **7 octets** (`rzu`, `MessageBuffer` ; côté serveur `GameCharacterPackets.HeaderSize`).

Charge utile : 28 × uint32 d'après le client 7.3, dont **24 poignées** positionnelles
d'après rzu et NGemity.

| offset absolu | type | nom | valeur observée | source |
|---|---|---|---|---|
| 0 | uint32 | `length` | `0x77` = **119** | client VA 0x48c816 (`movl $0x77,(%esi)`) |
| 4 | uint16 | `id` | `281` (0x119) | client VA 0x48c812 ; `op_codes.md:95` |
| 6 | uint8 | `checksum` | somme des octets 0..5 | client VA 0x48c82b ; en-tête rzu |
| 7 + 4·i, i ∈ [0,23] | uint32 (`ar_handle_t`) | `handle[i]` | `0` = emplacement vide, sinon poignée d'objet | `TS_CS_PUTON_ITEM_SET.h:6-8` (24 · `ar_handle_t`) ; `GameTypes.h:40` (`ar_handle_t` = `uint32_t`) ; client VA 0x48d805 (cible = octet 7 du paquet) |
| 103 + 4·j, j ∈ [0,3] | uint32 | — | copiés par le client, **non décrits par rzu/NGemity** | client VA 0x48d800 (`mov $0x1c,%ecx`) ; absence dans `TS_CS_PUTON_ITEM_SET.h` |

**Tableau à taille fixe, sans champ de comptage.** rzu sérialise un tableau fixe de
primitives par recopie brute, donc uniquement `size × sizeof(T)` octets, sans préfixe
(`librzu/src/lib/Packet/MessageBuffer.h:100`). NGemity fait de même
(`SIZE_F_ARRAY4` / `SERIALIZATION_F_ARRAY4`, `shared/Server/Packets/PacketDeclaration.h:219`
et `:321`). Aucun octet « nombre d'éléments » n'est présent sur le fil.

**Taille totale attendue — deux valeurs, écart à trancher :**

| lecture | taille | pourquoi |
|---|---|---|
| **client 7.3 (autorité)** | **119 octets** (7 + 112 = 7 + 28 × 4) | constructeur VA 0x48c7e0 écrit `0x77` dans `length` ; l'expéditeur copie 28 dwords dans la charge utile |
| rzu / NGemity | 103 octets (7 + 96 = 7 + 24 × 4) | `array (ar_handle_t, handle, 24)` pour `version >= EPIC_4_1` |

C'est **le client qui tranche** : la trame qui arrivera sur la socket fait 119 octets, et
`length` y vaut `0x77`. Une implémentation qui exige exactement 103 octets rejettera toutes
les trames réelles. Recommandation ferme pour `navis-dev` : **accepter toute trame d'au moins
103 octets**, décoder les 24 premières poignées (`offset = 7 + 4·i`, i ∈ [0,23]), ignorer la
fin, et ne jamais produire d'erreur sur la seule base de la taille. Le test d'offsets doit
couvrir 119 octets (voir §5.3).

Le champ 24..27 reste `NON ÉTABLI` : le client copie 28 dwords depuis sa structure interne
(source+0x13), mais rzu et NGemity n'en décrivent que 24, et le client lui-même ne connaît
que **24 emplacements** dans `TS_SC_WEAR_INFO` (202) à 7.3 — son handler boucle 24 fois
(`cmp $0x18`, VA 0x66f9f5) et lit ses tableaux aux offsets de charge utile 4 (`item_code`,
poids 0x60 = 24 × 4), 0x64 (`item_enhance`), 0xc4 (`item_level`) et 0x12b
(`elemental_effect_type`, 24 octets). Les 4 dwords excédentaires sont donc très
probablement des emplacements internes hors port 0..23, mais rien ne le prouve (§7).
Aucune lecture des dwords 24..27 n'est nécessaire pour traiter la requête.

---

## 4. Gating de version

Statut de référence : version 7.3 = `0x070300`.

| champ | gating rzu | décision pour Epic 7.3 |
|---|---|---|
| id du paquet | `281` si `version < EPIC_9_6_3`, `1281` sinon (`TS_CS_PUTON_ITEM_SET.h:11-12`) | **281** : `EPIC_7_3 = 0x070300 < EPIC_9_6_3 = 0x090603` (`PacketEpics.h:59` et `:96`). Le remap 9.6.3 est un remap global des ids de paquets GS — **ne pas** l'appliquer. |
| nombre de poignées | `24` si `version >= EPIC_4_1`, `14` si `version < EPIC_4_1` (`TS_CS_PUTON_ITEM_SET.h:7-8`) | **24** : `EPIC_4_1 = 0x040100` (`PacketEpics.h:50`), donc `0x070300 >= 0x040100`. La variante à 14 poignées (Epic ≤ 4.0) ne s'applique pas. **Nuance client** : la trame 7.3 porte 28 dwords (§3), cf. réserve §7. |
| type des poignées | `ar_handle_t` depuis 2020 (`GameTypes.h:40`, `= uint32_t`) | uint32 non signé ; les poignées d'objet NavisLamia sont déjà `uint` (`GameActionPackets.cs:149-163`), aucun changement de type. |
| champ `position` | **absent** de 281 (contrairement à 200) | ne pas en inventer un : `TS_CS_PUTON_ITEM.h:7-9` place `position` dans 200, `TS_CS_PUTON_ITEM_SET.h:6-8` n'a qu'un tableau. |

Aucun autre gating (`>= EPIC_*`) n'apparaît dans `TS_CS_PUTON_ITEM_SET.h` : rien d'autre à
statuer. Rappel des pièges voisins : le `TM_SC_ITEM_WEAR_INFO` (287) a lui deux gating —
`elemental_effect_type >= EPIC_6_1` et `appearance_code >= EPIC_7_4`
(`rzu/.../TS_SC_ITEM_WEAR_INFO.h:12-13`) : à 7.3 les **deux** sont conclus, le premier
inclus (`0x070300 >= 0x060100`), le second **exclu** (`0x070300 < 0x070400`) — voir §5.

---

## 5. Traitement attendu

### 5.1 Ce que la référence en fait

| référence | traitement de 281 | source |
|---|---|---|
| rzu (`librzu` + `rzgame`) | **aucun** : `TS_CS_PUTON_ITEM_SET.h` n'est inclus ni par le `GameHandler` ni ailleurs ; le seul paquet `*PUTON_ITEM*` dispatché est `TS_CS_PUTON_ITEM` (200) | `rzgame/src/StateHandler/GameHandler/GameHandler.cpp:32` et `:50` |
| NGemity (`Chihiro`) | **aucun** : `TS_CS_PUTON_ITEM_SET` n'a ni `onPutOnItemSet` dans `WorldSession` ni aucune référence hors de `ClientPackets.h:109`, `XPacket.h:132` et de sa propre déclaration | `Chihiro/src/Network/GameNetwork/WorldSession.h:98` (`onPutOnItem` seul) |

Le **modèle sémantique** vient donc du frère 200, traité par les deux références :

- NGemity `Chihiro/src/Network/GameNetwork/WorldSession.cpp:616-651` (`onPutOnItem`) :
  joueur vivant, objet trouvé et `IsWearable()`, objet bien possédé, cible = joueur ou
  invocation légitime ; `unit->Puton((ItemWearType)position, item)` ; si succès
  `CalculateStat()` + `SendStatInfo()` + `SendResult(..., TS_RESULT_SUCCESS, 0)` ; pour un
  joueur, `SendWearInfo()` (202) ; sinon `TS_RESULT_ACCESS_DENIED` / `TS_RESULT_NOT_EXIST`.
- NGemity `shared/Server/Packets/GameClient/TS_CS_PUTON_ITEM.h:7-9` : 200 =
  `int8_t position`, `uint32_t item_handle`, `uint32_t target_handle` — c'est-à-dire
  position **explicite** plus cible.
- NavisLamia fait déjà cela pour 200 : `Game/Services/EquipmentService.cs:28-76`
  (`EquipAsync`) → `SendItemWear` (287) pour l'objet déplacé **et** pour l'objet équipé,
  `SendStatInfo`, `SendResult(TM_CS_PUTON_ITEM, …)`, puis `BuildWearInfo` (202).

Différence structurelle de 281 : il n'y a **pas de `position`** (le port de chaque poignée
doit être déduit de l'objet lui-même) et **pas de `target_handle`** (donc pas d'équipement
d'invocation par ce paquet).

### 5.2 Réponses serveur attendues

1. **Accusé obligatoire** : `TS_SC_RESULT` (id 0) avec `request_msg_id = 281`, `result`,
   `value` — le client 7.3 a un bloc dédié à `request_msg_id == 281` dans son handler de
   `TM_SC_RESULT` (VA 0x66de44, libellé `셋트장착 요청결과`). Sans cet accusé, le client
   n'affiche aucun résultat pour l'action. Format : `rzu/.../TS_SC_RESULT.h:8-10` et
   `ngemity/shared/Server/Packets/GameClient/TS_SC_RESULT.h:7-11` concordent
   (`uint16 request_msg_id`, `uint16 result`, `int32 value`) — c'est exactement ce que
   produit `GameClient.SendResult(ushort id, ushort result, int value)`
   (`Game/Network/Clients/GameClient.cs:56`).
2. **Un `TM_SC_ITEM_WEAR_INFO` (287) par objet effectivement équipé ou déplacé**, avec les
   champs 7.3 : `item_handle` (uint32), `wear_position` (int16), `target_handle` (uint32),
   `enhance` (int32), `elemental_effect_type` (uint8) = **15 octets de charge utile,
   22 octets au total** ; `appearance_code` (int32, `>= EPIC_7_4`) est **absent** à 7.3.
   Sources : `rzu/.../TS_SC_ITEM_WEAR_INFO.h:7-13` ; `ngemity/shared/Server/Packets/GameClient/TS_SC_ITEM_WEAR_INFO.h:7-12` ;
   confirmation client : le handler de 287 (`SFrame.exe` VA 0x66fe20) lit la charge utile en
   +7 (dword), +0xb (word), +0xd (dword) et +0x15 (octet), soit les offsets de charge utile
   0, 4, 6 et **14** — la trame s'arrête donc à 15 octets de charge utile à 7.3.
   Côté NavisLamia, `GameCharacterPackets.BuildItemWearInfo`
   (`Game/Network/Packets/Game/GameCharacterPackets.cs:76-88`) est déjà conforme : à
   réutiliser tel quel.
3. **Un `TM_SC_WEAR_INFO` (202)** en fin de traitement, comme pour 200 : c'est le
   rafraîchissement complet des emplacements, déjà produit par `BuildWearInfo`
   (`GameCharacterPackets.cs:92-…`, `WearSlots = 24` en `:20`). Cohérent avec le client
   (`cmp $0x18` = 24 emplacements, VA 0x66f9f5) et avec rzu
   (`TS_SC_WEAR_INFO.h:22`, 24 pour `>= EPIC_4_1 && < EPIC_9_5`).
4. `SendStatInfo` après toute modification d'équipement, comme pour 200
   (`EquipmentService.cs:60-62`).

### 5.3 Plan d'implémentation imposé

1. **Enum** : `TM_CS_PUTON_ITEM_SET = 281` dans `GamePackets`
   (`Game/Network/Packets/Enums/GamePackets.cs`, à côté de `TM_CS_PUTON_ITEM = 200` en `:26`).
   Aucun `TM_SC_*` nouveau : 0, 202 et 287 existent déjà (`:9`, `:29`, `:30`).
2. **Lecture** : `GameActionPackets.TryReadPutonItemSet` calquant
   `TryReadPutonItem` (`GameActionPackets.cs:149-163`) : refuser si
   `packet.Length < HeaderSize + 96` (= 103), lire 24 `uint32` aux offsets
   `HeaderSize + 4*i`, i ∈ [0,23], tolérer tout octet excédentaire (la trame client en
   porte 16). Aucune lecture au-delà de `HeaderSize + 96`.
3. **Dispatch** : armer l'id dans la chaîne de `if` de `GameClient`
   (`Game/Network/Clients/GameClient.cs:660-663` pour 200, modèle à copier) **avant** le
   `switch` final de `:791`, qui lève `Exception("Unknown Packet Type")` : aucun membre de
   `GamePackets` ne doit pouvoir l'atteindre (critère transverse n° 4). Trame refusée →
   `SendResult((ushort)GamePackets.TM_CS_PUTON_ITEM_SET, (ushort)ResultCode.InvalidArgument)`.
4. **Traitement** : itérer les 24 poignées. Poignée `0` = emplacement vide → **ignorer
   silencieusement**. Poignée non nulle → même chemin que 200, en réutilisant
   `ICharacterService.EquipItemAsync` (`EquipmentService.cs:44-46`) : le port de destination
   vient de l'objet, pas de l'index du tableau (§7 : c'est la lecture qui reste correcte sous
   les deux hypothèses de §2). Un échec sur une poignée ne doit pas interrompre les
   suivantes (le joueur demande un set entier, pas une pièce).
5. **Résultat agrégé** : un **seul** `TS_SC_RESULT` portant `request_msg_id = 281` en fin de
   boucle. Convention proposée (non établie par les références, cf. §7) :
   `Success` si au moins une pièce a été équipée, sinon le premier code d'erreur rencontré
   (typiquement `AccessDenied` pour un objet non équipable/absent, `NotActable` pour un
   objet déjà porté). Ne pas envoyer un accusé par poignée : le client n'a qu'un bloc pour
   l'id 281.
6. **Tests d'offsets obligatoires** (`Tests/`, discipline du dépôt) : taille totale **119**
   pour une trame client complète et 103 comme minimum accepté ; `length` = 0x77 ;
   `id` = 281 en +4 ; checksum en +6 ; `handle[0]` en +7 ; `handle[1]` en +11 ;
   `handle[23]` en +99 ; octets 103..118 ignorés sans erreur ; trame tronquée à 102 octets
   refusée avec `InvalidArgument`. Les 448 tests existants doivent rester verts (aucun ne
   peut disparaître).

---

## 6. Écarts assumés avec NGemity

| écart | raison |
|---|---|
| NGemity ne traite pas 281 du tout ; NavisLamia l'implémente quand même | NGemity est en avance de version pour le *code* mais en retard de couverture paquets : `TS_CS_PUTON_ITEM_SET` n'y a jamais été branché (aucune référence hors déclaration). Le client 7.3, lui, émet 281 (constructeur VA 0x48c7e0). Le paquet doit être traité, sinon échec silencieux côté joueur. |
| NGemity répond par un `TS_SC_RESULT` *par item* (200) ; NavisLamia agrégera en un seul accusé | 281 est une requête unique : le client n'a qu'un bloc de journalisation pour l'id 281. Un accusé par poignée produirait 24 lignes d'erreur pour une seule action. |
| NGemity s'appuie sur `Puton((ItemWearType)position, item)` avec la position reçue ; NavisLamia déduit la position de l'objet | 281 ne transporte **aucune** position (`TS_CS_PUTON_ITEM_SET.h:6-8`). |
| NGemity conserve le `target_handle` (invocation) ; NavisLamia n'en a pas pour 281 | Le champ n'existe pas dans 281 : l'équipement d'invocation devra passer par 200. C'est déjà la situation de NavisLamia (`EquipmentService.cs:32-37` valide la cible pour 200). |
| rzu/NGemity : 24 poignées (103 o) ; NavisLamia acceptera 119 o | Le constructeur client 7.3 écrit `length = 0x77` et copie 28 dwords. Le client de référence tranche : voir §3 et §7. |
| rzu inclut `TS_CS_PUTON_ITEM` (200) et pas 281 dans son `GameHandler` ; NavisLamia traite les deux | rzu est inachevé côté serveur (aucun gestionnaire d'équipement complet) et n'est pas une base de conception ici, seulement un témoin de format. |

---

## 7. `NON ÉTABLI`

1. **Les 4 dwords excédentaires (octets 103..118).** Le client 7.3 en copie 28
   (`mov $0x1c,%ecx ; rep movsl`, VA 0x48d800) alors que rzu et NGemity n'en décrivent que
   24. Hypothèse la plus probable : des emplacements internes du client hors des ports
   0..23 (le paquet 202 n'en connaît que 24 à 7.3). Question précise à trancher : *le
   serveur officiel 7.3 lisait-il 24 ou 28 poignées sur ce paquet ?* Impact : aucun si le
   serveur se contente des 24 premières et ignore la fin (plan §5.3) ; mais si les dwords
   24..27 sont des emplacements réels (ex. emplacements de familier/invocation), les ignorer
   laisse une partie de la requête sans effet. À vérifier par Killian avec un client réel
   (chat 281 observé) ou par une capture réseau d'un serveur officiel.
2. **Le geste exact côté joueur** (§2) : « configuration d'équipement sauvegardée » contre
   « toutes les pièces d'un set d'objet ». Le libellé client `셋트장착` et la forme
   positionnelle à 24 entrées plaident pour la première, sans preuve directe.
3. **Sémantique de `handle[i]` : index d'emplacement ou simple liste ?** Le tableau fait
   exactement 24 entrées, comme le nombre de ports de 202 à 7.3 (`WearSlots = 24`,
   `GameCharacterPackets.cs:20`), ce qui suggère *index = port*. Mais rien n'interdit une
   liste de poignées où chaque objet porte son propre port. Le plan §5.3 est écrit pour être
   correct **sous les deux hypothèses** (le port vient de l'objet). Si Killian confirme la
   lecture positionnelle, on pourra renvoyer un `InvalidArgument` quand une poignée est
   placée dans un port incompatible.
4. **Est-ce que 281 remplace l'équipement en place ?** Aucune référence ne dit si le serveur
   doit au préalable déshabiller les ports absents du tableau (poignée `0` = « laisser tel
   quel » ou « retirer » ?). Recommandation : traiter `0` comme « ne rien faire », donc ne
   jamais déshabiller — c'est le comportement conservateur, réversible, et cohérent avec
   200 qui n'agit que sur un objet. À confirmer.
5. **Convention du résultat agrégé** (§5.3 point 5) : ni rzu ni NGemity ne traitent 281,
   donc le code exact (`Success` partiel, `NotActable`, `AccessDenied`) n'est établi par
   aucune source. La convention proposée est un choix d'ingénierie, réversible, à valider.
6. **La divergence 24 (rzu, NGemity, 202) contre 28 (trame 281 du client).** Aucune des deux
   références n'explique ce nombre ; aucun serveur officiel 7.3 n'est joignable depuis cet
   environnement pour lever le doute. Rappel : `cmp $0x18` (24) dans le handler client de
   202 (VA 0x66f9f5) contre `mov $0x1c,%ecx` (28) dans l'expéditeur de 281 (VA 0x48d800).

Aucun champ de §3 n'est deviné : ce qui n'était pas sourçable est listé ci-dessus et le
serveur est conçu pour y survivre.

---

## 8. Commits épinglés

| dépôt | commit | rôle |
|---|---|---|
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02) | HEAD au moment de la lecture |
| `reference/rzu` | `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07, « Adjust count management for packets and add all known GS packets as of 9.4 ») | introduction de `TS_CS_PUTON_ITEM_SET.h` avec `array (uint32_t, handle, 24)` et l'id 281 |
| `reference/rzu` | `94885467536899bd53031f190d4488fe9e05c70a` (2017-03-03, « Update packets with old clients tests ») | ajout de la variante `14` poignées pour `version < EPIC_4_1` (gating §4) |
| `reference/rzu` | `05bc2d82dac16003bc06162584e8559826310bff` (2020-03-29) | passage des poignées à `ar_handle_t` (= `uint32_t`) |
| `reference/rzu` | `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11) | remap d'id versionné : 281 → 1281 à `EPIC_9_6_3` |
| `reference/ngemity` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03) | HEAD au moment de la lecture |
| `reference/ngemity` | `90a500d46acc39d21ef943a047a37d0a641f7af4` et `44b7d25dac7a1f869490dc8b6ea3255ea68b8eb4` (2018-08) | introduction de `shared/Server/Packets/GameClient/TS_CS_PUTON_ITEM_SET.h` (24 × `uint32_t`) |
| `reference/ngemity` | `8f5e80fa1b7d312b5c33882ad3480a94aeb0716d` / `e1136b84f8f67a417e64715b7c3cebccd4cf3dcf` (2020-04-27) | relectures de forme (clang-format), sans changement de sémantique |
| `reference/client73` | `SFrame.exe` sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | lecture statique : constructeur 281, expéditeur, tables de dispatch, handlers 0 / 202 / 287 |
| `reference/client73` | `db_string.rdb` sha256 `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` | recherche d'un libellé d'interface « set » : **aucun** (le libellé `셋트장착` n'existe que dans `SFrame.exe`) |
| dépôt `Navislamia` | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` | `master` au moment de la rédaction |

Baseline mesurée sur cette base (branché avant toute modification) :
`dotnet build Navislamia.sln -c Debug` → 0 erreur, 160 avertissements ;
`dotnet test Tests/Tests.csproj` → 448 réussis / 448, 0 échec.
