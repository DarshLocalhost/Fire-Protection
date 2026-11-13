using System;

namespace FireProtection.Backend.Services.Placement.NotificationAppliances.Final.BruteForce
{
    /// <summary>
    /// Implements NFPA 72 (National Fire Alarm and Signaling Code) Chapter 18 engineering rules
    /// for visible notification appliances (Candela ratings & corridor spacing) and audible notification
    /// appliances (inverse square law sound attenuation & ambient noise thresholds).
    /// </summary>
    public static class Nfpa72AudibleVisualSolver
    {
        /// <summary>
        /// Reference distance for audible appliance decibel ratings (10 ft / ~3.05 m per UL 464 / NFPA 72 §18.4).
        /// </summary>
        public const double ReferenceDistanceFt = 10.0;

        /// <summary>
        /// Default design ambient sound level in public mode spaces (commercial/office, 55 dBA).
        /// </summary>
        public const double DefaultAmbientDba = 55.0;

        /// <summary>
        /// NFPA 72 §18.4.3.1 required sound level above average ambient noise in public mode (+15 dBA).
        /// </summary>
        public const double RequiredDbaAboveAmbient = 15.0;

        /// <summary>
        /// Standard minimum audible sound level target for public spaces (DefaultAmbientDba + RequiredDbaAboveAmbient = 70 dBA).
        /// </summary>
        public const double DefaultAudibleTargetDba = DefaultAmbientDba + RequiredDbaAboveAmbient; // 70.0 dBA

        #region Visible Notification (NFPA 72 §18.5)

        /// <summary>
        /// Returns the minimum required candela rating for a single wall-mounted visible appliance
        /// in a room of given maximum dimension, according to NFPA 72 Table 18.5.5.4.1(a).
        /// </summary>
        public static int GetRequiredCandelaForSingleWallAppliance(double maxRoomDimensionFt)
        {
            if (maxRoomDimensionFt <= 20.0) return 15;
            if (maxRoomDimensionFt <= 30.0) return 30;
            if (maxRoomDimensionFt <= 40.0) return 60;
            if (maxRoomDimensionFt <= 50.0) return 95;
            if (maxRoomDimensionFt <= 60.0) return 135;
            return 185;
        }

        /// <summary>
        /// Returns the maximum room dimension (length or width of a square/rectangular room)
        /// that can be covered by a single visible appliance with the specified candela rating,
        /// per NFPA 72 Table 18.5.5.4.1(a).
        /// </summary>
        public static double GetMaxRoomDimensionForCandela(int candela)
        {
            if (candela < 15) return 0.0;
            if (candela < 30) return 20.0;
            if (candela < 60) return 30.0;
            if (candela < 95) return 40.0;
            if (candela < 135) return 50.0;
            if (candela < 185) return 60.0;
            return 70.0;
        }

        /// <summary>
        /// Evaluates whether a space classifies as a corridor under NFPA 72 §18.5.5.5
        /// (width up to 20 ft, elongated aspect ratio >= 2.5:1).
        /// </summary>
        public static bool IsCorridor(double widthFt, double lengthFt)
        {
            double minDim = Math.Min(widthFt, lengthFt);
            double maxDim = Math.Max(widthFt, lengthFt);
            if (minDim <= 0.0 || maxDim <= 0.0) return false;

            // NFPA 72 §18.5.5.5: Corridors are 20 ft or less in width.
            if (minDim > 20.0) return false;

            // Aspect ratio threshold for a true corridor
            return (maxDim / minDim) >= 2.5;
        }

        /// <summary>
        /// Returns standard corridor visible appliance spacing per NFPA 72 §18.5.5.5:
        /// Maximum 100 ft spacing between 15 cd strobes, with appliances placed <= 15 ft from corridor ends.
        /// </summary>
        public static double GetCorridorMaxSpacingFt()
        {
            return 100.0;
        }

        /// <summary>
        /// Returns standard corridor end buffer distance per NFPA 72 §18.5.5.5 (15 ft).
        /// </summary>
        public static double GetCorridorEndOffsetFt()
        {
            return 15.0;
        }

        #endregion

        #region Audible Notification (NFPA 72 §18.4)

        /// <summary>
        /// Calculates the audible sound level at a given distance using the free-field inverse square law
        /// (NFPA 72 Annex A §A.18.4.1.2):
        /// Lp(d) = Lp(d0) - 20 * log10(d / d0).
        /// Drops approx 6.02 dBA per doubling of distance.
        /// </summary>
        /// <param name="ratedDbaAt10Ft">Sound rating in dBA at reference distance (10 ft / 3.05 m).</param>
        /// <param name="distanceFt">Distance from appliance in feet.</param>
        /// <returns>Sound pressure level in dBA at distance.</returns>
        public static double CalculateAudibleAttenuation(double ratedDbaAt10Ft, double distanceFt)
        {
            if (ratedDbaAt10Ft <= 0.0) return 0.0;
            if (distanceFt <= 0.0) return ratedDbaAt10Ft;

            // If closer than reference distance 10 ft, cap at rated dBA
            if (distanceFt <= ReferenceDistanceFt)
            {
                return ratedDbaAt10Ft;
            }

            double lossDba = 20.0 * Math.Log10(distanceFt / ReferenceDistanceFt);
            return Math.Max(0.0, ratedDbaAt10Ft - lossDba);
        }

        /// <summary>
        /// Calculates the maximum audible throw distance in feet before the sound level
        /// drops below the required target dBA (e.g. ambient + 15 dBA).
        /// d_max = d0 * 10^((L_rated - L_target) / 20)
        /// </summary>
        /// <param name="ratedDbaAt10Ft">Sound rating in dBA at 10 ft.</param>
        /// <param name="targetDba">Target minimum audible sound level (default 70 dBA).</param>
        /// <returns>Maximum coverage radius / throw distance in feet.</returns>
        public static double GetMaxAudibleThrowFt(double ratedDbaAt10Ft, double targetDba = DefaultAudibleTargetDba)
        {
            if (ratedDbaAt10Ft <= 0.0) return 0.0;
            if (ratedDbaAt10Ft <= targetDba)
            {
                // Appliance cannot even meet the target at 10 ft
                return 0.0;
            }

            double allowableLossDba = ratedDbaAt10Ft - targetDba;
            double distanceFactor = Math.Pow(10.0, allowableLossDba / 20.0);
            return ReferenceDistanceFt * distanceFactor;
        }

        #endregion
    }
}
