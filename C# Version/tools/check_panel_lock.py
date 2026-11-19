#!/usr/bin/env python3
"""Checks the locked panel files (FP / TP / RP) against panels.lock - the same rules as the app
(src/Core/PanelLock.cs): masters/FP|TP|RP/**, standards/FP|TP|RP.json and MODELS/**, SHA-256,
text files (.json .xml .txt .md) without carriage returns, Inventor leftovers skipped.

    python3 tools/check_panel_lock.py          check; exit code 1 when anything differs
    python3 tools/check_panel_lock.py --write  write panels.lock for the files as they are
"""
import hashlib, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOCK = os.path.join(ROOT, "panels.lock")
TYPES = ("FP", "TP", "RP")
TEXT = (".json", ".xml", ".txt", ".md")

def skip(name):
    n = name.lower()
    return n.endswith(".lck") or name.startswith("~") or n in ("thumbs.db", "desktop.ini")

def walk(d):
    for cur, dirs, files in os.walk(d):
        dirs[:] = [x for x in dirs if x.lower() != "oldversions"]
        for f in files:
            if not skip(f):
                yield os.path.relpath(os.path.join(cur, f), ROOT).replace(os.sep, "/")

def files():
    out = set()
    for t in TYPES:
        j = os.path.join(ROOT, "standards", t + ".json")
        if os.path.isfile(j): out.add(f"standards/{t}.json")
        m = os.path.join(ROOT, "masters", t)
        if os.path.isdir(m): out.update(walk(m))
    if os.path.isdir(os.path.join(ROOT, "MODELS")): out.update(walk(os.path.join(ROOT, "MODELS")))
    return sorted(out)

def digest(rel):
    data = open(os.path.join(ROOT, rel), "rb").read()
    if os.path.splitext(rel)[1].lower() in TEXT: data = data.replace(b"\r", b"")
    return hashlib.sha256(data).hexdigest()

def read():
    want = {}
    for line in open(LOCK, encoding="utf-8"):
        line = line.strip()
        if line and not line.startswith("#") and line[64:66] == "  ": want[line[66:]] = line[:64]
    return want

if "--write" in sys.argv:
    fs = files()
    with open(LOCK, "w", encoding="utf-8", newline="\n") as f:
        f.write("# Locked panel files (FP / TP / RP): SHA-256 and path. Written by lock-panels.cmd.\n")
        f.write("# The app blocks Apply for a type whose files differ from this list, and never writes them.\n")
        f.write("# Text files are hashed without carriage returns. Do not edit by hand.\n")
        for r in fs: f.write(f"{digest(r)}  {r}\n")
    print(f"locked {len(fs)} files -> {LOCK}")
    sys.exit(0)

if not os.path.isfile(LOCK):
    print("panels.lock is missing: the panel files are not locked. Run lock-panels.cmd (or this script with --write).")
    sys.exit(1)
want, have, bad = read(), files(), []
for rel, h in sorted(want.items()):
    if not os.path.isfile(os.path.join(ROOT, rel)): bad.append(f"missing: {rel}")
    elif digest(rel) != h: bad.append(f"changed: {rel}")
bad += [f"new: {r}" for r in have if r not in want]
if bad:
    print("Locked panel files differ from panels.lock:")
    for b in bad: print("  " + b)
    print("If this is on purpose: run lock-panels.cmd and commit panels.lock with the change.")
    sys.exit(1)
print(f"OK: {len(want)} locked panel files unchanged.")
