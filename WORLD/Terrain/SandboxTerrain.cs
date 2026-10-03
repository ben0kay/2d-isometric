using Godot;
using System.Collections.Generic;

// Small editable terrain volume. Shared density samples prevent chunk cracks.
// Negative density = solid; positive density = air.
public partial class SandboxTerrain : Node3D
{
	[Export] public TerrainSettings Settings = new();
	[Export] public float MiningRadius = 1.35f;
	[Export] public float MiningInterval = 0.25f;
	[Export] public float MiningReach = 5.0f;

	private const int ChunkSize = 16;
	private const int SizeX = 128;
	private const int SizeY = 64;
	private const int SizeZ = 128;

	private readonly Vector3 _origin = new(-64, -48, -64);
	private readonly float[,,] _density =
		new float[SizeX + 1, SizeY + 1, SizeZ + 1];

	private readonly Dictionary<Vector3I, TerrainChunk> _chunks = new();
	private readonly Queue<Vector3I> _dirty = new();
	private readonly HashSet<Vector3I> _queued = new();

	private WorldGenerator _generator;
	private StandardMaterial3D _material;
	private float _miningTimer;

		private sealed class TerrainChunk
	{
		public MeshInstance3D Visual;
		public CollisionShape3D Collision;
		public bool Built;
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
	
		#region Chunk Streaming

	private readonly Dictionary<Vector2I, Node3D> _columns = new();
	private Node3D _chunkRoot;

	public int Revision { get; private set; }
	public int LoadedColumnCount => _columns.Count;
	public int PendingSections => _queued.Count;
	public IEnumerable<Vector2I> LoadedColumns => _columns.Keys;

	// =========================================================
	// Convert a global position into a horizontal chunk coordinate.
	public Vector2I GetColumn(Vector3 globalPosition)
	{
		Vector3 local = ToLocal(globalPosition) - _origin;
		return new Vector2I(
			Mathf.FloorToInt(local.X / ChunkSize),
			Mathf.FloorToInt(local.Z / ChunkSize));
	}

	// =========================================================
	// Check whether a column lies inside the finite test world.
	public bool IsValidColumn(Vector2I key)
	{
		return key.X >= 0 && key.X < SizeX / ChunkSize
			&& key.Y >= 0 && key.Y < SizeZ / ChunkSize;
	}

	// =========================================================
	// Create a tall chunk containing four independently rebuilt sections.
	public void LoadColumn(Vector2I key)
	{
		if (!IsValidColumn(key) || _columns.ContainsKey(key)) return;

		var column = new Node3D
		{
			Name = $"Chunk_{key.X}_{key.Y}"
		};
		_chunkRoot.AddChild(column);
		_columns.Add(key, column);

		// Surface sections first, then deeper sections.
		for (int y = SizeY / ChunkSize - 1; y >= 0; y--)
		{
			var sectionKey = new Vector3I(key.X, y, key.Y);
			var section = new Node3D { Name = $"Section_{y}" };
			column.AddChild(section);

			var visual = new MeshInstance3D
			{
				Name = "Visual",
				MaterialOverride = _material
			};
			section.AddChild(visual);

			var body = new StaticBody3D
			{
				Name = "Body",
				CollisionLayer = 1,
				CollisionMask = 0
			};
			section.AddChild(body);
			body.AddToGroup("mineable_terrain");

			var collision = new CollisionShape3D { Name = "Collision" };
			body.AddChild(collision);

			_chunks.Add(sectionKey, new TerrainChunk
			{
				Visual = visual,
				Collision = collision
			});

			QueueSection(sectionKey);
		}
	}

	// =========================================================
	// Remove visual and collision nodes while retaining terrain edits.
	public void UnloadColumn(Vector2I key)
	{
		if (!_columns.Remove(key, out Node3D column)) return;

		for (int y = 0; y < SizeY / ChunkSize; y++)
		{
			var sectionKey = new Vector3I(key.X, y, key.Y);
			_chunks.Remove(sectionKey);
			_queued.Remove(sectionKey);
		}

		_chunkRoot.RemoveChild(column);
		column.QueueFree();
		Revision++;
	}

	// =========================================================
	// Queue a section once, whether loading it or rebuilding after mining.
	private void QueueSection(Vector3I key)
	{
		if (_queued.Add(key)) _dirty.Enqueue(key);
	}

	// =========================================================
	// Check sections around a body's feet and head, including chunk seams.
	public bool HasTerrainNear(Vector3 globalPosition)
	{
		Vector3 local = ToLocal(globalPosition) - _origin;

		int minX = Mathf.FloorToInt((local.X - 0.5f) / ChunkSize);
		int maxX = Mathf.FloorToInt((local.X + 0.5f) / ChunkSize);
		int minZ = Mathf.FloorToInt((local.Z - 0.5f) / ChunkSize);
		int maxZ = Mathf.FloorToInt((local.Z + 0.5f) / ChunkSize);
		int minY = Mathf.FloorToInt((local.Y - 1.0f) / ChunkSize);
		int maxY = Mathf.FloorToInt((local.Y + 2.0f) / ChunkSize);

		if (minX < 0 || maxX >= SizeX / ChunkSize ||
			minZ < 0 || maxZ >= SizeZ / ChunkSize ||
			local.Y < 0 || local.Y >= SizeY)
			return false;

		minY = Mathf.Clamp(minY, 0, SizeY / ChunkSize - 1);
		maxY = Mathf.Clamp(maxY, 0, SizeY / ChunkSize - 1);

		for (int x = minX; x <= maxX; x++)
		for (int y = minY; y <= maxY; y++)
		for (int z = minZ; z <= maxZ; z++)
		{
			if (!_chunks.TryGetValue(new Vector3I(x, y, z), out var chunk)
				|| !chunk.Built)
				return false;
		}

		return true;
	}

	// =========================================================
	// Supply loaded terrain geometry to the navigation baker.
	public IEnumerable<MeshInstance3D> GetLoadedVisuals()
	{
		foreach (TerrainChunk chunk in _chunks.Values)
			if (chunk.Built && chunk.Visual.Mesh != null)
				yield return chunk.Visual;
	}

	#endregion

	#region Generation

	// =========================================================
	// Initialise the generator, shared density data, and chunk meshes.
	public override void _Ready()
	{
		Settings ??= new TerrainSettings();
		_generator = new WorldGenerator(Settings);

		_material = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			Roughness = 1.0f
		};

		GenerateDensity();
		CreateChunks();
	}

	// =========================================================
	// Expose original surface height for spawning and terrain colouring.
	public float GetSurfaceHeight(float x, float z)
	{
		return _generator.GetSurfaceHeight(x, z);
	}

	// =========================================================
	// Sample the generated world and close the volume at its boundaries.
	private void GenerateDensity()
	{
		for (int x = 0; x <= SizeX; x++)
		for (int z = 0; z <= SizeZ; z++)
		{
			float height = GetSurfaceHeight(_origin.X + x, _origin.Z + z);

			for (int y = 0; y <= SizeY; y++)
			{
				Vector3 point = _origin + new Vector3(x, y, z);
				float terrain = _generator.SampleDensity(point, height);

				float boundary = Mathf.Max(
					Mathf.Max(0.25f - x, x - (SizeX - 0.25f)),
					Mathf.Max(
						Mathf.Max(0.25f - z, z - (SizeZ - 0.25f)),
						0.25f - y));

				_density[x, y, z] = Mathf.Max(terrain, boundary);
			}
		}
	}

		// =========================================================
	// Group streamed terrain chunks beneath a single helper node.
	private void CreateChunks()
	{
		_chunkRoot = new Node3D { Name = "Chunks" };
		AddChild(_chunkRoot);
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
	// Build one section per frame and ignore obsolete unload requests.
	public override void _Process(double delta)
	{
		while (_dirty.Count > 0)
		{
			Vector3I key = _dirty.Dequeue();

			if (!_queued.Contains(key) ||
				!_chunks.TryGetValue(key, out TerrainChunk chunk))
				continue;

			RebuildChunk(key);
			chunk.Built = true;
			_queued.Remove(key);
			Revision++;
			break;
		}
	}

	#endregion
}
