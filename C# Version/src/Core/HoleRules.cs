// Hole layout rules. Positions are in the -Au frame, measured from the start of the wall.
//   0 holes  -> none (HoleOn = 0, the model suppresses the hole features)
//   1 hole   -> in the middle of the wall
//   2 holes  -> HoleEdge from each end
//   n holes  -> 2 at HoleEdge, the rest evenly spaced: pitch = (span - 2*HoleEdge) / (n - 1)
//   width wall: even counts only above 2; 4 = the 2 edge holes + a centre pair at
//               span/2 +- MidOffset (MidOffset = HoleEdge - thickness - Clearance = 48.5 mm)
//   split inner panel (width above the split width): width wall 0 or 4 holes only

using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorSizer.Core
{
    public class WallLayout
    {
        public string Wall;
        public double Span, Holes, Start, Pitch;
        public int Count = 1, On = 1, MidOn;
        public List<double> Positions = new List<double>();
        public List<string> Errors = new List<string>();
    }

    public class HoleLayout
    {
        public WallLayout Length, Width;
        public double MidOffset, InnerWidth, InnerLength;
        public int Split;
        public List<string> Errors = new List<string>();
        public List<string> Notes = new List<string>();
        public List<Param> Params = new List<Param>();
    }

    public static class HoleRules
    {
        public static WallLayout Wall(string wall, double span, double holes, double edge, double midOffset, double minGap, bool widthRules)
        {
            var r = new WallLayout { Wall = wall, Span = span, Holes = holes, Start = edge, Pitch = Math.Max(span - 2 * edge, 1) };
            int n = (int)Math.Round(holes);
            if (holes < 0 || holes != n) { r.Errors.Add($"{wall} wall: the hole count must be a whole number, 0 or more (got {U.F(holes)})."); return r; }
            if (widthRules && n > 2 && n % 2 != 0) { r.Errors.Add($"{wall} wall: use 0, 1, 2, 4, 6, ... holes (got {n})."); return r; }
            if (n >= 2 && span - 2 * edge <= 0) { r.Errors.Add($"{wall} wall: HoleEdge {U.F(edge)} mm leaves no room on a {U.F(span)} mm wall."); return r; }

            if (n == 0) r.On = 0;
            else if (n == 1) { r.Start = span / 2; r.Positions.Add(span / 2); }
            else if (widthRules && n == 4)
            {
                r.Count = 2; r.Pitch = span - 2 * edge; r.MidOn = 1;
                r.Positions.AddRange(new[] { edge, span / 2 - midOffset, span / 2 + midOffset, span - edge });
                if (span / 2 - midOffset <= edge) r.Errors.Add($"{wall} wall: {U.F(span)} mm is too narrow for 4 holes (the centre pair would pass the edge holes).");
            }
            else
            {
                double pitch = (span - 2 * edge) / (n - 1);
                r.Count = n; r.Pitch = pitch;
                for (int i = 0; i < n; i++) r.Positions.Add(edge + i * pitch);
            }
            var sorted = r.Positions.OrderBy(p => p).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                double gap = sorted[i] - sorted[i - 1];
                if (gap < minGap) { r.Errors.Add($"{wall} wall: holes {i} and {i + 1} are only {U.F(gap)} mm apart (minimum {U.F(minGap)} mm)."); break; }
            }
            return r;
        }

        // n = ceil((span - 2 * HoleEdge) / maxPitch) + 1; width walls even above 2.
        public static int AutoCount(double span, double edge, double maxPitch, bool widthWall)
        {
            if (maxPitch <= 0) throw new SizerException("Automatic hole counts need a maximum pitch above 0.");
            double free = span - 2 * edge;
            if (free <= 0) return 1;
            int n = (int)Math.Ceiling(free / maxPitch - 1e-9) + 1;
            if (widthWall && n > 2 && n % 2 != 0) n++;
            return n;
        }

        public static int InnerSplit(double width, Settings s) => s.SplitWidth > 0 && width > s.SplitWidth ? 2 : 1;

        public static HoleLayout Layout(Dictionary<string, double> v, Settings s, Standard std = null)
        {
            foreach (var k in new[] { "Width", "Length", "HoleEdge", "Clearance", "HolesL", "HolesW" })
                if (!v.ContainsKey(k)) throw new SizerException($"Parameter '{k}' is missing.");
            double edge = v["HoleEdge"], thick = s.Thickness, mid = edge - thick - v["Clearance"];
            var r = new HoleLayout { MidOffset = mid };
            r.Split = InnerSplit(v["Width"], s);
            double inset = 2 * thick + 2 * v["Clearance"];
            r.InnerWidth = (v["Width"] - inset) / r.Split;
            r.InnerLength = v["Length"] - inset;

            // Split inner panel: each half has a hole at MidOffset from both of its ends, so the
            // tray needs the centre pair (W/2 +- MidOffset) next to the 2 edge holes.
            double holesW = v["HolesW"];
            if (r.Split >= 2)
            {
                r.Notes.Add($"Wider than {U.F(s.SplitWidth)} mm: the inner panel is split in 2 halves of {U.F(r.InnerWidth)} mm.");
                if (holesW == 2) { holesW = 4; r.Notes.Add("Width wall: 2 holes become 4 (the centre pair holds the inner ends of the 2 halves)."); }
                else if (holesW != 0 && holesW != 4) { r.Errors.Add($"Width wall: with a split inner panel use 0 or 4 holes (got {U.F(holesW)})."); holesW = 0; }
            }
            r.Length = Wall("Length", v["Length"], v["HolesL"], edge, mid, s.MinHoleGap, false);
            r.Width = Wall("Width", v["Width"], holesW, edge, mid, s.MinHoleGap, true);
            if (std != null)
            {
                if (std.MinHolesL > 0 && v["HolesL"] < std.MinHolesL) r.Errors.Add($"Length wall: {std.Family} panels need at least {std.MinHolesL} holes (got {U.F(v["HolesL"])}).");
                if (std.Height > 0 && v.ContainsKey("Height") && v["Height"] != std.Height) r.Notes.Add($"{std.Family} panels are normally {U.F(std.Height)} mm high (this one is {U.F(v["Height"])} mm).");
            }
            // Width-wall pattern of the -In part: the tray's when one piece; for a half 2 holes,
            // MidOffset from each end of the half, no centre pair.
            double inPitch = r.Width.Pitch; int inCount = r.Width.Count, inMidOn = r.Width.MidOn;
            if (r.Split >= 2)
            {
                inMidOn = 0;
                if (r.Width.On == 1) { inCount = 2; inPitch = Math.Max(r.InnerWidth - 2 * mid, 1); } else inCount = 1;
            }
            r.Params.AddRange(new[]
            {
                new Param("HoleStartL", r.Length.Start, "mm"), new Param("HolePitchL", r.Length.Pitch, "mm"),
                new Param("HoleCountL", r.Length.Count, "ul"), new Param("HoleOnL", r.Length.On, "ul"),
                new Param("HoleStartW", r.Width.Start, "mm"), new Param("HolePitchW", r.Width.Pitch, "mm"),
                new Param("HoleCountW", r.Width.Count, "ul"), new Param("HoleOnW", r.Width.On, "ul"),
                new Param("HoleMidOnW", r.Width.MidOn, "ul"), new Param("InSplit", r.Split, "ul"),
                new Param("InHolePitchW", inPitch, "mm"), new Param("InHoleCountW", inCount, "ul"), new Param("InHoleMidOnW", inMidOn, "ul"),
                new Param("InWidth", r.InnerWidth, "mm"),   // spacing of the inner panels when the master patterns them
            });
            r.Errors.AddRange(r.Length.Errors);
            r.Errors.AddRange(r.Width.Errors);
            return r;
        }
    }
}
