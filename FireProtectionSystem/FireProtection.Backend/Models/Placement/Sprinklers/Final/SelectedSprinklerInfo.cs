using Newtonsoft.Json;

namespace FireProtection.Backend.Models.Placement.Sprinklers.Final
{
    /// <summary>
    /// Captures the user-selected sprinkler family and type.
    /// </summary>
    public class SelectedSprinklerInfo
    {
        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        [JsonProperty("typeName")]
        public string TypeName { get; set; }

        public SelectedSprinklerInfo() { }

        public SelectedSprinklerInfo(string familyName, string typeName)
        {
            FamilyName = familyName;
            TypeName = typeName;
        }
    }
}
