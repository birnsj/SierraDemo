# Control map conventions

Control maps define where the player can walk. This project uses a single **luminance threshold** and an **invert** flag. The control image is sampled per-pixel; luma = `0.299*R + 0.587*G + 0.114*B`.

## Two common conventions

### 1. Bright = walkable (default in this POC)

- **Path/ground** is drawn **white or light** in the control image.
- **Walls/barriers** are **black or dark**.
- **room.json:** `"invert": false`, `"walkableIfLumaGte": 0.2`  
  → Walkable when luma ≥ 0.2 (so any non‑black area is walkable).

Use this when the control map was drawn by hand or by a tool that uses “white = path”.

### 2. QFG1 VGA / SCI style (white = barrier)

In Sierra’s SCI engine (used by Quest for Glory 1 VGA), the **control screen** uses **control value 15 (ctlWHITE)** for **impassable** areas. Other colors (0–14) are walkable or used for triggers.

- So in an **exported** control image from SCI / SCI Companion: **white (value 15) = barrier**, **black/other = walkable**.
- **room.json:** `"invert": true`, `"walkableIfLumaGte": 0.5`  
  → Walkable when luma **<** 0.5 (white and light gray are blocking; red and dark are walkable).

Use this when your `control.png` comes from an SCI pic export where barriers are drawn in white (control 15).

## Summary

| Convention              | invert | walkableIfLumaGte | Meaning                          |
|-------------------------|--------|-------------------|----------------------------------|
| Bright = walkable      | false  | 0.2               | Path is white/light, walls dark  |
| SCI / QFG1 (white barrier) | true   | 0.5               | Barriers = white, path = dark/red |

If the control map “isn’t working”, switch to the other convention (flip `invert` and set the threshold as above) to match how your control image was drawn or exported.
