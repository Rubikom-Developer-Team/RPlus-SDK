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

public sealed class ReliableEventQueue : IDisposable
{
  private readonly string _baseUrl;
  private readonly PartnerClient _client;
  private readonly IEventQueueStore _store;
  private readonly ReliableCommitQueueOptions _options;
  private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

  private List<EventQueueEntry> _entries = new List<EventQueueEntry>();
  private CancellationTokenSource? _cts;
  private Task? _worker;

  public Action<string>? Log { get; set; }

  public ReliableEventQueue(string baseUrl, PartnerClient client, IEventQueueStore store, ReliableCommitQueueOptions? options = null)
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

  public Task EnqueueAsync(PartnerEvent evt, string? traceId = null, CancellationToken ct = default)
  {
    if (evt == null) throw new ArgumentNullException(nameof(evt));
    if (evt.EventId == Guid.Empty) throw new ArgumentException("EventId is required.", nameof(evt));
    if (string.IsNullOrWhiteSpace(evt.Type)) throw new ArgumentException("Type is required.", nameof(evt));

    string raw = JsonUtil.Serialize(evt);
    EventQueueEntry entry = new EventQueueEntry
    {
      IdempotencyKey = evt.EventId.ToString(),
      TraceId = traceId,
      BodyJson = raw,
      CreatedAtUtc = DateTimeOffset.UtcNow,
      NextAttemptAtUtc = DateTimeOffset.UtcNow,
      Attempts = 0
    };
    return EnqueueInternalAsync(entry, ct);
  }

  private async Task EnqueueInternalAsync(EventQueueEntry entry, CancellationToken ct)
  {
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try
    {
      if (_entries.Any(e => string.Equals(e.IdempotencyKey, entry.IdempotencyKey, StringComparison.OrdinalIgnoreCase)))
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
      try { await ProcessDueAsync(ct).ConfigureAwait(false); } catch { }
      try { await Task.Delay(_options.TickInterval, ct).ConfigureAwait(false); } catch { }
    }
  }

  public async Task ProcessDueAsync(CancellationToken ct = default)
  {
    List<EventQueueEntry> due;
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
    foreach (EventQueueEntry entry in due)
    {
      if (ct.IsCancellationRequested)
        break;

      bool ok;
      bool retry;
      string? error;
      try
      {
        string url = _baseUrl.TrimEnd('/') + "/api/partners/events";
        await _client.PostRawAsync(url, entry.BodyJson, entry.IdempotencyKey, entry.TraceId, requireSignature: false, ct: ct).ConfigureAwait(false);
        ok = true;
        retry = false;
        error = null;
      }
      catch (PartnerApiException ex)
      {
        ok = false;
        error = ex.ErrorCode ?? ex.Message;
        retry = ShouldRetry(ex.StatusCode);
      }
      catch (Exception ex)
      {
        ok = false;
        error = ex.GetType().Name + ": " + ex.Message;
        retry = true;
      }

      await _gate.WaitAsync(ct).ConfigureAwait(false);
      try
      {
        EventQueueEntry? live = _entries.FirstOrDefault(e => string.Equals(e.IdempotencyKey, entry.IdempotencyKey, StringComparison.OrdinalIgnoreCase));
        if (live == null)
          continue;

        if (ok)
        {
          _entries.Remove(live);
          changed = true;
          SafeLog($"events: OK key={live.IdempotencyKey}");
          continue;
        }

        live.Attempts += 1;
        live.LastError = error;

        if (!retry || live.Attempts >= _options.RetryPolicy.MaxAttempts)
        {
          _entries.Remove(live);
          changed = true;
          SafeLog($"events: DROPPED key={live.IdempotencyKey} attempts={live.Attempts} error={error}");
          continue;
        }

        TimeSpan delay = _options.RetryPolicy.GetDelay(live.Attempts);
        live.NextAttemptAtUtc = DateTimeOffset.UtcNow.Add(delay);
        changed = true;
        SafeLog($"events: RETRY key={live.IdempotencyKey} attempts={live.Attempts} in={delay.TotalSeconds:F0}s error={error}");
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

  private static bool ShouldRetry(HttpStatusCode code)
  {
    int c = (int)code;
    if (c >= 500)
      return true;
    if (c == 408 || c == 429)
      return true;
    return false;
  }

  private void SafeLog(string msg)
  {
    try { Log?.Invoke(msg); } catch { }
  }
}
