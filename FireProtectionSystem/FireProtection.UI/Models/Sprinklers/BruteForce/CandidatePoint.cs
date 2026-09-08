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

        /// <summary>
        /// Index of the polygon edge this candidate was generated from (sidewall only).
        /// The placement layer uses this to find the wall face to host on.
        /// Null for ceiling-grid candidates.
        /// </summary>
        public int? WallEdgeIndex { get; set; }

        public List<string> RejectionReasons { get; set; }

        public CandidatePoint()
        {
            RejectionReasons = new List<string>();
        }
    }
}
