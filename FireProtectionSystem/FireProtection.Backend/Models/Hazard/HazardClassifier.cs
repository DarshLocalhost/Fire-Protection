using System;
using System.Collections.Generic;
using System.Linq;

namespace FireProtection.Backend.Models.Hazard
{
    public static class HazardClassifier
    {
        private static readonly Tuple<string, HazardClass>[] Map =
        {
            // LIGHT
            Tuple.Create("office", HazardClass.Light),
            Tuple.Create("school", HazardClass.Light),
            Tuple.Create("classroom", HazardClass.Light),
            Tuple.Create("hospital", HazardClass.Light),
            Tuple.Create("ward", HazardClass.Light),
            Tuple.Create("clinic", HazardClass.Light),
            Tuple.Create("church", HazardClass.Light),
            Tuple.Create("temple", HazardClass.Light),
            Tuple.Create("hotel room", HazardClass.Light),
            Tuple.Create("guest room", HazardClass.Light),
            Tuple.Create("lobby", HazardClass.Light),
            Tuple.Create("corridor", HazardClass.Light),
            Tuple.Create("toilet", HazardClass.Light),
            Tuple.Create("washroom", HazardClass.Light),
            Tuple.Create("conference", HazardClass.Light),
            Tuple.Create("meeting", HazardClass.Light),
            Tuple.Create("dining", HazardClass.Light),
            Tuple.Create("seating", HazardClass.Light),
            Tuple.Create("theatre", HazardClass.Light),
            Tuple.Create("theater", HazardClass.Light),
            Tuple.Create("museum", HazardClass.Light),
            Tuple.Create("library reading", HazardClass.Light),

            // OH1
            Tuple.Create("parking", HazardClass.OH1),
            Tuple.Create("garage", HazardClass.OH1),
            Tuple.Create("laundry", HazardClass.OH1),
            Tuple.Create("kitchen", HazardClass.OH1),
            Tuple.Create("canteen service", HazardClass.OH1),
            Tuple.Create("restaurant service", HazardClass.OH1),
            Tuple.Create("cannery", HazardClass.OH1),
            Tuple.Create("bakery", HazardClass.OH1),
            Tuple.Create("beverage", HazardClass.OH1),
            Tuple.Create("electronic", HazardClass.OH1),
            Tuple.Create("mechanical room", HazardClass.OH1),
            Tuple.Create("electrical room", HazardClass.OH1),
            Tuple.Create("pump room", HazardClass.OH1),

            // OH2
            Tuple.Create("machine shop", HazardClass.OH2),
            Tuple.Create("workshop", HazardClass.OH2),
            Tuple.Create("repair", HazardClass.OH2),
            Tuple.Create("mercantile", HazardClass.OH2),
            Tuple.Create("retail", HazardClass.OH2),
            Tuple.Create("shop floor", HazardClass.OH2),
            Tuple.Create("dry clean", HazardClass.OH2),
            Tuple.Create("wood assembly", HazardClass.OH2),
            Tuple.Create("carpentry", HazardClass.OH2),
            Tuple.Create("paper", HazardClass.OH2),
            Tuple.Create("post office", HazardClass.OH2),
            Tuple.Create("stack room", HazardClass.OH2),
            Tuple.Create("stationery", HazardClass.OH2),
            Tuple.Create("stockroom", HazardClass.OH2),
            Tuple.Create("store room", HazardClass.OH2),
            Tuple.Create("storage", HazardClass.OH2),
            Tuple.Create("warehouse", HazardClass.OH2),

            // EH1
            Tuple.Create("sawmill", HazardClass.EH1),
            Tuple.Create("die cast", HazardClass.EH1),
            Tuple.Create("printing", HazardClass.EH1),
            Tuple.Create("plywood", HazardClass.EH1),
            Tuple.Create("textile", HazardClass.EH1),
            Tuple.Create("upholster", HazardClass.EH1),
            Tuple.Create("foam", HazardClass.EH1),
            Tuple.Create("rubber", HazardClass.EH1),

            // EH2
            Tuple.Create("paint", HazardClass.EH2),
            Tuple.Create("spray booth", HazardClass.EH2),
            Tuple.Create("solvent", HazardClass.EH2),
            Tuple.Create("varnish", HazardClass.EH2),
            Tuple.Create("asphalt", HazardClass.EH2),
            Tuple.Create("flammable", HazardClass.EH2),
            Tuple.Create("fuel", HazardClass.EH2),
            Tuple.Create("oil quench", HazardClass.EH2),
            Tuple.Create("chemical", HazardClass.EH2),
            Tuple.Create("plastics processing", HazardClass.EH2)
        };

        private static readonly string[] AlwaysReview =
        {
            "storage",
            "warehouse",
            "rack",
            "high bay",
            "godown",
            "server",
            "data"
        };

        public static HazardResult ClassifyByName(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName))
            {
                return new HazardResult(
                    HazardClass.Light,
                    "",
                    false);
            }

            string name = roomName.Trim().ToLowerInvariant();

            HazardClass best = HazardClass.Light;
            bool matched = false;
            List<string> hits = new List<string>();

            foreach (Tuple<string, HazardClass> entry in Map)
            {
                if (!name.Contains(entry.Item1))
                    continue;

                hits.Add(entry.Item1);
                matched = true;

                if (entry.Item2 > best)
                    best = entry.Item2;
            }

            bool review =
                matched &&
                (hits.Count > 1 || AlwaysReview.Any(name.Contains));

            return new HazardResult(
                best,
                string.Join("+", hits),
                review);
        }
    }
}