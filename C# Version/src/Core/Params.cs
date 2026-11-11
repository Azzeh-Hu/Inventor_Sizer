// Parameters.xml (the input values, in Inventor's "Export to XML" layout so it can also be
// imported by hand) and settings.json.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace InventorSizer.Core
{
    public class Param
    {
        public string Name;
        public double Value;
        public string Unit;
        public string Description;
        public Param(string name, double value, string unit, string description = "") { Name = name; Value = value; Unit = unit ?? ""; Description = description ?? ""; }
        public string FormatValue() => (U.F(Value) + " " + Unit).Trim();
        public override string ToString() => $"{Name} = {FormatValue()}";
    }

    public static class ParamFile
    {
        public static readonly string[][] Defaults =
        {
            new[] { "Width", "386", "mm", "1st number in the file name" },
            new[] { "Length", "656", "mm", "2nd number in the file name" },
            new[] { "Height", "47", "mm", "3rd number in the file name" },
            new[] { "HolesL", "3", "ul", "Holes per length wall (0, 1, 2, 3, ...)" },
            new[] { "HolesW", "2", "ul", "Holes per width wall (0, 1, 2, 4, 6, ...)" },
            new[] { "HoleEdge", "50", "mm", "Hole centre distance from the panel edge (-Au)" },
            new[] { "HoleZ", "22", "mm", "Hole centre height" },
            new[] { "HoleDia", "4.1", "mm", "Hole diameter (fixed)" },
            new[] { "Clearance", "0.5", "mm", "Gap between -In and the -Au wall (assembly offsets)" },
            new[] { "HingeCount", "2", "ul", "TP: hinges per door, evenly spaced (other types: not used)" },
            new[] { "HandleCount", "2", "ul", "TP: handles per door, evenly spaced (other types: not used)" },
        };

        private static readonly Regex ValueRegex = new Regex(@"^\s*(-?\d+(?:[.,]\d+)?)\s*([^\d\s]*)\s*$");

        public static void Save(string path, IEnumerable<Param> ps)
        {
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var w = XmlWriter.Create(path, settings))
            {
                w.WriteStartDocument();
                w.WriteStartElement("ParamWithValueList");
                w.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");
                w.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                w.WriteElementString("version", "20080502");
                w.WriteStartElement("parameterTypes");
                foreach (var t in new[] { new[] { "Inventor", "0" }, new[] { "String", "1" }, new[] { "Boolean", "2" } })
                {
                    w.WriteStartElement("ParamType");
                    w.WriteElementString("typeName", t[0]);
                    w.WriteElementString("typeCode", t[1]);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                w.WriteStartElement("parameters");
                foreach (var p in ps)
                {
                    w.WriteStartElement("ParamWithValue");
                    w.WriteElementString("name", p.Name);
                    w.WriteElementString("typeCode", string.IsNullOrEmpty(p.Unit) ? "ul" : p.Unit);
                    w.WriteElementString("value", p.FormatValue());
                    w.WriteElementString("comment", p.Description);
                    w.WriteElementString("isKey", "false");
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndDocument();
            }
        }

        // Reads the file (creating it with defaults when missing); default entries an older file
        // lacks are added, so existing files keep working after an update.
        public static List<Param> Read(string path)
        {
            var list = new List<Param>();
            if (File.Exists(path))
            {
                var xml = new XmlDocument();
                xml.Load(path);
                foreach (XmlNode node in xml.SelectNodes("//ParamWithValue"))
                {
                    string name = node.SelectSingleNode("name")?.InnerText.Trim() ?? "";
                    string value = node.SelectSingleNode("value")?.InnerText ?? "";
                    var m = ValueRegex.Match(value);
                    if (!m.Success) throw new SizerException($"Parameters.xml: {name} = '{value}' is not a number with a unit. This file holds input values only; put formulas in Inventor.");
                    list.Add(new Param(name, U.P(m.Groups[1].Value), m.Groups[2].Value, node.SelectSingleNode("comment")?.InnerText ?? ""));
                }
            }
            var added = new List<string>();
            foreach (var d in Defaults)
            {
                if (list.Any(p => p.Name == d[0])) continue;
                list.Add(new Param(d[0], U.P(d[1]), d[2], d[3]));
                added.Add(d[0]);
            }
            if (added.Count > 0)
            {
                Save(path, list);
                Log.Write("Parameters.xml: added " + string.Join(", ", added));
            }
            return list;
        }
    }

    // settings.json. The folder for new panels is deliberately not saved (chosen per session).
    public class Settings
    {
        public double MinHoleGap = 10;     // mm between hole centres before Apply is blocked
        public double Thickness = 1;       // mm, one sheet (Staerke)
        public double SplitWidth = 500;    // mm, wider panels get the inner panel split in 2; 0 = never
        public double MaxHolePitch = 400;  // mm, automatic hole counts; 0 = by hand
        public string OutputFolder = "";   // this session only

        public static Settings Read(string path)
        {
            var s = new Settings();
            if (!File.Exists(path)) return s;
            try
            {
                if (Json.Parse(File.ReadAllText(path)) is JsonObject j)
                {
                    s.MinHoleGap = j.Num("minHoleGap", s.MinHoleGap);
                    s.Thickness = j.Num("thickness", s.Thickness);
                    s.SplitWidth = j.Num("splitWidth", s.SplitWidth);
                    s.MaxHolePitch = j.Num("maxHolePitch", s.MaxHolePitch);
                }
            }
            catch (Exception e) { Log.Write($"settings.json could not be read ({e.Message}) - defaults used"); }
            return s;
        }

        public void Save(string path)
        {
            if (MinHoleGap < 0) throw new SizerException("Minimum hole gap cannot be negative.");
            if (Thickness <= 0) throw new SizerException("Sheet thickness must be greater than 0.");
            if (SplitWidth < 0 || MaxHolePitch < 0) throw new SizerException("Split width and hole pitch cannot be negative.");
            var j = new JsonObject { ["minHoleGap"] = MinHoleGap, ["thickness"] = Thickness, ["splitWidth"] = SplitWidth, ["maxHolePitch"] = MaxHolePitch };
            File.WriteAllText(path, Json.Write(j), new UTF8Encoding(false));
        }

        public Settings Clone() => (Settings)MemberwiseClone();
    }
}
