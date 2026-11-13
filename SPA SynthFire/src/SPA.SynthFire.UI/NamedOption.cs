namespace SPA.SynthFire.UI;

/// <summary>A generic id + display label for list/combo binding (levels, etc.).</summary>
public sealed record NamedOption(string Id, string Label)
{
    public override string ToString() => Label;
}
