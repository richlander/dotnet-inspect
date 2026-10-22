# Opaque Byte Ranges and Inert UTF-8

## Status and owner

**Initial shared implementation and exact-Type result adoption.**
[#9991](https://github.com/richlander/dotnet-inspect/issues/9991) tracks this
focused design.

**Opaque Byte Ranges and Inert UTF-8** is a shared leaf capability. It owns
this exact claim:

> One immutable byte backing can issue cheap bounded `OpaqueByteRange`
> snapshots. An opaque range does not validate UTF-8 or apply `TextPolicy`; it
> answers only closed identity, matching, ordering, slicing, and structural
> questions without lending its bytes. A typed domain view identifies which
> exact range represents UTF-8 text and may issue a scoped
> `Utf8RangeReader`; arbitrary ranges cannot. Ordinary analysis and matching
> end in closed answers from that reader. When a sink needs displayable text,
> its producer constructs `InertUtf8String` from `TextPolicy` and the reader.
> That containing type unconditionally invokes `Utf8VisualEncoder`; callers do
> not choose whether containment happens. Its retained bytes are therefore
> already visually contained. `InertString` is the fixed UTF-16 projection of
> that contained UTF-8 for byte-originated text. Exceptional exact-byte
> transfer is isolated as
> `InertText.Encoding.OpaqueByteRangeEncoder`, not an ordinary range operation.

This owner defines:

- immutable backing and range lifetime;
- `OpaqueByteRange` as the retained ownership snapshot;
- byte-exact equality, hashing, ordering, checked slicing, and query
  comparison;
- scoped readers and builders that issue ranges without lending retained raw
  storage;
- the rule deciding whether an operation is safe on untreated bytes;
- explicit exact-byte encoding to a destination without lending the backing;
- `InertUtf8String` as the policy-contained UTF-8 presentation value;
- `InertUtf8String` construction as the mandatory presentation boundary;
- `Utf8VisualEncoder` as that containing type's sole implementation of the
  transition from untreated bytes to contained text;
- fixed contained UTF-8 output and conversion to `InertString`; and
- the zero-intermediate-copy claim and its limits.

It does not define:

- a domain grammar, marker vocabulary, identity profile, or validation policy;
- which metadata facts participate in API, Diff, or other product identity;
- visual-containment policy or spelling, which remain with
  [Inert Text](inert-text.md);
- ownership of a live artifact resource or a borrow from one, which remains
  with [Resource Ownership and Borrowing](resource-ownership-and-borrowing.md);
- a persisted format, transport encoding, interning scope, or stable hash; or
- whether a sink discloses contained text or substitutes a placeholder.

The initial implementation keeps `InertUtf8String` and `Utf8VisualEncoder` in
the same dependency-free `InertText` assembly. The constructor calls the
static `Utf8VisualEncoder.Encode` method directly. Their architectural
ownership follows these focused contracts, not the project boundary.

`docs/overview.md` is release-managed. The release tracker records its future
authority entry rather than this design editing that central file.

## Product question

**How can inspection retain artifact text as exact bytes for analysis and
matching, then establish visual containment only when a sink needs text?**

Metadata names begin as bytes. Current paths commonly decode them to UTF-16,
retain them as `string` or `InertString`, and then use that presentation form
for matching. That has three costs:

1. UTF-16 commonly requires twice the payload storage of ordinary UTF-8.
2. Decoding and containment happen even when analysis only compares names.
3. A presentation-safe spelling is a poor matching substrate because encoding
   changes the value being compared.

Returning a raw span is not the answer. It makes every receiver a new
boundary: the matcher must validate, bound, contain, and avoid leaking the
untreated value. The attractive zero-copy API then distributes the hardening
obligation across every analyzer.

The target follows the ownership-snapshot model and the closed-query shape of
`Utf8JsonReader.ValueTextEquals`:

```text
producer or scoped source reader
  -> ref-struct builder records ranges
     -> freeze creates one hidden immutable backing
        -> OpaqueByteRange snapshots retain exact bounded bytes
           -> typed domain text views retain classified ranges
              -> views issue Utf8RangeReader
                 +-> ordinary analysis/matching asks closed questions
                 |     -> answers end the ordinary path
                 +-> display value required
                       -> new InertUtf8String(TextPolicy, reader)
                          -> constructor calls Utf8VisualEncoder
                             -> contained UTF-8 output
                             -> InertString serves UTF-16/string sinks
```

For a typed Name consumer, `NameRange` means the exact `OpaqueByteRange`
retained privately by that domain view, and `SearchValue` means the caller's
pre-encoded query. The governing representation rule is:

> To decide whether `NameRange` equals `SearchValue`, perform a closed range
> evaluation through `Utf8RangeReader.ValueTextEquals`; no contained string is
> created. To print `NameRange`, construct `InertUtf8String` from
> `TextPolicy` and its classified reader, then print that value or its fixed
> `InertString` projection. The constructor necessarily invokes
> `Utf8VisualEncoder`; an opaque range is never printed directly.

The operation boundary decides whether access is safe:

> An operation belongs on an opaque range or its scoped reader when it can
> return its result without requiring the matcher or analyzer to harden itself
> against the stored value.

Opacity and containment are intentionally different. `OpaqueByteRange` protects
untreated bytes by never lending them. `InertUtf8String` contains text by
retaining only bytes that have passed UTF-8 decoding and the named visual
policy.

### First exact-Type adoption

The first product adoption is the direct, fully qualified exact-Type subject
path. It deliberately changes only the selected result boundary:

1. Existing structured exact matching determines whether one TypeDef is the
   unique match. Listing, globs, suggestions, implied-member fallback, and the
   broad `ApiSurface` lookup do not participate.
2. After uniqueness is known, `MetadataUtf8TypeDefinitionName` detaches the
   matched root namespace and root-to-leaf metadata-name segments into one
   compact immutable backing. No unmatched candidate Name is copied.
3. Its `MetadataUtf8Name` views issue `Utf8RangeReader`; containment
   constructs `InertUtf8String(TextPolicy.Field, reader)` values in a separate
   `MetadataContainedTypeDefinitionName`.
4. The existing raw `MetadataTypeDefinitionName` remains the exact lookup and
   binding identity. `TypeSubject` carries the byte-native name beside it and
   contains it on the first read of `ContainedType`, caching the result, so a
   command whose output never presents the name as bytes does no containment.
   Escaped presentation spellings never substitute into identity.

The direct command additionally creates one `MetadataExactTypeInput` from the
validated structured query. That host-owned value retains the raw
`MetadataTypeDefinitionName` and one compact UTF-8 backing for its namespace
and root-to-leaf segments. It does not authorize reuse by origin alone.
`MetadataReader.StringComparer` keeps its existing allocation-free candidate
comparison and unique-resolution semantics. Only after uniqueness is known
does the metadata operation compare every selected `#Strings` range byte for
byte with the corresponding host-owned range. The internal
`MetadataExactTypeMatch` issued by those comparisons permits the result to
reuse the query backing; a missing input or any byte mismatch falls back to
selected-range detachment. Either way the subject carries one byte-native name,
and containment happens only when a sink reads `ContainedType`.

`OpaqueByteRange.SequenceEqual(ReadOnlySpan<byte>)` owns the closed cross-range
comparison. It is a byte operation, not a text query, so it does not require
text classification. The metadata adapter supplies a scoped view over the
selected `#Strings` bytes; neither the opaque query range nor the artifact
bytes become a general raw-byte projection. Listing, fuzzy, prefix,
case-insensitive, and compatibility paths cannot issue the typed match. This
optimization is measured against the merged detached-name baseline, not
against the earlier UTF-16 presentation proxy.

The first end-to-end output adoption is the direct-library, fully qualified
exact-Type `Type Info` TSV document. It is deliberately narrower than the
exact-Type inspection boundary:

1. The host creates `MetadataExactTypeInput`; metadata resolution and typed
   byte-match evidence remain authoritative exactly as above.
2. `TypeSubject.ContainedType` contains the proven namespace and root-to-leaf
   Type segments on first read and carries them into Presentation without
   rebuilding that spelling from `MetadataTypeDefinitionName`.
3. Presentation writes the Type spelling directly to `IBufferWriter<byte>`,
   and the CLI writes the completed UTF-8 document to standard output without
   an intermediate UTF-16 document.
4. The direct document is admitted only for a clean non-generic Type whose
   established `Type Info` projection needs no rendered base-Type identity.
   Generic Types, transformed names, nontrivial base Types, other formats,
   row or column projections, and every broader command shape retain the
   established renderer.

That eligibility rule preserves the byte-identical selected document while
establishing one complete host-input-to-byte-sink path. Diagnostic reach
follows the work actually performed: the bounded exact-Type path does not
reproduce warnings or failure status caused only by unrelated metadata rows
visited by the broader `ApiSurface` traversal. It does not authorize a second
Type formatter, change raw lookup identity, or claim that the broad
`ApiSurface` path is byte-native. Performance evidence measures the complete
one-shot NativeAOT command, including input and plan construction, inspection,
Presentation, and output.

## Motivating evidence

Finding Diff Any's private `SymbolKey`, introduced by
[#9898](https://github.com/richlander/dotnet-inspect/pull/9898), copies metadata
names directly into a byte key and compares finished keys as bytes. That
change fixed a collision between ill-formed names and improved NativeAOT
performance by 20% to 30% across its pinned pairs. The
[Symbol Key design](https://github.com/richlander/dotnet-inspect/pull/9932)
generalizes that domain representation but still makes the domain type own its
byte string.

The `ApiQualifiedNamePilot` at
[`d46038c3`](https://github.com/richlander/dotnet-inspect/commit/d46038c3d96127dee9cd94cd24c146247b73e970)
compared eager UTF-16/`InertString`, one UTF-8 array per name, and typed ranges
over one compact UTF-8 arena. Under NativeAOT, the compact representation's
matching median was 73% to 79% faster and used 67% to 68% less allocated and
retained managed memory on:

- .NET 11 RC1 `System.Private.CoreLib.dll`;
- `System.Text.Json@10.0.9`; and
- `Microsoft.Extensions.Configuration@10.0.10`.

That pilot established that compact byte ranges can beat eager UTF-16. It did
not compare this design with Finding Diff Any's byte-native private Symbol Key,
and it did not measure the cost of the `OpaqueByteRange` and `InertUtf8String`
contracts. The Symbol Key pilot in the evidence plan must re-establish both time
and memory findings; this design does not assume the new representation wins.

The contract-level metadata Name pilot under
[#10376](https://github.com/richlander/dotnet-inspect/issues/10376)
re-established that result with `OpaqueByteRange`,
`OpaqueByteRangeBuilder`, `Utf8RangeReader`, and `InertUtf8String`. Across
CoreLib, `System.Text.Json` 10.0.9, and
`Microsoft.Extensions.Configuration` 10.0.10, the production-shaped method
scope found that one backing with one retained range per Name used 44% to 46%
as much memory as eager `InertString`, made closed matching 39% to 50% faster,
and made one-result sparse presentation 27% to 43% faster. The current
one-backing-per-Name shape retained 17% to 22% more memory before presentation
and about 56% more while every Name was projected; future population adoption
must not repeat it.

Retaining one population-local slot per Name reduced the stored graph further,
to 25% to 27% of eager memory, and kept the combined byte-plus-UTF-16 graph 9%
to 12% smaller during complete display. The existing public composition is
not the production answer: resolving and validating a range with `Slice(slot)`
for every candidate made matching and sparse selection about three times
slower than eager strings. A slot-backed adoption therefore requires
owner-level closed operations that preserve opacity without materializing a
retained range for every comparison.

A follow-up descriptor pilot isolated that operation with one shared byte
population. An owner-fused checked operation over an 8-byte `[offset, length]`
slot matched the latency of a 16-byte `[array, offset, length]` retained range
on all three assets, with median ratios from `0.99x` to `1.00x`. NativeAOT code
inspection showed that the compiler retained the
`offset + length <= array.Length` branch in both checked kernels. A
trusted-coordinate diagnostic that omitted the branch ran at `0.68x` to
`0.73x`, proving both that the check was not eliminated and that the safe
checked slot still achieved the required range-latency parity. The earlier
three-times loss therefore came from general range reconstruction, not from
the 8-byte descriptor.

This result establishes the performance shape, not a public unchecked
capability. A product adoption may fuse checked closed operations over slots.
It may omit per-operation validation only behind a stronger
collection-private invariant that prevents foreign or unvalidated slots from
reaching the operation. Until that separately reviewed capability exists,
retained ranges remain the usable match-heavy representation and slots remain
a demonstrated compact substrate rather than an adoption recommendation.

A declarative-index follow-up tested whether the owner could retain logical
identity instead of byte coordinates. A validated dense ordinal population
used one `N+1` 32-bit offset vector and remained within 5% of retained-range
latency on the complete metadata Name census and repeated method-production
shape. Five asset/scope cells were statistically inconclusive around parity;
the complete CoreLib census measured a small 5% loss. Its owned payload was
54% to 61% of retained ranges, versus 69% to 74% for 8-byte slots. This is the
best measured payload/latency shape when ordinal position is implicit, the
owner consumes the population as one ordered sequence, and individual
identities do not escape.

That result does not generalize to every declarative index. Adding one 4-byte
handle per occurrence to the offset vector restored essentially the same
payload as an 8-byte slot and made matching 15% to 43% slower. Interning equal
Name bytes reduced payload further, to 31% to 47% of retained ranges, at a 12%
to 19% median match-time cost. It is a candidate for repeated-value
populations whose retained-space win amortizes construction and indirection,
not a universal representation.

The metadata source's existing 4-byte `StringHandle` was the smallest borrowed
descriptor, but resolving it through
`MetadataReader.GetBlobReader(StringHandle)` repeatedly found the
null-terminated `#Strings` length and made full-population matching 3.74x to
4.85x slower. Source handles remain natural while the metadata image lifetime
and access pattern are acceptable; they are not evidence against a detached
owner that records boundaries once.

Declarative identity is therefore a semantic improvement only when the owner
has a matching structural advantage: implicit ordinals, repeated values worth
interning, fixed-width index arithmetic, or an already-live source heap. A
one-to-one explicit index over unique variable-length values is neither denser
nor faster than a checked slot. Arbitrary, overlapping, nested, or
independently escaping slices still require coordinates or an owner-bound
range.

The representation recommendation depends on the terminal:

- match-heavy and sparse-presentation paths should retain shared
  `OpaqueByteRange` values;
- owner-consumed ordered populations may use a validated dense ordinal design
  after a separately reviewed product operation establishes that identities do
  not escape;
- repeated-value populations may use an interned declarative index only after
  measuring construction and terminal tradeoffs in the adopting consumer;
- populations written repeatedly to UTF-8 sinks should retain
  `InertUtf8String`, paying containment once and writing contained bytes
  directly;
- one-shot UTF-8 output may use shared ranges to reduce allocation and peak
  memory, but the pilot established no end-to-end time win;
- inventories that always retain every UTF-16 value should remain eager
  `InertString`; and
- slots remain a compactness target pending owner-level closed operations.

The added direct-output evidence explains why the contained UTF-8 layer is not
interchangeable with untreated ranges. Repeatedly containing raw ranges before
writing was 3.3x to 4.2x slower than re-encoding eager strings. Retained
`InertUtf8String` made direct byte output 35% to 61% faster on the two assets
with conclusive timing and retained about 69% to 70% of the eager
store-plus-output graph. One-shot build-plus-byte-output time was inconclusive
for every byte-native representation, while allocation and retained memory
were substantial wins.

Measuring the contained UTF-16 length while building a compact population left
substantial construction work. Charging contained UTF-8 bytes instead produced
medians 25% to 38% below eager without establishing a consistent all-asset
win. That is evidence for a separately owned retained-text budget decision;
the pilot does not change the existing unit.

## Representation

### One backing, many opaque snapshots

`OpaqueByteRange` is a small `readonly struct` with value semantics over one
bounded range. Its hidden backing is independently retained and immutable for
the complete lifetime of every range. Creating a range or subrange does not
copy bytes.

The conceptual shape is:

```text
hidden immutable backing
  range [offset, length]                 -> OpaqueByteRange
    domain interpretation               -> SymbolKey<TDomain>
    domain Name view                    -> OpaqueByteRange (retained privately)
    nested range [offset, length]        -> OpaqueByteRange
```

Immutable here means that no InertText operation writes a backing once it is
frozen, and none lends its storage, so no caller can write it through the
library's surface. It is enforced by encapsulation, not memory protection: the
storage is an ordinary managed array, and code inside InertText, an assembly
granted internal access, reflection, or unsafe code can still reach it. Those
are trusted in-process parties, not adversaries this type defends against.

An opaque range guarantees only owner lifetime, immutability, and bounds. It
does not claim that its bytes are UTF-8, well-formed text, policy-permitted, or
safe to render. Those properties are neither checked nor cached when the range
is created.

Equality is ordinal over the selected bytes, not backing identity, offset, or
object identity. Two ranges over different backings compare equal when their
bytes are equal. The zero value denotes the empty range and agrees with an
explicit empty value for equality and hashing.

No public operation returns the backing, a mutable alias, `byte[]`,
`Memory<byte>`, `ReadOnlyMemory<byte>`, `ReadOnlySpan<byte>`, a stream, or a
pointer. A domain value retains an `OpaqueByteRange`; it does not retain an
interior managed reference whose validity depends on a movable object or stack
frame. The separately named encoder may write an exact copy to
destination-owned storage, but it never lends the backing.

### Scoped readers and builders

Mutable builders and live-source readers are `ref struct` values. They may
inspect a scoped source, write directly into the final population backing, and
record offsets and lengths. They do not issue retained views while that
backing remains mutable. Freezing the population transfers its storage to the
hidden immutable owner and resolves recorded slots into `OpaqueByteRange`
snapshots.

`InertText.OpaqueByteRangeBuilder` is the public builder every domain uses:

- its constructor declares the exact population length and reserves one
  backing;
- `Append(ReadOnlySpan<byte>)` copies source bytes into that backing and
  returns an `OpaqueByteRangeSlot`, which records an offset and length but is
  not a range;
- `Freeze()` refuses an incomplete population, marks the backing immutable, and
  returns the complete population as one range; and
- `OpaqueByteRange.Slice(OpaqueByteRangeSlot)` resolves recorded slots against
  that frozen range.

The internal range constructor refuses a backing that is not frozen, so no
range can exist over storage that is still being written. Copies of the builder
share one write position and frozen state; a copy can neither overwrite
appended bytes nor append after freezing. Domain assemblies use only this
public surface; they do not construct backings or ranges through internal
access.

A domain may use a scoped structural reader to interpret tags, lengths, and
integer parts within an opaque range. Such a reader returns semantic values,
checked child ranges, or typed domain views. It does not return a source span
or invoke caller code with source bytes.

`InertText.Utf8RangeReader` is the scoped UTF-8 query reader for a range that a
domain has classified as text. It also serves as the input when a contained
presentation value is required. It has no public constructor. A typed domain
text view issues it through
`InertText.Encoding.Utf8RangeClassification.Read(OpaqueByteRange)`, the single
public issuer. That conspicuous entry point records the domain's text
classification at each call site; like the encoders, it is an audit and
discoverability boundary, not a capability barrier. Binary key and Blob views
do not call it.

Its initial distinguishing capability is `ValueTextEquals`: evaluate whether
the stored range equals caller-supplied UTF-8 query bytes without decoding the
stored candidate. Like the analogous `Utf8JsonReader` operation, it returns an
answer rather than the value span. This closed range evaluation is the ordinary
path; it creates no `InertUtf8String` and does not continue into containment.
When a contained presentation value is separately required, the reader carries
the classified source into `InertUtf8String` construction without exposing that
source. Creating this reader does not validate the stored bytes or apply
`TextPolicy`.

### Inert UTF-8

`InertUtf8String` is a small `readonly struct` over an `OpaqueByteRange` whose
bytes are already a valid, visually contained UTF-8 spelling. It is a
demand-created presentation result, not the representation used by ordinary
analysis or matching. It also carries the same `VisualForm`, `TextConcern`,
and truncation evidence as `InertString`.

Its ordinary constructor is
`InertUtf8String(TextPolicy, Utf8RangeReader)`. Like
`InertString(TextPolicy, string)`, the constructor forwards to the encoder and
assigns only its result. `Utf8VisualEncoder.Encode` scans the reader's exact
classified source, applies the named policy, and returns an `InertUtf8String`
through an internal already-contained constructor. There is no public
constructor from `OpaqueByteRange` and no constructor that accepts a reader
without a policy. A caller chooses whether it needs a display value; once it
constructs this type, containment is not optional.

The name pairs `InertUtf8String` with `InertString` the way the
[`Utf8String` proposal](https://github.com/dotnet/runtime/issues/933) paired
`Utf8String` with `string`: UTF-16 is .NET's default text encoding, so the
UTF-8 currency names its encoding. Any length or offset it reports counts
UTF-8 bytes, not UTF-16 code units.

`InertText.Encoding.Utf8VisualEncoder` establishes the invariant. The
`InertUtf8String` constructor is its primary caller. The encoder accepts a
`Utf8RangeReader`, scans its exact classified source, decodes UTF-8 without
replacement, applies the named `TextPolicy`, and returns `InertUtf8String`.

- If every source byte forms permitted UTF-8 text, the result may retain the
  original range after that scan proves the stronger invariant.
- If a scalar requires visible spelling or the source contains an ill-formed
  byte, the encoder writes one canonical contained UTF-8 result into a new
  immutable backing and retains that range.

Each ill-formed source byte receives its own reversible visible spelling, so
byte-distinct names remain visibly distinguishable. The encoder is total over
admitted byte ranges.

The spelling is the surrogate escape popularized by Python's
[PEP 383](https://peps.python.org/pep-0383/). A byte outside a well-formed
sequence is always `0x80` or above, and is spelled as the lone low surrogate
`U+DC80`–`U+DCFF` it maps to, so `0x80` becomes `\uDC80` with form `BmpHex`
and concern `Surrogate`. An ill-formed UTF-8 value is therefore spelled exactly
as `VisualEncoder` spells its surrogate-escaped UTF-16 string. That is what
makes the `InertString` projection lossless and decodable with the existing
UTF-16 decoder, which recovers the surrogate-escaped string.

Two consequences follow, and both are accepted:

- The same spelling denotes a UTF-8 byte or a UTF-16 lone surrogate. Neither
  currency records which encoding a value came from, and within one origin each
  spelling still has exactly one source.
- Composition happens in the UTF-16 currency, so a projected byte escape can
  land beside a high-surrogate escape from another fragment, exactly as two
  separately encoded UTF-16 fragments can. `Join` keeps both escapes visible,
  but the pair decodes as the one astral scalar it denotes. `EnsurePermitted`
  re-spells from that decoded text, so under a stricter policy the pair can
  become one permitted graphic glyph with no surrogate concern. Containment
  still holds; the record that an ill-formed byte was present does not survive
  that re-spelling.

`SpellingParityTests` gates parity in the PR lane: every scalar under every
policy, every ill-formed byte, escape-like backslash text, and generated byte
strings must produce identical spelling, forms, and concerns from both
encoders. Both encoders take escape length and form from one internal table and
keep separate writers.

`Utf8VisualEncoder.MeasureInertStringLength` returns the UTF-16 length of the
spelling `Encode` would produce, without producing it. It shares `Encode`'s
first pass, and the parity gate requires it to equal both the projected
`InertString` length and the UTF-16 encoder's measure for every input. A
producer that retains a name as bytes uses it to charge a budget counted in
contained UTF-16 characters while leaving containment to the sink.

Equality follows `InertString`: the contained bytes and truncation participate,
while forms and concerns do not, because the encoder emits one canonical
spelling from which both follow. No operation bounds a UTF-8 value yet, so its
truncation flag stays false until a byte budget is introduced.

`InertUtf8String` may write its already-contained bytes to a fixed UTF-8 sink
and may produce `InertString` through fixed UTF-8-to-UTF-16 conversion. Neither
operation re-applies policy. It exposes no general raw-byte span.

For byte-originated text, `InertString` derives from `InertUtf8String`, never
directly from untreated `OpaqueByteRange`. Its physical representation need not
retain the `InertUtf8String` value, but its spelling and evidence must be the
same. Existing string-originated `InertString` construction continues to use
`VisualEncoder`.

### Shapes and lifetimes

| Shape | Role | Reason |
| --- | --- | --- |
| Hidden sealed reference object | Retains immutable storage shared by ranges | One object owns lifetime and prevents a class allocation per range |
| `readonly struct OpaqueByteRange` | Retains owner identity plus offset and length | Cheap ownership snapshot with exact byte value semantics |
| `readonly struct` domain view | Adds validated domain meaning to a range | Domain values allocate no storage and cannot weaken opacity |
| `ref struct` builder or reader | Performs scoped mutation, borrowing, parsing, or matching | Stack lifetime prevents a live source or mutable builder from escaping |
| `readonly struct InertUtf8String` | Retains contained UTF-8 and evidence | Byte sinks need no UTF-16 materialization |
| Existing `readonly struct InertString` | Retains contained UTF-16 and evidence | String sinks keep the established currency and safe `ToString()` |

The backing reference object is the only mandatory heap object for one compact
population. There is no class per range, name, nested key, or typed view.
Readonly structs do not attempt once-only lazy caching; a sink that retains
presentation owns that presentation lifetime.

### Construction and detachment

A producer writes bytes directly into the backing selected for the completed
population. Domain builders add their grammar while using the shared builder;
they do not first construct strings, per-name arrays, or a second complete
domain buffer.

An independently retained value cannot borrow a span from a disposed
`PEReader`. Detaching from such a source therefore requires one ownership copy
unless the source owner itself can be retained immutably. That copy writes
directly to the destination backing and is not repeated by ranges or domain
views.

A live resource may separately offer a synchronous scoped borrow under
[Resource Ownership and Borrowing](resource-ownership-and-borrowing.md). This
design consumes that ownership rule; it does not create a second borrowing
protocol.

## Closed operations

### Opaque range operations

The initial closed operation set is:

- length and emptiness;
- byte-exact equality;
- process-local hashing consistent with equality;
- ordinal ordering, required by Symbol Key;
- checked slicing; and
- bounded structural answers required by domain-owned readers.

Hashing is process-local collection support. It is not a persisted identity,
content digest, signature, or cross-process compatibility promise.

Repeated structural reads could deliberately reconstruct bytes. The repository
does not treat trusted in-process code as hostile; the boundary prevents
accidental bulk propagation and gives value-flow analysis one small surface to
audit. A convenience that returns bulk untreated content or invokes caller
code with it would change this contract and requires a focused design change.

### UTF-8 query operations

After a typed domain text view issues `Utf8RangeReader`, `ValueTextEquals`
compares its source with caller-supplied UTF-8 query bytes. The query is input
to the operation and is never retained. A consumer encodes a textual request
once and compares it with every candidate; stored candidates are not decoded,
normalized, or visually encoded.

`ValueTextEquals` is byte-exact. It neither establishes that the stored range
is well-formed UTF-8 nor changes identity for ill-formed metadata names.
Domain equality over arbitrary binary keys remains the ordinal
`OpaqueByteRange` operation.

The initial contract does not include culture-sensitive, normalized,
case-folded, or callback-defined matching. Each would need its own exact
semantics and evidence that callers do not inherit a hardening obligation.

### Encoding and persistence

Exceptional byte transfer and visual containment consistently use the
**Encoder** vocabulary. They live in `InertText.Encoding`, separate from the
ordinary range and query surfaces:

- `OpaqueByteRangeEncoder` writes an exact range to destination-owned binary
  storage without lending the backing; and
- `Utf8VisualEncoder` interprets a `Utf8RangeReader` source as UTF-8, applies
  policy, and returns `InertUtf8String`; its primary caller is that containing
  type's public constructor.

The exact-byte encoder is deliberately conspicuous. It is the escape
capability required by validated domain persistence, not a normal analysis or
matching operation. The domain still owns whether transfer is permitted and
which representation receives it. For example, a
`PersistedSymbolKeyEncoder` first enforces the persisted profile, then composes
`OpaqueByteRangeEncoder` to produce its bytes or canonical base64url form.

`InertText.Encoding.Utf8VisualEncoder` owns the UTF-8 transition, mirroring
the existing `InertText.Encoding.VisualEncoder` for UTF-16.
`InertUtf8String` construction invokes it so routine producers need only the
currency namespace. The encoder is both the code that must inspect untreated
input and the code that establishes the safe result; keeping it in the
conspicuous `InertText.Encoding` namespace makes that narrow transition easy to
audit. Direct invocation by trusted code remains possible and explicit, just
as it does for `VisualEncoder`; the namespace is an audit and discoverability
boundary, not a capability barrier.

Any explicit recovery of original untreated bytes also remains in that
conspicuous namespace rather than on `InertUtf8String` or
`OpaqueByteRange`.

## Demand-driven containment contract

The ordinary reader path terminates in closed answers and does not create a
contained string. When a caller separately needs a display value, construction
of the containing type owns the transition:

```text
OpaqueByteRange retained by a typed domain text view
  -- view issues -->
Utf8RangeReader
  +-- closed query --> answer; ordinary path ends
  +-- display value required -->
      new InertUtf8String(TextPolicy, reader)
        -- constructor calls Utf8VisualEncoder -->
        contained InertUtf8String
          -- fixed UTF-8-to-UTF-16 conversion --> InertString
```

1. `OpaqueByteRange` is safe by **opacity**. Its exact bytes remain untreated,
   but ordinary consumers receive only closed answers or further ranges.
2. `Utf8RangeReader` preserves the domain's text classification while ordinary
   queries remain closed; it does not imply presentation.
3. `InertUtf8String` is safe by **contained payload**. Its retained UTF-8 has
   already passed the named visual policy.
4. `InertString` is safe by the same contained-payload contract in UTF-16.

Only `InertUtf8String` construction crosses from opaque to contained, and that
constructor always invokes the encoder. A domain classification, reader, or
closed answer does not silently strengthen the bytes. Conversion from
`InertUtf8String` to `InertString` does not make another policy decision.

### Operations not admitted

The initial contract has no:

- returned raw span, memory, array, stream, or pointer projection;
- caller callback or general matcher registration receiving source data;
- decoding API returning untreated `string` or characters;
- public constructor that blesses an opaque range as `InertUtf8String`;
- exact-byte writer on the ordinary `OpaqueByteRange` surface;
- persisted hash;
- implicit text interpretation; or
- success-shaped fallback after a refused domain operation.

A future analyzer that cannot be expressed through the closed operations must
show which validated properties remove its hardening obligation. "The bytes
required no visual encoding" is insufficient: display safety does not prove
that an arbitrary parser is bounded or total.

## Domain views

A domain view is a cheap typed interpretation of one `OpaqueByteRange`. It
retains the range, adds domain validation and vocabulary, and delegates generic
byte operations to the range.

Domain views own:

- grammar and marker meaning;
- validation and typed non-success;
- domain-specific budgets;
- semantic equality when it differs from ordinal bytes;
- interpretation of child ranges;
- which exact ranges represent UTF-8 text and may issue
  `Utf8RangeReader`; and
- persistence eligibility, profiles, and domain-specific encoders.

They do not own:

- another storage buffer;
- general byte equality, hashing, or ordering;
- a borrowed raw-byte accessor or independent exact-byte transfer mechanism;
- visual-containment rules; or
- independent UTF-8-to-string conversion.

`SymbolKey<TDomain>` is the first domain view. It retains one
`OpaqueByteRange`; its `Name`, nested `Key`, and `Blob` parts retain subranges
over the same backing. Symbol Key continues to own its part grammar, profiles,
domain registry, persisted format, transport text, and interning semantics.
It stops owning the underlying byte string and generic operations.

An API qualified anchor can later retain typed declaration and signature views
over the same backing. That adoption must carry byte provenance through its
named-type and resolver boundaries; this design does not define anchor
identity.

## UTF-8 text and containment

### Text is a domain interpretation

An arbitrary opaque range is not automatically text. A domain identifies a
range as UTF-8 text when its grammar or source contract gives the exact bounded
range that meaning. It preserves that classification in a typed text view,
which can issue `Utf8RangeReader` without exposing its retained range. This
owner supplies the scoped reader and encoder; it does not own concepts such as
Symbol Key `Name`, metadata namespace, or package ID.

The range may contain ill-formed UTF-8. Replacement decoding is never used for
identity or matching. `Utf8RangeReader` still performs exact query matching;
construction of `InertUtf8String` invokes `Utf8VisualEncoder` internally to
validate and visibly contain the source.

### One path when presentation is requested

At presentation time, both sink forms derive from construction of the
containing type:

```text
presentation request for a typed domain Name view
  -> Utf8RangeReader retains the classified source
     -> new InertUtf8String(TextPolicy, reader)
        -> constructor calls Utf8VisualEncoder once
           -> contained InertUtf8String
           -> contained UTF-8 written directly
           -> or InertString with identical spelling and evidence
```

Well-formed scalars follow the existing `TextPolicy` rules. Ill-formed source
bytes receive canonical reversible spellings. Direct UTF-8 and `InertString`
routes report the same `VisualForm`, `TextConcern`, and truncation evidence.

Whether a product view prints a contained spelling or substitutes
`InertString.ContainmentRequiredPlaceholder` remains a sink disclosure
decision. This design guarantees safe projection, not mandatory disclosure.

No presentation result is cached in an identity value. A later sparse cache
requires its own lifetime, memory, and performance evidence.

## What zero copy means

The primary claim is **zero intermediate copy**, not that bytes never move:

- creating opaque ranges and domain views copies no payload;
- equality, hashing, ordering, matching, and structural reads allocate and
  copy no payload;
- textual queries are encoded once outside the candidate loop;
- when presentation is requested, a clean `InertUtf8String` may reuse its
  source backing after validation;
- when presentation requires transformation, `InertUtf8String` pays for one
  contained UTF-8 representation;
- a byte-capable presentation sink receives contained UTF-8 without an
  intermediate string; and
- a string presentation sink pays once for the required UTF-16 representation.

Output necessarily writes bytes into the sink, and detaching from a disposable
source may require one ownership copy. Those are destination and lifetime
boundaries, not intermediate representations.

The revised Symbol Key pilot must test whether the additional owner reference,
range values, scoped readers, and containment boundary preserve or improve the
private implementation's time and memory behavior. Zero-intermediate-copy
structure is not itself performance proof.

## Failure and bounds

Failures remain visible:

- a producer that exceeds its declared storage or work budget refuses
  construction;
- a domain parser returns its typed malformed, unsupported, or bounded-out
  result;
- a checked range operation rejects an invalid range rather than clipping it;
- `Utf8VisualEncoder` is total over admitted bytes, including ill-formed
  UTF-8; and
- a sink or domain encoder failure propagates rather than reporting successful
  output.

The producer owns population and total-work budgets because only it knows the
artifact operation being performed. `OpaqueByteRange` owns range arithmetic
and prevents a read outside its selected backing. A domain owns grammar depth,
part length, semantic validation, and persistence. Inert Text owns visual
policy. These layers do not repeat one another's checks.

## Audit boundary

The external actor is the artifact author. The input path is an admitted
artifact byte source. The permitted exits are:

> Untreated bytes remain in immutable backing and cross its ordinary public
> boundary only as a closed answer, a bounded `OpaqueByteRange` snapshot, or a
> typed domain view. Only a typed domain text view may issue
> `Utf8RangeReader`; ordinary analysis and matching terminate in closed reader
> answers. A display producer constructs `InertUtf8String` from `TextPolicy`
> and that reader; the constructor unconditionally invokes
> `Utf8VisualEncoder`, and byte-originated `InertString` derives from the
> contained result. Exceptional exact-byte transfer names
> `OpaqueByteRangeEncoder`, and domain persistence composes that capability only
> after applying its own eligibility rules.

The implementation gate combines:

- public-surface inspection rejecting raw arrays, spans, memory, strings,
  pointers, streams, callbacks, a public `InertUtf8String` constructor from
  `OpaqueByteRange`, and any reader constructor without `TextPolicy`;
- field-rooted value flow from backing storage to the closed operations,
  domain encoders, and visual encoder, with incompleteness reported as defined
  by [#9908](https://github.com/richlander/dotnet-inspect/issues/9908); and
- known-answer tests planting forbidden raw projections.

These gates audit trusted code against accidental propagation. They are not a
capability barrier against a malicious in-repository caller and do not broaden
the repository threat model.

## Evidence plan

| Property | Gate | Lane |
| --- | --- | --- |
| Immutable ownership snapshot | Equal ranges over different backings compare and hash equally; default and explicit empty agree; slices retain their owner | PR-fast |
| Bounds | Empty, edge, nested, overflowed, and out-of-range operations return the specified result without reading outside the backing | PR-fast |
| Freeze before issue | A builder refuses overflow, incomplete freeze, and any write or freeze after freezing, including through an earlier copy; no range can be constructed over a writable backing | PR-fast |
| Public domain boundary | Compiled member references from `ILInspector.Metadata` into `InertText` resolve only to public members, except the named encoded-length preflight; `Utf8RangeClassification.Read` is the only public `Utf8RangeReader` issuer | PR-fast |
| No raw projection | Compiled public-surface and field-flow gates reject every forbidden return, callback, store shape, and `InertUtf8String` blessing path | PR-fast |
| Opaque means untreated | Creating ranges and UTF-8 readers performs no UTF-8 scan, normalization, or `TextPolicy` work | PR-fast |
| `ValueTextEquals` | Ordinary, non-ASCII, concerning, and ill-formed stored names compare byte-exactly with pre-encoded queries without decoding candidates, applying policy, or creating `InertUtf8String` | PR-fast |
| Domain boundary | Only a typed domain text view can issue `Utf8RangeReader`; ordinary closed queries do not enter containment; `InertUtf8String` construction accepts that reader rather than `OpaqueByteRange`, so binary key and Blob views cannot accidentally take that route | PR-fast |
| Containing type owns encoding | `new InertUtf8String(policy, reader)` always delegates to `Utf8VisualEncoder`; compiled public-surface and construction tests reject a range constructor, a policy-free reader constructor, or another field-assignment path | PR-fast |
| Exact-byte encoder | Exact bytes reach destination-owned storage without lending the backing; domain eligibility and destination failures remain visible | PR-fast |
| Containment parity | Printing is admitted only from constructed `InertUtf8String`; `SpellingParityTests` compares both encoders over every scalar and policy, every ill-formed byte against its surrogate escape, escape-like backslash text, and generated byte strings for identical spelling, concern, and form evidence | PR-fast |
| Shared backing | A generated population of names and nested domain parts retains one backing rather than one array or class per view | PR-fast |
| Exact-Type selected identity | A fully qualified exact nested Type detaches only its matched namespace and root-to-leaf Name segments into one backing, preserves segment boundaries, keeps raw lookup identity unchanged, and carries the separately contained presentation without applying identity limits to its expanded spelling | First adoption PR |
| Exact-Type base/head | NativeAOT exact-command time and allocation comparison preserves result semantics; retained-memory claims include the detached matched Name backing and do not count UTF-16 compatibility output as byte-native savings | First adoption PR |
| Host-input optimization | Exact query bytes are encoded once, typed match evidence is available only after exact equality, and the candidate is compared with the merged exact-Type baseline | Follow-up adoption PR |
| Exact-Type direct byte output | Direct-library clean non-generic `Type Info` TSV preserves byte-identical output through host input, metadata proof, `TypeSubject`, Presentation, and the CLI UTF-8 sink; ineligible shapes reproduce the established fallback output | End-to-end Type adoption |
| Exact-Type one-shot cost | Exact-base/head NativeAOT comparison includes command input construction, plan construction, inspection, Presentation, and output; kernel-only measurements remain separately labeled | End-to-end Type adoption |
| Symbol Key representation pilot | SPC comparison of the current private Symbol Key and the proposed range-backed model measures construction and lookup time, allocated bytes, and retained managed memory on the same pinned census; exact identity answers must agree | Completed pilot |
| Finding Diff Any product path | Exact base/head NativeAOT comparison on all pinned Finding Diff Any pairs, with identity answers unchanged and no regression | Symbol Key implementation adoption |
| Finding Diff Any direct byte output | Exact-answer and allocation comparison for the first Diff sink that stays byte-native after `InertUtf8String` construction | Diff output adoption |

Documentation-only design changes require Markdown validation, not product
builds. Performance claims become implementation claims only when their named
exact-base/head gates run.

## Adoption sequence

1. **Opaque range and Inert UTF-8 design.** This document fixes ownership,
   representation, closed operations, containment, and non-claims.
2. **Symbol Key design and representation pilot.** Restack
   [#9932](https://github.com/richlander/dotnet-inspect/pull/9932) on this
   design, then compare the current private byte key with a range-backed
   prototype using time and memory SPC. Preserve negative or neutral evidence.
3. **Exact-Type baseline adoption.** Implement the immutable backing, opaque
   range, scoped reader, and Inert UTF-8 containment path. Detach only the
   unique fully qualified Type match into `MetadataUtf8TypeDefinitionName`;
   preserve the existing search algorithm and raw structured identity, and
   carry contained presentation separately from that identity.
4. **Exact-Type host-input optimization.** Add the auditable host-created
   exact-input type, return typed evidence from the same successful byte
   comparisons, and compare against the merged step 3 baseline.
5. **Exact-Type end-to-end output.** Carry the proven contained Type through
   `TypeSubject` into the first direct Presentation document and write it to a
   byte-capable CLI sink. Compare the complete one-shot command with the
   pre-adoption path.
6. **Shared Symbol Key and Finding Diff Any adoption.** Move Finding Diff
   Any's private Symbol Key onto the shared representation and remove the
   duplicate storage owner only with exact base/head NativeAOT parity.
7. **API qualified anchor adoption.** Carry byte-backed names through
   declaration, signature, and resolver boundaries under
   [#9907](https://github.com/richlander/dotnet-inspect/issues/9907).
8. **Byte-native Finding Diff Any result.** Keep applicable result names as
   domain views under Finding Diff Any's focused owner.
9. **Direct Finding Diff Any Presentation output.** Let Presentation consume
   those views and write contained UTF-8 directly at byte-capable sinks.
10. **CLI and Browser/Wasm completion.** Adopt the shared identities and direct
   projections in the product paths tracked by their owning issues.

Each adoption is independently reviewable and keeps its domain semantics with
its owner. This design does not authorize a multi-owner implementation PR.

## Non-claims

- No claim that every artifact byte sequence is text.
- No UTF-8 validation or visual-policy work when creating or matching an
  `OpaqueByteRange`.
- No raw byte access for arbitrary analyzers.
- No claim that the shared representation beats Finding Diff Any's private
  Symbol Key before the revised pilot.
- No requirement that every existing string model migrate at once.
- No domain identity, key grammar, diff policy, or anchor correspondence rule.
- No stable hash, cryptographic identity, or tamper evidence.
- No generic persistence, compression, or interop transport format.
- No presentation cache or promise that every sink retains zero allocations.
- No claim that display-safe bytes are safe for an arbitrary parser.
- No replacement for `InertString` or its unrestricted conversion to an
  already-contained `string`.
