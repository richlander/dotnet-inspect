# Member call-graph supply-chain focus

This document owns the application of the shared
[package supply-chain baseline](package-supply-chain-baseline.md) to
dependency-aware member call graphs. Implementation is tracked by
[#8334](https://github.com/richlander/dotnet-inspect/issues/8334), with the
shared-policy transfer tracked by
[#8368](https://github.com/richlander/dotnet-inspect/issues/8368).

## Claim and owner

`DotnetInspector.Queries` owns one host-neutral graph composition:

> Given exact Package ownership for every known graph node, classify the
> focused root and selected baseline Packages as connectors, classify other
> known Packages as highlighted boundaries, and preserve absent or ambiguous
> ownership as unclassified.

The composition does not change dependency traversal, Package acquisition,
Platform pruning, call-graph topology, or rendering. PackageQueries composes
the shared baseline policy with the exact package-role context. Sections
returns the selected baseline evidence and typed graph in one inspection
envelope. CLI and Browser/Wasm select product policy and render the same
result.

[Package dependency call-graph operation](package-dependency-call-graph-operation.md)
owns traversal, route realization, Package ownership, and graph execution.
[Workspace registration and call-graph focal
length](workspace-registration-and-call-graph-scope.md) owns inert Package
Prefix and ecosystem registrations. The shared baseline owner consumes those
facts and issues known-Package classification. This document consumes that
classification without redefining it.

## Baseline orientation

The shared baseline owner defines `Nothing`, `Self`, and
`SelfAndRegisteredEcosystems`, including exact root and registration membership.
This graph maps a known `Baseline` Package to a connector and a known
`IncrementalExposure` Package to a highlighted boundary.

Traversal remains inclusive. It may acquire and analyze Packages that the
baseline later classifies as connectors. Shared baseline classification never
prevents unknown boundaries from remaining visible.

## Identity and membership

Package ownership is established before baseline policy by exact live
package-role participants. A Package Prefix or ecosystem declaration never
proves ownership.

The operation request captures one exact Workspace registration revision
before route execution. Baseline classification and detached evidence consume
that revision even if the live Workspace registration set is replaced later;
they never join graph ownership to an ambient latest revision.

The completed document carries the shared detached baseline evidence so hosts
can explain the result without retaining a live Workspace.

## Platform boundary

PackageHouse remains the owner of target-applicable Platform pruning. A
dependency delegated to an exact Platform target does not become a Package
participant or a highlighted Package boundary. The detached Platform route
remains evidence for that decision.

This graph composition does not infer that an unresolved graph node belongs to
Platform from an assembly name, namespace, or pruned Package ID. A node without
exact Package ownership remains `unclassified-boundary`. A future exact
Platform participant composition can classify such nodes without changing the
shared Package-baseline contract.

Platform-baseline membership is product policy, not a security certification
or vulnerability verdict.

## Product adoption

The reusable inspection API defaults to an empty Workspace plan and the
`Nothing` baseline, preserving neutral construction.

CLI and Inspect Web explicitly choose:

```text
SelfAndRegisteredEcosystems
+ product platform Workspace plan
  (.NET Runtime, ASP.NET Core, Microsoft.Extensions)
```

The CLI additionally accepts explicit first-party Package Prefixes and all
three baseline values. Inspect Web uses the product default. Both consume the
same host-neutral graph roles and detached baseline evidence.

`Just My Code` remains a separate first-party-only analysis profile. It does
not share this subtractive baseline axis and is not implemented by relabeling
`Self`.

## Demo

For `Microsoft.Extensions.Http.Polly`, the default product baseline retains
the root Package and registered Microsoft.Extensions Packages as connectors.
The exact `Polly.Extensions.Http` Package remains outside the baseline, so
`HandleTransientHttpError` is a highlighted `boundary`.

A known `System.*` Package participant is a connector under the registered
.NET Runtime ecosystem. A target-applicable dependency delegated to Platform
is retained as a Platform route and is not materialized as a Package boundary.

## Required gates

- `Nothing` highlights a known dependency Package.
- `Self` suppresses a dependency Package matching an explicit first-party
  Package Prefix.
- `SelfAndRegisteredEcosystems` suppresses a dependency Package matching a
  registered ecosystem Package population.
- A later Workspace registration replacement cannot reinterpret an in-flight
  graph captured against an earlier registration revision.
- The real Polly scenario retains the exact Polly Package boundary under the
  product default.
- A Platform-delegated dependency remains a Platform route without a Package
  payload or highlighted Package subject.
- Missing, conflicting, or ambiguous ownership remains unclassified.
- CLI and Browser/Wasm select the product-curated default through the same
  Sections request and consume the same typed graph.

## Non-claims

This design does not:

- infer first-party ownership;
- treat every `Microsoft.*` Package as Platform;
- classify unresolved Platform graph nodes from names;
- change Package ownership or shared baseline membership;
- reduce dependency traversal or acquisition to highlighted Packages;
- make reusable Workspace construction implicitly curated; or
- combine supply-chain focus with `Just My Code`.
