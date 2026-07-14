using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Media.Media3D;
using CouchFixationTest;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    /// <summary>
    /// CouchFixationTest — an isolated harness for the "couch is placed wrong when the patient
    /// has fixation gear" problem.
    ///
    /// Workflow (all on a dedicated scratch structure set, so the clinical data is untouched):
    ///   1. Create (or reuse) a structure set called "FixationTest" on the open image.
    ///   2. Add the body with the native ESAPI search (CreateAndSearchBody).
    ///   3. Build the fixation-gear structure: every voxel with HU >= -550 that is not inside the
    ///      body (see <see cref="CouchFixationTest.FixationGear"/>).
    ///
    /// This modifies the patient (new structure set + structures), so it calls BeginModifications.
    /// Coordinates are ESAPI/DICOM patient (LPS): +x = patient left, +y = posterior, +z = cranial.
    /// </summary>
    public class Script
    {
        public Script() { }

        // Dedicated scratch structure set. Reused across runs so the clinical set is never touched.
        private const string SetId = "FixationTest";

        // Starting HU threshold for "denser than air / soft tissue" — tune this from real cases.
        private const double HuThreshold = -550.0;

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
            if (image == null) { log("No image open. Open an image (or a plan/structure set). Aborting."); return; }
            log($"Image: {image.Id}  orientation: {image.ImagingOrientation}  size: {image.XSize}x{image.YSize}x{image.ZSize}");

            patient.BeginModifications();

            // 1) Dedicated structure set on this image.
            StructureSet set = GetOrCreateSet(patient, image, log);
            if (set == null) { log("Could not obtain a structure set. Aborting."); return; }

            // 2) Body via native ESAPI search.
            Structure body = EnsureBody(set, log);
            if (body == null) { log("No body available; cannot exclude the patient interior. Aborting."); return; }
            LogBounds(body, log);
            log("");

            // 3) Fixation gear.
            log($"Building '{FixationGear.StructureId}' at HU >= {HuThreshold:0.#}, excluding body...");
            Structure fixation = FixationGear.Create(set, image, body, HuThreshold, log);

            if (fixation != null)
            {
                log("");
                LogBounds(fixation, log);
                log($"Done. Review '{fixation.Id}' in the '{set.Id}' structure set; tune the threshold and re-run.");
            }
        }

        // Reuse the FixationTest set if it already exists on this image (so re-runs stay clean and
        // don't pile up structure sets); otherwise create a new one and name it.
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
            try
            {
                set.Id = SetId;
                log($"Created structure set '{set.Id}'.");
            }
            catch (Exception ex)
            {
                log($"Created structure set '{set.Id}' (could not rename to '{SetId}': {ex.Message}).");
            }
            return set;
        }

        // Native ESAPI body creation, mirroring PalliativeAutoPlan's EnsureExternalBody.
        private static Structure EnsureBody(StructureSet set, Action<string> log)
        {
            Structure body = set.Structures.FirstOrDefault(s => s.DicomType == "EXTERNAL" && !s.IsEmpty);
            if (body != null)
            {
                log($"Body already present: '{body.Id}'.");
                return body;
            }

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
