using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Result of searching upward for a ceiling/soffit above a plan point.
/// <para><see cref="Face"/> is a HOSTABLE reference in the ACTIVE document (host the
/// family to it). It is null when the nearest surface is in a LINKED model — a linked
/// face cannot host a family (Revit limitation), so only its <see cref="HitZ"/> is
/// usable: place the device level-based at that real elevation so it sits at the
/// ceiling instead of floating in the void.</para>
/// </summary>
public readonly struct HostHit
{
    /// <summary>Hostable face in the active document, or null (no hit, or a linked hit).</summary>
    public Reference? Face { get; init; }

    /// <summary>The linked-model face reference when the nearest surface is in a link, else null.
    /// A linked wall/ceiling cannot host a Revit instance by ELEMENT, but a face-based
    /// (WorkPlaneBased) family CAN host to its FACE via this linked reference — the robust way to
    /// land a sidewall head against architectural walls that live in a link.</summary>
    public Reference? LinkedFace { get; init; }

    /// <summary>The element the face belongs to (active doc), for element-hosting fallbacks.</summary>
    public ElementId ElementId { get; init; }

    /// <summary>World Z (feet) of the surface that was hit — real ceiling elevation.</summary>
    public double HitZ { get; init; }

    /// <summary>Distance (feet) from the ray origin to the surface that was hit.</summary>
    public double Proximity { get; init; }

    /// <summary>True when the nearest surface is in a linked model (Face is null, HitZ valid).</summary>
    public bool IsLinked { get; init; }

    /// <summary>True when any surface was found within range.</summary>
    public bool Found { get; init; }
}

/// <summary>
/// Finds the real overhead surface (ceiling, floor slab above, roof, or structural
/// beam) above a plan point — in the active document AND in linked (architectural)
/// models — so devices host to, or at least sit at, the actual ceiling plane rather
/// than free-placing at a fabricated height and floating with Host: None.
///
/// A single multi-category <see cref="ReferenceIntersector"/> is built once per run
/// and reused for every point (a ray per point is what makes large models crawl).
/// </summary>
public sealed class HostFinder
{
    /// <summary>
    /// Surfaces above a point up to this far (feet) count as its ceiling. Generous
    /// enough for tall-but-real ceilings; beyond it we fall back to the room's own
    /// computed ceiling elevation rather than throwing the device into an atrium roof.
    /// </summary>
    public const double MaxSearchFt = 30.0;

    // Categories that can act as an overhead host, in the order we prefer them when a
    // ray reports several: a finished ceiling first, then the structural soffit
    // (floor slab above / roof), then framing.
    private static readonly IList<BuiltInCategory> HostCategories = new List<BuiltInCategory>
    {
        BuiltInCategory.OST_Ceilings,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Roofs,
        BuiltInCategory.OST_StructuralFraming,
    };

    // Walls act as the host for sidewall heads. Kept separate from the overhead categories
    // so a horizontal wall ray never reports a ceiling/floor.
    private static readonly IList<BuiltInCategory> WallCategories = new List<BuiltInCategory>
    {
        BuiltInCategory.OST_Walls,
    };

    private readonly ReferenceIntersector? _intersector;

    private HostFinder(ReferenceIntersector? intersector) => _intersector = intersector;

    /// <summary>Builds a finder over the given (or a created) 3D view. Returns a finder
    /// that always reports "not found" when no 3D view is available, so callers can use
    /// it unconditionally and just take the fallback path.</summary>
    public static HostFinder Create(View3D? view)
    {
        if (view is null) return new HostFinder(null);
        var filter = new ElementMulticategoryFilter(HostCategories);
        var ri = new ReferenceIntersector(filter, FindReferenceTarget.Face, view)
        {
            // Architectural ceilings usually live in a linked model, not the MEP model
            // this add-in runs in — search links too so those points still find a host Z.
            FindReferencesInRevitLinks = true,
        };
        return new HostFinder(ri);
    }

    /// <summary>
    /// Builds a finder that looks for WALLS (host and linked). Used to locate the wall a
    /// sidewall head sits against by shooting a horizontal ray from the head — reliable even
    /// on a shared/corner wall, where a nearest-boundary search would snap to the wrong wall.
    /// </summary>
    public static HostFinder CreateWallFinder(View3D? view)
    {
        if (view is null) return new HostFinder(null);
        var filter = new ElementMulticategoryFilter(WallCategories);
        var ri = new ReferenceIntersector(filter, FindReferenceTarget.Face, view)
        {
            FindReferencesInRevitLinks = true,
        };
        return new HostFinder(ri);
    }

    /// <summary>
    /// Shoots straight up from just above the floor at (x, y) and returns the nearest
    /// overhead surface within <see cref="MaxSearchFt"/>. <paramref name="floorZ"/> is
    /// the level elevation; the ray starts a little above it to clear the floor itself.
    /// </summary>
    public HostHit FindAbove(double x, double y, double floorZ)
    {
        if (_intersector is null) return default;

        ReferenceWithContext? hit = _intersector.FindNearest(
            new XYZ(x, y, floorZ + 0.5), XYZ.BasisZ);
        if (hit is null || hit.Proximity > MaxSearchFt) return default;

        Reference reference = hit.GetReference();
        bool linked = reference.LinkedElementId != ElementId.InvalidElementId;
        return new HostHit
        {
            Face = linked ? null : reference,
            LinkedFace = linked ? reference : null,
            ElementId = linked ? ElementId.InvalidElementId : reference.ElementId,
            HitZ = reference.GlobalPoint.Z,
            Proximity = hit.Proximity,
            IsLinked = linked,
            Found = true,
        };
    }

    /// <summary>
    /// Shoots a HORIZONTAL ray from (x, y, z) along (dirX, dirY) and returns the nearest wall
    /// within <paramref name="maxFt"/>. A sidewall head casts this in the direction opposite its
    /// throw to find the wall behind it. An own-model hit exposes a hostable <see cref="HostHit.Face"/>
    /// and <see cref="HostHit.ElementId"/>; a linked hit reports Found with a null Face (a linked
    /// wall cannot host a Revit instance), so the caller places the head level-based instead.
    /// </summary>
    public HostHit FindAlong(double x, double y, double z, double dirX, double dirY, double maxFt)
    {
        if (_intersector is null) return default;
        var dir = new XYZ(dirX, dirY, 0);
        if (dir.IsZeroLength()) return default;

        ReferenceWithContext? hit = _intersector.FindNearest(new XYZ(x, y, z), dir.Normalize());
        if (hit is null || hit.Proximity > maxFt) return default;

        Reference reference = hit.GetReference();
        bool linked = reference.LinkedElementId != ElementId.InvalidElementId;
        return new HostHit
        {
            Face = linked ? null : reference,
            LinkedFace = linked ? reference : null,
            ElementId = linked ? ElementId.InvalidElementId : reference.ElementId,
            HitZ = reference.GlobalPoint.Z,
            Proximity = hit.Proximity,
            IsLinked = linked,
            Found = true,
        };
    }
}
