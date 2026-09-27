# Progressive JSONL delivery

## Status

This document is the normative owner for **Progressive JSONL Delivery**,
tracked by
[#8595](https://github.com/richlander/dotnet-inspect/issues/8595).
The pattern is designed but not yet implemented.

[JSON Schema Vocabulary Bindings](json-schema-vocabulary-bindings.md) supplies
the exact output descriptor used to interpret every record. The
[engine-to-browser async event stream](engine-browser-async-event-stream.md)
supplies ordering, durable-event meaning, credit, cancellation, and terminal
semantics. This design owns only the compact JSONL record representation and
its bounded string-batching profile.

## Owner and exact claim

**Progressive JSONL Delivery** owns:

> Given an ordered sequence of typed durable rows, one exact output descriptor
> identity, and a bounded UTF-16 batch-size preference, encode every admitted
> row as one complete LF-terminated JSON value and publish ordered, nonempty
> string batches without splitting records or changing the stream's existing
> progress, failure, credit, cancellation, or terminal semantics.

This owner defines:

- the compact JSONL record and line-framing contract;
- responsibility for row serialization, line framing, and batch decoding;
- the data-batch shape and its record count;
- the request's soft batch-size currency;
- hard per-batch code-unit and record-count bounds;
- exact flush conditions;
- the descriptor-to-stream join;
- encoding-specific completion accounting;
- visible serialization, framing, size, and mismatch failure; and
- the first Package Query Browser/Wasm adoption.

It does not define:

- Package Query matching, row semantics, vocabulary terms, or result order;
- JSON Schema, positional slot meaning, or descriptor identity;
- async enumeration, generic progress-coalescing rules, item-failure meaning,
  credit, cancellation, terminality, or callback lifetime;
- Worker operation identity, event-entry batching, protocol versions, epoch
  lifetime, replay, or hard termination;
- DOM publication authority, row rendering, or viewport policy;
- the public CLI `--jsonl` object-per-row shape; or
- one incomplete JSON document split across callbacks.

The phrase "JSONL batch" means several complete JSONL records carried in one
string. It does not mean a JSON array, a JSON object containing `Content`, or a
Worker `Events` batch.

## Product need and production witness

Package Query currently serializes every durable match as a complete
`BrowserPackageQueryEvent` object. The Worker parses that JSON, validates the
object, and forwards one decoded durable event. Repeating the event envelope
and row property names for every match spends payload and managed allocation
without adding row meaning.

The first production witness remains a Package Query for the real nuget.org
asset `System.Text.Json@10.0.0`. The current row contains package identity,
acquisition tier, answers, evidence, download and verification facts,
producer, optional description and root request, owners, and optional manifest
data. The compact row preserves those semantics while replacing its repeated
top-level property names with the owner-issued positional declaration from
JSON Schema Vocabulary Bindings.

The user-visible goal is earlier useful rows with lower repeated-key overhead,
not merely a different callback shape. The adoption therefore measures:

- time from accepted operation to the first durable row;
- managed allocations through terminal settlement;
- managed-to-Worker and Worker-to-page JSON code units;
- callback and Worker-message counts; and
- total completion time.

The current object-event route is the base measurement and remains available
until the compact route demonstrates the same visible result semantics and a
useful payload or allocation improvement.

## Existing owners remain authoritative

### Row structure and vocabulary

The `package-query.durable-row` `Serialize` descriptor owns the exact
positional schema and bindings. The row writer consumes the same owner-issued
slot declaration as:

- the shared direction-qualified wire plan;
- generated TypeScript;
- JSON Schema `prefixItems`; and
- schema-location-to-vocabulary bindings.

This design neither copies the slot list nor derives it from
`BrowserPackageQueryRow`, a TypeScript interface, runtime samples, or tuple
ordinals.

### Event sequence and credit

The engine-to-browser async event-stream owner remains the authority for:

- producer order;
- Progress, Item, ItemFailure, and Completed meaning;
- flush before an incomplete producer suspension or physical termination;
- exactly one semantic completion;
- item credit and pull-ahead; and
- cancellation handoff of already established events.

This design refines only how a contiguous run of admitted Item rows is encoded
as one durable data event. Progress, item failure, and Package Query's durable
non-row assessment events remain separate typed nonterminal events.

### Managed callback and Worker transport

The managed-operation bridge retains one operation-scoped synchronous
nonterminal callback and its release ordering. A JSONL data batch crosses that
callback as a JavaScript string primitive. It is not escaped again as a JSON
string property inside another serialized event document. Record count is a
sibling primitive callback field, not a wrapper around the JSONL content.

The Worker runtime's `Events` message remains a batch of at most 64
nonterminal transport entries. A progressive JSONL data batch is one durable
entry regardless of how many JSONL records its content carries. Worker entry
count, JSONL record count, and JSON code-unit size are distinct currencies.

The Worker forwards the accepted batch content without concatenating,
splitting, normalizing, parsing and reserializing, or adding line breaks.
Operation authority retains the final decision about whether a current or
stale event may update the page.

## Contract model

Conceptually, one adopting feature declares:

```text
ProgressiveJsonlProfile
  format version
  stable JSON contract identity
  Serialize direction
  exact descriptor identity
  maximum batch JSON code units
  maximum records per batch
  line terminator = LF

ProgressiveJsonlRequest
  expected format version
  expected descriptor identity
  target batch JSON code units

ProgressiveJsonlBatch
  record count
  content

ProgressiveJsonlAccounting
  format version
  descriptor identity
  published record count
  published batch count
  published JSON code units

InspectionEnvelope<PackageQueryProgressiveCompletion>
  Content
    Package Query semantic Summary
    ProgressiveJsonlAccounting
  Share
  Diagnostics
```

The profile is product-authored generated data. The request selects one exact
format, one exact descriptor, and one soft target within that profile. A batch
contains only record data plus transport metadata outside its content string.
Normal feature completion carries semantic summary plus final transport
accounting. It does not repeat streamed rows, failures, or assessments.

The first Package Query profile is:

```text
format version: 1
contract identity: package-query.durable-row
direction: Serialize
maximum batch JSON code units: 1,048,576
maximum records per batch: 20
line terminator: LF
```

The maximum record count aligns with Package Query's current initial
durable-match credit. Replenishment remains ten matches and is not a batching
configuration. The website initially requests a target of 65,536 JSON code
units. That target is a tuning preference rather than a schema or compatibility
identity; measured adoption may change it without changing record meaning.

The request rejects:

- an unsupported or stale format version;
- an unknown, stale, or wrong-direction descriptor identity;
- zero, negative, non-integral, or unsafe target values; and
- a target above the profile's hard maximum.

Rejection occurs before the producer starts and before any callback can
publish a row.

## Record contract

One record is one complete compact JSON value satisfying the exact output
schema named by the profile's descriptor identity.

The owner-issued row writer:

- accepts one typed semantic row;
- writes one complete JSON value;
- uses the serializer behavior authenticated by the shared wire plan;
- emits no byte-order mark, indentation, leading whitespace, trailing
  whitespace, carriage return, or line feed; and
- reports serialization failure without returning partial text.

The row writer does not own line framing. The progressive JSONL batcher appends
exactly one U+000A line feed after every successfully serialized record.

Every nonempty batch therefore has this grammar:

```text
record LF
(record LF)*
```

Every batch ends in LF, including the final batch. An empty operation emits no
data batch. Concatenating all batch content strings in publication order
produces the same valid JSONL stream without inserting or removing text.

JSON strings escape embedded control characters, so a literal LF in the batch
content is always a record separator. CRLF, blank records, comments, a header
record, schema record, progress record, failure record, and completion record
are not admitted.

## Line framing and decoding responsibility

Managed code is responsible for line-ifying:

1. the product row writer produces one complete compact JSON value;
2. the progressive batcher appends LF;
3. the batcher groups framed records into the content string; and
4. the synchronous callback transfers that string.

The Worker validates the feature-owned primitive batch fields and forwards the
content unchanged. It does not turn objects into lines or lines back into
objects.

The page is responsible for consuming lines:

1. validate the nonempty content and declared record count;
2. require final LF and no empty interior record;
3. split or scan only at LF boundaries;
4. parse each record independently with `JSON.parse`;
5. require the top-level positional row shape from the exact descriptor; and
6. project known feature terms into the existing typed Package Query model;
   and
7. publish no row from the batch until every record in that batch has decoded.

The Browser has prior knowledge of the stable vocabulary terms required by the
Package Query feature. It resolves their current slot locations from the
descriptor bindings. It does not hard-code ordinals, infer semantics from
values, use display labels as keys, or maintain a TypeScript-only column table.
That resolution produces one operation-scoped row layout before the stream
request; a missing, duplicate, or non-positional required term rejects the
request locally.

The baseline does not add a general JSON Schema validator. The bounded JSONL
decoder validates framing and the declared positional container; existing
feature decoders validate the values they consume. Generated TypeScript
retains compile-time correspondence with the same row declaration.

## Batch-size currency

`targetBatchJsonCodeUnits` and `maximumBatchJsonCodeUnits` count UTF-16 code
units in the final batch content string:

```text
batch.Content.Length
```

.NET `string.Length` and JavaScript `string.length` use that same currency.
The count includes every JSON character and each trailing LF after serializer
escaping. It does not count:

- UTF-8 bytes;
- Unicode scalar values;
- source-string characters before JSON escaping;
- objects, rows, slots, callback invocations, or Worker entries; or
- transport-envelope overhead outside the content string.

A .NET 11 RC1 source-generated serializer probe wrote the record values
`"BMP-é"`, `"astral-😀"`, and `"line\nbreak"` plus final LF. The managed and
Node.js strings both measured 51 UTF-16 code units, the embedded newline was
escaped, and the batch contained exactly one literal LF. The implementation
gate retains this shape with its source-generated production writer.

The soft target is not an exact batch size. A single framed record larger than
the target is published alone when it fits the hard maximum. No batch may
exceed the hard maximum, and no record may be split to satisfy either bound.

The hard maximum is intentionally well below the current Package Query
object-event wire ceiling. It bounds one main-thread parse unit and one raw
string payload rather than trying to preserve the old event's worst-case
escaped-object formula.

## Analogous formats and deliberate boundary

[JSON Lines](https://jsonlines.org/) establishes the useful baseline: every
line is one valid JSON value, blank lines are invalid, and a trailing line
terminator is strongly recommended. This contract is stricter by requiring LF
after every record and every batch so arbitrary batches concatenate without a
boundary repair step.

[RFC 7464 JSON Text Sequences](https://www.rfc-editor.org/rfc/rfc7464) uses an
ASCII record separator and permits recovery from incomplete or invalid
elements. Those assumptions do not transfer. The product already exposes
JSONL, requires ordinary one-value-per-line content, and reports malformed or
truncated output as failure.

JSON Lines describes UTF-8 files. This boundary transfers JavaScript strings,
not encoded files or byte buffers. The JSON character content follows JSONL,
while UTF-16 code units are deliberately the size currency shared by .NET and
JavaScript. A later file or network sink chooses its own byte encoding without
changing this Browser callback contract.

## Batch formation

The adapter owns one initially empty buffer, record count, and publication
accounting. For each row established by the engine stream:

1. satisfy the existing durable-item credit rule before admitting the row;
2. serialize the complete record without mutating the current buffer;
3. compute the framed length as the serialized record length plus one LF;
4. if the framed record exceeds the hard maximum, flush an existing batch and
   return typed `RecordTooLarge` failure;
5. if adding the record would exceed the soft target or hard record-count
   bound, flush the current batch first;
6. append the record and LF atomically; and
7. flush when this is the first published row, the buffer reaches or exceeds
   the soft target, or the record-count bound is reached.

The first admitted row is therefore a singleton batch. This preserves the
progressive experience even when the producer can synchronously generate many
small rows before its next await. Later batches optimize transfer overhead.

The adapter also flushes a nonempty row buffer:

- before publishing a later progress, item-failure, assessment, or other
  non-row event;
- before awaiting a `MoveNextAsync` that did not complete synchronously;
- before returning semantic completion;
- before reporting physical cancellation or exceptional producer termination;
  and
- before waiting for additional durable-item credit.

These are encoding-specific applications of the existing async event-stream
flush contract. They do not add another ordering or cancellation state
machine.

A successful callback return admits the complete batch to publication. Only
then does the adapter increment published record, batch, and code-unit
accounting. A callback exception leaves that batch uncounted and follows the
managed bridge's existing failure path.

## Ordering and non-row events

Each data batch represents one contiguous run of Item rows in producer order.
The adapter cannot move a row across a progress, item failure, assessment, or
completion that it actually publishes.

### Package Query progress coalescing

Package Query can produce a same-phase progress checkpoint around each
candidate. Publishing every replaceable checkpoint would force one-row JSONL
batches and defeat the encoding's stated payload and callback goal.

The compact adapter uses the async event-stream owner's existing permission to
coalesce advisory progress:

1. the first checkpoint for a new phase is selected for publication;
2. a later checkpoint in that phase replaces an unpublished checkpoint;
3. if a row follows an unpublished same-phase checkpoint, the adapter discards
   that checkpoint rather than moving it after the row;
4. if suspension, phase change, item failure, assessment, or completion follows
   without an intervening row, the adapter flushes earlier rows and publishes
   the retained checkpoint before that later event; and
5. published progress remains monotonic within its phase.

Rows themselves make ongoing useful work visible. Completion retains the
authoritative final accounting. This profile does not coalesce or discard a
durable row, item failure, or assessment, and it does not reorder a published
progress value.

Once any non-row event is selected for publication, every earlier buffered row
is flushed first.

Item failures and assessments do not enter the JSONL content and do not consume
Package Query match credit. They retain their current typed payloads and
durable ordering. Completion remains outside the callback and follows every
accepted nonterminal event.

The Worker may carry a data batch and neighboring non-row events in one
`Events` transport message when a feature adapter chooses to form such a
message. Their entry order remains authoritative. The first Package Query
implementation may continue using singleton Worker event batches; Worker
batch-count optimization is not required for the JSONL adoption.

## Credit and bounded pull

Credit remains denominated in durable match rows:

- one JSONL record consumes one match credit;
- one JSONL batch consumes the sum of its record credits;
- progress, item failures, and assessments consume no match credit; and
- callback count and Worker entry count do not grant or consume row credit.

The existing initial credit of 20 and replenishment of 10 remain unchanged.
Batching cannot admit another producer row merely to approach the size target.
When the next row waits for credit, the adapter first publishes every already
admitted buffered row.

An uncredited row retains the async event-stream owner's existing cancellation
and deadline behavior. JSONL framing does not turn an uncredited row into an
admitted or published row.

## Descriptor and stream correspondence

The website obtains and resolves the exact
`package-query.durable-row` `Serialize` descriptor before starting the stream.
The request carries format version 1 and that descriptor identity.

The managed operation compares both values with the profile and descriptor
used by the row writer. It does not accept:

- another format version;
- structural schema identity in place of descriptor identity;
- another contract or direction;
- a latest or compatible descriptor;
- a stale vocabulary snapshot; or
- an identity supplied only after enumeration begins.

Normal Package Query completion's `ProgressiveJsonlAccounting` repeats the
format version and descriptor identity. The page verifies both values against
the request before committing terminal success and verifies:

```text
sum(batch.recordCount) == completion.publishedRecordCount
sum(batch.content.length) == completion.publishedJsonCodeUnits
number of batches == completion.publishedBatchCount
```

Package Query's semantic match and failure accounting remains separately
owned. Equal transport and semantic counts are asserted only where the
Package Query completion contract says every match produces one durable row.

Cancellation, Worker failure, callback failure, and unexpected producer
termination do not invent a semantic completion solely to carry accounting.
The operation request retains the expected descriptor identity, and existing
terminal failure or cancellation remains authoritative.

## Terminal projection and duplicate-work retirement

The current object route returns `BrowserPackageQueryInspection`, whose
terminal `BrowserPackageQueryDocument` repeats Results, Failures, and
library-literal assessments already observed through callbacks. Retaining that
Browser wire projection on the compact route would preserve the dominant
duplicate payload and serialization work while merely changing the earlier
callbacks.

The compact route instead returns:

```text
InspectionEnvelope<PackageQueryProgressiveCompletion>
```

Its Content contains the existing semantic Package Query Summary plus
`ProgressiveJsonlAccounting`. The envelope retains the product-issued Share
and diagnostics. It contains no second Browser row, failure, or assessment
array.

The page constructs its settled view from:

- rows decoded atomically from accepted JSONL batches;
- separately streamed typed failures and assessments;
- the terminal semantic Summary;
- the returned Share and diagnostics; and
- the verified descriptor and transport accounting.

This is page state composition, not reconstruction of a product
`InspectionEnvelope<PackageQueryDocument>`. The existing host-neutral
`PackageQueryInspection` remains the owner of its semantic document and Share
construction; this focused transport design does not redefine that result.
The compact Browser adapter consumes its Summary, Share, and diagnostics
without allocating or serializing a second `BrowserPackageQueryRow[]` solely
for terminal settlement.

The allocation comparison reports the retained host-neutral document cost
separately from eliminated Browser projection and wire cost. Eliminating the
semantic document itself would change the Package Query inspection owner and
requires its own focused design rather than being hidden inside this transport
adoption.

The object route keeps its existing terminal shape during comparison. Its
shape is not evidence that the compact route should duplicate streamed data.

## Failure behavior

Failure is visible and ordered:

- descriptor or request rejection occurs before enumeration and publication;
- row serialization failure flushes earlier buffered rows, publishes no
  partial record, and returns a typed terminal encoding failure;
- a framed record above the hard maximum flushes earlier buffered rows and
  returns typed `RecordTooLarge(actual, maximum)` failure;
- callback rejection or exception follows the managed bridge failure path;
- malformed or over-budget Worker input follows the Worker boundary failure
  path before partial event publication;
- malformed framing, record count, JSON, or required Package Query value at
  the page follows the existing feature-observer failure path; and
- unexpected producer failure flushes already established rows before the
  enclosing operation reports failure.

An adopter maps encoding failures to one of its existing honest non-success
terminal outcomes. It must not silently omit the row, emit malformed JSON,
substitute `null`, return successful empty output, or classify a lost required
row as an advisory progress event.

## Platform and trust boundary

The batcher and row writer are host-neutral managed code and remain
NativeAOT-friendly and Browser/Wasm-compatible. They add no reflection,
dynamic code generation, thread requirement, inspected-assembly loading,
network access, or third-party dependency.

Package metadata is untrusted internet-origin data, but it reaches framing
only as typed inert values and is escaped by the authenticated row writer.
Untrusted text cannot inject a record separator or control event. The
descriptor, profile, and row declaration are trusted product-authored data.

The raw batch string crosses managed JavaScript interop and Worker
`postMessage`. JavaScript strings are immutable values; "forward unchanged"
means equal UTF-16 code-unit content, not shared allocation or zero-copy
transfer.

## API and host use

The generated Inspect Web facade exposes:

1. the exact `package-query.durable-row` descriptor;
2. the Progressive JSONL profile; and
3. a Package Query operation accepting expected descriptor identity and target
   batch JSON code units.

The concrete generated names are implementation details. The host-neutral
operation receives typed rows and a batch sink; Browser-specific callback and
Worker adapters bind that sink at the edge.

The first implementation adds a compact Package Query route rather than
silently changing the existing object-event route. The website can exercise
both against the same real query during validation. After the compact route
proves equivalent semantics and useful measured improvement, a later
Package Query owner decision may retire the object route.

Public CLI `--jsonl` remains object-per-row and preserves its current schema.
That route supplies the established JSONL convention but is not migrated by
this Browser-focused issue. A future CLI streaming adoption may consume the
same host-neutral row batcher with its own object row writer only if its owner
preserves the public output contract.

## Demonstration

The website first obtains:

```text
contract: package-query.durable-row
direction: Serialize
descriptor: sha256:<exact descriptor digest>
hard batch limit: 1,048,576 UTF-16 JSON code units
```

It starts Package Query with:

```text
expectedFormatVersion = 1
expectedDescriptorIdentity = sha256:<exact descriptor digest>
targetBatchJsonCodeUnits = 65,536
```

The first matching row is published immediately:

```jsonl
["System.Text.Json","10.0.0","Nuspec",[],[],4810000000,true,"nuget.org",null,null,["dotnetframework"],null]
```

The concrete slot sequence is illustrative; the owner-issued descriptor is
authoritative. Later batches contain several records and still end after every
complete record:

```jsonl
["System.Text.Encodings.Web","10.0.0","Nuspec",[],[],null,null,"nuget.org",null,null,[],null]
["System.IO.Pipelines","10.0.0","Nuspec",[],[],null,null,"nuget.org",null,null,[],null]
```

No header line precedes them. The Browser resolves slot meaning and display
labels from the separately obtained descriptor and vocabulary snapshot.
Progress, failure, assessment, and terminal completion remain distinct events.

## Pathological cases and gates

Implementation must gate:

- empty streams producing no data batch and one ordinary completion;
- the first row flushing as one batch without waiting for the target;
- exact-target, one-code-unit-under, and one-code-unit-over boundaries;
- one record larger than the soft target but within the hard limit;
- one framed record larger than the hard limit failing without partial text;
- maximum record count flushing independently of code-unit size;
- BMP and surrogate-pair content, including serializer-escaped characters,
  producing equal .NET and JavaScript `length` results;
- embedded CR, LF, quotes, backslashes, and control characters remaining
  inside one valid JSON record;
- every nonempty batch ending in exactly one LF after its final record;
- arbitrary batch concatenation reproducing the same JSONL stream and rows;
- progress, item failure, and assessment forcing earlier row publication and
  retaining producer order;
- alternating same-phase progress and rows producing multi-row batches without
  moving a published checkpoint across a durable event;
- phase changes and retained progress before failure or suspension remaining
  ordered and monotonic;
- incomplete `MoveNextAsync`, credit wait, completion, cancellation, and
  producer failure flushing established rows;
- callback failure leaving the rejected batch out of completion accounting;
- stale format, structural-only, wrong-direction, and unknown descriptor
  identities rejecting before the first producer request;
- Worker forwarding preserving exact string content and event order before
  settlement;
- Browser record-count, framing, and JSON failure becoming visible operation
  failure without publishing an earlier record from the rejected batch;
- completion descriptor and transport accounting matching all accepted
  batches;
- compact terminal settlement containing Summary, Share, diagnostics, and
  transport accounting without a repeated Browser row, failure, or assessment
  array;
- existing Package Query match-credit bounds remaining unchanged;
- strict generated TypeScript compiling without a handwritten positional-row
  interface, ordinal map, or display-column table; and
- real `System.Text.Json@10.0.0` Package Query results matching the current
  object route's visible rows, failures, assessments, and completion.

The host-neutral row writer and batcher belong in a focused Release suite. The
managed Package Query adapter and generated facade belong in
`DotnetInspect.Web.Tests` and the existing `ts-jsexport` gate. Worker framing,
forwarding, and settlement order belong in the existing Inspect Web Worker
tests. Browser row projection and credit belong in Package Query tests.

The production comparison records exact base and head results for published
Browser/Wasm builds, including managed allocation, transferred code units,
callback and Worker-message count, first-row latency, and total completion.
The compact route is not declared an improvement when it merely changes shape
while preserving the former cost.

## Delivery plan

This focused pattern has three counted steps:

1. **Focused design - current.** Lock JSONL framing, string batching,
   descriptor join, currencies, failure, and adoption boundaries.
2. **Host-neutral writer and generated contract.** Implement the positional row
   writer, progressive batcher, profile, accounting, generated facade surface,
   and boundary gates after JSON Schema Vocabulary Bindings supplies the exact
   descriptor.
3. **Package Query Browser adoption.** Add the compact managed callback route,
   forward exact batches through the existing Worker event transport, decode
   them by descriptor bindings on the page, preserve credit and non-row events,
   return summary-only terminal content without duplicate Browser row
   projection, and record the production comparison against the object route.

Step 3 is the production consumer. It retains the object route during
measurement and removes any temporary handwritten schema, tuple, ordinal, or
display metadata before completion. Retirement of the object route is a later
Package Query decision based on the recorded evidence, not an automatic effect
of this design.

## Non-goals

- Replacing `IAsyncEnumerable<TEvent>` or exposing `MoveNextAsync` to
  JavaScript.
- Defining progress, item failure, assessment, completion, credit,
  cancellation, operation authority, or Worker epoch semantics.
- Streaming one incomplete JSON value across callbacks.
- A JSON array document, Server-Sent Events, NDJSON control records, or a
  schema/header line.
- Byte-array transport, UTF-8 byte targets, Base64, transferable buffers, or a
  zero-copy claim.
- A general JSON Schema runtime validator.
- Inferring row meaning from values, labels, property names, or ordinals.
- Repeating descriptor identity in every data batch.
- Changing public CLI `--jsonl`, adding a CLI flag, or making CLI output
  positional in this issue.
- Moving progress, failures, assessments, or completion into JSONL.
- Rebatching Worker events by byte size or changing the Worker's 64-entry
  protocol bound.
- Claiming exact callback size, exact batch count, or universal performance
  improvement from a soft target.
- Adopting every Browser event stream, diff, source document, or decoded-text
  path in one change.
