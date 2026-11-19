English | [Russian](WINDOWS_RELEASE.ru.md)

# Windows portable release

<!-- section:download -->
## Download status

GeoStack 0.5.5 for Windows 11 x64 is available from the [official v0.5.5 release](https://github.com/shinKatana0/GeoStack/releases/tag/v0.5.5). Obtain the complete ZIP and SHA-256 checksums there. Do not use a locally compiled development folder as an official release.

<!-- section:install -->
## Install and start

Extract the complete ZIP into a new writable folder and run GeoStack.exe. No installer or system-wide .NET 10 installation is required. Keep tools, geography, atlas, licenses and compliance together. The executable is unsigned: Windows may warn or prevent it from running. No trusted Authenticode signature is claimed; do not disable Windows security protections.

Use disposable files for initial Copy to Location and Move to Location checks. Copy preserves originals. Move removes sources only after verified destination commitment and confirmation. Country/City proposals require appropriate administrative coverage; missing coverage is shown rather than guessed. Optional detailed-map downloads need authorization.

<!-- section:materials -->
## Sources and notices

Keep the application, checksums, GeoStack-0.5.5-ffmpeg-sources-RC4.zip and GeoStack-0.5.5-perl-notices-and-sources-RC4.zip available together. The application includes Apache-2.0 notices for GeoStack, separate third-party terms, geographic attribution, complete bundled ODbL adapted databases and a readable export tool. Optional downloaded maps retain their own terms. Standard Perl-object source access directions name the exact official Strawberry archive; source archives and original notices supply the other selected arrangements. See [third-party notices](../THIRD_PARTY_NOTICES.md) and [the selected runtime lock](../release/dependencies.lock.json).

<!-- section:source -->
## Source correspondence

The original portable binary records compiler-input revision df4a9d7e4913db3f6eeb74045149ba2f9a40c8d6. This is an archival build identifier, not a commit imported into the public repository. The public release tag identifies the corresponding reviewed public-source snapshot. Public license headers and line endings differ from the original compiler inputs; the only application-code integration is the settings self-check correction. The separate GeoStack-0.5.5-RC4-build-sources.zip supplies the exact original compiler inputs without private history. [Release provenance](../release/windows-rc4.json) pins both that archive and the unchanged binary. Reproduction commands are in [BUILD](../BUILD.md). Rebuild output is separate from the original RC4; differing hashes must be recorded and must not silently replace it.

Extended physical cross-volume transfer testing was not performed for this version. Build and verification scripts do not publish binaries.
