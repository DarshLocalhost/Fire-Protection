namespace FireProtection.UI.ViewModels.Sprinklers.BruteForce
{
    /// <summary>
    /// Revit-free UI representation of a single sprinkler type.
    /// </summary>
    public class SprinklerTypeOption
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }

        public override string ToString()
        {
            return TypeName ?? string.Empty;
        }
    }
}