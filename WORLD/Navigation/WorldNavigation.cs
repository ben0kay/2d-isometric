using Godot;

// Builds navigation over loaded terrain, including cave floors.
public partial class WorldNavigation : Node3D
{
	#region Settings and Fields

	[Export] public float RebuildDelay = 0.75f;

	public SandboxTerrain Terrain;
	public bool IsBaking { get; private set; }

	private NavigationRegion3D _region;
	private NavigationMesh _bakingMesh;
	private NavigationMeshSourceGeometryData3D _source;
	private int _observedRevision = -1;
	private int _appliedRevision = -1;
	private int _bakeRevision;
	private int _syncFrames;
	private float _timer;

	public bool Ready => !IsBaking && _syncFrames == 0
		&& _appliedRevision == Terrain.Revision
		&& Terrain.PendingSections == 0
		&& _region.NavigationMesh != null
		&& _region.NavigationMesh.GetPolygonCount() > 0;

	#endregion

	#region Initialisation

	// =========================================================
	// Create one navigation region for this initial streaming test.
	public override void _Ready()
	{
		_region = new NavigationRegion3D { Name = "Region" };
		AddChild(_region);

		NavigationServer3D.MapSetCellSize(
			GetWorld3D().NavigationMap, 0.25f);
	}

	#endregion

	#region Baking

	// =========================================================
	// Debounce terrain changes and wait for pending terrain meshes.
	public override void _Process(double delta)
	{
		if (_observedRevision != Terrain.Revision)
		{
			_observedRevision = Terrain.Revision;
			_timer = RebuildDelay;
		}

		if (Terrain.PendingSections > 0)
		{
			_timer = RebuildDelay;
			return;
		}

		if (IsBaking || _appliedRevision == Terrain.Revision) return;

		_timer -= (float)delta;
		if (_timer <= 0) BeginBake();
	}

	// =========================================================
	// Gather terrain geometry and start an asynchronous navigation bake.
	private void BeginBake()
	{
		_source = new NavigationMeshSourceGeometryData3D();

		foreach (MeshInstance3D visual in Terrain.GetLoadedVisuals())
			_source.AddMesh(visual.Mesh, visual.GlobalTransform);

		_bakingMesh = new NavigationMesh
		{
			CellSize = 0.25f,
			CellHeight = 0.2f,
			AgentRadius = 0.4f,
			AgentHeight = 1.5f,
			AgentMaxClimb = 0.4f,
			AgentMaxSlope = 45.0f
		};

		_bakeRevision = Terrain.Revision;
		IsBaking = true;

		NavigationServer3D.BakeFromSourceGeometryDataAsync(
			_bakingMesh, _source,
			Callable.From(() => Callable.From(FinishBake).CallDeferred()));
	}

	// =========================================================
	// Apply a finished bake only if its terrain snapshot is still current.
	private void FinishBake()
	{
		if (!IsInsideTree()) return;

		if (_bakeRevision == Terrain.Revision &&
			Terrain.PendingSections == 0)
		{
			_region.NavigationMesh = _bakingMesh;
			_appliedRevision = _bakeRevision;
			_syncFrames = 2;
		}

		_source.Dispose();
		_source = null;
		_bakingMesh = null;
		IsBaking = false;
		_timer = RebuildDelay;
	}

	// =========================================================
	// Allow navigation server updates to synchronise before pursuit.
	public override void _PhysicsProcess(double delta)
	{
		if (_syncFrames > 0) _syncFrames--;
	}

	#endregion
}
