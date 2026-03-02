using System;

namespace RPlus.PartnerSdk.Reliability;

public sealed class EventQueueEntry
{
  public string IdempotencyKey { get; set; } = string.Empty; // recommended: eventId
  public string? TraceId { get; set; }
  public string BodyJson { get; set; } = string.Empty;

  public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
  public DateTimeOffset NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
  public int Attempts { get; set; }
  public string? LastError { get; set; }
}

