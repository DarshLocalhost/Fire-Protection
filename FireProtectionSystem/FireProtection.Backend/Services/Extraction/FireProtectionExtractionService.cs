using Autodesk.Revit.DB;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Services.Model;
using System;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Extraction
{
    public class FireProtectionExtractionService : IFireProtectionExtractionService
    {
        private readonly LevelExtractor _levelExtractor;
        private readonly RoomExtractor _roomExtractor;
        private readonly CeilingExtractor _ceilingExtractor;
        private readonly ObstacleExtractor _obstacleExtractor;
        private readonly ExistingSprinklerExtractor _existingSprinklerExtractor;
        private readonly ModelExtractionValidator _validator;
        private readonly JsonSnapshotExporter _exporter;

        public FireProtectionExtractionService()
        {
            _levelExtractor = new LevelExtractor();
            _roomExtractor = new RoomExtractor();
            _ceilingExtractor = new CeilingExtractor();
            _obstacleExtractor = new ObstacleExtractor();
            _existingSprinklerExtractor = new ExistingSprinklerExtractor();
            _validator = new ModelExtractionValidator();
            _exporter = new JsonSnapshotExporter();
        }

        public ModelSnapshot ExtractModelSnapshot(Document hostDocument)
        {
            if (hostDocument == null)
                throw new ArgumentNullException(nameof(hostDocument));

            List<ExtractionIssue> issues = new List<ExtractionIssue>();

            // 1. Initialize model context & discover links
            RevitModelContext context = new RevitModelContext(hostDocument);

            // 2. Extract Levels (host + links in host coordinates)
            List<LevelData> levels = _levelExtractor.ExtractLevels(context, issues);

            // 3. Extract Ceilings (3D solid recursive geometry + slope classification)
            List<CeilingExtractor.ExtractedCeilingItem> ceilingItems =
                _ceilingExtractor.CollectAllCeilings(context, issues);

            // 4. Extract Rooms, boundary geometry, hazard classifications, and associate ceilings
            List<RoomData> rooms = _roomExtractor.ExtractRooms(
                context,
                levels,
                ceilingItems,
                issues);

            // Count rooms per link for model structure info
            Dictionary<string, int> linkRoomCounts = new Dictionary<string, int>();
            foreach (RoomData r in rooms)
            {
                if (r.Source != null && r.Source.IsFromLink && !string.IsNullOrEmpty(r.Source.LinkInstanceId))
                {
                    if (!linkRoomCounts.ContainsKey(r.Source.LinkInstanceId))
                        linkRoomCounts[r.Source.LinkInstanceId] = 0;
                    linkRoomCounts[r.Source.LinkInstanceId]++;
                }
            }

            // 5. Extract Obstacles (columns, beams, walls, MEP elements)
            List<ObstacleData> obstacles = _obstacleExtractor.ExtractObstacles(context, issues);

            // 6. Extract Existing Sprinklers from host model (and linked models)
            List<ExistingSprinklerData> existingSprinklers =
                _existingSprinklerExtractor.ExtractExistingSprinklers(context, levels, issues);

            // 7. Retain all extracted ceilings at the global level so none are silently lost,
            //     then associate obstacles and sprinklers to their containing rooms.
            List<CeilingData> allCeilings = ceilingItems.ConvertAll(c => c.Dto);
            AssociateObstaclesToRooms(rooms, obstacles);
            AssociateSprinklersToRooms(rooms, existingSprinklers);

            // 8. Build root ModelSnapshot DTO
            ModelSnapshot snapshot = new ModelSnapshot
            {
                SchemaVersion = "1.0",
                Units = new UnitsInfo
                {
                    Length = "ft",
                    Area = "sq_ft",
                    Volume = "cu_ft",
                    Angle = "degrees"
                },
                CoordinateSystem = new CoordinateSystemInfo
                {
                    Canonical = "host_mep_model",
                    LengthUnit = "feet"
                },
                Project = new Models.DTOs.ProjectInfo
                {
                    Name = hostDocument.Title,
                    Standard = "NFPA13-2022",
                    TimestampUtc = DateTime.UtcNow.ToString("o")
                },
                Model = context.GetModelStructureInfo(linkRoomCounts),
                Levels = levels,
                Rooms = rooms,
                Ceilings = allCeilings,
                ExistingSprinklers = existingSprinklers,
                Obstacles = obstacles
            };

            // 9. Validate extraction & compute statistics                                      
            snapshot.Summary = _validator.Validate(snapshot, issues);

            // Populate issues list
            foreach (ExtractionIssue issue in issues)
            {
                if (issue.Severity == ExtractionIssueSeverity.Error.ToString())
                {
                    snapshot.Errors.Add(issue);
                }
                else
                {
                    snapshot.Warnings.Add(issue);
                }
            }

            return snapshot;
        }

        public ModelSnapshot ExtractLightweightSnapshot(Document hostDocument)
        {
            if (hostDocument == null)
                throw new ArgumentNullException(nameof(hostDocument));

            List<ExtractionIssue> issues = new List<ExtractionIssue>();

            // 1. Initialize model context & discover links
            RevitModelContext context = new RevitModelContext(hostDocument);

            // 2. Extract Levels (host + links in host coordinates)
            List<LevelData> levels = _levelExtractor.ExtractLevels(context, issues);

            // 3. Extract Ceilings (3D solid recursive geometry + slope classification)
            List<CeilingExtractor.ExtractedCeilingItem> ceilingItems =
                _ceilingExtractor.CollectAllCeilings(context, issues);

            // 4. Extract Rooms, boundary geometry, hazard classifications, and associate ceilings
            List<RoomData> rooms = _roomExtractor.ExtractRooms(
                context,
                levels,
                ceilingItems,
                issues);

            // Count rooms per link for model structure info
            Dictionary<string, int> linkRoomCounts = new Dictionary<string, int>();
            foreach (RoomData r in rooms)
            {
                if (r.Source != null && r.Source.IsFromLink && !string.IsNullOrEmpty(r.Source.LinkInstanceId))
                {
                    if (!linkRoomCounts.ContainsKey(r.Source.LinkInstanceId))
                        linkRoomCounts[r.Source.LinkInstanceId] = 0;
                    linkRoomCounts[r.Source.LinkInstanceId]++;
                }
            }

            // 5. Extract Existing Sprinklers from host model (and linked models)
            List<ExistingSprinklerData> existingSprinklers =
                _existingSprinklerExtractor.ExtractExistingSprinklers(context, levels, issues);

            List<CeilingData> allCeilings = ceilingItems.ConvertAll(c => c.Dto);
            AssociateSprinklersToRooms(rooms, existingSprinklers);

            // 6. Build root ModelSnapshot DTO with deferred obstacles for instant UI launch
            ModelSnapshot snapshot = new ModelSnapshot
            {
                SchemaVersion = "1.0",
                Units = new UnitsInfo
                {
                    Length = "ft",
                    Area = "sq_ft",
                    Volume = "cu_ft",
                    Angle = "degrees"
                },
                CoordinateSystem = new CoordinateSystemInfo
                {
                    Canonical = "host_mep_model",
                    LengthUnit = "feet"
                },
                Project = new Models.DTOs.ProjectInfo
                {
                    Name = hostDocument.Title,
                    Standard = "NFPA13-2022",
                    TimestampUtc = DateTime.UtcNow.ToString("o")
                },
                Model = context.GetModelStructureInfo(linkRoomCounts),
                Levels = levels,
                Rooms = rooms,
                Ceilings = allCeilings,
                ExistingSprinklers = existingSprinklers,
                Obstacles = new List<ObstacleData>() // Deferred until placement calculation
            };

            snapshot.Summary = _validator.Validate(snapshot, issues);

            foreach (ExtractionIssue issue in issues)
            {
                if (issue.Severity == ExtractionIssueSeverity.Error.ToString())
                    snapshot.Errors.Add(issue);
                else
                    snapshot.Warnings.Add(issue);
            }

            return snapshot;
        }

        public List<ObstacleData> ExtractObstacles(Document hostDocument, List<RoomData> rooms)
        {
            if (hostDocument == null) return new List<ObstacleData>();
            List<ExtractionIssue> issues = new List<ExtractionIssue>();
            RevitModelContext context = new RevitModelContext(hostDocument);
            List<ObstacleData> obstacles = _obstacleExtractor.ExtractObstacles(context, issues);
            AssociateObstaclesToRooms(rooms, obstacles);
            return obstacles;
        }

        public (ModelSnapshot Snapshot, string ExportPath) ExtractAndExport(
            Document hostDocument,
            string exportFilePath = null)
        {
            ModelSnapshot snapshot = ExtractModelSnapshot(hostDocument);
            string exportedPath = _exporter.ExportToFile(snapshot, exportFilePath);
            return (snapshot, exportedPath);
        }

        /// <summary>
        /// Associates each obstacle with the rooms it intersects in the XY plane and vertically.
        /// Populates both the obstacle's <see cref="ObstacleData.AssociatedRoomIds"/> and the
        /// room's <see cref="RoomData.AssociatedObstacleIds"/> for downstream placement logic.
        /// Optimized with HashSet lookups and vertical screening for large model scale.
        /// </summary>
        private static void AssociateObstaclesToRooms(List<RoomData> rooms, List<ObstacleData> obstacles)
        {
            if (rooms == null || obstacles == null || rooms.Count == 0 || obstacles.Count == 0) return;

            // Pre-index rooms by vertical span for rapid filtering
            var validRooms = new List<RoomData>(rooms.Count);
            var roomObstacleSets = new Dictionary<string, HashSet<string>>(rooms.Count);
            foreach (RoomData r in rooms)
            {
                if (r?.BoundingBox != null && !string.IsNullOrEmpty(r.RoomId))
                {
                    validRooms.Add(r);
                    if (r.AssociatedObstacleIds == null) r.AssociatedObstacleIds = new List<string>();
                    roomObstacleSets[r.RoomId] = new HashSet<string>(r.AssociatedObstacleIds);
                }
            }

            foreach (ObstacleData obstacle in obstacles)
            {
                if (obstacle?.BoundingBox == null) continue;
                if (obstacle.AssociatedRoomIds == null) obstacle.AssociatedRoomIds = new List<string>();
                var obsRoomSet = new HashSet<string>(obstacle.AssociatedRoomIds);

                double oMinZ = obstacle.BoundingBox.Min.Z;
                double oMaxZ = obstacle.BoundingBox.Max.Z;

                for (int i = 0; i < validRooms.Count; i++)
                {
                    RoomData room = validRooms[i];
                    BoundingBox3DData rBox = room.BoundingBox;

                    // Vertical screening first (fast scalar compare)
                    if (rBox.Min.Z > oMaxZ || rBox.Max.Z < oMinZ) continue;

                    // Horizontal overlap check
                    if (SpatialHelpers.XYOverlap(obstacle.BoundingBox, rBox))
                    {
                        if (obsRoomSet.Add(room.RoomId))
                            obstacle.AssociatedRoomIds.Add(room.RoomId);

                        if (roomObstacleSets.TryGetValue(room.RoomId, out var rSet))
                        {
                            if (rSet.Add(obstacle.ElementId))
                                room.AssociatedObstacleIds.Add(obstacle.ElementId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Associates each existing sprinkler with the room that contains its location point.
        /// </summary>
        private static void AssociateSprinklersToRooms(List<RoomData> rooms, List<ExistingSprinklerData> sprinklers)
        {
            if (rooms == null || sprinklers == null) return;

            foreach (ExistingSprinklerData sprinkler in sprinklers)
            {
                if (sprinkler.Location == null) continue;

                foreach (RoomData room in rooms)
                {
                    if (SpatialHelpers.PointInRoomXY(sprinkler.Location, room))
                    {
                        sprinkler.RoomId = room.RoomId;
                        sprinkler.RoomName = room.Name;
                        break;
                    }
                }
            }
        }
    }
}
