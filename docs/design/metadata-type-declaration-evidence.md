# Metadata Type Declaration Evidence

## Status and ownership

This design defines one `ILInspector.Metadata` claim:

> For one exact, locally owned TypeDef, Metadata posts the complete detached
> declaration facts needed to identify and locally classify that type as a
> method-like declaration owner, or one typed rejection, without exposing live
> reader authority or deciding whether C# can represent the declaration.

It also defines the independently demanded complete Signature post:

> For that exact TypeDef, Metadata can separately post the direct Base Type
> shape, every ordered generic-constraint Type shape, and the count of all
> immediate nested TypeDefs as one detached byte-backed result. Declaration
> demand does not decode those Type shapes or publish that complete result.

The first production consumer is
[CSharp declaration representability](csharp-declaration-representability.md)
under [#4852][issue-4852]. ReturnToSender is the first production host through
the eighteen-step adoption and compile-back retirement tracker
[#6199][issue-6199].

This proposal does not describe current product support. It defines the
containing-TypeDef prerequisite tracked by [#8348][issue-8348].

## Demo and motivating boundary

The canonical production witness is the containing TypeDef for the static
explicit-interface addition operator on `System.Int32`:

```csharp
static int IAdditionOperators<int, int, int>.operator +(
    int left, int right) => left + right;
```

The ordinary MethodDef post identifies the selected body owner only by its
exact `MetadataTypeDefinitionAddress`. Its signature represents `System.Int32`
through the primitive signature encoding. CSharp cannot prove that this body is
declared by that same structured type, or that the declaration owner is a
value type rather than a same-spelled class, from the address or display text.

The TypeDef post therefore supplies the exact structured definition identity,
its open declaration-self identity, the authenticated primitive alias for the
core-library `System.Int32` definition, raw TypeDef flags, and the local
declaration category. CSharp joins the type and method posts by their exact
TypeDef address, then compares only owner-issued structured identities.

The neighboring negative witness is a same-spelled `System.Int32` TypeDef in an
assembly that does not define the authenticated core-library root. Metadata
posts its exact named identity without the primitive alias. CSharp therefore
cannot equate it with an `ELEMENT_TYPE_I4` signature merely because the
namespace and name look familiar.

The motivating source is the .NET runtime's
[`System.Int32` declaration][dotnet-int32] at commit
`b0f34d51fccc69fd334253924abd8d6853fad7aa`. Deterministic compiler-produced
and authored-metadata fixtures preserve the accepted and neighboring shapes in
the repository; the production platform assembly remains the integration
witness.

## Design basis

The contract follows these normative owners:

- ECMA-335 owns TypeDef, NestedClass, GenericParam, signature primitive, flag,
  and extends encoding.
- `System.Reflection.Metadata` owns the load-free reader model used to decode
  those rows.
- [Metadata declaration sessions][metadata-declaration-sessions] own image
  admission, operation lifetime, work accounting, and detached publication.
- [Type, member, and API representation][type-member-representation] owns the
  distinction among a structured lookup name, signature shape, exact row
  address, and definition correspondence.
- [Metadata Method Declaration Evidence][method-declaration-evidence] owns the
  selected MethodDef and complete declaring-type generic parameter
  declarations.
- [CSharp declaration representability][csharp-representability] owns source
  legality, language-profile, accepted-request, and rendering decisions.

The closest existing Metadata surfaces are intentionally insufficient:

- `TypeDeclarationResult` answers whether one exact name is declared in an
  image; it does not post one exact addressed TypeDef's raw declaration facts
  or signature-self identity.
- `AssemblyTypeDeclarationInventory` supports discovery and public-surface
  listing; it does not preserve an exact TypeDef address or the identity needed
  to compare a method signature with its owner.
- `ResolvedTypeDefinition` is scoped to a frozen cross-assembly catalog and
  carries acquisition and correspondence concerns that this local declaration
  post does not need.
- mutable `ApiType` is a C# and API-surface projection, not Metadata evidence
  for admission.

The design follows the conventional SRM split between a TypeDef row and
signature decoding, but is stricter at the handoff: the post retains both the
exact named definition and every authenticated signature identity that can
denote its open declaration. It does not ask CSharp to reconstruct primitive
or generic self-correspondence from names.

For direct base references, the design reuses Metadata's existing recognized
core-library AssemblyRef contract: a known core-library assembly name paired
with one of its legitimate strong-name public-key tokens. That contract is
reference authentication, not cross-assembly resolution. The post does not
open or bind the referenced assembly.

## Request and result

A request identifies one `MetadataTypeDefinitionAddress` that must resolve to a
TypeDef in the declaration session's admitted image.

Both operations select one exact TypeDef rather than probing by name or
scanning for a declaration candidate. Each result is closed:

- `Posted` carries one complete detached
  `MetadataTypeDeclarationEvidence` or `MetadataTypeSignatureEvidence`; or
- `Rejected` carries one typed failure and the operation counters at the
  failure boundary.

There is no `Absent` arm. A concrete TypeDef address either names a row in the
admitted image or is an invalid request.

The public operations are `PostTypeDeclaration` and `PostTypeSignature`.
"Post" means publishing the final detached receipt after all reads, validation,
charging, and retention succeed. It does not mean constructing a session,
resolving another assembly, or rendering a source type.

Within one live declaration session, the exact TypeDef address is the cache key
for each operation. Repeating one request returns the same detached `Posted` or
`Rejected` result without repeating charged work. The Signature cache composes
the cached declaration post but is independent from it: declaration demand
does not decode Base or constraint Type shapes or project the nested-Type
count. The session checks cancellation and liveness before consulting either
cache, retains at most one result of each kind per addressed TypeDef, clears
both caches on disposal, and does not reuse results across independently opened
declaration sessions.

## Declaration post

The posted evidence carries:

- the exact requested `MetadataTypeDefinitionAddress`;
- one exact `MetadataNamedTypeIdentity` for the TypeDef, including scope,
  namespace, root-to-leaf segments, and per-segment introduced generic counts;
- one exact open declaration-self `MetadataTypeIdentity`;
- an optional authenticated primitive `MetadataTypeIdentity.Primitive` alias;
- raw `TypeAttributes`;
- one locally authenticated declaration category;
- whether the admitted assembly defines the unique core-library root;
- the exact direct declaring-TypeDef address when the selected type is nested;
  and
- the declaration of every generic parameter the definition's segments
  introduce.

The post contains no display string, C# keyword decision, escaped identifier,
rendered type header, base-list policy, mutable API-surface model, reader,
stream, lease, callback, catalog generation, or reopening authority.

### Structured declaration identity

The named definition identity preserves the exact local scope and structured
metadata name. It is not a display name or a claim that another assembly's
same-spelled type corresponds to this definition.

Before posting that identity, Metadata proves that the complete structured
name resolves uniquely to the requested TypeDef in the admitted image. A
duplicate selected definition or ambiguous enclosing-name chain rejects the
post. This bounded uniqueness check authenticates a value-equal signature
identity against the selected address; it does not select the TypeDef by name
or establish correspondence with another image.

The open declaration-self identity is the signature-shaped identity for this
definition:

- a non-generic definition posts a named identity;
- a generic definition posts a generic instance whose arguments are the exact
  cumulative declaring-type generic positions; and
- nested generic ownership uses the verified per-segment introduced counts
  rather than parsing a flattened name.

CSharp rejects or reports unavailable any joined posts whose type address,
generic count, or structured identity disagrees.

Primitive signature encodings do not carry a named scope. For a non-generic,
top-level definition that the admitted assembly authenticates as the unique
core-library declaration corresponding to one ECMA primitive type, the post
also carries the exact `MetadataTypeIdentity.Primitive` produced by the same
Metadata signature projection. No other type receives that alias.

The primitive alias is owner-issued equivalence evidence. CSharp may compare a
signature identity with either the open named identity or the authenticated
alias. It must not recreate that alias from `System` plus a familiar simple
name, an assembly name, or rendered text.

### Generic parameter declarations

Each GenericParam row that a segment introduces posts one declaration in
cumulative order: its defining segment, its metadata index, its exact name,
its raw `GenericParameterAttributes`, its GenericParamConstraint row count,
and its compiler annotations. A nested definition's repeated enclosing rows
are not reposted; each parameter is posted from the TypeDef that introduces
it.

The name is a `MetadataUtf8Name` over the exact `#Strings` bytes. All of one
post's parameter names share one immutable backing, so a post retains one
allocation for its names rather than one per name. Ill-formed UTF-8 keeps its
bytes; containment spells it when a sink presents the name. The post charges
the contained Field spelling length of each name as retained text.

The annotations are those Roslyn emits to complete a parameter's C#
constraint spelling:

- the nullable flag is the first byte of the parameter's own
  `NullableAttribute`, otherwise the value of the nearest
  `NullableContextAttribute` from the owning TypeDef outward through its
  declaring Types, otherwise `0`; and
- `IsUnmanagedAttribute` presence.

Only the single canonical encoding of each consulted attribute is read: a
`NullableAttribute(byte)` or non-empty `NullableAttribute(byte[])` value and a
`NullableContextAttribute(byte)` value, each with the standard prolog and no
named arguments, at most once per row. Any other encoding, a duplicate, or an
unreadable attribute row posts the annotations as non-canonical, which carries
no flag or marker claim. Annotations complete a spelling rather than the
parameter's identity, so they do not reject the post.

Constraint Types are not posted by `PostTypeDeclaration`. A consumer that
needs the complete Signature requests `PostTypeSignature`; declaration and
Name consumers retain this cheaper route.

## Complete Signature post

`PostTypeSignature` first requires the complete declaration post for the same
address, then atomically adds:

- the direct extends Type shape when the TypeDef has one;
- the exact declaration parameter population paired with every
  GenericParamConstraint Type shape in metadata row order;
- the effective nullable flag for each constraint Type;
- the Base `DynamicAttribute` transform flags consumed by established Type
  spelling; and
- the physical count of all immediate NestedClass children.

The result carries the declaration post rather than reproducing its identity,
category, flags, parameter names, or compiler annotations. Declaration and
Signature evidence therefore cannot disagree about the exact TypeDef or
parameter order.

### Byte-backed Type shapes

Every byte-originated named component remains a `MetadataUtf8Name` through
detached publication. All Base and constraint Type names in one Signature post
share one immutable backing. Primitive, named, generic-instance,
type-parameter, array, SZ-array, pointer, by-reference, function-pointer,
modified, and pinned signature forms remain typed nodes; Metadata does not
flatten them to display text or decode their names to UTF-16 for transport.

A named shape retains its namespace, root-to-leaf metadata Name segments, and
per-segment generic arity, plus the signature's class/value marker when one is
encoded. A direct TypeDefOrRef row posts that marker as unspecified. A local
TypeDef uses validated GenericParam ownership for arity and rejects a
contradictory metadata-name suffix; a TypeRef uses its canonical metadata-name
suffix. The operation validates the complete TypeRef resolution-scope chain
but does not retain scope. This result owns exact presentation spelling, not
cross-assembly Type identity or definition correspondence.

TypeSpec decoding uses the guarded Metadata signature provider and the
declaration's exact cumulative Type generic-parameter population. An
out-of-range Type parameter, method generic parameter, malformed generic
instance, unsafe TypeSpec, invalid TypeDefOrRef, or malformed scope chain
rejects the complete Signature. There is no degraded node, placeholder text,
or partial success.

### Base and generic constraints

Metadata posts the actual direct extends shape, including `System.Object`,
`System.ValueType`, and `System.Enum`. The Type/Member Signature projection
owns the established policy that omits those runtime roots and presents only a
nontrivial Base.

Each declaration parameter is paired with all of its GenericParamConstraint
rows in metadata order. Metadata does not classify pseudo constraints or
reorder their shapes. The Type/Member projection owns the established spelling
order—variance, primary keyword, non-pseudo Type constraints, `new()`, and
`allows ref struct`—and suppresses pseudo-constraint Types already represented
by keywords.

A constraint Type's nullable flag is the first byte of its own
`NullableAttribute`, otherwise the nearest declaring-Type
`NullableContextAttribute`, otherwise absent. Base dynamic transform flags are
retained only for a TypeSpec Base, matching the established formatter's
consumption boundary. Neither annotation authorizes a narrower result or a
fallback rerun.

### Immediate nested Type count

The count is the complete physical population returned by the selected
TypeDef's immediate NestedClass relationships. It includes public, nonpublic,
hidden, and compiler-generated children. A grandchild contributes to its
direct parent's count, not every ancestor's count. Zero is posted as zero.

The declaration post has already validated the complete NestedClass table,
selected visibility, and declaring chain. Signature production additionally
authenticates every counted child's direct declaring Type against the selected
TypeDef. Host admission policy and `--all` do not participate.

### Local declaration category

The post classifies the selected definition into one closed local category:

- `Class`;
- `Interface`;
- `Struct`;
- `Enum`; or
- `Delegate`.

The classification uses the exact TypeDef flags and direct extends
relationship. A local direct base uses unique in-image core-library-root
authentication. A cross-assembly TypeRef uses Metadata's existing recognized
core-library reference contract, matching both assembly name and legitimate
strong-name public-key token. The target assembly is not opened or treated as
resolved.

Those authenticated paths identify `System.ValueType`, `System.Enum`,
`System.Delegate`, and `System.MulticastDelegate`. The core root types
themselves remain classes; only definitions with the corresponding
authenticated direct base receive the derived category. A same-spelled base
through an unrecognized assembly reference is an ordinary class base, not a
runtime value-type, enum, or delegate root.

This is a local declaration fact, not a proof that an external base type is
available, that an inheritance graph is valid, or that C# can emit the type.
An extends encoding whose special category cannot be authenticated locally
returns typed rejection rather than a plausible derived category.

Raw `TypeAttributes` remain in the post so CSharp can evaluate accessibility,
abstract, sealed, static-class, layout, and other source rules without
Metadata choosing C# modifiers.

### Nesting

For a top-level TypeDef, the direct declaring-type address is absent and the
visibility flags must be top-level. For a nested TypeDef, the post retains the
exact direct declaring-TypeDef address and nested visibility flags.

The structured definition's segment chain, the NestedClass relationship, and
the per-segment generic ownership counts must agree. Missing, duplicate,
cyclic, out-of-range, or contradictory nesting evidence rejects the entire
post.

This first contract does not post the enclosing types' complete declaration
shapes. A consumer that needs a nested source shell must obtain the additional
owner-issued posts or return prerequisite unavailability. It cannot infer the
outer declarations from the selected type's flattened name.

## Atomicity and detached publication

Each operation stages all mutable work privately. It publishes `Posted` only
after its complete evidence validates. For the Signature post that includes:

- the address resolves in the admitted image;
- the TypeDef row and direct nesting relationship validate;
- the structured definition, open self identity, and any primitive alias are
  retained completely;
- the local declaration category is authenticated;
- every traversed relationship, structured node, and retained character is
  charged;
- all public values are detached; and
- cancellation has been observed at the final publication boundary.

The Signature post additionally requires complete Base and constraint decode,
exact declaration-parameter correspondence, and authenticated immediate nested
count. Any failure returns one `Rejected` result with no partial declaration or
Signature. A cancelled operation throws `OperationCanceledException`;
cancellation is not converted into a Metadata rejection.

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

The stages distinguish request validation, TypeDef row read, identity
projection, category classification, nesting validation, Signature Base
projection, Signature constraint projection, Signature nesting projection,
and result retention. The mechanisms distinguish image admission, address
resolution, row read, handle validation, relationship traversal, signature
decode, core-root authentication, structured projection, and text retention.

Failure detail is stable owner text. It does not quote artifact-authored type,
namespace, module, or assembly names.

## Pathological and neighboring evidence

The Release gate must include:

- the real platform `System.Int32` TypeDef, including its structured named
  identity, value-type category, and authenticated `int` primitive alias;
- a same-spelled non-core `System.Int32` without a primitive alias;
- a token-based signature reference to the selected open declaration identity;
- duplicate same-named TypeDefs and an ambiguous enclosing-name chain, both
  rejected before self identity publication;
- a generic and nested generic declaration whose cumulative positions and
  per-segment introduced counts agree;
- compiler-produced `notnull`, `class?`, `class`, `unmanaged`, and
  unconstrained parameters, plus agreement of every leaf-introduced platform
  parameter's annotations with the `ApiSurface` constraint projection;
- authored canonical, context-inherited, truncated, duplicated, empty-array,
  and malformed-context nullable annotations, each posted;
- an ill-formed generic parameter name that keeps its bytes and charges its
  contained spelling;
- malformed, duplicate, cyclic, and contradictory NestedClass evidence;
- top-level and nested visibility disagreement;
- authentic struct, enum, and delegate definitions plus the core
  `System.ValueType`, `System.Enum`, and `System.MulticastDelegate` class
  controls;
- ordinary struct, enum, and delegate definitions whose direct base uses a
  recognized strong-named core-library AssemblyRef, plus a same-named
  unrecognized reference control;
- a TypeDef whose extends shape cannot be classified locally;
- operation-budget exhaustion before structured identity publication;
- cancellation at final publication; and
- repeated exact requests within one declaration session, including posted and
  rejected results, with no repeated charge and no cancellation suppression;
- use of every posted value after the declaration and assembly sessions are
  disposed;
- a generic TypeSpec Base with a typed generic instance, array, and Type
  parameter shape;
- multiple GenericParamConstraint Type shapes in metadata order, including
  effective nullable annotations and pseudo-constraint controls;
- an ill-formed constraint-Type Name that keeps its exact UTF-8 bytes through
  detached publication;
- a malformed constraint target that the declaration post does not decode and
  the Signature post rejects at constraint projection;
- zero and multiple immediate nested TypeDefs across visibility and hidden
  shapes, plus a deeper descendant that does not increment its grandparent;
  and
- independent declaration and Signature cache reuse with no repeated charge.

Compiler-produced fixtures establish ordinary class, interface, struct, enum,
delegate, generic, and nested shapes. Authored metadata establishes malformed,
duplicate, cyclic, spoofed-core-name, and unsupported extends boundaries.

## Gates

| Gate | Property |
| --- | --- |
| `TDE001` | One exact TypeDef address produces one complete post or one typed rejection; there is no name-probe or first-match fallback. |
| `TDE002` | The complete structured name resolves uniquely to the requested TypeDef, and structured scope, nesting, and generic ownership produce the exact named and open declaration-self identities without display reconstruction. |
| `TDE003` | Only an authenticated unique core-library definition receives its exact primitive signature alias; same-spelled types do not. |
| `TDE004` | Raw flags plus authenticated local-root or recognized strong-named core-reference classification preserve class, interface, struct, enum, delegate, and core-root-class distinctions without opening another assembly. |
| `TDE005` | NestedClass multiplicity, cycles, bounds, visibility, structured segments, and generic ownership are validated atomically. |
| `TDE006` | Every metadata-controlled traversal, structured node, and retained character is bounded before publication; cancellation remains observable. |
| `TDE007` | The public request, post, rejection, and counters contain no live authority and remain usable after session retirement. |
| `TDE008` | One declaration session reuses the detached result of an identical TypeDef request without repeating charged work; both posted and rejected results are reusable, while cancellation is checked before cache lookup. |
| `TDE009` | Each introduced generic parameter posts its exact name bytes over one shared backing, raw flags, constraint row count, and canonical compiler annotations; a non-canonical annotation is posted without a claim rather than rejecting the post. |
| `TDE010` | Declaration demand does not decode Base or constraint Type shapes or project the nested-Type count; Signature demand composes the cached declaration as one independently cached complete result. |
| `TDE011` | Base and ordered constraint Type shapes remain typed and byte-backed through detached publication, with guarded TypeSpec decoding and no placeholder, degraded, or UTF-16 transport result. |
| `TDE012` | Base dynamic flags and constraint nullable flags preserve the annotation evidence consumed by established Type spelling without selecting a narrower plan or fallback rerun. |
| `TDE013` | Nested Type count includes every immediate NestedClass child independent of visibility, hidden admission, and host policy; it excludes deeper descendants and posts zero. |

The public input and result-shape test rejects `MetadataReader`, `PEReader`,
stream, session, operation-context, lease, callback, mutable collection, and
catalog-generation authority in the object graph.

## Non-claims

This contract does not:

- probe by name, resolve another assembly, or prove cross-image definition
  correspondence;
- post interface identities, an enclosing type shell, field set, layout,
  arbitrary custom attributes, or API surface;
- treat a Signature named shape as cross-assembly Type identity;
- define MethodDef, MethodImpl, InterfaceImpl, or MethodSemantics evidence;
- decide C# identifier spelling, accessibility, modifiers, category legality,
  language version, accepted requests, or rendering;
- select ReturnToSender targets, compile source, compare IL, or assign
  fidelity;
- load an inspected assembly or add Roslyn to a product path; or
- update root product, overview, architecture, or skill documentation.

## Completion boundary

The design slice is complete when this focused document is reviewed and
merged. It locks the Metadata-owned exact TypeDef post without changing CSharp
or ReturnToSender behavior.

The complete Signature evidence implementation slice is complete only when
`MetadataDeclarationSession.PostTypeSignature` and its detached result land,
`TDE001` through `TDE013` pass in Release, and Type/Member, CLI, and Browser
adoption remain separate slices.

[csharp-representability]: csharp-declaration-representability.md
[dotnet-int32]: https://github.com/dotnet/dotnet/blob/b0f34d51fccc69fd334253924abd8d6853fad7aa/src/runtime/src/libraries/System.Private.CoreLib/src/System/Int32.cs
[issue-4852]: https://github.com/richlander/dotnet-inspect/issues/4852
[issue-6199]: https://github.com/richlander/dotnet-inspect/issues/6199
[issue-8348]: https://github.com/richlander/dotnet-inspect/issues/8348
[metadata-declaration-sessions]: metadata-declaration-sessions.md
[method-declaration-evidence]: metadata-method-declaration-evidence.md
[type-member-representation]: type-member-api-representation.md
