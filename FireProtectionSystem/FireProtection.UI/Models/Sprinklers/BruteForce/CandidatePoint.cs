using System.Collections.Generic;

namespace FireProtection.UI.Models.Sprinklers.BruteForce
{
    /// <summary>
    /// A single BruteForce candidate evaluated during calculation.
    /// Rejected candidates retain their rejection reasons for diagnostics.
    /// </summary>
    public class CandidatePoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public bool IsValid { get; set; }

        public double Score { get; set; }

        public List<string> RejectionReasons { get; set; }

        public CandidatePoint()
        {
            RejectionReasons = new List<string>();
        }
    }
}
