using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;

namespace StructureComparePick
{
    /// <summary>Plain, ESAPI-free snapshot of one ROI (used for name matching).</summary>
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

        /// <summary>Label shown in the picker combo boxes.</summary>
        public string Display
        {
            get { return $"{Id}   ({Structures.Count} structures, image {ImageId})"; }
        }
    }

    /// <summary>Live ESAPI handles for one structure, kept for rasterization / metric computation.</summary>
    public sealed class StructureHandle
    {
        public Structure Structure { get; set; }
        public Image Image { get; set; }
        public string ImageUid { get; set; }
        public string ForUid { get; set; }
    }

    /// <summary>DTO snapshot plus a handle lookup keyed by (set id, structure id).</summary>
    public sealed class GatherResult
    {
        public List<StructureSetInfo> Sets { get; } = new List<StructureSetInfo>();
        public Dictionary<string, StructureHandle> Handles { get; } =
            new Dictionary<string, StructureHandle>(StringComparer.Ordinal);

        public static string Key(string setId, string structureId)
        {
            return (setId ?? "") + "" + (structureId ?? "");
        }

        public StructureHandle Handle(string setId, string structureId)
        {
            return Handles.TryGetValue(Key(setId, structureId), out StructureHandle h) ? h : null;
        }
    }

    /// <summary>
    /// Snapshots every structure set on the patient into ESAPI-free DTOs (for name matching) and,
    /// alongside, a handle lookup so the metric stage can reach the live ESAPI structures. All ESAPI
    /// reads happen here on the main thread.
    /// </summary>
    public static class PatientStructures
    {
        public static GatherResult Gather(Patient patient, Action<string> log = null)
        {
            var result = new GatherResult();

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
                    result.Handles[GatherResult.Key(set.Id, s.Id)] = new StructureHandle
                    {
                        Structure = s,
                        Image = set.Image,
                        ImageUid = set.Image?.UID,
                        ForUid = set.Image?.FOR,
                    };
                }

                result.Sets.Add(info);
                log?.Invoke($"'{info.Id}' (image '{info.ImageId}'): {info.Structures.Count} structure(s).");
            }

            log?.Invoke($"Found {result.Sets.Count} structure set(s) on the patient.");
            return result;
        }
    }
}
