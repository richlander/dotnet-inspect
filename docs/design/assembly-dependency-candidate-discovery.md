# Assembly dependency candidate discovery

## Owner and claim

`DotnetInspector.Services` owns the configured dependency discovery population.
A consumer that must make its own selection can obtain that population without
losing alternatives to a simple-name-first-wins projection. Discovery is not
selection, image admission, or proof of complete reference closure.

The established resolver's probing and acquisition are the baseline.
`DiscoverCandidates` reuses those operations rather than transferring their
policy into the decompiler harness. The deliberate difference from
`ResolveAll` is preservation of candidate alternatives and path-based source
exclusion; legacy callers retain their existing behavior.

## Population and acquisition

The population contains the rows produced by the resolver's enabled package,
platform, sibling, manifest, project, and corpus discovery sources. It retains
same-name alternatives and paths reached through distinct provenance. An entry
retains its discovered full path, provenance, and available package/framework
coordinates; order is not authority to select a compiler reference.

When target exclusion is requested, this surface excludes the configured
normalized source path, not unrelated candidates with the same simple name.
Path comparison is case-insensitive on Windows and ordinal elsewhere. This is
path exclusion, not filesystem alias detection or assembly identity.

A discovered row is only a candidate to acquire. Consumers use the same
resolver's `Acquire` operation to receive owner-issued assembly registration
and provenance, or its existing non-success. A malformed image remains visible
in discovery; discovery does not silently turn its failed acquisition into a
smaller successfully admitted inventory.

Package asset selection, discovery omissions, installed-platform fallback,
and enabled-source policy retain their existing contracts. In particular,
this population is not every assembly installed on the machine, every package
version, or every candidate a later request-specific fallback could acquire.
The existing resolver instance caches its discovery result; this adds no
snapshot or filesystem-change guarantee.

`ResolveAll`, `Resolve`, and `Select` keep their existing behavior, including
their legacy target-name exclusion and binding tier policies. Returning more
discovery rows does not authorize either binding or compiler-set selection.

## Adoption and retirement

The end-to-end RTS primary-harness and legacy-retirement tracker is
[#6199](https://github.com/richlander/dotnet-inspect/issues/6199).
The current remaining path has five delivery steps:

1. Services candidate discovery, this slice
   ([#6200](https://github.com/richlander/dotnet-inspect/issues/6200)).
2. RTS frozen-reference acquisition and authored-replay migration
   ([#6103](https://github.com/richlander/dotnet-inspect/issues/6103)).
3. Native RTS cutover evidence without legacy fallback.
4. Primary fidelity-command, baseline, and CI-consumer migration.
5. Legacy reconstruction and fallback retirement.

The immediate consumer is the Services contract harness; RTS adopts this API
in step 2. The existing tools-first scope under
[#5890](https://github.com/richlander/dotnet-inspect/issues/5890) defers
CLI/browser adoption and does not create a new platform exception. Services
retains its current platform contract. This slice does not implement frozen
reference selection, RTS admission, or legacy fidelity retirement.

## Evidence

The Release `AssemblyDependencyResolverTests` cases
`DiscoverCandidates_PreservesSameNameAlternativesAndLegacySelection`,
`DiscoverCandidates_TargetExclusionPreservesOtherSameNamedPaths`, and
`DiscoverCandidates_PreservesProvenanceAndInvalidImages` cover the consumer
outcomes: same-name alternatives, equal and different image identities,
separate acquisitions, source exclusion, repeated provenance, visible invalid
images, and unchanged legacy selection before and after discovery.
