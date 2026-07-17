using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;

namespace StructureCompareLink
{
    /// <summary>
    /// Plain, ESAPI-free snapshot of one ROI in a structure set.
    ///
    /// ESAPI model objects have thread affinity and can only be touched on the script's main
    /// thread, so everything the linking window needs is copied into these DTOs up front (in
    /// <see cref="SeriesData.Gather"/>). The window then works purely against this snapshot and
    /// never calls back into ESAPI.
    /// </summary>
    public sealed class StructureInfo
    {
        public string Id { get; set; }
        public string DicomType { get; set; }
        public bool IsEmpty { get; set; }
        public double VolumeCc { get; set; }

        /// <summary>Lower-cased, trimmed <see cref="Id"/> used for same-name auto-linking.</summary>
        public string NormalizedName
        {
            get { return (Id ?? string.Empty).Trim().ToLowerInvariant(); }
        }
    }

    /// <summary>One structure set drawn on an image of the series — i.e. one "method".</summary>
    public sealed class StructureSetInfo
    {
        public string Id { get; set; }
        public string ImageId { get; set; }
        public string ImageUid { get; set; }
        public List<StructureInfo> Structures { get; set; } = new List<StructureInfo>();
    }

    /// <summary>
    /// The whole snapshot handed to the window: every structure set drawn on any image belonging
    /// to the chosen series, plus a bit of context for the header.
    /// </summary>
    public sealed class SeriesData
    {
        public string PatientId { get; set; }
        public string SeriesId { get; set; }
        public string SeriesUid { get; set; }
        public string StudyId { get; set; }
        public List<StructureSetInfo> Sets { get; set; } = new List<StructureSetInfo>();

        /// <summary>
        /// Resolves the image to work from (open image, or the image behind an open structure set /
        /// plan), finds its series, and snapshots every structure set drawn on any image in that
        /// series. Returns null (with a reason logged) when there is no usable image.
        /// </summary>
        public static SeriesData Gather(ScriptContext context, Action<string> log)
        {
            Patient patient = context.Patient;
            if (patient == null) { log?.Invoke("No patient is open."); return null; }

            Image image = context.Image
                       ?? context.StructureSet?.Image
                       ?? context.PlanSetup?.StructureSet?.Image;
            if (image == null)
            {
                log?.Invoke("No image is open. Open an image (or a plan/structure set on the series).");
                return null;
            }

            Series series = image.Series;
            if (series == null) { log?.Invoke("The open image has no series."); return null; }

            string seriesUid = series.UID;
            log?.Invoke($"Series '{series.Id}' (UID {seriesUid}) — collecting structure sets on all its images.");

            var data = new SeriesData
            {
                PatientId = patient.Id,
                SeriesId = series.Id,
                SeriesUid = seriesUid,
                StudyId = series.Study?.Id,
            };

            // A structure set "belongs to the series" when the image it is drawn on is one of the
            // series' images (matched by series UID — the stable identifier).
            foreach (StructureSet set in patient.StructureSets
                         .Where(s => s.Image != null
                                     && s.Image.Series != null
                                     && s.Image.Series.UID == seriesUid)
                         .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
            {
                var setInfo = new StructureSetInfo
                {
                    Id = set.Id,
                    ImageId = set.Image.Id,
                    ImageUid = set.Image.UID,
                };

                foreach (Structure s in set.Structures.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
                {
                    double vol;
                    try { vol = s.Volume; } catch { vol = double.NaN; }

                    setInfo.Structures.Add(new StructureInfo
                    {
                        Id = s.Id,
                        DicomType = s.DicomType,
                        IsEmpty = s.IsEmpty,
                        VolumeCc = vol,
                    });
                }

                data.Sets.Add(setInfo);
                log?.Invoke($"  '{setInfo.Id}' on image '{setInfo.ImageId}': {setInfo.Structures.Count} structure(s).");
            }

            log?.Invoke($"Found {data.Sets.Count} structure set(s) in the series.");
            return data;
        }
    }
}
