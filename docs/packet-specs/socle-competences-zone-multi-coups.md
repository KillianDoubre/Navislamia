# Compétences de zone, multi-coups et déclencheurs Lua

Implémentation du 2 octobre 2026. Types d'effet, pas identifiants de compétences.

## Sources et portée

Sources officielles locales : `Game/Skill/StructSkill.cpp`, `StructSkillProp.cpp`,
`Game/NPCProc/MonsterAI.cpp`, `Game/Struct/StructCreature.cpp`, `StructCreature.h`,
`Game/Script/ScriptNPC.cpp`, `ScriptPlayer.cpp` et
`ServerRelease/Resource/Script/ETC_run_monster_skill.lua` de la distribution serveur Rappelz.
Les paramètres et liens viennent des exports `data/epic7/SkillResource.csv` et
`MonsterSkillResource.csv` ; les compétences sont jointes aux ressources Arcadia au démarrage.

| Effet | Résolution |
| --- | --- |
| 30011 | Dégâts physiques, cercle centré sur la cible |
| 232 | Dégâts magiques sur une cible, plusieurs tirs espacés |
| 261 | Dégâts magiques, cercle centré sur la cible |
| 271 | Zone fixe, dégâts magiques périodiques pendant sa durée |
| 111, 211 | Anciens dégâts physiques/magiques, cercle |
| 113, 213, 262, 30013 | Zone spéciale : cercle, cône, direction ou croix |
| 112, 212, 241, 30012, 30016 | Plusieurs impacts dans un même FIRE |
| 263 | Zone magique avec plusieurs tirs espacés |

Les joueurs utilisent 30011, 232, 241, 261, 262, 263, 271, 30012, 30013 et 30016,
en plus des familles déjà disponibles. Les familles anciennes sont accessibles aux monstres.

## Résolution et paquets

- `SkillAreaRules` porte les indices de paramètres, les formules, la sélection géométrique et
  la distribution. Une unité de rayon représente **12 unités du monde**. Le cône utilise un angle
  en radians ; direction et croix utilisent une demi-épaisseur en unités du monde.
- Distribution 0 : tous ; 1 : réduction des dégâts au-delà du nombre prévu ; 2 : sélection
  aléatoire limitée ; 3/4 : cibles les plus proches de la cible/du lanceur.
- Les positions des monstres sont leurs positions actuelles, y compris ceux ayant quitté leur
  cellule d'origine. Les cadavres et les autres couches sont exclus. Les apparitions statiques restent sur la couche 0.
- Pour les familles de zone et multi-coups, `SkillCastRangeRules` contrôle la portée avant le
  coût de mana, le cooldown et le lancement, côté joueur comme côté monstre (sélection normale
  et appels Lua). Sur une cible, la distance est réduite des deux rayons corporels ; la portée
  `cast_range × 12` est multipliée par 1,2, ou 1,5 si la cible se déplace. La valeur -1 utilise
  l'`AttackRange` effective du lanceur, convertie par `12 / 100`. Un refus répond `TooFar` au
  joueur ; le monstre peut essayer le slot suivant. Une cible ou un paquet d'une autre couche
  ne peut pas démarrer un lancer ciblé.
- Les lancers au sol conservent le contrôle direct de distance, sans rayon corporel ni tolérance
  de mouvement ; -1 utilise désormais la portée d'arme au lieu de contourner cette vérification.
  Les positions des joueurs sont interpolées pour les contrôles et la sélection des victimes,
  et une zone sur soi commence à la position actuelle du lanceur.
- Chaque impact passe par `ICombatService.RollHit`/`RollMonsterHit` puis
  `ApplyDamage`/`DamagePlayer`. Mort, récompense, butin et progression de quête restent dans le
  chemin de combat existant. Un impact fatal arrête les coups suivants sur cette cible.
- `GameSkillPackets.BuildSkill` écrit autant de blocs de **45 octets** que nécessaire : compteur
  uint16, nombre de cibles distinctes, nombre de coups du tir, indicateur multiple et rayon.
  Chaque observateur reçoit ses propres handles de monstres et uniquement les victimes qu'il connaît.
- La zone persistante est aussi un objet statique du monde : ENTER de 38 octets, type d'objet 5,
  caster, start_time et skill_num selon `TS_ENTER::SkillInfo` officiel. Les nouveaux observateurs
  reçoivent son ENTER ; sortie de vue et expiration envoient LEAVE. Son handle vient de l'allocateur
  commun et le handle du lanceur monstre est celui de chaque observateur.
- `SkillEffectScheduler` exécute les tirs sur l'horloge serveur (tick de traitement : 50 ms).
  Les nouveaux effets respectent leur délai de lancement. Un blocage du serveur ne produit pas
  une rafale de tous les tirs en retard. Les valeurs de l'horloge peuvent déborder sans casser la cadence.
- MP et cooldown sont engagés une seule fois. Une série de tirs se termine après ses impacts.
  La zone 271 libère le lanceur dès sa création, reste à son point initial, resélectionne les cibles
  à chaque pulsation et expire selon sa durée. Sa puissance magique est celle de sa création.
- Une déconnexion, un changement de couche ou une mort invalide les tirs du joueur.
  Une réapparition de monstre n'hérite pas des tirs de sa vie précédente. La mort du monstre lanceur
  interrompt ses tirs et ses zones.

Les listes envoyées sont limitées à 255 cibles et 255 coups par tir, conformément aux compteurs
octets du paquet. Les pulsations d'une zone sont également bornées à 65 535.

## Déclencheurs Lua

L'export conserve **1 067 emplacements**, dont **342 à probabilité nulle**, et **784 déclencheurs**
sur 546 liens de compétences. Un emplacement non pris en charge garde un marqueur dans le catalogue :
il ne peut pas être lancé, mais les indices Lua des suivants restent corrects.

Les cinq conditions officielles sont implémentées :

| Type | Condition |
| --- | --- |
| 1 | HP inférieurs ou égaux au seuil, une fois par combat |
| 2 | HP inférieurs ou égaux au seuil, avec probabilité en pourcentage |
| 3 | Délai depuis le début du combat, une fois |
| 4 | Délai depuis le début du combat, puis répétition |
| 5 | Immédiatement au début du combat, puis répétition |

Les déclencheurs précèdent le tirage habituel. Ils sont évalués dans l'ordre ; un lancement réussi
arrête le parcours. Une erreur Lua ou un lancement refusé laisse reprendre la sélection normale.
L'IA tente compétences et déclencheurs pendant la poursuite, avec une cadence indépendante du coup
de mêlée et une vérification de la ligne de vue.

`ScriptService` sérialise les accès à MoonSharp, charge les fichiers Lua dans l'ordre et appelle une
fonction nommée avec sept arguments : monstre, cible, indice du déclencheur, x, y, couche, indicateur
de raid (0 actuellement). Le contexte est retiré même après une exception. Callbacks disponibles :

- `get_monster_id(handle)` ;
- `monster_skill_cast(index, monster, cible)` ou `(index, monster, x, y)` ; indices commençant à 0 ;
- `add_state(id, niveau, durée_en_ticks, handle)` sur le monstre courant ou sa cible.
- `respawn_near_monster(monstre, id, nombre)` : apparition au point actuel du monstre, même couche,
  dispersion à ±60 unités avec vérification des collisions, vitesse multipliée par trois et cible
  héritée si le monstre est en combat. ENTER et MOVE utilisent les handles de chaque observateur.
  Les renforts morts sont retirés du monde au terme du délai de cadavre/réapparition ; ils ne réapparaissent pas.
- `set_auto_user(flag, joueur)` : sauvegarde de `Characters.AutoUsed` dans un contexte propre,
  valeur de session mise à jour après succès, résultat Lua 1/0. La remise à zéro efface les flags
  de tous les personnages du compte et retire l'état 5997 de la cible. À la connexion, un personnage
  retrouve le marquage de son compte si un autre personnage est marqué. Aucun changement de schéma.

Les dégâts de zone sont calculés pour toutes les victimes présentes dans la zone, même lorsqu'elles
ne sont pas streamées par le lanceur. Les paquets filtrent les impacts inconnus du destinataire.
La mort, les suppressions d'états et le départ d'un monstre touchent tous ses observateurs, même
ceux qui ne voient pas le joueur lanceur. Un joueur touché qui ne connaît pas le monstre lanceur
reçoit son impact avec un caster nul ; le rendu de ce cas doit encore être vérifié en client.

Le script officiel est embarqué, donc les déclencheurs de combat fonctionnent même si le chargement
des scripts externes est désactivé. Un script externe chargé peut remplacer sa fonction `trigger`.

## Limites précises

- La validation en client 7.3 reste à faire : animations, rendu d'une zone persistante, déplacement
  pendant le lancement et observation à deux clients.
- Le script disponible couvre 631 des 779 ressources de monstres portant un déclencheur dans
  l'export Epic 7. Les branches des autres monstres ne sont pas reconstituées.
- Les 148 branches absentes restent indisponibles : aucune des autres copies locales Epic 7, 8
  et 9 du script ne fournit ces branches. Les compétences non prises en charge ne sont pas
  remplacées par une attaque générique. Le mode raid et ses ajustements de statistiques ne sont
  pas modélisés ; l'argument raid transmis à Lua reste nul.
- Les états régionaux 302 restent ceux du socle initial, appliqués à la cible ou au monstre lui-même.
  Ni les invocations des joueurs, ni le PvP, ni les soins/buffs de groupe ne sont ajoutés ici.
- Les paramètres d'amélioration restent nuls, comme dans les compétences offensives précédentes.
  `cast_range` est contrôlé pour les familles de zone et multi-coups, y compris la portée d'arme (-1) ;
  les autres familles restent à compléter. Interruption, empilement et résistance
  aux états ne sont pas ajoutés.

## Vérification

`AreaSkillTests`, `SkillCastRangeTests`, `MonsterTriggerTests`, `MonsterSkillTests` et
`MonsterCallbackPersistenceTests` couvrent les familles demandées,
les formes, la distribution, les paquets, les handles par observateur, les tirs espacés, le coût unique,
la mort, la réapparition, la déconnexion, les couches, les zones fixes, l'expiration, la puissance
capturée, les cinq conditions, les indices conservés, les erreurs Lua et un appel du script embarqué.
Les renforts, leur retrait définitif, le marquage anti-bot et son effacement pour un compte,
la persistance après changement de contexte et les morts hors visibilité sont aussi couverts.
Les tests de persistance utilisent EF InMemory. Les horloges sont avancées explicitement,
sans attendre des temporisateurs réels.
Les tests de portée couvrent les limites, les tailles, les armes, les positions interpolées,
le mouvement et le retour de l'horloge à zéro, les refus sans coût, la sélection du slot suivant
et l'impossibilité de contourner la portée par Lua.

Résultat final : **90 tests ciblés** et **2 637 tests de la suite réussis**, aucun échec.
Le test explicite d'intégration PostgreSQL existant n'a pas été exécuté dans cette passe ;
ce changement ne demande pas de migration.

```powershell
dotnet test Tests/Tests.csproj --no-restore -p:WarningLevel=0 -v:minimal
python tools/export_monster_skills.py
python tools/import_monster_triggers.py "chemin/ETC_run_monster_skill.lua"
```

Aucune migration n'est nécessaire : les nouveaux champs utilisent des colonnes déjà importées.
