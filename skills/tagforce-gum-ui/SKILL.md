---
name: tagforce-gum-ui
description: Create or refine editable Gum screens, components and Forms controls for the Tag Force Special MonoGame PC host using the repository-local Gum installation. Use for menus, deck editing, setup, preferences, dialogs and authorized duel prompts; exclude Godot migration, web UI, gameplay rules and PSP asset decoding.
---

# Tag Force Gum UI

Build usable UI with editable Gum source and working host interactions. Use the
user's `Gum-win-x64` installation and the existing native C# application.

## Locate the tools and current host

Resolve the repository from the current directory or Git root. Expected tools:

- `Gum-win-x64/Gum.exe`: visual editor.
- `Gum-win-x64/GumCli/gumcli.exe`: project creation, checks, code generation,
  fonts and previews. Read its current help before using unfamiliar options.

The inspected bundle reports 2026.11.02; verify the installed version rather than
assuming it remains fixed. Do not download another editor when this one works.
The editor bundle is separate from the game runtime: inspect the PC `.csproj`
for a compatible, pinned `Gum.MonoGame` reference before claiming integration.
Editor/KNI/preview DLLs are not replacements for the MonoGame runtime package.

Follow repository context instructions. Inspect only the relevant screen,
controller, service and renderer source. Start with
`TagForceSpecial.PC/Game/GameLoop*.cs`,
`TagForceSpecial.PC/Rendering/PspViewport.cs`, and the screen's backing service.
Repository folder names may change; verify paths instead of creating a new host.

Read [references/workflow.md](references/workflow.md) for verified local CLI
syntax, file layout, integration pointers and official API documentation.

## Author editable screens

Translate the request into controls, layout, data and interactions, including
the relevant loading, empty, invalid, disabled and recovery states. Use existing
visual conventions or original references actually supplied/inspected. Separate
authored styling from claims of PSP visual equivalence.

For editor-based UI, deliver a Gum project with registered screens, reusable
components and assets that reopen in Gum. Prefer Forms controls for buttons,
lists, text entry, scrolling, focus and enabled/disabled states. Use graphical
elements for decoration. Code-only Gum is appropriate when explicitly requested
or for a small runtime integration probe; it does not fulfill a request for
editable screen designs by itself.

Reuse the project's existing serialization format and source organization.
When starting a new project, choose the extension explicitly: this CLI defaults
to JSON if no extension is supplied. Inspect actual bundled elements for enum
values, instance/parent references, component behavior and file-variable syntax;
do not invent Gum XML/JSON schemas or controls. Copy the needed reusable theme
resources into application-owned content, preserving relative dependencies.
Do not edit the installed theme templates in place.

Keep generated C# separate from handwritten event handlers/adapters. Inspect
code-generation settings and the intended MonoGame output library before
generation. Avoid generating a second game project: extend the existing PC
project and the root solution. Never stage/commit `.gitignore`.

## Connect layout, input and application services

Preserve the existing logical 480 x 272 canvas unless the user requests a
different presentation design. Set Gum's project canvas and runtime layout to
match; connect it to the actual render target and letterboxed presentation.
Apply the window-to-canvas pointer transform exactly once and reject clicks in
letterboxes. Test hit targets after resize and scaling changes.

Initialize/update/draw Gum through the existing game lifecycle using APIs for
the installed runtime version. End any active SpriteBatch before Gum drawing
and restore the graphics state required by the host. Keep GPU/UI work on the
game thread; import progress crosses from workers as data.

Define focus traversal and initial focus. Text fields own typing/navigation
while focused; Enter, Escape, Tab and controller input must not also activate
the legacy menu or duel command in the same frame. Modal dialogs block the
background screen. Unsubscribe handlers and release screen resources on exit;
reopening screens must not duplicate callbacks.

Bind controls to existing services: `DeckDraft`/`DeckLibrary` for editing and
revision persistence, `SetupStore` for import/preferences/recovery, and
`DuelController` with authorized views and revision-bound choices for duels.
Do not put Core rules or source/cache validation into Gum event handlers.
Gum bindings must not receive full duel state, hidden opponent identities or
live replay deck orders. All duel actions enter the command boundary.

Use authored or appropriately licensed UI resources. Original PSP material
comes from the validated private cache and remains outside public content and
packages. Font fallback/theming must not silently imply original PSP fidelity.

## Verify and deliver

Run Gum checks and inspect actual rendered previews for clipping, hierarchy,
text wrapping, empty/error states and focus/disabled visuals. Generate fonts
only as needed and account for their runtime content paths. Then build the
existing solution and run tests appropriate to the changed behavior.

For runtime changes, launch the PC host and exercise the requested interaction:
for example, search/filter/edit/save/reload a deck and launch only when legal.
Check pointer alignment, keyboard/gamepad focus, modal cancellation and screen
reentry as relevant. A CLI screenshot proves rendering, not input or completion.
Report blocked native interaction checks precisely rather than claiming them
from a preview or headless tests.

Report the editable Gum project, integration files, launch/open command, actual
validation performed and remaining limitations. Keep private captures and
continuation notes under `analysis/`; do not bundle the editor with the game.
