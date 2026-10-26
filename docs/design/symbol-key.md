# Symbol Key

## Status and owner

Focused design for
[#9918](https://github.com/richlander/dotnet-inspect/issues/9918). Its
representation pilot is complete, and adoption step 5 is implemented: the
shared in-process `SymbolKey<TDomain>`, its builder, the `#Strings` name
source, and the typed Name view live in `ILInspector.MetadataPrimitives`, and
Finding Diff Any's private key that
[#9898](https://github.com/richlander/dotnet-inspect/pull/9898) introduced is
retired onto them
([#10476](https://github.com/richlander/dotnet-inspect/issues/10476)), and
interning (step 7) is implemented for Finding Diff Any. The persisted
profile, its text form, and Browser exchange (steps 6 and 8) remain
unimplemented. The parent byte primitives first adopted a bounded
exact-Type result path; the adoption sequence below is the counted path from
that baseline to shared Symbol Key use in the CLI and Browser/Wasm hosts.

This is slice 2 of a two-PR design stack. Parent
[#9993](https://github.com/richlander/dotnet-inspect/pull/9993) establishes
[Opaque Byte Ranges and Inert UTF-8](inert-bytes.md) as the immutable backing,
opaque-query, and text-containment owner.

**Symbol Key** is a host-neutral Metadata domain view. It owns this exact claim:

> A Symbol Key is a domain-validated readonly view over an
> `OpaqueByteRange`. Its builder writes new keys as a sequence of
> self-delimiting parts, and its persisted reader validates that same grammar
> before issuing a view. Every key's first semantic part is a declared domain
> marker, so `Kind` is total. Two keys of one key domain are equal exactly
> when they contain the same parts. A Name part is exposed as a typed
> `SymbolKeyName` view that retains its untreated `OpaqueByteRange` without
> exposing it. That view may issue `Utf8RangeReader`; Symbol Key never decodes,
> validates, or visually encodes the Name. A persisted key is versioned and
> byte-identical across builds, platforms, and hosts for the same parts.

This owner defines:

- the part grammar: payload parts, the marker range, and their encoding;
- the builder, its name sources, and nesting;
- Name-part boundaries, their UTF-8 meaning, and the typed `SymbolKeyName`
  view that preserves that classification for shared query and containment
  operations;
- key domains and the compile-time separation between them;
- the in-process and persisted profiles, including the persisted header,
  validation, and text form; and
- scoped interning.

It does not define:

- immutable byte storage, range lifetime, generic byte operations, or
  exceptional exact-byte transfer, which remain with
  [Opaque Byte Ranges and Inert UTF-8](inert-bytes.md);
- any domain's marker vocabulary or what its keys identify, which remain with
  each consuming owner (for example, the
  [Identity](finding-diff-any.md#identity) section of Finding Diff Any);
- display text for names, which uses `InertUtf8String` and remains governed by
  [Inert Text](inert-text.md);
- structured API identity, which remains with
  [API Qualified Anchor](api-qualified-anchor.md); or
- work budgets for building keys over untrusted images, which remain with
  each producer, as Finding Diff Any's do.

## Why

Finding Diff Any's keys proved the domain grammar. Making keys injective by
construction, rather than by escaping, made Finding Diff Any 20% to 30% faster
on NativeAOT across its six pinned pairs, and keeping names as stored bytes
fixed a U+FFFD collision. The key, its builder, and its `#Strings` name reader
are `internal` to Finding Diff Any.

The parent owner generalizes the representation evidence: one immutable backing
supplies cheap `OpaqueByteRange` snapshots, generic byte operations, STJ-style
UTF-8 query matching, and one containing-type-owned presentation transition to
`InertUtf8String`. A bounded metadata exact-Type Name view is the first
production domain adoption. Symbol Key adds a grammar and profiles without
creating a second storage owner or applying presentation policy during
analysis.

Four planned consumers need the same thing, and each would otherwise grow a
private copy:

| Consumer | Needs |
| --- | --- |
| [API Qualified Anchor](api-qualified-anchor.md) ([#9907](https://github.com/richlander/dotnet-inspect/issues/9907)) | An encoded type or member identity, with typed records as a view; byte-exact name equality |
| [Ecosystem Receiver Index](ecosystem-receiver-index.md) | Persisted receiver and member keys in a checked-in artifact; the named lever if the artifact misses its [size goal](ecosystem-receiver-index.md#performance-goals) |
| Finding Diff Any step 5 | One interning table shared by both sides of a comparison |
| The C#/TypeScript boundary | An opaque key that TypeScript stores, compares, and passes back without decoding |

## Part grammar

A key is a non-empty sequence of parts. Every part begins with one tag byte.
An in-process key begins with a declared domain marker. A persisted key begins
with its header and then a declared domain marker. A nested `Key` payload has
no header and begins with its own declared domain marker.

| Tag | Part | Encoding after the tag |
| --- | --- | --- |
| `0x00` | Reserved | Never written |
| `0x01` | Int | Zig-zag signed LEB128 variable-length integer |
| `0x02` | Name | LEB128 length, then that many UTF-8 bytes, stored as given |
| `0x03` | Key | LEB128 length, then a nested key's bytes |
| `0x04` | Blob | LEB128 length, then that many raw bytes |
| `0x05` | Ref | LEB128 interned ID; in-process profile only |
| `0x06` | Persisted header | Format version, domain ID, and domain version, each LEB128; persisted profile only, first part only |
| `0x07`–`0x0F` | Reserved for this owner | Never written |
| `0x10`–`0xFF` | Domain marker | Nothing; the tag is the whole part |

Each part therefore decodes one way only, and a key decodes into exactly one
part sequence. The builder writes every integer and length in shortest-form
LEB128, so one part sequence also encodes to exactly one byte range. No name
is scanned, escaped, or normalized, whatever bytes it contains. Comparison
and ordering delegate to `OpaqueByteRange` ordinal operations, so order is a
function of the parts alone.

## Key domains

A **key domain** is one consumer's marker vocabulary: a byte-backed enum
whose values lie in the domain-marker range. Keys are typed by domain,
`SymbolKey<TDomain>`, and a builder is typed by the domain it writes. Keys of
different domains are different types and cannot be compared, mixed in one
collection, or nested in each other without an explicit conversion. Two
domains can therefore reuse marker values freely.

A domain declares its vocabulary once. Validating a domain type, when it is
first used, rejects an enum that is not byte-backed or that defines a value
below `0x10`.

The required first marker names the key's shape (`Type`, `Method`, `Field`),
and `Kind` returns it. Domain markers later in the key may describe structure
without changing its kind.

## Builder

The builder, `SymbolKeyBuilder<TDomain>`, stages one consumer-chosen
population in storage it owns. Completing a staged key records its offset and
length as a `StagedSymbolKey<TDomain>`, a builder-local slot that compares,
hashes, and orders by its bytes, including against staged keys of other
builders and against frozen keys, so a consumer matches while staging; it does
not issue a `SymbolKey<TDomain>` over mutable storage. Freezing the population
transfers the finished bytes through the shared range builder to one immutable
backing in one copy, and the returned `SymbolKeyPopulation<TDomain>` resolves
its staged keys into readonly `OpaqueByteRange`-backed key views; resetting
the population discards them instead, which is how a consumer compares
transient keys without ever freezing them. One key is open at a time: a key
embedded in another is completed first. A consumer that needs smaller
independent lifetimes chooses smaller population boundaries, and
`StagedSymbolKey<TDomain>.Detach` retains one key alone when a single key
must outlive an unfrozen population.

Nested construction has two explicit forms:

- an inline nested build writes directly inside the enclosing `Key` part and
  never issues an independently retained child; and
- embedding an independently completed slot writes that key's bytes into the
  enclosing `Key` payload.

The implementation provides the second form; the first is reserved for a
domain that measures the duplicate bytes as a cost worth its length patching.
The second form is a semantic copy required by the self-contained grammar:
both the independent key and its nested occurrence exist in the finished
population. It uses no intermediate array. A domain that adopts
[interning](#interning) replaces repeated embedded keys with `Ref` parts, as
Finding Diff Any does for the named Types its keys embed.

Finding Diff Any keeps two builders per side and spells rows through the
[shared speller](#metadata-rows). Its Type-level keys, Type definitions,
references, specifications, and primitives, memoized per token for the
comparison, are staged in a retained builder and frozen into shared backings
in chunks at unit boundaries; the unit table's lookup keys are resolved to
views at each chunk's freeze, and a dictionary keyed by frozen keys answers
staged lookups through the alternate lookup of `SymbolKeyComparer<TDomain>`.
Each Type-level key is also interned, when memoized, in the one
[interning table](#interning) the comparison's two sides share, and every
spelling that embeds a named Type (a definition, reference, or primitive),
whether a member key, a census fact, or a composite, writes its id as a `Ref`
part rather than its bytes; a Type specification's composite and a member
row embed as `Key` parts. Signature composites
built while one key decodes are staged in the speller's scratch population and
discarded when that spelling completes. Its member keys (methods, fields,
member references, method specifications, standalone signatures) and its census
facts are staged in a transient builder: a unit spells the members it declares
and the members its bodies refer to, memoized for that unit alone, holds its
facts as eight-byte slots that the builder sorts and compares against the other
side's with its storage resolved once, and discards all of it when the unit
ends, so no member spelling and no fact key is retained. The body comparison
remembers each pair of operand rows, one of each side, already proven to spell
the same key, so a member referred to from many Types is spelled once per
comparison, not once per referencing unit; what the comparison retains across
units is that pairing and the Type-level keys, not the member spellings.

Names enter through three sources:

| Source | Behavior |
| --- | --- |
| A `#Strings` handle | Copies the stored bytes directly into the final backing without decoding or Windows Runtime projection; a name offset outside the heap, or an unterminated entry, is malformed and throws `BadImageFormatException` |
| UTF-8 bytes | Writes the bytes as given into the final backing |
| UTF-16 text | Encodes well-formed text once into the final backing; text that contains a lone surrogate is refused, because it has no UTF-8 spelling and a replacement would collide |

The UTF-16 source exists so a consumer can compare user input with stored
names by encoding the query, never by decoding storage. A name built from
`#Strings` and one built from the same well-formed text are the same part.

Blobs are copied from a `BlobReader` without an intermediate array.

## Metadata rows

Two consumers spell the same metadata rows: Finding Diff Any, for its Types,
members, and the operands its body walk compares, and the IL census cursor,
whose atom's operand identity is the same key. One speller writes them, in
`ILInspector.MetadataPrimitives` where both can reach it.

`MetadataSymbolPart` is the shared marker vocabulary: `Type`, `Namespace`,
`Method`, `Field`, `MethodSpecification`, `LocalSignature`,
`StandaloneSignature`, `Primitive`, the signature composites (`SZArray`,
`Array`, `ByReference`, `Pointer`, `FunctionPointer`, `GenericInstance`,
`TypeParameter`, `MethodParameter`, `RequiredModifier`, `OptionalModifier`,
`Pinned`), `Vararg`, `Return`, and `Scope`. A consumer's domain declares the
values it uses under its own names, with the shared values, and the builder's
`MarkShared` writes a shared marker only when the domain declares it; a
domain that does not declare a marker cannot receive a spelling that uses it.
Two domains that declare the vocabulary spell one row with the same bytes.

`MetadataSymbolSpeller<TDomain, TRow>` spells one row into a builder the
consumer names: a Type definition as its namespace, declaring chain, and
names, and under `MetadataSymbolScope.Declared` the current assembly's name;
a Type reference as its chain and, under `Declared`, its resolution scope (the
scope's kind, then the assembly reference's name, version, culture, and public
key or token, or the module reference's name); a Type specification as
its decoded composite, or as the plain row it merely names, which the speller
returns for the consumer to memoize as that row; a method, field, member
reference, method specification, or standalone signature with its guarded,
decoded signature; and a primitive. Under `MetadataSymbolScope.Omitted`, the
Body policy's identity, a dependency's version is not a difference. Signature
composites are spelled in the speller's scratch population and embedded by
their consumer; the speller discards them when the outermost spelling
completes.

Rows a spelling refers to are the consumer's: `IMetadataRowKeys<TDomain, TRow>`
resolves a row to the consumer's key, spelling it into the consumer's own
builders and memo, and embeds a resolved key into an open one. So are a
definition's name part and, under `Declared`, a substituted scope name: the
speller asks the consumer to write the name of a Type in a declaring chain, a
method, or a field definition (Finding Diff Any writes the `#Strings` name as
stored; the IL census cursor writes the compiler-generated ordinal
correspondence's name where its policy folds one), and asks it for a name to
spell in place of a resolution scope (the cursor's current and platform
mechanics name `<current>` and `<platform>`; empty declares the scope as it
is, under its own scope kind). A reference's names are never rewritten. The
grammar stays one; a policy that rewrites names or scopes is the consumer's,
and two consumers under one policy spell one row with the same bytes. The
declaring Type, a member reference's parent, and a method specification's
method are
resolved before the referring key opens; a Type a signature names is resolved
when it is embedded, while that key is open. The role names why: the declaring
Type of a definition, or a Type or member a signature, specification, or member
reference names. A consumer therefore spells Type-level rows into a builder
other than the one its member keys are written in, as Finding Diff Any's
retained and transient populations are, so a Type resolved while a member key
is open never finds that builder open. The consumer does not open a key in the
builder the speller is writing while a spelling runs; the speller writes only
into that builder and its own scratch. The speller's own re-entry is the other
hazard: a Type specification named inside a signature (a custom modifier may
name one) is itself spelled through the consumer, in the speller's scratch, so
a composite resolves every Type specification it embeds before its scratch key
opens, and the embed then finds it in the consumer's memo; a consumer memoizes
a specification on its first resolution. A spelling the consumer embeds itself,
a property's signature, stays open until the consumer discards it, so a row
resolved during the embed cannot reset the composites the signature holds.

Guards bound every decode (`SignatureBlobGuard`, `TypeSpecGuard`); a rejected
shape is a `BadImageFormatException`, which a consumer classifies as malformed
input. A spelling used out of its lifetime is a `SymbolKeyStateException`,
which it must not.

## Name ownership and inert path

Symbol Key owns `Name` as a grammar part. It owns the `0x02` tag, shortest-form
length, exact payload boundary, source rules, and the fact that the payload is
UTF-8 text. `OpaqueByteRange` does not search for names, attach metadata
meaning to arbitrary ranges, validate UTF-8, or apply presentation policy.

After key validation, the Symbol Key part reader uses bounded structural reads
to consume one tag and its length. For a `Name`, it receives the exact payload
as a child `OpaqueByteRange` and returns `SymbolKeyName`, a typed view retaining
that range. The view does not expose the range. It supplies byte-exact value
semantics and may issue a scoped `Utf8RangeReader`, preserving the domain's
classification without validating or containing the payload.

The part reader never receives the containing key as a raw span and does not
scan the rest of a large key to discover the Name boundary. The length prefix
supplies the boundary in constant work, while the enclosing key's validation
budget owns total traversal.

Symbol Key's responsibility ends after issuing that typed view. It does not
match, decode, visually encode, or create `InertUtf8String`. A higher-level
matcher uses the view's reader to answer a query and stops there. Separately,
when display text is requested, a presentation component such as a renderer,
serializer, or test diagnostic constructs
`InertUtf8String(TextPolicy, Utf8RangeReader)`. The containing type invokes
`Utf8VisualEncoder`; routine Symbol Key consumers do not.

The ownership branches are:

```text
Symbol Key grammar: this exact child range is a Name
  -> SymbolKeyName: retain the OpaqueByteRange without exposing it
     -> Utf8RangeReader: preserve the Name classification
        +-> ordinary matcher: ValueTextEquals answers; branch ends
        +-> display value required
              -> new InertUtf8String(TextPolicy, reader)
                 -> constructor calls Utf8VisualEncoder
                    -> fixed UTF-8 output
                    -> or InertString with the same spelling and evidence
```

The governing Name rule is:

> If a consumer wants to know whether `NameRange` equals `SearchValue`, it asks
> `Utf8RangeReader.ValueTextEquals` and receives a closed answer over the
> retained opaque range. If it wants to print `NameRange`, it first requests
> an `InertUtf8String` from `TextPolicy` and the reader, then prints that value
> or its fixed `InertString` projection. The containing type necessarily calls
> `Utf8VisualEncoder`; matching does not construct a string, and printing does
> not expose the opaque range.

Symbol Key therefore owns **which range is a Name** and preserves that fact in
`SymbolKeyName`. The parent range owner owns **how that range stays opaque,
answers closed queries, and enters mandatory containment when
`InertUtf8String` is constructed**. Inert Text owns **which scalars require a
visible spelling and what that spelling is**.

### Worked Name with required containment

Consider the metadata name `invoice`, then `U+202E RIGHT-TO-LEFT OVERRIDE`,
then `gpj.exe`. Its stored UTF-8 payload is:

```text
69 6E 76 6F 69 63 65 E2 80 AE 67 70 6A 2E 65 78 65
```

Steps 1 through 3 are the ordinary identity and matching path. Steps 4 through
6 are a separate presentation example and run only when a caller requests
display text:

1. The builder writes `02 11` followed by those 17 payload bytes. It does not
   decode or visually encode them.
2. The part reader reads tag `02`, length `11`, and obtains exactly the next
   17 bytes as an `OpaqueByteRange` retained privately by `SymbolKeyName`. The
   containing key remains opaque. Creating the range and view performs no
   UTF-8 or policy scan.
3. Equality and hashing operate on the exact range. A matcher outside Symbol
   Key asks the Name view for a scoped `Utf8RangeReader` and calls
   `ValueTextEquals` with a pre-encoded query. The stored value is not decoded.
   `U+202E` remains part of identity; the visible spelling `\u202E` is not
   substituted into the key and would not match it as a query.
4. A presentation component outside Symbol Key asks the Name view for a
   scoped reader and constructs
   `new InertUtf8String(TextPolicy.Field, reader)`. The constructor calls
   `Utf8VisualEncoder`, which identifies `U+202E` as Unicode category `Cf` and
   returns an `InertUtf8String` retaining:

   ```text
   invoice\u202Egpj.exe
   ```

   The result reports `VisualForm.BmpHex`, `TextConcern.Format`, and required
   containment. Because transformation was required, it uses one new backing;
   the original three UTF-8 bytes never reach an ordinary sink.
5. A byte sink writes the already-contained `InertUtf8String`. A string sink
   converts that same value to `InertString` and receives the same spelling
   and evidence without another policy decision. Its unrestricted
   `ToString()` is safe because the retained UTF-16 is already contained.
6. A product sink may instead replace that value with
   `InertString.ContainmentRequiredPlaceholder`. Disclosure changes; identity
   and the presentation-containment invariant do not.

There is no eager `RequiresEncoding` bit on the Name, range, or backing.
`OpaqueByteRange` and `Utf8RangeReader` neither validate nor apply policy. The
`InertUtf8String` constructor invokes the one policy-bearing encoder, which
scans only the reader's exact classified source. If every byte is valid and
permitted, the constructed value may retain the original range after that scan
proves the stronger invariant.

## Profiles

### In-process

The in-process profile is the default. Its keys are valid within one process and
one build. A domain may change its markers between builds, and keys may contain
`Ref` parts from an interning table. Finding Diff Any's keys are in-process.

### Persisted

A persisted key begins with a header part, tag `0x06`, which only this owner
writes:

```text
0x06   format version (LEB128)   domain ID (LEB128)   domain version (LEB128)
```

Persisted keys are their own readonly view type,
`PersistedSymbolKey<TDomain>`, separate from the in-process
`SymbolKey<TDomain>`. The two types cannot be compared or mixed. Because the
header tag is outside the domain-marker range, the range of a persisted key
can never also be the range of an in-process key. A consumer that matches a
key built in-process against a persisted one converts it to the persisted
profile and compares there; converting a persisted key back strips and checks
its header. `Kind` on a persisted key returns the first marker after the
header.

- **Format version** is this owner's grammar version. This document defines
  version 1.
- **Domain ID** is assigned in a closed registry that this owner keeps. A
  domain that persists keys registers once; its ID is never reused.
- **Domain version** is the consuming owner's vocabulary version. A change in
  the meaning of any marker or part order increments it. An existing key's
  meaning never changes.

A persisted key never contains a `Ref` part. Converting an in-process key
expands every reference from its table, or refuses when the table is not
available. Conversion applies the same total-length and nesting-depth bounds as
the reader and refuses rather than issuing a persisted key that the reader
would reject.

Reading a persisted key uses bounded opaque-range structural reads and
validates the domain grammar completely before issuing the typed view:

- the first part is the header, and it names the expected domain and a
  supported format and domain version;
- the first part after the header is a marker declared by that domain, and
  every nested key begins with a declared marker and carries no header;
- every later tag is Int, Name, Key, or Blob, or a marker value that the
  expected domain's enum declares; a `Ref`, a second header, or a reserved
  tag is refused;
- every integer and length is shortest-form LEB128 within 64 bits, and every
  length fits a 32-bit signed integer;
- every length lies within the remaining bytes, and nested keys validate
  recursively;
- no bytes trail the last part; and
- the total length and nesting depth are within this owner's fixed bounds.

A key that fails validation is a typed refusal, never a partially read key.
Validation inspects structure only. It does not check that names are
well-formed UTF-8, because ill-formed names are identities. Any scan for
presentation happens later, outside this owner, when a consumer constructs
`InertUtf8String` from the Name view's reader.

Persisted keys are byte-defined: integers are LEB128, and no part depends on
endianness, culture, or runtime. The same parts produce the same bytes under
NativeAOT and Browser/Wasm. Only a completely validated persisted view may
invoke `PersistedSymbolKeyEncoder`, which composes the parent's conspicuous
`OpaqueByteRangeEncoder` capability.

### Text form

A persisted key crosses JSON and the C#/TypeScript boundary as canonical
unpadded base64url text of its bytes. Reading refuses padding, non-alphabet
characters, and nonzero unused trailing bits, so one byte sequence has one
accepted text spelling. `PersistedSymbolKeyEncoder` feeds the validated complete
range through `OpaqueByteRangeEncoder` into the base64url encoder; it never asks
for a borrowed source span. TypeScript treats the text as opaque. It compares
and stores it, and passes it back, but never decodes it or renders it. Display
text travels beside the key as `InertUtf8String` or `InertString` under [Inert
Text](inert-text.md).

## Interning

An interning table maps keys of one domain to dense integer IDs within one
scope, such as one comparison or one response. Lookup uses the candidate's
`OpaqueByteRange` without allocating another key or byte buffer. Equal keys
receive equal IDs, so a key that nests other keys by `Ref` remains injective
within that table: two `Ref`-bearing keys from one table are equal exactly when
their expansions are equal, provided the form an embedded key takes, `Ref` or
`Key`, is a function of that key alone, so that two embedded keys with equal
expansions are written in one form. A `Ref` form is never byte-equal to its
`Key` form, and a domain never lets one key take both.

An ID is meaningful only within its table. Keys that contain `Ref` parts are
bound to the table that issued them; comparing them across tables is a misuse
that the table owner prevents by scope, and persisting them is refused.

The table is `SymbolKeyInterner<TDomain>`. It interns a staged or frozen key,
looking the candidate up by its bytes or its range through the alternate
lookups of one id set whose comparer hashes and compares ids by the bytes they
name, and copies a first-seen key into the table's one storage; a lookup
allocates nothing, and a consumer that only needs equality compares ids. The
indexer resolves an id to the key it names as a copy, which only expansion and
diagnostics take. `Expand` writes a key into a builder with every `Ref`, to
the nesting depth the signature guard admits a spelling, replaced by the
`Key` form of the key it names, so a key without `Ref` parts expands to its
own bytes; it is the conversion the persisted
profile will use, and the gate that relates a `Ref`-bearing key to a full
spelling of the same identity. The builder's `Ref(int)` writes the part, and
the part reader exposes it as `SymbolKeyPartKind.Ref` with its `Id`.

Finding Diff Any is the first adopter. One table per comparison is shared by
both sides, so equal Type-level keys of the two images receive one id; a
Type-level key is interned when memoized, its `KeyRef` records the id and the
form, and two Type-level `KeyRef`s compare and hash by id. The form is decided
by the embedded key's kind, its first marker: a named Type (a Type definition,
reference, or primitive) embeds as the `Ref` of its id; a composite embeds as a
`Key` part of its bytes, whether it is a Type specification row or is spelled
inline in a signature, and its own Types are `Ref`s, so the two encodings of
one composite are one byte sequence; a member row embeds as a `Key` part. Named
Types, composites, and members begin with disjoint markers, so two embedded
keys with equal expansions have one kind and therefore one form, which is what
the injectivity above requires. The table serves the comparison and is released
with the builders when the sides complete. The IL census cursor writes
Type-level keys in full; its operand keys are the expansions of Finding Diff
Any's. A census side has no table, since a host holds one per image and pairs
it freely; Ref parts would reach it only through a table scoped to a batch
([#10533](https://github.com/richlander/dotnet-inspect/issues/10533)), and
Finding Census records why it does not take one now ([what remains of the
gap](finding-census.md#what-remains-of-the-gap)).

### Cost of the Ref parts

Measured on the NativeAOT linux-x64 build of `eng/measure-finding-diff-any.cs`
(20 iterations per process, medians; the retained figure is a completed
stepped comparison's heap, which no longer holds the table), base 8f2ae52c9
against head 86fa029b2 of
[#10590](https://github.com/richlander/dotnet-inspect/pull/10590), on the six
gate pairs, each process run on a quiet host (1-minute load under 1.5 before,
under 2.5 after) in two rounds; raw outputs in the
[gist](https://gist.github.com/richlander/a4aca8877418ed275c0265f4084fb023).
Verdicts are identical on every pair, and p95 is not wider on any. Every
pair is faster on the whole comparison and on the API axis alone, and
allocates and retains less:
member spellings and facts name their Types by two- or three-byte `Ref`s
instead of embedded copies, and composites nest `Ref`s in turn.

| Pair | Whole comparison ms, round 1 / round 2 | API axis alone ms, round 1 / round 2 | Allocated MB | Retained MB |
| --- | --- | --- | --- | --- |
| Aspire.Hosting 13.6.0 to 13.6.1 | 82.0 → 73.9 (-9.9%) / 83.2 → 73.7 (-11.4%) | 10.6 → 9.4 (-11.2%) / 11.0 → 9.5 (-13.6%) | 56.4 → 50.5 (-10.6%) | 3.35 → 2.82 (-15.9%) |
| System.Text.Json 9.0.0 to 10.0.0 | 13.6 → 12.0 (-11.3%) / 13.3 → 12.1 (-9.3%) | 3.8 → 3.5 (-8.2%) / 4.0 → 3.5 (-12.9%) | 13.7 → 10.7 (-21.9%) | 0.69 → 0.64 (-6.6%) |
| Newtonsoft.Json 13.0.3 to 13.0.4 | 16.1 → 14.9 (-7.3%) / 16.0 → 14.8 (-7.9%) | 4.3 → 4.2 (-3.3%) / 4.2 → 4.1 (-4.0%) | 13.6 → 12.2 (-10.4%) | 0.68 → 0.65 (-4.8%) |
| Newtonsoft.Json 11.0.2 to 13.0.4 | 12.7 → 11.5 (-9.4%) / 12.8 → 11.6 (-9.5%) | 4.1 → 3.8 (-7.0%) / 4.1 → 3.8 (-8.0%) | 11.9 → 10.4 (-12.1%) | 0.56 → 0.54 (-2.0%) |
| System.Private.Xml 10 to 11 | 59.4 → 55.8 (-6.0%) / 57.5 → 55.2 (-4.0%) | 9.3 → 8.8 (-5.3%) / 9.3 → 8.8 (-6.1%) | 38.8 → 36.9 (-4.8%) | 2.10 → 2.05 (-2.4%) |
| System.Private.CoreLib 10 to 11 | 174.8 → 164.1 (-6.1%) / 175.5 → 167.4 (-4.6%) | 83.0 → 76.8 (-7.5%) / 83.0 → 77.1 (-7.1%) | 139.8 → 115.3 (-17.5%) | 4.61 → 4.42 (-4.1%) |

## Diagnostics

Test failures and logs need a structural spelling of a key's parts. Symbol Key
supplies the part sequence: markers and integers, each `Name` as
`SymbolKeyName`, and each `Blob` as its opaque range. A diagnostic renderer
outside Symbol Key spells marker and integer structure as owner-authored ASCII,
constructs `InertUtf8String` under `TextPolicy.Field` from the reader issued by
each Name view, writes that value or its derived `InertString`, and gives each
`Blob` a structural binary spelling. A `Ref` renders as `#id`, followed by the
key it names in parentheses when the renderer is given the table that issued
it. The rendering is never compared, parsed, or presented as product identity.

## Hosts

The primitive is pure managed code over System.Reflection.Metadata, so it runs
unchanged under NativeAOT and Browser/Wasm. Finding Diff Any already runs its
keys in both hosts through its Browser export. Persisted keys and their text
form are the exchange currency between the hosts and checked-in artifacts.

## Evidence and gates

| Property | Gate | Lane |
| --- | --- | --- |
| Injectivity | Generated part sequences, including names with separators, empty names, ill-formed UTF-8, and nested keys, map to distinct bytes exactly when the sequences differ | PR-fast |
| Name sources agree | For every Type, method, and field name in System.Private.CoreLib, the `#Strings`, UTF-8, and UTF-16 sources write the same part | PR-fast |
| Lone surrogates refused | UTF-16 text with a lone surrogate is refused | PR-fast |
| Domain separation | Keys of two domains do not compile as comparable; an invalid domain enum is rejected at first use | PR-fast |
| Kind totality | The builder refuses to complete a key without a declared first marker; persisted validation refuses marker-free, leading-non-marker, and nested-header keys | PR-fast |
| Shared backing | Every issued key and part view in one frozen population retains its one opaque-range backing; slots issue no mutable views, inline nested builds create no intermediate payload, and an independently retained key embedded as a `Key` part has only the duplicate bytes required by that grammar | PR-fast |
| Name boundary | The part reader returns a `SymbolKeyName` retaining exactly the length-prefixed child range without exposing the containing key, its own range, or adjacent parts | PR-fast |
| Name matching | A `Utf8RangeReader` issued by `SymbolKeyName` matches ordinary, non-ASCII, concerning, and ill-formed stored payloads byte-exactly against pre-encoded queries without decoding, policy work, or `InertUtf8String` construction | PR-fast |
| Name printing | A Name containing `U+202E` remains byte-exact for matching without invoking containment; a presentation consumer constructs `InertUtf8String` from `TextPolicy.Field` and the Name-issued reader, the constructor necessarily invokes `Utf8VisualEncoder`, and direct UTF-8 and derived `InertString` output both emit `\u202E`, `VisualForm.BmpHex`, and `TextConcern.Format` | PR-fast |
| Persisted round trip | Every Type and member key of System.Private.CoreLib persisted, converted to text, read back, and compared | PR-fast |
| Persisted validation | Truncated, trailing, wrong-domain, unknown-version, marker-free, leading-non-marker, undeclared-marker, `Ref`-bearing, nested-header, non-shortest LEB128, over-64-bit integer, over-`int` length, over-deep, and over-long keys are each refused; conversion refuses the same depth and length excesses | PR-fast |
| Profile separation | In-process and persisted keys do not compile as comparable; a key without `Ref` parts round-trips byte-identically, while a `Ref`-bearing key persists by expansion and returns in `Key` form with the same expansion | PR-fast |
| Canonical text | Base64url writing is unpadded; reading refuses padding, non-alphabet characters, and nonzero unused trailing bits; accepted text re-encodes identically | PR-fast |
| Host parity | Persisted keys built under Browser/Wasm equal those built under NativeAOT for the same input | Inspect Web managed API tests |
| Interning | Equal keys share an ID; two `Ref`-bearing keys from one table are equal exactly when their expansions are equal; a `Ref` form is never byte-equal to its `Key` form; expansion is the `Key` form at every depth and the identity on a key without `Ref` parts; an id the table did not issue is refused. In Finding Diff Any, over every method, field, member reference, and Type specification of one image on both sides of a comparison: the sides give one row one key, no nested `Key` part is a named Type, a Type specification embeds as `Ref` exactly when it names a plain row, every spelling describes through the table as its expansion does, and the IL cursor's operand keys are the expansions of the walk's keys | PR-fast |
| Representation pilot | SPC comparison of current private Symbol Key and the proposed range-backed representation measures construction and lookup time, allocated bytes, and retained managed memory on the same pinned census; identity answers agree | Pilot before implementation |
| Finding Diff Any unchanged | Finding Diff Any's identity and oracle gates pass unchanged. NativeAOT base/head on the six gate pairs is read as the aggregate diff experience, weighted to the expensive common tasks: whole-comparison time and allocation do not regress on any pair, retained memory does not exceed base on any pair, and p95 does not widen; the API axis alone, the asynchronous surface pass, stays cheap enough to run that way: it does not regress on the large pairs (aspire, xml, CoreLib), the small pairs (stj, nj11, nj13) stay within a millisecond of base and under 5 ms, and CoreLib's stays under 100 ms. After time, memory is the priority | Adoption PR |

## Adoption sequence

Eight steps, tracked by
[#9918](https://github.com/richlander/dotnet-inspect/issues/9918):

1. **Opaque range and Inert UTF-8 design.** Parent PR #9993 defines immutable
   backing, `OpaqueByteRange`, scoped readers, STJ-style query matching, the
   explicit exact-byte encoder, and the `InertUtf8String`-owned presentation
   transition.
2. **This Symbol Key design.** Define the key domain view over that backing.
3. **Symbol Key representation pilot.** Compare current private Symbol Key
   with an `OpaqueByteRange`/`ValueTextEquals` prototype using time and memory
   SPC. A neutral or negative result changes the implementation plan rather
   than being hidden. The pilot tool retired with step 5, which deleted its
   baseline; its evidence stays recorded on
   [#10035](https://github.com/richlander/dotnet-inspect/pull/10035).
4. **Exact-Type baseline and host-input comparison.** The parent design first
   lands the shared primitives with one uniquely matched Type identity, then
   measures the separately reviewable host-input exact-match optimization
   against that merged baseline.
5. **Shared implementation, Finding Diff Any adopts** (implemented,
   [#10476](https://github.com/richlander/dotnet-inspect/issues/10476)). The
   key, builder, and `#Strings` reader live in
   `ILInspector.MetadataPrimitives` with the domain split and the UTF-8 and
   UTF-16 name sources. Finding Diff Any's `SymbolPart` became its domain,
   `FindingDiffAnyPart`. Each consumer-chosen population is staged and frozen
   before its final dictionaries and sequences are exposed, with transient
   populations compared staged and discarded; one-off nested keys are
   embedded as completed slots. The opaque-range dependency was added, and
   Finding Diff Any's private storage and key copy were deleted in the same
   step, with NativeAOT parity measured on the gate pairs.
6. **Persisted profile and text form.** Header, registry, validation,
   `PersistedSymbolKeyEncoder`, and base64url. The first adopter is whichever
   of the encoded API qualified anchor
   ([#9907](https://github.com/richlander/dotnet-inspect/issues/9907)) or the
   receiver index artifact
   ([#9889](https://github.com/richlander/dotnet-inspect/issues/9889) step 3)
   lands first; that adoption PR registers its domain.
7. **Interning.** The range-keyed table and `Ref` part, adopted by Finding
   Diff Any step 5. The memo is the pairing: once both sides have spelled a
   row and compared, the diff needs the verdict, not the spelling. The first
   half is done: member rows are spelled per unit and discarded, and the
   body comparison keeps the pairing of operand rows proven equal, rather
   than their spellings, while it runs and releases it when it completes, so
   a completed comparison retains the Type-level keys and the unit tables. The
   second half is done too: `SymbolKeyInterner<TDomain>`, the `Ref` part,
   and Finding Diff Any embedding every named Type as the `Ref` of the id
   its comparison-wide table gave it, and composites as `Key` parts whose
   Types are those `Ref`s
   ([#10533](https://github.com/richlander/dotnet-inspect/issues/10533)).
8. **Browser exchange.** The first Inspect Web consumer that passes a key to
   TypeScript and back adopts the text form, with the host-parity gate.

The Finding Diff Any-private implementation is retired in step 4. No other
private copy exists.

## Non-claims

- **Name well-formedness.** A key never decides whether a name is valid. A
  consumer that needs a decodable name, as Finding Diff Any does for Types,
  checks it under its own contract.
- **Cross-domain meaning.** Two domains' keys are never comparable, even when
  their bytes are equal.
- **Storage and containment.** The key does not own another byte buffer, raw
  accessor, UTF-8 reader, or visual encoder; those remain with the opaque-range
  owner and Inert Text.
- **Performance result.** The design does not claim that shared opaque ranges
  beat the current private Symbol Key before the required time-and-memory SPC
  pilot.
- **Compression.** Keys are compact by construction but are not compressed.
  Artifact compression belongs to the artifact's host.
- **Cryptographic identity.** A key is not a hash or a signature and makes no
  collision-resistance or tamper claim beyond injectivity of its parts.

## Alternatives not selected

- **Escaped strings.** #9860 made keys injective by escaping separators. It
  was correct but 3.4% to 7.2% slower, and it decoded names to UTF-16 first.
- **Keep it private to Finding Diff Any.** Each consumer would copy the
  grammar and the `#Strings` reader, and their injectivity and validation would
  drift.
- **A general serializer, such as MessagePack or CBOR.** A self-describing
  format admits several encodings of one value unless canonicalized, needs a
  dependency on the product path, and decodes names to strings. The key needs
  one encoding per part sequence and byte-exact names.
- **Hash keys.** A fixed-size hash is smaller but can collide, and
  injectivity is the claim.
