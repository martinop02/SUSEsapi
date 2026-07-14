using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// One matched prescription: the prescription itself, the course it lives in
    /// (where the plan must be created), the canonical compact name used for plan/
    /// structure ids, the ordered vertebra tokens, and the TotalSegmentator ids.
    /// </summary>
    public class PrescriptionMatch
    {
        public Course Course { get; set; }
        public RTPrescription Prescription { get; set; }
        public string Name { get; set; }              // e.g. "L1-L4"
        public List<string> Tokens { get; set; }      // e.g. L1, L2, L3, L4
        public List<string> SegmentIds { get; set; }  // e.g. vertebrae_L1 ... vertebrae_L4
    }

    /// <summary>
    /// Walks Patient -> Courses -> TreatmentPhases -> Prescriptions and returns the
    /// prescriptions whose name begins with a vertebra (range), e.g. "Th9" or "L1-L4".
    /// </summary>
    public static class PrescriptionScanner
    {
        public static List<PrescriptionMatch> FindVertebraPrescriptions(Patient patient, Action<string> log)
        {
            var matches = new List<PrescriptionMatch>();
            var seen = new HashSet<string>();  // dedupe a prescription that appears in several phases

            foreach (Course course in patient.Courses)
            {
                foreach (TreatmentPhase phase in course.TreatmentPhases)
                {
                    foreach (RTPrescription rx in phase.Prescriptions)
                    {
                        string key = course.Id + "|" + rx.Id;
                        if (!seen.Add(key)) continue;

                        string name;
                        List<string> tokens;
                        // The vertebra label may be in the prescription Id or Name; try both.
                        if (!VertebraOrder.TryParse(rx.Id, out name, out tokens) &&
                            !VertebraOrder.TryParse(rx.Name, out name, out tokens))
                            continue;

                        matches.Add(new PrescriptionMatch
                        {
                            Course = course,
                            Prescription = rx,
                            Name = name,
                            Tokens = tokens,
                            SegmentIds = tokens.Select(VertebraOrder.ToSegmentId).ToList()
                        });

                        log?.Invoke($"  Match: prescription '{rx.Id}' in course '{course.Id}' -> {name} ({string.Join(", ", tokens)})");
                    }
                }
            }

            return matches;
        }
    }
}
