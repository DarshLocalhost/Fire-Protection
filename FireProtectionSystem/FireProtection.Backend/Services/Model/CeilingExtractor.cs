using Autodesk.Revit.DB;
using FireProtection.Backend.Models.DTOs;
using System;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Model
{
    public class CeilingExtractor
    {
        public class ExtractedCeilingItem
        {
            public Ceiling Ceiling { get; set; }
            public Document Document { get; set; }
            public Transform Transform { get; set; }
            public SourceReferenceData Source { get; set; }
            public CeilingData Dto { get; set; }
            public BoundingBoxXYZ LocalBoundingBox { get; set; }
            public BoundingBox3DData HostBoundingBox { get; set; }
            public List<Solid> Solids { get; set; }
            public List<PlanarFace> BottomFaces { get; set; }
            public double? BottomElevationFt { get; set; }
            public double? TopElevationFt { get; set; }
            public string SlopeType { get; set; }
            public double? SlopeDegrees { get; set; }
        }

        public List<ExtractedCeilingItem> CollectAllCeilings(RevitModelContext context, List<ExtractionIssue> issues)
        {
            List<ExtractedCeilingItem> ceilingItems = new List<ExtractedCeilingItem>();

            CollectFromDocument(
                context.HostDocument,
                Transform.Identity,
                new SourceReferenceData
                {
                    DocumentTitle = context.HostDocument.Title,
                    DocumentPath = context.HostDocument.PathName ?? string.Empty,
                    IsFromLink = false,
                    LinkInstanceId = string.Empty,
                    LinkName = string.Empty
                },
                ceilingItems,
                issues);

            foreach (RevitLinkContext link in context.LoadedLinks)
            {
                if (link.LinkedDocument == null) continue;

#if REVIT_2024 || REVIT_2025 || REVIT_2026
                string linkInstanceId = link.InstanceId.Value.ToString();
#else
                string linkInstanceId = link.InstanceId.ToString();
#endif

                CollectFromDocument(
                    link.LinkedDocument,
                    link.TotalTransform ?? link.Transform,
                    new SourceReferenceData
                    {
                        DocumentTitle = link.DocumentTitle,
                        DocumentPath = link.DocumentPath,
                        IsFromLink = true,
                        LinkInstanceId = linkInstanceId,
                        LinkName = link.LinkName
                    },
                    ceilingItems,
                    issues);
            }

            return ceilingItems;
        }

        private void CollectFromDocument(
            Document document,
            Transform transform,
            SourceReferenceData source,
            List<ExtractedCeilingItem> ceilingItems,
            List<ExtractionIssue> issues)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_Ceilings)
                    .WhereElementIsNotElementType();

                foreach (Element element in collector)
                {
                    if (element is Ceiling ceiling)
                    {
                        ExtractedCeilingItem item = ProcessCeiling(ceiling, document, transform, source, issues);
                        if (item != null)
                        {
                            ceilingItems.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ExtractionIssue(
                    ExtractionIssueSeverity.Warning,
                    "CeilingExtraction",
                    $"Error collecting ceilings from document '{source.DocumentTitle}': {ex.Message}"));
            }
        }

        private ExtractedCeilingItem ProcessCeiling(
            Ceiling ceiling,
            Document document,
            Transform transform,
            SourceReferenceData source,
            List<ExtractionIssue> issues)
        {
#if REVIT_2024 || REVIT_2025 || REVIT_2026
            string elementId = ceiling.Id.Value.ToString();
#else
            string elementId = ceiling.Id.ToString();
#endif

            BoundingBoxXYZ localBBox = ceiling.get_BoundingBox(null);
            BoundingBox3DData hostBBox = localBBox != null
                ? RevitModelContext.TransformBoundingBox(localBBox, transform)
                : null;

            List<Solid> solids = new List<Solid>();
            ExtractSolids(ceiling, solids);

            double minBottomZ = double.MaxValue;
            double maxTopZ = double.MinValue;
            List<PlanarFace> bottomFaces = new List<PlanarFace>();
            bool hasSlopedFace = false;
            double maxSlopeDeg = 0.0;

            foreach (Solid solid in solids)
            {
                if (solid == null || solid.Volume <= 1e-9) continue;

                foreach (Face face in solid.Faces)
                {
                    if (face is PlanarFace planarFace)
                    {
                        XYZ normal = planarFace.FaceNormal;
                        XYZ hostNormal = RevitModelContext.TransformVector(normal, transform);

                        if (hostNormal.Z < -0.1)
                        {
                            bottomFaces.Add(planarFace);

                            double angleFromDown = hostNormal.AngleTo(new XYZ(0, 0, -1));
                            double angleDeg = angleFromDown * (180.0 / Math.PI);
                            if (angleDeg > 1.0)
                            {
                                hasSlopedFace = true;
                                if (angleDeg > maxSlopeDeg) maxSlopeDeg = angleDeg;
                            }
                        }
                    }

                    BoundingBoxUV uvBox = face.GetBoundingBox();
                    XYZ faceMin = face.Evaluate(uvBox.Min);
                    XYZ faceMax = face.Evaluate(uvBox.Max);
                    XYZ hMin = RevitModelContext.TransformPoint(faceMin, transform);
                    XYZ hMax = RevitModelContext.TransformPoint(faceMax, transform);

                    if (hMin.Z < minBottomZ) minBottomZ = hMin.Z;
                    if (hMax.Z > maxTopZ) maxTopZ = hMax.Z;
                }
            }

            if (minBottomZ == double.MaxValue && hostBBox != null)
            {
                minBottomZ = hostBBox.Min.Z;
                maxTopZ = hostBBox.Max.Z;
            }

            string slopeType = "FLAT";
            if (hasSlopedFace)
            {
                slopeType = "SLOPED";
            }
            else if (hostBBox != null && Math.Abs(hostBBox.Max.Z - hostBBox.Min.Z) > 0.5)
            {
                slopeType = "STEPPED";
            }

            string typeName = string.Empty;
            string familyName = string.Empty;
            ElementType elemType = document.GetElement(ceiling.GetTypeId()) as ElementType;
            if (elemType != null)
            {
                typeName = elemType.Name;
                familyName = elemType.FamilyName;
            }

            bool hasGrid = false;
            double gridOriginX = 0.0, gridOriginY = 0.0, gridU = 0.0, gridV = 0.0, gridAngle = 0.0;
            string gridOriginSource = CeilingData.GridOriginSources.None;
            if (string.Equals(slopeType, "FLAT", StringComparison.OrdinalIgnoreCase))
            {
                TryReadCeilingGrid(
                    ceiling, document, hostBBox, bottomFaces, transform, issues,
                    out hasGrid, out gridOriginX, out gridOriginY, out gridU, out gridV, out gridAngle,
                    out gridOriginSource);

                if (!hasGrid && TryParseTileSizeFromName(typeName, out double tnU, out double tnV))
                {
                    hasGrid = true;
                    gridU = tnU;
                    gridV = tnV;
                    gridOriginSource = CeilingData.GridOriginSources.TypeName;

                    DeriveOriginFromCeilingGeometry(
                        bottomFaces, hostBBox, transform,
                        out gridOriginX, out gridOriginY, out gridAngle);

                    issues.Add(new ExtractionIssue(
                        ExtractionIssueSeverity.Info,
                        "CeilingGrid",
                        $"Ceiling grid derived from type name '{typeName}': tile {gridU:F2}x{gridV:F2} ft, "
                        + $"origin=({gridOriginX:F3},{gridOriginY:F3}), angle={gridAngle:F4}. "
                        + "No readable surface pattern — verify tile centers on RCP.",
                        elementId,
                        ceiling.Name));
                }
            }

            string ceilingLevelId = string.Empty;
            string ceilingLevelName = string.Empty;
            if (ceiling.LevelId != null && ceiling.LevelId != ElementId.InvalidElementId)
            {
#if REVIT_2024 || REVIT_2025 || REVIT_2026
                ceilingLevelId = ceiling.LevelId.Value.ToString();
#else
                ceilingLevelId = ceiling.LevelId.ToString();
#endif
                Level ceilingLevel = document.GetElement(ceiling.LevelId) as Level;
                if (ceilingLevel != null)
                {
                    ceilingLevelName = ceilingLevel.Name;
                }
            }

            CeilingData dto = new CeilingData
            {
                ElementId = elementId,
                LevelId = ceilingLevelId,
                LevelName = ceilingLevelName,
                CeilingName = ceiling.Name,
                FamilyName = familyName,
                TypeName = typeName,
                Category = "OST_Ceilings",
                Source = new SourceReferenceData
                {
                    DocumentTitle = source.DocumentTitle,
                    DocumentPath = source.DocumentPath,
                    IsFromLink = source.IsFromLink,
                    LinkInstanceId = source.LinkInstanceId,
                    LinkName = source.LinkName
                },
                BoundingBox = hostBBox,
                BottomElevationFt = minBottomZ != double.MaxValue ? (double?)minBottomZ : null,
                TopElevationFt = maxTopZ != double.MinValue ? (double?)maxTopZ : null,
                SlopeType = slopeType,
                SlopeDegrees = hasSlopedFace ? (double?)maxSlopeDeg : 0.0,
                IsRoomDirectCeiling = false,
HasReadableGrid = hasGrid,
            GridOriginXFt = gridOriginX,
            GridOriginYFt = gridOriginY,
            GridSpacingUFt = gridU,
            GridSpacingVFt = gridV,
            GridAngleRad = gridAngle,
            GridOriginSource = hasGrid ? gridOriginSource : CeilingData.GridOriginSources.None
            };

            return new ExtractedCeilingItem
            {
                Ceiling = ceiling,
                Document = document,
                Transform = transform,
                Source = source,
                Dto = dto,
                LocalBoundingBox = localBBox,
                HostBoundingBox = hostBBox,
                Solids = solids,
                BottomFaces = bottomFaces,
                BottomElevationFt = minBottomZ != double.MaxValue ? (double?)minBottomZ : null,
                TopElevationFt = maxTopZ != double.MinValue ? (double?)maxTopZ : null,
                SlopeType = slopeType,
                SlopeDegrees = hasSlopedFace ? (double?)maxSlopeDeg : 0.0
            };
        }

        internal static bool TryParseTileSizeFromName(string typeName, out double uFt, out double vFt)
        {
            uFt = 0.0;
            vFt = 0.0;
            if (string.IsNullOrWhiteSpace(typeName)) return false;

            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                typeName, @"(\d+(?:\.\d+)?)\s*['""]?\s*[xX×]\s*(\d+(?:\.\d+)?)\s*['""]?");
            if (!m.Success) return false;

            if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double a)) return false;
            if (!double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double b)) return false;

            if (a >= 100.0 || b >= 100.0)
            {
                a /= 304.8;
                b /= 304.8;
            }

            if (a < 0.5 || b < 0.5 || a > 12.0 || b > 12.0) return false;

            uFt = a;
            vFt = b;
            return true;
        }

        private static void TryReadCeilingGrid(
            Ceiling ceiling,
            Document document,
            BoundingBox3DData hostBBox,
            List<PlanarFace> bottomFaces,
            Transform transform,
            List<ExtractionIssue> issues,
            out bool hasGrid,
            out double originX,
            out double originY,
            out double uFt,
            out double vFt,
            out double angleRad,
            out string originSource)
        {
            hasGrid = false;
            originX = 0.0; originY = 0.0; uFt = 0.0; vFt = 0.0; angleRad = 0.0;
            originSource = CeilingData.GridOriginSources.None;

#if REVIT_2024 || REVIT_2025 || REVIT_2026
            string failReason = null;
            try
            {
                // ── SOURCE 1 (preferred): the ceiling's OWN grid lines ──────────────────
                // Revit 2025.3 added Ceiling.GetCeilingGridLines. It is strictly better than the
                // material pattern because the lines are returned in MODEL space: the module is
                // the real median gap between them and the origin is the exact intersection of two
                // real lines. Every other source has to GUESS the phase, and a phase guess that is
                // wrong by half a tile puts every device on a tile CORNER while still reporting a
                // perfectly valid grid.
                if (TryReadGridFromCeilingGridLines(
                        ceiling, transform,
                        out originX, out originY, out uFt, out vFt, out angleRad))
                {
                    hasGrid = true;
                    originSource = CeilingData.GridOriginSources.Exact;

                    issues.Add(new ExtractionIssue(
                        ExtractionIssueSeverity.Info,
                        "CeilingGrid",
                        $"Ceiling grid read from GetCeilingGridLines: pitch {uFt:F3}x{vFt:F3} ft, "
                        + $"angle {angleRad:F4} rad, EXACT origin=({originX:F3},{originY:F3}). "
                        + "Device positions will sit on the drafter's tile centres.",
                        ceiling.Id.Value.ToString(),
                        ceiling.Name));
                    return;
                }

                // ── SOURCE 2: the grid pattern on the ceiling type's material ────────────
                List<ElementId> matIds = new List<ElementId>();
                if (bottomFaces != null)
                {
                    foreach (PlanarFace pf in bottomFaces)
                        if (pf != null) AddIfNew(matIds, pf.MaterialElementId);
                }
                CollectElementMaterials(ceiling, matIds);
                CeilingType ct = document.GetElement(ceiling.GetTypeId()) as CeilingType;
                if (ct != null)
                {
                    CollectCompoundStructureMaterials(ct, matIds);
                    CollectElementMaterials(ct, matIds);
                }

                if (matIds.Count == 0)
                {
                    failReason = "ceiling exposes no material (bottom face, instance, or type)";
                    return;
                }

                string patternOriginSource = CeilingData.GridOriginSources.Provisional;
                foreach (ElementId matId in matIds)
                {
                    Material mat = document.GetElement(matId) as Material;
                    if (mat == null) continue;

                    if (TryReadGridFromPattern(document, mat.SurfaceForegroundPatternId,
                            bottomFaces, transform, hostBBox,
                            out originX, out originY, out uFt, out vFt, out angleRad,
                            out patternOriginSource)
                         || TryReadGridFromPattern(document, mat.SurfaceBackgroundPatternId,
                            bottomFaces, transform, hostBBox,
                            out originX, out originY, out uFt, out vFt, out angleRad,
                            out patternOriginSource))
                    {
                        hasGrid = true;
                        originSource = patternOriginSource;
                        break;
                    }
                }

                if (!hasGrid)
                {
                    failReason = $"none of {matIds.Count} candidate material(s) had a 2+-line model fill pattern";
                    return;
                }

                bool exact = originSource == CeilingData.GridOriginSources.PatternMapped;
                issues.Add(new ExtractionIssue(
                    exact ? ExtractionIssueSeverity.Info : ExtractionIssueSeverity.Warning,
                    "CeilingGrid",
                    $"Ceiling grid read from material fill pattern: pitch {uFt:F3}x{vFt:F3} ft, "
                    + $"angle {angleRad:F4} rad, origin=({originX:F3},{originY:F3}), phase={originSource}. "
                    + (exact
                        ? "Device positions will sit on the drafter's tile centres."
                        : "ORIGIN IS PROVISIONAL (guessed from ceiling geometry, not from real grid "
                          + "lines) — the lattice may be offset within one tile. Set the per-room tile "
                          + "size override to align it, or check the RCP."),
                    ceiling.Id.Value.ToString(),
                    ceiling.Name));
            }
            catch (Exception ex)
            {
                hasGrid = false;
                originSource = CeilingData.GridOriginSources.None;
                failReason = "exception: " + ex.Message;
            }
            finally
            {
                if (!hasGrid && failReason != null)
                {
                    issues.Add(new ExtractionIssue(
                        ExtractionIssueSeverity.Info,
                        "CeilingGrid",
                        $"Ceiling grid NOT read ({failReason}). Free/centered layout used.",
                        ceiling.Id.Value.ToString(),
                        ceiling.Name));
                }
            }
#endif
        }

/// <summary>
        /// How far the two pattern grid directions may deviate from perpendicular before the
        /// pattern is rejected as not-a-tile-grid (~5.7 degrees). Loose enough to tolerate a
        /// hand-drawn pattern, tight enough that a hatch or a skewed pattern cannot slip through
        /// and be treated as a square/rectangular tile module.
        /// </summary>
        private const double OrthogonalityToleranceRad = 0.1;

        /// <summary>Angle tolerance for grouping grid lines into two families, radians (~1.1 deg).</summary>
        private const double GridDirectionToleranceRad = 0.02;

        /// <summary>Two line positions closer than this (ft) are the same grid line.</summary>
        private const double GridPositionToleranceFt = 1e-4;

        /// <summary>
        /// Sanity bounds for a tile module, feet. Mirrors the window the material-pattern reader has
        /// always used, so the two sources cannot disagree about what counts as a tile size.
        /// </summary>
        private const double MinGridPitchFt = 0.5;
        private const double MaxGridPitchFt = 12.0;

        /// <summary>
        /// Resolved once. <c>Ceiling.GetCeilingGridLines</c> exists only from Revit 2025.3, so this
        /// is null on 2024 and 2025.0-2025.2 and every caller simply falls through to the material
        /// pattern. Reflection rather than #if so ONE codebase compiles for all targets — the same
        /// approach used by the reference implementation this was ported from.
        /// </summary>
        private static readonly System.Reflection.MethodInfo GetCeilingGridLinesMethod =
            typeof(Ceiling).GetMethod(
                "GetCeilingGridLines",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(bool) },
                null);

        /// <summary>True when the running Revit exposes Ceiling.GetCeilingGridLines (2025.3+).</summary>
        public static bool IsCeilingGridLinesApiAvailable
        {
            get { return GetCeilingGridLinesMethod != null; }
        }

/// <summary>
        /// Reads the tile lattice from the ceiling's OWN grid lines (Revit 2025.3+).
        ///
        /// This is the only source that yields an EXACT origin. Every other source has to infer the
        /// phase from a material pattern in pattern-space, or from the type name, and a phase wrong
        /// by half a tile makes every device land on a tile CORNER while still reporting a valid
        /// grid. Here the origin is the literal intersection of two real lines, so it cannot be wrong.
        ///
        /// Pipeline: invoke the API -> keep straight, non-degenerate lines -> group by direction
        /// (mod PI, so line direction is directionless) -> require two near-perpendicular families
        /// -> the module in each direction is the median gap between that family's line OFFSETS ->
        /// the origin is where one line from each family crosses.
        ///
        /// Read-only. Returns false on any problem so the caller falls back to the material pattern
        /// rather than failing extraction. Never throws.
        /// </summary>
        private static bool TryReadGridFromCeilingGridLines(
            Ceiling ceiling,
            Transform transform,
            out double originX,
            out double originY,
            out double uFt,
            out double vFt,
            out double angleRad)
        {
            originX = 0.0; originY = 0.0; uFt = 0.0; vFt = 0.0; angleRad = 0.0;

            if (ceiling == null || GetCeilingGridLinesMethod == null) return false;

            try
            {
                object raw = GetCeilingGridLinesMethod.Invoke(ceiling, new object[] { false });
                System.Collections.IEnumerable seq = raw as System.Collections.IEnumerable;
                if (seq == null) return false;

                // family 0 holds lines running along dirA; family 1 along dirB.
                var familyA = new List<double[]>();
                var familyB = new List<double[]>();
                double angleA = 0.0;
                double angleB = 0.0;
                bool haveA = false;
                bool haveB = false;

                foreach (object o in seq)
                {
                    Line line = o as Line;
                    if (line == null) continue;

                    XYZ a = line.GetEndPoint(0);
                    XYZ b = line.GetEndPoint(1);
                    if (a == null || b == null) continue;

                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) <= 1e-9) continue;   // stub / zero-length

                    if (transform != null && !transform.IsIdentity)
                    {
                        a = transform.OfPoint(a);
                        b = transform.OfPoint(b);
                        dx = b.X - a.X;
                        dy = b.Y - a.Y;
                        if (Math.Sqrt(dx * dx + dy * dy) <= 1e-9) continue;
                    }

                    double ang = NormalizeAngle(Math.Atan2(dy, dx));

                    if (!haveA)
                    {
                        // First line defines family A. The second family is whichever direction is
                        // furthest from it, so a dominant direction plus its perpendicular wins even
                        // if stray/edge lines arrive first.
                        angleA = ang;
                        haveA = true;
                        familyA.Add(new[] { a.X, a.Y });
                        continue;
                    }

                    if (!haveB)
                    {
                        double d = AngleDistance(ang, angleA);
                        if (d > 20.0 * Math.PI / 180.0)
                        {
                            angleB = ang;
                            haveB = true;
                            familyB.Add(new[] { a.X, a.Y });
                        }
                        else
                        {
                            familyA.Add(new[] { a.X, a.Y });
                        }
                        continue;
                    }

                    if (AngleDistance(ang, angleA) < 20.0 * Math.PI / 180.0)
                        familyA.Add(new[] { a.X, a.Y });
                    else if (AngleDistance(ang, angleB) < 20.0 * Math.PI / 180.0)
                        familyB.Add(new[] { a.X, a.Y });
                }

                if (!haveA || !haveB || familyA.Count < 2 || familyB.Count < 2) return false;

                // PERPENDICULARITY. CeilingGridMath builds v̂ by rotating û by exactly 90 degrees, so
                // a non-perpendicular pair of families cannot be represented at all. Reject.
                double delta = Math.Abs(NormalizeAngle(angleB - angleA));
                if (Math.Abs(delta - (Math.PI / 2.0)) > OrthogonalityToleranceRad) return false;

                double dirAx = Math.Cos(angleA), dirAy = Math.Sin(angleA);
                double dirBx = Math.Cos(angleB), dirBy = Math.Sin(angleB);

                // A family-A line runs along dirA, so it sits at a constant position along dirB.
                // The spacing of family A therefore IS the module along v, and vice versa. This is
                // the same convention CeilingGridMath uses: U is measured along û, V along v̂.
                double moduleU = MedianGap(OffsetsOf(familyA, dirBx, dirBy));
                double moduleV = MedianGap(OffsetsOf(familyB, dirAx, dirAy));
                if (moduleU < MinGridPitchFt || moduleV < MinGridPitchFt) return false;
                if (moduleU > MaxGridPitchFt || moduleV > MaxGridPitchFt) return false;

                // Origin = crossing of the first line of each family.
                double a0x = familyA[0][0], a0y = familyA[0][1];
                double b0x = familyB[0][0], b0y = familyB[0][1];
                double denom = dirAx * dirBy - dirAy * dirBx;      // cross(dirA, dirB)
                if (Math.Abs(denom) < 1e-9) return false;
                double t = ((b0x - a0x) * dirBy - (b0y - a0y) * dirBx) / denom;
                if (double.IsNaN(t) || double.IsInfinity(t)) return false;

                originX = a0x + t * dirAx;
                originY = a0y + t * dirAy;
                uFt = moduleU;
                vFt = moduleV;
                angleRad = NormalizeAngle(angleA);
                return true;
            }
            catch
            {
                // Includes reflection TargetInvocationException. A tile grid is never worth failing
                // extraction over, so every failure just falls back to the material pattern.
                originX = 0.0; originY = 0.0; uFt = 0.0; vFt = 0.0; angleRad = 0.0;
                return false;
            }
        }

        /// <summary>
        /// The scalar offset of each parallel line along (dirX, dirY). Each line is parallel to the
        /// OTHER direction, so a single point on it is enough to locate it.
        /// </summary>
        private static List<double> OffsetsOf(List<double[]> points, double dirX, double dirY)
        {
            var offsets = new List<double>();
            foreach (double[] p in points)
            {
                double d = p[0] * dirX + p[1] * dirY;
                // Collapse duplicates (the same grid line reported twice, or two lines within
                // floating-point noise of each other).
                bool dup = false;
                for (int i = 0; i < offsets.Count; i++)
                {
                    if (Math.Abs(offsets[i] - d) <= GridPositionToleranceFt) { dup = true; break; }
                }
                if (!dup) offsets.Add(d);
            }
            return offsets;
        }

        /// <summary>Median gap between distinct parallel line offsets; 0 when fewer than two.</summary>
        private static double MedianGap(List<double> offsets)
        {
            if (offsets == null || offsets.Count < 2) return 0.0;

            var sorted = new List<double>(offsets);
            sorted.Sort();

            var gaps = new List<double>();
            for (int i = 1; i < sorted.Count; i++)
            {
                double g = sorted[i] - sorted[i - 1];
                if (g > GridPositionToleranceFt) gaps.Add(g);
            }
            if (gaps.Count == 0) return 0.0;

            gaps.Sort();
            int n = gaps.Count;
            return n % 2 == 1 ? gaps[n / 2] : (gaps[n / 2 - 1] + gaps[n / 2]) / 2.0;
        }

        /// <summary>Smallest angle between two line directions, ignoring line reversal (mod PI).</summary>
        private static double AngleDistance(double a, double b)
        {
            double d = Math.Abs(NormalizeAngle(a) - NormalizeAngle(b));
            return Math.Min(d, Math.PI - d);
        }

#if REVIT_2024 || REVIT_2025 || REVIT_2026
        private static bool TryReadGridFromPattern(
            Document document,
            ElementId patternId,
            List<PlanarFace> bottomFaces,
            Transform transform,
            BoundingBox3DData hostBBox,
            out double originX,
            out double originY,
            out double uFt,
            out double vFt,
            out double angleRad,
            out string originSource)
        {
            originX = 0.0; originY = 0.0; uFt = 0.0; vFt = 0.0; angleRad = 0.0;
            originSource = CeilingData.GridOriginSources.None;
            if (patternId == null || patternId == ElementId.InvalidElementId) return false;

            FillPatternElement fpe = document.GetElement(patternId) as FillPatternElement;
            if (fpe == null) return false;

            FillPattern pattern = fpe.GetFillPattern();
            if (pattern == null || pattern.IsSolidFill) return false;
            if (pattern.Target != FillPatternTarget.Model) return false;

            IList<FillGrid> grids = pattern.GetFillGrids();
            if (grids == null || grids.Count < 2) return false;

            double pitch0 = Math.Abs(grids[0].Offset);
            double pitch1 = Math.Abs(grids[1].Offset);
            if (pitch0 <= 1e-6 || pitch1 <= 1e-6) return false;
            if (pitch0 < 0.5 || pitch1 < 0.5 || pitch0 > 12.0 || pitch1 > 12.0) return false;

            // ORTHOGONALITY CHECK. CeilingGridMath assumes û and v̂ are perpendicular: v̂ is
            // computed as û rotated by exactly 90 degrees, and IndexToWorld/WorldToIndex are built
            // on that. A 2-line pattern whose lines are NOT perpendicular (a hatch, a skewed
            // pattern, or grids[0]/grids[1] simply not being the two grid families) would have had
            // its second angle silently DISCARDED before this check existed — giving a lattice that
            // is not the pattern at all. Reject instead.
            double angleDelta = Math.Abs(NormalizeAngle(grids[1].Angle - grids[0].Angle));
            if (Math.Abs(angleDelta - (Math.PI / 2.0)) > OrthogonalityToleranceRad)
                return false;

            uFt = pitch0;
            vFt = pitch1;
            angleRad = grids[0].Angle;

            bool originSet = false;
            try
            {
                double x1 = grids[0].Origin.U;
                double y1 = grids[0].Origin.V;
                double dx1 = Math.Cos(grids[0].Angle);
                double dy1 = Math.Sin(grids[0].Angle);

                double x2 = grids[1].Origin.U;
                double y2 = grids[1].Origin.V;
                double dx2 = Math.Cos(grids[1].Angle);
                double dy2 = Math.Sin(grids[1].Angle);

                // 2D Line Intersection for the pattern origin
                double det = dx1 * dy2 - dy1 * dx2;

                if (Math.Abs(det) > 1e-6)
                {
                    double t = ((x2 - x1) * dy2 - (y2 - y1) * dx2) / det;
                    double ou = x1 + dx1 * t;
                    double ov = y1 + dy1 * t;

                    PlanarFace face = FirstUsableBottomFace(bottomFaces);
                    if (face != null)
                    {
                        XYZ onFace = face.Evaluate(new UV(ou, ov));

                        if (transform != null && !transform.IsIdentity)
                            onFace = transform.OfPoint(onFace);

                        // Direct assignment! No more Math.Round snapping that destroyed phase.
                        originX = onFace.X;
                        originY = onFace.Y;
                        originSet = true;
                    }
                }
            }
            catch
            {
                originSet = false;
            }

            if (!originSet)
            {
                // The phase is a GUESS here. Say so, loudly: a lattice built on a guessed origin is
                // internally consistent but may not match the drafter's RCP, and previously this
                // was indistinguishable from an exact one in the extraction output.
                originSource = CeilingData.GridOriginSources.Provisional;

                DeriveOriginFromCeilingGeometry(
                    bottomFaces, hostBBox, transform,
                    out originX, out originY, out double derivedAngle);

                if (Math.Abs(angleRad) < 1e-12)
                    angleRad = derivedAngle;
            }
            else
            {
                originSource = CeilingData.GridOriginSources.PatternMapped;
            }

            return true;
        }

        /// <summary>Folds an angle into [0, PI) so direction comparisons ignore line reversal.</summary>
        private static double NormalizeAngle(double a)
        {
            double r = a % Math.PI;
            if (r < 0) r += Math.PI;
            return r;
        }

        private static PlanarFace FirstUsableBottomFace(List<PlanarFace> bottomFaces)
        {
            if (bottomFaces == null) return null;
            foreach (PlanarFace pf in bottomFaces)
                if (pf != null) return pf;
            return null;
        }

        private static void DeriveOriginFromCeilingGeometry(
            List<PlanarFace> bottomFaces,
            BoundingBox3DData hostBBox,
            Transform transform,
            out double originX, out double originY, out double angleRad)
        {
            originX = 0.0;
            originY = 0.0;
            angleRad = 0.0;

            PlanarFace face = FirstUsableBottomFace(bottomFaces);
            if (face != null)
            {
                BoundingBoxUV fbb = face.GetBoundingBox();
                UV center = new UV((fbb.Min.U + fbb.Max.U) * 0.5, (fbb.Min.V + fbb.Max.V) * 0.5);
                XYZ p = face.Evaluate(center);
                if (transform != null && !transform.IsIdentity)
                    p = transform.OfPoint(p);

                originX = p.X;
                originY = p.Y;

                try
                {
                    XYZ axisX = face.XVector;
                    if (transform != null && !transform.IsIdentity)
                        axisX = transform.OfVector(axisX);
                    angleRad = Math.Atan2(axisX.Y, axisX.X);
                }
                catch { angleRad = 0.0; }

                return;
            }

            if (hostBBox != null && hostBBox.Min != null)
            {
                originX = hostBBox.Min.X;
                originY = hostBBox.Min.Y;
                if (transform != null && !transform.IsIdentity)
                    angleRad = Math.Atan2(transform.BasisX.Y, transform.BasisX.X);
                return;
            }

            if (transform != null)
            {
                originX = transform.Origin.X;
                originY = transform.Origin.Y;
                angleRad = Math.Atan2(transform.BasisX.Y, transform.BasisX.X);
            }
        }
#endif

#if REVIT_2024 || REVIT_2025 || REVIT_2026
        private static void AddIfNew(List<ElementId> ids, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return;
            if (!ids.Contains(id)) ids.Add(id);
        }

        private static void CollectElementMaterials(Element element, List<ElementId> ids)
        {
            if (element == null) return;
            try
            {
                ICollection<ElementId> mats = element.GetMaterialIds(false);
                if (mats != null)
                {
                    foreach (ElementId id in mats) AddIfNew(ids, id);
                }
            }
            catch { }
        }

        private static void CollectCompoundStructureMaterials(HostObjAttributes type, List<ElementId> ids)
        {
            if (type == null) return;
            try
            {
                CompoundStructure cs = type.GetCompoundStructure();
                if (cs == null) return;
                foreach (CompoundStructureLayer layer in cs.GetLayers())
                {
                    if (layer != null) AddIfNew(ids, layer.MaterialId);
                }
            }
            catch { }
        }
#endif

        private static void ExtractSolids(Element element, List<Solid> solids)
        {
            Options options = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = false,
                IncludeNonVisibleObjects = true
            };

            GeometryElement geomElem = element.get_Geometry(options);
            if (geomElem == null) return;

            ExtractSolidsRecursive(geomElem, solids);
        }

        private static void ExtractSolidsRecursive(GeometryElement geomElem, List<Solid> solids)
        {
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid && solid.Volume > 1e-9)
                {
                    solids.Add(solid);
                }
                else if (obj is GeometryInstance inst)
                {
                    GeometryElement instGeom = inst.GetInstanceGeometry();
                    if (instGeom != null)
                    {
                        ExtractSolidsRecursive(instGeom, solids);
                    }
                }
            }
        }
    }
}
