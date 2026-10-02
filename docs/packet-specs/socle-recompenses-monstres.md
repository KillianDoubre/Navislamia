# Récompenses des monstres — 2 octobre 2026

## Ressource et gains

La formule provisoire par niveau est supprimée. Chaque instance conserve un `MonsterRewardProfile`
calculé au chargement depuis sa ligne `MonsterResource`. Le `ResourceId` de remplacement est respecté,
ainsi que les renforts créés par `respawn_near_monster`. Aucun accès à la base n'est ajouté à la mort.

| Gain | Colonnes | Application |
|---|---|---|
| EXP | `Exp` | quantité × rate `Exp`, arrondi aléatoire existant |
| JP | `Jp` | quantité × rate `Jp` (suit `Exp` si absent) |
| Or | `GoldDropPercentage`, `GoldMin`, `GoldMax` | chance × rate `Gold`, puis montant entier inclusif |
| Chaos | `ChaosDropPercentage`, `ChaosMin`, `ChaosMax` | chance × rate `ChaosDrop`, puis montant entier inclusif |

Les chances sont en pourcentage : succès si `chance × rate > tirage 0..99`, plafonné à 100 %.
Les rates d'or et de chaos multiplient la **chance**, sans multiplier le montant.
Un gain ou une chance nuls ne déclenchent aucun gain de remplacement. Les valeurs négatives sont
ramenées à zéro et les bornes inversées sont triées. Une pile d'or ne dépasse pas 1 000 000.

`MonsterSpawns.UseSecondaryRewards = true` sélectionne `Exp2`, `Jp2`, `GoldMin2/GoldMax2`,
`ChaosMin2/ChaosMax2`, avec les mêmes probabilités. La valeur par défaut est `false`.
Les réglages `MonsterSpawns` sont lus même lorsque `monster-spawns.73.json` fournit les positions.
Le choix des colonnes se fait au chargement du monde et nécessite un redémarrage pour être modifié.
`Rates.ChaosDrop` vaut 1 par défaut, se recharge à chaud et accepte `/rate chaos 2 1h`.

## Mort, groupe et chaos

Le tirage se fait une seule fois après `MonsterWorldState.TryKill`. Des dégâts répétés sur un cadavre
ne redonnent ni EXP, ni JP, ni or, ni chaos. Les gains EXP/JP et chaos sont répartis à parts entières
entre membres éligibles du groupe, en ligne, sur la même couche, à 540 unités ou moins du monstre.
Les restes vont aux premiers membres ; le tueur reçoit tout en solo. Le partage existant est conservé.

Le chaos reçu est borné par `IStatService.Compute(...).Total.MaxChaos`, donc par la capacité effective
du personnage. La part qui ne tient pas est perdue. Un personnage plein ne reçoit pas l'animation.
Un verrou de progression ordonne les crédits, notifications et montées de niveau des morts simultanées.
EXP/JP sont saturés à `long.MaxValue`, sans débordement.

## Or au sol et ramassage

L'or est créé près du cadavre via `DropGoldForMonster`, avec `item_code = 0` et le montant dans `count`.
Il utilise le même affichage et la même durée de vie que les autres objets au sol. Il tombe même
si le monstre n'a aucune table d'objets. Le portefeuille ne change qu'au ramassage.

Le propriétaire et les membres encore autorisés du groupe peuvent le prendre, y compris via un
familier, dans la portée de ramassage existante (300 unités). L'or est partagé entre les membres
en ligne de la même couche, à 400 unités ou moins du ramasseur, quel que soit le mode d'objets
`monopoly`/`random`/`linear`. Il n'avance pas le tour du mode `linear`.
Les restes vont aux premiers handles de personnage.

Les portefeuilles sont verrouillés dans l'ordre des handles. Si une part dépasserait le plafond porté
de 10 000 000 000, le ramassage répond `TooMuchMoney` : personne ne reçoit d'or et la pile reste au sol.
Ce refus global préserve l'or ; l'officiel peut ignorer les parts qui ne tiennent pas.
Une pile ne peut être prise qu'une fois, même avec des requêtes simultanées ou un échec réseau après
le crédit. L'or ne crée aucune ligne d'inventaire et ne subit aucun contrôle de poids d'objet.

Les soldes EXP/JP/or/chaos utilisent la sauvegarde de progression existante, périodique, à la
déconnexion et via `/save`. Aucune migration n'est nécessaire pour ce lot.

## Paquets

- `TS_SC_EXP_UPDATE` : EXP/JP après crédit, puis montée de niveau existante.
- `TS_SC_GET_CHAOS` **213**, 25 octets, structure officielle compacte : joueur `uint32` à +7,
  cadavre `uint32` à +11, montant `int32` à +15, type de bonus à +19, pourcentage à +20,
  bonus `int32` à +21. Les trois champs de bonus restent nuls.
- L'animation 213 est envoyée au bénéficiaire et aux observateurs qui connaissent le joueur et
  le cadavre, avec le handle de cadavre propre à chaque client.
- `TS_SC_GOLD_UPDATE` **601** publie or et chaos après attribution.
- L'or au sol utilise `TS_SC_ENTER` (3), puis `TS_SC_TAKE_ITEM_RESULT` (210) avant `TS_SC_LEAVE` (4)
  au ramassage, suivi des soldes 601 de ses bénéficiaires.

## Sources officielles locales

Racine : `A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer\Game`.

- `Resource/MonsterLoader.cpp`, vers 520 : `game.change_monster_drop_set`, sélection des colonnes `_2`.
- `Struct/StructMonster.cpp` : `procEXP` vers 1371, `procDropChaos` vers 1421,
  `procDropGold` vers 1540, `addChaos` vers 836.
- `Rule/GameRule.h` : `MAX_GOLD_DROP = 1000000`.
- `Message/GameMessage.h`, vers 1709 : `TS_SC_GET_CHAOS`, champs et ordre.
- `Message/GameMessage.cpp`, `onTakeItem` vers 1410 : code d'objet nul pour l'or,
  partage de l'or à 400 unités indépendamment du mode d'objets.

## Tests et vérification en jeu

`CombatRewardsTests`, `MonsterRewardIntegrationTests` et `PartyServiceTests` couvrent les valeurs
de ressource, les deux jeux de colonnes, le remplacement de ressource, les renforts Lua, les rates,
les probabilités et bornes inclusives, les plafonds, les restes de partage, le ramassage de groupe
dans les trois modes, les membres éloignés, le familier, les morts/ramassages simultanés, les requêtes
répétées, l'échec de notification, le paquet de chaos pour deux clients et le choix des récompenses
secondaires avec un catalogue de positions chargé par le serveur.

Commande :

```powershell
dotnet test Tests/Tests.csproj --no-restore -p:WarningLevel=0 -v:minimal
```

Dernier résultat : **2 663 tests réussis**, aucun échec. Le test PostgreSQL explicite des quêtes
n'est pas exécuté par cette commande ; il ne valide pas la sauvegarde sur la base réelle.

À essayer avec le client 7.3 :

1. En solo, tuer un monstre connu et comparer les gains à sa ressource avec les rates effectifs.
2. Vérifier que la pile d'or apparaît, que l'or augmente seulement au ramassage et qu'un second clic
   ne redonne rien ; essayer aussi avec le familier.
3. À deux clients en groupe, vérifier EXP/JP, animation et capacité de chaos, puis partage de l'or
   dans les trois modes. Éloigner un membre à plus de 400 unités du ramasseur.
4. Remplir un portefeuille pour vérifier le refus sans crédit partiel, puis libérer de la place
   et reprendre la même pile.
5. Faire `/save`, se reconnecter et vérifier EXP, JP, or et chaos.

## Limites

Le client réel n'a pas encore servi à valider l'affichage de la pile d'or ou l'animation 213.
Ce lot branche les récompenses importées et leurs probabilités. Les pondérations par dégâts,
pénalités d'écart de niveau, bonus de donjon/PC bang et stamina du serveur officiel ne sont pas
modélisés. Les règles d'attribution des objets autres que l'or restent celles du socle groupe.
