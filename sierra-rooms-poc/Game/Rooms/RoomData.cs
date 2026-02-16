using Godot;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SierraRooms.Game.Rooms;

/// <summary>Room definition. All coordinates (hotspots, exits, spawn, walkability) use base space: origin top-left, units in base pixels (room.json baseSize). Widescreen: use a wider baseSize (e.g. 640x360) in room.json; layout and viewport already scale from baseSize.</summary>
public partial class RoomData
{
    public string Id { get; set; }
    public string DisplayName { get; set; }
    /// <summary>Room size in base coordinates (e.g. 320x190; widescreen can be e.g. 640x360). All room coords are in [0, W) x [0, H).</summary>
    public SizeData BaseSize { get; set; }
    public AssetsData Assets { get; set; }
    public float RenderScale { get; set; }
    public Vector2Data Spawn { get; set; }
    public ControlRuleData ControlRule { get; set; }
    public int FootOffsetY { get; set; }
    public ExitData[] Exits { get; set; }
    public HotspotData[] Hotspots { get; set; }
}

public partial class SizeData
{
    public int W { get; set; }
    public int H { get; set; }
}

public partial class AssetsData
{
    public string PicBase { get; set; }
    public string PicBase1x { get; set; }
    public string PicBase6x { get; set; }
    public string PicBase12x { get; set; }
    public string Control { get; set; }
    public string Control1x { get; set; }
    public string Control6x { get; set; }
    public string Control12x { get; set; }
    public string Priority { get; set; }
    public string Priority1x { get; set; }
    public string Priority6x { get; set; }
    public string Priority12x { get; set; }
    public string PicUpres { get; set; }
}

public partial class Vector2Data
{
    public float X { get; set; }
    public float Y { get; set; }
}

public partial class ControlRuleData
{
    public float WalkableIfLumaGte { get; set; }
    public bool Invert { get; set; }
}

public partial class ExitData
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
    
    [JsonPropertyName("rect")]
    public RectData Rect { get; set; }
    
    [JsonPropertyName("targetRoomId")]
    public string TargetRoomId { get; set; }
    
    [JsonPropertyName("targetSpawn")]
    public Vector2Data TargetSpawn { get; set; }
}

public partial class HotspotData
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
    
    [JsonPropertyName("rect")]
    public RectData Rect { get; set; }
    
    /// <summary>Polygon vertices in base coords. If null, derived from Rect. Editor uses these; save both for compat.</summary>
    [JsonPropertyName("points")]
    public Vector2Data[] Points { get; set; }
    
    [JsonPropertyName("verbs")]
    public VerbActionsData Verbs { get; set; }
}

public partial class RectData
{
    [JsonPropertyName("x")]
    public int X { get; set; }
    
    [JsonPropertyName("y")]
    public int Y { get; set; }
    
    [JsonPropertyName("w")]
    public int W { get; set; }
    
    [JsonPropertyName("h")]
    public int H { get; set; }
}

public partial class VerbActionsData
{
    [JsonPropertyName("look")]
    public VerbActionData? Look { get; set; }
    
    [JsonPropertyName("use")]
    public VerbActionData? Use { get; set; }
    
    [JsonPropertyName("talk")]
    public VerbActionData? Talk { get; set; }
}

public partial class VerbActionData
{
    [JsonPropertyName("type")]
    public string Type { get; set; }
    
    [JsonPropertyName("value")]
    public string Value { get; set; }
}