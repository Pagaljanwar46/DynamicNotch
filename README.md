# DynamicNotch for Windows

A MacBook-style "dynamic notch" overlay for Windows, built with C# / WPF (.NET 8).

## Features

- Black pill pinned to the top-center of the primary screen, always on top, never steals focus
- Hover to expand with a smooth animation; collapses shortly after the mouse leaves
- Media controls: title, artist, album art, previous / play-pause / next (any app using Windows media controls: Spotify, browsers, etc.)
- Live-activity popups: charging / unplugged / low battery, volume and mute changes, track changes, "Copied" toasts
- Quick glance: time, date, battery status
- Clipboard: last 3 copied text items, click one to copy it again
- Right-click the notch for Exit

## Build and run

Requires Windows 10 (19041+) or Windows 11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet run
```

Or publish a single exe:

```
dotnet publish -c Release -r win-x64 --self-contained false
```

## Notes and next steps

- Notification mirroring (showing Windows toasts in the notch) is not included. `UserNotificationListener` needs package identity, so it requires packaging the app as MSIX. That is a good next step.
- Only the primary monitor is supported.
- Sizes and timings live in `SetState` and the timer fields in `MainWindow.xaml.cs`.
- Auto-start on login: put a shortcut to the exe in `shell:startup`.
