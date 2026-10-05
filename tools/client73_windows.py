"""Measure the script-window callbacks in the pinned Epic 7.3 SFrame.

Usage: python tools/client73_windows.py <SFrame.exe>
Uses client73_packets.py's PE mapping and Capstone; no executable is run.
"""
import contextlib
import hashlib
import io
import runpy
import struct
import sys
from pathlib import Path

with contextlib.redirect_stdout(io.StringIO()):
    binary = runpy.run_path(str(Path(__file__).with_name('client73_packets.py')))
exe, offset, md = binary['exe'], binary['offset'], binary['md']
assert hashlib.sha256(exe).hexdigest() == '41e0af2efafd35fc798ad4649b1a12ca5b27452d2015e5a63d6485b29fb9500e'


def string(va):
    start = offset(va)
    return exe[start:exe.index(b'\0', start)].decode('ascii')


def block(start, size):
    for address, _, mnemonic, operands in md.disasm_lite(exe[offset(start):offset(start) + size], start):
        print(f'  {address:08X} {mnemonic} {operands}')


for window, event, opening, name_va, template in [
    ('dungeon_raid_confirm_window', 0x27, 0x63BAFE, 0xA4B850, 0xA4C91C),
    ('secret_dungeon_confirm_window', 0x4C, 0x63BB80, 0xA4B81C, 0xA2CD80),
    ('instance_dungeon_confirm_window', 0x64, 0x63BD27, 0xA4B7C4, 0xA2CD80),
    ('instance_dungeon_confirm_window2', 0x62, 0x63BE3F, 0xA4B784, 0xA2CD80),
]:
    assert string(name_va) == window
    index = exe[offset(0x6429D4 + event - 2)]
    handler = struct.unpack_from('<I', exe, offset(0x6428F0 + index * 4))[0]
    instructions = list(md.disasm_lite(exe[offset(handler):offset(handler) + 100], handler))
    assert any(m == 'push' and op == hex(template) for _, _, m, op in instructions)
    print(f'{window}: open={opening:08X}, event={event}, reply={handler:08X}, format={string(template)!r}')
    block(handler, 100)

assert string(0xBB7538) == '%s'
print('recall_feather_confirm_window: open=0063BC3B, reply=0057B380, format=%s (exact trigger)')
block(0x57B3C1, 35)
assert string(0xA2CD30) == 'number_input_window'
print('number_input_window: open=0063B9E1, reply=00568FC1 / 0056935C, format=%s(%s) (edit text)')
block(0x568FC1, 30)
print('Cancel: confirmation replies test result==0; other result clears local trigger (006428C9), no 3001.')
print('Wire: all callbacks use script message constructor 004E3400, sent by 006491C0 as 3001.')
