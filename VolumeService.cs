using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicNotch;

/// <summary>Listens for master volume / mute changes on the default output device.</summary>
public class VolumeService : IDisposable
{
    MMDeviceEnumerator? _enumerator;
    MMDevice? _device; // keep a reference so the callback isn't garbage collected

    /// <summary>volume percent (0-100), muted</summary>
    public event Action<int, bool>? Changed;

    public void Start()
    {
        _enumerator = new MMDeviceEnumerator();
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
    }

    void OnVolume(AudioVolumeNotificationData data) =>
        Changed?.Invoke((int)Math.Round(data.MasterVolume * 100), data.Muted);

    public void Dispose()
    {
        if (_device is not null)
            _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
        _device?.Dispose();
        _enumerator?.Dispose();
    }
}
