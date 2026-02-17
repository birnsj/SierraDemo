using Godot;
using System;
using System.Text.Json;

namespace SierraRooms.Game.Rooms;

/// <summary>Result of loading a room package. RoomRuntime applies this (sprites, A*, ego) to the scene.</summary>
public class RoomLoadResult
{
    public RoomData Data { get; set; }
    public Image ControlImage { get; set; }
    public Image PriorityImage { get; set; }
    public string PackagePath { get; set; }
}

/// <summary>Loads room.json and control/priority images. Does not create scene nodes or A*; RoomRuntime does that.</summary>
public static class RoomLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Load room data and control/priority images. Returns null if JSON not found. Creates default control/priority images if files missing.</summary>
    public static RoomLoadResult Load(string roomPackagePath, int resolutionMode = 1)
    {
        if (!FileAccess.FileExists(roomPackagePath))
        {
            GD.PrintErr($"Room JSON not found: {roomPackagePath}");
            return null;
        }

        using var file = FileAccess.Open(roomPackagePath, FileAccess.ModeFlags.Read);
        string jsonText = file.GetAsText();
        var roomData = JsonSerializer.Deserialize<RoomData>(jsonText, JsonOptions);
        if (roomData == null)
        {
            GD.PrintErr("Failed to deserialize room.json");
            return null;
        }

        NormalizeHotspotPoints(roomData);
        string basePath = roomPackagePath.GetBaseDir();

        Image controlImage = LoadControlImage(roomData, basePath, resolutionMode);
        Image priorityImage = LoadPriorityImage(roomData, basePath, resolutionMode);

        GD.Print($"RoomLoader: Loaded {roomData.DisplayName} (ID: {roomData.Id}), base size {roomData.BaseSize.W}x{roomData.BaseSize.H}");

        return new RoomLoadResult
        {
            Data = roomData,
            ControlImage = controlImage,
            PriorityImage = priorityImage,
            PackagePath = roomPackagePath
        };
    }

    /// <summary>Ensure every hotspot has Points (from Rect if missing).</summary>
    public static void NormalizeHotspotPoints(RoomData roomData)
    {
        if (roomData?.Hotspots == null) return;
        foreach (var h in roomData.Hotspots)
        {
            if (h.Points == null && h.Rect != null)
                h.Points = RectToPoints(h.Rect);
        }
    }

    private static Vector2Data[] RectToPoints(RectData r)
    {
        return new[]
        {
            new Vector2Data { X = r.X, Y = r.Y },
            new Vector2Data { X = r.X + r.W, Y = r.Y },
            new Vector2Data { X = r.X + r.W, Y = r.Y + r.H },
            new Vector2Data { X = r.X, Y = r.Y + r.H }
        };
    }

    private static string GetControlFileName(RoomData roomData, int resolutionMode)
    {
        if (roomData?.Assets?.Control != null)
            return roomData.Assets.Control;
        return "control.png";
    }

    private static string GetPriorityFileName(RoomData roomData, int resolutionMode)
    {
        return roomData?.Assets?.Priority ?? "priority.png";
    }

    private static Image LoadControlImage(RoomData roomData, string basePath, int resolutionMode)
    {
        string controlFile = GetControlFileName(roomData, resolutionMode);
        string controlPath = basePath.PathJoin(controlFile);

        if (FileAccess.FileExists(controlPath))
        {
            var image = Image.LoadFromFile(controlPath);
            if (image == null)
            {
                var texture = GD.Load<Texture2D>(controlPath);
                if (texture != null)
                    image = texture.GetImage();
            }
            if (image != null && image.GetWidth() > 0 && image.GetHeight() > 0)
            {
                if (image.GetFormat() != Image.Format.Rgba8)
                    image.Convert(Image.Format.Rgba8);
                if (image.GetWidth() != roomData.BaseSize.W || image.GetHeight() != roomData.BaseSize.H)
                    GD.PrintErr($"Control map size ({image.GetWidth()}x{image.GetHeight()}) does NOT match baseSize ({roomData.BaseSize.W}x{roomData.BaseSize.H})");
                GD.Print($"RoomLoader: Control map loaded {controlFile}");
                return image;
            }
        }

        GD.PrintErr($"Control map not found or invalid: {controlPath}, creating default");
        return CreateDefaultControlImage(roomData.BaseSize.W, roomData.BaseSize.H);
    }

    private static Image LoadPriorityImage(RoomData roomData, string basePath, int resolutionMode)
    {
        string priorityFile = GetPriorityFileName(roomData, resolutionMode);
        string priorityPath = basePath.PathJoin(priorityFile);

        if (FileAccess.FileExists(priorityPath))
        {
            var texture = GD.Load<Texture2D>(priorityPath);
            if (texture != null)
            {
                var image = texture.GetImage();
                if (image != null && image.GetWidth() > 0 && image.GetHeight() > 0)
                {
                    if (image.GetWidth() != roomData.BaseSize.W || image.GetHeight() != roomData.BaseSize.H)
                        GD.PrintErr($"Priority map size does NOT match baseSize");
                    GD.Print($"RoomLoader: Priority map loaded {priorityFile}");
                    return image;
                }
            }
        }

        GD.PrintErr($"Priority map not found or invalid: {priorityPath}, creating default");
        return CreateDefaultPriorityImage(roomData.BaseSize.W, roomData.BaseSize.H);
    }

    private static Image CreateDefaultControlImage(int w, int h)
    {
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        image.Fill(Colors.White);
        GD.Print($"RoomLoader: Created default control map {w}x{h}");
        return image;
    }

    private static Image CreateDefaultPriorityImage(int w, int h)
    {
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        image.Fill(Colors.White);
        GD.Print($"RoomLoader: Created default priority map {w}x{h}");
        return image;
    }

    /// <summary>Get backdrop filename for the room (fallback; resolution-specific can be added).</summary>
    public static string GetBackdropFileName(RoomData roomData, bool useLarge)
    {
        return useLarge ? (roomData?.Assets?.PicBase ?? "pic_base_large.png") : "pic_base.png";
    }
}
