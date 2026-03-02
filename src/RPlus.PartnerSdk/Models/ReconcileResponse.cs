using System;

namespace RPlus.PartnerSdk.Models;

public sealed class ReconcileResponse
{
  // Optional detailed results; server may omit this and still return 200 OK.
  public ReconcileResultItem[]? Items { get; set; }

  // Raw response JSON for logging/debugging.
  public string? RawJson { get; set; }
}

public sealed class ReconcileResultItem
{
  public Guid ScanId { get; set; }
  public string? Status { get; set; } // e.g. "found", "created", "unknown"
  public string? Error { get; set; }
}

