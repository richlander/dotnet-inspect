# Symbol Key

## Status and owner

Proposed focused design for
[#9918](https://github.com/richlander/dotnet-inspect/issues/9918). Nothing in
this document is implemented beyond the Fast Diff-private `SymbolKey` that
[#9898](https://github.com/richlander/dotnet-inspect/pull/9898) introduced.
The adoption sequence below is the counted path to shared use in the CLI and
Browser/Wasm hosts.

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
  each consuming owner (for example, the [Identity](fast-diff.md#identity)
  section of Fast Diff);
- display text for names, which uses `InertUtf8` and remains governed by
  [Inert Text](inert-text.md);
- structured API identity, which remains with
  [API Qualified Anchor](api-qualified-anchor.md); or
- work budgets for building keys over untrusted images, which remain with
  each producer, as Fast Diff's do.

## Why

Fast Diff's keys proved the domain grammar. Making keys injective by
construction, rather than by escaping, made Fast Diff 20% to 30% faster on
NativeAOT across its six pinned pairs, and keeping names as stored bytes fixed
a U+FFFD collision. The key, its builder, and its `#Strings` name reader are
`internal` to Fast Diff.

The parent owner generalizes the representation evidence: one immutable backing
supplies cheap `OpaqueByteRange` snapshots, generic byte operations, STJ-style
UTF-8 query matching, and one explicit transition to `InertUtf8`. Symbol
Key is the first domain view. It adds a grammar and profiles without creating a
second storage owner or applying presentation policy during analysis.

Four planned consumers need the same thing, and each would otherwise grow a
private copy:

| Consumer | Needs |
| --- | --- |
| [API Qualified Anchor](api-qualified-anchor.md) ([#9907](https://github.com/richlander/dotnet-inspect/issues/9907)) | An encoded type or member identity, with typed records as a view; byte-exact name equality |
| [Ecosystem Receiver Index](ecosystem-receiver-index.md) | Persisted receiver and member keys in a checked-in artifact; the named lever if the artifact misses its [size goal](ecosystem-receiver-index.md#performance-goals) |
| Fast Diff step 5 | One interning table shared by both sides of a comparison |
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

The builder stages one consumer-chosen population through the shared
ref-struct range builder. Completing a staged key records its offset and
length as a builder-local slot; it does not issue a `SymbolKey<TDomain>` over
mutable storage. Freezing the population transfers the finished storage to
one immutable backing and resolves its slots into readonly
`OpaqueByteRange`-backed key views. A consumer that needs smaller independent
lifetimes chooses smaller population boundaries.

Nested construction has two explicit forms:

- an inline nested build writes directly inside the enclosing `Key` part and
  never issues an independently retained child; and
- embedding an independently completed slot writes that key's bytes into the
  enclosing `Key` payload.

The second form is a semantic copy required by the self-contained grammar:
both the independent key and its nested occurrence exist in the finished
population. It uses no intermediate array. A later domain may replace repeated
embedded keys with `Ref` parts when it adopts interning.

Fast Diff adoption stages its census and lookup keys, freezes their population,
and only then exposes the final dictionaries and sequences. One-off nested
keys use inline construction. Reused independent keys use slots while staging
and resolve to views after the same freeze.

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
match, decode, visually encode, or create `InertUtf8`. A higher-level matcher
uses the view's reader to answer a query. A presentation component, such as a
renderer, a serializer, or a test diagnostic, passes that reader to
`Utf8VisualEncoder` when it needs display text.

The ownership chain is:

```text
Symbol Key grammar: this exact child range is a Name
  -> SymbolKeyName: retain the OpaqueByteRange without exposing it
     -> Utf8RangeReader: preserve the Name classification
        -> matcher: ValueTextEquals answers exact UTF-8 queries
        -> presentation: Utf8VisualEncoder + TextPolicy creates InertUtf8
           -> fixed UTF-8 output
           -> or InertString with the same contained spelling and evidence
```

Symbol Key therefore owns **which range is a Name** and preserves that fact in
`SymbolKeyName`. The parent range owner owns **how that range stays opaque,
answers closed queries, and reaches `InertUtf8`**. Inert Text owns **which
scalars require a visible spelling and what that spelling is**.

### Worked Name with required containment

Consider the metadata name `invoice`, then `U+202E RIGHT-TO-LEFT OVERRIDE`,
then `gpj.exe`. Its stored UTF-8 payload is:

```text
69 6E 76 6F 69 63 65 E2 80 AE 67 70 6A 2E 65 78 65
```

The path is:

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
   scoped reader and passes it with `TextPolicy.Field` to `Utf8VisualEncoder`.
   The encoder identifies `U+202E` as Unicode category `Cf` and creates an
   `InertUtf8` retaining:

   ```text
   invoice\u202Egpj.exe
   ```

   The result reports `VisualForm.BmpHex`, `TextConcern.Format`, and required
   containment. Because transformation was required, it uses one new backing;
   the original three UTF-8 bytes never reach an ordinary sink.
5. A byte sink writes the already-contained `InertUtf8`. A string sink
   converts that same value to `InertString` and receives the same spelling
   and evidence without another policy decision. Its unrestricted
   `ToString()` is safe because the retained UTF-16 is already contained.
6. A product sink may instead replace that value with
   `InertString.ContainmentRequiredPlaceholder`. Disclosure changes; identity
   and the inert-flow invariant do not.

There is no eager `RequiresEncoding` bit on the Name, range, or backing.
`OpaqueByteRange` and `Utf8RangeReader` neither validate nor apply policy. The
one policy-bearing encoder scans only the reader's exact classified source when
presentation is requested. If every byte is valid and permitted, `InertUtf8`
may retain the original range after that scan proves the stronger invariant.

## Profiles

### In-process

The in-process profile is the default. Its keys are valid within one process
and one build. A domain may change its markers between builds, and keys may
contain `Ref` parts from an interning table. Fast Diff's keys are in-process.

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
presentation happens later, outside this owner, when a consumer passes the
Name view's reader to `Utf8VisualEncoder`.

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
text travels beside the key as `InertUtf8` or `InertString` under [Inert
Text](inert-text.md).

## Interning

An interning table maps keys of one domain to dense integer IDs within one
scope, such as one comparison or one response. Lookup uses the candidate's
`OpaqueByteRange` without allocating another key or byte buffer. Equal keys
receive equal IDs, so a key that nests other keys by `Ref` remains injective
within that table: two `Ref`-bearing keys from one table are equal exactly when
their expansions are equal, provided the domain always writes the same
position in the same form, either `Ref` or `Key`. A `Ref` form is never
byte-equal to its `Key` form, and a domain does not mix the two for one
position.

An ID is meaningful only within its table. Keys that contain `Ref` parts are
bound to the table that issued them; comparing them across tables is a misuse
that the table owner prevents by scope, and persisting them is refused.

## Diagnostics

Test failures and logs need a structural spelling of a key's parts. Symbol Key
supplies the part sequence: markers and integers, each `Name` as
`SymbolKeyName`, and each `Blob` as its opaque range. A diagnostic renderer
outside Symbol Key spells marker and integer structure as owner-authored ASCII,
passes the reader issued by each Name view through `Utf8VisualEncoder` under
`TextPolicy.Field`, writes the resulting `InertUtf8` or derived `InertString`,
and gives each `Blob` a structural binary spelling. The rendering is never
compared, parsed, or presented as product identity.

## Hosts

The primitive is pure managed code over System.Reflection.Metadata, so it
runs unchanged under NativeAOT and Browser/Wasm. Fast Diff already runs its
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
| Name matching | A `Utf8RangeReader` issued by `SymbolKeyName` matches ordinary, non-ASCII, concerning, and ill-formed stored payloads byte-exactly against pre-encoded queries without decoding or policy work | PR-fast |
| Name containment | A Name containing `U+202E` remains byte-exact for matching; a presentation consumer passing its Name-issued reader to `Utf8VisualEncoder` gets `InertUtf8`, and direct UTF-8 and derived `InertString` output both emit `\u202E`, `VisualForm.BmpHex`, and `TextConcern.Format` | PR-fast |
| Persisted round trip | Every Type and member key of System.Private.CoreLib persisted, converted to text, read back, and compared | PR-fast |
| Persisted validation | Truncated, trailing, wrong-domain, unknown-version, marker-free, leading-non-marker, undeclared-marker, `Ref`-bearing, nested-header, non-shortest LEB128, over-64-bit integer, over-`int` length, over-deep, and over-long keys are each refused; conversion refuses the same depth and length excesses | PR-fast |
| Profile separation | In-process and persisted keys do not compile as comparable; a key without `Ref` parts round-trips byte-identically, while a `Ref`-bearing key persists by expansion and returns in `Key` form with the same expansion | PR-fast |
| Canonical text | Base64url writing is unpadded; reading refuses padding, non-alphabet characters, and nonzero unused trailing bits; accepted text re-encodes identically | PR-fast |
| Host parity | Persisted keys built under Browser/Wasm equal those built under NativeAOT for the same input | Inspect Web managed API tests |
| Interning | Equal keys share an ID; two `Ref`-bearing keys from one table are equal exactly when their expansions are equal | PR-fast |
| Representation pilot | SPC comparison of current private Symbol Key and the proposed range-backed representation measures construction and lookup time, allocated bytes, and retained managed memory on the same pinned census; identity answers agree | Pilot before implementation |
| Fast Diff unchanged | Fast Diff's identity and oracle gates pass unchanged, and NativeAOT base/head on the #9898 pairs shows no regression | Adoption PR |

## Adoption sequence

Seven steps, tracked by
[#9918](https://github.com/richlander/dotnet-inspect/issues/9918):

1. **Opaque range and Inert UTF-8 design.** Parent PR #9993 defines immutable
   backing, `OpaqueByteRange`, scoped readers, STJ-style query matching, the
   explicit exact-byte encoder, and the transition to `InertUtf8`.
2. **This Symbol Key design.** Define the first domain view over that backing.
3. **Symbol Key representation pilot.** Compare current private Symbol Key
   with an `OpaqueByteRange`/`ValueTextEquals` prototype using time and memory
   SPC. A neutral or negative result changes the implementation plan rather
   than being hidden.
4. **Shared implementation, Fast Diff adopts.** Move the key, builder, and
   `#Strings` reader into `ILInspector.MetadataPrimitives` with the domain
   split and the UTF-8 and UTF-16 name sources. Fast Diff's `SymbolPart`
   becomes its domain. Stage each consumer-chosen population, freeze before
   exposing its final dictionaries and sequences, and build one-off nested
   keys inline. Add the opaque-range dependency; Fast Diff's private storage
   and key copy are deleted in the same step only when the pilot supports that
   adoption, with NativeAOT parity measured.
5. **Persisted profile and text form.** Header, registry, validation,
   `PersistedSymbolKeyEncoder`, and base64url. The first adopter is whichever
   of the encoded API qualified anchor
   ([#9907](https://github.com/richlander/dotnet-inspect/issues/9907)) or the
   receiver index artifact
   ([#9889](https://github.com/richlander/dotnet-inspect/issues/9889) step 3)
   lands first; that adoption PR registers its domain.
6. **Interning.** The range-keyed table and `Ref` part, adopted by Fast Diff
   step 5.
7. **Browser exchange.** The first Inspect Web consumer that passes a key to
   TypeScript and back adopts the text form, with the host-parity gate.

The Fast Diff-private implementation is retired in step 4. No other private
copy exists.

## Non-claims

- **Name well-formedness.** A key never decides whether a name is valid. A
  consumer that needs a decodable name, as Fast Diff does for Types,
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
- **Keep it private to Fast Diff.** Each consumer would copy the grammar and
  the `#Strings` reader, and their injectivity and validation would drift.
- **A general serializer, such as MessagePack or CBOR.** A self-describing
  format admits several encodings of one value unless canonicalized, needs a
  dependency on the product path, and decodes names to strings. The key needs
  one encoding per part sequence and byte-exact names.
- **Hash keys.** A fixed-size hash is smaller but can collide, and
  injectivity is the claim.
