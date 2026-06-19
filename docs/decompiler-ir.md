# IR and Importer Design

The foundational design for the pipeline from [decompiler.md](decompiler.md). Everything else — passes, statement tree, printers — builds on the two contracts defined here: the importer's input model and the typed IR. Both are designed for the dotnet-org audience: a Roslyn or RyuJIT engineer should recognize every choice.

## Importer contract

The importer separates three roles and makes the metadata lifetime explicit, because a `MetadataReader` points into a live `PEReader`'s memory block and disposing the reader while a result still borrows from it is a use-after-free:

- **`MethodBody`** — plain data: IL bytes, exception regions, max stack, local signature. No metadata handles, no lifetime; safe to hold forever.
- **`MetadataSource`** — `IDisposable` owner of the PE and metadata readers. Everything that resolves tokens borrows from it, and the rule is structural: *no analysis result that escapes a `MetadataSource`'s scope may hold metadata handles* — escaping results must be fully materialized (resolved `TypeRef`s, strings, byte arrays).
- **`SymbolSource`** — optional PDB access: local names, local scopes, sequence points, state-machine debug info, tuple element names. Absence lowers fidelity; it never changes the shape of the API.

## Type identity

Strings end at the printers. Inside the pipeline, a type is a `TypeRef`: a structured, comparable value carrying assembly identity, definition token, and shape — generic instantiation, byref, pointer, pinned, array rank, function-pointer signature, custom modifiers. Two rules:

- **Equality is semantic**, not textual: `List<int>` from two different facades that forward to the same definition compare equal; `int*` and `int[]` never collide because one renderer happened to print them alike.
- **Rendering is a printer concern**: short names, using-directive elision, language keywords (`int` vs `Int32`) are decided where text is produced, with full information still in hand.

Structured type identity is load-bearing: the moment a type degrades to a string, every downstream consumer inherits the loss, so `TypeRef` flows intact from importer to printer.

## The IR

A mutable tree of typed instruction nodes, in the ILSpy `ILInstruction` tradition (the right fit for rewrite-heavy raising; Roslyn-style immutability buys little when every pass mutates):

- **Parent pointers and child slots.** Every node knows its parent and its slot index; `ReplaceWith` is the primitive rewrite. No side-channel "this node was consumed" sets are needed — consumption *is* a tree edit.
- **Typed by `TypeRef` and stack type.** Every expression node carries its result type, so there is no opcode-guessing (`IsNonBooleanNumeric`-style heuristics, a classic decompiler bug source) — the information is present.
- **Explicit unrepresentable nodes.** IL with no C# spelling becomes an `UnsupportedNode` carrying the raw instruction and a diagnostic, rendered honestly and counted toward the result's fidelity level.
- **Hand-written, small.** ILSpy generates a 60 KB node set from T4; our node count is far smaller and stays reviewable by hand. If it grows past that, generation is a later option, not a founding requirement.
- **`CheckInvariant` from day one.** Parent/child consistency, slot integrity, type-fullness — validated after every pass in debug builds, the discipline all three neighbor codebases share.

## Pass infrastructure

An ordered list of named passes, each one class with one job, registered in a single place — the list *is* the architecture document. The `PassContext` provides:

- **Diagnostics sink** — passes report `DEC####` diagnostics; fidelity is computed from the finished tree (any `UnsupportedNode` ⇒ at most `Partial`), not asserted by passes.
- **Dataflow facts** — dominator tree and use-def chains, computed once and invalidated on structural rewrite. Deliberately short of SSA, per the pipeline doc.
- **A stepper.** Borrowed directly from ILSpy's `Stepper` (`IL/Transforms/Stepper.cs`): passes call `context.Step("description", nearNode)` at each interesting rewrite; recorded steps form a hierarchy; and replay-with-`StepLimit` re-runs the pipeline deterministically and stops at step N — which is what makes "show me the tree right before this rewrite went wrong" a one-flag operation. ILSpy gates this behind `#if STEP` debug builds and a GUI pane; ours stays in the shipping tool. It lives in the library (`Stepper`, `PassContext`, `IrPasses.RunWithSteps`) and is surfaced through the harness:

```bash
decompiler-harness --dump 'T::M'               # every stage projection
decompiler-harness --dump 'T::M' --steps       # + per-pass step log
decompiler-harness --dump 'T::M' --step-limit N  # replay to step N
decompiler-harness --dump 'T::M' --diff        # each pass as a unified hunk over the prior stage
```

The harness's `--diff` mode shows the pipeline not just by final output but layer by layer — JitDump in one tool. `--pass-impact` inverts it to the corpus scale: for a given pass, which methods it changes.

## Projections

Per the stage-projection principle: every boundary prints. The importer output projects as raw IL (must byte-match the existing IL view — that is a harness-checked invariant, not an aspiration), the typed tree projects as annotated IL, the raised tree as pseudo-C#-over-IL, the statement tree as C#. One projection function per stage, no parallel emitters to drift apart.

## Rosetta

| Concept | This design | Roslyn | RyuJIT | ILSpy |
| --- | --- | --- | --- | --- |
| Input split | `MethodBody` / `MetadataSource` / `SymbolSource` | syntax/references/PDB | IL + JIT interface | `PEFile` + `IAssemblyResolver` + `DebugInfo` |
| Type identity | `TypeRef` (token + shape) | `ITypeSymbol` | `CORINFO_CLASS_HANDLE` + sig | `IType` |
| Node base | parent + slots + `ReplaceWith` | `BoundNode` (immutable) | `GenTree` | `ILInstruction` |
| Unrepresentable | `UnsupportedNode` + diagnostic | — | `BADCODE` | error expressions |
| Step recording | `PassContext.Step` + replay-to-limit | — | `JitDump` phases | `Stepper` + DebugSteps pane |
| Validation | `CheckInvariant` per pass (debug) | assert culture | per-phase asserts | `CheckInvariant(ILPhase)` |
