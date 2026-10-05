"""Typed resource references, starting from the server's advertised Lua entry points.

Unlike a bag of all integers, a packet id, coordinate, comment or effect type never
protects a resource of another family. See filtre-ressources-73.md and its 134-row audit.
"""
import json
import re
from pathlib import Path

FAMILIES = ("ItemResources", "SkillResources", "StateResources", "StatResources",
            "MonsterResources", "NpcResources", "SummonResources", "QuestResources")
CALLS = {
    "ItemResources": ("insert_item", "find_item", "has_item", "get_item_handle", "get_item_name_id"),
    "SkillResources": ("cast_skill", "get_skill_level", "learn_skill"),
    "StateResources": ("add_state", "remove_state", "get_state_level"),
    "NpcResources": ("add_npc",),
    "MonsterResources": ("add_monster",),
    "QuestResources": ("get_quest_progress", "set_quest_status", "quest_info"),
}


def reachable(functions, dialogs, roots):
    """Follow direct calls and calls inside advertised dialogue strings, with cycles bounded."""
    seen, pending = set(), list(roots)
    while pending:
        for name in re.findall(r"\b([A-Za-z_]\w*)\s*\(", pending.pop()):
            if name in seen:
                continue
            seen.add(name)
            if name in functions:
                pending.append(functions[name])
            if name in dialogs:
                pending.extend(m["Trigger"] for m in dialogs[name].get("Menu", []))
            # NpcScriptCatalog.cs:32-33: six exported aliases.
            if name.startswith("NPC_all_2012_EnchantEvent_b_") and name + "_n" in functions:
                pending.append(functions[name + "_n"])
    return seen


def typed_lua(text):
    text = re.sub(r"--\[\[.*?\]\]|--[^\n]*", "", text, flags=re.S)
    found = {family: set() for family in FAMILIES}
    for family, calls in CALLS.items():
        for call in calls:
            found[family].update(int(n) for n in re.findall(r"\b" + call + r"\s*\(\s*(\d+)\b", text))
    # Resource-code dispatch/translation tables, not numeric amounts or coordinates.
    found["ItemResources"].update(int(n) for n in re.findall(
        r"\b(?:item_code|reward_code|code)\s*(?:==|<=|>=|<|>|=)\s*(\d+)\b", text))
    return found


def references(root, known_npcs, event_handlers=(), prop_scripts=()):
    root = Path(root)
    scripts = root / "Game/Scripting/Scripts"
    functions = json.loads((scripts / "npc_dialogs.json").read_text(encoding="utf-8-sig"))
    functions.update(json.loads((scripts / "event_area_functions.json").read_text(encoding="utf-8-sig")))
    catalog = json.loads((root / "DevConsole/npc-dialogs.73.json").read_text(encoding="utf-8-sig"))["NpcDialogCatalog"]
    roots = [v for k, v in catalog["Npcs"].items() if int(k) in known_npcs]
    roots += list(event_handlers) + list(prop_scripts)
    called = reachable(functions, catalog["Dialogs"], roots)
    found = {family: set() for family in FAMILIES}
    for text in roots + [body for name, body in functions.items() if name in called]:
        for family, ids in typed_lua(text).items():
            found[family].update(ids)
    # Explicit C# resource constants. Add another only with a runtime caller and source citation.
    # Jobs/SkillResetRules.cs:23, JobChangeService.Reset.cs:154,266;
    # official Resource/Script/NPC_JobChange.lua:95,123,150 (race stone and set_race).
    found["ItemResources"].add(601100284)
    return found, called


def monster_references(path, known_monsters):
    """Only branches for client/catalog monsters can spawn or attach an outside resource.

    Official ETC_run_monster_skill.lua (trigger); ScriptService.cs:51-63 has
    add_state(code,...) and respawn_near_monster(handle,code,count).
    monster_skill_cast uses a skill slot, not a SkillResource id.
    """
    found = {family: set() for family in FAMILIES}
    for block in re.split(r"(?m)(?=^\t(?:if|elseif) monster_id)", Path(path).read_text(encoding="utf-8-sig"))[1:]:
        selectors = {int(n) for n in re.findall(r"monster_id == (\d+)", block.splitlines()[0])}
        if not selectors & known_monsters:
            continue
        for family, ids in typed_lua(block).items():
            found[family].update(ids)
        found["MonsterResources"].update(int(n) for n in re.findall(
            r"respawn_near_monster\s*\(\s*monster_handle\s*,\s*(\d+)", block))
    return found
