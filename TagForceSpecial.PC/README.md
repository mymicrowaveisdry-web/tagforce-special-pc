# PC application host

[MonoGame GUI](Presentation.md) provides a 480 × 272 supported duel, native setup/deck editor and private-information views; original artwork/font/audio decoding and PSP visual matching remain unfinished.

Run `dotnet run --project TagForceSpecial.PC`. The native first-run wizard imports the exact English v2.1 ISO, including title artwork; pass `--psp-font <ltn4.pgf>` once for PSP-font text. New Game/Continue reach the original Main Menu using a native profile; Options, Save Data and the Deck Menu submenu work there; Deck Edit opens its original core screen (list, tabs, moves, exit; the Trunk is the PC's supported cards) and the other two submenu rows show a PC notice, and other rows show an unavailable notice. F12 opens setup and PC tools, including deck editing and supported duels. Original PSP save import remains unfinished; Tag Force 6 transfer is deliberately unsupported. Controls: arrows/D-pad, F1/controller Start, Enter/A, Tab/Y and Escape/B; see the [opponent contract](../TagForceSpecial.AI/Contract.md).

Presentation check: `dotnet run --project TagForceSpecial.PC -- --presentation-smoke --data-root <validated-private-root> --presentation-trace <new-private.json>` replays 5 cycles of every observed Continue/New Game, Main Menu, Options and Deck Menu route with fixed frames (`--deck-menu-frame <n>` fixes the submenu's own frame); add `--capture-flow <new-private-dir>` to save each state. `--data-root <private-copy> --presentation-realtime <new-private-dir>` walks New Game to the Main Menu and back at normal speed. Edit [GumProject.gumx](UI/Gum/GumProject.gumx).

## Private data and recovery

- Data defaults to `%LOCALAPPDATA%/TagForceSpecial`; `--data-root <directory>` isolates testing.
- Setup offers progress/cancel/resume, integrity checks, safe rebuilds, retained interrupted work and preference backup recovery.
- Deck revisions remain separate from caches; invalid drafts can be saved but cannot launch.

Original sources stay read-only. Assets/catalogs remain private and are not bundled or downloaded; resource status lists remaining art/font/audio gaps.
