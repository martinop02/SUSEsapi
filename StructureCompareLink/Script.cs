using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using StructureCompareLink;
using VMS.TPS.Common.Model.API;

// Read-only: the script only reads structures and writes a CSV to disk — it never modifies the
// patient, so the context does not need to be writeable.
[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = false)]

namespace VMS.TPS
{
    /// <summary>
    /// StructureCompareLink — the interactive front-end for the StructureCompare pipeline.
    ///
    /// For the series of the open image it collects every structure set drawn on any image of that
    /// series (each set being one contouring "method": AI, manual, other), shows them side by side,
    /// auto-links structures with matching names, and lets the user draw links between structures
    /// whose names differ slightly. The links are exported as a semicolon-delimited CSV in which
    /// rows that share a group number are the same anatomical structure by different methods — the
    /// correspondence table the comparison metrics are computed over.
    ///
    /// This class is a thin orchestrator: gather the data on the ESAPI thread, then hand a plain
    /// snapshot to <see cref="LinkWindow"/>.
    /// </summary>
    public class Script
    {
        public Script() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            var log = new List<string>();
            SeriesData data = SeriesData.Gather(context, line => log.Add(line));

            if (data == null)
            {
                MessageBox.Show(
                    "Could not start:\n\n" + string.Join("\n", log),
                    "StructureCompareLink", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (data.Sets.Count < 2)
            {
                MessageBox.Show(
                    $"The series '{data.SeriesId}' has {data.Sets.Count} structure set(s). " +
                    "At least two are needed to link structures between methods.\n\n" +
                    string.Join("\n", log),
                    "StructureCompareLink", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LinkWindow.Show(data);
        }
    }
}
