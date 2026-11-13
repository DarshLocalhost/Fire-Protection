namespace SPA.SynthFire.Core.Standards;

/// <summary>Measurement system a rule set is expressed in.</summary>
public enum UnitSystem
{
    /// <summary>Feet / square feet (NFPA 13).</summary>
    Imperial,

    /// <summary>Metres / square metres (AS 2118, BS EN 12845).</summary>
    Metric
}

/// <summary>
/// Length/area conversions. Revit's internal unit is decimal feet, so the
/// placement layer converts rule-set values to feet at the Revit boundary.
/// </summary>
public static class Units
{
    public const double FeetPerMetre = 3.280839895013123;
    public const double SquareFeetPerSquareMetre = 10.763910416709722;

    public static double MetresToFeet(double metres) => metres * FeetPerMetre;
    public static double FeetToMetres(double feet) => feet / FeetPerMetre;

    public static double SquareMetresToSquareFeet(double m2) => m2 * SquareFeetPerSquareMetre;
    public static double SquareFeetToSquareMetres(double ft2) => ft2 / SquareFeetPerSquareMetre;

    /// <summary>Convert a length value in the given system to feet (Revit internal).</summary>
    public static double LengthToFeet(double value, UnitSystem system)
        => system == UnitSystem.Imperial ? value : MetresToFeet(value);

    /// <summary>Convert an area value in the given system to square feet (Revit internal).</summary>
    public static double AreaToSquareFeet(double value, UnitSystem system)
        => system == UnitSystem.Imperial ? value : SquareMetresToSquareFeet(value);
}
