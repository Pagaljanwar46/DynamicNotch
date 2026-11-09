using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DynamicNotch;

enum NotchState { Compact, Expanded, Activity }

public record ClipItem(string Full)
{
    public string Preview => Full.Replace("\r", " ").Replace("\n", " ").Trim();
}

public partial class MainWindow : Window
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

    readonly MediaService _media = new();
    readonly PowerService _power = new();
    readonly VolumeService _volume = new();
    readonly ObservableCollection<ClipItem> _clips = new();

    readonly DispatcherTimer _activityTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    NotchState _state = NotchState.Compact;
    bool _hovering;
    string? _lastTitle;
    DateTime _startedAt = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();
        ClipList.ItemsSource = _clips;

        _activityTimer.Tick += (_, _) =>
        {
            _activityTimer.Stop();
            if (!_hovering) SetState(NotchState.Compact);
        };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!_hovering) SetState(NotchState.Compact);
        };
        _clockTimer.Tick += (_, _) => UpdateGlance();
    }

    // ---------- window setup ----------

    void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        AddClipboardFormatListener(hwnd);
    }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;

        ShowOnly(CompactView);
        UpdateGlance();
        _clockTimer.Start();

        _power.Changed += (plugged, pct, low) => Dispatcher.BeginInvoke(() => OnPowerChanged(plugged, pct, low));
        _power.Start();

        try
        {
            _volume.Changed += (pct, muted) => Dispatcher.BeginInvoke(() => OnVolume(pct, muted));
            _volume.Start();
        }
        catch { /* no audio device */ }

        try
        {
            _media.Changed += info => OnMedia(info);
            await _media.InitAsync();
        }
        catch { /* media session API unavailable */ }
    }

    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        RemoveClipboardFormatListener(new WindowInteropHelper(this).Handle);
        _volume.Dispose();
        _power.Dispose();
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // ---------- state machine + animation ----------

    void SetState(NotchState s)
    {
        _state = s;
        var (w, h, r) = s switch
        {
            NotchState.Expanded => (480d, 190d, 30d),
            NotchState.Activity => (320d, 44d, 22d),
            _ => (180d, 32d, 14d),
        };

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(300);
        Notch.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = ease });
        Notch.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
        Notch.CornerRadius = new CornerRadius(0, 0, r, r);

        ShowOnly(s switch
        {
            NotchState.Expanded => ExpandedView,
            NotchState.Activity => ActivityView,
            _ => CompactView,
        });
    }

    void ShowOnly(UIElement view)
    {
        foreach (var v in new UIElement[] { CompactView, ExpandedView, ActivityView })
            if (!ReferenceEquals(v, view)) v.Visibility = Visibility.Collapsed;

        view.Visibility = Visibility.Visible;
        view.BeginAnimation(OpacityProperty, null);
        view.Opacity = 0;
        view.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(100) });
    }

    void Notch_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hovering = true;
        _collapseTimer.Stop();
        _activityTimer.Stop();
        if (_state != NotchState.Expanded) SetState(NotchState.Expanded);
    }

    void Notch_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hovering = false;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    void ShowActivity(string icon, string text, double? progress = null)
    {
        if (_hovering) return;

        ActivityIcon.Text = icon;
        ActivityText.Text = text;
        if (progress is double p)
        {
            ActivityBar.Value = p;
            ActivityBar.Visibility = Visibility.Visible;
        }
        else
        {
            ActivityBar.Visibility = Visibility.Collapsed;
        }

        if (_state != NotchState.Activity) SetState(NotchState.Activity);
        _activityTimer.Stop();
        _activityTimer.Start();
    }

    // ---------- quick glance ----------

    void UpdateGlance()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("h:mm");
        DateText.Text = now.ToString("ddd, d MMM");

        if (_power.HasBattery)
        {
            string glyph = _power.PluggedIn ? "Charging" : "On battery";
            BatteryText.Text = $"{_power.Percent}% · {glyph}";
        }
        else
        {
            BatteryText.Text = "Plugged in";
        }
    }

    // ---------- media ----------

    void OnMedia(MediaInfo? info)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (info is null)
            {
                TitleText.Text = "Nothing playing";
                ArtistText.Text = "";
                ArtBorder.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
                CompactIcon.Visibility = Visibility.Collapsed;
                PlayBtn.Content = "\uE768";
                return;
            }

            TitleText.Text = string.IsNullOrWhiteSpace(info.Title) ? "Unknown title" : info.Title;
            ArtistText.Text = info.Artist;
            PlayBtn.Content = info.Playing ? "\uE769" : "\uE768";
            CompactIcon.Visibility = info.Playing ? Visibility.Visible : Visibility.Collapsed;

            ArtBorder.Background = info.Art is not null
                ? new ImageBrush(info.Art) { Stretch = Stretch.UniformToFill }
                : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));

            if (info.Playing && !string.IsNullOrWhiteSpace(info.Title) && info.Title != _lastTitle)
                ShowActivity("\uE8D6", info.Title);

            _lastTitle = info.Title;
        });
    }

    async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();
    async void Play_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();
    async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();

    // ---------- battery / volume ----------

    void OnPowerChanged(bool plugged, int pct, bool low)
    {
        if (low)
            ShowActivity("\uE850", $"Battery low · {pct}%");
        else if (plugged)
            ShowActivity("\uE83E", $"Charging · {pct}%");
        else
            ShowActivity("\uE850", $"On battery · {pct}%");
        UpdateGlance();
    }

    void OnVolume(int pct, bool muted)
    {
        // The audio API fires once right after registering; ignore that.
        if ((DateTime.UtcNow - _startedAt).TotalSeconds < 2) return;
        ShowActivity(muted ? "\uE74F" : "\uE767", muted ? "Muted" : $"Volume {pct}%", muted ? 0 : pct);
    }

    // ---------- clipboard ----------

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
            Dispatcher.BeginInvoke(OnClipboardChanged, DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    void OnClipboardChanged()
    {
        string? text = null;
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (Clipboard.ContainsText()) text = Clipboard.GetText();
                break;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(30); // clipboard locked by another app, retry
            }
        }

        if (string.IsNullOrWhiteSpace(text)) return;
        if (_clips.Count > 0 && _clips[0].Full == text) return;

        var existing = _clips.FirstOrDefault(c => c.Full == text);
        if (existing is not null) _clips.Remove(existing);
        _clips.Insert(0, new ClipItem(text));
        while (_clips.Count > 3) _clips.RemoveAt(_clips.Count - 1);

        var preview = _clips[0].Preview;
        if (preview.Length > 28) preview = preview[..28] + "…";
        ShowActivity("\uE8C8", "Copied: " + preview);
    }

    void ClipItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipItem item })
        {
            try { Clipboard.SetText(item.Full); } catch { /* locked, ignore */ }
        }
    }
}
