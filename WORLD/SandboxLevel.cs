using Godot;

// Creates the sandbox terrain, lighting, player spawn, and test HUD.
public partial class SandboxLevel : Node3D
{
		#region Settings and Fields

	[Export] public TerrainSettings WorldSettings = new();

	private SandboxTerrain _terrain;
	private ChunkStreamer _streamer;
	private WorldNavigation _navigation;
	private SandboxEnemy _enemy;
	private Label _debugLabel;
	private float _debugTimer;

	#endregion

	#region Initialisation

		// =========================================================
	// Create terrain streaming, navigation, and one pursuit-test enemy.
	public override void _Ready()
	{
		CreateEnvironment();

		WorldSettings ??= new TerrainSettings();
		_terrain = new SandboxTerrain
		{
			Name = "Terrain",
			Settings = WorldSettings
		};
		AddChild(_terrain);

		var player = GetNode<SandboxPlayer>("SandboxPlayer");
		player.Position = new Vector3(
			0, _terrain.GetSurfaceHeight(0, 8) + 0.2f, 8);
		player.RespawnPosition = player.GlobalPosition;
		player.FallResetY = -56.0f;
		player.Velocity = Vector3.Zero;

		var systems = new Node3D { Name = "WorldSystems" };
		AddChild(systems);

		_navigation = new WorldNavigation
		{
			Name = "Navigation",
			Terrain = _terrain
		};
		systems.AddChild(_navigation);

		var enemies = new Node3D { Name = "Enemies" };
		AddChild(enemies);

		_enemy = new SandboxEnemy
		{
			Name = "TestRobot",
			Position = new Vector3(
				6, _terrain.GetSurfaceHeight(6, 10) + 0.2f, 10),
			Target = player,
			Terrain = _terrain,
			Navigation = _navigation
		};
		enemies.AddChild(_enemy);

		_streamer = new ChunkStreamer
		{
			Name = "ChunkStreamer",
			Terrain = _terrain,
			Player = player,
			SecondaryObserver = _enemy
		};
		systems.AddChild(_streamer);

		CreateCameraLight(player);
		CreateHud();
	}

	#endregion

	#region Lighting

	// =========================================================
	// Create daylight and ambient illumination.
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
	// Attach a temporary exploration light to the player camera.
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

	#endregion

	#region HUD

		// =========================================================
	// Display aiming, controls, and streaming/navigation counters.
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

		_debugLabel = new Label
		{
			Position = new Vector2(16, 70),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		hud.AddChild(_debugLabel);
	}

	#endregion
	
		#region Debug Updates

	// =========================================================
	// Refresh debug text periodically instead of every frame.
	public override void _Process(double delta)
	{
		_debugTimer -= (float)delta;
		if (_debugTimer > 0) return;
		_debugTimer = 0.25f;

		string navigationState = _navigation.IsBaking
			? "Baking"
			: _navigation.Ready ? "Ready" : "Waiting for terrain";

		_debugLabel.Text =
			$"Chunks: {_terrain.LoadedColumnCount}   "
			+ $"Pending sections: {_terrain.PendingSections}\n"
			+ $"Navigation: {navigationState}";
	}

	#endregion
}
