# Panel rules: FP, TP, RP

These are the equations, the inner-panel split, the hardware holes, and the one-time setup per
master. They were derived from the parameter exports of:

- FP: 386×656, 276×696, 521×1616 and 726×1616
- TP: 519×1194 and 724×1194
- RP: 571×996 and 546×1197

Symbols:

- `W` × `L` × `H` = Width × Length × Height from the name
- `T` = Stärke (1 mm)
- `C` = Clearance (0.5 mm)
- `E` = HoleEdge (50 mm)
- `n` = holes per wall

## 1. Types

| Type | Height | HoleZ | Name | Inner part | Hardware |
|---|---|---|---|---|---|
| FP | 47 | 22 | `[project ]FP-WxLxH` | `-In`, split: `-In-2T` | none |
| TP | 42 | 20 | `[project ]TP-WxLxH` | `-In`, split: `-In-2T` | 2 hinges, 2 lockable handles |
| RP | 44 | 20 | `[project ]RP-WxLxH` | `-In`, split: `-In-2T` | 2 handles across the middle |

How names work:

- The type comes from the prefix in front of the size. If there is no prefix, it comes from the height.
- Text in front of the prefix (`6357-3 `) is the project lead. It is kept.
- Part files carry the lead but not the type prefix, e.g. `6357-3 519x1194x42-Au`.

**`-In-2T` rule (every type):** a split inner panel's file always ends in `-In-2T`, and a
one-piece inner panel's file always ends in `-In`.

- The app renames the inner part on Apply whenever the two disagree, also when the size stays the same.
- Example: the RP 571 master's split part is called `-In`. The first Apply saves it as `-In-2T`.
- The old file stays on disk, untouched.

## 2. Size equations

| Quantity | Equation | 519×1194×42 | 724×1194×42 |
|---|---|---|---|
| Tray (`-Au`) | `W` × `L` × `H` | 519 × 1194 × 42 | 724 × 1194 × 42 |
| Inner panels | `InSplit = 1` if `W ≤ 500`, else `2` | 2 | 2 |
| Inner width | `(W − 2T − 2C) / InSplit` | 258 | 360.5 |
| Inner length | `L − 2T − 2C` | 1191 | 1191 |
| Inner flange height | `H − T` | 41 | 41 |
| Assembly clearance offsets | `C` | 0.5 | 0.5 |

The 519 master was built with one inner panel by mistake (as you confirmed). The first Apply at
519 splits it.

## 3. Hole equations

Hole diameter 4.1, height `HoleZ`.

| Quantity | Equation | Example |
|---|---|---|
| Hole offset in the `-In` part | `MidOffset = E − T − C` | 48.5 |
| Holes per length wall (automatic) | `n = ceil((L − 2E) / 400) + 1` | 1194 → 4 |
| Holes per width wall (automatic) | the same; an odd count above 2 becomes even; split → 4 | 724 → 4 |
| Length-wall positions | `E + i · (L − 2E) / (n − 1)` | 50, 414.67, 779.33, 1144 |
| Width wall, 2 holes | `E`, `W − E` | 50, 469 |
| Width wall, 4 holes | `E`, `W/2 − 48.5`, `W/2 + 48.5`, `W − E` | 50, 313.5, 410.5, 674 |
| `-In` half, width wall | 48.5 from both ends of the half; pitch `(W − 2T − 2C)/2 − 97` | 263.5 |

### Checking the automatic count against your masters

The 400 mm maximum gap is a setting; 0 = enter the counts by hand.

| Master | The formula gives (L / W) | Master has | |
|---|---|---|---|
| FP 386×656 | 3 / 2 | 3 / 2 | ✓ |
| FP 726×1616 | 5 / 4 | 5 (pitch 379) / centre pair | ✓ |
| FP 521×1616 | 5 / 4 | 4 (pitch 505) / no centre pair | ✗ manual |
| TP 519×1194 | 4 / 4 | 4 (pitch 364.67) / 2 | ✓ / ✗ manual (not split) |
| TP 724×1194 | 4 / 4 | 4 / centre pair `d91`, `d92` | ✓ |
| RP 546×1197 | 4 / 4 | 4 (pitch 365.67) / 2 | ✓ / ✗ (not split) |
| RP 571×996 | 4 / 4 | 2 (no pattern) / centre pair `d96` | ✗ / ✓ |

The ✗ rows are the same kind of hand slip as the 519. Another one:

- The RP 546 `-In` part's pattern spacing was typed as 366.666 instead of 365.667.
- So its holes do not line up with the tray's.

Patterns as built:

- **TP and RP trays:** a fixed hole 50 from each end of the length wall, plus a pattern of `HoleCountL − 1` holes at `HolePitchL`.
  - So these types need at least 2 holes per length wall.
- **FP and RP 546:** the pattern holds all `HoleCountL` holes.

## 4. What the parameters in the TP and RP trays are

| Part | Parameter | Value | Meaning |
|---|---|---|---|
| TP 724 `-Au` | `d91`, `d92` | 48.5 | Centre-pair holes on the width wall. Now mapped to `HoleMidOffsetW`. |
| RP 571 `-Au` | `d96`, `d97 = d96` | 48.5 | The same centre pair. Mapped. |
| RP `-Au` | `d84` / `d142` = 117, `d85` / `d143` = Ø9 | | One handle: 2 Ø9 holes 117 apart. |
| RP `-Au` | `d94` / `d167` = 58.5 | | Handle holes at `W/2 ± 58.5`, centred on the seam between the 2 halves. |
| RP `-Au` | `d95` = 225 (996 long), `d166` = 300 (1197 long) | | Handle distance from the panel end. No single rule fits both. |
| TP `-Au` | `d48`–`d81` | same in 519 and 724 | Hinge cut-outs: fixed hardware geometry. |
| TP `-Au` | `d37`–`d41` (519) and `d93`–`d100` (724) | 78, 22, Ø9, 161 / 358 | Handle and lock holes. |

The last row cannot be settled from numbers alone: a `d` parameter does not say which direction
it runs. The **Learn hardware holes** button (section 6) reads the real hole positions from
Inventor instead, so this no longer blocks the drawing.

## 5. The split script (`Update-InnerSplit` in `lib\Inventor.ps1`)

On every Apply:

1. When `W ≤ split width`, every inner panel after the first is deleted.
2. The values are pushed (`InSplit` halves the part) and everything is rebuilt.
3. When `W > split width` and there is only one inner panel, a second copy is added:
   - It is placed one half width from the first, towards the middle of the tray.
   - It is tied to the first with 3 flush constraints on the origin planes. The one with the
     half-width offset is named `InSplit_Offset`.
4. On later Applies, `InSplit_Offset` follows the new half width.
5. When the master already has 2 inner panels with its own constraints, they are left alone.
   - Example: TP 724 has each half held against its own tray wall (`d26`, `d27`).
   - That works at any width: both halves shrink and still meet in the middle.
6. The inner part's file name follows the `-In` / `-In-2T` rule from section 1.

## 6. Hinge and handle holes in the drawing

The drawing shows the door hardware holes in orange.

- A solid circle is a hole on the visible side.
- A dashed circle is a hole on the far side.
- A red circle is a hole that falls outside the panel at the current size.

The holes are learned once per type from its master:

1. Open the master (for example `6357-3 TP-724x1194x42.iam`) and select its type.
2. Click **Learn hardware holes**. The app reads every drilled hole of the `-Au` and `-In` parts.
3. The holes the hole rules make (Ø4.1 at `HoleZ` in the walls) are left out. The rest is saved
   to the `hardware` section of `standards\TP.json`.
4. Each hole is stored by its distance from the nearest panel edge, or from the middle of the
   panel when it is near the middle. So hinges stay at the same distance from the ends, and the
   RP handles stay centred, at every size.

The JSON file is plain text: `from` is `start`, `end` or `mid`. If a hole is measured from the
wrong edge, change `from` and `value` there.

Please send me `TP.json` and `RP.json` from `standards\` after learning. With the
real positions I can name `d37`–`d41` / `d91`–`d100` exactly and map them to parameters.

## 7. One-time setup per master

The setup lives in each type's file (`standards\<type>.json`, section `setup`). **Set up
master...** applies it to the open master: user parameters first, then the formulas.

| Type | Master the d-numbers belong to |
|---|---|
| FP | `FP-386x656x47` (`-In` gets `InSplit`) |
| TP | `6357-3 TP-724x1194x42` (centre pair `d91`/`d92`; hinge and handle formulas, section 13) |
| RP | `6357-3 RP-571x996x44` (centre pair `d96`) |

Then:

1. Put the master paths into `"master"` in `standards\TP.json` and `RP.json`.
2. **TP:** give the centre-pair holes their own feature, with *Suppress if* `HoleMidOnW = 0`.
3. **RP 571:** add a length-wall hole pattern like TP's: count `HoleCountL - 1 ul`, spacing `HolePitchL`.
4. **FP:** point the `-In` part's width-wall hole pattern at `InHolePitchW` and `InHoleCountW`.
   Suppress its centre pair with `InHoleMidOnW = 0`.

Sheet thickness in Settings is one sheet (Stärke = 1).

## 8. More hinges or handles (3 or 4)

The plan is unchanged:

1. In the tray, repeat the hinge cut-out with a rectangular pattern: count `HingeCount`, spacing `HingePitch`.
2. In the assembly, replace the second hinge with *Pattern Component → Associative* on that
   pattern. Inventor then adds or removes hinges with the count.
3. Do the same for the handles.

The app would write `HingePitch = (L − 2 · HingeEdge) / (HingeCount − 1)`. The learned hole
template (section 6) gives `HingeEdge` and the hole layout of one hinge, so after the hardware
files arrive this can be added to the rules and the drawing.

## 9. Whole box: what the GR-Box export shows, and what is still needed

`6357 2035-N1-ZK750-GR-params.xml` holds 662 parameters, all assembly constraint offsets:

| Count | Value | Probably |
|---|---|---|
| 546 | 0 | flush / mate with no offset |
| 85 | 2 mm | gap between panel and profile |
| 13 | −25 mm | profile set-back at the corners |
| 8 | 3 mm | |
| 4 + 3 + 1 + 1 | 280, 450, 525, 730 | positions of sections or parts |

A parameter export has no component names and does not say which faces a constraint joins. It
cannot describe the box. That is what **Export assembly tree...** is for (section 10).

### What is needed to mate profiles, corner joints and panels freely, in one box or many

1. **Named mating geometry in every library part.** Use the iLogic *Names* tab you showed:
   right-click a face or work plane → *Name entity*.
   - **Profile:** `End_A`, `End_B` (end faces), `Panel_Seat` (face the panel lies on), `Axis`.
   - **Corner joint:** `Leg_X`, `Leg_Y`, `Leg_Z` (faces a profile end sits on), `Panel_Seat_*`.
   - **Panel assembly:** `Back` (outside face), `Edge_W0`, `Edge_WL`, `Edge_L0`, `Edge_LL`.
   - With names, a constraint is "profile `End_A` flush to corner `Leg_X`", the same for every
     size. The export reports these names, so I can check them.
2. **One skeleton per box.** A part with work planes at the box's outer faces and at each
   section split (the 280 / 450 / 525 / 730 values).
   - Profiles, corners and panels are mated to the skeleton's planes, not to each other.
   - Moving a plane resizes the box. Several boxes are several skeletons in one top assembly,
     each placed by 3 offsets.
3. **One profile part with a length parameter.**
   - Either iPart or model-state rows, or `Length` as a parameter and Save As per length (like the panels).
   - The export's `-parts.csv` groups files by family (`PF-#.ipt`), so it shows which way you use today.
4. **The rules, from you or from the exports:**
   - profile length = section size − 2 × corner size? (the −25 mm offsets suggest the corners set the profiles back by 25)
   - panel size = opening − 2 × 2 mm gap?
   - maximum panel width before a side is divided
   - where doors (TP) and handle panels (RP) go
5. **The box input.** For example, sections along the length (`750, 450, 1200`) with a panel
   type per opening (front: TP, top: FP, …). The app turns that into skeleton planes, profile
   lengths and panel names, then runs the panel resize for each panel.

## 10. Exporting the component tree

Removed. Your iLogic deep export covers this (sections 11 and 12), and the GR view reads
assemblies straight from Inventor.

## 11. GR findings from the iLogic exports (8185 GR, 6357 2035-N1-ZK750-GR)

This answers most of section 9. The box is built the same way with or without panels:

| Element | How it is built | In the data |
|---|---|---|
| **Frame** | a cuboid of 8 corners `APS Ecke Guß` and 12 profiles `P150 <len>mm`, 4 of each of its 3 lengths | 8185: 2 frames; 6357: 5 frames |
| **Profile to corner** | Mate + 2 × Flush, offset 0 | 82 Flush + 42 Mate (8185) |
| **Frame to frame** | corner to corner, Flush (3 per pair, plus Mates) | 6357 chain F1–F2–F3–F4–F5 |
| **Divider** | `P350 <len>mm` (some `gefräst`) on `Joint` parts, Mate + Flush to the joints; positioned by offsets from a P150 (280, 450, 500, 525, 600, 660, 700, 730 mm) | 32 (8185), 34 (6357) |
| **Profile set-back** | P150 to P350 offset −25 mm | 13× |
| **Panel** | Mate to the profiles of one frame with a 2 mm gap (3 mm for some), Flush to its neighbours | 47 panels in 6357 |
| **Damper / duct stub** | Mate to the profiles; damper centred by `<size> / 2 ul` offsets | 8185: 2 dampers, 4 stubs |

Equations that hold across both units:

- Frame outer size = profile length + 36 (2 corners).
- Panel length = profile length + 36 (FP and RP). Examples: 1580 → 1616, 1160 → 1196, 860 → 896, 410 → 446, 360 → 396.
- TP doors are 2 mm shorter: 1160 → 1194.
- Unit width = the profile length all frames share (6357: 1580; 8185: 1330).
- Unit height = the next most shared one (1160; 1220).
- Neighbouring frames along the unit share width and height. Their outer lengths add up: 6357 = 396 + 896 + 1876 + 996 + 396 = 4560.

### GR view in the app

**Show: GR (box)** draws a GR assembly from these exports (**Load GR export...**, choose the tree
and the constraints file) or straight from Inventor (**Read GR from Inventor**, also automatic
when the active assembly's name ends in `GR`). The drawing shows:

- each frame as a profile wireframe with its corners and number
- the frame lengths and the overall size
- a frame table: profile sizes, plus the panels, dividers and parts on each frame
- the cut list: P150 and P350 lengths × count, corners, joints, panels by type and size, dampers, stubs, base frame

### Still unknown from these exports

The exports have no positions and no faces, so two things are inferred:

- **The layout.** The orientation and order come from the shared sizes and the frame links.
- **Which face a panel or divider sits on.** This is not shown; panels and dividers are listed per frame.

To place panels and dividers exactly, add to the iLogic tree export, per occurrence:

- `oOcc.Transformation.Translation` (X, Y, Z in cm)
- the 3 axis directions (`Transformation.Cell(1..3, 1..3)`)

With them, the GR view could draw every part where it really is.

### Editing a GR in the app, and what writing it back to Inventor needs

The GR editor changes frame sizes and adds or removes frames in the model. It also updates the
profiles, dividers and panel sizes that depend on them.

To push an edited GR into Inventor, each changed length needs its own profile file, made the
same way as the panels:

1. Open the profile part of that series, e.g. `P150 1580mm.ipt`.
2. Set its length parameter.
3. Save it as `P150 1700mm.ipt`.
4. Replace the profile occurrences in the GR with the new file. The constraints stay.

Panels are resized with the existing panel tool (Save As per size). Needed from you:

- the name of the length parameter in a `P150` and a `P350` profile part, e.g. `d0` or `Länge`;
  a parameter export of one of each is enough
- the folder where profile files live, and whether new lengths may be created there

## 12. Findings from the deep export (Parts, Parameters, Features, Sketches)

**Profiles and corners**

| Part | Size (mm) | Length parameter | Notes |
|---|---|---|---|
| `P150 <len>mm` (frame profile) | 70 × 70 | `d89` (extrusion) | same in every P150 file |
| `P350 <len>mm` (divider) | 90 × 49.7 | `d0` | `gefräst`: 4 milled pockets, 20 mm offsets |
| `APS Ecke Guß` (corner) | 130 × 130 × 130 | none (imported solid) | |
| `Joint` | 50 × 48 × 60 | none (imported solid) | |

**Frame outer size = profile length + 2 × 130.** Each profile is mated to a corner face and flush
with its outer faces (Mate + 2 × Flush, offset 0), so each corner adds 130 mm. This replaces
the earlier "+ 36" guess, which is only the panel overlap: panel length = profile + 36.
Example: 6357 is about 5680 × 1840 × 1420 mm outside.

**Panels in the GR confirm the panel rules**

- `-In` = `-Au` − 3 in width and length, and `H − 1` in height, for FP, RP and TP. This means
  Stärke 1 mm per sheet: the app's "Sheet thickness" setting is one sheet, so set it to 1.
  - The −3 is 2 × 1 mm sheet + 2 × 0.5 mm clearance.
  - With 2, the app computes `-In` = `-Au` − 5. That is why your screenshot showed 439 instead of 441.
- Split inner panels: (W − 3) / 2. Examples: 521 → 259, 726 → 361.5, 724 → 360.5. The RP 521
  inner part is split but named `-In`; the `-In-2T` rule renames it on the next Apply.
- Data slips seen: `726x1196x44-Au` and `396x1196x44-Au` are 1197 long, but their names say 1196.

**Hardware in the GR**

- TP hinges: `Scharnier Teil1–4`, 8 of each = 4 doors × 2 hinges.
- TP lockable handles: `MFG140-035`, 8 = 4 doors × 2.
- RP handles: `MFG1000000` (139.8 long; its 2 holes are the 117 mm pair).

### Writing an edited GR back to Inventor

**Write profiles to Inventor** (GR editor) changes the GR that is open in Inventor:

1. For every new profile length, it opens the profile part of that series.
2. It sets `d89` (P150) or `d0` (P350), finding the parameter by its value if the name differs.
3. It saves a copy next to the original as `P150 <new>mm.ipt`. The original part keeps its
   length. An existing file of that name is used as it is.
4. It replaces those occurrences with the new file, so their constraints stay.
5. It leaves the GR unsaved, so you can check it before saving.

The parameter names are in `standards\GR.json` (`lengthParameters`).

Not written yet, and reported in the log instead:

- **Panels that need a new size.** Make them with the panel tool, then replace them.
- **Added or removed frames.** Writing is refused while the design has them. Placing 8 corners
  and 12 profiles with their constraints is the next step; it needs one frame's constraint list
  with the faces named (iLogic Names).

## 13. TP complete: hinges and handles (from the TP 724×1194 deep export)

All positions are on the tray's face. The sketch origin is the panel centre.

| Hardware | Holes | Position | Parameters |
|---|---|---|---|
| Hinge (one per end, mirrored) | Ø4.1 × 4: (8, 98), (8, 122), (36, 98), (36, 122) | (from the hinge edge, from the panel end) | Hole4: `d81` 98, `d48` 24, `d50`/`d51` 8, `d55` 28 |
| | Ø11 × 4: (19.5, 98), (19.5, 122), (52.5, 102), (52.5, 118) | | Hole5: `d66`/`d67` 19.5, `d68` 24, `d70` 33, `d71` 16, `d73` 4 |
| Lockable handle (one per end, mirrored) | Ø9 × 2 at 161 and 239 from the end | 22 from the handle edge (the opposite long edge) | Cut1: `d102` 161, `d95` 78, `d104` 22, `d93` Ø9 |

**The 2 spacings that were typed numbers are now formulas** (setup file
`6357-3 724x1194x42-Au-setup.xml`):

- `d114` = `d121` = `Length - 2 * d81 - d48` (974 at 1194). It puts the second hinge at L − 122.
- `d107` = `Length - 2 * d102 - d95` (794 at 1194). It puts the second handle at L − 239.
- New user parameters: `HingeEdge` = 98 (drives `d81`) and `HandleEdge` = 161 (drives `d102`).

The drawing uses the `hardware` section of `standards\TP.json`, built from these numbers. Its 6 sketch circles
match the master exactly.

**Local test** with the TP sizes of the 6357 GR, plus 444×654:

| Size | Wall holes L / W | Inner panel | Hardware |
|---|---|---|---|
| 724×1194 | 4 / 4 (centre pair) | 2 × 360.5 (`-In-2T`) | ok |
| 519×1194 | 4 / 4 (centre pair) | 2 × 258 (`-In-2T`) | ok |
| 394×1194 | 4 / 2 | 1 × 391 (`-In`) | ok |
| 519×569 | 3 / 4 (centre pair) | 2 × 258 (`-In-2T`) | ok |
| 444×654 | 3 / 2 | 1 × 441 (`-In`) | ok |

The shortest TP without hardware collisions is 487 mm. Below that, the two handles' inner holes
overlap; the drawing shows them red, with a note.

**One change needed in the TP master:**

- In the tray, Hole1 holds both the width-wall edge holes (`d14`/`d15`) and the centre pair (`d91`/`d92`).
- Narrow TPs (394, 444: 2 width-wall holes) need the centre pair off, which only works if it is its own feature.
- Split the centre pair into its own hole feature, with *Suppress if* `HoleMidOnW = 0`.

## 14. TP: 3 or 4 hinges or handles (linear patterns)

The app writes `HingeCount`, `HingePitch`, `HandleCount` and `HandlePitch`. Example at 1194 long:

| Count | Hinge pitch | Hinge screw columns | Handle pitch | Handle holes |
|---|---|---|---|---|
| 2 | 974 | 98/122, 1072/1096 | 794 | 161/239, 955/1033 |
| 3 | 487 | + 585/609 | 397 | + 558/636 |
| 4 | 324.667 | + 422.7/446.7, 747.3/771.3 | 264.667 | evenly between |

To use them, change the TP master once:

1. **Tray `-Au`:**
   - Replace the second hinge's sketch points (`d114`, `d121`) with a rectangular pattern of
     Hole4 + Hole5 along the length: count `HingeCount`, spacing `HingePitch`.
   - Do the same for Cut1 (handles): count `HandleCount`, spacing `HandlePitch`.
2. **Assembly:**
   - Keep one hinge (Scharnier Teil1–4) and one handle (`MFG140-035`) constrained to the first set of holes.
   - Delete the second ones.
   - Add *Pattern Component → Associative* on the tray patterns. Inventor then adds or removes
     hinges and handles with the count.
3. Run **Set up master...** again if `HingeCount` / `HandleCount` / the pitches are not in the master yet.

The hole sets are symmetric about their own middle. So a pattern (a move) of the first set
lands exactly on the mirrored last set, which the app checks: with count 2 the pitches equal
the master's 974 and 794.
