using System;

namespace FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce
{
    /// <summary>
    /// Axis-aligned obstacle bounds (feet) used by the smoke/notification calc engine: beams, ducts,
    /// soffits and other ceiling projections that a device must clear or that attenuate audible coverage.
    /// <para>
    /// Shared (public, top-level) rather than private-nested so both <see cref="SmokeDetectorCalculationService"/>
    /// (which builds the boxes) and <see cref="AudibleCoverageEngine"/> / the test harness (which consume them)
    /// reference one type. The sprinkler engine keeps its own private equivalent — the two calc paths are
    /// independent and intentionally not coupled through this DTO.
    /// </para>
    /// </summary>
    public sealed class ObstacleBox
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MinZ { get; set; }
        public double MaxZ { get; set; }
        public string Category { get; set; }
        public double ClearanceFt { get; set; }

        public bool SpansZ(double placementZ, double toleranceFt)
        {
            if (double.IsNaN(MinZ) || double.IsNaN(MaxZ)) return true;
            if (Math.Abs(MaxZ - MinZ) < 1e-6) return true;
            return placementZ >= MinZ - toleranceFt && placementZ <= MaxZ + toleranceFt;
        }
    }
}
