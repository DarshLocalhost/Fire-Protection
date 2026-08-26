using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    /// <summary>
    /// FaceBased families (master prompt §13, §14). Requires a real host face (host ceiling, or a linked
    /// ceiling via <see cref="Reference.CreateLinkReference"/>). If no valid host face exists the placement
    /// FAILS with <see cref="PlacementStatusCodes.RequiredHostUnavailable"/> — a FaceBased family is never
    /// forced onto a Level, and no apparently-successful-but-hostless instance is created (hard rule 8).
    /// </summary>
    internal sealed class FaceBasedPlacementStrategy : IFamilyPlacementStrategy
    {
        public string Name => "FaceBased";

        public bool CanHandle(string familyPlacementType) =>
            string.Equals(familyPlacementType, "FaceBased", StringComparison.OrdinalIgnoreCase);

        public PlacementOutcome Place(PlacementContext context)
        {
            CeilingHostLookup host = context.CeilingHostResolver.FindCeilingHost(
                context.Document, context.RequestedPoint, context.Level);

            if (host.HostFace == null)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.RequiredHostUnavailable,
                    "A FaceBased family requires a host face; no host or linked ceiling face was found at " +
                    "the requested point. Placement refused rather than creating a hostless instance.",
                    hostingStrategy: "FaceBased/RequiredHost",
                    ceilingSource: host.Source,
                    linkInstanceName: host.LinkInstanceName,
                    hostCeilingElementId: host.CeilingElementId);
            }

            try
            {
                // Face-hosted placement. The point stays in HOST coordinates (hard rules 10, 11).
                // referenceDirection must lie in the host face (perpendicular to its normal): a ceiling
                // underside is horizontal, so BasisX is valid; (0,0,1) would be parallel to the vertical
                // face normal and Revit would reject it (zero-length projection onto the face).
                FamilyInstance instance = context.Document.Create.NewFamilyInstance(
                    host.HostFace, context.RequestedPoint, XYZ.BasisX, context.Symbol);

                return PlacementOutcome.CreatedInstance(
                    instance, "FaceBasedHost", host.Source, host.LinkInstanceName, host.CeilingElementId);
            }
            catch (Exception ex)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.RevitCreationFailed,
                    "Revit rejected face-based placement: " + ex.Message,
                    hostingStrategy: "FaceBasedHost",
                    ceilingSource: host.Source,
                    linkInstanceName: host.LinkInstanceName,
                    hostCeilingElementId: host.CeilingElementId);
            }
        }
    }
}
