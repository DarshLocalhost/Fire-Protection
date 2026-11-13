namespace SPA.SynthFire.Core.Standards;

/// <summary>
/// A named occupancy/hazard classification within a standard (e.g. NFPA "Light
/// Hazard", AS 2118 "Ordinary Hazard OH1"). Each standard defines its own set,
/// which is why classification is a value rather than a shared enum.
/// </summary>
public sealed record HazardClassification(string Id, string DisplayName, string Description = "")
{
    public override string ToString() => DisplayName;
}
