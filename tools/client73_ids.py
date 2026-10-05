"""The resource ids the Epic 7.3 client itself knows, read from its db_*.rdb tables.

Extract the tables first (read-only): python tools/provision-navislamia/extract_client.py <client> <out>.
Layouts (measured on the 7.3 client, 2026-10-05): a 128-byte header, a u32 count, then fixed-size records whose
first field is the id. db_monster.rdb has no usable count (its records start at 132, 540 bytes each) and stores
the id scrambled like TS_SC_ENTER's monster_id.
"""
import pathlib
import struct


def _scramble_decode_map():
    encode = list(range(32))
    j = 3
    for i in range(32):
        encode[i], encode[j] = encode[j], encode[i]
        j = (j + i + 3) % 32
    decode = [0] * 32
    for index, value in enumerate(encode):
        decode[value] = index
    return decode


_DECODE = _scramble_decode_map()


def unscramble(value):
    result = 0
    for bit in range(32):
        if value & 1:
            result |= 1 << _DECODE[bit]
        value >>= 1
    return result - (1 << 32) if result >= 1 << 31 else result


def _records(path, size):
    data = pathlib.Path(path).read_bytes()
    count = struct.unpack_from("<I", data, 128)[0]
    if len(data) != 132 + count * size:
        raise ValueError(f"{path}: {len(data)} bytes is not 132 + {count} x {size}")
    return [struct.unpack_from("<i", data, 132 + i * size)[0] for i in range(count)]


# table -> (file, record size); the id is the first int32 of each record.
TABLES = {
    "ItemResources": ("db_item.rdb", 6256),
    "SkillResources": ("db_skill.rdb", 827),
    "NpcResources": ("db_npcresource.rdb", 1006),
    "SummonResources": ("db_creature.rdb", 950),
    "QuestResources": ("db_quest.rdb", 260),
}


def monster_ids(directory):
    data = (pathlib.Path(directory) / "db_monster.rdb").read_bytes()
    if (len(data) - 132) % 540:
        raise ValueError("db_monster.rdb fails the established 132 + N x 540 format")
    return {unscramble(struct.unpack_from("<I", data, 132 + i * 540)[0]) for i in range((len(data) - 132) // 540)}


def client_ids(directory):
    """{table: set of ids} for every Arcadia table the client has a table of its own for."""
    directory = pathlib.Path(directory)
    ids = {table: set(_records(directory / name, size)) for table, (name, size) in TABLES.items()}
    ids["MonsterResources"] = monster_ids(directory)
    return ids


def string_ids(path):
    """db_string: u32 name/text byte lengths, NUL strings, code i32 + five i32.

    Only the header's declared records are read by the client. The pinned community
    table has an ignored trailing 2804-byte fragment; it is not another client record.
    """
    data = pathlib.Path(path).read_bytes()
    if len(data) < 132:
        raise ValueError("Truncated db_string header")
    count = struct.unpack_from("<I", data, 128)[0]
    offset, result = 132, set()
    for _ in range(count):
        if offset + 8 > len(data):
            raise ValueError("Truncated db_string lengths")
        name_len, text_len = struct.unpack_from("<II", data, offset)
        start, code_at = offset + 8, offset + 8 + name_len + text_len
        if not name_len or not text_len or code_at + 24 > len(data):
            raise ValueError("Invalid db_string record lengths")
        if data[start + name_len - 1] != 0 or data[code_at - 1] != 0:
            raise ValueError("db_string strings must end with NUL")
        result.add(struct.unpack_from("<i", data, code_at)[0])
        offset = code_at + 24
    return result
