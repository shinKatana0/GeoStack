# GeoStack contributor instructions

Use English for source, comments, documentation, tests and commit messages. Keep Russian translations in localization resources. Build with .NET 10 on Windows. Use repository-local DOTNET_CLI_HOME through scripts/environment.ps1. Never change global Git settings or permissions.

Scanning is read-only. Use synthetic fixtures under .local for testing. Do not inspect personal media. Keep the runtime fully offline. Ship local geographic data and media tools with licenses. Do not claim decoder support without an actual decode test. Copy is the default for future organization features; moves require confirmation and verified transfer safety.

Run scripts/test.ps1 and scripts/build-portable.ps1 before recording acceptance. Record observed results with PASS, FAIL, BLOCKED or NOT_TESTED. Commit validated milestones locally. No public publishing.
