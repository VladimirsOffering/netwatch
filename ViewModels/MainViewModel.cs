using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Diagnostics.Tracing.Session;
using NetWatch.Etw;
using NetWatch.Helpers;
using NetWatch.Services;

namespace NetWatch.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly StatsStore _store = new();
    private readonly ProcessResolver _processes = new();
    private readonly DnsResolver _dns = new();
    private readonly Dictionary<(int Pid, string Ip), RemoteRowVm> _rows = new();
    private readonly RelayCommand<RemoteRowVm> _openLocationCommand;
    private readonly DispatcherTimer _timer;
    private NetworkCollector? _collector;

    private string _filter = string.Empty;
    private string _status = string.Empty;
    private bool _isRunning;
    private bool _isPaused;
    private int _processCount;

    public MainViewModel()
    {
        var view = new ListCollectionView(Rows)
        {
            Filter = FilterRow
        };
        view.SortDescriptions.Add(new SortDescription(nameof(RemoteRowVm.LastSeenTicks), ListSortDirection.Descending));
        view.LiveSortingProperties.Add(nameof(RemoteRowVm.LastSeenTicks));
        view.IsLiveSorting = true;
        View = view;

        StartCommand = new RelayCommand(Start, () => !IsRunning);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        PauseCommand = new RelayCommand(TogglePause, () => IsRunning);
        ResetCommand = new RelayCommand(Reset);
        _openLocationCommand = new RelayCommand<RemoteRowVm>(OpenLocation);

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Status = IsElevated
            ? "Готово. Нажмите «Старт»."
            : "Нет прав администратора. Нажмите «Старт», чтобы перезапустить программу с правами.";
    }

    public ObservableCollection<RemoteRowVm> Rows { get; } = new();

    public ICollectionView View { get; }

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand PauseCommand { get; }

    public RelayCommand ResetCommand { get; }

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
                View.Refresh();
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string TotalInText => ByteFormatter.Format(_store.TotalIn);

    public string TotalOutText => ByteFormatter.Format(_store.TotalOut);

    public int ProcessCount
    {
        get => _processCount;
        private set => Set(ref _processCount, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
                PauseCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsCollecting));
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (Set(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
                OnPropertyChanged(nameof(IsCollecting));
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    public string PauseButtonText => IsPaused ? "Продолжить" : "Пауза";

    public bool IsCollecting => IsRunning && !IsPaused;

    public string StateText => IsPaused ? "Пауза" : IsRunning ? "Сбор идёт" : "Остановлено";

    public static bool IsElevated => TraceEventSession.IsElevated() == true;

    public void Start()
    {
        if (IsRunning)
            return;

        if (!IsElevated)
        {
            if (TryRelaunchElevated())
                return;

            Status = "Запуск без прав администратора невозможен.";
            return;
        }

        try
        {
            _collector = new NetworkCollector(_store);
            _collector.Start();
            IsRunning = true;
            IsPaused = false;
            Status = "Сбор данных…";
        }
        catch (Exception ex)
        {
            _collector?.Dispose();
            _collector = null;
            IsRunning = false;
            Status = "Ошибка запуска: " + ex.Message;
        }
    }

    public void Stop()
    {
        _collector?.Dispose();
        _collector = null;
        IsRunning = false;
        IsPaused = false;
        Status = "Остановлено.";
    }

    public void TogglePause()
    {
        if (!IsRunning)
            return;

        IsPaused = !IsPaused;
        Status = IsPaused ? "Пауза. Сбор продолжается в фоне." : "Сбор данных…";
    }

    public void Reset()
    {
        _store.Clear();
        Rows.Clear();
        _rows.Clear();
        ProcessCount = 0;
        OnPropertyChanged(nameof(TotalInText));
        OnPropertyChanged(nameof(TotalOutText));
        Status = "Статистика сброшена.";
    }

    private void Refresh()
    {
        if (IsPaused)
            return;

        try
        {
            foreach (var snapshot in _store.Snapshot())
            {
                string? domain = _dns.GetDomain(snapshot.Ip);

                if (_rows.TryGetValue((snapshot.Pid, snapshot.Ip), out var row))
                {
                    row.Update(snapshot.InBytes, snapshot.OutBytes, snapshot.LastSeenUtcTicks, domain);
                }
                else
                {
                    row = new RemoteRowVm(
                        snapshot.Pid,
                        snapshot.Ip,
                        _processes.GetName(snapshot.Pid),
                        domain,
                        _openLocationCommand);

                    row.Update(snapshot.InBytes, snapshot.OutBytes, snapshot.LastSeenUtcTicks, domain);
                    _rows[(snapshot.Pid, snapshot.Ip)] = row;
                    Rows.Add(row);
                }
            }

            OnPropertyChanged(nameof(TotalInText));
            OnPropertyChanged(nameof(TotalOutText));
            ProcessCount = _rows.Keys.Select(key => key.Pid).Distinct().Count();
        }
        catch (Exception ex)
        {
            Status = "Ошибка обновления: " + ex.Message;
        }
    }

    private void OpenLocation(RemoteRowVm row)
    {
        string? path = _processes.GetExecutablePath(row.Pid);

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Status = $"Не удалось определить расположение процесса «{row.ProcessName}».";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
            Status = "Открыто расположение: " + path;
        }
        catch (Exception ex)
        {
            Status = "Не удалось открыть расположение: " + ex.Message;
        }
    }

    private bool FilterRow(object item)
    {
        if (string.IsNullOrWhiteSpace(_filter))
            return true;

        if (item is not RemoteRowVm row)
            return false;

        string filter = _filter.Trim();

        return row.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Ip.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.AddressText.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryRelaunchElevated()
    {
        try
        {
            string? exePath = Environment.ProcessPath;

            if (string.IsNullOrEmpty(exePath) ||
                string.Equals(Path.GetFileNameWithoutExtension(exePath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(AppContext.BaseDirectory, "NetWatch.exe");
                if (File.Exists(candidate))
                    exePath = candidate;
            }

            if (string.IsNullOrEmpty(exePath))
                return false;

            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });

            Application.Current.Shutdown();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _collector?.Dispose();
        _collector = null;
        _dns.Dispose();
    }
}
