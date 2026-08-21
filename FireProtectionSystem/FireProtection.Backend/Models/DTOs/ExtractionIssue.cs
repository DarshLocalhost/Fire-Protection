using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public enum ExtractionIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public class ExtractionIssue
    {
        [JsonProperty("severity")]
        public string Severity { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("elementId")]
        public string ElementId { get; set; }

        [JsonProperty("elementName")]
        public string ElementName { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        public ExtractionIssue() { }

        public ExtractionIssue(ExtractionIssueSeverity severity, string category, string message, string elementId = null, string elementName = null)
        {
            Severity = severity.ToString();
            Category = category;
            Message = message;
            ElementId = elementId;
            ElementName = elementName;
        }
    }

    public class ValidationSummary
    {
        [JsonProperty("levelsExtracted")]
        public int LevelsExtracted { get; set; }

        [JsonProperty("roomsExtracted")]
        public int RoomsExtracted { get; set; }

        [JsonProperty("roomsWithBoundaries")]
        public int RoomsWithBoundaries { get; set; }

        [JsonProperty("roomsMissingBoundaries")]
        public int RoomsMissingBoundaries { get; set; }

        [JsonProperty("ceilingsResolved")]
        public int CeilingsResolved { get; set; }

        [JsonProperty("roomsWithoutCeilings")]
        public int RoomsWithoutCeilings { get; set; }

        [JsonProperty("linkedModelsFound")]
        public int LinkedModelsFound { get; set; }

        [JsonProperty("linkedModelsLoaded")]
        public int LinkedModelsLoaded { get; set; }

        [JsonProperty("existingSprinklersExtracted")]
        public int ExistingSprinklersExtracted { get; set; }

        [JsonProperty("obstaclesExtracted")]
        public int ObstaclesExtracted { get; set; }

        [JsonProperty("warningCount")]
        public int WarningCount { get; set; }

        [JsonProperty("errorCount")]
        public int ErrorCount { get; set; }

        [JsonProperty("isValid")]
        public bool IsValid { get; set; }

        public string GetFormattedTextReport()
        {
            return $"Extraction Validation Summary:\n" +
                   $"  Levels extracted: {LevelsExtracted}\n" +
                   $"  Rooms extracted: {RoomsExtracted}\n" +
                   $"  Rooms with boundaries: {RoomsWithBoundaries}\n" +
                   $"  Rooms missing boundaries: {RoomsMissingBoundaries}\n" +
                   $"  Ceilings resolved: {CeilingsResolved}\n" +
                   $"  Rooms without ceilings: {RoomsWithoutCeilings}\n" +
                   $"  Linked models: {LinkedModelsLoaded}/{LinkedModelsFound} loaded\n" +
                   $"  Existing sprinklers: {ExistingSprinklersExtracted}\n" +
                   $"  Obstacles extracted: {ObstaclesExtracted}\n" +
                   $"  Warnings: {WarningCount}\n" +
                   $"  Errors: {ErrorCount}\n" +
                   $"  Overall Status: {(IsValid ? "VALID" : "VALID (with notices)")}";
        }
    }
}
