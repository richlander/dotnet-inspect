# Implementation Diff Boundary

`ImplementationDiff` is the product-side C# + IL/body diff projection in
`ILInspector.Research`. It is the reusable implementation-diff component for
future CLI sections, ReturnToSender, harnesses, and other consumers that need one
member-centric evidence model instead of separate C# and IL renderers.

## Ownership

- `ILInspector.Decompiler` owns C# body diff production and display rows through
  `CSharpBodyDiff` and `CSharpDiffPrinter`.
- `ILInspector.Instructions` owns IL/body diff production and display rows
  through `IlBodyDiff`, `IlAssemblyDiff`, and `IlDiffPrinter`.
- `ILInspector.Research` owns the join. `ImplementationDiff` compares assemblies
  with C# and IL/body mechanisms, groups evidence by `ResearchSubjectKey`, and
  exposes typed display rows and unified lines without reformatting producer
  wording.

## Consumer contract

Use `ImplementationDiff.CompareAssemblies` or `ImplementationDiff.Compare` when
the input is a pair of assemblies or `ResearchDiffInput` values. The result is a
list of changed implementation members. Each member can carry C# evidence, IL
evidence, or both; exact members are omitted.

Use `ImplementationDiff.ToIlEvidence` when a caller already has a scoped
`IlMemberDiffResult`, such as ReturnToSender comparing one original method to a
recompiled artifact method. This preserves typed IL diff evidence and projects
the same `ResearchDiffEvidence` rows used by assembly-wide Research diffs. Exact
typed diffs produce no IL evidence rows, but callers may still retain the typed
diff in their own result model when exact proof matters.

The component is intentionally not a CLI section yet. CLI wiring should be a
separate usability change that chooses section names, verbosity, table schema,
and row limits without changing the product evidence primitive.

## Non-goals

- It does not prove semantic equivalence; IL/body rows are evidence, not a
  verifier.
- It does not own API compatibility rows. API diff remains a separate Research
  mechanism that can be joined by callers when needed.
- It does not compile source artifacts or plan closure. ReturnToSender and other
  harnesses own artifact requests and compilation.
