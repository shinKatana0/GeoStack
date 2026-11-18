English | [Russian](REDISTRIBUTION_STATUS.ru.md)

# Source and binary distribution

<!-- section:source -->
## Source code

GeoStack-owned source and original branding are licensed under Apache-2.0, Copyright 2026 shinKatana0. The repository includes compact geographic data and the applicable notices. It does not include the native media-tool distributions. See [LICENSE](../LICENSE), [NOTICE](../NOTICE) and [third-party notices](../THIRD_PARTY_NOTICES.md) for the separate rights involved.

<!-- section:binary -->
## Windows packages

Official Windows binaries are not yet distributed. The outstanding requirements apply to the exact bundled tools and data, independently of GeoStack's source license.

| Component | Status and missing material |
|---|---|
| FFmpeg GPL-3.0-or-later | BLOCKED: matching complete corresponding source, patches and build/install recipes. |
| ExifTool/Perl native modules | BLOCKED: matching native source/build provenance and required notices for Brotli, Win32::FindFile and other components. |
| Geographic data | A binary package must include its own required attribution and data-access materials; the source tree already includes the source-scope materials. |
| Code signing | Public signing is not established. Do not use a self-signed certificate as a public trust mechanism. |

An LGPL-labelled replacement still needs its exact binary, source and redistribution obligations checked. A successful local build does not clear a package for redistribution.
