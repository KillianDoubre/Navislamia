# 4005 — `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE` (quitter une instance HuntaHolic)

Fiche de référence du lot `navis-dev` : branche `hermes/packet-4005-huntaholic-leave-instance`,
`master` = `b56967a`. **Aucun code serveur n'est modifié par cette fiche** (ni fichier de paquet, ni
`GamePackets`, ni `GameClient.cs`, ni `CLAUDE.md`).

Toutes les références ont été lues localement : `op_codes.md`, `reference/rzu/`,
`reference/ngemity/Chihiro/`, `reference/client73/SFrame.exe` (lecture statique seule, sans exécution).

## Questions tranchées par cette fiche

1. **4005 = 7 octets, en-tête seul, aucun champ.** Prouvé trois fois : rzu (`DEF(_)` vide), NGemity
   (`DEF(_)` vide) et le constructeur de trame du client 7.3 (`0x4c9380`, `Length = 7` écrit en dur).
2. **Sens : client → serveur**, et le client n'en reçoit jamais : rzu déclare
   `SessionPacketOrigin::Client` et le répartiteur entrant du client range 4005 dans sa branche
   « message non traité » (§5.2). Il n'existe donc **aucun** paquet de réponse à 4005.
3. **Deux et seulement deux sites d'émission** dans le client 7.3 : `0x5641f2` et `0x567453`, tous
   deux dans des fenêtres HuntaHolic (§2). Les autres fenêtres de la famille n'émettent jamais 4005.
4. **Aucun champ n'est gaté** : `X(4005, true)` (`..._ID(X)` lignes 7-8 de rzu) et une charge vide.
   Il n'y a donc rien à trancher pour 7.3 au-delà du constat « pas de gating ».
5. **`4008` est mort en 7.3** : le client ne le construit jamais (`0xfa8` n'est l'immédiat d'aucune
   instruction) et ne le route pas en entrée. La question ouverte (d) de
   `docs/packet-specs/socle-instances-jeu.md` est donc close : « quitter le lobby » ne passe pas par
   4008 côté client.
6. **Bonus famille** : le répartiteur entrant du client route **exactement** les membres serveur →
   client de la famille (4001, 4002, 4006, 4007, 4009, 4010, 4012, 4253) et **aucun** membre client →
   serveur. Les questions (a), (e) et (f) de `socle-instances-jeu.md` sont donc closes par lecture
   directe de tables (§9) — à reporter par le PO dans la fiche de socle, hors périmètre d'ici.

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id décimal | **4005** (`0x0FA5`) | `op_codes.md:231` |
| Nom | `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE` | `op_codes.md:231` |
| Sens | client → serveur | `reference/rzu/librzu/src/packets/GameClient/TS_CS_HUNTAHOLIC_LEAVE_INSTANCE.h:10` (`SessionPacketOrigin::Client`) |
| Charge utile | **aucune** | rzu même fichier `:5` ; NGemity `.../TS_CS_HUNTAHOLIC_LEAVE_INSTANCE.h:6` |
| Taille sur le fil | **7 octets** | §3 |
| Rôle dans la famille | sortie d'instance (symétrique de 4003 créer / 4004 rejoindre) | noms rzu + `op_codes.md:226-238` |

Sources, mot pour mot :

* `op_codes.md:231` — `[4005] = "TM_CS_HUNTAHOLIC_LEAVE_INSTANCE",`
* rzu, 12 lignes : `#define TS_CS_HUNTAHOLIC_LEAVE_INSTANCE_DEF(_)` (`:5`, vide),
  `#define TS_CS_HUNTAHOLIC_LEAVE_INSTANCE_ID(X) \ X(4005, true)` (`:7-8`),
  `CREATE_PACKET_VER_ID(TS_CS_HUNTAHOLIC_LEAVE_INSTANCE, SessionType::GameClient, SessionPacketOrigin::Client);` (`:10`).
* NGemity : `CREATE_PACKET(TS_CS_HUNTAHOLIC_LEAVE_INSTANCE, 4005);`
  (`shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_LEAVE_INSTANCE.h:8`), énumération
  `shared/Server/ClientPackets.h:238`, inclusion `shared/Server/XPacket.h:110`.
* Navislamia `master` : l'id **n'existe pas encore**. `Game/Network/Packets/Enums/GamePackets.cs`
  déclare `TM_CS_CHECK_CHARACTER_NAME = 2006` (`:196`) puis passe à la famille instance game
  (`:202-207`) : aucun membre `HUNTAHOLIC`. Aucun fichier `*Huntaholic*` n'est suivi par git
  (`git ls-tree -r --name-only master | grep -i huntaholic` → 0 résultat) : le fichier de la famille
  reste à créer (voir §5.4).

## 2. Ce que le joueur fait pour que le client l'envoie

Le client 7.3 construit la trame dans une **fonction minuscule et unique** (`0x4c9380`) et la remet
au transport. L'id `0xfa5` n'apparaît que **trois fois** dans tout `.text` (scan exhaustif des
immédiats, `grep -nE "0xfa5([^0-9a-f]|$)"`) :

| Site | Instruction | Nature |
|---|---|---|
| `0x4c9392` | `mov eax,0xfa5` | **le constructeur de trame** (celui de ce paquet) |
| `0x8b9e21` | `push 0xfa5` | mise en forme d'une erreur Win32 (`code → chaîne`), pas un paquet |
| `0x9a839d` | `mov ecx,0xfa5` | table d'erreurs Win32 `code → `ERROR_REC_NON_EXISTENT`` (§9, piège) |

Et l'id n'apparaît **jamais** comme donnée en `.rdata`/`.data` (scan du motif `a5 0f 00 00` : 0
occurrence) : aucun routage par table de données, ni entrant ni sortant.

### 2.1 Le constructeur de trame `0x4c9380` (7 octets, id `0xfa5`)

```
4c9380: push ebp / mov ebp,esp / sub esp,0x8
4c9386: xor eax,eax
4c9388: mov DWORD PTR [ebp-0x7],eax      ; Length = 0
4c938b: mov WORD  PTR [ebp-0x3],ax       ; ID = 0
4c938f: mov BYTE  PTR [ebp-0x1],al       ; Checksum = 0
4c9392: mov eax,0xfa5
4c9397: mov WORD  PTR [ebp-0x3],ax       ; ID = 4005
4c939b: mov DWORD PTR [ebp-0x7],0x7      ; Length = 7 (en dur)
4c93a2: lea eax,[ebp-0x7] / xor dl,dl
4c93a8: add dl,BYTE PTR [eax] / inc eax / cmp eax,[ebp-0x1] / jne 0x4c93a8
4c93b5: mov BYTE PTR [ebp-0x1],dl        ; Checksum = somme des octets 0..5
4c93b2: mov ecx,DWORD PTR [ecx+0x20]     ; pointeur de session
4c93b8: lea edx,[ebp-0x7] / push edx
4c93bc: call 0x4c7aa0                    ; émission de la trame telle quelle
4c93c1: pop esi / mov esp,ebp / pop ebp / ret
```

Lecture : l'objet-trame **est** le tampon (l'en-tête est à l'offset 0, aucune copie), la longueur est
posée en dur à 7, la charge n'est jamais écrite — la fonction ne touche aucun octet au-delà de
l'offset 6. Le constructeur **frère immédiat** `0x4c93d0` fait exactement la même chose pour `0xfab`
= **4011** (`mov eax,0xfab` en `0x4c93e2`, `Length = 7` en `0x4c93eb`) : les deux constructeurs se
suivent, ce qui confirme la lecture.

### 2.2 Fenêtre `SUIHuntaHolicInstanceWnd`, contrôle `button_entrance_02` — appelant `0x5641f2`

**Appartenance à la classe** (nécessaire pour dire « quel geste ») : l'enregistrement de la fenêtre est
en `0x636690` (`push 0xa49820` = `"Create: SUIHuntaHolicInstanceWnd\n"`, `push 0x4c0` = taille de
l'objet, `call 0x97671b` = `operator new`, `call 0x563fb0` = constructeur). Le constructeur suivant
dans `.text` est `0x564da0`, celui de `SUIHuntaHolicLobbyWnd` (`push 0xa49898` =
`"Create: SUIHuntaHolicLobbyWnd\n"` en `0x6364e3`, taille `0x4e4`). Donc tout code de `[0x563fb0,
0x564da0[` appartient à `SUIHuntaHolicInstanceWnd` — et `0x5641b0` y est. Les champs que le handler
remet à zéro (`[esi+0x4b4]`, `[esi+0x4b8]`, `[esi+0x4bc]`) tiennent dans un objet de `0x4c0` octets,
ce qui corrobore l'attribution.

**Enchaînement** (`0x5641b0`, 4 arguments : `ret 0x10`) :

| Adresse | Instruction | Sens |
|---|---|---|
| `0x5641b4` | `mov ebx,DWORD PTR [ebp+0x8]` | argument 1 = **nom du contrôle** (`const char*`) |
| `0x5641b9` | `mov edi,DWORD PTR [ebp+0xc]` | argument 2 = code d'évènement |
| `0x5641be` | `cmp edi,0xa` / `jne 0x56426d` | ne réagit qu'au code **10** |
| `0x5641c7` | `mov eax,ds:0xc16de8` → `push eax; push ebx; call 0x977ff7` | comparaison **insensible à la casse** (`0x977ff7` abaisse A-Z en a-z) du nom du contrôle avec `[0xc16de8]` = `0xa2c05c` = `"button_entrance_02"` |
| `0x5641da` | `mov ecx,ds:0xc4b28c` / `mov ecx,[ecx+0x144]` / `push 0xa1fa18` (`"huntaholic"`) / `call 0x48b760` | récupère l'objet-message nommé `huntaholic` |
| `0x5641f2` | `call 0x4c9380` | **émet 4005** |
| `0x5641f9` | `mov BYTE PTR [esi+0x4bc],al` / `mov DWORD PTR [esi+0x4b8],eax` / `mov DWORD PTR [esi+0x4b4],eax` | remet à zéro trois champs d'état de la fenêtre |
| `0x56427d` | `push [ebp+0x14]` / `push [ebp+0x10]` / `push edi` / `push ebx` / `call 0x5bbf80` | délègue au handler de base |

Le **même handler** contient le geste symétrique : si le nom du contrôle vaut `[0xc16de4]` =
`0xa2c070` = `"button_entrance_01"` (`0x56420d`), il appelle `0x4c93d0` = le constructeur de **4011**
(`TM_CS_HUNTAHOLIC_BEGIN_HUNTING`, `0x564238`), puis récupère le contrôle fils par son nom
(`0x79be10`), met son octet `+0x2a4` à 1 et appelle son handler virtuel (`[vtable+0x3c]`,
`0x56426b`). Autrement dit : **dans la fenêtre d'instance, un bouton fait « commencer la chasse »
(4011) et l'autre « quitter l'instance » (4005)**. Le libellé affiché de `button_entrance_02` reste
`NON ÉTABLI` (§7b) : le nom du contrôle évoque une entrée, alors que la trame émise est une sortie.

### 2.3 Fenêtre `SUIHuntaHolicScoreBoardWnd` — appelant `0x567453`

Second appelant, dans la fonction `0x5673f0` (`ret 4`) :

| Adresse | Instruction | Sens |
|---|---|---|
| `0x5673f6` | `cmp DWORD PTR [eax+0x4],0x426` / `jne 0x567459` | ne réagit qu'à la valeur `0x426` (1062) |
| `0x5673ff` | `add eax,0x17` puis `[eax+0x10]` = longueur, `[eax+0x14]` = capacité, `[eax]` = tampon | `std::string` à `+0x17` |
| `0x567421` | `push ecx; push 0xa2cac8` (`"huntaholic_scoreboard"`); `call 0x41e290` | comparaison bornée à 21 caractères |
| `0x56742d`/`0x567432` | `cmp esi,0x15` (`jb`/`jne` → retour) | la chaîne doit faire **exactement** 21 caractères, soit `"huntaholic_scoreboard"` en entier |
| `0x56743c` | `mov ecx,ds:0xc4b28c` / `[ecx+0x144]` / `push 0xa1fa18` (`"huntaholic"`) / `call 0x48b760` | même objet-message que §2.2 |
| `0x567453` | `call 0x4c9380` | **émet 4005** |

Attribution à la classe : l'enregistrement `Create: SUIHuntaHolicScoreBoardWnd` est en `0x63670c`
(`push 0xa497fc`, taille `0x510`, constructeur `0x5665e0`) ; `0x5673f0` est au-dessus, et les champs
touchés (`[edi+0x4b4]` en `0x56734a`) tiennent dans `0x510`. C'est le **seul** enregistrement de
fenêtre HuntaHolic postérieur (le client n'en journalise que six) : l'attribution repose sur cette
borne inférieure, le nom de contrôle comparé et la taille de l'objet — **pas** sur une table de
classes lue (§7e).

### 2.4 Ce qui n'est pas un déclencheur

* **Exhaustivité des appelants** : `call 0x4c9380` n'apparaît qu'en `0x5641f2` et `0x567453`
  (`grep -n "4c9380"` sur tout le désassemblage : la définition `0x4c9380` et ces deux appels).
  Ni le lobby, ni la création d'instance, ni la fenêtre de résultat, ni la confirmation de mot de
  passe ne peuvent donc émettre 4005.
* Aucun constructeur concurrent : `0xfa5` n'a qu'un site d'écriture d'id (§2, tableau).
* **`4008` n'existe pas en 7.3** : aucune instruction ne prend `0xfa8` en immédiat
  (`grep -nE "(push|mov|...)[a-z ]*0xfa8\b"` → 0 résultat), aucune trame n'est construite, et le
  répartiteur entrant le range aussi dans sa branche par défaut (§9). Le « quitter le lobby » du
  client passe donc **par autre chose que 4008** — quoi, n'est pas établi (§7d). Conséquence
  directe pour le dev : **ne rien implémenter pour 4008**.

## 3. Structure sur le fil — **7 octets**

| Offset | Taille | Type | Nom | Valeur / source |
|---|---|---|---|---|
| 0 | 4 | `uint32` LE | `Length` | **7** — client `mov DWORD PTR [ebp-0x7],0x7` en `0x4c939b` ; rzu `CREATE_STRUCT_IMPL(name_, 7, …)` (`librzu/src/lib/Packet/PacketDeclaration.h:616-621`) |
| 4 | 2 | `uint16` LE | `ID` | **4005** (`0x0FA5`) — client `mov eax,0xfa5` en `0x4c9392` puis `mov WORD PTR [ebp-0x3],ax` en `0x4c9397` ; rzu `X(4005, true)` `TS_CS_HUNTAHOLIC_LEAVE_INSTANCE.h:7-8` |
| 6 | 1 | `uint8` | `Checksum` | somme arithmétique des octets 0 à 5 — client boucle `0x4c93a8`-`0x4c93b0` (`add dl,BYTE PTR [eax]`, 6 itérations) puis écriture `mov BYTE PTR [ebp-0x1],dl` en `0x4c93b5` |
| 7 | — | — | *(aucun champ)* | `TS_CS_HUNTAHOLIC_LEAVE_INSTANCE_DEF(_)` vide (rzu `:5`, NGemity `:6`) : la charge est nulle, il n'y a pas d'octet 7 |

**Taille totale attendue : 7 octets.** Aucun champ après l'en-tête, aucun tableau, aucun remplissage.

Les trois sources concordent : rzu et NGemity déclarent une macro `_DEF(_)` vide (donc un
`getSize()` valant l'en-tête) et le client écrit la longueur 7 en dur avant d'émettre. Le contrôle
positif de la méthode est le constructeur frère `0x4c93d0` (4011), de forme rigoureusement identique
avec son propre id (`0xfab`) et sa propre longueur (7) : deux constructeurs voisins, deux ids
distincts, aucun risque de confusion d'id dans la lecture.

**Politique de lecture à implémenter** : accepter **exactement** 7 octets, refuser 6 et 8 — c'est la
règle du dépôt pour une trame sans charge (`GameInstanceGamePackets.HasNoPayload`, 4251/4252, fusionné
sur `master` : `Game/Network/Packets/Game/GameInstanceGamePackets.cs:51-58`). Une trame plus longue
signifie que l'émetteur n'est pas le client 7.3 ; une trame plus courte est tronquée. Le checksum
n'est pas vérifié (aucun paquet de cette famille ne l'est, cf. le handler fusionné de 4251) : cet
écart est assumé et signalé ici pour qu'il ne soit pas découvert par surprise.

## 4. Gating de version

| Élément | Ce que dit rzu | Décision pour Epic 7.3 |
|---|---|---|
| **Id 4005** | `#define TS_CS_HUNTAHOLIC_LEAVE_INSTANCE_ID(X) \ X(4005, true)` (`TS_CS_HUNTAHOLIC_LEAVE_INSTANCE.h:7-8`) | **4005, inchangé.** `X(id, condition)` engendre `if(condition) id = id_;` (`SERIALISATION_F_ID2`, `PacketDeclaration.h:587-589`, appelé par `CREATE_PACKET_VER_ID` `:616-621`) : avec `true`, l'id vaut 4005 pour **toutes** les versions connues de rzu. Aucune autre entrée `_ID(X)` n'existe dans le fichier (pas de renumérotation à un epic ultérieur) |
| **Champs** | aucun champ ; `#define TS_CS_HUNTAHOLIC_LEAVE_INSTANCE_DEF(_)` (`:5`) est vide, et le fichier ne contient **aucune** directive `version` | **rien à exclure, rien à inclure** : le paquet n'a pas de charge, donc il n'y a pas de gating de champ possible. C'est le cas le plus simple de la famille |
| **Plancher de version de la famille** | cet en-tête ne porte **pas** de commentaire `// Since EPIC_*`, contrairement à ses voisins directs : `TS_CS_HUNTAHOLIC_BEGIN_HUNTING.h:7`, `TS_SC_HUNTAHOLIC_BEGIN_COUNTDOWN.h:7`, `TS_CS_INSTANCE_GAME_ENTER.h:10`, `TS_CS_INSTANCE_GAME_EXIT.h:9`, `TS_CS_INSTANCE_GAME_SCORE_REQUEST.h:9` (tous `// Since EPIC_6_3`) | absence de commentaire = **absence de contrainte** (ne pas en déduire un gating) ; et de toute façon `EPIC_6_3 = 0x060300 < EPIC_7_3 = 0x070300` (`PacketEpics.h:56,59`), donc la famille existe en 7.3 |
| **Preuve client** | — | le constructeur `0x4c9380` n'a **aucune** branche de version : il écrit `Length = 7` et l'id `0x0fa5` sans test, et la fonction appelée (`0x4c7aa0`) émet le tampon tel quel. En 7.3 la trame est donc celle du §3, sans variante |

**Conclusion de version** : pour Epic 7.3, `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE` = id 4005, 7 octets,
aucun champ gaté, aucune variante à écrire. Aucun `NON ÉTABLI` de version ne subsiste sur ce paquet.

## 5. Traitement attendu

### 5.1 Ce que `Chihiro` (NGemity) fait du paquet : **rien**

| Fait | Preuve (relevée dans le contexte courant) |
|---|---|
| La classe et l'id sont déclarés, mais aucun code ne traite 4005 | `grep -rn "TS_CS_HUNTAHOLIC\|TS_SC_HUNTAHOLIC" --include=*.cpp Chihiro/src/` → **0 résultat** ; les seules occurrences du nom sont `shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_LEAVE_INSTANCE.h:8`, `shared/Server/ClientPackets.h:238` et l'include `shared/Server/XPacket.h:110` |
| Toute la logique d'instance/lobby est **commentée** | `Chihiro/src/Skills/Skill.cpp:1406` ouvre un `/*` et `:1433` le ferme : `INSTANCE_GAME_ENTER()`, `WARP_TO_HUNTAHOLIC_LOBBY()`, `INSTANCE_GAME_EXIT()` sont donc inatteignables |
| Les identifiants de compétences existent quand même | `Chihiro/src/Skills/SkillBase.h:224-225` : `SKILL_WARP_TO_HUNTAHOLIC_LOBBY = 64818`, `SKILL_INSTANCE_GAME_EXIT = 64827` |

Aucune référence ne dit donc quoi faire d'un 4005 : la conduite serveur ne peut pas être recopiée,
elle doit être tranchée (§7a) ou laissée en no-op documenté (§5.3).

### 5.2 Le client 7.3 ne reçoit **jamais** un 4005 — preuve directe

Le client convertit une trame brute en évènement dans une fonction **englobante `0x67e1d9`** :
`mov eax,[trame+4]` (l'id), une suite de `cmp eax,imm` / `jg` / `je`, puis, pour les plages denses,
`sub eax,<base>` / `cmp eax,<count>` / `movzx eax,BYTE PTR [eax+<table d'octets>]` /
`jmp DWORD PTR [eax*4+<table de sauts>]`. La **branche par défaut** est `0x67ef21`, qui journalise
`push 0xa53df0` — la chaîne coréenne « 처리되지 않은 메세지 : %d » (« message non traité ») — puis
saute à la queue de libération `0x67ef39`.

Pour la plage qui contient notre paquet, le bloc est en `0x67e736` : base `0xfa2` (4002), 251 entrées,
table d'octets `0x67f580`, table de sauts `0x67f560` (8 entrées, index 7 = défaut). Lecture directe
des tables :

| Id | Index d'octet | Cible | Verdict |
|---|---|---|---|
| **4005** | **7** | **`0x67ef21`** | **branche par défaut : le client n'a aucun bras pour 4005** |
| 4002 | 0 | `0x67e754` → `0x6708a0` | traité (objet de `0x39` = `0x13` + 38 → trame 45 o) |
| 4006 | 1 | `0x67e761` → `0x670910` | traité (objet `0x3c` = `0x13` + 41 → trame 48 o) |
| 4007 | 2 | `0x67e76e` → `0x6709a0` | traité (objet `0x1b` = `0x13` + 8 → trame 15 o) |
| 4009 | 3 | `0x67e77b` → `0x670a00` | traité (objet `0x17` = `0x13` + 4 → trame 11 o) |
| 4010 | 4 | `0x67e788` → `0x670b10` | traité (objet `0x13` → trame 7 o, aucune charge) |
| 4012 | 5 | `0x67e795` → `0x670b60` | traité (objet `0x13` → trame 7 o) |
| 4253 | 6 | `0x67e7a2` → `0x670bb0` | traité (objet `0x23` = `0x13` + 16 → trame 23 o) |
| 4003, 4004, 4008, 4011, 4250, 4251, 4252 | 7 | `0x67ef21` | non traités |

Contre-validations faites (obligatoires avant d'affirmer avoir lu une table) :

* `0x670bb0` (4253) lit **quatre dwords** aux offsets `7, 11, 15, 19` de la trame
  (`mov edx,[ecx+0x13]`, `[ecx+0xf]`, `[ecx+0xb]`, `[ecx+0x7]`) : c'est exactement la forme de 23
  octets que la fiche de socle §3.2.4 et la fiche fusionnée du lot S1 retiennent, et sa taille
  d'objet (`0x23`) le confirme.
* `0x670b10` (4010) alloue `0x13` octets : objet **sans charge**, donc trame de 7 octets — c'est le
  point (f) du socle, désormais attribué (4010) au lieu d'être « un objet à charge nulle non
  attribué ».
* `0x6708a0` (4002) copie 38 octets (`rep movs` de 9 dwords + `movsw`) : 45 octets au total, conforme
  au §3.4.3 du socle.
* Les ids internes des objets construits (`[objet+4]`) suivent l'ordre annoncé par le socle :
  4002 → `0x7f` (127), 4006 → `0x80` (128), 4007 → `0x81` (129), 4009 → `0x82` (130), 4010 → `0x83`
  (131), 4012 → `0xa3` (163), 4253 → `0x4b3` (1203).

**Conséquence directe : le serveur ne doit rien répondre.** Un 4005 descendant ne serait pas un accusé
mais un message inconnu : le client le journaliserait comme « message non traité » et le libérerait.
La ligne du socle §5.2 (« `4005` (quitter) / `4008` (quitter le lobby) → aucun accusé nécessaire »)
est donc **confirmée et prouvée** ici, y compris pour 4008 (non routé non plus).

### 5.3 Conduite serveur

Ce que le serveur doit faire d'un 4005 en 7.3, et rien de plus :

1. **Lire et valider** la trame : exactement 7 octets (rejet de 6 et 8), aucun champ à extraire.
2. **Journaliser** en `Debug` (le paquet est un geste de joueur normal, pas une anomalie).
3. **Ne rien émettre en retour**, et **ne rien changer à l'état** aujourd'hui : il n'existe côté
   Navislamia aucun état de lobby ni d'instance HuntaHolic (aucun service, aucune entité, aucune
   table) — la fiche de socle §5.3 décrit le lot protocole et §5.4 le découpage du reste. Il n'y a
   donc littéralement rien à quitter : la conduite est un **no-op par construction**, exactement
   comme pour le paquet fusionné `TM_CS_INSTANCE_GAME_EXIT` (4251), dont le handler dit « the
   character is brought back to the lobby by the server » sans que le départ du personnage soit
   encore produit (`Game/Network/Clients/GameClient.cs:1102-1117`).

Ce que le serveur ne doit **pas** faire, faute de source :

* répondre par un 4005 ou par un paquet S→C de la famille : aucun n'existe (§5.2) ;
* téléporter le personnage, le retirer d'un groupe, ou écrire en base : aucune des trois références
  ne le montre (NGemity : bloc commenté ; rzu : aucun code serveur pour cet id dans ce dépôt).

Prérequis nommé pour la vraie conduite (à ne pas contourner) : quand l'état de lobby/instance du
socle existera (lot 2/3 de `socle-instances-jeu.md` §5.4), 4005 devra retirer le personnage de
l'instance et le ramener à sa position d'origine. **Le paquet exact qui réalise ce retour n'est pas
établi** (§7a) : le client n'ayant aucun bras pour 4005, c'est le serveur qui choisit, et ce choix
doit être arbitré, pas deviné.

### 5.4 Contrat d'implémentation pour `navis-dev`

**Fichier** : `Game/Network/Packets/Game/GameHuntaholicPackets.cs` — **attention, ce fichier est un
point de collision** : les branches sœurs non mergées `hermes/packet-4000-huntaholic-instance-list`
(`b326bbb`), `hermes/packet-4003-huntaholic-create-instance` (`3081b0e`) et
`hermes/packet-4004-huntaholic-join-instance` (`dbdcfe0`) créent **chacune le même fichier** avec
**la même classe** `GameHuntaholicPackets` (et la même constante privée `HeaderSize = 7`). Le dev du
4005 doit donc écrire sa part dans ce fichier unique, sans dupliquer la classe, et la règle
d'intégration pour la branche mergée en second est celle du dépôt : **garder une seule déclaration**
(une classe d'une famille = un fichier), fusionner les membres, ne jamais recopier `HeaderSize` /
`EmptyLength` en double (une duplication = erreur de compilation, pas un détail de style).

Constantes et lecteur attendus (style famille : offsets nommés, un lecteur par paquet) :

```csharp
private const int HeaderSize = 7;

/// <summary>
/// Total size of TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005): the 7-byte header, no payload at all. The 7.3
/// client writes that length in hard at 0x4c939b and never writes a byte past offset 6.
/// </summary>
public const int LeaveInstanceLength = HeaderSize;

/// <summary>
/// TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005) is the "leave the instance" gesture of the HuntaHolic family
/// and carries no payload. Only the exact 7-byte form is accepted: a shorter frame is truncated and a
/// longer one carries bytes no field accounts for (so the sender is not the 7.3 client). The frame has
/// a checksum byte at offset 6 that is not verified, here as for every other packet of the family.
/// </summary>
public static bool IsLeaveInstance(ReadOnlySpan<byte> packet)
{
    return packet.Length == LeaveInstanceLength;
}
```

Ce lecteur ne prend aucune sortie : il n'y a rien à extraire. Pas de `record struct` de requête, pas
de champ, pas de valeur par défaut.

**Énumération** (`Game/Network/Packets/Enums/GamePackets.cs`) : ajouter le membre **avant** le bloc
`TM_CS_INSTANCE_GAME_*` (ordre croissant des ids, `:202`), avec un commentaire qui cite la fiche —
modèle :

```csharp
// TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005): 7 bytes, no payload, X(4005, true) in rzu so no version
// gating and no gated field. The 7.3 client sends it from its HuntaHolic instance window (control
// button_entrance_02) and from its scoreboard window, and never reads one back (its receive
// dispatcher routes 4005 to the "unhandled message" branch). See
// docs/packet-specs/4005-huntaholic-leave-instance.md.
TM_CS_HUNTAHOLIC_LEAVE_INSTANCE = 4005,
```

**Dispatch** (`Game/Network/Clients/GameClient.cs`) : un bras dans la chaîne de `if` du `switch`-moins-un
(aucun membre de `GamePackets` ne doit atteindre le `throw` « Unknown Packet Type »), placé avec les
autres paquets de la famille, et un handler à côté de `HandleInstanceGameExit` :

```csharp
/// <summary>
/// TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005) carries no payload and expects no answer: the client has no
/// receive arm for it and the HuntaHolic lobby does not exist server side yet, so there is no instance
/// to leave. Only the frame's exact 7-byte form is checked.
/// </summary>
private void HandleHuntaholicLeaveInstance(byte[] buffer)
{
    if (!GameHuntaholicPackets.IsLeaveInstance(buffer))
    {
        _logger.Warning("Malformed HuntaHolic leave instance request received from {clientTag} (Length: {length})",
            ClientTag, buffer.Length);
        return;
    }

    _logger.Debug("TM_CS_HUNTAHOLIC_LEAVE_INSTANCE ({id}) Length: {length} received from {clientTag}",
        (ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_INSTANCE, buffer.Length, ClientTag);
}
```

```csharp
// TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005): the instance window's leave gesture, read and nothing else —
// no answer exists for it (see the sheet §5.2).
if (header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_INSTANCE)
{
    HandleHuntaholicLeaveInstance(msgBuffer);
    continue;
}
```

**Tests** (nouveau fichier `Tests/Game/HuntaholicLeaveInstancePacketsTests.cs`, modèle
`Tests/Game/InstanceGamePacketsTests.cs`) : au minimum les tests d'offsets et de longueur exigés,
c'est-à-dire, sur une trame construite par le test lui-même (`Length` en `uint32` LE à 0, id `4005` en
`uint16` LE à 4, checksum à 6) :

* la trame fait **7** octets et `LeaveInstanceLength == 7` ;
* `Length` occupé aux offsets 0-3, id aux offsets 4-5 valant `0x0FA5`, checksum à l'offset 6 ;
* **refus** des longueurs 6 et 8 (et d'une trame vide), **acceptation** de 7 ;
* l'id `4005` est unique dans `GamePackets` et correspond à `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE` ;
* un test de dispatch (le bras atteint `HandleHuntaholicLeaveInstance` et ne retombe pas sur le `throw`
  final) si le fichier de tests du client en contient déjà pour la famille.

**Interdits explicites** : ne rien écrire pour 4008 (`LEAVE_LOBBY`, jamais émis par le client 7.3),
ni pour 4001/4002/4006/4007/4009/4010/4012 (côté serveur → client, hors de ce lot), ni aucun paquet de
réponse. Ne pas toucher `master`, ne pas modifier `CLAUDE.md` (Hermes refuse l'écriture : le bloc
destiné à `CLAUDE.md` va dans la description de la MR).

### 5.5 Cas limites

| Entrée | Conduite |
|---|---|
| 7 octets, id 4005 | chemin nominal : `Debug`, aucune écriture, aucune réponse |
| 6 octets (tronqué) | `Warning` « Malformed … (Length: 6) », retour immédiat, aucun traitement |
| 8 octets ou plus (rembourré) | `Warning` « Malformed … », retour immédiat : la longueur en dur du client (`0x4c939b`) prouve qu'un 7.3 n'envoie pas ça |
| id 4005 avec un checksum faux | **Corrigé par le lot `navis-dev` (27/09/2026)** : le lecteur `IsLeaveInstance` ne vérifie bien que la longueur (il accepte la trame), mais la **boucle de réception** de `GameClient.OnDataReceived` vérifie le checksum **avant tout dispatch**, journalise « Invalid Message received from … » et arrête la lecture : la trame n'est donc **jamais traitée**, et c'est le comportement de tous les paquets, pas un écart propre à 4005. Le mot « accepté » ne valait que pour le lecteur seul. Épinglé par `OnDataReceived_StopsOnAFrameWithABadChecksum` (§11.7) |
| rafale de 4005 (double clic) | deux `Debug`, aucun état modifié : pas de limitation de débit, comme le reste du lot |

## 6. Écarts assumés avec NGemity, et pourquoi

| Écart | Pourquoi on l'assume |
|---|---|
| NGemity ne traite pas 4005 (aucun bras, logique commentée) | NGemity se compile en **`EPIC_4_1_1`** (`reference/ngemity/shared/Common/Define.h:25` : `#define EPIC EPIC_4_1_1`). La famille HuntaHolic n'existe qu'à partir d'`EPIC_6_3` (`PacketEpics.h:12`) : son silence est un **artefact de version**, pas un désaccord. Le client 7.3, qui tranche, émet bien 4005 |
| NGemity déclare quand même la classe et l'id (`ClientPackets.h:238`) | c'est le reflet de son `shared/Server/Packets/` généré à partir de listes plus récentes que son `EPIC` compilé. On suit le client, pas la déclaration |
| NGemity porte des identifiants de compétences HuntaHolic (`SKILL_WARP_TO_HUNTAHOLIC_LOBBY = 64818`, `SKILL_INSTANCE_GAME_EXIT = 64827`, `SkillBase.h:224-225`) mais leurs handlers sont commentés (`Skill.cpp:1406-1433`) | utile plus tard pour savoir **d'où** le serveur *entrerait* dans un lobby (une compétence), mais ne dit rien de la conduite d'un **4005** : on ne décommente rien (hors périmètre, et le bloc est décompilé par endroits) |
| NGemity nomme `SKILL_WARP_TO_HUNTAHOLIC_LOBBY` là où rzu parle d'instance | les deux familles cohabitent dans les références (lobby = 4000-4008, instance = 4250-4253 + 4005/4011). On garde les noms rzu (`TM_CS_HUNTAHOLIC_LEAVE_INSTANCE`) et on ne fusionne pas les deux notions |

Aucun écart de **format** : les trois sources donnent 7 octets, id 4005, aucune charge.

## 7. `NON ÉTABLI`

### (a) La conduite serveur qui suit un 4005
Aucune des trois références ne montre ce que le serveur fait après réception : ni rzu (dépôt de
définition, aucun code serveur pour cet id), ni NGemity (bras inexistant), ni le client (qui n'attend
rien, §5.2). Le paquet qui ramènerait le personnage (téléportation ? `TM_SC_WARP` ? un
`TM_SC_CHANGE_LOCATION` en réponse ? une suppression pure et simple de l'instance ?) est à trancher
**quand l'état de lobby/instance existera** — pas dans ce lot.
*À trancher par* : Killian, au moment du lot « instance HuntaHolic » (socle §5.4, lot 2/3).

### (b) Le libellé du contrôle `button_entrance_02`
Le nom du contrôle dit « entrée » alors que la trame émise est `LEAVE_INSTANCE`. Les layouts `.nui`
ne sont pas dans le dump (`reference/client73/` ne contient que `SFrame.exe` et des `.rdb`), donc le
texte affiché au joueur n'est pas lisible ici. L'**action** est prouvée (id `0xfa5` écrit en dur) ;
c'est le mot sur le bouton qui manque.
*À trancher par* : observation en jeu, ou lecture du `.nui` du client si Killian en fournit un.

### (c) La valeur `0x426` du handler du tableau des scores (`0x5673f6`)
Comparée avant le nom, mais sa nature (id interne de message ? code de notification de l'UI ?) n'est
établie par aucune source lisible ici. Sans effet sur la fiche : le chemin 4005 est de toute façon
identifié par l'appel à `0x4c9380`.

### (d) Ce que le client fait pour « quitter le lobby »
`4008` n'est **jamais** émis par le client 7.3 (aucun immédiat `0xfa8`, aucune trame construite) et
n'est pas routé en entrée. Soit le geste n'existe pas côté client en 7.3, soit il passe par un autre
paquet (`TM_CS_CHANGE_LOCATION` est un candidat, mais rien ne le montre). **Conséquence pour le dev :
ne rien implémenter pour 4008.** La question ouverte (d) de `socle-instances-jeu.md` peut être
considérée comme close sur le point « 4008 n'est pas utilisé par le client 7.3 ».

### (e) Attribution exacte du handler `0x5673f0`
Borne inférieure ferme (constructeur `0x5665e0` de `SUIHuntaHolicScoreBoardWnd`, enregistré en
`0x63670c`), mais la borne supérieure n'a pas été lue (aucune autre fenêtre HuntaHolic n'est
journalisée par le client). L'attribution repose sur le nom comparé, la taille d'objet (`0x510`) et
le voisinage — solide, mais indirect. Sans effet sur le §3 (structure) ni sur le §5 (absence de
réponse).

### (f) L'id interne de l'objet-message `huntaholic`
Le client récupère un objet par son **nom** (`0xa1fa18` = `"huntaholic"` dans le gestionnaire
`[0xc4b28c]+0x144` via `0x48b760`) : l'identifiant interne de cet objet (et le rôle du second objet
`huntaholic_scoreboard`) n'est pas établi. Sans effet ici : c'est la fonction `0x4c9380` qui fixe
l'id 4005.

## 8. Commits et binaires épinglés

| Référence | Épinglage | Rôle |
|---|---|---|
| Navislamia | `b56967a07430422add88e0e5cdf292b41b18f6c6` (= `origin/master`, « Merge pull request #44 … ») | base de la branche de ce lot ; état des fichiers cités (`GamePackets.cs`, `GameClient.cs`, fiche de socle) |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (« packets: fix TS_SC_INVENTORY with older epics ») | ids, ordre des champs, gating (`TS_CS_HUNTAHOLIC_LEAVE_INSTANCE.h`, `PacketDeclaration.h`, `PacketEpics.h`) |
| NGemity (`reference/ngemity`) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03, « Fix compilation issue for GCC ») | logique serveur : absente pour 4005 (bras inexistant, bloc de compétences commenté) ; `EPIC = EPIC_4_1_1` (`Define.h:25`) |
| Client 7.3 `reference/client73/SFrame.exe` | `sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, 9 841 664 octets | constructeur `0x4c9380`, appelants `0x5641f2`/`0x567453`, répartiteur entrant `0x67e1d9` et ses tables |

Mesures refaites dans ce contexte (pas de valeur héritée d'une autre fiche) : `ctime`, taille et
`sha256` de `SFrame.exe` ; `git rev-parse` des trois dépôts ; lecture des tables `0x67f560`/`0x67f580`
et désassemblage des cibles `0x6708a0`, `0x670910`, `0x6709a0`, `0x670a00`, `0x670b10`, `0x670b60`,
`0x670bb0` ; extraction des chaînes `"button_entrance_02"`/`"button_entrance_01"` (`0xa2c05c` /
`0xa2c070`), `"huntaholic"` (`0xa1fa18`), `"huntaholic_scoreboard"` (`0xa2cac8`), et des journaux
`"Create: SUIHuntaHolic*Wnd"`.

## 9. Annexe — lecture du répartiteur entrant, et ce qu'elle clôt dans le socle

**Recette** (utilisable pour n'importe quel id) : dans la fonction `0x67e1d9`, repérer le bloc
`cmp eax,<max>` / `ja 0x67ef21` / `sub eax,<base>` / `cmp eax,<count>` ; lire l'octet
`table_octets[id - base]` puis l'adresse `table_sauts[octet]`. Un octet menant à `0x67ef21` signifie
**aucun handler** (branche « message non traité »), un octet menant à `0x67e7xx` désigne un stub
`push ebx; mov ecx,esi; call <handler>; jmp 0x67ef39` dont la cible est le handler réel.

**Résultat pour la famille entière** (bloc `0x67e736`, base 4002, 251 entrées) :

* routés **en entrée** : 4001 (`0x67e729` → `0x6707f0`), puis 4002, 4006, 4007, 4009, 4010, 4012,
  4253 par les tables (§5.2) ;
* **non routés** (branche par défaut) : 4000, 4003, 4004, **4005**, 4008, 4011, 4250, 4251, 4252.

**Ce que cela clôt** dans `docs/packet-specs/socle-instances-jeu.md` :

* §7a (« le client 7.3 reçoit-il 4253 ? ») : **oui**, handler `0x670bb0`, et le handler lit quatre
  dwords aux offsets 7/11/15/19 — soit exactement la trame de 23 octets de la fiche ;
* §7e (« routage id ↔ handler, appariement indirect ») : **établi directement** pour les six
  membres S→C (4001, 4002, 4006, 4007, 4009, 4010, 4012) — l'identité de structure n'est plus la
  seule preuve ;
* §7f (« `4010` pire cas ») : **attribué**, handler `0x670b10`, objet sans charge (trame de 7 octets) ;
* §7d (« 4008 utilisé ? ») : **non**, ni en émission ni en réception.
* Ces constats valent pour les branches sœurs 4000/4003/4004 : la même recette à leur donner.
* Le report dans la fiche de socle est un geste de PO (fichier de `master`), il ne fait pas partie de
  ce lot.

**Piège évité, à ne pas rejouer** : une fonction du client (`0x99fa95`, table de sauts jusqu'à
`0x9ab7ba`) contient un `mov ecx,0xfa5` et des clés à `0x80xxxxxx` : ce n'est **pas** un répartiteur de
paquets mais une table `code d'erreur Win32 → chaîne`, où la clé 4005 vaut
`"ERROR_REC_NON_EXISTENT"` — la cible de l'id 4005 y est cette chaîne, lue dans `.rdata`. Un
dépouillement rapide des immédiats ferait conclure à tort à un handler 4005. Le discriminant est le
couple `push <chaîne .rdata>` / retour de chaîne, et l'absence de toute écriture de longueur de trame.

## 10. Brouillon pour la description de la MR (`navis-dev`)

Bloc proposé pour `CLAUDE.md` — **le dev ne l'écrit pas dans `CLAUDE.md`** (Hermes refuse l'écriture),
il le colle dans la description de la MR comme l'exige le critère transversal 5. À ajuster après
implémentation :

```markdown
### Paquet 4005 — `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE` (quitter une instance HuntaHolic)

* **7 octets, en-tête seul, aucun champ** (rzu `X(4005, true)`, `DEF(_)` vide ; NGemity idem ;
  constructeur client `0x4c9380` : `Length = 7` en dur, id `0x0fa5`).
* **Aucun gating de version** : id inconditionnel dans rzu, aucun champ, donc rien à trancher pour 7.3.
* **Client → serveur, sans réponse possible** : le répartiteur entrant du client (`0x67e1d9`, bloc
  `0x67e736`) envoie 4005 vers la branche « message non traité » (`0x67ef21`) ; il n'existe aucun
  paquet de réponse dans la famille en 7.3.
* **Deux seuls émetteurs** dans le client 7.3 : la fenêtre d'instance
  (`SUIHuntaHolicInstanceWnd`, contrôle `button_entrance_02`, `0x5641f2`) et le tableau des scores
  (`SUIHuntaHolicScoreBoardWnd`, contrôle `huntaholic_scoreboard`, `0x567453`).
* **Conduite serveur en 7.3** : lecture + validation de la longueur exacte (7), journal `Debug`,
  aucun état modifié (aucun lobby/instance HuntaHolic n'existe côté serveur) et **aucune réponse**.
* **`4008` (`TM_CS_HUNTAHOLIC_LEAVE_LOBBY`) n'est jamais émis ni reçu par le client 7.3** : ne rien
  implémenter pour cet id.
* Fiche : `docs/packet-specs/4005-huntaholic-leave-instance.md`.
```

## 11. Implémentation livrée (lot `navis-dev`)

Branche `hermes/packet-4005-huntaholic-leave-instance` (base `master` = `b56967a`). Trois commits :
`2a30811` (fiche, archéologue), `c76d275` (code et tests), puis le commit documentaire qui ajoute la
présente section. Le contrat du §5.4 a été suivi tel quel : son lecteur (`IsLeaveInstance`), son nom de
fichier de tests et sa politique de refus sont ceux qui sont livrés.

### 11.1 Checklist des critères transversaux

| Critère | Mesure relevée | Verdict |
|---|---|---|
| `dotnet build Navislamia.sln -c Debug` | code de sortie **0** (164 avertissements, 0 erreur ; `NUGET_PACKAGES=/srv/navislamia/.nuget-cache`) | OK |
| `dotnet test Tests/Tests.csproj` | code de sortie **0**, **1327 réussis / 0 échec** (base relevée avant le lot : **1302 réussis / 0 échec** ; le lot ajoute **25** cas et n'en retire aucun) | OK |
| Au moins un test d'offsets | `Tests/Game/HuntaholicLeaveInstancePacketsTests.cs` : taille totale 7, `Length` en 0-3, id en 4-5 valant `0x0FA5`, checksum en 6, refus de 0/1/6/8/9/11/28/55, acceptation de 7 | OK |
| Enum et dispatch modifiés ensemble | membre `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE = 4005` **et** bras `if (header.ID == …)` dans `OnDataReceived`. Mesure : 140 membres dans `GamePackets`, **44** sans référence dans `GameClient.cs`, **0** de ces 44 n'est un `TM_CS_*` (les 44 sont des `TM_SC_*`, émission seule, qui ne peuvent pas atteindre le `switch` final) | OK |
| Savoir durable dans la fiche + bloc `CLAUDE.md` | présent §11 et §10 ; le bloc `CLAUDE.md` part dans la description de la MR et le compte rendu de la carte. **`CLAUDE.md` n'a pas été modifié** (fichier d'instructions protégé par Hermes) | OK |
| Version tranchée | `X(4005, true)` — entrée unique et inconditionnelle ; aucun champ, donc aucun gating de champ possible | OK |
| Aucun commit sur `master` locale | `git log --oneline origin/master..master` → **0** ligne | OK |
| Aucun `NON ÉTABLI` deviné | §7 (a) (b) (c) (d) (e) (f) laissés intacts ; aucune conduite de retour de personnage, aucun paquet de réponse, rien pour 4008 | OK |

### 11.2 Fichiers livrés (commit `c76d275`)

| Fichier | Nature |
|---|---|
| `Game/Network/Packets/Game/GameHuntaholicPackets.cs` | **neuf** : classe `GameHuntaholicPackets` (`HeaderSize = 7` privé), `LeaveInstanceLength = HeaderSize` public, `IsLeaveInstance(ReadOnlySpan<byte>)` |
| `Game/Network/Packets/Enums/GamePackets.cs` | membre `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE = 4005` |
| `Game/Network/Clients/GameClient.cs` | `HandleHuntaholicLeaveInstance(byte[])` et son bras de dispatch |
| `Tests/Game/HuntaholicLeaveInstancePacketsTests.cs` | **neuf** : 25 cas |
| `docs/packet-specs/4005-huntaholic-leave-instance.md` | la présente section |

### 11.3 Offsets livrés et noms des tests

Aucun offset de champ n'existe (charge nulle) : la seule constante est `LeaveInstanceLength = 7`, et les
tests des offsets d'en-tête sont `ClientPacket_UsesTheBareHeaderLayout` (`Length` en 0-3, id en 4-5,
checksum en 6 valant `0xBB` pour une trame bien formée), `ClientPacket_HasNoFieldOutsideTheHeader`
(`Marshal.SizeOf<Header>() == 7 == LeaveInstanceLength`), `ClientPacket_AnnouncesItsFieldsLittleEndian`
(octets posés à la main, plus les assertions « pas la valeur big-endienne ») et
`IsLeaveInstance_TakesNoOutputValue` (réflexion : un seul paramètre `ReadOnlySpan<byte>`, aucun `out`,
retour `bool` — il n'y a rien à extraire).

Refus par la longueur : `IsLeaveInstance_RejectsAnyLengthOtherThanSeven` (8 cas de 0 à 55, dont 11/28/55 =
longueurs des trames sœurs 4000/4004/4003). Boucle de réception réelle (harnais `StorageTestHarness`) :
`OnDataReceived_ConsumesThePacketWithoutThrowing`, `OnDataReceived_AnswersNothing`,
`OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne`,
`OnDataReceived_ConsumesAMalformedFrameWithoutThrowing` (3 longueurs),
`OnDataReceived_StopsOnAFrameWithABadChecksum`, et `Packet_IsDispatchedBeforeTheUnknownPacketThrow`
(lecture du source : le bras précède le `switch` final, contrôle de repli assumé).

### 11.4 Réponses émises

**Aucune.** Le handler journalise (`Warning` si la longueur n'est pas 7, sinon `Debug`), ne touche à aucun
état et n'appelle aucun `Connection.Send` : `OnDataReceived_AnswersNothing` et les tests de trame
coalescée assertent `Sent` vide. C'est exactement ce que prescrit le §5.3, et la raison est mesurée côté
client au §5.2 (4005 n'a aucun bras de réception).

### 11.5 Preuves par mutation (toutes restaurées, arbre final propre)

| Mutation | Mesure | Interprétation |
|---|---|---|
| Bras de dispatch rendu inatteignable (`header.ID == … TM_NONE`, id existant donc compilable) | `dotnet test --filter "FullyQualifiedName~HuntaholicLeaveInstance"` → code **1**, **7 échecs / 18 réussis**, **6** occurrences de `Unknown Packet Type` | le membre atteint bien le `switch` final si le bras disparaît : les 6 tests de boucle échouent sur le `throw`, le 7e est le contrôle de source |
| Lecteur `== LeaveInstanceLength` → `>= LeaveInstanceLength` | filtre identique → code **1**, **5 échecs / 20 réussis**, exactement les cinq cas de longueur > 7 (`PaddedFrame`, `FrameWithOneExtraByte`, `LobbyListFrameLength`, `JoinFrameLength`, `CreationFrameLength`) | les cas de refus portent la règle « exactement 7 », pas un littéral décoratif |
| Restauration | `git checkout --` sur chaque fichier, `git status --porcelain` vide, `git rev-parse --short HEAD` = `c76d275`, filtre relancé → **25 / 25** | l'arbre testé est bien la tête commitée |

### 11.6 Mesures de fusionnabilité (`git merge-tree --write-tree --name-only`, aucune écriture)

| Fusion jouée | Code | Chemins en collision |
|---|---|---|
| 4005 ↔ `hermes/packet-4000-huntaholic-instance-list` | 1 | `GameHuntaholicPackets.cs` (add/add) |
| 4005 ↔ `hermes/packet-4004-huntaholic-join-instance` | 1 | `GameHuntaholicPackets.cs` (add/add) |
| 4005 ↔ `hermes/packet-4003-huntaholic-create-instance` | 1 | `GameHuntaholicPackets.cs` (add/add) **et** `GameClient.cs` |
| 4000 ↔ 4003, 4000 ↔ 4004, 4003 ↔ 4004 (sans 4005) | 1 | `GameHuntaholicPackets.cs` pour les trois, **et `GameClient.cs` pour les deux paires contenant 4003** |

Lecture : la collision sur `GameHuntaholicPackets.cs` est **add/add par construction** (quatre branches
créent le fichier de la famille, cf. §5.4) et ne peut pas être évitée ; la collision `GameClient.cs` avec
4003 est **préexistante** (4003 la produit aussi contre 4000 et 4004, sans 4005 dans le train). Le
placement retenu tient donc sa promesse : avec 4000 et 4004, seul le fichier de famille entre en
collision. Les emplacements choisis sont :

* **énum** : après le bloc `TM_CS_SECURITY_NO = 9005`, avant `TM_SC_COMMERCIAL_STORAGE_INFO = 10003`
  (aucune branche sœur n'écrit là ; 4000 s'ancre près de 2006, 4003 près de 4253, 4004 près de 5001) ;
* **bras de dispatch** : juste avant le `switch` final, après le bras `TM_CS_XTRAP_CHECK` (4000 s'ancre
  près du bras 4250, 4004 près du bras 60, 4003 près de `TM_CS_CHANGE_LOCATION`) ;
* **handler** : juste après `HandleInstanceGameExit` (le voisin nommé par le §5.4).

### 11.7 Écarts relevés dans la présente fiche par le lot

* **§5.5, ligne « id 4005 avec un checksum faux »** : corrigée sur place (datée), le lecteur ne vérifie
  pas le checksum mais la **boucle de réception le vérifie avant tout dispatch** — la trame n'est donc
  jamais traitée, contrairement au mot « accepté » du tableau. Comportement général du serveur, pas écart
  propre à 4005 ; `OnDataReceived_StopsOnAFrameWithABadChecksum` l'épingle.
* **§5.4** : livré tel quel (aucune divergence de nom, d'offset ou de politique de refus), y compris le
  nom de fichier de tests annoncé.

### 11.8 Commandes relevées

```
export NUGET_PACKAGES=/srv/navislamia/.nuget-cache
dotnet build Navislamia.sln -c Debug                  # base : 0 / lot : 0 (164 avert., 0 erreur)
dotnet test Tests/Tests.csproj                        # base : 0 (1302 réussis) / lot : 0 (1327 réussis)
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~HuntaholicLeaveInstance"   # 0 (25 réussis)
git log --oneline origin/master..master               # 0 ligne
git merge-tree --write-tree --name-only hermes/packet-4000-huntaholic-instance-list HEAD  # 1 (fichier de famille)
```

## A VERIFIER PAR KILLIAN

1. **Fiche de référence de l'archéologue (commit `2a30811`)** : aucun fichier de code n'y était modifié.
   **État du code (27/09/2026, lot `navis-dev`, commit `c76d275`)** : le contrat du §5.4 est **livré**
   (lecteur, membre d'énum `TM_CS_HUNTAHOLIC_LEAVE_INSTANCE = 4005`, bras de dispatch, handler, 25 cas de
   test, build et tests en code 0) ; `CLAUDE.md` reste **non** modifié (fichier protégé, bloc préparé dans
   la MR). Mesures et placements au §11.
2. **Point d'arbitrage (a)** : la conduite serveur d'un 4005 (retour du personnage, paquet de
   téléportation) reste non établie — c'est la seule décision métier qui manque, et elle n'est pas
   nécessaire pour le lot de lecture (no-op documenté, comme 4251). **État du code (27/09/2026)** : le
   handler journalise et ne change **aucun** état, n'émet **aucune** réponse (§11.4) ; le point reste
   ouvert pour le lot « instance HuntaHolic ».
3. **Point d'arbitrage (b)** : le bouton `button_entrance_02` émet `LEAVE_INSTANCE` alors que son nom
   dit « entrée ». À confirmer par observation en jeu si un jour le client est lancé.
4. **Report dans la fiche de socle** : les points (a), (d), (e) et (f) de
   `socle-instances-jeu.md` sont désormais tranchables par la table du §9 (4253 reçu, appariement
   direct, `4010` attribué, `4008` non utilisé). Le report est un geste de PO sur `master`, pas de ce
   lot.
5. **Collision de fichier annoncée** : quatre branches (4000, 4003, 4004, 4005) créent
   `Game/Network/Packets/Game/GameHuntaholicPackets.cs` avec la même classe. La règle de fusion
   (une seule déclaration, membres fusionnés) est écrite au §5.4 ; elle devra être appliquée au
   deuxième merge. **Mesure du lot (27/09/2026, §11.6)** : `git merge-tree` ne montre que cette collision
   add/add contre 4000 et 4004, plus une collision `GameClient.cs` contre 4003 qui est **préexistante**
   (4003 la produit aussi contre 4000 et 4004, sans 4005 dans le train) ; l'énum ne collisionne jamais.
