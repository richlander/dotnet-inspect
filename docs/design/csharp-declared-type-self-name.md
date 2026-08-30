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
- the existing CSharp-owned generated-name classification when the operation
  explicitly permits a compilable substitute.

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

### Exact

Exact admission removes a generic-arity suffix only when
`MetadataNameArity` recognizes the canonical suffix and the authoritative leaf
count agrees. The remaining leaf must satisfy CSharpText's full Unicode
identifier grammar. A declaration keyword is represented with the ordinary
`@` prefix.

`@` escaping changes source spelling without changing the declared identifier,
so it remains exact. Punctuation encoding, character replacement, truncation,
or parsing a display escape is not exact admission.

### Generated substitute

A generated substitute is permitted only when the CSharp owner recognizes the
exact Metadata leaf as a compiler-generated shape for which the current
operation requests compilable shell output. The substitute must itself be a
legal declaration identifier and must be derived deterministically from the
exact leaf.

The result retains `GeneratedSubstitute`; it never claims that recompiling the
substitute recreates the original metadata identity. Callers cannot provide an
arbitrary substitute or relabel one as exact.

Admission does not establish output-name uniqueness. Existing type-printer
collision checks remain authoritative, and a substitute collision fails before
source publication.

### Unrepresentable

Admission is unrepresentable when:

- the leaf is not a legal C# identifier after valid keyword escaping;
- a claimed arity suffix is malformed or disagrees with the authoritative leaf
  count; or
- the leaf is not eligible for the bounded generated-substitute policy.

Identity-display escaping such as `A\+B`, inert-text containment, or replacement
with a plausible ordinary identifier cannot turn this result into `Admitted`.

## First-adopter contract

`CSharpTypePrinter` admits every exact self-name in one top-level type request,
including nested declared types, before publishing its source unit. For each
admitted type:

- the type header consumes the admitted identifier and separately composes its
  declared generic parameters;
- constructor and finalizer heads consume the same identifier without generic
  arguments; and
- no position calls a separate metadata-leaf formatter or searches completed
  declaration text.

If any exact self-name in that top-level request is `Unrepresentable`, the
request publishes no partial source unit. Its typed failure retains the exact
Metadata identity and admission reason. Other independent top-level requests
in a batch may keep their existing behavior, but neither an empty unit nor a
free-form diagnostic substitutes for the failed request.

An `ApiType` without `MetadataTypeDefinitionName` remains on the existing
legacy compatibility path. That path supports hand-composed and older
serialized inputs, but it cannot produce an admitted self-name or serve as
evidence for this claim.

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
  Unrepresentable(reason=InvalidIdentifier, identity=["A+B"])
uses
  no type, constructor, or finalizer source is published
```

A recognized generated leaf may instead produce
`Admitted(fidelity=GeneratedSubstitute)`. Its exact identity remains available
for diagnostics and correspondence, while only its legal substitute enters C#
syntax.

## Required implementation gates

These properties remain unverified until the Release suite contains:

- `DeclaredTypeSelfNameAdmissionTests.OutcomesAreClosedAndCompilerCharacterized`,
  covering ordinary, Unicode, keyword, nested, generic, generated, malformed
  arity, literal punctuation, and hostile metadata neighbors against the real
  C# compiler;
- `CSharpTypePrinterTests.SelfNameIsSharedByHeaderConstructorsAndFinalizers`,
  proving one admitted value supplies every self-name occurrence without
  display-text recovery; and
- `CSharpTypePrinterTests.UnrepresentableSelfNamePublishesNoPartialUnit`,
  using the hostile metadata fixture for top-level and nested failures while
  preserving exact typed evidence.

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
