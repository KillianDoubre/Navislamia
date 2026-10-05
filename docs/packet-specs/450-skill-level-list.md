# 450 — TM_CS_SKILL_LEVEL_LIST, ancien identifiant

État au 2026-10-05 : identifiant déclaré et reçu sans effet, **aucune structure inventée**.

## Sources et décision

Le serveur officiel 2015 `Game/Message/GameMessage.h:182` contient exactement la déclaration
**commentée** de `TM_CS_SKILL_LEVEL_LIST = 450`. Les lignes 183-184 déclarent les deux
identifiants actifs 451 et 452. Il n'y a ni structure cliente 450 ni bras de réception dans
`GameMessage.cpp` ; le gestionnaire `onSummonCardSkillList:9919-9945` répond au 452 par 451.

Dans le rzu local épinglé à `87c1e83bf84efe29bb6405e8e6da80349712f3fa`, la famille contient
`TS_CS_SUMMON_CARD_SKILL_LIST.h` et `TS_SC_SKILL_LEVEL_LIST.h`, sans définition cliente 450.
Le SFrame épinglé dans [322](322-show-summon-name-change.md) ne contient pas les noms ASCII
`TM_CS_SKILL_LEVEL_LIST` ou `TS_CS_SKILL_LEVEL_LIST`, tandis que le constructeur de 452 est
déjà établi dans sa [fiche](452-summon-card-skill-list.md). Cette absence de nom ne démontre
pas à elle seule l'absence de toute émission.

Navislamia déclare 450 dans `GamePackets` et le consomme dans `GameClient`, après validation
de l'en-tête et des limites générales. Aucun champ n'est lu, aucun acteur n'est choisi,
aucun résultat ou liste arbitraire n'est envoyé. La fonctionnalité de la fenêtre de carte
est fournie par [451](451-skill-level-list.md) en réponse à 452.

## NON ÉTABLI

**Taille, contenu, constructeur client et comportement d'une éventuelle requête 450.**
Une capture montrant réellement cet id ou une autre version officielle comportant son
gestionnaire serait nécessaire pour lui attribuer un comportement. Le test d'une trame de
7 octets vérifie uniquement sa consommation, sans affirmer que 7 est sa taille officielle.
