# 259 — TM_CS_DONATE_REWARD

Direction retenue : **client → serveur**. Réponse attendue : l'accusé générique `TM_SC_RESULT (0)` dont le
champ `RequestMsgID` porte `259` (§5.2) — le client ouvre un bloc de résultat dédié à ce request id.

Toute preuve issue de `reference/client73/` provient d'une **lecture statique** (`objdump -d -M intel`,
`strings`, lecture des tables de dispatch par script Python) : aucun binaire, aucun Lua et aucun script du
client n'a été exécuté. Les adresses (`VA`) citées sont celles de `reference/client73/SFrame.exe` :
`image base 0x400000`, `.text` VA `0x401000` / fichier `0x400`, `.rdata` VA `0xa0f000` / fichier `0x60da00`,
`.data` VA `0xc10000` / fichier `0x80e200`. Les numéros de ligne cités pour `SFrame.exe` sont ceux du dump
`strings -n 4 SFrame.exe` (reproductible localement).

Identités stables des binaires de référence :

| Fichier | sha256 |
| --- | --- |
| `reference/client73/SFrame.exe` | `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` |
| `reference/client73/db_string.rdb` | `4e8e3e06d08391bed554d4520992d3e629713589978b197342091d13b8f299e1` |

Base du dépôt : `master = ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (merge de la MR #4). Cette fiche
n'ajoute qu'un fichier Markdown : elle ne modifie ni l'énumération, ni le dispatch, ni les tests.

## 1. Identité et claimant retenu

| Élément | Valeur | Source |
| --- | --- | --- |
| id décimal | `259` (`0x0103`) | `op_codes.md:86` — `[259] = "TM_CS_DONATE_REWARD"` |
| nom | `TM_CS_DONATE_REWARD` | `op_codes.md:86` |
| claimant **montant retenu** | `TS_CS_DONATE_REWARD` — `X(259, version < EPIC_9_6_3)` | `reference/rzu/librzu/src/packets/GameClient/TS_CS_DONATE_REWARD.h:16-18` |
| claimant **descendant concurrent** | `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW` — `X(259, version < EPIC_9_6_3)` | `reference/rzu/librzu/src/packets/GameClient/TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW.h:7-9` ; `reference/ngemity/shared/Server/ClientPackets.h:100` |
| présence dans l'enum du dépôt | **absente** — la zone déclare `TM_CS_USE_ITEM = 253`, `TM_SC_DESTROY_ITEM = 254`, `TM_SC_UPDATE_ITEM_COUNT = 255`, puis passe à `TM_SC_HAIR_INFO = 220` | `Game/Network/Packets/Enums/GamePackets.cs:41-44` |
| présence du nom 258 (`TM_CS_DONATE_ITEM`) | **absente** aussi (`grep -rni donate Game/` → 0 occurrence) | `Game/Network/Packets/Enums/GamePackets.cs:41-48` |
| dispatch montant | **aucun bras** : le `switch` final du `Receive` lève `Unknown Packet Type` | `Game/Network/Clients/GameClient.cs:791-802` (repli `_ => throw ...`, l. 802) |

Le dev doit donc ajouter `TM_CS_DONATE_REWARD = 259` **et** son bras de traitement dans le même commit
(critère d'acceptation 4). Le pendant descendant attendu est `TM_SC_RESULT`, déjà présent
(`Game/Network/Packets/Enums/GamePackets.cs:5`) : aucun id SC nouveau n'est nécessaire pour cette fiche.

### 1.1 Le doublon d'id 259 — ce que le client 7.3 tranche

Deux paquets rzu portent le numéro 259 pour Epic 7.3 : le montant `TS_CS_DONATE_REWARD` et le descendant
`TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW`. La fiche du socle artisanat (`socle-artisanat-objets.md` §1.1) avait
laissé ce point en `NON ÉTABLI`, gardé le descendant **hors** de l'énumération et verrouillé cette absence
par un test. La lecture du client 7.3 tranche : **les deux claims existent réellement, mais dans des
directions opposées**. Les deux preuves sont indépendantes.

**a) Le client 7.3 émet bien 259** — chaîne complète, du clic au frame (§2 pour le détail du geste) :

| Maillon | Preuve (VA) |
| --- | --- |
| boîte de confirmation : le contrôle cliqué est comparé à `contribution_ok` | `push 0xa3d068` @ `0x5F0293` (chaîne `contribution_ok`, `strings` l. 23916) ; `call 0x41E290` @ `0x5F0299` |
| construction du message interne de clé `0x2C24` = 11300 | `mov DWORD PTR [ebp-0x28],0x2c24` @ `0x5F02CB` ; classe RTTI `.?AUSIMSG_UI_ACT_REQUESTREWARDITEM@@` (vtable `0xa3c9fc` @ `0x5F02E3` → COL `0xbc4fb8` → TD `0xc1d5ac`) |
| routage de la clé 11300 vers le constructeur du frame | `cmp eax,0x2c24` @ `0x49E71D` / `je 0x49e746` @ `0x49E723` |
| stub d'appel | `push edi` / `mov ecx,esi` / `call 0x48e530` @ `0x49E746-0x49E749` |
| constructeur : id littéral écrit dans le frame | `mov eax,0x103` @ `0x48E53E` ; `mov WORD PTR [ebp-0x8],ax` @ `0x48E546` |
| envoi sur le lien | `mov ecx,[eax+0xb8]` / `mov eax,[ecx]` / `mov edx,[eax+0xc4]` / `call edx` @ `0x48E60B-0x48E623` |

**b) Le client 7.3 consomme aussi un 259 entrant** — le répartiteur des paquets **reçus** (fonction
englobant `0x67E1D9`) lit l'id reçu à l'offset `+4` du buffer (`movzx ecx,WORD PTR [ebx+0x4]`
@ `0x67DF59`), puis :

```
0x67E1D9  cmp  eax,0x1F8                     ; 504
0x67E1E4  je   0x67EF39                      ; 504 -> queue libre (aucun handler)
0x67E1EA  sub  eax,0xFF                      ; base 255
0x67E1EF  cmp  eax,0xF5                      ; 245 entrées -> ids 255..500
0x67E1F4  ja   0x67EF21                      ; « 처리되지 않은 메세지 » (message non traité)
0x67E1FA  movzx edx,BYTE PTR [eax+0x67F218]  ; table d'octets
0x67E201  jmp  DWORD PTR [edx*4+0x67F19C]    ; table de sauts
```

Pour l'id 259 : index `259-255 = 4` → octet `2` (table `0x67F218`) → entrée 2 (table `0x67F19C`) =
stub `0x67E2FF` = `push ebx` / `mov ecx,esi` / `call 0x66FC20` / `jmp 0x67EF39`. Le handler `0x66FC20`
journalise la chaîne coréenne `보석 장착 열어` (« ouverture de l'équipement de joyaux », `.rdata` VA
`0xa52a5c`) et poste un message interne de **classe `.?AUSMSG_OPEN_JEWEL_EQUIP@@`** (vtable `0xa51fd0`).
Son jumeau est l'id 261 : handler `0x66FC80`, chaîne `보석 내구도 수리열어` (VA `0xa52a74`), classe
`.?AUSMSG_OPEN_SOUL_REPAIR@@` — soit exactement `TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW (261)`
(`op_codes.md:89`). La symétrie 259/261 (fenêtre d'équipement de joyaux / fenêtre de réparation) est
celle des deux claims rzu.

**c) Contrôle croisé de la direction** — la même lecture appliquée aux deux plages de la table (255-500 et
505-710) restitue **45 ids routés**, dont les noms internes correspondent aux noms SC d'`op_codes.md`
(extrait mesuré) :

| id | classe du message interne (RTTI) | nom attendu (`op_codes.md`) |
| --- | --- | --- |
| 255 | `.?AUSMSG_UPDATE_ITEM_COUNT@@` | `TM_SC_UPDATE_ITEM_COUNT` |
| 257 | `.?AUSMSG_MIX_RESULT@@` | `TM_SC_MIX_RESULT` |
| 259 | `.?AUSMSG_OPEN_JEWEL_EQUIP@@` | *(claim descendant contesté)* |
| 261 | `.?AUSMSG_OPEN_SOUL_REPAIR@@` | `TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW` |
| 282 | `.?AUSMSG_ITEM_DROP_INFO@@` | `TM_SC_ITEM_DROP_INFO` |
| 283 | `.?AUSMSG_USE_ITEM_RESULT@@` | `TM_SC_USE_ITEM_RESULT` |
| 286 | `.?AUSMSG_SKILLCARD_INFO@@` | `TM_SC_SKILLCARD_INFO` |
| 287 | `.?AUSMSG_ITEM_WEAR_INFO@@` | `TM_SC_ITEM_WEAR_INFO` |
| 301 | `.?AUSMSG_ADD_SUMMON_INFO@@` | `TM_SC_ADD_SUMMON_INFO` |
| 303 | `.?AUSMSG_EQUIP_SUMMON@@` | `TM_EQUIP_SUMMON` |
| 350-353 | `.?AUSMSG_UNSUMMON_PET@@`, `ADD_PET_INFO`, `REMOVE_PET_INFO`, `SHOW_SET_PET_NAME` | `TM_SC_UNSUMMON_PET` … `TM_SC_SHOW_SET_PET_NAME` |

**Décision.** Le claimant **retenu par cette fiche est le montant `TS_CS_DONATE_REWARD = 259`** : le client
7.3 en construit la trame (preuve a) et en attend un accusé (§5.2). Le claimant **descendant est laissé
`NON ÉTABLI`** : sa *consommation* par le client est établie (preuve b), mais **rien** dans le client ne
permet de savoir si un serveur 7.3 émet un jour ce frame. La décision du socle (le garder hors de
`GamePackets`) reste donc valable, avec une raison mieux fondée qu'« id non établi » : l'énumération C# du
dépôt est unique, y déclarer les deux entités créerait **deux membres de valeur 259**, alors que seule la
direction montante est attestée comme émise. La question ouverte du socle (« collision réelle dans le
modèle de rzu ») se reformule donc en « claims de directions opposées » : le serveur sait qui lui a envoyé
un frame, et le client ne reçoit jamais ses propres envois. Il n'y a pas de collision de protocole à
trancher, seulement une décision d'énumération, portée en réserve (§7.4).

## 2. Ce que le joueur fait pour que le client l'envoie

Le paquet part de la **fenêtre de dons (autel de la déesse)**, à la confirmation d'une demande de
récompense. Éléments relevés :

| Élément | Contenu | Source |
| --- | --- | --- |
| fenêtres | `window_donation_main.nui`, `window_donation_msgbox.nui` | `SFrame.exe` `strings` l. 24847, 24782 |
| 4 lignes de récompense déclarées | `selectitem_itemback_01` … `_04` et `static_itemcount01` … `04` | `SFrame.exe` `strings` l. 23868-23871, 23899-23903 |
| modèles de noms de lignes | `selectitem_itemback_%02d`, `static_itemcount%02d` (poussés @ `0x5EFDEB`, `0x5EFDFA`) | VA `.rdata` `0xa3cf58`, `0xa2fd94` |
| boucle de remplissage des lignes | `mov ebx,1` @ `0x5EFDCD` ; base `lea edi,[esi+0x4a0]` @ `0x5EFDE1` ; pas `add edi,8` @ `0x5EFECB` ; borne au global `[0xc1ab04]` (`cmp`, `jl` @ `0x5EFECE`) | handler `0x5EFDB0` |
| contrôle de confirmation | `contribution_ok` ; boîte construite avec `box_title`, `button_ok`, `button_cancel` | VA `0xa3d068`, `0xa3ced4` ; `strings` l. 23916, 23897 |
| classe de la fenêtre de confirmation | `.?AVSUIContributionRewardConfirmWnd@@` | `SFrame.exe` `strings` l. 44267 |
| message interne émis par le clic | `SIMSG_UI_ACT_REQUESTREWARDITEM`, clé `0x2C24` = 11300 | vtable `0xa3c9fc` ; `mov DWORD PTR [ebp-0x28],0x2c24` @ `0x5F02CB` |
| texte d'usage des lignes | `Cost: #@moralpoint@# Points.<br>Double-click to increase redemption quantity by 1.…` | `db_string.rdb` `ui_text_6747` (l. 112876) |
| texte d'échec | `ui_text_6746` = « Player cannot acquire item due to loss of moral points. », poussé par le chemin d'émission lui-même (`push 0x1a5a` @ `0x5F0368`) | `db_string.rdb` (l. 112874) ; `SFrame.exe` |

**Geste retenu** : le joueur ouvre la fenêtre de dons, sélectionne une ou plusieurs lignes de récompense,
augmente leur quantité, puis valide la boîte de confirmation par le contrôle `contribution_ok`. Le client
construit alors le message interne `SIMSG_UI_ACT_REQUESTREWARDITEM` (clé 11300) à partir de son propre
état — les 4 champs `u16` lus en `[esi+0x4a0]`, `[esi+0x4a8]`, `[esi+0x4b0]`, `[esi+0x4b8]`
@ `0x5F02EA-0x5F031F`, recopiés en `msg+0x13`, `+0x15`, `+0x17`, `+0x19` (`mov WORD PTR [ebp-0x19],dx`
@ `0x5F02FF`, `[ebp-0x17]` @ `0x5F030A`, `[ebp-0x15]` @ `0x5F0311`, `[ebp-0x13]` @ `0x5F031F`) —, le poste
(`call 0x6491C0` @ `0x5F0323`), et le routeur d'actions UI (`0x49E71C`) le transforme en **frame 259**.

**Émission du 259 par ce client : ÉTABLIE** (chaîne a : id littéral `0x103` écrit dans le frame
@ `0x48E53E`/`0x48E546`, puis envoi @ `0x48E60B-0x48E623`). Une recherche exhaustive de l'immédiat `0x0103`
dans `.text` ne donne **qu'un seul site d'émission** (`0x48E53E` ; les 137 autres occurrences sont des
déplacements de branchement) : le client 7.3 n'a qu'un constructeur pour ce paquet. Ce qui n'est **pas**
établi est le contrôle exact qui ouvre la boîte de confirmation (§7.3) et la table des lignes côté serveur
(§7.7).

Le même chemin contient un garde-feu **client** sur les points moraux (`lea eax,[eax+eax*4]`,
`imul eax,eax,0x3E8`, `cmp ecx,0x3E8` @ `0x5F0346-0x5F0357`), suivi de l'affichage de `ui_text_6746` :
c'est cosmétique, la décision reste serveur. Indice non exploité à ce stade : le bloc de libellés de la
fenêtre contient quatre valeurs de points moraux `30000`, `10000`, `5000`, `1000` et quatre icônes de
récompense (`icon_donation_reward_gold|silver|copper|gray`) — cohérent avec 4 lignes, mais **non probant**
(§7.2).

## 3. Structure sur le fil

**Taille totale attendue : `8 + 3 × N` octets**, `N` = nombre d'enregistrements de récompense
(`0 ≤ N ≤ 4` dans le client 7.3). Soit `8`, `11`, `14`, `17` ou `20` octets — jamais une autre valeur.

| Offset | Taille | Type | Nom | Valeur attendue | Source |
| --- | --- | --- | --- | --- | --- |
| 0 | 4 | `uint32` LE | `Length` | `8 + 3N`, **en-tête compris** | constructeur client `0x48E530` : gabarit `mov DWORD PTR [ebp-0xc],0x8` @ `0x48E54A` (cas `N=0`), puis `lea edx,[eax+eax*2+0x8]` @ `0x48E5A4`, recopié @ `0x48E5CD` ; en-tête de 7 octets d'rzu : `reference/rzu/librzu/src/lib/Packet/PacketDeclaration.h:616-621` |
| 4 | 2 | `uint16` LE | `ID` | `259` | `mov eax,0x103` @ `0x48E53E` ; `mov WORD PTR [ebp-0x8],ax` @ `0x48E546` ; rzu `.../TS_CS_DONATE_REWARD.h:17` |
| 6 | 1 | `uint8` | `Checksum` | somme des 6 premiers octets, calculée par le client | boucle @ `0x48E551-0x48E561` (recalcul après la longueur définitive @ `0x48E5AD-0x48E5BD`) ; côté dépôt `Game/Network/Packets/Header.cs:11`, `:24`, `Game/Network/Packets/PacketExtensions.cs:13` — **non imposé en réception** |
| 7 | 1 | `int8_t` (rzu) / `uint8` | `count` de `rewards` | **`N`** — nombre d'enregistrements qui suivent | rzu `.../TS_CS_DONATE_REWARD.h:13` (`_(count)(int8_t, rewards)`) ; client : compteur incrémenté pour chaque champ non nul (`0x48E573`, `0x48E57A`, `0x48E586`, `0x48E592`, `0x48E59E`) et recopié dans le frame par `mov eax,DWORD PTR [ebp-0x8]` @ `0x48E5C7` + `mov DWORD PTR [esi+4],eax` @ `0x48E5CF` |
| `8 + 3k` | 1 | `int8_t` | `reward_type` (1<sup>er</sup> champ de `TS_REWARD_INFO`) | position du champ non nul parmi les 4, comptée **à partir de 0** | rzu `.../TS_CS_DONATE_REWARD.h:6` ; client : `mov BYTE PTR [ebp+8],al` @ `0x48E5EC` (compteur initialisé à 0 @ `0x48E5D5`, incrémenté @ `0x48E5FF`, borné à 4 @ `0x48E603`) puis `mov WORD PTR [ecx],dx` @ `0x48E5F3` |
| `8 + 3k + 1` | 2 | `uint16` LE | `count` (2<sup>e</sup> champ de `TS_REWARD_INFO`) | quantité de la ligne (`static_itemcount%02d`) | rzu `.../TS_CS_DONATE_REWARD.h:7` ; client : octet bas écrit @ `0x48E5F3`, octet haut par `mov BYTE PTR [ecx+2],dl` @ `0x48E5F9` |
| — | 3 | — | pas de remplissage, pas de champ de version | l'enregistrement fait **exactement 3 octets** | `add ecx,3` @ `0x48E5FC` ; rzu `TS_REWARD_INFO` = `int8_t` + `uint16_t` |

En-tête d'rzu (`.../TS_CS_DONATE_REWARD.h:12-14`) : `_(count)(int8_t, rewards)` puis
`_(dynarray)(TS_REWARD_INFO, rewards)` → charge utile `1 + 3N`, donc trame `7 + 1 + 3N = 8 + 3N`. Les deux
lectures (client 7.3 et rzu) donnent la **même longueur** ; NGemity porte la même définition (§5.1).

Bornes et cohérence :

- **client 7.3 : `N ≤ 4`** — le constructeur itère sur exactement 4 champs (`cmp eax,4` @ `0x48E608`) et
  n'écrit un enregistrement que pour un champ non nul (`test dx,dx` / `je` @ `0x48E5E3-0x48E5E6`) ; la
  ressource déclare exactement 4 lignes (`selectitem_itemback_01..04`, §2).
- **rzu : `N ≤ 127`** — `getClampedCount<int8_t>` plafonne à `numeric_limits<int8_t>::max()`
  (`reference/rzu/librzu/src/lib/Packet/PacketDeclaration.h:85-88`, appel `:343-345`). Le type `int8_t`
  est **signé** : un octet `≥ 0x80` est négatif si on le lit tel quel.
- **le `count` de `+7` et la longueur sont cohérents par construction** côté client (tous deux dérivés du
  même `N`) : `count != (Length - 8) / 3` est donc, pour un client 7.3, une **trame non conforme**, pas
  une variante de format.
- **trame vide légitime** : `N = 0` → 8 octets, `count = 0`, aucun enregistrement. Le client peut
  l'émettre (aucun garde n'annule l'envoi quand les quatre lignes sont à zéro) : le serveur doit la
  traiter, pas la rejeter comme corruption.

## 4. Gating de version

Version de référence : **Epic 7.3 = `0x070300`** (`reference/rzu/librzu/src/lib/Packet/PacketEpics.h:59`).

| Élément | Gating rzu | Décision pour 7.3 |
| --- | --- | --- |
| id du paquet (montant) | `X(259, version < EPIC_9_6_3)` / `X(1259, version >= EPIC_9_6_3)` (`.../TS_CS_DONATE_REWARD.h:16-18`) | **`259`**. `EPIC_9_6_3 = 0x090603` (`PacketEpics.h:96`, remap des ids GS de `+1000` pour la plage concernée) ; `0x070300 < 0x090603` → jamais 1259 dans le périmètre Navislamia. |
| id du paquet (descendant concurrent) | même bascule `259 → 1259` (`.../TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW.h:7-9`) | sans objet ici : ce claim est laissé `NON ÉTABLI` (§1.1). Aucun id SC nouveau n'est déclaré par cette fiche. |
| `count` de `rewards` (offset 7) | **aucun gating** (`.../TS_CS_DONATE_REWARD.h:13`) | présent, 1 octet, `int8_t`, à toutes les versions. |
| `reward_type` | **aucun gating** (`.../TS_CS_DONATE_REWARD.h:6`) | présent, 1 octet, `int8_t`. |
| `count` de `TS_REWARD_INFO` | **aucun gating** (`.../TS_CS_DONATE_REWARD.h:7`) | présent, 2 octets `uint16`. |
| structure `TS_REWARD_INFO` | `TS_REWARD_INFO_DEF` déclaré sans condition (`.../TS_CS_DONATE_REWARD.h:5-9`) | structure de 3 octets, identique sur toute la plage couverte par rzu ; corroborée par le client (enregistrement de 3 octets, §3). |

La forme 7.3 n'est donc pas ambiguë : la seule bascule de version de ce paquet est la **renumérotation
9.6.3** (`259 → 1259`), hors périmètre. Aucun champ n'apparaît ni ne disparaît entre 7.3 et 9.6.3.

## 5. Traitement attendu

### 5.1 NGemity

- Définition : `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_DONATE_REWARD.h:12-16` —
  `_(count)(int8_t, rewards)` + `_(dynarray)(TS_REWARD_INFO, rewards)`,
  `CREATE_PACKET(TS_CS_DONATE_REWARD, 259)` (`:16`), sans gating de version (NGemity compile sur
  `EPIC = EPIC_4_1_1`, `reference/ngemity/shared/Common/Define.h:25`). Identique à rzu pour 7.3, en-tête
  de 7 octets compris.
- **Aucun traitement serveur** : `grep -rni donate reference/ngemity/Chihiro` → **0 occurrence** ;
  `grep -rn 259 reference/ngemity/Chihiro/src/Network/GameNetwork/WorldSession.cpp` → **0 occurrence**.
  NGemity fournit donc la **structure**, pas la logique : il n'y a pas de `onDonateReward` à porter,
  contrairement à 253 ou 203.

### 5.2 Ce que le client 7.3 attend en retour

- Le client dispose d'un **bloc de résultat dédié au request id 259** dans son gestionnaire `TM_SC_RESULT`
  (handler VA `0x66DB80`) : `movzx eax,WORD PTR [esi+0x7]` @ `0x66DBE2` (le request id), `cmp eax,0x119`
  @ `0x66DBE6`, `sub eax,5` / `cmp eax,0xFE` / `ja` repli @ `0x66DBF7-0x66DBFF`,
  `movzx eax,BYTE PTR [eax+0x66e2d8]` @ `0x66DC05`, `jmp DWORD PTR [eax*4+0x66e290]` @ `0x66DC0C`. Pour
  l'id 259 : index `254` de la table d'octets → **bloc 16**, à l'adresse `0x66DE23` ; le premier libellé
  que ce bloc référence est `모럴 포인트 부족-%s[%d]` (« points moraux insuffisants »). Le repli (index 17,
  VA `0x66E258`) affiche « 뭔가 결과-MSG ID : %d » ; 259 n'y tombe pas.
- Conséquence : **le client attend un accusé** `TM_SC_RESULT` dont `RequestMsgID = 259`. Le libellé du
  bloc n'est pas un code imposé : le bloc affiche `libellé-%s[%d]` en résolvant le code reçu par une
  fonction code → texte (`call 0x425980` @ `0x66DC19`), donc le code est choisi par le serveur (§7.6).
- Forme de la réponse, lue dans le même handler : `mov ax,WORD PTR [esi+0x7]` @ `0x66DBCC` (request id),
  `mov cx,WORD PTR [esi+0x9]` @ `0x66DBD4` (résultat), `mov edx,DWORD PTR [esi+0xb]` @ `0x66DBDC`
  (valeur) → **15 octets**. Le dépôt la produit déjà : `Game/Network/Packets/Game/TS_SC_RESULT.cs:5-17`
  (`Pack = 1`) et `GameClient.SendResult(ushort id, ushort result, int value = 0)`
  (`Game/Network/Clients/GameClient.cs:56-60`).

### 5.3 Enum, dispatch et primitives réutilisables

| À toucher | Où | Précisément |
| --- | --- | --- |
| énumération | `Game/Network/Packets/Enums/GamePackets.cs:41-44` | ajouter `TM_CS_DONATE_REWARD = 259` dans la zone des paquets d'objets (à côté de `TM_CS_USE_ITEM = 253`, `TM_SC_UPDATE_ITEM_COUNT = 255`). Aucun id descendant nouveau. |
| dispatch | `Game/Network/Clients/GameClient.cs` | un bras `if (header.ID == (ushort)GamePackets.TM_CS_DONATE_REWARD) { ... continue; }` **avant** le `switch` final (`:791-802`) : sans lui, l'id ajouté atteint `_ => throw new Exception("Unknown Packet Type")` (critère d'acceptation 4). |
| lecture | `Game/Network/Packets/Game/GameActionPackets.cs` | `HeaderSize = 7` (`:7`) ; ajouter un lecteur sur le modèle de `TryReadUseItem` (`:180-195`) : exiger une longueur ≥ 8, `(Length - 8) % 3 == 0`, `N = (Length - 8) / 3`, `packet[7] == N`, `N ≤ 4`, puis un `record struct` de 4 paires (`reward_type`, `count`). |
| accusé | `Game/Network/Clients/GameClient.cs:56-60` + `Game/Network/Packets/ResultCode.cs` | `SendResult(259, (ushort)ResultCode.<code>)`. Le dépôt emploie le motif pour tous les paquets d'objets (`TM_CS_PUTON_ITEM` `:360`, `TM_CS_ARRANGE_ITEM` `:378`, `TM_CS_ERASE_ITEM` `:396`, `TM_CS_TAKE_ITEM` `:414`, `TM_CS_DROP_ITEM` `:432`, `TM_CS_CHANGE_ITEM_POSITION` `:451`, `TM_CS_PUTOFF_ITEM` `:469`, `TM_CS_USE_ITEM` `:487`) et de façon générique en `:767` / `:781`. |
| ignoré-bruyant | `GameClient.cs:620-628` | précédent utile : `TM_SC_REGION_ACK` reçu côté serveur est journalisé puis ignoré au lieu d'atteindre le repli qui lève. Ici, à l'inverse, une trame non conforme doit **recevoir un accusé de refus** (§5.4), pas seulement un log. |

Primitives existantes **si** une écriture d'objet devenait arbitrée : `ICharacterService.AddItemAsync`
(`Game/Services/ICharacterService.cs:53`, implémentation `Game/Services/CharacterService.cs:334`, déjà
employée par `Game/Services/GroundItemService.cs:179`). Aucune primitive de **points moraux** ni de
**contribution** n'existe ; `Game/Services/InventoryService.cs` ne porte que `SwapPositionsAsync`,
`EraseAsync`, `ArrangeAsync`.

### 5.4 Politique de refus (ce que la fiche peut prouver)

Enveloppe conforme, déduite de §3 : `Length ∈ {8, 11, 14, 17, 20}`, `packet[7] == (Length - 8) / 3`, et
`reward_type ∈ {0,1,2,3}` (le client n'émet que ces positions, §3). Sont **non conformes** pour un client
7.3 : une longueur hors de cet ensemble ; `(Length - 8) % 3 != 0` ; un `count` en désaccord avec la
longueur ; un `reward_type` hors `0..3` ; deux enregistrements de même `reward_type` (le client visite
chaque position une seule fois) ; un enregistrement de `count` nul (le client n'émet que les positions non
nulles). Le checksum n'a pas à être imposé en réception (convention du dépôt).

Traitement retenu faute d'arbitrage métier : **lire, valider, acquitter**. `SendResult(259, code)` avec
`ResultCode.Success` pour une trame conforme (y compris la trame vide à 8 octets) et
`ResultCode.InvalidArgument` pour une trame non conforme, puis journaliser la sélection reçue. Les
questions que la fiche ne tranche pas (élargir `N` au-delà de 4, refuser un `count` nul, plafond de
quantité par ligne) vont en §7.

### 5.5 Relation avec 258 (`TM_CS_DONATE_ITEM`), absent de `master`

- 258 et 259 sont les deux paquets de la même fonction de jeu et **le client les émet depuis la même
  fenêtre** : 258 par `SIMSG_UI_ACT_ITEMCONTRIBUTION` (clé 1130 — fiche 258 §2), 259 par
  `SIMSG_UI_ACT_REQUESTREWARDITEM` (clé 11300, §2 ci-dessus). Le dépôt ne porte encore ni l'un ni l'autre
  (`GamePackets.cs:41-48`, `grep -rni donate Game/` → 0).
- Cette fiche **ne dépend pas** du merge de la MR #26 (258) : id, lecteur et accusé du 259 sont
  indépendants, et rien ici ne suppose un modèle de points moraux ni un compteur de contribution.
- Ce qui restera **vrai sans 258** : tout §3, §4 et §5.3. Ce qui devra être **réévalué après** le merge :
  le placement dans `GamePackets.cs` et dans la chaîne de `if` de `GameClient.cs` (mêmes fichiers, donc
  conflit de merge textuel possible — sans conflit sémantique), et l'effet de jeu final, qui dépendra d'un
  modèle de contribution commun aux deux paquets.

## 6. Écarts assumés avec NGemity

| Point | NGemity | Cette fiche | Pourquoi |
| --- | --- | --- | --- |
| traitement serveur | aucun handler (`grep -rni donate Chihiro` → 0) | lecture + acquittement ; crédit hors périmètre | il n'y a rien à porter : la fiche ne peut pas s'appuyer sur NGemity pour la logique (même situation que 550). |
| énumération | `enum class Packets` unique contenant **les deux** entités (montante et descendante) avec la même valeur 259 (`shared/Server/ClientPackets.h:22`, `:99-100`) | enum C# unique, **seul le montant** est déclaré | un `enum` C# à valeurs dupliquées rendrait tout `switch` sur `GamePackets` ambigu ; la consommation descendante est attestée (§1.1 b) mais pas l'émission serveur (§7.4). |
| version | NGemity compile sur `EPIC = EPIC_4_1_1` (`shared/Common/Define.h:25`), voit donc la forme `< 9.6.3` sans l'écrire | gating rzu explicite, tranché pour 7.3 (§4) | la valeur coïncide pour ce paquet ; l'écart doit rester écrit pour ne pas être « corrigé » plus tard. |
| format du frame | `CREATE_PACKET` (id fixe, en-tête 7) + `count` | identique : `8 + 3N`, `count` à `+7`, enregistrements de 3 octets | **aucun écart** : rzu, NGemity et le client 7.3 concordent, y compris sur l'octet `+7` (le client y écrit bien `N`). |
| sémantique | NGemity ne nomme pas le sens de `reward_type` / `count` au-delà des types | idem, réserves §7.1-§7.2 | pas de source pour trancher dans l'un ou l'autre référentiel. |

## 7. NON ÉTABLI

Chaque point est formulé pour être tranché par Killian sans nouvelle archéologie.

**7.1 — La correspondance `reward_type` ↔ ligne affichée.** Le constructeur écrit la position du champ
non nul parmi les 4, comptée de 0 à 3 (compteur @ `0x48E5D5`, écriture @ `0x48E5EC`) ; l'affichage, lui,
numérote les bancs de 1 à 4 (`selectitem_itemback_01..04`, base `[esi+0x4a0]`, boucle `mov ebx,1`
@ `0x5EFDCD`). L'alignement (même base `esi`, même pas de 8 octets) donne `reward_type = rang du banc - 1`.
**Question** : le serveur doit-il nommer `reward_type ∈ [0..3]` ou `[1..4]` dans sa propre table ? La
fiche retient **la valeur brute reçue (0..3)** et ne la normalise pas.

**7.2 — La sémantique de `count` (le `uint16` de chaque enregistrement).** Le client y place la valeur de
la ligne, affichée par `static_itemcount%02d` et modifiable au double-clic (« redemption quantity »,
`ui_text_6747`). Est-ce une **quantité d'objets**, un **coût en points moraux** (le bloc de libellés
contient `30000`, `10000`, `5000`, `1000` à côté des quatre icônes de récompense) ou un **id de ressource
d'objet** ? Non décidable dans le client seul. La fiche ne lui donne aucun sens : elle le lit, le
journalise et l'acquitte.

**7.3 — Le contrôle qui ouvre la boîte de confirmation.** La branche `0x426` (1062) de la fenêtre
(`cmp eax,0x426` @ `0x5F025F-0x5F0264`) agit sur le contrôle `contribution_ok` ; la boîte de confirmation
est construite par le code des fenêtres alentour (contrôles poussés @ `0x5F002F` pour `button_ok`
(`0xa21b9c`), `0x5F0076` pour `button_cancel` (`0xa21ba8`), `0x5F04DA` pour `box_title` (`0xa3ced4`),
`0x5F0041`/`0x5F0293` pour `contribution_ok`), et l'entrée qui la déclenche (clic sur une ligne, sur
`button_donation_item`, ou autre) n'a pas été identifiée. Sans effet sur la trame : seul l'enchaînement
compte.

**7.4 — L'émission serveur du claim descendant.** Le client **consomme** un 259 entrant comme ouverture de
la fenêtre d'équipement de joyaux (§1.1 b) et rzu nomme ce paquet `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW`.
Rien dans le client ne dit si un serveur 7.3 en émet un jour. **Question** : ce paquet (1259 en 9.6.3+)
doit-il exister dans Navislamia ? Si oui, une décision explicite sur la valeur dupliquée dans
`GamePackets` sera nécessaire ; en l'état, la décision du socle (hors énumération) est maintenue.

**7.5 — Ce que le client fait en l'absence d'accusé.** Le bloc de résultat dédié à 259 existe (§5.2),
donc le client sait traiter une réponse ; l'état de la fenêtre si aucune réponse n'arrive (validation
bloquée, texte d'attente, nouvelle tentative) n'a pas été lu.

**7.6 — Le code de résultat à envoyer sur un refus.** Le client affiche `libellé-%s[%d]` en résolvant le
code reçu par une fonction code → texte, et son bloc 16 porte le libellé « points moraux insuffisants » :
la correspondance exacte entre le code à envoyer et ce libellé n'est pas établie. La fiche retient
`ResultCode.Success` (conforme) et `ResultCode.InvalidArgument` (non conforme) ; pour un refus **métier**
(table de récompense absente, solde insuffisant), **aucun code** n'est fixé : il viendra de l'arbitrage.

**7.7 — Les lignes de récompense côté serveur.** Le client tire ses lignes d'une ressource locale
(4 bancs déclarés, libellés `%02d`) ; le serveur ne reçoit aucune table et le dépôt n'en possède aucune.
Toute traduction de `reward_type` en objet exige une source de données arbitrée.

**7.8 — Les bornes de la politique de refus.** La fiche retient `N ≤ 4` et `reward_type ∈ [0..3]` (bornes
du client 7.3) ainsi que le rejet des enregistrements de `count` nul et des doublons de `reward_type`.
**Questions** : faut-il tolérer `N > 4` (compatibilité d'un client 9.x, où rzu autorise jusqu'à 127) ?
Faut-il honorer un `count` nul plutôt que le refuser ? Existe-t-il un plafond de quantité par ligne
(`ui_text_6747` en annonce un côté client) ?

**7.9 — À porter dans la description de la MR (`## A VERIFIER PAR KILLIAN`).**

- §1.1 / §7.4 : le doublon d'id 259 (montant retenu ; claim descendant laissé `NON ÉTABLI`, hors de
  `GamePackets`) — la réponse à la question ouverte du socle §1.1 ;
- §7.1 : nommage `reward_type` 0..3 contre bancs affichés 1..4 ;
- §7.2 : sens du `uint16` et effet de jeu attendu (table de récompense, coût, seuil) — **hors périmètre
  faute d'arbitrage** ;
- §7.6 : code(s) de résultat à envoyer sur un refus métier ;
- §7.8 : bornes de la politique de refus ;
- §5.5 : relation avec la MR #26 (258) — conflit de merge possible sur `GamePackets.cs` et
  `GameClient.cs`, sans dépendance sémantique.

## 8. Commits épinglés

| Référentiel | Commit | Usage dans cette fiche |
| --- | --- | --- |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | structure et gating (`librzu/src/packets/GameClient/TS_CS_DONATE_REWARD.h`, `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW.h`), en-tête de 7 octets et `getClampedCount` (`librzu/src/lib/Packet/PacketDeclaration.h`), bornes d'épique (`librzu/src/lib/Packet/PacketEpics.h`) |
| NGemity | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | définition homologue (`shared/Server/Packets/GameClient/TS_CS_DONATE_REWARD.h`), énumération à valeur dupliquée (`shared/Server/ClientPackets.h`), `EPIC` de compilation (`shared/Common/Define.h`), absence de handler dans `Chihiro` |
| client de référence | `reference/client73/SFrame.exe` (`sha256 41e0af2e…500e`), `db_string.rdb` (`sha256 4e8e3e06…99e1`) | émission du 259, chaîne du geste, répartiteur des paquets reçus, table des résultats |
| dépôt Navislamia | `ec76b218cd0bd7c6498d725f253abb8b431f0cd6` (`master`) | état de l'énumération, du dispatch, des primitives et des tests au moment de la rédaction |

## 9. Implémentation livrée

Branche `hermes/packet-259-donate-reward`, par-dessus le commit de la fiche (`c1868bd`), base
`master = ec76b218cd0bd7c6498d725f253abb8b431f0cd6`. Périmètre exactement celui de §5.3 : **lire,
valider, journaliser, acquitter**. Aucun effet de jeu, aucune récompense créditée.

| Fichier | Rôle |
| --- | --- |
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_DONATE_REWARD = 259`, posé après `TM_SC_UPDATE_ITEM_COUNT = 255` |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `DonateRewardEntry(sbyte RewardType, ushort Count)` et `TryReadDonateReward` |
| `Game/Network/Clients/GameClient.cs` | bras de dispatch `TM_CS_DONATE_REWARD` + `HandleDonateReward` (synchrone : le paquet n'a ni état ni service) |
| `Tests/Game/DonateRewardPacketsTests.cs` | offsets, enveloppe, refus et dispatch (34 cas) |

Offsets confirmés par les tests, tels que §3 les fixe : total `8 + 3 × N` (8 / 11 / 14 / 17 / 20 aux N
testés) ; `Length` uint32 à 0 ; `ID` uint16 à 4 (= 259) ; checksum à 6 ; compte **signé** (int8) à **7** ;
`reward_type` int8 signé à `8 + 3k` ; `count` uint16 little-endian à `9 + 3k`. La réponse
`TM_SC_RESULT` fait **15 octets** : `RequestMsgID` uint16 à 7 (= 259), `Result` uint16 à 9,
`Value` int32 à 11 (= 0).

### 9.1 Décisions prises, et pourquoi

1. **Le compte est lu d'abord**, à l'offset 7, et borné `0..4` **avant** toute lecture
   d'enregistrement : aucune lecture d'un octet d'enregistrement ne peut précéder la borne.
2. **La longueur doit appartenir à `{8, 11, 14, 17, 20}`**, c'est-à-dire valoir exactement `8 + 3 × N`
   avec `N ≤ 4`. Ni plus courte, ni plus longue. C'est l'enveloppe de §5.4 appliquée à la lettre ;
   §5.3 la formulait pour la lecture (« longueur ≥ 8, `(Length - 8) % 3 == 0`, `N = (Length - 8) / 3`,
   `packet[7] == N`, `N ≤ 4` ») — les deux coïncident sous `N ≤ 4`, et c'est §5.4 qui tranche les
   trames hors de cette famille : pour un client 7.3, une longueur de 9 ou 12 octets est une trame non
   conforme, pas une variante. Le compte étant dérivé de la même `N` côté client (§3), un désaccord
   longueur/compte ne peut pas venir d'un client légitime.
3. **L'enveloppe est jugée avant tout octet d'enregistrement** : un refus ne repose jamais sur une
   lecture hors borne, ni sur une lecture partielle d'un enregistrement. Le checksum n'est pas imposé
   par le lecteur : la boucle de réception l'a déjà vérifié (§5.4, convention du dépôt).
4. **`reward_type` hors `0..3` refusé**, y compris un octet ≥ `0x80` (négatif en int8, comme le type
   rzu `int8_t`).
5. **Deux enregistrements de même `reward_type` refusés** : le client avance d'un cran par ligne, il
   ne peut pas nommer deux fois la même.
6. **Quantité nulle refusée** : le client n'écrit un enregistrement que pour une ligne non nulle (§2),
   donc un zéro est une trame fabriquée. C'est le point le plus discutable de §5.4 — reporté au §9.2.1.
7. **La trame vide (N = 0, 8 octets) est acceptée et acquittée `Success`** (§5.4) : aucune référence
   n'annule l'émission quand les quatre lignes sont à zéro, ce n'est donc pas une anomalie.
8. **La réponse est `SendResult(259, code, 0)`**, sur le refus (`InvalidArgument`, 28) comme sur le
   succès (`Success`, 0). `Value = 0` : aucune référence ne la renseigne (§5.2, §7.2) et c'est un champ
   à taille fixe, donc ce choix ne peut pas désaligner.
9. **Rien n'est interprété.** Le compte et chaque couple `(reward_type, count)` sont journalisés, et
   le contenu ne sert à aucune décision de jeu : ni table de récompense, ni crédit, ni seuil (§7.1,
   §7.2). Les deux champs restent `NON ÉTABLI` : le code ne leur prête aucune sémantique.
10. **Placement choisi pour cohabiter avec la MR voisine de la même famille d'objets.** §5.5 annonce
    un conflit possible avec la MR #26 (258) sur `GamePackets.cs` et `GameClient.cs`. Le membre est
    posé après `TM_SC_UPDATE_ITEM_COUNT = 255` (la 258 pose le sien juste après
    `TM_CS_USE_ITEM = 253`), le bras de dispatch entre `TM_CS_DROP_ITEM` et `TM_CS_ARRANGE_ITEM` (la
    258 le pose après `TM_CS_USE_ITEM`), le handler après `HandleTakeItemAsync` (la 258 le pose après
    `HandleUseItemAsync`), et le lecteur au milieu de `GameActionPackets` (la 258 ajoute à la fin du
    fichier). **Mesure** : `git merge-tree --write-tree` renvoie l'arbre fusionné sans aucune entrée
    de conflit, **exit 0**, contre `origin/master` **et** contre `hermes/packet-258-donate-item`.

### 9.2 Limites et réserves ouvertes

1. **Les règles de refus de §5.4 sont appliquées telles quelles — et §7.8 en laisse deux ouvertes.** Le
   rejet des doublons de `reward_type` et des longueurs hors famille découle de §3 (le client visite
   chaque position une seule fois, et dérive longueur et compte du même `N`). En revanche, §7.8 range
   parmi les **questions non tranchées** : « faut-il honorer un `count` nul plutôt que le refuser ? »,
   la tolérance de `N > 4` (un client 9.x, où rzu autorise 127) et l'existence d'un plafond de quantité
   par ligne. Le code applique aujourd'hui : `count` nul **refusé**, `N > 4` **refusé**, aucun plafond
   par ligne. Le premier est le seul dont le refus puisse faire échouer une trame qu'un client
   émettrait ; les deux autres ne peuvent concerner qu'un client plus récent que 7.3. À arbitrer.
2. **Le doublon d'id 259 reste tranché du côté CS** (§1.1, §7.4) : `TM_SC_SHOW_SOULSTONE_CRAFT_WINDOW`
   (SC, 259 dans rzu) n'entre pas dans `GamePackets`. Si Killian tranche l'inverse, ce travail est à
   reprendre.
3. **Le sens de `reward_type` et du `uint16` reste `NON ÉTABLI`** (§7.1, §7.2) : rien n'est comparé à
   la table de récompense que le client déclare, et rien n'est crédité.
4. **Aucun refus métier** (§7.6) : le seul code envoyé sur refus est `InvalidArgument`, faute
   d'arbitrage sur un code plus précis.
5. **Aucune portée joueur↔joueur, aucun état** : le paquet n'a ni persistance ni effet, donc aucune
   porte unique ni transaction n'était nécessaire (contraste avec le 258, qui retire de la valeur).
6. **La réponse n'est pas prouvée en jeu** : la 259 n'a aucun handler dans rzu ni dans NGemity (§5.1),
   la seule base est le bloc de résultat client §5.2, déduit d'une lecture statique du binaire.
7. **Rien n'est ré-émis vers le client** au-delà de l'accusé : ni mise à jour de fenêtre, ni
   notification — le §5.2 n'en identifie aucune.

### 9.3 Vérification

Relevé sur la branche, après implémentation et tests :

| Commande | Résultat |
| --- | --- |
| `dotnet build Navislamia.sln -c Debug` | code **0**, 0 erreur, 160 avertissements (identique à la baseline) |
| `dotnet test Tests/Tests.csproj` | code **0**, **482 tests** réussis sur 482 (448 à la baseline, **+34**) |
| `git log --oneline origin/master..master` | vide (aucun commit sur `master` locale) |

**La preuve de dispatch mesure bien le dispatch, et pas seulement le lecteur.** Avec le bras de
dispatch neutralisé (comparaison de l'id à celle de `TM_NONE`, donc plus aucune prise en charge), les
six cas `OnDataReceived_*` échouent et les 28 autres passent : un membre de `GamePackets` sans bras
retombe bien sur le `throw new Exception("Unknown Packet Type")` de la fin de
`GameClient.OnDataReceived`, exactement le critère 4 des critères transversaux. Le bras a été restauré
avant l'état commité, et les 34 cas passent ensuite.

Fusion : `git merge-tree --write-tree HEAD origin/master` exit **0** ; idem contre
`hermes/packet-258-donate-item` (aucune entrée de conflit dans les deux cas).

Aucun champ `NON ÉTABLI` de §7 n'a été deviné : chacun est soit laissé de côté (table de récompense,
sens du `uint16`, `NON ÉTABLI` du claimant SC), soit reporté au §9.2.

### 9.4 Bloc destiné à `CLAUDE.md` (à recopier par le QA)

Ajouter, sur le modèle des paquets 203, 253, 550 et 1202, la sous-section suivante dans la section des
paquets du serveur de jeu :

```markdown
### Paquet 259 — `TM_CS_DONATE_REWARD` (récompense choisie dans la fenêtre de don)

- 7.3 = id **259**, client → serveur : rzu remappe en **1259** à partir d'`EPIC_9_6_3`
  (`TS_CS_DONATE_REWARD.h:19-21` ; `EPIC_7_3 = 0x070300` est sous `0x090603`). NGemity compile en
  `EPIC_4_1_1` et déclare `CREATE_PACKET(TS_CS_DONATE_REWARD, 259)`.
- **Le doublon d'id 259 est tranché par le client 7.3** : `TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW` (SC, 259
  dans rzu) est une commande serveur sans handler, jamais émise par le client ; le seul 259 que la
  boucle de réception voit en 7.3 est la remontée de don. Ne pas ajouter le claimant SC à
  `GamePackets`.
- Trame cliente de **`8 + 3 × N` octets** : en-tête 7, compte **signé** (int8) à **7**, puis N
  enregistrements de 3 octets — `reward_type` int8 signé à `8 + 3k`, `count` uint16 little-endian à
  `9 + 3k` (rzu `TS_REWARD_INFO` : `_(simple)(int8_t, reward_type)`, `_(simple)(uint16_t, count)`,
  sans gating de version). N vaut 0..4 : **8, 11, 14, 17 ou 20 octets**.
- Réponse : **`TM_SC_RESULT` (0), 15 octets** — `RequestMsgID` = 259 à 7, `Result` = `Success` (0) ou
  `InvalidArgument` (28) à 9, `Value` = 0 à 11. Le client 7.3 ouvre un bloc de résultat dédié à ce
  request id ; le libellé affiché est un code serveur.
- **Rien n'est interprété** : ni `reward_type` ni le `uint16` n'ont de sémantique établie. Le serveur
  journalise et acquitte, sans table de récompense, sans crédit, sans effet de jeu. **Ne jamais
  inventer un mapping slot → récompense.**
- Enveloppe jugée **avant** toute lecture d'enregistrement : longueur exactement `8 + 3N`, compte
  0..4, `reward_type` distinct ∈ 0..3, quantité non nulle. La trame vide de 8 octets (N = 0) est
  légitime et acquittée `Success`.
- Aucun handler dans NGemity ni dans rzu : les deux références ne font que déclarer la structure,
  rien à porter.
- Restes ouverts (fiche §7 et §9.2) : sens des deux champs, table de récompense, code de refus métier,
  et les deux règles de refus non prouvées (doublon de slot, quantité nulle).
- Le savoir durable d'un paquet va dans sa fiche `docs/packet-specs/<id>-<nom>.md`, pas ici.
```

## A VERIFIER PAR KILLIAN

Synthèse de §7.9, avec les réserves propres au dev (§9.2) :

| # | Point à trancher | Pourquoi c'est ouvert | Où |
| --- | --- | --- | --- |
| 1 | Doublon d'id 259 : CS retenu, claimant SC laissé hors de `GamePackets` | le client 7.3 ne prouve que l'usage CS ; le socle §1.1 attend l'arbitrage | §1.1, §7.4, §9.2.2 |
| 2 | Nommage `reward_type` 0..3 contre bancs affichés 1..4 | décalage entre l'index du fil et l'étiquette du client | §7.1 |
| 3 | Sens du `uint16` et effet de jeu attendu (table de récompense, coût, seuil) | aucune référence ne l'implémente ; **hors périmètre ici** | §7.2, §9.2.3 |
| 4 | Code(s) de résultat à envoyer sur un refus métier | seul `InvalidArgument` est envoyé, faute d'arbitrage | §7.6, §9.2.4 |
| 5 | Bornes de la politique de refus : honorer un `count` nul, tolérer `N > 4`, plafond de quantité par ligne | §7.8 les laisse explicitement ouvertes ; le code refuse aujourd'hui les deux premières | §7.8, §9.2.1 |
| 6 | Le client 7.3 attend-il vraiment la `TM_SC_RESULT` de request id 259 ? | bloc déduit d'une lecture statique du binaire, aucun essai en jeu | §5.2, §9.2.6 |
| 7 | Relation avec la MR #26 (258) | fusion vérifiée propre, sans dépendance sémantique | §5.5, §9.1.10 |
