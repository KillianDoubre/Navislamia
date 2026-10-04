# Amis et liste de blocage (2026-10-04)

Aucun opcode : la fenêtre de messagerie du client 7.3 (`window_messenger_friend.nui`) envoie des lignes de chat et
lit les lignes `@FRIEND`. Les règles sont celles du serveur officiel 2015
(`A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer\Game`).

## 1. Sources

| Rôle | Source officielle |
|---|---|
| Commandes `/add_friend`, `/del_friend`, `/add_denial`, `/del_denial` | `Message/GameMessage.cpp:2108-2205`, table `:5568-5571` |
| Écriture et effets en ligne | `Db/DB_Friends.cpp` (`DB_InsertFriend`, `DB_DeleteFriend`) |
| Lignes `FLIST`, `DLIST`, `FSTATUS` | `Message/SendMessage.cpp:1712-1850` |
| Envoi à l'entrée en jeu | `SendMessage.cpp:1666-1672` |
| Déconnexion | `Struct/StructPlayer.cpp:1988` |
| Chuchotement refusé | `GameMessage.cpp:6679` (`RESULT_ACCESS_DENIED`) |
| Suppression d'un personnage | `Db/DB_DeleteCharacter.cpp:100-200` |
| Types de chat | `GameMessage.h:1880` (`CHAT_FRIEND` = 13), `:1898` (`CHAT_FRIEND_SYSTEM` = 140) |

Le client 7.3 porte les littéraux `FLIST`, `DLIST`, `FSTATUS`, `@FRIEND` et les quatre commandes (`SFrame.exe`).

## 2. Lignes

Émetteur `@FRIEND`. Les listes et statuts partent en type **140**, les messages lisibles en type **13**.

| Ligne | Format |
|---|---|
| liste d'amis | `FLIST|nom|statut|nom|statut|…` — statut 1 en ligne, 0 hors ligne, **2 en ligne mais bloquant le lecteur** (affiché hors ligne) |
| liste de blocage | `DLIST|nom|nom|…` |
| statut | `FSTATUS|nom|1|` (arrivée) ou `FSTATUS|nom|0|` (départ) |
| message à paramètre | `@467\v#@friend_name@#\vNom` (`\v` = 0x0B) |

## 3. Règles (`onAddFriend`… et `DB_InsertFriend`)

Dans l'ordre, pour `/add_friend nom` et `/add_denial nom` :

1. une ligne qui n'a pas exactement deux mots est ignorée sans réponse ;
2. soi-même → `@482` ;
3. nom invalide (4 à 18 lettres ou chiffres, `GameRule::IsValidName`) → `@481` ;
4. 25 entrées déjà → `@478` ;
5. personnage inexistant → `@481` ;
6. déjà dans la liste → `@469` (ami) / `@473` (blocage), avec le nom ;
7. sinon : écriture, liste renvoyée (`FLIST` ou `DLIST`), puis `@467` / `@472`.

Bloquer un joueur en ligne qui vous a dans ses amis lui envoie `FSTATUS|vous|0|` ; le débloquer, `FSTATUS|vous|1|`.

Pour `/del_friend nom` et `/del_denial nom` : absent → `@471` / `@475` avec le nom tapé ; sinon effacement, liste
renvoyée, `@470` / `@474`.

**Entrée en jeu** : `FLIST`, `DLIST`, puis `FSTATUS|moi|1|` à chaque joueur en ligne qui m'a dans ses amis, **sauf
ceux que je bloque**. **Sortie** (lobby et déconnexion) : `FSTATUS|moi|0|` aux mêmes.

**Chuchotement** vers un joueur qui bloque l'émetteur : `TS_SC_RESULT(TM_CS_CHAT_REQUEST, AccessDenied)`, rien n'est
transmis. Le blocage ne joue nulle part ailleurs chez l'officiel.

**Personnage supprimé** : il quitte les listes des joueurs en ligne, qui reçoivent leur `FLIST`/`DLIST` à jour.

## 4. Mise en œuvre

- `Game/Services/Friends/FriendService.cs` : règles, listes en mémoire des joueurs en jeu (un verrou global comme
  `s_FriendsLock`), une commande à la fois par joueur ; `FriendMessages` construit les lignes.
- `FriendStore` : table Telecaster **`CharacterFriends`** (`OwnerId`, `TargetId`, `IsDenial`, migration
  `Version0023_CharacterFriends`), unique sur le triplet tant que la ligne n'est pas effacée.
- Branchements : `GameClient.HandleChatRequest` (avant les commandes de groupe ; blocage du chuchotement),
  `GameActions.OnLogin` (après les guildes), les deux sorties de `GameClient`, la suppression de personnage.

## 5. Écarts assumés

- **Ids au lieu de noms.** L'officiel range les noms (`owner_id`, `friend_id`) et doit réécrire les listes à chaque
  renommage ou suppression (`StructPlayer::OnChangeName`, `DB_DeleteCharacter`). Ici les lignes portent les ids des
  personnages et les noms sont relus dans `Characters` : un renommage suit tout seul après reconnexion, un
  personnage supprimé (effacement logique) disparaît des listes chargées.
- **Les listes « de qui m'a pour ami » ne sont pas chargées.** L'officiel lit `friendofs` et `denialofs` à la
  connexion ; elles ne servent qu'à prévenir les joueurs en ligne, donc elles sont calculées depuis les listes des
  joueurs en jeu.
- **Écriture avant mémoire.** L'officiel met la mémoire à jour avant la base et l'annule en cas d'échec ; ici la
  base passe d'abord, et un échec répond `@103` sans rien changer.
- `game.allowed_names_as_friend` (noms invalides acceptés, vide par défaut) n'est pas modélisé.

## 6. Correctif de sécurité livré avec ce lot

`TS_CS_DELETE_CHARACTER` supprimait **n'importe quel personnage dont on donnait le nom**, et `TM_CS_CHARACTER_LIST`
listait les personnages du compte nommé **dans la trame**. Désormais la liste est celle du compte authentifié
(`ConnectionInfo.AccountName`) et seule un nom de cette liste peut être supprimé (`AccessDenied` sinon).

## NON ÉTABLI

- Que le client affiche `@103` comme l'officiel affichait `GetString(103)` suivi de `(4)` ou `(5)`.
- L'effet exact du statut 2 dans la fenêtre (l'officiel le commente « vu hors ligne »).
- `/change_name` (lot de Codex) : les listes en mémoire des joueurs en ligne gardent l'ancien nom jusqu'à leur
  prochaine entrée en jeu ; l'officiel les réécrit (`OnChangeName`).

Tests : `Tests/Game/FriendServiceTests.cs`, `FriendPostgreSqlTests.cs` (explicite, `NAVIS_FRIEND_TEST_CONNECTION`).
