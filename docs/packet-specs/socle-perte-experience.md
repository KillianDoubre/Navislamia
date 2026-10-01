# Socle — perte d'expérience à la mort

| | |
|---|---|
| Paquets | `TS_SC_EXP_UPDATE` (1003), et sur perte de niveau `TS_SC_LEVEL_UPDATE` (1002), `TS_SC_STAT_INFO` (1000), propriétés `max_hp`/`max_mp`/`mp` — **aucun identifiant nouveau** |
| Référence d'autorité | `CaptainHerlockServer.exe` 2012-11 : `StructPlayer::GetDeadEXPPenalty` (`0x1400c0de0`), `StructPlayer::procDecreaseEXPAndDropItem` (`0x1400e35f0`), `StructPlayer::onExpChange` (`0x1400dc080`), `GameContent::GetNeedExp` (`0x140188890`) |
| Code | `LevelCurve.DeathPenalty`, `LevelingService.ApplyDeathPenalty`, `CombatService.DamagePlayer` |
| Tests | `Tests/Game/MovementAndDeathTests.cs` |

## 1. Règle officielle

- `perte = GetNeedExp(niveau) × (0,15 / (niveau − 1) + 0,0005)`, soit 0,3 et 0,001 sur un serveur PK.
  `GetNeedExp(niveau)` lit la table des niveaux (bornée à 1..300) : le seuil pour passer au niveau suivant.
  Cela fait 3 à 5 % d'un niveau (niveau 10 : 87 points pour un niveau de 1 725).
- Pas de perte si le tueur est un joueur (hors serveur PK), ni dans un donjon d'instance.
- `SetEXP` écrit la nouvelle valeur, puis `onExpChange` recalcule le niveau à partir du total : **un joueur peut
  redescendre d'un niveau**.
- La perte est retenue (`[+0x1d0]`) pour être rendue en partie par une résurrection.

## 2. Ce que fait Navislamia

- `CombatService.DamagePlayer` est le seul point où les dégâts d'un monstre tombent sur un joueur (coup normal ou
  compétence) : PV, propriété `hp`, et sur le coup qui fait passer à 0, fin de l'attaque du joueur et pénalité.
- `LevelingService.ApplyDeathPenalty` retire la perte (jamais sous 0), envoie 1003 et, si le total passe sous le
  seuil du niveau, descend le niveau : 1002 aux observateurs comme au joueur, stats, `max_hp`, `max_mp`, et `mp`
  ramené au nouveau maximum. Le joueur reste mort (0 PV).
- Seuls les monstres tuent ici : la condition « tué par un joueur » n'a pas encore de cas.

## 3. Écarts

- **Niveau 1 : aucune perte.** L'officiel divise par `niveau − 1` = 0, ce qui donne une valeur infinie.
- Le facteur des points d'immoralité (×3 au-delà d'un seuil, sinon `1 + 0,2 × …`) n'est pas modélisé : rien ne
  tient ces points.
- La perte n'est pas retenue pour la résurrection : aucune résurrection ne rend d'expérience ici.
- Les objets lâchés à la mort (même fonction officielle) ne sont pas modélisés.
