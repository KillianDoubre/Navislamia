# 263 — `TM_CS_TRANSMIT_ETHEREAL_DURABILITY`

> Fiche d'archéologie de protocole. Elle ne touche pas au code serveur : elle établit le geste, la
> trame, le gating de version, la conduite attendue, et nomme ce qui n'est pas décidable par lecture.
> Branche `hermes/packet-263-transmit-ethereal-durability`, base `master`
> `b56967a07430422add88e0e5cdf292b41b18f6c6` (« Merge pull request #44 from
> KillianDoubre/hermes/fix-devconsole-compilation »).
>
> **Socle de rattachement** : `docs/packet-specs/socle-artisanat-objets.md` (lobe B « durabilité
> éthérée », §2.4, §3.5, §5.1-5.2, §6.3, §6.4, §8.4, §9.3.5). Cette fiche **re-source** chaque ligne
> qu'elle reprend et ne se contente pas de la recopier ; les trois ajouts de cette fiche par rapport au
> socle sont signalés §2.2, §2.3 et §5.2.

---

## 1. Identité

| Élément | Valeur | Source |
| --- | --- | --- |
| id | **263** (`0x107`) | `op_codes.md:91` `[263] = "TM_CS_TRANSMIT_ETHEREAL_DURABILITY"` |
| nom | `TM_CS_TRANSMIT_ETHEREAL_DURABILITY` | idem |
| sens | client → serveur (`SessionPacketOrigin::Client`) | rzu `TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h:13` |
| famille | artisanat / objets (lobe B) | `GamePackets.cs:56-62` |
| id post-9.6.3 | **`1263`** — non déclaré en 7.3 | rzu `…_DURABILITY.h:10-11` |
| id voisin | 264 `TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT` (`0x108`) | `op_codes.md:92` ; rzu `…_TO_EQUIPMENT.h:1` |

Le nom du paquet est **aussi porté par le client** : table d'annotation `TM_*` de `SFrame.exe`,
`sframe.dump:26371` (et `:26370` pour 264) ; le nom `window_main_inventory_repair.nui` est à
`sframe.dump:24723` et la RTTI de la fenêtre `.?AVSUIRepairWnd@@` à `sframe.dump:44328`.

---

## 2. Ce que le joueur fait pour que le client l'envoie

### 2.1 Le geste, tel que le client le décrit lui-même

La fenêtre d'inventaire « repair » (`window_main_inventory_repair.nui`) porte de **trois** modes
étiquetés par le client (`db_string.dump`, chacun estampillé `<7.2>`), et deux blocs d'aide :

| Preuve | Source |
| --- | --- |
| `Charge Stone<7.2>` (libellé d'onglet/bouton) | `db_string.dump:189800` |
| Aide de la fenêtre : « *To charge your Ethereal Stone : Place any equipment in Materials Slots 1-9 and click the "Charge Stone" button. … **This item will be permanently destroyed and cannot be recovered!*** » | `db_string.dump:189878` |
| Confirmation, au singulier : `Would you like to charge Ethereal with this item?<(version:7.3)>` | `db_string.dump:222405` (clé `ui_text9816` en `:222404`) |
| Aide de la fenêtre, autre encart : « *1. Place any equipment into your Materials Slots. 2. Click the "Charge Stone" button. — These items will be destroyed!* » | `db_string.dump:222403` |
| Info-bulle de la pierre : `The Charge Stone charges the Ethereal Stone by extracting the durability of equipment` — formule `Ethereal Stone/Big Ethereal Stone (Main Slot) + **Equipment (Consumed)** + Ethereal Charge Stone (Consumed)` | `db_string.dump:137609` |
| Le consommable : `Ethereal Charge Stone <Durability-Charging Consumable Material>` | `db_string.dump:8629` |
| Résultat annoncé au joueur : `You have charged the Ethereal Stone with #@Ethereal_Durability@# Ethereal. Durability points were extracted from the equipment item.` | `db_string.dump:151304` (clé `msg_Ethereal_Durability_7900` en `:151303`) |
| Refus nommé par le serveur : `rzlabs_msg_ethereal_durability` = « `<b>#@item_name@#</b> cannot be sacrificed or has no durability to sacrifice!` » | `db_string.dump:1015-1016` |
| Plafonds de la pierre : `You cannot recharge the Ethereal stone exceeding 1000.` (pierre) / `… exceeding 10000.` (grande pierre) | `db_string.dump:137571` / `:137583` |
| Manquant à l'appel : `Ethereal Stone required!<7.2>` | `db_string.dump:189826` |
| Textes d'état de la fenêtre : `inventory_ui_text9738/9739` = `Restoration complete!/Restoration failed!`, `9734/9735` = `Repair complete!/Repair failed!`, `9742/9743` = `Recharge complete!/Recharge failed!` | `db_string.dump:189811-189830` |

**Lecture retenue** (elle est univoque, et elle est celle du socle §2.4) : 263 est le geste
« **charger la pierre éthérée en sacrifiant un équipement** ». Le handle de la trame est celui de
l'**équipement sacrifié** — celui qui est détruit. Trois faits du client le verrouillent :

1. l'aide de la fenêtre dit que ce qui est placé dans les créneaux de matériaux est détruit ;
2. la confirmation est **au singulier** (« with this item »), alors que la trame ne porte **qu'un**
   handle (11 octets, §3) : la trame ne peut pas nommer plus d'un objet, la confirmation non plus ;
3. le refus nommé `rzlabs_msg_ethereal_durability` désigne l'objet par son **nom** et parle de
   « sacrifice » (`#@item_name@#`), donc d'un objet précis que le serveur doit pouvoir nommer —
   la garde est donc côté serveur, sur le handle reçu.

### 2.2 L'émission dans le client — preuve par désassemblage (ajout de cette fiche)

Le socle avait relevé le nom du paquet dans le client mais **pas** son émetteur (socle, §2.2 : « le
client donne le *nom* du paquet … mais pas la fonction »). Le désassemblage complet de `SFrame.exe`
(`objdump -d`, méthode §2.4) donne l'émetteur, et il est unique :

| Fait mesuré | Adresse | Détail |
| --- | --- | --- |
| Constructeur de la trame 263 | `SFrame.exe 0x5FB160-0x5FB1B1` | zéroïe l'en-tête, écrit `id = 0x107` (16 bits, `0x5FB189`) puis `length = 0xB` (`0x5FB194`) et recalcule la somme de contrôle |
| **Unique** occurrence de l'id 263 comme id de trame | `0x5FB189` | recherche de l'immédiat `0x107` dans tout le `.text` : aucun autre constructeur |
| **Unique** appelant du constructeur | `0x5FDFCB` | `call 0x5FB160` |
| Écriture du handle dans la trame | `0x5FDFD0` / `0x5FDFE0` | `edx = [esi+0x5D8]` puis `[ebp-0x29] = edx` — soit **l'offset 7** de la trame |
| Émission | `0x5FDFE3` | `call 0x649AD0` → `0x6498E0`, qui journalise `Send : %s [%d]` (format à `0xA4D700`) puis remet le tampon à la connexion |
| Fenêtre de rattachement | handler de `SUIRepairWnd`, fonction `0x5FD840-0x5FE1A1` | la RTTI de la classe est `sframe.dump:44328` |
| Cas de dispatch qui émet | clé de message d'interface **`0x4D0` (1232)** → `0x5FDE69` | table de sauts `0x5FDB93`-`0x5FE214` (codes `0x425`…`0x4D4`) |
| Garde de mode | `0x5FDE6D-0x5FDE76` | `[wnd+0x4A0] == 2` est **nécessaire** : `jne 0x5FDFFB` sinon |
| Source du handle | `0x5FDE7C` et `0x5FDFD0` | `[wnd+0x5D8]` = le handle de l'objet que la fenêtre tient (résolu par `0x4A4C40` sur le gestionnaire `[wnd+0x484]`) |
| Branches qui atteignent l'émission | `0x5FDE9A`, `0x5FDEC0`, `0x5FDECA` → `0x5FDFC8` | objet non résolu ; **ou** champ `+0xC` de la fiche de ressource = `13` (`0xD`) ; **ou** octet `+0x28` de l'objet nul |

Ce que cela établit, et qui n'était pas établi :

- 263 est bien **émis par le client**, par le chemin réseau normal (`Send : …`), et depuis la fenêtre
  de la durabilité éthérée — pas depuis un émetteur générique ;
- le handle émis est celui du créneau cible de la fenêtre (`+0x5D8`), c'est-à-dire **l'objet que le
  joueur a désigné**, ce qui recoupe la lecture §2.1 (l'équipement sacrifié) ;
- l'émission n'est **conditionnée à aucune validation locale positive** : elle est précisément
  atteinte quand l'objet n'est pas résolu, ou que sa fiche de ressource a `+0xC = 0xD`, ou que son
  octet `+0x28` est nul. Autrement dit, **le client peut émettre un 263 pour un objet qu'il n'a pas
  su qualifier** : la validation est à la charge du serveur (conséquence §5.2).

### 2.3 Le second chemin de la même fenêtre : 256 `TM_CS_MIX` (ajout de cette fiche)

La branche **complémentaire** des trois gardes ci-dessus (objet résolu, `+0xC ≠ 0xD`, `+0x28 ≠ 0`)
n'émet pas 263 : elle construit un **message d'interface** `0x415` (1045) — constructeur `0x5F87F0`,
remplissage `0x5F8860`, envoi au gestionnaire `[wnd+0x440]` par `0x6491C0` (`0x5FDF98`) — dont la
charge utile est une liste d'entrées `(handle u32, mot u16)` de 6 octets. Ce message est dispatché par
la table globale des messages d'interface (`0x49E21D`, table d'octets `0x49EA50`, table de sauts
`0x49E98C`) : `key 1045 (0x415)` → stub `0x49E2C0` → **`0x48E300`**, un émetteur de trame qui :

- appelle le constructeur `0x48C8D0`, qui écrit `id = 0x100` (`0x48C8FC`) et `length = 0xF` (`0x48C907`) ;
- recopie `main_item = ([msg+0x13], [msg+0x17])` puis, en boucle, `count = ([msg+0x1D]-[msg+0x19])/6`
  entrées de 6 octets, et **patche la taille en place** (`0x48E34C` : `len += 6*count`) ;
- émet donc `TM_CS_MIX` (**256**), de forme **15 + 6N**, en 7.3 (`rzu TS_CS_MIX.h:5-21`, gating
  `X(256, version < EPIC_9_6_3)`).

Conséquence, et c'est un apport au débat du socle §6.4 : **en 7.3, le même geste de fenêtre passe par
deux routes distinctes**, l'une dédiée (263, un seul handle) et l'autre par la com­binaison (256, la
liste `Main Slot + matériaux`). L'hypothèse (A) du socle (« 263/264 = voie dédiée, `mix_type`
801/802/803 = voie distincte passant par 256 ») cesse d'être une simple hypothèse côté client : les
**deux émetteurs de trame coexistent dans `SFrame.exe`**, et la route choisie dépend de la fiche de
ressource de l'objet tenu par la fenêtre. Le partage exact entre les deux routes reste à trancher par
un relevé, pas par lecture (§7, NON ÉTABLI 1 et 2).

### 2.4 Méthode de lecture du client (reproductible)

```bash
cd /srv/navislamia/reference/client73
sha256sum SFrame.exe                    # 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e
strings -a SFrame.exe > /tmp/sframe.dump        # md5 7d92f691a46e6768d4cf00e85747e776, 63 099 entrées
objdump -d SFrame.exe > /tmp/sframe.asm         # désassemblage complet, VA = adresses ci-dessus
objdump -h SFrame.exe                           # table des sections (VA → offset fichier), pour /tmp/va.py
# db_string.rdb est déchiffré hors client (clé XOR corrigée, CLAUDE.md:1001-1010) :
#   /tmp/db_string.dump, 255 814 lignes — les numéros cités sont ceux de ce dump
```

Aucun binaire ni script du client n'a été exécuté : tout est lecture statique (`strings`, `objdump`,
lecture d'octets aux adresses virtuelles). Les numéros de ligne `db_string.dump` cités sont ceux du
dump présent sur ce poste, reproductible par la même clé.

---

## 3. Structure sur le fil — **11 octets, fixe**

rzu `TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h:5-6` et `:8` (`…reference/rzu`, commit épinglé §8) :

| Offset | Type | Champ | Source |
| --- | --- | --- | --- |
| 0 | `int32` LE | `length` = **11** | protocole commun ; `TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h:5-6` (champs seuls) |
| 4 | `uint16` LE | `id` = **263** (`0x107`) | `op_codes.md:91` ; client `0x5FB189` |
| 6 | `uint8` | checksum | protocole commun |
| **7** | **`uint32` LE** | **`handle`** | rzu `…_DURABILITY.h:6` `_(simple)(ar_handle_t, handle)` |
| 11 | — | *fin* | **taille totale 11 octets** |

**Taille totale attendue : 11 octets.** Aucun champ conditionnel, aucune liste, aucune sentinelle
documentée : la garde de lecture est donc `length == 11` **exactement** — un refus strict, pas un
`remainingData >= 11` (à la différence de 23/25/27, `CLAUDE.md:52-56`).

**Offsets déjà testés** (le socle les a livrés, ils restent valides et suffisent pour ce lot) :
`Tests/Game/CraftingSoclePacketsTests.cs:281-306` — lecture du handle à l'offset 7 et refus des
trames de 0, 7, 10 et 12 octets ; `:32` épingle l'id 263 dans l'énumération. **Le lot dev n'a pas à
les réécrire**, seulement à ne pas les affaiblir ; il doit en revanche ajouter les tests de la
*conduite* (ce que le serveur fait du handle) que le socle n'avait pas à écrire.

---

## 4. Gating de version — décisions prises pour 7.3

| Gating rzu | Tran­ché pour 7.3 | Justification |
| --- | --- | --- |
| `X(263, version < EPIC_9_6_3)` / `X(1263, version >= EPIC_9_6_3)` (`…_DURABILITY.h:10-11`) | **263 retenu, 1263 non déclaré** | `EPIC_7_3 = 0x070300` et `EPIC_9_6_3 = 0x090603` (`PacketEpics.h:59`, `:96`), comparaison sur les 24 bits bas (`PacketEpics.h:8`) : `0x070300 < 0x090603` |
| `// Since EPIC_7_2` (`…_DURABILITY.h:8`) | **le paquet existe en 7.3** | `0x070200 <= 0x070300` ; corroboré par le client, qui estampille ses textes du geste `<7.2>` (`db_string.dump:189800`, `:189826`) |
| Champs conditionnels | **aucun** | la macro `_DEF` ne porte qu'un champ sans condition (`…_DURABILITY.h:5-6`) |

Le voisin 264 est, lui, le seul paquet de la famille avec un gating de champ : `target` (int8) n'arrive
qu'à `EPIC_8_1` (`…_TO_EQUIPMENT.h:7`, `:9`), donc **absent en 7.3** (11 octets, pas 12). C'est
`rzu` qui tranche, et ce n'est pas le propos de cette fiche ; il est rappelé ici parce que les deux
ids sont lus par le même bras de dispatch et que le socle a livré les deux lecteurs
(`GameActionPackets.cs:542-562` et `:571-586`).

---

## 5. Traitement attendu

### 5.1 Ce que NGemity (`Chihiro`) porte réellement

| Fait | Source |
| --- | --- |
| Le paquet est **déclaré** au bon id | `shared/Server/Packets/GameClient/TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h:10` `CREATE_PACKET(…, 263)` |
| La trame NGemity porte le même champ, en `uint32_t` | `:6-7` `_(simple)(uint32_t, handle)` |
| **Aucun gestionnaire, aucun appelant** | recherche sur `Chihiro/` et `shared/` : seuls les en-têtes, l'énumération (`shared/Server/ClientPackets.h`, `XPacket.h`) et le champ `ethereal_durability` du modèle |
| Les trois opérations existent chez NGemity comme **types de combinaison**, pas comme paquets | `Chihiro/src/Crafting/MixManager.h:44-46` `MIX_SACRIFICE_ITEM_FOR_ETHEREAL_DURABILITY = 801`, `MIX_TRANSMIT_ETHEREAL_DURABILITY = 802`, `MIX_RECOVER_EXHAUSTED_ETHEREAL_DURABILITY = 803` |
| Deux prédicats de table portent la donnée | `MixManager.h:97-98` `CHECK_ITEM_ETHEREAL_DURABILITY_E = 16`, `…_NE = 17` |
| Le champ d'objet existe des deux côtés | NGemity `shared/Server/Packets/GameClient/TS_SC_INVENTORY.h:18` ; `Chihiro/src/Entities/Unit/Unit.cpp` |

**NGemity ne fournit aucune politique de durabilité éthérée.** Il n'y a donc **rien à porter** : tout
ce qui n'est pas la trame est une décision de jeu (§5.4).

### 5.2 Ce que le serveur doit faire du paquet, et ce qu'il doit répondre

**Acquis du socle** (à ne pas refaire) : lecture bornée (`length == 11`), résolution du handle par
`CraftingSocleRules.ReferencedHandles` avec le zéro traité en sentinelle et non en objet
(`CraftingSocleRules.cs:70-76`), et **refus systématique** avec `ResultCode.InvalidArgument`
(`CraftingSocleService.cs:88-97`), conformément à la décision d'étape 1 du socle (§9.2).

**Ce que cette fiche ajoute, et qui commande la conduite du lot dev :**

1. **Le serveur doit valider lui-même.** Le client n'émet pas 263 seulement pour un objet validé : il
   l'émet aussi quand il n'a pas su qualifier l'objet (§2.2). Recevoir un 263 ne prouve donc ni que le
   handle existe, ni qu'il appartient au joueur, ni qu'il est un équipement, ni qu'il est intact. La
   garde nommée par le client lui-même (`rzlabs_msg_ethereal_durability`, « *cannot be sacrificed or has
   no durability to sacrifice!* », `db_string.dump:1015-1016`) est **une garde serveur** : c'est elle
   qui doit refuser, en nommant l'objet.
2. **Le handle désigne l'objet consommé**, pas la pierre ni la cible (§2.1, §2.2). Le serveur résout
   ce handle dans l'inventaire du joueur, il ne le réinterprète pas comme un second slot.
3. **Un seul objet par trame.** Si le joueur a plusieurs objets dans les créneaux de matériaux, le
   serveur ne peut pas les déduire de cette trame : il n'y a qu'un handle. Toute politique
   multi-objets passe par la route 256 (§2.3) ou par des trames successives — c'est une décision de
   jeu, pas une donnée de la trame (§7 NON ÉTABLI 3).
4. **Ce que le serveur doit répondre** :
   - **refus** : `TM_SC_RESULT` (id **0**), `request_msg_id = 263`, `result = <code>`, `value = 0` —
     forme rzu `TS_SC_RESULT.h:7-10` (`request_msg_id`, `result`, `value`), gating
     `X(0, version < EPIC_9_6_3)` (`:13`) donc **0 en 7.3**. Le dépôt sait déjà construire ce refus
     (`GameClient.SendResult(ushort id, ushort result, int value = 0)`, `GameClient.cs:68`) ; c'est ce
     que fait l'étape 1 du socle. **Ne pas inventer de code de résultat** : réutiliser un `ResultCode`
     existant (la table `Game/Network/Packets/Enums/ResultCode.cs`), et si aucun ne convient, c'est un
     arbitrage (§A VERIFIER).
   - **succès** : **il n'existe aucun paquet descendant dédié à la charge**. rzu ne porte pas de
     contrepartie SC de 263/264 : la recherche `ethereal` dans `librzu/src/packets/` ne rend que les
     deux trames CS et le champ `ethereal_durability` des structures d'objet
     (`TS_SC_INVENTORY.h:63`, `:75`, `:84`, `:98-101`). Le client apprend donc le résultat des
     **primitives déjà présentes** :
     - la valeur de la pierre, par la **propriété** `ethereal_stone` : le client connaît ce nom
       (`sframe.dump:26431`), le dépôt le sérialise (`GameActions.cs:275`,
       `GameStatPackets.BuildProperty`, `TM_SC_PROPERTY = 507`) — **mais uniquement à l'entrée en
       monde**. Après une charge réussie, l'émetteur doit donc renvoyer cette propriété avec la
       nouvelle valeur ;
     - la durabilité éthérée **de l'objet**, par le motif d'objet de 75 octets
       (`ItemFixedInfoWriter.cs:71`, champ à l'offset 24, `:89`) que rzu autorise en 7.3
       (`TS_SC_INVENTORY.h:75`, `version >= EPIC_6_3 && version < EPIC_9_8_1`) — et dont le dépôt a
       mesuré la forme 7.3 (75 octets, `CLAUDE.md:1247-1250` : **ne pas reconstruire les 71/81 octets
       de rzu**) ;
     - la **consommation** de l'équipement, par `TM_SC_DESTROY_ITEM` (254) et/ou
       `TM_SC_UPDATE_ITEM_COUNT` (255), déjà déclarés (`GamePackets.cs:47-48`).
   - Les textes « *You have charged the Ethereal Stone with …* » (`db_string.dump:151304`) et
     « *Recharge complete!/failed!* » (`db_string.dump:189828`, `:189830`) sont des chaînes **du
     client** : elles sont affichées par la fenêtre, pas envoyées par le serveur. Aucune primitive du
     dépôt n'envoie de clé `db_string` : `TM_SC_RESULT` porte un code, pas un texte.

### 5.3 État du dépôt à `b56967a` (ce qui est déjà là, ce qui manque)

| Élément | Ligne | État |
| --- | --- | --- |
| Id dans l'énumération | `GamePackets.cs:61` | présent (= 263) |
| Dispatch : l'id ne peut pas atteindre le `switch` final | `GameClient.cs:1617-1630` | présent (bras à cinq ids) |
| Lecteur de la trame | `GameActionPackets.cs:535`, `:542-562` | présent (handle à l'offset 7, strict 11 octets) |
| Résolution du handle | `CraftingSocleRules.cs:70-76` | présent (zéro = sentinelle) |
| Refus | `CraftingSocleService.cs:88-97` | présent (`InvalidArgument`) |
| Primitive pierre | `CharacterEntity.cs:53` `EtherealStoneDurability` (migration `…Version0001_TheBeginning.cs:94`) | présent |
| Sérialisation pierre | `GameActions.cs:275` (`ethereal_stone`) | présent, **entrée en monde seulement** |
| Primitive objet | `ItemEntity.cs:30` `EtherealDurability` ; `ItemResourceEntity.cs:36` ; `ArcadiaSchemaPSQL.sql:2219` ; `MSSQLItemResource.cs:40` | présent |
| Sérialisation objet | `ItemFixedInfoWriter.cs:20`, `:47`, `:71`, `:89` | présent |
| Destructions | `TM_SC_DESTROY_ITEM` 254, `TM_SC_UPDATE_ITEM_COUNT` 255 (`GamePackets.cs:47-48`) | présents |
| **Politique** (montant, plafond, consommation, destination, code de refus) | — | **absent : c'est le lot** |

Base mesurée sur cette branche, avant rédaction : `dotnet build Navislamia.sln -c Debug` → **code de
sortie 0** ; `dotnet test Tests/Tests.csproj` → **code de sortie 0**, `Failed: 0, Passed: 1302,
Skipped: 0`. (Les tests sont en NUnit : compter les `[Fact]` rendrait 0.)

### 5.4 Ce que le lot dev doit écrire, et ce qu'il ne doit pas décider seul

Le lot est **implémentable** : toutes les primitives existent (§5.3) et NGemity n'offre de toute façon
aucune logique à recopier (§5.1). Ce qui manque est une **décision de jeu**, qui se porte en
`## A VERIFIER PAR KILLIAN` sans parquer la carte (règle de la maison : l'infaisabilité se juge sur les
primitives absentes du dépôt, jamais sur un arbitrage métier en attente — même arbitrage que 57, 9005,
258, 259, 260, 262).

Ce que le lot peut livrer sans arbitrage : la validation complète du handle (existence, propriété,
nature, intégrité), le refus nommé, et les tests de cette validation. Ce qu'il ne doit **pas** inventer :
le montant rechargé, le plafond appliqué, l'objet consommé, et le code de résultat exact.

---

## 6. Écarts assumés avec NGemity, et pourquoi

| # | Écart | Pourquoi |
| --- | --- | --- |
| 1 | **Rien n'est porté du code de NGemity pour 263** | NGemity déclare la trame et n'a aucun gestionnaire (§5.1). Il n'y a pas de « plus récent » à recopier : la source de vérité de la trame est rzu, la conduite est une décision de jeu. |
| 2 | **`EPIC = EPIC_4_1_1`** (`shared/Common/Define.h:25`) : NGemity est **sous** 7.2 | Il ne peut pas valider un paquet apparu en `EPIC_7_2`. Sur ce lobe, NGemity n'est pas une référence plus ancienne, c'est une référence **muette**. |
| 3 | **Ne pas modéliser la charge comme un `mix_type`** (801/802/803) tant que le partage n'est pas tranché | Le client 7.3 porte **les deux routes** (§2.3). Modéliser la charge uniquement en combinaison, ou uniquement en paquet dédié, serait choisir à la place du client — le partage est en `NON ÉTABLI` 2. |
| 4 | **Refuser, ne pas déconnecter** | Même écart que le socle (§7 écart 1) : une trame mal bornée se refuse (`TM_SC_RESULT` + log), elle ne coupe pas la session. |
| 5 | **Ne pas recopier les codes `CHECK_*` inertes** (`MixManager.h:97-98`, déclarés et jamais atteints) | Si la voie 256 doit un jour appliquer `CHECK_ITEM_ETHEREAL_DURABILITY_E/NE`, ces prédicats devront être **implémentés** ou la ligne de ressource refusée explicitement — jamais ignorés en silence. |

---

## 7. NON ÉTABLI

1. **Quelle branche de la fenêtre correspond au bouton « Charge Stone ».** Le désassemblage est
   formel sur les adresses : l'émission de 263 est atteinte par les trois gardes « objet non résolu /
   `ressource+0xC = 0xD` / `objet+0x28 = 0` » (`0x5FDE9A`, `0x5FDEC0`, `0x5FDECA`), et la branche
   complémentaire part par 256 (`§2.3`). La lecture ne dit pas laquelle des deux est *le* geste
   « Charge Stone » : elle dit qu'elles coexistent. **À trancher par un relevé en jeu** (ou par
   Killian) — et c'est ce qui décide si le lobe 263 doit être servi par le même bouton que la voie 256.
2. **Le nom des états `[wnd+0x4A0]`.** La table `0xC1AF78` (`A`) indexée par ce champ vaut
   `{ 0, 9738, 9734, 9742 }` et la table `0xC1AF88` (`B`) `{ 0, 9739, 9735, 9743 }`, soit les textes
   `Restoration complete/failed!` (9738/9739), `Repair complete/failed!` (9734/9735),
   `Recharge complete/failed!` (9742/9743). L'état `2` — celui qui émet 263 — porte donc le texte
   `Repair complete!`, alors que les libellés d'onglet du client sont `Repair`, `Restore`,
   `Charge Stone` (`db_string.dump:189794-189800`). **L'appariement libellé ↔ état n'est pas tranché**
   par lecture : ne pas en déduire qu'un bouton nommé « Charge Stone » émet 263, ni l'inverse.
3. **Le sens du champ de ressource à l'offset `+0xC` (comparé à `13`, `0xD`).** L'accesseur est
   `0x4A6610` (`return [resource+0xC]`) ; l'offset correspond, dans l'ordre des colonnes de
   `MSSQLItemResource` (`id, name_id, tooltip_id, type, group, class, wear_type, …`), à `type` — mais
   `13` n'existe pas dans l'énumération du dépôt (`ItemType`: `Etc = 0`, puis `95`/`96`/`98`…,
   `EtherealStone = 451`). **Hypothèse, non établie.**
4. **Le multi-objet.** L'aide du client parle de « Materials Slots 1-9 » (`db_string.dump:189878`)
   alors que la trame ne porte qu'un handle (§3) et que la confirmation est au singulier
   (`db_string.dump:222405`). Le relevé qui trancherait : combien de trames 263 le client émet-il
   quand plusieurs objets sont placés, et un 263 par objet ou un 256 global ? Non établi.
5. **Le montant rechargé, le plafond, l'objet consommé.** `db_string.dump:137571` dit « *increases in
   proportion to the item's store price* » et nomme un plafond de **1000** (pierre) / **10000** (grande
   pierre) (`:137583`) ; la formule exacte, l'arrondi, ce qui est consommé (l'équipement seul ? le
   « Ethereal Charge Stone » aussi ?), et ce qui arrive si la pierre est déjà à son plafond ne sont
   **pas** décidables par lecture. Décision de jeu.
6. **Le code de résultat du refus.** Le socle envoie `InvalidArgument` ; le client porte des textes
   spécifiques (`db_string.dump:1015-1016`, `:189826`, `:189830`) mais aucun relevé ne dit quel code
   les déclenche. Ne pas inventer (§5.2).
7. **`rate` de 264** — hors périmètre de cette fiche, mais à ne pas confondre : le voisin porte un
   `float` dont l'unité est inconnue (socle, NON ÉTABLI 7), et le client 7.3 écrit `1.0` dans le cas
   nominal (constructeur `0x5EAE5B-0x5EAE9F`, `fld1` puis offset 7). Ce n'est pas une information sur
   263 et cela ne doit pas servir à déduire le montant de la charge.
8. **Fenêtre de collision de branches.** Mesurée à ce réveil, avec les deux voisines non mergées :
   `git diff --stat master...hermes/packet-260-soulstone-craft` → 20 fichiers, dont
   `Game/Services/CraftingSocleRules.cs`, `Game/Services/CraftingSocleService.cs`,
   `Game/Network/Clients/GameClient.cs`, `Tests/Game/CraftingSoclePacketsTests.cs` (les quatre fichiers
   que le lot 263 touche aussi) ; `git diff --stat master...hermes/packet-262-repair-soulstone` →
   **2 fichiers, tous deux nouveaux** (`Tests/Game/RepairSoulstonePacketsTests.cs`,
   `docs/packet-specs/262-repair-soulstone.md`) ; `git diff --stat master...<branche> --
   Game/Network/Packets/Game/GameActionPackets.cs` → **vide pour les deux**. Donc **`GameActionPackets.cs`
   est libre**, et la zone chaude est `CraftingSocleRules.cs` + `CraftingSocleService.cs` +
   `Tests/Game/CraftingSoclePacketsTests.cs` : le lot 263 doit être rebasé après le merge de 260, ou
   livré après lui.

---

## 8. Commits épinglés et méthode de reproduction

| Dépôt | Commit | Usage |
| --- | --- | --- |
| `reference/rzu` | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` | trame 263 (§3), gating (§4), `TS_SC_INVENTORY` (§5.2), `TS_SC_RESULT` (§5.2) |
| `reference/ngemity` | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` | `MixManager.h:44-46`, `:97-98` (§5.1) ; déclaration sans gestionnaire (§5.1) |
| `Navislamia` (base de cette branche) | `b56967a07430422add88e0e5cdf292b41b18f6c6` | état du dépôt (§5.3), collision (§7.8) |
| client de référence | `SFrame.exe` sha256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` ; `db_string.rdb` déchiffré | geste (§2), textes (§2.1) |

Fichiers de référence internes au poste (non versionnés, reproductibles §2.4) : `/tmp/sframe.dump`
(md5 `7d92f691a46e6768d4cf00e85747e776`), `/tmp/sframe.asm`, `/tmp/db_string.dump` (255 814 lignes).
Les numéros de ligne `db_string.dump` cités sont ceux de **ce** dump ; un dump produit avec une autre
clé ou une autre version du fichier peut décaler les lignes — les textes cités, eux, ne dépendent pas
du décalage.

---

## A VERIFIER PAR KILLIAN

1. **Le geste et la route** (§7.1, §7.2) : le bouton « Charge Stone » émet-il 263 (un équipement
   sacrifié, un handle) ou 256 `TM_CS_MIX` (Ethereal Stone en Main Slot + consommables) ? Les deux
   émetteurs existent dans le client de 7.3 ; la lecture ne tranche pas. Ce que cela change :
   si c'est 256, l'essentiel du geste est déjà couvert par la fiche du lobe A (`256-use-item`/socle) et
   263 reste la voie dédiée d'extraction.
2. **La politique de la charge** (§7.5) : montant rechargé (proportion au prix de l'objet — quelle
   formule, quel arrondi), plafond appliqué (1000 / 10000), objet(s) consommé(s) — l'équipement seul,
   ou aussi l'« Ethereal Charge Stone » —, et comportement quand la pierre est au plafond.
3. **Le code de résultat** du refus (§7.6) : `InvalidArgument` suffit-il, ou faut-il un code propre à
   « objet non sacrifiable » ?
4. **Le champ `+0xC = 0xD`** (§7.3) : confirmer contre `db_item.rdb` s'il s'agit du `type` d'objet, et
   ce que vaut 13 dans cette table.
5. **Priorité d'atterrissage** (§7.8) : le lot 263 doit passer après le merge de
   `hermes/packet-260-soulstone-craft` (mêmes fichiers). Le PO décide.

---

## Bloc prêt à coller dans `CLAUDE.md` (par `navis-qa`, dans la description de la MR)

### Paquet 263 — `TM_CS_TRANSMIT_ETHEREAL_DURABILITY` (charge de la pierre éthérée)

Trame **fixe de 11 octets** : `length` (4), `id` = 263 (2), checksum (1), `handle` `uint32` LE à
l'**offset 7**. Refus strict de toute autre taille (`length == 11` exactement). Id remappé à 1263 à
partir d'`EPIC_9_6_3` : **non déclaré en 7.3**. Aucun champ conditionnel. `EPIC_7_2` est le plancher du
paquet (`TS_CS_TRANSMIT_ETHEREAL_DURABILITY.h:8`), corroboré par le client qui estampille `<7.2>` ses
textes de charge (`db_string.dump:189800`, `:189826`).

Le handle désigne **l'équipement sacrifié** (détruit), pas la pierre : l'aide de la fenêtre le dit
(`db_string.dump:189878`, `:137609`), la confirmation au singulier le dit (`:222405`), et le
désassemblage de `SFrame.exe` le confirme — la seule émission de 263 dans tout le binaire est
`0x5FDFE3`, depuis le handler de `SUIRepairWnd` (`0x5FD840`), avec `[wnd+0x5D8]` écrit à l'offset 7.
**Le client n'émet pas que pour un objet validé** : cette émission est atteinte par les branches
« objet non résolu » (`0x5FDE9A`), « `ressource+0xC = 0xD` » (`0x5FDEC0`) et « `objet+0x28 = 0` »
(`0x5FDECA`). La validation est donc **entièrement au serveur** — le client porte lui-même le texte du
refus serveur (`rzlabs_msg_ethereal_durability`, `db_string.dump:1015-1016`, « *cannot be sacrificed
or has no durability to sacrifice!* »).

**La même fenêtre possède une seconde route** : la branche complémentaire poste un message d'interface
`0x415`, dispatché (`0x49EA50` / `0x49E98C`) vers `0x48E300`, qui émet **256 `TM_CS_MIX`** (15 + 6N,
constructeur `0x48C8D0`). Les deux routes coexistent en 7.3 : ne pas décider pour le client.

NGemity déclare 263 (`CREATE_PACKET(…, 263)`) et **n'a aucun gestionnaire** ; ses trois opérations
éthérées y sont des `MIX_TYPE` 801/802/803 (`MixManager.h:44-46`) jamais atteints. Il n'y a donc rien à
porter : le montant, le plafond (1000/10000, `db_string.dump:137571`, `:137583`), l'objet consommé et le
code de refus sont des **décisions de jeu**. Côté dépôt, tout est déjà là sauf la politique :
`CharacterEntity.EtherealStoneDurability` (sérialisée en propriété `ethereal_stone`,
`GameActions.cs:275` — **entrée en monde seulement**), `ItemEntity.EtherealDurability` (motif de 75
octets, offset 24, `ItemFixedInfoWriter.cs:89` ; ne pas reconstruire les 71/81 octets de rzu),
`TM_SC_DESTROY_ITEM` (254), `TM_SC_UPDATE_ITEM_COUNT` (255) et le refus par `TM_SC_RESULT` (0).

**Aucun paquet descendant n'est dédié à la charge** : le résultat passe par ces primitives, pas par un
paquet 263 descendant. Voir `docs/packet-specs/263-transmit-ethereal-durability.md` et
`docs/packet-specs/socle-artisanat-objets.md` §2.3, §2.4, §3.5, §6.3-6.4, §9.3.5.
