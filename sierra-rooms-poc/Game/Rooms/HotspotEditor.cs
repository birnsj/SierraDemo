using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SierraRooms.Game.Rooms;

/// <summary>Result of handling a click in F4/F5 mode. RoomRuntime uses this to open dialogs when Kind is Open*Dialog.</summary>
public class HotspotEditResult
{
    public enum Kind { None, Handled, OpenHotspotDialog, OpenExitDialog }
    public Kind ResultKind = Kind.None;
    public int Index = -1;
}

/// <summary>F4/F5 hotspot and exit editing: selection, move, resize, vertex edit, save. Draws overlay on request.</summary>
public class HotspotEditor
{
    private readonly RoomData _roomData;
    private readonly string _packagePath;
    private readonly Action<string> _showMessage;

    private int _selectedHotspotIndex = -1;
    private int _selectedExitIndex = -1;
    private bool _isResizing = false;
    private string _resizeHandle = "";
    private Vector2I _dragStartMouse = Vector2I.Zero;
    private Vector2 _dragStartMouseLocalPixels = Vector2.Zero;
    private RectData _dragStartRect;
    private int _draggingVertexIndex = -1;
    private int _selectedVertexIndex = -1;
    private bool _vertexDragPending; // Press on vertex; drag starts on first motion with button held
    private bool _hotspotDragPending; // Press on hotspot with Alt; drag starts on first motion with button held
    private Vector2 _lastMouseBase;   // For hotspot drag delta
    private Vector2 _dragStartVertexPosition;
    private double _lastHotspotClickTime;
    private Vector2I _lastHotspotClickPoint;

    private const float VertexHandleRadiusBase = 8f;
    private const float EdgeHitRadiusBase = 28f;

    public bool HasUnsavedChanges { get; private set; }
    public int SelectedHotspotIndex => _selectedHotspotIndex;
    public int SelectedExitIndex => _selectedExitIndex;
    public int SelectedVertexIndex => _selectedVertexIndex;

    public HotspotEditor(RoomData roomData, string packagePath, Action<string> showMessage)
    {
        _roomData = roomData ?? throw new ArgumentNullException(nameof(roomData));
        _packagePath = packagePath ?? "";
        _showMessage = showMessage ?? (_ => { });
    }

    public void ClearSelection()
    {
        _selectedHotspotIndex = -1;
        _selectedExitIndex = -1;
        _selectedVertexIndex = -1;
    }

    /// <summary>Handle left-click in editor mode. When isTextEditMode (F5) and click on hotspot/exit, returns OpenHotspotDialog/OpenExitDialog so RoomRuntime can open the dialog. altHeld: required to start moving hotspot (hold-and-drag).</summary>
    public HotspotEditResult TryHandleClick(Vector2I clickPoint, Vector2 clickLocalPixels, bool vertexMode, bool isTextEditMode, bool altHeld, Action queueRedraw)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        bool doubleClick = (now - _lastHotspotClickTime < 0.5) && (clickPoint - _lastHotspotClickPoint).LengthSquared() < 900;

        // F5 text mode: clicking hotspot/exit opens dialog; clicking empty adds default hotspot and opens dialog
        if (isTextEditMode)
        {
            if (_roomData.Hotspots != null)
            {
                for (int i = _roomData.Hotspots.Length - 1; i >= 0; i--)
                {
                    if (IsPointInHotspot(clickPoint, _roomData.Hotspots[i]))
                    {
                        _lastHotspotClickTime = now;
                        _lastHotspotClickPoint = clickPoint;
                        return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.OpenHotspotDialog, Index = i };
                    }
                }
            }
            if (_roomData.Exits != null)
            {
                for (int i = 0; i < _roomData.Exits.Length; i++)
                {
                    if (IsPointInRect(clickPoint, _roomData.Exits[i].Rect))
                    {
                        _lastHotspotClickTime = now;
                        _lastHotspotClickPoint = clickPoint;
                        return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.OpenExitDialog, Index = i };
                    }
                }
            }
            // F5 click on empty: add default "nothing here" hotspot at click and open its settings
            int newIdx = AddHotspotAtPoint(clickPoint, queueRedraw);
            _lastHotspotClickTime = now;
            _lastHotspotClickPoint = clickPoint;
            return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.OpenHotspotDialog, Index = newIdx };
        }

        // 1) Vertex mode (F4): vertex hit on any hotspot -> select that vertex (and hotspot); double-click edge -> add vertex
        if (vertexMode && _roomData.Hotspots != null)
        {
            // Check vertex hit on all hotspots first so clicking another vertex selects it (and its hotspot if different)
            for (int hi = 0; hi < _roomData.Hotspots.Length; hi++)
            {
                var hotspot = _roomData.Hotspots[hi];
                var pts = GetHotspotPoints(hotspot);
                if (pts == null) continue;
                int vi = GetVertexAtPoint(clickPoint, pts);
                if (vi >= 0)
                {
                    _selectedHotspotIndex = hi;
                    _selectedExitIndex = -1;
                    _selectedVertexIndex = vi;
                    _vertexDragPending = true;
                    _draggingVertexIndex = -1;
                    _dragStartVertexPosition = new Vector2((float)pts[vi].X, (float)pts[vi].Y);
                    _dragStartMouse = clickPoint;
                    _dragStartMouseLocalPixels = clickLocalPixels;
                    _lastHotspotClickTime = now;
                    _lastHotspotClickPoint = clickPoint;
                    queueRedraw?.Invoke();
                    return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.Handled };
                }
            }
            // Double-click edge to add vertex (only for selected hotspot)
            if (_selectedHotspotIndex >= 0 && _selectedHotspotIndex < _roomData.Hotspots.Length)
            {
                var hotspot = _roomData.Hotspots[_selectedHotspotIndex];
                var pts = GetHotspotPoints(hotspot);
                if (pts != null && doubleClick)
                {
                    int edgeIndex = GetEdgeAtPoint(clickPoint, pts);
                    if (edgeIndex >= 0)
                    {
                        AddVertexToHotspotEdge(_selectedHotspotIndex, edgeIndex, clickPoint);
                        _lastHotspotClickTime = 0;
                        queueRedraw?.Invoke();
                        return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.Handled };
                    }
                }
            }
        }

        // 2) Exit resize handles
        if (_selectedExitIndex >= 0 && _roomData.Exits != null && _selectedExitIndex < _roomData.Exits.Length)
        {
            var exit = _roomData.Exits[_selectedExitIndex];
            string handle = GetResizeHandle(clickPoint, exit.Rect);
            if (!string.IsNullOrEmpty(handle))
            {
                _isResizing = true;
                _resizeHandle = handle;
                _dragStartMouse = clickPoint;
                _dragStartMouseLocalPixels = clickLocalPixels;
                _dragStartRect = new RectData { X = exit.Rect.X, Y = exit.Rect.Y, W = exit.Rect.W, H = exit.Rect.H };
                queueRedraw?.Invoke();
                return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.Handled };
            }
        }

        // 3) Click on hotspot body
        if (_roomData.Hotspots != null)
        {
            for (int i = _roomData.Hotspots.Length - 1; i >= 0; i--)
            {
                var hotspot = _roomData.Hotspots[i];
                if (IsPointInHotspot(clickPoint, hotspot))
                {
                    _selectedVertexIndex = -1;
                    if (vertexMode)
                    {
                        if (_selectedHotspotIndex != i)
                        {
                            _selectedHotspotIndex = i;
                            _selectedExitIndex = -1;
                            GD.Print($"Selected hotspot: {hotspot.Id} (index {i})");
                        }
                        _lastHotspotClickTime = now;
                        _lastHotspotClickPoint = clickPoint;
                        queueRedraw?.Invoke();
                        return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.Handled };
                    }
                    var pts = GetHotspotPoints(hotspot);
                    if (_selectedHotspotIndex == i && doubleClick && pts != null)
                    {
                        int edgeIndex = GetEdgeAtPoint(clickPoint, pts);
                        if (edgeIndex >= 0)
                        {
                            AddVertexToHotspotEdge(i, edgeIndex, clickPoint);
                            _lastHotspotClickTime = 0;
                            queueRedraw?.Invoke();
                            return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.Handled };
                        }
                    }
                    // Move hotspot only when Alt is held; use hold-and-drag like vertices
                    if (altHeld)
                    {
                        _selectedHotspotIndex = i;
                        _selectedExitIndex = -1;
                        _hotspotDragPending = true;
                        _lastMouseBase = new Vector2(clickPoint.X, clickPoint.Y);
                    }
                    else
                    {
                        if (_selectedHotspotIndex != i)
                        {
                            _selectedHotspotIndex = i;
                            _selectedExitIndex = -1;
                            GD.Print($"Selected hotspot: {hotspot.Id} (index {i})");
                        }
                    }
                    _lastHotspotClickTime = now;
                    _lastHotspotClickPoint = clickPoint;
                    queueRedraw?.Invoke();
                    return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.Handled };
                }
            }
        }

        // 4) Click on exit body
        if (_roomData.Exits != null)
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
                    queueRedraw?.Invoke();
                    return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.Handled };
                }
            }
        }

        // 5) Clicked empty (F4 only): with Alt hold-and-drag hotspot; otherwise deselect
        if (!vertexMode && _selectedHotspotIndex >= 0 && _roomData.Hotspots != null && _selectedHotspotIndex < _roomData.Hotspots.Length)
        {
            if (altHeld)
            {
                _hotspotDragPending = true;
                _lastMouseBase = new Vector2(clickPoint.X, clickPoint.Y);
                queueRedraw?.Invoke();
                return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.Handled };
            }
        }
        if (!vertexMode && _selectedExitIndex >= 0 && _roomData.Exits != null && _selectedExitIndex < _roomData.Exits.Length)
        {
            MoveExitToPoint(_selectedExitIndex, clickPoint);
            queueRedraw?.Invoke();
            return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.Handled };
        }

        _selectedHotspotIndex = -1;
        _selectedExitIndex = -1;
        _selectedVertexIndex = -1;
        _lastHotspotClickTime = now;
        _lastHotspotClickPoint = clickPoint;
        queueRedraw?.Invoke();
        return new HotspotEditResult { ResultKind = HotspotEditResult.Kind.Handled };
    }

    /// <summary>Call each frame when mouse moves and we're dragging vertex or resizing. currentBase = mouse in room base coords, renderScale for resize delta. isLeftButtonPressed: when true and vertex was just pressed, start drag on first motion.</summary>
    public void HandleMotion(Vector2 currentScreenPos, Vector2 currentBase, float renderScale, bool isLeftButtonPressed, Action queueRedraw)
    {
        // Start vertex drag on first motion when user held the button down on a vertex.
        if (_vertexDragPending && isLeftButtonPressed)
        {
            _vertexDragPending = false;
            _draggingVertexIndex = _selectedVertexIndex;
        }
        if (_draggingVertexIndex >= 0)
        {
            HandleMotionVertexDrag(currentBase, queueRedraw);
            return;
        }
        // Hotspot drag: translate all points by mouse delta (hold-and-drag like vertices)
        if (_hotspotDragPending && isLeftButtonPressed && _selectedHotspotIndex >= 0 && _roomData.Hotspots != null && _selectedHotspotIndex < _roomData.Hotspots.Length)
        {
            Vector2 delta = currentBase - _lastMouseBase;
            _lastMouseBase = currentBase;
            var hotspot = _roomData.Hotspots[_selectedHotspotIndex];
            if (hotspot.Points != null)
            {
                for (int i = 0; i < hotspot.Points.Length; i++)
                {
                    float x = Mathf.Clamp((float)hotspot.Points[i].X + delta.X, 0, _roomData.BaseSize.W - 1);
                    float y = Mathf.Clamp((float)hotspot.Points[i].Y + delta.Y, 0, _roomData.BaseSize.H - 1);
                    hotspot.Points[i].X = x;
                    hotspot.Points[i].Y = y;
                }
                UpdateHotspotRectFromPoints(hotspot);
                HasUnsavedChanges = true;
            }
            queueRedraw?.Invoke();
            return;
        }
        if (_isResizing && _selectedExitIndex >= 0 && _roomData.Exits != null && _selectedExitIndex < _roomData.Exits.Length)
        {
            Vector2 deltaPixels = currentScreenPos - _dragStartMouseLocalPixels;
            HandleResizeDrag(deltaPixels, renderScale);
            queueRedraw?.Invoke();
        }
    }

    /// <summary>Apply resize delta (deltaPixels / renderScale -> base coords) to selected exit from _dragStartRect.</summary>
    private void HandleResizeDrag(Vector2 deltaPixels, float renderScale)
    {
        if (!_isResizing || _selectedExitIndex < 0 || _roomData.Exits == null || _selectedExitIndex >= _roomData.Exits.Length)
            return;
        if (renderScale <= 0) return;
        Vector2 deltaBase = deltaPixels / renderScale;
        Vector2I delta = new Vector2I((int)Mathf.Round(deltaBase.X), (int)Mathf.Round(deltaBase.Y));
        RectData newRect = new RectData { X = _dragStartRect.X, Y = _dragStartRect.Y, W = _dragStartRect.W, H = _dragStartRect.H };
        switch (_resizeHandle)
        {
            case "tl": newRect.X = _dragStartRect.X + delta.X; newRect.Y = _dragStartRect.Y + delta.Y; newRect.W = _dragStartRect.W - delta.X; newRect.H = _dragStartRect.H - delta.Y; break;
            case "tr": newRect.Y = _dragStartRect.Y + delta.Y; newRect.W = _dragStartRect.W + delta.X; newRect.H = _dragStartRect.H - delta.Y; break;
            case "bl": newRect.X = _dragStartRect.X + delta.X; newRect.W = _dragStartRect.W - delta.X; newRect.H = _dragStartRect.H + delta.Y; break;
            case "br": newRect.W = _dragStartRect.W + delta.X; newRect.H = _dragStartRect.H + delta.Y; break;
            case "t": newRect.Y = _dragStartRect.Y + delta.Y; newRect.H = _dragStartRect.H - delta.Y; break;
            case "b": newRect.H = _dragStartRect.H + delta.Y; break;
            case "l": newRect.X = _dragStartRect.X + delta.X; newRect.W = _dragStartRect.W - delta.X; break;
            case "r": newRect.W = _dragStartRect.W + delta.X; break;
        }
        if (newRect.W < 5) { if (_resizeHandle.Contains("l")) newRect.X = _dragStartRect.X + _dragStartRect.W - 5; newRect.W = 5; }
        if (newRect.H < 5) { if (_resizeHandle.Contains("t")) newRect.Y = _dragStartRect.Y + _dragStartRect.H - 5; newRect.H = 5; }
        newRect.X = Mathf.Clamp(newRect.X, 0, _roomData.BaseSize.W - newRect.W);
        newRect.Y = Mathf.Clamp(newRect.Y, 0, _roomData.BaseSize.H - newRect.H);
        _roomData.Exits[_selectedExitIndex].Rect = newRect;
        HasUnsavedChanges = true;
    }

    public void HandleMotionVertexDrag(Vector2 currentBase, Action queueRedraw)
    {
        if (_draggingVertexIndex < 0 || _selectedHotspotIndex < 0 || _roomData.Hotspots == null ||
            _selectedHotspotIndex >= _roomData.Hotspots.Length) return;
        var hotspot = _roomData.Hotspots[_selectedHotspotIndex];
        if (hotspot.Points == null || _draggingVertexIndex >= hotspot.Points.Length) return;
        float x = Mathf.Clamp(currentBase.X, 0, _roomData.BaseSize.W - 1);
        float y = Mathf.Clamp(currentBase.Y, 0, _roomData.BaseSize.H - 1);
        hotspot.Points[_draggingVertexIndex].X = x;
        hotspot.Points[_draggingVertexIndex].Y = y;
        UpdateHotspotRectFromPoints(hotspot);
        HasUnsavedChanges = true;
        queueRedraw?.Invoke();
    }

    public void HandleRelease(Action queueRedraw)
    {
        if (_draggingVertexIndex >= 0) _draggingVertexIndex = -1;
        _vertexDragPending = false;
        _hotspotDragPending = false;
        if (_isResizing) { _isResizing = false; _resizeHandle = ""; }
        queueRedraw?.Invoke();
    }

    public bool IsDraggingVertex => _draggingVertexIndex >= 0;
    /// <summary>True when user pressed on a vertex but hasn't moved yet; we need motion events to start drag.</summary>
    public bool VertexDragPending => _vertexDragPending;
    /// <summary>True when user pressed on hotspot with Alt but hasn't moved yet; we need motion events to start drag.</summary>
    public bool HotspotDragPending => _hotspotDragPending;
    public bool IsResizing => _isResizing;

    public void AddHotspot(Action queueRedraw)
    {
        int w = 60, h = 40;
        int x = Mathf.Clamp((_roomData.BaseSize.W - w) / 2, 0, _roomData.BaseSize.W - w);
        int y = Mathf.Clamp((_roomData.BaseSize.H - h) / 2, 0, _roomData.BaseSize.H - h);
        AddHotspotAtRect(new RectData { X = x, Y = y, W = w, H = h }, "Something.", "Something.", "Something.", queueRedraw);
    }

    /// <summary>Add a default "nothing here" hotspot at the given position. Returns the new hotspot index. Saved to room.json on Ctrl+S.</summary>
    public int AddHotspotAtPoint(Vector2I clickPoint, Action queueRedraw)
    {
        int w = 48, h = 32;
        int x = Mathf.Clamp(clickPoint.X - w / 2, 0, _roomData.BaseSize.W - w);
        int y = Mathf.Clamp(clickPoint.Y - h / 2, 0, _roomData.BaseSize.H - h);
        return AddHotspotAtRect(new RectData { X = x, Y = y, W = w, H = h },
            "Nothing of interest here.",
            "Nothing happens.",
            "There's no one to talk to.",
            queueRedraw);
    }

    private int AddHotspotAtRect(RectData rect, string lookText, string useText, string talkText, Action queueRedraw)
    {
        int n = (_roomData.Hotspots?.Length ?? 0) + 1;
        string id = $"hotspot_{n}";
        var newHotspot = new HotspotData
        {
            Id = id,
            Rect = rect,
            Points = RectToPoints(rect),
            Verbs = new VerbActionsData
            {
                Look = new VerbActionData { Type = "text", Value = lookText },
                Use = new VerbActionData { Type = "text", Value = useText },
                Talk = new VerbActionData { Type = "text", Value = talkText }
            }
        };
        var list = new List<HotspotData>(_roomData.Hotspots ?? Array.Empty<HotspotData>());
        list.Add(newHotspot);
        _roomData.Hotspots = list.ToArray();
        int newIndex = list.Count - 1;
        _selectedHotspotIndex = newIndex;
        _selectedExitIndex = -1;
        _selectedVertexIndex = -1;
        HasUnsavedChanges = true;
        queueRedraw?.Invoke();
        GD.Print($"Added hotspot '{id}'");
        return newIndex;
    }

    public void DeleteSelected(Action queueRedraw)
    {
        if (_selectedVertexIndex >= 0 && _selectedHotspotIndex >= 0 && _roomData.Hotspots != null &&
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
                HasUnsavedChanges = true;
                GD.Print($"Deleted vertex from hotspot '{hotspot.Id}'");
                queueRedraw?.Invoke();
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
            HasUnsavedChanges = true;
            GD.Print($"Deleted hotspot: {deletedId}");
            queueRedraw?.Invoke();
            return;
        }
        if (_selectedExitIndex >= 0 && _roomData.Exits != null && _selectedExitIndex < _roomData.Exits.Length)
        {
            var deletedId = _roomData.Exits[_selectedExitIndex].Id;
            var newList = new List<ExitData>(_roomData.Exits);
            newList.RemoveAt(_selectedExitIndex);
            _roomData.Exits = newList.ToArray();
            _selectedExitIndex = -1;
            HasUnsavedChanges = true;
            GD.Print($"Deleted exit: {deletedId}");
            queueRedraw?.Invoke();
        }
    }

    public bool SaveRoomData(Action queueRedraw)
    {
        if (string.IsNullOrEmpty(_packagePath))
        {
            _showMessage("Save failed: no room loaded");
            return false;
        }
        string realPath = ProjectSettings.GlobalizePath(_packagePath);
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
            HasUnsavedChanges = false;
            GD.Print($"Saved room data to: {realPath}");
            _showMessage("Room data saved!");
            queueRedraw?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to save room data: {ex.Message}");
            _showMessage($"Save failed: {ex.Message}");
            return false;
        }
    }

    public void ApplyHotspotEdit(int hotspotIndex, string name, string look, string use, string talk, Action queueRedraw)
    {
        if (_roomData.Hotspots == null || hotspotIndex < 0 || hotspotIndex >= _roomData.Hotspots.Length) return;
        var hotspot = _roomData.Hotspots[hotspotIndex];
        if (hotspot.Verbs == null) hotspot.Verbs = new VerbActionsData();
        if (!string.IsNullOrWhiteSpace(name)) hotspot.Id = name.Trim();
        hotspot.Verbs.Look = new VerbActionData { Type = "text", Value = look ?? "" };
        hotspot.Verbs.Use = new VerbActionData { Type = "text", Value = use ?? "" };
        hotspot.Verbs.Talk = new VerbActionData { Type = "text", Value = talk ?? "" };
        HasUnsavedChanges = true;
        SaveRoomData(queueRedraw);
        _showMessage("Saved. Enter = save again, Esc = close.");
    }

    public void ApplyExitEdit(int exitIndex, string id, string targetRoomId, string spawnXStr, string spawnYStr, Action queueRedraw)
    {
        if (_roomData.Exits == null || exitIndex < 0 || exitIndex >= _roomData.Exits.Length) return;
        var exit = _roomData.Exits[exitIndex];
        if (!string.IsNullOrWhiteSpace(id)) exit.Id = id.Trim();
        if (!string.IsNullOrWhiteSpace(targetRoomId)) exit.TargetRoomId = targetRoomId.Trim();
        if (exit.TargetSpawn == null) exit.TargetSpawn = new Vector2Data();
        if (float.TryParse(spawnXStr, out float sx)) exit.TargetSpawn.X = sx;
        if (float.TryParse(spawnYStr, out float sy)) exit.TargetSpawn.Y = sy;
        HasUnsavedChanges = true;
        SaveRoomData(queueRedraw);
        _showMessage("Saved. Enter = save again, Esc = close.");
    }

    /// <summary>Draw F4/F5 overlay: hotspots, exits, handles, instructions. Pass renderScale and base size for coordinate conversion.</summary>
    public void DrawOverlay(CanvasItem canvas, float renderScale, int baseW, int baseH, bool shapeMode, bool textMode)
    {
        if (_roomData == null) return;
        if (shapeMode || textMode)
        {
            if (_roomData.Hotspots != null)
            {
                for (int i = 0; i < _roomData.Hotspots.Length; i++)
                {
                    var hotspot = _roomData.Hotspots[i];
                    var pts = GetHotspotPoints(hotspot);
                    if (pts == null || pts.Length < 2) continue;
                    Color color = (i == _selectedHotspotIndex) ? Colors.Orange : Colors.Yellow;
                    for (int j = 0, k = pts.Length - 1; j < pts.Length; k = j++)
                    {
                        Vector2 a = new Vector2((float)pts[k].X, (float)pts[k].Y) * renderScale;
                        Vector2 b = new Vector2((float)pts[j].X, (float)pts[j].Y) * renderScale;
                        canvas.DrawLine(a, b, color, 2);
                    }
                    if (i == _selectedHotspotIndex && shapeMode)
                        DrawVertexHandles(canvas, pts, _selectedVertexIndex, renderScale);
                }
            }
            if (_roomData.Exits != null)
            {
                for (int i = 0; i < _roomData.Exits.Length; i++)
                {
                    var exit = _roomData.Exits[i];
                    var r = exit.Rect;
                    Rect2 rect = new Rect2(r.X * renderScale, r.Y * renderScale, r.W * renderScale, r.H * renderScale);
                    Color color = (i == _selectedExitIndex) ? Colors.Cyan : Colors.Green;
                    float lw = (i == _selectedExitIndex) ? 3 : 2;
                    canvas.DrawRect(rect, color, false, lw);
                    canvas.DrawString(ThemeDB.FallbackFont, new Vector2(rect.Position.X, rect.Position.Y - 5),
                        $"{exit.Id} -> {exit.TargetRoomId}", HorizontalAlignment.Left, -1, 12, color);
                    if (i == _selectedExitIndex && shapeMode)
                        DrawResizeHandles(canvas, rect, renderScale);
                }
            }
            const float DebugTextLeft = 8f;
            const int DebugFontSize = 28, DebugFontSizeMode = 32;
            float debugY = 18f;
            float debugLineHeight = 36f;
            float modeBlockGap = 12f;
            if (HasUnsavedChanges)
            {
                DrawDebugTextOutlined(canvas, new Vector2(DebugTextLeft, debugY), "UNSAVED CHANGES - Press Ctrl+S to save", Colors.Yellow, DebugFontSize);
                debugY += debugLineHeight;
            }
            if (textMode)
            {
                DrawDebugTextOutlined(canvas, new Vector2(DebugTextLeft, debugY), "F5 — TEXT MODE", Colors.Cyan, DebugFontSizeMode);
                debugY += debugLineHeight + modeBlockGap;
                DrawDebugTextOutlined(canvas, new Vector2(DebugTextLeft, debugY), "  Click hotspot → edit name, look, use, talk", Colors.Cyan, DebugFontSize);
                debugY += debugLineHeight;
                DrawDebugTextOutlined(canvas, new Vector2(DebugTextLeft, debugY), "  Enter = save  |  Esc = close dialog", Colors.Cyan, DebugFontSize);
            }
            else if (shapeMode)
            {
                DrawDebugTextOutlined(canvas, new Vector2(DebugTextLeft, debugY), "F4 — SHAPE MODE", Colors.White, DebugFontSizeMode);
                debugY += debugLineHeight + modeBlockGap;
                DrawDebugTextOutlined(canvas, new Vector2(DebugTextLeft, debugY), "  Hold Alt + drag to move hotspot  |  Hold Ctrl = vertex mode", Colors.White, DebugFontSize);
                debugY += debugLineHeight;
                DrawDebugTextOutlined(canvas, new Vector2(DebugTextLeft, debugY), "  Vertex (Ctrl): drag vertices, double-click edge to add point", Colors.White, DebugFontSize);
                debugY += debugLineHeight;
                DrawDebugTextOutlined(canvas, new Vector2(DebugTextLeft, debugY), "  Del = delete vertex or hotspot  |  A = add  |  Ctrl+S = save", Colors.White, DebugFontSize);
            }
        }
    }

    private void MoveHotspotToPoint(int index, Vector2I centerBase)
    {
        var hotspot = _roomData.Hotspots[index];
        var pts = GetHotspotPoints(hotspot);
        if (pts == null || pts.Length == 0) return;
        float cx = 0, cy = 0;
        foreach (var p in pts) { cx += (float)p.X; cy += (float)p.Y; }
        cx /= pts.Length; cy /= pts.Length;
        float dx = centerBase.X - cx; float dy = centerBase.Y - cy;
        if (hotspot.Points == null) hotspot.Points = (Vector2Data[])pts.Clone();
        for (int i = 0; i < hotspot.Points.Length; i++)
        {
            hotspot.Points[i].X = Mathf.Clamp((float)hotspot.Points[i].X + dx, 0, _roomData.BaseSize.W - 1);
            hotspot.Points[i].Y = Mathf.Clamp((float)hotspot.Points[i].Y + dy, 0, _roomData.BaseSize.H - 1);
        }
        UpdateHotspotRectFromPoints(hotspot);
        HasUnsavedChanges = true;
    }

    private void MoveExitToPoint(int index, Vector2I centerBase)
    {
        var r = _roomData.Exits[index].Rect;
        int newX = Mathf.Clamp(centerBase.X - r.W / 2, 0, _roomData.BaseSize.W - r.W);
        int newY = Mathf.Clamp(centerBase.Y - r.H / 2, 0, _roomData.BaseSize.H - r.H);
        _roomData.Exits[index].Rect.X = newX;
        _roomData.Exits[index].Rect.Y = newY;
        HasUnsavedChanges = true;
    }

    private void AddVertexToHotspotEdge(int hotspotIndex, int edgeIndex, Vector2I positionBase)
    {
        if (_roomData.Hotspots == null || hotspotIndex < 0 || hotspotIndex >= _roomData.Hotspots.Length) return;
        var hotspot = _roomData.Hotspots[hotspotIndex];
        var pts = GetHotspotPoints(hotspot);
        if (pts == null || edgeIndex < 0 || edgeIndex > pts.Length) return;
        if (hotspot.Points == null) hotspot.Points = (Vector2Data[])pts.Clone();
        var list = new List<Vector2Data>(hotspot.Points);
        list.Insert(edgeIndex, new Vector2Data { X = Mathf.Clamp(positionBase.X, 0, _roomData.BaseSize.W - 1), Y = Mathf.Clamp(positionBase.Y, 0, _roomData.BaseSize.H - 1) });
        hotspot.Points = list.ToArray();
        UpdateHotspotRectFromPoints(hotspot);
        HasUnsavedChanges = true;
    }

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

    private static void UpdateHotspotRectFromPoints(HotspotData hotspot)
    {
        var pts = hotspot.Points;
        if (pts == null || pts.Length == 0) return;
        float minX = pts[0].X, minY = pts[0].Y, maxX = pts[0].X, maxY = pts[0].Y;
        for (int i = 1; i < pts.Length; i++)
        {
            minX = Mathf.Min(minX, pts[i].X); minY = Mathf.Min(minY, pts[i].Y);
            maxX = Mathf.Max(maxX, pts[i].X); maxY = Mathf.Max(maxY, pts[i].Y);
        }
        hotspot.Rect = new RectData
        {
            X = Mathf.FloorToInt(minX), Y = Mathf.FloorToInt(minY),
            W = Mathf.CeilToInt(maxX - minX), H = Mathf.CeilToInt(maxY - minY)
        };
    }

    private static bool IsPointInRect(Vector2I point, RectData rect)
    {
        return point.X >= rect.X && point.X < rect.X + rect.W && point.Y >= rect.Y && point.Y < rect.Y + rect.H;
    }

    private static bool IsPointInPolygon(Vector2I point, Vector2Data[] points)
    {
        if (points == null || points.Length < 3) return false;
        float px = point.X, py = point.Y;
        bool inside = false;
        int n = points.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = points[i].X, yi = points[i].Y, xj = points[j].X, yj = points[j].Y;
            if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
                inside = !inside;
        }
        return inside;
    }

    private static bool IsPointInHotspot(Vector2I point, HotspotData hotspot)
    {
        var pts = GetHotspotPoints(hotspot);
        return pts != null && IsPointInPolygon(point, pts);
    }

    private static int GetVertexAtPoint(Vector2I clickBase, Vector2Data[] points)
    {
        if (points == null) return -1;
        float cx = clickBase.X, cy = clickBase.Y;
        for (int i = 0; i < points.Length; i++)
        {
            float dx = cx - (float)points[i].X, dy = cy - (float)points[i].Y;
            if (dx * dx + dy * dy <= VertexHandleRadiusBase * VertexHandleRadiusBase) return i;
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
            float projX = ax + t * (bx - ax); float projY = ay + t * (by - ay);
            float distSq = (px - projX) * (px - projX) + (py - projY) * (py - projY);
            if (distSq <= maxDistBase * maxDistBase) return i;
        }
        return -1;
    }

    private static string GetResizeHandle(Vector2I clickPoint, RectData rect)
    {
        float handleRadius = 8f;
        Vector2 click = new Vector2(clickPoint.X, clickPoint.Y);
        Vector2 tl = new Vector2(rect.X, rect.Y);
        Vector2 tr = new Vector2(rect.X + rect.W, rect.Y);
        Vector2 bl = new Vector2(rect.X, rect.Y + rect.H);
        Vector2 br = new Vector2(rect.X + rect.W, rect.Y + rect.H);
        Vector2 tm = new Vector2(rect.X + rect.W / 2f, rect.Y);
        Vector2 bm = new Vector2(rect.X + rect.W / 2f, rect.Y + rect.H);
        Vector2 lm = new Vector2(rect.X, rect.Y + rect.H / 2f);
        Vector2 rm = new Vector2(rect.X + rect.W, rect.Y + rect.H / 2f);
        if (click.DistanceTo(tl) <= handleRadius) return "tl";
        if (click.DistanceTo(tr) <= handleRadius) return "tr";
        if (click.DistanceTo(bl) <= handleRadius) return "bl";
        if (click.DistanceTo(br) <= handleRadius) return "br";
        if (click.DistanceTo(tm) <= handleRadius) return "t";
        if (click.DistanceTo(bm) <= handleRadius) return "b";
        if (click.DistanceTo(lm) <= handleRadius) return "l";
        if (click.DistanceTo(rm) <= handleRadius) return "r";
        return "";
    }

    private static void DrawResizeHandles(CanvasItem canvas, Rect2 rect, float renderScale)
    {
        float handleSize = Mathf.Max(12, 5 * renderScale);
        float half = handleSize / 2;
        void DrawHandle(Vector2 center)
        {
            var r = new Rect2(center.X - half, center.Y - half, handleSize, handleSize);
            canvas.DrawRect(r, Colors.Black, false, 2);
            canvas.DrawRect(r, Colors.White, true);
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

    private static void DrawVertexHandles(CanvasItem canvas, Vector2Data[] points, int selectedVertexIndex, float renderScale)
    {
        float handleSize = Mathf.Max(10, 4 * renderScale);
        float half = handleSize / 2;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 center = new Vector2((float)points[i].X, (float)points[i].Y) * renderScale;
            var r = new Rect2(center.X - half, center.Y - half, handleSize, handleSize);
            bool selected = (i == selectedVertexIndex);
            Color fillColor = selected ? new Color(1f, 0.5f, 0f) : Colors.White;
            Color outlineColor = selected ? Colors.Orange : Colors.Black;
            canvas.DrawRect(r, outlineColor, false, selected ? 3 : 2);
            canvas.DrawRect(r, fillColor, true);
        }
    }

    private static void DrawDebugTextOutlined(CanvasItem canvas, Vector2 pos, string text, Color fillColor, int fontSize)
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
}
