using System;

namespace RPlus.PartnerSdk.Models;

public sealed class ReconcileRequest
{
  // List of scan/order pairs that POS believes should exist on the server.
  public ReconcileItem[] Items { get; set; } = Array.Empty<ReconcileItem>();

  // If true, server should validate and report but must not create/update records.
  public bool? DryRun { get; set; }
}

public sealed class ReconcileItem
{
  public Guid ScanId { get; set; }
  public string? OrderId { get; set; }
  public DateTimeOffset? SeenAt { get; set; }
  public string? Terminal { get; set; }
}

