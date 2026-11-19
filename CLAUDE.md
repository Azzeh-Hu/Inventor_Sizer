# Inventor Sizer - rules for changes

## Panel files are locked (FP / TP / RP)
The panel process is finished. These files are locked in `C# Version/panels.lock` (SHA-256 per file):

- `C# Version/masters/FP/**`, `masters/TP/**`, `masters/RP/**` (Inventor masters and their parameter exports)
- `C# Version/standards/FP.json`, `TP.json`, `RP.json`
- `C# Version/MODELS/**` (hinge and handle parts the masters use)

Do not modify, move, regenerate or reformat them, and do not rewrite `panels.lock`, unless the user
explicitly asks for that specific panel change. The app blocks Apply for a type whose locked files
differ, and the GitHub check "Panel lock" fails. A change on purpose: the user runs `lock-panels.cmd`
(or `python3 "C# Version/tools/check_panel_lock.py" --write`) and commits `panels.lock` with it.

Panel code (`src/App`, `src/Core`) may change, but the behaviour for FP / TP / RP must stay the same.

## GR (unit frame) is separate
GR work stays in its own tree, so it does not touch the panel process:

- code: `C# Version/src/Gr/Core` (model, drawing) and `src/Gr/App` (Inventor, window: partial classes)
- data: `C# Version/GR/GR.json` (part rules) and `C# Version/GR/presets/` (designs)
