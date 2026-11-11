# Inventor Sizer

Change the size in the assembly name, for example `FP-386x656x47` → `FP-800x1200x47`, set the
number of holes per wall, and the assembly and all its parts resize. It handles the three panel
types FP (47 mm), TP (42 mm) and RP (44 mm), and splits the inner panel in two above 500 mm
width. The equations and the setup per master are in [docs/PANEL_RULES.md](docs/PANEL_RULES.md). A vector drawing of the
panel (any view, with bill of materials and reference axis) updates as you type, before
anything touches Inventor.

```
 Window (type, name, grid, settings) ──► hole rules + checks ──► Parameters.xml
        drawing + bill of materials                               Inventor user parameters
                                                                  (assembly + every part)
```

## Requirements

Nothing to install. The tool uses:

- Windows PowerShell 5.1 (included with Windows 10 and 11)
- Autodesk Inventor (tested against the 2024 API)

## Files

| File | Purpose |
|---|---|
| `Launch.bat` | Double-click to start. |
| `InventorSizer.ps1` | The window: type, name, parameter grid, settings, drawing, log. |
| `lib\Common.ps1` | Logging, error line numbers, `settings.json`. |
| `lib\ParamFile.ps1` | `Parameters.xml` in Inventor's parameter-export format. |
| `lib\HoleRules.ps1` | Hole count → positions, pattern values and the blocking checks. |
| `lib\Standards.ps1` | Product types from `standards\` and the bill of materials. |
| `lib\Inventor.ps1` | Inventor COM: open, push parameters, inner-panel split, rebuild, save as new size, Part Number. |
| `lib\Twin.ps1` | Drawings: views, bill of materials, axis, painting in the window, SVG sheet. |
| `lib\Hardware.ps1` | Hinge and handle holes: learned from a master, re-placed for every size. |
| `lib\TreeExport.ps1` | Exports any assembly's component tree, constraints and patterns to CSV and JSON. |
| `standards\hardware\` | Learned hardware holes per type (created by **Learn hardware holes**). |
| `standards\FP.json`, `TP.json`, `RP.json` | One file per panel type. |
| `docs\PANEL_RULES.md` | Equations, the split rule, setup per master, open points. |
| `Parameters.xml`, `settings.json` | Created on first run, next to the script. |
| `setup-xml\` | One-time setup files for the FP (386) and TP (519, 724) master documents. |
| `InventorSizer_excel.ps1` + `Launch_excel.bat` | The older Excel version (no hole rules, no drawing). |

## Using it

1. Run `Launch.bat`. If Inventor has an assembly open, the window reads its name.
2. Get the assembly you start from, in one of three ways:
   - **Open master**: opens the master assembly of the selected **Type** (from the standard).
   - **Open assembly...**: browse for any `.iam`. Inventor is started if it isn't running.
   - **Read from Inventor**: uses the assembly that is already active in Inventor.
3. Edit the **name** or the values in the **grid** (they stay in sync). `HolesL` and `HolesW`
   set the holes per length wall and per width wall. The name may start with a project number
   (`6357-3 TP-519x1194x42`); the **Type** follows the prefix in front of the size, or the
   height if there is none, and brings its own `HoleZ`.
4. The drawing on the right updates as you type:
   - **View**: Isometric, Front, Back, Left, Right, Top or Bottom.
   - **Show hidden edges**: back edges, back-wall holes, floor and wall thickness as dashed lines.
   - The **bill of materials** (bottom right) lists the parts of the type with their sizes and
     the number of holes; the **axis** (bottom left) shows X, Y and Z for the current view
     (a dot = axis points at you, a cross = away from you).
   - **Save drawing (SVG)...** writes a drawing sheet: the isometric view top left, the front
     and top views in the middle area, hole notes, and the bill of materials.
5. If a rule is broken, the reasons appear in red under the grid, the bad holes are red in the
   drawing, and **Apply to Inventor** is greyed out. Grey text there is a note only (for
   example that the inner panel will be split).
6. Click **Apply to Inventor**:
   - **Name unchanged:** values go into Inventor, the model rebuilds, and the files are saved in place.
   - **Name changed:** the model is saved as **new files** (`FP-800x1200x47.iam`,
     `800x1200x47-Au.ipt`, `800x1200x47-In.ipt`). The Part Number iProperty gets the new size
     too. The original files on disk are not touched. Existing files are never overwritten.
7. The log at the bottom lists every parameter that changed and in which file. Errors include
   the script line number.
8. **Learn hardware holes** (once per type, with its master open): reads the hinge and handle
   holes from Inventor. From then on they show in orange in every drawing of that type.
9. **Export assembly tree...**: writes the component tree, the files used (with how many times
   and their lengths) and every constraint of the active assembly to CSV and JSON.

## Where new files are saved

**Standard rule: each new file is saved in the same folder as the file it was made from.**
The new assembly goes next to the original assembly; each new part goes next to its original
part (e.g. `K:\CAD Bibliothek\2. Paneele\47er\Einzelteile\`). Parts whose names don't contain
the size (shared or library parts) are not copied; the new assembly keeps using them.

Another folder is only used when you ask for it: tick **Save new sizes to another folder
(this session only)** in Settings and choose the folder with **Browse...**. The tick is off
every time the app starts and the folder is not saved, so the standard rule always comes back.

## Settings

Under the grid, saved to `settings.json`:

- **Minimum hole gap**: smallest distance between hole centres before Apply is blocked (default 10 mm).
- **Sheet thickness** (Stärke): used for the centre-pair offset and the inner-panel size (default 1 mm).
- **Split inner panel above width**: wider panels get 2 inner panels (default 500 mm, 0 = never).
- **Automatic hole counts, max. pitch**: `HolesL` and `HolesW` follow the size so no gap between
  wall holes exceeds this value (default 400 mm; 0 = enter the counts by hand).

## Inner-panel split

Above the split width the `-In` part is made half as wide and used twice. On Apply the app
sets `InSplit = 2`, adds a second copy of the `-In` part to the assembly next to the first, and
mates the two with three flush constraints on their origin planes. Going back below the split
width deletes the second copy. The inner part's file is always named `-In-2T` when split and
`-In` when not, for every type; the app renames it on Apply when needed (the old file stays). A split panel always gets 4 holes on the width wall (2 edge
holes + the centre pair), because each half has a hole at both of its ends. The `-In` part
needs the `InSplit` parameter from its setup file; see [docs/PANEL_RULES.md](docs/PANEL_RULES.md).

## Product standards: `standards\`

Each `*.json` file in `standards\` is one panel type shown in the **Type** list.

| Type | Height | State |
|---|---|---|
| FP (`FP-`) | 47 | active; master `FP-386x656x47.iam` next to the app |
| TP (`TP-`) | 42 | active; put the master path into `TP.json` |
| RP (`RP-`) | 44 | active; put the master path into `RP.json` |

Fields of a type:

| Field | Meaning |
|---|---|
| `id`, `name` | Identifier and the text shown in the Type list. |
| `enabled` | `false` hides the type. |
| `prefix` | Text directly in front of the size, e.g. `TP-`. |
| `height` | Panel height of the type. Used to recognise the type when the name has no prefix; another height only gives a note. |
| `defaults` | Values set when the type is selected, e.g. `{ "HoleZ": 20 }`. |
| `minHolesL` | Smallest hole count per length wall (TP: 2). |
| `splitAxis` | `auto`, or `X`, `Y`, `Z`: the axis of the `-In` part its width runs along, if the app cannot tell. |
| `master` | Master assembly for **Open master**. A relative path is relative to the app folder. |
| `parts` | Bill of materials: `suffix`, optional `splitSuffix` (file ending of a split inner panel, e.g. `-In-2T`), `description`, `qty`, and `role`: `outer`, `inner` or `other`. |
| `notes` | Free text, ignored by the app. |

## Hole rules

Holes per wall are set separately for the length walls and the width walls (opposite walls
always match). Diameter is fixed at 4.1 mm (read-only in the grid). `E` = HoleEdge (50 mm).

| Holes | Length wall | Width wall |
|---|---|---|
| 0 | none | none |
| 1 | middle (L / 2) | middle (W / 2) |
| 2 | E from each end | E from each end |
| 3, 4, 5, … | E from each end, the rest evenly spaced: (L − 2E) / (n − 1) | odd counts are refused |
| 4 | (as above) | E from each end + a centre pair at W/2 ± 48.5 mm |
| 6, 8, … | (as above) | evenly spaced, like the length wall |

With a split inner panel the width wall takes 0 or 4 holes only; 2 is raised to 4.

48.5 mm = HoleEdge − thickness − Clearance, so the -Au and -In holes line up.

**Apply is blocked when:**
- two hole centres are closer than the minimum gap
- a width wall gets an odd count above 1
- a wall is too short for HoleEdge, or too narrow for the centre pair
- the name has no size in it, or disagrees with the values

## What the app writes into Inventor

| Parameter | Use it in the model for |
|---|---|
| `Width`, `Length`, `Height`, `HoleEdge`, `HoleZ`, `HoleDia`, `Clearance` | Inputs; model dimensions reference them by formula. |
| `HolesL`, `HolesW` | The counts you entered (for reference). |
| `HoleStartL` / `HoleStartW` | Sketch dimension of the first hole from the wall end. -In part: `HoleStartL - Stärke - Clearance`. |
| `HolePitchL` / `HolePitchW` | Rectangular pattern spacing along the wall. |
| `HoleCountL` / `HoleCountW` | Rectangular pattern count (always ≥ 1). |
| `HoleOnL` / `HoleOnW` | Hole + pattern: *Feature Properties → Suppress if* `HoleOnL = 0`. |
| `HoleMidOnW` | Centre-pair hole + pattern: *Suppress if* `HoleMidOnW = 0`. |
| `HoleMidOffsetW` | Formula in the part: centre pair at `Width / 2 - HoleMidOffsetW`, pattern count 2, spacing `2 * HoleMidOffsetW`. |
| `InSplit` | 1 or 2. `-In` width: `( Width - 2 * Stärke - 2 * Clearance ) / InSplit`. |
| `InHolePitchW`, `InHoleCountW`, `InHoleMidOnW` | Width-wall hole pattern of the `-In` part (differs from the tray's when split). |

Values are written as numbers in Inventor's internal units, so the Windows decimal setting
(4,1 or 4.1) doesn't matter, and unchanged values are left alone.

## One-time setup: `setup-xml/`

Import one file per document through *Manage → Parameters*, **on a copy first**. The part
files add all the user parameters above (with today's 386×656×47 values) and set the
existing model dimensions to formulas. Every formula gives the same number as today, so a
correct import changes no geometry. Then build the hole patterns as described in the table.

| File | Import into |
|---|---|
| `386x656x47-Au-setup.xml` | `386x656x47-Au.ipt` |
| `386x656x47-In-setup.xml` | `386x656x47-In.ipt` (import again: it now adds `InSplit`) |
| `FP-386x656x47-setup.xml` | `FP-386x656x47.iam` |
| `6357-3 519x1194x42-Au-setup.xml`, `…-In-setup.xml`, `6357-3 TP-519x1194x42-setup.xml` | the TP 519 master |
| `6357-3 724x1194x42-Au-setup.xml`, `…-In-2T-setup.xml`, `6357-3 TP-724x1194x42-setup.xml` | the TP 724 master (recommended TP master) |
| `6357-3 571x996x44-Au-setup.xml`, `…-In-setup.xml`, `6357-3 RP-571x996x44-setup.xml` | the RP 571 master |

## `Parameters.xml`

Holds the input values in Inventor's own export layout (edit in Notepad, or import into a
document by hand). Only plain values like `386 mm` or `4.1 mm` belong here; formulas live in
Inventor. Older files without `HolesL` / `HolesW` are upgraded automatically.

## Troubleshooting on another PC

| Symptom | Cause / fix |
|---|---|
| "Inventor is not running" although it is open | Inventor and the app run at different permission levels. Start both normally, or both as administrator. |
| Scripts blocked / nothing happens | Unblock the zip before extracting (right-click → *Properties* → *Unblock*), or unblock each `.ps1`, including those in `lib\`. |
| Error mentioning "language mode" | Company PowerShell lockdown (Constrained Language Mode). Needs an IT exception. |
| **Open master** greyed out | The `master` path in `standards\FP.json` doesn't point to an existing file on this PC. |
| Parts not found | The `K:` drive isn't mapped the same way on this PC. |
| Saving `Parameters.xml` fails | The app folder is read-only (e.g. a network share). Copy the folder locally. |

## Not done yet

- Drawings (`.idw` / `.dwg`) aren't copied or renamed for new sizes.
- Hinge and handle counts are fixed by the master (see docs/PANEL_RULES.md, section 8).
- Building a whole box (profiles, corner joints, panels) is planned (section 9 there).
- Read-only files (library, Content Center, Vault checked-in) are skipped. With Vault, check out first.

## GR view (unit frame)

Under the drawing, **Show** switches between **Panel** and **GR (box)**. The GR view draws a
whole unit frame:

- **Load GR export...**: choose the iLogic tree export and the constraints export (or one
  export that contains both).
- **Read GR from Inventor**: reads the active GR assembly. This also happens on **Read from
  Inventor** or **Open assembly...** when the assembly's name ends in `GR`.

It shows:

- each frame as a profile wireframe, numbered F1, F2, ... along the unit
- the frame lengths and the overall size
- a frame table: sizes, plus the panels, dividers and parts on each frame
- a cut list: profile lengths × count, corners, joints, panels by type and size, dampers, stubs, base frame

**Save drawing (SVG)...** writes the GR sheet. The layout is worked out from the constraints,
because these exports have no positions. See docs/PANEL_RULES.md, section 11.
