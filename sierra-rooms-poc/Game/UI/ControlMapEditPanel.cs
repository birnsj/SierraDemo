using Godot;
using System;
using SierraRooms.Game.Rooms;

namespace SierraRooms.Game.UI;

/// <summary>F2 control map mode: edit controlRule values and save to the room's room.json.</summary>
public partial class ControlMapEditPanel : CanvasLayer
{
    private Panel _panel;
    private SpinBox _lumaSpinBox;
    private CheckBox _invertCheckBox;
    private SpinBox _expandSpinBox;
    private SpinBox _paddingSpinBox;
    private Button _saveButton;
    private Action<ControlRuleData> _onSave;
    private bool _isVisibleFlag = false;

    public new bool IsVisible => _isVisibleFlag;

    private const float Pad = 20f;
    private const float ContentWidth = 340f;
    private const float LabelHeight = 22f;
    private const float RowGap = 10f;
    private const float ButtonH = 34f;

    public override void _Ready()
    {
        float panelW = ContentWidth + Pad * 2;
        float panelH = Pad + (LabelHeight + 32) * 4 + RowGap * 3 + ButtonH + Pad;
        _panel = new Panel();
        _panel.Position = new Vector2(380, 180);
        _panel.Size = new Vector2(panelW, panelH);
        _panel.Visible = false;

        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.1f, 0.12f, 0.18f, 0.97f);
        styleBox.BorderColor = new Color(0.3f, 0.7f, 0.5f);
        styleBox.SetBorderWidthAll(2);
        _panel.AddThemeStyleboxOverride("panel", styleBox);
        AddChild(_panel);

        float x = Pad;
        float y = Pad;
        float w = ContentWidth;

        var titleLabel = new Label();
        titleLabel.Text = "Control map (F2) — save to room.json";
        titleLabel.Position = new Vector2(x, y);
        titleLabel.AddThemeFontSizeOverride("font_size", 20);
        titleLabel.Modulate = new Color(0.85f, 1f, 0.9f);
        _panel.AddChild(titleLabel);
        y += LabelHeight + 8;

        var lumaLabel = new Label();
        lumaLabel.Text = "Walkable if luma ≥ (0–1):";
        lumaLabel.Position = new Vector2(x, y);
        lumaLabel.AddThemeFontSizeOverride("font_size", 16);
        _panel.AddChild(lumaLabel);
        y += LabelHeight;
        _lumaSpinBox = new SpinBox();
        _lumaSpinBox.Position = new Vector2(x, y);
        _lumaSpinBox.Size = new Vector2(w, 28);
        _lumaSpinBox.MinValue = 0;
        _lumaSpinBox.MaxValue = 1;
        _lumaSpinBox.Step = 0.05;
        _lumaSpinBox.Value = 0.2;
        _lumaSpinBox.AllowGreater = false;
        _lumaSpinBox.AllowLesser = false;
        _lumaSpinBox.AddThemeFontSizeOverride("font_size", 16);
        _panel.AddChild(_lumaSpinBox);
        y += 32 + RowGap;

        _invertCheckBox = new CheckBox();
        _invertCheckBox.Text = "Invert (walkable = luma < threshold)";
        _invertCheckBox.Position = new Vector2(x, y);
        _invertCheckBox.AddThemeFontSizeOverride("font_size", 16);
        _panel.AddChild(_invertCheckBox);
        y += 32 + RowGap;

        var expandLabel = new Label();
        expandLabel.Text = "Walkable expand pixels (0 = off):";
        expandLabel.Position = new Vector2(x, y);
        expandLabel.AddThemeFontSizeOverride("font_size", 16);
        _panel.AddChild(expandLabel);
        y += LabelHeight;
        _expandSpinBox = new SpinBox();
        _expandSpinBox.Position = new Vector2(x, y);
        _expandSpinBox.Size = new Vector2(w, 28);
        _expandSpinBox.MinValue = 0;
        _expandSpinBox.MaxValue = 5;
        _expandSpinBox.Step = 1;
        _expandSpinBox.Value = 0;
        _expandSpinBox.AddThemeFontSizeOverride("font_size", 16);
        _panel.AddChild(_expandSpinBox);
        y += 32 + RowGap;

        var paddingLabel = new Label();
        paddingLabel.Text = "Walkable padding (cells, 0–10):";
        paddingLabel.Position = new Vector2(x, y);
        paddingLabel.AddThemeFontSizeOverride("font_size", 16);
        _panel.AddChild(paddingLabel);
        y += LabelHeight;
        _paddingSpinBox = new SpinBox();
        _paddingSpinBox.Position = new Vector2(x, y);
        _paddingSpinBox.Size = new Vector2(w, 28);
        _paddingSpinBox.MinValue = 0;
        _paddingSpinBox.MaxValue = 10;
        _paddingSpinBox.Step = 1;
        _paddingSpinBox.Value = 3;
        _paddingSpinBox.AddThemeFontSizeOverride("font_size", 16);
        _panel.AddChild(_paddingSpinBox);
        y += 32 + RowGap;

        _saveButton = new Button();
        _saveButton.Text = "Save to room.json (Ctrl+S)";
        _saveButton.Position = new Vector2(x, y);
        _saveButton.Size = new Vector2(220, ButtonH);
        _saveButton.AddThemeFontSizeOverride("font_size", 16);
        _saveButton.Pressed += OnSavePressed;
        _panel.AddChild(_saveButton);
    }

    /// <summary>Show panel and fill from current control rule. onSave is called when user clicks Save (with updated rule).</summary>
    public void Show(ControlRuleData current, Action<ControlRuleData> onSave)
    {
        _onSave = onSave;
        if (current != null)
        {
            _lumaSpinBox.Value = Mathf.Clamp((double)current.WalkableIfLumaGte, 0, 1);
            _invertCheckBox.ButtonPressed = current.Invert;
            _expandSpinBox.Value = Mathf.Clamp(current.WalkableExpandPixels, 0, 5);
            _paddingSpinBox.Value = current.WalkablePadding.HasValue ? Mathf.Clamp(current.WalkablePadding.Value, 0, 10) : 3;
        }
        else
        {
            _paddingSpinBox.Value = 3;
        }
        _panel.Visible = true;
        _isVisibleFlag = true;
    }

    public new void Hide()
    {
        _panel.Visible = false;
        _isVisibleFlag = false;
        _onSave = null;
    }

    /// <summary>Call from RoomRuntime when Ctrl+S in F2 mode; performs save without closing.</summary>
    public void SaveIfVisible()
    {
        if (!_isVisibleFlag || _onSave == null) return;
        var data = GetCurrentRule();
        _onSave?.Invoke(data);
    }

    /// <summary>Return current control rule from spin boxes (for F2 full save: rule + walkable polygon).</summary>
    public ControlRuleData GetCurrentRuleData()
    {
        return GetCurrentRule();
    }

    private void OnSavePressed()
    {
        var data = GetCurrentRule();
        _onSave?.Invoke(data);
    }

    private ControlRuleData GetCurrentRule()
    {
        int paddingVal = (int)_paddingSpinBox.Value;
        return new ControlRuleData
        {
            WalkableIfLumaGte = (float)_lumaSpinBox.Value,
            Invert = _invertCheckBox.ButtonPressed,
            WalkableExpandPixels = (int)_expandSpinBox.Value,
            WalkablePadding = paddingVal
        };
    }
}
