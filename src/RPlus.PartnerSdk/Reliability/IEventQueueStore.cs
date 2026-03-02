using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RPlus.PartnerSdk.Reliability;

public interface IEventQueueStore
{
  Task<IReadOnlyList<EventQueueEntry>> LoadAsync(CancellationToken ct);
  Task SaveAsync(IReadOnlyList<EventQueueEntry> entries, CancellationToken ct);
}

