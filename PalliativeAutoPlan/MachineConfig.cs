namespace PalliativeAutoPlan
{
    /// <summary>
    /// Treatment unit selected for this run. Set once at startup by <see cref="TechniqueSelector"/>
    /// and read by <see cref="BeamBuilder"/> / <see cref="StaticFieldBuilder"/> when building the
    /// <c>ExternalBeamMachineParameters</c>. Default is SBH_2 (the site-verified beam-data machine
    /// id); the startup dialog can switch it to SBH3_2021.
    /// </summary>
    public static class MachineConfig
    {
        public const string SBH_2 = "SBH_2";
        public const string SBH3_2021 = "SBH3_2021";

        /// <summary>The chosen beam-data machine id (ExternalBeamMachineParameters machineId).</summary>
        public static string MachineId = SBH_2;
    }
}
