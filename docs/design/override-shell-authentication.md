# Override-shell authentication

> The owning design for deciding when metadata proves that a reconstructed
> compile-back shell may emit a C# `override`, retain a base constructor call,
> or inherit property accessibility from a base declaration.

This document owns the authentication decisions shared by Metadata and the
ReturnToSender compile-back planner. The extraction and resolution machinery
that supplies those decisions remains in
[assembly inspection query](assembly-inspection-query.md). The immutable source
artifact and compile-back experiment remain in
[the decompiler correctness pipeline](../decompiler-correctness-pipeline.md).

## The decision in one paragraph

A source-level relationship is emitted only after Metadata authenticates its
metadata relationship and complete structural shape. Current-image evidence is
fail-closed: every definition needed to decide the relationship must resolve
uniquely, including definitions nested under arrays, generic arguments,
modifiers, pinning, pointers, byrefs, and function pointers. Exact external
evidence is different: once the same-assembly slot itself is authenticated,
hierarchy or variance that is unavailable outside the image remains `Unknown`
and may be validated by the C# compiler. `Unknown` is never a substitute for
missing or ambiguous current-image evidence. The harness consumes these
product-owned decisions; it does not reconstruct them from names or rendered
text.

The enforcement map at the end of this document names the tests that gate each
property.

## Scope

This design owns:

- same-assembly class `MethodImpl` authentication for source-level override
  methods, properties, and indexers;
- exact parameter correspondence and covariant return compatibility;
- generic-parameter and constructed-generic conversion evidence;
- complete CLI constructor-shape authentication used by compile-back planning;
- property accessibility inherited through authenticated override chains;
- the boundary between product-owned evidence, harness planning, C# rendering,
  and compiler validation; and
- fail-closed behavior for malformed, cyclic, ambiguous, or over-budget
  metadata encountered on those paths.

This design does not own:

- general API extraction or cross-assembly acquisition;
- source rendering or C# syntax policy;
- method-body decompilation and raising;
- arbitrary CLR assignability;
- hostile in-process callers bypassing the typed product APIs; or
- a promise that every valid CLR relationship is expressible in C#.

## Ownership pipeline

| Stage | Owner | Authoritative input and output |
| --- | --- | --- |
| Relationship evidence | `ILInspector.Metadata` | SRM rows, `TypeNode`, exact handles, typed provenance, authenticated slot or refusal |
| External base facts | `ILInspector.Metadata` | Frozen resolution evidence, typed base facts, no reader or catalog handle leakage |
| Closure and target planning | ReturnToSender in `tools/DecompilerHarness` | Product-owned facts and exact metadata identities, producing typed CSharp requests |
| Source composition | `ILInspector.CSharp` | Typed requests, producing the immutable compile-back shell |
| Final external validation | C# compiler | Only relationships already authenticated except for explicitly retained external `Unknown` compatibility |

Rendered names are presentation at every stage. They never prove definition
identity, signature correspondence, accessibility, or constructor shape.

## Authentication states

The implementation uses a three-way compatibility result because absence of
local proof has two materially different meanings.

| State | Meaning | Consequence |
| --- | --- | --- |
| `Compatible` | The current image proves the required correspondence or conversion. | The authenticated relationship may be retained. |
| `Incompatible` | The image disproves the relationship, the shapes differ, or required current-image evidence is unavailable or ambiguous. | Decline the source-level relationship. |
| `Unknown` | The slot and exact external identities are authenticated, but external hierarchy or variance is not available in the current image. | Preserve the relationship for compiler validation. |

`Unknown` is allowed only after the `MethodImpl` slot, declaration shape, and
all current-image dependencies have authenticated. A same-spelled local type,
an unresolved current-image definition, malformed metadata, or a scope
mismatch is `Incompatible`, not `Unknown`.

## Slot authentication

A virtual `NewSlot` method or property accessor is reconstructed as a source
`override` only when all of the following hold:

1. An unambiguous class `MethodImpl` maps the body to a virtual declaration on
   the base-class chain.
2. The declaration is source-declarable and has compatible accessibility and
   static/instance shape.
3. Parameters correspond exactly by scoped structural identity and
   ref/out/in shape.
4. Return modifiers correspond exactly.
5. Return types are equal or the compatibility matrix below permits a
   covariant conversion.

An interface declaration, unrelated class declaration, ambiguous declaration,
or unavailable declaration does not materialize a class override slot.
Metadata tokens deduplicate the target and synthesized slot first; structural
shape is only the fallback when no shared token exists.

The base-class chain is a metadata walk, not a `TypeDef` walk. A compiler emits
`Derived : Base<string>` and `Derived<T> : Base<T>` with a `TypeSpec` `extends`
row, and emits the covariant-return `MethodImpl` declaration as a `MemberRef`
rooted in that `TypeSpec` rather than as a `MethodDef`. Metadata therefore walks
same-image `TypeDef` and constructed-generic `TypeSpec` bases alike, carrying
each step's exact generic arguments and substituting them into the declaration's
decoded signature, so the slot is authenticated by definition token and
substituted structural identity. A `TypeSpec` that is not a generic
instantiation of a definition in this image, a `MemberRef` that does not resolve
to exactly one method on an authenticated chain step, and an instantiation whose
arguments do not correspond all decline. No step matches a rendered name.

A reconstructed shell spells a same-assembly base reached directly, and one
reached through a constructed generic `TypeSpec` when the same Metadata-owned
traversal resolves it to a definition in this image *and* the derived type reuses
a virtual slot it did not introduce. A constructed base is otherwise left
dropped, because a closed instantiation whose only constructor is parameterized
carries the same implicit-`base()` exposure an external base does. An external
base is always dropped because the shell cannot own its construction, and
dropping a base drops `override` from every member whose slot that base owned.

An `Object` intrinsic slot is authenticated the same way. `ToString`,
`GetHashCode`, and `Equals(object)` reuse `System.Object`'s slot only when the
method is non-static, non-`NewSlot`, non-generic, its signature matches the
intrinsic read from primitive element types rather than from a rendered name,
and its same-image base chain terminates at the strong-name-authenticated
corelib `System.Object`. A base that leaves the image is not proof of the
`Object` slot, because that base may declare its own `NewSlot` virtual of the
same shape.

The full member-surface pass applies the same authentication to unselected
members. Reconstructing only the selected target must not sever another
authenticated slot needed for the type to compile.

## Structural identity

Parameters and equal returns use complete scoped `TypeNode` identities. The
identity retains:

- namespace and root-to-leaf nested segments;
- exact non-platform assembly or module scope;
- typed trusted-platform normalization as a separate projection;
- raw class/value-type kind and introduced arity;
- every generic argument and encoded argument count;
- SZ-array versus multidimensional-array shape, including rank, sizes, and
  lower bounds;
- pointer, byref, pinned, and required/optional modifier structure; and
- function-pointer calling convention, generic arity, return, parameters, and
  modifiers.

Trusted-platform normalization may replace only independently authenticated
platform scopes. It never erases a namespace, nesting boundary, argument,
modifier, raw type kind, function-pointer header, or non-platform scope.

Every current-image named definition reachable through the structural tree must
resolve uniquely. Wrapper nodes preserve their own shape while current-image
fail-closed inspection descends into their children.

## Return compatibility matrix

In the table, *implementation* is the reconstructed member and *declaration* is
the authenticated base slot.

| Implementation and declaration shape | Required evidence | Result |
| --- | --- | --- |
| Exact structural identities | Exact or permitted platform-normalized correspondence | `Compatible` |
| One side modified or pinned | Both sides have the same wrapper kind; modifiers correspond; recurse into the inner type | Inner result, otherwise `Incompatible` |
| SZ arrays | Both are SZ arrays; both element types are authenticated reference types; recurse covariantly on elements | Element result |
| Multidimensional arrays | Both are MD arrays with identical rank, sizes, and lower bounds; both elements are authenticated reference types; recurse covariantly | Element result |
| Constructed local generics | One exact local generic definition resolves uniquely; arity and every argument correspond under its declared variance | Aggregate argument result |
| Constructed external generics | Definition identities correspond, no current-image dependency is missing, and local variance metadata is unavailable | `Unknown` for compiler validation |
| Local named types | Both exact definitions resolve uniquely | `Compatible` only when the implementation is the same as or derives from or implements the declaration |
| External named types | Exact scopes authenticate but hierarchy is unavailable | `Unknown`, unless same-name/different-scope evidence disproves correspondence |
| Generic implementation parameter | The generic-parameter rules below establish equality or conversion | Rule result |
| Generic declaration parameter only | No covariant conversion can be proved | `Incompatible` |
| Value or degraded shape | No authenticated reference conversion exists | `Incompatible` |

Invariant generic arguments require exact structural correspondence. Covariant
and contravariant arguments recurse in their declared direction. A local
same-spelled generic definition never supplies variance for an external
definition.

## Generic-parameter evidence

A generic implementation return accepts:

- exact positional identity;
- conversion to `object` when the reference-type constraint or an authenticated
  class-constraint chain proves reference type; or
- conversion established by a typed explicit class or interface constraint
  reachable in the local metadata.

The reference-type flag alone proves conversion only to `object`; it does not
prove conversion to an arbitrary class. Absence of that flag does not prove
value type.

Array covariance needs stronger evidence because a value type can implement an
interface. An explicit constraint proves a generic array element is a reference
type only when Metadata authenticates the constraint as a class. An interface
constraint may establish conversion to that interface, but it does not
establish reference-ness for array covariance.

Constructed constraints retain the exact generic definition and every
argument. Local variance is read only from the uniquely resolved local
definition. Exact external constructed constraints may remain `Unknown`; a
non-generic or differently shaped candidate remains `Incompatible`.

Generic-parameter traversal tracks active handles. A cycle, malformed
constraint, degraded decode, or exhausted relationship budget fails closed.

## Constructor authentication

Metadata owns the reusable CLI constructor predicate. A method is an instance
constructor only when all of these hold:

- the name is exactly `.ctor`;
- `SpecialName` and `RTSpecialName` are present;
- the method is not static;
- the signature is a default managed method signature with instance `this` and
  no explicit `this`;
- neither the signature nor the metadata rows declare generic parameters; and
- the return type is `void`.

The planner applies parameter and accessibility matching only after that
predicate succeeds, on every path that discovers a constructor: primary
constructor detection, instance-constructor counting, member requirements, and
the full member-surface pass. A method with the right name or rendered
parameters but an invalid constructor shape is not a constructor, and is skipped
rather than reclassified as an ordinary method.

External base-definition extraction exports only authenticated, non-generic,
public-class facts and accessible parameterless-constructor availability.
Private base constructors are eligible for an explicit initializer only when
the derived type's exact metadata identity is transitively nested in the
constructor's declaring type. The reverse direction and unrelated types remain
inaccessible.

A synthetic parameterless constructor is emitted only when the C# member set
does not already contain a retained parameterless instance constructor.
Flattened shells that omit the base list also omit an explicit base initializer.

## Property accessibility

Property accessibility begins with the best accessor present on the property.
When only one accessor exists, Metadata may follow its authenticated override
slot to the base property to recover the source-level property accessibility.

The traversal is iterative, tracks visited accessor handles, and is bounded by
the metadata relationship limit. A self-base cycle, mutual-base cycle,
malformed property map, or exhausted bound fails closed. A missing initial slot
provides no inherited accessibility. After at least one authenticated hop, the
absence of another slot terminates the chain at the best accessor on that
authenticated base property. A valid acyclic single-accessor chain may also
terminate at a normal two-accessor property.

## Harness and compiler boundary

ReturnToSender may orchestrate closure discovery, select authenticated metadata
members, and choose body policy. It must call the Metadata-owned constructor
and override predicates rather than restating their metadata rules.

The harness does not:

- decide from a name and signature that a member occupies an `Object` intrinsic
  slot; that predicate is Metadata-owned, because a flattened external base can
  declare its own `NewSlot` virtual of the same shape and keeping `override`
  would silently rebind the member to `System.Object`;
- infer a relationship from rendered text;
- substitute a same-named type from another assembly;
- repair malformed metadata into a plausible relationship;
- parse source to construct or normalize the shell later compiled as product
  evidence; or
- treat compiler success as authentication of a slot the product did not
  identify.

The compiler is an independent validator only for exact external relationships
whose remaining compatibility is `Unknown`. Compiler rejection remains visible
as compile-back failure; it is not converted into a different shell.

## Failure and boundedness

All traversals use SRM and repository safety bounds. Cycles, malformed rows,
decode failures, ambiguous current-image definitions, and exhausted budgets
decline authentication.

Return-type compatibility is recursive over generic-parameter constraints, so
active-path cycle detection alone is not a bound: a constraint graph that is a
DAG rather than a cycle enumerates exponentially many acyclic paths, and a long
acyclic chain exhausts the stack before any node repeats. The comparison
therefore carries one budget for the whole comparison, charging cumulative
relationship work and capping recursion depth. Exhausting either budget refuses
the slot outright rather than yielding `Unknown`, because `Unknown` is the
retain-and-let-the-compiler-decide state and would turn budget exhaustion into a
success path. API extraction retains unavailable `MethodImpl`
provenance and a typed inspection failure where that surface promises
disclosure; the shell planner never converts unavailable evidence into a valid
override or constructor.

The shipped product path remains Roslyn-free and never loads the inspected
assembly. Roslyn participates only in the tools-only compile-back experiment.

## Enforcement map

### Slot and scoped identity

- `SameAssemblyOverrideSlot_UsesCompilerProducedCovariantMethodImpl`
- `PropertyDeclaration_UsesCompilerProducedCovariantMethodImpl`
- `SameAssemblyOverrideSlot_DeclinesInterfaceMethodImpl`
- `SameAssemblyOverrideSlot_DeclinesUnauthenticatedClassMethodImpl`
- `SameAssemblyOverrideSlot_AuthenticatesCompilerProducedScopedParameterIdentity`
- `SameAssemblyOverrideSlot_DeclinesSameFqnReturnFromDifferentAssemblies`
- `SameAssemblyOverrideSlot_DeclinesNestedVsNamespaceParameter`
- `SameAssemblyOverrideSlot_DeclinesIncompatibleCovariantReturn`
- `SameAssemblyOverrideSlot_DeclinesIncompatibleStructuredReturn`
- `CompileBackTargets_AllFullDoesNotDuplicateTargetedOverrideSlot`
- `CompileBackTargets_AllFullPreservesUnrelatedCovariantMethodImpl`

### Constructed generic bases and object intrinsic slots

- `SameAssemblyOverrideSlot_AuthenticatesConstructedGenericBaseCovariantMethodImpl`
- `SameAssemblyOverrideSlot_AuthenticatesConstructedGenericBaseSubstitutedParameter`
- `SameAssemblyOverrideSlot_AuthenticatesSyntheticConstructedGenericMethodImpl`
- `SameAssemblyOverrideSlot_DeclinesConstructedGenericMethodImplWithMismatchedInstantiation`
- `SameAssemblyOverrideSlot_DeclinesConstructedGenericMethodImplRootedInExternalDefinition`
- `AuthenticatedObjectSlotOverride_AcceptsSameImageChainToObject`
- `AuthenticatedObjectSlotOverride_DeclinesOverrideOfExternalBase`
- `AuthenticatedObjectSlotOverride_DeclinesNewSlotObjectShapedVirtual`
- `CompileBackTargets_PrefersSameAssemblyToStringSlotOverIntrinsicObjectShortcut`
- `CompileBackTargets_DoesNotRebindFlattenedExternalObjectShapedSlotToObject`
- `CompileBackTargets_AllFullPreservesReferenceConstrainedGenericCovariantMethodImpl`
- `CompileBackTargets_DoesNotReconstructGenericBaseClass`

### Comparison boundedness

- `SameAssemblyOverrideSlot_WideGenericParameterDagFailsClosedWithinBudget`
- `SameAssemblyOverrideSlot_DeepGenericParameterChainFailsClosed`
- `SameAssemblyOverrideSlot_DeepGenericParameterChainDoesNotCrashProcess`

### Wrappers, arrays, and local definition trust

- `SameAssemblyOverrideSlot_AllowsModifierWrappedExactLocalGenericCovariance`
- `SameAssemblyOverrideSlot_DeclinesModifierWrappedAmbiguousExactLocalGenericDefinition`
- `SameAssemblyOverrideSlot_DeclinesPinnedWrappedAmbiguousExactLocalGenericDefinition`
- `SameAssemblyOverrideSlot_AllowsMultiDimensionalArrayCovarianceWhenShapeMatches`
- `SameAssemblyOverrideSlot_DeclinesDifferentMultiDimensionalArrayShape`
- `SameAssemblyOverrideSlot_DeclinesAmbiguousExactLocalGenericDefinition`
- `SameAssemblyOverrideSlot_DeclinesArrayWrappedAmbiguousExactLocalGenericDefinition`

### Generic conversions and external unknowns

- `SameAssemblyOverrideSlot_AllowsReferenceConstrainedGenericCovariantMethodImpl`
- `SameAssemblyOverrideSlot_DeclinesReferenceConstrainedGenericToArbitraryClass`
- `SameAssemblyOverrideSlot_AllowsExplicitGenericBaseConstraint`
- `SameAssemblyOverrideSlot_DeclinesExplicitConstraintThatDoesNotReachReturn`
- `SameAssemblyOverrideSlot_AllowsExplicitClassConstrainedGenericArrayCovariance`
- `SameAssemblyOverrideSlot_DeclinesInterfaceConstrainedGenericArrayCovariance`
- `SameAssemblyOverrideSlot_DeclinesArrayCovarianceWhenExplicitConstraintDoesNotReachReturn`
- `SameAssemblyOverrideSlot_AllowsCovariantConstructedConstraint`
- `SameAssemblyOverrideSlot_DeclinesInvariantConstructedConstraint`
- `SameAssemblyOverrideSlot_AllowsCompilerProducedExternalConstructedConstraintVariance`
- `SameAssemblyOverrideSlot_DeclinesExternalConstructedConstraintForNonGenericCandidate`
- `SameAssemblyOverrideSlot_AllowsCompilerProducedNestedGenericVariance`
- `SameAssemblyOverrideSlot_UsesExactNestedGenericVarianceDefinition`
- `SameAssemblyOverrideSlot_AllowsCompilerProducedExternalGenericCovariance`
- `SameAssemblyOverrideSlot_DoesNotUseLocalVarianceForExternalGeneric`
- `CompileBackTargets_PreservesExternalGenericCovariantMethodImpl`

### Constructor planning

- `DirectDefinition_CarriesAccessibilityAndConstructorFacts`
- `Extract_DoesNotTreatNamedMethodAsConstructor`
- `FindAccessibleInstanceConstructor_ReturnsValidConstructor`
- `FindAccessibleInstanceConstructor_RejectsMalformedConstructorShapes`
- `CompileBackTargets_AllFullDoesNotDuplicatePrivateParameterlessConstructor`
- `CompileBackTargets_SelectedSynthesizesConstructorWhenMetadataSignatureIsOmitted`
- `CompileBackTargets_SelectedSynthesizesConstructorForOwnNestedDerivedType`
- `CompileBackTargets_PreservesPrivateEnclosingBaseConstructorForNestedDerived`
- `CompileBackTargets_DropsUnrelatedPrivateBaseConstructorInitializer`
- `CompileBackTargets_RecordShellDropsExplicitBaseInitializerWithDroppedBaseList`
- `CompileBackTargets_DoesNotPlanMalformedConstructorNamedMethod`

### Property traversal and malformed cycles

- `PropertyDeclaration_DerivesPropertyAccessibilityFromAuthenticatedBaseWhenOnlySetterIsPresent`
- `PropertyDeclaration_DerivesPropertyAccessibilityAcrossAcyclicSetterOnlyOverrideChain`
- `PropertyDeclaration_SelfBasePropertyCycleFailsClosed`
- `PropertyDeclaration_TwoTypePropertyCycleFailsClosed`

### End-to-end compile-back

- `CompileBackTargets_PreservesCompilerProducedCovariantMethodImpl`
- `CompileBackTargets_PreservesExternalCovariantMethodImpl`
- `CompileBackTargets_PreservesCovariantPropertyMethodImpl`
- `CompileBackTargets_PreservesCovariantIndexerMethodImpl`
- `CompileBackTargets_AllFullPreservesReferenceConstrainedGenericCovariantMethodImpl`
