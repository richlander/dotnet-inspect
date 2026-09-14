# Portable query payload

## Status

**Unverified.** This is a design-only contract. No part of it is implemented,
and every gate in [Required gates](#required-gates) is a requirement on the
implementation rather than a property enforced today. Statements about what a
codec does, admits, or refuses describe the contract an implementation must
satisfy, not observed behavior.

This is **slice 2 of 2** under
[#6971](https://github.com/richlander/dotnet-inspect/issues/6971), stacked on
[Portable query intent](portable-query-intent.md), which owns the model this
encodes. Read that document first: it defines the parts named here — terms,
execution bounds, selection stages, and order operations — and the resolution
and replay rules this encoding exists to make portable.

## Owner and consumers

This focused owner defines the **single byte spelling** a query intent has: how
it canonicalizes, the closed JSON object it becomes, the tokens and tuple shapes
inside that object, the limits it is bound by, and how it fills the delegation
slot the share packet already reserves.

Its claim is that one intent has exactly one canonical byte sequence, and that
two independent implementations — the .NET codec and the Browser adapter — must
produce it identically or sharing does not work.

[Workspace definitions](workspace-definitions.md) packet format 2 defines its
`q` table as `[queryId, payload]`, where the payload is "the closed JSON object
emitted by that query owner's version-2 packet codec" and must "round-trip its
payload byte-for-byte through parse and canonical write." It delegates property
order, string and numeric grammar, selector encodings, and limits to that codec.
This document is that codec's contract. It does not redefine the packet family,
its outer bounds, its base64url and JSON hardening, or any coordinate-bearing
arm.

## Canonicalization

Canonicalization is syntactic. Two intents are the same query when they carry
the same vocabulary **and** identical canonical bytes; equality is decided on
that pair and nowhere else. Payload bytes alone are not an identity — the same
bytes under two vocabularies are two different queries, because the keys inside
them resolve against different namespaces. [Packet
projection](#packet-projection) states where the vocabulary travels.

Everything below governs the bytes half of that pair.

- Terms sort by key, then operator, then value, each ordinal.
- Exactly duplicated terms collapse. A repeated key bearing a different
  operator or value is preserved. How the surviving terms compose — conjunction,
  or an OR-union inside a declared combining family — is the vocabulary's
  question, not the codec's, and so is whether the result is satisfiable.
- Execution bounds sort by dimension identity, ordinal. A repeated dimension is
  **invalid**, not collapsed and not last-wins, even when both entries carry the
  same maximum. Duplicate terms collapse because conjunction is idempotent —
  asking twice for the same predicate asks the same question — while a second
  bound on one dimension has no meaning to preserve, and silently choosing one
  would let a share link authorize work its author did not request.
- Selection stages are **position-significant and never reordered**. Sequence is
  their meaning, because each stage consumes the preceding stage's output.
- Order operations sort by role: the `base` operation first, then ranking
  operations by ascending stage index. Once every operation names its own role,
  the outer sequence carries no information, so leaving it unnormalized would
  give one query two canonical spellings.
- Sequence **inside** one field-list order operation is preserved exactly, since
  field terms compose lexicographically in declaration order.
- Scalar escaping follows the packet's pinned canonical rules rather than a
  second escaping convention.
- Values are never normalized here. Package identifiers, framework names, and
  assembly names each have owner-specific equivalence rules, and applying any
  of them at this layer would make canonical bytes depend on a vocabulary's
  current semantics.

The governing rule is that **sequence is canonical only where sequence carries
meaning**. It does inside the stage pipeline and inside one field-list order
operation, so those emit exactly as declared. It does not among conjoined terms,
among independent execution bounds, or among role-bearing order operations, so
those are normalized. No rule may move a part from one treatment to the other.

The value rule has a consequence worth stating plainly: two spellings that a
vocabulary would resolve to the same query can canonicalize to different bytes
and therefore share as different links. Share identity is syntactic. A
vocabulary that wants spelling-independent identity must normalize **before**
constructing intent, where the normalization is visible to the user who typed
it, rather than inside a codec where it would silently rewrite what was shared.

## The canonical payload

`workspace-definitions.md` delegates payload property order, string and numeric
grammar, selector encodings, and limits to the query owner's codec, so this
design fixes them. Two independent implementations — the .NET codec and the
Browser adapter — must produce identical bytes, so none of this may be left to
an implementation's choice.

The payload is one closed JSON object whose four properties are the four
serializable parts of [the intent contract](portable-query-intent.md#the-intent-contract). Each wire
property is an abbreviation of exactly one part name, and the two spellings are
never interchangeable: the long name is how this document and its consumers
refer to the part, and the short one is the only form that appears on the wire.

| Property | Part | Contents |
| --- | --- | --- |
| `t` | `terms` | The term set, each `[key, operator, value]` |
| `b` | `bounds` | The execution-bound set, each `[dimension, maximum]` |
| `s` | `stages` | The selection-stage pipeline |
| `o` | `order` | The order operations |

The fifth part, `vocabulary`, has no property here: it is the tuple's `queryId`,
as [Packet projection](#packet-projection) describes.

Abbreviations are used because the payload's budget is 3 KiB and the packet
family already spells its own fields this way — format 2's top level is `f`,
`t`, `g`, `a`, `x`, `q`, `v`. Note that the packet's top-level `t` is its
coordinate-tuple table while this payload's `t` is the term set. The reuse is
deliberate and unambiguous, because the two live at different scopes and a
parser never sees both in one object, but an implementer reading both documents
should not assume they mean the same thing.

Property order is exactly `t`, `b`, `s`, `o`, and a part that is absent or empty
is **omitted entirely** rather than emitted as `null` or `[]`. Emission is
compact: no whitespace appears between tokens, so every payload in this document
is shown exactly as it would be written.

```json
{"t":[["depends","eq","Serilog"]],"b":[["candidates",200]],"s":[["head",20]],"o":[["base","fields","downloads","desc"]]}
```

- `t` is sorted. Each term is three strings, and the operator is its identity
  token, not a symbol.
- `b` is sorted by dimension. Each bound is a string and a JSON integer, and
  no dimension appears twice.
- `s` is in declared order, encoded per the stage table below.
- `o` is in role order — `base` first, then ranking
  operations by ascending stage index — encoded per the order table below.
  Declaration order is not preserved here; only the field-term sequence inside
  one operation is.

Every token is fixed. Implementations do not derive one from a .NET enum name,
a CLI spelling, or a display label:

| Kind | Canonical tokens |
| --- | --- |
| Operator | `eq`, `ne`, `gte`, `lte` |
| Direction | `asc`, `desc` |
| Stage | `head`, `tail`, `window`, `top` |
| Order role | `base`, or a JSON integer index into `s` |
| Order kind | `named`, `fields` |

The operator set is exactly the four identities the row predicate syntax already
admits; equality and inequality plus the two inclusive ordered comparisons. There
is no strict `lt` or `gt`.

Each stage tuple has fixed arity, so no stage is ambiguous with another:

| Stage | Tuple | Notes |
| --- | --- | --- |
| Head | `["head", count]` | `count` is a positive integer. |
| Tail | `["tail", count]` | `count` is a positive integer. |
| Top | `["top", count]` | The ranking order binds through `o`, not here. |
| Window | `["window", start, end]` | Arity is always 3. An omitted bound is positional `null`; present bounds are positive 1-based inclusive integers. |

Positional `null` is permitted **only** in the two `window` endpoint slots.
Nowhere else may a slot carry `null`.

Each order operation carries its own role, kind, and boundary, so operations
cannot run together:

| Kind | Tuple |
| --- | --- |
| Named | `[role, "named", identity, direction]` |
| Field list | `[role, "fields", key, direction, key, direction, ...]` |

`role` is `base` for the baseline order, or the integer index of the `s` stage
whose ranking it supplies. The operation array is its own boundary, so a
baseline of `[a asc]` beside a ranking of `[b desc, c asc]` cannot serialize
identically to a baseline of `[a asc, b desc]` beside a ranking of `[c asc]`.
At most one operation carries `base`, and at most one carries any given stage
index; a role naming a stage that is not `top` is invalid.

Operations emit in role order — `base` first, then ascending stage index — so
one assignment of baseline and rankings has exactly one spelling.

Numbers are JSON integers with no sign, leading zero, fraction, or exponent.
Strings use the packet's pinned canonical scalar escaping rather than a second
convention. Unknown properties, duplicate properties, a repeated bound dimension,
a present-but-empty array, and any non-canonical scalar form are invalid
payloads, refused during payload validation before any vocabulary binder runs.

Inheriting that escaping means inheriting its rejections. `workspace-definitions.md`
refuses unpaired surrogates rather than substituting U+FFFD, so an unpaired
surrogate is a **negative decode vector here too**: it is refused before the
vocabulary binder runs and never reaches intent. Round-trip coverage is for valid
Unicode scalar sequences; hostile-token coverage for anything else is rejection
coverage. Accepting or repairing such a value would break the packet's byte
identity, which is the property the whole contract rests on.

**Vocabulary is not in the payload.** It is the tuple's `queryId`, so it appears
exactly once and cannot disagree with itself. Canonical identity is therefore
the pair `(queryId, payload bytes)`, which is also the key format 2 already
sorts and deduplicates on. A standalone encoding outside a packet — the `/query`
share link — carries the same pair rather than reintroducing the field.

## Declared limits

`workspace-definitions.md` delegates concrete payload limits to the query
owner's codec, so this design pins them rather than saying only "bounded". They
are a compatibility surface with the same standing as keys and operators: a link
one build emits must be admissible on another, so these maxima cannot vary by
build or by vocabulary.

| Limit | Maximum |
| --- | --- |
| Canonical payload | 3 KiB of UTF-8 |
| Nesting depth | 4, which the defined shape reaches exactly |
| Terms | 24 |
| Execution bounds | 8 |
| Selection stages | 8 |
| Order operations | 8, counting the baseline and every ranking operation |
| Order field terms | 8 in total across every operation |
| Key or dimension identity | 64 bytes of UTF-8 |
| Value token | 256 bytes of UTF-8 |

Every text maximum counts UTF-8 bytes, not scalars or grapheme clusters, so the
count is unambiguous for non-ASCII values.

The per-part maxima are independent ceilings and are **not jointly achievable**:
24 terms each carrying a 256-byte value would far exceed 3 KiB. The payload byte
limit binds first, and the part counts exist to bound parse work and value count
before that total is known.

Each limit keeps the payload beneath the packet's per-payload allowance of 4 KiB,
depth 12, and 256 JSON values. The shape above reaches depth 4 exactly — object,
array, inner array, scalar — and the declared maximum is that same 4, so every
limit in the table is reachable by an admissible payload rather than being a
ceiling no valid payload can touch.

Its worst-case JSON value count is 197:

| Part | Values | Worst case |
| --- | --- | --- |
| Object | 1 | |
| `t` | 97 | one array, 24 term arrays, 72 strings |
| `b` | 25 | one array, 8 bound arrays, 16 scalars |
| `s` | 33 | one array, 8 stage arrays, 24 scalars — every stage a three-slot `window` |
| `o` | 41 | one array, 8 operation arrays, 16 role and kind scalars, 16 field-term scalars |

A payload admitted here therefore cannot breach the outer bound. The `s` figure
uses the widest stage tuple rather than the narrowest, because a payload may use
`window` throughout.

Limits are charged **as parsed, before duplicate collapse**. A payload declaring
thirty terms that would collapse to three is rejected on the twenty-fifth rather
than accepted on the third, so collapse can never be used to force unbounded parse
work. The canonical form must independently satisfy every limit.

A vocabulary may declare stricter limits for its own keys; it may not relax
these, because relaxation would make a link admissible on one build and not
another.

## Packet projection

Canonical intent is the payload of one `[queryId, payload]` tuple in packet
format 2's query table: `queryId` is the vocabulary identity, and the payload is
the closed JSON object fixed by [The canonical payload](#the-canonical-payload).

- Parse and canonical write must round-trip byte-for-byte, satisfying the
  packet's owner-codec requirement.
- The pinned maxima in [Declared limits](#declared-limits) sit beneath the
  packet's per-payload allowance and must be enforced before any vocabulary
  binder runs.
- Semantically identical query states must deduplicate on the `(queryId,
  payload bytes)` pair, never on payload bytes alone,
  matching the packet's stated table ordering and dedup rule.

Packet format 2 retains `t`, `g`, `a`, and `x`, requires one view-state entry
per coordinate-table index, and rejects empty contexts. A package query has no
coordinate to carry a view state, so the packet family needs an arm that
carries query intent with no coordinate table. **That arm is owned by
[workspace definitions](workspace-definitions.md)**; this design supplies only
the payload it would carry.

## Worked examples

Keys, dimension identities, and named orders below are illustrative. The
vocabulary owner defines the real ones; these show the encoding, not a
vocabulary. [Portable query intent](portable-query-intent.md#worked-examples)
carries the model-level examples, including what a restored query must refuse.

### A package query, end to end

Someone narrows a package query to packages depending on Serilog, including
prereleases, over the first 200 candidates. Three spellings, one intent:

```text
rail      Microsoft.Extensions.*   [depends: Serilog]  [+ prerelease]
CLI       package query "Microsoft.Extensions.*" \
            --where "depends=Serilog" --where "prerelease=include" --take 200
intent    terms  (depends, eq, Serilog), (prefix, eq, Microsoft.Extensions.),
                 (prerelease, eq, include)
          bounds (candidates, 200)
```

Two model rules decide what this intent contains before any of it reaches the
codec, and both are owned by the parent slice rather than restated here: the
source input travels as a term because intent has [no privileged scope
slot](portable-query-intent.md#worked-examples), and the term's value is the
typed prefix `Microsoft.Extensions.` rather than the host's
`Microsoft.Extensions.*`, because [intent never contains an L3
spelling](portable-query-intent.md#what-intent-never-contains).

The byte-level consequence is this slice's: two hosts whose spelling conventions
differ for one typed prefix hold the same intent, so they must emit the same
bytes. Nothing in the encoding may reintroduce a distinction the model already
removed.

Terms sort ordinally: `depends`, `prefix`, `prerelease`. There is no stage
pipeline and no order, so `s` and `o` are omitted rather than emitted empty:

```json
{"t":[["depends","eq","Serilog"],["prefix","eq","Microsoft.Extensions."],["prerelease","eq","include"]],"b":[["candidates",200]]}
```

The packet tuple pairs those bytes with the vocabulary, and the share link
carries that same pair:

```json
["package.query",{"t":[["depends","eq","Serilog"],["prefix","eq","Microsoft.Extensions."],["prerelease","eq","include"]],"b":[["candidates",200]]}]
```

Opening the link re-runs the request. It restores no rows, no counts, and no
completion state, because it never carried any.

### The same query, typed the other way round

Someone else selects prerelease first, then the dependency. Their intent reaches
the codec in the opposite order and produces identical bytes, so both people
share one link and the packet deduplicates the two states into one:

```json
{"t":[["depends","eq","Serilog"],["prefix","eq","Microsoft.Extensions."],["prerelease","eq","include"]],"b":[["candidates",200]]}
```

Term sequence carries no meaning, so canonicalization removes it. Contrast the
next example, where sequence is the question.

### A row query with a baseline order and a ranking

Rows of at least medium confidence, ordered by name, first twenty, then the five
worst by triage severity:

```text
intent    terms  (confidence, gte, medium)
          stages head 20, then top 5
          order  base: fields name asc
                 stage 1: named triage desc
```

```json
{"t":[["confidence","gte","medium"]],"s":[["head",20],["top",5]],"o":[["base","fields","name","asc"],[1,"named","triage","desc"]]}
```

`s` keeps declaration sequence, because each stage consumes the previous stage's
output. The ranking operation names stage index `1` — the `top` — so it cannot
drift onto the `head`. Operations emit `base` first, then by ascending stage
index, so this assignment has exactly one spelling.

Reversing the pipeline asks a different question, not the same one rendered
differently: ranking the whole set and then taking a prefix **can** select
different rows from taking a prefix and then ranking it, and coincides only when
the ranked survivors already fall inside the prefix. Because the question
differs, the bytes differ whether or not a particular input distinguishes them:

```json
{"t":[["confidence","gte","medium"]],"s":[["top",5],["head",20]],"o":[["base","fields","name","asc"],[0,"named","triage","desc"]]}
```

### Two orders that must not collapse

One `top` stage, the same three field terms, split differently between the
baseline and the ranking. These are different questions and must have different
bytes:

```json
{"s":[["top",1]],"o":[["base","fields","a","asc"],[0,"fields","b","desc","c","asc"]]}
{"s":[["top",1]],"o":[["base","fields","a","asc","b","desc"],[0,"fields","c","asc"]]}
```

Each operation is its own array, so the boundary between baseline and ranking
survives. Flattening them into one sequence of `[reference, direction]` pairs
would make both spellings identical.

## Required gates

Named Release gates the implementation must add. Model-level gates — atomic
resolution, starting no work, visible replay refusal — belong to
[Portable query intent](portable-query-intent.md#required-gates).

| Gate | Contract |
| --- | --- |
| `QueryIdentityIsThePair` | Two states carrying identical payload bytes under different vocabularies remain distinct through canonicalization, packet-table ordering, and deduplication; payload bytes alone never establish equality. |
| `IntentCanonicalFormRoundTripsByteForByte` | Parse then canonical write reproduces exact bytes for every supported term, operator, execution bound, selection stage, order operation, and escaping vector, including all four `window` endpoint combinations and both order kinds. |
| `IntentCanonicalFormIsIndependentOfTermOrder` | Term sequence, duplicate terms, and execution-bound declaration sequence do not change canonical bytes; bounds sort by dimension identity; semantically identical states deduplicate. |
| `RepeatedBoundDimensionIsRefused` | A payload carrying one dimension twice is refused during payload validation before any vocabulary binder runs, whether the two maxima differ or are identical, and is never collapsed, reordered, or resolved last-wins. |
| `SelectionStageSequenceSurvivesRoundTrip` | Stage sequence survives byte-for-byte and is never sorted, deduplicated, or merged into the bound set; two intents differing only in stage sequence have different canonical bytes, witnessed by the `Head`/`Top` commutation case. |
| `OrderOperationsAreInjective` | Role, kind, operation boundary, and direction survive round-trip exactly; field-term sequence inside one operation is preserved while outer operations emit in role order, so one assignment of baseline and rankings has exactly one spelling; a ranking operation stays bound to its stage index; two intents differing only in baseline order, or only in how the same field terms divide between baseline and ranking, have different canonical bytes. |
| `DeclaredLimitsPrecedeVocabularyBinding` | Every limit in the declared-limits table is enforced against the payload as parsed, before duplicate collapse and before any vocabulary binder runs, with cancellation observed. |
| `DeclaredLimitsAreBuildInvariant` | The pinned maxima are identical across vocabularies and builds; a payload at each exact maximum is admissible and one byte past each is refused. |
| `DocumentedExamplesAreCanonical` | Every JSON payload example in this document parses, validates, and canonically re-emits to exactly its own bytes through the production codec, so an example cannot drift from the contract it illustrates. |
| `HostileIntentTextRemainsContained` | Adversarial value tokens that are valid Unicode scalar sequences — quotes, backslashes, lowercase C0 escapes, raw U+007F/U+0085/U+2028/U+2029, and a supplementary-plane scalar — round-trip through `InertText` construction and canonical escaping without escaping containment or reaching a diagnostic. |
| `NonCanonicalTextIsRefusedBeforeBinding` | Unpaired surrogates and every other non-canonical scalar form are refused at decode, before any vocabulary binder runs, and are never accepted, repaired, or substituted with U+FFFD. |

## Non-claims

- No intent model. The parts, their composition, resolution, and replay rules
  are [Portable query intent](portable-query-intent.md).
- No vocabulary. Package Query's keys, operands, tiers, and UI are
  [#6972](https://github.com/richlander/dotnet-inspect/issues/6972).
- No change to the packet's coordinate arms, outer bounds, hardening, escaping
  rules, or record family, and no new packet format defined here. The
  coordinate-free arm a query needs remains owned by
  [Workspace definitions](workspace-definitions.md).
- No transport. Base64url framing, URL shape, and storage are the adopting
  host's, under the packet owner's existing rules.
