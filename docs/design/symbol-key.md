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
[Inert Bytes](inert-bytes.md) as the immutable backing and operation owner.

**Symbol Key** is a host-neutral Metadata domain view. It owns this exact claim:

> A Symbol Key is a domain-validated readonly view over an `InertBytes` range.
> Its builder writes new keys as a sequence of self-delimiting parts, and its
> persisted reader validates that same grammar before issuing a view. Two keys
> of one key domain are equal exactly when they contain the same parts. Name
> parts remain UTF-8 byte subranges without decoding, so names that differ only
> in ill-formed sequences stay distinct. A persisted key is versioned and
> byte-identical across builds, platforms, and hosts for the same parts.

This owner defines:

- the part grammar: payload parts, the marker range, and their encoding;
- the builder, its name sources, and nesting;
- key domains and the compile-time separation between them;
- the in-process and persisted profiles, including the persisted header,
  validation, and text form; and
- scoped interning.

It does not define:

- immutable byte storage, range lifetime, generic byte operations, or
  untreated-byte transfer, which remain with
  [Inert Bytes](inert-bytes.md);
- any domain's marker vocabulary or what its keys identify, which remain with
  each consuming owner (for example, the [Identity](fast-diff.md#identity)
  section of Fast Diff);
- display text for names, which uses the Inert Bytes UTF-8 view and remains
  governed by [Inert Text](inert-text.md);
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

The Inert Bytes owner generalizes the representation evidence: one immutable
backing supplies cheap ranges, generic byte operations, and contained text
projection. Symbol Key is the first domain view. It adds a grammar and
profiles without creating a second storage owner.

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
and ordering delegate to `InertBytes` ordinal operations, so order is a
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

The first marker of a key conventionally names the key's shape (`Type`,
`Method`, `Field`), and `Kind` returns it.

## Builder

The builder writes parts through the shared Inert Bytes builder into one
backing. Builds nest: a key begun while another is open is written above it
and removed when it ends, so building a nested key creates no separate
payload. Completing the outer build retains one backing and returns a
`SymbolKey<TDomain>` view over its finished range.

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
available.

Reading a persisted key uses bounded Inert Bytes structural reads and validates
the domain grammar completely before issuing the typed view:

- the first part is the header, and it names the expected domain and a
  supported format and domain version;
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
well-formed UTF-8, because ill-formed names are identities.

Persisted keys are byte-defined: integers are LEB128, and no part depends on
endianness, culture, or runtime. The same parts produce the same bytes under
NativeAOT and Browser/Wasm. Only a completely validated persisted view may use
the backing's opaque binary-transfer operation.

### Text form

A persisted key crosses JSON and the C#/TypeScript boundary as unpadded
base64url text of its bytes. The view feeds its complete range through Inert
Bytes opaque binary transfer into the base64url encoder; it never asks for a
borrowed source span. TypeScript treats the text as opaque. It compares and
stores it, and passes it back, but never decodes it, and never renders it.
Display text travels beside the key, contained by the Inert Bytes UTF-8 view
under [Inert Text](inert-text.md).

## Interning

An interning table maps keys of one domain to dense integer IDs within one
scope, such as one comparison or one response. Lookup uses the candidate's
`InertBytes` range without allocating another key or byte buffer. Equal keys
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

A key renders a structural diagnostic spelling of its parts for test failures
and logs. Marker and integer structure is owner-authored ASCII. Every `Name`
part projects through its Inert Bytes UTF-8 view under `TextPolicy.Field`, and
every `Blob` part uses a structural binary spelling. The rendering is never
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
| Shared backing | A completed key, nested keys, and Name and Blob part views retain ranges over one Inert Bytes backing without payload duplication | PR-fast |
| Persisted round trip | Every Type and member key of System.Private.CoreLib persisted, converted to text, read back, and compared | PR-fast |
| Persisted validation | Truncated, trailing, wrong-domain, unknown-version, undeclared-marker, `Ref`-bearing, non-shortest LEB128, over-64-bit integer, over-`int` length, over-deep, and over-long keys are each refused | PR-fast |
| Profile separation | In-process and persisted keys do not compile as comparable; a key without `Ref` parts round-trips byte-identically, while a `Ref`-bearing key persists by expansion and returns in `Key` form with the same expansion | PR-fast |
| Host parity | Persisted keys built under Browser/Wasm equal those built under NativeAOT for the same input | Inspect Web managed API tests |
| Interning | Equal keys share an ID; two `Ref`-bearing keys from one table are equal exactly when their expansions are equal | PR-fast |
| Fast Diff unchanged | Fast Diff's identity and oracle gates pass unchanged, and NativeAOT base/head on the #9898 pairs shows no regression | Adoption PR |

## Adoption sequence

Six steps, tracked by
[#9918](https://github.com/richlander/dotnet-inspect/issues/9918):

1. **Inert Bytes design.** Parent PR #9993 defines immutable backing, ranges,
   generic byte operations, opaque binary transfer, and contained text
   projection.
2. **This Symbol Key design.** Define the first domain view over that backing.
3. **Shared implementation, Fast Diff adopts.** Move the key, builder, and
   `#Strings` reader into `ILInspector.MetadataPrimitives` with the domain
   split and the UTF-8 and UTF-16 name sources. Fast Diff's `SymbolPart`
   becomes its domain. Add the Inert Bytes dependency; Fast Diff's private
   storage and key copy are deleted in the same step, with NativeAOT parity
   measured.
4. **Persisted profile and text form.** Header, registry, validation, and
   base64url. The first adopter is whichever of the encoded API qualified
   anchor ([#9907](https://github.com/richlander/dotnet-inspect/issues/9907))
   or the receiver index artifact
   ([#9889](https://github.com/richlander/dotnet-inspect/issues/9889) step 3)
   lands first; that adoption PR registers its domain.
5. **Interning.** The range-keyed table and `Ref` part, adopted by Fast Diff
   step 5.
6. **Browser exchange.** The first Inspect Web consumer that passes a key to
   TypeScript and back adopts the text form, with the host-parity gate.

The Fast Diff-private implementation is retired in step 3. No other private
copy exists.

## Non-claims

- **Name well-formedness.** A key never decides whether a name is valid. A
  consumer that needs a decodable name, as Fast Diff does for Types,
  checks it under its own contract.
- **Cross-domain meaning.** Two domains' keys are never comparable, even when
  their bytes are equal.
- **Storage and containment.** The key does not own another byte buffer, raw
  accessor, or name encoder; those remain with Inert Bytes and Inert Text.
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
