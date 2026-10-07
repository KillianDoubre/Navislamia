# Filtre des ressources : ne garder que le 7.3 (2026-10-05)

Arcadia mêlait les tables Epic 7 (`tools/import_epic7.py`) et des lignes venues seulement de l'export 9.4. Une ligne
que le client 7.3 ne connaît pas donne un objet, un monstre ou un PNJ invisible, une compétence sans nom ou un état
sans icône. Elles ont été retirées.

## 1. Ce que « 7.3 » veut dire, table par table

**La référence est le client lui-même** quand il a sa table, l'export Epic 7 sinon. Le client vaut mieux que l'export
Epic 7 : par exemple, 108 compétences et 102 PNJ que le client connaît n'existent que dans l'export 9.4.

| Table | Référence | Lecture |
|---|---|---|
| `ItemResources` | `db_item.rdb` (28 265) | `tools/client73_ids.py` |
| `SkillResources` | `db_skill.rdb` (2 168) | idem |
| `MonsterResources` | `db_monster.rdb` (6 553, id brouillé comme `monster_id`) | idem |
| `NpcResources` | `db_npcresource.rdb` (1 185) | idem |
| `SummonResources` | `db_creature.rdb` (141) | idem |
| `QuestResources` | `db_quest.rdb` (956) | idem |
| `StateResources` | `StateResource` de l'export Epic 7 | pas de table d'états dans le client |
| `StatResources` | `StatResource` de l'export Epic 7 | table serveur seulement |

Format des tables du client (mesuré) : en-tête de 128 octets, `u32` de compte, enregistrements de taille fixe dont le
premier champ est l'id ; `db_monster.rdb` commence à 132 et compte par taille (540 octets). Extraction en lecture
seule : `tools/provision-navislamia/extract_client.py <client> <dossier>`.

## 2. Ce qui est gardé malgré tout

Une ligne hors du jeu 7.3 reste si quelque chose pointe encore vers elle (`tools/prune_to_client73.py`) :

- une ligne gardée d'une autre table : compétence, état ou invocation d'un objet, effet « compétence » (5) ou
  « état » (6) d'un objet, récompenses et cibles d'une quête, carte, statistiques, compétences et évolution d'une
  invocation, équipement et statistiques d'un PNJ, statistiques d'un métier ou d'un monstre, matériau d'amélioration ;
- Telecaster : objet porté par un personnage, compétence apprise, état sauvegardé, invocation, quête en cours ou
  terminée, objet payant, objet de départ ;
- les catalogues JSON par famille, les arguments Lua typés dans les fonctions accessibles depuis les PNJ client, menus, événements et quêtes, et les constantes C# explicitement justifiées.

Résultat de l'application (base locale, 2026-10-05) :

| Table | Avant | Retirées | Après | Gardées hors 7.3 |
|---|---|---|---|---|
| `ItemResources` | 33 146 | 4 892 | 28 254 | 60 (44 littéraux, 12 matériaux d'amélioration, 3 équipements de PNJ, 1 récompense) |
| `SkillResources` | 2 721 | 550 | 2 171 | 3 (littéraux) |
| `MonsterResources` | 7 431 | 850 | 6 581 | 28 (littéraux) |
| `NpcResources` | 1 456 | 261 | 1 195 | 13 (littéraux) |
| `SummonResources` | 147 | 3 | 144 | 3 (évolutions, carte) |
| `QuestResources` | 765 | 2 | 763 | 0 |
| `StateResources` | 1 950 | 371 | 1 579 | 50 (40 états de compétences gardées, 10 littéraux) |
| `StatResources` | 3 899 | 2 979 | 920 | 36 (littéraux) |
| `QuestLinkResources` | 908 | 7 | 901 | — |

Ce premier résultat utilisait une protection numérique trop large, remplacée par l’audit §5.

## 3. Catalogues filtrés à la source

- `auction-catalog.73.json` : les enchères automatiques (table 9.4) ne gardent que les objets du client (39 → 4).
  `tools/export_auction_catalog.py` les filtre désormais avec `--client-items`.
- `dungeon-resources.json` : les apparitions d'instance de monstres inconnus du client sont retirées (1 654 → 760).
  Les instances 50000, 60000 et 70000 n'avaient **aucun** monstre connu du client ; 40000, 41001, 42001 et 43001 en
  gardent une partie. `tools/export_dungeon_resources.py --client-monsters db_monster.rdb` filtre désormais à
  l'export.
- Déjà filtrés auparavant : apparitions de monstres, butin, marchés, familiers.

## 4. Rejouer

```powershell
pg_dump -Fc -f Arcadia-avant.dump Arcadia      # aucune annulation ligne à ligne
python tools/provision-navislamia/extract_client.py "A:\Rappelz Kiff\Epic_7_3" <dossier>
python tools/prune_to_client73.py <dossier>            # le plan
python tools/prune_to_client73.py <dossier> --apply    # une transaction
```

`NAVIS_EPIC7` désigne le dossier `data/epic7` s'il n'est pas sous le dépôt courant. Après un nouvel
`import_epic7.py`, le filtre est désormais exécuté automatiquement (§6). Sauvegardes du 2026-10-05 :
`A:\Rappelz Kiff\backups\{Arcadia,Telecaster}-2026-10-05-avant-filtre-73.dump`.

## NON ÉTABLI

- ~~Les instances 50000-70000 restent déclarées sans monstre : leur entrée est-elle seulement possible en 7.3 ?~~
  Non : 40000-70000 sont du contenu 9.4, retiré le 2026-10-07 (`socle-donjons-instances-secrets.md`, fin).

## 5. Audit individuel et chaînes client (RlwjZDsY)

Les [134 décisions](audit-litteraux-73.md) sont aussi disponibles dans
`audit-litteraux-73.json`, vérifiées par `tools/tests/test_client73_prune.py`.
**99 lignes supprimées, 35 objets conservés**. Une comparaison d’id monstre ne rend pas
ce monstre accessible ; un opcode, une coordonnée ou un numéro de ligne ne protège
aucune ressource. `2012644` est une base de calcul (`rank + 2012644`), seuls les résultats
2012646–2012651 sont des objets accessibles. Les fonctions Halloween 2013 n’ont aucun
point d’entrée publié. Les comparaisons d’objets autorisés et récompenses du changement
aléatoire sont typées : Lua officiel `NPC_ItemUP.lua:109,152-177` ; la pierre de race
est utilisée par `NPC_JobChange.lua:95,123,150`. Chaque objet conservé cite son Lua officiel.
Les branches du déclencheur des monstres sont limitées aux monstres client/catalogue ;
`respawn_near_monster(handle,code,count)` utilise le deuxième argument
(`Game/Scripting/ScriptService.cs:58-63`), pas les sélecteurs `monster_id`.

| Table | Avant audit | Supprimées | Après |
|---|---:|---:|---:|
| ItemResources | 28254 | 9 | 28245 |
| SkillResources | 2171 | 3 | 2168 |
| MonsterResources | 6581 | 28 | 6553 |
| NpcResources | 1195 | 13 | 1182 |
| StateResources | 1579 | 10 | 1569 |
| StatResources | 920 | 36 | 884 |
| StringResources | 102256 | 0 | 102256 |

SummonResources (144), QuestResources (763) et QuestLinkResources (901) inchangées.
Les dépendances de lignes gardées restent protégées, y compris les cases d’effets nulles
qui doivent conserver leur position par rapport à `BaseVar1`/`OptVar1`.

`db_string.rdb` : format mesuré, compte u32 à l’offset 128, puis deux longueurs u32,
nom et texte avec NUL, code i32 et cinq i32. Le fichier extrait contient **127840 codes**
déclarés ; un fragment de 2804 octets après ces enregistrements est ignoré. Le lecteur
rejette les troncatures, longueurs invalides et NUL absents. Toutes les 102256 chaînes
Arcadia actuelles appartiennent déjà à cet ensemble. Aucun texte ajouté n’est inventé.
Une chaîne hors ensemble est supprimée ; ses FK facultatives sont mises à NULL dans la
même transaction. Une FK obligatoire vers une chaîne inconnue interrompt le filtre
avant toute écriture et exige un audit. Le SQL officiel charge les codes depuis
`Game/Resource/StringLoader.cpp:53-60` ; le format binaire ci-dessus est mesuré sur
le client 7.3, sans prétendre le déduire de ce chargeur SQL 2015.

Avant suppression, sauvegarde `pg_dump -Fc` :
`A:\Rappelz Kiff\backups\Arcadia-2026-10-05-codex-avant-audit-175627.dump`.
SHA256 `7672751990bf25bb76af6f89bbd910459ba6e36943ff60d333ccbcaaf0a4480d`.
Restauration effective avec `pg_restore --exit-on-error` dans une base temporaire,
comparaison des comptes et empreintes de toutes les lignes des **27 tables** : identiques.
Base temporaire supprimée ensuite. Application dans une transaction ; second plan :
**0 suppression**, filtre idempotent.

Les 35 objets hors client conservés suivent encore les fonctions publiées ; leur rendu
côté 7.3 et les événements auxquels Killian souhaite réellement donner accès restent
à vérifier en jeu. L’audit garantit leur référence sémantique, pas leur affichage client.

## 6. Import suivi du filtre obligatoire (lbQQRm8S)

`tools/import_epic7.py` appelle `prune_to_client73.main` après l’import, la résolution
des FK et le retrait des tables intermédiaires, même si une seule table est importée.
Les règles restent celles du §5, avec leurs références Lua officielles ; aucune règle
issue d’un serveur tiers. Fournir `--client-dir` ou `NAVIS_CLIENT73` ; `NAVIS_EPIC7`
sert aux deux étapes. Les RDB client et CSV StateResource/StatResource sont validés
avant la première écriture. Une erreur du filtre fait échouer la commande ; il n’existe
aucun chemin de succès sans filtrage. Attention : l’import historique effectue plusieurs
transactions ; une erreur tardive ne les annule pas. Corriger puis rejouer après sauvegarde.

```powershell
python tools/import_epic7.py --client-dir <dossier-client-extrait> --plan
python tools/import_epic7.py --client-dir <dossier-client-extrait>
python tools/import_epic7.py --client-dir <dossier-client-extrait> SkillResources StateResources
```

`--plan` ne supprime aucune table intermédiaire, ne résout aucune FK et appelle le
filtre sans `--apply`. Il décrit l’import puis le filtre **sur la base actuelle**, sans
simuler les lignes qui seraient importées. Contrôle local sur les CSV Epic 7 et les
RDB 7.3 : plan complet réussi, filtre final 0 suppression. Quatre tests supplémentaires
vérifient l’ordre des étapes, l’import partiel, le rejet précoce d’un client invalide,
l’échec du filtre et l’absence d’écriture en mode plan ; le test existant sur les tables
intermédiaires vérifie aussi l’appel final sans `--apply`.
