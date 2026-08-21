using Autodesk.Revit.DB;
using FireProtection.Backend.Models.DTOs;
using System;

namespace FireProtection.Backend.Services.Model
{
    /// <summary>
    /// Encapsulates a Revit link instance, its underlying document, and its coordinate transformation.
    /// </summary>
    public class RevitLinkContext
    {
        public RevitLinkInstance LinkInstance { get; }
        public Document LinkedDocument { get; }
        public ElementId InstanceId => LinkInstance.Id;
        public string LinkName { get; }
        public string DocumentTitle => LinkedDocument != null ? LinkedDocument.Title : "<unloaded>";
        public string DocumentPath => LinkedDocument != null ? (LinkedDocument.PathName ?? string.Empty) : string.Empty;
        public bool IsLoaded => LinkedDocument != null;
        public Transform Transform { get; }
        public Transform TotalTransform { get; }

        public RevitLinkContext(RevitLinkInstance linkInstance)
        {
            LinkInstance = linkInstance ?? throw new ArgumentNullException(nameof(linkInstance));
            LinkedDocument = linkInstance.GetLinkDocument();
            LinkName = linkInstance.Name;

            Transform = linkInstance.GetTransform();
            TotalTransform = linkInstance.GetTotalTransform();
        }

        public TransformData ToTransformData()
        {
            Transform t = TotalTransform ?? Transform;
            if (t == null)
                return new TransformData();

            XYZ origin = t.Origin;
            XYZ basisX = t.BasisX;
            XYZ basisY = t.BasisY;
            XYZ basisZ = t.BasisZ;

            return new TransformData
            {
                IsIdentity = t.IsIdentity,
                Scale = t.Scale,
                Origin = new Point3DData(origin.X, origin.Y, origin.Z),
                BasisX = new Point3DData(basisX.X, basisX.Y, basisX.Z),
                BasisY = new Point3DData(basisY.X, basisY.Y, basisY.Z),
                BasisZ = new Point3DData(basisZ.X, basisZ.Y, basisZ.Z)
            };
        }

        public LinkInfo ToLinkInfo(int roomCount = 0)
        {
            return new LinkInfo
            {
#if REVIT_2024 || REVIT_2025 || REVIT_2026
                InstanceId = LinkInstance.Id.Value.ToString(),
#else
                InstanceId = LinkInstance.Id.ToString(),
#endif
                LinkName = LinkName,
                DocumentTitle = DocumentTitle,
                DocumentPath = DocumentPath,
                IsLoaded = IsLoaded,
                IsNested = false,
                RoomCount = roomCount,
                Transform = ToTransformData()
            };
        }
    }
}
