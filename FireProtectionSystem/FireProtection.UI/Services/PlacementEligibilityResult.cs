namespace FireProtection.UI.Services
{
    /// <summary>
    /// Machine-readable result of a pre-placement eligibility / preflight check for one room, given the
    /// currently selected sprinkler family/type. Produced by the Backend placement service (which reuses the
    /// exact strategy / host-resolution / placement-validation logic used by actual placement) and consumed by
    /// the UI so that a room can be shown blocked BEFORE the user executes placement. This is Revit-free; no
    /// Revit type crosses it.
    ///
    /// Four semantic states are mandatory (master prompt): ELIGIBLE, BLOCKED, PLACEMENT_ERROR, UNKNOWN.
    /// A placement *defect* (exception, created-but-invalid, unexpected runtime failure) is NEVER silently
    /// converted into BLOCKED — it is reported as PLACEMENT_ERROR or UNKNOWN so the defect stays visible.
    /// </summary>
    public class PlacementEligibilityResult
    {
        /// <summary>One of <see cref="PlacementEligibilityStates"/>. Drives every derived flag below.</summary>
        public string EligibilityState { get; set; } = PlacementEligibilityStates.Unknown;

        /// <summary>Human-readable explanation (shown in the UI tooltip). For non-eligible states this is the
        /// deterministic reason (BLOCKED) or the placement failure detail (PLACEMENT_ERROR / UNKNOWN).</summary>
        public string Reason { get; set; }

        /// <summary>Structured status code (see <see cref="PlacementEligibilityStatusCodes"/>).</summary>
        public string StatusCode { get; set; }

        /// <summary>Proven Revit <c>FamilyPlacementType</c> of the selected family (FaceBased / WorkPlaneBased / OneLevelBased).</summary>
        public string FamilyPlacementType { get; set; }

        /// <summary>Hosting strategy the placement would use (FaceBasedHost / WorkPlaneCeilingFace / WorkPlaneSketchPlane / LevelBased).</summary>
        public string HostingStrategy { get; set; }

        /// <summary>"host" / "link:&lt;name&gt;" / "none" — where a ceiling host was found (or not).</summary>
        public string CeilingSource { get; set; }

        /// <summary>Name of the linked model that supplied the ceiling host, when applicable.</summary>
        public string LinkInstanceName { get; set; }

        /// <summary>ElementId (string) of the discovered ceiling host, when applicable.</summary>
        public string HostCeilingElementId { get; set; }

        /// <summary>Resolved host Level id (string), when available.</summary>
        public string HostLevelId { get; set; }

        /// <summary>Resolved host Level name, when available.</summary>
        public string HostLevelName { get; set; }

        public bool IsEligible =>
            string.Equals(EligibilityState, PlacementEligibilityStates.Eligible, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Deterministic inability to place (explicit reason + status code required).</summary>
        public bool IsBlocked =>
            string.Equals(EligibilityState, PlacementEligibilityStates.Blocked, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Placement was attempted but an unexpected/API/runtime failure occurred (must NOT be treated as BLOCKED).</summary>
        public bool IsPlacementError =>
            string.Equals(EligibilityState, PlacementEligibilityStates.PlacementError, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Insufficient evidence to safely classify the room (must NOT be treated as BLOCKED).</summary>
        public bool IsUnknown =>
            string.Equals(EligibilityState, PlacementEligibilityStates.Unknown, System.StringComparison.OrdinalIgnoreCase);

        public static PlacementEligibilityResult Eligible(PlacementEligibilityResult template = null)
        {
            var r = CopyFrom(template);
            r.EligibilityState = PlacementEligibilityStates.Eligible;
            r.IsEligibleFlag = true;
            r.StatusCode = PlacementEligibilityStatusCodes.Eligible;
            r.Reason = null;
            return r;
        }

        public static PlacementEligibilityResult Blocked(PlacementEligibilityResult template, string statusCode, string reason)
        {
            var r = CopyFrom(template);
            r.EligibilityState = PlacementEligibilityStates.Blocked;
            r.IsEligibleFlag = false;
            r.StatusCode = statusCode;
            r.Reason = reason;
            return r;
        }

        // Convenience overload for deterministic (no-template) BLOCKED outcomes.
        public static PlacementEligibilityResult Blocked(string statusCode, string reason) =>
            Blocked((PlacementEligibilityResult)null, statusCode, reason);

        public static PlacementEligibilityResult PlacementError(PlacementEligibilityResult template, string statusCode, string reason)
        {
            var r = CopyFrom(template);
            r.EligibilityState = PlacementEligibilityStates.PlacementError;
            r.IsEligibleFlag = false;
            r.StatusCode = statusCode;
            r.Reason = reason;
            return r;
        }

        public static PlacementEligibilityResult Unknown(string reason, string statusCode = PlacementEligibilityStatusCodes.Unknown)
        {
            var r = new PlacementEligibilityResult
            {
                EligibilityState = PlacementEligibilityStates.Unknown,
                IsEligibleFlag = false,
                StatusCode = statusCode,
                Reason = reason
            };
            return r;
        }

        // Back-compat shim used by the VM when family/type is not yet selected (cannot evaluate yet).
        public static PlacementEligibilityResult Pending() => Unknown(
            "Select a sprinkler family and type to evaluate placement eligibility.",
            PlacementEligibilityStatusCodes.PendingFamilySelection);

        private bool IsEligibleFlag;

        private static PlacementEligibilityResult CopyFrom(PlacementEligibilityResult source)
        {
            if (source == null) return new PlacementEligibilityResult();
            return new PlacementEligibilityResult
            {
                EligibilityState = source.EligibilityState,
                IsEligibleFlag = source.IsEligibleFlag,
                StatusCode = source.StatusCode,
                Reason = source.Reason,
                FamilyPlacementType = source.FamilyPlacementType,
                HostingStrategy = source.HostingStrategy,
                CeilingSource = source.CeilingSource,
                LinkInstanceName = source.LinkInstanceName,
                HostCeilingElementId = source.HostCeilingElementId,
                HostLevelId = source.HostLevelId,
                HostLevelName = source.HostLevelName
            };
        }
    }

    /// <summary>Four mandatory semantic eligibility states.</summary>
    public static class PlacementEligibilityStates
    {
        public const string Eligible = "ELIGIBLE";
        public const string Blocked = "BLOCKED";
        public const string PlacementError = "PLACEMENT_ERROR";
        public const string Unknown = "UNKNOWN";
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
        public const string NoCeilingHost = "NO_CEILING_HOST";
        public const string PlacementStrategyUnavailable = "PLACEMENT_STRATEGY_UNAVAILABLE";
        public const string PreflightError = "PREFLIGHT_ERROR";

        // Candidate-generation / probe outcomes (authoritative eligibility via real calculation + real placement probe).
        public const string NoCandidatePoints = "NO_CANDIDATE_POINTS";
        public const string NoPlaceableCandidate = "NO_PLACEABLE_CANDIDATE";
        public const string NoValidCandidate = "NO_VALID_CANDIDATE";
        public const string CreatedButInvalid = "CREATED_BUT_INVALID";
        public const string ProbeException = "PROBE_EXCEPTION";

        public const string PlacementError = "PLACEMENT_ERROR";
        public const string Unknown = "UNKNOWN";
    }
}
