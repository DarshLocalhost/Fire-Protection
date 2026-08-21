using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// Resolves a host-model Level for a given target elevation (in feet).
    /// Strategy: nearest host Level by absolute elevation difference, subject
    /// to a configurable tolerance. Default tolerance is 0.5 ft.
    /// </summary>
    public class HostLevelResolver
    {
        private const double DefaultToleranceFt = 0.5;

        private readonly List<Level> _hostLevels;
        private readonly double _toleranceFt;

        public HostLevelResolver(Document hostDocument)
            : this(hostDocument, DefaultToleranceFt)
        {
        }

        public HostLevelResolver(Document hostDocument, double toleranceFt)
        {
            if (hostDocument == null) throw new ArgumentNullException(nameof(hostDocument));

            _toleranceFt = toleranceFt > 0.0 ? toleranceFt : DefaultToleranceFt;

            _hostLevels = new List<Level>();

            FilteredElementCollector collector =
                new FilteredElementCollector(hostDocument).OfClass(typeof(Level));

            foreach (Element element in collector)
            {
                if (element is Level level) _hostLevels.Add(level);
            }
        }

        public bool HasAnyLevel { get { return _hostLevels.Count > 0; } }

        public double ToleranceFt { get { return _toleranceFt; } }

        /// <summary>
        /// Returns the nearest host Level to the given elevation.
        /// Returns null if no host level exists or none is within tolerance.
        /// </summary>
        public Level Resolve(double targetElevationFt, out double deltaFt)
        {
            deltaFt = double.MaxValue;
            Level best = null;

            for (int i = 0; i < _hostLevels.Count; i++)
            {
                Level level = _hostLevels[i];
                double d = Math.Abs(level.Elevation - targetElevationFt);

                if (d < deltaFt)
                {
                    deltaFt = d;
                    best = level;
                }
            }

            if (best == null) return null;
            if (deltaFt > _toleranceFt) return null;

            return best;
        }
    }
}