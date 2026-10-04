"""Which packet ids the Epic 7.3 client dispatches, compared with GamePackets.

Usage: python tools/client73_packets.py <SFrame.exe> [<official GameMessage.h>]

The incoming dispatcher of SFrame.exe (entry 0x67DF59: movzx ecx, word [ebx+4]) is a binary tree of cmp/jg
with MSVC jump tables at its leaves. Every id 1..11000 is routed by a small concrete simulation; 0x67EF21 is
the "unhandled message" default, 0x67EF39 the empty case (handled, no effect). Requires capstone.
"""
import json, re, struct, sys
from pathlib import Path
import capstone

ENTRY, DEFAULT, EMPTY = 0x67DF5D, 0x67EF21, 0x67EF39
exe = Path(sys.argv[1]).read_bytes()
pe = struct.unpack_from('<I', exe, 0x3C)[0]
sections = struct.unpack_from('<H', exe, pe + 6)[0]
optional = struct.unpack_from('<H', exe, pe + 20)[0]
base = struct.unpack_from('<I', exe, pe + 24 + 28)[0]


def offset(va):
    for i in range(sections):
        o = pe + 24 + optional + 40 * i
        size, rva, raw_size, raw = struct.unpack_from('<IIII', exe, o + 8)
        if base + rva <= va < base + rva + max(size, raw_size):
            return raw + va - base - rva
    raise ValueError(hex(va))


md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
cache = {}


def at(va):
    if va not in cache:
        o = offset(va)
        cache[va] = next(md.disasm(exe[o:o + 16], va))
    return cache[va]


def s32(x):
    x &= 0xFFFFFFFF
    return x - (1 << 32) if x >> 31 else x


NODE = ('cmp', 'sub', 'add', 'dec', 'movzx', 'mov', 'jmp')


def route(pid):
    regs, flags, a = {'eax': 0, 'ecx': pid, 'edx': 0}, None, ENTRY
    for _ in range(200):
        if a in (DEFAULT, EMPTY):
            return a
        i = at(a)
        m, op, nxt = i.mnemonic, i.op_str, a + i.size
        if m == 'mov' and re.fullmatch(r'e[a-d]x, e[a-d]x', op):
            d, s = op.split(', ')
            regs[d] = regs[s]
        elif m == 'cmp':
            r, v = op.split(', ')
            flags = (regs[r] & 0xFFFFFFFF, int(v, 16))
        elif m in ('sub', 'add'):
            r, v = op.split(', ')
            regs[r] = (regs[r] + (-1 if m == 'sub' else 1) * int(v, 16)) & 0xFFFFFFFF
            flags = (regs[r], 0)
        elif m == 'dec':
            regs[op] = (regs[op] - 1) & 0xFFFFFFFF
            flags = (regs[op], 0)
        elif m == 'movzx' and 'byte ptr' in op:
            d, src = op.split(', ', 1)
            mm = re.search(r'\[(e[a-d]x) \+ (0x[0-9a-f]+)\]', src)
            regs[d] = exe[offset(int(mm.group(2), 16) + regs[mm.group(1)])]
        elif m == 'jmp':
            mm = re.search(r'\[(e[a-d]x)\*4 \+ (0x[0-9a-f]+)\]', op)
            nxt = struct.unpack_from('<I', exe, offset(int(mm.group(2), 16) + 4 * regs[mm.group(1)]))[0] if mm else int(op, 16)
            if nxt not in (DEFAULT, EMPTY) and at(nxt).mnemonic not in NODE[:-1]:
                return nxt
        elif m.startswith('j'):
            x, y = flags
            taken = {'je': x == y, 'jne': x != y, 'ja': x > y, 'jae': x >= y, 'jb': x < y, 'jbe': x <= y,
                     'jg': s32(x) > s32(y), 'jge': s32(x) >= s32(y), 'jl': s32(x) < s32(y), 'jle': s32(x) <= s32(y)}[m]
            if taken:
                nxt = int(op, 16)
                if nxt not in (DEFAULT, EMPTY) and at(nxt).mnemonic not in NODE:
                    return nxt
        else:
            return a
        a = nxt
    return None


incoming = {pid: t for pid in range(1, 11001) if (t := route(pid)) != DEFAULT}
root = Path(__file__).resolve().parents[1]
declared = {int(m.group(2)): m.group(1) for m in re.finditer(r'\b(T[MS]_[A-Z0-9_]+)\s*=\s*(\d+)',
            (root / 'Game/Network/Packets/Enums/GamePackets.cs').read_text(encoding='utf-8'))}
names = {}
if len(sys.argv) > 2:
    text = Path(sys.argv[2]).read_bytes().decode('cp949', 'replace')
    names = {int(m.group(2)): m.group(1) for m in re.finditer(r'unsigned\s+short\s+(T[MS]_\w+)\s*=\s*(\d+)', text)}
print(f'{len(incoming)} ids dispatched by the client, {sum(t == EMPTY for t in incoming.values())} as an empty case')
for pid, target in sorted(incoming.items()):
    if pid not in declared:
        print(f'  not declared: {pid:5} {names.get(pid, "?"):40} {"empty case" if target == EMPTY else hex(target)}')
