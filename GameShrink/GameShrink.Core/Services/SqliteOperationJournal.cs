using GameShrink.Core.Abstractions;
using GameShrink.Core.Models;
using Microsoft.Data.Sqlite;

namespace GameShrink.Core.Services;

public sealed class SqliteOperationJournal : IOperationJournal
{
    private readonly string _dbPath;

    public SqliteOperationJournal(string dbPath)
    {
        _dbPath = dbPath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        await using var con = new SqliteConnection($"Data Source={_dbPath}");
        await con.OpenAsync(cancellationToken).ConfigureAwait(false);

        var cmd = con.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS operations (
  id TEXT PRIMARY KEY,
  path TEXT NOT NULL,
  mode INTEGER NOT NULL,
  algorithm INTEGER NOT NULL,
  startedAt TEXT NOT NULL,
  finishedAt TEXT NULL,
  beforeBytes INTEGER NOT NULL,
  afterBytes INTEGER NOT NULL,
  status INTEGER NOT NULL,
  errorMessage TEXT NULL,
  isRollback INTEGER NOT NULL,
  originalOperationId TEXT NULL
);
CREATE INDEX IF NOT EXISTS idx_operations_startedAt ON operations(startedAt);
";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(OperationRecord record, CancellationToken cancellationToken)
    {
        await using var con = new SqliteConnection($"Data Source={_dbPath}");
        await con.OpenAsync(cancellationToken).ConfigureAwait(false);

        var cmd = con.CreateCommand();
        cmd.CommandText = @"
INSERT INTO operations(
  id, path, mode, algorithm, startedAt, finishedAt, beforeBytes, afterBytes, status, errorMessage, isRollback, originalOperationId
) VALUES (
  $id, $path, $mode, $algorithm, $startedAt, $finishedAt, $beforeBytes, $afterBytes, $status, $errorMessage, $isRollback, $originalOperationId
);
";
        Bind(cmd, record);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(OperationRecord record, CancellationToken cancellationToken)
    {
        await using var con = new SqliteConnection($"Data Source={_dbPath}");
        await con.OpenAsync(cancellationToken).ConfigureAwait(false);

        var cmd = con.CreateCommand();
        cmd.CommandText = @"
UPDATE operations SET
  path=$path,
  mode=$mode,
  algorithm=$algorithm,
  startedAt=$startedAt,
  finishedAt=$finishedAt,
  beforeBytes=$beforeBytes,
  afterBytes=$afterBytes,
  status=$status,
  errorMessage=$errorMessage,
  isRollback=$isRollback,
  originalOperationId=$originalOperationId
WHERE id=$id;
";
        Bind(cmd, record);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OperationRecord>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        await using var con = new SqliteConnection($"Data Source={_dbPath}");
        await con.OpenAsync(cancellationToken).ConfigureAwait(false);

        var cmd = con.CreateCommand();
        cmd.CommandText = @"
SELECT id, path, mode, algorithm, startedAt, finishedAt, beforeBytes, afterBytes, status, errorMessage, isRollback, originalOperationId
FROM operations
ORDER BY startedAt DESC
LIMIT $take;
";
        cmd.Parameters.AddWithValue("$take", take);

        var list = new List<OperationRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(Read(reader));
        }
        return list;
    }

    public async Task<OperationRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var con = new SqliteConnection($"Data Source={_dbPath}");
        await con.OpenAsync(cancellationToken).ConfigureAwait(false);

        var cmd = con.CreateCommand();
        cmd.CommandText = @"
SELECT id, path, mode, algorithm, startedAt, finishedAt, beforeBytes, afterBytes, status, errorMessage, isRollback, originalOperationId
FROM operations
WHERE id=$id;
";
        cmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return Read(reader);
    }

    private static void Bind(SqliteCommand cmd, OperationRecord r)
    {
        cmd.Parameters.Clear();

        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$path", string.IsNullOrWhiteSpace(r.Path) ? "" : r.Path);
        cmd.Parameters.AddWithValue("$mode", (int)r.Mode);
        cmd.Parameters.AddWithValue("$algorithm", (int)r.Algorithm);
        cmd.Parameters.AddWithValue("$startedAt", r.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$finishedAt", r.FinishedAt is null ? DBNull.Value : r.FinishedAt.Value.ToString("O"));
        cmd.Parameters.AddWithValue("$beforeBytes", r.BeforeBytes);
        cmd.Parameters.AddWithValue("$afterBytes", r.AfterBytes);
        cmd.Parameters.AddWithValue("$status", (int)r.Status);
        cmd.Parameters.AddWithValue("$errorMessage", string.IsNullOrWhiteSpace(r.ErrorMessage) ? DBNull.Value : r.ErrorMessage);
        cmd.Parameters.AddWithValue("$isRollback", r.IsRollback ? 1 : 0);
        cmd.Parameters.AddWithValue("$originalOperationId", r.OriginalOperationId is null ? DBNull.Value : r.OriginalOperationId.ToString());
    }

    private static OperationRecord Read(SqliteDataReader reader)
    {
        return new OperationRecord
        {
            Id = Guid.Parse(reader.GetString(0)),
            Path = reader.GetString(1),
            Mode = (CompressionMode)reader.GetInt32(2),
            Algorithm = (CompressionAlgorithm)reader.GetInt32(3),
            StartedAt = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            FinishedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
            BeforeBytes = reader.GetInt64(6),
            AfterBytes = reader.GetInt64(7),
            Status = (OperationStatus)reader.GetInt32(8),
            ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
            IsRollback = reader.GetInt32(10) != 0,
            OriginalOperationId = reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11))
        };
    }
}
