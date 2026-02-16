using Godot;
using System;

namespace SierraRooms.Game.UI;

/// <summary>Dialog to edit exit id, target room, and spawn. Same style as hotspot edit. Enter saves, Esc closes.</summary>
public partial class ExitEditDialog : CanvasLayer
{
    private Panel _panel;
    private LineEdit _idEdit;
    private LineEdit _targetRoomEdit;
    private LineEdit _spawnXEdit;
    private LineEdit _spawnYEdit;
    private Button _okButton;
    private Button _cancelButton;
    private Action<string, string, string, string> _onConfirm;
    private bool _isVisible = false;

    private const float Pad = 20f;
    private const float ContentWidth = 560f;
    private const float LabelHeight = 24f;
    private const float EditHeight = 36f;
    private const float RowGap = 10f;
    private const float SectionGap = 14f;
    private const float ButtonH = 36f;

    public override void _Ready()
    {
        float panelW = ContentWidth + Pad * 2;
        float panelH = Pad + (LabelHeight + EditHeight) * 4 + SectionGap * 2 + ButtonH + Pad;
        _panel = new Panel();
        _panel.Position = new Vector2(360, 200);
        _panel.Size = new Vector2(panelW, panelH);
        _panel.Visible = false;

        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
        styleBox.BorderColor = Colors.Yellow;
        styleBox.SetBorderWidthAll(3);
        _panel.AddThemeStyleboxOverride("panel", styleBox);
        AddChild(_panel);

        float x = Pad;
        float y = Pad;
        float w = ContentWidth;

        AddLabel("Name (id):", x, y);
        y += LabelHeight;
        _idEdit = AddLineEdit(x, y, w);
        y += EditHeight + SectionGap;

        AddLabel("Target room ID:", x, y);
        y += LabelHeight;
        _targetRoomEdit = AddLineEdit(x, y, w);
        y += EditHeight + RowGap;

        AddLabel("Spawn X:", x, y);
        y += LabelHeight;
        _spawnXEdit = AddLineEdit(x, y, w);
        y += EditHeight + RowGap;

        AddLabel("Spawn Y:", x, y);
        y += LabelHeight;
        _spawnYEdit = AddLineEdit(x, y, w);
        y += EditHeight + SectionGap;

        float buttonW = 120f;
        _okButton = new Button();
        _okButton.Text = "OK (Enter)";
        _okButton.Position = new Vector2(x, y);
        _okButton.Size = new Vector2(buttonW, ButtonH);
        _okButton.AddThemeFontSizeOverride("font_size", 20);
        _okButton.Pressed += OnOkPressed;
        _panel.AddChild(_okButton);

        _cancelButton = new Button();
        _cancelButton.Text = "Cancel";
        _cancelButton.Position = new Vector2(x + buttonW + 12, y);
        _cancelButton.Size = new Vector2(buttonW, ButtonH);
        _cancelButton.AddThemeFontSizeOverride("font_size", 20);
        _cancelButton.Pressed += OnCancelPressed;
        _panel.AddChild(_cancelButton);
    }

    private void AddLabel(string text, float x, float y)
    {
        var label = new Label();
        label.Text = text;
        label.Position = new Vector2(x, y);
        label.AddThemeFontSizeOverride("font_size", 20);
        label.Modulate = Colors.White;
        _panel.AddChild(label);
    }

    private LineEdit AddLineEdit(float x, float y, float width)
    {
        var le = new LineEdit();
        le.Position = new Vector2(x, y);
        le.Size = new Vector2(width, EditHeight);
        le.AddThemeFontSizeOverride("font_size", 22);
        le.GuiInput += (InputEvent e) =>
        {
            if (e is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                if (keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter)
                {
                    OnOkPressed();
                    GetViewport().SetInputAsHandled();
                }
            }
        };
        _panel.AddChild(le);
        return le;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_isVisible)
            return;
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.Escape)
            {
                OnCancelPressed();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public void ShowExit(string id, string targetRoomId, string spawnX, string spawnY, Action<string, string, string, string> onConfirm)
    {
        _idEdit.Text = id ?? "";
        _targetRoomEdit.Text = targetRoomId ?? "";
        _spawnXEdit.Text = spawnX ?? "";
        _spawnYEdit.Text = spawnY ?? "";
        _onConfirm = onConfirm;
        _panel.Visible = true;
        _isVisible = true;
        _idEdit.GrabFocus();
        _idEdit.SelectAll();
    }

    private void OnOkPressed()
    {
        string id = _idEdit.Text?.Trim() ?? "";
        string targetRoomId = _targetRoomEdit.Text?.Trim() ?? "";
        string spawnX = _spawnXEdit.Text?.Trim() ?? "";
        string spawnY = _spawnYEdit.Text?.Trim() ?? "";
        _onConfirm?.Invoke(id, targetRoomId, spawnX, spawnY);
    }

    private void OnCancelPressed()
    {
        Hide();
    }

    public new void Hide()
    {
        _panel.Visible = false;
        _isVisible = false;
        _onConfirm = null;
    }
}
