# Roadmap

**English** · [Русский](ROADMAP-RU.md)

✅ done · 🔲 left

## Done (1.0.0)

- ✅ Skeleton: `src/` (C# 5, `csc.exe`), `build.ps1` (`-Install`, `-Package`), `check-refs.ps1`.
- ✅ HUD reconnaissance from the `main.unity` scene: hierarchy, bindings, animations by path.
- ✅ Bars and food: position, scale, orientation, length, thickness, fixed length, growth
  anchor, cells (the game's own tile pattern), numbers, colour, visibility, opacity, icons.
- ✅ In-game editor (F7): frames, drag, corner and side handles, wheel, arrows, snapping, a
  foldable and resizable window, English / Russian.
- ✅ Styles per element; built-in presets (incl. AuthorsChoice) and your own, clipboard sharing.
- ✅ The rest of the vanilla HUD as simple elements; other mods' elements found in hudroot and
  moved in place; `HudLayoutApi` for mod authors.
- ✅ WeaponArts' label moved to uGUI (WeaponArts 0.15), so it is movable too.

## Release

- 🔲 `thunderstore/icon.png` (prompt in `ICON-PROMPT.md`), `docs/media/icon-128.png`.
- 🔲 Real screenshots and animations instead of the placeholders in `docs/media/`.
- 🔲 GitHub repository `tbsj1ga/HudLayoutValheim`, push, then `build.ps1 -Package` and upload.
- 🔲 Add HudLayout to the "More mods by j1gA" tables of the other mods.

## Later

- 🔲 Check at 1440p / 4K, with a gamepad, with UI mods (MinimalUI, Carturs UI HUD).
- 🔲 The top left messages: confirm they move (the game may draw them another way).
- 🔲 Boss health bar and the build panel as elements.
- 🔲 Scale by two axes for simple elements, spacing of the food icons.
