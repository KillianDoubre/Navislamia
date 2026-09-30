# 604 — `TM_CS_QUEST_INFO`

Fiche d'archéologie de protocole, Epic 7.3. Écrite en **lecture seule** sur les références
(`reference/rzu`, `reference/ngemity/Chihiro`, `reference/client73`) : aucun Lua, aucun script du
client, aucun exécutable client n'a été lancé, et **aucun code serveur n'a été modifié** (donc aucun
build ni test n'a été exécuté pour cette fiche). Le client 7.3 tranche la forme (il construit lui-même
la trame, §2 et §3) ; rzu tranche la taille, l'ordre des champs et le gating (§4) ; NGemity tranche la
logique serveur — sous réserve qu'elle n'en a **aucune** pour ce paquet (§5).

Cette fiche fait partie du **socle « cycle de quête »** : elle ne se substitue pas à
`docs/packet-specs/socle-cycle-quete.md` (qui traite 604 *et* 605 *et* le catalogue *et* le
déclencheur) ni à `docs/packet-specs/socle-quetes.md` (600 à 605, état du personnage).
**Lire ces deux fiches d'abord** : cette fiche-ci isole 604 comme paquet autonome et se contente de
renvoyer aux socles pour tout ce qui n'est pas la trame elle-même.

Résumé des arbitrages demandés :

| Question | Verdict de cette fiche |
|---|---|
| Id et taille en 7.3 | **604**, **11 octets** (7 + `int32`). Le constructeur client écrit `Length = 0xb` et n'initialise que ces 11 octets (§3) ; rzu déclare `int32_t code` et note « Since EPIC_6_3 » (§4) |
| 7.3 ou 1604 ? | **604** : rzu remappe en `1604` à partir d'`EPIC_9_6_3` (`0x090603`), la cible étant `0x070300` (§4) |
| Que faut-il répondre ? | **Rien, dans le lot de réception.** Le client 7.3 **n'a aucun handler** pour la seule réponse candidate (602), et 604 ne modifie rien localement : c'est une interrogation sans réponse établie. Si un rafraîchissement s'avère nécessaire en jeu, la seule trame consommable est **601** — **jamais 602** (§5.3) |
| Le geste | Sélectionner une quête dans `SUIQuestListWnd` puis cliquer le bouton **`quest_info_button`** (§2) |
| NGemity porte-t-il la logique ? | **Non** : l'id 604 y est **déclaré et jamais lu** — aucun handler dans `WorldSession`, aucune occurrence hors des en-têtes de paquets (§5.1). La référence la plus proche est un **dialogue 3000** émis par un script Lua, ce qui n'est pas la réponse à 604 (§5.3) |
| **604 est-il livrable seul ?** | **Oui, et sans aucun prérequis partagé avec 605.** Le lot (b1) du socle les groupe pour ne toucher qu'une fois les trois mêmes fichiers (`GamePackets.cs`, `GameClient.cs`, `QuestPacketsTests.cs`), pas parce que 604 dépendrait de 605 : 604 exige **strictement moins** que 605 (§5.4) |
| **Faut-il valider `code` contre l'état du personnage ?** | **Non.** 604 ne change aucun état et n'appelle aucune réponse : la seule règle est la taille (11) et la signature du champ. Ajouter une lecture de `CharacterQuests` serait une requête dont personne ne consomme le résultat (§5.5) |

**Reprises du socle, citées et non refaites** (`docs/packet-specs/socle-cycle-quete.md`, sur
`master`) : §3.1 pour la trame de 11 octets et l'ordre des champs ; §5.3 pour la décision
« aucune trame descendante en réponse » et le refus de 602 ; §5.5 lot **(b1)** pour le périmètre
attendu (gameplay : non) ; §6 pour les écarts avec NGemity ; §7.2 pour la réserve « ce que le serveur
7.3 d'origine répondait réellement à 604 ». Les mesures **nouvelles** de cette fiche (constructeur
client `0x0048d1b0` relu instruction par instruction, absence de handler NGemity, state du dépôt) sont
sourcées au fil du texte.

**État mesuré au réveil de la carte** (relevé du PO le 2026-09-30 à 06:00 CEST, repris tel quel) :
`master = 3021f681e98544112ce508b11ce1df305b3a0dd9`, arbre propre, `git log --oneline
origin/master..master` vide ; `dotnet build Navislamia.sln -c Debug` → exit 0 (190 avertissements,
0 erreur) ; `dotnet test Tests/Tests.csproj` → exit 0, **1494 réussis, 0 échec** (NUnit). Le dépôt
constate lui-même l'état livré : `CLAUDE.md:1324` — « 604/605 ne sont toujours ni déclarés ni lus
(lot (b1) de la fiche, non livré) ». **Cette fiche n'a lancé ni build ni test** : elle ne modifie
aucun fichier de code, et aucun chiffre ci-dessus ne vient de cette session.

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id client → serveur | **604** (`0x25c`) | `op_codes.md:159` ; `reference/rzu/librzu/src/packets/GameClient/TS_CS_QUEST_INFO.h:12` |
| Nom | `TM_CS_QUEST_INFO` | `op_codes.md:159` |
| Id alternatif | `1604` à partir d'`EPIC_9_6_3` — **hors cible** | `TS_CS_QUEST_INFO.h:12-13` |
| Gating d'apparition | « Since EPIC_6_3 » | `TS_CS_QUEST_INFO.h:10` |
| Déclaration NGemity | `CREATE_PACKET(TS_CS_QUEST_INFO, 604)`, « Since EPIC_6_3 » | `reference/ngemity/shared/Server/Packets/GameClient/TS_CS_QUEST_INFO.h:9-10` |
| Enum NGemity | `TS_CS_QUEST_INFO = 604` | `reference/ngemity/shared/Server/ClientPackets.h:164` |
| Constructeur client 7.3 | `0x0048d1b0` (id écrit en `0x0048d1d9`, longueur en `0x0048d1e4`) | lecture statique de `reference/client73/SFrame.exe` |
| Site d'envoi client | `0x0049d468`-`0x0049d494` (cas 72 de la table de messages, id de message interne **165**, `0xa5`) | `socle-cycle-quete.md` §2.0-2.1 |
| Taille | **11 octets** | §3 |
| Nom dans le binaire client | **absent** : `TM_CS_QUEST_INFO` n'est pas dans la table de noms du client (`strings` : 0 occurrence) ; l'identification passe par le constructeur et l'envoi, pas par un libellé | `socle-quetes.md` §5.4.1 |
| État dans Navislamia | **absent** : aucun membre `TM_CS_QUEST_INFO` (bande 600-605 réduite à `600`, `601`, `603`) et aucun bras de dispatch | `Game/Network/Packets/Enums/GamePackets.cs:145-147` ; `Game/Network/Clients/GameClient.cs` |

Le binaire client n'étant pas un dépôt git, il n'existe aucun SHA de client : toutes les citations
`SFrame.exe` renvoient au fichier local, empreinte
`sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`, en lecture statique
(`objdump -d -M intel`).

## 2. Ce que le joueur fait pour que le client l'envoie

**Il sélectionne une quête dans la fenêtre de liste des quêtes, puis clique le bouton
`quest_info_button`.** Le paquet part immédiatement, avec le `code` de la quête sélectionnée.

Chaîne d'émission, établie par lecture (détail complet : `socle-cycle-quete.md` §2.0-2.1) :

| étape | ce qui est lu | source |
|---|---|---|
| 1 | Le gestionnaire de messages du jeu (`0x0049cd00`) dispatche sur `[message+4]` : `eax > 0x400` → autre table ; sinon table d'octets `0x49e8b4` puis table de sauts `0x49e77c` | `0x0049cd3c`-`0x0049cd65` |
| 2 | L'index **72** correspond au **message interne 165** (`0xa5`), dont le cas est `0x0049d468`-`0x0049d494` | `socle-cycle-quete.md` §2.0 |
| 3 | Ce cas appelle le constructeur `0x0048d1b0`, recopie le code de quête à `frame+7`, puis **envoie immédiatement** par la session (`call [edx+0xc4]`) | `0x0049d468`-`0x0049d494` |
| 4 | Le message 165 est produit par `0x00587150` : `mov [eax+0x4],0xa5` (id), `mov [eax+0xb],0x13` (forme), `mov [eax+0x13],ecx` (le paramètre = code de quête) | `0x00587150`, `0x0058716b`, `0x00587178` |
| 5 | Ce constructeur a **deux appelants**, tous deux dans `SUIQuestListWnd` : `0x005890a9` et `0x005891a4`. Le second est le geste : comparaison du bouton cliqué avec la chaîne `quest_info_button` (`VA 0x00a2f5ac`) en `0x0058913e`, garde « aucune quête sélectionnée » (`cmp DWORD PTR [esi+0x4d0],0x0` / `jl`) en `0x00589161`, puis lecture de l'index de sélection `[esi+0x4d0]`, résolution (`0x004c1cb0`) et identifiant (`0x004c1150`) | `0x0058913e`-`0x005891a4` |

Le **libellé** du bouton vit dans les données du client (`data.000`, archive chiffrée) : sa position à
l'écran n'est pas lisible, l'action l'est. Aucun autre chemin d'émission n'existe : le constructeur
`0x0048d1b0` a **un seul appelant** (`xref` sur le rel32 de `.text` → `0x0049d46b`).

Ce geste est une **interrogation** : le client n'écrit rien dans son état local en envoyant 604, et il
n'attend aucune réponse identifiée (§5.3). Rien dans le trajet ne montre un `code` calculé, dérivé ou
borné : c'est l'identifiant de la ligne sélectionnée, tel quel.

## 3. Structure sur le fil

### 3.0 En-tête commun (7 octets)

| offset | type | nom | source |
| --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` (total, en-tête comprise) | dépôt `Game/Network/Packets/Header.cs:9,22` ; client : `mov DWORD PTR [eax],0xb` en `0x0048d1e4` |
| 4 | `uint16` LE | `ID` | `Header.cs:10,23` ; client : `mov edx,0x25c` en `0x0048d1d9`, puis `mov WORD PTR [eax+0x4],dx` |
| 6 | `uint8` | `Checksum` = somme des octets 0..5 | `Header.cs:11,24` ; client : boucle `0x0048d1ea`-`0x0048d1f7`, écriture en `0x0048d1f7` |

### 3.1 `TM_CS_QUEST_INFO` — **11 octets**

| offset | type | nom | valeur observée | source |
| --- | --- | --- | --- | --- |
| 0 | `uint32` LE | `Length` | **11** (`0xb`) | rzu `TS_CS_QUEST_INFO.h:8` ; client `0x0048d1e4` |
| 4 | `uint16` LE | `ID` | **604** (`0x25c`) | `op_codes.md:159` ; rzu `TS_CS_QUEST_INFO.h:12` ; client `0x0048d1d9` |
| 6 | `uint8` | `Checksum` | somme des octets 0..5 | client `0x0048d1ea`-`0x0048d1f7` |
| 7 | **`int32` LE** | `code` | quête sélectionnée dans `SUIQuestListWnd` (recopiée de `[message interne 165 + 0x13]`) | rzu `TS_CS_QUEST_INFO.h:8` ; NGemity `TS_CS_QUEST_INFO.h:7` ; client : recopie `0x0049d476`/`0x0049d479` → `frame+7` |

**Taille totale attendue : 11 octets** — 7 d'en-tête + 4 de `code`. Aucun autre champ, aucun
remplissage, aucune queue variable.

Preuve d'absence de champ supplémentaire, lue dans le constructeur client (`0x0048d1b0`), qui est
**linéaire et sans branche** :

```
0x0048d1b8  mov DWORD PTR [eax],0x7      ; Length = 7 (en-tête seul)
0x0048d1b2  lea ecx,[eax+0x6]            ; borne de la somme = offset 6 exclu
0x0048d1c4  add dl,BYTE PTR [esi]        ; somme des octets 0..5
0x0048d1cb  mov BYTE PTR [ecx],dl        ; Checksum @6 (premier passage, sur la forme de 7 octets)
0x0048d1cf  mov DWORD PTR [eax+0x4],edx  ; edx = 0 -> zéro sur 4 octets : ID @4, Checksum @6, code[0] @7
0x0048d1d2  mov WORD PTR [eax+0x8],dx    ; zéro sur 2 octets : code[1..2] @8
0x0048d1d6  mov BYTE PTR [eax+0xa],dl    ; zéro sur 1 octet : code[3] @10
0x0048d1d9  mov edx,0x25c                ; ID = 604
0x0048d1de  mov WORD PTR [eax+0x4],dx    ; ID @4
0x0048d1e4  mov DWORD PTR [eax],0xb      ; Length = 11
0x0048d1f7  mov BYTE PTR [ecx],dl        ; Checksum @6, recalculé sur la forme finale de 11 octets
```

Le constructeur écrit les offsets 0 à 10 et **rien d'autre** : les trois écritures de zéro couvrent
exactement `4..10` (ID 2 octets, Checksum 1, `code` 4), et la longueur finale vaut `0xb = 11`. Aucun
octet au-delà de 10 n'est touché par le bloc appelant non plus : `0x0049d476` lit `[edi+0x13]` et
`0x0049d479` écrit `DWORD PTR [ebp-0x11]`, soit `frame+7` (`frame = [ebp-0x18]`). Il n'existe donc ni
champ caché ni réserve.

`code` est **signé** : rzu et NGemity le déclarent tous deux `int32_t` et le client le recopie tel
quel. C'est aussi la convention déjà retenue par le dépôt pour son voisin `603`
(`GameActionPackets.TryReadDropQuest` lit un `int32` signé, `Game/Network/Packets/Game/GameActionPackets.cs:187` ;
`QuestDropRules.CheckRequest` refuse un code négatif, `Game/Services/QuestDropRules.cs:19-22`).

## 4. Gating de version — chaque champ est tranché pour Epic 7.3

Valeurs de référence : `EPIC_6_3 = 0x060300`, `EPIC_7_3 = 0x070300`, `EPIC_9_6_3 = 0x090603`
(`librzu/src/lib/Packet/PacketEpics.h:56,59,96`). **Cible : `0x070300`.**

| élément | gating montré par rzu | décision pour 7.3 | source |
| --- | --- | --- | --- |
| id | `X(604, version < EPIC_9_6_3)` / `X(1604, version >= EPIC_9_6_3)` | **604** — `0x070300 < 0x090603`, le `+1000` d'`EPIC_9_6_3` ne s'applique pas | `TS_CS_QUEST_INFO.h:11-13` |
| présence du paquet | « Since EPIC_6_3 » | **présent** — `0x070300 > 0x060300` | `TS_CS_QUEST_INFO.h:10` |
| `code` | **aucun gating** sur le champ : `_(simple)(int32_t, code)`, hors de tout `#if` | **présent**, `int32` LE @7, **signé** | `TS_CS_QUEST_INFO.h:7-8` |
| en-tête 7 octets | aucun gating | **7 octets**, `Length = 11`, `ID = 604` | §3.0 |

Résumé : **aucun champ de 604 n'est gaté par version** ; le seul gating est celui de l'**id**, et il
est tranché par `0x070300 < 0x090603`. Rien dans cette trame n'est dans le piège habituel des
`limit_*` ou des champs `>= EPIC_7_3` : il n'y a qu'un champ.

NGemity confirme la branche basse par une seconde voie : `CREATE_PACKET(TS_CS_QUEST_INFO, 604)` sans
`VER_ID` (`shared/Server/Packets/GameClient/TS_CS_QUEST_INFO.h:10`) — la référence ne connaît donc
**pas** la variante `1604`, ce qui concorde avec la cible 7.3. Cela ne date pas l'apparition de l'id
(pour cela, la seule source est le commentaire « Since EPIC_6_3 » de `TS_CS_QUEST_INFO.h:10` chez rzu
et `:9` chez NGemity) : cette ligne ne fait que montrer que la branche d'id retenue ici est celle que
les deux références prennent hors d'`EPIC_9_6_3`.

## 5. Traitement attendu

### 5.1 Ce que NGemity fait de 604 — **rien**

C'est le point le plus important de cette fiche, et il est négatif :

| vérification | résultat | source |
|---|---|---|
| Id déclaré | oui, `TS_CS_QUEST_INFO = 604` | `shared/Server/ClientPackets.h:164` ; en-tête `TS_CS_QUEST_INFO.h:10` |
| Handler dans `WorldSession` | **non** : la liste des handlers de quête ne contient que `onDropQuest` (603) | `Chihiro/src/Network/GameNetwork/WorldSession.h:107` (voisin de `onContact`/`onDialog`, `:105-106`) |
| Occurrence dans `WorldSession.cpp` | **aucune** | `grep -n "QUEST_INFO" WorldSession.cpp` → 0 |
| Occurrence ailleurs dans NGemity | **une seule**, sans rapport : `TS_QUEST_INFO` (**descendant**, élément de 600) en `Messages.cpp:731` | `Chihiro/src/Network/Messages.cpp:722-755` (`Messages::SendQuestList`, `TS_QUEST_INFO info{}` en `:731`) |

Autrement dit : NGemity **déclare 604 sans jamais le lire**. Un serveur qui reçoit 604 et ne fait rien
n'entre en contradiction avec aucune ligne de la référence — il y a simplement **rien à porter**.

Le chemin attesté le plus proche n'est pas une réponse à 604 : c'est un **dialogue 3000**
(`Messages::SendQuestInformation`, `Chihiro/src/Network/Messages.cpp:622-720`), déclenché par une
**fonction Lua** — `quest_info` est enregistrée en `XLua.cpp:98`, `SCRIPT_QuestInfo` en `:829-839`
appelle `SendQuestInformation(player, code, textID, 0)`. Ce dialogue porte le texte
`QUEST|<code>|<textID>` (`Messages.cpp:676`), un `type` 3/7/8 selon la progression
(`Messages.cpp:627-641`) et un menu de déclencheurs (`start_quest( … )` `:713`, `end_quest( … )`
`:699`). **Ce n'est pas une réponse à 604** : le paquet 604 ne porte pas de `textID`, il ne désigne pas
de PNJ, et il n'y a aucune raison établie de le faire aboutir à un dialogue. À lire comme la
description du **déclencheur** (`socle-cycle-quete.md` §5.2), pas du traitement de 604.

### 5.2 Ce que le client 7.3 sait consommer

| trame | état côté client 7.3 | source |
|---|---|---|
| **602** `TM_SC_QUEST_INFOMATION` (17 o) | **aucun handler** : la table de dispatch des trames reçues envoie 602 (comme 603, 604, 605) sur le défaut « non traité » (`index 18 → 0x0067ef21`) | `socle-quetes.md` §5.4.2 (`0x0067e1d9`, table d'octets `0x0067f35c`, table de sauts `0x0067f310`) |
| **601** `TM_SC_QUEST_STATUS` (40 o) | **handler** `0x0067db20` : écrit `code`, les six `status` et `nProgress` dans l'objet de quête déjà connu du client | `socle-quetes.md` §5.4.2 ; constructeur disponible : `GameQuestPackets.BuildQuestStatus`, `Game/Network/Packets/Game/GameQuestPackets.cs:110` |
| **600** `TM_SC_QUEST_LIST` | **handler** `0x00670cd0`, mais il **remet le conteneur de quêtes à zéro** avant de le reconstruire : une 600 bâtie sur un état partiel **efface** | `socle-quetes.md` §5.4.2, §5.5 |

Conséquence directe : **602 est inutilisable** (le client ne l'écoute pas — c'est le même constat qui
a fait écarter 602 du socle, `socle-quetes.md` §3.6 « Seems unused », `:211-215`), et **600 ne doit
jamais servir de réponse ciblée**.

### 5.3 Ce que le serveur doit faire de 604

**Décision : recevoir 604, la lire bornée, la journaliser, et ne rien répondre.**

Justification : (1) NGemity ne la traite pas (§5.1) ; (2) le client n'a **aucun handler** pour la seule
réponse candidate (§5.2) ; (3) le geste est une interrogation, et l'état que la fenêtre affiche est
**déjà livré** par 600/601 — le socle (a) envoie 600 au login (`QuestService.SendQuestListAsync`,
`Game/Services/QuestService.cs:29-45`) et 601 existe déjà comme constructeur. Inventer une réponse
serait inventer une politique.

**Si l'essai en jeu montre une fenêtre d'information vide**, la seule réponse consommable est **601**
(40 octets, `code` @7, `status[6]` @11, `nProgress` @35, `nTimeLimit` @36) — **jamais 602**. C'est un
arbitrage de terrain, pas une décision de cette fiche : voir `## A VERIFIER PAR KILLIAN`.

**Obligations mécaniques du dépôt** pour qui implémente ce lot (`CLAUDE.md`, « Change guidelines ») :

| obligation | où | source |
|---|---|---|
| déclarer l'id `TM_CS_QUEST_INFO = 604` | `Game/Network/Packets/Enums/GamePackets.cs` (après `TM_CS_DROP_QUEST = 603`, `:147`) | `GamePackets.cs:145-147` |
| ajouter le bras de dispatch **dans le même changement** | `GameClient.cs` : chaîne de `if (header.ID == (ushort)GamePackets.…) { …; continue; }` (modèle 603 en `:1742-1745`) placée **avant** le `switch` final | `GameClient.cs:1742-1745,2062-2074` |
| ne jamais atteindre le `switch` final | `_ => throw new Exception($"Unknown Packet Type {header.ID}")` en `GameClient.cs:2073` — un membre déclaré sans bras **tue la boucle de réception** | `GameClient.cs:2073` |
| refus d'une trame courte | la taille est **11** : `Length != 11` (ou `packet.Length < 11`) → journal + abandon, **sans** `TM_SC_RESULT` ; précédent : 603 (`GameActionPackets.TryReadDropQuest`, `GameActionPackets.cs:178-189`) et `HandleDropQuestAsync` (`GameClient.cs:1003-1009`) | `GameActionPackets.cs:176-189` |
| lecture signée | `BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(7, 4))` — **pas** de `uint`, un `code` négatif ne doit pas devenir un grand code positif | précédent `GameActionPackets.cs:187` ; `QuestDropRules.cs:19-22` |
| tests d'offsets | `Tests/Game/QuestPacketsTests.cs` (fichier existant, y compris un `DropQuestRequestSize = HeaderSize + 4`) : taille totale **11**, `ID` @4 = 604, `code` @7 sur 4 octets, refus d'une trame de 10 octets, conservation d'un code négatif | `Tests/Game/QuestPacketsTests.cs:30-64` |
| **lever le verrou de test sur 604** | `Tests/Game/QuestPacketsTests.cs:239` affirme aujourd'hui `Enum.IsDefined(typeof(GamePackets), (ushort)604).Should().BeFalse()` (commentaire `:235-237`). Déclarer 604 **casse ce test** : il doit passer à `BeTrue()` **dans le même commit**, sinon le critère « enum et dispatch modifiés ensemble » se retourne en échec de suite. 602 et 605 restent à `BeFalse()` | `Tests/Game/QuestPacketsTests.cs:235-240` |

Si le lot choisit **601 en réponse** (arbitrage §5.3), la trame fait **40 octets**
(`GameQuestPackets.QuestStatusSize = HeaderSize + 4 + 6 × 4 + 1 + 4`, `GameQuestPackets.cs:41`) et
`TM_SC_QUEST_STATUS = 601` est **déjà** déclaré (`GamePackets.cs:146`), donc sans nouvelle obligation
d'enum.

Ce qui n'est **pas** demandé : aucune évolution de `QuestService`, aucun accès au catalogue (b2) ni
au déclencheur (b3) — 604 ne transporte ni texte, ni état, ni choix.

### 5.4 Périmètre — **604 est livrable seul**, sans minimum commun avec 605

C'est le point de périmètre que la carte demande de trancher. **Verdict : oui, 604 est livrable seul.
Le minimum livrable n'est *pas* commun avec 605** (carte `BAMvMQ7u`, encore en `BACKLOG`) ; le socle
`§5.5` regroupe 604 et 605 dans un même lot **(b1)** pour une raison **mécanique**, pas une raison de
dépendance.

Ce que 604 demande, exhaustivement : **une** constante dans `GamePackets`, **un** bras de dispatch,
**une** lecture bornée signée de 4 octets, **un** test d'offsets — et **rien à répondre** (§5.3).
Aucune dépendance de données, aucune décision de jeu, aucun accès à `CharacterQuests`, aucun appel à
`QuestService`. Le socle le dit dans la colonne « dépendances » du lot (b1) : **aucune** ; et la
colonne « décision de jeu » : **non**.

L'argument du groupage, tel qu'il figure au socle `§5.5`, est un argument de **fichiers** : 604 et 605
touchent les trois mêmes endroits (`GamePackets.cs`, la chaîne de `if (header.ID == …)` de
`GameClient.cs`, `Tests/Game/QuestPacketsTests.cs`, plus le verrou `Enum.IsDefined(… 604/605 …)` de
`:239`). Un seul commit pour les deux évite de modifier deux fois les mêmes trois fichiers. C'est un
gain de coordination, **pas** une contrainte de faisabilité.

L'argument inverse — livrer 605 seul, ou 604 seul — est **asymétrique**, et c'est ce qui décide :
**605 exige plus que 604**. 605 porte un second champ (`nOptionalReward`, `int8` @11) et surtout une
**réponse** : le socle tranche `TM_SC_RESULT` taggé 605, `NotActable` hors de l'état de quête du
personnage (`socle-cycle-quete.md` §6, ligne « réponse à 605 »), ce qui suppose de **savoir quelle
quête le personnage porte** — donc le chemin `QuestService` / `CharacterQuestEntity` que 603 a déjà
ouvert. 604, lui, ne demande rien de tel.

Conséquences pratiques, sans élargir le périmètre de cette carte :

1. **Si le PO livre 604 seul** : le lot est complet avec les cinq obligations du tableau de §5.3, et le
   test `:239` passe à `Enum.IsDefined(… 604).Should().BeTrue()` **en laissant 602 et 605 à
   `BeFalse()`**. Aucune moitié de paquet, aucun état intermédiaire cassé.
2. **S'il livre (b1) en un seul commit** (604 + 605) : c'est le découpage du socle et il est
   parfaitement valide dès lors que **605 porte sa propre fiche** (`BAMvMQ7u`) — 605 n'est pas
   spécifiée ici, et cette fiche ne la spécifie pas. Un lot ne peut pas être livré sur la
   spécification d'un paquet qui n'a pas la sienne.
3. **Il n'y a aucune raison technique de livrer 605 d'abord** : 605 serait alors le lot le plus lourd
   (état du personnage + réponse) et 604 resterait déclarable plus tard au même coût de trois fichiers.
   C'est au PO de choisir entre « trois fichiers touchés deux fois » et « un seul commit ».

### 5.5 Portée de la validation du `code` contre l'état du personnage — **aucune**

Question posée par la carte. **Décision : la réception de 604 borne et lit, elle ne valide pas le
`code` contre `CharacterQuests`.**

Trois raisons, dans l'ordre de force :

1. **Rien ne dépend du verdict.** 604 n'émet aucune réponse et ne modifie aucun état (§5.3) : valider
   le `code` produirait un booléen que personne ne lit. Un refus silencieux et une acceptation muette
   sont **indiscernables** pour le joueur comme pour le client.
2. **Aucune lecture de l'état n'est pas nécessaire — elle existe, mais elle ne serait lue par
   personne.** `IQuestService`, le service que le client de jeu utilise pour les quêtes, n'expose que
   deux opérations : `SendQuestListAsync(GameClient)` et `DropQuestAsync(GameClient, DropQuestRequest)`
   (`Game/Services/Interfaces/IQuestService.cs:12-25` ; implémentation `Game/Services/QuestService.cs:29-45`
   et `:47-77`). Le dépôt possède pourtant de quoi juger l'appartenance : `ICharacterService` expose
   `GetQuestsAsync(characterName)` (`Game/Services/ICharacterService.cs:38`, implémentation
   `Game/Services/CharacterService.cs:186-189`) et le dépôt de données sait lire **par code**
   (`ICharacterRepository.GetQuestAsync(characterName, code)`, `Game/DataAccess/Repositories/Interfaces/ICharacterRepository.cs:49`,
   implémentation `CharacterRepository.cs:107-110`). Autrement dit, valider serait **possible** ; ce
   serait simplement une requête (ou une API de service supplémentaire) pour un verdict que personne
   ne consomme, sur un bouton dont la cadence de clic n'est pas bornée (§7.5). La règle de réception
   n'a pas à payer ce coût.
3. **Le précédent du dépôt refuse ce que la trame seule ne permet pas de juger**, il n'invente pas de
   règle supplémentaire. `QuestDropRules.CheckRequest` ne juge que le **signe** du code
   (`Game/Services/QuestDropRules.cs:5-22`) : l'appartenance à la liste, elle, est décidée par
   l'**effacement lui-même**, qui répond vrai ou faux (`CharacterService.DropQuestAsync`,
   `Game/Services/CharacterService.cs:191-206`) — c'est pour cela que 603 a besoin du verdict et 604
   non : 603 **agit**, 604 n'agit pas.

**Règle de réception, donc, complète** : trame de **11 octets** exactement, `ID` lu à l'offset 4,
`code` lu en `int32` **signé** à l'offset 7, journal de la réception, **aucune réponse, aucun accès
base de données, aucune mutation**. Le handler peut rester **synchrone** : aucun `await` n'est
nécessaire, contrairement à 603 (`GameClient.cs:1003-1019`).

Ce qui reste ouvert est **du ressort du PO et non du dev** : si un jour un lot veut refuser un `code`
hors de la liste envoyée, le verdict `TM_SC_RESULT` n'est **pas établi** (aucune source ne montre que
le client 7.3 attende un acquittement pour 604) — c'est la question 4 de `§7` et le point 3 de
`## A VERIFIER PAR KILLIAN`.

## 6. Écarts assumés avec NGemity, et pourquoi

NGemity compile en `EPIC_4_1_1` (`shared/Common/Define.h:25`) et déclare 604 sans le traiter (§5.1).
Les écarts ci-dessous sont des écarts de **périmètre**, pas des erreurs de la référence.

| # | écart | rzu / client 7.3 | NGemity | décision |
| --- | --- | --- | --- | --- |
| 1 | Traitement de 604 | le client 7.3 émet 604 et n'attend aucune réponse qu'il sache lire (§5.2) | **aucun handler** : id déclaré, jamais lu | **ne rien répondre** : transposition fidèle, aucune logique à porter |
| 2 | Réponse candidate | 602 existe chez rzu mais **n'a aucun handler** dans le client 7.3 | émet un **dialogue 3000** par Lua (`Messages.cpp:622-720`, `XLua.cpp:829-839`) | **ni 602, ni un 3000** en réponse à 604 : le 3000 décrit le *déclencheur* (`socle-cycle-quete.md` §5.2) |
| 3 | Exécution du chemin de quête | — | Lua (`XLua.cpp:98` enregistre `quest_info`) | Navislamia **n'exécute pas de Lua** : toute reprise serait une grammaire fermée en C#, et elle n'est pas nécessaire ici |
| 4 | Signature de `code` | `int32_t` chez rzu ET chez NGemity ; le client recopie le dword tel quel | `int32_t code` | **lu signé**, jamais replié en `uint` — c'est la traduction fidèle des trois |
| 5 | Nom du paquet | `TM_CS_QUEST_INFO` (`op_codes.md:159`) — chaîne **absente** du binaire client (l'identification vient du constructeur) | `TS_CS_QUEST_INFO` | garder le nom de `op_codes.md` pour l'enum du dépôt, comme pour 603 |

Ce tableau resserre la ligne « réponse à 604 » de `socle-cycle-quete.md` §6 et n'ajoute que les deux
écarts de **signature** et de **nom** propres à cette fiche ; les écarts de catalogue, de scripts et de
`value1..12` traités par le socle ne concernent pas 604, qui ne transporte aucun de ces champs.

## 7. NON ÉTABLI — questions ouvertes, à trancher

Aucune n'empêche le lot de réception (§5.3). Aucune ne doit être **devinée** par l'implémentation.

1. **Ce que le serveur 7.3 d'origine répondait à 604** — c'est la réserve §7.2 de
   `socle-cycle-quete.md`, reprise ici. Aucune référence ne le montre : NGemity n'a pas
   de handler (§5.1), et le client 7.3 n'a pas de handler pour 602 (§5.2). La fiche tranche « pas de
   réponse » ; si l'essai en jeu montre une fenêtre d'information vide, la seule réponse consommable
   est **601** — mais **rien ne prouve** que 601 suffise à remplir cette fenêtre plutôt qu'à mettre à
   jour un état déjà affiché. Question : 601 est-elle lue par cette fenêtre, ou seulement par la liste ?
2. **Ce que le libellé `quest_info_button` ouvre exactement.** Le bouton est identifié par sa chaîne
   (`VA 0x00a2f5ac`) et par le trajet jusqu'à l'envoi (§2) ; **la fenêtre qu'il ouvre côté client n'est
   pas lisible** (interface dans `data.000`, archive chiffrée). La fiche ne peut donc pas dire si 604
   alimente `SUIQuestInfoWnd`, une infobulle ou une fenêtre de journal.
3. **Le contrôle de taille côté client.** Aucun test de longueur n'a été trouvé dans le trajet
   d'émission ; on ne sait pas si le client refuse une réponse de taille inattendue (il n'en reçoit
   aucune dans la lecture de cette fiche). Le serveur, lui, doit exiger 11 — c'est une règle de
   robustesse, pas une preuve client.
4. **Le domaine de `code`.** Le client envoie l'identifiant de la ligne sélectionnée, donc un code
   présent dans la **liste que le serveur a lui-même envoyée** (600). Aucune borne n'est établie
   (NGemity n'en pose aucune puisqu'il ne lit pas la trame). Question : refuser par `TM_SC_RESULT`
   taggé 604 une trame dont le `code` ne correspond à aucune quête de la liste envoyée, ou l'ignorer
   silencieusement ? À défaut, le précédent du dépôt est de **refuser ce que la trame seule ne permet
   pas de juger** (`QuestDropRules.CheckRequest`, `QuestDropRules.cs:13-22`).
5. **Cadence.** Rien n'indique si le joueur peut marteler `quest_info_button` (un clic = un paquet,
   sans garde visible). Aucune limitation de débit n'est établie et aucune n'est proposée ici.
6. **Le mode de lecture sous forte concurrence.** 604 ne touche à aucun état : le handler peut rester
   synchrone. À confirmer avec le dev, mais aucune source ne montre d'accès base de données sur ce
   chemin.

## 8. Commits épinglés

| référence | commit | date | ce qui en est tiré |
| --- | --- | --- | --- |
| `reference/rzu` (librzu) | **`87c1e83bf84efe29bb6405e8e6da80349712f3fa`** | 2023-10-02 | `TS_CS_QUEST_INFO.h:7-15` : champ `int32_t code`, gating d'id `604`/`1604`, « Since EPIC_6_3 » |
| `reference/ngemity/Chihiro` | **`38ceb2c6065fabf6ff4ba71d52f955f362c6c839`** | 2025-12-03 | `shared/Server/Packets/GameClient/TS_CS_QUEST_INFO.h:6-12` (déclaration), `shared/Server/ClientPackets.h:164` (enum), `WorldSession.h:105-107` et `WorldSession.cpp` (**absence** de handler), `Messages.cpp:622-720` (dialogue de quête, chemin Lua), `Messages.cpp:722-755` (`TS_QUEST_INFO` descendant de 600), `XLua.cpp:98,829-839` (`quest_info`) |
| `reference/client73/SFrame.exe` | pas de dépôt git — empreinte `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (9 841 664 octets) | lecture statique `objdump -d -M intel` : `0x0048d1b0` (constructeur, `Length = 0xb`, `ID = 0x25c`), `0x0049d468`-`0x0049d494` (envoi), `0x00587150` et `0x005890a9`/`0x005891a4` (geste `quest_info_button`) |

Fiches du dépôt utilisées (à leur dernier commit sur `master`) : `docs/packet-specs/socle-quetes.md`
(600-605, état du personnage, tables de dispatch client) et `docs/packet-specs/socle-cycle-quete.md`
(geste, gating, découpage, réponse à 604).

## 9. Implémentation livrée (navis-dev)

Branche `hermes/packet-604-quest-info` ; le code et les tests sont dans le commit `094a390`, la présente
section dans le commit de fiche qui la porte (`1aed911` est le commit de la fiche archéologue).

| Fichier | Ce qui y a été fait |
| --- | --- |
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_QUEST_INFO = 604`, juste après `TM_CS_DROP_QUEST = 603` (famille quête groupée). |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `QuestInfoRequest(int Code)`, `public const int QuestInfoRequestSize = HeaderSize + 4` (= 11) et `TryReadQuestInfo`, placés à côté de leur frère 603 : c'est là que vivent les lecteurs C→S de la famille (`GameQuestPackets` ne porte que les constructeurs S→C). |
| `Game/Network/Clients/GameClient.cs` | Bras de dispatch `TM_CS_QUEST_INFO` juste après celui de 603 (donc avant le `switch` final) et `HandleQuestInfo(byte[])` **synchrone**. |
| `Tests/Game/QuestPacketsTests.cs` | 8 tests neufs, dont 4 cas de taille : le total de la suite passe de **1494** à **1502**. Le verrou d'énumération est passé à `BeTrue()` pour 604, 602 et 605 restant à `BeFalse()`. |

### 9.1 Le lecteur, et le seul choix qui restait ouvert

`TryReadQuestInfo` refuse toute trame dont la taille n'est **pas exactement 11 octets**, puis lit `code`
en **`int32` signé** à l'offset 7 (`packet.Slice(HeaderSize, 4)`), et rien d'autre.

Le §5.3 laissait le choix entre `Length != 11` et `packet.Length < 11`, et le §7.3 dit que « le serveur
doit exiger 11 » : c'est le **refus exact** qui a été retenu.

- Le constructeur client (`0x0048d1b0`) est linéaire et n'écrit que les offsets 0-10 : 604 n'a ni champ
  caché ni queue variable (§3.1), donc une trame plus longue est une anomalie de protocole, pas une
  variante à lire partiellement.
- C'est le patron déjà écrit deux fois dans ce dépôt pour des trames fixes : `TryReadCheckIllegalUser`
  (57) et `TryReadXtrapCheck` (59, « a short or padded frame is refused »), tous deux à 11 octets.
- La boucle de réception ne peut de toute façon lire que `header.Length` octets, donc refuser les deux
  bords ne coûte rien à un client honnête et documente le verdict.

### 9.2 Ce que le serveur ne fait pas — décision, pas oubli

- **Aucune réponse** n'est émise : ni 602 (aucun handler client, §5.2), ni 601 (rien ne montre que la
  fenêtre d'information la lise plutôt que la liste, §7.1), ni `TM_SC_RESULT`. Une trame refusée est
  journalisée en `Warning` et **rien** n'est envoyé.
- **Aucune validation du `code`** contre `CharacterQuests` ni contre un domaine : c'est la portée du
  §5.5, et elle est appliquée telle quelle. L'intervalle du §7.4 n'est donc pas jugé — aucune borne
  n'étant établie, trancher serait inventer une politique, et aucun verdict n'aurait de consommateur
  puisque rien ne répond.
- Le handler est **synchrone**, sans accès base ni écriture d'état : il lit et journalise en `Debug`.
  C'est la confirmation attendue par le §7.6, et la raison pour laquelle une réception réussie laisse
  `Connection.Sent` vide.
- **Aucune limitation de débit** n'est ajoutée : le §7.5 constate qu'aucune cadence n'est établie, et
  une garde inventée serait une politique de jeu non sourcée.

### 9.3 Preuve par mutation

Chaque mutant a été construit sur `094a390`, exécuté, puis retiré (`git checkout --`).

| # | Mutant | Résultat observé |
| --- | --- | --- |
| 1 | garde de taille `!= QuestInfoRequestSize` → `< QuestInfoRequestSize` | `TryReadQuestInfo_RejectsAPaddedFrame` et `_RejectsAQuestStatusSizedFrame` échouent (2 sur 4 cas) : la borne haute est bien tenue par le code. |
| 2 | bras de dispatch 604 retiré de `GameClient.cs` | `OnDataReceived_ConsumesQuestInfoWithoutThrowingOrAnswering` et `…CoalescedWithTheNextOne` échouent sur `System.Exception: Unknown Packet Type 604` : un id déclaré sans bras tue la boucle de réception, exactement le piège du critère « enum et dispatch ensemble ». |
| 3 | offset de lecture `HeaderSize` → `HeaderSize - 1` | `TryReadQuestInfo_ReadsTheSignedCodeAtSeven` et `_KeepsANegativeCodeSigned` échouent : les deux tests d'offset sont porteurs. |

La lecture **signée** n'a pas de mutant exécutable : le champ du `record struct` est un `int`, donc
remplacer la lecture par `ReadUInt32LittleEndian` ne compile qu'avec un cast `unchecked` explicite, qui
rend de nouveau -1. Le test `_KeepsANegativeCodeSigned` fige donc l'interprétation `int32_t` des deux
références (§3.1) plutôt qu'un piège de cast — c'est le même partage que pour 603, où le refus du code
négatif vit dans `QuestDropRules`, pas dans la lecture.

### 9.4 État mesuré

- `dotnet build Navislamia.sln -c Debug` : code de sortie **0**, 0 erreur, 190 avertissements
  (préexistants).
- `dotnet test Tests/Tests.csproj` : code de sortie **0**, **1502 passés, 0 échec** (1494 mesurés sur
  `1aed911` avant le lot).
- `git log --oneline origin/master..master` : **vide** — aucun commit sur `master` locale.

### 9.5 Collisions d'ancrage assumées

`GamePackets.cs` (ligne de `TM_CS_DROP_QUEST`), la chaîne de dispatch de `GameClient.cs` et
`Tests/Game/QuestPacketsTests.cs` sont les trois fichiers que le lot 605 touchera aux mêmes endroits
(§5.4) : les conflits d'énumération et de verrou `Enum.IsDefined` sont attendus à la fusion des deux
branches et se résolvent sur trois lignes (604 et 605 côte à côte, verrou passé à `BeTrue()` pour l'un
comme pour l'autre). Le reste du diff est isolé : un lecteur neuf, un handler neuf, huit tests neufs.

## A VERIFIER PAR KILLIAN

Ce que la lecture seule ne tranche pas, et qui demande une vérification **avec le client 7.3 réel**
(jamais par un script ni par `SFrame.exe` lancé seul) :

| # | Point | Vérification à faire | Pourquoi la lecture ne suffit pas |
|---|---|---|---|
| 1 | Faut-il répondre à 604 ? (§5.3, §7.1 ; réserve §7.2 du socle) | recevoir 604 sans répondre, ouvrir la fenêtre d'information d'une quête **réellement portée** (donc après le lot (b3) : catalogue + déclencheur) et regarder si elle s'affiche | aucun handler client pour la réponse candidate (602) et **aucun handler serveur** chez NGemity : les deux références sont muettes, l'effet perçu est le seul juge |
| 2 | Si oui, 601 suffit-elle ? (§5.3, §7.1) | comparer « 604 sans réponse » et « 604 → 601 » sur la même quête | 601 a un handler à `0x0067db20`, mais rien ne montre que la **fenêtre d'information** le lise plutôt que la liste |
| 3 | Refus d'une trame de taille fausse ou d'un `code` hors liste (§5.3, §7.4) | côté serveur : envoyer 604 tronquée / avec un `code` inconnu et vérifier que rien ne casse et qu'aucune réponse n'est attendue par le client | aucune borne n'est établie par les références ; le choix du verdict est une décision de robustesse |
| 4 | Le bouton et la fenêtre (§7.2) | confirmer que `quest_info_button` ouvre bien une fenêtre d'information de quête et repérer ce qu'elle affiche | l'interface est dans `data.000` (archive chiffrée) : le libellé et la fenêtre ne sont pas lisibles statiquement |

Ce que cette fiche **ne** demande **pas** de vérifier : l'id (604), la taille (11 octets), l'offset du
`code` (7) et sa signature (`int32`) — tranchés par le client (§3), rzu (§4) et la cohérence des trois
sources.

## Annexe — bloc destiné à `CLAUDE.md`

`CLAUDE.md` est protégé par Hermes côté worker : il est livré ici et dans la description de la MR,
à coller par l'opérateur.

```markdown
### Quête 604 — `TM_CS_QUEST_INFO`

- **604, 11 octets** (en-tête 7 + `code` `int32` LE @7), en 7.3 : rzu remappe en `1604` à partir
  d'`EPIC_9_6_3` (`TS_CS_QUEST_INFO.h:11-13`), et `code` n'est gaté par aucun `>= EPIC_*`. Le
  constructeur client écrit `Length = 0xb` (`SFrame.exe 0x0048d1e4`) et n'initialise que les offsets
  0-10 : pas de champ caché.
- Le geste : sélectionner une quête dans `SUIQuestListWnd` puis cliquer `quest_info_button`
  (`0x0058913e`), ce qui envoie le `code` de la ligne sélectionnée, sans autre chemin d'émission
  (`0x48d1b0` a un seul appelant, `0x0049d46b`).
- **NGemity déclare 604 et ne la lit jamais** (`ClientPackets.h:164`, aucun handler dans
  `WorldSession`) : il n'y a **aucune logique à porter**. Le dialogue 3000
  (`Messages::SendQuestInformation`) n'est pas une réponse à 604 — c'est le transport du
  **déclencheur**, émis par Lua (`XLua.cpp:829`).
- Le client 7.3 **n'a pas de handler pour 602** (`0x0067f35c` → défaut `0x0067ef21`) : ne jamais
  émettre 602 en réponse. La seule trame consommable si un rafraîchissement s'avère nécessaire est
  **601** ; 600 remet le conteneur à zéro et ne sert pas de réponse ciblée.
- Décision du lot : recevoir 604 bornée à **exactement** 11 octets, journaliser, **ne rien répondre**.
  Aucune validation du `code` contre `CharacterQuests` : le verdict ne serait lu par personne (603, lui,
  agit et répond). Handler synchrone, sans accès base : `GameClient.HandleQuestInfo`.
- **604 a été livré seul** : `GamePackets.TM_CS_QUEST_INFO = 604`, bras de dispatch dans `GameClient.cs`,
  `GameActionPackets.TryReadQuestInfo` et `QuestInfoRequestSize = 11`, tests dans
  `Tests/Game/QuestPacketsTests.cs`, détail et preuves par mutation dans
  `docs/packet-specs/604-quest-info.md` §9. La ligne `Enum.IsDefined(604).Should().BeFalse()` de
  `QuestPacketsTests` est passée à `BeTrue()`, 602 et 605 restent à `BeFalse()`. 605 exige plus (état du
  personnage + réponse `NotActable`) et garde sa propre branche.
- Le savoir durable de ce paquet est dans `docs/packet-specs/604-quest-info.md` (lu avec
  `socle-cycle-quete.md` et `socle-quetes.md`).
```
