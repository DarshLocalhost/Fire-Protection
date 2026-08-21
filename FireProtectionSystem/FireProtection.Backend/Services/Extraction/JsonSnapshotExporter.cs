using FireProtection.Backend.Models.DTOs;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;

namespace FireProtection.Backend.Services.Extraction
{
    public class JsonSnapshotExporter
    {
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            DateFormatString = "yyyy-MM-ddTHH:mm:ssZ"
        };

        /// <summary>
        /// Serializes the ModelSnapshot to a formatted JSON string.
        /// </summary>
        public string Serialize(ModelSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            return JsonConvert.SerializeObject(snapshot, SerializerSettings);
        }

        /// <summary>
        /// Exports the ModelSnapshot to the specified file path.
        /// </summary>
        public string ExportToFile(ModelSnapshot snapshot, string targetFilePath = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            string resolvedPath = targetFilePath;
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                resolvedPath = GetDefaultExportFilePath(snapshot);
            }

            string dir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = Serialize(snapshot);
            File.WriteAllText(resolvedPath, json);

            return resolvedPath;
        }

        /// <summary>
        /// Generates a sensible default file path in the user's Documents folder or add-in directory.
        /// </summary>
        public static string GetDefaultExportFilePath(ModelSnapshot snapshot)
        {
            string projectName = "RevitModel";
            if (snapshot?.Project?.Name != null && !string.IsNullOrWhiteSpace(snapshot.Project.Name))
            {
                projectName = SanitizeFileName(snapshot.Project.Name);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"ModelSnapshot_{projectName}_{timestamp}.json";

            // Try user documents directory first
            try
            {
                string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docsPath))
                {
                    string targetDir = Path.Combine(docsPath, "FireProtectionSystem", "Exports");
                    return Path.Combine(targetDir, fileName);
                }
            }
            catch
            {
                // Fallback
            }

            // Fallback: assembly location
            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                return Path.Combine(asmDir, fileName);
            }

            return Path.Combine(Path.GetTempPath(), fileName);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}
