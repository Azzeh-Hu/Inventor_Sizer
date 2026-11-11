// Core tests, run by tools/build.ps1 -Test (compiled against the running .NET).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InventorSizer.Core;

namespace InventorSizer.Tests
{
    public static partial class Program
    {
        static int fail, pass;
        static string Root, Data;

        public static void Check(string what, object got, object want)
        {
            string g = Convert.ToString(got, U.Inv), w = Convert.ToString(want, U.Inv);
            if (g == w) { pass++; Console.WriteLine($"ok   {what} = {g}"); }
            else { fail++; Console.WriteLine($"FAIL {what} : got '{g}' want '{w}'"); }
        }

        public static void Throws(string what, Action a)
        {
            try { a(); fail++; Console.WriteLine($"FAIL {what} : no exception"); }
            catch (Exception) { pass++; Console.WriteLine($"ok   {what} throws"); }
        }

        public static int Run(string[] args)
        {
            Root = args[0]; Data = args.Length > 1 ? args[1] : "";
            foreach (var t in new Action[] { TestRules, TestStandards, TestHardware, TestDrawings, TestJson, TestGrModel, TestGrEdits, TestGrReal })
            {
                try { t(); } catch (Exception e) { fail++; Console.WriteLine($"FAIL {t.Method.Name}: {e}"); }
            }
            Console.WriteLine($"passed {pass}, FAILURES: {fail}");
            return fail;
        }

    }
}
