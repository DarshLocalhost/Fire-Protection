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
                    BoundaryData boundaryData = ExtractBoundary(room, transform, issues);

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
                        ceilingItems);

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

                    BoundarySegmentData segData = new BoundarySegmentData
                    {
                        Type = segType,
                        Start = new Point3DData(start.X, start.Y, start.Z),
                        End = new Point3DData(end.X, end.Y, end.Z),
                        Mid = new Point3DData(mid.X, mid.Y, mid.Z),
                        LengthFt = curve.Length,
                        RadiusFt = radius
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
            List<CeilingExtractor.ExtractedCeilingItem> ceilingItems)
        {
            List<CeilingData> matches = new List<CeilingData>();
            if (ceilingItems == null || ceilingItems.Count == 0)
            {
                return matches;
            }

            foreach (CeilingExtractor.ExtractedCeilingItem cItem in ceilingItems)
            {
                if (cItem.HostBoundingBox == null) continue;

                BoundingBox3DData cBox = cItem.HostBoundingBox;

                // XY overlap between the ceiling footprint and the room footprint/bounding box.
                // Prefer the room bounding box; fall back to the room location point when no box exists.
                bool xyOverlap;
                if (hostBBox != null)
                {
                    xyOverlap = SpatialHelpers.XYOverlap(cBox, hostBBox);
                }
                else if (hostLocationPoint != null)
                {
                    xyOverlap = SpatialHelpers.XYContains(cBox, hostLocationPoint);
                }
                else
                {
                    xyOverlap = false;
                }

                if (!xyOverlap) continue;

                // Z overlap: the ceiling's vertical extent must intersect the room's vertical band.
                // Tightened from +5.0 ft to +0.5 ft upward slop: a ceiling that is many feet
                // above the room's nominal top is the floor of the level above (or unrelated),
                // not a candidate for this room. The room DTO and the BruteForce
                // SelectPrimaryCeiling re-sort again, so this is a coarse pre-filter.
                if (cBox.Min.Z <= roomTopZ + 0.5 && cBox.Max.Z >= roomBottomZ - 0.5)
                {
                    CeilingData clone = CloneCeilingData(cItem.Dto);
                    clone.IsRoomDirectCeiling = true;
                    matches.Add(clone);
                }
            }

            if (matches.Count == 0) return matches;

            // Sort: level-matched ceilings first (this room's own ceiling, not the slab of
            // the level above / below), then by BottomElevationFt descending so the room's
            // own (highest) ceiling is the FIRST element of the list — that becomes the
            // primary ceiling the DTO exposes to the calculation engine.
            bool hasRoomLevel = !string.IsNullOrEmpty(roomLevelId);
            matches.Sort((a, b) =>
            {
                int aLevel = (hasRoomLevel && a != null && string.Equals(a.LevelId, roomLevelId, System.StringComparison.OrdinalIgnoreCase)) ? 0 : 1;
                int bLevel = (hasRoomLevel && b != null && string.Equals(b.LevelId, roomLevelId, System.StringComparison.OrdinalIgnoreCase)) ? 0 : 1;
                if (aLevel != bLevel) return aLevel.CompareTo(bLevel);
                double aBottom = a.BottomElevationFt ?? double.NegativeInfinity;
                double bBottom = b.BottomElevationFt ?? double.NegativeInfinity;
                return bBottom.CompareTo(aBottom); // descending
            });

            return matches;
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
                IsRoomDirectCeiling = src.IsRoomDirectCeiling
            };
        }
    }
}
