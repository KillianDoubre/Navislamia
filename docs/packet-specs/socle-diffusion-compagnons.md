# Socle — familiers et invocations vus par les autres joueurs

| | |
|---|---|
| Paquets | `TM_SC_ENTER` (3, `EOT_Pet` 95 octets, `EOT_Summon` 96 octets), `TM_SC_LEAVE` (9), `TM_SC_MOVE` (8) — **aucun identifiant nouveau** |
| Code | `Game/Services/CompanionFrames.cs`, `PetWorldService`, `SummonWorldService`, `PetBehavior.Move`, `PlayerVisibilityService` |
| Tests | `Tests/Game/BroadcastTests.cs` |
| Fiches voisines | `socle-familier-pet.md`, `socle-invocations.md`, `socle-visibilite-joueurs.md` |

## 1. Règle

Un observateur voit les compagnons d'un joueur **tant qu'il voit ce joueur** :

- quand le maître entre dans sa vue : `ENTER` du maître, son équipement (202), **puis** l'`ENTER` de son
  familier et de chacune de ses invocations ;
- quand le maître sort de sa vue (déplacement, téléportation, retour au lobby, déconnexion) : le `LEAVE` des
  compagnons **avant** celui du maître ;
- quand le maître appelle ou range un compagnon : l'`ENTER` ou le `LEAVE` de ce compagnon ;
- quand le familier marche : son `TS_SC_MOVE`, sur l'horloge de chaque observateur.

Un compagnon a un handle **global** (`WorldObjectHandle`), contrairement à un monstre : la même trame sert à
tous. L'observateur reçoit l'objet avec `is_first_enter = 0` et **jamais** la fenêtre de créature
(351 `ADD_PET_INFO`, 301 `ADD_SUMMON_INFO`) ni le 350/305 de rangement, qui sont propres au maître : un
`LEAVE` (9) suffit à retirer l'objet.

## 2. État de session

- Familier : `ConnectionInfo.ActivePet` (déjà là).
- Invocations : `ConnectionInfo.Summons`, un tableau remplacé entier à chaque changement sous `SummonLock`,
  tenu par `SummonWorldService.Enter`/`Leave`.

La visibilité lit ces deux références **sans verrou**. Elle tient alors le verrou de visibilité d'un
observateur, alors que le chemin du familier prend le verrou du familier puis ceux de visibilité (diffusion) :
lire sous `PetLock` créerait un ordre inverse, donc un interblocage possible. Une lecture de référence est
atomique ; une position de familier lue en plein déplacement est au pire en retard d'un pas.

## 3. Écarts et limites

- **Aucune invocation n'existe encore en jeu** : `SummonWorldService` n'a toujours pas d'appelant (sorts 4001 à
  4003 refusés). La diffusion est câblée et testée, et s'appliquera dès qu'un appelant existera.
- Un joueur qui entre dans la vue entre l'`ENTER` d'un familier et son enregistrement dans la session peut ne
  pas le voir avant le prochain renouvellement (rappel, téléportation) ; l'invocation, elle, est enregistrée
  avant d'être annoncée.
- Le ramassage du familier (`item_taker` = familier) reste envoyé au seul maître.
