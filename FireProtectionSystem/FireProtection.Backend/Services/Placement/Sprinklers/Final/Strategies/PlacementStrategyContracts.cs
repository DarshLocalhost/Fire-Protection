using Autodesk.Revit.DB;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    /// <summary>
    /// Stable, machine-readable status/error codes for sprinkler placement (master prompt §26).
    /// The human-readable message is a supplement, never the primary signal. These strings are copied
    /// onto the Revit-independent UI result DTOs, so no Revit type ever crosses the boundary.
    /// </summary>
    internal static class PlacementStatusCodes
    {
        // Terminal placement statuses (§24).
        public const string PlacedAndValid = "PLACED_AND_VALID";
        public const string PlacedButInvalid = "PLACED_BUT_INVALID";
        public const string PlacementFailed = "PLACEMENT_FAILED";
        public const string SkippedDuplicate = "SKIPPED_DUPLICATE";

        // Failure reason codes (§26).
        public const string NonFiniteCoordinates = "NON_FINITE_COORDINATES";
        public const string LevelResolutionFailed = "LEVEL_RESOLUTION_FAILED";
        public const string UnsupportedFamilyPlacement = "UNSUPPORTED_FAMILY_PLACEMENT";
        public const string RequiredHostUnavailable = "REQUIRED_HOST_UNAVAILABLE";
        public const string WorkPlaneUnavailable = "WORKPLANE_UNAVAILABLE";
        public const string InvalidHost = "INVALID_HOST";
        public const string RevitCreationFailed = "REVIT_CREATION_FAILED";
        public const string PostPlacementValidationFailed = "POST_PLACEMENT_VALIDATION_FAILED";
        public const string Duplicate = "DUPLICATE";
    }

    /// <summary>
    /// Revit-facing inputs a placement strategy needs to execute one point. Constructed by the placement
    /// service after the family symbol, host level, and duplicate checks have already been resolved.
    /// </summary>
    internal sealed class PlacementContext
    {
        public Document Document;
        public FamilySymbol Symbol;
        public Level Level;
        public XYZ RequestedPoint;
        public string FamilyPlacementType;
        public CeilingHostResolver CeilingHostResolver;
    }

    /// <summary>
    /// Structured result of a single strategy placement attempt. A strategy either (a) fails to create an
    /// instance — <see cref="Instance"/> is null and <see cref="ErrorCode"/>/<see cref="Message"/> explain
    /// why — or (b) creates an instance, leaving post-placement spatial validation (§23) to the service.
    /// A returned <see cref="Instance"/> is NOT treated as success on its own (hard rule 8).
    /// </summary>
    internal sealed class PlacementOutcome
    {
        public FamilyInstance Instance;
        public string ErrorCode;          // null when an instance was created
        public string Message;            // human-readable supplement
        public string HostingStrategy;    // FaceBasedHost / WorkPlaneCeilingFace / WorkPlaneSketchPlane / LevelBased
        public string CeilingSource;      // "host" / "link:<name>" / "none"
        public string LinkInstanceName;
        public string HostCeilingElementId;

        public bool Created => Instance != null && Instance.Id != ElementId.InvalidElementId;

        public static PlacementOutcome Fail(
            string errorCode,
            string message,
            string hostingStrategy = null,
            string ceilingSource = null,
            string linkInstanceName = null,
            string hostCeilingElementId = null)
        {
            return new PlacementOutcome
            {
                Instance = null,
                ErrorCode = errorCode,
                Message = message,
                HostingStrategy = hostingStrategy,
                CeilingSource = ceilingSource ?? "none",
                LinkInstanceName = linkInstanceName,
                HostCeilingElementId = hostCeilingElementId
            };
        }

        public static PlacementOutcome CreatedInstance(
            FamilyInstance instance,
            string hostingStrategy,
            string ceilingSource,
            string linkInstanceName = null,
            string hostCeilingElementId = null)
        {
            return new PlacementOutcome
            {
                Instance = instance,
                ErrorCode = null,
                Message = null,
                HostingStrategy = hostingStrategy,
                CeilingSource = ceilingSource ?? "none",
                LinkInstanceName = linkInstanceName,
                HostCeilingElementId = hostCeilingElementId
            };
        }
    }

    /// <summary>
    /// A placement strategy for one Revit <c>FamilyPlacementType</c> (master prompt §13). The concrete
    /// strategy is chosen from the family's PROVEN placement type — never inferred from the family name —
    /// and never silently substitutes an incompatible strategy (hard rules 5, 6, 7).
    /// </summary>
    internal interface IFamilyPlacementStrategy
    {
        /// <summary>Diagnostic name of the strategy.</summary>
        string Name { get; }

        /// <summary>True when this strategy is the correct handler for the given FamilyPlacementType.</summary>
        bool CanHandle(string familyPlacementType);

        /// <summary>Attempt to create the instance. Does not perform post-placement validation.</summary>
        PlacementOutcome Place(PlacementContext context);
    }
}
