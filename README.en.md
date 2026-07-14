# ddys-jellyfin

Official Jellyfin Server channel plugin for the DDYS API. It adds DDYS browsing, saved-search entries, metadata details, grouped resources, and direct-play media items to Jellyfin channels.

## Features

- Jellyfin channel roots for latest, hot, movies, series, anime, variety, and documentaries.
- Saved search entries generated from plugin settings.
- Detail pages with overview, year, region, type, director, actor, and source links.
- Direct playable items for common media URLs such as `.m3u8`, `.mp4`, `.mkv`, `.webm`, and `.mpd`.
- External resource entries for cloud drive, magnet, and download-page links.
- Settings for API Base, Site Base, API Key, paging, cache, timeout, User-Agent, and direct play.
- Authenticated diagnostics endpoints: `/DDYS/Status`, `/DDYS/Search`, `/DDYS/Movies/{slug}`, and `/DDYS/Cache/Clear`.

## Install

This release targets Jellyfin Server 10.11.x and the matching official plugin SDK.

1. Download `ddys-jellyfin-v0.1.1.zip` and `ddys-jellyfin-v0.1.1.zip.sha256` from GitHub Releases.
2. Verify the package:

   ```powershell
   Get-FileHash .\ddys-jellyfin-v0.1.1.zip -Algorithm SHA256
   Get-Content .\ddys-jellyfin-v0.1.1.zip.sha256
   ```

3. Extract the plugin files into your Jellyfin plugin directory.
4. Restart Jellyfin Server.
5. Configure API Base, API Key, and saved search keywords from the plugin settings page.

Common plugin directories:

```text
%UserProfile%\AppData\Local\jellyfin\plugins
%ProgramData%\Jellyfin\Server\plugins
```

Default API Base:

```text
https://ddys.io/api/v1
```

When configured, API requests include:

```http
Authorization: Bearer <apiKey>
```

## Search behavior

Search is exposed through:

- Saved search entries generated from plugin settings and displayed in the channel root.
- Authenticated endpoint: `/DDYS/Search?query=keyword&page=1&perPage=24`.

## Release assets

Each release provides:

- `ddys-jellyfin-v0.1.1.zip`: plugin package with `Jellyfin.Plugin.Ddys.dll`, `Jellyfin.Plugin.Ddys.pdb`, `meta.json`, `README.md`, and `LICENSE`.
- `ddys-jellyfin-v0.1.1.zip.sha256`: ASCII SHA-256 checksum file without an implicit trailing newline.

`tools/build-package.ps1` creates a deterministic ZIP with fixed timestamps, ordinal entry ordering, and explicit ZIP headers instead of `Compress-Archive`. GitHub Actions runs `node tools/check.mjs`, `node tests/run.mjs`, then builds and uploads the ZIP plus checksum on pushes and tag builds.

## Local checks

```powershell
node tools/check.mjs
node tests/run.mjs
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-package.ps1
```

Packaging requires the .NET SDK 9.x. A machine with only the .NET Runtime can run static checks and tests, but cannot run `dotnet publish`.
