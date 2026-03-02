namespace RPlus.PartnerSdk.Models;

public sealed class ScanRequest
{
  // Exactly one of QrToken or OtpCode should be set.
  public string? QrToken { get; set; }
  public string? OtpCode { get; set; }

  // Optional context for audit/anti-fraud/analytics.
  public string? OrderId { get; set; }
  public decimal? OrderSum { get; set; }
  public string? TerminalId { get; set; }
  public string? CashierId { get; set; }
  public string? Context { get; set; } // e.g. "partner"
}

