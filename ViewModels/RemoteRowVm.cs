using System.Windows.Input;
using NetWatch.Helpers;

namespace NetWatch.ViewModels;

public sealed class RemoteRowVm : ObservableObject
{
    private static readonly long ActiveWindowTicks = TimeSpan.TicksPerSecond * 2;

    private string? _domain;
    private long _inBytes;
    private long _outBytes;
    private long _lastSeenTicks;
    private bool _isActive;

    public RemoteRowVm(int pid, string ip, string processName, string? domain, ICommand openLocationCommand)
    {
        Pid = pid;
        Ip = ip;
        ProcessName = processName;
        IsSystemProcess = SystemProcesses.IsSystem(processName, pid);
        OpenLocationCommand = openLocationCommand;
        _domain = domain;
    }

    public int Pid { get; }

    public string Ip { get; }

    public string ProcessName { get; }

    public string ProcessLabel => $"{ProcessName}  (PID {Pid})";

    public bool IsSystemProcess { get; }

    public ICommand OpenLocationCommand { get; }

    public string? Domain
    {
        get => _domain;
        private set
        {
            if (Set(ref _domain, value))
                OnPropertyChanged(nameof(AddressText));
        }
    }

    public string AddressText => string.IsNullOrEmpty(_domain) ? Ip : _domain!;

    public long InBytes
    {
        get => _inBytes;
        private set
        {
            if (Set(ref _inBytes, value))
            {
                OnPropertyChanged(nameof(InText));
                OnPropertyChanged(nameof(TotalBytes));
                OnPropertyChanged(nameof(TotalText));
            }
        }
    }

    public long OutBytes
    {
        get => _outBytes;
        private set
        {
            if (Set(ref _outBytes, value))
            {
                OnPropertyChanged(nameof(OutText));
                OnPropertyChanged(nameof(TotalBytes));
                OnPropertyChanged(nameof(TotalText));
            }
        }
    }

    public long TotalBytes => _inBytes + _outBytes;

    public long LastSeenTicks
    {
        get => _lastSeenTicks;
        private set
        {
            if (Set(ref _lastSeenTicks, value))
                OnPropertyChanged(nameof(LastSeenText));
        }
    }

    public bool IsActive
    {
        get => _isActive;
        private set => Set(ref _isActive, value);
    }

    public string InText => ByteFormatter.Format(_inBytes);

    public string OutText => ByteFormatter.Format(_outBytes);

    public string TotalText => ByteFormatter.Format(TotalBytes);

    public string LastSeenText => _lastSeenTicks == 0
        ? "—"
        : new DateTime(_lastSeenTicks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss");

    public void Update(long inBytes, long outBytes, long lastSeenTicks, string? domain)
    {
        InBytes = inBytes;
        OutBytes = outBytes;
        LastSeenTicks = lastSeenTicks;

        IsActive = lastSeenTicks != 0 && DateTime.UtcNow.Ticks - lastSeenTicks <= ActiveWindowTicks;

        if (!string.IsNullOrEmpty(domain))
            Domain = domain;
    }
}
