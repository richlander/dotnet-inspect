# Inspect Web source-diff transport

## Status and ownership

Implementation contract for
[#5684](https://github.com/richlander/dotnet-inspect/issues/5684) and
[#6057](https://github.com/richlander/dotnet-inspect/issues/6057).
The member source-diff worker feature adapter owns this boundary:

> An admitted member source comparison crosses the managed/worker/browser
> boundary as one complete, bounded, typed value without changing its endpoint
> text, analytical relations, statistics, or mapped presentation.

This is one reusable line-diff feature adapter under the
[worker runtime](inspect-web-worker-runtime.md#ownership), not a new runtime,
comparison producer, or viewer. Its responsibility is feature payload
admission, representation, encoding, and decoding. The first production
producer is the cross-version authored-Source comparison required by
[#8171](https://github.com/richlander/dotnet-inspect/issues/8171). The
same-member PDB/decompiled producer originally motivating this contract remains
separately owned and may adopt the same payload without merging the two
producers' acquisition or comparison semantics.

Supporting owners remain authoritative:

| Owner | Contract consumed |
| --- | --- |
| [Selected member source pair query](member-source-pair-query.md) | Exact cross-version member identity, independently resolved authored-Source endpoints, provenance, native comparison, and attempt outcomes |
| [Member source comparison query](member-source-comparison-query.md) | Separately owned same-member PDB/decompiled comparison semantics for its later transport adoption |
| [Member source diff presentation](member-source-diff-presentation.md) | Canonical endpoints, one analysis, six statistics, and one mapped diff |
| [Analysis diff](analysis-diff.md) | Complete ordered sequences, distinct relation cases, and correspondence facets |
| Markout `MappedTextDiff` | Ordered presentation ranges, inner mappings, annotations, and terminator assertions |
| [Worker runtime](inspect-web-worker-runtime.md) | Operation identity, admission, delivery, replay, cancellation, and realm lifetime |
| [Managed operation bridge](inspect-web-managed-operation-bridge.md) | Managed operation cancellation, closure, and retained work |
| [Page-facing engine client](inspect-web-jsexport-partitioning.md#page-facing-engine-client) | Generated facade bindings and the single-runtime production Worker cutover |
| [Surface composition](inspect-web-surface-composition.md) | Page selection and placement; adoption tracked by #5685 |
| Diff viewer, tracked by #5686 | Rows, interaction, selection, accessibility, and virtualization |

Source acquisition, producer-issued correspondence, C# normalization, page
placement, and viewer interaction are non-claims. The limits below apply to
this feature boundary, not to all metadata, source acquisition, or decompilation
work.

## Purpose and delivery

The user should receive the same comparison in the CLI and website, including
unequal replacements and movement that a conventional text view can hide.
A large result must not turn worker settlement into an unlimited browser
decode or silently become a partial comparison.

The original shared-host delivery tracker is
[#5526](https://github.com/richlander/dotnet-inspect/issues/5526);
[#5528](https://github.com/richlander/dotnet-inspect/issues/5528) composes the
six production-adoption milestones, including the existing client prerequisite:

1. Queries supplies the exact member comparison: #5682, implemented by #5724.
2. Shared Presentation and its CLI adopter: #5683, implemented by #5767.
3. Consume the separately owned single-runtime client cutover in #5987 and
   its Source scenario #5420.
4. This feature's bounded worker transport: design #5684, implementation #6057.
5. Browser Diff placement and page actions: #5685.
6. Browser diff viewer: #5686.

The CLI reached production at step 2, and the production Worker cutover is
complete. This implementation lands milestone 4 through the existing ordinary
Source operation and makes the typed result available to the page-facing
engine client. Starting a second managed runtime for Diff beside the existing
Worker runtime is not an alternative.
Source and Annotated Source remain separate artifacts; this is an additive
comparison operation, not their migration or replacement.
The retained method-body comparison facade operation compares explicit
IL/research subjects and is not an alternative implementation of this source
comparison. Its former contextual dialog is retired under #6491.

For the first adopter, Presentation projects the paired query's existing
`FindingComparison<string>` into `AnalysisDiff<string>` without matching again,
then performs the shared Markout lowering. Transport carries that typed shape
rather than introducing a browser differ or parsing formatted output.
Browser-specific row lowering and interaction belong to #5686; this adapter
provides neither HTML nor a second unified/side-by-side rendering algorithm.

## Admission and result boundary

The first-adopter request carries the existing browser's package, two exact
versions, framework, assembly, exact type, and selected MethodDef coordinates.
The worker resolves the coordinates through the existing
managed member-resolution and workspace services; process-local registrations
and page-owned workspace objects are not wire identities. It does not accept
arbitrary Before/After strings, source-fetch URLs, or declarations reconstructed
from display labels. The managed feature consumes the selected member source
pair query and existing host source capabilities. A later same-member
PDB/decompiled adopter owns its own request additions, including any decompiler
style selection; those producer semantics are not added to the authored-Source
operation by this transport.

One query result supplies both endpoint attempts. Only two complete attempts
may enter the shared Presentation adapter. Feature preflight may decline their
size before projection, but must not trim, split, normalize, or substitute text
to make them fit. This deliberately rejects some oversized inputs whose
canonical projection might have been smaller.

The operation outcome is a closed union, with endpoint distinctions retained
inside a succeeded comparison:

| Outcome | Meaning |
| --- | --- |
| Succeeded / Compared | One complete shared presentation, including an identical result with all six zero counts |
| Succeeded / Unavailable or Failed | No diff; retain each endpoint's Available, Unavailable, NotFound, Rejected, or Failed state and owner-issued detail, plus the available endpoint text |
| Failed | Request, query, or projection failed; retain the existing expected/unexpected classification and bounded diagnostic |
| TooComplex | A named feature limit was exceeded; retain its dimension, limit, and observed value |
| Canceled | Retain the managed operation's cancellation reason |

Only Succeeded / Compared carries endpoint sequences, relations, mapped changes, and
statistics. An unavailable or failed attempt is not an empty diff, an identical
result, or a one-sided completed comparison. A query's source-complexity
outcome remains distinguishable from a transport-capacity refusal.

Runtime rejection, cancellation, protocol failure, and epoch closure keep the
runtime's existing meanings; they are not successful feature outcomes.
The feature does not serialize exceptions or diagnostic source-shaped text as
an endpoint. A diagnostic that cannot fit its bounded representation uses a
fixed stage/reason message, not an unbounded exception string.

## Complete typed payload

The first wire schema has an explicit version and one operation kind.
Unknown versions, case tags, enum values, or fields are rejected rather than
silently dropped. The codec is a feature-owned projection of the existing
types, not their default object-graph serialization.

The managed facade emits one bounded JSON string. Its authenticated
`JsExportJsonOutput` declaration uses deferred parsing, so `ts-jsexport`
projects the boundary as
`JsonText<BrowserSourceComparisonResult>` without calling `JSON.parse`.
The existing ordinary Worker envelope carries that complete branded string;
the operation-selected `BoundedPayloadDecoder` unwraps the ordinary tuple,
enforces the 1 MiB UTF-8 ceiling before parsing, and admits the exact typed
shape. The page-facing operation publishes
`BrowserSourceComparisonResult` only after that decoder succeeds. There is no
unbounded intermediate parse or generic DTO trust. The JSON byte limit is an
encoding budget, not a claim about the browser engine's internal
structured-clone byte layout.

This is complete-document deferred realization, not progressive realization.
The feature defines no stream, chunk, page, cursor, ordering, backpressure, or
partial-result contract.

One Compared value contains:

- two ordered logical-line sequences, each with its producer label and
  final-line-terminator assertion;
- the complete ordered `AnalysisDiffRelation` population;
- the six named shared statistics;
- the complete ordered Markout change population, including inner mappings
  and annotations;
- bounded endpoint provenance and the request's existing browser coordinates;
  and
- separately admitted optional source-browse destinations.

The line arrays are stored once. Both analytical coordinates and mapped ranges
address those same arrays; duplicated canonical strings and duplicate mapped
line arrays are unnecessary. Rejoining a sequence for copying is literal text
assembly using its terminator assertion, not reflow, re-splitting, or matching.
The first producer currently asserts absent final terminators on both sides;
the codec preserves the full owner-defined terminator vocabulary.

The relation cases stay distinct:

| Case | Coordinate payload | Facets |
| --- | --- | --- |
| Addition | One After coordinate | None |
| Removal | One Before coordinate | None |
| Correspondence | Nonempty ordered Before and After coordinate populations | Content and placement, independently |

Coordinates and relation order are preserved exactly. An N:M correspondence
is not zipped into N pairs. Changed and moved populations may overlap.
Neither mapped ranges nor equal line text replace producer-issued relations.
The receiver does not compute replacement statistics or correspondence.

Mapped ranges retain zero-based starts and counts. Inner mappings retain both
side-local spans. Annotations retain their Change, Line, or Span target,
side where applicable, text, and Markout severity. Span offsets remain UTF-16
code units; UTF-8 payload accounting does not change coordinate units.
Current member source diffs have empty inner-mapping and annotation populations;
the codec's owner-type round-trip cases cover them without adding an annotation
producer to this effort.

The sender uses the shared result as a single value, rather than combining
independently obtained text, statistics, and mappings. Lossless codec tests
compare every retained population against that value. Receiver validation
checks the closed representation, scalar ranges, coordinate references, and
bounded collection shapes; it does not reinterpret the owners' content or
placement assertions.

## First admission profile

These are conservative feature limits, not measured latency guarantees.
They bound work and payload populations at this boundary without changing
the CLI or the underlying producers' limits.

| Dimension | Inclusive maximum |
| --- | ---: |
| Encoded request | 8 KiB |
| Raw member text before projection, per endpoint | 128 KiB UTF-8 |
| Physical lines before projection, per endpoint | 1,024 |
| Canonical logical lines, per endpoint | 1,024 |
| Canonical Before text | 128 KiB UTF-8 |
| Canonical After text | 128 KiB UTF-8 |
| Analytical relations | 2,048 |
| Coordinate occurrences across all relations | 2,048 |
| Mapped changes | 2,048 |
| Inner mappings across all changes | 1,024 |
| Annotations across all changes | 512 |
| Annotation text in aggregate | 64 KiB UTF-8 |
| Identity, labels, provenance, destinations, and diagnostics in aggregate | 16 KiB UTF-8 |
| Encoded feature result, including JSON escaping and framing | 1 MiB |

Every collection limit applies to the complete population, not independently
to each relation or change. A single long line cannot evade byte admission,
and many empty lines cannot evade line admission. Arithmetic is checked before
allocation or enumeration driven by a declared count.

For raw admission, CRLF counts as one boundary, as does each CR or LF. Empty
text has zero lines; nonempty text has one plus its boundary count, including
a final empty raw line when present. This is the `TextFindings` boundary
vocabulary, not the broader C# lexer newline vocabulary. Current endpoints are
already LF-normalized. Canonical counts are the shared analysis sequence
lengths; the repeated line cap is a receiver consistency check, not a new
independent producer limit.

The raw line bound keeps the present ordered matcher below
`(1,024 + 1)^2 = 1,050,625` matrix cells, well below its existing
64,000,000-cell limit. This is a structural input bound, not a claim that the
whole source operation has a bounded task-loop-silence interval.

The canonical text byte limits name the same per-endpoint bounds for the
receiver. Charge the UTF-8 bytes of every logical line plus one LF separator
between adjacent lines, plus a final LF when termination is asserted. Current
authored-Source comparisons have absent final terminators. The browser first enforces
each side's line-count cap and checks each line's UTF-16 length against that
side's byte ceiling. It then performs bounded aggregate UTF-8 accounting;
it never allocates or walks an unrestricted line population.

Admission happens before the expensive boundary it protects:

- raw endpoint checks precede canonical projection and comparison;
- complete-result population checks precede DTO materialization;
- byte-limited encoding precedes marshaling or posting the result; and
- the browser's fixed-shape, array-length, and string-length checks precede
  bounded collection and UTF-8 accounting walks.

Post-allocation measurement alone does not enforce an encoding limit.
The managed encoding sink must refuse before exceeding its byte capacity.
Any intermediate string representation is bounded before another parse or
clone. A UTF-16 string-length guard may reject impossible
inputs cheaply; admitted payload bytes are still charged as UTF-8, including
escaping. Results are not compressed or chunked in this first profile.

A refusal returns the small Too complex case and no partial data. There is no
prefix success, ellipsis standing in for omitted relations, or automatic
larger retry. A raised limit is a deliberate profile change with new boundary
evidence, not a renderer option.

## Liveness and publication

Payload admission does not make arbitrary source acquisition or synchronous
managed decompilation cooperatively yielding. The feature must declare
unbounded managed execution to the worker runtime until the runtime's
structural criteria for a bounded declaration are actually satisfied.
No stopwatch or input-size observation may substitute for that criterion.

Cancellation and result delivery use `WorkerRuntimeOperationRegistration`,
the worker's `WorkerOperationCatalog`, and the managed bridge, with the same
operation-authority-issued identity. The feature forwards cancellation through
supported product calls and checks it between feature stages; it does not promise interruption
inside an uncooperative synchronous stage. Runtime draining and explicit hard
realm release remain owned by the runtime. The page-facing client's recovery
policy is visible page reload, not automatic in-place rebinding or a new
feature-specific restart policy.

The result belongs to the operation binding that requested it. Existing epoch
and operation identity decide whether it may reach that sink. A canceled,
settled, superseded, or replaced binding must not publish this feature's late
payload to another member. Page-selection freshness remains with the surface
owner; transport does not infer it from member labels or invent a second page
generation.

Main-thread feature settlement is limited to admitting and handing off the
bounded immutable result. It does not materialize all viewer rows, run the
comparison again, or reinterpret source text. Viewer row-window bounds remain
separately required by #5686.

This effort adds no new operation-lifetime state machine. Runtime identity,
closure, and liveness model evidence remains owned by the runtime and bridge;
feature adoption still requires real cancellation and stale-delivery gates.

## Browse destinations and trust

Package, PDB, and source bytes are untrusted inputs. They may influence text,
names, and provenance, but URL-looking text is not navigation authority.
The controlled path here is a product-produced source location entering the
feature payload, not a hostile worker or cooperating browser code.

The first profile admits a PDB Open destination only when the selected
verified source document's resolved URL is accepted by
`ILInspector.SourceLink.SourceLinkProvenance.BrowseUrl`. That owner constructs an
HTTPS GitHub browse URL from an attributable immutable GitHub origin. The
feature checks its own size budget before invoking that grammar.

This is deliberately narrower than the existing Source section's raw
`BrowserSource.url`. That field's presence is not proof of browse authorization.
Other source providers may still yield a complete comparison, but have no Open
destination in this profile; adding another provider requires an owner-backed
browse projection rather than browser URL rewriting.
Repository display text, raw paths, diagnostic messages, and annotation text
never become destinations. Unavailable authorization is represented by no
destination, not by a browser-side URL guess.

The wire destination is distinct from display provenance and bounded before
URL parsing. The browser consumes an admitted destination; it does not
decorate raw SourceLink URLs or derive a link from the declaring type.
Decompiled comparison has no source-browse destination merely because its PDB
neighbor has one. Local download/blob actions are viewer concerns, not remote
source authorization.

The adapter preserves source text as text. HTML construction, DOM insertion,
and action invocation stay at their existing consumer boundaries.
This contract does not expand the repository threat model to malicious
same-process code, local interference, or inspected-code execution.

## Demo: proposed transport behavior

This is a mockup, not shipped browser output.

The ordinary `JsonSerializerOptions.MaxDepth:2` comparison used by the CLI
would arrive as one Complete payload with `PDB comparison` and
`Decompiled comparison`, Added 2, Removed 0, Changed 12 -> 4, Moved 0 -> 0,
and its complete mapped changes. The browser receives those counts; it does
not derive them from a unified hunk.

The neighboring codec fixture reuses the
[Analysis diff illustration](analysis-diff.md#pathological-demonstration):

```text
Before: [B, A, C, D]       After: [B1, B2, C, A2, E]

Correspondence [0] -> [0, 1]    Changed / Stable
Correspondence [1] -> [3]       Changed / Moved
Correspondence [2] -> [2]       Unchanged / Stable
Removal        [3]
Addition                         [4]

Added 1; Removed 1; Changed 2 -> 3; Moved 1 -> 1
```

This is owner-issued codec data, not a claim that the current producer
emits every illustrated facet. It demonstrates multiple differences, a
one-to-many replacement, and overlapping changed/moved populations. A mapped
view may render the moved item as removal/addition; the received analytical
relation remains intact.

At the admission boundary:

```text
1,024 lines per endpoint, all other limits satisfied -> Complete
1,025 lines in either raw endpoint                  -> Too complex: lines
valid shape whose escaped result exceeds 1 MiB      -> Too complex: encoded bytes
one endpoint unavailable                           -> Unavailable, no zero summary
identical complete endpoints                       -> Complete, all six zeros
old operation completes after cancellation          -> no publication to its sink
```

## Evidence and implementation gates

The implementation claims bounded browser execution and codec fidelity only
when the focused gates below pass. Prose, mocked packets, and inherited runtime
tests do not establish those properties.

| Required focused gate | Property |
| --- | --- |
| Request preservation | Exact package versions and selected MethodDef coordinates reach the existing paired query unchanged; malformed and oversized requests remain visible failures or typed capacity refusals |
| Managed feature admission | Exact limit and limit plus one at each admission boundary; byte-heavy and line-heavy endpoints; refusal before projection and before oversized encoding |
| Managed/worker/browser codec round trip | Exact text, labels, terminators, all relation cases/facets, six counts, empty and N:M populations, mapped ranges, inner mappings, annotation targets/severity, and Unicode span coordinates |
| Closed codec rejection | Unknown fields/tags/versions, invalid scalar or coordinate shapes, and collection limits fail visibly without repairing a payload |
| Browser settlement capacity | Maximum admitted and refused payloads traverse the production codec without unrestricted recursive walks, bulk row materialization, or an uncapped intermediate parse |
| Feature cancellation and stale delivery | Production adapter/bridge path; cancellation before projection, after projection, and a late completion after sink detachment or epoch replacement |
| Source destination admission | Allowed product-resolved destination, missing authorization, oversized destination, and URL-looking display/annotation text |
| Production Source operation | Existing managed member resolution and query through shared Presentation into the actual worker operation after #5987/#5420 activation, rather than a test-only DTO producer or a second page/worker runtime pair |

Managed gates run in Release. Codec and worker gates use the repository's
existing TypeScript/browser runners. Existing producer and runtime gates
remain supporting evidence; their contracts are not copied into a new model
or replaced by feature-only fakes. The later UI slice supplies its own visible
browser demo and workspace-demo adoption.

## Comparative basis

The baseline is the existing worker operation adapter with bounded,
owner-selected payload codecs, not an ad-hoc `postMessage` channel.
This feature chooses a complete size-admitted result rather than a new
chunking, acknowledgment, or streaming lifecycle.

[VS Code's pinned `LinesDiff` contract][vscode-diff] retains mappings and moves
and explicitly reports when a computation timeout produces an approximation.
Its useful precedent is visible incompleteness; its approximation behavior
does not transfer to this complete-comparison contract. This feature refuses
an oversized result instead.

[Markout's mapped-diff value types][markout-diff] preserve typed ranges,
annotations, and UTF-16 spans. They are the presentation input, not a
cross-process serializer or an analytical move model. JSON encoding is
transport, not a replacement for either ownership boundary. No external
implementation code is copied.

[vscode-diff]: https://github.com/microsoft/vscode/blob/1f625adb84abf41cdff31f40f66e58a222f033f6/src/vs/editor/common/diff/linesDiffComputer.ts
[markout-diff]: https://github.com/richlander/markout/blob/a7c59a92894a453e9d21132dc943c85771f3ed4a/src/Markout/TextDiffPrimitives.cs
