using Godot;

public partial class SandboxLevel : Node3D
{
	[Export] public float FloorSize = 60.0f;
	[Export] public Color FloorColour = new Color("#173d27");

	public override void _Ready()
	{
		CreateFloor();
		CreateEnvironment();
	}

	// One solid floor, with its top surface at Y = 0.
	private void CreateFloor()
	{
		Vector3 size = new Vector3(FloorSize, 1, FloorSize);

		var floor = new StaticBody3D
		{
			Name = "Floor",
			Position = new Vector3(0, -0.5f, 0),
			CollisionLayer = 1,
			CollisionMask = 0
		};

		floor.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = size },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = FloorColour,
				Roughness = 1.0f
			}
		});

		floor.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = size }
		});

		AddChild(floor);
	}

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
				AmbientLightEnergy = 0.6f
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
}
