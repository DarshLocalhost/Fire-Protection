namespace SPA.SynthFire.Core.Geometry;

/// <summary>
/// A Revit-independent 3D point in model units (feet). Used by the placement
/// solvers so they can be unit-tested without any reference to the Revit API.
/// </summary>
public readonly record struct Point3D(double X, double Y, double Z)
{
    public double DistanceTo(Point3D other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        double dz = Z - other.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
