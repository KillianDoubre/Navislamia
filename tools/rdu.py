"""Reader for Gala Lab's RC_RDU table dumps (server-side resource tables of the Epic 7 era).

An .rdu file is a self-describing export of one SQL Server table:

    "RC_RDU" 01 01, u16 column count
    per column: u8 name length, name, u32 SQL Server type id, u16 size, u8 precision, u8 scale,
                u16 flags (bit 0 = nullable), u8 collation length, collation
    u16 index count, each: 7 bytes (three u16, a flag byte), u8 name length, name, u16 column count,
                each column: u8 name length, name, u8 sort order
    u32 row count
    per row, per column: for a nullable column an indicator byte (non-zero = NULL, no value follows),
                then a fixed-size value of `size` bytes for fixed types and decimals; a u16 length
                then that many bytes for character and binary types.

A decimal is an OLE DECIMAL of 16 bytes: u16 variant type (14 = VT_DECIMAL, 0 = VT_EMPTY for NULL),
u8 scale, u8 sign (0x80 = negative), u32 high 32 bits, u64 low 64 bits of the unscaled value.
The reader refuses a file it does not consume exactly, which is what validates the layout.

Usage:
    python tools/rdu.py FILE.rdu                 # schema and row count
    python tools/rdu.py FILE.rdu --csv OUT.csv   # PostgreSQL-ready CSV (header, NULL = empty field)
    python tools/rdu.py DIRECTORY --check        # parse every .rdu below a directory
"""
import csv
import decimal
import pathlib
import struct
import sys

FIXED = {
    0x30: ("tinyint", "<B"), 0x34: ("smallint", "<h"), 0x38: ("int", "<i"), 0x7F: ("bigint", "<q"),
    0x3B: ("real", "<f"), 0x3E: ("float", "<d"), 0x68: ("bit", "<B"),
}
VARIABLE = {0xA7: "varchar", 0xAF: "char", 0xE7: "nvarchar", 0xEF: "nchar", 0xA5: "varbinary", 0xAD: "binary"}
DECIMAL = {0x6A: "decimal", 0x6C: "numeric"}
DATETIME = {0x3D: "datetime", 0x3A: "smalldatetime"}


class Column:
    def __init__(self, name, type_id, size, precision, scale, collation):
        self.name, self.type_id, self.size = name, type_id, size
        self.precision, self.scale, self.collation = precision, scale, collation

    @property
    def type_name(self):
        if self.type_id in FIXED:
            return FIXED[self.type_id][0]
        return (VARIABLE.get(self.type_id) or DECIMAL.get(self.type_id) or DATETIME.get(self.type_id)
                or f"0x{self.type_id:02x}")


class RduTable:
    def __init__(self, path):
        self.path = pathlib.Path(path)
        data = self.path.read_bytes()
        if data[:6] != b"RC_RDU":
            raise ValueError(f"{path}: not an RC_RDU file")
        self._data, self._pos = data, 8
        count = self._u16()
        self.columns = [self._column() for _ in range(count)]
        self.indexes = [self._index() for _ in range(self._u16())]
        self.row_count = self._u32()
        self.rows = [[self._value(c) for c in self.columns] for _ in range(self.row_count)]
        if self._pos != len(data):
            raise ValueError(f"{path}: {len(data) - self._pos} trailing bytes after {self.row_count} rows")

    def _take(self, n):
        chunk = self._data[self._pos:self._pos + n]
        if len(chunk) != n:
            raise ValueError(f"{self.path}: truncated at {self._pos}")
        self._pos += n
        return chunk

    def _u8(self):
        return self._take(1)[0]

    def _u16(self):
        return struct.unpack("<H", self._take(2))[0]

    def _u32(self):
        return struct.unpack("<I", self._take(4))[0]

    def _column(self):
        name = self._take(self._u8()).decode("ascii")
        type_id = self._u32()
        size = self._u16()
        precision, scale = self._u8(), self._u8()
        nullable = self._u16() & 1
        collation = self._take(self._u8()).decode("ascii")
        column = Column(name, type_id, size, precision, scale, collation)
        column.nullable = bool(nullable)
        return column

    def _index(self):
        """One index or unique constraint: three u16 and a byte of flags, its name, then its columns, each
        a name followed by a sort-order byte."""
        self._take(7)
        name = self._take(self._u8()).decode("ascii")
        columns = []
        for _ in range(self._u16()):
            columns.append(self._take(self._u8()).decode("ascii"))
            self._u8()
        return name, columns

    def _value(self, column):
        # A nullable column carries an indicator byte before its value: zero, then the value; non-zero,
        # NULL with no value bytes.
        if column.nullable and self._u8() != 0:
            return None
        t = column.type_id
        if t in FIXED:
            return struct.unpack(FIXED[t][1], self._take(column.size))[0]
        if t in DATETIME:
            return self._take(column.size).hex()
        if t in DECIMAL:
            vt, scale, sign, high, low = struct.unpack("<HBBIQ", self._take(16))
            if vt == 0:
                return None
            unscaled = (high << 64) | low
            return decimal.Decimal(-unscaled if sign & 0x80 else unscaled).scaleb(-scale)
        if t in VARIABLE:
            raw = self._take(self._u16())
            if t in (0xE7, 0xEF):
                return raw.decode("utf-16-le")
            if t in (0xA5, 0xAD):
                return raw.hex()
            return raw.decode("cp949", errors="replace")
        raise ValueError(f"{self.path}: column {column.name} has unknown type 0x{t:02x}")

    def to_csv(self, out):
        with open(out, "w", encoding="utf-8", newline="") as f:
            w = csv.writer(f, quoting=csv.QUOTE_NONNUMERIC, lineterminator="\n")
            w.writerow([c.name for c in self.columns])
            for row in self.rows:
                w.writerow(["" if v is None else (int(v) if isinstance(v, bool) else v) for v in row])


def main(argv):
    target = pathlib.Path(argv[1])
    if "--check" in argv:
        failed = 0
        for path in sorted(target.rglob("*.rdu")):
            try:
                t = RduTable(path)
                print(f"ok   {t.row_count:>7} rows {len(t.columns):>3} cols  {path}")
            except Exception as exc:  # noqa: BLE001 - report every file
                failed += 1
                print(f"FAIL {path}: {exc}")
        return 1 if failed else 0
    t = RduTable(target)
    if "--csv" in argv:
        t.to_csv(argv[argv.index("--csv") + 1])
    print(f"{target}: {t.row_count} rows")
    for c in t.columns:
        print(f"  {c.name:<32} {c.type_name:<9} size={c.size} p={c.precision} s={c.scale} {c.collation}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
