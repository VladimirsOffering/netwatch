using System.Collections.Concurrent;
using NetWatch.Models;

namespace NetWatch.Services;

public sealed class StatsStore
{
    private ConcurrentDictionary<(int Pid, string Ip), RemoteCounter> _map = new();
    private long _totalIn;
    private long _totalOut;

    public long TotalIn => Interlocked.Read(ref _totalIn);
    public long TotalOut => Interlocked.Read(ref _totalOut);

    public void Add(int pid, string ip, int bytes, bool outbound)
    {
        if (bytes <= 0 || string.IsNullOrEmpty(ip))
            return;

        var counter = _map.GetOrAdd((pid, ip), static _ => new RemoteCounter());

        if (outbound)
        {
            Interlocked.Add(ref counter.OutBytes, bytes);
            Interlocked.Add(ref _totalOut, bytes);
        }
        else
        {
            Interlocked.Add(ref counter.InBytes, bytes);
            Interlocked.Add(ref _totalIn, bytes);
        }

        Interlocked.Exchange(ref counter.LastSeenUtcTicks, DateTime.UtcNow.Ticks);
    }

    public List<RemoteSnapshot> Snapshot()
    {
        var result = new List<RemoteSnapshot>(_map.Count);
        foreach (var entry in _map)
        {
            var counter = entry.Value;
            result.Add(new RemoteSnapshot(
                entry.Key.Pid,
                entry.Key.Ip,
                Interlocked.Read(ref counter.InBytes),
                Interlocked.Read(ref counter.OutBytes),
                Interlocked.Read(ref counter.LastSeenUtcTicks)));
        }

        return result;
    }

    public void Clear()
    {
        Interlocked.Exchange(ref _map, new ConcurrentDictionary<(int, string), RemoteCounter>());
        Interlocked.Exchange(ref _totalIn, 0);
        Interlocked.Exchange(ref _totalOut, 0);
    }

    private sealed class RemoteCounter
    {
        public long InBytes;
        public long OutBytes;
        public long LastSeenUtcTicks;
    }
}
