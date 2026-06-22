# Readable Local Names (opt-in)

Design note for the #998 "Local naming and declaration placement" row. Scope is
deliberately narrow: an **opt-in** mode that gives synthesized, readable names to
locals that have no usable PDB source name, while leaving the **default** output
byte-identical.

## Problem

`CSharpPrinter.LocalName(index)` renders a local as its PDB source name when one
is present, usable as a C# identifier, and not already taken; otherwise it falls
back to `V_index`. With no PDB — the common case for shipped/stripped assemblies,
and the deterministic `--skip-pdb` reading path — every local is `V_0`, `V_1`, …,
which is the single largest readability gap in otherwise-structured output.

## Constraint: default output is load-bearing

`V_n` is not just cosmetic to leave alone — three things depend on it:

- **Fidelity gate / corpus snapshots** compare rendered text; any default-name
  change is a churn diff across the whole corpus.
- **`--skip-pdb`** exists precisely to give a *deterministic, symbol-independent*
  spelling for diffing; readable names are non-deterministic-looking and would
  defeat that.
- **Annotated-IL alignment** (the default member view) pairs source lines with IL
  offsets; names there should stay IL-aligned, not editorialized.

So readable names must be **opt-in** and must not touch any of those paths.

## Approach

1. A pure **name synthesizer** — `LocalNameSynthesizer` — maps a local's
   `(TypeRef type, role, ISet<string> taken)` to a readable identifier:
   - loop-counter `int`/`long` → `i`, `j`, `k`, … (role-driven);
   - by type: `string` → `text`/`str`, `bool` → `flag`, `T[]` → `items`/`array`,
     a named type `FooBar` → `fooBar`, etc.;
   - collision-resolved against `taken` (params, source-named locals, earlier
     synthesized names) with a numeric suffix.
   It never invents a name for a local that already has a usable source name.
2. `LocalName` consults the synthesizer **only when the mode is on and no usable
   source name exists**; otherwise the existing `V_index` fallback is untouched.
3. The mode is threaded as an explicit option (see the open fork below), defaulting
   **off**, so `Print`/`PrintRaised` and every gate keep today's output.

## Role evidence (no guessing)

A readable name is only as honest as the role it rests on. The synthesizer takes
*evidence already in the IR*, not heuristics over spelling:

- a local that is a `ForLoop`/`ForeachStatement` induction variable → counter name;
- a local whose only stores are of one concrete type → that type's name;
- otherwise a neutral type-based name.

When no evidence supports a readable name, it falls back to `V_index` rather than
fabricating — honest degradation, same as the rest of the pipeline.

## Open fork (needs a decision before wiring)

Where the opt-in lives:

- **A: printer option.** Add a `PrinterOptions` record (first field
  `ReadableLocalNames`) to `Print`/`PrintRaised`; the CLI maps a
  `--readable-names` flag to it. Most explicit; touches the printer signature and
  every caller.
- **B: IR pre-pass.** A pass populates `IrFunction.LocalNames` with synthesized
  names when empty, so the printer is unchanged. Cleanest printer, but it mutates
  the IR the annotation/fidelity views also read — risk of leaking into a
  non-opt-in path.
- **C: CLI-only render flag.** Thread a bool through the existing member-code
  provider to the printer without a formal options record. Smallest diff, least
  general.

## First contained slice

`LocalNameSynthesizer` as a standalone, fully-tested pure unit (type-based +
loop-counter roles, collision resolution), plus its single consumer wired through
whichever surface we pick in the fork. Default output stays byte-identical;
proven by the unchanged fidelity gate and a `--skip-pdb`-equivalent render test.
