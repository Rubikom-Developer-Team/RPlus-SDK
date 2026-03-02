using Microsoft.Data.Sqlite;
using RPlus.PartnerSdk.Reliability;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace RPlus.PartnerSdk.Sqlite;

public sealed class SqliteEventQueueStore : IEventQueueStore
{
  private readonly string _connectionString;

  public SqliteEventQueueStore(string dbPath)
  {
    if (string.IsNullOrWhiteSpace(dbPath))
      throw new ArgumentException("dbPath is required.", nameof(dbPath));
    _connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = dbPath,
      Mode = SqliteOpenMode.ReadWriteCreate,
      Cache = SqliteCacheMode.Shared,
    }.ToString();

    try { SQLitePCL.Batteries_V2.Init(); } catch { }
  }

  public Task<IReadOnlyList<EventQueueEntry>> LoadAsync(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    EnsureSchema();

    List<EventQueueEntry> result = new List<EventQueueEntry>();
    using (var conn = new SqliteConnection(_connectionString))
    {
      conn.Open();
      using (var cmd = conn.CreateCommand())
      {
        cmd.CommandText =
          "SELECT idempotencyKey, traceId, bodyJson, createdAtUtc, nextAttemptAtUtc, attempts, lastError " +
          "FROM event_queue ORDER BY nextAttemptAtUtc ASC;";

        using (var r = cmd.ExecuteReader())
        {
          while (r.Read())
          {
            EventQueueEntry e = new EventQueueEntry();
            e.IdempotencyKey = r.GetString(0);
            e.TraceId = r.IsDBNull(1) ? null : r.GetString(1);
            e.BodyJson = r.IsDBNull(2) ? string.Empty : r.GetString(2);
            e.CreatedAtUtc = ParseDto(r.IsDBNull(3) ? null : r.GetString(3));
            e.NextAttemptAtUtc = ParseDto(r.IsDBNull(4) ? null : r.GetString(4));
            e.Attempts = r.IsDBNull(5) ? 0 : r.GetInt32(5);
            e.LastError = r.IsDBNull(6) ? null : r.GetString(6);
            result.Add(e);
          }
        }
      }
    }

    return Task.FromResult((IReadOnlyList<EventQueueEntry>)result);
  }

  public Task SaveAsync(IReadOnlyList<EventQueueEntry> entries, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    EnsureSchema();

    using (var conn = new SqliteConnection(_connectionString))
    {
      conn.Open();
      using (var tx = conn.BeginTransaction())
      {
        using (var del = conn.CreateCommand())
        {
          del.Transaction = tx;
          del.CommandText = "DELETE FROM event_queue;";
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
                "INSERT INTO event_queue(idempotencyKey,traceId,bodyJson,createdAtUtc,nextAttemptAtUtc,attempts,lastError) " +
                "VALUES($key,$trace,$body,$created,$next,$attempts,$err);";

              ins.Parameters.AddWithValue("$key", e.IdempotencyKey);
              ins.Parameters.AddWithValue("$trace", (object?)e.TraceId ?? DBNull.Value);
              ins.Parameters.AddWithValue("$body", (object?)e.BodyJson ?? string.Empty);
              ins.Parameters.AddWithValue("$created", e.CreatedAtUtc.ToString("O"));
              ins.Parameters.AddWithValue("$next", e.NextAttemptAtUtc.ToString("O"));
              ins.Parameters.AddWithValue("$attempts", e.Attempts);
              ins.Parameters.AddWithValue("$err", (object?)e.LastError ?? DBNull.Value);
              ins.ExecuteNonQuery();
            }
          }
        }

        tx.Commit();
      }
    }

    return Task.CompletedTask;
  }

  private void EnsureSchema()
  {
    using (var conn = new SqliteConnection(_connectionString))
    {
      conn.Open();
      using (var cmd = conn.CreateCommand())
      {
        cmd.CommandText =
          "CREATE TABLE IF NOT EXISTS event_queue(" +
          "idempotencyKey TEXT NOT NULL PRIMARY KEY," +
          "traceId TEXT NULL," +
          "bodyJson TEXT NOT NULL," +
          "createdAtUtc TEXT NOT NULL," +
          "nextAttemptAtUtc TEXT NOT NULL," +
          "attempts INTEGER NOT NULL," +
          "lastError TEXT NULL" +
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

