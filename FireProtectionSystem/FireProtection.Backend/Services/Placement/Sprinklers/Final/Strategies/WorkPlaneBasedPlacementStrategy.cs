using System;
using Autodesk.Revit.DB;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    /// <summary>
    /// WorkPlaneBased families (master prompt §13, §15; supersedes Decision 010 — see Decision 011).
    ///
    /// "WorkPlaneBased" means the family is hosted on a WORK PLANE. Two valid work planes, tried in order:
    ///   1. a ceiling face (host or linked) at the requested point — best for a pendant (underside face);
    ///   2. a <see cref="SketchPlane"/> through the requested world XYZ — a valid work plane that preserves
    ///      the full world coordinate INCLUDING Z.
    ///
    /// It NEVER falls back to the Level overload <c>NewFamilyInstance(xyz, symbol, level, NonStructural)</c>.
    /// That overload discards the candidate position for this hosted family and was runtime-proven to place
    /// the instance at the project origin (0,0,0) — see SPRINKLER_ACTUAL_Z_DIAGNOSTIC.md. Using the level
    /// overload here would violate hard rules 5, 6, 8 and 19 and master prompt §13/§15.
    ///
    /// If neither work plane can be used the placement FAILS with a structured code — correctness over
    /// count (hard rule 20).
    /// </summary>
    internal sealed class WorkPlaneBasedPlacementStrategy : IFamilyPlacementStrategy
    {
        public string Name => "WorkPlaneBased";

        public bool CanHandle(string familyPlacementType) =>
            string.Equals(familyPlacementType, "WorkPlaneBased", StringComparison.OrdinalIgnoreCase);

        public PlacementOutcome Place(PlacementContext context)
        {
            Document doc = context.Document;
            XYZ xyz = context.RequestedPoint;

            // --- Attempt 1: host on a ceiling face (host or linked). ---
            CeilingHostLookup host = context.CeilingHostResolver.FindCeilingHost(doc, xyz, context.Level);
            if (host.HostFace != null)
            {
                try
                {
                    // referenceDirection must LIE IN the host face, i.e. be perpendicular to its normal.
                    // A ceiling underside is horizontal (normal ~ vertical), so a horizontal direction
                    // (BasisX) is valid; (0,0,1) would be parallel to the normal and rejected by Revit.
                    FamilyInstance faceInstance = doc.Create.NewFamilyInstance(
                        host.HostFace, xyz, XYZ.BasisX, context.Symbol);

                    return PlacementOutcome.CreatedInstance(
                        faceInstance, "WorkPlaneCeilingFace", host.Source, host.LinkInstanceName, host.CeilingElementId);
                }
                catch (Exception ex)
                {
                    // Fall through to the SketchPlane work plane rather than failing outright — a face-host
                    // rejection (common with linked faces on some API builds) should not lose the placement,
                    // but we DO record why we fell through.
                    host = new CeilingHostLookup
                    {
                        HostFace = null,
                        Source = host.Source + " (face-host rejected: " + ex.Message + ")",
                        LinkInstanceName = host.LinkInstanceName,
                        CeilingElementId = host.CeilingElementId
                    };
                }
            }

            // --- Attempt 2: place on a SketchPlane through the requested world XYZ (honors Z). ---
            try
            {
                // A horizontal work plane at the requested elevation, positioned at the requested point.
                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, xyz);
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                // There is no NewFamilyInstance(SketchPlane, ...) overload; a work-plane-based family is
                // placed on the plane's Reference via the reference overload. The plane passes through xyz
                // (its origin), so the location projects back onto itself and the full world Z is preserved.
                // referenceDirection (BasisX) lies in the horizontal plane (perpendicular to BasisZ).
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
                    "overload was rejected by Revit (" + ex.Message + "). The Level overload is deliberately " +
                    "NOT used for a WorkPlaneBased family, as it discards the requested position.",
                    hostingStrategy: "WorkPlaneSketchPlane",
                    ceilingSource: host.Source,
                    linkInstanceName: host.LinkInstanceName,
                    hostCeilingElementId: host.CeilingElementId);
            }
        }
    }
}
