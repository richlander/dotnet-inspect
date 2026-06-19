# Decompiler Inspection & Oracle

Status: draft / direction-setting.

This spec unifies two facilities that grew up apart — the single-method
**inspection** path (`--dump`, stage dumps) and the corpus-wide **oracle**
(compile-back) — into one pipeline viewed at increasing zoom, and decides
**what ships in the product vs. what stays a developer/CI tool**.

It supersedes the ad-hoc split documented informally in the working-notes gist
and consolidates the in-flight work on the `dump-stages` branch (`StageDump`,
`Stepper`, per-pass IR capture) with the harness oracle (`FidelityCheck`,
`ValidityCheck`).

## Motivation

### The three-renderer divergence (the bug this fixes)

The decompiler has three places that turn IL into C#, and they do not agree:

| Caller | Renderer | Audience |
| --- | --- | --- |
| Product output (`dotnet-inspect ... code`), all coverage sweeps | `CSharpPrinter.PrintRaised` | end users, the oracle |
| `--dump` (default `DumpStages`) | legacy `CSharpEmitter.Decompile` | nobody (legacy) |
| `--dump` (`Dump` / `StageDump.DumpMethod`) | `CSharpPrinter.Print` (lowered, "structure not yet raised") | maintainers |

Consequence: **the one built-in single-method inspector renders a different
artifact than the numbers measure.** During the StaleFieldRead investigation
(#605/#618) the legacy `--dump` printed the *correct* `int v = h.Value; ...`
while the product path emitted the buggy `h.Value + h.Value`. The inspector lied
about a defect the oracle had caught, and the only way to see the real product
output was a throwaway file-based probe calling `PrintRaised` directly.

Inspection and measurement must share one renderer, or inspection cannot be
trusted to debug what measurement reports.

### One funnel, two zoom levels

Compile-back and dump are the **same pipeline** at different depths:

```text
Import → passes → PrintRaised → recompile → disassemble → compare
└──────────── inspection ───────────┘└────────── oracle ──────────┘
```

- **dump** = the single-method explainer of the inspection half.
- **compile-back** = the corpus-wide aggregate over the oracle half.

They share the entire front half. The fix is to make them literally share it:
one projection library, two front ends, both terminating on `PrintRaised`.

## Scope decision: what ships, what doesn't

This is the central architectural question. The deciding constraint is
**Roslyn**: the oracle must *recompile* C#, which pulls in
`Microsoft.CodeAnalysis.CSharp` (tens of MB, slow startup). The product ships as
a Native AOT single-file executable with a network-free response budget under
0.5s. **Roslyn cannot ship in the product.** Therefore the recompile/compare
half is inherently a developer/CI tool, and the inspection half — which is pure
decompiler library with no Roslyn — is the only part eligible to ship.

That single constraint cuts the layering cleanly:

### Layer 0 — `ILInspector.Decompiler` (library, ships, the source of truth)

Pure projection of the decompiler's own stages. No Roslyn, no test deps.

- `IrImporter.Import` → passes → `CSharpPrinter.PrintRaised` (the product C#).
- `StageDump` / per-pass IR capture (`IrPasses.RunWithStages`).
- `Stepper` (ILSpy-style fine-grained rewrite trace + replay-to-step-limit).
- `IlProjection` (raw / typed / structured IL views).

Everything that *renders* lives here so both front ends are byte-identical.
**Single source of truth for every projection.**

### Layer 1 — `dotnet-inspect` (product, ships)

Surfaces the Layer-0 inspection projections as code sections / views: the raised
C#, and — progressively disclosed — the IR tree, per-pass stages, the step log,
and the IL views. No Roslyn. This is the **end-user / agent inspection funnel**:
"show me the C#, and if it looks wrong, show me how the pipeline got there."

What the product deliberately does **not** include: the recompile-and-compare
oracle. The product can show *what* it would produce; it cannot prove the output
recompiles to the same IL without Roslyn.

### Layer 2 — oracle library (developer/CI only, depends on Roslyn)

Factor today's harness oracle out of the `tools/DecompilerHarness` grab-bag into
a proper, test-consumable library (working name `ILInspector.Decompiler.Oracle`).
It already *is* consumed as an API by the xunit gate (`FidelityCheck.Evaluate`);
making that a real library boundary, not a file reach-in, is the cleanup.

Contains the Roslyn-dependent machinery:

- `FidelityCheck` (recompile in a reconstructed type skeleton, compare opcode
  streams) and `ValidityCheck` (parse + bind validity).
- The **aligned opcode diff** and **root-cause classifier** (see Roadmap).
- Corpus sweep + **regression baselines** (per-method exact/defect snapshots).

Consumed by both the harness CLI **and** the xunit gate. Renders C#/IR via
Layer 0, so the oracle's "explain" view and the product's inspection view are
the same projection.

### Layer 3 — `DecompilerHarness` (developer CLI, not shipped)

A thin front end over Layer 2 + Layer 0. Adds the single-method `--explain`
drill-down (the oracle analog of `--dump`) and the batch sweep modes.

### Rejected: a third standalone tool

A separate end-user-facing decompiler-verification tool was considered and
rejected. The oracle's only audiences are decompiler maintainers, CI gates, and
the agents driving quality — all served by the harness CLI and the xunit gate.
A third distributable adds packaging and maintenance with no audience the
harness does not already reach. If CI wants a narrower entry point, it is a
harness *mode*, not a new product.

### The boundary in one line

> Anything that only **projects** the decompiler ships (Layer 0/1). Anything that
> **recompiles** to judge the decompiler does not (Layer 2/3). They meet at one
> renderer.

## The unified single-method funnel

Both front ends present the same staged funnel; the harness simply has more
stages because it has Roslyn.

Product (`dotnet-inspect`, no oracle):

```text
IL (raw / typed / structured)
  → IR (after import; after each pass)            [progressive disclosure]
  → C# (raised — the shipped product)              ← PrintRaised, the default
```

Harness (`--explain Type::Method`, adds the oracle back half):

```text
… everything above …
  → recompiled IL
  → aligned opcode diff (original vs recompiled)
  → classification (branch-polarity / extra-temp / missing-cast / …)
```

Hard requirement: **the C# stage in both is `PrintRaised`.** The legacy
`CSharpEmitter` dump stage is removed (or explicitly quarantined as
non-product). `StageDump.DumpMethod` is updated to terminate on `PrintRaised`,
not `Print`, and its label corrected ("raised — the shipped product").

## Roadmap (independent, shippable slices)

Ordered by leverage. Each is a self-contained PR with before/after numbers.

1. **Terminate inspection on `PrintRaised`.** Make `StageDump` end on the
   product renderer; remove/quarantine the legacy emitter dump stage. Closes the
   divergence. (Layer 0/1; small.)

2. **`--diff-opcodes <baseline>` / `--emit-opcodes <file>` for compile-back.**
   Per-method `status + canonical stream` snapshot; the diff reports both
   regressions (exact→diff/fail) and wins. Mechanizes the manual
   stash-compare-to-baseline "zero regression" proof. Mirrors compile-check's
   existing `--emit-defects` / `--diff-defects`. (Layer 2; small.)

3. **Aligned first-divergence diff.** LCS-align the two opcode streams; show the
   divergence index with ±context instead of two flat lines. Shared rendering
   used by both the batch report and `--explain`. (Layer 2; medium.)

4. **Root-cause classifier.** Tag each Full diff and each compile-check defect by
   *root cause*, not compiler error code (e.g. CS0030/CS0266/CS0029 →
   "missing explicit cast"; `cgt`/`clt` on bool → "bool comparison render").
   Emit a deduplicated, prioritized histogram. Turns the error tally into a
   roadmap with "methods fixed per slice" estimates. (Layer 2; medium.)

5. **`--explain Type::Method` drill-down.** The oracle's single-method view: the
   full funnel for one method in one command (today 3–4 invocations). Built on
   1, 3, 4. (Layer 3; medium.)

6. **Skeleton caching for scale.** The module skeleton is rebuilt per target
   method (O(methods × moduleSize)) — why CoreLib compile-back is impractical.
   Build it once with a placeholder body, splice per method. Unlocks BCL-wide
   compile-back gating. (Layer 2; medium-high.)

7. **Representative corpus + CI trend gate.** Promote the top real-world shapes
   (pinned/`fixed`, RVA `<PrivateImplementationDetails>` spans, inline arrays,
   bool comparisons) into the compile-back fixture set; add a `--compile-check`
   trend gate over a pinned BCL assembly that fails on any new malformed method
   or per-root-cause regression. (Layers 2/CI; medium.)

8. **Tackle the malformed set first.** ~1,200 syntactically malformed CoreLib
   methods contribute *zero* agreement signal (a parse error aborts the whole
   compare). Fixing the top syntactic drivers unblocks measurement on a large
   slice. Sequenced after 4 (the classifier prioritizes which). (Layer 0; varies.)

## Relationship to the `dump-stages` branch

The `dump-stages` branch already builds the Layer-0 inspection front half:
`StageDump`, `Stepper`, `IrPasses.RunWithStages`, the `--steps` / `--step-limit`
CLI, and product section/view integration. This spec **adopts** that work and
adds two things it does not yet do:

1. Terminate on `PrintRaised` (it currently ends on `Print`/lowered) — roadmap 1.
2. Connect the inspection funnel to the oracle back half — roadmap 5.

The branch should rebase onto current `main` (it predates #618/#619) and land as
the Layer-0/1 foundation; the oracle slices (2–8) build on top.

## Open questions

- **Oracle library name + project placement** — `ILInspector.Decompiler.Oracle`
  under `src/` (test-consumable) vs. staying under `tools/`. Test consumption
  argues for `src/`.
- **How much pipeline depth the product exposes by default** — raised C# is the
  default; IR/stages/steps are opt-in. Where exactly on the verbosity ladder
  each stage sits is a section-model question (see `section-pipeline.md`).
- **Classifier taxonomy** — the initial root-cause buckets and how they map to
  the existing compile-check defect histogram.
