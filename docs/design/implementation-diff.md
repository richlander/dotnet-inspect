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
  members present on both sides. A disagreement is retained as a per-member
  `Failed` diagnostic; it does not abort healthy members in the same diff.

## Research comparison model

`ResearchDiff` is the operation facade. It returns one `ResearchComparison`
containing a flat `Changes` collection. `BySubject()` computes member- and
type-centric groups from that collection; grouped and flat consumers therefore
cannot observe divergent copies of the same result.

Each `ResearchChange` carries one mechanism, a `FindingDescriptor`, an
added/removed/changed/failed classification, its subject, and any native producer
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

### Deliberate dual-representation decision

This design revises the earlier plan to retire the C# and IL semantic
projections immediately after Finding adoption. The two retained
representations have different durable payloads:

- native Finding comparisons own inspection outcomes, stable census identity,
  and added/removed/present/changed transitions;
- `CSharpBodyDiff` and `IlBodyDiff` own aligned hunks, typed display failures,
  old/new offsets, and richer producer-formatted evidence.

The semantic projections therefore remain deliberately rather than by
accretion. Every overlapping member is cross-validated against the native
comparison, and divergence becomes a visible per-member `Failed` diagnostic.
If the Finding producers later carry equivalent aligned hunk and typed display
payloads, the semantic projections should be deleted rather than matched a
third time.

## Row currency contract

Every diff row across MetadataDiff, ILDiff, Analysis/body-signal diff, C#Diff,
and ResearchDiff must be reachable back to its owning API/member through stable
member currency, then locatable inside its own mechanism through native row
coordinates. The two obligations are separate: currency answers "which member,"
native coordinates answer "which row within that member." This section states
which layer supplies each obligation. It does not impose a universal
`Before`/`After` handle: IL rows already carry side as row polarity
(`Add`/`Remove`/`Context`), and forcing an explicit old/new pair onto that shape
would duplicate the native model. See [Finding Coordinates](finding-coordinates.md)
for the underlying subject / correspondence / provenance axes; this contract is
the diff-row application of those axes.

### Two carrier classes

Rows fall into exactly one of two classes by whether the producing layer knows
member identity:

- **Anchor-carrying rows** live at member altitude and carry the stable member
  currency directly. `ApiChange` (MetadataDiff) owns `MemberAnchor` through
  `ApiChangeSubject`, exposing `CanonicalSignature`, `StableSelector`
  (`Name~digest`), and the member digest as typed fields. Metadata is the layer
  that owns member identity, so its rows are self-describing.
- **Member-agnostic substrate rows** are produced by layers that intentionally
  do not depend on Metadata, so they cannot and must not embed a `MemberAnchor`.
  `IlDiffRow`/`CanonicalIlOperation` in `ILInspector.Instructions` and
  `CSharpDiffRow` in `ILInspector.Decompiler` carry only their native
  coordinates and a producer-owned `Message`. The caller that already resolved
  the member — Research, via `ResearchSubjectKey` from
  `ResearchMemberIdentity.SubjectFromAnchor` — supplies the stable currency by
  wrapping. This caller-owned wrapping is deliberate; it keeps
  `ILInspector.Instructions` and the C# printer free of a Metadata dependency
  and NativeAOT/SRM-clean, and it is why no low-level substrate row grows a
  `MemberAnchor` field.

A row is never both. Adding member identity to a substrate row, or reconstructing
it there from display text, would duplicate identity the wrapper already owns and
violate the layer-ownership rule.

### Native coordinates each mechanism preserves

The wrapper preserves, not flattens, the native coordinates so a consumer can
replay or locate the row after the member is known:

| Mechanism | Anchor source | Native row coordinates |
| --- | --- | --- |
| MetadataDiff | `ApiChange` (`MemberAnchor` on `ApiChangeSubject`) | `ApiChangeSubjectKind`, old/new member handles, category |
| ILDiff | wrapper (`ResearchSubjectKey`) | `HunkId`, `IlDiffKind` polarity, `CanonicalIlOperation`, IL offset (hint) |
| Analysis/body-signal | wrapper (`ResearchSubjectKey`) | signal / shape, added/removed/changed kind, IL offset(s) as evidence |
| C#Diff | wrapper (`ResearchSubjectKey`) | `ChangeId` / `CSharpDiffKind`, source-shape span, related IL offsets as evidence |

IL offsets, operation-array ordinals, and source spans are local evidence and
display hints, never the durable selector. The durable selector is always the
`MemberAnchor`-derived `StableSelector` / canonical signature / digest carried by
the anchor-carrying row or its wrapper.

### ResearchDiff projection

`ResearchChange` binds one member-agnostic native payload (`IlRow`, `CSharpRow`,
`ApiChange`, and the analysis signal fields) to one `ResearchSubjectKey` whose
`Id` is the anchor `StableSelector`, and to a cross-mechanism product `ChangeId`
via `FindingDescriptor`. It never erases the lower-layer typed payload and never
requires consumers to parse `Message`. Machine consumers query by `ChangeId`
through `HasChange`, `HasChangePrefix`, and `HasChangeCategory`; product
`ChangeId`s use fact concepts (`unsafe.stackalloc.added`, `il.hunk.changed`,
`csharp.return-expression.changed`), not incidental detail fields. `Message`
stays producer-owned presentation on either side of the join.

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

Finding acquisition and cross-validation failures use
`ResearchChangeKind.Failed`; they are operational diagnostics, never semantic
`Changed` rows in table, TSV, JSONL, or programmatic consumers.
When a semantic projection carries the corresponding typed failure, Research
keeps that richer row and suppresses the duplicate generic Finding failure.
Synthetic add/remove rows from the same failed C# hunk are omitted; genuine
body absence and independently decoded partial IL evidence remain visible.

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

Each IL change also retains its `IlBodyDiffResult`. Its total outcome is
`Exact`, `OperandDiff`, `OpcodeDiff`, or `Unavailable`. Exact means both bodies
are equal after the requested normalization. Unavailable means no comparison
verdict exists; typed failure rows retain the reason. Non-IL mechanisms do not
carry this payload.

Use `ImplementationDiff.UnifiedLines(change)` only at presentation boundaries.
The durable model keeps the producer-owned typed display rows rather than a
third implementation-specific row family.

The `diff` command exposes this component through the explicit-only
`Implementation Diff` section. The CLI projects one row per producer-owned
unified line with `Member`, `Mechanism`, `Difference`, `Change`, and `Evidence`
columns. `Difference` contains the IL body outcome for IL rows and is empty for
C# rows, keeping mechanism, result, edit kind, and evidence as separate
dimensions.
With `--authored-source`, it acquires each changed implementation member's
endpoint PDB and
SourceLink body, verifies the document checksum, and adds a separately labeled
`Source` lane. Missing mappings and acquisition failures remain visible rather
than falling back to decompiled C#.
The authored A→IL lane reuses the final RTS shell/request but compiles with
portable-PDB-recorded options when available; the decompiled B→IL lane uses the
RTS compile context. `BuildContext` and determinism verdicts therefore remain
part of interpreting any Exact/IlDifferent disagreement.
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
