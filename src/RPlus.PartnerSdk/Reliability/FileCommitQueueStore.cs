using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RPlus.PartnerSdk.Utils;

namespace RPlus.PartnerSdk.Reliability;

public sealed class FileCommitQueueStore : ICommitQueueStore
{
  private sealed class Envelope
  {
    public int Version { get; set; } = 1;
    public List<CommitQueueEntry> Entries { get; set; } = new List<CommitQueueEntry>();
  }

  private readonly string _path;

  public FileCommitQueueStore(string path)
  {
    _path = path ?? throw new ArgumentNullException(nameof(path));
  }

  public Task<IReadOnlyList<CommitQueueEntry>> LoadAsync(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    try
    {
      if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
        return Task.FromResult((IReadOnlyList<CommitQueueEntry>)Array.Empty<CommitQueueEntry>());

      string raw = File.ReadAllText(_path, Encoding.UTF8);
      if (string.IsNullOrWhiteSpace(raw))
        return Task.FromResult((IReadOnlyList<CommitQueueEntry>)Array.Empty<CommitQueueEntry>());

      Envelope? env = JsonConvert.DeserializeObject<Envelope>(raw);
      if (env == null || env.Entries == null)
        return Task.FromResult((IReadOnlyList<CommitQueueEntry>)Array.Empty<CommitQueueEntry>());

      return Task.FromResult((IReadOnlyList<CommitQueueEntry>)env.Entries);
    }
    catch
    {
      // If corrupted, rename and continue.
      try
      {
        string bak = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        File.Move(_path, bak);
      }
      catch
      {
      }
      return Task.FromResult((IReadOnlyList<CommitQueueEntry>)Array.Empty<CommitQueueEntry>());
    }
  }

  public Task SaveAsync(IReadOnlyList<CommitQueueEntry> entries, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (string.IsNullOrWhiteSpace(_path))
      return Task.CompletedTask;

    try
    {
      string? dir = Path.GetDirectoryName(_path);
      if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        Directory.CreateDirectory(dir);

      Envelope env = new Envelope { Entries = new List<CommitQueueEntry>(entries ?? Array.Empty<CommitQueueEntry>()) };
      string raw = JsonConvert.SerializeObject(env, Formatting.None, JsonUtil.Settings);

      // Atomic-ish save: write temp then replace.
      string tmp = _path + ".tmp";
      File.WriteAllText(tmp, raw, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

      try
      {
        if (File.Exists(_path))
          File.Delete(_path);
      }
      catch
      {
      }

      File.Move(tmp, _path);
    }
    catch
    {
      // best effort
    }

    return Task.CompletedTask;
  }
}
