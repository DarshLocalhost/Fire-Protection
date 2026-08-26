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
        /// </summary>
        private static void AssociateObstaclesToRooms(List<RoomData> rooms, List<ObstacleData> obstacles)
        {
            if (rooms == null || obstacles == null) return;

            foreach (ObstacleData obstacle in obstacles)
            {
                if (obstacle.BoundingBox == null) continue;

                foreach (RoomData room in rooms)
                {
                    if (room.BoundingBox == null) continue;

                    bool xy = SpatialHelpers.XYOverlap(obstacle.BoundingBox, room.BoundingBox);
                    bool z = SpatialHelpers.ZOverlap(obstacle.BoundingBox, room.BoundingBox);
                    if (xy && z)
                    {
                        if (!obstacle.AssociatedRoomIds.Contains(room.RoomId))
                            obstacle.AssociatedRoomIds.Add(room.RoomId);
                        if (!room.AssociatedObstacleIds.Contains(obstacle.ElementId))
                            room.AssociatedObstacleIds.Add(obstacle.ElementId);
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
