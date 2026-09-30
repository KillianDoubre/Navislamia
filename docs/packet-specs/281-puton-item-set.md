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

---

## 9. Bloc pour CLAUDE.md (à recopier dans la description de la MR)

```markdown
- **`TM_CS_PUTON_ITEM_SET` (281) est implémenté** : trame à tableau fixe, **sans `position` et sans
  `target_handle`**. Le client 7.3 en construit **119 octets** (`length = 0x77`, 28 dwords copiés) alors
  que rzu et NGemity n'en décrivent que 24 : le serveur accepte toute trame d'**au moins 103 octets**,
  lit les 24 poignées `uint32` à `7 + 4·i` (`i ∈ [0,23]`), ignore la fin et ne refuse jamais sur la
  seule taille. `GameActionPackets.PutonItemSetHandles = 24`.
- **Le port de destination vient de l'objet, jamais de l'index du tableau** : `ItemEntity.WearInfo` vaut
  `None` pour un objet d'inventaire, donc le port est lu dans la colonne `wear_type` de la ressource
  (`ItemResource.WearType`, catalogue figé `ItemWearCatalog` construit au démarrage, projeté par
  `IItemResourceRepository.GetWearFields()`). Deux types ne sont pas des ports et sont repliés comme
  NGemity les replie (`Player::TranslateWearPosition`, `Player.cpp:1754-1758`) : `Twohand` (99) →
  `Weapon` (0) et `TwofingerRing` (94) → `Ring` (9). Les autres (`CantWear`, les emplacements
  « spare » 24..27, `Skill` 100, `SummonOnly` 200) n'ont pas de port unique et la poignée est refusée
  en `InvalidArgument` — **le rang de la poignée n'est jamais utilisé comme repli** (fiche §11, réserve 1).
- **Sémantique** : poignée `0` = emplacement vide, laissé tel quel (281 ne déshabille jamais) ; une
  poignée qui échoue n'interrompt pas les suivantes. Réponses : **un seul** `TS_SC_RESULT`
  (`request_msg_id = 281`) en fin de traitement — `Success` dès qu'une pièce a été équipée, sinon le
  premier refus rencontré (`AccessDenied` pour un objet non possédé, `NotActable` pour un objet déjà
  porté, `InvalidArgument` pour un port irrésoluble) ; une trame sans aucune poignée exploitable est
  refusée en `InvalidArgument`. Un `TM_SC_ITEM_WEAR_INFO` (287) par objet déplacé, puis `SendStatInfo`
  une fois, `TS_SC_RESULT`, puis `TM_SC_WEAR_INFO` (202) une fois — la même queue que 200.
- **Invariant** : `TM_CS_PUTON_ITEM_SET = 281` est armé dans la chaîne de réception de `GameClient`
  (juste après `TM_CS_PUTOFF_ITEM`) : il ne peut plus atteindre le `switch` final
  (`Unknown Packet Type`). Aucun `TM_SC_*` nouveau.
- **Pièges et réserves** : `docs/packet-specs/281-puton-item-set.md` §7 (héritées) et §11 (ajoutées par
  le dev : deux bagues `wear_type = 9`, dwords 24..27 ignorés, convention d'accusé, règles de
  chevauchement NGemity non portées).
```

## 10. Implémentation (navis-dev, 2026-09-19)

Branche `hermes/packet-281-puton-item-set`, base `master`
`ec76b218cd0bd7c6498d725f253abb8b431f0cd6`, fiche `ed94da1`. Le plan §5.3 est suivi point par point ;
les seules décisions laissées ouvertes par la fiche sont tranchées et listées en §11.

### 10.1 Fichiers touchés

| Fichier | Rôle |
| --- | --- |
| `Game/Network/Packets/Enums/GamePackets.cs:28` | `TM_CS_PUTON_ITEM_SET = 281`, entre `TM_CS_PUTOFF_ITEM = 201` et `TM_SC_WEAR_INFO = 202` |
| `Game/Network/Packets/Game/GameActionPackets.cs:14`, `:176` | `PutonItemSetHandles = 24` et `TryReadPutonItemSet(ReadOnlySpan<byte>, out uint[])` : `packet.Length < HeaderSize + 96` (= 103) refusé, 24 `uint32` lus à `HeaderSize + 4·i`, rien au-delà |
| `Game/Network/Clients/GameClient.cs:374`, `:690` | `HandlePutonItemSetAsync` (`InvalidArgument` si la trame est refusée par la lecture) et le bras de réception, armé juste après `TM_CS_PUTOFF_ITEM` |
| `Game/Services/Interfaces/IEquipmentService.cs` | `Task EquipSetAsync(GameClient client, uint[] handles)` |
| `Game/Services/EquipmentService.cs:67`, `:170`, `:192` | `EquipSetAsync` (boucle, agrégation, queue d'envois), `ResolveSlotAsync`, `EquipAtSlotAsync` (cœur partagé avec 200 : 287 de l'objet déplacé puis de l'objet équipé) |
| `Game/Services/ItemWearRules.cs` | règles pures : `IsWearableSlot` (0..23, `WearSlots` de 202) et `TryResolveSlot` (replis `Twohand`/`TwofingerRing`) |
| `Game/Services/Interfaces/IItemWearCatalog.cs`, `Game/Services/ItemWearCatalog.cs` | `TryGetWearType(long resourceId, out ItemWearType)` sur un dictionnaire figé, forme de `ItemUseCatalog`/`ItemGroupCatalog` |
| `Game/DataAccess/Repositories/Interfaces/IItemResourceRepository.cs:27`, `ItemResourceRepository.cs:52` | `ItemWearFields(int Id, ItemWearType WearType)` et `GetWearFields()` — projection `ItemResources.Id/WearType` |
| `DevConsole/Program.cs:239` | `services.AddSingleton<IItemWearCatalog, ItemWearCatalog>();` |
| `Tests/Game/PutonItemSetPacketsTests.cs`, `Tests/Game/ItemWearTests.cs` | 19 tests : offsets du 281 et lecture des poignées (9), catalogue et règles de port (10) |

`EquipAsync` (200) n'est pas modifié sur le fil : son ordre d'envois (`287` déplacé, `287` équipé,
`SendStatInfo`, `Result`, `202`) est reproduit à l'identique, le cœur d'équipement étant seulement
extrait dans `EquipAtSlotAsync` et les codes de refus inchangés.

### 10.2 D'où vient le port : la ressource d'objet, avec les deux replis de NGemity

281 ne transporte aucun port (§4) et un objet d'inventaire porte `WearInfo = ItemWearType.None`
(`CharacterService.EquipItemAsync` exige `WearInfo == None` pour équiper) : le seul port que le dépôt
puisse déduire est la colonne `wear_type` de la ressource, absente jusqu'ici des projections
d'`IItemResourceRepository`. `GetWearFields()` l'ajoute sous la même forme que `GetUseFields()`, et
`ItemWearCatalog` la fige au démarrage (`FrozenDictionary<int, ItemWearType>`), comme les autres
catalogues d'objets.

`ItemWearRules.TryResolveSlot` couvre trois cas :

| `wear_type` de la ressource | port | fondement |
| --- | --- | --- |
| 0..23 (`Weapon` … `BagSlot`) | lui-même | c'est déjà un port de `TM_SC_WEAR_INFO` (202, `WearSlots = 24`) |
| `Twohand` (99) | `Weapon` (0) | `Player::TranslateWearPosition` (`Player.cpp:1754-1755`) écrit `WEAR_WEAPON` pour tout objet dont `GetWearType() == WEAR_TWOHAND` ; 99 n'est jamais une position : les contrôles de port bornent à `MAX_ITEM_WEAR = 24` (`Player.cpp:1853`) et `Unit::putonItem` borne à `MAX_SPARE_ITEM_WEAR = 28` (`Unit.cpp:1491`), alors que `m_anWear` ne compte que 24 entrées (`Unit.h:584`) |
| `TwofingerRing` (94) | `Ring` (9) | même fonction, `Player.cpp:1757-1758` (`pos = WEAR_RING`), et `Unit::putoffItem` replie `WEAR_TWOFINGER_RING` sur `WEAR_RING` de la même façon (`Unit.cpp:1515-1516`) |

Tout le reste est refusé pour la poignée concernée : `CantWear` (= `None` = -1), les emplacements
« spare » 24..27 (NGemity les tolère comme positions dans `Player::TranslateWearPosition`,
`Player.cpp:1628`, mais `m_anWear` n'a que `MAX_ITEM_WEAR = 24` entrées et 202 ne rapporte que 24
ports), `Skill` (100) et `SummonOnly` (200). Le rang `i` de la poignée dans la trame n'est **jamais**
utilisé comme port de repli : c'est la lecture que §7.3 laisse ouverte et le plan §5.3 l'interdit.

Un objet inconnu du personnage répond `AccessDenied`, exactement le code que 200 renvoie pour
`EquipItemOutcome.NotFound`, et un objet déjà porté répond `NotActable` (`EquipItemOutcome.AlreadyWorn`
sous 200) : les refus de 281 restent alignés sur ceux du frère 200 au lieu d'inventer une table.

### 10.3 Convention de l'accusé agrégé

Le plan §5.3-5 est appliqué à la lettre : **un seul** `TS_SC_RESULT` avec `request_msg_id = 281`, émis
après la boucle. `Success` dès qu'au moins une pièce a été équipée (même si d'autres poignées ont
échoué), sinon le **premier** refus rencontré dans l'ordre de la trame. Deux sous-cas que la fiche ne
tranche pas et qui sont donc explicitement choisis ici (réserve 3 de §11) :

- **trame sans aucune poignée exploitable** (les 24 dwords à zéro, cas non décrit par §7) : la réponse
  est `InvalidArgument`, le code que reçoit déjà une trame tronquée (§5.3-3). Répondre `Success` serait
  annoncer un équipement qui n'a pas eu lieu ; ne rien répondre violerait l'accusé obligatoire §5.2-1.
- **exception du chemin** (lecture de l'objet, écriture en base) : la poignée compte comme `DBError`,
  la boucle continue, et `DBError` remonte comme premier refus si rien n'a été équipé. Aucune exception
  n'est laissée remonter à la boucle de réception, comme pour 200.

Queue d'envois en cas de succès partiel : `287` (objet déplacé puis objet équipé) par pièce équipée,
puis `SendStatInfo` **une fois** (les blocs `Total`/`ByItem` sont des instantanés absolus, §5.2-4 est
satisfait sans les répéter 24 fois), `TS_SC_RESULT`, puis `BuildWearInfo` (202) **une fois** (§5.2-3),
dans l'ordre de 200.

### 10.4 Invariant énumération / dispatch

`TM_CS_PUTON_ITEM_SET` (281) est un paquet **montant** : son bras existe dans la chaîne de `if` de
`GameClient` (`:690`, juste après `TM_CS_PUTOFF_ITEM`) et il ne peut donc plus atteindre le `switch`
final de `:826` (`Unknown Packet Type`). Aucun `TM_SC_*` n'est ajouté : `TS_SC_RESULT` (0),
`TM_SC_ITEM_WEAR_INFO` (287) et `TM_SC_WEAR_INFO` (202) existaient déjà sur `master`.

### 10.5 Vérifications exécutées

| Commande | Résultat |
| --- | --- |
| `dotnet build Navislamia.sln -c Debug` | code de sortie **0**, 0 erreur, 160 avertissements (baseline identique) |
| `dotnet test Tests/Tests.csproj` | code de sortie **0**, **467 réussis / 467**, 0 échec (448 avant cette tâche) |
| `git log --oneline origin/master..master` | vide |
| `git branch --show-current` | `hermes/packet-281-puton-item-set` |

Tests d'offsets (`Tests/Game/PutonItemSetPacketsTests.cs`, 9 tests) : id 281 dans l'énumération ; trame
client de 119 octets (`length = 0x77`, id 281 en +4, checksum en +6) lue en 24 poignées ; `handle[0]` en
+7, `handle[1]` en +11, `handle[23]` en +99 (un test vérifie les 24 pas de 4 octets) ; l'octet +6 est
l'en-tête et non une poignée ; poignée `0` lue comme emplacement vide ; les 4 dwords 103..118 ignorés
(remplis de `0xAB`, aucune poignée polluée) ; minimum de 103 octets accepté ; 102 octets et trame
d'en-tête seul refusés (`TryReadPutonItemSet` renvoie `false` — c'est `HandlePutonItemSetAsync` qui
envoie alors `InvalidArgument`, hors de portée d'un test unitaire sans socket).

Tests de port (`Tests/Game/ItemWearTests.cs`, 10 tests) : le catalogue expose le `wear_type` d'une
ressource connue et laisse passer une ressource inconnue en `false` ; `IsWearableSlot` aux bornes
(-1/0/23/24/94/99) pour les deux surcharges ; `TryResolveSlot` identité sur 0..23, replis 99→0 et 94→9,
refus pour les spare 24..27, `Skill`, `SummonOnly` et `CantWear`.

Aucun test n'exerce `EquipSetAsync` : comme les autres services qui prennent un `GameClient`, il n'est
pas testable sans socket dans `Tests/` (aucun test existant ne le fait pour 200), et la couche testée
est la lecture de trame, le catalogue et les règles pures.

## 11. A VERIFIER PAR KILLIAN (ajouts du dev)

1. **Deux bagues dans une même trame** (le cas le plus visible) : une bague normale a
   `wear_type = 9` (`Ring`), donc les deux bagues d'une panoplie se résolvent sur le **même** port 9 ;
   la seconde équipée déplace la première, qui repasse en inventaire. NGemity connaît le port 10
   (`WEAR_SECOND_RING`) mais sa règle dépend de ce qui est déjà porté
   (`Player.cpp:1759-1760` : « si le port 9 est occupé par une bague qui n'est pas une bague à deux
   doigts, aller en 10 ») : appliquée à une trame qui rééquipe les deux anneaux d'un coup, elle
   enverrait la première bague en 10 puis la seconde en 10 à son tour (10 occupé par la première, 9
   occupé par l'ancienne), et déplacerait la mauvaise pièce. Le port de la seconde bague n'est
   distinguable que par le **rang** de la poignée dans la trame : c'est précisément l'hypothèse 1 de
   §7.3. Ce qu'il faut trancher : *le client envoie-t-il deux bagues dans deux cases différentes du
   tableau ?* Si oui, le correctif est court et localisé (`ResolveSlotAsync` : utiliser `i` pour les
   types à port ambigu, ici `Ring`/`SecondRing`) — il n'est pas appliqué ici parce que §5.3-4 impose le
   port par l'objet et que le deviner serait choisir une lecture non établie.
2. **Les 4 dwords 103..118** (§7.1 et §7.6) : ignorés, comme le plan §5.3-2 l'impose. Si l'on apprenait
   qu'il s'agit d'emplacements réels (familier, invocation, arme de rechange), une partie de la requête
   resterait sans effet. Les `wear_type` « spare » 24..27 sont refusés pour la même raison : 202 ne
   compte que 24 ports.
3. **Convention d'accusé** (§7.5) : un seul `TS_SC_RESULT` (281) par trame, `Success` dès qu'une pièce
   est équipée, sinon premier refus ; `AccessDenied` / `NotActable` / `InvalidArgument` alignés sur 200 ;
   trame sans poignée exploitable → `InvalidArgument`. Aucune source officielle ne décrit ce code.
4. **Règles de chevauchement NGemity non portées** : NGemity déshabille plusieurs ports à la fois pour
   un objet à deux mains (main gauche, bouclier déco) ou une bague à deux doigts (seconde bague), via
   `vOverlappedItemList` (`Player.cpp:1859-1888`). Le chemin livré ne déplace que le port visé, comme le
   fait déjà 200 dans ce dépôt : une panoplie contenant une arme à deux mains **laisse le bouclier en
   place** et 202 rapportera les deux. Élargir ces règles dépasserait 281 (elles manquent à 200 aussi).
5. **`GetWearFields()` n'a pas pu être exécuté** : aucun PostgreSQL n'est disponible dans
   l'environnement de développement, la projection suit donc la forme des projections voisines
   (`GetUseFields`, `GetGroupFields`) sans preuve d'exécution. Deux conséquences à vérifier au premier
   démarrage réel : la colonne s'appelle bien `WearType` (`ItemResource` d'Arcadia, migration
   `20231213174355_Version0001_TheBeginning.cs:104`) et, si la table `ItemResources` était vide, tout
   281 serait refusé en `InvalidArgument` (aucun risque de corruption : la poignée échoue seule).
6. **§7.2 (le geste du joueur) n'a pas d'effet sur l'implémentation** : que 281 soit « configuration
   sauvegardée » ou « toutes les pièces d'un set », le serveur applique le tableau reçu, `0` laissant
   l'emplacement tel quel (§7.4).
