[English](BUILD.md) | Русский

# Сборка и запуск GeoStack

<!-- section:requirements -->
## Требования

Нужны Windows 11 x64, SDK .NET 10 из `global.json`, PowerShell и Python 3.10 или новее. Для WPF требуется Windows. Если пакеты ещё не закэшированы, восстановление NuGet требует доступа к сети. Скрипт окружения хранит пакеты и данные CLI в каталоге `.local` внутри репозитория.

<!-- section:commands -->
## Компиляция и запуск

Выполните из корня репозитория:

```powershell
. ./scripts/environment.ps1
dotnet restore GeoStack.sln --ignore-failed-sources -p:NuGetAudit=false
dotnet build GeoStack.sln -c Release --no-restore
python scripts/check-public-tree.py --root . --report .local/public-audit.json
```

Компактный мировой атлас уже включён: для компиляции и его просмотра не нужно генерировать карты. Запуск с отдельным локальным профилем:

```powershell
$demoProfile = Join-Path $PWD '.local/demo-profile'
dotnet run --project src/GeoStack.App -c Release --no-build -- --data $demoProfile
```

Так индекс и настройки демонстрационной медиатеки не смешиваются с другими профилями. Для добавления файлов и создания превью нужны инструменты, перечисленные ниже. Не используйте личные файлы для тестов переноса.

<!-- section:tests -->
## Медиаинструменты и тесты

ExifTool и FFmpeg нужно предоставить отдельно. При запуске из исходников приложение ищет `tools/ExifTool/ExifTool.exe` и `tools/FFmpeg/ffmpeg.exe` в корне репозитория. Сопутствующие файлы ExifTool должны оставаться рядом с его исполняемым файлом. Эти инструменты не включены в исходники; [список зависимостей](release/dependencies.lock.json) содержит версии и хеши, использованные в существующем Windows-пакете. Перед получением или распространением инструмента проверьте его лицензию.

После подготовки инструментов запустите полный набор тестов:

```powershell
./scripts/test.ps1
```

Скрипт использует синтетические и лицензированные образцы и может загрузить недостающие файлы. В GitHub запускаются только проверки исходников и тесты передачи файлов, не требующие этих инструментов. Дополнительным генераторам карт нужны отдельные исходные данные или Python-пакеты; обычная сборка их не запускает.

<!-- section:portable -->
## Портативный пакет

Входные данные воспроизведения Windows RC4 и точный состав инструментов описаны в [материалах Windows](docs/WINDOWS_RELEASE.ru.md). Загрузите фиксированный архив входных данных компиляции из официального выпуска. Он сохраняет исходную ревизию сборки без импорта частной истории Git. Следующая команда из корня публичных исходников проверяет все входы компиляции и соответствующий код продукта; параметр --build запускает компиляцию в отдельную экспериментальную папку. Архив выпуска не подменяется.

```powershell
python scripts/reproduce-windows-rc4.py --inputs GeoStack-0.5.5-RC4-build-sources.zip --output .local/rc4-reproduction
```


```powershell
./scripts/build-portable.ps1 -RC4InputsArchive GeoStack-0.5.5-RC4-build-sources.zip -RC4ReferenceArchive GeoStack-0.5.5-windows-x64.zip -OutputFolder dist/rc4-reproduction -Channel Production -SkipTests
```
