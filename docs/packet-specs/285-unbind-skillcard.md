# 285 — TM_CS_UNBIND_SKILLCARD

Fiche de paquet établie par `navis-ref` (archéologue de protocole), branche
`hermes/packet-285-unbind-skillcard`, à partir de `master`
`ec76b218cd0bd7c6498d725f253abb8b431f0cd6`. Cette fiche ne contient **aucun** changement de code
serveur : elle fixe le format, le gating de version, le comportement attendu et les réserves
vérifiables.

Le jumeau `284` (`TM_CS_BIND_SKILLCARD`) et la réponse `286` (`TM_SC_SKILLCARD_INFO`) sont décrits
ici parce que le 285 ne se comprend pas sans eux. La fiche de 284 vit sur la branche
`hermes/packet-284-bind-skillcard` (`docs/packet-specs/284-bind-skillcard.md`, MR #23, **non
mergée** : rien de son code n'est sur `master`). Ce que cette fiche reprend de 284, ce qu'elle
corrige et ce qu'elle laisse à sa branche est dit explicitement en §2.2, §5.3 et §5.5.

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id décimal | **285** (`0x011D`) | `Navislamia/op_codes.md:99` — `[285] = "TM_CS_UNBIND_SKILLCARD"` |
| sens | client → serveur | rzu `librzu/src/packets/GameClient/TS_CS_UNBIND_SKILLCARD.h:13` (`SessionPacketOrigin::Client`) |
| nom client | `TM_CS_UNBIND_SKILLCARD` | `op_codes.md:99` ; dump `strings -n 4` de `SFrame.exe`, l. 26364 |
| jumeau montant | **284** `TM_CS_BIND_SKILLCARD`, même forme | `op_codes.md:98` ; rzu `TS_CS_BIND_SKILLCARD.h:6-11` ; dump l. 26365 |
| réponse serveur | **286** `TM_SC_SKILLCARD_INFO`, même forme | `op_codes.md:100` ; rzu `TS_SC_SKILLCARD_INFO.h:6-7` ; dump l. 26363 |
| taille de trame | **15 octets**, fixe (`7 + 4 + 4`) | §3 |
| direction de 286 | serveur → client | rzu `TS_SC_SKILLCARD_INFO.h:13` (`SessionPacketOrigin::Server`) — le fichier est rangé dans `librzu/src/packets/GameClient/` : la direction vient de l'origine déclarée, pas du répertoire |
| carte Trello | `https://trello.com/c/ZRjKHP4f` | corps de la carte d'archéologie (étape 1/3) ; suivi `navislamia:packet:285` |
| état dans le dépôt | **285 et 286 absents** de `GamePackets` (fichier de 102 lignes ; voisins présents : 283 l. 47, 287 l. 30) | `Game/Network/Packets/Enums/GamePackets.cs` |

Le client Epic 7.3 connaît la paire id ↔ nom : sa routine d'enregistrement construit trois entrées
consécutives `{id, nom}` — `0x11c` / `0xa53550` (longueur `0x14` = 20), `0x11d` / `0xa53538`
(longueur `0x16` = 22), `0x11e` / `0xa53520` (longueur `0x14` = 20) — soit exactement 284, **285** et
286, dans cet ordre aligné sur `op_codes.md`. Preuves brutes : `SFrame.exe` VA `0x676fba`-`0x6770fb`
(`objdump -d -M intel`), chaînes aux VA `0xa53520` / `0xa53538` / `0xa53550` (fichier
`SFrame.exe`, sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`).

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Ce qu'établissent les deux références

- la cible du 285 est **le joueur lui-même** : NGemity refuse tout `target_handle` différent de son
  propre handle (`WorldSession.cpp:1710-1713`), et `Unit::UnBindSkillCard` délie la carte du porteur
  du paquet (`Unit.cpp:2614-2622`). Le geste est donc « délier cette carte de compétence de mon
  personnage », jamais d'un tiers ;
- l'objet doit être une **carte de compétence de mon inventaire** : groupe `GROUP_SKILLCARD` (= 10),
  présent dans l'inventaire, propriétaire identique, et **effectivement lié** (`m_hBindedTarget != 0`)
  — `WorldSession.cpp:1714-1717` ;
- le paquet est le **symétrique exact du 284** : mêmes deux champs, même ordre, même taille, même
  handler de session, même écho — seuls l'id et le sens de l'état testé changent (`!= 0` en 284,
  `== 0` en 285).

### 2.2 Ce que le corpus client 7.3 établit (nouveau : tranche le §7.1 de la fiche 284)

Le binaire 7.3 porte un **constructeur de trame dédié au 285**, et c'est le même code que celui du
284. Lecture statique (`objdump -d -M intel` sur `SFrame.exe`, aucune exécution) :

- **VA `0x48c740`** — constructeur de trame 285 sur un tampon de 15 octets déjà zéro :
  `ba 1d 01 00 00` = `mov edx,0x11d` (`0x48c76c`), `66 89 50 04` = `mov WORD PTR [eax+0x4],dx`
  (`0x48c771`), `c7 00 0f 00 00 00` = `mov DWORD PTR [eax],0xf` (`0x48c777`) — soit `ID` = 285 et
  `Length` = 15 — puis recalcul de la somme de contrôle sur les six premiers octets et `ret`
  (`0x48c78a-0x48c78d`) ;
- **VA `0x48c6f0`** — constructeur strictement identique pour le 284
  (`ba 1c 01 00 00` = `mov edx,0x11c` en `0x48c71c`, `Length` = 15 en `0x48c727`) ;
- les deux sont appelés depuis **un seul et même handler**, à la **VA `0x48f1f0`**
  (`push ebp / mov ebp,esp / sub esp,0x10 / mov edi,ecx`, fenêtre en `ecx`, objet message en
  `[ebp+8]`), sur un sélecteur d'action lu en `[message+0x13]` (`mov eax,[esi+0x13]` en
  `0x48f256`) ; le corps du sélecteur n'est atteint que si `[fenêtre+0xa0]` vaut `0` ou `2`
  (`0x48f240-0x48f24d`) :
  - `0x48f2b5-0x48f2c2` : `cmp eax,0x2` → `lea ecx,[ebp-0x10]` puis `call 0x48c6f0` = **284** ;
  - `0x48f2c4-0x48f2cc` : `cmp eax,0x3` → même tampon puis `call 0x48c740` = **285** ;
- les deux champs sont remplis **après** le retour du constructeur, aux offsets 7 et 11 du tampon
  (`lea ecx,[ebp-0x10]` = base de trame) :
  `0x48f2d8: mov ecx,[esi+0x17]` / `0x48f2db: mov edx,[esi+0x23]` /
  `0x48f2de: mov [ebp-0x9],ecx` (= trame + 7) / `0x48f2e1: mov [ebp-0x5],edx` (= trame + 11) ;
- la trame est ensuite **émise** : si `[window+0xb8]` est non nul, appel virtuel
  `[vtable+0xc4]` avec la trame en argument (`0x48f2e6-0x48f2f8`, reprise en `0x48f2d1-0x48f2e4`
  pour les deux cas). Le 285 est donc bien un paquet que le client **construit et envoie** ;
- ce handler est atteint par la bascule de messages en `0x49e21d-0x49e234` (table d'octets
  `0x49ea50`, table de sauts `0x49e98c`, index = clé − `0x403`) pour la **clé `0x40c` (1036)**,
  dont le stub `0x49e23b-0x49e243` appelle `0x48f1f0`.

**Conséquence sur le §7.1 de la fiche 284.** La fiche 284 concluait « le geste exact qui fait
émettre le 284 n'est pas établi ». Ce qui est maintenant établi : le 284 et le 285 sont **deux
sous-commandes (`2` et `3`) d'un même handler, celui d'une même action cliente**, appelé avec les
mêmes deux valeurs (`[message+0x17]`, `[message+0x23]`). Il n'existe donc pas deux gestes distincts :
il existe **une action cliente dont le libellé dépend de l'état de la carte** — c'est un couple
lier/délier, l'un des deux étant proposé selon que la carte est liée ou non. Ce qui reste non
établi est le **libellé** de cette action, pas son existence (§7.1). La branche 284 n'est pas
mergée : cette conclusion est portée par l'arbitrage de Killian (§`A VERIFIER PAR KILLIAN`).

### 2.3 Le piège du répertoire (direction)

Les deux références rangent le fichier de 285 dans leur répertoire `GameClient/` (rzu) ou
`Server/Packets/GameClient/` (NGemity) : c'est l'origine déclarée dans le paquet qui donne le sens,
et les deux déclarent le client comme émetteur (`TS_CS_UNBIND_SKILLCARD.h:13`,
`SessionPacketOrigin::Client`). Un fichier `GameClient/` peut donc décrire un paquet montant
(285, `Client`) comme un paquet descendant (286, `Server`) : la direction appartient à la fiche.

## 3. Structure sur le fil

Trame client → serveur, en-tête Navislamia de 7 octets (`Length` `uint32` @0, `ID` `uint16` @4,
`Checksum` `uint8` @6 — `Game/Network/Packets/Header.cs:6-25`), puis les deux champs de rzu
`TS_CS_UNBIND_SKILLCARD.h:6-7` dans cet ordre :

| Offset | Taille | Type | Nom | Valeur observée | Source |
| --- | --- | --- | --- | --- | --- |
| 0 | 4 | `uint32` | `Length` | **`15`** (`0x0F`) — client 7.3 : `mov DWORD PTR [eax],0xf` | client `SFrame.exe` VA `0x48c777` ; `Header.cs:9` |
| 4 | 2 | `uint16` | `ID` | **`285`** (`0x011D`) — client 7.3 : `mov WORD PTR [eax+0x4],dx` avec `edx=0x11d` | client VA `0x48c76c` ; rzu `TS_CS_UNBIND_SKILLCARD.h:11` ; `op_codes.md:99` |
| 6 | 1 | `uint8` | `Checksum` | **`0x2D`** — somme des six premiers octets (`0x0F + 0x00 + 0x00 + 0x00 + 0x1D + 0x01`), recalculée par le client | client VA `0x48c77d-0x48c78a` ; `Header.cs:11` |
| 7 | 4 | `ar_handle_t` (`uint32`) | `item_handle` | handle de l'objet « carte de compétence » à délier ; client 7.3 : écrit en `trame+7` depuis `[message+0x17]` | rzu `TS_CS_UNBIND_SKILLCARD.h:6` ; NGemity `TS_CS_UNBIND_SKILLCARD.h:7` (`uint32_t`) ; client VA `0x48f2d8-0x48f2de` |
| 11 | 4 | `ar_handle_t` (`uint32`) | `target_handle` | handle de la cible du déliage : **le joueur lui-même** ; client 7.3 : écrit en `trame+11` depuis `[message+0x23]` | rzu `TS_CS_UNBIND_SKILLCARD.h:7` ; NGemity `TS_CS_UNBIND_SKILLCARD.h:8` ; client VA `0x48f2db-0x48f2e1` ; NGemity `WorldSession.cpp:1710` |

**Taille totale de la trame : 15 octets** (`7 + 4 + 4`). Trois sources indépendantes concordent :
rzu (deux `ar_handle_t`, `strong_typedef` sur `uint32_t` —
`librzu/src/lib/Packet/GameTypes.h:40`), NGemity (deux `uint32_t`) et le constructeur de trame du
client 7.3, qui écrit lui-même `Length = 15` et zéroise exactement les octets 0 à 14.

Réponse serveur → client **286** : même en-tête de 7 octets, **15 octets** au total, même ordre
(`item_handle` @7, `target_handle` @11), avec `target_handle = 0` lorsque la carte vient d'être
déliée (NGemity `Messages.cpp:1010-1017` renvoie `pItem->m_hBindedTarget`, remis à `0` par
`SetBindTarget(nullptr)` — `Item.cpp:326-329`).

Rappel utile au développeur : le 253 (`TM_CS_USE_ITEM`) exige une trame de 47 octets
(`GameActionPackets.cs:180-195`, 40 octets de charge), mais le 285 **n'a pas** de charge
supplémentaire — le constructeur du client fixe `Length = 15` et n'écrit rien après l'offset 14.
Un lecteur qui exigerait davantage rejetterait la trame réelle.

## 4. Gating de version

rzu déclare l'id par plage de version (`TS_CS_UNBIND_SKILLCARD.h:9-11`) :

```
#define TS_CS_UNBIND_SKILLCARD_ID(X) \
	X(285, version < EPIC_9_6_3) \
	X(1285, version >= EPIC_9_6_3)
```

| Constante | Valeur | Source |
| --- | --- | --- |
| `EPIC_7_3` (cible du dépôt) | `0x070300` | `librzu/src/lib/Packet/PacketEpics.h:59` |
| `EPIC_9_6_3` (bascule de l'id) | `0x090603` — commentaire « GS packet ID modified with version 20200713 » | `PacketEpics.h:96` |

**Décision, tranchée pour 7.3 : le dépôt sert l'id `285`.** `EPIC_7_3` (`0x070300`) est strictement
inférieur à `EPIC_9_6_3` (`0x090603`), donc la branche `version < EPIC_9_6_3` s'applique : **285**,
jamais **1285**. La famille `1285+` (Epic 9.6.3 et au-delà) est hors sujet pour ce serveur et ne doit
pas être déclarée.

**Aucun champ n'est gaté par version** : `item_handle` et `target_handle` sont deux `ar_handle_t`
déclarés sans condition, et NGemity (EPIC `4_1_1`, `shared/Common/Define.h:25`) porte exactement les
mêmes deux `uint32_t`. La seule chose gatée est l'id lui-même, et elle est statuée ci-dessus.

## 5. Traitement attendu

### 5.1 Ce que fait la référence NGemity (`38ceb2c`, EPIC `4_1_1`)

`WorldSession::onUnBindSkilLCard` — `Chihiro/src/Network/GameNetwork/WorldSession.cpp:1703-1720`
(déclaré dans la table `STATUS_AUTHED` en `WorldSession.h:101` et `WorldSession.cpp:134`) :

1. `sMemoryPool.GetObjectInWorld<Item>(item_handle)` → `nullptr` ⇒
   `SendResult(receivedId, TS_RESULT_NOT_EXIST, item_handle)` (`:1706-1709`) ;
2. `target_handle != m_pPlayer->GetHandle()` ⇒
   `SendResult(receivedId, TS_RESULT_NOT_ACTABLE, target_handle)` (`:1710-1713`) ;
3. `!IsInInventory()` **ou** `GetOwnerHandle() != mon handle` **ou** `GetItemGroup() !=
   GROUP_SKILLCARD` **ou** `m_hBindedTarget == 0` ⇒
   `SendResult(receivedId, TS_RESULT_ACCESS_DENIED, item_handle)` (`:1714-1717`) ;
4. sinon `m_pPlayer->UnBindSkillCard(pItem)` (`:1719`) et **rien d'autre : pas de `TS_SC_RESULT`
   de succès**.

`receivedId` est l'id reçu (`pRecvPct->getReceivedId()`), donc **285**.

Effet — `Unit::UnBindSkillCard`, `Chihiro/src/Entities/Unit/Unit.cpp:2614-2622` :

```cpp
Skill *pSkill = GetSkill(pItem->GetItemTemplate()->skill_id);
if (pSkill != nullptr) {
    pSkill->m_nEnhance = 0;                       // :2618  enhance de COMPÉTENCE remis à zéro
    pItem->SetBindTarget(nullptr);                // :2619  sockets 0 ET 1 à 0 + m_hBindedTarget = 0
    Messages::SendSkillCardInfo(dynamic_cast<Player *>(this), pItem);   // :2620  écho 286
}
```

`Item::SetBindTarget(nullptr)` — `Chihiro/src/Entities/Item/Item.cpp:314-331` : `SetSocketIndex(0, 0)`
(`:319`), `SetSocketIndex(1, 0)` (`:324`), `m_hBindedTarget = 0` (`:329`),
`m_bIsNeedUpdateToDB = true` (`:331`). Les sockets sont persistés aux colonnes `Socket_0..Socket_3`
(`Item.cpp:99-102` et `:138-141`).

Écho — `Messages::SendSkillCardInfo`, `Chihiro/src/Network/Messages.cpp:1010-1016` : 286 avec
`item_handle = pItem->GetHandle()` et `target_handle = pItem->m_hBindedTarget`, donc **cible nulle
après déliage**. Le client 7.3 sait traiter ce message (trace
`SGameInterface - MSG_SKILLCARD_INFO`, dump `SFrame.exe` l. 25158 ; classe d'IHM
`USMSG_SKILLCARD_INFO`, l. 44393).

Trois asymétries de la référence, relevées et **non portées** :

1. `onUnBindSkilLCard` n'a **pas** le garde `if (m_pPlayer == nullptr) return;` que son jumeau
   `onBindSkillCard` porte en `:1680-1681`, alors qu'il déréférence `m_pPlayer` en `:1710` : la
   référence peut planter là où le 284 refuse proprement ;
2. le handler du 285 ne teste **aucune** compétence, contrairement à `onBindSkillCard`
   (`:1697-1698`) : l'état lié est jugé **uniquement** sur `m_hBindedTarget` ;
3. `Unit::UnBindSkillCard` ne fait rien quand `GetSkill(skill_id)` est `nullptr` : l'objet reste lié,
   aucune écho 286 et **aucun refus** n'est envoyé au client. C'est l'écart à qualifier (§5.2 g,
   §6.4, §7.4).

### 5.2 Ce que Navislamia doit faire (décisions)

**a. Énumération.** Ce cycle ajoute `TM_CS_UNBIND_SKILLCARD = 285` et, pour la réponse,
`TM_SC_SKILLCARD_INFO = 286` — le sort du 286 est tranché en **§5.3**. Place de 285 : à côté de
283 (`GamePackets.cs:47`) et 287 (`:30`).

**b. Lecture.** Un `TryReadUnbindSkillCard` calqué sur `TryReadUseItem`
(`GameActionPackets.cs:180-195`) : exiger **au moins** `HeaderSize + 8 = 15` octets, sinon `false` ;
trame trop courte ⇒ `SendResult(285, ResultCode.InvalidArgument)` sur le modèle de
`HandlePutonItemAsync` (`GameClient.cs:356-362`). Ne **pas** exiger davantage : voir §3.

**c. Écriture de la réponse.** Un `BuildSkillCardInfo(uint itemHandle, uint targetHandle)` jumeau de
`BuildUseItemResult` (`GameCharacterPackets.cs:205-212`) : `CreatePacket(GamePackets.TM_SC_SKILLCARD_INFO, 7 + 8)`,
les deux handles en little-endian aux offsets 7 et 11, puis `WriteChecksum`. C'est exactement le
constructeur que la fiche 284 prescrit à sa branche (`284-bind-skillcard.md` §5.2 c) : si la branche
de base le porte déjà au moment du développement, **le réutiliser tel quel** (§5.3).

**d. Répartition (critère transversal 4).** Un bras dans la chaîne `if (header.ID == ...)` de
`GameClient.cs` (modèle : 253 en `:708-712`, avec `_ = ...Async(...)` puis `continue`) et, si le 286
est déclaré sur cette branche, un bras « paquet S→C reçu, journaliser et ignorer » calqué sur
`TM_SC_REGION_ACK` (`GameClient.cs:620-628`) : le client 7.3 ne l'envoie jamais, et sans ce bras le
membre atteindrait la bascule finale `_ => throw new Exception("Unknown Packet Type")`
(`GameClient.cs:802`), ce que le critère 4 interdit.

**e. Traitement.** Le jumeau 284 a déjà posé l'architecture sur sa branche (`SkillCardService`,
`SkillCardBindRules`, `SkillCardBindResult`, `ICharacterService.BindSkillCardAsync` — voir le §10 de
`284-bind-skillcard.md`, commit `67319ce`) : le 285 doit **prolonger ces fichiers**, pas en créer
d'autres (`UnbindAsync` dans `SkillCardService`, `CheckUnbindable` dans les règles — `IsBound` y est
déjà, `BuildSkillCardInfo` aussi). S'ils sont absents (branche 284 non mergée et cycle 285 développé
seul), le modèle de repli est `ItemUseService` (`ItemUseService.cs:17-103`). Dans tous les cas le
service applique les trois jugements de NGemity **dans son ordre**, puis délie :

| Ordre | Condition | Réponse |
| --- | --- | --- |
| 1 | lecture de la trame impossible | `SendResult(285, InvalidArgument)` |
| 2 | lecture de l'objet en base en échec (exception) | `SendResult(285, DBError, item_handle)` (modèle `ItemUseService.cs:45`) |
| 3 | handle introuvable parmi les objets du personnage | `SendResult(285, NotExist, item_handle)` — valeur = `item_handle`, comme NGemity `:1707` |
| 4 | `target_handle != ConnectionInfo.CharacterHandle` | `SendResult(285, NotActable, target_handle)` — valeur = `target_handle`, comme NGemity `:1711` |
| 5 | groupe d'objet connu et `!= ItemGroup.Skillcard` | `SendResult(285, AccessDenied, item_handle)` |
| 6 | objet équipé (`WearInfo != ItemWearType.None`) | `SendResult(285, AccessDenied, item_handle)` |
| 7 | `SocketItemIds[0] == 0` (carte **non liée**) | `SendResult(285, AccessDenied, item_handle)` |
| 8 | succès | socket 0 remis à `0`, persistance, puis **286** avec `target_handle = 0` ; **aucun** `TS_SC_RESULT` |

La convention « lié ⇔ `SocketItemIds[0] != 0` » est celle que la fiche 284 a fixée à sa branche
(elle y écrit l'`Id` du porteur dans le socket 0) : elle est reprise ici pour que 284 et 285
manipulent le **même bit d'état** (§6.2). Un `SocketItemIds` nul vaut « non liée ».

Le groupe d'objet est lu par `ItemGroupCatalog.TryGetGroup((int)item.ItemResourceId, out var group)`
(`ItemGroupCatalog.cs:28-31`). Une ressource **inconnue** du catalogue ne peut pas être jugée :
refuser serait un refus non prouvé, la déliaison est donc **laissée non gatée** dans ce cas, comme le
253 le fait pour les niveaux d'utilisation (`ItemUseService.cs:57-59`).

**f. Persistance.** L'écriture doit inverser exactement celle du cycle 284 : sous
`RunExclusiveAsync` (`CharacterService.cs:441-445`), lire le personnage par son nom
(`:204-208`), remettre `SocketItemIds[0]` à `0` en préservant les sockets 1 à 3, sauvegarder
(`:431-434`). Sur `master`, **cette primitive n'existe pas** : la branche 284 ajoute un
`WriteBearerSocket` (fiche 284 §5.2 f ; sur sa branche, `CharacterService.cs:251-262`, qui **refait**
le tableau `SocketItemIds` à quatre slots pour que le change tracker voie la ligne modifiée et
conserve les sockets 1-3). Si elle est déjà sur la branche de base, la réutiliser avec `0` comme
valeur ; sinon ajouter l'équivalent inverse (`ClearBearerSocketAsync(characterName, itemHandle)`),
strictement sur le socket 0, et **ne pas écrire** quand le verdict est un refus (décision 4 du
§10.1 de la fiche 284).

**g. Compétence et enhance (question 5 du cadrage).** Délier ne change, côté Navislamia, **que le
socket 0 de l'objet** :

- l'`Enhance` **de l'objet** (`ItemEntity.cs:29`) n'est touché par **aucune** des deux références :
  au lier on le recopie vers la compétence, au délier on ne le touche pas ;
- l'`Enhance` **de compétence** (`Skill::m_nEnhance`, `Unit.cpp:2618`) n'a **pas de support** sur
  `master` : `CharacterSkillEntity` ne porte que `CharacterId`, `SkillId`, `Level`
  (`Game/DataAccess/Entities/Telecaster/CharacterSkillEntity.cs:3-9`) ; seules les **ressources**
  portent des coefficients par enhance (`SkillResourceEntity.cs:40-130`). Il n'y a donc rien à
  remettre à zéro ;
- l'état observable côté client est celui de 286 : `item_handle` connu, `target_handle` nul.

Ce que le client fait de cet écho au-delà de l'affichage (recalcul des bonus de la compétence,
rafraîchissement des info-bulles) n'est **pas** démontrable statiquement : §7.2.

**h. Tests.** Un test d'offsets conforme au critère 3, dans la suite existante
(`Tests/`) : trame 285 de **15** octets (`Length` @0 = 15, `ID` @4 = 285, `Checksum` @6,
`item_handle` @7, `target_handle` @11), trame de 14 octets rejetée, écho 286 de **15** octets
(`ID` @4 = 286, `target_handle` @11 = `0`), et les refus 3/4/5/7 avec la valeur exacte de
`TS_SC_RESULT`. Plancher mesuré à ce commit de `master` : `dotnet build` 0 erreur et `dotnet test`
**448 réussis / 0 échec** (le critère 2 en exige 366 ; la branche 284 en est à 471 après son propre
cycle). Les tests de 284 sur sa branche couvrent la même trame montante et le même écho :
la duplication est attendue tant que les deux branches ne sont pas intégrées (§5.3).

**i. Garde de session.** Ne pas porter l'asymétrie de NGemity (§5.1, asymétrie 1) : le service
résout le personnage par `ConnectionInfo.CharacterName`, exactement comme le 253.

### 5.3 Le sort de 286 `TM_SC_SKILLCARD_INFO` sur cette branche — décision

**Décision : le 286 est déclaré sur cette branche**, dans `GamePackets` (`TM_SC_SKILLCARD_INFO = 286`),
avec un bras de dispatch « S→C reçu, journaliser et ignorer » calqué sur `TM_SC_REGION_ACK`
(`GameClient.cs:620-628`) et un constructeur `BuildSkillCardInfo` (§5.2 c).

Pourquoi ce choix, et pas « laisser le 286 à la branche 284 » :

1. **Le cycle 285 doit être intégrable seul.** Émettre une trame exige un membre de `GamePackets` :
   les constructeurs de paquets passent tous par `CreatePacket(GamePackets.<membre>, ...)`
   (`GameCharacterPackets.cs:198`, `:207`, `:217`). Sans le membre, le développeur ne peut pas
   écrire l'écho — la carte 285 ne serait pas livrable.
2. **Le 286 est le seul canal de l'état délié.** Le handler de référence n'envoie **aucun**
   `TS_SC_RESULT` de succès (`WorldSession.cpp:1719`) : le seul effet observable du déliage pour le
   client est le 286 à cible nulle (`Messages.cpp:1010-1016`). Un 285 qui ne répondrait rien
   laisserait l'IHM cliente sur un état lié faux.
3. **Le critère transversal 4 est satisfait** par le bras « journaliser et ignorer » : le membre
   n'atteint jamais la bascule finale (`GameClient.cs:802`).

**Règle d'intégration avec la branche 284 (tranchée ici, pour qu'aucun développement n'ait à
deviner).** La branche 284 déclare le **même** membre, avec la **même** valeur et la **même** forme
(fiche 284 §5.2 a et c). Les deux déclarations doivent rester **identiques au caractère près**, et
l'intégration suit une règle unique :

- si la branche de base porte déjà le 286 au moment du développement (284 mergée) : **ne rien
  déclarer**, réutiliser le membre, le bras et le constructeur existants ;
- sinon, ce cycle les ajoute à l'identique ; **c'est celui qui est intégré en second qui supprime son
  doublon** (une ligne dans `GamePackets.cs`, un bras dans `GameClient.cs`, une méthode dans
  `GameCharacterPackets.cs` — trois conflits triviaux, pas une divergence de conception).

Le reste du corpus 284 est également **dupliqué par construction** si les deux cycles avancent en
parallèle : `SkillCardService`, `SkillCardBindRules`, `SkillCardBindResult`, le contrat
`ICharacterService`, `WriteBearerSocket` et les tests de trame. La règle ci-dessus s'y applique
ligne à ligne : mêmes noms, mêmes signatures, un seul exemplaire après intégration. Le cycle 285
**peut** être développé seul (c'est le choix tranché ici) : il est alors complet sur sa branche et
l'intégration se réduit à supprimer les doublons, jamais à arbitrer un choix de conception.
Le §10 de `284-bind-skillcard.md` documente l'implémentation 284 réellement livrée (fichiers, lignes,
tests) : c'est la référence à consulter en premier au développement.

### 5.4 Primitives réutilisables (vérifiées sur `master` `ec76b21`)

| Primitive | Emplacement | Usage pour 285 |
| --- | --- | --- |
| `TM_SC_RESULT = 0` | `Enums/GamePackets.cs:5` | enveloppe des refus |
| 283 / 287 / 253 voisins | `Enums/GamePackets.cs:47` / `:30` / `:41` | placement de 285 |
| `ResultCode` : `Success=0` l. 6, `NotExist=1` l. 8, `NotActable=5` l. 12, `AccessDenied=6` l. 13, `DBError=8` l. 15, `InvalidArgument=28` l. 37 | `Enums/ResultCode.cs` | valeurs exactes des refus |
| en-tête 7 octets | `Packets/Header.cs:6-25` | format de trame |
| `HeaderSize = 7` | `GameActionPackets.cs:8`, `GameCharacterPackets.cs:19` | constantes de taille |
| `TryReadUseItem` (15/47 octets) | `GameActionPackets.cs:180-195` | modèle du lecteur |
| `BuildUseItemResult` | `GameCharacterPackets.cs:205-212` | modèle du constructeur 286 |
| `WriteInventoryItem` (sockets @38+4i, l. 353-356) | `GameCharacterPackets.cs:341-366` | sémantique des sockets dans le flux inventaire |
| `SendResult(id, result, value)` | `GameClient.cs:56-60` | émission des refus |
| bras « S→C reçu, journaliser et ignorer » | `GameClient.cs:620-628` | modèle du bras 286 |
| bras `TM_CS_USE_ITEM` | `GameClient.cs:708-712` | modèle du bras 285 |
| `GetItemByHandleAsync` (handle = `ItemEntity.Id`) | `CharacterService.cs:204-208`, `FindByHandle` `:436-439` | résolution de `item_handle` |
| `RunExclusiveAsync` / `SaveChanges` | `CharacterService.cs:441-445` / `:431-434` | écriture sérialisée |
| `ItemEntity.Id/Enhance/WearInfo/SocketItemIds` | `Telecaster/ItemEntity.cs:26, 29, 34, 35` | état de l'objet |
| `ItemGroup.Skillcard = 10`, `ItemWearType.None = -1` | `Enums/ItemGroup.cs:15`, `Enums/ItemWearType.cs:5` | jugements 5 et 6 |
| `ItemGroupCatalog.TryGetGroup` (inconnu ⇒ `false`) | `ItemGroupCatalog.cs:28-31` | jugement de groupe |
| `ItemUseService` (service complet : lecture, jugement, réponse) | `ItemUseService.cs:17-103` | modèle du service 285 |
| `CharacterSkillEntity` (**pas** d'enhance) | `Telecaster/CharacterSkillEntity.cs:3-9` | §5.2 g |
| `TS_SC_RESULT` | `GameClient.cs:58` (`new Packet<TS_SC_RESULT>`) | enveloppe des refus |

### 5.5 Ce que `master` ne porte pas (à ne pas supposer disponible)

- **285, 286 et 284** dans `GamePackets` : absents (vérifié, fichier de 102 lignes) ;
- `BuildSkillCardInfo`, `WriteBearerSocket` / `ClearBearerSocketAsync` : **inexistants** sur
  `master` ; ils n'existent que sur la branche `hermes/packet-284-bind-skillcard`, non mergée ;
- le socle « invocations et familiers » (MR #10 ouverte, carte dg6sVB6H) : absent, donc le
  socket 1 (liaison à une invocation) n'est **écrit nulle part** sur `master` (§6.5) ;
- les cartes de châsse 214/215 et `TM_CS_SUMMON_CARD_SKILL_LIST` 452 : **hors périmètre** de ce
  cycle, aucune primitive à en attendre.

## 6. Écarts assumés avec NGemity

1. **Espace de handles des refus.** NGemity distingue « handle inconnu de la mémoire du monde »
   (`NOT_EXIST`) de « objet présent mais pas à moi / pas une carte / pas lié » (`ACCESS_DENIED`).
   Navislamia résout le handle **dans les objets du personnage**
   (`CharacterService.cs:204-208`) : tout handle inconnu tombe en `NotExist`, et seuls les cas
   5 à 7 de §5.2 e produisent `AccessDenied`. Même écart que les cycles 203 et 284, assumé pour
   rester cohérent dans le dépôt.
2. **État « lié » = socket 0, pas `m_hBindedTarget`.** NGemity garde une carte liée en mémoire
   (`Item::m_hBindedTarget`, `Item.h:140`) et la persiste dans les sockets
   (`Item.cpp:314-331`). Navislamia n'a pas d'état lié hors base : l'unique vérité est
   `ItemEntity.SocketItemIds[0]` (l'`Id` du porteur, convention du cycle 284). « non lié » ⇒
   socket 0 nul. Les deux lectures coïncident tant que le socket 0 n'est pas réutilisé pour autre
   chose ; c'est le point de vigilance §7.6.
3. **Aucun rafraîchissement d'inventaire au succès.** NGemity n'en envoie pas ; le dépôt non plus :
   la réponse au succès est exactement un 286. Le socket 0 est du reste déjà visible dans le flux
   inventaire (`WriteInventoryItem`, `GameCharacterPackets.cs:353-356`), mais seulement dans les
   trames déjà envoyées.
4. **Compétence introuvable : silence dans la référence, déliage chez nous.** NGemity laisse
   l'objet lié et n'écho pas (`Unit.cpp:2616`, §5.1 asymétrie 3). Navislamia ne modélise pas
   l'enhance de compétence (§5.2 g) : le socket est l'unique état, donc une carte liée est déliée et
   le 286 part. Traduire la référence à la lettre supposerait de tenir un état de compétence
   inexistant sur `master` et produirait un client bloqué sur un état faux. Écart assumé, porté à
   Killian (§`A VERIFIER PAR KILLIAN`, point 3).
5. **Sockets 0 et 1.** `SetBindTarget(nullptr)` (`Item.cpp:314-331`) remet **les deux** sockets à
   zéro. Navislamia n'écrit que le socket 0 (socket 1 = invocation, socle absent de `master`) :
   équivalent aujourd'hui, à revoir quand le socle « invocations » arrivera (§7.6).
6. **286 déclaré sur cette branche alors que `master` ne le porte pas**, et déclaré aussi par la
   branche 284 : c'est une duplication **voulue**, avec la règle d'intégration de §5.3. Sans elle,
   le cycle 285 ne serait pas livrable seul.
7. **Le handler de NGemity s'appelle `onUnBindSkilLCard`** (double majuscule) : coquille de la
   référence, sans effet ; à ne pas recopier comme nom de service.
8. **`Checksum` : non vérifié.** Le client 7.3 émet `0x2D` pour cette trame (§3) ; `Header.cs` lit
   l'octet (`:24`) mais aucun chemin ne le compare côté serveur. Aucun refus n'est fondé dessus.
9. **Jugement 6 (`WearInfo`)** : `Item::IsInInventory()` (`Item.cpp:292-295`) n'a pas d'équivalent
   direct ; `WearInfo == None` en tient lieu, comme décidé pour 284. Une carte de compétence n'est
   pas équipable, l'écart est donc sans effet observable.

## 7. NON ÉTABLI

1. **Le libellé du geste.** Le mécanisme est établi (§2.2) : le 285 est émis par le handler
   `0x48f1f0`, sous-commande `3` de l'action ouverte par la clé de message `0x40c`. Aucun texte du
   domaine `skillcard` ne nomme cette action dans `db_string.rdb` (dump `strings -n 4` : seuls
   `tooltip_skillcard_*` et `static_common_skillcardicon` ressortent). La nature exacte du menu
   (menu contextuel d'objet, action de l'onglet « cartes »), et le libellé affiché, ne sont pas
   démontrables statiquement. Question précise : *quelle entrée d'IHM du client 7.3 appelle la
   sous-commande 3, et que lit le joueur à l'écran ?*
2. **Ce que le client fait du 286 seul.** Qu'il rafraîchisse la carte affichée est cohérent avec la
   trace `MSG_SKILLCARD_INFO`, mais on ne peut prouver qu'il recalcule les bonus de la compétence,
   ni qu'il se contente de retenir `target_handle`. Le même doute a été porté par la fiche 284
   (§7.2). Question précise : *le 286 suffit-il à remettre le client d'accord, ou faut-il
   l'accompagner d'un flux d'objet (`TS_SC_INVENTORY`) ?*
3. **Un éventuel refus local côté client.** On n'a pas établi que l'IHM grise l'action quand la
   carte n'est pas liée (elle le fait peut-être par sous-commande 2/3, ce qui expliquerait la
   coexistence des deux cas dans un même handler). Question précise : *le client peut-il émettre un
   285 sur une carte non liée, et si oui, voit-il le refus `AccessDenied` ?*
4. **Compétence introuvable.** Le comportement de la référence est observé (silence total,
   §5.1 asymétrie 3) ; celui du **client 7.3** dans cette situation n'est pas établi (le client
   n'ayant pas de raison d'afficher une carte liée sans compétence, le cas peut être inatteignable).
   La décision de §5.2 g est un choix documenté, pas une preuve.
5. **Données `Arcadia`.** Impossible de vérifier ici qu'il existe des ressources `item` de groupe
   10 (`Skillcard`) portant un `SkillId` non nul (`ItemResourceEntity.cs:13, 88`) : cela demande la
   base `Arcadia`, hors de ce VPS. Conséquence : le jugement de groupe repose sur le catalogue
   (correct) mais son taux de succès réel n'est pas mesuré.
6. **Sémantique du socket 0.** La convention « socket 0 = `Id` du porteur » vient de la fiche 284,
   pas d'une source de référence ; elle doit être confirmée par la revue de la MR 284. Le jour où
   une autre fonctionnalité écrirait dans le socket 0, le jugement « non lié » de §5.2 e deviendrait
   faux. Idem pour le socket 1 (invocation) que `SetBindTarget(nullptr)` remet à zéro (§6.5).
7. **Réassemblage de l'écriture.** Le cycle 284 écrit l'`Id` du porteur dans le socket 0 : cette
   écriture, son passage par `_databaseGate` et sa persistance ne sont pas sur `master` — la fiche
   285 ne peut donc pas confirmer le comportement sous concurrence, seulement le prescrire (§5.2 f).
8. **Trame 285 non conforme.** Le dépôt n'a pas de politique documentée de vérification de `Length`
   à la réception en deçà du garde de lecture ; le choix de §5.2 b (exiger 15 octets, refuser en
   dessous) suit le précédent du 253/200 mais n'est pas démontré par une source de référence.
9. **Checksum entrant.** Que le client 7.3 vérifie le checksum des trames **serveur** (donc celui de
   notre 286) n'est pas établi ; le dépôt l'écrit via `WriteChecksum` sans autre vérification.

## 8. Commits épinglés

| Référence | Commit | Usage dans cette fiche |
| --- | --- | --- |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | autorité sur les tailles, l'ordre des champs et le gating : `librzu/src/packets/GameClient/TS_CS_UNBIND_SKILLCARD.h`, `TS_CS_BIND_SKILLCARD.h`, `TS_SC_SKILLCARD_INFO.h`, `librzu/src/lib/Packet/GameTypes.h:40`, `PacketEpics.h:59,96` |
| NGemity / Chihiro | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | autorité sur la logique (EPIC `4_1_1`) : `WorldSession.cpp:1703-1720` et `:134`, `WorldSession.h:101`, `Unit.cpp:2614-2622`, `Item.cpp:292-295` et `:314-331`, `Item.h:140`, `Messages.cpp:1010-1016`, `TS_MESSAGE.h:54-70`, `shared/Server/Packets/GameClient/TS_CS_UNBIND_SKILLCARD.h` |
| Navislamia | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (`master`, HEAD relevé au réveil du 21/09/2026) | base de la branche ; toutes les lignes du dépôt citées ici sont lues sur ce commit |
| Corpus client 7.3 | **pas de SHA** (dépôt non git) | `reference/client73/SFrame.exe`, sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` ; lecture statique par `objdump -d -M intel` (VAs `0x676fba`, `0x48c6f0`, `0x48c740`, `0x48f1f0`, `0x49e21d`, `0x49e98c`, `0x49ea50`) et `strings -n 4` ; **aucune** exécution du client ni de Lua |
| Fiche 284 | `docs/packet-specs/284-bind-skillcard.md`, branche `hermes/packet-284-bind-skillcard` (MR #23, non mergée) | jumeau montant : décisions reprises en §5.2 a/c/f et §5.3 |

## 9. Bloc destiné à `CLAUDE.md` (proposition — à porter par la description de la MR, jamais écrit par l'archéologue)

> **Cartes de compétence (284/285/286).** `TM_CS_BIND_SKILLCARD` (284) et `TM_CS_UNBIND_SKILLCARD`
> (285) font **15 octets** (`item_handle` @7, `target_handle` @11), `TM_SC_SKILLCARD_INFO` (286) a la
> même forme et la même taille. Les trois ids sont gatés : 284/285/286 avant `EPIC_9_6_3`, 1284/1285/
> 1286 après — Epic 7.3 sert donc les ids courts (`librzu/src/lib/Packet/PacketEpics.h:96`).
> L'état « carte liée » **n'est pas** dans `m_hBindedTarget` de NGemity : chez nous il vit dans
> `ItemEntity.SocketItemIds[0]` (l'`Id` du porteur, 0 = non liée), et le socket 1 (invocation) n'est
> pas utilisé. Le succès s'annonce **uniquement** par 286 à `target_handle` nul ; les refus passent
> par `TS_SC_RESULT` (NotExist/NotActable/AccessDenied/DBError/InvalidArgument) sans jamais de
> `TS_SC_RESULT` de succès. Piège : NGemity laisse l'objet lié et reste muet quand la compétence est
> introuvable ; ne pas porter ce silence.

## A VERIFIER PAR KILLIAN

Les points ci-dessous sont des décisions ou des réserves **vérifiables**, pas des suppositions. Ils
n'empêchent pas le développement du 285 sur cette branche.

1. **Le 286 est déclaré sur cette branche** (§5.3), alors que la branche 284 le déclare aussi : deux
   déclarations identiques de `TM_SC_SKILLCARD_INFO = 286` cohabiteront jusqu'à l'intégration. Règle
   proposée : le premier intégré garde, le second supprime son doublon — trois conflits triviaux
   (`GamePackets.cs`, `GameClient.cs`, `GameCharacterPackets.cs`). *À confirmer avant le merge.*
2. **L'écho 286 à cible nulle est le seul succès** (§5.2 e, §6.3) : aucun `TS_SC_RESULT(285,
   Success)`. C'est ce que fait la référence ; si le client 7.3 exigeait une acquisition générique,
   il faudrait l'ajouter. Non démontrable ici.
3. **Écart de comportement sur compétence introuvable** (§5.2 g, §6.4) : NGemity laisse la carte
   liée et n'envoie rien ; Navislamia délie et envoie 286, faute de modèle d'enhance de compétence
   sur `master`. *Arbitrage demandé.*
4. **Convention de socket 0** héritée de la fiche 284 (§7.6) : elle doit être entérinée par la revue
   de la MR 284, dont le code n'est pas encore sur `master`.
5. **Sockets 0 et 1** (§6.5) : la référence remet les deux à zéro ; nous n'écrivons que le socket 0,
   le socle « invocations » (carte dg6sVB6H, MR #10) étant absent de `master`.
6. **Jugements non gatés** : ressource d'objet inconnue du catalogue (§5.2 e) et libellé du geste
   (§7.1) restent ouverts sans bloquer le développement.
7. **Données `Arcadia`** (§7.5) : l'existence de cartes de compétence réelles (groupe 10 avec
   `SkillId`) ne peut être vérifiée que sur la base de données du serveur.
8. **Correction apportée à la fiche 284** (§2.2) : le geste d'émission est désormais établi pour
   **284 et 285 ensemble** (deux sous-commandes d'un même handler). Le §7.1 de la fiche 284 peut être
   clos à ce titre lors de la revue de sa MR.

## Note de livraison

- Branche : `hermes/packet-285-unbind-skillcard`, créée depuis `master`
  `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (dépôt propre, `git log --oneline origin/master..master`
  vide avant et après les commits).
- Commits : `0f33ffc` (« Document TM_CS_UNBIND_SKILLCARD (285) in its packet spec ») puis celui qui
  porte les renvois à l'implémentation 284 livrée (§5.2 e/f, §5.3, §5.2 h).
- Aucun fichier de code touché : cette fiche est le seul livrable de `navis-ref`.
- `master` n'est pas modifié, aucune autre branche créée, aucun rebase ni merge ; aucune exécution du
  client, de Lua ou d'un script du client (lecture statique par `objdump` et `strings` uniquement).
- Contrôle de non-régression sur cette branche (changement documentaire uniquement) :
  `dotnet build Navislamia.sln -c Debug` → **0 erreur** (160 avertissements préexistants) ;
  `dotnet test Tests/Tests.csproj` → **448 réussis, 0 échec**
  (`NUGET_PACKAGES=/srv/navislamia/.nuget-cache`). C'est le plancher réel du dépôt à ce commit ; le
  critère 2 en exige 366 au minimum, il ne doit jamais baisser.
- Réserve principale : le sort du 286 sur cette branche (§5.3) — décision tranchée ici, intégration à
  confirmer avant le merge de la MR 284.
