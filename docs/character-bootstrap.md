# Epic 7.3 character bootstrap

## Stored defaults

Characters created directly through EF must receive the values normally assigned by the original
Telecaster creation procedure:

- level and maximum reached level `1`
- job level `1`
- job depth `Base`
- Gaia job `Rogue (100)`, Deva job `Guide (200)`, Asura job `Stepper (300)`
- three zeroed previous-job and previous-job-level entries
- a distinct lobby slot, the default world position and level-one HP/MP
- distinct inventory indices for the starter armor, weapon and bag

`CharacterDefaults` applies the same defaults when an older row is loaded. Repairs are persisted once,
so characters created before this support was added no longer keep job and level zero.

## Appearance mapping

The five model values retain the order sent by the character lobby:

| Index | Meaning | World field |
| ---: | --- | --- |
| 0 | Face | `faceId` |
| 1 | Hair | `hairId` |
| 2 | Base armor | armor fallback in `TS_SC_WEAR_INFO` |
| 3 | Base gloves | glove fallback in `TS_SC_WEAR_INFO` |
| 4 | Base boots | boots fallback in `TS_SC_WEAR_INFO` |

The lobby consumes the array as-is. This ordering is calibrated against the actual Epic 7.3 client;
it differs from some newer server implementations. Skin color, face texture, hair color and the
hide-equipment flag are copied independently from the character row.

The extended login packet expected by this client stores `faceTextureId`, `skinColor`, `faceId`, then
`hairId` after `race`; the name starts at absolute packet offset 82. This order was recovered from the
actual `SFrame.exe` deserializer and model handler. The client consumes absolute offset 70 as the
primary body color, offset 74 as the face model and offset 78 as the hair model. After world entry the
server reinforces the hidden-equipment and skin values with `TS_SC_HIDE_EQUIP_INFO (222)` and
`TS_SC_SKIN_INFO (224)`.

The base face and hair models must stay out of wear slots 12 and 13. They are cosmetic model IDs, not
item resource codes; treating them as equipped items creates geometry with transparent materials.
The local actor takes its face and hair IDs from the login result; its own player-enter packet does not
rebuild it because it already exists. Player-enter remains authoritative for other players seen in the
world. `TS_SC_HAIR_INFO (220)` must not be sent during bootstrap when the persisted custom RGB is
zero, because the runtime update path applies that value as a transparent material. Hair-info updates
are reserved for later changes with a resolved nonzero RGB color.

## Login sequence

After `TS_SC_LOGIN_RESULT (4)` and player `TS_SC_ENTER (3)`, the server sends:

1. `TS_SC_STAT_INFO (1000)`
2. one or more `TS_SC_INVENTORY (207)` packets, with at most 45 Epic 7.3 item records each
3. `TS_EQUIP_SUMMON (303)`
4. `TS_SC_WEAR_INFO (202)`
5. hidden-equipment and skin packets
6. `TS_SC_GOLD_UPDATE (1001)`
7. `TS_SC_LEVEL_UPDATE (1002)` and `TS_SC_EXP_UPDATE (1003)`
8. numeric `TS_SC_PROPERTY (507)` packets, including current and previous jobs
9. empty `TS_SC_ADDED_SKILL_LIST (404)` until learned skills are implemented
10. `TS_SC_BELT_SLOT_INFO (216)`
11. `TS_SC_GAME_TIME (1101)`
12. `TS_SC_STATUS_CHANGE (500)`
13. string `TS_SC_PROPERTY (507)` named `client_info`

`client_info` is the Epic 7.3 character-level settings blob. It contains the quick-slot defaults,
the complete `KMT` keyboard map and chat-mode flags as one pipe-delimited ASCII string. The server
stores it in the Telecaster `Characters.ClientInfo` text column. Older rows with no value receive the
default map during character loading. When the player changes a binding, the client sends the whole
value back in `TS_CS_SET_PROPERTY (508)`, normally during logout, and the server persists it for the
next login. Later-Epic properties such as `quick_slot`, `current_key` and `saved_key` are not used by
this client build.

The Epic 7.3 wear packet is 323 bytes including its seven-byte frame. It has a handle, 24 item codes,
24 enhancement values, 24 item levels and 24 elemental-effect bytes. The appearance-code array starts
at Epic 7.4 and must not be appended for this client.

This client build copies 85 bytes per inventory item. Its layout contains a four-byte
`appearance_code` between the elemental-effect block and `wear_position`, despite that field being
associated with Epic 7.4 in the public protocol reference. Omitting it shifts the equipped slot and
corrupts every item after the first one.

## Tests

`GameCharacterPacketsTests` checks model mapping, wear size and slots, inventory offsets, progression
packet fields and frame checksums. `GameStatPacketsTests` checks the numeric and string property
layouts. `CharacterDefaultsTests` checks all three starter jobs, the complete Epic 7.3 key map and the
automatic repair of existing characters.

## Return to character selection

Pressing the character-selection button sends `TM_CS_REQUEST_RETURN_LOBBY (25)`, which receives a
successful `TS_SC_RESULT (0)` tagged with request ID 25. This result only authorizes SFrame to display
the confirmation popup; the server does not save, clear state or start a scene transition yet. When
the user clicks Yes, SFrame sends `TM_CS_RETURN_LOBBY (23)`. Only then does the server stop combat,
persist level, exp, JP, gold and chaos, clear the active character/world state while retaining the
account session, and send the successful result tagged as 23. Sending result 23 automatically after
packet 25 runs the final scene handler before the popup has been confirmed and crashes `SFrame.exe`.

The validated Epic 7.3 exchange is:

```text
SFrame -> Game  TM_CS_REQUEST_RETURN_LOBBY (25)
Game   -> SFrame TS_SC_RESULT (0), RequestMsgID=25
                 [the player confirms the popup]
SFrame -> Game  TM_CS_RETURN_LOBBY (23)
Game   -> SFrame TS_SC_RESULT (0), RequestMsgID=23
SFrame -> Game  TM_CS_CHARACTER_LIST (2001)
Game   -> SFrame TS_SC_CHARACTER_LIST (2004)
```

Both result packets use the standard 15-byte Epic 7.3 layout. A duplicate packet 23 is ignored while
the first transition is running. Progress persistence is awaited before state is cleared; a database
failure is logged but does not leave SFrame blocked in the world. The character list is not pushed
early: the existing handler answers only when SFrame requests it after switching scenes.

`ActionPacketsTests` verifies that the reset removes all character and streamed-world state without
discarding the authenticated account session, and that both acknowledgements carry the required
request ID.
`CharacterDefaultsTests` verifies that progress persistence is awaitable.
