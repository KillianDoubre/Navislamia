# Guildes : emblèmes, annonces et classement

Implémentation du 2026-10-04. Complète `socle-guildes-alliances-sieges.md`.

## Sources

Sources locales sous `A:\Rappelz Kiff\Rappelz\Rappelz\program\server` :

- `GameServer/Game/Message/GameMessage.h` : `TS_SC_SHOW_CREATE_GUILD`, `TS_SC_OPEN_GUILD_WINDOW`,
  `TS_SC_UPDATE_GUILD_ICON`, `TS_SC_UPDATE_GUILD_BANNER`, `TS_SU_REQUEST_UPLOAD`, `TS_US_UPLOAD`.
- `GameServer/Game/Message/GameMessage.cpp` : `onGuildIcon`/`onGuildBanner` (1666/1687),
  `onGuildNotice` (4912), `onUpdateGuildIcon` (5074), `onUpload` (11068).
- `GameServer/Game/Message/SendMessage.cpp:2912` : `SendGuildAdvertiseInfo`, chaîne `GADV`.
- `GameServer/Game/Community/GuildManager.cpp:1828` : cumul entier de `DonationPoint`.
- `UploadServer/Upload/UploadServer.cpp:305` : retour 50009 ; nom sans NUL final.
- Le client disponible `program/client_10_13/game/Interface/SUIMarkPanelWnd.cpp:245` envoie `/gupdateicon`.

## Paquets déclarés

Tous ont l'en-tête habituel de 7 octets, checksum des six premiers octets, champs sans alignement.

| ID | Direction | Nom | Taille | Corps, offsets absolus |
|---|---|---|---|---|
| 650 | serveur → joueur | `TM_SC_SHOW_CREATE_GUILD` | 7 | aucun |
| 651 | serveur → joueur | `TM_SC_OPEN_GUILD_WINDOW` | 7 | aucun |
| 652 | serveur → joueur | `TM_SC_UPDATE_GUILD_ICON` | 51 | `int32 client_id` 7, `int32 account_id` 11, `int32 one_time_password` 15, `char raw_server_name[32]` 19 |
| 653 | serveur → joueur | `TM_SC_UPDATE_GUILD_BANNER` | 51 | identique à 652 |
| 660 | serveur → joueur | `TM_SC_SHOW_CREATE_ALLIANCE` | 7 | aucun |
| 50003 | jeu → upload | `TM_SU_REQUEST_UPLOAD` | 24 | `int32 client_id` 7, `account_id` 11, `guild_sid` 15, `one_time_password` 19, `uint8 type` 23 |
| 50004 | upload → jeu | `TM_US_REQUEST_UPLOAD` | 9 | `uint16 result` 7 ; acquittement sans effet, comme l'officiel |
| 50009 | upload → jeu | `TM_US_UPLOAD` | 17 + N | `int32 guild_id` 7, `file_size` 11, `uint8 filename_length` 15, `type` 16, N octets ASCII de nom en 17 |

Type 0 : emblème ; type 1 : bannière. Les 32 octets du nom serveur contiennent au plus 31 octets
ASCII puis NUL. `GameGuildPackets` et `GuildUploadPackets` construisent/lisent les trames.
`UploadClient` accepte le retour après son authentification. Ce sont des paquets entre serveurs,
distincts du protocole par lequel le client transmet son image à UploadServer.

## Cycle de l'emblème

1. `/gupdateicon` requiert `UpdateGuildIcon` (8192) ; `/gupdatebanner` requiert
   `AdvertiseManagement` (16384). Le chef possède les droits.
2. Une connexion upload prête est requise. OTP aléatoire cryptographique, persistance sur le personnage,
   puis 50003 au serveur d'upload et 652/653 au joueur, avec le même OTP.
3. Une demande est liée au personnage et à la guilde, valable cinq minutes. Une seule demande par
   personnage, et par guilde/type. Une demande en vol n'est pas reprise après un redémarrage.
4. Au retour 50009 : taille exacte, type, nom de fichier simple et taille positive ; vérification
   de la demande, de l'OTP, de l'appartenance et des droits actuels. Aucun chemin arbitraire.
5. `Guilds.Icon/IconSize` ou `Banner/BannerSize` sont persistés ; OTP et demande sont consommés.
   Le retour ne peut être rejoué. `GICON`/`GBANNER` sont diffusés pour rafraîchir les caches.

`/gicon <id>` et `/gbanner <id>` sont accessibles aux non-membres. Réponse officielle via le paquet
21, émetteur `@GUILD`, type 110 : `GICON|id|nom|url|taille` ou `GBANNER|id|nom|url|taille`.

### Réglages nécessaires pour le client

Le serveur d'upload et son hébergement HTTP restent ceux de l'installation. Configurer les URL
publiques réelles dans la section `Server` ; elles sont concaténées au nom renvoyé par UploadServer :

```json
{
  "Server": {
    "Name": "Navislamia",
    "GuildIconUrl": "https://votre-serveur/emblemes/",
    "GuildBannerUrl": "https://votre-serveur/bannieres/"
  }
}
```

Une URL non configurée ne produit pas d'adresse inventée. La publication du fichier relève
d'UploadServer ; aucun serveur HTTP supplémentaire n'est créé dans GameServer.

## Annonces et classement

- `/gnotice <texte>` persiste la notice, puis diffuse `NOTICE|texte` à la guilde.
- `GADV|type|secondes_restantes|commentaire|` est envoyé avec les informations de guilde.
  Une annonce expirée est présentée comme `GADV|0|0||`.
- `/gadvertise <type 0..3> <minutes> <texte>` est une **commande ajoutée au dépôt** : droit
  `AdvertiseManagement`, maximum 30 jours et 128 caractères ASCII sans délimiteur. Type 0 désactive
  l'annonce ; les autres types reprennent `AdvertiseType` : liste, normale, premium. Aucun tarif
  commercial officiel n'est établi ; cette commande n'effectue aucun achat.
- `/glist` affiche jusqu'à 30 annonces actives, triées par type puis identifiant.
- `/granking` affiche les dix guildes au plus grand `DonationPoint`, puis identifiant en cas
  d'égalité. La donation 258 alimente ce cumul dans sa transaction, par points moraux entiers.
  Le cumul de guilde reste indépendant du classement individuel mensuel.
- Ces deux listes sont des commandes du dépôt affichées dans le chat système. Aucun opcode de
  classement/publicité de guilde n'est établi dans le protocole 7.3 disponible ; aucun numéro n'est inventé.
- `/gwindow` et le choix « Ouvrir la guilde » du PNJ officier envoient 651 après contrôle d'appartenance.

## Vérification

`GuildMediaTests` : offsets et checksum, OTP partagé, permissions, expiration, retrait de droits,
retour rejoué/refusé, nom de fichier hostile, persistance, lecture par un non-membre et annonces.
`GuildTests` couvre la gestion et les titres de siège. `CommunityPostgreSqlTests` vérifie le cumul de dons
en PostgreSQL et son rollback avec le don. L'upload de bout en bout avec le client 7.3 et l'hébergement
HTTP réel reste à vérifier en jeu.
