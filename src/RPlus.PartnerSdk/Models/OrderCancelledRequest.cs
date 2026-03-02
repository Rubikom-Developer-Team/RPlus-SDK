using System;

namespace RPlus.PartnerSdk.Models;

public sealed class OrderCancelledRequest
{
  public Guid ScanId { get; set; }
  public string OrderId { get; set; } = string.Empty;
  public string? Reason { get; set; } // deleted/storned/etc
}

