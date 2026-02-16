using System;
using Godot;

namespace SierraRooms.Game.Rooms;

public partial class Ego : Node2D
{
    [Export]
    public float MoveSpeed = 100.0f;

    /// <summary>Set by RoomRuntime so we can check walkability without relying on GetParent(). If null, we assume walkable.</summary>
    public Func<Vector2I, bool> IsWalkableAtBase;

    private Vector2 _targetPositionBase; // Target in base coords
    private bool _hasTarget = false;
    private Sprite2D _sprite;
    private AtlasTexture _atlasTexture;
    private float _renderScale = 1.0f;

    // Sprite sheet: 8x8 grid
    private const int Cols = 8;
    private const int Rows = 8;
    private int _frameWidth;
    private int _frameHeight;

    // Direction: 0=Left, 1=Right, 2=Down, 3=Up, 4=SE, 5=SW, 6=NE, 7=NW
    private int _currentDirection = 2; // Down by default
    private int _currentFrame = 0;
    private float _distanceMoved; // Accumulate distance to advance walk frame
    private Vector2 _lastBasePosition; // For stuck detection
    private float _stuckTimer; // Seconds without moving toward target -> clear target

    public override void _Ready()
    {
        var sheetTexture = GD.Load<Texture2D>("res://Sprites/Character/Ego.png");
        if (sheetTexture == null)
        {
            GD.PrintErr("Ego: Failed to load sprite sheet res://Sprites/Character/Ego.png");
            // Fallback to placeholder
            CreatePlaceholderSprite();
            return;
        }

        _frameWidth = sheetTexture.GetWidth() / Cols;
        _frameHeight = sheetTexture.GetHeight() / Rows;
        GD.Print($"Ego sprite sheet: {sheetTexture.GetWidth()}x{sheetTexture.GetHeight()}, frame: {_frameWidth}x{_frameHeight}");

        _atlasTexture = new AtlasTexture();
        _atlasTexture.Atlas = sheetTexture;
        _atlasTexture.Region = new Rect2(0, 0, _frameWidth, _frameHeight);

        _sprite = new Sprite2D();
        _sprite.Texture = _atlasTexture;
        _sprite.TextureFilter = TextureFilterEnum.Nearest;
        _sprite.Centered = false;
        // Position so bottom-center of sprite is at ego's Position (foot position)
        _sprite.Position = new Vector2(-_frameWidth / 2f, -_frameHeight) * _renderScale;
        _sprite.Scale = new Vector2(_renderScale, _renderScale);
        AddChild(_sprite);
    }

    private void CreatePlaceholderSprite()
    {
        _sprite = new Sprite2D();
        _sprite.Modulate = Colors.Red;
        _sprite.TextureFilter = TextureFilterEnum.Nearest;
        _sprite.Centered = true;
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        _sprite.Texture = ImageTexture.CreateFromImage(image);
        _sprite.Scale = new Vector2(8 * _renderScale, 12 * _renderScale);
        AddChild(_sprite);
    }

    public void SetRenderScale(float scale)
    {
        _renderScale = scale;
        if (_sprite != null && _atlasTexture != null)
        {
            _sprite.Scale = new Vector2(_renderScale, _renderScale);
            _sprite.Position = new Vector2(-_frameWidth / 2f, -_frameHeight) * _renderScale;
        }
        else if (_sprite != null)
        {
            _sprite.Scale = new Vector2(8 * _renderScale, 12 * _renderScale);
        }
    }

    public void SetTarget(Vector2 targetBase)
    {
        _targetPositionBase = targetBase;
        _hasTarget = true;
        GD.Print($"Ego.SetTarget - Base: {targetBase}, RenderScale: {_renderScale}");
    }

    private Vector2 GetBasePosition()
    {
        return Position / _renderScale;
    }

    /// <summary>True if the given base position is walkable (uses callback from RoomRuntime). Used so we don't walk through walls.</summary>
    private bool GetWalkableAtBase(Vector2 basePos)
    {
        if (IsWalkableAtBase == null) return true;
        var p = new Vector2I(Mathf.FloorToInt(basePos.X), Mathf.FloorToInt(basePos.Y));
        return IsWalkableAtBase(p);
    }

    private void SetBasePosition(Vector2 basePos)
    {
        Position = basePos * _renderScale;
    }

    // Sheet layout: 0=Left, 1=Right, 2=Down, 3=Up, 4=SE, 5=SW, 6=NE, 7=NW
    private int DirectionToIndex(Vector2 direction)
    {
        if (direction.LengthSquared() < 0.01f) return _currentDirection;

        float angle = Mathf.Atan2(direction.Y, direction.X);
        float deg = Mathf.RadToDeg(angle);
        if (deg >= -22.5f && deg < 22.5f) return 1;   // Right
        if (deg >= 22.5f && deg < 67.5f) return 4;    // SE
        if (deg >= 67.5f && deg < 112.5f) return 2;   // Down
        if (deg >= 112.5f && deg < 157.5f) return 5;  // SW
        if (deg >= 157.5f || deg < -157.5f) return 0; // Left
        if (deg >= -157.5f && deg < -112.5f) return 7; // NW
        if (deg >= -112.5f && deg < -67.5f) return 3;  // Up
        return 6;  // NE (-67.5 to -22.5)
    }

    private void UpdateAtlasRegion()
    {
        if (_atlasTexture == null) return;

        int row = Mathf.Clamp(_currentDirection, 0, Rows - 1);
        int col = _currentFrame % Cols;
        _atlasTexture.Region = new Rect2(col * _frameWidth, row * _frameHeight, _frameWidth, _frameHeight);
    }

    public override void _Process(double delta)
    {
        if (_hasTarget)
        {
            Vector2 currentBase = GetBasePosition();
            Vector2 direction = _targetPositionBase - currentBase;
            float distance = direction.Length();

            if (distance < 2.0f)
            {
                SetBasePosition(_targetPositionBase);
                _hasTarget = false;
                GD.Print($"Ego reached target at base: {_targetPositionBase}");
            }
            else
            {
                direction = direction.Normalized();
                _currentDirection = DirectionToIndex(direction);
                float moveDist = MoveSpeed * (float)delta;
                Vector2 newBase = currentBase + direction * moveDist;
                float actualMove = 0f;

                if (GetWalkableAtBase(newBase))
                    actualMove = moveDist;
                else if (moveDist > 0.5f)
                {
                    float half = moveDist * 0.5f;
                    newBase = currentBase + direction * half;
                    if (GetWalkableAtBase(newBase))
                        actualMove = half;
                }
                if (actualMove <= 0f)
                {
                    // Direct (and half) step blocked: try sliding along X or Y so we don't get stuck on corners
                    Vector2 stepX = currentBase + new Vector2(Mathf.Sign(direction.X) * moveDist, 0f);
                    Vector2 stepY = currentBase + new Vector2(0f, Mathf.Sign(direction.Y) * moveDist);
                    bool okX = GetWalkableAtBase(stepX);
                    bool okY = GetWalkableAtBase(stepY);
                    if (okX) { newBase = stepX; actualMove = moveDist; }
                    else if (okY) { newBase = stepY; actualMove = moveDist; }
                }
                else
                    newBase = currentBase + direction * actualMove;

                if (actualMove > 0f)
                {
                    SetBasePosition(newBase);
                    _distanceMoved += actualMove;
                    if (_distanceMoved >= 4f) { _distanceMoved -= 4f; _currentFrame = (_currentFrame + 1) % Cols; }
                    _stuckTimer = 0f;
                }
                else
                {
                    _stuckTimer += (float)delta;
                    if (_stuckTimer >= 0.4f)
                    {
                        _hasTarget = false;
                        _stuckTimer = 0f;
                    }
                }
            }
        }
        else
        {
            _currentFrame = 0;
            _distanceMoved = 0;
        }

        if (_atlasTexture != null)
        {
            UpdateAtlasRegion();
        }
    }
}
