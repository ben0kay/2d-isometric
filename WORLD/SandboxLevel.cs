using Godot;

// Test level: editable terrain, lighting, player spawn, and crosshair.
public partial class SandboxLevel : Node3D
{
	private SandboxTerrain _terrain;

	// =========================================================
	// Build the test world and position the existing player instance.
	public override void _Ready()
	{
		CreateEnvironment();

		_terrain = new SandboxTerrain { Name = "Terrain" };
		AddChild(_terrain);

		var player = GetNode<SandboxPlayer>("SandboxPlayer");
		const float spawnX = 0;
		const float spawnZ = 8;

		player.Position = new Vector3(
			spawnX,
			_terrain.GetSurfaceHeight(spawnX, spawnZ) + 0.2f,
			spawnZ);

		CreateCameraLight(player);
		CreateHud();
	}

	// =========================================================
	// Simple daylight for the initial terrain test.
	private void CreateEnvironment()
	{
		AddChild(new WorldEnvironment
		{
			Name = "WorldEnvironment",
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color("#8ca6b8"),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color("#d1dce5"),
				AmbientLightEnergy = 0.25f
			}
		});

		AddChild(new DirectionalLight3D
		{
			Name = "Sunlight",
			RotationDegrees = new Vector3(-55, -30, 0),
			LightEnergy = 1.0f,
			ShadowEnabled = true
		});
	}

	// =========================================================
	// Temporary personal light so freshly excavated tunnels are visible.
	private void CreateCameraLight(SandboxPlayer player)
	{
		var camera = player.GetNode<Camera3D>("Camera");

		camera.AddChild(new OmniLight3D
		{
			Name = "TestLight",
			Position = new Vector3(0, 0, -0.2f),
			LightColor = new Color("#e0e8f1"),
			LightEnergy = 1.3f,
			OmniRange = 10.0f,
			ShadowEnabled = false
		});
	}

	// =========================================================
	// Crosshair and compact test controls.
	private void CreateHud()
	{
		var hud = new CanvasLayer { Name = "HUD" };
		AddChild(hud);

		var crosshair = new Label
		{
			Text = "+",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};

		hud.AddChild(crosshair);
		crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		crosshair.OffsetLeft = -12;
		crosshair.OffsetRight = 12;
		crosshair.OffsetTop = -12;
		crosshair.OffsetBottom = 12;
		crosshair.AddThemeFontSizeOverride("font_size", 24);

		hud.AddChild(new Label
		{
			Position = new Vector2(16, 16),
			Text = "Arrow Keys: Walk   |   Right Mouse: Jump\n"
				+ "Hold Left Mouse: Dig   |   Escape: Release Mouse",
			MouseFilter = Control.MouseFilterEnum.Ignore
		});
	}
}
