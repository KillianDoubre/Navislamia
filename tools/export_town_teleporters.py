"""Adds the town teleporters' dialogs to DevConsole/npc-dialogs.73.json.

The Epic 7.3 catalogue (tools/import_npc_dialogs.py, from the 9.4 Lua) has no page for the town teleporters:
their contact functions end on a conditional guild-dungeon entry, and their priced labels
("@90700620\\v#@price@#\\v48000") do not match its menu pattern. This tool reads them from the Epic 7 server
Lua (Epic 7 Part 4, branches/Live/NPC_TeleportTown.lua, 2012) and links each NPC to its Epic 7 contact
function (NPCResource.contact_script of the same dump). See docs/packet-specs/socle-point-de-retour.md.

Only the menu entries every player sees are written. An entry inside an `if` block (the guild dungeon
shortcut, scf_teleport_to_owned_dungeon) is left out, and so is the Halloween event entry of the Deva
teleporter (Trick_or_treat_2011, active during the event only). NPC_TeleportField_Beginner_contact depends
on a quest and the job depth: the server builds it (NpcDialogService), it is not written here.

    python tools/export_town_teleporters.py
"""
import argparse
import csv
import json
import re

DEFAULT_LUA = r"A:\Rappelz Kiff\Epic 7 Part 4\branches\Live\NPC_TeleportTown.lua"
DEFAULT_NPCS = r"data\epic7\NPCResource.csv"
DEFAULT_CATALOG = r"DevConsole\npc-dialogs.73.json"

CONTACTS = [
    "NPC_TeleportTown_Deva_contact",
    "NPC_TeleportTown_2_Deva_contact",
    "NPC_TeleportTown_Asura_contact",
    "NPC_TeleportTown_2_Asura_contact",
    "NPC_TeleportTown_Gaia_contact",
    "NPC_TeleportTown_2_Gaia_contact",
    "NPC_TeleportTown_Rondoh_contact",
    "NPC_TeleportTown_2_Rondoh_contact",
    "NPC_TeleportTown_Ancient_relic_contact",
]

# The Gaia teleporters' small talk, reached from their menu.
FOLLOW_UPS = ["quest_rumor6", "quest_rumor_a_3", "quest_rumor_b_3"]

# NPCs whose server-built page needs the contact link only.
LINK_ONLY = ["NPC_TeleportField_Beginner_contact"]

EXCLUDED_TRIGGERS = {"Trick_or_treat_2011()"}

FUNCTION = re.compile(r"(?m)^function\s+(\w+)\s*\(")
STRING = r"(\"(?:[^\"\\]|\\.)*\"|'(?:[^'\\]|\\.)*')"
TITLE = re.compile(r"^\s*dlg_title\s*\(\s*" + STRING + r"\s*\)")
TEXT = re.compile(r"^\s*dlg_text(?:_without_quest_menu)?\s*\(\s*" + STRING + r"\s*\)")
MENU = re.compile(r"^\s*dlg_menu\s*\(\s*" + STRING + r"\s*,\s*" + STRING + r"\s*\)")
BLOCK_OPEN = re.compile(r"^\s*(if|for|while)\b.*\b(then|do)\s*$")
BLOCK_CLOSE = re.compile(r"^\s*end\b")


def lua_string(literal: str) -> str:
    body = literal[1:-1]
    escapes = {"v": "\v", "n": "\n", "t": "\t", "\\": "\\", "'": "'", '"': '"'}
    return re.sub(r"\\(.)", lambda match: escapes.get(match.group(1), match.group(1)), body)


def read_functions(path: str) -> dict[str, list[str]]:
    with open(path, "rb") as stream:
        source = stream.read().decode("cp949", errors="replace").replace("\x00", "")
    source = re.sub(r"--\[\[.*?\]\]", "", source, flags=re.S)
    matches = list(FUNCTION.finditer(source))
    functions = {}
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(source)
        functions[match.group(1)] = source[match.end():end].splitlines()
    return functions


def read_dialog(name: str, lines: list[str]) -> dict:
    dialog = {"Title": "", "Text": "", "Menu": []}
    depth = 0
    for raw in lines:
        line = raw.split("--", 1)[0].rstrip()
        if not line.strip():
            continue
        if BLOCK_OPEN.match(line):
            depth += 1
            continue
        if BLOCK_CLOSE.match(line):
            if depth == 0:
                break
            depth -= 1
            continue
        if depth:
            continue
        if match := TITLE.match(line):
            dialog["Title"] = lua_string(match.group(1))
        elif match := TEXT.match(line):
            dialog["Text"] = lua_string(match.group(1))
        elif match := MENU.match(line):
            label, trigger = lua_string(match.group(1)), lua_string(match.group(2)).strip()
            if trigger in EXCLUDED_TRIGGERS:
                continue
            dialog["Menu"].append({"Label": label, "Trigger": trigger})
    if not dialog["Title"] or not dialog["Menu"]:
        raise SystemExit(f"{name}: no title or no menu read")
    return dialog


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--lua", default=DEFAULT_LUA)
    parser.add_argument("--npcs", default=DEFAULT_NPCS, help="Epic 7 NPCResource.csv (tools/rdu.py)")
    parser.add_argument("--catalog", default=DEFAULT_CATALOG)
    args = parser.parse_args()

    functions = read_functions(args.lua)
    with open(args.catalog, encoding="utf-8") as stream:
        catalog = json.load(stream)
    section = catalog["NpcDialogCatalog"]

    for name in CONTACTS + FOLLOW_UPS:
        if name not in functions:
            raise SystemExit(f"{name} is not in {args.lua}")
        section["Dialogs"][name] = read_dialog(name, functions[name])

    linked = set(CONTACTS) | set(LINK_ONLY)
    links = 0
    with open(args.npcs, encoding="utf-8") as stream:
        for row in csv.DictReader(stream):
            function = row["contact_script"].split("(", 1)[0].strip()
            if function in linked:
                section["Npcs"][row["id"]] = function + "()"
                links += 1

    catalog["Metadata"]["DialogCount"] = len(section["Dialogs"])
    catalog["Metadata"]["NpcCount"] = len(section["Npcs"])
    with open(args.catalog, "w", encoding="utf-8", newline="") as stream:
        json.dump(catalog, stream, ensure_ascii=False, separators=(",", ":"))
    print(f"{len(CONTACTS) + len(FOLLOW_UPS)} dialogs, {links} NPC links written to {args.catalog}")


if __name__ == "__main__":
    main()
