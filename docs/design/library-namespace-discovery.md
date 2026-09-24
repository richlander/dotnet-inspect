# Library namespace discovery

## Status and owner

This document owns namesake-Library discovery for one exact namespace. Issue
[#8474](https://github.com/richlander/dotnet-inspect/issues/8474) tracks its
focused production adoption in Router, Find, and Spotlight.

The owner supplies three host-neutral decisions:

- proper dotted Library-name candidates are tried longest first; and
- a package-space candidate is a hit only when the existing Library Type
  population returns at least one public definition or forwarder from the
  exact ordinal namespace; and
- a Platform candidate is a hit only when the PlatformHouse-derived complete
  Type catalog contains the same public exact-namespace evidence.

[Library inspection documents and
populations](library-inspection-document.md#population-identity) remains the
normative owner of package-space namespace membership, declaration membership,
bounds, continuation, and failure. Platform namespace discovery consumes the
detached catalog derived from the PlatformHouse's authoritative complete
population. Neither path derives namespace membership from rendered Type
names.

## Candidate contract

For `System.Text.Json.Nodes`, namesake candidates are:

```text
System.Text.Json
System.Text
```

Candidates are proper prefixes, ordered from longest to shortest. A
single-segment prefix such as `System` is not a Library namesake candidate;
this preserves established broad namespace browsing for inputs such as
`System.Text`. Empty segments and leading or trailing dots produce no namesake
candidates.

Candidate spelling is source selection, not Library identity. A host must
resolve each candidate through an authorized source owner before requesting
namespace evidence.

## Exact namespace probe

The package-space probe requests one bounded Type row with:

- public accessibility;
- definitions and forwarders;
- every definition kind;
- metadata ordering; and
- ordinal exact namespace matching.

One returned row confirms the candidate. An empty complete row segment is a
miss. Unavailable, rejected, incomplete, or failed Library outcomes remain
visible non-success and do not become a miss.

The probe is existence evidence only. Find requests and drains the ordinary
Library Type Rows population after a hit when it needs result rows.

The Platform probe reuses the already completed Platform Type catalog. It
matches public definitions and forwarders by ordinal exact namespace and exact
namesake assembly identity, retaining the catalog's family, target framework,
version, and population role. It performs no second platform-pack acquisition.

## Envelope and evidence

The host-neutral Platform operation returns an
`InspectionEnvelope<PlatformNamespaceDiscoveryOutcome>`. Namesake selection is
a fixed bounded composition step rather than a user-selectable row query, so it
does not define another QuerySpace. Find drains matching Type rows through the
existing Library population QuerySpace.

A host flow that performs network acquisition additionally exposes a Debug
`EvidenceInspectionEnvelope<TContent, TEvidence>` containing its ordered
PackageHouse acquisitions and transfer receipts. Evidence enrichment does not
change ordinary output. Router, Find, and Spotlight adopt that enrichment after
the PackageHouse transfer evidence is available through their retained
platform or package result.

## Host policies

The shared contract does not select sources or multiplicity:

- Router preserves exact Type and Member precedence, requests Platform
  candidates from the same target-bound catalog, and accepts the first
  confirmed namesake hit.
- Find preserves direct Type matches and may retain more than one confirmed
  source hit.
- Spotlight accepts the first namesake tier while preserving distinct eligible
  source observations at that tier.

Platform catalog names are eligible Platform requests in all three hosts.
Prune-inventory names are eligible in Platform and package space for Find and
Spotlight, while Router remains Platform-only. Equal Platform and package
observations do not collapse.

## Non-goals

- Namespace suffix discovery.
- Descendant matching during namesake discovery. An already selected Library
  may explicitly request its named namespace and descendants through the
  Library population contract.
- Package availability inference from missing prune data.
- Rendering or tree-versus-table defaults.
- Progressive result publication.
- Source acquisition or NuGetFetch internals.
