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
                if (!context.Symbol.IsActive)
                {
                    context.Symbol.Activate();
                    context.Document.Regenerate();
                }

                FamilyInstance instance = context.Document.Create.NewFamilyInstance(
                    context.RequestedPoint, context.Symbol, context.Level, StructuralType.NonStructural);

                // FIX: Enforce level association and offset securely
                LevelAssociation.EnforceAndVerify(context.Document, instance, context.Level, context.RequestedPoint.Z);

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