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
| `lib\InvFast.cs` | Compiled reader for large assemblies (GR): all components and constraints in one call. |
| `lib\GrModel.ps1`, `lib\GrTwin.ps1` | GR editing; loaded on the first switch to GR editing. |
| `standards\FP.json`, `TP.json`, `RP.json` | One file per panel type. |
| `docs\PANEL_RULES.md` | Equations, the split rule, setup per master, open points. |
| `Parameters.xml`, `settings.json` | Created on first run, next to the script. |
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
9. **Set up master...**: applies the type's setup from `standards\<type>.json` to the open master (see below).

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

## One-time setup of a master: **Set up master...**

Each type's file, `standards\<type>.json`, holds everything about the type in one place:

- `setup`: per part role (`outer`, `inner`, `assembly`), the user parameters and the formulas
  for the master's model dimensions
- `hardware`: hinge and handle holes
- `hardwareCounts`: which counts can change

To set up a master:

1. Open the master in Inventor.
2. Pick its type.
3. Click **Set up master...**. The app creates or sets the user parameters, then writes the
   formulas into the `-Au` part, the `-In` part and the assembly.

Do it on a copy first. Every formula gives the number the model has today, so a correct
setup changes no geometry. The d-numbers belong to the master named in `setup.master`:

| Type | Master |
|---|---|
| FP | `FP-386x656x47` |
| TP | `6357-3 TP-724x1194x42` |
| RP | `6357-3 RP-571x996x44` |

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

**Switch to GR editing** (top right) switches the whole window between panel editing
(FP / RP / TP) and GR editing; each mode shows and runs only its own part. The GR view draws a
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

### Editing a GR (new GR types)

In GR mode the left side shows one row per frame, with its **Length**, **Width** and **Height**
as profile lengths in mm. Type a new size and press Enter, and the drawing and cut list follow:

- **Frames with the same width / height change together** (ticked by default). Changing one
  frame's width or height changes every frame that had the same value, so the unit stays one
  size. A section of its own height, like a 410 end piece, keeps its height. Lengths change one
  frame at a time.
- **What follows a frame size:**
  - frame profiles (`P150 1580mm` → `P150 1700mm`)
  - dividers within 60 mm of the size, keeping their difference (milled `P350 1620mm gefräst` → `1740mm`)
  - panel sides within 60 mm of the size (`FP-521x1616x47` → `FP-521x1736x47`)

  Split panel widths keep their size; check them.
- **Add frame (like selected)** adds a frame of the selected frame's size at the end of the unit.
  **Remove frame** deletes the selected frame with its panels and dividers; the frames behind it move up.
- **Swap L / W** and **Swap W / H** fix the orientation when the app guessed it wrong (common
  for a GR with one frame).
- **Save GR design...** / **Load GR design...** keep a GR as a JSON file, so a new GR type can
  be built from an existing one and opened again later.
- **Save cut list (CSV)...** writes every part with its length, count and frames.

Part names differ between product lines (e.g. `Joint1`). `standards\GR.json` lists the
name patterns that mark corners, joints, frame profiles, dividers and panels; add a line there
for new names.

The edits stay in the app for now. Writing them back into Inventor needs the profile part's
length parameter name; see docs/PANEL_RULES.md, section 11.
- **Write new GR to Inventor**: enter the **New GR name** (e.g. `5794 GR`) and the **Part prefix**
  (e.g. `5794`), with the GR open in Inventor. The app:
  - saves the GR under the new name (the original stays untouched)
  - creates each new profile length as `<prefix> P150 1700mm.ipt` next to the original profile
  - replaces those profiles and sets the moved divider offsets
  - saves the new GR

  Panels that need a new size are listed in the log. Added or removed frames are not written yet.
- **Dividers**: the list under the frames shows every P350. Dividers placed by an offset to a
  frame profile can be moved (Position, mm); the others hang on joints and follow them. In the
  drawing they are purple dashed lines, orange once moved.

Note: the frame drawing uses outer size = profile length + 2 × 130 (corner cube).

## Modes, speed and the dev log

- **Nothing is read from Inventor at start.** Use **Open assembly...**, **Read from Inventor**
  or **Read GR from Inventor** when you want to.
- **Panel editing** and **GR editing** are separate modes; the GR code loads on the first switch.
- **Reading a GR from Inventor** uses a small compiled reader (`lib\InvFast.cs`, built
  automatically the first time). It reads all components and constraints in one go instead of
  thousands of slow single reads.
- **Dev log** (tab next to the log): every step with its time in ms, including the number of
  Inventor calls. **Save dev log...** writes it to a file to send for diagnosis.

## TP: more hinges or handles

`HingeCount` and `HandleCount` in the grid (TP only) set the hinges and handles per door. They
are spaced evenly between the first and the last set:

- `HingePitch = (Length - 2 * 98 - 24) / (HingeCount - 1)`
- `HandlePitch = (Length - 2 * 161 - 78) / (HandleCount - 1)`

A count of 1 puts the set in the middle. The drawing shows the holes, red where they collide.
The app writes the counts and pitches into the master. There they drive linear patterns of the
hinge / handle holes and of the hinge / handle parts (docs/PANEL_RULES.md, section 14).

## GR editing: boxes, outer sizes, dividers

- **Boxes:** each frame is a box (Box 0, Box 1, ...), one row per box. Select a box and click
  **Add box right**, **Add box left** or **Add box front** to add one of the same size next to
  it; a spot already taken moves the new box past it. **Remove box** removes the selected one.
- **Outer sizes:** **Outer L / W / H** are the outside sizes including the corners. The P150
  profiles are calculated as outer − 2 × 130 and shown next to them.
- **Real positions:** read from Inventor, every divider and joint is drawn where it really is:
  - P350 purple, P350 gefräst green, a moved divider orange
  - corners grey squares, joints gold squares
- **Spacing:** dimension chains show the distance from the frame profile's inner face to each
  divider, and between subsequent dividers.
- **Export files** have no positions, so there the dividers placed by an offset are drawn
  schematically.
- Added boxes are shown and listed but not yet written into Inventor.

## Working without opening files in Inventor

**Panels (FP / RP / TP).** Each type has a master assembly: the path in `standards\<type>.json`
(set it with **Set master...**), or the first `.iam` in `masters\<type>\` next to the app. Type the
name and the sizes, then click **Apply to Inventor**. The app then:

1. asks once per session for the folder for the new panels;
2. opens the master in Inventor in the background, without a window, and applies the values;
3. saves the new panel (assembly and `-Au` / `-In` parts) into that folder, then opens it.

The master on disk is never changed, and whatever is open in Inventor is not used.

**GR.** A GR design (**Save GR design...**) remembers the GR assembly it came from. Saved in
`presets\GR\`, it appears in the **Preset** list and loads without Inventor. **Write new GR to
Inventor** opens that source GR in the background, writes the new GR under the new name and
opens it.
