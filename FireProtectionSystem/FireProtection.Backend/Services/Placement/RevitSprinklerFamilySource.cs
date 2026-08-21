using Autodesk.Revit.DB;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;
using System;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Placement
{
    public class RevitSprinklerFamilySource : ISprinklerFamilySource
    {
        private readonly Document _hostDocument;
        private readonly SprinklerFamilyResolver _resolver;

        public RevitSprinklerFamilySource(Document hostDocument)
        {
            _hostDocument = hostDocument ?? throw new ArgumentNullException(nameof(hostDocument));
            _resolver = new SprinklerFamilyResolver();
        }

        public IReadOnlyList<SprinklerFamilyOption> GetAvailableFamilies()
        {
            return _resolver.GetAvailableFamilies(_hostDocument);
        }
    }
}