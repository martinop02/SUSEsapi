using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;

namespace StructureCompareAutoMatch
{
    /// <summary>Plain, ESAPI-free snapshot of one ROI.</summary>
    public sealed class StructureInfo
    {
        public string Id { get; set; }
        public string DicomType { get; set; }
        public bool IsEmpty { get; set; }
    }

    /// <summary>One structure set on the patient — i.e. one contouring "method".</summary>
    public sealed class StructureSetInfo
    {
        public string Id { get; set; }
        public string ImageId { get; set; }
        public string SeriesId { get; set; }
        public List<StructureInfo> Structures { get; set; } = new List<StructureInfo>();
    }

    /// <summary>
    /// Snapshots every structure set on the patient into ESAPI-free DTOs. ESAPI model objects have
    /// thread affinity, so all reads happen here on the main thread and the matcher/window then work
    /// purely against the snapshot.
    /// </summary>
    public static class PatientStructures
    {
        public static List<StructureSetInfo> Gather(Patient patient, Action<string> log = null)
        {
            var sets = new List<StructureSetInfo>();

            foreach (StructureSet set in patient.StructureSets
                         .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
            {
                var info = new StructureSetInfo
                {
                    Id = set.Id,
                    ImageId = set.Image?.Id,
                    SeriesId = set.Image?.Series?.Id,
                };

                foreach (Structure s in set.Structures
                             .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
                {
                    info.Structures.Add(new StructureInfo
                    {
                        Id = s.Id,
                        DicomType = s.DicomType,
                        IsEmpty = s.IsEmpty,
                    });
                }

                sets.Add(info);
                log?.Invoke($"'{info.Id}' (image '{info.ImageId}'): {info.Structures.Count} structure(s).");
            }

            log?.Invoke($"Found {sets.Count} structure set(s) on the patient.");
            return sets;
        }
    }
}
