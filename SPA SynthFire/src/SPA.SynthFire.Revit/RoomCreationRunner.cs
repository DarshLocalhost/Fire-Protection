using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SPA.SynthFire.UI.Views;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Creates rooms in every enclosed region on the chosen levels, via an
/// ExternalEvent so Revit stays interactive. Uses <c>NewRooms2</c>, which places a
/// room in each closed, room-bounded region on a level.
/// </summary>
public sealed class RoomCreationRunner : IRoomCreationRunner
{
    private readonly ExternalEvent _event;
    private readonly Handler _handler;

    public RoomCreationRunner(RoomCreationWindow window)
    {
        _handler = new Handler(window);
        _event = ExternalEvent.Create(_handler);
    }

    public void RequestCreate(IReadOnlyList<string> levelIds)
    {
        _handler.LevelIds = levelIds;
        _event.Raise();
    }

    private sealed class Handler : IExternalEventHandler
    {
        private readonly RoomCreationWindow _window;
        public IReadOnlyList<string> LevelIds = Array.Empty<string>();

        public Handler(RoomCreationWindow window) => _window = window;

        public string GetName() => "SPA SynthFire — Create Rooms";

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc is null) { _window.SetStatus("Open a project first."); return; }
                Document doc = uiDoc.Document;

                int created = 0, skipped = 0, levels = 0;
                using (var tx = new Transaction(doc, "SPA SynthFire — Create Rooms"))
                {
                    WarningSwallower.AttachTo(tx);
                    tx.Start();
                    foreach (string idText in LevelIds)
                    {
                        if (!long.TryParse(idText, out long idVal)) continue;
                        if (doc.GetElement(new ElementId(idVal)) is not Level level) continue;

                        try
                        {
                            // Only fill enclosed regions that DON'T already have a room —
                            // avoids creating "Redundant Room" duplicates over existing rooms.
                            PlanTopology topo = doc.get_PlanTopology(level);
                            foreach (PlanCircuit circuit in topo.Circuits)
                            {
                                if (circuit.IsRoomLocated) { skipped++; continue; }
                                doc.Create.NewRoom(null, circuit);
                                created++;
                            }
                            levels++;
                        }
                        catch (Exception) { /* level had no plan circuits */ }
                    }
                    tx.Commit();
                }

                _window.SetStatus(created > 0
                    ? $"Created {created} rooms across {levels} level(s)" + (skipped > 0 ? $"; skipped {skipped} region(s) that already had rooms." : ".")
                    : skipped > 0
                        ? $"All enclosed regions already have rooms ({skipped} skipped)."
                        : "No enclosed regions found. Ensure the model has room-bounding walls (linked models must be set Room Bounding).");
            }
            catch (Exception ex)
            {
                _window.SetStatus("Error: " + ex.Message);
            }
        }
    }
}
