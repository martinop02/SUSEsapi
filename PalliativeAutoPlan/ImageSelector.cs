using System;
using System.Linq;
using VMS.TPS.Common.Model.API;

namespace PalliativeAutoPlan
{
    /// <summary>Picks the image the vertebrae are segmented on.</summary>
    public static class ImageSelector
    {
        // Image-selection mode:
        //   true  = use the image currently open in the Eclipse context. Convenient for TESTING —
        //           just open the image you want and run.
        //   false = automatically pick the newest kVCBCT image (see GetNewestKvCbct). This is what
        //           the ACTUAL APPLICATION needs. Remember to set this to false for production.
        private const bool UseOpenContextImage = true;

        // Automatic mode: only images whose Id (the name shown in Eclipse) contains this substring
        // are eligible — i.e. the kV cone-beam CT, not the planning CT or an MV image.
        // Case-insensitive; if the "kVCBCT" tag lives on the series instead of the image Id, match
        // img.Series?.Id in GetNewestKvCbct too.
        private const string RequiredNameSubstring = "kVCBCT";

        /// <summary>
        /// Picks the image to segment on according to <see cref="UseOpenContextImage"/>: the open
        /// context image (manual/testing) or the newest kVCBCT image (automatic/production). Logs
        /// which mode ran and returns null if no image is available.
        /// </summary>
        public static Image SelectImage(ScriptContext context, Action<string> log)
        {
            if (UseOpenContextImage)
            {
                Image open = context.Image;
                if (open == null)
                    log?.Invoke("Image selection (context mode): no image is open in the context.");
                else
                    log?.Invoke($"Image selection (context mode): using open context image '{open.Id}'.");
                return open;
            }

            Image auto = GetNewestKvCbct(context.Patient);
            if (auto == null)
                log?.Invoke("Image selection (automatic mode): no 3D image with 'kVCBCT' in its name was found.");
            else
                log?.Invoke($"Image selection (automatic mode): newest kVCBCT image '{auto.Id}'.");
            return auto;
        }

        /// <summary>
        /// Automatic mode: the most recent 3D image whose Id contains "kVCBCT" (case-insensitive),
        /// across every study of the patient — i.e. the latest kV cone-beam CT. Null if none match.
        ///
        /// Ordering by the (nullable) CreationDateTime works whether the property is DateTime or
        /// DateTime?: nulls sort last under OrderByDescending, so a dated image is preferred when
        /// one exists.
        /// </summary>
        public static Image GetNewestKvCbct(Patient patient)
        {
            return patient.Studies
                          .SelectMany(study => study.Images3D)
                          .Where(img => (img.Id ?? string.Empty)
                              .IndexOf(RequiredNameSubstring, StringComparison.OrdinalIgnoreCase) >= 0)
                          .OrderByDescending(img => img.CreationDateTime)
                          .FirstOrDefault();
        }
    }
}
