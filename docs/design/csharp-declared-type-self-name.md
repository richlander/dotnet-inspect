# C# declared-type self-name admission

## Status

Implemented for issue #5367. The admission, shared-use, and atomic-refusal
properties are enforced by the named Release gates below. The composed
product-output compile/SRM endpoint is specified below and remains unverified.

This is the focused successor to superseded PR #5110. It does not inherit that
PR's proposed declaration-result, receipt, composition, or retention protocol.
Compiler-accurate type-declaration identifier admission is supplied by
`CSharpIdentifier.AdmitTypeDeclaration`, whose compiler and emitted-TypeDef
oracle landed with issue #5215.

## Responsibility

`ILInspector.CSharp` declared-type self-name admission owns one decision:
whether an exact Metadata type-definition leaf can supply the identifier used
by a C# type header and that type's constructor and finalizer heads.

The component issues one result per declared type. An admitted self-name is
reused by all three declaration positions; they do not independently format or
recover it from display text.

This is the bounded pattern-plus-first-adopter exception described by
[design scope](../design-scope.md#stage-implementation-after-locking-the-design).
The pattern and its first `CSharpTypePrinter` adoption lock together. No other
declaration occurrence adopts it in this effort.

## Concrete failure

`MetadataTypeDefinitionName` already distinguishes one literal leaf segment
`A+B` from two nested segments `A`, `B`. The current CSharp formatter preserves
that identity distinction by spelling the literal leaf as `A\+B`, then the
declaration writer treats those bytes as identifier syntax:

```text
metadata identity:  namespace="" segments=["A+B"]
current type name:  A\+B
current constructor: public A\+B()
current finalizer:   ~A\+B()
```

The repository SDK rejects the first declaration token:

```text
error CS1056: Unexpected character '\'
```

The current spelling is useful inert identity display, but it is not C# syntax.
The defect is therefore not insufficient escaping. One string is being asked
to serve both exact identity display and admitted declaration syntax.

Compiler-authored neighbors establish the positive side: `class` is a legal
metadata name represented by the C# identifier `@class`, and Unicode identifier
`Ω` is legal without substitution.

## Conventional baseline and deliberate divergence

The ILSpy family favors best-effort inspectability at its current
[`1522aee`](https://github.com/icsharpcode/ILSpy/commit/1522aee3022482ce9cb30d918770f356bd9dded8)
pin. Its AST identifier accepts arbitrary non-null text, while whole-project
export opts into an
[`EscapeInvalidIdentifiers`](https://github.com/icsharpcode/ILSpy/blob/1522aee3022482ce9cb30d918770f356bd9dded8/ICSharpCode.Decompiler/CSharp/Transforms/EscapeInvalidIdentifiers.cs#L31-L87)
transform that can rename invalid identifiers. Constructor and destructor
tokens are synchronized with the parent type
[during output](https://github.com/icsharpcode/ILSpy/blob/1522aee3022482ce9cb30d918770f356bd9dded8/ICSharpCode.Decompiler/CSharp/OutputVisitor/CSharpOutputVisitor.cs#L2340-L2395),
rather than issued from one earlier admitted value. Its error recovery also
preserves useful partial output.

This design adopts the conventional identity-preserving `@` keyword escape but
deliberately rejects identity-changing substitution. `CSharpTypePrinter`
produces compile-back source with one selected replacement range, so a source
unit containing a renamed declared type or only a partial requested batch
would be success-shaped but would not represent the requested exact artifact.
The stricter exact-name admission, shared prepared value, and source-less
batch refusal make that difference explicit. No implementation code is copied
from the surveyed decompilers.

## Immediate boundary

The admission input is:

- one exact `MetadataTypeDefinitionName`;
- the complete authoritative per-segment introduced generic-parameter count;
- the declared type's leaf generic-parameter list; and
- access to `CSharpIdentifier.AdmitTypeDeclaration`.

An arbitrary `ApiType.Name`, formatted type name, compatibility string, or
inert display value is not admission evidence. CSharpText continues to own the
model-free compiler-accurate identifier and declaration-keyword classification;
this component invokes that owner directly with the proven arity-free Metadata
leaf. It does not accept a caller-supplied lexical result that could have been
issued for different text.

The closed result is one of:

- **Admitted**: the exact Metadata identity and one identity-preserving legal
  C# identifier;
- **Unrepresentable**: the exact Metadata identity and a closed reason, with no
  C# identifier.

The result is not a declaration-wide outcome and carries no source fragment,
receipt, namespace requirement, compatibility text, or diagnostic prose.

## Admission contract

Admission is one ordered decision, not independent predicates:

1. Require exact definition identity and per-segment introduced counts. A
   missing definition identity or `null` or empty count vector stays on the
   legacy path outside this contract.
2. Keep a leaf recognized by the existing CSharp generated-name classifier on
   the legacy path regardless of its exact-side arity evidence; generated
   substitution and arity validation are not admitted by this design.
3. For the remaining ordinary leaf, validate the supplied nonempty count vector
   against the definition-name segment count, then validate the leaf's
   canonical metadata arity against its introduced count and require the leaf
   generic-parameter list to have that same count. A malformed nonempty vector
   does not become legacy merely because its length is wrong.
4. Invoke `CSharpIdentifier.AdmitTypeDeclaration` directly on the arity-free
   leaf in type-declaration position.
5. Map that owner-issued spelling into `Admitted`, or return the one
   `Unrepresentable` reason selected by the first unsuccessful applicable step.

This order makes `Admitted`, `Unrepresentable`, and legacy compatibility
disjoint. An ordinary exact leaf is never judged by generated-substitute
policy, and this component does not reproduce CSharpText's compiler exceptions.

### Admitted

Exact admission requires the canonical metadata arity to equal the
authoritative introduced count: a positive count requires its matching suffix,
and a zero count requires no suffix. It removes that suffix only after the
equality is proven. The declared type must carry exactly that many leaf generic
parameters; fewer or more cannot recreate the exact TypeDef identity. CSharpText
`CSharpIdentifier.AdmitTypeDeclaration` must then issue a legal source spelling
whose compiler-emitted TypeDef name equals the remaining leaf. A reserved
type-declaration word may therefore use the ordinary `@` prefix without
changing identity.

`@` escaping changes source spelling without changing the declared identifier,
so the result remains identity-preserving. Punctuation encoding, character
replacement, truncation, generated substitution, or parsing a display escape
is not admission.

### Unrepresentable

Admission is unrepresentable when:

- the complete count vector or canonical leaf arity does not agree with the
  exact definition name;
- the declared leaf generic-parameter count does not equal that arity; or
- CSharpText refuses the residual ordinary leaf in type-declaration position.

The corresponding closed reasons are `ArityMismatch` and
`IdentifierNotAdmitted`, with the latter retaining CSharpText's closed lexical
reason.
A noncanonical backtick sequence is residual leaf text, not a canonical suffix.
With an authoritative introduced count of zero it reaches CSharpText as literal
text and normally produces `IdentifierNotAdmitted`; with a positive count it
cannot supply the required canonical suffix and produces `ArityMismatch`. The
ordered decision gives one input one reason.

Identity-display escaping such as `A\+B`, inert-text containment, or replacement
with a plausible ordinary identifier cannot turn this result into `Admitted`.

## First-adopter contract

`CSharpTypePrinter` preflights every admissible exact self-name in the complete
batch, including nested declared types, before existing duplicate validation,
rendering, or source publication. Its target return is an atomic typed outcome:
`Printed` carries the existing `CSharpTypePrintResult`; `NotRendered` carries
the exact self-name failures and has no units, source, source artifact, or
replacement range.

For each admitted type:

- the type header consumes the admitted identifier and separately composes its
  declared generic parameters;
- constructor and destructor-spelled finalizer heads consume the same
  identifier without generic arguments;
- a finalizer intentionally emitted as `void Finalize()` has no self-name
  occurrence and preserves that existing fidelity fallback; and
- no position calls a separate metadata-leaf formatter or searches completed
  declaration text.

If any exact self-name is `Unrepresentable`, the complete batch is
`NotRendered`. No namespace group, independent top-level request, nested
subtree, or selected replacement body is published. An empty compilation unit,
a partial `CSharpTypePrintResult`, and a free-form diagnostic are not failure
results.

An `ApiType` without `MetadataTypeDefinitionName`, or with a `null` or empty
introduced-count vector, remains on the existing legacy compatibility path.
That path supports hand-composed and older serialized inputs, but it cannot
produce an admitted self-name or serve as evidence for this claim. For an
ordinary exact leaf, a nonempty vector whose length disagrees with the exact
definition-name segment count is instead contradictory evidence and produces
`ArityMismatch`. Recognized generated names preserve their current legacy
normalization regardless of count-vector length, canonical suffix, or leaf
generic-parameter count; none of that evidence acquires admission or
identity-fidelity claims here.
Existing formatter methods may continue to produce identity display or legacy
text; that text cannot be converted to the admitted currency.

Existing duplicate-output validation remains outside this self-name result.
Its current exception behavior applies to exact, legacy, and mixed batches;
this design neither makes it a `NotRendered` arm nor claims its key is
compiler-complete. Correcting generated-name or cross-scope collisions is
separate work tracked by issue #5217. Because exact-name preflight runs first,
a batch containing both an exact self-name refusal and any duplicate always
returns `NotRendered`, independent of request order. Duplicate validation runs
only after every applicable exact name admits.

## Mock interaction

```text
request
  exact leaf = "class"
result
  Admitted(identifier="@class")
uses
  type header = "class @class"
  constructor = "public @class()"
  finalizer   = "~@class()"

request
  exact leaf = "A+B"
result
  Unrepresentable(
    reason=IdentifierNotAdmitted(InvalidIdentifier),
    identity=["A+B"])
uses
  no type, constructor, or finalizer source is published
```

An exact generated leaf remains on the legacy compatibility path. Its current
substitute may enter legacy shell output, but it is not an admitted identifier
and makes no exact identity claim.

## Validation endpoint

The terminal observation for this adopter is the public
`CSharpTypePrintOutcome`, not the intermediate CSharpText admission result.
CSharpText proves that one admitted leaf token compiles and preserves its
TypeDef leaf identity; this component owns whether that token was composed into
the requested model-bound artifact correctly.

The complete validation goal has two outcome arms:

- `NotRendered` carries the exact self-name failures and exposes no source,
  units, source artifact, or replacement range.
- `Printed` carries source produced by `CSharpTypePrinter`. A tools-only gate
  compiles that source unchanged, reads the emitted TypeDefs through SRM, and
  requires the complete namespace, nesting, leaf name, and canonical generic
  arity of every exact admitted declaration to equal the requested Metadata
  identity. Successful compilation also proves that constructor and
  destructor-spelled finalizer heads bind to their containing declarations.

The compiler harness may supply references and compilation options, but it must
not construct, normalize, or repair the source under test. Compilation success
is distinct from identity agreement, following
[C# assembly round-trip testing](csharp-member-recompilation.md#proof-levels).
Roslyn remains tools/test-only; this validation goal adds no compiler dependency
to `CSharpText` or `ILInspector.CSharp`.

The named gates below currently prove lexical admission, model-bound handoff,
shared use, hostile-metadata refusal, and atomic publication separately. A
retained gate that compiles actual `CSharpTypePrinter` output and compares all
exact emitted TypeDef identities through SRM is **unverified**. That gate is the
next evidence endpoint; it strengthens evidence without broadening this
component into member, namespace, generated-name, or whole-assembly policy.

## Required implementation gates

These properties are enforced by:

- `DeclaredTypeSelfNameAdmissionTests.OrdinaryExactNamesConsumeCSharpTextAdmission`,
  covering ordinary, BMP and supplementary Unicode, Unicode format characters,
  ordinary and newly reserved words such as `extension`, nested, generic,
  noncanonical-backtick, arity-mismatch, and literal-punctuation neighbors; the
  gate relies on #5215's real-compiler and emitted
  TypeDef-identity evidence rather than restating its tables, and rejects
  top-level and nested ordinary requests with fewer or more leaf generic
  parameters than the exact introduced count;
- `TypeShellProducerTests.HostileMetadataSelfNameIsNotRendered`, proving a
  legal SRM-read TypeDef whose literal leaf is `A+B`, `A<B`, or whitespace-only
  retains that exact identity through shell production and reaches the typed
  CSharp refusal boundary;
- `CSharpTypePrinterTests.SelfNameIsSharedByItsDeclarationPositions`, proving
  a positive-arity exact non-delegate named `extension` uses one prepared
  admitted identifier in its type header, instance and static constructors,
  and destructor-spelled finalizer while its suppressed finalizer uses no
  self-name. The header alone composes the declared generic parameters; no
  constructor or finalizer head does. A second positive-arity exact `extension`
  delegate proves the separate delegate header path consumes that same kind of
  prepared identifier and composes its declared generic parameters; and
- `CSharpTypePrinterTests.SelfNameFailureMakesBatchNotRendered`, using the
  hostile exact identity for top-level, nested, same-namespace,
  multi-namespace, and selected-replacement failures while proving the typed
  outcome exposes no partial source surface. Singleton `Print`, `PrintBatch`,
  generated-name legacy routing with valid, truncated, overlong, and
  suffix-disagreeing arity evidence and with fewer or more leaf generic
  parameters than its introduced count, missing-identity and `null` or
  empty-count legacy routing, ordinary-name truncated and overlong count-vector
  refusal, mixed legacy/exact batches, and both request orders for refusal plus
  duplicate validation are explicit cases; and
- `CSharpTypePrinterTests.GeneratedLegacyNameIsSharedWithTypeNameContext`,
  `GeneratedLegacyNamesUseRenderedSpellingForDuplicateValidation`, and
  `GeneratedLegacyNameUsesExactLeafInsteadOfDottedDisplayName`, proving the
  one normalized generated legacy leaf remains shared by declaration,
  type-name context, and output duplicate validation even when exact-side arity
  evidence disagrees, without replacing its raw canonical metadata identity.

The implementation remains in the existing SRM-only, Roslyn-free product
closure and introduces no platform-specific API. Compiler use belongs only to
the test oracle.

## Non-claims

This design does not define:

- a universal CSharp declaration-result or provenance protocol;
- generated-name substitution or declaration-identity collision policy
  (#5217);
- names of ordinary members, parameters, generic parameters, namespaces,
  attributes, constraints, type references, or explicit-interface qualifiers;
- declaration composition, receipts, compatibility text, retention domains, or
  adoption registries;
- Metadata identity construction or CSharpText lexical policy;
- source-unit layout, bodies, initializers, fallback presentation, or CLI
  behavior;
- a new collision or generated-name policy beyond making the existing choice
  explicit and typed; or
- faithful round-trip C# for a generated substitute.
