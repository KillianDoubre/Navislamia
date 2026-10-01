# Socle — diffusion du combat aux autres joueurs

| | |
|---|---|
| Paquets | `TM_SC_ATTACK_EVENT` (101), `TM_SC_SKILL` (401), `TM_SC_MOVE` (8), `TM_SC_STATUS_CHANGE` (500), `TM_SC_LEAVE` (9) — **aucun identifiant nouveau** |
| Référence | NGemity `Unit::broadcastAttackMessage` (`Unit.cpp:1307`), qui diffuse chaque coup à la région ; `Unit::ResurrectByState` → `Messages::BroadcastHPMPMessage` |
| Code | `Game/Services/ObserverFrames.cs`, `CombatService`, `MonsterAiService.ToOtherWatchers`, `SkillCastService.SendSkill`, `MonsterSkillService` |
| Tests | `Tests/Game/BroadcastTests.cs` |

## 1. Ce qui manquait

Les autres joueurs voyaient un joueur entrer, marcher, s'équiper, perdre et regagner des PV (509), s'asseoir et
passer en mode PK (500, `GameClient.SendActorStatus`, déjà diffusé à l'entrée dans la vue comme au
changement). Ils ne voyaient **aucun combat** : ni les coups du joueur, ni ceux des monstres sur lui, ni ses
compétences, ni la mort d'un monstre qu'il tuait, ni sa propre mort.

## 2. La mort d'un joueur vue par les autres

Le client 7.3 n'a pas de paquet de mort (`TM_SC_DEAD` 504 est libéré sans effet) : un joueur meurt quand ses
PV passent à 0. Le joueur l'apprend par le `target_hp` du coup fatal et la propriété `hp` ; un observateur, par
le **même coup fatal**, diffusé désormais, et par le `TS_SC_HPMP` (509) déjà envoyé. La résurrection sur place
passe par le 509 (comme `ResurrectByState` de la référence), celle en ville par la téléportation, qui renvoie
l'`ENTER` du joueur. **Ne jamais envoyer 500 + `1 << 8` pour un joueur** : ce bit est « assis ».

## 3. Le handle du monstre diffère par client

Un monstre a un handle par client (à partir de `0x40000000`), et chaque client a son propre décalage
d'horloge : une trame de combat ne peut pas être copiée, elle est **reconstruite pour chaque destinataire**
avec son handle du monstre (et son horloge pour un déplacement). Un destinataire qui ne voit pas le monstre ne
reçoit rien.

| Trame | Destinataires |
|---|---|
| coup du joueur (101), fin d'attaque (101 `EndAttack`) | observateurs du joueur qui voient le monstre |
| mort du monstre (500 `1 << 8`) puis `LEAVE` différé de 6 s | idem : chacun a son cadavre et son `LEAVE` |
| compétence du joueur sur un monstre (401) | idem, cible et coup réécrits avec leur handle |
| compétence du joueur sur lui-même (bonus, aura, soin) | observateurs du joueur, trame identique |
| coup d'un monstre sur un joueur (101) | clients qui voient le monstre **et** le joueur |
| poursuite, retour et arrêt d'un monstre (8) | clients qui voient le monstre, chacun sur son horloge |
| compétence d'un monstre (401) | observateurs du joueur visé qui voient le monstre |

Les trames d'un monstre envoyées par l'IA partent à **tous** les clients qui le voient (l'IA tient la liste des
clients) ; celles du combat du joueur et des compétences, aux **observateurs du joueur** (ces services ne
dépendent pas de `NetworkService`, ce qui créerait le cycle d'injection connu).

## 4. Écarts assumés

- Un joueur qui voit le monstre mais pas le joueur qui le tue ne reçoit ni le coup ni la mort : il retire le
  cadavre à sa prochaine synchronisation.
- L'activation d'un objet du décor (compétence 9501) reste privée : le handle d'un objet du décor est par
  client et rien ne dit quel observateur le voit.
- Les états (505) posés par une compétence ne sont pas diffusés, ni ceux du joueur ni ceux d'un monstre ; l'aura
  (407) non plus.
- Le blocage du joueur sur place pendant son coup (arrêt de déplacement) n'est envoyé qu'à lui.
