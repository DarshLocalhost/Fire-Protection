
using System.Collections.Generic;

namespace FireProtection.UI.ViewModels.Sprinklers.BruteForce
{
    /// <summary>
    /// Revit-free UI representation of a sprinkler family and its available types.
    /// </summary>
    public class SprinklerFamilyOption
    {
        public string FamilyName { get; set; }

        public IReadOnlyList<SprinklerTypeOption> Types { get; set; }

        public SprinklerFamilyOption()
        {
            Types = new List<SprinklerTypeOption>();
        }

        public override string ToString()
        {
            return FamilyName ?? string.Empty;
        }
    }
}