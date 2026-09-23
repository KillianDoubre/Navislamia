<#
.SYNOPSIS
Exports every table of the 9.4 SQL Server databases to CSV files, so the import scripts no longer need
SQL Server running.

.DESCRIPTION
One file per table, `<OutputDirectory>\<Database>\<Table>.csv`, plus `_manifest.json` listing each
table's columns, SQL types and row count. The files are local data and are git-ignored (/data/sqlserver/).

The CSV is the one PostgreSQL's `\copy ... WITH (FORMAT csv, HEADER)` reads directly:
- a header row with the source column names;
- NULL is an empty unquoted field, and every string is quoted, so an empty string stays distinct
  from NULL;
- numbers use the invariant culture (a dot, never a comma), bits are 0/1, dates are ISO 8601,
  binary columns are PostgreSQL bytea hex (`\x...`);
- UTF-8 without a BOM (psql's \copy rejects a BOM). Text comes out of SqlClient already decoded with
  each column's collation, so the Korean/Chinese/Russian string tables survive the trip.

Run it once while SQL Server is up; re-run it only if the source database changes.
#>
[CmdletBinding()]
param(
    [string]$SqlServer = 'localhost\SQLEXPRESS',
    [string[]]$Databases = @('Arcadia'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'data\sqlserver')
)

$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Data -TypeDefinition @'
using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Text;

public static class SqlCsvExporter
{
    public static long Export(string connectionString, string table, string path, out string[] columns, out string[] types)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();
            using (var command = new SqlCommand("SELECT * FROM [dbo].[" + table.Replace("]", "]]") + "]", connection))
            {
                command.CommandTimeout = 0;
                using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
                using (var writer = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16))
                {
                    writer.NewLine = "\n";
                    var count = reader.FieldCount;
                    columns = new string[count];
                    types = new string[count];
                    for (var i = 0; i < count; i++)
                    {
                        columns[i] = reader.GetName(i);
                        types[i] = reader.GetDataTypeName(i);
                        if (i > 0) writer.Write(',');
                        WriteQuoted(writer, columns[i]);
                    }
                    writer.WriteLine();

                    long rows = 0;
                    while (reader.Read())
                    {
                        for (var i = 0; i < count; i++)
                        {
                            if (i > 0) writer.Write(',');
                            if (reader.IsDBNull(i)) continue;
                            WriteValue(writer, reader.GetValue(i));
                        }
                        writer.WriteLine();
                        rows++;
                    }

                    return rows;
                }
            }
        }
    }

    private static void WriteValue(TextWriter writer, object value)
    {
        var text = value as string;
        if (text != null) { WriteQuoted(writer, text); return; }
        if (value is bool) { writer.Write((bool)value ? "1" : "0"); return; }
        if (value is DateTime) { writer.Write(((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)); return; }
        if (value is DateTimeOffset) { writer.Write(((DateTimeOffset)value).ToString("o", CultureInfo.InvariantCulture)); return; }
        if (value is Guid) { writer.Write(value.ToString()); return; }
        var bytes = value as byte[];
        if (bytes != null)
        {
            var builder = new StringBuilder("\\x", 2 + bytes.Length * 2);
            foreach (var b in bytes) builder.Append(b.ToString("x2"));
            writer.Write(builder.ToString());
            return;
        }
        if (value is double) { writer.Write(((double)value).ToString("R", CultureInfo.InvariantCulture)); return; }
        if (value is float) { writer.Write(((float)value).ToString("R", CultureInfo.InvariantCulture)); return; }
        var formattable = value as IFormattable;
        if (formattable != null) { writer.Write(formattable.ToString(null, CultureInfo.InvariantCulture)); return; }
        WriteQuoted(writer, value.ToString());
    }

    private static void WriteQuoted(TextWriter writer, string text)
    {
        writer.Write('"');
        writer.Write(text.Replace("\"", "\"\""));
        writer.Write('"');
    }
}
'@

foreach ($database in $Databases) {
    $connectionString = "Server=$SqlServer;Database=$database;Integrated Security=True;TrustServerCertificate=True"
    $directory = Join-Path $OutputDirectory $database
    New-Item -ItemType Directory -Force $directory | Out-Null

    $tables = @(sqlcmd -S $SqlServer -E -d $database -h -1 -W -Q `
        "SET NOCOUNT ON; SELECT name FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY name;" |
        Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() })
    if ($tables.Count -eq 0) { throw "No table found in $database on $SqlServer." }

    Write-Host ("{0}: {1} tables -> {2}" -f $database, $tables.Count, $directory) -ForegroundColor Cyan
    $manifest = [ordered]@{
        source     = "$SqlServer/$database"
        exportedAt = (Get-Date).ToString('s')
        tables     = [ordered]@{}
    }

    $total = 0
    foreach ($table in $tables) {
        $columns = $null
        $types = $null
        $rows = [SqlCsvExporter]::Export($connectionString, $table, (Join-Path $directory "$table.csv"),
            [ref]$columns, [ref]$types)
        $total += $rows
        $manifest.tables[$table] = [ordered]@{
            rows    = $rows
            columns = @(for ($i = 0; $i -lt $columns.Count; $i++) { [ordered]@{ name = $columns[$i]; type = $types[$i] } })
        }
        Write-Host ("  {0,-40} {1,9} rows" -f $table, $rows)
    }

    $json = $manifest | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText((Join-Path $directory '_manifest.json'), $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("{0}: {1} rows exported." -f $database, $total) -ForegroundColor Green
}
