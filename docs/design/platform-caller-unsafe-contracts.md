# Platform caller-unsafe contracts

Status: Proposed. Lands the inventory's named successor for cross-assembly
explicit-contract consumption, scoped to .NET platform members, for
[#5254](https://github.com/richlander/dotnet-inspect/issues/5254).

## Owner and claim

**ILInspector.Analysis owns platform caller-unsafe contract consumption.** A
call from an inspected body to a .NET platform member counts as an
explicit-contract call in the
[unsafe-member inventory](method-body-inspection.md#updated-semantics-unsafe-member-uses)
when that member carries an explicit updated-model caller-unsafe contract in
upstream's published reference assemblies. The repository records those
contracts as a committed, generated projection of one exact reference pack,
and every role admitted through it names that pack and version.

The inventory owner keeps its admission rule; this document adds a source of
explicit contracts for cross-assembly call targets. It does not redefine
attribution, exposure, or completeness, which
[Unsafe member findings](unsafe-member-findings.md) own.

## Motivating asset

`Unsafe.As<T>(object)` is the canonical caller-unsafe API. Under the merged
inventory, a library method whose only unsafe work is a call to
`Unsafe.As`, `MemoryMarshal.GetReference`, or `Marshal.ReadInt32` has no role
and no finding, because the first slice resolves explicit contracts only within
the primary image. Reading the callee's own metadata cannot close the gap yet:
the .NET 11 implementation assemblies that a library binds to at run time,
including `System.Private.CoreLib`, carry no caller-unsafe markers.

## The upstream fact

[dotnet/runtime#131733](https://github.com/dotnet/runtime/pull/131733) compiles
reference assemblies under the updated memory-safety rules, applying the
approved proposals for `Unsafe`
([#126956](https://github.com/dotnet/runtime/issues/126956)), `MemoryMarshal`
([#127098](https://github.com/dotnet/runtime/issues/127098)), `Vector`
([#128075](https://github.com/dotnet/runtime/issues/128075)), and interop
([#129750](https://github.com/dotnet/runtime/issues/129750)). The published
`Microsoft.NETCore.App.Ref` 11.0.0-rc.1.26425.128 carries the result: nine
assemblies declare `MemorySafetyRulesAttribute`, and 2,327 methods carry
`RequiresUnsafeAttribute`.

| Reference assembly | Marked methods | Largest types |
| --- | --- | --- |
| `System.Runtime.Intrinsics` | 1,973 | `Sve` 487, `AdvSimd.Arm64` 305, `AdvSimd` 235 |
| `System.Runtime.InteropServices` | 159 | `Marshal` 97, `ComWrappers` 9 |
| `System.Runtime` | 133 | `Unsafe` 36, `MemoryMarshal` 23, `SafeBuffer` 7 |
| `System.Numerics.Vectors` | 47 | `Vector` 29 |
| Five other assemblies | 15 | `RSAOpenSsl` and peers, `ISymbolWriter` |

`Microsoft.AspNetCore.App.Ref` of the same release marks none. The markers are
pre-release contracts: upstream leaves nine `Unsafe` methods unmarked today and
may change any of them before release.

Pruning has the same shape: the authoritative fact is published in a reference
pack, and [Platform/package pruning](platform-package-pruning.md#data-acquisition)
reads that pack in an explicit generator and commits the projection rather
than acquiring it at inspection time.

## Contract identity

A projection entry is the documentation-comment identifier of one marked
method, for example
```M:System.Runtime.CompilerServices.Unsafe.As``1(System.Object)```,
together with the reference assembly that declared it. Property and event
accessors are their accessor methods; the projection does not add a contract to
a property whose accessor is unmarked.

A call matches an entry when both hold:

- the callee, reduced from a `MethodSpec` to its generic method definition and
  from a constructed declaring type to its definition, has the entry's
  identifier; and
- the callee's declaring type is defined in the primary image or referenced
  through an `AssemblyRef`, and that assembly's public key token is a .NET
  platform token: `7cec85d7bea7798e` (`System.Private.*`), `b03f5f7f11d50a3a`
  (`System.Runtime` and most platform assemblies), `cc7b13ffcd2ddd51`
  (`netstandard`, `System.Memory`, and other .NET Standard-era assemblies), or
  `b77a5c561934e089` (`mscorlib` and `System`).

The match is by type and member identity, not by assembly name, because one
platform type is referenced through different assemblies: compiled against the
reference pack, `Unsafe` comes from `System.Runtime`; inside the shared
framework it is defined in `System.Private.CoreLib`; a .NET Standard library
reaches `MemoryMarshal` through `System.Memory` and `Marshal` through
`netstandard`. All name the same contract. This slice reads no callee image, so
the token is the `AssemblyRef`'s, not one read from a resolved file. A
same-named type in an assembly with any other token never matches. A token is
shared with some non-platform Microsoft packages, but a match still requires
the exact identifier of a platform-defined member, so such a package matches
only where it carries that platform type itself, as the
`System.Runtime.CompilerServices.Unsafe` package does.

## Contract source

A matched call is an `ExplicitContractCall` role at its IL offset with the
callee in its detail. Each `ExplicitContractCall` evidence item carries a typed
contract source, not text in its detail:

- `SameImage`: the callee is a primary-image definition whose own metadata
  carries the marker; or
- `PlatformProjection`: the projection supplied the contract, with its pack
  identity and version.

Other roles have no contract source. `UnsafeMemberUseEvidence` and the
`UnsafeMemberFinding` evidence that
[Unsafe member findings](unsafe-member-findings.md#finding-unit-and-identity)
retains both carry the source through. A host that renders call evidence shows
a projected source distinctly from a same-image one, with the pack version; the
CLI `Unsafe Members` section and the Inspect Web Unsafe tab adopt this when
they render the census.

When the primary image declares `MemorySafetyRulesAttribute`, its own markers
are authoritative for its own definitions: a same-image callee is either
`SameImage` or no contract, and the projection is not consulted. Otherwise, a
same-image callee carrying the marker is `SameImage`, and an unmarked one is
looked up in the projection.

## Applicability

The inventory applies updated language semantics to every input assembly, so
the projection applies whatever platform version the input targets: a role
states that the call requires an unsafe context under the projected platform's
contracts, not under the platform the assembly was compiled against. The
contract source keeps that distinction visible.

Inspecting a .NET 11 platform implementation image is no exception: inside
`System.Private.CoreLib`, which does not declare `MemorySafetyRulesAttribute`,
a call to `Unsafe.As` is same-image, the callee is unmarked, and the projection
supplies the contract. The projection adds no
declaration role: CoreLib's own `Unsafe.As` does not become a finding for
having a projected contract.

## Missing data must not become no

The projection is a positive source only. A platform member absent from it is
not asserted safe, and the inventory's absence of a role for a call to it is
not a negative claim. A pack that cannot be read fails generation; it never
produces an empty projection.

The projection does not hand-edit upstream. It adds no member that upstream
leaves unmarked and removes none that upstream marks.

## Data acquisition

`eng/generate-platform-caller-unsafe-contracts.cs` reads an exact
`Microsoft.NETCore.App.Ref` directory and writes the committed projection that
Analysis embeds. The projection records the pack identity and version, the
number of entries, and a content digest, then one sorted
`assembly<TAB>identifier` line per entry. Generation fails when a pack assembly
cannot be read, when a marked method appears in an assembly that lacks
`MemorySafetyRulesAttribute`, or when two entries collide.

The projection tracks the repository's pinned SDK reference pack. Moving the
pin regenerates the projection in the same change, so review sees contract
changes beside the version that caused them; a gate enforces that the recorded
pack version is the pinned SDK's reference-pack version. No inspection-time download or
pack probe is added, and the data loads lazily only when the inventory runs.

## Retirement

The projection is a bridge until implementation assemblies carry the markers,
expected with .NET 12. Exact contract consumption for a resolved callee image,
platform or not, is the general cross-assembly successor; once it lands,
callee metadata is authoritative for any image that declares
`MemorySafetyRulesAttribute`, and the projection is consulted only for older
platform images.

## Non-claims

- **Third-party contracts.** Calls into non-platform assemblies keep the
  inventory's existing non-claim.
- **Other platform packs.** ASP.NET Core marks nothing in this release;
  Windows Desktop has no pinned pack. Either joins by regeneration when it
  publishes markers.
- **Platform declarations.** A projected contract adds no declaration role and
  no propagation.
- **Per-target exactness.** The projection is one pack version; it does not
  select contracts by the input's target framework.

## Gates

Focused Release gates in `ILInspector.Analysis.Tests`:

| Property | Gate |
| --- | --- |
| A call to a projected platform member is an explicit-contract call with a typed `PlatformProjection` source that reaches its finding | `PlatformContractCallIsAnExplicitContractCall` |
| A marked same-image callee keeps a `SameImage` source | `SameImageContractKeepsItsSource` |
| `System.Runtime`, `System.Private.CoreLib`, `netstandard`, and `System.Memory` references to one member match the same entry | `PlatformContractMatchesAcrossForwarding` |
| A generic method instantiation and a constructed declaring type match their definition | `PlatformContractMatchesGenericDefinitions` |
| A same-named type without a platform key does not match | `NonPlatformLookalikeDoesNotMatch` |
| An unprojected platform member admits nothing | `UnprojectedPlatformMemberAdmitsNothing` |
| An image declaring `MemorySafetyRulesAttribute` is authoritative for its own unmarked members | `UpdatedRulesImageIsAuthoritativeForItsMembers` |
| A projected contract adds no declaration role | `ProjectionAddsNoDeclarationRole` |
| The committed projection matches its recorded digest and count | `ProjectionMatchesItsHeader` |
| The recorded pack version is the pinned SDK's reference-pack version | `ProjectionTracksPinnedSdk` |
| An unreadable pack assembly fails generation | `GenerationFailsOnUnreadableAssembly` |
| A marker in an assembly without `MemorySafetyRulesAttribute` fails generation | `GenerationFailsOnMarkerWithoutRules` |
| Colliding entries fail generation | `GenerationFailsOnCollidingEntries` |

The generation gates run the projection builder that the generator calls over
synthetic packs. NativeAOT binary-size and `Unsafe Members` timing deltas
are reported for the implementing PR.
