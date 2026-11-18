English | [Russian](GEOGRAPHIC_ORGANIZATION.ru.md)

# Geographic copy, move and recovery

<!-- section:preview -->
## Prepare destinations

Use Copy to Location, or deliberately choose Move to Location. Select exact media IDs, choose a destination root and prepare a preview. Country / City is the default; region/district variants use available verified boundaries. Resolve every file's original GPS independently, never infer locality from filenames or neighboring selected files. Boundary packages differ from visual maps. Missing or historic coverage is explicit and editable; use Skip / Unknown Location / Manual Destination for missing GPS. Folder naming defaults to English independently of UI language.

<!-- section:copy -->
## Copy is the default

Copy preserves originals. Review all destination directories, selection count, hidden selections, conflicts and uncertainty before confirming. The existing engine reserves collision-safe names, streams bounded data and verifies size/SHA-256. It never silently overwrites destination files. Catalog-only removal is unrelated and never deletes original or copied media.

<!-- section:move -->
## Deliberate move and recovery

Move requires deliberate mode selection and confirmation. Sources are removed only after verified destination commitment. Same-volume safe rename and cross-volume staged transfer have different filesystem behavior; never promise universal atomic rollback. If deletion fails, both files remain and the result is partial, not a successful move.

A durable journal records verified/finalized/source-removal phases. Review interrupted operations; uncertain recovery never automatically deletes either file. Completed moves reconcile catalog paths and thumbnails. Cancellation reports completed and unchanged items. Keep independent backups and test with disposable synthetic files before using valuable media. Power loss, failing hardware and every filesystem failure are not universally certified.
