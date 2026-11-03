# Initial presentation

MonoGame DesktopGL 3.8.6 renders a fixed 480 x 272 target with aspect-preserving letterboxing and optional integer scaling. Screens receive immutable player views and revision-bound legal choices from Core/Views; commands enter DuelEngine.Execute.

Title, menu, preview deck/order selection, field/hand, card inspection and results are available. Arrows/D-pad select, Enter/A confirms, Tab/Y inspects, Escape/B returns to menus. The initial opponent shortcut chooses the first legal action; a dedicated policy is later work.

`--pixel-exact`, `--capture <new.png>` and `--smoke` support bounded rendering/command verification. Authored geometry/font are fallbacks; original capture, texture/font/audio conversion and PSP screen comparison gates remain open. No original assets ship.
