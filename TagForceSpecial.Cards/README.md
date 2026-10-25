# Card database models

Immutable normalized card records and a catalog keyed by game-local ID. Source
hashes and raw row fields preserve provenance. Numeric codes remain uninterpreted;
null ATK/DEF preserves the variable-stat sentinel. No game assets are bundled.

The local PSP import adapter is in TagForceSpecial.Tools; binary readers remain
in TagForceSpecial.Assets. This project has no format, renderer or emulator dependency.
See [research status](../docs/STATUS.md) for evidence and pending visual checks.
