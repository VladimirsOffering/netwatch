namespace NetWatch.Models;

public readonly record struct RemoteSnapshot(
    int Pid,
    string Ip,
    long InBytes,
    long OutBytes,
    long LastSeenUtcTicks);
