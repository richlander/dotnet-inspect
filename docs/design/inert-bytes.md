# Inert Bytes

## Status and owner

**Design only; no implementation or production adoption is claimed.**
[#9991](https://github.com/richlander/dotnet-inspect/issues/9991) tracks this
focused design.

**Inert Bytes** is a shared leaf capability. It owns this exact claim:

> One immutable byte backing can be retained through cheap bounded ranges.
> `InertBytes` answers closed identity, matching, structural-read, and
> contained-projection operations without copying the range or lending a raw
> view. It can write an opaque range to a declared binary destination that
> does not interpret it as text. Domain representations retain ranges over
> that backing rather than creating another byte or string representation.

This owner defines:

- immutable backing and range lifetime;
- the owner/range representation split;
- byte-exact equality, hashing, ordering, and query comparison;
- bounded slicing and structural reads that return values or further ranges;
- the rule deciding whether an operation belongs on untreated bytes;
- explicit opaque transfer to a destination that consumes binary data without
  interpreting it as text;
- UTF-8 classification and the only route from an untreated range to contained
  text or contained UTF-8 output; and
- the zero-intermediate-copy claim and its limits.

It does not define:

- a domain grammar, marker vocabulary, identity profile, or validation policy;
- which metadata facts participate in API, Diff, or other product identity;
- the visual-containment policy or spelling, which remain with
  [Inert Text](inert-text.md);
- ownership of a live artifact resource or a borrow from one, which remains
  with [Resource Ownership and Borrowing](resource-ownership-and-borrowing.md);
- a persisted format, transport encoding, interning scope, or stable hash; or
- whether a sink discloses contained text or substitutes a placeholder.

The initial implementation may share the dependency-free `InertText` assembly
without making the two focused owners one component. Co-location lets the
untreated-byte projection invoke the closed Inert Text policy and encoder
without a friend assembly or a callback that receives source bytes.
Architectural ownership follows these focused contracts, not the project
boundary.

`docs/overview.md` is release-managed. The release tracker records its future
authority entry rather than this design editing that central file.

## Product question

**How can inspection retain artifact text as bytes for analysis and matching,
then contain it exactly once when a sink actually needs text?**

Metadata names begin as bytes. Current paths commonly decode them to UTF-16,
retain them as `string` or `InertString`, and then use that presentation form
for matching. That has three costs:

1. UTF-16 commonly requires twice the payload storage of ordinary UTF-8.
2. Decoding and containment happen even when analysis only compares names.
3. A presentation-safe spelling is a poor matching substrate because encoding
   changes the value being compared.

Returning a raw span instead is not the answer. It makes every receiver a new
boundary: the matcher must validate, bound, contain, and avoid leaking the
untreated value. The attractive zero-copy API then distributes the hardening
obligation across every analyzer.

The target is closer to a signing device than a byte-array wrapper:

```text
producer writes or retains bytes
  -> InertBytes owns one immutable backing and answers operations
     -> domain views retain bounded ranges
        -> matchers receive answers, not untreated storage
        -> opaque binary sinks receive an explicit write
        -> byte sinks receive directly contained UTF-8
        -> string sinks receive InertString
```

The operation boundary, rather than visual-encoding status by itself, decides
whether access is safe:

> An operation belongs on `InertBytes` when it can return its result without
> requiring the matcher, analyzer, or sink to harden itself against the stored
> value.

## Motivating evidence

Fast Diff's private `SymbolKey`, introduced by
[#9898](https://github.com/richlander/dotnet-inspect/pull/9898), copies metadata
names directly into a byte key and compares the finished keys as bytes. That
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

The pilot is a representation kernel, not complete product-path evidence. Its
retained-display scenario also represents full cache saturation, not sparse
last-mile projection. It shows why ranges should share one backing and why the
identity value should not retain a presentation object for every name.

Together, Fast Diff and the pilot establish the design direction: producers
that write UTF-8 directly into final byte-native storage and consumers that
compare that storage can avoid UTF-16 and per-name allocation. They do not
establish literal zero-copy detachment from a disposable metadata reader.

## Representation

### One backing, many ranges

An ordinary retained `InertBytes` value has value semantics over one bounded
range. Its backing is independently retained and immutable for the complete
lifetime of every range. Creating a range or subrange does not copy bytes.

The conceptual shape is:

```text
immutable backing
  range [offset, length]                 -> InertBytes
    domain interpretation               -> SymbolKey<TDomain>
    UTF-8 text interpretation           -> UTF-8 name view
    nested range [offset, length]        -> InertBytes
```

Equality is ordinal over the selected bytes, not backing identity, offset, or
object identity. Two ranges over different backings compare equal when their
bytes are equal. The zero value denotes the empty range and agrees with an
explicit empty value for equality and hashing.

No public operation returns the backing, a mutable alias, `byte[]`,
`Memory<byte>`, `ReadOnlyMemory<byte>`, or `ReadOnlySpan<byte>`. A domain value
retains an `InertBytes` range; it does not retain an interior managed reference
whose validity depends on a movable object or stack frame.

One explicit operation may write the selected bytes to a declared opaque
binary destination. It transfers content, not a borrow: the destination owns
the written copy and receives no reference to the backing. This is the route
for a domain's persistence or structural encoding when the destination does
not parse, render, or otherwise interpret the source bytes.

### Structs, classes, and ref structs

The representation uses each C# shape for the lifetime it can state:

| Shape | Role | Reason |
| --- | --- | --- |
| One hidden sealed reference object per immutable backing | Retains storage shared by ranges | One object owns lifetime and prevents a class allocation per name or domain view |
| Small `readonly struct` for `InertBytes` | Carries backing identity plus a bounded range | Copies are cheap, preserve value semantics, and retain the backing through its reference |
| Small `readonly struct` for domain views | Adds validated domain meaning to one range | `SymbolKey<TDomain>` and similar values do not allocate or duplicate storage |
| `ref struct` for a mutable builder or a scoped live-source reader | Enforces stack-bounded mutation or borrowing | A builder or live borrow must not be retained as an immutable detached value |
| Existing `readonly struct` `InertString` | Carries materialized contained UTF-16 | It remains the sink-facing string currency rather than the identity backing |

The backing reference object is the only mandatory heap object for one compact
population. There is no class per range, name, nested key, or typed view.
Domain structs stay small; indexes or validation tables that would make each
view large belong once with the backing or the domain owner.

A readonly struct does not attempt once-only lazy caching. Copies cannot
coordinate such a cache without introducing another reference object per
value. A sink that retains presentation owns that presentation lifetime.

### Construction and detachment

A producer writes bytes directly into the backing selected for the completed
value. Domain builders add their grammar while using the shared byte builder;
they do not first construct strings, per-name arrays, or a second complete
domain buffer.

An independently retained value cannot borrow a span from a disposed
`PEReader`. Detaching from such a source therefore requires one ownership copy
unless the source owner itself can be retained immutably. That copy writes
directly to the destination backing and is not repeated by domain views.

A live resource may separately offer a synchronous scoped borrow under
[Resource Ownership and Borrowing](resource-ownership-and-borrowing.md). The
borrow is stack-only and cannot become an ordinary retained `InertBytes`.
This design consumes that ownership rule; it does not create a second borrowing
protocol.

## Closed operations

### Operations available for every range

The initial closed operation set is:

- length and emptiness;
- byte-exact equality;
- process-local hashing consistent with equality;
- ordinal ordering, required by Symbol Key;
- comparison with caller-supplied query bytes; and
- checked slicing; and
- explicit writing to an opaque binary destination.

The query is input to the operation and is never retained. For textual queries,
the consumer encodes once and compares that UTF-8 against every candidate,
following the `Utf8JsonReader.ValueTextEquals` pattern. Stored candidates are
not decoded.

Hashing is process-local collection support. It is not a persisted identity,
content digest, signature, or cross-process compatibility promise.

### Opaque binary transfer

Opaque transfer is admitted by the same hardening test as matching. A binary
destination that copies, persists, or structurally encodes the complete range
does not need to validate text, defend a parser, or contain rendering. The
operation writes into destination-owned storage and returns completion or a
visible destination failure; it never returns a borrowed source view.

The domain decides when binary transfer is meaningful. For example, a
validated persisted Symbol Key may be written to an artifact or fed directly
to its base64url encoder. `InertBytes` owns the transfer mechanism, while
Symbol Key continues to own the persisted profile and the rule that
unvalidated or in-process-only keys cannot take that route.

Opaque binary output is not text output. A destination that will render or
place the value in a textual grammar must use a domain structural encoding,
such as Symbol Key's base64url form, or the contained UTF-8 path for a range
classified as text.

### Structural reads

A validated domain view sometimes needs to read tags, lengths, or integer
parts. `InertBytes` therefore provides bounded structural operations that
return primitive values or further `InertBytes` ranges. Every read checks its
range and advances only within it.

These operations do not return a raw range to caller code. They let a parser
answer questions such as "what is the next tag?" and "what bounded child range
does this length describe?" without exposing a byte span. Domain validation,
shortest-form integer rules, marker vocabularies, and nesting budgets remain
with the domain owner.

Repeated structural reads could deliberately reconstruct bytes. The repository
does not treat trusted in-process code as hostile; the boundary prevents
accidental raw propagation and gives value-flow analysis one small surface to
audit. A new convenience that returns bulk untreated data would change that
surface and requires a focused design change.

### Operations not admitted

The initial contract has no:

- returned raw span, memory, array, stream, or pointer projection;
- caller callback or general matcher registration receiving source data;
- decoding API returning untreated `string` or characters;
- persisted hash;
- implicit text interpretation; or
- success-shaped fallback after a refused domain operation.

A future analyzer that cannot be expressed through the closed operations must
show which validated properties remove its hardening obligation. "The bytes
required no visual encoding" is insufficient: display safety does not prove
that an arbitrary parser is bounded or total.

## Domain views

A domain view is a cheap typed interpretation of one `InertBytes` range. It
retains the range, adds domain validation and vocabulary, and delegates generic
byte operations to the range.

Domain views own:

- grammar and marker meaning;
- validation and typed non-success;
- domain-specific budgets;
- semantic equality when it differs from ordinal bytes;
- interpretation of child ranges; and
- which ranges represent UTF-8 text.

They do not own:

- another storage buffer;
- general byte equality, hashing, or ordering;
- a borrowed raw-byte accessor or independent binary-transfer mechanism;
- visual-containment rules; or
- independent UTF-8-to-string conversion.

`SymbolKey<TDomain>` is the first domain view. It retains one `InertBytes`
range; its `Name`, nested `Key`, and `Blob` parts retain subranges over the same
backing. Symbol Key continues to own its part grammar, profiles, domain
registry, persisted format, transport text, and interning semantics. A
validated persisted key uses the backing's opaque binary transfer when writing
that format. Symbol Key stops owning the underlying byte string and generic
operations.

An API qualified anchor can later retain typed declaration and signature views
over the same backing. That adoption must carry byte provenance through its
named-type and resolver boundaries; this design does not define anchor
identity.

## UTF-8 text and containment

### Text is a domain interpretation

An arbitrary byte range is not automatically text. A domain identifies a range
as UTF-8 text when its source contract gives the bytes that meaning. The range
may still contain ill-formed UTF-8; replacement decoding is never used for
identity or matching.

The UTF-8 text view adds:

- exact comparison with pre-encoded UTF-8 query text;
- direct contained UTF-8 output under a named `TextPolicy`; and
- contained UTF-16 materialization as `InertString`.

It does not expose untreated text. Binary `SymbolKey` bytes cannot accidentally
take the text projection; only a `Name` range classified by the domain can.

### One containment path

Both output forms use the same closed Inert Text policy and visual speller:

```text
UTF-8 name range
  -> scan and classify once for this projection
     -> contained UTF-8 written directly to a byte sink
     -> or contained UTF-16 retained as InertString
```

Well-formed scalars follow the existing `TextPolicy` rules. Each ill-formed
source byte receives its own canonical visible spelling, so byte-distinct
names remain visibly distinguishable. The projection reports the same
`VisualForm`, `TextConcern`, and truncation evidence in either output form.

The byte-output operation writes into caller-supplied destination storage but
does not return untreated source storage. When no visual spelling is required,
it copies the original UTF-8 directly to the final sink. When spelling is
required, it emits contained UTF-8 without an intermediate UTF-16 string.

`ToInertString` remains available for sinks that require a .NET string.
`InertString` continues to mean that its retained payload is already inert;
it does not become a byte owner or lazy wrapper.

Whether a product view prints a contained spelling or substitutes
`InertString.ContainmentRequiredPlaceholder` remains a sink disclosure
decision. `InertBytes` guarantees safe projection, not mandatory disclosure.

No presentation result is cached in the identity value. A later sparse cache
requires its own lifetime, memory, and performance evidence.

## What zero copy means

The primary claim is **zero intermediate copy**, not that bytes never move:

- creating ranges and domain views copies no payload;
- equality, hashing, ordering, matching, and structural reads allocate and
  copy no payload;
- textual queries are encoded once outside the candidate loop;
- an opaque binary sink receives the selected range without an intermediate
  domain buffer;
- a byte-capable sink receives contained UTF-8 without an intermediate string
  or presentation array; and
- a string sink pays once for the required `InertString`.

Output necessarily writes bytes into the sink, and detaching from a disposable
source may require one ownership copy. Those are destination and lifetime
boundaries, not intermediate representations.

A retained immutable source plus a byte-capable sink can therefore remain
byte-native all the way through analysis, domain views, a Fast Diff-owned
result, Presentation containment, and final output. Fast Diff is the first
planned proof: after Symbol Key adopts the backing, a separate Fast Diff slice
can keep matched names and result representations byte-native until
Presentation asks for contained output.

## Failure and bounds

Failures remain visible:

- a producer that exceeds its declared storage or work budget refuses
  construction;
- a domain parser returns its typed malformed, unsupported, or bounded-out
  result;
- a checked range operation rejects an invalid range rather than clipping it;
- containment is total over admitted bytes, including ill-formed UTF-8; and
- a destination failure propagates rather than reporting successful output.

The producer owns population and total-work budgets because only it knows the
artifact operation being performed. `InertBytes` owns range arithmetic and
prevents a read outside its selected backing. A domain owns grammar depth,
part length, and semantic validation. These layers do not repeat one another's
checks.

## Audit boundary

The external actor is the artifact author. The input path is an admitted
artifact byte source. The invariant is:

> Untreated bytes remain in immutable backing and cross its public boundary
> only as an answer, a bounded `InertBytes` subrange, an explicit write to a
> declared opaque binary destination, or output contained by the closed Inert
> Text policy.

The implementation gate combines:

- public-surface inspection rejecting raw arrays, spans, memory, strings,
  pointers, streams, and caller callbacks;
- field-rooted value flow from backing storage to the closed operations and
  containment projections, with incompleteness reported as defined by
  [#9908](https://github.com/richlander/dotnet-inspect/issues/9908); and
- known-answer tests planting a forbidden raw projection.

These gates audit trusted code against accidental propagation. They are not a
capability barrier against a malicious in-repository caller and do not broaden
the repository threat model.

## Evidence plan

| Property | Gate | Lane |
| --- | --- | --- |
| Immutable range value | Equal ranges over different backings compare and hash equally; default and explicit empty agree; slices retain their owner | PR-fast |
| Bounds | Empty, edge, nested, overflowed, and out-of-range structural reads return the specified result without reading outside the backing | PR-fast |
| No raw projection | Compiled public-surface and field-flow gates reject every forbidden return, callback, and store shape | PR-fast |
| Opaque binary transfer | Exact bytes reach the declared binary destination; no backing alias escapes; destination failure remains visible | PR-fast |
| Query matching | Ordinary, non-ASCII, concerning, and ill-formed UTF-8 names compare without decoding stored values | PR-fast |
| Containment parity | Well-formed inputs produce the same spelling and evidence through direct UTF-8 output and current UTF-16 `InertString`; byte-distinct malformed inputs remain visibly distinct | PR-fast |
| Shared backing | A generated population of names and nested domain parts retains one backing rather than one array or class per view | PR-fast |
| Representation performance | NativeAOT time, allocation, and retained-memory comparison against eager `string`/`InertString` on the pinned pilot census | Adoption PR |
| Fast Diff product path | Exact base/head NativeAOT comparison on all pinned Fast Diff pairs, with identity answers unchanged and no regression | Symbol Key implementation adoption |
| Direct byte output | Exact-answer and allocation comparison for the first Diff sink that stays byte-native through containment | Diff output adoption |

Documentation-only design changes require Markdown validation, not product
builds. Performance claims become implementation claims only when their named
exact-base/head gates run.

## Adoption sequence

1. **Inert Bytes design.** This document fixes ownership, representation,
   operations, containment, and non-claims.
2. **Symbol Key design adoption.** Restack
   [#9932](https://github.com/richlander/dotnet-inspect/pull/9932) on this
   design. Symbol Key becomes the first domain view and retains its grammar,
   persisted format, and interning claims.
3. **Shared implementation and Fast Diff adoption.** Implement the backing,
   range operations, and containment path; move Fast Diff's private Symbol Key
   onto it and remove the duplicate storage owner.
4. **API qualified anchor adoption.** Carry byte-backed names through
   declaration, signature, and resolver boundaries under
   [#9907](https://github.com/richlander/dotnet-inspect/issues/9907).
5. **Byte-native Fast Diff result.** Keep applicable result names as domain
   views under Fast Diff's focused owner.
6. **Direct Presentation output.** Let Presentation consume those views and
   write contained UTF-8 directly at byte-capable sinks.
7. **CLI and Browser/Wasm completion.** Adopt the shared identities and direct
   projections in the product paths tracked by their owning issues.

Each adoption is independently reviewable and keeps its domain semantics with
its owner. This design does not authorize a multi-owner implementation PR.

## Non-claims

- No claim that every artifact byte sequence is text.
- No raw byte access for arbitrary analyzers.
- No requirement that every existing string model migrate at once.
- No domain identity, key grammar, diff policy, or anchor correspondence rule.
- No stable hash, cryptographic identity, or tamper evidence.
- No persistence, compression, or interop transport format.
- No presentation cache or promise that every sink retains zero allocations.
- No claim that display-safe bytes are safe for an arbitrary parser.
- No replacement for `InertString` or its unrestricted conversion to an
  already-contained `string`.
