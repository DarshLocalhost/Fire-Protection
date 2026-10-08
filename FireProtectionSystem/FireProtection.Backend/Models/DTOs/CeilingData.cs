using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public class SourceReferenceData
    {
        [JsonProperty("documentTitle")]
        public string DocumentTitle { get; set; }

        [JsonProperty("documentPath")]
        public string DocumentPath { get; set; }

        [JsonProperty("linkInstanceId")]
        public string LinkInstanceId { get; set; }

        [JsonProperty("linkName")]
        public string LinkName { get; set; }

        [JsonProperty("isFromLink")]
        public bool IsFromLink { get; set; }
    }

    public class CeilingData
    {
        [JsonProperty("elementId")]
        public string ElementId { get; set; }

        [JsonProperty("levelId")]
        public string LevelId { get; set; }

        [JsonProperty("levelName")]
        public string LevelName { get; set; }

        [JsonProperty("ceilingName")]
        public string CeilingName { get; set; }

        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        [JsonProperty("typeName")]
        public string TypeName { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("source")]
        public SourceReferenceData Source { get; set; }

        [JsonProperty("boundingBox")]
        public BoundingBox3DData BoundingBox { get; set; }

        [JsonProperty("bottomElevationFt")]
        public double? BottomElevationFt { get; set; }

        [JsonProperty("topElevationFt")]
        public double? TopElevationFt { get; set; }

        [JsonProperty("heightAboveLevelFt")]
        public double? HeightAboveLevelFt { get; set; }

        [JsonProperty("slopeType")]
        public string SlopeType { get; set; } = "FLAT";

        [JsonProperty("slopeDegrees")]
        public double? SlopeDegrees { get; set; }

        [JsonProperty("boundaryPolygon")]
        public List<double[]> BoundaryPolygon { get; set; }

        [JsonProperty("thicknessFt")]
        public double? ThicknessFt { get; set; }

        [JsonProperty("isRoomDirectCeiling")]
        public bool IsRoomDirectCeiling { get; set; }

        // ---- Ceiling surface grid (acoustic-tile RCP) --------------------------------
        // Populated at the Revit-aware edge (CeilingExtractor.TryReadCeilingGrid) by reading
        // the ceiling type's surface fill pattern when it is a grid pattern. All values are
        // host MEP model coordinates / feet, already normalized by the extraction transform
        // (do NOT apply a second transform downstream). Default off — when HasReadableGrid is
        // false the placement engines behave exactly as before (free centered array / lattice).
        //
        // Convention: the center of tile (i, j) is
        //   center = origin + (i + 0.5)·U·û + (j + 0.5)·V·v̂
        // where û = (cos GridAngleRad, sin GridAngleRad), v̂ = (-sin, cos), origin =
        // (GridOriginXFt, GridOriginYFt), U = GridSpacingUFt, V = GridSpacingVFt.
        //
        // PHASE IS THE FRAGILE PART. Pitch and angle read reliably; the lattice ORIGIN is what
        // decides whether an index maps to a tile centre or a tile corner. Two origins are exact
        // (Ceiling.GetCeilingGridLines intersection, and the material pattern mapped onto the
        // face's UV space); the rest are guesses from ceiling geometry and are tagged as such in
        // GridOriginSource / HasExactGridPhase. Do NOT treat HasReadableGrid alone as "devices are
        // on the drafter's tile centres" — check HasExactGridPhase.

        /// <summary>True only when a grid surface pattern (tile pitch + angle) was read.</summary>
        [JsonProperty("hasReadableGrid")]
        public bool HasReadableGrid { get; set; }

        /// <summary>X of a lattice node (tile-corner origin) in host feet.</summary>
        [JsonProperty("gridOriginXFt")]
        public double GridOriginXFt { get; set; }

        /// <summary>Y of a lattice node (tile-corner origin) in host feet.</summary>
        [JsonProperty("gridOriginYFt")]
        public double GridOriginYFt { get; set; }

        /// <summary>Tile pitch along the û axis, feet.</summary>
        [JsonProperty("gridSpacingUFt")]
        public double GridSpacingUFt { get; set; }

        /// <summary>Tile pitch along the v̂ axis, feet.</summary>
        [JsonProperty("gridSpacingVFt")]
        public double GridSpacingVFt { get; set; }

        /// <summary>Rotation of the û axis from world +X, radians.</summary>
        [JsonProperty("gridAngleRad")]
        public double GridAngleRad { get; set; }

        /// <summary>
        /// How the tile lattice ORIGIN (i.e. its phase) was established. See
        /// <see cref="GridOriginSources"/>.
        /// </summary>
        /// <remarks>
        /// This exists because pitch is easy to read and PHASE is not, yet a wrong phase is
        /// invisible until you look at the ceiling: a lattice whose origin is off by half a tile
        /// reports every device as being on a tile centre when it is actually on a tile CORNER.
        ///
        /// <see cref="GridOriginSources.Exact"/> means the origin is the true intersection of two
        /// real grid lines and tile centres are trustworthy.
        /// <see cref="GridOriginSources.Provisional"/> means the pitch and angle came from a real
        /// source but the origin was GUESSED from ceiling geometry, so the lattice may be offset
        /// within a tile. Devices will still be laid out on a consistent lattice, but that lattice
        /// is not necessarily the drafter's.
        /// </remarks>
        [JsonProperty("gridOriginSource")]
        public string GridOriginSource { get; set; }

        /// <summary>Known values for <see cref="GridOriginSource"/>.</summary>
        public static class GridOriginSources
        {
            /// <summary>Origin is the intersection of two real ceiling grid lines. Trustworthy.</summary>
            public const string Exact = "ExactGridLines";

            /// <summary>Pitch+angle from the material surface pattern; origin mapped through the face. Usually right.</summary>
            public const string PatternMapped = "MaterialPatternMapped";

            /// <summary>Pitch+angle from the material pattern, but the origin was guessed from ceiling geometry.</summary>
            public const string Provisional = "ProvisionalGeometry";

            /// <summary>Pitch parsed out of the ceiling TYPE NAME (e.g. "600x600"); origin guessed.</summary>
            public const string TypeName = "TypeName";

            /// <summary>No grid at all.</summary>
            public const string None = "None";
        }

        /// <summary>
        /// True when the lattice phase is trustworthy enough to promise "this device is on a tile
        /// centre". False for a guessed origin, which is laid out consistently but may be offset
        /// from the real RCP grid.
        /// </summary>
        [JsonIgnore]
        public bool HasExactGridPhase
        {
            get
            {
                return HasReadableGrid
                    && (GridOriginSource == GridOriginSources.Exact
                        || GridOriginSource == GridOriginSources.PatternMapped);
            }
        }

        [JsonProperty("notes")]
        public string Notes { get; set; }

        public CeilingData()
        {
            Source = new SourceReferenceData();
            BoundaryPolygon = new List<double[]>();
            GridOriginSource = GridOriginSources.None;
        }
    }
}
