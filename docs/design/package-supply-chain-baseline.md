# Package supply-chain baseline

## Status, owner, and claim

Status: **implementation contract** for
[#8368](https://github.com/richlander/dotnet-inspect/issues/8368).

The **Package Supply-Chain Baseline Policy** in
`DotnetInspector.Queries` owns:

> Given canonical root Package IDs, one selected baseline kind, and one exact
> captured Workspace registration revision, classify an already-known Package
> ID as baseline context or incremental exposure and return detached evidence
> for that classification basis.

This is a single-owner transfer from the baseline policy previously embedded
in
[member call-graph supply-chain focus](member-call-graph-supply-chain-focus.md).
That query remains the first adopter and preserves its existing graph behavior.

The policy does not establish Package ownership. A caller first proves that a
subject belongs to one exact Package, then supplies that Package ID for
classification.

## Baseline orientation

The baseline names Packages excluded from incremental-exposure highlighting:

| Baseline | Known Package behavior |
| --- | --- |
| `Nothing` | Only the exact roots are baseline context |
| `Self` | Roots and explicitly registered first-party Package Prefixes are baseline context |
| `SelfAndRegisteredEcosystems` | Roots, first-party prefixes, and Packages contributed by registered ecosystems are baseline context |

Roots are always baseline because they define the comparison or analysis
context. `Nothing` means no dependency Package is excluded.

Baseline classification is independent from acquisition and traversal. It
never authorizes a source, changes a traversal target, reduces a work budget,
removes graph connectors, or proves that a Package is trustworthy.

## Captured policy

`PackageSupplyChainBaselinePolicy` snapshots:

- one or more canonical root Package IDs;
- the selected `PackageSupplyChainBaseline`;
- top-level first-party Package Prefix registrations; and
- registered ecosystem declarations

from one exact `WorkspaceRegistrationRevision`.

The neutral `Nothing` policy may be constructed without a Workspace revision.
It retains only its roots and classifies every other known Package as exposure.

Root IDs are canonicalized and de-duplicated in caller order. Several exact
versions of one Package ID remain one Self identity. Several different root
Package IDs are independently baseline; the policy does not infer common
ownership between them.

The policy snapshots the supplied revision. Replacing live Workspace
registrations later cannot reinterpret an existing policy or its detached
evidence.

## Membership

After a caller has established Package ownership, the policy classifies a
canonical Package ID in this order:

1. a root Package ID is `Baseline`;
2. under `Nothing`, every other Package is `Exposure`;
3. a matching top-level first-party Package Prefix is `Baseline`;
4. under `Self`, every remaining Package is `Exposure`;
5. a registered ecosystem core Package is `Baseline`;
6. a registered ecosystem Package Prefix population is `Baseline`;
7. a registered ecosystem exact Package-origin Library contributes its Package
   ID as `Baseline`; and
8. every remaining Package is `Exposure`.

Matching is case-insensitive over canonical Package IDs through the existing
Package ID and Package Prefix contracts. Namespace roots, assembly names,
display labels, publisher text, and inferred company prefixes never establish
membership.

Platform-origin exact Libraries and Platform populations do not manufacture
Package membership. A composition that delegates a dependency to Platform
retains that owner-issued route instead of fabricating a Package identity for
this policy.

## Detached evidence

`PackageSupplyChainBaselineEvidence` is resource-free and retains:

- selected baseline kind;
- canonical root Package IDs;
- first-party Package Prefix spellings; and
- registered ecosystem identities.

It contains no live Workspace, registration revision, Package participant,
source authority, or classification callback. Hosts use it to explain the
selected policy, not to re-run membership from display values.

## First adopter and production path

The dependency-aware member call graph constructs one policy from its exact
focused root and captured registration revision. Known Package graph nodes use
the shared classification. Missing, conflicting, or ambiguous Package
ownership remains the call-graph query's `unclassified-boundary`; this policy
is not invoked for those nodes.

The completed call-graph Document carries the shared detached baseline
evidence. CLI and Browser/Wasm keep their existing product default:

```text
SelfAndRegisteredEcosystems
+ product platform Workspace plan
```

The dependency closure Diff tracked by
[#8365](https://github.com/richlander/dotnet-inspect/issues/8365) is the second
adopter. It will apply the same captured policy after complete closure
comparison without changing either endpoint traversal.

This owner has no rendering path. Adopters retain typed classification and
evidence through their own shared Documents. CLI call graphs continue to use
their existing output adapter, and Browser/Wasm continues to consume the same
managed call-graph projection.

## Required gates

- `Nothing` classifies roots as baseline and another known Package as exposure.
- `Self` classifies a first-party-prefix Package as baseline.
- `SelfAndRegisteredEcosystems` classifies core Packages, Package Prefix
  populations, and exact Package-origin Library populations as baseline.
- Several exact root versions with one canonical Package ID produce one
  detached root identity.
- Several distinct root Package IDs are independently baseline.
- A later Workspace registration replacement cannot change an issued policy.
- Detached evidence retains the selected kind, roots, first-party prefixes,
  and ecosystem identities.
- The existing real `Microsoft.Extensions.Http.Polly` call-graph scenario
  continues to highlight Polly while registered Microsoft.Extensions Packages
  remain connectors.
- CLI and Browser/Wasm retain their current default and render the same graph
  roles as before the transfer.

## Non-goals

This policy does not:

- prove Package ownership;
- infer first-party scope;
- treat every `Microsoft.*` Package as Platform;
- authorize package acquisition;
- change dependency traversal or Platform pruning;
- classify unresolved graph nodes;
- certify security, trust, vulnerability, license, or publisher identity;
- combine supply-chain focus with `Just My Code`; or
- define dependency closure comparison or host rendering.
