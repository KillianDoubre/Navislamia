# 102 — `TM_SC_CANT_ATTACK` (`TS_SC_CANT_ATTACK`) : le refus d'attaque

Fiche d'archéologie produite par `navis-ref` sur `hermes/packet-102-cant-attack`, depuis `master` = `0fa5b8c`,
pour la carte Trello `qgcFACXs` « Correction : TM_SC_CANT_ATTACK (102) pour flèches manquantes et cible non
attaquable » (board `navislamia`, `owmwrG7O`). Elle ne code rien : elle fixe la trame, les codes officiels et
le sous-ensemble minimal à implémenter.

## 1. Identité

| | |
|---|---|
| id | **102** |
| nom | `TM_SC_CANT_ATTACK` (`op_codes.md:45`), structure `TS_SC_CANT_ATTACK` |
| sens | serveur → client, uniquement |
| enum C# | `GamePackets.TM_SC_CANT_ATTACK = 102` (`Game/Network/Packets/Enums/GamePackets.cs:16`) |
| générateur | `GameStateResultPackets.CantAttack(attacker, target, reason)` (`GameStateResultPackets.cs:40-47`), `CantAttackSize = 19` (`:14`) |
| rôle | dire au client *pourquoi* l'attaque demandée n'a pas lieu ; le client abandonne sa visée et, pour un seul code, affiche son propre message (§5.5) |

La trame **existe déjà** : il n'y a aucun paquet à écrire, seule la décision de l'envoyer manque. Une trame
entrante 102 est déjà refusée et journalisée (`Game/Network/Clients/GameClient.cs:3004-3008`).

La demande qui la précède est `TM_CS_ATTACK_REQUEST` = 100, 15 octets (`handle` u32 @7, `target_handle` u32
@11 ; `rzu/librzu/src/packets/GameClient/TS_CS_ATTACK_REQUEST.h:7-14`), lue par
`GameClient.HandleAttackRequest` (`GameClient.cs:1106-1124`) et remise à `CombatService.StartAttack`
(`CombatService.cs:105-139`).

## 2. Ce que le joueur fait pour que le client l'envoie

- Le joueur clique une cible avec une arme (ou maintient la visée) : le client envoie 100. Le serveur répond
  102 quand il refuse.
- La demande est **rejouée** tant que la visée dure : l'officiel mesure l'écart entre deux demandes de la même
  créature (`onAttackRequest` serveur officiel `0x140110a43-0x140110a67` puis `0x140110b1b-0x140110b2d`) et
  journalise (message `0x3d2` = 978, chaînes `0x140403848` / `0x140403858`) quand l'écart dépasse 1000 ms et
  qu'un drapeau de configuration (`0x14052b918`) est armé (`0x140110b53-0x140110b81`). La cible n'est donc pas
  supposée stable d'une demande à l'autre — c'est exactement ce que le refus 102 doit gérer.
- Côté client, la visée à l'arc a ses propres animations (`rcfx_arrowattack_*`, chaînes de
  `reference/client73/SFrame.exe`) : rien de tout cela n'est du ressort du serveur.

## 3. Structure sur le fil

Little-endian partout ; la trame porte l'en-tête commun du dépôt (u32 `length`, u16 `id`, u8 `checksum`).

| offset | type | nom | valeur / sens | source |
|---|---|---|---|---|
| 0 | u32 | `length` | **19** (`0x13`) | officiel `SendCantAttackMsg` `0x14014d458` (`mov DWORD PTR [rsp+0x20],0x13`) ; `GameStateResultPackets.cs:52` |
| 4 | u16 | `id` | **102** (`0x66`) | officiel `0x14014d451` (`mov WORD PTR [rsp+0x24],0x66`) ; `op_codes.md:45` |
| 6 | u8 | `checksum` | somme des six premiers octets | `GameStateResultPackets.cs:54` (`Frame`) |
| 7 | u32 | `attacker_handle` | handle de la créature qui a demandé (`ar_handle_t` = 4 o) | `rzu .../TS_SC_CANT_ATTACK.h:8` ; officiel `0x14014d460` (`mov DWORD PTR [rsp+0x27],edx`) ; NGemity `Messages.cpp:551` ; client 7.3 `0x66edc1` (`mov edx,[ecx+0x7]`) |
| 11 | u32 | `target_handle` | handle de la cible visée | `rzu ...:9` ; officiel `0x14014d464` (`mov DWORD PTR [rsp+0x2b],r8d`) ; NGemity `Messages.cpp:552` ; client `0x66edc7` |
| 15 | i32 | `reason` | code `TS_RESULT_*`, **entier signé de 4 octets** (pas un u16) | `rzu ...:10` (`int32_t`) ; officiel `0x14014d469` (`mov DWORD PTR [rsp+0x2f],r9d`) ; NGemity `Messages.cpp:553` ; client `0x66edcd` → `[eax+0x1b]` |

`ar_handle_t` est un `strong_typedef<..., uint32_t>` (`rzu .../lib/Packet/GameTypes.h:40`) et se sérialise
comme un `uint32_t` : 4 octets, sans en-tête ni longueur propre.

**Taille totale attendue : 19 octets** (7 + 4 + 4 + 4). Trois sources indépendantes l'écrivent :
le constructeur officiel (`0x13` en dur, `0x14014d458`), le client 7.3 (il réserve `0x1f` octets pour son objet
`SMSG_CANT_ATTACK` et y écrit la longueur `0x13` à `0x66eda4`) et le client de décembre 2011 avec PDB
(`SGameVM::onCantAttack`, `0x661780`).

## 4. Gating de version — statué pour le client 7.3

`rzu/librzu/src/packets/GameClient/TS_SC_CANT_ATTACK.h:12-14` :

```
X(102,  version <  EPIC_9_6_3)
X(1102, version >= EPIC_9_6_3)
```

Le client 7.3 vaut `EPIC_7_3 = 0x070300` (`rzu .../lib/Packet/PacketEpics.h:59`) et la bascule est
`EPIC_9_6_3 = 0x090603` (`:96`). **7.3 < EPIC_9_6_3 → id 102** : tranché, sans ambiguïté. Corroboré par deux
binaires de l'époque : le serveur officiel écrit `0x66` en dur (`0x14014d451`) et le client 7.3 route cette
trame vers son gestionnaire `SMSG_CANT_ATTACK` (`0x66ed80`, RTTI `.?AUSMSG_CANT_ATTACK@@`). Même bascule et
même conclusion pour la demande (100 en 7.3, 1100 au-delà : `TS_CS_ATTACK_REQUEST.h:11-13`).

Aucun champ n'est gaté par version : les trois champs sont des `_(simple)` sans variante, et l'enregistrement
déclaré (`TS_SC_CANT_ATTACK.h:7-10`) est identique à celui du serveur officiel de 2012. **Il n'y a donc aucun
`NON ÉTABLI` de version dans ce paquet.**

## 5. Traitement attendu

### 5.1 Le constructeur officiel

`SendCantAttackMsg(StructPlayer *, uint handle, uint target, int reason)` —
`epic7part4/server/2012-11/CaptainHerlockServer.exe`, `0x14014d430`. Il remplit la trame depuis la pile
(longueur `0x13` à `[rsp+0x20]`, id `0x66` à `[rsp+0x24]`, puis les trois champs à `[rsp+0x27]`,
`[rsp+0x2b]`, `[rsp+0x2f]`) et n'appelle l'envoi que si deux prédicats de la cible passent
(`call [rax+0x30]` puis `call [rax+0x18]` sur `[rbx+0x23d0]`, `0x14014d44e-0x14014d49c`) : inutile de
construire la trame pour un client qui n'est plus en jeu.

### 5.2 La décision, dans `onAttackRequest` (officiel `0x140110970`)

Sept étapes, **dans cet ordre exact**. L'ordre et la présence d'un `EndAttack` (vtable `+0x190`) changent le
paquet envoyé :

| # | condition officielle | ce qui part | adresse |
|---|---|---|---|
| 0 | `[player+0x110] == 0` → retour muet | rien | `0x140110999` |
| 1 | `attacker_handle` (`+7`) ≠ `GetHandle()` : `StructPlayer::GetSummon(uint)` (`0x1400c6da0`) ne rend rien | **3** `NOT_OWN` | `0x1401109c0-0x1401109ce` |
| 2 | `target_handle` (`+0xb`) nul : annule la cible courante (`slot +0x188` si `[player+0xd44] != 0`) | rien | `0x140110a43`, `0x140110a67` |
| 3 | `StructCreature::get(uint)` (`0x140081fb0`) ne trouve pas la cible : `EndAttack` **si** une cible était tenue, sinon | **1** `NOT_EXIST` | `0x140110b05-0x140110b15` |
| 4 | `StructPlayer::IsEnemy(cible, false)` (vtable `+0x230`) faux : idem | **5** `NOT_ACTABLE` | `0x140110bb8-0x140110bc8` |
| 5 | `StructPlayer::IsAttackable()` (vtable `+0x1f8`) faux — **envoyé sans condition** | **6** `ACCESS_DENIED` | `0x140110bff-0x140110c0f` |
| 6 | `IsUsingBow` (`+0x128`) ou `IsUsingCrossBow` (`+0x130`), prédicat `+0x30` vrai, et `GetBulletCount()` (`+0x138`) **< 1** — envoyé sans condition | **32** `NOT_ENOUGH_BULLET` | `0x140110c75-0x140110c85` |
| 7 | sinon `StartAttack(target, true)` (vtable `+0x180`) | rien (l'attaque commence) | `0x140110cd1` |

Les codes sont ceux de `rzu/librzu/src/packets/PacketEnums.h:5-37` : `NOT_EXIST = 0x1`, `TOO_FAR = 0x2`,
`NOT_OWN = 0x3`, `NOT_ACTABLE = 0x5`, `ACCESS_DENIED = 0x6`, `NOT_ENOUGH_BULLET = 0x20` (les mêmes valeurs
sont dans `Game/Network/Packets/ResultCode.cs:8-42`). Les étapes 3 et 4 utilisent `EndAttack` **à la place** du
paquet quand une cible était déjà tenue ; les étapes 1, 5 et 6 envoient toujours, sans `EndAttack`.

`StructPlayer::IsAttackable()` (`0x1400dad30`) est un agrégat :
`[this+0x2c2c] == 0` et `[this+0x2c28] == 0` et `IsActable()` et `!IsSitDown()` (vtable `+0x158`) et
`!StructCreature::IsUsingSkill()` (`0x140080aa0`) et **bit 14** de `[this+0xc34]` armé.
`StructPlayer::IsActable()` (`0x1400dac70`) : `[this+0x25b8] == 0`, `[this+0x110] != 0`, bit 20 (`0x14`) de
`[this+0xc34]` à zéro, et au moins un des bits 13 à 17 (`0xd`-`0x11`) armé.
`StructPlayer::IsEnemy` (`0x1400e3210`) lit d'autres bits du même mot (`0xa`, `0x12` de `[+0xc34]`), la
valeur `[+0x2554]` et `StructPlayer::IsInStartedCompete` (`0x1400d8260`) : **il ne dépend pas du bit
d'attaquabilité**, donc une cible immortelle peut être une ennemie — et c'est alors l'étape 5 (code 6) qui la
refuse, pas l'étape 4.

### 5.3 Pendant le combat (`StructCreature::processAttack`, officiel `0x1400a2500`)

- Flèches épuisées en pleine volée : `GetBulletCount() < 1` → `EndAttack` (`slot +0x190`) et retour,
  **sans 102** (`0x1400a25bd-0x1400a25d5`). Il n'y a donc rien à afficher au client à cet instant : le
  102 du code 32 ne part qu'à la **demande** d'attaque (§5.2, étape 6).
- Cible disparue en pleine volée : `EndAttack` puis **code 1** (`mov r9d,0x1` à `0x1400a2a08`, envoi
  `0x1400a2a18` ; même chose à `0x1400a2a3d` / `0x1400a2a4d`).
- La flèche consommée est retirée dans le même corps (`StructPlayer::EraseBullet`, `0x1400c52d0`, appelée à
  `0x1400a2e08`) : le dépôt le fait déjà (`CombatService.ConsumeArrow`, `CombatService.cs:384`).

### 5.4 Hors de portée (crochet `onCantAttack`)

`StructPlayer::onCantAttack` (`0x1400c3820`) et `StructSummon::onCantAttack` (`0x140106640`) envoient le
**code 2** `TOO_FAR`, avec un anti-rebond (`[this+0x2594] + 100`, `0x1400c3840-0x1400c3868` ; le serviteur
écrit au nom de son maître, `Messages::SendCantAttackMessage(GetMaster(), ...)`). NGemity porte exactement ces
deux envois (`Player.cpp:1620`, `Summon.cpp:539`). Aucun n'était dans le périmètre de la carte ; **celui de
l'invocation est porté depuis le 2026-10-06** (§11).

### 5.5 Ce que le client 7.3 en fait

`reference/client73/SFrame.exe` (`sha256 41e0af2e…`) :

- Le gestionnaire `0x66ed80` alloue un objet `SMSG_CANT_ATTACK` (RTTI `.?AUSMSG_CANT_ATTACK@@`,
  vtable `0xa51f08`), y écrit la longueur `0x13` (`0x66eda4`), un type d'évènement `14` (`0x66ed96`) puis les
  trois champs de la trame : `+7` → `[eax+0x13]`, `+11` → `[eax+0x17]`, `+15` → `[eax+0x1b]`
  (`0x66edc1-0x66edd3`), et l'empile (`0x64d0e0`). Le paquet est donc **lu jusqu'à l'octet 18 inclus**.
- Le consommateur est le `case` `MSG_CANT_ATTACK` de `SGameInterface` (`0x63c98a-0x63c9b6`) :
  - `reason == 0x20` (32) → le client lève **son propre message d'interface n° 334 (`0x14e`)**
    (`push 0x14e`, `0x63c990-0x63c9a0`) ;
  - tous les autres codes → le `case` marque seulement l'évènement traité
    (`mov BYTE PTR [esi+0x8],1`, `0x63c9ad`) et journalise (`SGameInterface - MSG_CANT_ATTACK`, `0x63c9a5`).
- Le client interprète bien `reason` comme un code `TS_RESULT` : son binaire porte une table de pointeurs
  `.data:0xc10000` indexée par le code, vers les noms `RESULT_*`. Deux calages le confirment :
  `RESULT_ACCESS_DENIED` y est en `0xc10018` (index 6) et `RESULT_NOT_ENOUGH_BULLET` en `0xc10080` (index 32).

Conséquence à écrire telle quelle dans le compte rendu : **le seul code dont un message client est prouvé est
32.** Les codes 1, 3, 5 et 6 ne déclenchent, dans ce `case`, qu'un marquage interne. Le critère « le client
affiche son message » de la carte `qgcFACXs` ne vaut donc que pour la flèche manquante.

### 5.6 Ce que Navislamia doit changer — état actuel et sous-ensemble minimal

État actuel, relevé ligne à ligne :

- `GameClient.HandleAttackRequest` (`GameClient.cs:1106-1124`) : `attacker` lu à `+7`, `target` à `+11` ;
  `NotOwn` si l'attaquant n'est ni le personnage ni un de ses serviteurs (`:1121`) ; `target == 0` →
  `StopAttack` (`:1122`). **Conforme à l'officiel** (étapes 1 et 2 de §5.2).
- `CombatService.StartPlayerAttack` (`CombatService.cs:641`) : cible introuvable → `NotExist` (`:651`),
  cible morte ou non ennemie → `NotActable` (`:653`). **Conforme aux étapes 3 et 4.**
- `CombatService.ProcessSwing` (`:209`) : tir à l'arc sans flèche → `StopAttack` muet (`:255-263`), flèche
  consommée à `:281`. **Conforme à l'officiel** (§5.3) : rien à ajouter là.
- `CombatService.CantAttackOrEnd` (`:634-639`) : session active → `StopAttack` muet, sinon envoi. C'est
  exactement la règle des étapes 3 et 4 (le `EndAttack` **à la place** du paquet) — mais **pas** celle des
  étapes 1, 5 et 6, qui envoient toujours. Les deux nouveaux codes ne doivent donc pas passer par la branche
  « session active » de cet utilitaire.
- **Il manque exactement les étapes 5 et 6** : `ACCESS_DENIED` (6) et `NOT_ENOUGH_BULLET` (32).
  Les codes existent déjà : `ResultCode.AccessDenied = 6` (`ResultCode.cs:13`), `NotEnoughBullet = 32` (`:42`).

La trame est déjà documentée ailleurs et la fiche ne fait que la resserrer, sans la contredire :
`docs/packet-specs/socle-etats-periodiques-energie.md` §1 (ligne 12, 102 = 19 octets, `attacker_handle` @7,
`target_handle` @11, `reason` i32 @15) et `docs/packet-specs/inventaire-client-73.md:27` (« livré le
2026-10-05 ») ; la même fiche d'inventaire relève à la ligne 95 précisément ce que cette branche corrige :
« 102 n'est pas envoyé pour `NOT_ENOUGH_BULLET` (flèches) ni `ACCESS_DENIED` (`IsAttackable`) ».
`docs/packet-specs/socle-mode-pk.md:371` réserve le seul code PK (`TS_RESULT_PK_LIMIT = 0x1D` = 29) — cette
fiche ne le touche pas.

**Cas A — flèches manquantes (code 32) : implémentable sans arbitrage.** Au début de
`CombatService.StartAttack` (`:105-139`), avec les tests déjà écrits pour la volée
(`Combat.AttackMechanics.IsRanged`, `AttackMechanics.cs:59` ; flèches = `info.LeftHand` dont `WeaponType` est
nul, `ConnectionInfo.cs:36`, `Game/Services/Combat/LeftHandItem.cs:11-14`) : si l'arme est un arc ou une
arbalète et que la réserve est absente ou à zéro, envoyer 102 avec le code 32 **et ne pas ouvrir de session**.
Ce test vient **après** les refus de cible (étapes 4 et 5) : une cible refusée garde son propre code, même
sans flèches.

**Cas B — cible non attaquable (code 6).** La règle est établie (code 6, étape 5, après le test d'ennemi et
avant celui des flèches, envoyé sans `EndAttack`). Ce qui reste à trancher est la **liste des états locaux**
qui valent « non attaquable », parce que `IsAttackable()` est un agrégat dont trois termes n'ont pas de
portage mesurable ici : les deux drapeaux `[+0x2c2c]` et `[+0x2c28]` et les bits de `[+0xc34]` n'ont pas de
nom dans le PDB, et `IsUsingSkill()` n'a pas d'état correspondant dans le modèle local. Les termes qui ont un
portage direct sont `IsSitDown` → `ConnectionInfo.IsSitting` (`ConnectionInfo.cs:427`) et le drapeau
d'invulnérabilité → `ConnectionInfo.IsImmortal` (`:435`, armé par `/immortal`, `GmCommandService.cs:365`).

Attention à l'ordre, qui est le piège du cas B : `IsImmortal` est aujourd'hui **dans** `ArePlayerEnemies`
(`CombatService.cs:597`), donc une cible immortelle part en code 5 (`:653`). L'officiel, lui, la refuse en 6
(« ennemie, mais non attaquable », §5.2) : porter le code 6 demande de sortir `IsImmortal` du test d'ennemi.
Même remarque pour la cible **morte**, que `:653` répond en 5 alors que l'officiel la refuse par
`!IsActable()` donc en 6 : ce déplacement-là n'est pas mesuré (on ne sait pas ce que vaut `[+0x110]` sur un
cadavre) et va en `## A VERIFIER PAR KILLIAN`.

### 5.7 Tests exigés de la branche

- Test d'offsets (taille totale et position de chaque champ) : il existe déjà et il est le bon gabarit —
  `Tests/Game/StateEnergyTests.cs:48-66` (`Energy_and_cant_attack_match_the_client_layouts`) vérifie 19,
  l'id 102 à `@4`, les ancres `@7` / `@11` et le `reason` **i32** à `@15`. L'étendre à un cas par code ajouté
  (32 et, si Killian tranche, 6) plutôt que d'écrire un second test.
- Un test par code de refus ajouté, comme le demande la carte `qgcFACXs` : pour A, une demande d'attaque à
  l'arc sans flèches doit produire une trame 102/19 avec `reason = 32` **et** ne pas ouvrir de session ; pour
  B, une cible refusée doit produire `reason = 6` (et la cible non ennemie rester en 5).
- Les tests sont en NUnit (`[Test]`, `[TestCase]`, FluentAssertions).

## 6. Écarts assumés avec NGemity

NGemity est ici **en retard sur l'officiel**, et une partie de son code est à ne pas porter.

- **Trame** : identique. `shared/Server/Packets/GameClient/TS_SC_CANT_ATTACK.h:6-11` déclare
  `uint32_t attacker_handle`, `uint32_t target_handle`, `int32_t reason`, et `ClientPackets.h:64` fixe 102 sans
  gating de version — cohérent avec `rzu` pour la branche `< EPIC_9_6_3`, la sienne étant `EPIC_4_1_1`
  (`shared/Common/Define.h:25`). L'envoi (`Messages.cpp:545-555`) remplit les trois champs dans le même ordre.
- **Deux codes faux** dans `Chihiro/src/Network/GameNetwork/WorldSession.cpp:1158-1205` : le refus « pas un
  ennemi » (`:1195`) et le refus « arc sans flèches » (`:1200`) envoient tous deux `reason = 0`
  (`TS_RESULT_SUCCESS`), et prennent `pRecvPct->getReceivedId()` comme attaquant au lieu de
  `pRecvPct->handle`. L'officiel écrit **5** (`0x140110bb8`) et **32** (`0x140110c75`), et recopie le handle de
  la demande (`0x140110b0f`, `0x140110c7f`). C'est un bogue de NGemity : ne pas le porter.
- **Étape 5 absente** : NGemity n'a **aucun** `IsAttackable` / `ACCESS_DENIED` dans `onAttackRequest`
  (`WorldSession.cpp:1158-1205` s'arrête à `IsEnemy` puis au test des flèches). L'officiel l'a ; c'est
  précisément la moitié de la carte `qgcFACXs` que NGemity ne peut pas trancher seul.
- **Reste conforme** : `NOT_OWN` (`:1170`), `NOT_EXIST` (`:1186`), l'annulation quand `target_handle == 0`
  (`:1174-1178`) ; `Unit.cpp:596-637` envoie `NOT_EXIST` en pleine volée comme `processAttack` ; `Player.cpp:1620`
  et `Summon.cpp:539` envoient `TOO_FAR`. Un écart sans effet ici : `Player::onCantAttack` écrit
  `UNIT_LAST_SAVE_TIME` au lieu de `UNIT_LAST_CANT_ATTACK_TIME` (`Player.cpp:1619`) — bogue local à NGemity,
  l'officiel écrit bien `[this+0x2594]` (`0x1400c3851`).

## 7. `NON ÉTABLI`

1. **Noms et sens des drapeaux de `IsAttackable`** : `[player+0x2c2c]`, `[player+0x2c28]` et les bits 13-17 /
   14 / 20 du mot `[player+0xc34]` n'ont pas de nom dans le PDB de `CaptainHerlockServer.exe`. On connaît le
   calcul, pas la sémantique de jeu de chaque bit.
2. **Le prédicat de vtable `+0x30`** appelé avant `GetBulletCount()` (`0x140110c5c` dans `onAttackRequest`,
   `0x1400a25b0` dans `processAttack`, et dans `SendCantAttackMsg` `0x14014d44e`) : son symbole n'est pas
   unique (plusieurs fonctions repliées à la même adresse par ICF). Condition d'entrée du test des flèches,
   donc à ne pas ignorer dans un portage strict — mais son absence de nom empêche de dire ce qu'il teste.
3. **Ce que vaut `[player+0x110]` sur un cadavre** : décide si une cible morte doit être refusée en 6
   (`!IsActable`) comme le dit la lecture de §5.2, ou en 5 comme le fait le dépôt aujourd'hui (`:653`).
4. **Le drapeau de configuration `0x14052b918`** (fenêtre de 1000 ms entre deux demandes) et le message
   journalisé `0x3d2` = 978 : rôle non identifié (anti-flood ? trace de triche ?). Sans effet sur la trame.
5. **Le texte du message client n° 334 (`0x14e`)** affiché pour `reason = 32` : la table de ressources qui le
   porte n'a pas été identifiée (lecture seule, aucun script client exécuté).
6. **Le chemin des compétences** (`onSkillRequest` / `processSkill`) : non lu. S'il envoie lui aussi 102, ses
   codes ne sont pas décrits ici, et la carte `qgcFACXs` ne les demande pas.

## A VERIFIER PAR KILLIAN

Trois points, dans l'ordre où ils bloquent le dev.

1. **Cas B (code 6) — quels états locaux valent « non attaquable » ?** La règle officielle est établie
   (code 6, étape 5 de §5.2), mais `IsAttackable()` est un agrégat dont trois termes n'ont pas de portage
   mesurable (`NON ÉTABLI` 1 et 2). Les deux états locaux qui portent directement un sous-test nommé sont
   `ConnectionInfo.IsSitting` (`ConnectionInfo.cs:427`, contre `IsSitDown`) et `ConnectionInfo.IsImmortal`
   (`:435`, contre le bit d'attaquabilité). Questions : (a) `IsImmortal` doit-il sortir de
   `ArePlayerEnemies` (`CombatService.cs:597`) pour qu'une cible immortelle reçoive 6 au lieu de 5 ?
   (b) `IsSitting` doit-il, lui aussi, répondre 6 ? **Sans réponse, le dev n'implémente que le cas A et le
   dit dans la MR** (carte de suivi `t_cbd13429`, « ne code rien et n'invente rien »).
2. **Cible morte** : le dépôt répond 5 (`CombatService.cs:653`), la lecture de l'officiel donnerait 6
   (`!IsActable()`, `NON ÉTABLI` 3). Faut-il changer ce refus existant, ou le laisser tel quel pour ne pas
   élargir la correction ? Le déplacer n'est pas un sous-ensemble minimal : c'est un choix.
3. **Portée du sous-ensemble** : la carte vise `CombatService`. L'officiel applique pourtant les étapes 4 à 6 à
   l'unité **résolue** — joueur **ou serviteur** (`rbx` dans `onAttackRequest`) —, alors que le dépôt a un
   chemin séparé pour le serviteur (`CreatureService.SummonAttack`, `CreatureService.cs:2627`, atteint depuis
   `GameClient.cs:1116`). Ce chemin n'a pas été relu dans cette passe : faut-il que la correction le couvre ?

Pour information, sans arbitrage nécessaire : le message client n° 334 (`0x14e`) affiché pour le code 32 n'est
pas identifié (`NON ÉTABLI` 5) ; c'est le seul code pour lequel le client 7.3 affiche quelque chose (§5.5).

## 8. Commits épinglés des références

| référence | commit | usage |
|---|---|---|
| rzu (`reference/rzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | trame, ordre des champs, gating par version, valeurs `TS_RESULT_*` |
| NGemity (`reference/ngemity`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | portage lisible (et bogues à ne pas porter) |
| Navislamia (`master`, base de la branche) | `0fa5b8ca77d15d932fc797dbd81a1b169605e5a5` | ancrages du dépôt |
| `epic7part4/server/2012-11/CaptainHerlockServer.exe` | `sha256 83b54fe18f15601578e35bcad1832a5c254bb09f512ab6297217fa49d9dd3eac` | décision officielle (PDB concordant) |
| `reference/client73/SFrame.exe` | `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | ce que le client 7.3 fait de la trame |
| `epic7part4/client-pdb/2011-12-14-part4-design/SFrame_Release.exe` | `sha256 77329d514d44a763c1bd71b692348249511039c3567c9b6ab8276c5ae983bb0b` | recoupement du gestionnaire client (PDB) |

## 9. Livraison de la branche (`navis-dev`, `hermes/packet-102-cant-attack`)

**Périmètre livré : le cas A seul (code 32 `NOT_ENOUGH_BULLET`).** Le cas B (code 6 `ACCESS_DENIED`) n'est
**pas** implémenté : la liste des états locaux valant « non attaquable » est en arbitrage (voir
`## A VERIFIER PAR KILLIAN` point 1, `NON ÉTABLI` 1 à 3). Aucun champ ni code n'a été deviné.

- `Game/Services/Combat/AttackMechanics.cs:66` — `HasArrows(LeftHandItem)` : la réserve d'un arc
  (`GetBulletCount()`) est le `LeftHandItem` de la main gauche **et seulement** s'il ne porte pas d'arme
  (`WeaponType` non renseigné, groupe `Bullet`). `CombatService.ProcessSwing` (`:266`) et le nouveau refus
  lisent désormais la même règle : l'expression `LeftHand is not { WeaponType: null } arrows || arrows.Amount < 1`
  qui y vivait est remplacée par elle, sans changement de comportement.
- `Game/Services/CombatService.cs:657-671` — `RefuseWithoutBullets(client, targetHandle)` : si
  `AttackMechanics.IsRanged(info.EquippedWeapon)` est vrai et `HasArrows(info.LeftHand)` faux, envoie
  `GameStateResultPackets.CantAttack(info.CharacterHandle, targetHandle, ResultCode.NotEnoughBullet)` et
  rend `true`. Le paquet part **directement par `client.Connection.Send`**, jamais par `CantAttackOrEnd` :
  l'étape 6 de l'officiel n'a pas d'`EndAttack`, donc la trame part même si une session est tenue.
- Appelé aux deux endroits où une session peut s'ouvrir, et **après** les refus de cible de l'officiel :
  `StartAttack` (`:130`, après le contrôle « cible vivante » de l'étape 4/5 du monstre, avant
  `_sessions[client] = …`) et `StartPlayerAttack` (`:683`, après `NotExist` et `NotActable`). Un chemin de
  serviteur (`CreatureService.SummonAttack`) n'est pas couvert : voir la réserve 3.
- Le refus ne touche pas la réserve d'objets : il ne consomme ni ne déplace une flèche, et il n'ouvre ni ne
  ferme de session. En pleine volée, la règle de §5.3 reste celle du dépôt (`ProcessSwing` `:264-269` :
  réserve épuisée → `StopAttack` muet, aucun 102), conformément à l'officiel.

Tests ajoutés (14 cas, tous verts) :

- `Tests/Game/StateEnergyTests.cs:72` `The_cant_attack_frame_keeps_the_same_19_bytes_for_every_refusal_code`
  — offsets de la trame pour chaque code de refus envoyé par le dépôt : 19 octets, id 102 @4, `checksum`,
  `attacker_handle` @7, `target_handle` @11, `reason` **i32** @15 (3, 1, 5, 32).
- `Tests/Game/CombatMechanicsTests.cs:277` — arc/arbalète sans flèche (réserve absente **et** réserve à zéro)
  : une seule trame 102/19, `reason = 32`, aucun `attacker_handle` inventé (celui du personnage) et
  **aucune session ouverte** (`_sessions` vide, lu par réflexion comme les tests voisins).
- `:303` — contrôle inverse : les trois armes à distance avec une flèche, et une arme de mêlée sans rien,
  ouvrent bien la session et n'envoient aucun 102 (le refus ne déborde pas).
- `:317` — ordre officiel : cible morte (5 `NOT_ACTABLE`) et cible inconnue (1 `NOT_EXIST`) gardent leur
  code **même avec un carquois vide** ; c'est ce qui fixe la place du test après les étapes 3 à 5.
- `:339` — une session vivante qui redemande l'attaque avec un carquois vide reçoit le 102/32 **sans** que
  la session soit fermée (pas d'`EndAttack`), ce qui épingle la décision de ne pas passer par
  `CantAttackOrEnd`.
- `Tests/Game/PvpTests.cs:225` — même refus sur le chemin joueur/joueur (`StartPlayerAttack`) : trame
  102/19, `reason = 32`, et aucun dégât au tick suivant.

`dotnet build Navislamia.sln -c Debug` : 0 erreur. `dotnet test Tests/Tests.csproj` : 3791 passés,
0 échec, 0 ignoré. Enum et dispatch inchangés : 102 est déjà refusé et journalisé par la branche
« server-only » de `GameClient.Receive` (`GameClient.cs:3004-3008`), donc rien n'atteint le `switch` final.

## 10. Relecture du 2026-10-05 (Claude) — cas B livré

La source officielle 2015 (`Game/Message/GameMessage.cpp`, `onAttackRequest`) est lisible en clair et tranche les
points laissés ouverts par le désassemblage :

```
IsDead() → rien ; handle inconnu → 3 NOT_OWN ; cible absente → EndAttack ou 1 NOT_EXIST ;
!IsEnemy → EndAttack ou 5 NOT_ACTABLE ; (arc || arbalète) && IsPlayer() && GetBulletCount() < 1 → 32 ;
assis → StandUp() + BroadcastStatusMessage ; !IsAttackable() → 6 ACCESS_DENIED ; StartAttack.
```

- **Cas B porté** (`CombatService.RefuseNotAttackable`, `IsAttackable`, `AttackMechanics.BlocksAttack`) : un joueur
  assis se relève (500 à lui et ses observateurs) puis l'attaque continue ; `IsAttackable` est faux en monture
  (`StructPlayer::IsAttackable`), pendant une incantation (`IsUsingSkill`), pendant la chute de monture (9001), ou
  sous un état qui retire `STATUS_ATTACKABLE` (`CalculateStat.cpp:2679-2730, 2905-2978`) : `EF_MEZZ` (82) avec
  `value_0` ou `value_2`, `EF_TRANSFORMATION` (104) avec `value_2`, et les `EF_MISC` 13601, 6006, 6005, 999990,
  999991, 9007, 9001, 6012, 6019, 6009, 314113, 201085, 6016, 201084, plus la peur 6008 (`IsActable`). Réponse 102
  code **6**, envoyée quel que soit l'état de session, comme le 32.
- **Réponses aux questions** : (a) `IsImmortal` n'entre pas dans `IsAttackable` (c'est un drapeau de MJ sur les
  dégâts reçus) ; (b) être assis n'est pas un refus, on se relève ; (c) la cible morte reste 5 (refus d'`IsEnemy`) ;
  (d) **l'invocation n'est pas concernée par le refus de flèches** (`&& pAttacker->IsPlayer()`), donc
  `CreatureService.SummonAttack` n'a pas à le porter.
- La chute de monture refusait jusqu'ici en silence ; elle répond maintenant 6 comme l'officiel. Un attaquant mort
  reste ignoré sans réponse.
- Tests : `CombatMechanicsTests` (mezz `value_0`/`value_2`, mezz de déplacement seul accepté, transformation,
  étourdissement, monture, état `EF_MISC` hors liste accepté, peur, joueur assis qui se relève et attaque).

## Bloc pour CLAUDE.md

À recopier dans la description de la MR (le dev n'écrit pas `CLAUDE.md`) :

```markdown
### 102 `TM_SC_CANT_ATTACK` — trame et codes de refus

- Trame **19 octets** : `length` @0, id **102** @4, `checksum` @6, `attacker_handle` @7, `target_handle` @11,
  `reason` **i32** @15 (`GameStateResultPackets.CantAttack`). Aucun champ gaté par version : id 102 sous
  `EPIC_9_6_3` (donc pour 7.3), 1102 au-delà.
- La décision est `onAttackRequest` (serveur officiel `0x140110970`), dans cet ordre : **3** `NOT_OWN`
  (handle d'attaquant non résolu), **1** `NOT_EXIST` (cible introuvable), **5** `NOT_ACTABLE`
  (`IsEnemy` faux), **6** `ACCESS_DENIED` (`IsAttackable` faux), **32** `NOT_ENOUGH_BULLET`
  (arc/arbalète et `GetBulletCount() < 1`). **2** `TOO_FAR` vient du crochet `onCantAttack`.
  Aux étapes 1 et 5, un `EndAttack` **remplace** le paquet quand une cible était déjà tenue ; les
  étapes 3, 6 et 32 envoient toujours.
- En pleine volée (`StructCreature::processAttack` `0x1400a2500`), la dernière flèche tirée arrête
  l'attaque **en silence** : aucun 102.
- Client 7.3 : **seul `reason = 32`** lève son message d'interface n° 334 ; les autres codes ne sont que
  tracés (`case MSG_CANT_ATTACK`, `SFrame.exe 0x63c98a`).
- Ne pas porter NGemity (`WorldSession.cpp:1195,1200`) : il envoie `reason = 0` là où l'officiel écrit 5 et
  32, et n'a pas l'étape `IsAttackable`.
- Navislamia n'émet pour l'instant que 3, 1, 5 et 32 (`CombatService.RefuseWithoutBullets`) : l'étape
  `IsAttackable` (**6**) n'est pas portée, la liste des états locaux valant « non attaquable » restant à
  trancher. Le 32 part par `client.Connection.Send` et non par `CantAttackOrEnd`, donc il est émis même si
  une session est tenue ; il ne consomme aucune flèche.

Voir `docs/packet-specs/102-cant-attack.md`.
```


## 11. `TOO_FAR` d'une invocation : ce qui fait marcher la créature (2026-10-06)

Bug noté : « attaque créature et attaque créature groupée : la créature ne se déplace plus vers la cible, ou part
dans une autre direction, et n'attaque pas ». Le serveur officiel ne déplace **jamais** une invocation vers sa cible
(`processAttack` hors de portée → `onCantAttack`, `StructCreature.cpp:4159-4290` ; aucun `SetMove` d'approche dans
`StructSummon.cpp`) : c'est le client qui la fait marcher, et il le fait **sur la 102 `TOO_FAR`**.

Le §5.5 ne regardait que le consommateur d'interface. Il y en a un second, qui décide (build client du 2011-12-14 avec
sa PDB, `epic7part4/client-pdb/2011-12-14-part4-design`) :

- `SCommandSystem::ProcMsgAtStatic`, `case MSG_CANT_ATTACK` (`0x45fb28-0x45fb44`) : retrouve l'acteur
  `attacker_handle` et appelle sa méthode virtuelle `+0x258`, `OnNetInput` ;
- `SGameCreature::OnNetInput` (`0x6d21f0`) délègue à sa machine à états ; `SCreatureStateMachine::OnNetInput`
  (`0x6aab30`, `switch (type − 4)`, type 14 → `0x6aad2d`) : raison **5 ou 6** → rien ; raison **2 (`TOO_FAR`) avec
  une cible non nulle** → `SInputAttack(target, false)` donnée à la machine (`0x6aad71-0x6aad8e`) — la créature
  repart à l'attaque, donc chemine vers la cible et envoie ses `TM_CS_MOVE_REQUEST` ; toute autre raison → état
  réinitialisé (fin de l'attaque).

Le serveur n'envoyait aucun `TOO_FAR` : la créature restait sur place, ou gardait son état précédent (suivre le
maître), et le coup était repoussé de 200 ms sans fin. `CreatureService.ProcessSwings` envoie désormais, tant que
l'invocation est hors de portée, `CantAttack(summon, handle du monstre chez le maître, TooFar)` au maître, au plus une
fois par 100 ticks (`StructSummon::onCantAttack`, sans la garde « immobile » du joueur). Test :
`CreatureTests.A_summon_out_of_reach_tells_its_master_too_far_so_that_the_client_walks_it_in`.

`StructPlayer::onCantAttack` (même code, seulement quand le joueur ne marche pas) n'est pas porté : l'attaque du joueur
marche déjà, son client l'amenant à portée de lui-même. À reprendre si un joueur reste planté devant un monstre qui
s'éloigne.
