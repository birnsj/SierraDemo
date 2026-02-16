# Coordinates and widescreen

## Coordinate system

- **Base space**: All room coordinates (hotspots, exits, spawn, walkability, priority) use **room base space** defined in `room.json` → `baseSize` (e.g. `"w": 320, "h": 190`).
- Origin is **top-left**. X increases right, Y increases down.
- Valid range: **X in [0, baseSize.w)**, **Y in [0, baseSize.h)**.
- Screen position = base position × `RenderScale`; `RenderScale` is computed from the viewport so the room fits with aspect ratio (center X, anchor bottom, black bars).

## Widescreen later

- To support widescreen, use a **wider base size** in `room.json` (e.g. `640x360` or your target aspect ratio). No engine changes are required: layout, viewport scaling, and all coordinate math already use `baseSize` from the room.
- Ensure art (control map, priority map, backdrops) matches the new base resolution; hotspot/exit rects and spawn stay in base coordinates.
