# Surface Analysis Extension

**Version 1.0.2**

A SOLIDWORKS add-in that extends SOLIDWORKS' built-in surface analysis tools with:

- **Isophote and environment (matcap-style) zebra-stripe analysis** — with adjustable line count, line width, and edge blur. Supports isolating the overlay to selected faces only.
- **User-controllable mesh tolerance and chord angle** — direct control over analysis mesh density and precision, independent of the part's own display quality setting.
- **True-surface isocurve display** — real B-spline isocurves (not an approximation), shown per selected face, with a callout reporting each face's surface degree and control-point count in both the U and V directions.
- **Surface normal visualization.**
- **G2 (curvature) edge-continuity checking** between two faces, with live on-screen deviation reporting (validated against Rhino's own `EdgeContinuity` tool).

Everything is toggled from a single button added to the SOLIDWORKS CommandManager.

Built by Andrew Jackson — [AJ Design Studio LTD](https://ajdesignstudio.co.nz).

This software is provided free of charge, with no warranty of any kind.

## Changes since V1.0.1

- Made isocurves thicker, now 2 pixels wide
- Coloured isocurves to match UV direction. Also referenced in the callout
- Fixed - isocurves would not clear upon closure of the dialog box
- Fixed - dialog box was not working with Windows scaling

## Changes since v1.0.0

- Minimum mesh deviation lowered to 0.0001mm (was 0.001mm)
- Removed isocurve display for all visible surfaces — it was too slow on large models
- Added per-surface isocurve display, plus a surface degree/CV-count callout

## Requirements

- SOLIDWORKS 2025 (build/run) — see [Compatibility](#compatibility) below for other years
- .NET Framework 4.7.2
- Parts only — this add-in does not currently support assemblies
- An NVIDIA GPU — the add-in has only been tested on NVIDIA hardware

## Compatibility

The project is intentionally **compiled against SOLIDWORKS 2017's interop DLLs**, not 2025's, even though development happens on SW2025. This is deliberate: the codebase only uses API surface that already existed in SW2017, and building against the older, strongly-named interop assemblies has been confirmed (manually, with zero SW2025 interop DLLs present in the loaded path) to work correctly on SW2017, 2020, 2023, and 2025. Building against the 2017 references gives the widest real-world compatibility without any code changes.

The add-in has been tested to open and generally operate on SOLIDWORKS 2017, 2020, 2021, and 2023, and has been extensively tested on SOLIDWORKS 2025.

## Building

1. Clone this repo.
2. You'll need the three SOLIDWORKS interop DLLs, which are **not included in this repo** (see [Interop DLLs](#interop-dlls) below). Add them as references to the `SwIsophoteAddin` project:
   - `SolidWorks.Interop.sldworks.dll`
   - `SolidWorks.Interop.swconst.dll`
   - `SolidWorks.Interop.swpublished.dll`

   For each reference: set **Embed Interop Types = False** and **Copy Local = True**.
3. Build in **Release** configuration. On first build, the project self-registers via `RegAsm` (requires Visual Studio to be run **as Administrator** — a normal, non-elevated build will fail silently to register and the add-in won't appear in SOLIDWORKS).
4. Launch SOLIDWORKS and enable **Surface Analysis Extension** under Tools > Add-Ins.

### Interop DLLs

SOLIDWORKS' own API documentation (`install_dir\api\redist\redist.txt`) explicitly grants redistribution rights for these three DLLs, but they aren't committed to this repo — pull them from your own SOLIDWORKS install instead:

```
<SOLIDWORKS install dir>\api\redist\SolidWorks.Interop.sldworks.dll
<SOLIDWORKS install dir>\api\redist\SolidWorks.Interop.swconst.dll
<SOLIDWORKS install dir>\api\redist\SolidWorks.Interop.swpublished.dll
```

For the compatibility reasons above, source these from an **SW2017** install if you have one available. If you only have a newer SOLIDWORKS version installed, its own redist copies will also work for building and running on that version — just note the compatibility caveat above if you intend to distribute the result for use on other SOLIDWORKS years.

### Debugging in Visual Studio

Since this is a Class Library (COM add-in), Visual Studio can't "run" it directly — you need to point it at SOLIDWORKS itself:

1. Project Properties → **Debug** tab
2. **Start action** → Start external program → browse to `SLDWORKS.exe` (typically `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe`)
3. Press F5. Visual Studio launches SOLIDWORKS and breakpoints will hit once the add-in loads.

(Make sure you're debugging the **Debug** configuration, not Release — Release strips the debug-launch settings.)

## Installing (pre-built)

An Inno Setup installer script is included under `installer/`. It:

- Installs to `Program Files\Surface Analysis Extension` (DLL, .pdb, icons, and the three interop DLLs bundled from the SW2017 redist folder)
- Registers the add-in via `RegAsm.exe /codebase` on install, and unregisters on uninstall
- Shows a short welcome page (`WelcomeInfo.txt`) before install

To build the installer yourself, you'll need [Inno Setup](https://jrsoftware.org/isinfo.php) and a Release build of the project with the three interop DLLs present in the build output (which happens automatically if you set Copy Local = True as described above).

## License

Licensed under the MIT License — see [`LICENSE`](LICENSE).

