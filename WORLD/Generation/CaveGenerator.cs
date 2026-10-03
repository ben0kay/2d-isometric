using Godot;

// Generates connected rounded passages and chambers.
// Negative distance = inside the cave; positive distance = outside.
public sealed class CaveGenerator
{
	private readonly Vector3[] _path;
	private readonly float[] _radii;
	private readonly Vector3[] _chambers;
	private readonly FastNoiseLite _noise;
	private readonly float _chamberRadius;
	private readonly float _roughness;

	// =========================================================
	// Create a descending test route with seeded width variation.
	public CaveGenerator(TerrainSettings settings, float entranceHeight)
	{
		float depth = Mathf.Clamp(settings.CaveDepth, 8, 20);
		float scale = depth / 18.0f;

		_path = new[]
		{
			new Vector3(0, entranceHeight + 1.0f, 4),
			new Vector3(0, -1.0f * scale, -3),
			new Vector3(4, -4.0f * scale, -10),
			new Vector3(-4, -7.0f * scale, -15),
			new Vector3(-11, -10.0f * scale, -7),
			new Vector3(-4, -13.0f * scale, 1),
			new Vector3(7, -16.0f * scale, 5),
			new Vector3(12, -depth, -5)
		};

		_chambers = new[]
		{
			_path[2],
			_path[4],
			_path[6],
			_path[7]
		};

		var random = new RandomNumberGenerator
		{
			Seed = unchecked((ulong)(uint)settings.Seed)
		};

		float radius = Mathf.Clamp(settings.TunnelRadius, 1.8f, 3.5f);
		_radii = new float[_path.Length - 1];

		for (int i = 0; i < _radii.Length; i++)
			_radii[i] = radius * random.RandfRange(0.95f, 1.08f);

		_chamberRadius = Mathf.Clamp(settings.ChamberRadius, 3, 5);
		_roughness = Mathf.Clamp(settings.CaveRoughness, 0, 0.4f);

		_noise = new FastNoiseLite
		{
			Seed = unchecked(settings.Seed + 7919),
			Frequency = Mathf.Max(0.001f, settings.CaveNoiseFrequency),
			NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth
		};
	}

	// =========================================================
	// Union the tunnel capsules and flattened chamber volumes.
	public float SampleDistance(Vector3 point)
	{
		float distance = float.MaxValue;

		for (int i = 0; i < _radii.Length; i++)
		{
			float tunnel = DistanceToSegment(point, _path[i], _path[i + 1])
				- _radii[i];

			distance = Mathf.Min(distance, tunnel);
		}

		foreach (Vector3 centre in _chambers)
		{
			Vector3 offset = point - centre;

			// Flatten chambers slightly to create broad rooms.
			offset.Y /= 0.7f;
			float chamber = (offset.Length() - _chamberRadius) * 0.7f;

			distance = Mathf.Min(distance, chamber);
		}

		// Noise changes the wall surface while keeping a broad central passage.
		if (Mathf.Abs(distance) < 1.0f && _roughness > 0)
		{
			distance += _noise.GetNoise3D(point.X, point.Y, point.Z)
				* _roughness;
		}

		return distance;
	}

	// =========================================================
	// Distance to the closest point along a tunnel centre line.
	private float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
	{
		Vector3 segment = end - start;
		float lengthSquared = segment.LengthSquared();

		if (lengthSquared < 0.000001f)
			return point.DistanceTo(start);

		float t = Mathf.Clamp(
			(point - start).Dot(segment) / lengthSquared, 0, 1);

		return point.DistanceTo(start + segment * t);
	}
}
