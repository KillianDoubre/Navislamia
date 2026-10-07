# 6006 / 6007 — `TM_CS_NURSE_CREATURE` et `TM_SC_RESULT_NURSE`

Suivi : `navislamia:packet:6006` ; carte Trello **`2ILR2tbk`** (`https://trello.com/c/2ILR2tbk`) — shortLink
repris du corps de la tâche Hermes, non lu via Trello (ce rôle n'a pas d'accès MCP Trello). Fiche sœur du
socle `docs/packet-specs/socle-ferme-creatures-officielle.md` (« La ferme de créatures », §3.4 et §5.4) et
de `docs/packet-specs/socle-ferme-creatures.md` (les trames, §3.5).

Méthode : le serveur officiel (arbre 2015, recoupé sur le binaire Epic 7.3 `2012-11`) tranche la valeur ;
rzu tranche la taille, l'ordre des champs et le gating ; le client Epic 7.3 tranche en dernier ressort ;
NGemity ne dit rien (il n'implémente aucune ligne de la ferme). `SFrame.exe` (client 7.3) et
`CaptainHerlockServer.exe` (serveur 7.3) ont été **désassemblés localement** (`objdump -d`, lecture seule),
et les `.rdb`/`.csv` lus comme fichiers de données. **Aucun code serveur n'est écrit ici, aucun binaire,
Lua ni script du client n'a été exécuté.** Aucun dépôt git ne couvre `reference/official2015`,
`reference/epic7part4` ni `reference/client73` : ces sources se citent **fichier + ligne** (et SHA-256 pour
les binaires, §8).

---

## 1. Identité

| Élément | Valeur | Source |
|---|---|---|
| Id | **6006** (`0x1776`) | `op_codes.md:262` — `[6006] = "TM_CS_NURSE_CREATURE"` |
| Nom | `TM_CS_NURSE_CREATURE` | `op_codes.md:262` |
| Nom rzu / NGemity | `TS_CS_NURSE_CREATURE` | rzu `…/TS_CS_NURSE_CREATURE.h:11-12` ; NGemity `shared/Server/Packets/GameClient/TS_CS_NURSE_CREATURE.h:10` |
| Sens | **client → serveur** | rzu `TS_CS_NURSE_CREATURE.h:14` (`SessionPacketOrigin::Client`) ; serveur officiel `GameMessage.h:3940-3944` |
| Taille totale | **11 octets** (7 d'en-tête + 4) | rzu `PacketDeclaration.h:616-621` + `GameTypes.h:40` ; client `0x6105b4` ; serveur 7.3 : poignée lue en `+7` (`0x140126ce1`) |
| Id | **6007** (`0x1777`) | `op_codes.md:263` — `[6007] = "TM_SC_RESULT_NURSE"` |
| Nom | `TM_SC_RESULT_NURSE` | `op_codes.md:263` |
| Nom rzu / NGemity | `TS_SC_RESULT_NURSE` | rzu `…/TS_SC_RESULT_NURSE.h:11-12` ; NGemity `…/TS_SC_RESULT_NURSE.h:10` |
| Sens | **serveur → client** | rzu `TS_SC_RESULT_NURSE.h:14` (`SessionPacketOrigin::Server`) |
| Taille totale | **8 octets** (7 d'en-tête + 1) | serveur 7.3 : `movl $0x8` en `0x140126cd9` ; socle §3.4 ; client : `result` lu en `+7` (`0x6722be`) |
| En-tête | 7 octets : `Length` u32 (`+0`), `ID` u16 (`+4`), `Checksum` u8 (`+6`), charge utile à `+7` | `Game/Network/Packets/Header.cs:9-11`, `:20-25` ; `PacketExtensions.cs:13-25` ; rzu `PacketDeclaration.h:617-621` (taille d'en-tête `7`) |
| État dans Navislamia (`master` `a9f01fb`) | **6006** déclaré et lu, jamais répondu ; **6007 non déclaré** | `GamePackets.cs:451` ; `GameFarmPackets.cs:181` ; `GameClient.cs:1152-1171`, `:3731-3733` ; `grep -n "TM_SC_RESULT_NURSE" Game/` → aucun résultat |

C'est le paquet « **prendre soin** » de la ferme de créatures : le joueur flatte une créature déposée
et, une fois par jour, la créature lui offre (ou non) un cadeau. Le serveur répond **toujours** par 6007,
dont l'unique octet `result` dit si la demande était invalide, si le cadeau n'est pas tombé, ou s'il est
tombé.

---

## 2. Ce que le joueur fait

1. Il parle au PNJ de la ferme (son nom de PNJ est `@91000349`, `NPC_Creature_farm.lua:14-17`) et ouvre la
   fenêtre — menu `@91000353` → `show_creature_farm_window()` (`NPC_Creature_farm.lua:67`). La fenêtre est
   `SUICreatureFarmWnd` (nom de classe RTTI dans `SFrame.exe`), ressource `window_creature_farm.nui`.
2. La fenêtre affiche **trois emplacements** de ferme. Chacun porte un bouton « soin » :
   `button_ministration_01`, `02`, `03` (chaînes de `SFrame.exe` ; poignées d'interface
   `0xa3feb8` / `0xa3fea0` / `0xa3fe88`).
3. Au clic, le client renseigne l'index de l'emplacement (0, 1, 2) dans la variable globale
   `0xc1b4c8` — `movl $0x0,0xc1b4c8` (`0x615929`), `$0x1` (`0x61599a`), `$0x2` (`0x615a0b`) —, lit la
   **poignée de la carte** dans l'emplacement (`window+0x610` pour l'emplacement 0 en `0x615933`,
   `window+0x710` en `0x6159a4`, `window+0x810` en `0x615a15`), puis appelle `0x610a40` (`0x61593c`,
   `0x6159ad`, `0x615a1e`) qui construit une trame de 11 octets d'id 6006 et l'envoie.
4. Le client **ne construit rien** si son propre contrôle d'emplacement échoue : avant l'envoi, il exige
   des champs d'emplacement non nuls (`0x564`/`0x5cc` pour l'emplacement 0, `0x664`/`0x6cc` pour 1,
   `0x764`/`0x7cc` pour 2) et deux comparaisons de valeurs de temps (`0x5ec ≥ 0x5f4` et `0x5f8 ≤ 0`
   pour 0, mêmes offsets `+0x100`/`+0x200` pour 1 et 2) — `0x6158f0-0x615923`, `0x615958-0x615994`,
   `0x6159c9-0x615a05`. La sémantique exacte de ces deux derniers champs n'est pas établie (§7.6) :
   ils se comportent comme un feu vert de disponibilité, pas comme la poignée.
5. Le client attend 6007 et réagit (§3.2). Un emplacement sans carte ne produit donc jamais de 6006.

---

## 3. Structure sur le fil

### 3.1 `TM_CS_NURSE_CREATURE` (6006) — client → serveur, **11 octets**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0 | `u32` LE | `Length` | **11** (`0x0b`) | client `0x6105b4` (`movl $0xb,(%eax)`) ; 7 octets + 4, cf. `Header.cs:9` |
| 4 | `u16` LE | `ID` | **6006** (`0x1776`) | client `0x6105a9`/`0x6105ae` ; serveur `GameMessage.h:362` ; rzu `TS_CS_NURSE_CREATURE.h:12` |
| 6 | `u8` | `Checksum` | somme des octets `0..5` modulo 256 | client `0x610594-0x61059b` puis `0x6105c0-0x6105c7` (boucle d'addition) ; `PacketExtensions.cs:13-25` ; `Packet.cs:57` |
| 7 | `u32` LE | `creature_card_handle` | poignée de la carte déposée dans l'emplacement pressé (client : `window+0x610`/`+0x710`/`+0x810`) | rzu `TS_CS_NURSE_CREATURE.h:8` (`ar_handle_t` = `uint32_t`, `GameTypes.h:40`) ; client `0x610a6d` (`mov %edi,-0x5(%rbp)` = base+7) ; serveur `GameMessage.h:3940-3944` ; serveur 7.3 lit en `+7` (`0x140126ce1`) |

**Taille totale attendue : 11 octets.** Le client la refuse autrement : `TryReadNurseCreature`
(`GameFarmPackets.cs:181-206`) en fait la condition d'acceptation côté Navislamia (11 exactement), et le
lecteur officiel lit sans borne (`0x7(%rbx)`).

Le champ 7 n'est **pas** un identifiant d'objet ni un index d'emplacement : c'est une `AR_HANDLE` que le
serveur résout en objet d'inventaire (`StructItem::FindItem(hHandle)`, `StructPlayer.cpp:11425`) puis
compare à la carte du dépôt par **UID d'objet** (`GetItemUID()`, `StructPlayer.cpp:11437`). C'est la même
famille de poignée que 6004 (`creature_card_handle`) et que les `ticket_handle`/`cracker_handle` de 6002.

### 3.2 `TM_SC_RESULT_NURSE` (6007) — serveur → client, **8 octets**

| Offset | Type | Nom | Valeur observée | Source |
|---|---|---|---|---|
| 0 | `u32` LE | `Length` | **8** (`0x08`) | serveur 7.3 `0x140126cd9` (`movl $0x8,0x30(%rsp)`) ; socle §3.4 |
| 4 | `u16` LE | `ID` | **6007** (`0x1777`) | serveur 7.3 `0x140126cd2` (`movw $0x1777,0x34(%rsp)`) ; `GameMessage.h:363` ; rzu `TS_SC_RESULT_NURSE.h:12` |
| 6 | `u8` | `Checksum` | **0** dans le chemin mesuré | serveur 7.3 : les 8 octets sont mis à zéro (`0x140126ccb`), puis `Length`, `ID` et `result` sont écrits — `+6` n'est jamais écrit |
| 7 | `i8` | `result` | **0** = `FAILED`, **1** = `NO_REWARD`, **2** = `REWARDED` | officiel `GameMessage.h:3954-3959` ; serveur 7.3 `0x140126d7b-0x140126d80` et `0x140126d9b` ; client `0x6722be` (lecture) et `0x613c94-0x613d24` (usage, §3.3) |

**Taille totale attendue : 8 octets.** Le client lit l'octet en `+7` sans lire le reste
(`0x6722be`, `mov 0x7(%ecx),%dl`) : `result` est bien le seul champ.

Sur le `Checksum` : le client **calcule** la somme en sortie (§3.1) mais Navislamia la calcule aussi en
sortie (`Packet.cs:57`). Le constructeur du serveur 7.3 mesuré laisse `+6` à zéro et `PendMessage`
(`0x140126da8`) est appelé ensuite : rien n'établit si l'envoi la recalcule. Le client, lui, ne consulte
pas cet octet dans le chemin 6007. Aucune des deux valeurs n'est donc établie comme obligatoire (§7.5).

### 3.3 Ce que le client fait des trois valeurs de `result`

Le handler 6007 du client (`0x672280`, celui de `TM_SC_RESULT_NURSE`) range l'octet reçu à l'offset
`+0x13` d'un événement `USMSG_RESULT_NURSE` (nom de classe RTTI dans `SFrame.exe`) marqué du type `0xaf`
en `+4` (`0x672296`), puis le met en file (`0x6722ce`). Le répartiteur de la fenêtre (`SUICreatureFarmWnd`)
aiguille par table de saut (`0x614ac4`, index 2 = type `0xaf`) vers la branche `0x613c94`, qui :

* `result == 0` → **rien** : retour immédiat, aucun message, aucun effet (`0x613c94-0x613c98` → `0x614aae`) ;
* `result != 0` → l'emplacement en cours (celui de `0xc1b4c8`) est marqué comme soigné : le client écrit
  `1` à `+0x2a4` de l'objet d'emplacement et appelle sa méthode virtuelle en `+0x3c`
  (`0x613c9e-0x613cb7`), puis
  * `result == 1` → boîte de message de l'id de chaîne `0x56c8e3e` (`0x613cc0`) et rafraîchissement ;
  * `result == 2` → boîte de message de l'id de chaîne `0x56c8e3f` (`0x613d2a`) et rafraîchissement ;
  * toute autre valeur → rien (`0x613d22-0x613d24`).

Autrement dit le client **ne distingue que 0, 1 et 2**, et confirme par l'ordre du code la valeur
`FAILED = 0` : seule cette valeur produit un silence total, ce que le serveur officiel renvoie pour une
demande invalide ou hors délai. Les identifiants de chaîne `0x56c8e3e` (pas de cadeau) et `0x56c8e3f`
(cadeau) sont des ids chiffrés du client ; leur texte n'est pas décodable avec les références locales et
aucun outil du client ne peut être lancé (§7.4).

---

## 4. Gating de version — statué pour Epic 7.3

* rzu ne montre **aucun gating de champ** : `// Since EPIC_7_3` puis une **entrée unique** par paquet —
  `X(6006, true)` (`TS_CS_NURSE_CREATURE.h:10-12`) et `X(6007, true)` (`TS_SC_RESULT_NURSE.h:10-12`).
  Le second argument n'est consommé que comme condition d'identifiant (`SERIALISATION_F_ID2`,
  `PacketDeclaration.h:585-589`) ; avec une seule entrée il vaut inconditionnellement, donc `true` =
  « inconditionnel » : **aucun id de repli, aucune variante de charge utile**, en 7.3 comme après.
  Rapport au gating global : `EPIC_7_3 = 0x070300` (`PacketEpics.h:59`).
* Le **client 7.3** émet l'id 6006 (`0x1776`, `0x6105ae`) et consomme l'id 6007 (`0x1777`, aiguillage
  `0x672280`) : les deux ids sont **servis en 7.3**, le lot n'a donc aucune substitution à faire.
* Le **serveur 7.3** (binaire `2012-11`) lit la poignée en `+7` (`0x140126ce1`) et répond l'id `0x1777`
  avec une longueur de 8 (`0x140126cd2`, `0x140126cd9`).

**Décision : 6006 et 6007 sont ceux d'Epic 7.3, sans variante ni champ conditionnel.** Le seul point à
trancher par le code est la **déclaration** : `TM_SC_RESULT_NURSE = 6007` manque dans `GamePackets`
(`GamePackets.cs:442-452`), et le commentaire `:444-445` (« their `result` values are not established »)
doit être corrigé puisque cette fiche les établit (§5.4). Les deux autres trames de résultat, 6003 et 6005,
**ne font pas partie de ce lot** : elles appartiennent aux branches `hermes/packet-6002-foster-creature`
et `hermes/packet-6004-retrieve-creature` (MR ouvertes) ; les déclarer ici élargirait le lot sans raison.
Un membre de `GamePackets` n'est déclaré que s'il est envoyé ou reçu, et l'enum et l'aiguillage bougent
ensemble (critère transversal : aucun membre ne doit atteindre le `switch` final).

---

## 5. Traitement attendu

### 5.1 Le chemin officiel, pas à pas

`onNurseCreature` (officiel `GameMessage.cpp:11921-11941` ; serveur 7.3 `0x140126c30`) sous verrou de
personnage :

1. lire la poignée dans la trame reçue (`+7`) et appeler `NurseSummon(poignée)` (`StructPlayer.cpp:11423`,
   7.3 `0x1400d63a0`) : serveur 7.3 `mov 0x7(%rbx),%edx` en `0x140126ce1`, appel en `0x140126ce7` ;
2. `NurseSummon` rend `false` → `result = FAILED` (`0`) : officiel `:11937`, 7.3 `0x140126d9b` ;
3. `NurseSummon` rend `true` → exécuter le script Lua `NPC_Creature_Farm_nurse_handler()` (officiel
   `:11931` ; 7.3 `0x140126d1c`/`0x140126d23`, chaîne `"return NPC_Creature_Farm_nurse_handler()"` en
   `0x140412128`) et comparer sa valeur de retour à la chaîne `"1"` (7.3 : constante en `0x140412120`) :
   égale → `REWARDED` (`2`), sinon → `NO_REWARD` (`1`) : officiel `:11933`, 7.3 `0x140126d60-0x140126d80` ;
4. répondre **6007** (id `0x1777`, 8 octets) portant cet octet : officiel `PendMessage` `:11940`, 7.3
   `0x140126da8`.

### 5.2 Les conditions de `NurseSummon` (une par une)

| Condition | Officiel 2015 | Serveur 7.3 |
|---|---|---|
| La poignée doit résoudre un objet de l'inventaire | `FindItem(hHandle)` sinon `false` — `StructPlayer.cpp:11425-11426` | appel `0x1400d63c4`, retour `false` |
| La carte doit porter `ITEM_FLAG_FARMED_SUMMON` (**bit 27**) : « carte non déposée → refus » | `:11429-11430` (`ItemInstance.h:90`) | `btl $0x1b,0x15c(%rax)` en `0x1400d63d5` (test du bit 27 du champ de drapeaux de l'objet) |
| L'objet doit correspondre à une entrée de ferme, comparée par **UID d'objet** | `:11432-11437` (`GetItemUID()`) | boucle sur les 3 emplacements (`0x1400d63f2-0x1400d6420`), comparaison `0x100(%rdi)` / `0x100(%rax)` en `0x1400d6405` |
| **Fenêtre d'un soin par jour** : `refresh` = aujourd'hui 06:00 **heure locale du serveur** ; si l'horloge est encore avant, `refresh` = hier 06:00 (`tm_isdst = -1` puis `mktime`) | `:11439-11453` (`time(NULL)`, `localtime_s`) | `movl $0x6,0xf0(%rsp)` en `0x1400d6446` (heure 6), `tm_isdst = -1` en `0x1400d645f`, `mktime` puis décrément du jour en `0x1400d6472-0x1400d6496` |
| Refus si l'entrée a déjà été soignée depuis ce `refresh` (`nursing_time >= refresh`) | `:11455-11456` | `cmp %rax,0x28(%rbx)` en `0x1400d6496` → `false` |
| Sinon : écrire l'heure de soin en base, la mémoriser, journaliser, rendre `true` | `:11458-11462` (`DB_UpdateNursingTime` → `dbo.smp_update_nursing_time`, `DB_Farm.cpp:55-70` ; log `LM_FARM_NURSE`) | `new` en `0x1400d64a8`, constructeur `DB_UpdateNursingTime` en `0x1400d64cb`, `DBQuery` en `0x1400d64db` ; `nursing_time = now` en `0x1400d64e8` ; log `LM_FARM_NURSE` par appel virtuel en `0x1400d6524` puis `true` |

Le verrou de soin est donc **par couple (personnage, carte)** et **par jour**, pas par emplacement : c'est
l'heure de soin de l'entrée de ferme qui est comparée, et non un drapeau d'objet. L'horloge est celle de la
machine serveur (`time(NULL)` + `localtime_s`), pas un fuseau configuré — c'est l'écart de politique du §7.2.
Aucun ticket, aucun coupon et aucun consommable n'est consommé par le soin (contrairement au dépôt 6002 qui
exige tickets et crackers) : `NurseSummon` ne touche qu'à `nursing_time`.

### 5.3 Le cadeau : un script Lua, hors 6007

`NPC_Creature_farm.lua:52-59` (officiel 2015) :

```lua
function NPC_Creature_Farm_nurse_handler()
   local rate = math.random(1, 10)
   if rate < 6 then
      insert_item( 710009, 1 )
      return 1
   end
   return 0
end
```

Le cadeau est donc **inséré par le script**, par la voie normale d'ajout d'objet — jamais dans la trame
6007, qui ne porte que le verdict. `math.random(1,10) < 6` vaut **5 chances sur 10**, l'objet est
**710009 en quantité 1**, et la valeur de retour est le texte `"1"` ou `"0"` (c'est la chaîne, non un
booléen, que les deux serveurs comparent).

Identité de l'objet, établie pour 7.3 : `710009` — `name_id` `10710009`, groupe 94, classe 0,
`reference/sqlserver/Arcadia/ItemResource.csv:3513` ; textes `StringResource_FR.csv:23548`
(« Cadeau du Refuge ») et `:39687` (« … du Refuge. Contient un seul objet. », « Lié au personnage ») ;
`StringResource_DE.csv:23524` (« Geschenk von deiner Kreatur »). Nom anglais du catalogue 7.3 :
**« Creature's Present »** (`DevConsole/auction-catalog.73.json`, entrée
`{"Code":710009,"NameId":10710009,"Group":94,"Class":0}`). `client73/db_item.rdb` porte également
`710009` et son `name_id` `10710009` (corroboration ; l'en-tête de ce `.rdb` porte la date `20251207` et
« Written by Arche… », donc ce fichier n'est pas une preuve d'époque à lui seul).

Corroboration indépendante de l'existence du cadeau **en 7.3**, par les chaînes de l'époque :
`StringResource_FR.csv:1069` (`smsg_creaturefarm08`, `<(version:7.3)>`) « Vous pouvez rendre visite à vos
créatures confiées une fois par jour. » et `:1070` (`smsg_creaturefarm09`, `<(version:7.3)>`) « Si vous
prenez soin de votre créature régulièrement, elle pourrait vous offrir un cadeau pour vous remercier ! ».

### 5.4 Ce que Navislamia a déjà, et ce que le lot devra produire

* **Accroche de lecture** : `GamePackets.cs:451` (`TM_CS_NURSE_CREATURE = 6006`),
  `GameFarmPackets.cs:181-206` (`TryReadNurseCreature` : 11 octets exactement, poignée `u32` en `+7`),
  `GameClient.cs:1152-1171` (`HandleNurseCreature` : lit, journalise, **ne répond rien**) et
  `:3731-3733` (aiguillage).
* **Déclaration manquante** : `TM_SC_RESULT_NURSE = 6007` (§4).
* **État de ferme déjà sur `master`** : `CreatureFarmEntity.NursingTime` (`nursing_time`, commentaire
  `:57-60`) et la migration `20261006104428_Version0026_CreatureFarm.cs` ;
  `CreatureFarmStore.SetNursingTimeAsync(personnage, carte, heure)` (`CreatureFarmStore.cs:56`, `:157`)
  — l'écriture exacte de `DB_UpdateNursingTime` et rien d'autre ; `CreatureFarmRules.NursingResetHour = 6`
  (`:79`), `LastNursingReset(now)` (`:178-188`) et `RefreshSeconds` (`:190-212`).
* **Bac à sable Lua déjà sur `master`** : `insert_item` est lié par `NpcScriptService.cs:382-391` — il
  refuse un objet absent du catalogue 7.3 (`_names.TryGetItem`, `:384`) et **710009 y est présent**
  (catalogue `DevConsole/auction-catalog.73.json`, chargé par `DevConsole/Program.cs:136-149`, exposé par
  `IAuctionCatalog.TryGetItem`, `AuctionCatalog.cs:11-28`). Le script de ferme, lui, **n'est pas dans le
  dépôt** (`git ls-tree master -- Game/Scripting` : aucun `*farm*`/`*nurse*`) et le contrat d'appel du
  serveur officiel (chunk Lua rendant une chaîne, comparée à `"1"`) n'a pas d'équivalent direct :
  `ScriptService.RunString(string)` (`ScriptService.cs:102-110`) rend `1`/`0` selon le succès de
  l'exécution, pas la valeur de retour du chunk. Comportement officiel à conserver : **script absent ou
  valeur de retour autre que `"1"` ⇒ `NO_REWARD` (1)**, jamais `FAILED` — `FAILED` est réservé au
  `false` de `NurseSummon` (officiel `:11935-11938`).
* **Cohérence** : ce lot ne doit **pas** passer par `CreatureFarmDepositService` (MR #84) ni par la table
  de coût des tickets (MR #86) — voir §5.5.

### 5.5 Couture avec les MR ouvertes — **aucune dépendance**

Le soin ne consomme ni ticket ni cracker (§5.2 : `NurseSummon` n'écrit que `nursing_time`) et son cadeau
est un ajout d'objet ordinaire (§5.3). Il ne touche donc **ni** `CreatureFarmDepositService` /
`CreatureFarmItemCatalog` (branche `hermes/packet-6002-foster-creature`, MR #84) **ni** la table de coût
des tickets (branche `hermes/packet-socle-cout-tickets-ferme`, MR #86). Tout ce dont il a besoin est sur
`master` : accroche de lecture, `CreatureFarmEntity.NursingTime`, `CreatureFarmStore.SetNursingTimeAsync`,
`CreatureFarmRules` et le bac à sable Lua. **Réponse au point 5 du cadrage : non, ce lot n'est pas
dépendant des MR #84 et #86 et n'a pas à attendre leur fusion.** Le seul état de ferme qu'il lit est la
ligne de dépôt de la carte, par sa poignée (§3.1).

### 5.6 Recette (critères transversaux du dépôt)

Test d'offsets pour 6007 (taille totale 8, `ID` en `+4`, `result` en `+7`) et pour la trame 6006 déjà lue
(11 octets, poignée en `+7`) ; enum et aiguillage modifiés ensemble pour `TM_SC_RESULT_NURSE` ; au moins
un cas par valeur de `result` (0/1/2) et par refus de `NurseSummon` (poignée inconnue, carte non déposée,
soin déjà fait avant 06:00, soin autorisé après 06:00) ; `dotnet build` et `dotnet test` en code 0, sans
baisse du nombre de tests.

### 5.7 Ce que le lot livre (implementé)

Branche `hermes/packet-6006-nurse-creature`. Tout le savoir durable du lot est ici ; rien n'a été deviné et
aucune constante 2015 n'a été portée.

* **`GamePackets`** : `TM_SC_RESULT_NURSE = 6007` déclaré et le commentaire de la famille corrigé (6003/6005
  restent non déclarés, ils appartiennent aux MR #84/#86). L'aiguillage de `GameClient` gagne son bras :
  6007 est journalisé et **abandonné** (patron de 6001) — enum et aiguillage ont bougé ensemble, aucun membre
  ne peut atteindre le `switch` final.
* **`GameFarmPackets`** : `NurseResult` (`Failed = 0`, `NoReward = 1`, `Rewarded = 2`), `ResultNurseLength = 8`,
  `ResultNurseResultOffset = 7` et `BuildResultNurse` — le seul écrivain de la trame. La somme de contrôle est
  calculée comme pour toutes les trames du dépôt (réserve 5 inchangée).
* **`CreatureFarmStore.LoadNursingTargetAsync(personnage, poignée)`** : deux lectures, aucune écriture. La
  poignée `6006` est **l'id de l'objet** (`ItemFixedInfo.FromItem` : `Handle = (uint)item.Id`) ; la carte est
  résolue dans l'inventaire du personnage, et la comparaison d'UID du §5.2 devient l'existence de la ligne de
  ferme nommant cette carte (`CardItemId`). Retour `null` = `FindItem` en échec.
* **`CreatureFarmRules`** : `NurseHandlerFunction = "NPC_Creature_Farm_nurse_handler"`, `CanNurse` (les quatre
  conditions du §5.2, dans l'ordre) et `NurseVerdict` (retour comparé à `"1"`). La fenêtre du 06:00 réutilise
  `RefreshSeconds(nursing_time, now) == 0` — la même horloge, donc la même fonction, que le `refresh_time` du
  `6001` : aucune seconde logique de temps n'a été ajoutée. **`ITEM_FLAG_NURSED_SUMMON` (bit 28) n'est ni lu
  ni posé** : les sources officielles tranchent pour « aucun drapeau » (réserve 3).
* **`CreatureFarmService.NurseAsync`** : `NurseSummon` puis son appelant, dans l'ordre du §5.1 — décider,
  **écrire** `nursing_time` (`SetNursingTimeAsync`, et rien d'autre : ni drapeau, ni ticket), appeler le
  script, répondre. Toute refus rend `FAILED` (0) **et un 6007 est bien envoyé** : le client attend cette
  réponse pour chaque 6006 qu'il a lui-même construit. Une lecture ou une écriture qui échoue répond aussi
  `FAILED` (silence côté client) plutôt que de casser la session ; une session sans personnage ne répond rien.
  Une trame 6006 malformée n'est pas répondue (politique du dépôt, réserve 6).
* **Suture Lua — `IScriptService.CallGlobalFunction(nom)`** (nouveau membre, implémentation par défaut
  `null`) : le contrat du §5.4 n'avait pas d'équivalent (`RunString` rend le succès de l'exécution, pas la
  valeur du chunk). Il appelle la fonction globale et rend sa valeur sous forme de chaîne (`"1"`, `"0"`, …),
  ou `null` si la fonction est absente ou lève. `ScriptService` l'implémente sur son interpréteur.
* **Recette** : 24 tests neufs dans `Tests/Game/CreatureFarmNurseTests.cs` (offsets des deux trames, les trois
  valeurs de verdict, chacune des quatre conditions, le geste de bout en bout sur un store simulé — 0/1/2,
  ligne de ferme disparue avant l'écriture, script absent, session sans personnage —, l'interpréteur réel
  (`ScriptService` et MoonSharp : valeur du chunk, fonction absente, chunk qui lève) et l'aiguillage : 6006
  répondu, 6006 malformé muet, 6007 entrant abandonné) ; `FarmPacketsTests` suit (6007
  déclaré et routé, 6003/6005 non déclarés, 6006 retiré de la liste des trames muettes).
  `dotnet build` et `dotnet test` en code 0 : **4031 tests passés, 0 échec** (dont les 24 de
  `Tests/Game/CreatureFarmNurseTests.cs`, comptés par `--filter`) ; le total mesuré avant le lot n'a pas été
  relevé sur cette branche, seul le plancher de 366 est contractuel.

---

## 6. Écarts assumés avec NGemity

NGemity / Chihiro @ `38ceb2c6` **n'implémente rien de la ferme** : `grep -rniE 'nurse|foster|farm'`
sur `reference/ngemity/src/` → **0 résultat**. Il ne fournit que les deux en-têtes de trame
(`TS_CS_NURSE_CREATURE.h:6-10`, `TS_SC_RESULT_NURSE.h:6-10`), qui s'accordent avec rzu
(`uint32_t creature_card_handle`, `int8_t result`) mais **ne portent ni l'énumération de `result`, ni
aucun traitement**. Il est donc écarté comme source de logique, conformément au cadrage : il ne sert qu'à
écarter la piste « NGemity aurait déjà tranché ». rzu, lui, ne fait que refléter les en-têtes officiels.

Aucun écart avec le serveur **officiel** n'est assumé pour 6007 ou pour `NurseSummon` : les deux sources
(2015 et binaire 7.3) concordent sur tous les points mesurés (§5.1-5.2), y compris le mapping du verdict
(`"1"` ⇒ 2) et la fenêtre 06:00. Les écarts de la ferme relevés par le socle (§6.2 de
`socle-ferme-creatures-officielle.md`, expérience horaire, `ValidateCracker`) ne concernent pas ce lot.

---

## 7. `NON ÉTABLI` — questions ouvertes, formulées précisément

1. **Probabilité et quantité du cadeau en 7.3** : le script `NPC_Creature_farm.lua` de l'époque 7.3
   **n'est pas dans les références locales** (`find /srv/navislamia/reference/epic7part4 -iname
   '*farm*'` → rien ; seul l'arbre 2015 en a un). Le nom de la fonction est bien 7.3 (le binaire
   `2012-11` appelle `NPC_Creature_Farm_nurse_handler()`, chaîne en `0x140412128`) et le cadeau existe
   en 7.3 (§5.3), mais **la valeur 5/10 et la quantité 1 ne sont établies que pour 2015**. Question :
   le 7.3 tire-t-il la même chose ? → **§A VERIFIER, réserve 1**.
2. **Portée exacte de « 06:00 »** : officiellement l'heure locale **de la machine serveur**
   (`time(NULL)` + `localtime_s`, `StructPlayer.cpp:11439-11453` ; `movl $0x6` en `0x1400d6446`). Le
   projet, lui, prend l'horloge en paramètre (`CreatureFarmRules.LastNursingReset(now)`, `:178-188`) et
   documente « 06:00 heure locale du serveur » : **quel appelant fournit `now`, et dans quel fuseau**
   (machine ou `Europe/Paris` comme d'autres tables du dépôt) n'est pas tranchable sur les sources
   seules → **§A VERIFIER, réserve 2**.
3. **`ITEM_FLAG_NURSED_SUMMON` (bit 28)** : les deux serveurs officiels (2015 et 7.3) **ne posent aucun
   drapeau d'objet** au soin — ils écrivent `nursing_time` et comparent cette heure (`StructPlayer.cpp:11423-11467` ;
   `NurseSummon` en `0x1400d63a0` : `btl $0x1b` en `0x1400d63d5`, comparaison de l'heure en
   `0x1400d6496`, écriture en `0x1400d64e8`, aucun `bts` du bit 28). Le masque et les aides `NursedSummonMask` / `IsNursed` /
   `WithNursedSummon` / `WithoutNursedSummon` existent sur `master` (`CreatureFarmRules.cs:71-76`) avec
   un commentaire qui le dit déjà (« le `NurseSummon` 7.3 ne pose aucun flag »). En revanche le §5.6 du
   socle (point 2, « sous-ensemble 7.3 minimal à porter ») range le bit 28 « au soin », ce qui
   **contredit** le §5.4 du même socle :
   les sources disent « aucun drapeau, l'heure suffit », donc **le lot du soin n'a pas à toucher le bit
   28** — la contradiction entre les deux paragraphes du socle est portée au §A VERIFIER (réserve 3).
4. **Texte des deux messages du client** : les ids de chaîne `0x56c8e3e` (pas de cadeau) et `0x56c8e3f`
   (cadeau) sont des identifiants chiffrés du client ; la table de chaînes correspondante n'est pas dans
   les références locales et aucun outil du client ne peut être lancé. Le fait établi est la
   **séparation** des deux réponses, pas leur libellé → **§A VERIFIER, réserve 4**.
5. **Octet `Checksum` des trames serveur → client** : le constructeur 7.3 mesuré laisse `+6` à `0` et
   l'envoi passe ensuite par `PendMessage` ; Navislamia le calcule (`Packet.cs:57`). Rien n'établit que
   le client vérifie cet octet sur 6007 (il ne le lit pas dans le chemin `0x6722be`). Aucune action
   demandée au lot, mais la valeur « officielle » reste **non établie** → **§A VERIFIER, réserve 5**.
6. **Sémantique des deux champs de garde du client** (§2.4 : `0x5ec ≥ 0x5f4` et `0x5f8 ≤ 0`, déclinés par
   emplacement) : ils conditionnent l'envoi côté client mais leur sens (compte à rebours de disponibilité ?
   état du bouton ?) n'est pas établi par lecture. Sans effet sur la trame ; à ne pas confondre avec une
   validation serveur → **§A VERIFIER, réserve 6**.
7. **Trame 6006 malformée** : la politique du dépôt (lire, journaliser, **ne rien répondre**) est celle
   de `GameClient.cs:1152-1164` aujourd'hui ; aucun écart n'est établi entre cette politique et le client
   7.3 (qui n'attend une réponse que pour une trame qu'il a lui-même construite en 11 octets). Décision
   de conception, pas de source → **§A VERIFIER, réserve 7**.

---

## 8. Commits et références épinglés

| Référence | Épinglage |
|---|---|
| `NavisLamia` (base de la fiche) | `master` = `a9f01fbad8437e0f52eb10089c37b13552f69b15` ; branche `hermes/packet-6006-nurse-creature` |
| rzu (tailles, ordre, gating) | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` — `librzu/src/packets/GameClient/TS_CS_NURSE_CREATURE.h`, `TS_SC_RESULT_NURSE.h`, `librzu/src/lib/Packet/PacketDeclaration.h`, `GameTypes.h`, `PacketEpics.h:59` |
| NGemity / Chihiro (logique : néant) | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` — `shared/Server/Packets/GameClient/TS_{CS_NURSE_CREATURE,SC_RESULT_NURSE}.h` ; `grep -rniE 'nurse\|foster\|farm' src/` → 0 |
| Serveur officiel 2015 (**pas un dépôt git**) | `reference/official2015/` : `GameServer/Game/Message/GameMessage.{h,cpp}` (SHA-256 `183e1315…`, `e7f5399a…`), `GameServer/Game/Struct/StructPlayer.cpp` (`40c1fbcf…`), `GameServer/Game/Db/DB_Farm.cpp` (`1a428983…`), `GameServer/Game/Rule/GameRule.cpp` (`90e2600b…`), `GameServer/Resource/Script/NPC_Creature_farm.lua` (`4c713c92…`) |
| Client Epic 7.3 (**pas un dépôt git**) | `reference/client73/SFrame.exe` SHA-256 `41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` ; `db_item.rdb` (`e70c1858…`), `db_creaturefarm.rdb` |
| Serveur Epic 7.3 `2012-11` (**pas un dépôt git**) | `reference/epic7part4/server/2012-11/CaptainHerlockServer.exe` SHA-256 `83b54fe18f15601578e35bcad1832a5c254bb09f512ab6297217fa49d9dd3eac` (+ `CaptainHerlockServer.symbols.tsv`) |
| Base de données 7.3 (textes, objets) | `reference/sqlserver/Arcadia/ItemResource.csv` (`9c93c585…`), `StringResource_FR.csv` (`1113b9a2…`), `StringResource_DE.csv` (`df9e1454…`) |

Fiches du dépôt à lire avec celle-ci : `docs/packet-specs/socle-ferme-creatures-officielle.md`
(§3.4 verdict 6007, §3.7 les huit trames, §5.4 le soin, §5.6 le modèle minimal),
`docs/packet-specs/socle-ferme-creatures.md` (§2.1 fenêtre, §3.4/§3.5 tailles, §7 questions ouvertes).

---

## A VERIFIER PAR KILLIAN

1. **Probabilité et quantité du cadeau en 7.3.** La seule valeur chiffrée disponible est celle du script
   **2015** : `math.random(1,10) < 6` (5/10) et `insert_item(710009, 1)` (`NPC_Creature_farm.lua:52-59`).
   Le script de l'époque 7.3 est absent des références locales ; le nom de fonction, l'objet et
   l'existence d'un cadeau sont, eux, établis en 7.3. **Décision demandée** : le lot porte-t-il 5/10 × 1
   objet (valeur 2015, étiquetée comme telle), ou attend-on un autre chiffre ? Aucune constante ne sera
   choisie au hasard.
2. **Quel appelant fournit l'horloge du `06:00`** (§7.2) : heure locale de la machine (comme les deux
   serveurs officiels) ou fuseau configuré ? `CreatureFarmRules.LastNursingReset(now)` prend l'horloge en
   paramètre et n'a aujourd'hui aucun appelant.
3. **Bit `ITEM_FLAG_NURSED_SUMMON` (28)** : contradiction interne au socle entre son §5.4 (« aucun
   drapeau au soin ») et son §5.6.2 (« bit 28 au soin »). Les sources officielles tranchent pour
   « aucun drapeau ». Confirmer que le lot du soin **n'utilise pas** le bit 28, ou dire ce qu'il doit
   en faire.
4. **Textes des deux messages de résultat** (`0x56c8e3e` / `0x56c8e3f`) : non décodables localement
   (§7.4). À confirmer si un jour la table de chaînes du client est disponible — sans effet sur la trame.
5. **Octet `Checksum` en sortie** (§7.5) : le serveur 7.3 mesuré laisse `0`, Navislamia calcule la
   somme. À arbitrer si la QA constate un écart client (aucun n'est établi).
6. **Politique de jeu non tranchable sur les sources** : réponse à une trame 6006 malformée (politique du
   dépôt : silence, `GameClient.cs:1152-1164`) et comportement quand le script Lua de la ferme est absent
   (officiel : Le script est toujours appelé, tout ce qui n'est pas `"1"` vaut `NO_REWARD`, jamais
   `FAILED`). Confirmer ces deux politiques.
7. **Interpréteur du script de ferme** (§5.7, livré par ce lot) : le lot appelle
   `NPC_Creature_Farm_nurse_handler()` par `IScriptService.CallGlobalFunction`, donc dans l'interpréteur
   global de `ScriptService` (celui qui charge `<racine>/Scripts/**.lua`). Or `insert_item` n'est lié que dans
   le bac à sable des dialogues (`NpcScriptService.cs:382-391`) : un script de ferme chargé dans
   l'interpréteur global échouerait sur `insert_item` et rendrait `NO_REWARD` — sans effet visible pour le
   joueur, mais sans cadeau non plus. **Décision demandée** : lier `insert_item` dans l'interpréteur global,
   ou déplacer l'appel dans le bac à sable des dialogues (qui devrait alors rendre la valeur du chunk) ?
   Cette question commande la réserve 1 : sans script chargé, aucun `REWARDED` n'est servi.

---

## Bloc pour CLAUDE.md

> **Ferme de créatures — le soin (6006/6007).** Le client n'émet `TM_CS_NURSE_CREATURE` (6006, **11
> octets**, poignée de carte `u32` en `+7`) que par l'un des trois boutons « ministration » d'emplacement,
> et ne fait rien d'autre que l'aiguiller. Le serveur répond **toujours** `TM_SC_RESULT_NURSE` (6007,
> **8 octets**, `result` `i8` en `+7`) : `0` = `FAILED` (poignée inconnue, carte non déposée, ou soin déjà
> fait depuis le dernier 06:00 — le client ne réagit pas), `1` = `NO_REWARD`, `2` = `REWARDED` (le client
> affiche alors deux messages distincts et marque l'emplacement). `NurseSummon` exige
> `ITEM_FLAG_FARMED_SUMMON` (bit **27**) et l'égalité d'UID avec une entrée de ferme, puis écrit
> `nursing_time` (`DB_UpdateNursingTime`) — **une fois par jour et par carte**, fenêtre à **06:00 heure
> locale du serveur** (deux sources concordantes : officiel 2015 et binaire 7.3). Aucun ticket n'est
> consommé. Le cadeau (`710009`, « Creature's Present » / « Cadeau du Refuge ») n'est **pas** dans 6007 :
> il est inséré par le script Lua `NPC_Creature_Farm_nurse_handler()` (`insert_item(710009, 1)` si
> `math.random(1,10) < 6`, valeur 2015), dont la valeur de retour texte `"1"` vaut `REWARDED` ; script
> absent ou autre retour ⇒ `NO_REWARD`, jamais `FAILED`. **Livré** : `TM_SC_RESULT_NURSE = 6007` est déclaré
> et routé (log et abandon d'un 6007 entrant), et `CreatureFarmService.NurseAsync` sert le verdict — le
> script de ferme n'étant pas dans le dépôt, c'est `NO_REWARD` qui part aujourd'hui (A VERIFIER 7).
> Le lot n'a aucune dépendance sur les MR #84/#86.
> Tout le savoir est dans `docs/packet-specs/6006-nurse-creature.md`.

## Revue d'intégration du 2026-10-07 (Claude, fusion des PR #84, #85, #86, #87)

Corrections apportées à la fusion, la fiche ci-dessus restant la trace du lot :

- **Heures en UTC.** Le dépôt et le soin écrivaient `DateTime.Now` (heure locale) dans des colonnes
  `timestamp with time zone` : Npgsql 8 lève une exception au `SaveChanges`, que les magasins factices des tests ne
  voient pas. `CreatureFarmStore` convertit en UTC à l'écriture. L'expiration d'une entrée (`IsExpired`, 6000)
  comparait l'heure UTC relue en base à l'heure locale : elle passe par `CreatureFarmRules.ElapsedSeconds`.
- **Table des coûts branchée.** `ICreatureFarmTicketCost` était enregistré vide (tout dépôt refusé) alors que le socle
  de la PR #86 charge `creature-farm-costs.73.json` : `CreatureFarmTicketCost.FromOptions`.
- **Refus de `FarmSummon` ajoutés au dépôt** (`StructPlayer.cpp:11264-11300`, `CreatureFarmRules.DepositRefusal`) :
  invocation au plafond (100, forme 1 à 60, forme 2 à 115), ticket ordinaire pour une invocation pas sous le niveau du
  maître, carte formée, invocation sortie ou carte de ceinture (`HeldItemRules`).
- **La carte quitte le sac du client** (`PopItem` → 254, `RemoveSummon` → 302 ; `PushItem` → 207, `AddSummon` → 301) :
  `ICreatureService.OnCardFarmedAsync`, `BuildInventory` n'envoie jamais une carte au bit 27, `HeldItemRules` et la
  formation la refusent. Le choix « la carte reste dans le sac » laissait une carte en ferme invocable, échangeable et
  vendable.
- **La fenêtre s'ouvre** : `show_creature_farm_window()` du PNJ 11467 envoie `TS_SC_DIALOG` type 9 `CreatureFarm`
  (`SCRIPT_ShowCreatureFarmWindow`). 6008 (marché de la ferme) reste sans réponse.
- Non repris, faute de champ dans le dépôt : le refus d'une carte à durée limitée ou à durabilité éthérée épuisée.
