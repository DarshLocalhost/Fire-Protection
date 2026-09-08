using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    /// <summary>
    /// WallSidewall placement strategy (master prompt §13, §15; Decision 011 extension).
    ///
    /// Sidewall sprinklers are hosted on a vertical wall face. The calculation layer
    /// produces a <see cref="PlacementContext.WallEdgeIndex"/> that identifies which
    /// room-b polygon edge the candidate was generated from. This strategy finds the
    /// wall whose face aligns with that edge and places the family instance on it.
    ///
    /// Placement: doc.Create.NewFamilyInstance(reference, XYZ, FamilySymbol)
    /// where reference is the wall's interior face.
    /// </summary>
    internal sealed class WallSidewallPlacementStrategy : IFamilyPlacementStrategy
    {
        public string Name => "WallSidewall";

        public bool CanHandle(string familyPlacementType) => false;

        public PlacementOutcome Place(PlacementContext context)
        {
            Document doc = context.Document;
            XYZ xyz = context.RequestedPoint;

            if (!context.WallEdgeIndex.HasValue || context.RoomPolygon == null ||
                context.RoomPolygon.Count < 3)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    "WallSidewall placement requires a wall edge index and room polygon, " +
                    "but one or both were missing from the PlacementContext.");
            }

            int edgeIdx = context.WallEdgeIndex.Value;
            List<double[]> polygon = context.RoomPolygon;

            if (edgeIdx < 0 || edgeIdx >= polygon.Count)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    $"WallEdgeIndex {edgeIdx} is out of range for a polygon with {polygon.Count} vertices.");
            }

            double[] a = polygon[edgeIdx];
            double[] b = polygon[(edgeIdx + 1) % polygon.Count];
            if (a == null || b == null || a.Length < 2 || b.Length < 2)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    "Wall edge vertices are invalid.");
            }

            // Edge midpoint for wall lookup.
            XYZ midPt = new XYZ((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5, xyz.Z);
            XYZ edgeDir = new XYZ(b[0] - a[0], b[1] - a[1], 0).Normalize();

            // Collect walls near the edge midpoint.
            Reference wallRef = FindWallFace(doc, midPt, edgeDir, context.Level);
            if (wallRef == null)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    $"No wall found near edge {edgeIdx} (midpoint [{midPt.X:F2}, {midPt.Y:F2}]). " +
                    "Ensure the room boundary corresponds to actual wall elements.");
            }

            try
            {
                // referenceDirection must lie in the host face. For a vertical wall face,
                // the direction along the wall edge (edgeDir) lies in the face.
                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    wallRef, xyz, edgeDir, context.Symbol);

                return PlacementOutcome.CreatedInstance(
                    instance, "WallSidewallFace", $"wall-edge-{edgeIdx}");
            }
            catch (Exception ex)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    $"WallSidewall Revit placement failed: {ex.Message}",
                    hostingStrategy: "WallSidewallFace");
            }
        }

        /// <summary>
        /// Finds a wall whose interior face is near the given midpoint and whose
        /// direction aligns with the edge direction.
        /// </summary>
        private static Reference FindWallFace(
            Document doc, XYZ midPt, XYZ edgeDir, Level level)
        {
            double toleranceFt = 3.0;
            double dirTolerance = 0.866; // cos(30°) — allow 30° angular deviation.

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType();

            foreach (Element elem in collector)
            {
                Wall wall = elem as Wall;
                if (wall == null) continue;

                // Quick level check if both have levels.
                if (level != null && wall.LevelId != null && wall.LevelId != level.Id)
                {
                    // Allow linked walls (LevelId may not match directly).
                    // The geometric check below is the real filter.
                }

                // Get the wall's location curve midpoint for proximity check.
                LocationCurve locCurve = wall.Location as LocationCurve;
                if (locCurve == null || locCurve.Curve == null) continue;

                XYZ wallMid = locCurve.Curve.Evaluate(0.5, true);
                double dist = wallMid.DistanceTo(midPt);
                if (dist > toleranceFt) continue;

                // Check directional alignment.
                XYZ wallDir = (locCurve.Curve.GetEndPoint(1) - locCurve.Curve.GetEndPoint(0)).Normalize();
                double dot = Math.Abs(wallDir.DotProduct(edgeDir));
                if (dot < dirTolerance) continue;

                // Found a matching wall — return a reference to its interior face.
                // The interior face is the one facing toward the room (midPt).
                Reference faceRef = GetInteriorFaceReference(wall, midPt);
                if (faceRef != null) return faceRef;
            }

            return null;
        }

        /// <summary>
        /// Returns a Reference to the wall face that faces toward the given interior point.
        /// </summary>
        private static Reference GetInteriorFaceReference(Wall wall, XYZ interiorPoint)
        {
            Options options = new Options
            {
                ComputeReferences = true
            };

            GeometryElement geomElem = wall.get_Geometry(options);
            if (geomElem == null) return null;

            Reference bestRef = null;
            double bestDot = -1;

            foreach (GeometryObject geomObj in geomElem)
            {
                Solid solid = geomObj as Solid;
                if (solid == null || solid.Faces.Size == 0) continue;

                foreach (Face face in solid.Faces)
                {
                    PlanarFace planarFace = face as PlanarFace;
                    if (planarFace == null) continue;

                    // Vertical faces have a roughly horizontal normal.
                    XYZ normal = planarFace.FaceNormal;
                    if (Math.Abs(normal.Z) > 0.5) continue;

                    // The interior face is the one whose normal points toward the interior point.
                    XYZ faceCenter = planarFace.Evaluate(new UV(0.5, 0.5));
                    XYZ toInterior = (interiorPoint - faceCenter).Normalize();
                    double dot = normal.DotProduct(toInterior);

                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestRef = face.Reference;
                    }
                }
            }

            return bestRef;
        }
    }
}
