using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Hazard;
using System;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Model
{
    public class RoomExtractor
    {
        public List<RoomData> ExtractRooms(
            RevitModelContext context,
            List<LevelData> levels,
            List<CeilingExtractor.ExtractedCeilingItem> ceilingItems,
            List<ExtractionIssue> issues)
        {
            List<RoomData> allRooms = new List<RoomData>();

            // 1. Linked architectural models
            foreach (RevitLinkContext link in context.LoadedLinks)
            {
                if (link.LinkedDocument == null) continue;

#if REVIT_2024 || REVIT_2025 || REVIT_2026
                string linkInstanceId = link.InstanceId.Value.ToString();
#else
                string linkInstanceId = link.InstanceId.ToString();
#endif

                ExtractFromDocument(
                    link.LinkedDocument,
                    link.TotalTransform ?? link.Transform,
                    new SourceReferenceData
                    {
                        DocumentTitle = link.DocumentTitle,
                        DocumentPath = link.DocumentPath,
                        IsFromLink = true,
                        LinkInstanceId = linkInstanceId,
                        LinkName = link.LinkName
                    },
                    levels,
                    ceilingItems,
                    allRooms,
                    issues);
            }

            // 2. Host model (if rooms exist directly in host)
            ExtractFromDocument(
                context.HostDocument,
                Transform.Identity,
                new SourceReferenceData
                {
                    DocumentTitle = context.HostDocument.Title,
                    DocumentPath = context.HostDocument.PathName ?? string.Empty,
                    IsFromLink = false,
                    LinkInstanceId = string.Empty,
                    LinkName = string.Empty
                },
                levels,
                ceilingItems,
                allRooms,
                issues);

            return allRooms;
        }

        private void ExtractFromDocument(
            Document document,
            Transform transform,
            SourceReferenceData source,
            List<LevelData> levels,
            List<CeilingExtractor.ExtractedCeilingItem> ceilingItems,
            List<RoomData> allRooms,
            List<ExtractionIssue> issues)
        {
            FilteredElementCollector collector;
            try
            {
                collector = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType();
            }
            catch (Exception ex)
            {
                issues.Add(new ExtractionIssue(
                    ExtractionIssueSeverity.Warning,
                    "RoomExtraction",
                    $"Failed to collect rooms from '{source.DocumentTitle}': {ex.Message}"));
                return;
            }

            foreach (Element element in collector)
            {
                if (!(element is Room room)) continue;

                // Skip unplaced / unbound rooms
                if (room.Area <= 0 || room.Location == null)
                {
                    continue;
                }

#if REVIT_2024 || REVIT_2025 || REVIT_2026
                string elementId = room.Id.Value.ToString();
#else
                string elementId = room.Id.ToString();
#endif

                try
                {
                    // Resolve Level
                    LevelData matchedLevel = ResolveRoomLevel(room, document, transform, levels);
                    string levelId = matchedLevel?.LevelId ?? (room.LevelId != null ? room.LevelId.ToString() : string.Empty);
                    string levelName = matchedLevel?.Name ?? (room.Level != null ? room.Level.Name : "<Unknown Level>");
                    double levelElevation = matchedLevel?.ElevationFt ?? (room.Level != null ? room.Level.Elevation : 0.0);

                    // Hazard classification via senior-provided HazardClassifier
                    HazardResult hazard = HazardClassifier.ClassifyByName(room.Name);
                    string hazardClassUpper = hazard.Class.ToString().ToUpperInvariant();

                    // Location Point in host MEP coordinates
                    Point3DData hostLocationPoint = null;
                    if (room.Location is LocationPoint locPt && locPt.Point != null)
                    {
                        XYZ hPt = RevitModelContext.TransformPoint(locPt.Point, transform);
                        hostLocationPoint = new Point3DData(hPt.X, hPt.Y, hPt.Z);
                    }

                    // Bounding box in host MEP coordinates
                    BoundingBoxXYZ localBBox = room.get_BoundingBox(null);
                    BoundingBox3DData hostBBox = localBBox != null
                        ? RevitModelContext.TransformBoundingBox(localBBox, transform)
                        : null;

                    // Boundary Extraction
                    BoundaryData boundaryData = ExtractBoundary(document, room, transform, issues);

                    // Room volume (if computed)
                    double? volumeCuFt = null;
                    if (room.Volume > 0)
                    {
                        volumeCuFt = room.Volume;
                    }

                    // Phase
                    string phaseName = string.Empty;
                    Parameter phaseParam = room.get_Parameter(BuiltInParameter.ROOM_PHASE);
                    if (phaseParam != null && phaseParam.HasValue)
                    {
                        phaseName = phaseParam.AsValueString();
                    }

                    // Ceiling association
                    double floorZ = levelElevation;
                    double heightEstimate = 0.0;
                    if (hostBBox != null && (hostBBox.Max.Z - hostBBox.Min.Z) > 0.5)
                    {
                        heightEstimate = hostBBox.Max.Z - hostBBox.Min.Z;
                    }
                    else if (room.UnboundedHeight > 0)
                    {
                        heightEstimate = room.UnboundedHeight;
                    }
                    else
                    {
                        heightEstimate = 20.0;
                    }
                    double roomTopZ = floorZ + heightEstimate;

                    List<CeilingData> matchedCeilings = FindCeilingsForRoom(
                        room,
                        hostLocationPoint,
                        hostBBox,
                        floorZ,
                        roomTopZ,
                        levelId,
                        ceilingItems,
                        boundaryData?.Polygon);



                    //Nemo

                    // Determine primary ceiling height and type
                    double? ceilingHeightFt = room.UnboundedHeight > 0 ? (double?)room.UnboundedHeight : null;
                    string ceilingType = "NONE";

                    if (matchedCeilings.Count > 0)
                    {
                        CeilingData primaryCeiling = matchedCeilings[0];
                        ceilingType = primaryCeiling.SlopeType;
                        if (primaryCeiling.BottomElevationFt.HasValue)
                        {
                            ceilingHeightFt = primaryCeiling.BottomElevationFt.Value - levelElevation;
                            primaryCeiling.HeightAboveLevelFt = ceilingHeightFt;
                        }
                    }

                    //// Determine primary ceiling height and type
                    //double? ceilingHeightFt = null;
                    //string ceilingType = "NONE";

                    //if (matchedCeilings.Count > 0)
                    //{
                    //    CeilingData primaryCeiling = matchedCeilings[0];
                    //    ceilingType = primaryCeiling.SlopeType;
                    //    if (primaryCeiling.BottomElevationFt.HasValue)
                    //    {
                    //        ceilingHeightFt = primaryCeiling.BottomElevationFt.Value - levelElevation;
                    //        primaryCeiling.HeightAboveLevelFt = ceilingHeightFt;
                    //    }
                    //}

                    else
                    {
                        // No physical ceiling exists — leave ceilingHeightFt as null so the engine
                        // flags the room for review or resolves the sloped roof underside via ray-casting.
                    }
                    List<string> roomWarnings = new List<string>();
                    if (boundaryData.Polygon.Count == 0)
                    {
                        roomWarnings.Add("Room boundary could not be resolved from boundary segments.");
                    }
                    if (matchedCeilings.Count == 0)
                    {
                        roomWarnings.Add("No physical ceiling found intersecting room volume; using unbounded room height.");
                    }

                    RoomData roomData = new RoomData
                    {
                        RoomId = elementId,
                        ElementId = elementId,
                        Name = room.Name,
                        Number = room.Number,
                        LevelId = levelId,
                        LevelName = levelName,
                        LevelElevationFt = levelElevation,
                        AreaSqFt = room.Area,
                        VolumeCuFt = volumeCuFt,
                        Phase = phaseName,
                        Source = new SourceReferenceData
                        {
                            DocumentTitle = source.DocumentTitle,
                            DocumentPath = source.DocumentPath,
                            IsFromLink = source.IsFromLink,
                            LinkInstanceId = source.LinkInstanceId,
                            LinkName = source.LinkName
                        },
                        LocationPoint = hostLocationPoint,
                        BoundingBox = hostBBox,
                        Boundary = boundaryData,
                        Hazard = new HazardData
                        {
                            HazardClass = hazard.Class.ToString(),
                            MatchedKeyword = hazard.MatchedKeyword,
                            MatchedTerms = !string.IsNullOrEmpty(hazard.MatchedKeyword)
                                ? new List<string>(hazard.MatchedKeyword.Split('+'))
                                : new List<string>(),
                            RequiresReview = hazard.RequiresHumanReview
                        },
                        Classification = new ClassificationData
                        {
                            HazardClass = hazardClassUpper,
                            SuggestedByClassifier = hazardClassUpper,
                            Overridden = false,
                            ConfirmedBy = string.Empty
                        },
                        Geometry = new GeometryData
                        {
                            Polygon = boundaryData.Polygon,
                            CeilingHeightFt = ceilingHeightFt,
                            CeilingType = ceilingType
                        },
                        Ceilings = matchedCeilings,
                        RequiresHumanReview = hazard.RequiresHumanReview,
                        Warnings = roomWarnings
                    };

                    // Add room to level collection
                    if (matchedLevel != null)
                    {
                        matchedLevel.Rooms.Add(roomData);
                    }

                    allRooms.Add(roomData);
                }
                catch (Exception ex)
                {
                    issues.Add(new ExtractionIssue(
                        ExtractionIssueSeverity.Warning,
                        "RoomExtraction",
                        $"Error extracting room {room.Number} ({room.Name}): {ex.Message}",
                        elementId,
                        room.Name));
                }
            }
        }

        private LevelData ResolveRoomLevel(
            Room room,
            Document document,
            Transform transform,
            List<LevelData> levels)
        {
            if (room.LevelId != null && room.LevelId != ElementId.InvalidElementId)
            {
#if REVIT_2024 || REVIT_2025 || REVIT_2026
                string localLevelId = room.LevelId.Value.ToString();
#else
                string localLevelId = room.LevelId.ToString();
#endif

                // Match by ID
                foreach (LevelData lvl in levels)
                {
                    if (lvl.LevelId == localLevelId) return lvl;
                }
            }

            // Fallback: match by elevation
            if (room.Level != null)
            {
                XYZ sourcePt = new XYZ(0, 0, room.Level.Elevation);
                XYZ hostPt = RevitModelContext.TransformPoint(sourcePt, transform);

                LevelData bestMatch = null;
                double minDelta = double.MaxValue;

                foreach (LevelData lvl in levels)
                {
                    double delta = Math.Abs(lvl.ElevationFt - hostPt.Z);
                    if (delta < 0.1 && delta < minDelta)
                    {
                        minDelta = delta;
                        bestMatch = lvl;
                    }
                }

                if (bestMatch != null) return bestMatch;
            }

            return null;
        }

        private BoundaryData ExtractBoundary(
            Document document,
            Room room,
            Transform transform,
            List<ExtractionIssue> issues)
        {
            BoundaryData result = new BoundaryData();

            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            IList<IList<BoundarySegment>> boundaryLoops;
            try
            {
                boundaryLoops = room.GetBoundarySegments(options);
            }
            catch
            {
                boundaryLoops = null;
            }

            if (boundaryLoops == null || boundaryLoops.Count == 0)
            {
                return result;
            }

            bool isFirstLoop = true;
            foreach (IList<BoundarySegment> loop in boundaryLoops)
            {
                BoundaryLoopData loopData = new BoundaryLoopData
                {
                    IsOuter = isFirstLoop
                };

                foreach (BoundarySegment segment in loop)
                {
                    Curve curve = segment.GetCurve();
                    if (curve == null) continue;

                    XYZ start = RevitModelContext.TransformPoint(curve.GetEndPoint(0), transform);
                    XYZ end = RevitModelContext.TransformPoint(curve.GetEndPoint(1), transform);
                    XYZ mid = RevitModelContext.TransformPoint(curve.Evaluate(0.5, true), transform);

                    string segType = "Line";
                    double? radius = null;

                    if (curve is Arc arc)
                    {
                        segType = "Arc";
                        radius = arc.Radius;
                    }
                    else if (curve is Ellipse || curve is NurbSpline || curve is HermiteSpline)
                    {
                        segType = "Curve";
                    }

                    bool isWallBoundary = segment.ElementId != ElementId.InvalidElementId
                        && document.GetElement(segment.ElementId) is Wall;

                    BoundarySegmentData segData = new BoundarySegmentData
                    {
                        Type = segType,
                        Start = new Point3DData(start.X, start.Y, start.Z),
                        End = new Point3DData(end.X, end.Y, end.Z),
                        Mid = new Point3DData(mid.X, mid.Y, mid.Z),
                        LengthFt = curve.Length,
                        RadiusFt = radius,
                        IsWall = isWallBoundary
                    };

                    loopData.Segments.Add(segData);

                    // Add start point to loop polygon vertices
                    loopData.Polygon.Add(new double[] { start.X, start.Y });

                    // If curve is an arc or non-linear, tessellate interior points to preserve true shape
                    if (segType != "Line")
                    {
                        IList<XYZ> tessellated = curve.Tessellate();
                        for (int i = 1; i < tessellated.Count - 1; i++)
                        {
                            XYZ tPt = RevitModelContext.TransformPoint(tessellated[i], transform);
                            loopData.Polygon.Add(new double[] { tPt.X, tPt.Y });
                        }
                    }
                }

                if (isFirstLoop)
                {
                    result.OuterLoop = loopData;
                    result.Polygon = new List<double[]>(loopData.Polygon);
                    isFirstLoop = false;
                }
                else
                {
                    result.InnerLoops.Add(loopData);
                }
            }

            return result;
        }

        private List<CeilingData> FindCeilingsForRoom(
            Room room,
            Point3DData hostLocationPoint,
            BoundingBox3DData hostBBox,
            double roomBottomZ,
            double roomTopZ,
            string roomLevelId,
            List<CeilingExtractor.ExtractedCeilingItem> ceilingItems,
            List<double[]> roomPolygon = null)
        {
            List<CeilingData> matches = new List<CeilingData>();
            if (ceilingItems == null || ceilingItems.Count == 0)
            {
                return matches;
            }

            // -------------------------------------------------------------------------
            // PASS 1: Search for true finished ceilings (OST_Ceilings).
            // Finished ceilings take strict priority over overhead structural slabs.
            // -------------------------------------------------------------------------
            foreach (CeilingExtractor.ExtractedCeilingItem cItem in ceilingItems)
            {
                if (cItem.HostBoundingBox == null) continue;
                if (!string.Equals(cItem.Dto?.Category, "OST_Ceilings", StringComparison.OrdinalIgnoreCase)) continue;

                BoundingBox3DData cBox = cItem.HostBoundingBox;

                // XY coarse check
                bool xyOverlap = (hostBBox != null)
                    ? SpatialHelpers.XYOverlap(cBox, hostBBox)
                    : (hostLocationPoint != null && SpatialHelpers.XYContains(cBox, hostLocationPoint));

                if (!xyOverlap) continue;

                // Z overlap: ceiling's vertical extent must intersect the room's vertical band
                if (cBox.Min.Z <= roomTopZ + 0.5 && cBox.Max.Z >= roomBottomZ - 0.5)
                {
                    // Precise horizontal test: verify that the ceiling face physically covers
                    // or intersects this room (prevents L-shaped/adjacent corridor ceilings
                    // from falsely matching neighboring rooms).
                    if (CeilingIntersectsRoom(cItem, hostLocationPoint, hostBBox, roomPolygon))
                    {
                        CeilingData clone = CloneCeilingData(cItem.Dto);
                        clone.IsRoomDirectCeiling = true;
                        matches.Add(clone);
                    }
                }
            }

            // -------------------------------------------------------------------------
            // PASS 2: If the room has NO true finished ceiling OR only partial-coverage
            // ceilings (such as modular bathroom pods / Model Groups covering < 50% of the room),
            // search for the overhead structural floor slab (OST_Floors) or roof (OST_Roofs).
            // This ensures open-structure / loft rooms with local pods are based on the true
            // room-wide overhead deck (e.g. 12' 10") rather than adopting the 8' pod height
            // for the entire room.
            // -------------------------------------------------------------------------
            double roomArea = room.Area;
            bool hasFullCoverageCeiling = false;
            foreach (CeilingData cd in matches)
            {
                bool isPartial = cd.IsModelGroup || (roomArea > 0 && cd.AreaSqFt > 0 && (cd.AreaSqFt / roomArea) < 0.50);
                if (!isPartial)
                {
                    hasFullCoverageCeiling = true;
                    break;
                }
            }

            if (!hasFullCoverageCeiling)
            {
                CeilingExtractor.ExtractedCeilingItem bestSlab = null;
                double closestDelta = double.MaxValue;

                foreach (CeilingExtractor.ExtractedCeilingItem cItem in ceilingItems)
                {
                    if (cItem.HostBoundingBox == null) continue;
                    string cat = cItem.Dto?.Category;
                    if (!string.Equals(cat, "OST_Floors", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(cat, "OST_Roofs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    BoundingBox3DData cBox = cItem.HostBoundingBox;

                    // Fast XY overlap
                    if (!SpatialHelpers.XYOverlap(cBox, hostBBox)) continue;

                    // The slab underside must be overhead (at least 4.0 ft above room floor)
                    double slabBottomZ = cItem.BottomElevationFt ?? cBox.Min.Z;
                    if (slabBottomZ < roomBottomZ + 4.0) continue;

                    // Must sit near the room's upper limit (not multiple floors above)
                    if (slabBottomZ > roomTopZ + 4.0) continue;

                    // Verify slab covers the room horizontally
                    bool covers = (hostLocationPoint != null && SpatialHelpers.XYContains(cBox, hostLocationPoint))
                        || (hostBBox != null && SpatialHelpers.XYOverlap(cBox, hostBBox));
                    if (!covers) continue;

                    double delta = Math.Abs(slabBottomZ - roomTopZ);
                    if (delta < closestDelta)
                    {
                        closestDelta = delta;
                        bestSlab = cItem;
                    }
                }

                if (bestSlab != null)
                {
                    CeilingData slabDto = CloneCeilingData(bestSlab.Dto);
                    slabDto.IsRoomDirectCeiling = true;
                    double slabBottomZ = bestSlab.BottomElevationFt ?? bestSlab.HostBoundingBox.Min.Z;
                    slabDto.BottomElevationFt = slabBottomZ;
                    slabDto.HeightAboveLevelFt = slabBottomZ - roomBottomZ;
                    matches.Add(slabDto);
                }
            }

            if (matches.Count == 0) return matches;

            // Sort: Full-coverage ceilings / overhead slabs come first (so the room's
            // primary ceiling is the true room-wide boundary, not a small pod),
            // then level-matched, then by BottomElevationFt descending.
            bool hasRoomLevel = !string.IsNullOrEmpty(roomLevelId);
            matches.Sort((a, b) =>
            {
                bool aPartial = a.IsModelGroup || (roomArea > 0 && a.AreaSqFt > 0 && (a.AreaSqFt / roomArea) < 0.50);
                bool bPartial = b.IsModelGroup || (roomArea > 0 && b.AreaSqFt > 0 && (b.AreaSqFt / roomArea) < 0.50);
                if (aPartial != bPartial) return aPartial.CompareTo(bPartial); // false (full) before true (partial)

                int aLevel = (hasRoomLevel && a != null && string.Equals(a.LevelId, roomLevelId, System.StringComparison.OrdinalIgnoreCase)) ? 0 : 1;
                int bLevel = (hasRoomLevel && b != null && string.Equals(b.LevelId, roomLevelId, System.StringComparison.OrdinalIgnoreCase)) ? 0 : 1;
                if (aLevel != bLevel) return aLevel.CompareTo(bLevel);
                double aBottom = a.BottomElevationFt ?? double.NegativeInfinity;
                double bBottom = b.BottomElevationFt ?? double.NegativeInfinity;
                return bBottom.CompareTo(aBottom); // descending
            });

            return matches;
        }

        private static bool CeilingIntersectsRoom(
            CeilingExtractor.ExtractedCeilingItem cItem,
            Point3DData hostLocationPoint,
            BoundingBox3DData hostBBox,
            List<double[]> roomPolygon)
        {
            if (cItem.BottomFaces == null || cItem.BottomFaces.Count == 0)
            {
                return true;
            }

            Transform invTransform = cItem.Transform != null ? cItem.Transform.Inverse : null;

            // 1. Check if the room's location point projects onto any bottom face of the ceiling
            if (hostLocationPoint != null)
            {
                XYZ locPt = new XYZ(hostLocationPoint.X, hostLocationPoint.Y, cItem.BottomElevationFt ?? hostLocationPoint.Z);
                XYZ localLocPt = invTransform != null ? invTransform.OfPoint(locPt) : locPt;
                foreach (PlanarFace face in cItem.BottomFaces)
                {
                    IntersectionResult ir = face.Project(localLocPt);
                    if (ir != null) return true;
                }
            }

            // 2. Check if the room center projects onto any bottom face
            if (hostBBox != null)
            {
                XYZ centerPt = new XYZ(
                    (hostBBox.Min.X + hostBBox.Max.X) * 0.5,
                    (hostBBox.Min.Y + hostBBox.Max.Y) * 0.5,
                    cItem.BottomElevationFt ?? ((hostBBox.Min.Z + hostBBox.Max.Z) * 0.5));
                XYZ localCenterPt = invTransform != null ? invTransform.OfPoint(centerPt) : centerPt;
                foreach (PlanarFace face in cItem.BottomFaces)
                {
                    IntersectionResult ir = face.Project(localCenterPt);
                    if (ir != null) return true;
                }
            }

            // 3. Check if any room polygon vertex projects onto any bottom face
            if (roomPolygon != null && roomPolygon.Count >= 3)
            {
                for (int i = 0; i < roomPolygon.Count; i++)
                {
                    XYZ polyPt = new XYZ(roomPolygon[i][0], roomPolygon[i][1], cItem.BottomElevationFt ?? 0.0);
                    XYZ localPolyPt = invTransform != null ? invTransform.OfPoint(polyPt) : polyPt;
                    foreach (PlanarFace face in cItem.BottomFaces)
                    {
                        IntersectionResult ir = face.Project(localPolyPt);
                        if (ir != null) return true;
                    }
                }
            }

            // 4. Check if any vertex of the ceiling bottom face is inside the room polygon
            if (roomPolygon != null && roomPolygon.Count >= 3)
            {
                foreach (PlanarFace face in cItem.BottomFaces)
                {
                    try
                    {
                        Mesh mesh = face.Triangulate();
                        if (mesh != null)
                        {
                            for (int i = 0; i < mesh.Vertices.Count; i++)
                            {
                                XYZ hv = RevitModelContext.TransformPoint(mesh.Vertices[i], cItem.Transform);
                                if (SpatialHelpers.PointInPolygonXY(hv.X, hv.Y, roomPolygon))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore triangulation errors
                    }
                }
            }

            return false;
        }

        private static CeilingData CloneCeilingData(CeilingData src)
        {
            return new CeilingData
            {
                ElementId = src.ElementId,
                LevelId = src.LevelId,
                LevelName = src.LevelName,
                CeilingName = src.CeilingName,
                FamilyName = src.FamilyName,
                TypeName = src.TypeName,
                Category = src.Category,
                Source = new SourceReferenceData
                {
                    DocumentTitle = src.Source.DocumentTitle,
                    DocumentPath = src.Source.DocumentPath,
                    IsFromLink = src.Source.IsFromLink,
                    LinkInstanceId = src.Source.LinkInstanceId,
                    LinkName = src.Source.LinkName
                },
                BoundingBox = src.BoundingBox,
                BottomElevationFt = src.BottomElevationFt,
                TopElevationFt = src.TopElevationFt,
                HeightAboveLevelFt = src.HeightAboveLevelFt,
                SlopeType = src.SlopeType,
                SlopeDegrees = src.SlopeDegrees,
                IsRoomDirectCeiling = src.IsRoomDirectCeiling,
                IsModelGroup = src.IsModelGroup,
                AreaSqFt = src.AreaSqFt,
                // Grid fields MUST be carried, or the ceiling reaches the calc engine with
                // HasReadableGrid=false and tile-center snapping silently never runs (the room
                // falls back to the free/centered array or the user-typed tile size).
                ThicknessFt = src.ThicknessFt,
                BoundaryPolygon = src.BoundaryPolygon,
                HasReadableGrid = src.HasReadableGrid,
                GridOriginXFt = src.GridOriginXFt,
                GridOriginYFt = src.GridOriginYFt,
                GridSpacingUFt = src.GridSpacingUFt,
                GridSpacingVFt = src.GridSpacingVFt,
                GridAngleRad = src.GridAngleRad,
                Notes = src.Notes
            };
        }
    }
}
