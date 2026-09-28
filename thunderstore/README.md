# HudLayout

Move, resize and restyle the player's HUD — the health, stamina, eitr and adrenaline bars
and the food icons. Client-side only: other players and the server need nothing.

- **In-game editor (F7)**: drag the elements, resize them by the corners, wheel for size,
  Ctrl+wheel for bar length, right click to put one back, arrows to nudge, grid and centre
  snapping. The character and the camera stand still while you edit; Esc closes it.
- **Per element**: position, scale, horizontal or vertical, bar length, which end stays put
  while the bar grows, visibility (as in the game / always / hidden), opacity, the number on
  the bar (none, 75, 75/100, 75%) and its size, bar colour, food timers.
- **Styles** for every element: Vanilla, Minimal, Numeric, Percent, Faded, Contrast for the
  bars; Vanilla, IconsOnly, LargeTimers, Faded for the food.
- **Presets** of the whole HUD: Vanilla, Centered, CenteredMinimal, BottomLeft, Minimal,
  Numbers, AuthorsChoice — applied whole, layout only or look only. Save your own, delete them, share one as
  a single line through the clipboard and import a friend's.
- English and Russian; everything is in `j1ga.hudlayout.cfg` and ConfigurationManager too.

Console: `hudlayout edit | presets | apply <name> [all|layout|style] | save <name> | delete <name> | export [name] | import | reset | dump`

Not compatible with mods that replace the HUD (Auga and the like).
