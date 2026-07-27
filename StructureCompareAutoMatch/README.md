# StructureCompareAutoMatch

The **automatic** sibling of `StructureCompareLink`. No manual linking: it reads **every structure
set on the open patient**, matches organs across them **by name**, keeps the organs that appear in
two or more sets, and saves the result as a **semicolon-delimited CSV**. Organs with no counterpart
in another set are ignored.

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

## CSV format

Semicolon-delimited, UTF-8 with BOM (opens directly in Excel where `;` is the list separator):

```
Group;MatchKey;StructureSetId;StructureId
1;parotid|L;CT_AI;Parotid_L
1;parotid|L;CT_Manual;parotid_sin
2;v:t11;CT_AI;Th11
2;v:t11;CT_Manual;T11
```

- **Group** — rows sharing a group number are the same organ matched across sets.
- **MatchKey** — the canonical key the matcher derived (`organ`, `organ|L`/`|R`, or `v:<label>`).
- **StructureSetId** — the set (method) the structure came from.
- **StructureId** — the ROI name as it appears in that set.

Matching is symmetric (no ground-truth column). If you need a ground-truth vs compare split for the
downstream metrics, use `StructureCompareLink`, or say the word and it can be added here too.

## How to run

1. Build in Visual Studio (net48, x64). Output is `StructureCompareAutoMatch.esapi.dll`. **No NuGet
   dependencies** — only WPF and the shared ESAPI assemblies in `..\ESAPI\`.
2. Point the Debug `OutputPath` in the `.csproj` at your Eclipse published-scripts folder, or copy
   the DLL there.
3. Run it from Eclipse with a **patient open**. It scans all structure sets, matches automatically,
   and shows a summary window with a **Save CSV…** button. The script is **read-only**.

## Layout

- `Script.cs` — entry point (`VMS.TPS.Script`): gathers, matches, shows the summary.
- `PatientStructures.cs` — snapshots every structure set on the patient into ESAPI-free DTOs.
- `OrganNameMatcher.cs` — the name-normalization core (the matching rules above).
- `AutoMatcher.cs` — groups structures by match key, keeps 2+-set groups, writes the CSV.
- `ResultsWindow.cs` — the code-only WPF summary window + Save dialog.
