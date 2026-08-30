# C# declaration result and receipt protocol

Status: proposed design for #5182. This document owns the shared CSharp carrier
and containment protocol for declaration results, text currencies, occurrence
receipts, and diagnostics. It consumes the four-arm outcome and complete
fallback facts owned by
`member-inspection-planning-and-metadata-projection.md`; it does not redefine
that mapping or adopt any type or member declaration form.

## Claim

Given the owner-issued facts and outcome constraints, a CSharp declaration
producer must return one typed four-arm carrier and enough
occurrence-scoped evidence to prove how every attempted semantic input was
admitted. A consumer must be able to compose the result without rescanning text
or treating presentation as identity.

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
generic-parameter[0].identifier -> MetadataIdentifier / Escaped
parameter[1].default-value      -> TypedScalar / Rendered
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
result. Only the CSharp admission/rendering seam can construct it. This type
does not by itself prove that a whole declaration is representable.

### `CSharpCompatibilityText`

Inert text carried only for complete `FallbackRequired` presentation. It cannot
be concatenated into `CSharpDeclarationText`, reparsed to manufacture semantic
facts, promoted by rescanning, or attached to `Degraded` or `Unavailable`.

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

The typed carrier for one declaration attempt. Its arm consumes the
authoritative outcome unchanged; this protocol constrains the CSharp payload.

| Arm | Meaning | Permitted payload |
| --- | --- | --- |
| `Representable` | CSharp proved a complete faithful declaration | contained declaration text, exact namespace requirements, and a complete receipt containing only faithfully admitted dispositions |
| `FallbackRequired` | Metadata facts are complete but C# cannot faithfully represent them | stable reason, complete typed fallback facts, optional subordinate compatibility text, and an optional receipt that may contain compatibility or opaque entries |
| `Degraded` | The owner-issued outcome reports degraded input | exact input status, bounded nonauthoritative typed evidence, and an optional partial receipt for attempted occurrences; no declaration-text or compatibility-text payload |
| `Unavailable` | The owner-issued outcome reports Metadata declaration failure | one or more exact Metadata failures, bounded typed diagnostics, and an optional receipt for earlier attempted occurrences; no declaration-text or compatibility-text payload |

The Metadata status and failure mapping is forwarded unchanged from
`member-inspection-planning-and-metadata-projection.md`. CSharp does not infer
an arm from fact shape: an owner-issued degraded signature stays `Degraded`,
while an owner-issued rejected signature stays `Unavailable` even though both
may lack complete facts. Only complete owner-issued facts proceed to CSharp
admission, where faithful spelling produces `Representable` and unsupported
spelling, including an opaque admission, produces `FallbackRequired`. A
missing or invalid receipt entry is a result-construction failure, not another
result arm.

## Closed value classes

Every admitted occurrence has one protocol-owned value class. The initial
taxonomy distinguishes:

- `MetadataIdentifier`;
- `MetadataType`, including identities and signatures;
- `TypedScalar`, including enum values;
- `CSharpSyntaxChoice`, including keywords and punctuation chosen by CSharp;
- `ContainedFragment` returned by another admitted result;
- inert `CompatibilityFragment`;
- `OpaqueEvidence` retained only for fallback or degradation.

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
- `Composed`: a subordinate representable result was admitted and its receipt
  was imported;
- `CompatibilityContained`: inert evidence was retained outside declaration
  text;
- `Opaque`: CSharp cannot establish the occurrence's C# meaning.

`Opaque` is incompatible with `Representable`. Compatibility containment does
not make an occurrence representable. A subordinate result is composed by its
result arm and receipt, never by accepting its text alone.

The value class and disposition are separate closed axes. Each disposition
accepts only the value classes and typed evidence compatible with its meaning:
for example, `Escaped` requires `MetadataIdentifier`, `Composed` requires a
successfully admitted `ContainedFragment` and carries its child receipt, and
`CompatibilityContained` requires `CompatibilityFragment`. A `Composed` entry
attests admission but does not copy or retain the fragment bytes; only a
representable parent's declaration-text payload uses them. A combined
class/disposition enum is not an equivalent receipt.

## Result invariants

### Representable

A `Representable` result proves all of the following:

- declaration text is complete and contained;
- every planned semantic occurrence has exactly one faithfully rendered,
  escaped, synthesized, or composed receipt entry;
- no receipt entry exists without a planned occurrence;
- every subordinate result selected for composition was itself
  `Representable`;
- namespace requirements are exact for the selected qualification policy;
- diagnostics did not substitute for missing facts.

Qualification is part of the declaration request. A fully qualified result may
render more text while requiring fewer namespaces; consumers must use the
returned requirement set rather than deriving imports from spelling.

### Fallback required

`FallbackRequired` preserves a stable reason and complete typed declaration
facts. Compatibility text and opaque receipt entries may explain why no
faithful C# declaration exists, but neither replaces those required payloads.
A consumer can render a fallback lens without mistaking it for compilable
source.

A receipt is absent only when no occurrence was attempted anywhere in the
selected subtree. Once admission attempts an occurrence, the result carries a
receipt covering every attempted occurrence exactly once, even when no
compatibility text is retained. Every compatibility fragment is attached to
its semantic path through a `CompatibilityContained` entry; equal fragments
cannot collapse occurrences. Unattempted slots remain authoritative in the
complete fallback facts rather than acquiring invented receipt entries.

If a subordinate result requires fallback, the enclosing declaration cannot be
`Representable`. The enclosing producer retains the subordinate typed facts
and combines its own owner-issued outcome with the child under the composition
rule below.

### Degraded

`Degraded` names the exact incomplete or ambiguous input status and bounds any
nonauthoritative typed evidence. If admission began before the owner-issued
degraded status was reached, a partial receipt preserves each attempted
occurrence exactly once. It never carries a declaration-text or
compatibility-text payload. Later composition cannot upgrade it by selecting
the plausible parts.

### Unavailable

`Unavailable` retains every Metadata failure that prevented declaration facts
from being produced. If another occurrence was attempted before that
owner-issued failure was reached, its receipt remains attached without
retaining a declaration-text payload. CSharp admission failure cannot
manufacture this arm.

## Diagnostic containment

Diagnostics are subordinate to the authoritative arm. `Degraded` and
`Unavailable` diagnostics use closed reasons, semantic paths, and bounded typed
evidence; they never carry plausible C# text. Only complete
`FallbackRequired` may carry `CSharpCompatibilityText`.

A diagnostic must identify the affected semantic path when one exists.
Diagnostic rendering may abbreviate evidence for presentation, but the result
retains the exact status and typed facts needed to distinguish decode failure,
missing evidence, C# unrepresentability, and successful admission.

Failures remain visible. No arm may encode failure as empty text, an empty
receipt, or an empty fallback-fact collection.

## Composition

Composition follows the result algebra rather than string concatenation.

- The parent first has its own arm: its owner-issued `Unavailable` or
  `Degraded` outcome, or the `Representable`/`FallbackRequired` result of
  admitting complete owner-issued facts.
- The final arm is the maximum of that arm and every selected child's arm under
  the precedence `Unavailable` > `Degraded` > `FallbackRequired` >
  `Representable`.
- An `Unavailable` parent retains every causative Metadata failure; a
  `Degraded` parent retains every exact incomplete input status; and a
  `FallbackRequired` parent has complete facts containing every selected
  child's complete fallback facts and stable reason.
- A `Representable` parent therefore requires its own representable admission
  and all-`Representable` selected children.

This precedence is total for every mixture of the parent's own arm and child
arms and cannot be overridden by selecting the most plausible payload. If the
parent renders an occurrence from independently owner-issued facts instead of
consuming a child result, that result is not a selected subordinate and
contributes nothing to the parent.

Receipt path prefixing is structural and immutable. Composition rejects
duplicate paths, missing planned paths, mismatched value classes, opaque
or compatibility dispositions in a representable result, or namespace
requirements detached from the qualification request. Every selected child
receipt is propagated under its prefixed path. If any occurrence was attempted
in the selected subtree, the parent has a receipt even when its final arm is
not representable; completed `Composed` entries retain child receipts but not
child fragment bytes.

## Adoption contract

Each declaration-form adoption has a focused owning design and a registered
plan. The plan declares:

- its form identity;
- its closed set of semantic slot kinds and repeatable role paths;
- the typed input class accepted by each slot;
- the owner-issued status and fact payload it consumes;
- the subordinate result boundaries it composes;
- the positive and close-negative fixtures that gate faithful status forwarding
  and payload construction.

An adopter must produce its plan from typed facts before rendering. The
renderer returns a result and receipt against that plan. Neither the adopter
nor a later consumer may fill receipt gaps by scanning output text.

Incomplete prerequisite evidence remains on the legacy path outside this
result API. It does not receive a provisional result arm merely to make
migration appear complete. The adopting issue names the evidence dependency
and adds the form only when every owner-issued outcome can be carried
authoritatively and complete facts can be admitted as representable or
fallback.

The shared registry is closed over adopted forms. Registration requires an
owning design and a plan factory. Every result-producing declaration form
appears exactly once; the expected set comes from the independently discovered
result-producing entry points, not from the registry itself. A separate
executable adoption-gate catalog has the same form set and invokes each form's
outcome/receipt gates in the Release suite. This keeps "supported by the
protocol" distinct from "adopted by a declaration form" and makes missing,
stale, or named-but-nonexistent gates fail.

## Adjacent owners

This document references rather than redefines adjacent contracts:

- `member-inspection-planning-and-metadata-projection.md` owns the arm meanings,
  Metadata status/failure mapping, and complete fallback fact construction.
  This document owns the CSharp carrier, payload containment, admission
  receipt, and composition contract that realizes that outcome.
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

- `CSharpDeclarationProtocolTests.ResultArmsForwardAuthoritativeOutcome`
  proves owner-issued degraded and unavailable outcomes are forwarded without
  fact-shape inference, while complete facts alone enter CSharp admission; all
  arms require their stable reason, facts, statuses, failures, and permitted
  text/receipt payloads.
- `CSharpDeclarationProtocolTests.RepresentableRequiresCompleteReceipt`
  derives required paths from the plan and rejects missing, duplicate, extra,
  opaque, or compatibility entries.
- `CSharpDeclarationProtocolTests.NonrepresentableReceiptsPreserveAttempts`
  proves every nonrepresentable arm omits its receipt only when zero
  occurrences were attempted, propagates selected child receipts by prefixed
  path, and retains neither composed fragment bytes nor invented occurrences.
- `CSharpDeclarationProtocolTests.EqualValuesRemainDistinctOccurrences` proves
  repeated equal values retain separate semantic paths.
- `CSharpDeclarationProtocolTests.ReceiptSchemaIsClosedAndOffsetFree` derives
  legal value-class/disposition/evidence combinations from the closed catalog
  and proves entries are immutable and expose no output coordinates.
- `CSharpDeclarationProtocolTests.DeclarationTextConstructionIsClosed` proves
  only the CSharp admission/rendering seam can construct contained declaration
  text.
- `CSharpDeclarationProtocolTests.CompatibilityTextCannotBecomeDeclarationText`
  proves inert text has no promotion or concatenation path and occurs only on
  complete fallback results.
- `CSharpDeclarationProtocolTests.CompositionIsTotalAndMonotone` covers the
  cross product of parent-own and selected-child arms and proves a parent
  cannot hide either source's fallback, degradation, or unavailability.
- `CSharpDeclarationProtocolTests.QualificationOwnsNamespaceRequirements`
  proves requirements come from the qualified request/result, not rescanning.
- `CSharpDeclarationProtocolTests.DiagnosticsCannotSubstituteForFacts` proves
  diagnostics cannot satisfy a plan or produce success-shaped empty payloads;
  it derives the closed reason/evidence schema and rejects free-form or
  plausible C# text members.
- `CSharpDeclarationAdoptionTests.RegistryMatchesResultProducingForms` derives
  the expected form set independently from result-producing entry points and
  asserts set equality with owned plans.
- `CSharpDeclarationAdoptionTests.RegisteredFormGatesExecute` asserts set
  equality with the executable adoption-gate catalog and invokes every
  registered form's outcome/receipt gates.

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
