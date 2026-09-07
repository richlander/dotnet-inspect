# Assembly dependency candidate inventory

## Owner and purpose

`DotnetInspector.Services/AssemblyDependencyResolver` owns this contract.
It supplies explicit discovery evidence before a consumer chooses compiler
references. It does not own that selection.

[#6201](https://github.com/richlander/dotnet-inspect/issues/6201) implements
this producer prerequisite for ReturnToSender migration
[#6103](https://github.com/richlander/dotnet-inspect/issues/6103).
The existing [Services/tools boundary](fact-planned-compile-back-harness.md#assembly-and-package-resolution-service)
remains unchanged. Metadata owns [descriptor classification](assembly-inspection-query.md),
acquisition registrations and image identity. Services consumes those results,
not a second PE classifier.

The ordinary caller supplies resolver options and requests an explicit capture.
Inputs include the discovery documents and assembly files reached through the
enabled tiers. Supporting owners and cooperating callers are trusted; the
repository's existing untrusted-data boundary applies to input content.

## Capture boundary

The inventory covers every DLL path emitted by the resolver's enabled,
owner-selected discovery tiers, before filename coalescing and filename-wide
target exclusion. It preserves repeated discovery entries and their
provenance. Once a tier emits a candidate path, inability to acquire that
candidate is evidence, not permission to remove its row.

This boundary inherits existing package-version, asset-directory, target-
framework and optional-tier choices. It does not enumerate alternative
package versions, every compatible TFM, or all framework installations.
Ordinary absence of an optional discovery source is not a failed capture.
An actual discovery or document-reading failure is not ordinary absence.
Missing or wrong-kind required discovery-document structure and rejected
declared asset paths are failures, not successful empty discovery. This is
not full schema validation: empty collections, optional asset groups and
the inherited package/TFM choices retain their existing meaning.

Request-driven installed-platform fallback is outside this inventory.
`Select` and the tools [explicit platform policy](csharp-member-recompilation.md#explicit-platform-compatibility-policy)
separately supply their request selections and selected P/A acquisitions.
Unselected discovery entries are not thereby shadows, identity-rejected
candidates, or eligible alternatives for an arbitrary binding request.
The inventory is neither a complete dependency closure nor a binding-domain
receipt.

## Evidence and outcomes

Capture returns an immutable, policy-version-associated result that
distinguishes successful capture from failure-bearing, explicitly partial
evidence. Each emitted row retains its discovery entry, acquisition
provenance, target-input role and acquisition outcome.

`CaptureDiscoveryInventory` returns `AssemblyDependencyDiscoveryResult`.
Its `Captured` arm exposes `Entries`; its `Failed` arm exposes
`PartialEntries` and `DiscoveryFailures`. Both carry the resolver's typed
`Version`. Each entry's `Acquisition` distinguishes `Acquired`,
`Descriptorless`, `Rejected` and `Unavailable`.

Managed assembly readiness and descriptorless exclusion use Metadata's
classification. Valid descriptorless inputs are not malformed managed
assemblies and are not compiler-reference candidates. A recognized malformed
PE/CLR image, unusable managed identity, unreadable emitted path or exhausted
acquisition budget remains a visible failure. Services must not inspect
exception text or independently decode metadata to reconstruct classification.

A successful capture requires that discovery completed within the stated
boundary and that every emitted row was acquired or positively classified as
descriptorless. A rejected acquisition or discovery failure cannot produce
that success variant. Failure may retain already-observed rows and the
rejected row for diagnostics, but it must not advertise those rows as a
complete inventory. Cancellation and caller-contract exceptions retain their
ordinary exception semantics rather than becoming successful empty output.

The existing descriptor cache is the acquisition authority. Reusing a
discovery entry preserves its canonical registration. Distinct acquisitions
remain distinct, including different provenance for one path, even if their
bytes are shared. Capture does not grant platform authority or correspondence.
Its Services policy version is a typed association, not a displayed string
or substitute artifact-generation identity.

## Target input and ordering

Services identifies which discovery occurrences belong to its configured
target input. The target's ordinary sibling-provenance occurrence retains
that role even though it has a different acquisition registration from
`AcquireTargetAssembly`. A distinct same-filename candidate is not removed
or assigned the target role merely because its filename matches.

This is an input-role association, not a same-file proof, cross-path
correspondence claim, or permission to coalesce registrations. Consumers use
the supplied role rather than reconstructing it from names, hashes or display
paths. Symlink, reparse-point and same-machine interference defenses remain
outside this contract.

Discovery evidence order and binding order are separate concerns. Capture
must not sort or replace the underlying candidate sequence used by legacy
selection. `ResolveAll` retains its existing projection; ordinary `Select`
retains its existing precedence and identity policy. Neither API is relabeled
as an undiscarded inventory.

Capture is explicit additional acquisition work. Retention and resource
limits remain those of the resolver's existing snapshot options; a larger
inventory can exhaust that budget and must report the exhaustion. An immutable
inventory collection does not by itself promise retained image bytes when
snapshotting is disabled.

## Consumers and adoption

The immediate consumer is the Services contract suite. The planned
production consumer is RTS in #6103, which will admit the supplied acquisitions
to its artifact owner, obtain owner digests and perform its tools-owned
selection. That composition must preserve the distinction between discovery
rows and request-selected fallback images.

The user approved this focused prerequisite while resuming the tools-first
migration after #6133. The frozen-reference adoption path has four steps:

1. Exact frozen inventory, set and scoped API: #6006, landed.
2. Explicit platform compatibility policy: #6133, landed.
3. Services discovery inventory: #6201, this producer slice.
4. RTS consumption and retirement of its competing first-name-wins reference
   paths: #6103.

These are prerequisites within #5890's second decoder-adoption step. The
approved tools-first scope continues to defer CLI/browser production adoption.
This slice does not migrate RTS, define replay lifetime, adopt signature/body
closure, or issue final compile-back admission receipts.

The surface is typed evidence, not a rendering domain. It introduces no
host-specific output strategy; any later presentation belongs to its consumer.

## Evidence

The existing nullable/deduplicated API and owner selection cases are the
compatibility baseline, not the new inventory's completeness oracle. Focused
Release cases must exercise same-name/version-skewed entries, acquisition
registration reuse and distinction, target-input roles, Metadata's
descriptorless/rejected distinction, visible discovery/acquisition failure,
budget exhaustion and unchanged legacy selection.

`AssemblyDependencyResolverTests.CaptureDiscoveryInventory_*` gates the
capture guarantees in Release. The existing `AssemblyDependencyResolverTests`,
`ProjectAssetsParserTests` and `NuspecParserTests` cases gate the legacy
projections and shared parsing behavior. Run the focused set with:

```sh
dotnet run --project src/DotnetInspector.Services.Tests -c Release -- \
  --filter-class \
  DotnetInspector.Services.Tests.AssemblyDependencyResolverTests \
  DotnetInspector.Services.Tests.ProjectAssetsParserTests \
  DotnetInspector.Services.Tests.NuspecParserTests
```

The public-API demo contrasts one legacy projected dependency with three
captured rows: the source and two distinct same-filename candidates. Malformed
JSON and invalid required discovery structure both yield `Failed` with
explicitly partial rows rather than a smaller successful inventory.
