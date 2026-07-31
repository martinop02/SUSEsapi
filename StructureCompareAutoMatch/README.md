# StructureCompareAutoMatch

The **automatic** sibling of `StructureCompareLink`. No manual linking: it reads **every structure
set on the open patient**, matches organs across them **by name**, and — for each matched
ground-truth vs AI pair — **computes the comparison metrics** (DICE, Jaccard, Hausdorff, HD95, ASSD,
volumes, centre-of-mass difference) directly in ESAPI, then saves everything as a
**semicolon-delimited CSV**. Organs with no counterpart in another set are ignored.

This is the full StructureCompare pipeline running inside Eclipse — no DICOM export, no Python.

## Matching rules

All matching is case-insensitive and separator-agnostic (see `OrganNameMatcher.cs`):

| Variation | Example matches |
|---|---|
| **Casing / separators** — PascalCase, camelCase, `_`, `-`, space, `.` all tokenize alike | `SpinalCord` = `spinal_cord` = `Spinal-Cord` |
| **Laterality** — a left/right token is stripped and recorded as a side, so a base organ matches only the same side | `Parotid_L` = `parotid_left` = `ParotidL` = `parotid_sin`; `Parotid_R` = `parotid_dxt` = `parotid_right`; but `Parotid_L` ≠ `Parotid_R` |
| **Vertebrae** — region+number label, `Th`→`T`, leading zeros dropped, filler words (`vertebra(e)`, `vert`, `corpus`) ignored | `Th11` = `T11`; `vertebrae_s1` = `S1`; `L1` reads as **lumbar L1**, not a left-side marker |

Laterality tokens recognised:

- **Left:** `l`, `lt`, `left`, `sin`, `sinister`, `sinistra`
- **Right:** `r`, `rt`, `right`, `dx`, `dxt`, `dexter`, `dextra`

Vertebra regions: `C`, `T`/`Th`, `L`, `S` + 1–2 digits.

### Deliberate limits (conservative to avoid false matches)

- Laterality glued on with **no separator and no case change** (e.g. `parotidl`) is **not** detected —
  there is no reliable way to tell it from an organ that simply ends in `l`/`r` (`adrenal`).
- A leading `D` (dorsal) is **not** treated as thoracic by default.

Both are one-line additions to the tables in `OrganNameMatcher.cs` if a site needs them.

## Ground truth vs AI (by name)

Roles are assigned automatically from the structure-set name:

- The set whose name does **not** contain `auto` (any case) is the **GroundTruth** (manual reference).
- Sets whose name **does** contain `auto`/`Auto` are the **AI**-segmented sets, compared against it.

There should be exactly one ground-truth set; if none or more than one lacks `auto`, the summary
shows a warning so you can rename before relying on the output.

## Metrics

For each matched organ, the ground-truth structure is compared against every AI structure in the
group. Both structures are rasterized onto the ground truth's image voxel grid using ESAPI's own
`Structure.IsPointInsideSegment` test (the reliable method used elsewhere in this repo), so it works
even when the AI set was drawn on a different image instance, as long as it shares the frame of
reference. The metric formulas match `medpy.metric.binary` (the library the Python `StructureCompare`
uses) — validated to ~1e-15 against a scipy Euclidean-distance-transform reference:

- **DICE**, **Jaccard** — voxel overlap.
- **Hausdorff_mm**, **HD95_mm**, **ASSD_mm** — surface distances between the structures' border
  voxels (nearest-neighbour via a k-d tree). `hd` = max of the two directed maxima; `hd95` = 95th
  percentile of both directed distance sets; `assd` = mean of the two directed means.
- **Volume_GT_cc**, **Volume_AI_cc**, **VolumeDiff_cc** (AI − GT), **COMdiff_mm** (centre-of-mass
  distance).

A pair is skipped (metrics blank, reason in the **Note** column) when the two structures are in
different frames of reference (no shared coordinate system), when one has no segment, or when the
shared bounding box would exceed 25 M voxels (guards against a body/couch match). Ground truth vs AI
is decided by name — see below.

## Ground truth vs AI (by name)

- The set whose name does **not** contain `auto` (any case) is the **GroundTruth** (manual reference).
- Sets whose name **does** contain `auto`/`Auto` are the **AI**-segmented sets, compared against it.

There should be exactly one ground-truth set; if none or more than one lacks `auto`, the summary
shows a warning so you can rename before relying on the output.

## CSV format

Semicolon-delimited, UTF-8 with BOM (opens directly in Excel where `;` is the list separator). One
row per ground-truth vs AI comparison:

```
Group;MatchKey;GroundTruthSet;GroundTruthStructure;AiSet;AiStructure;DICE;Jaccard;Hausdorff_mm;HD95_mm;ASSD_mm;Volume_GT_cc;Volume_AI_cc;VolumeDiff_cc;COMdiff_mm;Note
1;parotid|L;Manual;Parotid_L;AutoContour1;parotid_sin;0.87;0.77;5.83;3.16;1.02;12.4;13.1;0.7;1.05;
2;v:t11;Manual;Th11;AutoContour1;T11;0.91;0.83;4.12;2.24;0.81;9.8;9.5;-0.3;0.62;
```

- **Group / MatchKey** — the matched organ and its canonical key (`organ`, `organ|L`/`|R`, `v:<label>`).
- **GroundTruthSet / GroundTruthStructure** — the reference (non-`auto`) set and its ROI name.
- **AiSet / AiStructure** — the AI (`auto`) set and its ROI name.
- **Metric columns** — see above; blank when the pair was skipped.
- **Note** — why a pair was skipped, if it was.

## How to run

1. Build in Visual Studio (net48, x64). Output is `StructureCompareAutoMatch.esapi.dll`. **No NuGet
   dependencies** — only WPF and the shared ESAPI assemblies in `..\ESAPI\`.
2. Point the Debug `OutputPath` in the `.csproj` at your Eclipse published-scripts folder, or copy
   the DLL there.
3. Run it from Eclipse with a **patient open**. It matches and computes metrics automatically (this
   can take a little while for many organs — Eclipse shows a busy cursor), then shows a summary
   window with a **Save CSV…** button. The script is **read-only**.

## Layout

- `Script.cs` — entry point (`VMS.TPS.Script`): gathers, matches, computes metrics, shows the summary.
- `PatientStructures.cs` — snapshots every structure set into DTOs + live ESAPI handles.
- `OrganNameMatcher.cs` — the name-normalization core (the matching rules above).
- `AutoMatcher.cs` — groups structures by match key, keeps 2+-set groups, assigns GT/AI roles.
- `Rasterizer.cs` — fills structure contours to aligned binary voxel masks on the image grid.
- `VoxelMask.cs` — the binary mask (counts, centroid, border voxels).
- `KdTree3D.cs` — nearest-neighbour search for the surface-distance metrics.
- `Metrics.cs` — DICE / Jaccard / Hausdorff / HD95 / ASSD / volume / COM from two masks.
- `Comparison.cs` — builds GT-vs-AI rows, computes metrics, writes the CSV.
- `ResultsWindow.cs` — the code-only WPF summary window + Save dialog.
