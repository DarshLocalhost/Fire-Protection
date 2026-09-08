using System.Collections.Generic;
using FireProtection.Backend.Models.Hazard;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// NFPA13-2022 compliant rule provider. Values are based on standard sprinkler spacing
    /// requirements for different hazard classifications. Set HasApprovedRules to true when
    /// values are confirmed by a qualified fire protection engineer.
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
                        ExistingSprinklerSeparationFt = 7.5,
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
                        Notes = "NFPA13-2022 Light Hazard spacing (15 ft max, 225 sq ft max coverage). Engineering review required."
                    };

                case HazardClass.OH1:
                    return new HazardPlacementRuleSet
                    {
                        HazardClass = HazardClass.OH1,
                        MaxSpacingFt = 12.0,
                        MinSpacingFt = 6.0,
                        MaxCoverageAreaSqFt = 130.0,
                        CoverageRadiusFt = 6.0,
                        ObstacleClearanceFt = 1.0,
                        BoundaryClearanceFt = 1.0,
                        ExistingSprinklerSeparationFt = 6.0,
                        MaxDistanceFromWallsFt = 6.0,
                        MinKFactor = 5.6,
                        CeilingHeightAdjustmentFactor = 0.9,
                        ObstacleSpecificClearances = new Dictionary<string, double>
                        {
                            { "beam", 1.0 },
                            { "column", 1.0 },
                            { "duct", 1.5 }
                        },
                        IsProvisional = true,
                        Notes = "NFPA13-2022 OH1 spacing (12 ft max, 130 sq ft max coverage). Engineering review required."
                    };

                case HazardClass.OH2:
                    return new HazardPlacementRuleSet
                    {
                        HazardClass = HazardClass.OH2,
                        MaxSpacingFt = 12.0,
                        MinSpacingFt = 5.0,
                        MaxCoverageAreaSqFt = 100.0,
                        CoverageRadiusFt = 5.0,
                        ObstacleClearanceFt = 1.0,
                        BoundaryClearanceFt = 1.0,
                        ExistingSprinklerSeparationFt = 5.0,
                        MaxDistanceFromWallsFt = 5.0,
                        MinKFactor = 8.0,
                        CeilingHeightAdjustmentFactor = 0.85,
                        ObstacleSpecificClearances = new Dictionary<string, double>
                        {
                            { "beam", 1.5 },
                            { "column", 1.0 },
                            { "duct", 2.0 }
                        },
                        IsProvisional = true,
                        Notes = "NFPA13-2022 OH2 spacing (12 ft max, 100 sq ft max coverage). Engineering review required."
                    };

                case HazardClass.EH1:
                    return new HazardPlacementRuleSet
                    {
                        HazardClass = HazardClass.EH1,
                        MaxSpacingFt = 10.0,
                        MinSpacingFt = 4.5,
                        MaxCoverageAreaSqFt = 90.0,
                        CoverageRadiusFt = 4.5,
                        ObstacleClearanceFt = 1.5,
                        BoundaryClearanceFt = 1.5,
                        ExistingSprinklerSeparationFt = 4.5,
                        MaxDistanceFromWallsFt = 4.5,
                        MinKFactor = 8.0,
                        CeilingHeightAdjustmentFactor = 0.8,
                        ObstacleSpecificClearances = new Dictionary<string, double>
                        {
                            { "beam", 2.0 },
                            { "column", 1.5 },
                            { "duct", 2.5 }
                        },
                        IsProvisional = true,
                        Notes = "NFPA13-2022 EH1 spacing (10 ft max, 90 sq ft max coverage). Engineering review required."
                    };

                case HazardClass.EH2:
                    return new HazardPlacementRuleSet
                    {
                        HazardClass = HazardClass.EH2,
                        MaxSpacingFt = 10.0,
                        MinSpacingFt = 4.5,
                        MaxCoverageAreaSqFt = 90.0,
                        CoverageRadiusFt = 4.5,
                        ObstacleClearanceFt = 2.0,
                        BoundaryClearanceFt = 2.0,
                        ExistingSprinklerSeparationFt = 4.5,
                        MaxDistanceFromWallsFt = 4.5,
                        MinKFactor = 11.2,
                        CeilingHeightAdjustmentFactor = 0.75,
                        ObstacleSpecificClearances = new Dictionary<string, double>
                        {
                            { "beam", 2.5 },
                            { "column", 2.0 },
                            { "duct", 3.0 }
                        },
                        IsProvisional = true,
                        Notes = "NFPA13-2022 EH2 spacing (10 ft max, 90 sq ft max coverage). Engineering review required."
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
                        ExistingSprinklerSeparationFt = 7.5,
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
                        Notes = "Default Light Hazard spacing applied. Engineering review required."
                    };
            }
        }
    }
}