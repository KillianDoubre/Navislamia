import argparse
import glob
import json
import os
import re
import subprocess


FUNCTION_PATTERN = re.compile(r"(?m)^\s*function\s+([\w.:]+)\s*\([^\r\n]*")
TITLE_PATTERN = re.compile(r"\bdlg_title\s*\(\s*[\"']\s*(@\d+)\s*[\"']")
TEXT_PATTERN = re.compile(r"\bdlg_text(?:_without_quest_menu)?\s*\(\s*[\"']\s*(@\d+)\s*[\"']")
MENU_PATTERN = re.compile(
    r"\bdlg_menu\s*\(\s*[\"']\s*(@\d+)\s*[\"']\s*,\s*[\"']([^\"']*)[\"']"
)
CONTACT_FUNCTION_PATTERN = re.compile(r"^\s*([A-Za-z_][A-Za-z0-9_]*)")


def read_contacts(server: str, database: str) -> dict[int, str]:
    query = (
        "SET NOCOUNT ON; SELECT id, MAX(contact_script) FROM dbo.NPCResource "
        "WHERE ISNULL(contact_script, '') <> '' GROUP BY id ORDER BY id"
    )
    result = subprocess.run(
        ["sqlcmd", "-S", server, "-E", "-C", "-d", database, "-h", "-1", "-W", "-s", "|", "-Q", query],
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    contacts: dict[int, str] = {}
    for line in result.stdout.splitlines():
        if "|" not in line:
            continue
        resource_id, expression = line.split("|", 1)
        contacts[int(resource_id.strip())] = expression.strip()
    return contacts


def read_functions(lua_directory: str) -> dict[str, str]:
    functions: dict[str, str] = {}
    for path in sorted(glob.glob(os.path.join(lua_directory, "*.lua"))):
        with open(path, "rb") as stream:
            source = stream.read().decode("latin1")
        matches = list(FUNCTION_PATTERN.finditer(source))
        for index, match in enumerate(matches):
            end = matches[index + 1].start() if index + 1 < len(matches) else len(source)
            functions[match.group(1)] = source[match.end():end]
    return functions


def read_dialogs(functions: dict[str, str]) -> dict[str, dict]:
    dialogs: dict[str, dict] = {}
    for name, body in sorted(functions.items()):
        show = body.find("dlg_show")
        if show < 0:
            continue
        visible_path = body[:show]
        title_match = TITLE_PATTERN.search(visible_path)
        text_match = TEXT_PATTERN.search(visible_path)
        menu = [
            {"Label": match.group(1), "Trigger": match.group(2).strip()}
            for match in MENU_PATTERN.finditer(visible_path)
        ]
        if title_match is None and text_match is None:
            continue
        dialogs[name] = {
            "Title": title_match.group(1) if title_match else "",
            "Text": text_match.group(1) if text_match else "",
            "Menu": menu,
        }
    return dialogs


def read_contact_function(expression: str) -> str:
    match = CONTACT_FUNCTION_PATTERN.match(expression)
    return match.group(1) if match else ""


def main() -> None:
    parser = argparse.ArgumentParser(description="Build the Epic 7.3 NPC dialog catalog")
    parser.add_argument("--sql-server", default=r".\SQLEXPRESS")
    parser.add_argument("--database", default="Arcadia")
    parser.add_argument("--lua-directory", required=True)
    parser.add_argument("--output", default="DevConsole/npc-dialogs.73.json")
    args = parser.parse_args()

    contacts = read_contacts(args.sql_server, args.database)
    functions = read_functions(args.lua_directory)
    dialogs = read_dialogs(functions)
    renderable = sum(read_contact_function(expression) in dialogs for expression in contacts.values())
    catalog = {
        "Metadata": {
            "ClientEpic": "7.3",
            "Source": "9.4 NPCResource contact scripts and decompressed server Lua, using Epic 7 string references",
            "NpcCount": len(contacts),
            "DialogCount": len(dialogs),
            "DirectlyRenderableNpcCount": renderable,
        },
        "NpcDialogCatalog": {
            "Npcs": {str(key): value for key, value in sorted(contacts.items())},
            "Dialogs": dialogs,
        },
    }
    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    with open(args.output, "w", encoding="utf-8", newline="") as stream:
        json.dump(catalog, stream, ensure_ascii=True, separators=(",", ":"))
    print(f"Wrote {len(contacts)} NPC links and {len(dialogs)} dialogs to {args.output}")


if __name__ == "__main__":
    main()
