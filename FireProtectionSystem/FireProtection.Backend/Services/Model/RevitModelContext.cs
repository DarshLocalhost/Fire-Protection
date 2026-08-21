using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FireProtection.Backend.Models.DTOs;
using System;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Model
{
    /// <summary>
    /// Model context that represents the active MEP host model, loaded Revit links,
    /// and provides spatial transform lookup.
    /// </summary>
    public class RevitModelContext
    {
        public Document HostDocument { get; }
        public List<RevitLinkContext> Links { get; }
        public List<RevitLinkContext> LoadedLinks { get; }
        public List<RevitLinkContext> ArchitecturalLinks { get; }

        public RevitModelContext(Document hostDocument)
        {
            HostDocument = hostDocument ?? throw new ArgumentNullException(nameof(hostDocument));
            Links = new List<RevitLinkContext>();
            LoadedLinks = new List<RevitLinkContext>();
            ArchitecturalLinks = new List<RevitLinkContext>();

            DiscoverLinks();
        }

        private void DiscoverLinks()
        {
            FilteredElementCollector linkCollector = new FilteredElementCollector(HostDocument)
                .OfClass(typeof(RevitLinkInstance));

            foreach (Element element in linkCollector)
            {
                if (element is RevitLinkInstance linkInstance)
                {
                    RevitLinkContext context = new RevitLinkContext(linkInstance);
                    Links.Add(context);

                    if (context.IsLoaded)
                    {
                        LoadedLinks.Add(context);

                        // Check if link contains rooms or architectural elements
                        if (HasRooms(context.LinkedDocument))
                        {
                            ArchitecturalLinks.Add(context);
                        }
                    }
                }
            }
        }

        private static bool HasRooms(Document document)
        {
            if (document == null) return false;
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType();

                foreach (Element elem in collector)
                {
                    if (elem is Room room && room.Area > 0)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignored
            }
            return false;
        }

        public DocumentInfo GetHostDocumentInfo()
        {
            return new DocumentInfo
            {
                Title = HostDocument.Title,
                Path = HostDocument.PathName ?? string.Empty,
                IsWorkshared = HostDocument.IsWorkshared,
                RevitVersion = HostDocument.Application.VersionNumber,
                IsHostMep = true
            };
        }

        public ModelStructureInfo GetModelStructureInfo(Dictionary<string, int> linkRoomCounts = null)
        {
            ModelStructureInfo info = new ModelStructureInfo
            {
                Host = GetHostDocumentInfo()
            };

            foreach (RevitLinkContext link in Links)
            {
                int roomCount = 0;
#if REVIT_2024 || REVIT_2025 || REVIT_2026
                string instanceId = link.InstanceId.Value.ToString();
#else
                string instanceId = link.InstanceId.ToString();
#endif
                if (linkRoomCounts != null && linkRoomCounts.TryGetValue(instanceId, out int count))
                {
                    roomCount = count;
                }
                info.Links.Add(link.ToLinkInfo(roomCount));
            }

            return info;
        }

        /// <summary>
        /// Transforms a 3D point from linked model space to canonical host MEP space.
        /// </summary>
        public static XYZ TransformPoint(XYZ point, Transform transform)
        {
            if (point == null) return null;
            if (transform == null || transform.IsIdentity) return point;
            return transform.OfPoint(point);
        }

        /// <summary>
        /// Transforms a 3D vector from linked model space to canonical host MEP space.
        /// </summary>
        public static XYZ TransformVector(XYZ vector, Transform transform)
        {
            if (vector == null) return null;
            if (transform == null || transform.IsIdentity) return vector;
            return transform.OfVector(vector);
        }

        /// <summary>
        /// Transforms a bounding box from source space into host MEP coordinates.
        /// </summary>
        public static BoundingBox3DData TransformBoundingBox(BoundingBoxXYZ bbox, Transform transform)
        {
            if (bbox == null) return null;

            XYZ min = bbox.Min;
            XYZ max = bbox.Max;

            if (transform == null || transform.IsIdentity)
            {
                return new BoundingBox3DData(
                    new Point3DData(min.X, min.Y, min.Z),
                    new Point3DData(max.X, max.Y, max.Z));
            }

            // Transform all 8 corners to determine accurate min/max in host coordinates
            XYZ[] corners = new XYZ[]
            {
                new XYZ(min.X, min.Y, min.Z),
                new XYZ(max.X, min.Y, min.Z),
                new XYZ(min.X, max.Y, min.Z),
                new XYZ(max.X, max.Y, min.Z),
                new XYZ(min.X, min.Y, max.Z),
                new XYZ(max.X, min.Y, max.Z),
                new XYZ(min.X, max.Y, max.Z),
                new XYZ(max.X, max.Y, max.Z)
            };

            double hMinX = double.MaxValue, hMinY = double.MaxValue, hMinZ = double.MaxValue;
            double hMaxX = double.MinValue, hMaxY = double.MinValue, hMaxZ = double.MinValue;

            foreach (XYZ pt in corners)
            {
                XYZ tPt = transform.OfPoint(pt);
                if (tPt.X < hMinX) hMinX = tPt.X;
                if (tPt.Y < hMinY) hMinY = tPt.Y;
                if (tPt.Z < hMinZ) hMinZ = tPt.Z;

                if (tPt.X > hMaxX) hMaxX = tPt.X;
                if (tPt.Y > hMaxY) hMaxY = tPt.Y;
                if (tPt.Z > hMaxZ) hMaxZ = tPt.Z;
            }

            return new BoundingBox3DData(
                new Point3DData(hMinX, hMinY, hMinZ),
                new Point3DData(hMaxX, hMaxY, hMaxZ));
        }
    }
}
