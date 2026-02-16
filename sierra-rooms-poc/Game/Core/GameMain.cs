using Godot;
using System.Text.Json;
using SierraRooms.Game.Rooms;
using SierraRooms.Game.UI;

namespace SierraRooms.Game.Core;

public partial class GameMain : Node2D
{
	private UIBar _uiBar;
	
	public override void _Ready()
	{
		// Set viewport background to black
		RenderingServer.SetDefaultClearColor(Colors.Black);
		
		// Force window to 1920x1080
		var window = GetWindow();
		window.Mode = Window.ModeEnum.Windowed;
		window.MinSize = new Vector2I(0, 0);
		window.MaxSize = new Vector2I(0, 0);
		window.Size = new Vector2I(1920, 1080);
		
		GD.Print($"GameMain: Window size set to {window.Size}");
		
		// Pre-read room.json to configure UI
		string roomPath = "res://RoomPackages/QFG1VGA_TOWN_ENTRANCE/room.json";
		var roomData = LoadRoomData(roomPath);
		
		if (roomData != null)
		{
			// Find and configure UIBar with scale for 1920x1080
			_uiBar = GetNode<UIBar>("UIRoot/UIBar");
			if (_uiBar != null)
			{
				float scale = 1920.0f / roomData.BaseSize.W;
				_uiBar.SetRenderScale(scale);
			}
			
			GD.Print($"GameMain initialized at 1920x1080");
		}
		else
		{
			GD.Print("GameMain initialized (no room data)");
		}
	}
	
	private RoomData LoadRoomData(string jsonPath)
	{
		if (!FileAccess.FileExists(jsonPath))
		{
			GD.PrintErr($"Room JSON not found: {jsonPath}");
			return null;
		}

		using var file = FileAccess.Open(jsonPath, FileAccess.ModeFlags.Read);
		string jsonText = file.GetAsText();
		
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};
		
		return JsonSerializer.Deserialize<RoomData>(jsonText, options);
	}
}
