using SPA.SynthFire.Core.Devices;

namespace SPA.SynthFire.Revit;

/// <summary>
/// One manually placed device tracked per open coverage plan for undo / redo / click-delete.
/// <see cref="ElementId"/> is the created Revit instance id (so it can be deleted); on redo the
/// head is re-placed and this id is refreshed. <paramref name="Z"/> is the device elevation
/// for 3D visualisation (0 when unknown).
/// </summary>
internal sealed record ManualHead(
    string RoomId, double X, double Y, DeviceMount Mount, string Kind, long ElementId, double Z = 0);