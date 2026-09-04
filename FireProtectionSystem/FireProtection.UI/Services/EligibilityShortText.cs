using System;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Maps a <see cref="PlacementEligibilityResult.StatusCode"/> to a short, grid-friendly label.
    /// <para>
    /// The room rows have a few characters of width for the "why", so the full
    /// <see cref="PlacementEligibilityResult.Reason"/> gets truncated to something unreadable
    /// ("Sprinkler f&#8230;"). The row shows this label instead and keeps the full reason in its tooltip:
    /// nothing is lost and nothing is clipped. Status codes are the contract here — the reason text is a
    /// human supplement and is never parsed.
    /// </para>
    /// </summary>
    public static class EligibilityShortText
    {
        /// <summary>
        /// Short label for a status code. Falls back to the first sentence of <paramref name="reason"/>
        /// for codes with no mapping, so a new code degrades to something readable instead of blank.
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
                case PlacementEligibilityStatusCodes.UnsupportedFamilyPlacement:
                case PlacementEligibilityStatusCodes.UnsupportedFamilyPlacementType:
                    return "Unsupported family";
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
