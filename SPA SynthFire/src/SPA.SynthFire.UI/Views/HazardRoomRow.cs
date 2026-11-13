using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using SPA.SynthFire.Core.Standards;

namespace SPA.SynthFire.UI.Views;

/// <summary>
/// Plain data handed to the Hazard Classification window for one room: the room's
/// identity, the auto-suggested hazard tier + matched keywords, whether it needs
/// review, and any hazard already stored on the room.
/// </summary>
public sealed record HazardRoomSeed(
    string RoomId,
    string RoomName,
    string LevelName,
    HazardTier SuggestedTier,
    string MatchedKeywords,
    bool ReviewRequired,
    string? ExistingHazardId);

/// <summary>
/// One editable row in the hazard grid: fixed room facts plus the user's assigned
/// hazard class (bindable). The assigned value defaults to the auto-suggested class
/// (or the room's existing stored class), and the user can change it per row.
/// </summary>
public sealed class HazardRoomRow : INotifyPropertyChanged
{
    public string RoomId { get; }
    public string RoomName { get; }
    public string LevelName { get; }
    public bool ReviewRequired { get; }
    public string SuggestedText { get; }

    /// <summary>Hazard classes selectable in this row (the active standard's set).</summary>
    public IReadOnlyList<HazardClassification> HazardOptions { get; }

    private HazardClassification _assigned;
    public HazardClassification Assigned
    {
        get => _assigned;
        set { if (!Equals(_assigned, value)) { _assigned = value; OnChanged(); } }
    }

    public HazardRoomRow(
        HazardRoomSeed seed,
        IReadOnlyList<HazardClassification> options,
        HazardClassification suggested,
        HazardClassification? existing)
    {
        RoomId = seed.RoomId;
        RoomName = seed.RoomName;
        LevelName = seed.LevelName;
        ReviewRequired = seed.ReviewRequired;
        HazardOptions = options;

        string keywords = seed.MatchedKeywords.Length > 0 ? seed.MatchedKeywords : "no keyword — default";
        SuggestedText = $"{suggested.Id} ({keywords})" + (seed.ReviewRequired ? "  ⚠ review" : "");

        // Prefer a hazard already stored on the room; otherwise the auto-suggestion.
        _assigned = existing ?? suggested;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
