namespace FireProtection.UI.Services
{
    /// <summary>
    /// Machine-readable result of a pre-placement eligibility / preflight check for one room, given the
    /// currently selected sprinkler family/type AND the room's effective HazardClass. Produced by the Backend
    /// placement service using a deterministic feasibility preflight (the SAME family/level/host/candidate
    /// prerequisites that actual placement uses) and consumed by the UI so a room can be shown blocked BEFORE
    /// the user executes placement. This is Revit-free; no Revit type crosses it.
    ///
    /// Three explicit semantic states (master prompt): ELIGIBLE, BLOCKED, UNDETERMINED. Different states are
    /// never collapsed into one boolean internally.
    /// </summary>
    public class PlacementEligibilityResult
    {
        /// <summary>One of <see cref="EligibilityStates"/>.</summary>
        public string EligibilityState { get; set; } = EligibilityStates.Undetermined;

        /// <summary>Human-readable explanation (shown in the UI tooltip / status). For non-eligible states this is
        /// the deterministic reason (BLOCKED) or the evaluation-failure detail (UNDETERMINED).</summary>
        public string Reason { get; set; }

        /// <summary>Structured status code (see <see cref="PlacementEligibilityStatusCodes"/>).</summary>
        public string StatusCode { get; set; }

        /// <summary>Number of candidate points that satisfy BOTH the candidate-generation rules AND the selected
        /// placement strategy's hosting prerequisites (e.g. a usable ceiling host where required).</summary>
        public int ValidCandidateCount { get; set; }

        /// <summary>The room's effective HazardClass that drove this evaluation (e.g. "Light", "OH1"). Carried for
        /// diagnostics only; the authoritative hazard-dependent calculation already happened in candidate generation.</summary>
        public string HazardClass { get; set; }

        /// <summary>Proven Revit FamilyPlacementType of the selected family (FaceBased / WorkPlaneBased / OneLevelBased).</summary>
        public string FamilyPlacementType { get; set; }

        /// <summary>Hosting strategy the placement would use (FaceBasedHost / WorkPlaneCeilingFace / WorkPlaneSketchPlane / LevelBased).</summary>
        public string HostingStrategy { get; set; }

        /// <summary>"host" / "link:&lt;name&gt;" / "none" — where a ceiling host was found (or not).</summary>
        public string CeilingSource { get; set; }

        /// <summary>Where a ceiling host was found, normalized ("host" / "link:&lt;name&gt;" / "none"). Mirrors <see cref="CeilingSource"/>.</summary>
        public string HostSource { get; set; }

        /// <summary>Linked model name supplying the ceiling host, when applicable.</summary>
        public string LinkInstanceName { get; set; }

        /// <summary>ElementId (string) of the discovered ceiling host, when applicable.</summary>
        public string HostCeilingElementId { get; set; }

        /// <summary>Resolved host Level id (string), when available.</summary>
        public string HostLevelId { get; set; }

        /// <summary>Resolved host Level name, when available.</summary>
        public string HostLevelName { get; set; }

        public bool IsEligible =>
            string.Equals(EligibilityState, EligibilityStates.Eligible, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Deterministic inability to place (explicit reason + status code required).</summary>
        public bool IsBlocked =>
            string.Equals(EligibilityState, EligibilityStates.Blocked, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Could not be determined (configuration/infrastructure/environmental error). Must NOT be
        /// confused with BLOCKED — a normal missing ceiling/host is BLOCKED, not UNDETERMINED.</summary>
        public bool IsUndetermined =>
            string.Equals(EligibilityState, EligibilityStates.Undetermined, System.StringComparison.OrdinalIgnoreCase);

        public static PlacementEligibilityResult Eligible(PlacementEligibilityResult template = null)
        {
            var r = CopyFrom(template);
            r.EligibilityState = EligibilityStates.Eligible;
            r.StatusCode = PlacementEligibilityStatusCodes.Eligible;
            r.Reason = null;
            return r;
        }

        public static PlacementEligibilityResult Blocked(PlacementEligibilityResult template, string statusCode, string reason)
        {
            var r = CopyFrom(template);
            r.EligibilityState = EligibilityStates.Blocked;
            r.StatusCode = statusCode;
            r.Reason = reason;
            return r;
        }

        // Convenience overload for deterministic (no-template) BLOCKED outcomes.
        public static PlacementEligibilityResult Blocked(string statusCode, string reason) =>
            Blocked((PlacementEligibilityResult)null, statusCode, reason);

        /// <summary>Used when eligibility genuinely cannot be determined (config/infra error), NOT when a room is
        /// simply unplaceable — a missing ceiling/host is BLOCKED, never UNDETERMINED.</summary>
        public static PlacementEligibilityResult Undetermined(string reason, string statusCode = PlacementEligibilityStatusCodes.PreflightError)
        {
            return new PlacementEligibilityResult
            {
                EligibilityState = EligibilityStates.Undetermined,
                StatusCode = statusCode,
                Reason = reason
            };
        }

        // Back-compat shim used by the VM when family/type is not yet selected (cannot evaluate yet).
        public static PlacementEligibilityResult Pending() => Undetermined(
            "Select a sprinkler family and type to evaluate placement eligibility.",
            PlacementEligibilityStatusCodes.PendingFamilySelection);

        private static PlacementEligibilityResult CopyFrom(PlacementEligibilityResult source)
        {
            if (source == null) return new PlacementEligibilityResult();
            return new PlacementEligibilityResult
            {
                EligibilityState = source.EligibilityState,
                StatusCode = source.StatusCode,
                Reason = source.Reason,
                ValidCandidateCount = source.ValidCandidateCount,
                HazardClass = source.HazardClass,
                FamilyPlacementType = source.FamilyPlacementType,
                HostingStrategy = source.HostingStrategy,
                CeilingSource = source.CeilingSource,
                HostSource = source.HostSource,
                LinkInstanceName = source.LinkInstanceName,
                HostCeilingElementId = source.HostCeilingElementId,
                HostLevelId = source.HostLevelId,
                HostLevelName = source.HostLevelName
            };
        }
    }

    /// <summary>Three mandatory semantic eligibility states.</summary>
    public static class EligibilityStates
    {
        public const string Eligible = "ELIGIBLE";
        public const string Blocked = "BLOCKED";
        public const string Undetermined = "UNDETERMINED";
    }

    /// <summary>
    /// Structured status codes for <see cref="PlacementEligibilityResult"/>. Machine-readable; the
    /// <see cref="PlacementEligibilityResult.Reason"/> is a human supplement only.
    /// </summary>
    public static class PlacementEligibilityStatusCodes
    {
        public const string Eligible = "ELIGIBLE";
        public const string PendingFamilySelection = "PENDING_FAMILY_SELECTION";
        public const string MissingRoomGeometry = "MISSING_ROOM_GEOMETRY";
        public const string UnsupportedFamilyPlacement = "UNSUPPORTED_FAMILY_PLACEMENT";
        public const string UnsupportedFamilyPlacementType = "UNSUPPORTED_FAMILY_PLACEMENT_TYPE";
        public const string MissingHostLevel = "MISSING_HOST_LEVEL";
        public const string NoCandidatePoints = "NO_CANDIDATE_POINTS";

        /// <summary>Required ceiling host does not exist for a family/strategy that requires one (FaceBased and
        /// WorkPlaneBased; a numeric "Ceiling Height" UI value is NOT proof of a usable host).</summary>
        public const string NoUsableCeilingHost = "NO_USABLE_CEILING_HOST";

        /// <summary>Candidate calculation could not be run (insufficient evidence) — UNDETERMINED, never BLOCKED.</summary>
        public const string CalculationFailed = "CALCULATION_FAILED";

        public const string PreflightError = "PREFLIGHT_ERROR";
    }
}
