# Analysis local-throw evidence

Status: Analysis implementation
([#6961](https://github.com/richlander/dotnet-inspect/issues/6961)).

## Owner and claim

**ILInspector.Analysis owns local-throw evidence.** A known target means that
one proven object construction supplies the value consumed by a physical
`throw`, and that its type is qualified as `System.Exception` or a derived
type. An unresolved site or uninspected body is not a complete empty result.

This is a bounded static body claim, not a claim that execution reaches the
instruction or that the exception escapes its method. The contract gate is the
Release `LocalThrowEvidenceTests` suite.

[Method-body inspection](method-body-inspection.md) owns acquisition and the
shared decoded body. [Subject Relations](subject-relations-workflows.md) owns
the eventual member-to-exception relation and query semantics. This document
does not redefine either contract.

## Motivating asset and prior art

In dotnet/runtime's .NET 10.0.10 `System.Private.CoreLib`,
`ArgumentNullException.Throw(string)` constructs `ArgumentNullException` and
immediately throws it. The selected `ThrowIfNull` overload calls that helper
but has no local throw. This distinction motivates retaining physical body
evidence instead of inferring it from names, signatures, or call reachability.
`RuntimeThrowAndThrowIfNullPreserveRealAssetDistinction` preserves the same
scenario against the repository-selected SDK's CoreLib, rather than depending
on a separately installed .NET 10 runtime.

The existing `Throws` signal counts throw instructions, while `ExceptionTypes`
classifies constructions. Their aggregate conjunction does not associate a
construction with a throw operand. The existing allocation "feeds throw soon"
probe is likewise a proximity heuristic, not value provenance.

Analysis already has typed-stack and reaching-definition-backed
`ResolvedValueSet` evidence. This producer consumes that evidence and the
existing caller-authorized metadata binding policy. It does not establish a
second value interpreter or assembly acquisition policy.

## Result and identity

Local throws are separately requested through `LibraryBodyAnalysisFeatures`.
They do not enter `Default`, and requesting them does not implicitly request
JSON call-argument or return-flow projections. Access when unrequested fails
explicitly.

Each result identifies a physical MethodDef token within the index's
image-derived `LibraryBodyModuleIdentity`. An inspected body also retains its
`MethodIdentity`. Each site retains its IL offset and distinguishes `throw`
from `rethrow`.

A known type retains the original constructed `TypeRef`, construction offset,
constructor operand token, and the qualified definition's owner-issued
`MetadataTypeDefinitionAddress`. The reference origin remains associated with
the producing image. The address records which definition was qualified; it
is not a logical correspondence token. Neither a display name, semantic facade
normalization, nor MVID equality establishes cross-image correspondence.
Consumers use the existing Metadata correspondence contract and retain this
qualification association rather than silently rebinding to another definition.

Results remain attached to the physical body. A compiler-generated body is not
silently relabeled as its async kickoff, iterator, or enclosing source method.

## Qualification and uncertainty

The initial supported value is a single proven `newobj` source constructing a
non-generic named type. Generic constructions remain explicitly unresolved.
Supported transparent forwarding uses the shared provenance rules, including unaddressed
locals, duplication, and casts. Multiple possible producers remain unresolved,
even if their display names agree. No base-type widening turns an exact
`ArgumentNullException` target into an `ArgumentException` target.

Qualification follows metadata base types to the framework-qualified
`System.Exception` anchor. External definitions require the caller's existing
resolution authority. Missing definitions, unsupported origins, cycles, and
exhausted relationship bounds withhold qualification. Exception-name suffixes
and arbitrary constructor return declarations are not qualification.

The following are explicit unresolved sites, not guessed exception targets:

- Unknown, addressed, or unsupported value provenance.
- Multiple construction sources.
- Argument, field, arbitrary call-result, and null operands.
- `rethrow`, whose caught value is not established by this producer.
- An unresolved constructed type or exception ancestry.
- A constructed value established not to be an exception.

A locally caught throw is still a local throw. Catch declarations,
construction-only bodies, and calls to throwing helpers do not create sites.
Runtime wrapping of non-exception objects and the implicit exception from a
null operand are outside this initial qualification contract.

An inspected body is complete only when every local site has a known type.
An inspected body with no sites is complete for this local claim. Excluded
scope, absent managed bodies, reference-assembly inputs, and acquisition or
analysis failures are explicit unavailable outcomes. Recoverable failures
retain already-produced sites alongside the existing Analysis diagnostic.
The legacy construction-name census must also terminate on cyclic local
ancestry so it cannot prevent this producer from reporting an unresolved site.
Its existing acyclic, name-based signal interpretation is unchanged.

## Adoption and gates

This is the Analysis producer slice of the
[Find/Relations delivery plan](subject-relations-workflows.md#adoption-and-retirement)
and [#6761](https://github.com/richlander/dotnet-inspect/issues/6761): step 8
of 16. The named consumer is shared Subject Relations composition (step 11),
followed by typed section/Markout lowering (12), definitions and portability
(13), CLI (14), Browser/Wasm (15), and old-command retirement after parity
(16). This slice does not ship `--throws`, change section discovery, or add
host-specific rendering.

The producer inherits Analysis's platform and acquisition contracts. It
requires no Decompiler exception-flow catalog: that catalog owns normal
transfer and cleanup, not the value consumed by a throw.

Focused Release gates cover the real construction/helper distinction,
transparent and overwritten values, multiple sources, caught throws,
rethrows, unresolved and non-exception types, scope, absent and reference
bodies, and explicit feature selection. Existing resolved-value tests cover
preservation of the shared producer behavior. The new tests are PR-fast;
classification follows measured xUnit timings rather than the size of the
containing assembly. Cyclic ancestry is exercised through the public
prefetched-image entry point, including the acquisition-time signal census.
