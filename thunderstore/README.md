# HudLayout

**Put your HUD where you want it.**

Drag the health, stamina, eitr and adrenaline bars, the food, the hotbar, the minimap and
the rest of the HUD with the mouse, resize them and change how they look — right in the
game.

![The editor: every element framed, a bar dragged and resized](https://raw.githubusercontent.com/tbsj1ga/HudLayoutValheim/main/docs/media/editor.webp)

## How to use

Press **F7** in the game. Every element gets a frame:

- **drag** it to move it, drag a **corner** to resize it;
- **mouse wheel** — size, **Ctrl+wheel** — bar length, **Shift+wheel** — bar thickness;
- **right click** — back to where the game puts it;
- **F7** or **Esc** — done. The **–** button folds the window out of the way.


## What you can change

- **Bars:** horizontal or vertical, length and thickness, a fixed length that doesn't grow
  with your max (only the number does), cells of 10 points, the number on the bar (none, 75,
  75/100, 75%), its size and place, colour, and when the bar shows — as in the game, always,
  never, or only when it isn't full.
- **Food:** a column or a row, with or without timers.
- **The rest of the HUD:** hotbar, Forsaken power, status effects, minimap, raid bar, mount
  and ship panels, key hints, messages — move, resize, fade or hide.
- **Other mods' HUD** is picked up too — for example ExtraSlots' hotbars.

![Changing a bar's look: style, numbers, cells, colour](https://raw.githubusercontent.com/tbsj1ga/HudLayoutValheim/main/docs/media/styles.webp)

![Numbers and colours on a bar](https://raw.githubusercontent.com/tbsj1ga/HudLayoutValheim/main/docs/media/bars.png)

![Everything the editor can move, including other mods' elements](https://raw.githubusercontent.com/tbsj1ga/HudLayoutValheim/main/docs/media/elements.png)

## Presets

Ready-made layouts — **Vanilla, Centered, Centered minimal, Bottom left, Minimal, Numbers,
Author's choice** — applied in one click, whole or only the layout or only the look. Save
your own, and share one with a friend as a single line of text.


## Multiplayer

Only you need it: nothing is sent to other players, and the server doesn't need it.

## Settings

Everything is also in [ConfigurationManager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/)
(F1 in game) and in `BepInEx/config/j1ga.hudlayout.cfg`. The editor is in English or
Russian — it follows the game, or pick one in the settings.

## Compatibility

Tested with **Valheim 1.0.16**, **BepInEx 5.4.23.5** (BepInExPack_Valheim 5.4.2351).
Mods that **replace** the HUD entirely (Auga and the like) don't mix with it — use one or
the other. If another mod's element shouldn't be moved, add its name to
`IgnoreModElements` in the config.

## Bugs and feedback

GitHub Issues: https://github.com/tbsj1ga/HudLayoutValheim/issues — please attach
`BepInEx/LogOutput.log` and a screenshot; the console command `hudlayout dump` (F5) writes
the HUD layout into that log.

## More mods by j1gA

| | Mod |
|---|---|
| [![LivingMap](https://raw.githubusercontent.com/tbsj1ga/LivingMapValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/) | **[LivingMap](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/)** — Your bases, roads, fields and cleared forest on the map and minimap as you build. |
| [![WeaponArts](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/) | **[WeaponArts](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/)** — One key, one active ability per weapon: stagger, taunt, heals, berserk, crits. |
| [![StationSpeed](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/) | **[StationSpeed](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/)** — Faster smelters, kilns, fermenters and crops — consistent even for players without the mod. |
| [![ExtendedBosses](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/) | **[ExtendedBosses](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/)** — Raid-style boss fights: phases, adds, nests, shields, marks — built from vanilla parts. |
| [![HostOwner](https://raw.githubusercontent.com/tbsj1ga/HostOwnerValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) | **[HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/)** — The host takes ownership of stations and bosses near it, so its mods work for everyone. |

Source, full documentation and the changelog: https://github.com/tbsj1ga/HudLayoutValheim

*Developed with the help of an AI assistant (Claude by Anthropic); the design decisions,
verification against the game code and in-game testing are the author's.*
