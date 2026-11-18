English | [Russian](PORTABLE.ru.md)

# Installation and portable use

<!-- section:scope -->
## Source-only status

No public executable ZIP is approved. These instructions describe a locally built or separately approved Windows 11 x64 portable package, not an available download. Native media tools are not in this source tree; see [BUILD](../BUILD.md).

<!-- section:usage -->
## Use and preserve data

Extract the complete package into a writable directory and run `GeoStack.exe`. Keep required tools, geography and notices together. Self-contained builds need no installer or system-wide .NET runtime. Do not overwrite a running executable. Preserve the data profile, map catalog, installed packages, thumbnails and copy/move journals when updating. Back up the previous complete package separately from user data.

Choose language/theme in Settings; System follows supported system defaults with English fallback. An explicit `--data <isolated-profile>` can be used for disposable testing; do not point a preview at production data. Never weaken Windows security to run an unsigned package.
