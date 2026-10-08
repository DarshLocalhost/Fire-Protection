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
        /// <param name="progress">
        /// Optional progress + cancellation channel. Reported once per room; cancellation is honoured at room
        /// boundaries and rolls the whole run back, so a cancelled run places nothing.
        /// </param>
        /// <param name="existingDevicePolicy">
        /// What to do with rooms that already contain sprinklers, i.e. what a second run does.
        /// </param>
        SprinklerPlacementResult PlaceSprinklers(
            string selectedFamilyName,
            string selectedTypeName,
            BruteForceCalculationResult calcResult,
            IPlacementProgress progress = null,
            ExistingDevicePolicy existingDevicePolicy = ExistingDevicePolicy.SkipRoom);

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
        /// Drops any cached eligibility results.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT called automatically before every sweep. The cache key already covers
        /// room identity, level name, hazard class, family and type (see
        /// RevitSprinklerPlacementService), so those inputs miss the cache on their own. Clearing
        /// unconditionally made every sweep start cold, which multiplied the cost of an already
        /// expensive probe by the number of triggers.
        ///
        /// Call it when the DOCUMENT changes in a way the key cannot see - in practice, after
        /// <c>TryLoadFamily</c> succeeds. Known remaining gap: a linked-model edit, or an in-place
        /// ceiling move, is not visible to the key and can therefore serve stale results until the
        /// next family load or manual invalidation. Subscribing to Revit's DocumentChanged event is
        /// the proper fix and is deliberately left open rather than faked here.
        /// </remarks>
        void ClearEligibilityCache();

        /// <summary>
        /// Opens a read-only host-resolution pass: ceiling/floor/roof and link-instance element lists
        /// are collected once and reused for every candidate point until
        /// <see cref="EndHostResolutionPass"/>.
        /// </summary>
        /// <remarks>
        /// This exists purely for performance. Without it, each candidate point rebuilt the same
        /// document-wide element lists, which is O(rooms x candidates x document) collectors on the
        /// thread that also pumps the UI - the main cause of the tool freezing.
        ///
        /// Callers MUST bracket a single read-only sweep and MUST end it before any document
        /// modification. Elements are only valid while the document is unmodified, which is why the
        /// cache is scoped to one pass rather than kept for the session.
        /// </remarks>
        void BeginHostResolutionPass();

        /// <summary>Ends the pass opened by <see cref="BeginHostResolutionPass"/> and releases cached elements.</summary>
        void EndHostResolutionPass();

        /// <summary>
        /// Document-wide collector invocations avoided by the host-resolution cache, for diagnostics.
        /// </summary>
        long HostCollectorCallsSaved { get; }

        /// <summary>
        /// Read-only probe that lists (Room, Family, Type) entries whose chosen family/type
        /// is not loadable in the current Revit model (Decision 017). Returns an empty
        /// list when every row's family is available. No elements are created.
        /// </summary>
        System.Collections.Generic.IReadOnlyList<MissingFamilyEntry> ProbeMissingFamilies(
            BruteForceCalculationResult calcResult);
    }

    public sealed class MissingFamilyEntry
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
    }
}
