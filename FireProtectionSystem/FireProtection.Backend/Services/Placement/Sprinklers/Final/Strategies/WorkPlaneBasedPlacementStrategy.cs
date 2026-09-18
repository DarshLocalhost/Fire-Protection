using System;
using Autodesk.Revit.DB;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    internal sealed class WorkPlaneBasedPlacementStrategy : IFamilyPlacementStrategy
    {
        public string Name => "WorkPlaneBased";

        public bool CanHandle(string familyPlacementType) =>
            string.Equals(familyPlacementType, "WorkPlaneBased", StringComparison.OrdinalIgnoreCase);

        public PlacementOutcome Place(PlacementContext context)
        {
            Document doc = context.Document;
            XYZ xyz = context.RequestedPoint;

            // Ensure symbol is active
            if (!context.Symbol.IsActive)
            {
                context.Symbol.Activate();
                doc.Regenerate();
            }

            // --- Attempt 1: Host on a ceiling face (host or linked). ---
            CeilingHostLookup host = context.CeilingHostResolver.FindCeilingHost(doc, xyz, context.Level);
            if (host.HostFace != null)
            {
                try
                {
                    XYZ refDir = ComputeInPlaneReferenceDirection(doc, host.HostFace);

                    FamilyInstance faceInstance = doc.Create.NewFamilyInstance(
                        host.HostFace, xyz, refDir, context.Symbol);

                    return PlacementOutcome.CreatedInstance(
                        faceInstance, "WorkPlaneCeilingFace", host.Source, host.LinkInstanceName, host.CeilingElementId);
                }
                catch (Exception ex)
                {
                    host = new CeilingHostLookup
                    {
                        HostFace = null,
                        Source = host.Source + " (face-host rejected: " + ex.Message + ")",
                        LinkInstanceName = host.LinkInstanceName,
                        CeilingElementId = host.CeilingElementId
                    };
                }
            }

            // --- Attempt 2: Place on a SketchPlane through requested world XYZ (honors Z). ---
            try
            {
                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, xyz);
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    sketchPlane.GetPlaneReference(), xyz, XYZ.BasisX, context.Symbol);

                return PlacementOutcome.CreatedInstance(
                    instance, "WorkPlaneSketchPlane", host.Source);
            }
            catch (Exception ex)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.WorkPlaneUnavailable,
                    "WorkPlaneBased placement failed: no usable ceiling face and the SketchPlane work-plane " +
                    "overload was rejected by Revit (" + ex.Message + ").",
                    hostingStrategy: "WorkPlaneSketchPlane",
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