using System;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Maps a <see cref="PlacementEligibilityResult.StatusCode"/> to a short, grid-friendly label.
    /// </summary>
    public static class EligibilityShortText
    {
        /// <summary>
        /// Short label for a status code.
        /// </summary>
        public static string For(string statusCode, string reason)
        {
            switch (statusCode)
            {
                case PlacementEligibilityStatusCodes.Eligible:
                    return "Ready to place";
                case PlacementEligibilityStatusCodes.PendingFamilySelection:
                    return "Pick family/type";
                case PlacementEligibilityStatusCodes.MissingRoomGeometry:
                    return "No room geometry";
                case PlacementEligibilityStatusCodes.FamilyNotLoaded:
                    return "Family not loaded";
                case PlacementEligibilityStatusCodes.UnsupportedFamilyPlacement:
                case PlacementEligibilityStatusCodes.UnsupportedFamilyPlacementType:
                    return "Unsupported placement type";
                case PlacementEligibilityStatusCodes.BeamPathTooShort:
                    return "Beam path too short";
                case PlacementEligibilityStatusCodes.MissingHostLevel:
                    return "No host level";
                case PlacementEligibilityStatusCodes.NoCandidatePoints:
                    return "No candidate points";
                case PlacementEligibilityStatusCodes.NoUsableCeilingHost:
                    return "No ceiling host";
                case PlacementEligibilityStatusCodes.CalculationFailed:
                    return "Calculation failed";
                case PlacementEligibilityStatusCodes.PreflightError:
                    return "Preflight error";
                default:
                    return FirstSentence(reason);
            }
        }

        private static string FirstSentence(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return string.Empty;

            string trimmed = reason.Trim();
            int stop = trimmed.IndexOf('.');
            if (stop > 0) trimmed = trimmed.Substring(0, stop);

            const int maxLength = 34;
            if (trimmed.Length <= maxLength) return trimmed;
            return trimmed.Substring(0, maxLength).TrimEnd() + "…";
        }
    }
}