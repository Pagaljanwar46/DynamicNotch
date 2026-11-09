using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DynamicNotch;

/// <summary>Polls battery / AC state and raises an event when it changes.</summary>
public class PowerService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    bool? _lastPlugged;
    bool _lowNotified;

    public bool HasBattery { get; private set; }
    public bool PluggedIn { get; private set; }
    public int Percent { get; private set; }

    /// <summary>plugged in, percent, isLowBatteryWarning</summary>
    public event Action<bool, int, bool>? Changed;

    public void Start()
    {
        Poll(initial: true);
        _timer.Tick += (_, _) => Poll(initial: false);
        _timer.Start();
    }

    void Poll(bool initial)
    {
        if (!GetSystemPowerStatus(out var s)) return;

        HasBattery = (s.BatteryFlag & 128) == 0 && s.BatteryLifePercent <= 100;
        PluggedIn = s.ACLineStatus == 1;
        Percent = HasBattery ? s.BatteryLifePercent : 100;

        if (!HasBattery) return;

        if (!initial && _lastPlugged is bool last && last != PluggedIn)
        {
            _lowNotified = false;
            Changed?.Invoke(PluggedIn, Percent, false);
        }
        _lastPlugged = PluggedIn;

        if (!PluggedIn && Percent <= 20 && !_lowNotified)
        {
            _lowNotified = true;
            Changed?.Invoke(PluggedIn, Percent, true);
        }
        if (PluggedIn || Percent > 20) _lowNotified = false;
    }

    public void Dispose() => _timer.Stop();
}
