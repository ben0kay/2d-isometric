using Godot;
using System.Collections.Generic;

// Streams tall terrain chunks around the player and a secondary observer.
public partial class ChunkStreamer : Node
{
	#region Settings and Fields

	[Export] public int LoadRadius = 2;
	[Export] public int UnloadRadius = 3;

	public SandboxTerrain Terrain;
	public SandboxPlayer Player;
	public Node3D SecondaryObserver;

	private float _refreshTimer;

	#endregion

	#region Initialisation

	// =========================================================
	// Queue nearby terrain before allowing player movement.
	public override void _Ready()
	{
		Player.SetPhysicsProcess(false);
		RefreshChunks();
	}

	#endregion

	#region Streaming

	// =========================================================
	// Refresh the requested area periodically instead of every frame.
	public override void _Process(double delta)
	{
		_refreshTimer -= (float)delta;
		if (_refreshTimer > 0) return;

		_refreshTimer = 0.25f;
		RefreshChunks();
	}

	// =========================================================
	// Load nearby columns first and unload distant columns with hysteresis.
	private void RefreshChunks()
	{
		Vector2I playerColumn = Terrain.GetColumn(Player.GlobalPosition);
		bool hasSecondary = GodotObject.IsInstanceValid(SecondaryObserver);
		Vector2I secondaryColumn = hasSecondary
			? Terrain.GetColumn(SecondaryObserver.GlobalPosition)
			: playerColumn;

		int loadRadius = Mathf.Clamp(LoadRadius, 1, 4);
		int unloadRadius = Mathf.Max(loadRadius + 1, UnloadRadius);

		var wanted = new HashSet<Vector2I>();
		AddArea(wanted, playerColumn, loadRadius);

		if (hasSecondary)
			AddArea(wanted, secondaryColumn, 1);

		var ordered = new List<Vector2I>(wanted);
		ordered.Sort((a, b) =>
			DistanceSquared(a, playerColumn).CompareTo(
				DistanceSquared(b, playerColumn)));

		foreach (Vector2I key in ordered)
			Terrain.LoadColumn(key);

		var loaded = new List<Vector2I>(Terrain.LoadedColumns);
		foreach (Vector2I key in loaded)
		{
			bool nearPlayer = WithinRadius(key, playerColumn, unloadRadius);
			bool nearSecondary = hasSecondary
				&& WithinRadius(key, secondaryColumn, 2);

			if (!nearPlayer && !nearSecondary)
				Terrain.UnloadColumn(key);
		}
	}

	// =========================================================
	// Collect a square area clipped to the test world's boundaries.
	private void AddArea(HashSet<Vector2I> result, Vector2I centre, int radius)
	{
		for (int x = centre.X - radius; x <= centre.X + radius; x++)
		for (int z = centre.Y - radius; z <= centre.Y + radius; z++)
		{
			var key = new Vector2I(x, z);
			if (Terrain.IsValidColumn(key)) result.Add(key);
		}
	}

	// =========================================================
	// Compare column distance without calculating square roots.
	private int DistanceSquared(Vector2I a, Vector2I b)
	{
		Vector2I difference = a - b;
		return difference.X * difference.X + difference.Y * difference.Y;
	}

	// =========================================================
	// Use square loading regions with a larger unloading boundary.
	private bool WithinRadius(Vector2I a, Vector2I b, int radius)
	{
		return Mathf.Abs(a.X - b.X) <= radius
			&& Mathf.Abs(a.Y - b.Y) <= radius;
	}

	#endregion

	#region Movement Safety

	// =========================================================
	// Pause movement if the current or upcoming terrain is not ready.
	public override void _PhysicsProcess(double delta)
	{
		Vector3 position = Player.GlobalPosition;
		Vector3 next = position + Player.Velocity * 0.25f;

		bool ready = Terrain.HasTerrainNear(position)
			&& Terrain.HasTerrainNear(next);

		Player.SetPhysicsProcess(ready);
	}

	#endregion
}
