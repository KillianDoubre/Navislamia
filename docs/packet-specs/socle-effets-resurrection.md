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
`OptTypes` (0 ligne sur 33 142), et `ItemResources.SkillId` est vide partout. **La première version de
cette fiche en concluait que le lien « parchemin → compétence » manquait : c'était faux.** Le lien est
dans l'emplacement d'effet, déjà importé. Le Parchemin de résurrection (603002) porte
`opt_type_0 = 5` (`ItemEffectInstant.Skill`), `opt_var1_0 = 6001` (la compétence) et
`opt_var2_0 = 1` (son niveau) ; NGemity lit exactement ces deux variables
(`Unit::onItemUseEffect`, cas `ITEM_EFFECT_INSTANT::SKILL` : `CastSkill(var1, var2, …)`). Le
`skill_id` du SQL Server 9.4 ne sert qu'aux cartes de compétence (groupe 10).

Les parchemins de résurrection de créature (608406, 920001, …) pointent vers 6013, qui ne vise que
les invocations : `tf_avatar = 0`, `tf_summon = 1`. 6001 a `tf_avatar = 1`. Cette colonne n'avait
jamais été importée (`UseOnCharacter` était rangé parmi les colonnes « absentes de la source » alors
qu'elle s'appelle `tf_avatar`, de même que `UseOnMonster` ↔ `tf_monster`) ; elle l'est désormais.
Sur les données réelles, un seul objet résout vers une résurrection de personnage : **603002**.

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

### Lot R2 — voie objet (livré)

Décision de Killian (2026-09-23) : **consommer un objet de résurrection** du sac. NGemity laisse la
branche `use_potion` vide ; ce lot applique son chemin d'objet (`Player::UseItem` →
`ITEM_EFFECT_INSTANT::SKILL` → `SKILL_RESURRECTION`) au mort lui-même.

- `ResurrectionItemCatalog` (figé au démarrage) : un objet de résurrection est un objet dont un
  emplacement d'effet vaut `ItemEffectInstant.Skill` (5) et dont `var1` désigne une compétence
  504/30501 qui vise un personnage (`UseOnCharacter`). Emplacements visités dans l'ordre de
  NGemity (`base_type[i]` puis `opt_type[i]`), le premier gagne ; le niveau est `var2`.
- `CharacterService.ConsumeFirstAsync` : le premier objet correspondant du sac (ordre `Idx`, objets
  portés exclus) perd une unité, recherche et retrait dans la même opération verrouillée.
- `ResurrectionRules.VitalsBySkill`, formules de `Skill.cpp` : 504 = `PV max × var0 × niveau`,
  `PM max × var1 × niveau` ; 30501 = `PV max × (var0 + var1 × niveau)`,
  `PM max × (var2 + var3 × niveau)` (termes d'enchantement nuls). PV planchés à 1, PM ajoutés à ceux
  gardés, bornés aux maxima — mêmes bornes que la voie état.
- Réponse : `TM_SC_UPDATE_ITEM_COUNT` (255) ou `TM_SC_DESTROY_ITEM` (254) pour la dernière unité,
  puis les propriétés `hp`/`mp`, puis `TS_SC_RESULT(513, Success)`. **Sur place**, sans `Warp`. Sans
  objet de résurrection : `NotActable`, rien ne change.
- La consommation attend la base alors que le personnage est encore à 0 PV : un indicateur de session
  (`ConnectionInfo.ResurrectionInProgress`) refuse en `NotActable` une seconde demande arrivée
  pendant ce temps, sinon deux demandes groupées consommeraient deux parchemins.

Avec le parchemin 603002 (6001, niveau 1, `var0 = 0.1`, `var1 = 0`) : **10 % des PV max**, PM
inchangés. Chemin de test : `/item 603002`, `/die`, bouton « ressusciter avec un objet ».

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
| 2 `RT_UsePotion` | — | **livrée** (513 lu, 255/254, `hp`/`mp`, 513 résultat) |
| 3 `RT_Compete` | duels (4500-4506), voir `socle-arenes-bataille.md` | — |
| 4 `RT_Deathmatch` | instances de match à mort (4250), voir `socle-arenes-bataille.md` | — |

## NON ÉTABLI

- Quelle trame ferme la fenêtre de mort du client après une résurrection par état : la référence
  n'envoie que le résultat 513 et les PV/PM, c'est ce que fait ce lot.
- Si le client 7.3 n'affiche le bouton « par état » qu'en présence d'un état 109 (probable, non vérifié).
- Si le client 7.3 connaît l'objet 603002 et n'affiche le bouton « par objet » qu'en sa présence
  (le `db_item.rdb` du client n'est pas décodé).
- Si la réapparition par objet rend aussi l'expérience perdue : `SKILL_RESURRECTION` la laisse en
  `@todo`, ce lot ne rend rien.

## A VERIFIER PAR KILLIAN

1. En jeu : `/buff 13472`, `/die`, bouton « ressusciter par état » → retour sur place avec 5 % des PV.
2. En jeu : `/item 603002`, `/die`, bouton « par objet » → retour sur place avec 10 % des PV, un
   parchemin de moins.
