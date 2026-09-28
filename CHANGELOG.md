# Changelog

**English** · [Русский](CHANGELOG-RU.md)

The version is set in one place — `HudLayoutPlugin.Version` in `src/HudLayoutPlugin.cs`.

## 0.11.1

- Editor window: the element list is split into groups (bars and food, rest of the HUD, other mods) and long names wrap, so the content fits the window again; other mods' object names are split into words.

## 0.11.0

- **`HudLayout.HudLayoutApi` for other mods**: `Register(id, RectTransform, name[, wrap])`,
  `Unregister(id)`, `IsEditing`, `ApiVersion`. Called through reflection, no hard dependency.
  For objects outside hudroot (a mod's own canvas) and for a proper name in the editor; the
  settings go to `30 Mod <id>`. See the README.

## 0.10.0

- **The rest of the HUD is movable too**, as simple elements (position, scale, visibility,
  opacity; in the editor, presets and the config like the bars): hotbar, Forsaken power,
  status effects, minimap, raid bar, action progress, stagger bar, mount panel, ship
  controls, key hints, centre and top left messages, save and bad connection icons.
  Sections `10 HotKeyBar` … `23 BadConnection`.
- **Other mods' HUD elements**: anything another mod puts into `hudroot` (e.g. ExtraSlots'
  quick, ammo and food hotbars) is found every two seconds and becomes movable, with a
  section `30 Mod <name>`. `ModElements` turns this off, `IgnoreModElements` skips names.
- Elements other code may look up by path — the vanilla hotbar (ExtraSlots copies it via
  `hudroot.Find("HotKeyBar")`) and all other mods' elements — are not wrapped but moved in
  place: our offset, scale and rotation go on top of their own pose, and a pose their owner
  writes becomes the new base.
- A preset applied before another mod's element appeared still applies to it when it does.

## 0.9.5

- The edit mode window has a solid dark background (`EditorOpacity`, 0.95 by default; also a slider in the window).

## 0.9.4

- **Cells use the game's own pattern.** The fill is a tiled 32 px image with a dark edge,
  and the game draws 32 px per 25 points, so the bar already has 25-point cells. Segments no
  longer draws dividers over it; it scales that pattern so one tile covers `SegmentSize`
  points. The fill is kept one tile high and stretched to the thickness, so the pattern
  never repeats or crops.

## 0.9.3

- **Fixed: the wheel still zoomed the camera in the editor** with FirstPersonMode, which
  reads the wheel in its own camera postfix; our distance restore now runs after every other
  mod's, and the game's wheel input reads as zero while editing.
- **Visibility NotFull** for every bar: hidden while full, shown as soon as it drops below
  100% (with a short fade).
- **Cells** (`Segments`, `SegmentSize`): dividers every N points of the maximum (10 by
  default); a bigger maximum gives more cells, with FixedLength the cells get narrower.

## 0.9.2

- **Fixed: Length of the adrenaline bar did nothing** while there was no adrenaline (the
  game sizes that bar only when it is above zero); it is now sized at zero too.
- **Fixed: Thickness repeated or cropped the fill** (a tiled image); the fill is now scaled
  across, the frame and background stretched.
- **Fixed: Number size of stamina, eitr and adrenaline did nothing**: their number sits in a
  40×16 rectangle; the rectangle now grows with the size and never clips "75/100".
- **Number position** per bar: as in the game, middle of the bar, or middle of the filled part.
- The mouse wheel no longer zooms the camera while the editor is open.

## 0.9.1

- **Fixed: the number styles did nothing.** Numeric, Percent and any Text other than
  Vanilla were replaced by the game's number: the game writes its number every frame, and
  ours was only written when the value changed. Now it is written every frame after the game.
- **Thickness** of each bar, independent of Length (the number keeps its size): slider,
  Shift+wheel, and the blue handles in the middle of the bar's sides in the editor.
- **FixedLength**: the bar keeps one length whatever the maximum; eating raises only the
  number (Text = CurrentMax shows it).
- **Icon** on/off for the heart under the health bar and the food symbol.
- Editor window: fits on the screen (the right part was cut off), long texts wrap, only
  vertical scrolling, resizable by its bottom right corner.

## 0.9.0

First version, every stage of the plan in one go; not yet tested in the game.

- **Layout from the config**: position (fraction of the screen, `-1` = the game's), scale,
  orientation (horizontal / vertical; numbers and icons stay upright), bar length and the
  point that stays in place while a bar grows, for health, stamina, eitr, adrenaline and food.
  Applied every frame after `Hud.Update` on wrappers of our own, so the game's animations,
  bar widths and its build-mode lift keep working.
- **Look**: visibility (as in the game / always / hidden), opacity, the number on the bar
  (as in the game / none / current / current/max / percent) and its size, bar colour, food
  timers and their size.
- **Styles per element**: Vanilla, Minimal, Numeric, Percent, Faded, Contrast for bars;
  Vanilla, IconsOnly, LargeTimers, Faded for food. Editing an option by hand switches the
  element to Custom (or to the style it now matches).
- **Presets** of the whole HUD: built-in Vanilla, Centered, CenteredMinimal, BottomLeft,
  Minimal, Numbers; applied whole, layout only or look only. Own presets saved as text files
  in `BepInEx/config/j1ga.hudlayout.presets/`, deleted, exported to and imported from the
  clipboard as one line.
- **Edit mode** (F7): drag, resize by the corners, wheel and Ctrl+wheel, right click to
  reset, arrows and Tab, grid and centre snapping, a window with all options and presets,
  in English or Russian. The character and the camera stand still meanwhile; Esc closes the
  editor instead of opening the game menu.
- Console command `hudlayout` (edit, presets, apply, save, delete, export, import, reset, dump).
