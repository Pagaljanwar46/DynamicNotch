using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicNotch;

public record MediaInfo(string Title, string Artist, bool Playing, ImageSource? Art);

/// <summary>
/// Wraps the Windows system media transport controls (Spotify, browsers, etc.).
/// </summary>
public class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _session;

    public event Action<MediaInfo?>? Changed;

    public async Task InitAsync()
    {
        _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _mgr.CurrentSessionChanged += (_, _) => Attach();
        Attach();
    }

    void Attach()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaProps;
            _session.PlaybackInfoChanged -= OnPlaybackInfo;
        }

        _session = _mgr?.GetCurrentSession();

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaProps;
            _session.PlaybackInfoChanged += OnPlaybackInfo;
        }

        _ = RefreshAsync();
    }

    void OnMediaProps(
        GlobalSystemMediaTransportControlsSession s,
        MediaPropertiesChangedEventArgs e)
    {
        _ = RefreshAsync();
    }

    void OnPlaybackInfo(
        GlobalSystemMediaTransportControlsSession s,
        PlaybackInfoChangedEventArgs e)
    {
        _ = RefreshAsync();
    }

    async Task RefreshAsync()
    {
        var s = _session;

        if (s is null)
        {
            Changed?.Invoke(null);
            return;
        }

        try
        {
            var props = await s.TryGetMediaPropertiesAsync();

            var playing = s.GetPlaybackInfo()?.PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var art = await LoadThumbAsync(props.Thumbnail);

            Changed?.Invoke(new MediaInfo(
                props.Title ?? "",
                props.Artist ?? "",
                playing,
                art
            ));
        }
        catch
        {
            Changed?.Invoke(null);
        }
    }

    static async Task<BitmapImage?> LoadThumbAsync(
        IRandomAccessStreamReference? reference)
    {
        if (reference is null)
            return null;

        try
        {
            using var ras = await reference.OpenReadAsync();

            if (ras.Size == 0 || ras.Size > int.MaxValue)
                return null;

            using var reader = new DataReader(ras.GetInputStreamAt(0));

            await reader.LoadAsync((uint)ras.Size);

            byte[] bytes = new byte[(int)ras.Size];
            reader.ReadBytes(bytes);

            using var ms = new MemoryStream(bytes);

            var bmp = new BitmapImage();

            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        if (_session is not null)
            await _session.TryTogglePlayPauseAsync();
    }

    public async Task NextAsync()
    {
        if (_session is not null)
            await _session.TrySkipNextAsync();
    }

    public async Task PreviousAsync()
    {
        if (_session is not null)
            await _session.TrySkipPreviousAsync();
    }
}