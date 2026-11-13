using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace SPA.SynthFire.Revit;

/// <summary>A placed host room offered to the Hazard Classification UI.</summary>
public sealed record HazardRoomInfo(
    string RoomId, string RoomName, string LevelName, string? ExistingHazardId);

/// <summary>
/// Lists placed host rooms for the Hazard Classification window and parses any
/// hazard already stored in a room's Comments ("SPA Hazard: standard/hazard").
/// Only host rooms are listed — linked rooms live in the link document and their
/// Comments cannot be written from here.
/// </summary>
public static class HazardRoomReader
{
    public const string TagPrefix = "SPA Hazard:";

    public static IReadOnlyList<HazardRoomInfo> Read(Document doc)
    {
        var rooms = new List<HazardRoomInfo>();
        foreach (Room room in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_Rooms)
                     .WhereElementIsNotElementType()
                     .OfType<Room>()
                     .Where(r => r.Area > 0)
                     .OrderBy(r => (doc.GetElement(r.LevelId) as Level)?.Elevation ?? 0)
                     .ThenBy(r => r.Number))
        {
            string levelName = (doc.GetElement(room.LevelId) as Level)?.Name ?? "(no level)";
            string? existing = ParseExistingHazardId(
                room.LookupParameter("Comments")?.AsString());
            rooms.Add(new HazardRoomInfo(
                room.Id.Value.ToString(),
                string.IsNullOrWhiteSpace(room.Name) ? $"Room {room.Id.Value}" : room.Name,
                levelName,
                existing));
        }
        return rooms;
    }

    /// <summary>Extracts the hazard id from "SPA Hazard: standard/hazard", else null.</summary>
    public static string? ParseExistingHazardId(string? comments)
    {
        if (string.IsNullOrWhiteSpace(comments) || !comments.StartsWith(TagPrefix)) return null;
        string body = comments.Substring(TagPrefix.Length).Trim();
        int slash = body.IndexOf('/');
        return slash >= 0 && slash < body.Length - 1 ? body[(slash + 1)..].Trim() : null;
    }
}
