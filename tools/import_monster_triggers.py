"""Import the official ETC_run_monster_skill.lua as the embedded monster trigger script.

Usage: python tools/import_monster_triggers.py path/to/ETC_run_monster_skill.lua
The server source distribution uses cp949. Only comments, the module-name declaration and
the duplicated `monster_id == monster_id ==` comparison are changed.
"""
import argparse
import pathlib
import re


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=pathlib.Path)
    parser.add_argument("--encoding", default="cp949")
    args = parser.parse_args()
    script = args.source.read_text(encoding=args.encoding)
    script = re.sub(r"--[^\n]*", "", script)
    script = re.sub(r"function get_module_name\(\).*?end", "", script, count=1, flags=re.S)
    script = script.replace("monster_id == monster_id ==", "monster_id ==")
    script = "\n".join(line.rstrip() for line in script.splitlines() if line.strip())
    output = pathlib.Path(__file__).resolve().parent.parent / "Game/Scripting/Scripts/monster_triggers.lua"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("-- Official ETC_run_monster_skill.lua; comments removed, duplicate comparison corrected.\n"
                      + script + "\n", encoding="utf-8")
    print(f"{output.name}: {len(script.splitlines())} lines")


if __name__ == "__main__":
    main()
