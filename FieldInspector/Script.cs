using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FieldInspector;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

// Read-only inspector: never writes to the patient, so IsWriteable = false.
[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = false)]

namespace VMS.TPS
{
    /// <summary>
    /// FieldInspector — a read-only harness that dumps the "interesting" per-field values so you
    /// can eyeball them in Eclipse:
    ///
    ///   * Setup notes on imaging / setup fields   (Beam.SetupNote, Beam.SetupTechnique)
    ///   * Table-top (couch) positions per beam    (ControlPoint.TableTop{Lateral,Longitudinal,Vertical}Position)
    ///   * Computed delta couch shift              (each treatment beam vs. the reference setup field)
    ///
    /// It touches nothing on the patient — no BeginModifications, no structure sets. Everything is a
    /// plain property read, collected into a scrollable log window at the end.
    ///
    /// Coordinate note: table-top positions are in millimetres, in the IEC TABLE TOP coordinate
    /// system, exactly as ESAPI reports them. They are frequently unset (double.NaN) unless couch
    /// coordinates were entered/registered for the plan, so every value is NaN-guarded below.
    /// </summary>
    public class Script
    {
        public Script() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            LogWindow.Run("Field inspector", log => Run(context, log));
        }

        private void Run(ScriptContext context, Action<string> log)
        {
            log("=== FieldInspector (read-only) ===");

            Patient patient = context.Patient;
            if (patient == null) { log("No patient is open. Aborting."); return; }
            log($"Patient: {patient.Id}");
            if (context.Course != null) log($"Course:  {context.Course.Id}");

            // Prefer every plan the user put in scope; fall back to the single active plan.
            var plans = (context.PlansInScope ?? Enumerable.Empty<PlanSetup>()).ToList();
            if (plans.Count == 0 && context.PlanSetup != null) plans.Add(context.PlanSetup);
            if (plans.Count == 0) { log("No plan in scope. Open/select a plan and re-run. Aborting."); return; }

            foreach (var plan in plans)
            {
                log("");
                log(new string('=', 78));
                log($"PLAN: {plan.Id}   (Course: {plan.Course?.Id})");
                log(new string('=', 78));
                InspectPlan(plan, log);
            }
        }

        private static void InspectPlan(PlanSetup plan, Action<string> log)
        {
            var beams = plan.Beams?.ToList() ?? new List<Beam>();
            if (beams.Count == 0) { log("  (plan has no beams)"); return; }

            var setupFields = beams.Where(b => b.IsSetupField).ToList();
            var treatFields = beams.Where(b => !b.IsSetupField).ToList();

            // ---- Setup / imaging fields: setup notes -------------------------------------------
            log("");
            log($"-- Setup / imaging fields ({setupFields.Count}) --");
            if (setupFields.Count == 0)
            {
                log("  (none)");
            }
            else
            {
                foreach (var b in setupFields)
                {
                    log($"  [{b.Id}]");
                    log($"      SetupTechnique : {Safe(() => b.SetupTechnique.ToString())}");
                    log($"      SetupNote      : {Quote(Safe(() => b.SetupNote))}");
                    log($"      Comment        : {Quote(Safe(() => b.Comment))}");
                    log($"      TableTop (lat/lng/vrt mm): {TableTop(b)}");
                }
            }

            // ---- Treatment fields ---------------------------------------------------------------
            log("");
            log($"-- Treatment fields ({treatFields.Count}) --");
            if (treatFields.Count == 0)
            {
                log("  (none)");
            }
            else
            {
                foreach (var b in treatFields)
                {
                    log($"  [{b.Id}]  energy={Safe(() => b.EnergyModeDisplayName)}  MLCType={Safe(() => b.MLCPlanType.ToString())}");
                    log($"      TableTop (lat/lng/vrt mm): {TableTop(b)}");
                }
            }

            // ---- Delta couch shift: each treatment beam vs. the reference setup field -----------
            // There is no dedicated "delta" property in ESAPI; we difference the absolute table-top
            // positions ourselves. The first setup field is used as the reference frame.
            log("");
            log("-- Delta couch shift (treatment - setup reference), mm --");
            var reference = setupFields.FirstOrDefault();
            if (reference == null)
            {
                log("  (no setup field to use as reference)");
            }
            else if (treatFields.Count == 0)
            {
                log("  (no treatment fields)");
            }
            else
            {
                var r = FirstCp(reference);
                log($"  reference setup field: [{reference.Id}]  table (lat/lng/vrt) = {TableTop(reference)}");
                foreach (var b in treatFields)
                {
                    var t = FirstCp(b);
                    log($"  [{b.Id}]  dLat={Diff(t?.lat, r?.lat)}  dLng={Diff(t?.lng, r?.lng)}  dVrt={Diff(t?.vrt, r?.vrt)}");
                }
            }
        }

        // ---- helpers ---------------------------------------------------------------------------

        // Table-top position of a beam's first control point. Null if there are no control points.
        private static (double lat, double lng, double vrt)? FirstCp(Beam b)
        {
            try
            {
                var cp = b.ControlPoints?.FirstOrDefault();
                if (cp == null) return null;
                return (cp.TableTopLateralPosition, cp.TableTopLongitudinalPosition, cp.TableTopVerticalPosition);
            }
            catch { return null; }
        }

        private static string TableTop(Beam b)
        {
            var cp = FirstCp(b);
            if (cp == null) return "(no control points)";
            return $"{Fmt(cp.Value.lat)} / {Fmt(cp.Value.lng)} / {Fmt(cp.Value.vrt)}";
        }

        // Difference of two possibly-NaN/null values, formatted; "n/a" if either side is missing.
        private static string Diff(double? a, double? b)
        {
            if (a == null || b == null || double.IsNaN(a.Value) || double.IsNaN(b.Value)) return "n/a";
            return (a.Value - b.Value).ToString("0.0");
        }

        private static string Fmt(double v) => double.IsNaN(v) ? "NaN" : v.ToString("0.0");

        private static string Quote(string s) => s == null ? "(null)" : $"\"{s}\"";

        private static string Safe(Func<string> f)
        {
            try { return f() ?? "(null)"; }
            catch (Exception ex) { return $"(error: {ex.Message})"; }
        }
    }
}
