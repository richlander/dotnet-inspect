# Decompiler fixture original-source plan

## Decision

Every compiler-produced decompiler fixture target will carry authoritative,
byte-verified original source. Source correspondence, rather than absence of
decompiler residue, will become the fixture-level proof for a `Fully raised`
claim.

This is a staged migration. Until source correspondence reaches complete
fixture coverage, reports may retain the explicitly labeled
`zero decompiler residue (V1 signal)`. Once a fixture target has verified source,
the source result takes precedence. The final gate removes the V1 fallback for
all compiler-produced fixture targets.

## Why source paths are insufficient

The repository already retains much of the source:

- `FixtureCatalog.SourcePaths()` discovers checked-in and linked `Compile`
  inputs for built fixture projects;
- 53 `GeneratedFixtureCatalog` definitions retain their source strings;
- `ReturnToSenderSourceProbe` parses fixture source and correlates authored
  members with compiled targets.

Those facts do not yet prove that a source file is the exact input that produced
a particular binary. A file can change after compilation, conditional symbols
can select a different body, linked inputs can be missed by directory scans, and
compiler-generated methods do not have a one-to-one authored declaration.
Test-local dynamic compilations also disappear after the test and are not
addressable through either catalog.

The required contract is therefore:

```text
compiled fixture target
  -> exact built assembly identity
  -> exact compiler input document and checksum
  -> one authored source member or a typed synthesized-owner relationship
  -> source-correspondence outcome
```

## Scope

### Required

Original-source coverage is required for every target used to make a
source-level decompiler quality claim from:

1. the 15 registered `FixtureCatalog.DecompilerFixtures` entries;
2. decompiler-tagged version-pair fixtures such as `DiffV1` and `DiffV2`;
3. all `GeneratedFixtureCatalog` targets;
4. compiler-produced assemblies currently created inside decompiler tests;
5. future compiler-lowering fixtures.

The opt-in .NET fixture assembly must be registered in `FixtureCatalog` rather
than remaining a special corpus path.

### Not source-level targets

Synthetic IR, hand-authored invalid IL, malformed metadata, and impossible
control-flow seam fixtures do not have authoritative C# source by design. They
remain valid negative and invariant evidence, but must declare
`SourceApplicability.NotApplicable` with a reason and cannot produce a
`Fully raised` source claim.

Compiler-generated methods are not exempt. They map to an authored owner:

- async/iterator `MoveNext` maps to the kickoff method;
- synthesized accessors map to their property or event declaration;
- record-generated members map to the record declaration and a typed synthesis
  kind;
- closure/state-machine helpers map through their kickoff/containing member;
- a generated member without a proven owner is `Unresolved`, not source-covered.

## Authoritative source bundle

Each built fixture assembly will have a deterministic source bundle emitted by
MSBuild beside the DLL and portable PDB:

```text
<assembly>.fixture-source.json
<assembly>.fixture-source.zip
```

The manifest records:

- schema version and fixture ID;
- assembly name, SHA-256, MVID, target framework, configuration, and compiler
  version;
- language version, optimization, nullable mode, unsafe setting, deterministic
  setting, and conditional symbols;
- every evaluated `@(Compile)` item after imports and glob expansion;
- stable repository-relative/logical document path;
- SHA-256 and the portable-PDB document checksum algorithm/value;
- source-bundle entry name and byte length.

The ZIP contains the exact source bytes supplied to the compiler. It is an
evidence artifact, not a second editable source tree. Checked-in `.cs` files
remain the fixture source of truth.

The producer must consume evaluated MSBuild `@(Compile)` items. Directory
enumeration remains useful for discovery but is not authoritative because it
cannot represent conditions, removes, generated inputs, or all imported item
logic.

## Core contracts

The contracts should live with fixture infrastructure, without a Markout or
Decompiler dependency:

```csharp
public enum SourceApplicability
{
    Required,
    NotApplicable,
}

public sealed record FixtureSourceBundle(
    FixtureBinaryIdentity Binary,
    IReadOnlyList<FixtureSourceDocument> Documents,
    FixtureCompilationOptions Compilation);

public sealed record VerifiedFixtureSource(
    FixtureSourceDocument Document,
    ReadOnlyMemory<byte> Bytes,
    SourceVerification Verification);

public abstract record FixtureSourceOwner
{
    public sealed record Authored(MemberAnchor Member) : FixtureSourceOwner;
    public sealed record Synthesized(MemberAnchor Owner, SynthesizedMemberKind Kind)
        : FixtureSourceOwner;
    public sealed record Unresolved(string Reason) : FixtureSourceOwner;
}
```

`VerifiedFixtureSource` is returned only after all available bindings agree:

1. the requested assembly matches the manifest SHA-256 and MVID;
2. bundled bytes match the manifest SHA-256;
3. bundled bytes match the portable-PDB document checksum;
4. the source member maps to the compiled target's stable `MemberAnchor` or to
   a proven synthesized owner.

Decode, checksum, PDB, and identity failures stay visible as typed outcomes.
They must not become an empty source collection.

## Correspondence and Fully Raised

Source availability and source correspondence are separate results:

```csharp
public enum FixtureSourceCorrespondence
{
    Match,
    Different,
    InvalidOutput,
    SourceUnavailable,
    TargetUnresolved,
}
```

For the first source-backed gate:

```text
Fully raised = verified original source + source correspondence Match
```

`Full`, valid C#, compile-back `Exact`, and zero residue remain independent
evidence. None can upgrade `Different`, `SourceUnavailable`, or
`TargetUnresolved` to fully raised.

The initial `Match` policy should reuse the existing source probe's normalized
member-body comparison. Intentional product-owned taste differences remain
`Different` until a later contract represents typed equivalence. This keeps the
first source gate strict and understandable.

The report comparison schema should preserve the evidence basis:

```text
SourceCorrespondenceV1
ResidueZeroV1
NotEstablished
```

When source evidence exists, `SourceCorrespondenceV1` always wins over
`ResidueZeroV1`. The before/after renderer prints the basis next to the claim.

## Fast original-source compilation

An isolated method body is not a sufficient compilation unit. It loses fields,
overloads, generic constraints, containing types, aliases, usings, attributes,
partial declarations, and language/module options. The fast path therefore
compiles a **source island** whose default boundary is the complete authored
containing type.

The source island contains:

- every partial declaration of the target type;
- the complete chain of containing types;
- file/global usings, aliases, nullable directives, and required attributes;
- assembly/module attributes that affect the target semantics;
- referenced fixture types from their verified authored documents;
- the exact fixture compilation options from the source manifest.

The compiler runs once per source-island key and all target members in that
type reuse the result. The cache key is based on the source-bundle digest,
compilation options, target framework/reference identity, and source-island
identity. It must not use timestamps or current checkout paths.

Some fixture boundaries require the complete fixture assembly rather than a
type island. Use assembly scope when the evidence depends on module attributes,
assembly attributes, cross-type initialization, file-local identity, internals,
or another condition that crosses the containing-type boundary. The fast path
must not synthesize dependency shells or otherwise create a second version of
the fixture. Because fixture assemblies are small and already built by the
normal graph, the assembly-scope fallback is acceptable and remains cacheable.

### Selecting the comparison target

Compilation scope and comparison target are separate. A full type may contain
many methods, but each comparison names exactly one target:

```csharp
public sealed record FixtureSourceTarget(
    string FixtureId,
    FixtureBinaryIdentity Binary,
    MemberAnchor Target,
    FixtureSourceOwner SourceOwner,
    FixtureSourceSpan SourceSpan,
    SourceCompilationScope Scope);

public abstract record SourceCompilationScope
{
    public sealed record Type(FixtureTypeIdentity ContainingType) : SourceCompilationScope;
    public sealed record Assembly(string FixtureId) : SourceCompilationScope;
}
```

`FixtureTypeIdentity` names the complete containing type, including assembly,
namespace, nesting, arity, and file-local identity where applicable. It chooses
the reusable compilation boundary; it does not choose a result to compare.
`MemberAnchor` supplies the stable selector and canonical signature used to
find the corresponding method in both the fixture binary and the compiled
source island. `FixtureSourceSpan` identifies the verified authored declaration
inside the bundled document. Metadata tokens may be retained as local lookup
hints for the exact fixture binary, but they are not correspondence authority
because recompilation can assign different tokens.

For authored methods, accessors, operators, and constructors, the target is the
compiled member itself. For compiler-generated state-machine or closure
methods, the comparison target remains the authored kickoff/owner plus a typed
synthesis relationship; the harness must not pretend that a generated
`MoveNext` body has an independent original C# method.

The fast lane can consequently answer two distinct questions without ambiguity:

1. **source correspondence** — does the decompiled target match the verified
   authored member selected by `SourceSpan`?
2. **compiled fidelity** — when the decompiled target is substituted into the
   cached type/assembly island, does its emitted member selected by
   `MemberAnchor` match the fixture binary under the fidelity contract?

The fixture binary remains the original-IL witness. Recompiling unchanged
original source is a calibration check for the source island, not a replacement
for that witness. A source island must first reproduce the selected original
member before it can grade a decompiled substitution.

## Fixture populations and migration

### Built `FixtureCatalog` projects

Add source policy and bundle metadata to `FixtureDefinition`. Build the bundle
through the normal solution graph and teach `FixtureCatalog` to resolve it as an
asset. Add catalog contract tests that require every decompiler fixture to have
one of:

- a verified source bundle; or
- an explicit `NotApplicable` classification.

The gate must validate evaluated compiler inputs, linked Ladder sources,
conditional inputs, portable-PDB checksums, assembly identity, and stale-source
rejection.

### `GeneratedFixtureCatalog`

Generated definitions already own their source. Promote that source to the
same document contract when materializing the temporary project:

- give each generated document a stable `fixture://<fixture-id>/Fixture.cs`
  identity;
- emit portable PDBs and checksums;
- retain source bytes and compilation options in the run result even when the
  temporary directory is deleted;
- map every declared target to its source member during materialization;
- make missing/ambiguous target correspondence a fixture failure.

Do not copy generated source into a second catalog. The existing definition is
the source of truth.

### Test-local dynamic compilation

The current inventory finds 29 decompiler-test files using
`CSharpCompilation.Create` or `RoslynTestCompiler`. Classify each use:

1. reusable compiler-produced positive: move to a fixture project or
   `GeneratedFixtureCatalog`;
2. focused dynamic positive that needs a compilation matrix: use a shared
   materializer that returns a source bundle and target map;
3. malformed/adversarial input: declare `NotApplicable` and retain the reason;
4. test of source indexing itself: keep local source, but construct the same
   source-document contract.

After migration, a guard test rejects new anonymous compiler-produced
decompiler fixtures that do not return source evidence.

## Delivery slices

### PR 1: contracts and measured inventory

- Add source applicability, binary identity, document, compilation-option, and
  verification contracts.
- Add an inventory command/report covering built, generated, and dynamic
  fixture populations.
- Record covered, unresolved, synthesized-owner, and not-applicable counts.
- Make no Fully Raised behavior change.

### PR 2: built fixture source bundles

- Emit deterministic bundle/manifest artifacts for catalog fixture projects.
- Register the opt-in .NET fixture.
- Resolve and verify bundles through `FixtureCatalog`.
- Add tamper, stale-build, linked-source, conditional-source, and mismatched-PDB
  negative tests.

### PR 3: generated and dynamic fixture convergence

- Project `GeneratedFixtureCatalog` source through the common contracts.
- Introduce the shared source-retaining dynamic materializer.
- Migrate compiler-produced positives out of anonymous test-local compilation.
- Gate all generated targets on unique source ownership.

### PR 4: unified source correspondence

- Move reusable source indexing/correlation out of the RTS-only orchestration
  path.
- Consume the verified bundle rather than current checkout paths.
- Cover methods, constructors, accessors, operators, local functions, records,
  async/iterator kickoff methods, and synthesized owners.
- Emit structured per-target correspondence results.

This is the likely meeting point with parallel source-pipeline work. That work
can provide acquisition and verified-document contracts; this plan supplies the
fixture bundle and target-ownership producer. Integration should occur at
`VerifiedFixtureSource` plus stable `MemberAnchor`, not by sharing display text
or filesystem assumptions.

### PR 5: report integration and final gate

- Feed correspondence results into typed harness reports and the before/after
  comparison tool.
- Prefer `SourceCorrespondenceV1` over `ResidueZeroV1`.
- Generate Original, Before, After, and Fully Raised sections from keyed target
  evidence.
- Require 100% source applicability classification and 100% verified source for
  compiler-produced fixture targets.
- Remove the residue-only Fully Raised fallback for fixtures.

## Evidence and gates

The migration is complete only when automated reports prove:

| Population | Required result |
| --- | --- |
| Built decompiler fixture targets | Verified source and unique owner, or explicit not-applicable reason |
| Generated fixture targets | Verified retained source and unique owner |
| Dynamic compiler-produced positives | Shared materializer evidence or catalog migration |
| Synthesized members | Typed authored owner |
| Source-correspondence report | No `SourceUnavailable` or `TargetUnresolved` fixture targets |
| Fully Raised fixture claims | Basis is `SourceCorrespondenceV1`, never residue-only |

Required close negatives include modified source after build, wrong assembly,
wrong PDB, wrong checksum algorithm/value, duplicate member identities,
conditional-source mismatch, linked-source omission, synthesized member without
an owner, and a source-valid but textually different decompilation.

## Non-goals

- Requiring source for arbitrary third-party corpus assemblies. SourceLink and
  package-source acquisition can extend the same consumer contract later.
- Treating source text as a semantic oracle for hand-authored invalid IL.
- Reconstructing product behavior in the harness to make source comparison pass.
- Calling compile-back `Exact`, decompiler `Full`, or zero residue a source
  match.
- Defining taste-equivalence rules in the first source-backed gate.
