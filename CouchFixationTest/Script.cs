using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CouchFixationTest;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    /// <summary>
    /// CouchFixationTest — isolates the "couch placed wrong with fixation gear" problem.
    ///
    /// Two-pass fixation extraction (all on a scratch "FixationTest" structure set):
    ///   1. Coarse segment: HU >= CoarseHuThreshold, minus body -> rough/patchy fixation.
    ///   2. Save the original body, then OR the coarse fixation into the body so the external bulges
    ///      to include the fixation bulk.
    ///   3. Add the couch. Because the body now includes the fixation, the couch lands correctly.
    ///   4. Refined segment: a much lower threshold, keeping only voxels below (posterior to) the
    ///      ORIGINAL body and outside the couch -> a clean base-fixation structure.
    ///   5. Merge the refined fixation into the body.
    ///
    /// Modifies the patient (structure set + structures), so it calls BeginModifications.
    /// Coordinates are ESAPI/DICOM patient (LPS): +x = left, +y = posterior, +z = cranial.
    /// </summary>
    public class Script
    {
        public Script() { }

        private const string SetId = "FixationTest";
        private const double CoarseHuThreshold = -550.0;   // rough pass (dense fixation + couch)
        private const double RefinedHuThreshold = -900.0;  // final pass, spatially constrained (catches foam)
        private const string CouchModel = "Exact_IGRT_Couch_Top_thick"; // must match Eclipse (as PalliativeAutoPlan)

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            LogWindow.Run("Couch fixation test", log => Run(context, log));
        }

        private void Run(ScriptContext context, Action<string> log)
        {
            log("=== CouchFixationTest ===");

            Patient patient = context.Patient;
            if (patient == null) { log("No patient is open. Aborting."); return; }
            log($"Patient: {patient.Id}");

            Image image = context.Image
                       ?? context.StructureSet?.Image
                       ?? context.PlanSetup?.StructureSet?.Image;
            if (image == null) { log("No image open. Aborting."); return; }
            log($"Image: {image.Id}  orientation: {image.ImagingOrientation}  size: {image.XSize}x{image.YSize}x{image.ZSize}");

            patient.BeginModifications();

            StructureSet set = GetOrCreateSet(patient, image, log);
            if (set == null) { log("Could not obtain a structure set. Aborting."); return; }

            Structure body = EnsureBody(set, log);
            if (body == null) { log("No body available. Aborting."); return; }
            LogBounds(body, log);
            log("");

            // 1) Coarse fixation.
            log($"[1/4] Coarse fixation (HU >= {CoarseHuThreshold:0.#}, minus body)...");
            Structure coarse = FixationGear.Segment(set, set.Image, "fixation_coarse", Colors.Gray,
                                                    CoarseHuThreshold, new[] { body }, null, log);
            if (coarse == null) { log("Coarse pass failed. Aborting."); return; }
            log("");

            // 2) Preserve the original body, then OR the coarse fixation into the body.
            log("[2/4] Saving original body and merging coarse fixation into body...");
            Structure bodyOrig = CopyStructure(set, body, "body_orig", Colors.DimGray, log);
            OrInto(body, coarse, log);
            log($"  Body volume after merge: {SafeVolume(body):0.0} cc.");
            log("");

            // 3) Add the couch (now that body includes the fixation bulk, it places correctly).
            log("[3/4] Adding couch...");
            IList<Structure> couch = EnsureCouch(set, log);
            log("");

            // 4) Refined fixation: low threshold, below the ORIGINAL body, outside the couch.
            log($"[4/4] Refined fixation (HU >= {RefinedHuThreshold:0.#}, below original body, minus couch)...");
            var erase = new List<Structure> { bodyOrig };
            erase.AddRange(couch);
            Structure fixation = FixationGear.Segment(set, set.Image, "fixation_gear",
                                                      Color.FromRgb(0, 220, 220),
                                                      RefinedHuThreshold, erase, bodyOrig, log);

            if (fixation != null)
            {
                log("");
                LogBounds(fixation, log);

                // 5) Merge the clean fixation into the body so the external includes it.
                log("Merging fixation into body...");
                OrInto(body, fixation, log);
                log($"  Body volume after merge: {SafeVolume(body):0.0} cc.");

                log($"Done. Final structure '{fixation.Id}' (and merged into body) in the '{set.Id}' set. Tune thresholds and re-run.");
            }
        }

        // Reuse the FixationTest set on this image if present (keeps re-runs clean); else create it.
        private static StructureSet GetOrCreateSet(Patient patient, Image image, Action<string> log)
        {
            StructureSet existing = patient.StructureSets
                .FirstOrDefault(s => s.Id == SetId && s.Image != null && s.Image.Id == image.Id);
            if (existing != null)
            {
                log($"Reusing existing structure set '{existing.Id}'.");
                return existing;
            }

            StructureSet set = image.CreateNewStructureSet();
            try { set.Id = SetId; log($"Created structure set '{set.Id}'."); }
            catch (Exception ex) { log($"Created structure set '{set.Id}' (rename to '{SetId}' failed: {ex.Message})."); }
            return set;
        }

        // Native ESAPI body creation, mirroring PalliativeAutoPlan's EnsureExternalBody.
        private static Structure EnsureBody(StructureSet set, Action<string> log)
        {
            Structure body = set.Structures.FirstOrDefault(s => s.DicomType == "EXTERNAL" && !s.IsEmpty);
            if (body != null) { log($"Body already present: '{body.Id}'."); return body; }

            try
            {
                SearchBodyParameters p = set.GetDefaultSearchBodyParameters();
                body = set.CreateAndSearchBody(p);
                log($"Created body '{body.Id}' via CreateAndSearchBody.");
                return body;
            }
            catch (Exception ex)
            {
                log("ERROR: could not create a body structure: " + ex.Message);
                return null;
            }
        }

        // Copies a structure's volume into a fresh structure with the given id.
        private static Structure CopyStructure(StructureSet set, Structure src, string id, Color color, Action<string> log)
        {
            Structure existing = set.Structures.FirstOrDefault(s => s.Id == id);
            if (existing != null && set.CanRemoveStructure(existing)) set.RemoveStructure(existing);

            Structure copy = set.AddStructure("CONTROL", id);
            copy.SegmentVolume = src.SegmentVolume;
            copy.Color = color;
            log($"  Saved '{src.Id}' as '{copy.Id}'.");
            return copy;
        }

        // target = target OR add (match resolution first).
        private static void OrInto(Structure target, Structure add, Action<string> log)
        {
            try
            {
                if (add.IsHighResolution && !target.IsHighResolution) target.ConvertToHighResolution();
                target.SegmentVolume = target.SegmentVolume.Or(add.SegmentVolume);
            }
            catch (Exception ex)
            {
                log("  WARNING: could not OR into '" + target.Id + "': " + ex.Message);
            }
        }

        // Adds the treatment couch, mirroring PalliativeAutoPlan's EnsureCouch. Returns the couch
        // (SUPPORT) structures present afterwards.
        private static IList<Structure> EnsureCouch(StructureSet set, Action<string> log)
        {
            if (set.Structures.Any(s => s.DicomType == "SUPPORT" && !s.IsEmpty))
                log("  Couch (support) already present.");
            else
            {
                string canError;
                if (!set.CanAddCouchStructures(out canError))
                {
                    log("  Cannot add couch structures: " + canError);
                    return new List<Structure>();
                }

                try
                {
                    IReadOnlyList<Structure> added;
                    bool imageResized;
                    string error;
                    bool ok = set.AddCouchStructures(
                        CouchModel,
                        PatientOrientation.NoOrientation,
                        RailPosition.Out, RailPosition.Out,
                        null, null, null,
                        out added, out imageResized, out error);

                    if (ok) log($"  Added couch '{CouchModel}' ({added.Count} structure(s)){(imageResized ? " — image resized." : ".")}");
                    else log($"  Could not add couch '{CouchModel}': {error} (check the id matches Eclipse).");
                }
                catch (Exception ex)
                {
                    log("  WARNING: couch creation failed: " + ex.Message);
                }
            }

            return set.Structures.Where(s => s.DicomType == "SUPPORT" && !s.IsEmpty).ToList();
        }

        private static double SafeVolume(Structure s)
        {
            try { return s.Volume; } catch { return double.NaN; }
        }

        private static void LogBounds(Structure s, Action<string> log)
        {
            try
            {
                Rect3D b = s.MeshGeometry.Bounds;
                log($"  {s.DicomType,-8} '{s.Id}'  x[{b.X:0} .. {b.X + b.SizeX:0}]  y[{b.Y:0} .. {b.Y + b.SizeY:0}]  z[{b.Z:0} .. {b.Z + b.SizeZ:0}] (mm)");
            }
            catch (Exception ex)
            {
                log($"  {s.DicomType} '{s.Id}': bounds unavailable ({ex.Message}).");
            }
        }
    }
}
