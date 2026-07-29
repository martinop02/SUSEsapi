using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using SetupNoteParser;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

// Read-only: never writes to the patient.
[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = false)]

namespace VMS.TPS
{
    /// <summary>
    /// SetupNoteParser demo — a read-only harness that reads each field's setup note and prints the
    /// Lng / Lat / Vrt couch-shift values parsed out of it (see <see cref="NoteShiftParser"/>).
    ///
    /// Every field that carries a setup note is shown: the raw note text, then the parsed values as
    /// signed centimetres (or "n/a" for an axis that was not found). Nothing on the patient is
    /// modified.
    /// </summary>
    public class Script
    {
        public Script() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            LogWindow.Run("Setup note parser", log => Run(context, log));
        }

        private void Run(ScriptContext context, Action<string> log)
        {
            log("=== SetupNoteParser (read-only) ===");

            PlanSetup plan = context.PlanSetup ?? context.ExternalPlanSetup;
            if (plan == null) { log("No plan is open/selected in the context. Aborting."); return; }
            log($"Patient: {context.Patient?.Id}");
            log($"Plan:    {plan.Id}");

            var beams = (plan.Beams ?? Enumerable.Empty<Beam>())
                .OrderByDescending(b => b.IsSetupField)   // setup fields first
                .ToList();
            if (beams.Count == 0) { log("Plan has no beams. Aborting."); return; }

            int notesFound = 0;
            foreach (var beam in beams)
            {
                string note = Safe(() => beam.SetupNote);
                if (string.IsNullOrWhiteSpace(note)) continue;
                notesFound++;

                log("");
                log(new string('=', 70));
                log($"Field [{beam.Id}]   (setup field: {Safe(() => beam.IsSetupField.ToString())})");
                log(new string('-', 70));
                log("raw note:");
                foreach (var line in note.Replace("\r", "").Split('\n'))
                    log("   | " + line);

                NoteShift shift = NoteShiftParser.NoteShiftParser.Parse(note);
                log("parsed:");
                log("   " + NoteShiftParser.NoteShiftParser.Describe(shift));
                if (!shift.Any)
                    log("   (no Lng/Lat/Vrt values recognised on any line)");
            }

            log("");
            if (notesFound == 0)
                log("No setup notes found on any field in this plan.");
            else
                log($"Done. {notesFound} field(s) with a setup note inspected.");
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? string.Empty; }
            catch (Exception ex) { return $"(error: {ex.Message})"; }
        }
    }
}
