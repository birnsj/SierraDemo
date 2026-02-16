using Godot;

namespace SierraRooms.Game.UI;

public partial class UIBar : Control
{
    private TextureRect _barImage;
    private float _currentRenderScale = 1.0f;
    private int _baseBarHeight = 10; // Will be set from image height
    private const float HOVER_ZONE_HEIGHT = 30.0f; // Pixels from top to trigger show
    private const float FADE_SPEED = 8.0f; // Speed of fade in/out
    
    private bool _isMouseOver = false;
    private float _visibilityAlpha = 0.0f; // 0 = hidden, 1 = visible

    public override void _Ready()
    {
        // Create black background first
        var background = new ColorRect();
        background.Color = Colors.Black;
        background.AnchorRight = 1.0f;
        background.AnchorBottom = 1.0f;
        AddChild(background);
        
        // Load and create texture rect for the bar image
        var texture = GD.Load<Texture2D>("res://Game/UI/TopbarTemp.png");
        
        if (texture == null)
        {
            GD.PrintErr("Failed to load TopbarTemp.png!");
            return;
        }
        
        // Get the actual height from the image
        _baseBarHeight = texture.GetHeight();
        GD.Print($"UIBar image loaded: {texture.GetWidth()}x{texture.GetHeight()}");
        
        _barImage = new TextureRect();
        _barImage.Texture = texture;
        _barImage.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _barImage.StretchMode = TextureRect.StretchModeEnum.Scale;
        _barImage.TextureFilter = CanvasItem.TextureFilterEnum.Nearest; // Pixel-perfect
        _barImage.AnchorRight = 1.0f; // Fill width
        _barImage.AnchorBottom = 1.0f; // Fill height of parent
        AddChild(_barImage);
        
        // Set initial anchors - top-left, full width
        AnchorLeft = 0.0f;
        AnchorTop = 0.0f;
        AnchorRight = 1.0f; // Fill viewport width
        AnchorBottom = 0.0f;
        
        OffsetLeft = 0;
        OffsetTop = 0;
        
        // Set initial size
        UpdateBarSize();
        
        // Start fully visible for testing
        Modulate = new Color(1, 1, 1, 1);
        _visibilityAlpha = 1.0f;
        
        GD.Print($"UIBar initialized:");
        GD.Print($"  Position: {Position}");
        GD.Print($"  Size: {Size}");
        GD.Print($"  GlobalPosition: {GlobalPosition}");
        GD.Print($"  Base height: {_baseBarHeight}");
    }

    public override void _Process(double delta)
    {
        // Check if mouse is in hover zone (top area of screen)
        Vector2 mousePos = GetViewport().GetMousePosition();
        bool inHoverZone = mousePos.Y < HOVER_ZONE_HEIGHT;
        
        // Update target visibility
        float targetAlpha = inHoverZone ? 1.0f : 0.0f;
        
        // Smooth fade
        _visibilityAlpha = Mathf.Lerp(_visibilityAlpha, targetAlpha, (float)delta * FADE_SPEED);
        
        // Apply alpha to modulate
        Modulate = new Color(1, 1, 1, _visibilityAlpha);
        
        // Only process input when visible
        MouseFilter = (_visibilityAlpha > 0.1f) ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
    }

    /// <summary>Update bar size based on current render scale</summary>
    public void SetRenderScale(float renderScale)
    {
        _currentRenderScale = renderScale;
        UpdateBarSize();
        GD.Print($"UIBar render scale set to {renderScale}, height: {CustomMinimumSize.Y}");
    }

    private void UpdateBarSize()
    {
        int scaledHeight = Mathf.RoundToInt(_baseBarHeight * _currentRenderScale);
        
        // Set the control's minimum size
        CustomMinimumSize = new Vector2(0, scaledHeight);
        Size = new Vector2(Size.X, scaledHeight);
        OffsetBottom = scaledHeight;
        
        // Update bar image size
        if (_barImage != null)
        {
            _barImage.Size = new Vector2(_barImage.Size.X, scaledHeight);
            _barImage.CustomMinimumSize = new Vector2(0, scaledHeight);
            _barImage.AnchorBottom = 1.0f;
            _barImage.OffsetBottom = 0;
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Only block input when bar is visible
        if (_visibilityAlpha > 0.1f && @event is InputEventMouse mouseEvent)
        {
            Vector2 mousePos = GetViewport().GetMousePosition();
            Rect2 barRect = new Rect2(GlobalPosition, Size);
            
            if (barRect.HasPoint(mousePos))
            {
                // Mouse is over UI bar - consume the event to prevent room interaction
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
