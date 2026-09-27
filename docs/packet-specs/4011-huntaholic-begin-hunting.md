# 4011 — `TM_CS_HUNTAHOLIC_BEGIN_HUNTING` (demander le départ de la chasse)

Fiche d'archéologie de protocole, rédigée avant toute implémentation. Elle ne modifie aucun fichier de
code du serveur. Paquet **client → serveur**, **7 octets**, **sans charge utile**.

*(Les numéros de ligne de `reference/rzu` et `reference/ngemity` renvoient à leurs commits du §8 ; les
adresses du client 7.3 sont des VMA de `SFrame.exe`, sha256 et taille au §8. Aucun exécutable, script
Lua ou `.nui` du client n'a été lancé : tout est obtenu par lecture de fichiers et désassemblage.)*

## Questions tranchées par cette fiche

1. **Id et nom** : `4011`, `TM_CS_HUNTAHOLIC_BEGIN_HUNTING` (`op_codes.md:237`).
2. **Taille** : **7 octets**, en-tête seul, **aucun champ** — tranché par trois sources indépendantes
   (rzu, NGemity, constructeur du client 7.3).
3. **Gating de version** : **aucun**. rzu écrit `X(4011, true)` (condition littérale) ; la famille
   n'existe qu'à partir d'`EPIC_6_3 = 0x060300`, or `EPIC_7_3 = 0x070300 > 0x060300`. Décision pour
   7.3 : **l'id est 4011, la taille 7, et il n'y a aucun champ à gater** (§4).
4. **Déclencheur client** : un **seul** émetteur dans le client 7.3 — le contrôle **`button_entrance_01`**
   de la fenêtre **`SUIHuntaHolicInstanceWnd`** (preuve RTTI, §2.2). Le contrôle voisin
   `button_entrance_02` de la **même** méthode envoie, lui, `4005` (§2.3).
5. **Aucune réponse n'existe pour 4011** : le répartiteur entrant du client envoie l'id vers sa branche
   « message non traité » (§5.2). Un serveur qui renverrait un 4011 serait journalisé comme inconnu.
6. **Ce qui suit côté serveur** : le déroulé `4012` → `4009` → `4007`/`4006` → `4010`, dont les
   **formats** sont établis (socle §3.4.6-3.4.8) mais dont **l'ordre et la cadence ne le sont pas**
   (§7b ici, socle §7k).
7. **Délimitation du lot `navis-dev`** : lecture + validation de la longueur exacte, journalisation,
   **aucun état modifié**, **aucune réponse** (§5.4). Ce que le serveur devra *émettre* un jour
   (4012/4009/4006/4007/4010) est hors de ce paquet.

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id décimal | `4011` | `op_codes.md:237` |
| Nom | `TM_CS_HUNTAHOLIC_BEGIN_HUNTING` | `op_codes.md:237` ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:11` ; `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:9` |
| Sens | client → serveur (`SessionPacketOrigin::Client`) | rzu `TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:11` |
| Id hexadécimal du client | `0x0FAB` | client `0x4c93e2` |
| Famille | HuntaHolic, 4000-4012 ; lot **S5** du socle (avec 4012 et 4009) | `docs/packet-specs/socle-instances-jeu.md:480` (§5.4) |
| Taille totale | **7 octets** | §3 |
| Tête de lecture dans Navislamia | `GamePackets` (aucun membre 4000-4012 présent sur `master` aujourd'hui) | `Game/Network/Packets/Enums/GamePackets.cs` |

Le socle de la famille est `docs/packet-specs/socle-instances-jeu.md` (commits et tableaux de tailles
de référence) ; la présente fiche ne le contredit nulle part et n'ajoute que ce qui concerne 4011.

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Le constructeur de trame `0x4c93d0` — 7 octets, id `0x0fab`

Aucune autre fonction du binaire ne construit ce paquet. Le corps complet (`objdump -d -M intel`) :

```asm
4c93d0: push ebp                       ; prologue
4c93d3: sub  esp,0x8
4c93d6: xor  eax,eax
4c93d8: mov  DWORD PTR [ebp-0x7],eax   ; tampon de 7 octets remis à zéro
4c93db: mov  WORD  PTR [ebp-0x3],ax
4c93df: mov  BYTE  PTR [ebp-0x1],al
4c93e2: mov  eax,0xfab                 ; id 4011
4c93e7: mov  WORD  PTR [ebp-0x3],ax    ; id écrit aux offsets 4-5
4c93eb: mov  DWORD PTR [ebp-0x7],0x7   ; Length = 7 en dur
4c93f2: lea  eax,[ebp-0x7]
4c93f5: xor  dl,dl
4c93f8: add  dl,BYTE PTR [eax]         ; somme des octets 0..5
4c93fa: inc  eax
4c93fb: lea  esi,[ebp-0x1]
4c93fe: cmp  eax,esi
4c9400: jne  0x4c93f8
4c9402: mov  ecx,DWORD PTR [ecx+0x20]  ; session de l'objet message
4c9405: mov  BYTE PTR [ebp-0x1],dl     ; somme mod 256 aux offsets 6
4c9408: lea  edx,[ebp-0x7]
4c940b: push edx
4c940c: call 0x4c7aa0                  ; envoi
```

Trois faits en sortent, sans interprétation :

* la trame fait **7 octets** et rien de plus : le seul `mov DWORD PTR [ebp-0x7],0x7` fixe la longueur
  et aucun accès ne dépasse l'offset 6 ;
* `0xfab` = **4011** est écrit aux offsets 4-5 (l'`mov WORD` en `0x4c93e7`) ;
* l'octet 6 est la **somme modulo 256 des octets 0 à 5** ; la boucle `0x4c93f8`-`0x4c9400` s'arrête
  sur `ebp-0x1`, donc elle somme les offsets 0 à 5 et écrit en 6 (`0x4c9405`).

**Unicité de l'émetteur, deux relevés indépendants :**

* `grep -n "0xfab" /tmp/sframe.asm` → **une seule ligne** dans tout le `.text` : `4c93e2`.
* `grep -n "call.*4c93d0" /tmp/sframe.asm` → **un seul site d'appel** : `0x564238`.

Le `ds:0xc16de4`/`ds:0xc16de8` du §2.2 sont des **pointeurs** statiques rangés en `.data` : les deux
dwords valent `0xa2c070` et `0xa2c05c`, qui sont les adresses des chaînes `button_entrance_01` et
`button_entrance_02` (lecture des sections par adressage VMA, §9).

### 2.2 Fenêtre `SUIHuntaHolicInstanceWnd`, contrôle `button_entrance_01` — appelant `0x564238`

Le site d'appel est dans la méthode `0x5641b0`, atteinte depuis la table virtuelle dont l'entrée
`0xa2c0ec` pointe sur `0x5641b0`. Cette table porte le RTTI de la classe :

| Preuve | Relevé |
|---|---|
| Pointeur de descripteur de classe (COL) précédant la vtable | `0xa2c0d0` → `0x00bc0c3c`, `pTypeDescriptor = 0x00c16e54` |
| Nom RTTI de la classe | `.?AVSUIHuntaHolicInstanceWnd@@` |
| Début de vtable | `0xa2c0d4` ; l'entrée `0xa2c0ec` (index 6) = `0x005641b0` |

La méthode est donc bien une entrée virtuelle de **`SUIHuntaHolicInstanceWnd`**, et `esi` (le `this`)
est cette fenêtre. Corps utile :

```asm
5641b0: push ebp / mov ebp,esp / push ebx            ; arg1 = ebx = nom du contrôle ([ebp+8])
5641b4: mov  ebx,DWORD PTR [ebp+0x8]
5641b9: mov  edi,DWORD PTR [ebp+0xc]                 ; arg2 = code d'événement
5641be: cmp  edi,0xa                                 ; un seul code traité : 10 (0x0a)
5641c1: jne  0x56426d                                ; sinon -> retour
5641c7: mov  eax,ds:0xc16de8                         ; -> "button_entrance_02"
5641cc: push eax / push ebx / call 0x977ff7          ; comparaison insensible à la casse
5641d6: test eax,eax / jne 0x56420d                  ; non -> essayer l'autre contrôle
        ... push 0xa1fa18 / call 0x48b760 / mov ecx,eax
5641f2: call 0x4c9380                                ; -> 4005 (TM_CS_HUNTAHOLIC_LEAVE_INSTANCE)
56420d: mov  edx,ds:0xc16de4                         ; -> "button_entrance_01"
564213: push edx / push ebx / call 0x977ff7
56421d: test eax,eax / jne 0x56426d
        ... push 0xa1fa18 / call 0x48b760 / mov ecx,eax
564238: call 0x4c93d0                                ; -> 4011 (cette fiche)
56423d: ... push ds:0xc16de4 / mov ecx,esi / call 0x79be10
56424b: test eax,eax / je 0x56426d
56425d: mov  edx,DWORD PTR [eax]
56425f: mov  BYTE PTR [eax+0x2a4],0x1
564268: mov  eax,DWORD PTR [edx+0x3c] / call eax
```

Traduction factuelle :

* la méthode ne traite **qu'un seul** code d'événement, **10 (`0xa`)**, sur le nom de contrôle reçu en
  premier argument ;
* `0x977ff7` est une comparaison de chaînes **insensible à la casse** : le chemin `0x978027` saute en
  `0x977efd`, qui abaisse `A`-`Z` en ajoutant `0x20` (`lea ecx,[eax-0x41]` / `cmp ecx,0x19` /
  `add eax,0x20`) avant de comparer octet par octet ;
* quand le nom vaut **`button_entrance_01`**, le client construit et envoie **4011** ;
* quand il vaut **`button_entrance_02`**, la **même** méthode envoie **4005** (branche `0x5641f2`,
  confirmée par `docs/packet-specs/4005-huntaholic-leave-instance.md` §2.2) ;
* `0x48b760` est la recherche de l'objet-message HuntaHolic par nom (`push 0xa1fa18` = chaîne
  `huntaholic`, `ecx = [0xc4b28c + 0x144]`, allocation de `0xf8` octets en cas d'absence en
  `0x48b789`) ; c'est son `+0x20` que le constructeur lit comme session (`0x4c9402`) ;
* **après** l'envoi, le client retrouve le contrôle `button_entrance_01` dans la fenêtre
  (`0x79be10` : parcours du conteneur `[this+0x44]`, comparaison du `std::string` en `+0x50`),
  écrit `1` à `[contrôle+0x2a4]` et appelle l'entrée virtuelle `[vtable+0x3c]` du contrôle. C'est un
  effet **local d'interface** : aucun paquet entrant n'y intervient (§7c).

**Résumé du geste** : le joueur, dans la fenêtre HuntaHolic de son instance, active le contrôle
`button_entrance_01` (événement 10) ; le client envoie un 4011 de 7 octets et désactive localement ce
contrôle en attendant la suite. Le **libellé affiché** de ce contrôle n'est pas établi (§7a).

### 2.3 Ce qui n'est pas un déclencheur

* **Aucun autre site d'appel** : le relevé `call 0x4c93d0` donne exactement un résultat, et le relevé
  de l'immédiat `0xfab` dans tout le `.text` en donne aussi un seul (`0x4c93e2`). Il n'existe donc ni
  seconde fenêtre, ni chemin de secours (scoreboard, lobby, compétence) qui enverrait 4011.
* **Le lobby n'envoie pas 4011** : `SUIHuntaHolicLobbyWnd` (`Create: SUIHuntaHolicLobbyWnd`, chaîne
  `0xa49898`) est la fenêtre des `4000`/`4004` (socle §2.3) ; le seul handler qui construit 4011 est
  celui de `SUIHuntaHolicInstanceWnd` (§2.2). La ligne du socle §2.3 qui dit « bouton [...] **du
  lobby** » est donc à lire comme « bouton de la fenêtre HuntaHolic » : la preuve RTTI tranche pour
  `InstanceWnd`.
* **4011 n'est jamais reçu** : voir §5.2 (branche « message non traité »).
* Aucun émetteur de 4011 n'a été trouvé côté serveur (Navislamia n'a pas l'id, §1) ni côté logique
  NGemity (§5.1).

## 3. Structure sur le fil — **7 octets**

En-tête de paquet de jeu identique à celui du reste de la famille (4 + 2 + 1 = 7, cf.
`Game/Network/Packets/Game/GameInstanceGamePackets.cs:18`) :

| Offset | Taille | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` LE | `Length` (de l'en-tête) | `7` en dur | client `0x4c93eb` (`mov DWORD PTR [ebp-0x7],0x7`) ; rzu `TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:11` → `CREATE_PACKET_VER_ID` → en-tête de **7** (`PacketDeclaration.h:616-618`) |
| 4 | 2 | `uint16` LE | `ID` | `0x0FAB` = 4011 | client `0x4c93e2` (`mov eax,0xfab`) + `0x4c93e7` ; `op_codes.md:237` ; rzu `TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:8-9` ; NGemity `ClientPackets.h:244` et `TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:9` |
| 6 | 1 | `uint8` | `Checksum` | somme des octets 0-5, modulo 256 | client `0x4c93f8`-`0x4c9405` (boucle sur `ebp-0x7` → `ebp-0x1`, écriture en `ebp-0x1`) |
| 7 | — | — | *(aucun champ)* | — | rzu `TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:5` : `#define TS_CS_HUNTAHOLIC_BEGIN_HUNTING_DEF(_)` **sans corps** ; NGemity `TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:6` : `..._DEF(_)` **sans corps** |

**Taille totale attendue : 7 octets.** Toute trame 4011 d'une autre longueur est malformée : le
client 7.3 n'a qu'un constructeur pour cet id et il écrit `Length = 7` sans jamais toucher l'offset 7.

**Réserves de méthode (assumées, pas contournées) :**

* Le **octet 6 (checksum) n'est pas vérifié** par Navislamia, ici comme pour toute la famille
  (`GameHuntaholicPackets.IsLeaveInstance` et `GameInstanceGamePackets.HasNoPayload` ne testent que la
  longueur). Écart connu du dépôt, pas une découverte de cette fiche.
* Les trois sources de la ligne « ID » donnent **le même** id ; aucune divergence à arbitrer, donc
  aucun risque de confusion d'id entre rzu, NGemity et le client pour ce paquet.

## 4. Gating de version

### 4.1 L'id : inconditionnel — décision pour 7.3 : **4011**

```c
// reference/rzu/librzu/src/packets/GameClient/TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h
5:  #define TS_CS_HUNTAHOLIC_BEGIN_HUNTING_DEF(_)          // liste de champs VIDE
7:  // Since EPIC_6_3
8:  #define TS_CS_HUNTAHOLIC_BEGIN_HUNTING_ID(X) \
9:      X(4011, true)
11: CREATE_PACKET_VER_ID(TS_CS_HUNTAHOLIC_BEGIN_HUNTING, SessionType::GameClient, SessionPacketOrigin::Client);
```

* Le second argument de `X(...)` est une **condition compilée** : `#define SERIALISATION_F_ID2(id_,
  condition_) if(condition_) id = id_;` (`PacketDeclaration.h:587-589`). Ici la condition est le
  littéral `true` : **l'id 4011 vaut pour toutes les versions**, aucune autre valeur n'est déclarée
  dans ce fichier (`grep -n "X(" → une seule entrée`).
* Le commentaire `// Since EPIC_6_3` (ligne 7) **n'est pas** du gating : il date l'apparition de la
  famille. `EPIC_6_3 = 0x060300` (`PacketEpics.h:56`) et `EPIC_7_3 = 0x070300` (`:59`), donc
  `EPIC_7_3 > EPIC_6_3` et l'id est **valide en 7.3**. Piège classique du dépôt : lire le commentaire
  comme un `>= EPIC_6_3` n'aurait ici aucune conséquence (7.3 est plus récent), mais la règle reste
  qu'un commentaire ne gate rien et que seul `X(<id>, <condition>)` décide.
* NGemity est d'accord : `CREATE_PACKET(TS_CS_HUNTAHOLIC_BEGIN_HUNTING, 4011)` avec le même
  commentaire (`shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:8-9`), et l'id
  figure à `shared/Server/ClientPackets.h:244`.

**Décision écrite pour Epic 7.3 : id = 4011, un seul id, aucune variante.**

### 4.2 Les champs : aucun — donc aucun gating à statuer

| Champ | Gating rzu | Décision pour 7.3 |
|---|---|---|
| *(aucun champ)* | `TS_CS_HUNTAHOLIC_BEGIN_HUNTING_DEF(_)` est vide : il n'y a ni `_(simple)`, ni `_(version)` | **rien à trancher, rien à exclure** — la trame est l'en-tête de 7 octets |

Contrôle : `grep -n "version\|EPIC" reference/rzu/librzu/src/packets/GameClient/TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h`
ne renvoie **que** le commentaire de la ligne 7, jamais une directive de champ. Aucun champ de cette
fiche ne peut donc rester non tranché au titre du critère transversal n° 6.

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` (NGemity) fait du paquet : **rien**

```
grep -rn "TS_CS_HUNTAHOLIC_BEGIN_HUNTING\|TS_SC_HUNTAHOLIC" reference/ngemity/Chihiro/src/
→ 0 résultat
```

L'id est **déclaré** (`ClientPackets.h:244`, en-tête vide) et jamais **traité** : ni `switch`, ni
handler, ni émission de 4009/4012. Le reste de la famille HuntaHolic dans `Chihiro` se limite à des
énumérations d'items, de monstres et de compétences (`ItemTemplate.hpp:84`, `MonsterBase.h:65-68`,
`Skill.cpp:1422-1423`), sans consommation de paquet. **Il n'y a donc aucune logique à porter**, et
rien à inventer à partir de NGemity pour 4011.

### 5.2 Le client 7.3 ne reçoit **jamais** un 4011 — preuve directe

Le répartiteur entrant du client classe les ids par plages. Le bloc qui contient 4011 commence en
`0x67e736` :

```asm
67e736: sub  eax,0xfa2                      ; base du bloc : 4002
67e73b: cmp  eax,0xfb                       ; 252 entrées : 4002 .. 4253 inclus
67e740: ja   0x67ef21                       ; hors plage -> défaut
67e746: movzx eax,BYTE PTR [eax+0x67f580]   ; table d'octets, 252 octets, une par id
67e74d: jmp  DWORD PTR [eax*4+0x67f560]     ; table de sauts, 8 dwords
```

Lectures des deux tables par adressage VMA (`file_off = 0x400 + (vma - 0x401000)`) :

| Index | Id | Octet | Cible |
|---|---|---|---|
| 0 | 4002 | 0 | `0x67e754` |
| 1 | 4003 | 7 | `0x67ef21` |
| 2 | 4004 | 7 | `0x67ef21` |
| 3 | 4005 | 7 | `0x67ef21` |
| 4 | 4006 | 1 | `0x67e761` |
| 5 | 4007 | 2 | `0x67e76e` |
| 6 | 4008 | 7 | `0x67ef21` |
| 7 | 4009 | 3 | `0x67e77b` |
| 8 | 4010 | 4 | `0x67e788` |
| **9** | **4011** | **7** | **`0x67ef21`** |
| 10 | 4012 | 5 | `0x67e795` |
| 251 | 4253 | 6 | `0x67e7a2` |

Tous les autres indices valent **7**. Or la cible 7 est `0x67ef21`, la **branche de défaut** : elle
affiche la chaîne `0xa53df0` = `처리되지 않은 메세지 : %d` (« message non traité : %d ») avec l'id.
Autrement dit :

* **aucune structure de rattrapage n'est installée pour 4011** dans le client, alors que ses voisins
  traités (4002, 4006, 4007, 4009, 4010, 4012, 4253) ont chacun leur branche ;
* **un 4011 émis par le serveur serait journalisé comme inconnu** : il n'existe aucun paquet de
  réponse à 4011 dans la famille, et il ne faut pas en inventer un.

C'est la même conclusion que celle déjà écrite pour 4005 et 4008 (socles S3), obtenue par la même
méthode : la table est lue, pas déduite.

### 5.3 Conduite serveur attendue

| Point | Attendu | Source / raison |
|---|---|---|
| Reconnaissance | l'id 4011 doit être **déclaré dans `GamePackets`** et **routé** dans la boucle de `OnDataReceived`, sinon il atteint le `throw` final (critère transversal n° 4) | `Game/Network/Clients/GameClient.cs` (dispatch existant, p. ex. `TM_CS_INSTANCE_GAME_EXIT` : 4251) ; `_ => throw new Exception($"Unknown Packet Type {header.ID}")` |
| Longueur | **7 exactement**. Toute autre longueur est malformée : journal d'avertissement, rien d'autre | constructeur client `0x4c93d0` (§2.1) ; patron déjà en place `GameInstanceGamePackets.HasNoPayload` |
| Charge utile | rien à lire : aucun `out`, aucun `record struct` de requête | §3 |
| Réponse immédiate | **aucune**. Le client ne peut pas interpréter un 4011 entrant (§5.2) et n'attend aucun accusé | §5.2 |
| Effets de jeu | **aucun dans ce lot** : Navislamia n'a ni lobby, ni instance, ni minuteur HuntaHolic (le socle S1 n'a livré que 4250-4253) | socle §5.3, §9 |
| Suite logique (hors lot) | le serveur émettra un jour `4012` (7 o), `4009` (11 o, `begin_time` `uint32`), `4007` (15 o), `4006` (48 o), `4010` (7 o) | socle §3.4.6-3.4.8 (formats) et §7k (ordre/cadence **non établis**) |
| Journalisation | une ligne `Debug` (patron de 4251/4252 dans `GameClient.cs`) | style du dépôt |

### 5.4 Contrat d'implémentation pour `navis-dev`

Périmètre : **le seul opcode 4011**. Les déroulés `4012`/`4009`/`4006`/`4007`/`4010` sont les lots
S5 (suite) et S6 du socle et ne font pas partie de ce paquet.

1. **`Game/Network/Packets/Enums/GamePackets.cs`** — ajouter
   `TM_CS_HUNTAHOLIC_BEGIN_HUNTING = 4011,` avec un commentaire citant cette fiche, la taille 7 et
   l'absence de gating. **Emplacement** : la famille HuntaHolic 4000-4012 est revendiquée par quatre
   branches sœurs (`hermes/packet-4000-huntaholic-instance-list`, `-4003-huntaholic-create-instance`,
   `-4004-huntaholic-join-instance`, `-4005-huntaholic-leave-instance`) qui ajoutent chacune leurs
   membres au même endroit ; pour rester hors de leur zone, placer le membre **en fin de bloc**
   (après `TM_CS_SECURITY_NO = 9005`, comme l'a fait la branche 4005) et non dans la plage
   4000-4012. L'énumération n'est pas triée par valeur, elle est groupée par famille. La branche
   sœur `-4008-huntaholic-leave-lobby` ne touche ni `GamePackets.cs` ni `GameClient.cs` (livrée sans
   code, cf. §5.6).
2. **`Game/Network/Packets/Game/GameHuntaholicPackets.cs`** — **fichier créé par les mêmes quatre
   branches sœurs** (`git diff --stat master...hermes/packet-4005-huntaholic-leave-instance`), avec
   la classe
   `public static class GameHuntaholicPackets` dans le namespace
   `Navislamia.Game.Network.Packets.Game`. Y ajouter, **dans la classe existante** : une constante
   `BeginHuntingLength = HeaderSize` (7) et un prédicat `IsBeginHunting(ReadOnlySpan<byte>)` qui
   n'accepte que la longueur exacte, sur le modèle de `IsLeaveInstance` / `HasNoPayload`. Ne pas
   créer une seconde classe de la famille, ne pas déplacer les membres existants.
   *Voir le relevé « hotspot » ci-dessous.*
3. **`Game/Network/Clients/GameClient.cs`** — dans la boucle de `OnDataReceived`, **avant** le
   `switch` final, une branche `if (header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_BEGIN_HUNTING) {
   HandleHuntaholicBeginHunting(msgBuffer); continue; }`, méthode qui vérifie
   `GameHuntaholicPackets.IsBeginHunting`, journalise en `Warning` si la longueur est fausse, sinon
   une ligne `Debug` (id, longueur, `ClientTag`), et **n'envoie rien**.
4. **Tests** — `Tests/Game/HuntaholicBeginHuntingPacketsTests.cs`, sur le patron de
   `Tests/Game/InstanceGamePacketsTests.cs` : `BeginHuntingLength == 7`, trame de 7 octets acceptée,
   6 et 8 octets refusées, id `4011` présent dans `GamePackets` **et** distinct des autres membres
   (pas de collision avec 4250-4253), et un test d'offsets du paquet **tel que le client l'écrit**
   (id aux offsets 4-5 = `0x0FAB`, longueur 0-3 = 7, somme des octets 0-5 = octet 6) construit à la
   main, sans exécuter de code client.
5. **Interdits du lot** : aucune réponse émise, aucune écriture dans `CLAUDE.md` (bloc à recopier
   dans la description de la MR, §10), aucun état HuntaHolic, aucune migration, aucun refactoring des
   paquets voisins, aucun commit sur `master`.

**Hotspot à signaler.** `Game/Network/Packets/Game/GameHuntaholicPackets.cs` est créé *de zéro* par
les quatre branches sœurs ci-dessus, chacune avec son propre `HeaderSize`, ses propres constantes et sa
propre classe : toute fusion de ces branches dans le même `master` produira un conflit sur ce fichier.
Ce n'est pas une raison d'éclater la famille, mais le dev doit (a) repartir de la version de
`GameHuntaholicPackets.cs` déjà en tête de sa branche parente, (b) n'ajouter que ses trois ou quatre
membres, (c) vérifier au `git merge-tree --write-tree --name-only` la liste des conflits avant de
pousser. Même remarque pour le point d'insertion dans `GamePackets.cs`.

### 5.5 Cas limites

* **Longueur ≠ 7** : malformé, refus propre (avertissement, aucune réponse). Un 4011 de 11 octets
  (confusion avec `4009`/`4250`) ou de 6 octets (tronqué) n'est pas un 4011.
* **Checksum faux** : non vérifié par le dépôt pour toute la famille ; le lot ne le vérifie pas non
  plus (écart assumé, §3).
* **4011 reçu par le serveur d'un client qui n'est dans aucune instance HuntaHolic** : le serveur n'a
  aujourd'hui aucun moyen de le savoir (pas d'état HuntaHolic), donc la branche de réception reste
  sans contrôle de contexte — à documenter dans le code comme une réserve, pas à combler par un
  refus inventé (§7c).
* **Répétition** : rien dans le client n'empêche un second envoi (`button_entrance_01` est remis à
  l'état 1 localement, mais un rechargement d'interface le réactive) ; le serveur ne doit ni
  mémoriser ni dédupliquer dans ce lot.

### 5.6 Les deux décisions que cette fiche doit trancher noir sur blanc

#### (a) Que fait le serveur d'une demande d'ouverture de chasse ?

**Il la lit, il ne répond rien.** Preuve de lecture, et non convention :

* aucun paquet entrant ne porte l'accusé de 4011 : la table d'octets du répartiteur entrant du client
  envoie l'index 9 (4011) sur la cible 7, la branche « message non traité » (§5.2). Il n'existe donc
  aucun paquet serveur → client dont le client ferait quelque chose en retour d'un 4011 ;
* les paquets descendants que le client **sait** recevoir (`4012` cible `0x67e795`, `4009` cible
  `0x67e77b`, `4007` cible `0x67e76e`, `4006` cible `0x67e761`, `4010` cible `0x67e788` — mêmes
  tables, §5.2) sont **autonomes** : leurs désérialiseurs se contentent de construire l'objet-message
  (allocation de 23 octets en `0x670a07` puis vtable `0xa52120` pour `4009` ; 19 octets en `0x670b65`
  puis vtable `0xa52128` pour `4012`) et de l'enfiler (`0x64d0e0` appelé avec `[esi+0x2c]`) : aucun
  d'eux ne consulte un état de chasse ni ne se rattache à un `4011` reçu — réserve faite du
  consommateur de la file, qui n'a pas été rattaché ici (§7f) ;
* `Chihiro` ne relie rien non plus (0 occurrence, §5.1).

**Comportement retenu pour ce lot : lecture + validation de la longueur + journalisation, aucune
émission, aucun état.** La question « faut-il un jour émettre le déroulé `4012`/`4009` ? » est
**hors du paquet** et reste en `NON ÉTABLI` (§7b) : aucune lecture ne la tranche, et l'inventer
serait une réponse fabriquée.

#### (b) `4012` et `4009` sont-ils dans le périmètre de cette fiche ?

**Non. Ni l'un ni l'autre.** La carte ne porte que `4011` ; la fiche décrit leur format parce que le
dev doit savoir ce qui *suivra* (et ne pas confondre les tailles), mais ce lot ne les déclare pas, ne
les lit pas et ne les émet pas. `4012` (7 o) et `4009` (11 o, `begin_time` `uint32`) appartiennent au
reste du lot S5 ; `4006` (48 o), `4007` (15 o) et `4010` (7 o) au lot S6 (socle §5.4).

#### Ce que le dev écrit, et ce qu'il n'écrit pas

| Écrit | N'écrit pas |
|---|---|
| `TM_CS_HUNTAHOLIC_BEGIN_HUNTING = 4011` dans `GamePackets` | `4012`, `4009`, `4006`, `4007`, `4010` dans `GamePackets` |
| une constante de longueur (7) et un prédicat de longueur exacte dans `GameHuntaholicPackets` | un lecteur de charge utile (il n'y a aucun champ) |
| une branche de dispatch + un handler `Debug` (et `Warning` si la longueur est fausse) | un émetteur de `4012`/`4009`/`4006`/`4007`/`4010`, un accusé quelconque, une minuterie, un état de chasse |
| un test d'offsets de la trame de 7 octets et un test d'unicité de l'id | une table de ressource, une migration, une diffusion aux membres de l'instance |

**Différence à ne pas rater avec le lot `4008`** : `4008` a été livré **sans aucun code** (fiche +
test de garde) parce que le client 7.3 n'émet **jamais** cet opcode. Ici c'est l'inverse : `4011` est
réellement émis par le client (§2.1), donc l'id **doit** être déclaré **et** routé — sans quoi la
trame atteint le `throw` final de `OnDataReceived` et le client se fait déconnecter (`Unknown Packet
Type`). Le lot 4011 est donc un lot *avec* code, minimal mais réel.

## 6. Écarts assumés avec NGemity, et pourquoi

| Point | NGemity (`Chihiro`) | Navislamia | Pourquoi |
|---|---|---|---|
| Déclaration | id déclaré, `_DEF` vide | id déclaré dans `GamePackets` (même nom `TM_CS_*`) | nomenclature alignée sur `op_codes.md` et rzu |
| Traitement | **aucun** (0 occurrence dans `Chihiro/src`) | lecture + journalisation, longueur exacte validée | le client est la source de vérité : sa trame fait 7 octets, et rien n'est à porter côté logique |
| Réponse | aucune | aucune | le client route 4011 vers « message non traité » (§5.2) ; inventer un accusé serait faux |
| Effet de jeu | aucun | aucun (pas de lobby/instance/minuteur) | même périmètre que le socle S1 ; les effets dépendent des lots S5 (suite) et S6 |
| Gating | commentaire `// Since EPIC_6_3` | id inconditionnel retenu, commentaire lu comme une datation | `X(4011, true)` est la seule vérité de rzu |

Aucun écart de taille, d'ordre de champ ou d'id : ces trois points sont identiques dans les trois
sources, ce qui rend ce paquet peu risqué.

## 7. `NON ÉTABLI`

Formulées comme des questions à trancher, jamais comblées par une supposition. Aucune n'empêche
d'implémenter le lot §5.4.

### (a) Libellé et rôle exact du contrôle `button_entrance_01`
Le nom du contrôle est établi (§2.2) ; son **libellé affiché** et son rôle joueur ne le sont pas. La
symétrie avec `button_entrance_02` (4005, quitter) rend « commencer la chasse » très probable, et le
socle §2.3 va dans ce sens, mais la preuve est indirecte. Les mises en page `.nui` et les scripts Lua
sont dans `data.000`, non extraits (et hors périmètre : ne pas exécuter de script client). Question :
quel bouton de la fenêtre `SUIHuntaHolicInstanceWnd` porte ce nom de contrôle ?

### (b) Le déroulé que le serveur doit émettre après un 4011
`4012` → `4009` → `4007`/`4006` → `4010` : les **formats** sont établis (socle §3.4.6-3.4.8) mais
l'**ordre**, les **délais**, le **destinataire** (le seul demandeur ou tous les membres de
l'instance) et la **sémantique de `4009.begin_time`** (secondes epoch ou ticks serveur ?) ne le sont
par aucune source (socle §7k). Question pour Killian : le lot 4011 doit-il se contenter de la
reconnaissance, ou faut-il dès maintenant fixer un ordre par convention documentée ?

### (c) Contrôle de contexte côté serveur
Faut-il refuser un 4011 émis par un personnage qui n'est pas dans l'instance HuntaHolic ? Navislamia
n'a aucun état HuntaHolic (socle §5.3) et `Chihiro` ne traite pas le paquet : aucune règle n'est
disponible. Le lot ne contrôle donc rien et l'écrit comme une réserve.

### (d) Signification de `[contrôle+0x2a4] = 1` et de l'appel `[vtable+0x3c]`
Après l'envoi, le client écrit 1 à `+0x2a4` du contrôle `button_entrance_01` et appelle une entrée
virtuelle (`0x56425f`-`0x56426b`). L'effet (désactivation visuelle ? verrou ? libération ?) n'est pas
établi ; classiquement, cela ressemble à un état « en attente de réponse serveur », mais aucun test
client ne peut être exécuté pour le confirmer. Conséquence pour nous : le serveur ne peut pas
*extraire* de ce code une obligation d'envoyer quelque chose — il ne fait que constater que le
client change d'état local.

### (e) Événement 10 (`0xa`)
Le handler ne traite que le code `10`. La nomenclature des codes d'événement des fenêtres SSS n'est
attestée nulle part dans le dépôt (le socle constate le même chiffre). Question : est-ce « clic
relâché » ou « activation » ? Sans impact sur le format du paquet.

### (f) Consommateur de la file de messages du client
Les désérialiseurs de `4009` et `4012` enfilent l'objet-message dans `[esi+0x2c]` par `0x64d0e0`
(§5.6). Le consommateur de cette file n'a pas été rattaché : ce que le client fait ensuite
(affichage `@NOTICE` pour `4009`, compte à rebours pour `4012`) n'est établi ici qu'au niveau du
socle (§3.4.7-3.4.8). Aucune lecture ne montre qu'il lise un état issu d'un `4011` reçu, mais je ne
peux pas l'exclure formellement sans désassembler le consommateur.

## 8. Commits et binaires épinglés

| Référence | Identifiant | Emploi |
|---|---|---|
| Navislamia `origin/master` (base de la branche) | `b56967a07430422add88e0e5cdf292b41b18f6c6` (`b56967a`, `Merge pull request #44 from KillianDoubre/hermes/fix-devconsole-compilation`) | état du serveur au moment de la fiche |
| rzu (`reference/rzu`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | id, `DEF` vide, gating, en-tête de 7 octets |
| NGemity (`reference/ngemity`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | id, absence de traitement |
| Client 7.3 (`reference/client73/SFrame.exe`) | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 octets | constructeur de trame, RTTI, tables du répartiteur |
| Désassemblage | `objdump -d -M intel SFrame.exe` → `/tmp/sframe.asm` (non versionné) | toutes les VMA citées |
| Dump de chaînes | `strings -n 4 SFrame.exe` → 63 099 lignes (`/tmp/sframe.strings`, non versionné) | littéraux du client |
| Fiche socle de la famille | `docs/packet-specs/socle-instances-jeu.md` (§3.3.6, §3.4.6-3.4.8, §4, §5.2, §5.4, §7k) | tailles et gating de référence |
| Fiches sœurs | `docs/packet-specs/4000-*.md`, `4003-*.md`, `4004-*.md`, `4005-*.md`, `4008-*.md` (branches `hermes/packet-*`, hors `master` au moment de la rédaction) | contrat et conventions de la famille |

Aucune autre source n'a servi : la question n'a pas nécessité de recherche web.

Ces références ont été **relevées dans les dépôts locaux**, pas recopiées : `git rev-parse HEAD` dans
`reference/rzu` (`87c1e83b…`) et `reference/ngemity` (`38ceb2c…`), `git log --oneline -1 origin/master`
et `git rev-parse origin/master` dans `Navislamia` (`b56967a`), `sha256sum reference/client73/SFrame.exe`
(`41e0af2e…`, 9 841 664 octets).

**Mesures relevées sur la base `b56967a`** (elles ne font pas partie du livrable, mais fixent le
plancher que le lot `navis-dev` doit conserver) :

```
export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
dotnet build Navislamia.sln -c Debug   # code de sortie 0 (23 avertissements, 0 erreur)
dotnet test  Tests/Tests.csproj        # code de sortie 0, 1302 tests réussis, 0 échec
```

Le critère transversal n° 2 impose **au moins 366** tests : le compte mesuré (1302) est le plancher
réel à ne pas faire baisser.

## 9. Annexe — reproductibilité (lecture seule, aucune exécution du client)

```bash
# id, nom, gating — rzu et NGemity
cat -n reference/rzu/librzu/src/packets/GameClient/TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h   # DEF vide l.5, commentaire l.7, X(4011, true) l.8-9
cat -n reference/ngemity/shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h  # DEF vide l.6, CREATE_PACKET(..., 4011) l.9
sed -n '244p' reference/ngemity/shared/Server/ClientPackets.h                        # TS_CS_HUNTAHOLIC_BEGIN_HUNTING = 4011
grep -n "SERIALISATION_F_ID2" -A2 reference/rzu/librzu/src/lib/Packet/PacketDeclaration.h  # l.587-589
grep -n "EPIC_6_3\|EPIC_7_3" reference/rzu/librzu/src/lib/Packet/PacketEpics.h      # l.56 / l.59
grep -rn "TS_CS_HUNTAHOLIC\|TS_SC_HUNTAHOLIC" reference/ngemity/Chihiro/src/         # 0 résultat (aucun traitement)

# client 7.3 : un seul émetteur
sha256sum reference/client73/SFrame.exe
objdump -d -M intel reference/client73/SFrame.exe > /tmp/sframe.asm
grep -n "0xfab" /tmp/sframe.asm          # 1 seule ligne : 4c93e2
grep -n "call.*4c93d0" /tmp/sframe.asm   # 1 seul site : 564238
```

Les **tables du répartiteur entrant** et les **pointeurs de `.data`** se lisent par adressage VMA :
`file_off = section.raw + (vma - image_base - section.vaddr)`, avec `.text` `vaddr=0x1000`,
`.rdata` `vaddr=0x60f000`, `.data` `vaddr=0x810000`, `image_base = 0x400000` (`objdump -h`). Ainsi :

* table d'octets du bloc 4002-4253 : `0x67f580` (252 octets) ; table de sauts : `0x67f560` (8 dwords) ;
* `0xc16de4` → `0xa2c070` = `button_entrance_01`, `0xc16de8` → `0xa2c05c` = `button_entrance_02` ;
* RTTI : `0xa2c0d0` → COL `0xbc0c3c` → type descriptor `0xc16e54` → nom `.?AVSUIHuntaHolicInstanceWnd@@`.

**Aucun `SFrame.exe`, aucun `.lua`, aucun `.nui` n'a été lancé** ; aucune base PostgreSQL n'a été
sollicitée ; aucun service n'a été installé.

## 10. Brouillon pour la description de la MR (`navis-dev`)

Bloc proposé pour `CLAUDE.md` — **le dev ne l'écrit pas dans `CLAUDE.md`** (Hermes refuse l'écriture) ;
il le colle dans la description de la MR, comme l'exige le critère transversal n° 5. À ajuster après
implémentation :

```markdown
### Paquet 4011 — `TM_CS_HUNTAHOLIC_BEGIN_HUNTING` (départ de la chasse HuntaHolic)

* **7 octets, en-tête seul, aucun champ** (rzu `X(4011, true)` avec `_DEF(_)` vide ; NGemity idem ;
  constructeur client `0x4c93d0` : `Length = 7` en dur à `0x4c93eb`, id `0x0fab` à `0x4c93e2`,
  checksum = somme des octets 0-5 à l'offset 6).
* **Aucun gating de version** : id inconditionnel dans rzu (`PacketDeclaration.h:587-589`), aucun
  champ. Le commentaire `// Since EPIC_6_3` date la famille (`EPIC_6_3 = 0x060300 < EPIC_7_3`) et ne
  gate rien.
* **Un seul émetteur client** : le contrôle `button_entrance_01` de `SUIHuntaHolicInstanceWnd`
  (RTTI `.?AVSUIHuntaHolicInstanceWnd@@`, vtable `0xa2c0d4`, handler `0x5641b0`, appel `0x564238`),
  événement 10. Le contrôle voisin `button_entrance_02` de la **même** méthode envoie 4005.
* **Client → serveur, sans réponse possible** : la table du répartiteur entrant du client
  (`0x67e736`, table d'octets `0x67f580`, index 9 = 4011) envoie 4011 à la branche « message non
  traité » (`0x67ef21`). Ne jamais renvoyer un 4011.
* **Conduite serveur en 7.3** : lecture, validation de la longueur exacte (7), journal `Debug`,
  aucun état modifié (pas de lobby/instance/minuteur HuntaHolic côté serveur) et **aucune réponse**.
* **Suite (hors de ce paquet)** : le déroulé 4012 → 4009 → 4007/4006 → 4010, formats établis par
  `docs/packet-specs/socle-instances-jeu.md` §3.4.6-3.4.8, **ordre et cadence non établis** (§7k).
* Fiche : `docs/packet-specs/4011-huntaholic-begin-hunting.md`.

* **Hotspot** : `Game/Network/Packets/Game/GameHuntaholicPackets.cs` et le point d'insertion dans
  `GamePackets.cs` sont revendiqués par les branches sœurs 4000/4003/4004/4005/4008 — conflit de
  fusion attendu, à traiter par ajout minimal, jamais par réécriture de la classe.
```

## A VERIFIER PAR KILLIAN

1. **Le lot 4011 reste-t-il sans effet de jeu ?** La reconnaissance et la validation de longueur
   suffisent-elles, ou faut-il dès maintenant émettre le déroulé (`4012`/`4009`/`4006`/`4007`/`4010`)
   dont l'ordre et la cadence ne sont établis par aucune source (§7b, socle §7k) ?
2. **Réponses à `4011`** : confirmer qu'aucun accusé n'est souhaité — la preuve client est que le
   répartiteur entrant envoie l'id vers « message non traité » (§5.2), donc tout accusé serait
   journalisé comme inconnu par le client.
3. **Contrôle de contexte** : faut-il refuser un `4011` hors instance HuntaHolic ? Aucun état
   HuntaHolic n'existe côté serveur et `Chihiro` ne tranche pas (§7c).
