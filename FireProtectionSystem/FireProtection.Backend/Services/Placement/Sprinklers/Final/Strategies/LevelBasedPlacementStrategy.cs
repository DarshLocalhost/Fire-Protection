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

                XYZ placePt = context.RequestedPoint;
                if (context.CeilingHostResolver != null)
                {
                    CeilingHostLookup host = context.CeilingHostResolver.FindCeilingHost(context.Document, context.RequestedPoint, context.Level);
                    if (host != null && host.ProjectedPointOnFace != null)
                    {
                        // Deflector drop: 1 inch (0.0833 ft) below the exact overhead ceiling/slab face
                        placePt = new XYZ(context.RequestedPoint.X, context.RequestedPoint.Y, host.ProjectedPointOnFace.Z - 0.0833);
                    }
                }

                FamilyInstance instance = context.Document.Create.NewFamilyInstance(
                    placePt, context.Symbol, context.Level, StructuralType.NonStructural);

                // FIX: Enforce level association and offset securely
                LevelAssociation.EnforceAndVerify(context.Document, instance, context.Level, placePt.Z);

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