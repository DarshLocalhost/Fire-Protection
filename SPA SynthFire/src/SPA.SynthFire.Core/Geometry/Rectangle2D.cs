namespace SPA.SynthFire.Core.Geometry;

/// <summary>
/// An axis-aligned rectangle in model units (feet), defined by its min/max
/// corners. The Phase 2 solver works on rectangular room footprints; irregular
/// polygons are handled in a later phase by decomposing into rectangles.
/// </summary>
public readonly record struct Rectangle2D(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double Area => Width * Height;

    public double CenterX => (MinX + MaxX) / 2.0;
    public double CenterY => (MinY + MaxY) / 2.0;

    public bool Contains(double x, double y) =>
        x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    public static Rectangle2D FromSize(double originX, double originY, double width, double height) =>
        new(originX, originY, originX + width, originY + height);
}
