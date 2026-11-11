# Panel rules: FP, TP, RP

Equations, the inner-panel split, and the one-time setup per master. Everything here was
derived from the parameter exports of `386x656x47` (FP), `519x1194x42` and `724x1194x42` (TP).
RP is prepared and waits for its parameter files.

Symbols: `W` × `L` × `H` = Width × Length × Height from the name, `T` = Stärke (1 mm),
`C` = Clearance (0.5 mm), `E` = HoleEdge (50 mm), `n` = holes per wall.

## 1. Types

| Type | Height | HoleZ | Name | Inner part | Hardware |
|---|---|---|---|---|---|
| FP | 47 | 22 | `FP-WxLxH` | `-In` | none |
| TP | 42 | 20 | `[project ]TP-WxLxH` | `-In`, split: `-In-2T` | 2 hinges, 2 lockable handles |
| RP | 44 | to confirm | `[project ]RP-WxLxH` | `-In` (also when split) | 2 handles |

The type is taken from the prefix in front of the size; without a known prefix, from the height.
Text in front of the prefix (`6357-3 `) is the project lead: it is kept, and part files carry it
without the type prefix (`6357-3 519x1194x42-Au`).

## 2. Size equations

| Quantity | Equation | 519×1194×42 | 724×1194×42 |
|---|---|---|---|
| Tray (`-Au`) | `W` × `L` × `H` | 519 × 1194 × 42 | 724 × 1194 × 42 |
| Inner panels | `InSplit = 1` if `W ≤ 500`, else `2` | (as built: 1) | 2 |
| Inner width | `(W − 2T − 2C) / InSplit` | 516 | 360.5 |
| Inner length | `L − 2T − 2C` | 1191 | 1191 |
| Inner flange height | `H − T` | 41 | 41 |
| Assembly clearance offsets | `C` | 0.5 | 0.5 |
| Pitch between the 2 halves | inner width (the halves touch) | – | 360.5 |

The split width (500) is in Settings. By this rule the 519 master would be split; it was built
with one inner panel, so the first Apply at 519 splits it unless the split width is raised.

## 3. Hole equations

Hole diameter 4.1, height `HoleZ` (same value in `-Au` and `-In`).

| Quantity | Equation | Example |
|---|---|---|
| Hole offset in the `-In` part | `MidOffset = E − T − C` | 48.5 |
| Length wall positions (`-Au`) | `E + i · PitchL`, `i = 0 … n − 1` | 50, 414.67, 779.33, 1144 |
| `HolePitchL` | `(L − 2E) / (n − 1)` | 1094 / 3 = 364.667 |
| Width wall, 2 holes | `E` and `W − E` | 50, 469 |
| Width wall, 4 holes | `E`, `W/2 − MidOffset`, `W/2 + MidOffset`, `W − E` | 50, 313.5, 410.5, 674 |
| `HolePitchW` (edge pair) | `W − 2E` | 624 |
| Width wall of one half (`-In`, split) | `MidOffset` from each end of the half | 48.5, 312 |
| `InHolePitchW` (split) | `(W − 2T − 2C)/2 − 2 · MidOffset` | 263.5 |

Why a split panel needs 4 holes on the width wall: each half has a hole 48.5 from both of its
ends. Seen from the tray these are at `E`, `W/2 − 48.5`, `W/2 + 48.5` and `W − E`, which is
exactly the "2 edge holes + centre pair" layout. So with a split, `HolesW = 2` is raised to 4
automatically, and 1, 6, 8 … are refused.

TP as built: each length wall has one fixed hole at each end plus a pattern, so the pattern
count is `HoleCountL − 1` and TP needs at least 2 holes per length wall.

## 4. Parameters the app writes

Inputs: `Width`, `Length`, `Height`, `HoleEdge`, `HoleZ`, `HoleDia`, `Clearance`, `HolesL`, `HolesW`.

Calculated: `HoleStartL/W`, `HolePitchL/W`, `HoleCountL/W`, `HoleOnL/W`, `HoleMidOnW`, and new:

| Parameter | Meaning |
|---|---|
| `InSplit` | 1 or 2. The `-In` width formula divides by it. |
| `InHolePitchW`, `InHoleCountW`, `InHoleMidOnW` | Width-wall pattern of the `-In` part. Equal to the tray's values when not split; for a half: pitch as above, count 2, no centre pair. |

## 5. The split script (`Update-InnerSplit` in `lib\Inventor.ps1`)

On every Apply:

1. `W ≤ split width`: every `-In` occurrence after the first is deleted.
2. The values are pushed (`InSplit` halves the part) and everything is rebuilt.
3. `W > split width` and only one `-In` occurrence:
   - the width direction of the part is found by comparing its extents with the expected half width,
   - a second occurrence of the same file is added one half width from the first, on the side of the tray's middle,
   - it is tied to the first with 3 flush constraints between the origin planes: two with offset 0, one (named `InSplit_Offset`) with the half width.
4. On later Applies the `InSplit_Offset` value follows the new half width.
5. If the master already has 2 inner panels with your own constraints, they are left as they are.
6. When saving as a new size, TP names the inner file `-In-2T` when split and `-In` when not.

If the constraints cannot be created, the second half is grounded at the right place instead and
the log says so; the app then moves it on every Apply.

## 6. One-time setup per master

Do this on copies first. Import through *Manage → Parameters → Import from XML*.

| Master document | Import |
|---|---|
| `6357-3 519x1194x42-Au.ipt` | `6357-3 519x1194x42-Au-setup.xml` |
| `6357-3 519x1194x42-In.ipt` | `6357-3 519x1194x42-In-setup.xml` |
| `6357-3 TP-519x1194x42.iam` | `6357-3 TP-519x1194x42-setup.xml` |
| `6357-3 724x1194x42-Au.ipt` | `6357-3 724x1194x42-Au-setup.xml` |
| `6357-3 724x1194x42-In-2T.ipt` | `6357-3 724x1194x42-In-2T-setup.xml` |
| `386x656x47-In.ipt` (FP, again) | `386x656x47-In-setup.xml` (adds `InSplit`) |

Every formula gives the number the model has today (the 519 hole pitch goes from the typed
364.666 to the exact 364.667). Then:

1. Check that nothing moved, and that the `-In` part halves when you set `InSplit = 2` by hand.
2. Put the master path into `"master"` in `standards\TP.json` (and `RP.json`).
3. The `-In` part must be constrained to **one wall** of the `-Au` part (offset `Clearance`), not
   to its middle, so the halved part stays against that wall. Your 0.5 offsets suggest it already is.
4. The `-In` part's origin planes must be intact (the script mates on them).
5. FP only: point the width-wall hole pattern of the `-In` part at `InHolePitchW`, `InHoleCountW`
   and suppress its centre pair with `InHoleMidOnW = 0` instead of the tray's values.

### TP model dimensions mapped by the setup files

| Part | Dimension | Formula |
|---|---|---|
| `-Au` | `d0`, `d1`, `d7` | `Width`, `Length`, `Height` |
| `-Au` | `d14`, `d15`, `d24`, `d29` | `HoleEdge` |
| `-Au` | `d13`, `d16`, `d25`, `d28` | `HoleZ` |
| `-Au` | `d17`, `d30` | `HoleDia` |
| `-Au` | `d84`, `d86` | `HoleCountL - 1 ul`, `HolePitchL` |
| `-In` | `d0` | `( Width - 2 * Stärke - 2 * Clearance ) / InSplit` |
| `-In` | `d1`, `d7` | `Length - 2 * Stärke - 2 * Clearance`, `Height - Stärke` |
| `-In` | `d27`, `d28`, `d37`, `d38` | `HoleMidOffsetW` (= `HoleEdge - Stärke - Clearance`) |
| `-In` | `d29`, `d39` | `HoleZ` |
| `-In` | `d30`, `d47` | `HoleDia` |
| `-In` | `d40`, `d42` | `HoleCountL - 1 ul`, `HolePitchL` |
| assembly | `d6`, `d7` | `Clearance` |

Not mapped, because the exports do not say what they are (all are the same in 519 and 724
unless noted, so they are treated as fixed hardware dimensions):

- `-Au` `d48`–`d81` (24, 8, 28, 19.5, 33, 16, 4, 11, 98, Ø4.1): hinge and handle cut-outs.
- `-Au` 519 `d37`–`d41` (22, 22, 78, 161, Ø9) and 724 `d91`–`d96` (48.5, 48.5, Ø9, 78, 78, 22).
- `-Au` 724 `d97`–`d100` (358, `d97`, `Stärke`, 0°).

## 7. Open points

1. What are `d37`–`d41` (519) and `d91`–`d100` (724) in the TP tray? In particular: is `d91`/`d92`
   = 48.5 the centre pair for the split, and what is 358 (it equals `(W − 8)/2` for 724)?
2. Is the 519 TP meant to stay unsplit although it is wider than 500?
3. Split file name for FP and RP: TP uses `-In-2T`, the RP example uses plain `-In`.
4. RP: parameter exports of the tray, the inner part and the assembly; `HoleZ`.
5. Is the hole count per length wall a rule? `n = ceil((L − 2E) / 400) + 1` gives 3 for 656 and
   4 for 1194, matching both masters; it could be filled in automatically.

## 8. More hinges or handles (3 or 4)

Possible, and best done with Inventor's own patterns so nothing has to be mated by script:

1. In the tray, make the cut-out for one hinge a single feature set and repeat it with a
   rectangular pattern along the length: count `HingeCount`, spacing `HingePitch`.
2. In the assembly, replace the second hinge with *Pattern Component → Associative*, picking that
   feature pattern. Inventor then adds or removes hinges with the count.
3. The same for the handles (`HandleCount`, `HandlePitch`).

The app would then write `HingeCount`, `HingePitch = (L − 2 · HingeEdge) / (HingeCount − 1)` and
the handle equivalents. Needed for that: which dimensions position the first and last hinge and
handle (see open point 1), and the spacing rule you want for 3 and 4.

## 9. Whole box (profiles, corner joints, panels)

The code is laid out for it: the rules are plain functions without Inventor, and
`Update-PanelAssembly` resizes any panel assembly without the window, so a box builder can call
it once per panel. Needed to build it:

1. The parameter export and a screenshot of the browser tree of a box assembly and of one
   section, with the constraint list of one panel, one profile and one corner joint.
2. The profile and corner-joint files: which are library parts, which are cut to length, and the
   length rule (for example profile length = box length − 2 × corner size).
3. The equations from box size to panel sizes: gaps between panel and profile, and how a side is
   divided into several panels (maximum panel width, where doors go).
4. Naming and folders for the box, its sections, profiles and panels.
5. How a panel is located in the frame (which faces or planes it is mated to), so each generated
   panel can be placed by the same three constraints.
6. Which inputs you want in the window: box W × L × H only, or a list of sections with a panel
   type per opening.
