# Portable query payload

## Status

**Unverified.** This is a design-only contract. Nothing here is implemented, and
every gate in [Required gates](#required-gates) is a requirement on the
implementation rather than a property enforced today.

One thing does run. The exact shape of the payload and the vectors that witness
it are not prose; they live in
[`models/portable-query-payload/`](models/portable-query-payload/vectors.json)
and are checked by `eng/check-portable-query-payload-vectors.cs`. That checker
is a design-stage probe, reproducible on demand, not yet a CI gate. When the
product codec exists, its tests consume the same vectors file and the probe
retires.

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
`q` table as `[queryId, payload]`, where the payload is "the closed JSON object
emitted by that query owner's version-2 packet codec" and must "round-trip its
payload byte-for-byte through parse and canonical write." It delegates property
order, string and numeric grammar, selector encodings, and limits to that codec.
This document is that codec's contract. It does not redefine the packet family,
its outer bounds, its base64url and JSON hardening, or any coordinate-bearing
arm.

## Where the rules live

Three artifacts, one normative location per rule:

| Artifact | Owns |
| --- | --- |
| This document | The principles: what canonical form means, what identity is, what the codec may and may not know, how limits behave |
| `eng/check-portable-query-payload-vectors.cs`, its shape table | The exact structure: property order, tokens, tuple arity, declared maxima |
| `models/portable-query-payload/vectors.json` | The witnesses: every canonical form the contract promises and every rejection it requires |

The prose never restates a token, an arity, or a maximum. If a sentence here
seems to disagree with the shape table or a vector, the sentence is wrong.

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
role — sequence carries nothing, so canonical form supplies one. The vectors
`order-operations-supplied-out-of-role-order` and `row-query-reversed-pipeline`
show the two sides of that line.

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

Emission is compact, with no whitespace between tokens, and absent or empty
parts are omitted rather than emitted as `null` or `[]`. Bytes that arrive in
any other spelling — reordered, padded, or carrying a duplicate the canonical
form would have collapsed — are refused as non-canonical rather than repaired.
The packet owner already refuses non-canonical decoded JSON; this payload does
the same.

## Shape

The payload is one closed JSON object whose properties abbreviate the four
serializable parts of the intent contract:

| Property | Part |
| --- | --- |
| `t` | `terms` |
| `b` | `bounds` |
| `s` | `stages` |
| `o` | `order` |

The long name is how documents and consumers refer to a part; the short one is
the only form that appears on the wire. The abbreviations exist because the
payload's budget is small and the packet family already spells its own fields
this way. Note that the packet's top-level `t` is its coordinate-tuple table
while this payload's `t` is the term set; the two live at different scopes and a
parser never sees both in one object, but a reader of both documents should not
assume they mean the same thing.

Each part is an array of fixed-arity tuples. Every token — operator, direction,
stage, order role, order kind — is a fixed string that no implementation derives
from a .NET enum name, a CLI spelling, or a display label. The operator set is
exactly the four identities the row predicate syntax already admits; there is no
strict `lt` or `gt`. `window` alone may carry `null`, in its two endpoint slots,
so that an omitted bound is a positional gap rather than a shorter tuple. An
order operation carries its own role, kind, and boundary, so a baseline of
`[a asc]` beside a ranking of `[b desc, c asc]` can never serialize identically
to a baseline of `[a asc, b desc]` beside a ranking of `[c asc]` — the vectors
`split-a` and `split-b` are that pair.

The exact tokens, arities, and integer grammar are the shape table in the
checker. Strings use the packet's pinned canonical scalar escaping rather than a
second convention, and inheriting that escaping means inheriting its
rejections: an unpaired surrogate is refused before any vocabulary binder runs
and is never repaired to U+FFFD. The escaping rules pin lowercase hex for
control characters where general-purpose serializers emit uppercase, so the
codec must reuse the packet's canonical writer rather than a general serializer.

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
pinned in the shape table rather than described as "bounded". They are a
compatibility surface with the same standing as tokens: a link one build emits
must be admissible on another, so no vocabulary may relax them, though one may
declare stricter limits for its own keys.

Three properties of the limits matter more than their values:

- **Every text maximum counts UTF-8 bytes**, not scalars or grapheme clusters, so
  non-ASCII values count unambiguously.
- **Limits are charged as parsed, before duplicate collapse.** A payload
  declaring thirty terms that would collapse to three is refused on the
  twenty-fifth, so collapse can never be used to force unbounded parse work. The
  vector `too-many-terms` is that case.
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

Format 2 permits a query reference only from a per-coordinate view entry,
requires one view state per coordinate tuple, and rejects empty contexts. A
package query has no coordinate, so a payload that satisfies this contract still
has nowhere valid to attach. **That coordinate-free attachment is owned by
[Workspace definitions](workspace-definitions.md)** and is a counted step in
[#6971](https://github.com/richlander/dotnet-inspect/issues/6971); this document
supplies only the payload it would carry.

## Required gates

Named Release gates the implementation must add. Each names the vectors that
witness it; a gate without a vector is a gap in the vectors file, not a gate that
needs no evidence.

| Gate | Contract | Witnesses |
| --- | --- | --- |
| `QueryIdentityIsThePair` | Identical payload bytes under different vocabularies remain distinct through canonicalization, ordering, and deduplication. | any encode vector under two `queryId` values |
| `CanonicalFormRoundTripsByteForByte` | Every canonical vector decodes and re-emits to exactly its own bytes. | every `encode` vector |
| `CanonicalFormIsIndependentOfMeaninglessSequence` | Term sequence, exact duplicate terms, bound declaration sequence, and outer order-operation sequence do not change canonical bytes. | `package-query-entered-in-another-order`, `package-query-with-exact-duplicate`, `order-operations-supplied-out-of-role-order` |
| `MeaningfulSequenceSurvives` | Stage sequence and field-term sequence inside one operation survive exactly; two intents differing only there have different bytes. | `row-query-reversed-pipeline`, `split-a`, `split-b` |
| `RepeatedBoundDimensionIsRefused` | One dimension twice is refused before binding, whether the maxima differ or match. | `repeated-bound-dimension-*` |
| `NonCanonicalBytesAreRefused` | Reordered, padded, or duplicate-bearing bytes are refused, never repaired. | `pretty-printed`, `terms-unsorted`, `properties-out-of-order`, `exact-duplicate-terms-on-the-wire`, `order-operations-out-of-role-order-on-the-wire` |
| `ShapeIsClosed` | Unknown or duplicate properties, empty parts, wrong arity, unknown tokens, and `null` outside a window endpoint are refused. | the structural `reject` vectors |
| `DeclaredLimitsPrecedeBinding` | Every pinned maximum is enforced as parsed, before collapse and before any binder, with cancellation observed. | `too-many-terms`, `too-many-order-field-terms`, `value-too-long` |
| `DeclaredLimitsAreBuildInvariant` | The maxima are identical across vocabularies and builds; the joint maximum is admissible and produces exactly 204 values. | `joint-maximum` |
| `NonCanonicalTextIsRefusedBeforeBinding` | Unpaired surrogates and other non-canonical scalar forms are refused at decode and never repaired. | `unpaired-surrogate` |
| `HostileTextRemainsContained` | Valid scalar sequences carrying quotes, backslashes, and controls round-trip without escaping containment. | `quotes-and-backslash-in-value`, plus C0 and non-ASCII vectors once the codec adopts the packet writer |
| `VectorsAreTheGate` | The product codec's tests consume `vectors.json` directly, and the design-stage probe retires. | the file itself |

## Non-claims

- No intent model. The parts, their composition, resolution, and replay rules
  are [Portable query intent](portable-query-intent.md).
- No vocabulary. Package Query's keys, operands, tiers, and UI are
  [#6972](https://github.com/richlander/dotnet-inspect/issues/6972).
- No value semantics. What a value means, whether it is well-formed for its key,
  and whether two values are equivalent are never this codec's to decide.
- No change to the packet's coordinate arms, outer bounds, hardening, escaping
  rules, or record family, and no new packet format. The coordinate-free
  attachment a query needs remains owned by
  [Workspace definitions](workspace-definitions.md).
- No transport. Base64url framing, URL shape, and storage are the adopting
  host's, under the packet owner's existing rules.
