# Implementation Diff Boundary

`ImplementationDiff` is the product-side C# + IL/body diff projection in
`ILInspector.Research`. It is the reusable implementation-diff component for
future CLI sections, ReturnToSender, harnesses, and other consumers that need one
member-centric change model instead of separate C# and IL renderers.

## Ownership

- `ILInspector.Decompiler` owns C# body diff production and display rows through
  `CSharpBodyDiff` and `CSharpDiffPrinter`.
- `ILInspector.Instructions` owns IL/body diff production and display rows
  through `IlBodyDiff`, `IlAssemblyDiff`, and `IlDiffPrinter`.
- `ILInspector.Research` owns the join. `ImplementationDiff` compares assemblies
  with C# and IL/body mechanisms, groups changes by `ResearchSubjectKey`, and
  exposes typed display rows and unified lines without reformatting producer
  wording.

## Research comparison model

`ResearchDiff` is the operation facade. It returns one `ResearchComparison`
containing a flat `Changes` collection. `BySubject()` computes member- and
type-centric groups from that collection; grouped and flat consumers therefore
cannot observe divergent copies of the same result.

Each `ResearchChange` carries one mechanism, a `FindingDescriptor`, an
added/removed/changed classification, its subject, and any native producer
payload needed for typed presentation. It is deliberately not a
`PairFinding<T>`. Metadata now exposes genuine API type/member comparisons and
`ResearchComparison.ApiComparison` retains that producer-owned envelope. C#,
IL/body, body-signal, and ReturnToSender mechanisms do not all expose equivalent
old/new Finding censuses yet, so the cross-mechanism `ResearchChange` projection
must not manufacture Finding atoms or misuse `PairKind`.

## Consumer contract

Use `ImplementationDiff.CompareAssemblies` or `ImplementationDiff.Compare` when
the input is a pair of assemblies or `ResearchDiffInput` values. The result is a
list of changed implementation members. Each member can carry C# changes, IL
changes, or both; exact members are omitted.

Use `ImplementationDiff.CompareMembers` when the caller already resolved exact
old/new `MethodDefinitionHandle` values in live `MetadataSource` instances. The
member result keeps the typed C# diff, typed IL diff, joined implementation
changes, and a single `ResearchSubjectKey`; exact members return an empty
change list with `IsExact` set.

Use `ImplementationDiff.ToIlChanges` when a caller already has a scoped
`IlMemberDiffResult`, such as ReturnToSender comparing one original method to a
recompiled artifact method. This preserves typed IL diff data and projects the
same `ResearchChange` model used by assembly-wide Research diffs. Exact typed
diffs produce no IL changes, but callers may still retain the typed
diff in their own result model when exact proof matters.

Use `ImplementationDiff.UnifiedLines(change)` only at presentation boundaries.
The durable model keeps the producer-owned typed display rows rather than a
third implementation-specific row family.

The component is intentionally not a CLI section yet. CLI wiring should be a
separate usability change that chooses section names, verbosity, table schema,
and row limits without changing the product change primitive.

## Non-goals

- It does not prove semantic equivalence; IL/body rows are evidence, not a
  verifier.
- It does not own API compatibility rows. Metadata owns API observations,
  matching, and compatibility classification; Research retains and projects
  that comparison separately from `ImplementationDiff`.
- It does not compile source artifacts or plan closure. ReturnToSender and other
  harnesses own artifact requests and compilation.
