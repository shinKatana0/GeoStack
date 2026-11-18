English | [Russian](MAP_DOWNLOAD_PROVIDERS.ru.md)

# Map providers and package preparation

<!-- section:provider -->
## Verified format path

The implemented provider is Protomaps/OSM via documented PMTiles HTTP ranges. GeoStack reads bounded vector content and generates compatible regional/detail packages with the native pipeline. PMTiles vector archives are not raster MBTiles; generic foreign archives are not automatically compatible. Prague demonstrated a newly acquired city and offline reopen; that is not universal region/label coverage certification. Natural Earth supplies global context, not complete streets.

<!-- section:obligations -->
## Limits and rights

Planning requires explicit confirmation before network use. The current design bounds source transfers per region, retries, work and cancellation; storage preflight uses labelled conservative limits when exact sizes are unknown. Unsupported/failed regions must report failure without installing incomplete archives. Never infer an archive's integrity from its filename.

Provider terms: [Protomaps](https://docs.protomaps.com/basemaps/downloads), [OSM](https://www.openstreetmap.org/copyright). Preserve package source manifests, attribution and applicable ODbL obligations. Optional maps are not bundled release assets. Basic acquisition does not require a paid account; no provider-limit bypass or public OSM tile scraping is supported.
