# Artisanat — le `MixManager` officiel (tous les types Epic 7)

Suite de `socle-artisanat-ressources.md` (§13-16) et de `socle-artisanat-cartes-competences.md`. Source :
`MixManager.cpp`, `MixBase.h`, `GameMessage.cpp` (`onMix`) du serveur officiel (Epic 7 Part 4, cp949). Les
décisions du 2026-09-29 (§14 de la fiche ressources) avaient pour règle « les défauts de NGemity » ; elles ont été
prises avant de disposer du serveur officiel. Là où il tranche autrement, c'est lui qui est porté (§1 et §3).

Données Epic 7 (`MixResources`, 3 965 lignes) : 601 (2 610), 103 (454), 805 (296), 101 (267), 803 (66),
202 (63), 402 (54), 201 (36), 702 (27), 301 (19), 311 (18), 603 (11), 806 (10), 703 (10), 212 (7), 701 (6),
804 (3), 104 (2), 501 (2), 105, 704, 214, 215 (1 chacun). **Tous sont désormais exécutés.**

## 1. Résolution — `GetProperMixInfoAndArrangeSubMaterials`

- **Arrangement, plus appariement par position.** Pour chaque condition secondaire, dans l'ordre, le serveur
  prend **le premier matériau non encore pris** qui la satisfait ; les matériaux ressortent dans l'ordre des
  conditions. L'ordre dans la fenêtre du client n'a donc plus d'importance (la §14 point 9 prévoyait de repasser
  en permutation si le client acceptait les deux ordres : l'officiel l'établit). Un premier matériau qui
  satisfait deux conditions est pris par la première : c'est un tirage glouton, pas une recherche exhaustive.
- **Conditions jugées** (`check_material_info`), en plus de 1-10, 13, 14 : 11/12 élément (élément `n` = bit
  `n − 1`, sans élément = 0), 15 quantité au moins (garde la quantité de la trame), 16/17 durabilité éthérée
  courante, 18 grade, 23 premier code de châsse, 26/27 durabilité éthérée maximale, 30 type (`ItemBaseType`),
  32 groupe différent, 35 rang d'invocation, 37 types du concepteur (1 arme, 2 armures : groupes 2 à 8).
  Après l'arrangement (`post_arrange_check_material_info`) : 19, 24, 25 comme avant, **20 même code
  d'invocation** (le `summon_id` de la ressource d'abord, sinon celui de la carte, comparé au matériau
  principal), 28/29 premier code de châsse plus grand ou plus petit, 31 même classe, 33 limite de race incluse.
- Refusées faute de colonne : 21/22 (expiration), 34 (éveil), 36/38 (drapeaux de base). Aucune recette
  Epic 7 ne les emploie.

## 2. Durabilité éthérée — convention

Rien dans ce serveur ne consomme la durabilité éthérée et aucun chemin de création ne l'initialise :
`Items.EtherealDurability` vaut 0 sur presque tout objet. **Un 0 stocké sur un objet qui a un maximum est lu comme
plein** (`MixMaterial.CurrentEthereal`), ce que l'`AllocItem` officiel lui aurait donné. Un mélange écrit la
valeur réelle. À revoir le jour où l'usure sera modélisée (initialiser la valeur à la création).

## 3. Types portés

| Type | Fonction officielle | Ce qui se passe |
|---|---|---|
| 101 / 103 | `EnhanceItem` | **tous** les matériaux consommés à leur quantité ; gain `[v1, v2]` (101) ou 1 (103), plafonné ; échec 101 = `procEnhanceFail`, échec 103 = `((rand(e × v2, e × v3) / 1000) + 5) / 10` |
| — | `procEnhanceFail` | 2 : une unité prise, copie à −3 au-dessus de +3 ; **1 : `FAILED` et châsses vidées (sauf ceinture)** ; 3 : −3, 0 à +3 ou moins ; **0, 4 : rien** |
| 104 / 105 | `EnhanceCreatureCard` | deux cartes de même amélioration, aucune formée, pas le joker 540070, aucune épuisée ; chance `pct[e] × 100 000 + (niv₁ + niv₂) × 125` (+ Friendship of Crown du joker) ; succès : +1, durabilité éthérée pleine, invocation renouvelée, titre 3201/3202 ; échec d'une carte +3 de rang ≥ 1 : `v3` à `v5` %, puis `procEnhanceFail` |
| 201, 202, 211-215, 301, 302, 311, 312 | `MixItemLevel` | niveau et amélioration posés ou ajoutés (`v0` = amélioration × 100 + niveau, 0 garde la valeur), bit `v1` allumé ou éteint par `v2`, création de `v3` à `v5` % (un seul tirage de groupe) |
| 401 / 402 | `RecycleItem` | équipement (groupe 1-9) + cube, dans un ordre quelconque ; le cube part toujours ; `v4` % : `rand(e × v2, e × v3)` de `v0` au niveau `v1` (402 en 1/10 000 arrondi), sinon l'équipement est perdu |
| 501 | `RestoreEnhance` | une poudre ; amélioration `((rand(e × v2, e × v3) / 1000) + 5) / 10` ; bit `v0` allumé ou éteint par `v1` |
| 603 | `ChangeAppearanceCode` | l'objet prend l'apparence du premier matériau (`Items.AppearanceCode`, migration `Version0016_ItemAppearanceCode`, @71 du motif de 75 octets) |
| 701 | `SetElementalEffect` | élément = `fOptVar1[0]` du matériau, durée `fOptVar2[0]` s (0 = permanent), 0 efface |
| 702 | `SetElementalEffectParameter` | points d'attaque et de magie tirés dans les deux premières options |
| 703 | `SetSocket` | la première châsse valant `v0` prend `v1` (`v2` %) ou `v3` ; remplir une châsse vide ajoute l'endurance de la pierre si `v4` ; succès : `v5` en prime |
| 704 | `ReplaceSocketWith` | le matériau `v0` prête ses châsses (`v1` %), puis est détruit (1) ou vidé (2) selon `v2`/`v3` |
| 801 / 804 | `SacrificeItemForEtherealDurability` | le premier matériau est seulement dépensé ; chaque autre rend, à `v3` %, `prix × v0 / 100 × quantité + v1` (un quart s'il est épuisé) jusqu'au plein ; bits de `v2` = matériaux gardés ; 801 annonce `@7900` |
| 805 | `…ForEtherealStoneDurability` | le même don versé dans la **pierre éthérée** du personnage (100 000 000 au plus) ; un succès répond par la ligne et **sans 257** |
| 802 | `TransmitEtherealDurability` | batterie et catalyseur (aucune recette Epic 7) |
| 806 | `…FromEtherealStone` | la pierre recharge l'objet par points entiers : `min((usé − v1) × 100 / v0, points − 1)` |
| 803 | `RecoverExhaustedEtherealDurability` | à `v2` % : `v2` % du maximum + `v0` |

`v[k]` est `mix_value_0(k+1)`. 102 et 601 sont décrits dans leurs fiches ; 111, 604, 605, 807, 808, 901 n'ont
aucune recette Epic 7 et restent refusés.

## 4. Application et réponses

Un plan porte : les piles consommées, les **mutations** d'objets qui restent (avec l'état attendu), les copies
(faites avant toute consommation), les objets créés, la variation de la pierre éthérée et des lignes de
discussion. `ICharacterService.ApplyMixAsync` applique tout en **une** sauvegarde, rien si un objet n'est plus dans
l'état vu par le plan. Réponses : 254/255 par pile, 207 des objets changés et créés, propriété `ethereal_stone`,
lignes `@SYSTEM` du canal objet, puis 257 (sauf 805 réussi).

## 5. NON ÉTABLI et écarts

- Les lignes `@253`/`@254` (« objet obtenu ») ne sont pas envoyées : elles citent un `NameId` d'objet non fiable ici.
- `SetElementalEffect` peut refuser côté officiel (`StructItem::SetElementalEffect`, non lu) ; ici il réussit
  toujours. Le temps restant de l'élément n'est pas écrit dans le motif d'objet (0).
- L'invocation renouvelée après une amélioration de carte repart pleine en session, sans écriture immédiate en base.
- Un objet non empilable créé en plusieurs exemplaires reste une seule ligne.
