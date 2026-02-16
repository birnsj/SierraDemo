using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using SierraRooms.Game.UI;

namespace SierraRooms.Game.Rooms;

public partial class RoomRuntime : Node2D
{
    private RoomData _roomData;
    private Sprite2D _backgroundSprite;
    private Sprite2D _foregroundSprite;  // For priority-based foreground rendering
    private Ego _ego;
    private Image _controlImage;
    private AStar2D _astar; // Grid pathfinding; rebuilt when control map loads
    private Sprite2D _controlDebugSprite;
    private Image _priorityImage;
    private Sprite2D _priorityDebugSprite;
    private MessageBox _messageBox;
    private TextInputDialog _textInputDialog;
    private HotspotEditDialog _hotspotEditDialog;
    private ExitEditDialog _exitEditDialog;
    private DebugOverlay _debugOverlay;
    private Node2D _debugDrawNode;  // Separate node for debug drawing with high Z-index

    // Single active mode: 0=None, 1=F1, 2=F2, 3=F3, 4=F4, 5=F5. New mode closes previous; same key closes all.
    private int _debugOverlayMode = 0;

    // Verb state
    private string _currentVerb = "look";  // Default verb
    
    // Debug state
    private bool _showDebugDraw = false;
    private bool _showHotspotsExits = false;  // F4: create, delete, size, move
    private bool _showHotspotTextMode = false; // F5: title + 3 descriptions only (mutually exclusive with F4)
    private Vector2I _lastRequestedTarget = Vector2I.Zero;
    private Vector2I _lastClampedTarget = Vector2I.Zero;
    private bool _hasDebugTargets = false;
    
    // Priority debug state
    private Vector2I _egoFootSampleCoord = Vector2I.Zero;
    private int _egoPriorityValue = 0;
    
    // Hotspot editing state
    private int _selectedHotspotIndex = -1;
    private int _selectedExitIndex = -1;
    private bool _isResizing = false;
    private string _resizeHandle = ""; // for exits only now
    private Vector2I _dragStartMouse = Vector2I.Zero;
    private Vector2 _dragStartMouseLocalPixels = Vector2.Zero;
    private RectData _dragStartRect;
    // Polygon hotspot: vertex drag and add/delete
    private int _draggingVertexIndex = -1;
    private int _selectedVertexIndex = -1;
    private Vector2 _dragStartVertexPosition; // base coords when started dragging vertex
    private double _lastHotspotClickTime;
    private Vector2I _lastHotspotClickPoint;
    private bool _hasUnsavedChanges = false;
    private bool _justSelected = false; // Track if we just selected to avoid immediate drag
    
    // Exit collision tracking
    private string _lastExitTriggered = "";
    private double _exitCooldownTimer = 0.0;

    // Hotspot click: show description by the hotspot (not in message box)
    private string _clickedHotspotDescription;
    private Vector2 _clickedHotspotScreenPos;
    private double _clickedHotspotDescriptionTime;
    private const double ClickedDescriptionDuration = 6.0;

    // Backdrop toggle (T key): pic_base.png vs pic_base_large.png
    private string _roomPackagePath;
    private bool _useLargeBackdrop = true;
    
    // Resolution scale modes (window size presets; room coords always use room.json BaseSize, including future widescreen)
    private int _resolutionMode = 1; // 0=1x, 1=6x, 2=12x
    private readonly int[] _resolutionWidths = { 320, 1920, 3840 };
    private readonly int[] _resolutionHeights = { 200, 1080, 2160 };
    private readonly string[] _resolutionNames = { "320x200 (1x)", "1920x1080 (6x)", "3840x2160 (12x)" };
    private float _resolutionIndicatorTimer = 0.0f;
    private Vector2 _lastViewportSize = Vector2.Zero; // For resize detection (center + bottom anchor)

    public RoomData RoomData => _roomData;
    public Vector2 BaseSize => new Vector2(_roomData.BaseSize.W, _roomData.BaseSize.H);
    public float RenderScale => _roomData?.RenderScale ?? 1.0f;
    public string CurrentVerb => _currentVerb;
    /// <summary>True when F4 (size/move) or F5 (text edit) overlay is on.</summary>
    public bool IsHotspotOrTextEditMode => _showHotspotsExits || _showHotspotTextMode;
    /// <summary>True when F2, F3, F4, or F5 is on. F1 text only shows when this is false and user pressed F1.</summary>
    public bool IsAnyOtherDebugMode => _showDebugDraw || (_priorityDebugSprite != null && _priorityDebugSprite.Visible) || _showHotspotsExits || _showHotspotTextMode;

    public override void _Ready()
    {
        GD.Print("=== RoomRuntime._Ready() START ===");
        
        // Create debug draw node with very high Z-index
        _debugDrawNode = new Node2D();
        _debugDrawNode.ZIndex = 2000; // Higher than everything else
        _debugDrawNode.Draw += OnDebugDraw;
        AddChild(_debugDrawNode);
        GD.Print("Debug draw node created with Z-index 2000");
        
        // Find MessageBox, TextInputDialog, HotspotEditDialog, DebugOverlay
        _messageBox = GetTree().Root.GetNode<MessageBox>("GameMain/MessageBox");
        _textInputDialog = GetTree().Root.GetNode<TextInputDialog>("GameMain/TextInputDialog");
        _hotspotEditDialog = GetTree().Root.GetNode<HotspotEditDialog>("GameMain/HotspotEditDialog");
        _exitEditDialog = GetTree().Root.GetNode<ExitEditDialog>("GameMain/ExitEditDialog");
        _debugOverlay = GetTree().Root.GetNode<DebugOverlay>("GameMain/DebugOverlay");
        
        LoadRoom("res://RoomPackages/QFG1VGA_TOWN_ENTRANCE/room.json");
        ApplyDebugMode(); // Apply mode 0 so only one mode can be on

        if (_roomData != null)
        {
            GD.Print($"Control image loaded: {_controlImage != null}");
            if (_controlImage != null)
                GD.Print($"Control image size: {_controlImage.GetWidth()}x{_controlImage.GetHeight()}");
        }
        GD.Print("=== RoomRuntime._Ready() END ===");
    }

    private void ApplyDebugMode()
    {
        _debugOverlay?.SetF1Visible(_debugOverlayMode == 1);
        _showDebugDraw = _debugOverlayMode == 2;
        if (_controlDebugSprite != null)
        {
            _controlDebugSprite.Visible = _showDebugDraw;
            _controlDebugSprite.QueueRedraw();
        }
        if (_priorityDebugSprite != null)
        {
            _priorityDebugSprite.Visible = _debugOverlayMode == 3;
            _priorityDebugSprite.QueueRedraw();
        }
        _showHotspotsExits = _debugOverlayMode == 4;
        _showHotspotTextMode = _debugOverlayMode == 5;
        if (_debugOverlayMode != 4 && _debugOverlayMode != 5)
        {
            _selectedHotspotIndex = -1;
            _selectedExitIndex = -1;
        }
        if (_debugDrawNode != null)
            _debugDrawNode.QueueRedraw();
        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            bool overlayOpen = IsAnyInputOverlayVisible();
            // When a message box or edit dialog is open, only allow F-keys and Ctrl+S so we don't steal input from text fields
            if (overlayOpen)
            {
                int newMode = -1;
                if (keyEvent.Keycode == Key.F1) newMode = 1;
                else if (keyEvent.Keycode == Key.F2) newMode = 2;
                else if (keyEvent.Keycode == Key.F3) newMode = 3;
                else if (keyEvent.Keycode == Key.F4) newMode = 4;
                else if (keyEvent.Keycode == Key.F5) newMode = 5;
                if (newMode >= 0)
                {
                    _debugOverlayMode = (_debugOverlayMode == newMode) ? 0 : newMode;
                    ApplyDebugMode();
                    GD.Print($"Debug mode: {(_debugOverlayMode == 0 ? "OFF" : $"F{_debugOverlayMode}")}");
                    if (_debugOverlayMode == 5)
                        ShowMessage("F5: Click a hotspot to edit name and 3 descriptions. Enter saves.");
                    return;
                }
                if (keyEvent.Keycode == Key.S && keyEvent.CtrlPressed && _showHotspotsExits && _hasUnsavedChanges)
                {
                    SaveRoomData();
                    return;
                }
                return; // ignore all other keys when overlay is open
            }

            // F1–F5: one mode active at a time; same key again closes that mode (all off)
            int newMode2 = -1;
            if (keyEvent.Keycode == Key.F1) newMode2 = 1;
            else if (keyEvent.Keycode == Key.F2) newMode2 = 2;
            else if (keyEvent.Keycode == Key.F3) newMode2 = 3;
            else if (keyEvent.Keycode == Key.F4) newMode2 = 4;
            else if (keyEvent.Keycode == Key.F5) newMode2 = 5;
            if (newMode2 >= 0)
            {
                _debugOverlayMode = (_debugOverlayMode == newMode2) ? 0 : newMode2;
                ApplyDebugMode();
                GD.Print($"Debug mode: {(_debugOverlayMode == 0 ? "OFF" : $"F{_debugOverlayMode}")}");
                if (_debugOverlayMode == 5)
                    ShowMessage("F5: Click a hotspot to edit name and 3 descriptions. Enter saves.");
                return;
            }

            // Ctrl+S to save (F4 size mode only; F5 saves via dialog)
            if (keyEvent.Keycode == Key.S && keyEvent.CtrlPressed)
            {
                if (_showHotspotsExits && _hasUnsavedChanges)
                {
                    SaveRoomData();
                }
                return;
            }

            if (keyEvent.Keycode == Key.R)
            {
                ToggleBackdrop();
                return;
            }
            if (keyEvent.Keycode == Key.T)
            {
                // Don't change backdrop in F4 or F5 mode
                if (!_showHotspotsExits && !_showHotspotTextMode)
                    ToggleBackdrop();
                return;
            }

            // Delete (F4 only)
            if (keyEvent.Keycode == Key.Delete)
            {
                if (_showHotspotsExits && (_selectedHotspotIndex >= 0 || _selectedExitIndex >= 0))
                {
                    DeleteSelectedHotspotOrExit();
                }
                return;
            }

            // A adds hotspot (F4 only)
            if (_showHotspotsExits && keyEvent.Keycode == Key.A && _roomData != null)
            {
                AddNewHotspot();
                return;
            }

            // Verb keys 1/2/3 (play mode only, not in F4/F5)
            if (!_showHotspotsExits && !_showHotspotTextMode)
            {
                if (keyEvent.Keycode == Key.Key1)
                {
                    _currentVerb = "look";
                    GD.Print($"Current verb: {_currentVerb}");
                    if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                    return;
                }
                if (keyEvent.Keycode == Key.Key2)
                {
                    _currentVerb = "use";
                    GD.Print($"Current verb: {_currentVerb}");
                    if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                    return;
                }
                if (keyEvent.Keycode == Key.Key3)
                {
                    _currentVerb = "talk";
                    GD.Print($"Current verb: {_currentVerb}");
                    if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                    return;
                }
            }
        }
        
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed)
            {
                if (IsAnyInputOverlayVisible())
                    return;
                // Convert viewport mouse to room local space, then to base coords (so F4 hit-test matches cursor)
                Vector2 screenPos = GetMouseLocalPosition();
                Vector2 baseCoords = ScreenToRoomBase(screenPos);
                
                GD.Print($"RAW Click - Local: {screenPos}, BaseCoords: {baseCoords}, RenderScale: {RenderScale}");
                
                // Clamp to room base bounds [0, BaseSize.W) x [0, BaseSize.H)
                baseCoords.X = Mathf.Clamp(baseCoords.X, 0, _roomData.BaseSize.W - 1);
                baseCoords.Y = Mathf.Clamp(baseCoords.Y, 0, _roomData.BaseSize.H - 1);
                
                Vector2I clickPoint = new Vector2I((int)baseCoords.X, (int)baseCoords.Y);
                
                // F5 text mode: open edit dialog on hotspot or exit click
                if (_showHotspotTextMode)
                {
                    if (CheckHotspotTextEdit(clickPoint))
                        return;
                    if (CheckExitTextEdit(clickPoint))
                        return;
                    return;
                }
                // F4 size mode: select, move, resize
                if (_showHotspotsExits)
                {
                    if (TryStartHotspotEdit(clickPoint, screenPos))
                        return;
                    return;
                }
                
                // Normal play: check hotspots then click-to-walk
                if (CheckHotspots(clickPoint))
                    return;
                _clickedHotspotDescription = null;
                if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                HandleClickToWalk(clickPoint);
            }
            else if (mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed)
            {
                if (_draggingVertexIndex >= 0)
                {
                    _draggingVertexIndex = -1;
                    if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                }
                if (_isResizing)
                {
                    _isResizing = false;
                    _resizeHandle = "";
                    if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                }
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed && (_showHotspotsExits || _showHotspotTextMode))
            {
                _selectedHotspotIndex = -1;
                _selectedExitIndex = -1;
                _selectedVertexIndex = -1;
                if (_debugDrawNode != null)
                    _debugDrawNode.QueueRedraw();
            }
        }
        
        if (@event is InputEventMouseMotion mouseMotion)
        {
            if (_showHotspotsExits && _draggingVertexIndex >= 0)
            {
                HandleVertexDrag(mouseMotion);
                return;
            }
            if (_showHotspotsExits && _isResizing)
                HandleHotspotResize(mouseMotion);
        }
    }
    
    private void OnDebugDraw()
    {
        // Draw click/walk debug targets only when F1 is on (not when F2 control overlay is on)
        if (_hasDebugTargets && _debugOverlayMode == 1)
        {
            Vector2 requestedScreen = new Vector2(_lastRequestedTarget.X, _lastRequestedTarget.Y) * RenderScale;
            _debugDrawNode.DrawLine(requestedScreen + new Vector2(-8, -8), requestedScreen + new Vector2(8, 8), Colors.Red, 3);
            _debugDrawNode.DrawLine(requestedScreen + new Vector2(-8, 8), requestedScreen + new Vector2(8, -8), Colors.Red, 3);
            _debugDrawNode.DrawCircle(requestedScreen, 10, new Color(1, 0, 0, 0.3f));

            Vector2 clampedScreen = new Vector2(_lastClampedTarget.X, _lastClampedTarget.Y) * RenderScale;
            _debugDrawNode.DrawCircle(clampedScreen, 10, Colors.Green);
            _debugDrawNode.DrawCircle(clampedScreen, 7, Colors.Black);
            _debugDrawNode.DrawCircle(clampedScreen, 5, Colors.Green);

            Vector2 footSampleScreen = new Vector2(_egoFootSampleCoord.X, _egoFootSampleCoord.Y) * RenderScale;
            _debugDrawNode.DrawCircle(footSampleScreen, 4, Colors.Blue);
            _debugDrawNode.DrawCircle(footSampleScreen, 2, Colors.Cyan);
        }

        // In normal play: when a hotspot was clicked, show its description in a message box by that hotspot
        if (!_showHotspotsExits && !_showHotspotTextMode && !string.IsNullOrEmpty(_clickedHotspotDescription) && _roomData != null)
        {
            int descFontSize = Mathf.Clamp(24 + (int)(RenderScale * 3f), 32, 56);
            Color descColor = new Color(1f, 1f, 0.95f);
            string text = _clickedHotspotDescription.Length > 60 ? _clickedHotspotDescription.Substring(0, 57) + "..." : _clickedHotspotDescription;
            var font = ThemeDB.FallbackFont;
            Vector2 textSize = font.GetStringSize(text);
            float scale = descFontSize / 16f;
            float boxPadX = 12f;
            float boxPadYTop = 18f;   // extra space above text so box top doesn't cut through (DrawString Y is near baseline)
            float boxPadYBottom = 10f;
            float boxH = textSize.Y * scale + boxPadYTop + boxPadYBottom;
            Vector2 boxSize = new Vector2(textSize.X * scale + boxPadX * 2, boxH);
            float roomW = _roomData.BaseSize.W * RenderScale;
            float roomH = _roomData.BaseSize.H * RenderScale;
            Vector2 boxPos = _clickedHotspotScreenPos - new Vector2(boxPadX, boxPadYTop);
            boxPos.X = Mathf.Clamp(boxPos.X, 0, roomW - boxSize.X);
            boxPos.Y = Mathf.Clamp(boxPos.Y, 0, roomH - boxSize.Y);
            Vector2 textPos = boxPos + new Vector2(boxPadX, boxPadYTop + boxH * 0.2f + 10f);
            Rect2 boxRect = new Rect2(boxPos, boxSize);
            _debugDrawNode.DrawRect(boxRect, new Color(0.08f, 0.08f, 0.12f, 0.92f), true);
            _debugDrawNode.DrawRect(boxRect, new Color(1f, 0.9f, 0.4f), false, 2);
            DrawDebugTextOutlined(_debugDrawNode, textPos, text, descColor, descFontSize);
        }

        // Draw hotspots and exits when F4 or F5 overlay is on (editor: polygons + labels + handles)
        if ((_showHotspotsExits || _showHotspotTextMode) && _roomData != null)
        {
            // Draw hotspots as polygons
            if (_roomData.Hotspots != null)
            {
                for (int i = 0; i < _roomData.Hotspots.Length; i++)
                {
                    var hotspot = _roomData.Hotspots[i];
                    var pts = GetHotspotPoints(hotspot);
                    if (pts == null || pts.Length < 2) continue;
                    Color color = (i == _selectedHotspotIndex) ? Colors.Orange : Colors.Yellow;
                    float lineWidth = (i == _selectedHotspotIndex) ? 3 : 2;
                    // Polygon outline
                    for (int j = 0, k = pts.Length - 1; j < pts.Length; k = j++)
                    {
                        Vector2 a = new Vector2(pts[k].X, pts[k].Y) * RenderScale;
                        Vector2 b = new Vector2(pts[j].X, pts[j].Y) * RenderScale;
                        _debugDrawNode.DrawLine(a, b, color, lineWidth);
                    }
                    // Label by object: top-left of hotspot, left-aligned; size scales with room
                    float minX = pts[0].X, minY = pts[0].Y;
                    foreach (var p in pts)
                    {
                        if (p.X < minX) minX = (float)p.X;
                        if (p.Y < minY) minY = (float)p.Y;
                    }
                    float labelX = minX * RenderScale;
                    float labelY = minY * RenderScale - 6f;
                    int nameFontSize = Mathf.Clamp(12 + (int)(RenderScale * 1.5f), 16, 26);
                    int descFontSize = Mathf.Clamp(10 + (int)(RenderScale * 1.2f), 14, 22);
                    float lineAdvance = 2f + descFontSize * 1.1f;
                    string nameText = hotspot.Id ?? "";
                    if (!string.IsNullOrEmpty(nameText))
                    {
                        DrawDebugTextOutlined(_debugDrawNode, new Vector2(labelX, labelY), nameText, color, nameFontSize);
                        labelY += lineAdvance;
                    }
                    string descText = GetHotspotVerbDescription(hotspot, _currentVerb);
                    if (!string.IsNullOrEmpty(descText))
                    {
                        const int MaxDescChars = 50;
                        if (descText.Length > MaxDescChars)
                            descText = descText.Substring(0, MaxDescChars - 3) + "...";
                        DrawDebugTextOutlined(_debugDrawNode, new Vector2(labelX, labelY), descText, color, descFontSize);
                    }
                    if (i == _selectedHotspotIndex && _showHotspotsExits)
                        DrawVertexHandles(pts, _selectedVertexIndex);
                }
            }
            
            // Draw exits in green
            if (_roomData.Exits != null)
            {
                for (int i = 0; i < _roomData.Exits.Length; i++)
                {
                    var exit = _roomData.Exits[i];
                    var rect = new Rect2(
                        exit.Rect.X * RenderScale,
                        exit.Rect.Y * RenderScale,
                        exit.Rect.W * RenderScale,
                        exit.Rect.H * RenderScale
                    );
                    
                    // Different color if selected
                    Color color = (i == _selectedExitIndex) ? Colors.Cyan : Colors.Green;
                    float lineWidth = (i == _selectedExitIndex) ? 3 : 2;
                    
                    _debugDrawNode.DrawRect(rect, color, false, lineWidth);
                    
                    // Draw label
                    _debugDrawNode.DrawString(ThemeDB.FallbackFont, 
                        new Vector2(rect.Position.X, rect.Position.Y - 5), 
                        $"{exit.Id} -> {exit.TargetRoomId}", 
                        HorizontalAlignment.Left, 
                        -1, 
                        12, 
                        color);
                    
                    // Draw resize handles only in F4 (not in F5 text mode)
                    if (i == _selectedExitIndex && _showHotspotsExits)
                    {
                        DrawResizeHandles(rect);
                    }
                }
            }
            
            // On-screen instructions: one block per mode, 2x larger, 10px lower so not cut off at top
            const float DebugTextLeft = 8f;
            const int DebugFontSize = 28;
            const int DebugFontSizeMode = 32;
            float debugY = 18f;
            float debugLineHeight = 36f;
            float modeBlockGap = 12f;

            if (_showHotspotsExits && _hasUnsavedChanges)
            {
                DrawDebugTextOutlined(_debugDrawNode, new Vector2(DebugTextLeft, debugY), "UNSAVED CHANGES - Press Ctrl+S to save", Colors.Yellow, DebugFontSize);
                debugY += debugLineHeight;
            }

            if (_showHotspotTextMode)
            {
                DrawDebugTextOutlined(_debugDrawNode, new Vector2(DebugTextLeft, debugY), "F5 — TEXT MODE", Colors.Cyan, DebugFontSizeMode);
                debugY += debugLineHeight + modeBlockGap;
                DrawDebugTextOutlined(_debugDrawNode, new Vector2(DebugTextLeft, debugY), "  Click hotspot → edit name, look, use, talk", Colors.Cyan, DebugFontSize);
                debugY += debugLineHeight;
                DrawDebugTextOutlined(_debugDrawNode, new Vector2(DebugTextLeft, debugY), "  Enter = save  |  Esc = close dialog", Colors.Cyan, DebugFontSize);
            }
            else if (_showHotspotsExits)
            {
                DrawDebugTextOutlined(_debugDrawNode, new Vector2(DebugTextLeft, debugY), "F4 — SHAPE MODE", Colors.White, DebugFontSizeMode);
                debugY += debugLineHeight + modeBlockGap;
                DrawDebugTextOutlined(_debugDrawNode, new Vector2(DebugTextLeft, debugY), "  Move: click shape or empty to move  |  Hold Ctrl = vertex mode", Colors.White, DebugFontSize);
                debugY += debugLineHeight;
                DrawDebugTextOutlined(_debugDrawNode, new Vector2(DebugTextLeft, debugY), "  Vertex (Ctrl): drag vertices, double-click edge to add point", Colors.White, DebugFontSize);
                debugY += debugLineHeight;
                DrawDebugTextOutlined(_debugDrawNode, new Vector2(DebugTextLeft, debugY), "  Del = delete vertex or hotspot  |  A = add  |  Ctrl+S = save", Colors.White, DebugFontSize);
            }
        }

        // Resolution indicator (same style: left, outlined)
        if (_resolutionIndicatorTimer > 0 && _roomData != null)
        {
            const float DebugTextLeft = 8f;
            const int DebugFontSize = 22;
            float debugY = (_showHotspotsExits || _showHotspotTextMode) ? 120f : 18f;
            float debugLineHeight = 26f;

            string backdropFile = _useLargeBackdrop ? "pic_base_large.png" : "pic_base.png";
            float currentScale = (float)_resolutionWidths[_resolutionMode] / _roomData.BaseSize.W;
            string resText = $"Resolution: {_resolutionNames[_resolutionMode]}\nImage: {backdropFile}\nRender Scale: {currentScale:F1}x";
            float alpha = Mathf.Clamp(_resolutionIndicatorTimer, 0.0f, 1.0f);
            Color textColor = new Color(0, 1, 1, alpha);
            foreach (string line in resText.Split('\n'))
            {
                DrawDebugTextOutlined(_debugDrawNode, new Vector2(DebugTextLeft, debugY), line, textColor, DebugFontSize);
                debugY += debugLineHeight;
            }
        }
    }

    /// <summary>Draw debug text on the left: small font, black border, left-aligned.</summary>
    private void DrawDebugTextOutlined(CanvasItem canvas, Vector2 pos, string text, Color fillColor, int fontSize)
    {
        var font = ThemeDB.FallbackFont;
        Color outlineColor = Colors.Black;
        int outlineWidth = 1;
        for (int dx = -outlineWidth; dx <= outlineWidth; dx++)
            for (int dy = -outlineWidth; dy <= outlineWidth; dy++)
                if (dx != 0 || dy != 0)
                    canvas.DrawString(font, pos + new Vector2(dx, dy), text, HorizontalAlignment.Left, -1, fontSize, outlineColor);
        canvas.DrawString(font, pos, text, HorizontalAlignment.Left, -1, fontSize, fillColor);
    }
    
    private void DrawResizeHandles(Rect2 rect)
    {
        float handleSize = Mathf.Max(12, 5 * RenderScale);
        float half = handleSize / 2;
        Color fillColor = Colors.White;
        Color outlineColor = Colors.Black;
        void DrawHandle(Vector2 center)
        {
            var r = new Rect2(center.X - half, center.Y - half, handleSize, handleSize);
            _debugDrawNode.DrawRect(r, outlineColor, false, 2);
            _debugDrawNode.DrawRect(r, fillColor, true);
        }
        DrawHandle(rect.Position);
        DrawHandle(new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y));
        DrawHandle(new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y));
        DrawHandle(rect.Position + rect.Size);
        DrawHandle(new Vector2(rect.Position.X + rect.Size.X / 2, rect.Position.Y));
        DrawHandle(new Vector2(rect.Position.X + rect.Size.X / 2, rect.Position.Y + rect.Size.Y));
        DrawHandle(new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y / 2));
        DrawHandle(new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y + rect.Size.Y / 2));
    }

    private void DrawVertexHandles(Vector2Data[] points, int selectedVertexIndex)
    {
        float handleSize = Mathf.Max(10, 4 * RenderScale);
        float half = handleSize / 2;
        for (int i = 0; i < points.Length; i++)
        {
            var p = points[i];
            Vector2 center = new Vector2((float)p.X, (float)p.Y) * RenderScale;
            var r = new Rect2(center.X - half, center.Y - half, handleSize, handleSize);
            bool selected = (i == selectedVertexIndex);
            Color fillColor = selected ? new Color(1f, 0.5f, 0f) : Colors.White;   // Orange = selected (Del to delete)
            Color outlineColor = selected ? Colors.Orange : Colors.Black;
            _debugDrawNode.DrawRect(r, outlineColor, false, selected ? 3 : 2);
            _debugDrawNode.DrawRect(r, fillColor, true);
        }
    }
    
    public override void _Process(double delta)
    {
        // Recompute scale and position on viewport resize (center + bottom anchor, black bars)
        if (_roomData != null)
        {
            Vector2 vp = GetViewport().GetVisibleRect().Size;
            if (vp != _lastViewportSize)
                UpdateRoomViewportLayout();
        }

        // F4 or F5 overlay: freeze player and game
        if (_showHotspotsExits || _showHotspotTextMode)
        {
            if (_ego != null)
            {
                Vector2 egoBase = GetEgoBasePosition();
                _ego.SetTarget(egoBase); // Keep target at current position so ego doesn't move
            }
            if (_resolutionIndicatorTimer > 0)
            {
                _resolutionIndicatorTimer -= (float)delta;
                if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
            }
            return;
        }
        
        // Update exit cooldown timer
        if (_exitCooldownTimer > 0)
        {
            _exitCooldownTimer -= delta;
        }

        // Hotspot click description: hide after duration, keep redrawing while visible
        if (!string.IsNullOrEmpty(_clickedHotspotDescription))
        {
            double now = Time.GetTicksMsec() / 1000.0;
            if (now - _clickedHotspotDescriptionTime > ClickedDescriptionDuration)
            {
                _clickedHotspotDescription = null;
                if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
            }
            else if (_debugDrawNode != null)
                _debugDrawNode.QueueRedraw();
        }
        
        // Update resolution indicator timer
        if (_resolutionIndicatorTimer > 0)
        {
            _resolutionIndicatorTimer -= (float)delta;
            if (_debugDrawNode != null)
            {
                _debugDrawNode.QueueRedraw();
            }
        }
        
        // Update ego priority every frame
        UpdateEgoPriority();
        
        // Check if ego is in an exit zone
        CheckExitCollision();
    }
    
    /// <summary>Check if clicking a hotspot in F5 text mode; opens dialog to rename and edit all 3 verb texts.</summary>
    private bool CheckHotspotTextEdit(Vector2I clickPoint)
    {
        if (!_showHotspotTextMode || _roomData?.Hotspots == null || _hotspotEditDialog == null)
            return false;

        for (int i = 0; i < _roomData.Hotspots.Length; i++)
        {
            var hotspot = _roomData.Hotspots[i];
            if (IsPointInHotspot(clickPoint, hotspot))
            {
                string look = hotspot.Verbs?.Look?.Value ?? "";
                string use = hotspot.Verbs?.Use?.Value ?? "";
                string talk = hotspot.Verbs?.Talk?.Value ?? "";
                int hotspotIndex = i;
                _hotspotEditDialog.ShowHotspot(hotspot.Id ?? "", look, use, talk, (name, newLook, newUse, newTalk) =>
                {
                    OnHotspotEditDialogConfirmed(hotspotIndex, name, newLook, newUse, newTalk);
                });
                return true;
            }
        }

        return false;
    }

    /// <summary>Check if clicking an exit in F5 mode; opens dialog to edit exit id, target room, spawn.</summary>
    private bool CheckExitTextEdit(Vector2I clickPoint)
    {
        if (!_showHotspotTextMode || _roomData?.Exits == null || _exitEditDialog == null)
            return false;
        for (int i = 0; i < _roomData.Exits.Length; i++)
        {
            var exit = _roomData.Exits[i];
            if (IsPointInRect(clickPoint, exit.Rect))
            {
                string spawnX = exit.TargetSpawn != null ? exit.TargetSpawn.X.ToString() : "0";
                string spawnY = exit.TargetSpawn != null ? exit.TargetSpawn.Y.ToString() : "0";
                int exitIndex = i;
                _exitEditDialog.ShowExit(exit.Id ?? "", exit.TargetRoomId ?? "", spawnX, spawnY, (id, targetRoomId, spawnXStr, spawnYStr) =>
                {
                    OnExitEditDialogConfirmed(exitIndex, id, targetRoomId, spawnXStr, spawnYStr);
                });
                return true;
            }
        }
        return false;
    }
    
    /// <summary>Try to start editing a hotspot or exit (vertex drag, add point, select, move). Only used in F4 mode. Hold Ctrl = vertex mode (no move).</summary>
    private bool TryStartHotspotEdit(Vector2I clickPoint, Vector2 clickLocalPixels)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        bool doubleClick = (now - _lastHotspotClickTime < 0.5) && (clickPoint - _lastHotspotClickPoint).LengthSquared() < 900;
        bool vertexMode = Input.IsKeyPressed(Key.Ctrl);

        // 1) Vertex mode (Ctrl held): vertex hit -> drag; double-click edge -> add vertex. Only when Ctrl is down.
        if (vertexMode && _selectedHotspotIndex >= 0 && _roomData?.Hotspots != null && _selectedHotspotIndex < _roomData.Hotspots.Length)
        {
            var hotspot = _roomData.Hotspots[_selectedHotspotIndex];
            var pts = GetHotspotPoints(hotspot);
            if (pts != null)
            {
                int vi = GetVertexAtPoint(clickPoint, pts);
                if (vi >= 0)
                {
                    _draggingVertexIndex = vi;
                    _selectedVertexIndex = vi;
                    _dragStartVertexPosition = new Vector2((float)pts[vi].X, (float)pts[vi].Y);
                    _dragStartMouse = clickPoint;
                    _dragStartMouseLocalPixels = clickLocalPixels;
                    _lastHotspotClickTime = now;
                    _lastHotspotClickPoint = clickPoint;
                    if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                    return true;
                }
                if (doubleClick)
                {
                    int edgeIndex = GetEdgeAtPoint(clickPoint, pts);
                    if (edgeIndex >= 0)
                    {
                        AddVertexToHotspotEdge(_selectedHotspotIndex, edgeIndex, clickPoint);
                        _lastHotspotClickTime = 0;
                        if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                        return true;
                    }
                }
            }
        }

        // 2) Exit resize handles (exits stay rect-based)
        if (_selectedExitIndex >= 0 && _roomData.Exits != null && _selectedExitIndex < _roomData.Exits.Length)
        {
            var exit = _roomData.Exits[_selectedExitIndex];
            string handle = GetResizeHandle(clickPoint, exit.Rect);
            if (handle != "")
            {
                _isResizing = true;
                _resizeHandle = handle;
                _dragStartMouse = clickPoint;
                _dragStartMouseLocalPixels = clickLocalPixels;
                _dragStartRect = exit.Rect;
                if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                return true;
            }
        }

        // 3) Click on hotspot body. Move mode only when Ctrl not held: select or move whole polygon. (Vertex actions are in block 1.)
        if (_roomData?.Hotspots != null)
        {
            for (int i = _roomData.Hotspots.Length - 1; i >= 0; i--)
            {
                var hotspot = _roomData.Hotspots[i];
                if (IsPointInHotspot(clickPoint, hotspot))
                {
                    _selectedVertexIndex = -1;
                    if (vertexMode)
                    {
                        // Vertex mode: only change selection, never move
                        if (_selectedHotspotIndex != i)
                        {
                            _selectedHotspotIndex = i;
                            _selectedExitIndex = -1;
                            GD.Print($"Selected hotspot: {hotspot.Id} (index {i})");
                        }
                        _lastHotspotClickTime = now;
                        _lastHotspotClickPoint = clickPoint;
                        if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                        return true;
                    }
                    var pts = GetHotspotPoints(hotspot);
                    if (_selectedHotspotIndex == i && doubleClick && pts != null)
                    {
                        int edgeIndex = GetEdgeAtPoint(clickPoint, pts);
                        if (edgeIndex >= 0)
                        {
                            AddVertexToHotspotEdge(i, edgeIndex, clickPoint);
                            _lastHotspotClickTime = 0;
                            if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                            return true;
                        }
                    }
                    if (_selectedHotspotIndex == i)
                    {
                        MoveHotspotToPoint(_selectedHotspotIndex, clickPoint);
                        GD.Print($"Moved hotspot: {hotspot.Id}");
                    }
                    else
                    {
                        _selectedHotspotIndex = i;
                        _selectedExitIndex = -1;
                        GD.Print($"Selected hotspot: {hotspot.Id} (index {i})");
                    }
                    _lastHotspotClickTime = now;
                    _lastHotspotClickPoint = clickPoint;
                    if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                    return true;
                }
            }
        }

        // 4) Click on exit body
        if (_roomData?.Exits != null)
        {
            for (int i = _roomData.Exits.Length - 1; i >= 0; i--)
            {
                var exit = _roomData.Exits[i];
                if (IsPointInRect(clickPoint, exit.Rect))
                {
                    if (_selectedExitIndex == i)
                        MoveExitToPoint(_selectedExitIndex, clickPoint);
                    else
                    {
                        _selectedExitIndex = i;
                        _selectedHotspotIndex = -1;
                    }
                    _lastHotspotClickTime = now;
                    _lastHotspotClickPoint = clickPoint;
                    if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                    return true;
                }
            }
        }

        // 5) Clicked empty: move selected hotspot/exit to click (move mode only; vertex mode just deselects)
        if (!vertexMode && _selectedHotspotIndex >= 0 && _roomData?.Hotspots != null && _selectedHotspotIndex < _roomData.Hotspots.Length)
        {
            MoveHotspotToPoint(_selectedHotspotIndex, clickPoint);
            _selectedVertexIndex = -1;
            if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
            return true;
        }
        if (!vertexMode && _selectedExitIndex >= 0 && _roomData?.Exits != null && _selectedExitIndex < _roomData.Exits.Length)
        {
            MoveExitToPoint(_selectedExitIndex, clickPoint);
            if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
            return true;
        }

        _selectedHotspotIndex = -1;
        _selectedExitIndex = -1;
        _selectedVertexIndex = -1;
        _lastHotspotClickTime = now;
        _lastHotspotClickPoint = clickPoint;
        if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
        return false;
    }
    
    private void MoveHotspotToPoint(int index, Vector2I centerBase)
    {
        var hotspot = _roomData.Hotspots[index];
        var pts = GetHotspotPoints(hotspot);
        if (pts == null || pts.Length == 0) return;
        float cx = 0, cy = 0;
        foreach (var p in pts) { cx += (float)p.X; cy += (float)p.Y; }
        cx /= pts.Length;
        cy /= pts.Length;
        float dx = centerBase.X - cx;
        float dy = centerBase.Y - cy;
        if (hotspot.Points == null) hotspot.Points = (Vector2Data[])pts.Clone();
        for (int i = 0; i < hotspot.Points.Length; i++)
        {
            float x = Mathf.Clamp((float)hotspot.Points[i].X + dx, 0, _roomData.BaseSize.W - 1);
            float y = Mathf.Clamp((float)hotspot.Points[i].Y + dy, 0, _roomData.BaseSize.H - 1);
            hotspot.Points[i].X = x;
            hotspot.Points[i].Y = y;
        }
        UpdateHotspotRectFromPoints(hotspot);
        _hasUnsavedChanges = true;
    }
    
    private void AddVertexToHotspotEdge(int hotspotIndex, int edgeIndex, Vector2I positionBase)
    {
        if (_roomData?.Hotspots == null || hotspotIndex < 0 || hotspotIndex >= _roomData.Hotspots.Length) return;
        var hotspot = _roomData.Hotspots[hotspotIndex];
        var pts = GetHotspotPoints(hotspot);
        if (pts == null || edgeIndex < 0 || edgeIndex > pts.Length) return;
        if (hotspot.Points == null) hotspot.Points = (Vector2Data[])pts.Clone();
        var list = new List<Vector2Data>(hotspot.Points);
        list.Insert(edgeIndex, new Vector2Data { X = Mathf.Clamp(positionBase.X, 0, _roomData.BaseSize.W - 1), Y = Mathf.Clamp(positionBase.Y, 0, _roomData.BaseSize.H - 1) });
        hotspot.Points = list.ToArray();
        UpdateHotspotRectFromPoints(hotspot);
        _hasUnsavedChanges = true;
        GD.Print($"Added vertex to hotspot '{hotspot.Id}' (double-click edge to add)");
    }

    private void MoveExitToPoint(int index, Vector2I centerBase)
    {
        var r = _roomData.Exits[index].Rect;
        int newX = Mathf.Clamp(centerBase.X - r.W / 2, 0, _roomData.BaseSize.W - r.W);
        int newY = Mathf.Clamp(centerBase.Y - r.H / 2, 0, _roomData.BaseSize.H - r.H);
        _roomData.Exits[index].Rect.X = newX;
        _roomData.Exits[index].Rect.Y = newY;
        _hasUnsavedChanges = true;
    }
    
    /// <summary>Get which resize handle is near the click point</summary>
    private string GetResizeHandle(Vector2I clickPoint, RectData rect)
    {
        float handleRadius = 8.0f; // Hit area in base coordinates
        
        Vector2 tl = new Vector2(rect.X, rect.Y);
        Vector2 tr = new Vector2(rect.X + rect.W, rect.Y);
        Vector2 bl = new Vector2(rect.X, rect.Y + rect.H);
        Vector2 br = new Vector2(rect.X + rect.W, rect.Y + rect.H);
        Vector2 tm = new Vector2(rect.X + rect.W / 2.0f, rect.Y);
        Vector2 bm = new Vector2(rect.X + rect.W / 2.0f, rect.Y + rect.H);
        Vector2 lm = new Vector2(rect.X, rect.Y + rect.H / 2.0f);
        Vector2 rm = new Vector2(rect.X + rect.W, rect.Y + rect.H / 2.0f);
        
        Vector2 click = new Vector2(clickPoint.X, clickPoint.Y);
        
        // Check corners first (priority over edges)
        if (click.DistanceTo(tl) <= handleRadius) return "tl";
        if (click.DistanceTo(tr) <= handleRadius) return "tr";
        if (click.DistanceTo(bl) <= handleRadius) return "bl";
        if (click.DistanceTo(br) <= handleRadius) return "br";
        
        // Then check edges
        if (click.DistanceTo(tm) <= handleRadius) return "t";
        if (click.DistanceTo(bm) <= handleRadius) return "b";
        if (click.DistanceTo(lm) <= handleRadius) return "l";
        if (click.DistanceTo(rm) <= handleRadius) return "r";
        
        return "";
    }
    
    private void HandleVertexDrag(InputEventMouseMotion mouseMotion)
    {
        if (_draggingVertexIndex < 0 || _selectedHotspotIndex < 0 || _roomData?.Hotspots == null ||
            _selectedHotspotIndex >= _roomData.Hotspots.Length) return;
        var hotspot = _roomData.Hotspots[_selectedHotspotIndex];
        if (hotspot.Points == null || _draggingVertexIndex >= hotspot.Points.Length) return;
        Vector2 currentBase = ScreenToRoomBase(GetMouseLocalPosition());
        float x = Mathf.Clamp(currentBase.X, 0, _roomData.BaseSize.W - 1);
        float y = Mathf.Clamp(currentBase.Y, 0, _roomData.BaseSize.H - 1);
        hotspot.Points[_draggingVertexIndex].X = x;
        hotspot.Points[_draggingVertexIndex].Y = y;
        UpdateHotspotRectFromPoints(hotspot);
        _hasUnsavedChanges = true;
    }

    /// <summary>Handle resizing of selected exit (hotspots use vertex drag).</summary>
    private void HandleHotspotResize(InputEventMouseMotion mouseMotion)
    {
        Vector2 currentPixels = GetMouseLocalPosition();
        Vector2 deltaPixels = currentPixels - _dragStartMouseLocalPixels;
        Vector2 deltaBase = deltaPixels / RenderScale;
        Vector2I delta = new Vector2I((int)Mathf.Round(deltaBase.X), (int)Mathf.Round(deltaBase.Y));
        
        if (_isResizing)
        {
            // Resize based on which handle is being dragged
            RectData newRect = new RectData
            {
                X = _dragStartRect.X,
                Y = _dragStartRect.Y,
                W = _dragStartRect.W,
                H = _dragStartRect.H
            };
            
            switch (_resizeHandle)
            {
                case "tl":
                    newRect.X = _dragStartRect.X + delta.X;
                    newRect.Y = _dragStartRect.Y + delta.Y;
                    newRect.W = _dragStartRect.W - delta.X;
                    newRect.H = _dragStartRect.H - delta.Y;
                    break;
                case "tr":
                    newRect.Y = _dragStartRect.Y + delta.Y;
                    newRect.W = _dragStartRect.W + delta.X;
                    newRect.H = _dragStartRect.H - delta.Y;
                    break;
                case "bl":
                    newRect.X = _dragStartRect.X + delta.X;
                    newRect.W = _dragStartRect.W - delta.X;
                    newRect.H = _dragStartRect.H + delta.Y;
                    break;
                case "br":
                    newRect.W = _dragStartRect.W + delta.X;
                    newRect.H = _dragStartRect.H + delta.Y;
                    break;
                case "t":
                    newRect.Y = _dragStartRect.Y + delta.Y;
                    newRect.H = _dragStartRect.H - delta.Y;
                    break;
                case "b":
                    newRect.H = _dragStartRect.H + delta.Y;
                    break;
                case "l":
                    newRect.X = _dragStartRect.X + delta.X;
                    newRect.W = _dragStartRect.W - delta.X;
                    break;
                case "r":
                    newRect.W = _dragStartRect.W + delta.X;
                    break;
            }
            
            // Ensure minimum size
            if (newRect.W < 5) 
            {
                if (_resizeHandle.Contains("l"))
                    newRect.X = _dragStartRect.X + _dragStartRect.W - 5;
                newRect.W = 5;
            }
            if (newRect.H < 5) 
            {
                if (_resizeHandle.Contains("t"))
                    newRect.Y = _dragStartRect.Y + _dragStartRect.H - 5;
                newRect.H = 5;
            }
            
            // Clamp to room bounds
            newRect.X = Mathf.Clamp(newRect.X, 0, _roomData.BaseSize.W - newRect.W);
            newRect.Y = Mathf.Clamp(newRect.Y, 0, _roomData.BaseSize.H - newRect.H);
            
            if (_selectedExitIndex >= 0 && _roomData.Exits != null && _selectedExitIndex < _roomData.Exits.Length)
            {
                _roomData.Exits[_selectedExitIndex].Rect = newRect;
                _hasUnsavedChanges = true;
            }
        }
        
        if (_debugDrawNode != null)
        {
            _debugDrawNode.QueueRedraw();
        }
    }
    
    /// <summary>Add a new hotspot as a default quad; select it for vertex editing.</summary>
    private void AddNewHotspot()
    {
        int w = 60;
        int h = 40;
        int x = Mathf.Clamp((_roomData.BaseSize.W - w) / 2, 0, _roomData.BaseSize.W - w);
        int y = Mathf.Clamp((_roomData.BaseSize.H - h) / 2, 0, _roomData.BaseSize.H - h);
        var rect = new RectData { X = x, Y = y, W = w, H = h };
        int n = (_roomData.Hotspots?.Length ?? 0) + 1;
        string id = $"hotspot_{n}";
        string actionText = "Something.";
        var newHotspot = new HotspotData
        {
            Id = id,
            Rect = rect,
            Points = RectToPoints(rect),
            Verbs = new VerbActionsData
            {
                Look = new VerbActionData { Type = "text", Value = actionText },
                Use = new VerbActionData { Type = "text", Value = actionText },
                Talk = new VerbActionData { Type = "text", Value = actionText }
            }
        };
        
        var list = new List<HotspotData>(_roomData.Hotspots ?? Array.Empty<HotspotData>());
        list.Add(newHotspot);
        _roomData.Hotspots = list.ToArray();
        _selectedHotspotIndex = list.Count - 1;
        _selectedExitIndex = -1;
        _hasUnsavedChanges = true;
        if (_debugDrawNode != null)
            _debugDrawNode.QueueRedraw();
        GD.Print($"Added hotspot '{id}' (press A again to add more, move/resize with handles, Del to remove, Ctrl+S to save)");
    }
    
    /// <summary>Delete selected vertex (if one selected) or the selected hotspot/exit.</summary>
    private void DeleteSelectedHotspotOrExit()
    {
        if (_selectedVertexIndex >= 0 && _selectedHotspotIndex >= 0 && _roomData?.Hotspots != null &&
            _selectedHotspotIndex < _roomData.Hotspots.Length)
        {
            var hotspot = _roomData.Hotspots[_selectedHotspotIndex];
            if (hotspot.Points != null && hotspot.Points.Length > 3 && _selectedVertexIndex < hotspot.Points.Length)
            {
                var list = new List<Vector2Data>(hotspot.Points);
                list.RemoveAt(_selectedVertexIndex);
                hotspot.Points = list.ToArray();
                UpdateHotspotRectFromPoints(hotspot);
                _selectedVertexIndex = -1;
                _hasUnsavedChanges = true;
                GD.Print($"Deleted vertex from hotspot '{hotspot.Id}' (min 3 points)");
                if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                return;
            }
        }
        if (_selectedHotspotIndex >= 0 && _roomData.Hotspots != null && _selectedHotspotIndex < _roomData.Hotspots.Length)
        {
            var deletedId = _roomData.Hotspots[_selectedHotspotIndex].Id;
            var newList = new List<HotspotData>(_roomData.Hotspots);
            newList.RemoveAt(_selectedHotspotIndex);
            _roomData.Hotspots = newList.ToArray();
            _selectedHotspotIndex = -1;
            _selectedVertexIndex = -1;
            _hasUnsavedChanges = true;
            GD.Print($"Deleted hotspot: {deletedId}");
            if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
        }
        else if (_selectedExitIndex >= 0 && _roomData.Exits != null && _selectedExitIndex < _roomData.Exits.Length)
        {
            var deletedId = _roomData.Exits[_selectedExitIndex].Id;
            var newList = new List<ExitData>(_roomData.Exits);
            newList.RemoveAt(_selectedExitIndex);
            _roomData.Exits = newList.ToArray();
            _selectedExitIndex = -1;
            _hasUnsavedChanges = true;
            GD.Print($"Deleted exit: {deletedId}");
            if (_debugDrawNode != null)
            {
                _debugDrawNode.QueueRedraw();
            }
        }
    }
    
    /// <summary>Save room data back to JSON file</summary>
    private void SaveRoomData()
    {
        if (string.IsNullOrEmpty(_roomPackagePath))
        {
            GD.PrintErr("Cannot save: room package path is not set");
            ShowMessage("Save failed: no room loaded");
            return;
        }
        
        string realPath = ProjectSettings.GlobalizePath(_roomPackagePath);
        foreach (var h in _roomData.Hotspots ?? Array.Empty<HotspotData>())
            UpdateHotspotRectFromPoints(h);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        
        string jsonText = JsonSerializer.Serialize(_roomData, options);
        
        try
        {
            System.IO.File.WriteAllText(realPath, jsonText);
            _hasUnsavedChanges = false;
            GD.Print($"Saved room data to: {realPath}");
            ShowMessage("Room data saved!");
            if (_debugDrawNode != null)
            {
                _debugDrawNode.QueueRedraw();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to save room data: {ex.Message}");
            ShowMessage($"Save failed: {ex.Message}");
        }
    }
    
    /// <summary>Check if click hits a hotspot and handle interaction</summary>
    private bool CheckHotspots(Vector2I clickPoint)
    {
        if (_roomData?.Hotspots == null)
            return false;
            
        for (int i = 0; i < _roomData.Hotspots.Length; i++)
        {
            var hotspot = _roomData.Hotspots[i];
            if (IsPointInHotspot(clickPoint, hotspot))
            {
                GD.Print($"Clicked hotspot: {hotspot.Id} with verb: {_currentVerb}");
                VerbActionData? action = _currentVerb switch
                {
                    "look" => hotspot.Verbs.Look,
                    "use" => hotspot.Verbs.Use,
                    "talk" => hotspot.Verbs.Talk,
                    _ => null
                };
                string message = (action != null && action.Type == "text") ? action.Value : $"You can't {_currentVerb} that.";
                var pts = GetHotspotPoints(hotspot);
                float cx = clickPoint.X;
                float minY = clickPoint.Y;
                if (pts != null && pts.Length > 0)
                {
                    cx = 0; minY = (float)pts[0].Y;
                    foreach (var p in pts) { cx += (float)p.X; if (p.Y < minY) minY = (float)p.Y; }
                    cx /= pts.Length;
                }
                _clickedHotspotDescription = message ?? "";
                _clickedHotspotScreenPos = new Vector2(cx * RenderScale, minY * RenderScale - 12f);
                _clickedHotspotDescriptionTime = Time.GetTicksMsec() / 1000.0;
                if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>Called when user confirms hotspot edit dialog (Enter/OK). Updates name + 3 verb texts, saves to JSON.</summary>
    private void OnHotspotEditDialogConfirmed(int hotspotIndex, string name, string look, string use, string talk)
    {
        if (_roomData?.Hotspots == null || hotspotIndex < 0 || hotspotIndex >= _roomData.Hotspots.Length)
            return;

        var hotspot = _roomData.Hotspots[hotspotIndex];
        if (hotspot.Verbs == null)
            hotspot.Verbs = new VerbActionsData();

        if (!string.IsNullOrWhiteSpace(name))
            hotspot.Id = name.Trim();
        hotspot.Verbs.Look = new VerbActionData { Type = "text", Value = look ?? "" };
        hotspot.Verbs.Use = new VerbActionData { Type = "text", Value = use ?? "" };
        hotspot.Verbs.Talk = new VerbActionData { Type = "text", Value = talk ?? "" };

        _hasUnsavedChanges = true;
        SaveRoomData();
        if (_debugDrawNode != null) _debugDrawNode.QueueRedraw(); // Update hotspot name on screen
        GD.Print($"Updated hotspot '{hotspot.Id}' (name + look/use/talk) and saved to JSON.");
        ShowMessage("Saved. Enter = save again, Esc = close.");
    }

    /// <summary>Called when user confirms exit edit dialog. Updates exit id, target room, spawn; saves to JSON.</summary>
    private void OnExitEditDialogConfirmed(int exitIndex, string id, string targetRoomId, string spawnXStr, string spawnYStr)
    {
        if (_roomData?.Exits == null || exitIndex < 0 || exitIndex >= _roomData.Exits.Length)
            return;
        var exit = _roomData.Exits[exitIndex];
        if (!string.IsNullOrWhiteSpace(id))
            exit.Id = id.Trim();
        if (!string.IsNullOrWhiteSpace(targetRoomId))
            exit.TargetRoomId = targetRoomId.Trim();
        if (exit.TargetSpawn == null)
            exit.TargetSpawn = new Vector2Data();
        if (float.TryParse(spawnXStr, out float sx))
            exit.TargetSpawn.X = sx;
        if (float.TryParse(spawnYStr, out float sy))
            exit.TargetSpawn.Y = sy;
        _hasUnsavedChanges = true;
        SaveRoomData();
        if (_debugDrawNode != null) _debugDrawNode.QueueRedraw();
        GD.Print($"Updated exit '{exit.Id}' and saved to JSON.");
        ShowMessage("Saved. Enter = save again, Esc = close.");
    }
    
    /// <summary>Check if ego has entered an exit zone and trigger room transition</summary>
    private void CheckExitCollision()
    {
        if (_roomData?.Exits == null || _ego == null)
            return;
            
        // Don't check exits while cooldown is active
        if (_exitCooldownTimer > 0)
            return;
            
        // Get ego's base position
        Vector2 egoBasePos = GetEgoBasePosition();
        Vector2I egoPoint = new Vector2I((int)egoBasePos.X, (int)egoBasePos.Y);
        
        bool inAnyExit = false;
        
        foreach (var exit in _roomData.Exits)
        {
            if (IsPointInRect(egoPoint, exit.Rect))
            {
                inAnyExit = true;
                
                // Only trigger if this is a different exit than the last one
                // or if we haven't triggered any exit recently
                if (_lastExitTriggered != exit.Id)
                {
                    GD.Print($"Ego entered exit: {exit.Id} -> {exit.TargetRoomId}");
                    
                    // Check if target room exists
                    string targetRoomPath = $"res://RoomPackages/{exit.TargetRoomId}/room.json";
                    if (!FileAccess.FileExists(targetRoomPath))
                    {
                        ShowMessage($"Can't go that way (room '{exit.TargetRoomId}' not found).");
                        _lastExitTriggered = exit.Id;
                        _exitCooldownTimer = 2.0; // 2 second cooldown
                        return;
                    }
                    
                    // Trigger room transition
                    ShowMessage($"Transitioning to room: {exit.TargetRoomId}");
                    _lastExitTriggered = exit.Id;
                    _exitCooldownTimer = 2.0; // 2 second cooldown to prevent re-triggering
                    
                    // TODO: Implement full room loading with target spawn point
                    // LoadRoom($"res://RoomPackages/{exit.TargetRoomId}/room.json");
                    // SpawnEgoAt(exit.TargetSpawn);
                }
                
                break; // Only process one exit at a time
            }
        }
        
        // If not in any exit, clear the last triggered exit
        if (!inAnyExit)
        {
            _lastExitTriggered = "";
        }
    }
    
    /// <summary>Check if click hits an exit (DEPRECATED - exits now trigger on walk-over)</summary>
    private bool CheckExits(Vector2I clickPoint)
    {
        if (_roomData?.Exits == null)
            return false;
            
        foreach (var exit in _roomData.Exits)
        {
            if (IsPointInRect(clickPoint, exit.Rect))
            {
                GD.Print($"Clicked exit: {exit.Id} -> {exit.TargetRoomId}");
                
                // Check if target room exists
                string targetRoomPath = $"res://RoomPackages/{exit.TargetRoomId}/room.json";
                if (!FileAccess.FileExists(targetRoomPath))
                {
                    ShowMessage($"Can't go that way (room '{exit.TargetRoomId}' not found).");
                    return true;
                }
                
                // Load target room (simplified for now - just reload same room)
                ShowMessage($"Would transition to room: {exit.TargetRoomId}");
                // TODO: Implement room loading with target spawn point
                
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>Handle click-to-walk (fallback when not clicking hotspot/exit)</summary>
    private void HandleClickToWalk(Vector2I clickPoint)
    {
        bool walkableAtClick = IsWalkable(clickPoint);

        Vector2I clampedTarget = clickPoint;
        if (!walkableAtClick)
        {
            clampedTarget = ClampToNearestWalkable(clickPoint);
            if (!IsWalkable(clampedTarget))
                return; // Could not find walkable position
        }

        _lastRequestedTarget = clickPoint;
        _lastClampedTarget = clampedTarget;
        _hasDebugTargets = true;

        if (_debugDrawNode != null)
            _debugDrawNode.QueueRedraw();
        
        if (_ego != null)
        {
            Vector2 egoBase = GetEgoBasePosition();
            Vector2 targetBase = new Vector2(clampedTarget.X, clampedTarget.Y);
            List<Vector2> path = ComputePath(egoBase, targetBase);
            if (path != null && path.Count > 0)
                _ego.SetPath(path);
            else
                _ego.SetTarget(targetBase);
        }
    }
    
    /// <summary>Helper to check if point is in rect</summary>
    private bool IsPointInRect(Vector2I point, RectData rect)
    {
        return point.X >= rect.X && point.X < rect.X + rect.W &&
               point.Y >= rect.Y && point.Y < rect.Y + rect.H;
    }

    /// <summary>Check if point is inside polygon (ray-cast). Points in base coords.</summary>
    private static bool IsPointInPolygon(Vector2I point, Vector2Data[] points)
    {
        if (points == null || points.Length < 3) return false;
        float px = point.X;
        float py = point.Y;
        bool inside = false;
        int n = points.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = points[i].X, yi = points[i].Y;
            float xj = points[j].X, yj = points[j].Y;
            if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Get polygon points for hotspot (always returns Points or builds from Rect).</summary>
    private static Vector2Data[] GetHotspotPoints(HotspotData hotspot)
    {
        if (hotspot.Points != null && hotspot.Points.Length >= 3) return hotspot.Points;
        if (hotspot.Rect != null) return RectToPoints(hotspot.Rect);
        return null;
    }

    private static Vector2Data[] RectToPoints(RectData r)
    {
        return new[]
        {
            new Vector2Data { X = r.X, Y = r.Y },
            new Vector2Data { X = r.X + r.W, Y = r.Y },
            new Vector2Data { X = r.X + r.W, Y = r.Y + r.H },
            new Vector2Data { X = r.X, Y = r.Y + r.H }
        };
    }

    /// <summary>Update Rect to bounding box of Points (for serialization compat).</summary>
    private static void UpdateHotspotRectFromPoints(HotspotData hotspot)
    {
        var pts = hotspot.Points;
        if (pts == null || pts.Length == 0) return;
        float minX = pts[0].X, minY = pts[0].Y, maxX = pts[0].X, maxY = pts[0].Y;
        for (int i = 1; i < pts.Length; i++)
        {
            minX = Mathf.Min(minX, pts[i].X);
            minY = Mathf.Min(minY, pts[i].Y);
            maxX = Mathf.Max(maxX, pts[i].X);
            maxY = Mathf.Max(maxY, pts[i].Y);
        }
        hotspot.Rect = new RectData
        {
            X = Mathf.FloorToInt(minX),
            Y = Mathf.FloorToInt(minY),
            W = Mathf.CeilToInt(maxX - minX),
            H = Mathf.CeilToInt(maxY - minY)
        };
    }

    /// <summary>After load: ensure every hotspot has Points (from Rect if needed).</summary>
    private void NormalizeHotspotPoints()
    {
        if (_roomData?.Hotspots == null) return;
        foreach (var h in _roomData.Hotspots)
        {
            if (h.Points == null && h.Rect != null)
                h.Points = RectToPoints(h.Rect);
        }
    }

    private bool IsPointInHotspot(Vector2I point, HotspotData hotspot)
    {
        var pts = GetHotspotPoints(hotspot);
        return pts != null && IsPointInPolygon(point, pts);
    }

    private static string GetHotspotVerbDescription(HotspotData hotspot, string verb)
    {
        if (hotspot?.Verbs == null) return "";
        VerbActionData action = verb switch
        {
            "look" => hotspot.Verbs.Look,
            "use" => hotspot.Verbs.Use,
            "talk" => hotspot.Verbs.Talk,
            _ => null
        };
        return action?.Value?.Trim() ?? "";
    }

    private const float VertexHandleRadiusBase = 8f;
    private static int GetVertexAtPoint(Vector2I clickBase, Vector2Data[] points)
    {
        if (points == null) return -1;
        float cx = clickBase.X, cy = clickBase.Y;
        for (int i = 0; i < points.Length; i++)
        {
            float dx = cx - (float)points[i].X;
            float dy = cy - (float)points[i].Y;
            if (dx * dx + dy * dy <= VertexHandleRadiusBase * VertexHandleRadiusBase)
                return i;
        }
        return -1;
    }

    private static int GetEdgeAtPoint(Vector2I clickBase, Vector2Data[] points, float maxDistBase = 28f)
    {
        if (points == null || points.Length < 2) return -1;
        float px = clickBase.X, py = clickBase.Y;
        int n = points.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float ax = (float)points[j].X, ay = (float)points[j].Y;
            float bx = (float)points[i].X, by = (float)points[i].Y;
            float t = Mathf.Clamp(((px - ax) * (bx - ax) + (py - ay) * (by - ay)) / (Mathf.Max(0.001f, (bx - ax) * (bx - ax) + (by - ay) * (by - ay))), 0f, 1f);
            float projX = ax + t * (bx - ax);
            float projY = ay + t * (by - ay);
            float distSq = (px - projX) * (px - projX) + (py - projY) * (py - projY);
            if (distSq <= maxDistBase * maxDistBase)
                return i;
        }
        return -1;
    }
    
    /// <summary>True if any message box or edit dialog is open; room clicks should be ignored so editing isn't disrupted.</summary>
    private bool IsAnyInputOverlayVisible()
    {
        return (_messageBox != null && _messageBox.IsVisible) ||
               (_hotspotEditDialog != null && _hotspotEditDialog.IsVisible) ||
               (_exitEditDialog != null && _exitEditDialog.IsVisible) ||
               (_textInputDialog != null && _textInputDialog.IsVisible);
    }

    /// <summary>Show a message in the message box</summary>
    private void ShowMessage(string message)
    {
        if (_messageBox != null)
        {
            _messageBox.Show(message);
        }
        else
        {
            GD.Print($"[MESSAGE] {message}");
        }
    }

    // Coordinate system: all gameplay and editing use "base" coords from room.json (BaseSize). Origin top-left; range [0, BaseSize.W) x [0, BaseSize.H). Widescreen = use a wider BaseSize in room.json; no code changes needed.

    /// <summary>Get mouse position in RoomRuntime local space (matches drawn content and input).</summary>
    private Vector2 GetMouseLocalPosition()
    {
        return GetLocalMousePosition();
    }

    /// <summary>Convert screen pixel position (room local) to base room coordinates. Valid for any BaseSize (including widescreen).</summary>
    public Vector2 ScreenToRoomBase(Vector2 screenPos)
    {
        return screenPos / RenderScale;
    }

    /// <summary>Convert base room coordinates to screen pixel position.</summary>
    public Vector2 RoomBaseToScreen(Vector2 basePos)
    {
        return basePos * RenderScale;
    }

    /// <summary>Scale room to base dimensions (from room.json BaseSize; works for 4:3 or widescreen). Fit viewport, center X, anchor bottom, black bars.</summary>
    private void UpdateRoomViewportLayout()
    {
        if (_roomData == null)
            return;
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        int baseW = _roomData.BaseSize.W;
        int baseH = _roomData.BaseSize.H;
        float scale = Mathf.Min(viewportSize.X / baseW, viewportSize.Y / baseH);
        _roomData.RenderScale = scale;
        float drawW = baseW * scale;
        float drawH = baseH * scale;
        Position = new Vector2((viewportSize.X - drawW) / 2f, viewportSize.Y - drawH);
        _lastViewportSize = viewportSize;

        if (_backgroundSprite != null && _backgroundSprite.Texture != null)
        {
            float sx = drawW / _backgroundSprite.Texture.GetWidth();
            float sy = drawH / _backgroundSprite.Texture.GetHeight();
            _backgroundSprite.Scale = new Vector2(sx, sy);
        }
        if (_foregroundSprite != null)
            _foregroundSprite.Scale = new Vector2(scale, scale);
        if (_ego != null)
            _ego.SetRenderScale(scale);
        if (_controlDebugSprite != null)
            _controlDebugSprite.Scale = new Vector2(scale, scale);
        if (_priorityDebugSprite != null)
            _priorityDebugSprite.Scale = new Vector2(scale, scale);
        var uiBar = GetTree().Root.GetNodeOrNull<UIBar>("GameMain/UIRoot/UIBar");
        if (uiBar != null)
            uiBar.SetRenderScale(scale);
    }

    public void LoadRoom(string roomPackagePath)
    {
        _roomPackagePath = roomPackagePath;

        // Load room.json
        string jsonPath = roomPackagePath;
        if (!FileAccess.FileExists(jsonPath))
        {
            GD.PrintErr($"Room JSON not found: {jsonPath}");
            return;
        }

        using var file = FileAccess.Open(jsonPath, FileAccess.ModeFlags.Read);
        string jsonText = file.GetAsText();
        
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _roomData = JsonSerializer.Deserialize<RoomData>(jsonText, options);
        NormalizeHotspotPoints();

        GD.Print($"Loaded room: {_roomData.DisplayName} (ID: {_roomData.Id})");
        GD.Print($"Base size: {_roomData.BaseSize.W}x{_roomData.BaseSize.H}");

        // Scale to fit viewport at base dimensions (320x190 etc.), center X, anchor bottom, black bars
        UpdateRoomViewportLayout();

        // Load control map for walkability
        LoadControlMap(roomPackagePath);
        
        // Load priority map for depth sorting
        LoadPriorityMap(roomPackagePath);

        // Load and display background
        LoadBackground(roomPackagePath);
        
        // Create foreground layer from priority map
        CreateForegroundLayer();
        
        // Spawn ego
        SpawnEgo();
        
        // Update ego priority immediately after spawning
        UpdateEgoPriority();
    }

    private void LoadBackground(string roomPackagePath)
    {
        string basePath = roomPackagePath.GetBaseDir();
        string backdropFile = _roomData.Assets.PicBase; // Always use fallback
        string picBasePath = basePath.PathJoin(backdropFile);

        if (!FileAccess.FileExists(picBasePath))
        {
            GD.PrintErr($"Background image not found: {picBasePath}");
            return;
        }

        var texture = GD.Load<Texture2D>(picBasePath);
        if (texture == null)
        {
            GD.PrintErr($"Failed to load backdrop texture: {picBasePath}");
            return;
        }

        _backgroundSprite = new Sprite2D();
        _backgroundSprite.Texture = texture;
        _backgroundSprite.TextureFilter = TextureFilterEnum.Nearest; // Pixel-perfect filtering
        _backgroundSprite.Centered = false;
        _backgroundSprite.Position = Vector2.Zero;
        
        // Scale backdrop to fill target display size
        float targetWidth = _roomData.BaseSize.W * _roomData.RenderScale;
        float targetHeight = _roomData.BaseSize.H * _roomData.RenderScale;
        float scaleX = targetWidth / texture.GetWidth();
        float scaleY = targetHeight / texture.GetHeight();
        _backgroundSprite.Scale = new Vector2(scaleX, scaleY);
        
        _backgroundSprite.ZIndex = 0;
        
        AddChild(_backgroundSprite);
        
        GD.Print($"Background: {backdropFile}, scale: {_backgroundSprite.Scale}, Z-index: {_backgroundSprite.ZIndex}");
    }

    private void ToggleBackdrop()
    {
        if (_backgroundSprite == null || string.IsNullOrEmpty(_roomPackagePath)) return;

        string basePath = _roomPackagePath.GetBaseDir();
        string otherFile = _useLargeBackdrop ? "pic_base.png" : "pic_base_large.png";
        string otherPath = basePath.PathJoin(otherFile);

        if (!FileAccess.FileExists(otherPath))
        {
            GD.Print($"Backdrop not found, cannot toggle: {otherPath}");
            return;
        }

        _useLargeBackdrop = !_useLargeBackdrop;
        string backdropFile = _useLargeBackdrop ? "pic_base_large.png" : "pic_base.png";
        string picBasePath = basePath.PathJoin(backdropFile);

        var texture = GD.Load<Texture2D>(picBasePath);
        _backgroundSprite.Texture = texture;
        float targetWidth = _roomData.BaseSize.W * _roomData.RenderScale;
        float targetHeight = _roomData.BaseSize.H * _roomData.RenderScale;
        _backgroundSprite.Scale = new Vector2(targetWidth / texture.GetWidth(), targetHeight / texture.GetHeight());

        // Regenerate foreground layer so it samples from the new backdrop
        if (_foregroundSprite != null)
        {
            RemoveChild(_foregroundSprite);
            _foregroundSprite = null;
        }
        CreateForegroundLayer();

        GD.Print($"Backdrop toggled to: {backdropFile}");
    }
    
    private void CycleResolution()
    {
        // Cycle through resolution modes: 1x -> 6x -> 12x -> 1x
        _resolutionMode = (_resolutionMode + 1) % _resolutionWidths.Length;
        int targetWidth = _resolutionWidths[_resolutionMode];
        int targetHeight = _resolutionHeights[_resolutionMode];
        
        GD.Print($"=== CycleResolution START ===");
        GD.Print($"Target resolution: {targetWidth}x{targetHeight}");
        
        // Calculate render scale based on target resolution
        float newScale = (float)targetWidth / _roomData.BaseSize.W;
        
        // Update room data render scale
        _roomData.RenderScale = newScale;
        
        // Use fallback image and stretch it to fit
        string backdropFile = _roomData.Assets.PicBase;
        string basePath = _roomPackagePath.GetBaseDir();
        string picBasePath = basePath.PathJoin(backdropFile);
        
        if (FileAccess.FileExists(picBasePath))
        {
            var texture = GD.Load<Texture2D>(picBasePath);
            if (texture != null && _backgroundSprite != null)
            {
                _backgroundSprite.Texture = texture;
            }
        }
        
        // Update window size to exact target resolution
        var window = GetWindow();
        GD.Print($"Window before resize: Size={window.Size}, Mode={window.Mode}, Unresizable={window.Unresizable}");
        
        window.Mode = Window.ModeEnum.Windowed; // Ensure windowed mode
        
        // Reset constraints to allow any size
        window.MinSize = new Vector2I(0, 0);
        window.MaxSize = new Vector2I(0, 0); // 0 means unlimited
        
        // Now set the target size
        window.Size = new Vector2I(targetWidth, targetHeight);
        
        GD.Print($"Window after resize: Size={window.Size}, MinSize={window.MinSize}, MaxSize={window.MaxSize}");
        
        // Update background sprite scale to stretch to target size
        if (_backgroundSprite != null)
        {
            var texture = _backgroundSprite.Texture;
            _backgroundSprite.Scale = new Vector2((float)targetWidth / texture.GetWidth(), (float)targetHeight / texture.GetHeight());
        }
        
        // Regenerate foreground layer from the backdrop
        if (_foregroundSprite != null)
        {
            RemoveChild(_foregroundSprite);
            _foregroundSprite = null;
        }
        CreateForegroundLayer();
        
        // Reload control map (stretch to fit)
        if (_controlDebugSprite != null)
        {
            RemoveChild(_controlDebugSprite);
            _controlDebugSprite = null;
        }
        LoadControlMapForResolution(_resolutionMode);
        
        // Reload priority map (stretch to fit)
        if (_priorityDebugSprite != null)
        {
            RemoveChild(_priorityDebugSprite);
            _priorityDebugSprite = null;
        }
        LoadPriorityMapForResolution(_resolutionMode);
        
        // Update ego scale
        if (_ego != null)
        {
            _ego.SetRenderScale(newScale);
        }
        
        // Update room position (anchor to bottom)
        float yPosition = targetHeight - (float)targetHeight;
        Position = new Vector2(0, yPosition);
        
        // Update UI bar scale
        var uiBar = GetTree().Root.GetNode<UIBar>("GameMain/UIRoot/UIBar");
        if (uiBar != null)
        {
            uiBar.SetRenderScale(newScale);
        }
        
        // Show indicator for 3 seconds
        _resolutionIndicatorTimer = 3.0f;
        if (_debugDrawNode != null)
        {
            _debugDrawNode.QueueRedraw();
        }
        
        GD.Print($"Resolution changed to: {_resolutionNames[_resolutionMode]}, Scale: {newScale}x, Image: {backdropFile}");
        GD.Print($"=== CycleResolution END ===");
    }
    
    private void ForceWindowResize(int width, int height)
    {
        var window = GetWindow();
        GD.Print($"ForceWindowResize called: target={width}x{height}");
        window.Size = new Vector2I(width, height);
        GD.Print($"ForceWindowResize after: actual={window.Size}");
    }
    
    private string GetBackdropForResolution(int mode)
    {
        // Get the appropriate backdrop image based on resolution mode
        // Falls back to PicBase if specific resolution image is not defined
        switch (mode)
        {
            case 0: // 1x (320x200)
                return !string.IsNullOrEmpty(_roomData.Assets.PicBase1x) 
                    ? _roomData.Assets.PicBase1x 
                    : _roomData.Assets.PicBase;
            case 1: // 6x (1920x1140)
                return !string.IsNullOrEmpty(_roomData.Assets.PicBase6x) 
                    ? _roomData.Assets.PicBase6x 
                    : _roomData.Assets.PicBase;
            case 2: // 12x (3840x2280)
                return !string.IsNullOrEmpty(_roomData.Assets.PicBase12x) 
                    ? _roomData.Assets.PicBase12x 
                    : _roomData.Assets.PicBase;
            default:
                return _roomData.Assets.PicBase;
        }
    }
    
    private string GetControlMapForResolution(int mode)
    {
        if (_roomData?.Assets?.Control != null)
            return _roomData.Assets.Control;
        return "control.png";
    }
    
    private string GetPriorityMapForResolution(int mode)
    {
        // For now, always use the fallback priority map and stretch it
        return _roomData.Assets.Priority;
    }
    
    private void LoadControlMap(string roomPackagePath)
    {
        LoadControlMapForResolution(_resolutionMode);
    }
    
    private void LoadControlMapForResolution(int resMode)
    {
        string basePath = _roomPackagePath.GetBaseDir();
        string controlFile = GetControlMapForResolution(resMode);
        string controlPath = basePath.PathJoin(controlFile);

        if (!FileAccess.FileExists(controlPath))
        {
            GD.PrintErr($"Control map not found: {controlPath}");
            // Create a default all-walkable control map
            CreateDefaultControlMap();
            return;
        }

        // Load as Image from JSON path so pixel sampling matches what we display (Texture2D.GetImage() can return wrong/empty data for imported PNGs)
        _controlImage = Image.LoadFromFile(controlPath);
        if (_controlImage == null)
        {
            var texture = GD.Load<Texture2D>(controlPath);
            if (texture != null)
                _controlImage = texture.GetImage();
        }
        if (_controlImage == null || _controlImage.GetWidth() == 0 || _controlImage.GetHeight() == 0)
        {
            GD.PrintErr($"Failed to load control map image: {controlPath}");
            CreateDefaultControlMap();
            return;
        }

        // Ensure format gives reliable GetPixel() values (0-1 RGB)
        if (_controlImage.GetFormat() != Image.Format.Rgba8)
        {
            _controlImage.Convert(Image.Format.Rgba8);
            GD.Print($"Control map converted to Rgba8 for sampling");
        }

        // Verify control map size matches base size
        if (_controlImage.GetWidth() != _roomData.BaseSize.W || _controlImage.GetHeight() != _roomData.BaseSize.H)
        {
            GD.PrintErr($"ERROR: Control map size ({_controlImage.GetWidth()}x{_controlImage.GetHeight()}) " +
                       $"does NOT match baseSize ({_roomData.BaseSize.W}x{_roomData.BaseSize.H})!");
            GD.PrintErr($"Walkability checks will be INCORRECT. Please resize {controlFile} to {_roomData.BaseSize.W}x{_roomData.BaseSize.H}");
        }
        
        _isWalkableDebugCount = 0; // Reset so next 5 IsWalkable calls are logged
        BuildPathfindingGraph();
        GD.Print($"Control map loaded: {controlFile} ({_controlImage.GetWidth()}x{_controlImage.GetHeight()})");
        GD.Print($"Walkability rule: luma >= {_roomData.ControlRule.WalkableIfLumaGte}, invert: {_roomData.ControlRule.Invert}");
        
        // Create debug sprite for the control map
        var controlTexture = ImageTexture.CreateFromImage(_controlImage);
        _controlDebugSprite = new Sprite2D();
        _controlDebugSprite.Texture = controlTexture;
        _controlDebugSprite.TextureFilter = TextureFilterEnum.Nearest;
        _controlDebugSprite.Centered = false;
        _controlDebugSprite.Position = Vector2.Zero;
        _controlDebugSprite.Scale = new Vector2(_roomData.RenderScale, _roomData.RenderScale);
        _controlDebugSprite.Modulate = new Color(1, 1, 1, 0.5f);
        _controlDebugSprite.Visible = false; // Start hidden; F2 toggles
        _controlDebugSprite.ZIndex = 1000;
        AddChild(_controlDebugSprite);

        GD.Print($"Control debug sprite created with Z-index 1000");
    }

    private void CreateDefaultControlMap()
    {
        // Create a simple test control map - all white (walkable)
        _controlImage = Image.CreateEmpty(_roomData.BaseSize.W, _roomData.BaseSize.H, false, Image.Format.Rgb8);
        _controlImage.Fill(Colors.White);
        
        // Create debug sprite even for default control map
        var controlTexture = ImageTexture.CreateFromImage(_controlImage);
        _controlDebugSprite = new Sprite2D();
        _controlDebugSprite.Texture = controlTexture;
        _controlDebugSprite.TextureFilter = TextureFilterEnum.Nearest;
        _controlDebugSprite.Centered = false;
        _controlDebugSprite.Position = Vector2.Zero;
        _controlDebugSprite.Scale = new Vector2(_roomData.RenderScale, _roomData.RenderScale);
        _controlDebugSprite.Modulate = new Color(1, 1, 1, 0.5f);
        _controlDebugSprite.Visible = false;
        _controlDebugSprite.ZIndex = 1000;
        AddChild(_controlDebugSprite);

        _isWalkableDebugCount = 0;
        BuildPathfindingGraph();
        GD.Print($"Created default all-walkable control map: {_roomData.BaseSize.W}x{_roomData.BaseSize.H}");
    }

    private void BuildPathfindingGraph()
    {
        _astar = new AStar2D();
        if (_controlImage == null || _roomData?.ControlRule == null) return;
        int w = _roomData.BaseSize.W;
        int h = _roomData.BaseSize.H;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!IsWalkable(new Vector2I(x, y))) continue;
                int id = y * w + x;
                _astar.AddPoint(id, new Vector2(x + 0.5f, y + 0.5f), 1f);
            }
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!IsWalkable(new Vector2I(x, y))) continue;
                int id = y * w + x;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], ny = y + dy[d];
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    if (!IsWalkable(new Vector2I(nx, ny))) continue;
                    int nid = ny * w + nx;
                    if (!_astar.ArePointsConnected(id, nid))
                        _astar.ConnectPoints(id, nid, true);
                }
            }
    }

    /// <summary>Compute path (base coords). Returns simplified corner waypoints or empty.</summary>
    public List<Vector2> ComputePath(Vector2 fromBase, Vector2 toBase)
    {
        var result = new List<Vector2>();
        if (_roomData == null) return result;
        int w = _roomData.BaseSize.W;
        int h = _roomData.BaseSize.H;
        var fromCell = new Vector2I(Mathf.Clamp(Mathf.FloorToInt(fromBase.X), 0, w - 1), Mathf.Clamp(Mathf.FloorToInt(fromBase.Y), 0, h - 1));
        var toCell = new Vector2I(Mathf.Clamp(Mathf.FloorToInt(toBase.X), 0, w - 1), Mathf.Clamp(Mathf.FloorToInt(toBase.Y), 0, h - 1));
        if (_astar == null)
        {
            if (IsWalkable(toCell)) result.Add(toBase);
            return result;
        }
        if (!IsWalkable(fromCell)) fromCell = ClampToNearestWalkable(fromCell, 30);
        if (!IsWalkable(toCell)) toCell = ClampToNearestWalkable(toCell, 60);
        if (!IsWalkable(fromCell) || !IsWalkable(toCell)) return result;
        int fromId = fromCell.Y * w + fromCell.X;
        int toId = toCell.Y * w + toCell.X;
        if (!_astar.HasPoint(fromId) || !_astar.HasPoint(toId)) return result;
        if (fromId == toId) { result.Add(toBase); return result; }
        var pointPath = _astar.GetPointPath(fromId, toId);
        if (pointPath == null || pointPath.Length == 0) return result;
        for (int i = 0; i < pointPath.Length; i++)
            result.Add(pointPath[i]);
        if (result.Count > 0) result[result.Count - 1] = toBase;
        return SimplifyPath(result);
    }

    /// <summary>Keep only corners (direction changes) so fewer waypoints = smoother animation.</summary>
    private static List<Vector2> SimplifyPath(List<Vector2> path)
    {
        if (path == null || path.Count <= 2) return path;
        var outList = new List<Vector2> { path[0] };
        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector2 d1 = (path[i] - path[i - 1]).Normalized();
            Vector2 d2 = (path[i + 1] - path[i]).Normalized();
            if (d1.Dot(d2) < 0.94f) outList.Add(path[i]); // only keep sharper corners (~20°+) to reduce corner waypoints
        }
        outList.Add(path[path.Count - 1]);
        return outList;
    }
    
    private void LoadPriorityMap(string roomPackagePath)
    {
        LoadPriorityMapForResolution(_resolutionMode);
    }
    
    private void LoadPriorityMapForResolution(int resMode)
    {
        string basePath = _roomPackagePath.GetBaseDir();
        string priorityFile = GetPriorityMapForResolution(resMode);
        string priorityPath = basePath.PathJoin(priorityFile);

        if (!FileAccess.FileExists(priorityPath))
        {
            GD.PrintErr($"Priority map not found: {priorityPath}");
            // Create a default priority map (all white = max priority)
            CreateDefaultPriorityMap();
            return;
        }

        var texture = GD.Load<Texture2D>(priorityPath);
        if (texture == null)
        {
            GD.PrintErr($"Failed to load priority texture: {priorityPath}");
            CreateDefaultPriorityMap();
            return;
        }
        
        _priorityImage = texture.GetImage();
        
        if (_priorityImage.GetWidth() == 0 || _priorityImage.GetHeight() == 0)
        {
            GD.PrintErr($"Priority map is empty, creating default");
            CreateDefaultPriorityMap();
            return;
        }
        
        // Verify priority map size matches base size
        if (_priorityImage.GetWidth() != _roomData.BaseSize.W || _priorityImage.GetHeight() != _roomData.BaseSize.H)
        {
            GD.PrintErr($"ERROR: Priority map size ({_priorityImage.GetWidth()}x{_priorityImage.GetHeight()}) " +
                       $"does NOT match baseSize ({_roomData.BaseSize.W}x{_roomData.BaseSize.H})!");
            GD.PrintErr($"Priority sorting will be INCORRECT. Please resize {priorityFile} to {_roomData.BaseSize.W}x{_roomData.BaseSize.H}");
        }
        
        GD.Print($"Priority map loaded: {priorityFile} ({_priorityImage.GetWidth()}x{_priorityImage.GetHeight()})");
        
        // Create debug sprite for the priority map
        var priorityTexture = ImageTexture.CreateFromImage(_priorityImage);
        _priorityDebugSprite = new Sprite2D();
        _priorityDebugSprite.Texture = priorityTexture;
        _priorityDebugSprite.TextureFilter = TextureFilterEnum.Nearest;
        _priorityDebugSprite.Centered = false;
        _priorityDebugSprite.Position = Vector2.Zero;
        _priorityDebugSprite.Scale = new Vector2(_roomData.RenderScale, _roomData.RenderScale);
        _priorityDebugSprite.Modulate = new Color(1, 1, 1, 0.5f);
        _priorityDebugSprite.Visible = false;
        _priorityDebugSprite.ZIndex = 1000; // Render on top of everything for debugging
        AddChild(_priorityDebugSprite);
        
        GD.Print($"Priority debug sprite created with Z-index 1000");
    }
    
    private void CreateDefaultPriorityMap()
    {
        // Create a simple test priority map - all white (max priority)
        _priorityImage = Image.CreateEmpty(_roomData.BaseSize.W, _roomData.BaseSize.H, false, Image.Format.Rgb8);
        _priorityImage.Fill(Colors.White);
        
        GD.Print($"Created default priority map: {_roomData.BaseSize.W}x{_roomData.BaseSize.H}");
        
        // Create debug sprite even for default priority map
        var priorityTexture = ImageTexture.CreateFromImage(_priorityImage);
        _priorityDebugSprite = new Sprite2D();
        _priorityDebugSprite.Texture = priorityTexture;
        _priorityDebugSprite.TextureFilter = TextureFilterEnum.Nearest;
        _priorityDebugSprite.Centered = false;
        _priorityDebugSprite.Position = Vector2.Zero;
        _priorityDebugSprite.Scale = new Vector2(_roomData.RenderScale, _roomData.RenderScale);
        _priorityDebugSprite.Modulate = new Color(1, 1, 1, 0.5f);
        _priorityDebugSprite.Visible = false;
        _priorityDebugSprite.ZIndex = 1000; // Render on top of everything for debugging
        AddChild(_priorityDebugSprite);
    }
    
    /// <summary>Create a foreground layer that masks background based on priority</summary>
    private void CreateForegroundLayer()
    {
        if (_backgroundSprite == null || _priorityImage == null || _roomData == null)
            return;
            
        // Get the background texture
        var bgTexture = _backgroundSprite.Texture;
        if (bgTexture == null)
            return;
            
        var bgImage = bgTexture.GetImage();
        int bgW = bgImage.GetWidth();
        int bgH = bgImage.GetHeight();
        // Map base coords to backdrop pixels (handles both low-res and high-res backdrops)
        float scaleX = (float)bgW / _roomData.BaseSize.W;
        float scaleY = (float)bgH / _roomData.BaseSize.H;

        // Create a new image for the foreground (parts with high priority)
        var foregroundImage = Image.CreateEmpty(_roomData.BaseSize.W, _roomData.BaseSize.H, false, Image.Format.Rgba8);

        // Priority threshold - pixels above this will appear in front of ego
        float priorityThreshold = 0.5f; // 128/255 = 0.5

        // Copy pixels from background where priority > threshold
        for (int y = 0; y < _roomData.BaseSize.H; y++)
        {
            for (int x = 0; x < _roomData.BaseSize.W; x++)
            {
                Color priorityPixel = _priorityImage.GetPixel(x, y);
                float luma = priorityPixel.R * 0.299f + priorityPixel.G * 0.587f + priorityPixel.B * 0.114f;

                if (luma > priorityThreshold)
                {
                    // High priority - sample backdrop at correct coords for its resolution
                    int bx = Mathf.Clamp(Mathf.FloorToInt(x * scaleX), 0, bgW - 1);
                    int by = Mathf.Clamp(Mathf.FloorToInt(y * scaleY), 0, bgH - 1);
                    Color bgPixel = bgImage.GetPixel(bx, by);
                    foregroundImage.SetPixel(x, y, bgPixel);
                }
                else
                {
                    // Low priority - make transparent
                    foregroundImage.SetPixel(x, y, new Color(0, 0, 0, 0));
                }
            }
        }
        
        // Create foreground sprite
        var foregroundTexture = ImageTexture.CreateFromImage(foregroundImage);
        _foregroundSprite = new Sprite2D();
        _foregroundSprite.Texture = foregroundTexture;
        _foregroundSprite.TextureFilter = TextureFilterEnum.Nearest;
        _foregroundSprite.Centered = false;
        _foregroundSprite.Position = Vector2.Zero;
        _foregroundSprite.Scale = new Vector2(_roomData.RenderScale, _roomData.RenderScale);
        _foregroundSprite.ZIndex = 256; // Render in front of ego (ego max is 255)
        AddChild(_foregroundSprite);
        
        GD.Print($"Foreground layer created with Z-index 256");
    }
    
    /// <summary>Sample priority map at ego's foot position and update Z-index</summary>
    private void UpdateEgoPriority()
    {
        if (_ego == null || _priorityImage == null || _roomData == null)
            return;
        
        // Get ego position in base coords
        Vector2 basePos = _ego.BasePosition;
        Vector2I egoBasePos = new Vector2I(Mathf.FloorToInt(basePos.X), Mathf.FloorToInt(basePos.Y));
        
        // Apply foot offset from room.json
        Vector2I footSampleCoord = new Vector2I(
            egoBasePos.X,
            egoBasePos.Y + _roomData.FootOffsetY
        );
        
        // Store for debug display
        _egoFootSampleCoord = footSampleCoord;
        
        // Clamp to valid bounds
        footSampleCoord.X = Mathf.Clamp(footSampleCoord.X, 0, _roomData.BaseSize.W - 1);
        footSampleCoord.Y = Mathf.Clamp(footSampleCoord.Y, 0, _roomData.BaseSize.H - 1);
        
        // Sample priority map
        Color pixel = _priorityImage.GetPixel(footSampleCoord.X, footSampleCoord.Y);
        
        // Calculate luminance (0..1)
        float luma = pixel.R * 0.299f + pixel.G * 0.587f + pixel.B * 0.114f;
        
        // Map to 0..255 priority value
        int priorityValue = Mathf.RoundToInt(luma * 255.0f);
        
        // Store for debug display
        _egoPriorityValue = priorityValue;
        
        // Set ego's Z-index
        _ego.ZIndex = priorityValue;
        
        // Debug log the first few times to see what's happening
        GD.Print($"Ego priority: footCoord=({footSampleCoord.X},{footSampleCoord.Y}), luma={luma:F3}, priority={priorityValue}, Z-index={_ego.ZIndex}");
    }
    
    
    private static int _isWalkableDebugCount = 0;

    /// <summary>Check if a base coordinate is walkable</summary>
    public bool IsWalkable(Vector2I pBase)
    {
        if (_controlImage == null || _roomData == null)
        {
            if (_isWalkableDebugCount < 2) GD.Print($"[IsWalkable] early return: walkable=true (controlImage null={_controlImage == null}, roomData null={_roomData == null})");
            _isWalkableDebugCount++;
            return true; // Default to walkable if no control map
        }
        if (_roomData.ControlRule == null)
        {
            if (_isWalkableDebugCount < 2) GD.Print($"[IsWalkable] early return: walkable=true (ControlRule is null)");
            _isWalkableDebugCount++;
            return true;
        }

        // Check bounds
        if (pBase.X < 0 || pBase.Y < 0 || pBase.X >= _roomData.BaseSize.W || pBase.Y >= _roomData.BaseSize.H)
            return false;

        // Sample within actual image dimensions (image may differ from baseSize)
        int iw = _controlImage.GetWidth();
        int ih = _controlImage.GetHeight();
        int sx = Mathf.Clamp(pBase.X, 0, iw - 1);
        int sy = Mathf.Clamp(pBase.Y, 0, ih - 1);

        // Get pixel color from control map
        Color pixel = _controlImage.GetPixel(sx, sy);

        // Calculate luminance (standard formula)
        float luma = pixel.R * 0.299f + pixel.G * 0.587f + pixel.B * 0.114f;

        // Check against threshold
        bool walkable = luma >= _roomData.ControlRule.WalkableIfLumaGte;

        // Apply invert flag
        if (_roomData.ControlRule.Invert)
            walkable = !walkable;

        if (_isWalkableDebugCount < 5)
        {
            GD.Print($"[IsWalkable] pBase=({pBase.X},{pBase.Y}) sample=({sx},{sy}) pixel=({pixel.R:F2},{pixel.G:F2},{pixel.B:F2}) luma={luma:F3} thresh={_roomData.ControlRule.WalkableIfLumaGte} invert={_roomData.ControlRule.Invert} => walkable={walkable}");
            _isWalkableDebugCount++;
        }

        return walkable;
    }
    
    /// <summary>Check walkability (same as IsWalkable; use for breakpoint/debugging).</summary>
    public bool IsWalkableDebug(Vector2I pBase)
    {
        return IsWalkable(pBase);
    }
    
    /// <summary>Find nearest walkable position using spiral search</summary>
    public Vector2I ClampToNearestWalkable(Vector2I pBase, int maxRadius = 60)
    {
        // If already walkable, return as-is
        if (IsWalkable(pBase))
            return pBase;
            
        // Spiral search outward
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            // Check points in a square spiral at this radius
            for (int i = 0; i < radius * 8; i++)
            {
                Vector2I testPos = GetSpiralPosition(pBase, radius, i);
                
                // Check bounds
                if (testPos.X >= 0 && testPos.Y >= 0 && 
                    testPos.X < _roomData.BaseSize.W && testPos.Y < _roomData.BaseSize.H)
                {
                    if (IsWalkable(testPos))
                    {
                        return testPos;
                    }
                }
            }
        }
        
        // If no walkable position found, return original (shouldn't happen normally)
        GD.PrintErr($"No walkable position found near {pBase} within radius {maxRadius}");
        return pBase;
    }
    
    private Vector2I GetSpiralPosition(Vector2I center, int radius, int index)
    {
        // Generate positions in a square spiral pattern
        int perimeter = radius * 8;
        int sideLength = radius * 2;
        
        if (index < sideLength)
        {
            // Top edge (left to right)
            return new Vector2I(center.X - radius + index, center.Y - radius);
        }
        else if (index < sideLength * 2)
        {
            // Right edge (top to bottom)
            int offset = index - sideLength;
            return new Vector2I(center.X + radius, center.Y - radius + offset);
        }
        else if (index < sideLength * 3)
        {
            // Bottom edge (right to left)
            int offset = index - sideLength * 2;
            return new Vector2I(center.X + radius - offset, center.Y + radius);
        }
        else
        {
            // Left edge (bottom to top)
            int offset = index - sideLength * 3;
            return new Vector2I(center.X - radius, center.Y + radius - offset);
        }
    }

    private void SpawnEgo()
    {
        var egoScene = GD.Load<PackedScene>("res://Game/Rooms/Ego.tscn");
        _ego = egoScene.Instantiate<Ego>();
        
        // Tell ego the render scale first
        _ego.SetRenderScale(_roomData.RenderScale);
        
        // Set position in scaled coords (base * renderScale)
        Vector2 baseSpawnPos = new Vector2(_roomData.Spawn.X, _roomData.Spawn.Y);
        _ego.SetInitialBasePosition(baseSpawnPos);
        _ego.IsWalkableAtBase = IsWalkable;
        _ego.OnStuckRepath = (fromBase, toBase) =>
        {
            List<Vector2> path = ComputePath(fromBase, toBase);
            if (path != null && path.Count > 0) { _ego.SetPath(path); return true; }
            return false;
        };
        GD.Print($"Ego spawned at base coords: {baseSpawnPos}, screen coords: {_ego.Position}");
        AddChild(_ego);
    }

    public Vector2 GetMouseRoomCoords()
    {
        Vector2 screenPos = GetMouseLocalPosition();
        return ScreenToRoomBase(screenPos);
    }
    
    public bool GetMouseWalkable()
    {
        Vector2 mouseBase = GetMouseRoomCoords();
        Vector2I mouseBaseInt = new Vector2I((int)mouseBase.X, (int)mouseBase.Y);
        return IsWalkable(mouseBaseInt);
    }
    
    public Vector2I GetLastRequestedTarget() => _lastRequestedTarget;
    public Vector2I GetLastClampedTarget() => _lastClampedTarget;
    public bool HasDebugTargets() => _hasDebugTargets;
    
    public Vector2 GetEgoBasePosition()
    {
        if (_ego == null) return Vector2.Zero;
        return _ego.BasePosition;
    }
    
    // Priority debug info getters
    public Vector2I GetEgoFootSampleCoord() => _egoFootSampleCoord;
    public int GetEgoPriorityValue() => _egoPriorityValue;
    public int GetEgoZIndex() => _ego?.ZIndex ?? 0;
}