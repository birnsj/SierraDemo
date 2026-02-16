# UI Bar Implementation

## Overview
A pure UI overlay bar that scales with the room's renderScale without affecting room coordinate space.

## Files Created/Modified

### Created:
1. **`Game/UI/UIBar.cs`** - UIBar control script
2. **`Game/UI/UIBar.tscn`** - UIBar scene

### Modified:
1. **`Game/Core/GameMain.tscn`** - Added UIRoot CanvasLayer with UIBar instance
2. **`Game/Core/GameMain.cs`** - Wired up UIBar.SetRenderScale() call

## Scene Tree Layout

```
GameMain (Node2D)
├── RoomRuntime (Node2D)
│   └── [Room content at 0,0 with 320x190 base coords]
├── UIRoot (CanvasLayer, layer=100)
│   └── UIBar (Control)
│       ├── ColorRect (background)
│       └── Label ("Sierra Classics POC")
├── DebugOverlay (CanvasLayer)
└── MessageBox (CanvasLayer)
```

## Key Features

### 1. Independent Coordinate Space
- **Room coordinates**: Always 0-319 x 0-189 (base coords)
- **UI Bar**: Rendered in screen space via CanvasLayer
- **No offset**: Room origin remains at (0,0)

### 2. Dynamic Scaling
- Base bar height: **10 pixels**
- At renderScale=1.0: Bar height = **10 pixels**
- At renderScale=2.0: Bar height = **20 pixels**
- Formula: `height = Mathf.RoundToInt(10 * renderScale)`

### 3. Input Handling
- Mouse events over the UI bar are consumed (blocked from room)
- Mouse events outside the bar pass through to room normally
- Uses `GetViewport().SetInputAsHandled()` to block events

### 4. Visual Properties
- **Background**: Dark blue-gray (0.1, 0.1, 0.15)
- **Text**: "Sierra Classics POC" in white
- **Position**: Anchored to top of viewport, full width
- **Z-order**: CanvasLayer 100 (renders above room content)

## API

### UIBar.SetRenderScale(float renderScale)
```csharp
// Called when room loads or renderScale changes
_uiBar.SetRenderScale(2.0f); // Sets bar height to 20 pixels
```

Updates:
- Bar height via CustomMinimumSize
- Background ColorRect size
- Label vertical alignment
- Immediately updates layout

## Verification

### At renderScale=1.0:
- ✅ Bar height: 10 pixels
- ✅ Room coords: 0-319 x 0-189
- ✅ Room origin: (0,0)
- ✅ No coordinate offset

### At renderScale=2.0:
- ✅ Bar height: 20 pixels
- ✅ Room coords: Still 0-319 x 0-189
- ✅ Room origin: Still (0,0)
- ✅ Input mapping unchanged
- ✅ UI scales visually only

## Implementation Details

### Why CanvasLayer?
- Renders in screen space (independent of Node2D transforms)
- Always on top of scene content
- Perfect for UI overlays

### Why Control with Anchors?
- Automatically fills viewport width (anchor_right = 1.0)
- Handles window resize gracefully
- Standard Godot UI pattern

### Input Blocking Strategy
- `_Input()` checks if mouse is within bar bounds
- If yes: Calls `SetInputAsHandled()` to consume event
- If no: Event propagates to room normally
- This prevents clicking "through" the UI bar

## Testing

Run the game and verify:
1. UI bar appears at top with "Sierra Classics POC"
2. Bar height matches 10 * renderScale
3. Room is positioned below bar but coordinates unchanged
4. Mouse clicks on bar don't affect room
5. Mouse clicks below bar work normally
6. Debug overlay (F1) still shows correct room coords

## Future Enhancements

Possible additions:
- Current room name display
- Inventory icons
- Score/time display
- Save/load buttons
- Settings menu button
- Dynamic content based on game state
