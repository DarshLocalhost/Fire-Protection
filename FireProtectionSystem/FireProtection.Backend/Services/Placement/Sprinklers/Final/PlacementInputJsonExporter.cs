using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Models.Sprinklers.BruteForce;
using FireProtection.UI.Services;
using Newtonsoft.Json;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final
{
    public class PlacementInputJsonExporter : IPlacementInputExporter
    {
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            DateFormatString = "yyyy-MM-ddTHH:mm:ssZ"
        };

        private readonly IReadOnlyList<ObstacleData> _globalObstacles;
        private readonly IReadOnlyList<ExistingSprinklerData> _globalExistingSprinklers;
        private readonly DeviceContextResolver _resolver;

        public PlacementInputJsonExporter()
            : this(null, null, null)
        {
        }

        public PlacementInputJsonExporter(
            IReadOnlyList<ObstacleData> globalObstacles,
            IReadOnlyList<ExistingSprinklerData> globalExistingSprinklers)
            : this(globalObstacles, globalExistingSprinklers, null)
        {
        }

        /// <summary>
        /// Step 2 — the optional <paramref name="resolver"/> is the small
        /// Backend-internal <see cref="DeviceContextResolver"/> delegate produced
        /// by the Revit-aware boundary (the <c>RevitSprinklerFamilySource</c>).
        /// When <c>null</c> (the original Step 1 constructor), the builder leaves
        /// the per-row context at the Step 1 default — fully backward compatible.
        ///
        /// This delegate-only form keeps the <c>FireProtection.UI</c>-defined
        /// <c>ISprinklerFamilySource</c> interface untouched and avoids forcing
        /// the UI to declare Revit-aware resolution methods.
        /// </summary>
        public PlacementInputJsonExporter(
            IReadOnlyList<ObstacleData> globalObstacles,
            IReadOnlyList<ExistingSprinklerData> globalExistingSprinklers,
            DeviceContextResolver resolver)
        {
            _globalObstacles = globalObstacles;
            _globalExistingSprinklers = globalExistingSprinklers;
            _resolver = resolver;
        }

        public PlacementInputExportResult ExportInput(
            string projectName,
            string selectedFamilyName,
            string selectedTypeName,
            IReadOnlyList<PlacementRoomInputItem> selectedRooms)
        {
            try
            {
                List<PlacementRoomSelection> selections = BuildSelections(selectedRooms);

                PlacementInputSnapshot snapshot = PlacementInputBuilder.Build(
                    projectName,
                    selectedFamilyName,
                    selectedTypeName,
                    selections,
                    _resolver);

                string exportPath = Export(snapshot);

                return new PlacementInputExportResult
                {
                    Success = true,
                    RoomsCount = snapshot.TotalRoomsSelected,
                    TotalAreaSqFt = snapshot.TotalAreaSqFt,
                    FamilyName = selectedFamilyName,
                    TypeName = selectedTypeName,
                    ExportFilePath = exportPath
                };
            }
            catch (Exception ex)
            {
                return new PlacementInputExportResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Builds the per-room selection DTOs from the UI-supplied items, rehydrating the full room
        /// payload (ceilings, source) and re-associating global obstacles/existing sprinklers.
        /// Shared by <see cref="ExportInput"/> and <see cref="CalculateBruteForce"/>.
        /// </summary>/
        private List<PlacementRoomSelection> BuildSelections(IReadOnlyList<PlacementRoomInputItem> selectedRooms)
        {
            List<PlacementRoomSelection> selections = new List<PlacementRoomSelection>();

            if (selectedRooms == null) return selections;

            foreach (PlacementRoomInputItem item in selectedRooms)
            {
                if (item == null) continue;

                var selection = new PlacementRoomSelection
                {
                    LevelId = item.LevelId,
                    LevelName = item.LevelName,
                    LevelElevationFt = item.LevelElevationFt,
                    RoomId = item.RoomId,
                    RoomName = item.RoomName,
                    RoomNumber = item.RoomNumber,
                    AreaSqFt = item.AreaSqFt,
                    EffectiveHazardClass = item.EffectiveHazardClass,
                    CeilingHeightFt = item.CeilingHeightFt,
                    CeilingType = item.CeilingType,
                    Polygon = item.Polygon ?? new List<double[]>(),
                    SelectedSprinklerFamilyName = item.SelectedSprinklerFamilyName,
                    SelectedSprinklerTypeName = item.SelectedSprinklerTypeName,
                    OverrideMaxSpacingFt = item.OverrideMaxSpacingFt,
                    OverrideBoundaryClearanceFt = item.OverrideBoundaryClearanceFt
                };

                RoomData roomData = null;
                if (item.FullRoomJson != null)
                {
                    try
                    {
                        roomData = JsonConvert.DeserializeObject<RoomData>(
                            JsonConvert.SerializeObject(item.FullRoomJson, Formatting.None));
                        if (roomData != null)
                        {
                            selection.Ceilings = roomData.Ceilings;
                            selection.Source = roomData.Source;
                        }
                    }
                    catch
                    {
                        // Parse failure leaves fields null; builder falls back to empty collections.
                    }
                }

                List<string> associatedObstacleIds = roomData?.AssociatedObstacleIds;
                if (_globalObstacles != null && _globalObstacles.Count > 0)
                {
                    selection.Obstacles = _globalObstacles
                        .Where(o => (o.AssociatedRoomIds != null && o.AssociatedRoomIds.Contains(item.RoomId)) ||
                                    (associatedObstacleIds != null && associatedObstacleIds.Contains(o.ElementId)))
                        .ToList();
                }

                if (_globalExistingSprinklers != null && _globalExistingSprinklers.Count > 0)
                {
                    selection.ExistingSprinklers = _globalExistingSprinklers
                        .Where(s => string.Equals(s.RoomId, item.RoomId, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                selections.Add(selection);
            }

            return selections;
        }

        public BruteForceCalculationResult CalculateBruteForce(
            string projectName,
            string selectedFamilyName,
            string selectedTypeName,
            IReadOnlyList<PlacementRoomInputItem> selectedRooms)
        {
            List<PlacementRoomSelection> selections = BuildSelections(selectedRooms);

            PlacementInputSnapshot snapshot = PlacementInputBuilder.Build(
                projectName,
                selectedFamilyName,
                selectedTypeName,
                selections,
                _resolver);

            BruteForceCalculationConfig config = BruteForceCalculationConfig.Default();
            IHazardPlacementRules rules = new DefaultHazardPlacementRules();

            return BruteForceCalculationService.Calculate(snapshot, rules, config);
        }

        public string ExportPlacementResult(SprinklerPlacementResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            string resolvedPath = GetDefaultPlacementExportPath();

            string dir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonConvert.SerializeObject(result, SerializerSettings);
            File.WriteAllText(resolvedPath, json);

            return resolvedPath;
        }

        public static string GetDefaultPlacementExportPath()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"sprinkler_placement_result_{timestamp}.json";

            try
            {
                string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docsPath))
                {
                    string targetDir = Path.Combine(docsPath, "FireProtectionSystem", "Exports");
                    return Path.Combine(targetDir, fileName);
                }
            }
            catch
            {
                // Fallback to assembly directory
            }

            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                return Path.Combine(asmDir, fileName);
            }

            return Path.Combine(Path.GetTempPath(), fileName);
        }

        public string Export(PlacementInputSnapshot snapshot, string targetFilePath = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            string resolvedPath = targetFilePath;
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                resolvedPath = GetDefaultExportPath();
            }

            string dir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonConvert.SerializeObject(snapshot, SerializerSettings);
            File.WriteAllText(resolvedPath, json);

            return resolvedPath;
        }

        public static string GetDefaultExportPath()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"sprinkler_placement_input_{timestamp}.json";

            try
            {
                string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docsPath))
                {
                    string targetDir = Path.Combine(docsPath, "FireProtectionSystem", "Exports");
                    return Path.Combine(targetDir, fileName);
                }
            }
            catch
            {
                // Fallback to assembly directory
            }

            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                return Path.Combine(asmDir, fileName);
            }

            return Path.Combine(Path.GetTempPath(), fileName);
        }
    }
}
