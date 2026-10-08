using System.Collections.Generic;
using FireProtection.Backend.Models.Hazard;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Default hazard placement rule provider. Spacing/coverage/separation values are traced to
    /// NFPA 13 (2002) as reproduced in the NFSA textbook "Layout, Detail and Calculation of Fire
    /// Sprinkler Systems" (the client-supplied source): max spacing 15 ft light/ordinary &amp; 12 ft
    /// extra (p.218), min spacing 6 ft cold-soldering (p.218), max wall distance = ½ max spacing
    /// (p.219), coverage areas Table 19-1 (p.221). They are NOT independently verified against the
    /// standard text or signed off by an FPE — and the 2002 edition itself is superseded — so
    /// HasApprovedRules stays false and every rule set is flagged IsProvisional until a qualified
    /// fire protection engineer confirms the design basis (see STANDARDS_MEMORY.md).
    /// Flip HasApprovedRules to true ONLY after that sign-off, with sourced values.
    /// </summary>
    public class DefaultHazardPlacementRules : IHazardPlacementRules
    {
        public bool HasApprovedRules => false; // Set to true after engineering review

        public HazardPlacementRuleSet GetRules(HazardClass hazardClass)
        {
            switch (hazardClass)
            {
                case HazardClass.Light:
                    return new HazardPlacementRuleSet
                    {
                        HazardClass = HazardClass.Light,
                        MaxSpacingFt = 15.0,
                        MinSpacingFt = 6.0,
                        MaxCoverageAreaSqFt = 225.0,
                        CoverageRadiusFt = 7.5,
                        ObstacleClearanceFt = 1.0,
                        BoundaryClearanceFt = 1.0,
                        ExistingSprinklerSeparationFt = 6.0,
                        MaxDistanceFromWallsFt = 7.5,
                        MinKFactor = 5.6,
                        CeilingHeightAdjustmentFactor = 1.0,
                        ObstacleSpecificClearances = new Dictionary<string, double>
                        {
                            { "beam", 1.0 },
                            { "column", 1.0 },
                            { "duct", 1.5 }
                        },
                        IsProvisional = true,
                        Notes = "Provisional Light Hazard: max spacing 15 ft, min 6 ft (p.218), max wall distance 7.5 ft = ½ spacing (p.219), max coverage 225 sq ft (Table 19-1, p.221). NFPA 13 (2002) via NFSA textbook — NOT AHJ/FPE-verified, engineering review required."
                    };

                case HazardClass.OH1:
                    return new HazardPlacementRuleSet
                    {
                        HazardClass = HazardClass.OH1,
                        MaxSpacingFt = 15.0,
                        MinSpacingFt = 6.0,
                        MaxCoverageAreaSqFt = 130.0,
                        CoverageRadiusFt = 7.5,
                        ObstacleClearanceFt = 1.0,
                        BoundaryClearanceFt = 1.0,
                        ExistingSprinklerSeparationFt = 6.0,
                        MaxDistanceFromWallsFt = 7.5,
                        MinKFactor = 5.6,
                        CeilingHeightAdjustmentFactor = 0.9,
                        ObstacleSpecificClearances = new Dictionary<string, double>
                        {
                            { "beam", 1.0 },
                            { "column", 1.0 },
                            { "duct", 1.5 }
                        },
                        IsProvisional = true,
                        Notes = "Provisional OH1: max spacing 15 ft, min 6 ft (p.218), max wall distance 7.5 ft = ½ spacing (p.219), max coverage 130 sq ft (Table 19-1 ordinary, p.221). NFPA 13 (2002) via NFSA textbook — NOT AHJ/FPE-verified, engineering review required."
                    };

                case HazardClass.OH2:
                    return new HazardPlacementRuleSet
                    {
                        HazardClass = HazardClass.OH2,
                        MaxSpacingFt = 15.0,
                        MinSpacingFt = 6.0,
                        MaxCoverageAreaSqFt = 130.0,
                        CoverageRadiusFt = 7.5,
                        ObstacleClearanceFt = 1.0,
                        BoundaryClearanceFt = 1.0,
                        ExistingSprinklerSeparationFt = 6.0,
                        MaxDistanceFromWallsFt = 7.5,
                        MinKFactor = 8.0,
                        CeilingHeightAdjustmentFactor = 0.85,
                        ObstacleSpecificClearances = new Dictionary<string, double>
                        {
                            { "beam", 1.5 },
                            { "column", 1.0 },
                            { "duct", 2.0 }
                        },
                        IsProvisional = true,
                        Notes = "Provisional OH2: max spacing 15 ft, min 6 ft (p.218), max wall distance 7.5 ft = ½ spacing (p.219), max coverage 130 sq ft (Table 19-1 ordinary, p.221; 168 noncombustible-obstructed not applied). NFPA 13 (2002) via NFSA textbook — NOT AHJ/FPE-verified, engineering review required."
                    };

                case HazardClass.EH1:
                    return new HazardPlacementRuleSet
                    {
                        HazardClass = HazardClass.EH1,
                        MaxSpacingFt = 12.0,
                        MinSpacingFt = 6.0,
                        MaxCoverageAreaSqFt = 100.0,
                        CoverageRadiusFt = 6.0,
                        ObstacleClearanceFt = 1.5,
                        BoundaryClearanceFt = 1.5,
                        ExistingSprinklerSeparationFt = 6.0,
                        MaxDistanceFromWallsFt = 6.0,
                        MinKFactor = 8.0,
                        CeilingHeightAdjustmentFactor = 0.8,
                        ObstacleSpecificClearances = new Dictionary<string, double>
                        {
                            { "beam", 2.0 },
                            { "column", 1.5 },
                            { "duct", 2.5 }
                        },
                        IsProvisional = true,
                        Notes = "Provisional EH1: max spacing 12 ft, min 6 ft (p.218), max wall distance 6 ft = ½ spacing (p.219), max coverage 100 sq ft (Table 19-1, density ≥0.25 gpm/ft²; 130 applies below that — density is hydraulic, out of scope). NFPA 13 (2002) via NFSA textbook — NOT AHJ/FPE-verified, engineering review required."
                    };

                case HazardClass.EH2:
                    return new HazardPlacementRuleSet
                    {
                        HazardClass = HazardClass.EH2,
                        MaxSpacingFt = 12.0,
                        MinSpacingFt = 6.0,
                        MaxCoverageAreaSqFt = 100.0,
                        CoverageRadiusFt = 6.0,
                        ObstacleClearanceFt = 2.0,
                        BoundaryClearanceFt = 2.0,
                        ExistingSprinklerSeparationFt = 6.0,
                        MaxDistanceFromWallsFt = 6.0,
                        MinKFactor = 11.2,
                        CeilingHeightAdjustmentFactor = 0.75,
                        ObstacleSpecificClearances = new Dictionary<string, double>
                        {
                            { "beam", 2.5 },
                            { "column", 2.0 },
                            { "duct", 3.0 }
                        },
                        IsProvisional = true,
                        Notes = "Provisional EH2: max spacing 12 ft, min 6 ft (p.218), max wall distance 6 ft = ½ spacing (p.219), max coverage 100 sq ft (Table 19-1, density ≥0.25 gpm/ft²; 130 applies below that — density is hydraulic, out of scope). NFPA 13 (2002) via NFSA textbook — NOT AHJ/FPE-verified, engineering review required."
                    };

                default:
                    return new HazardPlacementRuleSet
                    {
                        HazardClass = HazardClass.Light,
                        MaxSpacingFt = 15.0,
                        MinSpacingFt = 6.0,
                        MaxCoverageAreaSqFt = 225.0,
                        CoverageRadiusFt = 7.5,
                        ObstacleClearanceFt = 1.0,
                        BoundaryClearanceFt = 1.0,
                        ExistingSprinklerSeparationFt = 6.0,
                        MaxDistanceFromWallsFt = 7.5,
                        MinKFactor = 5.6,
                        CeilingHeightAdjustmentFactor = 1.0,
                        ObstacleSpecificClearances = new Dictionary<string, double>
                        {
                            { "beam", 1.0 },
                            { "column", 1.0 },
                            { "duct", 1.5 }
                        },
                        IsProvisional = true,
                        Notes = "Default Light Hazard applied (provisional): max spacing 15 ft, min 6 ft, wall 7.5 ft, coverage 225 sq ft. NFPA 13 (2002) via NFSA textbook — engineering review required."
                    };
            }
        }
    }
}