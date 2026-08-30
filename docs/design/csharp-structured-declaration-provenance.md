# C# declaration result and receipt protocol

Status: proposed design for #5182. This document owns the shared CSharp
protocol for declaration results, text currencies, occurrence receipts, and
diagnostic containment. It does not adopt any type or member declaration form.

## Claim

A CSharp declaration producer must report one authoritative four-arm outcome
and enough typed, occurrence-scoped evidence to prove how every semantic input
was admitted to that outcome. A consumer must be able to compose the result
without rescanning text or treating presentation as identity.

The protocol is intentionally smaller than a complete declaration model.
Type-form adoption belongs to #5181, member-form adoption belongs to #5183, and
aggregate source-unit composition belongs to #5142.

## Why this is a protocol

The same bytes can have different semantic roles and different trust
properties. For example, `class` can be an identifier requiring escaping, a
fragment of contained C# syntax, or inert compatibility text. A declaration
pipeline that accepts all three as `string` cannot show which treatment was
applied and cannot safely infer the answer from the final output.

The protocol therefore separates:

- Metadata facts and their typed identities;
- CSharp admission and representability decisions;
- contained C# text from inert compatibility text;
- occurrence evidence from output coordinates;
- authoritative outcomes from subordinate diagnostics.

This boundary prevents a consumer from upgrading text by guessing how it was
produced.

## Protocol-only example

Consider an adoption plan with two occurrences whose source bytes are both
`class`:

```text
generic-parameter[0].identifier = metadata identifier "class"
parameter[1].default-value      = typed string scalar "class"
```

The first occurrence can produce contained C# text `@class`. The second cannot
be treated as an identifier merely because its bytes have the same spelling; it
produces the C# string literal `"class"`. A `Representable` result records
separate receipt entries:

```text
generic-parameter[0].identifier -> IdentifierEscaped
parameter[1].default-value      -> TypedScalarRendered
```

The receipt is not a source map. Its stable semantic paths distinguish equal
bytes and repeated occurrences without depending on whitespace or output
offsets. This example exercises only the shared protocol; #5181 and #5183 own
the real plans for their declaration forms.

## Trust boundary

Inspected metadata, decoded names, source-derived values, and compatibility
fragments are untrusted input. Metadata owns the facts and identities it can
prove. CSharp owns whether those facts have a faithful C# representation and
how representable facts are spelled.

The protocol must not:

- load an inspected assembly;
- accept reflection types as declaration identity;
- ask Metadata to decide C# legality or spelling;
- infer semantic identity from display text;
- parse final declaration text to reconstruct provenance;
- turn a decode or admission failure into an empty successful result.

Product paths remain SRM-only, NativeAOT-friendly, and Roslyn-free.

## Shared currencies

The CSharp layer uses four distinct currencies.

### `CSharpDeclarationText`

Contained text that is safe to place in the declaration context stated by the
result. Construction is internal to CSharp admission and rendering. This type
does not by itself prove that a whole declaration is representable.

### `CSharpCompatibilityText`

Inert text carried only for diagnostics or fallback presentation. It cannot be
concatenated into `CSharpDeclarationText`, reparsed to manufacture semantic
facts, or promoted by rescanning.

### `CSharpDeclarationReceipt`

Immutable occurrence evidence for a complete declaration attempt. Each entry
contains:

- a closed declaration slot kind;
- a stable semantic role/ordinal path;
- a closed value class describing the admitted input;
- an admission disposition;
- the typed evidence needed by that disposition.

Paths identify semantic occurrences, not substrings. Repeated parameters,
generic parameters, constraints, attributes, or other repeated roles remain
independently observable even when their values and rendered bytes are equal.
Receipts carry no output offsets and cannot be reconstructed from rendered
text.

### `CSharpDeclarationResult`

The authoritative result of one declaration attempt. It has exactly one of the
following arms.

| Arm | Meaning | Permitted payload |
| --- | --- | --- |
| `Representable` | CSharp proved a complete faithful declaration | contained declaration text, exact namespace requirements, complete non-opaque receipt |
| `FallbackRequired` | Metadata facts are complete but C# cannot faithfully represent them | complete typed fallback facts, optional subordinate compatibility text, optional receipt containing opaque entries |
| `Degraded` | Facts or admission evidence are incomplete | exact status and bounded nonauthoritative evidence; no declaration text |
| `Unavailable` | Metadata could not produce the declaration facts | the Metadata failure and bounded diagnostics; no declaration text |

Only an actual Metadata declaration failure produces `Unavailable`.
Unsupported C# spelling is `FallbackRequired`, not `Unavailable`. Incomplete or
ambiguous evidence is `Degraded`, not a plausible declaration.

## Closed value classes

Every admitted occurrence has one protocol-owned value class. The initial
taxonomy distinguishes:

- metadata identifiers;
- metadata type identities and signatures;
- typed scalar and enum values;
- declaration keywords and punctuation chosen by CSharp;
- contained C# fragments returned by another admitted result;
- inert compatibility fragments;
- opaque evidence retained only for fallback or degradation.

Adoption designs may propose a new class only when none of these classes can
state its trust and composition rules. Adding a class changes the shared
protocol and must add positive and close-negative gate specimens. Adopters
must not introduce parallel string wrappers that bypass this taxonomy.

## Admission dispositions

A receipt entry uses a closed disposition that explains what CSharp did with
one occurrence:

- `Rendered`: the typed input was faithfully rendered as contained C#;
- `Escaped`: an identifier was faithfully represented with C# escaping;
- `Synthesized`: CSharp chose syntax from typed facts rather than copied text;
- `Composed`: contained text and its receipt were imported from a subordinate
  representable result;
- `CompatibilityContained`: inert evidence was retained outside declaration
  text;
- `Opaque`: CSharp cannot establish the occurrence's C# meaning.

`Opaque` is incompatible with `Representable`. Compatibility containment does
not make an occurrence representable. A subordinate result is composed by its
result arm and receipt, never by accepting its text alone.

## Result invariants

### Representable

A `Representable` result proves all of the following:

- declaration text is complete and contained;
- every planned semantic occurrence has exactly one faithfully rendered,
  escaped, synthesized, or composed receipt entry;
- no receipt entry exists without a planned occurrence;
- every subordinate result was itself `Representable`;
- namespace requirements are exact for the selected qualification policy;
- diagnostics did not substitute for missing facts.

Qualification is part of the declaration request. A fully qualified result may
render more text while requiring fewer namespaces; consumers must use the
returned requirement set rather than deriving imports from spelling.

### Fallback required

`FallbackRequired` preserves complete typed declaration facts. Compatibility
text and opaque receipt entries may explain why no faithful C# declaration
exists, but neither replaces those facts. A consumer can render a fallback
lens without mistaking it for compilable source.

If a subordinate result requires fallback, the enclosing declaration cannot be
`Representable`. The enclosing producer retains the subordinate typed facts
and maps its outcome according to the completeness of its own evidence.

### Degraded

`Degraded` names the exact incomplete or ambiguous evidence and bounds any
nonauthoritative diagnostic payload. It never carries
`CSharpDeclarationText`. Later composition cannot upgrade it by selecting the
plausible parts.

### Unavailable

`Unavailable` retains the Metadata failure that prevented declaration facts
from being produced. CSharp admission failure cannot manufacture this arm.

## Diagnostic containment

Diagnostics are subordinate to the authoritative arm. They use typed facts or
`CSharpCompatibilityText`; they never masquerade as declaration text.

A diagnostic must identify the affected semantic path when one exists.
Diagnostic rendering may abbreviate evidence for presentation, but the result
retains the exact status and typed facts needed to distinguish decode failure,
missing evidence, C# unrepresentability, and successful admission.

Failures remain visible. No arm may encode failure as empty text, an empty
receipt, or an empty fallback-fact collection.

## Composition

Composition follows the result algebra rather than string concatenation.

- `Representable` children contribute contained text, namespace requirements,
  and receipt entries under a prefixed semantic path.
- `FallbackRequired` children prevent a representable parent and contribute
  complete typed fallback facts.
- `Degraded` children prevent a representable or complete-fallback parent
  unless the parent has independent authoritative evidence for the omitted
  facts.
- `Unavailable` children retain their Metadata failure; a parent cannot
  relabel it as C# unrepresentability.

Receipt path prefixing is structural and immutable. Composition rejects
duplicate paths, missing planned paths, mismatched value classes, opaque
entries in a representable result, or namespace requirements detached from the
qualification request.

## Adoption contract

Each declaration-form adoption has a focused owning design and a registered
plan. The plan declares:

- its form identity;
- its closed set of semantic slot kinds and repeatable role paths;
- the typed input class accepted by each slot;
- the subordinate result boundaries it composes;
- how incomplete source evidence maps to the four result arms;
- the positive and close-negative fixtures that gate the mapping.

An adopter must produce its plan from typed facts before rendering. The
renderer returns a result and receipt against that plan. Neither the adopter
nor a later consumer may fill receipt gaps by scanning output text.

Incomplete prerequisite evidence remains on the legacy path outside this
result API. It does not receive a provisional result arm merely to make
migration appear complete. The adopting issue names the evidence dependency
and adds the form only when all four arms can be produced authoritatively.

The shared registry is closed over adopted forms. Registration requires an
owning design, a plan factory, and named outcome/receipt gates. This keeps
"supported by the protocol" distinct from "adopted by a declaration form."

## Adjacent owners

This document references rather than redefines adjacent contracts:

- `member-inspection-planning-and-metadata-projection.md` owns the
  authoritative four-arm representability outcome and complete fallback
  facts.
- `inspection-layers.md` owns layer and consumer boundaries.
- `type-member-api-representation.md` owns the repository currency map.
- #5134 owns final scalar containment in CSharpText.
- #5142 owns complete source units, declaration bodies, initializers, nesting,
  and aggregate result propagation.
- #5181 owns type-declaration adoption.
- #5183 owns member-declaration adoption.

Metadata evidence improvements required by an adopter remain owned by their
focused Metadata issues. They do not expand this protocol into a declaration
form or metadata-source inventory.

## Required gates

The implementation is not complete until named tests enforce the protocol
itself:

- `CSharpDeclarationProtocolTests.ResultArmsPreserveAuthoritativeOutcome`
  proves the four arms cannot be interchanged by missing payloads.
- `CSharpDeclarationProtocolTests.RepresentableRequiresCompleteReceipt`
  derives required paths from the plan and rejects missing, duplicate, extra,
  or opaque entries.
- `CSharpDeclarationProtocolTests.EqualValuesRemainDistinctOccurrences` proves
  repeated equal values retain separate semantic paths.
- `CSharpDeclarationProtocolTests.CompatibilityTextCannotBecomeDeclarationText`
  proves inert text has no promotion or concatenation path.
- `CSharpDeclarationProtocolTests.CompositionPreservesChildOutcomes` proves
  child fallback, degradation, and unavailability cannot be hidden by a
  parent.
- `CSharpDeclarationProtocolTests.QualificationOwnsNamespaceRequirements`
  proves requirements come from the qualified request/result, not rescanning.
- `CSharpDeclarationProtocolTests.DiagnosticsCannotSubstituteForFacts` proves
  diagnostics cannot satisfy a plan or produce success-shaped empty payloads.
- `CSharpDeclarationAdoptionTests.RegistryMatchesOwnedPlansAndGates` derives
  the adopted form set from the registry and asserts that every entry names an
  owner, plan factory, and outcome/receipt gate.

Each adopting design adds its own compiled or real-artifact canaries for every
result arm plus close negative cases. Core protocol tests do not stand in for
form adoption evidence.

These properties are **unverified** until the named gates exist and run in the
Release test suite.

## Non-goals

This design does not:

- enumerate type or member declaration slots;
- define metadata extraction for any declaration form;
- make every declaration form immediately representable;
- define source-unit layout, bodies, or initializers;
- replace complete fallback facts with printable text;
- provide source mapping or output-coordinate tracking;
- promise round-trip C# for opaque or degraded evidence.
