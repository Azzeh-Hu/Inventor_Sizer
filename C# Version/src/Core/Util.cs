// Shared helpers: number formatting, the size in a name, the log and the dev log.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace InventorSizer.Core
{
    public static class U
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Parameters encoded in the file name, in order: [project ]<type>-<Width>x<Length>x<Height>
        public static readonly string[] SizeParams = { "Width", "Length", "Height" };
        public static readonly Regex SizeRegex = new Regex(@"(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)", RegexOptions.Compiled);

        public static string F(double v) => v.ToString("0.###", Inv);
        public static double P(string s) => double.Parse(s.Trim().Replace(',', '.'), NumberStyles.Float, Inv);
        public static bool TryP(string s, out double v) => double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, Inv, out v);

        public static string Times => "×";
        public static string Dia => "Ø";

        public static string LeafName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var parts = path.Split('\\', '/');
            return Path.GetFileNameWithoutExtension(parts[parts.Length - 1]);
        }
    }

    // The log the user reads, and the dev log: every step with the time since start and how long
    // it took, so slow or failing steps can be found. The window subscribes to both.
    public static class Log
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();
        private static readonly List<string> DevLines = new List<string>();
        private static int depth;
        public static event Action<string> Written;
        public static event Action<string> DevWritten;

        public static void Write(string message)
        {
            var line = $"{DateTime.Now:HH:mm:ss}  {message}";
            Written?.Invoke(line);
            if (Written == null) Console.WriteLine(line);
            Dev("log: " + message);
        }

        public static void Dev(string message)
        {
            var line = $"{Watch.ElapsedMilliseconds,9:N0} ms  {new string(' ', depth * 2)}{message}";
            lock (DevLines) DevLines.Add(line);
            DevWritten?.Invoke(line);
        }

        public static T Measure<T>(string label, Func<T> body)
        {
            var sw = Stopwatch.StartNew();
            Dev("> " + label);
            depth++;
            try { return body(); }
            catch (Exception e) { Dev($"! {label} failed after {sw.ElapsedMilliseconds} ms: {e.Message}"); throw; }
            finally { depth = Math.Max(0, depth - 1); Dev($"< {label}  {sw.ElapsedMilliseconds} ms"); }
        }

        public static void Measure(string label, Action body) => Measure<object>(label, () => { body(); return null; });

        public static string[] DevSnapshot() { lock (DevLines) return DevLines.ToArray(); }

        public static void SaveDev(string path) => File.WriteAllLines(path, DevSnapshot(), new UTF8Encoding(false));
    }

    public class SizerException : Exception
    {
        public SizerException(string message) : base(message) { }
    }
}
