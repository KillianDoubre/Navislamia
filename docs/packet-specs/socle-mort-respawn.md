# Socle — mort et réapparition du personnage joueur (Epic 7.3)

Fiche produite par `navis-ref` (archéologue de protocole) sur la branche
`hermes/packet-socle-mort-respawn`, depuis `master`
`6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` (2026-07-18). Cette fiche **ne modifie aucun fichier de
code applicatif** : elle spécifie le socle que la tâche `navis-dev` implémentera, et dont le paquet
client→serveur `TM_CS_RESURRECTION` (513) est la porte de sortie.

Ce document remplace la fiche `513-resurrection.md` : le lot n'est pas « le paquet 513 » mais « le
socle mort/réapparition », la carte Trello 513 restant en `THINKING` pour un lot ultérieur.

Convention de citation :

- `SFrame.exe` = `/srv/navislamia/reference/client73/SFrame.exe`, binaire du client Epic 7.3,
  **lecture statique uniquement** (`strings`, `objdump -d`, lecture d'octets) ;
  `sha256 = 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e`.
  `reference/client73/` n'est pas un dépôt git : il n'y a pas de SHA de commit à épingler, seule
  l'empreinte du binaire fait foi. Correspondance fichier → adresse virtuelle utilisée :
  `.text` `0x400`→`0x401000`, `.rdata` `0x60da00`→`0xa0f000`, `.data` `0x80e200`→`0xc10000`.
- `rzu` = `/srv/navislamia/reference/rzu` @ `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02).
- `NGemity` = `/srv/navislamia/reference/ngemity` @ `38ceb2c6065fabf6ff4ba71d52f955f362c6c839`
  (2025-12-03).
- Les numéros de ligne `strings` sont donnés **avec la commande exacte** qui les produit, car la
  numérotation dépend de `-n`.

---

## 1. Identité et cadrage

| Élément | Valeur | Source |
|---|---|---|
| Paquet de sortie de l'état mort | `TM_CS_RESURRECTION`, opcode **513** (0x201), client → serveur | `op_codes.md:147`, `rzu/librzu/src/packets/GameClient/TS_CS_RESURRECTION.h:24` |
| Paquet serveur « mort » nommé par les références | **aucun** utilisable (voir §3) | recherche élargie §3.1 |
| Id mort connu du client 7.3 mais sans effet | `TM_SC_DEAD`, opcode **504** | `op_codes.md:138` ; `SFrame.exe` table id→nom `0x00677988` |
| État dans le dépôt | ni 504 ni 513 dans `GamePackets` | `Game/Network/Packets/Enums/GamePackets.cs:50-56` |
| Tramme 513 en 7.3 | **12 octets fixes** | §4 |
| Accroches existantes confirmées | `TM_SC_WARP` (12), `GameStatPackets.BuildProperty`, `WarpService`, `StateTimeType.EraseOnResurrect`, `CharacterEntity.Hp/Mp/Position/Layer/LastDecreasedExp` | §6, §7, §12 |
| Statut de la carte | la carte Trello 513 n'est **pas** traitée ici (lot distinct) | corps de la tâche |

### 1.1 Cadrage du PO : ce qui est confirmé, ce qui est démenti

Le PO a demandé une vérification de chaque constat. Résultats :

| Constat du PO | Verdict | Preuve |
|---|---|---|
| `MonsterAiService.cs:202` porte un plancher à 1 HP | **confirmé** | `Game/Services/MonsterAiService.cs:202` |
| C'est le seul chemin de dégâts vers le joueur | **confirmé** | toutes les écritures de `CharacterHp` : `GameActions.cs:98` (connexion), `LevelingService.cs:48` (montée de niveau), `MonsterAiService.cs:202` (dégâts), `SkillCastService.cs:408` (soin). `MonsterAiRules.PlayerDamage` n'est appelé qu'à `MonsterAiService.cs:201`. |
| `CharacterEntity` n'expose que `Position` (27), `Layer` (28), `LastDecreasedExp` (34), `Hp` (35), `Mp` (36) et aucune donnée de renaissance | **confirmé** | `Game/DataAccess/Entities/Telecaster/CharacterEntity.cs:27,28,34,35,36` (`Position` est `int[]` commenté `// X Y Z`) |
| Le format du 513 se résume à « champ unique `type` (int8) » | **démenti** (incomplet) | le 513 porte **`handle` (uint32) puis `type` (int8)** en 7.3 (`rzu …/TS_CS_RESURRECTION.h:15-16`) ; la version 6.1 et postérieures a remplacé la paire `use_state`/`use_potion` par `type`, mais `handle` reste. §4 |
| « NGemity n'a aucun handler `Resurrection` dans Chihiro » | **démenti** | `NGemity/Chihiro/src/Network/GameNetwork/WorldSession.cpp:1391` `WorldSession::onRevive(const TS_CS_RESURRECTION *)`, déclaré en `WorldSession.h:116`, introduit par `ff604d2` (2018-01-13, « Added reviving :^) »). §9.1 |
| Aucun paquet serveur→client de mort ou de résurrection nommé dans les deux arbres | **confirmé**, y compris après élargissement de la recherche | §3.1 |
| Opcode 513 absent de `GamePackets.cs` | **confirmé** | `GamePackets.cs` ne contient ni 504 ni 513 |
| Accroches citées (`WarpService`, `BuildProperty`, `EraseOnResurrect` 128, `SkillEffectType.Resurrection` 504 / `ResurrectionWithRecover` 30501, `ItemEffectInstant.Resurrection` 4, `EffectTrigger.Resurrection` 109 / `AutoResurrectionAfterRemoveState`) | **toutes confirmées** | `StateTimeType.cs:16` (128), `SkillEffectType.cs:108` (504) et `:255` (30501), `ItemEffectInstant.cs:10` (4), `EffectTrigger.cs:60` (109) et `:87` (3321) ; pour les services voir §6 et §7 |

Une divergence de nom à signaler : `op_codes.md:140` appelle 506 `TM_SC_HAVOC` alors que le client
7.3 enregistre `TM_SC_RAGE` pour 506 (`SFrame.exe` chaîne `.rdata` `0xa53344`, insertion code
`0x00677a4c`). Sans effet sur ce socle, mais le dépôt et le client ne nomment pas 506 pareil.

---

## 2. Ce que le joueur fait pour que le client envoie le 513

1. Le personnage joueur tombe à 0 point de vie (aujourd'hui impossible : §6).
2. Le client affiche sa **fenêtre de mort / résurrection**. Preuves dans le binaire 7.3 :

| Élément d'UI | Preuve |
|---|---|
| Ressource de fenêtre `window_system_resurrect.nui` | `strings -n 4 SFrame.exe` ligne 24793 ; chaîne `.rdata` `0xa4455f` ; nom court `window_system_resurrect` ligne 29614, `.rdata` `0xa79d87` |
| Classes `SUIResurrectWnd` et `SUIPVPResurrectWnd` | `strings -n 4 SFrame.exe` lignes 24960 et 24959 (« Create : SUIResurrectWnd » / « Create : SUIPVPResurrectWnd », `.rdata` `0xa49b14` / `0xa49af4`) ; RTTI `?AVSUIResurrectWnd@@` en `.data` `0xc1c555` ; la fabrique d'écrans instancie les deux (`SFrame.exe` `0x006357f6`, objet de 1196 octets, vtable `0xa456c4`) |
| Trois boutons | `button_resurrect_buff`, `button_resurrect_item`, `button_resurrect_town` — `strings -n 4 SFrame.exe` lignes 21997-21999, `.rdata` `0xa2e423` / `0xa2e43b` / `0xa2e453` |
| Libellés injectés depuis la base de chaînes | `#@resurrect_buff@#` (`strings -n 4 SFrame.exe` ligne 22003, `.rdata` `0xa2e4b7`) |
| Textes de la fenêtre (base de chaînes du client) | `strings -n 6 db_string.rdb` ligne 78528 : « You have died.&lt;br&gt;You may resurrect here by using a #@item_name@#. » ; ligne 110802 : « You have died.&lt;br&gt;You can be revived by using #@item_name@# or #@state_name@# » |
| Variante « confirmer » de la fenêtre selon le contexte | `strings -n 6 db_string.rdb` ligne 195676 : « You have died.&lt;br&gt;Click the confirm button to resurrect at **your return point**. » ; ligne 195702 : « … at the **resurrection point**. » ; ligne 40691 : « … at the **respawn point**. » ; ligne 195928 : « You will resurrect at the starting point of the dungeon. » |
| Voie « objet » (bouton *item*) | `strings -n 5 db_string.rdb` ligne 111249 : « Resurrect using the #@resurrect_buff@# buff. » ; `strings -n 6 db_string.rdb` ligne 4945 etc. (résurrection d'arène) |
| Voie « buff / état » (bouton *buff*) | « Resurrect using God Mother Fairy's Bottle. » et « Able to revive without others' help. Loss of Experience by death is slightly recovered. » (`strings -n 6 db_string.rdb` ligne 49508) |

3. Le joueur clique un bouton ; le client émet `TM_CS_RESURRECTION` (513) avec le `handle` à
   ressusciter et le type d'action.

Le client 7.3 nomme donc bien l'écran et le paquet ; **ce que le serveur doit envoyer pour que cet
écran s'ouvre** est traité en §3, et ce que chaque bouton envoie en §5.

---

## 3. Le paquet (ou l'absence de paquet) qui annonce la mort au client en 7.3

### 3.1 Recherche élargie : les références ne nomment aucun paquet serveur de mort ou de résurrection

Recherche refaite le 2026-09-19 (motifs élargis demandés par le PO) :

- `find /srv/navislamia/reference -iname '*resurrect*' -o -iname '*dead*' -o -iname '*revive*'
  -o -iname '*death*' -o -iname '*die*' -o -iname '*hospital*' -o -iname '*rebirth*'
  -o -iname '*recover*'` → cinq fichiers seulement : les deux `TS_CS_RESURRECTION.h` (rzu, NGemity),
  et `TS_SC_DEATHMATCH_RANKING{,_OPEN,_REOPEN}.h` (rzu, classement de match PvP, hors sujet).
- `grep -rn -iE "resurrect|revive|respawn|death|dead|die"` sur `rzu/librzu/src/packets/**` : seuls
  `TS_CS_RESURRECTION`, `TS_SC_STATUS_CHANGE` (`TCS_FlagDead = 1 << 8`, ligne 14), et des
  `SRST_SummonDead` / `SRST_TargetDead` / `SRST_RespawnMonster` (résultats de skill).
- `grep -rn "SC_DEAD"` sur `rzu` **et dans tout son historique git** (`git log -S"SC_DEAD" --all`) :
  aucun paquet `TS_SC_DEAD` n'a jamais existé dans rzu. Idem dans NGemity (`shared/Server/…`,
  `ClientPackets.h`) : aucun. `grep -rn "504"` sur les paquets rzu ne renvoie qu'un `X(4504, true)`
  (compete countdown), et sur `ngemity/shared/Server/ClientPackets.h` que `TS_SC_COMPETE_COUNTDOWN = 4504`.
- **Côté client 7.3** : l'espace de noms de paquets du binaire ne contient que trois entrées liées à
  la mort ou au retour — `TM_CS_RESURRECTION` (513), `TM_SC_DEAD` (504) et `TM_CS_RETURN_LOBBY`
  (`strings -n 4 SFrame.exe | grep -E '^T[MS]_(CS|SC|_)'` filtré sur
  `DEAD|DIE|RESURRECT|REVIVE|REBIRTH|HOSPITAL|RECOVER|CORPSE` ; 186 noms `TM_*` au total en `.rdata`).
  Aucun `TM_SC_HOSPITAL`, `TM_SC_REBIRTH`, `TM_SC_REVIVE`, `TM_SC_ANNOUNCE_*`.

### 3.2 `TM_SC_DEAD` (504) existe dans le client 7.3 — et son handler est un no-op

Le client 7.3 déclare l'identifiant, mais ne fait rien de ce paquet. Preuves, dans le désassembleur
du binaire (lecture statique, `objdump -d`) :

- Table d'annotations `id → nom` du client (initialiseur en `.text` `0x00677xxx` qui insère chaque
  paire dans une table) : `0x00677988` charge `$0x1f8` (504) et pousse `0xa5335c` = `TM_SC_DEAD` ;
  `0x00677b72` charge `$0x1fd` (509) et pousse `0xa53314` = `TM_SC_HPMP` ; `0x00677dbe` charge
  `$0x201` (513) et pousse `0xa532a8` = `TM_CS_RESURRECTION` ; `0x006778c4` = 500
  `TM_SC_STATUS_CHANGE`. → **504 = `TM_SC_DEAD` dans le client 7.3 lui-même**, et `op_codes.md:138`
  le nomme déjà ainsi.
- Répartiteur des paquets entrants (fonction englobant `0x0067e1d9`) :
  `cmp $0x1f8,%eax` / `jg 0x0067e3c2` / **`je 0x0067ef39`** ;
  puis `sub $0xff`, `cmp $0xf5`, table de saut `0x0067f19c` indexée par `0x0067f218`
  (identifiants 255-500), et une seconde chaîne pour 505-710 (`sub $0x1f9`, `cmp $0xcd`,
  table `0x0067f310` / index `0x0067f35c`) plus 901 et au-delà.
  `0x0067ef39` est la queue commune : elle libère le paquet dans le pool (`mov 0x90(%esi),%ecx` …
  `call *0xc(%edx)`) puis enchaîne sur le paquet suivant de la file (`cmp $0x7,%eax` / `jae 0x0067df30`).
  **Tous les cas traités y sautent après avoir appelé leur handler ; 504 y saute directement.** Donc
  recevoir 504 ne produit aucun effet — pire, ce n'est même pas journalisé comme non traité.
- Le cas « non traité » (`0x0067ef21`) est distinct : il journalise la chaîne coréenne
  `0xa53df0` = « 처리되지 않은 메세지 : %d » (« message non traité : %d »), puis va à la queue.
  `0x0067ef21` est la cible par défaut des deux tables, et c'est là que tombent **510, 511, 513 et 517**
  (paquets client→serveur ou non implémentés, ce qui est attendu pour 513).
- Contrôle croisé de la méthode (preuve que la lecture des tables est correcte) : les identifiants
  obtenus par les tables de saut résolvent 500 → `0x0067e39b` → `call 0x0066da20` ;
  505 → `0x0067e480` → `call 0x0067a690` ; 506 → `0x0067e48d` → `call 0x00670380` ;
  509 → `0x0067e4a7` → `call 0x00670560` ; 512 → `0x0067e4b4` → `call 0x00670600` ;
  514 → `0x0067e473` → `call 0x0066db10` ; 515 → `0x0067e459` → `call 0x00671380` ;
  516 → `0x0067e466` → `call 0x0066da90`. Le handler de 509 copie **8 entiers et 1 octet** depuis la
  trame (`mov 0x7(%ecx)`…`mov 0x1f(%ecx)`, puis `mov 0x23(%ecx),%cl`), ce qui correspond exactement
  au corps de `TS_SC_HPMP` en 7.3 (`handle`, `add_hp`, `hp`, `max_hp`, `add_mp`, `mp`, `max_mp`,
  `need_to_display` = 29 octets, `rzu …/TS_SC_HPMP.h:7-18`).

### 3.3 Conséquence : le client apprend sa mort par sa valeur de points de vie, pas par un paquet dédié

Trois canaux seulement portent les points de vie du joueur vers son propre client, et tous les trois
existent déjà dans le dépôt :

1. `TM_SC_ATTACK_EVENT` (101) — le coup qui tue porte `target_hp = 0`
   (`Game/Services/MonsterAiService.cs:204-206`, `GameAttackPackets.BuildAttackEvent`) ;
2. la propriété `hp` (`TM_SC_PROPERTY` 507) — `MonsterAiService.cs:207`,
   `GameStatPackets.BuildProperty` (`GameStatPackets.cs:78`) ;
3. `TM_SC_HPMP` (509), non utilisé par ce dépôt aujourd'hui.

Deux éléments corroborent que la mort est déduite de la valeur de PV plutôt que d'un paquet :

- `CLAUDE.md:217` énonce déjà que « le client joue l'animation de mort quand `target_hp` atteint 0 » —
  formulation à corriger, voir §3.4.
- NGemity (serveur 9.x joué) **n'envoie aucun paquet de mort** : `Unit::onDead`
  (`Chihiro/src/Entities/Unit/Unit.cpp:1395-1414`) annule le sort, l'attaque, les auras et la haine,
  efface les états marqués `AF_ERASE_ON_DEAD`, et rien d'autre ; `Player::onDead`
  (`Player.cpp:3534-3542`) ne fait que désinvoquer l'invocation principale. La mort du joueur y est
  donc portée au client par les mêmes paquets de PV que ci-dessus.

**Décision de la fiche** : le socle n'introduit **aucun** paquet serveur de mort. Il laisse les
points de vie atteindre 0 et continue de publier cette valeur par les canaux existants (1) et (2).
L'identifiant 504 reste inutilisé et n'est **pas** ajouté à `GamePackets` (un membre d'énumération
sans bras de traitement atteindrait le `switch` final, `GameClient.cs:681`).

### 3.4 Correction à `CLAUDE.md`

`CLAUDE.md:217` (« The client plays the death animation when `target_hp` reaches 0; there is no
`TS_SC_DEAD` in this version. ») est à corriger : le client 7.3 **connaît** `TM_SC_DEAD = 504`
(table id→nom, §3.2), mais son répartiteur ne lui donne aucun effet. La formulation exacte est :
« le client 7.3 déclare `TM_SC_DEAD` (504) mais son répartiteur le libère sans effet (aucun handler) ;
la mort se lit sur les points de vie (`target_hp` de l'attaque, propriété `hp`) ». Le bloc §17
propose le texte.

---

## 4. Structure du 513 sur le fil (tramme fixe de 12 octets)

En-tête du dépôt : `Game/Network/Packets/Header.cs:9-11` (`Length` `uint32`, `ID` `uint16`,
`Checksum` `uint8`), soit 7 octets — même constante que les paquets voisins
(`GameActionPackets.cs:8` : `HeaderSize = 7`). rzu écrit les champs à la suite, sans remplissage
(`rzu/librzu/src/lib/Packet/MessageBuffer.h:73-86`) : **une trame fait la somme de ses champs, sans
alignement à 4 octets**. Précédent du dépôt : le 511 `TM_CS_TARGETING`, dont le seul champ utile est
un handle et dont la trame fait 11 octets, pas 12.

| Offset | Taille | Type | Nom | Valeur attendue | Source |
|---|---|---|---|---|---|
| 0 | 4 | `uint32` | `Length` | 12 | `Header.cs:9` |
| 4 | 2 | `uint16` | `ID` | 513 | `Header.cs:10`, `op_codes.md:147` |
| 6 | 1 | `uint8` | `Checksum` | somme de contrôle d'en-tête | `Header.cs:11` |
| 7 | 4 | handle (`ar_handle_t` = `strong_typedef<uint32_t>`) | `handle` | identifiant **du personnage à ressusciter** | `rzu …/TS_CS_RESURRECTION.h:15` (`version >= EPIC_4_1`), `rzu …/lib/Packet/GameTypes.h:40` |
| 11 | 1 | `int8` (`TS_RESURRECTION_TYPE`) | `type` | 0 `RT_UseNone`, 1 `RT_UseState`, 2 `RT_UsePotion`, 3 `RT_Compete`, 4 `RT_Deathmatch` | `rzu …/TS_CS_RESURRECTION.h:5-12` et `:16` (`version >= EPIC_6_1`) |

**Taille totale : 7 + 4 + 1 = 12 octets.** Aucun octet de remplissage, aucune chaîne.

Champs **absents** en 7.3 (ils forment la forme pré-6.1, longue de 13 octets) :
`use_state` (`bool`, `version < EPIC_6_1`, ligne 17) et `use_potion` (`bool`,
`version >= EPIC_4_1 && version < EPIC_6_1`, ligne 18). C'est **exactement cette forme-là** que
NGemity compile (§9.1) : le portage naïf de son handler lit donc deux octets qui n'existent plus.

---

## 5. Champ `type` : gating, valeurs, et ce que le client envoie

### 5.1 Gating tranché pour l'Epic 7.3

Constantes de version : `EPIC_4_1 = 0x040100` (`rzu …/lib/Packet/PacketEpics.h:50`),
`EPIC_6_1 = 0x060100` (`:54`), `EPIC_7_3 = 0x070300` (`:59`), `EPIC_9_6_3 = 0x090603` (`:96`).
7.3 = `0x070300` → `>= EPIC_4_1` **oui**, `>= EPIC_6_1` **oui**, `< EPIC_6_1` **non**.

| Champ | Gating rzu | Présent en 7.3 ? | Décision |
|---|---|---|---|
| `handle` | `version >= EPIC_4_1` | oui (4 octets, offset 7) | présent |
| `type` | `version >= EPIC_6_1` | oui (1 octet, offset 11) | présent |
| `use_state` | `version < EPIC_6_1` | non | **exclu** : en 7.3 le choix « état » passe par `type == 1` |
| `use_potion` | `version >= EPIC_4_1 && version < EPIC_6_1` | non | **exclu** : en 7.3 le choix « objet / potion » passe par `type == 2` |
| identifiant | `X(513, version < EPIC_9_6_3)` / `X(1513, version >= EPIC_9_6_3)` (`:20-22`) | 513 | **513** (et non 1513) |

Historique rzu du fichier (`git log --follow … TS_CS_RESURRECTION.h`) : `bdd362a6` (2017-02-26,
« Update packets based on available GS pdbs (5.2, 6.1, 6.2, 7.1, 7.2, 7.3, 7.4, 8.1) ») introduit
l'énumération `TS_RESURRECTION_TYPE` et le gating `type >= 6.1` — c'est la mise à jour fondée sur le
**PDB du serveur 7.3**, donc la source directe de cette fiche ; `94885467` (2017-03-03) ajoute le
gating du handle et de `use_potion` ; `05bc2d82` (2020-03-29) remplace `uint32_t` par
`ar_handle_t` ; `11f2b6fd` (2020-08-11) versionne l'identifiant (513 / 1513).

Le fichier rzu ne porte pas de ligne `// Last tested:` (contrairement à `TS_SC_STATUS_CHANGE.h:5` ou
`TS_SC_HPMP.h:5`, marqués `EPIC_9_8_1`) : sa disposition 7.3 vient des PDB, pas d'un test de capture.
Réserve §15.3.

### 5.2 Valeurs de `type`

`rzu …/TS_CS_RESURRECTION.h:5-12` :

```
RT_UseNone   = 0   // ressusciter sans état ni objet : « à votre point de retour » (§2)
RT_UseState  = 1   // ressusciter via un état (buff de résurrection)
RT_UsePotion = 2   // ressusciter via un objet (parchemin / potion / bouteille)
RT_Compete   = 3   // contexte compétition
RT_Deathmatch= 4   // contexte match à mort
```

La correspondance avec les trois boutons du client est **déduite du nom des contrôles** —
`button_resurrect_buff` → `RT_UseState`, `button_resurrect_item` → `RT_UsePotion`,
`button_resurrect_town` → `RT_UseNone` — et corroborée par les textes de la base de chaînes
(§2, « Resurrect using the #@resurrect_buff@# buff » / « … by using a #@item_name@# » /
« … at your return point »). Elle n'est **pas** prouvée par lecture du code qui émet la trame :
voir §15.1.

### 5.3 Validation d'entrée recommandée

Le socle n'implémente que le chemin « ville » (`type = RT_UseNone`), donc au minimum :

1. `packet.Length != 12` → `ResultCode.InvalidArgument` (le style du dépôt est
   `if (packet.Length < packetLength)` dans `GameActionPackets.TryReadLearnSkill` (`:155-169`) ;
   pour une trame fixe, l'égalité stricte est préférable, le client n'envoyant rien d'autre) ;
2. `handle` ≠ handle du personnage connecté → `ResultCode.NotOwn` (le dépôt compare déjà
   `request.Caster != info.CharacterHandle` en `SkillCastService.cs:201`) ;
3. personnage vivant (`info.CharacterHp > 0`) → `ResultCode.NotActable` (aucune raison de
   ressusciter un vivant) ;
4. `type` ∈ {1, 2, 3, 4} → lot ultérieur : répondre `ResultCode.NotActable` sans rien changer
   d'autre. Réserve §15.2 sur le contenu attendu par le client d'un `TM_SC_RESULT` tagué 513.

---

## 6. Chemin de dégâts : où le plancher à 1 HP doit céder, et ce qu'il faut diffuser

Le seul chemin de dégâts vers le joueur est l'attaque du monstre aggroté :

- `Game/Services/MonsterAiService.cs:201-207` : `MonsterAiRules.PlayerDamage(info.CharacterMaxHp)`
  puis `info.CharacterHp = Math.Max(1, info.CharacterHp - damage);` — le plancher ;
- `Game/Services/MonsterAiRules.cs:29` : `PlayerDamage(maxHp) => Math.Max(1, maxHp / 100)`, documenté
  « Test damage: one hundredth of the player's max HP, never below 1 » ;
- le coup est déjà diffusé par `GameAttackPackets.BuildAttackEvent(handle, CharacterHandle, …,
  damage, info.CharacterHp, hpDuMonstre)` (`MonsterAiService.cs:204-206`) et la valeur du joueur est
  publiée par la propriété `hp` (`:207`).

Ce que la fiche statue :

1. **Le plancher tombe à 0** (`Math.Max(0, …)`). La valeur `1` du produit `PlayerDamage` (`Math.Max(1,
   maxHp / 100)`) n'est pas concernée : c'est un plancher de **dégâts**, pas de PV.
   Tests existants à adapter en conséquence, sans faire baisser le total :
   `Tests/Game/MonsterAiRulesTests.cs:87-96`.
2. **Ce qui est diffusé, et à qui** : rien de nouveau. Le client du joueur reçoit le coup
   (`TM_SC_ATTACK_EVENT` avec `target_hp = 0`) et la propriété `hp = 0` — les deux canaux qui
   l'informent de sa mort (§3.3). Les autres joueurs voient le personnage via le streamer de monde
   (`Game/Services/WorldObjectStreamer.cs`) : il n'existe aujourd'hui **aucun** paquet de statut
   joueur-mort à diffuser, et `TM_SC_STATUS_CHANGE` (500) **ne doit pas** servir ici : pour un
   handle de **joueur**, le bit `1 << 8` vaut `TCS_FlagSitdown`, pas `TCS_FlagDead`
   (`rzu …/TS_SC_STATUS_CHANGE.h:14` et `:20-21` : `TCS_FlagDead` est rangé sous « Monster »,
   `TCS_FlagSitdown` sous « Player »). C'est le piège du dépôt : `CombatService.cs:21`
   (`MonsterDeadStatus = 1 << 8`) et son envoi à `:217` sont **légitimes pour un monstre seulement**.
3. **Arrêter ce qui frappe un cadavre** : le monstre attaquant doit cesser ses coups dès que
   `info.CharacterHp == 0`. Le point d'ancrage le plus proche est la boucle d'attaque elle-même
   (`MonsterAiService.Attack`, appelée depuis le tick d'IA) : `Game/Services/MonsterAiService.cs`
   pour l'état, `Game/Services/CombatService.cs` pour l'arrêt d'attaque côté joueur
   (`StopAttack`, `DropAggro`, utilisés par `WarpService.Warp` avant tout déplacement,
   `Game/Services/WarpService.cs:39-43`).
4. **Effet de bord déjà présent, à ne pas casser** : `SkillCastService.cs:207-211` refuse déjà toute
   incantation quand `info.CharacterHp <= 0` (`ResultCode.NotActable`). Ce garde-fou devient
   réellement actif le jour où les PV peuvent atteindre 0 : c'est une accroche, pas un chantier.

---

## 7. La réapparition effective

Le socle implémente la **réapparition à la ville** (`type = RT_UseNone`) :

1. **Téléportation** : `WarpService.Warp(client, x, y)` (`Game/Services/WarpService.cs:32-56`)
   fait exactement les bons gestes — arrêt de l'attaque, purge de l'aggro, `LeaveEverything`,
   `GameSpawnPackets.BuildWarp(x, y, 0f, layer)` (`TM_SC_WARP` 12, `GameSpawnPackets.cs:101-114`),
   puis resynchronisation des PNJ, monstres et props. Rien à réécrire.
2. **Restauration des points de vie** : `GameStatPackets.BuildProperty(handle, "hp", valeur)`
   (`GameStatPackets.cs:78`) — même canal que le reste du dépôt ; `"mp"` pour la mana.
3. **États à effacer** : `StateTimeType.EraseOnResurrect = 128` (`StateTimeType.cs:16`) — la valeur
   est déclarée mais **aucun code ne l'utilise** (`grep -rn "EraseOnResurrect"` ne renvoie que
   l'énumération). NGemity applique l'effacement à la mort
   (`Unit::removeStateByDead`, `Unit.cpp:3028-3031`, qui retire `AF_ERASE_ON_DEAD`) et à la
   résurrection (`Unit::ClearRemovedStateByDeath`, `Unit.cpp:3070-3075`). Décision : le socle efface
   les états marqués `EraseOnResurrect` **seulement s'il en existe déjà un porteur** ; sinon la
   fiche acte que l'énumération reste déclarative et la question part en §16.
4. **Où cela s'applique** : dans le service qui traite le 513 (même forme que les autres paquets du
   dépôt : lecture du tampon, décision, envoi), après validation (§5.3). L'état de mort n'est
   matérialisé par rien d'autre que `CharacterHp == 0` : le lever consiste donc à republier des PV
   supérieurs à zéro.
5. **Visibilité rendue aux autres joueurs** : le warp repeuple déjà l'environnement ; les entités
   voisines du lieu de renaissance reçoivent l'apparition du personnage par le chemin existant
   (`WorldObjectStreamer`). Aucun paquet nouveau.

---

## 8. Le point de réapparition : quelle donnée, et où elle doit vivre

État vérifié du dépôt :

- `CharacterEntity.Position` (`int[]`, commenté `// X Y Z`) et `Layer`
  (`CharacterEntity.cs:27-28`) = position persistée du personnage ;
- `GameActions.cs:96-99` : **à la connexion**, `info.CharacterHp = stats.MaxHp` et l'entrée en jeu
  se fait à la position persistée — `character.Hp` (colonne `Hp`, `CharacterEntity.cs:35`) n'est
  **pas** relue au démarrage du monde ;
- aucune notion de « point de retour » n'existe : `grep -rn "LastTown|ReturnPosition|RevivePoint"`
  sur `Game/` ne renvoie rien, et `TM_CS_RETURN_LOBBY` (23) est un retour au lobby, pas une ville.

La référence NGemity tranche la **forme** de la donnée, pas sa valeur : `Player::GetLastTownPosition`
(`Chihiro/src/Entities/Player/Player.cpp:2527-2546`) lit deux marqueurs du personnage (`rx`, `ry`) et,
s'ils sont absents ou nuls, retombe sur des coordonnées de ville **codées en dur par race**
(`case 0: 6625,6980`, `case 1: 116799,58205`, `case 2: 153513,77203` — des constantes de test).
Le texte du client 7.3 parle de « **your return point** » et de « the resurrection point »
(`db_string.rdb` lignes 195676 et 195702) : le client ne choisit pas la position, il nomme le point
que le serveur applique.

**La politique de réapparition n'appartient pas à la fiche** (§16.1) : trois options sont
documentables, aucune n'est tranchée ici — (a) réutiliser la position persistée
(`CharacterEntity.Position`), (b) ajouter un « point de retour » de personnage (colonne nouvelle,
**migration requise** : contexte `Telecaster`, dernière migration
`Game/DataAccess/Migrations/Telecaster/20260714164541_Version0007_CharacterSkills` + snapshot
`TelecasterContextModelSnapshot.cs`), (c) constantes par race reprises de NGemity pour
débloquer le socle sans écrire de données. La fiche constate que (a) et (c) ne demandent **aucune
migration**, (b) en demande une.

---

## 9. Traitement attendu côté serveur, et écarts avec NGemity

### 9.1 Ce que NGemity fait du 513 — et pourquoi on ne le porte pas tel quel

`WorldSession::onRevive` (`Chihiro/src/Network/GameNetwork/WorldSession.cpp:1391-1432`) :

- si `pRecvPct->use_state` : cible = le joueur ou son invocation (`GetSummonByHandle`), refuse si
  hors monde ou vivant (`TS_RESULT_NOT_ACTABLE`), tente `ResurrectByState()`, et **envoie un
  `TS_SC_RESULT` tagué 513** ;
- sinon si `pRecvPct->use_potion` : **branche vide** ;
- puis, pour la réapparition en ville : `sScriptingMgr.RunString(player, "revive_in_town(N)")` avec
  `N ∈ {0 normal, 1 bataille, 2 compétition, 3 siège de donjon}` et `ClearRemovedStateByDeath()`.

Trois écarts, dont un décisif :

1. **Écart de version** : NGemity compile en `EPIC_4_1_1` — `#define EPIC EPIC_4_1_1`
   (`ngemity/shared/Common/Define.h:25`) et `Game.PacketVersion` par défaut `EPIC_4_1_1`
   (`shared/Configuration/Config.cpp:70`). Sa structure `TS_CS_RESURRECTION` est donc la forme
   **pré-6.1** : `uint32_t handle` + `bool use_state` + `bool use_potion` = **13 octets**, et son
   handler lit `use_state` / `use_potion` parce que, dans **sa** compilation, `type` n'existe pas.
   Porter ce handler tel quel en 7.3 lirait le mauvais octet. Traduction à faire : `type == RT_UseState`
   ↔ branche `use_state` ; `type == RT_UsePotion` ↔ branche (vide) `use_potion` ;
   `type ∈ {RT_UseNone, RT_Compete, RT_Deathmatch}` ↔ chemin ville. Noter aussi que
   `CREATE_PACKET(TS_CS_RESURRECTION, 513)` de NGemity (`shared/Server/…/TS_CS_RESURRECTION.h:21`)
   n'est **pas** versionné : pour les deux références l'identifiant 7.3 vaut 513, l'écart est donc nul
   sur l'opcode.
2. **La logique de ville n'est pas dans le dépôt de référence** : `revive_in_town` est un script Lua
   du serveur NGemity ; le clone ne contient que `Chihiro/src/Scripting/XLua.cpp`, qui enregistre
   `warp_to_revive_position` (`XLua.cpp:89`/`905-916`, qui warp à `GetLastTownPosition()`). Aucun
   fichier `.lua` n'est présent : **le montant de PV/MP rendus par une réapparition en ville n'est
   établi par aucune référence** (§15.4).
3. **Ne pas confondre deux énumérations** : `Unit::Resurrect` prend un `_CHARACTER_RESURRECTION_TYPE`
   interne à NGemity (`Chihiro/src/Entities/Unit/Unit.h:77-88` : `CRT_NORMAL=0`, `CRT_BATTLE=1`,
   `CRT_COMPETE=2`, `CRT_SKILL=3`, …) dont les valeurs ne coïncident **pas** avec
   `TS_RESURRECTION_TYPE` de la trame (3 = `RT_Compete` côté client, `CRT_SKILL` côté NGemity).
   `Unit::Resurrect` (`Unit.cpp:3077-3105`) ajoute au moins 1 PV, restaure ou purge les états selon
   un booléen, sauvegarde le personnage et diffuse `BroadcastHPMPMessage`
   (`Chihiro/src/Network/Messages.cpp:448-462`). C'est un bon modèle de **forme**, pas de chiffres.
4. Forme de la réponse : `Messages::SendResult` (`Messages.cpp:361-383`) émet
   `TS_SC_RESULT` (0) avec `request_msg_id`, `result`, `value` — le dépôt a l'équivalent
   (`GameClient.SendResult`, `Game/Network/Clients/GameClient.cs:56-60`). NGemity n'en envoie **que**
   sur le chemin « état », jamais sur le chemin ville.

### 9.2 Ce que le serveur NavisLamia doit faire, en résumé

1. Laisser les PV du joueur atteindre 0 (§6) et continuer de publier `hp` et `target_hp`.
2. Enregistrer `TM_CS_RESURRECTION = 513` dans `GamePackets` **et** lui donner un bras de traitement
   dans le répartiteur (`GameClient.OnDataReceived`, `GameClient.cs:497-682`) dans le même commit :
   un membre déclaré sans bras atteint `_ => throw new Exception("Unknown Packet Type")`
   (`GameClient.cs:681`), ce que le critère d'acceptation 4 interdit. Attention : le dispatch des
   paquets de **jeu** est la chaîne de `if (header.ID == …)` de `GameClient.cs:509-668` puis le
   `switch` final `:670-682` ; la table `_actions.Add(…)` de
   `Game/Network/Clients/Actions/GameActions.cs:43-50` (8 entrées) ne concerne que la session
   pré-jeu (version, login, liste/création/suppression de personnage, rapport) — le corps de la tâche
   `navis-dev` dit l'inverse, c'est ici corrigé.
3. Traiter la trame comme en §5.3, puis appliquer : warp (§7.1), PV/MP (§7.2), états (§7.3), et
   répondre `ResultCode.Success` (ou l'erreur de §5.3) par `SendResult(513, …)`.
4. Ne rien envoyer d'autre : ni 504 (§3.2), ni `TM_SC_STATUS_CHANGE` avec `1 << 8` pour un joueur
   (§6.2).

---

## 10. Pénalité de mort

- Le dépôt porte la colonne `LastDecreasedExp` (`CharacterEntity.cs:34`, et `SummonEntity.cs:20`),
  présente depuis la migration initiale (`20231213221150_Version0001_TheBeginning.cs:75` et `:287`)
  mais **lue par aucun code** : `grep -rn "LastDecreasedExp" --include=*.cs .` ne renvoie que les
  entités et les migrations.
- Elle traduit une mécanique réelle du client 7.3 : « If you are dead! At Lv5 or under, you will not
  lose experience points. » (`strings -n 6 db_string.rdb` ligne 10221 — donc **pas de perte avant le
  niveau 5**), et les textes de résurrection qui « recover a part of the previous experience points
  lost due to the death » (parchemin de résurrection, sorts `Resurrection` / `Corps de créature`,
  `db_string.rdb` lignes 49508, 16931, 17097).
- NGemity a le même axe : `Unit::damage(from, nDamage, decreaseEXPOnDead)` et
  `Resurrect(..., nRecoveryEXP, ...)` (`Unit.cpp:3077-3090`, `Player.cpp:3534`) — l'expérience est
  retirée à la mort puis partiellement rendue par la résurrection **rémunérée**.
- **Ce que le socle fait** : rien. Le dépôt sait publier l'expérience (`TM_SC_EXP_UPDATE` 1003,
  `GamePackets.cs:61`) et `LevelingService` en est le propriétaire ; retirer de l'expérience à la mort
  est une **décision d'exploitation** (§16.2). Elle appartient à Killian, pas à cette fiche ni au
  développement. `LastDecreasedExp` doit rester à 0 tant que la décision n'est pas prise.

---

## 11. Résurrection par autrui : chantier distinct

Le socle se **scinde**, et la fiche le dit explicitement :

| Mécanique | Accroche dans le dépôt | Pourquoi ce n'est pas ce socle |
|---|---|---|
| Résurrection par un sort d'un autre joueur | `SkillEffectType.Resurrection = 504` (`SkillEffectType.cs:108`), `ResurrectionWithRecover = 30501` (`:255`), `EffectTrigger.Resurrection = 109` (`EffectTrigger.cs:60`) | suppose le ciblage d'un **cadavre** et des sorts ciblés sur cible morte ; `SkillCastService` refuse déjà toute incantation avec `CharacterHp <= 0` *pour le lanceur* (`:207`) et l'arsenal de ciblage n'a pas été instruit pour une cible morte. Découpe à ouvrir comme carte distincte. |
| Résurrection automatique par état | `EffectTrigger.AutoResurrectionAfterRemoveState = 3321` (`EffectTrigger.cs:87`) ; « Guardian Angel … You gain the Miracle of Life effect when you die. When this effect runs out, you will be revived and mana will be consumed. » (`strings -n 6 db_string.rdb` ligne 73976) ; « Tears of Gaia … Allows the target to self-resurrect upon death » (`db_string.rdb` ligne 17020) | dépend du cycle de vie des états (`RemoveState` → résurrection), chantier `StateTimeType` / auras déjà ouvert ailleurs. |
| Résurrection par objet | `ItemEffectInstant.Resurrection = 4` (`ItemEffectInstant.cs:10`) ; textes `db_string.rdb` lignes 49508-49510, 52147 | c'est le bouton `button_resurrect_item` (`type = RT_UsePotion`) : chemin d'inventaire, hors du minimum du socle. |

Le minimum du socle (§12) couvre **la mort du joueur, l'avertissement du client, et la réapparition à
la ville** — c'est-à-dire `RT_UseNone` seul.

---

## 12. Découpage : le minimum que cette branche implémente

### 12.1 Sous-ensemble minimal (branche `hermes/packet-socle-mort-respawn`)

1. `TM_CS_RESURRECTION = 513` dans `GamePackets` (`GamePackets.cs`) + bras de traitement dans
   `GameClient.OnDataReceived` (énumération et dispatch modifiés **ensemble**).
2. Lecture de la trame de **12 octets** (`handle` @7, `type` @11) et validations de §5.3.
3. Le plancher à 1 HP levé (`MonsterAiService.cs:202`), sans changer la formule de dégâts.
4. Le monstre cesse de frapper un personnage à 0 PV (arrêt de l'attaque en cours pour cet aggro).
5. La réapparition : warp (`WarpService.Warp`) + republication des PV/MP (`BuildProperty`), au point
   retenu par la décision §16.1 (par défaut : la position persistée du personnage, qui n'exige ni
   colonne ni migration).
6. Tests : offsets de la trame (taille 12 et position de chaque champ, discipline du dépôt, cf.
   `Tests/Game/*PacketsTests.cs`), plus tests unitaires du socle (plancher levé, mort à 0, refus de
   ressusciter un vivant, `type` hors `RT_UseNone`, warp demandé, PV republiés).
7. Le bloc `CLAUDE.md` de §17 dans la description de la MR.

### 12.2 Paquets restants (pour que la carte 513 puisse rouvrir sans refaire ce travail)

| Paquet | Opcode | Format (7.3) | Prérequis |
|---|---|---|---|
| `TM_CS_RESURRECTION` chemin état | 513 | 12 octets, `type = 1` | un état de résurrection porté par le joueur (`SEF_RESURRECTION = 109`, `EffectTrigger.cs:60`) ; gestion d'états déjà en place ailleurs |
| `TM_CS_RESURRECTION` chemin objet | 513 | 12 octets, `type = 2` | consommation d'objet de résurrection (`ItemEffectInstant.Resurrection = 4`) et son inventaire |
| `TM_CS_RESURRECTION` compétition / match à mort | 513 | 12 octets, `type = 3` / `4` | arènes (`TM_SC_BATTLE_ARENA_*`) et compte à rebours de pénalité ; textes `db_string.rdb` lignes 4945, 4957 |
| Résurrection par autrui | 513 (`type = 1` depuis un tiers) ou sort ciblé | sorts `EF_RESURRECTION` (504) / `EF_RESURRECTION_WITH_RECOVER` (30501) | ciblage d'une cible morte + `TCS_FlagDead` **de monstre** pour l'animation, et décision sur la récupération d'expérience |
| Récupération d'expérience perdue | — | `TM_SC_EXP_UPDATE` 1003 (déjà en place) | décision §16.2, colonne `LastDecreasedExp` déjà présente |
| `TM_SC_DEAD` (504) | 504 | inconnu (aucun format dans rzu ni NGemity) | **aucun** : le client 7.3 ne lui donne aucun effet (§3.2). À ne pas implémenter. |

---

## 13. Persistance et migrations

- **Aucune migration n'est requise** pour le sous-ensemble de §12.1 tel qu'il est borné : la mort est
  l'état `CharacterHp == 0` en mémoire (`ConnectionInfo.CharacterHp`,
  `Game/Network/Clients/ConnectionInfo.cs:31`), remis à 0 à la réinitialisation (`:157`), et la
  position de réapparition existe déjà (`CharacterEntity.Position`, `Layer`).
- Ce qui survit à une relance serveur, aujourd'hui comme après le socle : rien de plus que le
  personnage persisté. `GameActions.cs:96-99` remet les PV au **maximum** à chaque entrée en jeu et
  ne relit pas `CharacterEntity.Hp` : un redémarrage ne peut donc pas laisser un personnage « mort »
  au sens du serveur. La question « faut-il persister l'expérience perdue » est la seule qui touche
  les données, et elle dépend de §16.2 (`LastDecreasedExp` existe déjà, donc **même dans ce cas
  aucune migration** n'est nécessaire).
- Si Killian choisit la politique (b) de §8 (point de retour par personnage), alors une migration est
  nécessaire : contexte EF `Telecaster` (`Game/DataAccess/Migrations/Telecaster/`, dernière migration
  `20260714164541_Version0007_CharacterSkills` + `TelecasterContextModelSnapshot.cs`).

---

## 14. État de `master`, branches ouvertes et dépendances

- La branche part de `master` = `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` (2026-07-18), arbre propre.
- Six branches de MR ouvertes touchent `Game/Network/Packets/Enums/GamePackets.cs` :
  `hermes/packet-1202-emotion`, `hermes/packet-203-drop-item`, `hermes/packet-221-hide-equip-info`,
  `hermes/packet-223-swap-equip`, `hermes/packet-253-use-item`, `hermes/packet-408-request-remove-state`.
  Le socle ajoute **une** entrée (`TM_CS_RESURRECTION = 513`) dans la bande 500-517, où aucune des six
  ne travaille : le risque de conflit est limité à la même zone de fichier, pas aux mêmes lignes.
  Comme elles ne sont pas mergées, `docs/packet-specs/` n'existe pas sur `master` : **cette fiche
  recrée le dossier**, ce n'est pas une anomalie (le PO l'a écrit, c'est confirmé : la branche 408
  contient `docs/packet-specs/408-request-remove-state.md`, `master` ne contient que trois documents
  dans `docs/`).
- Le socle **ne dépend d'aucune fiche non mergée** : le 513 est spécifié ici, les autres paquets du
  lot sont listés en §12.2.
- Rappel d'outillage : `CLAUDE.md` **ne contient aujourd'hui aucune occurrence de `packet-specs`**
  (`grep -n "packet-specs" CLAUDE.md` → vide), alors que le critère d'acceptation le suppose. Le
  pointeur devra être ajouté par Killian (Hermes refuse l'écriture d'un agent dans `CLAUDE.md`).

---

## 15. NON ÉTABLI

1. **Quelle valeur chaque bouton du client met dans `type`.** La table de correspondance de §5.2 est
   déduite du nom des contrôles et des libellés, pas lue dans le code qui émet la trame : je n'ai pas
   trouvé le site d'émission du 513 dans `SFrame.exe` (aucun `push`/`mov` immédiat de 0x201 vers un
   en-tête de trame n'a pu être attribué sans ambiguïté). Ce qui trancherait : une capture réseau
   d'un client 7.3 réel, ou la décompilation du gestionnaire de clic des trois boutons.
2. **Ce que le client attend comme réponse pour sortir de l'état mort.** NGemity n'envoie un
   `TS_SC_RESULT` tagué 513 que sur le chemin « état ». Aucune référence n'établit si le client 7.3
   ferme la fenêtre sur `TM_SC_RESULT` tagué 513, sur la propriété `hp > 0`, sur le `TM_SC_WARP`, ou
   sur les trois. Recommandation de la fiche : envoyer les trois (résultat, warp, PV) ; à confirmer
   par un test en jeu.
3. **Le format 12 octets est-il celui du client ?** rzu est la référence de taille pour cette fiche,
   mais son fichier `TS_CS_RESURRECTION.h` ne porte pas de marqueur `// Last tested:` (contrairement
   à `TS_SC_STATUS_CHANGE.h:5`). Un client compilé avec la forme pré-6.1 enverrait 13 octets. Ce qui
   trancherait : une capture ; en attendant, le test d'offsets fige 12 et le code doit refuser
   franchement toute autre taille plutôt que de désaligner le flux.
4. **Le montant de PV/MP rendu par une réapparition à la ville.** La logique NGemity est dans un
   script Lua absent du clone (`revive_in_town`, cf. §9.1 point 2) : ni `Unit::Resurrect` (qui prend
   un `nIncHP` fourni par l'appelant) ni les textes du client ne donnent de chiffre. Ce qui
   trancherait : le script d'origine, ou une décision d'exploitation (§16.3).
5. **Ce qu'une résurrection « en ville » fait aux états.** `StateTimeType.EraseOnResurrect = 128` est
   déclaré mais jamais utilisé par le dépôt ; NGemity efface `AF_ERASE_ON_DEAD` à la mort et purge
   les états retirés à la résurrection. Aucune référence locale n'établit la liste exacte d'états
   concernée en 7.3.
6. **Le déclencheur exact de l'ouverture de la fenêtre de mort côté client.** J'ai établi que 504
   est sans effet et qu'il n'existe pas d'autre paquet dédié (§3), donc que la mort se lit sur les
   points de vie — mais je n'ai pas localisé, dans `SFrame.exe`, le test qui ouvre
   `SUIResurrectWnd` (le handler de `TM_SC_HPMP`, `0x00670560`, ne fait qu'empiler des valeurs pour
   un lissage). Ce qui trancherait : soit la décompilation du consommateur de cette file, soit un
   test en jeu (le plus rapide : laisser les PV tomber à 0 et regarder si la fenêtre s'ouvre).
7. **Le doublon 506.** `op_codes.md:140` nomme 506 `TM_SC_HAVOC`, le client 7.3 `TM_SC_RAGE` :
   aucune des deux références ne tranche la sémantique. Hors socle.
8. **`docs/packet-specs/` et les six MR ouvertes.** Aucune des six fiches n'est mergée : si Killian
   merge ces branches avant celle-ci, le dossier `docs/packet-specs/` existera déjà et l'exception
   `.gitignore` de cette branche (`!/docs/packet-specs/`) sera un doublon inoffensif.

---

## 16. A VERIFIER PAR KILLIAN

> Le socle de cette branche applique déjà les valeurs par défaut que ces questions laissent ouvertes :
> point de retour (a) au point 1, maxima de PV/MP au point 3, aucune pénalité au point 2, aucun
> effacement d'états au point 4. Le détail, point par point et fichier par fichier, est en §20.6.

1. **Politique de point de réapparition** (choix d'exploitation, aucune valeur inventée) :
   (a) la position persistée du personnage (`CharacterEntity.Position`/`Layer`, aucune migration) ;
   (b) un « point de retour » par personnage, mis à jour à la visite des villes (équivalent des
   marqueurs `rx`/`ry` de NGemity) — **migration EF requise** sur le contexte `Telecaster` ;
   (c) les coordonnées par race codées dans NGemity (`Player.cpp:2527-2546`), bonnes pour débloquer,
   fausses pour jouer. Le socle implémentera (a) par défaut si Killian ne tranche pas ; dis-moi si tu
   veux (b), c'est le seul chemin qui coûte une migration.
2. **Pénalité de mort** : perte d'expérience (`LastDecreasedExp` existe, `TM_SC_EXP_UPDATE` 1003
   existe) ? Le client 7.3 documente « At Lv5 or under, you will not lose experience points »
   (`db_string.rdb` ligne 10221) et des résurrections qui rendent une partie de l'expérience perdue.
   Le socle livre sans pénalité tant que ce n'est pas tranché.
3. **Points de vie rendus par la réapparition en ville** : maximum (`max_hp`), une fraction, ou 1 ?
   Aucune référence locale ne le donne (§15.4). Valeur par défaut proposée, à corriger si tu as la
   réponse : maximum de PV et de MP (cohérent avec la montée de niveau, `LevelingService.cs:48-57`).
4. **Effacement des états à la réapparition** (`EraseOnResurrect = 128`) : le socle doit-il déjà
   l'appliquer (aucun porteur n'existe aujourd'hui) ou laisser l'énumération déclarative ?
5. **`CLAUDE.md`** : y ajouter le pointeur vers `docs/packet-specs/` (inexistant aujourd'hui, §14) et
   y corriger la phrase sur `TS_SC_DEAD` (§3.4), avec le bloc §17. Hermes refuse l'écriture d'un agent
   dans ce fichier : c'est ton geste.
6. **Ordre de merge des six MR ouvertes** (§14) : si l'une d'elles part en conflit sur
   `GamePackets.cs`, c'est un conflit de zone, pas de ligne.

---

## 17. Bloc destiné à `CLAUDE.md` (à coller par Killian)

```markdown
### Mort et réapparition du personnage joueur

Le client Epic 7.3 **déclare** `TM_SC_DEAD` (504) mais son répartiteur le libère **sans effet**
(aucun handler : la table id→nom de `SFrame.exe` mappe 504 vers `TM_SC_DEAD` et le cas `cmp $0x1f8`
saute directement à la queue de libération). Aucun paquet serveur→client de mort n'existe donc :
le joueur est mort quand ses points de vie sont à 0, publiés par `TS_SC_ATTACK_EVENT` (`target_hp`)
et par la propriété `hp`. La phrase précédente de ce fichier (« there is no TS_SC_DEAD in this
version ») doit se lire ainsi.

La réapparition est demandée par le client avec `TM_CS_RESURRECTION` (513) : trame fixe de **12
octets**, `handle` (uint32) à l'offset 7 et `type` (int8) à l'offset 11. En 7.3, `type` remplace la
paire pré-6.1 `use_state`/`use_potion` (qui ferait 13 octets) : 0 = réapparition à la ville,
1 = par état, 2 = par objet, 3/4 = compétition/match à mort. NGemity compile en `EPIC_4_1_1`, donc
son `WorldSession::onRevive` lit `use_state`/`use_potion` et ignore `type` : à traduire, pas à
recopier.

`TM_SC_STATUS_CHANGE` (`500`) avec `1 << 8` est le drapeau mort **d'un monstre** : pour un handle de
joueur le même bit vaut `TCS_FlagSitdown`. Ne jamais envoyer 500 + `1 << 8` pour un joueur.

La fiche complète (enchaînement côté client, écarts NGemity, découpage, réserves) est dans
`docs/packet-specs/socle-mort-respawn.md`.

Le socle est en place : `TM_CS_RESURRECTION` (513) est décodé (trame de 12 octets, toute autre taille
refusée plutôt que lue), le personnage réapparaît à sa position persistée avec ses PV/MP au maximum,
et un monstre lâche une cible tombée à 0 PV (les PV d'un joueur n'ont plus de plancher à 1). Le
serveur n'émet toujours aucun paquet de mort.
```

---

## 18. Commits et binaires épinglés

| Référence | Version épinglée | Usage dans cette fiche |
|---|---|---|
| rzu | `87c1e83bf84efe29bb6405e8e6da80349712f3fa` (2023-10-02) | tailles, ordre des champs, gating de version |
| rzu `bdd362a6` (2017-02-26) | « Update packets based on available GS pdbs (5.2, 6.1, 6.2, 7.1, 7.2, 7.3, 7.4, 8.1) » | introduction de `TS_RESURRECTION_TYPE` et du gating `type >= EPIC_6_1` (PDB 7.3) |
| rzu `94885467` (2017-03-03) | « Update packets with old clients tests » | gating `handle >= EPIC_4_1`, `use_potion >= 4.1 && < 6.1` |
| rzu `05bc2d82` (2020-03-29) | « Packets: Use strong typedef for handles and game time values » | `ar_handle_t` (`GameTypes.h:40`) |
| rzu `11f2b6fd` (2020-08-11) | « packets: use versionned ID for all packets and update their ID with epic 9.6.3 » | identifiant 513 / 1513 |
| NGemity / Chihiro | `38ceb2c6065fabf6ff4ba71d52f955f362c6c839` (2025-12-03) | logique serveur, sous réserve de sa version |
| NGemity `ff604d2` (2018-01-13) | « Added reviving :^) » | introduction de `WorldSession::onRevive` |
| client Epic 7.3 | `reference/client73/SFrame.exe`, `sha256 41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e` (pas de dépôt git : `reference/client73/` est un extrait de ressources) | identifiants, absence de handler pour 504, piste de la fenêtre d'écran |
| client Epic 7.3 | `reference/client73/db_string.rdb` (base de chaînes ; lecture par `strings -n 5` / `-n 6`) | libellés d'écran de mort, texte de pénalité d'expérience |
| NavisLamia `master` | `6a982c81e6c87eb6dfca37fe3baf432d811ad1c7` (2026-07-18) | état du dépôt |

Méthode de lecture du client : `strings -n 4` / `-n 5` / `-n 6` pour les chaînes et la table
d'annotations, `objdump -d --start-address=… --stop-address=…` sur `SFrame.exe` pour le
répartiteur et la fabrique d'écrans, lecture d'octets brute pour les tables de saut
(`0x0067f19c`/`0x0067f218` et `0x0067f310`/`0x0067f35c`). **Aucun fichier du client n'a été exécuté**
(pas de `SFrame.exe`, pas de Lua, pas de script).

---

## 19. Note de livraison

- Fiche livrée : `docs/packet-specs/socle-mort-respawn.md`, branche
  `hermes/packet-socle-mort-respawn`, plus l'exception `.gitignore` `!/docs/packet-specs/`
  (identique aux branches 203/253/1202/408). Aucun fichier de code applicatif touché, aucun test
  ajouté. Base mesurée avant rédaction : `dotnet build Navislamia.sln -c Debug` → code de sortie **0**,
  `dotnet test Tests/Tests.csproj` → code de sortie **0**, **366** tests réussis, 0 échec.
- Le dév implémente le §12.1, écrit les tests d'offsets (taille 12 ; `Length` @0, `ID` @4,
  `Checksum` @6, `handle` @7, `type` @11) et recopie le bloc §17 dans la description de la MR.
  Il **n'écrit pas** `CLAUDE.md`.
- Les deux pièges à ne pas rater : (1) NGemity est compilé en `EPIC_4_1_1`, son handler lit des
  champs qui n'existent plus en 7.3 ; (2) `1 << 8` dans `TM_SC_STATUS_CHANGE` vaut « assis » pour un
  joueur, jamais « mort ».

---

## 20. Implémentation livrée (dev)

Socle de §12.1 seul, sur `hermes/packet-socle-mort-respawn`. Les points que le socle a dû trancher
faute de référence sont en §20.6, un par un avec ce qui les trancherait.

### 20.1 Critères d'acceptation

| # | Critère | État mesuré |
|---|---|---|
| 1 | `dotnet build Navislamia.sln -c Debug` | 0 erreur, code de sortie **0** |
| 2 | `dotnet test Tests/Tests.csproj` | code de sortie **0**, **387** tests réussis, 0 échec (366 avant la branche : +21) |
| 3 | Test d'offsets de la nouvelle trame | `Tests/Game/ResurrectionPacketTests.cs` : taille 12 et position de chaque champ |
| 4 | Enum et dispatch modifiés ensemble | `TM_CS_RESURRECTION = 513` **et** branche dans `GameClient.OnDataReceived` ; un test traverse la vraie boucle de réception, où un id sans branche atteindrait le `switch` qui lève |
| 5 | Savoir durable dans la fiche | cette section ; le bloc `CLAUDE.md` est en §17 et part dans la description de la MR |
| 6 | Version tranchée | trame 7.3 strictement de 12 octets ; aucun champ d'une autre version n'est lu |
| 7 | Aucun commit sur `master` locale | `git log --oneline origin/master..master` vide |
| 8 | Aucun champ NON ÉTABLI deviné | §20.6 et §15, non tranchés et non implémentés |

### 20.2 Fichiers livrés

| Fichier | Rôle |
|---|---|
| `Game/Network/Packets/Enums/GamePackets.cs` | `TM_CS_RESURRECTION = 513` dans la bande 500-517, entre `TM_CS_TARGETING` et `TM_CS_MONSTER_RECOGNIZE` |
| `Game/Network/Packets/Enums/ResurrectionType.cs` | `type` : `UseNone` 0, `UseState` 1, `UsePotion` 2, `Compete` 3, `Deathmatch` 4 |
| `Game/Network/Packets/Game/GameActionPackets.cs` | `TryReadResurrection` et `ResurrectionPacketLength` (12) |
| `Game/Services/ResurrectionRules.cs` | décisions pures : validations de §5.3, PV/MP rendus |
| `Game/Services/ResurrectionService.cs`, `Game/Services/Interfaces/IResurrectionService.cs` | warp, republication des PV/MP, acquittement |
| `Game/Network/Clients/GameClient.cs` | branche de dispatch et `HandleResurrection` |
| `Game/Network/NetworkService.cs`, `DevConsole/Program.cs` | câblage du service dans le conteneur |
| `Game/Network/Clients/ConnectionInfo.cs`, `Game/Network/Clients/Actions/GameActions.cs` | point de retour (`RespawnX`/`RespawnY`/`RespawnLayer`) capturé à l'entrée en jeu, remis à zéro par `ClearCharacterSession` |
| `Game/Services/MonsterAiRules.cs`, `Game/Services/MonsterAiService.cs` | plancher des PV à 0 et cible morte lâchée |
| `Tests/Game/ResurrectionPacketTests.cs` | 19 tests : offsets, validations, refus, chemin complet par la boucle de réception |
| `Tests/Game/MonsterAiRulesTests.cs`, `Tests/Game/ActionPacketsTests.cs` | +2 tests (plancher, cible morte) et les assertions du point de retour |

### 20.3 Offsets livrés

Trame client `TM_CS_RESURRECTION` (513), **12 octets**, sans remplissage :

| Offset | Taille | Champ |
|---|---|---|
| 0 | 4 | `Length` = 12 (uint32 LE) |
| 4 | 2 | `ID` = 513 (uint16 LE) |
| 6 | 1 | `Checksum` |
| 7 | 4 | `handle` (uint32 LE) |
| 11 | 1 | `type` (`int8`, `ResurrectionType`) |

Tests correspondants : `ClientFrame_IsTheFixedTwelveByteEpic73Shape`,
`ClientFrame_KeepsTheHandleAtSevenAndTheTypeAtEleven`,
`TryReadResurrection_ReadsEveryTypeTheReferenceDeclares`,
`TryReadResurrection_RefusesThePre61ThirteenByteShape`, `TryReadResurrection_RefusesAShortFrame`,
`EnumMember_SitsBetweenItsNeighbours`.

La lecture exige une taille **exactement** de 12 octets (`packet.Length != 12` → refus) : la forme
pré-6.1 en ferait 13 (`use_state`/`use_potion`), et accepter « au moins 12 » laisserait un octet
orphelin désaligner la suite du flux.

### 20.4 Réponses émises

| Situation | Réponse |
|---|---|
| trame dont la taille n'est pas 12 | `TM_SC_RESULT` tagué 513, `InvalidArgument` (28) — rien d'autre n'est écrit |
| session sans personnage en jeu (`CharacterHandle == 0`) | 513, `NotActable` (5) |
| `handle` ≠ personnage connecté | 513, `NotOwn` (3) |
| personnage vivant (`CharacterHp > 0`) | 513, `NotActable` (5) |
| `type` ∈ {1,2,3,4} | 513, `NotActable` (5), aucun déplacement, aucun état touché |
| personnage mort, son propre handle, `type = 0` | `TM_SC_WARP` (point de retour + son calque), `TM_SC_PROPERTY` `hp`, `TM_SC_PROPERTY` `mp`, puis `TM_SC_RESULT` tagué 513 `Success` |

L'ordre retenu est « warp, propriétés, acquittement », donc l'acquittement en dernier. §15.2 laissant
ouvert ce sur quoi le client 7.3 ferme sa fenêtre de mort, les trois trames partent ensemble ; la
position dans la séquence est un choix, pas une mesure.

### 20.5 Ce qui n'est pas porté, et pourquoi

- **Les chemins état, objet, compétition et match à mort** du 513 (§12.2) : refusés par `NotActable`,
  sans effet, en attendant leurs lots. Aucune supposition sur leurs effets.
- **La logique NGemity** : `WorldSession::onRevive` lit `use_state`/`use_potion` (compilation
  `EPIC_4_1_1`), champs qui n'existent plus en 7.3 ; elle est traduite (handle + type), pas recopiée.
- **`TM_SC_DEAD` (504)** : toujours pas émis, conformément à §3.2 et §12.2. Le client apprend la mort
  par `target_hp = 0` et par la propriété `hp`.
- **La pénalité de mort** (perte d'expérience, §16.2) : non implémentée, le socle livre sans pénalité.
- **L'effacement des états `StateTimeType.EraseOnResurrect = 128`** : non implémenté, l'énumération
  reste déclarative faute de porteur (§16.4).
- **La résurrection par autrui** (§11) : chantier distinct, non entamé.

### 20.6 Réserves : ce que le socle a tranché faute de référence

1. **Point de retour = option (a) de §16.1.** `GameActions.OnLogin` mémorise dans
   `ConnectionInfo.RespawnX/RespawnY/RespawnLayer` la position qu'il vient de lire sur
   `CharacterEntity` (avec le repli `DefaultSpawn` déjà en place). Aucune colonne, aucune migration.
   La valeur en base ne bouge qu'à la sauvegarde de fin de session (`SaveProgressAsync`), donc la
   valeur en mémoire est la valeur persistée pendant toute la session. Si Killian veut (b), la
   migration EF reste à faire et le point de retour devra être mis à jour à la visite des villes.
2. **PV/MP rendus = les maxima recalculés**, via `IStatService.Compute(info).Total` — la même source
   que la montée de niveau (`LevelingService.cs:48-57`), et non `CharacterMaxHp` seul, qui laisserait
   les MP à 0 (aucun maximum de MP n'est conservé dans `ConnectionInfo`). C'est le défaut proposé par
   §16.3, pas une mesure.
3. **Refus d'une session sans personnage** (`CharacterHandle == 0` → `NotActable`) : garde ajoutée
   au-delà de la liste de §5.3, parce qu'un client revenu au choix de personnage a
   `CharacterHandle == 0` **et** `CharacterHp == 0` : sans cette garde, un 513 avec `handle = 0`
   passerait toutes les autres validations et déclencherait un warp hors du monde.
4. **`type` hors {0,1,2,3,4}** → `NotActable`, comme les valeurs 1 à 4 : §5.3 ne tranche que
   l'appartenance à {1,2,3,4}, et `InvalidArgument` n'est employé que pour la taille de trame.
5. **`RespawnLayer` est appliqué avant le warp** (`info.Layer = info.RespawnLayer`). En pratique les
   deux valent la même chose aujourd'hui (`TM_CS_CHANGE_LOCATION` ne change que X/Y), mais le retour
   au point de retour doit rester correct si le calque devient modifiable.
6. **Le monstre lâche l'aggro et rentre chez lui** quand sa cible tombe à 0 PV : c'est la branche
   `Drop` existante (`GoHome`), pas une nouvelle politique. Il ne frappe plus (`Acquire` ignore aussi
   un personnage mort, sinon il réacquérirait une cible que `Act` relâche au tick suivant).
7. **Le plancher à 0 est celui de tous les dégâts de monstre** (`MonsterAiRules.PlayerHpAfterDamage`) :
   la formule de dégâts (`PlayerDamage`) n'est pas touchée, seul le plancher passe de 1 à 0.
8. **Ordre des trois trames** (warp, propriétés, acquittement) : choix documentaire, §15.2 non tranché.
9. **Le test de dispatch sait atteindre l'état de session** malgré `Client.ConnectionInfo` interne
   (`internal`, sans `InternalsVisibleTo` pour `Tests`) : le test le renseigne par réflexion
   (`ResurrectionPacketTests.SessionState`). À revoir si le dépôt ouvre un jour l'assemblée aux tests.

### 20.7 Commandes exécutées et codes de sortie

Depuis `/srv/navislamia/Navislamia`, `NUGET_PACKAGES=/srv/navislamia/.nuget-cache` :

| Commande | Code de sortie | Sortie |
|---|---|---|
| `dotnet build Navislamia.sln -c Debug` | **0** | `0 Error(s)` (160 avertissements préexistants) |
| `dotnet test Tests/Tests.csproj` | **0** | `Passed! - Failed: 0, Passed: 387, Skipped: 0, Total: 387` |
| `git log --oneline origin/master..master` | **0** | vide (aucun commit sur `master` locale) |
