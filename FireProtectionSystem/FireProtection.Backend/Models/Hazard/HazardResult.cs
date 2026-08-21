namespace FireProtection.Backend.Models.Hazard
{
    public class HazardResult
    {
        public HazardClass Class { get; set; }

        public string MatchedKeyword { get; set; }

        public bool RequiresHumanReview { get; set; }

        public HazardResult(
            HazardClass @class,
            string matchedKeyword,
            bool requiresHumanReview)
        {
            Class = @class;
            MatchedKeyword = matchedKeyword;
            RequiresHumanReview = requiresHumanReview;
        }
    }
}