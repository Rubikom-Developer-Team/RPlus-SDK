using System;

namespace RPlus.PartnerSdk.Reliability;

public sealed class RetryPolicy
{
  public int MaxAttempts { get; set; } = 200; // long-lived terminals can keep retrying for hours/days
  public TimeSpan MinDelay { get; set; } = TimeSpan.FromSeconds(2);
  public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(10);

  // Exponential backoff with cap.
  public TimeSpan GetDelay(int attempts)
  {
    if (attempts <= 0)
      return MinDelay;

    double factor = Math.Pow(2.0, Math.Min(10, attempts)); // cap exponent
    double sec = Math.Max(MinDelay.TotalSeconds, Math.Min(MaxDelay.TotalSeconds, MinDelay.TotalSeconds * factor));
    return TimeSpan.FromSeconds(sec);
  }
}

