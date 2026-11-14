# Yu-Gi-Oh! ARC-V Tag Force Special PC

C# reconstruction for native PC play, with extensible cards as a long-term goal.
**Initial playable slice:** the MonoGame host supports a 14-card pool against a seeded opponent; game assets are not bundled.

## Quick start

Requires .NET 10 SDK. Tests/demo use synthetic inputs; the [PC GUI](TagForceSpecial.PC/README.md) needs a validated private cache.

```powershell
dotnet restore ygo-tag-force-special-decomp.slnx -p:RestorePackagesWithLockFile=false
dotnet build ygo-tag-force-special-decomp.slnx --no-restore -p:IncludeReferenceFixtures=false
dotnet test ygo-tag-force-special-decomp.slnx --no-build --no-restore -p:IncludeReferenceFixtures=false --filter 'Category!=LocalReference'
dotnet run --project TagForceSpecial.Tools --no-build -- duel-demo
```

## Implemented scope

- Deterministic two-player turns, draws, summons/sets, positions, basic combat and defeat.
- Explicit opt-ins for decisions, scoped Spell lifecycles, costs/targets and chains.
- Experimental triggers, continuous stats/negation, staged battle and attack replays.
- Immutable catalogs with authored cards and preserved import provenance.

General card effects, shuffle, original texture/font/audio conversion and PSP screen matching remain unfinished; native setup/editor prepares supported decks and [opponent acceptance](TagForceSpecial.AI/Contract.md) covers their win/loss flow.

## Validation and research

[CI](.github/workflows/progress.yml) builds and tests synthetic behavior; see its results for current counts. Screen reconstruction records and PC/reference gates stay local.
`LocalReference` tests require ignored PSP fixtures and `IncludeReferenceFixtures=true`.
Research tools/data stay local. Retained comparisons cover limited outcomes;
new timing comparisons remain deferred. Synthetic tests do not establish full PSP equivalence.

## Projects

| Project | Purpose |
| --- | --- |
| [Assets](TagForceSpecial.Assets/README.md) | PSP resource readers |
| [Cards](TagForceSpecial.Cards/README.md) | Immutable card catalog |
| [Core](TagForceSpecial.Core/README.md) | Duel state, commands and experimental contracts |
| [Effects](TagForceSpecial.Effects/README.md) | Reviewed card families and effect definitions |
| [AI](TagForceSpecial.AI/README.md) | Seeded policy, bounded stepping and replays |
| [PC](TagForceSpecial.PC/README.md) | MonoGame host with authorized player views |
| [Tools](TagForceSpecial.Tools/README.md) | Demo, imports and local research commands |
| `TagForceSpecial.Tests` | Synthetic and optional PSP reference tests |
