using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using SPA.SynthFire.UI.Views;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Tags rooms with a hazard class (written to each room's Comments as
/// "SPA Hazard: standardId/hazardId"), via an ExternalEvent so Revit stays
/// interactive. Applies a per-room map (roomId → hazardId) built in the UI.
/// </summary>
public sealed class HazardClassificationRunner : IHazardRunner
{
    private readonly ExternalEvent _event;
    private readonly Handler _handler;

    public HazardClassificationRunner(HazardClassificationWindow window)
    {
        _handler = new Handler(window);
        _event = ExternalEvent.Create(_handler);
    }

    public void RequestApply(string standardId, IReadOnlyDictionary<string, string> roomHazards)
    {
        _handler.StandardId = standardId;
        _handler.RoomHazards = roomHazards;
        _event.Raise();
    }

    private sealed class Handler : IExternalEventHandler
    {
        private readonly HazardClassificationWindow _window;
        public string StandardId = "";
        public IReadOnlyDictionary<string, string> RoomHazards = new Dictionary<string, string>();

        public Handler(HazardClassificationWindow window) => _window = window;

        public string GetName() => "SPA SynthFire — Hazard Classification";

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc is null) { _window.SetStatus("Open a project first."); return; }
                Document doc = uiDoc.Document;

                if (RoomHazards.Count == 0) { _window.SetStatus("No rooms to classify."); return; }

                int tagged = 0, skipped = 0;
                using (var tx = new Transaction(doc, "SPA SynthFire — Hazard Classification"))
                {
                    tx.Start();
                    foreach (var (roomId, hazardId) in RoomHazards)
                    {
                        if (!long.TryParse(roomId, out long idVal) ||
                            doc.GetElement(new ElementId(idVal)) is not Room room)
                        {
                            skipped++;
                            continue;
                        }

                        Parameter? comments = room.LookupParameter("Comments");
                        if (comments is { IsReadOnly: false })
                        {
                            comments.Set($"SPA Hazard: {StandardId}/{hazardId}");
                            tagged++;
                        }
                        else skipped++;
                    }
                    tx.Commit();
                }

                _window.SetStatus(skipped == 0
                    ? $"Classified {tagged} room(s) under {StandardId}."
                    : $"Classified {tagged} room(s); skipped {skipped} (missing/read-only).");
            }
            catch (Exception ex)
            {
                _window.SetStatus("Error: " + ex.Message);
            }
        }
    }
}
