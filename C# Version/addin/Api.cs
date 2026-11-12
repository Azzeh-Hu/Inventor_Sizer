// Late-bound access to the Inventor API: reads any API object by asking the object itself
// (its COM type information) which properties it has. Nothing here names an Inventor type,
// so it shows whatever the installed Inventor version offers.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace InventorSizer.AsmReader
{
    // One property of an API object. Enum = the constant's name when the value is an API enum.
    public class Member { public string Name; public object Value; public string Enum; }

    public static class Api
    {
        [ComImport, Guid("00020400-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDispatch
        {
            [PreserveSig] int GetTypeInfoCount(out int count);
            [PreserveSig] int GetTypeInfo(int index, int lcid, out ComTypes.ITypeInfo info);
        }

        class Getter { public string Name; public Dictionary<long, string> Enum; }
        class TypeData { public string Name = ""; public List<Getter> Getters = new List<Getter>(); }

        static readonly Dictionary<Guid, TypeData> Types = new Dictionary<Guid, TypeData>();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;
        const int FuncRestrictedOrHidden = 0x41, VtUserDefined = 29;

        // ------------------------------------------------------------------ type information

        static TypeData Data(object o)
        {
            if (o == null || !Marshal.IsComObject(o)) return null;
            try
            {
                if (!(o is IDispatch disp) || disp.GetTypeInfo(0, 0, out ComTypes.ITypeInfo ti) != 0 || ti == null) return null;
                ti.GetTypeAttr(out IntPtr pAttr);
                var attr = Marshal.PtrToStructure<ComTypes.TYPEATTR>(pAttr);
                ti.ReleaseTypeAttr(pAttr);
                if (Types.TryGetValue(attr.guid, out TypeData known)) return known;

                var data = new TypeData();
                ti.GetDocumentation(-1, out string typeName, out _, out _, out _);
                data.Name = (typeName ?? "").TrimStart('_');
                for (int i = 0; i < attr.cFuncs; i++)
                {
                    ti.GetFuncDesc(i, out IntPtr pFunc);
                    try
                    {
                        var fd = Marshal.PtrToStructure<ComTypes.FUNCDESC>(pFunc);
                        if (fd.invkind != ComTypes.INVOKEKIND.INVOKE_PROPERTYGET || fd.cParams != 0 || (fd.wFuncFlags & FuncRestrictedOrHidden) != 0) continue;
                        ti.GetDocumentation(fd.memid, out string name, out _, out _, out _);
                        if (string.IsNullOrEmpty(name) || name.StartsWith("_")) continue;
                        data.Getters.Add(new Getter { Name = name, Enum = EnumNames(ti, fd.elemdescFunc.tdesc) });
                    }
                    finally { ti.ReleaseFuncDesc(pFunc); }
                }
                data.Getters = data.Getters.GroupBy(g => g.Name).Select(g => g.First()).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (attr.guid != Guid.Empty) Types[attr.guid] = data;
                return data;
            }
            catch { return null; }
        }

        // value -> constant name, when the property returns an API enum (kUpToDateHealth, ...)
        static Dictionary<long, string> EnumNames(ComTypes.ITypeInfo ti, ComTypes.TYPEDESC desc)
        {
            try
            {
                if (desc.vt != VtUserDefined) return null;
                ti.GetRefTypeInfo(unchecked((int)desc.lpValue.ToInt64()), out ComTypes.ITypeInfo rti);
                rti.GetTypeAttr(out IntPtr pAttr);
                var attr = Marshal.PtrToStructure<ComTypes.TYPEATTR>(pAttr);
                rti.ReleaseTypeAttr(pAttr);
                if (attr.typekind == ComTypes.TYPEKIND.TKIND_ALIAS) return EnumNames(rti, attr.tdescAlias);
                if (attr.typekind != ComTypes.TYPEKIND.TKIND_ENUM) return null;
                var map = new Dictionary<long, string>();
                for (int i = 0; i < attr.cVars; i++)
                {
                    rti.GetVarDesc(i, out IntPtr pVar);
                    try
                    {
                        var vd = Marshal.PtrToStructure<ComTypes.VARDESC>(pVar);
                        rti.GetDocumentation(vd.memid, out string name, out _, out _, out _);
                        map[Convert.ToInt64(Marshal.GetObjectForNativeVariant(vd.desc.lpvarValue), Inv)] = name;
                    }
                    finally { rti.ReleaseVarDesc(pVar); }
                }
                return map;
            }
            catch { return null; }
        }

        // "ExtrudeFeature", "MateConstraint", "ComponentOccurrence", ...
        public static string TypeName(object o)
        {
            if (o == null) return "";
            TypeData d = Data(o);
            if (d != null && d.Name != "") return d.Name;
            return Marshal.IsComObject(o) ? "Object" : o.GetType().Name;
        }

        // ------------------------------------------------------------------ reading

        // Calls a method or reads a property that takes arguments. Throws when Inventor refuses.
        public static object Call(object o, string name, params object[] args)
        {
            try { return o.GetType().InvokeMember(name, Flags | BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, o, args, Inv); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
        }

        // Reads a property path ("ComponentDefinition.Occurrences"); null when any step is not there.
        public static object Get(object o, string path)
        {
            foreach (string name in path.Split('.'))
            {
                if (o == null) return null;
                try { o = o.GetType().InvokeMember(name, Flags | BindingFlags.GetProperty, null, o, null, Inv); }
                catch { return null; }
            }
            return o;
        }

        public static string Text(object o, string path) => Convert.ToString(Get(o, path), Inv) ?? "";

        // Every property of the object that can be read without arguments, with its value.
        // Properties Inventor refuses to give in the object's current state are left out.
        public static List<Member> Members(object o)
        {
            var list = new List<Member>();
            if (o == null) return list;
            TypeData d = Data(o);
            if (d != null)
            {
                foreach (Getter g in d.Getters)
                {
                    object v;
                    try { v = o.GetType().InvokeMember(g.Name, Flags | BindingFlags.GetProperty, null, o, null, Inv); }
                    catch { continue; }
                    string en = null;
                    if (g.Enum != null && v != null && !IsObject(v)) { try { g.Enum.TryGetValue(Convert.ToInt64(v, Inv), out en); } catch { } }
                    list.Add(new Member { Name = g.Name, Value = v, Enum = en });
                }
                return list;
            }
            if (Marshal.IsComObject(o)) return list;
            foreach (PropertyInfo p in o.GetType().GetProperties(Flags).Where(p => p.GetIndexParameters().Length == 0).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                try { list.Add(new Member { Name = p.Name, Value = p.GetValue(o) }); } catch { }
            }
            return list;
        }

        // True for API objects (something to open further), false for numbers, texts, arrays.
        public static bool IsObject(object v) =>
            v != null && !(v is string) && !(v is Array) && !(v is Enum) && !(v is decimal) && !(v is DateTime) && !v.GetType().IsPrimitive;

        public static bool IsCollection(object o)
        {
            if (!IsObject(o)) return false;
            TypeData d = Data(o);
            if (d != null) return d.Getters.Any(g => g.Name == "Count");
            return !Marshal.IsComObject(o) && o is IEnumerable;
        }

        public static int Count(object coll)
        {
            try { return Convert.ToInt32(Get(coll, "Count"), Inv); } catch { return 0; }
        }

        // The items of an API collection; enumerated directly (some collections report a Count
        // that Item(i) cannot reach), with Item(i) as a fallback.
        public static List<object> Items(object coll)
        {
            var list = new List<object>();
            if (coll == null) return list;
            try
            {
                if (coll is IEnumerable en) { foreach (object o in en) if (o != null) list.Add(o); return list; }
            }
            catch { list.Clear(); }
            int n = Count(coll);
            for (int i = 1; i <= n; i++) { try { object o = Call(coll, "Item", i); if (o != null) list.Add(o); } catch { } }
            return list;
        }

        // ------------------------------------------------------------------ display

        static string Num(object v) => Convert.ToDouble(v, Inv).ToString("0.######", Inv);

        static string Coords(object o, double scale, params string[] axes)
        {
            var parts = new List<string>();
            foreach (string a in axes) { object v = Get(o, a); if (v == null) return null; parts.Add((Convert.ToDouble(v, Inv) * scale).ToString("0.###", Inv)); }
            return string.Join(", ", parts);
        }

        // One line for an API object: points and boxes in mm, everything else as "Name (Type)".
        public static string Short(object o)
        {
            if (o == null) return "";
            string t = TypeName(o);
            try
            {
                switch (t)
                {
                    case "Point": { string c = Coords(o, 10, "X", "Y", "Z"); if (c != null) return $"({c}) mm"; break; }
                    case "Point2d": { string c = Coords(o, 10, "X", "Y"); if (c != null) return $"({c}) mm"; break; }
                    case "Vector": case "UnitVector": { string c = Coords(o, 1, "X", "Y", "Z"); if (c != null) return $"({c})"; break; }
                    case "Vector2d": case "UnitVector2d": { string c = Coords(o, 1, "X", "Y"); if (c != null) return $"({c})"; break; }
                    case "Box": case "Box2d":
                    {
                        object min = Get(o, "MinPoint"), max = Get(o, "MaxPoint");
                        string[] axes = t == "Box" ? new[] { "X", "Y", "Z" } : new[] { "X", "Y" };
                        var size = axes.Select(a => ((Convert.ToDouble(Get(max, a), Inv) - Convert.ToDouble(Get(min, a), Inv)) * 10).ToString("0.###", Inv));
                        return $"{string.Join(" x ", size)} mm, from {Short(min)}";
                    }
                    case "Matrix": { string c = Coords(Get(o, "Translation"), 10, "X", "Y", "Z"); if (c != null) return $"position ({c}) mm"; break; }
                }
            }
            catch { }
            string name = Text(o, "Name");
            string kind = IsCollection(o) ? $"{t}, {Count(o)} items" : t;
            return name != "" ? $"{name} ({kind})" : $"({kind})";
        }

        // A property value as text.
        public static string Format(object v, string enumName = null)
        {
            if (v == null) return "";
            if (enumName != null) return $"{enumName} ({Convert.ToString(v, Inv)})";
            if (v is string s) return s;
            if (v is double || v is float) return Num(v);
            if (v is Array a)
            {
                var items = a.Cast<object>().Take(50).Select(x => Format(x)).ToList();
                return string.Join("; ", items) + (a.Length > 50 ? $"; ... ({a.Length} values)" : "");
            }
            if (IsObject(v)) return Short(v);
            return Convert.ToString(v, Inv) ?? "";
        }
    }
}
