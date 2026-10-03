using Godot;

public partial class SandboxPlayer : CharacterBody3D
{
	// Movement.
	[Export] public float WalkSpeed = 4.5f;
	[Export] public float Acceleration = 24.0f;
	[Export] public float JumpSpeed = 6.0f;
	[Export] public float Gravity = 20.0f;

	// Mouse look. Sensitivity is measured in degrees per pixel.
	[Export] public float MouseSensitivity = 0.15f;
	[Export] public bool InvertY = true;
	[Export] public bool InvertX = false;
	[Export] public float CameraFov = 80.0f;
	
		[Export] public float FallResetY = -40.0f;
	public Vector3 RespawnPosition = new(0, 2, 8);

	private Camera3D _camera;
	private float _pitch;
	private bool _jumpRequested;

	public override void _Ready()
	{
		CollisionLayer = 2;
		CollisionMask = 1;
		FloorSnapLength = 0.2f;

		CreateBody();
		CreateCamera();
		CreateHands();

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	// Invisible collision body. The player origin is at the feet.
	private void CreateBody()
	{
		AddChild(new CollisionShape3D
		{
			Name = "BodyCollision",
			Position = new Vector3(0, 0.9f, 0),
			Shape = new CapsuleShape3D
			{
				Radius = 0.3f,
				Height = 1.8f
			}
		});
	}

	private void CreateCamera()
	{
		_camera = new Camera3D
		{
			Name = "Camera",
			Position = new Vector3(0, 1.62f, 0),
			Fov = CameraFov,
			Near = 0.03f,
			Current = true
		};

		AddChild(_camera);
	}

	// Grey placeholder hands, attached to the camera.
	private void CreateHands()
	{
		var hands = new Node3D { Name = "Hands" };
		_camera.AddChild(hands);

		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color("#92979e"),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			NoDepthTest = true,
			RenderPriority = 10
		};

		foreach (int side in new[] { -1, 1 })
		{
			var arm = new Node3D
			{
				Name = side < 0 ? "LeftHand" : "RightHand",
				Position = new Vector3(side * 0.3f, -0.26f, -0.48f),
				RotationDegrees = new Vector3(-8, side * -8, side * -8)
			};

			hands.AddChild(arm);

			AddHandMesh(
				arm,
				new Vector3(0, -0.07f, 0.15f),
				new Vector3(0.12f, 0.13f, 0.32f),
				material);

			AddHandMesh(
				arm,
				new Vector3(0, -0.02f, -0.06f),
				new Vector3(0.14f, 0.12f, 0.17f),
				material);
		}
	}

	private void AddHandMesh(Node3D parent, Vector3 position,
		Vector3 size, Material material)
	{
		parent.AddChild(new MeshInstance3D
		{
			Position = position,
			Mesh = new BoxMesh { Size = size },
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		});
	}

	public override void _UnhandledInput(InputEvent input)
	{
		if (input is InputEventKey key &&
			key.Pressed && !key.Echo &&
			key.Keycode == Key.Escape)
		{
			bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;

			Input.MouseMode = captured
				? Input.MouseModeEnum.Visible
				: Input.MouseModeEnum.Captured;

			_jumpRequested = false;
			return;
		}

		if (input is InputEventMouseButton mouse && mouse.Pressed)
		{
			// Clicking the game recaptures the mouse.
			if (Input.MouseMode != Input.MouseModeEnum.Captured)
			{
				if (mouse.ButtonIndex == MouseButton.Left)
					Input.MouseMode = Input.MouseModeEnum.Captured;

				return;
			}

			if (mouse.ButtonIndex == MouseButton.Right)
				_jumpRequested = true;

			// Left mouse: reserved for interaction.
			// Middle mouse: reserved for attacking.
		}

		if (input is InputEventMouseMotion motion &&
			Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			float sensitivity = Mathf.DegToRad(MouseSensitivity);
			float horizontalSign = InvertX ? 1.0f : -1.0f;
			float verticalSign = InvertY ? 1.0f : -1.0f;

			RotateY(motion.Relative.X * sensitivity * horizontalSign);

			_pitch = Mathf.Clamp(
				_pitch + motion.Relative.Y * sensitivity * verticalSign,
				Mathf.DegToRad(-89),
				Mathf.DegToRad(89));

			_camera.Rotation = new Vector3(_pitch, 0, 0);
		}
	}

	// =========================================================
	// Arrow-key movement, right-mouse jump, gravity, and fall recovery.
	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		Vector2 movement = Vector2.Zero;

		if (Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			movement.X = (Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0)
					   - (Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0);

			movement.Y = (Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0)
					   - (Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0);
		}

		movement = movement.LimitLength();

		Vector3 direction = Transform.Basis
			* new Vector3(movement.X, 0, movement.Y);

		Vector3 velocity = Velocity;
		velocity.X = Mathf.MoveToward(
			velocity.X, direction.X * WalkSpeed, Acceleration * dt);
		velocity.Z = Mathf.MoveToward(
			velocity.Z, direction.Z * WalkSpeed, Acceleration * dt);

		if (IsOnFloor())
			velocity.Y = _jumpRequested ? JumpSpeed : 0;
		else
			velocity.Y -= Gravity * dt;

		_jumpRequested = false;
		Velocity = velocity;
		MoveAndSlide();

		if (GlobalPosition.Y < FallResetY)
		{
			GlobalPosition = RespawnPosition + Vector3.Up * 0.2f;
			Velocity = Vector3.Zero;
		}
	}
}
