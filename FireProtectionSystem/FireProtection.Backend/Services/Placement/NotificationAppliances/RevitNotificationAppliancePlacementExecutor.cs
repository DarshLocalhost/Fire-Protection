using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FireProtection.Backend.Services.Placement.Devices;
using FireProtection.Backend.Services.Placement.NotificationAppliances.Final.BruteForce;
using FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Services;

namespace FireProtection.Backend.Services.Placement.NotificationAppliances
{
    /// <summary>
    /// Notification-appliance (strobe / speaker / speaker-strobe) placement executor. A thin wrapper over the
    /// shared <see cref="FireAlarmDevicePlacementCore"/>: it supplies the notification calculation step
    /// (input snapshot + <see cref="Nfpa72NotificationApplianceRules"/>) and the device labels. Because the
    /// notification rules are PROVISIONAL and catalog-independent, the run and its report are flagged
    /// "engineering review required"; all Revit element creation and hosting live in the core.
    /// </summary>
    public class RevitNotificationAppliancePlacementExecutor : IDevicePlacementExecutor
    {
        private readonly FireAlarmDevicePlacementCore _core;

        /// <param name="catalogAccessor">Accepted for construction parity with the other device executors;
        /// the provisional notification rules do not read the catalog, so it is unused today.</param>
        public RevitNotificationAppliancePlacementExecutor(Document document, Func<ICatalog> catalogAccessor = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var familySource = new RevitNotificationApplianceFamilySource(document);
            _core = new FireAlarmDevicePlacementCore(
                document,
                "notification appliance",
                "Fire Protection: Place Notification Appliances",
                items => SmokeDetectorCalculationService.Calculate(
                    NotificationAppliancePlacementInputBuilder.BuildSnapshot(
                        items.ToList(),
                        familySource),
                    new Nfpa72NotificationApplianceRules(),
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
