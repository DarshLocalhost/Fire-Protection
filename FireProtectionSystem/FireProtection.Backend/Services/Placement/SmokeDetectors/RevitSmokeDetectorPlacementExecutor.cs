using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FireProtection.Backend.Models.Placement.SmokeDetectors.Final;
using FireProtection.Backend.Services.Placement.Devices;
using FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Services;

namespace FireProtection.Backend.Services.Placement.SmokeDetectors
{
    /// <summary>
    /// NFPA 72 (Chapter 17) smoke-detector placement executor. A thin wrapper over the shared
    /// <see cref="FireAlarmDevicePlacementCore"/>: it supplies the smoke-detector calculation step
    /// (input snapshot + <see cref="Nfpa72SmokeDetectorRules"/>) and the device labels; all Revit element
    /// creation, symbol/level resolution, existing-device policy and cancellation live in the core.
    /// </summary>
    public class RevitSmokeDetectorPlacementExecutor : IDevicePlacementExecutor
    {
        private readonly FireAlarmDevicePlacementCore _core;

        public RevitSmokeDetectorPlacementExecutor(Document document, Func<ICatalog> catalogAccessor = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            // One family source per run, not one per calculation call (it is stateless over the document).
            var familySource = new RevitSmokeDetectorFamilySource(document, catalogAccessor);
            _core = new FireAlarmDevicePlacementCore(
                document,
                "smoke detector",
                "Fire Protection: Place Smoke Detectors",
                items => SmokeDetectorCalculationService.Calculate(
                    SmokeDetectorPlacementInputBuilder.BuildSnapshot(items.ToList(), familySource),
                    new Nfpa72SmokeDetectorRules(),
                    BruteForceCalculationConfig.Default()));
        }

        public PlacementRunReport ExecutePlacement(
            IReadOnlyList<DeviceRoomInputItem> items,
            IPlacementProgress progress = null,
            ExistingDevicePolicy existingDevicePolicy = ExistingDevicePolicy.SkipRoom)
        {
            return _core.ExecutePlacement(items, progress, existingDevicePolicy);
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, PlacementEligibilityResult> EvaluateEligibility(
            IReadOnlyList<DeviceRoomInputItem> items)
        {
            return _core.EvaluateEligibility(items);
        }
    }
}
