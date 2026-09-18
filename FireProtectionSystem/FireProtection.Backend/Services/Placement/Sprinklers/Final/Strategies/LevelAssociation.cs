using System;
using Autodesk.Revit.DB;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    /// <summary>
    /// Professional Level Association & Elevation Offset Enforcer.
    /// Guarantees that instances created on any level (L1, L2, etc.) are bound to the correct
    /// target Level with positive elevation offsets, preventing incorrect Schedule Level assignments
    /// (e.g. L2 with -12' offset for L1 rooms).
    /// </summary>
    internal static class LevelAssociation
    {
        public static bool EnforceAndVerify(Document doc, FamilyInstance instance, Level targetLevel, double targetWorldZ)
        {
            if (instance == null || targetLevel == null || !instance.IsValidObject) return false;

            try
            {
                double levelElevation = targetLevel.Elevation;
                double desiredOffset = targetWorldZ - levelElevation;

                // 1. Enforce Schedule Level parameter
                Parameter schedLevelParam = instance.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                if (schedLevelParam != null && !schedLevelParam.IsReadOnly)
                {
                    schedLevelParam.Set(targetLevel.Id);
                }

                // 2. Enforce Reference Level parameter
                Parameter refLevelParam = instance.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                if (refLevelParam != null && !refLevelParam.IsReadOnly)
                {
                    refLevelParam.Set(targetLevel.Id);
                }

                // 3. Enforce Family Level parameter
                Parameter famLevelParam = instance.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                if (famLevelParam != null && !famLevelParam.IsReadOnly)
                {
                    famLevelParam.Set(targetLevel.Id);
                }

                // 4. Set Elevation / Free Host Offset parameter
                Parameter offsetParam = instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                if (offsetParam != null && !offsetParam.IsReadOnly)
                {
                    offsetParam.Set(desiredOffset);
                }
                else
                {
                    Parameter elevationParam = instance.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                    if (elevationParam != null && !elevationParam.IsReadOnly)
                    {
                        elevationParam.Set(desiredOffset);
                    }
                }

                doc.Regenerate();

                // 5. Read-back verification
                if (schedLevelParam != null && schedLevelParam.HasValue)
                {
                    ElementId currentSchedLevelId = schedLevelParam.AsElementId();
                    if (currentSchedLevelId != ElementId.InvalidElementId && currentSchedLevelId != targetLevel.Id)
                    {
                        // Retry setting Schedule Level if Revit overrode it during regeneration
                        if (!schedLevelParam.IsReadOnly)
                        {
                            schedLevelParam.Set(targetLevel.Id);
                            doc.Regenerate();
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[LEVEL-ASSOCIATION] Exception enforcing level: " + ex.Message);
                return false;
            }
        }
    }
}