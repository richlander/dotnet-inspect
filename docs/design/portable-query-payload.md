# Portable query payload

## Status

**Implemented.** `QuerySpace` carries the codec under its root namespace.
[QuerySpace Library Boundary](query-space-library.md) owns that physical and
namespace composition without changing this byte contract, and
`DotnetInspector.PortableQueries.Tests` runs every vector in
[`models/portable-query-payload/`](models/portable-query-payload/vectors.json)
against it in CI. The vectors come in four kinds — an intent and the bytes it
must become, an intent that must be refused, bytes that must be refused, and a
pair of states that are or are not one query — so both directions of the codec
and its identity rule are witnessed. The design-stage probe that formerly ran
from `eng/` has retired, as this document said it would: the file it checked now
gates the product.

Adoption is not complete. Package Query now supplies the first production
vocabulary and both CLI and Browser lower through it, but Definitions query
records and the packet's query arm still carry no payload. Those are the
remaining steps in
[#6971](https://github.com/richlander/dotnet-inspect/issues/6971).

This is **slice 2 of 2** under
[#6971](https://github.com/richlander/dotnet-inspect/issues/6971), stacked on
[Portable query intent](portable-query-intent.md), which owns the model this
encodes. Read that document first: it defines the parts — terms, execution
bounds, selection stages, and order operations — and the resolution and replay
rules this encoding exists to make portable.

## Owner and claim

This focused owner defines the **single byte spelling** a query intent has.

The claim is that one intent has exactly one canonical byte sequence, and that
two independent implementations — the .NET codec and the Browser adapter — must
produce it identically or sharing does not work. Every rule below serves that
claim; anything that does not is not this document's.

[Workspace definitions](workspace-definitions.md) packet format 2 defines its
`q` table as `[queryId, payload]`, where `queryId` names the exact portable
vocabulary and `payload` is the closed JSON object emitted by
`PortableQueryPayloadCodec`. The payload must round-trip byte-for-byte through
parse and canonical write. Workspace Definitions delegates property order,
string and numeric grammar, identity spelling, and nested limits to this
document's shared codec contract; vocabulary owners retain semantic binding
and execution. This document does not redefine the packet family, its outer
bounds, its base64url and JSON hardening, or any coordinate-bearing arm.

## Where the rules live

Three artifacts, one normative location per rule:

| Artifact | Owns |
| --- | --- |
| This document | The principles: what canonical form means, what identity is, what the codec may and may not know, how limits behave |
| `PortableQueryPayloadCodec` | The exact structure: property order, tuple layouts and slot kinds, role encoding, integer grammar, the string rule, and every limit with its scope. Its declared maxima are public constants and its layouts are the reader and writer themselves, so the structure cannot drift from what ships. |
| [Portable query intent](portable-query-intent.md) | Everything this encoding carries but does not define: the parts, every identity text — operators, directions, stage kinds, order kinds, the `base` role — and the semantic orders with their comparator. |
| `models/portable-query-payload/vectors.json` | The witnesses: every canonical form the contract promises and every rejection it requires |

The prose never restates an identity text, an arity, or a maximum. If a
sentence here seems to disagree with the codec, the model, or a vector,
the sentence is wrong. Two things this encoding carries are not its to define:
the identity texts, and the orders in which elements are emitted — the model's
[semantic orders](portable-query-intent.md#semantic-order), including its
comparator, Unicode scalar value, which is UTF-8 byte order. This codec emits
both without defining either.

## Identity

Two intents are the same query when they carry the same vocabulary **and**
identical canonical bytes. Equality is decided on that pair and nowhere else.

Payload bytes alone are not an identity: the same bytes under two vocabularies
are two different queries, because the keys inside them resolve against
different namespaces. The vocabulary is therefore never a property of the
payload. It travels beside it — as the packet tuple's `queryId`, and as the same
pair in any standalone encoding such as the `/query` share link — so it appears
exactly once and cannot disagree with itself.

## Canonical form

Canonical form is **syntactic**. It normalizes how a request is spelled, never
what it means, and it is fixed by one principle:

> Sequence is canonical only where sequence carries meaning.

The stage pipeline is a sequence, because each stage consumes the output of the
one before it. The field terms inside one order operation are a sequence,
because they compose lexicographically. Everywhere else — conjoined terms,
independent execution bounds, order operations that each already name their own
role — sequence carries nothing, so canonical form supplies the model's semantic
order. The vectors `order-operations-supplied-out-of-role-order`,
`bounds-entered-in-another-order`, and `row-query-reversed-pipeline` show the two
sides of that line, and `scalar-order-not-utf16-order` shows why the comparator
had to be named: two keys above and below the surrogate range sort one way by
UTF-8 bytes and the other by UTF-16 code units.

Three consequences follow, each with its own reason:

- **Exact duplicate terms collapse**, because conjunction is idempotent: asking
  twice asks the same question.
- **A repeated bound dimension is refused**, not collapsed and not last-wins,
  even when both maxima are identical. A second bound on one dimension has no
  meaning to preserve, and silently choosing one would let a share link
  authorize work its author did not request. The asymmetry with terms is
  deliberate.
- **Values are never normalized here.** Package identifiers, framework names, and
  assembly names each have owner-specific equivalence rules, and applying any of
  them in the codec would make canonical bytes depend on a vocabulary's current
  semantics. Share identity is therefore syntactic: two spellings that a
  vocabulary would treat as equal can share as different links. A vocabulary
  that wants spelling-independent identity must normalize **before** constructing
  intent, where the user can see it happen, rather than inside a codec where it
  would silently rewrite what was shared.

Emission is compact, and a part the intent does not use has no presence on
the wire at all — the codec fixes that spelling. Whether a query with no
parts means anything is the vocabulary's question, and never this codec's. Bytes
that arrive in
any other spelling — reordered, padded, or carrying a duplicate the canonical
form would have collapsed — are refused as non-canonical rather than repaired.
The packet owner already refuses non-canonical decoded JSON; this payload does
the same.

## Shape

The payload is one closed JSON object with one property per serializable part
of the intent contract, each an abbreviation of that part's name: `t` is the
**terms**, `b` the **bounds**, `s` the **stages**, and `o` the **order**. The
fifth part, the vocabulary, has no property here; it travels beside the payload
as the tuple's `queryId`, as [Packet projection](#packet-projection) describes.
The long name is how documents and consumers refer to a part; the short one is
the only form that appears on the wire. The abbreviations exist because the
payload's budget is small and the packet family already spells its own fields
this way. Note that the packet's top-level `t` is its coordinate-tuple table
while this payload's `t` is the term set; the two live at different scopes and a
parser never sees both in one object, but a reader of both documents should not
assume they mean the same thing.

One complete payload, exactly as written — the canonical form of the parent's
worked package query, with the terms sorted and the bound beside them, and no
stage or order part because that intent has none:

```json
{"t":[["depends","eq","Serilog"],["prefix","eq","Microsoft.Extensions."],["prerelease","eq","include"]],"b":[["candidates",200]]}
```

A row query that selects and ranks fills all four parts; the vector
`all-four-parts` is that payload. Every other layout, token, and limit
that these bytes obey is fixed once, in the shape region, and witnessed in the
vectors rather than restated here.

Each part is an array of tuples whose layouts the codec fixes. Every
layout is fixed-arity except the field-list order operation, which has a fixed
head and a repeating key-and-direction pair. The strings that name an operator,
a direction, a stage kind, an order kind, or the baseline role are the model's
identity texts, carried here verbatim — an implementation derives none of them
from a .NET enum name, a CLI spelling, or a display label, and the model's
operator set is exactly the eight identities the Portable Query Intent owner
declares: `eq`, `ne`, `starts-with`, `not-starts-with`, `contains`,
`not-contains`, `gte`, and `lte`; there is no strict `lt` or `gt`. An omitted
window bound is a gap in place rather than a shorter tuple, so no window can be
mistaken for another stage, and a closed window's bounds are ordered — that is
the stage owner's construction precondition, and the parent slice makes it this
codec's to enforce at decode, so no payload can reach a resolver carrying a
stage it could not construct. Counts have one portable domain, fixed by the
codec so that a host's native integer width never decides what another host must
admit. An order operation carries its own role, kind, and boundary, so a
baseline of `[a asc]` beside a ranking of `[b desc, c asc]` can never serialize
identically to a baseline of `[a asc, b desc]` beside a ranking of `[c asc]` —
the vectors `operation-boundary-a` and `operation-boundary-b` are that pair.

Every string has exactly one spelling: the packet owner's, which the codec
carries so that no second convention is introduced here.
Inheriting that rule means inheriting its rejections: whatever the packet's
writer refuses, this codec refuses before any vocabulary binder runs, and it
repairs nothing.
Measured, no `System.Text.Json` encoder implements the rule — each uppercases
the hex, escapes U+007F, U+0085, U+2028, and U+2029, and turns a supplementary
character into a surrogate-pair escape — so the codec must reuse the packet's
writer, and the checker carries its own. The vectors `c0-control-lowercase-hex`,
`raw-scalars-above-ascii`, and `literal-backslash-u-text` are the cases that
distinguish the two.

## What the codec does not know

The codec validates structure and canonical form. It does not interpret a
value. It cannot tell a package identifier from a framework name, does not know
which keys a vocabulary admits, does not know how terms compose, and does not
know that `Microsoft.Extensions.*` is a host spelling whose typed value is
`Microsoft.Extensions.` — all of that is decided before intent reaches it, by
the [model](portable-query-intent.md) and the vocabulary. A value that is
structurally a string of admissible length is a valid value here, whatever it
says. This is why the negative vectors contain no "bad value" case: there is no
such thing at this layer.

## Limits

The packet owner delegates concrete payload limits to this codec, so they are
pinned as constants rather than described as "bounded". They are a
compatibility surface with the same standing as tokens: a link one build emits
must be admissible on another, so no vocabulary may relax them, though one may
declare stricter limits for its own keys.

Three properties of the limits matter more than their values:

- **Every text maximum counts UTF-8 bytes**, not scalars or grapheme clusters, so
  non-ASCII values count unambiguously.
- **Limits are charged as parsed, before duplicate collapse**, on both sides of
  the codec. An intent declaring twenty-five identical terms is refused before
  it is canonicalized, and bytes carrying twenty-five terms are refused before
  they are decoded; collapse can never be used to force unbounded parse work.
  The vectors `too-many-terms-as-parsed` and `too-many-terms` are the two sides.
- **The per-part maxima are not jointly achievable, and which limit binds first
  depends on shape.** A term-heavy payload hits the byte limit long before the
  part counts; the payload with the most JSON values — the vector
  `joint-maximum` — is small in bytes but reaches 204 values, because the role
  rules couple stages to order operations and mixed order kinds carry more
  scalars than uniform ones. Both stay beneath the packet's per-payload allowance,
  and every declared maximum is reachable by some admissible payload, so no
  limit is a ceiling nothing valid can touch.

## Packet projection

Canonical intent is the payload of one `[queryId, payload]` tuple in format 2's
query table. Parse and canonical write must round-trip byte-for-byte; the pinned
limits must be enforced before any vocabulary binder runs; and semantically
identical query states deduplicate on the `(queryId, payload bytes)` pair, never
on payload bytes alone — matching the packet's stated table ordering.

Format 2 permits query references from its leading Workspace entry and from
per-coordinate view entries, requires one view state per coordinate tuple, and
rejects empty contexts. It therefore remains sufficient for ordinary
state-bound queries but cannot represent a query-only package-discovery
scenario. [Workspace definitions](workspace-definitions.md) now specifies a
format-3 query-only composition whose leading null-navigation row carries one
coordinate-free primary query without supplying the Workspace subject as query
input. Implementing that attachment remains a counted step in
[#6971](https://github.com/richlander/dotnet-inspect/issues/6971); this document
supplies only the payload it carries.

## Required gates

Named Release gates the implementation must add. Each names the vectors that
witness it; a gate without a vector is a gap in the vectors file, not a gate that
needs no evidence.

| Gate | Contract | Witnesses |
| --- | --- | --- |
| `QueryIdentityIsThePair` | Identical payload bytes under different vocabularies remain distinct; the same vocabulary and bytes are one query. | `same-bytes-two-vocabularies`, `same-query-two-entries` |
| `CanonicalFormRoundTripsByteForByte` | Every canonical vector decodes and re-emits to exactly its own bytes. | every `encode` vector |
| `CanonicalFormIsIndependentOfMeaninglessSequence` | Term sequence, exact duplicate terms, bound sequence, and outer order-operation sequence do not change canonical bytes. | `package-query-entered-in-another-order`, `package-query-with-exact-duplicate`, `bounds-entered-in-another-order`, `order-operations-supplied-out-of-role-order` |
| `MeaningfulSequenceSurvives` | Stage sequence and field-term sequence inside one operation survive exactly; intents differing only there have different bytes. | `row-query-reversed-pipeline`, `field-sequence-a`, `field-sequence-b`, `operation-boundary-a`, `operation-boundary-b` |
| `TextOrderIsScalarOrder` | Every sort compares by Unicode scalar value, never UTF-16 code units. | `scalar-order-not-utf16-order` |
| `RepeatedBoundDimensionIsRefused` | One dimension twice is refused in intent and on the wire, whether the maxima differ or match. | `repeated-dimension-in-intent`, `repeated-bound-dimension-*` |
| `NonCanonicalBytesAreRefused` | Reordered, padded, duplicate-bearing, or non-canonically escaped bytes are refused, never repaired. | `pretty-printed`, `terms-unsorted`, `bounds-unsorted`, `properties-out-of-order`, `exact-duplicate-terms-on-the-wire`, `order-operations-out-of-role-order-on-the-wire`, `escaped-ascii`, `uppercase-c0-hex`, `escaped-non-ascii`, `surrogate-pair-escape` |
| `ShapeIsClosed` | Anything not JSON, not an object, or carrying an unknown or duplicate property, an empty part, a wrong arity, an unknown token, a bad integer, or `null` outside a window bound is refused. | `malformed`, `not-an-object`, and the structural `reject` vectors |
| `DeclaredLimitsPrecedeBinding` | Every pinned maximum is enforced as parsed, before collapse and before any binder, on intent and on bytes. | `too-many-terms-as-parsed`, `nine-bounds`, `nine-stages`, `nine-order-operations`, `identity-one-over`, `too-many-terms`, `too-many-order-field-terms`, `value-too-long`, `identity-too-long`, `payload-too-large` |
| `DeclaredMaximaAreAdmissible` | A payload at each exact maximum is admitted; the joint maximum produces exactly 204 values. | `joint-maximum`, `identity-at-maximum`, `value-at-maximum`, `payload-near-byte-maximum` |
| `StringRuleIsThePacketOwners` | Short escapes, lowercase `\u00xx`, and raw UTF-8 above U+001F round-trip exactly; unpaired surrogates are refused; literal backslash text is text. | `quotes-backslash-and-short-escapes`, `c0-control-lowercase-hex`, `raw-scalars-above-ascii`, `literal-backslash-u-text`, `unpaired-high-surrogate`, `unpaired-low-surrogate` |
| `VectorsAreTheGate` | The product codec's tests consume `vectors.json` directly, and the design-stage probe retires. | the file itself, embedded in `DotnetInspector.PortableQueries.Tests` |

Two properties have no vector because no vector can witness them, and the
codec's own tests must: that the limits are identical across builds and
vocabularies, and that cancellation is observed before any binder runs.
`PortableQueryCodecContractTests` carries both.

## Non-claims

- No intent model. The parts, their composition, resolution, and replay rules
  are [Portable query intent](portable-query-intent.md).
- No vocabulary. Package Query's keys, operands, tiers, and UI are
  [#6972](https://github.com/richlander/dotnet-inspect/issues/6972).
- No value semantics. What a value means, whether it is well-formed for its key,
  and whether two values are equivalent are never this codec's to decide.
- No change to the packet's coordinate arms, outer bounds, hardening, escaping
  rules, or record family, and no new packet format. The coordinate-free
  Package attachment remains owned by
  [Workspace definitions](workspace-definitions.md).
- No transport. Base64url framing, URL shape, and storage are the adopting
  host's, under the packet owner's existing rules.
