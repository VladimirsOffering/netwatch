using System.Net;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using NetWatch.Services;

namespace NetWatch.Etw;

public sealed class NetworkCollector : IDisposable
{
    private readonly StatsStore _store;
    private TraceEventSession? _session;
    private Task? _pump;

    public NetworkCollector(StatsStore store)
    {
        _store = store;
    }

    public void Start()
    {
        if (TraceEventSession.IsElevated() != true)
            throw new InvalidOperationException("Для сбора трафика нужны права администратора.");

        if (_pump is { IsCompleted: false })
            return;

        _session = new TraceEventSession("NetWatch-Kernel");
        _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

        var kernel = _session.Source.Kernel;
        kernel.TcpIpSend += data => Handle(data.ProcessID, data.daddr, data.size, outbound: true);
        kernel.TcpIpRecv += data => Handle(data.ProcessID, data.daddr, data.size, outbound: false);
        kernel.TcpIpSendIPV6 += data => Handle(data.ProcessID, data.daddr, data.size, outbound: true);
        kernel.TcpIpRecvIPV6 += data => Handle(data.ProcessID, data.daddr, data.size, outbound: false);
        kernel.UdpIpSend += data => Handle(data.ProcessID, data.daddr, data.size, outbound: true);
        kernel.UdpIpRecv += data => Handle(data.ProcessID, data.daddr, data.size, outbound: false);
        kernel.UdpIpSendIPV6 += data => Handle(data.ProcessID, data.daddr, data.size, outbound: true);
        kernel.UdpIpRecvIPV6 += data => Handle(data.ProcessID, data.daddr, data.size, outbound: false);

        var session = _session;
        _pump = Task.Run(() =>
        {
            try
            {
                session.Source.Process();
            }
            catch
            {
            }
        });
    }

    private void Handle(int pid, IPAddress? address, int size, bool outbound)
    {
        try
        {
            if (address is null || size <= 0)
                return;

            if (IPAddress.IsLoopback(address))
                return;

            if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                return;

            _store.Add(pid, address.ToString(), size, outbound);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        try
        {
            _session?.Stop();
        }
        catch
        {
        }

        try
        {
            _pump?.Wait(3000);
        }
        catch
        {
        }

        try
        {
            _session?.Dispose();
        }
        catch
        {
        }

        _session = null;
        _pump = null;
    }
}
