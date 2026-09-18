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

                string status;
                if (attempted == 0) status = "Skipped";
                else if (placedValid + placedInvalid == 0) status = "Failed";
                else if (placedInvalid > 0 || realFailures > 0) status = "ReviewRequired";
                else status = "Success";

                string message;
                switch (status)
                {
                    case "Success":
                        message = "Placed " + placedValid + " of " + attempted + " sprinkler(s)."
                            + (skippedDup > 0 ? " " + skippedDup + " skipped (duplicate)." : string.Empty)
                            + (skippedOutside > 0 ? " " + skippedOutside + " refused (outside room boundary)." : string.Empty);
                        break;
                    case "Failed":
                        message = "No sprinklers could be placed (" + realFailures + " failure(s))."
                            + (firstFailure != null ? " First: " + firstFailure : string.Empty);
                        break;
                    case "ReviewRequired":
                        message = "Placed " + (placedValid + placedInvalid) + " with " + placedInvalid
                            + " spatially-invalid and " + realFailures + " failed point(s); engineering review required."
                            + (firstFailure != null ? " First: " + firstFailure : string.Empty);
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
    }
}
