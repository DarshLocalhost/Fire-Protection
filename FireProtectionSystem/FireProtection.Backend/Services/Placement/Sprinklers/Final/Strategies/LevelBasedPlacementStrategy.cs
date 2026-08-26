using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    /// <summary>
    /// OneLevelBased families (master prompt §13). These are genuinely level-hosted, so the level overload
    /// <c>NewFamilyInstance(xyz, symbol, level, NonStructural)</c> is the CORRECT placement — it is used
    /// here only because the family's proven <c>FamilyPlacementType</c> is OneLevelBased, never as a
    /// fallback for a hosted family (hard rules 5, 6).
    /// </summary>
    internal sealed class LevelBasedPlacementStrategy : IFamilyPlacementStrategy
    {
        public string Name => "LevelBased";

        public bool CanHandle(string familyPlacementType) =>
            string.Equals(familyPlacementType, "OneLevelBased", StringComparison.OrdinalIgnoreCase);

        public PlacementOutcome Place(PlacementContext context)
        {
            try
            {
                FamilyInstance instance = context.Document.Create.NewFamilyInstance(
                    context.RequestedPoint, context.Symbol, context.Level, StructuralType.NonStructural);

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
