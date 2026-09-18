using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.Placement.SmokeDetectors.Final;
using FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;

namespace FireProtection.Backend.Services.Placement.NotificationAppliances.Final.BruteForce
{
    /// <summary>
    /// Simplified audible coverage engine for notification appliances (NFPA 72 Ch. 18).
    /// Performs a post-placement sound-level audit: samples the room on a coarse grid,
    /// applies inverse-distance drop + surface absorption + door/wall reduction + assumed
    /// ambient by occupancy, and flags any sample point that falls short of the required
    /// dBA target.
    /// <para>Internal: <see cref="Evaluate"/> exposes the internal <c>RoomGeometry</c>. The test
    /// assembly reaches it via <c>InternalsVisibleTo</c>.</para>
    /// </summary>
    internal static class AudibleCoverageEngine
    {
        public const double ReferenceDistanceFt = 3.0;
        public const double MinSampleDistanceFt = 0.5;
        public const double DefaultSurfaceAbsorptionDb = 3.0;
        public const double DefaultDoorAttenuationDb = 6.0;
        public const double DefaultWallAttenuationDb = 2.0;

        public static AudibleCoverageResult Evaluate(
            IReadOnlyList<CalculatedSmokeDetectorPoint> appliances,
            RoomGeometry geometry,
            List<ObstacleBox> obstacles,
            double ambientDb,
            double maxSustainedDb,
            bool isSleepingArea,
            BruteForceCalculationConfig config)
        {
            var result = new AudibleCoverageResult
            {
                AmbientDb = ambientDb,
                MaxSustainedDb = maxSustainedDb,
                IsSleepingArea = isSleepingArea
            };

            if (appliances == null || appliances.Count == 0 || geometry == null)
            {
                result.Status = "NoAppliances";
                result.Message = "No notification appliances were placed; audible coverage cannot be evaluated.";
                return result;
            }

            double requiredDb = CalculateRequiredDb(ambientDb, maxSustainedDb, isSleepingArea);
            result.RequiredDb = requiredDb;

            double sampleStep = Math.Max(MinSampleDistanceFt, Math.Min(3.0, (geometry.MaxX - geometry.MinX) / 10.0));
            if (sampleStep > (geometry.MaxY - geometry.MinY) / 10.0)
                sampleStep = Math.Max(MinSampleDistanceFt, (geometry.MaxY - geometry.MinY) / 10.0);

            int samples = 0;
            int uncovered = 0;
            double worstDeficit = 0.0;
            double worstX = 0.0;
            double worstY = 0.0;
            double worstZ = 0.0;

            for (double sy = geometry.MinY; sy <= geometry.MaxY + config.ToleranceFt; sy += sampleStep)
            {
                for (double sx = geometry.MinX; sx <= geometry.MaxX + config.ToleranceFt; sx += sampleStep)
                {
                    if (!geometry.IsPointInsideRoom(sx, sy, config.ToleranceFt)) continue;

                    bool insideObstacle = false;
                    foreach (ObstacleBox box in obstacles)
                    {
                        if (GeometryMath.InsideExpandedBox(sx, sy, box.MinX, box.MinY, box.MaxX, box.MaxY, 0.0))
                        {
                            insideObstacle = true;
                            break;
                        }
                    }
                    if (insideObstacle) continue;

                    samples++;

                    double sampleDb = CalculateSampleDb(sx, sy, appliances, geometry, obstacles);
                    if (sampleDb < requiredDb - config.ToleranceFt)
                    {
                        uncovered++;
                        double deficit = requiredDb - sampleDb;
                        if (deficit > worstDeficit)
                        {
                            worstDeficit = deficit;
                            worstX = sx;
                            worstY = sy;
                            worstZ = appliances[0].Z;
                        }
                    }
                }
            }

            result.SamplesChecked = samples;
            result.UncoveredSamples = uncovered;
            result.WorstDeficitDb = worstDeficit;
            result.WorstPoint = worstDeficit > 0 ? new CalculatedSmokeDetectorPoint { X = worstX, Y = worstY, Z = worstZ } : null;

            if (samples > 0 && uncovered > 0)
            {
                double pct = (double)uncovered * 100.0 / samples;
                result.Status = "CoverageGap";
                result.Message = string.Format(
                    "Audible coverage gap: {0} of {1} sample points ({2:F1}%) are below {3:F1} dBA. " +
                    "Worst deficit: {4:F1} dBA at ({5:F2}, {6:F2}).",
                    uncovered, samples, pct, requiredDb, worstDeficit, worstX, worstY);
                result.CoveragePercentage = 100.0 - pct;
            }
            else
            {
                result.Status = "Pass";
                result.Message = string.Format(
                    "Audible coverage PASS: all {0} sample points meet the {1:F1} dBA requirement.",
                    samples, requiredDb);
                result.CoveragePercentage = 100.0;
            }

            return result;
        }

        private static double CalculateRequiredDb(double ambientDb, double maxSustainedDb, bool isSleepingArea)
        {
            if (isSleepingArea)
            {
                return Math.Max(75.0, Math.Max(ambientDb + 15.0, maxSustainedDb + 5.0));
            }
            return Math.Max(ambientDb + 15.0, maxSustainedDb + 5.0);
        }

        private static double CalculateSampleDb(
            double sx, double sy,
            IReadOnlyList<CalculatedSmokeDetectorPoint> appliances,
            RoomGeometry geometry,
            List<ObstacleBox> obstacles)
        {
            double totalDb = double.NegativeInfinity;

            foreach (CalculatedSmokeDetectorPoint appliance in appliances)
            {
                double distance = GeometryMath.Distance(sx, sy, appliance.X, appliance.Y);
                if (distance < MinSampleDistanceFt) distance = MinSampleDistanceFt;

                double sourceDb = 85.0;
                double attenuatedDb = sourceDb - 20.0 * Math.Log10(distance / ReferenceDistanceFt);

                attenuatedDb -= DefaultSurfaceAbsorptionDb;

                int wallCrossed = CountWallCrossings(sx, sy, appliance.X, appliance.Y, geometry);
                if (wallCrossed > 0)
                {
                    attenuatedDb -= wallCrossed * DefaultWallAttenuationDb;
                }

                foreach (ObstacleBox box in obstacles)
                {
                    if (GeometryMath.InsideExpandedBox(sx, sy, box.MinX, box.MinY, box.MaxX, box.MaxY, 0.0))
                    {
                        attenuatedDb -= 3.0;
                        break;
                    }
                }

                if (attenuatedDb > totalDb) totalDb = attenuatedDb;
            }

            return totalDb;
        }

        private static int CountWallCrossings(double x1, double y1, double x2, double y2, RoomGeometry geometry)
        {
            if (geometry == null || geometry.OuterPolygon == null || geometry.OuterPolygon.Count < 3) return 0;

            int crossings = 0;
            int n = geometry.OuterPolygon.Count;
            for (int i = 0; i < n; i++)
            {
                double[] a = geometry.OuterPolygon[i];
                double[] b = geometry.OuterPolygon[(i + 1) % n];
                if (a == null || b == null || a.Length < 2 || b.Length < 2) continue;

                if (LineSegmentsIntersect(x1, y1, x2, y2, a[0], a[1], b[0], b[1]))
                {
                    crossings++;
                }
            }

            return crossings;
        }

        private static bool LineSegmentsIntersect(double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4)
        {
            double d1 = Direction(x3, y3, x4, y4, x1, y1);
            double d2 = Direction(x3, y3, x4, y4, x2, y2);
            double d3 = Direction(x1, y1, x2, y2, x3, y3);
            double d4 = Direction(x1, y1, x2, y2, x4, y4);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            {
                return true;
            }

            return false;
        }

        private static double Direction(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            return (x3 - x1) * (y2 - y1) - (y3 - y1) * (x2 - x1);
        }
    }
}
