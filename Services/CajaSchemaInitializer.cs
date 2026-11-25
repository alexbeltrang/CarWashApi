using System.Data;
using Carwash.API.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Services;

public sealed class CajaSchemaInitializer(AppDbContext database)
{
    public async Task<IReadOnlyList<string>> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = ((SqliteConnection)database.Database.GetDbConnection())
                .BeginTransaction(IsolationLevel.Serializable, deferred: false);
            await using var registration = await database.Database.UseTransactionAsync(transaction, cancellationToken);
            foreach (var table in new[] { ("CajaDiaria", "IdCaja"), ("TurnosMovimientos", "IdTurnoMovimientos"),
                         ("GastosCaja", "IdGasto"), ("valesOperarios", "IdValeOperario") })
            {
                var columns = await ColumnsAsync(table.Item1, transaction, cancellationToken);
                if (!columns.TryGetValue(table.Item2, out var key) || key.PrimaryKey != 1)
                    throw new InvalidOperationException("CAJA_SCHEMA_AMBIGUO");
            }
            await AddColumnAsync("TurnosMovimientos", "IdCaja", "INTEGER", transaction, cancellationToken);
            await AddColumnAsync("TurnosMovimientos", "Naturaleza", "TEXT", transaction, cancellationToken);
            foreach (var table in new[] { "GastosCaja", "valesOperarios" })
            {
                await AddColumnAsync(table, "IdFormaPagoSalida", "INTEGER", transaction, cancellationToken);
                await AddColumnAsync(table, "DesembolsoConfirmado", "INTEGER", transaction, cancellationToken);
            }
            await database.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS CajaOperaciones (
                    IdOperacion TEXT NOT NULL PRIMARY KEY, Tipo TEXT NOT NULL, Objetivo TEXT NOT NULL,
                    AutorId INTEGER NOT NULL, PayloadHash TEXT NOT NULL, IdsCajaAfectadasJson TEXT NOT NULL,
                    ConfirmadaEnUtc TEXT NOT NULL, HttpStatus INTEGER NOT NULL, ResultadoJson TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS CajaCierres (
                    Id TEXT NOT NULL PRIMARY KEY, IdCaja INTEGER NOT NULL, Version INTEGER NOT NULL,
                    IdCierreAnterior TEXT NULL, Origen TEXT NOT NULL, ConfirmadoPorId INTEGER NULL,
                    Responsable TEXT NULL, ConfirmadoEnUtc TEXT NULL, RevisionFuentes INTEGER NULL,
                    ResumenToken TEXT NULL, PeriodoInicio TEXT NULL, PeriodoFin TEXT NULL, Zona TEXT NULL,
                    SnapshotVersion INTEGER NOT NULL, SnapshotJson TEXT NOT NULL,
                    EfectivoContado TEXT NULL, Diferencia TEXT NULL, JustificacionDiferencia TEXT NULL,
                    MotivoRevision TEXT NULL,
                    CONSTRAINT CK_CajaCierres_Version CHECK (Version >= 1 AND SnapshotVersion >= 1),
                    FOREIGN KEY (IdCaja) REFERENCES CajaDiaria(IdCaja) ON DELETE RESTRICT,
                    FOREIGN KEY (IdCierreAnterior) REFERENCES CajaCierres(Id) ON DELETE RESTRICT
                );
                CREATE TABLE IF NOT EXISTS CajaControl (
                    IdCaja INTEGER NOT NULL PRIMARY KEY, Revision INTEGER NOT NULL, EnRevision INTEGER NOT NULL,
                    RevisionAbiertaPor INTEGER NULL, RevisionAbiertaEnUtc TEXT NULL, MotivoRevision TEXT NULL,
                    IdCierreVigente TEXT NULL, CONSTRAINT CK_CajaControl_Revision CHECK (Revision >= 0),
                    FOREIGN KEY (IdCaja) REFERENCES CajaDiaria(IdCaja) ON DELETE RESTRICT,
                    FOREIGN KEY (IdCierreVigente) REFERENCES CajaCierres(Id) ON DELETE RESTRICT
                );
                CREATE TABLE IF NOT EXISTS CajaAuditoria (
                    Id TEXT NOT NULL PRIMARY KEY, IdOperacion TEXT NOT NULL, Accion TEXT NOT NULL,
                    AutorId INTEGER NOT NULL, FechaUtc TEXT NOT NULL, Motivo TEXT NULL,
                    IdsCajaAfectadasJson TEXT NOT NULL, IdentificadoresJson TEXT NOT NULL,
                    AntesJson TEXT NOT NULL, DespuesJson TEXT NOT NULL,
                    FOREIGN KEY (IdOperacion) REFERENCES CajaOperaciones(IdOperacion) ON DELETE RESTRICT
                );
                """, cancellationToken);
            foreach (var table in database.Model.GetRelationalModel().Tables.Where(table =>
                         table.Name is "CajaControl" or "CajaCierres" or "CajaAuditoria" or "CajaOperaciones"))
            {
                var columns = await ColumnsAsync(table.Name, transaction, cancellationToken);
                foreach (var column in table.Columns)
                    if (!columns.TryGetValue(column.Name, out var actual)
                        || !string.Equals(actual.Type, column.StoreType, StringComparison.OrdinalIgnoreCase)
                        || actual.Nullable != column.IsNullable
                        || (actual.PrimaryKey > 0) != (table.PrimaryKey?.Columns.Contains(column) == true))
                        throw new InvalidOperationException("CAJA_SCHEMA_AMBIGUO");
                await using var references = database.Database.GetDbConnection().CreateCommand();
                references.Transaction = transaction;
                references.CommandText = $"PRAGMA foreign_key_list(\"{table.Name}\")";
                var actualReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using (var reader = await references.ExecuteReaderAsync(cancellationToken))
                    while (await reader.ReadAsync(cancellationToken))
                        actualReferences.Add($"{reader.GetString(3)}:{reader.GetString(2)}:{reader.GetString(4)}:{reader.GetString(6)}");
                var expectedReferences = table.ForeignKeyConstraints.SelectMany(foreignKey => foreignKey.Columns.Select((column, index) =>
                    $"{column.Name}:{foreignKey.PrincipalTable.Name}:{foreignKey.PrincipalColumns[index].Name}:RESTRICT"));
                if (!actualReferences.SetEquals(expectedReferences)) throw new InvalidOperationException("CAJA_SCHEMA_AMBIGUO");
                references.CommandText = $"PRAGMA foreign_key_check(\"{table.Name}\")";
                await using var violations = await references.ExecuteReaderAsync(cancellationToken);
                if (await violations.ReadAsync(cancellationToken)) throw new InvalidOperationException("CAJA_SCHEMA_AMBIGUO");
            }
            await IndexAsync("CajaCierres", "IX_CajaCierres_IdCaja_Version", ["IdCaja", "Version"], true, transaction, cancellationToken);
            await IndexAsync("CajaAuditoria", "IX_CajaAuditoria_IdOperacion", ["IdOperacion"], true, transaction, cancellationToken);
            await IndexAsync("CajaAuditoria", "IX_CajaAuditoria_IdsCajaAfectadasJson_FechaUtc", ["IdsCajaAfectadasJson", "FechaUtc"], false, transaction, cancellationToken);
            await IndexAsync("TurnosMovimientos", "IX_TurnosMovimientos_IdCaja_FechaMovimiento_IdTurnoMovimientos", ["IdCaja", "FechaMovimiento", "IdTurnoMovimientos"], false, transaction, cancellationToken);
            await IndexAsync("GastosCaja", "IX_GastosCaja_idCaja", ["idCaja"], false, transaction, cancellationToken);
            await IndexAsync("valesOperarios", "IX_valesOperarios_idCaja", ["idCaja"], false, transaction, cancellationToken);
            var diagnostics = new List<string>();
            await using var count = database.Database.GetDbConnection().CreateCommand();
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM CajaDiaria WHERE Estado = 1";
            if (Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken)) > 1)
                diagnostics.Add("MULTIPLE_CAJAS_ACTIVAS");
            else
            {
                await database.Database.ExecuteSqlRawAsync(
                    "CREATE UNIQUE INDEX IF NOT EXISTS IX_CajaDiaria_Estado ON CajaDiaria(Estado) WHERE Estado = 1", cancellationToken);
                await using var definition = database.Database.GetDbConnection().CreateCommand();
                definition.Transaction = transaction;
                definition.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND name='IX_CajaDiaria_Estado'";
                var sql = Convert.ToString(await definition.ExecuteScalarAsync(cancellationToken)) ?? "";
                var normalized = sql.Replace("\"", "").Replace(" ", "").Replace("\r", "").Replace("\n", "");
                if (!normalized.Contains("UNIQUEINDEX", StringComparison.OrdinalIgnoreCase)
                    || !normalized.EndsWith("ONCajaDiaria(Estado)WHEREEstado=1", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("CAJA_SCHEMA_AMBIGUO");
            }
            await transaction.CommitAsync(cancellationToken);
            return diagnostics;
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    private async Task AddColumnAsync(string table, string name, string type, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var columns = await ColumnsAsync(table, transaction, cancellationToken);
        if (columns.TryGetValue(name, out var column))
        {
            if (!string.Equals(column.Type, type, StringComparison.OrdinalIgnoreCase) || !column.Nullable || column.PrimaryKey != 0)
                throw new InvalidOperationException("CAJA_SCHEMA_AMBIGUO");
            return;
        }
        await database.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{table}\" ADD COLUMN \"{name}\" {type} NULL", cancellationToken);
    }

    private async Task IndexAsync(string table, string name, string[] columns, bool unique, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await database.Database.ExecuteSqlRawAsync($"CREATE {(unique ? "UNIQUE " : "")}INDEX IF NOT EXISTS \"{name}\" ON \"{table}\" ({string.Join(",", columns.Select(column => $"\"{column}\""))})", cancellationToken);
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA index_info(\"{name}\")";
        var actualColumns = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) actualColumns.Add(reader.GetString(2));
        if (!actualColumns.SequenceEqual(columns, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("CAJA_SCHEMA_AMBIGUO");
        command.CommandText = $"PRAGMA index_list(\"{table}\")";
        await using var indexes = await command.ExecuteReaderAsync(cancellationToken);
        while (await indexes.ReadAsync(cancellationToken))
            if (indexes.GetString(1) == name && (indexes.GetInt32(2) != (unique ? 1 : 0) || indexes.GetInt32(4) != 0))
                throw new InvalidOperationException("CAJA_SCHEMA_AMBIGUO");
    }

    private async Task<Dictionary<string, ColumnInfo>> ColumnsAsync(string table, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
            columns.Add(reader.GetString(1), new(reader.GetString(2), reader.GetInt32(3) == 0, reader.GetInt32(5)));
        return columns;
    }

    private sealed record ColumnInfo(string Type, bool Nullable, int PrimaryKey);
}