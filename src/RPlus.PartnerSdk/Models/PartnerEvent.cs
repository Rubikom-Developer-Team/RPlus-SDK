using System;
using System.Collections.Generic;

namespace RPlus.PartnerSdk.Models;

public sealed class PartnerEvent
{
  public Guid EventId { get; set; } = Guid.NewGuid();
  public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
  public string Type { get; set; } = string.Empty;

  public Guid? ScanId { get; set; }
  public string? OrderId { get; set; }
  public string? TerminalId { get; set; }

  public Dictionary<string, string>? Details { get; set; }
}

