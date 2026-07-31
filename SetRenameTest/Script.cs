using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using VMS.TPS.Common.Model.API;

// Modifies the patient (renames a structure set), so the context must be writeable.
[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    /// <summary>
    /// Minimal test: renames the structure set in the current context to "SetRenameTest", to confirm
    /// that StructureSet.Id has a working setter. Run with a structure set open (Contouring), or a
    /// plan open (its StructureSet is used). Nothing is persisted until you save in Eclipse.
    /// </summary>
    public class Script
    {
        public Script() { }

        private const string NewId = "SetRenameTest";   // 13 chars — within the 16-char DICOM limit

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            Patient patient = context.Patient;
            if (patient == null)
            {
                MessageBox.Show("No patient is open.", "SetRenameTest",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StructureSet set = context.StructureSet ?? context.PlanSetup?.StructureSet;
            if (set == null)
            {
                MessageBox.Show("No structure set in context. Open a structure set (or a plan) and retry.",
                    "SetRenameTest", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string oldId = set.Id;

            try
            {
                patient.BeginModifications();       // required for a writeable edit
                set.Id = NewId;                     // the actual test — the StructureSet.Id setter

                MessageBox.Show(
                    $"Renamed structure set:\n\n'{oldId}'  ->  '{set.Id}'\n\n" +
                    "The change is in memory — save in Eclipse to persist it.",
                    "SetRenameTest", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // e.g. an approved/locked set, a duplicate id, or an id over 16 chars.
                MessageBox.Show(
                    $"Could not rename '{oldId}' to '{NewId}':\n\n{ex.Message}",
                    "SetRenameTest", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
