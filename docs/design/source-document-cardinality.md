# Source document cardinality

## Status and purpose

This document owns the Source-result composition tracked by
[issue #8281](https://github.com/richlander/dotnet-inspect/issues/8281).
It defines the third production cardinality reference: one resolved Source
document carries scalar identity and provenance facts beside one ordered
`Lines` inventory that can require continuation.

The first production demo is:

```text
type Npgsql.NpgsqlConnection --package Npgsql@10.0.0 -S Source
```

The default CLI result remains the complete source document. Inspect Web
progressively requests line segments instead of receiving the complete text
before the viewer can open.

## Authority and exact claim

A successful exact type or member Source operation publishes one immutable
decoded document in `InspectionEnvelope<TContent>`. The content has:

- scalar document facts: provider, source identity, provenance, location,
  fallback and mapping evidence when applicable, language, and one
  owner-issued content binding; and
- one ordered `Lines` row set whose row unit is one exact source line in that
  document's UTF-16 coordinate space.

Rows and Count observe `Lines` only. Document facts, characters, checksum
bytes, JSON properties, transport pieces, rendered fragments, and physical
source batches are never rows.

The resolved Source section is therefore inventory-shaped for cardinality
terminals even though its result also carries scalar document facts. Missing,
rejected, checksum-invalid, content-incomplete, or otherwise unsuccessful
source does not become an empty line inventory. A complete selected document
remains successful when type-document mapping evidence says the type itself is
partial; that partiality is a scalar document fact, not incomplete content.

This owner defines the document/line association, line identity, content
binding, exact Count requirement, and Source-owned continuation compatibility.
It does not redefine:

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
document published by the Source operation, not the original encoded file.
PDB checksum evidence continues to describe the acquired authored bytes. This
contract does not claim to preserve an original encoding, byte-order mark, or
byte-for-byte source representation after decoding.

## Document facts and content binding

The shared host-neutral content retains the facts currently projected
separately into CLI `CliSourceDocument` and Browser `BrowserSource`:

- provider (`pdb` or `decompiled`);
- provenance and authoritative source location when available;
- the authored-attempt limitation or fallback reason;
- type-document mapping evidence when the source owner supplies it;
- language;
- the exact decoded content binding; and
- the line result for the requested terminal and execution bound.

`InspectionEnvelope<TContent>` remains outside that content and preserves
Share and diagnostics unchanged.

The content binding is an opaque owner-issued value. It binds the exact decoded
text, provider, source identity, and source-producing request, including
decompiler style when decompilation supplied the text. For PDB source, it
retains the association with the existing checksum-verified document; it does
not replace or reinterpret the PDB checksum.

Hosts do not construct the binding from display text, URLs, line rows, a hash
they choose, or a sample of the document. Equal text from two independently
resolved Source operations does not establish equal identity.

## Ordered Lines inventory

One line row carries:

- its one-based line number;
- its zero-based UTF-16 start offset;
- content excluding the line terminator; and
- the exact existing terminator: CRLF, CR, LF, NEL, line separator, paragraph
  separator, or none.

The line identity is the pair of content binding and one-based line number.
Rows are ordered by line number and cover the decoded document without gaps or
overlap. Concatenating each row's content and terminator reconstructs the exact
decoded text.

The coordinate model includes an empty final line after a trailing terminator.
An empty document has one empty line with start zero and no terminator. These
rules retain a usable editor coordinate space and match the existing
`CSharpSourceText` convention.

Count is the exact number of rows under these rules. A producer that has not
proved the complete decoded document cannot return an exact Count, exhaustion,
or successful complete inventory.

## Rows, Count, and continuation

Rows and Count use the same Source request, content binding, ordered line
population, and semantic selection.

One Rows execution may return a bounded segment plus a Source-owned
continuation. The continuation is an opaque receipt bound to:

- the content binding;
- the unchanged Source-producing request;
- the unchanged line row intent and semantic selection; and
- the next unconsumed position in the ordered line population.

While valid, the continuation is snapshot-stable: every resume observes the
same immutable decoded document and line-coordinate space. It may expire and
is not portable across a Worker or process lifetime unless a later
Source-owned interchange contract says otherwise. It contains no credential
or reusable package, repository, or network authority; a resume supplies
current authority through the ordinary Source path.

A continuation does not prove incomplete semantic selection, population
exhaustion, or exact Count. Different physical segment sizes preserve the same
rows, order, Count, completion, and continuation meaning.

An incompatible request, binding, semantic selection, or expired receipt fails
visibly. The operation never silently restarts at line one or resumes against
newly acquired content.

The initial production execution bound is 256 line rows. It is execution
policy, not portable query meaning or a new page-size concept.

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
- make a bounded line segment appear to be the complete document.

Authored, decompiled fallback, unavailable, checksum-failure, and
content-incomplete outcomes retain their current visible behavior. A complete
selected authored document retains successful line results and its existing
partial-type mapping evidence. Member-part spans and other adjacent typed
annotations remain with their owners and may reference this document only
through the owner-issued content binding and coordinates.

## Host adoption and rendering

The shared operation and line model are host-neutral and remain SRM-only,
NativeAOT-friendly, Roslyn-free, and usable on single-threaded Browser/Wasm.

CLI adoption:

1. `type ... -S Source` and `member ... -S Source` consume the shared document
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

1. Type and member Source use the same shared document operation and envelope.
2. The Worker returns scalar document facts and the first bounded line segment;
   the viewer requests later segments with the Source-owned continuation.
3. The Browser never treats callback credit, mounted DOM rows, or received
   segments as exact Count or completion.
4. The current full-text `BrowserSource` transport retires only after the
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

The preserved production gate uses package and PDB acquisition rather than
checking a repository copy into the fixture tree. Focused synthetic fixtures
supplement it with CRLF, CR, NEL, line separator, paragraph separator, no final
terminator, a final terminator, an empty document, stale continuation, and
incompatible-request cases.

## Focused delivery sequence

Implementation proceeds through focused slices:

1. Lock this Source document, line identity, content-binding, continuation, and
   host-adoption contract.
2. Add the immutable host-neutral document/line types and projection over the
   existing completed type and member Source envelopes. Gate exact
   reconstruction and failure preservation without changing hosts.
3. Add bounded line execution and the Source-owned opaque continuation,
   composing Query Space's continuation contract without treating physical
   segments as pages.
4. Adopt the shared operation in CLI type and member `Source`, preserve complete
   default output, and retire the host-local source projection.
5. Adopt the same operation in Inspect Web's type and member Source viewer,
   preserve the shared envelope, and retire the full-text Browser transport.
6. Preserve the real Npgsql asset in CLI and published Browser/Wasm gates, each
   requiring at least one continuation resume.

Each slice remains independently coherent and keeps the direct path to both
production hosts. This sequence does not authorize a broad rewrite of Source
acquisition, Query Space, or the viewer.

## Required evidence

| Gate | Property | Status |
| --- | --- | --- |
| `SourceDocumentLinesReconstructExactDecodedText` | Every supported terminator, empty/final-empty line, line number, and UTF-16 start offset reconstruct the exact decoded text. | Unverified until slice 2. |
| `SourceDocumentProjectionPreservesProviderAndFailureEvidence` | PDB and decompiled success preserve facts, Share, diagnostics, and successful partial-type mapping evidence; unavailable, checksum, incomplete-content, cancellation, and cleanup outcomes do not become successful line results. | Unverified until slice 2. |
| `SourceLineCountMatchesCompletelyDrainedRows` | Exact Count equals the completely drained ordered line population under one content binding. | Unverified until slice 3. |
| `SourceLineSegmentSizeDoesNotChangeMeaning` | Different execution bounds preserve lines, order, Count, completion, reconstruction, and continuation meaning. | Unverified until slice 3. |
| `SourceLineContinuationRejectsIncompatibleBinding` | Stale, expired, different-document, different-request, and different-selection receipts fail without restarting. | Unverified until slice 3. |
| `CliSourceDrainsContinuationWithoutChangingOutput` | CLI default output equals the pre-adoption decoded document, while Count and Rows observe source lines. | Unverified until slice 4. |
| `BrowserSourceRequestsContinuedLines` | Published Browser/Wasm obtains the same envelope and document facts, then requests later line segments instead of receiving complete text first. | Unverified until slice 5. |
| `NpgsqlConnectionSourceRequiresContinuation` | The real checksum-verified Npgsql document requires and resumes at least one continuation in both production hosts. | Unverified until slice 6. |

All correctness gates run in Release. A source-fetch measurement or one-shot
timing is design evidence, not a runtime performance proof.

## Non-claims

This contract does not claim:

- preservation of original encoded bytes, encoding, or byte-order mark;
- that line continuation avoids complete source acquisition inside the engine;
- random line seeking or portable continuation receipts;
- that exact Count is cheaper than splitting the complete decoded text;
- that every source-related section shares this line population;
- that member parts, annotations, Findings, or diff rows are Source lines;
- that Browser virtualization policy belongs in the shared operation; or
- that equal source text establishes identity across operations.
