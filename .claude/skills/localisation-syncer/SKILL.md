---
name: localisation-syncer
description: Maintain Windows RESX translations.
---

# Windows localization

Use scripts under `scripts/windows/` to compare the base `Strings.resx` with
localized catalogs, find unused keys, and apply translations. Review script help
before use. Preserve resource keys used by Windows code and XAML. Run the
localization CatalogValidator and Windows smoke suite after edits.
