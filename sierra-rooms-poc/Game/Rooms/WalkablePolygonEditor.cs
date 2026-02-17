using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SierraRooms.Game.Rooms;

/// <summary>F2 vector control map: edit a single walkable polygon per room (vertices/lines like F4). One box only; can add one if deleted.</summary>
public class WalkablePolygonEditor
{
    private readonly RoomData _roomData;
    private readonly string _packagePath;
    private readonly Action<string> _showMessage;

    private int _selectedVertexIndex = -1;
    private int _draggingVertexIndex = -1;
    private bool _vertexDragPending;
    private bool _polygonDragPending;
    private Vector2 _lastMouseBase;
    private Vector2 _dragStartVertexPosition;
    private double _lastClickTime;
    private Vector2I _lastClickPoint;

    private const float VertexHandleRadiusBase = 8f;
    private const float EdgeHitRadiusBase = 28f;

    public bool HasUnsavedChanges { get; private set; }
    /// <summary>Clear dirty flag after room was saved externally (e.g. F2 Ctrl+S).</summary>
    public void MarkSaved() => HasUnsavedChanges = false;
    public int SelectedVertexIndex => _selectedVertexIndex;
    public bool IsDraggingVertex => _draggingVertexIndex >= 0;
    public bool VertexDragPending => _vertexDragPending;
    public bool PolygonDragPending => _polygonDragPending;

    public WalkablePolygonEditor(RoomData roomData, string packagePath, Action<string> showMessage)
    {
        _roomData = roomData ?? throw new ArgumentNullException(nameof(roomData));
        _packagePath = packagePath ?? "";
        _showMessage = showMessage ?? (_ => { });
    }

    public void ClearSelection()
    {
        _selectedVertexIndex = -1;
    }

    private Vector2Data[] GetPoints()
    {
        return _roomData.WalkablePolygon;
    }

    /// <summary>Handle left-click in F2 vector mode. vertexMode = Ctrl held.</summary>
    public bool TryHandleClick(Vector2I clickPoint, Vector2 clickLocalPixels, bool vertexMode, bool altHeld, Action queueRedraw)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        bool doubleClick = (now - _lastClickTime < 0.5) && (clickPoint - _lastClickPoint).LengthSquared() < 900;
        var pts = GetPoints();

        // No polygon: click does nothing except we could add on A (handled by RoomRuntime)
        if (pts == null || pts.Length < 3)
        {
            _selectedVertexIndex = -1;
            _lastClickTime = now;
            _lastClickPoint = clickPoint;
            queueRedraw?.Invoke();
            return true;
        }

        // 1) Vertex hit (always in vertex mode or when clicking near vertex)
        int vi = GetVertexAtPoint(clickPoint, pts);
        if (vi >= 0)
        {
            _selectedVertexIndex = vi;
            _vertexDragPending = true;
            _draggingVertexIndex = -1;
            _dragStartVertexPosition = new Vector2((float)pts[vi].X, (float)pts[vi].Y);
            _lastClickTime = now;
            _lastClickPoint = clickPoint;
            queueRedraw?.Invoke();
            return true;
        }

        // 2) Double-click edge to add vertex (vertex mode or when selected)
        if (vertexMode || _selectedVertexIndex >= 0)
        {
            int edgeIndex = GetEdgeAtPoint(clickPoint, pts);
            if (edgeIndex >= 0 && doubleClick)
            {
                AddVertexToEdge(edgeIndex, clickPoint);
                _lastClickTime = 0;
                queueRedraw?.Invoke();
                return true;
            }
        }

        // 3) Click inside polygon: select (or start polygon drag with Alt)
        if (IsPointInPolygon(clickPoint, pts))
        {
            if (altHeld)
            {
                _polygonDragPending = true;
                _lastMouseBase = new Vector2(clickPoint.X, clickPoint.Y);
            }
            _selectedVertexIndex = -1;
            _lastClickTime = now;
            _lastClickPoint = clickPoint;
            queueRedraw?.Invoke();
            return true;
        }

        // 4) Click outside: deselect
        _selectedVertexIndex = -1;
        _lastClickTime = now;
        _lastClickPoint = clickPoint;
        queueRedraw?.Invoke();
        return true;
    }

    public void HandleMotion(Vector2 currentScreenPos, Vector2 currentBase, float renderScale, bool isLeftButtonPressed, Action queueRedraw)
    {
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
        if (_polygonDragPending && isLeftButtonPressed && GetPoints() != null)
        {
            Vector2 delta = currentBase - _lastMouseBase;
            _lastMouseBase = currentBase;
            var pts = _roomData.WalkablePolygon;
            int w = _roomData.BaseSize.W;
            int h = _roomData.BaseSize.H;
            for (int i = 0; i < pts.Length; i++)
            {
                float x = Mathf.Clamp((float)pts[i].X + delta.X, 0, w - 1);
                float y = Mathf.Clamp((float)pts[i].Y + delta.Y, 0, h - 1);
                pts[i].X = x;
                pts[i].Y = y;
            }
            HasUnsavedChanges = true;
            queueRedraw?.Invoke();
        }
    }

    private void HandleMotionVertexDrag(Vector2 currentBase, Action queueRedraw)
    {
        var pts = GetPoints();
        if (pts == null || _draggingVertexIndex < 0 || _draggingVertexIndex >= pts.Length) return;
        int w = _roomData.BaseSize.W;
        int h = _roomData.BaseSize.H;
        float x = Mathf.Clamp(currentBase.X, 0, w - 1);
        float y = Mathf.Clamp(currentBase.Y, 0, h - 1);
        pts[_draggingVertexIndex].X = x;
        pts[_draggingVertexIndex].Y = y;
        HasUnsavedChanges = true;
        queueRedraw?.Invoke();
    }

    public void HandleRelease(Action queueRedraw)
    {
        _draggingVertexIndex = -1;
        _vertexDragPending = false;
        _polygonDragPending = false;
        queueRedraw?.Invoke();
    }

    /// <summary>Add the single allowed walkable polygon (default box). Only if none exists.</summary>
    public void AddPolygon(Action queueRedraw)
    {
        if (GetPoints() != null && GetPoints().Length >= 3)
        {
            _showMessage("Room already has a walkable polygon. Only one per room.");
            return;
        }
        int w = _roomData.BaseSize.W;
        int h = _roomData.BaseSize.H;
        int margin = Mathf.Max(20, Mathf.Min(w, h) / 8);
        int x = margin;
        int y = margin;
        int rw = w - 2 * margin;
        int rh = h - 2 * margin;
        if (rw < 20) rw = 20;
        if (rh < 20) rh = 20;
        _roomData.WalkablePolygon = new[]
        {
            new Vector2Data { X = x, Y = y },
            new Vector2Data { X = x + rw, Y = y },
            new Vector2Data { X = x + rw, Y = y + rh },
            new Vector2Data { X = x, Y = y + rh }
        };
        _selectedVertexIndex = -1;
        HasUnsavedChanges = true;
        queueRedraw?.Invoke();
        GD.Print("Added walkable polygon (one per room)");
        _showMessage("Walkable polygon added. Edit vertices, then Ctrl+S to save.");
    }

    public void DeleteSelected(Action queueRedraw)
    {
        var pts = GetPoints();
        if (pts == null || pts.Length < 3) return;
        if (_selectedVertexIndex >= 0 && pts.Length > 3)
        {
            var list = new List<Vector2Data>(pts);
            list.RemoveAt(_selectedVertexIndex);
            _roomData.WalkablePolygon = list.ToArray();
            _selectedVertexIndex = -1;
            HasUnsavedChanges = true;
            GD.Print("Deleted vertex from walkable polygon");
            queueRedraw?.Invoke();
            return;
        }
        _roomData.WalkablePolygon = null;
        _selectedVertexIndex = -1;
        HasUnsavedChanges = true;
        GD.Print("Deleted walkable polygon. Press A to add one.");
        _showMessage("Walkable polygon removed. Press A to add one. Ctrl+S to save.");
        queueRedraw?.Invoke();
    }

    public bool SaveRoomData(Action queueRedraw)
    {
        if (string.IsNullOrEmpty(_packagePath))
        {
            _showMessage("Save failed: no room loaded");
            return false;
        }
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        string jsonText = JsonSerializer.Serialize(_roomData, options);
        try
        {
            string realPath = ProjectSettings.GlobalizePath(_packagePath);
            System.IO.File.WriteAllText(realPath, jsonText);
            HasUnsavedChanges = false;
            GD.Print($"Saved room (walkable polygon) to: {realPath}");
            _showMessage("Room saved (walkable polygon + control rule).");
            queueRedraw?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to save room: {ex.Message}");
            _showMessage($"Save failed: {ex.Message}");
            return false;
        }
    }

    private void AddVertexToEdge(int edgeIndex, Vector2I positionBase)
    {
        var pts = GetPoints();
        if (pts == null || edgeIndex < 0 || edgeIndex > pts.Length) return;
        int w = _roomData.BaseSize.W;
        int h = _roomData.BaseSize.H;
        var list = new List<Vector2Data>(pts);
        list.Insert(edgeIndex, new Vector2Data
        {
            X = Mathf.Clamp(positionBase.X, 0, w - 1),
            Y = Mathf.Clamp(positionBase.Y, 0, h - 1)
        });
        _roomData.WalkablePolygon = list.ToArray();
        HasUnsavedChanges = true;
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

    private static int GetEdgeAtPoint(Vector2I clickBase, Vector2Data[] points)
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
            if (distSq <= EdgeHitRadiusBase * EdgeHitRadiusBase) return i;
        }
        return -1;
    }

    /// <summary>Draw F2 vector overlay: walkable polygon, vertex handles, instructions.</summary>
    public void DrawOverlay(CanvasItem canvas, float renderScale, int baseW, int baseH, bool settingsPanelVisible)
    {
        if (_roomData == null) return;
        var pts = GetPoints();
        const float DebugTextLeft = 8f;
        const int DebugFontSize = 28;
        const int DebugFontSizeMode = 32;
        float debugY = 18f;
        float debugLineHeight = 36f;

        if (HasUnsavedChanges)
        {
            DrawDebugTextOutlined(canvas, new Vector2(DebugTextLeft, debugY), "UNSAVED — Ctrl+S to save walkable polygon", Colors.Lime, DebugFontSize);
            debugY += debugLineHeight;
        }
        if (!settingsPanelVisible)
        {
            DrawDebugTextOutlined(canvas, new Vector2(DebugTextLeft, debugY), "F2 — Walkable polygon (vector)", Colors.Lime, DebugFontSizeMode);
            debugY += debugLineHeight;
            DrawDebugTextOutlined(canvas, new Vector2(DebugTextLeft, debugY), "  T = settings  |  Ctrl = vertex mode  |  Del = delete vertex/polygon  |  A = add polygon", Colors.White, DebugFontSize);
        }

        if (pts == null || pts.Length < 2) return;
        Color color = Colors.Lime;
        for (int j = 0, k = pts.Length - 1; j < pts.Length; k = j++)
        {
            Vector2 a = new Vector2((float)pts[k].X, (float)pts[k].Y) * renderScale;
            Vector2 b = new Vector2((float)pts[j].X, (float)pts[j].Y) * renderScale;
            canvas.DrawLine(a, b, color, 2);
        }
        if (!settingsPanelVisible)
            DrawVertexHandles(canvas, pts, _selectedVertexIndex, renderScale);
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
