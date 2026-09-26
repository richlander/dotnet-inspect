# Decoded text document

## Status

Focused design and implementation for
[#8640](https://github.com/richlander/dotnet-inspect/issues/8640), the first
delivery slice under
[#8319](https://github.com/richlander/dotnet-inspect/issues/8319).

`Inspector.Text` implements the immutable document, exact line, bounded pull,
source-local position, oversized-line, and fragment contracts. The existing
Source view projection is the first adopter: it now uses this substrate for
its complete line inventory instead of maintaining a second line parser.

SourceHouse continuation, QueryOverflow composition, host-visible receipts,
CLI draining, Browser delivery, and package README adoption remain unverified
until their focused successor slices land.

## Owner and exact claim

**Decoded text document** owns this exact claim:

> One immutable decoded .NET string can be projected as an exact ordered line
> population through restartable bounded pulls without first constructing the
> complete line inventory. Every accepted pull partition reconstructs the same
> decoded string, line coordinates, terminators, and exact terminal Count.

The owner defines:

- the decoded line and terminator model;
- source-local positions within one immutable document instance;
- positive hard maxima for candidate rows, UTF-16 row text, and
  JSON-encoded UTF-8 row text;
- normal batches that satisfy every maximum;
- explicit identification of one individually over-bound semantic line;
- bounded execution-only fragments for that line; and
- exact line Count disclosure only when the document is exhausted.

The owner does not define:

- byte acquisition, decoding, checksum verification, media type, provenance,
  or source authority;
- Source, package, PDB, decompiler, language, or Markdown semantics;
- QuerySpace selection, QueryOverflow checkpoints, or terminal meaning;
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

The deliberate difference from an enumerable is an explicit, document-bound
restart position. No iterator, stream, callback, or borrowed input buffer
crosses a pull boundary. The position is source-local and repeatable; the
outer operation supplies one-shot or expiring host receipt behavior when
needed.

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
- a QueryOverflow checkpoint; or
- proof of source completion or exact Count.

An adopting operation retains the document, source position, resolved query,
and QueryOverflow checkpoint under its own lifetime and compatibility rules.

## Exact line model

One line carries:

- a one-based number;
- a zero-based UTF-16 start;
- immutable content excluding its terminator; and
- exactly one of CRLF, CR, LF, NEL, line separator, paragraph separator, or no
  terminator.

Concatenating content and terminators reconstructs the decoded string exactly.
Line starts and fragment starts are UTF-16 code-unit coordinates. A valid
surrogate pair is never divided between fragments.

The empty string contains one empty line at start zero with no terminator.
A decoded string ending in a terminator contains a final empty line after that
terminator. These are coordinate lines even when another consumer, such as a
Finding census, intentionally defines an empty observation population.

## Pull contract

Each pull request supplies three positive hard maxima:

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

When the next line cannot fit either content maximum by itself, the pull
returns that line alone with `OversizedLine` kind. This is not permission to
publish an over-bound host segment. It lets QueryOverflow still observe one
complete candidate row while the outer delivery operation uses the fragment
contract below.

The candidate-row maximum is designed to accept
`QueryOverflowInputRequest.MaximumCandidateRows`. The decoded-text owner
retains its independent UTF-16 and serialized-text ceilings. QueryOverflow
does not learn physical text bounds, and the decoded-text source does not
interpret Head, Count, delivery credit, or query completion.

## Oversized-line fragments

An individually over-bound line can be read as a sequence of fragments. A
fragment carries a content slice, its UTF-16 position within the line, and the
exact terminator only when that fragment completes the line.

Every fragment satisfies the caller's UTF-16 and JSON-encoded UTF-8 maxima. A
fragment boundary never divides a valid surrogate pair. If the supplied limits
cannot hold the next Unicode scalar or the complete terminator, fragmentation
fails visibly instead of returning an empty nonterminal fragment.

A fragment is an execution or transport piece. It is not:

- a line row;
- a line identity;
- a Count unit;
- a QuerySpace selection input; or
- evidence that the line or document is complete.

The outer operation may advance QueryOverflow with the complete immutable line
and retain fragment delivery state until that published row has been fully
transferred. It must not request another candidate batch while an earlier
published row remains only partially delivered.

## Completion and Count

A batch reports document completion only after it contains the final
coordinate line. Only that completed batch exposes exact line Count, equal to
the final line number.

A candidate-row limit, content limit, short batch, oversized line, fragment,
or continuation does not establish Count. QueryOverflow decides whether the
resolved terminal is semantically complete; the decoded-text source supplies
only document exhaustion.

## Ownership and failure

Line and fragment content are immutable memory slices over the document's
retained string. They remain valid after a batch object is released and do not
borrow mutable source buffers.

Invalid limits, a cross-document position, a position outside the document,
an offset that splits a surrogate pair, integer overflow, or a fragment limit
that cannot make progress fails visibly. The implementation does not return a
successful empty batch, silently clamp a request, or restart from the
beginning.

## First adopter and production path

`SourceViewInspection` now uses an unbounded pull to create its existing
complete `SourceView.Lines` value. This preserves the current public Source
shape while removing its duplicate line grammar. It does not yet claim
progressive Source execution.

The counted production path is:

1. SourceHouse joins one Source view binding and decoded-text position with one
   QueryOverflow execution.
2. The CLI drains that operation for ordinary complete Source output.
3. Inspect Web requests bounded Source segments.
4. Inspect Web package README adopts the same decoded-text source behind its
   package-owned evidence and operation.

The tracker in #8319 owns the sequence and current step count. Exact-byte CLI
destination streaming remains separate under #8303.

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
- an individually over-bound line split across bounded fragments;
- a fragment request too small to make progress; and
- a position used with the wrong document.

## Required evidence

| Gate | Property | Status |
| --- | --- | --- |
| `PullsReconstructExactDecodedText` | Mixed terminators, line numbers, UTF-16 starts, and supplementary scalars reconstruct exactly across bounded pulls. | Verified in Release by `Inspector.Text.Tests`. |
| `EmptyAndFinalEmptyLinesAreCoordinates` | Empty and trailing-terminator documents retain their coordinate lines and exact Count. | Verified in Release by `Inspector.Text.Tests`. |
| `DifferentPullBoundsPreserveRowsAndCompletion` | Accepted pull partitions preserve line values, order, completion, and Count. | Verified in Release by `Inspector.Text.Tests`. |
| `NormalPullsRespectEveryMaximum` | Every normal batch satisfies all three caller maxima. | Verified in Release by `Inspector.Text.Tests`. |
| `OversizedLineProducesBoundedFragments` | One oversized row is explicit; fragments are bounded, preserve its terminator, avoid surrogate-pair splits, and reconstruct it exactly. | Verified in Release by `Inspector.Text.Tests`. |
| `PullPositionIsBoundToOneDocument` | A source position cannot resume a different document. | Verified in Release by `Inspector.Text.Tests`. |
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
- that fragments are semantic rows;
- that every text Finding uses this coordinate model; or
- completed CLI or Browser progressive delivery.
