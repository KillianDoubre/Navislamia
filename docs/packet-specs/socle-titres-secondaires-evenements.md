# Titres secondaires, événements d'invocation et d'artisanat, création d'objets (601)

**Suite du 2026-10-04** : événements de siège vérifiés, meurtre PK et PC bang raccordés.
Voir `socle-titres-siege-pk-pcbang.md`.

Suite de `socle-progression-monstres-quetes-titres.md`. Sources officielles : `StructPlayer.cpp`
(`SetMainTitle`, `SetSubTitle`, `IsUsableTitle`), `CalculateStat.cpp` (`applyStatByTitle`,
`amplifyStatByTitle`), `StructTitleManager.cpp`, `TitleBase.h`, `GameRule.h`, `GameProc.cpp` (`ProcTame`),
`MixManager.cpp` (`CreateItem`), `GameMessage.cpp` (`onMix`).

## 1. Pas de paquet de titre en 7.3

Le serveur officiel a `TM_SC_TITLE_LIST` (625) à `TM_SC_SET_SUB_TITLE` (631), mais le `SFrame.exe` 7.3 n'en
nomme **aucun** (ni `TITLE_LIST` ni `MAIN_TITLE` dans ses chaînes). Les titres restent donc pilotés par les
commandes de chat : `/titles`, `/title <id|0>`, `/subtitle <emplacement 1-5> <id|0>`.

## 2. Titres secondaires

- Cinq emplacements (`SUB_TITLE_COUNT`). Un titre secondaire doit être possédé, disponible, ne pas être déjà
  porté (principal ou secondaire, `IsUsableTitle`) et de rang **5 au plus** (`SUB_TITLE_RATE_LIMIT`, colonne
  `rate` de `TitleResource`, désormais exportée).
- Il donne **10 %** de ses options (`SUB_TITLE_RATE` = 0,1) : `applyStatByTitle` multiplie `var2` des options
  `IncParameterA`/`AmpParameterA` par 0,1. Le bogue d'indice de `applyTitleEffect` (la boucle des secondaires lit
  `nOptType[i]`, l'indice de l'emplacement) n'est pas reproduit : chaque option compte à 10 %.
- Persistance : `Characters.SubTitleIds` (migration `Version0015_SubTitles`), relu par `StatService.Seed`.
- Titre principal : **5 minutes** entre deux changements d'un titre porté, retrait compris
  (`TITLE_RESET_COOL_TIME` = 30 000 ticks). Le délai est tenu en session (`MainTitleLockedUntil`) ; l'officiel le
  garde dans `m_tRemainTitleTime`, dont la persistance n'est pas établie.

## 3. Événements de titre

`TitleEvents` traduit les `UpdateTitleConditionBy…` officiels ; `UpdateRelatedTitleCondition` compare les trois
valeurs exactement, un type `is_set` prend la valeur au lieu de l'ajouter.

| Catégorie | Officiel | Déclencheur ici |
|---|---|---|
| 3001 / 3002 | `BySummonTame(code, rang, réussite)` | tirage de `ProcTame` (`CreatureService.CommitTamingAsync`) |
| 3101 / 3102 | `BySummonEquip` (formation) | **recalculé** : nombre de cartes formées du code/rang à l'amélioration demandée au moins |
| 3301 | `BySummonMount` | **recalculé** : l'invocation montée |
| 3901 / 3902 | `BySummonCardGet` | **recalculé** : cartes détenues (code ou rang, liée ou non) |
| 2201 | `ByItemUse` | `ItemUseService`, après l'acquittement |
| 2401 | `ByItemCreateByMixing` | mélange 601 (§4) |

Les catégories recalculées sont toutes `skip_db_update` dans les données (colonne désormais exportée) : leur
compteur n'est pas une histoire mais l'état courant, ce que le recalcul à chaque rafraîchissement (connexion,
formation, montée, apprivoisement) rend exactement. Hors périmètre : 3201/3202 (amélioration de carte de
créature, mélanges 104/105 non portés), 3801 (ceinture de cartes, absente), 2001 (objet obtenu, trop de chemins),
5201/5202 (sièges), 6002 (meurtres PK), 9000 (PC bang).

## 4. `MIX_CREATE_ITEM` (601)

2 610 recettes Epic 7 — la famille d'artisanat la plus nombreuse, et la seule qui alimente la catégorie 2401.
`MixManager::CreateItem` :

- le matériau principal part **en entier**, chaque matériau secondaire à son compte ;
- `mix_value_03` % de chances (`value[2] > XRandom(0, 99)`), sinon tout est consommé pour rien ;
- un compte tiré dans `[mix_value_04, mix_value_05]` ; `mix_value_01` positif donne ce compte de l'objet,
  négatif (1 288 recettes) tire **autant de fois** le groupe de butin, chaque tirage avec son propre compte
  (`SelectItemIDFromDropGroup`, membres et poids de `monster-drops.73.json` — les 190 groupes utilisés y sont) ;
- les objets sont créés au niveau `mix_value_02`, `GenerateBySource = Mix`, en une sauvegarde avec la
  consommation (`ApplyCraftWithCreationAsync`) ; réponse 254/255 par pile, 207 des objets créés, 257 avec le
  dernier objet créé ou vide.

Écarts : un objet non empilable n'est pas découpé en unités (les recettes concernées donnent 1) ; la ligne de
discussion `@254` n'est pas envoyée (les `NameId` d'objets ne sont pas fiables ici) ; 605 (`IN_MASS`) n'a aucune
recette Epic 7.
