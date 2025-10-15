using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;

namespace JplEphemerisOrbitViewer.Horizons
{
    public enum EphemerisDataKind
    {
        RaDec,          // RA/DEC + delta (topocentric veya geocentric)
        StateVector     // Cartesian X,Y,Z (km) + velocity (km/s)
    }

    public sealed class HorizonsEphemeris
    {
        public string TargetName { get; init; } = "";
        public string CenterName { get; init; } = "";
        public Vector3d TargetRadiiKmABC { get; init; } = new(1000, 1000, 1000);
        public Vector3d CenterRadiiKmABC { get; init; } = new(1000, 1000, 1000);

        public bool IsTopocentric { get; init; }
        public SiteInfo? ObserverSite { get; init; }
        public EphemerisDataKind DataKind { get; init; }

        public sealed class SiteInfo
        {
            public double LonDeg { get; init; }
            public double LatDeg { get; init; }
            public double AltKm  { get; init; }
            public double CylLonDeg { get; init; }
            public double DxyKm     { get; init; }
            public double DzKm      { get; init; }
        }

        public sealed class Entry
        {
            public DateTime TimeUtc { get; init; }

            // Legacy (RA/DEC path) – preserved for compatibility
            public double RaRad { get; init; }
            public double DecRad { get; init; }
            public double DeltaAu { get; init; }
            public Vector3d Direction { get; init; }

            // State vector extras (only meaningful when DataKind == StateVector)
            public Vector3d PositionKm { get; init; }  // Geocentric (veya merkez frame) konum
            public Vector3d VelocityKmPerSec { get; init; }
        }

        public List<Entry> Entries { get; } = new();
    }

    public static class HorizonsParser
    {
        // Header regex
        private static readonly Regex ReTarget = new(@"^Target body name:\s*(.+?)\s*\(", RegexOptions.Compiled);
        private static readonly Regex ReCenter = new(@"^Center body name:\s*(.+?)\s*\(", RegexOptions.Compiled);
        private static readonly Regex ReTargetRadii = new(@"^Target radii\s*:\s*([0-9.\-]+),\s*([0-9.\-]+),\s*([0-9.\-]+)\s*km", RegexOptions.Compiled);
        private static readonly Regex ReCenterRadii = new(@"^Center radii\s*:\s*([0-9.\-]+),\s*([0-9.\-]+),\s*([0-9.\-]+)\s*km", RegexOptions.Compiled);
        private static readonly Regex ReCenterSite = new(@"^Center-site name:", RegexOptions.Compiled);
        private static readonly Regex ReCenterGeodetic = new(@"^Center geodetic\s*:\s*([\-0-9.]+),\s*([\-0-9.]+),\s*([\-0-9.]+)", RegexOptions.Compiled);
        private static readonly Regex ReCenterCylindric = new(@"^Center cylindric\s*:\s*([\-0-9.]+),\s*([\-0-9.]+),\s*([\-0-9.]+)", RegexOptions.Compiled);
        private static readonly Regex ReStateVectorType = new(@"^Output type\s*:\s*GEOMETRIC cartesian states", RegexOptions.Compiled);

        // New: “GEOPHYSICAL PROPERTIES” support for target Equatorial/Polar radii
        private static readonly Regex ReEquatorialRadius = new(@"^Equ\.\s*radius,\s*km\s*=\s*([0-9.\-]+)", RegexOptions.Compiled);
        private static readonly Regex RePolarAxis = new(@"^Polar\s*axis,\s*km\s*=\s*([0-9.\-]+)", RegexOptions.Compiled);

        // RA/DEC row regex
        private static readonly Regex TimeAtStart = new(@"^\s*(\d{4}-[A-Za-z]{3}-\d{2})\s+(\d{2}:\d{2}(?::\d{2})?)", RegexOptions.Compiled);
        private static readonly Regex RaDecGroup = new(@"\b(\d{2})\s+(\d{2})\s+(\d{1,2}(?:\.\d+)?)\s+([+\-]\d{2})\s+(\d{2})\s+(\d{1,2}(?:\.\d+)?)", RegexOptions.Compiled);

        // State vector block regex
        private static readonly Regex ReSvStart = new(@"^(\d+\.\d+)\s*=\s*A\.D\.\s*(\d{4}-[A-Za-z]{3}-\d{2})\s+(\d{2}:\d{2}:\d{2}\.\d+)", RegexOptions.Compiled);
        private static readonly Regex ReSvXYZ = new(@"X\s*=\s*([\-0-9.E+]+)\s*Y\s*=\s*([\-0-9.E+]+)\s*Z\s*=\s*([\-0-9.E+]+)", RegexOptions.Compiled);
        private static readonly Regex ReSvV = new(@"VX\s*=\s*([\-0-9.E+]+)\s*VY\s*=\s*([\-0-9.E+]+)\s*VZ\s*=\s*([\-0-9.E+]+)(?:\s*LT\s*=\s*([\-0-9.E+]+)\s*RG\s*=\s*([\-0-9.E+]+)\s*RR\s*=\s*([\-0-9.E+]+))?", RegexOptions.Compiled);

        private static readonly string[] RowDateFormats = { "yyyy-MMM-dd HH:mm", "yyyy-MMM-dd HH:mm:ss" };
        private static readonly string SvDateFormat = "yyyy-MMM-dd HH:mm:ss.ffff";

        private const double KmPerAU = 149_597_870.7;
        private const double Deg2Rad = Math.PI / 180.0;
        private const double TwoPi = Math.PI * 2.0;

        public static HorizonsEphemeris ParseFile(string path)
        {
            Debug.WriteLine($"[Horizons] ParseFile path='{path}'");

            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); }
            catch { lines = File.ReadAllLines(path); }

            string target = "", center = "";
            Vector3d targetR = default, centerR = default;
            bool haveTargetR = false, haveCenterR = false;

            // New: stash Equatorial/Polar from GEOPHYSICAL PROPERTIES block for the target
            double? targetEquKm = null;
            double? targetPolarKm = null;

            bool isTopocentric = false;
            double siteLonDeg = 0, siteLatDeg = 0, siteAltKm = 0;
            double siteCylLonDeg = 0, siteDxyKm = 0, siteDzKm = 0;
            bool haveGeodetic = false, haveCyl = false;

            EphemerisDataKind dataKind = EphemerisDataKind.RaDec;

            foreach (var raw in lines)
            {
                var line = raw.Trim();

                if (line.StartsWith("Target body name:", StringComparison.Ordinal))
                {
                    var m = ReTarget.Match(line);
                    if (m.Success) target = m.Groups[1].Value.Trim();
                }
                else if (line.StartsWith("Center body name:", StringComparison.Ordinal))
                {
                    var m = ReCenter.Match(line);
                    if (m.Success) center = m.Groups[1].Value.Trim();
                }
                else if (line.StartsWith("Target radii", StringComparison.Ordinal))
                {
                    var m = ReTargetRadii.Match(line);
                    if (m.Success)
                    {
                        targetR = new Vector3d(ParseD(m.Groups[1]), ParseD(m.Groups[2]), ParseD(m.Groups[3]));
                        haveTargetR = true;
                    }
                }
                else if (line.StartsWith("Center radii", StringComparison.Ordinal))
                {
                    var m = ReCenterRadii.Match(line);
                    if (m.Success)
                    {
                        centerR = new Vector3d(ParseD(m.Groups[1]), ParseD(m.Groups[2]), ParseD(m.Groups[3]));
                        haveCenterR = true;
                    }
                }
                // Pick up Equatorial/Polar radii from the geophysical block for the TARGET
                else if (line.StartsWith("Equ. radius", StringComparison.Ordinal))
                {
                    var m = ReEquatorialRadius.Match(line);
                    if (m.Success) targetEquKm = ParseD(m.Groups[1]);
                }
                else if (line.StartsWith("Polar axis", StringComparison.Ordinal))
                {
                    var m = RePolarAxis.Match(line);
                    if (m.Success) targetPolarKm = ParseD(m.Groups[1]);
                }
                else if (ReCenterSite.IsMatch(line))
                {
                    isTopocentric = true;
                }
                else if (line.StartsWith("Center geodetic", StringComparison.Ordinal))
                {
                    var m = ReCenterGeodetic.Match(line);
                    if (m.Success)
                    {
                        siteLonDeg = ParseD(m.Groups[1]);
                        siteLatDeg = ParseD(m.Groups[2]);
                        siteAltKm  = ParseD(m.Groups[3]);
                        haveGeodetic = true;
                    }
                }
                else if (line.StartsWith("Center cylindric", StringComparison.Ordinal))
                {
                    var m = ReCenterCylindric.Match(line);
                    if (m.Success)
                    {
                        siteCylLonDeg = ParseD(m.Groups[1]);
                        siteDxyKm     = ParseD(m.Groups[2]);
                        siteDzKm      = ParseD(m.Groups[3]);
                        haveCyl = true;
                    }
                }
                else if (ReStateVectorType.IsMatch(line))
                {
                    dataKind = EphemerisDataKind.StateVector;
                }
            }

            // Prefer GEOPHYSICAL Equ./Polar for target if explicit Target radii are not present
            if (!haveTargetR && (targetEquKm.HasValue || targetPolarKm.HasValue))
            {
                double a = targetEquKm ?? targetPolarKm ?? 1000.0;
                double b = a;
                double c = targetPolarKm ?? targetEquKm ?? 1000.0;
                targetR = new Vector3d(a, b, c);
                haveTargetR = true;
            }

            var entries = dataKind == EphemerisDataKind.StateVector
                ? ParseStateVectors(lines)
                : ParseRaDec(lines, isTopocentric, haveCyl, siteCylLonDeg, siteDxyKm, siteDzKm);

            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(center))
                throw new InvalidDataException("Target or Center not found in header.");

            if (!haveTargetR) targetR = new Vector3d(1000, 1000, 1000);
            if (!haveCenterR) centerR = new Vector3d(1000, 1000, 1000);

            HorizonsEphemeris.SiteInfo? siteInfo = null;
            if (isTopocentric)
            {
                siteInfo = new HorizonsEphemeris.SiteInfo
                {
                    LonDeg = siteLonDeg,
                    LatDeg = siteLatDeg,
                    AltKm = siteAltKm,
                    CylLonDeg = siteCylLonDeg,
                    DxyKm = siteDxyKm,
                    DzKm = siteDzKm
                };
            }

            var eph = new HorizonsEphemeris
            {
                TargetName = target,
                CenterName = center,
                TargetRadiiKmABC = targetR,
                CenterRadiiKmABC = centerR,
                IsTopocentric = isTopocentric,
                ObserverSite = siteInfo,
                DataKind = dataKind
            };
            eph.Entries.AddRange(entries);
            Debug.WriteLine($"[Horizons] Parsed entries={entries.Count}, kind={dataKind}");
            return eph;
        }

        // -------- RA/DEC parsing (existing path, retains topocentric correction) ----------
        private static List<HorizonsEphemeris.Entry> ParseRaDec(
            string[] lines,
            bool isTopocentric,
            bool haveCyl,
            double siteCylLonDeg,
            double siteDxyKm,
            double siteDzKm)
        {
            var entries = new List<HorizonsEphemeris.Entry>();
            int tried = 0;

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                if (line.Length == 0) continue;
                if (line.StartsWith("$$") || line.StartsWith("*")) continue;
                if (line.StartsWith("Date__(UT)") || line.StartsWith("Ephemeris /")) continue;

                tried++;
                if (!TryParseRaDecRow(line, out var e))
                    continue;

                if (isTopocentric && e.TimeUtc != DateTime.MinValue && haveCyl)
                {
                    // topocentric -> geocentric düzeltmesi
                    double lonRad = siteCylLonDeg * Deg2Rad;
                    var rObsEcefKm = new Vector3d(
                        siteDxyKm * Math.Cos(lonRad),
                        siteDxyKm * Math.Sin(lonRad),
                        siteDzKm);

                    double gmst = GmstRadians(e.TimeUtc);
                    var rObsEciKm = RotateZ(rObsEcefKm, -gmst);

                    double deltaKm = e.DeltaAu * KmPerAU;
                    var rTopoKm = e.Direction * deltaKm;
                    var rGeoKm = rObsEciKm + rTopoKm;

                    double magKm = rGeoKm.Length;
                    var dirGeo = magKm > 0 ? rGeoKm / magKm : e.Direction;

                    e = new HorizonsEphemeris.Entry
                    {
                        TimeUtc = e.TimeUtc,
                        RaRad = e.RaRad,
                        DecRad = e.DecRad,
                        DeltaAu = magKm / KmPerAU,
                        Direction = dirGeo,
                        PositionKm = rGeoKm,
                        VelocityKmPerSec = Vector3d.Zero
                    };
                }
                else
                {
                    // Approx geocentric position vector for compatibility
                    double deltaKm = e.DeltaAu * KmPerAU;
                    var posKm = e.Direction * deltaKm;
                    e = new HorizonsEphemeris.Entry
                    {
                        TimeUtc = e.TimeUtc,
                        RaRad = e.RaRad,
                        DecRad = e.DecRad,
                        DeltaAu = e.DeltaAu,
                        Direction = e.Direction,
                        PositionKm = posKm,
                        VelocityKmPerSec = Vector3d.Zero
                    };
                }

                entries.Add(e);
            }
            return entries;
        }

        private static bool TryParseRaDecRow(string line, out HorizonsEphemeris.Entry entry)
        {
            entry = default!;
            DateTime dtUtc = DateTime.MinValue;
            int startIdx = 0;

            var mTime = TimeAtStart.Match(line);
            if (mTime.Success)
            {
                var dtStr = $"{mTime.Groups[1].Value} {mTime.Groups[2].Value}";
                DateTime.TryParseExact(dtStr, RowDateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dtUtc);
                startIdx = mTime.Index + mTime.Length;
            }

            string after = line[startIdx..];
            var nums = Regex.Matches(after, @"[-+]?\d+(?:\.\d+)?(?:[eE][\-+]?\d+)?");

            double rah, ram, ras, decDegSigned, decMin, decSec, deltaAu;

            if (nums.Count >= 9)
            {
                if (!TryD(nums[0], out rah) || !TryD(nums[1], out ram) || !TryD(nums[2], out ras)) return false;
                if (!TryD(nums[3], out decDegSigned) || !TryD(nums[4], out decMin) || !TryD(nums[5], out decSec)) return false;
                if (!TryD(nums[8], out deltaAu)) return false;
            }
            else
            {
                var mRA = RaDecGroup.Match(line);
                if (!mRA.Success) return false;

                rah = ParseD(mRA.Groups[1]);
                ram = ParseD(mRA.Groups[2]);
                ras = ParseD(mRA.Groups[3]);
                decDegSigned = ParseD(mRA.Groups[4]);
                decMin = ParseD(mRA.Groups[5]);
                decSec = ParseD(mRA.Groups[6]);

                var afterRaDec = line[(mRA.Index + mRA.Length)..];
                var nums2 = Regex.Matches(afterRaDec, @"[-+]?\d+(?:\.\d+)?(?:[eE][\-+]?\d+)?");
                if (nums2.Count < 3) return false;
                deltaAu = ParseD(nums2[2]);
            }

            double raHours = rah + ram / 60.0 + ras / 3600.0;
            double raRad = raHours * Math.PI / 12.0;

            int sign = decDegSigned < 0 ? -1 : 1;
            double decAbs = Math.Abs(decDegSigned);
            double decDeg = sign * (decAbs + decMin / 60.0 + decSec / 3600.0);
            double decRad = decDeg * Math.PI / 180.0;

            double cosDec = Math.Cos(decRad);
            var dir = new Vector3d(
                cosDec * Math.Cos(raRad),
                Math.Sin(decRad),
                cosDec * Math.Sin(raRad));

            entry = new HorizonsEphemeris.Entry
            {
                TimeUtc = dtUtc,
                RaRad = raRad,
                DecRad = decRad,
                DeltaAu = deltaAu,
                Direction = dir
            };
            return true;
        }

        // -------- State vector parsing ----------
        private static List<HorizonsEphemeris.Entry> ParseStateVectors(string[] lines)
        {
            var list = new List<HorizonsEphemeris.Entry>();
            bool inData = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd();
                if (line.StartsWith("$$SOE"))
                {
                    inData = true;
                    continue;
                }
                if (line.StartsWith("$$EOE"))
                    break;
                if (!inData) continue;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var mStart = ReSvStart.Match(line);
                if (!mStart.Success) continue; // only react on start line

                // Need next two lines for XYZ and V line
                if (i + 2 >= lines.Length) break;
                var xyzLine = lines[i + 1];
                var vLine = lines[i + 2];

                var mXYZ = ReSvXYZ.Match(xyzLine);
                var mV = ReSvV.Match(vLine);
                if (!mXYZ.Success || !mV.Success) continue;

                // Time
                DateTime timeUtc;
                var dateStr = $"{mStart.Groups[2].Value} {mStart.Groups[3].Value}";
                if (!DateTime.TryParseExact(dateStr, SvDateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out timeUtc))
                {
                    // Fallback without fractional seconds
                    if (!DateTime.TryParse(dateStr.Replace(".0000", ""), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timeUtc))
                        timeUtc = DateTime.MinValue;
                }

                // Position km
                var pos = new Vector3d(
                    ParseSci(mXYZ.Groups[1].Value),
                    ParseSci(mXYZ.Groups[2].Value),
                    ParseSci(mXYZ.Groups[3].Value));

                // Velocity km/s
                var vel = new Vector3d(
                    ParseSci(mV.Groups[1].Value),
                    ParseSci(mV.Groups[2].Value),
                    ParseSci(mV.Groups[3].Value));

                double magKm = pos.Length;
                double deltaAu = magKm / KmPerAU;
                var dir = magKm > 0 ? pos / magKm : Vector3d.UnitX;

                // Derive RA/DEC (ICRF) for compatibility:
                // RA = atan2(Y,X), DEC = asin(Z/|r|)
                double raRad = Math.Atan2(pos.Y, pos.X);
                if (raRad < 0) raRad += TwoPi;
                double decRad = magKm > 0 ? Math.Asin(pos.Z / magKm) : 0;

                list.Add(new HorizonsEphemeris.Entry
                {
                    TimeUtc = timeUtc,
                    RaRad = raRad,
                    DecRad = decRad,
                    DeltaAu = deltaAu,
                    Direction = dir,
                    PositionKm = pos,
                    VelocityKmPerSec = vel
                });

                i += 2; // Skip xyz & v lines already processed
            }

            return list;
        }

      
        private static double ParseD(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);
        private static double ParseSci(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        private static bool TryD(Match m, out double v) =>
            double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static double GmstRadians(DateTime utc)
        {
            double jd = ToJulianDate(utc);
            double t = (jd - 2451545.0) / 36525.0;
            double thetaDeg =
                280.46061837
                + 360.98564736629 * (jd - 2451545.0)
                + 0.000387933 * t * t
                - (t * t * t) / 38710000.0;

            double theta = thetaDeg * Deg2Rad;
            theta %= TwoPi;
            if (theta < 0) theta += TwoPi;
            return theta;
        }

        private static double ToJulianDate(DateTime utc)
        {
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            int Y = utc.Year;
            int M = utc.Month;
            double D = utc.Day + (utc.Hour + (utc.Minute + (utc.Second + utc.Millisecond / 1000.0) / 60.0) / 60.0) / 24.0;
            if (M <= 2) { Y -= 1; M += 12; }
            int A = Y / 100;
            int B = 2 - A + A / 5;
            double jd = Math.Floor(365.25 * (Y + 4716))
                        + Math.Floor(30.6001 * (M + 1))
                        + D + B - 1524.5;
            return jd;
        }

        private static Vector3d RotateZ(in Vector3d v, double a)
        {
            double c = Math.Cos(a);
            double s = Math.Sin(a);
            return new Vector3d(c * v.X - s * v.Y, s * v.X + c * v.Y, v.Z);
        }
    }
}