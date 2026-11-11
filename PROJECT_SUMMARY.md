# Inventor Sizer – project summary (handoff)

Paste this file into a new chat to continue. The full code is in `InventorSizer.zip`.

## Goal

A tool for Autodesk Inventor 2024 (German locale) that resizes a panel assembly
("Festes-Paneel", FP) from its file name. Example: `FP-386x656x47` → Width 386 × Length 656 ×
Height 47 mm. Changing the name resizes the assembly and its parts, keeps their features, and
saves the new size as new files.

## The user's model

- Assembly `FP-386x656x47.iam`. The master currently sits in the app folder
  (`C:\Users\la\Desktop\InventorSizer\`).
- Parts are in `K:\CAD Bibliothek\2. Paneele\47er\Einzelteile\`:
  - `386x656x47-Au.ipt`: outer sheet-metal tray.
  - `386x656x47-In.ipt`: inner panel, inset by thickness 1 mm + clearance 0.5 mm per side.
- The assembly has offset constraints `d0` and `d2` of 0.5 mm (clearance), and `d1` = 0.
- Hole mapping, confirmed by the user in Inventor's Parameters dialog:
  - **Au part:**
    - `d0`, `d1`, `d7` are the width, length and height.
    - `HoleEdge_Au` (renamed by the user), `d63`, `d64` and `d94` are 50 mm, the hole distance from the edge.
    - `HoleZ_Au`, `d65` and `d66` are 22 mm, the hole height.
    - `d47` and `d67` are the 4.1 mm hole diameter.
  - **In part:**
    - `d0` and `d1` are `W-3` and `L-3`.
    - `d59` is `H-1`.
    - `d97`, `d98`, `d107` and `d108` are 48.5 mm.
    - `d99` and `d109` are 22 mm.
    - `d100` and `d110` are 4.1 mm.
- The sheet-metal thickness parameter is `Stärke` (German parameter names).

## Language and approach

- **Windows PowerShell 5.1 + COM:** it ships with Windows, so nothing needs installing.
- **WinForms window:** the user chose to go back to the plain window after trying web-based
  interfaces (gothic, white and Windows-XP styles), all of which were removed.
- **Value store:** `Parameters.xml` in Inventor's own parameter-export format. Excel was
  dropped, but the Excel version is kept separately.
- **How values reach Inventor:**
  - The app writes **user parameters** only.
  - Model dimensions reference those user parameters through formulas, set up once with the `setup-xml` files.
  - Values are matched by parameter name across the assembly and every part.

## Files (in the zip)

| File | Purpose |
|---|---|
| `Launch.bat` | Starts the app: `powershell -STA -ExecutionPolicy Bypass`. |
| `InventorSizer.ps1` | The WinForms window. |
| `lib\Common.ps1` | Logging (with script line numbers) and `settings.json` (minimum hole gap, thickness). |
| `lib\ParamFile.ps1` | Reads and writes `Parameters.xml`, and adds missing default entries. |
| `lib\HoleRules.ps1` | Hole rules and the checks that block Apply. |
| `lib\Standards.ps1` | Product types from `standards\*.json`, and the bill of materials. |
| `lib\Inventor.ps1` | COM: open or start Inventor, push values, rebuild, save as a new size, update the Part Number. |
| `lib\Twin.ps1` | Vector drawings: isometric and orthographic views, bill of materials, axis icon, painting in the window, the SVG sheet. |
| `standards\FP.json` | FP-1In is active. FP-2In and FP-3In are prepared but disabled, with placeholder prefixes `FP2-` and `FP3-`. |
| `setup-xml\*.xml` | One-time imports that add the user parameters and the `d…` formulas to the 386 master files. |
| `InventorSizer_excel.ps1` + `Launch_excel.bat` | The old Excel-based version, kept on request. |
| `README.md` | The full user guide. |

## Features (current version)

- **Name and grid stay in sync:** editing the name `FP-WxLxH` updates the values in the grid, and editing the grid updates the name.
- **Getting the starting assembly:** **Open master** (the selected type's master), **Open assembly…** (a file picker that starts Inventor if needed), or **Read from Inventor**.
- **Type dropdown:** product types come from `standards\`. The type is chosen automatically from the name prefix, and changing the type swaps the prefix in the name.
- **Apply to Inventor:**
  - Saves `Parameters.xml` and pushes the values.
  - Values are compared as numbers and written in Inventor's internal units, so the Windows decimal setting doesn't matter.
  - Rebuilds parts first, then the assembly.
  - If the name is unchanged, it saves in place.
  - If the name changed:
    - It does a Save As of every file whose name contains the old size, then the assembly.
    - It updates the Part Number iProperty.
    - It never overwrites existing files, and the originals stay untouched.
- **Save-location rule:**
  - Each new file goes in the folder of the file it was made from.
  - Another folder is used only if **Save new sizes to another folder (this session only)** is ticked. The tick is off at every start and the folder is never saved, as the user asked.
- **Hole rules:** set per wall, with opposite walls always matching. Diameter is fixed at 4.1 mm. E = HoleEdge = 50.
  - **Any wall:**
    - 0 holes: none.
    - 1 hole: in the middle.
    - 2 holes: E from each end.
  - **Length wall, 3 or more holes:** E from each end, the rest evenly spaced at (L − 2E)/(n − 1).
  - **Width wall:**
    - Even counts only above 2.
    - 4 holes: the 2 edge holes plus a centre pair at W/2 ± 48.5.
    - 6 or more: evenly spaced, like the length wall.
  - 48.5 = HoleEdge − thickness − Clearance.
- **Warn and block:** Apply is locked, with red messages under the grid, when:
  - two hole centres are closer than the minimum gap (default 10 mm)
  - a width wall gets an odd count above 2
  - a wall is too short for HoleEdge, or too narrow for the centre pair
  - the name has no size in it, or disagrees with the values
- **What the app writes for the holes:**
  - The counts: `HolesL` and `HolesW`.
  - The pattern values: `HoleStartL`/`W`, `HolePitchL`/`W` and `HoleCountL`/`W`.
  - The on/off flags: `HoleOnL`/`W` and `HoleMidOnW`.
  - `HoleMidOffsetW` is a formula set in the part.
- **The drawing:**
  - Views: Isometric, Front, Back, Left, Right, Top and Bottom. **Show hidden edges** adds dashed lines.
  - Holes have centre marks, and holes that break a rule are red.
  - A bill of materials and an XYZ axis icon appear in every view.
- **Save drawing (SVG):**
  - A 1400×990 sheet: the isometric view centred top-left, the front and top views in the middle area (top below front), hole notes, and the bill of materials bottom-right.
- **Settings:** minimum hole gap and sheet thickness, saved to `settings.json`.

## Test status

- The user confirmed earlier versions work against Inventor: renaming 386→399→800 saved the files correctly.
- The latest window was shown running (screenshot), with **Read from Inventor** working.
- Of the newest features, only the drawings were tested, rendered on Linux. Still untested on Windows: the Type dropdown, Open master, Open assembly, the folder checkbox, the view selector, the SVG sheet, and the BOM/axis painting in the window.

## Open items / next steps

1. **Test the latest version on Windows** and check:
   - Sheet thickness shows 1.00. A bug used to save 0.1, so the user may need to set it once.
   - The Browse button is visible.
2. **Model work still needed in Inventor:**
   - Import the `setup-xml` files into copies of the masters.
   - Build each wall's holes as a hole plus a rectangular pattern, using `HoleStart`/`Pitch`/`Count`.
   - Set *Feature Properties → Suppress if* using `HoleOnL`/`W` and `HoleMidOnW`.
3. **FP-2In / FP-3In:**
   - Build their masters.
   - Set the prefixes and master paths in `standards\FP.json` and enable them.
   - Define the inner-panel size rule; it's currently `role: other`, so no size is shown.
   - The drawing still shows one inner panel.
4. **Drawings (`.idw`/`.dwg`):** these aren't copied or renamed for new sizes yet.
5. **Possible:** a better-defined place for product rules such as edge distance, thickness and gap per type (currently in settings and defaults).

## Update: TP / RP types and the inner-panel split

- Three panel types, recognised by prefix or height: FP 47, TP 42, RP 44 (`standards\*.json`).
  The FP2-/FP3- placeholders are gone; the split rule replaces them.
- Names may carry a project lead: `6357-3 TP-519x1194x42`; parts are `6357-3 519x1194x42-Au` / `-In`.
- Split rule for all types: `W > 500` (setting) → `InSplit = 2`, the `-In` part is halved and a second
  occurrence is added and mated by `Update-InnerSplit` (`lib\Inventor.ps1`). TP names the split part `-In-2T`.
- `Update-PanelAssembly` resizes any panel assembly without the window (for a later box builder).
- Equations, setup per master, open questions, hinge/handle and box plans: `docs\PANEL_RULES.md`.
- Tested on Linux only: rules, naming, drawings, and the split script against a mocked Inventor.
  Not yet run against Inventor.
