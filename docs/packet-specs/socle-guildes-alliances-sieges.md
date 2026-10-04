# Guildes, alliances, raids et sièges

**Suite livrée le 2026-10-04** : paquets 650–653/660 et 50003/50004/50009, emblèmes,
notices, annonces et classement. Voir `socle-guildes-emblemes-annonces-classement.md` ;
titres de siège vérifiés dans `socle-titres-siege-pk-pcbang.md`.

Lot de Codex (2026-10-03), fusionné avec ses donjons (`socle-donjons-instances-secrets.md`). Cette fiche est
rédigée **à partir de son code et de ses tests** (`Game/Services/Guilds/`, `Tests/Game/GuildTests.cs`) : la
source officielle de chaque règle n'est pas citée ici, à compléter.

## 1. Données

Migration Telecaster `Version0017_GuildsAndSieges` : `Guilds` gagne `LeaderId`, `NormalizedName` (unique, sans
casse), alliance et donjon possédés ; `Characters` gagne `GuildPermission` (rang 1-7) et `GuildMemo` ; nouvelles
tables `GuildRaids` (inscription et temps par semaine), `GuildSieges` et `GuildSiegeParticipants`. Le groupe de
guilde reste en mémoire (`GuildRuntime`). Essai PostgreSQL : `GuildPostgreSqlTests` (`Explicit`).

## 2. Guilde

- **Création** : chez un officier de guilde (PNJ 1012, 2012, 4012, 6012, 7037), `show_guild_create()` ouvre la
  fenêtre de saisie (trame 650 ; 660 pour une alliance), le client renvoie `/gcreate nom`, le serveur demande
  confirmation, puis crée : niveau 20, 100 000 or, nom unique (1-30 lettres, chiffres ou espaces). Rien n'est
  débité si la confirmation est périmée ou refusée.
- Commandes de chat : `/ginvite`, `/gjoin` (invitation liée au destinataire, à durée limitée, consommée une fois),
  `/gkick`, `/gleave` (sept jours avant de rejoindre une guilde), `/gpromote`, `/gpermission`, `/gpermissionset`,
  `/gpermissionname`, `/gnotice`, `/gurl`, `/gmemo`, `/ginfo`, `/gdestroy`. 200 membres au plus ; le rang protège le
  chef et les rangs supérieurs. Lignes système sur les canaux `GuildSystem` (110) et `AllianceSystem` (150).

## 3. Alliance

`/gacreate`, `/gainvite`, `/gajoin`, `/gakick`, `/galeave`, `/gadestroy` : trois guildes au plus, identité commune
(une guilde alliée compte pour le propriétaire d'un donjon), délai après un départ, départ refusé à une guilde qui
possède un donjon. En mode PK, les alliés sont amis ; un duel l'emporte.

## 4. Raids, sièges, taxes

- **Raid** : `/graid <donjon>` inscrit la guilde pour la semaine (horaires de raid du catalogue régional), `/graidcancel`
  l'annule ; l'équipe d'attaque se forme par `/rpcreate`, `/rp_ginvite`, `/rp_gjoin` ; un temps n'est enregistré
  qu'après les deux boss. Classement hebdomadaire : la plus rapide, rattrapé après un redémarrage.
- **Siège** : horloge de siège, le cœur (un monstre) tombé échange attaque et défense, un donjon neutre revient à la
  guilde qui abat le cœur, la fin règle la défense ; titres crédités une fois, hors ligne compris. Un joueur tué en
  siège ne perd ni expérience ni moralité.
- **Taxe** : `/gtax 1..10` (gestion du donjon), prélevée sur l'or et le chaos tirés dans le donjon avant partage ;
  `/gwithdraw gold|chaos` une fois ; `/gdropdungeon` (chef) abandonne le donjon, sept jours de blocage.

## 5. À vérifier en jeu / NON ÉTABLI

- Les formats exacts des lignes `@GUILD`/`@ALLIANCE` attendues par la fenêtre de guilde du client 7.3 et les trames
  650/660 n'ont pas été mesurés dans le client pour ce lot.
- L'emblème, la publicité et le classement de guilde ne sont pas couverts.
