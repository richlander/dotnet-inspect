# Source view cardinality and physical artifact association

## Status and purpose

This document owns the Source-result composition tracked by
[issue #8281](https://github.com/richlander/dotnet-inspect/issues/8281).
The host-neutral line-operation composition is implemented under
[issue #8723](https://github.com/richlander/dotnet-inspect/issues/8723).
It defines the third production cardinality reference: one resolved Source
target view carries scalar identity and provenance facts beside one ordered
`Lines` inventory that can require continuation. An authored view separately
retains the physical source artifact from which it was selected.

The first production demo is:

```text
type Npgsql.NpgsqlConnection --package Npgsql@10.0.0 -S Source
```

The default CLI result remains the complete selected Source view. Inspect Web
progressively requests line segments instead of receiving the complete view
before the viewer can open.

## Authority and exact claim

A successful exact type or member Source operation publishes one immutable
decoded target view in `InspectionEnvelope<TContent>`. The content has:

- scalar view facts: kind, provider, target identity, provenance, physical
  artifact association, fallback and mapping evidence when applicable,
  language, and one owner-issued view binding; and
- one ordered `Lines` row set whose row unit is one exact source line in that
  view's UTF-16 coordinate space.

Rows and Count observe `Lines` only. View facts, characters, checksum
bytes, JSON properties, transport pieces, rendered fragments, and physical
source batches are never rows.

The resolved Source section is therefore inventory-shaped for cardinality
terminals even though its result also carries scalar view facts. Missing,
rejected, checksum-invalid, content-incomplete, or otherwise unsuccessful
source does not become an empty line inventory. A complete selected document
remains successful when type-document mapping evidence says the type itself is
partial; that partiality is a scalar view fact, not incomplete content.

This owner defines the view/artifact distinction, view/line association, line
identity, view binding, exact Count requirement, and Source-owned continuation
compatibility.
It does not redefine:

- the shared decoded-text document, segment, pull, line-limit failure, or
  continuation mechanics owned by
  [Decoded text document](decoded-text-document.md) and tracked through
  [issue #8319](https://github.com/richlander/dotnet-inspect/issues/8319);
- authored preference, decompiled fallback, checksum verification, or
  acquisition failure, which remain owned by
  [Source Finding producers](source-finding-producers.md) and the existing
  [type](type-source-acquisition.md) and member Source operations;
- general scalar/inventory declarations, owned by
  [Section cardinality](section-cardinality.md);
- semantic selection, terminal resolution, and opaque continuation
  composition, owned by
  [Query-space composition](query-space-composition.md); or
- CLI and Browser presentation policy.

## Conventional basis and deliberate boundary

The line model follows editor and compiler source-text coordinates rather than
`TextReader.ReadLine`:

- .NET and JavaScript strings both use decoded UTF-16 code-unit offsets.
- Roslyn `SourceText.Lines` is analogous evidence for retaining line starts,
  line-break spans, and a final empty coordinate line after a trailing
  terminator. This project does not take a Roslyn dependency.
- Existing `CSharpText.CSharpSourceText` recognizes C# line terminators, while
  `CSharpAnnotatedSourceProjection` already preserves exact terminator code
  units during structural projection.

`TextReader.ReadLine` is not the model because it discards terminators and
cannot reconstruct the exact decoded document. HTTP ranges and SourceLink
transport chunks are not the model because byte chunks do not define semantic
source lines.

The deliberate restriction is that the line inventory describes the decoded
view published by the Source operation, not the original encoded file or an
associated artifact that differs from the view. PDB checksum evidence continues
to describe the acquired authored bytes. This contract does not claim to
preserve an original encoding, byte-order mark, or byte-for-byte source
representation after decoding.

## Target views and physical artifacts

The Source result is a target-oriented view, not necessarily a physical source
file. Its kind is one of:

| View kind | Published content | Physical artifact association |
| --- | --- | --- |
| `AuthoredWholeDocument` | The complete decoded checksum-verified PDB document selected for an exact type request. | Required. The view and artifact have the same decoded content. |
| `AuthoredDeclarationExcerpt` | The declaration excerpt selected for an exact member request, including the existing dedenting and line-ending projection. | Required. The origin retains the PDB member mapping, selected document, and checksum verdict, but the view is not the artifact and does not use artifact coordinates. |
| `DecompiledType` | The reconstructed Source result for one exact type request. | None. |
| `DecompiledMember` | The reconstructed Source result for one exact member request. | None. |

A physical artifact is identified by SourceLink's owner-issued document
observation together with the accepted checksum verdict and acquisition
provenance. The Source view does not infer artifact identity from a URL,
display path, view text, or target name.

An authored declaration excerpt is derived from its artifact, but ordinary
member Source does not publish an exact UTF-16 artifact span. Its current
PDB mapping supplies physical line evidence and CSharpText supplies the
normalized declaration view. The separately owned exact member-parts operation
may publish artifact spans for consumers that request that operation; this
contract does not reconstruct or imply one.

For a partial type, `AuthoredWholeDocument` names one selected physical
document. Additional mapped documents remain typed navigation evidence. They
are not concatenated into the view, and their lines do not enter its Count.
Decompiled Type Source instead remains one reconstructed exact-type view.

## View facts and binding

The shared host-neutral view retains the facts currently projected
separately into CLI `CliSourceDocument` and Browser `BrowserSource`:

- view kind;
- provider (`pdb` or `decompiled`);
- provenance and authoritative source location when available;
- the authored-attempt limitation or fallback reason;
- type-document mapping evidence when the source owner supplies it;
- language;
- the exact decoded view binding; and
- the line result for the requested terminal and execution bound.

`InspectionEnvelope<TContent>` remains outside that content and preserves
Share and diagnostics unchanged.

The view binding is an opaque owner-issued value. It binds the exact decoded
view text, view kind, provider, target identity, and source-producing request,
including decompiler style when decompilation supplied the text. For authored
source, the typed origin separately retains association with the existing
checksum-verified physical artifact; the view binding does not replace or
reinterpret the PDB checksum.

Hosts do not construct the binding from display text, URLs, line rows, a hash
they choose, or a sample of the view. Equal text from two independently
resolved Source operations does not establish equal view identity.

## Ordered Lines inventory

One line row carries:

- its one-based line number;
- its zero-based UTF-16 start offset;
- content excluding the line terminator; and
- the exact existing terminator: CRLF, CR, LF, NEL, line separator, paragraph
  separator, or none.

The line identity is the pair of view binding and one-based line number. Rows
are ordered by line number and cover the decoded view without gaps or overlap.
Concatenating each row's content and terminator reconstructs the exact decoded
view text. It reconstructs a physical artifact only for
`AuthoredWholeDocument`.

The coordinate model includes an empty final line after a trailing terminator.
An empty document has one empty line with start zero and no terminator. These
rules retain a usable editor coordinate space and match the existing
`CSharpSourceText` convention.

Count is the exact number of rows under these rules. A producer that has not
proved the complete decoded document cannot return an exact Count, exhaustion,
or successful complete inventory. Count therefore remains unknown during
ordinary progressive delivery until the producer proves exhaustion; a host
must not infer it from segment size, mounted rows, or continuation presence.

## Rows, Count, and continuation

Rows and Count use the same Source request, content binding, ordered line
population, and semantic selection.

`SourceViewLineOperation` retains one completed
`InspectionEnvelope<SourceView>`, its `DecodedTextDocument`, the next
`DecodedTextPosition`, and one
`QueryOverflowExecution<SourceViewLine>`. It does not add retained execution
state to `SourceHouseReceipt`, which remains resource-free settlement
evidence.

`SourceViewLineVocabulary` owns the executable `Number`, `Start`, `Content`,
and `Terminator` row keys. The operation accepts only a resolved plan issued
by that vocabulary. QueryOverflow admission runs before any decoded line is
pulled, so an unsupported plan declines without line work.

The shared decoded-text substrate makes Rows pull-driven: one consumer
execution requests the next bounded segment and no later line rows are
projected or transferred until another execution resumes the result. For
Source, a normal segment contains at most:

- 256 complete line rows;
- 32,768 UTF-16 code units of row text, including exact terminators; and
- 65,536 JSON-encoded UTF-8 bytes of row text for the Browser transport.

The JSON bound covers encoded row text only. Property names, line-number and
offset fields, arrays, scalar view facts, diagnostics, and envelope framing are
additional transport overhead. These limits are execution policy rather than
portable query meaning or a caller-selectable page size. They cap work even
when unusually long lines make the row limit ineffective.

The complete next row is included only when all applicable bounds remain
satisfied. If one untrusted line exceeds a content bound by itself, the shared
substrate fails visibly without returning a partial row or advancing the
source position. Source reports that typed failure instead of weakening its
execution policy. Every successful segment contains only complete semantic
rows.

One Rows execution may return its bounded segment plus a Source-compatible
continuation. The continuation is an opaque receipt bound to:

- the view binding;
- the unchanged Source-producing request;
- the unchanged line row intent and semantic selection; and
- the next unconsumed position in the ordered line population.

While valid, the continuation is snapshot-stable: every resume observes the
same immutable decoded view and line-coordinate space. It may expire and
is not portable across a Worker or process lifetime unless a later
Source-owned interchange contract says otherwise. It contains no credential
or reusable package, repository, or network authority; a resume supplies
current authority through the ordinary Source path.

A continuation does not prove incomplete semantic selection, population
exhaustion, or exact Count. Different physical segment sizes preserve the same
rows, order, Count, completion, and continuation meaning.

An incompatible request, binding, semantic selection, generation, or expired
receipt fails visibly. The operation never silently restarts at line one or
resumes against newly acquired content. Its process-local continuation binds
the Source view binding to one operation identity and generation; the retained
operation binds those values to the unchanged request, decoded position, and
resolved row plan.

Source composes its continuation from the decoded-text position and
QueryOverflow checkpoint without exposing either as a second host receipt.
Source does not independently implement generic segment accounting.
Pull-driven Rows does not imply that checksum verification, source acquisition,
or decompilation can publish an unsettled view; those operations may complete
before the first row becomes available.

For each pull, QueryOverflow first selects the maximum candidate-row demand
from Source's ceiling, host final-row credit, and semantic Head remainder.
Source then pulls complete lines under all three Source limits, projects only
that batch, and advances QueryOverflow. The decoded position commits only the
consumed prefix. Current admitted plans consume the whole requested batch;
the composition still recovers the exact shorter position if a later admitted
plan consumes only a prefix.

`SourceView.Lines` remains a one-time complete compatibility materialization
for old hosts. Constructing the view no longer materializes that array, and
`SourceViewLineOperation` never reads it.

## Acquisition, provider, and failure preservation

The existing authored-first/decompiled-fallback operations remain the source
of provider choice and failure evidence. One host-neutral projection adapts
their successful available branch into the document/line result.

The projection must not:

- catch checksum, mapping, acquisition, decompilation, cancellation, cleanup,
  or binding-policy failures and substitute empty or partial lines;
- erase an unsuccessful authored attempt when decompilation supplies content;
- infer source identity from a rendered URL or provenance sentence;
- change exact type/member selection or partial-type document scope; or
- make a bounded line segment appear to be the complete view.

Authored, decompiled fallback, unavailable, checksum-failure, and
content-incomplete outcomes retain their current visible behavior. A complete
selected authored document retains successful line results and its existing
partial-type mapping evidence. Member-part spans and other adjacent typed
annotations remain with their owners and may reference an authored artifact or
Source view only through the applicable owner-issued identity, binding, and
coordinate space.

## Host adoption and rendering

The shared operation and view/line model are host-neutral and remain SRM-only,
NativeAOT-friendly, Roslyn-free, and usable on single-threaded Browser/Wasm.

CLI adoption:

1. `type ... -S Source` and `member ... -S Source` consume the shared view
   operation.
2. Ordinary output repeatedly executes Rows until the line population is
   exhausted, then concatenates exact content and terminators. Native output
   remains the default; explicit Markdown continues through the existing
   Markout code-document lowering.
3. `--count` reports exact line Count. Semantic `--rows` selects line rows;
   rendered `-n` remains a separate presentation limit.
4. The current host-local `CliSourceDocument` projection retires after existing
   direct JSON, notes, printable-document, and failure behavior is preserved.

Inspect Web adoption:

1. Type and member Source use the same shared view operation and envelope.
2. The Worker returns scalar view facts and the first bounded line segment;
   the viewer requests later segments with the Source-owned continuation.
3. The Browser never treats callback credit, mounted DOM rows, or received
   segments as exact Count or completion.
4. Authored Source and Decompiled Source remain independently selectable and
   lazy. Selecting authored Source by default does not hide, precompute, or
   replace the exact-type decompiled view.
5. The current full-text `BrowserSource` transport retires only after the
   progressive viewer preserves provenance, errors, cancellation,
   supersession, navigation, and accessibility behavior.

The line model is structured input to both hosts. It does not make a table the
default Source presentation. Markout remains the CLI's multi-format document
renderer; the Browser viewer owns interactive virtualization and line mounting
after receiving typed rows.

## Real asset and pathological cases

The motivating asset is
`Npgsql@10.0.0`, type `Npgsql.NpgsqlConnection`, mapped by its Portable PDB to:

```text
https://raw.githubusercontent.com/npgsql/npgsql/a18021849f244716d3b68eefd705677f131f9ace/src/Npgsql/NpgsqlConnection.cs
```

The checksum-verified decoded document measured during design has 87,069 UTF-16
code units and 2,038 line rows: 2,037 LF-terminated rows plus one final empty
line. A 256-line execution bound deterministically requires eight successful
Rows executions, so the ordinary CLI and Browser scenarios cannot pass through
a one-shot-only implementation.

The pinned authored-source corpus provides the broader execution-policy
evidence. `tools/SourceCorpusCensus` deduplicated 24,005 harvested corpus rows
by SourceLink URL plus checksum identity, then fetched and verified all 3,334
unique physical documents through the product checksum and decoding paths.
The population contained 995,925 lines with zero retrieval or checksum
failures. Document p95 was 42,758 bytes and 978 lines; p99 was 136,449 bytes
and 2,826 lines; the maximum was 1,841,248 bytes and 38,928 lines. The longest
observed line was 1,047 UTF-16 code units and 1,291 JSON-encoded row-text
bytes.

At the selected 256-row, 32,768-UTF-16, and 65,536-JSON-byte bounds, 855 of
3,334 documents required continuation. The population produced 5,935 total
segments; p95 was four segments per document and the maximum was 153. No
observed line exceeded either content bound. Doubling both content bounds
saved one segment across the population, so the 256-row bound dominates
ordinary files while the content bounds reject unsupported pathological
lines.

A separate exact PDB census reproduced the pinned 104-assembly broad package
pool. Seventy-seven assemblies supplied Portable PDBs and 76 supplied
SourceLink maps. The census inspected 32,992 TypeDefs. Of 27,486 types with
correlated document mappings, 759 mapped to multiple physical documents.
Restricting the population to 19,659 source-spellable exact type names, 703
(3.58%) mapped to multiple documents; p95 was one document, p99 two, and the
maximum 83. The 27 assemblies without available Portable PDBs are explicit
coverage gaps and do not enter the mapping denominator. This evidence supports
independent per-document navigation rather than concatenating a partial type's
documents.

These observations are exact for the pinned populations, not claims about
every SourceLink document or package. The authored-source corpus is biased
toward documents containing harvested eligible methods. Reproduce both
reports with the commands in
[`tools/SourceCorpusCensus/README.md`](../../tools/SourceCorpusCensus/README.md).

The preserved production gate uses package and PDB acquisition rather than
checking a repository copy into the fixture tree. Focused synthetic fixtures
supplement it with CRLF, CR, NEL, line separator, paragraph separator, no final
terminator, a final terminator, an empty document, an over-bound line, stale
continuation, and incompatible-request cases.

### Small-document composition cost

[`tools/SourceLineOperationBenchmark.cs`](../../tools/SourceLineOperationBenchmark.cs)
preserves an observational post-settlement comparison. It constructs settled
Source views outside the measured interval and compares complete compatibility
materialization through `SourceView.Lines` with Source line-operation
admission, one or more complete pulls, and QueryOverflow row snapshots. Query
resolution is outside both intervals. Every sample consumes an identical
semantic checksum.

The local comparison used 31 fresh-process median samples per cell, no warmup,
1-, 10-, and 100-line ASCII documents, and the first 1, 2, 5, or 10
operations. The endpoint results summarize the complete matrix:

| Runtime | Lines | First operation ratio | First 10 operations ratio | First-operation allocation delta | 10-operation allocation delta |
| --- | ---: | ---: | ---: | ---: | ---: |
| CoreCLR | 1 | 1.466 | 1.469 | +568 B | +4,960 B |
| CoreCLR | 10 | 1.430 | 1.463 | +712 B | +6,400 B |
| CoreCLR | 100 | 1.450 | 1.456 | +2,152 B | +20,800 B |
| NativeAOT | 1 | 1.029 | 1.045 | +856 B | +5,248 B |
| NativeAOT | 10 | 1.037 | 1.064 | +1,000 B | +6,688 B |
| NativeAOT | 100 | 1.035 | 1.058 | +2,440 B | +21,088 B |

Across all 12 cells, the CoreCLR ratio was 1.414-1.485 and the NativeAOT
ratio was 1.028-1.064. CoreCLR direct totals were approximately 3.9-4.4 ms
and composed totals 5.6-6.4 ms from the first through tenth operation, so
one-time JIT surface dominated the measured difference. NativeAOT shows the
smaller recurring execution premium after ahead-of-time compilation.
Allocation deltas grow with executions and rows because QueryOverflow retains
detached execution/request state and snapshots published rows.

Reproduce one process sample with:

```bash
dotnet run tools/SourceLineOperationBenchmark.cs -c Release -- \
  direct 10 1
dotnet run tools/SourceLineOperationBenchmark.cs -c Release --no-build -- \
  overflow 10 1
dotnet publish tools/SourceLineOperationBenchmark.cs -c Release \
  -o /tmp/source-line-operation-benchmark
/tmp/source-line-operation-benchmark/SourceLineOperationBenchmark \
  direct 10 1
```

Each output row is elapsed Stopwatch ticks, Stopwatch frequency, allocated
bytes on the current thread, and semantic checksum. These measurements explain
the selected composition's cost; they are not production latency thresholds
or cross-platform performance claims. Browser/Wasm composition cost remains
unmeasured until the Browser adoption slice.

## Focused delivery sequence

Implementation proceeds through focused slices:

1. Lock this Source view, physical-artifact association, line identity,
   view-binding, execution-policy, and host-adoption contract, with a
   reproducible observational census.
2. [Decoded text document](decoded-text-document.md) adds the shared immutable
   decoded-text document, bounded pull, source-local position, and visible
   line-limit failure under #8319.
3. [Issue #8723](https://github.com/richlander/dotnet-inspect/issues/8723)
   composes the existing host-neutral Source view/artifact model and completed
   type and member Source envelopes with that shared substrate. It gates exact
   reconstruction, Source request compatibility, and failure preservation
   without changing hosts.
4. Adopt the composed operation in CLI type and member `Source`, preserve complete
   default output, and retire the host-local source projection.
5. Adopt the same operation in Inspect Web's type and member Source viewer,
   preserve the shared envelope and independent Decompiled Source selection,
   and retire the full-text Browser transport.
6. Preserve the real Npgsql asset in CLI and published Browser/Wasm gates, each
   requiring at least one continuation resume.

Each slice remains independently coherent and keeps the direct path to both
production hosts. This sequence does not authorize a broad rewrite of Source
acquisition, Query Space, or the viewer.

## Required evidence

| Gate | Property | Status |
| --- | --- | --- |
| `SourceViewLinesReconstructExactDecodedText` | Every supported terminator, empty/final-empty line, line number, and UTF-16 start offset reconstruct the exact decoded view text. | Verified in Release by `SourceViewInspectionTests`. |
| `SourceViewProjectionDistinguishesArtifactsAndPreservesEvidence` | The four view kinds are explicit; authored origins retain their physical artifact and member mapping; decompiled origins have no artifact; a declaration excerpt does not masquerade as its physical file; PDB/decompiled success and non-success preserve facts, Share, diagnostics, mapping, and typed failures. | Verified in Release by `SourceViewInspectionTests`. |
| `SourceLineOperationPreservesMeaningAcrossPullBounds` | Exact Count equals the completely drained ordered line population; different delivery bounds preserve lines, order, completion, reconstruction, and continuation meaning under one content binding. | Verified in Release by `SourceViewLineOperationTests`. |
| `SourceLineSegmentsRespectExecutionBounds` | Successful pulls stay within 256 rows, 32,768 UTF-16 row-text units, and 65,536 JSON-encoded row-text bytes; an individually over-bound line fails visibly without returning a partial row or advancing its position. | Verified in Release by `SourceViewLineOperationTests`. |
| `SourceLineContinuationRejectsIncompatibleUse` | Stale, terminal, different-binding, and different-selection process-local receipts fail without restarting. | Verified in Release by `SourceViewLineOperationTests`; cross-process expiry remains outside this slice. |
| `SourceLineHeadPullsOnlyTheRequiredPrefix` | Head completes after its exact required candidate prefix without scanning or publishing later Source lines. | Verified in Release by `SourceViewLineOperationTests`. |
| `SourceLineOperationDeclinesAndCancelsVisibly` | Unsupported plans decline before line work, and cancellation remains a visible terminal Source operation state. | Verified in Release by `SourceViewLineOperationTests`. |
| `CliSourceDrainsContinuationWithoutChangingOutput` | CLI default output equals the pre-adoption decoded document, while Count and Rows observe source lines. | Unverified until slice 4. |
| `BrowserSourceRequestsContinuedLines` | Published Browser/Wasm obtains the same envelope and document facts, requests later line segments instead of receiving complete text first, and keeps authored and decompiled views independently selectable and lazy. | Unverified until slice 5. |
| `NpgsqlConnectionSourceRequiresContinuation` | The real checksum-verified Npgsql document requires and resumes at least one continuation in both production hosts. | Unverified until slice 6. |

All correctness gates run in Release. A source-fetch measurement or one-shot
timing is design evidence, not a runtime performance proof.

## Non-claims

This contract does not claim:

- preservation of original encoded bytes, encoding, or byte-order mark;
- that line continuation avoids complete source acquisition inside the engine;
- that exact line Count is known before complete decoded-view exhaustion;
- random line seeking or portable continuation receipts;
- that exact Count is cheaper than splitting the complete decoded text;
- that every Source view is a physical source artifact;
- an exact physical-artifact span for the normalized authored member excerpt;
- concatenation of partial-type documents into one Source view;
- that every source-related section shares this line population;
- that member parts, annotations, Findings, or diff rows are Source lines;
- that Browser virtualization policy belongs in the shared operation; or
- that equal source text establishes identity across operations.
