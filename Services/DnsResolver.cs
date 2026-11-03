using System.Collections.Concurrent;
using System.Management;
using System.Net;

namespace NetWatch.Services;

public sealed class DnsResolver : IDisposable
{
    private readonly ConcurrentDictionary<string, string> _ipToName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _reverseTried = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public DnsResolver()
    {
        _loop = Task.Run(LoopAsync);
    }

    public string? GetDomain(string ip)
    {
        if (string.IsNullOrEmpty(ip))
            return null;

        if (_ipToName.TryGetValue(ip, out var name))
            return name;

        QueueReverseLookup(ip);
        return null;
    }

    private async Task LoopAsync()
    {
        RefreshCache();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
                RefreshCache();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RefreshCache()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\StandardCimv2",
                "SELECT Name, Entry, Data, Type FROM MSFT_DNSClientCache");

            foreach (ManagementBaseObject record in searcher.Get())
            {
                try
                {
                    int type = Convert.ToInt32(record["Type"]);
                    if (type != 1 && type != 28)
                        continue;

                    string? ip = (record["Data"] as string)?.Trim().TrimEnd('.');
                    string? host = (record["Entry"] as string ?? record["Name"] as string)?.Trim().TrimEnd('.');

                    if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(host))
                        continue;

                    if (!IPAddress.TryParse(ip, out _))
                        continue;

                    _ipToName[ip] = host;
                }
                catch
                {
                }
                finally
                {
                    record.Dispose();
                }
            }
        }
        catch
        {
        }
    }

    private void QueueReverseLookup(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
            return;

        if (IPAddress.IsLoopback(address))
            return;

        if (!_reverseTried.TryAdd(ip, 0))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                cts.CancelAfter(TimeSpan.FromSeconds(3));

                var entry = await Dns.GetHostEntryAsync(ip, cts.Token).ConfigureAwait(false);
                string? host = entry.HostName?.TrimEnd('.');

                if (!string.IsNullOrWhiteSpace(host) &&
                    !string.Equals(host, ip, StringComparison.OrdinalIgnoreCase))
                {
                    _ipToName.TryAdd(ip, host);
                }
            }
            catch
            {
            }
        });
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        try
        {
            _loop.Wait(1000);
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
