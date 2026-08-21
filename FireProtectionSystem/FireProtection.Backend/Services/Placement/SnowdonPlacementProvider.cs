//using System;
//using System.Collections.Generic;
//using System.IO;
//using FireProtection.Backend.Models.Placement;
//using FireProtection.Backend.Models.Snowdon;
//using Newtonsoft.Json;

//namespace FireProtection.Backend.Services.Placement
//{
//    /// <summary>
//    /// Phase 1 test provider: loads the Snowdon sprinkler-coordinates JSON and
//    /// converts entries to SprinklerPlacementPoint objects.
//    ///
//    /// Matching is by roomId AND levelId (string equality, case-sensitive,
//    /// matching the Revit element ID strings produced by LevelExtractor and RoomExtractor).
//    ///
//    /// Z coordinate:
//    ///   sprinkler Z = levelElevationFt + entry.OffsetFromLevelFt
//    ///
//    /// This is consistent with how LevelData.ElevationFt is stored (host-model
//    /// coordinates, feet, link transform applied) and how RevitSprinklerPlacer
//    /// passes Z directly to NewFamilyInstance.
//    ///
//    /// X and Y are taken directly from the Snowdon JSON (already in host-model
//    /// coordinate system, feet).
//    ///
//    /// This class does NOT call GenerateGridPoints and does NOT use any
//    /// placement rules, polygon geometry, or hazard logic.
//    /// </summary>
//    public class SnowdonPlacementProvider
//    {
//        // Key: "levelId|roomId"  Value: list of entries
//        private readonly Dictionary<string, List<SnowdonSprinklerEntry>> _index;

//        /// <summary>
//        /// Loads and indexes the Snowdon JSON from the given file path.
//        /// Throws if the file cannot be read or parsed.
//        /// </summary>
//        public SnowdonPlacementProvider(string jsonFilePath)
//        {
//            if (string.IsNullOrWhiteSpace(jsonFilePath))
//                throw new ArgumentNullException(nameof(jsonFilePath));

//            if (!File.Exists(jsonFilePath))
//                throw new FileNotFoundException(
//                    "Snowdon sprinkler JSON not found: " + jsonFilePath, jsonFilePath);

//            string json = File.ReadAllText(jsonFilePath);

//            SnowdonSprinklerData data =
//                JsonConvert.DeserializeObject<SnowdonSprinklerData>(json);

//            if (data == null)
//                throw new InvalidOperationException(
//                    "Snowdon JSON deserialized to null. Check file content.");

//            _index = new Dictionary<string, List<SnowdonSprinklerEntry>>(
//                StringComparer.Ordinal);

//            if (data.Sprinklers != null)
//            {
//                foreach (SnowdonSprinklerEntry entry in data.Sprinklers)
//                {
//                    if (entry == null) continue;
//                    if (string.IsNullOrEmpty(entry.LevelId)) continue;
//                    if (string.IsNullOrEmpty(entry.RoomId)) continue;

//                    string key = MakeKey(entry.LevelId, entry.RoomId);

//                    if (!_index.ContainsKey(key))
//                        _index[key] = new List<SnowdonSprinklerEntry>();

//                    _index[key].Add(entry);
//                }
//            }
//        }

//        /// <summary>
//        /// Returns SprinklerPlacementPoint objects for the given room/level pair.
//        ///
//        /// levelElevationFt must be the LevelData.ElevationFt value already
//        /// stored in host-model coordinates (as produced by LevelExtractor).
//        ///
//        /// Z = levelElevationFt + entry.OffsetFromLevelFt
//        ///
//        /// Returns an empty list if no entries exist for this room/level.
//        /// </summary>
//        public List<SprinklerPlacementPoint> GetPoints(
//            string levelId,
//            string roomId,
//            double levelElevationFt)
//        {
//            List<SprinklerPlacementPoint> result = new List<SprinklerPlacementPoint>();

//            if (string.IsNullOrEmpty(levelId) || string.IsNullOrEmpty(roomId))
//                return result;

//            string key = MakeKey(levelId, roomId);

//            List<SnowdonSprinklerEntry> entries;
//            if (!_index.TryGetValue(key, out entries))
//                return result;

//            foreach (SnowdonSprinklerEntry entry in entries)
//            {
//                double z = levelElevationFt + entry.OffsetFromLevelFt;

//                result.Add(new SprinklerPlacementPoint(
//                    entry.X,
//                    entry.Y,
//                    z,
//                    entry.RoomId,
//                    entry.LevelId));
//            }

//            return result;
//        }

//        /// <summary>
//        /// Returns true if any sprinkler entries exist for this room/level pair.
//        /// </summary>
//        public bool HasPoints(string levelId, string roomId)
//        {
//            if (string.IsNullOrEmpty(levelId) || string.IsNullOrEmpty(roomId))
//                return false;

//            return _index.ContainsKey(MakeKey(levelId, roomId));
//        }

//        private static string MakeKey(string levelId, string roomId)
//        {
//            return levelId + "|" + roomId;
//        }
//    }
//}






























using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FireProtection.Backend.Models.Placement;
using FireProtection.Backend.Models.Snowdon;
using Newtonsoft.Json;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// Phase 1 test provider: loads Snowdon sprinkler coordinates.
    ///
    /// IMPORTANT:
    /// X and Y come from the Snowdon JSON.
    ///
    /// The JSON offsetFromLevelFt is intentionally NOT used for Z.
    ///
    /// Sprinkler Z is resolved from the actual Revit ceiling geometry at
    /// the sprinkler's X/Y location.
    /// </summary>
    public class SnowdonPlacementProvider
    {
        // Key: "levelId|roomId"
        // Value: sprinkler entries for that room/level.
        private readonly Dictionary<string, List<SnowdonSprinklerEntry>> _index;

        private readonly Document _document;
        private readonly HashSet<string> _diagnosticRooms =
            new HashSet<string>(StringComparer.Ordinal);

        private const int MaximumDiagnosticRooms = 5;

        /// <summary>
        /// Loads the Snowdon JSON and keeps a reference to the Revit document
        /// so actual ceiling geometry can be inspected during placement.
        /// </summary>
        public SnowdonPlacementProvider(
            string jsonFilePath,
            Document document)
        {
            if (string.IsNullOrWhiteSpace(jsonFilePath))
                throw new ArgumentNullException(nameof(jsonFilePath));

            if (document == null)
                throw new ArgumentNullException(nameof(document));

            if (!File.Exists(jsonFilePath))
            {
                throw new FileNotFoundException(
                    "Snowdon sprinkler JSON not found: " + jsonFilePath,
                    jsonFilePath);
            }

            _document = document;

            string json = File.ReadAllText(jsonFilePath);

            SnowdonSprinklerData data =
                JsonConvert.DeserializeObject<SnowdonSprinklerData>(json);

            if (data == null)
            {
                throw new InvalidOperationException(
                    "Snowdon JSON deserialized to null. Check file content.");
            }

            _index =
                new Dictionary<string, List<SnowdonSprinklerEntry>>(
                    StringComparer.Ordinal);

            if (data.Sprinklers == null)
                return;

            foreach (SnowdonSprinklerEntry entry in data.Sprinklers)
            {
                if (entry == null)
                    continue;

                if (string.IsNullOrEmpty(entry.LevelId))
                    continue;

                if (string.IsNullOrEmpty(entry.RoomId))
                    continue;

                string key = MakeKey(
                    entry.LevelId,
                    entry.RoomId);

                if (!_index.ContainsKey(key))
                {
                    _index[key] =
                        new List<SnowdonSprinklerEntry>();
                }

                _index[key].Add(entry);
            }
        }

        /// <summary>
        /// Returns sprinkler placement points for the requested room/level.
        ///
        /// X/Y:
        ///     Taken directly from Snowdon JSON.
        ///
        /// Z:
        ///     Resolved from the actual Revit ceiling geometry at X/Y.
        ///
        /// offsetFromLevelFt is intentionally ignored.
        /// </summary>
        public List<SprinklerPlacementPoint> GetPoints(
            string levelId,
            string roomId,
            double levelElevationFt)
        {
            List<SprinklerPlacementPoint> result =
                new List<SprinklerPlacementPoint>();

            if (string.IsNullOrEmpty(levelId) ||
                string.IsNullOrEmpty(roomId))
            {
                return result;
            }

            string key = MakeKey(levelId, roomId);

            List<SnowdonSprinklerEntry> entries;

            if (!_index.TryGetValue(key, out entries))
                return result;

            foreach (SnowdonSprinklerEntry entry in entries)
            {
                double z = ResolveCeilingElevation(
                    entry.X,
                    entry.Y,
                    levelElevationFt,
                    entry);

                result.Add(
                    new SprinklerPlacementPoint(
                        entry.X,
                        entry.Y,
                        z,
                        entry.RoomId,
                        entry.LevelId));
            }

            return result;
        }

        /// <summary>
        /// Returns true if any sprinkler entries exist for this room/level.
        /// </summary>
        public bool HasPoints(
            string levelId,
            string roomId)
        {
            if (string.IsNullOrEmpty(levelId) ||
                string.IsNullOrEmpty(roomId))
            {
                return false;
            }

            return _index.ContainsKey(
                MakeKey(levelId, roomId));
        }

        /// <summary>
        /// Finds the actual ceiling elevation at the supplied X/Y.
        ///
        /// A vertical line is cast upward from the level elevation.
        /// The first valid ceiling-face intersection above the level
        /// is used.
        ///
        /// This allows:
        ///     - flat ceilings
        ///     - sloped ceilings
        ///     - different ceiling elevations at different X/Y positions
        /// </summary>
        private double ResolveCeilingElevation(
            double x,
            double y,
            double levelElevationFt,
            SnowdonSprinklerEntry entry)
        {
            const double toleranceFt = 0.05;

            // Start slightly below the level so we don't miss a ceiling
            // that begins very close to the level.
            double startZ = levelElevationFt - 0.1;

            // Search sufficiently high above the level.
            // 1000 ft is deliberately generous for architectural models.
            double endZ = levelElevationFt + 1000.0;

            XYZ startPoint =
                new XYZ(x, y, startZ);

            XYZ endPoint =
                new XYZ(x, y, endZ);

            Line verticalLine =
                Line.CreateBound(
                    startPoint,
                    endPoint);

            List<double> intersections =
                new List<double>();

            FilteredElementCollector collector =
                new FilteredElementCollector(_document)
                    .OfCategory(BuiltInCategory.OST_Ceilings)
                    .WhereElementIsNotElementType();

            foreach (Element element in collector)
            {
                Ceiling ceiling =
                    element as Ceiling;

                if (ceiling == null)
                    continue;

                BoundingBoxXYZ boundingBox =
                    ceiling.get_BoundingBox(null);

                if (boundingBox == null)
                    continue;

                // Cheap XY test before opening the actual geometry.
                if (!IsPointInsideBoundingBoxXY(
                    x,
                    y,
                    boundingBox,
                    toleranceFt))
                {
                    continue;
                }

                CollectCeilingIntersections(
                    ceiling,
                    verticalLine,
                    levelElevationFt,
                    intersections);
            }

            if (intersections.Count == 0)
            {
                throw new InvalidOperationException(
                    "No ceiling was found at sprinkler location " +
                    FormatPoint(x, y, levelElevationFt) +
                    ". The sprinkler cannot be placed using " +
                    "ceiling-based elevation.\n" +
                    BuildCeilingLookupDiagnostics(entry, x, y, levelElevationFt));
            }

            // We want the lowest valid ceiling surface above the level.
            double closestZ = double.MaxValue;

            foreach (double z in intersections)
            {
                if (z < levelElevationFt - toleranceFt)
                    continue;

                if (z < closestZ)
                    closestZ = z;
            }

            if (closestZ == double.MaxValue)
            {
                throw new InvalidOperationException(
                    "A ceiling element was found near sprinkler location " +
                    FormatPoint(x, y, levelElevationFt) +
                    ", but no valid ceiling surface was found above " +
                    "the associated level.\n" +
                    BuildCeilingLookupDiagnostics(entry, x, y, levelElevationFt));
            }

            return closestZ;
        }

        private string BuildCeilingLookupDiagnostics(
            SnowdonSprinklerEntry entry,
            double x,
            double y,
            double levelElevationFt)
        {
            if (entry == null || !ClaimDiagnosticRoom(entry.RoomId))
                return "Ceiling diagnostics omitted after the five-room limit.";

            StringBuilder report = new StringBuilder();
            XYZ hostPoint = new XYZ(x, y, levelElevationFt);

            report.AppendLine("SNOWDON CEILING DIAGNOSTICS");
            report.AppendLine(
                "ActiveDocument='" + _document.Title + "'; Path='"
                + (_document.PathName ?? string.Empty) + "'");
            report.AppendLine(
                "JSON LevelId='" + entry.LevelId + "'; RoomId='"
                + entry.RoomId + "'; X=" + entry.X.ToString("F3")
                + "; Y=" + entry.Y.ToString("F3")
                + "; OffsetFromLevelFt=" + entry.OffsetFromLevelFt.ToString("F3"));
            report.AppendLine(
                "HostSearchPoint=" + FormatXyz(hostPoint)
                + "; SearchZ=" + (levelElevationFt - 0.1).ToString("F3")
                + ".." + (levelElevationFt + 1000.0).ToString("F3") + " ft");

            bool hostMatch = AppendDocumentCeilingDiagnostics(
                report,
                "HOST",
                _document,
                hostPoint,
                levelElevationFt);

            bool linkedMatch = false;
            FilteredElementCollector links =
                new FilteredElementCollector(_document)
                    .OfClass(typeof(RevitLinkInstance));

            int linkCount = 0;
            foreach (Element element in links)
            {
                RevitLinkInstance link = element as RevitLinkInstance;
                if (link == null)
                    continue;

                linkCount++;
                Document linkedDocument = link.GetLinkDocument();
                Transform transform = link.GetTransform();

                report.AppendLine(
                    "LINK ElementId=" + link.Id.Value.ToString()
                    + "; Document='"
                    + (linkedDocument == null ? "<unloaded>" : linkedDocument.Title)
                    + "'; TransformOrigin=" + FormatXyz(transform.Origin)
                    + "; BasisX=" + FormatXyz(transform.BasisX)
                    + "; BasisY=" + FormatXyz(transform.BasisY)
                    + "; BasisZ=" + FormatXyz(transform.BasisZ));

                if (linkedDocument == null)
                    continue;

                XYZ linkedPoint = transform.Inverse.OfPoint(hostPoint);
                report.AppendLine(
                    "LINKED SEARCH POINT=" + FormatXyz(linkedPoint)
                    + "; SearchZ=" + (linkedPoint.Z - 0.1).ToString("F3")
                    + ".." + (linkedPoint.Z + 1000.0).ToString("F3") + " ft");

                if (AppendDocumentCeilingDiagnostics(
                    report,
                    "LINKED",
                    linkedDocument,
                    linkedPoint,
                    linkedPoint.Z))
                {
                    linkedMatch = true;
                }
            }

            if (linkCount == 0)
                report.AppendLine("LINKS none");

            report.AppendLine(
                "MATCH RESULT: host ceiling=" + (hostMatch ? "YES" : "NO")
                + "; linked ceiling=" + (linkedMatch ? "YES" : "NO")
                + "; final placement decision unchanged=YES");

            return report.ToString().TrimEnd();
        }

        private bool AppendDocumentCeilingDiagnostics(
            StringBuilder report,
            string label,
            Document document,
            XYZ point,
            double levelElevationFt)
        {
            bool matched = false;
            int containingCount = 0;

            FilteredElementCollector ceilings =
                new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_Ceilings)
                    .WhereElementIsNotElementType();

            foreach (Element element in ceilings)
            {
                Ceiling ceiling = element as Ceiling;
                if (ceiling == null)
                    continue;

                BoundingBoxXYZ boundingBox = ceiling.get_BoundingBox(null);
                if (boundingBox == null
                    || !IsPointInsideBoundingBoxXY(point.X, point.Y, boundingBox, 0.05))
                {
                    continue;
                }

                containingCount++;
                List<string> geometryTypes = new List<string>();
                List<double> intersectionZs = new List<double>();
                Options options = new Options
                {
                    ComputeReferences = false,
                    DetailLevel = ViewDetailLevel.Fine,
                    IncludeNonVisibleObjects = true
                };

                GeometryElement geometry = ceiling.get_Geometry(options);
                CollectDiagnosticGeometry(
                    geometry,
                    point,
                    levelElevationFt,
                    geometryTypes,
                    intersectionZs);

                if (intersectionZs.Count > 0)
                    matched = true;

                report.AppendLine(
                    label + " CEILING ElementId=" + ceiling.Id.Value.ToString()
                    + "; BBoxMin=" + FormatXyz(boundingBox.Min)
                    + "; BBoxMax=" + FormatXyz(boundingBox.Max)
                    + "; GeometryTypes=" + Join(geometryTypes)
                    + "; VerticalIntersection="
                    + (intersectionZs.Count > 0 ? "YES" : "NO")
                    + "; IntersectionZ=" + FormatDoubles(intersectionZs));
            }

            if (containingCount == 0)
                report.AppendLine(label + " CEILINGS containing XY point: none");

            return matched;
        }

        private static void CollectDiagnosticGeometry(
            GeometryElement geometry,
            XYZ point,
            double levelElevationFt,
            List<string> geometryTypes,
            List<double> intersectionZs)
        {
            if (geometry == null)
                return;

            Line verticalLine = Line.CreateBound(
                new XYZ(point.X, point.Y, levelElevationFt - 0.1),
                new XYZ(point.X, point.Y, levelElevationFt + 1000.0));

            foreach (GeometryObject geometryObject in geometry)
            {
                string typeName = geometryObject.GetType().Name;
                if (!geometryTypes.Contains(typeName))
                    geometryTypes.Add(typeName);

                Solid solid = geometryObject as Solid;
                if (solid != null && solid.Volume > 0.0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        IntersectionResultArray results;
                        SetComparisonResult result =
                            face.Intersect(verticalLine, out results);

                        if (result != SetComparisonResult.Overlap || results == null)
                            continue;

                        for (int i = 0; i < results.Size; i++)
                        {
                            IntersectionResult intersection = results.get_Item(i);
                            if (intersection == null || intersection.XYZPoint == null)
                                continue;

                            if (intersection.XYZPoint.Z >= levelElevationFt - 0.05)
                                intersectionZs.Add(intersection.XYZPoint.Z);
                        }
                    }
                }

                GeometryInstance instance = geometryObject as GeometryInstance;
                if (instance != null)
                {
                    GeometryElement instanceGeometry = instance.GetInstanceGeometry();
                    CollectDiagnosticGeometry(
                        instanceGeometry,
                        point,
                        levelElevationFt,
                        geometryTypes,
                        intersectionZs);
                }
            }
        }

        private bool ClaimDiagnosticRoom(string roomId)
        {
            if (_diagnosticRooms.Contains(roomId))
                return false;

            if (_diagnosticRooms.Count >= MaximumDiagnosticRooms)
                return false;

            _diagnosticRooms.Add(roomId);
            return true;
        }

        private static string Join(List<string> values)
        {
            return values == null || values.Count == 0
                ? "none"
                : string.Join(",", values.ToArray());
        }

        private static string FormatDoubles(List<double> values)
        {
            if (values == null || values.Count == 0)
                return "none";

            List<string> formatted = new List<string>();
            foreach (double value in values)
                formatted.Add(value.ToString("F3") + " ft");

            return string.Join(",", formatted.ToArray());
        }

        private static string FormatXyz(XYZ point)
        {
            if (point == null)
                return "<null>";

            return "(" + point.X.ToString("F3") + ","
                + point.Y.ToString("F3") + ","
                + point.Z.ToString("F3") + ")";
        }

        /// <summary>
        /// Extracts intersections between the vertical sprinkler ray
        /// and the actual solid geometry of a ceiling.
        /// </summary>
        private void CollectCeilingIntersections(
     Ceiling ceiling,
     Line verticalLine,
     double levelElevationFt,
     List<double> intersections)
        {
            if (ceiling == null)
                return;

            Options options = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true,
                ComputeReferences = false
            };

            GeometryElement geometry =
                ceiling.get_Geometry(options);

            if (geometry == null)
                return;

            CollectGeometryIntersections(
                geometry,
                verticalLine,
                levelElevationFt,
                intersections);
        }
        private void CollectGeometryIntersections(
    GeometryElement geometry,
    Line verticalLine,
    double levelElevationFt,
    List<double> intersections)
        {
            if (geometry == null)
                return;

            foreach (GeometryObject geometryObject in geometry)
            {
                if (geometryObject == null)
                    continue;

                Solid solid = geometryObject as Solid;

                if (solid != null)
                {
                    if (solid.Volume <= 1e-9)
                        continue;

                    CollectSolidIntersections(
                        solid,
                        verticalLine,
                        levelElevationFt,
                        intersections);

                    continue;
                }

                GeometryInstance geometryInstance =
                    geometryObject as GeometryInstance;

                if (geometryInstance != null)
                {
                    GeometryElement instanceGeometry =
                        geometryInstance.GetInstanceGeometry();

                    if (instanceGeometry == null)
                        continue;

                    CollectGeometryIntersections(
                        instanceGeometry,
                        verticalLine,
                        levelElevationFt,
                        intersections);
                }
            }
        }
        private void CollectSolidIntersections(
    Solid solid,
    Line verticalLine,
    double levelElevationFt,
    List<double> intersections)
        {
            if (solid == null || solid.Volume <= 1e-9)
                return;

            FaceArray faces = solid.Faces;

            foreach (Face face in faces)
            {
                if (face == null)
                    continue;

                try
                {
                    IntersectionResultArray results;

                    SetComparisonResult result =
                        face.Intersect(
                            verticalLine,
                            out results);

                    if (result != SetComparisonResult.Overlap ||
                        results == null)
                    {
                        continue;
                    }

                    foreach (IntersectionResult intersection in results)
                    {
                        if (intersection == null)
                            continue;

                        XYZ point =
                            intersection.XYZPoint;

                        if (point == null)
                            continue;

                        double z = point.Z;

                        if (z < levelElevationFt - 0.05)
                            continue;

                        intersections.Add(z);
                    }
                }
                catch
                {
                    // Some Revit faces/geometry can reject direct intersection.
                    // Do not allow one problematic face to abort the entire ceiling.
                }
            }
        }


        ///
        /// 
        /// 
        /// 
        private void CollectFaceFallbackIntersection(
    Face face,
    double x,
    double y,
    double levelElevationFt,
    List<double> intersections)
        {
            if (face == null)
                return;

            BoundingBoxUV faceBox =
                face.GetBoundingBox();

            if (faceBox == null)
                return;

            // Check whether the supplied XY point can be projected
            // onto this face.
            UV candidate = new UV(
                faceBox.Min.U +
                (faceBox.Max.U - faceBox.Min.U) * 0.5,
                faceBox.Min.V +
                (faceBox.Max.V - faceBox.Min.V) * 0.5);

            try
            {
                IntersectionResult projection =
                    face.Project(
                        new XYZ(
                            x,
                            y,
                            levelElevationFt));

                if (projection == null)
                    return;

                XYZ projectedPoint =
                    projection.XYZPoint;

                if (projectedPoint == null)
                    return;

                double z = projectedPoint.Z;

                if (z >= levelElevationFt - 0.05)
                {
                    intersections.Add(z);
                }
            }
            catch
            {
                // Projection is only a fallback.
            }
        }
        /// <summary>
        /// Checks only X/Y against the ceiling bounding box.
        /// Z is intentionally ignored because the actual geometry
        /// intersection determines the final elevation.
        /// </summary>
        private static bool IsPointInsideBoundingBoxXY(
            double x,
            double y,
            BoundingBoxXYZ boundingBox,
            double toleranceFt)
        {
            return
                x >= boundingBox.Min.X - toleranceFt &&
                x <= boundingBox.Max.X + toleranceFt &&
                y >= boundingBox.Min.Y - toleranceFt &&
                y <= boundingBox.Max.Y + toleranceFt;
        }

        private static string FormatPoint(
            double x,
            double y,
            double z)
        {
            return
                "(" +
                x.ToString("F3") +
                ", " +
                y.ToString("F3") +
                ", level=" +
                z.ToString("F3") +
                " ft)";
        }

        private static string MakeKey(
            string levelId,
            string roomId)
        {
            return levelId + "|" + roomId;
        }
    }
}