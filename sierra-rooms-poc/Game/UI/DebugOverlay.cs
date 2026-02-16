using Godot;
using SierraRooms.Game.Rooms;

namespace SierraRooms.Game.UI;

public partial class DebugOverlay : CanvasLayer
{
    private Node2D _drawNode;
    private RoomRuntime _roomRuntime;
    private bool _textVisible = false; // Default off; F1 toggles

    private const float Pad = 16f;
    private const float TextTopPad = 24f; // Extra top padding so text doesn't overlap panel border
    private const int FontSize = 33;   // 3x (was 11)
    private const float LineHeight = 42f; // 3x (was 14)

    public override void _Ready()
    {
        _drawNode = new Node2D();
        _drawNode.Visible = _textVisible; // Default off
        _drawNode.Draw += OnDrawDebugText;
        AddChild(_drawNode);

        _roomRuntime = GetTree().Root.GetNode<RoomRuntime>("GameMain/RoomRuntime");
    }

    /// <summary>Called by RoomRuntime so only one debug mode (F1–F5) is active at a time.</summary>
    public void SetF1Visible(bool visible)
    {
        _textVisible = visible;
        _drawNode.Visible = visible;
        _drawNode.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_drawNode.Visible != _textVisible)
        {
            _drawNode.Visible = _textVisible;
            _drawNode.QueueRedraw();
        }
        if (_textVisible)
            _drawNode.QueueRedraw();
    }

    private void OnDrawDebugText()
    {
        if (!_textVisible || _roomRuntime == null || _roomRuntime.RoomData == null)
            return;

        var roomData = _roomRuntime.RoomData;
        Vector2 mouseRoomCoords = _roomRuntime.GetMouseRoomCoords();
        Vector2 egoBasePos = _roomRuntime.GetEgoBasePosition();
        bool mouseWalkable = _roomRuntime.GetMouseWalkable();
        Vector2I footSampleCoord = _roomRuntime.GetEgoFootSampleCoord();
        int priorityValue = _roomRuntime.GetEgoPriorityValue();
        int egoZIndex = _roomRuntime.GetEgoZIndex();
        string currentVerb = _roomRuntime.CurrentVerb.ToUpper();

        var lines = new System.Collections.Generic.List<string>
        {
            $"Room: {roomData.Id}",
            $"Verb: {currentVerb} [1=LOOK, 2=USE, 3=TALK]",
            $"Mouse: ({Mathf.FloorToInt(mouseRoomCoords.X)}, {Mathf.FloorToInt(mouseRoomCoords.Y)}) Walkable: {mouseWalkable}",
            $"Ego: ({Mathf.FloorToInt(egoBasePos.X)}, {Mathf.FloorToInt(egoBasePos.Y)})",
            $"FootSample: ({footSampleCoord.X}, {footSampleCoord.Y})",
            $"Priority: {priorityValue} | ZIndex: {egoZIndex}",
            $"BaseSize: {roomData.BaseSize.W}x{roomData.BaseSize.H}",
            $"RenderScale: {roomData.RenderScale:F1}"
        };

        if (_roomRuntime.HasDebugTargets())
        {
            var requested = _roomRuntime.GetLastRequestedTarget();
            var clamped = _roomRuntime.GetLastClampedTarget();
            lines.Add($"Requested: ({requested.X}, {requested.Y})");
            lines.Add($"Clamped: ({clamped.X}, {clamped.Y})");
        }

        lines.Add("[F1] Toggle | [F2] Control | [F3] Priority | [F4] Hotspots [F5] Text");
        lines.Add("[F4] A=Add Click=Move Handles=Resize Del=Delete Ctrl+S=Save");

        // Size panel to surround the text (measure longest line; GetStringSize uses default 16px so scale for our FontSize)
        var font = ThemeDB.FallbackFont;
        float maxW = 0;
        foreach (string line in lines)
        {
            Vector2 sz = font.GetStringSize(line);
            if (sz.X > maxW) maxW = sz.X;
        }
        float scale = FontSize / 16f;
        float contentWidth = maxW * scale;
        float boxWidth = contentWidth + Pad * 2;
        float boxHeight = lines.Count * LineHeight + TextTopPad + Pad;

        Vector2 viewport = _drawNode.GetViewport().GetVisibleRect().Size;
        float x = Pad;
        float y = Pad;
        if (x + boxWidth > viewport.X - Pad) x = Mathf.Max(Pad, viewport.X - boxWidth - Pad);
        if (y + boxHeight > viewport.Y - Pad) y = Mathf.Max(Pad, viewport.Y - boxHeight - Pad);
        var panelRect = new Rect2(x, y, boxWidth, boxHeight);

        _drawNode.DrawRect(panelRect, new Color(0.08f, 0.08f, 0.08f, 0.92f), true);
        _drawNode.DrawRect(panelRect, Colors.Yellow, false, 2);

        float textX = x + Pad;
        float textY = y + TextTopPad;
        Color fillColor = Colors.Yellow;
        foreach (string line in lines)
        {
            DrawTextOutlined(_drawNode, new Vector2(textX, textY), line, fillColor, FontSize);
            textY += LineHeight;
        }
    }

    private static void DrawTextOutlined(CanvasItem canvas, Vector2 pos, string text, Color fillColor, int fontSize)
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
