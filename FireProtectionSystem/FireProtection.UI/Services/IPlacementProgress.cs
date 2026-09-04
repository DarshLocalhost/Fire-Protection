using System;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Revit-free progress + cancellation channel for a placement run. The Backend reports per room and
    /// checks <see cref="IsCancellationRequested"/> between rooms; the UI implementation updates the status
    /// bar and pumps the dispatcher so the window stays repainted while Revit works on the same thread.
    /// </summary>
    public interface IPlacementProgress
    {
        /// <summary>True once the user pressed Cancel. Checked between rooms, never mid-element.</summary>
        bool IsCancellationRequested { get; }

        /// <summary>Reports that <paramref name="completed"/> of <paramref name="total"/> rooms are done.</summary>
        void Report(int completed, int total, string label);
    }

    /// <summary>No-op progress for callers that do not care (tests, batch use).</summary>
    public sealed class NullPlacementProgress : IPlacementProgress
    {
        public static readonly NullPlacementProgress Instance = new NullPlacementProgress();

        public bool IsCancellationRequested { get { return false; } }

        public void Report(int completed, int total, string label) { }
    }

    /// <summary>
    /// What to do about devices that already exist in a room, i.e. what a second Place run does.
    /// Skip is the default: re-running the tool must not silently double up devices.
    /// </summary>
    public enum ExistingDevicePolicy
    {
        /// <summary>Leave rooms that already contain a device of this kind untouched.</summary>
        SkipRoom = 0,

        /// <summary>Place anyway, adding to what is already there (coincident points are still skipped).</summary>
        AddAnyway = 1,

        /// <summary>Delete the existing devices inside the room, then place the new set.</summary>
        ReplaceExisting = 2
    }

    public static class ExistingDevicePolicyOptions
    {
        public const string SkipRoomLabel = "Skip rooms that already have devices";
        public const string AddAnywayLabel = "keep existing";
        public const string ReplaceExistingLabel = "Replace existing devices in the room";

        public static readonly string[] Labels =
        {
            SkipRoomLabel,
            AddAnywayLabel,
            ReplaceExistingLabel
        };

        /// <summary>Single tooltip describing all three options, shown on the policy combo.</summary>
        public const string Explanation =
            "What a second Place run does with rooms that already contain devices of this kind:\n"
            + "• Skip - leave those rooms untouched (default; re-running never doubles up devices).\n"
            + "• Add anyway - place in addition to what is there (coincident points are still skipped).\n"
            + "• Replace - delete the existing devices in the room first (asks for confirmation).";

        public static ExistingDevicePolicy Parse(string label)
        {
            if (string.Equals(label, AddAnywayLabel, StringComparison.OrdinalIgnoreCase))
                return ExistingDevicePolicy.AddAnyway;
            if (string.Equals(label, ReplaceExistingLabel, StringComparison.OrdinalIgnoreCase))
                return ExistingDevicePolicy.ReplaceExisting;
            return ExistingDevicePolicy.SkipRoom;
        }
    }
}
