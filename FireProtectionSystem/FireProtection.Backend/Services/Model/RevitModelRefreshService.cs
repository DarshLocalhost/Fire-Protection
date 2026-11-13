using System;
using Autodesk.Revit.DB;
using FireProtection.Backend.Services.Extraction;
using FireProtection.UI.Services;
using Newtonsoft.Json;

namespace FireProtection.Backend.Services.Model
{
    public class RevitModelRefreshService : IModelRefreshService
    {
        private readonly Document _document;

        public RevitModelRefreshService(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public string RefreshModelSnapshot()
        {
            var extractionService = new FireProtectionExtractionService();
            var (snapshot, _) = extractionService.ExtractAndExport(_document);
            return JsonConvert.SerializeObject(snapshot, Formatting.Indented);
        }
    }
}
