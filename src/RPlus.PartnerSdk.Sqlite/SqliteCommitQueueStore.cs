using Microsoft.Data.Sqlite;
using RPlus.PartnerSdk.Reliability;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace RPlus.PartnerSdk.Sqlite;

public sealed class SqliteCommitQueueStore : ICommitQueueStore
{
  private readonly string _connectionString;

  public SqliteCommitQueueStore(string dbPath)
  {
    if (string.IsNullOrWhiteSpace(dbPath))
      throw new ArgumentException("dbPath is required.", nameof(dbPath));
    _connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = dbPath,
      Mode = SqliteOpenMode.ReadWriteCreate,
      Cache = SqliteCacheMode.Shared,
    }.ToString();

    // Ensure SQLite native bits are loaded (bundle package).
    try { SQLitePCL.Batteries_V2.Init(); } catch { }
  }

  public async Task<IReadOnlyList<CommitQueueEntry>> LoadAsync(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    EnsureSchema();

    List<CommitQueueEntry> result = new List<CommitQueueEntry>();
    using (var conn = new SqliteConnection(_connectionString))
    {
      conn.Open();
      using (var cmd = conn.CreateCommand())
      {
        cmd.CommandText =
          "SELECT endpoint, idempotencyKey, traceId, bodyJson, createdAtUtc, nextAttemptAtUtc, attempts, lastError, orderId, scanId " +
          "FROM commit_queue ORDER BY nextAttemptAtUtc ASC;";

        using (var r = cmd.ExecuteReader())
        {
          while (r.Read())
          {
            CommitQueueEntry e = new CommitQueueEntry();
            e.Endpoint = (CommitEndpoint)r.GetInt32(0);
            e.IdempotencyKey = r.GetString(1);
            e.TraceId = r.IsDBNull(2) ? null : r.GetString(2);
            e.BodyJson = r.IsDBNull(3) ? string.Empty : r.GetString(3);
            e.CreatedAtUtc = ParseDto(r.IsDBNull(4) ? null : r.GetString(4));
            e.NextAttemptAtUtc = ParseDto(r.IsDBNull(5) ? null : r.GetString(5));
            e.Attempts = r.IsDBNull(6) ? 0 : r.GetInt32(6);
            e.LastError = r.IsDBNull(7) ? null : r.GetString(7);
            e.OrderId = r.IsDBNull(8) ? null : r.GetString(8);
            e.ScanId = r.IsDBNull(9) ? null : r.GetString(9);
            result.Add(e);
          }
        }
      }
    }

    return result;
  }

  public async Task SaveAsync(IReadOnlyList<CommitQueueEntry> entries, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    EnsureSchema();

    using (var conn = new SqliteConnection(_connectionString))
    {
      conn.Open();
      using (var tx = conn.BeginTransaction())
      {
        // Full replace for simplicity; queue size is small.
        using (var del = conn.CreateCommand())
        {
          del.Transaction = tx;
          del.CommandText = "DELETE FROM commit_queue;";
          del.ExecuteNonQuery();
        }

        if (entries != null)
        {
          foreach (var e in entries)
          {
            if (e == null || string.IsNullOrWhiteSpace(e.IdempotencyKey))
              continue;

            using (var ins = conn.CreateCommand())
            {
              ins.Transaction = tx;
              ins.CommandText =
                "INSERT INTO commit_queue(endpoint,idempotencyKey,traceId,bodyJson,createdAtUtc,nextAttemptAtUtc,attempts,lastError,orderId,scanId) " +
                "VALUES($endpoint,$key,$trace,$body,$created,$next,$attempts,$err,$orderId,$scanId);";

              ins.Parameters.AddWithValue("$endpoint", (int)e.Endpoint);
              ins.Parameters.AddWithValue("$key", e.IdempotencyKey);
              ins.Parameters.AddWithValue("$trace", (object?)e.TraceId ?? DBNull.Value);
              ins.Parameters.AddWithValue("$body", (object?)e.BodyJson ?? string.Empty);
              ins.Parameters.AddWithValue("$created", e.CreatedAtUtc.ToString("O"));
              ins.Parameters.AddWithValue("$next", e.NextAttemptAtUtc.ToString("O"));
              ins.Parameters.AddWithValue("$attempts", e.Attempts);
              ins.Parameters.AddWithValue("$err", (object?)e.LastError ?? DBNull.Value);
              ins.Parameters.AddWithValue("$orderId", (object?)e.OrderId ?? DBNull.Value);
              ins.Parameters.AddWithValue("$scanId", (object?)e.ScanId ?? DBNull.Value);

              ins.ExecuteNonQuery();
            }
          }
        }

        tx.Commit();
      }
    }
  }

  private void EnsureSchema()
  {
    using (var conn = new SqliteConnection(_connectionString))
    {
      conn.Open();
      using (var cmd = conn.CreateCommand())
      {
        cmd.CommandText =
          "CREATE TABLE IF NOT EXISTS commit_queue(" +
          "endpoint INTEGER NOT NULL," +
          "idempotencyKey TEXT NOT NULL," +
          "traceId TEXT NULL," +
          "bodyJson TEXT NOT NULL," +
          "createdAtUtc TEXT NOT NULL," +
          "nextAttemptAtUtc TEXT NOT NULL," +
          "attempts INTEGER NOT NULL," +
          "lastError TEXT NULL," +
          "orderId TEXT NULL," +
          "scanId TEXT NULL," +
          "PRIMARY KEY(endpoint, idempotencyKey)" +
          ");";
        cmd.ExecuteNonQuery();
      }
    }
  }

  private static DateTimeOffset ParseDto(string? s)
  {
    if (string.IsNullOrWhiteSpace(s))
      return DateTimeOffset.UtcNow;
    if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
      return dto;
    return DateTimeOffset.UtcNow;
  }
}
