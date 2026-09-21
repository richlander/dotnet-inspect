# Decompiler local declaration and binding plan

This document owns the Decompiler's final declaration ownership and local
binding plan for one fully raised function or nested body. It refines the
[Decompiler pipeline](../decompiler.md) rule that naming and declaration
placement run over fully determined scopes.

The focused claim is:

> Before presentation, the shared Decompiler pipeline decides where every
> materialized local is declared and which legal C# identifier binds its
> references. A renderer spells that decision; it does not reconstruct it.

This is one Decompiler responsibility moving out of `CSharpPrinter`. It does
not create a new storage, PDB, C# language, or host owner.

Before the first production adoption, the pipeline's exact-name and PDB-scope
decisions asked the printer to reconstruct declaration scopes, while the
printer also allocated final names. That reversed the intended dependency and
made the chosen binding unavailable to non-text consumers. The adoption below
retires that dependency for materialized locals.

## Boundaries

The plan consumes, without redefining:

- typed materialized locals and their storage provenance from the raised IR;
- exact Portable PDB local identity, fidelity, and fallback disposition from
  [Decompiler name and symbol preservation](decompiler-symbol-preservation.md);
- readable synthesis policy from
  [Readable local names](readable-local-names.md);
- the existing structured statement tree, including lexical blocks and
  syntax-owned declarations; and
- explicit presentation options supplied to the shared library path.

[The thin writer](value-typed-emission.md) owns residual stack-slot
materialization. A residual stack slot is not a materialized local and is not
silently converted by this plan. Definite-assignment analysis owns whether a
declaration requires initialization such as `= default`; this plan consumes
that disposition and does not redesign it.

The first production adoption uses the lexical structure already proved by
the existing PDB-scope and scope-entry-local passes. Consolidating those
passes' region-selection algorithms requires a separately demonstrated
composition problem; it is not a prerequisite for issuing this plan.

## Planning unit and identity

One plan covers exactly one finalized body:

- an `IrFunction`;
- one raised lambda body; or
- one raised local-function body.

A planned local is identified by its owning body and finalized logical-local
index. That pair is an IR binding key, not artifact identity. A raw index is
not unique across nested bodies, a display name is never identity, and an
original stack-slot number does not identify every logical local produced by
splitting or projection.

When present, `PdbLocalDeclaration` remains the exact artifact identity. Its
source method, local-variable row, physical slot, and half-open `LocalScope`
range remain distinct from the finalized IR binding key.

The plan is valid only for the body state from which it was issued. A later
rewrite, clone, or nested-body replacement must provide complete explicit
correspondence for every affected binding and declaration owner or discard
and recompute the plan. Equal node text, local indices, source offsets, or
names do not transfer a plan.

## Two decisions, in order

### Declaration ownership

Declaration ownership is independent of presentation taste. For every retained
materialized local, the plan selects exactly one emitted declaration owner:

- a body or lexical-block declaration;
- a declaration-bearing store or initialization;
- or a syntax construct that owns the declaration, such as a pattern,
  `foreach`, `using`, `fixed`, catch variable, or verified `out` declaration.

The resulting emitted declaration scope must contain every reference bound to
that local. It must also respect control-flow entry, unsafe/await boundaries,
reference-local requirements, and the initialization disposition already
established by their owners. If those constraints do not authorize a narrower
declaration, the plan retains the prior valid wider declaration rather than
guessing.

Planning declaration ownership does not move a statement, alter a branch,
split storage, manufacture initialization, or retain a lexical block.

### Binding allocation

Binding allocation consumes the complete declaration-ownership result and the
explicit presentation options for that render. It allocates in this order:

1. establish exact parameters, generic binders, local-function declarations,
   and exact local identities under the complete C# declaration-space relation;
2. preserve each usable exact local wherever its emitted declaration scope
   permits the binding, including legal reuse across a raised nested-callable
   boundary;
3. reserve every exact, enclosing, and descendant binder that approximate or
   generated presentation must conservatively avoid;
4. when explicitly enabled, prefer an eligible approximate PDB-derived stem
   for an exact identity that cannot be emitted;
5. use pass-issued synthesized names when present;
6. use readable type/role synthesis when enabled; and
7. use the stable slot-style fallback.

Exact identities are never renamed merely to admit a synthesized preference.
An approximate or synthesized spelling is presentation, not recovered source
identity. Its allocation does not remove an exact-name fidelity cause or
upgrade fidelity.

The final spelling is deterministic for equal finalized IR, artifact evidence,
and presentation options. Collision suffixes describe C# binding conflicts;
they are not confidence values or source ordinals.

## C# binding relation

The interference relation is the C#
[local-variable declaration-space contract][csharp-declaration-spaces], not
physical live-range overlap and not string equality alone.

- Two exact names may be reused when their emitted declaration spaces are
  disjoint.
- The same name cannot be allocated where one declaration space contains the
  other or where another binder reserves it under C# rules.
- PDB-disjoint ranges do not authorize reuse when final emitted declarations
  overlap.
- A raised lambda or local function has its own local identity and declaration
  domain. Exact nested parameters and locals may reuse a non-captured enclosing
  parameter or local name where C# permits it.
- Same-list duplicates, a collision with an actually referenced captured
  binder, and flattened local-function declaration conflicts remain illegal.
  Approximate and generated names conservatively avoid enclosing and descendant
  binders rather than introducing optional shadowing.

The planner operates on typed binding keys throughout. Rendered identifier text
is an output checked for legality, never an input used to rediscover ownership.

[csharp-declaration-spaces]: https://github.com/dotnet/csharpstandard/blob/df649b5d1ed2b68126128ab6af4c2b4f58b4c0ca/standard/basic-concepts.md#L84-L91

## Closed results and failure

For each retained materialized local, planning produces:

- its finalized body-local binding key;
- its declaration owner and emitted declaration scope;
- its final identifier plus exact, approximate, synthesized, or fallback
  provenance;
- its exact-name disposition when artifact identity exists; and
- any structured fidelity cause or Applied Taste decision required by the
  owning preservation contract.

A successful result is complete for its body. Missing ownership for one
retained reference, an illegal binding, stale correspondence, or an
unrepresentable declaration cannot produce a partially usable success-shaped
plan. The Decompiler keeps the prior valid representation where its existing
contract permits that fallback; otherwise it reports the typed unsupported or
failure result and degrades visibly.

Eliminated locals have no emitted declaration and no presentation name. Their
absence is established by the transformation that eliminated them, not inferred
from an empty planner result.

## Invariants

For a successful plan:

1. Every emitted materialized-local reference resolves to one plan entry in
   its owning body.
2. Every retained plan entry has exactly one declaration owner.
3. The emitted declaration scope contains every reference assigned to that
   body-local binding; none lies outside.
4. All final identifiers are legal and collision-free under the complete C#
   binder relation.
5. Exact local identity is emitted only when its artifact binding is preserved;
   fallback provenance and fidelity remain explicit otherwise.
6. Declaration planning does not change control flow, storage identity,
   evaluation order, exception regions, or initialization semantics.
7. Planning the same finalized body with the same options produces the same
   result.

The first production adoption gates the declaration-ownership subset in
Release: `PdbLocalDeclarationScopeTests` checks that the plan owns
materialized-local declarations, excludes residual stack slots, covers raised
nested bodies, and supplies the emitted scopes consumed by exact-name
allocation. Existing output behavior remains covered by
`PdbLocalNameScopeTests`, `PdbLocalScopeFidelityTests`,
`NestedScopeNameCollisionTests`, `ReadableLocalNamesTests`, and
`ByteNeutralityGateTests`. Completing final approximate, synthesized, and
fallback allocation as one closed plan remains **unverified** until later
adoption slices.

An eventual claim that no semantic declaration or local-name decision remains
in the printer is a composition absence claim. Before making it, the operator
must choose full, partial, or no automated coverage under
[Evidence and validation](../evidence-and-validation.md). This document does
not preselect that coverage.

## Production adoption

The shared `ILInspector.Decompiler` pipeline issues the plan. CLI and
Browser/Wasm consumers render the same host-neutral result; a host may select
documented presentation options but does not recompute declaration ownership or
bindings.

The first adoption covers already-materialized locals in methods, raised
lambdas, and raised local functions. It must:

- preserve current default and opt-in rendered behavior and byte neutrality;
- preserve exact-name fidelity and Applied Taste disclosure;
- remove the pipeline-to-printer declaration-analysis dependency; and
- leave residual stack slots on the explicitly named #2095 path.

A side-by-side computation is bounded migration evidence. It is not a
long-lived second authority: the adoption slice names the production consumer
and removes the replaced decision path.

The first production consumer is `CSharpPrinter`: it consumes the
pipeline-owned `LocalDeclarationPlan` for materialized-local declaring stores,
syntax-owned declarations, verified `out` declarations, unsafe-placement
dispositions, and exact emitted scopes. `ExactLocalNameAllocation` and
`PdbLocalScopePass` consume those same emitted scopes directly. The former
printer callback and its duplicate materialized-local collection path have
been removed; residual `StoreStackSlot` declaration handling remains in the
printer for the #2095 adoption.

## Pathological case

The pinned real witness is `dotnet-inspect.any` 0.14.0
`ObjectInitializerPass.Apply`.

Its final tree contains an exact nested local named `initializer` and two
logical locals whose exact PDB identities also request `initializer`, but whose
emitted declaration scopes overlap that exact binding.

```csharp
ObjectInitializerExpression initializer_1;
ObjectInitializerExpression initializer_2;

{
    ObjectInitializerExpression initializer =
        new(plan.Creation, plan.IsCollection, entries);
    stackSlot.Use.ReplaceWith(initializer);
}
```

Strict presentation uses readable synthesized names for the two ambiguous
bindings and reports the exact-identity loss. The explicit approximate option
uses `initializer_1` and `initializer_2`; the nested exact local keeps
`initializer`, and fidelity remains unchanged. The plan must explain all three
bindings without treating the shared text or physical slots as identity.

Neighboring exact evidence comes from the compiler-produced disjoint-scope
fixtures and the scope-entry projection fixtures. The close negative remains
`SequentialStackCarry`, where raised uses overlap despite disjoint PDB ranges.

## Analogous designs

Current ILSpy provides the strongest open implementation analogy:

- [`AssignVariableNames` is the final standard IL transform][ilspy-pipeline],
  after variable splitting and control-flow/nested-function transforms;
- [`DeclareVariables`][ilspy-declarations] later computes declaration
  insertion from the generated C# AST and typed `ILVariable` annotations; and
- rendering happens afterward through
  [`CSharpOutputVisitor`][ilspy-output].

This supports the ordering and printer separation, not wholesale transfer of
its model. ILSpy's debug-info interface exposes slot-indexed names rather than
dotnet-inspect's exact PDB row/scope identity, and some split variables
deliberately share naming identity by original slot.

dnSpy's current decompiler pins an older ILSpy-derived implementation. It also
names before AST construction and sinks declarations in a late AST pass, but
its declaration analysis can discover uses by name text and its allocation is
conservatively method-wide. It is product evidence, not an independent
algorithmic lineage and not a suitable identity model
([pipeline][dnspy-pipeline], [declaration placement][dnspy-declarations]).

dotnet-inspect deliberately combines the conventional late-allocation shape
with stricter typed identity, exact PDB scope evidence, visible declines, and
scope-aware reuse. No implementation is transferred from either codebase.

[ilspy-pipeline]: https://github.com/icsharpcode/ILSpy/blob/fe393d01d19bcc9cc3d7d03f4052a8e8afbd0080/ICSharpCode.Decompiler/CSharp/CSharpDecompiler.cs#L89-L179
[ilspy-declarations]: https://github.com/icsharpcode/ILSpy/blob/fe393d01d19bcc9cc3d7d03f4052a8e8afbd0080/ICSharpCode.Decompiler/CSharp/Transforms/DeclareVariables.cs#L84-L169
[ilspy-output]: https://github.com/icsharpcode/ILSpy/blob/fe393d01d19bcc9cc3d7d03f4052a8e8afbd0080/ICSharpCode.Decompiler/CSharp/CSharpDecompiler.cs#L1580-L1594
[dnspy-pipeline]: https://github.com/dnSpyEx/ILSpy/blob/b063cd99fbfc052a022634a1efb5dd2ddddb7fbb/ICSharpCode.Decompiler/Ast/Transforms/TransformationPipeline.cs#L35-L52
[dnspy-declarations]: https://github.com/dnSpyEx/ILSpy/blob/b063cd99fbfc052a022634a1efb5dd2ddddb7fbb/ICSharpCode.Decompiler/Ast/Transforms/DeclareVariables.cs#L62-L123

## Non-claims

- No method-wide scope solver or replacement for existing exact-scope passes.
- No stack-slot splitting, typing, coalescing, or materialization contract.
- No new liveness, reaching-definition, dominance, or definite-assignment
  service without a demonstrated consumer.
- No new `= default`, `scoped`, unsafe-context, or ref-local policy.
- No inference of source syntax from PDB ranges.
- No promise that every exact PDB identity is representable in C#.
- No stable local key across body mutation without explicit correspondence.
- No Roslyn dependency, inspected-assembly loading, or host-specific planning
  path.

Related: [#8111](https://github.com/richlander/dotnet-inspect/issues/8111),
[#2095](https://github.com/richlander/dotnet-inspect/issues/2095), and
[#8036](https://github.com/richlander/dotnet-inspect/issues/8036).
