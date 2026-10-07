"""Export the Epic 7.3 creature-farm ticket-cost table from the client's own db_creaturefarm.rdb.

Writes DevConsole/creature-farm-costs.73.json, read at startup by
Program.ConfigureCreatureFarmTicketCosts. The source is the client table, not the 9.4 export
(data/sqlserver/Arcadia/CreatureFarmResource.csv): that export is a later era (108 rows, form 1..3,
whose form 2 costs 7..10 where the 7.3 client costs 3..12), and it is excluded from Git anyway
(reference/README.md). Only the client copy prices what the 7.3 client can deposit.

Layout of db_creaturefarm.rdb (measured, docs/packet-specs/socle-cout-tickets-ferme.md §3.1): a
128-byte header (ASCII date then zeroes), a uint32 record count at 0x80, then one 4-byte record per
key, in increasing (rate, form, enhance_level) order: rate @0, form @1, enhance_level @2 and the
ticket_count it returns @3 — four uint8, which is the column order the official
GameContent::GetCreatureFarmTicketCount compares and the DDL of ArcadiaSchemaPSQL.sql uses.

The script refuses a file whose size is not 128 + 4 + N x 4, a record whose ticket_count is below 1
(0 is the reserved "no such key" answer the seam owes, so a free row would make an absent key
indistinguishable from a priced one), a duplicated key, and a body that does not cover the full
cartesian product of the three columns it declares (same discipline as tools/export_pet_catalog.py
and tools/export_monster_drops.py).

Usage:
    python tools/export_creature_farm_costs.py <db_creaturefarm.rdb>
"""
import argparse
import json
import os
import struct
import sys

HEADER_SIZE = 0x80
COUNT_OFFSET = HEADER_SIZE
RECORD_SIZE = 4
RATE, FORM, ENHANCE_LEVEL, TICKET_COUNT = 0, 1, 2, 3


def read_rows(path):
    data = open(path, 'rb').read()
    if len(data) < HEADER_SIZE + 4:
        sys.exit(f'{path}: {len(data)} bytes, too short for the {HEADER_SIZE}-byte header and its count')

    count = struct.unpack_from('<I', data, COUNT_OFFSET)[0]
    expected = HEADER_SIZE + 4 + count * RECORD_SIZE
    if len(data) != expected:
        sys.exit(f'{path}: {len(data)} bytes, expected {expected} for {count} records of {RECORD_SIZE}')

    rows = []
    for index in range(count):
        record = data[HEADER_SIZE + 4 + index * RECORD_SIZE:
                      HEADER_SIZE + 4 + (index + 1) * RECORD_SIZE]
        rate, form, enhance_level, ticket_count = struct.unpack('<4B', record)
        if ticket_count < 1:
            sys.exit(f'{path}: record {index} ({rate}, {form}, {enhance_level}) costs {ticket_count} '
                     'tickets; 0 is the answer reserved for a key the table does not carry')
        rows.append({'Rate': rate, 'Form': form, 'EnhanceLevel': enhance_level,
                     'TicketCount': ticket_count})

    keys = [(row['Rate'], row['Form'], row['EnhanceLevel']) for row in rows]
    if len(set(keys)) != len(keys):
        duplicated = sorted({key for key in keys if keys.count(key) > 1})
        sys.exit(f'{path}: {len(keys) - len(set(keys))} duplicated keys, first {duplicated[0]}')

    domains = [sorted({key[column] for key in keys}) for column in range(3)]
    product = len(domains[0]) * len(domains[1]) * len(domains[2])
    if product != len(keys):
        sys.exit(f'{path}: {len(keys)} records for {product} keys of '
                 f'{domains[0]} x {domains[1]} x {domains[2]}: the table is incomplete')
    if keys != sorted(keys):
        sys.exit(f'{path}: the records are not in increasing (rate, form, enhance_level) order')

    return rows, domains


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('rdb')
    parser.add_argument('--output', default=os.path.join('DevConsole', 'creature-farm-costs.73.json'))
    args = parser.parse_args()

    rows, domains = read_rows(args.rdb)
    with open(args.output, 'w', encoding='utf-8', newline='\n') as stream:
        json.dump({'CreatureFarmTicketCosts': {'Rows': rows}}, stream, indent=2)
        stream.write('\n')

    print(f'{len(rows)} rows, rate {domains[0][0]}..{domains[0][-1]}, form {domains[1][0]}..{domains[1][-1]}, '
          f'enhance_level {domains[2][0]}..{domains[2][-1]}, '
          f'{min(row["TicketCount"] for row in rows)}..{max(row["TicketCount"] for row in rows)} tickets '
          f'-> {args.output}')


if __name__ == '__main__':
    main()
