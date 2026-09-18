using System.Runtime.CompilerServices;

// The NFPA 13 / NFPA 72 calculation engines (RoomGeometry, GeometryMath, AudibleCoverageEngine, etc.)
// are internal — they are implementation detail, not public API. The Revit-free test harness
// (FireProtection.Tests) exercises them directly, so it is granted access to internals here.
[assembly: InternalsVisibleTo("FireProtection.Tests")]
