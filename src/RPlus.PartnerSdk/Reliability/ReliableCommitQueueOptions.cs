using System;

namespace RPlus.PartnerSdk.Reliability;

public sealed class ReliableCommitQueueOptions
{
  // How often the background worker checks the queue.
  public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(2);

  public RetryPolicy RetryPolicy { get; set; } = new RetryPolicy();
}

