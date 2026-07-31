# StructureComparePick

A **manual two-set** variant of `StructureCompareAutoMatch`. Instead of deciding ground truth vs AI
from the set names, it shows a **simple dialog listing the patient's structure sets** and lets you
**pick two** — a ground-truth (reference) set and a set to compare against it. Organs are matched by
name and the geometry metrics are computed and saved as a semicolon-delimited CSV.

## What it does

1. Lists every structure set on the patient in two drop-downs (**Ground truth** / **Compare against**).
2. Matches organs between the two chosen sets by name (`OrganNameMatcher` — robust to PascalCase vs
   underscores, left/right naming, vertebra labels, case-insensitive). Organs present in only one of
   the two sets are ignored.
3. For each matched organ, computes **DICE, Jaccard, Hausdorff, HD95, ASSD, volumes, centre-of-mass
   difference** (same engine as `StructureCompareAutoMatch`, validated against `medpy.metric.binary`).
4. Shows a summary and saves a **semicolon-delimited CSV** at the location you choose.

The two sets only need to share a **frame of reference** (they can be on different image instances);
both are rasterized onto the ground truth's image grid via `Structure.IsPointInsideSegment`.

## CSV format

```
MatchKey;GroundTruthSet;GroundTruthStructure;CompareSet;CompareStructure;DICE;Jaccard;Hausdorff_mm;HD95_mm;ASSD_mm;Volume_GT_cc;Volume_Cmp_cc;VolumeDiff_cc;COMdiff_mm;Note
parotid|L;Manual;Parotid_L;TotalSegAuto;parotid_lt;0.87;0.77;5.83;3.16;1.02;12.4;13.1;0.7;1.05;
```

`VolumeDiff_cc` is compare − ground truth. `Note` explains any pair that was skipped (different
frame of reference, empty structure, or bounding box over 25 M voxels).

## How to run

1. Build in Visual Studio (net48, x64) → `StructureComparePick.esapi.dll`. **No NuGet dependencies.**
2. Point the Debug `OutputPath` at your Eclipse published-scripts folder (already set to the shared
   scripts share), or copy the DLL there.
3. Run with a **patient open**. Pick the two sets, click **Compare**; after the metrics compute
   (Eclipse shows a busy cursor for a moment), use **Save CSV…**. The script is **read-only**.

## Layout

- `Script.cs` — entry point: gather sets, show the picker, compute, show results.
- `PickWindow.cs` — the simple two-drop-down picker.
- `PairCompare.cs` — matches organs between the two sets, computes metrics, writes the CSV.
- `ResultsWindow.cs` — summary window + Save dialog.
- `PatientStructures.cs`, `OrganNameMatcher.cs`, `Rasterizer.cs`, `VoxelMask.cs`, `KdTree3D.cs`,
  `Metrics.cs` — the shared gather + matching + metric engine (same as `StructureCompareAutoMatch`).
