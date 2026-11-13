using System;
using System.Collections.Generic;
using FireProtection.UI.Services;

namespace FireProtection.Backend.Services.Catalog
{
    /// <summary>
    /// Alarm-device attribute lookups that span BOTH catalog sheets.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// Smoke detectors and notification appliances share the single Revit category
    /// <c>OST_FireAlarmDevices</c>, and the product decision is that BOTH tabs present the SAME
    /// family/type list rather than trying to split families by name. The workbook, however, stores
    /// them in two separate sheets (<c>SmokeDetectors</c> and <c>NotificationAppliances</c>) and
    /// every sheet-scoped lookup only searches its own sheet.
    ///
    /// The consequence if that is left alone: a strobe shown on the Smoke Detectors tab is
    /// catalogued under <c>NotificationAppliances</c>, so the smoke lookup finds nothing, the
    /// derived read-only attributes go null, and the UI re-exposes editable pickers bound to an
    /// EMPTY list — which is worse than the current behaviour where they are read-only text.
    ///
    /// Searching both sheets keeps the shared list coherent on both tabs.
    ///
    /// Attributes the other sheet does not carry are returned as null / zero rather than guessed.
    /// A zero Candela or dBA is already understood downstream as "no rating known" and falls back
    /// to the unrated floor, so it is honest rather than misleading.
    /// </summary>
    public static class DeviceCatalogOverlay
    {
        /// <summary>
        /// Smoke-detector attributes for a family, searching the SmokeDetectors sheet first and
        /// then NotificationAppliances for families catalogued only there.
        /// </summary>
        public static IReadOnlyList<SmokeDetectorCatalogEntry> SmokeEntries(
            ICatalog overlay, string familyName)
        {
            var result = new List<SmokeDetectorCatalogEntry>();
            if (overlay == null || string.IsNullOrEmpty(familyName)) return result;

            IReadOnlyList<SmokeDetectorCatalogEntry> primary = SafeSmoke(overlay, familyName);
            if (primary != null) result.AddRange(primary);

            // Only pay for the cross-sheet scan when the own sheet found nothing.
            if (result.Count == 0)
            {
                IReadOnlyList<NotificationApplianceCatalogEntry> other = SafeAppliance(overlay, familyName);
                if (other == null) return result;

                foreach (NotificationApplianceCatalogEntry a in other)
                {
                    if (a == null) continue;
                    result.Add(new SmokeDetectorCatalogEntry
                    {
                        FamilyName = a.FamilyName,
                        TypeName = a.TypeName,
                        DetectorType = null,   // not carried by the appliance sheet
                        Mount = null,          // not carried by the appliance sheet
                        CeilingSlope = null    // not carried by the appliance sheet
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Notification-appliance attributes for a family, searching the NotificationAppliances
        /// sheet first and then SmokeDetectors for families catalogued only there.
        /// </summary>
        public static IReadOnlyList<NotificationApplianceCatalogEntry> ApplianceEntries(
            ICatalog overlay, string familyName)
        {
            var result = new List<NotificationApplianceCatalogEntry>();
            if (overlay == null || string.IsNullOrEmpty(familyName)) return result;

            IReadOnlyList<NotificationApplianceCatalogEntry> primary = SafeAppliance(overlay, familyName);
            if (primary != null) result.AddRange(primary);

            if (result.Count == 0)
            {
                IReadOnlyList<SmokeDetectorCatalogEntry> other = SafeSmoke(overlay, familyName);
                if (other == null) return result;

                foreach (SmokeDetectorCatalogEntry s in other)
                {
                    if (s == null) continue;
                    result.Add(new NotificationApplianceCatalogEntry
                    {
                        FamilyName = s.FamilyName,
                        TypeName = s.TypeName,
                        ApplianceType = null,   // not carried by the detector sheet
                        Candela = 0,            // 0 == "no rating known", already handled downstream
                        NotificationDba = 0
                    });
                }
            }
            return result;
        }

        private static IReadOnlyList<SmokeDetectorCatalogEntry> SafeSmoke(ICatalog overlay, string familyName)
        {
            try { return overlay.GetSmokeDetectorEntriesForFamily(familyName); }
            catch (Exception ex)
            {
                FireProtection.UI.Services.FireProtectionLog.Warn(
                    "Smoke entry lookup failed for '" + familyName + "': " + ex.Message);
                return null;
            }
        }

        private static IReadOnlyList<NotificationApplianceCatalogEntry> SafeAppliance(
            ICatalog overlay, string familyName)
        {
            try { return overlay.GetNotificationAppliancesForFamily(familyName); }
            catch (Exception ex)
            {
                FireProtection.UI.Services.FireProtectionLog.Warn(
                    "Appliance entry lookup failed for '" + familyName + "': " + ex.Message);
                return null;
            }
        }
    }
}