# ddys-jellyfin

Official Jellyfin Server channel plugin for the DDYS API. It adds DDYS browsing, saved-search entries, detail resources, and direct-play media items to Jellyfin channels.

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

1. Download `ddys-jellyfin-v0.1.0.zip` from GitHub Releases.
2. Extract the plugin files into your Jellyfin Server plugins directory.
3. Restart Jellyfin Server.
4. Open the plugin settings page and configure API Base, API Key, and saved searches as needed.

Default API Base:

```text
https://ddys.io/api/v1
```
