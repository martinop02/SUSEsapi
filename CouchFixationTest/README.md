# CouchFixationTest

An isolated ESAPI harness for the **couch-placement problem with fixation gear**.

When fixation gear rolls the patient slightly, the body's posterior (back) surface is no longer
parallel to the treatment couch. `AddCouchStructures` drops an *axis-aligned* couch, so it ends up
tilted relative to the patient. This project exists to reproduce and measure that mismatch in
isolation — separate from the full `PalliativeAutoPlan` workflow — so a correction can be developed
and verified quickly.

## What it does now

Runs entirely on its own **scratch structure set**, so clinical data is never touched:

1. **Structure set** — creates (or reuses) a set called `FixationTest` on the open image.
2. **Body** — adds it with the native ESAPI search (`CreateAndSearchBody`).
3. **Fixation gear** — builds a `fixation_gear` structure defined as:

   > every voxel with **HU ≥ −550** that is **not inside the body**.

Fixation-gear mechanics (`FixationGear.cs`), all per axial slice in the mask domain:

- **Threshold + erase body + close (per slice)** — mask voxels at/above the HU threshold, fill the
  body's own contours (from `GetContoursOnImagePlane`) with 0 to remove the interior, and
  morphologically **close** (`CloseRadiusPx`) small gaps so thin/low-HU fixation is less patchy.
  Each cleaned slice is stored into a full 3D volume.
- **3D component filter** — keep only connected components whose total volume is
  ≥ `MinComponentVolumeCc`; this drops noise specks and small couch fragments.
- **Write** — extract the remaining contours per slice with OpenCV (same technique as
  `PalliativeAutoPlan/Segmenter.cs`) and write them onto the structure.

The size filter is deliberately **3D, not per-slice**: the fixation is thin on any one axial slice
but large as a 3D object, so a per-slice area filter can't tell it apart from noise and deletes it
too (that produced an empty structure). Filtering by 3D component volume keeps the fixation while
removing genuinely small blobs. A plain global threshold is both too greedy (noise/couch are also
dense-and-outside-body) and too timid (thin/low-HU fixation dips below threshold → patchy), which
the close + 3D filter counteract. All parameters are tunable constants in `FixationGear.cs`.

Excluding the body in the mask domain (instead of thresholding everything and then
`SegmentVolume.Sub(body)`) matters for speed: at −550 HU the whole patient is above threshold, so
the naive approach writes thousands of body contours via the expensive `AddContourOnImagePlane` and
then discards them. Erasing the body first cuts the write count by 1-2 orders of magnitude.

What remains is everything denser than the threshold that is outside the patient — fixation
devices/masks and, if imaged, the couch/table. This is a deliberately simple first definition; the
threshold (`HuThreshold` in `Script.cs`, default −550) and noise filtering are meant to be tuned.

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

- `Script.cs` — entry point (`VMS.TPS.Script`): scratch-set + body setup, threshold constant,
  orchestration + logging.
- `FixationGear.cs` — the HU-threshold + body-exclusion structure builder.
- `LogWindow.cs` — minimal code-only WPF log window.
- ESAPI assemblies are shared from the repo-root `..\ESAPI\` folder; OpenCvSharp comes via NuGet
  (`packages.config`).
