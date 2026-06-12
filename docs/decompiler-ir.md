# Replacement Pipeline: IR and Importer Design

The foundational design for the greenfield pipeline from [decompiler-pipeline.md](decompiler-pipeline.md). Everything else in the replacement — passes, statement tree, printers — builds on the two contracts defined here: the importer's input model and the typed IR. Both are designed for the dotnet-org audience: a Roslyn or RyuJIT engineer should recognize every choice.

## Importer contract

The old pipeline's `MethodBodyContext` mixes three roles and hides a lifetime: its `MetadataReader` points into a live `PEReader`'s memory block, and nothing in the type says so (disposing the reader under a live context is a use-after-free that crashed the test suite, not a test failure). The new contract separates the roles and makes the lifetime explicit:

- **`MethodBody`** — plain data: IL bytes, exception regions, max stack, local signature. No metadata handles, no lifetime; safe to hold forever.
- **`MetadataSource`** — `IDisposable` owner of the PE and metadata readers. Everything that resolves tokens borrows from it, and the rule is structural: *no analysis result that escapes a `MetadataSource`'s scope may hold metadata handles* — escaping results must be fully materialized (resolved `TypeRef`s, strings, byte arrays).
- **`SymbolSource`** — optional PDB access: local names, local scopes, sequence points, state-machine debug info, tuple element names. Absence lowers fidelity (per the shipping plan's levels); it never changes the shape of the API.

## Type identity

Strings end at the printers. Inside the pipeline, a type is a `TypeRef`: a structured, comparable value carrying assembly identity, definition token, and shape — generic instantiation, byref, pointer, pinned, array rank, function-pointer signature, custom modifiers. Two rules:

- **Equality is semantic**, not textual: `List<int>` from two different facades that forward to the same definition compare equal; `int*` and `int[]` never collide because one renderer happened to print them alike.
- **Rendering is a printer concern**: short names, using-directive elision, language keywords (`int` vs `Int32`) are decided where text is produced, with full information still in hand.

This is the part of the old pipeline that could not be retrofitted — `MethodBodyContext` exposes `LocalTypes` as `IReadOnlyList<string>` and every downstream consumer inherits the loss.

## The IR

A mutable tree of typed instruction nodes, in the ILSpy `ILInstruction` tradition (the right fit for rewrite-heavy raising; Roslyn-style immutability buys little when every pass mutates):

- **Parent pointers and child slots.** Every node knows its parent and its slot index; `ReplaceWith` is the primitive rewrite. This is what makes the side-channel sets of the old emitter unnecessary — "this node was consumed" is a tree edit.
- **Typed by `TypeRef` and stack type.** Every expression node carries its result type. `IsNonBooleanNumeric`-style opcode guessing — a recurring bug source in the old emitter — has no equivalent because the information is present.
- **Explicit unrepresentable nodes.** IL with no C# spelling becomes an `UnsupportedNode` carrying the raw instruction and a diagnostic, rendered honestly and counted toward the result's fidelity level.
- **Hand-written, small.** ILSpy generates a 60 KB node set from T4; our node count is far smaller and stays reviewable by hand. If it grows past that, generation is a later option, not a founding requirement.
- **`CheckInvariant` from day one.** Parent/child consistency, slot integrity, type-fullness — validated after every pass in debug builds, the discipline all three neighbor codebases share.

## Pass infrastructure

An ordered list of named passes, each one class with one job, registered in a single place — the list *is* the architecture document. The `PassContext` provides:

- **Diagnostics sink** — passes report `DEC####` diagnostics; fidelity is computed from the finished tree (any `UnsupportedNode` ⇒ at most `Partial`), not asserted by passes.
- **Dataflow facts** — dominator tree and use-def chains, computed once and invalidated on structural rewrite. Deliberately short of SSA, per the pipeline doc.
- **A stepper.** Borrowed directly from ILSpy's `Stepper` (`IL/Transforms/Stepper.cs`): passes call `context.Step("description", nearNode)` at each interesting rewrite; recorded steps form a hierarchy; and replay-with-`StepLimit` re-runs the pipeline deterministically and stops at step N — which is what makes "show me the tree right before this rewrite went wrong" a one-flag operation. ILSpy gates this behind `#if STEP` debug builds and a GUI pane; ours stays in the shipping tool, surfaced through the harness:

```bash
# today: stage boundaries          # with the new pipeline: every pass, every step
decompiler-harness --dump 'T::M'   decompiler-harness --dump 'T::M' --pipeline next --steps
```

The harness's diff mode then compares `current` and `next` not just on final output but layer by layer — JitDump plus asmdiffs in one tool.

## Projections

Per the stage-projection principle: every boundary prints. The importer output projects as raw IL (must byte-match the existing IL view — that is a harness-checked invariant, not an aspiration), the typed tree projects as annotated IL, the raised tree as pseudo-C#-over-IL, the statement tree as C#. One projection function per stage, no parallel emitters to drift apart.

## Vertical slice and parity

The first milestone is one method flowing importer → typed IR → minimal passes → statement tree → printer, validated three ways: the corpus probe, `--dump` stage comparison against `current`, and the harness diff. The slice grows by corpus difficulty (straight-line methods, then branches, then loops, then exception regions) — the same gradient the h2h corpus already encodes. The old emitter freezes for feature work when the slice is meaningful (per the shipping plan), and the parity gate is unchanged: exact-or-better on the graded corpus, no untriaged BCL-sweep regressions, all suites green through `next`.

## Rosetta

| Concept | This design | Roslyn | RyuJIT | ILSpy |
| --- | --- | --- | --- | --- |
| Input split | `MethodBody` / `MetadataSource` / `SymbolSource` | syntax/references/PDB | IL + JIT interface | `PEFile` + `IAssemblyResolver` + `DebugInfo` |
| Type identity | `TypeRef` (token + shape) | `ITypeSymbol` | `CORINFO_CLASS_HANDLE` + sig | `IType` |
| Node base | parent + slots + `ReplaceWith` | `BoundNode` (immutable) | `GenTree` | `ILInstruction` |
| Unrepresentable | `UnsupportedNode` + diagnostic | — | `BADCODE` | error expressions |
| Step recording | `PassContext.Step` + replay-to-limit | — | `JitDump` phases | `Stepper` + DebugSteps pane |
| Validation | `CheckInvariant` per pass (debug) | assert culture | per-phase asserts | `CheckInvariant(ILPhase)` |

## Open questions (for review)

1. **Namespace and naming.** Proposal: namespace `DotnetInspector.Decompiler.Pipeline` for the new code during coexistence, with ILSpy-aligned node vocabulary (`ILFunction`, `Block`, instruction-kind names) so the recognizability goal extends to identifiers. The alternative — fresh names to avoid any confusion with the legacy `ILAst*` types in the same assembly — trades familiarity for separation.
2. **Importer reuse.** The CFG and stack-simulation *algorithms* carry over per the plan; the question is whether they run on the new IR from day one (port now, one less migration later) or the old structures feed a converter initially (faster first slice, temporary glue). Proposal: port now — the algorithms are small and the converter would be demolition-scheduled code.
3. **`TypeRef` scope for the slice.** Full shape coverage (fnptr, modifiers, pinned) from the start, or core shapes first with `UnsupportedNode` for the exotic ones? Proposal: core first — the fidelity machinery exists precisely so coverage can grow honestly.
