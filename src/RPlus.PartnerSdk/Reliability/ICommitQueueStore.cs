using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RPlus.PartnerSdk.Reliability;

public interface ICommitQueueStore
{
  Task<IReadOnlyList<CommitQueueEntry>> LoadAsync(CancellationToken ct);
  Task SaveAsync(IReadOnlyList<CommitQueueEntry> entries, CancellationToken ct);
}

