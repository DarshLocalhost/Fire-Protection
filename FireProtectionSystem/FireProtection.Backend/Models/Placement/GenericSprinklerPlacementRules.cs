namespace FireProtection.Backend.Models.Placement
{
    /// <summary>
    /// PROVISIONAL / GENERIC rule set. Values are placeholders used to build
    /// the placement architecture. They DO NOT represent final NFPA-compliant
    /// design values and MUST be replaced with the senior-provided rules
    /// in a later phase.
    /// </summary>
    public class GenericSprinklerPlacementRules : SprinklerPlacementRules
    {
        /// <summary>Minimum room area (sq ft) to be considered for sprinkler placement in this generic phase.</summary>
        public double MinRoomAreaSqFt { get; set; }

        /// <summary>Minimum ceiling height (ft) to be considered eligible under generic rules.</summary>
        public double MinCeilingHeightFt { get; set; }

        public override bool IsRoomEligible(
            double areaSqFt,
            double? ceilingHeightFt,
            string effectiveHazardClass,
            out string reason)
        {
            if (areaSqFt < MinRoomAreaSqFt)
            {
                reason = "Room area is below the generic minimum ("
                         + MinRoomAreaSqFt.ToString("F2") + " sq ft).";
                return false;
            }

            if (ceilingHeightFt.HasValue && ceilingHeightFt.Value < MinCeilingHeightFt)
            {
                reason = "Ceiling height is below the generic minimum ("
                         + MinCeilingHeightFt.ToString("F2") + " ft).";
                return false;
            }

            if (string.IsNullOrWhiteSpace(effectiveHazardClass))
            {
                reason = "Effective hazard class is not set.";
                return false;
            }

            reason = null;
            return true;
        }
    }
}