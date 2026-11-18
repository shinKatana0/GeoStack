[English](GEOGRAPHIC_DATASETS.md) | Русский

# Географические данные и регенерация

<!-- section:inventory -->
## Включённые наборы данных

Глобальная география и населённые пункты Natural Earth — производные данных общественного достояния. `assets/geography/world.manifest.json` закрепляет источники и результат генерации. `assets/geography/urban-roads.gslite` содержит обобщённый контекст motorway/trunk/primary; региональные `.gsatlas` и их манифест находятся в `offline-maps/atlas-pilot-compact`. Версия источника OSM — Protomaps 4.14.3 / 2026-03-31.

Эти коллекции векторных координат — адаптированные базы, предлагаемые под ODbL-1.0. Полные адаптированные файлы уже включены в дерево без дополнительной платы и ограничений. Содержимое общественного достояния сохраняет самостоятельные права. © Участники OpenStreetMap; источник через Protomaps. [OSM](https://www.openstreetmap.org/copyright), [ODbL](https://opendatacommons.org/licenses/odbl/1-0/), [Natural Earth](https://www.naturalearthdata.com/about/terms-of-use/).

<!-- section:boundaries -->
## Административные образцы

Выбранные образцы Японии, Китая, Швейцарии и Польши получены из Overture 2026-10-21.0. Манифесты SQLite и читаемые `source/*.geojsonseq` сохраняют идентификаторы источников, атрибуты ODbL/CC BY 4.0 и закреплённые метаданные Wikidata CC0. Атрибуция включает участников OpenStreetMap, Overture Maps Foundation, geoBoundaries, Esri Community Maps и указанные Maps Entity Variant Names. Для Санкт-Петербурга используется исторический контур 2017 года из geoBoundaries/Wambacher на основе OSM, а не современные юридически точные границы. Сохраняйте полные `.gsgeo`, читаемые входы, уведомления и лицензии вместе. Это выбранное покрытие, а не полные базы стран.

<!-- section:formats -->
## Форматы и инструменты

`.gsatlas` — gzip с UTF-8 JSON: пути геометрии и подписи в нормализованной проекции Web Mercator. `.gslite` — gzip с little-endian форматом `GSROAD01` версии 1; `scripts/read-adapted-geography.py` на стандартной библиотеке документирует и экспортирует каждый путь координат. Во время работы Python не нужен.

Необязательные инструменты разработчика: `prepare-compact-basemap.py`, `prepare-urban-roads.py`, `prepare-atlas-pilot.py`, `compact-atlas-pilot.py` и `convert-geographic-package.py`. Сохраняются закреплённые зависимости, ожидаемые хеши и явные пределы либо работа только с кэшем. Для части регенерации нужны отдельно предоставленные лицензированные кэши источников или Python-пакеты; большие входы не распространяются, а для сборки регенерация не нужна. Не запускайте генерацию всей планеты или больших тестовых наборов. Исходники нативной загрузки и преобразования Protomaps также входят в C#-приложение. Не заявляйте побайтовую идентичность независимой регенерации без фактического измерения.

Источники и условия: [Protomaps archive](https://data.source.coop/protomaps/openstreetmap/v4.pmtiles), [Overture](https://docs.overturemaps.org/attribution/#divisions), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), [CC0](https://creativecommons.org/publicdomain/zero/1.0/).
