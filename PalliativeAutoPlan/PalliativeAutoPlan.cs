using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using PalliativeAutoPlan;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    /// <summary>
    /// PalliativeAutoPlan entry point.
    ///
    /// Workflow:
    ///   1. Scan the open patient's prescriptions for vertebra names (e.g. "Th9", "L1-L4").
    ///   2. Segment those vertebrae plus the fixed OARs on the newest 3D image (latest CBCT).
    ///   3. For each matching prescription create a plan (MVx_NAME_8) in the prescription's
    ///      course with CTV/PTV, copy the Rx dose, add a VMAT arc and optimize.
    ///
    /// Progress is shown in a <see cref="LogWindow"/> (a clean replacement for the Eclipse
    /// console). The work runs on the ESAPI main thread inside that window; the heavy lifting
    /// lives in the separate modules so this class stays a thin orchestrator.
    /// </summary>
    public class Script
    {
        public Script() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context /*, System.Windows.Window window, ScriptEnvironment environment*/)
        {
            // Ask which beam technique to use before opening the log window. A null result means the
            // user cancelled/closed the chooser, so nothing runs.
            PlanTechnique? technique = TechniqueSelector.Show();
            if (technique == null)
                return;

            LogWindow.RunWithLog(log => RunWorkflow(context, technique.Value, log));
        }

        private void RunWorkflow(ScriptContext context, PlanTechnique technique, Action<string> log)
        {
            log("=== PalliativeAutoPlan ===");
            log($"Technique: {technique}.");
            var total = Timing.Start();

            Patient patient = context.Patient;
            if (patient == null)
            {
                log("No patient is open. Aborting.");
                return;
            }

            // 1) Find prescriptions whose name is a vertebra or vertebra range.
            log("Scanning prescriptions for vertebra names...");
            List<PrescriptionMatch> matches = PrescriptionScanner.FindVertebraPrescriptions(patient, log);
            if (matches.Count == 0)
            {
                log("No vertebra prescriptions found. Aborting.");
                return;
            }
            log($"{matches.Count} matching prescription(s).");

            // 2) Single segmentation run: the union of all prescription vertebrae plus the
            //    fixed set of OARs (heart, lungs, spinal cord, kidneys), which are always segmented.
            List<string> vertebraIds = matches.SelectMany(m => m.SegmentIds).Distinct().ToList();
            List<string> finalIds = vertebraIds.Concat(Anatomy.Oars).Distinct().ToList();
            log("Vertebrae to segment: " + string.Join(", ", vertebraIds));
            log("OARs (always): " + string.Join(", ", Anatomy.Oars));

            // Newest 3D image (expected to be the latest CBCT).
            Image image = ImageSelector.SelectImage(context, log);
            if (image == null)
            {
                log("No image available to segment on. Aborting.");
                return;
            }
            log($"Segmenting on image '{image.Id}' (series '{image.Series?.Id}', created {image.CreationDateTime}).");

            patient.BeginModifications();

            context.Image.Series.SetImagingDevice("Def_CTScanner");

            StructureSet set;
            bool reuseStructures = RunConfig.StructuresAlreadyGenerated;

            if (reuseStructures)
            {
                // Reuse the structure set already open in the Eclipse context (e.g. from an earlier
                // VMAT run on this same image) instead of creating a new one and re-running
                // TotalSegmentator.
                set = context.StructureSet;
                if (set == null)
                {
                    log("ERROR: 'Structures already generated' was checked, but no structure set is open in the context. Open the structure set to reuse and re-run. Aborting.");
                    return;
                }
                if (set.Image == null || set.Image.Id != image.Id)
                {
                    log($"ERROR: 'Structures already generated' was checked, but the open structure set '{set.Id}' belongs to image '{set.Image?.Id}', not the selected image '{image.Id}'. Open the matching structure set and re-run. Aborting.");
                    return;
                }
                log($"Reusing existing structure set '{set.Id}' (structures already generated).");
            }
            else
            {
                // New structure set on the chosen image; all vertebrae + OARs + CTVs/PTVs go here.
                set = image.CreateNewStructureSet();
                log($"Created structure set '{set.Id}'.");

                // Give it a distinguishable, collision-free Id ("TotalSegAuto", "TotalSegAuto1", ...)
                // rather than the Eclipse default, so it's easy to recognize this auto-created set.
                var takenIds = new HashSet<string>(
                 patient.StructureSets.Where(s => s.UID != set.UID).Select(s => s.Id),
                StringComparer.OrdinalIgnoreCase);

                string newId = "TotalSegAuto";
                for (int n = 1; takenIds.Contains(newId); n++) newId = "TotalSegAuto" + n;

                set.Id = newId;
            }

            // A new structure set has no external/BODY contour; beam placement and optimization
            // (PlanFactory) crash without one, so create it now if it is missing.
            EnsureExternalBody(set, log);

            // 3) Segment the vertebrae + OARs — or, if they were already generated by an earlier
            //    run on this image, just verify they're all there and abort clearly if not.
            if (reuseStructures)
            {
                bool allPresent = Segmenter.VerifyStructuresExist(set, finalIds, log);
                if (!allPresent)
                {
                    log("ERROR: one or more required structures are missing or empty in the existing structure set. Aborting before plan creation.");
                    return;
                }
            }
            else
            {
                bool segmented = Timing.Run("Segmentation", log, () => Segmenter.Segment(image, finalIds, set, log));
                if (!segmented)
                {
                    log("Segmentation did not run; aborting before plan creation.");
                    return;
                }
            }

            // Optional fixation handling (pre-couch): clean the body, segment a rough fixation and
            // merge it into the body so the couch places correctly on a patient tilted by the gear.
            // Skipped when reusing structures — the earlier run already baked any fixation handling
            // into this structure set's body.
            Fixation.State fixState = null;
            if (RunConfig.Fixation && !reuseStructures)
            {
                log("Fixation gear handling (pre-couch)...");
                Structure body = set.Structures.FirstOrDefault(s => s.DicomType == "EXTERNAL" && !s.IsEmpty);
                fixState = Timing.Run("Fixation pre-couch", log, () => Fixation.PreCouch(set, body, log));
            }
            else if (RunConfig.Fixation && reuseStructures)
            {
                log("Fixation gear handling skipped (structures already generated; reusing the earlier run's fixation handling).");
            }

            // Add the treatment couch so it is part of the structure set used for optimization
            // and dose calculation.
            EnsureCouch(set, log);

            // Optional fixation handling (post-couch): segment the clean fixation (below the body,
            // outside the couch), merge it into the body, and delete the rough fixation.
            if (RunConfig.Fixation && fixState != null)
            {
                log("Fixation gear handling (post-couch)...");
                Timing.Run("Fixation post-couch", log, () => Fixation.PostCouch(set, fixState, log));
            }

            // 4) One plan per prescription. MVx continues from the global maximum; the number
            //    only advances when a plan is actually created.
            int mv = PlanFactory.GetHighestMvNumber(patient);
            log($"Highest existing MV number: {mv}.");

            int created = 0;
            foreach (PrescriptionMatch match in matches)
            {
                try
                {
                    PlanSetup plan = Timing.Run($"Plan {match.Name}", log, () => PlanFactory.CreatePlan(match, set, mv + 1, technique, log));
                    if (plan != null)
                    {
                        mv++;
                        created++;
                    }
                }
                catch (Exception ex)
                {
                    log($"ERROR creating plan for '{match.Name}': {ex.Message}");
                }
            }

            Timing.Stop(total, "TOTAL", log);
            log($"Done. Created {created} of {matches.Count} plan(s). You can close this window.");
        }

        // A newly created structure set has no external/BODY contour, and beam placement /
        // optimization in PlanFactory fails without one. Create it via Search Body if it is missing.
        private static void EnsureExternalBody(StructureSet set, Action<string> log)
        {
            Structure body = set.Structures.FirstOrDefault(s => s.DicomType == "EXTERNAL");
            if (body != null)
            {
                log($"External (BODY) structure present: '{body.Id}'.");
                return;
            }

            try
            {
                SearchBodyParameters p = set.GetDefaultSearchBodyParameters();
                body = set.CreateAndSearchBody(p);
                log($"No external structure found; created body '{body.Id}'.");
            }
            catch (Exception ex)
            {
                log("WARNING: could not auto-create a body structure: " + ex.Message);
            }
        }

        // Couch profile id — must match a couch model configured in this site's Eclipse.
        private const string CouchModel = "Exact_IGRT_Couch_Top_thick";

        // Adds the treatment couch to the structure set so it is included in optimization / dose
        // calculation. Skips (with a log) if a couch is already present or cannot be added.
        private static void EnsureCouch(StructureSet set, Action<string> log)
        {
            if (set.Structures.Any(s => s.DicomType == "SUPPORT"))
            {
                log("Couch (support structure) already present.");
                return;
            }

            string canError;
            if (!set.CanAddCouchStructures(out canError))
            {
                log("Cannot add couch structures: " + canError);
                return;
            }

            try
            {
                IReadOnlyList<Structure> added;
                bool imageResized;
                string error;
                bool ok = set.AddCouchStructures(
                    CouchModel,
                    PatientOrientation.NoOrientation,    // take orientation from the image
                    RailPosition.Out, RailPosition.Out,  // rails retracted / out of field
                    null, null, null,                    // surface/interior/rail HU from the couch profile
                    out added, out imageResized, out error);

                if (ok)
                {
                    string resizedNote = imageResized ? " — image was resized to fit." : ".";
                    log($"Added couch '{CouchModel}' ({added.Count} structure(s)){resizedNote}");
                }
                else
                {
                    log($"Could not add couch '{CouchModel}': {error} (check the CouchModel id matches Eclipse).");
                }
            }
            catch (Exception ex)
            {
                log("WARNING: couch creation failed: " + ex.Message);
            }
        }
    }
}
