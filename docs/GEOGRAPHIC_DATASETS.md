English | [Russian](GEOGRAPHIC_DATASETS.ru.md)

# Geographic data and regeneration

<!-- section:inventory -->
## Included datasets

Natural Earth global geography and populated places are public-domain derivatives. `assets/geography/world.manifest.json` pins sources and generated output. `assets/geography/urban-roads.gslite` contains generalized motorway/trunk/primary context; regional `.gsatlas` files and their manifest are in `offline-maps/atlas-pilot-compact`. OSM source version is Protomaps 4.14.3 / 2026-03-31.

These vector coordinate collections are adapted databases offered under ODbL-1.0. Complete adapted files are already supplied in this tree without an additional charge or restriction. Public-domain contents keep their independent rights. © OpenStreetMap contributors; source via Protomaps. [OSM](https://www.openstreetmap.org/copyright), [ODbL](https://opendatacommons.org/licenses/odbl/1-0/), [Natural Earth](https://www.naturalearthdata.com/about/terms-of-use/).

<!-- section:boundaries -->
## Administrative fixtures

Selected Japan/China/Switzerland/Poland fixtures derive from Overture 2026-10-21.0. SQLite manifests and readable `source/*.geojsonseq` retain source IDs, ODbL/CC BY 4.0 attributes and pinned Wikidata CC0 metadata. Attribution includes OpenStreetMap contributors, Overture Maps Foundation, geoBoundaries, Esri Community Maps and recorded Maps Entity Variant Names. Saint Petersburg uses historic geoBoundaries/Wambacher OSM-derived 2017 extent, not present legal precision. Keep the complete `.gsgeo`, readable inputs, notices and license texts together. These are selected coverage, not full country databases.

<!-- section:formats -->
## Formats and tools

`.gsatlas` is gzip UTF-8 JSON with feature paths/labels in normalized Web Mercator. `.gslite` is gzip little-endian `GSROAD01` version 1; the standard-library `scripts/read-adapted-geography.py` documents and exports every coordinate path. No Python is required at runtime.

Optional developer generators include `prepare-compact-basemap.py`, `prepare-urban-roads.py`, `prepare-atlas-pilot.py`, `compact-atlas-pilot.py` and `convert-geographic-package.py`. Their pinned requirements, expected source hashes and bounded/cache-only behavior remain explicit. Some regeneration needs separately supplied licensed source caches or optional Python packages; those large inputs are not shipped and regeneration is not required to build. Do not run a full planet or large benchmark generation. Protomaps acquisition/conversion runtime source is also included in the C# application. Do not claim an independent byte-identical dataset rebuild unless actually measured.

Source and terms: [Protomaps archive](https://data.source.coop/protomaps/openstreetmap/v4.pmtiles), [Overture](https://docs.overturemaps.org/attribution/#divisions), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), [CC0](https://creativecommons.org/publicdomain/zero/1.0/).
