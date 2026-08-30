# C# declared-type self-name admission

## Status

Proposed design for issue #5182. The component and first adoption do not exist,
so the admission, shared-use, and refusal properties below are **unverified**
until their named gates land.

This is the focused successor to superseded PR #5110. It does not inherit that
PR's proposed declaration-result, receipt, composition, or retention protocol.

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
- the authoritative introduced generic-parameter count for its leaf; and
- the existing CSharp-owned generated-name classification.

An arbitrary `ApiType.Name`, formatted type name, compatibility string, or
inert display value is not admission evidence. CSharpText continues to own the
model-free Unicode identifier grammar and declaration-keyword classification;
this component applies those rules to the typed Metadata identity.

The closed result is one of:

- **Admitted**: the exact Metadata identity, one legal C# identifier, and one
  fidelity value, `Exact` or `GeneratedSubstitute`;
- **Unrepresentable**: the exact Metadata identity and a closed reason, with no
  C# identifier.

The result is not a declaration-wide outcome and carries no source fragment,
receipt, namespace requirement, compatibility text, or diagnostic prose.

## Admission contract

Admission is one ordered decision, not independent predicates:

1. Require exact definition identity and authoritative per-segment introduced
   counts. An input missing either stays on the legacy path outside this
   contract.
2. Validate the leaf's canonical metadata arity against its introduced count.
3. If the existing CSharp generated-name classifier recognizes the exact leaf,
   derive and validate its substitute.
4. Otherwise require an identity-preserving C# identifier.
5. Return the one failure selected by the first unsuccessful applicable step.

This order makes `Exact`, `GeneratedSubstitute`, and `Unrepresentable`
disjoint. A generated leaf is considered for its explicit substitute before
ordinary identifier rejection; an ordinary exact leaf is never rejected merely
because it is ineligible for generated substitution.

### Exact

Exact admission requires the canonical metadata arity to equal the
authoritative introduced count: a positive count requires its matching suffix,
and a zero count requires no suffix. It removes that suffix only after the
equality is proven. The remaining leaf must satisfy CSharpText's full Unicode
identifier grammar and preserve its exact identity when compiled. A declaration
keyword is represented with the ordinary `@` prefix.

`@` escaping changes source spelling without changing the declared identifier,
so it remains exact. Punctuation encoding, character replacement, truncation,
or parsing a display escape is not exact admission.

Unicode format characters are valid in portions of C#'s identifier grammar but
are removed from identifier identity by the compiler. A leaf containing one is
therefore not `Exact`. The compiler characterization gate compares the emitted
TypeDef leaf with the Metadata input; successful compilation alone is
insufficient.

### Generated substitute

A generated substitute is permitted only when the CSharp owner recognizes the
exact Metadata leaf with its existing compiler-generated-name classifier. The
substitute must itself be a legal declaration identifier and must be derived
deterministically from the exact leaf. The choice is unconditional wherever
the first adopter currently normalizes a generated type name; body policy does
not add another switch.

The result retains `GeneratedSubstitute`; it never claims that recompiling the
substitute recreates the original metadata identity. Callers cannot provide an
arbitrary substitute or relabel one as exact.

Admission of one value does not establish output-name uniqueness. Before
publication, the first adopter compares declaration identity by namespace,
containing declared-type identity, admitted identifier semantics, and generic
arity. Type-parameter spelling is not part of that key. A generated substitute
that collides with another generated or ordinary name fails atomically even
when their type-parameter names differ.

### Unrepresentable

Admission is unrepresentable when:

- its canonical arity does not equal the authoritative introduced count;
- a recognized generated name does not produce a legal substitute; or
- the residual ordinary leaf cannot preserve its exact Metadata identity as a
  C# identifier.

The corresponding closed reasons are `ArityMismatch`,
`InvalidGeneratedSubstitute`, and `IdentityNotRepresentable`.
A noncanonical backtick sequence is residual leaf text, not a malformed
canonical arity suffix. It is judged by the final identity-preservation step.
The ordered decision gives one input one reason.

Identity-display escaping such as `A\+B`, inert-text containment, or replacement
with a plausible ordinary identifier cannot turn this result into `Admitted`.

## First-adopter contract

`CSharpTypePrinter` admits every exact self-name in the complete batch,
including nested declared types, before rendering or publishing source. Its
target return is an atomic typed outcome: `Printed` carries the existing
`CSharpTypePrintResult`; `NotRendered` carries the exact self-name or
declaration-identity collision failures and has no units, source, source
artifact, or replacement range.

For each admitted type:

- the type header consumes the admitted identifier and separately composes its
  declared generic parameters;
- constructor and destructor-spelled finalizer heads consume the same
  identifier without generic arguments;
- a finalizer intentionally emitted as `void Finalize()` has no self-name
  occurrence and preserves that existing fidelity fallback; and
- no position calls a separate metadata-leaf formatter or searches completed
  declaration text.

If any exact self-name is `Unrepresentable`, or two admitted names collide
under declaration identity, the complete batch is `NotRendered`. No namespace
group, independent top-level request, nested subtree, or selected replacement
body is published. An empty compilation unit, a partial
`CSharpTypePrintResult`, and a free-form diagnostic are not failure results.

An `ApiType` without `MetadataTypeDefinitionName`, or without authoritative
per-segment introduced counts, remains on the existing legacy compatibility
path. That path supports hand-composed and older serialized inputs, but it
cannot produce an admitted self-name or serve as evidence for this claim.
Existing formatter methods may continue to produce identity display or legacy
text; that text cannot be converted to the admitted currency.

## Mock interaction

```text
request
  exact leaf = "class"
result
  Admitted(identifier="@class", fidelity=Exact)
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

A recognized generated leaf may instead produce
`Admitted(fidelity=GeneratedSubstitute)`. Its exact identity remains available
for diagnostics and correspondence, while only its legal substitute enters C#
syntax.

## Required implementation gates

These properties remain unverified until the Release suite contains:

- `DeclaredTypeSelfNameAdmissionTests.OutcomesAreOrderedAndRoundTripIdentity`,
  covering ordinary, Unicode, Unicode-format, keyword, nested, generic,
  generated, noncanonical-backtick, arity-mismatch, literal-punctuation, and
  hostile metadata neighbors against the real C# compiler, with emitted
  TypeDef-name equality required for `Exact`;
- `CSharpTypePrinterTests.SelfNameIsSharedAndCollisionSafe`, proving one
  admitted value supplies the type header, constructors, and
  destructor-spelled finalizers, suppressed finalizers use no self-name, and
  generated/ordinary collisions use identifier-plus-arity identity rather than
  type-parameter spelling; and
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
