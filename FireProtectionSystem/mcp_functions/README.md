# FireProtectionSystem MCP Functions

This package contains 23 MCP function definitions for the FireProtectionSystem Revit add-in, enabling LLMs to discover and call reusable features for fire protection engineering workflows.

## Function Index

| Function | Category | Code Location | Wrapper Status |
|----------|----------|---------------|----------------|
| `fireprotection_extract_model_snapshot` | Model Extraction | `FireProtection.Backend/Services/Extraction/FireProtectionExtractionService.cs:30` | Needs new wrapper code |
| `fireprotection_extract_lightweight_snapshot` | Model Extraction | `FireProtection.Backend/Services/Extraction/FireProtectionExtractionService.cs:128` | Needs new wrapper code |
| `fireprotection_extract_obstacles` | Model Extraction | `FireProtection.Backend/Services/Extraction/FireProtectionExtractionService.cs:214` | Needs new wrapper code |
| `fireprotection_load_catalog` | Catalog | `FireProtection.Backend/Services/Catalog/CatalogLoader.cs:18` | Needs new wrapper code |
| `fireprotection_query_catalog_sprinklers` | Catalog | `FireProtection.Backend/Services/Catalog/CatalogService.cs:247` | Needs new wrapper code |
| `fireprotection_query_catalog_smoke_detectors` | Catalog | `FireProtection.Backend/Services/Catalog/CatalogService.cs:328` | Needs new wrapper code |
| `fireprotection_query_catalog_notification_appliances` | Catalog | `FireProtection.Backend/Services/Catalog/CatalogService.cs:362` | Needs new wrapper code |
| `fireprotection_calculate_sprinkler_layout` | Calculation | `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/BruteForceCalculationService.cs:14` | Needs new wrapper code |
| `fireprotection_place_sprinklers` | Placement | `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs:55` | Needs new wrapper code |
| `fireprotection_evaluate_sprinkler_eligibility` | Eligibility | `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs:396` | Needs new wrapper code |
| `fireprotection_calculate_smoke_detector_layout` | Calculation | `FireProtection.Backend/Services/Placement/SmokeDetectors/Final/BruteForce/Nfpa72SmokeDetectorEngine.cs:36` | Needs new wrapper code |
| `fireprotection_place_smoke_detectors` | Placement | `FireProtection.Backend/Services/Placement/SmokeDetectors/RevitSmokeDetectorPlacementExecutor.cs:51` | Needs new wrapper code |
| `fireprotection_evaluate_smoke_detector_eligibility` | Eligibility | `FireProtection.Backend/Services/Placement/Devices/FireAlarmDevicePlacementCore.cs:171` | Needs new wrapper code |
| `fireprotection_calculate_notification_appliance_layout` | Calculation | `FireProtection.Backend/Services/Placement/NotificationAppliances/RevitNotificationAppliancePlacementExecutor.cs:39` | Needs new wrapper code |
| `fireprotection_place_notification_appliances` | Placement | `FireProtection.Backend/Services/Placement/NotificationAppliances/RevitNotificationAppliancePlacementExecutor.cs:52` | Needs new wrapper code |
| `fireprotection_evaluate_notification_appliance_eligibility` | Eligibility | `FireProtection.Backend/Services/Placement/Devices/FireAlarmDevicePlacementCore.cs:171` | Needs new wrapper code |
| `fireprotection_probe_missing_families` | Diagnostics | `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs:635` | Needs new wrapper code |
| `fireprotection_begin_host_resolution_pass` | Performance | `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs:617` | Needs new wrapper code |
| `fireprotection_end_host_resolution_pass` | Performance | `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs:623` | Needs new wrapper code |
| `fireprotection_clear_eligibility_cache` | Diagnostics | `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs:611` | Needs new wrapper code |
| `fireprotection_check_clearance` | Coordination | Revit API `check_clearance` | Needs new wrapper code |
| `fireprotection_recipe_clash_review` | Coordination | Revit API `recipe_clash_review` | Needs new wrapper code |
| `fireprotection_recipe_model_health_triage` | Diagnostics | Revit API `recipe_model_health_triage` | Needs new wrapper code |

**Legend:**
- **Read directly from code**: The function maps to existing backend code that can be called directly
- **Needs new wrapper code**: The backend functionality exists but requires a thin MCP wrapper to expose it as a callable function (e.g., JSON serialization, Revit document context handling, error wrapping)

## Skipped Features

The following were intentionally NOT exposed as MCP functions:

| Feature | Reason |
|---------|--------|
| UI dialogs (TaskDialog, WPF views) | Require live user interface; not callable programmatically |
| `FireProtectionCommand` / `FireProtectionApplication` | Revit add-in entry points; not reusable functions |
| `JsonSnapshotExporter.ExportToFile` | Already covered by `export_file_path` parameter in extraction functions |
| `CatalogValidator` / `CatalogLoadException` | Internal validation; errors surfaced via `fireprotection_load_catalog` |
| `RoomExtractor`, `CeilingExtractor`, `LevelExtractor`, `ObstacleExtractor`, `ExistingSprinklerExtractor` | Internal extraction steps; exposed via unified extraction functions |
| `DevicePlacementBehaviorResolver`, `DeviceKindResolver` | Internal helpers; logic embedded in calculation/placement functions |
| `PlacementInputBuilder` | Internal DTO builder; inputs accepted directly as JSON |
| `CeilingHostResolver`, `CeilingGridMath`, `TileCentricPatternGenerator` | Internal calculation helpers; not independently callable |
| `SpatialHelpers`, `GeometryMath`, `RevitModelContext` | Utility classes; not independently callable |
| `HazardClassifier`, `HazardRuleValues` | Internal classification; exposed via room data in extraction |
| Converters, ViewModels, XAML code-behind | UI layer; not callable from MCP |
| Test files (`FireProtection.Tests\*.cs`) | Test code; not production functionality |

## Known Risks

| Risk | Description |
|------|-------------|
| **Thread safety** | All functions require a live Revit document context (`Autodesk.Revit.DB.Document`). The Revit API is single-threaded (UI thread only). The MCP wrapper must marshal calls to the Revit UI thread. |
| **Long-running work** | Model extraction on large models with many links can take 10-30 seconds. Placement transactions can take minutes. The wrapper should support async/cancellation. |
| **Authentication/Context** | No auth in the traditional sense, but requires an active Revit session with a document open. The wrapper must resolve the current `UIDocument`/`Document`. |
| **Unit-dependent values** | All internal units are **feet** (Revit internal). Input schemas accept feet; output schemas return feet. Callers must be aware. |
| **Destructive operations** | `fireprotection_place_sprinklers`, `fireprotection_place_smoke_detectors`, `fireprotection_place_notification_appliances` WRITE to the Revit model. They run in a TransactionGroup with rollback on cancellation, but partial commits before cancellation are possible if the transaction commits before the group rolls back. No dry-run mode currently implemented in backend. |
| **Provisional rules** | Sprinkler calculation uses provisional 15 ft spacing (placeholder). Notification appliance rules are explicitly provisional. Results flagged `is_provisional=true` require engineering review before construction use. |
| **Linked model coordinate transforms** | All linked-model geometry is normalized to host-MEP coordinates during extraction. Placement must NOT apply a second coordinate transform. The wrapper must ensure this invariant. |
| **Family loading** | Placement functions require families to be pre-loaded in the active document. Use `fireprotection_probe_missing_families` to check first. |
| **Ceiling grid phase ambiguity** | Ceiling tile grid origin phase may be provisional (`GridOriginSource != Exact`). Snapped placements may be on tile corners rather than centers. Diagnostic `GRID:` status in calculation result indicates reliability. |

## Open Questions for Senior

1. **MCP Host Architecture**: Should the wrapper run as a Revit add-in command (ExternalCommand) that the MCP server calls via IPC, or as a long-running Revit add-in with a named pipe/HTTP endpoint?
2. **Document Context**: How does the MCP server obtain the active `Document`? Via `UIDocument` from the Revit API context?
3. **Transaction Management**: The backend uses `TransactionGroup` for atomic runs. Should the MCP wrapper expose a `dry_run: true` option that wraps in a transaction and rolls back?
4. **Async/Cancellation**: Long operations (extraction, placement) need progress reporting and cancellation. The `IPlacementProgress` interface exists but needs MCP integration.
5. **Error Serialization**: Revit exceptions (`Autodesk.Revit.Exceptions.*`) need to be caught and serialized to JSON error responses.
6. **Version Compatibility**: The add-in supports Revit 2024 (net48), 2025 (net8), 2026 (net8). The wrapper must target the correct TFM for the running Revit version.
7. **Catalog Path**: The catalog Excel path is currently user-configured in UI. Should the MCP function accept a path parameter or use a default project-relative path?
8. **Units**: All internal units are feet. Should the MCP layer accept/return metric with automatic conversion, or keep feet as the contract?

## Deliverables

- `functions/` — 23 individual JSON function definitions
- `all_functions.json` — Array of all function objects
- `mcp_functions_list.json` — MCP-compatible `{"tools": [...]}` list
- `README.md` — This file

## 2-Line Message for Senior

> Created 23 MCP function definitions for FireProtectionSystem covering model extraction, catalog queries, sprinkler/smoke/notification calculation & placement, eligibility preflight, coordination clash review, and model health triage. All backend functionality exists but needs thin Revit-context wrappers (see README for skipped items, risks, and 8 open questions). Zip ready at `mcp_functions/`.