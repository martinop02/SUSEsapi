# CouchFixationTest

An isolated ESAPI harness for the **couch-placement problem with fixation gear**.

When fixation gear rolls the patient slightly, the body's posterior (back) surface is no longer
parallel to the treatment couch. `AddCouchStructures` drops an *axis-aligned* couch, so it ends up
tilted relative to the patient. This project exists to reproduce and measure that mismatch in
isolation — separate from the full `PalliativeAutoPlan` workflow — so a correction can be developed
and verified quickly.

## What it does now

Runs entirely on its own **scratch structure set** (`FixationTest`), so clinical data is never
touched. A single global HU threshold can't cleanly isolate fixation (devices span a huge HU range
and overlap the couch), so it uses a **two-pass, couch-assisted** approach:

1. **Coarse fixation** — `HU ≥ −550` minus body. Rough and patchy, but that's fine.
2. **Merge into body** — save the original body as `body_orig`, then OR the coarse fixation into the
   `EXTERNAL` body so it bulges to include the fixation bulk.
3. **Add the couch** — with the body now including the fixation, `AddCouchStructures` places the
   couch at the correct height.
4. **Refined fixation** — a much lower threshold (`HU ≥ −900`, catches foam), keeping only voxels
   that are **below (posterior to) the original body** on each axial slice and **outside the couch**.
   The result, `fixation_gear`, is the clean base fixation between patient and couch.

Structures left in the set: `body_orig` (saved original), `fixation_coarse` (pass 1), the couch
supports, the augmented body, and `fixation_gear` (the deliverable).

Per-pass mechanics (`FixationGear.Segment`, all per axial slice): threshold → erase a list of
structures' interiors → morphological **close** (`CloseRadiusPx`) → optional **keep-below-reference**
constraint → store into a 3D volume → **3D connected-component** size filter (`MinComponentVolumeCc`)
→ write contours (OpenCV, same technique as `PalliativeAutoPlan/Segmenter.cs`). The size filter is
3D on purpose: fixation is thin per slice but large in 3D, so a 2D per-slice filter would delete it.

Tunables — thresholds and couch model in `Script.cs` (`CoarseHuThreshold`, `RefinedHuThreshold`,
`CouchModel`); cleanup in `FixationGear.cs` (`CloseRadiusPx`, `MinComponentVolumeCc`). "Below" is
larger pixel-row = posterior (head-first-supine); flip it in `KeepBelowReference` if needed.

## How to run

1. **NuGet restore** first (OpenCvSharp is required), then build in Visual Studio (net48, x64).
   Output is `CouchFixationTest.esapi.dll`.
2. Point the Debug `OutputPath` in the `.csproj` at your Eclipse published-scripts folder, or copy
   the DLL there.
3. Run it from Eclipse with **an image open** (a plan or structure set on that image is fine too —
   only the image is required; it makes its own `FixationTest` set and body). A window logs the
   thresholded vs. body-excluded volumes and bounds. Re-running reuses the `FixationTest` set and
   replaces `fixation_gear`, so cleanup is just deleting that scratch set.

The context must be **writable** (not approved/locked), since it creates a structure set and
structures.

## Layout

- `Script.cs` — entry point (`VMS.TPS.Script`): the 4-step workflow, couch/body helpers, thresholds.
- `FixationGear.cs` — `Segment(...)`: HU threshold + erase-structures + optional below-reference +
  3D cleanup; used for both the coarse and refined passes.
- `LogWindow.xaml` / `LogWindow.xaml.cs` — WPF log window (same proven pattern as
  PalliativeAutoPlan's; shows first, runs the work in `OnContentRendered`, pumps repaints).
- ESAPI assemblies are shared from the repo-root `..\ESAPI\` folder; OpenCvSharp comes via NuGet
  (`packages.config`).
