using System;
using Autodesk.Revit.DB;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies;

namespace FireProtection.Backend.Services.Placement.SmokeDetectors
{
    /// <summary>
    /// Smoke-detector-only catch-all placement strategy. The four shared strategies (FaceBased,
    /// WorkPlaneBased, OneLevelBased, WallSidewall) leave every other <c>FamilyPlacementType</c>
    /// unhandled — most importantly <c>OneLevelBasedHosted</c>, the usual template for hosted
    /// ceiling smoke-detector families — so those rooms placed zero devices ("No strategy supports
    /// placement type '…'.").
    ///
    /// This is wired in ONLY by <see cref="RevitSmokeDetectorPlacementExecutor"/> (opt-in constructor
    /// argument on the shared core) and is appended LAST in the strategy list, so it never preempts a
    /// real strategy for a type that already works and cannot change notification-appliance or
    /// sprinkler behaviour. It attempts, in order: a real ceiling/soffit host face, a work-plane
    /// (sketch plane) at the target Z, then a level-based instance — returning the first that Revit
    /// accepts. Placement remains provisional / review-required exactly like every other path.
    /// </summary>
    internal sealed class SmokeDetectorLastResortPlacementStrategy : IFamilyPlacementStrategy
    {
        public string Name => "SmokeLastResort";

        // Catch-all: only reached when no earlier strategy handled the placement type.
        public bool CanHandle(string familyPlacementType) => true;

        public PlacementOutcome Place(PlacementContext context)
        {
            Document doc = context.Document;
            XYZ xyz = context.RequestedPoint;

            if (!context.Symbol.IsActive)
            {
                context.Symbol.Activate();
                doc.Regenerate();
            }

            string tried = context.FamilyPlacementType ?? "?";

            // 1. Prefer a real ceiling/soffit face (required for genuinely hosted families).
            CeilingHostLookup host = context.CeilingHostResolver?.FindCeilingHost(doc, xyz, context.Level)
                                     ?? new CeilingHostLookup();
            if (host.HostFace != null)
            {
                try
                {
                    XYZ refDir = ComputeInPlaneReferenceDirection(doc, host.HostFace);
                    FamilyInstance faceInstance = doc.Create.NewFamilyInstance(
                        host.HostFace, xyz, refDir, context.Symbol);
                    LevelAssociation.EnforceAndVerify(doc, faceInstance, context.Level, xyz.Z);
                    return PlacementOutcome.CreatedInstance(
                        faceInstance, "LastResortFaceHost", host.Source, host.LinkInstanceName, host.CeilingElementId);
                }
                catch (Exception ex)
                {
                    tried += "; face-host rejected: " + ex.Message;
                }
            }

            // 2. Work-plane (sketch plane) at the target Z.
            try
            {
                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, xyz);
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                FamilyInstance planeInstance = doc.Create.NewFamilyInstance(
                    sketchPlane.GetPlaneReference(), xyz, XYZ.BasisX, context.Symbol);
                LevelAssociation.EnforceAndVerify(doc, planeInstance, context.Level, xyz.Z);
                return PlacementOutcome.CreatedInstance(planeInstance, "LastResortSketchPlane", host.Source);
            }
            catch (Exception ex)
            {
                tried += "; sketch-plane rejected: " + ex.Message;
            }

            // 3. Level-based, last resort.
            try
            {
                FamilyInstance levelInstance = doc.Create.NewFamilyInstance(
                    xyz, context.Symbol, context.Level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                LevelAssociation.EnforceAndVerify(doc, levelInstance, context.Level, xyz.Z);
                return PlacementOutcome.CreatedInstance(levelInstance, "LastResortLevelBased", host.Source);
            }
            catch (Exception ex)
            {
                tried += "; level-based rejected: " + ex.Message;
            }

            return PlacementOutcome.Fail(
                PlacementStatusCodes.UnsupportedFamilyPlacement,
                "Smoke detector could not be placed by any hosting method for placement type '" + tried + "'.",
                hostingStrategy: "SmokeLastResort",
                ceilingSource: host.Source,
                linkInstanceName: host.LinkInstanceName,
                hostCeilingElementId: host.CeilingElementId);
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
