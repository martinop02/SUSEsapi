# CouchFixationTest

An isolated ESAPI harness for the **couch-placement problem with fixation gear**.

When fixation gear rolls the patient slightly, the body's posterior (back) surface is no longer
parallel to the treatment couch. `AddCouchStructures` drops an *axis-aligned* couch, so it ends up
tilted relative to the patient. This project exists to reproduce and measure that mismatch in
isolation — separate from the full `PalliativeAutoPlan` workflow — so a correction can be developed
and verified quickly.

## What it does now

Read-only. It does **not** modify the plan. On the open plan / structure set it reports:

- Patient / image / plan orientation and the structures involved (with bounding boxes).
- The **body roll angle**, fit from the posterior surface across the left-right axis.
- The **couch top roll angle** (if a SUPPORT structure is present), fit the same way.
- The **mismatch** = body roll − couch roll — the tilt to correct.

## How to run

1. Build in Visual Studio (net48, x64). Output is `CouchFixationTest.esapi.dll`.
2. Point the Debug `OutputPath` in the `.csproj` at your Eclipse published-scripts folder, or copy
   the DLL there.
3. Run it from Eclipse with a patient plan open. A window shows the measured angles.

## Layout

- `Script.cs` — entry point (`VMS.TPS.Script`) + geometry measurement.
- `LogWindow.cs` — minimal code-only WPF log window.
- ESAPI assemblies are shared from the repo-root `..\ESAPI\` folder.

The correction logic is not written yet; see the clearly marked `TODO` block in `Script.cs`.
