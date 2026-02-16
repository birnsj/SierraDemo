# Hotspot Editor Guide

## Resolution Control

Press **R** to cycle through 3 resolution modes:
- **320x200 (1x)** - Original Sierra resolution, window displays at 2x for visibility
- **1920x1140 (6x)** - Full HD resolution
- **3840x2280 (12x)** - 4K resolution

When you change resolution, an on-screen indicator shows:
- Current resolution and scale
- Which image is being used (pic_base.png or pic_base_large.png)
- The render scale multiplier

Press **T** to toggle between `pic_base.png` and `pic_base_large.png` backdrop images.

## Activating the Editor

Press **F4** to toggle the hotspot/exit editor mode. When active, you'll see:
- Yellow rectangles for hotspots
- Green rectangles for exits
- Labels showing the ID and target room

## Hotspots vs Exits

- **Hotspots**: Interactive objects that respond to verbs (look, use, talk). Triggered by **clicking**.
- **Exits**: Room transition zones. Triggered **automatically when the player walks into them**.

## Editing Hotspots and Exits

### Selecting
- **Click once** on any hotspot or exit to select it
- Selected items turn **orange** (hotspots) or **cyan** (exits)
- White handles appear at corners and edges when selected
- Click on empty space to deselect

### Moving
- **Click** where you want the hotspot/exit to go (with it selected, or click on empty space with one selected)
- The box **moves so its center** is at the click position
- Use **resize handles** to change size

### Resizing
- **Click and drag** any of the white handles to resize:
  - **Corner handles**: Resize from that corner (diagonal)
  - **Edge handles**: Resize from that edge (horizontal/vertical only)
- Minimum size is enforced (5x5 pixels)
- Position and size are clamped to room bounds

### Adding a hotspot
- Press **A** (with F4 mode on) to add a new hotspot
- A default box appears in the center of the room; it is selected so you can move and resize it with handles like the others
- Press **A** again to add more; use **Delete** to remove the selected one, **Ctrl+S** to save

### F4 = Hotspot mode (game frozen)
- While F4 is on, the **player and game are frozen** (no movement, no exit triggers). You only edit hotspots.
- **Size mode** (default): Select hotspots, move them, resize with handles, add (A), delete (Del). Ctrl+S saves.
- **Text edit mode**: Press **R** (while F4 is on) to switch. The on-screen line shows which mode you're in.

### Editing hotspot text
- With **F4** on, press **R** to enter **text edit mode**
- **Select a verb** (1=Look, 2=Use, 3=Talk)
- **Click on a hotspot** → text input dialog opens for that verb
- Edit the text and press **Enter** (or click OK) → saves to the JSON file, dialog closes, you return to **size mode**
- Press **R** again anytime (F4 on) to switch back to text edit mode

### Deleting
- Select a hotspot or exit
- Press **Delete** key to remove it
- Changes are not saved until you press Ctrl+S

## Saving Changes

- Press **Ctrl+S** to save all changes back to the room.json file
- A yellow "UNSAVED CHANGES" message appears when you have unsaved edits
- A confirmation message appears when saved successfully
- JSON is pretty-printed with proper indentation

## Controls Summary

| Key | Action |
|-----|--------|
| **R** | Cycle resolution (320x200 → 1920x1140 → 3840x2280) |
| **T** | Toggle backdrop image (pic_base.png ↔ pic_base_large.png) |
| **F4** | Hotspot mode (game frozen): select, move, resize, add, delete |
| **R** (when F4 on) | Toggle text edit mode ↔ size mode |
| **T** (when F4 off) | Toggle backdrop image |
| **A** | Add new hotspot (size mode) |
| **1/2/3** | Select verb for text edit (Look/Use/Talk) |
| **Click hotspot** | Size mode: select, or move selected to cursor | Text edit mode: open text dialog |
| **Click empty** | Move selected hotspot/exit to cursor (size mode) |
| **Drag Handle** | Resize selected item (size mode) |
| **Delete** | Delete selected hotspot/exit |
| **Ctrl+S** | Save geometry changes (size mode). Text save is automatic on Enter in dialog. |
| **Esc / Click empty** | Deselect |

## Coordinate System

- All editing is done in **base room coordinates** (e.g., 0-319 x 0-189 for a 320x190 room)
- Rectangles are automatically clamped to valid room bounds
- The editor accounts for render scale automatically
- What you see is what you get - handles appear exactly where they should be clicked

## Tips

- **Select first, then drag** - This prevents accidental movement when selecting
- **Use handles for precise resizing** - Corner handles are especially useful
- **Save often** - Press Ctrl+S regularly to avoid losing changes
- **Test interactions** - You can click hotspots normally even in edit mode (just not when selected)
- **Test exits** - Walk your character into exit zones to test room transitions
- The editor works alongside normal gameplay - you can still walk around and test
- **Exit cooldown** - Exits have a 2-second cooldown to prevent accidental re-triggering

## Debug Overlay

The debug overlay shows:
- Current room ID and verb
- Mouse coordinates (base coords)
- Whether mouse is over walkable area
- Ego position and priority
- Editor mode instructions
- Unsaved changes indicator

## Troubleshooting

**Hotspot jumps when I click it:**
- This is normal on first selection. Click again on the selected item to start dragging smoothly.

**Can't select a hotspot:**
- Make sure F4 mode is ON (you should see the rectangles)
- Try clicking in the center of the rectangle, not on another item

**Resize handles don't work:**
- Make sure the item is selected (should be orange/cyan)
- Click directly on the white handle circles
- Handles are clickable in an 8-pixel radius in base coordinates

**Changes aren't saved:**
- Press Ctrl+S to save
- Check the console for save confirmation or error messages
- Ensure you have write permissions to the room.json file

**Exit triggers when I click instead of walk:**
- Exits now trigger only when the player character walks into them
- This is the correct Sierra-style behavior
- To test, use click-to-walk to move your character into the exit zone
