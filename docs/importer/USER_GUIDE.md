# Importer user guide

Build and launch from the repository root on Windows:

```powershell
./rtk.ps1 dotnet build ygo-tag-force-special-decomp.slnx
dotnet run --project TagForceSpecial.Workbench --no-build
```

In Settings select a private data folder **outside Git worktrees**, plus the repository folder containing function evidence. Settings default to `%LOCALAPPDATA%/TagForceSpecial.Workbench/settings.json`; `TFSP_WORKBENCH_SETTINGS` can select an isolated settings file. `--data-root <folder>` overrides the startup data root.

On Import select the English v2.1 ISO or its complete extracted directory. Verify shows recognized, structurally valid and supported separately. Optionally select prepared `recipes/viewer.json`; blank retains active recipes or carries forward the previous importer's library. Choose presentation, inventory and cache reuse, then start. Cancel waits for safe completion; repeat imports reuse validated outputs. Partial/failed results retain diagnostics and preserve a complete active import.

Dashboard selects recorded sessions. Asset Explorer loads the inventory or card catalog; search matches paths, formats, parser results and other fields, and column headers sort. Expand the directory tree, select a file, then select EHP members for GIM previews. PNG export saves the displayed frame. Previews require the unchanged original source. Validation loads recorded checks or rechecks generated artifacts; repeat import to recheck the source. Logs supports search/copy/export and view-only clearing. Comparison uses two sessions from the current data root. Analysis loads persisted functions/evidence without executing tests.

The in-game importer offers the same import/recipes/repeat/cancel core and Continue. Cleanup in either GUI previews owned paths and asks for confirmation. CLI equivalents:

```powershell
dotnet run --project TagForceSpecial.Tools --no-build -- import <iso-or-folder> <data-root> --recipes <recipes/viewer.json>
dotnet run --project TagForceSpecial.Tools --no-build -- import-check <data-root>
dotnet run --project TagForceSpecial.Tools --no-build -- import-clean-legacy <data-root>
dotnet run --project TagForceSpecial.Tools --no-build -- import-clean-stages <data-root>
```

Cleanup previews unless `--yes` is supplied. Close other importers using that root first. Re-import invalid outputs instead of editing checksums. Original resources remain private and are never downloaded or bundled. Exported logs/JSON can include local paths; review before sharing.
