# Inventor Sizer

Change the size in the assembly name, for example `FP-386x656x47` → `FP-800x1200x47`, set the
number of holes per wall, and the assembly and all its parts resize. A vector drawing of the
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
| `lib\Inventor.ps1` | Inventor COM: open, push parameters, rebuild, save as new size, Part Number. |
| `lib\Twin.ps1` | Drawings: views, bill of materials, axis, painting in the window, SVG sheet. |
| `standards\FP.json` | The FP standard: one entry per FP type. |
| `Parameters.xml`, `settings.json` | Created on first run, next to the script. |
| `setup-xml\` | One-time setup files for the 386 master documents. |
| `InventorSizer_excel.ps1` + `Launch_excel.bat` | The older Excel version (no hole rules, no drawing). |

## Using it

1. Run `Launch.bat`. If Inventor has an assembly open, the window reads its name.
2. Get the assembly you start from, in one of three ways:
   - **Open master**: opens the master assembly of the selected **Type** (from the standard).
   - **Open assembly...**: browse for any `.iam`. Inventor is started if it isn't running.
   - **Read from Inventor**: uses the assembly that is already active in Inventor.
3. Edit the **name** or the values in the **grid** (they stay in sync). `HolesL` and `HolesW`
   set the holes per length wall and per width wall.
4. The drawing on the right updates as you type:
   - **View**: Isometric, Front, Back, Left, Right, Top or Bottom.
   - **Show hidden edges**: back edges, back-wall holes, floor and wall thickness as dashed lines.
   - The **bill of materials** (bottom right) lists the parts of the type with their sizes and
     the number of holes; the **axis** (bottom left) shows X, Y and Z for the current view
     (a dot = axis points at you, a cross = away from you).
   - **Save drawing (SVG)...** writes a drawing sheet: the isometric view top left, the front
     and top views in the middle area, hole notes, and the bill of materials.
5. If a rule is broken, the reasons appear in red under the grid, the bad holes are red in the
   drawing, and **Apply to Inventor** is greyed out.
6. Click **Apply to Inventor**:
   - **Name unchanged:** values go into Inventor, the model rebuilds, and the files are saved in place.
   - **Name changed:** the model is saved as **new files** (`FP-800x1200x47.iam`,
     `800x1200x47-Au.ipt`, `800x1200x47-In.ipt`). The Part Number iProperty gets the new size
     too. The original files on disk are not touched. Existing files are never overwritten.
7. The log at the bottom lists every parameter that changed and in which file. Errors include
   the script line number.

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

## Product standards: `standards\`

Each `*.json` file in `standards\` is one product family; each entry in `variants` is one type
shown in the **Type** list. `FP.json` contains:

| Type | State |
|---|---|
| FP with 1 inner panel (`FP-`) | active; master `FP-386x656x47.iam` next to the app |
| FP with 2 inner panels (`FP2-`) | prepared, `enabled: false` |
| FP with 3 inner panels (`FP3-`) | prepared, `enabled: false` |

Fields of a type:

| Field | Meaning |
|---|---|
| `id`, `name` | Identifier and the text shown in the Type list. |
| `enabled` | `false` hides the type until it's ready. |
| `prefix` | Start of the assembly name, e.g. `FP-`. The type is picked automatically from the name. |
| `master` | Master assembly for **Open master**. A relative path is relative to the app folder. |
| `parts` | Bill of materials: `suffix` (added to the size, e.g. `-Au` → `386x656x47-Au`), `description`, `qty`, and `role`: `outer` (size W × L × H), `inner` (size reduced by 2 × thickness + 2 × clearance, height − thickness) or `other` (no size shown). |
| `notes` | Free text, ignored by the app. |

To add the 2-panel type later: build its master assembly, put its path in `master`, check the
prefix and parts, and set `enabled` to `true`.

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
| `386x656x47-In-setup.xml` | `386x656x47-In.ipt` |
| `FP-386x656x47-setup.xml` | `FP-386x656x47.iam` |

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
- The drawing shows one inner panel; for the 2- and 3-panel types only the bill of materials changes.
- Read-only files (library, Content Center, Vault checked-in) are skipped. With Vault, check out first.
