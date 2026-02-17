using Godot;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SierraRooms.Game.Rooms;

namespace SierraRooms.Game.UI;

/// <summary>F6 mode: edit all ego settings and persist them to user://ego_settings.json.</summary>
public partial class EgoSettingsDialog : CanvasLayer
{
    private Panel _panel;
    private SpinBox _moveSpeedSpinBox;
    private HSlider _slideSlider;
    private Label _slideValueLabel;
    private Button _saveButton;
    private Button _closeButton;
    private Ego _currentEgo;
    private bool _isVisibleFlag = false;

    public new bool IsVisible => _isVisibleFlag;

    private const string SettingsPath = "user://ego_settings.json";
    private const float Pad = 20f;
    private const float ContentWidth = 320f;
    private const float LabelHeight = 24f;
    private const float RowGap = 12f;
    private const float ButtonH = 36f;

    public override void _Ready()
    {
        float panelW = ContentWidth + Pad * 2;
        float panelH = Pad + LabelHeight + 40f + RowGap + (LabelHeight + 28) + RowGap + ButtonH + Pad;
        _panel = new Panel();
        _panel.Position = new Vector2(400, 220);
        _panel.Size = new Vector2(panelW, panelH);
        _panel.Visible = false;

        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.12f, 0.12f, 0.14f, 0.98f);
        styleBox.BorderColor = new Color(0.4f, 0.6f, 1f);
        styleBox.SetBorderWidthAll(2);
        _panel.AddThemeStyleboxOverride("panel", styleBox);
        AddChild(_panel);

        float x = Pad;
        float y = Pad;
        float w = ContentWidth;

        var titleLabel = new Label();
        titleLabel.Text = "Ego settings (F6)";
        titleLabel.Position = new Vector2(x, y);
        titleLabel.AddThemeFontSizeOverride("font_size", 22);
        titleLabel.Modulate = new Color(0.9f, 0.9f, 1f);
        _panel.AddChild(titleLabel);
        y += LabelHeight + 8;

        var moveSpeedLabel = new Label();
        moveSpeedLabel.Text = "Move speed (base units/sec):";
        moveSpeedLabel.Position = new Vector2(x, y);
        moveSpeedLabel.AddThemeFontSizeOverride("font_size", 18);
        _panel.AddChild(moveSpeedLabel);
        y += LabelHeight;

        _moveSpeedSpinBox = new SpinBox();
        _moveSpeedSpinBox.Position = new Vector2(x, y);
        _moveSpeedSpinBox.Size = new Vector2(w, 32);
        _moveSpeedSpinBox.MinValue = 10;
        _moveSpeedSpinBox.MaxValue = 500;
        _moveSpeedSpinBox.Step = 10;
        _moveSpeedSpinBox.Value = 100;
        _moveSpeedSpinBox.AddThemeFontSizeOverride("font_size", 18);
        _panel.AddChild(_moveSpeedSpinBox);
        y += 40 + RowGap;

        var slideLabel = new Label();
        slideLabel.Text = "Slide (0=none, 1=full):";
        slideLabel.Position = new Vector2(x, y);
        slideLabel.AddThemeFontSizeOverride("font_size", 18);
        _panel.AddChild(slideLabel);
        y += LabelHeight;
        _slideSlider = new HSlider();
        _slideSlider.Position = new Vector2(x, y);
        _slideSlider.Size = new Vector2(w - 50, 24);
        _slideSlider.MinValue = 0;
        _slideSlider.MaxValue = 1;
        _slideSlider.Step = 0.05;
        _slideSlider.Value = 1;
        _slideSlider.ValueChanged += (double v) => { if (_slideValueLabel != null) _slideValueLabel.Text = v.ToString("F2"); };
        _panel.AddChild(_slideSlider);
        _slideValueLabel = new Label();
        _slideValueLabel.Text = "1.00";
        _slideValueLabel.Position = new Vector2(x + w - 48, y - 2);
        _slideValueLabel.AddThemeFontSizeOverride("font_size", 16);
        _panel.AddChild(_slideValueLabel);
        y += 28 + RowGap;

        float buttonW = 100f;
        _saveButton = new Button();
        _saveButton.Text = "Save";
        _saveButton.Position = new Vector2(x, y);
        _saveButton.Size = new Vector2(buttonW, ButtonH);
        _saveButton.AddThemeFontSizeOverride("font_size", 18);
        _saveButton.Pressed += OnSavePressed;
        _panel.AddChild(_saveButton);

        _closeButton = new Button();
        _closeButton.Text = "Close";
        _closeButton.Position = new Vector2(x + buttonW + 10, y);
        _closeButton.Size = new Vector2(buttonW, ButtonH);
        _closeButton.AddThemeFontSizeOverride("font_size", 18);
        _closeButton.Pressed += OnClosePressed;
        _panel.AddChild(_closeButton);
    }

    public override void _Input(InputEvent @event)
    {
        if (!_isVisibleFlag) return;
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.Escape)
            {
                OnClosePressed();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>Show the dialog and load settings from JSON (or from ego if no file). Pass null to just hide.</summary>
    public void Show(Ego ego)
    {
        _currentEgo = ego;
        if (ego != null)
        {
            var loaded = LoadFromFile();
            if (loaded != null)
            {
                _moveSpeedSpinBox.Value = loaded.MoveSpeed;
                _slideSlider.Value = Mathf.Clamp((double)loaded.SlideFraction, 0, 1);
                _slideValueLabel.Text = loaded.SlideFraction.ToString("F2");
                ApplyToEgo(ego, loaded);
            }
            else
            {
                _moveSpeedSpinBox.Value = ego.MoveSpeed;
                _slideSlider.Value = Mathf.Clamp((double)ego.SlideFraction, 0, 1);
                _slideValueLabel.Text = ego.SlideFraction.ToString("F2");
            }
            _panel.Visible = true;
            _isVisibleFlag = true;
        }
        else
        {
            _panel.Visible = false;
            _isVisibleFlag = false;
        }
    }

    public new void Hide()
    {
        _panel.Visible = false;
        _isVisibleFlag = false;
        _currentEgo = null;
    }

    private void OnSavePressed()
    {
        var data = new EgoSettingsData
        {
            MoveSpeed = (float)_moveSpeedSpinBox.Value,
            SlideFraction = (float)_slideSlider.Value
        };
        SaveToFile(data);
        if (_currentEgo != null)
            ApplyToEgo(_currentEgo, data);
        GD.Print($"Ego settings saved to {SettingsPath}");
    }

    private void OnClosePressed()
    {
        Hide();
    }

    private static void ApplyToEgo(Ego ego, EgoSettingsData data)
    {
        if (ego == null) return;
        ego.MoveSpeed = data.MoveSpeed;
        ego.SlideFraction = Mathf.Clamp(data.SlideFraction, 0f, 1f);
    }

    private static EgoSettingsData LoadFromFile()
    {
        if (!FileAccess.FileExists(SettingsPath))
            return null;
        try
        {
            using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read);
            string json = file.GetAsText();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<EgoSettingsData>(json, options);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"EgoSettings: failed to load {SettingsPath}: {ex.Message}");
            return null;
        }
    }

    private static void SaveToFile(EgoSettingsData data)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            string json = JsonSerializer.Serialize(data, options);
            using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
            file.StoreString(json);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"EgoSettings: failed to save {SettingsPath}: {ex.Message}");
        }
    }

    private class EgoSettingsData
    {
        [JsonPropertyName("moveSpeed")]
        public float MoveSpeed { get; set; } = 100f;
        [JsonPropertyName("slideFraction")]
        public float SlideFraction { get; set; } = 1f;
    }
}
