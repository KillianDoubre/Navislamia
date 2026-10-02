# Buffs persistants, groupe, invocations et soins de zone

État : implémenté et testé localement le 2 octobre 2026 ; client 7.3 à vérifier.

## Persistance

`BuffPersistence` conserve un instantané dans Telecaster `CharacterStates`, remplacé en une sauvegarde
EF à la déconnexion, au retour au lobby et par `/save`. Le retour au lobby désinscrit également le
joueur du minuteur de compétences avant de sauvegarder et de vider sa session.

Chaque état conserve sa compétence, son niveau, sa durée restante et l'heure UTC de sauvegarde.
La ligne de joueur utilise `SummonCardId = 0` ; celle d'une invocation utilise le handle stable de sa
carte, et non le handle temporaire de l'acteur en monde. Le code respecte `StateResource.StateTimeType` :

| Drapeau | Comportement hors ligne |
|---|---|
| `EraseOnLogout` (2) | état non sauvegardé et non restauré |
| `TimeDecreaseOnLogout` (4) | temps hors ligne soustrait ; état expiré ignoré |
| aucun des deux | durée restante suspendue pendant la déconnexion |

Les états inconnus ou expirés sont ignorés. Une aura sans limite conserve son activation ; les états
empruntés à l'aura d'un autre joueur sont recalculés en monde et ne sont pas sauvegardés.
Une aura réservée aux invocations conserve un marqueur d'activation (`StateId = 0`), sans état sur le
joueur. Ce marqueur n'est jamais émis comme état au client.

À l'entrée en jeu, les états sont chargés avant le calcul des statistiques de connexion ; les icônes
505 et activations 407 sont ensuite rejouées. Les états d'invocation attendent `SummonWorldService.Enter`,
puis sont attachés au nouveau handle. Ranger une invocation conserve ses états par carte dans la session.

Migration : `Version0012_CharacterStates`, appliquée par le chemin de migrations existant au démarrage.
Le modèle EF ne présente aucun changement en attente après génération. Le [script SQL](migration-character-states.sql)
est fourni pour inspection. Cette migration n'a pas été appliquée à la base réelle pendant ce travail.

## Ciblage des buffs et auras

Le catalogue charge maintenant `ValidRange`, les autorisations `UseOnSelf/Party/Neutral/Character/Summon`
et les familles de soins 508/521. Les cibles sont sélectionnées à l'effet, après la durée de lancer.
Un joueur ciblé directement doit être connu du lanceur, vivant, sur la même couche et dans `CastRange`.
Une cible partie, morte ou devenue hors portée pendant le lancer annule l'effet ciblé.

| `SkillTarget` | Cibles |
|---|---|
| 1 | joueur ou invocation désigné |
| 2, 4 | unités autorisées dans la région |
| 3, 6 | région excluant le lanceur |
| 21 | membres du groupe |
| 31 | invocation désignée, ou première invocation du lanceur |
| 32 | invocations des membres du groupe |
| 45 | propriétaire de la cible et ses invocations |
| 51 | membres du groupe et leurs invocations |

Les rayons de buffs et de groupe utilisent `ValidRange × 12`. Les positions de joueurs sont interpolées.
Les unités mortes, les autres couches et les catégories interdites par la compétence sont exclues.
Un type 31/32 ne buffe pas le joueur à la place de l'invocation. Les buffs appliqués utilisent les règles
d'empilement existantes et la durée issue des colonnes `StateSecond` ; chaque bénéficiaire reçoit ses
états et ses statistiques, ainsi que les observateurs autorisés.

Les invocations conservent désormais leurs HP/MP courants et une liste d'états. Leurs statistiques sont
recalculées à l'application et à l'expiration à partir de `SummonWorldEntry.BaseStats`, si le caller
les fournit, ou des maxima HP/MP connus à l'entrée. Les états sont également montrés aux observateurs
qui entrent dans la vue après le lancer.

Les auras de groupe/invocation suivent les mêmes ensembles de cibles. Leur projection dure 11 secondes,
avec renouvellement avant expiration ; le minuteur retire les cibles sorties du rayon ou du groupe.
La désactivation, le remplacement et la sortie du lanceur retirent les projections. Le lanceur conserve
le bouton d'aura même si la compétence n'applique un état qu'aux invocations.

## Soins de zone

- **521 — HP** : formule du serveur original avec attaque magique du lanceur, niveau de compétence
  et maximum HP propre à chaque bénéficiaire.
- **508 — HP/MP** : formules séparées HP et MP, avec les maxima propres à chaque bénéficiaire.
- Rayon : `var10 × 12`, autour de la cible choisie. `var11` filtre la relation ; 2 désigne les alliés
  du groupe, 1 exclut les ennemis. Pour 508, `var12` vaut 1 pour les joueurs, 2 pour les invocations,
  3 pour les deux. Les familiers ramasseurs ne sont pas des invocations et restent exclus.
- Le coût MP est payé une fois, le délai de lancer et le cooldown restent ceux du chemin commun.
- Les gains effectifs sont bornés aux maxima ; les morts ne sont pas ressuscités par un soin.
- Chaque bénéficiaire reçoit ses propriétés HP/MP et un résultat de soin dans le 401 ; les soins
  alimentent le chemin de haine existant. Les compétences d'objets utilisent les mêmes effets sans
  facturer une seconde fois le MP ou exiger l'apprentissage.

Le 401 conserve le pas de 45 octets par résultat. Le type **22 — AddHpMpSp** porte HP à +5, MP à +9,
gain HP à +13, gain MP à +17 et gain SP nul à +21, relativement au résultat.

## Sources locales et tests

Racine officielle : `A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer\Game`.

- `Resource/SkillBase.h` : cibles 21/31/32/45/51, rayon `valid_range × DEFAULT_UNIT_SIZE`,
  `EF_ADD_REGION_HP_MP = 508`, `EF_ADD_REGION_HP = 521`, `TOGGLE_LIVE_TIME = 1100`.
- `Skill/StructSkill.cpp` : énumération des cibles vers 3528–3830, auras vers 4393,
  soins HP/MP de région vers 7894 et HP vers 7986.
- `StateTimeType` reprend les drapeaux de temps de `StateResource` déjà importés.

`SupportSkillTests` couvre les cibles de groupe/invocation, les exclus, les flags de créature,
les couches et rayons, la cible disparue pendant le lancer, HP/MP et résultats multiples,
les ennemis, l'expiration et les observateurs tardifs, les auras, les durées hors ligne et le passage
par plusieurs services et contextes de base. Les anciens tests de catalogue ont été adaptés aux
familles désormais prises en charge. La suite complète est exécutée avec :

```powershell
dotnet test Tests/Tests.csproj --no-restore -p:WarningLevel=0 -v:minimal
```

Dernier résultat : **2 875 tests réussis**, aucun échec, dont 25 cas de `SupportSkillTests`.
Un test bloque volontairement la sauvegarde EF et vérifie que la reconnexion attend sa fin.

Le test PostgreSQL explicite des quêtes reste hors de cette commande. La persistance des nouveaux
états est testée sur des contextes EF en mémoire ; la validation de la migration et des icônes sur
le serveur réel reste à faire.

## Vérification en jeu

1. Redémarrer pour appliquer `Version0012_CharacterStates`, lancer un buff temporaire, attendre,
   se déconnecter puis revenir : icône, bonus et durée restante doivent suivre les drapeaux de la ressource.
2. Refaire par le retour au lobby ; annuler le buff, sortir et vérifier qu'il ne revient pas.
3. À deux membres proches, tester un buff 21 puis 51 ; éloigner l'un et changer de couche.
4. Avec des invocations présentes, tester 31/32/45/51, les statistiques, l'expiration et un nouvel observateur.
5. Tester 508/521 sur plusieurs cibles blessées, une cible pleine et une morte ; vérifier les HP/MP
   et l'animation sur les deux clients.
6. Activer une aura, sortir du rayon ou du groupe, puis la désactiver ; ses bonus empruntés doivent disparaître.

## Limites

Le cycle d'appel/apprivoisement des invocations reste le chantier existant : ce lot traite les invocations
déjà présentes via `SummonWorldService`, sans implémenter les sorts 4001/4002/4003. Il ne crée pas un
calcul complet des statistiques de base d'invocation. Les cibles de guilde, équipe d'attaque et sorts
de soin destinés aux ennemis ne sont pas ajoutés par ce lot. Les bonus d'enchantement des soins ne sont
pas modélisés. Une panne brutale avant une sauvegarde reste distincte d'une déconnexion normale.
