# Decoded text document

## Status

Focused design and implementation for
[#8640](https://github.com/richlander/dotnet-inspect/issues/8640), the first
delivery slice under
[#8319](https://github.com/richlander/dotnet-inspect/issues/8319).

`Inspector.Text` implements the immutable document, exact line, bounded pull,
allocation-conscious forward cursor, source-local position, and line-limit
failure contracts. The existing Source view projection is the first adopter:
it now uses this substrate for its complete line inventory instead of
maintaining a second line parser.

The Source owner now selects a House-only adaptive Complete/Cold Pull model in
[Source view cardinality](source-document-cardinality.md). Demand-aware Source
execution, host-visible receipts, CLI draining, Browser delivery, and package
README adoption remain unverified under
[#8766](https://github.com/richlander/dotnet-inspect/issues/8766).

## Owner and exact claim

**Decoded text document** owns this exact claim:

> One immutable decoded .NET string can be projected as an exact ordered line
> population through restartable bounded pulls or allocation-conscious forward
> traversal without first constructing the complete line inventory. Both forms
> reconstruct the same decoded string, line coordinates, terminators, and exact
> terminal Count.

The owner defines:

- the decoded line and terminator model;
- source-local positions within one immutable document instance;
- a document-bound value cursor and borrowed line slice for forward execution
  without per-line heap objects;
- positive hard maxima for candidate rows, UTF-16 row text, and
  JSON-encoded UTF-8 row text;
- batches that satisfy every maximum;
- visible failure when one complete line exceeds a content maximum; and
- exact line Count disclosure only when the document is exhausted.

The owner does not define:

- byte acquisition, decoding, checksum verification, media type, provenance,
  or source authority;
- Source, package, PDB, decompiler, language, or Markdown semantics;
- QuerySpace selection, cross-batch checkpoints, or terminal meaning;
- asynchronous I/O, cancellation, resource leases, or operation lifetime;
- host-visible continuation receipts, delivery credit, serialization
  envelopes, rendering, or Browser virtualization; or
- original encoding, byte-order marks, or exact encoded bytes.

## Conventional basis

The line model follows editor and compiler coordinate systems rather than
`TextReader.ReadLine`: content excludes its terminator, while the exact
terminator and UTF-16 start remain available. CRLF is one terminator. Empty
text has one empty coordinate line, and a trailing terminator creates one
final empty coordinate line.

The pull contract follows `Stream.Read` and `PipeReader`: the caller supplies
capacity, a result may be shorter than requested without implying exhaustion,
and the next source position advances only across returned values. Unlike
those byte APIs, this source returns semantic line rows.

The `wordcount` experiments in
[`richlander/convenience` at
`9eca1355`](https://github.com/richlander/convenience/tree/9eca1355fd0d45275780d6dc34fa9bff932e39ad/wordcount/wordcount)
provide analogous implementation evidence for using
`SearchValues<char>` and `IndexOfAny` to skip non-boundary text. Only that
platform search technique transfers. The experiments' byte decoder, buffer
carry, whitespace/word semantics, and LF-only line count do not satisfy this
owner's immutable-string, exact-terminator contract, and no code is copied.

A fresh-process local comparison used 31 median samples per cell and exercised
actual `DecodedTextDocument` construction and `Pull`, without benchmark
warmup. For one complete pull of the pinned 87,069-UTF-16-unit
`NpgsqlConnection.cs` asset, the `SearchValues<char>` implementation used
90.2% of the scalar implementation's CoreCLR time and 87.6% of its NativeAOT
time. For one future-policy 256-row segment, it used 102.6% on CoreCLR and
92.0% on NativeAOT. A short 16-line, 40-character-per-line document used
105.4% on CoreCLR and 98.8% on NativeAOT. At ten pulls, the search
implementation led both Npgsql shapes on both runtimes; short CoreCLR remained
2.4% slower.

Publishing the actual NativeAOT CLI with each implementation produced
identically sized 127,086,680-byte executables and identical section sizes
because existing product paths already root the platform implementation. These
local results select the line-search strategy for the measured desktop Source
path without claiming a universal latency win.

A separate production Mono/Wasm comparison on `fernie` used the same 31-sample
fresh-process method under Node 24.11. For the first pull, the
SearchValues/scalar ratios were 1.013 for the short document, 0.982 for one
future-policy Npgsql segment, and 1.051 for the complete Npgsql document. At
ten pulls they were 1.022, 0.997, and 0.922 respectively. This does not
establish a first-pull Wasm speedup; it shows parity for the demand-driven
segment and a benefit only after repeated complete-document work.

The actual published Inspect Web variants had identical uncompressed
`dotnet.native.wasm`, `System.Private.CoreLib.wasm`, `Inspector.Text.wasm`, and
other managed Wasm file sizes. Their complete precompressed site directories
differed by 2,110 bytes amid build-wide compressed-file variation, so no
product payload increase was attributable to this use of `SearchValues<char>`.

The deliberate difference from an enumerable is an explicit, document-bound
restart position. No iterator, stream, callback, or borrowed input buffer
crosses a pull boundary. The position is source-local and repeatable; the
outer operation supplies one-shot or expiring host receipt behavior when
needed.

The forward cursor follows the value-reader precedent of
`System.Reflection.Metadata.BlobReader`: one caller-owned value advances over
immutable retained memory without iterator or row-object allocation. Unlike a
metadata blob offset, the cursor remains bound to its issuing document and
advances only through the document owner's exact line grammar.

## Immutable document and source position

Construction accepts one already-decoded immutable `string`. Acquisition and
decoding failures must have been resolved by the caller before construction.
The document retains that string and creates line values as immutable memory
slices over it.

A position contains the next UTF-16 offset and one-based line number plus an
opaque association with the issuing document instance. The same position can
be read repeatedly with the same result. A different document rejects it
instead of restarting at line one or interpreting its numeric coordinates.

The position is not:

- a portable token;
- a host continuation receipt;
- a credential or source authority;
- a cross-batch query checkpoint; or
- proof of source completion or exact Count.

An adopting operation retains the document, source position, resolved query,
and any execution state under its own lifetime and compatibility rules.

## Forward cursor and borrowed line slice

The document also issues one value cursor for allocation-conscious forward
execution. `TryReadLine` advances that caller-owned cursor and returns one
value slice over the document's retained string. It uses the same line grammar
as bounded `Pull`; it does not construct a line object, batch, immutable array,
or next-position object for each candidate.

The cursor is document-bound and exposes exact Count only after it reaches the
final coordinate line. A copied cursor is an independent in-process value, not
a portable checkpoint or host continuation. End of document returns `false`
without changing the completed cursor.

The slice exposes line coordinates, borrowed content, exact terminator, total
UTF-16 row-text length, and the JSON-encoded UTF-8 row-text measure. Computing
the JSON measure remains explicit: consumers that only need line coordinates
or predicate text do not pay that work. A consumer can apply the owner's typed
per-line limit failure to a slice before committing its copied cursor. The
adopting owner applies its own aggregate bounds and terminal closure; the
cursor does not interpret Source segments, QuerySpace selection, delivery
credit, cancellation, or disposal.

## Exact line model

One line carries:

- a one-based number;
- a zero-based UTF-16 start;
- immutable content excluding its terminator; and
- exactly one of CRLF, CR, LF, NEL, line separator, paragraph separator, or no
  terminator.

Concatenating content and terminators reconstructs the decoded string exactly.
Line starts are UTF-16 code-unit coordinates.

The empty string contains one empty line at start zero with no terminator.
A decoded string ending in a terminator contains a final empty line after that
terminator. These are coordinate lines even when another consumer, such as a
Finding census, intentionally defines an empty observation population.

## Pull contract

Each `DecodedTextPullLimits` value supplies three positive hard maxima:

```text
MaximumCandidateRows
MaximumUtf16CodeUnits
MaximumJsonEncodedUtf8Bytes
```

The JSON measure is the number of UTF-8 bytes produced for row text by
`JavaScriptEncoder.Default`, excluding JSON quotes, property names, arrays,
scalar facts, diagnostics, and envelope framing. Row text is content plus the
exact terminator.

A normal batch:

- contains at least one complete semantic line;
- contains no more than `MaximumCandidateRows`;
- stays within both aggregate content maxima;
- preserves source order; and
- supplies either the next document-bound position or document completion.

When a later line would exceed an aggregate maximum, the batch ends before
that line. Therefore, a short batch does not imply completion. The caller must
use the explicit completion state.

When the next complete line cannot fit either content maximum by itself, the
pull fails with `DecodedTextLineLimitException`. The failure identifies the
line and both measured and allowed sizes. It does not return a partial row,
advance the source position, or let an outer operation observe the rejected
line. An owner may retry the same position under a different compatible
policy; Source's fixed production policy instead reports the operation
failure.

The candidate-row maximum accepts an outer operation's maximum candidate
demand. The decoded-text owner retains its independent UTF-16 and
serialized-text ceilings. The outer operation does not learn physical text
bounds, and the decoded-text source does not interpret Head, Count, delivery
credit, or query completion.

## Completion and Count

A batch reports document completion only after it contains the final
coordinate line. Only that completed batch exposes exact line Count, equal to
the final line number.

A candidate-row limit, content limit, short batch, line-limit failure, or
continuation does not establish Count. The adopting operation decides whether
the resolved terminal is semantically complete; the decoded-text source
supplies only document exhaustion.

## Ownership and failure

Line content is an immutable memory slice over the document's retained string.
It remains valid after a batch object is released and does not borrow mutable
source buffers.

Invalid limits, a cross-document position, a position outside the document,
an individually over-bound line, invalid decoded text, or integer overflow
fails visibly. The implementation does not return a partial row, successful
empty batch, silently clamp a request, or restart from the beginning.

## First adopter and production path

`SourceViewInspection` now uses an unbounded pull to create its existing
complete `SourceView.Lines` value. This preserves the current public Source
shape while removing its duplicate line grammar. It does not yet claim
progressive Source execution.

The next production step under
[#8766](https://github.com/richlander/dotnet-inspect/issues/8766) implements
the Source-owned adaptive Complete/Cold Pull model and validates it through
Source projection and Browser transport without penalizing small complete
output. The tracker in #8319 owns later Source and package adoption.
Exact-byte CLI destination streaming remains separate under #8303.

## Real asset and pathological case

The motivating production asset is the checksum-verified
`Npgsql@10.0.0` `NpgsqlConnection.cs` document recorded by
[Source view cardinality](source-document-cardinality.md). Its 2,038 line
rows require eight pulls under Source's 256-row ceiling.

The pure substrate additionally gates:

- mixed CRLF, CR, LF, NEL, line-separator, and paragraph-separator input;
- supplementary Unicode scalars;
- empty and final-empty coordinate lines;
- aggregate bounds ending a batch before its candidate-row maximum;
- an individually over-bound line failing visibly; and
- a position used with the wrong document.

## Required evidence

| Gate | Property | Status |
| --- | --- | --- |
| `PullsReconstructExactDecodedText` | Mixed terminators, line numbers, UTF-16 starts, and supplementary scalars reconstruct exactly across bounded pulls. | Verified in Release by `Inspector.Text.Tests`. |
| `EmptyAndFinalEmptyLinesAreCoordinates` | Empty and trailing-terminator documents retain their coordinate lines and exact Count. | Verified in Release by `Inspector.Text.Tests`. |
| `DifferentPullBoundsPreserveRowsAndCompletion` | Accepted pull partitions preserve line values, order, completion, and Count. | Verified in Release by `Inspector.Text.Tests`. |
| `NormalPullsRespectEveryMaximum` | Every successful batch satisfies all three caller maxima. | Verified in Release by `Inspector.Text.Tests`. |
| `LineExceedingPullLimitsFailsVisibly` | A line exceeding either content maximum reports a typed failure with measured and allowed sizes. | Verified in Release by `Inspector.Text.Tests`. |
| `PullPositionIsBoundToOneDocument` | A source position cannot resume a different document. | Verified in Release by `Inspector.Text.Tests`. |
| `ForwardCursorMatchesBoundedPull` | Forward traversal preserves the bounded-pull line values, order, completion, and exact Count. | Verified in Release by `Inspector.Text.Tests`. |
| `ForwardCursorIsBoundToOneDocument` | A value cursor cannot advance through a different document. | Verified in Release by `Inspector.Text.Tests`. |
| `ForwardSliceEnforcesLineLimits` | Borrowed slices report the same typed UTF-16 and JSON line-limit failure for an individually over-bound row. | Verified in Release by `Inspector.Text.Tests`. |
| `ForwardCursorAllocatesNoPerLineObjects` | After warmup, draining through the forward primitive allocates no managed bytes per line. | Verified in Release by `Inspector.Text.Tests`. |
| `SourceViewLinesReconstructExactDecodedText` | The first Source adopter preserves its existing exact line contract through the shared substrate. | Verified in Release by `DotnetInspector.Queries.Tests`. |

All correctness gates run in Release. Production continuation and
host-observable performance remain unverified until the named adoption slices
land.

## Non-claims

This contract does not claim:

- reduced network, archive, or decoding work;
- that a complete decoded string is absent from the engine;
- exact Count before document exhaustion;
- portable or durable continuation;
- random access by line number;
- original-byte reconstruction;
- support for a line exceeding an owner's content ceilings;
- that every text Finding uses this coordinate model; or
- completed CLI or Browser progressive delivery.
