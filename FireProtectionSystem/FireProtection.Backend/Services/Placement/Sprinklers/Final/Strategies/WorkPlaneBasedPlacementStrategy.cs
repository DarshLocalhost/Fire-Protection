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

            if (!context.Symbol.IsActive)
            {
                context.Symbol.Activate();
                doc.Regenerate();
            }

            CeilingHostLookup host = context.CeilingHostResolver.FindCeilingHost(doc, xyz, context.Level);

            if (host.HostFace != null)
            {
                try
                {
                    XYZ placeAt = host.ProjectedPointOnFace ?? xyz;
                    XYZ refDir = ComputeInPlaneReferenceDirection(doc, host.HostFace);
                    FamilyInstance faceInstance = doc.Create.NewFamilyInstance(
                        host.HostFace, placeAt, refDir, context.Symbol);

                    LevelAssociation.EnforceAndVerify(doc, faceInstance, context.Level, placeAt.Z);

                    // FIX: Force Revit to not shift the Z-elevation from the face
                    Parameter offsetParam = faceInstance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                    if (offsetParam != null && !offsetParam.IsReadOnly)
                    {
                        offsetParam.Set(0.0);
                    }

                    return PlacementOutcome.CreatedInstance(
                        faceInstance, "WorkPlaneCeilingFace", host.Source, host.LinkInstanceName, host.CeilingElementId, expectedLocation: placeAt);
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

            try
            {
                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, xyz);
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    sketchPlane.GetPlaneReference(), xyz, XYZ.BasisX, context.Symbol);

                LevelAssociation.EnforceAndVerify(doc, instance, context.Level, xyz.Z);

                return PlacementOutcome.CreatedInstance(
                    instance, "WorkPlaneSketchPlane", host.Source, expectedLocation: xyz);
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