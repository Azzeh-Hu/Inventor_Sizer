using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorSizer.Core
{
    public enum SetupAssignmentStatus { Pending, Applied, Skipped, Failed }
    public enum SetupStepAction { Continue, SkipNext, CancelRemaining }

    public sealed class SetupStepDecision
    {
        public SetupStepAction Action = SetupStepAction.Continue;
        public string Expression;
        public SetupAssignment AddedAssignment;
        public string Error = "";
    }

    public sealed class SetupAssignment
    {
        public string Role, File, Target, Expression, Purpose, Unit, Error = "";
        public bool IsFormula;
        public bool PurposeDeclared;
        public SetupAssignmentStatus Status = SetupAssignmentStatus.Pending;

        public static SetupAssignment FromJson(string role, string file, JsonObject item, bool formula)
        {
            string target = item.Str("name");
            string expression = item.Str(formula ? "formula" : "value");
            string label = item.Str("label");
            if (label == "") label = item.Str("purpose");
            string purpose = label;
            if (purpose == "")
            {
                purpose = formula ? DescribePurpose(expression) : DescribeInputPurpose(target);
                if (purpose == "") purpose = $"Purpose not specified for {target}; inspect manually.";
            }
            return new SetupAssignment
            {
                Role = role,
                File = file,
                Target = target,
                Expression = expression,
                Unit = item.Str("unit"),
                Purpose = purpose,
                PurposeDeclared = label != "",
                IsFormula = formula
            };
        }

        static string DescribePurpose(string expression)
        {
            string e = (expression ?? "").Trim();
            switch (e)
            {
                case "Width": return "Overall panel width";
                case "Length": return "Overall panel length";
                case "Height": return "Overall panel height";
                case "HoleEdge": return "Hole distance from the panel edge";
                case "HoleZ": return "Hole height";
                case "HoleDia": return "Hole diameter";
                case "Clearance": return "Assembly clearance";
                case "HoleMidOffsetW": return "Width-wall center-pair offset";
                case "HolePitchL": return "Length-wall hole spacing";
                case "HolePitchW": return "Width-wall hole spacing";
                case "HoleCountL - 1 ul": return "Length-wall hole-pattern count";
                case "HoleCountW - 1 ul": return "Width-wall hole-pattern count";
                case "HingeEdge": return "Hinge setback from the panel end";
                case "HandleEdge": return "Handle setback from the panel end";
                case "HingePitch": return "Hinge spacing";
                case "HandlePitch": return "Handle spacing";
                case "HingeCount": return "Hinge-pattern count";
                case "HandleCount": return "Handle-pattern count";
                case "InSplit": return "Inner-panel count";
                case "InWidth": return "Inner-panel spacing";
                case "HoleEdge - Stärke - Clearance": return "Inner-panel hole setback from the panel edge";
                case "( Width - 2 * Stärke - 2 * Clearance ) / InSplit": return "Inner-panel width";
                case "Length - 2 * Stärke - 2 * Clearance": return "Inner-panel length";
                case "Height - Stärke": return "Inner-panel height";
                default: return "";
            }
        }

        static string DescribeInputPurpose(string target)
        {
            switch ((target ?? "").Trim())
            {
                case "Width": return "Overall panel width input";
                case "Length": return "Overall panel length input";
                case "Height": return "Overall panel height input";
                case "HoleEdge": return "Hole distance from the panel edge input";
                case "HoleZ": return "Hole height input";
                case "HoleDia":
                case "HoleDia_sides":
                case "HoleDia_handle":
                case "HoleDia_hinge_small":
                case "HoleDia_hinge_large": return "Hole diameter input";
                case "Clearance": return "Assembly clearance input";
                case "HolesL":
                case "HoleCountL": return "Length-wall hole count input";
                case "HolesW":
                case "HoleCountW": return "Width-wall hole count input";
                case "HoleStartL": return "First length-wall hole position input";
                case "HolePitchL": return "Length-wall hole spacing input";
                case "HoleStartW": return "First width-wall hole position input";
                case "HolePitchW": return "Width-wall hole spacing input";
                case "HoleMidOffsetW": return "Width-wall center-pair offset input";
                case "InSplit": return "Inner-panel count input";
                case "InWidth": return "Inner-panel spacing input";
                case "HingeEdge": return "Hinge setback input";
                case "HingePitch": return "Hinge spacing input";
                case "HingeCount": return "Hinge count input";
                case "HandleEdge": return "Handle setback input";
                case "HandlePitch": return "Handle spacing input";
                case "HandleCount": return "Handle count input";
                default: return $"Input parameter: {target}";
            }
        }

        public string ChecklistLine()
        {
            string status = Status.ToString().ToUpperInvariant();
            string line = $"{status} | {Role} | {File} | {Target} = {Expression} | {Purpose}";
            return Error == "" ? line : line + " | Error: " + Error;
        }
    }

    public sealed class MasterSetupReport
    {
        public readonly List<SetupAssignment> Assignments = new List<SetupAssignment>();
        public bool Cancelled;
        public string FinalizationError = "";
        public bool Complete => !Cancelled && FinalizationError == "" && Assignments.Count > 0 &&
                                Assignments.All(a => a.Status == SetupAssignmentStatus.Applied);

        public string Format()
        {
            var lines = new List<string>
            {
                $"Assignments: {Assignments.Count(a => a.Status == SetupAssignmentStatus.Applied)} applied, " +
                $"{Assignments.Count(a => a.Status == SetupAssignmentStatus.Skipped)} skipped, " +
                $"{Assignments.Count(a => a.Status == SetupAssignmentStatus.Failed)} failed" +
                (Cancelled ? ", remaining setup cancelled" : "")
            };
            lines.AddRange(Assignments.Select(a => a.ChecklistLine()));
            if (FinalizationError != "") lines.Add("SETUP FINALIZATION FAILED | " + FinalizationError);
            lines.Add(Complete
                ? "Setup complete. Changes are unsaved until you choose a save option."
                : "Setup incomplete. Changes are unsaved; close without saving to undo them.");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
