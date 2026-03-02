using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RPlus.PartnerSdk.Utils;

namespace RPlus.PartnerSdk.Reliability;

public sealed class FileEventQueueStore : IEventQueueStore
{
  private sealed class Envelope
  {
    public int Version { get; set; } = 1;
    public List<EventQueueEntry> Entries { get; set; } = new List<EventQueueEntry>();
  }

  private readonly string _path;

  public FileEventQueueStore(string path)
  {
    _path = path ?? throw new ArgumentNullException(nameof(path));
  }

  public Task<IReadOnlyList<EventQueueEntry>> LoadAsync(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    try
    {
      if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
        return Task.FromResult((IReadOnlyList<EventQueueEntry>)Array.Empty<EventQueueEntry>());

      string raw = File.ReadAllText(_path, Encoding.UTF8);
      if (string.IsNullOrWhiteSpace(raw))
        return Task.FromResult((IReadOnlyList<EventQueueEntry>)Array.Empty<EventQueueEntry>());

      Envelope? env = JsonConvert.DeserializeObject<Envelope>(raw);
      if (env == null || env.Entries == null)
        return Task.FromResult((IReadOnlyList<EventQueueEntry>)Array.Empty<EventQueueEntry>());

      return Task.FromResult((IReadOnlyList<EventQueueEntry>)env.Entries);
    }
    catch
    {
      try
      {
        string bak = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        File.Move(_path, bak);
      }
      catch
      {
      }
      return Task.FromResult((IReadOnlyList<EventQueueEntry>)Array.Empty<EventQueueEntry>());
    }
  }

  public Task SaveAsync(IReadOnlyList<EventQueueEntry> entries, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (string.IsNullOrWhiteSpace(_path))
      return Task.CompletedTask;

    try
    {
      string? dir = Path.GetDirectoryName(_path);
      if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        Directory.CreateDirectory(dir);

      Envelope env = new Envelope { Entries = new List<EventQueueEntry>(entries ?? Array.Empty<EventQueueEntry>()) };
      string raw = JsonConvert.SerializeObject(env, Formatting.None, JsonUtil.Settings);

      string tmp = _path + ".tmp";
      File.WriteAllText(tmp, raw, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
      try { if (File.Exists(_path)) File.Delete(_path); } catch { }
      File.Move(tmp, _path);
    }
    catch
    {
    }

    return Task.CompletedTask;
  }
}
