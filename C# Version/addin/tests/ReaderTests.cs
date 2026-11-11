// Tests for the reader on stand-in objects that have the same property names as the Inventor API.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InventorSizer.AsmReader;

static class ReaderTests
{
    static int fail, pass;

    static void Check(string what, object got, object want)
    {
        if (Equals(Convert.ToString(got), Convert.ToString(want))) { pass++; Console.WriteLine($"ok   {what} = {got}"); }
        else { fail++; Console.WriteLine($"FAIL {what} : got '{got}' want '{want}'"); }
    }

    class Point { public double X { get; set; } public double Y { get; set; } public double Z { get; set; } }
    class Box { public Point MinPoint { get; set; } public Point MaxPoint { get; set; } }
    class Broken { public string Name => "bad"; public string Fails => throw new InvalidOperationException("no"); }

    static Node Child(Node n, string start) => n.Load().First(k => k.Text.StartsWith(start));

    static int Main()
    {
        object par = new { Name = "Length", Expression = "800 mm", Value = 80.0, Units = "mm" };
        object partDef = new
        {
            Features = new List<object> { new { Name = "Extrusion1", Suppressed = false, Parameters = new List<object> { par }, Sketch = new { Name = "Sketch1", SketchLines = new List<object> { new { Length = 5.0 } } } } },
            Sketches = new List<object>(),
            Parameters = new { Count = 1, UserParameters = new List<object> { par } },
        };
        object partDoc = new { FullFileName = @"K:\CAD\800x1200x47-Au.ipt", DocumentType = 12290, ComponentDefinition = partDef, PropertySets = new List<object>() };
        object occ1 = new { Name = "800x1200x47-Au:1", Suppressed = false, Definition = new { Document = partDoc }, ReferencedDocumentDescriptor = new { FullDocumentName = @"K:\CAD\800x1200x47-Au.ipt" }, RangeBox = new Box { MinPoint = new Point(), MaxPoint = new Point { X = 80, Y = 120, Z = 4.7 } } };
        object occ2 = new { Name = "Screw:1", Suppressed = true, ReferencedDocumentDescriptor = new { FullDocumentName = @"K:\CAD\Screw.ipt" } };
        object occ3 = new { Name = "Screw:2", Suppressed = false, ReferencedDocumentDescriptor = new { FullDocumentName = @"K:\CAD\Screw.ipt" } };
        var occs = new List<object> { occ1, occ2, occ3 };
        object con = new { Name = "Mate:1", OccurrenceOne = occ1, OccurrenceTwo = occ3, Offset = new { Expression = "0 mm" }, HealthStatus = 11778 };
        object asmDef = new { Occurrences = new FakeOccurrences(occs), Constraints = new List<object> { con } };
        object asm = new { FullFileName = @"K:\CAD\FP-800x1200x47.iam", DocumentType = 12291, ComponentDefinition = asmDef, ReferencedDocuments = new List<object> { partDoc } };

        Node root = Tree.Document(asm);
        Check("root", root.Text, "FP-800x1200x47.iam");
        var top = root.Load();
        Check("groups", string.Join("|", top.Select(n => n.Text)), "Components (3)|Constraints (1)|Unique files with quantity|Referenced files (1)|All API objects of this " + asm.GetType().Name);

        Node comps = top[0];
        Check("occurrences", string.Join("|", comps.Load().Select(n => n.Text)), "800x1200x47-Au:1|Screw:1  (suppressed)|Screw:2");
        Node o1 = comps.Load()[0];
        Check("occurrence file row", o1.Extra[0].Value, @"K:\CAD\800x1200x47-Au.ipt");
        Check("occurrence box in mm", Tree.Rows(o1).First(r => r.Key == "RangeBox").Value, "800 x 1200 x 47 mm, from (0, 0, 0) mm");
        Node file = Child(o1, "File:");
        Check("occurrence file node", file.Text, "File: 800x1200x47-Au.ipt");
        Node feat = Child(file, "Features").Load()[0];
        Check("feature", feat.Text.StartsWith("Extrusion1  ["), true);
        Check("feature sketch row", feat.Extra[0].Value, "Sketch1");
        Check("feature parameter", Child(feat, "Parameters").Load()[0].Text, "Length = 800 mm");
        Check("sketch lines", Child(Child(feat, "Sketch1"), "Lines").Text, "Lines (1)");
        Check("user parameters", Child(Child(file, "Parameters"), "User parameters").Load()[0].Text, "Length = 800 mm");

        Node c = top[1].Load()[0];
        Check("constraint", c.Text.EndsWith("800x1200x47-Au:1 <-> Screw:2"), true);
        Check("constraint offset", c.Extra.First(r => r.Key == "Offset").Value, "0 mm");
        Check("quantities", string.Join("|", top[2].Load().Select(n => n.Text)), "1 x 800x1200x47-Au.ipt|2 x Screw.ipt");

        Node raw = top.Last();
        Check("raw is marked", raw.Raw, true);
        Check("raw children", string.Join("|", raw.Load().Select(n => n.Text.Split(':')[0])), "ComponentDefinition|ReferencedDocuments");
        Check("raw collection items", Tree.RawNode("x", occs).Load().Count, 3);

        Check("failing property is left out", string.Join("|", Api.Members(new Broken()).Select(m => m.Name)), "Name");
        Check("missing path", Api.Get(asm, "ComponentDefinition.Nothing.Name"), null);
        Check("format enum", Api.Format(11778, "kUpToDateHealth"), "kUpToDateHealth (11778)");
        Check("format array", Api.Format(new[] { 1.5, 2.0 }), "1.5; 2");

        string xml = Path.Combine(Path.GetTempPath(), "reader-test.xml");
        int count = Tree.Export(root, xml);
        string text = File.ReadAllText(xml);
        File.Delete(xml);
        Check("export wrote nodes", count > 15, true);
        Check("export has the feature", text.Contains("Extrusion1"), true);
        Check("export writes a file once", text.Split(new[] { "ContentWrittenAbove" }, StringSplitOptions.None).Length - 1, 1);
        Check("export skips raw nodes", text.Contains("All API objects"), false);

        Console.WriteLine($"passed {pass}, FAILURES: {fail}");
        return fail;
    }

    // Like Inventor's ComponentOccurrences: a collection with an extra AllLeafOccurrences.
    class FakeOccurrences : List<object>
    {
        public FakeOccurrences(IEnumerable<object> items) : base(items) { }
        public List<object> AllLeafOccurrences => this;
    }
}
