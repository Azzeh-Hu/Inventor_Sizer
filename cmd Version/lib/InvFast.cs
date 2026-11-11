// Fast reads from Inventor for large assemblies (GR). PowerShell reads every COM property
// through its own adapter, which costs milliseconds per call; a GR needs thousands of calls.
// This class does the same reads from compiled code through IDispatch, in one call from
// PowerShell. Compiled on first use by lib\Inventor.ps1 (Add-Type). C# 5 syntax, so the
// compiler that ships with Windows PowerShell 5.1 can build it.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

public class InvAssemblyRead
{
    public string Name = "";
    public string FullFileName = "";
    public List<string[]> Occurrences = new List<string[]>();   // name, full path of the file, suppressed, then the
                                                                 // range box in mm: min x, y, z, max x, y, z ("" if unknown)
    public List<string[]> Constraints = new List<string[]>();   // name, type, occurrence 1, occurrence 2, suppressed, offset / angle
    public int Skipped;                                          // items Inventor could not return
    public long Calls;                                           // COM calls made
    public long Milliseconds;
}

public static class InvFast
{
    private static long calls;

    private static object Get(object o, string name)
    {
        calls++;
        return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);
    }

    private static object Item(object collection, int i)
    {
        calls++;
        return collection.GetType().InvokeMember("Item", BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, collection, new object[] { i });
    }

    private static string Text(object v)
    {
        return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    private static string TryText(object o, string a, string b)
    {
        try
        {
            object v = Get(o, a);
            if (b != null) { v = v == null ? null : Get(v, b); }
            return Text(v);
        }
        catch { return ""; }
    }

    public static string TypeName(object o)
    {
        if (o == null) { return ""; }
        try { return Microsoft.VisualBasic.Information.TypeName(o); }
        catch { return o.GetType().Name; }
    }

    // The top-level occurrences and the constraints of an assembly document.
    public static InvAssemblyRead ReadAssembly(object asm)
    {
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        calls = 0;
        InvAssemblyRead r = new InvAssemblyRead();
        r.FullFileName = TryText(asm, "FullFileName", null);
        r.Name = TryText(asm, "DisplayName", null);
        object def = Get(asm, "ComponentDefinition");

        object occs = Get(def, "Occurrences");
        int n = Convert.ToInt32(Get(occs, "Count"));
        for (int i = 1; i <= n; i++)
        {
            object occ;
            try { occ = Item(occs, i); } catch { r.Skipped++; continue; }
            if (occ == null) { r.Skipped++; continue; }
            string file = TryText(occ, "ReferencedDocumentDescriptor", "FullDocumentName");
            if (file.Length == 0)
            {
                try { file = Text(Get(Get(Get(occ, "Definition"), "Document"), "FullFileName")); } catch { }
            }
            string[] box = new string[] { "", "", "", "", "", "" };
            try
            {
                object rb = Get(occ, "RangeBox");
                object mn = Get(rb, "MinPoint"), mx = Get(rb, "MaxPoint");
                string[] axes = new string[] { "X", "Y", "Z" };
                for (int a = 0; a < 3; a++)
                {
                    box[a] = (Convert.ToDouble(Get(mn, axes[a])) * 10).ToString("0.###", CultureInfo.InvariantCulture);       // cm -> mm
                    box[a + 3] = (Convert.ToDouble(Get(mx, axes[a])) * 10).ToString("0.###", CultureInfo.InvariantCulture);
                }
            }
            catch { box = new string[] { "", "", "", "", "", "" }; }
            r.Occurrences.Add(new string[] { TryText(occ, "Name", null), file, TryText(occ, "Suppressed", null),
                                             box[0], box[1], box[2], box[3], box[4], box[5] });
        }

        object cons = Get(def, "Constraints");
        int m = Convert.ToInt32(Get(cons, "Count"));
        for (int i = 1; i <= m; i++)
        {
            object c;
            try { c = Item(cons, i); } catch { r.Skipped++; continue; }
            if (c == null) { r.Skipped++; continue; }
            string type = TypeName(c);
            if (type.EndsWith("Constraint")) { type = type.Substring(0, type.Length - "Constraint".Length); }
            string value = TryText(c, "Offset", "Expression");
            if (value.Length == 0) { value = TryText(c, "Angle", "Expression"); }
            r.Constraints.Add(new string[] {
                TryText(c, "Name", null), type,
                TryText(c, "OccurrenceOne", "Name"), TryText(c, "OccurrenceTwo", "Name"),
                TryText(c, "Suppressed", null), value });
        }
        r.Calls = calls;
        r.Milliseconds = sw.ElapsedMilliseconds;
        return r;
    }

    // Sets the offset of named constraints (divider positions): "name=expression" pairs.
    // Returns the names that could not be set.
    public static List<string> SetConstraintOffsets(object asm, string[] nameEqualsExpression)
    {
        List<string> failed = new List<string>();
        object cons = Get(Get(asm, "ComponentDefinition"), "Constraints");
        foreach (string pair in nameEqualsExpression)
        {
            int k = pair.IndexOf('=');
            string name = pair.Substring(0, k), expr = pair.Substring(k + 1);
            try
            {
                object c = cons.GetType().InvokeMember("Item", BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, cons, new object[] { name });
                object off = Get(c, "Offset");
                off.GetType().InvokeMember("Expression", BindingFlags.SetProperty, null, off, new object[] { expr });
            }
            catch { failed.Add(name); }
        }
        return failed;
    }
}
