# C# declaration-attached authored documentation

## Status and ownership

This document is the normative owner for the model-free CSharpText operation
tracked by
[#6583](https://github.com/richlander/dotnet-inspect/issues/6583). It is
DocumentationHouse production-adoption slice 15 under
[#6579](https://github.com/richlander/dotnet-inspect/issues/6579).
The CSharpText operation, SourceHouse and DocumentationHouse integration, and
CLI and Inspect Web adoption are implemented through slices 16-21. Slice 22
retires the superseded name-searching parser.

The one claim is:

> Given one decoded C# source buffer, one caller-supplied exact physical
> declaration span in that buffer, optional physical active-branch evidence,
> and finite work, CSharpText returns the documentation comment attached to
> that uniquely recovered declaration as bounded parsed fields and exact local
> spans, or one typed non-success. It does not search by name or claim that the
> declaration corresponds to Metadata, a PDB row, a SourceHouse result, or any
> other source buffer.

CSharpText owns:

- the model-free request, limits, result, work, and local-span evidence;
- matching the supplied raw UTF-16 span to one recovered physical C#
  declaration;
- conditional projection from caller-issued physical evidence;
- documentation-comment lexical recognition and declaration attachment;
- source-form exterior normalization and bounded XML-fragment parsing;
- the distinction between attached documentation, authoritative local
  absence, no matching declaration, ambiguity, uncertainty, malformed
  documentation, and incomplete work; and
- detached parsed values compatible with CSharpText's existing
  `XmlDocumentationEntry` field grammar.

SourceHouse owns source acquisition, decoding, source identity, checksums,
mapping, and correspondence between one exact Metadata target and the
caller-supplied physical declaration. Metadata owns Metadata identity and
compiler XML-documentation identity. DocumentationHouse owns channel and field
settlement. Callers own the authority and provenance of active-branch evidence.
This design does not redefine those owners.

## Product question

**What documentation is attached to this exact physical C# declaration?**

```text
ReadAttachedDocumentation(
    decoded C# source,
    exact raw declaration span,
    optional physical active-line evidence,
    finite limits)
  -> Available
   | Absent
   | NoDeclaration
   | Ambiguous
   | Uncertain
   | Malformed
   | Incomplete
```

`Available` and `Absent` are local CSharpText facts about one supplied source
buffer. Neither proves that the declaration produced a requested Metadata
target. A SourceHouse adapter may use them only after the exact target-to-source
mapping, checksum verification, and declaration selection owned by
[SourceHouse PDB-mapped declaration correspondence](source-house-pdb-mapped-declaration-correspondence.md).

## Demo and motivating asset

The real source subject is
[`MemberTextSlicer.ExtractMemberText`](https://github.com/richlander/dotnet-inspect/blob/f0659c64a0627d1923b80bf6a600d929d277172c/src/CSharpText.MemberSlicing/MemberTextSlicer.cs#L21-L111).
Its declaration carries a multi-paragraph `///` comment and has neighboring
members in the same type. The operation receives its exact physical
declaration span, not the strings `MemberTextSlicer` or `ExtractMemberText`:

```text
source + exact declaration span
  -> Available
     declaration: Method
     documentation spans: [ ... ]
     summary: "Locates the declaration containing the selection range..."
```

Moving a same-named overload before it does not change the selected
declaration. Supplying the overload's span selects the overload. Supplying a
span that covers neither declaration returns `NoDeclaration`; CSharpText does
not recover by name, proximity, nesting, or signature display.

The implementation slice preserves this real method as a production-shaped
Release canary. Focused fixtures provide the pathological neighbors, malformed
fragments, and finite-work boundaries that the real file should not be changed
to manufacture.

## Request and local correspondence

The request contains:

- the complete decoded C# source string;
- one non-negative, overflow-safe, zero-based half-open span measured in UTF-16
  code units against that exact string;
- optional positive, sorted, distinct one-based physical active-line
  coordinates against the same string; and
- positive finite limits for source scanning, declaration recovery,
  documentation isolation, XML parsing, and retained output.

Request construction rejects null source, negative or overflowing coordinates,
out-of-bounds spans, and non-positive limits. While source and line counting
remain inside those configured bounds, it also rejects non-positive, unsorted,
duplicate, or out-of-bounds active lines. Otherwise the operation returns the
applicable typed `Incomplete` before enumerating or validating that optional
evidence. Invalid admitted inputs are caller contract violations, not
source-analysis outcomes.

The operation matches the supplied span to the complete raw syntax extent of a
recovered declaration. Documentation leading trivia is outside that extent;
attributes are inside it. A match is exact rather than containment-based, so a
nested member cannot satisfy its enclosing type's span and an enclosing type
cannot satisfy its member's span.

The matching declaration reports CSharpText's existing `DeclarationKind`.
CSharpText does not consume or interpret Roslyn syntax-kind names. A later
adapter may compare independently issued syntax evidence without moving that
association into this operation.

The initial profile admits declaration forms for which CSharpText can recover
one exact raw extent. Types, delegates, methods, constructors, destructors,
operators, properties, indexers, events, fields, and enum members are eligible
when that condition holds. Accessors and other syntax that the declaration
index does not model return `NoDeclaration`; the operation never substitutes
the containing property or event. Several rows sharing one raw declaration
extent, such as unsupported multi-declarator shapes, return `Ambiguous` unless
the index can issue one exact row from the supplied span alone.

A physical partial-type part is one local declaration. CSharpText may return
the comment attached to that part, but does not combine comments across parts
or claim symbol-level documentation. DocumentationHouse's initial authored
channel separately requires SourceHouse correspondence for a supported exact
TypeDef or MethodDef.

## Conditional and line-directive evidence

CSharpText does not evaluate preprocessor expressions or accept symbol names as
authority. The exact declaration span selects a conditional branch only when
the vouched declaration lies wholly within that one branch. Optional active
physical lines may select additional complete conditional groups only when
they identify exactly one branch in each group. Unselected branch content is
projected away while preserving every original physical line and UTF-16
coordinate.

Projection must recover the same exact declaration extent and a vouched
documentation association in the original buffer. Zero or multiple evidenced
branches, an incomplete conditional group, a group crossing the declaration
boundary, or branch-dependent documentation that the supplied evidence does
not resolve produces `Uncertain`. CSharpText never chooses the first branch or
the branch nearest the declaration.

`#line` changes logical compiler and PDB coordinates, not physical UTF-16
coordinates. An exact-span request therefore does not fail merely because the
file contains `#line`. Active lines are accepted only as physical coordinates
already vouched by the caller; this operation does not convert logical lines
or infer that PDB lines are physical. A future line-selection profile would
need its own explicit `#line` refusal and does not enter this contract.

## Documentation attachment

The operation recognizes the C# documentation forms:

- `///` when a fourth slash does not follow; and
- `/**` when a fourth slash, a second asterisk, or an immediate closing slash
  does not turn the token into another comment form.

Thus `////`, `/***`, and `/**/` are ordinary comments, not documentation.
Adjacent `///` lines form one source fragment. Delimited documentation and
single-line documentation remain separately bounded source fragments and are
combined in source order only after each exterior is normalized.

Attachment follows the conventional Roslyn declaration-leading-trivia rule:

1. The attachment boundary is the declaration's first token, including its
   first attribute list when attributes are present.
2. Moving backward from that boundary, whitespace, line endings, ordinary
   comments, and active directives may precede the nearest documentation
   fragment without detaching it.
3. After the nearest documentation fragment is found, whitespace and line
   endings may separate additional documentation fragments. An ordinary
   comment or directive separates any older documentation from the
   declaration.
4. Documentation written after the first attribute list is not attached to
   that declaration.
5. Documentation in an inactive projected branch does not exist for this
   operation. Unresolved branch choice produces `Uncertain`, not a comment
   selected from the lexical fallback.

This preserves the compiler's established compatibility behavior where an
ordinary comment or active directive may sit between the nearest documentation
fragment and the declaration. It deliberately does not interpret every
preceding `///` block as documentation for the declaration.

`Absent` is authoritative only after one exact declaration and its attachment
boundary are vouched and no attached documentation fragment remains. A
documentation-looking token elsewhere in the file, after an attribute, in an
inactive branch, or separated as an older fragment does not weaken that local
absence.

## Parsed documentation

Each attached source fragment has its C# exterior removed under the language's
single-line or delimited-documentation whitespace rules. The normalized
fragments are parsed as one inert XML fragment under a synthetic root. DTDs
and external resolution are prohibited. Parsing performs no filesystem,
network, include, or source acquisition.

`Available` carries:

- the exact matched declaration span and `DeclarationKind`;
- every exact attached source span in source order;
- a detached `XmlDocumentationEntry` using the same summary, remarks, returns,
  parameters, exceptions, samples, reference-text normalization, and sample
  path normalization as compiler XML reading;
- explicit flags for unexpanded `<include>` and `<inheritdoc>` elements; and
- finite-work evidence.

An attached, well-formed comment with none of the supported fields is still
`Available` with an empty entry. `Absent` means no attached documentation, not
"no recognized field."

Malformed XML, a prohibited DTD, an unterminated documentation comment, or any
other source-fragment parse failure produces `Malformed`. No partial field and
no plain-text fallback is published. `<include>` and `<inheritdoc>` remain
unexpanded limitations beside otherwise parsed fields; CSharpText does not
fetch, bind, inherit, or silently discard them. `cref` values remain text and
are not symbol-bound.

## Outcomes and precedence

Every valid request returns one closed outcome:

- **Available** retains the exact declaration and documentation spans, parsed
  entry, unresolved-element flags, and completed work.
- **Absent** retains the exact declaration span, kind, and completed work that
  establishes no attached documentation.
- **NoDeclaration** means no recovered declaration has the supplied exact raw
  extent and no known lexical uncertainty prevents that conclusion.
- **Ambiguous** retains the locally matching declarations when the span alone
  cannot select one.
- **Uncertain** names the lexical, declaration, conditional, or documentation-
  attachment reason CSharpText cannot vouch for an otherwise plausible answer.
- **Malformed** retains the attached documentation spans and a closed safe
  parse reason, without source-authored diagnostic text.
- **Incomplete** names the exhausted work boundary and completed work counts.

Configured-bound exhaustion takes precedence over a later semantic answer.
After completed scanning, uncertainty precedes `NoDeclaration`, ambiguity
precedes attachment parsing, and malformed attached documentation precedes
`Available`. No outcome contains an alternative success selected before the
operation reached its terminal state.

Results are detached values. They retain no source buffer, lexer, declaration
index, XML reader, stream, path, SourceHouse capability, or reopening
authority. Exact local spans address only the source string used by that
invocation; equal text or equal spans in another string do not transfer
identity.

## Finite work

The operation is a bounded whole-source scan because exact declaration
selection and leading-trivia attachment depend on lexical structure before the
supplied span. Its plan limits:

- source characters and physical lines examined;
- lexical tokens retained;
- declarations retained or compared;
- attached documentation characters;
- XML depth and nodes;
- repeated parameters, exceptions, and samples; and
- retained field text.

CSharpText may impose lower absolute safety ceilings in addition to
caller-selected limits. Every exhausted boundary returns `Incomplete` with the
boundary and observed count; it does not throw a parser-limit exception,
report authoritative absence, or return fields retained before exhaustion.
Malformed source or XML is not relabeled as incomplete unless a configured
bound actually prevents the corresponding validation from completing.

There is no runtime-speed claim. The implementation gate measures exact work
and threshold behavior rather than using elapsed time as a proxy.

## Convention and deliberate divergence

The analogues inform this owner; they are not architectural authority, and no
external code is transferred.

| Evidence | Conventional behavior | This contract |
| --- | --- | --- |
| [C# documentation-comment specification](https://github.com/dotnet/csharpstandard/blob/1397ed398812d5bbc11018ff7af613f9d73af2d0/standard/documentation-comments.md) | Defines exact line and delimited forms, exterior normalization, and declaration-leading placement. | Adopts those lexical forms and normalization. |
| [Roslyn source documentation selection](https://github.com/dotnet/roslyn/blob/5a9f1b4bb88ec57c776fd9be0c8693eafb375b10/src/Compilers/CSharp/Portable/DocumentationComments/SourceDocumentationCommentUtils.cs#L40-L116) | Starts from one declaring syntax node and selects documentation from its leading trivia, including legacy separator behavior. | Adopts declaration-first attachment and separator behavior without a symbol model. |
| [Roslyn documentation compilation](https://github.com/dotnet/roslyn/blob/5a9f1b4bb88ec57c776fd9be0c8693eafb375b10/src/Compilers/CSharp/Portable/Compiler/DocumentationCommentCompiler.cs#L820-L860) | Uses declaring syntax references and rejects malformed documentation from compiler XML output. | Uses a caller-supplied exact physical span and returns typed `Malformed`; it never searches by name. |
| [DocFX Roslyn bridge](https://github.com/dotnet/docfx/blob/6cd3acee927f565b5cca52116c33a9cb45fd6aa2/src/Docfx.Dotnet/ExtensionMethods/ISymbolExtensions.cs#L36-L111) | Delegates declaration association and documentation production to Roslyn. | Confirms that text-name search is not a conventional substitute for declaration identity. |
| Existing `DeclarationIndex` and `XmlDocumentationReader` | Supply Roslyn-oracle-tested conservative declaration spans and bounded parsed documentation fields. | Reuses those owner substrates and adds only the anchored source-comment operation. |

Roslyn and DocFX are MIT-licensed; their behavior is evidence only.

The deliberate divergences are:

- the product remains model-free and Roslyn-free in its existing CSharpText
  boundary, so it returns explicit uncertainty instead of compiler symbols;
- the operation is finitely bounded and returns `Incomplete`, while Roslyn's
  local documentation parser does not expose equivalent per-operation limits;
- the operation does not bind `cref`, expand `<include>` or `<inheritdoc>`, or
  combine partial-symbol documentation; and
- exact physical coordinates remain valid in a file containing `#line`, while
  logical-line correlation remains outside the contract.

## Required Release gates

Slice 16 must gate:

- the real `MemberTextSlicer.ExtractMemberText` declaration and its attached
  multi-paragraph documentation through the production operation;
- same-named overloads, neighboring and nested declarations, constructors,
  operators, properties, accessors, and physical partial-type parts without
  name or proximity fallback;
- exact raw-span equality, attribute inclusion, documentation before
  attributes, documentation after attributes, ordinary-comment separators,
  blank lines, mixed `///` and `/** */` forms, and the `////`, `/***`, and
  `/**/` negatives;
- caller-selected conditional branches, unresolved and contradictory branch
  evidence, branch-dependent attachment, and an exact physical-span case in a
  file containing `#line`;
- authoritative `Absent` only for a uniquely vouched declaration;
- distinct `NoDeclaration`, `Ambiguous`, and `Uncertain` outcomes;
- malformed and unterminated comments, prohibited DTDs, and no plain-text or
  partial-field fallback;
- bounded source, token, declaration, comment, XML depth/node, repeated-field,
  and retained-text thresholds at the exact limit and one unit beyond it;
- explicit `<include>` and `<inheritdoc>` limitations; and
- complete detachment of every terminal result.

The declaration and attachment cases use Roslyn only as an independent test
oracle over the unchanged source fixture. The product operation remains in
CSharpText and consumes no Roslyn type. The gate compares exact declaration
and documentation spans plus parsed fields; matching a summary string alone is
not sufficient evidence.

This synchronous, stateless operation introduces no lifecycle, concurrency, or
distributed-state protocol. A TLA+ model would duplicate the input/output
contract rather than test an interaction.

## Production adoption and retirement

The DocumentationHouse production-adoption plan is complete:

1. slice 16 implemented the owner-issued CSharpText operation;
2. slice 17 added the SourceHouse-to-DocumentationHouse deferred operation;
3. slice 18 added the authored channel and field settlement;
4. slice 19 published authored evidence through Queries;
5. slices 20 and 21 adopted it in Inspect Web and the CLI; and
6. slice 22 deleted the name-searching `DocCommentParser`.

The retired parser's name search, unbounded whole-file sample scan, broad
exception fallback, and plain-text malformed-comment success are not
compatibility requirements for the exact declaration-attached operation.

## Non-claims

This contract does not:

- establish Metadata, Library, PDB, SourceLink, SourceHouse, build, or
  cross-buffer correspondence;
- acquire, decode, authenticate, or select source;
- accept a type name, member name, signature, XML documentation ID, path, URL,
  or line proximity as declaration identity;
- model symbols, overload resolution, accessors, partial-symbol aggregation,
  compiler-generated members, or semantic binding;
- evaluate conditional expressions or infer active symbols;
- convert PDB logical lines into physical coordinates;
- expand `<include>` or `<inheritdoc>` or bind `cref`;
- settle compiled and authored channels or documentation fields;
- define serialization, rendering, CLI, or Browser behavior.
