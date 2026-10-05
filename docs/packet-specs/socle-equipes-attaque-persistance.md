# Équipes d'attaque au redémarrage (2026-10-05)

Carte de board non créée : aucun connecteur de board disponible dans cette session.

## Sources officielles

Chemins sous `GameServer/Game/` : `Community/PartyLoader.cpp:19-24` charge type et `lead_party_id`,
`:264-276` détruit les groupes vides/sans chef/HuntaHolic/arène, `:281-359` lit la guilde du chef,
le donjon de raid et rétablit les liens. `Community/PartyManager.cpp:1452-1480` juge la capacité et
interdit deux équipes principales pour une guilde. Le chargement utilise les identités persistées,
pas les horaires de la session précédente. Aucune règle NGemity.

## Données et démarrage

Aucune migration : `Parties.PartyType`, `LeadPartyId`, `Characters.PartyId`, `Guilds.DungeonId`
et les alliances existent déjà (dernière migration sur master : Version0025).
`PartyStore` relit la guilde du chef et l'identité de l'alliance ; le donjon et le maximum de groupes
viennent de la guilde effective et du catalogue régional. `PartyService` persiste les types 0/1/2,
charge les groupes principaux avant leurs groupes liés puis valide guilde, donjon, type, capacité,
chef et membres. Un groupe invalide est supprimé avec ses appartenances. Les liens directs seulement
sont acceptés, comme `joinLinkedParty` : pas de chaîne ni cycle. Les HuntaHolic restent transitoires.

Avant ouverture du réseau : `parties.LoadAsync()` puis `guilds.LoadAsync()` rétablit `_teams`
et les identités de guildes ensemble. Les membres reviennent hors ligne ; leur entrée en jeu retrouve
`PartyId` et produit `LOGIN/PINFO`. Dissoudre une équipe persiste aussi sa suppression.
Écart robuste : tri des principaux avant les groupes liés ; l'officiel suppose les ids croissants.

## Tests et validation

`GuildTests.Attack_teams_and_guild_links_survive_restart_and_disband_is_persisted` : raid et siège,
alliance, stockage/relecture, appartenance, lien guilde et suppression. `PartyServiceTests.Persistence`
refuse absence de guilde/donjon/chef de lien, autre type/identité, dépassement de capacité et double
équipe principale. Les tests ordinaires de groupes et `ServiceGraphTests` restent requis.
Vérifier en jeu deux groupes d'une même équipe, redémarrage propre puis reconnexion des chefs et
membres, raid/siège durant leurs horaires, dissolution et second redémarrage.

## NON ÉTABLI

Arrêt brutal entre un changement en mémoire et sa sauvegarde asynchrone : même limite que les
groupes ordinaires. `FlushAsync` vide la file à l'arrêt propre ; pas de promesse de durabilité avant cela.
Aucune reprise des monstres/salles de raid en cours : les groupes reviennent, une nouvelle salle doit démarrer.
