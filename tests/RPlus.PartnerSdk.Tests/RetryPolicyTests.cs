using RPlus.PartnerSdk.Reliability;
using System;
using Xunit;

namespace RPlus.PartnerSdk.Tests;

public sealed class RetryPolicyTests
{
  [Fact]
  public void GetDelay_ReturnsMinDelayForZeroAttempts()
  {
    var policy = new RetryPolicy
    {
      MinDelay = TimeSpan.FromSeconds(2),
      MaxDelay = TimeSpan.FromMinutes(10)
    };

    TimeSpan delay = policy.GetDelay(0);

    Assert.Equal(TimeSpan.FromSeconds(2), delay);
  }

  [Fact]
  public void GetDelay_IsCappedByMaxDelay()
  {
    var policy = new RetryPolicy
    {
      MinDelay = TimeSpan.FromSeconds(2),
      MaxDelay = TimeSpan.FromSeconds(30)
    };

    TimeSpan delay = policy.GetDelay(100);

    Assert.Equal(TimeSpan.FromSeconds(30), delay);
  }
}
