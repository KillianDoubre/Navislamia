# 324 — `TM_CS_GET_SUMMON_SETUP_INFO`

Fiche du paquet client `324`. Base : `master` `8297cf07db3a3038b9c1142d72f9ce1af4b07ae6`.
Branche : `hermes/packet-324-get-summon-setup-info`.

Méthode : lecture seule de `reference/rzu` (commit `87c1e83b`), de `reference/ngemity` (commit
`38ceb2c6`) et du client 7.3 (`reference/client73`). Aucun binaire, Lua ou script client exécuté :
le client est lu par `strings` (dump de 63 099 lignes) et par
`objdump -d --no-show-raw-insn -M intel SFrame.exe` (image `pei-i386`, `ImageBase`/`.text`
VMA `0x401000` au fichier `0x400`, `.rdata` VMA `0xa0f000` au fichier `0x60da00`). Toutes les
adresses citées sont des **VA** ; les chaînes citées sont données en VA **et** en numéro de ligne du
dump `strings` (`strings SFrame.exe`, longueur minimale 4), dump reproductible et identique au
décompte déjà utilisé par `socle-invocations.md` (63 099 lignes).

## 1. Identité

| | |
|---|---|
| id 7.3 | **324** (`0x144`) |
| nom | `TM_CS_GET_SUMMON_SETUP_INFO` |
| source du nom et de l'id | `op_codes.md:115` — `[324] = "TM_CS_GET_SUMMON_SETUP_INFO"` |
| sens | client → serveur |
| déclaration rzu | `reference/rzu/librzu/src/packets/GameClient/TS_CS_GET_SUMMON_SETUP_INFO.h:14` — `CREATE_PACKET_VER_ID(..., SessionType::GameClient, SessionPacketOrigin::Client)` (origine « Client » : le serveur ne l'émet jamais) |
| déclaration NGemity | `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_GET_SUMMON_SETUP_INFO.h:9` ; membre `TS_CS_GET_SUMMON_SETUP_INFO = 324` en `shared/Server/ClientPackets.h:128` |
| état Navislamia | **absent** de `Game/Network/Packets/Enums/GamePackets.cs` (89 membres, aucun doublon de valeur — vérifié : `grep -oE "= ([0-9]+)," … \| sort -n \| uniq -d` est vide) |

`TM_EQUIP_SUMMON = 303` existe déjà (`Game/Network/Packets/Enums/GamePackets.cs:50`) : c'est le seul
membre de la famille invocation présent dans l'énumération. Ajouter `TM_CS_GET_SUMMON_SETUP_INFO = 324`
ne collisionne avec **aucune** valeur existante, ni avec le lot 10000 (qui ajoute `10000` sur
`hermes/packet-10000-open-item-shop`). La seule zone réellement disputée est la chaîne de dispatch de
`GameClient.Receive` — voir §5.3.3.

Le tableau de noms du client 7.3 (`TM_*`, lu dans le dump `strings`) contient `TM_EQUIP_SUMMON` et
`TM_CS_SUMMON` mais **pas** `TM_CS_GET_SUMMON_SETUP_INFO`. Ce tableau est donc incomplet pour ce
paquet : son absence de la liste de noms n'est **pas** une preuve d'absence. La preuve positive est
dans le binaire lui-même (§2 et §3).

## 2. Ce que le joueur fait pour que le client l'envoie

Le client 7.3 **construit et émet réellement** la 324, de façon entièrement traçable :

1. **Le geste** — le joueur ouvre la fenêtre de formation de créature. Le texte 7.3 du didacticiel
   du dresseur le dit : « If you activate the creature window by pressing **Alt + R**, you can view
   the creatures that are currently in your formation »
   (`db_string.rdb`, dump `strings`, ligne 10546 ; l'info-bulle de la barre rapide porte
   `(Alt+R)` en ligne 79709). Deux textes plus récents du même rdb parlent de `(Y)`
   (ligne 42341) : c'est une variante d'une version ultérieure, pas la 7.3.
2. **Deux boutons de l'interface émettent la commande interne `req_summon_formation`** (chaîne VA
   `0xa20244`) :
   - `button_formation` (VA `0xa2d8bc`, dump ligne 21882) dans la fenêtre principale : test de la
     chaîne en VA `0x570c17`, émission de la commande en VA `0x570c55`;
   - `button_common_quick_creature_edit` (VA `0xa348a8`, dump ligne 23092) de la barre rapide : test
     en VA `0x5b1759`, émission en VA `0x5b1797`.
   La fenêtre correspondante est `window_summon_formation.nui` (dump ligne 24838) et
   `window_main_creature.nui` (24839) ; elle affiche six emplacements de carte
   (`creatureform_cardslot%02d`, 21649).
3. **Le répartiteur d'interface traduit la commande en trame réseau.** La fonction en VA `0x49cd00`
   (répartiteur de messages `SGameInterface`) compare le champ chaîne du message (objet reçu en
   `[ebp+0x8]`, chaîne en `+0x13`) à la liste des commandes d'interface ; sur
   `push 0xa20244` / `call 0x48ef10` (comparaison de chaînes), la branche en **VA `0x49d092`** est
   prise et appelle le constructeur de trame (`0x49d0a7`). Les voisines immédiates de la liste sont
   `close_item_shop` (`0xa20234`) et `drop_quest` (`0xa2025c`), puis `refreshLobby` etc.
4. Un troisième site construit la même chaîne de commande en VA `0x68a9b8`, dans la fonction
   `0x689f70`, à côté de la commande `commercial_shop` (`0x68a692`) : c'est un autre producteur de
   la commande, non rattaché ici à un geste précis (§7, question 5).

Aucun autre appelant direct n'existe : le constructeur de la 324 n'a **qu'un seul** site d'appel dans
tout le binaire (recherche linéaire exhaustive du motif `call 0x48c640` sur le désassemblage complet
et recherche du pointeur de chaîne `0xa20244` : un seul résultat chacun, `0x49d0a7` et `0x49d092`).
La 324 n'est donc pas un paquet périodique ni un keepalive : elle est émise une fois par ouverture de
la fenêtre de formation — sous réserve d'un appel indirect que le désassemblage linéaire ne montrerait
pas (§7, question 7).

### 2.1 Le drapeau `show_dialog` est calculé par le client, pas constant

Immédiatement après la construction de la trame, le client écrit l'octet de charge utile (voir §3)
puis envoie :

```
49d0a4: lea    ecx,[ebp-0x18]        ; la trame est dans [ebp-0x18]
49d0a7: call   0x48c640              ; constructeur 324 (Length 8, id 0x144)
49d0ac: mov    ecx,DWORD PTR [esi+0xb8]   ; l'objet de connexion
49d0b2: push   0x2c                  ; clé 44
49d0b4: call   0x61feb0              ; lecture d'un réglage booléen indexé par 44
49d0bf: test   al,al
49d0c1: sete   al                    ; al = (réglage == 0)
49d0c4: mov    BYTE PTR [ebp-0x11],al ; [ebp-0x18] + 7 = octet 7 de la trame = show_dialog
49d0cf: mov    edx,DWORD PTR [esi]    ; envoi par la vtable de la connexion
49d0d1: mov    edx,DWORD PTR [edx+0xc4]
49d0d7: lea    eax,[ebp-0x18]
49d0da: push   eax
49d0db: mov    ecx,esi
49d0dd: call   edx
```

Le réglage est lu par `0x61feb0` → `mov ecx,[ecx+0x24c]` puis saut en `0x648f30` → `mov ecx,[ecx+0xc]`
puis saut en `0x62f210`, qui est un `find` de table (`ecx+0x28`, appel `0x42f3a0`) et renvoie l'octet
`[entrée+0xc]`. Autrement dit : **`show_dialog = !réglage[44]`**, un booléen.

Conséquence pratique pour le serveur : `show_dialog` vaut **0 ou 1**, jamais autre chose, et il n'est
pas constant d'un joueur à l'autre — le serveur doit le **relire dans la requête** et le renvoyer,
pas le fixer.

## 3. Structure sur le fil

| offset | type | nom | valeur observée | source |
|---|---|---|---|---|
| 0 | `uint32` (4) | `Length` | **8** | client : `mov DWORD PTR [eax],0x8` en VA `0x48c66d` (constructeur VA `0x48c640`) ; convention d'en-tête `CLAUDE.md:47-49` |
| 4 | `uint16` (2) | `ID` | **324** (`0x144`) | client : `mov edx,0x144` / `mov WORD PTR [eax+0x4],dx` en VA `0x48c662`-`0x48c667` ; `op_codes.md:115` |
| 6 | `uint8` (1) | `Checksum` | somme des octets 0 à 5 | client : boucle `0x48c680`-`0x48c687` (« add dl,[esi] » sur `[eax, eax+6)`, écriture en `[ecx]` = `+6`) ; `CLAUDE.md:47-49` |
| 7 | `bool` (1) | `show_dialog` | 0 ou 1 (calculé, §2.1) | rzu `TS_CS_GET_SUMMON_SETUP_INFO.h:8` (`_(simple)(bool, show_dialog)`) ; client : écriture de l'octet 7 en VA `0x49d0c4` |

**Taille totale attendue : 8 octets.** Un seul octet de charge utile après l'en-tête de 7 octets.

Justification de la taille du `bool` (c'est le seul champ dont la largeur était discutable) :
`bool` est fondamental pour le sérialiseur rzu (`StructSerializer.h:27-29`, `is_primitive`), donc sa
taille est `sizeof(bool)` (`PacketDeclaration.h:71-76`) = **1 octet**, écrit tel quel par
`MessageBuffer::write` (`MessageBuffer.h:80-86`, `sizeof(T)`). NGemity a le même calcul
(`shared/Server/Packets/PacketDeclaration.h:78-82`). Le client **tranche définitivement** : le
constructeur réserve 8 octets (`Length = 8`) et le seul octet écrit après l'en-tête est l'octet 7.
Ni 2, ni 4 octets — l'en-tête 7 + 1 est la seule lecture compatible avec `Length = 8`.

Note de mécanique client (utile pour comprendre l'ordre) : le constructeur `0x48c640` zéro-fill les
octets 4 à 7 (`mov DWORD PTR [eax+0x4],edx`, `edx = 0`), écrit l'id en 4-5, calcule et écrit le
checksum en 6, **puis rend la main** ; l'appelant écrit `show_dialog` en 7 **après** le checksum
(`0x49d0c4`). C'est cohérent : le checksum du client ne couvre que les octets 0 à 5, l'écriture
tardive de l'octet 7 ne l'invalide donc pas. Notre `WriteChecksum` doit couvrir les mêmes six octets.

Contrôle croisé indépendant : le même passage de désassemblage donne les constructeurs C→S voisins,
et leurs longueurs encodées dans la trame confirment les fiches déjà livrées —
303 → `0x48cd10`, `Length = 0x20` (32) ; 323 → `0x48c5e0`, `Length = 0x1a` (26) ; 550 → `0x684b60`
(lu en `0x684b8c`), `Length = 0xf` (15) ; 203 → `Length = 0xf` (15) ; 223 → `Length = 7`
(en-tête seule) ; 253 → 47 ; 284/285 → 15 ; 214 → 12 ; 215 → 8 ; 221 → 11. Aucune de ces valeurs ne
contredit une fiche existante, ce qui valide la méthode de lecture employée ici.

## 4. Gating de version — tranché pour Epic 7.3

```
TS_CS_GET_SUMMON_SETUP_INFO_ID(X)      # rzu, TS_CS_GET_SUMMON_SETUP_INFO.h:10-12
    X(324,  version <  EPIC_9_6_3)
    X(1324, version >= EPIC_9_6_3)
```

- `EPIC_7_3 = 0x070300` (`librzu/src/lib/Packet/PacketEpics.h:59`) est **strictement sous**
  `EPIC_9_6_3 = 0x090603` (`PacketEpics.h:96`, commentaire « GS packet ID modified with version
  20200713 »).
- **Décision : id `324` en Epic 7.3.** Le remappage `1324` de la branche `>= EPIC_9_6_3` ne concerne
  pas ce serveur ; aucune constante de version n'est à introduire dans Navislamia (il n'en a aucune).
- **Aucun champ n'est gated** : rzu ne conditionne pas `show_dialog` par version. C'est le champ
  entier du paquet, il n'y a donc rien d'autre à trancher.
- **Borne basse** : `TS_CS_GET_SUMMON_SETUP_INFO.h` ne porte **aucun** commentaire `// Since EPIC_x`,
  contrairement à ses voisins de la même génération (`TS_CS_SUMMON_CARD_SKILL_LIST.h:10`,
  `TS_CS_FOSTER_CREATURE.h:28`, `TS_CS_REQUEST_FARM_INFO.h:9`… qui disent tous `// Since EPIC_7_3`).
  rzu ne déclare donc aucune borne basse : la branche `version < EPIC_9_6_3` couvre 7.3 par
  construction, et `EPIC_7_3` y tombe.
- Contrôle de cohérence : le frère direct `TS_CS_SUMMON_CARD_SKILL_LIST` (même famille, « Since
  EPIC_7_3 ») utilise exactement la même forme `X(452, version < EPIC_9_6_3)` et l'id 452 est bien
  celui de `op_codes.md:134` pour la 7.3. Le motif de génération est donc fiable ici.
- Corroborations indépendantes de l'existence de 324 avant 9.6.3 :
  - rzu liste 324 dans les paquets **testés sur client réel** pour `EPIC_9_6_2`
    (`PacketEpics.h:83` : « … 301, 303, 305, **324**, 351 … »), sous le commentaire
    « Defined from FR version on 2020-03-07 » ;
  - NGemity fixe 324 sans variante de version (`ClientPackets.h:128`) ;
  - **le client 7.3 lui-même** code l'id en dur (`0x144` en VA `0x48c662`) — c'est la preuve la plus
    directe, et elle est antérieure à toute hypothèse de remappage.

## 5. Traitement attendu

### 5.1 Ce que NGemity (Chihiro) en fait

```cpp
void WorldSession::onGetSummonSetupInfo(const TS_CS_GET_SUMMON_SETUP_INFO *pRecvPct)
{
    Messages::SendCreatureEquipMessage(m_pPlayer, pRecvPct->show_dialog);
}
```
`Chihiro/src/Network/GameNetwork/WorldSession.cpp:692-695`, enregistré pour `STATUS_AUTHED`
(`WorldSession.cpp:109`). **Aucun** écriture en base, aucun état modifié : le paquet est une simple
demande de renvoi.

`Messages::SendCreatureEquipMessage` (`Chihiro/src/Network/Messages.cpp:122-136`) construit alors
`TS_EQUIP_SUMMON` avec `open_dialog = show_dialog ? 1 : 0` et les six handles de
`Player::m_aBindSummonCard` (0 pour un emplacement vide), puis envoie. À noter : la fonction
vérifie `pPlayer == nullptr`, mais `SendCreatureEquipMessage` est appelé avec `m_pPlayer` sans test
préalable — le test vient de la fonction appelée.

La référence n'a donc qu'**une seule** réponse possible à la 324 : le paquet **303 `TM_EQUIP_SUMMON`**,
32 octets, dont l'octet 7 (`open_dialog`, `bool`) recopie le `show_dialog` reçu et dont les six
handles (offsets 8, 12, 16, 20, 24, 28) viennent de la formation enregistrée.

Vérification côté client (l'émetteur C→S du 303 est distinct de la réponse S→C, mais la disposition
est la même et le client la construit lui aussi) : constructeur VA `0x48cd10` → `Length = 0x20`,
`mov ecx,0x12f` (303) en `0x48cd44`, zéro-fill des octets 4 à 31, puis le remplisseur VA `0x48d3b0`
copie les six handles depuis l'argument (`[eax+0x13]`, `+0x17`, `+0x1b`, `+0x1f`, `+0x23`, `+0x27`)
vers `[ebp-0x18]`, `[ebp-0x14]`, `[ebp-0x10]`, `[ebp-0xc]`, `[ebp-0x8]`, `[ebp-0x4]`, c'est-à-dire
les offsets 8, 12, 16, 20, 24, 28 de la trame — six `uint32` consécutifs, exactement la disposition
déclarée par rzu (`TS_EQUIP_SUMMON.h:8-9`).

### 5.2 Ce que rzu en fait

Rien d'autre que la déclaration : pas de serveur de jeu complet, le paquet sert de référence de
sérialisation (`CREATE_PACKET_VER_ID`, origine `Client`). Sa valeur ici est le gating d'id et la
largeur du champ.

### 5.3 Ce que le serveur Navislamia doit faire

**5.3.1 Périmètre** — un membre d'énumération `TM_CS_GET_SUMMON_SETUP_INFO = 324` **et** son bras de
dispatch dans `GameClient.Receive`, dans le même commit (`CLAUDE.md:1271-1274`, critère transversal
n° 4 : un id déclaré sans bras atteint `_ => throw new Exception("Unknown Packet Type")`). Aucune
écriture en base, aucun état modifié : la 324 ne fait que demander un renvoi.

**5.3.2 Lecture et réponse** — la trame fait exactement 8 octets ; `msgBuffer` inclut les 7 octets
d'en-tête (`GameClient.cs:580`, l'en-tête est relu par `new Header(...)` en `561` puis retiré selon
l'usage des handlers existants). Le handler doit donc :

1. refuser (journal + abandon, sans réponse) toute trame dont `header.Length != 8` ;
2. refuser toute trame reçue avant l'entrée en jeu (`ConnectionInfo.CharacterHandle == 0`), comme le
   fait déjà `HandleGetRegionInfo` (`GameClient.cs:184-189`) ;
3. lire `show_dialog = msgBuffer[7] != 0` ;
4. répondre **303**, 32 octets, `open_dialog = show_dialog`, puis les six handles.

**La réponse exige un `BuildEquipSummon` qui prenne le drapeau.** Aujourd'hui
`GameCharacterPackets.BuildEquipSummon(long[] summonSlots)` (`Game/Network/Packets/Game/GameCharacterPackets.cs:214-227`)
ne prend **pas** de paramètre de dialogue et laisse l'octet 7 à zéro (`CreatePacket` zéro-remplit et
la boucle n'écrit que les handles à partir de `HeaderSize + 1`). Le test existant
`BuildBaseStatePackets_UseTheExpectedEpic73Sizes` fige déjà la taille à 32
(`Tests/Game/GameCharacterPacketsTests.cs:147,153,159`). Le dev doit étendre ce constructeur avec un
paramètre `openDialog` (défaut `false` pour l'appel d'entrée en jeu, `GameActions.cs:190`) plutôt que
d'en créer un second : c'est un seul paquet, un seul layout, deux appelants.

**5.3.3 Contrainte de collision mesurée** — la chaîne de `if` de `GameClient.Receive` est le vrai
point chaud. Relevé fait localement sur les branches ouvertes (`git diff master...<branche>` de
chaque `hermes/packet-*`, position d'insertion = bras existant qui suit immédiatement le bloc
inséré) :

| ancre d'insertion | branches ouvertes qui l'utilisent |
|---|---|
| avant le bras `TM_CS_CHANGE_LOCATION` | 9 |
| avant le bras `TM_CS_TARGETING` | 5 |
| avant `IPacket msg = header.ID switch` | 4 |
| avant le bras `TM_CS_CHAT_REQUEST` | 3 |
| avant `TM_CS_ARRANGE_ITEM` / `TM_TIMESYNC` / `TM_CS_GAME_TIME` / `TM_CS_ATTACK_REQUEST` | 2 chacune |
| avant `TM_CS_PUTOFF_ITEM` (214) / `TM_CS_PUTON_ITEM` (408) / le `is`-bloc `TM_CS_UPDATE` (socle-booths) / `TM_CS_USE_ITEM` (socle-entrepot-personnage) | 1 chacune |

Recommandation : ancrer le bras 324 **immédiatement avant le bras `TM_CS_USE_ITEM`**
(`Game/Network/Clients/GameClient.cs`, bloc `if (header.ID == (ushort)GamePackets.TM_CS_USE_ITEM)`),
la seule ancre de fin de chaîne occupée par une unique branche ouverte ; ne pas l'ajouter avant
`TM_CS_CHANGE_LOCATION` (9 branches). Le dev doit de toute façon rebaser sur `master` avant que le QA
ne publie, et poser le bras **avant** le `switch` final quel que soit l'emplacement choisi.

**5.3.4 Contenu des six handles** — la source est `CharacterEntity.SummonSlotItemIds`
(`Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:63`, `long[]`, longueur maximale 6 imposée
par `Game/DataAccess/Contexts/TelecasterContext.cs:116`), lue aujourd'hui une seule fois, à l'entrée
en jeu (`Game/Network/Clients/Actions/GameActions.cs:190`). Rien ne la remplit : aucune écriture,
aucun test. La réponse à la 324 sera donc **six zéros** tant que la formation n'est pas persistée,
exactement comme la 303 d'entrée en jeu aujourd'hui. **Ne pas inventer** de handles (pas de
dérivation depuis l'inventaire, pas de `0xFFFFFFFF`) : renvoyer ce que la colonne contient, c'est
tout.

**5.3.5 `show_dialog` — ne pas le fixer.** Le client l'a calculé (`!réglage[44]`) et attend que le
serveur le lui rende dans `open_dialog`. Renvoyer systématiquement 1 (ou 0) ferait diverger le
serveur de la question posée.

### 5.4 Le piège du 303 entrant (à ne pas manquer)

Le **même** client émet aussi la **303 en client → serveur** : constructeur VA `0x48cd10`
(`Length = 0x20`, id `0x12f`), remplisseur VA `0x48d3b0`, appelé depuis le répartiteur d'interface en
VA `0x49e311`. L'octet 7 (`open_dialog` côté trame client) n'est **jamais** écrit par ce chemin : le
client pousse sa formation avec `open_dialog = 0`. C'est le chemin « le joueur valide la formation »
(glisser une carte scellée dans `creatureform_cardslot`).

Or `TM_EQUIP_SUMMON = 303` **est** déjà défini dans `GamePackets` (`GamePackets.cs:50`) : une trame
entrante 303 passe donc la garde `Enum.IsDefined` (`GameClient.cs:584-588`) et atteint le `switch`
final, qui ne connaît que les paquets de compte (`GameClient.cs:791-803`) → `_ => throw new
Exception("Unknown Packet Type")`. Aucun `try`/`catch` n'entoure le corps de `OnDataReceived`
(`GameClient.cs:555-809`), donc l'exception remonte au gestionnaire de lecture : **le premier joueur
qui valide sa formation de créature fait tomber sa propre boucle de réception**.

Une trame 324 entrante, elle, n'a pas ce problème aujourd'hui : l'id n'étant pas déclaré, elle est
silencieusement ignorée au `GameClient.cs:584-588`. C'est l'ajout du membre d'énumération (étape 1 du
dev) qui la rend dangereuse si le bras de dispatch ne suit pas dans le même commit.

Décision à prendre par le PO, pas par cette fiche : la 324 seule est un paquet cohérent, mais elle
laisse le client dans un état où sa validation de formation tue la connexion. Les options
documentables sont (a) traiter 303 C→S dans le même lot, (b) créer une carte distincte pour 303 C→S,
(c) poser dans ce lot un bras 303 minimal « journal + abandon » pour fermer le piège, la logique
métier venant ensuite. La référence NGemity traite le 303 C→S dans `onEquipSummon`
(`WorldSession.cpp:936-1019`) : validation des cartes contre `SKILL_CREATURE_CONTROL` (1801), création
des `Summon` manquants, compactage, puis réponse 303. Ce n'est pas le périmètre de la 324.

## 6. Écarts assumés avec NGemity

| écart | pourquoi |
|---|---|
| NGemity déclare la 324 en `CREATE_PACKET(..., 324)` sans aucune variante de version ; nous documentons le gating `324/1324` de rzu sans l'implémenter | Navislamia ne compile que pour Epic 7.3 et n'a aucune constante de version ; le gating est une décision écrite ici, pas du code mort (§4) |
| NGemity déclare `bool show_dialog` dans la 324 mais `uint8_t open_dialog` dans sa 303 (`shared/Server/Packets/GameClient/TS_EQUIP_SUMMON.h:7`) | incohérence de forme sans effet sur le fil (1 octet dans les deux cas) ; côté Navislamia le constructeur prendra un `bool` et écrira un octet, le test fixera l'octet 7 |
| NGemity accepte la 324 sans tester `m_pPlayer` avant l'appel, la garde étant dans `SendCreatureEquipMessage` | nos handlers testent l'état du personnage **avant** de construire une réponse (`GameClient.cs:184-189`) ; on suit la convention du dépôt |
| NGemity répond avec `Player::m_aBindSummonCard`, une structure en mémoire alimentée par d'autres paquets de la famille invocation | Navislamia n'a pas cette structure vivante ; la seule source disponible est `CharacterEntity.SummonSlotItemIds`, vide aujourd'hui — la fiche l'assume et ne devine rien (§5.3.4) |
| NGemity ne distingue pas l'appel d'entrée en jeu (`open_dialog = false` en dur, `Player.cpp:778`) de la réponse à la 324 (valeur recopiée) | même distinction chez nous, mais elle doit être **explicite** dans la signature du constructeur (§5.3.2) |
| NGemity gère aussi le 303 C→S complet (`onEquipSummon`) | hors périmètre de la 324 ; le piège d'énumération qu'il révèle est documenté en §5.4 |

## 7. NON ÉTABLI

Aucune de ces questions ne doit être devinée ; elles sont posées pour être tranchées par lecture
complémentaire ou par Killian.

1. **Nature du réglage 44.** Le client calcule `show_dialog` en lisant un booléen indexé par la clé
   `44` (0x2c) dans une table associée à la connexion (`0x61feb0` → `0x648f30` → `0x62f210`, `find`
   en `0x42f3a0`) puis en le **niant**. Quelle option d'interface (ou quel champ du `client_info`) est
   la clé 44 n'est pas établi ; ce qu'on sait, c'est que la valeur transmise est 0 ou 1 et qu'elle
   n'est pas constante (§2.1).
2. **Ce que le client fait de `open_dialog = 1`.** La référence admet que le drapeau ouvre une
   boîte de dialogue ; le client 7.3 ne le montre pas statiquement. À vérifier au premier essai en
   jeu : fenêtre ouverte + dialogue vs simple rafraîchissement.
3. **Représentation des six handles.** `CharacterEntity.SummonSlotItemIds` est commentée
   « verify if item id or item resource id » (`CharacterEntity.cs:63`) — la question est ouverte
   **avant** cette fiche : le client attend des handles d'objet comparables à ceux de
   `TS_SC_INVENTORY`, pas des ids de ressource. Tant que la colonne reste vide, aucune des deux
   lectures n'est vérifiable en jeu.
4. **Le client exige-t-il d'autres paquets après la 303.** NGemity enchaîne, au login seulement,
   `SendAddSummonMessage` pour chaque invocation (`Player.cpp:772-775`) et la liste de compétences du
   familier. Rien n'établit que l'ouverture de la fenêtre par le bouton de formation en dépende.
5. **Geste exact du troisième producteur** de la commande `req_summon_formation` (fonction
   `0x689f70`, site `0x68a9b8`). Les deux autres sont identifiés (bouton de fenêtre principale,
   bouton de barre rapide) ; celui-ci n'est pas rattaché à un élément d'interface.
6. **Ordre d'arrivée** entre la 303 d'entrée en jeu (poussée sans demande, `GameActions.cs:190`) et
   une éventuelle 324 immédiate du client (si l'interface se rouvre sur un état mémorisé). Non
   observable statiquement, sans conséquence sur le handler, mais à garder en tête si le joueur voit
   une formation vide puis remplie.
7. **Y a-t-il un quatrième `show_dialog` possible ?** Le client n'écrit que `sete al` (0 ou 1). Un
   client modifié pourrait envoyer autre chose ; le serveur normalise en `!= 0`, ce qui est un choix
   documenté ici, pas une observation.
8. **Limite de la lecture linéaire.** Le désassemblage est linéaire (`objdump -d`) : un appel
   **indirect** au constructeur de la 324 (`call eax` / entrée de table) ne serait pas vu par la
   recherche de motif. Le résultat « un seul appelant » vaut pour les appels directs ; la conclusion
   « une fois par ouverture de fenêtre » est celle qui est écrite, mais elle repose sur cette limite.

## 8. Commits et binaires épinglés

| Référence | Commit / empreinte |
|---|---|
| Navislamia (`master`, base de la branche) | `8297cf07db3a3038b9c1142d72f9ce1af4b07ae6` (2026-09-22) |
| rzu (`reference/rzu`, HEAD) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02) |
| rzu — création de `TS_CS_GET_SUMMON_SETUP_INFO.h` | `d7c58ee6927b0a8391a22500c7edfc39153d36d3` (2017-02-07, « add all known GS packets as of 9.4 ») |
| rzu — gating 9.6.3 (`324` → `1324`) et mise à jour des ids | `11f2b6fd69913c192e6103afd3953a358ef6226a` (2020-08-11) |
| rzu — commentaire « Last tested: EPIC_9_8_1 » | `3b31db1e5aea84565926f6910e2437646c8a3c51` (2023-09-30) |
| rzu — `PacketEpics.h` (`EPIC_7_3`, `EPIC_9_6_2` et sa liste de paquets testés, `EPIC_9_6_3`) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (HEAD) |
| NGemity (`reference/ngemity`, HEAD) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` |
| NGemity — structure 324 (`TS_CS_GET_SUMMON_SETUP_INFO.h`, `ClientPackets.h:128`) | `e1136b8` (« Don't format packet definitions tho », dernière retouche du fichier dans la référence) |
| Client — `SFrame.exe` | sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 octets, 63 099 lignes au dump `strings`) — **pas de SHA git, `client73` n'est pas un dépôt** |
| Client — `data.000` | sha256 `b88ac39a0976f49746671d413ba68af616af37e6e26095c3a109d5884510adbf` |
| Client — `db_string.rdb`, `db_localcommand.rdb` | fichier de ressources lu par `strings` uniquement (textes du didacticiel et commandes locales) |

## 9. Forme du test d'offsets attendu

Le dépôt exige au moins un test d'offsets par nouveau paquet (`CLAUDE.md:1269`), taille **et**
position de chaque champ. Pour ce paquet, la forme attendue :

- **réponse** (le seul paquet que le serveur construit) : dans
  `Tests/Game/GameCharacterPacketsTests.cs`, à côté de
  `BuildBaseStatePackets_UseTheExpectedEpic73Sizes:143-163` qui fige déjà 32 octets —
  `packet.Should().HaveCount(7 + 1 + 6 * 4)` ; id lu sur 2 octets à l'offset 4 = `(ushort)GamePackets.TM_EQUIP_SUMMON` ;
  longueur lue sur 4 octets à l'offset 0 = 32 ; octet 7 = 1 quand `openDialog` vaut vrai et 0 sinon ;
  les six handles lus en little-endian aux offsets **8, 12, 16, 20, 24, 28** (un handle non nul à
  un seul indice pour prouver l'ordre) ; checksum = somme des octets 0 à 5 ;
- **requête** : un test du lecteur, sur une trame de 8 octets construite à la main
  (`Length = 8`, id 324 à l'offset 4, checksum recalculé), qui vérifie que `show_dialog` vaut faux
  pour l'octet 7 = 0 et vrai pour 1, et qu'une trame de 7 ou 9 octets est **refusée** (journal, pas
  de réponse) — c'est la même discipline que les gardes de taille des fiches 550 et 203 ;
- **le gating 7.3** : le test doit porter la valeur `324` en dur (ou `(ushort)GamePackets.TM_CS_GET_SUMMON_SETUP_INFO`)
  avec un commentaire renvoyant à §4 : aucune variante 1324 ne doit apparaître dans le code.

## 10. Bloc prêt à coller dans `CLAUDE.md`

`CLAUDE.md` est protégé côté worker : le dev ne l'écrit pas, ce bloc voyage dans la description de la
MR et dans cette fiche.

```markdown
### Paquet 324 — `TM_CS_GET_SUMMON_SETUP_INFO` / réponse `TM_EQUIP_SUMMON` (303)

- 7.3 = id **324**, **8 octets** : 7 d'en-tête + `show_dialog` (1 octet à l'offset 7). rzu remappe en
  1324 à partir d'`EPIC_9_6_3` (`TS_CS_GET_SUMMON_SETUP_INFO.h:10-12`) ; `EPIC_7_3 = 0x070300` est
  sous `0x090603`. Le client 7.3 écrit l'id en dur (« 0x144 » en VA `0x48c662`, `Length = 8` en
  `0x48c66d`), ce qui rend la question de version sans objet.
- Le client émet la 324 sur la commande d'interface `req_summon_formation`, produite par le bouton
  `button_formation` de la fenêtre principale (VA `0x570c55`) et par `button_common_quick_creature_edit`
  de la barre rapide (VA `0x5b1797`) — donc à l'ouverture de la fenêtre de formation (Alt + R). Un
  seul site d'appel du constructeur dans tout le binaire.
- `show_dialog` n'est pas constant : le client l'écrit à l'offset 7 juste après la construction, avec
  `sete` sur la négation d'un booléen indexé par la clé 44 (VA `0x49d0ac`-`0x49d0c4`). Le serveur doit
  le relire dans la requête et le rendre dans `open_dialog`, jamais le fixer.
- **Réponse : 303 seul**, 32 octets, `open_dialog` = le `show_dialog` reçu, six handles aux offsets
  8, 12, 16, 20, 24, 28 (source : NGemity `WorldSession.cpp:692-695`, `Messages.cpp:122-135`). Aucune
  écriture en base : la 324 ne fait que demander un renvoi. `BuildEquipSummon` doit accepter le
  drapeau au lieu d'en créer un second constructeur — l'appel d'entrée en jeu passe `false`.
- **Piège : 303 est déjà un id défini** (`GamePackets.cs:50`) mais n'a aucun bras de dispatch. Le même
  client émet 303 **en client → serveur** (constructeur VA `0x48cd10`, `Length = 0x20`, remplisseur
  `0x48d3b0`, `open_dialog = 0`) quand le joueur valide sa formation : une telle trame passe
  `Enum.IsDefined` (`GameClient.cs:584`) et atteint `_ => throw new Exception("Unknown Packet Type")`
  sans `try`/`catch` dans `OnDataReceived` — la connexion du joueur tombe. Déclarer 324 sans bras 303
  ne ferme pas ce piège.
- Contenu des six handles : `CharacterEntity.SummonSlotItemIds`, **vide** en base aujourd'hui ; la
  réponse est six zéros tant que la formation n'est pas persistée. Ne rien dériver de l'inventaire.
```

## 11. A VERIFIER PAR KILLIAN

> **Réserve de méthode — carte Trello non lue.** Cette fiche a été produite par un worker `navis-ref`
> qui n'a **aucun accès Trello** (aucun outil `trello` dans le profil, aucun jeton d'API dans la
> configuration locale lisible) : la description de la carte `zKEmres3` et son commentaire du
> 2026-09-22 **n'ont pas été lus**. Le brief les donnait comme source du constat initial du PO. Rien
> ici ne les contredit — les deux sources de faisabilité qu'ils citent pour 324
> (`WorldSession.cpp:692-695`, `Messages.cpp:122-135`) ont été vérifiées et confirmées byte à byte —
> mais si ce commentaire nomme un fait que cette fiche ignore, c'est ce fait qui manque. À confronter
> par le dev ou la QA, qui ont l'accès.

1. **Périmètre de la carte** : la 303 client → serveur existe dans le client 7.3 et tue la boucle de
   réception (§5.4). Faut-il l'inclure dans ce lot, créer une carte distincte, ou se contenter d'un
   bras « journal + abandon » ? La fiche ne tranche pas : c'est un choix de périmètre.
2. **Réglage 44** du client (question 1 de §7) : s'il s'agit d'une option connue du serveur (par
   exemple un réglage d'interface du `client_info`), le préciser serait utile mais n'est pas
   nécessaire au fonctionnement.
3. **Représentation des handles de formation** (question 3 de §7) : tant que
   `CharacterEntity.SummonSlotItemIds` n'est alimentée par personne, la réponse sera vide en jeu et
   aucune vérification client n'est possible. Décider si le remplissage de cette colonne relève d'une
   autre carte.
4. **La réponse du test d'offsets** : la taille est certaine (8 requête / 32 réponse) et le format
   des asserts est déjà imposé par le dépôt ; si le dev veut vérifier `open_dialog = 1` de bout en
   bout, il faut un test au niveau du handler (pas seulement du constructeur), ce qui n'existe pas
   encore dans `Tests/` pour ce type de paquet.
