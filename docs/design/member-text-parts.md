# Exact member text parts

## Owner and exact claim

`CSharpText` is the architectural owner for the focused lexical prerequisite in
[#7718](https://github.com/richlander/dotnet-inspect/issues/7718).
This document owns its exact source-parts contract. `CSharpText.MemberSlicing`
consumes that structure through its existing selection of one declaration from
caller-supplied physical line evidence.

> A uniquely supported existing member selection exposes exact producer-issued
> source parts addressing the original decoded UTF-16 buffer, while
> `ExtractMemberText` behavior remains unchanged.

The result identifies:

- the complete member;
- each attached XML-documentation group;
- each applied attribute list;
- the signature; and
- the body, when one exists.

Every part carries an absolute zero-based UTF-16 start and length and a
one-based inclusive physical line range. The producer computes both forms from
the same lexer and declaration index. A consumer slices the supplied decoded
text by the issued span; it does not rescan text, normalize line endings, infer
columns from display lines, or search for declaration spellings.

This is the first of three planned deliveries for #7718:

1. this lexical primitive;
2. a SourceHouse/shared completed `InspectionEnvelope` operation; and
3. CLI and Browser/Wasm production adoption.

The later deliveries consume this contract without extending its authority.
This delivery does not acquire source, alter existing acquisition, advertise
flags, or define a host output shape.

The existing declaration index and normalized member extractor are the
behavioral baseline. Annotated source documents provide the analogous
half-open UTF-16 span convention; their decompiler provenance is not part of
this model. Unlike normalized member text, these parts retain original-buffer
coordinates so a later consumer can print authored text without reconstructing
its boundaries. No rendering strategy changes in this prerequisite.

## Input and selection boundary

The operation accepts the same decoded source text, physical start and end
lines, metadata-style member name, and optional active physical lines as
`ExtractMemberText`. It shares that operation's declaration selection,
constructor boundary reasoning, conditional projection validation, `#line`
refusal, typed invalid-coordinate exception, and explicit `null` outcome when
the index cannot vouch for a unique supported declaration.

Accessor evidence selects its containing property or event. It does not create
an accessor declaration that the index does not own. Constructor initializer
text remains part of the constructor signature. A constructor represented only
by field, property, or event initializer evidence remains unresolved.

Exact columns allow a unique member to share a physical boundary line with its
declaring type without including the type prefix or suffix. Line-only evidence
still cannot distinguish same-line sibling declarations, so that selection is
refused. Types, namespaces, unknown spans, ambiguous constructors, and other
existing unsupported selection shapes remain explicit `null` outcomes.

Conditional selection may blank unselected branches only to validate and
select the declaration. Every returned span addresses the original decoded
text, never the projected buffer. A conditional group that crosses a selected
declaration boundary or changes the vouched declaration remains refused.

## Part boundaries

All boundaries are token boundaries in the original decoded text. No part
includes unrelated text merely because it shares a physical line.

### Complete member

The complete member is one contiguous span from the earliest attached
XML-documentation token, applied attribute-list token, or declaration token
through the declaration's terminal token.

The span includes all original text between those endpoints, including
indentation, line terminators, blank lines, and ordinary comments between
attached documentation, attributes, and the declaration. An ordinary leading
comment does not by itself move the complete-member start before the
declaration. Text after the terminal token, including trailing whitespace and
comments, is excluded.

### XML documentation

XML documentation preserves authored markers and content. `///` groups and
`/** ... */` groups are source parts, not parsed XML. Consecutive lexical
documentation belonging to the selected declaration may form one exact group;
ordinary comments, blank lines, attributes, and other non-documentation text
separate groups. Multiple groups are retained in source order.

No documentation is a valid empty collection. This contract does not classify
XML elements, apply inheritance, compare authored and compiled documentation,
or replace the parsed-documentation work in
[#6583](https://github.com/richlander/dotnet-inspect/issues/6583).

### Attributes

Each member-applied attribute list is one exact source part, in source order.
Multiple lists remain distinct even when they share a line. Compilation-unit
`assembly` and `module` attributes are not member parts.

No attributes is a valid empty collection. Attribute names, arguments, and
metadata correspondence remain outside this lexical contract.

### Signature

The signature starts at the first declaration token after applied attributes
and ends at the last signature token. It excludes leading documentation,
attributes, trailing whitespace, and the body opener.

For a brace-bodied declaration, the opening brace is not part of the
signature. For an expression-bodied declaration, `=>` is not part of the
signature. Constructor initializers remain in the signature. A bodyless
declaration's terminal semicolon is its last signature token.

### Body

A brace body starts at `{`. An expression body starts at `=>`. The body extends
through the declaration's terminal token, including a terminal semicolon when
present. This preserves accessor blocks, expression bodies, and any
declaration-owned initializer tail after an accessor block.

A declaration with no body has no body part. Absence is a valid result, not
selection failure.

## Exact span and line semantics

Offsets count UTF-16 code units in the original `string`. `Start` is inclusive,
`Length` is nonnegative, and `Start + Length` is the exclusive end. The same
rules therefore cover ASCII, non-BMP characters represented by surrogate
pairs, CRLF, CR, LF, NEL, line separator, and paragraph separator without
transcoding or newline replacement.

The physical line range is one-based and inclusive. It names the lines touched
by the exact span and exists for display and correlation only. It is not an
alternative slicing coordinate.

The lexer and declaration index retain the positions needed to issue spans.
The public result does not expose lexer tokens or require another grammar
scanner. `DocCommentParser` is not used: it searches by name and owns neither
declaration selection nor exact source-part production.

## Failure and non-claims

Invalid physical coordinates use
`InvalidMemberTextCoordinatesException`, matching the existing operation.
Unsupported, ambiguous, or unvouched selection returns `null`. A supported
bodyless declaration returns parts with an absent body, empty documentation
and attribute collections when applicable, and otherwise complete exact
parts. The operation does not manufacture empty success for selection failure.

This owner does not claim:

- exact Metadata-to-source correspondence;
- parsed or rendered documentation;
- source acquisition, checksum, URL, or SourceLink policy;
- decompiled-source correspondence;
- semantic attribute interpretation;
- host serialization, section selection, printing, or flags; or
- support for declarations the existing index cannot vouch for.

SourceHouse may compose these lexical parts with exact PDB mapping and
checksum-verified source under the separately owned
[PDB-mapped declaration correspondence](source-house-pdb-mapped-declaration-correspondence.md).

## Evidence

The motivating real source is `richlander/dotnet-inspect` at
`bffd209a896d0380193e8d5f0f3a8beac3770d0f`, specifically
`src/CSharpText.MemberSlicing/MemberTextSlicer.cs`, selected through its actual
`ExtractMemberText` declaration rather than a synthetic-only fixture.

PR-fast Release tests gate:

- exact original text, UTF-16 offsets, and physical lines for `///` and
  `/** ... */` documentation;
- same-line and multiline attribute lists, multiple lists, and intervening
  ordinary comments and blank lines;
- multiline signatures, constructor initializers, brace bodies, expression
  bodies, accessor-owned properties and events, and bodyless declarations;
- unique same-line parent boundaries and refused same-line siblings;
- conditional projection, crossed-boundary refusal, and `#line` refusal;
- CRLF and every supported Unicode line terminator;
- surrogate-pair columns and lengths;
- the production `MemberTextSlicer.ExtractMemberText` declaration; and
- unchanged `ExtractMemberText` outputs and refusal behavior through its
  existing suite.

No new whole-assembly exhaustive gate is required. Existing corpus gates remain
unchanged and continue to classify their established cost.
