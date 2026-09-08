namespace FireProtection.Backend.Models.Placement.Sprinklers.Final
{
    /// <summary>
    /// Plain, Revit-free classification of the placement behavior required by the selected
    /// sprinkler family/type. The value is derived from the proven
    /// <c>Autodesk.Revit.DB.FamilyPlacementType</c> at the Revit-aware boundary
    /// (see <see cref="FireProtection.Backend.Services.Placement.RevitSprinklerFamilySource"/>)
    /// and carried through the input pipeline as a plain string + this enum.
    ///
    /// The values map 1:1 to the candidate-mode categories identified in the Step 1 audit
    /// (G-02 / G-16) and to the existing placement strategies:
    ///
    /// <list type="bullet">
    /// <item><see cref="CeilingOverhead"/>   — pendent / drop-down pendent on a flat ceiling
    ///                                                (proven <c>FamilyPlacementType</c> is
    ///                                                <c>WorkPlaneBased</c> hosted on a ceiling face).</item>
    /// <item><see cref="WallSidewall"/>       — sidewall sprinkler; the future candidate grid
    ///                                                is wall-anchored, not ceiling-anchored.</item>
    /// <item><see cref="FaceHosted"/>         — <c>FaceBased</c> family; the future candidate
    ///                                                grid requires a usable host face at each point.</item>
    /// <item><see cref="WorkPlaneDependent"/> — generic work-plane family; the future candidate
    ///                                                grid must place on a work plane (level or
    ///                                                sketch plane) at the candidate point.</item>
    /// <item><see cref="LevelHosted"/>        — <c>OneLevelBased</c> family; the future candidate
    ///                                                grid is plain level-anchored and ignores
    ///                                                ceiling/host requirements.</item>
    /// <item><see cref="Unsupported"/>        — the family could not be resolved, or its
    ///                                                <c>FamilyPlacementType</c> did not map to
    ///                                                a known behavior; the room must be flagged
    ///                                                REVIEW REQUIRED (never silently downgraded).</item>
    /// </list>
    ///
    /// Step 2 carries this enum through the existing input pipeline WITHOUT changing
    /// <see cref="FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce.BruteForceCalculationService"/>
    /// behavior. The string is stored alongside the enum on <see cref="SelectedSprinklerInfo"/>
    /// and on <see cref="PlacementRoomInput"/> for both audit and future-candidate use.
    /// </summary>
    public enum DevicePlacementBehavior
    {
        /// <summary>Unknown / not yet resolved. The default for a freshly built DTO.</summary>
        Unknown = 0,

        /// <summary>Ceiling-anchored pendent / overhead. Maps to a WorkPlaneBased family
        /// placed on a ceiling face; the future candidate grid is the existing AABB XY grid
        /// with a per-point Z from the ceiling mesh.</summary>
        CeilingOverhead = 1,

        /// <summary>Wall-anchored sidewall. The future candidate grid is wall-aligned,
        /// not ceiling-anchored.</summary>
        WallSidewall = 2,

        /// <summary>Face-hosted family. The future candidate grid requires a usable host
        /// face (ceiling or other) for every candidate point.</summary>
        FaceHosted = 3,

        /// <summary>Generic work-plane family. The future candidate grid must place on
        /// a work plane (level or sketch plane) at the candidate point.</summary>
        WorkPlaneDependent = 4,

        /// <summary>Plain level-hosted family. No ceiling or host required; the future
        /// candidate grid stays on a single level plane.</summary>
        LevelHosted = 5,

        /// <summary>Family/type could not be resolved or the placement type is unsupported.
        /// The room must be flagged REVIEW REQUIRED; the calculation layer does not
        /// silently fall back to a default placement behavior.</summary>
        Unsupported = 6
    }
}
