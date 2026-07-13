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

The extended login packet expected by this client stores `faceId`, `skinColor`, `hairId`, then
`faceTextureId` after `race`; the name starts at absolute packet offset 82. This order was recovered
from the actual `SFrame.exe` deserializer and handler. In particular, the client consumes absolute
offset 70 as the primary body color. Sending `faceId` there makes the body blue-purple. After world
entry the server reinforces the hidden-equipment and skin values with `TS_SC_HIDE_EQUIP_INFO (222)`
and `TS_SC_SKIN_INFO (224)`.

This client is calibrated to expect the base hair model in wear slot 13. The face model must stay out
of slot 12 because it overrides the correctly textured login face with a transparent mesh. The login
result remains authoritative for the face and hair IDs, while slot 13 prevents the subsequent
equipment refresh from dropping the hairstyle. `TS_SC_HAIR_INFO (220)` is kept for runtime hairstyle
changes and is not sent during the initial bootstrap.

## Login sequence

After `TS_SC_LOGIN_RESULT (4)` and player `TS_SC_ENTER (3)`, the server sends:

1. `TS_SC_STAT_INFO (1000)`
2. one or more `TS_SC_INVENTORY (207)` packets, with at most 45 Epic 7.3 item records each
3. `TS_EQUIP_SUMMON (303)`
4. `TS_SC_WEAR_INFO (202)`
5. hidden-equipment and skin packets, with skin last so it applies to the assembled body
6. `TS_SC_GOLD_UPDATE (1001)`
7. `TS_SC_LEVEL_UPDATE (1002)` and `TS_SC_EXP_UPDATE (1003)`
8. numeric `TS_SC_PROPERTY (507)` packets, including current and previous jobs
9. empty `TS_SC_ADDED_SKILL_LIST (404)` until learned skills are implemented
10. `TS_SC_BELT_SLOT_INFO (216)`
11. `TS_SC_GAME_TIME (1101)`
12. `TS_SC_STATUS_CHANGE (500)`

The Epic 7.3 wear packet is 323 bytes including its seven-byte frame. It has a handle, 24 item codes,
24 enhancement values, 24 item levels and 24 elemental-effect bytes. The appearance-code array starts
at Epic 7.4 and must not be appended for this client.

This client build copies 85 bytes per inventory item. Its layout contains a four-byte
`appearance_code` between the elemental-effect block and `wear_position`, despite that field being
associated with Epic 7.4 in the public protocol reference. Omitting it shifts the equipped slot and
corrupts every item after the first one.

## Tests

`GameCharacterPacketsTests` checks model mapping, wear size and slots, inventory offsets, progression
packet fields and frame checksums. `CharacterDefaultsTests` checks all three starter jobs and the
automatic repair of existing characters.
