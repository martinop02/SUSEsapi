namespace PalliativeAutoPlan
{
    /// <summary>
    /// Run-level choices made at startup by <see cref="TechniqueSelector"/> and read by the beam /
    /// optimization code. Holds the treatment machine and the "fast optimization" toggle.
    /// </summary>
    public static class RunConfig
    {
        // --- Treatment unit (ExternalBeamMachineParameters machineId) ---
        public const string SBH_2 = "SBH_2";
        public const string SBH3_2021 = "SBH3_2021";

        /// <summary>Chosen beam-data machine id. Default SBH_2 (site-verified); dialog can switch to SBH3_2021.</summary>
        public static string MachineId = SBH_2;

        /// <summary>
        /// Fast VMAT optimization: lower Aperture Shape Controller (Moderate instead of Very High)
        /// and a short OptimizeVMAT (few cycles, no intermediate dose). Trades some plan quality for
        /// speed — reasonable for palliative. Off = the higher-quality defaults.
        /// </summary>
        public static bool Fast = false;

        /// <summary>
        /// Patient has fixation gear. When set, the couch is placed with the fixation accounted for
        /// and the fixation is segmented into 'fixation_gear' (merged into the body). See
        /// <see cref="Fixation"/>. Off = the normal couch placement, no fixation handling.
        /// </summary>
        public static bool Fixation = false;
    }
}
