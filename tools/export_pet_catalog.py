"""Export the Epic 7.3 pet catalogue from the client's own db_pet.rdb.

Writes DevConsole/pet-catalog.73.json, read at startup by Program.ConfigurePetCatalog. The client table is
the source, not the 9.4 PetResource export: it lists the 106 pets this client knows, and its cage links
are unique, whereas the 9.4 table reuses 27 cage ids for two pets each (a later-epic artefact).

Layout of db_pet.rdb (measured, docs/packet-specs/socle-familier-pet.md, lot 2): a 128-byte header (date
and packer banner), a uint32 record count at 0x80, then fixed 332-byte records from 0x84 with
id @0, type @4, name_id @8, cage_id @12 (all int32) and the model name as a nul-terminated ASCII string
@64. The script refuses a file whose size does not divide into that layout.

Usage:
    python tools/export_pet_catalog.py <db_pet.rdb> [--strings data/sqlserver/Arcadia/StringResource_EN.csv]

The English names come from the 9.4 string export (name_id is shared); a missing name stays empty and
the client shows its own.
"""
import argparse
import csv
import json
import os
import struct
import sys

HEADER_SIZE = 0x80
RECORD_SIZE = 332
MODEL_OFFSET = 64
MODEL_SIZE = 64


def read_pets(path):
    data = open(path, 'rb').read()
    count = struct.unpack_from('<I', data, HEADER_SIZE)[0]
    expected = HEADER_SIZE + 4 + count * RECORD_SIZE
    if len(data) != expected:
        sys.exit(f'{path}: {len(data)} bytes, expected {expected} for {count} records of {RECORD_SIZE}')

    pets = []
    for index in range(count):
        record = data[HEADER_SIZE + 4 + index * RECORD_SIZE:HEADER_SIZE + 4 + (index + 1) * RECORD_SIZE]
        pet_id, pet_type, name_id, cage_id = struct.unpack_from('<4i', record, 0)
        model = record[MODEL_OFFSET:MODEL_OFFSET + MODEL_SIZE].split(b'\0')[0].decode('ascii', 'replace')
        pets.append({'Id': pet_id, 'Type': pet_type, 'NameId': name_id, 'CageId': cage_id, 'Model': model})
    return pets


def read_names(path):
    if not path or not os.path.exists(path):
        return {}
    names = {}
    with open(path, encoding='utf-8-sig', newline='') as stream:
        for row in csv.DictReader(stream):
            names[row.get('code') or row.get('id')] = row.get('value') or ''
    return names


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('rdb')
    parser.add_argument('--strings', default=os.path.join('data', 'sqlserver', 'Arcadia', 'StringResource_EN.csv'))
    parser.add_argument('--output', default=os.path.join('DevConsole', 'pet-catalog.73.json'))
    args = parser.parse_args()

    pets = read_pets(args.rdb)
    names = read_names(args.strings)

    cages = {}
    for pet in pets:
        if pet['CageId'] <= 0:
            sys.exit(f"pet {pet['Id']} has no cage")
        if pet['CageId'] in cages:
            sys.exit(f"cage {pet['CageId']} is linked to pets {cages[pet['CageId']]} and {pet['Id']}")
        cages[pet['CageId']] = pet['Id']
        # The wire name is ASCII, 18 characters at most; anything else is left to the client.
        name = names.get(str(pet['NameId']), '')
        pet['Name'] = name if name.isascii() else ''

    pets.sort(key=lambda pet: pet['Id'])
    with open(args.output, 'w', encoding='utf-8', newline='\n') as stream:
        json.dump({'PetCatalog': {'Pets': pets}}, stream, indent=2)
        stream.write('\n')

    named = sum(1 for pet in pets if pet['Name'])
    print(f'{len(pets)} pets, {len(cages)} cages, {named} named -> {args.output}')


if __name__ == '__main__':
    main()
