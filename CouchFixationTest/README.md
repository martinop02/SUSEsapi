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

Fixation-gear mechanics (`FixationGear.cs`):

- **Threshold** — per axial slice, mask voxels at/above the HU threshold and convert the mask to
  contours with OpenCV (the same technique `PalliativeAutoPlan/Segmenter.cs` uses).
- **Exclude body** — subtract the body volume with ESAPI's boolean op (`SegmentVolume.Sub`).

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
