# Roadmap

**English** · [Русский](ROADMAP-RU.md)

✅ — done in code (0.9.0), 🔲 — left.

## Stage 0. Skeleton
- ✅ `src/` (C# 5, built by `csc.exe`), `build.ps1` (`-Install`, `-Package`), `check-refs.ps1`, `.csproj`.
- ✅ Changelog in both languages, Thunderstore manifest.
- 🔲 `thunderstore/icon.png` (256×256) and screenshots.

## Stage 1. HUD reconnaissance
- ✅ Hierarchy and bindings taken from the `main.unity` scene: `healthpanel` (the vertical
  `Health` bar, `healthicon`, food slots `food0..2`, `foodicon (1)`), `staminapanel`,
  `eitrpanel`, `adrenalinepanel` (the game sets their `anchoredPosition` to 130 / 320 every
  frame); animations (`Health/border`, `darken`, `Stamina`) are bound by path.
- ✅ `hudlayout dump` writes the HUD hierarchy to the log.

## Stage 2. Layout from the config
- ✅ Element wrappers the game never touches; position, scale, orientation, bar length,
  growth anchor; build-mode lift; `Enabled = false` gives exactly the game's HUD.

## Stage 3. Edit mode
- ✅ F7, frames, dragging, corner resize, wheel, right click, arrows, Tab, grid and centre
  snapping, character and camera held still, Esc.

## Stage 4. Look and per-element styles
- ✅ Visibility, opacity, number on the bar, number size, bar colour, food timers.
- ✅ Ready-made styles for every element; editing by hand → Custom.

## Stage 5. Presets
- ✅ Built-in presets of the whole HUD; applied whole / layout only / look only.
- ✅ Own presets: save, delete, export and import through the clipboard, files in
  `BepInEx/config/j1ga.hudlayout.presets/`.

## Stage 6. Testing and 1.0.0
- 🔲 In-game testing: 1080p and 1440p/4K, build mode, at the helm, death and respawn, logout
  and back, ConfigurationManager.
- 🔲 Tune the built-in presets' coordinates from screenshots.
- 🔲 Compatibility with popular UI mods; README with screenshots and GIFs; release.
