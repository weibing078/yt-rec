using System.Runtime.InteropServices;

namespace YtRec.Capture;

/// <summary>Keeps the machine (and display) awake for the duration of a recording — the Windows
/// equivalent of mac's beginActivity. Dispose to release. SetThreadExecutionState is per-thread-ish
/// (the request persists until cleared with ES_CONTINUOUS alone), so keep the instance alive for the
/// whole session.</summary>
public sealed class SleepPrevention : IDisposable
{
    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    private bool _released;

    /// <summary>Begin keeping the system + display awake. <paramref name="keepDisplayOn"/> also blocks the
    /// screen from dimming/locking (the capture target must stay rendered — a locked screen freezes WGC).</summary>
    public SleepPrevention(bool keepDisplayOn = true)
    {
        var flags = ExecutionState.Continuous | ExecutionState.SystemRequired;
        if (keepDisplayOn) flags |= ExecutionState.DisplayRequired;
        SetThreadExecutionState(flags);
    }

    public void Dispose()
    {
        if (_released) return;
        _released = true;
        SetThreadExecutionState(ExecutionState.Continuous); // clear the request
    }
}
