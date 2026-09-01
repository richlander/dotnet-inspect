# Nuspec structural compatibility

`DotnetInspector.Services` owns recognition and extraction of nuspec XML
structure. This document defines which `package` and `metadata` namespace forms
that parser accepts and which nearby XML shapes do not become package metadata.

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
  Services parser does not require package identity.

These distinctions avoid both permissive local-name matching and rejecting
unrelated extension content.

## Ownership boundaries

Services owns XML shape recognition, namespace relationships, field extraction,
and safe malformed/unsupported-structure failures. `HardenedXml` remains the
shared XML decoding boundary.

`PackageManifestFactsQuery` owns expected identity matching, validated
self-attested coordinate construction for direct content, typed identity
provenance, dependency contract validation, scalar/count/byte limits, and
projection into typed manifest failures. Both identity paths consume the same
single Services parse. Acquisition belongs to the host, and CLI or Browser
presentation is outside this policy. This document neither makes Services an
identity, acquisition, or presentation owner.

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

The pinned live command in `eng/package-manifest-corpus.md` is required evidence
for an acceptance-policy change; synthetic unit tests alone do not establish
real-package compatibility.
