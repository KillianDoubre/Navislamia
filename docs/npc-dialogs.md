# Epic 7.3 NPC dialogs

## Data flow

The client starts an interaction with `TS_CS_CONTACT (3002)`. Its only payload is the four-byte NPC
handle at offset 7. This is the temporary handle assigned when that NPC was streamed to this client,
not the Arcadia resource ID. The server rejects handles that are not present in the client's current
visible set, then resolves the resource ID to its contact function.

Each connection maintains both `resource ID -> handle` and `handle -> resource ID` indexes. Contact
resolution is therefore an O(1) lookup and never scans all visible NPCs. Both indexes are updated under
the same lock when an NPC enters or leaves the visibility window; leaving also invalidates an open
dialog belonging to that NPC.

The dialog catalog is `DevConsole/npc-dialogs.73.json`. It was generated from the `contact_script`
column of the available 9.4 `NPCResource` table and the decompressed server Lua. The catalog retains
the `@<id>` resource references used by the scripts, so the Epic 7.3 client resolves them through its
own localized string database. The Lost Island functions are explicitly marked as Epic 7 content in
the source scripts. The generator is `tools/import_npc_dialogs.py`.

The local PostgreSQL Arcadia table has also been populated with the matching `ContactScript` values
for all 1,445 matching resources. Runtime rendering uses the versioned catalog so a clean checkout
does not depend on the external SQL Server or the decompressed Lua directory.

At startup, `NpcDialogService` normalizes contact expressions to function names, validates menu
entries, builds all static `TS_SC_DIALOG` packet templates and freezes both lookup dictionaries. A
click only performs dictionary lookups, copies the selected template and inserts the connection's NPC
handle. JSON parsing, Lua-expression parsing and string encoding never occur on the interaction path.

## Packets

`TS_SC_DIALOG (3000)` has this Epic 7.3 layout:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | total packet length |
| 4 | `uint16` | packet ID `3000` |
| 6 | `byte` | header checksum |
| 7 | `int32` | dialog type |
| 11 | `uint32` | NPC handle |
| 15 | `uint16` | title byte length |
| 17 | `uint16` | text byte length |
| 19 | `uint16` | menu byte length |
| 21 | bytes | title, text and menu without null terminators |

The original server appends each menu choice as:

```text
\t<label>\t<trigger>\t
```

The client returns a selected choice in `TS_CS_DIALOG (3001)`: a `uint16` trigger length at offset 7,
followed by the trigger bytes at offset 9.

## Safety and current scope

The parser rejects malformed packets and triggers above 1,024 bytes before allocating their string.
The server remembers the exact triggers sent in the current dialog and rejects every other value.
It extracts only the function name and performs a catalog lookup; client input is never evaluated as
Lua. Static follow-up dialog functions therefore work without exposing a remote script-execution path.

Dialogs and their close/menu choices are implemented. Actions that mutate gameplay state or open a
specialized window, including markets, teleportation, storage, auctions and quests, require their own
packet and service implementations. Their Lua triggers are intentionally not executed yet.
