using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using VMS.TPS.Common.Model.API;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Couch-assisted fixation-gear handling, split around the couch add so PalliativeAutoPlan can
    /// keep its existing EnsureCouch call in place:
    ///
    ///   PreCouch:  clean the body (keep largest component), segment a rough fixation, save the
    ///              original body, then OR the rough fixation into the body so the couch places
    ///              correctly even though the patient is tilted by the gear.
    ///   PostCouch: segment a clean fixation (lower threshold, below the original body, outside the
    ///              couch), rebuild the body as original-body OR clean-fixation, and delete the rough
    ///              fixation + the temporary saved body.
    ///
    /// Only runs when the user chose "with fixation" (<see cref="RunConfig.Fixation"/>). The final
    /// 'fixation_gear' structure is kept; 'fixation_coarse' (rough) is deleted.
    /// </summary>
    public static class Fixation
    {
        // Thresholds / parameters (same as the CouchFixationTest tool).
        private const double CoarseHuThreshold = -550.0;
        private const double RefinedHuThreshold = -750.0;
        private const double CoarseMinComponentCc = 0.2;
        private const int ZMarginSlices = 10;

        private const string CoarseId = "fixation_coarse";  // rough pass — deleted after the run
        public const string FinalId = "fixation_gear";       // clean pass — kept
        private const string BodyOrigId = "body_orig_fix";   // temporary saved body — deleted after the run

        /// <summary>Carried from PreCouch to PostCouch.</summary>
        public sealed class State
        {
            public FixationGear.Buffers Buf;
            public Structure Body;
            public Structure BodyOrig;
            public int KMin, KMax;
        }

        /// <summary>
        /// Pre-couch step. Returns the state to hand to <see cref="PostCouch"/>, or null if it could
        /// not run (no body / no image), in which case PostCouch is a no-op.
        /// </summary>
        public static State PreCouch(StructureSet set, Structure body, Action<string> log)
        {
            if (body == null) { log("  Fixation: no body found; skipping fixation handling."); return null; }
            if (set.Image == null) { log("  Fixation: no image; skipping fixation handling."); return null; }

            var st = new State { Body = body, Buf = new FixationGear.Buffers(set.Image) };

            log("  Cleaning body (keep largest connected component)...");
            FixationGear.KeepLargestComponent(body, set.Image, st.Buf, out st.KMin, out st.KMax, log);

            log($"  Coarse fixation (HU >= {CoarseHuThreshold:0.#}, minus body)...");
            Structure coarse = FixationGear.Segment(
                set, set.Image, CoarseId, Colors.Gray,
                CoarseHuThreshold, new[] { body }, null,
                CoarseMinComponentCc, false, st.Buf,
                st.KMin - ZMarginSlices, st.KMax + ZMarginSlices, log);

            st.BodyOrig = Copy(set, body, BodyOrigId, Colors.DimGray, log);
            if (coarse != null)
            {
                OrInto(body, coarse, log);   // body now includes the fixation bulk -> couch places right
                log($"  Body volume after merging coarse fixation: {SafeVolume(body):0.0} cc.");
            }
            return st;
        }

        /// <summary>
        /// Post-couch step. Segments the clean fixation, rebuilds the body to include it, and deletes
        /// the rough fixation and the temporary saved body.
        /// </summary>
        public static void PostCouch(StructureSet set, State st, Action<string> log)
        {
            if (st == null || st.BodyOrig == null) return;

            var couch = set.Structures.Where(s => s.DicomType == "SUPPORT" && !s.IsEmpty).ToList();
            var erase = new List<Structure> { st.BodyOrig };
            erase.AddRange(couch);

            var buf = st.Buf.Matches(set.Image) ? st.Buf : new FixationGear.Buffers(set.Image);

            log($"  Refined fixation (HU >= {RefinedHuThreshold:0.#}, below original body, minus couch)...");
            Structure fixation = FixationGear.Segment(
                set, set.Image, FinalId, Color.FromRgb(0, 220, 220),
                RefinedHuThreshold, erase, st.BodyOrig,
                0.0, true, buf, 0, int.MaxValue, log);

            // Final body = clean patient body OR the clean fixation (drops the rough coarse bulge).
            if (fixation != null)
            {
                try
                {
                    st.Body.SegmentVolume = st.BodyOrig.SegmentVolume;   // reset to the clean patient
                    OrInto(st.Body, fixation, log);
                    log($"  Body volume after merging clean fixation: {SafeVolume(st.Body):0.0} cc.");
                }
                catch (Exception ex)
                {
                    log("  WARNING: could not rebuild body with fixation: " + ex.Message);
                }
            }

            // Clean up: the rough fixation (as requested) and the temporary saved body.
            Remove(set, CoarseId, log);
            Remove(set, BodyOrigId, log);
        }

        // --- helpers ---

        private static Structure Copy(StructureSet set, Structure src, string id, Color color, Action<string> log)
        {
            Structure existing = set.Structures.FirstOrDefault(s => s.Id == id);
            if (existing != null && set.CanRemoveStructure(existing)) set.RemoveStructure(existing);
            Structure copy = set.AddStructure("CONTROL", id);
            copy.SegmentVolume = src.SegmentVolume;
            copy.Color = color;
            return copy;
        }

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

        private static void Remove(StructureSet set, string id, Action<string> log)
        {
            Structure s = set.Structures.FirstOrDefault(x => x.Id == id);
            if (s == null) return;
            if (set.CanRemoveStructure(s)) { set.RemoveStructure(s); log($"  Removed '{id}'."); }
            else log($"  Could not remove '{id}' (approved/locked?).");
        }

        private static double SafeVolume(Structure s)
        {
            try { return s.Volume; } catch { return double.NaN; }
        }
    }
}
