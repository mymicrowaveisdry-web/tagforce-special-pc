# Local Gum workflow

## Tools and commands

Run commands from the repository root through its RTK wrapper. These signatures
were read from local gumcli 2026.11.02; rediscover help after upgrading the bundle.
Keep dependencies/edits sequential; help/version queries are read-only.

```powershell
./rtk.ps1 bypass ./Gum-win-x64/GumCli/gumcli.exe --help
./rtk.ps1 bypass ./Gum-win-x64/GumCli/gumcli.exe new --help
./rtk.ps1 bypass ./Gum-win-x64/GumCli/gumcli.exe check --help
./rtk.ps1 bypass ./Gum-win-x64/GumCli/gumcli.exe codegen-init --help
./rtk.ps1 bypass ./Gum-win-x64/GumCli/gumcli.exe screenshot --help
```

For a new editor-authored UI project, use an explicit destination and extension.
The following path is a suggested convention when no project already exists,
not an assertion that the directory has been created:

```powershell
./rtk.ps1 bypass ./Gum-win-x64/GumCli/gumcli.exe new TagForceSpecial.PC/UI/Gum/GumProject.gumx --template forms
./rtk.ps1 bypass ./Gum-win-x64/GumCli/gumcli.exe check TagForceSpecial.PC/UI/Gum/GumProject.gumx --json
```

`new` accepts `--template forms` (default) or `empty`; Forms supports `--theme`.
Omitting the project extension defaults to `.gumj`. Existing `.gumx` XML projects
use `.gusx` screens and `.gucx` components, with separate standards/behaviors and
project references. Preserve existing JSON projects rather than converting them
as a side effect. The inspected XML project schema version is 7. The bundled
themes have larger default canvases; set both design and runtime sizes for the
intended host.

Avoid `new --platform monogame` for ordinary work in this repository: it creates
a separate game project. The PC host already exists. Runtime bootstrap can be
studied in a disposable copy if needed, with any extra solution files disabled
or excluded according to repository instructions.

After the runtime dependency and paths have been configured, code-generation
commands are:

```powershell
./rtk.ps1 bypass ./Gum-win-x64/GumCli/gumcli.exe codegen-init TagForceSpecial.PC/UI/Gum/GumProject.gumx --csproj TagForceSpecial.PC/TagForceSpecial.PC.csproj
./rtk.ps1 bypass ./Gum-win-x64/GumCli/gumcli.exe codegen TagForceSpecial.PC/UI/Gum/GumProject.gumx --element DeckEditor
```

`codegen-init` writes `ProjectCodeSettings.codsj`; inspect it and existing
per-element settings before changing them. `--force` replaces existing settings.
`codegen --prune` removes orphaned generated files, so first verify that its
output directory contains only the intended generated code. Keep handwritten
logic in separate adapters/partial files. Code generation can depend on restored
package metadata; do not diagnose a missing runtime from editor binaries alone.

```powershell
./rtk.ps1 bypass ./Gum-win-x64/GumCli/gumcli.exe fonts TagForceSpecial.PC/UI/Gum/GumProject.gumx
./rtk.ps1 bypass ./Gum-win-x64/GumCli/gumcli.exe screenshot TagForceSpecial.PC/UI/Gum/GumProject.gumx DeckEditor --width 480 --height 272 --backend monogame --output artifacts/gum/deck-editor.png
```

Create the private output parent first. Inspect the resulting image; a successful
exit is insufficient evidence of usable layout. `fonts` can generate additional
system/project fonts with `--font <name-or-relative-path> --size <sizes>`.
Use permitted fonts and preserve `.fnt`/atlas dependencies in runtime content.
Bulk `resave`, reference fixes, packing, conversion and cross-renderer screenshot
diffs are separate operations; use them for a concrete task, not on every edit.

## Useful local references

- `Gum-win-x64/Content/FormsThemes/Standard/GumProject.gumx`: project references,
  canvas sizes, point filtering and behavior registrations.
- `.../Screens/DemoScreenGum.gusx`: instances, state variables and parent links.
- `.../Components/Controls/TextBox.gucx`, `ButtonStandard.gucx`, `ListBox.gucx`
  and `ScrollViewer.gucx`: actual Forms shapes, states and required children.

Read only the relevant elements. Names like `Controls/TextBox` are component
paths; retain their references and dependent behavior/assets when copying them.
Do not ship the whole editor installation as application content.

Suggested ownership when adding a first UI project:

| Path | Role |
| --- | --- |
| `TagForceSpecial.PC/UI/Gum/` | Editable project, screens, components and permitted UI assets |
| `TagForceSpecial.PC/UI/Generated/` | Gum-generated C#; configure the actual generator path |
| `TagForceSpecial.PC/UI/Adapters/` | Handwritten bindings, lifecycle and service calls |
| `TagForceSpecial.PC/Game/GameLoop*.cs` | Existing lifecycle/navigation and transition integration |
| `TagForceSpecial.PC/Rendering/PspViewport.cs` | Logical canvas, letterboxes and pointer mapping |
| `TagForceSpecial.PC/Decks/` and `TagForceSpecial.PC/Setup/` | Existing deck and import/recovery services |
| `TagForceSpecial.PC/DuelUI/DuelController.cs`, `TagForceSpecial.Core/Views/` | Authorized duel views/choices |

Follow an existing organization instead of introducing parallel ownership if
the repository has already adopted Gum by the time this skill is used.

## Runtime API sources

Use the installed/restored package's API as the authority; documentation may
describe a different version. The relevant official documentation is:

- [MonoGame integration](https://docs.flatredball.com/gum/code/getting-started/setup/adding-initializing-gum/monogame-kni-fna)
- [GumService lifecycle and project loading](https://docs.flatredball.com/gum/code/gum-code-reference/gumservice-gumui)
- [Forms controls](https://docs.flatredball.com/gum/code/controls)
- [MonoGame's Gum tutorial](https://docs.monogame.net/articles/tutorials/building_2d_games/20_implementing_ui_with_gum/)

The documented service is `MonoGameGum.GumService`; initialization, update, draw,
content loading, focus registration and input transformation must match the
chosen runtime. Existing PC initialization currently owns a custom render target
and a separate presentation pass, so a backbuffer-only tutorial needs adaptation.

Verify authored files and required assets copy to the actual output and publish
directories with portable relative paths. Launch from the built output directory
to expose accidental dependencies on the repo CWD or `Gum-win-x64` resources.
