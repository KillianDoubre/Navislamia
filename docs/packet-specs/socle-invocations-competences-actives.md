# Invocations : compétences actives et maintien

Livré le 2026-10-03. Implémentation : `SkillCastService.Summons.cs`,
`SkillCastService.SummonAuras.cs`, `CreatureService`, `CastInterrupts`, `CreatureEvents` et
`GmCommandService`. Tests : `SummonActiveSkillTests` et les scénarios invocation de `PvpTests`.

## 1. Lancement depuis le client

La requête `TM_CS_SKILL` (400) porte déjà le handle du lanceur. Si ce handle désigne une invocation
présente appartenant au joueur, le serveur utilise sa carte et ses compétences apprises. Le lancement
du personnage reste indépendant de celui de l'invocation.

- Comme `onSkill` officiel, un niveau demandé hors de `1..niveau appris` est ramené au niveau appris (la
  même règle vaut désormais pour le personnage, qui lançait toujours au niveau appris) ; le niveau de
  l'invocation doit satisfaire le niveau requis de la compétence. Un lanceur qui n'est pas une
  invocation du joueur répond `NotExist` ; la demande d'un maître mort est ignorée sans réponse.
- Invocation morte, montée, étrangère, absente ou immobilisée par un état interrompant les
  compétences : refus avant consommation de MP. Le maître doit être vivant.
- La portée part de la position interpolée de l'invocation, avec sa taille, sa portée d'attaque et
  les règles de portée communes. Couche et coordonnées sont vérifiées.
- Le coût utilise ses MP ; temps de lancement, délai commun et délai de réutilisation viennent des
  ressources. Les délais restent attachés à la carte pendant la session, y compris après renvoi
  et nouvelle invocation. Ils ne sont pas enregistrés entre deux connexions.
- Les messages 401 `Casting`, `CastingUpdate`, `Fire`, `RegionFire`, `Complete`, `Cancel` et le
  rappel 403 portent le handle et les constantes vitales de l'invocation. Les handles de monstres
  sont adaptés à chaque observateur.

## 2. Effets

Les familles déjà résolues par le catalogue sont disponibles : dégâts physiques et magiques,
zones, coups multiples et séquences, effets persistants au sol, soins, buffs, auras, malus et
résurrection de joueur. Les effets non classés par le catalogue restent indisponibles.

- Dégâts, précision, critiques et résistances utilisent les statistiques et le niveau de
  l'invocation. Les récompenses et la contribution aux dégâts passent par le maître ; la haine
  produite par ses dégâts et soins identifie l'invocation.
- Le PvP conserve les règles d'hostilité, résistances, bouclier de mana et facteur de dégâts
  communs. Une réflexion atteint l'invocation attaquante ; les conséquences d'une élimination
  sont attribuées au maître.
- Soins et états visent les joueurs et invocations autorisés par les ressources, selon le groupe,
  la portée et la couche. Les cibles `Master` (101) et `SelfWithMaster` (102) sont classées et
  résolues pour le lanceur invocation.
- Les auras portent le handle source de l'invocation. Leurs projections sont retirées à la
  désactivation, à la sortie de portée/groupe, à la mort, au renvoi et au changement de monde.
  Leur statut 407 et leurs états 505 sont envoyés aux nouveaux observateurs.
- Un effet au sol garde son centre et la puissance magique du premier déclenchement. Le lanceur
  peut agir de nouveau après sa création. L'effet cesse à l'expiration ou si son invocation
  quitte le monde ou meurt.

La mort, le renvoi, la téléportation, la déconnexion, les états bloquants et le maintien interrompent
un lancement en cours. Les dégâts peuvent le retarder ou l'interrompre selon `casting_type`.
La requête d'annulation 11 s'applique à son acteur : annuler l'invocation laisse le lancement du
maître en cours. Les annulations manuelles respectent `Cancelable` ; les interruptions forcées
du cycle de vie et du maintien l'emportent.

## 3. Commande de maintien

Commandes accessibles à tous les joueurs :

```text
/hold                     # maintien de toutes les invocations disponibles
/hold off                 # reprise du suivi
/hold <handle>            # maintien d'une invocation
/hold <handle> on
/hold <handle> off
```

Le maintien arrête l'attaque, le lancement en cours et le déplacement à la position interpolée
actuelle, puis envoie `STOP_MOVE`. Les demandes de suivi (`speed_sync != 0`) sont bloquées jusqu'à
`off`. Un ordre explicite de déplacement, d'attaque ou de compétence reste possible en maintien.
Une invocation morte, montée ou étrangère est refusée. Le mode est lié à sa présence en jeu :
une nouvelle entrée dans le monde repart sans maintien.

Cette commande est un ajout Navislamia. Dans la source cliente disponible,
`SGameLocalCreature::SetHold` règle localement `STATE_STANDBY` ; aucun paquet dédié de maintien
ni commande officielle correspondante n'a été identifié. Le bouton client de maintien doit
encore être vérifié en jeu ; `/hold` fournit le contrôle côté serveur.

## 4. Vérification

Les tests couvrent le paquet 400, l'annulation 11, la propriété du lanceur, les compétences
apprises, les MP propres, les délais et leur conservation après renvoi, les positions, les
interruptions, les résistances, les zones et séquences, les soins de groupe, les cibles 101/102,
les auras et observateurs tardifs, la réflexion PvP et la commande de maintien.
Une vérification des animations et commandes du client réel reste à effectuer en jeu.
