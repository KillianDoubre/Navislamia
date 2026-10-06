"""Writes the 7.3 client's own NPC country flags to Game/Services/npc-client-flags.json.

The client's db_npcresource.rdb carries a local_flag per NPC (int32 at +36 of each 1006-byte record, after id,
text_id, name_text_id, race, sex, x, y, z and face — the server table's column order). It is a later state than the
Epic 7 server dump: it switches off the events that were over (Wonderland, Christmas 2011, every 9.4-only event
row) and the Korean cash shop merchants. The server excludes an NPC that either source excludes
(NpcSpawnService, docs/packet-specs/socle-pnj-pays-periodes.md).

    python tools/export_npc_client_flags.py <directory holding db_npcresource.rdb>
"""
import json
import pathlib
import struct
import sys

RECORD = 1006
FLAG_OFFSET = 36

source = pathlib.Path(sys.argv[1]) / "db_npcresource.rdb"
data = source.read_bytes()
count = struct.unpack_from("<I", data, 128)[0]
if len(data) != 132 + count * RECORD:
    raise SystemExit(f"{source}: {len(data)} bytes is not 132 + {count} x {RECORD}")

flags = {}
for index in range(count):
    offset = 132 + index * RECORD
    npc_id = struct.unpack_from("<i", data, offset)[0]
    flags[str(npc_id)] = struct.unpack_from("<i", data, offset + FLAG_OFFSET)[0]

target = pathlib.Path(__file__).resolve().parents[1] / "Game/Services/npc-client-flags.json"
target.write_text(json.dumps(dict(sorted(flags.items(), key=lambda item: int(item[0]))), separators=(",", ":")) + "\n",
                  encoding="utf-8")
print(f"{len(flags)} NPC flags written to {target}")
