English | [Russian](ANDROID_HANDOFF.ru.md)

# Android architectural handoff

<!-- section:reuse -->
## Reusable concepts

Evaluate geographic models, GPS validation, spatial clustering, offline containment, destination planning, package formats, indexed media concepts and integrity/journal contracts. Reuse must preserve original-media permissions and failure semantics; no Windows refactor is required just for hypothetical portability.

<!-- section:replace -->
## Platform work to investigate later

Replace WPF, Windows graphics/decoders, process APIs, file dialogs and Explorer drag-and-drop. Investigate MediaStore, Storage Access Framework, scoped storage, persistable URI permissions, original-media access/deletion consent and lifecycle/background limits. A URI is not a desktop path; cross-provider moves need a new verified contract. No Android project or feature implementation is part of this release.
