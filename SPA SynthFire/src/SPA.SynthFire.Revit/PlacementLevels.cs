using Autodesk.Revit.DB;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Forces a placed instance onto the correct floor at the correct height. Ported
/// from the proven FireProtectionSystem approach: after ANY NewFamilyInstance
/// (created with the ABSOLUTE world Z), set every writable level parameter to the
/// target level, set the offset-from-level so the world Z is exact, regenerate,
/// then re-read and retry once if Revit overrode the schedule level during regen.
/// </summary>
public static class PlacementLevels
{
    /// <summary>
    /// For FACE-HOSTED instances (pendent/detector on a ceiling face, sidewall on a
    /// wall face): the face already fixes the world Z, so we set ONLY the level
    /// parameters for correct floor-plan/schedule association — never the offset,
    /// which for a hosted instance is relative to the host face and would shove the
    /// device off its ceiling/wall.
    /// </summary>
    public static void SetLevelOnly(Document doc, FamilyInstance instance, Level targetLevel)
    {
        SetLevelParams(instance, targetLevel);
        doc.Regenerate();
        ReassertScheduleLevel(doc, instance, targetLevel);
    }

    /// <summary>
    /// For LEVEL-BASED / free instances (upright, unhosted): sets the level params
    /// AND the offset-from-level so the absolute world Z is exact.
    /// </summary>
    /// <param name="targetWorldZ">Absolute project Z (feet) the device should sit at.</param>
    public static void Apply(Document doc, FamilyInstance instance, Level targetLevel, double targetWorldZ)
    {
        SetLevelParams(instance, targetLevel);

        double desiredOffset = targetWorldZ - targetLevel.Elevation;
        var offset = instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
        if (offset is { IsReadOnly: false })
        {
            offset.Set(desiredOffset);
        }
        else
        {
            var elevation = instance.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
            if (elevation is { IsReadOnly: false }) elevation.Set(desiredOffset);
        }

        doc.Regenerate();
        ReassertScheduleLevel(doc, instance, targetLevel);
    }

    private static void SetLevelParams(FamilyInstance instance, Level targetLevel)
    {
        foreach (var bip in new[]
                 {
                     BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
                     BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
                     BuiltInParameter.FAMILY_LEVEL_PARAM
                 })
        {
            var p = instance.get_Parameter(bip);
            if (p is { IsReadOnly: false, StorageType: StorageType.ElementId })
                p.Set(targetLevel.Id);
        }
    }

    private static void ReassertScheduleLevel(Document doc, FamilyInstance instance, Level targetLevel)
    {
        var schedLevel = instance.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
        if (schedLevel is { IsReadOnly: false } && schedLevel.HasValue)
        {
            ElementId current = schedLevel.AsElementId();
            if (current != ElementId.InvalidElementId && current != targetLevel.Id)
            {
                schedLevel.Set(targetLevel.Id);
                doc.Regenerate();
            }
        }
    }
}
