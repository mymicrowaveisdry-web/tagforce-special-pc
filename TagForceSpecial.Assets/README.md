# Asset readers

Readers for EHP containers, card metadata, descriptions and image indexes.
Inputs are supplied locally; proprietary game assets are not bundled.
`CardAttackGetter` reconstructs packed-word numeric extraction separately from
the nullable card model. See [research status](../docs/STATUS.md) for the limits
of the observed runtime comparisons.
