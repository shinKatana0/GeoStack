English | [Russian](ARCHITECTURE.ru.md)

# Architecture

<!-- section:layers -->
## Windows layers

GeoStack.Core contains geographic models, indexed media concepts, clustering and organization contracts. GeoStack.App provides native WPF rendering, dialogs, localization and Windows integration. The Hybrid Atlas combines compact global geometry with optional regional/detail packages; it does not embed a browser. SQLite indexing and administrative containment are distinct from visual map tiles.

<!-- section:contracts -->
## Safety and portability

Background preparation, frozen graphics and bounded caches preserve interaction. Import indexes original paths without copying media. Organization uses collision-safe destinations, integrity verification and durable operation state. UI theme/language and folder-naming language are independent. Keep rendering, indexing, downloaded data and destructive transfer concerns separate. [Android handoff](docs/ANDROID_HANDOFF.md) identifies future replacement boundaries without a Windows refactor.
