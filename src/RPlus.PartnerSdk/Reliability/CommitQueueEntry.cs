using System;

namespace RPlus.PartnerSdk.Reliability;

public enum CommitEndpoint
{
  OrdersClosed = 1,
  OrdersCancelled = 2,
}

public sealed class CommitQueueEntry
{
  public CommitEndpoint Endpoint { get; set; }

  // Unique idempotency key (recommended: scanId)
  public string IdempotencyKey { get; set; } = string.Empty;

  // Optional trace id for observability.
  public string? TraceId { get; set; }

  // JSON string exactly as sent (used for stable signing across retries).
  public string BodyJson { get; set; } = string.Empty;

  public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
  public DateTimeOffset NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
  public int Attempts { get; set; }
  public string? LastError { get; set; }

  // For diagnostics only.
  public string? OrderId { get; set; }
  public string? ScanId { get; set; }

  // When server replies scan_not_found for /orders/closed, we keep the entry and try to reconcile instead of dropping.
  public bool NeedsReconcile { get; set; }
  public DateTimeOffset? NextReconcileAtUtc { get; set; }
}
