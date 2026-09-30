# Socle — groupe (party) : commandes de chat et lignes `@PARTY`

| | |
|---|---|
| Paquets | `TM_CS_CHAT_REQUEST` (20) en entrée, `TM_SC_CHAT` (22) type **100** en sortie — **aucun identifiant nouveau** |
| Référence d'autorité | serveur officiel `CaptainHerlockServer.exe` du dump Epic 7 Part 4 (`reference/epic7part4/server/2012-11/`, PDB assortie), lu par désassemblage et symboles |
| Référence secondaire | NGemity/Chihiro `AllowedCommandInfo.cpp`, `Messages.cpp`, `GroupManager.cpp` (portage partiel et parfois divergent) |
| Client | `reference/client73/SFrame.exe` (chaînes : commandes et mots-clés lus, §2) |
| Code | `Game/Services/Party/PartyService.cs`, `PartyMessages.cs` ; tests `Tests/Game/PartyServiceTests.cs`, `ChatRoutingTests.cs` |

## 1. Réponses courtes

1. **Le groupe n'a pas d'opcode en 7.3.** La fenêtre de groupe du client envoie des **lignes de chat**
   (`/pcreate`, `/pinvite`…) et lit des **lignes système** `TS_SC_CHAT` de type 100 dont l'émetteur est
   `@PARTY`. Le serveur ne fait que lire des commandes et écrire des chaînes.
2. **Les formats viennent du serveur officiel**, pas de NGemity : NGemity écrit par exemple
   `CREATE|nom|chef|` et `PINFO|nom|chef|…`, alors que l'officiel écrit `CREATE|%s|%s|%d|` et
   `PINFO|%d|%s|%s|%d|%d|%d|%d|` (l'id du groupe en tête). Chaque format est rattaché à la fonction qui
   l'émet (§3).
3. **Les groupes vivent en mémoire** (§6) : ils survivent à la déconnexion d'un membre, pas au
   redémarrage du serveur.

## 2. Côté client (`SFrame.exe`, lecture des chaînes)

Commandes émises : `/pcreate %s`, `/pinvite %s`, `/pjoin %d %d`, `/pjoin %d`, `/pleave`, `/pdestroy`,
`/pkick %s`, `/ppromote %s`, `/pshare %s`, `/plist`, `/pinfo`, `/passist %u`. Mots-clés lus près de
`MINFO` (VA `0x61e68c`) : `MINFO`, `NEW`, `LOGIN`, `JOIN`, `LOGOUT`, `PINFO`, `DESTROY`, `LEAVE`, `KICK`,
et `party_update` / `delete_party_wnd` pour la fenêtre.

## 3. Serveur officiel : messages et règles

Toutes les lignes partent en `SendChatMessage(…, 100, "@PARTY", joueur, texte)` ou, pour tout le groupe,
en `PrintfPartyChatMessage(100, idGroupe, format, …)` qui itère `DoEachMember`.

| Évènement | Fonction (VA) | Ligne | Destinataires |
|---|---|---|---|
| création | `onPartyCreate` `0x14010dca0` | `CREATE|groupe|chef|0|` puis `PINFO` | le créateur |
| invitation | `onPartyInvite` `0x140120ad0` | `INVITE|invitant|groupe|id|motdepasse|` | la cible |
| adhésion | `onPartyJoin` `0x140121300` | `NEW|membre|` **avant** l'ajout ; puis `JOIN|groupe|`, `PINFO`, `MINFO` | membres ; le nouveau ; le nouveau ; tous |
| départ | `onPartyLeave` `0x140122120` → `BroadcastPartyLeave` | `LEAVE|membre|` avant le retrait | tous, le partant compris |
| exclusion | `onPartyKick` `0x140121ad0` | `KICK|groupe|membre|` avant le retrait | tous, l'exclu compris |
| promotion | `onPartyPromote` `0x140112530` | `PROMOTE|nouveauchef|` | tous |
| dissolution | `onPartyDestroy` `0x1401207a0` → `BroadcastPartyDestroy` | `DESTROY|groupe|` | tous |
| partage | `onPartyShareMode` `0x140120600` | `MODE|n|` (monopoly 0, random 1, linear 2) | tous |
| entrée en jeu | `SendLoginResult` `0x140152f90` | `LOGIN|groupe|membre|`, `MINFO`, `PINFO` | tous ; tous ; le membre |
| sortie | `StructPlayer::LogoutNow` | `LOGOUT|membre|` | les autres |
| PV/PM | `onHPChange`, `onMPChange`, `onDead` → `BroadcastPartyMemberInfo` | `MINFO|…` | tous |

`PINFO|id|groupe|chef|partage|niv.max|niv.min|type|` puis une entrée par membre (`SendPartyInfoByID`
`0x140150370`, foncteur `0x140147b90`) :

- en ligne : `handle|nom|race|métier|PV%|PM%|x|y|2|` — `MINFO|` suivi de la même entrée
  (`BroadcastPartyMemberInfo` `0x140146370`) ;
- hors ligne : `0|nom|%d|%d|0|0|0|0|0|`, les deux entiers lus dans l'étiquette de membre (+0x30, +0x34).

Pourcentage : `(int)(v × 100 / max)` tronqué à l'octet, **1 et non 0** tant que la valeur n'est pas nulle.
Niveaux min/max : ceux des membres **en ligne** (`GetMinLevel` `0x14026cdf0`).

Refus (ligne `@PARTY` au demandeur) : `ERROR_ALREADY_PARTY_MEMBER`, `INVALID_PARTY_NAME`, `ALREADY_EXIST`,
`CANT_JOIN`, `ERROR_TARGET_ANOTHER`, `ERROR_MAX` (8 membres), `ERROR_YOU_CAN_JOIN_ONLY_ONE_PARTY`,
`ERROR_INVALID_PARTY`, `HAS_NO_AUTHORITY` (mot de passe). Silence : non-chef qui invite, exclut, promeut,
dissout ou change le partage ; cible d'invitation hors ligne ; **chef qui quitte** ; nom de plus de 30.

## 4. Ce que fait Navislamia

`PartyService` reproduit le tableau du §3 ; un verrou unique protège l'état, et chaque trame est enfilée
sous ce verrou (l'ordre des lignes est celui du tableau). `ConnectionInfo.PartyId` est tenu par le
service (création, adhésion, entrée en jeu ; remis à `null` au départ, à l'exclusion, à la dissolution)
et sert au chat de groupe (type `0x0A`). `GameClient.HandleChatRequest` passe toute ligne `/…` (hors
chuchotement) au service **avant** les commandes MJ ; ce qui n'est pas une commande de groupe continue
vers elles. `MINFO` suit `SendVitalProperty` et la régénération, **seulement quand un pourcentage change**.

## 5. Écarts assumés

- **`NEW` après le contrôle de taille** : l'officiel annonce `NEW` puis peut refuser `ERROR_MAX` ; ici la
  taille est jugée d'abord, les membres ne sont jamais prévenus d'une adhésion qui n'a pas lieu.
- **Nom de groupe** : 1 à 30 lettres ou chiffres ASCII, et pas de mot interdit. `GameRule::IsValidName`
  n'est pas lu ; ce sous-ensemble ne peut jamais contenir `|`, qui casserait chaque ligne.
- **Non modélisés** : HuntaHolic, donjons d'instance, match à mort, règle PK d'invitation
  (`IsPartyInvitable`), équipes d'attaque et de raid (`/passist`, `RMINFO`, `SINFO`), invocations des
  membres (`SendSummonInfo`), partage d'expérience et de butin (le mode est tenu et diffusé, sans effet).

## 6. Persistance

Aucune. `Characters.PartyId` n'est plus lu à l'entrée en jeu : la table `Parties` du dépôt n'a pas le mot
de passe d'invitation et son `LeadPartyId` auto-référent est obligatoire. Un groupe survit à la sortie de
ses membres (hors ligne dans `PINFO`) et disparaît au redémarrage, ou quand son dernier membre le quitte.

## 7. NON ÉTABLI

- Le sens des deux entiers de l'entrée hors ligne (+0x30, +0x34 de l'étiquette) : lus comme niveau puis
  métier, dans l'ordre de `PartyMemberTag` de NGemity.
- Le `%d` final de `CREATE` : l'officiel y écrit 0.
- `/pjoin %d` sans mot de passe : l'officiel refuse `HAS_NO_AUTHORITY` faute de second jeton ; son usage
  côté client n'est pas identifié.
- Ce que la fenêtre du client fait d'un `LEAVE` ou d'un `KICK` qui le nomme lui-même : fermeture
  attendue (`delete_party_wnd`), non observée en jeu.
