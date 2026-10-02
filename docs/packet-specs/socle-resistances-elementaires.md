# Résistances élémentaires

Implémenté le 2 octobre 2026. Validation avec le client Epic 7.3 encore à faire.

## Statistiques et ressources

`StatBlock` conserve sept résistances indépendantes : neutre (0), feu (1), eau (2), vent (3),
terre (4), lumière (5), ténèbres (6). Une copie de statistiques d'invocation conserve ces valeurs
sans partager un tableau mutable avec son modèle.

- Objets : `IncParameterB` (97) et `AmpParameterB` (99), bits 0 à 6. Les autres bits restent ignorés.
- États : effets 1 et 2, triplets 2 et 3 de `value_0..value_17`, même masque B.
- Passif `EF_MAGIC_REGISTANCE_INCREASE` (10006) : quatre triplets
  `(élément, base, gain par niveau)` dans `var_0..var_11`, avec la sélection d'arme existante.
- Les ajouts sont additionnés avant les amplifications ; les amplifications de résistance se cumulent
  en une seule somme, comme `getAmplifiedResistByAmplifier`.
- Les monstres lisent leurs états au moment de chaque frappe. Sans effet de résistance, leur valeur
  est zéro ; `MonsterResource` ne fournit pas de colonne de résistance innée.

Les colonnes nécessaires sont déjà importées. Aucune migration supplémentaire. Seuls les effets de
résistance effectivement présents dans les ressources donnent des points ; la modification ne crée pas
de nouveaux objets ni de nouveaux sorts dans Arcadia.

## Calcul et chemins de combat

Après défense, dispersion et critique :

```text
dégâts reçus = max(0, floor(dégâts × (1 − résistance / 300)))
```

150 points absorbent la moitié ; 300 points ou plus absorbent tout ; une résistance négative augmente
les dégâts. Aucun plancher à 1 après résistance. Un raté ou blocage parfait reste zéro, et la résistance
ne tire aucun dé supplémentaire. Les dégâts excessifs sont bornés à `int.MaxValue`.

`CombatFormulas.Resolve` traite les frappes neutres et les compétences élémentaires. L'élément de
`SkillResource` est conservé par les deux projections du repository et les catalogues joueur/monstre,
puis transmis pour chaque cible et chaque coup, y compris les zones persistantes.

Les dégâts additionnels d'attaque et les renvois passent par le même calcul de résistance pour leur
élément propre. Le renvoi consulte la résistance du monstre attaquant. La résistance s'applique avant
le bouclier de mana, la perte de PV, la haine et le chemin de mort/récompense.

Sur le fil : `ATTACK_INFO.elemental_damage` contient les suppléments réellement reçus ;
`HIT_DAMAGE_INFO.damage_type` contient l'élément de la compétence, `damage` le montant réduit et
`target_hp` les PV après application. L'identifiant des paquets et la taille des enregistrements ne changent
pas. Les champs supplémentaires de dégâts élémentaires des compétences restent à zéro : les effets
additionnels propres aux compétences ne sont pas implémentés par cette modification.

## Références locales

Sources officielles présentes dans `Rappelz/program/server/GameServer/Game` :

- `Struct/StructMisc.h` : `FLAG_ET_*_RESIST`, bits 0 à 6.
- `Struct/CalculateStat.cpp` : `incParameter2`, `ampParameter2`,
  `getAmplifiedResistByAmplifier` et `EF_MAGIC_REGISTANCE_INCREASE`.
- `Struct/StructCreature.cpp:6781` : `resist.amp -= GetElementalResist(element) / 300`.
- `Logic/Damage.cpp` : `CalculateActualDamage`, `CalculateResistance` et bouclier de mana.
- `Skill/StructSkill.cpp` : `AddSkillDamageResult` et son `damage_type` élémentaire.

**Correction volontaire du bug de division entière** : `GetElementalResist` retourne un entier et
le commentaire original signale que la division rend la résistance inopérante. La division décimale
permet aux points inférieurs à 300 de fonctionner ; cette correction diffère du comportement de ce bug
du serveur original.

## Vérification

`dotnet test Tests/Tests.csproj --no-restore -p:WarningLevel=0 -v:minimal` : **2 908 tests réussis**,
dont **33 nouveaux cas** pour ce bloc. Le test PostgreSQL explicite des quêtes n'a pas été exécuté ;
aucune base réelle n'est requise par les tests de résistances.

Tests automatisés dans `ElementalResistanceTests`, `AreaSkillTests` et `CombatMechanicsTests` :
sept masques indépendants, équipement + état + passif, amplifications cumulées, copie isolée,
zéro/150/300/négatif, critique physique et magique, raté/blocage, dégâts additionnels et renvois,
PV et paquets produits par les services réels.

Les familles joueur 30011, 232, 261, 271 et monstre 101, 201, 111, 113, 261, 262, 30013 sont
exercées avec des résistances. Un état ajouté entre deux coups de 232 est relu au coup suivant.
Deux joueurs touchés par la même zone reçoivent des dégâts différents selon leur résistance.
Les sorts 101/201 vérifient aussi l'ordre résistance → bouclier de mana.

Les tests utilisent des ressources et connexions en mémoire. La validation en base réelle et avec le
client reste à faire : équiper/retirer un objet de résistance, ajouter/retirer un état, comparer les pertes
de PV pour le bon élément et un autre, puis répéter avec une zone et un sort à plusieurs coups.
