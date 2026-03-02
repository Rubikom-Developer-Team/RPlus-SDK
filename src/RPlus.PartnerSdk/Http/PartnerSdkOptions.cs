using System;

namespace RPlus.PartnerSdk.Http;

public sealed class PartnerSdkOptions
{
  // Example: https://api.example.com (no trailing slash)
  public string BaseUrl { get; set; } = string.Empty;

  // X-Integration-Key
  public string IntegrationKey { get; set; } = string.Empty;

  // HMAC secret for signing /orders/closed and /orders/cancelled
  public string? SigningSecret { get; set; }

  public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

  // Optional for observability.
  public string? UserAgent { get; set; }
}

