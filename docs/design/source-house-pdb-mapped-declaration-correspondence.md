# SourceHouse PDB-mapped declaration correspondence

## Status and scope

This document is the normative owner for SourceHouse correspondence between
one exact MethodDef and one declaration selected from checksum-verified
PDB-mapped source. It is tracked by
[#8342](https://github.com/richlander/dotnet-inspect/issues/8342) and replaces
the build-attestation contract previously tracked by
[#6584](https://github.com/richlander/dotnet-inspect/issues/6584) and
[#7859](https://github.com/richlander/dotnet-inspect/issues/7859).

The one claim is:

> Given one exact Library implementation assembly, its associated portable or
> embedded PDB, one MethodDef target, and source bytes accepted under host
> policy, SourceHouse may issue a declaration result only after the PDB maps
> that target to the source document, the bytes satisfy the PDB checksum, and
> CSharpText uniquely selects a bounded declaration from the mapped line
> evidence.

This is a presentation-correspondence claim. It does not claim that an
independent witness observed the publisher's build or that the source can
reproduce the assembly.

## Trust basis

Selecting and using a NuGet package is already the caller's package-publisher
trust decision. In common use, the inspected package is already a dependency
whose code the application loads and executes. dotnet-inspect does not execute
inspected code, and requiring a second receipt issued by the same publisher
would add no independent protection.

The meaningful product boundary is therefore structural:

- the assembly and PDB must be associated with the same exact Library;
- the MethodDef and PDB mapping must be exact;
- source acquisition must obey host network and origin policy;
- accepted source bytes must satisfy the PDB checksum;
- decoding and lexical declaration selection must remain bounded;
- inspected bytes and rendered text remain inert; and
- absence, ambiguity, uncertainty, malformed input, incomplete work, and
  operational failure remain visible.

The PDB and SourceLink data are publisher-supplied presentation metadata,
consistent with ordinary debugger source navigation. SourceHouse validates
their internal association and integrity; it does not authenticate the
publisher's semantic account of its own package.

## Contract

SourceHouse owns:

1. the exact Library implementation content and transferred operation lease;
2. portable or embedded PDB association and identity checks;
3. MethodDef-to-document mapping and mapped line evidence;
4. source candidate ordering under the selected host policy;
5. PDB checksum verification and bounded source decoding;
6. CSharpText member-declaration selection from mapped line evidence; and
7. a resource-free outcome preserving the mapping, checksum, selected
   document, declaration coordinates, attempts, work, and settlement.

The separate receipt preserves request, PDB contribution, work, and Library
lease settlement. Consumers that need the authored mapping and declaration
evidence use the completed outcome while adapting it; the receipt does not
retain source buffers or duplicate the complete authored attempt.

An available member result exposes the complete decoded source document and
CSharpText-issued member parts for the uniquely selected declaration.
`MemberTextParts.Declaration` is the exact declaration span consumed by
`CSharpAuthoredDocumentation`; DocumentationHouse does not reconstruct it.

Only MethodDefs with exact PDB line mapping are in scope for authored
documentation. Type source-document correlation remains useful SourceHouse
evidence, but it does not identify one exact type declaration span. Type
authored documentation remains unavailable until a focused owner defines that
selection contract. Bodyless members commonly have no sequence point and
remain unavailable.

## Line directives

PDB mapping is authoritative for source presentation, including a destination
introduced by `#line`. SourceHouse accepts only the exact mapped document whose
bytes satisfy its PDB checksum, then asks CSharpText to select a declaration
from the active mapped lines. CSharpText may return no declaration, ambiguity,
uncertainty, malformed input, or incomplete work; SourceHouse and
DocumentationHouse preserve that result rather than falling back to a name or
neighboring declaration.

## Failure boundaries

The following remain distinct:

- no PDB versus an associated PDB that cannot be read;
- PDB identity mismatch versus no target mapping;
- no mapped document versus source acquisition disallowed or unavailable;
- checksum rejection versus decoding or lexical failure;
- no uniquely selected declaration versus an authoritative declaration with
  no attached documentation;
- deadline or work-limit exhaustion versus operational failure; and
- Library lease rejection or incomplete settlement versus source non-success.

No terminal path returns success-shaped empty output for a missing mapping,
checksum mismatch, lexical uncertainty, or failed operation.

## Production adoption

DocumentationHouse consumes only the existing SourceHouse authored member
result and its CSharpText-issued exact declaration span. Public browser and CLI
hosts use their ordinary source capabilities; no build-observer capability or
test-only capability injection participates in production composition.

The non-packable `SourceBuildAttestation` project and
`ISourceHousePhysicalDeclarationCapability` hierarchy are retired. They could
only attest builds that explicitly cooperated with the tool and therefore
could not provide authored documentation for ordinary packages from
nuget.org.

## Evidence

PR-fast Release gates cover:

- exact assembly/PDB/MethodDef/source correspondence;
- checksum-valid SourceLink and embedded-source success;
- checksum mismatch and missing mapping;
- ambiguous or unsupported lexical selection;
- bodyless members without sequence points;
- `#line` destination handling;
- split API and implementation assemblies;
- cancellation, deadline, finite-work limits, and Library lease settlement;
  and
- public Browser/Wasm composition without a privileged injected capability.
