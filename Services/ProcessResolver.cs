using System.Collections.Concurrent;
using System.Diagnostics;

namespace NetWatch.Services;

public sealed class ProcessResolver
{
    private readonly ConcurrentDictionary<int, CacheEntry> _cache = new();

    public string GetName(int pid)
    {
        if (pid <= 0)
            return "System";

        try
        {
            using var process = Process.GetProcessById(pid);
            long startTicks = SafeStartTicks(process);

            if (_cache.TryGetValue(pid, out var cached) && cached.StartTicks == startTicks)
                return cached.Name;

            string name = string.IsNullOrEmpty(process.ProcessName)
                ? $"PID {pid}"
                : process.ProcessName + ".exe";

            _cache[pid] = new CacheEntry(name, startTicks);
            return name;
        }
        catch
        {
            return _cache.TryGetValue(pid, out var cached) ? cached.Name : $"PID {pid}";
        }
    }

    public string? GetExecutablePath(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static long SafeStartTicks(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch
        {
            return 0;
        }
    }

    private readonly record struct CacheEntry(string Name, long StartTicks);
}
