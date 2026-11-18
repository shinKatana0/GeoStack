English | [Russian](MEDIA_LIBRARY.ru.md)

# Media Library and supported formats

<!-- section:import -->
## Add media

Open a folder or drop files/folders from Explorer. Import adds to the current collection and never moves, renames or edits originals. Recursive scanning skips unsupported items and guards directory-link loops; unavailable sources remain cataloged. Repeated and overlapping paths are deduplicated. Progress/cancellation are available; imports are serialized rather than spawning unbounded scans.

<!-- section:selection -->
## Browse and select

All Media, With GPS and Without GPS use indexed coordinate validity without rescanning metadata. No-GPS files remain in the sidebar without invented map positions. Ctrl+click toggles, Shift+click selects the current sorted range and Ctrl+A selects the active filtered set. Check the total/hidden selection counts before a batch action; Escape/Clear Selection clears it. Double-click navigates to valid GPS; F4 opens preview. Geographic stacks and off-screen counts follow catalog changes.

<!-- section:formats -->
## Formats and catalog removal

JPEG, HEIC/HEIF, supported RAW and MOV/MP4 plus other implemented formats depend on metadata/codec tools and file contents. Existing tests exercise real decode samples, but do not establish support for every camera or codec. No decoder binaries or sample photo corpus are bundled in source.

The three-dot/right-click Remove from Library menu removes only catalog entries after confirmation. Originals and copy history remain untouched; exclusions persist through restart and folder rescans. Explicitly importing an individual excluded file restores it. [Organization](GEOGRAPHIC_ORGANIZATION.md) describes actual file transfers.
