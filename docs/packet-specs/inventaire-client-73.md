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
| 406 | `TS_SC_STATE_RESULT` | dégâts et soins périodiques d'un état (poison, régénération) affichés au tick, `StructCreature.cpp:872` | haute |
| 515 | `TS_SC_ENERGY` | compte d'énergie diffusé (`StructPlayer::onEnergyChange`) | haute, voir *Énergie* |
| 901 | `TS_SC_CHANGE_LOCATION` | réponse à `TM_CS_CHANGE_LOCATION` (900) : lieu précédent et lieu courant | **livré le 2026-10-04** avec la météo (`901-change-location.md`) |
| 102 | `TS_SC_CANT_ATTACK` | attaque impossible | basse |
| 30 | `TS_SC_CHANGE_NAME` | changement de nom (personnage, familier) | avec `/change_name` |
| 322 | `TS_SC_SHOW_SUMMON_NAME_CHANGE` | fenêtre de renommage d'une invocation | basse |
| 451 | `TS_SC_SKILL_LEVEL_LIST` | niveaux de compétences (réponse à 450) | à étudier |
| 512 / 514 | `TS_SC_TARGET` / `TS_SC_SP` | cible d'un acteur ; SP des invocations | basse |
| 3003 / 3004 | `TS_SC_SHOW_WINDOW` / `TS_SC_GENERAL_MESSAGE_BOX` | fenêtre ou boîte ouverte par un script | basse |
| 650-652, 660 | fenêtres de guilde, icône de guilde | **lot de Codex** (emblème) | — |
| 9000 / 9001 | `TS_SC_OPEN_URL` / `TS_SC_URL_LIST` | ouverture d'URL (boutique web) | hors périmètre |
| 9002-9011 | numéro de sécurité | création, changement, effacement (le client envoie aussi 9006-9012) | hors périmètre (décision de Killian, 9005) |
| 10001-10025 | boutique d'objets payants | ouverture de la boutique, sans source établie | hors périmètre sans boutique |
| 6003/6005/6007 | résultats de la ferme | ferme non implémentée (décision) | — |
| 103, 506 | sans nom (`TM_SC_HAVOC` pour 506) | non établi | — |

## Systèmes absents

- **Amis et liste de blocage** : livrés le 2026-10-04 (`socle-amis.md`).
- **Énergie.** Quinze compétences jouables coûtent de l'énergie (`CostEnergy`, effets 301, 505 et 30001-30016), et
  d'autres en produisent (`StructSkill.cpp:6420`, `7023`). L'officiel refuse le lancement sans énergie
  (`RESULT_NOT_ENOUGH_ENERGY`). Ici, ces compétences partent gratuitement et le compteur n'existe pas.
- **Commandes de chat officielles non traitées** :
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
