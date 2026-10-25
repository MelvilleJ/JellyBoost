# JellyBoost

A Jellyfin 12.x plugin that boosts quiet movie audio for each user, on every client.

## How it works

Native TV and mobile apps can't run plugin JavaScript, so JellyBoost boosts audio in two ways:

| Client | How the boost is applied | When changes take effect |
| --- | --- | --- |
| Desktop browsers (Chrome, Edge, Firefox) | Web Audio in the browser. No transcoding | Instantly |
| Android TV, Fire TV, Roku, webOS, Tizen, Swiftfin, Jellyfin Android/iOS, Jellyfin Media Player, Safari | The server re-encodes **only the audio track** with an ffmpeg gain + limiter. Video is still stream-copied | Next time playback starts |

- **Server side:** when a boosted user requests playback, the plugin turns off direct play for that request. The client then gets an HLS stream where the video is copied and the audio is transcoded. The plugin wraps Jellyfin's `ITranscodeManager` and adds `volume=<n>dB,alimiter` to that ffmpeg job. A transcode the server would have run anyway (for example to lower bitrate) gets the boost too.
- **Web side:** the plugin adds a script to the served `index.html` (in memory; no files on disk are changed). The script puts a **volume boost** button (equalizer icon) in the video player controls. The panel uses large +/- buttons so it works with a TV remote as well as a mouse or touch.
- **No double boost:** browsers that boost locally register their device id with the server, and the server skips its boost for those devices.
- **Night mode** compresses loud effects so dialogue stays clear. It uses the same settings in ffmpeg and in Web Audio.

The level is stored per user account. If you set it on your phone or in a browser, it also applies on your TV. Admins can also set a level for each user on the plugin settings page, which helps accounts that only watch on a TV.

## Install

```sh
dotnet publish Jellyfin.Plugin.JellyBoost -c Release -o artifacts
```

1. Copy `artifacts/Jellyfin.Plugin.JellyBoost.dll` into `<jellyfin data dir>/plugins/JellyBoost_1.0.1.0/`. The data dir is usually `/var/lib/jellyfin`, or `/config` in Docker.
2. Restart Jellyfin.
3. Open **Dashboard > Plugins > JellyBoost** to set the maximum boost, defaults and per-user levels.
4. Reload the web client (or restart the mobile/desktop app) so the player button shows up.

Requires Jellyfin 12.0 or newer (built against 12.0, so it loads on any 12.x server; .NET 10).

## Limitations

- Clients that read files directly instead of streaming from the server get no boost. This includes Infuse, and Kodi in "native/direct path" mode.
- For boosted users on native clients, playback uses an audio-only transcode instead of direct play. This costs very little CPU, but the server's transcoding must be enabled for those users.
- Jellyfin's own "Audio boost when downmixing" setting (Dashboard > Playback) still applies when 5.1 audio is downmixed to stereo. JellyBoost's gain is added on top of it.

## Development

```sh
dotnet test
```
