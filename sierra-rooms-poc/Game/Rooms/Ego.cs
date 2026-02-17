using System;
using System.Collections.Generic;
using Godot;

namespace SierraRooms.Game.Rooms;

public partial class Ego : Node2D
{
    [Export]
    public float MoveSpeed = 100.0f;

    /// <summary>0 = no sliding along walls when blocked, 1 = full sliding (try partial steps and axis slide).</summary>
    public float SlideFraction = 1f;

    /// <summary>Set by RoomRuntime so we can check walkability without relying on GetParent(). If null, we assume walkable.</summary>
    public Func<Vector2I, bool> IsWalkableAtBase;
    /// <summary>When stuck near a path target, RoomRuntime can repath from current position to final target. Return true if a new path was set.</summary>
    public Func<Vector2, Vector2, bool> OnStuckRepath;

    private Vector2 _targetPositionBase; // Current target (single or current waypoint)
    private List<Vector2> _pathWaypoints; // When set, we follow these; target is always _pathWaypoints[0]
    private bool _hasTarget = false;
    private Vector2 _exactBasePosition; // Smooth position for movement logic; display uses rounded pixels
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
    private const float WalkAnimationFps = 10f;
    private float _walkFrameTimer; // Advance walk frame at 10 FPS when moving
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

    /// <summary>Current position in base coords (smooth, used for pathfinding).</summary>
    public Vector2 BasePosition => _exactBasePosition;

    /// <summary>Set position when spawning; use this so exact base position stays in sync.</summary>
    public void SetInitialBasePosition(Vector2 basePos)
    {
        _exactBasePosition = basePos;
        float px = basePos.X * _renderScale;
        float py = basePos.Y * _renderScale;
        Position = new Vector2(Mathf.Round(px), Mathf.Round(py));
    }

    public void SetTarget(Vector2 targetBase)
    {
        _pathWaypoints = null;
        _targetPositionBase = targetBase;
        _hasTarget = true;
        GD.Print($"Ego.SetTarget - Base: {targetBase}, RenderScale: {_renderScale}");
    }

    /// <summary>Clear walk target so the character stops (e.g. when WASD is released).</summary>
    public void ClearTarget()
    {
        _pathWaypoints = null;
        _hasTarget = false;
    }

    /// <summary>Follow path (simplified waypoints). Advance to next waypoint when passed, so direction stays stable and animation is smooth.</summary>
    public void SetPath(List<Vector2> waypoints)
    {
        if (waypoints == null || waypoints.Count == 0) { _pathWaypoints = null; _hasTarget = false; return; }
        _pathWaypoints = new List<Vector2>(waypoints);
        _targetPositionBase = _pathWaypoints[0];
        _hasTarget = true;
    }

    private Vector2 GetBasePosition()
    {
        return _exactBasePosition;
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
        _exactBasePosition = basePos;
        // Snap to whole pixels for display so the sprite doesn't subpixel-jitter
        float px = basePos.X * _renderScale;
        float py = basePos.Y * _renderScale;
        Position = new Vector2(Mathf.Round(px), Mathf.Round(py));
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

            // Path: advance waypoint when we've passed it (within radius OR closer to next) to avoid corner hang-ups and jitter
            bool advanceWaypoint = false;
            if (_pathWaypoints != null && _pathWaypoints.Count > 0)
            {
                float distToCurrent = distance;
                if (distToCurrent < 10f)
                    advanceWaypoint = true;
                else if (_pathWaypoints.Count > 1)
                {
                    Vector2 toNext = _pathWaypoints[1] - currentBase;
                    float distToNext = toNext.Length();
                    if (distToNext < distToCurrent)
                        advanceWaypoint = true; // already past current waypoint
                }
                if (advanceWaypoint)
                {
                    Vector2 reached = _targetPositionBase;
                    _pathWaypoints.RemoveAt(0);
                    if (_pathWaypoints.Count == 0)
                    {
                        SetBasePosition(reached);
                        _pathWaypoints = null;
                        _hasTarget = false;
                    }
                    else
                    {
                        _targetPositionBase = _pathWaypoints[0];
                        direction = _targetPositionBase - currentBase;
                        distance = direction.Length();
                    }
                }
            }
            else if (_pathWaypoints == null && distance < 2.0f)
            {
                SetBasePosition(_targetPositionBase);
                _hasTarget = false;
            }
            if (_hasTarget)
            {
                direction = direction.Normalized();
                int newDir = DirectionToIndex(direction);
                // Hysteresis: only change direction when it differs by 2+ steps (45°+) to avoid jittery flipping
                int diff = Math.Abs(newDir - _currentDirection);
                if (diff > 4) diff = 8 - diff;
                if (diff > 1) _currentDirection = newDir;
                // Tick walk animation at 10 FPS whenever we have a target (even if blocked this frame)
                _walkFrameTimer += (float)delta;
                float frameInterval = 1f / WalkAnimationFps;
                if (_walkFrameTimer >= frameInterval)
                {
                    _walkFrameTimer -= frameInterval;
                    _currentFrame = (_currentFrame + 1) % Cols;
                }
                float moveDist = MoveSpeed * (float)delta;
                Vector2 newBase = currentBase + direction * moveDist;
                float actualMove = 0f;

                // Try progressively smaller steps so we can squeeze through tight spots (e.g. narrow archways)
                float[] stepFractions = { 1f, 0.5f, 0.25f, 0.125f, 0.0625f, 0.03125f };
                int maxSlideSteps = SlideFraction <= 0f ? 0 : Math.Max(1, (int)Math.Round(SlideFraction * stepFractions.Length));
                for (int i = 0; i < maxSlideSteps; i++)
                {
                    float frac = stepFractions[i];
                    float step = moveDist * frac;
                    if (step < 0.05f) break;
                    Vector2 tryBase = currentBase + direction * step;
                    if (GetWalkableAtBase(tryBase))
                    {
                        actualMove = step;
                        newBase = tryBase;
                        break;
                    }
                }
                if (actualMove <= 0f && SlideFraction > 0f)
                {
                    // Direct step blocked: try sliding along X, Y, then half-steps and diagonal
                    float sx = Mathf.Sign(direction.X) * moveDist;
                    float sy = Mathf.Sign(direction.Y) * moveDist;
                    Vector2 stepX = currentBase + new Vector2(sx, 0f);
                    Vector2 stepY = currentBase + new Vector2(0f, sy);
                    if (GetWalkableAtBase(stepX)) { newBase = stepX; actualMove = moveDist; }
                    else if (GetWalkableAtBase(stepY)) { newBase = stepY; actualMove = moveDist; }
                    if (actualMove <= 0f && moveDist >= 0.5f && maxSlideSteps >= 2)
                    {
                        float hx = sx * 0.5f;
                        float hy = sy * 0.5f;
                        if (GetWalkableAtBase(currentBase + new Vector2(hx, 0f))) { newBase = currentBase + new Vector2(hx, 0f); actualMove = moveDist * 0.5f; }
                        else if (GetWalkableAtBase(currentBase + new Vector2(0f, hy))) { newBase = currentBase + new Vector2(0f, hy); actualMove = moveDist * 0.5f; }
                        else if (GetWalkableAtBase(currentBase + new Vector2(hx, hy))) { newBase = currentBase + new Vector2(hx, hy); actualMove = moveDist * 0.5f; }
                    }
                    // Try quarter then eighth steps along axes and diagonal when still stuck (narrow archways, tight corners)
                    if (actualMove <= 0f && moveDist >= 0.25f && maxSlideSteps >= 3)
                    {
                        float qx = sx * 0.25f;
                        float qy = sy * 0.25f;
                        if (GetWalkableAtBase(currentBase + new Vector2(qx, 0f))) { newBase = currentBase + new Vector2(qx, 0f); actualMove = moveDist * 0.25f; }
                        else if (GetWalkableAtBase(currentBase + new Vector2(0f, qy))) { newBase = currentBase + new Vector2(0f, qy); actualMove = moveDist * 0.25f; }
                        else if (GetWalkableAtBase(currentBase + new Vector2(qx, qy))) { newBase = currentBase + new Vector2(qx, qy); actualMove = moveDist * 0.25f; }
                    }
                    if (actualMove <= 0f && moveDist >= 0.15f && maxSlideSteps >= 4)
                    {
                        float ex = sx * 0.125f;
                        float ey = sy * 0.125f;
                        if (GetWalkableAtBase(currentBase + new Vector2(ex, 0f))) { newBase = currentBase + new Vector2(ex, 0f); actualMove = moveDist * 0.125f; }
                        else if (GetWalkableAtBase(currentBase + new Vector2(0f, ey))) { newBase = currentBase + new Vector2(0f, ey); actualMove = moveDist * 0.125f; }
                        else if (GetWalkableAtBase(currentBase + new Vector2(ex, ey))) { newBase = currentBase + new Vector2(ex, ey); actualMove = moveDist * 0.125f; }
                    }
                    // Nudge toward next waypoint when stuck at a corner (e.g. archway, red square entrance)
                    if (actualMove <= 0f && maxSlideSteps >= 5 && _pathWaypoints != null && _pathWaypoints.Count > 1)
                    {
                        Vector2 toNext = _pathWaypoints[1] - currentBase;
                        float len = toNext.Length();
                        if (len > 0.01f)
                        {
                            Vector2 dirNext = toNext / len;
                            float nudge = Mathf.Min(moveDist * 0.5f, len * 0.5f);
                            if (nudge >= 0.1f && GetWalkableAtBase(currentBase + dirNext * nudge))
                            {
                                newBase = currentBase + dirNext * nudge;
                                actualMove = nudge;
                            }
                        }
                    }
                    // Corner: if still stuck but close to current waypoint, advance so we move toward next segment (helps in narrow passages)
                    if (actualMove <= 0f && _pathWaypoints != null && _pathWaypoints.Count > 1 && distance < 15f)
                    {
                        _pathWaypoints.RemoveAt(0);
                        _targetPositionBase = _pathWaypoints[0];
                    }
                }
                else
                    newBase = currentBase + direction * actualMove;

                if (actualMove > 0f)
                {
                    SetBasePosition(newBase);
                    _stuckTimer = 0f;
                }
                else
                {
                    _stuckTimer += (float)delta;
                    if (OnStuckRepath != null && _pathWaypoints != null && _pathWaypoints.Count > 0)
                    {
                        Vector2 finalTarget = _pathWaypoints[_pathWaypoints.Count - 1];
                        if (_stuckTimer >= 0.12f && _stuckTimer - (float)delta < 0.12f)
                        {
                            if (OnStuckRepath(GetBasePosition(), finalTarget))
                                _stuckTimer = 0f;
                        }
                        else if (_stuckTimer >= 0.28f && _stuckTimer - (float)delta < 0.28f)
                        {
                            if (OnStuckRepath(GetBasePosition(), finalTarget))
                                _stuckTimer = 0f;
                        }
                    }
                    if (_stuckTimer >= 0.5f)
                    {
                        _pathWaypoints = null;
                        _hasTarget = false;
                        _stuckTimer = 0f;
                    }
                }
            }
        }
        else
        {
            _currentFrame = 0;
            _walkFrameTimer = 0f;
        }

        if (_atlasTexture != null)
        {
            UpdateAtlasRegion();
        }
    }
}
