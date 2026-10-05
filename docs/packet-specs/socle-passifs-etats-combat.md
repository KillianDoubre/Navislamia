# Socle : passifs qui posent un état au combat

Livré le 2026-10-05. Logique portée depuis le serveur officiel 2015 :
`A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer\Game` (C++ cp949).
Cette mécanique utilise les paquets d'état et de statistiques déjà intégrés ; aucun nouvel
opcode ni migration n'est nécessaire.

## Identifiants et destinations

Les effets de **compétence** sont `10048..10062` (`Resource/SkillBase.h:241-255`).
Les effets des **états actifs** ont un espace de valeurs différent (`Struct/StructMisc.h:149-156,209-221`).
**32001 est l'amplification d'expérience des invocations**, et non un proc d'état
(`SkillBase.h:303`). Les anciennes mentions « 32001 et suivants » étaient incorrectes.

« Adversaire » désigne l'autre acteur de l'événement, y compris celui qui attaque lors
d'un coup reçu, d'une esquive ou d'un blocage. Le lanceur de l'état reste le propriétaire du proc.

| Événement | Compétence sur l'adversaire / sur soi | État sur l'adversaire / sur soi | Source CalculateStat.cpp |
|---|---|---|---|
| Coup donné | 10048 / 10049 | 36 / 37 | 923-939 ; 2063-2079 |
| Coup reçu | 10050 / 10051 | 38 / 39 | 943-959 ; 2083-2099 |
| Critique donné | 10053 / 10054 | 3202 / 3203 | 974-990 ; 2171-2187 |
| Critique reçu | 10055 / 10056 | 3204 / 3205 | 994-1010 ; 2191-2207 |
| Esquive | 10057 / 10058 | 3206 / 3207 | 1014-1030 ; 2211-2227 |
| Blocage | 10059 / 10060 | 3208 / 3209 | 1034-1050 ; 2231-2247 |
| Blocage parfait | 10061 / 10062 | 3210 / 3211 | 1054-1070 ; 2251-2267 |
| Meurtre | — / 10052 | — / 3201 | 963-970 ; 2149-2156 |
| Mort | — | — / 3311 | 2160-2167 |

L'ancien état 26 est également reconnu : arme vérifiée, attaque normale exclusivement,
élément indifférent, aucun filtre de PV ni coût MP (`CalculateStat.cpp:2051-2058`).
Le dump local `data/epic7/SkillResource.csv` contient **45 lignes** dans cette famille ;
`StateResource.csv` contient **25 lignes** dans les familles reconnues, ancien 26 inclus.
Les familles absentes du dump restent décodées et testées avec leurs identifiants officiels.

## Courbes et conditions

Les indices ci-dessous sont ceux du C++, **à partir de zéro**. Les colonnes CSV des
compétences s'appellent `var1..var20` et correspondent à ces indices `0..19`.

| Valeur | Formule / sens | Source |
|---|---|---|
| État appliqué | `int(var0)` | `CalculateStat.cpp:939` |
| Niveau de l'état | `int(var2 + var3 × niveau du passif)` | `:939` |
| Durée en ticks | `(var4 + var5 × niveau) × 100`, après multiplication | `:939` |
| Probabilité | `int(var6 + var7 × niveau)` ; succès si `rand % 100 < ratio` | `StructProc.h:8-19`, `StructProc.cpp:11` |
| Armes | classe égale à l'une de `var8..11`, ou `var8 == 99` | `CalculateStat.cpp:926-928` |
| Type d'attaque | `(var12 & type) == type` | `StructProc.cpp:25` |
| Coût MP | `int(var13)` du propriétaire, après tentative d'ajout | `CalculateStat.cpp:939`, `StructProc.cpp:83-85` |
| PV propriétaire | bornes `var14/15`, toutes deux inclusives ; zéro désactive | `StructProc.cpp:13-14` |
| PV de l'autre acteur | minimum `var16` inclusif, maximum `var17` **exclusif** | `StructProc.cpp:15-16` |
| Élément | `var18 == 99` ou égalité exacte | `StructProc.cpp:26` |

Types : normal = 1, compétence physique = 2, magique = 4, bénéfique = 8,
néfaste = 16 (`StructMisc.h:313-320`). Les compétences offensives transmettent le
type et l'élément de la ressource. Les soins et buffs réussis transmettent le type
bénéfique magique ; les invocations utilisent leurs propres compétences et leur arme.

**Mort et meurtre utilisent `_KILL_TAG`**, sans filtre de type d'attaque ni d'élément.
`var18` devient l'écart maximum `niveau propriétaire - niveau autre` (`-1` = sans limite),
et `var19` le minimum de MP en pour cent (`StructProc.cpp:31-38`). Le meurtre conserve
les bornes de PV du propriétaire ; la mort n'en a pas. Le constructeur officiel
**ignore les bornes de PV cible**, même si elles lui sont passées (`StructProc.h:43-46`) :
ce détail est conservé et testé.

Les pourcentages de PV/MP sont nuls à zéro et valent au moins 1 pour une valeur positive,
comme `StructCreature.h:651,654`.

Le coût MP n'est pas un prérequis : le proc peut partir à zéro MP et la soustraction est
bornée à zéro. Un refus de cumul consomme quand même les MP (`StructProc.cpp:83-85`).
Un état inconnu est ignoré ; aucune ressource ou formule n'est inventée.

## Raccordements et application

- `StateProcs` charge les compétences par type d'effet, indépendamment du catalogue des
  compétences lançables. `AttackProcConditions` porte les conditions communes, également
  utilisées par les passifs d'énergie 32262/32263.
- `CombatService.NotifyHit` traite les attaques des joueurs, des invocations et des monstres,
  les compétences offensives simples, de zone et à plusieurs coups, ainsi que les soins/buffs
  réussis. Les états actifs des trois types d'acteur peuvent fournir un proc.
- Un raté appelle l'esquive et n'appelle pas coup donné/reçu. Le blocage parfait a son propre
  événement. Le second coup d'une double attaque supprime seulement coup donné/reçu ;
  critique, blocage et esquive restent possibles (`StructCreature.cpp:3843-3855`).
- Chaque liste est résolue **avant** l'application de ses états, comme les clones officiels
  (`StructCreature.cpp:3563-3586`) : remplacer un état qui fournissait un autre proc ne
  supprime pas ce dernier pendant l'itération.
- Le relais `CastInterrupts` applique l'état via `SkillCastService` sans cycle d'injection.
  Cumul, remplacement, interruption par contrôle, statistiques, échéance et notifications
  empruntent le moteur existant. Le propriétaire du proc et ses statistiques sont conservés
  pour les dégâts périodiques ; les monstres conservent aussi leur id stable et leur génération.
- Les spectateurs reçoivent les états des monstres sous **leur propre handle**, y compris
  lorsque le bénéficiaire ou le lanceur est une invocation.
- La mort d'un joueur vient des chemins existants `DamagePlayer` / `LandPlayerDamage`.
  La mort par un état périodique appelle mort/meurtre, sans appeler les événements de coup.
  Un joueur déjà mort ne déclenche pas de nouveau proc lors d'une seconde application de dégâts.
- Pour un monstre, `TryKill` assure l'unicité. La distribution des récompenses déclenche le
  proc de meurtre de chaque bénéficiaire et de ses invocations présentes à **525 unités**
  du cadavre (`StructMonster.cpp:1066-1073`, `Rule/GameRule.h:22`). Le proc de mort du
  monstre est évalué avant le nettoyage de ses états. La mort d'une invocation frappée
  par un monstre passe par le chemin de mort existant de `CreatureService`.
- Un acteur mort refuse les états `EraseOnDead` ou `EraseOnResurrect`, comme
  `StructCreature.cpp:1346-1348`.

## Tests et contrôle en jeu

`Tests/Game/StateProcsTests.cs` couvre les 15 effets de compétence, les 17 effets d'état,
les courbes, les bornes de probabilité/PV/MP, armes, masques, éléments, coût MP et cumul,
les ratés/critique/blocages, un vrai tick d'attaque, une vraie compétence offensive,
un soin, les invocations, les handles des observateurs, le meurtre, la mort PvP et périodique.
`EnergyProcsTests` et `ServiceGraphTests` vérifient la mécanique commune et l'injection.

Validation du 2026-10-05 : `dotnet build Navislamia.sln -c Release` réussi (aucune erreur) ;
`dotnet test Tests/Tests.csproj -c Release` : **3 811 réussites, aucun échec**.
Les tests PostgreSQL marqués Explicit ne sont pas exécutés par cette commande.

En jeu : tester un passif appris sur attaque et critique, un effet reçu sur esquive/blocage,
un passif d'invocation avec le maître sans ce passif, puis un meurtre en groupe avec un
second observateur. Vérifier l'icône, le niveau, la durée, les statistiques et le coût MP.
Les probabilités doivent être observées sur plusieurs essais, pas sur un seul coup.

## Limites et NON ÉTABLI

- Les effets produits sont exécutés suivant les familles déjà portées par le moteur d'états.
  Par exemple, le déclencheur officiel **314084 → 314085** est posé à la mort avec au moins
  60 % de MP ; l'effet **3321** de 314085 (résurrection différée) n'est pas porté dans ce lot.
  La pose d'un état ne garantit pas que son effet particulier est déjà exécuté.
- Les modifications de cooldown et les procs de soin/vol/absorption sont d'autres familles.
  Les effets directement portés par l'équipement ne sont pas chargés par ce catalogue ;
  les états actifs provenant d'un équipement bénéficient bien des mêmes déclencheurs.
- La source 2015 emploie `flag ^ MISS` pour les compétences (`StructSkill.cpp:2828`), ce qui
  diffère d'un test du bit lors d'une combinaison inhabituelle « raté + autre flag ». Le
  moteur actuel ne produit pas ces combinaisons et teste le bit de raté. Le comportement
  d'une telle combinaison dans le binaire Epic 7 reste **NON ÉTABLI**.
- Le résultat visuel en client réel et les cas de scripts attribuant une compétence passive
  directement à un monstre sans passer par un état restent à vérifier. Aucun catalogue
  fictif de compétences apprises par les monstres n'a été ajouté.
