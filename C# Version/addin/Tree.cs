// The tree of the reader: what is inside a document, in the order you would look for it.
// Every node keeps its API object; the window shows all properties of that object, and the
// last child of every node ("All API objects") opens whatever the API offers beyond this tree.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;

namespace InventorSizer.AsmReader
{
    public class Node
    {
        public string Text = "";
        public object Obj;                       // the API object, null for a plain group
        public Func<List<Node>> Load;            // children, read when the node is opened; null = none
        public bool Raw;                         // part of "All API objects" (not exported)
        public string DocPath;                   // set on document nodes
        public List<KeyValuePair<string, string>> Extra = new List<KeyValuePair<string, string>>();
        public Node Row(string name, string value) { if (!string.IsNullOrEmpty(value)) Extra.Add(new KeyValuePair<string, string>(name, value)); return this; }
    }

    public static class Tree
    {
        public static object App;                // Inventor.Application, set by the add-in
        const int PartDoc = 12290, AssemblyDoc = 12291;
        const string ILogicAddIn = "{3BDD8D79-2179-4B11-8A5A-257B1C0263AC}";
        const int MaxRawItems = 5000;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string FileName(string path) => path.Substring(path.LastIndexOfAny(new[] { '\\', '/' }) + 1);

        // ------------------------------------------------------------------ building blocks

        // A group "Text (count)" over an API collection; left out when the collection is not there.
        static void Group(List<Node> list, string text, object coll, Func<object, Node> make)
        {
            if (coll == null) return;
            int n = Api.Count(coll);
            var g = new Node { Text = $"{text} ({n})", Obj = coll };
            if (n > 0) g.Load = () => Api.Items(coll).Select(make).ToList();
            list.Add(g);
        }

        // Adds the "All API objects" child to a node's children.
        static List<Node> WithRaw(List<Node> list, object o)
        {
            list.Add(RawNode($"All API objects of this {Api.TypeName(o)}", o));
            return list;
        }

        public static Node RawNode(string text, object o)
        {
            var n = new Node { Text = text, Obj = o, Raw = true };
            n.Load = () =>
            {
                var kids = new List<Node>();
                if (Api.IsCollection(o))
                {
                    var items = Api.Items(o);
                    for (int i = 0; i < items.Count && i < MaxRawItems; i++) kids.Add(RawNode($"[{i + 1}] {Api.Short(items[i])}", items[i]));
                    if (items.Count > MaxRawItems) kids.Add(new Node { Text = $"... {items.Count - MaxRawItems} more" });
                }
                foreach (Member m in Api.Members(o))
                    if (Api.IsObject(m.Value) && m.Name != "Application") kids.Add(RawNode($"{m.Name}: {Api.Short(m.Value)}", m.Value));
                return kids;
            };
            return n;
        }

        // Any API object without a special layout.
        static Node Item(object o)
        {
            string name = Api.Text(o, "Name"), type = Api.TypeName(o);
            var n = new Node { Text = name != "" ? $"{name}  [{type}]" : $"[{type}]", Obj = o };
            n.Load = () => WithRaw(new List<Node>(), o);
            return n;
        }

        // ------------------------------------------------------------------ documents

        public static Node Document(object doc)
        {
            string path = Api.Text(doc, "FullFileName");
            int type = Convert.ToInt32(Api.Get(doc, "DocumentType") ?? 0, Inv);
            var n = new Node { Text = path != "" ? FileName(path) : Api.Text(doc, "DisplayName"), Obj = doc, DocPath = path };
            n.Row("File", path);
            n.Load = () =>
            {
                object def = Api.Get(doc, "ComponentDefinition");
                var list = new List<Node>();
                if (type == AssemblyDoc)
                {
                    Group(list, "Components", Api.Get(def, "Occurrences"), Occurrence);
                    Group(list, "Constraints", Api.Get(def, "Constraints"), Constraint);
                    Group(list, "Joints", Api.Get(def, "Joints"), Item);
                    Group(list, "Component patterns", Api.Get(def, "OccurrencePatterns"), Item);
                    list.Add(new Node { Text = "Unique files with quantity", Load = () => Quantities(def) });
                    Group(list, "View representations", Api.Get(def, "RepresentationsManager.DesignViewRepresentations"), Item);
                    Group(list, "Positional representations", Api.Get(def, "RepresentationsManager.PositionalRepresentations"), Item);
                    Group(list, "Model states", Api.Get(def, "ModelStates"), Item);
                }
                if (type == PartDoc)
                {
                    Group(list, "Features", Api.Get(def, "Features"), Feature);
                    Group(list, "Sketches", Api.Get(def, "Sketches"), Sketch);
                    Group(list, "3D sketches", Api.Get(def, "Sketches3D"), Item);
                    Group(list, "Bodies", Api.Get(def, "SurfaceBodies"), Item);
                    Group(list, "Model states", Api.Get(def, "ModelStates"), Item);
                }
                object pars = Api.Get(def, "Parameters");
                if (pars != null)
                {
                    list.Add(new Node
                    {
                        Text = $"Parameters ({Api.Count(pars)})", Obj = pars,
                        Load = () =>
                        {
                            var kinds = new List<Node>();
                            Group(kinds, "User parameters", Api.Get(pars, "UserParameters"), p => Parameter(p, doc));
                            Group(kinds, "Model parameters", Api.Get(pars, "ModelParameters"), p => Parameter(p, doc));
                            Group(kinds, "Reference parameters", Api.Get(pars, "ReferenceParameters"), p => Parameter(p, doc));
                            if (kinds.Count == 0) kinds.AddRange(Api.Items(pars).Select(p => Parameter(p, doc)));
                            return kinds;
                        }
                    });
                }
                Group(list, "iProperties", Api.Get(doc, "PropertySets"), PropertySet);
                Group(list, "Work planes", Api.Get(def, "WorkPlanes"), Item);
                Group(list, "Work axes", Api.Get(def, "WorkAxes"), Item);
                Group(list, "Work points", Api.Get(def, "WorkPoints"), Item);
                Group(list, "Sheets", Api.Get(doc, "Sheets"), Item);
                Node rules = Rules(doc);
                if (rules != null) list.Add(rules);
                Group(list, "Referenced files", Api.Get(doc, "ReferencedDocuments"), Document);
                return WithRaw(list, doc);
            };
            return n;
        }

        // ------------------------------------------------------------------ assembly content

        static Node Occurrence(object occ)
        {
            bool suppressed = Api.Get(occ, "Suppressed") is bool b && b;
            var n = new Node { Text = Api.Text(occ, "Name") + (suppressed ? "  (suppressed)" : ""), Obj = occ };
            n.Row("File", Api.Text(occ, "ReferencedDocumentDescriptor.FullDocumentName"));
            n.Load = () =>
            {
                var list = new List<Node>();
                object subs = Api.Get(occ, "SubOccurrences");
                if (Api.Count(subs) > 0) Group(list, "Components", subs, Occurrence);
                Group(list, "Constraints on this component", Api.Get(occ, "Constraints"), Constraint);
                Group(list, "Joints on this component", Api.Get(occ, "Joints"), Item);
                object doc = Api.Get(occ, "Definition.Document");
                if (doc != null) { Node d = Document(doc); d.Text = "File: " + d.Text; list.Add(d); }
                return WithRaw(list, occ);
            };
            return n;
        }

        static Node Constraint(object c)
        {
            string one = Api.Text(c, "OccurrenceOne.Name"), two = Api.Text(c, "OccurrenceTwo.Name");
            string between = one != "" || two != "" ? $"  {one} <-> {two}" : "";
            var n = new Node { Text = $"{Api.Text(c, "Name")}  [{Api.TypeName(c)}]{between}", Obj = c };
            n.Row("Component one", one).Row("Component two", two);
            n.Row("Offset", Api.Text(c, "Offset.Expression")).Row("Angle", Api.Text(c, "Angle.Expression"));
            n.Load = () => WithRaw(new List<Node>(), c);
            return n;
        }

        // Every file used in the assembly (all levels) with the number of times it is used.
        static List<Node> Quantities(object def)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (object occ in Api.Items(Api.Get(def, "Occurrences.AllLeafOccurrences")))
            {
                string path = Api.Text(occ, "ReferencedDocumentDescriptor.FullDocumentName");
                if (path == "") path = Api.Text(occ, "Name");
                counts[path] = counts.TryGetValue(path, out int k) ? k + 1 : 1;
            }
            return counts.OrderBy(c => FileName(c.Key), StringComparer.OrdinalIgnoreCase)
                .Select(c => new Node { Text = $"{c.Value} x {FileName(c.Key)}" }.Row("File", c.Key).Row("Quantity", c.Value.ToString(Inv))).ToList();
        }

        // ------------------------------------------------------------------ part content

        static Node Feature(object f)
        {
            bool suppressed = Api.Get(f, "Suppressed") is bool b && b;
            var n = new Node { Text = $"{Api.Text(f, "Name")}  [{Api.TypeName(f)}]" + (suppressed ? "  (suppressed)" : ""), Obj = f };
            object sketch = Api.Get(f, "Profile.Parent") ?? Api.Get(f, "Sketch");
            n.Row("Sketch", Api.Text(sketch, "Name")).Row("Distance", Api.Text(f, "Extent.Distance.Expression")).Row("Angle", Api.Text(f, "Extent.Angle.Expression"));
            n.Load = () =>
            {
                var list = new List<Node>();
                object doc = Api.Get(f, "Parent.Document");
                Group(list, "Parameters", Api.Get(f, "Parameters"), p => Parameter(p, doc));
                if (sketch != null) list.Add(Sketch(sketch));
                return WithRaw(list, f);
            };
            return n;
        }

        static Node Sketch(object s)
        {
            var n = new Node { Text = $"{Api.Text(s, "Name")}  [{Api.TypeName(s)}]", Obj = s };
            n.Load = () =>
            {
                var list = new List<Node>();
                Group(list, "Lines", Api.Get(s, "SketchLines"), Item);
                Group(list, "Circles", Api.Get(s, "SketchCircles"), Item);
                Group(list, "Arcs", Api.Get(s, "SketchArcs"), Item);
                Group(list, "Ellipses", Api.Get(s, "SketchEllipses"), Item);
                Group(list, "Splines", Api.Get(s, "SketchSplines"), Item);
                Group(list, "Points", Api.Get(s, "SketchPoints"), Item);
                Group(list, "Dimensions", Api.Get(s, "DimensionConstraints"), Dimension);
                Group(list, "Geometric constraints", Api.Get(s, "GeometricConstraints"), Item);
                return WithRaw(list, s);
            };
            return n;
        }

        static Node Dimension(object d)
        {
            Node n = Item(d);
            string name = Api.Text(d, "Parameter.Name");
            if (name != "") n.Text = $"{name} = {Api.Text(d, "Parameter.Expression")}  [{Api.TypeName(d)}]";
            return n;
        }

        // ------------------------------------------------------------------ parameters, iProperties, iLogic

        static Node Parameter(object p, object doc)
        {
            var n = new Node { Text = $"{Api.Text(p, "Name")} = {Api.Text(p, "Expression")}", Obj = p };
            try { n.Row("Value in its unit", Convert.ToString(Api.Call(Api.Get(doc, "UnitsOfMeasure"), "GetStringFromValue", Api.Get(p, "Value"), Api.Get(p, "Units")), Inv)); } catch { }
            n.Load = () => WithRaw(new List<Node>(), p);
            return n;
        }

        static Node PropertySet(object set)
        {
            string name = Api.Text(set, "DisplayName");
            if (name == "") name = Api.Text(set, "Name");
            var n = new Node { Text = $"{name} ({Api.Count(set)})", Obj = set };
            n.Load = () => Api.Items(set).Select(p =>
            {
                string label = Api.Text(p, "DisplayName");
                if (label == "") label = Api.Text(p, "Name");
                return new Node { Text = $"{label} = {Api.Format(Api.Get(p, "Value"))}", Obj = p };
            }).ToList();
            return n;
        }

        static Node Rules(object doc)
        {
            try
            {
                object auto = Api.Get(Api.Call(Api.Get(App, "ApplicationAddIns"), "ItemById", ILogicAddIn), "Automation");
                var rules = Api.Items(Api.Call(auto, "Rules", doc));
                if (rules.Count == 0) return null;
                return new Node
                {
                    Text = $"iLogic rules ({rules.Count})",
                    Load = () => rules.Select(r => new Node { Text = Api.Text(r, "Name") }.Row("Rule text", Api.Text(r, "Text"))).ToList()
                };
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ properties, export

        // The rows of the property list: the node's own rows, then every API property.
        public static List<KeyValuePair<string, string>> Rows(Node n)
        {
            var rows = new List<KeyValuePair<string, string>>(n.Extra);
            if (n.Obj == null) return rows;
            rows.Add(new KeyValuePair<string, string>("API type", Api.TypeName(n.Obj)));
            foreach (Member m in Api.Members(n.Obj))
                if (m.Name != "Application") rows.Add(new KeyValuePair<string, string>(m.Name, Api.Format(m.Value, m.Enum)));
            return rows;
        }

        static string Clean(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(c >= ' ' || c == '\t' || c == '\n' || c == '\r' ? c : ' ');
            return sb.ToString();
        }

        // Writes the tree below a node with all properties. Each file's content is written once.
        public static int Export(Node root, string file)
        {
            int count = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (XmlWriter w = XmlWriter.Create(file, new XmlWriterSettings { Indent = true }))
            {
                void Write(Node n)
                {
                    if (n.Raw) return;
                    count++;
                    w.WriteStartElement("Node");
                    w.WriteAttributeString("Text", Clean(n.Text));
                    bool repeat = !string.IsNullOrEmpty(n.DocPath) && !seen.Add(n.DocPath);
                    if (repeat) w.WriteAttributeString("ContentWrittenAbove", "true");
                    foreach (var r in Rows(n))
                    {
                        w.WriteStartElement("Property");
                        w.WriteAttributeString("Name", r.Key);
                        w.WriteString(Clean(r.Value));
                        w.WriteEndElement();
                    }
                    if (!repeat && n.Load != null)
                    {
                        List<Node> kids;
                        try { kids = n.Load(); } catch (Exception e) { kids = new List<Node> { new Node { Text = "Could not be read: " + e.Message } }; }
                        foreach (Node k in kids) Write(k);
                    }
                    w.WriteEndElement();
                }
                w.WriteStartElement("InventorReader");
                w.WriteAttributeString("Note", "Raw API lengths are in cm and angles in radians; values marked mm are converted.");
                Write(root);
                w.WriteEndElement();
            }
            return count;
        }
    }
}
