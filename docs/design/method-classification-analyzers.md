# Method classification analyzers

## Status

Focused design for item 6 of
[#8733](https://github.com/richlander/dotnet-inspect/issues/8733). It replaces
`MethodClassificationScanner.Scan` with three
[Producer Planning](producer-planning.md) producers over the method-row gate
that [The source gate owns safety](producer-planning.md#the-source-gate-owns-safety)
defines. Not implemented; every property below is **unverified** until its
gate lands.

## Examples

`dotnet-inspect library System.Text.Json --section "Async Methods"` shows the
library's public async methods. The request names one producer, the async
analyzer. The gate applies the scope and classifies each row. The analyzer
matches the async attribute type in place, then reads identity text only for
the rows it publishes. Nothing tests for P/Invoke imports or walks pointer
signatures, and the analyzer has no dependency, so it runs as a closed-query
kernel.

`dotnet-inspect library System.Text.Json --section "Async Methods" --count`
reads no identity text. The plan declares no `IdentityText`, so the gate's
budget is never armed.

A hostile image that exhausts the gate's identity budget while rows are being
projected aborts the execution. Every requested analyzer is `Aborted` with one
`CriticalFailure`, and nothing is published. Legacy failed all three questions
together in the same case.

## Owner and exact claim

**Method classification analyzers** owns this exact claim:

> The P/Invoke, async, and pointer-signature analyzers each publish, for one
> method-definition image, the rows `MethodClassificationScanner.Scan` publishes
> for its classification on the same image, with the same method name,
> declaring type, namespace, signature text, classification, module name,
> anchor, and return type. Rows from two or more analyzers, merged by metadata
> order and then by the legacy order P/Invoke, async, pointer, equal the
> legacy list. Count and Exists over these analyzers read no identity text.

This owner defines:

- the scope the method-row gate applies for classification: public,
  non-accessor methods on types whose name does not start with `<`, as legacy
  defines it;
- the gate's classes for classification and which classes each analyzer
  accepts;
- each analyzer's test and the fields it declares; and
- the merge order that reproduces the legacy list.

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
| Async | `Other` | runtime-async flag, or the async state-machine attribute types | `Flags`, `AttributeTypeMatch`, and `IdentityText` for rows |
| Pointer signature | `Other` | a pointer in the return or a parameter type | `SignatureShape`, and `IdentityText` for rows |

For classification, the gate applies the legacy scope. It then classifies each
in-scope row as `PInvoke`, by the `PinvokeImpl` flag, or `Other`. Rows outside
the scope belong to neither class, so no analyzer attempts them. The analyzers
accept gate classes through the scope guards of #8735, with the gate as the
classifier, which makes this migration the production caller #8735 owes.

Async and pointer signature are independent. A method that is both async and
has a pointer signature produces one async row and one pointer row, as legacy
does. A P/Invoke method produces only its P/Invoke row, because legacy
classifies P/Invoke first and stops.

Row projection (declaring type, name, namespace, signature text, anchor,
return type, module name) goes through the gate's Tier 2 identity accessor.
That accessor uses Metadata's existing projection functions, so the text
equals legacy's. Only Rows declares `IdentityText`; Count and Exists do not.

## Tier 1 faithfulness

Each Tier 1 test must equal the legacy test on every input:

- **Scope and P/Invoke** are flag and name tests. The name prefixes (`<`,
  `get_`, `set_`, `add_`, `remove_`) are compared in place with
  `MetadataStringComparer`.
- **Async** in legacy is not in place. `ClassifyAsyncMethod` calls
  `AttributeReader.HasAttribute`, which materializes each custom attribute's
  full type name through `TypeResolver.GetTypeName` and compares strings. It
  charges no budget. Hardening that path for its existing consumers is
  [#8780](https://github.com/richlander/dotnet-inspect/issues/8780). The gate
  here adopts the in-place match instead. The gate's attribute type match
  compares namespace and name handles in place, walking a nested type's
  declaring or resolution-scope chain segment by segment. Its answer is
  memoized per attribute constructor handle, and the chain walk per type
  handle, for the execution. Total work is therefore linear in the
  CustomAttribute, MemberRef, TypeRef, and TypeDef rows. The existing
  `MaxRelationshipNodes` chain bound backstops each walk, and exceeding it
  aborts. The match must equal the materialized comparison for attribute
  types that are:
  - defined in the image or referenced;
  - nested, including a nested chain whose segments spell the target name
    in legacy's formatting;
  - reached through a generic `TypeSpecification` parent. Such a type is
    answered without decoding, because a generic instantiation's formatted
    name can never equal the two non-generic target names.
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

## Budget

The gate owns one budget per execution, sized as legacy's
`MaxClassificationScanWorkChars` and `MaxClassificationIdentityDecodeFailures`.
It is armed only when a requested analyzer declares `IdentityText`, and it is
charged where identity text is decoded. Exhausting it aborts the execution
under
[Budget exhaustion aborts the execution](producer-planning.md#budget-exhaustion-aborts-the-execution).
Every requested closing is `Aborted` with the typed `CriticalFailure`. Nothing
is published, and a partial count is never reported. On hostile inputs, rows
fail together, as legacy's do.

Below the budgets, legacy handles two recoverable failures differently, and
the analyzers do the same:

- **Identity projection fails** for a row that is classified. The row is
  still emitted, with signature text `methodName(...)` and a null anchor and
  return type. The failure counts against the decode-failure budget. This
  applies to P/Invoke, async, and pointer rows alike.
- **The pointer probe fails** with a malformed signature. The method gets no
  pointer row, and a `BadImageFormatException` counts against the
  decode-failure budget. Legacy also swallows any other exception from the
  probe silently. The analyzer keeps the same rows, but records the failure
  in the receipt as a diagnostic, so it is visible without changing output.

## Queries and demand

The queries live in host-neutral `DotnetInspector.Queries`, beside
`UnsafeEvidencePresenceQuery`:

- **One query per analyzer:** P/Invoke, async, and pointer signature. Each is
  parameterized by its closing (Rows, Count, or Exists) and returns a typed
  result for that closing, or the typed critical failure.
- **Rows order is a typed request parameter,** not a host sort. Today's
  outputs use two orders, and a query offers both:
  - **Model order:** declaring type, then method name, using today's
    comparers. Async rows are sorted by kind first, compared ordinally. The
    JSON and model outputs use it.
  - **Display order:** declaring type, then method name and signature. The
    Markdown Async Methods view uses it today
    (`LibraryInspectionView.cs`).

  A host names the order it shows, and the query returns rows in that order.
  No host sorts or projects rows itself.
- **One combined request** runs every requested analyzer and closing in one
  execution, with each consumer's own closing. Only the Finding asks for
  Rows. The merged rows, in legacy order, and the Finding inspection built
  from them come only when the Finding is requested. Signals and LibraryInfo
  counts get Count and Exists closings, matching what each shows today
  (`AuditSignalBuilder` shows counts for pointer and P/Invoke, and the async
  kinds present), which declare no `IdentityText`, so
  they spend no identity budget. No host merges results by hand.

Hosts only bind. A section registers the query and the closing it shows.
`LibraryInspection` maps typed results into its model, with no splitting,
merging, sorting, or counting logic of its own. Counts come from Count
closings, or from the combined result's counts, never from counting rows in
the host.

Each consumer asks only for what it shows:

| Consumer | Asks for |
| --- | --- |
| Async Methods section | async rows, or Count for `--count` |
| P/Invoke Methods section | P/Invoke rows |
| Pointer-signature method list (`UnsafeMethods`) | pointer rows |
| Signals | Count for pointer ("public pointer signatures"); Count for P/Invoke when the metadata-wide P/Invoke count is unavailable; Exists for each async kind |
| LibraryInfo counts | Count for each analyzer |
| Classified-method Finding | Rows of all three, merged |

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

- **Consumer output equivalence.** A request for Signals alone gives the same
  numeric pointer and P/Invoke counts, and the same async kind, as legacy.
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
  attribute types that are defined, referenced, nested, and reached through a
  generic `TypeSpec` parent.
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
- **Postcard.** Old, NLinq, and Planner over the async question, against the
  NLinq fixture of
  [#8745](https://github.com/richlander/dotnet-inspect/issues/8745). The
  recorded 1.19–1.69× came from an experiment that treated async and pointer
  as exclusive and skipped projection and budgets, so it is re-measured.
