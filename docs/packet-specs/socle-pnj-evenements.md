# PNJ et entrées de dialogue d'événement (Halloween, Noël, Saint-Valentin, Pâques)

| | |
|---|---|
| Code | `Game/Services/NpcEvents.cs`, `NpcDialogService.ShownMenu`, `NpcSpawnService.Sync`, `GameRuleOptions.Events` |
| Données | `Game/Services/npc-event-menus.json` (embarqué), `tools/export_npc_event_menus.py` |
| Tests | `Tests/Game/NpcEventsTests.cs` |
| Voir aussi | `socle-pnj-pays-periodes.md` (quels PNJ sont posés), `socle-props.md` (props d'événement) |

## 1. Le constat

Bug noté le 2026-10-06 : « les dialogues de PNJ sont tous affichés peu importe la date, dont les événements Halloween et
Noël ». Après le filtre des PNJ (`socle-pnj-pays-periodes.md`), il restait deux choses :

- **une entrée d'Halloween dans les menus de PNJ permanents** : « 사탕주면 안잡아먹지! » (« des bonbons ou je te
  mange ! », `@90604959`, `Trick_or_treat_2011()`) chez 18 PNJ (marchands de nourriture, guides d'aventure, entrepôts,
  marchand d'équipement d'Asura…), et `Valentine_2016_Kale_Give()` chez Kale (6038, Rondo), importée de la 9.4 (2016
  est postérieur à l'Epic 7 ; son libellé `@90610184` n'existe pas dans le `db_string.rdb` du client 7.3) ;
- **des PNJ d'événement posés toute l'année** parce que leur `local_flag` vaut 0 : les cinq esprits-citrouilles
  9982-9986 (`npc_wonderland_teleport_contact`, modèle `spirit_blackpumpkin`) et le dragon de Pâques 11477
  (`NPC_dragonservicer_easter_contact`).

## 2. Ce que fait le serveur officiel

**Aucune date.** Le Lua Epic 7 (`branches/Live/NPC_FoodShop.lua:39-40`) écrit :

```lua
--할로윈 사탕받기//할로윈 이벤트에만 가동 Npc_event.lua에 있음
		dlg_menu( "@90604959", 'Trick_or_treat_2011()' )
```

(« bonbons d'Halloween // actif pendant l'événement Halloween seulement, dans NPC_Event.lua »). Ni la ligne ni
`Trick_or_treat_2011` (`NPC_Event.lua:1531`) ne testent la date : **l'opérateur ajoutait l'entrée au script pour
l'événement et l'en retirait ensuite**. La copie 2015 (`Resource/Script`) garde la ligne à l'identique. Le catalogue
`npc-dialogs.73.json`, aplati depuis ces scripts, la garde donc pour toujours.

## 3. Ce qui est porté

- **Un événement s'ouvre par une fenêtre de dates** dans `GameRules:Events` :

  ```json
  "GameRules": {
    "Events": [
      { "Name": "Halloween", "From": "10-20", "To": "11-05" },
      { "Name": "Christmas", "From": "12-20", "To": "01-05" }
    ]
  }
  ```

  `From`/`To` sont `MM-dd`, inclus, à la date locale du serveur ; une fenêtre dont la fin précède le début enjambe le
  Nouvel An. **Aucun événement n'est ouvert par défaut** : c'est l'état d'un script officiel hors événement. Les dates
  ci-dessus sont un exemple, pas une donnée officielle : aucune source ne fixe les périodes. La date est relue à chaque
  contact et à chaque passage de visibilité ; la liste des fenêtres est lue au démarrage.
- **Une entrée de menu d'événement** (`NpcEvents.EventOfMenu`) est retirée du menu tant que son événement est fermé ;
  elle n'est donc pas annoncée et ne peut pas être choisie (`NpcDialogTriggers`). Les deux chemins de menus figés
  passent par `NpcDialogService.ShownMenu` (`TryShow`, `ShowQuestContactAsync`).
- **Un PNJ d'événement** (`NpcEvents.EventOfNpc`) n'entre dans aucune vue tant que son événement est fermé
  (`NpcSpawnService.Sync`, à côté de la période) ; il apparaît à l'ouverture sans redémarrage.

## 4. Quelles entrées, quels PNJ

`tools/export_npc_event_menus.py` écrit `npc-event-menus.json` :

- une fonction est d'événement si elle est définie dans `NPC_Event.lua` (Epic 7 `Live` ou 2015), si son nom porte
  `event` comme mot (`NPC_2015_Event_rental_contact`), ou si son nom dit l'événement ;
- **Menus** = les seules *portes* : une entrée d'événement dans le menu d'un dialogue qui n'est pas lui-même un
  événement (5 : `Trick_or_treat_2011`, `Valentine_2016_Kale_Give`, `NPC_event_contact`, `darkwood_event_1/2`). Ce qui
  est derrière une porte n'est atteint que par elle ;
- **Npcs** = les PNJ du catalogue dont le contact est une fonction d'événement (845). Seuls six sont posés par les
  règles de `socle-pnj-pays-periodes.md` (9982-9986, 11477) ; les autres ne le sont déjà pas ;
- le nom de l'événement vient du nom de la fonction : `hallow`/`trick_or_treat`/`pumpkin`/`wonderland` → `Halloween`,
  `valentine` → `Valentine`, `xmas`/`christmas`/`santa`/`rangifer` → `Christmas`, `easter` → `Easter`,
  `new_year`/`lunar` → `NewYear` ; sinon la fonction elle-même (`NPC_Event_Lvup_contact`…), ouvrable par ce nom.

## 5. NON ÉTABLI

- Les périodes réelles de chaque événement en 7.3 : rien ne les donne, d'où l'absence de valeur par défaut.
- Si les esprits-citrouilles 9982-9986 sont joignables hors Halloween : ils se tiennent vers y = 13 000-15 000, dans
  une zone que rien ne relie au reste du monde dans nos données. Les cacher hors événement ne retire rien de jouable.
- Ce que `Trick_or_treat_2011` rend une fois ouvert : la fonction est du Lua officiel que le catalogue n'exécute pas
  (aucun dialogue `Trick_or_treat_2011` dans `npc-dialogs.73.json`). Ouvrir Halloween montre l'entrée ; son effet
  reste à brancher sur le bac à sable Lua (`npc_dialogs.json`) le jour où l'événement est voulu.
