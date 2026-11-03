# PC application host

[MonoGame GUI](Presentation.md) provides a 480 × 272 supported duel, [native setup/deck editor](UsableSetup.md) and private-information views; original artwork/font/audio decoding and PSP visual matching remain unfinished.

Run `./rtk.ps1 bypass dotnet run --project TagForceSpecial.PC`. Use Local Game Setup to select the exact English v2.1 ISO, then Decks to edit/save/play; no research commands or preconverted package are required. Controls: arrows/D-pad, Enter/A, Tab/Y and Escape/B; see the [opponent contract](../TagForceSpecial.AI/Contract.md).

## Private data and recovery

- Data defaults to `%LOCALAPPDATA%/TagForceSpecial`; `--data-root <directory>` isolates testing.
- Setup offers progress/cancel/resume, integrity checks, safe rebuilds, retained interrupted work and preference backup recovery.
- Deck revisions remain separate from caches; invalid drafts can be saved but cannot launch.

Original sources stay read-only. Assets/catalogs remain private and are not bundled or downloaded; resource status lists remaining art/font/audio gaps.
