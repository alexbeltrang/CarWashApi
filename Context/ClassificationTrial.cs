using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using static Carwash.API.Context.DecimalTextStorageTrial;

namespace Carwash.API.Context;

public static class ClassificationTrial
{
    public const string SourceHash = "517657EFE7601BC60B7363C8189E269170E4C5E1DC768AB0E512E63EF8DDE743";
    public const string SourceFingerprint = "D93EE009E1C1506389112843E24FAB2378A22D0DCFA574A4F196D37D234B7675";
    public sealed record Movement(int Id, int Turn, int Medium, string Amount)
    {
        public bool Credit => Id is 388 or 391;
    }

    public static IReadOnlyList<Movement> Manifest { get; } = Array.AsReadOnly(new Movement[]
    {
        new(382,389,2,"30000"), new(383,387,2,"60000"), new(384,386,1,"35000"), new(385,388,1,"90000"),
        new(386,393,1,"20000"), new(387,391,1,"30000"), new(388,390,4,"30000"), new(389,392,1,"35000"),
        new(390,394,1,"30000"), new(391,396,4,"35000"), new(392,397,1,"50000"), new(393,400,1,"35000"),
        new(394,399,1,"70000"), new(395,401,1,"30000"), new(396,403,1,"35000"), new(397,407,1,"40000"),
        new(398,408,2,"30000"), new(399,404,1,"30000"), new(400,406,1,"35000"), new(401,405,2,"35000"),
        new(402,410,1,"35000"), new(403,409,2,"80000"), new(404,411,2,"25000"), new(405,398,1,"50000"),
        new(406,398,2,"20000"), new(407,395,2,"70000"), new(408,412,2,"80000"), new(409,413,1,"35000")
    });

    public static string Guard(string root, string path, bool writable, bool create = false)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        path = Path.GetFullPath(path);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(root), temp, StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(Path.GetFileName(root), @"\ACarWash\.DecimalText\.P6-[0-9a-f]{32}\z")
            || !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path is outside the authorized P6 TEMP root.");
        var relative = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        var roles = writable ? new[] { "trial", "faults", "fixtures" } : new[] { "baseline", "trial", "faults", "fixtures" };
        if (relative.Length < 2 || !roles.Contains(relative[0], StringComparer.Ordinal))
            throw new InvalidOperationException("Unauthorized P6 path role.");
        Standalone(path);
        if (create ? File.Exists(path) || Directory.Exists(path) : !File.Exists(path))
            throw new InvalidOperationException("P6 destination/source existence precondition failed.");
        return path;
    }

    private static void Standalone(string path)
    {
        for (var entry = new FileInfo(path) as FileSystemInfo; entry != null; entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            if ((File.Exists(entry.FullName) || Directory.Exists(entry.FullName)) && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("P6 rejects reparse paths.");
        if (new[] { "-wal", "-shm", "-journal" }.Any(suffix => File.Exists(path + suffix)))
            throw new InvalidOperationException("P6 requires a standalone closed file.");
    }

    public static string Hash(string root, string path) => HashFile(Guard(root, path, false));

    private static string HashFile(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static void CopyApprovedSource(string root)
    {
        var source = Path.Combine(Path.GetTempPath(), "CarWash.DecimalText.P5-26c93fc7a4894cd199daefa3e29cff15", "trial", "CarWash.db");
        var destinations = new[] { Path.Combine(root, "baseline", "CarWash.db"), Path.Combine(root, "trial", "CarWash.db") };
        foreach (var destination in destinations) Guard(root, destination, destination == destinations[1], true);
        Standalone(source);
        using var stream = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Convert.ToHexString(SHA256.HashData(stream)) != SourceHash)
            throw new InvalidOperationException("Approved P5 source file hash differs.");
        foreach (var destination in destinations)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Guard(root, destination, destination == destinations[1], true);
            stream.Position = 0;
            using (var output = File.Open(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.CopyTo(output);
            if (Hash(root, destination) != SourceHash) throw new InvalidOperationException("P6 copy hash mismatch.");
        }
        stream.Position = 0;
        if (Convert.ToHexString(SHA256.HashData(stream)) != SourceHash)
            throw new InvalidOperationException("P5 source changed while copying.");
        Standalone(source);
    }

    public static string VerifyApprovedSourceFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "CarWash.DecimalText.P5-26c93fc7a4894cd199daefa3e29cff15", "trial", "CarWash.db");
        Standalone(path);
        var hash = HashFile(path);
        if (hash != SourceHash) throw new InvalidOperationException("P5 source is not unchanged.");
        return hash;
    }

    public static SqliteConnection OpenReadOnly(string root, string path)
    {
        var connection = Open(Guard(root, path, false), true);
        Execute(connection, null, "PRAGMA query_only=ON");
        return connection;
    }

    public static Snapshot Read(string root, string path)
    {
        using var connection = OpenReadOnly(root, path);
        using var transaction = connection.BeginTransaction(deferred: true);
        return Capture(connection, transaction);
    }

    public static Snapshot Expected(Snapshot baseline)
    {
        if (baseline.Integrity != "ok" || baseline.ForeignKeyIssues != 0
            || baseline.Schema.Any(entry => entry.Type is "view" or "trigger"))
            throw new InvalidOperationException("Unsupported P6 baseline schema/integrity.");
        var tables = baseline.Tables.Select(table => table with { Rows = table.Rows.Select(row => row.ToArray()).ToArray() }).ToArray();
        TableData Table(string name) => tables.Single(table => table.Name == name);
        static int Index(TableData table, string name)
        {
            var index = Array.IndexOf(table.Names, name);
            return index >= 0 ? index : throw new InvalidOperationException("Required P6 column is missing.");
        }
        static object?[] Row(TableData table, string key, long id)
            => table.Rows.Single(row => row[Index(table, key)] is long number && number == id);
        static void Require(TableData table, object?[] row, string field, object? value)
        {
            if (!Equals(row[Index(table, field)], value)) throw new InvalidOperationException("P6 manifest precondition differs.");
        }
        var media = Table("FormaPago");
        foreach (var (id, name) in new[] { (1L, "EFECTIVO"), (2L, "TRANSFERENCIA"), (4L, "CREDITO") })
        {
            var matches = media.Rows.Where(row => Category(row[Index(media, "Nombre")] as string) == name).ToArray();
            if (matches.Length != 1 || !Equals(matches[0][Index(media, "IdFormaPago")], id))
                throw new InvalidOperationException("P6 catalogue identity/category is ambiguous.");
        }
        var boxes = Table("CajaDiaria");
        var box = Row(boxes, "idCaja", 31);
        Require(boxes, box, "Estado", 0L);
        Require(boxes, box, "FechaApertura", "2026-11-21 10:07:03.938");
        Require(boxes, box, "FechaCierre", "2026-11-21 19:11:13.272");
        var movements = Table("TurnosMovimientos");
        if (movements.Rows.Length != Manifest.Count) throw new InvalidOperationException("Unexpected movement IDs/count.");
        foreach (var item in Manifest)
        {
            var row = Row(movements, "IdTurnoMovimientos", item.Id);
            Require(movements, row, "IdTurno", (long)item.Turn);
            Require(movements, row, "IdFormaPago", (long)item.Medium);
            Require(movements, row, "MontoPagado", item.Amount);
            Require(movements, row, "IdCaja", null);
            Require(movements, row, "Naturaleza", null);
            row[Index(movements, "Naturaleza")] = item.Credit ? "CREDITO" : "COBRO";
            if (!item.Credit) row[Index(movements, "IdCaja")] = 31L;
        }
        var expenses = Table("GastosCaja");
        if (expenses.Rows.Length != 2) throw new InvalidOperationException("Unexpected expense IDs/count.");
        foreach (var (id, amount) in new[] { (25L, "35000"), (26L, "8450") })
        {
            var row = Row(expenses, "IdGasto", id);
            Require(expenses, row, "idCaja", 31L);
            Require(expenses, row, "Valor", amount);
            Require(expenses, row, "IdFormaPagoSalida", null);
            Require(expenses, row, "DesembolsoConfirmado", null);
            row[Index(expenses, "IdFormaPagoSalida")] = 1L;
            row[Index(expenses, "DesembolsoConfirmado")] = 1L;
        }
        return baseline with { Tables = tables };
    }

    private static string Category(string? name) => new string((name ?? "").Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray()).Trim().ToUpperInvariant();

    public static bool Run(string root, string path, Snapshot baseline, Action<string>? checkpoint = null)
    {
        path = Guard(root, path, true);
        var expected = Expected(baseline);
        using var connection = Open(path, false);
        using var transaction = connection.BeginTransaction(deferred: false);
        var current = Capture(connection, transaction);
        if (current.Fingerprint == expected.Fingerprint) return false;
        if (current.Fingerprint != baseline.Fingerprint) throw new InvalidOperationException("P6 source differs or is partially classified.");
        checkpoint?.Invoke("validated");
        foreach (var item in Manifest)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = item.Credit
                ? "UPDATE TurnosMovimientos SET Naturaleza='CREDITO' WHERE IdTurnoMovimientos=@id AND IdCaja IS NULL AND Naturaleza IS NULL"
                : "UPDATE TurnosMovimientos SET IdCaja=31,Naturaleza='COBRO' WHERE IdTurnoMovimientos=@id AND IdCaja IS NULL AND Naturaleza IS NULL";
            command.Parameters.AddWithValue("@id", item.Id);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("P6 conditional movement update failed.");
            checkpoint?.Invoke("movement-" + item.Id);
        }
        foreach (var id in new[] { 25, 26 })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE GastosCaja SET IdFormaPagoSalida=1,DesembolsoConfirmado=1 WHERE IdGasto=@id AND idCaja=31 AND IdFormaPagoSalida IS NULL AND DesembolsoConfirmado IS NULL";
            command.Parameters.AddWithValue("@id", id);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("P6 conditional expense update failed.");
            checkpoint?.Invoke("expense-" + id);
        }
        if (Capture(connection, transaction).Fingerprint != expected.Fingerprint)
            throw new InvalidOperationException("P6 precommit full preservation verification failed.");
        checkpoint?.Invoke("verified");
        checkpoint?.Invoke("before-commit");
        transaction.Commit();
        return true;
    }

    private static SqliteConnection Open(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            ForeignKeys = true, Pooling = false, DefaultTimeout = 1
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}