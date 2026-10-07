/-
Checks the axioms of one built Lean model for eng/run-lean-checks.sh.

Usage, from a model directory after `lake build`:
  lake env lean --run <path>/CheckAxioms.lean Module.Name...

Fails when a listed module declares an axiom, or when any constant a listed
module defines depends on an axiom other than `propext`, `Classical.choice`,
and `Quot.sound`. Checking the elaborated environment, rather than source text,
covers every declaration spelling, including attributes, modifiers, and
docstrings.
-/
import Lean

open Lean

def standardAxioms : List Name := [``propext, ``Classical.choice, ``Quot.sound]

def main (args : List String) : IO UInt32 := do
  if args.isEmpty then
    IO.eprintln "CheckAxioms: no modules were given."
    return 2
  initSearchPath (← findSysroot)
  let modules := args.map String.toName
  let env ← importModules (modules.map ({ module := · })).toArray {} 0
  let context : Core.Context := { fileName := "<CheckAxioms>", fileMap := default }
  let mut failures := 0
  let mut checked := 0
  for (name, info) in env.constants.toList do
    let some idx := env.getModuleIdxFor? name | continue
    unless modules.contains env.header.moduleNames[idx.toNat]! do continue
    checked := checked + 1
    if info matches .axiomInfo _ then
      IO.eprintln s!"declares an axiom: {name}"
      failures := failures + 1
      continue
    let (axioms, _) ← (collectAxioms name : CoreM (Array Name)).toIO context { env }
    let extra := axioms.toList.filter (fun a => !standardAxioms.contains a)
    unless extra.isEmpty do
      IO.eprintln s!"depends on a non-standard axiom: {name} uses {extra}"
      failures := failures + 1
  if checked == 0 then
    IO.eprintln "CheckAxioms: the listed modules define no constants."
    return 2
  IO.println s!"CheckAxioms: {checked} constants use only standard axioms."
  return (if failures == 0 then 0 else 1)
