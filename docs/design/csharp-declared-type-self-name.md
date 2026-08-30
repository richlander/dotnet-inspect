# C# declared-type self-name admission

## Status

Proposed design for issue #5182. The component and first adoption do not exist,
so the admission, shared-use, and refusal properties below are **unverified**
until their named gates land.

This is the focused successor to superseded PR #5110. It does not inherit that
PR's proposed declaration-result, receipt, composition, or retention protocol.
Compiler-accurate type-declaration identifier admission remains an explicit
CSharpText prerequisite in issue #5215.

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

## Immediate boundary

The admission input is:

- one exact `MetadataTypeDefinitionName`;
- the complete authoritative per-segment introduced generic-parameter count;
  and
- the CSharpText-owned type-declaration identifier-admission result from #5215.

An arbitrary `ApiType.Name`, formatted type name, compatibility string, or
inert display value is not admission evidence. CSharpText continues to own the
model-free compiler-accurate identifier and declaration-keyword classification;
this component applies its owner-issued result to the typed Metadata identity.

The closed result is one of:

- **Admitted**: the exact Metadata identity and one identity-preserving legal
  C# identifier;
- **Unrepresentable**: the exact Metadata identity and a closed reason, with no
  C# identifier.

The result is not a declaration-wide outcome and carries no source fragment,
receipt, namespace requirement, compatibility text, or diagnostic prose.

## Admission contract

Admission is one ordered decision, not independent predicates:

1. Require exact definition identity and authoritative per-segment introduced
   counts. An input missing either stays on the legacy path outside this
   contract.
2. Keep a leaf recognized by the existing CSharp generated-name classifier on
   the legacy path; generated substitution is not admitted by this design.
3. Validate the complete count vector against the definition-name segment
   count, then validate the leaf's canonical metadata arity against its
   introduced count.
4. Ask CSharpText #5215 to admit the arity-free leaf in type-declaration
   position.
5. Map that owner-issued spelling into `Admitted`, or return the one
   `Unrepresentable` reason selected by the first unsuccessful applicable step.

This order makes `Admitted`, `Unrepresentable`, and legacy compatibility
disjoint. An ordinary exact leaf is never judged by generated-substitute
policy, and this component does not reproduce CSharpText's compiler exceptions.

### Admitted

Exact admission requires the canonical metadata arity to equal the
authoritative introduced count: a positive count requires its matching suffix,
and a zero count requires no suffix. It removes that suffix only after the
equality is proven. CSharpText #5215 must then issue a legal source spelling
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
  exact definition name; or
- CSharpText refuses the residual ordinary leaf in type-declaration position.

The corresponding closed reasons are `ArityMismatch` and
`IdentifierNotAdmitted`, with the latter retaining CSharpText's closed lexical
reason.
A noncanonical backtick sequence is residual leaf text, not a malformed
canonical arity suffix. It is judged by the final identity-preservation step.
The ordered decision gives one input one reason.

Identity-display escaping such as `A\+B`, inert-text containment, or replacement
with a plausible ordinary identifier cannot turn this result into `Admitted`.

## First-adopter contract

`CSharpTypePrinter` admits every exact self-name in the complete batch,
including nested declared types, before rendering or publishing source. Its
target return is an atomic typed outcome: `Printed` carries the existing
`CSharpTypePrintResult`; `NotRendered` carries the exact self-name failures and
has no units, source, source artifact, or replacement range.

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

An `ApiType` without `MetadataTypeDefinitionName`, or without authoritative
per-segment introduced counts, remains on the existing legacy compatibility
path. That path supports hand-composed and older serialized inputs, but it
cannot produce an admitted self-name or serve as evidence for this claim.
Recognized generated names likewise preserve their current legacy normalization
without acquiring admission or identity-fidelity claims.
Existing formatter methods may continue to produce identity display or legacy
text; that text cannot be converted to the admitted currency.

Existing duplicate-output validation remains outside this self-name result.
Its current exception behavior applies to exact, legacy, and mixed batches;
this design neither makes it a `NotRendered` arm nor claims its key is
compiler-complete. Correcting generated-name or cross-scope collisions is
separate work tracked by issue #5217.

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
  Unrepresentable(reason=IdentityNotRepresentable, identity=["A+B"])
uses
  no type, constructor, or finalizer source is published
```

An exact generated leaf remains on the legacy compatibility path. Its current
substitute may enter legacy shell output, but it is not an admitted identifier
and makes no exact identity claim.

## Required implementation gates

These properties remain unverified until the Release suite contains:

- `DeclaredTypeSelfNameAdmissionTests.ExactNamesConsumeCSharpTextAdmission`,
  covering ordinary, BMP and supplementary Unicode, Unicode format characters,
  ordinary and newly reserved words such as `extension`, nested, generic,
  noncanonical-backtick, arity-mismatch, literal-punctuation, and hostile
  metadata neighbors; the gate requires #5215's real-compiler and emitted
  TypeDef-identity evidence rather than restating its tables;
- `CSharpTypePrinterTests.SelfNameIsSharedByItsDeclarationPositions`, proving
  one admitted value supplies the type header, constructors, and
  destructor-spelled finalizers while suppressed finalizers use no self-name;
  and
- `CSharpTypePrinterTests.SelfNameFailureMakesBatchNotRendered`, using the
  hostile metadata fixture for top-level, nested, same-namespace,
  multi-namespace, and selected-replacement failures while proving the typed
  outcome exposes no partial source surface.

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
