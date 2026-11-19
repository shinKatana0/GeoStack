English | [Русский](README.ru.md)

# GeoStack

<img src="assets/branding/GeoStack-256.png" width="64" alt="GeoStack logo">

**An offline photo and video organizer for Windows.**

GeoStack puts your photos and videos on a map using the GPS information saved in each file. Browse a trip by place, view photos taken near one another, and copy or move selected files into country/city folders. Use JPEG, HEIC, supported RAW files and videos together, and download more detailed maps when you need them.

**Download GeoStack 0.5.5 portable for Windows.** [Official release](https://github.com/shinKatana0/GeoStack/releases/tag/v0.5.5) · [Installation and release materials](docs/WINDOWS_RELEASE.md).

![World map with photo groups and the Media Library](docs/screenshots/world-Light.png)

*Photos grouped by place, with individual files in the Media Library. Screenshots use demonstration media.*

<!-- section:features -->
## What you can do

- **Add folders or individual files.** Open a folder or drag files from Explorer. New additions join your library; repeated imports do not add duplicates.
- **Browse by location.** Nearby photos share a stack on the map. Double-click a geotagged item to go to its location. Files without GPS stay available in the library.
- **Choose the files you need.** Filter by GPS, select with Ctrl or Shift, and preview with F4. Removing an item from the library leaves it on disk and keeps it out of folder rescans.
- **Organize files by place.** Preview country/city folders before copying or moving a selection. You can adjust uncertain locations instead of guessing where a file belongs.

The interface is available in English and Russian, with System, Light and Dark themes. Folder-naming language is a separate setting.

<!-- section:source -->
## Start on Windows

GeoStack 0.5.5 is portable and runs on Windows 11 x64. The .NET 10 runtime is included.

1. [Download the Windows x64 ZIP](https://github.com/shinKatana0/GeoStack/releases/download/v0.5.5/GeoStack-0.5.5-windows-x64.zip) from the [official release](https://github.com/shinKatana0/GeoStack/releases/tag/v0.5.5).
2. Extract the complete ZIP into a writable folder and keep its subfolders together.
3. Launch `GeoStack.exe`; no installer is required.
4. Add photos, videos or folders with **Open folder** or drag them from Explorer.
5. Browse the offline map and Media Library; select files and filter by GPS.
6. When you need more map detail, review and confirm optional packages in **Maps**.
7. Use **Copy to Location** or deliberately choose **Move to Location**, review the country/city destinations and confirm the operation.

Download `SHA256SUMS.txt` from the same release and compare its ZIP entry with:

```powershell
Get-FileHash GeoStack-0.5.5-windows-x64.zip -Algorithm SHA256
```

The executables are unsigned. Windows SmartScreen or Smart App Control may warn or block them. Do not disable Windows security protections. For source builds and dependency details, see [BUILD](BUILD.md) and [third-party notices](THIRD_PARTY_NOTICES.md).

<!-- section:maps -->
## Maps for your library

The world map works offline. For more local detail, open **Maps → Download Detailed Maps for My Library**. Review the proposed areas, coverage and storage requirements, then choose what to download. Installed maps can be used offline afterward.

The built-in atlas is an overview, not a worldwide street map. Detail and translated place names depend on installed data. See [offline maps](docs/OFFLINE_MAPS.md).

<!-- section:formats -->
## Media formats

JPEG, HEIC/HEIF, supported RAW formats, MOV and MP4 can be browsed together. Preview support varies by camera format, codec and local media tools. See [Media Library and formats](docs/MEDIA_LIBRARY.md).

<!-- section:safety -->
## Your files and privacy

Normal browsing works offline, without accounts, telemetry or cloud uploads. Map downloads start only after confirmation. They request geographic areas, not your photos, filenames or individual photo-coordinate lists.

Copy preserves originals and is the default. Move is a separate choice: GeoStack checks the destination before removing the source file. Review the folders, keep backups and check interrupted operations before continuing. See [copy, move and recovery](docs/GEOGRAPHIC_ORGANIZATION.md).

<!-- section:gallery -->
## A closer look

<details>
<summary>Photo groups, detailed maps and folder previews</summary>

![Nearby photos grouped on a map of Tokyo, with individual files in the sidebar](docs/screenshots/photo-stacks-Light.png)

*A photo stack near Tokyo, shown with regional map context.*

![Downloaded Prague city map in the Dark theme](docs/screenshots/detailed-city-Dark.png)

*Downloaded city detail in Prague, using the Dark theme.*

![Copy to Location preview showing country and city destination folders](docs/screenshots/organization-Light.png)

*Preview country/city folders before copying, or select Move to Location.*

</details>

[More screenshots](docs/screenshots/README.md) · [Known limitations](docs/KNOWN_LIMITATIONS.md) · [Security](SECURITY.md)

<!-- section:license -->
## License and acknowledgments

GeoStack-owned source and original branding are licensed under [Apache-2.0](LICENSE), Copyright 2026 shinKatana0. Map data and other third-party material keep their own licenses; see [NOTICE](NOTICE) and [third-party notices](THIRD_PARTY_NOTICES.md). The atlas uses Natural Earth and OpenStreetMap-derived data through Protomaps.

[Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md) · [Architecture](ARCHITECTURE.md)
