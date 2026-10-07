# Socle — la table de coût en tickets de la ferme de créatures (`CreatureFarmResource`)

Fiche de **sous-lot**, pas de paquet : elle spécifie une *donnée de ressource* et le geste qui la
charge, là où les fiches `60xx` spécifient des trames. Toutes les mesures ont été refaites sur le VPS
le 7 octobre 2026 par lecture directe des fichiers locaux (aucun binaire du client exécuté, aucun Lua,
aucun script du client). Chaque ligne porte sa source `fichier:ligne`.

Consommateur unique de cette table : le lot **`6002`** (`TM_CS_FOSTER_CREATURE`), dont la fiche
`docs/packet-specs/6002-foster-creature.md` a décodé les 72 lignes en §5.5, y a **décidé de renvoyer le
chargement à une carte « Socle » séparée** (bloc *Décision* de §5.5) et laisse la provenance en
A VERIFIER 1 de sa fiche.

---

## 0. Les cinq points de la carte — réponses

| # | point | réponse | section |
|---|---|---|---|
| 1 | `(a)` le socle apporte sa propre couture sur `master`, ou `(b)` il dépend du merge d'un lot antérieur ? | **`(b)`, dépendance explicite au merge de la MR `#84` (lot `6002`).** `master` (`a9f01fb`) ne porte **aucun** `ICreatureFarmTicketCost`, `CreatureFarmTicketCost`, `CreatureFarmItemCatalog` ni `CreatureFarmDepositService` (`git grep` sur `master` : 0 occurrence de ces quatre noms). Le seul consommateur de la couture vit sur `hermes/packet-6002-foster-creature` (tête `b22c65a`, couture posée par `f8b0cc9`, **pas** ancêtre de `master` : `git merge-base --is-ancestor hermes/packet-6002-foster-creature master` → faux). L'option `(a)` recréerait le même type au même chemin et un composant que personne n'appelle, puis un conflit garanti à la fusion : **écartée**. La branche du socle se coupe de `master` **après** la fusion de `#84`. Aucune pile de branches n'a été construite. | §5.3 |
| 2 | Ce qu'est la table, mesuré, et la provenance | **Deux copies locales lisibles, pas une** : le `.rdb` du client 7.3 (72 lignes, `sha256 36564e4a…dea3d`) et le CSV de l'export 9.4 déjà présent dans les données du dépôt (108 lignes, `sha256 38a55f69…cede7`), plus le DDL seul dans le dépôt (`ArcadiaSchemaPSQL.sql:46-52`, aucun `INSERT`). Le serveur officiel de l'ère 7.3 (build 2012-11) implémente bien la table et son accesseur, mais l'extrait local `epic7part4/` **ne contient pas** la table. Cela **corrige deux affirmations** de la fiche `6002` (§5.5 point 1 et §7.5). | §3, §6.4 |
| 3 | Le mode de chargement | **Tranché : outil Python hors ligne → catalogue JSON → enregistrement au démarrage**, le précédent du dépôt pour toute donnée de ressource venue du client (13 catalogues `*.73.json` committés, `tools/export_*.py`, `DevConsole/Program.cs`). Aucun lecteur `.rdb` n'existe côté serveur et il ne faut pas en ajouter un. | §5.3 |
| 4 | Schéma d'accès retenu | `(rate, form, enhance_level) → ticket_count`, quatre entiers, **sans** valeur par défaut : la référence rend **0** pour une clé absente après un `assert` (`GameContent.cpp:2922-2924`), et **0 reste le sentinelle de la clé absente** — une ligne ne peut donc pas coûter moins de 1. Clés uniques et table complète (mesuré). | §3, §4 |
| 5 | Ce que devient la validation quand la table est absente | **Fait, mesuré** : la couture rend 0 pour toute clé, et un dépôt est alors **toujours refusé** (`6003`, `result = 0`) quel que soit le nombre de tickets offert. Les options de repli sont listées en §5.4 et **le choix reste à Killian** (A VERIFIER 1) : c'est une décision de jeu, pas d'archéologie. | §5.4 |

---

## 1. Identité

| élément | valeur | source |
|---|---|---|
| Nature | table de ressources `CreatureFarmResource` (4 colonnes), **pas un paquet** | `ArcadiaSchemaPSQL.sql:46-52` |
| Colonne de la clé | `(rate, form, enhance_level)`, `ticket_count` rendu | `ArcadiaSchemaPSQL.sql:46-52` ; `ContentStruct.h:286-299` |
| Accesseur de référence | `GameContent::GetCreatureFarmTicketCount(rate, form, enhance_level)` | `GameContent.cpp:2910-2925` |
| Site d'appel unique | `FosterCreature` → `GetRate()`, `GetTransformLevel()`, `GetEnhance()` de la carte de créature | `GameMessage.cpp:11866` |
| Tramage | aucune trame 7.3 ne transporte la table ; elle n'est ni dans `6000`-`6008` ni nulle part ailleurs | `op_codes.md:256-264` |
| Paquet consommateur | `[6002] = "TM_CS_FOSTER_CREATURE"`, réponse `[6003] = "TM_SC_RESULT_FOSTER"` | `op_codes.md:258-259` |
| Couture du dépôt | `ICreatureFarmTicketCost.GetTicketCount(int rate, int form, int enhanceLevel)` + `CreatureFarmTicketCostRow(Rate, Form, EnhanceLevel, TicketCount)` | branche `hermes/packet-6002-foster-creature` : `Game/Services/Creatures/CreatureFarmTicketCost.cs:16-26,33-51` |
| Gating rzu | **aucun** : rzu porte les quatre trames de la famille (`TS_CS_REQUEST_FARM_INFO.h:7-13` `X(6000, true)`, `TS_CS_FOSTER_CREATURE.h:28-32` `X(6002, true)`, `TS_SC_FARM_INFO.h`, `TS_SC_RESULT_FOSTER.h:10-14` `X(6003, true)`, toutes sous `// Since EPIC_7_3`) mais **aucune structure pour la table** (`grep -ri creaturefarm` sur rzu → 0 ; les seules occurrences sont ces quatre en-têtes de trame) | rzu `87c1e83b` |
| NGemity | **aucune trace** : `grep -rli creaturefarm` sur tout le dépôt → **0 fichier** ; `Database/Arcadia.sql` (81 195 lignes) et `Database/Telecaster.sql` (698 lignes) ne déclarent pas la table | NGemity `38ceb2c6` |

---

## 2. Ce que le joueur fait pour que cette donnée serve

1. Il ouvre la fenêtre de la ferme : le client envoie `TM_CS_REQUEST_FARM_INFO` (6000) et le serveur
   répond `6001` (fiche `docs/packet-specs/socle-ferme-creatures.md` §2.1, §3.1, §3.2).
2. Il pose une **carte de créature** dans une case libre : le bouton d'assignation envoie
   `TM_CS_FOSTER_CREATURE` (6002) avec le handle de la carte, une pile de tickets et, au choix, une pile
   de crackers (fiche `6002`, §2 et §3).
3. Le nombre de tickets que cette carte exige **n'est ni dans la trame ni dans une constante** : c'est la
   ligne `(rate, form, enhance_level)` de la carte dans `CreatureFarmResource`, et le serveur refuse la
   trame si la somme offerte ne l'égale pas exactement (`GameMessage.cpp:11799`,
   `return ticketCount == requiredTicketCount`).
4. Le client, lui, charge la même table pour son propre affichage : `db_CreatureFarm.rdb` est une chaîne
   du binaire 7.3 (`strings -a SFrame.exe`, chaîne `db_CreatureFarm.rdb` — noter la majuscule du nom
   interne alors que le fichier extrait s'appelle `db_creaturefarm.rdb`).

Ce que le client en fait exactement (quel libellé pour quel refus, quel site remplit les deux comptes)
n'est **pas** établi : la fiche `6002` §7 points 3 et 4 le portent déjà. Le socle n'en dépend pas.

---

## 3. Structure de la donnée sur le disque

Il n'y a pas de paquet ici ; « la structure sur le fil » de la fiche de paquet devient ici la structure
des octets de la ressource, et la taille attendue en octets du chargement.

### 3.1 Le fichier du client 7.3 — `reference/client73/db_creaturefarm.rdb`

Mesure : **420 octets**, `sha256 36564e4a4405afa01133a4e519e6e83d5a379228ad4202ec4ed91b7d2dfdea3d`.

| offset | type | sens | valeur mesurée | source |
|---|---|---|---|---|
| 0–7 | ASCII | marque de date du packer | `20110523` | octets lus (`od -t x1`) |
| 8–127 | octets | reste de l'en-tête, **tout à zéro** (128 octets d'en-tête au total) | `00 … 00` | idem ; même en-tête 128 o que `tools/export_pet_catalog.py:7-9` et `tools/client73_ids.py:4-6` |
| 128–131 | `uint32` LE | **nombre d'enregistrements** | `48 00 00 00` = **72** | octets lus ; `tools/client73_ids.py:38` lit ce même `u32` à l'offset 128 |
| 132–419 | 72 × 4 × `uint8` | les enregistrements, `128 + 4 + 72 × 4 = 420`, **sans reste** | **288 octets** de corps | octets lus ; `tools/export_monster_drops.py:43-45` refuse justement un corps qui ne se divise pas |

Correspondance **octet → colonne** : `octet 0 → rate`, `octet 1 → form`, `octet 2 → enhance_level`,
`octet 3 → ticket_count`. Trois recoupements concordants :

- le désassemblage du binaire 7.3 (`GameContent::GetCreatureFarmTicketCount` à `0x14018bb10` dans
  `CaptainHerlockServer.exe` de 2012-11) compare l'octet 0 au 1er paramètre, l'octet 1 au 2e, l'octet 2
  au 3e et rend l'octet 3 — relevé déjà fait par la fiche `6002` §5.5, non refait ici ;
- l'ordre des colonnes du DDL du dépôt est le même : `rate`, `form`, `enhance_level`, `ticket_count`
  (`ArcadiaSchemaPSQL.sql:47-51`) ;
- le corps **se lit comme une table complète** : `rate` ∈ 0..5 × `form` ∈ 1..2 × `enhance_level` ∈ 0..5 =
  **72 clés, exactement le nombre d'enregistrements**, dans cet ordre croissant, valeurs 1..18.

Contenu décodé (renforcement 0 → 5) — **indépendamment de la fiche `6002`**, même résultat :

```
rate 0, form 1 : 1 2 2 3 3 4        rate 0, form 2 : 3 6 6 9 9 12
rate 1, form 1 : 1 2 2 3 3 4        rate 1, form 2 : 3 6 6 9 9 12
rate 2, form 1 : 1 2 2 3 3 4        rate 2, form 2 : 3 6 6 9 9 12
rate 3, form 1 : 2 3 3 4 4 5        rate 3, form 2 : 6 9 9 12 12 15
rate 4, form 1 : 2 3 3 4 4 5        rate 4, form 2 : 6 9 9 12 12 15
rate 5, form 1 : 3 4 4 5 5 6        rate 5, form 2 : 9 12 12 15 15 18
```

Contrôles de complétude (mesurés) : 72 clés distinctes, **aucun doublon**, **aucun trou** dans le
produit cartésien 6 × 2 × 6, ordre croissant `(rate, form, enhance_level)`, `min = 1`, `max = 18`.

### 3.2 La copie de l'ère 7.3 du serveur officiel

Le binaire `epic7part4/server/2012-11/CaptainHerlockServer.exe` (build 2012-11-22, branche
`gameserver_release/2011-12-12`) porte **toute** la machinerie, relevée dans son fichier de symboles :

| symbole | adresse | source |
|---|---|---|
| `GameContent::GetCreatureFarmTicketCount(char, char, char)` | `0x14018bb10` | `CaptainHerlockServer.symbols.tsv:3605` |
| `GameContent::RegisterCreatureFarmInfo(char, char, char, char)` | `0x14019bde0` | `:4020` |
| `onCreatureFarmData(struct dbCreatureFarm *)` | `0x14017b7d0` | `:3463` |
| `dbCreatureFarm::GetADOBindingEntries(void)` + `rgADOBindingEntries` (la table des colonnes liées) | `0x14017b7f0` + `0x140418eb0` | `:3464`, `:9879` |
| `LoadDbResource<struct dbCreatureFarm, …>(char const *, …)` | `0x14017b800` | `:3465` |
| `CreatureFarmLoader::onProcess(int)` | `0x14017bda0` | `:3466` |
| `CreatureFarmLoader::onEnd(bool)` | `0x1400bf4b0` | `:1905` |
| `CreatureFarmLoader::~CreatureFarmLoader(void)` | `0x140393ef0` | `:6728` |
| `FosterCreature` / `onFosterCreature` | `0x14011e3e0` / `0x14011e5f0` | `:2818`, `:2819` |
| `ValidateFarmTicket` / `ValidateCracker` | `0x14011e2d0` / `0x14010cb20` | `:2817`, `:2723` |
| `SCRIPT_ShowCreatureFarmWindow(struct lua_State *)` (le script serveur ouvre la fenêtre) | `0x1401ef000` | `:4749` |
| littéraux `"CreatureFarmResource"` / `"CreatureFarm"` | `0x140419080` / `0x140434db8` | `:9882`, `:10516` |

Deux conséquences pour la version (détaillées §4) : la ferme **existe** dans l'ère 7.3, et la table y est
une **table de base de données serveur**, pas une constante. En revanche l'extrait local
`reference/epic7part4/` **ne contient pas** la table : `rdu/` et `csv/` comptent 48 tables chacun
(`ls | wc -l`), dont `SummonResource` et `CreatureEnhance`, et **aucune** `CreatureFarmResource`
(`ls rdu/ | grep -i farm` → vide). Les lignes que le serveur de l'ère 7.3 lisait ne sont donc **pas**
lisibles localement ; c'est un `NON ÉTABLI` (§7.3), pas une absence de fonctionnalité.

### 3.3 Les deux autres copies locales

**(a) L'export 9.4 du dépôt** — `data/sqlserver/Arcadia/CreatureFarmResource.csv`
(le lien `data/sqlserver -> /srv/navislamia/reference/sqlserver`, exclu de Git par `.git/info/exclude`,
`reference/README.md:21-22`). **109 lignes = 1 en-tête + 108 lignes**, 951 octets,
`sha256 38a55f692f02b20c82a832557a0e5a79216417d493d58380d41c48012d6cede7`.

| élément | mesure |
|---|---|
| en-tête CSV | `"rate","form","enhance_level","ticket_count"` |
| lignes | **108** = `rate` 0..5 × `form` **1..3** × `enhance_level` 0..5, clés distinctes, complètes, ordre croissant |
| valeurs | `min = 1`, `max = 28` |
| blocs | `(r, 1) = 1 2 2 3 3 4` pour tout `r` ; `(r, 2) = 7 8 8 9 9 10` ; `(r, 3) = 25 26 26 27 27 28` |

**(b) Le DDL du dépôt, seul et non peuplé** — `ArcadiaSchemaPSQL.sql:46-52` :

```sql
create table "CreatureFarmResource"
(
    rate          int not null,
    form          int not null,
    enhance_level int not null,
    ticket_count  int not null
);
```

Aucune `primary key`, aucune contrainte d'unicité, **aucun `INSERT`** nulle part (`grep -rn
CreatureFarmResource` sur tout le dépôt versionné → cette seule ligne + les fiches), aucune entité EF,
aucun `DbSet` (`git grep` sur `master` : 0). Noter l'écart de type : le DDL du dépôt dit `int` là où la
référence lie des `adTinyInt` et où le fichier client écrit un octet (§6.2).

### 3.4 Tailles attendues du chargement

| grandeur | valeur attendue | pourquoi |
|---|---|---|
| fichier client complet | **420 octets** | `128 (en-tête) + 4 (compte) + 72 × 4` |
| corps utile | **288 octets** | `72 × 4`, sans reste |
| lignes chargées (source client) | **72** | 6 × 2 × 6, complet |
| lignes chargées (source export 9.4) | **108** | 6 × 3 × 6, complet |
| trame à ne pas régresser | `TM_SC_RESULT_FOSTER` (6003) = **8 octets**, `result` à `+7` | lot `6002`, fiche `docs/packet-specs/6002-foster-creature.md` §3.3 et §5.6 |

---

## 4. Gating de version — statué pour 7.3

Il n'y a **rien à gater** : rzu ne porte aucune structure pour cette table (§1), et la famille de trames
`6000`-`6008` est en `X(<id>, true)` sous `// Since EPIC_7_3` — les identifiants nus, aucun `version >=`,
donc aucun champ à retirer pour 7.3. Ce qui se statue pour 7.3 est **l'ère de la donnée** :

| question de version | décision pour 7.3 | source |
|---|---|---|
| La ferme et sa table existent-elles dans l'ère 7.3 ? | **Oui.** Le serveur officiel de 2012-11 expose l'accesseur, l'enregistreur, le chargeur et le littéral `"CreatureFarmResource"` | `CaptainHerlockServer.symbols.tsv` (§3.2) |
| La table est-elle une constante du binaire ou une donnée serveur ? | **Une donnée serveur** : chargée depuis la base au démarrage | `CreatureFarmLoader.cpp:32-44` ; `CaptainHerlockServer.cpp:194` |
| Quelle copie fait foi pour 7.3 ? | **Celle du client 7.3** : `db_creaturefarm.rdb`, datée `20110523`, 72 lignes, `form` ∈ {1, 2}. L'export 9.4 est un **recoupement d'ère plus tardive**, pas la donnée 7.3 | octets lus (§3.1) ; `reference/README.md:13-26` |
| Faut-il retirer les lignes `form 3` de l'export 9.4 ? | **Ne pas les importer** : elles n'existent dans aucune copie 7.3, et leur `form 2` porte des valeurs différentes de celles du client (`7 8 8 9 9 10` contre `3 6 6 9 9 12`) | mesures §3.1 / §3.3 |
| L'ère 7.3 accepte-t-elle un dépôt de créature `form 3` ? | **Non tranché** — voir §7.2 : les deux copies 7.3 locales se contredisent (le client connaît 35 invocations de `form 3`, sa propre table n'en price aucune). Ce n'est pas une question de gating mais de données ; le **refus** (`result = 0`) est le comportement par défaut retenu, celui de la référence pour une clé absente | mesures §7.2 |

Aucun champ de cette fiche n'est laissé sans statut de version.

---

## 5. Traitement attendu

### 5.1 La référence officielle, mot pour mot

```c
void GameContent::RegisterCreatureFarmInfo( const char rate, const char form, const char enhance_level, const char ticket_count )
{
    CreatureFarmInfo *creatureFarmInfo = new CreatureFarmInfo( rate, form, enhance_level, ticket_count );
    g_vCreatureFarmInfo.push_back( creatureFarmInfo );
}

const char GameContent::GetCreatureFarmTicketCount( const char rate, const char form, const char enhance_level )
{
    std::vector< CreatureFarmInfo * >::const_iterator it;

    for( it = g_vCreatureFarmInfo.begin(); it != g_vCreatureFarmInfo.end(); ++it )
    {
        if( (*it)->rate == rate &&
            (*it)->form == form &&
            (*it)->enhance_level == enhance_level )
            return (*it)->ticket_count;
    }

    assert( 0 && "Creature Farm Info ID" );

    return 0;
}
```

(`GameContent.cpp:2904-2925` ; le vecteur global est déclaré en `:320`.) Ce que cela impose :

| point | référence | conséquence pour le dépôt |
|---|---|---|
| Recherche | balayage linéaire, correspondance sur **les trois** colonnes | le dépôt indexe la même clé à trois composantes (dictionnaire) — même résultat, la référence n'a pas d'index |
| Clé absente | `assert` **puis `0`** | **0 est le contrat** : « aucune ligne » et « coût nul » sont indistinguables, et `0` doit le rester (une ligne à `ticket_count = 0` rendrait une clé absente indistinguable d'une clé gratuite) |
| Ordre | premier match gagnant | le dépôt **refuse** un doublon plutôt que de dépendre de l'ordre (le fichier et le CSV n'en portent aucun : mesuré) |
| Chargement | `LoadDbResource<dbCreatureFarm>("CreatureFarmResource", …)` — table entière, lie `adTinyInt` sur les quatre colonnes, enregistre ligne à ligne | le dépôt ne charge pas depuis SQL (§5.3) |
| Défaut | **aucun** : pas de ligne → 0 → refus | idem, à conserver |

Le site d'appel et l'effet sur la réponse :

```c
int requiredTicketCount = GameContent::GetCreatureFarmTicketCount( creature->GetRate(), creature->GetTransformLevel(), creature->GetEnhance() );

if( ValidateFarmTicket( pClient, ticketInfo, pMsg->ticket_info_count, requiredTicketCount ) && … )
```

(`GameMessage.cpp:11866-11868`). `ValidateFarmTicket` rend `ticketCount == requiredTicketCount`
(`:11766-11800`, `:11799`) : **la somme des tickets offerts doit égaler exactement la table**. Le
résultat part dans `TS_SC_RESULT_FOSTER` (6003, 8 octets, `result` à `+7`) : **1** = confiée, **0** =
refus (fiche `6002` §5.6). La table décide donc **le seul octet** que le lot `6002` écrit.

### 5.2 Ce que le dépôt fournit déjà au socle

| pièce | état sur `master` (`a9f01fb`) | sur `hermes/packet-6002-foster-creature` (`b22c65a`) |
|---|---|---|
| `ICreatureFarmTicketCost` / `CreatureFarmTicketCostRow` / `CreatureFarmTicketCost` | **absent** | `Game/Services/Creatures/CreatureFarmTicketCost.cs:16-51` (constructeur `CreatureFarmTicketCost(IReadOnlyList<CreatureFarmTicketCostRow> rows = null)`) |
| Consommateur | **absent** | `CreatureFarmDepositService.cs:47,55` (`ICreatureFarmTicketCost costs`) |
| Enregistrement DI | **absent** | `DevConsole/Program.cs:534-535` — `AddSingleton<ICreatureFarmTicketCost, CreatureFarmTicketCost>()`, donc **sans ligne** : toute clé rend 0 |
| Catalogue des invocations (`Rate`, `Form`, `CardId`) | présent | présent |
| `CreatureEnhance` (niveaux de renforcement de la carte) | présent (`CreatureCatalog.cs:18`) | présent |

**Le socle n'a donc qu'un geste à faire** : fournir les lignes au constructeur déjà écrit, au moment de
l'enregistrement. Il ne touche ni `CreatureFarmDepositService`, ni les trames, ni `CreatureFarmRules`.

Indice de plage du renforcement, mesuré sur les deux copies : `CreatureEnhance.csv` porte **6 lignes**
`enhance_level` 0..5 à l'ère 7.3 (`epic7part4/csv/CreatureEnhance.csv`, `sha256 cdcf33d4…b2b`,
identiques à l'export 9.4) — la dimension `enhance_level` du fichier client (0..5) est donc complète pour
7.3.

### 5.3 Le mode de chargement — tranché

**Décision : outil Python hors ligne → catalogue JSON committé → enregistrement au démarrage.** C'est le
précédent établi du dépôt pour toute donnée de ressource venue du client, et il faut le suivre :

| étape | précédent du dépôt | ce que le socle ajoute |
|---|---|---|
| 1. outil | `tools/export_pet_catalog.py` lit `db_pet.rdb` et écrit `DevConsole/pet-catalog.73.json` (`:1-13,29-40`) ; `tools/export_creature_catalog.py`, `export_monster_drops.py:34-35`, `export_auction_catalog.py` | `tools/export_creature_farm_costs.py` : lit `db_creaturefarm.rdb`, écrit `DevConsole/creature-farm-costs.73.json`. **Refuse** un fichier dont la taille n'est pas `128 + 4 + N × 4` et dont le produit cartésien n'est pas complet (même discipline que `export_pet_catalog.py:38-40` et `export_monster_drops.py:44-45`) |
| 2. données | 13 catalogues committés, `DevConsole/DevConsole.csproj:59-97` (`<None Update="….73.json">` + `CopyToOutputDirectory=PreserveNewest`) | `DevConsole/creature-farm-costs.73.json` committé et déclaré de la même façon. **Le CSV 9.4 ne peut pas être committé** (`data/sqlserver` est exclu de Git, `reference/README.md:21-22`) : c'est une des raisons du passage par un catalogue |
| 3. options | `Configuration/Options/CreatureCatalogOptions.cs:5-22`, `PetCatalogOptions.cs:5-13` (« exported from its own `db_pet.rdb` … read with `System.Text.Json` at startup like the other catalogues ») | `Configuration/Options/CreatureFarmTicketCostOptions.cs` : `List<CreatureFarmTicketCostRow> Rows` |
| 4. lecture au démarrage | `DevConsole/Program.cs:209-230` (`ConfigureCreatureCatalog`), `:232-239` (`ConfigurePetCatalog`) — et surtout la **branche d'absence** `:212-216` : fichier absent → options vides, aucune exception | `ConfigureCreatureFarmTicketCosts` sur le même patron |
| 5. enregistrement | `DevConsole/Program.cs:435-436` montre le patron d'une fabrique DI (`AddSingleton<ICastInterrupts>(provider => …)`) | remplacer `:534-535` de la branche `6002` par une fabrique qui passe les lignes chargées : `AddSingleton<ICreatureFarmTicketCost>(new CreatureFarmTicketCost(options.Rows))` |

**Absence du fichier** : le patron du dépôt (`Program.cs:212-216`) laisse l'application démarrer avec un
catalogue vide ; la couture rend alors 0 pour toute clé, donc **tout dépôt reçoit `result = 0`**. C'est le
comportement actuel de la branche `6002` : rien ne se dégrade, rien n'est accepté à l'aveugle.

Pourquoi **pas** l'autre mode : un lecteur `.rdb` côté serveur n'existe pas et n'a pas à être écrit. La
fiche `6002` §5.5 point 2 dit « aucun lecteur `.rdb` dans le dépôt » — c'est **vrai côté serveur**, c'est
**faux côté outils** : `tools/client73_ids.py:36-41` (`_records`, en-tête 128 o, `u32` à 128, corps à
partir de 132), `tools/export_pet_catalog.py:35-40`, `tools/export_monster_drops.py:39-46` lisent tous
des `.rdb` — hors ligne, à la main, jamais au démarrage. Le socle suit ce partage : lecture hors ligne,
catalogue au démarrage.

### 5.4 Ce que devient la validation quand la table est absente — les faits

Faits mesurés, sans choix de jeu :

1. **Avec `0` pour toute clé, aucun dépôt ne passe.** `ValidateFarmTicket` exige
   `somme(tickets offerts) == requiredTicketCount` (`GameMessage.cpp:11799`). Le lot `6002` refuse en
   outre une entrée de `ticket_count ≤ 0` (garde du dépôt, fiche `6002` bloc *CLAUDE.md* ; un tableau
   `T = 0` est refusé de même, A VERIFIER 4 de sa fiche) : une somme
   strictement positive ne peut donc pas égaler 0, et la somme nulle est refusée en amont. **Résultat :
   `6003` avec `result = 0` pour toute carte de créature bien formée.**
2. **C'est le comportement de la référence**, pas une panne : la clé absente y rend `0` après un
   `assert` (`GameContent.cpp:2922-2924`). Une base de l'ère 7.3 sans ligne pour une clé donnée produisait
   déjà le même refus.
3. **Ce qui change le jour où les lignes sont chargées** : le même chemin accepte exactement les cartes
   dont la clé a une ligne et dont la somme offerte égale la table. Aucun autre code ne bouge.
4. **Les options de repli** (à trancher par Killian, A VERIFIER 1) :
   - **garder le refus** — comportement actuel et celui de la référence ; la ferme reste inutilisable
     tant que les lignes ne sont pas là ;
   - **charger les 72 lignes du client** — la donnée 7.3 telle quelle ; laisse les 35 invocations de
     `form 3` du client sans prix (donc refusées) ;
   - **charger les 108 lignes de l'export 9.4** — couvre `form 3`, mais change le prix des `form 2` et
     importe une ère plus tardive ;
   - **compléter les 72 lignes du client par les seules lignes `form 3` de l'export** — un **mélange
     d'ères**, donc une décision de données explicite, à écrire dans la fiche si elle est prise.
5. **Rien de tout cela n'est observable comme un bug** : le client ne teste jamais l'octet `result`, il le
   remet à son interface (fiche `6002` §5.6 et §7 point 4).

---

## 6. Écarts assumés

### 6.1 Avec NGemity

**Aucun écart** : NGemity n'a rien sur cette table (`grep -rli creaturefarm` → 0 fichier). Le paquet
lui-même y est modélisé (`shared/Server/Packets/GameClient/…`), mais aucune constante de coût, aucune
table, aucun portage possible. NGemity ne tranche donc rien ici.

### 6.2 Entre la source 2015 (ère 9.x) et l'ère 7.3

| point | source 2015 | socle 7.3 | pourquoi |
|---|---|---|---|
| Type des colonnes | `char` lié en `adTinyInt` (`CreatureFarmLoader.cpp:12-21`) — soit 1 octet signé | `int` : le DDL du dépôt dit `int` (`ArcadiaSchemaPSQL.sql:47-51`), le client écrit 1 octet | la largeur de la référence borne le **domaine accepté** (0..255) ; `int` évite les surprises de `char` signé en C#, sans changer le comportement |
| Stockage | vecteur de pointeurs, balayage linéaire, premier match | dictionnaire `(rate, form, enhance_level)` | même résultat pour des clés uniques (mesuré : elles le sont) ; le dépôt doit **refuser** un doublon au lieu de dépendre de l'ordre |
| Clé absente | `assert` **puis** `0` | `0` **sans** `assert` (l'assert est une garde de développement de la référence, pas une règle de jeu ; l'imposer ferait tomber le serveur sur une donnée incomplète) | la fiche `6002` §6.2 trie déjà ce qui se porte de la source 2015 et ce qui ne se porte pas |
| Défaut absent | aucun | aucun | identique |
| Fraîcheur | 9.x | 7.3 | l'ère 7.3 est la cible ; l'export 9.4 ne sert qu'à recouper |

### 6.3 Avec l'export 9.4 du dépôt

L'export 9.4 est une copie **peuplée et complète** (108 lignes) de la table, disponible localement, et
`reference/README.md:23-25` nomme même `CreatureFarmResource` parmi les tables pour lesquelles « aucune
donnée disponible » n'est plus un motif de blocage. Il ne faut **pas** l'importer telle quelle dans le
lot 7.3 pour autant :

- **36 lignes de plus** : le bloc `form 3`, absent de toutes les copies 7.3 (le client n'en price aucune) ;
- **48 lignes divergentes** : tout `form 2` (`7 8 8 9 9 10` au lieu de `3 6 6 9 9 12`) et tout `form 1` de
  `rate 3..5` (`1 2 2 3 3 4` au lieu de `2 3 3 4 4 5` et `3 4 4 5 5 6`) — 48 des 72 clés, mesuré.

C'est exactement le genre d'« artefact d'ère plus tardive » que le dépôt écarte déjà ailleurs (les cages
de `PetResource`, `export_pet_catalog.py:4-5`). **La copie retenue pour 7.3 est celle du client.**

### 6.4 Avec la fiche `6002` — deux affirmations corrigées

| affirmation de `6002` §5.5 | ce que la mesure montre |
|---|---|
| « **Aucune donnée dans le dépôt ni dans les références de logique** : la table n'y existe qu'en déclaration DDL » (§5.5 point 1), et « `db_creaturefarm.rdb` est le seul exemplaire local » (§7.5) | **Faux depuis le 29 septembre 2026** : l'export 9.4 est dans les données du dépôt, et `reference/README.md:23-25` **nomme cette table**. Deux copies locales lisibles, pas une (§3.3). La conclusion de `6002` (provenance = décision de Killian) **reste** juste ; son motif « aucune donnée » ne l'est plus |
| « **Aucun lecteur `.rdb` dans le dépôt** » (§5.5 point 2) | **Vrai côté serveur, faux côté outils** : `tools/client73_ids.py:36-41`, `tools/export_pet_catalog.py:35-40`, `tools/export_monster_drops.py:39-46` lisent des `.rdb` hors ligne. Le socle **s'appuie** sur ce précédent (§5.3) au lieu de proposer un seed SQL |

Le reste de `6002` §5.5 (les 72 lignes décodées, la correspondance octet → colonne, la ligne `rate 0 /
form 2` recoupant la table publiée) est **reconfirmé indépendamment** ici, à l'octet près.

---

## 7. `NON ÉTABLI`

1. **Quelle copie le serveur doit charger** (72 lignes client / 108 lignes 9.4 / union). Les deux copies
   sont mesurées et complètes ; choisir est une décision de données, pas de protocole. Le défaut proposé
   est la copie du client (§4). → A VERIFIER 1.
2. **Le dépôt d'une créature `form 3` à l'ère 7.3.** Faits, mesurés : le client 7.3 connaît **141**
   invocations (`db_creature.rdb`, 134 082 octets, `u32` à 128 = 141, enregistrements de 950 octets,
   `132 + 141 × 950 = 134 082` sans reste) dont **28** `form 0` (sans `card_id`, donc non déposables),
   **44** `form 1`, **34** `form 2` et **35** `form 3` ; sa propre table de coût n'a **aucune** ligne
   `form 3`. La copie 2012-11 de `SummonResource` (147 lignes) donne les mêmes 35 `form 3` avec un
   `card_id` réel (`540001`-`540079`), et l'export 9.4 les price (25 à 28 tickets). Donc soit la ferme 7.3
   refusait ces créatures, soit il manque des lignes que **ni le client ni `epic7part4/` ne portent**.
   *Question précise : la ferme 7.3 acceptait-elle un dépôt de créature de `form 3`, et si oui avec quel
   coût ?* Sans réponse, ces 35 clés rendent 0 → dépôt refusé. → A VERIFIER 2.
3. **Les lignes de la table telles que la base 7.3 de Gala Lab les portait.** Le binaire de 2012-11 les
   lit dans sa base ; l'extrait local (`epic7part4/rdu/`, `csv/`) ne contient pas la table (48 tables,
   aucune `*Farm*`). Seule la base SQL Server de Killian pourrait les donner. → A VERIFIER 3.
4. **La largeur exacte de `form` côté serveur.** Le client écrit un octet, la référence prend un `char`,
   l'ère 7.3 ne dépasse pas `form 3` dans ses données, l'export 9.4 non plus. Aucune source locale ne
   borne ce champ au-delà de « un octet ». Sans conséquence tant que la validation refuse toute valeur
   hors du domaine chargé.
5. **Le site du client qui remplit les deux comptes de la trame `6002`** — déjà `NON ÉTABLI` §7 point 3
   de la fiche `6002`, repris ici pour mémoire : le client **charge** la table, donc il peut calculer le coût,
   mais le serveur ne peut pas s'en remettre à « le client envoie exactement le coût ». Cette fiche ne
   change rien à ce point.

---

## A VERIFIER PAR KILLIAN

| # | question | ce que la fiche propose (défaut appliqué si rien n'est tranché) |
|---|---|---|
| 1 | **La provenance des lignes** : le `.rdb` du client 7.3 (72), l'export 9.4 (108), ou un mélange ? | **Le `.rdb` du client**, tel quel, 72 lignes (§4, §5.3) : c'est la donnée 7.3, complète sur `form` 1-2, et son `form 2` recoupe la table publiée. Le mélange avec les lignes `form 3` de l'export est une décision de données qui doit être écrite noir sur blanc si elle est prise |
| 2 | **Le dépôt d'une créature `form 3`** : accepté (il manque alors des lignes que nous n'avons pas) ou refusé (comportement actuel) ? | **Refusé** : c'est le comportement de la référence pour une clé absente, et les 35 invocations concernées n'ont de coût nulle part en 7.3 (§7.2) |
| 3 | **Les lignes de la base 7.3 de Gala Lab** : les as-tu (base SQL Server de l'ère 7.3) ? | Aucune action par défaut : sans elles, la copie retenue est celle du client. Si tu les as, elles tranchent A VERIFIER 2 d'un coup |
| 4 | **Le mode de chargement** : catalogue JSON hors ligne (précédent du dépôt, §5.3) ou table SQL chargée au démarrage comme la référence ? | **Catalogue JSON** : aucun lecteur `.rdb` côté serveur n'existe, 13 catalogues suivent déjà ce chemin, et le CSV 9.4 ne peut pas être committé (`data/sqlserver` est exclu de Git) |
| 5 | **Le comportement quand le catalogue est absent** : démarrer avec 0 ligne (tout dépôt refusé) ou refuser de démarrer ? | **Démarrer avec 0 ligne**, patron `DevConsole/Program.cs:212-216` : la ferme refuse les dépôts au lieu de faire tomber le serveur |

---

## 8. Commits et empreintes épinglés

| référence | empreinte |
|---|---|
| Navislamia, base du lot | `master` = `a9f01fbad8437e0f52eb10089c37b13552f69b15` |
| lot dont le socle dépend | `hermes/packet-6002-foster-creature` = `b22c65ab3c2ecd3a0d30601d8763dbed8b8d2174` — couture posée par `f8b0cc9`, base `f4f3f109d2014285540554211d9bcb13e6abfb32` ; **non ancêtre de `master`** |
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` — `librzu/src/packets/GameClient/TS_CS_REQUEST_FARM_INFO.h`, `TS_CS_FOSTER_CREATURE.h`, `TS_SC_FARM_INFO.h`, `TS_SC_RESULT_FOSTER.h` (aucune structure de table) |
| NGemity | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` — `grep -rli creaturefarm` → 0 fichier |
| source officielle `GameContent.cpp` | `sha256 c297222bec0e04e7aba0398e9e0f35c32d38e0ff4f731c9ae990adc70894ae10` |
| source officielle `CreatureFarmLoader.cpp` | `sha256 fd6f5ac2cbdeef48744556ebf63045393912da0ecad2ddb4d1e77a9cd5003f7b` |
| source officielle `GameMessage.cpp` | `sha256 e7f5399ad9f2ab1e1ac0af49df6cc24774ecc1f4c2e80eee91da0130dbe5ac2` |
| source officielle `ContentStruct.h` | `sha256 96bf461f2fce56a80f1aecc90fddc78a5a2868e92be14c6d968cb98a39d554f1` (nouveau dans le jeu de pins de cette fiche) |
| source officielle `CaptainHerlockServer.cpp` | `sha256 781c845e4a79fba76fef91273272179d0dc5068a4660691f250889615d0a4809` (idem) |
| serveur ère 7.3 (2012-11) | `CaptainHerlockServer.exe` `sha256 83b54fe18f15601578e35bcad1832a5c254bb09f512ab629721fa49d9dd3eac` ; `CaptainHerlockServer.symbols.tsv` `sha256 aef7e6bc8d436a3641fa5a589b53c557ae940e6b6ed4b555a648f8aab7c7e7f3` |
| client 7.3 | `SFrame.exe` `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` |
| données 7.3 — la copie retenue | `db_creaturefarm.rdb` `sha256 36564e4a4405afa01133a4e519e6e83d5a379228ad4202ec4ed91b7d2dfdea3d` (420 octets, 72 lignes) |
| données 7.3 — la couverture des clés | `db_creature.rdb` `sha256 268e7cccd8a30d092140ffa6686c8e4683ad9eaa2f17d55859695845eede02a8` (134 082 octets, 141 invocations, 950 octets chacune) |
| recoupement ère 7.x | `epic7part4/csv/SummonResource.csv` `sha256 96415226f77bfae86303177b711dd1ee81a18e37fc18c7738ff5d79395ae6bb8` (147 lignes ; 48 tables extraites, **aucune** `CreatureFarmResource`) ; `epic7part4/csv/CreatureEnhance.csv` `sha256 cdcf33d42b1c7e1bb049ab564a14bca489b5d6ba85c4680e60809e735bdedb2b` (6 niveaux) |
| recoupement ère 9.4 | `data/sqlserver/Arcadia/CreatureFarmResource.csv` `sha256 38a55f692f02b20c82a832557a0e5a79216417d493d58380d41c48012d6cede7` (108 lignes) ; `SummonResource.csv` `sha256 549df9dbf811c50114aace627f0e3c21a67ff74c664993906eff336e51c2a07c` |

Fichiers du dépôt lus pour cette fiche : `op_codes.md:256-264`, `ArcadiaSchemaPSQL.sql:46-52`,
`Game/Network/Packets/Enums/GamePackets.cs:442-452`, `Game/Network/Clients/GameClient.cs:1102-1127,3719-3721`,
`Game/Services/Creatures/CreatureCatalog.cs:13,18`, `Configuration/Options/CreatureCatalogOptions.cs:1-22`,
`Configuration/Options/PetCatalogOptions.cs:5-13`, `DevConsole/Program.cs:209-239,435-436`,
`DevConsole/DevConsole.csproj:59-97`, `tools/client73_ids.py:1-7,36-41`,
`tools/export_pet_catalog.py:1-13,29-40`, `tools/export_monster_drops.py:34-46`,
`docs/packet-specs/6002-foster-creature.md` §5.5, §5.6, §7.5, `docs/packet-specs/socle-ferme-creatures.md`,
`docs/packet-specs/socle-ferme-creatures-officielle.md:391`, `reference/README.md:13-26,29-38`.

---

## Bloc pour CLAUDE.md

Bloc à recopier dans la description de la MR du lot (le dev n'écrit **pas** `CLAUDE.md`, le fichier est
protégé) :

```markdown
### Ferme de créatures — la table de coût en tickets (`CreatureFarmResource`)

- La table de coût (`rate`, `form`, `enhance_level` → `ticket_count`) n'est **pas** dans une trame : c'est
  la ligne de la carte de créature, et `ValidateFarmTicket` exige que la somme des tickets offerts
  l'égale **exactement**. Clé absente = **0** = dépôt refusé (`6003`, `result = 0`).
- **Deux copies locales, pas une** : le `.rdb` du client 7.3 (`reference/client73/db_creaturefarm.rdb`,
  420 octets, en-tête 128 o + `u32` = 72 à l'offset 128 + 72 enregistrements de 4 octets, 72 clés
  complètes sur `form` 1-2, valeurs 1..18) et l'export 9.4 déjà présent
  (`data/sqlserver/Arcadia/CreatureFarmResource.csv`, 108 lignes, `form` 1-3 — une ère plus tardive, dont
  le `form 2` vaut 7..10 au lieu de 3..12 : **ne pas l'importer** en 7.3).
- Chargement : outil hors ligne `tools/export_creature_farm_costs.py` → `DevConsole/creature-farm-costs.73.json`
  (committé, déclaré dans `DevConsole.csproj`) → lue au démarrage en `System.Text.Json`, comme les autres
  catalogues ; fichier absent = pas de ligne = tout dépôt refusé, jamais d'exception.
- 35 des 141 invocations que le client 7.3 connaît sont `form 3` et sa propre table n'en price aucune : la
  question « la ferme 7.3 acceptait-elle une `form 3` ? » reste ouverte (A VERIFIER 2).
- Savoir complet : `docs/packet-specs/socle-cout-tickets-ferme.md`.
```
