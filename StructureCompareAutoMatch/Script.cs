using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using StructureCompareAutoMatch;
using VMS.TPS.Common.Model.API;

// Read-only: reads structures and writes a CSV to disk — no patient modifications.
[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = false)]

namespace VMS.TPS
{
    /// <summary>
    /// StructureCompareAutoMatch — the automatic sibling of StructureCompareLink.
    ///
    /// It reads every structure set on the open patient, matches organs across them purely by name
    /// using <see cref="OrganNameMatcher"/> (robust to PascalCase vs underscores, left/right naming
    /// variants and vertebra labels, all case-insensitive), keeps organs found in two or more sets,
    /// and lets the user save the result as a semicolon-delimited CSV. Organs with no counterpart in
    /// another set are ignored.
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
                MessageBox.Show("No patient is open.", "StructureCompareAutoMatch",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<StructureSetInfo> sets = PatientStructures.Gather(patient);
            if (sets.Count < 2)
            {
                MessageBox.Show(
                    $"The patient has {sets.Count} structure set(s). At least two are needed to match " +
                    "organs between them.",
                    "StructureCompareAutoMatch", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<MatchGroup> groups = AutoMatcher.Match(sets);
            ResultsWindow.Show(patient.Id, sets, groups);
        }
    }
}
