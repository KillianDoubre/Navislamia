# Socle — effets de résurrection (`TM_CS_RESURRECTION` 513, voies 1 et 2 ; sorts 504/30501)

Epic 7.3. Base : `master` `a1c4950`, 2026-09-23. Débloque les voies `RT_UseState` (1) et
`RT_UsePotion` (2) de la carte 513, que le socle « mort et réapparition »
(`socle-mort-respawn.md` §12.2) laissait refusées.

## 1. Constats du PO, vérifiés

| Constat | Vérification sur `a1c4950` |
|---|---|
| `SkillEffectType.Resurrection` (504) et `ResurrectionWithRecover` (30501) déclarés, jamais référencés | Exact. `Resurrection = 504` est à `SkillEffectType.cs:108`, `ResurrectionWithRecover = 30501` à `:255` (la carte citait `:255` pour les deux). |
| `ItemEffectInstant` consommé par personne | Exact : aucune occurrence de `ItemEffectInstant.` hors de l'énumération. |
| `ItemUseCatalog` 44 lignes, `ItemUseRules` 35 lignes, aucun effet d'objet | Exact. |
| Aucun état de résurrection branché ; `StateTimeType.EraseOnResurrect` sans usage | Exact. |

## 2. Ce que fait la référence

rzu fixe le fil : `TS_CS_RESURRECTION.h` — `handle` (`ar_handle_t`, `>= EPIC_4_1`) puis `type`
(`TS_RESURRECTION_TYPE`, `int8`, `>= EPIC_6_1`), valeurs `RT_UseNone 0`, `RT_UseState 1`,
`RT_UsePotion 2`, `RT_Compete 3`, `RT_Deathmatch 4`. Id 513 sous `EPIC_9_6_3`. Aucune logique.

NGemity fixe la logique (`WorldSession::onRevive`, compilé `EPIC_4_1_1`, donc sur la paire
pré-6.1 `use_state`/`use_potion` qu'il faut traduire en `type`) :

- **`use_state`** → `Unit::ResurrectByState()` puis `SendResult(513, résultat, 0)`. Refus
  `NOT_ACTABLE` si l'unité n'est pas morte ou ne porte aucun état de résurrection.
- **`use_potion`** → **branche vide** (`else if (pRecvPct->use_potion) {}`) : NGemity ne fait rien.
- sinon (ville) → Lua `revive_in_town(type)`, déjà porté par le socle mort-respawn.

`Unit::ResurrectByState` (`Unit.cpp`) :

1. parmi les états actifs, garder celui d'effet `SEF_RESURRECTION` (**109**, `StateBase.h:317`) de
   **plus haut niveau** ;
2. PV rendus `= (value_0 + value_1 × niveau) × PV max`, PM rendus `= (value_2 + value_3 × niveau) × PM max`
   (ratios, pas des pourcentages) ; l'expérience n'est pas rendue (`@todo` dans la référence) ;
3. `ClearRemovedStateByDeath`, sauvegarde, retrait du drapeau de mort ;
4. **retrait de l'état de résurrection** (`RemoveState`) ;
5. diffusion PV/PM. **Pas de téléportation** : on revient là où on est tombé.

Les sorts `EF_RESURRECTION` (504) et `EF_RESURRECTION_WITH_RECOVER` (30501) (`Skill.cpp`
`SKILL_RESURRECTION`, `SKILL_RESURRECTION_WITH_RECOVER`) s'appliquent à une **cible morte autre que
le lanceur** et émettent un hit `SHT_REBIRTH` dans `ST_Fire`. NGemity donne aussi à chaque joueur la
compétence d'objet 6001 (`SKILL_ITEM_RESURRECTION_SCROLL`, `Player.cpp:576`) : le parchemin de
résurrection passe par une **compétence d'objet** 504, pas par `ItemEffectInstant`.

## 3. Données 7.3 (Arcadia)

États d'effet 109 (`StateResources`) :

| État | value_0..3 | Posé par | Cible du sort | Dans le catalogue 7.3 |
|---|---|---|---|---|
| 13472 | 0.05 / 0 / 0.03 / 0 | sort 3472 (301, buff) | 1 (soi) | oui, métier 112 |
| 145226 | 0 / 0.03 / 0 / 0.03 | sort 45424 (301) | 1 | non (9.4 seulement) |
| 163201 | 0 / 0.01 / 0 / 0.01 | sort 63201 (301) | 31 (invocation) | métiers 203, 214 — refusé, cible invocation |

Sorts 504/30501 (`SkillResources`) : 3205, 3220, 4202, 45408 (30501) ; 6001, 6011, 6012, 6013,
9304, 10004 (504). Tous cible 1.

**Aucun objet ne porte l'effet `ItemEffectInstant.Resurrection` (4)**, ni en `BaseTypes` ni en
`OptTypes` (0 ligne sur 33 142). Et **`ItemResources.SkillId` est vide pour tous les objets** :
l'import n'a pas repris `skill_id` (CLAUDE.md, *Database access*). Le lien « parchemin → compétence
6001/6011-6013 » n'existe donc pas dans la base locale.

## 4. Découpage

### Lot R1 — voie état (livré)

`RT_UseState` : port fidèle de `ResurrectByState`.

- `StateEffectType.Resurrection = 109` ; `IStateResourceRepository.GetStatesWithEffect` ;
  `IStateCatalog.TryGetResurrection` (figé au démarrage, valeurs `ResurrectionStateValues`).
- `ResurrectionRules.TrySelectState` (plus haut niveau) et `VitalsByState` (formule ci-dessus) ;
  `CheckRequest` accepte désormais les types 0 et 1.
- `ResurrectionService.ResurrectByState` : états actifs → état choisi → PV/PM → retrait de l'état par
  `ISkillCastService.RemoveState` (retrait `TS_SC_STATE` + rafraîchissement des statistiques, comme
  l'expiration) → propriétés `hp`/`mp` → `TS_SC_RESULT(513, Success)`. **Sur place**, sans `Warp`.
  Sans état de résurrection : `TS_SC_RESULT(513, NotActable)`, rien ne change.

Écart assumé : **PV planchés à 1**. `ResurrectByState` ne le fait pas, mais un ratio nul laisserait
le personnage mort ; le plancher est emprunté à `Unit::Resurrect` de la même référence
(`AddHealth(std::max(nIncHP, 1))`). Les PM sont ajoutés à ceux que le personnage avait gardés.

La mort ne retire aucun état dans ce dépôt, donc un état de résurrection posé de son vivant est
toujours là à la mort. Chemin de jeu réel : apprendre 3472 (métier 112), le lancer, mourir, cliquer
« ressusciter par état ». Chemin de test : `/buff 13472`, `/die`, puis le bouton.

### Lot R2 — voie objet (bloqué par les données)

Décision de Killian (2026-09-23) : **consommer un objet de résurrection** du sac. Il faut savoir
quels objets le sont. Deux sources possibles, aucune disponible aujourd'hui :

1. **Recommandé** : réimporter `ItemResource.skill_id` (et le niveau de compétence de l'objet) depuis
   le SQL Server 9.4 dans `ItemResources.SkillId`, la clé étrangère vers `SkillResources` étant
   désormais remplissable. Gain général : tous les objets à compétence en profitent.
2. Décoder `db_item.rdb` du client 7.3 (présent sur le VPS, format d'enregistrement non établi).

Une fois le lien présent : un objet de résurrection est un objet dont la compétence a l'effet 504
ou 30501. Le serveur en consomme un (`ItemRemoval`, `TM_SC_UPDATE_ITEM_COUNT`/`TM_SC_DESTROY_ITEM`
comme 253), applique la formule de `SKILL_RESURRECTION` (`PV max × var0 × niveau`,
`PM max × var1 × niveau`) ou de `SKILL_RESURRECTION_WITH_RECOVER`, ressuscite sur place, répond
513 `Success`. Sans objet : `NotActable`.

### Lot R3 — résurrection par autrui (non livrable)

Les sorts 504/30501 visent un **autre** joueur mort : il faut que les joueurs se voient, ce que le
serveur ne sait pas faire (`TS_SC_ENTER_PLAYER` n'est envoyé qu'au client qui entre). Prérequis :
visibilité joueur↔joueur. Paquets à venir alors : `TS_SC_SKILL` (`401`) `ST_Fire` avec un hit
`SHT_REBIRTH` (`hTarget`, `target_hp`, `nIncHP`, `nIncMP`, `nRecoveryEXP`, `target_mp`) dans la
foulée de 45 octets — **format du hit à établir contre rzu** avant d'écrire quoi que ce soit.

## 5. Paquets restants pour rouvrir la carte 513

| Voie | Prérequis | Paquets |
|---|---|---|
| 1 `RT_UseState` | — | **livrée** |
| 2 `RT_UsePotion` | lien objet → compétence (lot R2) | 513 (lu), 255/254, 513 résultat |
| 3 `RT_Compete` | duels (4500-4506), voir `socle-arenes-bataille.md` | — |
| 4 `RT_Deathmatch` | instances de match à mort (4250), voir `socle-arenes-bataille.md` | — |

## NON ÉTABLI

- Quelle trame ferme la fenêtre de mort du client après une résurrection par état : la référence
  n'envoie que le résultat 513 et les PV/PM, c'est ce que fait ce lot.
- Si le client 7.3 n'affiche le bouton « par état » qu'en présence d'un état 109 (probable, non vérifié).
- Le niveau de compétence d'un objet de résurrection (colonne non importée).

## A VERIFIER PAR KILLIAN

1. En jeu : `/buff 13472`, `/die`, bouton « ressusciter par état » → retour sur place avec 5 % des PV.
2. Démarrer SQL Server (`MSSQL$SQLEXPRESS`, droits administrateur) pour débloquer le lot R2.
