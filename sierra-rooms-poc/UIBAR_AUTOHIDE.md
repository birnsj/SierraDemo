# UI Bar Implementation - Auto-Hide with Image

## Overview
A Sierra-style UI overlay bar that uses a custom image and auto-hides, appearing only when the cursor hovers near the top of the screen.

## Key Features

### 1. Auto-Hide Behavior
- **Hidden by default** - Bar starts at 0% opacity
- **Hover zone** - Appears when cursor is within 20 pixels of screen top
- **Smooth fade** - Fade in/out at 8x speed (adjustable)
- **No accidental clicks** - Input blocked only when bar is visible

### 2. Image-Based Design
- Uses custom Sierra-style UI bar image (`ui_bar.png`)
- TextureRect with scale stretching
- Maintains aspect ratio while filling width
- No labels or backgrounds - pure image display

### 3. Dynamic Scaling
- Base bar height: **10 pixels**
- Scales with room renderScale
- Image stretches to fit scaled dimensions

## Implementation Details

### Constants
```csharp
BASE_BAR_HEIGHT = 10        // Base height in pixels
HOVER_ZONE_HEIGHT = 20.0f   // Trigger zone from top (pixels)
FADE_SPEED = 8.0f           // Fade animation speed
```

### Visibility Logic
```csharp
1. Check if mouse Y < HOVER_ZONE_HEIGHT
2. Target alpha = in zone ? 1.0 : 0.0
3. Lerp current alpha toward target
4. Apply to Modulate.a
5. Update MouseFilter based on visibility
```

### Input Handling
- **Visible (alpha > 0.1)**: MouseFilter = Stop, consumes clicks
- **Hidden (alpha ≤ 0.1)**: MouseFilter = Ignore, passes through

## File Structure

```
Game/UI/
├── UIBar.cs           // Auto-hide logic + image display
├── UIBar.tscn         // Scene definition
├── ui_bar.png         // Sierra-style UI bar image
└── ui_bar.png.import  // Godot import settings
```

## Behavior Flow

```
Game starts
  ↓
Bar hidden (alpha = 0)
  ↓
Mouse moves to top 20px of screen
  ↓
Bar fades IN (smooth lerp to alpha = 1)
  ↓
Cursor remains at top → Bar stays visible
  ↓
Cursor moves away from top
  ↓
Bar fades OUT (smooth lerp to alpha = 0)
  ↓
Bar hidden, room fully interactive
```

## User Experience

### Discovery
- Players naturally move cursor to top to explore
- Bar slides in smoothly when needed
- Doesn't obstruct gameplay when not in use

### Interaction
- Hover to reveal
- Click on visible bar items (future: inventory, save, etc.)
- Move away to hide
- No mode switching or hotkeys required

## Performance

- `_Process()` runs every frame but is lightweight:
  - Single mouse position check
  - One lerp calculation
  - Modulate update
- No render cost when hidden (alpha = 0)
- Input system automatically ignores when hidden

## Configuration

Easy to adjust behavior by changing constants:

```csharp
// Larger hover zone (easier to trigger)
HOVER_ZONE_HEIGHT = 40.0f

// Faster fade (snappier response)
FADE_SPEED = 15.0f

// Slower fade (more cinematic)
FADE_SPEED = 4.0f
```

## Future Enhancements

Possible additions to the bar:
- Clickable inventory icons (read from image regions)
- Action buttons (save, load, settings)
- Score/time display overlay
- Context-sensitive tooltips
- Click-through regions for specific areas

## Testing Checklist

✅ Bar is hidden on game start
✅ Bar appears when cursor moves to top
✅ Bar fades in smoothly
✅ Bar fades out when cursor moves away
✅ Bar scales correctly with renderScale
✅ Image displays without distortion
✅ Input blocked only when bar is visible
✅ Room remains fully interactive when bar hidden
✅ No performance issues or stuttering
✅ Works at different renderScale values (1.0, 2.0, etc.)

## Comparison: Before vs After

### Before:
- Always visible bar at top
- Takes up screen space
- Blocks top portion of room
- Static, always there

### After:
- Hidden by default
- Appears on hover (20px zone)
- Smooth fade animation
- Full screen available for gameplay
- Non-intrusive, discoverable UI
