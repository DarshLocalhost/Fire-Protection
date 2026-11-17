using System;
using System.Collections.Generic;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Maps the rich sprinkler <see cref="SprinklerPlacementResult"/> into the device-neutral
    /// <see cref="PlacementRunReport"/> so the BruteForce tab can show the SAME engineering-grade
    /// success/failure report window the smoke-detector and notification tabs use. Pure mapping,
    /// Revit-free. Room verdicts follow the device semantics: Failed = nothing placed, ReviewRequired
    /// = placed but spatially invalid or otherwise flagged, Skipped = left untouched (existing /
    /// duplicate / missing family / outside-room), Success otherwise.
    /// </summary>
    public static class SprinklerPlacementReportMapper
    {
        public static PlacementRunReport ToPlacementRunReport(
            this SprinklerPlacementResult result,
            BruteForceCalculationResult calc,
            string familyName,
            string typeName)
        {
            var report = new PlacementRunReport();
            if (result == null)
            {
                report.OverallStatus = "Failed";
                report.Summary = "Placement produced no result.";
                return report;
            }

            report.RoomsProcessed = result.RoomsProcessed;
            report.SprinklersRequested = result.CalculatedSprinklerCount;
            report.SprinklersPlaced = result.PlacedSprinklerCount;

            if (calc != null)
            {
                report.IsProvisional = calc.IsProvisional;
                report.AppliedRulesSummary = calc.AppliedRulesSummary;
            }
            if (!string.IsNullOrEmpty(result.ResolvedFamilyPlacementType))
            {
                report.AppliedRulesSummary = (report.AppliedRulesSummary ?? string.Empty)
                    + (string.IsNullOrEmpty(report.AppliedRulesSummary) ? string.Empty : " | ")
                    + "Family placement type: " + result.ResolvedFamilyPlacementType
                    + " (" + familyName + " / " + typeName + ")";
            }

            if (result.WasCancelled)
            {
                report.OverallStatus = "Cancelled";
                report.Summary = "Placement cancelled by the user; everything created in this run was rolled back.";
                return report;
            }

            int roomsSucceeded = 0, roomsFailed = 0, roomsSkipped = 0, roomsReview = 0;

            foreach (PlacementRoomResult room in result.Rooms)
            {
                if (room == null) continue;

                int attempted = (room.Placed?.Count ?? 0) + (room.Failed?.Count ?? 0);
                int placedValid = 0, placedInvalid = 0, skippedDup = 0, skippedOutside = 0, realFailures = 0;
                string firstFailure = null;
                string levelName = null;

                if (room.Placed != null)
                {
                    foreach (PlacedSprinklerEntry p in room.Placed)
                    {
                        if (p == null) continue;
                        if (levelName == null) levelName = p.HostLevelName ?? p.ActualInstanceLevelName;
                        if (p.IsSpatiallyValid) placedValid++;
                        else
                        {
                            placedInvalid++;
                            if (firstFailure == null)
                                firstFailure = "instance " + p.RevitElementId + " placed at ("
                                    + p.X.ToString("F2") + "," + p.Y.ToString("F2") + "," + p.Z.ToString("F2")
                                    + ") but requested (" + p.RequestedX.ToString("F2") + ","
                                    + p.RequestedY.ToString("F2") + "," + p.RequestedZ.ToString("F2")
                                    + "), deviation " + p.PlacementDeviationFt.ToString("F2") + " ft";
                        }
                    }
                }

                if (room.Failed != null)
                {
                    foreach (FailedSprinklerEntry f in room.Failed)
                    {
                        if (f == null) continue;
                        if (f.SkippedDueToDuplicate) { skippedDup++; continue; }
                        if (string.Equals(f.ErrorCode, "OUTSIDE_ROOM_BOUNDARY", StringComparison.OrdinalIgnoreCase))
                        { skippedOutside++; continue; }
                        realFailures++;
                        if (firstFailure == null && !string.IsNullOrEmpty(f.Reason))
                            firstFailure = "(" + f.X.ToString("F2") + "," + f.Y.ToString("F2") + ") " + f.Reason;
                    }
                }

                RoomCalculationResult calcRoom = null;
                if (calc?.Rooms != null)
                {
                    calcRoom = calc.Rooms.Find(r => r != null && (r.RoomId == room.RoomId || r.RoomNumber == room.RoomNumber || r.RoomName == room.RoomName));
                }

                double? coveragePercent = null;
                if (room.Placed != null && room.Placed.Count > 0)
                {
                    coveragePercent = CalculateRoomCoveragePercent(calcRoom, room.Placed);
                }

                string status;
                if (attempted == 0) status = "Skipped";
                else if (placedValid + placedInvalid == 0) status = "Failed";
                else if (placedInvalid > 0 || realFailures > 0) status = "ReviewRequired";
                else if (coveragePercent.HasValue && coveragePercent.Value < 95.0) status = "ReviewRequired";
                else status = "Success";

                string message;
                switch (status)
                {
                    case "Success":
                        message = (coveragePercent.HasValue ? "Coverage: " + coveragePercent.Value.ToString("F1") + "%. " : string.Empty)
                            + "Placed " + placedValid + " of " + attempted + " sprinkler(s)."
                            + (skippedDup > 0 ? " " + skippedDup + " skipped (duplicate)." : string.Empty)
                            + (skippedOutside > 0 ? " " + skippedOutside + " refused (outside room boundary)." : string.Empty);
                        break;
                    case "Failed":
                        message = "No sprinklers could be placed (" + realFailures + " failure(s))."
                            + (firstFailure != null ? " First: " + firstFailure : string.Empty);
                        break;
                    case "ReviewRequired":
                        if (coveragePercent.HasValue && coveragePercent.Value < 95.0 && placedInvalid == 0 && realFailures == 0)
                        {
                            message = "Coverage incomplete: " + coveragePercent.Value.ToString("F1") + "% (threshold 95%). Placed "
                                + placedValid + " of " + attempted + " sprinkler(s); engineering review required."
                                + (skippedDup > 0 ? " " + skippedDup + " skipped (duplicate)." : string.Empty);
                        }
                        else
                        {
                            message = (coveragePercent.HasValue ? "Coverage: " + coveragePercent.Value.ToString("F1") + "%. " : string.Empty)
                                + "Placed " + (placedValid + placedInvalid) + " with " + placedInvalid
                                + " spatially-invalid and " + realFailures + " failed point(s); engineering review required."
                                + (firstFailure != null ? " First: " + firstFailure : string.Empty);
                        }
                        break;
                    default:
                        message = "Room skipped - nothing was calculated or attempted here"
                            + (skippedDup > 0 ? " (" + skippedDup + " point(s) were duplicates of existing sprinklers)" : string.Empty)
                            + (skippedOutside > 0 ? " (" + skippedOutside + " point(s) refused: outside room boundary)" : string.Empty)
                            + ".";
                        break;
                }

                report.RoomReports.Add(new PlacementRoomReport
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName ?? room.RoomNumber ?? room.RoomId,
                    LevelName = levelName,
                    Status = status,
                    CoveragePercent = coveragePercent,
                    Message = message,
                    FailureReason = status == "Failed" || status == "ReviewRequired" ? message : null,
                    ReviewRequired = status == "ReviewRequired",
                    PointsRequested = attempted,
                    PointsPlaced = placedValid + placedInvalid
                });

                switch (status)
                {
                    case "Success": roomsSucceeded++; break;
                    case "Failed": roomsFailed++; break;
                    case "ReviewRequired": roomsReview++; break;
                    default: roomsSkipped++; break;
                }
            }

            report.RoomsSucceeded = roomsSucceeded;
            report.RoomsFailed = roomsFailed;
            report.RoomsSkipped = roomsSkipped + result.SkippedExistingRoomCount + result.SkippedMissingFamilyCount;
            report.ReviewRequiredCount = roomsReview;

            if (result.Warnings != null) foreach (string w in result.Warnings) if (!string.IsNullOrEmpty(w)) report.Warnings.Add(w);
            if (result.Errors != null) foreach (string e2 in result.Errors) if (!string.IsNullOrEmpty(e2)) report.Warnings.Add(e2);

            var failedReasons = new List<string>();
            if (result.SkippedMissingFamilyCount > 0)
                failedReasons.Add("Rooms skipped for missing family/type: " + result.SkippedMissingFamilyCount);
            foreach (PlacementRoomReport rr in report.RoomReports)
                if (rr.Status == "Failed" && !string.IsNullOrEmpty(rr.Message))
                    failedReasons.Add(rr.RoomName + ": " + rr.Message);
            report.FailedReasons = failedReasons;

            var reviewReasons = new List<string>();
            if (result.PlacedButInvalidCount > 0)
                reviewReasons.Add(result.PlacedButInvalidCount + " placed sprinkler(s) were spatially INVALID (deviation beyond tolerance).");
            if (result.SkippedOutsideRoomCount > 0)
                reviewReasons.Add(result.SkippedOutsideRoomCount + " point(s) refused as OUTSIDE the room boundary - check the linked-model coordinate transform.");
            if (result.SkippedDuplicateCount > 0)
                reviewReasons.Add(result.SkippedDuplicateCount + " point(s) skipped as duplicates of existing sprinklers.");
            foreach (PlacementRoomReport rr in report.RoomReports)
                if (rr.Status == "ReviewRequired" && !string.IsNullOrEmpty(rr.Message))
                    reviewReasons.Add(rr.RoomName + ": " + rr.Message);
            report.ReviewRequiredReasons = reviewReasons;

            report.OverallStatus = roomsFailed > 0
                ? "Failed"
                : (roomsReview > 0 || report.IsProvisional ? "ReviewRequired" : (roomsSkipped > 0 ? "PassWithSkips" : "Success"));

            report.Summary = "Sprinklers (" + familyName + " / " + typeName + "): placed "
                + report.SprinklersPlaced + " of " + report.SprinklersRequested + " across "
                + report.RoomsProcessed + " room(s); " + roomsSucceeded + " room(s) ok, "
                + roomsFailed + " failed, " + roomsReview + " review required, "
                + report.RoomsSkipped + " skipped."
                + (report.IsProvisional ? " PROVISIONAL rules - engineering review required." : string.Empty);

            return report;
        }

        private static double? CalculateRoomCoveragePercent(
            RoomCalculationResult calcRoom,
            List<PlacedSprinklerEntry> placed)
        {
            if (placed == null || placed.Count == 0) return 0.0;

            // 1. Resolve room floor area (from RoomCalculationResult or Shoelace polygon area fallback)
            double roomArea = 0.0;
            if (calcRoom?.RoomAreaSqFt.HasValue == true && calcRoom.RoomAreaSqFt.Value > 0)
            {
                roomArea = calcRoom.RoomAreaSqFt.Value;
            }
            else if (calcRoom?.Polygon != null && calcRoom.Polygon.Count >= 3)
            {
                roomArea = Math.Abs(ComputePolygonArea(calcRoom.Polygon));
            }

            if (roomArea <= 0.0) return null;

            // 2. Count active placed heads
            int activeCount = 0;
            foreach (var p in placed)
            {
                if (p != null) activeCount++;
            }
            if (activeCount == 0) return 0.0;

            // 3. Resolve max coverage area per head (catalog override, ruleSet value, or spacing square fallback)
            double maxCoveragePerHead = 225.0; // standard Light Hazard fallback
            if (calcRoom?.MaxCoverageAreaSqFt.HasValue == true && calcRoom.MaxCoverageAreaSqFt.Value > 0)
            {
                maxCoveragePerHead = calcRoom.MaxCoverageAreaSqFt.Value;
            }
            else if (calcRoom?.AppliedMaxSpacingFt.HasValue == true && calcRoom.AppliedMaxSpacingFt.Value > 0)
            {
                // Fallback to S * S if explicit coverage area is not set
                maxCoveragePerHead = calcRoom.AppliedMaxSpacingFt.Value * calcRoom.AppliedMaxSpacingFt.Value;
            }

            // 4. Formula-based coverage: (Total Heads Placed * Head Max Coverage Area) / Room Area * 100%
            double totalCoverageArea = activeCount * maxCoveragePerHead;
            double coveragePct = (totalCoverageArea / roomArea) * 100.0;

            // 5. Clamp to 100.0% max as approved
            return Math.Min(100.0, Math.Max(0.0, coveragePct));
        }

        private static double ComputePolygonArea(List<double[]> polygon)
        {
            if (polygon == null || polygon.Count < 3) return 0.0;
            double area = 0.0;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                double[] p1 = polygon[i];
                double[] p2 = polygon[(i + 1) % n];
                if (p1 == null || p2 == null || p1.Length < 2 || p2.Length < 2) continue;
                area += (p1[0] * p2[1]) - (p2[0] * p1[1]);
            }
            return area * 0.5;
        }
    }
}
