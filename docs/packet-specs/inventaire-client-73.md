# Inventaire des systèmes du client 7.3 (2026-10-04)

Objectif : savoir ce qui manque encore pour un émulateur 7.3 complet, à partir de ce que le client sait faire.
Rien n'est déduit de la seule absence d'un nom de paquet : la table id → nom du client est partielle.

## Méthode

Trois sources ont été croisées.

1. **Aiguillage entrant de `SFrame.exe`** (`tools/client73_packets.py`). L'entrée est en `0x67DF59` et l'aiguillage est
   un arbre `cmp`/`jg` avec des tables de saut MSVC aux feuilles. Chaque id de 1 à 11 000 y est routé par simulation :
   - `0x67EF21` est le cas « paquet non traité » ;
   - `0x67EF39` est le cas vide (traité, sans effet).

   Résultat : **131 ids traités** par le client, dont 4 sans effet (53, 55, 58, 504).
2. **Journaux du serveur** (`DevConsole/logs`). Tous les « Undefined packet ID » vus en jeu sont désormais déclarés.
3. **Serveur officiel** : `GameMessage.h` pour les ids, et la table des commandes de chat (`GameMessage.cpp:5500-5580`).
   Le groupe, les guildes et les amis y passent par des commandes `/…`, sans opcode.

## Paquets que le client traite et que le serveur n'émet jamais

| id | paquet | rôle chez l'officiel | priorité |
|---|---|---|---|
| 406 | `TS_SC_STATE_RESULT` | dégâts et soins périodiques d'un état (poison, régénération) affichés au tick, `StructCreature.cpp:872` | **livré le 2026-10-05** (`socle-etats-periodiques-energie.md`) |
| 515 | `TS_SC_ENERGY` | compte d'énergie diffusé (`StructPlayer::onEnergyChange`) | **livré le 2026-10-05** |
| 901 | `TS_SC_CHANGE_LOCATION` | réponse à `TM_CS_CHANGE_LOCATION` (900) : lieu précédent et lieu courant | **livré le 2026-10-04** avec la météo (`901-change-location.md`) |
| 102 | `TS_SC_CANT_ATTACK` | attaque impossible | **livré le 2026-10-05** |
| 30 | `TS_SC_CHANGE_NAME` | changement de nom (personnage, familier) | avec `/change_name` |
| 322 | `TS_SC_SHOW_SUMMON_NAME_CHANGE` | fenêtre de renommage d'une invocation | **livré le 2026-10-05** (`322-show-summon-name-change.md`) |
| 451 | `TS_SC_SKILL_LEVEL_LIST` | niveaux de compétences (réponse officielle à **452** ; 450 commenté) | **livré le 2026-10-05** (`451-skill-level-list.md`) |
| 512 / 514 | `TS_SC_TARGET` / `TS_SC_SP` | cible du joueur destinataire ; SP des invocations | **livré le 2026-10-05** (`512-target.md`, `514-sp.md`) |
| 3003 / 3004 | `TS_SC_SHOW_WINDOW` / `TS_SC_GENERAL_MESSAGE_BOX` | fenêtre ou boîte ouverte par un script | **livré le 2026-10-05** (`3003-show-window.md`, `3004-general-message-box.md`) |
| 650-652, 660 | fenêtres de guilde, icône de guilde | **lot de Codex** (emblème) | — |
| 9000 / 9001 | `TS_SC_OPEN_URL` / `TS_SC_URL_LIST` | ouverture d'URL (boutique web) | hors périmètre |
| 9002-9011 | numéro de sécurité | création, changement, effacement (le client envoie aussi 9006-9012) | hors périmètre (décision de Killian, 9005) |
| 10001-10025 | boutique d'objets payants | ouverture de la boutique, sans source établie | hors périmètre sans boutique |
| 6003/6005/6007 | résultats de la ferme | ferme non implémentée (décision) | — |
| 103, 506 | sans nom (`TM_SC_HAVOC` pour 506) | non établi | — |

## Systèmes absents

- **Amis et liste de blocage** : livrés le 2026-10-04 (`socle-amis.md`).
- **Énergie** (livrée le 2026-10-05, `socle-etats-periodiques-energie.md`). Quinze compétences jouables coûtent de l'énergie (`CostEnergy`, effets 301, 505 et 30001-30016), et
  d'autres en produisent (`StructSkill.cpp:6420`, `7023`). L'officiel refuse le lancement sans énergie
  (`RESULT_NOT_ENOUGH_ENERGY`). Ici, ces compétences partent gratuitement et le compteur n'existe pas.
- **Commandes de chat officielles** (livrées le 2026-10-05 : `/passist`, `/change_name` et les commandes MJ ci-dessous) :
  - `/passist` (assistance de groupe) et `/change_name` ;
  - côté MJ : `block_chat`, `check_auto_user`, `force_warp`, `invisible`, `kick`, `rebirth` et `lv`.
  - Les commandes de guilde `gicon`, `gupdateicon`, `gbanner`, `glist`, `change_guild_name` et
    `change_alliance_name` reviennent au lot de Codex.
- **Pas de courrier** dans ce client : aucune chaîne, aucun paquet, rien chez l'officiel.

## Ce qui n'est pas un manque

- **Arènes de bataille (4700-4719)** : elles datent de l'Epic 8.1 (`socle-arenes-bataille.md`), et le client ne les
  aiguille pas.
- **Recherche de groupe (7000-7002)** et **décomposition (265/266)** : le client 7.3 ne les aiguille pas.
- **Titres (625-636)** : le client ne les aiguille pas, ce qui confirme les commandes `/titles`, `/title` et
  `/subtitle`.
- `TS_SC_SKIN_INFO` (224) est émis alors que le client ne l'aiguille pas : sans effet, mais sans danger.

## Trous connus (état au 2026-10-05)

Ce qui reste pour un émulateur 7.3 complet, une fois tous les systèmes du client couverts. Chaque ligne dit où
regarder ; une ligne traitée se barre ici et se documente dans la fiche du système.

### Paquets que le client lit et que le serveur n'envoie jamais

Tous livrés le 2026-10-05 (lot de Codex relu) : ~~322~~, ~~451~~ (réponse à 452 ; 450 est commenté chez l'officiel),
~~512~~, ~~514~~, ~~3003 / 3004~~. Restent :

- **Fenêtres de confirmation ouvertes par le C++ officiel** : `secret_dungeon_confirm_window`
  (`warp_to_secret_dungeon`), `instance_dungeon_confirm_window` et `…_window2` (`warp_to_instance_dungeon`,
  `exit_indun`), `dungeon_raid_confirm_window` (`begin_dungeon_raid`), `recall_feather_confirm_window` et
  `number_input_window` (`on_channel_set`), `ScriptPlayer.cpp:2578-3203`, `StructCreature.cpp:4908`. Le serveur passe
  aujourd'hui par des pages de dialogue. Le rappel de ces fenêtres porte des paramètres ajoutés par le client
  (l'officiel l'accepte par `strstr`) : leur format n'est pas établi, il faut le lire dans `SFrame.exe`.
- La 451 d'une carte dont le propriétaire est hors ligne (vue aux enchères) répond `NotExist` : l'officiel la trouve
  par `StructItem::FindItem`, ici seules les cartes des joueurs en ligne sont en mémoire.
- Le menu du gestionnaire de siège (`NpcDialogService.TryShow`, `NPC_dungeon_siege_manager_contact`) envoie des
  libellés **en français écrits en dur** (« Entrer dans le donjon », « Commencer le raid de guilde »…) au lieu
  d'identifiants de chaîne du client.

### Effets déclenchés au combat (`m_vProcBy*`, `CalculateStat.cpp`)

Seuls les passifs d'énergie 32262/32263 sont branchés (`Combat/EnergyProcs.cs`). Manquent :

- les passifs qui posent un état sur un coup, un coup reçu, un critique, une esquive, un blocage ou une mort
  (`EF_ADD_STATE_ON_*`, 32001 et suivants) ;
- les temps de recharge modifiés sur attaque, coup reçu ou mort (`IncSkillCoolTimeOn*`, 10063-10065) ;
- la maîtrise du bouclier (`IncreaseExtensionAttribute`, 10009) et l'expertise d'esquive (10011, sans valeurs) ;
- les passifs des invocations (`AmplifySummonHpMpSp` 10032, `HuntingTraining` 10013).

Ces effets partagent une seule mécanique officielle (`_ATTACK_TAG`, `CheckProcByAttack`) : `EnergyProcs.Applies` en
porte déjà les conditions et peut servir de base commune.

### Petits manques

- `TS_SC_CANT_ATTACK` (102) n'est pas envoyé pour `NOT_ENOUGH_BULLET` (flèches) ni `ACCESS_DENIED` (`IsAttackable`).
- Après `/change_name`, les guildes, alliances et enchères gardent l'ancien nom jusqu'à la reconnexion.
- Le filtre de ramassage du familier (355) est lu et gardé, jamais appliqué.
- Les objets au sol ne s'ouvrent jamais aux autres joueurs (l'officiel les ouvre au groupe puis à tous après un délai).
- Les invocations n'ont pas de durée : `TS_SC_REMOVE_SUMMON_INFO` (302) et `TS_SC_UNSUMMON_NOTICE` (306) n'ont pas
  d'appelant.
- Les groupes vivent en mémoire et disparaissent au redémarrage du serveur.
- `EventAreaInfo` n'a que ses polygones pour les zones hors `EventAreaResources` (`socle-zones-evenement.md`).
- `/passist` envoie aussi `TS_SC_TARGET` (512), que l'officiel n'envoie jamais (`SendTargetMsg` sans appelant) : ajout
  de Codex, gardé car sans danger, à confirmer en jeu (`512-target.md`).
- `docs/gm-commands.md` (français) est en partie dépassé : `/level` seul monte, `/lv` monte et descend, et les groupes
  existent ; la liste à jour est `docs/gm-commands.en.md`.

### Données après le filtre 7.3 (`filtre-ressources-73.md`)

- Les instances 50000, 60000 et 70000 n'ont plus aucun monstre (tous inconnus du client 7.3) ; 40000, 41001, 42001
  et 43001 en ont perdu une partie. Savoir si ces instances existent seulement en 7.3.
- 134 lignes hors du jeu 7.3 restent parce qu'un entier littéral du code ou du Lua pourrait les nommer : à revoir une
  à une pour une base strictement 7.3.
- `StringResources` (102 256 lignes) n'est pas filtrée.
- Un nouvel `import_epic7.py` réinsère les lignes Epic 7 hors client : rejouer `tools/prune_to_client73.py` après.

### Décisions laissées à Killian

- Numéro de sécurité (9002-9011) : stockage et vérification du code, transport vers le serveur d'authentification.
- Boutique payante et coffre commercial (10001 et suivants, 9000/9001).
- Ferme de créatures (6000-6008) : volontairement non implémentée.

### Avant d'ouvrir à de vrais joueurs

- **Anti-triche** : le serveur croit les positions du client (`TM_CS_MOVE_REQUEST`, `TM_CS_REGION_UPDATE`, 900) sans
  contrôle de vitesse ni d'obstacles ; les paquets 54, 57 et 59 sont lus et ignorés.
- **Exploitation** : PostgreSQL démarre à la main avec les droits administrateur, aucune sauvegarde automatique des
  bases, aucun test de charge à plusieurs dizaines de joueurs.
- **Validation en jeu** : la plupart des systèmes à plusieurs joueurs (échange, étals, enchères, groupe, guildes,
  sièges, duel, PvP, buffs de groupe) et les contenus (artisanat, quêtes, HuntaHolic, donjons) ne sont validés que
  par les tests automatiques.
