"""Export the auction house catalogue to DevConsole/auction-catalog.73.json.

What the official AuctionManager reads at runtime besides the auctions themselves:

- Categories: AuctionCategoryResource (category_id, sub_category_id, item_group, item_class; -1 = none/any), the
  same 38 rows as the 7.3 client's db_auctioncategoryresource.rdb (128-byte header, u32 count, 24-byte records).
- Items: for every item the 7.3 client knows (its db_item.rdb), the name id and the English in-game name
  (StringResource_EN by code), which the keyword search matches (StructItem::GetNameInGame, case-insensitive), and
  the group/class the categories classify.

Usage: python tools/export_auction_catalog.py --client-items db_item.rdb
"""
import csv
import json
import os
import struct
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SQL = os.path.join(ROOT, 'data', 'sqlserver', 'Arcadia')
EPIC7 = os.path.join(ROOT, 'data', 'epic7')
OUT = os.path.join(ROOT, 'DevConsole', 'auction-catalog.73.json')
csv.field_size_limit(10 ** 9)


def rows(path):
    with open(path, encoding='utf-8') as stream:
        return list(csv.DictReader(stream))


def client_items(path):
    data = open(path, 'rb').read()
    count = struct.unpack_from('<I', data, 128)[0]
    stride = (len(data) - 132) // count
    return {struct.unpack_from('<i', data, 132 + i * stride)[0] for i in range(count)}


def main(argv):
    allowed = client_items(argv[argv.index('--client-items') + 1]) if '--client-items' in argv else None
    categories = [{'CategoryId': int(r['category_id']), 'SubCategoryId': int(r['sub_category_id']),
                   'NameId': int(r['name_id']), 'ItemGroup': int(r['item_group']), 'ItemClass': int(r['item_class'])}
                  for r in rows(os.path.join(SQL, 'AuctionCategoryResource.csv')) if r['local_flag'] in ('0', '')]
    names = {r['code']: r['value'] for r in rows(os.path.join(SQL, 'StringResource_EN.csv'))}
    items = []
    for r in rows(os.path.join(EPIC7, 'ItemResource.csv')):
        code = int(r['id'])
        if allowed is not None and code not in allowed:
            continue
        items.append({'Code': code, 'NameId': int(r['name_id'] or 0), 'Name': names.get(r['name_id'], ''),
                      'Group': int(r['group'] or 0), 'Class': int(r['class'] or 0)})
    with open(OUT, 'w', encoding='utf-8') as stream:
        json.dump({'AuctionCatalog': {'Categories': categories, 'Items': items}}, stream, ensure_ascii=False,
                  separators=(',', ':'))
    named = sum(1 for i in items if i['Name'])
    print(f'{os.path.basename(OUT)}: {len(categories)} categories, {len(items)} items ({named} named)')


if __name__ == '__main__':
    main(sys.argv)
