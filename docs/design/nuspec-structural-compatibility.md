# Nuspec structural compatibility

`DotnetInspector.Packages` owns recognition and extraction of nuspec XML
structure and bounded projection into package-manifest facts. This document
defines which `package` and `metadata` namespace forms that parser accepts and
which nearby XML shapes do not become package metadata.

The policy is intentionally narrower than XSD validation. Deployed manifests
use more than one historical schema placement, while this parser needs only a
stable structural boundary for the facts it extracts.

## Supported document forms

The document root must have the case-sensitive local name `package`. Its
namespace is either empty or belongs to the Microsoft nuspec namespace family:

```text
http://schemas.microsoft.com/packaging/{version}/nuspec.xsd
```

The existing parser treats the namespace-family prefix and suffix
case-insensitively and requires a non-empty `{version}`. Prefix choice is
irrelevant because XML expanded names use the namespace URI.

Package metadata, when present, is exactly one direct `metadata` child in one of
these forms:

| `package` namespace | `metadata` namespace | Meaning |
| --- | --- | --- |
| empty | empty | namespace-free manifest |
| Microsoft nuspec `N` | the same `N` | root-schema manifest |
| empty | Microsoft nuspec `N` | legacy metadata-schema manifest |

The selected metadata namespace determines the reported manifest version.
Namespace-free metadata reports `nuspec`; a Microsoft namespace reports its
`{version}` token.

The real-manifest corpus records deployed examples of root-schema and legacy
metadata-schema placement. Its live verifier is the compatibility check before
this matrix is tightened; see
[`eng/package-manifest-corpus.md`](../../eng/package-manifest-corpus.md).

## Close structural cases

- A root with another local name or a foreign namespace is rejected.
- Microsoft-nuspec metadata under a namespaced root must use exactly the root
  namespace. Namespace-free or differently versioned metadata there is
  rejected rather than reinterpreted.
- More than one compatible direct `metadata` child is rejected, including a
  namespace-free and legacy-schema pair under a namespace-free root.
- A foreign-namespace direct `metadata` child is an extension-shaped sibling,
  not package metadata. It is ignored and does not shadow one compatible
  `metadata` child.
- A nested `metadata` element is not a direct package child and is ignored.
- Metadata fields are read only as direct children in the selected metadata
  namespace. Foreign or nested lookalikes are not package fields.
- No direct compatible `metadata` child produces an empty `NuspecData`; the
  package parser does not require package identity.

These distinctions avoid both permissive local-name matching and rejecting
unrelated extension content.

## Framework-reference facts

`PackageManifestFactsProjection` retains every direct
`frameworkReferences/group` occurrence in manifest source order. Each group
retains its required `targetFramework` source spelling and canonical NuGet
framework identity. Each direct `frameworkReference` retains its required
non-empty `name` source spelling and a group-local semantic identity using
NuGet's ordinal-ignore-case comparer.

The semantic reference list contains one identity per case-insensitive name,
using the first source spelling. The occurrence list remains complete and
associates case-only duplicates with that same identity. Explicitly empty
groups remain groups. An absent `frameworkReferences` element is successful,
complete empty framework-reference evidence.

Invalid or missing group targets, invalid or missing reference names, and the
framework-reference group or reference limits fail the complete section
without producing a partial group list. That section failure remains inside
otherwise valid `PackageManifestFacts`; it does not erase identity,
dependencies, or other manifest facts. Malformed XML, unsupported document
shape, invalid manifest identity, and whole-manifest limits remain outer
`PackageManifestFactsResult.Failed` outcomes.

The section admits at most 1,024 groups and 4,096 reference occurrences.
Targets and names share the manifest scalar limit of 32,768 UTF-16 code units.
This owner does not select a group for a requested target, associate facts
with a PackageHouse settlement, or map a framework name to platform evidence.

## Ownership boundaries

Packages owns XML shape recognition, namespace relationships, field
extraction, and safe malformed/unsupported-structure failures. `HardenedXml`
remains the shared XML decoding boundary.

`PackageManifestFactsProjection` owns expected identity matching, validated
self-attested coordinate construction for direct content, typed identity
provenance, dependency and framework-reference validation,
scalar/count/byte limits, and projection into typed manifest and section
failures. `PackageManifestFactsQuery` is the network-free query facade over
that package-owned projection. Both identity paths consume the same single
package parse. Self-attested version construction normalizes surrounding XML
whitespace consistently with expected-coordinate version matching; package ID
whitespace remains invalid in both paths. Acquisition belongs to the host, and
CLI or Browser presentation is outside this policy. This document does not
make Packages a presentation owner.

Missing `metadata`, `id`, or `version` is therefore not a structural parser
error. A consuming query may reject the resulting incomplete facts according
to its own contract.

## Gates

`NuspecParserTests.ParseContent_SupportedPackageAndMetadataNamespaceFormsAreAccepted`
gates the three supported namespace placements.
`ParseContent_IncompatibleNuspecMetadataNamespaceIsRejected`,
`ParseContent_DuplicateCompatibleMetadataIsRejected`,
`ParseContent_ForeignDirectMetadataIsNotPackageMetadata`,
`ParseContent_NestedMetadataIsNotPackageMetadata`, and
`ParseContent_ForeignMetadataSiblingDoesNotShadowPackageMetadata` gate the
direct-child and namespace boundary.
`ParseContent_InvalidDocumentRootIsRejected` gates the root contract.
`PackageManifestFactsQueryTests.Execute_ReportsIncompatibleMetadataNamespaceAsUnsupportedDocumentShape`
gates projection of the tightened Services boundary into the existing
content-free typed query failure.
`ExecuteSelfAttested_ProjectsEquivalentFactsWithTypedProvenance` gates parity
between expected-coordinate and direct-content projection.
`ExecuteSelfAttested_RejectsInvalidIdentity`,
`ExecuteSelfAttested_RejectsMissingIdentity`,
`ExecuteSelfAttested_EnforcesIdentityScalarLimit`, and
`ExecuteSelfAttested_EnforcesManifestByteLimit` gate the self-attested identity
and byte boundary. `ExecuteSelfAttested_RejectsManifestBeyondDecodedCharacterLimit`,
`ExecuteSelfAttested_RejectsMalformedXml`, and
`ExecuteSelfAttested_PreservesHostileDescriptionAsInertText` gate its shared
parse, decoded-text, and inert-text boundaries.

`MicrosoftAzureSignalR_PreservesNonEmptyAndEmptyGroups` gates the real
`Microsoft.Azure.SignalR@1.33.1` non-empty `net8.0` and explicit empty
`netstandard2.0` groups.
`CaseOnlyDuplicates_PreserveOccurrencesAndOneSemanticIdentity` gates complete
source evidence and NuGet-compatible duplicate identity.
`InvalidFrameworkSection_PreservesIndependentManifestFacts`,
`FrameworkReferenceGroupLimit_FailsOnlyTheSection`, and
`FrameworkReferenceCountLimit_FailsOnlyTheSection` gate section-scoped
failure and non-partial projection.
`ExecuteAsync_UsesPackageOwnedFrameworkReferenceFacts` and
`QueryFacade_UsesPackageOwnedFrameworkReferenceProjection` gate equivalent
archive and query-facade consumption of the package-owned facts.

The pinned live command in `eng/package-manifest-corpus.md` is required evidence
for an acceptance-policy change; synthetic unit tests alone do not establish
real-package compatibility.
