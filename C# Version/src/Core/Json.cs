// Small JSON reader / writer with no dependencies (the standards and design files).
// Values: Dictionary<string, object> (ordered by insertion), List<object>, double, string, bool, null.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace InventorSizer.Core
{
    public sealed class JsonObject : Dictionary<string, object>
    {
        private readonly List<string> order = new List<string>();
        public IEnumerable<string> OrderedKeys => order;

        public new object this[string key]
        {
            get { object v; return TryGetValue(key, out v) ? v : null; }
            set { if (!ContainsKey(key)) order.Add(key); base[key] = value; }
        }

        public new void Add(string key, object value) { this[key] = value; }

        public new bool Remove(string key) { order.Remove(key); return base.Remove(key); }

        public string Str(string key, string def = "") { var v = this[key]; return v == null ? def : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public double Num(string key, double def = 0) { var v = this[key]; return v is double d ? d : v is bool b ? (b ? 1 : 0) : v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : def; }
        public bool Bool(string key, bool def = false) { var v = this[key]; return v is bool b ? b : v == null ? def : v is double d ? d != 0 : def; }
        public JsonObject Obj(string key) => this[key] as JsonObject;
        public List<object> Arr(string key) => this[key] as List<object> ?? new List<object>();
    }

    public static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            if (text.Length > 0 && text[0] == '﻿') i = 1;   // BOM
            var v = ReadValue(text, ref i);
            SkipWs(text, ref i);
            if (i < text.Length) throw new FormatException($"Unexpected text after the JSON value at {i}.");
            return v;
        }

        private static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object ReadValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON.");
            char c = s[i];
            if (c == '{')
            {
                var o = new JsonObject(); i++;
                SkipWs(s, ref i);
                if (s[i] == '}') { i++; return o; }
                while (true)
                {
                    SkipWs(s, ref i);
                    string key = ReadString(s, ref i);
                    SkipWs(s, ref i);
                    if (s[i] != ':') throw new FormatException($"':' expected at {i}.");
                    i++;
                    o[key] = ReadValue(s, ref i);
                    SkipWs(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return o; }
                    throw new FormatException($"',' or '}}' expected at {i}.");
                }
            }
            if (c == '[')
            {
                var a = new List<object>(); i++;
                SkipWs(s, ref i);
                if (s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(ReadValue(s, ref i));
                    SkipWs(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw new FormatException($"',' or ']' expected at {i}.");
                }
            }
            if (c == '"') return ReadString(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException($"Unexpected '{c}' at {i}.");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string ReadString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException($"'\"' expected at {i}.");
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int n) { sb.Append('\n').Append(' ', n * 2); }

        private static void WriteValue(StringBuilder sb, object v, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: WriteString(sb, s); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case JsonObject o:
                    if (o.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    bool first = true;
                    foreach (var k in o.OrderedKeys)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Indent(sb, depth + 1);
                        WriteString(sb, k); sb.Append(": ");
                        WriteValue(sb, o[k], depth + 1);
                    }
                    Indent(sb, depth); sb.Append('}');
                    break;
                case System.Collections.IDictionary dict:
                    var jo = new JsonObject();
                    foreach (System.Collections.DictionaryEntry e in dict) jo[Convert.ToString(e.Key, CultureInfo.InvariantCulture)] = e.Value;
                    WriteValue(sb, jo, depth);
                    break;
                case System.Collections.IEnumerable list:
                    var items = new List<object>();
                    foreach (var x in list) items.Add(x);
                    if (items.Count == 0) { sb.Append("[]"); break; }
                    bool simple = items.TrueForAll(x => !(x is JsonObject) && !(x is System.Collections.IDictionary) && !(x is System.Collections.IList));
                    sb.Append('[');
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (i > 0) sb.Append(simple ? ", " : ",");
                        if (!simple) Indent(sb, depth + 1);
                        WriteValue(sb, items[i], depth + 1);
                    }
                    if (!simple) Indent(sb, depth);
                    sb.Append(']');
                    break;
                default: WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture)); break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
