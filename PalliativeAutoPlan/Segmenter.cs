using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using Point = OpenCvSharp.Point;
using Image = VMS.TPS.Common.Model.API.Image;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Runs TotalSegmentator on a given image and writes the resulting masks back into a
    /// structure set as contoured structures. Handles the lobe→lung merge for OARs
    /// (lung_left / lung_right) the same way VirvelSegmentator does; individual vertebrae and
    /// the other OARs are produced directly.
    /// </summary>
    public static class Segmenter
    {
        // Environment-specific paths (same shared folder the disk-based server watches).
        private const string TempFolder = @"M:\Personlige mapper\Martin\temp\";
        private const string PythonExe = "python";
        private const string TotalSegScript =
            @"C:\Python\Programs\Python\Python310\Lib\site-packages\totalsegmentator\bin\TotalSegmentator.py";
        private const string CombineMasksScript =
            @"C:\Python\Programs\Python\Python310\Lib\site-packages\totalsegmentator\bin\totalseg_combine_masks.py";

        /// <summary>
        /// Segments every id in <paramref name="finalIds"/> (e.g. "vertebrae_L1", "heart",
        /// "lung_left") on <paramref name="image"/> and adds them to <paramref name="set"/>.
        /// Mergeable OARs are combined from their lobes after segmentation. Returns false if
        /// the server is unreachable.
        /// </summary>
        public static bool Segment(Image image, List<string> finalIds, StructureSet set, Action<string> log)
        {
            if (finalIds == null || finalIds.Count == 0)
            {
                log?.Invoke("No structures requested; nothing to segment.");
                return false;
            }

            log?.Invoke("Checking connection to segmentation server...");
            if (!Command.IsServerRunning(TempFolder))
            {
                log?.Invoke("ERROR: Could not reach the segmentation server. Make sure serverDisk.py is running.");
                return false;
            }
            log?.Invoke("Server is running. Connection OK.");

            // 1) Export the image as NIfTI for TotalSegmentator.
            log?.Invoke("Exporting image to NIfTI...");
            Nifti nifti = new Nifti(image);
            NiftiWriter.SaveNifti(TempFolder + "temp.nii", nifti);
            log?.Invoke("NIfTI file saved.");

            // 2) Run TotalSegmentator restricted to the expanded ROI subset (lungs -> lobes).
            List<string> roiSubset = Anatomy.ExpandRoiSubset(finalIds);
            string cli = $@"{PythonExe} {TotalSegScript} -i ""{TempFolder}temp.nii"" -o ""{TempFolder}output"" --roi_subset {string.Join(" ", roiSubset)}";
            log?.Invoke("Running automatic segmentation... (this may take some minutes)");
            Command.SaveToDisk(cli, TempFolder);
            log?.Invoke("Segmentation done.");

            // 3) Combine lobes into whole-lung structures for any mergeable final id.
            foreach (string mergeId in finalIds.Where(id => Anatomy.MergeArgs.ContainsKey(id)).Distinct())
            {
                string mergeCli = $@"{PythonExe} {CombineMasksScript} -i ""{TempFolder}output"" -o ""{TempFolder}output\{mergeId}.nii.gz"" -m {mergeId}";
                log?.Invoke("  Combining " + mergeId + "...");
                Command.SaveToDisk(mergeCli, TempFolder);
            }

            // 4) Read the produced masks back and write them as contoured structures.
            var colors = ColorCodes.GetDictionary();
            var wanted = new HashSet<string>(finalIds);

            log?.Invoke("Transforming segment data to contours...");
            foreach (string path in Directory.GetFiles(TempFolder + "output", "*.nii.gz"))
            {
                string id = path.Split('\\').Last().Split('.').First();
                if (!wanted.Contains(id)) continue;   // skip lobes and anything not requested

                log?.Invoke("  Transforming structure: " + id + "...");
                NiftiData data = NiftiReader.LoadSegments(path);
                if (data.Empty)
                {
                    log?.Invoke("    (empty mask, skipped)");
                    continue;
                }

                Structure structure;

                if (!set.Structures.Any(s => s.Id == id))
                {
                    structure = set.AddStructure("ORGAN", id);
                } else
                {
                    structure = set.AddStructure("ORGAN", id + "_ny");
                }


                if (colors.TryGetValue(id, out var color)) structure.Color = color;

                WriteContours(structure, data.Data, image);
            }

            // 5) Clean up the shared temp folder.
            CleanUp(log);
            log?.Invoke("Segmentation complete.");
            return true;
        }

        // Converts a binary mask into per-slice contours and writes them onto the structure.
        private static void WriteContours(Structure structure, byte[,,] data, Image img)
        {
            int width = data.GetLength(0);
            int height = data.GetLength(1);

            for (int k = 0; k < img.ZSize; k++)
            {
                Mat slice = new Mat(height, width, MatType.CV_8UC1);

                unsafe
                {
                    byte* ptr = (byte*)slice.DataPointer;
                    for (int j = 0; j < height; j++)
                        for (int i = 0; i < width; i++)
                        {
                            int x = width - 1 - i;
                            int y = height - 1 - j;
                            ptr[y * slice.Step() + x] = data[i, j, k];
                        }
                }

                Point[][] contours = Cv2.FindContoursAsArray(
                    slice, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

                foreach (Point[] contour in contours)
                {
                    VVector[] vv = contour.Select(pt => ToDicom(img, pt.X, pt.Y, k)).ToArray();
                    structure.AddContourOnImagePlane(vv, k);
                }
            }
        }

        private static VVector ToDicom(Image img, int x, int y, int z)
        {
            return img.Origin
                 + x * img.XRes * img.XDirection
                 + y * img.YRes * img.YDirection
                 + z * img.ZRes * img.ZDirection;
        }

        private static void CleanUp(Action<string> log)
        {
            try
            {
                var outputDir = new DirectoryInfo(TempFolder + "output");
                if (outputDir.Exists)
                    foreach (FileInfo file in outputDir.GetFiles())
                        file.Delete();

                string tempNii = TempFolder + "temp.nii";
                if (File.Exists(tempNii)) File.Delete(tempNii);
            }
            catch (Exception e)
            {
                log?.Invoke("Cleanup warning: " + e.Message);
            }
        }
    }
}
