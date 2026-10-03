using Godot;

// Shared generation settings. Assign this resource through the level Inspector.
[GlobalClass]
public partial class TerrainSettings : Resource
{
	[ExportGroup("World")]
	[Export] public int Seed = 12345;
	[Export] public float SurfaceNoiseFrequency = 0.09f;
	[Export] public float SurfaceNoiseStrength = 0.6f;
	[Export] public float HillHeight = 5.5f;

	[ExportGroup("Caves")]
	[Export] public bool GenerateCaves = true;
	[Export(PropertyHint.Range, "1.8,3.5,0.1")]
	public float TunnelRadius = 2.2f;

	[Export(PropertyHint.Range, "3,5,0.1")]
	public float ChamberRadius = 4.0f;

	[Export(PropertyHint.Range, "8,20,0.5")]
	public float CaveDepth = 18.0f;

	[Export(PropertyHint.Range, "0,0.4,0.05")]
	public float CaveRoughness = 0.2f;

	[Export] public float CaveNoiseFrequency = 0.35f;
}
