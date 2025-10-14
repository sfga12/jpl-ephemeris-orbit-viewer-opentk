using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace JplEphemerisOrbitViewer
{
    public sealed class EphemerisTrack
    {
        public readonly List<DateTime> TimesUtc = new();
        public readonly List<Vector3d> Directions = new(); // unit vectors
        public readonly List<double> DeltaAu = new();       // distance in AU

        public DateTime StartUtc => TimesUtc[0];
        public DateTime EndUtc => TimesUtc[^1];
        public TimeSpan Step => TimesUtc.Count > 1 ? TimesUtc[1] - TimesUtc[0] : TimeSpan.Zero;
        public int Count => TimesUtc.Count;

        // Evaluate world-space position at time t (scene units) using linear interpolation.
        public Vector3 Evaluate(DateTime t, float unitsPerAU)
        {
            if (TimesUtc.Count == 0) return Vector3.Zero;
            if (t <= StartUtc) return ToUnits(0, unitsPerAU);
            if (t >= EndUtc) return ToUnits(Count - 1, unitsPerAU);

            // Binary search
            int lo = 0, hi = TimesUtc.Count - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (TimesUtc[mid] <= t) lo = mid; else hi = mid;
            }

            var t0 = TimesUtc[lo];
            var t1 = TimesUtc[hi];
            double denom = Math.Max((t1 - t0).TotalSeconds, 1e-6);
            float alpha = (float)((t - t0).TotalSeconds / denom);

            Vector3 p0 = ToUnits(lo, unitsPerAU);
            Vector3 p1 = ToUnits(hi, unitsPerAU);
            return Vector3.Lerp(p0, p1, Math.Clamp(alpha, 0f, 1f));
        }

        public Vector3 ToUnits(int i, float unitsPerAU)
        {
            var dir = Directions[i];
            double d = DeltaAu[i] * unitsPerAU;
            return new Vector3((float)(dir.X * d), (float)(dir.Y * d), (float)(dir.Z * d));
        }

        public static EphemerisTrack FromHorizons(JplEphemerisOrbitViewer.Horizons.HorizonsEphemeris eph)
        {
            var tr = new EphemerisTrack();
            foreach (var e in eph.Entries)
            {
                tr.TimesUtc.Add(e.TimeUtc);
                tr.Directions.Add(e.Direction);
                tr.DeltaAu.Add(e.DeltaAu);
            }
            return tr;
        }
    }
}