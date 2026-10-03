using Godot;
using System.Collections.Generic;

// Small editable terrain volume. Shared density samples prevent chunk cracks.
// Negative density = solid; positive density = air.
public partial class SandboxTerrain : Node3D
{
	[Export] public int Seed = 12345;
	[Export] public float MiningRadius = 1.35f;
	[Export] public float MiningInterval = 0.25f;
	[Export] public float MiningReach = 5.0f;

	private const int ChunkSize = 8;
	private const int SizeX = 32;
	private const int SizeY = 24;
	private const int SizeZ = 32;

	private readonly Vector3 _origin = new(-16, -12, -16);
	private readonly float[,,] _density =
		new float[SizeX + 1, SizeY + 1, SizeZ + 1];

	private readonly Dictionary<Vector3I, TerrainChunk> _chunks = new();
	private readonly Queue<Vector3I> _dirty = new();
	private readonly HashSet<Vector3I> _queued = new();

	private FastNoiseLite _noise;
	private StandardMaterial3D _material;
	private float _miningTimer;

	private sealed class TerrainChunk
	{
		public MeshInstance3D Visual;
		public CollisionShape3D Collision;
	}

	private static readonly Vector3I[] Corners =
	{
		new(0, 0, 0), new(1, 0, 0),
		new(1, 1, 0), new(0, 1, 0),
		new(0, 0, 1), new(1, 0, 1),
		new(1, 1, 1), new(0, 1, 1)
	};

	// All cubes use the same subdivision, including at chunk boundaries.
	private static readonly int[,] Tetrahedra =
	{
		{ 0, 5, 1, 6 },
		{ 0, 1, 2, 6 },
		{ 0, 2, 3, 6 },
		{ 0, 3, 7, 6 },
		{ 0, 7, 4, 6 },
		{ 0, 4, 5, 6 }
	};

	#region Generation

	// =========================================================
	// Create the shared terrain data and initial chunk meshes.
	public override void _Ready()
	{
		_noise = new FastNoiseLite
		{
			Seed = Seed,
			Frequency = 0.09f,
			NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth
		};

		_material = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			Roughness = 1.0f
		};

		GenerateDensity();
		CreateChunks();
	}

	// =========================================================
	// Surface height: small noise variations plus two test hills.
	public float GetSurfaceHeight(float x, float z)
	{
		float hillA = 5.5f * Mathf.Exp(
			-((x - 3) * (x - 3) + (z + 8) * (z + 8)) / 30.0f);

		float hillB = 2.5f * Mathf.Exp(
			-((x + 8) * (x + 8) + (z + 2) * (z + 2)) / 22.0f);

		return 0.4f + _noise.GetNoise2D(x, z) * 0.6f + hillA + hillB;
	}

	// =========================================================
	// Fill the volume. Boundary planes close its sides and bottom.
	private void GenerateDensity()
	{
		for (int x = 0; x <= SizeX; x++)
		for (int z = 0; z <= SizeZ; z++)
		{
			float height = GetSurfaceHeight(_origin.X + x, _origin.Z + z);

			for (int y = 0; y <= SizeY; y++)
			{
				float surface = _origin.Y + y - height;

				float boundary = Mathf.Max(
					Mathf.Max(0.25f - x, x - (SizeX - 0.25f)),
					Mathf.Max(
						Mathf.Max(0.25f - z, z - (SizeZ - 0.25f)),
						0.25f - y));

				_density[x, y, z] = Mathf.Max(surface, boundary);
			}
		}
	}

	// =========================================================
	// Each chunk has one visual mesh and one static collision body.
	private void CreateChunks()
	{
		for (int x = 0; x < SizeX / ChunkSize; x++)
		for (int y = 0; y < SizeY / ChunkSize; y++)
		for (int z = 0; z < SizeZ / ChunkSize; z++)
		{
			var key = new Vector3I(x, y, z);
			var root = new Node3D { Name = $"Chunk_{x}_{y}_{z}" };
			AddChild(root);

			var visual = new MeshInstance3D
			{
				Name = "Visual",
				MaterialOverride = _material
			};
			root.AddChild(visual);

			var body = new StaticBody3D
			{
				Name = "Body",
				CollisionLayer = 1,
				CollisionMask = 0
			};
			root.AddChild(body);
			body.AddToGroup("mineable_terrain");

			var collision = new CollisionShape3D { Name = "Collision" };
			body.AddChild(collision);

			_chunks.Add(key, new TerrainChunk
			{
				Visual = visual,
				Collision = collision
			});

			RebuildChunk(key);
		}
	}

	#endregion

	#region Meshing

	// =========================================================
	// Generate only the solid/air boundary, rather than individual cubes.
	private void RebuildChunk(Vector3I key)
	{
		using var surface = new SurfaceTool();
		surface.Begin(Mesh.PrimitiveType.Triangles);

		var positions = new Vector3[8];
		var values = new float[8];
		var inside = new int[4];
		var outside = new int[4];
		int triangleCount = 0;

		Vector3I start = key * ChunkSize;

		for (int x = start.X; x < start.X + ChunkSize; x++)
		for (int y = start.Y; y < start.Y + ChunkSize; y++)
		for (int z = start.Z; z < start.Z + ChunkSize; z++)
		{
			bool hasSolid = false;
			bool hasAir = false;

			for (int i = 0; i < 8; i++)
			{
				Vector3I sample = new Vector3I(x, y, z) + Corners[i];
				positions[i] = _origin
					+ new Vector3(sample.X, sample.Y, sample.Z);
				values[i] = _density[sample.X, sample.Y, sample.Z];

				if (values[i] < 0) hasSolid = true;
				else hasAir = true;
			}

			if (!hasSolid || !hasAir)
				continue;

			for (int tetra = 0; tetra < 6; tetra++)
				PolygoniseTetrahedron(
					surface, positions, values, tetra,
					inside, outside, ref triangleCount);
		}

		TerrainChunk chunk = _chunks[key];

		if (triangleCount == 0)
		{
			chunk.Visual.Mesh = null;
			chunk.Collision.SetDeferred("shape", default(Variant));
			return;
		}

		ArrayMesh mesh = surface.Commit();
		chunk.Visual.Mesh = mesh;
		chunk.Collision.SetDeferred("shape", mesh.CreateTrimeshShape());
	}

	// =========================================================
	// A tetrahedron intersects the surface as a triangle or a quad.
	private void PolygoniseTetrahedron(
		SurfaceTool surface, Vector3[] positions, float[] values,
		int tetra, int[] inside, int[] outside, ref int triangleCount)
	{
		int insideCount = 0;
		int outsideCount = 0;
		Vector3 solidCentre = Vector3.Zero;
		Vector3 airCentre = Vector3.Zero;

		for (int i = 0; i < 4; i++)
		{
			int corner = Tetrahedra[tetra, i];

			if (values[corner] < 0)
			{
				inside[insideCount++] = corner;
				solidCentre += positions[corner];
			}
			else
			{
				outside[outsideCount++] = corner;
				airCentre += positions[corner];
			}
		}

		if (insideCount == 0 || outsideCount == 0)
			return;

		Vector3 outward = airCentre / outsideCount
			- solidCentre / insideCount;

		if (insideCount == 1)
		{
			Vector3 a = EdgePoint(inside[0], outside[0], positions, values);
			Vector3 b = EdgePoint(inside[0], outside[1], positions, values);
			Vector3 c = EdgePoint(inside[0], outside[2], positions, values);
			AddTriangle(surface, a, b, c, outward, ref triangleCount);
		}
		else if (insideCount == 3)
		{
			Vector3 a = EdgePoint(outside[0], inside[0], positions, values);
			Vector3 b = EdgePoint(outside[0], inside[1], positions, values);
			Vector3 c = EdgePoint(outside[0], inside[2], positions, values);
			AddTriangle(surface, a, b, c, outward, ref triangleCount);
		}
		else
		{
			Vector3 a = EdgePoint(inside[0], outside[0], positions, values);
			Vector3 b = EdgePoint(inside[0], outside[1], positions, values);
			Vector3 c = EdgePoint(inside[1], outside[0], positions, values);
			Vector3 d = EdgePoint(inside[1], outside[1], positions, values);

			AddTriangle(surface, a, b, c, outward, ref triangleCount);
			AddTriangle(surface, b, d, c, outward, ref triangleCount);
		}
	}

	// =========================================================
	// Interpolate the zero-density crossing along an edge.
	private Vector3 EdgePoint(
		int a, int b, Vector3[] positions, float[] values)
	{
		float t = values[a] / (values[a] - values[b]);
		return positions[a].Lerp(positions[b], t);
	}

	// =========================================================
	// Orient outward normals and emit clockwise front faces.
	private void AddTriangle(
		SurfaceTool surface, Vector3 a, Vector3 b, Vector3 c,
		Vector3 outward, ref int triangleCount)
	{
		Vector3 normal = (b - a).Cross(c - a);
		if (normal.LengthSquared() < 0.000001f)
			return;

		if (normal.Dot(outward) < 0)
		{
			(b, c) = (c, b);
			normal = -normal;
		}

		normal = normal.Normalized();

		AddVertex(surface, a, normal);
		AddVertex(surface, c, normal);
		AddVertex(surface, b, normal);
		triangleCount++;
	}

	// =========================================================
	// Green upward-facing surface; grey exposed rock.
	private void AddVertex(SurfaceTool surface, Vector3 point, Vector3 normal)
	{
		float height = GetSurfaceHeight(point.X, point.Z);
		bool grass = normal.Y > 0.45f && point.Y > height - 0.65f;

		surface.SetColor(grass
			? new Color("#214d30")
			: new Color("#747982"));

		surface.SetNormal(normal);
		surface.AddVertex(point);
	}

	#endregion

	#region Mining

	// =========================================================
	// Query the world during physics updates, using the active camera.
	public override void _PhysicsProcess(double delta)
	{
		_miningTimer = Mathf.Max(0, _miningTimer - (float)delta);

		if (Input.MouseMode != Input.MouseModeEnum.Captured ||
			!Input.IsMouseButtonPressed(MouseButton.Left) ||
			_miningTimer > 0 || _dirty.Count > 0)
			return;

		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
			return;

		Vector3 from = camera.GlobalPosition;
		Vector3 to = from - camera.GlobalTransform.Basis.Z * MiningReach;

		using var query = PhysicsRayQueryParameters3D.Create(from, to, 1);
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

		if (hit.Count == 0)
			return;

		var collider = hit["collider"].AsGodotObject() as Node;
		if (collider == null || !collider.IsInGroup("mineable_terrain"))
			return;

		Vector3 point = hit["position"].AsVector3();
		Vector3 normal = hit["normal"].AsVector3();

		// Place the excavation slightly inside the rock.
		Excavate(point - normal * MiningRadius * 0.3f, MiningRadius);
		_miningTimer = Mathf.Max(0.05f, MiningInterval);
	}

	// =========================================================
	// Subtract a sphere from the shared terrain density.
	private void Excavate(Vector3 centre, float radius)
	{
		Vector3 local = centre - _origin;
		Vector3 extent = Vector3.One * radius;

		Vector3 low = local - extent;
		Vector3 high = local + extent;

		int minX = Mathf.Clamp(Mathf.FloorToInt(low.X), 0, SizeX);
		int minY = Mathf.Clamp(Mathf.FloorToInt(low.Y), 0, SizeY);
		int minZ = Mathf.Clamp(Mathf.FloorToInt(low.Z), 0, SizeZ);
		int maxX = Mathf.Clamp(Mathf.CeilToInt(high.X), 0, SizeX);
		int maxY = Mathf.Clamp(Mathf.CeilToInt(high.Y), 0, SizeY);
		int maxZ = Mathf.Clamp(Mathf.CeilToInt(high.Z), 0, SizeZ);

		bool changed = false;

		for (int x = minX; x <= maxX; x++)
		for (int y = minY; y <= maxY; y++)
		for (int z = minZ; z <= maxZ; z++)
		{
			Vector3 point = _origin + new Vector3(x, y, z);
			float distance = point.DistanceTo(centre);

			if (distance >= radius)
				continue;

			float previous = _density[x, y, z];
			float next = Mathf.Max(previous, radius - distance);

			if (next <= previous)
				continue;

			_density[x, y, z] = next;
			changed = true;
		}

		if (!changed)
			return;

		// Include both chunks whenever an edited sample lies on a seam.
		foreach (Vector3I key in _chunks.Keys)
		{
			Vector3I start = key * ChunkSize;

			bool overlaps =
				start.X <= maxX && start.X + ChunkSize >= minX &&
				start.Y <= maxY && start.Y + ChunkSize >= minY &&
				start.Z <= maxZ && start.Z + ChunkSize >= minZ;

			if (overlaps && _queued.Add(key))
				_dirty.Enqueue(key);
		}
	}

	// =========================================================
	// Spread mesh rebuilding across frames.
	public override void _Process(double delta)
	{
		const int rebuildsPerFrame = 2;

		for (int i = 0; i < rebuildsPerFrame && _dirty.Count > 0; i++)
		{
			Vector3I key = _dirty.Dequeue();
			_queued.Remove(key);
			RebuildChunk(key);
		}
	}

	#endregion
}
