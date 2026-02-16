using Godot;
using System;

namespace SierraRooms.Game.UI;

public partial class TextInputDialog : CanvasLayer
{
    private Panel _panel;
    private Label _promptLabel;
    private LineEdit _lineEdit;
    private Button _okButton;
    private Button _cancelButton;
    private Action<string> _onConfirm;
    private bool _isVisible = false;

    public override void _Ready()
    {
        // Create panel - centered in 1920x1080 window
        _panel = new Panel();
        _panel.Position = new Vector2(460, 400);
        _panel.Size = new Vector2(1000, 280);
        _panel.Visible = false;
        
        // Style panel
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
        styleBox.BorderColor = Colors.Yellow;
        styleBox.SetBorderWidthAll(3);
        _panel.AddThemeStyleboxOverride("panel", styleBox);
        
        AddChild(_panel);
        
        // Create prompt label
        _promptLabel = new Label();
        _promptLabel.Position = new Vector2(20, 20);
        _promptLabel.Size = new Vector2(960, 50);
        _promptLabel.Modulate = Colors.White;
        _promptLabel.AddThemeFontSizeOverride("font_size", 24);
        _panel.AddChild(_promptLabel);
        
        // Create line edit
        _lineEdit = new LineEdit();
        _lineEdit.Position = new Vector2(20, 90);
        _lineEdit.Size = new Vector2(960, 60);
        _lineEdit.AddThemeFontSizeOverride("font_size", 28);
        _panel.AddChild(_lineEdit);
        
        // Create OK button
        _okButton = new Button();
        _okButton.Text = "OK";
        _okButton.Position = new Vector2(650, 200);
        _okButton.Size = new Vector2(150, 60);
        _okButton.AddThemeFontSizeOverride("font_size", 24);
        _okButton.Pressed += OnOkPressed;
        _panel.AddChild(_okButton);
        
        // Create Cancel button
        _cancelButton = new Button();
        _cancelButton.Text = "Cancel";
        _cancelButton.Position = new Vector2(820, 200);
        _cancelButton.Size = new Vector2(150, 60);
        _cancelButton.AddThemeFontSizeOverride("font_size", 24);
        _cancelButton.Pressed += OnCancelPressed;
        _panel.AddChild(_cancelButton);
    }

    public override void _Input(InputEvent @event)
    {
        if (!_isVisible)
            return;
            
        // Submit on Enter
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter)
            {
                OnOkPressed();
            }
            else if (keyEvent.Keycode == Key.Escape)
            {
                OnCancelPressed();
            }
        }
    }

    public void Show(string prompt, string currentValue, Action<string> onConfirm)
    {
        _promptLabel.Text = prompt;
        _lineEdit.Text = currentValue;
        _onConfirm = onConfirm;
        _panel.Visible = true;
        _isVisible = true;
        _lineEdit.GrabFocus();
        _lineEdit.SelectAll();
    }

    private void OnOkPressed()
    {
        string text = _lineEdit.Text;
        Hide();
        _onConfirm?.Invoke(text);
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
