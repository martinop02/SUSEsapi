using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using StructureComparePick;
using VMS.TPS.Common.Model.API;

// Read-only: reads structures and writes a CSV to disk — no patient modifications.
[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = false)]

namespace VMS.TPS
{
    /// <summary>
    /// StructureComparePick — pick any two structure sets and compare them.
    ///
    /// Lists the patient's structure sets in a simple dialog; the user picks a ground-truth
    /// (reference) set and a set to compare against it. Organs are matched by name
    /// (<see cref="OrganNameMatcher"/>) and the geometry metrics (DICE, Jaccard, Hausdorff, HD95,
    /// ASSD, volumes, centre-of-mass difference) are computed for each organ present in both sets,
    /// then saved as a semicolon-delimited CSV. Organs in only one set are ignored.
    /// </summary>
    public class Script
    {
        public Script() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            Patient patient = context.Patient;
            if (patient == null)
            {
                MessageBox.Show("No patient is open.", "StructureComparePick",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GatherResult gather = PatientStructures.Gather(patient);
            List<StructureSetInfo> sets = gather.Sets;
            if (sets.Count < 2)
            {
                MessageBox.Show(
                    $"The patient has {sets.Count} structure set(s). At least two are needed to compare.",
                    "StructureComparePick", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            PickResult pick = PickWindow.Show(sets);
            if (pick == null) return;   // cancelled

            // Metrics run on the ESAPI thread (rasterization needs the live structures); this can take
            // a little while for many organs — Eclipse shows a busy cursor meanwhile.
            List<PairRow> rows = PairCompare.Build(pick.GroundTruth, pick.Compare, gather);

            ResultsWindow.Show(patient.Id, pick.GroundTruth, pick.Compare, rows);
        }
    }
}
