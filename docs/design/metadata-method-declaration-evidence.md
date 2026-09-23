# Metadata Method Declaration Evidence

## Status and ownership

This design defines one `ILInspector.Metadata` claim:

> For one exact, locally owned MethodDef, Metadata posts a complete detached
> declaration record, or one typed rejection, without exposing live reader
> authority or deciding whether C# can represent the declaration.

The first production consumer is
[CSharp declaration representability](csharp-declaration-representability.md)
under [#4852][issue-4852]. ReturnToSender is the first production host through
the seventeen-step adoption and compile-back retirement tracker
[#6199][issue-6199].

This proposal does not describe current product support. It defines the
ordinary MethodDef prerequisite tracked by [#7886][issue-7886].

## Demo and motivating boundary

The canonical production witness is the MethodDef body for the static explicit
interface addition operator on `System.Int32`:

```csharp
static int IAdditionOperators<int, int, int>.operator +(
    int left, int right) => left + right;
```

Metadata must post the body method's exact name, flags, signature, generic
context, parameter correspondence, and marker facts. The body MethodDef itself
does not carry `SpecialName`; the separate MethodImpl post authenticates the
resolved interface declaration and its special-name evidence. That post also
composes with InterfaceImpl evidence. CSharp then decides whether the combined
evidence supports an explicit-interface operator under the selected language
profile.

The neighboring negative witness is a MethodDef named `op_Addition` without
`SpecialName`. Metadata posts the exact name and false operator-candidate fact;
it does not repair the declaration, classify it as malformed metadata, or turn
it into an operator.

## Design basis

The contract follows these normative owners:

- ECMA-335 owns MethodDef, Param, GenericParam, GenericParamConstraint,
  signature, flag, and custom-modifier encoding.
- `System.Reflection.Metadata` owns the load-free reader model used to decode
  those rows.
- [Metadata declaration sessions][metadata-declaration-sessions] own image
  admission, operation lifetime, and detached result publication.
- [CSharp declaration representability][csharp-representability] owns source
  category, spelling, language-profile, and accepted-request decisions.

Historical ReturnToSender admission work in [#7465][issue-7465] supplies
pathological evidence, not architecture. In particular, `HideBySig`, valid
operator signatures, and readonly-byref marker agreement remain CSharp
admission rules. Metadata reports their inputs rather than deciding them.

## Request and result

A request identifies:

- one `MetadataTypeDefinitionAddress`; and
- one `MetadataMethodAddress` that must resolve to a MethodDef declared
  directly by that TypeDef in the session's admitted image.

The operation selects one MethodDef rather than scanning for declaration
candidates. Before using SRM owner-range lookups, it does scan the physical
CustomAttribute, GenericParam, and GenericParamConstraint rows needed to prove
those lookups complete. Its result is closed:

- `Posted` carries one complete detached
  `MetadataMethodDeclarationEvidence`; or
- `Rejected` carries one typed failure and the operation counters at the
  failure boundary.

There is no `Absent` arm. A concrete MethodDef address either names the exact
owned row or is an invalid request. A future candidate-enumeration operation
may define absence independently.

The public operation is named `PostMethodDeclaration`. "Post" means publishing
the final detached receipt after all reads, validation, charging, and
retention succeed. It does not mean constructing or retaining a live session.

## Declaration post

The posted evidence carries:

- the exact requested type and method addresses;
- the inert metadata method name;
- raw `MethodAttributes` and `MethodImplAttributes`;
- whether the MethodDef has a nonzero body RVA;
- one complete `MetadataMethodSignatureIdentity`, including the raw signature
  header, generic arity, required-parameter count, return type, parameter
  types, exact scopes, custom modifiers, by-ref shape, arrays, pointers,
  function pointers, and generic positions;
- the complete declaring-type and method generic parameter contexts;
- one return-parameter fact and one parameter fact aligned to every signature
  position;
- a locally authenticated constructor-name category;
- a locally authenticated operator-candidate fact; and
- a locally authenticated finalizer-shape candidate.

The post contains no display string, C# keyword, escaped identifier, rendered
signature, mutable API-surface model, reader, stream, lease, callback, or
reopening authority.

The containing type's source-relevant shape remains a separate prerequisite
owned by its own Metadata post. The exact
`MetadataTypeDefinitionAddress` joins that post to this method post. The first
CSharp slice must return `Unavailable` when the required containing-type post
is absent; this method operation does not duplicate type-shell facts.

### Generic context

Each generic parameter retains:

- whether it belongs to the declaring type or method context;
- its validated zero-based index;
- its inert metadata name;
- raw `GenericParameterAttributes`;
- every GenericParamConstraint target as an exact structured
  `MetadataTypeIdentity`, preserving order and multiplicity; and
- marker evidence needed to distinguish the unmanaged encoding from an
  ordinary value-type constraint.

Declaring-type parameters preserve the cumulative nested-type context used by
the MethodDef signature. Generic parameter indices must be contiguous and
agree with the method signature's generic arity. A constraint that cannot be
decoded into an exact structured identity rejects the post rather than
becoming display text or disappearing.

The post does not classify effective base classes, infer transitive
constraints, or choose C# constraint keywords. Those are consumer decisions
over the exact attributes, constraints, and marker facts.

The public post uses a new immutable Metadata evidence type.
`GenericContext` remains an internal decoding helper; its names and
value-type flags are not the public generic declaration result.

### Parameter correspondence

Parameter evidence is indexed by signature position, not by physical Param-row
order. Each entry retains:

- whether a Param row exists;
- its inert name when present;
- raw `ParameterAttributes`; and
- recognized marker evidence.

Sequence zero is the return parameter. Sequence `N` corresponds only to
signature parameter `N`. Missing Param rows are posted explicitly. Duplicate
sequence numbers, negative or out-of-range sequences, and disagreement with
the signature reject the entire post. The operation never chooses the first
duplicate or drops an extra row.

### Modifier and marker evidence

The structured signature is the exact owner of signature custom modifiers,
including readonly-related `modreq` and `modopt` nodes. Param custom attributes
are separate evidence.

For the return position and every parameter position, Metadata reports counts
for the recognized declaration markers required by the first CSharp consumer:

- `IsReadOnlyAttribute`;
- `RequiresLocationAttribute`;
- `ParamArrayAttribute`; and
- `ParamCollectionAttribute`;
- `ScopedRefAttribute`; and
- `UnscopedRefAttribute`.

Counts preserve duplicate physical attributes. The marker set is either
`Complete` or `Unknown`. `Unknown` means at least one attribute constructor
could not be identified sufficiently to prove the recognized marker counts
complete. It is not false and cannot support CSharp acceptance for a form that
depends on that marker set.

Marker authentication uses the exact top-level namespace and metadata-name
segment. A nested type whose flattened display name matches a recognized
marker is an identified non-marker, not the framework declaration.

Generic-parameter marker evidence follows the same complete-or-unknown rule for
`IsUnmanagedAttribute`.

Metadata does not synthesize `ref readonly`, `in`, `out`, `params`,
`scoped`, or another C# spelling. CSharp compares the signature modifiers,
raw flags, and complete marker counts under its language profile.

### Local declaration candidates

The post authenticates only categories that can be decided from the selected
MethodDef:

- `InstanceConstructorCandidate` requires the exact `.ctor` metadata name and
  both `SpecialName` and `RTSpecialName`;
- `StaticConstructorCandidate` requires the exact `.cctor` metadata name and
  both flags;
- `OperatorCandidate` is true only when `SpecialName` is set and the exact
  unqualified member-name segment begins with `op_`; an explicit-interface
  metadata name may therefore end with `.op_*`; and
- `FinalizerShapeCandidate` records the local `Finalize`, instance, zero
  parameter, `void` return shape.

Raw names and flags remain in the post, so malformed or unusual neighboring
combinations are not erased by those classifications.

These are candidates, not C# decisions. In particular:

- CSharp owns the bounded operator-name catalog, staticness, arity, operand,
  conversion, checked-operator, and language-version rules;
- CSharp owns whether constructor shape is representable; and
- a finalizer requires slot evidence beyond one MethodDef. Explicit
  `System.Object.Finalize` association composes with the separate MethodImpl
  post. Any implicit reuse-slot proof belongs to a separately owned
  relationship result. This operation never infers destructor syntax from the
  name and local signature alone.

A finalizer-shape candidate without the required separate slot evidence cannot
support CSharp acceptance.

## Atomicity and detached publication

The operation stages all mutable work privately. It publishes `Posted` only
after:

- both addresses resolve and direct ownership is authenticated;
- the MethodDef signature and every generic constraint decode completely;
- generic indices and parameter correspondence validate;
- all required structured nodes and retained text are charged;
- all public values are detached; and
- cancellation has been observed at the final publication boundary.

Any failure returns one `Rejected` result with no partial declaration. A
cancelled operation throws `OperationCanceledException`; cancellation is not
converted into a Metadata rejection.

After publication, disposing the declaration session, assembly session, or
operation context cannot change or invalidate the result.

## Failures

Failures identify the request, reason, stage, mechanism, relevant handle, and
budget evidence when applicable. The closed reasons are:

- `InvalidRequest`;
- `MalformedMetadata`;
- `Cycle`;
- `BudgetExceeded`; and
- `UnsupportedShape`.

The stages distinguish request validation, MethodDef read, generic-context
read, signature decode, parameter correspondence, marker read, and result
retention. The mechanisms distinguish image admission, address resolution,
direct ownership, row read, handle validation, relationship traversal,
TypeSpec traversal, signature decode, custom-attribute identification, and
text retention.

Failure detail is diagnostic prose, not identity or machine-readable policy.
Tests assert typed reason, stage, mechanism, handle, and budget fields rather
than matching the prose.

## Bounded work

The point query reuses existing operation dimensions:

- `DeclarationCandidates` charges exactly once before reading the addressed
  MethodDef;
- `RelationshipEdges` charges before following ownership, declaring-type,
  generic-parameter, constraint, Param, custom-attribute, and TypeSpec edges;
- `SignatureBytes` charges before decoding the MethodDef signature and every
  TypeSpec reached by constraints or nested signature nodes;
- `StructuredNodes` charges before creating decode trees and detached identity
  nodes; and
- `RetainedText` charges the inert encoded length before retaining names or
  scope text.

Image admission remains charged once by `MetadataDeclarationSession`.
`MethodImplementationRows` and `InterfaceImplementationRows` are not charged
because this operation does not read those tables.

Before reading an owner range, the operation charges and validates the physical
ordering of CustomAttribute parents, GenericParam owners, and
GenericParamConstraint owners. A falsely asserted sorted-table flag therefore
rejects the post rather than hiding a marker or constraint.

The first budget failure wins. A rejected charge does not advance counters and
no text or structured node is materialized before its corresponding charge.

## Composition invariants

Consumers compose posts only through owner-issued identities:

- the exact `MetadataMethodAddress` joins this post to the selected body and
  MethodImpl request;
- `MetadataTypeDefinitionAddress` authenticates direct local ownership;
- `MetadataMethodSignatureIdentity` preserves the selected declaration shape;
  and
- exact structured type scopes preserve assembly and module distinctions.

Display text, escaped identifiers, simple type names, operator names, and
matching signatures are not correspondence currencies.

An accepted CSharp request must consume this complete post. It cannot replace
an `Unknown` marker set, failed post, or missing relationship post with a
legacy `ApiMember`, display signature, or metadata-looking fallback.

## Pathological evidence and gates

The Release gate must include:

- the real `System.Int32` generic-math operator MethodDef;
- the same `op_*` name with and without `SpecialName`;
- a MethodDef missing `HideBySig`, proving Metadata posts the false flag while
  CSharp owns admission;
- ordinary and generic methods whose declaring and method generic positions,
  names, attributes, and constraints survive detachment;
- same-spelled constraint and signature types from different assembly scopes;
- instance and static constructor names with correct and neighboring flag
  combinations;
- finalizer-shaped methods that do and do not have separate slot evidence;
- by-ref returns and parameters with marker-only, modifier-only, agreeing,
  duplicate, scoped, unscoped, and unknown marker evidence;
- missing, duplicate, reordered, and out-of-range Param rows;
- malformed and over-deep signatures and TypeSpec constraint graphs;
- below, at, and above every exercised operation limit;
- cancellation before work and at the final publication boundary;
- session, assembly, and operation disposal before access and after
  publication; and
- consumption from a public no-friend assembly.

The harness must exercise product-owned post construction. It may author
pathological metadata fixtures and assert detached output, but it must not
construct or repair the evidence that the product is supposed to post.

## Non-claims

This contract does not:

- decide C# representability, category spelling, modifiers, identifiers, or
  language-profile support;
- resolve or aggregate MethodImpl, InterfaceImpl, MemberRef, MethodSemantics,
  properties, indexers, or events;
- prove a finalizer slot from local MethodDef shape alone;
- infer interface inheritance, runtime dispatch, or source provenance;
- load the inspected assembly or use Roslyn;
- expose reader lifetime to a consumer; or
- change legacy API-surface extraction or ReturnToSender selection before the
  owning adoption steps land.

[csharp-representability]: csharp-declaration-representability.md
[issue-4852]: https://github.com/richlander/dotnet-inspect/issues/4852
[issue-6199]: https://github.com/richlander/dotnet-inspect/issues/6199
[issue-7465]: https://github.com/richlander/dotnet-inspect/pull/7465
[issue-7886]: https://github.com/richlander/dotnet-inspect/issues/7886
[metadata-declaration-sessions]: member-inspection-planning-and-metadata-projection.md
