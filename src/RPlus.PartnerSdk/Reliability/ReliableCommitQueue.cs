using Newtonsoft.Json;
using RPlus.PartnerSdk.Http;
using RPlus.PartnerSdk.Models;
using RPlus.PartnerSdk.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace RPlus.PartnerSdk.Reliability;

public sealed class ReliableCommitQueue : IDisposable
{
  private readonly string _baseUrl;
  private readonly PartnerClient _client;
  private readonly ICommitQueueStore _store;
  private readonly ReliableCommitQueueOptions _options;
  private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

  private List<CommitQueueEntry> _entries = new List<CommitQueueEntry>();
  private CancellationTokenSource? _cts;
  private Task? _worker;

  public Action<string>? Log { get; set; }

  public ReliableCommitQueue(string baseUrl, PartnerClient client, ICommitQueueStore store, ReliableCommitQueueOptions? options = null)
  {
    if (string.IsNullOrWhiteSpace(baseUrl))
      throw new ArgumentException("baseUrl is required.", nameof(baseUrl));
    _baseUrl = baseUrl;
    _client = client ?? throw new ArgumentNullException(nameof(client));
    _store = store ?? throw new ArgumentNullException(nameof(store));
    _options = options ?? new ReliableCommitQueueOptions();
  }

  public async Task InitializeAsync(CancellationToken ct = default)
  {
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try
    {
      var loaded = await _store.LoadAsync(ct).ConfigureAwait(false);
      _entries = loaded.Where(e => e != null && !string.IsNullOrWhiteSpace(e.IdempotencyKey)).ToList();
    }
    finally
    {
      _gate.Release();
    }
  }

  public void Start()
  {
    if (_worker != null)
      return;
    _cts = new CancellationTokenSource();
    _worker = Task.Run(() => WorkerMainAsync(_cts.Token));
  }

  public async Task StopAsync()
  {
    if (_cts == null)
      return;
    try { _cts.Cancel(); } catch { }
    try { if (_worker != null) await _worker.ConfigureAwait(false); } catch { }
    _cts.Dispose();
    _cts = null;
    _worker = null;
  }

  public void Dispose()
  {
    try { StopAsync().GetAwaiter().GetResult(); } catch { }
    _gate.Dispose();
  }

  public Task EnqueueOrderClosedAsync(OrderClosedRequest req, string? traceId = null, CancellationToken ct = default)
  {
    if (req == null) throw new ArgumentNullException(nameof(req));
    if (req.ScanId == Guid.Empty) throw new ArgumentException("ScanId is required.", nameof(req));
    if (string.IsNullOrWhiteSpace(req.OrderId)) throw new ArgumentException("OrderId is required.", nameof(req));

    string raw = JsonUtil.Serialize(req);
    CommitQueueEntry entry = new CommitQueueEntry
    {
      Endpoint = CommitEndpoint.OrdersClosed,
      IdempotencyKey = req.ScanId.ToString(),
      TraceId = traceId,
      BodyJson = raw,
      CreatedAtUtc = DateTimeOffset.UtcNow,
      NextAttemptAtUtc = DateTimeOffset.UtcNow,
      Attempts = 0,
      OrderId = req.OrderId,
      ScanId = req.ScanId.ToString(),
    };
    return EnqueueAsync(entry, ct);
  }

  public Task EnqueueOrderCancelledAsync(OrderCancelledRequest req, string? traceId = null, CancellationToken ct = default)
  {
    if (req == null) throw new ArgumentNullException(nameof(req));
    if (req.ScanId == Guid.Empty) throw new ArgumentException("ScanId is required.", nameof(req));
    if (string.IsNullOrWhiteSpace(req.OrderId)) throw new ArgumentException("OrderId is required.", nameof(req));

    string raw = JsonUtil.Serialize(req);
    CommitQueueEntry entry = new CommitQueueEntry
    {
      Endpoint = CommitEndpoint.OrdersCancelled,
      IdempotencyKey = req.ScanId.ToString(),
      TraceId = traceId,
      BodyJson = raw,
      CreatedAtUtc = DateTimeOffset.UtcNow,
      NextAttemptAtUtc = DateTimeOffset.UtcNow,
      Attempts = 0,
      OrderId = req.OrderId,
      ScanId = req.ScanId.ToString(),
    };
    return EnqueueAsync(entry, ct);
  }

  private async Task EnqueueAsync(CommitQueueEntry entry, CancellationToken ct)
  {
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try
    {
      // Idempotent by endpoint + key.
      if (_entries.Any(e => e.Endpoint == entry.Endpoint && string.Equals(e.IdempotencyKey, entry.IdempotencyKey, StringComparison.OrdinalIgnoreCase)))
        return;

      _entries.Add(entry);
      await _store.SaveAsync(_entries, ct).ConfigureAwait(false);
    }
    finally
    {
      _gate.Release();
    }
  }

  private async Task WorkerMainAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      try
      {
        await ProcessDueAsync(ct).ConfigureAwait(false);
      }
      catch
      {
      }

      try
      {
        await Task.Delay(_options.TickInterval, ct).ConfigureAwait(false);
      }
      catch
      {
      }
    }
  }

  public async Task ProcessDueAsync(CancellationToken ct = default)
  {
    List<CommitQueueEntry> due;
    DateTimeOffset now = DateTimeOffset.UtcNow;

    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try
    {
      due = _entries.Where(e => e != null && e.NextAttemptAtUtc <= now).OrderBy(e => e.NextAttemptAtUtc).ToList();
    }
    finally
    {
      _gate.Release();
    }

    if (due.Count == 0)
      return;

    bool changed = false;
    foreach (CommitQueueEntry entry in due)
    {
      if (ct.IsCancellationRequested)
        break;

      bool ok;
      bool retry;
      string? error;
      bool scanNotFound;
      try
      {
        await SendOnceAsync(entry, ct).ConfigureAwait(false);
        ok = true;
        retry = false;
        error = null;
        scanNotFound = false;
      }
      catch (PartnerApiException ex)
      {
        ok = false;
        error = ex.ErrorCode ?? ex.Message;
        retry = ShouldRetry(ex.StatusCode, ex.ErrorCode);
        scanNotFound = string.Equals(ex.ErrorCode?.Trim(), "scan_not_found", StringComparison.OrdinalIgnoreCase);
      }
      catch (Exception ex)
      {
        ok = false;
        error = ex.GetType().Name + ": " + ex.Message;
        retry = true;
        scanNotFound = false;
      }

      await _gate.WaitAsync(ct).ConfigureAwait(false);
      try
      {
        CommitQueueEntry? live = _entries.FirstOrDefault(e => e.Endpoint == entry.Endpoint && string.Equals(e.IdempotencyKey, entry.IdempotencyKey, StringComparison.OrdinalIgnoreCase));
        if (live == null)
          continue;

        if (ok)
        {
          _entries.Remove(live);
          changed = true;
          SafeLog($"queue: OK endpoint={live.Endpoint} key={live.IdempotencyKey} orderId={live.OrderId}");
          continue;
        }

        live.Attempts += 1;
        live.LastError = error;

        // If commit fails with scan_not_found, we prefer "reconcile" instead of dropping:
        // keep the entry, try to ensure the scan exists on the server, then retry the commit.
        if (scanNotFound && live.Endpoint == CommitEndpoint.OrdersClosed)
        {
          live.NeedsReconcile = true;
          // Try reconcile soon; if reconcile isn't implemented server-side, it will just keep retrying with backoff.
          TimeSpan rdelay = TimeSpan.FromSeconds(Math.Max(30, _options.RetryPolicy.GetDelay(live.Attempts).TotalSeconds));
          live.NextReconcileAtUtc = DateTimeOffset.UtcNow.Add(rdelay);
          live.NextAttemptAtUtc = live.NextReconcileAtUtc.Value;
          changed = true;
          SafeLog($"queue: RECONCILE endpoint={live.Endpoint} key={live.IdempotencyKey} attempts={live.Attempts} in={rdelay.TotalSeconds:F0}s error={error}");
          continue;
        }

        if (!retry || live.Attempts >= _options.RetryPolicy.MaxAttempts)
        {
          // Never drop entries that are waiting for reconcile: operator already granted discount, this is money.
          if (live.NeedsReconcile)
          {
            // Keep retrying periodically; this can be caused by temporary server inconsistency.
            live.NextAttemptAtUtc = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(30));
            live.NextReconcileAtUtc = live.NextAttemptAtUtc;
            changed = true;
            SafeLog($"queue: STUCK endpoint={live.Endpoint} key={live.IdempotencyKey} attempts={live.Attempts} error={error}");
            continue;
          }

          // Drop (permanent failure). Caller can decide to keep a separate audit trail.
          _entries.Remove(live);
          changed = true;
          SafeLog($"queue: DROPPED endpoint={live.Endpoint} key={live.IdempotencyKey} attempts={live.Attempts} error={error}");
          continue;
        }

        TimeSpan delay = _options.RetryPolicy.GetDelay(live.Attempts);
        live.NextAttemptAtUtc = DateTimeOffset.UtcNow.Add(delay);
        changed = true;
        SafeLog($"queue: RETRY endpoint={live.Endpoint} key={live.IdempotencyKey} attempts={live.Attempts} in={delay.TotalSeconds:F0}s error={error}");
      }
      finally
      {
        _gate.Release();
      }
    }

    if (changed)
    {
      await _gate.WaitAsync(ct).ConfigureAwait(false);
      try
      {
        await _store.SaveAsync(_entries, ct).ConfigureAwait(false);
      }
      finally
      {
        _gate.Release();
      }
    }
  }

  private async Task SendOnceAsync(CommitQueueEntry entry, CancellationToken ct)
  {
    // If server couldn't find scan for this commit earlier, try to reconcile first.
    if (entry.NeedsReconcile && entry.Endpoint == CommitEndpoint.OrdersClosed)
    {
      try
      {
        if (Guid.TryParse(entry.IdempotencyKey, out Guid scanId))
        {
          var rr = new ReconcileRequest
          {
            Items = new[]
            {
              new ReconcileItem
              {
                ScanId = scanId,
                OrderId = entry.OrderId,
                SeenAt = entry.CreatedAtUtc,
                Terminal = null,
              }
            },
            DryRun = false,
          };

          await _client.ReconcileAsync(rr, idempotencyKey: entry.IdempotencyKey, traceId: entry.TraceId, ct: ct).ConfigureAwait(false);
        }
      }
      catch
      {
        // Let the outer loop decide retry/backoff. We deliberately do not swallow errors here.
        throw;
      }
    }

    string path = entry.Endpoint == CommitEndpoint.OrdersClosed
      ? "/api/partners/orders/closed"
      : "/api/partners/orders/cancelled";

    // Require signature for closed; allow best-effort for cancelled (depends on server policy).
    bool requireSignature = entry.Endpoint == CommitEndpoint.OrdersClosed;

    string url = CombineBaseAndPath(path);
    await _client.PostRawAsync(url, entry.BodyJson, entry.IdempotencyKey, entry.TraceId, requireSignature, ct).ConfigureAwait(false);
  }

  private string CombineBaseAndPath(string path)
  {
    // We avoid reflection: PostRawAsync accepts full URL, but we can also call PostJsonAsync.
    // ReliableCommitQueue persists raw JSON, so we need raw-post on the correct URL.
    // The recommended usage is to create ReliableCommitQueue together with the same BaseUrl used by PartnerClient.
    //
    // We derive BaseUrl by sending to a relative path via PostJsonAsync would re-serialize JSON, breaking stable signing,
    // so we do need full URL. Therefore, caller must provide a FileCommitQueueStore path and keep PartnerClient BaseUrl stable.
    //
    // Here we reconstruct it from the current request URI by parsing one known endpoint.
    // This is a no-op if path is already absolute.
    if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
      return path;

    return _baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
  }

  private static bool ShouldRetry(HttpStatusCode code, string? errorCode)
  {
    int c = (int)code;
    if (c >= 500)
      return true;
    if (c == 408 || c == 429)
      return true;

    string? codeName = errorCode?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(codeName))
      return false;

    switch (codeName)
    {
      case "scan_not_found":
      case "integration_error":
      case "unknown_service":
      case "commit_conflict":
      case "order_not_found":
      case "service_unavailable":
      case "database_busy":
        return true;
    }

    return false;
  }

  private void SafeLog(string msg)
  {
    try { Log?.Invoke(msg); } catch { }
  }
}
