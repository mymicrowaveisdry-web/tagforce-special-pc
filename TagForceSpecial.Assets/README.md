# Asset readers

Readers for EHP containers, card metadata, descriptions and image indexes.
Inputs are supplied locally; proprietary game assets are not bundled.
`CardAttackGetter` reconstructs packed-word numeric extraction separately from
the nullable card model. Original-game runtime comparisons require optional
local reference fixtures; CI covers the synthetic packed-word cases.
