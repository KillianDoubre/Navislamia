# 223 — TM_CS_SWAP_EQUIP

## 1. Identité

| Élément | Valeur |
|---|---|
| id décimal | **223** (`0xDF`) |
| nom | `TM_CS_SWAP_EQUIP` |
| sens | client → serveur (`SessionType::GameClient`, `SessionPacketOrigin::Client`) |
| source de l'id et du nom | `op_codes.md:72` — `[223] = "TM_CS_SWAP_EQUIP"` |
| précédent / suivant immédiats | `221 TM_CS_HIDE_EQUIP_INFO`, `222 TM_SC_HIDE_EQUIP_INFO`, `224 TM_SC_SKIN_INFO` (`op_codes.md:70-73`) |
| état dans Navislamia | **absent** de l'enum : `GamePackets.cs:39-40` saute de `TM_SC_HIDE_EQUIP_INFO = 222` à `TM_SC_SKIN_INFO = 224`. Rien d'autre dans le dépôt ne mentionne 223 (`grep -rn "SWAP_EQUIP" --include=*.cs .` → aucun résultat). |

Convention de nommage : le dépôt nomme les membres d'enum `TM_*` (nomenclature
`op_codes.md`), les références externes les nomment `TS_CS_*`. Même paquet.

## 2. Ce que le joueur fait pour que le client l'envoie

**NON ÉTABLI.** Aucune trace d'un tel envoi dans le client 7.3 examiné :

- aucune chaîne `SWAP_EQUIP` / `SwapEquip` dans `SFrame.exe` ni dans `data.000`
  (relevé `strings -n 4 SFrame.exe | grep -i swap` → 6 occurrences, toutes sans
  rapport : `PNG_WRITE_SWAP_SUPPORTED`, `UCS-2-SWAPPED`, `UCS-4-SWAPPED`,
  `ERROR_SWAPERROR`, `INHIBIT SYMMETRIC SWAPPING`,
  `ACTIVATE SYMMETRIC SWAPPING`) ;
- aucun identifiant d'UI, de message ou de classe (`SUIEquipmentWnd`,
  `SUIPopupEquip`, `SUIJewelEquipWnd`, `SUIEquipCreatureWnd`,
  `AUSIMSG_HIDE_EQUIP_INFO`, `MSG_EQUIP_SUMMON` existent ; rien d'équivalent
  pour un échange d'équipement) ;
- le seul « swap » du client concerne la barre de raccourcis :
  `db_string.rdb` → `Swap Hotbar`, `Swap Hotbar Key` (aucun rapport avec
  l'équipement).

Le paquet ne doit donc pas être implémenté à partir d'une intention supposée :
voir sections 5 et 7.

## 3. Structure sur le fil

Taille totale attendue : **7 octets** (en-tête seul, aucune charge utile).

| offset | taille | type | nom | valeur observée | source |
|---|---|---|---|---|---|
| 0 | 4 | `uint` little-endian | `Length` | 7 | `Game/Network/Packets/Header.cs:9,22` ; `CLAUDE.md:40` |
| 4 | 2 | `ushort` little-endian | `ID` | 223 | `Game/Network/Packets/Header.cs:10,23` ; `op_codes.md:72` |
| 6 | 1 | `byte` | `Checksum` | somme des 6 premiers octets | `Game/Network/Packets/Header.cs:11,24` ; `CLAUDE.md:40-41` |
| 7 | 0 | — | *(aucun champ)* | — | `rzu …/TS_CS_SWAP_EQUIP.h:7` (macro `_DEF(_)` vide) ; `ngemity …/TS_CS_SWAP_EQUIP.h:6` (macro `_DEF(_)` vide) |

Aucun champ n'est déclaré par les deux références, et la taille du paquet se
réduit donc à l'en-tête. Contrôle de cohérence : c'est exactement la convention
des paquets sans charge utile déjà connus du dépôt — `TM_CS_RETURN_LOBBY (23)`,
`TM_CS_REQUEST_RETURN_LOBBY (25)`, `TM_CS_LOGOUT (27)` font 7 octets
(`CLAUDE.md:44-48`), et les deux références déclarent leurs corps vides de la
même façon (`rzu …/TS_CS_RETURN_LOBBY.h:7`, `ngemity …/TS_CS_LOGOUT.h:6`).

Force de cette conclusion (à lire avant d'implémenter) :

- Les deux références **concordent**, mais elles partagent visiblement une même
  origine (mêmes macros `_(impl)(array)(…)`, même commentaire
  `// Since EPIC_7_2`), donc elles se corroborent plus qu'elles ne se
  confirment indépendamment.
- `rzu` ne porte **pas** la ligne `// Last tested: …` sur ce fichier, alors
  qu'il l'écrit sur ses paquets vérifiés (`TS_CS_RETURN_LOBBY.h:5`,
  `TS_SC_HIDE_EQUIP_INFO.h:5`) : le corps vide n'a jamais été testé par rzu.
  À l'inverse, rzu est précis sur le voisin `TS_SC_HIDE_EQUIP_INFO (222)`
  (`ar_handle_t handle` + `uint32_t nHideEquipFlag` = 8 octets de charge utile),
  ce que confirme le code du dépôt (`GameCharacterPackets.cs:45-53`,
  `HeaderSize + 8`).
- Aucune confirmation côté client : le client 7.3 ne nomme pas ce paquet
  (voir section 7, point 2, et l'annexe pour la méthode).

En conséquence : **7 octets** est la valeur la mieux étayée, elle n'est pas
confirmée par le client. Un `Length` différent de 7 (ou une charge utile lue à
partir de l'offset 7) serait une invention.

## 4. Gating de version

`rzu` porte un gating d'id explicite (`rzu …/TS_CS_SWAP_EQUIP.h:9-12`) :

```
// Since EPIC_7_2
#define TS_CS_SWAP_EQUIP_ID(X) \
    X(223, version < EPIC_9_6_3) \
    X(1223, version >= EPIC_9_6_3)
```

Décision pour Epic 7.3 : **223**. La borne `EPIC_9_6_3` est postérieure à 7.3,
donc la branche `1223` ne concerne pas ce serveur et ne doit pas être câblée.
`ngemity` fige d'ailleurs `223` (`…/TS_CS_SWAP_EQUIP.h:9`), et `op_codes.md:72`
(la table du projet) dit la même chose : trois sources alignées.

Aucun autre gating à statuer : le paquet n'a aucun champ, donc aucun
`>= EPIC_*` de champ (ni le piège `EPIC_9_6_7` de réordonnancement des champs
que porte par exemple `TS_CS_PUTON_ITEM.h:8-12`).

## 5. Traitement attendu

Ce que fait `Chihiro` (NGemity, commit `38ceb2c`) :

- le paquet est **déclaré** (`shared/Server/ClientPackets.h:88`,
  `shared/Server/Packets/GameClient/TS_CS_SWAP_EQUIP.h:9`) ;
- il n'a **aucun handler** : `WorldSession.h:59-122` n'expose pas de
  `onSwapEquip`, et `worldPacketHandler[]` (`WorldSession.cpp:92-137`) ne
  contient aucune entrée pour cet id ;
- il n'est **pas** dans la liste `ignoredPackets[]`
  (`WorldSession.cpp:140-142`), donc s'il arrivait, la boucle
  `ProcessIncoming` (`WorldSession.cpp:151-164`) journaliserait
  `Got unknown packet '223'` en debug puis rendrait `ReadDataHandlerResult::Ok`
  — **aucune réponse, pas de déconnexion** ;
- aucune écriture serveur de `TS_SC_*` en lien : `grep -rn "SWAP_EQUIP"` sur
  tout le dépôt NGemity hors `.git` ne renvoie que ces déclarations.

Ce que le serveur Navislamia doit faire :

1. **Ajouter le membre d'enum** `TM_CS_SWAP_EQUIP = 223` à `GamePackets`
   (`Game/Network/Packets/Enums/GamePackets.cs`, entre `222` et `224`).
2. **Ajouter la branche correspondante dans la même modification.** Le dispatch
   des paquets de jeu est un enchaînement de `if` dans `GameClient` terminé par
   `IPacket msg = header.ID switch { … _ => throw new Exception("Unknown Packet Type") }`
   (`Game/Network/Clients/GameClient.cs:670-682`). Un membre d'enum sans branche
   fait donc **lever une exception dans la boucle de lecture**. Le paquet étendu
   doit être consommé avant ce `switch`, sur le modèle de `TM_CS_LOGOUT`
   (`GameClient.cs:664-668`) : journaliser puis `continue`. Attention : le
   registre `GameActions._actions` (`Game/Network/Clients/Actions/GameActions.cs:32,43-50`)
   ne concerne que les paquets de connexion/lobby et exige un
   `Packet<T>` désérialisé par le `switch` ; ce n'est pas l'endroit où poser un
   bras sans charge utile.
3. **Ne rien répondre.** Aucune des deux références ne répond à 223, et le
   client 7.3 n'a ni nom ni handler pour cet id : un
   `TS_SC_RESULT` tagué 223 serait une invention. Si Killian veut malgré tout
   une trace de résultat, la question est ouverte en section 7 (point 4) et doit
   être tranchée par lui, pas devinée.
4. **Test d'offsets obligatoire** (critère d'acceptation 3), sur le modèle de
   `Tests/Game/ActionPacketsTests.cs:203-214` (`Packet<TS_SC_RESULT>`) :
   `Length == 7` aux offsets 0-3, `ID == 223` aux offsets 4-5,
   `Checksum == somme des octets 0..5` à l'offset 6, **et aucune donnée au-delà
   de l'offset 6**. Un second test doit vérifier que 223 est consommé par le
   dispatch sans lever (`Assert.DoesNotThrow`/absence de déconnexion).

## 6. Écarts assumés avec NGemity

| Sujet | NGemity | Navislamia (à faire) | Justification |
|---|---|---|---|
| Nom du type | `TS_CS_SWAP_EQUIP` | `TM_CS_SWAP_EQUIP` | nomenclature du dépôt (`op_codes.md`, `GamePackets.cs`) |
| Corps | vide (`_DEF(_)`) | vide | identique |
| Handler | absent → paquet journalisé en debug et ignoré, connexion conservée | branche explicite « log + `continue` » | Navislamia **lève** sur un id non branché (`GameClient.cs:681`), donc copier NGemity à la lettre casserait le serveur ; l'écart est de forme, pas de comportement observable |
| Réponse | aucune | aucune | conforme |

Aucun écart de taille, d'ordre de champs ni d'id.

## 7. NON ÉTABLI

1. **Sémantique du paquet.** Ni rzu, ni NGemity, ni le client 7.3 examiné
   n'expliquent ce que « swap equip » change. Ni le nom, ni son emplacement
   entre `HIDE_EQUIP_INFO` (221/222) et `SKIN_INFO` (224) ne suffisent à
   conclure quoi que ce soit sur le comportement attendu. Question précise à
   trancher : *ce paquet demande-t-il une permutation d'objets déjà équipés
   (et laquelle), où est-il un simple accusé côté client sans effet serveur ?*
2. **Le client 7.3 peut-il émettre 223 ?** Preuves relevées, toutes négatives
   mais **non concluantes** : la table de noms de paquets de `SFrame.exe`
   (59 cas vérifiés, ids 1 → 287) n'a **aucun** cas pour 223, et les deux
   chaînes `TM_CS_SWAP_EQUIP` et `TM_SC_SKIN_INFO` sont absentes du binaire
   (167 chaînes `TM_*` au total). Mais cette table n'est **pas** le registre des
   paquets émis : `TM_CS_CHANGE_ITEM_POSITION (218)` et
   `TM_CS_ARRANGE_ITEM (219)` en sont absents eux aussi alors que le client les
   envoie bel et bien (`CLAUDE.md:997-1002` : glisser-déposer et tri
   d'inventaire fonctionnent en jeu). Cette absence ne prouve donc rien.
3. **Le corps est-il réellement vide pour 7.3 ?** Voir section 3 : accord de
   deux références possiblement non indépendantes, sans test client ni ligne
   `Last tested`. Question précise : *le client 7.3 envoie-t-il 7 octets ou
   davantage ?*
4. **Faut-il répondre `TS_SC_RESULT` tagué 223 ?** Aucun élément dans les deux
   références. Poser la question à Killian plutôt que d'ajouter une réponse.

## 8. Commits épinglés

| Dépôt | Commit | Contenu utilisé |
|---|---|---|
| Navislamia | `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` | `op_codes.md:72`, `Game/Network/Packets/Enums/GamePackets.cs:39-40`, `Game/Network/Packets/Header.cs:9-24`, `Game/Network/Clients/GameClient.cs:664-682`, `CLAUDE.md:40-48,997-1002` |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | `librzu/src/packets/GameClient/TS_CS_SWAP_EQUIP.h` (7,9-12), `TS_CS_RETURN_LOBBY.h:5-11`, `TS_SC_HIDE_EQUIP_INFO.h:5-13`, `TS_CS_PUTON_ITEM.h:8-12` ; fichier créé par `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (« add all known GS packets as of 9.4 », 2017-02-07) |
| NGemity (RZEmulator) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | `shared/Server/Packets/GameClient/TS_CS_SWAP_EQUIP.h:6-9`, `shared/Server/ClientPackets.h:88`, `shared/Server/Packets/GameClient/TS_CS_LOGOUT.h:6-8`, `Chihiro/src/Network/GameNetwork/WorldSession.cpp:92-164`, `WorldSession.h:59-122` |
| Client Epic 7.3 | `SFrame.exe`, 9 841 664 octets, SHA-256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | lecture statique seule (§2, §7.2, annexe) |

## Annexe — reproductibilité des relevés client

Aucun exécutable client n'a été lancé : `SFrame.exe` n'est lu qu'en statique.
Les deux relevés qui fondent la section 7.2 sont rejouables avec
`strings -n 4 SFrame.exe` (recherche de `TM_CS_SWAP_EQUIP`, `SWAP_EQUIP`,
`swap`) et par cartographie des chaînes `TM_*` dans `.rdata`
(`va = 0x400000 + 0x60f000 + (offset_fichier - 0x60da00)`) puis recherche des
références `push <longueur>` / `push <adresse de la chaîne>` dans `.text`
(offsets 0x400 → 0x60aa00) ; chaque cas se poursuit par un `mov eax, <id>`
immédiat, ce qui restitue la table id → nom du client. Sites relevés pour la
zone concernée : `217 → 0x275be8`, `221 → 0x275c5e`, `222 → 0x275cd4`,
`250 → 0x275d4a`, un corps de cas valant 118 octets (`0x76`).
