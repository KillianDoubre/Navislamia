"""Build DevConsole/market-catalog.73.json and name the merchant triggers of npc-dialogs.73.json.

Sources (the Epic 7 Part 4 SVN dump, read with tools/rdu.py):
  * trunk/MarketResource.rdu: sort_id, name, code, price_ratio, huntaholic_ratio.
  * trunk/ItemResource.rdu: the item base price. NGemity's loader turns the ratio into the price the
    window shows and the buyer pays: floor(price_ratio * item.price), huntaholic point 0
    (ObjectMgr.cpp:847-852); a line whose item is unknown is skipped, like the reference.
  * the client's own db_item.rdb (optional): a line whose item the 7.3 client does not know would render
    nothing, so it is dropped.
  * the server Lua of the dump (trunk and branches): each <npc>_contact() names its market literally,
    open_market( 'name' ).

The dialog catalogue only kept the truncated prefix "open_market(" (the 9.4 corpus it was generated from
built the name at run time), and it kept both branches of the scripts' `if cash_usable_server == 0`
test as two menu entries. Each merchant dialog is rewritten here to one entry per market actually
offered, with the full trigger. A server without the cash shop is what the scripts call
cash_usable_server == 0, so the flat_sum_* market wins whenever a pair exists.

Dialogs the Lua does not cover are named by NAME_RULES below: the market names of MarketResource follow
the NPC function names closely enough for these, and each rule is spelled out rather than guessed at run
time. A dialog no rule names keeps its truncated trigger, which the market service still refuses.

Usage: python tools/export_market_catalog.py PART4_DIR [--client-items db_item.rdb]
"""
import json
import pathlib
import re
import struct
import sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import rdu  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parent.parent
MARKETS_OUT = ROOT / "DevConsole" / "market-catalog.73.json"
DIALOGS = ROOT / "DevConsole" / "npc-dialogs.73.json"

RACES = {"Asura": "asura", "Deva": "deva", "Gaia": "gaia", "Rondoh": "rondoh",
         "Ancient_relic": "Ancient_relic", "Mare": "mare"}


def name_rules(function):
    """The market names a dialog without Lua offers, in menu order (flat_sum first when it exists)."""
    m = re.fullmatch(r"NPC_Merchant_(Weapon|Etc)_(\w+?)_contact", function)
    if m and m.group(2) in RACES:
        kind, race = m.group(1).lower(), RACES[m.group(2)]
        return [f"flat_sum_{race}_{kind}", f"{race}_{kind}"]
    m = re.fullmatch(r"NPC_ItemUP_(\w+)_contact", function)
    if m and m.group(1) in ("Asura", "Deva", "Gaia", "Rondoh", "Beginner"):
        prefix = "deva" if m.group(1) == "Deva" else m.group(1)
        return [f"{prefix}_itemUP_suppert"]
    fixed = {
        "NPC_Merchant_Weapon_Beginner_contact": ["beginner_weapon"],
        "NPC_Merchant_Etc_Beginner_contact": ["beginner_etc"],
        "NPC_Merchant_Weapon_Secroute_contact": ["secroute_weapon"],
        "NPC_Merchant_Equip_Secroute_contact": ["secroute_equip"],
        "NPC_Merchant_Creature_card_contact": ["Creature_card"],
        "NPC_Merchant_deco_effect_contact": ["deco_effect"],
        "NPC_Merchant_inhance_helper_contact": ["inhance_helper_randombox"],
        "NPC_Secroute_tickets_contact": ["secroute_tickets"],
        "NPC_Merchant_crushop_etc_contact": ["crushop_etc"],
        "NPC_Merchant_crushop_hair_contact": ["crushop_hair"],
    }
    if function in fixed:
        return fixed[function]
    if re.fullmatch(r"NPC_Merchant_expansion_bag_(asura|deva|gaia|rondoh)_contact", function):
        return ["expansion_acc_bag"]
    return None


def lua_markets(part4):
    """{contact function: [market, ...]} from every Lua script of the dump, trunk first."""
    found = {}
    for sub in ("trunk", "branches", "trunk_testserver"):
        for path in sorted((part4 / sub).rglob("*.lua")):
            text = path.read_bytes().decode("cp949", errors="replace")
            parts = re.split(r"(?m)^function\s+(\w+)\s*\([^)]*\)", text)
            for i in range(1, len(parts), 2):
                calls = re.findall(r"open_market\(\s*'([^']+)'\s*\)", parts[i + 1])
                if calls and parts[i] not in found:
                    found[parts[i]] = calls
    return found


def offered(names, known):
    """Collapse the cash-shop pairs to the flat_sum market, keep the others, drop unknown markets.

    Names are matched without case, as the reference's Korean_Wansung_CI_AS collation did (the scripts
    write Foodshop_etc for the table's foodshop_etc), and the table's own spelling is kept."""
    canonical = {k.lower(): k for k in known}
    lowered = [n.lower() for n in names]
    out = []
    for name in lowered:
        if f"flat_sum_{name}" in lowered and f"flat_sum_{name}" in canonical:
            continue
        if name in canonical and canonical[name] not in out:
            out.append(canonical[name])
    return out


def client_items(path):
    """Item ids of the client's db_item.rdb: a 128-byte header, a u32 count, fixed-size records."""
    data = pathlib.Path(path).read_bytes()
    count = struct.unpack_from("<I", data, 128)[0]
    stride, rest = divmod(len(data) - 132, count)
    if rest:
        sys.exit(f"{path}: {len(data)} bytes do not split into {count} records")
    return {struct.unpack_from("<i", data, 132 + i * stride)[0] for i in range(count)}


def main(argv):
    part4 = pathlib.Path(argv[1])
    allowed = client_items(argv[argv.index("--client-items") + 1]) if "--client-items" in argv else None

    items = rdu.RduTable(part4 / "trunk" / "ItemResource.rdu")
    price_column = [c.name for c in items.columns].index("price")
    prices = {row[0]: row[price_column] for row in items.rows}

    market = rdu.RduTable(part4 / "trunk" / "MarketResource.rdu")
    rows, skipped = [], {"unknown item": 0, "not in client": 0}
    for sort_id, name, code, ratio, _hunt in market.rows:
        if code not in prices:
            skipped["unknown item"] += 1
            continue
        if allowed is not None and code not in allowed:
            skipped["not in client"] += 1
            continue
        rows.append({"Name": name, "SortId": sort_id, "Code": code,
                     "Price": int((ratio * prices[code]).to_integral_value(rounding="ROUND_FLOOR")),
                     "HuntaholicPoint": 0})
    known = {r["Name"] for r in rows}
    MARKETS_OUT.write_text(json.dumps({"MarketCatalog": {"Markets": rows}}, indent=1) + "\n", encoding="utf-8")
    print(f"{MARKETS_OUT.name}: {len(rows)} lines over {len(known)} markets, skipped {skipped}")

    lua = lua_markets(part4)
    doc = json.loads(DIALOGS.read_text(encoding="utf-8"))
    dialogs = doc["NpcDialogCatalog"]["Dialogs"]
    named, unnamed = 0, []
    for function, dialog in dialogs.items():
        menu = dialog.get("Menu", [])
        merchant = [i for i, m in enumerate(menu) if m.get("Trigger", "").startswith("open_market")]
        if not merchant:
            continue
        source = lua.get(function) or name_rules(function)
        markets = offered(source, known) if source else []
        if not markets:
            unnamed.append(function)
            continue
        entries = [dict(menu[merchant[min(i, len(merchant) - 1)]],
                        Trigger=f"open_market( '{name}' )") for i, name in enumerate(markets)]
        first = merchant[0]
        rest = [m for i, m in enumerate(menu) if i not in merchant]
        dialog["Menu"] = rest[:first] + entries + rest[first:]
        named += 1
    DIALOGS.write_text(json.dumps(doc, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"{DIALOGS.name}: {named} merchant dialogs named, {len(unnamed)} left refused: {unnamed}")


if __name__ == "__main__":
    main(sys.argv)
