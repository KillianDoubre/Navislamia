# Équipement d'une invocation — `TM_CS_PUTON_ITEM` (200) / `TM_CS_PUTOFF_ITEM` (201) sur un handle d'invocation

Aucun paquet nouveau : `onPutonItem` / `onPutoffItem` du serveur officiel acceptent pour `target_handle` le
handle d'une invocation du joueur (`pClient->GetSummon(target_handle)`, sinon `RESULT_ACCESS_DENIED`).
Sources : `GameMessage.cpp` (`onPutonItem` l. 696, `onPutoffItem` l. 788), `StructSummon.cpp`
(`TranslateWearPosition` l. 482, `putonItem` l. 1119, `putoffItem` l. 1175), `StructCreature.cpp`
(`TranslateWearPosition`, `Puton`, `putonItem`), `CalculateStat.cpp` l. 5963.

## 1. Ce qu'une invocation peut porter

`StructSummon::TranslateWearPosition` :

1. la règle de base (`StructCreature::TranslateWearPosition`) : un objet qui se porte (`wear_type ≠ -1`), au
   **niveau de l'invocation** (plancher de rang, `use_min_level`, `use_max_level`) ;
2. l'objet doit être **en forme de carte** : `ITEM_FLAG_CARD`, bit 0 du champ `flag` — c'est le bit que le
   mélange 311 (`MIX_ADD_LEVEL_SET_FLAG`) allume et éteint. Un joueur, lui, refuse un objet en forme de carte ;
3. les emplacements valent `0 .. slot_amount − 1`, où `slot_amount` vient de `CreatureEnhance` à
   l'amélioration de la carte (2 à +0) :
   - position **24** (`MAX_ITEM_WEAR`) : le serveur choisit. Équipement → emplacements 0-1, accessoire (groupe 9)
     → emplacement 2 (s'il existe), artefact (groupe 130) → 3 et au-delà. Le premier libre est pris ; un
     équipement ou accessoire du **même groupe** déjà porté dans la plage refuse ;
   - autre position : acceptée telle quelle si elle est sous `slot_amount`, l'objet qui l'occupe est retiré.

Refus : `NotActable` (5). Cible qui n'est pas une invocation du joueur : `AccessDenied` (6). Objet inconnu :
`NotExist` (1).

## 2. Ce que le serveur renvoie

Comme `StructCreature::Puton` : la 287 (`TS_SC_ITEM_WEAR_INFO`) de l'objet retiré (position -1) puis de l'objet
porté, **`target_handle` = handle de l'invocation**, les deux 1000 de l'invocation (total, objets), puis le
`TS_SC_RESULT` de 200. Le retrait (201) : la 287 de l'objet (-1), les stats, le résultat ; un emplacement vide
répond `NotExist`. Aucune 202 : elle décrit le modèle du joueur.

## 3. Persistance et séparation d'avec le maître

L'objet garde son emplacement d'invocation dans `Items.WearInfo` (0, 1, 2…) et `EquippedBySummonId` = id de la
ligne `Summons`. Ces valeurs se lisent comme l'arme, le bouclier, l'armure… du joueur : **toute lecture côté
joueur passe par `ItemWearRules.IsWornByPlayer` / `IsWornByPlayerAt`** — trame 202, stats, arme équipée, échange
223, retrait, butin de mort PK, écran de sélection, revalidation de connexion.

Dans l'inventaire, un objet d'invocation part **non porté** (position -1, propriétaire 0) : son propriétaire est
un handle de monde que le client n'apprend qu'avec la 301 de l'invocation. À l'entrée en jeu, `CreatureService`
envoie après les 301 la 287 de chaque objet porté par une invocation formée, qui le place sur elle.

Les options des objets portés entrent dans les stats de l'invocation (`StatsOf`), comme celles du maître.

## 4. Routage

Le choix se fait dans `GameClient` : `target_handle` ni nul ni égal au personnage → `ICreatureService`.
`EquipmentService` ne peut pas dépendre des créatures : le cycle combat → butin de mort → équipement →
créatures → combat n'échouerait qu'au démarrage (attrapé par `Tests/Host/ServiceGraphTests.cs`).

## 5. NON ÉTABLI et écarts

- Le contrôle de l'artefact sur le code d'invocation (`pItem->GetSummonCode()`) n'est pas porté : aucune
  instance d'objet ne porte ce code ici.
- Le script `on_equip_item` d'un objet n'est pas exécuté (aucun Lua).
- Le retrait officiel d'un objet non `WEAR_SUMMON_ONLY` passe par son `wear_type` ; ici la position envoyée est
  prise comme emplacement absolu, ce que le client envoie pour une invocation (à confirmer en jeu).
- Une invocation non formée n'a pas de handle : ses objets restent affichés non portés jusqu'à sa formation.
