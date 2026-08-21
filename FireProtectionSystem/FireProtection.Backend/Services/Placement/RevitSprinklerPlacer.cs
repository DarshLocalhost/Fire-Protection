using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using FireProtection.Backend.Models.Placement;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// Creates sprinkler FamilyInstances in the host document for one room,
    /// using a caller-resolved host Level.
    ///
    /// IMPORTANT (host-level vs sprinkler Z):
    ///   * The host Level is resolved by the CALLER, from the target host-level
    ///     elevation (i.e. LevelData.ElevationFt / PlacementRequestItem.LevelElevationFt).
    ///   * The SprinklerPlacementPoint.Z is the sprinkler MOUNTING elevation
    ///     (typically ceiling minus offset), and must NOT be used to identify
    ///     a host Level. These are separate concepts.
    ///
    /// Hosting:
    ///   * The selected FamilyPlacementType is inspected at runtime.
    ///   * FaceBased families use a real host-face Reference and the face-based
    ///     NewFamilyInstance overload.
    ///   * HostBased families use a real host Element and the host-based
    ///     NewFamilyInstance overload.
    ///   * WorkPlaneBased families use the resolved ceiling host Element and
    ///     the host-element NewFamilyInstance overload.
    ///   * OneLevelBased families use the resolved host Level directly.
    ///
    /// Callers are responsible for opening/committing the Transaction.
    /// </summary>
    public class RevitSprinklerPlacer
    {
        private readonly Document _hostDocument;
        private readonly FamilySymbol _symbol;
        private readonly Level _hostLevel;
        private HostFaceInfo _currentHostFace;

        public RevitSprinklerPlacer(
            Document hostDocument,
            FamilySymbol symbol,
            Level hostLevel)
        {
            _hostDocument = hostDocument ?? throw new ArgumentNullException(nameof(hostDocument));
            _symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
            _hostLevel = hostLevel ?? throw new ArgumentNullException(nameof(hostLevel));
        }

        /// <summary>
        /// Places one FamilyInstance per SprinklerPlacementPoint on the
        /// caller-provided host Level. Returns the number of successfully
        /// created instances. Per-point failures are appended to <paramref name="perPointErrors"/>.
        /// The caller must have an open Transaction on the host Document.
        /// </summary>
        public int Place(
            IList<SprinklerPlacementPoint> points,
            List<string> perPointErrors)
        {
            if (perPointErrors == null) throw new ArgumentNullException(nameof(perPointErrors));

            if (points == null || points.Count == 0)
            {
                perPointErrors.Add("No sprinkler points to place.");
                return 0;
            }

            if (!_symbol.IsActive)
            {
                try
                {
                    _symbol.Activate();
                    _hostDocument.Regenerate();
                }
                catch (Exception ex)
                {
                    perPointErrors.Add(
                        "Failed to activate FamilySymbol '"
                        + SafeSymbolName(_symbol) + "': "
                        + ExceptionMessage(ex));
                    return 0;
                }
            }

            int placed = 0;

            for (int i = 0; i < points.Count; i++)
            {
                SprinklerPlacementPoint p = points[i];

                try
                {
                    ValidateSymbol();

                    XYZ position = CreateValidPosition(p);
                    FamilyInstance instance = CreateHostedInstance(position);

                    if (instance != null)
                    {
                        placed++;
                    }
                    else
                    {
                        perPointErrors.Add(
                            "NewFamilyInstance returned null at "
                            + FormatPoint(p) + " on level '" + _hostLevel.Name + "'.");
                    }
                }
                catch (Exception ex)
                {
                    perPointErrors.Add(BuildDiagnosticMessage(p, ex));
                }
            }

            return placed;
        }

        private FamilyInstance CreateHostedInstance(XYZ position)
        {
            _currentHostFace = null;

            FamilyPlacementType placementType = _symbol.Family.FamilyPlacementType;
            string placementTypeName = placementType.ToString();

            if (string.Equals(placementTypeName, "OneLevelBased", StringComparison.OrdinalIgnoreCase))
            {
                return _hostDocument.Create.NewFamilyInstance(
                    position,
                    _symbol,
                    _hostLevel,
                    StructuralType.NonStructural);
            }

            if (string.Equals(placementTypeName, "WorkPlaneBased", StringComparison.OrdinalIgnoreCase))
            {
                HostFaceInfo hostFace = FindHostFace(position);
                _currentHostFace = hostFace;

                if (hostFace == null
                    || hostFace.HostElement == null
                    || hostFace.HostElement.Id == null)
                {
                    throw new InvalidOperationException(
                        "No ceiling host element was found for the WorkPlaneBased family.");
                }

                return _hostDocument.Create.NewFamilyInstance(
                    position,
                    _symbol,
                    hostFace.HostElement,
                    StructuralType.NonStructural);
            }

            if (string.Equals(placementTypeName, "FaceBased", StringComparison.OrdinalIgnoreCase))
            {
                HostFaceInfo hostFace = FindHostFace(position);
                _currentHostFace = hostFace;

                if (hostFace == null
                    || hostFace.Reference == null
                    || hostFace.ReferenceDirection == null)
                {
                    throw new InvalidOperationException(
                        "No ceiling face/reference was found at the sprinkler point.");
                }

                // A face-hosted instance must be located on its host face.
                // Do not silently change the requested mounting Z.
                if (Math.Abs(hostFace.IntersectionPoint.Z - position.Z) > 0.01)
                {
                    throw new InvalidOperationException(
                        "The requested sprinkler Z is not on the resolved host face. "
                        + "Requested Z=" + position.Z.ToString("F3")
                        + " ft, host-face Z="
                        + hostFace.IntersectionPoint.Z.ToString("F3") + " ft.");
                }

                return _hostDocument.Create.NewFamilyInstance(
                    hostFace.Reference,
                    position,
                    hostFace.ReferenceDirection,
                    _symbol);
            }

            if (string.Equals(placementTypeName, "HostBased", StringComparison.OrdinalIgnoreCase))
            {
                HostFaceInfo hostFace = FindHostFace(position);
                _currentHostFace = hostFace;

                if (hostFace == null
                    || hostFace.HostElement == null
                    || hostFace.HostElement.Id == null)
                {
                    throw new InvalidOperationException(
                        "No ceiling host element was found at the sprinkler point.");
                }

                return _hostDocument.Create.NewFamilyInstance(
                    position,
                    _symbol,
                    hostFace.HostElement,
                    StructuralType.NonStructural);
            }

            throw new InvalidOperationException(
                "Unsupported FamilyPlacementType '"
                + placementTypeName
                + "'; no placement overload was used.");
        }

        private void ValidateSymbol()
        {
            if (_symbol == null)
                throw new InvalidOperationException("FamilySymbol is null.");

            if (_symbol.Family == null)
                throw new InvalidOperationException("FamilySymbol has no Family.");

            if (!_symbol.IsActive)
            {
                _symbol.Activate();
                _hostDocument.Regenerate();
            }

            if (!_symbol.IsActive)
                throw new InvalidOperationException("FamilySymbol is not active.");
        }

        private static XYZ CreateValidPosition(SprinklerPlacementPoint point)
        {
            if (point == null)
                throw new ArgumentNullException(nameof(point));

            if (!IsFinite(point.X) || !IsFinite(point.Y) || !IsFinite(point.Z))
            {
                throw new InvalidOperationException(
                    "Sprinkler point contains a non-finite coordinate.");
            }

            return new XYZ(point.X, point.Y, point.Z);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private HostFaceInfo FindHostFace(XYZ position)
        {
            const double searchDistanceFt = 1000.0;
            const double toleranceFt = 0.05;

            XYZ start = new XYZ(
                position.X,
                position.Y,
                position.Z - searchDistanceFt);

            XYZ end = new XYZ(
                position.X,
                position.Y,
                position.Z + searchDistanceFt);

            Line vertical = Line.CreateBound(start, end);

            Options options = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            HostFaceInfo best = null;

            FilteredElementCollector collector =
                new FilteredElementCollector(_hostDocument)
                    .OfCategory(BuiltInCategory.OST_Ceilings)
                    .WhereElementIsNotElementType();

            foreach (Element element in collector)
            {
                HostObject hostObject = element as HostObject;
                if (hostObject == null)
                    continue;

                GeometryElement geometry = hostObject.get_Geometry(options);
                if (geometry == null)
                    continue;

                FindHostFaceInGeometry(
                    geometry,
                    hostObject,
                    vertical,
                    position,
                    toleranceFt,
                    ref best);
            }

            return best;
        }

        private static void FindHostFaceInGeometry(
            GeometryElement geometry,
            HostObject hostObject,
            Line vertical,
            XYZ position,
            double toleranceFt,
            ref HostFaceInfo best)
        {
            foreach (GeometryObject geometryObject in geometry)
            {
                Solid solid = geometryObject as Solid;
                if (solid != null && solid.Volume > 0.0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        IntersectionResultArray results;
                        SetComparisonResult intersection =
                            face.Intersect(vertical, out results);

                        if (intersection != SetComparisonResult.Overlap || results == null)
                            continue;

                        for (int i = 0; i < results.Size; i++)
                        {
                            IntersectionResult result = results.get_Item(i);
                            if (result == null || result.XYZPoint == null)
                                continue;

                            XYZ hit = result.XYZPoint;
                            double distance = Math.Abs(hit.Z - position.Z);

                            if (best != null && distance >= best.DistanceFt)
                                continue;

                            if (distance > toleranceFt && hit.Z < position.Z)
                                continue;

                            XYZ normal = face.ComputeNormal(result.UVPoint);
                            XYZ referenceDirection =
                                Math.Abs(normal.DotProduct(XYZ.BasisX)) < 0.9
                                    ? XYZ.BasisX
                                    : XYZ.BasisY;

                            best = new HostFaceInfo
                            {
                                HostElement = hostObject,
                                Reference = face.Reference,
                                IntersectionPoint = hit,
                                ReferenceDirection = referenceDirection,
                                DistanceFt = distance
                            };
                        }
                    }
                }

                GeometryInstance instance = geometryObject as GeometryInstance;
                if (instance != null)
                {
                    GeometryElement instanceGeometry = instance.GetInstanceGeometry();
                    if (instanceGeometry != null)
                    {
                        FindHostFaceInGeometry(
                            instanceGeometry,
                            hostObject,
                            vertical,
                            position,
                            toleranceFt,
                            ref best);
                    }
                }
            }
        }

        private sealed class HostFaceInfo
        {
            public HostObject HostElement { get; set; }
            public Reference Reference { get; set; }
            public XYZ IntersectionPoint { get; set; }
            public XYZ ReferenceDirection { get; set; }
            public double DistanceFt { get; set; }
        }

        private string BuildDiagnosticMessage(
            SprinklerPlacementPoint point,
            Exception exception)
        {
            string placementType = GetPlacementTypeName();
            string hostId = "<none>";
            string hostCategory = "<none>";
            string hostFaceElevation = "<none>";

            if (_currentHostFace != null)
            {
                if (_currentHostFace.HostElement != null)
                {
                    hostId = _currentHostFace.HostElement.Id.Value.ToString();
                    hostCategory = _currentHostFace.HostElement.Category == null
                        ? "<none>"
                        : _currentHostFace.HostElement.Category.Name;
                }

                if (_currentHostFace.IntersectionPoint != null)
                {
                    hostFaceElevation =
                        _currentHostFace.IntersectionPoint.Z.ToString("F3") + " ft";
                }
            }

            return "NewFamilyInstance failed. "
                + "Family/Type='" + SafeSymbolName(_symbol) + "'; "
                + "FamilyPlacementType='" + placementType + "'; "
                + "Host='ceiling elements (OST_Ceilings)'; "
                + "HostElementId='" + hostId + "'; "
                + "HostCategory='" + hostCategory + "'; "
                + "HostFaceElevation='" + hostFaceElevation + "'; "
                + "RequestedXYZ=" + FormatPoint(point) + "; "
                + "Level='" + (_hostLevel == null ? "<null>" : _hostLevel.Name) + "'; "
                + "Reason=" + ExceptionMessage(exception);
        }

        private string GetPlacementTypeName()
        {
            if (_symbol == null || _symbol.Family == null)
                return "<unknown>";

            return _symbol.Family.FamilyPlacementType.ToString();
        }

        private static string FormatPoint(SprinklerPlacementPoint p)
        {
            return "(" + p.X.ToString("F3") + ", "
                       + p.Y.ToString("F3") + ", "
                       + p.Z.ToString("F3") + " ft)";
        }

        private static string SafeSymbolName(FamilySymbol s)
        {
            string family = s.Family != null ? s.Family.Name : "<no-family>";
            return family + " : " + s.Name;
        }

        private static string ExceptionMessage(Exception ex)
        {
            // Include type name so we can distinguish
            // Autodesk.Revit.Exceptions.* from InvalidOperationException, etc.
            return ex.GetType().Name + " - " + ex.Message;
        }
    }
}