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
    /// Current step: extract the fixation gear as its own structure so we can reason about it.
    /// It is defined as every voxel above a Hounsfield-unit threshold that is NOT inside the body
    /// (see <see cref="CouchFixationTest.FixationGear"/>). Starting threshold: -550 HU.
    ///
    /// This modifies the plan (it adds a structure), so it calls BeginModifications. Coordinates are
    /// ESAPI/DICOM patient (LPS): +x = patient left, +y = posterior, +z = cranial.
    /// </summary>
    public class Script
    {
        public Script() { }

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

            StructureSet set = context.StructureSet ?? context.PlanSetup?.StructureSet;
            if (set == null)
            {
                log("No structure set in context (open a plan or structure set). Aborting.");
                return;
            }
            log($"Structure set: {set.Id}");

            Image img = set.Image;
            if (img == null) { log("Structure set has no image. Aborting."); return; }
            log($"Image: {img.Id}  orientation: {img.ImagingOrientation}  size: {img.XSize}x{img.YSize}x{img.ZSize}");

            Structure body = set.Structures.FirstOrDefault(s => s.DicomType == "EXTERNAL" && !s.IsEmpty);
            if (body == null)
            {
                log("No non-empty EXTERNAL (BODY) structure found — needed to exclude the patient. Aborting.");
                return;
            }
            LogBounds(body, log);
            foreach (Structure c in set.Structures.Where(s => s.DicomType == "SUPPORT" && !s.IsEmpty))
                LogBounds(c, log);
            log("");

            // Adding a structure is a modification.
            patient.BeginModifications();

            log($"Building '{FixationGear.StructureId}' at HU >= {HuThreshold:0.#}, excluding body...");
            Structure fixation = FixationGear.Create(set, img, body, HuThreshold, log);

            if (fixation != null)
            {
                log("");
                LogBounds(fixation, log);
                log("Done. Review the structure in Eclipse; tune the threshold and re-run as needed.");
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
