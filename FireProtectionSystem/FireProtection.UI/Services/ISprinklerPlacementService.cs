using FireProtection.UI.Models;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Revit-independent contract for the actual Revit sprinkler placement step.
    /// The concrete implementation lives in the Backend (it holds the active Revit Document and
    /// uses the Autodesk.Revit.DB API). This keeps the calculation/models free of Revit references.
    /// </summary>
    public interface ISprinklerPlacementService
    {
        /// <summary>
        /// Creates real Revit FamilyInstance sprinklers for every calculated point in <paramref name="calcResult"/>,
        /// resolving the selected FamilySymbol dynamically and handling level/host/transaction concerns.
        /// </summary>
        SprinklerPlacementResult PlaceSprinklers(
            string selectedFamilyName,
            string selectedTypeName,
            BruteForceCalculationResult calcResult);

        /// <summary>
        /// Authoritative pre-placement eligibility / preflight for a single room given the currently selected
        /// sprinkler family/type and the room's actual candidate sprinkler points (produced by the SAME
        /// BruteForce calculation the placement step consumes). The implementation probes real placement of those
        /// candidates through the SAME strategy/host-resolution/level-resolution path as <see cref="PlaceSprinklers"/>
        /// inside a rolled-back Revit transaction, so eligibility is provable rather than heuristic. A room is
        /// eligible only when at least one candidate can be placed AND validated (spatially valid). Returns a
        /// structured, Revit-free result the UI uses to block rooms the production pipeline cannot place.
        /// </summary>
        PlacementEligibilityResult EvaluateRoomEligibility(
            RoomUiData room,
            System.Collections.Generic.IReadOnlyList<CalculatedSprinklerPoint> candidates,
            string selectedFamilyName,
            string selectedTypeName);

        /// <summary>
        /// Drops any cached eligibility results. Call when inputs that influence placement feasibility change
        /// (family/type/level/model state) so the next probe is authoritative, not stale.
        /// </summary>
        void ClearEligibilityCache();
    }
}
