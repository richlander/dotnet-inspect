# Implementation Diff Boundary

`ImplementationDiff` is the product-side decompiled C# + IL/body + authored
Source diff projection in
`ILInspector.Research`. It is the reusable implementation-diff component for
the CLI, ReturnToSender, harnesses, and other consumers that need one
member-centric change model instead of separate C# and IL renderers.

Terminology follows [Finding Nomenclature](finding-nomenclature.md):
`Finding<T>` is a one-version observation, `PairFinding<T>` is a two-version
transition, and evidence is the role either may play rather than a competing row
family.

## Ownership

- `ILInspector.Decompiler` owns C# body diff production and display rows through
  `CSharpBodyDiff` and `CSharpDiffPrinter`.
- `ILInspector.Instructions` owns IL/body diff production and display rows
  through `IlBodyDiff`, `IlAssemblyDiff`, and `IlDiffPrinter`.
- `ILInspector.Research` owns the join. `ImplementationDiff` compares assemblies
  with decompiled C# and IL/body mechanisms, accepts checksum-gated authored
  line inspections from Services, groups changes by `ResearchSubjectKey`, and
  exposes typed display rows and unified lines without reformatting producer
  wording.
- `ResearchComparison.RetainedComparisons` keeps the native
  `FindingComparison<CSharpCanonicalLine>` and
  `FindingComparison<CanonicalIlOperation>` envelopes when requested. Authored
  Source comparisons retain `FindingComparison<string>` with the `text.line`
  descriptor. Research
  cross-checks their exactness against the richer semantic projections for
  members present on both sides.

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
must not manufacture Finding atoms or misuse `PairKind`. `ResearchChange` is a Research-owned migration projection, not the seed of a
parallel generic `EvidenceRow` spine. C# and IL now have native comparisons;
their semantic rows remain because they carry richer producer-owned evidence,
while retained comparisons expose the exact census transitions. `Source` never
replaces or changes the meaning of `CSharp`: one describes checksum-verified
authored text and the other describes product-decompiled text.

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

Use `CompareMembersWithAuthoredSource` when the caller also has old/new
`FindingInspection<string>` envelopes from Services. Use
`WithAuthoredSourceComparisons` to enrich an assembly comparison. These APIs
preserve `Complete`, `Absent`, and `Failed` independently and retain the native
line comparison. Research does not fetch source.

The `diff --finding csharp.line` and `diff --finding il.op` focused lenses read
those retained comparisons and render native `PairFinding` cases. Missing
members and methods without bodies remain distinct inspection states. IL
retention pairs the union of declared method identities, so added, removed, and
signature-changed methods are not lost by the semantic body-diff intersection.
Failed inspections render explicit failure rows instead of becoming empty
comparisons.

Use `ImplementationDiff.ToIlChanges` when a caller already has a scoped
`IlMemberDiffResult`, such as ReturnToSender comparing one original method to a
recompiled artifact method. This preserves typed IL diff data and projects the
same `ResearchChange` model used by assembly-wide Research diffs. Exact typed
diffs produce no IL changes, but callers may still retain the typed
diff in their own result model when exact proof matters.

Use `ImplementationDiff.UnifiedLines(change)` only at presentation boundaries.
The durable model keeps the producer-owned typed display rows rather than a
third implementation-specific row family.

The `diff` command exposes this component through the explicit-only
`Implementation Diff` section. The CLI projects one row per producer-owned
unified line with `Member`, `Mechanism`, `Change`, and `Evidence` columns.
With `--authored-source`, it acquires each changed implementation member's
endpoint PDB and
SourceLink body, verifies the document checksum, and adds a separately labeled
`Source` lane. Missing mappings and acquisition failures remain visible rather
than falling back to decompiled C#.
Package, platform, and local-library ranges use the same acquisition path as the
default API diff; `--type`, `--member`, row limits, table, TSV, and JSONL
projection continue to apply. The CLI consumes this product component and does
not invoke or reconstruct the C# and IL producers independently.

## Non-goals

- It does not prove semantic equivalence; IL/body rows are evidence, not a
  verifier.
- It does not own API compatibility rows. Metadata owns API observations,
  matching, and compatibility classification; Research retains and projects
  that comparison separately from `ImplementationDiff`.
- It does not compile source artifacts or plan closure. ReturnToSender and other
  harnesses own artifact requests and compilation.
