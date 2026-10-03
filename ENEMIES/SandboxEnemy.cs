using Godot;

// Simple robot that pursues the player along terrain navigation paths.
public partial class SandboxEnemy : CharacterBody3D
{
	#region Settings and Fields

	[Export] public float MoveSpeed = 2.5f;
	[Export] public float Gravity = 20.0f;
	[Export] public float StopDistance = 1.5f;

	public SandboxPlayer Target;
	public SandboxTerrain Terrain;
	public WorldNavigation Navigation;

	private NavigationAgent3D _agent;
	private float _repathTimer;
	private Vector3 _spawnPosition;

	#endregion

	#region Initialisation

	// =========================================================
	// Create collision, placeholder robot geometry, and path following.
	public override void _Ready()
	{
		CollisionLayer = 4;
		CollisionMask = 3;
		FloorSnapLength = 0.3f;
		_spawnPosition = GlobalPosition;

		AddChild(new CollisionShape3D
		{
			Position = new Vector3(0, 0.75f, 0),
			Shape = new CapsuleShape3D
			{
				Radius = 0.3f,
				Height = 1.5f
			}
		});

		CreateVisuals();

		_agent = new NavigationAgent3D
		{
			Name = "NavigationAgent",
			PathDesiredDistance = 0.5f,
			TargetDesiredDistance = StopDistance,
			AvoidanceEnabled = false
		};
		AddChild(_agent);
	}

	// =========================================================
	// Build a grey robot with a red panel showing its forward direction.
	private void CreateVisuals()
	{
		var visuals = new Node3D { Name = "Visuals" };
		AddChild(visuals);

		visuals.AddChild(new MeshInstance3D
		{
			Position = new Vector3(0, 0.75f, 0),
			Mesh = new CapsuleMesh
			{
				Radius = 0.3f,
				Height = 1.5f
			},
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color("#808995"),
				Roughness = 0.8f
			}
		});

		visuals.AddChild(new MeshInstance3D
		{
			Position = new Vector3(0, 1.15f, -0.29f),
			Mesh = new BoxMesh
			{
				Size = new Vector3(0.3f, 0.12f, 0.08f)
			},
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color("#e64c4c"),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			}
		});
	}

	#endregion

	#region Pursuit

	// =========================================================
	// Follow the next path point and use normal character collision.
	public override void _PhysicsProcess(double delta)
	{
		if (!Terrain.HasTerrainNear(GlobalPosition))
		{
			Velocity = Vector3.Zero;
			return;
		}

		float dt = (float)delta;
		Vector3 direction = Vector3.Zero;
		_repathTimer -= dt;

		if (Navigation.Ready && GodotObject.IsInstanceValid(Target))
		{
			if (_repathTimer <= 0)
			{
				_agent.TargetPosition = Target.GlobalPosition;
				_repathTimer = 0.5f;
			}

			if (GlobalPosition.DistanceTo(Target.GlobalPosition) > StopDistance
				&& !_agent.IsNavigationFinished())
			{
				Vector3 next = _agent.GetNextPathPosition();
				direction = next - GlobalPosition;
				direction.Y = 0;

				if (direction.LengthSquared() > 0.001f)
					direction = direction.Normalized();
			}
		}

		Vector3 velocity = Velocity;
		velocity.X = direction.X * MoveSpeed;
		velocity.Z = direction.Z * MoveSpeed;
		velocity.Y = IsOnFloor() ? 0 : velocity.Y - Gravity * dt;
		Velocity = velocity;
		MoveAndSlide();

		if (direction.LengthSquared() > 0)
			Rotation = new Vector3(
				0, Mathf.Atan2(-direction.X, -direction.Z), 0);

		if (GlobalPosition.Y < -56)
		{
			GlobalPosition = _spawnPosition;
			Velocity = Vector3.Zero;
		}
	}

	#endregion
}
