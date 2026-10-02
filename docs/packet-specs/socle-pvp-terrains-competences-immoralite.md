# PvP : terrains PK, compétences et immoralité

Livré le 2 octobre 2026. Tests automatiques exécutés ; vérification avec deux clients encore à faire.

## 1. Sources

Sources officielles locales, sous `Rappelz/program/server/GameServer/Game/` :

- `Struct/StructPlayer.cpp` : `IsInPKField` (11785), `IsEnemy` (10115),
  `ProcImmoralPoint` (10602), activation PK (3951), `AddExp` (8110), perte d'EXP (10417).
- `Struct/StructPlayer.h` (697) : seuils Bloody et Demoniac.
- `Struct/StructMonster.cpp` (945) : baisse de l'immoralité à la récompense d'un monstre.
- `Resource/GameContent.cpp` (2337) : sélection du lieu par priorité des polygones.
- `Rule/GameRule.cpp` : coefficient PvP de 0,05 et écart de niveau DK de 10.
- `Struct/StructCreature.cpp` (5043, 5065) : objets ajoutant ou fixant l'immoralité.

La propriété `immoral` transporte l'entier fixe à quatre décimales, donc les points multipliés par
10 000 (`SetImmoralPoint`, `DB_Login` ; client `GameSystem/SGameSystem.cpp`, 2499).
Un personnage à 100 points reçoit la valeur 1 000 000 sur le fil.

## 2. Terrains PK

`MapService.GetLocationId` interroge les polygones `.nfl` chargés par les cartes et leurs scripts
`call_lc_In` (callback `SetCurrentLocationId`). La priorité numérique la plus basse gagne. Les coordonnées utilisées sont
les positions interpolées du joueur, comme pour la portée des compétences.

Le chargeur crée maintenant un objet distinct par polygone, conserve les coordonnées de chaque cellule
lors du chargement parallèle et continue après un lieu sans identifiant. Les chaînes terminées par un
seul octet NUL sont consommées. L'arbre de lieux appartient à chaque instance de carte et ses accès sont
protégés pendant le chargement.

`PkFieldService` relie l'identifiant à `WorldLocation.LocationType` :

| Type | Lieu | PK ordinaire |
| --- | --- | --- |
| 1, 3 | Ville, zone sans PK | Non |
| 5, 7 | Champ de bataille, événement | Non |
| 8, 9, 10 | Lobby HuntaHolic, donjon HuntaHolic, marché | Non |
| 11, 15, 16 | Deathmatch, arène, salle de prière | Non |
| Autres types | Champs et donjons ordinaires notamment | Oui |

Abadon, identifiant 110900, est protégé quel que soit son type. **Un lieu absent des cartes ou de
WorldLocation est protégé** : ce choix conservateur diffère du défaut permissif de l'officiel.
`GameRules:PkFieldsEverywhere` reste une option de débogage, désactivée par défaut, qui force tous les
terrains PK. Elle n'est plus nécessaire pour jouer en PK sur une carte correctement renseignée.

## 3. Adversaires et compétences

Un duel commencé autorise les deux adversaires, y compris en ville et dans un même groupe ou une même
guilde. Hors duel :

- les deux joueurs doivent être en terrain PK et sur la même couche ;
- les membres d'un même groupe ou d'une même guilde ne sont pas ennemis ;
- l'attaquant peut viser si son PK est activé, si celui de la cible est activé, ou si la cible a au moins
  100 points d'immoralité ;
- une cible immortelle est protégée.

Les familles offensives déjà prises en charge peuvent désormais toucher les joueurs : dégâts directs
231/30001, multi-coups 232, zones 30011/261 et zone persistante 271 notamment, ainsi que les malus
classifiés par le catalogue. `UseOnCharacter`, visibilité, état vivant et portée sont vérifiés avant
de consommer les MP. L'identité de la session, les permissions et la portée sont revérifiées à la fin
de l'incantation et entre les coups. Les attaques normales s'arrêtent aussi si la cible change de
session ou entre en lieu protégé.

Les zones réunissent monstres et joueurs ennemis avant d'appliquer le plafond de cibles et la
distribution des dégâts. Une zone 271 reste au point de lancement et sélectionne ses victimes à chaque
déclenchement ; elle respecte à chaque fois les terrains PK de son propriétaire et des victimes.
Les identifiants internes des joueurs sont distincts des instances de monstres ; les paquets 401
contiennent les handles et les PV de chaque victime adaptés à chaque destinataire.

Les dégâts utilisent les stats, esquive, blocage, critique et résistances de la cible, puis
`GameRules:PvpDamageRate` (défaut **0,05**, soit 5 %). Dégâts additionnels, bouclier de mana et renvoi
suivent également ce chemin. Le renvoi ne déclenche pas un nouveau renvoi. Les dégâts perturbent une
incantation et la mort interrompt les actions de la victime. Le duel conserve son chemin de fin et de
résurrection, sans immoralité ni pénalité de mort.

## 4. Immoralité et compteurs

`ConnectionInfo` contient l'immoralité décimale, PKC et DKC. Les statuts publiés sont des masques complets :
Bloody à **100 points**, Demoniac à **1 000 points** (avec Bloody également).

- Activer PK ajoute **5 points** sur serveur ordinaire, zéro sur serveur PK. Répéter la trame 800 sans
  désactiver PK ne fait pas payer à nouveau.
- Tuer un joueur PK ou criminel n'ajoute ni immoralité ni compteurs.
- Tuer un joueur innocent ajoute **50 points** ; si l'écart de niveau atteint
  `GameRules:PkPenaltyLevel` (défaut 10), la base est 100, 200 pour un tueur d'immoralité négative, ou
  1 000 si elle vaut au plus -1 000. PKC augmente ; DKC augmente dans ce dernier cas d'écart de niveau.
- Le multiplicateur de la base augmente de 0,1 par tranche de 100 points positifs, plafonne à 2 au-delà
  de 1 000 points, puis ajoute 0,5 pour DKC ≥ 5 ou 1 pour DKC ≥ 20. Sur serveur PK, le multiplicateur
  de groupe conserve la division entière de l'officiel : 1 seul, 2 en groupe de deux ou plus.
- Sur serveur ordinaire, un meurtre puni applique Nemesis, état 5999, niveaux 1/2/3 aux seuils
  100/500/1 000, pendant 720 000/1 440 000/2 880 000 ticks.
- Un monstre de niveau au moins égal à celui d'un bénéficiaire vivant réduit son immoralité positive
  de 1 point, ou 0,5 dès 1 000 points. En groupe, la baisse est multipliée par `2 / (membres + 1)`.
  Les fractions sont conservées à quatre décimales ; cette baisse peut franchir zéro.
- L'EXP des monstres est réduite de 9 % par tranche de 100 points au-dessus de 100, jusqu'à 90 %.
  Le calcul utilise l'immoralité après sa baisse. Les JP ne subissent pas cette réduction.
- La perte d'EXP à la mort est multipliée par `1 + 0,2 × tranches de 100` au-dessus de 100 points,
  puis par 3 au-dessus de 1 000. La mort par monstre, ou par joueur sur serveur PK, réduit aussi
  l'immoralité positive de `10 - PKC / 10`, ou 1 point à partir de PKC = 100, sans passer sous zéro.
- Les effets instantanés d'objet 41 et 42 ajoutent ou fixent l'immoralité, avec un plancher de zéro.

`MoralityRules` centralise ces calculs, la propriété 507 et les changements de statut 500. Le gain et les
compteurs d'un meurtre ne sont attribués qu'une fois, même si une seconde frappe arrive sur le cadavre.

## 5. Sauvegarde et configuration

Les colonnes `Characters.ImmoralPoint`, `PkCount` et `DkCount` existaient déjà : **aucune migration PvP**.
L'entrée en jeu charge les trois valeurs. Déconnexion, retour au lobby, commande de sauvegarde GM et
sauvegarde de don transmettent leur instantané à `SaveProgressAsync`. Les appelants sans cet instantané
conservent les valeurs présentes en base. Le mode PK conserve sa sauvegarde existante.

Les valeurs par défaut suffisent ; `PkServer` conserve le choix existant des pénalités de mort et des
objets lâchés hors duel. Les trois options sont dans `Configuration/Options/GameRuleOptions.cs`.

## 6. Vérification

**82 nouveaux cas** dans `PkFieldTests`, `MoralityTests`, `PvpTests` et `CharacterDefaultsTests` couvrent :
chargement Lua des lieux et polygones en parallèle, types protégés, priorités, incantations annulées,
permissions, dégâts et diffusion, multi-coups, zones mixtes et persistantes, duels, mort PK, résistances,
bouclier de mana, renvoi, dégâts additionnels, propriétés fixes, récompenses et sauvegarde.

Commande :

```powershell
dotnet test Tests/Tests.csproj --no-restore -p:WarningLevel=0 -v:minimal
```

Résultat : **2 990 tests réussis, zéro échec**. Le scénario PostgreSQL explicite de remise de quête
reste ignoré sans sa configuration ; cette exécution ne vérifie pas une base PostgreSQL réelle.

À tester avec deux clients :

1. Activer PK dans un champ ; vérifier +5 points, puis attaquer par coup normal et compétence.
2. Entrer en ville pendant une incantation ou entre les coups ; vérifier la protection.
3. Faire un duel en ville, vérifier les compétences, la fin et l'absence de pénalité.
4. Tuer un innocent, contrôler PKC/DKC, nom/statut Bloody et Nemesis ; tuer ensuite des monstres
   de niveau suffisant, puis reconnecter et contrôler les valeurs conservées.
5. Lancer 271 près de plusieurs joueurs, déplacer les victimes et vérifier la sélection à chaque tick.

Ce lot n'ajoute pas les modes d'arène/siège, les alliances de guildes, le compte à rebours de bascule PK,
les nouveaux types de compétences encore absents du catalogue ni la conversion des dons de l'autel.
