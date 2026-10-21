> **About this fork**
>
> This repository is an **English-localized and extended fork** of the excellent
> [DWG2IFC](https://baidurovlabs.ru) plugin originally created by **baidurovlabs.ru**.
> The core engineering — the B-Rep tessellation pipeline, Plant 3D DataLinks extraction
> through reflection, the streaming ISO-10303-21 IFC writer, the entity-classification
> heuristics, the WPF settings dialog, and the heap-corruption fix in `acdbmgdbrep.dll` —
> is their work. Huge thanks to the upstream authors; none of this would exist
> without them.
>
> **What this fork changes relative to upstream:**
> - Full English translation of the user-facing UI (export dialog, installer, ribbon,
>   command-line banner, package manifest).
> - Fresh `ProductCode` / `UpgradeCode` GUIDs so Autodesk's Autoloader treats this fork
>   as a distinct product and does not conflict with an installed upstream build.
> - **Plant 3D pipe geometry** — a three-tier extractor that reads
>   `AcPpDb3dPipe` endpoints and outside diameter via reflection and emits them as
>   parametric `IfcSweptDiskSolid` (so pipes stay editable in Revit/Tekla, and files
>   are roughly 10× smaller than the equivalent mesh). Falls back to procedural mesh
>   or to `Entity.Explode` for anything the direct type switch misses.
> - **Diagnostics** — every export writes a plain-text `<name>.export.log` next to the
>   IFC listing per-category totals and the AutoCAD handle of each skipped object,
>   and a new `DWG2IFC_DIAG` command (also a ribbon button) scans the drawing without
>   writing an IFC and tells you what *would* export and what *would not*, and why.
> - User-friendly export-settings dialog: three new checkboxes (parametric pipes,
>   explode fallback, write log) with hover-over explanations in plain language.
> - Attribution notes added so users can tell which build they are running.
>
> **Fork maintainer:** Wahid Hassani — repository at
> [github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0](https://github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0).
> Please file issues or questions via the repository's
> [Issues tab](https://github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0/issues),
> or contact the maintainer through their
> [GitHub profile](https://github.com/OneAndOnlyWahidHassani) inbox.
> **Original author:** baidurovlabs.ru — all credit for the plugin's design and
> original implementation belongs to them.
> **License (both upstream and this fork):** [GNU GPLv3](LICENSE).

---

A plugin for AutoCAD and AutoCAD Plant 3D that exports 3D models and process piping
to clean **IFC** (IFC4 / IFC2x3) with honest B-Rep geometry, correct classification, and
semantic property sets (Pset) — without process crashes, parameter loss, or heavy
commercial toolchains.

Original project: [baidurovlabs.ru](https://baidurovlabs.ru) · English fork: [github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0](https://github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0) · licensed under [GNU GPLv3](LICENSE)

## Why
Base AutoCAD has no native direct export to IFC. In AutoCAD Plant 3D the built-in export
tools are either missing or reduced to heavy intermediate formats (NWCOUT / FBX / DXF / SAT)
that lose semantic structure: pipe diameters, specifications, line numbers, materials,
and valve types all collapse into mute geometric blobs.

Existing commercial plugins (AstraPlant, Codemill, MDC) are either tied to expensive closed
licenses or crash with out-of-memory errors on real industrial assemblies with complex
blocks and thousands of fittings.

**At the same time, all the required semantics are already present in the drawing:**
- In Plant 3D: the project database (`DataLinksManager`) stores the process line number,
  specification, nominal diameter (DN), steel grade, design pressure, and service medium.
- In AutoCAD: the topological B-Rep kernel (`acdbmgdbrep.dll`) contains strict boundary
  geometry of solids, and the block/layer structure defines the assembly hierarchy.

DWG2IFC reads topology and properties directly from the AutoCAD session's memory and
translates them into valid IFC.

## Download & installation

Pre-built binaries are published on the
[Releases page](https://github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0/releases/latest).
Every release contains three files:

| File | What it is | Who it's for |
|---|---|---|
| `DWG2IFC-Setup.exe` | Self-contained installer with the plugin bundle embedded | Most users |
| `DWG2IFC-<version>.zip` | Plain bundle folder, LICENSE, and README | Manual / air-gapped installs, IT departments |
| `SHA256SUMS.txt` | SHA-256 checksums of the two files above | Verifying the download hasn't been tampered with |

### Option 1 — Automated installer (recommended)

1. Close AutoCAD / Plant 3D if it is running.
2. Download **`DWG2IFC-Setup.exe`** from the Releases page.
3. (Optional but recommended) Verify the download — in PowerShell:
   ```powershell
   Get-FileHash .\DWG2IFC-Setup.exe -Algorithm SHA256
   ```
   The hash should match the line for `DWG2IFC-Setup.exe` inside `SHA256SUMS.txt`.
4. Run the installer. Windows SmartScreen may show an "unknown publisher" warning
   while code-signing is still being set up (see the **Code signing** section below) —
   click **More info → Run anyway**.
5. Click **Install plugin**. Administrator rights are **not** required — the bundle
   registers in the user-level `%APPDATA%\Autodesk\ApplicationPlugins\` auto-load
   folder. A desktop shortcut is created.
6. Reopen AutoCAD or Plant 3D 2025 / 2026. A new **DWG2IFC** tab appears on the ribbon.
   Click **Export to IFC**, or type `IFCEXPORT` at the AutoCAD command line.
7. To uninstall later, run `DWG2IFC-Setup.exe` again and click **Uninstall plugin**.

The installer also has a silent command-line mode for scripted deployments:

```powershell
DWG2IFC-Setup.exe --install      # silent install, exit code 0 on success
DWG2IFC-Setup.exe --uninstall    # remove the plugin and shortcuts
DWG2IFC-Setup.exe --status       # print whether the plugin is currently installed
```

### Option 2 — Manual bundle install (zip)

Useful on machines that block executable installers, when deploying through a
corporate software catalogue, or when you want to inspect the bundle contents
before installing.

1. Close AutoCAD / Plant 3D if it is running.
2. Download **`DWG2IFC-<version>.zip`** from the Releases page.
3. Extract the archive. You will get a folder named `DWG2IFC.bundle` (plus the
   `LICENSE` and `README.md` files, which are informational only).
4. Copy the entire `DWG2IFC.bundle` folder into one of these locations:
   - **User-level (recommended, no admin rights):**
     `%APPDATA%\Autodesk\ApplicationPlugins\`
   - **Machine-wide (requires admin):**
     `%ProgramData%\Autodesk\ApplicationPlugins\`
5. Open AutoCAD / Plant 3D. The **DWG2IFC** ribbon tab should appear automatically.
   If it does not, type `IFCRIBBON` at the command line to force-rebuild the tab,
   or type `APPAUTOLOADER` to see Autodesk's load diagnostics.
6. To uninstall manually, simply delete the `DWG2IFC.bundle` folder from the
   `ApplicationPlugins` directory you copied it into, and (optionally) the
   `%APPDATA%\Plant3D_IfcExporter\` settings folder.

### Option 3 — Build from source

```powershell
git clone https://github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0.git
cd DWG2IFC-1.0
powershell -ExecutionPolicy Bypass -File install.ps1
```

The script compiles the plugin against .NET 8.0 and registers the bundle in
AutoCAD. Restart AutoCAD: the ribbon will show the **DWG2IFC** tab.
Prerequisites: Windows 10/11 x64 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

### Commands registered in AutoCAD / Plant 3D

- `DWG2IFC` or `IFCEXPORT` — open the export dialog with schema, filter, and coordinate options.
  (Aliases: `PLANTTOIFC`, `IFCEXPORT_PLANT`.)
- `DWG2IFC_DIAG` — scan the current drawing and write a `<drawing>.diag.log` file next to the
  DWG listing every 3D object, which extraction tier handled it, and the reason if it was
  skipped. Writes no IFC. Also available as the **Diagnose drawing** button on the ribbon.
  Run this first if an export silently produces fewer objects than you expected.
- `IFCRIBBON` — force the ribbon tab to be restored or activated.

## Code signing

The `DWG2IFC-Setup.exe` installer is being code-signed as part of the
[SignPath Foundation](https://signpath.org/) programme for open-source projects.
Code signing proves the installer you downloaded was built from this exact
repository by its continuous-integration pipeline and has not been tampered
with on the way to your machine.

> **Free code signing on Windows provided by [SignPath.io](https://signpath.io/?utm_source=foundation&utm_medium=github&utm_campaign=DWG2IFC-1.0), certificate by [SignPath Foundation](https://signpath.org/).**

Until the SignPath integration is live, releases are published **unsigned**;
Windows SmartScreen will show an "unknown publisher" warning the first time you
run the installer. The three practical ways to confirm you have a legitimate
build until signing is in place:

1. Compare the SHA-256 hash of your `DWG2IFC-Setup.exe` against the value in
   `SHA256SUMS.txt` published alongside the release.
2. Confirm the release was published by GitHub Actions on this repository — the
   **Releases** page shows the exact workflow run and commit that produced each
   artefact.
3. Build the plugin yourself from source (Option 3 above) and compare.

## Entity mapping

DWG2IFC parses both Plant 3D process components and general-purpose 3D solids from base AutoCAD.
For every entity, the plugin walks a three-tier extractor and uses the first tier that produces
geometry: **(1) direct type switch** on AutoCAD CLR types, **(2) parametric pipe** reconstruction
through Plant 3D reflection, **(3) explode-and-recurse fallback** for custom DBX objects.

| DWG / Plant 3D object | How geometry is extracted | IFC entity (IFC4 / IFC2x3) | PredefinedType |
|---|---|---|---|
| Pipe (`AcPpDb3dPipe`) — straight or curved | Tier 2: reflection reads `StartPoint`/`EndPoint`/`OutsideDiameter`/`WallThickness` and emits a parametric swept-disk; curved pipes sample `PathCurve` | `IfcPipeSegment` as `IfcSweptDiskSolid` *(or triangulated cylinder mesh if the user turns parametric pipes off)* | `.RIGIDSEGMENT.` |
| Elbow, tee, reducer, flange, olet, cap | Tier 1 for catalog block refs, Tier 3 explode fallback for pure procedural fittings | `IfcPipeFitting` (mesh) | `.BEND.`, `.JUNCTION.`, `.TRANSITION.`, `.CONNECTOR.` |
| Cock, gate valve, control valve, damper | Tier 1 or Tier 3 depending on how the catalog defines it | `IfcValve` | `.NOTDEFINED.` (refined by name heuristics) |
| Pump, compressor | Tier 1 B-Rep mesh / Tier 3 explode | `IfcPump` / `IfcFlowMovingDevice` | `.NOTDEFINED.` |
| Vessel, apparatus, tank, exchanger | Tier 1 solid-geometry mesh | `IfcEnergyConversionDevice` | `.NOTDEFINED.` |
| Pipe support, hanger | Tier 1 if solid; Tier 4 axis-cylinder fallback is opt-in via config | `IfcDiscreteAccessory` / `IfcFastener` | `.NOTDEFINED.` |
| Plant 3D structural steel | B-Rep profile and extrusion | `IfcBeam` / `IfcColumn` / `IfcMember` | `.BEAM.`, `.COLUMN.`, `.NOTDEFINED.` |
| Solid body (`AcDb3dSolid`) | Tier 1: analytical B-Rep topology → triangulated face set | `IfcTriangulatedFaceSet` (IFC4) / `IfcFacetedBrep` (IFC2x3) | `IfcBuildingElementProxy` |
| Surface / Body (`Surface`, `Body`) | Tier 1: AcBr boundary shell | `IfcTriangulatedFaceSet` | `IfcBuildingElementProxy` |
| Region (`AcDbRegion`) | Tier 1: triangulation of a closed contour | `IfcTriangulatedFaceSet` | — |
| 3D face (`AcDbFace`) | Tier 1: direct 3/4-point polygon triangulation | `IfcTriangulatedFaceSet` | — |
| Block (`AcDbBlockReference`) | Tier 1: recursive traversal using `BlockTransform` matrix, depth ≤ 8 | Expanded assembly | Based on child solids |
| Mesh (`AcDbSubDMesh`, `PolyFaceMesh`) | Tier 1: indexed vertex arrays | `IfcTriangulatedFaceSet` | — |
| Any unrecognised Plant/custom DBX entity | Tier 3: `Entity.Explode` in-memory, recurse through Tier 1 on each child, depth ≤ 2 | Mesh of whatever explode produced | `IfcBuildingElementProxy` |

2D primitives (lines, arcs, splines, dimensions, text annotations) are not converted into 3D
solids and are filtered out during candidate collection.

## Properties and data sets (Pset)
Every object is tagged with buildingSMART property sets built from Plant 3D DataLinks attributes
and AutoCAD system metadata:

1. **`Pset_Plant3D_Piping` (Process properties):**
   - `LineNumberTag` — process line number;
   - `Size` / `NominalDiameter` — nominal diameter (DN);
   - `Spec` — pipe specification / mini-catalog;
   - `Description` / `ShortDescription` — name and factory mark;
   - `Material` — steel grade / alloy;
   - `Service` — process medium;
   - `DesignPressure` / `DesignTemperature` — design parameters;
   - `Tag` — positional designation for instrumentation and valves;
   - `Status` — design status (New, Existing, Demolition).

2. **`Pset_AutoCAD_Metadata` (Base properties):**
   - `Layer` — original drawing layer;
   - `Handle` — immutable hexadecimal object handle in the DWG database;
   - `Color` — RGB / color index;
   - `Entity` — original AutoCAD class type (`AcDb3dSolid`, `AcPpDb3dPipe`, etc.).

3. **Service identifiers:**
   - Every element is assigned a deterministic **IFC GUID** (22-character base64 per RFC 4122),
     computed from the hash of the handle and parameters. GUIDs do not change when geometry is
     re-exported, which prevents broken links and false collisions in Solibri, Navisworks, and BIMCollab.

## Performance and stability
The plugin is designed for uninterrupted work on large industrial models and complex assemblies:

- **Streaming I/O:**
  The IFC generator writes to disk via a buffered `StreamWriter` as it walks the drawing.
  The file is never assembled in RAM as one giant DOM tree, so a 500,000-polygon model
  export fits within 150–200 MB of the AutoCAD process working set.
- **Vertex welding:**
  Automatic merging of duplicate spatial coordinates in the mesh, with a configurable
  tolerance (default 0.5 mm), reduces the vertex list and the final IFC file size by **50–70%**.
- **IFC4 TriangulatedFaceSet:**
  The IFC4 schema uses the compact `IfcCartesianPointList3D` representation with a single
  triangle index array. This shrinks the file 3–4× compared to the legacy `IfcPolyLoop`
  format from IFC2x3.
- **Offset for distant coordinates:**
  If a drawing uses geodetic coordinates (such as MSK / SK-42) that place it tens of
  kilometers from the origin, the plugin can offset the geometry to a local zero and
  record the delta in site metadata.

### Defeating heap corruption (0xc0000374) in the B-Rep API
During development, a critical AutoCAD API vulnerability was identified and fixed:
in the `acdbmgdbrep.dll` library, the iterator classes `Element2d` and `Node` inherit
from `DisposableWrapper` and hold unmanaged pointers to the facet buffer of the native
`Mesh2d` object.

When processing a model containing several thousand solids, the .NET garbage collector
could not destroy the wrappers in time, so they ended up on the finalization queue. When
the native buffer was destroyed, the deferred finalizer call touched freed memory and
crashed the AutoCAD process with the fatal error `STATUS_HEAP_CORRUPTION`
(`0xc0000374` in `ntdll.dll`).

In DWG2IFC, topology traversal is isolated inside deterministic `using` blocks: unmanaged
resources are released immediately and GC finalizer calls are suppressed.

> **Stress-test result on the real drawing "Горка без крыши финал.dwg":**
> The model contains **3,862 objects** with highly complex blocks of **114,000+ faces**.
> The plugin processed all **3,832 spatial solids (over 300,000 polygons)** without a single
> memory failure, with exit code `0`.

## Honest limitations
We make no promises of a "magic button that does everything for you". The scope is strict:

- **2D annotations and wireframes:** Lines (`AcDbLine`), circles, polylines, and text do not
  form closed solids and are not exported as IFC geometry. 2D drawings are the proper tool for them.
- **Block nesting depth:** Recursive block parsing is capped at depth 8 to protect against
  cyclic cross-references inside damaged drawings. The separate explode-fallback recursion
  is capped at depth 2.
- **Paper space entities** are not exported; only ModelSpace is walked.
- **Non-manifold solids:** If a 3D solid is damaged inside the DWG and the ACIS/B-Rep kernel
  returns a topology error during tessellation, the plugin falls back to a bounding-box
  parallelepiped so the object is still represented and its property set survives in the IFC.
  This fallback is applied **only** to `AcDb3dSolid`; it is deliberately **not** applied to
  Plant 3D custom entities, because silently emitting a mystery box for an unknown Plant
  object would mislead downstream BIM reviewers. Unknown Plant entities are logged with
  their AutoCAD handle and skipped instead.
- **Xref'd geometry** is not walked by default. Opt in via the `IncludeXrefs` flag in
  `%APPDATA%\Plant3D_IfcExporter\config.json`.
- **Pipes with no diameter available** — if a pipe entity's outside diameter cannot be read
  from the Plant 3D API, from DataLinks, nor from its `Size` tag, the pipe is skipped
  (listed in the export log as `MissingDiameter`). We will not guess a diameter.
- **The receiving BIM tool matters.** `IfcSweptDiskSolid` is supported by Revit, Tekla,
  Solibri, FZK Viewer, and BIMvision. A handful of older viewers only render triangle
  meshes — if you hit one, uncheck "Export Plant 3D pipes as parametric geometry" in the
  settings dialog and pipes will come through as tessellated cylinders instead.

## Troubleshooting
*For non-technical users: these are the three things to try, in order, when an export
doesn't contain what you expected.*

**1. Nothing / fewer objects than expected came out**

Click the **Diagnose drawing** button on the **DWG2IFC** ribbon (or type `DWG2IFC_DIAG`).
It writes a `<drawingname>.diag.log` file next to your DWG. Open it in Notepad. For every
3D object in the drawing it tells you: the AutoCAD handle, the class name, which extraction
tier handled it, and — if the object was skipped — the reason. You can select an object by
its handle in AutoCAD with `(handent "<handle>")` + `highlight`, or just look at the class
names in the log to see what kind of objects aren't being picked up.

**2. Pipes are missing from the IFC but fittings/valves appear**

Pipes in Plant 3D are drawn procedurally and need the parametric path to export. In the
export dialog, confirm **Export Plant 3D pipes as parametric geometry** is checked. Also
make sure your pipes have a nominal diameter set in Plant 3D — pipes with no diameter info
anywhere (Plant API, DataLinks, Size tag) are deliberately skipped and listed in the log
as `MissingDiameter`.

**3. The export log file**

Every export writes `<yourfile>.export.log` next to the IFC (unless you unchecked
**Write diagnostic log**). It gives you:
- how many 3D objects were exported, by category (Pipes, Fittings, Valves, Equipment, …);
- which extraction tier each category used;
- the AutoCAD handle and reason for every skipped object (first 100 by default).

Send this log to the maintainer if you need help — it contains no drawing contents, only
object counts and class names.

## Cross-version compatibility
The plugin is built for **.NET 8.0 (x64)** and runs on recent Autodesk versions:

| CAD | Runtime | Compatibility |
|---|---|---|
| AutoCAD 2025 | .NET 8.0, x64 | Full |
| AutoCAD 2026 | .NET 8.0, x64 | Full |
| AutoCAD Plant 3D 2025 | .NET 8.0, x64 | Full (with DataLinks extraction) |
| AutoCAD Plant 3D 2026 | .NET 8.0, x64 | Full (with DataLinks extraction) |

The bundle manifest is configured for the `R25.0`–`R26.0` series range, which guarantees
automatic loading on all minor updates and service packs of AutoCAD 2025 and 2026.

## Architecture
```
DWG2IFC/
├─ bundle/DWG2IFC.bundle/   ← Autodesk auto-loader bundle TEMPLATE (xml + icon only).
│  │                           The deployable bundle with compiled DLLs lives at
│  │                           bin/Release/bundle/ after build.ps1 runs.
│  ├─ PackageContents.xml   registration manifest, runtime requirements, commands
│  └─ Contents/icon32.png   ribbon icon
├─ src/
│  ├─ DWG2IFC/              ← main plugin (AutoCAD .NET Extension)
│  │  ├─ Commands/          command entry points (DWG2IFC, IFCEXPORT, PLANTTOIFC,
│  │  │                     IFCRIBBON, DWG2IFC_DIAG, TESTEXPORT, INSPECT_BLOCK)
│  │  ├─ Geometry/          Three-tier extractor (BrepMeshExtractor), swept-disk and
│  │  │                     mesh models, vertex welding
│  │  ├─ Ifc/               streaming IFC writer (ISO-10303-21) — mesh and
│  │  │                     IfcSweptDiskSolid emission, deterministic IFC GUIDs
│  │  ├─ Plant/             reflection-based Plant 3D DataLinks property extractor
│  │  │                     and PlantPipeGeometryHelper (pipe axis + radius reader,
│  │  │                     cylinder tessellator)
│  │  ├─ Settings/          export configuration (JSON in %APPDATA%\Plant3D_IfcExporter\)
│  │  └─ UI/                WPF settings dialog + ribbon manager
│  └─ DWG2IFC.Setup/        ← standalone WPF/CLI installer with embedded payload
├─ libs/AutoCAD_2025/       ← reference AutoCAD API assemblies (.NET 8.0)
├─ build.cmd / build.ps1    plugin and installer build scripts
├─ install.ps1              bundle deployment into an installed AutoCAD
└─ pack.ps1                 release distribution builder into dist\
```

### Advanced settings (`%APPDATA%\Plant3D_IfcExporter\config.json`)

Most users never need to edit this file — the UI dialog covers the important knobs. For
scripted deployments or power users, the full set of fields is:

| Field | Default | What it does |
|---|---|---|
| `UseAnalyticalPipes` | `true` | Pipes export as parametric `IfcSweptDiskSolid`. Off = tessellated cylinder mesh. |
| `EnableExplodeFallback` | `true` | For custom entities the direct type switch doesn't handle, explode a copy in memory and extract from the children. |
| `UseAnalyticalPipeSupports` | `false` | Opt-in: approximate `*Support*`/`*Hanger*` custom entities as a cylinder along their bounding-box's long axis. Not accurate — only turn on if your supports otherwise don't export at all. |
| `PipeTessellationSides` | `20` | Facet count for the cylinder tessellator (used when parametric pipes are off, and for support fallbacks). |
| `CurvedPipeSamples` | `16` | Sample count along a pipe's `PathCurve` when discretising a curved pipe into a polyline directrix. |
| `WriteExportLog` | `true` | Write a `.export.log` next to the IFC. |
| `MaxLoggedSkipped` | `100` | How many skipped-entity rows to include in the log. |
| `IncludeXrefs` | `false` | Walk xref model spaces as well as the host ModelSpace. |

## Build and packaging

Build from the command line:
```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Build a full release distribution:
```powershell
powershell -ExecutionPolicy Bypass -File pack.ps1 -Version 1.0.0
```

The script creates a `dist\` folder with:
- `DWG2IFC-Setup.exe` — standalone installer with the bundle zip embedded;
- `DWG2IFC-1.0.0.zip` — unpacked bundle for manual installation;
- `SHA256SUMS.txt` — file checksums.

## Ecosystem
DWG2IFC is part of an open pipeline for seamless model translation:
```
AutoCAD / Plant 3D (DWG) ──[ DWG2IFC ]──► IFC4 / IFC2x3 ──[ IFC2RVT ]──► Revit (native elements)
```
Related project: [IFC2RVT](https://github.com/AnT1pal/IFC2RVT) — direct IFC import into Revit
that builds native editable walls, floors, columns, and beams instead of dead DirectShape.

## License
DWG2IFC is free software: distributed under the terms of the
**GNU General Public License version 3 (GPLv3)**. Full text in the [LICENSE](LICENSE) file.

GPLv3 requires that the original copyright notice is preserved, that modifications are
clearly marked, and that source code is made available with every binary distribution.
This fork complies with all three obligations — the upstream copyright remains intact,
the "About this fork" notice at the top of this README lists the changes, and the full
source of the fork is published at
[github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0](https://github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0).

Original work © 2026 [baidurovlabs.ru](https://baidurovlabs.ru) — all rights to the
underlying design, geometry pipeline, and IFC writer belong to them. We are deeply
grateful for the upstream project.

English-fork modifications © 2026 **Wahid Hassani**
([github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0](https://github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0)),
released under the same GPLv3 license.
