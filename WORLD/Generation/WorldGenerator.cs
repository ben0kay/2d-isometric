using Godot;

// Evaluates the original world shape independently of rendering and mining.
public sealed class WorldGenerator
{
	private readonly TerrainSettings _settings;
	private readonly FastNoiseLite _surfaceNoise;
	private readonly CaveGenerator _caves;

	// =========================================================
	// Initialise reproducible surface and cave generation.
	public WorldGenerator(TerrainSettings settings)
	{
		_settings = settings;

		_surfaceNoise = new FastNoiseLite
		{
			Seed = settings.Seed,
			Frequency = Mathf.Max(0.001f, settings.SurfaceNoiseFrequency),
			NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth
		};

		if (settings.GenerateCaves)
			_caves = new CaveGenerator(settings, GetSurfaceHeight(0, 4));
	}

	// =========================================================
	// Surface height with the original test hills.
	public float GetSurfaceHeight(float x, float z)
	{
		float hillA = Mathf.Max(0, _settings.HillHeight) * Mathf.Exp(
			-((x - 3) * (x - 3) + (z + 8) * (z + 8)) / 30.0f);

		float hillB = 2.5f * Mathf.Exp(
			-((x + 8) * (x + 8) + (z + 2) * (z + 2)) / 22.0f);

		return 0.4f
			+ _surfaceNoise.GetNoise2D(x, z) * _settings.SurfaceNoiseStrength
			+ hillA + hillB;
	}

	// =========================================================
	// Subtract caves from terrain. Negative = rock, positive = air.
	public float SampleDensity(Vector3 point, float surfaceHeight)
	{
		float terrain = point.Y - surfaceHeight;

		if (_caves != null)
			terrain = Mathf.Max(terrain, -_caves.SampleDistance(point));

		return terrain;
	}
}
