# Method classification analyzers

## Status

Focused design for item 6 of
[#8733](https://github.com/richlander/dotnet-inspect/issues/8733). It replaces
`MethodClassificationScanner.Scan` with three
[Producer Planning](producer-planning.md) producers over the method-row gate
that [The source gate owns safety](producer-planning.md#the-source-gate-owns-safety)
defines. Not implemented; every property below is **unverified** until its
gate lands.

Slice 2b of #8733 amends the async analyzer chosen in
[#8788](https://github.com/richlander/dotnet-inspect/issues/8788). Async is
now two analyzers, runtime async and compiler async, and the async analyzer
asks both in one pass. Compiler async matches the state-machine attributes in
place instead of reading `StateMachineRelationshipIndex`, under the
[fidelity policy](#fidelity-policy).

## Examples

`dotnet-inspect library System.Text.Json --section "Async Methods"` shows the
library's public async methods. The request names one producer, the async
analyzer. The gate applies the scope and classifies each row. The analyzer
reads the runtime-async flag, and only when it is clear matches the method's
state-machine attributes in place, then reads identity text only for the rows
it publishes. Nothing tests for P/Invoke imports or walks pointer signatures,
and the analyzer has no dependency, so it runs as a closed-query kernel.

`dotnet-inspect library System.Text.Json --section "Async Methods" --count`
reads no identity text. The plan declares no `IdentityText`, so the gate's
budget is never armed. An Exists question stops at the first async method.

A hostile image that exhausts the gate's identity budget while rows are being
projected aborts the execution. Every requested analyzer is `Aborted` with one
`CriticalFailure`, and nothing is published. Legacy failed all three questions
together in the same case.

## Owner and exact claim

**Method classification analyzers** owns this exact claim:

> The P/Invoke, runtime-async, compiler-async, and pointer-signature analyzers
> each publish, for one method-definition image compiled by Roslyn, the rows
> `MethodClassificationScanner.Scan` publishes for its classification on the
> same image, with the same method name, declaring type, namespace, signature
> text, classification, module name, anchor, and return type. Runtime async
> publishes legacy's `RuntimeAsync` rows and compiler async its
> `StateMachineAsync` rows. Rows from two or more analyzers, merged by
> metadata order and then by the legacy order P/Invoke, async, pointer, equal
> the legacy list. Head(N) returns at most the first N matching rows in
> metadata order and stops at the Nth match. Count and Exists over these
> analyzers read no identity text.

This owner defines:

- the scope the method-row gate applies for classification: public,
  non-accessor methods on types whose name does not start with `<`, as legacy
  defines it;
- the gate's classes for classification and which classes each analyzer
  accepts;
- each analyzer's test and the fields it declares;
- the fidelity each analyzer promises; and
- the async analyzer that asks both async tests and the merge order that reproduces the
  legacy list.

This owner does not define:

- the gate, its two tiers, field demand, the budget, or the abort.
  [Producer Planning](producer-planning.md) owns those.
- async recognition, pointer detection, anchors, or signature text. Metadata
  owns those, and this design reuses them behind the gate.
- the classified-method Finding descriptor, which Metadata owns; or
- sections, Signals rows, or presentation, which each host binds and
  presents.

## The analyzers

| Analyzer | Accepts, from the gate | Test (Tier 1) | Declares |
| --- | --- | --- | --- |
| P/Invoke | `PInvoke` | none: the class is the test | `Flags`, and `IdentityText` for rows |
| Async | `Other` | the runtime-async test, else the compiler-async test, per row | `Flags`, `AttributeTypeMatch`, and `IdentityText` for rows |
| Runtime async | `Other` | the runtime-async implementation flag, `0x2000` | `Flags`, and `IdentityText` for rows |
| Compiler async | `Other` | no runtime-async flag, and a custom attribute whose type is `System.Runtime.CompilerServices.AsyncStateMachineAttribute` or `AsyncIteratorStateMachineAttribute`, matched in place | `Flags`, `AttributeTypeMatch`, and `IdentityText` for rows |
| Pointer signature | `Other` | a pointer in the return or a parameter type | `SignatureShape`, and `IdentityText` for rows |
| Extension | `Extension`, from the extension scope | none: the class is the test | `Flags`, `AttributeTypeMatch`, `HiddenAttribute`, and `IdentityText` for rows |

The extension analyzer uses its own gate classifier, the **extension scope**,
not the classification scope. Its type scope is a static (sealed abstract)
type carrying `[Extension]` and not hidden; its one class, `Extension`, is a
public static method carrying `[Extension]` and not hidden. That is the
method half of `ExtensionMethodScanner.FindAllExtensions(includeAll: false)`,
which the Library Info Extension Methods row counts; extension properties
from C# 14 extension blocks are a different population over nested marker
types and are not this analyzer's rows. The legacy scan also decodes each
candidate's signature through the signature guard and skips a method with no
parameters or a signature the guard rejects. Roslyn never emits a
parameterless `[Extension]` method, and a Roslyn signature exceeds the guard
only when a parameter or return type nests more than 512 array or pointer
levels, where the method is an extension method and the guard's rejection is
containment, not fidelity. Under the [fidelity policy](#fidelity-policy) the analyzer omits
the decode: on Roslyn-produced assemblies its Count equals the legacy count
plus any such over-bound method legacy drops (gated by a 513-level fixture in
`MethodClassificationAnalyzerTests`), and it is wrong but contained
elsewhere. Its rows are not merged into the classified-method Finding.

For classification, the gate applies the legacy scope. It then classifies each
in-scope row as `PInvoke`, by the `PinvokeImpl` flag, or `Other`. Rows outside
the scope belong to neither class, so no analyzer attempts them. The analyzers
accept gate classes through the scope guards of #8735, with the gate as the
classifier, which makes this migration the production caller #8735 owes.

Runtime async and compiler async are disjoint: compiler async excludes
methods that carry the runtime flag, as legacy classifies the flag first. The
async analyzer reuses both tests in one pass, so its rows are their union and
a runtime-async method never pays for the attribute match. Each row carries
the kind the test found. Async
and pointer signature are independent. A method that is both async and
has a pointer signature produces one async row and one pointer row, as legacy
does. A P/Invoke method produces only its P/Invoke row, because legacy
classifies P/Invoke first and stops.

Row projection (declaring type, name, namespace, signature text, anchor,
return type, module name) goes through the gate's Tier 2 identity accessor.
That accessor uses Metadata's existing projection functions, so the text
equals legacy's. Rows and Head declare `IdentityText`; Count and Exists do not.

## Tier 1 faithfulness

Each Tier 1 test must equal the legacy test on every input:

- **Scope and P/Invoke** are flag and name tests. The name prefixes (`<`,
  `get_`, `set_`, `add_`, `remove_`) are compared in place with
  `MetadataStringComparer`.
- **Runtime async** is a flag test, equal to legacy on every input.
- **Compiler async** in legacy is not in place. `ClassifyAsyncMethod` calls
  `AttributeReader.HasAttribute`, which materializes each custom attribute's
  full type name through `TypeResolver.GetTypeName` and compares strings. It
  charges no budget. Hardening that path for its existing consumers is
  [#8780](https://github.com/richlander/dotnet-inspect/issues/8780).
  Compiler async asks the same question through the gate's Tier 1 attribute
  type match, which materializes no name:
  - It compares namespace and name handles in place with
    `MetadataStringComparer`, walking a nested type's declaring or
    resolution-scope chain segment by segment, and spells the name as
    `TypeResolver` does.
  - A `TypeSpecification` parent is read as `TypeResolver` decodes it:
    custom modifiers and `pinned` spell nothing, and `class` or `valuetype`
    spells its TypeDef or TypeRef. A generic instantiation, and every other
    element type, spells a bracket, suffix, keyword, or argument list that
    never equals the two non-generic target names, so it is answered without
    decoding.
  - Its answer is memoized per attribute constructor handle, per attribute
    type handle, and for a `TypeSpecification` parent per signature blob, for
    the execution. Total work is therefore linear in the CustomAttribute,
    MemberRef, TypeRef, and TypeDef rows and the `#Blob` heap.
  - The existing `MaxRelationshipNodes` chain bound backstops each walk. A
    `TypeSpecification` blob is read only where legacy's `TypeSpecGuard`
    would decode it: within 4,096 bytes and `SignatureBlobGuard`'s structural
    bounds. A chain that repeats a handle or exceeds the bound, or a blob
    past legacy's guard, aborts; an unreadable name fails the analyzer at
    that method.

  The match equals the materialized comparison for attribute types that are
  defined in the image or referenced, nested, or reached through a
  `TypeSpecification` parent. Compiler async therefore equals legacy's
  attribute test wherever legacy's test completes.
- **Extension scope** reads the type's `Sealed` and `Abstract` flags and the
  method's `Public` and `Static` flags, matches `[Extension]` on the type and
  on the method through the same in-place attribute type match and memo as
  compiler async, and applies the legacy hidden test (`EditorBrowsable(Never)`,
  or an `Obsolete` that is not Roslyn's compiler-compatibility marker) as
  Metadata's `AttributeReader.HasHiddenAttribute` defines it, with the three
  attribute types (`EditorBrowsable`, `Obsolete`, `CompilerFeatureRequired`)
  matched through the same per-constructor memo and the values read in
  place: one `int32` for the browsable state, and the fixed-argument string
  compared byte for byte against the two compiler-compatibility messages and
  feature names, within legacy's sixteen-byte length slack. No name or value
  is materialized, and the row's `[CompilerFeatureRequired]` features are
  read once however many compatibility markers it carries, so the test stays
  inside the Tier 1 work bound; it is declared as its own `HiddenAttribute` field,
  and the scope reads it only for types and rows that passed the cheaper
  in-place tests. Hardening the legacy scan's own path is
  [#8780](https://github.com/richlander/dotnet-inspect/issues/8780).
- **Pointer signature** in legacy walks the signature with a detector that
  charges the shared scan work budget. Every composite and `TypeSpec` visit is
  charged, because a wide `GENERICINST` repeated across methods is the known
  hostile case. Here the walk is a Tier 1 signature-shape accessor, bounded
  in two ways:
  - **Memoized.** For the execution, the gate caches the pointer-shape answer
    for each `TypeSpec` handle and for each signature blob it walks. Each
    `TypeSpec` and each blob is therefore walked at most once, however many
    rows or nested generic arguments share it. Every visited node consumes at
    least one blob byte, so the total nodes walked are at most the size of
    the `#Blob` heap. That is linear in the image, with no multiplier and no
    budget. Pointer Count and Exists stay free of the identity budget.
  - **Backstop.** A fixed per-row cap of 65,536 type nodes, the existing
    `MetadataSafetyPolicy.MaxSignatureTypeNodes` bound on one signature.
    Exceeding it aborts the execution with the typed `CriticalFailure`.

  Faithfulness through the cache: a `TypeSpec`'s cached entry also records
  the deepest re-entry depth and the largest byte closure its expansion
  reaches. A reuse checks those against `TypeSpecGuard`'s limits (depth 256,
  4,096 bytes) in the current context. So the gate refuses exactly where
  legacy's guard would. Where legacy's guard refused, legacy answered "no
  pointer" for that part, a success-shaped answer. The gate aborts with the
  typed `CriticalFailure` instead, as the abort rule requires. On inputs where
  no guard refuses, the answer equals legacy.

## Fidelity policy

A classification may take a fast path whose answer is exact for Roslyn
output, including SDK-trimmed output, and wrong but never insecure for output
of other tools:

- **Roslyn output is exact.** Roslyn emits `AsyncStateMachineAttribute` or
  `AsyncIteratorStateMachineAttribute` exactly on the kickoff methods of its
  async state machines. ILLink keeps or removes a kickoff method and its state
  machine as a unit and has no default rule that strips either attribute. A
  trim experiment on .NET 11.0.0-rc.1 compared the attribute test with
  `StateMachineRelationshipIndex` across 10 assemblies, untrimmed and with
  `PublishTrimmed` in `partial` and `full` modes, and found no disagreement,
  rejected relationship, or orphan state machine.
- **Other tools may be wrong, never insecure.** A rewriter that keeps the
  attribute but deletes the state machine makes compiler async overclaim; a
  custom trimmer that strips the attribute makes it undercount. Every read
  stays bounded, memoized, and inert, so neither case costs more than the
  image's rows or allocates metadata names.
- **One fidelity for every closing.** Rows, Head, Count, and Exists apply the
  same test. Count equals the number of rows whenever Rows succeeds, and
  Head(N) equals the first N of those metadata-ordered rows.
- **Authentication stays with its owner.** `StateMachineRelationshipIndex`
  serves work whose job is authentication: decompiler reconstruction and
  explicitly requested relationship facts. The classification analyzers do
  not read it.

Fixture scope follows the policy. The analyzers' equivalence gates use
normal-case, Roslyn-shaped fixtures and every built repository fixture as
legacy classifies it, and pin no spoofed or malformed state-machine outcome;
adversarial correctness fixtures belong to the authentication owners, while
the gate keeps its hostile safety fixtures.

## Budget

The gate owns one identity budget per execution, sized as legacy's
`MaxClassificationScanWorkChars` and `MaxClassificationIdentityDecodeFailures`.
It is armed only when a requested analyzer declares `IdentityText`, and it is
charged where identity text is decoded, including identity-decode failures. Exhausting it aborts the execution
under
[Budget exhaustion aborts the execution](producer-planning.md#budget-exhaustion-aborts-the-execution).
Every requested closing is `Aborted` with the typed `CriticalFailure`. Nothing
is published, and a partial count is never reported. On hostile inputs, rows
fail together, as legacy's do.

Count and Exists decode no identity text, and that is what makes them fast.
The consequence: they cannot notice hostility that lives in identity text.
On such an image, a Count or Exists may answer, possibly with a misleading
result, where a Rows request over the same image decodes the identities,
exhausts the budget, and aborts. Count equals the number of rows whenever
Rows succeeds. This is a consequence of the performance design, not a goal,
and hostile images are not promised an answer.

Head decodes the identity text of each row it publishes. Reaching N matching
rows settles Head(N) before the next raw method is read. A later hostile method
therefore cannot fail that already settled request, just as a later hostile
method cannot fail Exists after its first match.

Below the budget, the analyzers handle recoverable failures as follows:

- **Identity projection fails** for a row that is classified. This happens
  only in Rows, where `IdentityText` is declared. As in legacy, the row is
  still emitted, with signature text `methodName(...)` and a null anchor and
  return type, and the failure counts against the identity budget's
  decode-failure limit. This applies to P/Invoke, async, and pointer rows
  alike.
- **The pointer probe fails** with a malformed signature. This happens in
  any closing, Count and Exists included. The probe is a Tier 1 read, so per
  [The source gate owns safety](producer-planning.md#the-source-gate-owns-safety)
  the unreadable row fails the pointer analyzer, with a typed `Failed`
  outcome that names the method. The method is not silently omitted, and no
  decode-failure counter is needed, because the analyzer stops at its first
  failure. This departs from legacy only on images with malformed
  signatures. Legacy skipped such methods silently, and counted a
  `BadImageFormatException` toward its failure limit, so its pointer count
  could quietly miss them.

## Queries and demand

The queries live in host-neutral `DotnetInspector.Queries`, beside
`UnsafeEvidencePresenceQuery`:

- **One query per analyzer:** P/Invoke, runtime async, compiler async,
  pointer signature, and extension. Each is parameterized by its closing (Rows, Head(N),
  Count, or Exists) and returns a typed result for that closing, or the typed
  critical failure.
- **Async is one producer,** not a composition of the two. Its test is the
  runtime-async test, else the compiler-async test, reusing both analyzers'
  code, and it declares the union of their fields. Exists stops at the first
  async method, Head(N) stops at the Nth matching method, and Count and Rows
  are one walk; the answers equal the union of runtime async and compiler
  async, which stay askable on their own.
  Composing disjunctions across producers belongs to QuerySpace
  ([#8574](https://github.com/richlander/dotnet-inspect/issues/8574)).
  The operator chose this on 2026-09-28 over a query-layer composite that
  asked runtime async across the whole scope before compiler async. Slice
  2b's performance scorecard showed that composite paid for two walks on
  assets with no runtime async: Exists rose 16% on Humanizer and 8% on
  Mono.Cecil, and CoreLib's Count 4%.

  Scope (library, type, or method) is an axis independent of the question.
- **Request identity.** Each analyzer has exactly one producer declaration.
  Closing and row order are request parameters, not declaration parameters.
  So several consumers asking the same analyzer never create conflicting
  declarations. Requests are not merged at this level. Per
  [Requests are QuerySpace requests, never merged here](producer-planning.md#requests-are-queryspace-requests-never-merged-here),
  each consumer's request runs its own closing:
  - Rows, each distinct Head(N), Count, and Exists for the same analyzer are
    separate requests. Count and Exists never declare `IdentityText`, even
    when a row closing is also requested.
  - Nothing derives one closing from another, and no ranking of closings
    exists in the queries or the planner.
  - Collapsing several requests for one resource into one pass belongs to
    QuerySpace, [#8574](https://github.com/richlander/dotnet-inspect/issues/8574).
    Until it lands, a section's Rows and a summary's Count for the same
    analyzer cost one extra pass, and the Count pass decodes no identity
    text.
- **Rows order is a typed request parameter,** not a host sort. A query
  offers exactly the orders today's outputs use:

  | Analyzer | Model order (JSON, `LibraryInspection`) | Display order (Markdown view) |
  | --- | --- | --- |
  | Async, runtime async, compiler async | kind (ordinal), then declaring type, then method name (default comparer) | declaring type, method name, signature (`OrdinalIgnoreCase`) |
  | P/Invoke | declaring type, then method name (default comparer) | declaring type, method name, module name, signature (`OrdinalIgnoreCase`) |
  | Pointer signature | declaring type, then method name (default comparer) | none; the list has no Markdown view |

  The model orders are the ones in `LibraryMetadataService`, and the display
  orders are the ones in `LibraryInspectionView.cs`.

  A host names the order it shows, and the query returns rows in that order.
  No host sorts or projects rows itself.
- **Head is metadata-ordered in this adoption.** A positive N is part of the
  closing identity. The source visits as many raw method definitions as needed
  to produce N matches, projects only those matches, and stops before the next
  method. Source exhaustion returns the fewer matching rows. Model- and
  display-ordered Head are not admitted because those orders require the
  complete population before selection.
- **One combined request** runs every requested analyzer and closing in one
  execution, with each consumer's own closing and no merging between
  them. Rows are requested only by the Finding and the row
  sections. The merged rows, in legacy order, and the Finding inspection built
  from them come only when the Finding is requested. Signals and LibraryInfo
  counts get Count closings, matching what each shows today; LibraryInfo asks
  the async and extension Counts and no longer demands the extension row scan:
  `AuditSignalBuilder` shows counts for pointer and P/Invoke. Count closings
  declare no `IdentityText`, so they spend no identity budget. The Async Kind
  signal is not a consumer. It reads
  `AssemblyDetailScanner.ScanPresenceFlags`, whose scope includes P/Invoke
  methods, and it stays as is. Its unbudgeted attribute match is covered by
  #8780. No host merges results by hand.

Hosts only bind. A section registers the query and the closing it shows.
`LibraryInspection` maps typed results into its model, with no splitting,
merging, sorting, or counting logic of its own. Counts come from Count
closings, or from the combined result's counts, never from counting rows in
the host.

Each consumer asks only for what it shows. Operator decision (2026-09-28):
summaries and the default `--json` model collapse to counts, and lists appear
only when their section asks for them.

| Consumer | Asks for |
| --- | --- |
| Async Methods section | async rows in the model and display orders, or Count for `--count` |
| P/Invoke Methods section | P/Invoke rows in the model and display orders, or Count for `--count` |
| Pointer-signature method list (`UnsafeMethods`) | nothing: no section shows it, so the list is retired and the pointer Count stands for it |
| Signals | Count for pointer ("public pointer signatures") and Count for P/Invoke; the P/Invoke signal prefers the metadata-wide count |
| Library Info | Count for async, the one classification count it shows |
| Default `--json` model dump | Count for each analyzer |
| Classified-method Finding | nothing in the CLI: no host reads its per-method observations, and classification failures come from the typed answers |

The combined request's Finding observations equal those legacy produces from
`ClassifiedMethodsQuery`. On a critical failure, every query that was asked
returns the one typed failure, and every host presents it.

## Layering

The analyzers and the method-row gate live in `ILInspector.Analysis`, beside
Producer Planning. The gate uses Metadata's classification, in-place
comparison, and projection functions through a public API that names no
Planning type, so Metadata does not reference Planning. The queries live in
`DotnetInspector.Queries`, which already references Analysis. Hosts reference
the queries only.

## Production adoption

1. **CLI.** Bind Async Methods first, as the demo. Then bind P/Invoke
   Methods, the pointer-signature list, Signals, the Finding, and the
   LibraryInfo counts. Each migrated consumer drops its read of the combined
   `ClassifiedMethodsQuery` result, and `ApplyClassifiedMethodsResult` loses
   its filtering, sorting, and projection.
2. **Browser/Wasm.** Approval record: on 2026-09-28 the operator approved
   CLI-first scope for #8773. Browser/Wasm binds the same
   `DotnetInspector.Queries` analyzers when a browser consumer exists. The
   browser has no consumer of these sections today.
   Under the layering, the browser reaches inspection only through
   host-neutral product queries that return `InspectionEnvelope<T>`. A future
   browser consumer binds the analyzer queries above, the same way the CLI
   does. That is binding, not porting: the browser needs no analyzer, gate,
   merge, or order logic of its own.
3. **Retirement.** When no consumer reads the combined result,
   `ClassifiedMethodsQuery`, `AssemblyInspectionSession.ClassifiedMethods`, and
   `MethodClassificationScanner.Scan` are removed. `ClassifyAsyncMethod` stays,
   because the decompiler, C# shells, and PDB context use async
   classification.

## Existing budgeted producer

Unsafe presence keeps its same-image correspondence budget, because that bound
is specific to its domain. Today, exhausting it throws
`BadImageFormatException`, which the executor treats as a recoverable failure
at one method. That is the Microsoft.CodeAnalysis.CSharp 4.14.0 result
recorded as `incomplete`. Moving it to the typed critical signal is follow-up
work, tracked in #8733, and not part of this change.

## Verification

- **Combined consumers.** Async section Rows and a LibraryInfo async Count
  run as separate requests with equal answers, and the Count declares no
  `IdentityText`. Nothing in the queries or the planner ranks or merges
  closings.
- **Head stop.** An async Head(2) fixture places one nonmatching raw method
  before two matches and a hostile method after them. It visits three raw
  methods, returns the two matches, reports a stopped producer, and does not
  read the hostile fourth method; Rows over the same image aborts visibly.
- **Malformed pointer signature under Count.** It fails the pointer analyzer
  with a typed `Failed` outcome naming the method, and never publishes a
  silently reduced count.
- **Section `--count`.** `--count` on the Async Methods and P/Invoke Methods
  sections requests Count and declares no `IdentityText`.
- **Display order.** A P/Invoke overload pair on one type, `Run(int)` from
  `a.dll` and `Run(bool)` from `z.dll`, shows in the legacy order. Async rows
  that differ only by kind, or only by signature, show in the legacy order in
  both the model and the display.
- **Consumer output equivalence.** A request for Signals alone gives the same
  numeric pointer and P/Invoke counts as legacy. The Async Kind signal is
  unchanged, because it does not read the classified result. A public P/Invoke
  method that is also async, and is the only async method, still reports its
  async kind.
  The scanner test fixture with several pointer-signature methods covers it,
  and no host counts anything.
- **Equivalence on real assets.** For each analyzer, and for the merged rows,
  output equals `MethodClassificationScanner.Scan` field by field on the
  repository's pinned test packages. Boundary fixtures:
  - an empty type and compiler-generated types;
  - accessors and non-public methods;
  - a P/Invoke method;
  - runtime async, and state-machine async;
  - a method that is both async and has a pointer signature (two rows);
  - an unreadable signature.
- **In-place attribute match.** It equals the materialized comparison on
  attribute types that are defined, referenced, nested, and reached through
  a `TypeSpec` parent (generic, `class`, modified, array, and self-naming).
  A cyclic or over-bound nested chain, and a `TypeSpec` blob past legacy's
  byte or shape guard, abort. `TypeSpec` rows that share one blob read it
  once.
- **Async equals legacy.** On the pinned packages, the eight performance
  scorecard assemblies, and every built repository fixture, runtime-async
  rows equal legacy's `RuntimeAsync` rows, compiler-async rows its
  `StateMachineAsync` rows, the two are disjoint, and async's Rows, Count,
  Head, and Exists agree with legacy's async rows.
- **One pass.** Async's Rows, Head, Count, and Exists equal the union of
  runtime async and compiler async. Exists stops at the first async method,
  and Head stops at its requested match, before a later method whose attribute
  match would abort; a runtime-async method's attributes are never matched.
- **Count reads no identity text.** On the existing hostile classification
  fixtures, Count, Exists, and classification charge zero identity budget and
  complete. Rows on the same fixtures abort with `CriticalFailure`.
- **Linear structural work.** A hostile fixture in which many methods share
  deeply nested generic `TypeSpec`s completes pointer Count, and the number
  of walked nodes is at most the image's `#Blob` heap size. A matching
  fixture that shares nested attribute parent chains keeps attribute-match
  work within its row counts, with no dependence on the number of methods.
- **Per-row cap aborts.** A single signature whose nodes exceed 65,536 aborts
  pointer Count and Rows with a `CriticalFailure` that names the gate's
  per-row signature cap. Every row of the pinned test packages stays under
  the cap, and the largest count seen is recorded.
- **Guard parity.** On fixtures where legacy's `TypeSpecGuard` refuses, in
  both a fresh context and a nested one, the gate aborts. On every fixture
  where it does not refuse, the pointer answers equal legacy.
- **Abort.** When two analyzers run together and the gate's budget is
  exhausted, neither publishes a result, both are `Aborted` with the same
  `CriticalFailure`, and no row after the exhausting one is read.
- **Thin hosts.** The CLI's classified-method binding contains no
  filtering, sorting, merging, or counting of classified rows. The query tests
  assert order and counts, so the CLI's output tests are not the gate.
- **Kernel.** An analyzer asked alone runs as a closed-query kernel over the
  gate, and its results equal the interpreted executor's.
- **End to end.** A NativeAOT base/head comparison of the migrated sections,
  per the [evidence contract](../evidence-and-validation.md#nativeaot-beforeafter-for-modernization),
  on every supported terminal: rows, `--count`, `-n`, and `--rows`.
- **Performance scorecard.** Old, LINQ, NLinq, and Planner over async and
  each of runtime and compiler async, against the NLinq fixture
  of [#8745](https://github.com/richlander/dotnet-inspect/issues/8745). LINQ,
  NLinq, and Planner apply the identical analysis, the same flag test and
  in-place attribute match; only the read machinery differs.
