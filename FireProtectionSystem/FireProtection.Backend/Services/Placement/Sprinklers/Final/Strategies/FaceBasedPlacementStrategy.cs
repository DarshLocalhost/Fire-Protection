using System;
using Autodesk.Revit.DB;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    internal sealed class FaceBasedPlacementStrategy : IFamilyPlacementStrategy
    {
        public string Name => "FaceBased";

        public bool CanHandle(string familyPlacementType) =>
            string.Equals(familyPlacementType, "FaceBased", StringComparison.OrdinalIgnoreCase);

        public PlacementOutcome Place(PlacementContext context)
        {
            Document doc = context.Document;

            if (!context.Symbol.IsActive)
            {
                context.Symbol.Activate();
                doc.Regenerate();
            }

            CeilingHostLookup host = context.CeilingHostResolver.FindCeilingHost(
                doc, context.RequestedPoint, context.Level);

            if (host.HostFace == null)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.RequiredHostUnavailable,
                    "A FaceBased family requires a host face; no host or linked ceiling face was found at " +
                    "the requested point.",
                    hostingStrategy: "FaceBased/RequiredHost",
                    ceilingSource: host.Source,
                    linkInstanceName: host.LinkInstanceName,
                    hostCeilingElementId: host.CeilingElementId);
            }

            try
            {
                XYZ refDir = ComputeInPlaneReferenceDirection(doc, host.HostFace);

                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    host.HostFace, context.RequestedPoint, refDir, context.Symbol);

                // FIX: Enforce level association and offset
                LevelAssociation.EnforceAndVerify(doc, instance, context.Level, context.RequestedPoint.Z);

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

        private static XYZ ComputeInPlaneReferenceDirection(Document doc, Reference faceRef)
        {
            try
            {
                GeometryObject geomObj = doc.GetElement(faceRef)?.GetGeometryObjectFromReference(faceRef);
                if (geomObj is PlanarFace planarFace)
                {
                    XYZ normal = planarFace.FaceNormal;
                    XYZ temp = Math.Abs(normal.DotProduct(XYZ.BasisZ)) < 0.8 ? XYZ.BasisZ : XYZ.BasisY;
                    return normal.CrossProduct(temp).Normalize();
                }
            }
            catch { }
            return XYZ.BasisX;
        }
    }
}