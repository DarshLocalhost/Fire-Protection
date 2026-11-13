using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Placement;

namespace SPA.SynthFire.Revit;

/// <summary>Outcome of writing sprinklers into the model.</summary>
public sealed class SprinklerPlacementOutcome
{
    public int Placed { get; init; }
    public int HostedToCeiling { get; init; }
    public int HostedToWall { get; init; }
    public int Cleared { get; init; }
    public int Failed { get; init; }
    public SprinklerMount Mount { get; init; }
    public string? Error { get; init; }
    /// <summary>Actionable note when heads failed for a diagnosable reason (e.g. a host-requiring
    /// sidewall family with only linked walls). Null when there is nothing to explain.</summary>
    public string? Note { get; init; }

    /// <summary>How many device points the solver produced (attempted). If this is 0 the solver
    /// laid out nothing — the family/mount never reached the writer, a separate failure from
    /// "attempted N, placed 0".</summary>
    public int Attempted { get; init; }

    /// <summary>The selected family's Revit <see cref="FamilyPlacementType"/> as text. This is the
    /// single fact that decides whether a sidewall family can place against linked walls at all,
    /// so it is surfaced for diagnosis: WorkPlaneBased/OneLevelBased place unhosted; a strictly
    /// OneLevelBasedHosted family CANNOT place without a real (non-linked) host wall.</summary>
    public string? PlacementType { get; init; }

    /// <summary>Sidewall heads that failed specifically because the family requires a host and only
    /// a linked/absent wall was available (the diagnosable, non-fixable-here case).</summary>
    public int HostRequiredFails { get; init; }

    /// <summary>Distinct per-head failure traces for a sidewall run (which placement branch each
    /// failing head reached and the exact Revit rejection / Z there). Empty when nothing failed.
    /// Surfaced verbatim in the diagnostic dialog so the true cause is seen, not inferred.</summary>
    public IReadOnlyList<string> SidewallDiag { get; init; } = System.Array.Empty<string>();

    public bool Succeeded => Error is null;
}

/// <summary>
/// Writes sprinklers into the model with a placement strategy chosen PER FAMILY,
/// not one-size-fits-all:
///   * Ceiling (pendent/upright): ray-cast up to the ceiling and host the head to
///     that face at the correct floor and Z.
///   * Sidewall: host heads to the room's longest wall(s) and space them along it.
///   * Free (level-based unhosted): place at the exact world point.
///
/// Placed heads are tagged in their Comments so a re-run ("redesign") can clear the
/// previous SPA SynthFire heads first. Everything runs in one transaction.
/// </summary>
public static class SprinklerPlacer
{
    private const string Tag = "SPA SynthFire";

    // Manual coverage-plan placements carry a DISTINCT Comments value so a design-window
    // re-run (Apply → ClearPrevious) leaves them alone: only the auto-solved grid is cleared
    // and rewritten. Clear Devices still removes both (it matches the "SPA SynthFire" prefix).
    private const string ManualTag = "SPA SynthFire (manual)";

    private static bool IsSpaTag(string? comments) => comments == Tag || comments == ManualTag;

    public static SprinklerPlacementOutcome Place(
        Document doc,
        PlacementResult result,
        IReadOnlyDictionary<string, ElementId> roomLevels,
        string familySymbolId,
        SprinklerRuleSet rules,
        bool clearPrevious)
    {
        if (doc.GetElement(new ElementId(long.Parse(familySymbolId))) is not FamilySymbol symbol)
            return new SprinklerPlacementOutcome { Error = "The selected sprinkler family type could not be found." };

        SprinklerMount mount = SprinklerMountResolver.Resolve(symbol);
        int placed = 0, ceilingHosted = 0, wallHosted = 0, failed = 0, cleared = 0, hostRequiredFails = 0;
        int attempted = result.Rooms.Sum(r => r.Sprinklers.Count);
        string placementType = symbol.Family.FamilyPlacementType.ToString();

        // Distinct per-head failure traces for sidewall (which branch died and why). Surfaced in
        // the diagnostic dialog so the real Revit rejection is visible, not guessed.
        var sidewallDiag = new HashSet<string>();

        using (var tx = new Transaction(doc, "SPA SynthFire — Place Sprinklers"))
        {
            WarningSwallower.AttachTo(tx);
            tx.Start();

            if (clearPrevious) cleared = ClearPrevious(doc);

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            // Overhead host search: ceilings, floor slabs above, roofs and beams, in the
            // active model AND linked models, so a head hosts to (or at least sits at) the
            // real ceiling plane instead of free-floating at a guessed height.
            View3D? view3d = GetOrCreate3DView(doc);
            HostFinder hostFinder = mount is SprinklerMount.Ceiling or SprinklerMount.Upright
                ? HostFinder.Create(view3d)
                : HostFinder.Create(null);

            // Sidewall heads need the ceiling finder (deflector Z) and a wall finder (to locate
            // the wall each solved head sits against, host OR linked). Built once, reused.
            HostFinder sidewallCeiling = mount == SprinklerMount.Sidewall ? HostFinder.Create(view3d) : HostFinder.Create(null);
            HostFinder sidewallWalls = mount == SprinklerMount.Sidewall ? HostFinder.CreateWallFinder(view3d) : HostFinder.Create(null);

            foreach (var room in result.Rooms)
            {
                if (!roomLevels.TryGetValue(room.RoomId, out ElementId levelId)) continue;
                if (doc.GetElement(levelId) is not Level level) continue;

                if (mount == SprinklerMount.Sidewall)
                {
                    // Place the SOLVED sidewall points (exactly what the coverage plan shows) at
                    // their own point and throw direction, wall-hosting when a real wall is behind
                    // the head and level-based when the wall is linked/unreachable. The writer runs
                    // NO layout of its own and never needs the host Room element — so linked-model
                    // rooms get their heads too (no more empty rooms), and none cluster on a shared
                    // wall or drop out. Matches the plan head-for-head.
                    var (ok, hosted, fail, hostReq) = PlaceSidewall(doc, symbol, room, level, sidewallCeiling, sidewallWalls, sidewallDiag);
                    placed += ok; wallHosted += hosted; failed += fail; hostRequiredFails += hostReq;
                    continue;
                }

                // Upright heads point UP on a sprig just below the ceiling and must NOT
                // face-host (that flips them to point down); place them level-based at the
                // real ceiling Z. Pendent/ceiling heads prefer face-hosting.
                bool preferFaceHost = mount == SprinklerMount.Ceiling;

                foreach (var p in room.Sprinklers)
                {
                    HostHit hit = hostFinder.FindAbove(p.X, p.Y, level.Elevation);
                    var r = PlacementHelpers.PlaceOverheadDevice(
                        doc, symbol, level, p.X, p.Y, hit, p.Z, preferFaceHost, out ElementId placedId);
                    if (r == PlacementHelpers.HostResult.SurfaceHosted) { placed++; ceilingHosted++; TagInstance(doc, placedId); }
                    else if (r == PlacementHelpers.HostResult.PlacedAtZ) { placed++; TagInstance(doc, placedId); }
                    else failed++;
                }
            }

            tx.Commit();
        }

        return new SprinklerPlacementOutcome
        {
            Placed = placed, HostedToCeiling = ceilingHosted, HostedToWall = wallHosted,
            Cleared = cleared, Failed = failed, Mount = mount,
            Attempted = attempted, PlacementType = placementType, HostRequiredFails = hostRequiredFails,
            SidewallDiag = sidewallDiag.ToList(),
            // A host-requiring sidewall family cannot attach to a LINKED wall, and Revit will not
            // place it unhosted — so tell the user the concrete remedy instead of a bare count.
            Note = hostRequiredFails > 0
                ? $"{hostRequiredFails} sidewall head(s) could not be placed: the family "
                  + $"'{symbol.Family.Name}' must host to a wall, but the wall(s) here are in a "
                  + "linked model (Revit cannot host an instance to a linked wall). Use a "
                  + "face-based sidewall sprinkler family, or copy/monitor the architectural walls "
                  + "into this model, then re-run."
                : null
        };
    }

    // --- manual click-to-place: one head at a chosen plan point -----------------

    /// <summary>
    /// Places ONE sprinkler at a user-clicked plan point (Phase 3 click-to-place), hosted per the
    /// selected family's mount, in its own transaction and SPA-tagged so Clear Devices removes it.
    /// Returns true on success. Live wall/ceiling hosting is unverified outside Revit.
    /// </summary>
    public static bool PlaceOne(
        Document doc, string familySymbolId,
        IReadOnlyDictionary<string, ElementId> roomLevels,
        string roomId, double x, double y, DeviceMount mount, out long placedElementId, out string message)
    {
        placedElementId = 0;
        message = "";
        if (doc.GetElement(new ElementId(long.Parse(familySymbolId))) is not FamilySymbol symbol)
        { message = "The selected sprinkler family type could not be found."; return false; }
        if (!roomLevels.TryGetValue(roomId, out ElementId levelId) || doc.GetElement(levelId) is not Level level)
        { message = "Could not resolve the room's level."; return false; }

        bool ok = false;
        ElementId placed = ElementId.InvalidElementId;
        using (var tx = new Transaction(doc, "SPA SynthFire — Place One Sprinkler"))
        {
            WarningSwallower.AttachTo(tx);
            tx.Start();
            if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

            if (mount == DeviceMount.Sidewall)
            {
                ok = PlaceSidewallOne(doc, symbol, roomId, level, x, y, out placed);
            }
            else
            {
                // Same multi-category (+linked) overhead search as a full run so a manual
                // head lands on the real ceiling/soffit rather than floating. Upright must
                // not face-host (it would flip to point down); pendent/ceiling prefers it.
                HostHit hit = HostFinder.Create(GetOrCreate3DView(doc)).FindAbove(x, y, level.Elevation);
                double roomCeilingZ = level.Elevation + 9.0;
                var r = PlacementHelpers.PlaceOverheadDevice(
                    doc, symbol, level, x, y, hit, roomCeilingZ,
                    preferFaceHost: mount == DeviceMount.Ceiling, out placed);
                ok = r != PlacementHelpers.HostResult.Rejected;
                if (ok) TagInstance(doc, placed, ManualTag);
            }

            if (ok) tx.Commit(); else tx.RollBack();
        }

        if (!ok) { message = "The head could not be hosted at that point (no wall/ceiling reached)."; return false; }
        placedElementId = placed.Value;
        return true;
    }

    /// <summary>
    /// Deletes ONE previously placed sprinkler by element id (undo / click-delete). Verifies the
    /// instance is SPA-tagged before deleting so an auto-placed or foreign element is never removed.
    /// Own transaction. Returns true on success.
    /// </summary>
    public static bool RemoveOne(Document doc, long elementId, out string message)
    {
        message = "";
        if (doc.GetElement(new ElementId(elementId)) is not FamilyInstance inst)
        { message = "The device to remove could not be found."; return false; }
        if (!IsSpaTag(inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()))
        { message = "That device was not placed by SPA SynthFire."; return false; }

        using (var tx = new Transaction(doc, "SPA SynthFire — Remove One Sprinkler"))
        {
            WarningSwallower.AttachTo(tx);
            tx.Start();
            doc.Delete(inst.Id);
            tx.Commit();
        }
        return true;
    }

    // Host one sidewall head at the user-clicked point, thrown into the room away from the
    // nearest wall. Room-independent (works for linked-model rooms too): the throw direction is
    // found by probing outward for the nearest wall rather than reading the host Room boundary.
    private static bool PlaceSidewallOne(
        Document doc, FamilySymbol symbol, string roomId, Level level, double x, double y, out ElementId placedId)
    {
        placedId = ElementId.InvalidElementId;
        View3D? view3d = GetOrCreate3DView(doc);
        HostFinder ceilingFinder = HostFinder.Create(view3d);
        HostFinder wallFinder = HostFinder.CreateWallFinder(view3d);

        // Probe for the nearest wall at mid-wall height (see WallProbeZFt) so a partition wall
        // shorter than the deflector Z is still found; HostSidewallPoint places at the deflector Z.
        double probeZ = level.Elevation + WallProbeZFt;
        if (!NearestWallDirection(wallFinder, x, y, probeZ, out double towardWallX, out double towardWallY))
            return false; // no wall within reach — cannot place a sidewall head here

        // Throw INTO the room = away from the nearest wall.
        double throwRadians = Math.Atan2(-towardWallY, -towardWallX);
        return HostSidewallPoint(doc, symbol, level, x, y, throwRadians, ceilingFinder, wallFinder, ManualTag, out placedId)
            is SidewallResult.WallHosted or SidewallResult.PlacedUnhosted;
    }

    // Probe outward around a point to find the nearest wall (host or linked); returns the unit
    // vector FROM the point TOWARD that wall. Used by manual click-to-place, which has no solved
    // throw direction. Sixteen rays give enough angular resolution for a single interactive head.
    private static bool NearestWallDirection(
        HostFinder wallFinder, double x, double y, double z, out double dx, out double dy)
    {
        dx = dy = 0;
        double bestProx = double.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            double a = i * (2.0 * Math.PI / 16.0);
            double ux = Math.Cos(a), uy = Math.Sin(a);
            HostHit h = wallFinder.FindAlong(x, y, z, ux, uy, MaxSidewallSnapFt);
            if (h.Found && h.Proximity < bestProx) { bestProx = h.Proximity; dx = ux; dy = uy; }
        }
        return bestProx < double.MaxValue;
    }

    // --- redesign: remove heads this tool placed before -------------------------
    private static int ClearPrevious(Document doc)
    {
        var ids = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Sprinklers)
            .WhereElementIsNotElementType()
            .Where(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == Tag)
            .Select(e => e.Id)
            .ToList();

        if (ids.Count > 0) doc.Delete(ids);
        return ids.Count;
    }

    private static void TagInstance(FamilyInstance instance) => TagInstance(instance, Tag);

    private static void TagInstance(FamilyInstance instance, string tag)
    {
        Parameter? comments = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments is { IsReadOnly: false }) comments.Set(tag);
    }

    private static void TagInstance(Document doc, ElementId id) => TagInstance(doc, id, Tag);

    private static void TagInstance(Document doc, ElementId id, string tag)
    {
        if (id != ElementId.InvalidElementId && doc.GetElement(id) is FamilyInstance inst) TagInstance(inst, tag);
    }

    // --- sidewall (wall-hosted, thrown across the room) ------------------------

    /// <summary>Outcome of placing one sidewall head, so the writer can report honestly.</summary>
    private enum SidewallResult
    {
        /// <summary>Nothing placed for an undiagnosed reason (bad Z, Revit refusal).</summary>
        Failed = 0,
        /// <summary>Not placed because the family requires a host and only a linked/no wall exists.</summary>
        FailedHostRequired = 1,
        /// <summary>Placed unhosted at the solved point (linked wall — level/work-plane family).</summary>
        PlacedUnhosted = 2,
        /// <summary>Hosted to an own-model wall element or face.</summary>
        WallHosted = 3,
    }

    /// <summary>
    /// Writes the room's SOLVED sidewall heads — the exact points/directions the coverage plan
    /// draws. Each head is placed at its own point and throw, wall-hosted when a real wall sits
    /// behind it and level-based when that wall is linked or unreachable. The writer runs NO
    /// layout of its own and needs NO host <c>Room</c> element, so linked-model rooms get heads
    /// too and none cluster on a shared wall or drop out. Returns (placed, wall-hosted, failed,
    /// host-required-failures) — the last count drives an actionable status note.
    /// </summary>
    private static (int placed, int hosted, int failed, int hostRequired) PlaceSidewall(
        Document doc, FamilySymbol symbol, RoomPlacementResult room, Level level,
        HostFinder ceilingFinder, HostFinder wallFinder, ICollection<string> diag)
    {
        int placed = 0, hosted = 0, failed = 0, hostRequired = 0;
        bool sampled = false;
        foreach (var p in room.Sprinklers)
        {
            // Solved sidewall points always carry a throw direction; fall back to "toward the
            // room centre" only defensively.
            double thr;
            if (p.OrientationRadians is double o) thr = o;
            else if (room.Footprint is { } fp) thr = Math.Atan2(fp.CenterY - p.Y, fp.CenterX - p.X);
            else thr = 0.0;

            SidewallResult res = HostSidewallPoint(
                doc, symbol, level, p.X, p.Y, thr, ceilingFinder, wallFinder, Tag, out ElementId pid, diag);
            switch (res)
            {
                case SidewallResult.WallHosted: placed++; hosted++; break;
                case SidewallResult.PlacedUnhosted: placed++; break;
                case SidewallResult.FailedHostRequired: failed++; hostRequired++; break;
                default: failed++; break;
            }

            // Sample the FIRST placed head's real position (loc + bbox Z) into the diagnostic.
            // This is the datum that resolves "the dialog says placed but I see none": if the
            // sample's bbox Z is at the deflector, the heads are in the model (a view/visibility
            // issue); if it collapsed to ~0, hosting is not lifting them off the base.
            if (!sampled && (res is SidewallResult.WallHosted or SidewallResult.PlacedUnhosted)
                && doc.GetElement(pid) is FamilyInstance fi)
            {
                diag.Add($"PLACED sample: {(res == SidewallResult.WallHosted ? "wall-hosted" : "unhosted")} {ZReport(fi)}");
                sampled = true;
            }
        }
        return (placed, hosted, failed, hostRequired);
    }

    /// <summary>
    /// Places ONE sidewall head at a solved plan point (x, y) throwing along
    /// <paramref name="throwRadians"/> (into the room). It resolves the deflector Z from the real
    /// ceiling, then finds the wall BEHIND the head by shooting a short ray opposite the throw:
    ///   * own-model wall  → host the head to that wall (attaches to and moves with it);
    ///   * linked wall / no wall → for a face-based family, place it on a VERTICAL work plane at
    ///     the wall face (facing the throw) so it still lands where the plan drew it; for a plain
    ///     level family, place level-based and rotate to the throw. A wall-HOSTED family cannot do
    ///     either against a linked wall (Revit limitation) → reported as host-required.
    /// Shared by the bulk writer and manual click-to-place.
    /// </summary>
    private static SidewallResult HostSidewallPoint(
        Document doc, FamilySymbol symbol, Level level,
        double x, double y, double throwRadians,
        HostFinder ceilingFinder, HostFinder wallFinder, string tag, out ElementId placedId,
        ICollection<string>? diag = null)
    {
        placedId = ElementId.InvalidElementId;

        // Deflector Z from the real ceiling (ray-cast at the head), stable 8 ft fallback —
        // never Room.UnboundedHeight (that's floor-to-floor, not the finished ceiling).
        double targetZ = SidewallDeflectorZ(ceilingFinder, x, y, level);
        var point = new XYZ(x, y, targetZ);

        double tx = Math.Cos(throwRadians), ty = Math.Sin(throwRadians);
        var inward = new XYZ(tx, ty, 0);                                  // throw = into the room
        var inwardPoint = new XYZ(x + tx * 10.0, y + ty * 10.0, targetZ); // used to flip facing

        var trace = new System.Text.StringBuilder();
        trace.Append($"pt=({x:F1},{y:F1}) targetZ={targetZ:F1} type={symbol.Family.FamilyPlacementType}");

        try
        {
            // Host the head to its wall using the PROVEN FireProtectionSystem approach: find the
            // room-facing wall face by iterating wall ELEMENTS + geometry (own doc, then every
            // linked model with the right coordinate transform + CreateLinkReference), then set the
            // elevation offset so the head sits at the deflector Z. This replaces the ray-cast wall
            // search (missed walls → found=False) and the SetLevelOnly face-host (no offset → the
            // head sat at the floor, the bboxZ=-0.0 we saw).
            SidewallWallHost.Result r = SidewallWallHost.Place(doc, symbol, level, point, inward);
            if (r.Placed)
            {
                // A face host takes facing from the wall face — flip it to face into the room; the
                // level/sketch-plane paths already rotated to the throw inside the helper.
                if (r.How == SidewallWallHost.Strategy.Face)
                    OrientSidewall(doc, r.Instance!, point, throwRadians, inwardPoint, faceHosted: true);
                TagInstance(r.Instance!, tag);
                placedId = r.Instance!.Id;
                return SidewallResult.WallHosted;
            }

            // No wall reachable (an open/vaulted area with no back wall) → free-place at the solved
            // point/Z so the head still lands where the plan drew it (review-required, may need
            // manual orientation). A strictly host-requiring family cannot: reported as host-required.
            FamilyInstance? free =
                PlacementHelpers.PlaceOnVerticalPlane(doc, symbol, level, point, inward)
                ?? PlacementHelpers.PlaceUnhosted(doc, symbol, level, point);
            if (free is null)
            {
                diag?.Add(trace.Append(" => no wall found; unhosted placement NULL").ToString());
                return PlacementHelpers.RequiresHost(symbol)
                    ? SidewallResult.FailedHostRequired : SidewallResult.Failed;
            }
            doc.Regenerate();
            OrientSidewall(doc, free, point, throwRadians, inwardPoint, faceHosted: false);
            TagInstance(free, tag);
            placedId = free.Id;
            diag?.Add(trace.Append($" => no wall found; placed UNHOSTED at solved Z ({ZReport(free)})").ToString());
            return SidewallResult.PlacedUnhosted;
        }
        catch (Exception e) { diag?.Add(trace.Append(" => EXCEPTION:" + Brief(e)).ToString()); }
        return SidewallResult.Failed;
    }

    // First line of an exception message, trimmed — enough to identify the Revit rejection
    // without dumping a stack trace into the dialog.
    private static string Brief(Exception e)
    {
        string m = e.Message?.Split('\n')[0].Trim() ?? e.GetType().Name;
        return m.Length > 80 ? m.Substring(0, 80) : m;
    }

    // Actual world Z of a placed instance. Measured from the BOUNDING BOX (real geometry),
    // NOT LocationPoint: a WorkPlaneBased/face-based sprinkler family reports a plane-relative
    // or zero LocationPoint.Z (the trace showed gotZ=-0.0 for correctly-created heads), so
    // LocationPoint would wrongly read ~0 and get the head deleted. The bbox reflects where the
    // geometry actually is. Falls back to LocationPoint only when there is no bbox.
    private static double ActualZ(FamilyInstance inst)
    {
        BoundingBoxXYZ? bb = inst.get_BoundingBox(null);
        if (bb is not null) return (bb.Min.Z + bb.Max.Z) / 2.0;
        return (inst.Location as LocationPoint)?.Point.Z is double z ? z : double.NaN;
    }

    // Both Z readings for a placed head, so the diagnostic dialog can show whether a "placed"
    // head sits at the deflector height (bbox ≈ target) or has collapsed to the building base.
    private static string ZReport(FamilyInstance inst)
    {
        double loc = (inst.Location as LocationPoint)?.Point.Z is double z ? z : double.NaN;
        return $"locZ={loc:F1} bboxZ={ActualZ(inst):F1}";
    }

    /// <summary>
    /// Points a sidewall head along its throw. A wall/face-hosted head takes its facing from the
    /// host wall, so we only flip it to face INTO the room; a level-based (linked/unhosted) head
    /// has no host to derive facing from, so we rotate it about Z to the solved throw direction.
    /// </summary>
    private static void OrientSidewall(Document doc, FamilyInstance inst, XYZ point, double throwRadians, XYZ inwardPoint, bool faceHosted)
    {
        // A real WALL host, or a FACE host (own or linked wall face), fixes facing from the face
        // normal — only flip it to face INTO the room. A work-plane (sketch-plane) or level instance
        // has no wall/face to derive facing from, so we rotate it about Z to the solved throw.
        if (faceHosted || inst.Host is Wall)
        {
            PlacementHelpers.OrientTowardRoom(doc, inst, inwardPoint);
            return;
        }
        try
        {
            doc.Regenerate();
            XYZ facing = inst.FacingOrientation;
            double current = Math.Atan2(facing.Y, facing.X);
            double delta = throwRadians - current;
            if (Math.Abs(delta) > 1e-6)
            {
                Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, inst.Id, axis, delta);
            }
        }
        catch { /* best-effort orientation */ }
    }

    /// <summary>Deflector drop below the finished ceiling for a sidewall head (feet).</summary>
    private const double SidewallDropFt = 0.5; // ~6 in, mid of NFPA 13's 4–12 in band

    /// <summary>Fallback deflector height above the level when no ceiling is found (feet).</summary>
    private const double SidewallFallbackFt = 8.0; // ~1 ft below a nominal 9 ft ceiling

    /// <summary>How far behind a solved head to look for its host wall (feet). The trace showed the
    /// failing heads had NO wall within the old 3 ft (found=False, prox=0) — a deep-room head thrown
    /// from an opposite-wall row, or a solved point standing well off the wall, sits further back
    /// than that. Ten feet reaches the real back wall (the ray is anti-throw, so it points at the
    /// head's OWN wall, not across the room) while still being "the wall behind this head".</summary>
    private const double WallProbeFt = 10.0;

    /// <summary>
    /// Height above the level at which to shoot the horizontal wall-finding ray (feet). Kept at
    /// mid-wall so the ray stays INSIDE any wall — a partition wall may stop below the deflector
    /// Z (its top is under the slab/deck the ceiling ray found), and a ray at that higher Z would
    /// pass over the wall and find nothing. A wall's plan position is height-independent, so we
    /// probe low here and place the head at the deflector Z separately.
    /// </summary>
    private const double WallProbeZFt = 4.0;

    /// <summary>How far a manual click may be from a wall and still snap a sidewall throw to it (feet).</summary>
    private const double MaxSidewallSnapFt = 40.0;

    /// <summary>
    /// World Z for a sidewall deflector. Ray-casts UP from a point just inside the room
    /// to the REAL modeled ceiling (own or linked model, via <see cref="HostFinder"/>) and
    /// sits the deflector <see cref="SidewallDropFt"/> below it. Falls back to a stable
    /// <see cref="SidewallFallbackFt"/> above the level when nothing is overhead — NEVER
    /// uses <c>Room.UnboundedHeight</c>, which is the floor-to-floor volume height (often
    /// the slab above), not the finished ceiling.
    /// </summary>
    private static double SidewallDeflectorZ(HostFinder finder, double sampleX, double sampleY, Level level)
    {
        HostHit hit = finder.FindAbove(sampleX, sampleY, level.Elevation);
        return hit.Found ? hit.HitZ - SidewallDropFt : level.Elevation + SidewallFallbackFt;
    }

    private static View3D? GetOrCreate3DView(Document doc)
    {
        View3D? existing = new FilteredElementCollector(doc)
            .OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate);
        if (existing is not null) return existing;

        ViewFamilyType? vft = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
        return vft is null ? null : View3D.CreateIsometric(doc, vft.Id);
    }
}
