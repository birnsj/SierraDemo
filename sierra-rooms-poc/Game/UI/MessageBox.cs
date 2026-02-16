using Godot;

namespace SierraRooms.Game.UI;

public partial class MessageBox : CanvasLayer
{
    private Panel _panel;
    private Label _messageLabel;
    private bool _isVisible = false;
    public bool IsVisible => _isVisible;

    public override void _Ready()
    {
        // Create panel - sized for 1920x1080 window
        _panel = new Panel();
        _panel.Position = new Vector2(100, 800);
        _panel.Size = new Vector2(1720, 200);
        _panel.Visible = false;
        
        // Style panel with dark background
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0, 0, 0, 0.9f);
        styleBox.BorderColor = Colors.Yellow;
        styleBox.SetBorderWidthAll(3);
        _panel.AddThemeStyleboxOverride("panel", styleBox);
        
        AddChild(_panel);
        
        // Create label
        _messageLabel = new Label();
        _messageLabel.Position = new Vector2(20, 20);
        _messageLabel.Size = new Vector2(1680, 160);
        _messageLabel.Modulate = Colors.Yellow;
        _messageLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _messageLabel.AddThemeFontSizeOverride("font_size", 28);
        _panel.AddChild(_messageLabel);
    }

    public override void _Input(InputEvent @event)
    {
        if (!_isVisible)
            return;
            
        // Close on any mouse click or key press
        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            Hide();
        }
        else if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            Hide();
        }
    }

    public void Show(string message)
    {
        _messageLabel.Text = message;
        _panel.Visible = true;
        _isVisible = true;
    }

    public new void Hide()
    {
        _panel.Visible = false;
        _isVisible = false;
    }
}
