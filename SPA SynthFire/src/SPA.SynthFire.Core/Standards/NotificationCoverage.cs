using System;
using System.Collections.Generic;
using System.Linq;

namespace SPA.SynthFire.Core.Standards;

/// <summary>
/// NFPA 72 notification-appliance coverage math, Revit-free and unit-tested.
///
/// VISIBLE (strobe): Table 18.5.5.5.1(a) — Room Spacing for Wall-Mounted Visual
/// Notification Appliances. One appliance per wall, located at the halfway point of
/// the wall; the candela rating sets the largest SQUARE room a single appliance covers.
///
/// AUDIBLE (horn): §18.4.4 public mode — the signal must be ≥15 dBA above the average
/// ambient sound level (or ≥5 dBA above the 60 s maximum, whichever is greater). Sound
/// falls off by the inverse-square law: ~6 dBA per doubling of distance from the source.
///
/// PROVISIONAL: these encode published tables/relationships, not an FPE-verified design.
/// Ambient levels, wall/ceiling reflections, obstructions and the specific appliance
/// listing all matter — every notification layout stays review-required.
/// </summary>
public static class NotificationCoverage
{
    /// <summary>
    /// Table 18.5.5.5.1(a): (maximum square room side in feet, minimum effective
    /// intensity in candela) for a single wall-mounted visual appliance, ascending.
    /// </summary>
    public static readonly IReadOnlyList<(double MaxRoomSideFt, double Candela)> WallVisibleTable = new[]
    {
        (20.0, 15.0), (28.0, 30.0), (30.0, 34.0), (40.0, 60.0), (45.0, 75.0),
        (50.0, 94.0), (54.0, 110.0), (55.0, 115.0), (60.0, 135.0), (63.0, 150.0),
        (68.0, 177.0), (70.0, 184.0), (80.0, 240.0), (90.0, 304.0), (100.0, 375.0),
        (110.0, 455.0), (120.0, 540.0), (130.0, 635.0),
    };

    /// <summary>
    /// Largest square room side (ft) a single wall-mounted appliance of the given
    /// candela covers, per Table 18.5.5.5.1(a). Returns the side of the highest table
    /// row whose required candela is ≤ <paramref name="candela"/>; 0 if below the
    /// smallest listed rating (15 cd).
    /// </summary>
    public static double MaxRoomSideForCandela(double candela)
    {
        double side = 0.0;
        foreach (var (maxSide, cd) in WallVisibleTable)
            if (cd <= candela + 1e-9) side = maxSide;
        return side;
    }

    /// <summary>
    /// Minimum listed candela rating whose covered room size is ≥ the room's larger plan
    /// dimension, per Table 18.5.5.5.1(a). Null when the room is larger than the biggest
    /// listed single-appliance coverage (130 ft) — such a room must be subdivided
    /// (<see cref="WallAppliancesForRoom"/>).
    /// </summary>
    public static double? MinCandelaForRoom(double lengthFt, double widthFt)
    {
        double longest = Math.Max(lengthFt, widthFt);
        foreach (var (maxSide, cd) in WallVisibleTable)
            if (maxSide >= longest - 1e-9) return cd;
        return null;
    }

    /// <summary>
    /// Number of wall-mounted visual appliances a rectangular room needs at the given
    /// candela, subdividing each plan dimension into spans no larger than the appliance's
    /// covered room side (Table 18.5.5.5.1(a) with the large-room subdivision of
    /// 18.5.5.5.5). Always ≥1 for a positive room; 0 if the candela is below the table
    /// (cannot cover any room).
    /// </summary>
    public static int WallAppliancesForRoom(double lengthFt, double widthFt, double candela)
    {
        double side = MaxRoomSideForCandela(candela);
        if (side <= 0 || lengthFt <= 0 || widthFt <= 0) return 0;
        int cols = Math.Max(1, (int)Math.Ceiling(lengthFt / side - 1e-9));
        int rows = Math.Max(1, (int)Math.Ceiling(widthFt / side - 1e-9));
        return cols * rows;
    }

    /// <summary>
    /// Public-mode required sound level (dBA) at the listener: the greater of
    /// 15 dBA above the average ambient and 5 dBA above the 60 s maximum ambient
    /// (§18.4.4.1). Pass the maximum as ≤ average to ignore that leg.
    /// </summary>
    public static double RequiredPublicModeDba(double averageAmbientDba, double maxAmbientDba)
        => Math.Max(averageAmbientDba + 15.0, maxAmbientDba + 5.0);

    /// <summary>
    /// Free-field audible coverage radius (ft) at which an appliance rated
    /// <paramref name="ratedDbaAt10Ft"/> (the UL reverberant rating, taken at 10 ft) still
    /// delivers <paramref name="requiredDba"/>, by the inverse-square law
    /// (−6 dBA per doubling of distance): r = 10·10^((rated − required)/20).
    /// Clamped to ≥0; returns 0 when the appliance cannot meet the requirement even at 10 ft.
    /// </summary>
    public static double AudibleRadiusFt(double ratedDbaAt10Ft, double requiredDba)
    {
        if (ratedDbaAt10Ft < requiredDba) return 0.0;
        return 10.0 * Math.Pow(10.0, (ratedDbaAt10Ft - requiredDba) / 20.0);
    }
}
