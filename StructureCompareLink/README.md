# StructureCompareLink

An ESAPI (Eclipse) script that provides the **interactive front-end** for the
[StructureCompare](https://github.com/HUSRadFys/StructureCompare) pipeline, run directly inside
Eclipse instead of on exported DICOM files.

For the **series of the open image** it lists every structure set drawn on any image of that
series — each set being one contouring *method* (AI, manual, other) — and lets you declare which
structures are "the same" across methods by **linking** them. Structures with matching names are
linked automatically; you draw the rest by hand. The result is exported as a **semicolon-delimited
CSV** correspondence table, which the comparison metrics are then computed over.

This replaces the hard-coded name-mapping table in `StructureCompare/analysis/patient.py` (the
`mapping` dict) with an interactive step that works on the live Eclipse database.

## What it does

1. **Find the series** — resolves the open image (or the image behind an open plan/structure set),
   takes its series, and collects every `StructureSet` whose image belongs to that series
   (matched by series UID).
2. **Show the sets side by side** — one column per structure set, each listing its structures
   (empty structures are dimmed; hover for DICOM type and volume).
3. **Auto-link by name** — on open, structures whose names match (case-insensitive, trimmed) are
   linked across sets. Links that span three or more sets are chained into a single group.
4. **Manual linking** — **drag** from a structure in one column to the matching structure in
   another to link names that differ slightly (e.g. `Parotid_L` ↔ `parotid_lt`). Blue lines are
   auto-links, orange lines are manual.
5. **Edit links** — click a line to select it (turns red); press **Delete** or **Remove selected
   link** to remove it. **Right-click** a line removes it immediately. **Clear all links** starts
   over; **Auto-link by name** re-adds any missing same-name links without touching manual ones.
6. **Save CSV…** — writes the correspondence table to a location you choose.

## CSV format

Semicolon-delimited, UTF-8 with BOM (opens directly in Excel where `;` is the list separator):

```
Group;StructureSetId;StructureId
1;CT_AI;Parotid_L
1;CT_Manual;parotid_lt
2;CT_AI;SpinalCord
2;CT_Manual;spinalcord
```

- **Group** — rows that share a group number are the same anatomical structure by different
  methods. A comparison then evaluates every member of a group against the others (or against a
  chosen reference/ground-truth member).
- **StructureSetId** — identifies the method (the structure set the structure came from).
- **StructureId** — the ROI name as it appears in that set.

By default only linked structures (groups of two or more) are exported. Tick **Include unlinked
structures in CSV** to also emit every unlinked structure as its own single-member group, giving a
complete record of everything in the series.

## How to run

1. Build in Visual Studio (net48, x64). Output is `StructureCompareLink.esapi.dll`. There are **no
   NuGet dependencies** — only WPF and the shared ESAPI assemblies in `..\ESAPI\`.
2. Point the Debug `OutputPath` in the `.csproj` at your Eclipse published-scripts folder, or copy
   the DLL there.
3. Run it from Eclipse with **an image open** (a plan or structure set on that image works too).
   The script is **read-only** — it never modifies the patient, it only reads structures and writes
   the CSV you save.

## Layout

- `Script.cs` — entry point (`VMS.TPS.Script`): gathers the series snapshot, then opens the window.
- `SeriesData.cs` — resolves the series and snapshots its structure sets into ESAPI-free DTOs.
- `LinkGraph.cs` — the link model: edges, same-name auto-linking, connected-component grouping
  (union-find) and CSV export.
- `LinkWindow.cs` — the code-only WPF window (columns, drag-to-link, edge editing, Save dialog).
- ESAPI assemblies are shared from the repo-root `..\ESAPI\` folder.
