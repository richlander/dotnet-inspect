# Contained document publication

## Status and owner

**Proposed focused pattern.**
[#10111](https://github.com/richlander/dotnet-inspect/issues/10111) tracks the
design, shared construction contract, first Browser adoption, CLI assessment,
and retirement of the transitional package README path.

**Contained document publication** owns this exact claim:

> An owner-issued structured document becomes publishable only after every
> externally authored presentation leaf has crossed its declared
> `TextPolicy`, the contained leaves correspond exactly to the structure that
> requested them, and the final value preserves that trusted structure.

The owner defines:

- the distinction between source, structured, contained, and publishable
  document states;
- presentation-leaf identity and complete correspondence;
- construction of a publishable document only from trusted structure and
  policy-contained leaves;
- separation of displayed text from actionable values;
- stale, missing, duplicate, or surplus leaf failure; and
- the minimum proof a host sink requires before publication.

It does not define:

- byte acquisition, ownership, decoding, or exact-byte output;
- Markdown, HTML, DOM, Markout, or another document grammar;
- structural parsing, sanitization, or an allow list;
- `TextPolicy`, visual spelling, `InertString`, or `InertUtf8String`;
- URI identity, validation, navigation, or network authority;
- one universal document tree or renderer; or
- a host's interaction, styling, accessibility, or layout.

`docs/overview.md` is release-managed. The tracker records the future authority
entry rather than this design editing that central file.

## Product question

**How can a host publish an externally authored structured document without
treating either raw source or serialized markup as contained presentation?**

`WrongTurn@0.1.15` is the motivating real package. Its root `README.md`
contains bidi format controls in displayed URLs. The package README repair in
[#10098](https://github.com/richlander/dotnet-inspect/pull/10098) correctly
parses and sanitizes the Markdown before applying managed visual containment.
Its focused implementation carries the complete serialized HTML through one
`InertString`. That is safe, but one string type cannot state both:

- the markup structure came from the structural owner; and
- every displayed leaf crossed its required visual policy.

The target makes those propositions separate and then composes them:

```text
owner-issued source
  -> parser and structural validator
     -> structured document with identified presentation leaves
        -> policy containment of every leaf
           -> contained document with unchanged trusted structure
              -> host-specific publication
```

No later phase parses a contained leaf to recover identity or structure.

## Four document states

### Source

Source is exact or decoded input admitted by the source owner. It is not
publishable.

An exact UTF-8 source may retain an `OpaqueByteRange`; an already-decoded
source may retain a `DecodedTextDocument` or another owner-issued immutable
value. Those owners decide acquisition, decoding, bounds, validity, and
lifetime. This pattern neither exposes their retained input nor turns opacity
into presentation safety.

`OpaqueByteRange` is useful when a parser can preserve unchanged source runs,
source locations, or exact evidence without allocating presentation strings.
It cannot represent every rendered leaf: Markdown escapes, entities,
normalization, generated labels, and structural transformations can produce
text that is not one contiguous source range. Such text becomes a new
owner-issued leaf value rather than a fabricated source slice.

Exact-byte transfer remains a parser or exact-output capability. It is never a
route from opaque source directly to a display sink.

### Structured

A structured document is a non-publishable value issued by the grammar and
structural-safety owner. Its structure has already passed that owner's parser,
bounds, and allow-list decisions.

Every externally authored value that a reader can see is represented as one
presentation leaf. A leaf carries:

- a document-local identity;
- its owner-declared `TextPolicy`;
- the rendered text requiring containment; and
- its structural placement, retained only by the structure owner.

The rendered text may originate from an exact source-backed run, decoded input,
or parser-generated text. Those origins do not change the containment rule.

The structured value may also retain actionable values such as validated link
targets. An actionable value is not a presentation leaf merely because the
same source spelling can also be shown to a reader.

### Contained

Containment constructs one `InertString` or `InertUtf8String` for every
presentation leaf under that leaf's declared policy. It does not sanitize
structure, rewrite actionable values, or choose whether a leaf is displayed.

The containing operation returns leaf identity with each contained value.
Before constructing a contained document, composition verifies:

- every requested identity occurs exactly once;
- no unrequested identity is present;
- every result belongs to the same structure generation;
- no result is stale relative to the current structure; and
- the structural value itself is unchanged.

Failure to contain one leaf, identity mismatch, duplicate, omission, surplus,
or stale result prevents construction. There is no partial publishable value
and no fallback to untreated text.

### Publishable

A publishable document is trusted structure whose complete presentation-leaf
population has been replaced by contained values. Its type, brand, or
construction boundary must be distinct from raw source, merely parsed
structure, merely sanitized structure, and an arbitrary serialized string.

A host sink accepts only the publishable form. Serialization may lower that
form after construction, but serialized Markdown or HTML is not itself proof
of complete containment.

## Displayed and actionable values

A link demonstrates the required split:

```text
displayed label -> presentation leaf -> InertString
navigation target -> validated actionable value -> host navigation
```

Visually containing a target would change the value followed by the browser.
Publishing an untreated label would let the artifact control what the reader
sees. A document therefore retains both roles when it both displays and acts on
one source value.

The same rule applies to image alternative text versus image source, code
labels versus copy payloads, visible paths versus file operations, and any
other value with both presentation and functional roles.

Structural owners declare a closed `TextPolicy` for each leaf. Multi-line prose
and code normally use `TextPolicy.Prose`; one-line labels, alternative text,
tooltips, and metadata normally use `TextPolicy.Field`. This pattern guarantees
that the declared policy is applied. Each adopting owner remains responsible
for assigning the policy that matches its structural role.

## Browser package README adoption

Inspect Web package README publication is the first planned adopter. Its target
composition is:

```text
decoded package Markdown
  -> Marked
     -> DOMPurify document fragment
        -> identified text and displayed-attribute leaves
           -> one managed containment batch
              -> exact leaf correspondence
                 -> contained document fragment
                    -> DOM publication
```

The Browser owner continues to own Marked, DOMPurify, the HTML allow list, and
the chosen DOM structure. The managed containment boundary continues to
construct `InertString` and the generated TypeScript contract continues to
carry its brand.

The target path:

- contains text nodes after Markdown escapes and entities have resolved;
- contains displayed attributes such as alternative text and tooltips;
- keeps validated link targets and other actionable attributes outside visual
  containment;
- reinserts contained values through text or attribute APIs rather than
  reparsing them as markup;
- constructs a Browser-local contained-fragment brand only after exact leaf
  correspondence; and
- permits the document viewer to publish only that brand.

Frontmatter displayed beside the Markdown participates in the same document
composition even when the Browser constructs its structure directly rather
than through Marked.

The whole-serialized-HTML containment operation from #10098 remains a safe
transitional path. The adoption retires it only after the contained-fragment
path carries the same ordinary rendering behavior and stronger leaf-completion
evidence.

## CLI and Markout boundary

Ordinary CLI package-document output currently preserves exact document
content; it is not a rich Markdown renderer and does not become one through
this pattern. Exact-byte output remains outside contained publication.

Markout already owns host-neutral structure for product-generated documents,
and many of its view models carry `InertString` leaves. The planned CLI slice
assesses whether any externally authored structured document crosses Markout
without complete typed leaf containment. It adopts this pattern only for such a
consumer. It does not migrate every Markout document merely to introduce a new
wrapper around already-contained product values.

## Failure and bounds

Every phase keeps its existing failure ownership:

- source acquisition or decoding reports no structured document;
- parser, structural limit, or sanitization failure reports no structured
  document;
- containment failure or correspondence mismatch reports no contained
  document; and
- host lowering failure reports no successful publication.

The structure owner bounds nodes, attributes, depth, and parser work. The
containing operation bounds leaf count and aggregate input/output text. Inert
Text owns scalar policy and spelling. No layer silently clips, substitutes
untreated text, or returns a success-shaped empty document after failure.

Asynchronous containment binds results to one immutable structure generation.
Replacing or closing a document invalidates its pending result. A result from
an older generation cannot complete a newer document merely because its leaf
identities have the same numeric or textual spelling.

## Required evidence

| Property | Gate |
| --- | --- |
| Complete containment | A known structure cannot construct its publishable form with a missing, duplicate, surplus, failed, or wrong-generation leaf. |
| Structure preservation | Structure identity, element/attribute shape, and validated actionable values are equal before and after leaf containment. |
| Rendered-value containment | Markdown escapes, entities, ordinary prose, code, alternative text, tooltips, and frontmatter are contained after their structural transformations. |
| Literal distinction | An actual refused scalar and source text spelling its visible escape remain distinct after parsing, containment, and publication. |
| Action separation | A displayed link label is contained while its validated navigation target remains the structural owner's exact actionable value. |
| Stale rejection | Closing, replacing, or reparsing a document prevents an older containment result from publishing. |
| Sink closure | Raw source, parsed structure, sanitized-only structure, arbitrary HTML/Markdown strings, and partial containment cannot reach the publication sink's accepted type. |
| Real asset | The exact `WrongTurn@0.1.15` package README reaches the Browser sink with no raw refused scalar in displayed leaves and unchanged allowed structure. |
| Neighboring behavior | Ordinary package Markdown, inline formatting, tables, fenced code, links, frontmatter, and document lifecycle retain their supported behavior. |

The design is documentation-only and makes no implementation or performance
claim. Markdown lint is its local gate. Each implementation and adoption slice
must name the focused executable gates that establish its subset of this table.

## Adoption sequence

1. Lock this focused pattern and transfer package-document composition from
   Inert Text without changing that owner's scalar policy.
2. Implement the smallest shared leaf-containment and correspondence contract.
3. Adopt it in Inspect Web package README publication.
4. Assess the CLI/Markout boundary and adopt only an identified externally
   authored structured-document consumer.
5. Retire the transitional Browser whole-HTML containment operation.

The Browser package README is the first production consumer. Additional
document grammars or hosts adopt one owner at a time under
[Design scope and composition](../design-scope.md).

## Non-claims

This contract does not claim:

- a common parser, sanitizer, AST, DOM, serializer, or renderer;
- that every document retains `OpaqueByteRange`;
- zero-copy rendering when a parser generates or normalizes text;
- that contained text is suitable for identity, matching, parsing, or actions;
- that a structurally valid document has safe presentation leaves;
- that a contained leaf makes arbitrary surrounding structure safe;
- that complete containment makes an actionable value trustworthy;
- exact-byte reconstruction from a contained document; or
- completed adoption before the tracked implementation slices land.
