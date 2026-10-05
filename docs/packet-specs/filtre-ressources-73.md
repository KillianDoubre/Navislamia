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
- les catalogues JSON (par famille de clé), le Lua embarqué et les entiers littéraux du code C#.

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

Les protections « littéral » sont larges (tout entier de 3 à 10 chiffres du code) : elles gardent quelques lignes de
trop, jamais une de moins.

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
`import_epic7.py`, qui réinsère les lignes Epic 7 hors client, rejouer le filtre. Sauvegardes du 2026-10-05 :
`A:\Rappelz Kiff\backups\{Arcadia,Telecaster}-2026-10-05-avant-filtre-73.dump`.

## NON ÉTABLI

- Les lignes gardées par un littéral du code : à vérifier une à une si l'on veut une base strictement 7.3.
- `StringResources` (102 256 lignes Epic 7) n'est pas filtrée : un texte de trop ne s'affiche jamais.
- Les instances 50000-70000 restent déclarées sans monstre : leur entrée est-elle seulement possible en 7.3 ?
