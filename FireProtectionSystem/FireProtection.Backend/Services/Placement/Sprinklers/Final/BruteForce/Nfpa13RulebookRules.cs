using System.Collections.Generic;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Sprinkler layout rules transcribed from the project rulebook:
    /// "Layout, Detail and Calculation of Fire Sprinkler Systems" (NFSA), which reproduces
    /// <b>NFPA 13 (2002)</b>.
    ///
    /// DESIGN BASIS NOTE
    /// -----------------
    /// These values are the project's declared design basis. Every number here is traceable to
    /// quoted rulebook prose in the comments. This edition is superseded by NFPA 13 (2022), so
    /// results remain provisional and flagged for engineering review until an FPE confirms the
    /// basis — that is a recorded decision, not an oversight (see STANDARDS_MEMORY.md).
    ///
    /// OCR RELIABILITY
    /// ----------------
    /// Only rules stated unambiguously in the book are implemented here. Numeric TABLES in the
    /// OCR are NOT reliably readable, and where the book only *references* an NFPA table without
    /// reproducing it, no value is invented. See <see cref="BeamRuleTableAvailable"/> and the
    /// explicitly-not-implemented list on <see cref="KnownGaps"/>.
    /// </summary>
    public static class Nfpa13RulebookRules
    {
        // ---------------------------------------------------------------------------------
        // Chapter 19 — Distances between sprinklers
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Ch.19 p.218: "The minimum allowable distance between sprinklers is 6 ft. Sprinklers are
        /// not allowed to be spaced closer than that because water spray from one sprinkler might
        /// prevent the sprinkler next to it from opening during a fire... 'cold soldering'."
        /// </summary>
        public const double MinSpacingFt = 6.0;

        /// <summary>
        /// Ch.19 p.218: "The maximum allowable distance between sprinklers is 15 ft in light and
        /// ordinary hazard occupancies and 12 ft in extra hazard and high-piled storage occupancies."
        /// </summary>
        public const double MaxSpacingLightOrdinaryFt = 15.0;

        /// <summary>Ch.19 p.218: max spacing for extra hazard / high-piled storage.</summary>
        public const double MaxSpacingExtraHazardFt = 12.0;

        /// <summary>
        /// Ch.19 p.219: "The maximum allowable distance between a sprinkler and the wall is
        /// one-half of the allowable distance between sprinklers. ... the distance to a wall is
        /// allowed to be half of the allowable distance between sprinklers, not half of the actual
        /// distance between sprinklers."
        /// </summary>
        public const double WallDistanceFractionOfMaxSpacing = 0.5;

        /// <summary>
        /// Ch.19 p.220: "The minimum allowable distance between a sprinkler and the wall is 4
        /// inches. Sprinklers are not allowed to be installed closer than 4 inches because there
        /// will not be enough room to get a sprinkler wrench in."
        /// </summary>
        public const double MinWallClearanceFt = 4.0 / 12.0;

        /// <summary>
        /// Ch.19 p.220, non-90-degree corner: "an additional requirement establishes that the
        /// sprinklers cannot be any more than 3/4 of the maximum allowable distance between
        /// sprinklers away from the corner of the room. For example ... 11.25 feet from the corner
        /// (0.75 x 15)."
        /// </summary>
        public const double CornerDistanceFractionOfMaxSpacing = 0.75;      

        // ---------------------------------------------------------------------------------
        // Chapter 19 — Deflector distance below ceiling
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Ch.19 p.225, unobstructed construction: "the sprinkler needs to be between 1 and 12
        /// inches down from the ceiling. The sprinkler is not allowed to be closer than 1 inch to
        /// the ceiling so that it can be removed from its fitting without removing a piece of the
        /// ceiling. The sprinkler cannot be more than 12 inches down from the ceiling because this
        /// will delay the activation of the sprinkler."
        /// </summary>
        public const double DeflectorMinDropUnobstructedFt = 1.0 / 12.0;
        public const double DeflectorMaxDropUnobstructedFt = 12.0 / 12.0;

        /// <summary>
        /// Ch.19 p.225-226, obstructed construction: sprinklers are placed "between one and six
        /// inches below the structural members".
        /// </summary>
        public const double DeflectorMinDropObstructedFt = 1.0 / 12.0;
        public const double DeflectorMaxDropObstructedFt = 6.0 / 12.0;

        /// <summary>
        /// Ch.19 p.226, obstructed exception 1: "The first exception allows sprinklers to be
        /// installed above the plane of the bottom of the structural members if the sprinkler is far
        /// enough away from the structural member to spray under the member (needs to meet the Beam
        /// Rule). In this case, the sprinkler is not allowed to be more than 22 inches below the
        /// roof deck."
        /// </summary>
        public const double DeflectorMaxDropAboveMembersFt = 22.0 / 12.0;

        /// <summary>
        /// Ch.19 p.226, obstructed exception 2 (pockets): "the sprinklers are expected to be within
        /// 12 inches of the roof deck, treating each ceiling pocket as a room with an unobstructed
        /// ceiling."
        /// </summary>
        public const double DeflectorMaxDropInPocketFt = 12.0 / 12.0;

        /// <summary>
        /// Ch.19 p.229, sloped ceiling peak rule: "sprinklers need to be placed so that the highest
        /// sprinkler near the peak is not more than 3 ft down from the peak". Exception: on steep
        /// pitches the sprinkler may be lower, provided a 2 ft clear distance to the structural
        /// member is preserved.
        /// </summary>
        public const double PeakMaxDropFt = 3.0;

        /// <summary>
        /// Ch.19 p.219, sloped roofs: "Roofs with a gentle slope less than 2 in 12 are allowed to be
        /// treated as flat... Where sprinklers are installed under a sloped roof or ceiling, the
        /// distance between sprinklers ... is required to be measured along the slope."
        /// NFPA 13 defines flat as slope not exceeding a rise of 1 in 2 per 12 of run.
        /// </summary>
        public const double FlatSlopeRisePer12Run = 1.0 / 2.0;
        public const double GentleSlopeRisePer12Run = 2.0 / 12.0;

        // ---------------------------------------------------------------------------------
        // Chapter 19 — Unobstructed vs obstructed construction (NFPA 13 3.7.2 as quoted)
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Ch.19 p.216 quoting NFPA 13 3.7.2: "No matter what kind of structural members they are,
        /// even if they are solid, they are considered Unobstructed Construction if they are more
        /// than 7 1/2 ft on center. For structural members spaced less than 7 1/2 ft on center, the
        /// construction can only be considered Unobstructed if the openings in the cross section of
        /// the members are greater than 70%."
        /// </summary>
        public const double UnobstructedIfMemberSpacingOverFt = 7.5;

        /// <summary>Ch.19 p.227: below 3 ft on center the construction is necessarily obstructed.</summary>
        public const double ObstructedIfMemberSpacingUnderFt = 3.0;

        /// <summary>
        /// The 70% open-cross-section test. The extractor does not measure cross-section openings,
        /// so members between 3 ft and 7 1/2 ft on center cannot be proven unobstructed and are
        /// treated as OBSTRUCTED (the conservative branch).
        /// </summary>
        public const double UnobstructedOpenCrossSectionFraction = 0.70;

        // ---------------------------------------------------------------------------------
        // Chapter 19 — Small Room Rule
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Ch.19 p.223, all four conditions must hold:
        ///   "1) The room must be a light hazard occupancy
        ///    2) The room must be less than 800 sq ft
        ///    3) The ceiling must be unobstructed construction
        ///    4) The room must be surrounded by walls and a ceiling. Openings in walls ... are
        ///       permitted if there is at least an 8 inch deep lintel above the opening to trap heat."
        /// </summary>
        public const double SmallRoomMaxAreaSqFt = 800.0;
        public const double SmallRoomLintelDepthFt = 8.0 / 12.0;

        /// <summary>
        /// Ch.19 p.223: "sprinklers in the room are allowed to be spaced up to 9 ft away from one of
        /// the walls in the room. Note that the 9 ft distance is not limited to a single sprinkler."
        /// </summary>
        public const double SmallRoomMaxWallDistanceFt = 9.0;

        // ---------------------------------------------------------------------------------
        // Chapter 20 — Obstructions to sprinklers
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Ch.20 p.246: "The dividing line between the two zones is a horizontal plane 18 inches
        /// (46 cm) below the sprinkler extending throughout the room or the area of sprinkler
        /// coverage. This plane was selected as the dividing line between the two zones to coincide
        /// with the minimum clearance rule for equipment and storage below sprinklers."
        /// </summary>
        public const double ObstructionZonePlaneBelowSprinklerFt = 18.0 / 12.0;

        /// <summary>
        /// Ch.20 p.248, "Three Times" rule (NFPA 13 8.6.5.2.1.3): "requires sprinklers to be a
        /// distance away from the potential obstruction of three times the maximum dimension of the
        /// obstruction up to a maximum of 24 inches (61 cm). Note that the maximum dimension of 24
        /// inches applies to the maximum required distance for the sprinkler away from the
        /// obstruction, not the maximum allowable dimension of the obstruction."
        ///
        /// Worked example from the book: a 3 in x 4 in column requires 12 in from the near edge
        /// (3 x 4 in), confirming the MAXIMUM dimension governs, not the nearest one.
        /// </summary>
        public const double ThreeTimesRuleFactor = 3.0;
        public const double ThreeTimesRuleMaxRequiredDistanceFt = 24.0 / 12.0;

        /// <summary>
        /// Ch.20 p.248, exception to the Three Times rule: "if sprinklers are installed on the other
        /// side of the obstruction, the 'Three Times' rule can be ignored, as long as the sprinkler on
        /// the other side of the obstruction is not more than one-half the allowable distance
        /// between sprinklers away from the centerline of the obstruction."
        /// </summary>
        public const double ThreeTimesExceptionHalfSpacing = 0.5;

        /// <summary>
        /// Ch.20 p.248, "If the obstruction is less than 4 ft wide, and the beam rule cannot be met,
        /// sprinklers are allowed to be installed on the other side of the obstruction as long as the
        /// distance from the sprinklers to the centerline of the obstruction is not more than half
        /// the maximum distance allowed between sprinklers."
        /// </summary>
        public const double OtherSideExceptionMaxObstructionWidthFt = 4.0;

        /// <summary>
        /// Ch.20 p.249, lower zone: "The only real obstruction rule in this zone is to put a
        /// sprinkler under any permanent fixture that is over 4 ft (1.2 m) wide. Note that furniture
        /// is not considered a permanent feature. However, one of the things that is considered a
        /// permanent feature is an overhead door."
        /// </summary>
        public const double PermanentFixtureRequiringSprinklerWidthFt = 4.0;

        /// <summary>
        /// Ch.20 p.246, list of obstruction categories the "Three Times" rule applies to:
        /// "This applies to vertical obstructions like columns and to open horizontal obstructions
        /// like the bottom chords of trusses and the bottom flanges of bar joist members."
        /// </summary>
        public static readonly string[] ThreeTimesRuleCategories =
        {
            "OST_StructuralColumns",
            "OST_Columns",
            "OST_StructuralFraming"  // open bar joist / truss bottom chord only; see KnownGaps
        };

        // ---------------------------------------------------------------------------------
        // What this design basis does NOT contain
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// FALSE — the Beam rule lookup table (NFPA 13 Table 8.6.5.1.2) is NOT reproduced in the
        /// project rulebook. Ch.20 p.246 only says: "See Table 8.6.5.1.2 and Figure 8.6.5.1.2(a) of
        /// NFPA 13 for more information on exactly how far away sprinklers need to be from these
        /// obstructions."
        ///
        /// The required clear distance depends on the distance from the bottom of the obstruction to
        /// the sprinkler deflector, and those numbers are simply not in the book. They are therefore
        /// NOT implemented and NOT guessed. Obstructions tight to the ceiling and continuous across
        /// the room are flagged for engineering review instead.
        /// </summary>
        public const bool BeamRuleTableAvailable = false;

        /// <summary>
        /// Rules that are present in the rulebook but are intentionally NOT implemented yet,
        /// recorded here so the gap is explicit rather than silent.
        /// </summary>
        public static readonly IReadOnlyList<string> KnownGaps = new List<string>
        {
            "Beam rule (NFPA 13 Table 8.6.5.1.2) clear distances: the rulebook references the NFPA " +
            "table but does not reproduce it, so no values are implemented. Rooms with obstructions " +
            "tight to the ceiling are flagged for review.",

            "Obstructed-construction exception 'sprinklers in every pocket created by the structural " +
            "members' (Ch.19 p.226) and the concrete-tee exception (Ch.19 p.226): not implemented, as " +
            "they need per-member pocket geometry the AABB obstacle model cannot represent.",

            "Composite wood joist firestopping (Ch.19 p.228) and concealed-space / attic rules " +
            "(Ch.19 p.230): not implemented; extraction does not identify these assemblies.",

            "High-piled storage and rack storage commodity/clearance tables (Ch.37, Ch.38): not " +
            "implemented. Rooms whose name suggests storage are already flagged for human review.",

            "Extended-coverage sprinkler spacing (Ch.21) and ESFR rules: not implemented; ESFR remains " +
            "a review-only flag with no geometry.",

            "Deflector drop is applied as a single room-wide value. Where the book requires the drop " +
            "to be measured per obstruction (Beam rule) or per beam pocket, the room is flagged."
        };
    }
}