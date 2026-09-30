# 4000 — `TM_CS_HUNTAHOLIC_INSTANCE_LIST`

Suivi : `navislamia:packet:4000` ; carte Trello `9mXwzRvf` (cycle du 2026-09-27) ; lot **S2** du socle
`docs/packet-specs/socle-instances-jeu.md`.

Fiche du paquet **4000** seul (client → serveur) : format sur le fil, gating Epic 7.3, conduite serveur
attendue. Elle reprend et **vérifie** la §3.3.1 du socle (`socle-instances-jeu.md:192-203`) et sa ligne
`4000` du tableau §1.2 (`:42`), puis **tranche** ce que le socle laissait ouvert : le sort de la réponse
`4001`/`4002` (§5.3) et la réconciliation du « `4 o` » de son tableau §5.2 (`:465`).

Le lot S2 du socle range trois ids (`4000`, `4001`, `4002`). **Aucun de `4001`/`4002` n'a de carte, et
cette fiche ne les déclare ni ne les spécifie** : elle dit pourquoi (§5.3) et ce qu'il faudrait pour les
ouvrir (`## A VERIFIER PAR KILLIAN`, points 1 à 3). Le paquet **4003** de la même famille a sa propre
carte, sa propre branche (`hermes/packet-4003-huntaholic-create-instance`, MR #38 **ouverte, non
mergée**) et sa propre fiche ; il n'est cité ici qu'au titre de ses preuves **et** de la collision de
fichier qu'il annonce (§5.6).

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id décimal | `4000` (`0x0FA0`) | `op_codes.md:226` — `[4000] = "TM_CS_HUNTAHOLIC_INSTANCE_LIST"` |
| Nom `TM_CS_*` | `TM_CS_HUNTAHOLIC_INSTANCE_LIST` | idem |
| Sens | client → serveur | rzu `TS_CS_HUNTAHOLIC_INSTANCE_LIST.h:13` (`SessionPacketOrigin::Client`) |
| Taille totale | **11 octets** (en-tête de 7 + charge utile de 4) | §3.1 |
| État du dépôt | **absent** de `GamePackets.cs` à la base `b56967a` : la bande `4xxx` n'y contient que `TM_CS_INSTANCE_GAME_* = 4250-4253` et `TM_CS_COMPETE_* = 4500/4502` (`Game/Network/Packets/Enums/GamePackets.cs:202-214`) | lecture du fichier |
| Nature | **demande de page** de la liste des salles du lobby HuntaHolic. La réponse attendue est `4001` (`TM_SC_HUNTAHOLIC_INSTANCE_LIST`), **hors périmètre** de ce lot (§5.3) | rzu `TS_SC_HUNTAHOLIC_INSTANCE_LIST.h:6-11` ; client `0x6707f0` |

Le nom `TM_CS_*` est celui de `op_codes.md` ; rzu et NGemity nomment la même trame
`TS_CS_HUNTAHOLIC_INSTANCE_LIST` (`TS_` = paquet de session, `TM_` = paquet monde). Les deux
désignent l'id 4000 : aucune divergence de nom à signaler.

---

## 2. Ce que le joueur fait pour que le client l'envoie

Le client émet 4000 dans **deux situations seulement**, toutes deux liées à la fenêtre du lobby
HuntaHolic (`Create: SUIHuntaHolicLobbyWnd`, chaîne `0xa49898`) :

1. **À l'ouverture du lobby** — le client récupère (ou crée) son objet « huntaholic » puis demande
   **la page 1** :
   - `0x564d00`-`0x564d1f` : `cmp BYTE PTR [ebp+0x8],0x0` / `je 0x564d84` — le corps n'est exécuté que
     si l'argument booléen de la méthode est vrai ;
   - `0x564d38`-`0x564d46` : recherche de l'objet par son nom, chaîne `"huntaholic"` (`0xa1fa18`) via
     `0x4c7e10` ;
   - `0x564d48`-`0x564d70` : s'il est absent, allocation de `0xf8` (248) octets (`0x97671b`) puis
     construction par `0x4caa70` (nom `"huntaholic"`) ;
   - `0x564d7b` : `push 0x1` — **la page demandée est 1** — puis `0x564d7f` : `call 0x4c91f0`
     (le constructeur du §3.1).
2. **À chaque rafraîchissement de la liste** — deux sites d'appel passent **la page courante** de
   l'objet, avec un plancher à 1 :
   - `0x565c3d` : `cmp DWORD PTR [eax+0x3c],0x1` / `jge 0x565c52` ; si la page courante est `< 1`,
     `0x565c4e` : `push 0x1`, sinon `0x565c77` : `push [system+0x3c]` ; puis `0x565c84` :
     `call 0x4c91f0` ;
   - `0x4c9520` (gestionnaire de messages de `SHuntaHolicSystem`, cas 4005 « quitter l'instance » —
     table de sauts `0x4c9840`) : `0x4c97f2`-`0x4c97f5` : `mov ecx,DWORD PTR [esi+0x3c]` /
     `push ecx`, puis `0x4c97f8` : `call 0x4c91f0`.

Ces **trois** sites d'appel sont les seuls du binaire (`grep "call 0x4c91f0"` → `0x4c97f8`, `0x564d7f`,
`0x565c84`). Le constructeur envoie lui-même : `0x4c922a` charge la session (`mov ecx,[ecx+0x20]`),
`0x4c9236` empile la trame et `0x4c923a` : `call 0x4c7aa0`, qui est un **saut virtuel par
l'emplacement `+0xc4` de la vtable** de session (`0x4c7aa3-0x4c7aae`) — le même emplacement que la
famille `4250`/`4251` (`socle-instances-jeu.md:131`). Il n'y a **pas** d'émission locale (le
gestionnaire de messages local est `0x4c7ab0`, emplacement `+0x124` : le constructeur de 4000 ne
l'appelle pas).

**Le numéro de page est 1-basé et vient de la réponse.** Le champ `[system+0x3c]` que les deux
rafraîchissements renvoient est écrit par le traitement de la réponse `4001` (`0x4ca530`) :
`0x4ca544`-`0x4ca556` comparent `total_page` (`[msg+0x1f]`) à `page` (`[msg+0x17]`) et posent
`[system+0x3c] = page` si `total_page >= page`, sinon `= total_page`. Autrement dit le client
**demande toujours une page qu'il croit valide** ; il ne demande jamais 0 (plancher `jge 1` ci-dessus).
C'est la seule sémantique de `page` que le client atteste.

Preuve de l'exclusivité : la constante `0x0FA0` apparaît **44 fois** dans `.text` (balayage des octets
du fichier), dont **une seule** écrit un id de trame — `0x4c9205` dans le constructeur. Toutes les
autres occurrences sont des valeurs ou des déplacements (`[reg+0xfa0]`, p. ex. `0x6ca493` où `0xfa0`
est un **seuil de niveau** entre `0xbb8`/3000 et `0x1388`/5000, ou `0x9a84f4` où `0xfa0` indexe la
table de messages `ERROR_WINS_INTERNAL` de Windows). Aucune n'est une comparaison d'id.

---
## 3. Structure sur le fil

### 3.1 Table des champs — **11 octets**

| Offset | Type | Nom | Valeur observée / preuve | Source |
|---|---|---|---|---|
| 0 | `uint32` LE | `Length` | `11` (`0xb`), écrit en dur | rzu `PacketDeclaration.h:616-621` (en-tête de 7) ; client `mov DWORD PTR [ebp-0xc],0xb` en `0x4c920e` |
| 4 | `uint16` LE | `ID` | `4000` (`0x0FA0`) | client `mov eax,0xfa0` en `0x4c9205` puis `mov WORD PTR [ebp-0x8],ax` en `0x4c920a` |
| 6 | `uint8` | `Checksum` | somme des 6 premiers octets, mod 256 | client : boucle `0x4c9220`-`0x4c922a` (somme de `[ebp-0xc]` à `[ebp-0x7]`, écriture en `[ebp-0x6]`) ; dépôt `Game/Network/Packets/PacketExtensions.cs:13-25` |
| 7 | `int32` LE | `page` | **variable** : `1` à l'ouverture du lobby, `[system+0x3c]` au rafraîchissement | rzu `TS_CS_HUNTAHOLIC_INSTANCE_LIST.h:8` ; client `mov edx,DWORD PTR [ebp+0x8]` puis `mov DWORD PTR [ebp-0x5],edx` en `0x4c9230`-`0x4c9237` |

**Taille totale attendue : 11 octets.** 7 d'en-tête + 4 de charge utile ; le client l'écrit lui-même
(`0xb` en `0x4c920e`) et les trois sources concordent (§3.2). La trame est de **taille fixe** : aucune
longueur variable, aucun tableau, aucun remplissage. Le tampon de pile va de `[ebp-0xc]` à `[ebp-0x2]`
inclus, soit 11 octets — l'octet `[ebp-0x2]` est le dernier octet de `page`.

### 3.2 Concordance des trois sources

| Source | Référence | Ce qu'elle donne |
|---|---|---|
| rzu | `TS_CS_HUNTAHOLIC_INSTANCE_LIST.h:7-8` | `_(simple)(int32_t, page)` — un seul champ, 4 octets |
| rzu (id) | idem `:10-11` | `X(4000, true)` — id 4000, **sans condition de version** |
| rzu (en-tête) | idem `:13`, `PacketDeclaration.h:616-621` | `CREATE_PACKET_VER_ID(...Client)` → en-tête de **7** octets |
| NGemity | `shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_INSTANCE_LIST.h:6-9` | `_(simple)(int32_t, page)` + `CREATE_PACKET(TS_CS_HUNTAHOLIC_INSTANCE_LIST, 4000)` |
| NGemity (id) | `shared/Server/ClientPackets.h:233` | `TS_CS_HUNTAHOLIC_INSTANCE_LIST = 4000` |
| Client 7.3 | `0x4c91f0` (constructeur) | `Length = 0xb`, `ID = 0xfa0`, `int32` écrit à l'offset 7 |
| op_codes | `op_codes.md:226` | `[4000] = "TM_CS_HUNTAHOLIC_INSTANCE_LIST"` |

Les trois sources concordent **sans réserve** : 11 octets, un `int32` à l'offset 7. Contrairement à
`4003`/`4004` (chaînes de taille fixe), il n'y a ici **aucune question de troncature** : le champ est un
entier, et il n'y a pas de NUL à valider.

### 3.3 Convention d'en-tête du dépôt — recoupée, pas supposée

Le dépôt mesure son en-tête à 7 octets ; les quatre relèves suivantes le confirment pour ce paquet :

- rzu `PacketDeclaration.h:576-581` (`CREATE_PACKET`, taille `7`) et `:616-621`
  (`CREATE_PACKET_VER_ID`, taille `7`, celui qu'emploie 4000) ;
- `Game/Network/Packets/Header.cs:6-25` : `[StructLayout(LayoutKind.Sequential, Pack = 1)]`,
  `uint Length` + `ushort ID` + `byte Checksum` = 7, avec lecture aux offsets 0, 4 et 6 ;
- `Game/Network/Packets/PacketExtensions.cs:13-25` : le checksum est la somme des 4 octets de `Length`
  et des 2 octets de `ID` — la même somme de 6 octets que la boucle du client (`0x4c9220`-`0x4c9228`) ;
- précédent du dépôt pour une charge utile unique de 4 octets à l'offset 7 :
  `TryReadSummonCardSkillList` (`Game/Network/Packets/Game/GameActionPackets.cs:78-98`), qui compare
  `packet.Length` à `HeaderSize + 4` — soit exactement la forme de 4000.

Il n'existe pas de constante `HeaderSize` partagée : chaque fichier de paquets déclare la sienne en
privé (`GameActionPackets.cs:9`, `GameCommercialStoragePackets.cs:33`, `GameChatPackets.cs:10`, ...).
Le fichier de la famille HuntaHolic suit cette convention (§5.6).

### 3.4 Forme du test d'offsets attendu (critère transversal 3)

Un test suffit et doit être **exact** :

1. trame valide de **11 octets** — `Length` lu à 0 == 11, `ID` lu à 4 == 4000, `Checksum` lu à 6 == la
   somme des octets 0 à 5 mod 256, `page` lu **signé** à 7 (un `int32`) ;
2. **refus** d'une trame de 10 octets et d'une trame de 12 octets (le lecteur ne doit pas lire un
   `int32` tronqué ni accepter un remplissage) ;
3. `page` doit être lu à l'offset **7**, et non 6 ni 8 : le test doit écrire une valeur dont les octets
   rendent l'erreur visible (p. ex. `0x0BADC0DE`), et vérifier que la valeur relue est bien celle-là ;
4. valeur `page = 1` relue telle quelle, et `page` négatif relu tel quel (pas de `uint`) — le client
   n'en émet pas (§3.5) mais le champ est `int32_t` chez rzu : le dev **journalise la valeur brute**,
   il ne la valide pas contre une règle inventée (§7(d)).

Il n'y a **pas** de test de tableau dans ce lot : le critère « N=0 → 23 o, N=1 → 61 o, N=2 → 99 o » du
`socle-instances-jeu.md:477` est un critère de **`4001`**, pas de `4000` (§5.3). Le recopier tel quel
dans le lot 4000 ferait échouer la revue.

### 3.5 Sémantique de `page` telle que le client l'atteste

- **1-basé** : l'ouverture demande `1` (`push 0x1` en `0x564d7b`) et le rafraîchissement applique un
  plancher `jge 1` (`0x565c3d`) avant de renvoyer la page courante (`0x565c77`) ;
- **fourni par la réponse `4001`** : `[system+0x3c]` est posé par `0x4ca544`-`0x4ca556` à partir de
  `page` et `total_page` de la réponse, avec un écrêtage de `page` à `total_page` ;
- **jamais 0, jamais négatif** dans les trois sites d'émission connus ;
- la valeur est un `int32` signé sur le fil (rzu `:8`), sans contrainte de plage attestée côté serveur.

---

## 4. Gating de version

**Décision pour Epic 7.3 : `4000` est déclaré sans condition de version, et sa forme de 7.3 est
celle décrite au §3 — 11 octets, `page` `int32` à l'offset 7, aucun champ gaté, aucune variante.**

Chaîne de preuve :

1. rzu `TS_CS_HUNTAHOLIC_INSTANCE_LIST.h:10-11` : `X(4000, true)`. Le second argument de `X(...)` est
   la condition d'émission de l'id (`SERIALISATION_F_ID1(id_) id = id_;` pour la forme à un argument,
   `SERIALISATION_F_ID2(id_, condition_) if (condition_) id = id_;` pour la forme à deux —
   `PacketDeclaration.h:585-589`). `true` est **inconditionnel** : à la différence de `212`
   (`int64_t` seulement à partir d'`EPIC_4_1_1`) ou de `9005` (id remappé en 8105 dès `EPIC_9_6_3`),
   **il n'y a ici aucun remappage d'id et aucune variante de champ**.
2. rzu `:5` : `// Last tested: EPIC_9_8_1` — la déclaration est vérifiée jusqu'à `EPIC_9_8_1`
   (`PacketEpics.h:116`, `0x090801`), donc bien au-delà d'`EPIC_7_3` (`PacketEpics.h:59`,
   `0x070300`). Le socle §4.1 arrive à la même conclusion pour les 17 opcodes de la famille
   (`socle-instances-jeu.md:360-390`) : aucun n'est renuméroté, tous sont valides en 7.3.
   *Précision que le socle ne fait pas et qui vaut pour 4000* : son en-tête ne porte **aucun**
   commentaire `// Since EPIC_*` (contrairement à `4250`-`4253`, `4011` et `4012`, qui portent
   `// Since EPIC_6_3`). L'epic d'**introduction** de 4000 n'est donc pas écrit dans rzu — cela ne
   gate rien (l'absence de commentaire n'est pas une contrainte, et `X(4000, true)` est
   inconditionnel), et la question ne se pose pas pour 7.3 : le client 7.3 écrit `0xfa0` en dur
   (§3.1), ce qui tranche la validité à 7.3 indépendamment du commentaire.
3. NGemity `TS_CS_HUNTAHOLIC_INSTANCE_LIST.h:9` : `CREATE_PACKET(..., 4000)` — forme **non versionnée**
   (un seul id), cohérente avec rzu.
4. Client 7.3 : la seule écriture d'id est `0x4c9205` (`mov eax,0xfa0`), sans test de version — le
   client de 7.3 envoie donc l'id 4000, et il n'y a pas de second id candidat dans le binaire (§2).

**Conséquence pour le dev** : `TM_CS_HUNTAHOLIC_INSTANCE_LIST = 4000` doit être déclaré tel quel, sans
`if (version >= ...)`, sans membre alternatif, et la taille attendue reste 11 quel que soit le client
Epic 7.3. Toute variante « 8.x » qui apparaîtrait dans rzu pour cet id doit être ignorée — il n'y en a
pas.

---
## 5. Traitement attendu

### 5.1 Ce que les deux références serveur en font : **rien**

- **NGemity (Chihiro)** : 4000 est **déclaré et jamais traité**. L'id existe
  (`shared/Server/ClientPackets.h:233`), l'en-tête du paquet est inclus
  (`shared/Server/XPacket.h:108`) et la structure est complète
  (`shared/Server/Packets/GameClient/TS_CS_HUNTAHOLIC_INSTANCE_LIST.h:6-9`), mais la liste des paquets
  réellement traités est la table `worldPacketHandler[]` (`WorldSession.cpp:92-137`, **45 entrées**),
  dont chaque entrée est construite par `declareHandler` (`:78-90`) : l'id y est déduit du type du
  paquet (`handlerData.cmd = T::getId(EPIC_4_1_1)`, `:81`) et il n'existe **aucun**
  `onHuntaholic*` dans `Chihiro/src`. Un `grep -rn HUNTAHOLIC --include=*.cpp --include=*.h
  Chihiro/` ne remonte que des éléments sans rapport (drapeaux d'objet `ItemTemplate.hpp:200,218`,
  types de monstres `MonsterBase.h:65-68`, et des `case SKILL_*` **commentés** dans
  `Skill.cpp:1418-1433`, dont `SKILL_WARP_TO_HUNTAHOLIC_LOBBY = 64818` — `SkillBase.h:224`).
- **rzu** : la famille n'existe que comme **déclarations de paquets** dans
  `librzu/src/packets/GameClient/`. Aucune occurrence de `HUNTAHOLIC` hors de ces en-têtes : ni dans
  `rzgame/`, ni dans `rzauth/`, ni ailleurs (`grep -rn HUNTAHOLIC --include=*.cpp --include=*.h
  --include=*.hpp .` → seuls les fichiers `TS_*_HUNTAHOLIC_*.h` et le type
  `TS_HUNTAHOLIC_INSTANCE_INFO.h`).
- Les données de référence, elles, existent : `reference/ngemity/Database/Arcadia.sql:19136-19160`
  (`HuntaholicResource`, **une seule ligne**, `id = 10000`, `name_id = 80010000`) et
  `:18826-18852` (`HuntaholicInstanceResource`, **10 lignes**, toutes `huntaholic_id = 10000`). Elles
  intéressent un futur lot `4001`, pas celui-ci (§5.3).

**Conséquence** : il n'y a **aucune logique serveur à porter** pour 4000. Le client 7.3 est le seul
arbitre, comme pour `452` et `711`.

### 5.2 Ce que le client 7.3 attend en retour — et ce qu'il n'attend pas

**(a) La réponse attendue est `4001` (`TM_SC_HUNTAHOLIC_INSTANCE_LIST`).** Établi par le client seul :
le routeur de paquets entrants compare `cmp eax,0xfa1` en `0x67e6e6` / `je 0x67e729`, qui appelle le
lecteur **`0x6707f0`**. Ce lecteur :

- lit **quatre `DWORD`** aux offsets 7, 11, 15 et 19 du tampon reçu (`mov eax,[edi+0x7]` `0x670832`,
  `mov edx,[edi+0xb]` `0x670838`, `mov eax,[edi+0xf]` `0x67083e`, `mov edx,[edi+0x13]` `0x670844`) ;
- refuse le tableau si `infos` (`[edi+0xf]`) est `<= 0` (`cmp eax,ecx` / `jle 0x67087b`, `0x67084a`) ;
- alloue `infos × 0x26` = `infos × 38` octets (`mov edx,0x26` `0x670850`, `imul ecx,ecx,0x26`
  `0x670867`) et recopie depuis l'offset **23** (`add edi,0x17` `0x67086b`) ;
- poste un message interne 0x7e (`mov DWORD PTR [eax+0x4],0x7e` `0x670808`, vtable `0xa52100`) vers
  `SHuntaHolicSystem`, dont le gestionnaire `0x4ca530` range les valeurs et **écrête la page**
  (§3.5).

Autrement dit : **le seul paquet qui alimente le lobby est 4001**, et c'est la réponse attendue à
4000. C'est cohérent avec le socle (`socle-instances-jeu.md:262-277`) à l'octet près — et, sur les
quatre `DWORD`, avec l'ordre des champs de rzu (`huntaholic_id`, `page`, `infos`, `total_page`).
**Le contenu de ce paquet n'est pas spécifié par cette fiche** : ses trois inconnues sont au §5.3.

**(b) Le client n'a *aucune* réaction à un 4000 sans réponse, refusé ou ignoré.** Chaîne de preuve sur
`SFrame.exe` désassemblé (`objdump -d -M intel`, 2 256 750 lignes, sha256 en §8) :

1. la constante `0x0FA0` n'apparaît qu'**une seule fois** comme écriture d'id de trame
   (`0x4c9205`, le constructeur) sur 44 occurrences dans `.text` (§2) ;
2. **elle est absente de la cascade `MSG_RESULT`** (`0x4770a0`, handler du cas `MSG_RESULT`), où
   figurent pourtant ses voisines : `0x4774dd` : `cmp eax,0xfa4` (4004) / `jg 0x4775ec`,
   `je 0x477503` pour 4004, `cmp eax,0x19a` (410) en `0x4774ea`, `cmp eax,0x2bc` (700) en `0x4774f1`,
   `cmp eax,0xfa3` (4003) en `0x4774f8`. Un message de résultat portant l'id de requête **4000**
   tombe dans le défaut : `jne 0x4776ef` en `0x4774fd`, et `0x4776ef`-`0x4776ff` est l'épilogue nu de
   la fonction (aucun affichage, aucune notice) ;
3. le routeur de paquets entrants (`0x67e6e6` et suivants) ne route pas 4000 — normal, c'est un id
   client → serveur ;
4. aucun gestionnaire de la famille n'attend 4000 : la table de sauts de `SHuntaHolicSystem`
   (`0x4c9840`) couvre les ids **4003 à 4008** et rien d'autre (§5.4).

**Donc journaliser et ignorer un 4000 est invisible pour le joueur** : pas de notice, pas de
`msgbox`, pas de déconnexion, aucune fenêtre bloquée identifiée. C'est la même conduite que `452`
(`GameClient.cs:1520-1529` : « the frame is read, bounded and logged, and nothing is answered »).

### 5.3 Le sort de `4001`/`4002` : **ce lot ne les livre pas**

Le socle range trois ids dans S2 et le critère propre du lot (`socle-instances-jeu.md:477`) porte sur
**`4001`** (« N=0 → 23 o, N=1 → 61 o, N=2 → 99 o ; pas de 38 ; `name` de 31 octets non tronqué à 30 »).
Réponse de la fiche : **`4001` et `4002` restent hors périmètre**, parce que les trois décisions dont
ils dépendent n'ont **aucune source** à la base `b56967a` et relèvent de Killian :

1. **le contenu de la liste** — le serveur n'a *aucun* catalogue d'instances HuntaHolic : ni entité,
   ni migration, ni table EF pour `HuntaholicInstanceResource` / `HuntaholicResource`
   (`grep -rni huntaholic Game/DataAccess/Entities` → aucune classe de ressource). Le seul matériau
   est le dump de référence `Arcadia.sql:18826-18852` (10 lignes) et `:19136-19154` (1 ligne) : il
   renseigne *un* lobby (`10000`) et ses 10 paliers, mais ce n'est pas une décision de jeu ;
2. **la pagination** — combien d'entrées par page, et donc quel `total_page` : choix libre, aucune
   source (socle §7i, `:602`) ;
3. **le `huntaholic_id`** — le client ne l'envoie **pas** (4000 ne porte qu'une page, §3.1) : la
   valeur à renvoyer n'est établie par aucune source serveur (socle §7l, `:602-606`), même si les
   deux tables de référence ci-dessus ne connaissent qu'un lobby, `10000` (§7(a), avec les deux
   sources concordantes).

Décision du lot, alignée sur les précédents du dépôt (`452`, `4003`) : **déclarer + router + lire +
journaliser, sans réponse, sans état, sans table.** Aucun paquet de réponse n'est inventé ici ;
`4001`/`4002` demandent leurs propres cartes, après l'arbitrage de Killian (`## A VERIFIER PAR
KILLIAN`, points 1 à 3).

### 5.4 Ce que le lot ne débloque pas

- **Rien de visible pour le joueur.** La fenêtre du lobby s'ouvre, demande la page 1 — et reste
  **vide** : pas de liste, donc aucune salle à créer ni à rejoindre (`4003`/`4004` sont d'autres
  lots), et pas de début de chasse (`4011`/`4012`).
- Le socle dit `4000` « déjà alimenté par les tables `HuntaholicResource`/`HuntaholicInstanceResource` »
  (`socle-instances-jeu.md:465`) : **cette phrase vaut pour le dump SQL de référence, pas pour le
  serveur Navislamia**, qui n'a aucune entité de ces tables (§5.3 point 1). C'est une précision, pas
  une contradiction : la fiche `4001` devra créer ce catalogue avant d'émettre quoi que ce soit.
- La valeur du lot est donc **protocolaire** : un id de la famille cesse d'être non géré, sa taille et
  ses offsets sont épinglés par un test, et le jour où `4001` arrive, le point d'entrée existe déjà.

### 5.5 Points d'implémentation (ce que le dev doit écrire)

1. **`Game/Network/Packets/Enums/GamePackets.cs`** : `TM_CS_HUNTAHOLIC_INSTANCE_LIST = 4000`, dans la
   bande `4xxx`, en **ordre croissant d'id** — donc **avant** `TM_CS_INSTANCE_GAME_ENTER = 4250`
   (`GamePackets.cs:204`), après `TM_CS_CHECK_CHARACTER_NAME = 2006`. Commentaire de renvoi à cette
   fiche, comme les blocs `4250-4253` et `4500-4502` le font (`:202-203`, `:209-211`). *Si la MR #38
   a atterri avant*, son `4003` est inséré après `4253` : la résolution doit remettre **4000 puis
   4003 puis 4250** dans l'ordre croissant, et fusionner les deux blocs de commentaires.
2. **`Game/Network/Clients/GameClient.cs`** : une branche de routage **avant le `switch` final**
   (`:1865`, `_ => throw new Exception("Unknown Packet Type ...")`), sur le modèle exact du 452
   (`:1520-1529`), avec le commentaire qui dit *pourquoi* il n'y a pas de réponse. Aucun membre de
   `GamePackets` ne doit atteindre le `throw` (critère transversal 4).
3. **Le lecteur**, dans un fichier de la famille (§5.6) : `TryReadHuntaholicInstanceList(packet, out
   int page)` avec `packet.Length != 11` → refus (pas de lecture partielle), `page` lu **signé** à
   l'offset 7 (`BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(7, 4))`). Le champ est `int32_t`
   chez rzu : **ne pas le lire en `uint`**.
4. **La journalisation**, sur le modèle du 452 (`GameClient.cs:497-512`) : `Warning` + `ClientTag` +
   `Length` si la trame est mal formée, `Debug` (gardé par `IsEnabled(LogEventLevel.Debug)`) avec la
   valeur de `page` si elle est valide. **Aucune réponse, aucun changement d'état, aucune commande
   DevConsole** n'est requise par ce lot.
5. **Le test d'offsets** (§3.4), dans `Tests/Game/` : `Tests/Game/HuntaholicInstanceListPacketsTests.cs`
   (convention par paquet : `Tests/Game/SummonCardSkillListPacketsTests.cs` pour le 452,
   `Tests/Game/InstanceGamePacketsTests.cs` pour le S1). Le nom du fichier **n'est pas** celui de
   `4003` : un test par paquet évite l'`add/add` subi par le fichier de production (§5.6).
6. **Aucune constante de version, aucun `if (version >= ...)`** (§4). Aucun champ optionnel.

**Baseline relevée sur cette branche, à la base `b56967a`** (`NUGET_PACKAGES=/srv/navislamia/.nuget-cache`) :
`dotnet build Navislamia.sln -c Debug` → **code 0** (0 erreur, 7,6 s) ; `dotnet test
Tests/Tests.csproj` → **1302 réussis, 0 échec, 0 ignoré** (code 0). Le plancher « au moins 366 tests »
des critères transversaux est donc largement tenu par la base ; le dev doit au minimum ne pas
descendre sous **1302**.

### 5.6 Zones de recouvrement à nommer dans la MR

Le PO a mesuré, au réveil du 2026-09-27, **26 MR ouvertes** : `GamePackets.cs` est touché par **19**,
`GameClient.cs` par **20**. Le lot ne peut éviter ni l'un ni l'autre ; ce sont des conflits de
**bande** (insertions voisines), pas de logique.

**Collision directe, à nommer :** la **MR #38** (branche `hermes/packet-4003-huntaholic-create-instance`,
ouverte, non mergée) crée `Game/Network/Packets/Game/GameHuntaholicPackets.cs`, **absent de `master`**.

Décision de découpage de cette fiche : le lecteur de 4000 va dans **ce même fichier de famille**,
`Game/Network/Packets/Game/GameHuntaholicPackets.cs`, parce que le dépôt range déjà une famille par
fichier (`GameInstanceGamePackets.cs` pour `4250-4253`, `GameCompetePackets.cs` pour `4500-4502`,
`GameAuctionPackets.cs`, `GameRankingPackets.cs`). Conséquence assumée : deux branches ajoutent le
même chemin → **conflit `add/add`** au second merge, résolu en gardant **les deux corps** (ils sont
complémentaires : 4000 d'un côté, 4003 de l'autre) et en fusionnant les commentaires d'en-tête.
**Aucune primitive de #38 ne doit être utilisée** : rien de `GameHuntaholicPackets.cs` n'existe sur
`master`, le lot doit compiler seul. Si la QA juge l'`add/add` trop coûteux, la variante équivalente
est de mettre le lecteur de 4000 dans `GameActionPackets.cs` (fichier du 452) : **le lot ne doit pas
dépendre du choix de #38 pour compiler.**

### 5.7 Implémentation livrée (branche `hermes/packet-4000-huntaholic-instance-list`)

Écrite d'après §5.5, sans écart de périmètre : **déclarer, router, lire, journaliser, ne rien
répondre**. Quatre fichiers touchés.

| Fichier | Ce qui y est écrit |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs:202-207` | `TM_CS_HUNTAHOLIC_INSTANCE_LIST = 4000`, bloc de commentaire de renvoi à cette fiche, **inséré avant** le bloc `TM_CS/SC_INSTANCE_GAME_* = 4250-4253` (ordre croissant d'id, §5.5 point 1) |
| `Game/Network/Clients/GameClient.cs:1172-1203` | `HandleHuntaholicInstanceList(byte[])` : `Warning` + `ClientTag` + `Length` si `Length` ≠ 11, sinon `Debug` gardé par `IsEnabled(LogEventLevel.Debug)` avec la valeur **brute** de `page` ; aucune réponse, aucun état, aucune commande DevConsole |
| `Game/Network/Clients/GameClient.cs:1463-1474` | le bras de routage, **avant le `switch` final** (critère transversal 4) |
| `Game/Network/Packets/Game/GameHuntaholicPackets.cs` (nouveau, 65 lignes) | classe `public static class GameHuntaholicPackets`, `private const int HeaderSize = 7`, `PageOffset = 7`, `PageFieldLength = 4`, `PayloadLength = 4`, `InstanceListLength = 11`, et `TryReadHuntaholicInstanceList(ReadOnlySpan<byte>, out int page)` |
| `Tests/Game/HuntaholicInstanceListPacketsTests.cs` (nouveau) | **19 cas** (§5.5 point 5) |

**Choix d'implémentation à connaître pour la fusion :**

1. **`page` est lu signé et brut** : `BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(PageOffset,
   PageFieldLength))`, `out int` — pas de `uint`, pas d'écrêtage, pas de plancher à 1, pas de refus
   d'une valeur hors bornes (§7c laisse la règle ouverte : le lecteur ne l'invente pas).
2. **Seule la longueur exacte de 11 est acceptée** (`packet.Length != InstanceListLength` → `false`
   **et `page = 0`**, aucune lecture partielle). Un refus laisse donc `out page` à zéro : le test
   négatif vérifie les deux à la fois.
3. **Le bras de routage est placé juste avant le bras `TM_CS_INSTANCE_GAME_ENTER`** (ligne 1468),
   c'est-à-dire dans le voisinage thématique de la famille « instances de jeu », et **pas** après le
   bras du 452 : c'est un choix de **fusion**, pour ne pas atterrir sur l'ancre que d'autres branches
   ouvertes utilisent déjà à l'entrée du `switch` (§5.6).
4. **Résolution de l'`add/add` de `GameHuntaholicPackets.cs`** (si #38 atterrit avant ou après) :
   garder **une** déclaration `public static class GameHuntaholicPackets`, **un** `using`/namespace,
   **un** `private const int HeaderSize = 7` (les deux corps le déclarent), puis l'**union** des
   membres (`NameOffset`…`TryReadCreateInstance` pour le 4003, `PageOffset`…`TryReadHuntaholicInstanceList`
   pour le 4000) et la fusion des deux résumés de classe. Les deux lecteurs sont indépendants : aucun
   n'appelle l'autre.
5. **Ordre de l'enum après fusion avec #38** : #38 a inséré `TM_CS_HUNTAHOLIC_CREATE_INSTANCE = 4003`
   **après** `TM_SC_INSTANCE_GAME_SCORE_REQUEST = 4253` ; la résolution doit remettre **`4000`, puis
   `4003`, puis `4250`-`4253`** dans l'ordre croissant et fusionner les deux blocs de commentaires
   (§5.5 point 1).

**Commandes et relevés de cette branche** (`NUGET_PACKAGES=/srv/navislamia/.nuget-cache`) :

- `dotnet build Navislamia.sln -c Debug` → **code 0** (0 erreur, 164 avertissements préexistants) ;
- `dotnet test Tests/Tests.csproj` → **code 0**, **1321 réussis, 0 échec, 0 ignoré** = les 1302 de
  `master` (§5.5) **+ 19** nouveaux cas. Le compte ne baisse pas.

**Preuve que les tests mordent** (mutation temporaire du lecteur, rejouée sur cette branche) :

- lire le champ à l'offset **6** au lieu de 7 (`PageOffset - 1`) fait **échouer 4 cas**
  (`ReadsPageAtOffsetSeven`, `ReadsTheValueLittleEndian`, `ReadsPageAsSignedInt32`,
  `ReadsTheRawPageWithoutClampingOrDefaulting`) → c'est bien la **position** qui est mesurée, et un
  décalage d'un seul octet est détecté ;
- relâcher le contrôle de longueur (`< HeaderSize + 1` au lieu de `!= InstanceListLength`) fait
  **échouer 2 cas** de plus (`RejectsATruncatedFrame`, `RejectsAPaddedFrame`) → les cas négatifs
  mesurent la **taille** et pas seulement l'absence d'exception.

**Aucun champ `NON ÉTABLI` n'a été deviné** : ni `huntaholic_id`, ni pagination, ni contenu de liste,
ni règle de refus pour un `page` hors bornes — les points (a) à (d) de §7 et les points 1 à 4 de la
section `A VERIFIER PAR KILLIAN` restent ouverts et sont à l'arbitrage de Killian.

---

## 6. Écarts assumés avec NGemity, et pourquoi

| Point | NGemity | Ce que fait cette fiche | Pourquoi |
|---|---|---|---|
| Traitement de 4000 | déclaré (`ClientPackets.h:233`), **jamais routé** (absent de `worldPacketHandler[]`) | déclarer **et router** | un `Switch` de réception qui ne connaît pas l'id ferait tomber la boucle sur le `throw` final ; le dépôt a déjà tranché ce cas pour `452` |
| Forme du paquet | `_(simple)(int32_t, page)` + `CREATE_PACKET(..., 4000)` | identique | aucune divergence entre rzu et NGemity : pas d'écart à assumer sur le format |
| Réponse | aucune | aucune réponse | `4001` dépend de trois décisions sans source (§5.3) ; NGemity ne fournit donc **aucun** modèle à copier, et rzu non plus (§5.1) |
| Version | id non versionné | id non versionné (§4) | `X(4000, true)` |

Aucun écart de comportement n'est introduit « contre » NGemity : NGemity n'implémente simplement pas
la famille HuntaHolic, comme il n'implémente pas `452`. Le seul arbitre est le client 7.3, ce qui est
la doctrine déjà écrite dans `452-summon-card-skill-list.md` §6.

---

## 7. `NON ÉTABLI`

### (a) Quelle valeur de `huntaholic_id` pour la future réponse `4001` ?

Le client ne l'envoie pas (§3.1) et aucune source serveur ne le fixe. **Deux sources concordantes
donnent un candidat unique, `10000`, mais aucune ne prouve que le serveur 7.3 doive l'envoyer :**

- le dump de référence `reference/ngemity/Database/Arcadia.sql:19159` : `HuntaholicResource` contient
  **une seule ligne**, `id = 10000`, `name_id = 80010000` (et `:18842-18851` : les 10 lignes de
  `HuntaholicInstanceResource` portent toutes `huntaholic_id = 10000`) ;
- le **client 7.3 lui-même** : `reference/client73/db_huntaholicresource.rdb` (152 octets ; en-tête
  daté `20091218` puis, à `0x80`, le **nombre de lignes = 1** suivi d'une ligne de 20 octets à
  `0x84`) ne porte qu'une entrée : `10000` (`0x84`), `80010000` (`0x88`) — le même `name_id` que le
  dump. La structure `en-tête de 0x80 + compte + lignes` est confirmée sur trois autres `.rdb`
  (`db_auctioncategoryresource` : 38 lignes, pas de 24 ; `db_banword` : 1028 lignes, pas de 256 ;
  `db_basicstat` : 881 lignes, pas de 16).

Ce qui manque pour trancher : **le client se sert-il de `huntaholic_id` pour retrouver sa ligne de
`db_huntaholicresource.rdb` ?** Le handler `0x4ca530` range la valeur reçue en `[system+0x38]`
(`0x4ca53e-0x4ca541`), mais aucun accès à `+0x38` n'a été relevé dans le code de `SHuntaHolicSystem`
(plage `0x4c8…`-`0x4cb…`) : la lecture qui consomme ce champ n'est pas identifiée. À mesurer :
chercher le lecteur de `[objet_du_lobby+0x38]` (ou la fonction qui indexe la table `HuntaholicResource`
du client), puis vérifier si l'id reçu doit correspondre à une ligne, sous peine de lobby sans nom.

### (b) La pagination de `4001` : combien d'entrées par page ?

Aucune source : c'est un choix serveur (socle §7i, `socle-instances-jeu.md:602`). Le client **écrête**
la page reçue à `total_page` (`0x4ca544-0x4ca556`) et ne demande jamais 0 : un serveur qui renvoie
`total_page` incohérent avec `infos` ne plantera pas le client, mais la page courante du joueur sera
fausse. À trancher par Killian (contenu + pagination) avant d'ouvrir la carte `4001`.

### (c) Que fait un serveur d'un `page` nul, négatif, ou supérieur à `total_page` ?

Le client n'en émet jamais (§3.5), et aucune référence ne montre de règle serveur. Ce qui est
**établi** : le champ est un `int32` signé sur le fil, et le client sait écrêter sa propre page. Ce qui
n'est **pas** établi : refuser, écrêter, ou renvoyer une liste vide. Le lot 4000 journalise la valeur
**brute**, sans règle inventée.

### (d) Un 4000 sans réponse a-t-il un coût visible côté client ?

Établi : aucun gestionnaire indexé sur l'id 4000 (§5.2), donc aucune notice ni fenêtre. **Non établi** :
un éventuel délai d'attente ou un indicateur de chargement **non indexé sur l'id** (p. ex. piloté par
la fenêtre) ne serait pas visible dans ce balayage. À mesurer : test en jeu (Killian), impossible sur
ce VPS ; à défaut, chercher dans `SUIHuntaHolicLobbyWnd` une temporisation de rafraîchissement.

### (e) Le nom des champs 4 et 5 de `db_huntaholicresource.rdb`

La ligne du client se lit `(10000, 80010000, 0, 1500, 3000)`. L'appariement avec
`HuntaholicResource` (`daily_begin_time = 0`, `hunting_period = 1500`, `cycle = 3600`,
`objective_point = 3000`, `max_point = 6000`) se fait **par valeur** : `0` = `local_flag`,
`1500` = `hunting_period`, `3000` = `objective_point`. Vraisemblable, mais l'ordre des champs du
`.rdb` client n'est pas prouvé (5 champs de 4 octets contre 14 colonnes en base) : à ne pas citer
comme établi.

### (f) Contradictions et renvois imprécis relevés dans les documents mergés (signalés, non corrigés)

1. **`socle-instances-jeu.md:91`** (attribution des chaînes du journal de débogage du client) : le
   socle écrit « `sub eax,0xfa3` (4003) … puis 4004 … puis `sub eax,0xf6` → 4250, … (p. ex.
   `0xa5245c` pour 4003, `0xa524ac` pour 4250) ». Relevé du désassemblage : le branchement de `0x66e0df`
   associe **4003 → `0xa523d0`**, **4004 → `0xa52418`**, **4250 → `0xa5245c`**, et `0xa524ac` est la
   chaîne du cas **1308** (`sub eax,0x51c` en `0x66e04a`), sans rapport avec la famille. Les deux
   exemples cités par le socle sont donc mal attribués (les textes coréens se lisent, décodés en
   UTF-8 : `인스턴스게임 입장 에러` = « erreur d'entrée d'instance de jeu » pour 4250,
   `베어로드 플레이 횟수 부족으로 방생성 실패` pour 4003, `…입장 실패` pour 4004). **Aucune
   conclusion de cette fiche ne dépend de cette attribution** : 4000 n'a **pas** de chaîne dans ce
   journal (seuls 4003, 4004 et 4250 y figurent).
2. **`socle-instances-jeu.md:385`** cite `PacketDeclaration.h:625-630` pour la sémantique de
   `X(id, condition)`, et la fiche `4003` (`docs/packet-specs/4003-huntaholic-create-instance.md`)
   cite `:609-612` : la macro qui porte cette sémantique est
   `SERIALISATION_F_ID1/2` en `reference/rzu/librzu/src/lib/Packet/PacketDeclaration.h:585-589`
   (`:609-612` est l'en-tête de sérialisation versionné, `:625-630` l'expansion des `case` de
   commutation). Le fond des deux fiches est juste ; seul le renvoi de ligne est imprécis.
3. **`socle-instances-jeu.md:465`**, colonne « Charge utile » : « `4 o` » pour `4000`. Réconcilié ici
   (§3.1) : **4 octets est la charge utile**, la **taille totale est 11 octets**. Le tableau du socle
   est homogène s'il désigne la charge utile (ses colonnes disent « 0 » pour `4251`/`4252`, dont la
   taille totale est 7).

---
## 8. Commits et binaires épinglés

| Référence | Commit / empreinte | Chemin |
|---|---|---|
| `rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (« packets: fix TS_SC_INVENTORY with older epics ») | `/srv/navislamia/reference/rzu` |
| `ngemity` (Chihiro) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (« Fix compilation issue with GCC ») | `/srv/navislamia/reference/ngemity` |
| Client Epic 7.3 | `SFrame.exe`, 9 841 664 octets, sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` | `/srv/navislamia/reference/client73/SFrame.exe` |
| Données client | `db_huntaholicresource.rdb`, 152 octets | `/srv/navislamia/reference/client73/db_huntaholicresource.rdb` |
| `Navislamia` (base de la branche) | `b56967a07430422add88e0e5cdf292b41b18f6c6` (`master`, 2026-09-27) | `/srv/navislamia/Navislamia` |

Outils de relevé : `objdump -d -M intel` sur `SFrame.exe` → 2 256 750 lignes de désassemblage
(adresses = VA) ; `strings -t x -n 6` (offsets **fichier**, à convertir en VA par la table de
sections : `.rdata` = fichier `0x60da00` = VA `0xa0f000`) ; lecture directe des octets pour les
immediats et pour les `.rdb` (nombre de lignes en `0x80`, puis lignes de taille fixe).

## 9. Bloc prêt à coller dans `CLAUDE.md`

> ### Paquet 4000 — `TM_CS_HUNTAHOLIC_INSTANCE_LIST` (page de la liste du lobby HuntaHolic)
>
> **11 octets, `X(4000, true)` chez rzu : aucun gating de version, aucune variante.** En-tête de 7
> octets + **un seul** `int32` signé `page` à l'offset 7. Le client 7.3 le construit en `0x4c91f0`
> (`mov DWORD PTR [ebp-0xc],0xb` en `0x4c920e`, id `mov eax,0xfa0` en `0x4c9205`, `page` écrit en
> `[ebp-0x5]` en `0x4c9237`, checksum = somme des 6 premiers octets en `0x4c9220`-`0x4c922d`), avec
> **trois** et seulement trois sites d'appel : `0x4c97f8` (cas 4005 du gestionnaire de messages),
> `0x564d7f` (ouverture du lobby, `push 0x1` = **page 1**) et `0x565c84` (rafraîchissement de la page
> courante). Fiche : `docs/packet-specs/4000-huntaholic-instance-list.md`.
>
> **La réponse attendue est 4001, et elle n'est pas livrée ici.** Le routeur du client lit `0xfa1` en
> `0x67e6e6` / `je 0x67e729` → lecteur `0x6707f0`, qui lit **quatre `DWORD`** aux offsets 7, 11, 15 et
> 19 (`huntaholic_id`, `page`, `infos`, `total_page`) puis `infos` entrées de **38** octets à partir
> de l'offset **23** (`mov edx,0x26` en `0x670850`, `add edi,0x17` en `0x67086b`) : 23 + 38·N. Le
> handler `0x4ca530` **écrête** la page reçue à `total_page` et la garde en `[system+0x3c]` — le champ
> même que 4000 renvoie au rafraîchissement : la page est donc **1-basée** et le serveur doit envoyer
> un `total_page` cohérent.
>
> **Le client ne réagit à rien d'autre :** l'id `0x0FA0` n'apparaît qu'une fois comme écriture d'id
> sur 44 occurrences dans `.text`, et il est **absent de la cascade `MSG_RESULT`** (`0x4770a0`), où
> 4004/410/700/4003 sont comparés avant le défaut (`jne 0x4776ef` en `0x4774fd`) — journaliser et
> ignorer un 4000 est donc **invisible** pour le joueur.
>
> **NGemity ne tranche rien** : 4000 y est déclaré (`shared/Server/ClientPackets.h:233`) et absent de
> `worldPacketHandler[]` (`WorldSession.cpp:92-137`), comme toute la famille ; `rzu` n'a que les
> en-têtes (aucune occurrence hors `librzu/src/packets/`). Le client 7.3 est le seul arbitre.
>
> **Lot S2 (partie 4000)** : lecture, refus propre si `Length` ≠ 11, journalisation, **sans réponse,
> sans état, sans table** — `4001`/`4002` demandent leurs propres cartes, après arbitrage du contenu,
> de la pagination et du `huntaholic_id`. `TM_CS_HUNTAHOLIC_INSTANCE_LIST = 4000` doit être déclaré
> dans `GamePackets` **et** routé dans `GameClient.OnDataReceived` (aucun id ne doit atteindre le
> `throw` final).

---

## A VERIFIER PAR KILLIAN

1. **Contenu de la liste `4001` — décision métier.** Aucune source serveur à la base : le serveur n'a
   ni entité ni table pour `HuntaholicInstanceResource`/`HuntaholicResource` (seul le dump de
   référence `reference/ngemity/Database/Arcadia.sql:18826-18852` en donne 10 lignes, toutes
   `huntaholic_id = 10000`). Faut-il créer ce catalogue avant d'ouvrir la carte `4001` ?
2. **Pagination de `4001`.** Combien d'entrées par page, donc quel `total_page` ? Le client écrête la
   page à `total_page` (`0x4ca544-0x4ca556`) : une valeur incohérente laisse le joueur sur une page
   fausse, sans plantage.
3. **`huntaholic_id`.** Deux sources concordantes pointent `10000` (`Arcadia.sql:19159` et
   `db_huntaholicresource.rdb` du client 7.3, une seule ligne : `10000`, `80010000`) : confirmer que
   le serveur doit renvoyer cette valeur. Le client se sert-il de ce champ pour retrouver sa ligne de
   ressource ? La lecture consommatrice n'a pas été identifiée (§7a).
4. **`page` hors bornes.** Faut-il refuser, écrêter ou renvoyer une liste vide pour un `page` nul,
   négatif ou supérieur à `total_page` ? Le client n'en émet jamais (§3.5) et aucune référence
   serveur ne tranche (§7c).
5. **Correction documentaire à valider** (`socle-instances-jeu.md:91`) : les exemples d'attribution
   des chaînes du journal de débogage sont faux — 4003 → `0xa523d0`, 4004 → `0xa52418`,
   4250 → `0xa5245c` ; `0xa524ac` appartient au cas 1308 (`0x66e09d`). Aucune conclusion de cette
   fiche n'en dépend (§7f). Deux renvois de ligne à corriger au passage : `socle-...:385`
   (`PacketDeclaration.h:625-630` → `:585-589`) et la fiche 4003 (`:609-612` → `:585-589`).
6. **Collision de merge à trancher si nécessaire** : la MR #38 crée
   `Game/Network/Packets/Game/GameHuntaholicPackets.cs`, et cette fiche place le lecteur de 4000 dans
   **ce même** fichier de famille (convention du dépôt) → conflit `add/add` au second merge, résolu en
   gardant les deux corps. Variante sans `add/add` : le lecteur de 4000 dans `GameActionPackets.cs`
   (fichier du 452). Le lot ne dépend d'aucune primitive de #38 (§5.6).
