namespace NetWatch.Helpers;

public static class SystemProcesses
{
    private static readonly HashSet<string> CoreNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "System.exe",
        "Idle",
        "Registry",
        "Memory Compression",
        "MemCompression",
        "smss.exe",
        "csrss.exe",
        "wininit.exe",
        "winlogon.exe",
        "services.exe",
        "lsass.exe",
        "svchost.exe",
        "spoolsv.exe",
        "dwm.exe",
        "fontdrvhost.exe"
    };

    public static bool IsSystem(string processName, int pid)
        => pid is 0 or 4 || CoreNames.Contains(processName);
}
