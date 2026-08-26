using System;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Configurable, non-engineering calculation controls (tolerances, grid resolution, caps).
    /// Spacing/coverage engineering values live in <see cref="IHazardPlacementRules"/>.
    /// </summary>
    public class BruteForceCalculationConfig
    {
        /// <summary>Geometric equality tolerance (feet). Never rely on exact floating point equality.</summary>
        public double ToleranceFt { get; set; }

        /// <summary>Base grid resolution for candidate generation (feet). Adapted downward if needed to stay under the candidate cap.</summary>
        public double GridResolutionFt { get; set; }

        /// <summary>Hard cap on generated candidates per room to prevent brute-force explosion.</summary>
        public int MaxCandidatePoints { get; set; }

        /// <summary>Hard cap on selection-search iterations per room.</summary>
        public int MaxSearchIterations { get; set; }

        public BruteForceCalculationConfig()
        {
            ToleranceFt = 1e-6;
            GridResolutionFt = 1.0;
            MaxCandidatePoints = 4000;
            MaxSearchIterations = 500000;
        }

        public static BruteForceCalculationConfig Default()
        {
            return new BruteForceCalculationConfig();
        }
    }
}
