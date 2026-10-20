> **About this fork**
>
> This repository is an **English-localized fork** of the excellent
> [DWG2IFC](https://baidurovlabs.ru) plugin originally created by **baidurovlabs.ru**.
> All of the engineering, geometry work, Plant 3D DataLinks extraction, B-Rep stability
> fixes, and the IFC writer itself are their work — enormous thanks to the upstream
> authors; this plugin would not exist without them.
>
> The only changes in this fork are:
> - A full English translation of the user-facing UI (export dialog, installer, ribbon,
>   command-line banner, package manifest).
> - Attribution notes added so users can tell which build they are running.
> - Fresh `ProductCode` / `UpgradeCode` GUIDs so Autodesk's Autoloader treats this fork
>   as a distinct product and does not conflict with an installed upstream build.
>
> No logic, geometry, classification, or export behavior has been modified.
>
> **Fork maintainer:** Wahid Hassani — [github.com/OneAndOnlyWahidHassani](https://github.com/OneAndOnlyWahidHassani)
> (contact via GitHub inbox).
> **Original author:** baidurovlabs.ru — all credit for the plugin's design and
> implementation belongs to them.
> **License (both upstream and this fork):** [GNU GPLv3](LICENSE).

---

A plugin for AutoCAD and AutoCAD Plant 3D that exports 3D models and process piping
to clean **IFC** (IFC4 / IFC2x3) with honest B-Rep geometry, correct classification, and
semantic property sets (Pset) — without process crashes, parameter loss, or heavy
commercial toolchains.

Original project: [baidurovlabs.ru](https://baidurovlabs.ru) · English fork: [github.com/OneAndOnlyWahidHassani](https://github.com/OneAndOnlyWahidHassani) · licensed under [GNU GPLv3](LICENSE)

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

## Quick start
A ready-to-use build is available in [Releases](https://github.com/AnT1pal/DWG2IFC/releases):
download `DWG2IFC-Setup.exe`, run it, and click "Install plugin". Administrator rights are
not required — the bundle registers in the user-level `ApplicationPlugins` auto-load folder.

```powershell
DWG2IFC-Setup.exe --install      # silent install
DWG2IFC-Setup.exe --uninstall    # remove the plugin and shortcuts
DWG2IFC-Setup.exe --status       # check registration status
```

From source:

```powershell
git clone https://github.com/AnT1pal/DWG2IFC.git
cd DWG2IFC
powershell -ExecutionPolicy Bypass -File install.ps1
```

The script compiles the plugin against .NET 8.0 and registers the bundle in AutoCAD.
Restart AutoCAD: the ribbon will show a **DWG2IFC** tab → **Export to IFC**.

Commands available in the AutoCAD / Plant 3D command line:
- `DWG2IFC` or `IFCEXPORT` — open the export dialog with schema, filter, and coordinate options;
- `PLANTTOIFC` — export using the default parameters;
- `IFCRIBBON` — force the ribbon tab to be restored or activated.

## Entity mapping

DWG2IFC parses both Plant 3D process components and general-purpose 3D solids from base AutoCAD:

| DWG / Plant 3D object | Geometry extraction method | IFC entity (IFC4 / IFC2x3) | PredefinedType / Classification |
|---|---|---|---|
| Pipe (`PIPE`, `AcPpDb3dPipe`) | B-Rep tessellation of the pipe body | `IfcPipeSegment` | `.RIGIDSEGMENT.` |
| Elbow, tee, reducer, flange | B-Rep tessellation of the fitting | `IfcPipeFitting` | `.BEND.`, `.JUNCTION.`, `.TRANSITION.`, `.CONNECTOR.` |
| Cock, gate valve, control valve, damper | Boundary triangulation of the valve | `IfcValve` | `.ISOLATING.`, `.REGULATING.` |
| Pump, compressor | B-Rep mesh of the unit | `IfcPump` / `IfcFlowMovingDevice` | `.CIRCULATOR.` / `$` |
| Vessel, apparatus, tank | Solid geometry of the equipment | `IfcEnergyConversionDevice` | Equipment / Tank |
| Pipe support, hanger | Tessellation of support elements | `IfcDiscreteAccessory` / `IfcFastener` | Fastening elements |
| Plant 3D structural steel (`Structure`) | B-Rep profile and extrusion | `IfcBeam` / `IfcColumn` / `IfcMember` | Beams, columns, braces |
| Solid body (`AcDb3dSolid`) | Analytical B-Rep topology | `IfcTriangulatedFaceSet` / `IfcFacetedBrep` | `IfcBuildingElementProxy` |
| Surface / Body (`Surface`, `Body`) | AcBr boundary shell | `IfcTriangulatedFaceSet` | `IfcBuildingElementProxy` |
| Region (`AcDbRegion`) | Triangulation of a closed contour | `IfcTriangulatedFaceSet` | Geometric face |
| 3D face (`AcDbFace`) | Direct 3/4-point polygon triangulation | `IfcTriangulatedFaceSet` | Polygonal facet |
| Block (`AcDbBlockReference`) | Recursive traversal of elements using the `BlockTransform` matrix | Expanded component assembly | Based on child solids |
| Mesh (`AcDbSubDMesh`, `PolyFaceMesh`) | Indexed vertex arrays | `IfcTriangulatedFaceSet` | Faceted mesh |

2D primitives (lines, arcs, splines, dimensions, text annotations) are not converted into 3D
solids and are filtered out during geometry validation.

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
  cyclic cross-references inside damaged drawings.
- **Non-manifold solids:** If a 3D solid is damaged inside the DWG and the ACIS/B-Rep kernel
  returns a topology error during tessellation, the plugin creates a safe bounding-box
  parallelepiped (Bounding Box B-Rep fallback), preserves all element properties, and
  continues exporting the rest of the drawing.

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
├─ bundle/DWG2IFC.bundle/   ← Autodesk auto-loaded bundle
│  ├─ PackageContents.xml   registration manifest, runtime requirements, commands
│  └─ Contents/             icons and compiled net8.0 binaries
├─ src/
│  ├─ DWG2IFC/              ← main plugin (AutoCAD .NET Extension)
│  │  ├─ Commands/          command entry points (DWG2IFC, IFCEXPORT, PLANTTOIFC, IFCRIBBON)
│  │  ├─ Geometry/          B-Rep solid tessellation, vertex welding
│  │  ├─ Ifc/               streaming IFC writer (ISO-10303-21), GUID generator
│  │  ├─ Plant/             dynamic extraction of PnP DataLinks properties via reflection
│  │  ├─ Settings/          export configuration (JSON in AppData)
│  │  └─ UI/                WPF interface styled after AutoCAD 2026, ribbon manager
│  └─ DWG2IFC.Setup/        ← standalone WPF/CLI installer with embedded payload
├─ libs/AutoCAD_2025/       ← reference AutoCAD API assemblies (.NET 8.0)
├─ build.cmd / build.ps1    plugin and installer build scripts
├─ install.ps1              bundle deployment into an installed AutoCAD
└─ pack.ps1                 release distribution builder into dist\
```

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
[github.com/OneAndOnlyWahidHassani](https://github.com/OneAndOnlyWahidHassani).

Original work © 2026 [baidurovlabs.ru](https://baidurovlabs.ru) — all rights to the
underlying design, geometry pipeline, and IFC writer belong to them. We are deeply
grateful for the upstream project.

English-fork modifications © 2026 **Wahid Hassani**
([github.com/OneAndOnlyWahidHassani](https://github.com/OneAndOnlyWahidHassani)),
released under the same GPLv3 license.
