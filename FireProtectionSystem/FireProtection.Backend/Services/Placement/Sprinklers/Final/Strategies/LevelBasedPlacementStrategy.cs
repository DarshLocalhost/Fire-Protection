using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    internal sealed class LevelBasedPlacementStrategy : IFamilyPlacementStrategy
    {
        public string Name => "LevelBased";

        public bool CanHandle(string familyPlacementType) =>
            string.Equals(familyPlacementType, "OneLevelBased", StringComparison.OrdinalIgnoreCase);

        public PlacementOutcome Place(PlacementContext context)
        {
            try
            {
                // Ensure symbol is active before placement
                if (!context.Symbol.IsActive)
                {
                    context.Symbol.Activate();
                    context.Document.Regenerate();
                }

                FamilyInstance instance = context.Document.Create.NewFamilyInstance(
                    context.RequestedPoint, context.Symbol, context.Level, StructuralType.NonStructural);

                // Ensure the elevation parameter is explicitly aligned to the requested point elevation
                Parameter elevationParam = instance.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM)
                                        ?? instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);

                if (elevationParam != null && !elevationParam.IsReadOnly)
                {
                    double levelElevation = context.Level?.Elevation ?? 0.0;
                    double offsetFromLevel = context.RequestedPoint.Z - levelElevation;
                    elevationParam.Set(offsetFromLevel);
                }

                return PlacementOutcome.CreatedInstance(instance, "LevelBased", "none");
            }
            catch (Exception ex)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.RevitCreationFailed,
                    "Revit rejected level-based placement: " + ex.Message,
                    hostingStrategy: "LevelBased");
            }
        }
    }
}