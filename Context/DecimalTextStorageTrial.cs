using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Carwash.API.Context;

public static class DecimalTextStorageTrial
{
    public static IReadOnlyDictionary<string, string[]> Columns { get; } = new Dictionary<string, string[]>
    {
        ["CajaDiaria"] = ["MontoInicial", "TotalIngresosEfectivo", "TotalIngresosTransferencias", "TotalIngresosDatafono", "TotalIngresosCredito", "TotalEgresos", "TotalValesOperarios", "TotalFinal"],
        ["Turnos"] = ["Valor", "ValorBaseComision", "PorcentajeComision", "ValorComision"],
        ["TurnosMovimientos"] = ["MontoPagado"],
        ["GastosCaja"] = ["Valor"],
        ["ValesOperarios"] = ["Valor"]
    };

    public sealed record SchemaEntry(string Type, string Name, string Table, string? Sql);
    public sealed record TableData(string Name, string[] Names, object?[][] Rows);
    public sealed record Snapshot(SchemaEntry[] Schema, TableData[] Tables, string Integrity, int ForeignKeyIssues)
    {
        public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                Schema,
                Tables = Tables.Select(table => new { table.Name, table.Names, Rows = table.Rows.Select(row => row.Select(Cell)) }),
                Integrity,
                ForeignKeyIssues
            }))));
    }

    public static string GuardPath(string root, string path, bool writable)
        => ValidatePath(root, path, writable, false);

    public static string GuardNewPath(string root, string path, bool writable)
        => ValidatePath(root, path, writable, true);

    private static string ValidatePath(string root, string path, bool writable, bool newPath)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        path = Path.GetFullPath(path);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(Path.GetFileName(root), @"\ACarWash\.DecimalText\.P5-[0-9a-f]{32}\z")
            || !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Trial path is outside the authorized TEMP root.");
        var relative = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        var allowed = writable ? new[] { "trial", "faults", "fixtures" } : new[] { "baseline", "trial", "faults", "fixtures" };
        if (relative.Length < 2 || !allowed.Contains(relative[0], StringComparer.Ordinal))
            throw new InvalidOperationException("Trial path has no authorized role.");
        for (var entry = new FileInfo(path) as FileSystemInfo; entry != null; entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            if ((File.Exists(entry.FullName) || Directory.Exists(entry.FullName)) && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Reparse paths are not permitted.");
        if ((newPath ? File.Exists(path) || Directory.Exists(path) : !File.Exists(path))
            || new[] { "-wal", "-shm", "-journal" }.Any(suffix => File.Exists(path + suffix)))
            throw new InvalidOperationException("Trial requires an existing standalone copy.");
        return path;
    }

    public static Snapshot Read(string root, string path)
    {
        using var connection = Open(GuardPath(root, path, false), true);
        Execute(connection, null, "PRAGMA query_only=ON");
        using var transaction = connection.BeginTransaction(deferred: true);
        return Capture(connection, transaction);
    }

    public static Snapshot Expected(Snapshot baseline)
    {
        if (baseline.Integrity != "ok" || baseline.ForeignKeyIssues != 0
            || baseline.Schema.Any(entry => entry.Type is "view" or "trigger"))
            throw new InvalidOperationException("Unsupported or inconsistent source schema.");
        foreach (var table in Columns.Keys)
            if (baseline.Tables.Count(entry => entry.Name == table) != 1)
                throw new InvalidOperationException("Missing allowlisted table.");
        var schema = baseline.Schema.Select(entry => entry.Type == "table" && Columns.TryGetValue(entry.Name, out var fields)
            ? entry with { Sql = ChangeTypes(entry.Sql!, fields) } : entry).ToArray();
        var tables = baseline.Tables.Select(table =>
        {
            var rows = table.Rows.Select(row => row.ToArray()).ToArray();
            if (Columns.TryGetValue(table.Name, out var fields))
                foreach (var field in fields)
                {
                    var index = Array.IndexOf(table.Names, field);
                    if (index < 0) throw new InvalidOperationException("Missing allowlisted field.");
                    foreach (var row in rows) row[index] = ExactValue(row[index]);
                }
            return table with { Rows = rows };
        }).ToArray();
        return baseline with { Schema = schema, Tables = tables };
    }

    public static bool Run(string root, string path, Snapshot baseline, Action<string>? checkpoint = null)
    {
        path = GuardPath(root, path, true);
        var expected = Expected(baseline);
        using var connection = Open(path, false);
        var foreignKeys = Scalar(connection, null, "PRAGMA foreign_keys");
        Execute(connection, null, "PRAGMA foreign_keys=OFF");
        try
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            var current = Capture(connection, transaction);
            if (current.Fingerprint == expected.Fingerprint) return false;
            if (current.Fingerprint != baseline.Fingerprint)
                throw new InvalidOperationException("Source differs from the approved snapshot.");
            checkpoint?.Invoke("validated");
            foreach (var tableName in Columns.Keys)
            {
                var sourceTable = current.Tables.Single(table => table.Name == tableName);
                var targetTable = expected.Tables.Single(table => table.Name == tableName);
                var sourceSql = expected.Schema.Single(entry => entry.Type == "table" && entry.Name == tableName).Sql!;
                var temporaryName = "__p5_" + tableName;
                var prefix = "CREATE TABLE " + Quote(tableName);
                if (!sourceSql.StartsWith(prefix + " (", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsupported CREATE TABLE header.");
                Execute(connection, transaction, "CREATE TABLE " + Quote(temporaryName) + sourceSql[prefix.Length..]);
                checkpoint?.Invoke(tableName + ":create");
                foreach (var row in targetTable.Rows)
                {
                    using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO " + Quote(temporaryName) + " (" + string.Join(",", sourceTable.Names.Select(Quote))
                        + ") VALUES (" + string.Join(",", row.Select((_, index) => "@value" + index)) + ")";
                    for (var index = 0; index < row.Length; index++)
                        insert.Parameters.AddWithValue("@value" + index, row[index] ?? DBNull.Value);
                    insert.ExecuteNonQuery();
                }
                checkpoint?.Invoke(tableName + ":copy");
                Execute(connection, transaction, "DROP TABLE " + Quote(tableName));
                checkpoint?.Invoke(tableName + ":drop");
                Execute(connection, transaction, "ALTER TABLE " + Quote(temporaryName) + " RENAME TO " + Quote(tableName));
                checkpoint?.Invoke(tableName + ":rename");
                foreach (var index in current.Schema.Where(entry => entry.Type == "index" && entry.Table == tableName && entry.Sql != null))
                    Execute(connection, transaction, index.Sql!);
                checkpoint?.Invoke(tableName + ":indexes");
            }
            var sequences = current.Tables.SingleOrDefault(table => table.Name == "sqlite_sequence");
            if (sequences != null)
            {
                Execute(connection, transaction, "DELETE FROM sqlite_sequence");
                foreach (var row in sequences.Rows)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "INSERT INTO sqlite_sequence(rowid,name,seq) VALUES (@rowid,@name,@seq)";
                    command.Parameters.AddWithValue("@rowid", row[0]!);
                    command.Parameters.AddWithValue("@name", row[1]!);
                    command.Parameters.AddWithValue("@seq", row[2]!);
                    command.ExecuteNonQuery();
                }
            }
            checkpoint?.Invoke("sequences");
            if (Capture(connection, transaction).Fingerprint != expected.Fingerprint)
                throw new InvalidOperationException("Precommit preservation verification failed.");
            checkpoint?.Invoke("verified");
            checkpoint?.Invoke("before-commit");
            transaction.Commit();
            return true;
        }
        finally
        {
            Execute(connection, null, "PRAGMA foreign_keys=" + foreignKeys);
            if (Scalar(connection, null, "PRAGMA foreign_keys") != foreignKeys)
                throw new InvalidOperationException("Foreign key setting was not restored.");
            checkpoint?.Invoke("foreign-keys-restored");
        }
    }

    public static void ValidateReport(Snapshot baseline, JsonElement report)
    {
        var storedSchema = report.GetProperty("schema").EnumerateArray().Select(entry => new SchemaEntry(
            entry.GetProperty("type").GetString()!, entry.GetProperty("name").GetString()!,
            entry.GetProperty("tbl_name").GetString()!, entry.GetProperty("sql").GetString())).ToArray();
        if (!baseline.Schema.SequenceEqual(storedSchema)) throw new InvalidOperationException("P4 schema mismatch.");
        var expected = Expected(baseline);
        var count = 0;
        foreach (var cell in report.GetProperty("moneyCells").EnumerateArray())
        {
            var tableName = cell.GetProperty("table").GetString()!;
            var field = cell.GetProperty("column").GetString()!;
            var metadata = report.GetProperty("tables").EnumerateArray().Single(table => table.GetProperty("table").GetString() == tableName);
            var key = metadata.GetProperty("columns").EnumerateArray().Single(column => column.GetProperty("pk").GetInt32() == 1).GetProperty("name").GetString()!;
            var table = baseline.Tables.Single(entry => entry.Name == tableName);
            var keyIndex = Array.IndexOf(table.Names, key);
            var index = Array.IndexOf(table.Names, field);
            var identity = cell.GetProperty("id").GetInt64();
            var row = table.Rows.Single(entry => Convert.ToInt64(entry[keyIndex], CultureInfo.InvariantCulture) == identity);
            if (row[index] is not double real || BitConverter.DoubleToInt64Bits(real).ToString("X16", CultureInfo.InvariantCulture)
                != cell.GetProperty("ieee754Bits").GetString()
                || ExactValue(real) != cell.GetProperty("exactDecimalCandidate").GetString())
                throw new InvalidOperationException("P4 financial candidate mismatch.");
            count++;
        }
        var cells = baseline.Tables.Where(table => Columns.ContainsKey(table.Name))
            .Sum(table => table.Rows.Length * Columns[table.Name].Length);
        if (count != 146 || cells != count || baseline.Tables.Count(table => table.Name != "sqlite_sequence") != 34
            || baseline.Tables.Where(table => table.Name != "sqlite_sequence").Sum(table => table.Rows.Length) != 384)
            throw new InvalidOperationException("P4 inventory mismatch.");
        if (expected.ForeignKeyIssues != 0) throw new InvalidOperationException("P4 foreign key mismatch.");
    }

    private static string ChangeTypes(string sql, string[] fields)
    {
        foreach (var field in fields)
        {
            var pattern = new Regex(@"(?<prefix>[(,]\s*" + Regex.Escape(Quote(field)) + @"\s+)float\b", RegexOptions.CultureInvariant);
            if (pattern.Matches(sql).Count != 1) throw new InvalidOperationException("Unsupported financial declaration.");
            sql = pattern.Replace(sql, "${prefix}TEXT");
        }
        return sql;
    }

    private static string? ExactValue(object? value) => value switch
    {
        null => null,
        double real => DecimalText.Format(DecimalText.FromExactReal(real)),
        long integer => DecimalText.Format(integer),
        string text => DecimalText.Format(DecimalText.Parse(text)),
        _ => throw new FormatException("Unsupported financial storage class.")
    };

    private static string Cell(object? value) => value switch
    {
        null => "null",
        string text => "text:" + text,
        long integer => "integer:" + integer.ToString(CultureInfo.InvariantCulture),
        double real => "real:" + BitConverter.DoubleToInt64Bits(real).ToString("X16", CultureInfo.InvariantCulture),
        byte[] bytes => "blob:" + Convert.ToHexString(bytes),
        _ => throw new InvalidOperationException("Unexpected SQLite value type.")
    };

    internal static Snapshot Capture(SqliteConnection connection, SqliteTransaction transaction)
    {
        var schema = new List<SchemaEntry>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT type,name,tbl_name,sql FROM sqlite_master ORDER BY type,name";
            using var reader = command.ExecuteReader();
            while (reader.Read()) schema.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        var tables = new List<TableData>();
        foreach (var entry in schema.Where(entry => entry.Type == "table"))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT rowid AS \"__p5_rowid\",* FROM " + Quote(entry.Name) + " ORDER BY rowid";
            using var reader = command.ExecuteReader();
            var names = Enumerable.Range(0, reader.FieldCount).Select(index => index == 0 ? "rowid" : reader.GetName(index)).ToArray();
            var rows = new List<object?[]>();
            while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
            tables.Add(new(entry.Name, names, rows.ToArray()));
        }
        var issues = 0;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "PRAGMA foreign_key_check";
            using var reader = command.ExecuteReader();
            while (reader.Read()) issues++;
        }
        return new(schema.ToArray(), tables.ToArray(), Scalar(connection, transaction, "PRAGMA integrity_check"), issues);
    }

    private static SqliteConnection Open(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 1
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    private static string Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)!;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}